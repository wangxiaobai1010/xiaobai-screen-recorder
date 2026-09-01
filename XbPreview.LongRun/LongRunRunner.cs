using System.Diagnostics;
using XbPreview.Host;

namespace XbPreview.LongRun;

internal enum LongRunExitCode
{
    Pass = 0,
    InvalidArguments = 2,
    PreviewStartFailed = 3,
    RecordingStartFailed = 4,
    RecordingFailed = 5,
    StopOrFinalizeFailed = 6,
    OutputValidationFailed = 7,
    ResourceResidual = 8,
    CanceledSafely = 9,
    UnhandledException = 10,
    EvidenceGateFailed = 11,
    SummaryPublishFailed = 12,
}

internal sealed class LongRunFailureException : Exception
{
    internal LongRunFailureException(LongRunExitCode exitCode, string message)
        : base(message) => ExitCode = exitCode;

    internal LongRunExitCode ExitCode { get; }
}

internal enum LongRunEndReason
{
    None = 0,
    DurationReached = 1,
    CancellationRequested = 2,
    RuntimeFailure = 3,
}

internal sealed class LongRunEndReasonLatch
{
    private int _value;

    internal LongRunEndReason Value =>
        (LongRunEndReason)Volatile.Read(ref _value);

    internal bool TrySet(LongRunEndReason reason)
    {
        if (reason == LongRunEndReason.None)
        {
            throw new ArgumentOutOfRangeException(nameof(reason));
        }
        return Interlocked.CompareExchange(
            ref _value,
            (int)reason,
            (int)LongRunEndReason.None) == (int)LongRunEndReason.None;
    }
}

internal sealed class LongRunTerminationCoordinator : IDisposable
{
    private readonly LongRunEndReasonLatch _endReason = new();
    private readonly CancellationToken _cancellationToken;
    private readonly Action<LongRunStage>? _stageObserver;
    private readonly Action<string>? _diagnostic;
    private readonly CancellationTokenRegistration _cancellationRegistration;
    private int _cancellationObserved;
    private int _runtimeFailureObserved;
    private int _stageObserverFailureRecorded;
    private int _sealed;

    internal LongRunTerminationCoordinator(
        CancellationToken cancellationToken,
        Action<LongRunStage>? stageObserver = null,
        Action<string>? diagnostic = null)
    {
        _cancellationToken = cancellationToken;
        _stageObserver = stageObserver;
        _diagnostic = diagnostic;
        _cancellationRegistration = cancellationToken.Register(
            static state =>
                ((LongRunTerminationCoordinator)state!).ObserveCancellation(),
            this);
    }

    internal LongRunEndReason EndReason => _endReason.Value;

    internal bool CancellationObserved =>
        Volatile.Read(ref _cancellationObserved) != 0;

    internal bool RuntimeFailureObserved =>
        Volatile.Read(ref _runtimeFailureObserved) != 0;

    internal bool MarkDurationReached() =>
        _endReason.TrySet(LongRunEndReason.DurationReached);

    internal bool CompleteTimedRun()
    {
        bool won = MarkDurationReached();
        NotifyStage(LongRunStage.TimedRunCompleted);
        return won;
    }

    internal void ObserveCancellation()
    {
        Interlocked.Exchange(ref _cancellationObserved, 1);
        _endReason.TrySet(LongRunEndReason.CancellationRequested);
    }

    internal void MarkRuntimeFailure()
    {
        Interlocked.Exchange(ref _runtimeFailureObserved, 1);
        _endReason.TrySet(LongRunEndReason.RuntimeFailure);
    }

    internal void NotifyStage(LongRunStage stage)
    {
        try
        {
            _stageObserver?.Invoke(stage);
        }
        catch (Exception error)
        {
            MarkRuntimeFailure();
            if (Interlocked.CompareExchange(
                    ref _stageObserverFailureRecorded,
                    1,
                    0) == 0)
            {
                _diagnostic?.Invoke(
                    $"Long-run stage observer failed at {stage}: " +
                    $"{error.GetType().Name}: {error.Message}");
            }
        }
    }

    internal void Seal()
    {
        if (Interlocked.CompareExchange(ref _sealed, 1, 0) != 0)
        {
            return;
        }
        _cancellationRegistration.Dispose();
        if (_cancellationToken.IsCancellationRequested)
        {
            ObserveCancellation();
        }
    }

    public void Dispose() => Seal();
}

internal enum LongRunStage
{
    RunStarted,
    PreviewStarted,
    RecordingStarted,
    TimedRunCompleted,
    StopStarted,
    StopCompleted,
    TerminalSnapshotReadStarted,
    TerminalSnapshotRead,
    PreviewCloseStarted,
    PreviewCloseCompleted,
    RunFinalizing,
}

