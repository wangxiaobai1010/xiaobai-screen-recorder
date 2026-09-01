namespace XbPreview.Host;

internal enum ManagedRecordingState
{
    Idle = 0,
    Starting = 1,
    Recording = 2,
    Stopping = 3,
    Completed = 4,
    Failed = 5,
    Pausing = 6,
    Paused = 7,
    Resuming = 8,
    UserCancelled = 9,
}

internal sealed record ManagedRecordingSnapshot(
    ManagedRecordingState State,
    NativeMethods.Result LastResult,
    DateTimeOffset? StartUtc,
    TimeSpan Elapsed,
    string SessionId,
    string OutputPath,
    string ErrorMessage,
    bool OutputSuccess,
    bool FinalizeAttempted,
    int FinalizeHResult,
    int FailureHResult,
    uint FinalizeCount,
    bool ActiveEncoder,
    uint ResidualOutstanding,
    bool OutputCleanupAttempted,
    bool OutputCleanupSucceeded,
    int OutputCleanupHResult,
    ulong FramesSubmitted,
    ulong PauseCount,
    TimeSpan TotalPaused)
{
    // OutputPath permanently retains the P2.5 direct-output compatibility
    // meaning and aliases WorkingPath. Successful-output consumers must use
    // PublishedPath so one field never has two meanings.
    public string WorkingPath { get; init; } = string.Empty;

    public string PlannedFinalPath { get; init; } = string.Empty;

    public string PublishedPath { get; init; } = string.Empty;

    public bool ReadyToPublish { get; init; }

    public bool Published { get; init; }

    public bool PublishAttempted { get; init; }

    public int PublishHResult { get; init; }

    public bool ValidationAttempted { get; init; }

    public int ValidationHResult { get; init; }

    internal static ManagedRecordingSnapshot Idle { get; } = new(
        ManagedRecordingState.Idle,
        NativeMethods.Result.Ok,
        null,
        TimeSpan.Zero,
        string.Empty,
        string.Empty,
        string.Empty,
        false,
        false,
        0,
        0,
        0,
        false,
        0,
        false,
        false,
        0,
        0,
        0,
        TimeSpan.Zero);

    internal bool IsActive => State is
        ManagedRecordingState.Starting or
        ManagedRecordingState.Recording or
        ManagedRecordingState.Pausing or
        ManagedRecordingState.Paused or
        ManagedRecordingState.Resuming or
        ManagedRecordingState.Stopping;

    internal bool IsTerminal => State is
        ManagedRecordingState.Completed or
        ManagedRecordingState.Failed or
        ManagedRecordingState.UserCancelled;
}

internal sealed record RecordingOutputPresentation(
    string PathText,
    string StatusText,
    bool CanOpenVideo,
    bool CanOpenFolder);

internal static class RecordingOutputActions
{
    internal static RecordingOutputPresentation Describe(
        ManagedRecordingSnapshot snapshot)
    {
        bool completed = snapshot.State == ManagedRecordingState.Completed &&
            snapshot.ReadyToPublish &&
            snapshot.Published &&
            snapshot.OutputSuccess;
        string path = completed
            ? snapshot.PublishedPath ?? string.Empty
            : snapshot.WorkingPath ?? string.Empty;
        bool fileExists = completed && FileExists(path);
        bool directoryExists = completed && DirectoryExists(path);
        string status = snapshot.State switch
        {
            ManagedRecordingState.Completed when fileExists =>
                "输出：已完成，可打开视频",
            ManagedRecordingState.Completed when directoryExists =>
                "输出：已完成，但视频文件当前不存在",
            ManagedRecordingState.Completed => "输出：已完成，但输出路径无效",
            ManagedRecordingState.Failed => "输出：失败",
            ManagedRecordingState.Starting or
                ManagedRecordingState.Recording or
                ManagedRecordingState.Pausing or
                ManagedRecordingState.Paused or
                ManagedRecordingState.Resuming or
                ManagedRecordingState.Stopping => "输出：录制中",
            _ => "输出：—",
        };
        return new RecordingOutputPresentation(
            string.IsNullOrWhiteSpace(path) ? "—" : path,
            status,
            fileExists && snapshot.OutputSuccess,
            directoryExists);
    }

