namespace XbPreview.Host;

internal enum OutputScaleMode
{
    Identity = 0,
    Explicit = 1,
}

internal readonly record struct OutputCanvas
{
    internal int Width { get; }
    internal int Height { get; }
    internal OutputScaleMode ScaleMode { get; }

    private OutputCanvas(
        int width,
        int height,
        OutputScaleMode scaleMode)
    {
        Width = width;
        Height = height;
        ScaleMode = scaleMode;
    }

    internal static OutputCanvas CreateIdentity(CaptureRegion region)
    {
        if (region.Width <= 0 || region.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(region));
        }
        return new OutputCanvas(
            region.Width,
            region.Height,
            OutputScaleMode.Identity);
    }

    internal static OutputCanvas CreateExplicit(int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(width),
                "OutputCanvas dimensions must be positive.");
        }
        return new OutputCanvas(
            width,
            height,
            OutputScaleMode.Explicit);
    }
}