internal sealed record PreviewCloseEvidence(
    bool InvocationCompleted,
    PreviewLifecycleState FinalState,
    string LastError,
    string ExceptionDetail)
{
    internal static PreviewCloseEvidence NotAttempted { get; } = new(
        false,
        PreviewLifecycleState.NotInitialized,
        string.Empty,
        string.Empty);

    internal bool Passed =>
        InvocationCompleted &&
        FinalState == PreviewLifecycleState.Disposed &&
        LastError.Length == 0 &&
        ExceptionDetail.Length == 0;

    internal string Describe() =>
        $"invocationCompleted={InvocationCompleted}; " +
        $"finalState={FinalState}; " +
        $"lastError={(LastError.Length == 0 ? "<empty>" : LastError)}; " +
        $"exception={(ExceptionDetail.Length == 0 ? "<none>" : ExceptionDetail)}";
}

internal sealed class LongRunRunner
{
    private static readonly TimeSpan RecordingStartTimeout =
        TimeSpan.FromSeconds(15);
    private static readonly TimeSpan TerminalSnapshotTimeout =
        TimeSpan.FromSeconds(30);
    private readonly LongRunOptions _options;
    private readonly Form _window;
    private readonly Panel _surface;
    private readonly CancellationTokenSource _cancellationSource;
    private readonly CancellationToken _cancellationToken;
    private readonly Action<LongRunStage>? _stageObserver;
    private LongRunTerminationCoordinator? _termination;
    private readonly RunObservations _observations = new();
    private readonly LongRunSummary _summary;
    private EvidenceWriter? _evidence;
    private PreviewLifecycleController? _lifecycle;
    private RecordingController? _recording;
    private CameraDiagnosticLogger? _cameraLogger;
    private ComfortZoneDiagnosticLogger? _followLogger;
    private string _diagnosticsDirectory = string.Empty;
    private Stopwatch? _recordingClock;
    private int _missedPeriodicSamples;
    private double _actualRecordingWallSeconds;
    private bool _terminalSnapshotReadAfterStop;
    private bool _previewClosed;
    private bool _durationTargetReached;
    private PreviewCloseEvidence _previewCloseEvidence =
        PreviewCloseEvidence.NotAttempted;
    private ManagedRecordingSnapshot _terminalSnapshot =
        ManagedRecordingSnapshot.Idle;
    private LoadedModuleEvidence? _loadedModules;
    private GitWorkspaceFacts? _gitStart;
    private GitWorkspaceFacts? _gitEnd;
    private string? _gitCaptureError;
    private bool _evidencePathsGitSafe;
    private RelatedProcessEvidence? _relatedProcessesAtStart;
    private RelatedProcessEvidence? _relatedProcessesAtEnd;
    private RelatedProcessEvidence? _relatedProcessesBeforePreviewClose;
    private RelatedProcessEvidence? _relatedProcessesAfterPreviewClose;
    private ProcessMetrics? _baselineMetrics;
    private ProcessMetrics? _finalMetrics;
    private EvidenceFileValidation _jsonlValidation = new(
        false,
        false,
        false,
        false,
        0,
        0,
        0,
        false,
        "JSONL evidence was not initialized.");

    internal LongRunRunner(
        LongRunOptions options,
        Form window,
        Panel surface,
        CancellationTokenSource cancellationSource,
        Action<LongRunStage>? stageObserver = null)
    {
        _options = options;
        _window = window;
        _surface = surface;
        _cancellationSource = cancellationSource;
        _cancellationToken = cancellationSource.Token;
        _stageObserver = stageObserver;
        _summary = new LongRunSummary
        {
            Parameters = options,
            StartUtc = DateTimeOffset.UtcNow,
            ProcessId = Environment.ProcessId,
            ProcessName = Path.GetFileNameWithoutExtension(
                Environment.ProcessPath) ?? "unavailable",
        };
    }

    internal LongRunEndReason EndReason =>
        _termination?.EndReason ?? LongRunEndReason.None;

    internal bool CancellationObserved =>
        _termination?.CancellationObserved == true;

    internal PreviewCloseEvidence CloseEvidence => _previewCloseEvidence;

