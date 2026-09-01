namespace XbPreview.Host;

internal enum MinimalShellPhase
{
    Idle,
    Countdown,
    Recording,
    Stopping,
    Completed,
    Failed,
}

internal enum RecordingShellAudioMode
{
    None,
    SystemOnly,
    MicrophoneOnly,
    Dual,
}

internal readonly record struct MinimalShellControlState(
    MinimalShellPhase Phase,
    string StateText,
    bool CanStart,
    bool CanStop,
    bool CanChangeAudio,
    bool CanChangeDirector,
    bool CanChangeStrength,
    bool CanUseManualCamera,
    bool ShowCompletion,
    bool ShowFailure,
    TimeSpan Elapsed);

internal static class MinimalRecordingShellPolicy
{
    internal static RecordingShellAudioMode AudioMode(
        bool systemEnabled,
        bool microphoneEnabled) => (systemEnabled, microphoneEnabled) switch
        {
            (true, false) => RecordingShellAudioMode.SystemOnly,
            (false, true) => RecordingShellAudioMode.MicrophoneOnly,
            (true, true) => RecordingShellAudioMode.Dual,
            _ => RecordingShellAudioMode.None,
        };

    internal static NativeMethods.AudioProgramMode NativeAudioMode(
        bool systemEnabled,
        bool microphoneEnabled) => AudioMode(
            systemEnabled,
            microphoneEnabled) switch
        {
            RecordingShellAudioMode.SystemOnly =>
                NativeMethods.AudioProgramMode.SystemOnly,
            RecordingShellAudioMode.MicrophoneOnly =>
                NativeMethods.AudioProgramMode.MicrophoneOnly,
            RecordingShellAudioMode.Dual =>
                NativeMethods.AudioProgramMode.Dual,
            _ => NativeMethods.AudioProgramMode.None,
        };

    internal static MinimalShellControlState Resolve(
        ManagedRecordingSnapshot snapshot,
        bool countdownActive,
        bool previewing,
        bool pendingOperation,
        bool directorEnabled)
    {
        bool completed = snapshot.State == ManagedRecordingState.Completed &&
            snapshot.ReadyToPublish &&
            snapshot.Published &&
            snapshot.OutputSuccess;
        MinimalShellPhase phase = countdownActive
            ? MinimalShellPhase.Countdown
            : snapshot.State switch
            {
                ManagedRecordingState.Starting or
                    ManagedRecordingState.Recording =>
                    MinimalShellPhase.Recording,
                ManagedRecordingState.Stopping => MinimalShellPhase.Stopping,
                ManagedRecordingState.Completed when completed =>
                    MinimalShellPhase.Completed,
                ManagedRecordingState.Completed => MinimalShellPhase.Failed,
                ManagedRecordingState.Failed => MinimalShellPhase.Failed,
                _ => MinimalShellPhase.Idle,
            };
        bool active = countdownActive || snapshot.IsActive;
        return new MinimalShellControlState(
            phase,
            StateText(phase),
            CanStart: previewing && !pendingOperation && !active &&
                snapshot.State is ManagedRecordingState.Idle or
                    ManagedRecordingState.Completed or
                    ManagedRecordingState.Failed,
            CanStop: !countdownActive && !pendingOperation &&
                snapshot.State is ManagedRecordingState.Starting or
                    ManagedRecordingState.Recording,
            CanChangeAudio: previewing && !pendingOperation && !active,
            CanChangeDirector: previewing && !active,
            CanChangeStrength: previewing && directorEnabled && !active,
            CanUseManualCamera: previewing && !directorEnabled &&
                phase is not MinimalShellPhase.Countdown and
                    not MinimalShellPhase.Stopping,
            ShowCompletion: completed,
            ShowFailure: snapshot.State == ManagedRecordingState.Failed ||
                snapshot.State == ManagedRecordingState.Completed && !completed,
            Elapsed: snapshot.Elapsed);
    }

    private static string StateText(MinimalShellPhase phase) => phase switch
    {
        MinimalShellPhase.Countdown => "准备录制",
        MinimalShellPhase.Recording => "正在录制",
        MinimalShellPhase.Stopping => "正在安全保存",
        MinimalShellPhase.Completed => "录制完成",
        MinimalShellPhase.Failed => "录制失败",
        _ => "准备就绪",
    };
}

internal sealed class MinimalRecordingShellActionGate
{
    private int _countdownActive;
    private int _stopRequested;

    internal bool CountdownActive => Volatile.Read(ref _countdownActive) != 0;

    internal bool TryBeginCountdown() =>
        Interlocked.CompareExchange(ref _countdownActive, 1, 0) == 0;

    internal void EndCountdown() =>
        Interlocked.Exchange(ref _countdownActive, 0);

    internal bool TryRequestStop() =>
        Interlocked.CompareExchange(ref _stopRequested, 1, 0) == 0;

    internal void Observe(ManagedRecordingSnapshot snapshot)
    {
        if (!snapshot.IsActive)
        {
            Interlocked.Exchange(ref _stopRequested, 0);
        }
    }
}
