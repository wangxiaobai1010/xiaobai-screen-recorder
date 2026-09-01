using XbPreview.Avalonia.Contracts;
using XbPreview.Avalonia.Views.Panels;
using XbPreview.Host;

namespace XbPreview.Managed.Tests;

internal static class RecordingFixedHomeAdapterTests
{
    internal static void Run()
    {
        IdleIsReady();
        StartingIsPending();
        RecordingUsesOrangeTimer();
        PausedUsesGrayTimer();
        StoppingUsesBlackTimer();
        RestartConfirmationPreservesActiveLifecycle();
        RestartIsRejectedOutsideRecordingAndPaused();
        CancellationPendingUsesStoppingPresentation();
        CancelledTerminalReturnsToReady();
        CompletedUsesPublishedContract();
        FailedAndHostErrorsAreVisible();
        CompletedReturnIsSharedPresentationState();
        OutputRootPersistencePreservesValidState();
        FrameRateSelectionAndLockingAreExplicit();
    }

    private static void IdleIsReady()
    {
        RecordingPanelPresentationState state = Create(
            RecordingReviewState.Idle,
            trayInFrame: true);
        Require(
            state.IdlePresentationVisible &&
            state.CanStart &&
            state.CanChangePath &&
            state.CanChangeFrameRate &&
            state.CanToggleTrayInFrame &&
            !state.TimerVisible,
            "Idle exposes path, screenshot, and Start only");
    }

    private static void StartingIsPending()
    {
        RecordingPanelPresentationState state = Create(
            RecordingReviewState.Starting,
            commandPending: true);
        Require(
            state.ActivePresentationVisible &&
            state.StatusText == "正在启动…" &&
            !state.TimerVisible &&
            !state.CanStart &&
            !state.CanChangePath &&
            !state.CanChangeFrameRate &&
            !state.CanToggleTrayInFrame,
            "Starting locks Panel 4 from preflight");
    }

    private static void RecordingUsesOrangeTimer()
    {
        RecordingPanelPresentationState state = Create(
            RecordingReviewState.Recording);
        Require(
            state.TimerVisible &&
            state.TimerColor == RecordingPanelTimerColor.Orange &&
            state.CanPause &&
            state.CanStop &&
            state.CanRestart &&
            !state.CanChangePath &&
            !state.CanChangeFrameRate &&
            state.CanToggleTrayInFrame,
            "Recording uses orange elapsed and allows TrayInFrame");
    }

    private static void PausedUsesGrayTimer()
    {
        RecordingPanelPresentationState state = Create(
            RecordingReviewState.Paused);
        Require(
            state.TimerColor == RecordingPanelTimerColor.Gray &&
            state.CanResume &&
            state.CanStop &&
            state.CanRestart &&
            !state.CanPause &&
            !state.CanChangeFrameRate &&
            state.CanToggleTrayInFrame &&
            state.PauseResumeText == "继续",
            "Paused uses gray elapsed and Resume/Stop");
    }

    private static void StoppingUsesBlackTimer()
    {
        RecordingPanelPresentationState state = Create(
            RecordingReviewState.Stopping,
            commandPending: true);
        Require(
            state.TimerColor == RecordingPanelTimerColor.Black &&
            state.StatusText == "正在保存…" &&
            !state.ActiveCommandsVisible &&
            !state.CanStop &&
            !state.CanRestart &&
            !state.RestartConfirmationVisible,
            "Stopping uses black elapsed and locks commands");
    }

    private static void RestartConfirmationPreservesActiveLifecycle()
    {
        foreach (RecordingReviewState phase in new[]
        {
            RecordingReviewState.Recording,
            RecordingReviewState.Paused,
        })
        {
            RecordingPanelPresentationState before = Create(phase);
            RecordingPanelPresentationState confirmation = Create(
                phase,
                restartConfirmationVisible: true);
            Require(
                confirmation.RecordingState == before.RecordingState &&
                confirmation.SessionId == before.SessionId &&
                confirmation.Elapsed == before.Elapsed &&
                confirmation.TimerColor == before.TimerColor &&
                confirmation.StatusText == before.StatusText &&
                confirmation.RestartConfirmationVisible &&
                confirmation.CanDismissRestartConfirmation &&
                confirmation.CanDiscardCurrentRecording &&
                !confirmation.CanRestart &&
                !confirmation.CanPause &&
                !confirmation.CanResume &&
                !confirmation.CanStop &&
                !confirmation.CompletedPresentationVisible,
                $"{phase} confirmation is modal without changing lifecycle facts");
        }
    }

