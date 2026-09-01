using XbPreview.Avalonia.Contracts;
using XbPreview.Avalonia.Localization;

namespace XbPreview.Avalonia.Views.Panels;

public enum RecordingPanelTimerColor
{
    Hidden = 0,
    Orange = 1,
    Gray = 2,
    Black = 3,
}

public enum RecordingFrameRateMode
{
    Fps30 = 30,
    Fps60 = 60,
}

public enum RecordingResolutionChoice
{
    Original = 0,
    Fhd1080 = 1,
    Qhd1440 = 2,
    Uhd2160 = 3,
}

/// <summary>
/// Shared immutable Panel 4 state for Home and a future Floating view.
/// </summary>
public sealed record RecordingPanelPresentationState
{
    public RecordingReviewState RecordingState { get; init; }
    public bool CommandPending { get; init; }
    public string SessionId { get; init; } = string.Empty;
    public TimeSpan Elapsed { get; init; }
    public ulong PauseCount { get; init; }
    public TimeSpan TotalPaused { get; init; }
    public string CanonicalOutputRoot { get; init; } = string.Empty;
    public string WorkingPath { get; init; } = string.Empty;
    public string PlannedFinalPath { get; init; } = string.Empty;
    public string PublishedPath { get; init; } = string.Empty;
    public string ErrorMessage { get; init; } = string.Empty;
    public bool ErrorVisible { get; init; }
    public bool OutputSuccess { get; init; }
    public bool FinalizeSuccess { get; init; }
    public bool ValidationSuccess { get; init; }
    public bool ReadyToPublish { get; init; }
    public bool Published { get; init; }
    public bool TrayInFrame { get; init; }
    public RecordingFrameRateMode FrameRateMode { get; init; } =
        RecordingFrameRateMode.Fps30;
    public RecordingResolutionChoice ResolutionChoice { get; init; } =
        RecordingResolutionChoice.Original;
    public bool ResolutionUpscalesSource { get; init; }
    public string ResolutionToolTip { get; init; } = string.Empty;
    public string StartToolTip { get; init; } = string.Empty;
    public string CaptureAffinityResult { get; init; } = string.Empty;
    public bool CompletionSummaryVisible { get; init; }
    public bool IdlePresentationVisible { get; init; }
    public bool ActivePresentationVisible { get; init; }
    public bool CompletedPresentationVisible { get; init; }
    public bool ActiveCommandsVisible { get; init; }
    public bool RestartConfirmationVisible { get; init; }
    public bool CancellationPending { get; init; }
    public bool TimerVisible { get; init; }
    public RecordingPanelTimerColor TimerColor { get; init; }
    public bool IsCompletedPresentationActive =>
        RecordingState == RecordingReviewState.Completed &&
        CompletionSummaryVisible;
    public string Title { get; init; } = Strings.Get("RecordingTitle");
    public string StatusText { get; init; } = string.Empty;
    public string ElapsedText { get; init; } = "00:00:00";
    public string ReadyOutputPathText { get; init; } = Strings.Get("DefaultSaveLocation");
    public string FinalFileName { get; init; } = Strings.Get("NoFileYet");
    public string PauseResumeText { get; init; } = Strings.Get("Pause");
    public bool CanStart { get; init; }
    public bool CanPause { get; init; }
    public bool CanResume { get; init; }
    public bool CanStop { get; init; }
    public bool CanRestart { get; init; }
    public bool CanDismissRestartConfirmation { get; init; }
    public bool CanDiscardCurrentRecording { get; init; }
    public bool CanChangePath { get; init; }
    public bool CanChangeResolution { get; init; }
    public bool CanChangeFrameRate { get; init; }
    public bool CanToggleTrayInFrame { get; init; }
    public bool CanOpenFolder { get; init; }
    public bool CanOpenVideo { get; init; }
    public bool CanDismissCompletion { get; init; }

    public static RecordingPanelPresentationState Initial { get; } = Create(
        RecordingReviewSnapshot.Idle,
        true,
        string.Empty,
        string.Empty,
        string.Empty,
        string.Empty,
        false,
        Strings.Get("CaptureConnecting"),
        false,
        false,
        false);

