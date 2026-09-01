using System.Diagnostics;

namespace XbPreview.Host;

internal enum FollowState
{
    Disabled,
    WaitingForZoom,
    Rearming,
    InsideComfortZone,
    Following,
    CursorOutsideMonitor,
    ErrorFallback,
}

internal readonly record struct ComfortZoneFollowStep(
    FollowState State,
    bool FollowEnabled,
    CameraCursorObservation? Cursor,
    CameraPoint CurrentCenter,
    CameraPoint DesiredCenter,
    CameraPoint OutputCenter,
    ComfortZoneBounds Bounds,
    bool OutsideLeft,
    bool OutsideRight,
    bool OutsideTop,
    bool OutsideBottom,
    bool FollowActiveX,
    bool FollowActiveY,
    double VelocityX,
    double VelocityY,
    double DeltaSeconds,
    bool ClampX,
    bool ClampY,
    int FollowErrorCount,
    string Event,
    string? Error,
    bool ShouldApplyCenter,
    bool ShouldLog);

internal sealed class ComfortZoneTracker
{
    private readonly object _gate = new();
    private readonly CameraFollowSmoother _smoother = new();
    private readonly long _frequency;
    private bool _enabled;
    private bool _requireInsideBeforeFollow;
    private FollowState _state;
    private CameraMode _lastCameraMode = CameraMode.Wide;
    private string? _pendingEvent;
    private long _lastLogQpc;
    private int _errorCount;

    internal ComfortZoneTracker(bool enabled, long frequency = 0)
    {
        _enabled = enabled;
        _state = enabled ? FollowState.WaitingForZoom : FollowState.Disabled;
        _frequency = frequency > 0 ? frequency : Stopwatch.Frequency;
        _pendingEvent = enabled ? "follow-enabled" : "follow-disabled";
    }

    internal bool Enabled
    {
        get
        {
            lock (_gate)
            {
                return _enabled;
            }
        }
    }

    internal void SetEnabled(bool enabled)
    {
        lock (_gate)
        {
            if (_enabled == enabled)
            {
                return;
            }

            _enabled = enabled;
            _smoother.Reset();
            _requireInsideBeforeFollow =
                enabled && _lastCameraMode == CameraMode.ZoomedFixed;
            _state = enabled
                ? FollowState.WaitingForZoom
                : FollowState.Disabled;
            _pendingEvent = enabled
                ? "follow-enabled"
                : "follow-disabled";
        }
    }

    internal bool ShouldReadCursor(CameraState cameraState)
    {
        lock (_gate)
        {
            return _enabled &&
                cameraState.Enabled &&
                cameraState.Mode == CameraMode.ZoomedFixed &&
                cameraState.Zoom >
                    CameraSettings.WideZoom +
                    ComfortZoneSettings.BoundaryEpsilon;
        }
    }

    internal ComfortZoneFollowStep Update(
        CameraState cameraState,
        CameraCursorObservation? cursor,
        double deltaSeconds,
        long nowQpc)
    {
        lock (_gate)
        {
            CameraPoint current = new(
                cameraState.CenterX,
                cameraState.CenterY);
            _lastCameraMode = cameraState.Mode;

            if (!_enabled)
            {
                _smoother.Reset();
                return CreateStationaryStep(
                    cameraState,
                    cursor,
                    current,
                    deltaSeconds,
                    nowQpc,
                    FollowState.Disabled);
            }

            if (!cameraState.IsValid)
            {
                return EnterErrorUnsafe(
                    cameraState,
                    cursor,
                    deltaSeconds,
                    nowQpc,
                    "Camera state is invalid before follow.");
            }

            if (!cameraState.Enabled ||
                cameraState.Mode != CameraMode.ZoomedFixed ||
                cameraState.Zoom <=
                    CameraSettings.WideZoom +
                    ComfortZoneSettings.BoundaryEpsilon)
            {
                _smoother.Reset();
                _requireInsideBeforeFollow = false;
                return CreateStationaryStep(
                    cameraState,
                    cursor,
                    current,
                    deltaSeconds,
                    nowQpc,
                    FollowState.WaitingForZoom);
            }

            // A concurrent UI enable can occur after ShouldReadCursor. Wait
            // one update instead of treating the absent observation as a
            // GetCursorPos failure.
            if (cursor is null)
            {
                _smoother.Reset();
                return CreateStationaryStep(
                    cameraState,
                    null,
                    current,
                    deltaSeconds,
                    nowQpc,
                    FollowState.Rearming,
                    eventOverride: "cursor-sample-pending");
            }

            CameraCursorObservation observation = cursor.Value;
            if (!observation.GetCursorPosResult)
            {
                return EnterErrorUnsafe(
                    cameraState,
                    observation,
                    deltaSeconds,
                    nowQpc,
                    observation.Error ?? "GetCursorPos failed.");
            }
            if (!observation.InsidePrimaryMonitor)
            {
                _smoother.Reset();
                return CreateStationaryStep(
                    cameraState,
                    observation,
                    current,
                    deltaSeconds,
                    nowQpc,
                    FollowState.CursorOutsideMonitor);
            }

            ComfortZoneCalculation calculation =
                ComfortZoneMath.Calculate(
                    cameraState.Zoom,
                    current,
                    observation.Normalized);
            if (!calculation.IsValid)
            {
                return EnterErrorUnsafe(
                    cameraState,
                    observation,
                    deltaSeconds,
                    nowQpc,
                    "Comfort-zone calculation produced an invalid state.");
            }

            if (_requireInsideBeforeFollow)
            {
                _smoother.Reset();
                if (!calculation.FollowActiveX &&
                    !calculation.FollowActiveY)
                {
                    _requireInsideBeforeFollow = false;
                    return CreateStep(
                        cameraState,
                        observation,
                        current,
                        current,
                        current,
                        calculation,
                        deltaSeconds,
                        nowQpc,
                        FollowState.InsideComfortZone,
                        0.0,
                        0.0,
                        false,
                        false,
                        null);
                }

                return CreateStep(
                    cameraState,
                    observation,
                    current,
                    calculation.DesiredCenter,
                    current,
                    calculation,
                    deltaSeconds,
                    nowQpc,
                    FollowState.Rearming,
                    0.0,
                    0.0,
                    false,
                    false,
                    null);
            }

            if (!calculation.FollowActiveX &&
                !calculation.FollowActiveY)
            {
                _smoother.Reset();
                return CreateStep(
                    cameraState,
                    observation,
                    current,
                    current,
                    current,
                    calculation,
                    deltaSeconds,
                    nowQpc,
                    FollowState.InsideComfortZone,
                    0.0,
                    0.0,
                    false,
                    false,
                    null);
            }

            CameraFollowSmoothResult smooth = _smoother.Step(
                cameraState.Zoom,
                current,
                calculation.DesiredCenter,
                calculation.FollowActiveX,
                calculation.FollowActiveY,
                deltaSeconds);
            bool changed =
                Math.Abs(smooth.Center.X - current.X) > 1e-12 ||
                Math.Abs(smooth.Center.Y - current.Y) > 1e-12;
            return CreateStep(
                cameraState,
                observation,
                current,
                calculation.DesiredCenter,
                smooth.Center,
                calculation,
                deltaSeconds,
                nowQpc,
                FollowState.Following,
                smooth.VelocityX,
                smooth.VelocityY,
                smooth.ClampX,
                smooth.ClampY,
                null,
                changed);
        }
    }

