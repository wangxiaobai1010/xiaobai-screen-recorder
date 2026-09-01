namespace XbPreview.Host;

internal readonly record struct HotkeyBinding(
    int Id,
    uint VirtualKey,
    CameraCommand Command,
    string DisplayName);

internal static class HotkeyBindings
{
    internal const int F9Id = 0x0B09;
    internal const int F10Id = 0x0B10;
    internal const uint VkF9 = 0x78;
    internal const uint VkF10 = 0x79;

    internal static readonly HotkeyBinding Standard = new(
        F9Id,
        VkF9,
        CameraCommand.ToggleStandardCloseUp,
        "F9 / 切换 1.6x 标准特写与 1.0x 全景");

    internal static readonly HotkeyBinding Strong = new(
        F10Id,
        VkF10,
        CameraCommand.ToggleStrongCloseUp,
        "F10 / 切换 2.0x 强特写与 1.0x 全景");

    internal static IReadOnlyList<HotkeyBinding> All { get; } =
        [Standard, Strong];

    internal static bool TryResolveId(int id, out HotkeyBinding binding)
    {
        foreach (HotkeyBinding candidate in All)
        {
            if (candidate.Id == id)
            {
                binding = candidate;
                return true;
            }
        }

        binding = default;
        return false;
    }

    internal static bool TryResolveVirtualKey(
        uint virtualKey,
        out HotkeyBinding binding)
    {
        foreach (HotkeyBinding candidate in All)
        {
            if (candidate.VirtualKey == virtualKey)
            {
                binding = candidate;
                return true;
            }
        }

        binding = default;
        return false;
    }
}