    internal async Task<LongRunExitCode> RunAsync()
    {
        using LongRunTerminationCoordinator termination = new(
            _cancellationToken,
            _stageObserver,
            diagnostic => _summary.Reasons.Add(diagnostic));
        _termination = termination;
        LongRunExitCode executionExitCode = LongRunExitCode.UnhandledException;
        NotifyStage(LongRunStage.RunStarted);
        try
        {
            CaptureStartFacts();
            PrepareEvidence();
            await StartPreviewAsync();
            NotifyStage(LongRunStage.PreviewStarted);
            _loadedModules = LoadedModuleEvidence.Capture();
            if (!_loadedModules.Complete)
            {
                throw new LongRunFailureException(
                    LongRunExitCode.EvidenceGateFailed,
                    $"Loaded-module verification failed: {_loadedModules.Error}");
            }
            _terminalSnapshot = await StartAndRunRecordingAsync();
            executionExitCode = LongRunExitCode.Pass;
        }
        catch (OperationCanceledException error)
        {
            if (_cancellationToken.IsCancellationRequested)
            {
                termination.ObserveCancellation();
            }
            else
            {
                MarkRuntimeFailure();
                _summary.Reasons.Add(
                    $"Unexpected cancellation exception: {error.Message}");
            }
            if (termination.EndReason == LongRunEndReason.CancellationRequested)
            {
                _summary.Reasons.Add(
                    "Cancellation requested; formal StopForCloseAsync was entered.");
            }
            _terminalSnapshot = await SafeStopAndReadTerminalAsync();
            executionExitCode = termination.EndReason ==
                LongRunEndReason.CancellationRequested
                ? LongRunExitCode.CanceledSafely
                : LongRunExitCode.UnhandledException;
        }
        catch (LongRunFailureException failure)
        {
            MarkRuntimeFailure();
            _summary.Reasons.Add(failure.Message);
            executionExitCode = failure.ExitCode;
            _terminalSnapshot = await SafeStopAndReadTerminalAsync();
        }
        catch (Exception error)
        {
            MarkRuntimeFailure();
            _summary.Reasons.Add(
                $"Unhandled {error.GetType().Name}: {error.Message}");
            executionExitCode = LongRunExitCode.UnhandledException;
            _terminalSnapshot = await SafeStopAndReadTerminalAsync();
        }
        finally
        {
            await ClosePreviewAsync();
            DisposeDiagnosticLoggers();
            CaptureEndFacts();
            CloseAndValidateJsonl();
        }

        NotifyStage(LongRunStage.RunFinalizing);
        termination.Seal();
        if (termination.EndReason == LongRunEndReason.None)
        {
            termination.MarkRuntimeFailure();
            _summary.Reasons.Add(
                "No atomic end reason was recorded before finalization.");
        }
        LongRunEndReason endReason = termination.EndReason;
        if (executionExitCode == LongRunExitCode.Pass)
        {
            executionExitCode = endReason switch
            {
                LongRunEndReason.DurationReached => LongRunExitCode.Pass,
                LongRunEndReason.CancellationRequested =>
                    LongRunExitCode.CanceledSafely,
                _ => LongRunExitCode.UnhandledException,
            };
        }
        _summary.Reasons.Add($"Long-run end reason: {endReason}.");

        string sourceReaderValidation =
            EvidenceWriter.ReadSourceReaderValidation(
                _diagnosticsDirectory,
                _terminalSnapshot.SessionId);
        GitReproducibility git = GitReproducibility.Compare(
            _gitStart,
            _gitEnd,
            _gitCaptureError,
            _evidencePathsGitSafe);
        GateEvaluation gates = GateEvaluation.Create(
            endReason == LongRunEndReason.CancellationRequested,
            _options.DurationSeconds,
            _actualRecordingWallSeconds,
            _terminalSnapshot,
            _terminalSnapshotReadAfterStop,
            sourceReaderValidation,
            _jsonlValidation,
            _observations,
            _previewCloseEvidence,
            _relatedProcessesAfterPreviewClose,
            _relatedProcessesAtStart,
            _relatedProcessesAtEnd,
            _loadedModules,
            git,
            _baselineMetrics,
            _finalMetrics);

        LongRunExitCode finalExitCode = ResolveExitCode(
            executionExitCode,
            gates.Passed);
        PopulateSummary(
            finalExitCode,
            sourceReaderValidation,
            git,
            gates);

        return LongRunResultPublisher.Publish(
            _options.SummaryJsonPath,
            _summary,
            finalExitCode,
            Console.Out,
            Console.Error);
    }

    private void CaptureStartFacts()
    {
        string repositoryRoot = RepositoryFacts.FindRepositoryRoot();
        try
        {
            _gitStart = GitWorkspaceFacts.Capture(repositoryRoot);
            _evidencePathsGitSafe =
                RepositoryFacts.IsEvidencePathGitSafe(
                    repositoryRoot,
                    _options.SnapshotsJsonlPath) &&
                RepositoryFacts.IsEvidencePathGitSafe(
                    repositoryRoot,
                    _options.SummaryJsonPath) &&
                RepositoryFacts.IsEvidencePathGitSafe(
                    repositoryRoot,
                    _options.RunDirectory);
            if (!_evidencePathsGitSafe)
            {
                _gitCaptureError =
                    "Evidence paths inside the repository must be Git-ignored.";
            }
        }
        catch (Exception error)
        {
            _gitCaptureError =
                $"Git start capture failed: {error.GetType().Name}: {error.Message}";
        }
        _relatedProcessesAtStart =
            RelatedProcessEvidence.Capture(Environment.ProcessId);
        _baselineMetrics = ProcessMetrics.Read("baseline-before-preview");
        _observations.AddProcessMetrics(_baselineMetrics);
    }

