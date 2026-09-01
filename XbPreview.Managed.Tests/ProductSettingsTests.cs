using XbPreview.Host;
using XbPreview.Avalonia.Views.Panels;

namespace XbPreview.Managed.Tests;

internal static class ProductSettingsTests
{
    internal static void Run()
    {
        DefaultsAreExact();
        Panel3PresentationDefaultsAreExact();
        RuntimeSwitchingUsesThinContracts();
        PersistenceReloadAndMicrophoneMigrationRoundTrip();
        MissingAndUnknownFrameRateNormalizeTo30();
        ResolutionPersistenceAndNormalizationAreExact();
        ResetToDefaultsIsExact();
    }

    private static void Panel3PresentationDefaultsAreExact()
    {
        Stage3DPanelPresentationSnapshot value =
            Stage3DPanelPresentationSnapshot.Initial;
        Require(
            value.Orientation == Stage3DPanelOrientation.Right &&
            value.Level == Stage3DPanelLevel.Level2 &&
            value.IsActive &&
            !value.ActionsEnabled,
            "Panel 3 reflects frozen RIGHT LEVEL_2 before runtime attach");
    }

    private static void DefaultsAreExact()
    {
        ProductSettings value = ProductSettings.Defaults;
        Require(value.CaptureTargetMode == ProductCaptureTargetMode.FullScreen,
            "capture target defaults to full screen");
        Require(value.MouseVisible, "recording mouse defaults on");
        Require(value.ManualHotkeysEnabled, "manual hotkeys default on");
        Require(!value.AutoDirectorEnabled, "auto director defaults off");
        Require(value.DirectorFinalVideoEnabled, "director final video defaults on");
        Require(value.StageOrientation == ProductStageOrientation.Right &&
            value.StageLevel == ProductStageLevel.Level2,
            "frozen SHOWCASE defaults to RIGHT LEVEL_2");
        Require(value.BackgroundSource == ProductBackgroundSource.Preset &&
            value.BackgroundPreset == ProductBackgroundPreset.Warm,
            "background defaults to Warm");
        Require(!value.MicrophoneEnabled && value.SystemAudioEnabled,
            "microphone defaults off and system audio defaults on");
        Require(value.OutputRoot is null && value.CustomBackgroundPath is null,
            "optional paths have safe unset defaults");
        Require(value.FrameRateMode == FrameRateMode.Fps30,
            "recording frame rate defaults to 30 FPS");
        Require(value.RecordingResolutionMode ==
                RecordingResolutionMode.Original,
            "recording resolution defaults to Original");
        Require(value.RecoveryDismissedSessionIds.Length == 0,
            "recovery reminder acknowledgements default to empty");
    }

