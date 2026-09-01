using System.Globalization;
using Avalonia;
using Avalonia.VisualTree;
using XbPreview.Avalonia.Contracts;
using XbPreview.Avalonia.Localization;
using XbPreview.Avalonia.Views;
using XbPreview.Avalonia.Views.Panels;
using XbPreview.Host;

namespace XbPreview.Managed.Tests;

internal static class LocalizationContractTests
{
    internal static void Run()
    {
        CultureInfo originalUiCulture = CultureInfo.CurrentUICulture;
        CultureInfo? originalDefaultUiCulture =
            CultureInfo.DefaultThreadCurrentUICulture;
        try
        {
            CultureResolutionContracts();
            PersistenceAndBackwardCompatibilityContracts();
            ResourceParityAndCriticalKeys();
            SetupAvalonia();
            FinalProductLanguageUxContracts();
            FormalShellSmoke();
        }
        finally
        {
            CultureInfo.CurrentUICulture = originalUiCulture;
            CultureInfo.DefaultThreadCurrentUICulture = originalDefaultUiCulture;
        }
    }

    private static void CultureResolutionContracts()
    {
        Require(UiLanguage.Resolve(null, CultureInfo.GetCultureInfo("zh-HK")) ==
                UiLanguage.SimplifiedChinese,
            "A: missing setting plus zh Windows culture resolves zh-CN");
        Require(UiLanguage.Resolve(null, CultureInfo.GetCultureInfo("fr-FR")) ==
                UiLanguage.English,
            "B: missing setting plus non-zh Windows culture resolves en");
        Require(UiLanguage.Resolve("future-locale",
                CultureInfo.GetCultureInfo("zh-TW")) ==
                UiLanguage.SimplifiedChinese,
            "E: invalid setting safely follows the system UI culture");
    }