    private void CaptureEndFacts()
    {
        _finalMetrics = ProcessMetrics.Read("final-after-preview-close");
        _observations.AddProcessMetrics(_finalMetrics);
        _relatedProcessesAtEnd =
            RelatedProcessEvidence.Capture(Environment.ProcessId);
        try
        {
            _gitEnd = GitWorkspaceFacts.Capture(
                RepositoryFacts.FindRepositoryRoot());
        }
        catch (Exception error)
        {
            string endError =
                $"Git end capture failed: {error.GetType().Name}: {error.Message}";
            _gitCaptureError = string.IsNullOrWhiteSpace(_gitCaptureError)
                ? endError
                : $"{_gitCaptureError}; {endError}";
        }
    }

    private void PrepareEvidence()
    {
        Directory.CreateDirectory(_options.OutputBaseDirectory);
        if (Directory.Exists(_options.RunDirectory) ||
            File.Exists(_options.RunDirectory))
        {
            throw new LongRunFailureException(
                LongRunExitCode.InvalidArguments,
                $"Run directory appeared after argument validation: {_options.RunDirectory}");
        }
        Directory.CreateDirectory(_options.RunDirectory);
        _diagnosticsDirectory = Path.Combine(
            _options.RunDirectory,
            "diagnostic-logs",
            "level-1",
            "level-2",
            "level-3");
        Directory.CreateDirectory(_diagnosticsDirectory);
        _evidence = new EvidenceWriter(_options.SnapshotsJsonlPath);
    }

    private async Task StartPreviewAsync()
    {
        if (!_window.IsHandleCreated || !_surface.IsHandleCreated ||
            _surface.ClientSize.Width <= 0 ||
            _surface.ClientSize.Height <= 0)
        {
            throw new LongRunFailureException(
                LongRunExitCode.PreviewStartFailed,
                "Preview window handles are not ready.");
        }

        _cameraLogger = new CameraDiagnosticLogger(_diagnosticsDirectory);
        _followLogger = new ComfortZoneDiagnosticLogger(
            _diagnosticsDirectory);
        FixedTargetCameraController camera = new();
        _lifecycle = new PreviewLifecycleController(
            () => NativePreviewSession.Create(
                _surface.Handle,
                _window.Handle,
                _diagnosticsDirectory),
            (session, followEnabled) => new CameraUpdateService(
                camera,
                session,
                _cameraLogger,
                _followLogger,
                followEnabled),
            camera,
            _ => { },
            notificationContext: SynchronizationContext.Current);

        EnsurePreviewResult(
            await _lifecycle.InitializeAsync(),
            "Initialize");
        CaptureDisplaySnapshot display =
            new DisplayGeometryProvider().ReadPrimaryDisplay();
        EnsurePreviewResult(
            await _lifecycle.SetDesiredGeometryAsync(
                SessionGeometry.CreateFullScreen(display)),
            "SetDesiredGeometry");
        EnsurePreviewResult(
            await _lifecycle.RequestResizeAsync(
                _surface.ClientSize.Width,
                _surface.ClientSize.Height),
            "Resize");
        EnsurePreviewResult(
            await _lifecycle.StartAsync(
                cameraEnabled: true,
                followEnabled: true,
                NativeMethods.CursorMode.CustomCursor),
            "Start");
        if (_lifecycle.State != PreviewLifecycleState.Previewing)
        {
            throw new LongRunFailureException(
                LongRunExitCode.PreviewStartFailed,
                $"Preview did not reach Previewing; state={_lifecycle.State}.");
        }
        _recording = _lifecycle.GetOrCreateRecordingController();
        NativeMethods.AudioProgramMode audioMode =
            ResolveRequestedAudioProgramMode();
        NativeMethods.Result audioModeResult =
            _recording.SetAudioProgramMode(audioMode);
        if (audioModeResult != NativeMethods.Result.Ok)
        {
            throw new LongRunFailureException(
                LongRunExitCode.RecordingStartFailed,
                $"Audio program mode was rejected: {audioModeResult}.");
        }
        string? requestedMicrophoneEndpoint =
            Environment.GetEnvironmentVariable(
                "XB_PREVIEW_TEST_MICROPHONE_ENDPOINT_ID");
        if (!string.IsNullOrWhiteSpace(requestedMicrophoneEndpoint))
        {
            MicrophoneDevice? selected = _recording.GetMicrophoneDevices().
                Devices.FirstOrDefault(device => string.Equals(
                    device.EndpointId,
                    requestedMicrophoneEndpoint,
                    StringComparison.Ordinal));
            if (selected is null)
            {
                throw new LongRunFailureException(
                    LongRunExitCode.RecordingStartFailed,
                    "Requested concrete microphone is not in the current " +
                    "GstDeviceMonitor catalog.");
            }
            NativeMethods.Result selectionResult =
                _recording.SetMicrophoneSelection(new MicrophoneSelection(
                    MicrophoneSelectionKind.ConcreteEndpoint,
                    selected.EndpointId,
                    selected.DisplayName));
            if (selectionResult != NativeMethods.Result.Ok)
            {
                throw new LongRunFailureException(
                    LongRunExitCode.RecordingStartFailed,
                    $"Concrete microphone selection was rejected: " +
                    $"{selectionResult}.");
            }
        }
    }

