using System.Text.Json;
using XbPreview.Avalonia.Contracts;
using XbPreview.Avalonia.Controls;

namespace XbPreview.Host;

internal sealed record FormalHomeIntegrationGateRequest(
    string Scenario,
    string EvidencePath,
    string OutputRoot)
{
    internal string RunDirectory =>
        Path.GetDirectoryName(EvidencePath) ??
            throw new InvalidOperationException(
                "Gate evidence path has no parent directory.");

    internal string DiagnosticDirectory =>
        Path.Combine(RunDirectory, "diagnostics");

    internal string SettingsPath =>
        Path.Combine(RunDirectory, "product-settings.json");

    internal static FormalHomeIntegrationGateRequest Parse(
        string[] args,
        int markerIndex)
    {
        if (markerIndex + 3 >= args.Length)
        {
            throw new ArgumentException(
                "--formal-avalonia-home-gate requires scenario, evidence path, " +
                "and output root.");
        }
        string scenario = args[markerIndex + 1].Trim().ToLowerInvariant();
        if (scenario is not (
            "controls" or "idle-close" or
            "recording-close" or "paused-close"))
        {
            throw new ArgumentException($"Unknown formal Home gate: {scenario}.");
        }
        string evidence = Path.GetFullPath(args[markerIndex + 2]);
        string output = Path.GetFullPath(args[markerIndex + 3]);
        Directory.CreateDirectory(
            Path.GetDirectoryName(evidence) ??
                throw new ArgumentException(
                    "Evidence path has no parent directory."));
        Directory.CreateDirectory(output);
        return new FormalHomeIntegrationGateRequest(
            scenario,
            evidence,
            output);
    }
}

internal sealed class FormalHomeIntegrationGate
{
    internal const string WindowFixtureTitle =
        "Legacy Review Formal Home Real Window Fixture";
    private readonly FormalHomeIntegrationGateRequest _request;
    private readonly Dictionary<string, object?> _facts =
        new(StringComparer.Ordinal);
    private readonly List<string> _failures = [];

    internal FormalHomeIntegrationGate(
        FormalHomeIntegrationGateRequest request)
    {
        _request = request ?? throw new ArgumentNullException(nameof(request));
        _facts["Scenario"] = request.Scenario;
        _facts["StartedUtc"] = DateTimeOffset.UtcNow;
    }

    internal async Task RunAsync(FormalAvaloniaHomeHost host)
    {
        try
        {
            await WaitForGpuPresentationAsync(host);
            RecordIdentities(host, "Initial");
            switch (_request.Scenario)
            {
                case "controls":
                    await RunControlsAsync(host);
                    break;
                case "idle-close":
                    Require(
                        host.RecordingAdapter.CurrentSnapshot.State ==
                            RecordingReviewState.Idle,
                        "Idle close did not begin from Idle.");
                    _facts["CloseRequestedFrom"] = "Idle";
                    break;
                case "recording-close":
                    await PrepareActiveCloseAsync(host, pause: false);
                    break;
                case "paused-close":
                    await PrepareActiveCloseAsync(host, pause: true);
                    break;
            }
        }
        catch (Exception error)
        {
            RecordFailure(error);
        }
        finally
        {
            host.Close();
        }
    }

    internal void RecordFailure(Exception error)
    {
        string value = $"{error.GetType().Name}: {error.Message}";
        if (!_failures.Contains(value, StringComparer.Ordinal))
        {
            _failures.Add(value);
        }
        _facts["LastError"] = error.ToString();
    }

    internal void RecordClose(
        ManagedRecordingSnapshot before,
        ManagedRecordingSnapshot after,
        int resumeCommandCount,
        ulong lastPresentedFrame)
    {
        _facts["CloseBeforeState"] = before.State.ToString();
        _facts["CloseAfterState"] = after.State.ToString();
        _facts["CloseResumeCommandCount"] = resumeCommandCount;
        _facts["CloseFinalizeAttempted"] = after.FinalizeAttempted;
        _facts["CloseFinalizeCount"] = after.FinalizeCount;
        _facts["CloseReadyToPublish"] = after.ReadyToPublish;
        _facts["ClosePublished"] = after.Published;
        _facts["CloseValidationAttempted"] = after.ValidationAttempted;
        _facts["ClosePublishedPath"] = after.PublishedPath;
        _facts["CloseLastPresentedFrame"] = lastPresentedFrame;

        switch (_request.Scenario)
        {
            case "idle-close":
                Check(
                    "IdleClose",
                    before.State == ManagedRecordingState.Idle &&
                    after.State == ManagedRecordingState.Idle,
                    $"{before.State} -> {after.State}");
                break;
            case "recording-close":
                CheckClosePublish(
                    "RecordingClose",
                    before,
                    after,
                    resumeCommandCount,
                    requirePaused: false);
                break;
            case "paused-close":
                CheckClosePublish(
                    "PausedClose",
                    before,
                    after,
                    resumeCommandCount,
                    requirePaused: true);
                break;
            case "controls":
                Check(
                    "CompletedClose",
                    before.State == ManagedRecordingState.Completed &&
                    after.State == ManagedRecordingState.Completed,
                    $"{before.State} -> {after.State}");
                break;
        }
    }

