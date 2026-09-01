using System.Runtime.InteropServices;

namespace XbPreview.Host;

[StructLayout(LayoutKind.Sequential, Pack = 8)]
internal struct SessionGeometryNativeV1
{
    internal const uint CurrentVersion = 1;
    internal const int ExpectedSize = 56;

    internal uint StructSize;
    internal uint Version;
    internal int SourceWidth;
    internal int SourceHeight;
    internal int CaptureLeft;
    internal int CaptureTop;
    internal int CaptureWidth;
    internal int CaptureHeight;
    internal int OutputWidth;
    internal int OutputHeight;
    internal ulong GeometryRevision;
    internal uint Flags;
    internal uint Reserved0;

    internal static SessionGeometryNativeV1 FromGeometry(
        SessionGeometry geometry,
        ulong geometryRevision)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        if (geometryRevision == 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(geometryRevision),
                "Geometry revision must be non-zero.");
        }
        CaptureDisplaySnapshot display = geometry.CaptureDisplay;
        CaptureRegion capture = geometry.CaptureRegion;
        OutputCanvas output = geometry.OutputCanvas;
        if (!capture.IsWithin(display.Width, display.Height) ||
            output.Width <= 0 ||
            output.Height <= 0)
        {
            throw new ArgumentException(
                "SessionGeometry is not valid for the captured display.",
                nameof(geometry));
        }

        return new SessionGeometryNativeV1
        {
            StructSize = ExpectedSize,
            Version = CurrentVersion,
            SourceWidth = display.Width,
            SourceHeight = display.Height,
            CaptureLeft = capture.Left,
            CaptureTop = capture.Top,
            CaptureWidth = capture.Width,
            CaptureHeight = capture.Height,
            OutputWidth = output.Width,
            OutputHeight = output.Height,
            GeometryRevision = geometryRevision,
            Flags = 0,
            Reserved0 = 0,
        };
    }

    internal static bool ContentEquals(
        SessionGeometry left,
        SessionGeometry right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        return left.CaptureDisplay.Width == right.CaptureDisplay.Width &&
            left.CaptureDisplay.Height == right.CaptureDisplay.Height &&
            left.CaptureRegion == right.CaptureRegion &&
            left.OutputCanvas.Width == right.OutputCanvas.Width &&
            left.OutputCanvas.Height == right.OutputCanvas.Height;
    }
}