    internal static bool CanOpenVideo(ManagedRecordingSnapshot snapshot) =>
        Describe(snapshot).CanOpenVideo;

    internal static bool CanOpenFolder(ManagedRecordingSnapshot snapshot) =>
        Describe(snapshot).CanOpenFolder;

    private static bool FileExists(string path)
    {
        try
        {
            return !string.IsNullOrWhiteSpace(path) && File.Exists(path);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static bool DirectoryExists(string path)
    {
        try
        {
            string? directory = Path.GetDirectoryName(path);
            return !string.IsNullOrWhiteSpace(directory) &&
                Directory.Exists(directory);
        }
        catch (Exception)
        {
            return false;
        }
    }
}

internal sealed class RecordingController : IAsyncDisposable
{
    private static readonly TimeSpan ClosePollInterval =
        TimeSpan.FromMilliseconds(25);
    private static readonly TimeSpan CloseTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan PauseResumePollInterval =
        TimeSpan.FromMilliseconds(10);
    private static readonly TimeSpan PauseResumeTimeout =
        TimeSpan.FromSeconds(5);
    private readonly IRecordingNativeSession _native;
    private readonly object _gate = new();
    private ManagedRecordingSnapshot _current =
        ManagedRecordingSnapshot.Idle;
    private Task<ManagedRecordingSnapshot>? _startTask;
    private Task<ManagedRecordingSnapshot>? _stopTask;
    private Task<ManagedRecordingSnapshot>? _pauseResumeTask;
    private PauseResumeOperation _pauseResumeOperation;
    private long _controlGeneration;
    private long _startAuthorizationGeneration;
    private long _pendingStartAuthorization;
    private string _acceptedSessionId = string.Empty;
    private bool _disposed;

    private enum TerminalDisposition
    {
        Complete,
        UserCancelled,
    }

    private enum SnapshotOrigin
    {
        OrdinaryRefresh,
        ExplicitStart,
        CommandResult,
    }

    private enum PauseResumeOperation
    {
        None,
        Pause,
        Resume,
    }

    internal RecordingController(IRecordingNativeSession native)
    {
        _native = native ?? throw new ArgumentNullException(nameof(native));
    }

    internal ManagedRecordingSnapshot CurrentSnapshot
    {
        get
        {
            lock (_gate)
            {
                return _current;
            }
        }
    }

    internal bool HasPendingOperation
    {
        get
        {
            lock (_gate)
            {
                return _startTask is not null || _stopTask is not null ||
                    _pauseResumeTask is not null;
            }
        }
    }

    internal bool IsDisposed
    {
        get
        {
            lock (_gate)
            {
                return _disposed;
            }
        }
    }

    internal NativeMethods.Result SetAudioControls(
        bool systemMuted,
        bool microphoneMuted,
        double microphoneGainDb)
    {
        lock (_gate)
        {
            ThrowIfDisposed();
        }
        return _native.SetAudioControls(
            systemMuted,
            microphoneMuted,
            microphoneGainDb);
    }

    internal NativeMethods.Result SetAudioProgramMode(
        NativeMethods.AudioProgramMode mode)
    {
        NativeMethods.Result result;
        lock (_gate)
        {
            ThrowIfDisposed();
        }
        result = _native.SetAudioProgramMode(mode);
        return result;
    }

    internal MicrophoneDeviceCatalog GetMicrophoneDevices()
    {
        lock (_gate)
        {
            ThrowIfDisposed();
        }
        return _native.GetMicrophoneDevices();
    }

    internal NativeMethods.Result SetMicrophoneSelection(
        MicrophoneSelection selection)
    {
        lock (_gate)
        {
            ThrowIfDisposed();
        }
        return _native.SetMicrophoneSelection(selection);
    }

    internal MicrophoneSelectionStatus GetMicrophoneSelection()
    {
        lock (_gate)
        {
            ThrowIfDisposed();
        }
        return _native.GetMicrophoneSelection();
    }

    internal NativeMethods.AudioControlSnapshotV1 GetAudioControlSnapshot()
    {
        lock (_gate)
        {
            ThrowIfDisposed();
        }
        return _native.GetAudioControlSnapshot();
    }

    internal ManagedRecordingSnapshot RefreshSnapshot()
    {
        lock (_gate)
        {
            ThrowIfDisposed();
        }

        try
        {
            ManagedRecordingSnapshot snapshot = Publish(
                Map(_native.GetRecordingSnapshot()),
                SnapshotOrigin.OrdinaryRefresh);
            if (snapshot.State == ManagedRecordingState.Stopping)
            {
                lock (_gate)
                {
                    if (!_disposed)
                    {
                        if (_stopTask is null)
                        {
                            ++_controlGeneration;
                        }
                        _ = EnsureStopTaskLocked(
                            TerminalDisposition.Complete);
                    }
                }
            }
            return snapshot;
        }
        catch (Exception error)
        {
            _ = error;
            return CurrentSnapshot;
        }
    }

    internal Task<ManagedRecordingSnapshot> StartAsync()
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            if (_startTask is not null)
            {
                return _startTask;
            }
            if (_stopTask is not null || _current.IsActive)
            {
                return Task.FromResult(_current);
            }

            long startAuthorization = ++_startAuthorizationGeneration;
            _pendingStartAuthorization = startAuthorization;
            Task<ManagedRecordingSnapshot> task = Task.Run(
                () => StartCore(startAuthorization));
            _startTask = task;
            _ = task.ContinueWith(
                completed => ClearStartTask(completed),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
            return task;
        }
    }

    internal Task<ManagedRecordingSnapshot> PauseAsync(
        CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            if (_stopTask is not null)
            {
                return Task.FromResult(_current);
            }
            if (_pauseResumeTask is not null)
            {
                return _pauseResumeOperation == PauseResumeOperation.Pause
                    ? _pauseResumeTask
                    : Task.FromResult(InvalidTransitionLocked(
                        "Pause is invalid while Resume is pending."));
            }
            if (_current.State == ManagedRecordingState.Paused)
            {
                return Task.FromResult(_current);
            }
            if (_current.State == ManagedRecordingState.Pausing)
            {
                return StartPauseResumeTaskLocked(
                    PauseResumeOperation.Pause,
                    issueCommand: false,
                    cancellationToken);
            }
            if (_current.State != ManagedRecordingState.Recording)
            {
                return Task.FromResult(InvalidTransitionLocked(
                    $"Pause is invalid from {_current.State}."));
            }
            return StartPauseResumeTaskLocked(
                PauseResumeOperation.Pause,
                issueCommand: true,
                cancellationToken);
        }
    }