    internal void WriteEvidence()
    {
        _facts["CompletedUtc"] = DateTimeOffset.UtcNow;
        _facts["Failures"] = _failures.ToArray();
        _facts["Status"] = _failures.Count == 0 ? "PASS" : "FAIL";
        Directory.CreateDirectory(_request.RunDirectory);
        File.WriteAllText(
            _request.EvidencePath,
            JsonSerializer.Serialize(
                _facts,
                new JsonSerializerOptions { WriteIndented = true }));
    }

    private async Task RunControlsAsync(FormalAvaloniaHomeHost host)
    {
        ProductionHomeAdapter product = host.ProductAdapter;
        ProductionRecordingAdapter recording = host.RecordingAdapter;
        object initialGpuPreview = host.HomeView.PreviewControl;
        object initialLifecycle = host.Lifecycle;
        object initialNative = host.NativeSession;
        object initialRecording = host.RecordingController;

        ulong priorStream = host.NativeSession.GpuStreamGeneration;
        await RequireSuccess(product.SetCaptureTargetFullScreenAsync());
        await WaitForNewGpuStreamPresentationAsync(
            host,
            priorStream,
            "Fullscreen capture target");
        ProductReviewCommandResult refreshed = product.RefreshDevices();
        Require(refreshed.Succeeded, refreshed.Detail);
        ProductReviewWindowChoice? window = product.CurrentSnapshot.Windows
            .FirstOrDefault(choice => string.Equals(
                choice.Title,
                WindowFixtureTitle,
                StringComparison.Ordinal)) ??
            product.CurrentSnapshot.Windows.FirstOrDefault();
        Require(window is not null, "Real window enumeration returned no item.");
        ProductReviewWindowChoice selectedWindow = window!;
        _facts["RealWindowCount"] = product.CurrentSnapshot.Windows.Count;
        _facts["SelectedRealWindow"] = selectedWindow.Title;
        _facts["SelectedRealWindowProcess"] = selectedWindow.ProcessName;
        priorStream = host.NativeSession.GpuStreamGeneration;
        await RequireSuccess(product.SetCaptureTargetWindowAsync(
            selectedWindow.Id));
        await WaitForNewGpuStreamPresentationAsync(
            host,
            priorStream,
            "Window capture target");
        Require(
            product.CurrentSnapshot.CaptureTargetMode ==
            ProductReviewCaptureTargetMode.Window,
            "Window target was not reflected by Product state.");
        priorStream = host.NativeSession.GpuStreamGeneration;
        await RequireSuccess(product.SetCaptureTargetFullScreenAsync());
        await WaitForNewGpuStreamPresentationAsync(
            host,
            priorStream,
            "Fullscreen capture target restore");
        Check("CaptureTargetRoundtrip", true, "Fullscreen -> Window -> Fullscreen");
        Check("CaptureTargetPresentationRecovery", true,
            $"three managed GPU stream transitions; current=" +
            $"{host.NativeSession.GpuStreamGeneration}");

        ProductReviewSnapshot devices = product.CurrentSnapshot;
        Require(devices.Microphones.Count > 0,
            "Microphone enumeration did not expose Windows default status.");
        _facts["MicrophoneChoiceCount"] = devices.Microphones.Count;
        _facts["MicrophoneAvailable"] = devices.SelectedMicrophoneAvailable;
        await RequireSuccess(product.SetMicrophoneSelectionAsync(
            devices.Microphones[0].Id));
        await RequireSuccess(product.SetMicrophoneEnabledAsync(true));
        await RequireSuccess(product.SetMicrophoneEnabledAsync(false));
        Check("Microphone", true,
            devices.SelectedMicrophoneAvailable ? "available" : "unavailable reported");

        await RequireSuccess(product.SetSystemAudioEnabledAsync(true));
        await RequireSuccess(product.SetSystemAudioEnabledAsync(false));
        await RequireSuccess(product.SetSystemAudioEnabledAsync(true));
        Check("SystemAudio", product.CurrentSnapshot.SystemAudioEnabled,
            "ON -> OFF -> ON");

        await RequireSuccess(product.SetCursorVisibleAsync(true));
        await RequireSuccess(product.SetCursorVisibleAsync(false));
        await RequireSuccess(product.SetCursorVisibleAsync(true));
        Check("Cursor", product.CurrentSnapshot.CursorVisible,
            "ON -> OFF -> ON");

        await RequireSuccess(product.SetAutoDirectorEnabledAsync(false));
        await RequireSuccess(product.ExecuteManualZoomAsync(
            ProductReviewManualZoom.Standard));
        Require(
            product.CurrentSnapshot.ManualZoom ==
                ProductReviewManualZoom.Standard,
            "Manual 1.6x was not applied.");
        await RequireSuccess(product.ExecuteManualZoomAsync(
            ProductReviewManualZoom.Strong));
        Require(
            product.CurrentSnapshot.ManualZoom == ProductReviewManualZoom.Strong,
            "Manual 2.0x was not applied.");
        await RequireSuccess(product.ExecuteManualZoomAsync(
            ProductReviewManualZoom.Strong));
        Require(
            product.CurrentSnapshot.ManualZoom == ProductReviewManualZoom.Wide,
            "Manual zoom did not return to Wide.");
        Check("ManualZoom", true, "1.6x -> 2.0x -> 1.0x");

        await RequireSuccess(product.SetHotkeysEnabledAsync(true));
        await RequireSuccess(product.SetHotkeysEnabledAsync(false));
        await RequireSuccess(product.SetHotkeysEnabledAsync(true));
        Check(
            "Hotkeys",
            product.CurrentSnapshot.HotkeysEnabled,
            $"enabled -> disabled -> {product.CurrentSnapshot.HotkeyState}");

        await RequireSuccess(product.SetAutoDirectorEnabledAsync(true));
        Require(
            !product.CurrentSnapshot.ManualCommandsEnabled,
            "Auto Director did not gate manual commands.");
        ProductReviewCommandResult gated = await product.ExecuteManualZoomAsync(
            ProductReviewManualZoom.Standard);
        Require(!gated.Succeeded,
            "Manual zoom was accepted while Auto Director owned the camera.");
        await RequireSuccess(product.SetAutoDirectorEnabledAsync(false));
        Require(product.CurrentSnapshot.ManualCommandsEnabled,
            "Manual commands did not recover after Auto Director was disabled.");
        Check("AutoDirector", true, "OFF -> ON (manual gated) -> OFF");

        await RequireSuccess(product.SetStagePoseAsync(
            ProductReviewStageOrientation.Front,
            ProductReviewStageLevel.Level2));
        await RequireSuccess(product.SetStagePoseAsync(
            ProductReviewStageOrientation.Left,
            ProductReviewStageLevel.Level1));
        await RequireSuccess(product.SetStagePoseAsync(
            ProductReviewStageOrientation.Right,
            ProductReviewStageLevel.Level3));
        await RequireSuccess(product.SetStagePoseAsync(
            ProductReviewStageOrientation.Front,
            ProductReviewStageLevel.Level2));
        Check("Stage3D", true, "FRONT2 -> LEFT1 -> RIGHT3 -> FRONT2");

        foreach (ProductReviewBackgroundPreset preset in new[]
        {
            ProductReviewBackgroundPreset.Warm,
            ProductReviewBackgroundPreset.Fantasy01,
            ProductReviewBackgroundPreset.Fantasy001,
            ProductReviewBackgroundPreset.Warm,
        })
        {
            await RequireSuccess(product.SetBackgroundPresetAsync(preset));
        }
        string image = Directory.GetFiles(
                Path.Combine(AppContext.BaseDirectory, "assets"),
                "*.png")
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault() ??
            throw new FileNotFoundException(
                "Packaged custom background fixture was unavailable.");
        await RequireSuccess(product.SetCustomBackgroundAsync(image));
        Require(product.CurrentSnapshot.CustomBackgroundSelected,
            "Valid custom background was not selected.");
        await RequireSuccess(product.SetBackgroundPresetAsync(
            ProductReviewBackgroundPreset.Warm));
        Check("Backgrounds", true,
            "Warm -> Fantasy01 -> Fantasy001 -> Warm; custom -> preset");

        await RequireSuccess(product.SetOutputRootAsync(_request.OutputRoot));
        Require(string.Equals(
            product.CurrentSnapshot.OutputRoot,
            _request.OutputRoot,
            StringComparison.OrdinalIgnoreCase),
            "Custom output root was not reflected.");
        await RequireSuccess(product.SetOutputRootAsync(null));
        Require(string.IsNullOrEmpty(product.CurrentSnapshot.OutputRoot),
            "Default output root was not restored.");
        Check("OutputRoot", true, "custom -> default");

        await RunSettingsRoundtripAsync(host);
        await RequireSuccess(product.SetOutputRootAsync(_request.OutputRoot));

        ulong recordingStream = host.NativeSession.GpuStreamGeneration;
        RecordingReviewSnapshot first = await RunRecordingAsync(recording, 1);
        RecordingReviewSnapshot second = await RunRecordingAsync(recording, 2);
        Require(!string.Equals(
            first.SessionId,
            second.SessionId,
            StringComparison.Ordinal),
            "Recording restart reused a session id.");
        _facts["Recording1"] = RecordingFacts(first);
        _facts["Recording2"] = RecordingFacts(second);
        Require(
            ReferenceEquals(initialGpuPreview, host.HomeView.PreviewControl) &&
            ReferenceEquals(initialLifecycle, host.Lifecycle) &&
            ReferenceEquals(initialNative, host.NativeSession) &&
            ReferenceEquals(initialRecording, host.RecordingController) &&
            host.NativeSession.GpuStreamGeneration == recordingStream,
            "Recording restart replaced or restarted the Preview engine.");
        Check("CompletedRestart", true,
            "Start/Stop/Completed twice on one controller");
        Check("SamePreviewNativeEngine", true,
            $"native={FormalAvaloniaHomeHost.Identity(host.NativeSession)}; " +
            $"stream={recordingStream}");

        string completedSession = recording.CurrentSnapshot.SessionId;
        host.ShowSettings();
        await Task.Delay(200);
        host.PerformSettingsBack();
        PresentationCheckpoint completedAfterReturn =
            CapturePresentationCheckpoint(host);
        await WaitForGpuPresentationAdvanceAsync(
            host,
            completedAfterReturn,
            "Completed Settings roundtrip");
        Require(
            recording.CurrentSnapshot.State == RecordingReviewState.Completed &&
            string.Equals(
                recording.CurrentSnapshot.SessionId,
                completedSession,
                StringComparison.Ordinal),
            "Completed state was lost across Settings roundtrip.");
        Check("CompletedSettingsRoundtrip", true, "Completed preserved");

        await RunWindowStateSpotAsync(host);
        RecordIdentities(host, "Final");
    }