    public static RecordingPanelPresentationState Create(
        RecordingReviewSnapshot recording,
        bool commandPending,
        string canonicalOutputRoot,
        string workingPath,
        string plannedFinalPath,
        string publishedPath,
        bool trayInFrame,
        string captureAffinityResult,
        bool completionSummaryVisible,
        bool publishedFileExists,
        bool publishedDirectoryExists,
        string? actionError = null,
        bool restartConfirmationVisible = false,
        bool cancellationPending = false,
        RecordingFrameRateMode frameRateMode = RecordingFrameRateMode.Fps30,
        RecordingResolutionChoice resolutionChoice =
            RecordingResolutionChoice.Original,
        bool resolutionUpscalesSource = false)
    {
        ArgumentNullException.ThrowIfNull(recording);

        RecordingReviewState phase = recording.State;
        bool completedPresentation =
            phase == RecordingReviewState.Completed &&
            completionSummaryVisible;
        bool readyPresentation = phase is
            RecordingReviewState.Idle or RecordingReviewState.Failed ||
            phase == RecordingReviewState.Completed &&
            !completionSummaryVisible;
        bool activePresentation = phase is
            RecordingReviewState.Starting or
            RecordingReviewState.Recording or
            RecordingReviewState.Paused or
            RecordingReviewState.Stopping;
        bool activeCommands = phase is
            RecordingReviewState.Recording or RecordingReviewState.Paused;
        bool confirmationVisible = restartConfirmationVisible &&
            activeCommands && !commandPending && !cancellationPending;
        bool activeCommandEnabled = activeCommands && !commandPending &&
            !confirmationVisible && !cancellationPending;
        bool timerVisible = phase is
            RecordingReviewState.Recording or
            RecordingReviewState.Paused or
            RecordingReviewState.Stopping || completedPresentation;
        RecordingPanelTimerColor timerColor = phase switch
        {
            RecordingReviewState.Recording =>
                RecordingPanelTimerColor.Orange,
            RecordingReviewState.Paused => RecordingPanelTimerColor.Gray,
            RecordingReviewState.Stopping or
                RecordingReviewState.Completed =>
                RecordingPanelTimerColor.Black,
            _ => RecordingPanelTimerColor.Hidden,
        };
        bool publishedContract = completedPresentation &&
            recording.OutputSuccess && recording.ReadyToPublish &&
            recording.Published && !string.IsNullOrWhiteSpace(publishedPath);
        string recordingError = phase == RecordingReviewState.Failed
            ? string.IsNullOrWhiteSpace(recording.ErrorMessage)
                ? Strings.Get("RecordingFailureDetail")
                : LocalizeRecordingError(recording.ErrorMessage)
            : string.Empty;
        string error = !string.IsNullOrWhiteSpace(actionError)
            ? actionError
            : recordingError;
        if (error == "MicUnavailableAtStart")
        {
            error = Strings.Get("MicrophoneUnavailableAtStart");
        }

        TimeSpan elapsed = recording.Elapsed < TimeSpan.Zero
            ? TimeSpan.Zero
            : recording.Elapsed;
        string canonical = canonicalOutputRoot ?? string.Empty;
        string published = publishedPath ?? string.Empty;

        return new RecordingPanelPresentationState
        {
            RecordingState = phase,
            CommandPending = commandPending,
            SessionId = recording.SessionId ?? string.Empty,
            Elapsed = elapsed,
            PauseCount = recording.PauseCount,
            TotalPaused = recording.TotalPaused < TimeSpan.Zero
                ? TimeSpan.Zero
                : recording.TotalPaused,
            CanonicalOutputRoot = canonical,
            WorkingPath = workingPath ?? string.Empty,
            PlannedFinalPath = plannedFinalPath ?? string.Empty,
            PublishedPath = published,
            ErrorMessage = error,
            ErrorVisible = !string.IsNullOrWhiteSpace(error),
            OutputSuccess = recording.OutputSuccess,
            FinalizeSuccess = recording.FinalizeAttempted &&
                recording.FinalizeHResult >= 0,
            ValidationSuccess = recording.ValidationAttempted &&
                recording.ValidationHResult >= 0,
            ReadyToPublish = recording.ReadyToPublish,
            Published = recording.Published,
            TrayInFrame = trayInFrame,
            FrameRateMode = frameRateMode is RecordingFrameRateMode.Fps60
                ? RecordingFrameRateMode.Fps60
                : RecordingFrameRateMode.Fps30,
            ResolutionChoice = Enum.IsDefined(resolutionChoice)
                ? resolutionChoice
                : RecordingResolutionChoice.Original,
            ResolutionUpscalesSource = resolutionUpscalesSource,
            ResolutionToolTip = resolutionUpscalesSource
                ? Strings.Get("UpscaleWarning")
                : string.Empty,
            StartToolTip =
                resolutionChoice == RecordingResolutionChoice.Uhd2160 &&
                frameRateMode == RecordingFrameRateMode.Fps60
                    ? Strings.Get("HighLoadWarning")
                    : string.Empty,
            CaptureAffinityResult = captureAffinityResult ?? string.Empty,
            CompletionSummaryVisible = completedPresentation,
            IdlePresentationVisible = readyPresentation,
            ActivePresentationVisible = activePresentation,
            CompletedPresentationVisible = completedPresentation,
            ActiveCommandsVisible = activeCommands,
            RestartConfirmationVisible = confirmationVisible,
            CancellationPending = cancellationPending,
            TimerVisible = timerVisible,
            TimerColor = timerColor,
            Title = phase switch
            {
                RecordingReviewState.Starting or
                    RecordingReviewState.Recording or
                    RecordingReviewState.Paused or
                    RecordingReviewState.Stopping => Strings.Get("Recording"),
                RecordingReviewState.Completed when completedPresentation =>
                    Strings.Get("RecordingComplete"),
                RecordingReviewState.Failed => Strings.Get("RecordingFailed"),
                _ => Strings.Get("RecordingTitle"),
            },
            StatusText = cancellationPending
                ? Strings.Get("Canceling")
                : phase switch
                {
                    RecordingReviewState.Starting => Strings.Get("Starting"),
                    RecordingReviewState.Recording => Strings.Get("RecordingNow"),
                    RecordingReviewState.Paused => Strings.Get("Paused"),
                    RecordingReviewState.Stopping => Strings.Get("Saving"),
                    RecordingReviewState.Completed => Strings.Get("Saved"),
                    RecordingReviewState.Failed => Strings.Get("RecordingFailed"),
                    _ => string.Empty,
                },
            ElapsedText = FormatElapsed(elapsed),
            ReadyOutputPathText = string.IsNullOrWhiteSpace(canonical)
                ? Strings.Get("DefaultSaveLocation")
                : canonical,
            FinalFileName = string.IsNullOrWhiteSpace(published)
                ? Strings.Get("NoFileYet")
                : Path.GetFileName(published),
            PauseResumeText = phase == RecordingReviewState.Paused
                ? Strings.Get("Resume")
                : Strings.Get("Pause"),
            CanStart = readyPresentation && !commandPending,
            CanPause = phase == RecordingReviewState.Recording &&
                activeCommandEnabled,
            CanResume = phase == RecordingReviewState.Paused &&
                activeCommandEnabled,
            CanStop = activeCommandEnabled,
            CanRestart = activeCommandEnabled,
            CanDismissRestartConfirmation = confirmationVisible,
            CanDiscardCurrentRecording = confirmationVisible,
            CanChangePath = readyPresentation && !commandPending,
            CanChangeResolution = readyPresentation && !commandPending,
            CanChangeFrameRate = readyPresentation && !commandPending,
            CanToggleTrayInFrame =
                (readyPresentation || phase is
                    RecordingReviewState.Recording or
                    RecordingReviewState.Paused) &&
                !commandPending && !confirmationVisible &&
                !cancellationPending,
            CanOpenFolder = publishedContract && publishedDirectoryExists &&
                !commandPending,
            CanOpenVideo = publishedContract && publishedFileExists &&
                !commandPending,
            CanDismissCompletion = completedPresentation && !commandPending,
        };
    }

