using XbPreview.Host;

namespace XbPreview.Managed.Tests;

internal static class ProductFeaturesTests
{
    internal static async Task RunAsync()
    {
        RegionCaptureIsDisabledByDefault();
        RegionCaptureButtonsAreHiddenAndLeaveTabOrder();
        await DisabledCommandsDoNotInvokeUiTransactionsAsync();
        await FullScreenStartupIsSingleStartAndStableAsync();
    }

    private static void RegionCaptureIsDisabledByDefault()
    {
        Require(
            !ProductFeatures.RegionCaptureEnabled,
            "RegionCaptureEnabled defaults to false");
        Require(
            ProductFeatures.ResolveUserCaptureRangeMode(
                CaptureRangeMode.CustomRegion) ==
                CaptureRangeMode.FullScreen,
            "disabled capability resolves stale CustomRegion to FullScreen");
        Require(
            ProductFeatures.ResolveUserCaptureRangeMode(
                CaptureRangeMode.FullScreen) ==
                CaptureRangeMode.FullScreen,
            "disabled capability preserves FullScreen");
    }

    private static void RegionCaptureButtonsAreHiddenAndLeaveTabOrder()
    {
        RegionCaptureUiPolicy policy = ProductFeatures.RegionCaptureUi;
        Require(!policy.Visible, "range buttons are hidden");
        Require(!policy.Enabled, "hidden range buttons are disabled");
        Require(!policy.TabStop, "hidden range buttons leave tab order");
    }

    private static async Task DisabledCommandsDoNotInvokeUiTransactionsAsync()
    {
        int invocationCount = 0;
        Func<Task> command = () =>
        {
            invocationCount++;
            return Task.CompletedTask;
        };

        bool regionInvoked =
            await ProductFeatures.TryExecuteRegionCaptureCommandAsync(command);
        bool fullScreenInvoked =
            await ProductFeatures.TryExecuteRegionCaptureCommandAsync(command);

        Require(!regionInvoked, "region UI command is rejected");
        Require(!fullScreenInvoked, "full-screen UI command is rejected");
        Require(
            invocationCount == 0,
            "disabled range commands do not reach Controller transactions");
    }

    private static async Task FullScreenStartupIsSingleStartAndStableAsync()
    {
        PreviewLifecycleTests.Harness harness = new();
        await harness.InitializeAsync(setDefaultGeometry: false);

        CaptureRangeMode startupMode =
            ProductFeatures.ResolveUserCaptureRangeMode(
                CaptureRangeMode.CustomRegion);
        SessionGeometry startupGeometry =
            startupMode == CaptureRangeMode.FullScreen
                ? harness.FullScreenGeometry
                : harness.CustomGeometry;
        PreviewLifecycleResult geometry =
            await harness.Controller.SetDesiredGeometryAsync(startupGeometry);
        Require(geometry.Succeeded, "startup FullScreen geometry accepted");
        ulong initialRevision =
            harness.Controller.DesiredGeometryRevision;

        PreviewLifecycleResult first = await harness.StartAsync();
        Require(first.Succeeded, "first FullScreen Preview starts");
        Require(harness.Native.StartCount == 1, "first Preview starts once");
        Require(
            harness.Native.GeometrySetCount == 1,
            "first Preview configures one Geometry");
        Require(
            harness.Controller.CurrentRangeMode ==
                CaptureRangeMode.FullScreen,
            "first Preview is FullScreen");
        Require(
            harness.Controller.CurrentGeometryRevision == initialRevision,
            "first Start adds no Geometry revision");

        PreviewLifecycleResult stopped =
            await harness.Controller.StopAsync();
        PreviewLifecycleResult restarted = await harness.StartAsync();
        Require(stopped.Succeeded && restarted.Succeeded, "Stop/Start succeeds");
        Require(harness.Native.StartCount == 2, "Stop/Start adds one Start");
        Require(
            harness.Native.GeometrySetCount == 1,
            "Stop/Start does not reconfigure unchanged FullScreen Geometry");
        Require(
            harness.Controller.CurrentRangeMode ==
                CaptureRangeMode.FullScreen,
            "Stop/Start remains FullScreen");
        Require(
            harness.Controller.CurrentGeometryRevision == initialRevision,
            "Stop/Start adds no Geometry revision");

        await harness.Controller.CloseAsync();
    }

    private static void Require(bool condition, string name)
    {
        if (!condition)
        {
            throw new InvalidOperationException(
                $"Product feature test failed: {name}");
        }
    }
}