    internal Task<ManagedRecordingSnapshot> ResumeAsync(
        CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            if (_stopTask is not null)
            {
                return Task.FromResult(_current);
            }
            if (_pauseResumeTask is not null)
            {
                return _pauseResumeOperation == PauseResumeOperation.Resume
                    ? _pauseResumeTask
                    : Task.FromResult(InvalidTransitionLocked(
                        "Resume is invalid while Pause is pending."));
            }
            if (_current.State == ManagedRecordingState.Resuming)
            {
                return StartPauseResumeTaskLocked(
                    PauseResumeOperation.Resume,
                    issueCommand: false,
                    cancellationToken);
            }
            if (_current.State != ManagedRecordingState.Paused)
            {
                return Task.FromResult(InvalidTransitionLocked(
                    $"Resume is invalid from {_current.State}."));
            }
            return StartPauseResumeTaskLocked(
                PauseResumeOperation.Resume,
                issueCommand: true,
                cancellationToken);
        }
    }

    private Task<ManagedRecordingSnapshot> StartPauseResumeTaskLocked(
        PauseResumeOperation operation,
        bool issueCommand,
        CancellationToken cancellationToken)
    {
        long generation = ++_controlGeneration;
        Task<ManagedRecordingSnapshot> task = Task.Run(
            () => PauseResumeCoreAsync(
                operation,
                issueCommand,
                generation,
                cancellationToken),
            CancellationToken.None);
        _pauseResumeOperation = operation;
        _pauseResumeTask = task;
        _ = task.ContinueWith(
            completed => ClearPauseResumeTask(completed),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        return task;
    }

    private async Task<ManagedRecordingSnapshot> PauseResumeCoreAsync(
        PauseResumeOperation operation,
        bool issueCommand,
        long generation,
        CancellationToken cancellationToken)
    {
        if (issueCommand)
        {
            NativeMethods.Result result;
            try
            {
                result = operation == PauseResumeOperation.Pause
                    ? _native.PauseRecording()
                    : _native.ResumeRecording();
            }
            catch (Exception error)
            {
                return PublishControlResult(
                    generation,
                    NativeMethods.Result.NativeFailure,
                    $"{operation} command failed: {error.Message}");
            }
            if (result != NativeMethods.Result.Ok)
            {
                return PublishControlResult(
                    generation,
                    result,
                    $"{operation} command failed: {result}; " +
                        SafeGetLastError());
            }
        }

        DateTimeOffset deadline = DateTimeOffset.UtcNow +
            PauseResumeTimeout;
        ManagedRecordingState acknowledgedState =
            operation == PauseResumeOperation.Pause
                ? ManagedRecordingState.Paused
                : ManagedRecordingState.Recording;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (ControlWasPreempted(generation))
            {
                return CurrentSnapshot;
            }

            ManagedRecordingSnapshot snapshot;
            try
            {
                snapshot = Map(_native.GetRecordingSnapshot());
            }
            catch (Exception error)
            {
                return PublishControlResult(
                    generation,
                    NativeMethods.Result.NativeFailure,
                    $"{operation} Snapshot acknowledgement failed: " +
                        error.Message);
            }
            snapshot = PublishControlSnapshot(generation, snapshot);
            if (snapshot.State == acknowledgedState ||
                snapshot.State == ManagedRecordingState.Stopping ||
                snapshot.IsTerminal)
            {
                return snapshot;
            }
            if (DateTimeOffset.UtcNow >= deadline)
            {
                return PublishControlResult(
                    generation,
                    NativeMethods.Result.Timeout,
                    $"Timed out waiting for native {operation} acknowledgement.");
            }
            await Task.Delay(
                PauseResumePollInterval,
                cancellationToken).ConfigureAwait(false);
        }
    }

    internal Task<ManagedRecordingSnapshot> StopAsync()
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            if (_stopTask is not null)
            {
                return _stopTask;
            }
            if (_current.State is not (
                ManagedRecordingState.Starting or
                ManagedRecordingState.Recording or
                ManagedRecordingState.Pausing or
                ManagedRecordingState.Paused or
                ManagedRecordingState.Resuming or
                ManagedRecordingState.Stopping))
            {
                return Task.FromResult(_current);
            }

            ++_controlGeneration;
            return EnsureStopTaskLocked(TerminalDisposition.Complete);
        }
    }

    internal Task<ManagedRecordingSnapshot> CancelAsync()
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            if (_stopTask is not null)
            {
                return _stopTask;
            }
            if (_current.State is not (
                ManagedRecordingState.Recording or
                ManagedRecordingState.Pausing or
                ManagedRecordingState.Paused or
                ManagedRecordingState.Resuming))
            {
                return Task.FromResult(_current);
            }

            ++_controlGeneration;
            return EnsureStopTaskLocked(TerminalDisposition.UserCancelled);
        }
    }

    private Task<ManagedRecordingSnapshot> EnsureStopTaskLocked(
        TerminalDisposition disposition)
    {
        if (_stopTask is not null)
        {
            return _stopTask;
        }
        Task<ManagedRecordingSnapshot> task = Task.Run(
            () => StopCore(disposition));
        _stopTask = task;
        _ = task.ContinueWith(
            completed => ClearStopTask(completed),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        return task;
    }

    internal async Task<ManagedRecordingSnapshot> StopForCloseAsync()
    {
        Task<ManagedRecordingSnapshot>? pendingStart;
        lock (_gate)
        {
            if (_disposed)
            {
                return _current;
            }
            pendingStart = _startTask;
        }
        if (pendingStart is not null)
        {
            await pendingStart.ConfigureAwait(false);
        }

        ManagedRecordingSnapshot snapshot = RefreshSnapshot();
        if (snapshot.State is ManagedRecordingState.Starting or
            ManagedRecordingState.Recording or
            ManagedRecordingState.Pausing or
            ManagedRecordingState.Paused or
            ManagedRecordingState.Resuming)
        {
            ManagedRecordingSnapshot stopped =
                await StopAsync().ConfigureAwait(false);
            await AwaitPauseResumeForCloseAsync().ConfigureAwait(false);
            return stopped;
        }

        if (snapshot.State != ManagedRecordingState.Stopping)
        {
            return snapshot;
        }

        Task<ManagedRecordingSnapshot>? pendingStop;
        lock (_gate)
        {
            pendingStop = _stopTask;
        }
        if (pendingStop is not null)
        {
            ManagedRecordingSnapshot stopped =
                await pendingStop.ConfigureAwait(false);
            await AwaitPauseResumeForCloseAsync().ConfigureAwait(false);
            return stopped;
        }

        DateTimeOffset deadline = DateTimeOffset.UtcNow + CloseTimeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(ClosePollInterval).ConfigureAwait(false);
            snapshot = RefreshSnapshot();
            if (snapshot.State != ManagedRecordingState.Stopping)
            {
                await AwaitPauseResumeForCloseAsync().ConfigureAwait(false);
                return snapshot;
            }
        }
        return PublishManagedFailure(
            "Timed out waiting for native recording stop.");
    }

    private async Task AwaitPauseResumeForCloseAsync()
    {
        Task<ManagedRecordingSnapshot>? pending;
        lock (_gate)
        {
            pending = _pauseResumeTask;
        }
        if (pending is null)
        {
            return;
        }
        try
        {
            _ = await pending.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception)
        {
            // Stop/Close owns the terminal result. A preempted control task
            // must never replace it or prevent disposal.
        }
    }

    public async ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }
        }

        await StopForCloseAsync().ConfigureAwait(false);
        lock (_gate)
        {
            _disposed = true;
        }
    }

    private ManagedRecordingSnapshot StartCore(long startAuthorization)
    {
        NativeMethods.Result result;
        try
        {
            result = _native.StartRecording();
        }
        catch (Exception error)
        {
            return PublishManagedFailure(
                $"StartRecording failed: {error.Message}");
        }
        try
        {
            ManagedRecordingSnapshot snapshot = Map(
                _native.GetRecordingSnapshot());
            if (result != NativeMethods.Result.Ok &&
                snapshot.State != ManagedRecordingState.Failed)
            {
                return PublishManagedFailure(
                    $"StartRecording failed: {result}; {_native.GetLastError()}",
                    result);
            }
            if (result != NativeMethods.Result.Ok)
            {
                return PublishManagedFailure(
                    string.IsNullOrWhiteSpace(snapshot.ErrorMessage)
                        ? $"StartRecording failed: {result}"
                        : snapshot.ErrorMessage,
                    result);
            }
            return Publish(
                snapshot,
                SnapshotOrigin.ExplicitStart,
                startAuthorization);
        }
        catch (Exception error)
        {
            return result == NativeMethods.Result.Ok
                ? CurrentSnapshot
                : PublishManagedFailure(
                    $"StartRecording failed: {result}; {error.Message}",
                    result);
        }
    }

    private ManagedRecordingSnapshot StopCore(
        TerminalDisposition disposition)
    {
        bool userCancelled = disposition == TerminalDisposition.UserCancelled;
        string operation = userCancelled
            ? "CancelRecording"
            : "StopRecording";
        NativeMethods.Result result;
        try
        {
            result = userCancelled
                ? _native.CancelRecording()
                : _native.StopRecording();
        }
        catch (Exception error)
        {
            return PublishManagedFailure(
                $"{operation} failed: {error.Message}");
        }
        try
        {
            ManagedRecordingSnapshot snapshot = Map(
                _native.GetRecordingSnapshot());
            if (result != NativeMethods.Result.Ok &&
                snapshot.State != ManagedRecordingState.Failed)
            {
                return PublishManagedFailure(
                    $"{operation} failed: {result}; {_native.GetLastError()}",
                    result);
            }
            return Publish(snapshot, SnapshotOrigin.CommandResult);
        }
        catch (Exception error)
        {
            return result == NativeMethods.Result.Ok
                ? CurrentSnapshot
                : PublishManagedFailure(
                    $"{operation} failed: {result}; {error.Message}",
                    result);
        }
    }

    private ManagedRecordingSnapshot Publish(
        ManagedRecordingSnapshot snapshot,
        SnapshotOrigin origin,
        long startAuthorization = 0)
    {
        lock (_gate)
        {
            if (!AcceptSessionIdentityLocked(
                    snapshot.SessionId,
                    origin,
                    startAuthorization))
            {
                return _current;
            }
            if (_current.IsTerminal && !snapshot.IsTerminal &&
                string.Equals(
                    _current.SessionId,
                    snapshot.SessionId,
                    StringComparison.Ordinal))
            {
                return _current;
            }
            _current = snapshot;
            return snapshot;
        }
    }

    private ManagedRecordingSnapshot PublishControlSnapshot(
        long generation,
        ManagedRecordingSnapshot snapshot)
    {
        lock (_gate)
        {
            if (generation != _controlGeneration ||
                _stopTask is not null ||
                _current.State == ManagedRecordingState.Stopping ||
                _current.IsTerminal ||
                !IsAcceptedSessionLocked(snapshot.SessionId))
            {
                return _current;
            }
            _current = snapshot;
            return snapshot;
        }
    }

    private ManagedRecordingSnapshot PublishControlResult(
        long generation,
        NativeMethods.Result result,
        string error)
    {
        lock (_gate)
        {
            if (generation != _controlGeneration || _stopTask is not null)
            {
                return _current;
            }
            _current = _current with
            {
                LastResult = result,
                ErrorMessage = error,
            };
            return _current;
        }
    }

    private ManagedRecordingSnapshot InvalidTransitionLocked(string error) =>
        _current with
        {
            LastResult = NativeMethods.Result.InvalidState,
            ErrorMessage = error,
        };

    private bool ControlWasPreempted(long generation)
    {
        lock (_gate)
        {
            return generation != _controlGeneration ||
                _stopTask is not null ||
                _current.State == ManagedRecordingState.Stopping ||
                _current.IsTerminal;
        }
    }

    private string SafeGetLastError()
    {
        try
        {
            return _native.GetLastError();
        }
        catch (Exception error)
        {
            return error.Message;
        }
    }

    private ManagedRecordingSnapshot PublishManagedFailure(
        string error,
        NativeMethods.Result result = NativeMethods.Result.NativeFailure)
    {
        lock (_gate)
        {
            _current = _current with
            {
                State = ManagedRecordingState.Failed,
                LastResult = result,
                ErrorMessage = error,
                OutputSuccess = false,
            };
            return _current;
        }
    }

    private void ClearStartTask(Task<ManagedRecordingSnapshot> completed)
    {
        lock (_gate)
        {
            if (ReferenceEquals(_startTask, completed))
            {
                _startTask = null;
                _pendingStartAuthorization = 0;
            }
        }
    }

    private bool AcceptSessionIdentityLocked(
        string sessionId,
        SnapshotOrigin origin,
        long startAuthorization)
    {
        if (_pendingStartAuthorization != 0)
        {
            if (origin != SnapshotOrigin.ExplicitStart ||
                startAuthorization != _pendingStartAuthorization ||
                string.IsNullOrEmpty(sessionId) ||
                IsAcceptedSessionLocked(sessionId))
            {
                return false;
            }

            _acceptedSessionId = sessionId;
            _pendingStartAuthorization = 0;
            return true;
        }
        if (string.IsNullOrEmpty(sessionId))
        {
            return string.IsNullOrEmpty(_acceptedSessionId);
        }
        return IsAcceptedSessionLocked(sessionId);
    }

    private bool IsAcceptedSessionLocked(string sessionId) =>
        !string.IsNullOrEmpty(_acceptedSessionId) &&
        string.Equals(
            _acceptedSessionId,
            sessionId,
            StringComparison.Ordinal);

    private void ClearStopTask(Task<ManagedRecordingSnapshot> completed)
    {
        lock (_gate)
        {
            if (ReferenceEquals(_stopTask, completed))
            {
                _stopTask = null;
            }
        }
    }

    private void ClearPauseResumeTask(
        Task<ManagedRecordingSnapshot> completed)
    {
        lock (_gate)
        {
            if (ReferenceEquals(_pauseResumeTask, completed))
            {
                _pauseResumeTask = null;
                _pauseResumeOperation = PauseResumeOperation.None;
            }
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(RecordingController));
        }
    }

    private static ManagedRecordingSnapshot Map(
        NativeMethods.RecordingSnapshot native)
    {
        DateTimeOffset? startUtc = null;
        if (native.StartUtc100ns > 0)
        {
            try
            {
                startUtc = DateTimeOffset.FromFileTime(native.StartUtc100ns);
            }
            catch (ArgumentOutOfRangeException)
            {
            }
        }

        return new ManagedRecordingSnapshot(
            MapState(native.State, native.LastResult),
            native.LastResult,
            startUtc,
            TimeSpan.FromTicks(Math.Max(0, native.Elapsed100ns)),
            native.GetSessionId(),
            native.GetOutputPath(),
            native.GetErrorMessage(),
            native.OutputSuccess != 0,
            native.FinalizeAttempted != 0,
            native.FinalizeHResult,
            native.FailureHResult,
            native.FinalizeCount,
            native.ActiveEncoder != 0,
            native.ResidualOutstanding,
            native.OutputCleanupAttempted != 0,
            native.OutputCleanupSucceeded != 0,
            native.OutputCleanupHResult,
            native.FramesSubmitted,
            native.PauseCount,
            TimeSpan.FromTicks(
                native.TotalPaused100ns > long.MaxValue
                    ? long.MaxValue
                    : (long)native.TotalPaused100ns))
        {
            WorkingPath = native.GetWorkingPath(),
            PlannedFinalPath = native.GetPlannedFinalPath(),
            PublishedPath = native.GetPublishedPath(),
            ReadyToPublish = native.ReadyToPublish != 0,
            Published = native.Published != 0,
            PublishAttempted = native.PublishAttempted != 0,
            PublishHResult = native.PublishHResult,
            ValidationAttempted = native.ValidationAttempted != 0,
            ValidationHResult = native.ValidationHResult,
        };
    }

    private static ManagedRecordingState MapState(
        NativeMethods.RecordingState state,
        NativeMethods.Result lastResult)
    {
        if (state == NativeMethods.RecordingState.UserCancelled &&
            lastResult != NativeMethods.Result.Ok)
        {
            return ManagedRecordingState.Failed;
        }

        return state switch
        {
            NativeMethods.RecordingState.Idle => ManagedRecordingState.Idle,
            NativeMethods.RecordingState.Starting =>
                ManagedRecordingState.Starting,
            NativeMethods.RecordingState.Recording =>
                ManagedRecordingState.Recording,
            NativeMethods.RecordingState.Stopping =>
                ManagedRecordingState.Stopping,
            NativeMethods.RecordingState.Completed =>
                ManagedRecordingState.Completed,
            NativeMethods.RecordingState.Failed =>
                ManagedRecordingState.Failed,
            NativeMethods.RecordingState.Pausing =>
                ManagedRecordingState.Pausing,
            NativeMethods.RecordingState.Paused =>
                ManagedRecordingState.Paused,
            NativeMethods.RecordingState.Resuming =>
                ManagedRecordingState.Resuming,
            NativeMethods.RecordingState.UserCancelled =>
                ManagedRecordingState.UserCancelled,
            _ => ManagedRecordingState.Failed,
        };
    }
}

