using System.Runtime.InteropServices;
using System.Text;

namespace XbPreview.Host;

internal enum CaptureTargetKind
{
    Monitor = 0,
    Window = 1,
}

internal readonly record struct CaptureTarget(
    CaptureTargetKind Kind,
    nint WindowHandle,
    string Title)
{
    internal static CaptureTarget FullScreen { get; } =
        new(CaptureTargetKind.Monitor, nint.Zero, "全屏");

    internal bool IsWindow => Kind == CaptureTargetKind.Window;
}

internal readonly record struct WindowCaptureChoice(nint Handle, string Title)
{
    public override string ToString() => Title;
}

internal static class WindowCaptureSelector
{
    private const uint DwmwaCloaked = 14;
    private const uint DwmwaExtendedFrameBounds = 9;

    internal static IReadOnlyList<WindowCaptureChoice> Enumerate()
    {
        List<WindowCaptureChoice> windows = [];
        uint currentProcessId = (uint)Environment.ProcessId;
        _ = EnumWindows((window, _) =>
        {
            if (TryDescribe(window, currentProcessId, out WindowCaptureChoice choice))
            {
                windows.Add(choice);
            }
            return true;
        }, nint.Zero);
        return windows
            .OrderBy(static item => item.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    internal static bool IsSelectableFacts(
        bool isWindow,
        bool isVisible,
        bool isRoot,
        bool isCloaked,
        uint processId,
        uint currentProcessId,
        string title) =>
        isWindow && isVisible && isRoot && !isCloaked &&
        processId != 0 && processId != currentProcessId &&
        !string.IsNullOrWhiteSpace(title);

    internal static bool TryMapDesktopPoint(
        nint window,
        int screenX,
        int screenY,
        out CameraPoint point)
    {
        point = default;
        if (!TryGetPhysicalWindowRect(window, out Rect rect))
        {
            return false;
        }
        int width = rect.Right - rect.Left;
        int height = rect.Bottom - rect.Top;
        return TryNormalizePoint(
            screenX,
            screenY,
            rect.Left,
            rect.Top,
            rect.Right,
            rect.Bottom,
            out point);
    }

    internal static bool TryNormalizePoint(
        int screenX,
        int screenY,
        int left,
        int top,
        int right,
        int bottom,
        out CameraPoint point)
    {
        point = default;
        int width = right - left;
        int height = bottom - top;
        if (width <= 0 || height <= 0 ||
            screenX < left || screenX >= right ||
            screenY < top || screenY >= bottom)
        {
            return false;
        }
        point = new CameraPoint(
            (screenX - left) / (double)width,
            (screenY - top) / (double)height);
        return true;
    }

    internal static bool TryMapCurrentCursor(
        nint window,
        out CameraPoint point)
    {
        point = default;
        return GetCursorPos(out Point cursor) &&
            TryMapDesktopPoint(window, cursor.X, cursor.Y, out point);
    }

    private static bool TryDescribe(
        nint window,
        uint currentProcessId,
        out WindowCaptureChoice choice)
    {
        choice = default;
        _ = GetWindowThreadProcessId(window, out uint processId);
        int titleLength = GetWindowTextLength(window);
        string title = string.Empty;
        if (titleLength > 0)
        {
            StringBuilder value = new(titleLength + 1);
            _ = GetWindowText(window, value, value.Capacity);
            title = value.ToString().Trim();
        }
        bool cloaked = DwmGetWindowAttribute(
            window,
            DwmwaCloaked,
            out uint cloakedValue,
            sizeof(uint)) == 0 && cloakedValue != 0;
        bool selectable = IsSelectableFacts(
            IsWindow(window),
            IsWindowVisible(window),
            GetAncestor(window, 2) == window,
            cloaked,
            processId,
            currentProcessId,
            title);
        if (selectable)
        {
            choice = new WindowCaptureChoice(window, title);
        }
        return selectable;
    }

    private static bool TryGetPhysicalWindowRect(nint window, out Rect rect)
    {
        if (DwmGetWindowAttribute(
                window,
                DwmwaExtendedFrameBounds,
                out rect,
                Marshal.SizeOf<Rect>()) == 0)
        {
            return true;
        }
        return GetWindowRect(window, out rect);
    }

    private delegate bool EnumWindowsCallback(nint window, nint state);

    [StructLayout(LayoutKind.Sequential)]
    private struct Point { internal int X; internal int Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        internal int Left;
        internal int Top;
        internal int Right;
        internal int Bottom;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsCallback callback, nint state);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(nint window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint window);

    [DllImport("user32.dll")]
    private static extern nint GetAncestor(nint window, uint flags);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(
        nint window,
        out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(nint window);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(
        nint window,
        StringBuilder value,
        int maximumCount);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint window, out Rect rect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out Point point);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(
        nint window,
        uint attribute,
        out uint value,
        int size);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(
        nint window,
        uint attribute,
        out Rect value,
        int size);
}