    internal ComfortZoneFollowStep ForceError(
        CameraState cameraState,
        double deltaSeconds,
        long nowQpc,
        string error)
    {
        lock (_gate)
        {
            return EnterErrorUnsafe(
                cameraState,
                null,
                deltaSeconds,
                nowQpc,
                error);
        }
    }

    internal void ResetAfterCameraStateRace()
    {
        lock (_gate)
        {
            _smoother.Reset();
            _state = FollowState.WaitingForZoom;
            _pendingEvent = "camera-state-race-retry";
        }
    }

    private ComfortZoneFollowStep EnterErrorUnsafe(
        CameraState cameraState,
        CameraCursorObservation? cursor,
        double deltaSeconds,
        long nowQpc,
        string error)
    {
        _enabled = false;
        _requireInsideBeforeFollow = false;
        _smoother.Reset();
        _errorCount++;
        return CreateStationaryStep(
            cameraState,
            cursor,
            new CameraPoint(cameraState.CenterX, cameraState.CenterY),
            deltaSeconds,
            nowQpc,
            FollowState.ErrorFallback,
            error,
            "follow-error-fallback");
    }

    private ComfortZoneFollowStep CreateStationaryStep(
        CameraState cameraState,
        CameraCursorObservation? cursor,
        CameraPoint current,
        double deltaSeconds,
        long nowQpc,
        FollowState state,
        string? error = null,
        string? eventOverride = null)
    {
        ComfortZoneCalculation calculation = ComfortZoneMath.Calculate(
            cameraState.Zoom,
            current,
            cursor?.Normalized ?? current);
        return CreateStep(
            cameraState,
            cursor,
            current,
            current,
            current,
            calculation,
            deltaSeconds,
            nowQpc,
            state,
            0.0,
            0.0,
            false,
            false,
            error,
            false,
            eventOverride);
    }

    private ComfortZoneFollowStep CreateStep(
        CameraState cameraState,
        CameraCursorObservation? cursor,
        CameraPoint current,
        CameraPoint desired,
        CameraPoint output,
        ComfortZoneCalculation calculation,
        double deltaSeconds,
        long nowQpc,
        FollowState state,
        double velocityX,
        double velocityY,
        bool clampX,
        bool clampY,
        string? error,
        bool shouldApplyCenter = false,
        string? eventOverride = null)
    {
        string eventName = eventOverride ??
            _pendingEvent ??
            (state != _state
                ? $"follow-state-{state}"
                : "tick");
        _pendingEvent = null;
        _state = state;
        bool important =
            eventName != "tick" ||
            cameraState.Event != "tick" ||
            state == FollowState.Following ||
            error is not null;
        bool stableDue =
            nowQpc - _lastLogQpc >= _frequency;
        bool shouldLog = important || stableDue;
        if (shouldLog)
        {
            _lastLogQpc = nowQpc;
        }

        return new ComfortZoneFollowStep(
            state,
            _enabled,
            cursor,
            current,
            desired,
            output,
            calculation.Bounds,
            calculation.OutsideLeft,
            calculation.OutsideRight,
            calculation.OutsideTop,
            calculation.OutsideBottom,
            calculation.FollowActiveX,
            calculation.FollowActiveY,
            velocityX,
            velocityY,
            deltaSeconds,
            clampX || calculation.ClampX,
            clampY || calculation.ClampY,
            _errorCount,
            eventName,
            error,
            shouldApplyCenter,
            shouldLog);
    }
}
