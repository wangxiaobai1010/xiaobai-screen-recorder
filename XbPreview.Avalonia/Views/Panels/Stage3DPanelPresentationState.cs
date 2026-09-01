namespace XbPreview.Avalonia.Views.Panels;

public enum Stage3DPanelOrientation
{
    Left = 0,
    Front = 1,
    Right = 2,
}

public enum Stage3DPanelLevel
{
    Level1 = 0,
    Level2 = 1,
    Level3 = 2,
}

public sealed record Stage3DPanelPresentationSnapshot(
    Stage3DPanelOrientation Orientation,
    Stage3DPanelLevel Level,
    bool IsActive,
    bool ActionsEnabled)
{
    // The final frozen showcase enters RIGHT / LEVEL_2 on the first Window
    // Capture frame. Panel 3 reflects that exact base-pose target.
    public static Stage3DPanelPresentationSnapshot Initial { get; } = new(
        Stage3DPanelOrientation.Right,
        Stage3DPanelLevel.Level2,
        IsActive: true,
        ActionsEnabled: false);
}

public sealed record Stage3DPanelInteractionCommand(
    Stage3DPanelOrientation Orientation,
    Stage3DPanelLevel Level,
    bool IsActive);

/// <summary>
/// Pure Panel 3 interaction policy. FRONT is always a 2.5D pose; Flat is
/// represented only by IsActive=false and is never encoded as an orientation.
/// </summary>
public static class Stage3DPanelInteraction
{
    public static Stage3DPanelInteractionCommand DirectionClick(
        Stage3DPanelPresentationSnapshot current,
        Stage3DPanelOrientation direction)
    {
        ArgumentNullException.ThrowIfNull(current);
        bool activate = !current.IsActive || current.Orientation != direction;
        return new(direction, current.Level, activate);
    }

    public static Stage3DPanelInteractionCommand LevelClick(
        Stage3DPanelPresentationSnapshot current,
        Stage3DPanelLevel level)
    {
        ArgumentNullException.ThrowIfNull(current);
        return new(current.Orientation, level, current.IsActive);
    }
}

public sealed class Stage3DPanelPresentationState
{
    private Stage3DPanelPresentationSnapshot _snapshot =
        Stage3DPanelPresentationSnapshot.Initial;

    public event EventHandler? Changed;

    public Stage3DPanelPresentationSnapshot Snapshot =>
        Volatile.Read(ref _snapshot);

    public void Apply(Stage3DPanelPresentationSnapshot snapshot)
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