    private async Task RunSettingsRoundtripAsync(FormalAvaloniaHomeHost host)
    {
        object avaloniaHost = host.AvaloniaHost;
        object home = host.HomeView;
        object gpuPreview = host.HomeView.PreviewControl;
        object lifecycle = host.Lifecycle;
        object native = host.NativeSession;
        object recording = host.RecordingController;
        ProductState productState = host.ProductState;

        PresentationCheckpoint before = CapturePresentationCheckpoint(host);
        GpuPreviewPresentationDiagnostics beforeDiagnostics =
            host.HomeView.PreviewControl.PresentationDiagnostics;
        _facts["SettingsBefore"] = PresentationFacts(
            host,
            beforeDiagnostics);
        host.ShowSettings();
        Require(host.SettingsVisible, "Settings did not become visible.");
        await Task.Delay(200);
        _facts["SettingsWhile1"] = PresentationFacts(
            host,
            beforeDiagnostics);
        host.PerformSettingsBack();
        Require(!host.SettingsVisible, "Settings back did not restore Home.");
        RecordIdentities(host, "SettingsAfterReturn1");
        PresentationCheckpoint afterReturn1 =
            CapturePresentationCheckpoint(host);
        _facts["SettingsAfterReturn1Immediate"] = PresentationFacts(
            host,
            beforeDiagnostics);
        try
        {
            await WaitForGpuPresentationAdvanceAsync(
                host,
                afterReturn1,
                "Settings roundtrip #1");
        }
        finally
        {
            _facts["SettingsAfterReturn1Settled"] = PresentationFacts(
                host,
                beforeDiagnostics);
        }
        GpuPreviewPresentationDiagnostics secondBaseline =
            host.HomeView.PreviewControl.PresentationDiagnostics;
        host.ShowSettings();
        Require(host.SettingsVisible,
            "Second Settings navigation did not become visible.");
        await Task.Delay(200);
        _facts["SettingsWhile2"] = PresentationFacts(
            host,
            secondBaseline);
        host.PerformSettingsBack();
        Require(!host.SettingsVisible, "Second Settings back failed.");
        PresentationCheckpoint afterReturn2 =
            CapturePresentationCheckpoint(host);
        try
        {
            await WaitForGpuPresentationAdvanceAsync(
                host,
                afterReturn2,
                "Settings roundtrip #2");
        }
        finally
        {
            _facts["SettingsAfterReturn2Settled"] = PresentationFacts(
                host,
                secondBaseline);
        }

        Require(
            ReferenceEquals(avaloniaHost, host.AvaloniaHost) &&
            ReferenceEquals(home, host.HomeView) &&
            ReferenceEquals(gpuPreview, host.HomeView.PreviewControl) &&
            ReferenceEquals(lifecycle, host.Lifecycle) &&
            ReferenceEquals(native, host.NativeSession) &&
            ReferenceEquals(recording, host.RecordingController) &&
            host.NativeSession.GpuStreamGeneration ==
                before.NativeStreamGeneration &&
            host.HomeView.PreviewControl.PresentationDiagnostics
                .StreamTransitions == beforeDiagnostics.StreamTransitions &&
            !host.HomeView.PreviewControl.PresentationDiagnostics
                .ShutdownStarted,
            "A Settings roundtrip replaced a host-owned production object.");

        await RequireSuccess(host.ProductAdapter.SetStagePoseAsync(
            ProductReviewStageOrientation.Left,
            ProductReviewStageLevel.Level1));
        Require(
            ReferenceEquals(productState, host.ProductAdapter.ProductState) &&
            productState.Current.StageOrientation ==
                ProductStageOrientation.Left,
            "Home and Settings do not share one ProductState owner.");
        host.ShowSettings();
        Require(
            host.SettingsVisible &&
            ReferenceEquals(productState, host.ProductAdapter.ProductState) &&
            productState.Current.StageOrientation ==
                ProductStageOrientation.Left,
            "Settings did not observe the Home-owned ProductState update.");
        Check("HomeToSettingsStateSync", true,
            "Home update reached the host-owned ProductState used by Settings");
        await RequireSuccess(host.ProductAdapter.ResetToDefaultsAsync());
        host.SettingsView.ApplyPresentationDefaults();
        Require(
            productState.Current == ProductSettings.Defaults &&
            host.SettingsView.ResetDefaultsRequested,
            "Settings Restore Defaults did not update shared ProductState.");
        Check("RestoreDefaults", true,
            "Settings reset updated the single host-owned ProductState");
        host.PerformSettingsBack();
        PresentationCheckpoint stateSyncAfterReturn =
            CapturePresentationCheckpoint(host);
        await WaitForGpuPresentationAdvanceAsync(
            host,
            stateSyncAfterReturn,
            "Settings Restore Defaults roundtrip");
        ProductReviewSnapshot reset = host.ProductAdapter.CurrentSnapshot;
        Require(
            reset.StageOrientation == ProductReviewStageOrientation.Front &&
            reset.StageLevel == ProductReviewStageLevel.Level2 &&
            reset.BackgroundPreset == ProductReviewBackgroundPreset.Warm,
            "Home did not reflect Settings Restore Defaults.");
        Check("SettingsToHomeStateSync", true,
            "Settings reset was reflected by the Home snapshot");

        Check("SettingsRoundtrip", true,
            "Home -> Settings -> Home -> Settings -> Home");
        Check("SettingsIdentity", true,
            "Avalonia host/Home/GPU/Lifecycle/Native/Recording identities " +
            "preserved; no GPU shutdown or stream restart");
        Check("SettingsStateSync", true,
            "one host-owned ProductState; Restore Defaults synchronized");
        Check("SettingsGpuPresentation", true,
            $"stream={before.NativeStreamGeneration}; native export " +
            $"{before.NativeExportGeneration} -> " +
            $"{ReadNativeGpuFrame(host)}; Avalonia presentation " +
            $"{before.AvaloniaPresentationGeneration} -> " +
            $"{host.HomeView.PreviewControl.LastPresentedFrame}; " +
            $"Avalonia={host.HomeView.PreviewControl.InteropStatus}");
    }

