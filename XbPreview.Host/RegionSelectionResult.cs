namespace XbPreview.Host;

internal enum RegionSelectionCancelReason
{
    None,
    UserCancelled,
    DisplayChanged,
    Error,
}

internal readonly record struct RegionSelectionResult
{
    internal bool Confirmed { get; }
    internal bool Cancelled => !Confirmed;
    internal CaptureDisplaySnapshot? Display { get; }
    internal CaptureRegion? Region { get; }
    internal RegionSelectionCancelReason CancelReason { get; }
    internal string? Detail { get; }

    private RegionSelectionResult(
        bool confirmed,
        CaptureDisplaySnapshot? display,
        CaptureRegion? region,
        RegionSelectionCancelReason cancelReason,
        string? detail)
    {
        if (confirmed != (display is not null && region is not null) ||
            (confirmed && cancelReason != RegionSelectionCancelReason.None))
        {
            throw new ArgumentException("Illegal region-selection result.");
        }
        Confirmed = confirmed;
        Display = display;
        Region = region;
        CancelReason = cancelReason;
        Detail = detail;
    }

    internal static RegionSelectionResult Confirm(
        CaptureDisplaySnapshot display,
        CaptureRegion region) =>
        new(true, display, region, RegionSelectionCancelReason.None, null);

    internal static RegionSelectionResult Cancel(
        RegionSelectionCancelReason reason = RegionSelectionCancelReason.UserCancelled,
        string? detail = null)
    {
        if (reason == RegionSelectionCancelReason.None)
        {
            throw new ArgumentOutOfRangeException(nameof(reason));
        }
        return new RegionSelectionResult(false, null, null, reason, detail);
    }

    internal CaptureDisplaySnapshot? ResolveDisplay(
        CaptureDisplaySnapshot? previousDisplay) =>
        Confirmed ? Display : previousDisplay;

    internal CaptureRegion? ResolveRegion(CaptureRegion? previousRegion) =>
        Confirmed ? Region : previousRegion;
}
