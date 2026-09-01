using System.Diagnostics;
using System.Text.RegularExpressions;

namespace XbPreview.Host;

internal sealed class PreviewLifecycleController : IAsyncDisposable
{
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly object _stateGate = new();
    private readonly object _resizeGate = new();
    private readonly Func<IPreviewNativeSession> _sessionFactory;
    private readonly Func<
        IPreviewNativeSession,
        bool,
        IPreviewCameraUpdateService> _cameraServiceFactory;
    private readonly FixedTargetCameraController _cameraController;
    private readonly Action<bool> _setPreviewAvailable;
    private readonly Action<
        CameraState,
        NativeMethods.Result,
        string?>? _writeCameraLog;
    private readonly Action<ManagedStartupDiagnosticEvent>?
        _writeStartupDiagnostic;
    private readonly SynchronizationContext? _notificationContext;
    private IPreviewNativeSession? _session;
    private RecordingController? _recordingController;
    private IPreviewCameraUpdateService? _cameraUpdates;
    private PreviewLifecycleState _state =
        PreviewLifecycleState.NotInitialized;
    private string? _lastError;
    private volatile bool _nativeMayNeedStop;
    private bool _sessionDisposed;
    private int _closingRequested;
    private int _pendingWidth;
    private int _pendingHeight;
    private long _resizeVersion;
    private long _appliedResizeVersion;
    private SessionGeometry? _desiredGeometry;
    private ulong _desiredGeometryRevision;
    private ulong _configuredGeometryRevision;
    private SessionGeometry? _currentGeometry;
    private ulong _currentGeometryRevision;
    private CaptureRangeMode _currentRangeMode =
        CaptureRangeMode.FullScreen;
    private CaptureTarget _currentCaptureTarget = CaptureTarget.FullScreen;
    private PreviewRuntimeSettings _currentSettings;
    private CancellationTokenSource? _selectionCancellation;
    private ulong _nextGeometryRevision = 1;
    private double _lastEngineStopDurationMs;
    private double _lastLifecycleCloseDurationMs;
    private int _startAttemptNumber;
    private string? _activeStartupAttemptId;

    internal PreviewLifecycleController(
        Func<IPreviewNativeSession> sessionFactory,
        Func<
            IPreviewNativeSession,
            bool,
            IPreviewCameraUpdateService> cameraServiceFactory,
        FixedTargetCameraController cameraController,
        Action<bool> setPreviewAvailable,
        Action<
            CameraState,
            NativeMethods.Result,
            string?>? writeCameraLog = null,
        Action<ManagedStartupDiagnosticEvent>? writeStartupDiagnostic = null,
        SynchronizationContext? notificationContext = null)
    {
        _sessionFactory = sessionFactory ??
            throw new ArgumentNullException(nameof(sessionFactory));
        _cameraServiceFactory = cameraServiceFactory ??
            throw new ArgumentNullException(nameof(cameraServiceFactory));
        _cameraController = cameraController ??
            throw new ArgumentNullException(nameof(cameraController));
        _setPreviewAvailable = setPreviewAvailable ??
            throw new ArgumentNullException(nameof(setPreviewAvailable));
        _writeCameraLog = writeCameraLog;
        _writeStartupDiagnostic = writeStartupDiagnostic;
        _notificationContext =
            notificationContext ?? SynchronizationContext.Current;
    }

    internal event Action<PreviewLifecycleSnapshot>? StateChanged;

    internal event Action<CameraState, NativeMethods.Result>?
        CameraStatePublished;

    internal event Action<ComfortZoneFollowStep>? FollowStatePublished;

    internal PreviewLifecycleState State
    {
        get
        {
            lock (_stateGate)
            {
                return _state;
            }
        }
    }

    internal string? LastError
    {
        get
        {
            lock (_stateGate)
            {
                return _lastError;
            }
        }
    }

    internal bool IsPreviewing => State == PreviewLifecycleState.Previewing;

    internal RecordingController GetOrCreateRecordingController()
    {
        lock (_stateGate)
        {
            if (_session is null || _sessionDisposed)
            {
                throw new InvalidOperationException(
                    "Native preview session is unavailable.");
            }
            return _recordingController ??= new RecordingController(_session);
        }
    }

    internal double LastEngineStopDurationMs
    {
        get
        {
            lock (_stateGate)
            {
                return _lastEngineStopDurationMs;
            }
        }
    }

    internal double LastLifecycleCloseDurationMs
    {
        get
        {
            lock (_stateGate)
            {
                return _lastLifecycleCloseDurationMs;
            }
        }
    }

    internal bool CanReconfigureRegion
    {
        get
        {
            PreviewLifecycleState state = State;
            return Volatile.Read(ref _closingRequested) == 0 &&
                _session is not null &&
                (state is PreviewLifecycleState.Previewing or
                    PreviewLifecycleState.Stopped ||
                 state == PreviewLifecycleState.Error &&
                    !_nativeMayNeedStop);
        }
    }

    internal SessionGeometry? DesiredGeometry
    {
        get
        {
            lock (_stateGate)
            {
                return _desiredGeometry;
            }
        }
    }

    internal ulong DesiredGeometryRevision
    {
        get
        {
            lock (_stateGate)
            {
                return _desiredGeometryRevision;
            }
        }
    }

    internal ulong ConfiguredGeometryRevision
    {
        get
        {
            lock (_stateGate)
            {
                return _configuredGeometryRevision;
            }
        }
    }

    internal SessionGeometry? CurrentGeometry
    {
        get
        {
            lock (_stateGate)
            {
                return _currentGeometry;
            }
        }
    }

    internal ulong CurrentGeometryRevision
    {
        get
        {
            lock (_stateGate)
            {
                return _currentGeometryRevision;
            }
        }
    }

    internal PreviewRuntimeSettings CurrentRuntimeSettings
    {
        get
        {
            lock (_stateGate)
            {
                return _currentSettings;
            }
        }
    }

    internal bool IsCustomRegionPreview
    {
        get
        {
            lock (_stateGate)
            {
                return _currentGeometry is not null &&
                    _currentRangeMode == CaptureRangeMode.CustomRegion;
            }
        }
    }

    internal CaptureRangeMode CurrentRangeMode
    {
        get
        {
            lock (_stateGate)
            {
                return _currentRangeMode;
            }
        }
    }

    internal CaptureTarget CurrentCaptureTarget
    {
        get
        {
            lock (_stateGate)
            {
                return _currentCaptureTarget;
            }
        }
    }

