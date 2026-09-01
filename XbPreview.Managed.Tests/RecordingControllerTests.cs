using XbPreview.Host;
using XbPreview.Avalonia.Contracts;
using XbPreview.Avalonia.Views.Panels;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace XbPreview.Managed.Tests;

internal static class RecordingControllerTests
{
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate NativeMethods.Result RecordingCommandDelegate(
        nint handle);

    internal static async Task RunAsync()
    {
        await SixStateLifecycleUsesNativeFactsAsync();
        await DuplicateStartAndStopAreSingleFlightAsync();
        await PausedRefreshCatchesNativeDurationAsync();
        await CompletedSessionCanRestartAsync();
        await SnapshotReadFailurePreservesPublishedFactsAsync();
        await FailedSnapshotPreservesTerminalFactsAsync();
        await InactivePreviewStartFailsAsync();
        await CloseAndDisposeUseOneStopAsync();
        await PreviewCloseUsesManagedRecordingControllerAsync();
        RecordingSnapshotMarshalPreservesUnicodePaths();
        OutputActionsOnlyExposeCompletedExistingFiles();
        MainFormControlsUseOneCompletedSnapshot();
        RealMainFormRecordingPathRoundTrip();
    }

    internal static async Task RunP26A3Async()
    {
        await SixStateLifecycleUsesNativeFactsAsync();
        await DuplicateStartAndStopAreSingleFlightAsync();
        await CompletedSessionCanRestartAsync();
        await SnapshotReadFailurePreservesPublishedFactsAsync();
        await FailedSnapshotPreservesTerminalFactsAsync();
        await CloseAndDisposeUseOneStopAsync();
        await PreviewCloseUsesManagedRecordingControllerAsync();
        RecordingSnapshotMarshalPreservesUnicodePaths();
        OutputActionsOnlyExposeCompletedExistingFiles();
        MainFormControlsUseOneCompletedSnapshot();
    }

    internal static async Task RunP26EAsync()
    {
        await NativeStorageStopUsesSingleManagedStopAsync();
        StorageFailuresHaveActionablePresentation();
    }

    internal static async Task RunPanel4CancelRecordingAsync()
    {
        await Panel4NormalStopRegressionGateAsync();
        await Panel4CancelWhileRecordingGateAsync();
        await Panel4CancelWhilePausedGateAsync();
        await Panel4TerminalRaceGateAsync();
        await Panel4CurrentSessionIdentityGateAsync();
    }

    internal static void RunMvpAudioModeRouting()
    {
        AudioProgramModeRoutesThroughController();
    }

    internal static async Task RunMvpAudioGStreamerAsync()
    {
        MicrophoneSelectionUsesPerUserVisibleSettings();
        foreach (NativeMethods.AudioProgramMode mode in Enum.GetValues<
            NativeMethods.AudioProgramMode>())
        {
            FakeRecordingSession native = new();
            await using RecordingController controller = new(native);
            Require(controller.SetAudioProgramMode(mode) ==
                    NativeMethods.Result.Ok &&
                native.LastAudioProgramMode == mode,
                $"{mode} reaches the native GStreamer audio owner");
            ManagedRecordingSnapshot started = await controller.StartAsync();
            ManagedRecordingSnapshot stopped = await controller.StopAsync();
            Require(started.State == ManagedRecordingState.Recording &&
                stopped.State == ManagedRecordingState.Completed &&
                native.StartCount == 1 && native.StopCount == 1,
                $"{mode} uses the single native recording lifecycle");
        }

        FakeRecordingSession unavailable = new()
        {
            MicrophoneUnavailableAtStart = true,
        };
        await using RecordingController rejected = new(unavailable);
        Require(rejected.SetAudioProgramMode(
                NativeMethods.AudioProgramMode.MicrophoneOnly) ==
                NativeMethods.Result.Ok,
            "MicrophoneOnly reaches the native device availability gate");
        ManagedRecordingSnapshot failed = await rejected.StartAsync();
        Require(failed.State == ManagedRecordingState.Failed &&
            unavailable.SuccessfulStartCount == 0 &&
            unavailable.StopCount == 0 &&
            RecordingFailurePresentation.Describe(failed) ==
                "当前选择的麦克风不可用，请重新连接或选择其他麦克风。",
            "MicUnavailableAtStart never enters Recording and has exact user text");
    }

