namespace XbPreview.Host;

internal readonly record struct RegionCaptureUiPolicy(
    bool Visible,
    bool Enabled,
    bool TabStop);

internal static class ProductFeatures
{
    internal static readonly bool RegionCaptureEnabled = false;

    internal static RegionCaptureUiPolicy RegionCaptureUi =>
        new(
            Visible: RegionCaptureEnabled,
            Enabled: RegionCaptureEnabled,
            TabStop: RegionCaptureEnabled);

    internal static CaptureRangeMode ResolveUserCaptureRangeMode(
        CaptureRangeMode requestedMode) =>
        RegionCaptureEnabled
            ? requestedMode
            : CaptureRangeMode.FullScreen;

    internal static async Task<bool> TryExecuteRegionCaptureCommandAsync(
        Func<Task> command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!RegionCaptureEnabled)
        {
            return false;
        }

        await command();
        return true;
    }
}
