namespace XbPreview.Host;

internal sealed record SessionGeometry
{
    internal CaptureDisplaySnapshot CaptureDisplay { get; }
    internal CaptureRegion CaptureRegion { get; }
    internal OutputCanvas OutputCanvas { get; }

    private SessionGeometry(
        CaptureDisplaySnapshot captureDisplay,
        CaptureRegion captureRegion,
        OutputCanvas outputCanvas)
    {
        CaptureDisplay = captureDisplay;
        CaptureRegion = captureRegion;
        OutputCanvas = outputCanvas;
    }

    internal static SessionGeometry Create(
        CaptureDisplaySnapshot captureDisplay,
        CaptureRegion captureRegion,
        OutputCanvas outputCanvas)
    {
        ArgumentNullException.ThrowIfNull(captureDisplay);
        if (!captureRegion.IsWithin(captureDisplay.Width, captureDisplay.Height))
        {
            throw new ArgumentOutOfRangeException(
                nameof(captureRegion),
                "CaptureRegion is outside its display snapshot.");
        }
        if (outputCanvas.Width <= 0 ||
            outputCanvas.Height <= 0 ||
            !Enum.IsDefined(outputCanvas.ScaleMode))
        {
            throw new ArgumentException(
                "OutputCanvas must have positive dimensions and a known scale mode.",
                nameof(outputCanvas));
        }

        return new SessionGeometry(
            captureDisplay,
            captureRegion,
            outputCanvas);
    }

    internal static SessionGeometry CreateFullScreen(
        CaptureDisplaySnapshot captureDisplay)
    {
        ArgumentNullException.ThrowIfNull(captureDisplay);
        CaptureRegion region = captureDisplay.FullRegion;
        return Create(
            captureDisplay,
            region,
            OutputCanvas.CreateIdentity(region));
    }
}

internal enum CaptureRangeMode
{
    FullScreen,
    CustomRegion,
}

internal readonly record struct SessionStartPlan(
    SessionGeometry Geometry,
    bool StartNativePreview,
    string Message);

internal static class SessionGeometryPlanner
{
    internal static SessionStartPlan CreateStartPlan(
        CaptureRangeMode mode,
        CaptureDisplaySnapshot currentDisplay,
        CaptureDisplaySnapshot? confirmedDisplay,
        CaptureRegion? confirmedRegion,
        bool overlayTransactionActive)
    {
        if (overlayTransactionActive)
        {
            throw new InvalidOperationException(
                "Preview cannot start while a region-selection transaction is active.");
        }
        ArgumentNullException.ThrowIfNull(currentDisplay);

        if (mode == CaptureRangeMode.FullScreen)
        {
            return new SessionStartPlan(
                SessionGeometry.CreateFullScreen(currentDisplay),
                true,
                "Full-screen geometry is valid.");
        }
        if (confirmedDisplay is null ||
            confirmedRegion is null ||
            !confirmedDisplay.Matches(currentDisplay))
        {
            throw new InvalidOperationException(
                "The confirmed custom region no longer matches the primary display.");
        }

        SessionGeometry geometry = SessionGeometry.Create(
            currentDisplay,
            confirmedRegion.Value,
            OutputCanvas.CreateIdentity(confirmedRegion.Value));
        return new SessionStartPlan(
            geometry,
            true,
            "Custom SessionGeometry is configured before Start and rendered " +
            "through the independent GPU Crop transform.");
    }
}