    private static void RestartIsRejectedOutsideRecordingAndPaused()
    {
        foreach (RecordingReviewState phase in new[]
        {
            RecordingReviewState.Idle,
            RecordingReviewState.Starting,
            RecordingReviewState.Stopping,
            RecordingReviewState.Completed,
            RecordingReviewState.Failed,
        })
        {
            RecordingPanelPresentationState state = Create(
                phase,
                restartConfirmationVisible: true);
            Require(
                !state.RestartConfirmationVisible &&
                !state.CanRestart &&
                !state.CanDismissRestartConfirmation &&
                !state.CanDiscardCurrentRecording,
                $"{phase} rejects destructive confirmation entry");
        }
    }

    private static void CancellationPendingUsesStoppingPresentation()
    {
        RecordingPanelPresentationState state = Create(
            RecordingReviewState.Stopping,
            commandPending: true,
            restartConfirmationVisible: true,
            cancellationPending: true);
        Require(
            state.ActivePresentationVisible &&
            state.StatusText == "正在取消…" &&
            state.TimerColor == RecordingPanelTimerColor.Black &&
            !state.ActiveCommandsVisible &&
            !state.RestartConfirmationVisible &&
            !state.CanRestart &&
            !state.CanStop &&
            !state.CompletedPresentationVisible,
            "Cancel pending reuses locked Stopping without saved presentation");
    }

    private static void CancelledTerminalReturnsToReady()
    {
        RecordingPanelPresentationState state = Create(
            RecordingReviewState.Idle);
        Require(
            state.IdlePresentationVisible &&
            state.CanStart &&
            !state.CanRestart &&
            !state.CompletedPresentationVisible &&
            state.StatusText == string.Empty &&
            !state.CanOpenFolder &&
            !state.CanOpenVideo,
            "User-cancelled terminal projection is recording-ready, never saved");
    }

    private static void CompletedUsesPublishedContract()
    {
        RecordingReviewSnapshot completed = Snapshot(
            RecordingReviewState.Completed) with
        {
            OutputSuccess = true,
            FinalizeAttempted = true,
            FinalizeHResult = 0,
            ReadyToPublish = true,
            Published = true,
            ValidationAttempted = true,
            ValidationHResult = 0,
        };
        RecordingPanelPresentationState safe =
            RecordingPanelPresentationState.Create(
                completed,
                false,
                @"C:\recordings",
                @"C:\recordings\.working",
                @"C:\recordings\final.mp4",
                @"C:\recordings\final.mp4",
                false,
                "capture",
                true,
                true,
                true);
        Require(
            safe.CompletedPresentationVisible &&
            safe.TimerColor == RecordingPanelTimerColor.Black &&
            safe.FinalizeSuccess &&
            safe.ValidationSuccess &&
            safe.CanOpenFolder &&
            safe.CanOpenVideo &&
            !safe.CanRestart &&
            safe.CanDismissCompletion,
            "Completed enables real, safe published output actions");

        RecordingPanelPresentationState missingFile =
            RecordingPanelPresentationState.Create(
                completed,
                false,
                @"C:\recordings",
                string.Empty,
                string.Empty,
                @"C:\recordings\final.mp4",
                false,
                "capture",
                true,
                false,
                true);
        Require(
            missingFile.CanOpenFolder && !missingFile.CanOpenVideo,
            "Open Video additionally requires the published file");
    }

    private static void FailedAndHostErrorsAreVisible()
    {
        RecordingPanelPresentationState microphoneUnavailable =
            RecordingPanelPresentationState.Create(
                Snapshot(RecordingReviewState.Failed) with
                {
                    ErrorMessage = "MicUnavailableAtStart",
                },
                false,
                @"C:\recordings",
                string.Empty,
                string.Empty,
                string.Empty,
                false,
                string.Empty,
                false,
                false,
                false);

        Require(
            microphoneUnavailable.ErrorVisible &&
            microphoneUnavailable.ErrorMessage ==
                "未检测到麦克风，请连接后重试或关闭麦克风录制。" &&
            microphoneUnavailable.Title == "录制失败" &&
            microphoneUnavailable.CanStart,
            "Mic unavailable at Start is localized without changing retry");

        RecordingPanelPresentationState failed =
            RecordingPanelPresentationState.Create(
                Snapshot(RecordingReviewState.Failed) with
                {
                    ErrorMessage = "native failure",
                },
                false,
                @"C:\recordings",
                string.Empty,
                string.Empty,
                string.Empty,
                true,
                "capture",
                false,
                false,
                false);
        Require(
            failed.ErrorVisible &&
            failed.ErrorMessage == "native failure" &&
            failed.CanStart,
            "Failed exposes native error and safe retry");

        RecordingPanelPresentationState hostError =
            RecordingPanelPresentationState.Create(
                Snapshot(RecordingReviewState.Idle),
                false,
                @"C:\recordings",
                string.Empty,
                string.Empty,
                string.Empty,
                true,
                "capture",
                false,
                false,
                false,
                "open failed");
        Require(
            hostError.ErrorVisible &&
            hostError.ErrorMessage == "open failed",
            "Host action errors are visible in every presentation");
    }

