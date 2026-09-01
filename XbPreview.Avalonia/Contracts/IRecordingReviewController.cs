namespace XbPreview.Avalonia.Contracts;

public enum RecordingReviewState
{
    Idle = 0,
    Starting = 1,
    Recording = 2,
    Paused = 3,
    Stopping = 4,
    Completed = 5,
    Failed = 6,
}

public sealed record RecordingReviewSnapshot(
    RecordingReviewState State,
    bool CommandPending,
    string SessionId,
    string OutputPath,
    string ErrorMessage,
    TimeSpan Elapsed,
    ulong FramesSubmitted,
    ulong PauseCount,
    TimeSpan TotalPaused,
    bool ActiveEncoder,
    bool OutputSuccess,
    bool FinalizeAttempted,
    int FinalizeHResult,
    uint FinalizeCount,
    bool ReadyToPublish,
    bool Published,
    bool PublishAttempted,
    int PublishHResult,
    bool ValidationAttempted,
    int ValidationHResult)
{
    public static RecordingReviewSnapshot Idle { get; } = new(
        RecordingReviewState.Idle,
        false,
        string.Empty,
        string.Empty,
        string.Empty,
        TimeSpan.Zero,
        0,
        0,
        TimeSpan.Zero,
        false,
        false,
        false,
        0,
        0,
        false,
        false,
        false,
        0,
        false,
        0);
}

public interface IRecordingReviewController
{
    event Action<RecordingReviewSnapshot>? SnapshotChanged;

    RecordingReviewSnapshot CurrentSnapshot { get; }

    Task StartAsync();

    Task PauseAsync();

    Task ResumeAsync();

    Task StopAsync();

    Task CancelAsync();
}