    private static NativeMethods.AudioProgramMode
        ResolveRequestedAudioProgramMode()
    {
        string value = Environment.GetEnvironmentVariable(
            "XB_PREVIEW_RECORDING_AUDIO_SOURCE")?.Trim().ToLowerInvariant() ??
            string.Empty;
        return value switch
        {
            "none" or "off" => NativeMethods.AudioProgramMode.None,
            "system" or "system-loopback" =>
                NativeMethods.AudioProgramMode.SystemOnly,
            "microphone" => NativeMethods.AudioProgramMode.MicrophoneOnly,
            _ => NativeMethods.AudioProgramMode.Dual,
        };
    }

    private async Task<ManagedRecordingSnapshot> StartAndRunRecordingAsync()
    {
        ManagedRecordingSnapshot snapshot = await _recording!.StartAsync();
        DateTimeOffset deadline = DateTimeOffset.UtcNow +
            RecordingStartTimeout;
        while (snapshot.State == ManagedRecordingState.Starting &&
               DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(25, _cancellationToken);
            snapshot = _recording.RefreshSnapshot();
            WriteSample("periodic", snapshot, 0);
        }
        if (snapshot.State != ManagedRecordingState.Recording)
        {
            throw new LongRunFailureException(
                LongRunExitCode.RecordingStartFailed,
                $"Recording did not reach Recording; state={snapshot.State}; " +
                $"error={snapshot.ErrorMessage}");
        }
        EnsureNativeOutputInsideRunDirectory(snapshot);
        NotifyStage(LongRunStage.RecordingStarted);

        _recordingClock = Stopwatch.StartNew();
        if (_options.CancelAfterSeconds.HasValue)
        {
            _cancellationSource.CancelAfter(
                TimeSpan.FromSeconds(
                    _options.CancelAfterSeconds.Value));
        }
        long intervalTicks = checked((long)(
            Stopwatch.Frequency *
            (_options.SampleIntervalMilliseconds / 1000.0)));
        long nextSample = 0;
        while (_recordingClock.Elapsed <
               TimeSpan.FromSeconds(_options.DurationSeconds))
        {
            _cancellationToken.ThrowIfCancellationRequested();
            snapshot = _recording.RefreshSnapshot();
            WriteSample(
                "periodic",
                snapshot,
                _recordingClock.Elapsed.TotalSeconds);
            if (snapshot.State == ManagedRecordingState.Failed)
            {
                throw new LongRunFailureException(
                    LongRunExitCode.RecordingFailed,
                    $"Recording entered Failed: {snapshot.ErrorMessage}");
            }
            if (snapshot.State != ManagedRecordingState.Recording)
            {
                throw new LongRunFailureException(
                    LongRunExitCode.RecordingFailed,
                    $"Unexpected recording state during timed run: {snapshot.State}");
            }

            nextSample = checked(nextSample + intervalTicks);
            long delayTicks = nextSample - _recordingClock.ElapsedTicks;
            if (delayTicks > 0)
            {
                await Task.Delay(
                    TimeSpan.FromSeconds(
                        delayTicks / (double)Stopwatch.Frequency),
                    _cancellationToken);
            }
            else
            {
                long skipped = (-delayTicks / intervalTicks) + 1;
                _missedPeriodicSamples = checked(
                    _missedPeriodicSamples + (int)skipped);
                nextSample = checked(nextSample + skipped * intervalTicks);
            }
        }

        _actualRecordingWallSeconds =
            _recordingClock.Elapsed.TotalSeconds;
        _recordingClock.Stop();
        _durationTargetReached = true;
        _termination!.CompleteTimedRun();
        _summary.StopRequestedUtc = DateTimeOffset.UtcNow;
        NotifyStage(LongRunStage.StopStarted);
        Stopwatch finalizeClock = Stopwatch.StartNew();
        _ = await _recording.StopAsync();
        NotifyStage(LongRunStage.StopCompleted);
        ManagedRecordingSnapshot terminal =
            await ReadIndependentTerminalSnapshotAsync();
        finalizeClock.Stop();
        _summary.FinalizeCompletedUtc = DateTimeOffset.UtcNow;
        _summary.FinalizeDurationMilliseconds =
            finalizeClock.Elapsed.TotalMilliseconds;
        return terminal;
    }

