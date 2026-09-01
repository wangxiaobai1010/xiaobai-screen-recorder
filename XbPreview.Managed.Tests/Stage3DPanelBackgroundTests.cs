using XbPreview.Avalonia.Views.Panels;
using XbPreview.Host;

namespace XbPreview.Managed.Tests;

internal static class Stage3DPanelBackgroundTests
{
    internal static void Run()
    {
        Stage3DPanelBackgroundSnapshot initial =
            Stage3DPanelBackgroundSnapshot.Initial;
        Require(initial.Source == Stage3DPanelBackgroundSource.Preset &&
            initial.Preset == Stage3DPanelBackgroundPreset.Warm &&
            initial.PresentationText == "Warm" &&
            !initial.ActionsEnabled,
            "Panel 3 background starts as disabled Warm");

        string directory = Path.Combine(
            Path.GetTempPath(),
            "xbpreview-panel3-background-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            RunSingleCustomImageFixture(directory);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void RunSingleCustomImageFixture(string directory)
    {
        string settingsPath = Path.Combine(directory, "product-settings.json");
        ProductSettingsStore store = new(
            settingsPath,
            Path.Combine(directory, "legacy-microphone.json"));
        ProductState productState = new(store);
        productState.Set(ProductSettings.Defaults with
        {
            StageOrientation = ProductStageOrientation.Left,
            StageLevel = ProductStageLevel.Level3,
        });
        Stage3DPanelBackgroundState presentation = new();
        BackgroundNative native = new();
        Stage3DPanelBackgroundController controller = new(
            presentation,
            productState,
            () => native);

        Require(controller.Initialize(actionsEnabled: true) ==
            NativeMethods.Result.Ok && presentation.Snapshot.ActionsEnabled,
            "Idle enables the background selector after Warm is applied");
        Require(native.Presets.SequenceEqual(
            [NativeMethods.WindowShowcaseBackgroundPreset.Warm]),
            "startup maps to exact frozen Warm preset");

        Require(controller.SelectPreset(Stage3DPanelBackgroundPreset.Art01) ==
            NativeMethods.Result.Ok &&
            controller.SelectPreset(Stage3DPanelBackgroundPreset.Art001) ==
                NativeMethods.Result.Ok &&
            controller.SelectPreset(Stage3DPanelBackgroundPreset.Warm) ==
                NativeMethods.Result.Ok,
            "all frozen presets are accepted");
        Require(native.Presets.SequenceEqual(
        [
            NativeMethods.WindowShowcaseBackgroundPreset.Warm,
            NativeMethods.WindowShowcaseBackgroundPreset.Art01,
            NativeMethods.WindowShowcaseBackgroundPreset.Art001,
            NativeMethods.WindowShowcaseBackgroundPreset.Warm,
        ]), "Warm / 幻彩01 / 幻彩02 use the exact native preset seam");

        Require(controller.SelectPreset(Stage3DPanelBackgroundPreset.Art01) ==
            NativeMethods.Result.Ok,
            "fixture establishes 幻彩01 before cancellation");
        Stage3DPanelBackgroundSnapshot beforeCancel = presentation.Snapshot;
        int customCallsBeforeCancel = native.CustomPaths.Count;
        Require(controller.SelectCustom(selectedPath: null) ==
            NativeMethods.Result.Ok &&
            presentation.Snapshot == beforeCancel &&
            native.CustomPaths.Count == customCallsBeforeCancel,
            "picker cancellation preserves the real background and presentation");

        string corrupt = Path.Combine(directory, "corrupt.png");
        File.WriteAllBytes(corrupt, [0x89, 0x50, 0x4e, 0x47]);
        native.CustomResult = path => string.Equals(
            path,
            Path.GetFullPath(corrupt),
            StringComparison.OrdinalIgnoreCase)
                ? NativeMethods.Result.NativeFailure
                : NativeMethods.Result.Ok;
        Require(controller.SelectCustom(corrupt) ==
            NativeMethods.Result.NativeFailure &&
            presentation.Snapshot.Source ==
                Stage3DPanelBackgroundSource.Preset &&
            presentation.Snapshot.Preset ==
                Stage3DPanelBackgroundPreset.Art01 &&
            productState.Current.BackgroundSource ==
                ProductBackgroundSource.Preset &&
            productState.Current.BackgroundPreset ==
                ProductBackgroundPreset.Fantasy01,
            "decode failure preserves 幻彩01 in runtime, state, and settings");

        string valid = Path.Combine(directory, "valid.png");
        File.WriteAllBytes(
            valid,
            Convert.FromBase64String(
                "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJ" +
                "AAAADUlEQVR42mNk+M/wHwAF/gL+JxJ6WQAAAABJRU5ErkJggg=="));
        Require(controller.SelectCustom(valid) == NativeMethods.Result.Ok &&
            presentation.Snapshot.Source ==
                Stage3DPanelBackgroundSource.CustomImage &&
            presentation.Snapshot.PresentationText == "自定义" &&
            presentation.Snapshot.CustomImagePath == Path.GetFullPath(valid) &&
            productState.Current.CustomBackgroundPath ==
                Path.GetFullPath(valid),
            "valid custom image keeps exact identity and presents 自定义");
        Require(productState.Current.StageOrientation ==
            ProductStageOrientation.Left &&
            productState.Current.StageLevel == ProductStageLevel.Level3,
            "background changes preserve the independent 2.5D pose state");

        Stage3DPanelBackgroundSnapshot beforeLock = presentation.Snapshot;
        controller.SetActionsEnabled(false);
        Require(controller.SelectPreset(Stage3DPanelBackgroundPreset.Warm) ==
            NativeMethods.Result.InvalidState &&
            (presentation.Snapshot with { ActionsEnabled = true }) == beforeLock,
            "Recording disables and locks the background selector");
        controller.SetActionsEnabled(false);
        Require(controller.SelectPreset(Stage3DPanelBackgroundPreset.Warm) ==
            NativeMethods.Result.InvalidState,
            "Paused uses the same disabled background contract");

        ProductSettings persisted = new ProductState(store).Current;
        Require(persisted.BackgroundSource ==
            ProductBackgroundSource.CustomImage &&
            persisted.CustomBackgroundPath == Path.GetFullPath(valid),
            "custom background path persists through the existing settings seam");
        File.Delete(valid);
        ProductSettings missingFallback = new ProductState(store).Current;
        Require(missingFallback.BackgroundSource ==
            ProductBackgroundSource.Preset &&
            missingFallback.BackgroundPreset == ProductBackgroundPreset.Warm &&
            missingFallback.CustomBackgroundPath is null,
            "missing persisted custom image fails safe to truthful Warm");

        foreach (string extension in new[] { ".png", ".jpg", ".jpeg", ".bmp" })
        {
            string path = Path.Combine(directory, "supported" + extension);
            File.WriteAllBytes(path, [0x00]);
            Require(ProductPathContract.TryValidateCustomBackground(path, out _),
                $"{extension} is allowed by the custom image contract");
        }
        string unsupported = Path.Combine(directory, "unsupported.gif");
        File.WriteAllBytes(unsupported, [0x00]);
        Require(!ProductPathContract.TryValidateCustomBackground(
            unsupported, out _),
            "unsupported file types are rejected before native decode");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(
                $"Panel 3 background test failed: {message}");
        }
    }

    private sealed class BackgroundNative : IWindowShowcaseBackgroundCommands
    {
        internal List<NativeMethods.WindowShowcaseBackgroundPreset> Presets
            { get; } = [];

        internal List<string> CustomPaths { get; } = [];

        internal Func<string, NativeMethods.Result> CustomResult { get; set; } =
            static _ => NativeMethods.Result.Ok;

        public NativeMethods.Result SetWindowShowcaseBackgroundPreset(
            NativeMethods.WindowShowcaseBackgroundPreset preset)
        {
            Presets.Add(preset);
            return NativeMethods.Result.Ok;
        }

        public NativeMethods.Result SetWindowShowcaseCustomBackground(
            string validatedLocalPath)
        {
            CustomPaths.Add(validatedLocalPath);
            return CustomResult(validatedLocalPath);
        }
    }
}
