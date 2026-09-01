using System.Diagnostics;
using XbPreview.Avalonia.Contracts;

namespace XbPreview.Avalonia.Localization;

public readonly record struct UiLanguagePresentation(
    bool EntryVisible,
    bool PromptVisible);

public static class UiLanguagePresentationPolicy
{
    public static bool ControlsAllowed(
        RecordingReviewState recordingState,
        bool commandPending) =>
        !commandPending && recordingState is
            RecordingReviewState.Idle or
            RecordingReviewState.Completed;

    public static UiLanguagePresentation Resolve(
        string activeLanguage,
        string persistedLanguage,
        RecordingReviewState recordingState,
        bool commandPending,
        bool promptDeferred)
    {
        bool entryVisible = ControlsAllowed(recordingState, commandPending);
        bool pending =
            UiLanguage.NormalizePersisted(activeLanguage) is { } active &&
            UiLanguage.NormalizePersisted(persistedLanguage) is
                { } persisted &&
            !string.Equals(active, persisted, StringComparison.Ordinal);
        return new UiLanguagePresentation(
            entryVisible,
            entryVisible && pending && !promptDeferred);
    }
}

public static class UiRestartContract
{
    public static ProcessStartInfo CreateRelaunchStartInfo(
        string executablePath,
        string workingDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        return new ProcessStartInfo
        {
            FileName = executablePath,
            WorkingDirectory = workingDirectory,
            UseShellExecute = true,
        };
    }
}