    private async Task<ManagedRecordingSnapshot>
        SafeStopAndReadTerminalAsync()
    {
        if (_recordingClock is not null)
        {
            _actualRecordingWallSeconds =
                _recordingClock.Elapsed.TotalSeconds;
            _recordingClock.Stop();
        }
        if (_recording is null)
        {
            return ManagedRecordingSnapshot.Idle;
        }
        if (_terminalSnapshotReadAfterStop)
        {
            return _terminalSnapshot;
        }

        try
        {
            _summary.StopRequestedUtc ??= DateTimeOffset.UtcNow;
            NotifyStage(LongRunStage.StopStarted);
            Stopwatch clock = Stopwatch.StartNew();
            _ = await _recording.StopForCloseAsync();
            NotifyStage(LongRunStage.StopCompleted);
            ManagedRecordingSnapshot terminal =
                await ReadIndependentTerminalSnapshotAsync();
            clock.Stop();
            _summary.FinalizeCompletedUtc ??= DateTimeOffset.UtcNow;
            _summary.FinalizeDurationMilliseconds ??=
                clock.Elapsed.TotalMilliseconds;
            return terminal;
        }
        catch (Exception error)
        {
            MarkRuntimeFailure();
            _summary.Reasons.Add(
                $"Safe Stop/Finalize failed: {error.GetType().Name}: {error.Message}");
            return _recording.CurrentSnapshot;
        }
    }

    private async Task<ManagedRecordingSnapshot>
        ReadIndependentTerminalSnapshotAsync()
    {
        NotifyStage(LongRunStage.TerminalSnapshotReadStarted);
        DateTimeOffset deadline = DateTimeOffset.UtcNow +
            TerminalSnapshotTimeout;
        ManagedRecordingSnapshot snapshot;
        do
        {
            snapshot = _recording!.RefreshSnapshot();
            if (snapshot.IsTerminal)
            {
                _terminalSnapshotReadAfterStop = true;
                _terminalSnapshot = snapshot;
                WriteSample(
                    "terminal",
                    snapshot,
                    _actualRecordingWallSeconds);
                NotifyStage(LongRunStage.TerminalSnapshotRead);
                return snapshot;
            }
            await Task.Delay(25);
        }
        while (DateTimeOffset.UtcNow < deadline);

        throw new LongRunFailureException(
            LongRunExitCode.StopOrFinalizeFailed,
            $"Timed out waiting for terminal Snapshot; state={snapshot.State}.");
    }

    private void WriteSample(
        string sampleType,
        ManagedRecordingSnapshot snapshot,
        double wallSeconds)
    {
        ProcessMetrics metrics = ProcessMetrics.Read(
            sampleType == "terminal" ? "terminal" : "running");
        _observations.Add(snapshot, metrics);
        _evidence!.WriteSample(new LongRunSample(
            sampleType,
            DateTimeOffset.UtcNow,
            wallSeconds,
            snapshot.State.ToString(),
            snapshot.SessionId,
            snapshot.Elapsed.Ticks,
            snapshot.OutputPath,
            snapshot.FramesSubmitted,
            snapshot.ActiveEncoder,
            snapshot.FinalizeAttempted,
            snapshot.FinalizeHResult,
            snapshot.FinalizeCount,
            snapshot.OutputSuccess,
            snapshot.FailureHResult,
            snapshot.OutputCleanupAttempted,
            snapshot.OutputCleanupSucceeded,
            snapshot.OutputCleanupHResult,
            snapshot.ResidualOutstanding,
            _lifecycle?.State.ToString() ?? "unavailable",
            metrics.WorkingSet,
            metrics.PrivateMemorySize,
            metrics.HandleCount,
            metrics.ThreadCount));
    }