    internal async Task<PreviewLifecycleResult> InitializeAsync()
    {
        await _operationGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (Volatile.Read(ref _closingRequested) != 0)
            {
                return Rejected("Preview lifecycle is closing.");
            }
            if (State != PreviewLifecycleState.NotInitialized)
            {
                return NoChange();
            }

            try
            {
                _session = await Task.Run(_sessionFactory).
                    ConfigureAwait(false);
                SetState(PreviewLifecycleState.Stopped, null);
                return Succeeded();
            }
            catch (Exception error)
            {
                string detail = $"Native session initialization failed: {error.Message}";
                SetState(PreviewLifecycleState.Error, detail);
                return Failed(detail);
            }
        }
        finally
        {
            _operationGate.Release();
        }
    }

    internal async Task<PreviewLifecycleResult> StartAsync(
        bool cameraEnabled,
        bool followEnabled,
        NativeMethods.CursorMode cursorMode)
    {
        await _operationGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (Volatile.Read(ref _closingRequested) != 0)
            {
                return Rejected("Preview lifecycle is closing.");
            }

            PreviewLifecycleState state = State;
            if (state == PreviewLifecycleState.Previewing)
            {
                return NoChange();
            }
            if (state is PreviewLifecycleState.NotInitialized or
                PreviewLifecycleState.Closing or
                PreviewLifecycleState.Disposed)
            {
                return Rejected($"Cannot Start while lifecycle state is {state}.");
            }
            if (_session is null)
            {
                string missing = "Native session is unavailable.";
                SetState(PreviewLifecycleState.Error, missing);
                return Failed(missing);
            }

            int attemptNumber = Interlocked.Increment(
                ref _startAttemptNumber);
            string attemptId = Guid.NewGuid().ToString("D").ToUpperInvariant();
            _activeStartupAttemptId = attemptId;
            PublishStartupDiagnostic(new ManagedStartupDiagnosticEvent
            {
                ManagedStage = "LifecycleStartRequested",
                StartupAttemptId = attemptId,
                StartAttemptNumber = attemptNumber,
                LifecycleState = State.ToString(),
                Result = "begin",
            });
            SetPreviewAvailable(false);
            SetState(PreviewLifecycleState.Starting, null);

            if (_nativeMayNeedStop)
            {
                NativeMethods.Result recoveryStop = await Task.Run(
                    _session.Stop).ConfigureAwait(false);
                if (recoveryStop != NativeMethods.Result.Ok)
                {
                    string recoveryError = DescribeNativeFailure(
                        "Start recovery Stop",
                        recoveryStop);
                    SetState(PreviewLifecycleState.Error, recoveryError);
                    return Failed(recoveryError);
                }
                _nativeMayNeedStop = false;
            }

            SessionGeometry? desiredGeometry;
            ulong desiredRevision;
            ulong configuredRevision;
            lock (_stateGate)
            {
                desiredGeometry = _desiredGeometry;
                desiredRevision = _desiredGeometryRevision;
                configuredRevision = _configuredGeometryRevision;
            }
            if (desiredGeometry is null || desiredRevision == 0)
            {
                const string missingGeometry =
                    "A valid desired SessionGeometry is required before Start.";
                SetState(PreviewLifecycleState.Error, missingGeometry);
                return Failed(missingGeometry);
            }
            CaptureRangeMode desiredRangeMode =
                PreviewRuntimeSettings.IsCustomRegion(desiredGeometry)
                    ? CaptureRangeMode.CustomRegion
                    : CaptureRangeMode.FullScreen;
            PreviewRuntimeSettings runtimeSettings =
                PreviewRuntimeSettings.ForCaptureMode(
                    desiredRangeMode,
                    new PreviewRuntimeSettings(
                        cameraEnabled,
                        followEnabled,
                        cursorMode,
                        CameraCommandsAvailable: true));
            if (desiredRevision != configuredRevision)
            {
                SessionGeometryNativeV1 nativeGeometry =
                    SessionGeometryNativeV1.FromGeometry(
                        desiredGeometry,
                        desiredRevision);
                NativeMethods.Result geometryResult =
                    _session.SetSessionGeometry(in nativeGeometry);
                if (geometryResult != NativeMethods.Result.Ok)
                {
                    string geometryError = DescribeNativeFailure(
                        "SetSessionGeometry before Start",
                        geometryResult);
                    SetState(PreviewLifecycleState.Error, geometryError);
                    return Failed(geometryError);
                }
                lock (_stateGate)
                {
                    _configuredGeometryRevision = desiredRevision;
                }
            }

            NativeMethods.Result cursorResult = _session.SetCursorMode(
                runtimeSettings.CursorMode);
            if (cursorResult != NativeMethods.Result.Ok)
            {
                string cursorError = DescribeNativeFailure(
                    "SetCursorMode before Start",
                    cursorResult);
                SetState(PreviewLifecycleState.Error, cursorError);
                return Failed(cursorError);
            }

            NativeMethods.Result startResult;
            try
            {
                PublishStartupDiagnostic(new ManagedStartupDiagnosticEvent
                {
                    ManagedStage = "NativeStartCallBegin",
                    StartupAttemptId = attemptId,
                    StartAttemptNumber = attemptNumber,
                    LifecycleState = State.ToString(),
                    Result = "begin",
                });
                _nativeMayNeedStop = true;
                startResult = await Task.Run(_session.Start).
                    ConfigureAwait(false);
            }
            catch (Exception error)
            {
                PublishStartupDiagnostic(new ManagedStartupDiagnosticEvent
                {
                    ManagedStage = "NativeStartThrew",
                    StartupAttemptId = attemptId,
                    StartAttemptNumber = attemptNumber,
                    SessionGuid = TryReadNativeSessionGuid(),
                    LifecycleState = State.ToString(),
                    NativeHResult = TryReadNativeHresult(),
                    Result = error.GetType().FullName,
                });
                return await FailStartAndCleanupAsync(
                    $"Native Start threw: {error.Message}").
                    ConfigureAwait(false);
            }

            PublishStartupDiagnostic(new ManagedStartupDiagnosticEvent
            {
                ManagedStage = startResult == NativeMethods.Result.Ok
                    ? "NativeStartReturnedSuccess"
                    : "NativeStartReturnedFailure",
                StartupAttemptId = attemptId,
                StartAttemptNumber = attemptNumber,
                SessionGuid = TryReadNativeSessionGuid(),
                LifecycleState = State.ToString(),
                NativeHResult = TryReadNativeHresult(),
                Result = startResult.ToString(),
            });

            if (startResult != NativeMethods.Result.Ok)
            {
                return await FailStartAndCleanupAsync(
                    DescribeNativeFailure("Native Start", startResult)).
                    ConfigureAwait(false);
            }

            NativeMethods.Result resizeResult =
                await ApplyLatestResizeUnderGateAsync().ConfigureAwait(false);
            if (resizeResult != NativeMethods.Result.Ok)
            {
                return await FailStartAndCleanupAsync(
                    DescribeNativeFailure(
                        "Resize after Start",
                        resizeResult)).ConfigureAwait(false);
            }

            if (Volatile.Read(ref _closingRequested) != 0)
            {
                SetState(
                    PreviewLifecycleState.Closing,
                    "Preview lifecycle is closing.");
                string? closeError =
                    await StopRunningPreviewUnderGateAsync(
                        submitWide: true).ConfigureAwait(false);
                if (closeError is not null)
                {
                    SetLastError(closeError);
                }
                return Rejected("Preview lifecycle is closing.");
            }

            try
            {
                _cameraController.SetPreviewRunning(
                    true,
                    Stopwatch.GetTimestamp());
                _cameraController.SetEnabled(
                    runtimeSettings.CameraEnabled,
                    Stopwatch.GetTimestamp());
                _cameraUpdates = _cameraServiceFactory(
                    _session,
                    runtimeSettings.FollowEnabled);
                _cameraUpdates.StatePublished += OnCameraStatePublished;
                _cameraUpdates.FollowStatePublished += OnFollowStatePublished;
                _cameraUpdates.Start();
                SetPreviewAvailable(runtimeSettings.CameraCommandsAvailable);
                _nativeMayNeedStop = true;
                CommitCurrentGeometry(
                    desiredGeometry,
                    desiredRevision,
                    desiredRangeMode,
                    runtimeSettings);
                SetState(PreviewLifecycleState.Previewing, null);
                PublishStartupDiagnostic(new ManagedStartupDiagnosticEvent
                {
                    ManagedStage = "LifecycleEnteredPreviewing",
                    StartupAttemptId = attemptId,
                    StartAttemptNumber = attemptNumber,
                    SessionGuid = TryReadNativeSessionGuid(),
                    LifecycleState = State.ToString(),
                    Result = "success",
                });
                return Succeeded();
            }
            catch (Exception error)
            {
                return await FailStartAndCleanupAsync(
                    $"Camera service startup failed: {error.Message}").
                    ConfigureAwait(false);
            }
        }
        finally
        {
            _operationGate.Release();
        }
    }

    internal async Task<PreviewLifecycleResult> StopAsync()
    {
        await _operationGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (Volatile.Read(ref _closingRequested) != 0)
            {
                return Rejected("Preview lifecycle is closing.");
            }
            PreviewLifecycleState state = State;
            if (state is PreviewLifecycleState.NotInitialized or
                PreviewLifecycleState.Stopped)
            {
                return NoChange();
            }
            if (state is PreviewLifecycleState.Closing or
                PreviewLifecycleState.Disposed)
            {
                return Rejected($"Cannot Stop while lifecycle state is {state}.");
            }

            SetState(PreviewLifecycleState.Stopping, null);
            string? recordingError =
                await StopRecordingForPreviewStopAsync().ConfigureAwait(false);
            string? previewError = await StopRunningPreviewUnderGateAsync(
                submitWide: _nativeMayNeedStop).ConfigureAwait(false);
            string? error = CombineErrors(recordingError, previewError);
            if (error is not null)
            {
                SetState(PreviewLifecycleState.Error, error);
                return Failed(error);
            }

            SetState(PreviewLifecycleState.Stopped, null);
            return Succeeded();
        }
        finally
        {
            _operationGate.Release();
        }
    }

    internal async Task<PreviewLifecycleResult> SetCursorModeAsync(
        NativeMethods.CursorMode cursorMode)
    {
        await _operationGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (Volatile.Read(ref _closingRequested) != 0)
            {
                return Rejected("Preview lifecycle is closing.");
            }
            if (State is not (
                PreviewLifecycleState.Stopped or
                PreviewLifecycleState.Error) ||
                _session is null)
            {
                return Rejected(
                    $"Cursor mode cannot change while state is {State}.");
            }
            NativeMethods.Result result = _session.SetCursorMode(cursorMode);
            if (result == NativeMethods.Result.Ok)
            {
                return Succeeded();
            }
            string detail = DescribeNativeFailure("SetCursorMode", result);
            SetLastError(detail);
            return Failed(detail);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    internal async Task<PreviewLifecycleResult> SetRecordCursorVisibleAsync(
        bool visible)
    {
        await _operationGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (Volatile.Read(ref _closingRequested) != 0)
            {
                return Rejected("Preview lifecycle is closing.");
            }
            if (State is not (
                PreviewLifecycleState.Stopped or
                PreviewLifecycleState.Previewing or
                PreviewLifecycleState.Error) ||
                _session is null)
            {
                return Rejected(
                    $"Record cursor visibility cannot change while state is {State}.");
            }

            NativeMethods.Result result =
                _session.SetRecordCursorVisible(visible);
            if (result != NativeMethods.Result.Ok)
            {
                string detail = DescribeNativeFailure(
                    "SetRecordCursorVisible", result);
                SetLastError(detail);
                return Failed(detail);
            }

            RecordCursorVisibilitySnapshot snapshot =
                _session.GetRecordCursorVisible();
            if (snapshot.RequestedVisible != visible ||
                snapshot.AppliedVisible != visible)
            {
                string detail =
                    $"Record cursor visibility readback mismatch: " +
                    $"requested={snapshot.RequestedVisible}; " +
                    $"applied={snapshot.AppliedVisible}; " +
                    $"revision={snapshot.Revision}.";
                SetLastError(detail);
                return Failed(detail);
            }
            return Succeeded();
        }
        finally
        {
            _operationGate.Release();
        }
    }

    internal async Task<PreviewLifecycleResult> SetCaptureTargetAsync(
        CaptureTarget target)
    {
        if (!Enum.IsDefined(target.Kind) ||
            target.IsWindow && target.WindowHandle == nint.Zero)
        {
            return Rejected("Capture target is invalid.");
        }

        await _operationGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (Volatile.Read(ref _closingRequested) != 0)
            {
                return Rejected("Preview lifecycle is closing.");
            }
            if (State is not (
                PreviewLifecycleState.Stopped or PreviewLifecycleState.Error) ||
                _session is null)
            {
                return Rejected(
                    $"Capture target cannot change while state is {State}.");
            }

            NativeMethods.Result result = _session.SetCaptureTarget(target);
            if (result != NativeMethods.Result.Ok)
            {
                string detail = DescribeNativeFailure(
                    "SetCaptureTarget", result);
                SetLastError(detail);
                return Failed(detail);
            }
            lock (_stateGate)
            {
                _currentCaptureTarget = target;
            }
            return Succeeded();
        }
        finally
        {
            _operationGate.Release();
        }
    }

    internal void SetFollowEnabled(bool enabled)
    {
        IPreviewCameraUpdateService? service = _cameraUpdates;
        service?.SetFollowEnabled(enabled);
    }

    internal async Task<PreviewLifecycleResult> SetDesiredGeometryAsync(
        SessionGeometry geometry)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        _ = SessionGeometryNativeV1.FromGeometry(geometry, 1);

        await _operationGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (Volatile.Read(ref _closingRequested) != 0)
            {
                return Rejected("Preview lifecycle is closing.");
            }
            PreviewLifecycleState state = State;
            if (state is PreviewLifecycleState.NotInitialized or
                PreviewLifecycleState.Closing or
                PreviewLifecycleState.Disposed)
            {
                return Rejected(
                    $"SessionGeometry cannot change while state is {state}.");
            }

            lock (_stateGate)
            {
                if (_desiredGeometry is not null &&
                    SessionGeometryNativeV1.ContentEquals(
                        _desiredGeometry,
                        geometry))
                {
                    return NoChange();
                }
                if (_nextGeometryRevision == ulong.MaxValue)
                {
                    const string exhausted =
                        "SessionGeometry revision space is exhausted.";
                    _lastError = exhausted;
                    return Failed(exhausted);
                }
                _desiredGeometry = geometry;
                _desiredGeometryRevision = _nextGeometryRevision;
                _nextGeometryRevision++;
            }
            return Succeeded();
        }
        finally
        {
            _operationGate.Release();
        }
    }

    internal async Task<PreviewLifecycleResult> ReconfigureRegionAsync(
        Func<
            RegionSelectionRequest,
            CancellationToken,
            GeometrySelectionResult> selectRegion,
        PreviewRuntimeSettings fullScreenSettings)
    {
        ArgumentNullException.ThrowIfNull(selectRegion);

        SessionGeometry? priorGeometry;
        ulong priorRevision;
        CaptureRangeMode priorRangeMode;
        PreviewRuntimeSettings priorSettings;
        bool restartPriorPreview;
        CancellationTokenSource selectionCancellation = new();

        await _operationGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (Volatile.Read(ref _closingRequested) != 0)
            {
                selectionCancellation.Dispose();
                return Rejected("Preview lifecycle is closing.");
            }
            PreviewLifecycleState state = State;
            if (state is not (
                PreviewLifecycleState.Previewing or
                PreviewLifecycleState.Stopped or
                PreviewLifecycleState.Error))
            {
                selectionCancellation.Dispose();
                return Rejected(
                    $"Region selection cannot begin while state is {state}.");
            }
            if (state == PreviewLifecycleState.Error &&
                _nativeMayNeedStop)
            {
                selectionCancellation.Dispose();
                return Rejected(
                    "Region selection is unsafe until native Error cleanup completes.");
            }
            if (_session is null)
            {
                selectionCancellation.Dispose();
                return Failed("Native session is unavailable.");
            }

            lock (_stateGate)
            {
                priorGeometry = _currentGeometry ?? _desiredGeometry;
                priorRevision = _currentGeometryRevision != 0
                    ? _currentGeometryRevision
                    : _configuredGeometryRevision;
                priorRangeMode = _currentRangeMode;
                priorSettings = _currentSettings;
                _selectionCancellation = selectionCancellation;
            }
            restartPriorPreview =
                state == PreviewLifecycleState.Previewing;
            SetState(PreviewLifecycleState.SelectingRegion, null);
            SetPreviewAvailable(false);
            if (restartPriorPreview)
            {
                string? stopError =
                    await StopRunningPreviewUnderGateAsync(
                        submitWide: true).ConfigureAwait(false);
                if (stopError is not null)
                {
                    ClearSelectionCancellation(selectionCancellation);
                    SetState(PreviewLifecycleState.Error, stopError);
                    return Failed(stopError);
                }
            }
        }
        finally
        {
            _operationGate.Release();
        }

        GeometrySelectionResult selection;
        try
        {
            RegionSelectionRequest request = new(
                RollbackGeometry: priorGeometry,
                InitialSelection:
                    priorRangeMode == CaptureRangeMode.CustomRegion
                        ? priorGeometry?.CaptureRegion
                        : null,
                CurrentRangeMode: priorRangeMode);
            selection = await InvokeSelectionAsync(
                selectRegion,
                request,
                selectionCancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            selection = GeometrySelectionResult.Cancelled();
        }
        catch (Exception error)
        {
            selection = GeometrySelectionResult.Failed(error.Message);
        }

        await _operationGate.WaitAsync().ConfigureAwait(false);
        try
        {
            ClearSelectionCancellation(selectionCancellation);
            if (Volatile.Read(ref _closingRequested) != 0 ||
                State is PreviewLifecycleState.Closing or
                    PreviewLifecycleState.Disposed)
            {
                return Rejected("Preview lifecycle is closing.");
            }

            SetState(PreviewLifecycleState.Reconfiguring, null);
            if (selection.Outcome != GeometrySelectionOutcome.Confirmed)
            {
                PreviewLifecycleResult restore =
                    await RestorePriorAfterSelectionUnderGateAsync(
                        priorGeometry,
                        priorRevision,
                        priorRangeMode,
                        priorSettings,
                        restartPriorPreview).ConfigureAwait(false);
                if (selection.Outcome == GeometrySelectionOutcome.Failed &&
                    restore.Succeeded)
                {
                    string detail =
                        selection.Error ?? "Region selection failed.";
                    SetLastError(detail);
                    return Failed(detail);
                }
                return restore;
            }

            SessionGeometry? candidate = selection.CandidateGeometry;
            if (candidate is null)
            {
                PreviewLifecycleResult restored =
                    await RestorePriorAfterSelectionUnderGateAsync(
                        priorGeometry,
                        priorRevision,
                        priorRangeMode,
                        priorSettings,
                        restartPriorPreview).ConfigureAwait(false);
                const string invalid =
                    "Confirmed selection did not supply SessionGeometry.";
                if (!restored.Succeeded)
                {
                    string combined = CombineErrors(
                        invalid,
                        restored.Error) ?? invalid;
                    SetState(PreviewLifecycleState.Error, combined);
                    return Failed(combined);
                }
                SetLastError(invalid);
                return Failed(invalid);
            }
            try
            {
                _ = SessionGeometryNativeV1.FromGeometry(candidate, 1);
            }
            catch (Exception error)
            {
                PreviewLifecycleResult restored =
                    await RestorePriorAfterSelectionUnderGateAsync(
                        priorGeometry,
                        priorRevision,
                        priorRangeMode,
                        priorSettings,
                        restartPriorPreview).ConfigureAwait(false);
                string invalid =
                    $"Candidate SessionGeometry validation failed: {error.Message}";
                if (!restored.Succeeded)
                {
                    string combined = CombineErrors(
                        invalid,
                        restored.Error) ?? invalid;
                    SetState(PreviewLifecycleState.Error, combined);
                    return Failed(combined);
                }
                SetLastError(invalid);
                return Failed(invalid);
            }
            return await ApplyCandidateGeometryUnderGateAsync(
                candidate,
                CaptureRangeMode.CustomRegion,
                fullScreenSettings,
                priorGeometry,
                priorRevision,
                priorRangeMode,
                priorSettings,
                restartPriorPreview).ConfigureAwait(false);
        }
        finally
        {
            selectionCancellation.Dispose();
            _operationGate.Release();
        }
    }

    internal async Task<PreviewLifecycleResult> ReconfigureGeometryAsync(
        SessionGeometry candidate,
        PreviewRuntimeSettings requestedSettings)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        _ = SessionGeometryNativeV1.FromGeometry(candidate, 1);

        await _operationGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (Volatile.Read(ref _closingRequested) != 0)
            {
                return Rejected("Preview lifecycle is closing.");
            }
            PreviewLifecycleState state = State;
            if (state is not (
                PreviewLifecycleState.Previewing or
                PreviewLifecycleState.Stopped or
                PreviewLifecycleState.Error))
            {
                return Rejected(
                    $"Geometry cannot be reconfigured while state is {state}.");
            }
            if (state == PreviewLifecycleState.Error &&
                _nativeMayNeedStop)
            {
                return Rejected(
                    "Geometry reconfiguration is unsafe until native Error cleanup completes.");
            }
            if (_session is null)
            {
                return Failed("Native session is unavailable.");
            }

            SessionGeometry? priorGeometry;
            ulong priorRevision;
            CaptureRangeMode priorRangeMode;
            PreviewRuntimeSettings priorSettings;
            lock (_stateGate)
            {
                priorGeometry = _currentGeometry ?? _desiredGeometry;
                priorRevision = _currentGeometryRevision != 0
                    ? _currentGeometryRevision
                    : _configuredGeometryRevision;
                priorRangeMode = _currentRangeMode;
                priorSettings = _currentSettings;
            }
            if (priorGeometry is not null &&
                SessionGeometryNativeV1.ContentEquals(
                    priorGeometry,
                    candidate) &&
                state == PreviewLifecycleState.Previewing)
            {
                return NoChange();
            }

            bool restartPriorPreview =
                state == PreviewLifecycleState.Previewing;
            SetPreviewAvailable(false);
            SetState(PreviewLifecycleState.Reconfiguring, null);
            if (restartPriorPreview)
            {
                string? stopError =
                    await StopRunningPreviewUnderGateAsync(
                        submitWide: true).ConfigureAwait(false);
                if (stopError is not null)
                {
                    SetState(PreviewLifecycleState.Error, stopError);
                    return Failed(stopError);
                }
            }

            return await ApplyCandidateGeometryUnderGateAsync(
                candidate,
                CaptureRangeMode.FullScreen,
                requestedSettings,
                priorGeometry,
                priorRevision,
                priorRangeMode,
                priorSettings,
                restartPriorPreview).ConfigureAwait(false);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    internal async Task<PreviewLifecycleResult> RequestResizeAsync(
        int width,
        int height)
    {
        if (width <= 0 || height <= 0)
        {
            return NoChange();
        }

        lock (_resizeGate)
        {
            _pendingWidth = width;
            _pendingHeight = height;
            _resizeVersion++;
        }

        await _operationGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (State != PreviewLifecycleState.Previewing ||
                _session is null ||
                Volatile.Read(ref _closingRequested) != 0)
            {
                return NoChange();
            }

            NativeMethods.Result result =
                await ApplyLatestResizeUnderGateAsync().ConfigureAwait(false);
            if (result == NativeMethods.Result.Ok)
            {
                return Succeeded();
            }
            string detail = DescribeNativeFailure("Resize", result);
            SetLastError(detail);
            return Failed(detail);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    internal bool TryReadStats(
        out NativeMethods.PreviewStats stats,
        out NativeMethods.CursorStats cursor,
        out string? error)
    {
        stats = default;
        cursor = default;
        error = null;
        if (!_operationGate.Wait(0))
        {
            return false;
        }
        try
        {
            if (_session is null ||
                State is PreviewLifecycleState.NotInitialized or
                    PreviewLifecycleState.Disposed)
            {
                return false;
            }
            try
            {
                stats = _session.GetStats();
                cursor = _session.GetCursorStats();
                return true;
            }
            catch (Exception readError)
            {
                error = readError.Message;
                return false;
            }
        }
        finally
        {
            _operationGate.Release();
        }
    }

    internal async Task CloseAsync()
    {
        long closeStarted = Stopwatch.GetTimestamp();
        Interlocked.Exchange(ref _closingRequested, 1);
        CancellationTokenSource? selection;
        lock (_stateGate)
        {
            selection = _selectionCancellation;
        }
        try
        {
            selection?.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
        await _operationGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (State == PreviewLifecycleState.Disposed)
            {
                return;
            }

            SetState(PreviewLifecycleState.Closing, LastError);
            SetPreviewAvailable(false);
            string? recordingError =
                await StopRecordingForPreviewStopAsync().ConfigureAwait(false);
            string? previewError = await StopRunningPreviewUnderGateAsync(
                submitWide: _nativeMayNeedStop).ConfigureAwait(false);
            string? stopError = CombineErrors(recordingError, previewError);
            if (stopError is not null)
            {
                SetLastError(stopError);
            }

            if (!_sessionDisposed && _session is not null)
            {
                try
                {
                    if (_recordingController is not null)
                    {
                        try
                        {
                            await _recordingController.DisposeAsync().
                                ConfigureAwait(false);
                        }
                        catch (Exception error)
                        {
                            SetLastError(CombineErrors(
                                LastError,
                                $"Recording controller Dispose failed: {error.Message}"));
                        }
                    }
                    _session.Dispose();
                }
                finally
                {
                    _recordingController = null;
                    _sessionDisposed = true;
                    _session = null;
                }
            }
            SetState(PreviewLifecycleState.Disposed, LastError);
        }
        finally
        {
            lock (_stateGate)
            {
                _lastLifecycleCloseDurationMs =
                    Stopwatch.GetElapsedTime(closeStarted).TotalMilliseconds;
            }
            _operationGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await CloseAsync().ConfigureAwait(false);
    }

    private async Task<GeometrySelectionResult> InvokeSelectionAsync(
        Func<
            RegionSelectionRequest,
            CancellationToken,
            GeometrySelectionResult> selectRegion,
        RegionSelectionRequest request,
        CancellationToken cancellationToken)
    {
        if (_notificationContext is null)
        {
            return await Task.Run(
                () => selectRegion(request, cancellationToken),
                cancellationToken).ConfigureAwait(false);
        }

        TaskCompletionSource<GeometrySelectionResult> completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        _notificationContext.Post(
            static value =>
            {
                var (callback, selectionRequest, token, target) =
                    ((Func<
                        RegionSelectionRequest,
                        CancellationToken,
                        GeometrySelectionResult>,
                      RegionSelectionRequest,
                      CancellationToken,
                      TaskCompletionSource<GeometrySelectionResult>))value!;
                try
                {
                    token.ThrowIfCancellationRequested();
                    target.SetResult(callback(selectionRequest, token));
                }
                catch (OperationCanceledException)
                {
                    target.SetCanceled(token);
                }
                catch (Exception error)
                {
                    target.SetException(error);
                }
            },
            (selectRegion, request, cancellationToken, completion));
        return await completion.Task.ConfigureAwait(false);
    }

    private async Task<PreviewLifecycleResult>
        RestorePriorAfterSelectionUnderGateAsync(
            SessionGeometry? priorGeometry,
            ulong priorRevision,
            CaptureRangeMode priorRangeMode,
            PreviewRuntimeSettings priorSettings,
            bool restartPriorPreview)
    {
        if (!restartPriorPreview)
        {
            SetState(PreviewLifecycleState.Stopped, null);
            return Succeeded();
        }
        if (priorGeometry is null || priorRevision == 0)
        {
            const string missing =
                "Cannot restore Preview because no committed geometry exists.";
            SetState(PreviewLifecycleState.Error, missing);
            return Failed(missing);
        }

        StartAttemptResult restart =
            await StartConfiguredPreviewAttemptUnderGateAsync(
                priorSettings).ConfigureAwait(false);
        if (!restart.Success)
        {
            string detail =
                $"Restoring the previous Preview failed: {restart.Error}";
            SetState(PreviewLifecycleState.Error, detail);
            return Failed(detail);
        }
        CommitCurrentGeometry(
            priorGeometry,
            priorRevision,
            priorRangeMode,
            priorSettings);
        SetState(PreviewLifecycleState.Previewing, null);
        return Succeeded();
    }

    private async Task<PreviewLifecycleResult>
        ApplyCandidateGeometryUnderGateAsync(
            SessionGeometry candidate,
            CaptureRangeMode candidateRangeMode,
            PreviewRuntimeSettings requestedSettings,
            SessionGeometry? priorGeometry,
            ulong priorRevision,
            CaptureRangeMode priorRangeMode,
            PreviewRuntimeSettings priorSettings,
            bool restartPriorPreview)
    {
        if (_session is null)
        {
            const string missing = "Native session is unavailable.";
            SetState(PreviewLifecycleState.Error, missing);
            return Failed(missing);
        }
        if (priorGeometry is not null &&
            SessionGeometryNativeV1.ContentEquals(
                priorGeometry,
                candidate))
        {
            return await RestorePriorAfterSelectionUnderGateAsync(
                priorGeometry,
                priorRevision,
                priorRangeMode,
                priorSettings,
                restartPriorPreview: true).ConfigureAwait(false);
        }

        if (!TryAllocateGeometryRevision(out ulong candidateRevision))
        {
            const string exhausted =
                "SessionGeometry revision space is exhausted.";
            SetState(PreviewLifecycleState.Error, exhausted);
            return Failed(exhausted);
        }

        SessionGeometryNativeV1 nativeCandidate =
            SessionGeometryNativeV1.FromGeometry(
                candidate,
                candidateRevision);
        NativeMethods.Result configureResult =
            _session.SetSessionGeometry(in nativeCandidate);
        if (configureResult != NativeMethods.Result.Ok)
        {
            string configureError = DescribeNativeFailure(
                "Set candidate SessionGeometry",
                configureResult);
            PreviewLifecycleResult restored =
                await RestorePriorAfterSelectionUnderGateAsync(
                    priorGeometry,
                    priorRevision,
                    priorRangeMode,
                    priorSettings,
                    restartPriorPreview).ConfigureAwait(false);
            if (!restored.Succeeded)
            {
                string combined = CombineErrors(
                    configureError,
                    restored.Error) ?? configureError;
                SetState(PreviewLifecycleState.Error, combined);
                return Failed(combined);
            }
            SetLastError(configureError);
            return Failed(configureError);
        }
        lock (_stateGate)
        {
            _configuredGeometryRevision = candidateRevision;
        }

        PreviewRuntimeSettings candidateSettings =
            PreviewRuntimeSettings.ForCaptureMode(
                candidateRangeMode,
                requestedSettings);
        StartAttemptResult candidateStart =
            await StartConfiguredPreviewAttemptUnderGateAsync(
                candidateSettings).ConfigureAwait(false);
        if (candidateStart.Success)
        {
            CommitCurrentGeometry(
                candidate,
                candidateRevision,
                candidateRangeMode,
                candidateSettings);
            SetState(PreviewLifecycleState.Previewing, null);
            return Succeeded();
        }

        string candidateError =
            candidateStart.Error ?? "Candidate Preview Start failed.";
        if (priorGeometry is null ||
            !TryAllocateGeometryRevision(out ulong rollbackRevision))
        {
            string noRollback = CombineErrors(
                candidateError,
                "No prior geometry or rollback revision is available.") ??
                candidateError;
            SetState(PreviewLifecycleState.Error, noRollback);
            return Failed(noRollback);
        }

        SessionGeometryNativeV1 rollbackGeometry =
            SessionGeometryNativeV1.FromGeometry(
                priorGeometry,
                rollbackRevision);
        NativeMethods.Result rollbackConfigure =
            _session.SetSessionGeometry(in rollbackGeometry);
        if (rollbackConfigure != NativeMethods.Result.Ok)
        {
            string combined = CombineErrors(
                candidateError,
                DescribeNativeFailure(
                    "Rollback SetSessionGeometry",
                    rollbackConfigure)) ?? candidateError;
            SetState(PreviewLifecycleState.Error, combined);
            return Failed(combined);
        }
        lock (_stateGate)
        {
            _configuredGeometryRevision = rollbackRevision;
        }

        StartAttemptResult rollbackStart =
            await StartConfiguredPreviewAttemptUnderGateAsync(
                priorSettings).ConfigureAwait(false);
        if (!rollbackStart.Success)
        {
            string combined = CombineErrors(
                candidateError,
                $"Rollback Start failed: {rollbackStart.Error}") ??
                candidateError;
            SetState(PreviewLifecycleState.Error, combined);
            return Failed(combined);
        }

        CommitCurrentGeometry(
            priorGeometry,
            rollbackRevision,
            priorRangeMode,
            priorSettings);
        string recovered = CombineErrors(
            candidateError,
            "The previous Preview was restored.") ?? candidateError;
        SetState(PreviewLifecycleState.Previewing, recovered);
        return Failed(recovered);
    }

    private async Task<StartAttemptResult>
        StartConfiguredPreviewAttemptUnderGateAsync(
            PreviewRuntimeSettings settings)
    {
        if (_session is null)
        {
            return new StartAttemptResult(
                false,
                "Native session is unavailable.");
        }

        NativeMethods.Result cursorResult =
            _session.SetCursorMode(settings.CursorMode);
        if (cursorResult != NativeMethods.Result.Ok)
        {
            return new StartAttemptResult(
                false,
                DescribeNativeFailure(
                    "SetCursorMode before reconfigured Start",
                    cursorResult));
        }

        try
        {
            _nativeMayNeedStop = true;
            NativeMethods.Result startResult =
                await Task.Run(_session.Start).ConfigureAwait(false);
            if (startResult != NativeMethods.Result.Ok)
            {
                string failure = DescribeNativeFailure(
                    "Reconfigured Native Start",
                    startResult);
                return new StartAttemptResult(
                    false,
                    await CleanupFailedStartAttemptAsync(failure).
                        ConfigureAwait(false));
            }

            NativeMethods.Result resizeResult =
                await ApplyLatestResizeUnderGateAsync().ConfigureAwait(false);
            if (resizeResult != NativeMethods.Result.Ok)
            {
                string failure = DescribeNativeFailure(
                    "Resize after reconfigured Start",
                    resizeResult);
                return new StartAttemptResult(
                    false,
                    await CleanupFailedStartAttemptAsync(failure).
                        ConfigureAwait(false));
            }

            if (Volatile.Read(ref _closingRequested) != 0)
            {
                return new StartAttemptResult(
                    false,
                    await CleanupFailedStartAttemptAsync(
                        "Preview lifecycle is closing.").
                        ConfigureAwait(false));
            }

            _cameraController.SetPreviewRunning(
                true,
                Stopwatch.GetTimestamp());
            _cameraController.SetEnabled(
                settings.CameraEnabled,
                Stopwatch.GetTimestamp());
            _cameraUpdates = _cameraServiceFactory(
                _session,
                settings.FollowEnabled);
            _cameraUpdates.StatePublished += OnCameraStatePublished;
            _cameraUpdates.FollowStatePublished += OnFollowStatePublished;
            _cameraUpdates.Start();
            SetPreviewAvailable(settings.CameraCommandsAvailable);
            _nativeMayNeedStop = true;
            return new StartAttemptResult(true, null);
        }
        catch (Exception error)
        {
            return new StartAttemptResult(
                false,
                await CleanupFailedStartAttemptAsync(
                    $"Reconfigured Start threw: {error.Message}").
                    ConfigureAwait(false));
        }
    }

    private async Task<string> CleanupFailedStartAttemptAsync(
        string failure)
    {
        SetPreviewAvailable(false);
        string? cameraError =
            await StopCameraServiceAsync().ConfigureAwait(false);
        string? stopError = null;
        if (_session is not null)
        {
            try
            {
                NativeMethods.Result stop =
                    await Task.Run(_session.Stop).ConfigureAwait(false);
                if (stop == NativeMethods.Result.Ok)
                {
                    _nativeMayNeedStop = false;
                }
                else
                {
                    stopError = DescribeNativeFailure(
                        "Failed Start cleanup Stop",
                        stop);
                }
            }
            catch (Exception error)
            {
                stopError =
                    $"Failed Start cleanup Stop threw: {error.Message}";
            }
        }
        _cameraController.SetPreviewRunning(
            false,
            Stopwatch.GetTimestamp());
        return CombineErrors(failure, cameraError, stopError) ?? failure;
    }

    private void CommitCurrentGeometry(
        SessionGeometry geometry,
        ulong revision,
        CaptureRangeMode rangeMode,
        PreviewRuntimeSettings settings)
    {
        lock (_stateGate)
        {
            _currentGeometry = geometry;
            _currentGeometryRevision = revision;
            _currentRangeMode = rangeMode;
            _desiredGeometry = geometry;
            _desiredGeometryRevision = revision;
            _configuredGeometryRevision = revision;
            _currentSettings = settings;
        }
    }

    private bool TryAllocateGeometryRevision(out ulong revision)
    {
        lock (_stateGate)
        {
            if (_nextGeometryRevision == ulong.MaxValue)
            {
                revision = 0;
                return false;
            }
            revision = _nextGeometryRevision;
            _nextGeometryRevision++;
            return true;
        }
    }

    private void ClearSelectionCancellation(
        CancellationTokenSource selectionCancellation)
    {
        lock (_stateGate)
        {
            if (ReferenceEquals(
                _selectionCancellation,
                selectionCancellation))
            {
                _selectionCancellation = null;
            }
        }
    }

    private async Task<PreviewLifecycleResult> FailStartAndCleanupAsync(
        string startError)
    {
        PublishStartupDiagnostic(new ManagedStartupDiagnosticEvent
        {
            ManagedStage = "FailStartAndCleanupBegin",
            StartupAttemptId = _activeStartupAttemptId,
            StartAttemptNumber = _startAttemptNumber,
            SessionGuid = TryReadNativeSessionGuid(),
            LifecycleState = State.ToString(),
            NativeHResult = TryReadNativeHresult(),
            Result = "begin",
        });
        SetPreviewAvailable(false);
        string? cameraError = await StopCameraServiceAsync().
            ConfigureAwait(false);
        NativeMethods.Result cleanupResult = NativeMethods.Result.Ok;
        string? cleanupError = null;
        if (_session is not null)
        {
            try
            {
                cleanupResult = await Task.Run(_session.Stop).
                    ConfigureAwait(false);
                if (cleanupResult == NativeMethods.Result.Ok)
                {
                    _nativeMayNeedStop = false;
                }
                else
                {
                    cleanupError = DescribeNativeFailure(
                        "Start failure cleanup Stop",
                        cleanupResult);
                }
            }
            catch (Exception error)
            {
                cleanupError =
                    $"Start failure cleanup Stop threw: {error.Message}";
            }
        }
        _cameraController.SetPreviewRunning(
            false,
            Stopwatch.GetTimestamp());

        string combined = CombineErrors(
            startError,
            cameraError,
            cleanupError) ?? startError;
        SetState(PreviewLifecycleState.Error, combined);
        PublishStartupDiagnostic(new ManagedStartupDiagnosticEvent
        {
            ManagedStage = "FailStartAndCleanupEnd",
            StartupAttemptId = _activeStartupAttemptId,
            StartAttemptNumber = _startAttemptNumber,
            SessionGuid = TryReadNativeSessionGuid(),
            LifecycleState = State.ToString(),
            NativeHResult = TryReadNativeHresult(),
            RetryAvailable = true,
            Result = cleanupResult.ToString(),
        });
        return Failed(combined);
    }

    private async Task<string?> StopRunningPreviewUnderGateAsync(
        bool submitWide)
    {
        SetPreviewAvailable(false);
        string? cameraError = await StopCameraServiceAsync().
            ConfigureAwait(false);
        string? wideError = null;
        string? stopError = null;

        if (_session is not null && submitWide)
        {
            CameraState wide = _cameraController.PrepareForExit(
                Stopwatch.GetTimestamp());
            NativeMethods.Result wideResult = _session.SetCameraState(wide);
            _writeCameraLog?.Invoke(
                wide,
                wideResult,
                "explicit-wide-before-stop");
            if (wideResult != NativeMethods.Result.Ok)
            {
                wideError = DescribeNativeFailure(
                    "Submit Wide before Stop",
                    wideResult);
            }

            NativeMethods.Result stopResult;
            long engineStopStarted = Stopwatch.GetTimestamp();
            try
            {
                stopResult = await Task.Run(_session.Stop).
                    ConfigureAwait(false);
            }
            catch (Exception error)
            {
                stopResult = NativeMethods.Result.NativeFailure;
                stopError = $"Native Stop threw: {error.Message}";
            }
            finally
            {
                lock (_stateGate)
                {
                    _lastEngineStopDurationMs = Stopwatch.GetElapsedTime(
                        engineStopStarted).TotalMilliseconds;
                }
            }
            if (stopResult == NativeMethods.Result.Ok)
            {
                _nativeMayNeedStop = false;
            }
            else if (stopError is null)
            {
                stopError = DescribeNativeFailure("Native Stop", stopResult);
            }
        }
        else
        {
            _cameraController.SetPreviewRunning(
                false,
                Stopwatch.GetTimestamp());
        }

        return CombineErrors(cameraError, wideError, stopError);
    }

    private async Task<string?> StopRecordingForPreviewStopAsync()
    {
        RecordingController? controller = _recordingController;
        if (controller is null || controller.IsDisposed)
        {
            return null;
        }

        ManagedRecordingSnapshot before = controller.RefreshSnapshot();
        if (!before.IsActive)
        {
            return null;
        }

        ManagedRecordingSnapshot final =
            await controller.StopForCloseAsync().ConfigureAwait(false);
        if (final.State != ManagedRecordingState.Failed)
        {
            return null;
        }
        return string.IsNullOrWhiteSpace(final.ErrorMessage)
            ? $"Recording stop failed: 0x{final.FailureHResult:X8}."
            : $"Recording stop failed: {final.ErrorMessage}";
    }

    private async Task<string?> StopCameraServiceAsync()
    {
        IPreviewCameraUpdateService? service = _cameraUpdates;
        if (service is null)
        {
            return null;
        }

        _cameraUpdates = null;
        service.StatePublished -= OnCameraStatePublished;
        service.FollowStatePublished -= OnFollowStatePublished;
        string? error = null;
        try
        {
            service.SetFollowEnabled(false);
            await service.StopAsync().ConfigureAwait(false);
        }
        catch (Exception stopError)
        {
            error = $"Camera service Stop failed: {stopError.Message}";
        }
        try
        {
            await service.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception disposeError)
        {
            error = CombineErrors(
                error,
                $"Camera service Dispose failed: {disposeError.Message}");
        }
        return error;
    }

    private async Task<NativeMethods.Result>
        ApplyLatestResizeUnderGateAsync()
    {
        if (_session is null)
        {
            return NativeMethods.Result.InvalidHandle;
        }

        int width;
        int height;
        long version;
        lock (_resizeGate)
        {
            width = _pendingWidth;
            height = _pendingHeight;
            version = _resizeVersion;
            if (version == 0 ||
                version <= _appliedResizeVersion ||
                width <= 0 ||
                height <= 0)
            {
                return NativeMethods.Result.Ok;
            }
        }

        NativeMethods.Result result = await Task.Run(
            () => _session.Resize(width, height)).ConfigureAwait(false);
        if (result == NativeMethods.Result.Ok)
        {
            lock (_resizeGate)
            {
                _appliedResizeVersion = Math.Max(
                    _appliedResizeVersion,
                    version);
            }
        }
        return result;
    }

    private void OnCameraStatePublished(
        CameraState state,
        NativeMethods.Result result) =>
        CameraStatePublished?.Invoke(state, result);

    private void OnFollowStatePublished(ComfortZoneFollowStep step) =>
        FollowStatePublished?.Invoke(step);

    private void PublishStartupDiagnostic(
        ManagedStartupDiagnosticEvent diagnostic)
    {
        try
        {
            _writeStartupDiagnostic?.Invoke(diagnostic);
        }
        catch
        {
            // Startup diagnostics cannot own preview lifecycle health.
        }
    }

    private string? TryReadNativeSessionGuid()
    {
        try
        {
            if (_session is null)
            {
                return null;
            }
            NativeMethods.PreviewStats stats = _session.GetStats();
            if (stats.SessionIdHigh == 0 && stats.SessionIdLow == 0)
            {
                return null;
            }
            byte[] bytes = new byte[16];
            BitConverter.TryWriteBytes(
                bytes.AsSpan(0, 8),
                stats.SessionIdHigh);
            BitConverter.TryWriteBytes(
                bytes.AsSpan(8, 8),
                stats.SessionIdLow);
            return new Guid(bytes).ToString("D").ToUpperInvariant();
        }
        catch
        {
            return null;
        }
    }

    private string? TryReadNativeHresult()
    {
        try
        {
            string detail = _session?.GetLastError() ?? string.Empty;
            Match match = Regex.Match(
                detail,
                @"HRESULT=0x[0-9A-Fa-f]{8}",
                RegexOptions.CultureInvariant);
            return match.Success
                ? "0x" + match.Value["HRESULT=0x".Length..].ToUpperInvariant()
                : null;
        }
        catch
        {
            return null;
        }
    }

    private string DescribeNativeFailure(
        string operation,
        NativeMethods.Result result)
    {
        string detail = _session?.GetLastError() ?? string.Empty;
        return string.IsNullOrWhiteSpace(detail)
            ? $"{operation} failed: {result}."
            : $"{operation} failed: {result}; {detail}";
    }

    private void SetPreviewAvailable(bool available)
    {
        try
        {
            _setPreviewAvailable(available);
        }
        catch
        {
            // Hotkey availability cannot own native preview health.
        }
    }

    private PreviewLifecycleResult Succeeded() =>
        new(
            PreviewLifecycleOperationStatus.Succeeded,
            State,
            LastError);

    private PreviewLifecycleResult NoChange() =>
        new(
            PreviewLifecycleOperationStatus.NoChange,
            State,
            LastError);

    private PreviewLifecycleResult Rejected(string error) =>
        new(
            PreviewLifecycleOperationStatus.Rejected,
            State,
            error);

    private PreviewLifecycleResult Failed(string error) =>
        new(
            PreviewLifecycleOperationStatus.Failed,
            State,
            error);

    private void SetLastError(string? error)
    {
        lock (_stateGate)
        {
            _lastError = error;
        }
    }

    private void SetState(
        PreviewLifecycleState state,
        string? error)
    {
        PreviewLifecycleSnapshot snapshot;
        lock (_stateGate)
        {
            _state = state;
            _lastError = error;
            snapshot = new PreviewLifecycleSnapshot(state, error);
        }
        PostStateChanged(snapshot);
    }

    private void PostStateChanged(PreviewLifecycleSnapshot snapshot)
    {
        Action<PreviewLifecycleSnapshot>? handler = StateChanged;
        if (handler is null)
        {
            return;
        }
        if (_notificationContext is not null)
        {
            _notificationContext.Post(
                static value =>
                {
                    var (callback, state) =
                        ((Action<PreviewLifecycleSnapshot>,
                          PreviewLifecycleSnapshot))value!;
                    callback(state);
                },
                (handler, snapshot));
        }
        else
        {
            ThreadPool.QueueUserWorkItem(
                static value =>
                {
                    var (callback, state) =
                        ((Action<PreviewLifecycleSnapshot>,
                          PreviewLifecycleSnapshot))value!;
                    callback(state);
                },
                (handler, snapshot));
        }
    }

    private static string? CombineErrors(params string?[] errors)
    {
        string[] actual = errors.
            Where(static error => !string.IsNullOrWhiteSpace(error)).
            Cast<string>().
            ToArray();
        return actual.Length == 0
            ? null
            : string.Join(" | ", actual);
    }

    private readonly record struct StartAttemptResult(
        bool Success,
        string? Error);
}

internal sealed class ManagedCloseDiagnostics
{
    private readonly long _requestTimestamp;

    internal ManagedCloseDiagnostics(
        string? sessionGuid,
        DateTimeOffset requestUtc,
        long requestTimestamp)
    {
        SessionGuid = sessionGuid;
        ManagedCloseRequestUtc = requestUtc;
        _requestTimestamp = requestTimestamp;
    }

    internal string? SessionGuid { get; }
    internal DateTimeOffset ManagedCloseRequestUtc { get; }
    internal DateTimeOffset ImmediateHideRequestedUtc { get; set; }
    internal DateTimeOffset ImmediateHideAppliedUtc { get; set; }
    internal bool VisibleAfterHide { get; set; }
    internal bool HandleCreatedAfterHide { get; set; }
    internal DateTimeOffset CleanupStartUtc { get; set; }
    internal DateTimeOffset CleanupEndUtc { get; set; }
    internal DateTimeOffset FinalClosePostedUtc { get; set; }
    internal DateTimeOffset? FormClosedUtc { get; private set; }
    internal double VisibleCloseLatencyMs { get; set; }
    internal double CleanupDurationMs { get; set; }
    internal double ManagedCloseDurationMs { get; set; }
    internal double? CloseRequestToFormClosedMs { get; private set; }
    internal int CleanupInvocationCount { get; set; }
    internal int HideInvocationCount { get; set; }
    internal int FinalCloseInvocationCount { get; set; }
    internal bool ClosingFeedbackShown { get; set; }
    internal bool CleanupSucceeded { get; set; }
    internal string? CleanupExceptionType { get; set; }

    internal void MarkFormClosed(DateTimeOffset formClosedUtc)
    {
        FormClosedUtc = formClosedUtc;
        CloseRequestToFormClosedMs = Stopwatch.GetElapsedTime(
            _requestTimestamp).TotalMilliseconds;
    }
}

internal sealed class ManagedCloseCoordinator
{
    private int _started;

    internal async Task<bool> TryExecuteAsync(
        string? sessionGuid,
        Action prepareForClose,
        Action hide,
        Func<bool> isVisible,
        Func<bool> isHandleCreated,
        Func<Task> cleanup,
        Action<ManagedCloseDiagnostics> beforeFinalClose,
        Action postFinalClose,
        DateTimeOffset? closeRequestUtc = null,
        long? closeRequestTimestamp = null)
    {
        if (Interlocked.CompareExchange(ref _started, 1, 0) != 0)
        {
            return false;
        }

        long requestTimestamp = closeRequestTimestamp ?? Stopwatch.GetTimestamp();
        ManagedCloseDiagnostics diagnostics = new(
            sessionGuid,
            closeRequestUtc ?? DateTimeOffset.UtcNow,
            requestTimestamp);

        prepareForClose();
        diagnostics.ImmediateHideRequestedUtc = DateTimeOffset.UtcNow;
        diagnostics.HideInvocationCount++;
        hide();
        long hideAppliedTimestamp = Stopwatch.GetTimestamp();
        diagnostics.ImmediateHideAppliedUtc = DateTimeOffset.UtcNow;
        diagnostics.VisibleAfterHide = isVisible();
        diagnostics.HandleCreatedAfterHide = isHandleCreated();
        diagnostics.VisibleCloseLatencyMs = Stopwatch.GetElapsedTime(
            requestTimestamp,
            hideAppliedTimestamp).TotalMilliseconds;

        long cleanupStarted = Stopwatch.GetTimestamp();
        diagnostics.CleanupStartUtc = DateTimeOffset.UtcNow;
        diagnostics.CleanupInvocationCount++;
        try
        {
            await cleanup();
            diagnostics.CleanupSucceeded = true;
        }
        catch (Exception error)
        {
            diagnostics.CleanupSucceeded = false;
            diagnostics.CleanupExceptionType = error.GetType().FullName;
        }
        finally
        {
            long cleanupEnded = Stopwatch.GetTimestamp();
            diagnostics.CleanupEndUtc = DateTimeOffset.UtcNow;
            diagnostics.CleanupDurationMs = Stopwatch.GetElapsedTime(
                cleanupStarted,
                cleanupEnded).TotalMilliseconds;
            diagnostics.ManagedCloseDurationMs = Stopwatch.GetElapsedTime(
                requestTimestamp,
                cleanupEnded).TotalMilliseconds;
            diagnostics.FinalClosePostedUtc = DateTimeOffset.UtcNow;
            diagnostics.FinalCloseInvocationCount++;
            beforeFinalClose(diagnostics);
            postFinalClose();
        }

        return true;
    }
}
