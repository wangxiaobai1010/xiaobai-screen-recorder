using System.Globalization;
using XbPreview.Avalonia.Localization;

namespace XbPreview.Host;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ManagedStartupDiagnostics.Write(new ManagedStartupDiagnosticEvent
        {
            ManagedStage = "Program.MainEntered",
            LifecycleState = PreviewLifecycleState.NotInitialized.ToString(),
            Result = "begin",
        });
        ProductSettings startupSettings = new ProductSettingsStore().Load();
        string uiLanguage = UiLanguage.Resolve(
            startupSettings.UiLanguage,
            CultureInfo.InstalledUICulture);
        UiLanguage.Apply(uiLanguage);
        ApplicationConfiguration.Initialize();

        if (args.Length == 0 || args.Contains(
            "--module1-capture-review",
            StringComparer.OrdinalIgnoreCase))
        {
            FormalAvaloniaHomeHost.SetupAvalonia();
            using StructuralAvaloniaShellHost module1CaptureHost = new();
            Application.Run(module1CaptureHost);
            return;
        }

        int structuralShellGateIndex = Array.FindIndex(
            args,
            argument => string.Equals(
                argument,
                "--skill-ui-structural-gate",
                StringComparison.OrdinalIgnoreCase));
        if (structuralShellGateIndex >= 0)
        {
            StructuralShellPerformanceGateRequest request =
                StructuralShellPerformanceGateRequest.Parse(
                    args,
                    structuralShellGateIndex);
            FormalAvaloniaHomeHost.SetupAvalonia();
            using StructuralAvaloniaShellHost structuralShellHost =
                new(request);
            Application.Run(structuralShellHost);
            return;
        }

        if (args.Contains(
            "--formal-home-window-fixture",
            StringComparer.OrdinalIgnoreCase))
        {
            using Form fixture = new()
            {
                Text = FormalHomeIntegrationGate.WindowFixtureTitle,
                ClientSize = new System.Drawing.Size(480, 270),
                StartPosition = FormStartPosition.Manual,
                Location = new System.Drawing.Point(32, 32),
            };
            Application.Run(fixture);
            return;
        }

        bool formalAvaloniaHomeReview = args.Contains(
            "--formal-avalonia-home-review",
            StringComparer.OrdinalIgnoreCase);
        int formalHomeGateIndex = Array.FindIndex(
            args,
            argument => string.Equals(
                argument,
                "--formal-avalonia-home-gate",
                StringComparison.OrdinalIgnoreCase));
        if (formalAvaloniaHomeReview || formalHomeGateIndex >= 0)
        {
            FormalHomeIntegrationGateRequest? request =
                formalHomeGateIndex >= 0
                    ? FormalHomeIntegrationGateRequest.Parse(
                        args,
                        formalHomeGateIndex)
                    : null;
            FormalAvaloniaHomeHost.SetupAvalonia();
            using FormalAvaloniaHomeHost formalAvaloniaHomeHost = new(request);
            Application.Run(formalAvaloniaHomeHost);
            return;
        }

        bool legacyHost = args.Contains(
            "--legacy-host",
            StringComparer.OrdinalIgnoreCase) ||
            args.Contains(
                "--director-lite",
                StringComparer.OrdinalIgnoreCase) ||
            args.Contains(
                "--package-smoke",
                StringComparer.OrdinalIgnoreCase);
        if (!legacyHost)
        {
            bool formalWindowSelectorReview = args.Contains(
                "--formal-window-selector-review",
                StringComparer.OrdinalIgnoreCase);
            bool formalMicSelectorReview = args.Contains(
                "--formal-mic-selector-review",
                StringComparer.OrdinalIgnoreCase);
            bool formalMicNoDeviceReview = args.Contains(
                "--formal-mic-no-device-review",
                StringComparer.OrdinalIgnoreCase);
            bool formalMicDeviceReturnReview = args.Contains(
                "--formal-mic-device-return-review",
                StringComparer.OrdinalIgnoreCase);
            bool formalBackgroundSelectorReview = args.Contains(
                "--formal-background-selector-review",
                StringComparer.OrdinalIgnoreCase);
            bool formalSettingsReview = args.Contains(
                "--formal-settings-review",
                StringComparer.OrdinalIgnoreCase);
            using FormalUiV4Form formalUiShell = new(
                formalWindowSelectorReview,
                formalMicSelectorReview,
                formalMicNoDeviceReview,
                formalMicDeviceReturnReview,
                formalBackgroundSelectorReview,
                formalSettingsReview);
            Application.Run(formalUiShell);
            return;
        }

        bool directorLite = args.Contains(
            "--director-lite",
            StringComparer.OrdinalIgnoreCase);
        DirectorFocusStrength directorFocusStrength = args.Contains(
            "--director-focus-strong",
            StringComparer.OrdinalIgnoreCase)
                ? DirectorFocusStrength.Strong
                : DirectorFocusStrength.Soft;
        bool packageSmoke = args.Contains(
            "--package-smoke",
            StringComparer.OrdinalIgnoreCase);
        using MainForm mainForm = new(directorLite, directorFocusStrength);
        if (packageSmoke)
        {
            mainForm.Shown += async (_, _) =>
            {
                await Task.Delay(TimeSpan.FromSeconds(3));
                mainForm.Close();
            };
        }
        Application.Run(mainForm);
    }
}