    private async Task ClosePreviewAsync()
    {
        NotifyStage(LongRunStage.PreviewCloseStarted);
        bool invocationCompleted = false;
        string exceptionDetail = string.Empty;
        _relatedProcessesBeforePreviewClose =
            RelatedProcessEvidence.Capture(Environment.ProcessId);
        try
        {
            if (_lifecycle is not null)
            {
                await _lifecycle.CloseAsync();
                invocationCompleted = true;
            }
        }
        catch (Exception error)
        {
            MarkRuntimeFailure();
            exceptionDetail = $"{error.GetType().Name}: {error.Message}";
        }
        finally
        {
            PreviewLifecycleState finalState = _lifecycle?.State ??
                PreviewLifecycleState.NotInitialized;
            string lastError = _lifecycle?.LastError ?? string.Empty;
            _previewCloseEvidence = new PreviewCloseEvidence(
                invocationCompleted,
                finalState,
                lastError,
                exceptionDetail);
            _relatedProcessesAfterPreviewClose =
                RelatedProcessEvidence.Capture(Environment.ProcessId);
            _previewClosed = _previewCloseEvidence.Passed &&
                _relatedProcessesAfterPreviewClose.NoneFound;
            if (!_previewClosed)
            {
                MarkRuntimeFailure();
            }
            _summary.Reasons.Add(
                $"Preview close evidence: {_previewCloseEvidence.Describe()}.");
            NotifyStage(LongRunStage.PreviewCloseCompleted);
        }
    }

    private void DisposeDiagnosticLoggers()
    {
        try
        {
            _cameraLogger?.Dispose();
        }
        catch (Exception error)
        {
            MarkRuntimeFailure();
            _summary.Reasons.Add(
                $"Camera logger close failed: {error.Message}");
        }
        try
        {
            _followLogger?.Dispose();
        }
        catch (Exception error)
        {
            MarkRuntimeFailure();
            _summary.Reasons.Add(
                $"Follow logger close failed: {error.Message}");
        }
    }

    private void CloseAndValidateJsonl()
    {
        if (_evidence is null)
        {
            return;
        }
        try
        {
            _jsonlValidation = _evidence.CloseAndValidate(
                _observations.SampleCount);
        }
        catch (Exception error)
        {
            _jsonlValidation = new EvidenceFileValidation(
                false,
                File.Exists(_options.SnapshotsJsonlPath),
                false,
                false,
                0,
                _observations.SampleCount,
                0,
                false,
                $"JSONL validation failed: {error.GetType().Name}: {error.Message}");
        }
    }

    private LongRunExitCode ResolveExitCode(
        LongRunExitCode executionExitCode,
        bool gatesPassed) => ResolveExitCode(
            executionExitCode,
            gatesPassed,
            _termination!.EndReason,
            _termination.RuntimeFailureObserved,
            _termination.CancellationObserved);

    internal static LongRunExitCode ResolveExitCode(
        LongRunExitCode executionExitCode,
        bool gatesPassed,
        LongRunEndReason endReason,
        bool runtimeFailureObserved,
        bool cancellationObserved = false)
    {
        if (!gatesPassed)
        {
            return executionExitCode is LongRunExitCode.Pass or
                LongRunExitCode.CanceledSafely
                ? LongRunExitCode.EvidenceGateFailed
                : executionExitCode;
        }
        if (runtimeFailureObserved &&
            executionExitCode is LongRunExitCode.Pass or
                LongRunExitCode.CanceledSafely)
        {
            return LongRunExitCode.UnhandledException;
        }
        if (executionExitCode is not (
                LongRunExitCode.Pass or
                LongRunExitCode.CanceledSafely))
        {
            return executionExitCode;
        }
        if (cancellationObserved ||
            endReason == LongRunEndReason.CancellationRequested)
        {
            return LongRunExitCode.CanceledSafely;
        }
        return executionExitCode == LongRunExitCode.Pass &&
            endReason == LongRunEndReason.DurationReached
            ? LongRunExitCode.Pass
            : LongRunExitCode.UnhandledException;
    }

