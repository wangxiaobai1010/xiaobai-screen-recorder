using System.Runtime.InteropServices;

namespace XbPreview.Host;

internal readonly record struct RawPointerActivity(
    bool IsLeftButtonDown,
    bool IsMeaningfulActivity);

internal interface IRawMouseInputApi
{
    bool Register(nint targetWindow, bool remove, out int windowsError);

    bool TryRead(
        nint rawInputHandle,
        out RawPointerActivity activity,
        out int windowsError);
}

internal sealed class RawMouseInputObserver : IDisposable
{
    internal const int WmInput = 0x00FF;

    private readonly IRawMouseInputApi _api;
    private nint _targetWindow;
    private bool _disposed;

    internal RawMouseInputObserver(IRawMouseInputApi? api = null)
    {
        _api = api ?? new Win32RawMouseInputApi();
    }

    internal event Action<RawPointerActivity>? ActivityObserved;

    internal bool IsActive { get; private set; }

    internal int LastWindowsError { get; private set; }

    internal bool Start(nint targetWindow)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (IsActive)
        {
            return true;
        }
        if (targetWindow == nint.Zero)
        {
            LastWindowsError = 1400; // ERROR_INVALID_WINDOW_HANDLE
            return false;
        }
        if (!_api.Register(targetWindow, remove: false, out int error))
        {
            LastWindowsError = error;
            return false;
        }

        _targetWindow = targetWindow;
        IsActive = true;
        LastWindowsError = 0;
        return true;
    }

    internal void Stop()
    {
        if (!IsActive)
        {
            return;
        }
        if (!_api.Register(nint.Zero, remove: true, out int error))
        {
            LastWindowsError = error;
        }
        else
        {
            LastWindowsError = 0;
        }
        _targetWindow = nint.Zero;
        IsActive = false;
    }

    internal bool ProcessMessage(int message, nint rawInputHandle)
    {
        if (!IsActive || message != WmInput)
        {
            return false;
        }
        if (!_api.TryRead(rawInputHandle, out RawPointerActivity activity, out int error))
        {
            LastWindowsError = error;
            return true;
        }
        LastWindowsError = 0;
        if (activity.IsMeaningfulActivity)
        {
            ActivityObserved?.Invoke(activity);
        }
        return true;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        Stop();
        ActivityObserved = null;
        _disposed = true;
    }
}

internal sealed class Win32RawMouseInputApi : IRawMouseInputApi
{
    private const ushort GenericDesktopUsagePage = 0x01;
    private const ushort MouseUsage = 0x02;
    private const uint RidevRemove = 0x00000001;
    private const uint RidevInputSink = 0x00000100;
    private const uint RidInput = 0x10000003;
    private const uint RimTypeMouse = 0;
    private const ushort LeftButtonDown = 0x0001;

    public bool Register(
        nint targetWindow,
        bool remove,
        out int windowsError)
    {
        RawInputDevice device = new()
        {
            UsagePage = GenericDesktopUsagePage,
            Usage = MouseUsage,
            Flags = remove ? RidevRemove : RidevInputSink,
            TargetWindow = remove ? nint.Zero : targetWindow,
        };
        bool succeeded = RegisterRawInputDevices(
            [device],
            1,
            (uint)Marshal.SizeOf<RawInputDevice>());
        windowsError = succeeded ? 0 : Marshal.GetLastWin32Error();
        return succeeded;
    }

    public bool TryRead(
        nint rawInputHandle,
        out RawPointerActivity activity,
        out int windowsError)
    {
        activity = default;
        uint size = (uint)Marshal.SizeOf<RawInput>();
        uint read = GetRawInputData(
            rawInputHandle,
            RidInput,
            out RawInput input,
            ref size,
            (uint)Marshal.SizeOf<RawInputHeader>());
        if (read == uint.MaxValue || read < size || input.Header.Type != RimTypeMouse)
        {
            windowsError = read == uint.MaxValue
                ? Marshal.GetLastWin32Error()
                : 0;
            return read != uint.MaxValue;
        }

        RawMouse mouse = input.Mouse;
        bool leftDown = (mouse.ButtonFlags & LeftButtonDown) != 0;
        bool meaningful = leftDown ||
            mouse.ButtonFlags != 0 ||
            mouse.LastX != 0 ||
            mouse.LastY != 0;
        activity = new RawPointerActivity(leftDown, meaningful);
        windowsError = 0;
        return true;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RawInputDevice
    {
        internal ushort UsagePage;
        internal ushort Usage;
        internal uint Flags;
        internal nint TargetWindow;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RawInputHeader
    {
        internal uint Type;
        internal uint Size;
        internal nint Device;
        internal nint WParam;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct RawMouse
    {
        [FieldOffset(0)] internal ushort Flags;
        [FieldOffset(4)] internal uint Buttons;
        [FieldOffset(4)] internal ushort ButtonFlags;
        [FieldOffset(6)] internal ushort ButtonData;
        [FieldOffset(8)] internal uint RawButtons;
        [FieldOffset(12)] internal int LastX;
        [FieldOffset(16)] internal int LastY;
        [FieldOffset(20)] internal uint ExtraInformation;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RawInput
    {
        internal RawInputHeader Header;
        internal RawMouse Mouse;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterRawInputDevices(
        [In] RawInputDevice[] devices,
        uint deviceCount,
        uint deviceSize);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetRawInputData(
        nint rawInputHandle,
        uint command,
        out RawInput data,
        ref uint size,
        uint headerSize);
}