    private static string FormatElapsed(TimeSpan elapsed) =>
        $"{(int)elapsed.TotalHours:00}:{elapsed.Minutes:00}:" +
        $"{elapsed.Seconds:00}";

    private static string LocalizeRecordingError(string error) => error switch
    {
        "当前选择的麦克风不可用，请重新连接或选择其他麦克风。" =>
            Strings.Get("RecordingMicDisconnected"),
        "磁盘空间不足，录制已安全停止；已有素材会保留。" =>
            Strings.Get("RecordingDiskFull"),
        "录制目录不可写，请检查目录权限或磁盘写保护。" =>
            Strings.Get("RecordingFolderReadOnly"),
        "录制目录或磁盘当前不可用；已有素材会保留。" =>
            Strings.Get("RecordingDriveUnavailable"),
        "写入录制磁盘时发生错误；已有素材会保留。" =>
            Strings.Get("RecordingDiskWriteFailed"),
        "录制未能完成，请检查录制目录和可用磁盘空间。" =>
            Strings.Get("RecordingStorageFailure"),
        _ => error,
    };
}

public interface IRecordingPanelController
{
    event Action<RecordingPanelPresentationState>? StateChanged;
    RecordingPanelPresentationState CurrentState { get; }
    Task StartAsync();
    Task PauseOrResumeAsync();
    Task StopAsync();
    void ShowRestartConfirmation();
    void DismissRestartConfirmation();
    Task DiscardCurrentRecordingAsync();
    void SetTrayInFrame(bool trayInFrame);
    Task SetResolutionAsync(RecordingResolutionChoice resolutionChoice);
    void SetFrameRate(RecordingFrameRateMode frameRateMode);
    void ChooseOutputRoot();
    void OpenRecordingFolder();
    void OpenRecording();
    void ReturnToRecordingReady();
}
