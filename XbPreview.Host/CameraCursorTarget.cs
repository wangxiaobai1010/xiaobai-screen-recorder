using System.ComponentModel;
using System.Runtime.InteropServices;

namespace XbPreview.Host;

internal readonly record struct CameraCursorObservation(
    bool GetCursorPosResult,
    int ScreenX,
    int ScreenY,
    double NormalizedX,
    double NormalizedY,
    bool InsidePrimaryMonitor,
    int LastError,
    string? Error)
{
    internal CameraPoint Normalized =>
        new(NormalizedX, NormalizedY);
}

internal static class CameraCursorTarget
{
    private const uint MonitorDefaultToPrimary = 1;

    internal static CameraPoint ReadPrimaryMonitorTarget()
    {
        CameraCursorObservation observation = ReadPrimaryMonitorObservation();
        if (!observation.GetCursorPosResult)
        {
            throw new Win32Exception(
                observation.LastError,
                observation.Error ?? "Primary monitor cursor read failed.");
        }

        // Preserve P1a's one-shot F9 behavior: a target outside the primary
        // capture is clamped to the primary content edge.
        return new CameraPoint(
            CameraMath.Clamp(observation.NormalizedX, 0.0, 1.0),
            CameraMath.Clamp(observation.NormalizedY, 0.0, 1.0));
    }

    internal static CameraCursorObservation ReadPrimaryMonitorObservation()
    {
        if (!GetCursorPos(out Point cursor))
        {
            int error = Marshal.GetLastWin32Error();
            return new CameraCursorObservation(
                false,
                0,
                0,
                0.0,
                0.0,
                false,
                error,
                "GetCursorPos failed.");
        }

        nint monitor = MonitorFromPoint(default, MonitorDefaultToPrimary);
        MonitorInfo info = new() { Size = (uint)Marshal.SizeOf<MonitorInfo>() };
        if (monitor == nint.Zero || !GetMonitorInfo(monitor, ref info))
        {
            int error = Marshal.GetLastWin32Error();
            return new CameraCursorObservation(
                false,
                cursor.X,
                cursor.Y,
                0.0,
                0.0,
                false,
                error,
                "GetMonitorInfo failed.");
        }

        int width = info.Monitor.Right - info.Monitor.Left;
        int height = info.Monitor.Bottom - info.Monitor.Top;
        if (width <= 0 || height <= 0)
        {
            return new CameraCursorObservation(
                false,
                cursor.X,
                cursor.Y,
                0.0,
                0.0,
                false,
                0,
                "Primary monitor dimensions are invalid.");
        }

        double normalizedX =
            (cursor.X - info.Monitor.Left) / (double)width;
        double normalizedY =
            (cursor.Y - info.Monitor.Top) / (double)height;
        bool inside =
            cursor.X >= info.Monitor.Left &&
            cursor.X < info.Monitor.Right &&
            cursor.Y >= info.Monitor.Top &&
            cursor.Y < info.Monitor.Bottom;
        return new CameraCursorObservation(
            true,
            cursor.X,
            cursor.Y,
            normalizedX,
            normalizedY,
            inside,
            0,
            null);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        internal int X;
        internal int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        internal int Left;
        internal int Top;
        internal int Right;
        internal int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        internal uint Size;
        internal Rect Monitor;
        internal Rect Work;
        internal uint Flags;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out Point point);

    [DllImport("user32.dll")]
    private static extern nint MonitorFromPoint(
        Point point,
        uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(
        nint monitor,
        ref MonitorInfo info);
}