internal static class MicrophoneAvailabilityContract
{
    internal const string ErrorCode = "MicUnavailableAtStart";
    internal const string UserMessage =
        "当前选择的麦克风不可用，请重新连接或选择其他麦克风。";
}

internal static class RecordingFailurePresentation
{
    internal static string Describe(ManagedRecordingSnapshot snapshot)
    {
        if (snapshot.ErrorMessage.StartsWith(
                MicrophoneAvailabilityContract.ErrorCode,
                StringComparison.Ordinal))
        {
            return MicrophoneAvailabilityContract.UserMessage;
        }
        int code = snapshot.FailureHResult & 0xFFFF;
        return code switch
        {
            112 or 39 =>
                "磁盘空间不足，录制已安全停止；已有素材会保留。",
            5 or 19 =>
                "录制目录不可写，请检查目录权限或磁盘写保护。",
            2 or 3 or 21 or 1167 =>
                "录制目录或磁盘当前不可用；已有素材会保留。",
            1117 =>
                "写入录制磁盘时发生错误；已有素材会保留。",
            _ when !string.IsNullOrWhiteSpace(snapshot.ErrorMessage) =>
                snapshot.ErrorMessage,
            _ => "录制未能完成，请检查录制目录和可用磁盘空间。",
        };
    }
}