    private async Task RunWindowStateSpotAsync(FormalAvaloniaHomeHost host)
    {
        host.WindowState = FormWindowState.Maximized;
        PresentationCheckpoint maximizeStart =
            CapturePresentationCheckpoint(host);
        await WaitForGpuPresentationAdvanceAsync(
            host,
            maximizeStart,
            "Maximize");

        host.WindowState = FormWindowState.Normal;
        PresentationCheckpoint restoreStart =
            CapturePresentationCheckpoint(host);
        await WaitForGpuPresentationAdvanceAsync(
            host,
            restoreStart,
            "Restore Down");

        host.WindowState = FormWindowState.Minimized;
        await Task.Delay(250);
        host.WindowState = FormWindowState.Normal;
        host.Activate();
        PresentationCheckpoint taskbarRestoreStart =
            CapturePresentationCheckpoint(host);
        await WaitForGpuPresentationAdvanceAsync(
            host,
            taskbarRestoreStart,
            "Taskbar Restore");
        Check("WindowStateMaximize", true,
            $"native/Avalonia presentation > " +
            $"{maximizeStart.NativeExportGeneration}/" +
            $"{maximizeStart.AvaloniaPresentationGeneration}");
        Check("WindowStateRestore", true,
            $"native/Avalonia presentation > " +
            $"{restoreStart.NativeExportGeneration}/" +
            $"{restoreStart.AvaloniaPresentationGeneration}");
        Check("WindowStateTaskbarRestore", true,
            $"native/Avalonia presentation > " +
            $"{taskbarRestoreStart.NativeExportGeneration}/" +
            $"{taskbarRestoreStart.AvaloniaPresentationGeneration}");
        Check("ProgressiveRepaint", true,
            "none detected; native present, export, and Avalonia completion " +
            "all advanced after each settled window-state transition");
    }

