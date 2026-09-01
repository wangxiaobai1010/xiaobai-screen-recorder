using XbPreview.Host;

namespace XbPreview.Managed.Tests;

internal static class PreviewLifecycleTests
{
    internal static async Task RunAsync()
    {
        await ManagedCloseHidesBeforeBlockedCleanupAsync();
        await ManagedCloseIsSingleFlightAsync();
        await ManagedCloseFailureStillPostsFinalCloseAsync();
        await AutoStartOccursOnceAsync();
        await RepeatedStartIsIdempotentAsync();
        await StopOrderingIsDeterministicAsync();
        await RepeatedStopIsIdempotentAsync();
        await StartStopStartReusesSessionAsync();
        await StartFailureCleansUpAndCanRetryAsync();
        await ManagedStartupDiagnosticsCorrelateFailureAndRetryAsync();
        await ConcurrentStartIsSingleFlightAsync();
        await CrossingStartAndStopAreSerializedAsync();
        await CloseRejectsNewWorkAndDisposesOnceAsync();
        await ResizeIsLastWinsAsync();
        await StateAndErrorReflectNativeTruthAsync();
        await PreviewingRegionSelectionIsControllerOwnedAsync();
        await DuplicateSelectionIsRejectedAsync();
        await ConfirmCommitsOnlyAfterSuccessfulStartAsync();
        await CancelRestartsWithoutRevisionAsync();
        await SameRegionDoesNotAllocateRevisionAsync();
        await GeometryFailureRestoresPriorPreviewAsync();
        await StartFailureRollsBackWithHigherRevisionAsync();
        await RollbackFailureEntersErrorAsync();
        await CloseCancelsSelectionWithoutRestartAsync();
        await FullScreenReconfigurationIsAutomaticAsync();
        await CustomRegionForcesSafeRuntimeSettingsAsync();
        await FullScreenSelectionRequestHasNoInitialSelectionAsync();
        await CustomRegionSelectionRequestKeepsEditableRegionAsync();
        NoSelectionCannotConfirmUntilDrag();
        NewSelectionClearsCandidate();
        RegionSelectionButtonStatesAreExplicit();
    }

    internal static Task RunResizeAsync() => ResizeIsLastWinsAsync();

    internal static async Task RunCursorVisibilityGatesAsync()
    {
        Harness harness = new();
        await harness.InitializeAsync();
        RecordCursorVisibilitySnapshot initial =
            harness.Native.GetRecordCursorVisible();
        Require(
            initial.RequestedVisible && initial.AppliedVisible &&
            harness.Native.RecordCursorVisibilityCalls.Count == 0,
            "Gate 1 default cursor presentation is ON");
        Require(
            string.IsNullOrEmpty(Environment.GetEnvironmentVariable(
                "XB_CURSOR_VISIBILITY_HUMAN_REVIEW")),
            "Gate 1 production default has no operator ring review surface");
        Console.WriteLine("Gate 1 PASS: default ON regression contract");

        Require((await harness.StartAsync()).Succeeded, "Gate 2 Preview starts");
        int cameraStarts = harness.CameraServices.Single().StartCount;
        PreviewLifecycleResult off =
            await harness.Controller.SetRecordCursorVisibleAsync(false);
        RecordCursorVisibilitySnapshot hidden =
            harness.Native.GetRecordCursorVisible();
        Require(
            off.Succeeded && !hidden.RequestedVisible &&
            !hidden.AppliedVisible && cameraStarts == 1,
            "Gate 2 presentation OFF is independent of tracking service");
        Console.WriteLine("Gate 2 PASS: OFF presentation separation contract");

        RecordingController recording =
            harness.Controller.GetOrCreateRecordingController();
        ManagedRecordingSnapshot started = await recording.StartAsync();
        Require(
            started.State == ManagedRecordingState.Recording,
            "Gate 3 recording starts");
        foreach (bool visible in new[] { true, false, true, false })
        {
            PreviewLifecycleResult toggle =
                await harness.Controller.SetRecordCursorVisibleAsync(visible);
            Require(toggle.Succeeded, "Gate 3 runtime toggle accepted");
        }
        Require(
            harness.SessionFactoryCount == 1 &&
            harness.Native.StartCount == 1 &&
            harness.Native.StopCount == 0 &&
            harness.Native.RecordingStartCount == 1 &&
            harness.Native.RecordingStopCount == 0 &&
            harness.Native.RecordingFinalizeCount == 0,
            "Gate 3 toggle does not restart Preview/encoder/finalize");
        Console.WriteLine("Gate 3 PASS: Recording runtime toggle contract");

        ManagedRecordingSnapshot paused = await recording.PauseAsync();
        Require(
            paused.State == ManagedRecordingState.Paused,
            "Gate 4 recording pauses");
        PreviewLifecycleResult pausedToggle =
            await harness.Controller.SetRecordCursorVisibleAsync(true);
        ManagedRecordingSnapshot resumed = await recording.ResumeAsync();
        Require(
            pausedToggle.Succeeded &&
            resumed.State == ManagedRecordingState.Recording &&
            harness.Native.GetRecordCursorVisible().AppliedVisible &&
            harness.Native.RecordingPauseCount == 1 &&
            harness.Native.RecordingResumeCount == 1,
            "Gate 4 paused toggle applies on Resume without timeline reset");
        Console.WriteLine("Gate 4 PASS: Paused toggle contract");

        PreviewLifecycleResult followOff =
            await harness.Controller.SetRecordCursorVisibleAsync(false);
        harness.Controller.SetFollowEnabled(true);
        Require(
            followOff.Succeeded &&
            harness.CameraServices.Single().StartCount == cameraStarts &&
            harness.Log.Contains("camera:follow:true") &&
            harness.Native.GetRecordCursorVisible().AppliedVisible == false,
            "Gate 5 Follow remains independently active while cursor hidden");
        Console.WriteLine("Gate 5 PASS: Follow independence contract");

        await recording.StopAsync();
        await harness.Controller.CloseAsync();
    }

    internal static async Task RunWindowCaptureContractAsync()
    {
        Harness window = new(blockRecordingStop: true);
        await window.InitializeAsync();
        CaptureTarget windowTarget = new(
            CaptureTargetKind.Window,
            new nint(0x1234),
            "Contract window");
        PreviewLifecycleResult configured =
            await window.Controller.SetCaptureTargetAsync(windowTarget);
        Require(configured.Succeeded, "window target accepted while stopped");
        Require(
            window.Native.CaptureTargets.SequenceEqual([windowTarget]),
            "window HWND reaches native before Start");
        Require((await window.StartAsync()).Succeeded, "window Preview starts once");
        PreviewLifecycleResult activeChange =
            await window.Controller.SetCaptureTargetAsync(CaptureTarget.FullScreen);
        Require(
            activeChange.Status == PreviewLifecycleOperationStatus.Rejected &&
            window.Native.CaptureTargets.Count == 1,
            "running Preview rejects target mutation without fallback");

        RecordingController recording =
            window.Controller.GetOrCreateRecordingController();
        ManagedRecordingSnapshot started = await recording.StartAsync();
        Require(
            started.State == ManagedRecordingState.Recording &&
            window.Native.RecordingStartCount == 1,
            "window recording starts once");
        Task<ManagedRecordingSnapshot> firstStop = recording.StopAsync();
        Require(
            window.Native.RecordingStopEntered.Wait(TimeSpan.FromSeconds(2)),
            "native window recording Stop entered");
        Task<ManagedRecordingSnapshot> secondStop = recording.StopAsync();
        Require(
            ReferenceEquals(firstStop, secondStop),
            "duplicate window Stop shares one in-flight task");
        window.Native.ReleaseRecordingStop();
        ManagedRecordingSnapshot[] terminal =
            await Task.WhenAll(firstStop, secondStop);
        Require(
            terminal.All(item =>
                item.State == ManagedRecordingState.Completed &&
                item.FinalizeAttempted && item.FinalizeCount == 1 &&
                item.ValidationAttempted && item.ValidationHResult == 0 &&
                item.PublishAttempted && item.PublishHResult == 0 &&
                item.Published),
            "duplicate Stop shares one Finalize / Validate / Publish result");
        Require(
            window.Native.RecordingStopCount == 1 &&
            window.Native.RecordingFinalizeCount == 1,
            "native Stop and Finalize occur exactly once");
        await window.Controller.CloseAsync();

        Harness monitor = new();
        await monitor.InitializeAsync();
        Require(
            monitor.Controller.CurrentCaptureTarget == CaptureTarget.FullScreen,
            "monitor remains the unchanged default path");
        Require((await monitor.StartAsync()).Succeeded, "monitor Preview still starts");
        await monitor.Controller.CloseAsync();
    }