    private static void CompletedReturnIsSharedPresentationState()
    {
        RecordingPanelPresentationState state =
            RecordingPanelPresentationState.Create(
                Snapshot(RecordingReviewState.Completed),
                false,
                @"C:\recordings",
                string.Empty,
                string.Empty,
                string.Empty,
                false,
                "capture",
                false,
                false,
                false);
        Require(
            state.IdlePresentationVisible &&
            state.Title == "保存 / 录制" &&
            state.CanStart &&
            state.CanChangePath &&
            state.CanChangeFrameRate &&
            state.CanToggleTrayInFrame &&
            !state.CompletionSummaryVisible,
            "Completed dismissal is represented by shared state");
    }

    private static void OutputRootPersistencePreservesValidState()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "xbpreview-panel4",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string output = Path.Combine(directory, "recordings");
            Directory.CreateDirectory(output);
            ProductSettingsStore store = new(
                Path.Combine(directory, "product-settings.json"),
                legacyMicrophonePath: string.Empty);
            ProductState state = new(store);
            Require(
                state.TrySetOutputRoot(output),
                "valid local output root is accepted");
            state.Persist();
            ProductSettings accepted = state.Current;
            Require(
                !state.TrySetOutputRoot(
                    Path.Combine(directory, "missing")) &&
                state.Current == accepted,
                "invalid output root preserves the old state");
            Require(
                new ProductState(store).Current.OutputRoot ==
                    Path.GetFullPath(output),
                "ProductSettings.OutputRoot persists and reloads");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void FrameRateSelectionAndLockingAreExplicit()
    {
        RecordingPanelPresentationState idle60 = Create(
            RecordingReviewState.Idle,
            frameRateMode: RecordingFrameRateMode.Fps60);
        RecordingPanelPresentationState recording = Create(
            RecordingReviewState.Recording,
            frameRateMode: RecordingFrameRateMode.Fps60);
        RecordingPanelPresentationState paused = Create(
            RecordingReviewState.Paused,
            frameRateMode: RecordingFrameRateMode.Fps60);
        Require(
            idle60.FrameRateMode == RecordingFrameRateMode.Fps60 &&
            idle60.CanChangeFrameRate &&
            recording.FrameRateMode == RecordingFrameRateMode.Fps60 &&
            !recording.CanChangeFrameRate &&
            paused.FrameRateMode == RecordingFrameRateMode.Fps60 &&
            !paused.CanChangeFrameRate,
            "30/60 selection is explicit and locked for Recording/Paused");
    }

    private static RecordingPanelPresentationState Create(
        RecordingReviewState phase,
        bool commandPending = false,
        bool trayInFrame = false,
        bool restartConfirmationVisible = false,
        bool cancellationPending = false,
        RecordingFrameRateMode frameRateMode =
            RecordingFrameRateMode.Fps30) =>
        RecordingPanelPresentationState.Create(
            Snapshot(phase),
            commandPending,
            @"C:\recordings",
            @"C:\recordings\.working",
            @"C:\recordings\final.mp4",
            string.Empty,
            trayInFrame,
            "capture",
            false,
            false,
            false,
            actionError: null,
            restartConfirmationVisible: restartConfirmationVisible,
            cancellationPending: cancellationPending,
            frameRateMode: frameRateMode);

    private static RecordingReviewSnapshot Snapshot(
        RecordingReviewState phase) =>
        RecordingReviewSnapshot.Idle with
        {
            State = phase,
            SessionId = "panel4-test",
            Elapsed = TimeSpan.FromSeconds(65),
            PauseCount = 1,
            TotalPaused = TimeSpan.FromSeconds(5),
        };

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