    private void PopulateSummary(
        LongRunExitCode exitCode,
        string sourceReaderValidation,
        GitReproducibility git,
        GateEvaluation gates)
    {
        _summary.EndUtc = DateTimeOffset.UtcNow;
        _summary.ActualWallDurationSeconds =
            _actualRecordingWallSeconds;
        _summary.FinalNativePts100ns =
            _terminalSnapshot.Elapsed.Ticks;
        _summary.SessionGuid = _terminalSnapshot.SessionId;
        _summary.Mp4Path = _terminalSnapshot.PublishedPath;
        try
        {
            FileInfo output = new(_terminalSnapshot.PublishedPath);
            _summary.Mp4Size = output.Exists ? output.Length : null;
        }
        catch
        {
            _summary.Mp4Size = null;
        }
        _summary.StateSequenceLegal =
            _observations.StateSequenceLegal;
        _summary.PtsMonotonic = _observations.PtsMonotonic;
        _summary.SampleCount = _observations.SampleCount;
        _summary.MissedSampleCount = _missedPeriodicSamples;
        _summary.SnapshotsJsonl = _jsonlValidation;
        _summary.ProcessStart = _baselineMetrics;
        _summary.ProcessMaximum = _observations.CalculateMaximum();
        _summary.ProcessEnd = _finalMetrics;
        _summary.RelatedProcessesAtStart =
            _relatedProcessesAtStart;
        _summary.RelatedProcessesAtEnd =
            _relatedProcessesAtEnd;
        _summary.RelatedProcessesBeforePreviewClose =
            _relatedProcessesBeforePreviewClose;
        _summary.RelatedProcessesAfterPreviewClose =
            _relatedProcessesAfterPreviewClose;
        _summary.LoadedModules = _loadedModules;
        _summary.Git = git;
        _summary.TerminalSnapshotReadAfterStop =
            _terminalSnapshotReadAfterStop;
        _summary.TerminalSnapshot = _terminalSnapshot;
        _summary.SourceReaderValidation = sourceReaderValidation;
        _summary.PreviewClosedNormally = _previewClosed;
        _summary.PreviewClose = _previewCloseEvidence;
        _summary.EndReason = _termination!.EndReason;
        _summary.CancellationObserved = _termination.CancellationObserved;
        _summary.DurationTargetReached = _durationTargetReached;
        _summary.RuntimeFailureObserved =
            _termination.RuntimeFailureObserved;
        _summary.GateMatrix = gates;
        _summary.ExitCode = (int)exitCode;
        _summary.Verdict = exitCode switch
        {
            LongRunExitCode.Pass => "PASS",
            LongRunExitCode.CanceledSafely => "CANCELED-SAFELY",
            _ => "BLOCKED",
        };
        foreach (GateResult gate in gates.Gates.Where(
            gate => gate.Required && !gate.Passed))
        {
            _summary.Reasons.Add(
                $"Gate failed: {gate.Name}: {gate.Detail}");
        }
        if (exitCode == LongRunExitCode.Pass)
        {
            _summary.Reasons.Add(
                "All normal-run evidence gates passed.");
        }
        else if (exitCode == LongRunExitCode.CanceledSafely)
        {
            _summary.Reasons.Add(
                "All cancellation safety evidence gates passed; target duration was intentionally not required.");
        }
    }

    private void EnsureNativeOutputInsideRunDirectory(
        ManagedRecordingSnapshot snapshot)
    {
        string expectedDirectory = Path.GetFullPath(Path.Combine(
            _options.RunDirectory,
            "p2.5a-recordings"));
        string actualDirectory = Path.GetFullPath(
            Path.GetDirectoryName(snapshot.WorkingPath) ?? string.Empty);
        if (!string.Equals(
            expectedDirectory,
            actualDirectory,
            StringComparison.OrdinalIgnoreCase))
        {
            throw new LongRunFailureException(
                LongRunExitCode.RecordingStartFailed,
                "Native output escaped the run directory. " +
                $"Expected {expectedDirectory}; actual {actualDirectory}.");
        }
    }

    private static void EnsurePreviewResult(
        PreviewLifecycleResult result,
        string operation)
    {
        if (!result.Succeeded)
        {
            throw new LongRunFailureException(
                LongRunExitCode.PreviewStartFailed,
                $"Preview {operation} failed: " +
                $"{result.Error ?? result.Status.ToString()}");
        }
    }

    private void NotifyStage(LongRunStage stage) =>
        _termination!.NotifyStage(stage);

    private void MarkRuntimeFailure() =>
        _termination!.MarkRuntimeFailure();
}

internal static class RepositoryFacts
{
    internal static string FindRepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, ".git")))
            {
                return current.FullName;
            }
            current = current.Parent;
        }
        throw new InvalidOperationException(
            "Git repository root was not found.");
    }

    internal static string RunGit(
        string repositoryRoot,
        params string[] arguments)
    {
        ProcessStartInfo start = new("git")
        {
            WorkingDirectory = repositoryRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }
        using Process process = Process.Start(start) ??
            throw new InvalidOperationException("Unable to start git.");
        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd().Trim();
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"git {string.Join(' ', arguments)} failed: {error}");
        }
        return output;
    }

    internal static bool IsEvidencePathGitSafe(
        string repositoryRoot,
        string path)
    {
        string fullRoot = Path.GetFullPath(repositoryRoot)
            .TrimEnd(Path.DirectorySeparatorChar) +
            Path.DirectorySeparatorChar;
        string fullPath = Path.GetFullPath(path);
        if (!fullPath.StartsWith(
            fullRoot,
            StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        string relative = Path.GetRelativePath(repositoryRoot, fullPath);
        ProcessStartInfo start = new("git")
        {
            WorkingDirectory = repositoryRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        start.ArgumentList.Add("check-ignore");
        start.ArgumentList.Add("--quiet");
        start.ArgumentList.Add("--");
        start.ArgumentList.Add(relative);
        using Process process = Process.Start(start) ??
            throw new InvalidOperationException(
                "Unable to start git check-ignore.");
        process.WaitForExit();
        return process.ExitCode == 0;
    }
}
