namespace XbPreview.Host;

// HUMAN REVIEW / PRESENTATION DATA ONLY.
// These labels are deliberately isolated from production audio device discovery,
// identifiers, selection, and lifecycle code.
internal sealed record FormalUiMicDevicePresentationItem(string Id, string Name);

internal sealed class FormalUiMicDevicePresentationState
{
    private static readonly IReadOnlyList<FormalUiMicDevicePresentationItem> DemoItems =
    [
        new("presentation-realtek", "麦克风 (Realtek(R) Audio)"),
        new("presentation-intel-array", "麦克风阵列 (Intel® Smart Sound)"),
        new("presentation-wh1000xm4", "耳机麦克风 (WH-1000XM4)"),
        new("presentation-nvidia-broadcast", "麦克风 (NVIDIA Broadcast)"),
    ];

    internal IReadOnlyList<FormalUiMicDevicePresentationItem> Items => DemoItems;
    internal FormalUiMicDevicePresentationItem SelectedDevice { get; private set; } = DemoItems[0];
    internal bool DeviceAvailable { get; private set; } = true;
    internal bool MicEnabled { get; private set; } = true;

    internal void Select(FormalUiMicDevicePresentationItem item)
    {
        if (DeviceAvailable && DemoItems.Contains(item))
        {
            SelectedDevice = item;
        }
    }

    internal void SetMicEnabled(bool enabled)
    {
        MicEnabled = DeviceAvailable && enabled;
    }

    internal void SetDeviceAvailable(bool available)
    {
        DeviceAvailable = available;
        if (!available)
        {
            MicEnabled = false;
        }

        // A returning device intentionally does not turn the microphone back on.
    }
}
