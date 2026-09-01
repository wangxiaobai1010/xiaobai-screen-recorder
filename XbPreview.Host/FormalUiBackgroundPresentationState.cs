namespace XbPreview.Host;

// HUMAN REVIEW / PRESENTATION STATE ONLY.
// This state intentionally has no dependency on the production background
// renderer, preview stage, recording output, or persistent settings.
internal enum FormalUiBackgroundMode
{
    Preset,
    CustomImage,
}

internal enum FormalUiBackgroundSwatch
{
    Warm,
    Fantasy01,
    Fantasy001,
    CustomImage,
}

internal sealed record FormalUiBackgroundPresentationItem(
    string Id,
    string DisplayName,
    FormalUiBackgroundSwatch Swatch,
    bool OpensFileDialog = false);

internal sealed class FormalUiBackgroundPresentationState
{
    internal const string CustomImageItemId = "custom-image";

    private static readonly IReadOnlyList<FormalUiBackgroundPresentationItem> PresentationItems =
    [
        new("warm", "暖白", FormalUiBackgroundSwatch.Warm),
        new("fantasy-01", "幻彩01", FormalUiBackgroundSwatch.Fantasy01),
        new("fantasy-001", "幻彩02", FormalUiBackgroundSwatch.Fantasy001),
        new(CustomImageItemId, "自定义图片…", FormalUiBackgroundSwatch.CustomImage, true),
    ];

    internal IReadOnlyList<FormalUiBackgroundPresentationItem> Items => PresentationItems;

    internal FormalUiBackgroundMode BackgroundMode { get; private set; } =
        FormalUiBackgroundMode.Preset;

    internal FormalUiBackgroundPresentationItem SelectedPreset { get; private set; } =
        PresentationItems[0];

    internal string? SelectedCustomImagePath { get; private set; }

    internal string ActiveItemId => BackgroundMode == FormalUiBackgroundMode.CustomImage
        ? CustomImageItemId
        : SelectedPreset.Id;

    internal string SelectorDisplayName => BackgroundMode == FormalUiBackgroundMode.CustomImage
        ? "自定义图片"
        : SelectedPreset.DisplayName;

    internal void SelectPreset(FormalUiBackgroundPresentationItem item)
    {
        if (item.OpensFileDialog || !PresentationItems.Contains(item))
        {
            return;
        }

        SelectedPreset = item;
        BackgroundMode = FormalUiBackgroundMode.Preset;
    }

    internal void SelectCustomImage(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        SelectedCustomImagePath = path;
        BackgroundMode = FormalUiBackgroundMode.CustomImage;
    }
}
