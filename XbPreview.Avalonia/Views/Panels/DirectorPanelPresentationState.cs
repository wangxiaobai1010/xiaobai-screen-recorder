namespace XbPreview.Avalonia.Views.Panels;

public enum DirectorPanelManualZoom
{
    Wide = 0,
    Standard = 1,
    Strong = 2,
}

public sealed record DirectorPanelPresentationSnapshot(
    DirectorPanelManualZoom ManualZoom,
    bool HotkeysEnabled,
    bool AutoDirectorEnabled,
    bool ManualControlsEnabled,
    bool ActionsEnabled)
{
    public static DirectorPanelPresentationSnapshot Initial { get; } = new(
        DirectorPanelManualZoom.Wide,
        HotkeysEnabled: true,
        AutoDirectorEnabled: false,
        ManualControlsEnabled: false,
        ActionsEnabled: false);
}

/// <summary>
/// The single UI read model shared by the docked and floating live views.
/// Product commands and persistence remain owned by the Host-side adapter.
/// </summary>
public sealed class DirectorPanelPresentationState
{
    private DirectorPanelPresentationSnapshot _snapshot =
        DirectorPanelPresentationSnapshot.Initial;

    public event EventHandler? Changed;

    public DirectorPanelPresentationSnapshot Snapshot =>
        Volatile.Read(ref _snapshot);

    public void Apply(DirectorPanelPresentationSnapshot snapshot)
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