    private async Task PrepareActiveCloseAsync(
        FormalAvaloniaHomeHost host,
        bool pause)
    {
        await RequireSuccess(host.ProductAdapter.SetOutputRootAsync(
            _request.OutputRoot));
        await host.RecordingAdapter.StartAsync();
        Require(
            host.RecordingAdapter.CurrentSnapshot.State ==
                RecordingReviewState.Recording,
            "Close gate did not reach Recording.");
        await Task.Delay(1500);
        if (pause)
        {
            await host.RecordingAdapter.PauseAsync();
            Require(
                host.RecordingAdapter.CurrentSnapshot.State ==
                    RecordingReviewState.Paused,
                "Paused close gate did not reach Paused.");
            _facts["CloseRequestedFrom"] = "Paused";
        }
        else
        {
            _facts["CloseRequestedFrom"] = "Recording";
        }
    }

    private static async Task<RecordingReviewSnapshot> RunRecordingAsync(
        ProductionRecordingAdapter recording,
        int number)
    {
        await recording.StartAsync();
        Require(
            recording.CurrentSnapshot.State == RecordingReviewState.Recording,
            $"Recording #{number} did not reach Recording.");
        await Task.Delay(1800);
        await recording.StopAsync();
        RecordingReviewSnapshot snapshot = recording.CurrentSnapshot;
        RequireRecordingPublish(snapshot, $"Recording #{number}");
        return snapshot;
    }

