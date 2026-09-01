using XbPreview.Avalonia.Localization;

namespace XbPreview.Avalonia.Views.Panels;

public enum Stage3DPanelBackgroundPreset
{
    Warm = 0,
    Art01 = 1,
    Art001 = 2,
}

public enum Stage3DPanelBackgroundSource
{
    Preset = 0,
    CustomImage = 1,
}

public enum Stage3DPanelBackgroundChoice
{
    Warm = 0,
    Art01 = 1,
    Art001 = 2,
    Custom = 3,
}

public sealed record Stage3DPanelBackgroundSnapshot(
    Stage3DPanelBackgroundSource Source,
    Stage3DPanelBackgroundPreset Preset,
    string CustomImagePath,
    bool ActionsEnabled,
    string StatusText)
{
    public static Stage3DPanelBackgroundSnapshot Initial { get; } = new(
        Stage3DPanelBackgroundSource.Preset,
        Stage3DPanelBackgroundPreset.Warm,
        CustomImagePath: string.Empty,
        ActionsEnabled: false,
        StatusText: string.Empty);

    public string PresentationText => Source ==
        Stage3DPanelBackgroundSource.CustomImage
            ? Strings.Get("Custom")
            : Preset switch
            {
                Stage3DPanelBackgroundPreset.Art01 => Strings.Get("Fantasy01"),
                Stage3DPanelBackgroundPreset.Art001 => Strings.Get("Fantasy02"),
                _ => "Warm",
            };
}

public sealed class Stage3DPanelBackgroundState
{
    private Stage3DPanelBackgroundSnapshot _snapshot =
        Stage3DPanelBackgroundSnapshot.Initial;

    public event EventHandler? Changed;

    public Stage3DPanelBackgroundSnapshot Snapshot =>
        Volatile.Read(ref _snapshot);

    public void Apply(Stage3DPanelBackgroundSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (Volatile.Read(ref _snapshot) == snapshot)
        {
            return;
        }

        Volatile.Write(ref _snapshot, snapshot);
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
