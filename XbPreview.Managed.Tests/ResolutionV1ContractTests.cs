using System.Xml.Linq;
using XbPreview.Avalonia.Contracts;
using XbPreview.Avalonia.Views.Panels;
using XbPreview.Host;

namespace XbPreview.Managed.Tests;

internal static class ResolutionV1ContractTests
{
    private static readonly XNamespace XamlNamespace =
        "http://schemas.microsoft.com/winfx/2006/xaml";

    internal static void Run()
    {
        ResolutionModesAndOriginalAreExact();
        FixedOutputsContainWithoutStretchOrCrop();
        UpscalingPolicyUsesRenderedScale();
        DpiCannotChangePhysicalOutputPixels();
        PresentationLockAndHintsAreExact();
        SessionImmutabilityIsEnforcedAsync().GetAwaiter().GetResult();
        UiContractIsFrozen();
        NativeSeamsRemainSingleAndLinear();
    }

    private static void ResolutionModesAndOriginalAreExact()
    {
        SessionGeometry original = Geometry(1920, 1200, dpi: 96);
        RecordingResolutionPlan unchanged =
            RecordingResolutionPolicy.CreatePlan(
                RecordingResolutionMode.Original,
                original);
        Require(
            unchanged.Geometry.OutputCanvas.Width == 1920 &&
            unchanged.Geometry.OutputCanvas.Height == 1200 &&
            unchanged.Geometry.OutputCanvas.ScaleMode ==
                OutputScaleMode.Identity,
            "Original retains the frozen composition canvas");
        SessionGeometry frozenWindowCanvas = Geometry(2560, 1440, dpi: 144);
        RecordingResolutionPlan windowOriginal =
            RecordingResolutionPolicy.CreatePlan(
                RecordingResolutionMode.Original,
                frozenWindowCanvas);
        Require(
            windowOriginal.Geometry.OutputCanvas.Width == 2560 &&
            windowOriginal.Geometry.OutputCanvas.Height == 1440 &&
            windowOriginal.Geometry.OutputCanvas.ScaleMode ==
                OutputScaleMode.Identity,
            "Window Original retains its final stage canvas, not raw WGC size");

        (RecordingResolutionMode Mode, int Width, int Height)[] fixedModes =
        [
            (RecordingResolutionMode.Fhd1080, 1920, 1080),
            (RecordingResolutionMode.Qhd1440, 2560, 1440),
            (RecordingResolutionMode.Uhd2160, 3840, 2160),
        ];
        foreach ((RecordingResolutionMode mode, int width, int height) in
            fixedModes)
        {
            RecordingResolutionPlan plan =
                RecordingResolutionPolicy.CreatePlan(mode, original);
            Require(
                plan.Geometry.OutputCanvas.Width == width &&
                plan.Geometry.OutputCanvas.Height == height &&
                plan.Geometry.OutputCanvas.ScaleMode ==
                    OutputScaleMode.Explicit &&
                (width & 1) == 0 && (height & 1) == 0,
                $"{mode} resolves to the exact even NV12/H.264 dimensions");
        }
        Require(
            RecordingResolutionPolicy.Normalize(
                (RecordingResolutionMode)999) ==
                    RecordingResolutionMode.Original,
            "unknown in-memory mode normalizes to Original");
    }

    private static void FixedOutputsContainWithoutStretchOrCrop()
    {
        (int Width, int Height)[] sources =
        [
            (1920, 1080),
            (1920, 1200),
            (2560, 1080),
            (1600, 1200),
        ];
        (int Width, int Height)[] outputs =
        [
            (1920, 1080),
            (2560, 1440),
            (3840, 2160),
        ];
        foreach ((int sourceWidth, int sourceHeight) in sources)
        {
            foreach ((int outputWidth, int outputHeight) in outputs)
            {
                ContentViewport viewport =
                    RecordingResolutionPolicy.CalculateContainViewport(
                        sourceWidth,
                        sourceHeight,
                        outputWidth,
                        outputHeight);
                Require(viewport.IsValid, "contain viewport is valid");
                Require(
                    Near(
                        viewport.Width / viewport.Height,
                        sourceWidth / (double)sourceHeight) &&
                    viewport.X >= 0.0 && viewport.Y >= 0.0 &&
                    viewport.X + viewport.Width <= outputWidth + 0.001 &&
                    viewport.Y + viewport.Height <= outputHeight + 0.001,
                    $"{sourceWidth}x{sourceHeight} -> " +
                    $"{outputWidth}x{outputHeight} preserves the full aspect");
                Require(
                    Near(viewport.X * 2.0 + viewport.Width, outputWidth) &&
                    Near(viewport.Y * 2.0 + viewport.Height, outputHeight),
                    "letterbox/pillarbox is centered without silent crop");
            }
        }
    }

