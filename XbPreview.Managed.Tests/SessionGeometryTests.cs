using System.Runtime.InteropServices;
using XbPreview.Host;

namespace XbPreview.Managed.Tests;

internal static class SessionGeometryTests
{
    internal static async Task RunAsync()
    {
        AbiLayoutIsStable();
        FullScreenMappingIsExact();
        OddDimensionsArePreserved();
        CaptureAndOutputRemainSeparate();
        await GeometryPrecedesAutomaticStartAsync();
        await GeometryFailurePreventsStartAsync();
        await PreviewingUpdateRemainsPendingAsync();
        await StopStartAppliesPendingAsync();
        await PendingGeometryIsLastWinsAsync();
        await ResizeDoesNotChangeGeometryAsync();
        await CloseRejectsGeometryAsync();
        await IdenticalGeometryReusesRevisionAsync();
    }

    private static void AbiLayoutIsStable()
    {
        Require(
            Marshal.SizeOf<SessionGeometryNativeV1>() == 56,
            "SessionGeometryNativeV1 size");
        Require(Offset(nameof(SessionGeometryNativeV1.SourceWidth)) == 8, "source offset");
        Require(Offset(nameof(SessionGeometryNativeV1.CaptureLeft)) == 16, "capture offset");
        Require(Offset(nameof(SessionGeometryNativeV1.OutputWidth)) == 32, "output offset");
        Require(Offset(nameof(SessionGeometryNativeV1.GeometryRevision)) == 40, "revision offset");
        Require(Offset(nameof(SessionGeometryNativeV1.Flags)) == 48, "flags offset");
        Require(
            SessionGeometryNativeV1.CurrentVersion == 1,
            "managed geometry version");
    }

    private static void FullScreenMappingIsExact()
    {
        SessionGeometry geometry = FullScreen();
        SessionGeometryNativeV1 native =
            SessionGeometryNativeV1.FromGeometry(geometry, 1);
        Require(
            native.StructSize == 56 &&
            native.Version == 1 &&
            native.SourceWidth == 1920 &&
            native.SourceHeight == 1080 &&
            native.CaptureLeft == 0 &&
            native.CaptureTop == 0 &&
            native.CaptureWidth == 1920 &&
            native.CaptureHeight == 1080 &&
            native.OutputWidth == 1920 &&
            native.OutputHeight == 1080 &&
            native.GeometryRevision == 1 &&
            native.Flags == 0 &&
            native.Reserved0 == 0,
            "full-screen mapping");
    }

    private static void OddDimensionsArePreserved()
    {
        SessionGeometryNativeV1 native =
            SessionGeometryNativeV1.FromGeometry(
                Custom(100, 50, 1001, 701),
                7);
        Require(
            native.CaptureWidth == 1001 &&
            native.CaptureHeight == 701 &&
            native.OutputWidth == 1001 &&
            native.OutputHeight == 701,
            "odd dimensions preserved");
    }

    private static void CaptureAndOutputRemainSeparate()
    {
        SessionGeometry geometry = SessionGeometry.Create(
            Display(),
            CaptureRegion.Create(100, 50, 1001, 701, 1920, 1080),
            OutputCanvas.CreateExplicit(1280, 720));
        SessionGeometryNativeV1 native =
            SessionGeometryNativeV1.FromGeometry(geometry, 9);
        Require(
            native.CaptureWidth == 1001 &&
            native.CaptureHeight == 701 &&
            native.OutputWidth == 1280 &&
            native.OutputHeight == 720,
            "capture and output separated");
    }

    private static async Task GeometryPrecedesAutomaticStartAsync()
    {
        PreviewLifecycleTests.Harness harness = new();
        await harness.InitializeAsync();
        harness.Log.Clear();
        await harness.StartAsync();
        RequireOrdered(harness.Log, "native:geometry:1", "native:start");
        await harness.Controller.CloseAsync();
    }

    private static async Task GeometryFailurePreventsStartAsync()
    {
        PreviewLifecycleTests.Harness harness = new();
        await harness.InitializeAsync();
        harness.Native.GeometryResults.Enqueue(
            NativeMethods.Result.InvalidGeometry);
        harness.Native.LastError = "synthetic invalid geometry";

        PreviewLifecycleResult result = await harness.StartAsync();

        Require(
            result.Status == PreviewLifecycleOperationStatus.Failed &&
            harness.Controller.State == PreviewLifecycleState.Error,
            "geometry failure reaches Error");
        Require(harness.Native.StartCount == 0, "geometry failure prevents Start");
        Require(harness.CameraServices.Count == 0, "geometry failure prevents camera");
        Require(harness.Availability.Last() == false, "geometry failure keeps hotkeys unavailable");
        await harness.Controller.CloseAsync();
    }

    private static async Task PreviewingUpdateRemainsPendingAsync()
    {
        PreviewLifecycleTests.Harness harness = new();
        await harness.InitializeAsync();
        await harness.StartAsync();
        int geometryCalls = harness.Native.GeometrySetCount;
        int starts = harness.Native.StartCount;
        int stops = harness.Native.StopCount;
        ulong priorRevision = harness.Controller.DesiredGeometryRevision;

        PreviewLifecycleResult result =
            await harness.Controller.SetDesiredGeometryAsync(
                Custom(100, 50, 1001, 701));

        Require(result.Succeeded, "previewing geometry request accepted");
        Require(
            harness.Controller.DesiredGeometryRevision > priorRevision,
            "previewing geometry revision increases");
        Require(harness.Native.GeometrySetCount == geometryCalls, "previewing setter deferred");
        Require(harness.Native.StartCount == starts, "previewing request does not Start");
        Require(harness.Native.StopCount == stops, "previewing request does not Stop");
        await harness.Controller.CloseAsync();
    }