    private static void RequireRecordingPublish(
        RecordingReviewSnapshot snapshot,
        string name)
    {
        Require(
            snapshot.State == RecordingReviewState.Completed &&
            snapshot.FramesSubmitted > 0 &&
            snapshot.FinalizeAttempted && snapshot.FinalizeCount == 1 &&
            snapshot.FinalizeHResult == 0 &&
            snapshot.ReadyToPublish && snapshot.Published &&
            snapshot.PublishAttempted && snapshot.PublishHResult == 0 &&
            snapshot.ValidationAttempted && snapshot.ValidationHResult == 0 &&
            File.Exists(snapshot.OutputPath),
            $"{name} did not complete one validated Safe Publish.");
    }

    private void CheckClosePublish(
        string key,
        ManagedRecordingSnapshot before,
        ManagedRecordingSnapshot after,
        int resumeCommandCount,
        bool requirePaused)
    {
        bool startingState = requirePaused
            ? before.State == ManagedRecordingState.Paused
            : before.State == ManagedRecordingState.Recording;
        bool passed = startingState &&
            after.State == ManagedRecordingState.Completed &&
            after.FinalizeAttempted && after.FinalizeCount == 1 &&
            after.ReadyToPublish && after.Published &&
            after.ValidationAttempted &&
            File.Exists(after.PublishedPath) &&
            (!requirePaused || resumeCommandCount == 0);
        Check(
            key,
            passed,
            $"{before.State} -> {after.State}; " +
            $"finalize={after.FinalizeCount}; resumeCommands={resumeCommandCount}");
    }

