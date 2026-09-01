using XbPreview.Avalonia.Contracts;
using XbPreview.Avalonia.Views;
using XbPreview.Avalonia.Localization;

namespace XbPreview.Avalonia.Views.Panels;

public enum Panel1AudioProgramMode
{
    None = 0,
    SystemOnly = 1,
    MicrophoneOnly = 2,
    Dual = 3,
}

public sealed record Panel1MicrophoneChoice(
    string Key,
    string DisplayName,
    bool IsWindowsDefault,
    bool Available)
{
    public override string ToString() => DisplayName;
}

public readonly record struct Panel1ControlAvailability(
    bool CaptureEnabled,
    bool AudioEnabled,
    bool CursorEnabled);

public enum Panel1AudioSourceIndicator
{
    Unavailable = 0,
    Available = 1,
    Ready = 2,
}

public static class Panel1PreparationPolicy
{
    public static Panel1AudioSourceIndicator ResolveAudioSourceIndicator(
        bool available,
        bool enabled) => !available
            ? Panel1AudioSourceIndicator.Unavailable
            : enabled
                ? Panel1AudioSourceIndicator.Ready
                : Panel1AudioSourceIndicator.Available;

    public static Panel1AudioProgramMode ResolveAudioProgramMode(
        bool microphoneEnabled,
        bool systemAudioEnabled) =>
        (microphoneEnabled, systemAudioEnabled) switch
        {
            (false, false) => Panel1AudioProgramMode.None,
            (false, true) => Panel1AudioProgramMode.SystemOnly,
            (true, false) => Panel1AudioProgramMode.MicrophoneOnly,
            (true, true) => Panel1AudioProgramMode.Dual,
        };

    public static Panel1ControlAvailability ResolveControlAvailability(
        RecordingReviewState recordingPhase,
        bool recordingCommandPending,
        bool captureCommandPending,
        bool audioCommandPending,
        bool cursorCommandPending)
    {
        bool recordingSessionActive = recordingCommandPending ||
            recordingPhase is RecordingReviewState.Starting or
                RecordingReviewState.Recording or
                RecordingReviewState.Paused or
                RecordingReviewState.Stopping;
        return new Panel1ControlAvailability(
            CaptureEnabled: !recordingSessionActive &&
                !captureCommandPending,
            AudioEnabled: !recordingSessionActive && !audioCommandPending,
            CursorEnabled: !cursorCommandPending);
    }
}

public sealed record Panel1PreparationSnapshot
{
    public long PresentationRevision { get; init; }

    public StructuralCaptureTargetPresentation CaptureTarget { get; init; }

    public IReadOnlyList<StructuralCaptureWindowChoice> WindowChoices
        { get; init; } = Array.Empty<StructuralCaptureWindowChoice>();

    public string CaptureDetail { get; init; } = string.Empty;

    public bool MouseHidden { get; init; }

    public string MouseHiddenDetail { get; init; } = string.Empty;

    public bool MicrophoneAvailable { get; init; }

    public bool SelectedMicrophoneAvailable { get; init; }

    public bool MicrophoneEnabled { get; init; }

    public IReadOnlyList<Panel1MicrophoneChoice> MicrophoneDevices
        { get; init; } = Array.Empty<Panel1MicrophoneChoice>();

    public string SelectedMicrophoneKey { get; init; } = string.Empty;

    public string MicrophoneDetail { get; init; } = string.Empty;

    public bool SystemAudioEnabled { get; init; }

    public bool SystemAudioDefaultRenderPresent { get; init; }

    public bool SystemAudioAvailable { get; init; }

    public string SystemAudioDetail { get; init; } = string.Empty;

    public int SystemMeterActiveSegments { get; init; }

    public bool SystemMeterAvailable { get; init; }

    public int MicrophoneMeterActiveSegments { get; init; }

    public bool MicrophoneMeterAvailable { get; init; }

    public Panel1AudioProgramMode AudioProgramMode { get; init; }

    public RecordingReviewState RecordingPhase { get; init; } =
        RecordingReviewState.Idle;

    public bool CaptureControlsEnabled { get; init; }

    public bool AudioControlsEnabled { get; init; }

    public bool CursorControlEnabled { get; init; }

    public bool Pending { get; init; }

    public string Detail { get; init; } = string.Empty;

    public static Panel1PreparationSnapshot Initial { get; } = new()
    {
        CaptureDetail = Strings.Get("CaptureConnecting"),
        MouseHiddenDetail = Strings.Get("CaptureConnecting"),
        MicrophoneDetail = Strings.Get("RefreshMicrophone"),
        SystemAudioDetail = Strings.Get("RefreshSystemAudio"),
        Detail = Strings.Get("CaptureConnecting"),
    };
}

public readonly record struct Panel1PreparationCommandResult(
    bool Succeeded,
    string Detail,
    Panel1PreparationSnapshot Snapshot);

public interface IPanel1PreparationController : IStructuralCaptureCommands
{
    event Action<Panel1PreparationSnapshot>? SnapshotChanged;

    Panel1PreparationSnapshot CurrentSnapshot { get; }

    Task<Panel1PreparationCommandResult> RefreshMicrophonesAsync();

    Task<Panel1PreparationCommandResult>
        RefreshSystemAudioAvailabilityAsync();

    Task<Panel1PreparationCommandResult> SetMicrophoneEnabledAsync(
        bool enabled);

    Task<Panel1PreparationCommandResult> SetSystemAudioEnabledAsync(
        bool enabled);

    Task<Panel1PreparationCommandResult> SelectMicrophoneAsync(
        Panel1MicrophoneChoice choice);

    Task<Panel1PreparationCommandResult> SetMouseHiddenAsync(bool hidden);
}
