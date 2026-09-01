namespace XbPreview.Host;

internal sealed record CaptureDisplaySnapshot
{
    internal string DeviceName { get; }
    internal int DesktopLeft { get; }
    internal int DesktopTop { get; }
    internal int Width { get; }
    internal int Height { get; }
    internal uint DpiX { get; }
    internal uint DpiY { get; }

    private CaptureDisplaySnapshot(
        string deviceName,
        int desktopLeft,
        int desktopTop,
        int width,
        int height,
        uint dpiX,
        uint dpiY)
    {
        DeviceName = deviceName;
        DesktopLeft = desktopLeft;
        DesktopTop = desktopTop;
        Width = width;
        Height = height;
        DpiX = dpiX;
        DpiY = dpiY;
    }

    internal static CaptureDisplaySnapshot Create(
        string deviceName,
        int desktopLeft,
        int desktopTop,
        int width,
        int height,
        uint dpiX,
        uint dpiY)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceName);
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(width),
                "Display physical dimensions must be positive.");
        }
        if (dpiX == 0 || dpiY == 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(dpiX),
                "Display DPI must be positive.");
        }

        _ = checked(desktopLeft + width);
        _ = checked(desktopTop + height);
        return new CaptureDisplaySnapshot(
            deviceName,
            desktopLeft,
            desktopTop,
            width,
            height,
            dpiX,
            dpiY);
    }

    internal bool Matches(CaptureDisplaySnapshot? other) =>
        other is not null &&
        string.Equals(DeviceName, other.DeviceName, StringComparison.Ordinal) &&
        DesktopLeft == other.DesktopLeft &&
        DesktopTop == other.DesktopTop &&
        Width == other.Width &&
        Height == other.Height &&
        DpiX == other.DpiX &&
        DpiY == other.DpiY;

    internal CaptureRegion FullRegion =>
        CaptureRegion.Create(0, 0, Width, Height, Width, Height);
}