    private static object RecordingFacts(RecordingReviewSnapshot snapshot) => new
    {
        snapshot.State,
        snapshot.SessionId,
        snapshot.OutputPath,
        snapshot.FramesSubmitted,
        snapshot.PauseCount,
        snapshot.FinalizeAttempted,
        snapshot.FinalizeCount,
        snapshot.FinalizeHResult,
        snapshot.ReadyToPublish,
        snapshot.Published,
        snapshot.PublishAttempted,
        snapshot.PublishHResult,
        snapshot.ValidationAttempted,
        snapshot.ValidationHResult,
        FileExists = File.Exists(snapshot.OutputPath),
    };

    private void RecordIdentities(
        FormalAvaloniaHomeHost host,
        string prefix)
    {
        _facts[$"{prefix}HostIdentity"] =
            FormalAvaloniaHomeHost.Identity(host);
        _facts[$"{prefix}AvaloniaHostIdentity"] =
            FormalAvaloniaHomeHost.Identity(host.AvaloniaHost);
        _facts[$"{prefix}HomeIdentity"] =
            FormalAvaloniaHomeHost.Identity(host.HomeView);
        _facts[$"{prefix}GpuPreviewIdentity"] =
            FormalAvaloniaHomeHost.Identity(host.HomeView.PreviewControl);
        _facts[$"{prefix}LifecycleIdentity"] =
            FormalAvaloniaHomeHost.Identity(host.Lifecycle);
        _facts[$"{prefix}NativeIdentity"] =
            FormalAvaloniaHomeHost.Identity(host.NativeSession);
        _facts[$"{prefix}RecordingIdentity"] =
            FormalAvaloniaHomeHost.Identity(host.RecordingController);
    }

    private readonly record struct PresentationCheckpoint(
        ulong NativeStreamGeneration,
        ulong NativePresentGeneration,
        ulong NativeExportGeneration,
        ulong AvaloniaStreamGeneration,
        ulong AvaloniaPresentationGeneration);

    private static async Task WaitForGpuPresentationAsync(
        FormalAvaloniaHomeHost host)
    {
        await WaitUntilAsync(
            () =>
            {
                GpuPreviewPresentationDiagnostics diagnostics =
                    host.HomeView.PreviewControl.PresentationDiagnostics;
                ulong stream = host.NativeSession.GpuStreamGeneration;
                return host.NativeSession.GpuStreamActive && stream > 0 &&
                    ReadNativePreviewFrame(host) > 0 &&
                    ReadNativeGpuFrame(host) > 0 &&
                    diagnostics.LastCompletedStreamGeneration == stream &&
                    diagnostics.LastCompletedGeneration > 0 &&
                    host.HomeView.PreviewControl.InteropStatus.StartsWith(
                    "PASS",
                    StringComparison.Ordinal);
            },
            "Avalonia GPU presentation did not start.",
            TimeSpan.FromSeconds(20));
    }

    private static Task WaitForNewGpuStreamPresentationAsync(
        FormalAvaloniaHomeHost host,
        ulong priorStream,
        string operation) => WaitUntilAsync(
            () =>
            {
                GpuPreviewPresentationDiagnostics diagnostics =
                    host.HomeView.PreviewControl.PresentationDiagnostics;
                ulong stream = host.NativeSession.GpuStreamGeneration;
                return host.NativeSession.GpuStreamActive &&
                    stream > priorStream &&
                    ReadNativePreviewFrame(host) > 0 &&
                    ReadNativeGpuFrame(host) > 0 &&
                    diagnostics.LastCompletedStreamGeneration == stream &&
                    diagnostics.LastCompletedGeneration > 0 &&
                    host.HomeView.PreviewControl.InteropStatus.StartsWith(
                    "PASS",
                    StringComparison.Ordinal);
            },
            $"GPU presentation did not recover on the new stream after " +
            $"{operation}.",
            TimeSpan.FromSeconds(10));

