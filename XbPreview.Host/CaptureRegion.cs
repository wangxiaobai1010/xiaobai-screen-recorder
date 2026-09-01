namespace XbPreview.Host;

internal readonly record struct CaptureRegion
{
    internal int Left { get; }
    internal int Top { get; }
    internal int Width { get; }
    internal int Height { get; }
    internal int Right => checked(Left + Width);
    internal int Bottom => checked(Top + Height);

    private CaptureRegion(int left, int top, int width, int height)
    {
        Left = left;
        Top = top;
        Width = width;
        Height = height;
    }

    internal static CaptureRegion Create(
        int left,
        int top,
        int width,
        int height,
        int sourceWidth,
        int sourceHeight)
    {
        if (sourceWidth <= 0 || sourceHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sourceWidth),
                "Source dimensions must be positive.");
        }
        if (left < 0 || top < 0 || width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(width),
                "A capture region must have a non-negative origin and positive dimensions.");
        }

        long right = (long)left + width;
        long bottom = (long)top + height;
        if (right > sourceWidth || bottom > sourceHeight)
        {
            throw new ArgumentOutOfRangeException(
                nameof(width),
                "The capture region must be wholly inside its source.");
        }

        return new CaptureRegion(left, top, width, height);
    }

    internal bool IsWithin(int sourceWidth, int sourceHeight) =>
        sourceWidth > 0 &&
        sourceHeight > 0 &&
        Left >= 0 &&
        Top >= 0 &&
        Width > 0 &&
        Height > 0 &&
        (long)Left + Width <= sourceWidth &&
        (long)Top + Height <= sourceHeight;

    internal bool Contains(int x, int y) =>
        x >= Left && x < Right && y >= Top && y < Bottom;
}
