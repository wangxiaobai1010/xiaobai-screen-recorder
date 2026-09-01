using System.Diagnostics;

namespace XbPreview.Host;

internal sealed class CameraUpdateService : IPreviewCameraUpdateService
{
    private readonly FixedTargetCameraController _controller;
    private readonly IPreviewNativeSession _session;
    private readonly CameraDiagnosticLogger _logger;
    private readonly ComfortZoneDiagnosticLogger _followLogger;
    private readonly ComfortZoneTracker _followTracker;
    private readonly Func<CameraCursorObservation> _cursorReader;
    private readonly CancellationTokenSource _cancellation = new();
    private Task? _loop;
    private long _lastStableLogQpc;
    private long _lastFollowTickQpc;

    internal CameraUpdateService(
        FixedTargetCameraController controller,
        IPreviewNativeSession session,
        CameraDiagnosticLogger logger,
        ComfortZoneDiagnosticLogger followLogger,
        bool followEnabled,
        Func<CameraCursorObservation>? cursorReader = null)
    {
        _controller = controller;
        _session = session;
        _logger = logger;
        _followLogger = followLogger;
        _followTracker = new ComfortZoneTracker(followEnabled);
        _cursorReader = cursorReader ??
            CameraCursorTarget.ReadPrimaryMonitorObservation;
    }

    public event Action<CameraState, NativeMethods.Result>? StatePublished;

    public event Action<ComfortZoneFollowStep>? FollowStatePublished;

    public void SetFollowEnabled(bool enabled) =>
        _followTracker.SetEnabled(enabled);

    public void Start()
    {
        _loop ??= Task.Run(RunAsync);
    }

    public async ValueTask StopAsync()
    {
        _cancellation.Cancel();
        if (_loop is not null)
        {
            try
            {
                await _loop.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }
    }

    private async Task RunAsync()
    {
        using PeriodicTimer timer = new(
            TimeSpan.FromSeconds(1.0 / CameraSettings.UpdateRateHz));
        while (await timer.WaitForNextTickAsync(_cancellation.Token).ConfigureAwait(false))
        {
            long nowQpc = Stopwatch.GetTimestamp();
            try
            {
                CameraState state = _controller.Snapshot(nowQpc);
                double deltaSeconds = _lastFollowTickQpc == 0
                    ? 0.0
                    : Math.Max(
                        0.0,
                        (nowQpc - _lastFollowTickQpc) /
                            (double)Stopwatch.Frequency);
                _lastFollowTickQpc = nowQpc;
                ComfortZoneFollowStep follow;
                try
                {
                    CameraCursorObservation? cursor =
                        _followTracker.ShouldReadCursor(state)
                            ? _cursorReader()
                            : null;
                    follow = _followTracker.Update(
                        state,
                        cursor,
                        deltaSeconds,
                        nowQpc);
                    if (follow.ShouldApplyCenter)
                    {
                        if (_controller.TrySetZoomedCenter(
                            state,
                            follow.OutputCenter,
                            out CameraState updatedState))
                        {
                            state = updatedState;
                        }
                        else
                        {
                            // F9 can race this background tick. Discard the
                            // stale follow output and resnapshot the P1a state
                            // so ZoomingOut starts at its current true center.
                            _followTracker.ResetAfterCameraStateRace();
                            state = _controller.Snapshot(nowQpc);
                            follow = _followTracker.Update(
                                state,
                                null,
                                0.0,
                                nowQpc);
                        }
                    }
                }
                catch (Exception error)
                {
                    // Follow owns neither capture nor the P1a state machine.
                    // Unexpected follow faults disable only follow and retain
                    // the last valid fixed-camera state.
                    follow = _followTracker.ForceError(
                        state,
                        deltaSeconds,
                        nowQpc,
                        $"follow-isolated-error: {error.Message}");
                }
                Publish(state, follow);
            }
            catch (Exception error)
            {
                // Camera/log/UI failures are isolated from WGC. Submit one
                // explicit full-view state and keep the update loop alive.
                _controller.SetEnabled(false, nowQpc);
                CameraState fallback = _controller.Snapshot(nowQpc);
                NativeMethods.Result fallbackResult;
                try
                {
                    fallbackResult = _session.SetCameraState(fallback);
                }
                catch
                {
                    fallbackResult = NativeMethods.Result.NativeFailure;
                }
                try
                {
                    _logger.Write(
                        fallback,
                        fallbackResult,
                        detail: $"camera-loop-error-fallback: {error.Message}");
                }
                catch
                {
                }
                ComfortZoneFollowStep followFallback =
                    _followTracker.ForceError(
                        fallback,
                        0.0,
                        nowQpc,
                        $"camera-loop-error-fallback: {error.Message}");
                _followLogger.TryWrite(
                    fallback,
                    followFallback,
                    fallbackResult,
                    null,
                    error.Message);
                Notify(fallback, fallbackResult, followFallback);
            }
        }
    }

    private void Publish(
        CameraState state,
        ComfortZoneFollowStep follow)
    {
        NativeMethods.Result result = _session.SetCameraState(state);
        string? detail = result == NativeMethods.Result.Ok
            ? null
            : _session.GetLastError();
        bool transition = state.Mode is CameraMode.ZoomingIn or CameraMode.ZoomingOut;
        bool eventRecord = state.Event != "tick";
        bool stableSummaryDue =
            state.TimestampQpc - _lastStableLogQpc >= Stopwatch.Frequency;
        bool cameraLogDue =
            transition ||
            eventRecord ||
            stableSummaryDue ||
            result != NativeMethods.Result.Ok;
        NativeMethods.PreviewStats? stats = null;
        if (cameraLogDue || follow.ShouldLog)
        {
            try
            {
                stats = _session.GetStats();
            }
            catch (Exception error)
            {
                detail = string.IsNullOrEmpty(detail)
                    ? $"GetStats: {error.Message}"
                    : $"{detail}; GetStats: {error.Message}";
            }
        }
        if (cameraLogDue)
        {
            _logger.Write(state, result, stats, detail);
            if (!transition)
            {
                _lastStableLogQpc = state.TimestampQpc;
            }
        }
        if (follow.ShouldLog)
        {
            _followLogger.TryWrite(
                state,
                follow,
                result,
                stats,
                detail);
        }
        Notify(state, result, follow);
    }

    private void Notify(
        CameraState state,
        NativeMethods.Result result,
        ComfortZoneFollowStep follow)
    {
        try
        {
            StatePublished?.Invoke(state, result);
            FollowStatePublished?.Invoke(follow);
        }
        catch
        {
            // Status UI is observational and cannot own camera/capture health.
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _cancellation.Dispose();
    }
}