    private static Task WaitForGpuPresentationAdvanceAsync(
        FormalAvaloniaHomeHost host,
        PresentationCheckpoint baseline,
        string operation) => WaitUntilAsync(
            () =>
            {
                GpuPreviewPresentationDiagnostics diagnostics =
                    host.HomeView.PreviewControl.PresentationDiagnostics;
                return host.NativeSession.GpuStreamActive &&
                    host.NativeSession.GpuStreamGeneration ==
                        baseline.NativeStreamGeneration &&
                    baseline.AvaloniaStreamGeneration ==
                        baseline.NativeStreamGeneration &&
                    ReadNativePreviewFrame(host) >
                        baseline.NativePresentGeneration &&
                    ReadNativeGpuFrame(host) >
                        baseline.NativeExportGeneration &&
                    diagnostics.LastCompletedStreamGeneration ==
                        baseline.NativeStreamGeneration &&
                    diagnostics.LastCompletedGeneration >
                        baseline.AvaloniaPresentationGeneration &&
                    host.HomeView.PreviewControl.InteropStatus.StartsWith(
                        "PASS",
                        StringComparison.Ordinal);
            },
            $"GPU presentation did not continue after {operation}.",
            TimeSpan.FromSeconds(10));

    private static PresentationCheckpoint CapturePresentationCheckpoint(
        FormalAvaloniaHomeHost host)
    {
        GpuPreviewPresentationDiagnostics diagnostics =
            host.HomeView.PreviewControl.PresentationDiagnostics;
        return new PresentationCheckpoint(
            host.NativeSession.GpuStreamGeneration,
            ReadNativePreviewFrame(host),
            ReadNativeGpuFrame(host),
            diagnostics.LastCompletedStreamGeneration,
            diagnostics.LastCompletedGeneration);
    }

    private static ulong ReadNativePreviewFrame(FormalAvaloniaHomeHost host) =>
        host.NativeSession.GetStats().PresentFrameCount;

    private static ulong ReadNativeGpuFrame(FormalAvaloniaHomeHost host) =>
        host.NativeSession.TryGetGpuExportFrame(
            out NativeMethods.GpuExportFrameV1 frame)
                ? frame.FrameGeneration
                : 0;

    private static object PresentationFacts(
        FormalAvaloniaHomeHost host,
        GpuPreviewPresentationDiagnostics baseline)
    {
        GpuPreviewPresentationDiagnostics current =
            host.HomeView.PreviewControl.PresentationDiagnostics;
        ulong nativeExportGeneration = ReadNativeGpuFrame(host);
        return new
        {
            current.PresentationActive,
            current.OutstandingCompositionUpdate,
            current.PendingPresentations,
            current.HasPresentationSource,
            current.IsVisible,
            NativeStreamGeneration = host.NativeSession.GpuStreamGeneration,
            NativeStreamActive = host.NativeSession.GpuStreamActive,
            NativePresentGeneration = ReadNativePreviewFrame(host),
            NativeExportGeneration = nativeExportGeneration,
            current.LastExportStreamGeneration,
            current.LastExportGeneration,
            current.LastCompletedStreamGeneration,
            current.LastCompletedGeneration,
            current.StreamTransitions,
            current.CompositionRequests,
            current.CompositionCallbacks,
            current.CompletionCallbacks,
            NewCompositionRequests =
                current.CompositionRequests - baseline.CompositionRequests,
            NewCompositionCallbacks =
                current.CompositionCallbacks - baseline.CompositionCallbacks,
            NewCompletionCallbacks =
                current.CompletionCallbacks - baseline.CompletionCallbacks,
            ExportSlotAvailable =
                current.LastExportStreamGeneration ==
                    host.NativeSession.GpuStreamGeneration &&
                nativeExportGeneration > baseline.LastExportGeneration,
            current.ShutdownStarted,
        };
    }

    private static async Task WaitUntilAsync(
        Func<bool> predicate,
        string failure,
        TimeSpan? timeout = null)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow +
            (timeout ?? TimeSpan.FromSeconds(5));
        while (!predicate())
        {
            if (DateTimeOffset.UtcNow >= deadline)
            {
                throw new TimeoutException(failure);
            }
            await Task.Delay(25);
        }
    }

    private static async Task RequireSuccess(
        Task<ProductReviewCommandResult> command)
    {
        ProductReviewCommandResult result = await command;
        Require(result.Succeeded, result.Detail);
    }

    private void Check(string key, bool passed, string detail)
    {
        _facts[key] = passed ? "PASS" : "FAIL";
        _facts[$"{key}Detail"] = detail;
        if (!passed)
        {
            _failures.Add($"{key}: {detail}");
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
