namespace XbPreview.Host;

internal enum RegionSelectionState
{
    NoSelection,
    Drawing,
    Selected,
    Moving,
    Resizing,
    Confirmed,
    Cancelled,
}

internal sealed class RegionSelectionStateMachine
{
    internal RegionSelectionState State { get; private set; } =
        RegionSelectionState.NoSelection;

    internal bool TryTransition(RegionSelectionState next)
    {
        if (State == next &&
            next is RegionSelectionState.Confirmed or RegionSelectionState.Cancelled)
        {
            return true;
        }

        bool allowed = State switch
        {
            RegionSelectionState.NoSelection =>
                next is RegionSelectionState.Drawing or RegionSelectionState.Cancelled,
            RegionSelectionState.Drawing =>
                next is RegionSelectionState.NoSelection or
                    RegionSelectionState.Selected or
                    RegionSelectionState.Cancelled,
            RegionSelectionState.Selected =>
                next is RegionSelectionState.Drawing or
                    RegionSelectionState.Moving or
                    RegionSelectionState.Resizing or
                    RegionSelectionState.Confirmed or
                    RegionSelectionState.Cancelled,
            RegionSelectionState.Moving or RegionSelectionState.Resizing =>
                next is RegionSelectionState.Selected or RegionSelectionState.Cancelled,
            RegionSelectionState.Confirmed or RegionSelectionState.Cancelled => false,
            _ => false,
        };
        if (allowed)
        {
            State = next;
        }
        return allowed;
    }
}

internal static class RegionSelectionAvailability
{
    internal static bool HasSelection(
        CaptureRegion? selectedRegion,
        RegionSelectionState state) =>
        selectedRegion.HasValue &&
        state == RegionSelectionState.Selected;

    internal static bool CanSelectRegion(
        bool closing,
        PreviewLifecycleState lifecycleState,
        bool overlayTransactionActive) =>
        !closing &&
        !overlayTransactionActive &&
        lifecycleState is
            PreviewLifecycleState.Previewing or
            PreviewLifecycleState.Stopped or
            PreviewLifecycleState.Error;

    internal static bool CanSelectRegion(
        bool closing,
        bool previewRunning,
        bool nativeStopped,
        bool overlayTransactionActive) =>
        !closing &&
        !previewRunning &&
        nativeStopped &&
        !overlayTransactionActive;
}
