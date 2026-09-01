using System.Runtime.InteropServices;

namespace XbPreview.Host;

internal readonly record struct WindowDisplayAffinityResult(
    bool Succeeded,
    int WindowsErrorCode);

internal static class WindowDisplayAffinity
{
    private const uint WdaNone = 0x00000000;
    private const uint WdaExcludeFromCapture = 0x00000011;

    internal const uint AllowCapture = WdaNone;
    internal const uint ExcludeFromCapture = WdaExcludeFromCapture;

    internal static WindowDisplayAffinityResult TryExclude(nint window)
    {
        if (window == nint.Zero)
        {
            return new WindowDisplayAffinityResult(false, 87);
        }

        Marshal.SetLastPInvokeError(0);
        bool result = SetWindowDisplayAffinity(
            window,
            WdaExcludeFromCapture);
        return new WindowDisplayAffinityResult(
            result,
            result ? 0 : Marshal.GetLastPInvokeError());
    }

    internal static WindowDisplayAffinityResult TryAllow(nint window)
    {
        return TrySet(window, WdaNone);
    }

    internal static WindowDisplayAffinityResult TrySet(
        nint window,
        uint affinity)
    {
        if (window == nint.Zero)
        {
            return new WindowDisplayAffinityResult(false, 87);
        }

        Marshal.SetLastPInvokeError(0);
        bool result = SetWindowDisplayAffinity(
            window,
            affinity);
        return new WindowDisplayAffinityResult(
            result,
            result ? 0 : Marshal.GetLastPInvokeError());
    }

    internal static WindowDisplayAffinityResult TryRead(
        nint window,
        out uint affinity)
    {
        affinity = 0;
        if (window == nint.Zero)
        {
            return new WindowDisplayAffinityResult(false, 87);
        }

        Marshal.SetLastPInvokeError(0);
        bool result = GetWindowDisplayAffinity(window, out affinity);
        return new WindowDisplayAffinityResult(
            result,
            result ? 0 : Marshal.GetLastPInvokeError());
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowDisplayAffinity(
        nint window,
        uint affinity);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowDisplayAffinity(
        nint window,
        out uint affinity);
}