    private static async Task StopStartAppliesPendingAsync()
    {
        PreviewLifecycleTests.Harness harness = new();
        await harness.InitializeAsync();
        await harness.StartAsync();
        await harness.Controller.SetDesiredGeometryAsync(
            Custom(100, 50, 1001, 701));
        harness.Log.Clear();

        await harness.Controller.StopAsync();
        await harness.StartAsync();

        RequireOrdered(
            harness.Log,
            "native:stop",
            "native:geometry:2",
            "native:start");
        Require(
            harness.Controller.ConfiguredGeometryRevision ==
            harness.Controller.DesiredGeometryRevision,
            "pending revision configured");
        await harness.Controller.CloseAsync();
    }

    private static async Task PendingGeometryIsLastWinsAsync()
    {
        PreviewLifecycleTests.Harness harness = new();
        await harness.InitializeAsync();
        await harness.StartAsync();
        await harness.Controller.SetDesiredGeometryAsync(
            Custom(10, 10, 1001, 701));
        await harness.Controller.SetDesiredGeometryAsync(
            Custom(20, 20, 901, 601));
        int before = harness.Native.GeometrySetCount;
        await harness.Controller.StopAsync();
        await harness.StartAsync();

        Require(
            harness.Native.GeometrySetCount == before + 1,
            "pending geometry submits once");
        SessionGeometryNativeV1 applied =
            harness.Native.GeometryHistory.Last();
        Require(
            applied.CaptureLeft == 20 &&
            applied.CaptureTop == 20 &&
            applied.CaptureWidth == 901 &&
            applied.CaptureHeight == 601,
            "pending geometry last-wins");
        await harness.Controller.CloseAsync();
    }

    private static async Task ResizeDoesNotChangeGeometryAsync()
    {
        PreviewLifecycleTests.Harness harness = new();
        await harness.InitializeAsync();
        ulong revision = harness.Controller.DesiredGeometryRevision;
        SessionGeometry desired = harness.Controller.DesiredGeometry!;
        await harness.Controller.RequestResizeAsync(800, 450);
        await harness.Controller.RequestResizeAsync(1280, 720);

        Require(
            harness.Controller.DesiredGeometryRevision == revision,
            "Resize does not change revision");
        Require(
            ReferenceEquals(harness.Controller.DesiredGeometry, desired),
            "Resize does not replace geometry");
        await harness.Controller.CloseAsync();
    }

    private static async Task CloseRejectsGeometryAsync()
    {
        PreviewLifecycleTests.Harness harness = new(blockStart: true);
        await harness.InitializeAsync();
        Task<PreviewLifecycleResult> start = harness.StartAsync();
        Require(harness.Native.StartEntered.Wait(3000), "Close geometry Start entered");
        Task close = harness.Controller.CloseAsync();
        Task<PreviewLifecycleResult> geometry =
            harness.Controller.SetDesiredGeometryAsync(
                Custom(100, 50, 1001, 701));
        harness.Native.ReleaseStart();
        await start;
        await close;
        PreviewLifecycleResult result = await geometry;

        Require(
            result.Status == PreviewLifecycleOperationStatus.Rejected,
            "Close rejects geometry");
        Require(harness.Native.GeometrySetCount == 1, "Close request never reaches native setter");
        Require(harness.Native.DisposeCount == 1, "Close disposes session once");
    }

    private static async Task IdenticalGeometryReusesRevisionAsync()
    {
        PreviewLifecycleTests.Harness harness = new();
        await harness.InitializeAsync();
        ulong revision = harness.Controller.DesiredGeometryRevision;
        PreviewLifecycleResult duplicate =
            await harness.Controller.SetDesiredGeometryAsync(
                FullScreen());

        Require(
            duplicate.Status == PreviewLifecycleOperationStatus.NoChange,
            "identical geometry is idempotent");
        Require(
            harness.Controller.DesiredGeometryRevision == revision,
            "identical geometry reuses revision");
        await harness.Controller.CloseAsync();
    }

    private static CaptureDisplaySnapshot Display() =>
        CaptureDisplaySnapshot.Create(
            "DISPLAY1",
            0,
            0,
            1920,
            1080,
            96,
            96);

    private static SessionGeometry FullScreen() =>
        SessionGeometry.CreateFullScreen(Display());

    private static SessionGeometry Custom(
        int left,
        int top,
        int width,
        int height)
    {
        CaptureDisplaySnapshot display = Display();
        CaptureRegion capture = CaptureRegion.Create(
            left,
            top,
            width,
            height,
            display.Width,
            display.Height);
        return SessionGeometry.Create(
            display,
            capture,
            OutputCanvas.CreateIdentity(capture));
    }

    private static int Offset(string field) =>
        Marshal.OffsetOf<SessionGeometryNativeV1>(field).ToInt32();

    private static void RequireOrdered(
        IReadOnlyList<string> log,
        params string[] expected)
    {
        int previous = -1;
        foreach (string item in expected)
        {
            int index = -1;
            for (int candidate = previous + 1; candidate < log.Count; candidate++)
            {
                if (log[candidate] == item)
                {
                    index = candidate;
                    break;
                }
            }
            Require(index >= 0, $"ordered event missing: {item}");
            previous = index;
        }
    }

    private static void Require(bool condition, string name)
    {
        if (!condition)
        {
            throw new InvalidOperationException(
                $"SessionGeometry test failed: {name}");
        }
    }
}