    private static void PersistenceAndBackwardCompatibilityContracts()
    {
        string directory = NewTemporaryDirectory("persistence");
        try
        {
            string path = Path.Combine(directory, "product-settings.json");
            ProductSettingsStore store = new(path, string.Empty);
            foreach (string language in new[]
            {
                UiLanguage.SimplifiedChinese,
                UiLanguage.English,
            })
            {
                ProductState state = new(store);
                Require(state.TrySetUiLanguage(language),
                    $"language {language} saves");
                Require(new ProductState(store).Current.UiLanguage == language,
                    $"language {language} reloads exactly");
            }

            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("zh-CN");
            ProductState restartOnly = new(store);
            Require(restartOnly.TrySetUiLanguage(UiLanguage.English),
                "I: selection persists");
            Require(CultureInfo.CurrentUICulture.Name == "zh-CN",
                "I: persistence does not hot-switch current UI culture");

            ProductSettings oldSettings = ProductSettings.Defaults with
            {
                AutoDirectorEnabled = true,
                OutputRoot = directory,
                RecoveryDismissedSessionIds = ["session-a"],
            };
            store.Save(oldSettings);
            string json = File.ReadAllText(path);
            File.WriteAllText(path, RemoveJsonProperty(json, "UiLanguage"));
            ProductSettings loadedOld = store.Load();
            Require(loadedOld.UiLanguage is null &&
                    loadedOld.AutoDirectorEnabled &&
                    loadedOld.OutputRoot == Path.GetFullPath(directory) &&
                    loadedOld.RecoveryDismissedSessionIds.SequenceEqual(
                        new[] { "session-a" }),
                "J: old settings load without losing existing values");

            store.Save(oldSettings with { UiLanguage = UiLanguage.English });
            json = File.ReadAllText(path).Replace(
                "\"en\"",
                "\"unsupported\"",
                StringComparison.Ordinal);
            File.WriteAllText(path, json);
            Require(store.Load().UiLanguage is null,
                "E: unsupported persisted language normalizes to missing");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void ResourceParityAndCriticalKeys()
    {
        IReadOnlySet<string> neutral = Strings.ResourceKeys(
            CultureInfo.InvariantCulture);
        IReadOnlySet<string> chinese = Strings.ResourceKeys(
            CultureInfo.GetCultureInfo("zh-CN"));
        IReadOnlySet<string> english = Strings.ResourceKeys(
            CultureInfo.GetCultureInfo("en"));
        Require(neutral.SetEquals(chinese) && neutral.SetEquals(english),
            "F: neutral, zh-CN, and en resource key sets are identical");
        Require(neutral.Count == 215,
            "F: the mature localization catalog remains exactly 215 keys");

        string[] criticalKeys =
        [
            "CaptureTitle", "CaptureTarget", "FullScreen", "Window",
            "DirectorTitle", "StageTitle", "RecordingTitle",
            "StartRecording", "Language", "RestartToApply", "BrandName",
            "LanguageEntry", "RestartNow", "RestartLater",
        ];
        string[] recoveryKeys =
        [
            "RecoveryFoundOne", "RecoveryBodyPreserved", "RecoveryTry",
            "OpenContainingFolder", "Later", "DontRemindAgain",
        ];
        foreach (CultureInfo culture in new[]
        {
            CultureInfo.GetCultureInfo("zh-CN"),
            CultureInfo.GetCultureInfo("en"),
        })
        {
            CultureInfo.CurrentUICulture = culture;
            foreach (string key in criticalKeys.Concat(recoveryKeys))
            {
                Require(!string.IsNullOrWhiteSpace(Strings.Get(key)),
                    $"G/H: {culture.Name}/{key} is nonempty");
            }
        }

        UiLanguage.Apply(UiLanguage.SimplifiedChinese);
        Require(Strings.BrandName == "小白录",
            "brand: zh-CN formal product name is 小白录");
        Require(Strings.FullScreen == "全屏" &&
                Strings.SystemAudio == "系统声音" &&
                Strings.Microphone == "麦克风" &&
                Strings.Light == "轻" &&
                Strings.Medium == "中" &&
                Strings.Strong == "强" &&
                Strings.RecordingTitle == "保存 / 录制" &&
                Strings.Pause == "暂停" &&
                Strings.RestartRecording == "重录" &&
                Strings.Stop == "停止" &&
                Strings.Get("Resume") == "继续",
            "Chinese HUMAN-approved Home and recording copy is unchanged");
        Require(Strings.RestartToApply == "语言将在重启后生效" &&
                Strings.RestartNow == "立即重启" &&
                Strings.RestartLater == "稍后",
            "restart: zh-CN prompt copy is final");
        UiLanguage.Apply(UiLanguage.English);
        Require(Strings.BrandName == "Xiaobai Recorder",
            "brand: English formal product name is Xiaobai Recorder");
        Require(Strings.FullScreen == "Screen" &&
                Strings.SystemAudio == "PC Audio" &&
                Strings.Microphone == "Mic" &&
                Strings.Light == "Low" &&
                Strings.Medium == "Mid" &&
                Strings.Strong == "High" &&
                Strings.RecordingTitle == "Record" &&
                Strings.ManualZoom == "Manual Zoom" &&
                Strings.AutoZoom == "Auto Zoom" &&
                Strings.ResolutionOriginal == "Original" &&
                Strings.Resolution1080 == "1080p" &&
                Strings.Resolution4K == "4K",
            "English Home uses complete, natural short labels");
        Require(Strings.Pause == "Pause" &&
                Strings.RestartRecording == "Redo" &&
                Strings.Stop == "Stop" &&
                Strings.Get("Resume") == "Resume",
            "I: English recording commands are Pause/Redo/Stop and Resume/Redo/Stop");
        Require(Strings.LanguageEntry == "中 / EN",
            "J: language entry copy is exactly 中 / EN");
        RecordingPanelPresentationState recording =
            CreateRecordingPresentation(RecordingReviewState.Recording);
        RecordingPanelPresentationState paused =
            CreateRecordingPresentation(RecordingReviewState.Paused);
        Require(recording.PauseResumeText == "Pause" &&
                paused.PauseResumeText == "Resume",
            "I: formal Recording and Paused presentation labels are exact");
        Require(Strings.RestartToApply ==
                    "Language will apply after restart" &&
                Strings.RestartNow == "Restart Now" &&
                Strings.RestartLater == "Later",
            "restart: English prompt copy is final");
    }

    private static void SetupAvalonia()
    {
        AppBuilder.Configure<XbPreview.Avalonia.App>()
            .UsePlatformDetect()
            .SetupWithoutStarting();
    }

    private static void FinalProductLanguageUxContracts()
    {
        string directory = NewTemporaryDirectory("final-ux");
        try
        {
            string path = Path.Combine(directory, "product-settings.json");
            ProductSettingsStore store = new(path, string.Empty);
            ProductState productState = new(store);
            Require(productState.TrySetUiLanguage(
                    UiLanguage.SimplifiedChinese),
                "final UX fixture starts persisted in zh-CN");

            UiLanguage.Apply(UiLanguage.SimplifiedChinese);
            StructuralShellView shell = CreatePersistingShell(
                productState,
                UiLanguage.SimplifiedChinese,
                UiLanguage.SimplifiedChinese,
                out RequestCounter persistenceRequests);
            shell.ApplyLanguageRecordingState(
                RecordingReviewState.Idle,
                commandPending: false);
            Require(shell.LanguageEntryVisible &&
                    !shell.RestartPromptVisible,
                "A: Idle shows the Home language entry without a prompt");
            Require(shell.LanguageEntryUsesHomeOverlay &&
                    shell.LanguageEntryButtonWidth >= 68.0 &&
                    !shell.SettingsVisible,
                "H/J: full 中 / EN entry is on the Home overlay, not Settings");
            shell.ShowSettings();
            Require(!shell.SettingsVisible,
                "H: the formal product has no independent Settings surface");

            Require(shell.RequestUiLanguageSelection(UiLanguage.English),
                "active zh-CN can persist an English choice");
            Require(shell.HasPendingUiLanguage &&
                    shell.PersistedUiLanguage == UiLanguage.English &&
                    productState.Current.UiLanguage == UiLanguage.English &&
                    CultureInfo.CurrentUICulture.Name == "zh-CN" &&
                    shell.RestartPromptVisible,
                "selection persists without a hot switch and shows prompt");

            Require(shell.RequestUiLanguageSelection(
                    UiLanguage.SimplifiedChinese),
                "selecting the active language persists cancellation");
            Require(!shell.HasPendingUiLanguage &&
                    productState.Current.UiLanguage ==
                        UiLanguage.SimplifiedChinese &&
                    !shell.RestartPromptVisible,
                "selecting back to active language cancels pending");

            Require(shell.RequestUiLanguageSelection(UiLanguage.English),
                "English can be selected again for pending contracts");
            Require(shell.DeferRestartPrompt() &&
                    !shell.RestartPromptVisible &&
                    CultureInfo.CurrentUICulture.Name == "zh-CN",
                "Later hides only the prompt and does not hot-switch");
            ProductSettings nextLaunchSettings =
                new ProductSettingsStore(path, string.Empty).Load();
            Require(UiLanguage.Resolve(
                    nextLaunchSettings.UiLanguage,
                    CultureInfo.GetCultureInfo("zh-CN")) ==
                    UiLanguage.English,
                "Later leaves the persisted choice for the next fresh start");

            Require(shell.RequestUiLanguageSelection(UiLanguage.English) &&
                    shell.RestartPromptVisible &&
                    shell.HasPendingUiLanguage &&
                    CultureInfo.CurrentUICulture.Name == "zh-CN",
                "A: clicking the same pending English option after Later reopens prompt");
            Require(shell.RequestUiLanguageSelection(
                    UiLanguage.SimplifiedChinese) &&
                    !shell.HasPendingUiLanguage &&
                    !shell.RestartPromptVisible &&
                    productState.Current.UiLanguage ==
                        UiLanguage.SimplifiedChinese,
                "B: clicking active zh-CN cancels pending and hides prompt");
            Require(shell.RequestUiLanguageSelection(UiLanguage.English),
                "pending English is restored for recording visibility contracts");

            int requestsBeforeRecording = persistenceRequests.Count;
            shell.ApplyLanguageRecordingState(
                RecordingReviewState.Recording,
                commandPending: false);
            Require(!shell.LanguageEntryVisible &&
                    !shell.RestartPromptVisible,
                "B: Recording hides both language entry and restart prompt");
            Require(!shell.RequestUiLanguageSelection(
                    UiLanguage.SimplifiedChinese) &&
                    !shell.DeferRestartPrompt() &&
                    !shell.RequestRestartNow() &&
                    persistenceRequests.Count == requestsBeforeRecording &&
                    shell.HasPendingUiLanguage &&
                    shell.PersistedUiLanguage == UiLanguage.English,
                "E: Recording blocks language/restart actions and preserves pending");

            shell.ApplyLanguageRecordingState(
                RecordingReviewState.Paused,
                commandPending: false);
            Require(!shell.LanguageEntryVisible &&
                    !shell.RestartPromptVisible &&
                    shell.HasPendingUiLanguage,
                "C: Paused hides language UI while preserving pending");
            shell.ApplyLanguageRecordingState(
                RecordingReviewState.Stopping,
                commandPending: true);
            Require(!shell.LanguageEntryVisible &&
                    !shell.RestartPromptVisible,
                "transition/command-pending presentation also hides language UI");
            shell.ApplyLanguageRecordingState(
                RecordingReviewState.Idle,
                commandPending: false);
            Require(shell.LanguageEntryVisible &&
                    shell.RestartPromptVisible &&
                    shell.HasPendingUiLanguage,
                "H: return to Idle restores entry and the preserved pending prompt");

            bool restartRequested = false;
            shell.RestartNowRequested += (_, _) => restartRequested = true;
            Require(shell.RequestRestartNow() && restartRequested,
                "Restart Now is available only after returning to ready UI");

            const string executable = @"C:\Product\XiaobaiRecorder.exe";
            const string workingDirectory = @"C:\Product";
            System.Diagnostics.ProcessStartInfo relaunch =
                UiRestartContract.CreateRelaunchStartInfo(
                    executable,
                    workingDirectory);
            Require(relaunch.FileName == executable &&
                    relaunch.WorkingDirectory == workingDirectory &&
                    relaunch.UseShellExecute &&
                    string.IsNullOrEmpty(relaunch.Arguments) &&
                    string.IsNullOrEmpty(relaunch.Verb),
                "Restart Now relaunches the same executable with the normal shell contract");

            ProductState englishProductState = new(new ProductSettingsStore(
                Path.Combine(directory, "english-product-settings.json"),
                string.Empty));
            Require(englishProductState.TrySetUiLanguage(UiLanguage.English),
                "symmetric fixture starts persisted in English");
            UiLanguage.Apply(UiLanguage.English);
            StructuralShellView englishShell = CreatePersistingShell(
                englishProductState,
                UiLanguage.English,
                UiLanguage.English,
                out _);
            Require(englishShell.RequestUiLanguageSelection(
                    UiLanguage.SimplifiedChinese) &&
                    englishShell.HasPendingUiLanguage &&
                    englishProductState.Current.UiLanguage ==
                        UiLanguage.SimplifiedChinese &&
                    CultureInfo.CurrentUICulture.Name == "en" &&
                    englishShell.RestartPromptVisible,
                "English to zh-CN has the same persist-only pending behavior");
            Require(englishShell.DeferRestartPrompt() &&
                    !englishShell.RestartPromptVisible,
                "symmetric English fixture defers the pending prompt");
            Require(englishShell.RequestUiLanguageSelection(
                    UiLanguage.SimplifiedChinese) &&
                    englishShell.RestartPromptVisible &&
                    englishShell.HasPendingUiLanguage,
                "C: clicking the same pending zh-CN option after Later reopens prompt");
            Require(englishShell.RequestUiLanguageSelection(
                    UiLanguage.English) &&
                    !englishShell.HasPendingUiLanguage &&
                    !englishShell.RestartPromptVisible &&
                    englishProductState.Current.UiLanguage ==
                        UiLanguage.English,
                "D: clicking active English cancels pending and hides prompt");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static StructuralShellView CreatePersistingShell(
        ProductState productState,
        string activeLanguage,
        string persistedLanguage,
        out RequestCounter persistenceRequests)
    {
        RequestCounter requests = new();
        StructuralShellView shell = new(new EmptyFrameSource());
        shell.ConfigureUiLanguage(activeLanguage, persistedLanguage);
        shell.UiLanguageRequested += (_, args) =>
        {
            requests.Count++;
            args.Persisted = productState.TrySetUiLanguage(args.Language);
        };
        persistenceRequests = requests;
        return shell;
    }

    private sealed class RequestCounter
    {
        public int Count { get; set; }
    }

    private static RecordingPanelPresentationState CreateRecordingPresentation(
        RecordingReviewState state) => RecordingPanelPresentationState.Create(
            RecordingReviewSnapshot.Idle with { State = state },
            commandPending: false,
            canonicalOutputRoot: string.Empty,
            workingPath: string.Empty,
            plannedFinalPath: string.Empty,
            publishedPath: string.Empty,
            trayInFrame: false,
            captureAffinityResult: string.Empty,
            completionSummaryVisible: false,
            publishedFileExists: false,
            publishedDirectoryExists: false);

    private static void FormalShellSmoke()
    {
        foreach (string language in new[]
        {
            UiLanguage.SimplifiedChinese,
            UiLanguage.English,
        })
        {
            UiLanguage.Apply(language);
            StructuralShellView shell = new(new EmptyFrameSource());
            shell.ConfigureUiLanguage(language, language);
            shell.ApplyLanguageRecordingState(
                RecordingReviewState.Idle,
                commandPending: false);
            Require(shell.LanguageEntryVisible &&
                    shell.LanguageEntryUsesHomeOverlay &&
                    !shell.SettingsVisible,
                $"{language} formal Home shell constructs without Settings");

            CapturePanelView capture = shell.DockedCaptureView;
            global::Avalonia.Controls.TextBlock systemLabel =
                global::Avalonia.Controls.NameScopeExtensions.Find<
                    global::Avalonia.Controls.TextBlock>(
                    capture,
                    "SystemAudioLabel") ??
                throw new InvalidOperationException("System label not found.");
            global::Avalonia.Controls.Grid systemMeter =
                global::Avalonia.Controls.NameScopeExtensions.Find<
                    global::Avalonia.Controls.Grid>(
                    capture,
                    "SystemAudioMeter") ??
                throw new InvalidOperationException("System meter not found.");
            global::Avalonia.Controls.Grid englishSystemLayout =
                global::Avalonia.Controls.NameScopeExtensions.Find<
                    global::Avalonia.Controls.Grid>(
                    capture,
                    "EnglishSystemAudioLayout") ??
                throw new InvalidOperationException(
                    "English System-row layout not found.");

            if (language == UiLanguage.SimplifiedChinese)
            {
                Require(!englishSystemLayout.IsVisible &&
                        ReferenceEquals(systemLabel.Parent, systemMeter.Parent) &&
                        global::Avalonia.Controls.Grid.GetColumnSpan(systemLabel) == 2 &&
                        systemMeter.Margin ==
                            new global::Avalonia.Thickness(12, 0, 6, 0),
                    "zh-CN keeps the MIC-GOOD System-row presentation");
                Require(
                    GetNamedIconMargin(
                        shell.DockedCaptureView,
                        "CaptureTitleIcon") ==
                        new global::Avalonia.Thickness(60, -2, 0, 0) &&
                    GetNamedIconMargin(
                        shell.DockedStage3DView,
                        "StageTitleIcon") ==
                        new global::Avalonia.Thickness(49.8167, -2, 0, 0) &&
                    GetNamedIconMargin(
                        shell.DockedRecordingView,
                        "RecordingTitleIcon") ==
                        new global::Avalonia.Thickness(73.49, -2, 0, 0),
                    "zh-CN title icon margins remain exactly unchanged");
                continue;
            }

            global::Avalonia.Controls.Button systemRefresh =
                global::Avalonia.Controls.NameScopeExtensions.Find<
                    global::Avalonia.Controls.Button>(
                    capture,
                    "SystemAudioRefreshButton") ??
                throw new InvalidOperationException(
                    "System refresh button not found.");
            global::Avalonia.Controls.Window layoutWindow = new()
            {
                Width = 910,
                Height = 635,
                Content = shell,
                ShowInTaskbar = false,
            };
            layoutWindow.Show();
            global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            global::Avalonia.Controls.TextBlock recordingTitle =
                global::Avalonia.Controls.NameScopeExtensions.Find<
                    global::Avalonia.Controls.TextBlock>(
                    shell.DockedRecordingView,
                    "RecordingSectionTitleText") ??
                throw new InvalidOperationException(
                    "Recording section title not found.");
            recordingTitle.Text = Strings.RecordingTitle;
            global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            (string Name, string Text, double TextWidth, double MarginLeft,
                double Gap)[]
                titleSpacing =
            [
                MeasureTitleSpacing("Recording", shell.DockedCaptureView),
                MeasureTitleSpacing("Director", shell.DockedDirectorView),
                MeasureTitleSpacing("3D View", shell.DockedStage3DView),
                MeasureTitleSpacing("Record", shell.DockedRecordingView),
            ];
            Console.WriteLine(
                "TITLE_SPACING_AUDIT " +
                string.Join(
                    " | ",
                    titleSpacing.Select(item =>
                        $"{item.Name}:text={item.Text}," +
                        $"textWidth={item.TextWidth:F4}," +
                        $"marginLeft={item.MarginLeft:F4}," +
                        $"gap={item.Gap:F4}")));
            (string Name, string Text, double TextWidth, double MarginLeft,
                double Gap) directorSpacing = titleSpacing.Single(
                    item => item.Name == "Director");
            double directorSemanticGap =
                directorSpacing.MarginLeft - directorSpacing.TextWidth;
            Require(
                Math.Abs(directorSpacing.MarginLeft - 60.0) < 0.0001 &&
                directorSpacing.Gap > 0.0 &&
                titleSpacing.All(item =>
                    Math.Abs(
                        item.MarginLeft - item.TextWidth -
                        directorSemanticGap) < 0.001) &&
                titleSpacing.All(item =>
                    Math.Abs(item.Gap - directorSpacing.Gap) < 0.25),
                "English Recording/Director/3D View/Record title-icon gaps " +
                "match the untouched Director reference within layout rounding");

            global::Avalonia.Point labelRight = systemLabel.TranslatePoint(
                    new global::Avalonia.Point(systemLabel.Bounds.Width, 0),
                    capture) ??
                throw new InvalidOperationException(
                    "System label position unavailable.");
            global::Avalonia.Point meterLeft = systemMeter.TranslatePoint(
                    default,
                    capture) ??
                throw new InvalidOperationException(
                    "System meter position unavailable.");
            global::Avalonia.Point meterRight = systemMeter.TranslatePoint(
                    new global::Avalonia.Point(systemMeter.Bounds.Width, 0),
                    capture) ??
                throw new InvalidOperationException(
                    "System meter right position unavailable.");
            global::Avalonia.Point refreshLeft = systemRefresh.TranslatePoint(
                    default,
                    capture) ??
                throw new InvalidOperationException(
                    "System refresh position unavailable.");
            global::Avalonia.Controls.Border[] segments = systemMeter.Children
                .OfType<global::Avalonia.Controls.Border>()
                .ToArray();
            double[] segmentGaps = segments
                .Zip(segments.Skip(1),
                    (left, right) => right.Bounds.Left - left.Bounds.Right)
                .ToArray();

            Require(englishSystemLayout.IsVisible &&
                    systemLabel.Text == "PC Audio" &&
                    meterLeft.X >= labelRight.X + 8.0 - 0.01 &&
                    meterRight.X <= refreshLeft.X - 6.0 + 0.01,
                "English PC Audio/System meter/refresh actual bounds do not overlap; " +
                $"labelRight={labelRight.X:F2}; meter={meterLeft.X:F2}-" +
                $"{meterRight.X:F2}; refreshLeft={refreshLeft.X:F2}");
            Require(segments.Length == 12 &&
                    segments.All(segment => segment.Bounds.Width > 0.0) &&
                    segments.Max(segment => segment.Bounds.Width) -
                        segments.Min(segment => segment.Bounds.Width) <= 1.01 &&
                    segmentGaps.All(gap => gap > 0.0) &&
                    segmentGaps.Max() - segmentGaps.Min() < 0.01,
                "English retains every existing System meter segment, " +
                "evenly and continuously");
            layoutWindow.Close();
        }
    }

    private static (string Name, string Text, double TextWidth,
        double MarginLeft, double Gap)
        MeasureTitleSpacing(
            string name,
            global::Avalonia.Controls.Control panel)
    {
        global::Avalonia.Controls.TextBlock title = panel
            .GetVisualDescendants()
            .OfType<global::Avalonia.Controls.TextBlock>()
            .First(control =>
                control.IsVisible &&
                control.Classes.Contains("skill-section-title") &&
                control.Bounds.Width > 0.0);
        global::Avalonia.Controls.Viewbox icon = panel
            .GetVisualDescendants()
            .OfType<global::Avalonia.Controls.Viewbox>()
            .First(control =>
                !control.IsHitTestVisible &&
                Math.Abs(control.Width - 24.0) < 0.01 &&
                Math.Abs(control.Height - 22.0) < 0.01);
        double textWidth = title.TextLayout.Width;
        global::Avalonia.Point titleRight = title.TranslatePoint(
                new global::Avalonia.Point(textWidth, 0),
                panel) ??
            throw new InvalidOperationException(
                $"{name} title position unavailable.");
        global::Avalonia.Point iconLeft = icon.TranslatePoint(
                default,
                panel) ??
            throw new InvalidOperationException(
                $"{name} icon position unavailable.");
        return (
            name,
            title.Text ?? string.Empty,
            textWidth,
            icon.Margin.Left,
            iconLeft.X - titleRight.X);
    }

    private static global::Avalonia.Thickness GetNamedIconMargin(
        global::Avalonia.Controls.Control panel,
        string name) =>
        (global::Avalonia.Controls.NameScopeExtensions.Find<
            global::Avalonia.Controls.Viewbox>(panel, name) ??
            throw new InvalidOperationException($"{name} not found."))
        .Margin;

    private sealed class EmptyFrameSource : IGpuPreviewFrameSource
    {
        public bool SetPresentationSize(uint pixelWidth, uint pixelHeight) => true;
        public bool TryGetLatestFrame(out GpuPreviewFrame frame)
        {
            frame = default;
            return false;
        }
        public bool IsCurrentStream(ulong streamGeneration) => false;
    }

    private static string NewTemporaryDirectory(string suffix)
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            "xbpreview-localization-tests",
            $"{suffix}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static string RemoveJsonProperty(string json, string property)
    {
        string[] lines = json.Split(["\r\n", "\n"],
            StringSplitOptions.None);
        int index = Array.FindIndex(lines, line =>
            line.Contains($"\"{property}\"", StringComparison.Ordinal));
        Require(index >= 0, $"fixture contains {property}");
        return string.Join(Environment.NewLine,
            lines.Where((_, candidate) => candidate != index));
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(
                $"Localization contract failed: {message}");
        }
    }
}
