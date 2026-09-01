using System.Runtime.InteropServices;

namespace XbPreview.Host;

internal readonly record struct SystemAudioDefaultRenderAvailability(
    bool DefaultRenderPresent,
    bool Active);

internal static class WindowsCoreAudioDefaultRenderProbe
{
    private const int EndpointNotFound = unchecked((int)0x80070490);
    private const uint DeviceStateActive = 0x00000001;

    internal static SystemAudioDefaultRenderAvailability Query()
    {
        IMMDeviceEnumerator? enumerator = null;
        IMMDevice? endpoint = null;
        try
        {
            enumerator = (IMMDeviceEnumerator)(object)
                new MMDeviceEnumerator();
            int result = enumerator.GetDefaultAudioEndpoint(
                EDataFlow.Render,
                ERole.Multimedia,
                out endpoint);
            if (result == EndpointNotFound)
            {
                return new(false, false);
            }
            Marshal.ThrowExceptionForHR(result);

            result = endpoint.GetState(out uint state);
            Marshal.ThrowExceptionForHR(result);
            return new(
                DefaultRenderPresent: true,
                Active: (state & DeviceStateActive) != 0);
        }
        finally
        {
            Release(endpoint);
            Release(enumerator);
        }
    }

    private static void Release(object? value)
    {
        if (value is not null && Marshal.IsComObject(value))
        {
            _ = Marshal.FinalReleaseComObject(value);
        }
    }

    private enum EDataFlow
    {
        Render = 0,
        Capture = 1,
        All = 2,
    }

    private enum ERole
    {
        Console = 0,
        Multimedia = 1,
        Communications = 2,
    }

    [ComImport]
    [Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private sealed class MMDeviceEnumerator
    {
    }

    [ComImport]
    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig]
        int EnumAudioEndpoints(
            EDataFlow dataFlow,
            uint stateMask,
            out IntPtr devices);

        [PreserveSig]
        int GetDefaultAudioEndpoint(
            EDataFlow dataFlow,
            ERole role,
            [MarshalAs(UnmanagedType.Interface)] out IMMDevice endpoint);

        [PreserveSig]
        int GetDevice(
            [MarshalAs(UnmanagedType.LPWStr)] string id,
            [MarshalAs(UnmanagedType.Interface)] out IMMDevice endpoint);

        [PreserveSig]
        int RegisterEndpointNotificationCallback(IntPtr client);

        [PreserveSig]
        int UnregisterEndpointNotificationCallback(IntPtr client);
    }

    [ComImport]
    [Guid("D666063F-1587-4E43-81F1-B948E807363F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig]
        int Activate(
            in Guid interfaceId,
            uint classContext,
            IntPtr activationParameters,
            out IntPtr instance);

        [PreserveSig]
        int OpenPropertyStore(uint accessMode, out IntPtr properties);

        [PreserveSig]
        int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);

        [PreserveSig]
        int GetState(out uint state);
    }
}
