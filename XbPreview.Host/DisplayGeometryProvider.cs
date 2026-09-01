using System.ComponentModel;
using System.Runtime.InteropServices;

namespace XbPreview.Host;

internal sealed class DisplayGeometryProvider
{
    private const uint MonitorDefaultToPrimary = 1;
    private const int MonitorInfoPrimary = 1;
    private const int EffectiveDpi = 0;

    internal CaptureDisplaySnapshot ReadPrimaryDisplay()
    {
        NativePoint origin = default;
        nint monitor = MonitorFromPoint(origin, MonitorDefaultToPrimary);
        if (monitor == nint.Zero)
        {
            throw new Win32Exception(
                Marshal.GetLastWin32Error(),
                "MonitorFromPoint failed for the primary display.");
        }

        MonitorInfoEx info = new()
        {
            Size = Marshal.SizeOf<MonitorInfoEx>(),
            DeviceName = string.Empty,
        };
        if (!GetMonitorInfo(monitor, ref info) ||
            (info.Flags & MonitorInfoPrimary) == 0)
        {
            throw new Win32Exception(
                Marshal.GetLastWin32Error(),
                "GetMonitorInfo failed for the primary display.");
        }

        int width = checked(info.Monitor.Right - info.Monitor.Left);
        int height = checked(info.Monitor.Bottom - info.Monitor.Top);
        int dpiResult = GetDpiForMonitor(
            monitor,
            EffectiveDpi,
            out uint dpiX,
            out uint dpiY);
        if (dpiResult < 0 || dpiX == 0 || dpiY == 0)
        {
            throw new Win32Exception(
                dpiResult,
                "GetDpiForMonitor failed for the primary display.");
        }

        return CaptureDisplaySnapshot.Create(
            info.DeviceName,
            info.Monitor.Left,
            info.Monitor.Top,
            width,
            height,
            dpiX,
            dpiY);
    }

    internal PhysicalPixelPoint ReadCursorRelativeTo(
        CaptureDisplaySnapshot display)
    {
        ArgumentNullException.ThrowIfNull(display);
        if (!GetCursorPos(out NativePoint point))
        {
            throw new Win32Exception(
                Marshal.GetLastWin32Error(),
                "GetCursorPos failed while selecting a region.");
        }

        return new PhysicalPixelPoint(
            checked(point.X - display.DesktopLeft),
            checked(point.Y - display.DesktopTop));
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        internal int X;
        internal int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        internal int Left;
        internal int Top;
        internal int Right;
        internal int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfoEx
    {
        internal int Size;
        internal NativeRect Monitor;
        internal NativeRect Work;
        internal int Flags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        internal string DeviceName;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint MonitorFromPoint(
        NativePoint point,
        uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(
        nint monitor,
        ref MonitorInfoEx monitorInfo);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint point);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(
        nint monitor,
        int dpiType,
        out uint dpiX,
        out uint dpiY);
}
