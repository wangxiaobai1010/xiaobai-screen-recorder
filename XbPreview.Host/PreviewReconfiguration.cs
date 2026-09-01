namespace XbPreview.Host;

internal enum GeometrySelectionOutcome
{
    Confirmed,
    Cancelled,
    Failed,
}

internal sealed record GeometrySelectionResult(
    GeometrySelectionOutcome Outcome,
    SessionGeometry? CandidateGeometry,
    string? Error)
{
    internal static GeometrySelectionResult Confirmed(
        SessionGeometry geometry) =>
        new(
            GeometrySelectionOutcome.Confirmed,
            geometry ?? throw new ArgumentNullException(nameof(geometry)),
            null);

    internal static GeometrySelectionResult Cancelled() =>
        new(GeometrySelectionOutcome.Cancelled, null, null);

    internal static GeometrySelectionResult Failed(string error) =>
        new(
            GeometrySelectionOutcome.Failed,
            null,
            string.IsNullOrWhiteSpace(error)
                ? "Region selection failed."
                : error);
}

internal sealed record RegionSelectionRequest(
    SessionGeometry? RollbackGeometry,
    CaptureRegion? InitialSelection,
    CaptureRangeMode CurrentRangeMode)
{
    internal bool HasInitialSelection =>
        InitialSelection.HasValue;
}

internal readonly record struct PreviewRuntimeSettings(
    bool CameraEnabled,
    bool FollowEnabled,
    NativeMethods.CursorMode CursorMode,
    bool CameraCommandsAvailable)
{
    internal static PreviewRuntimeSettings ForGeometry(
        SessionGeometry geometry,
        PreviewRuntimeSettings requested)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        return IsCustomRegion(geometry)
            ? new PreviewRuntimeSettings(
                CameraEnabled: false,
                FollowEnabled: false,
                CursorMode: NativeMethods.CursorMode.SystemCursor,
                CameraCommandsAvailable: false)
            : requested;
    }

    internal static PreviewRuntimeSettings ForCaptureMode(
        CaptureRangeMode mode,
        PreviewRuntimeSettings requested) =>
        mode == CaptureRangeMode.CustomRegion
            ? new PreviewRuntimeSettings(
                CameraEnabled: false,
                FollowEnabled: false,
                CursorMode: NativeMethods.CursorMode.SystemCursor,
                CameraCommandsAvailable: false)
            : requested;

    internal static bool IsCustomRegion(SessionGeometry geometry)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        CaptureRegion region = geometry.CaptureRegion;
        CaptureDisplaySnapshot display = geometry.CaptureDisplay;
        return region.Left != 0 ||
            region.Top != 0 ||
            region.Width != display.Width ||
            region.Height != display.Height;
    }
}