    private static async Task ManagedStartupDiagnosticsCorrelateFailureAndRetryAsync()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            $"xbpreview-managed-startup-{Environment.ProcessId}");
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
        ManagedStartupDiagnostics.ResetForTests();
        ManagedStartupDiagnostics.Write(new ManagedStartupDiagnosticEvent
        {
            ManagedStage = "Program.MainEntered",
            LifecycleState = PreviewLifecycleState.NotInitialized.ToString(),
            Result = "begin",
        });
        ManagedStartupDiagnostics.Configure(root);

        Harness harness = new();
        Guid session = Guid.NewGuid();
        byte[] sessionBytes = session.ToByteArray();
        harness.Native.Stats = new NativeMethods.PreviewStats
        {
            SessionIdHigh = BitConverter.ToUInt64(sessionBytes, 0),
            SessionIdLow = BitConverter.ToUInt64(sessionBytes, 8),
        };
        harness.Native.LastError =
            "controlled Native Start failed; HRESULT=0x80070424";
        harness.Native.StartResults.Enqueue(NativeMethods.Result.NativeFailure);
        harness.Native.StartResults.Enqueue(NativeMethods.Result.Ok);
        await harness.InitializeAsync();

        PreviewLifecycleResult failed = await harness.Controller.StartAsync(
            cameraEnabled: true,
            followEnabled: true,
            NativeMethods.CursorMode.CustomCursor);
        Require(!failed.Succeeded, "controlled Native Start failure returned");
        Require(
            harness.Controller.State == PreviewLifecycleState.Error,
            "Native Start failure remains visible Error state");
        Require(
            harness.Native.StopCount == 1 &&
            harness.Native.DisposeCount == 0,
            "failure cleanup stops once without auto close or dispose");

        ManagedStartupDiagnosticEvent begin = harness.StartupDiagnostics.
            Single(item => item.ManagedStage == "NativeStartCallBegin");
        ManagedStartupDiagnosticEvent returned = harness.StartupDiagnostics.
            Single(item => item.ManagedStage == "NativeStartReturnedFailure");
        ManagedStartupDiagnosticEvent cleanupEnd = harness.StartupDiagnostics.
            Single(item => item.ManagedStage == "FailStartAndCleanupEnd");
        Require(
            !string.IsNullOrWhiteSpace(begin.StartupAttemptId) &&
            begin.StartupAttemptId == returned.StartupAttemptId &&
            returned.StartupAttemptId == cleanupEnd.StartupAttemptId,
            "managed startup attempt identifier is stable");
        Require(
            returned.SessionGuid == session.ToString("D").ToUpperInvariant() &&
            returned.NativeHResult == "0x80070424" &&
            cleanupEnd.RetryAvailable == true,
            "attempt maps to native SessionGuid/HRESULT and preserves retry");

        ManagedStartupDiagnostics.Write(returned with
        {
            ManagedStage = "UiEnteredErrorState",
            MainFormIsHandleCreated = true,
            MainFormHandle = 1234,
            PreviewSurfaceIsHandleCreated = true,
            PreviewSurfaceHandle = 5678,
            Visible = true,
            WindowState = "Normal",
            IsDisposed = false,
            Disposing = false,
            LifecycleState = PreviewLifecycleState.Error.ToString(),
            RetryAvailable = true,
        });

        harness.Native.LastError = string.Empty;
        PreviewLifecycleResult retried = await harness.Controller.StartAsync(
            cameraEnabled: true,
            followEnabled: true,
            NativeMethods.CursorMode.CustomCursor);
        Require(
            retried.Succeeded &&
            harness.Controller.State == PreviewLifecycleState.Previewing,
            "Error to Retry behavior remains unchanged");
        await harness.Controller.CloseAsync();

        string logPath = ManagedStartupDiagnostics.FilePath!;
        ManagedStartupDiagnostics.Close();
        string json = File.ReadAllText(logPath);
        foreach (string required in new[]
        {
            "\"ManagedStage\":\"Program.MainEntered\"",
            "\"ManagedStage\":\"NativeStartReturnedFailure\"",
            "\"ManagedStage\":\"UiEnteredErrorState\"",
            "\"StartupAttemptId\":",
            "\"SessionGuid\":",
            "\"MainFormIsHandleCreated\":true",
            "\"PreviewSurfaceIsHandleCreated\":true",
            "\"Visible\":true",
            "\"LifecycleState\":\"Error\"",
            "\"NativeHResult\":\"0x80070424\"",
            "\"RetryAvailable\":true",
        })
        {
            Require(json.Contains(required, StringComparison.Ordinal),
                "managed startup diagnostic schema");
        }
        ManagedStartupDiagnostics.ResetForTests();
        Directory.Delete(root, recursive: true);
    }

    private static async Task ManagedCloseHidesBeforeBlockedCleanupAsync()
    {
        ManagedCloseCoordinator coordinator = new();
        TaskCompletionSource releaseCleanup = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        List<string> order = [];
        bool visible = true;
        bool handleCreated = true;
        ManagedCloseDiagnostics? diagnostics = null;
        int finalCloseCount = 0;

        Task<bool> close = coordinator.TryExecuteAsync(
            "test-session",
            () => order.Add("prepare"),
            () =>
            {
                order.Add("hide");
                visible = false;
            },
            () => visible,
            () => handleCreated,
            async () =>
            {
                order.Add("cleanup");
                await releaseCleanup.Task;
            },
            value => diagnostics = value,
            () =>
            {
                order.Add("close");
                finalCloseCount++;
            });

        Require(!visible, "managed close hides synchronously");
        Require(handleCreated, "managed close preserves handle while hidden");
        Require(
            order.SequenceEqual(["prepare", "hide", "cleanup"]),
            "managed close starts cleanup only after hide");
        Require(!close.IsCompleted, "blocked cleanup remains pending after hide");

        releaseCleanup.SetResult();
        Require(await close, "first managed close owns cleanup");
        Require(finalCloseCount == 1, "managed close posts final close once");
        Require(diagnostics is not null, "managed close publishes diagnostics");
        ManagedCloseDiagnostics actualDiagnostics = diagnostics!;
        Require(
            actualDiagnostics.HideInvocationCount == 1 &&
            actualDiagnostics.CleanupInvocationCount == 1 &&
            actualDiagnostics.FinalCloseInvocationCount == 1,
            "managed close diagnostic invocation counts");
        Require(!actualDiagnostics.VisibleAfterHide, "visible is false after hide");
        Require(
            actualDiagnostics.HandleCreatedAfterHide,
            "handle remains created after hide");
        Require(
            actualDiagnostics.VisibleCloseLatencyMs >= 0.0,
            "visible close latency is finite and nonnegative");
    }

    private static async Task ManagedCloseIsSingleFlightAsync()
    {
        ManagedCloseCoordinator coordinator = new();
        TaskCompletionSource enteredCleanup = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource releaseCleanup = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        int hideCount = 0;
        int cleanupCount = 0;
        int finalCloseCount = 0;
        Task<bool> first = coordinator.TryExecuteAsync(
            null,
            () => { },
            () => hideCount++,
            () => false,
            () => true,
            async () =>
            {
                cleanupCount++;
                enteredCleanup.SetResult();
                await releaseCleanup.Task;
            },
            _ => { },
            () => finalCloseCount++);
        await enteredCleanup.Task;
        bool repeated = await coordinator.TryExecuteAsync(
            null,
            () => throw new InvalidOperationException("repeat prepare"),
            () => throw new InvalidOperationException("repeat hide"),
            () => true,
            () => true,
            () => throw new InvalidOperationException("repeat cleanup"),
            _ => throw new InvalidOperationException("repeat completion"),
            () => throw new InvalidOperationException("repeat close"));
        Require(!repeated, "repeated managed close is rejected");
        releaseCleanup.SetResult();
        Require(await first, "first managed close completes");
        Require(
            hideCount == 1 && cleanupCount == 1 && finalCloseCount == 1,
            "repeat close does not duplicate hide cleanup or final close");
    }

    private static async Task ManagedCloseFailureStillPostsFinalCloseAsync()
    {
        ManagedCloseCoordinator coordinator = new();
        bool visible = true;
        int finalCloseCount = 0;
        ManagedCloseDiagnostics? diagnostics = null;
        bool started = await coordinator.TryExecuteAsync(
            null,
            () => { },
            () => visible = false,
            () => visible,
            () => true,
            () => throw new InvalidOperationException("cleanup failure"),
            value => diagnostics = value,
            () => finalCloseCount++);
        Require(started && !visible, "failed cleanup remains hidden");
        Require(finalCloseCount == 1, "failed cleanup still posts final close");
        Require(
            diagnostics is not null &&
            !diagnostics.CleanupSucceeded &&
            diagnostics.CleanupExceptionType == typeof(InvalidOperationException).FullName,
            "cleanup exception is observed and diagnosed");
    }

    private static async Task AutoStartOccursOnceAsync()
    {
        Harness harness = new(blockStart: true);
        Require(
            harness.Controller.State == PreviewLifecycleState.NotInitialized,
            "auto-start begins NotInitialized");
        await harness.InitializeAsync();
        Require(
            harness.Controller.State == PreviewLifecycleState.Stopped,
            "auto-start initialization reaches Stopped");

        Task<PreviewLifecycleResult> start = harness.Controller.StartAsync(
            true,
            true,
            NativeMethods.CursorMode.CustomCursor);
        Require(harness.Native.StartEntered.Wait(3000), "auto-start entered");
        Require(
            harness.Controller.State == PreviewLifecycleState.Starting,
            "auto-start exposes Starting");
        harness.Native.ReleaseStart();
        PreviewLifecycleResult result = await start;

        Require(result.Succeeded, "auto-start succeeds");
        Require(harness.Native.StartCount == 1, "auto-start occurs once");
        Require(
            harness.Controller.State == PreviewLifecycleState.Previewing,
            "auto-start reaches Previewing");
        await harness.Controller.CloseAsync();
    }

    private static async Task RepeatedStartIsIdempotentAsync()
    {
        Harness harness = new();
        await harness.InitializeAsync();
        PreviewLifecycleResult first = await harness.StartAsync();
        PreviewLifecycleResult second = await harness.StartAsync();

        Require(first.Succeeded && second.Succeeded, "repeated Start result");
        Require(harness.Native.StartCount == 1, "repeated Start is native no-op");
        Require(harness.CameraServices.Count == 1, "single camera service");
        await harness.Controller.CloseAsync();
    }

    private static async Task StopOrderingIsDeterministicAsync()
    {
        Harness harness = new(blockStop: true);
        await harness.InitializeAsync();
        await harness.StartAsync();
        harness.Log.Clear();
        harness.Native.GeometryObserved = () =>
            Require(
                harness.Controller.State ==
                    PreviewLifecycleState.Reconfiguring,
                "candidate Geometry is configured in Reconfiguring");

        Task<PreviewLifecycleResult> stopping =
            harness.Controller.StopAsync();
        Require(harness.Native.StopEntered.Wait(3000), "Stop entered native");
        Require(
            harness.Controller.State == PreviewLifecycleState.Stopping,
            "Stop exposes Stopping");
        await Task.Delay(25);
        harness.Native.ReleaseStop();
        PreviewLifecycleResult result = await stopping;

        Require(result.Succeeded, "Stop succeeds");
        RequireOrdered(
            harness.Log,
            "hotkey:false",
            "camera:follow:false",
            "camera:stop",
            "native:wide",
            "native:stop");
        Require(
            harness.Controller.State == PreviewLifecycleState.Stopped,
            "Stop reaches Stopped");
        Require(
            harness.Controller.LastEngineStopDurationMs >= 15.0,
            "native Engine Stop duration is measured around the awaited call");
        await harness.Controller.CloseAsync();
    }

    private static async Task RepeatedStopIsIdempotentAsync()
    {
        Harness harness = new();
        await harness.InitializeAsync();
        await harness.StartAsync();
        await harness.Controller.StopAsync();
        int stopCount = harness.Native.StopCount;

        PreviewLifecycleResult second = await harness.Controller.StopAsync();

        Require(second.Succeeded, "repeated Stop result");
        Require(harness.Native.StopCount == stopCount, "repeated Stop native no-op");
        await harness.Controller.CloseAsync();
    }

    private static async Task StartStopStartReusesSessionAsync()
    {
        Harness harness = new();
        await harness.InitializeAsync();
        await harness.StartAsync();
        await harness.Controller.StopAsync();
        await harness.StartAsync();

        Require(harness.SessionFactoryCount == 1, "one native session owner");
        Require(harness.Native.StartCount == 2, "second start reaches native");
        Require(harness.CameraServices.Count == 2, "camera service recreated");
        Require(
            harness.CameraServices[0].StopCount == 1 &&
            harness.CameraServices[0].DisposeCount == 1,
            "first camera service fully retired");
        Require(
            harness.CameraServices[1].StartCount == 1,
            "new camera service active");
        await harness.Controller.CloseAsync();
    }

    private static async Task StartFailureCleansUpAndCanRetryAsync()
    {
        Harness harness = new();
        harness.Native.StartResults.Enqueue(NativeMethods.Result.NativeFailure);
        harness.Native.StartResults.Enqueue(NativeMethods.Result.Ok);
        harness.Native.LastError = "synthetic start failure";
        await harness.InitializeAsync();

        PreviewLifecycleResult failed = await harness.StartAsync();
        Require(
            failed.Status == PreviewLifecycleOperationStatus.Failed,
            "Start failure status");
        Require(
            harness.Controller.State == PreviewLifecycleState.Error,
            "Start failure reaches Error");
        Require(harness.Native.StopCount == 1, "failed Start cleans native");
        Require(harness.CameraServices.Count == 0, "failed Start has no camera");
        Require(harness.Availability.Last() == false, "failed Start disables hotkeys");

        harness.Native.LastError = string.Empty;
        PreviewLifecycleResult retry = await harness.StartAsync();
        Require(retry.Succeeded, "retry after Start failure succeeds");
        Require(harness.Native.StartCount == 2, "retry calls Start once");
        await harness.Controller.CloseAsync();
    }

    private static async Task ConcurrentStartIsSingleFlightAsync()
    {
        Harness harness = new(blockStart: true);
        await harness.InitializeAsync();
        Task<PreviewLifecycleResult> first = harness.StartAsync();
        Require(harness.Native.StartEntered.Wait(3000), "concurrent Start entered");
        Task<PreviewLifecycleResult> second = harness.StartAsync();
        harness.Native.ReleaseStart();
        PreviewLifecycleResult[] results = await Task.WhenAll(first, second);

        Require(results.All(static result => result.Succeeded), "concurrent Start results");
        Require(harness.Native.StartCount == 1, "concurrent Start single-flight");
        Require(harness.Native.MaxConcurrentLifecycle == 1, "native lifecycle serialized");
        await harness.Controller.CloseAsync();
    }

    private static async Task CrossingStartAndStopAreSerializedAsync()
    {
        Harness harness = new(blockStart: true);
        await harness.InitializeAsync();
        Task<PreviewLifecycleResult> start = harness.StartAsync();
        Require(harness.Native.StartEntered.Wait(3000), "crossing Start entered");
        Task<PreviewLifecycleResult> stop = harness.Controller.StopAsync();
        harness.Native.ReleaseStart();
        await Task.WhenAll(start, stop);

        Require(
            harness.Controller.State == PreviewLifecycleState.Stopped,
            "crossing Start/Stop final state");
        Require(harness.Native.StartCount == 1, "crossing Start count");
        Require(harness.Native.StopCount == 1, "crossing Stop count");
        Require(harness.Native.MaxConcurrentLifecycle == 1, "crossing calls serialized");
        await harness.Controller.CloseAsync();
    }

    private static async Task CloseRejectsNewWorkAndDisposesOnceAsync()
    {
        Harness harness = new(blockStart: true);
        await harness.InitializeAsync();
        Task<PreviewLifecycleResult> start = harness.StartAsync();
        Require(harness.Native.StartEntered.Wait(3000), "Close waits for active Start");
        Task closing = Task.WhenAll(
            harness.Controller.CloseAsync(),
            harness.Controller.CloseAsync());
        Task<PreviewLifecycleResult> rejectedStart = harness.StartAsync();
        harness.Native.ReleaseStart();
        PreviewLifecycleResult initialStart = await start;
        await closing;
        PreviewLifecycleResult rejected = await rejectedStart;

        Require(
            harness.Controller.State == PreviewLifecycleState.Disposed,
            "Close reaches Disposed");
        Require(
            initialStart.Status == PreviewLifecycleOperationStatus.Rejected,
            "active Start yields to Close before publishing Previewing");
        Require(
            rejected.Status == PreviewLifecycleOperationStatus.Rejected,
            "Start rejected after Close");
        Require(harness.Native.StopCount == 1, "Close stops once");
        Require(harness.Native.DisposeCount == 1, "Close disposes once");
        Require(harness.CameraServices.Count == 0, "Close prevents camera startup");
        Require(
            harness.Controller.LastLifecycleCloseDurationMs >= 0.0,
            "managed lifecycle Close duration is retained after idempotent cleanup");
    }

    private static async Task ResizeIsLastWinsAsync()
    {
        Harness harness = new();
        await harness.InitializeAsync();
        await harness.Controller.RequestResizeAsync(800, 450);
        await harness.Controller.RequestResizeAsync(1280, 720);
        await harness.Controller.RequestResizeAsync(0, 0);
        Require(harness.Native.ResizeCount == 0, "stopped resize deferred");

        await harness.StartAsync();
        Require(harness.Native.ResizeCount == 1, "latest deferred resize applied");
        Require(harness.Native.LastResize == (1280, 720), "deferred resize last-wins");

        SessionGeometry geometryBeforeResize =
            harness.Controller.CurrentGeometry ??
            throw new InvalidOperationException(
                "running preview did not retain SessionGeometry");
        RecordingController recording =
            harness.Controller.GetOrCreateRecordingController();
        ManagedRecordingSnapshot active = await recording.StartAsync();
        Require(active.IsActive, "recording is active before monitor resize");
        int logCountBeforeResize = harness.Log.Count;
        await harness.Controller.RequestResizeAsync(1440, 810);
        Require(harness.Native.LastResize == (1440, 810), "running resize applied");
        Require(harness.Native.ResizeCount == 2, "running resize count");
        Require(
            harness.Native.RecordingStopCount == 0 &&
            harness.Native.RecordingFinalizeCount == 0 &&
            recording.CurrentSnapshot.IsActive,
            "monitor resize does not Stop or Finalize active recording");
        Require(
            ReferenceEquals(
                geometryBeforeResize,
                harness.Controller.CurrentGeometry) &&
            geometryBeforeResize.OutputCanvas ==
                harness.Controller.CurrentGeometry!.OutputCanvas,
            "monitor resize does not change SessionGeometry or OutputCanvas");
        Require(
            harness.Log.Skip(logCountBeforeResize).SequenceEqual(
                ["native:resize:1440x810"]),
            "monitor resize submits presentation size only");
        ManagedRecordingSnapshot stopped = await recording.StopAsync();
        Require(
            stopped.State == ManagedRecordingState.Completed &&
            harness.Native.RecordingStopCount == 1 &&
            harness.Native.RecordingFinalizeCount == 1,
            "explicit Stop remains the only recording terminal action");
        await harness.Controller.CloseAsync();
    }

    private static async Task StateAndErrorReflectNativeTruthAsync()
    {
        Harness harness = new();
        harness.Native.StartResults.Enqueue(NativeMethods.Result.NativeFailure);
        harness.Native.LastError = "native truth";
        await harness.InitializeAsync();
        PreviewLifecycleResult result = await harness.StartAsync();

        Require(
            result.Status == PreviewLifecycleOperationStatus.Failed,
            "native failure is not reported as success");
        Require(
            harness.Controller.State == PreviewLifecycleState.Error,
            "native failure state truth");
        Require(
            harness.Controller.LastError?.Contains(
                "native truth",
                StringComparison.Ordinal) == true,
            "native error retained");
        Require(!harness.Controller.IsPreviewing, "failure is not Previewing");
        await harness.Controller.CloseAsync();
    }

    private static async Task PreviewingRegionSelectionIsControllerOwnedAsync()
    {
        Harness harness = new();
        await harness.InitializeAsync();
        await harness.StartAsync();
        harness.Log.Clear();

        PreviewLifecycleResult result =
            await harness.Controller.ReconfigureRegionAsync(
                (_, _) =>
                {
                    Require(
                        harness.Controller.State ==
                            PreviewLifecycleState.SelectingRegion,
                        "selector runs in SelectingRegion");
                    harness.Log.Add("selector:confirmed");
                    return GeometrySelectionResult.Confirmed(
                        harness.CustomGeometry);
                },
                harness.RequestedSettings);

        Require(result.Succeeded, "Previewing region transaction succeeds");
        RequireOrdered(
            harness.Log,
            "hotkey:false",
            "camera:follow:false",
            "camera:stop",
            "native:wide",
            "native:stop",
            "selector:confirmed",
            "native:geometry:2",
            "native:cursor:SystemCursor",
            "native:start");
        Require(
            harness.Controller.State == PreviewLifecycleState.Previewing,
            "region transaction returns Previewing");
        Require(
            SessionGeometryNativeV1.ContentEquals(
                harness.Controller.CurrentGeometry!,
                harness.CustomGeometry),
            "confirmed geometry becomes Current");
        await harness.Controller.CloseAsync();
    }

    private static async Task DuplicateSelectionIsRejectedAsync()
    {
        Harness harness = new();
        await harness.InitializeAsync();
        await harness.StartAsync();
        ManualResetEventSlim entered = new(false);
        ManualResetEventSlim release = new(false);
        Task<PreviewLifecycleResult> first =
            harness.Controller.ReconfigureRegionAsync(
                (_, _) =>
                {
                    entered.Set();
                    release.Wait(TimeSpan.FromSeconds(5));
                    return GeometrySelectionResult.Cancelled();
                },
                harness.RequestedSettings);
        Require(entered.Wait(3000), "first selector entered");
        PreviewLifecycleResult duplicate =
            await harness.Controller.ReconfigureRegionAsync(
                (_, _) => GeometrySelectionResult.Confirmed(
                    harness.CustomGeometry),
                harness.RequestedSettings);
        Require(
            duplicate.Status == PreviewLifecycleOperationStatus.Rejected,
            "duplicate selector rejected");
        release.Set();
        await first;
        await harness.Controller.CloseAsync();
    }

    private static async Task ConfirmCommitsOnlyAfterSuccessfulStartAsync()
    {
        Harness harness = new();
        await harness.InitializeAsync();
        await harness.StartAsync();
        harness.Native.BlockNextStart();
        Task<PreviewLifecycleResult> transaction =
            harness.Controller.ReconfigureRegionAsync(
                (_, _) => GeometrySelectionResult.Confirmed(
                    harness.CustomGeometry),
                harness.RequestedSettings);
        Require(
            harness.Native.StartEntered.Wait(3000),
            "candidate Start entered");
        Require(
            SessionGeometryNativeV1.ContentEquals(
                harness.Controller.CurrentGeometry!,
                harness.FullScreenGeometry),
            "Current remains old while candidate Start is pending");
        harness.Native.ReleaseNextStart();
        PreviewLifecycleResult result = await transaction;
        Require(result.Succeeded, "candidate Start succeeds");
        Require(
            SessionGeometryNativeV1.ContentEquals(
                harness.Controller.CurrentGeometry!,
                harness.CustomGeometry),
            "Current commits after candidate Start");
        await harness.Controller.CloseAsync();
    }

    private static async Task CancelRestartsWithoutRevisionAsync()
    {
        Harness harness = new();
        await harness.InitializeAsync();
        await harness.StartAsync();
        int geometryCount = harness.Native.GeometrySetCount;
        ulong revision = harness.Controller.CurrentGeometryRevision;

        PreviewLifecycleResult result =
            await harness.Controller.ReconfigureRegionAsync(
                (_, _) => GeometrySelectionResult.Cancelled(),
                harness.RequestedSettings);

        Require(result.Succeeded, "cancel restores prior Preview");
        Require(
            harness.Native.GeometrySetCount == geometryCount,
            "cancel performs no SetSessionGeometry");
        Require(
            harness.Controller.CurrentGeometryRevision == revision,
            "cancel allocates no revision");
        Require(harness.Native.StartCount == 2, "cancel restarts old Preview");
        await harness.Controller.CloseAsync();
    }

    private static async Task SameRegionDoesNotAllocateRevisionAsync()
    {
        Harness harness = new();
        await harness.InitializeAsync();
        await harness.StartAsync();
        ulong revision = harness.Controller.CurrentGeometryRevision;
        int geometryCount = harness.Native.GeometrySetCount;

        PreviewLifecycleResult result =
            await harness.Controller.ReconfigureRegionAsync(
                (_, _) => GeometrySelectionResult.Confirmed(
                    harness.FullScreenGeometry),
                harness.RequestedSettings);

        Require(result.Succeeded, "same region safely restarts");
        Require(
            harness.Controller.CurrentGeometryRevision == revision,
            "same region revision unchanged");
        Require(
            harness.Native.GeometrySetCount == geometryCount,
            "same region performs no geometry Set");
        await harness.Controller.CloseAsync();
    }

    private static async Task GeometryFailureRestoresPriorPreviewAsync()
    {
        Harness harness = new();
        await harness.InitializeAsync();
        await harness.StartAsync();
        harness.Native.GeometryResults.Enqueue(
            NativeMethods.Result.InvalidGeometry);

        PreviewLifecycleResult result =
            await harness.Controller.ReconfigureRegionAsync(
                (_, _) => GeometrySelectionResult.Confirmed(
                    harness.CustomGeometry),
                harness.RequestedSettings);

        Require(
            result.Status == PreviewLifecycleOperationStatus.Failed,
            "geometry failure is reported");
        Require(
            harness.Controller.State == PreviewLifecycleState.Previewing,
            "geometry failure restores old Preview");
        Require(
            SessionGeometryNativeV1.ContentEquals(
                harness.Controller.CurrentGeometry!,
                harness.FullScreenGeometry),
            "geometry failure keeps old Current");
        await harness.Controller.CloseAsync();
    }

    private static async Task StartFailureRollsBackWithHigherRevisionAsync()
    {
        Harness harness = new();
        await harness.InitializeAsync();
        await harness.StartAsync();
        harness.Native.StartResults.Enqueue(
            NativeMethods.Result.NativeFailure);
        harness.Native.StartResults.Enqueue(NativeMethods.Result.Ok);

        PreviewLifecycleResult result =
            await harness.Controller.ReconfigureRegionAsync(
                (_, _) => GeometrySelectionResult.Confirmed(
                    harness.CustomGeometry),
                harness.RequestedSettings);

        Require(
            result.Status == PreviewLifecycleOperationStatus.Failed,
            "candidate Start failure is reported");
        Require(
            harness.Controller.State == PreviewLifecycleState.Previewing,
            "rollback restores Previewing");
        SessionGeometryNativeV1 candidate =
            harness.Native.GeometryHistory[^2];
        SessionGeometryNativeV1 rollback =
            harness.Native.GeometryHistory[^1];
        Require(
            rollback.GeometryRevision > candidate.GeometryRevision,
            "rollback revision is strictly higher");
        Require(
            rollback.CaptureWidth ==
                harness.FullScreenGeometry.CaptureRegion.Width,
            "rollback content is prior geometry");
        Require(
            harness.Controller.CurrentGeometryRevision ==
                rollback.GeometryRevision,
            "controller publishes rollback revision");
        await harness.Controller.CloseAsync();
    }

    private static async Task RollbackFailureEntersErrorAsync()
    {
        Harness harness = new();
        await harness.InitializeAsync();
        await harness.StartAsync();
        harness.Native.StartResults.Enqueue(
            NativeMethods.Result.NativeFailure);
        harness.Native.GeometryResults.Enqueue(NativeMethods.Result.Ok);
        harness.Native.GeometryResults.Enqueue(
            NativeMethods.Result.NativeFailure);

        PreviewLifecycleResult result =
            await harness.Controller.ReconfigureRegionAsync(
                (_, _) => GeometrySelectionResult.Confirmed(
                    harness.CustomGeometry),
                harness.RequestedSettings);

        Require(
            result.Status == PreviewLifecycleOperationStatus.Failed,
            "rollback failure reported");
        Require(
            harness.Controller.State == PreviewLifecycleState.Error,
            "rollback failure reaches Error");
        await harness.Controller.CloseAsync();
    }

    private static async Task CloseCancelsSelectionWithoutRestartAsync()
    {
        Harness harness = new();
        await harness.InitializeAsync();
        await harness.StartAsync();
        ManualResetEventSlim entered = new(false);
        Task<PreviewLifecycleResult> selection =
            harness.Controller.ReconfigureRegionAsync(
                (_, token) =>
                {
                    entered.Set();
                    token.WaitHandle.WaitOne(TimeSpan.FromSeconds(5));
                    token.ThrowIfCancellationRequested();
                    return GeometrySelectionResult.Cancelled();
                },
                harness.RequestedSettings);
        Require(entered.Wait(3000), "Close-during-selection entered");
        int startsBeforeClose = harness.Native.StartCount;
        await harness.Controller.CloseAsync();
        PreviewLifecycleResult result = await selection;

        Require(
            result.Status == PreviewLifecycleOperationStatus.Rejected,
            "selection yields to Close");
        Require(
            harness.Native.StartCount == startsBeforeClose,
            "Close during selection does not restart");
        Require(
            harness.Controller.State == PreviewLifecycleState.Disposed,
            "Close during selection disposes");
    }

    private static async Task FullScreenReconfigurationIsAutomaticAsync()
    {
        Harness harness = new();
        await harness.InitializeAsync();
        await harness.StartAsync();
        await harness.Controller.ReconfigureRegionAsync(
            (_, _) => GeometrySelectionResult.Confirmed(
                harness.CustomGeometry),
            harness.RequestedSettings);

        PreviewLifecycleResult result =
            await harness.Controller.ReconfigureGeometryAsync(
                harness.FullScreenGeometry,
                harness.RequestedSettings);

        Require(result.Succeeded, "full-screen automatic transaction");
        Require(
            !harness.Controller.IsCustomRegionPreview,
            "full-screen becomes current");
        Require(
            harness.Native.StartCount == 3,
            "full-screen reconfiguration automatically restarts");
        await harness.Controller.CloseAsync();
    }

    private static async Task CustomRegionForcesSafeRuntimeSettingsAsync()
    {
        Harness harness = new();
        await harness.InitializeAsync();
        await harness.StartAsync();

        await harness.Controller.ReconfigureRegionAsync(
            (_, _) => GeometrySelectionResult.Confirmed(
                harness.CustomGeometry),
            harness.RequestedSettings);

        Require(
            harness.Native.CursorModes.Last() ==
                NativeMethods.CursorMode.SystemCursor,
            "custom region forces SystemCursor");
        Require(
            harness.CameraServices.Last().FollowEnabled == false,
            "custom region forces Follow Off");
        Require(
            harness.Availability.Last() == false,
            "custom region disables Camera hotkeys");
        Require(
            harness.Controller.IsCustomRegionPreview,
            "custom region state is explicit");
        await harness.Controller.CloseAsync();
    }

    private static async Task
        FullScreenSelectionRequestHasNoInitialSelectionAsync()
    {
        Harness harness = new();
        await harness.InitializeAsync();
        await harness.StartAsync();
        ulong revision = harness.Controller.CurrentGeometryRevision;
        int geometrySets = harness.Native.GeometrySetCount;

        PreviewLifecycleResult result =
            await harness.Controller.ReconfigureRegionAsync(
                (request, _) =>
                {
                    Require(
                        request.CurrentRangeMode ==
                            CaptureRangeMode.FullScreen,
                        "full-screen request mode");
                    Require(
                        request.RollbackGeometry is not null &&
                        SessionGeometryNativeV1.ContentEquals(
                            request.RollbackGeometry,
                            harness.FullScreenGeometry),
                        "full-screen rollback geometry preserved");
                    Require(
                        request.InitialSelection is null &&
                        !request.HasInitialSelection,
                        "full-screen InitialSelection is empty");
                    return GeometrySelectionResult.Cancelled();
                },
                harness.RequestedSettings);

        Require(result.Succeeded, "full-screen empty selection cancels");
        Require(
            harness.Controller.CurrentGeometryRevision == revision &&
            harness.Native.GeometrySetCount == geometrySets,
            "empty full-screen cancel creates no revision or geometry");
        await harness.Controller.CloseAsync();
    }

    private static async Task
        CustomRegionSelectionRequestKeepsEditableRegionAsync()
    {
        Harness harness = new();
        await harness.InitializeAsync();
        await harness.StartAsync();
        await harness.Controller.ReconfigureRegionAsync(
            (_, _) => GeometrySelectionResult.Confirmed(
                harness.CustomGeometry),
            harness.RequestedSettings);

        PreviewLifecycleResult result =
            await harness.Controller.ReconfigureRegionAsync(
                (request, _) =>
                {
                    Require(
                        request.CurrentRangeMode ==
                            CaptureRangeMode.CustomRegion,
                        "custom request mode");
                    Require(
                        request.RollbackGeometry is not null &&
                        SessionGeometryNativeV1.ContentEquals(
                            request.RollbackGeometry,
                            harness.CustomGeometry),
                        "custom rollback geometry preserved");
                    Require(
                        request.InitialSelection ==
                            harness.CustomGeometry.CaptureRegion,
                        "custom InitialSelection is old custom region");
                    Require(
                        request.InitialSelection !=
                            harness.FullScreenGeometry.CaptureRegion,
                        "custom InitialSelection is not source full-screen");
                    return GeometrySelectionResult.Cancelled();
                },
                harness.RequestedSettings);

        Require(result.Succeeded, "custom re-entry cancellation restores");
        Require(
            harness.Controller.CurrentRangeMode ==
                CaptureRangeMode.CustomRegion,
            "custom mode remains current");
        await harness.Controller.CloseAsync();
    }

    private static void NoSelectionCannotConfirmUntilDrag()
    {
        RegionSelectionStateMachine state = new();
        CaptureRegion? selection = null;
        Require(
            state.State == RegionSelectionState.NoSelection,
            "selector begins NoSelection");
        Require(
            !RegionSelectionAvailability.HasSelection(
                selection,
                state.State),
            "NoSelection has no confirmable candidate");
        Require(
            !state.TryTransition(RegionSelectionState.Confirmed),
            "Enter cannot confirm NoSelection");

        Require(
            state.TryTransition(RegionSelectionState.Drawing),
            "drag begins from NoSelection");
        Require(
            RegionSelectionMath.TryCreateFromDrag(
                new PhysicalPixelPoint(300, 200),
                new PhysicalPixelPoint(1301, 901),
                1920,
                1080,
                RegionAspectMode.Free,
                out CaptureRegion drawn),
            "drag creates candidate");
        selection = drawn;
        Require(
            state.TryTransition(RegionSelectionState.Selected),
            "drag completion selects candidate");
        Require(
            RegionSelectionAvailability.HasSelection(
                selection,
                state.State),
            "drag enables confirmation");
        Require(
            drawn.Left == 300 &&
            drawn.Top == 200 &&
            drawn.Width == 1001 &&
            drawn.Height == 701,
            "drag dimensions and coordinates preserved");
    }

    private static void NewSelectionClearsCandidate()
    {
        RegionSelectionStateMachine state = new();
        Require(
            state.TryTransition(RegionSelectionState.Drawing) &&
            state.TryTransition(RegionSelectionState.Selected),
            "existing custom candidate state");
        CaptureRegion? selection = CaptureRegion.Create(
            300,
            200,
            1001,
            701,
            1920,
            1080);

        Require(
            state.TryTransition(RegionSelectionState.Drawing) &&
            state.TryTransition(RegionSelectionState.NoSelection),
            "new-selection command reaches NoSelection");
        selection = null;
        Require(
            !RegionSelectionAvailability.HasSelection(
                selection,
                state.State),
            "new-selection command does not manufacture full-screen");
    }

    private static void RegionSelectionButtonStatesAreExplicit()
    {
        Require(
            RegionSelectionAvailability.CanSelectRegion(
                false,
                PreviewLifecycleState.Previewing,
                false),
            "Previewing allows region selection");
        Require(
            !RegionSelectionAvailability.CanSelectRegion(
                false,
                PreviewLifecycleState.SelectingRegion,
                false),
            "SelectingRegion disables region selection");
        Require(
            !RegionSelectionAvailability.CanSelectRegion(
                false,
                PreviewLifecycleState.Reconfiguring,
                false),
            "Reconfiguring disables region selection");
    }

    private static void RequireOrdered(
        IReadOnlyList<string> log,
        params string[] expected)
    {
        int previous = -1;
        foreach (string item in expected)
        {
            int index = -1;
            for (int candidate = previous + 1; candidate < log.Count; candidate++)
            {
                if (log[candidate] == item)
                {
                    index = candidate;
                    break;
                }
            }
            Require(index >= 0, $"missing ordered event {item}");
            previous = index;
        }
    }

    private static void Require(bool condition, string name)
    {
        if (!condition)
        {
            throw new InvalidOperationException(
                $"Preview lifecycle test failed: {name}");
        }
    }

    internal sealed class Harness
    {
        internal readonly List<string> Log = [];
        internal readonly List<bool> Availability = [];
        internal readonly List<FakeCameraService> CameraServices = [];
        internal readonly List<ManagedStartupDiagnosticEvent>
            StartupDiagnostics = [];
        internal readonly FakeNativeSession Native;
        internal readonly PreviewLifecycleController Controller;
        internal readonly SessionGeometry FullScreenGeometry;
        internal readonly SessionGeometry CustomGeometry;
        internal readonly PreviewRuntimeSettings RequestedSettings =
            new(
                CameraEnabled: true,
                FollowEnabled: true,
                CursorMode: NativeMethods.CursorMode.CustomCursor,
                CameraCommandsAvailable: true);
        internal int SessionFactoryCount;

        internal Harness(
            bool blockStart = false,
            bool blockStop = false,
            bool blockRecordingStop = false)
        {
            Native = new FakeNativeSession(
                Log,
                blockStart,
                blockStop,
                blockRecordingStop);
            CaptureDisplaySnapshot display =
                CaptureDisplaySnapshot.Create(
                    "DISPLAY1",
                    0,
                    0,
                    1920,
                    1080,
                    96,
                    96);
            FullScreenGeometry =
                SessionGeometry.CreateFullScreen(display);
            CaptureRegion custom = CaptureRegion.Create(
                300,
                200,
                1001,
                701,
                display.Width,
                display.Height);
            CustomGeometry = SessionGeometry.Create(
                display,
                custom,
                OutputCanvas.CreateIdentity(custom));
            Controller = new PreviewLifecycleController(
                () =>
                {
                    SessionFactoryCount++;
                    return Native;
                },
                (_, followEnabled) =>
                {
                    FakeCameraService service = new(Log, followEnabled);
                    CameraServices.Add(service);
                    return service;
                },
                new FixedTargetCameraController(1000),
                available =>
                {
                    Availability.Add(available);
                    Log.Add($"hotkey:{available.ToString().ToLowerInvariant()}");
                },
                writeStartupDiagnostic: item =>
                {
                    StartupDiagnostics.Add(item);
                    ManagedStartupDiagnostics.Write(item);
                },
                notificationContext: null);
        }

        internal async Task InitializeAsync(bool setDefaultGeometry = true)
        {
            PreviewLifecycleResult initialized =
                await Controller.InitializeAsync();
            Require(initialized.Succeeded, "harness initialized");
            if (setDefaultGeometry)
            {
                PreviewLifecycleResult geometry =
                    await Controller.SetDesiredGeometryAsync(
                        FullScreenGeometry);
                Require(geometry.Succeeded, "default geometry accepted");
            }
        }

        internal Task<PreviewLifecycleResult> StartAsync() =>
            Controller.StartAsync(
                true,
                true,
                NativeMethods.CursorMode.CustomCursor);
    }

    internal sealed unsafe class FakeNativeSession : IPreviewNativeSession
    {
        private readonly List<string> _log;
        private readonly ManualResetEventSlim? _startRelease;
        private readonly ManualResetEventSlim? _stopRelease;
        private readonly ManualResetEventSlim? _recordingStopRelease;
        private ManualResetEventSlim? _nextStartRelease;
        private int _activeLifecycle;
        private int _maxConcurrentLifecycle;

        internal FakeNativeSession(
            List<string> log,
            bool blockStart,
            bool blockStop,
            bool blockRecordingStop)
        {
            _log = log;
            if (blockStart)
            {
                _startRelease = new ManualResetEventSlim(false);
            }
            if (blockStop)
            {
                _stopRelease = new ManualResetEventSlim(false);
            }
            if (blockRecordingStop)
            {
                _recordingStopRelease = new ManualResetEventSlim(false);
            }
        }

        internal Queue<NativeMethods.Result> StartResults { get; } = [];
        internal Queue<NativeMethods.Result> GeometryResults { get; } = [];
        internal List<SessionGeometryNativeV1> GeometryHistory { get; } = [];
        internal List<NativeMethods.CursorMode> CursorModes { get; } = [];
        internal List<bool> RecordCursorVisibilityCalls { get; } = [];
        internal List<CaptureTarget> CaptureTargets { get; } = [];
        internal List<(NativeMethods.WindowStageOrientation Orientation,
            NativeMethods.WindowStageLevel Level)> StagePoses { get; } = [];
        internal List<(NativeMethods.WindowStageOrientation Orientation,
            NativeMethods.WindowStageLevel Level,
            bool Active)> ShowcasePoseRequests { get; } = [];
        internal List<NativeMethods.WindowShowcaseBackgroundPreset>
            BackgroundPresets { get; } = [];
        internal List<string> CustomBackgroundPaths { get; } = [];
        internal List<string?> RecordingOutputRoots { get; } = [];
        internal List<uint> RecordingFrameRates { get; } = [];
        internal ManualResetEventSlim StartEntered { get; } = new(false);
        internal ManualResetEventSlim StopEntered { get; } = new(false);
        internal ManualResetEventSlim RecordingStopEntered { get; } = new(false);
        internal int StartCount { get; private set; }
        internal int StopCount { get; private set; }
        internal int ResizeCount { get; private set; }
        internal int GeometrySetCount { get; private set; }
        internal int CameraStateSetCount { get; private set; }
        internal double AppliedZoom { get; set; } = CameraSettings.WideZoom;
        internal int DisposeCount { get; private set; }
        internal int RecordingStartCount { get; private set; }
        internal int RecordingStopCount { get; private set; }
        internal int RecordingFinalizeCount { get; private set; }
        internal int RecordingPauseCount { get; private set; }
        internal int RecordingResumeCount { get; private set; }
        internal int MaxConcurrentLifecycle => Volatile.Read(ref _maxConcurrentLifecycle);
        internal (int Width, int Height) LastResize { get; private set; }
        internal string LastError { get; set; } = string.Empty;
        internal NativeMethods.PreviewStats Stats { get; set; }
        internal Action? GeometryObserved { get; set; }

        public NativeMethods.Result Start()
        {
            EnterLifecycle();
            try
            {
                StartCount++;
                _log.Add("native:start");
                StartEntered.Set();
                _startRelease?.Wait(TimeSpan.FromSeconds(5));
                Interlocked.Exchange(
                    ref _nextStartRelease,
                    null)?.Wait(TimeSpan.FromSeconds(5));
                return StartResults.Count == 0
                    ? NativeMethods.Result.Ok
                    : StartResults.Dequeue();
            }
            finally
            {
                ExitLifecycle();
            }
        }

        public NativeMethods.Result Stop()
        {
            EnterLifecycle();
            try
            {
                StopCount++;
                _log.Add("native:stop");
                StopEntered.Set();
                _stopRelease?.Wait(TimeSpan.FromSeconds(5));
                return NativeMethods.Result.Ok;
            }
            finally
            {
                ExitLifecycle();
            }
        }

        private NativeMethods.RecordingSnapshot _recordingSnapshot = new()
        {
            State = NativeMethods.RecordingState.Idle,
            LastResult = NativeMethods.Result.Ok,
        };
        private RecordCursorVisibilitySnapshot _recordCursorVisibility =
            new(true, true, 0);

        public NativeMethods.Result StartRecording()
        {
            RecordingStartCount++;
            NativeMethods.RecordingSnapshot snapshot = new()
            {
                State = NativeMethods.RecordingState.Recording,
                LastResult = NativeMethods.Result.Ok,
                ActiveEncoder = 1,
            };
            string sessionId = $"preview-session-{RecordingStartCount}";
            char* target = snapshot.SessionId;
            for (int index = 0; index < sessionId.Length; index++)
            {
                target[index] = sessionId[index];
            }
            _recordingSnapshot = snapshot;
            return NativeMethods.Result.Ok;
        }

        public NativeMethods.Result SetAudioProgramMode(
            NativeMethods.AudioProgramMode mode) => NativeMethods.Result.Ok;

        public NativeMethods.Result PauseRecording()
        {
            RecordingPauseCount++;
            _recordingSnapshot.State = NativeMethods.RecordingState.Paused;
            _recordingSnapshot.PauseCount = (ulong)RecordingPauseCount;
            return NativeMethods.Result.Ok;
        }

        public NativeMethods.Result ResumeRecording()
        {
            RecordingResumeCount++;
            _recordingSnapshot.State = NativeMethods.RecordingState.Recording;
            return NativeMethods.Result.Ok;
        }

        public NativeMethods.Result StopRecording()
        {
            RecordingStopCount++;
            RecordingStopEntered.Set();
            _recordingStopRelease?.Wait(TimeSpan.FromSeconds(5));
            RecordingFinalizeCount++;
            _recordingSnapshot.State =
                NativeMethods.RecordingState.Completed;
            _recordingSnapshot.OutputSuccess = 1;
            _recordingSnapshot.FinalizeAttempted = 1;
            _recordingSnapshot.FinalizeHResult = 0;
            _recordingSnapshot.FinalizeCount =
                (uint)RecordingFinalizeCount;
            _recordingSnapshot.ValidationAttempted = 1;
            _recordingSnapshot.ValidationHResult = 0;
            _recordingSnapshot.ReadyToPublish = 1;
            _recordingSnapshot.PublishAttempted = 1;
            _recordingSnapshot.PublishHResult = 0;
            _recordingSnapshot.Published = 1;
            _recordingSnapshot.ActiveEncoder = 0;
            return NativeMethods.Result.Ok;
        }

        public NativeMethods.RecordingSnapshot GetRecordingSnapshot() =>
            _recordingSnapshot;

        public NativeMethods.Result SetAudioControls(
            bool systemMuted,
            bool microphoneMuted,
            double microphoneGainDb) => NativeMethods.Result.Ok;

        public NativeMethods.AudioControlSnapshotV1
            GetAudioControlSnapshot() => new()
            {
                StructSize = NativeMethods.ExpectedAudioControlSnapshotV1Size,
                AbiVersion = NativeMethods.AudioControlsAbiVersionV1,
                MicrophoneGainLinear = 1.0,
                ProgramHeadroomCoefficient = 0.5,
                MeterWindowFrames = 4_800,
            };

        public NativeMethods.Result Resize(int width, int height)
        {
            ResizeCount++;
            LastResize = (width, height);
            _log.Add($"native:resize:{width}x{height}");
            return NativeMethods.Result.Ok;
        }

        public NativeMethods.Result SetSessionGeometry(
            in SessionGeometryNativeV1 geometry)
        {
            GeometrySetCount++;
            GeometryHistory.Add(geometry);
            _log.Add($"native:geometry:{geometry.GeometryRevision}");
            GeometryObserved?.Invoke();
            return GeometryResults.Count == 0
                ? NativeMethods.Result.Ok
                : GeometryResults.Dequeue();
        }

        public NativeMethods.Result SetCameraState(CameraState state)
        {
            CameraStateSetCount++;
            AppliedZoom = state.Zoom;
            _log.Add(
                Math.Abs(state.Zoom - CameraSettings.WideZoom) < 0.000001
                    ? "native:wide"
                    : "native:camera");
            return NativeMethods.Result.Ok;
        }

        public NativeMethods.Result SetCursorMode(
            NativeMethods.CursorMode cursorMode)
        {
            CursorModes.Add(cursorMode);
            _log.Add($"native:cursor:{cursorMode}");
            return NativeMethods.Result.Ok;
        }

        public NativeMethods.Result SetRecordCursorVisible(bool visible)
        {
            RecordCursorVisibilityCalls.Add(visible);
            ulong revision = _recordCursorVisibility.Revision;
            if (_recordCursorVisibility.RequestedVisible != visible)
            {
                revision++;
            }
            _recordCursorVisibility = new(
                visible,
                visible,
                revision);
            _log.Add($"native:record-cursor:{visible}");
            return NativeMethods.Result.Ok;
        }

        public RecordCursorVisibilitySnapshot GetRecordCursorVisible() =>
            _recordCursorVisibility;

        public NativeMethods.Result SetCaptureTarget(CaptureTarget target)
        {
            CaptureTargets.Add(target);
            _log.Add($"native:capture-target:{target.Kind}:{target.WindowHandle}");
            return NativeMethods.Result.Ok;
        }

        public NativeMethods.Result SetWindowStagePose(
            NativeMethods.WindowStageOrientation orientation,
            NativeMethods.WindowStageLevel level)
        {
            StagePoses.Add((orientation, level));
            return NativeMethods.Result.Ok;
        }

        public NativeMethods.Result SetWindowShowcasePose(
            NativeMethods.WindowStageOrientation orientation,
            NativeMethods.WindowStageLevel level,
            bool active)
        {
            StagePoses.Add((orientation, level));
            ShowcasePoseRequests.Add((orientation, level, active));
            return NativeMethods.Result.Ok;
        }

        public NativeMethods.Result SetWindowShowcaseBackgroundPreset(
            NativeMethods.WindowShowcaseBackgroundPreset preset)
        {
            BackgroundPresets.Add(preset);
            return NativeMethods.Result.Ok;
        }

        public NativeMethods.Result SetWindowShowcaseCustomBackground(
            string validatedLocalPath)
        {
            CustomBackgroundPaths.Add(validatedLocalPath);
            return NativeMethods.Result.Ok;
        }

        public NativeMethods.Result SetRecordingOutputRoot(
            string? validatedLocalPath)
        {
            RecordingOutputRoots.Add(validatedLocalPath);
            return NativeMethods.Result.Ok;
        }

        public NativeMethods.Result SetRecordingFrameRate(uint framesPerSecond)
        {
            RecordingFrameRates.Add(framesPerSecond);
            return NativeMethods.Result.Ok;
        }

        public NativeMethods.CursorStats GetCursorStats() => default;

        public NativeMethods.PreviewStats GetStats() => Stats;

        public string GetLastError() => LastError;

        public void Dispose()
        {
            DisposeCount++;
            _log.Add("native:dispose");
        }

        internal void ReleaseStart() => _startRelease?.Set();

        internal void BlockNextStart()
        {
            StartEntered.Reset();
            _nextStartRelease = new ManualResetEventSlim(false);
        }

        internal void ReleaseNextStart() =>
            _nextStartRelease?.Set();

        internal void ReleaseStop() => _stopRelease?.Set();

        internal void ReleaseRecordingStop() => _recordingStopRelease?.Set();

        private void EnterLifecycle()
        {
            int current = Interlocked.Increment(ref _activeLifecycle);
            int prior;
            do
            {
                prior = Volatile.Read(ref _maxConcurrentLifecycle);
                if (current <= prior)
                {
                    break;
                }
            }
            while (Interlocked.CompareExchange(
                ref _maxConcurrentLifecycle,
                current,
                prior) != prior);
        }

        private void ExitLifecycle() =>
            Interlocked.Decrement(ref _activeLifecycle);
    }

    internal sealed class FakeCameraService : IPreviewCameraUpdateService
    {
        private readonly List<string> _log;
        private Action<CameraState, NativeMethods.Result>? _statePublished;
        private Action<ComfortZoneFollowStep>? _followStatePublished;

        internal FakeCameraService(List<string> log, bool followEnabled)
        {
            FollowEnabled = followEnabled;
            _log = log;
            _log.Add($"camera:create:{followEnabled}");
        }

        public event Action<CameraState, NativeMethods.Result>? StatePublished
        {
            add => _statePublished += value;
            remove => _statePublished -= value;
        }

        public event Action<ComfortZoneFollowStep>? FollowStatePublished
        {
            add => _followStatePublished += value;
            remove => _followStatePublished -= value;
        }
        internal int StartCount { get; private set; }
        internal int StopCount { get; private set; }
        internal int DisposeCount { get; private set; }
        internal bool FollowEnabled { get; }

        public void SetFollowEnabled(bool enabled) =>
            _log.Add($"camera:follow:{enabled.ToString().ToLowerInvariant()}");

        public void Start()
        {
            StartCount++;
            _log.Add("camera:start");
        }

        public ValueTask StopAsync()
        {
            StopCount++;
            _log.Add("camera:stop");
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            _log.Add("camera:dispose");
            return ValueTask.CompletedTask;
        }
    }
}