    private static void UpscalingPolicyUsesRenderedScale()
    {
        RecordingResolutionPlan upscale =
            RecordingResolutionPolicy.CreatePlan(
                RecordingResolutionMode.Uhd2160,
                Geometry(1920, 1080, dpi: 96));
        RecordingResolutionPlan portraitDownscale =
            RecordingResolutionPolicy.CreatePlan(
                RecordingResolutionMode.Fhd1080,
                Geometry(1080, 1920, dpi: 96));
        Require(upscale.UpscalesSource,
            "1080p source to 4K exposes the lightweight upscaling hint");
        Require(!portraitDownscale.UpscalesSource,
            "a contain downscale is not mislabeled from width alone");
    }

    private static void DpiCannotChangePhysicalOutputPixels()
    {
        foreach (uint dpi in new uint[] { 96, 120, 144 })
        {
            RecordingResolutionPlan plan =
                RecordingResolutionPolicy.CreatePlan(
                    RecordingResolutionMode.Qhd1440,
                    Geometry(1920, 1080, dpi));
            Require(
                plan.Geometry.OutputCanvas.Width == 2560 &&
                plan.Geometry.OutputCanvas.Height == 1440,
                $"DPI {dpi} cannot convert Avalonia DIP into output pixels");
        }
    }

    private static void PresentationLockAndHintsAreExact()
    {
        RecordingPanelPresentationState idle = CreatePresentation(
            RecordingReviewState.Idle,
            RecordingResolutionChoice.Uhd2160,
            RecordingFrameRateMode.Fps60,
            upscales: true);
        Require(
            idle.CanChangeResolution &&
            idle.ResolutionToolTip ==
                "将放大输出，不会增加原始画面的真实细节" &&
            idle.StartToolTip ==
                "4K + 60 FPS 对电脑性能要求较高",
            "idle 4K60 exposes both exact lightweight hints");

        foreach (RecordingReviewState state in new[]
        {
            RecordingReviewState.Starting,
            RecordingReviewState.Recording,
            RecordingReviewState.Paused,
            RecordingReviewState.Stopping,
        })
        {
            Require(
                !CreatePresentation(
                    state,
                    RecordingResolutionChoice.Qhd1440,
                    RecordingFrameRateMode.Fps30,
                    upscales: false).CanChangeResolution,
                $"{state} locks resolution intent and session dimensions");
        }
        foreach (ManagedRecordingState state in new[]
        {
            ManagedRecordingState.Starting,
            ManagedRecordingState.Recording,
            ManagedRecordingState.Pausing,
            ManagedRecordingState.Paused,
            ManagedRecordingState.Resuming,
            ManagedRecordingState.Stopping,
        })
        {
            Require(
                (ManagedRecordingSnapshot.Idle with { State = state }).IsActive,
                $"{state} is covered by the coordinator's active lock");
        }
    }

