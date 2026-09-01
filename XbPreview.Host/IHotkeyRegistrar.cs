using System.Runtime.InteropServices;

namespace XbPreview.Host;

internal interface IHotkeyRegistrar
{
    bool Register(
        nint window,
        int id,
        uint modifiers,
        uint virtualKey,
        out int windowsErrorCode);

    bool Unregister(nint window, int id);
}

internal sealed class Win32HotkeyRegistrar : IHotkeyRegistrar
{
    public bool Register(
        nint window,
        int id,
        uint modifiers,
        uint virtualKey,
        out int windowsErrorCode)
    {
        bool registered = RegisterHotKey(window, id, modifiers, virtualKey);
        windowsErrorCode = registered ? 0 : Marshal.GetLastWin32Error();
        return registered;
    }

    public bool Unregister(nint window, int id) =>
        UnregisterHotKey(window, id);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(
        nint window,
        int id,
        uint modifiers,
        uint virtualKey);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(nint window, int id);
}