    private static void RuntimeSwitchingUsesThinContracts()
    {
        PreviewLifecycleTests.FakeNativeSession native = new(
            [], blockStart: false, blockStop: false,
            blockRecordingStop: false);
        (NativeMethods.WindowStageOrientation, NativeMethods.WindowStageLevel)[]
            poses =
            [
                (NativeMethods.WindowStageOrientation.Front,
                    NativeMethods.WindowStageLevel.Level2),
                (NativeMethods.WindowStageOrientation.Left,
                    NativeMethods.WindowStageLevel.Level1),
                (NativeMethods.WindowStageOrientation.Right,
                    NativeMethods.WindowStageLevel.Level3),
                (NativeMethods.WindowStageOrientation.Front,
                    NativeMethods.WindowStageLevel.Level2),
            ];
        foreach ((NativeMethods.WindowStageOrientation orientation,
            NativeMethods.WindowStageLevel level) in poses)
        {
            Require(native.SetWindowShowcasePose(
                    orientation,
                    level,
                    active: true) ==
                NativeMethods.Result.Ok, $"pose {orientation}/{level}");
        }
        Require(native.StagePoses.SequenceEqual(poses),
            "all poses reach only the frozen Motion/Punch product seam");

        NativeMethods.WindowShowcaseBackgroundPreset[] backgrounds =
        [
            NativeMethods.WindowShowcaseBackgroundPreset.Warm,
            NativeMethods.WindowShowcaseBackgroundPreset.Art01,
            NativeMethods.WindowShowcaseBackgroundPreset.Art001,
            NativeMethods.WindowShowcaseBackgroundPreset.Warm,
        ];
        foreach (NativeMethods.WindowShowcaseBackgroundPreset preset in
            backgrounds)
        {
            Require(native.SetWindowShowcaseBackgroundPreset(preset) ==
                NativeMethods.Result.Ok, $"background {preset}");
        }
        Require(native.BackgroundPresets.SequenceEqual(backgrounds),
            "Warm -> 幻彩01 -> 幻彩02 -> Warm reaches one setter");

        string directory = NewTemporaryDirectory("runtime");
        try
        {
            string image = Path.Combine(directory, "background.png");
            File.WriteAllBytes(image, [0x89, 0x50, 0x4e, 0x47]);
            string output = Path.Combine(directory, "output");
            Directory.CreateDirectory(output);
            ProductSettings settings = ProductSettings.Defaults with
            {
                BackgroundSource = ProductBackgroundSource.CustomImage,
                CustomBackgroundPath = image,
                OutputRoot = output,
            };
            ProductSettingsApplyResult applied =
                ProductSettingsRuntimeAdapter.Apply(native, settings);
            Require(applied.Succeeded &&
                native.CustomBackgroundPaths.Last() == Path.GetFullPath(image),
                "validated custom image reaches native adapter");
            Require(native.RecordingOutputRoots.Last() == Path.GetFullPath(output),
                "validated output root reaches native adapter");
            Require(native.RecordingFrameRates.Last() == 30,
                "30 FPS default reaches the idle-only native setter");
            Require(!ProductPathContract.TryValidateCustomBackground(
                    Path.Combine(directory, "missing.png"), out _),
                "missing custom image is a safe validation failure");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void PersistenceReloadAndMicrophoneMigrationRoundTrip()
    {
        string directory = NewTemporaryDirectory("persistence");
        try
        {
            string productPath = Path.Combine(directory, "product-settings.json");
            string legacyPath = Path.Combine(directory, "microphone-selection.json");
            MicrophoneSelection migratedMicrophone = new(
                MicrophoneSelectionKind.ConcreteEndpoint,
                "{migrated-endpoint}",
                "Migrated microphone");
            MicrophoneSelectionSettings.Save(legacyPath, migratedMicrophone);
            ProductSettingsStore store = new(productPath, legacyPath);
            ProductSettings migrated = store.Load();
            Require(migrated.MicrophoneSelection == migratedMicrophone,
                "legacy microphone selection migrates into product settings");

            string image = Path.Combine(directory, "persisted.jpg");
            File.WriteAllBytes(image, [0xff, 0xd8, 0xff, 0xd9]);
            string output = Path.Combine(directory, "recordings");
            Directory.CreateDirectory(output);
            ProductSettings expected = migrated with
            {
                CaptureTargetMode = ProductCaptureTargetMode.Window,
                SelectedWindowIdentity = new("notepad", "Notes"),
                MicrophoneEnabled = true,
                SystemAudioEnabled = false,
                MouseVisible = false,
                ManualHotkeysEnabled = false,
                AutoDirectorEnabled = true,
                DirectorFinalVideoEnabled = false,
                StageOrientation = ProductStageOrientation.Right,
                StageLevel = ProductStageLevel.Level3,
                BackgroundSource = ProductBackgroundSource.CustomImage,
                BackgroundPreset = ProductBackgroundPreset.Fantasy001,
                CustomBackgroundPath = image,
                OutputRoot = output,
                FrameRateMode = FrameRateMode.Fps60,
                RecordingResolutionMode = RecordingResolutionMode.Qhd1440,
            };
            ProductState state = new(store);
            state.Set(expected);
            state.Persist();

            MicrophoneSelectionSettings.Save(
                legacyPath, MicrophoneSelection.WindowsDefault);
            ProductSettings reloaded = new ProductState(store).Current;
            ProductSettings normalizedExpected = expected with
            {
                CustomBackgroundPath = Path.GetFullPath(image),
                OutputRoot = Path.GetFullPath(output),
            };
            Require(reloaded with
                {
                    RecoveryDismissedSessionIds =
                        normalizedExpected.RecoveryDismissedSessionIds,
                } == normalizedExpected &&
                reloaded.RecoveryDismissedSessionIds.SequenceEqual(
                    normalizedExpected.RecoveryDismissedSessionIds),
                "all product values persist and reload exactly");
            Require(reloaded.MicrophoneSelection == migratedMicrophone,
                "product file remains the microphone source of truth");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void MissingAndUnknownFrameRateNormalizeTo30()
    {
        string directory = NewTemporaryDirectory("frame-rate-normalize");
        try
        {
            string productPath = Path.Combine(directory, "product-settings.json");
            ProductSettingsStore store = new(productPath, string.Empty);
            ProductSettings source = ProductSettings.Defaults with
            {
                AutoDirectorEnabled = true,
                FrameRateMode = FrameRateMode.Fps60,
            };
            store.Save(source);
            string persisted = File.ReadAllText(productPath);
            string missing = RemoveJsonProperty(persisted, "FrameRateMode");
            Require(missing != persisted,
                "test fixture removed the persisted frame-rate field");
            File.WriteAllText(productPath, missing);
            ProductSettings missingLoaded = store.Load();
            Require(
                missingLoaded.FrameRateMode == FrameRateMode.Fps30 &&
                missingLoaded.AutoDirectorEnabled,
                "missing frame-rate field falls back to 30 without resetting other settings");

            File.WriteAllText(
                productPath,
                persisted.Replace(
                    "\"Fps60\"",
                    "\"future-fps\"",
                    StringComparison.Ordinal));
            ProductSettings unknownLoaded = store.Load();
            Require(
                unknownLoaded.FrameRateMode == FrameRateMode.Fps30 &&
                unknownLoaded.AutoDirectorEnabled,
                "unknown frame-rate field normalizes to 30 without resetting other settings");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void ResolutionPersistenceAndNormalizationAreExact()
    {
        string directory = NewTemporaryDirectory("resolution-normalize");
        try
        {
            string productPath = Path.Combine(directory, "product-settings.json");
            ProductSettingsStore store = new(productPath, string.Empty);
            foreach (RecordingResolutionMode mode in new[]
            {
                RecordingResolutionMode.Fhd1080,
                RecordingResolutionMode.Qhd1440,
                RecordingResolutionMode.Uhd2160,
            })
            {
                store.Save(ProductSettings.Defaults with
                {
                    AutoDirectorEnabled = true,
                    RecordingResolutionMode = mode,
                });
                ProductSettings reloaded = store.Load();
                Require(
                    reloaded.RecordingResolutionMode == mode &&
                    reloaded.AutoDirectorEnabled,
                    $"{mode} persists without changing other settings");
            }

            store.Save(ProductSettings.Defaults with
            {
                AutoDirectorEnabled = true,
                RecordingResolutionMode = RecordingResolutionMode.Uhd2160,
            });
            string persisted = File.ReadAllText(productPath);
            File.WriteAllText(
                productPath,
                RemoveJsonProperty(persisted, "RecordingResolutionMode"));
            ProductSettings missing = store.Load();
            Require(
                missing.RecordingResolutionMode ==
                    RecordingResolutionMode.Original &&
                missing.AutoDirectorEnabled,
                "missing resolution defaults to Original only");

            string invalidFixture = persisted.Replace(
                    "\"Uhd2160\"",
                    "\"future-resolution\"",
                    StringComparison.Ordinal);
            File.WriteAllText(productPath, invalidFixture);
            ProductSettings invalid = store.Load();
            Require(
                invalid.RecordingResolutionMode ==
                    RecordingResolutionMode.Original &&
                invalid.AutoDirectorEnabled,
                "unknown resolution normalizes to Original only; " +
                $"actual={invalid.RecordingResolutionMode}; " +
                $"autoDirector={invalid.AutoDirectorEnabled}");

            string invalidNumericFixture = persisted.Replace(
                "\"Uhd2160\"",
                "999",
                StringComparison.Ordinal);
            File.WriteAllText(productPath, invalidNumericFixture);
            ProductSettings invalidNumeric = store.Load();
            Require(
                invalidNumeric.RecordingResolutionMode ==
                    RecordingResolutionMode.Original &&
                invalidNumeric.AutoDirectorEnabled,
                "invalid numeric resolution normalizes to Original only");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void ResetToDefaultsIsExact()
    {
        string directory = NewTemporaryDirectory("reset");
        try
        {
            ProductState state = new(new ProductSettingsStore(
                Path.Combine(directory, "product-settings.json"),
                Path.Combine(directory, "legacy.json")));
            state.Set(ProductSettings.Defaults with
            {
                StageOrientation = ProductStageOrientation.Left,
                StageLevel = ProductStageLevel.Level1,
                AutoDirectorEnabled = true,
            });
            Require(state.ResetToDefaults() == ProductSettings.Defaults &&
                state.Current == ProductSettings.Defaults,
                "ResetToDefaults restores the exact product contract");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string NewTemporaryDirectory(string suffix)
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            "xbpreview-product-settings-tests",
            $"{suffix}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static string RemoveJsonProperty(string json, string property)
    {
        string[] lines = json.Split(
            ["\r\n", "\n"],
            StringSplitOptions.None);
        int index = Array.FindIndex(
            lines,
            line => line.Contains(
                $"\"{property}\"",
                StringComparison.Ordinal));
        Require(index >= 0, $"fixture contains {property}");
        lines = lines.Where((_, candidate) => candidate != index).ToArray();
        if (index > 0 && lines[index - 1].TrimEnd().EndsWith(','))
        {
            bool nextClosesObject = index >= lines.Length ||
                lines[index].TrimStart().StartsWith('}');
            if (nextClosesObject)
            {
                lines[index - 1] = lines[index - 1].TrimEnd().TrimEnd(',');
            }
        }
        return string.Join(Environment.NewLine, lines);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(
                $"Product settings test failed: {message}");
        }
    }
}