    private static void MicrophoneSelectionUsesPerUserVisibleSettings()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "xbpreview-microphone-settings-tests",
            Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "microphone-selection.json");
        try
        {
            MicrophoneSelection clean =
                MicrophoneSelectionSettings.Load(path);
            Require(
                clean.Kind == MicrophoneSelectionKind.WindowsDefault &&
                string.IsNullOrEmpty(clean.EndpointId),
                "clean user defaults visibly to Windows default without a machine endpoint");

            MicrophoneSelection selected = new(
                MicrophoneSelectionKind.ConcreteEndpoint,
                "{test-endpoint-id}",
                "Test Microphone");
            MicrophoneSelectionSettings.Save(path, selected);
            Require(
                MicrophoneSelectionSettings.Load(path) == selected,
                "explicit concrete endpoint and display name round-trip in user settings");

            MicrophoneSelectionSettings.Save(
                path,
                MicrophoneSelection.WindowsDefault);
            string serialized = File.ReadAllText(path);
            Require(
                MicrophoneSelectionSettings.Load(path).Kind ==
                    MicrophoneSelectionKind.WindowsDefault &&
                !serialized.Contains("test-endpoint-id", StringComparison.Ordinal),
                "Windows default persistence contains no concrete development endpoint");
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    internal static async Task RunPausePhaseCGateAsync(int gate)
    {
        switch (gate)
        {
            case 1:
                PausePhaseCAbiGate();
                break;
            case 2:
                PausePhaseCNativePauseGate();
                break;
            case 3:
                PausePhaseCNativeResumeGate();
                break;
            case 4:
                await PausePhaseCControllerRoundTripGateAsync();
                break;
            case 5:
                await PausePhaseCIdempotencyGateAsync();
                break;
            case 6:
                await PausePhaseCInvalidTransitionsGateAsync();
                break;
            case 7:
                await PausePhaseCStopPriorityGateAsync();
                break;
            case 8:
                await PausePhaseCClosePriorityGateAsync();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(gate));
        }
    }

    private static unsafe void PausePhaseCAbiGate()
    {
        NativeMethods.ValidateManagedLayout();
        Require(
            NativeMethods.ApiVersion == 0x0004_0004U &&
                (int)NativeMethods.RecordingState.Idle == 0 &&
                (int)NativeMethods.RecordingState.Failed == 5 &&
                (int)NativeMethods.RecordingState.Pausing == 6 &&
                (int)NativeMethods.RecordingState.Paused == 7 &&
                (int)NativeMethods.RecordingState.Resuming == 8 &&
                (int)NativeMethods.RecordingState.UserCancelled == 9,
            "Gate 1 API 4.4 and recording enum values are frozen");
        Require(
            sizeof(NativeMethods.RecordingSnapshot) == 2856 &&
                Marshal.OffsetOf<NativeMethods.RecordingSnapshot>(
                    nameof(NativeMethods.RecordingSnapshot.StartUtc100ns)).
                    ToInt32() == 16 &&
                Marshal.OffsetOf<NativeMethods.RecordingSnapshot>(
                    nameof(NativeMethods.RecordingSnapshot.PauseCount)).
                    ToInt32() == 1240 &&
                Marshal.OffsetOf<NativeMethods.RecordingSnapshot>(
                    nameof(NativeMethods.RecordingSnapshot.TotalPaused100ns)).
                    ToInt32() == 1248 &&
                Marshal.OffsetOf<NativeMethods.RecordingSnapshot>(
                    nameof(NativeMethods.RecordingSnapshot.PublishedPath)).
                    ToInt32() == 2336,
            "Gate 1 Snapshot size and old/reserved offsets are preserved");
        Require(
            !NativePreviewSession.SupportsPauseResumeVersion(0x0004_0003U) &&
                NativePreviewSession.SupportsPauseResumeVersion(0x0004_0004U) &&
                NativePreviewSession.SupportsPauseResumeVersion(0x0004_0005U) &&
                !NativePreviewSession.SupportsPauseResumeVersion(0x0005_0004U),
            "Gate 1 managed session enforces the native 4.4 feature gate");

        string nativePath = Path.Combine(
            AppContext.BaseDirectory,
            NativeMethods.DllName);
        nint module = NativeLibrary.Load(nativePath);
        try
        {
            nint pauseAddress = NativeLibrary.GetExport(
                module, "XbPreview_PauseRecording");
            nint resumeAddress = NativeLibrary.GetExport(
                module, "XbPreview_ResumeRecording");
            RecordingCommandDelegate pause =
                Marshal.GetDelegateForFunctionPointer<
                    RecordingCommandDelegate>(pauseAddress);
            RecordingCommandDelegate resume =
                Marshal.GetDelegateForFunctionPointer<
                    RecordingCommandDelegate>(resumeAddress);
            Require(
                pause(nint.Zero) == NativeMethods.Result.InvalidHandle &&
                    resume(nint.Zero) == NativeMethods.Result.InvalidHandle,
                "Gate 1 Pause/Resume exports exist and reject null handles");
        }
        finally
        {
            NativeLibrary.Free(module);
        }
        Console.WriteLine("C-GATE-1-ABI-VERSION = PASS");
    }

    private static void PausePhaseCNativePauseGate()
    {
        FakeRecordingSession native = new();
        Require(
            native.StartRecording() == NativeMethods.Result.Ok,
            "Gate 2 native recording starts");
        native.SetElapsed(TimeSpan.FromSeconds(2));
        NativeMethods.RecordingSnapshot before =
            native.GetRecordingSnapshot();
        Require(
            native.PauseRecording() == NativeMethods.Result.Ok &&
                native.GetRecordingSnapshot().State ==
                    NativeMethods.RecordingState.Pausing,
            "Gate 2 accepted command publishes Pausing");
        native.AcknowledgePause();
        NativeMethods.RecordingSnapshot paused =
            native.GetRecordingSnapshot();
        Require(
            paused.State == NativeMethods.RecordingState.Paused &&
                paused.PauseCount == 1 && paused.ActiveEncoder == 1 &&
                paused.Elapsed100ns == before.Elapsed100ns &&
                paused.GetSessionId() == before.GetSessionId() &&
                paused.GetWorkingPath() == before.GetWorkingPath(),
            "Gate 2 full native acknowledgement publishes stable Paused facts");
        Console.WriteLine("C-GATE-2-NATIVE-PAUSE = PASS");
    }

    private static void PausePhaseCNativeResumeGate()
    {
        FakeRecordingSession native = new();
        _ = native.StartRecording();
        native.SetElapsed(TimeSpan.FromSeconds(3));
        _ = native.PauseRecording();
        native.AcknowledgePause();
        NativeMethods.RecordingSnapshot paused =
            native.GetRecordingSnapshot();
        Require(
            native.ResumeRecording() == NativeMethods.Result.Ok &&
                native.GetRecordingSnapshot().State ==
                    NativeMethods.RecordingState.Resuming,
            "Gate 3 accepted command publishes Resuming");
        native.AcknowledgeFullAvResume(TimeSpan.FromSeconds(5));
        NativeMethods.RecordingSnapshot resumed =
            native.GetRecordingSnapshot();
        Require(
            resumed.State == NativeMethods.RecordingState.Recording &&
                resumed.Elapsed100ns == paused.Elapsed100ns &&
                resumed.PauseCount == 1 &&
                resumed.TotalPaused100ns ==
                    (ulong)TimeSpan.FromSeconds(5).Ticks &&
                resumed.ActiveEncoder == 1,
            "Gate 3 Recording returns only after the full A/V Resume acknowledgement");
        Console.WriteLine("C-GATE-3-NATIVE-RESUME = PASS");
    }

    private static async Task PausePhaseCControllerRoundTripGateAsync()
    {
        FakeRecordingSession native = new();
        await using RecordingController controller = new(native);
        ManagedRecordingSnapshot recording = await controller.StartAsync();
        native.SetElapsed(TimeSpan.FromSeconds(2));
        recording = controller.RefreshSnapshot();
        Task<ManagedRecordingSnapshot> pause = controller.PauseAsync();
        Require(
            native.PauseEntered.Wait(TimeSpan.FromSeconds(2)),
            "Gate 4 native Pause command entered");
        ManagedRecordingSnapshot pausing = controller.RefreshSnapshot();
        Require(
            pausing.State == ManagedRecordingState.Pausing,
            "Gate 4 controller observes Pausing through Snapshot");
        native.AcknowledgePause();
        ManagedRecordingSnapshot paused =
            await pause.WaitAsync(TimeSpan.FromSeconds(2));
        Require(
            paused.State == ManagedRecordingState.Paused &&
                paused.Elapsed == recording.Elapsed &&
                paused.PauseCount == 1 && paused.ActiveEncoder &&
                paused.SessionId == recording.SessionId &&
                paused.WorkingPath == recording.WorkingPath,
            "Gate 4 Paused freezes elapsed and retains session ownership");

        Task<ManagedRecordingSnapshot> resume = controller.ResumeAsync();
        Require(
            native.ResumeEntered.Wait(TimeSpan.FromSeconds(2)),
            "Gate 4 native Resume command entered");
        ManagedRecordingSnapshot resuming = controller.RefreshSnapshot();
        Require(
            resuming.State == ManagedRecordingState.Resuming &&
                resuming.Elapsed == paused.Elapsed,
            "Gate 4 Resuming remains frozen before full A/V ack");
        native.AcknowledgeFullAvResume(TimeSpan.FromSeconds(4));
        ManagedRecordingSnapshot resumed =
            await resume.WaitAsync(TimeSpan.FromSeconds(2));
        Require(
            resumed.State == ManagedRecordingState.Recording &&
                resumed.PauseCount == 1 &&
                resumed.TotalPaused == TimeSpan.FromSeconds(4) &&
                resumed.SessionId == recording.SessionId &&
                resumed.WorkingPath == recording.WorkingPath,
            "Gate 4 controller completes the Pause/Resume round trip from native facts");
        await controller.StopAsync();
        Console.WriteLine("C-GATE-4-CONTROLLER-ROUNDTRIP = PASS");
    }

    private static async Task PausePhaseCIdempotencyGateAsync()
    {
        FakeRecordingSession native = new();
        await using RecordingController controller = new(native);
        _ = await controller.StartAsync();
        Task<ManagedRecordingSnapshot> firstPause = controller.PauseAsync();
        Require(native.PauseEntered.Wait(TimeSpan.FromSeconds(2)),
            "Gate 5 Pause entered");
        Task<ManagedRecordingSnapshot> secondPause = controller.PauseAsync();
        Require(ReferenceEquals(firstPause, secondPause),
            "Gate 5 duplicate Pausing call shares one task");
        native.AcknowledgePause();
        _ = await firstPause.WaitAsync(TimeSpan.FromSeconds(2));
        ManagedRecordingSnapshot pausedAgain = await controller.PauseAsync();
        Require(
            pausedAgain.State == ManagedRecordingState.Paused &&
                native.PauseCount == 1 && pausedAgain.PauseCount == 1,
            "Gate 5 Pause in Paused is idempotent without count inflation");

        Task<ManagedRecordingSnapshot> firstResume = controller.ResumeAsync();
        Require(native.ResumeEntered.Wait(TimeSpan.FromSeconds(2)),
            "Gate 5 Resume entered");
        Task<ManagedRecordingSnapshot> secondResume = controller.ResumeAsync();
        Require(ReferenceEquals(firstResume, secondResume),
            "Gate 5 duplicate Resuming call shares one task");
        native.AcknowledgeFullAvResume(TimeSpan.FromSeconds(1));
        _ = await firstResume.WaitAsync(TimeSpan.FromSeconds(2));
        Require(
            native.ResumeCount == 1 && native.PauseCount == 1,
            "Gate 5 duplicate commands reach native exactly once");
        await controller.StopAsync();
        Console.WriteLine("C-GATE-5-IDEMPOTENCY = PASS");
    }

    private static async Task PausePhaseCInvalidTransitionsGateAsync()
    {
        FakeRecordingSession native = new();
        await using RecordingController controller = new(native);
        ManagedRecordingSnapshot idlePause = await controller.PauseAsync();
        Require(
            idlePause.State == ManagedRecordingState.Idle &&
                idlePause.LastResult == NativeMethods.Result.InvalidState,
            "Gate 6 Pause from Idle is explicit InvalidState");
        _ = await controller.StartAsync();
        ManagedRecordingSnapshot recordingResume =
            await controller.ResumeAsync();
        Require(
            recordingResume.State == ManagedRecordingState.Recording &&
                recordingResume.LastResult == NativeMethods.Result.InvalidState,
            "Gate 6 Resume from Recording is explicit InvalidState");

        Task<ManagedRecordingSnapshot> pause = controller.PauseAsync();
        Require(native.PauseEntered.Wait(TimeSpan.FromSeconds(2)),
            "Gate 6 enters Pausing");
        ManagedRecordingSnapshot pausingResume =
            await controller.ResumeAsync();
        Require(
            pausingResume.LastResult == NativeMethods.Result.InvalidState &&
                native.ResumeCount == 0,
            "Gate 6 Resume from Pausing is rejected without native command");
        native.AcknowledgePause();
        _ = await pause.WaitAsync(TimeSpan.FromSeconds(2));
        Task<ManagedRecordingSnapshot> resume = controller.ResumeAsync();
        Require(native.ResumeEntered.Wait(TimeSpan.FromSeconds(2)),
            "Gate 6 enters Resuming");
        ManagedRecordingSnapshot resumingPause =
            await controller.PauseAsync();
        Require(
            resumingPause.LastResult == NativeMethods.Result.InvalidState &&
                native.PauseCount == 1,
            "Gate 6 Pause from Resuming is rejected without native command");
        native.AcknowledgeFullAvResume(TimeSpan.FromSeconds(1));
        _ = await resume.WaitAsync(TimeSpan.FromSeconds(2));
        await controller.StopAsync();
        Console.WriteLine("C-GATE-6-INVALID-TRANSITIONS = PASS");
    }

    private static async Task PausePhaseCStopPriorityGateAsync()
    {
        await StopFromPauseStateAsync(ManagedRecordingState.Pausing);
        await StopFromPauseStateAsync(ManagedRecordingState.Paused);
        await StopFromPauseStateAsync(ManagedRecordingState.Resuming);
        Console.WriteLine("C-GATE-7-STOP-PRIORITY = PASS");
    }

    private static async Task StopFromPauseStateAsync(
        ManagedRecordingState stopFrom)
    {
        FakeRecordingSession native = new();
        await using RecordingController controller = new(native);
        _ = await controller.StartAsync();
        Task<ManagedRecordingSnapshot> pause = controller.PauseAsync();
        Require(native.PauseEntered.Wait(TimeSpan.FromSeconds(2)),
            "Gate 7 Pause command entered");
        if (stopFrom != ManagedRecordingState.Pausing)
        {
            native.AcknowledgePause();
            _ = await pause.WaitAsync(TimeSpan.FromSeconds(2));
        }
        Task<ManagedRecordingSnapshot>? resume = null;
        if (stopFrom == ManagedRecordingState.Resuming)
        {
            resume = controller.ResumeAsync();
            Require(native.ResumeEntered.Wait(TimeSpan.FromSeconds(2)),
                "Gate 7 Resume command entered");
        }
        Require(
            controller.RefreshSnapshot().State == stopFrom,
            "Gate 7 reaches requested preempted state");
        ManagedRecordingSnapshot stopped =
            await controller.StopAsync().WaitAsync(TimeSpan.FromSeconds(2));
        _ = await pause.WaitAsync(TimeSpan.FromSeconds(2));
        if (resume is not null)
        {
            _ = await resume.WaitAsync(TimeSpan.FromSeconds(2));
        }
        Require(
            stopped.State == ManagedRecordingState.Completed &&
                native.StopCount == 1 && native.FinalizeCount == 1 &&
                !controller.HasPendingOperation,
            $"Gate 7 Stop from {stopFrom} preempts control and finalizes once");
    }

    private static async Task PausePhaseCClosePriorityGateAsync()
    {
        FakeRecordingSession native = new();
        RecordingController controller = new(native);
        _ = await controller.StartAsync();
        Task<ManagedRecordingSnapshot> pause = controller.PauseAsync();
        Require(native.PauseEntered.Wait(TimeSpan.FromSeconds(2)),
            "Gate 8 Pause entered");
        native.AcknowledgePause();
        _ = await pause.WaitAsync(TimeSpan.FromSeconds(2));
        ManagedRecordingSnapshot closed =
            await controller.StopForCloseAsync().WaitAsync(
                TimeSpan.FromSeconds(2));
        await controller.DisposeAsync();
        Require(
            closed.State == ManagedRecordingState.Completed &&
                native.StopCount == 1 && native.FinalizeCount == 1 &&
                native.ResumeCount == 0 && controller.IsDisposed &&
                !controller.HasPendingOperation,
            "Gate 8 Paused Close reuses Stop, never resumes, and leaves no task");
        Console.WriteLine("C-GATE-8-CLOSE-PRIORITY = PASS");
    }

    private static void AudioProgramModeRoutesThroughController()
    {
        FakeRecordingSession native = new();
        RecordingController controller = new(native);
        Require(
            controller.SetAudioProgramMode(
                NativeMethods.AudioProgramMode.SystemOnly) ==
                NativeMethods.Result.Ok &&
            native.AudioProgramModeSetCount == 1 &&
            native.LastAudioProgramMode ==
                NativeMethods.AudioProgramMode.SystemOnly,
            "selected shell mode reaches the native recording owner exactly");
    }

    private static async Task NativeStorageStopUsesSingleManagedStopAsync()
    {
        FakeRecordingSession native = new() { BlockStop = true };
        await using RecordingController controller = new(native);
        await controller.StartAsync();
        native.SetNativeStopping(
            unchecked((int)0x80070070),
            "Recording storage is critically low.");
        ManagedRecordingSnapshot observed = controller.RefreshSnapshot();
        Require(observed.State == ManagedRecordingState.Stopping,
            "native critical storage state maps to Stopping");
        Require(native.StopEntered.Wait(TimeSpan.FromSeconds(2)),
            "refresh starts the formal managed Stop task");
        for (int index = 0; index < 5; index++)
        {
            _ = controller.RefreshSnapshot();
        }
        Task<ManagedRecordingSnapshot> shared = controller.StopAsync();
        native.StopRelease.Set();
        ManagedRecordingSnapshot terminal = await shared;
        Require(terminal.State == ManagedRecordingState.Completed &&
            native.StopCount == 1 && native.FinalizeCount == 1,
            "native storage stop and repeated refresh share one Stop/Finalize");
    }

    private static void StorageFailuresHaveActionablePresentation()
    {
        ManagedRecordingSnapshot diskFull =
            ManagedRecordingSnapshot.Idle with
            {
                State = ManagedRecordingState.Failed,
                FailureHResult = unchecked((int)0x80070070),
                ErrorMessage = "Encoded sample write failed.",
            };
        ManagedRecordingSnapshot denied = diskFull with
        {
            FailureHResult = unchecked((int)0x80070005),
        };
        ManagedRecordingSnapshot unavailable = diskFull with
        {
            FailureHResult = unchecked((int)0x8007048F),
        };
        Require(
            RecordingFailurePresentation.Describe(diskFull).Contains(
                "磁盘空间不足", StringComparison.Ordinal) &&
            RecordingFailurePresentation.Describe(denied).Contains(
                "不可写", StringComparison.Ordinal) &&
            RecordingFailurePresentation.Describe(unavailable).Contains(
                "不可用", StringComparison.Ordinal),
            "storage failures use actionable user text without raw HRESULT");
    }

    internal static unsafe void RecordingSnapshotMarshalPreservesUnicodePaths()
    {
        string legacy =
            "E:\\小白录屏器\\xiaobai-screen-recorder\\artifacts\\p2.5a-recordings\\旧输出.mp4";
        string working =
            "E:\\小白录屏器\\xiaobai-screen-recorder\\artifacts\\p2.6a-recordings\\测试工作.partial.mp4";
        string planned =
            "E:\\小白录屏器\\xiaobai-screen-recorder\\artifacts\\p2.6a-recordings\\测试最终.mp4";
        string published =
            "E:\\小白录屏器\\xiaobai-screen-recorder\\artifacts\\p2.6a-recordings\\已发布.mp4";
        NativeMethods.RecordingSnapshot snapshot = new()
        {
            StructSize = (uint)sizeof(NativeMethods.RecordingSnapshot),
            ApiVersion = NativeMethods.ApiVersion,
            State = NativeMethods.RecordingState.Completed,
            OutputSuccess = 1,
            ReadyToPublish = 1,
            Published = 1,
            PublishAttempted = 1,
            PublishHResult = 0,
            ValidationAttempted = 1,
            ValidationHResult = 0,
        };
        CopyFixedString(snapshot.OutputPath, 260, legacy);
        CopyFixedString(snapshot.WorkingPath, 260, working);
        CopyFixedString(snapshot.PlannedFinalPath, 260, planned);
        CopyFixedString(snapshot.PublishedPath, 260, published);
        Require(typeof(NativeMethods.RecordingSnapshot).
                StructLayoutAttribute?.CharSet == CharSet.Unicode,
            "RecordingSnapshot explicitly uses Unicode layout");
        Require(Marshal.SizeOf<NativeMethods.RecordingSnapshot>() ==
            sizeof(NativeMethods.RecordingSnapshot),
            "RecordingSnapshot marshal and managed sizes agree");
        GCHandle pinned = GCHandle.Alloc(snapshot, GCHandleType.Pinned);
        pinned.Free();

        nint buffer = Marshal.AllocHGlobal(sizeof(NativeMethods.RecordingSnapshot));
        try
        {
            Marshal.StructureToPtr(snapshot, buffer, false);
            RequireMarshaledPath(buffer, 208, legacy, "legacy output");
            RequireMarshaledPath(buffer, 1296, working, "working");
            RequireMarshaledPath(buffer, 1816, planned, "planned final");
            RequireMarshaledPath(buffer, 2336, published, "published");

            NativeMethods.RecordingSnapshot roundTrip =
                Marshal.PtrToStructure<NativeMethods.RecordingSnapshot>(buffer);
            Require(
                string.Equals(roundTrip.GetOutputPath(), legacy,
                    StringComparison.Ordinal) &&
                string.Equals(roundTrip.GetWorkingPath(), working,
                    StringComparison.Ordinal) &&
                string.Equals(roundTrip.GetPlannedFinalPath(), planned,
                    StringComparison.Ordinal) &&
                string.Equals(roundTrip.GetPublishedPath(), published,
                    StringComparison.Ordinal),
                "RecordingSnapshot unmarshals every UTF-16 path fact");

            FakeRecordingSession native = new();
            native.SetPublicationFacts(snapshot);
            RecordingController controller = new(native);
            ManagedRecordingSnapshot mapped = controller.RefreshSnapshot();
            Require(
                string.Equals(mapped.WorkingPath, working,
                    StringComparison.Ordinal) &&
                string.Equals(mapped.PlannedFinalPath, planned,
                    StringComparison.Ordinal) &&
                string.Equals(mapped.PublishedPath, published,
                    StringComparison.Ordinal) &&
                mapped.ReadyToPublish && mapped.Published &&
                mapped.PublishAttempted && mapped.PublishHResult == 0 &&
                mapped.ValidationAttempted &&
                mapped.ValidationHResult == 0,
                "Managed snapshot maps native path and publication facts");

            NativeMethods.RecordingSnapshot defaults = new();
            Require(
                defaults.ReadyToPublish == 0 &&
                defaults.Published == 0 &&
                defaults.PublishAttempted == 0 &&
                defaults.ValidationAttempted == 0 &&
                defaults.GetWorkingPath().Length == 0 &&
                defaults.GetPlannedFinalPath().Length == 0 &&
                defaults.GetPublishedPath().Length == 0,
                "new Managed ABI fields default to safe unpublished values");
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static void RequireMarshaledPath(
        nint buffer,
        int offset,
        string expected,
        string fact)
    {
        byte[] pathBytes = new byte[260 * sizeof(char)];
        Marshal.Copy(buffer + offset, pathBytes, 0, pathBytes.Length);
        string rawPath = Encoding.Unicode.GetString(pathBytes);
        int terminator = rawPath.IndexOf('\0');
        rawPath = terminator >= 0 ? rawPath[..terminator] : rawPath;
        Require(string.Equals(rawPath, expected, StringComparison.Ordinal),
            $"RecordingSnapshot marshals UTF-16 {fact} path; " +
            $"expectedLength={expected.Length}; actualLength={rawPath.Length}; " +
            $"actual={DescribeCodePoints(rawPath)}");
    }

    private static unsafe void CopyFixedString(
        char* destination,
        int capacity,
        string value)
    {
        int count = Math.Min(capacity - 1, value.Length);
        for (int index = 0; index < count; index++)
        {
            destination[index] = value[index];
        }
        destination[count] = '\0';
    }

    private static string DescribeCodePoints(string value) =>
        string.Join(
            " ",
            value.Take(24).Select(character =>
                $"U+{(int)character:X4}"));

    private static async Task SixStateLifecycleUsesNativeFactsAsync()
    {
        FakeRecordingSession native = new();
        await using RecordingController controller = new(native);
        Require(controller.CurrentSnapshot.State == ManagedRecordingState.Idle,
            "initial managed Idle");

        native.BlockStart = true;
        Task<ManagedRecordingSnapshot> start = controller.StartAsync();
        Require(native.StartEntered.Wait(TimeSpan.FromSeconds(2)),
            "native Start entered");
        ManagedRecordingSnapshot starting = controller.RefreshSnapshot();
        Require(starting.State == ManagedRecordingState.Idle,
            "ordinary refresh cannot establish the pending Start identity");
        native.StartRelease.Set();
        ManagedRecordingSnapshot recording = await start;
        Require(recording.State == ManagedRecordingState.Recording,
            "native Recording maps to managed Recording");

        native.BlockStop = true;
        Task<ManagedRecordingSnapshot> stop = controller.StopAsync();
        Require(native.StopEntered.Wait(TimeSpan.FromSeconds(2)),
            "native Stop entered");
        ManagedRecordingSnapshot stopping = controller.RefreshSnapshot();
        Require(stopping.State == ManagedRecordingState.Stopping,
            "native Stopping maps to managed Stopping");
        native.StopRelease.Set();
        ManagedRecordingSnapshot completed = await stop;
        Require(completed.State == ManagedRecordingState.Completed,
            "native Completed maps to managed Completed");

        native.SetFailed(
            unchecked((int)0x80004005),
            "injected failure",
            cleanupAttempted: true,
            cleanupSucceeded: true);
        Require(controller.RefreshSnapshot().State ==
            ManagedRecordingState.Failed,
            "native Failed maps to managed Failed");
    }

    private static async Task DuplicateStartAndStopAreSingleFlightAsync()
    {
        FakeRecordingSession native = new() { BlockStart = true };
        await using RecordingController controller = new(native);
        Task<ManagedRecordingSnapshot> firstStart = controller.StartAsync();
        Require(native.StartEntered.Wait(TimeSpan.FromSeconds(2)),
            "blocked Start entered");
        Task<ManagedRecordingSnapshot> secondStart = controller.StartAsync();
        Require(ReferenceEquals(firstStart, secondStart),
            "duplicate Start shares one task");
        native.StartRelease.Set();
        await Task.WhenAll(firstStart, secondStart);
        Require(native.StartCount == 1, "duplicate Start calls native once");

        native.BlockStop = true;
        Task<ManagedRecordingSnapshot> firstStop = controller.StopAsync();
        Require(native.StopEntered.Wait(TimeSpan.FromSeconds(2)),
            "blocked Stop entered");
        Task<ManagedRecordingSnapshot> secondStop = controller.StopAsync();
        Require(ReferenceEquals(firstStop, secondStop),
            "duplicate Stop shares one task");
        native.StopRelease.Set();
        await Task.WhenAll(firstStop, secondStop);
        Require(native.StopCount == 1, "duplicate Stop calls native once");
        Require(native.FinalizeCount == 1,
            "duplicate Stop finalizes exactly once");
    }

    private static async Task Panel4NormalStopRegressionGateAsync()
    {
        FakeRecordingSession native = new();
        await using RecordingController controller = new(native);
        _ = await controller.StartAsync();

        ManagedRecordingSnapshot completed = await controller.StopAsync();

        Require(
            completed.State == ManagedRecordingState.Completed &&
            completed.FinalizeAttempted && completed.FinalizeCount == 1 &&
            completed.ValidationAttempted && completed.ReadyToPublish &&
            completed.PublishAttempted && completed.Published &&
            completed.OutputSuccess &&
            native.StopCount == 1 && native.CancelCount == 0 &&
            native.FinalizeCount == 1 && native.PublishCount == 1 &&
            native.CleanupCount == 0,
            "Gate 1 normal Stop still finalizes, validates, publishes, and completes once");
        Console.WriteLine("PANEL4-CANCEL-GATE-1-NORMAL-STOP = PASS");
    }

    private static async Task Panel4CancelWhileRecordingGateAsync()
    {
        FakeRecordingSession native = new();
        await using RecordingController controller = new(native);
        _ = await controller.StartAsync();

        ManagedRecordingSnapshot cancelled = await controller.CancelAsync();
        RecordingReviewSnapshot cancelledUi =
            new ProductionRecordingAdapter(controller).CurrentSnapshot;

        Require(
            cancelled.State == ManagedRecordingState.UserCancelled &&
            !cancelled.IsActive && !cancelled.ActiveEncoder &&
            cancelled.ResidualOutstanding == 0 &&
            cancelled.FinalizeAttempted && cancelled.FinalizeCount == 1 &&
            !cancelled.OutputSuccess &&
            !cancelled.ValidationAttempted && !cancelled.ReadyToPublish &&
            !cancelled.PublishAttempted && !cancelled.Published &&
            native.CancelCount == 1 && native.StopCount == 0 &&
            native.FinalizeCount == 1 && native.PublishCount == 0 &&
            native.CleanupCount == 1 &&
            cancelledUi.State == RecordingReviewState.Idle &&
            string.IsNullOrEmpty(cancelledUi.ErrorMessage),
            "Gate 2 Recording cancel releases resources, discards, and returns Idle without publish");

        FakeRecordingSession failedNative = new()
        {
            CancelCleanupFailure = true,
        };
        await using RecordingController failedController = new(failedNative);
        ProductionRecordingAdapter failedAdapter = new(failedController);
        await failedAdapter.StartAsync();
        await failedAdapter.CancelAsync();

        RecordingReviewSnapshot failed = failedAdapter.CurrentSnapshot;
        for (int refresh = 0; refresh < 3; refresh++)
        {
            RecordingReviewSnapshot refreshed = failedAdapter.RefreshSnapshot();
            Require(
                refreshed.State == RecordingReviewState.Failed &&
                refreshed.ErrorMessage == failed.ErrorMessage,
                "Gate A cleanup failure remains Failed with its error after refresh");
        }
        RecordingPanelPresentationState failedPresentation =
            RecordingPanelPresentationState.Create(
                failedAdapter.CurrentSnapshot,
                commandPending: false,
                canonicalOutputRoot: @"C:\recordings",
                workingPath: string.Empty,
                plannedFinalPath: string.Empty,
                publishedPath: string.Empty,
                trayInFrame: false,
                captureAffinityResult: string.Empty,
                completionSummaryVisible: false,
                publishedFileExists: false,
                publishedDirectoryExists: false);
        NativeMethods.RecordingSnapshot durableCancelled =
            failedNative.GetRecordingSnapshot();
        Require(
            failed.State == RecordingReviewState.Failed &&
            failed.ErrorMessage == "Injected cancellation cleanup failure." &&
            failedPresentation.RecordingState == RecordingReviewState.Failed &&
            failedPresentation.ErrorVisible &&
            failedPresentation.ErrorMessage == failed.ErrorMessage &&
            durableCancelled.State == NativeMethods.RecordingState.UserCancelled &&
            durableCancelled.LastResult == NativeMethods.Result.NativeFailure,
            "Gate A durable UserCancelled cleanup failure stays truthful in managed/UI projection");
        Console.WriteLine("PANEL4-CANCEL-GATE-2-RECORDING = PASS");
        Console.WriteLine(
            "PANEL4-CANCEL-GATE-A-CLEANUP-FAILURE-REFRESH = PASS");
    }

    private static async Task Panel4CancelWhilePausedGateAsync()
    {
        FakeRecordingSession native = new();
        await using RecordingController controller = new(native);
        _ = await controller.StartAsync();
        Task<ManagedRecordingSnapshot> pause = controller.PauseAsync();
        Require(
            native.PauseEntered.Wait(TimeSpan.FromSeconds(2)),
            "Gate 3 Pause entered before cancel");
        native.AcknowledgePause();
        _ = await pause.WaitAsync(TimeSpan.FromSeconds(2));

        ManagedRecordingSnapshot cancelled = await controller.CancelAsync();

        Require(
            cancelled.State == ManagedRecordingState.UserCancelled &&
            !cancelled.ActiveEncoder &&
            cancelled.ResidualOutstanding == 0 &&
            cancelled.FinalizeAttempted && cancelled.FinalizeCount == 1 &&
            native.PauseCount == 1 && native.ResumeCount == 0 &&
            native.CancelCount == 1 && native.StopCount == 0 &&
            native.PublishCount == 0 && native.CleanupCount == 1,
            "Gate 3 Paused cancel never resumes or publishes and returns Idle");
        Console.WriteLine("PANEL4-CANCEL-GATE-3-PAUSED = PASS");
    }

    private static async Task Panel4TerminalRaceGateAsync()
    {
        FakeRecordingSession cancelFirstNative = new() { BlockCancel = true };
        await using (RecordingController controller =
            new(cancelFirstNative))
        {
            _ = await controller.StartAsync();
            Task<ManagedRecordingSnapshot> cancel = controller.CancelAsync();
            Require(
                cancelFirstNative.CancelEntered.Wait(TimeSpan.FromSeconds(2)),
                "Gate 6 Cancel-first entered native owner");
            Task<ManagedRecordingSnapshot> stop = controller.StopAsync();
            Task<ManagedRecordingSnapshot> duplicateCancel =
                controller.CancelAsync();
            Require(
                ReferenceEquals(cancel, stop) &&
                ReferenceEquals(cancel, duplicateCancel),
                "Gate 6 Cancel-first owns one shared terminal task");
            cancelFirstNative.CancelRelease.Set();
            ManagedRecordingSnapshot terminal =
                await cancel.WaitAsync(TimeSpan.FromSeconds(2));
            _ = await stop;
            _ = await duplicateCancel;
            _ = await controller.CancelAsync();
            Require(
                terminal.State == ManagedRecordingState.UserCancelled &&
                cancelFirstNative.CancelCount == 1 &&
                cancelFirstNative.StopCount == 0 &&
                cancelFirstNative.FinalizeCount == 1 &&
                cancelFirstNative.PublishCount == 0 &&
                cancelFirstNative.CleanupCount == 1,
                "Gate 6 Cancel-first never double-finalizes, publishes, or cleans");
        }

        FakeRecordingSession stopFirstNative = new() { BlockStop = true };
        await using (RecordingController controller = new(stopFirstNative))
        {
            _ = await controller.StartAsync();
            Task<ManagedRecordingSnapshot> stop = controller.StopAsync();
            Require(
                stopFirstNative.StopEntered.Wait(TimeSpan.FromSeconds(2)),
                "Gate 6 Stop-first entered native owner");
            Task<ManagedRecordingSnapshot> cancel = controller.CancelAsync();
            Require(
                ReferenceEquals(stop, cancel),
                "Gate 6 Stop-first owns one shared terminal task");
            stopFirstNative.StopRelease.Set();
            ManagedRecordingSnapshot terminal =
                await stop.WaitAsync(TimeSpan.FromSeconds(2));
            _ = await cancel;
            Require(
                terminal.State == ManagedRecordingState.Completed &&
                stopFirstNative.StopCount == 1 &&
                stopFirstNative.CancelCount == 0 &&
                stopFirstNative.FinalizeCount == 1 &&
                stopFirstNative.PublishCount == 1 &&
                stopFirstNative.CleanupCount == 0,
                "Gate 6 Stop-first preserves one normal publish and rejects discard");
        }

        FakeRecordingSession staleNative = new();
        await using (RecordingController controller = new(staleNative))
        {
            ManagedRecordingSnapshot first = await controller.StartAsync();
            staleNative.SetNativeState(NativeMethods.RecordingState.Stopping);
            staleNative.BlockNextSnapshotRead = true;
            Task<ManagedRecordingSnapshot> staleRefresh = Task.Run(
                controller.RefreshSnapshot);
            Require(
                staleNative.SnapshotReadCaptured.Wait(
                    TimeSpan.FromSeconds(2)),
                "Gate B captured the same-session stale Stopping snapshot");

            staleNative.SetUserCancelledTerminal();
            ManagedRecordingSnapshot terminal = controller.RefreshSnapshot();
            staleNative.SnapshotReadRelease.Set();
            ManagedRecordingSnapshot late = await staleRefresh.WaitAsync(
                TimeSpan.FromSeconds(2));

            Require(
                terminal.State == ManagedRecordingState.UserCancelled &&
                late == terminal && controller.CurrentSnapshot == terminal,
                "Gate B stale same-session Stopping cannot downgrade terminal");

            ManagedRecordingSnapshot second = await controller.StartAsync();
            Require(
                second.State == ManagedRecordingState.Recording &&
                second.SessionId != first.SessionId,
                "Gate B a new SessionId clears the old terminal guard");
            _ = await controller.StopAsync();
        }
        Console.WriteLine("PANEL4-CANCEL-GATE-6-TERMINAL-RACE = PASS");
        Console.WriteLine(
            "PANEL4-CANCEL-GATE-B-TERMINAL-MONOTONICITY = PASS");
    }

    private static async Task Panel4CurrentSessionIdentityGateAsync()
    {
        FakeRecordingSession native = new()
        {
            CancelCleanupFailure = true,
        };
        await using RecordingController controller = new(native);

        ManagedRecordingSnapshot sessionA = await controller.StartAsync();
        ManagedRecordingSnapshot failedA = await controller.CancelAsync();
        NativeMethods.RecordingSnapshot terminalA =
            native.GetRecordingSnapshot();
        Require(
            failedA.State == ManagedRecordingState.Failed &&
                failedA.SessionId == sessionA.SessionId &&
                !string.IsNullOrEmpty(failedA.ErrorMessage),
            "failed cancellation remains visible until an explicit new Start");

        native.CancelCleanupFailure = false;
        native.BlockStart = true;
        native.StartEntered.Reset();
        native.StartRelease.Reset();
        Task<ManagedRecordingSnapshot> startB = controller.StartAsync();
        Require(
            native.StartEntered.Wait(TimeSpan.FromSeconds(2)),
            "Session B explicit Start reached native Starting");

        NativeMethods.RecordingSnapshot canonicalStartingB =
            native.GetRecordingSnapshot();
        Require(
            canonicalStartingB.State == NativeMethods.RecordingState.Starting &&
                canonicalStartingB.GetSessionId() != sessionA.SessionId,
            "native produced the canonical Session B identity");

        foreach (NativeMethods.RecordingState state in new[]
        {
            NativeMethods.RecordingState.UserCancelled,
            NativeMethods.RecordingState.Completed,
            NativeMethods.RecordingState.Failed,
        })
        {
            NativeMethods.RecordingSnapshot staleA = terminalA;
            staleA.State = state;
            staleA.LastResult = state == NativeMethods.RecordingState.Failed
                ? NativeMethods.Result.NativeFailure
                : NativeMethods.Result.Ok;
            staleA.ActiveEncoder = 0;
            native.SetPublicationFacts(staleA);
            Require(
                controller.RefreshSnapshot() == failedA &&
                    controller.CurrentSnapshot == failedA,
                $"old Session A {state} is ignored during Start B");
        }
        Console.WriteLine(
            "PANEL4-START-AUTH-GATE-1-OLD-A-TERMINAL = PASS");

        foreach (NativeMethods.RecordingState state in new[]
        {
            NativeMethods.RecordingState.Starting,
            NativeMethods.RecordingState.Recording,
            NativeMethods.RecordingState.Paused,
            NativeMethods.RecordingState.Stopping,
        })
        {
            NativeMethods.RecordingSnapshot staleA = terminalA;
            staleA.State = state;
            staleA.LastResult = NativeMethods.Result.Ok;
            staleA.ActiveEncoder = 1;
            native.SetPublicationFacts(staleA);
            Require(
                controller.RefreshSnapshot() == failedA &&
                    controller.CurrentSnapshot == failedA,
                $"old Session A {state} is ignored during Start B");
        }
        Console.WriteLine(
            "PANEL4-START-AUTH-GATE-2-OLD-A-NONTERMINAL = PASS");

        foreach (NativeMethods.RecordingState state in new[]
        {
            NativeMethods.RecordingState.Starting,
            NativeMethods.RecordingState.Completed,
        })
        {
            NativeMethods.RecordingSnapshot historicalC =
                FakeRecordingSession.CreatePublicationSnapshot(
                    state,
                    "historical-C");
            native.SetPublicationFacts(historicalC);
            Require(
                controller.RefreshSnapshot() == failedA &&
                    controller.CurrentSnapshot == failedA,
                $"foreign historical Session C {state} cannot steal Start B");
        }
        NativeMethods.RecordingSnapshot emptyPending =
            FakeRecordingSession.CreatePublicationSnapshot(
                NativeMethods.RecordingState.Idle,
                string.Empty);
        native.SetPublicationFacts(emptyPending);
        Require(
            controller.RefreshSnapshot() == failedA &&
                controller.CurrentSnapshot == failedA,
            "empty SessionId cannot consume pending Start B authorization");
        Console.WriteLine(
            "PANEL4-START-AUTH-GATE-3-FOREIGN-AND-EMPTY = PASS");

        native.SetPublicationFacts(canonicalStartingB);
        Require(
            controller.RefreshSnapshot() == failedA &&
                controller.CurrentSnapshot == failedA,
            "ordinary refresh carrying canonical B cannot consume its token");
        native.StartRelease.Set();
        ManagedRecordingSnapshot recordingB =
            await startB.WaitAsync(TimeSpan.FromSeconds(2));
        NativeMethods.RecordingSnapshot nativeRecordingB =
            native.GetRecordingSnapshot();
        Require(
            recordingB.State == ManagedRecordingState.Recording &&
                recordingB.SessionId == canonicalStartingB.GetSessionId() &&
                string.IsNullOrEmpty(recordingB.ErrorMessage),
            "only the explicit Start result accepts canonical Session B");
        Console.WriteLine(
            "PANEL4-START-AUTH-GATE-4-EXPLICIT-RESULT = PASS");

        foreach ((string sessionId, NativeMethods.RecordingState state) in
            new[]
            {
                (sessionA.SessionId, NativeMethods.RecordingState.Recording),
                (sessionA.SessionId,
                    NativeMethods.RecordingState.UserCancelled),
                ("historical-C", NativeMethods.RecordingState.Starting),
                ("historical-C", NativeMethods.RecordingState.Completed),
            })
        {
            NativeMethods.RecordingSnapshot stale =
                FakeRecordingSession.CreatePublicationSnapshot(
                    state,
                    sessionId);
            native.SetPublicationFacts(stale);
            Require(
                controller.RefreshSnapshot() == recordingB &&
                    controller.CurrentSnapshot == recordingB,
                $"{sessionId}/{state} cannot overwrite accepted Session B");
        }
        NativeMethods.RecordingSnapshot emptyAfterAccepted = new()
        {
            StructSize = (uint)Marshal.SizeOf<
                NativeMethods.RecordingSnapshot>(),
            ApiVersion = NativeMethods.ApiVersion,
            State = NativeMethods.RecordingState.Idle,
            LastResult = NativeMethods.Result.Ok,
        };
        native.SetPublicationFacts(emptyAfterAccepted);
        ManagedRecordingSnapshot afterEmpty = controller.RefreshSnapshot();
        Require(
            afterEmpty == recordingB &&
                controller.CurrentSnapshot == recordingB,
            "empty SessionId snapshot cannot overwrite active Recording B");

        native.SetPublicationFacts(nativeRecordingB);
        Require(
            controller.RefreshSnapshot() == recordingB,
            "ordinary refresh continues the accepted Session B lifecycle");
        Task<ManagedRecordingSnapshot> pauseB = controller.PauseAsync();
        Require(
            native.PauseEntered.Wait(TimeSpan.FromSeconds(2)),
            "accepted Session B entered Pause");
        native.AcknowledgePause();
        ManagedRecordingSnapshot pausedB =
            await pauseB.WaitAsync(TimeSpan.FromSeconds(2));
        Task<ManagedRecordingSnapshot> resumeB = controller.ResumeAsync();
        Require(
            native.ResumeEntered.Wait(TimeSpan.FromSeconds(2)),
            "accepted Session B entered Resume");
        native.AcknowledgeFullAvResume(TimeSpan.FromSeconds(1));
        ManagedRecordingSnapshot resumedB =
            await resumeB.WaitAsync(TimeSpan.FromSeconds(2));
        ManagedRecordingSnapshot terminalB = await controller.StopAsync();
        Require(
            pausedB.State == ManagedRecordingState.Paused &&
                pausedB.SessionId == recordingB.SessionId &&
                resumedB.State == ManagedRecordingState.Recording &&
                resumedB.SessionId == recordingB.SessionId &&
                terminalB.State == ManagedRecordingState.Completed &&
                terminalB.SessionId == recordingB.SessionId,
            "accepted Session B lifecycle remains monotonic and complete");

        native.BlockStart = false;
        ManagedRecordingSnapshot recordingC = await controller.StartAsync();
        Require(
            recordingC.State == ManagedRecordingState.Recording &&
                recordingC.SessionId != recordingB.SessionId &&
                recordingC.SessionId != sessionA.SessionId &&
                string.IsNullOrEmpty(recordingC.ErrorMessage),
            "explicit Start switches accepted identity from terminal B to C");
        ManagedRecordingSnapshot terminalC = await controller.StopAsync();
        Console.WriteLine(
            "PANEL4-START-AUTH-GATE-5-POST-ACCEPTANCE = PASS");

        native.PreviewActive = false;
        ManagedRecordingSnapshot failedStart = await controller.StartAsync();
        Require(
            failedStart.State == ManagedRecordingState.Failed &&
                failedStart.SessionId == terminalC.SessionId &&
                failedStart.LastResult == NativeMethods.Result.InvalidState &&
                failedStart.ErrorMessage == "Preview is not active.",
            "failed explicit Start is truthful without accepting a new identity");
        Require(
            SpinWait.SpinUntil(
                () => !controller.HasPendingOperation,
                TimeSpan.FromSeconds(2)),
            "failed explicit Start clears its task and authorization");
        FieldInfo pendingAuthorization = typeof(RecordingController).GetField(
            "_pendingStartAuthorization",
            BindingFlags.Instance | BindingFlags.NonPublic) ??
            throw new InvalidOperationException(
                "pending Start authorization field was not found");
        Require(
            (long)pendingAuthorization.GetValue(controller)! == 0,
            "failed explicit Start leaves no authorization token");

        NativeMethods.RecordingSnapshot failedTokenForeign =
            FakeRecordingSession.CreatePublicationSnapshot(
                NativeMethods.RecordingState.Recording,
                "failed-token-foreign");
        native.SetPublicationFacts(failedTokenForeign);
        Require(
            controller.RefreshSnapshot() == failedStart &&
                controller.CurrentSnapshot == failedStart,
            "foreign refresh after failed Start remains rejected");

        native.PreviewActive = true;
        ManagedRecordingSnapshot nextStart = await controller.StartAsync();
        Require(
            nextStart.State == ManagedRecordingState.Recording &&
                nextStart.SessionId != terminalC.SessionId &&
                nextStart.SessionId != "failed-token-foreign" &&
                string.IsNullOrEmpty(nextStart.ErrorMessage),
            "next explicit Start accepts its own fresh Session identity");
        _ = await controller.StopAsync();
        Console.WriteLine(
            "PANEL4-START-AUTH-GATE-6-FAILED-START-RECOVERY = PASS");
    }

    private static async Task PausedRefreshCatchesNativeDurationAsync()
    {
        FakeRecordingSession native = new();
        await using RecordingController controller = new(native);
        ManagedRecordingSnapshot started = await controller.StartAsync();
        native.SetElapsed(TimeSpan.FromSeconds(2));
        ManagedRecordingSnapshot first = controller.RefreshSnapshot();
        native.SetElapsed(TimeSpan.FromSeconds(19));
        Require(controller.CurrentSnapshot.Elapsed == first.Elapsed,
            "paused UI refresh does not synthesize time");
        ManagedRecordingSnapshot caughtUp = controller.RefreshSnapshot();
        Require(caughtUp.Elapsed == TimeSpan.FromSeconds(19) &&
            caughtUp.Elapsed > started.Elapsed,

            "resumed refresh catches native PTS duration");
        await controller.StopAsync();
    }

    private static async Task CompletedSessionCanRestartAsync()
    {
        FakeRecordingSession native = new();
        await using RecordingController controller = new(native);
        ManagedRecordingSnapshot first = await controller.StartAsync();
        await controller.StopAsync();
        ManagedRecordingSnapshot second = await controller.StartAsync();
        Require(first.SessionId != second.SessionId,
            "second session GUID differs");
        Require(first.OutputPath != second.OutputPath,
            "second session output path differs");
        Require(RecordingOutputActions.Describe(second).PathText ==
            second.OutputPath,
            "second session presentation uses the second Snapshot path");
        Require(second.Elapsed == TimeSpan.Zero &&
            second.FinalizeCount == 0 &&
            string.IsNullOrEmpty(second.ErrorMessage),
            "second session does not inherit terminal facts");
        await controller.StopAsync();
    }

    private static async Task SnapshotReadFailurePreservesPublishedFactsAsync()
    {
        FakeRecordingSession native = new();
        await using RecordingController controller = new(native);
        await controller.StartAsync();
        ManagedRecordingSnapshot completed = await controller.StopAsync();
        Require(completed.State == ManagedRecordingState.Completed &&
            completed.Published && completed.OutputSuccess,
            "native publication facts are established before read failure");
        native.ThrowSnapshotReads = true;
        ManagedRecordingSnapshot preserved = controller.RefreshSnapshot();
        Require(preserved == completed &&
            preserved.PublishedPath == completed.PublishedPath,
            "snapshot read failure cannot rewrite native success as Failed");
        native.ThrowSnapshotReads = false;
    }

    private static async Task FailedSnapshotPreservesTerminalFactsAsync()
    {
        FakeRecordingSession native = new();
        await using RecordingController controller = new(native);
        await controller.StartAsync();
        native.SetFailed(
            unchecked((int)0x80070020),
            "native finalize failed",
            cleanupAttempted: true,
            cleanupSucceeded: false,
            cleanupHResult: unchecked((int)0x80070005));
        ManagedRecordingSnapshot failed = controller.RefreshSnapshot();
        Require(failed.State == ManagedRecordingState.Failed,
            "Failed state preserved");
        Require(failed.FailureHResult == unchecked((int)0x80070020) &&
            failed.OutputCleanupHResult == unchecked((int)0x80070005),
            "failure and cleanup HRESULTs remain independent");
        Require(failed.FinalizeAttempted && failed.FinalizeCount == 1 &&
            failed.OutputCleanupAttempted &&
            !failed.OutputCleanupSucceeded &&
            !failed.OutputSuccess,
            "finalize and cleanup facts map exactly");
        Require(failed.ErrorMessage == "native finalize failed",
            "native error text preserved");
    }

    private static async Task InactivePreviewStartFailsAsync()
    {
        FakeRecordingSession native = new() { PreviewActive = false };
        await using RecordingController controller = new(native);
        ManagedRecordingSnapshot failed = await controller.StartAsync();
        Require(failed.State == ManagedRecordingState.Failed,
            "inactive Preview maps Start to Failed");
        Require(failed.LastResult == NativeMethods.Result.InvalidState &&
            !failed.OutputSuccess,
            "inactive Preview cannot look successful");
        Require(native.StartCount == 1 && native.StopCount == 0,
            "inactive Preview creates no managed retry or Stop");
    }

    private static async Task CloseAndDisposeUseOneStopAsync()
    {
        FakeRecordingSession native = new() { BlockStop = true };
        RecordingController controller = new(native);
        await controller.StartAsync();
        Task<ManagedRecordingSnapshot> firstClose =
            controller.StopForCloseAsync();
        Require(native.StopEntered.Wait(TimeSpan.FromSeconds(2)),
            "close Stop entered");
        Task<ManagedRecordingSnapshot> secondClose =
            controller.StopForCloseAsync();
        native.StopRelease.Set();
        ManagedRecordingSnapshot[] results =
            await Task.WhenAll(firstClose, secondClose);
        Require(results.All(result =>
                result.State == ManagedRecordingState.Completed),
            "all close waiters observe terminal snapshot");
        Require(native.StopCount == 1 && native.FinalizeCount == 1,
            "close reuses single Stop/Finalize");

        await controller.DisposeAsync();
        Require(controller.IsDisposed && !controller.HasPendingOperation,
            "Dispose leaves no controller task or timer");
        Require(native.StopCount == 1,
            "Dispose does not repeat completed Stop");
    }

    private static async Task PreviewCloseUsesManagedRecordingControllerAsync()
    {
        PreviewLifecycleTests.Harness harness = new();
        await harness.InitializeAsync();
        Require((await harness.StartAsync()).Succeeded,
            "Preview is active before recording");
        RecordingController controller =
            harness.Controller.GetOrCreateRecordingController();
        Require((await controller.StartAsync()).State ==
            ManagedRecordingState.Recording,
            "managed controller starts native recording");

        await harness.Controller.CloseAsync();
        Require(harness.Native.RecordingStartCount == 1 &&
            harness.Native.RecordingStopCount == 1 &&
            harness.Native.RecordingFinalizeCount == 1,
            "Preview Close reuses one managed Stop/Finalize");
        Require(controller.CurrentSnapshot.State ==
            ManagedRecordingState.Completed,
            "Preview Close waits for native terminal snapshot");
        Require(controller.IsDisposed &&
            harness.Native.DisposeCount == 1,
            "controller and native session dispose after terminal state");
    }

    private static void OutputActionsOnlyExposeCompletedExistingFiles()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"xbpreview-p2.5b-output-{Guid.NewGuid():N}.mp4");
        try
        {
            File.WriteAllBytes(path, []);
            ManagedRecordingSnapshot completed =
                ManagedRecordingSnapshot.Idle with
                {
                    State = ManagedRecordingState.Completed,
                    OutputSuccess = true,
                    OutputPath = path,
                    WorkingPath = path + ".partial.mp4",
                    PlannedFinalPath = path,
                    PublishedPath = path,
                    ReadyToPublish = true,
                    Published = true,
                    PublishAttempted = true,
                    PublishHResult = 0,
                };
            Require(RecordingOutputActions.CanOpenVideo(completed),
                "Completed existing output enables open video");
            Require(RecordingOutputActions.CanOpenFolder(completed),
                "output directory enables open folder");
            RecordingOutputPresentation firstPresentation =
                RecordingOutputActions.Describe(completed);
            Require(firstPresentation.PathText == path &&
                firstPresentation.CanOpenVideo &&
                firstPresentation.CanOpenFolder,
                "presentation uses the completed Snapshot output path");

            ManagedRecordingSnapshot missingFile = completed with
            {
                PublishedPath = path + ".missing",
            };
            RecordingOutputPresentation missingPresentation =
                RecordingOutputActions.Describe(missingFile);
            Require(!missingPresentation.CanOpenVideo &&
                missingPresentation.CanOpenFolder,
                "Completed missing file keeps its existing folder available");

            ManagedRecordingSnapshot failed = completed with
            {
                State = ManagedRecordingState.Failed,
                OutputSuccess = false,
            };
            Require(!RecordingOutputActions.CanOpenVideo(failed),
                "Failed output cannot open as a successful video");
            Require(!RecordingOutputActions.CanOpenFolder(failed),
                "Failed output cannot open a completed-output folder action");

            ManagedRecordingSnapshot empty = completed with
            {
                PublishedPath = string.Empty,
            };
            Require(!RecordingOutputActions.Describe(empty).CanOpenVideo &&
                RecordingOutputActions.Describe(completed).CanOpenVideo,
                "valid path refresh does not retain an old disabled state");

            ManagedRecordingSnapshot illegal = completed with
            {
                PublishedPath = path + "\0trailing-data",
            };
            RecordingOutputPresentation illegalPresentation =
                RecordingOutputActions.Describe(illegal);
            Require(!illegalPresentation.CanOpenVideo &&
                illegalPresentation.StatusText !=
                    "输出：已完成，可打开视频",
                "illegal or offset-corrupted path cannot look like a " +
                "successful video");
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static void MainFormControlsUseOneCompletedSnapshot()
    {
        RunOnSta(() =>
        {
            string hostPath = Path.Combine(
                AppContext.BaseDirectory,
                "XbPreview.Host.dll");
            Assembly host = Assembly.LoadFrom(hostPath);
            Type mainFormType = host.GetType(
                "XbPreview.Host.MainForm",
                throwOnError: true)!;
            Type snapshotType = host.GetType(
                "XbPreview.Host.ManagedRecordingSnapshot",
                throwOnError: true)!;
            object formObject = Activator.CreateInstance(
                mainFormType,
                BindingFlags.Instance |
                    BindingFlags.Public |
                    BindingFlags.NonPublic,
                binder: null,
                args: null,
                culture: null) ??
                throw new InvalidOperationException(
                    "MainForm construction failed.");
            using Form form = (Form)formObject;
            MethodInfo refresh = mainFormType.GetMethod(
                "RefreshRecordingUi",
                BindingFlags.Instance | BindingFlags.NonPublic) ??
                throw new InvalidOperationException(
                    "RefreshRecordingUi was not found.");
            Label outputLabel = GetControl<Label>(
                mainFormType, form, "_recordingPathLabel");
            TextBox outputPath = GetControl<TextBox>(
                mainFormType, form, "_recordingPathBox");
            Button openVideo = GetControl<Button>(
                mainFormType, form, "_openVideoButton");
            Button openFolder = GetControl<Button>(
                mainFormType, form, "_openRecordingFolderButton");

            string root = Path.Combine(
                Path.GetTempPath(),
                $"xbpreview-p2.5b-mainform-{Guid.NewGuid():N}");
            Directory.CreateDirectory(root);
            string first = Path.Combine(root, "first.mp4");
            string second = Path.Combine(root, "second.mp4");
            try
            {
                object empty = CreateHostSnapshot(
                    host,
                    snapshotType,
                    "Completed",
                    string.Empty,
                    outputSuccess: true);
                refresh.Invoke(form, [empty]);
                Require(outputPath.Text == "—" &&
                    !openVideo.Enabled && !openFolder.Enabled,
                    "real MainForm empty Completed output is unavailable");

                File.WriteAllBytes(first, []);
                object completed = CreateHostSnapshot(
                    host,
                    snapshotType,
                    "Completed",
                    first,
                    outputSuccess: true);
                refresh.Invoke(form, [completed]);
                Require(outputPath.Text == first,
                    "real MainForm path box uses Snapshot.OutputPath");
                Require(outputLabel.Text.Contains(
                        "已完成",
                        StringComparison.Ordinal) &&
                    !outputLabel.Text.Contains(
                        "—",
                        StringComparison.Ordinal),
                    "real MainForm output label leaves the initial dash state");
                Require(openVideo.Enabled && openFolder.Enabled,
                    "real MainForm enables both Completed output actions");

                object missing = CreateHostSnapshot(
                    host,
                    snapshotType,
                    "Completed",
                    Path.Combine(root, "missing.mp4"),
                    outputSuccess: true);
                refresh.Invoke(form, [missing]);
                Require(!openVideo.Enabled && openFolder.Enabled,
                    "real MainForm permits folder when completed file is missing");


                object failed = CreateHostSnapshot(
                    host,
                    snapshotType,
                    "Failed",
                    first,
                    outputSuccess: false);
                refresh.Invoke(form, [failed]);
                Require(!openVideo.Enabled && !openFolder.Enabled &&
                    outputLabel.Text.Contains("失败", StringComparison.Ordinal),
                    "real MainForm rejects failed output actions");

                File.WriteAllBytes(second, []);
                object secondCompleted = CreateHostSnapshot(
                    host,
                    snapshotType,
                    "Completed",
                    second,
                    outputSuccess: true);
                refresh.Invoke(form, [secondCompleted]);
                Require(outputPath.Text == second &&
                    openVideo.Enabled && openFolder.Enabled,
                    "real MainForm replaces first Session with second output");
            }
            finally
            {
                File.Delete(first);
                File.Delete(second);
                Directory.Delete(root, recursive: false);
            }
        });
    }

    private static void RealMainFormRecordingPathRoundTrip()
    {
        RunOnSta(() =>
        {
            int staThreadId = Environment.CurrentManagedThreadId;
            Require(Thread.CurrentThread.GetApartmentState() ==
                    ApartmentState.STA,
                "real MainForm runs on an STA thread");
            string hostPath = Path.Combine(
                AppContext.BaseDirectory,
                "XbPreview.Host.dll");
            Assembly host = Assembly.LoadFrom(hostPath);
            Type mainFormType = host.GetType(
                "XbPreview.Host.MainForm",
                throwOnError: true)!;
            object formObject = Activator.CreateInstance(
                mainFormType,
                BindingFlags.Instance |
                    BindingFlags.Public |
                    BindingFlags.NonPublic,
                binder: null,
                args: null,
                culture: null) ??
                throw new InvalidOperationException(
                    "Real MainForm construction failed.");
            Form form = (Form)formObject;
            Button start = GetControl<Button>(
                mainFormType, form, "_startRecordingButton");
            Button stop = GetControl<Button>(
                mainFormType, form, "_stopRecordingButton");
            Button openVideo = GetControl<Button>(
                mainFormType, form, "_openVideoButton");
            Button openFolder = GetControl<Button>(
                mainFormType, form, "_openRecordingFolderButton");
            Label stateLabel = GetControl<Label>(
                mainFormType, form, "_recordingStateLabel");
            Label outputLabel = GetControl<Label>(
                mainFormType, form, "_recordingPathLabel");
            TextBox outputPath = GetControl<TextBox>(
                mainFormType, form, "_recordingPathBox");

            Exception? scenarioFailure = null;
            RealPathSession? first = null;
            RealPathSession? second = null;
            bool handleCreated = false;
            bool scenarioFinished = false;
            bool formClosed = false;
            using ApplicationContext context = new(form);
            EventHandler handleCreatedHandler = (_, _) =>
            {
                handleCreated = true;
                Require(Environment.CurrentManagedThreadId == staThreadId,
                    "MainForm handle is created on the owning STA thread");
            };
            FormClosedEventHandler formClosedHandler = (_, _) =>
            {
                formClosed = true;
                Require(Environment.CurrentManagedThreadId == staThreadId,
                    "MainForm closes on the owning STA thread");
            };

            async void ExecuteScenario()
            {
                try
                {
                    Require(Environment.CurrentManagedThreadId == staThreadId,
                        "real MainForm scenario starts on the owning STA thread");
                    await WaitUntilAsync(
                        () => form.IsHandleCreated && start.Enabled,
                        TimeSpan.FromSeconds(30),
                        "real MainForm Preview reaches recording-ready state");
                    first = await RunRealPathSessionAsync(
                        mainFormType,
                        form,
                        start,
                        stop,
                        openVideo,
                        openFolder,
                        stateLabel,
                        outputLabel,
                        outputPath,
                        sessionNumber: 1,
                        staThreadId);
                    second = await RunRealPathSessionAsync(
                        mainFormType,
                        form,
                        start,
                        stop,
                        openVideo,
                        openFolder,
                        stateLabel,
                        outputLabel,
                        outputPath,
                        sessionNumber: 2,
                        staThreadId);
                }
                catch (Exception error)
                {
                    scenarioFailure = error;
                }
                finally
                {
                    scenarioFinished = true;
                    try
                    {
                        RequestFormClose(form, context, staThreadId);
                    }
                    catch (Exception closeError)
                    {
                        scenarioFailure ??= closeError;
                        context.ExitThread();
                    }
                }
            }

            EventHandler shownHandler = (_, _) =>
                form.BeginInvoke((Action)ExecuteScenario);
            form.HandleCreated += handleCreatedHandler;
            form.FormClosed += formClosedHandler;
            form.Shown += shownHandler;
            try
            {
                form.Show();
                Require(handleCreated && form.IsHandleCreated,
                    "real MainForm handle is stable before the scenario runs");
                Application.Run(context);
            }
            finally
            {
                form.Shown -= shownHandler;
                form.FormClosed -= formClosedHandler;
                form.HandleCreated -= handleCreatedHandler;
                if (!form.IsDisposed)
                {
                    form.Dispose();
                }
            }

            Require(scenarioFinished,
                "real MainForm scenario finishes before the STA loop exits");
            Require(formClosed,
                "real MainForm raises FormClosed before disposal");
            if (scenarioFailure is not null)
            {
                throw new InvalidOperationException(
                    "Real MainForm recording path scenario failed.",
                    scenarioFailure);
            }
            RealPathSession firstCompleted = first ??
                throw new InvalidOperationException(
                    "real first Session did not complete");
            RealPathSession secondCompleted = second ??
                throw new InvalidOperationException(
                    "real second Session did not complete");
            Require(firstCompleted.SessionId != secondCompleted.SessionId,
                "real consecutive Sessions use distinct identifiers");
            Require(!string.Equals(
                    firstCompleted.Path,
                    secondCompleted.Path,
                    StringComparison.Ordinal),
                "real consecutive Sessions use distinct output paths");
            Console.WriteLine(
                $"P2.5B_REAL_PATH Session1={firstCompleted.SessionId} " +
                $"Path1={firstCompleted.Path} " +
                $"Session2={secondCompleted.SessionId} " +
                $"Path2={secondCompleted.Path}");
        }, TimeSpan.FromSeconds(120));
    }

    private static async Task<RealPathSession> RunRealPathSessionAsync(
        Type mainFormType,
        Form form,
        Button start,
        Button stop,
        Button openVideo,
        Button openFolder,
        Label stateLabel,
        Label outputLabel,
        TextBox outputPath,
        int sessionNumber,
        int staThreadId)
    {
        Require(Environment.CurrentManagedThreadId == staThreadId,
            $"real Session {sessionNumber} starts on the owning STA thread");
        Require(start.Enabled,
            $"real Session {sessionNumber} Start is enabled");
        start.PerformClick();
        await WaitUntilAsync(
            () => stateLabel.Text.Contains(
                "正在录制",
                StringComparison.Ordinal) && stop.Enabled,
            TimeSpan.FromSeconds(30),
            $"real Session {sessionNumber} reaches Recording");
        await Task.Delay(TimeSpan.FromSeconds(2));
        stop.PerformClick();
        await WaitUntilAsync(
            () => stateLabel.Text.Contains(
                    "录制完成",
                    StringComparison.Ordinal) &&
                outputLabel.Text == "输出：已完成，可打开视频" &&
                openVideo.Enabled &&
                openFolder.Enabled,
            TimeSpan.FromSeconds(30),
            $"real Session {sessionNumber} reaches usable Completed output");

        object controller = GetFieldValue(
            mainFormType,
            form,
            "_recordingController");
        object managedSnapshot = controller.GetType().GetProperty(
            "CurrentSnapshot",
            BindingFlags.Instance |
                BindingFlags.Public |
                BindingFlags.NonPublic)?.GetValue(controller) ??
            throw new InvalidOperationException(
                "Managed RecordingController Snapshot was unavailable.");
        string managedPath = GetSnapshotString(
            managedSnapshot,
            "PublishedPath");
        string managedSession = GetSnapshotString(
            managedSnapshot,
            "SessionId");
        bool outputSuccess = (bool)(managedSnapshot.GetType().GetProperty(
            "OutputSuccess")?.GetValue(managedSnapshot) ?? false);
        NativeMethods.RecordingSnapshot nativeSnapshot =
            ReadRawNativeSnapshot(controller);
        string nativePath = nativeSnapshot.GetPublishedPath();
        string nativeSession = nativeSnapshot.GetSessionId();

        Require(nativeSnapshot.State ==
                NativeMethods.RecordingState.Completed &&
            nativeSnapshot.OutputSuccess == 1 && outputSuccess &&
            nativeSnapshot.ReadyToPublish == 1 &&
            nativeSnapshot.Published == 1 &&
            nativeSnapshot.PublishAttempted == 1 &&
            nativeSnapshot.PublishHResult == 0,
            $"real Session {sessionNumber} preserves Completed success facts");
        Require(string.Equals(
                nativeSession,
                managedSession,
                StringComparison.Ordinal),
            $"real Session {sessionNumber} preserves SessionId across ABI");
        Require(string.Equals(
                nativePath,
                managedPath,
                StringComparison.Ordinal) &&
            string.Equals(
                managedPath,
                outputPath.Text,
                StringComparison.Ordinal),
            $"real Session {sessionNumber} path is identical from native ABI " +
            "through Managed Snapshot and MainForm");
        Require(Path.IsPathFullyQualified(managedPath),
            $"real Session {sessionNumber} output path is fully qualified");
        Require(managedPath.Contains(
                "小白录屏器",
                StringComparison.Ordinal),
            $"real Session {sessionNumber} exercises a Chinese directory");
        Require(managedPath == managedPath.Trim() &&
            !managedPath.Any(character =>
                character is '\"' or '\r' or '\n' or '\t' or '\0'),
            $"real Session {sessionNumber} path has no hidden edge characters");
        Require(string.Equals(
                Path.GetExtension(managedPath),
                ".mp4",
                StringComparison.Ordinal),
            $"real Session {sessionNumber} extension is exactly .mp4");

        Require(File.Exists(managedPath),
            $"real Session {sessionNumber} MP4 exists");
        string? directory = Path.GetDirectoryName(managedPath);
        Require(!string.IsNullOrEmpty(directory) &&
            Directory.Exists(directory),
            $"real Session {sessionNumber} parent directory exists");
        Require(string.Equals(
                Path.GetFullPath(managedPath),
                managedPath,
                StringComparison.Ordinal),
            $"real Session {sessionNumber} GetFullPath is unchanged");

        await WaitUntilAsync(
            () => start.Enabled,
            TimeSpan.FromSeconds(10),
            $"real Session {sessionNumber} permits the next recording");
        Require(Environment.CurrentManagedThreadId == staThreadId,
            $"real Session {sessionNumber} remains on the owning STA thread");
        return new RealPathSession(managedSession, managedPath);
    }

    private static void RequestFormClose(
        Form form,
        ApplicationContext context,
        int staThreadId)
    {
        Require(Environment.CurrentManagedThreadId == staThreadId,
            "MainForm close is requested from the owning STA thread");
        if (form.IsDisposed || !form.IsHandleCreated)
        {
            context.ExitThread();
            return;
        }

        form.BeginInvoke((Action)(() =>
        {
            Require(Environment.CurrentManagedThreadId == staThreadId,
                "MainForm queued close runs on the owning STA thread");
            if (!form.IsDisposed && !form.Disposing)
            {
                form.Close();
            }
        }));
    }

    private static unsafe NativeMethods.RecordingSnapshot ReadRawNativeSnapshot(
        object controller)
    {
        object nativeSession = GetFieldValue(
            controller.GetType(),
            controller,
            "_native");
        SafeHandle safeHandle = (SafeHandle)GetFieldValue(
            nativeSession.GetType(),
            nativeSession,
            "_handle");
        nint library = NativeLibrary.Load(Path.Combine(
            AppContext.BaseDirectory,
            NativeMethods.DllName));
        try
        {
            nint export = NativeLibrary.GetExport(
                library,
                "XbPreview_GetRecordingSnapshot");
            delegate* unmanaged[Stdcall]<
                nint,
                NativeMethods.RecordingSnapshot*,
                NativeMethods.Result> getSnapshot =
                (delegate* unmanaged[Stdcall]<
                    nint,
                    NativeMethods.RecordingSnapshot*,
                    NativeMethods.Result>)export;
            NativeMethods.RecordingSnapshot snapshot = new()
            {
                StructSize = (uint)sizeof(NativeMethods.RecordingSnapshot),
                ApiVersion = NativeMethods.ApiVersion,
            };
            NativeMethods.Result result = getSnapshot(
                safeHandle.DangerousGetHandle(),
                &snapshot);
            Require(result == NativeMethods.Result.Ok,
                $"raw native Snapshot returned {result}");
            return snapshot;
        }
        finally
        {
            NativeLibrary.Free(library);
        }
    }

    private static object GetFieldValue(
        Type type,
        object instance,
        string fieldName) =>
        type.GetField(
            fieldName,
            BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(instance) ??
        throw new InvalidOperationException(
            $"Required field was unavailable: {type.FullName}.{fieldName}");

    private static string GetSnapshotString(
        object snapshot,
        string propertyName) =>
        (string?)(snapshot.GetType().GetProperty(
            propertyName,
            BindingFlags.Instance |
                BindingFlags.Public |
                BindingFlags.NonPublic)?.GetValue(snapshot)) ??
        throw new InvalidOperationException(
            $"Snapshot property was unavailable: {propertyName}");

    private static async Task WaitUntilAsync(
        Func<bool> condition,
        TimeSpan timeout,
        string message)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }
            await Task.Delay(20);
        }
        throw new TimeoutException(message);
    }

    private sealed record RealPathSession(string SessionId, string Path);

    private static object CreateHostSnapshot(
        Assembly host,
        Type snapshotType,
        string state,
        string outputPath,
        bool outputSuccess)
    {
        ConstructorInfo constructor = snapshotType.GetConstructors(
            BindingFlags.Instance |
            BindingFlags.Public |
            BindingFlags.NonPublic).
            OrderByDescending(candidate =>
                candidate.GetParameters().Length).
            First();
        object?[] arguments = constructor.GetParameters().Select(parameter =>
        {
            string name = parameter.Name?.ToLowerInvariant() ?? string.Empty;
            return name switch
            {
                "state" => Enum.Parse(
                    host.GetType(
                        "XbPreview.Host.ManagedRecordingState",
                        throwOnError: true)!,
                    state),
                "lastresult" => Enum.Parse(
                    host.GetType(
                        "XbPreview.Host.NativeMethods+Result",
                        throwOnError: true)!,
                    "Ok"),
                "startutc" => DateTimeOffset.UtcNow,
                "elapsed" => TimeSpan.FromSeconds(20),
                "sessionid" => Guid.NewGuid().ToString("D"),
                "outputpath" => outputPath,
                "errormessage" => string.Empty,
                "outputsuccess" => outputSuccess,
                "finalizeattempted" => true,
                "finalizehresult" => 0,
                "failurehresult" => 0,
                "finalizecount" => 1U,
                "activeencoder" => false,
                "residualoutstanding" => 0U,
                "outputcleanupattempted" => false,
                "outputcleanupsucceeded" => false,
                "outputcleanuphresult" => 0,
                "framessubmitted" => 1200UL,
                _ => throw new InvalidOperationException(
                    $"Unexpected Snapshot parameter: {parameter.Name}"),
            };
        }).ToArray();
        object snapshot = constructor.Invoke(arguments);
        bool completed = state == "Completed" && outputSuccess;
        SetSnapshotProperty(snapshot, "WorkingPath",
            completed ? outputPath + ".partial.mp4" : outputPath);
        SetSnapshotProperty(snapshot, "PlannedFinalPath", outputPath);
        SetSnapshotProperty(snapshot, "PublishedPath",
            completed ? outputPath : string.Empty);
        SetSnapshotProperty(snapshot, "ReadyToPublish", completed);
        SetSnapshotProperty(snapshot, "Published", completed);
        SetSnapshotProperty(snapshot, "PublishAttempted", completed);
        SetSnapshotProperty(snapshot, "PublishHResult",
            completed ? 0 : unchecked((int)0x8000000A));
        return snapshot;
    }

    private static void SetSnapshotProperty(
        object snapshot,
        string name,
        object value)
    {
        PropertyInfo property = snapshot.GetType().GetProperty(
            name,
            BindingFlags.Instance |
                BindingFlags.Public |
                BindingFlags.NonPublic) ??
            throw new InvalidOperationException(
                $"Snapshot property was unavailable: {name}");
        property.SetValue(snapshot, value);
    }

    private static T GetControl<T>(
        Type formType,
        Form form,
        string fieldName)
        where T : Control
    {
        FieldInfo field = formType.GetField(
            fieldName,
            BindingFlags.Instance | BindingFlags.NonPublic) ??
            throw new InvalidOperationException(
                $"MainForm field was not found: {fieldName}");
        return (T)(field.GetValue(form) ??
            throw new InvalidOperationException(
                $"MainForm control was null: {fieldName}"));
    }

    private static void RunOnSta(
        Action action,
        TimeSpan? timeout = null)
    {
        Exception? failure = null;
        Thread thread = new(() =>
        {
            ThreadExceptionEventHandler threadExceptionHandler = (_, args) =>
            {
                failure ??= args.Exception;
                Application.ExitThread();
            };
            try
            {
                Application.SetUnhandledExceptionMode(
                    UnhandledExceptionMode.CatchException,
                    threadScope: true);
                Application.ThreadException += threadExceptionHandler;
                action();
            }
            catch (Exception error)
            {
                failure ??= error;
            }
            finally
            {
                Application.ThreadException -= threadExceptionHandler;
            }
        });
        thread.IsBackground = true;
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        if (!thread.Join(timeout ?? TimeSpan.FromSeconds(30)))
        {
            throw new TimeoutException(
                "MainForm UI wiring test did not finish.");
        }
        if (failure is not null)
        {
            throw new InvalidOperationException(
                "MainForm UI wiring test failed.",
                failure);
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed unsafe class FakeRecordingSession : IRecordingNativeSession
    {
        private readonly object _gate = new();
        private NativeMethods.RecordingSnapshot _snapshot =
            CreateSnapshot(NativeMethods.RecordingState.Idle);
        private int _sessionNumber;

        internal bool PreviewActive { get; set; } = true;
        internal bool MicrophoneUnavailableAtStart { get; set; }
        internal bool BlockStart { get; set; }
        internal bool BlockStop { get; set; }
        internal bool BlockCancel { get; set; }
        internal bool CancelCleanupFailure { get; set; }
        internal bool BlockNextSnapshotRead { get; set; }
        internal bool ThrowSnapshotReads { get; set; }
        internal ManualResetEventSlim StartEntered { get; } = new(false);
        internal ManualResetEventSlim StartRelease { get; } = new(false);
        internal ManualResetEventSlim StopEntered { get; } = new(false);
        internal ManualResetEventSlim StopRelease { get; } = new(false);
        internal ManualResetEventSlim CancelEntered { get; } = new(false);
        internal ManualResetEventSlim CancelRelease { get; } = new(false);
        internal ManualResetEventSlim SnapshotReadCaptured { get; } = new(false);
        internal ManualResetEventSlim SnapshotReadRelease { get; } = new(false);
        internal ManualResetEventSlim PauseEntered { get; } = new(false);
        internal ManualResetEventSlim ResumeEntered { get; } = new(false);
        internal int StartCount { get; private set; }
        internal int SuccessfulStartCount { get; private set; }
        internal int StopCount { get; private set; }
        internal int CancelCount { get; private set; }
        internal int PauseCount { get; private set; }
        internal int ResumeCount { get; private set; }
        internal int FinalizeCount { get; private set; }
        internal int PublishCount { get; private set; }
        internal int CleanupCount { get; private set; }
        internal int AudioControlsSetCount { get; private set; }
        internal int AudioProgramModeSetCount { get; private set; }
        internal NativeMethods.AudioProgramMode LastAudioProgramMode {
            get; private set;

        }
        internal bool LastSystemMuted { get; private set; }
        internal bool LastMicrophoneMuted { get; private set; }
        internal double LastMicrophoneGainDb { get; private set; }
        internal string? OutputDirectory { get; init; }
        internal List<string>? LifecycleEvents { get; init; }

        internal static NativeMethods.RecordingSnapshot
            CreatePublicationSnapshot(
                NativeMethods.RecordingState state,
                string sessionId,
                NativeMethods.Result result = NativeMethods.Result.Ok) =>
            CreateSnapshot(state, result, sessionId);

        internal void SetPublicationFacts(
            NativeMethods.RecordingSnapshot snapshot)
        {
            lock (_gate)
            {
                _snapshot = snapshot;
            }
        }

        public NativeMethods.Result StartRecording()
        {
            lock (_gate)
            {
                StartCount++;
                if (!PreviewActive)
                {
                    _snapshot = CreateSnapshot(
                        NativeMethods.RecordingState.Failed,
                        NativeMethods.Result.InvalidState,
                        error: "Preview is not active.",
                        failureHResult: unchecked((int)0x8007139F));
                    return NativeMethods.Result.InvalidState;
                }
                if (MicrophoneUnavailableAtStart)
                {
                    _snapshot = CreateSnapshot(
                        NativeMethods.RecordingState.Failed,
                        NativeMethods.Result.NativeFailure,
                        error: "MicUnavailableAtStart",
                        failureHResult: unchecked((int)0x80070490));
                    return NativeMethods.Result.NativeFailure;
                }

                _sessionNumber++;
                string sessionId = $"session-{_sessionNumber}";
                string outputPath = string.IsNullOrWhiteSpace(OutputDirectory)
                    ? $"recording-{_sessionNumber}.partial.mp4"
                    : Path.Combine(
                        OutputDirectory,
                        $"recording-{_sessionNumber}.partial.mp4");
                if (!string.IsNullOrWhiteSpace(OutputDirectory))
                {
                    Directory.CreateDirectory(Path.Combine(
                        OutputDirectory,
                        "sessions",
                        sessionId));
                }
                _snapshot = CreateSnapshot(
                    NativeMethods.RecordingState.Starting,
                    sessionId: sessionId,
                    outputPath: outputPath);
            }
            StartEntered.Set();
            if (BlockStart)
            {
                StartRelease.Wait(TimeSpan.FromSeconds(5));
            }
            lock (_gate)
            {
                _snapshot.State = NativeMethods.RecordingState.Recording;
                SuccessfulStartCount++;
                return NativeMethods.Result.Ok;
            }
        }

        public NativeMethods.Result SetAudioProgramMode(
            NativeMethods.AudioProgramMode mode)
        {
            AudioProgramModeSetCount++;
            LastAudioProgramMode = mode;
            return NativeMethods.Result.Ok;
        }

        public NativeMethods.Result PauseRecording()
        {
            lock (_gate)
            {
                if (_snapshot.State != NativeMethods.RecordingState.Recording)
                {
                    return NativeMethods.Result.InvalidState;
                }
                PauseCount++;
                _snapshot.State = NativeMethods.RecordingState.Pausing;
            }
            PauseEntered.Set();
            return NativeMethods.Result.Ok;
        }

        public NativeMethods.Result ResumeRecording()
        {
            lock (_gate)
            {
                if (_snapshot.State != NativeMethods.RecordingState.Paused)
                {
                    return NativeMethods.Result.InvalidState;
                }
                ResumeCount++;
                _snapshot.State = NativeMethods.RecordingState.Resuming;
            }
            ResumeEntered.Set();
            return NativeMethods.Result.Ok;
        }

        public MicrophoneDeviceCatalog GetMicrophoneDevices() => new(
            1,
            true,
            true,
            "test-endpoint",
            "Test Microphone",
            0,
            0,
            [new MicrophoneDevice("test-endpoint", "Test Microphone")]);

        public NativeMethods.Result SetMicrophoneSelection(
            MicrophoneSelection selection) => NativeMethods.Result.Ok;

        public MicrophoneSelectionStatus GetMicrophoneSelection() => new(
            MicrophoneSelectionKind.WindowsDefault,
            true,
            false,
            "test-endpoint",
            "Test Microphone");

        public NativeMethods.Result StopRecording()
        {
            LifecycleEvents?.Add("native-stop");
            lock (_gate)
            {
                StopCount++;
                _snapshot.State = NativeMethods.RecordingState.Stopping;
            }
            StopEntered.Set();
            if (BlockStop)
            {
                StopRelease.Wait(TimeSpan.FromSeconds(5));
            }
            lock (_gate)
            {
                FinalizeCount++;
                _snapshot.State = NativeMethods.RecordingState.Completed;
                _snapshot.OutputSuccess = 1;
                _snapshot.FinalizeAttempted = 1;
                _snapshot.FinalizeHResult = 0;
                _snapshot.FinalizeCount = (uint)FinalizeCount;
                _snapshot.ValidationAttempted = 1;
                _snapshot.ValidationHResult = 0;
                _snapshot.ReadyToPublish = 1;
                _snapshot.PublishAttempted = 1;
                _snapshot.PublishHResult = 0;
                _snapshot.Published = 1;
                PublishCount++;
                NativeMethods.RecordingSnapshot published = _snapshot;
                CopyString(
                    published.PublishedPath,
                    260,
                    _snapshot.GetPlannedFinalPath());
                _snapshot = published;
                _snapshot.ActiveEncoder = 0;
                return NativeMethods.Result.Ok;
            }
        }

        public NativeMethods.Result CancelRecording()
        {
            lock (_gate)
            {
                CancelCount++;
                _snapshot.State = NativeMethods.RecordingState.Stopping;
            }
            CancelEntered.Set();
            if (BlockCancel)
            {
                CancelRelease.Wait(TimeSpan.FromSeconds(5));
            }
            lock (_gate)
            {
                string sessionId = _snapshot.GetSessionId();
                string outputPath = _snapshot.GetOutputPath();
                FinalizeCount++;
                CleanupCount++;
                _snapshot = CreateSnapshot(
                    NativeMethods.RecordingState.UserCancelled,
                    CancelCleanupFailure
                        ? NativeMethods.Result.NativeFailure
                        : NativeMethods.Result.Ok,
                    sessionId,
                    outputPath,
                    CancelCleanupFailure
                        ? "Injected cancellation cleanup failure."
                        : string.Empty,
                    CancelCleanupFailure
                        ? unchecked((int)0x80070005)
                        : 0);
                _snapshot.FinalizeAttempted = 1;
                _snapshot.FinalizeHResult = 0;
                _snapshot.FinalizeCount = (uint)FinalizeCount;
                _snapshot.ActiveEncoder = 0;
                _snapshot.ResidualOutstanding = 0;
                _snapshot.OutputCleanupAttempted = 1;
                _snapshot.OutputCleanupSucceeded =
                    CancelCleanupFailure ? 0U : 1U;
                _snapshot.OutputCleanupHResult = CancelCleanupFailure
                    ? unchecked((int)0x80070005)
                    : 0;
                return CancelCleanupFailure
                    ? NativeMethods.Result.NativeFailure
                    : NativeMethods.Result.Ok;
            }
        }

        public NativeMethods.RecordingSnapshot GetRecordingSnapshot()
        {
            NativeMethods.RecordingSnapshot snapshot;
            bool block;
            lock (_gate)
            {
                if (ThrowSnapshotReads)
                {
                    throw new InvalidOperationException(
                        "Injected Snapshot read failure.");
                }
                snapshot = _snapshot;
                block = BlockNextSnapshotRead;
                BlockNextSnapshotRead = false;
            }
            if (block)
            {
                SnapshotReadCaptured.Set();
                SnapshotReadRelease.Wait(TimeSpan.FromSeconds(5));
            }
            return snapshot;
        }

        public NativeMethods.Result SetAudioControls(
            bool systemMuted,
            bool microphoneMuted,
            double microphoneGainDb)
        {
            AudioControlsSetCount++;
            LastSystemMuted = systemMuted;
            LastMicrophoneMuted = microphoneMuted;
            LastMicrophoneGainDb = microphoneGainDb;
            return NativeMethods.Result.Ok;
        }

        public NativeMethods.AudioControlSnapshotV1
            GetAudioControlSnapshot() => new()
            {
                StructSize = NativeMethods.ExpectedAudioControlSnapshotV1Size,
                AbiVersion = NativeMethods.AudioControlsAbiVersionV1,
                MicrophoneGainLinear = 1.0,
                ProgramHeadroomCoefficient = 1.0,
                MeterWindowFrames = 4_800,
            };

        public string GetLastError()
        {
            lock (_gate)
            {
                return _snapshot.GetErrorMessage();
            }
        }

        internal void SetElapsed(TimeSpan elapsed)
        {
            lock (_gate)
            {
                _snapshot.Elapsed100ns = elapsed.Ticks;
            }
        }

        internal void AcknowledgePause()
        {
            lock (_gate)
            {
                if (_snapshot.State != NativeMethods.RecordingState.Pausing)
                {
                    return;
                }
                _snapshot.State = NativeMethods.RecordingState.Paused;
                _snapshot.PauseCount++;
                _snapshot.ActiveEncoder = 1;
            }
        }

        internal void AcknowledgeFullAvResume(TimeSpan totalPaused)
        {
            lock (_gate)
            {
                if (_snapshot.State != NativeMethods.RecordingState.Resuming)
                {
                    return;
                }
                _snapshot.TotalPaused100ns =
                    checked((ulong)totalPaused.Ticks);
                _snapshot.State = NativeMethods.RecordingState.Recording;
                _snapshot.ActiveEncoder = 1;
            }
        }

        internal void SetNativeStopping(int hresult, string message)
        {
            lock (_gate)
            {
                NativeMethods.RecordingSnapshot snapshot = _snapshot;
                snapshot.State = NativeMethods.RecordingState.Stopping;
                snapshot.FailureHResult = hresult;
                CopyString(snapshot.ErrorMessage, 260, message);
                _snapshot = snapshot;
            }
        }

        internal void SetNativeState(NativeMethods.RecordingState state)
        {
            lock (_gate)
            {
                _snapshot.State = state;
            }
        }

        internal void SetUserCancelledTerminal()
        {
            lock (_gate)
            {
                string sessionId = _snapshot.GetSessionId();
                string outputPath = _snapshot.GetOutputPath();
                _snapshot = CreateSnapshot(
                    NativeMethods.RecordingState.UserCancelled,
                    NativeMethods.Result.Ok,
                    sessionId,
                    outputPath);
                _snapshot.FinalizeAttempted = 1;
                _snapshot.FinalizeHResult = 0;
                _snapshot.FinalizeCount = 1;
                _snapshot.OutputCleanupAttempted = 1;
                _snapshot.OutputCleanupSucceeded = 1;
            }
        }

        internal void SetFailed(
            int failureHResult,
            string error,
            bool cleanupAttempted,
            bool cleanupSucceeded,
            int cleanupHResult = 0)
        {
            lock (_gate)
            {
                string session = _snapshot.GetSessionId();
                string output = _snapshot.GetOutputPath();
                _snapshot = CreateSnapshot(
                    NativeMethods.RecordingState.Failed,
                    NativeMethods.Result.NativeFailure,
                    session,
                    output,
                    error,
                    failureHResult);
                _snapshot.FinalizeAttempted = 1;
                _snapshot.FinalizeHResult = failureHResult;
                _snapshot.FinalizeCount = 1;
                _snapshot.OutputCleanupAttempted =
                    cleanupAttempted ? 1U : 0U;
                _snapshot.OutputCleanupSucceeded =
                    cleanupSucceeded ? 1U : 0U;
                _snapshot.OutputCleanupHResult = cleanupHResult;
                _snapshot.OutputSuccess = 0;
            }
        }

        private static NativeMethods.RecordingSnapshot CreateSnapshot(
            NativeMethods.RecordingState state,
            NativeMethods.Result result = NativeMethods.Result.Ok,
            string sessionId = "",
            string outputPath = "",
            string error = "",
            int failureHResult = 0)
        {
            NativeMethods.RecordingSnapshot snapshot = new()
            {
                StructSize = (uint)sizeof(NativeMethods.RecordingSnapshot),
                ApiVersion = NativeMethods.ApiVersion,
                State = state,
                LastResult = result,
                StartUtc100ns = state == NativeMethods.RecordingState.Idle
                    ? 0
                    : DateTimeOffset.UtcNow.ToFileTime(),
                FailureHResult = failureHResult,
                ActiveEncoder = state is
                    NativeMethods.RecordingState.Starting or
                    NativeMethods.RecordingState.Recording or
                    NativeMethods.RecordingState.Pausing or
                    NativeMethods.RecordingState.Paused or
                    NativeMethods.RecordingState.Resuming or
                    NativeMethods.RecordingState.Stopping
                    ? 1U
                    : 0U,
            };
            CopyString(snapshot.SessionId, 64, sessionId);
            CopyString(snapshot.OutputPath, 260, outputPath);
            CopyString(snapshot.WorkingPath, 260, outputPath);
            string plannedFinalPath = outputPath.EndsWith(
                ".partial.mp4",
                StringComparison.Ordinal)
                ? outputPath[..^".partial.mp4".Length] + ".mp4"
                : outputPath;
            CopyString(snapshot.PlannedFinalPath, 260, plannedFinalPath);
            CopyString(snapshot.ErrorMessage, 256, error);
            return snapshot;
        }

        private static void CopyString(
            char* destination,
            int capacity,
            string value)
        {
            int count = Math.Min(capacity - 1, value.Length);
            for (int index = 0; index < count; index++)
            {
                destination[index] = value[index];
            }
            destination[count] = '\0';
        }
    }

}