    private static async Task SessionImmutabilityIsEnforcedAsync()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"xb-resolution-session-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        PreviewLifecycleTests.Harness harness = new();
        try
        {
            await harness.InitializeAsync();
            Require((await harness.StartAsync()).Succeeded,
                "preview starts before idle resolution reconfigure");
            ProductState productState = new(new ProductSettingsStore(
                Path.Combine(directory, "product-settings.json"),
                legacyMicrophonePath: string.Empty));
            RecordingController recording =
                harness.Controller.GetOrCreateRecordingController();
            using RecordingResolutionCoordinator coordinator = new(
                harness.Controller,
                productState,
                recording);

            RecordingResolutionChangeResult selected =
                await coordinator.SetResolutionAsync(
                    RecordingResolutionMode.Qhd1440);
            Require(
                selected.Succeeded &&
                harness.Controller.CurrentGeometry?.OutputCanvas.Width == 2560 &&
                harness.Controller.CurrentGeometry?.OutputCanvas.Height == 1440 &&
                harness.CameraServices.Last().FollowEnabled,
                "idle selection reconfigures Preview to 1440p transactionally");

            ManagedRecordingSnapshot active = await recording.StartAsync();
            int geometrySets = harness.Native.GeometrySetCount;
            RecordingResolutionChangeResult rejected =
                await coordinator.SetResolutionAsync(
                    RecordingResolutionMode.Uhd2160);
            Require(
                active.IsActive && !rejected.Succeeded &&
                coordinator.CurrentMode == RecordingResolutionMode.Qhd1440 &&
                harness.Native.GeometrySetCount == geometrySets &&
                harness.Controller.CurrentGeometry?.OutputCanvas.Width == 2560,
                "active session rejects mode and native geometry mutation; " +
                $"state={active.State}; rejected={rejected.Succeeded}; " +
                $"mode={coordinator.CurrentMode}; " +
                $"geometrySets={harness.Native.GeometrySetCount}/{geometrySets}; " +
                $"width={harness.Controller.CurrentGeometry?.OutputCanvas.Width}");

            ManagedRecordingSnapshot stopped = await recording.StopAsync();
            RecordingResolutionChangeResult nextSession =
                await coordinator.SetResolutionAsync(
                    RecordingResolutionMode.Uhd2160);
            Require(
                stopped.State == ManagedRecordingState.Completed &&
                nextSession.Succeeded &&
                harness.Controller.CurrentGeometry?.OutputCanvas.Width == 3840 &&
                harness.Controller.CurrentGeometry?.OutputCanvas.Height == 2160 &&
                harness.CameraServices.Last().FollowEnabled,
                "terminal session permits the next idle selection");

            RecordingResolutionChangeResult original =
                await coordinator.SetResolutionAsync(
                    RecordingResolutionMode.Original);
            Require(
                original.Succeeded &&
                harness.Controller.CurrentGeometry?.OutputCanvas.Width == 1920 &&
                harness.Controller.CurrentGeometry?.OutputCanvas.Height == 1080 &&
                harness.Controller.CurrentGeometry?.OutputCanvas.ScaleMode ==
                    OutputScaleMode.Identity,
                "Original restores the unchanged final composition canvas");
        }
        finally
        {
            await harness.Controller.CloseAsync();
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void UiContractIsFrozen()
    {
        string root = Environment.CurrentDirectory;
        XDocument panel = XDocument.Load(Path.Combine(
            root,
            "XbPreview.Avalonia",
            "Views",
            "Panels",
            "RecordingPanelView.axaml"));
        XElement idle = FindNamed(panel, "RecordingIdlePresentation");
        XElement tray = FindNamed(panel, "TrayInFrameControl");
        XElement path = FindNamed(panel, "ChooseRecordingFolderButton");
        XElement resolution = FindNamed(panel, "ResolutionControl");
        XElement selector = FindNamed(panel, "ResolutionSelector");
        XElement frameRate = FindNamed(panel, "FrameRateControl");
        XElement start = FindNamed(panel, "StartRecordingButton");
        XElement commands = FindNamed(panel, "RecordingActiveCommands");
        XElement stop = FindNamed(panel, "StopRecordingButton");

        Require(
            Attribute(idle, "RowDefinitions") == "22,28,28,28,42" &&
            Attribute(idle, "RowSpacing") == "2" &&
            Attribute(tray, "Height") == "22" &&
            Attribute(path.Parent!, "Grid.Row") == "1" &&
            Attribute(resolution, "Grid.Row") == "2" &&
            Attribute(frameRate, "Grid.Row") == "3" &&
            Attribute(start, "Grid.Row") == "4",
            "Panel 4 order and compact idle geometry are exact");
        string[] choices = selector.Elements()
            .Where(element => element.Name.LocalName == "ComboBoxItem")
            .Select(element => Attribute(element, "Content"))
            .ToArray();
        Require(
            choices.SequenceEqual(
            [
                "原始（推荐）",
                "1080p（更稳定）",
                "2K（1440p）",
                "4K（性能要求高）",
            ]) &&
            Classes(selector).SequenceEqual(
                ["skill-select", "skill-window-select"]),
            "resolution selector has four exact product choices and frozen style");
        Require(
            Attribute(start, "Width") == "140" &&
            Attribute(start, "Height") == "42" &&
            Attribute(commands, "ColumnDefinitions") == "*,4,*,4,*" &&
            Attribute(stop, "Padding") == "13,8",
            "Start is 140x42 and active command geometry is unchanged");

        XDocument shell = XDocument.Load(Path.Combine(
            root,
            "XbPreview.Avalonia",
            "Views",
            "StructuralShellView.axaml"));
        Require(
            shell.Descendants().Any(element =>
                Attribute(element, "RowDefinitions") == "*,225"),
            "Panel deck remains exactly 225 DIP high (zero shell diff)");
        string panelText = File.ReadAllText(Path.Combine(
            root,
            "XbPreview.Avalonia",
            "Views",
            "Panels",
            "RecordingPanelView.axaml"));
        Require(
            !panelText.Contains("#0078", StringComparison.OrdinalIgnoreCase) &&
            !panelText.Contains("SystemAccent", StringComparison.OrdinalIgnoreCase),
            "resolution UI introduces no system-blue accent");
    }

    private static void NativeSeamsRemainSingleAndLinear()
    {
        string root = Environment.CurrentDirectory;
        string renderer = File.ReadAllText(Path.Combine(
            root, "XbPreview.Native", "PreviewRenderer.cpp"));
        string engine = File.ReadAllText(Path.Combine(
            root, "XbPreview.Native", "PreviewEngine.cpp"));
        string nativeGeometry = File.ReadAllText(Path.Combine(
            root, "XbPreview.Native", "SessionGeometryStore.cpp"));
        Require(
            renderer.Contains("CalculateLetterbox(", StringComparison.Ordinal) &&
            renderer.Contains("DrawFullscreenPass(", StringComparison.Ordinal) &&
            !renderer.Contains("CUBIC", StringComparison.OrdinalIgnoreCase) &&
            !renderer.Contains("LANCZOS", StringComparison.OrdinalIgnoreCase),
            "fullscreen uses contain while the production Linear pass remains");
        Require(
            engine.Contains(
                "Window capture uses the WGC cursor",
                StringComparison.Ordinal) &&
            engine.Contains("WindowStageComposer::ComposeFlat(",
                StringComparison.Ordinal),
            "Window cursor remains in captured content and flat-stage mapping is shared");
        Require(
            nativeGeometry.Contains(
                "state != XbPreviewState_Stopped",
                StringComparison.Ordinal) &&
            !Directory.GetFiles(
                    Path.Combine(root, "XbPreview.Host"),
                    "*Resolution*Native*",
                    SearchOption.TopDirectoryOnly).Any(),
            "active native geometry is immutable and no second resolution ABI exists");
    }

    private static SessionGeometry Geometry(int width, int height, uint dpi)
    {
        CaptureDisplaySnapshot display = CaptureDisplaySnapshot.Create(
            "DISPLAY-RESOLUTION-TEST",
            0,
            0,
            width,
            height,
            dpi,
            dpi);
        return SessionGeometry.CreateFullScreen(display);
    }

    private static RecordingPanelPresentationState CreatePresentation(
        RecordingReviewState state,
        RecordingResolutionChoice resolution,
        RecordingFrameRateMode frameRate,
        bool upscales) => RecordingPanelPresentationState.Create(
            RecordingReviewSnapshot.Idle with { State = state },
            commandPending: false,
            canonicalOutputRoot: @"C:\recordings",
            workingPath: string.Empty,
            plannedFinalPath: string.Empty,
            publishedPath: string.Empty,
            trayInFrame: false,
            captureAffinityResult: string.Empty,
            completionSummaryVisible: false,
            publishedFileExists: false,
            publishedDirectoryExists: false,
            frameRateMode: frameRate,
            resolutionChoice: resolution,
            resolutionUpscalesSource: upscales);

    private static XElement FindNamed(XDocument document, string name) =>
        document.Descendants().Single(element =>
            (string?)element.Attribute(XamlNamespace + "Name") == name);

    private static string Attribute(XElement element, string name) =>
        (string?)element.Attribute(name) ?? string.Empty;

    private static string[] Classes(XElement element) =>
        Attribute(element, "Classes").Split(
            ' ',
            StringSplitOptions.RemoveEmptyEntries);

    private static bool Near(double actual, double expected) =>
        Math.Abs(actual - expected) <= 0.001;

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(
                $"Resolution v1 contract failed: {message}");
        }
    }
}
