namespace XbPreview.Avalonia.Contracts;

public enum ProductReviewCaptureTargetMode
{
    FullScreen = 0,
    Window = 1,
}

public enum ProductReviewManualZoom
{
    Wide = 0,
    Standard = 1,
    Strong = 2,
}

public enum ProductReviewStageOrientation
{
    Left = 0,
    Front = 1,
    Right = 2,
}

public enum ProductReviewStageLevel
{
    Level1 = 0,
    Level2 = 1,
    Level3 = 2,
}

public enum ProductReviewBackgroundPreset
{
    Warm = 0,
    Fantasy01 = 1,
    Fantasy001 = 2,
}

public sealed record ProductReviewWindowChoice(
    string Id,
    string ProcessName,
    string Title)
{
    public override string ToString() => Title;
}

public sealed record ProductReviewMicrophoneChoice(
    string Id,
    string DisplayName,
    bool Available)
{
    public override string ToString() => Available
        ? DisplayName
        : $"{DisplayName}（不可用）";
}

public sealed record ProductReviewSnapshot(
    ProductReviewCaptureTargetMode CaptureTargetMode,
    string SelectedWindowId,
    IReadOnlyList<ProductReviewWindowChoice> Windows,
    bool MicrophoneEnabled,
    string SelectedMicrophoneId,
    bool SelectedMicrophoneAvailable,
    IReadOnlyList<ProductReviewMicrophoneChoice> Microphones,
    bool SystemAudioEnabled,
    bool CursorVisible,
    bool HotkeysEnabled,
    string HotkeyState,
    bool AutoDirectorEnabled,
    bool ManualCommandsEnabled,
    ProductReviewManualZoom ManualZoom,
    ProductReviewStageOrientation StageOrientation,
    ProductReviewStageLevel StageLevel,
    ProductReviewBackgroundPreset BackgroundPreset,
    bool CustomBackgroundSelected,
    string CustomBackgroundPath,
    string OutputRoot,
    bool SettingsChangeEnabled,
    string StatusText);

public readonly record struct ProductReviewCommandResult(
    bool Succeeded,
    string Detail)
{
    public static ProductReviewCommandResult Success(string detail = "") =>
        new(true, detail);

    public static ProductReviewCommandResult Rejected(string detail) =>
        new(false, detail);
}

public interface IProductReviewController
{
    event Action<ProductReviewSnapshot>? SnapshotChanged;

    ProductReviewSnapshot CurrentSnapshot { get; }

    ProductReviewCommandResult RefreshDevices();

    Task<ProductReviewCommandResult> SetCaptureTargetFullScreenAsync();

    Task<ProductReviewCommandResult> SetCaptureTargetWindowAsync(string id);

    Task<ProductReviewCommandResult> SetMicrophoneEnabledAsync(bool enabled);

    Task<ProductReviewCommandResult> SetMicrophoneSelectionAsync(string id);

    Task<ProductReviewCommandResult> SetSystemAudioEnabledAsync(bool enabled);

    Task<ProductReviewCommandResult> SetCursorVisibleAsync(bool visible);

    Task<ProductReviewCommandResult> ExecuteManualZoomAsync(
        ProductReviewManualZoom zoom);

    Task<ProductReviewCommandResult> SetHotkeysEnabledAsync(bool enabled);

    Task<ProductReviewCommandResult> SetAutoDirectorEnabledAsync(bool enabled);

    Task<ProductReviewCommandResult> SetStagePoseAsync(
        ProductReviewStageOrientation orientation,
        ProductReviewStageLevel level);

    Task<ProductReviewCommandResult> SetBackgroundPresetAsync(
        ProductReviewBackgroundPreset preset);

    Task<ProductReviewCommandResult> SetCustomBackgroundAsync(string path);

    Task<ProductReviewCommandResult> SetOutputRootAsync(string? path);

    Task<ProductReviewCommandResult> ResetToDefaultsAsync();
}
