using System.Diagnostics;
using System.Runtime.InteropServices;
using XbPreview.Avalonia.Contracts;

namespace XbPreview.Host;

// Thin command/state adapter only. Production Preview, camera, hotkey,
// recording, audio, and persistence objects remain the sole owners.
internal sealed class ProductionHomeAdapter : IProductReviewController
{
    private const string DefaultMicrophoneId = "windows-default";
    private readonly ProductState _productState;
    private readonly PreviewLifecycleController _lifecycle;
    private readonly IPreviewNativeSession _native;
    private readonly RecordingController _recording;
    private readonly FixedTargetCameraController _camera;
    private readonly HotkeyService _hotkeys;
    private readonly Func<bool, ProductReviewCommandResult> _setDirectorEnabled;
    private readonly Func<CaptureTarget, CameraPoint> _readCameraTarget;
    private IReadOnlyList<ResolvedWindowChoice> _windows =
        Array.Empty<ResolvedWindowChoice>();
    private CaptureTarget _captureTarget = CaptureTarget.FullScreen;
    private string _statusText = "Production adapters are initializing.";

    private sealed record ResolvedWindowChoice(
        WindowCaptureChoice Native,
        ProductReviewWindowChoice Review);

    internal ProductionHomeAdapter(
        ProductState productState,
        PreviewLifecycleController lifecycle,
        IPreviewNativeSession native,
        RecordingController recording,
        FixedTargetCameraController camera,
        HotkeyService hotkeys,
        Func<bool, ProductReviewCommandResult> setDirectorEnabled,
        Func<CaptureTarget, CameraPoint> readCameraTarget)
    {
        _productState = productState ??
            throw new ArgumentNullException(nameof(productState));
        _lifecycle = lifecycle ??
            throw new ArgumentNullException(nameof(lifecycle));
        _native = native ?? throw new ArgumentNullException(nameof(native));
        _recording = recording ??
            throw new ArgumentNullException(nameof(recording));
        _camera = camera ?? throw new ArgumentNullException(nameof(camera));
        _hotkeys = hotkeys ?? throw new ArgumentNullException(nameof(hotkeys));
        _setDirectorEnabled = setDirectorEnabled ??
            throw new ArgumentNullException(nameof(setDirectorEnabled));
        _readCameraTarget = readCameraTarget ??
            throw new ArgumentNullException(nameof(readCameraTarget));
    }

    public event Action<ProductReviewSnapshot>? SnapshotChanged;

    public ProductReviewSnapshot CurrentSnapshot => BuildSnapshot();

    internal ProductState ProductState => _productState;

    internal CaptureTarget CaptureTarget => _captureTarget;

    internal void RefreshState(string detail) =>
        Publish(ProductReviewCommandResult.Success(detail));

    internal async Task<ProductReviewCommandResult> InitializeAsync()
    {
        RefreshWindowChoices();
        ProductSettings settings = _productState.Current;
        ProductSettingsApplyResult apply =
            ProductSettingsRuntimeAdapter.Apply(_native, settings);
        if (!apply.Succeeded)
        {
            return Publish(ProductReviewCommandResult.Rejected(
                $"{apply.Operation} was rejected: {apply.Result}."));
        }

        _ = _hotkeys.SetUserEnabled(settings.ManualHotkeysEnabled);
        ProductWindowIdentity? identity = settings.SelectedWindowIdentity;
        ResolvedWindowChoice? selected = identity is null
            ? null
            : _windows.FirstOrDefault(choice =>
                string.Equals(
                    choice.Review.ProcessName,
                    identity.ProcessName,
                    StringComparison.OrdinalIgnoreCase) &&
                string.Equals(
                    choice.Review.Title,
                    identity.WindowTitle,
                    StringComparison.Ordinal));
        if (settings.CaptureTargetMode == ProductCaptureTargetMode.Window &&
            selected is not null)
        {
            ProductReviewCommandResult target = await SwitchCaptureTargetAsync(
                new CaptureTarget(
                    CaptureTargetKind.Window,
                    selected.Native.Handle,
                    selected.Native.Title));
            if (!target.Succeeded)
            {
                return target;
            }
        }
        if (settings.AutoDirectorEnabled)
        {
            ProductReviewCommandResult director = _setDirectorEnabled(true);
            if (!director.Succeeded)
            {
                settings = settings with { AutoDirectorEnabled = false };
                _productState.Set(settings);
                TryPersist();
            }
        }
        return Publish(ProductReviewCommandResult.Success(
            "Production controls are ready."));
    }

    public ProductReviewCommandResult RefreshDevices()
    {
        RefreshWindowChoices();
        return Publish(ProductReviewCommandResult.Success(
            $"Enumerated {_windows.Count} real selectable windows."));
    }

    public async Task<ProductReviewCommandResult>
        SetCaptureTargetFullScreenAsync()
    {
        ProductReviewCommandResult mutable = RequireSettingsChange();
        if (!mutable.Succeeded)
        {
            return Publish(mutable);
        }
        ProductReviewCommandResult result = await SwitchCaptureTargetAsync(
            CaptureTarget.FullScreen);
        if (!result.Succeeded)
        {
            return result;
        }
        ProductSettings settings = _productState.Current with
        {
            CaptureTargetMode = ProductCaptureTargetMode.FullScreen,
            SelectedWindowIdentity = null,
        };
        return Commit(settings, "Capture target: full screen.");
    }

    public async Task<ProductReviewCommandResult> SetCaptureTargetWindowAsync(
        string id)
    {
        ProductReviewCommandResult mutable = RequireSettingsChange();
        if (!mutable.Succeeded)
        {
            return Publish(mutable);
        }
        RefreshWindowChoices();
        ResolvedWindowChoice? choice = _windows.FirstOrDefault(item =>
            string.Equals(item.Review.Id, id, StringComparison.Ordinal));
        if (choice is null)
        {
            return Publish(ProductReviewCommandResult.Rejected(
                "The selected real window is no longer available."));
        }
        ProductReviewCommandResult result = await SwitchCaptureTargetAsync(
            new CaptureTarget(
                CaptureTargetKind.Window,
                choice.Native.Handle,
                choice.Native.Title));
        if (!result.Succeeded)
        {
            return result;
        }
        ProductSettings settings = _productState.Current with
        {
            CaptureTargetMode = ProductCaptureTargetMode.Window,
            SelectedWindowIdentity = new ProductWindowIdentity(
                choice.Review.ProcessName,
                choice.Review.Title),
        };
        return Commit(settings, $"Capture target: {choice.Review.Title}.");
    }

    public Task<ProductReviewCommandResult> SetMicrophoneEnabledAsync(
        bool enabled) => ApplyAndCommitAsync(
            _productState.Current with { MicrophoneEnabled = enabled },
            $"Microphone {(enabled ? "enabled" : "disabled")}.");

    public Task<ProductReviewCommandResult> SetMicrophoneSelectionAsync(
        string id)
    {
        MicrophoneDeviceCatalog catalog = SafeMicrophoneCatalog();
        MicrophoneSelection? selection = string.Equals(
            id,
            DefaultMicrophoneId,
            StringComparison.Ordinal)
                ? new MicrophoneSelection(
                    MicrophoneSelectionKind.WindowsDefault,
                    string.Empty,
                    catalog.DefaultDisplayName)
                : catalog.Devices
                    .Where(device => string.Equals(
                        device.EndpointId,
                        id,
                        StringComparison.Ordinal))
                    .Select(device => new MicrophoneSelection(
                        MicrophoneSelectionKind.ConcreteEndpoint,
                        device.EndpointId,
                        device.DisplayName))
                    .FirstOrDefault();
        if (selection is null)
        {
            return Task.FromResult(Publish(
                ProductReviewCommandResult.Rejected(
                    "The selected microphone is unavailable.")));
        }
        return ApplyAndCommitAsync(
            _productState.Current with { MicrophoneSelection = selection },
            $"Microphone selection: {selection.DisplayName}.");
    }

    public Task<ProductReviewCommandResult> SetSystemAudioEnabledAsync(
        bool enabled) => ApplyAndCommitAsync(
            _productState.Current with { SystemAudioEnabled = enabled },
            $"System audio {(enabled ? "enabled" : "disabled")}.");

    public Task<ProductReviewCommandResult> SetCursorVisibleAsync(bool visible) =>
        ApplyAndCommitAsync(
            _productState.Current with { MouseVisible = visible },
            $"Recorded cursor {(visible ? "visible" : "hidden")}.");

    public Task<ProductReviewCommandResult> ExecuteManualZoomAsync(
        ProductReviewManualZoom zoom)
    {
        if (_camera.Owner != CameraOwner.Manual)
        {
            return Task.FromResult(Publish(
                ProductReviewCommandResult.Rejected(
                    "Manual zoom is suspended while Auto Director is on.")));
        }
        CameraCommand command = zoom switch
        {
            ProductReviewManualZoom.Standard =>
                CameraCommand.ToggleStandardCloseUp,
            ProductReviewManualZoom.Strong =>
                CameraCommand.ToggleStrongCloseUp,
            _ => _camera.TargetZoom == CameraSettings.StandardZoom
                ? CameraCommand.ToggleStandardCloseUp
                : CameraCommand.ToggleStrongCloseUp,
        };
        bool accepted;
        string detail;
        try
        {
            accepted = _camera.Execute(
                command,
                () => _readCameraTarget(_captureTarget),
                Stopwatch.GetTimestamp(),
                out detail);
        }
        catch (Exception error)
        {
            accepted = false;
            detail = error.Message;
        }
        return Task.FromResult(Publish(accepted
            ? ProductReviewCommandResult.Success(detail)
            : ProductReviewCommandResult.Rejected(detail)));
    }

    public Task<ProductReviewCommandResult> SetHotkeysEnabledAsync(bool enabled)
    {
        ProductReviewCommandResult mutable = RequireSettingsChange();
        if (!mutable.Succeeded)
        {
            return Task.FromResult(Publish(mutable));
        }
        HotkeyRegistrationResult activation =
            _hotkeys.SetUserEnabled(enabled);
        ProductSettings settings = _productState.Current with
        {
            ManualHotkeysEnabled = enabled,
        };
        string detail = activation.State == HotkeyActivationState.Failed
            ? $"Hotkey registration failed: {activation.WindowsErrorCode}."
            : $"Hotkeys {(enabled ? "enabled" : "disabled")}.";
        return Task.FromResult(Commit(settings, detail));
    }

    public Task<ProductReviewCommandResult> SetAutoDirectorEnabledAsync(
        bool enabled)
    {
        ProductReviewCommandResult mutable = RequireSettingsChange();
        if (!mutable.Succeeded)
        {
            return Task.FromResult(Publish(mutable));
        }
        ProductReviewCommandResult activation = _setDirectorEnabled(enabled);
        if (!activation.Succeeded)
        {
            return Task.FromResult(Publish(activation));
        }
        ProductSettings settings = _productState.Current with
        {
            AutoDirectorEnabled = enabled,
        };
        return Task.FromResult(Commit(settings, activation.Detail));
    }

    public Task<ProductReviewCommandResult> SetStagePoseAsync(
        ProductReviewStageOrientation orientation,
        ProductReviewStageLevel level) => ApplyAndCommitAsync(
            _productState.Current with
            {
                StageOrientation = (ProductStageOrientation)orientation,
                StageLevel = (ProductStageLevel)level,
            },
            $"3D pose: {orientation} {level}.");

    public Task<ProductReviewCommandResult> SetBackgroundPresetAsync(
        ProductReviewBackgroundPreset preset) => ApplyAndCommitAsync(
            _productState.Current with
            {
                BackgroundSource = ProductBackgroundSource.Preset,
                BackgroundPreset = (ProductBackgroundPreset)preset,
            },
            $"Background preset: {preset}.");

    public Task<ProductReviewCommandResult> SetCustomBackgroundAsync(string path)
    {
        if (!ProductPathContract.TryValidateCustomBackground(
            path,
            out string validated))
        {
            return Task.FromResult(Publish(
                ProductReviewCommandResult.Rejected(
                    "Custom background must be an existing local image.")));
        }
        return ApplyAndCommitAsync(
            _productState.Current with
            {
                BackgroundSource = ProductBackgroundSource.CustomImage,
                CustomBackgroundPath = validated,
            },
            $"Custom background: {validated}.");
    }

    public Task<ProductReviewCommandResult> SetOutputRootAsync(string? path)
    {
        string? validated = null;
        if (!string.IsNullOrWhiteSpace(path))
        {
            if (!ProductPathContract.TryValidateOutputRoot(
                path,
                out string validatedPath))
            {
                return Task.FromResult(Publish(
                    ProductReviewCommandResult.Rejected(
                        "Output root must be an existing local directory.")));
            }
            validated = validatedPath;
        }
        return ApplyAndCommitAsync(
            _productState.Current with { OutputRoot = validated },
            validated is null
                ? "Output root restored to product default."
                : $"Output root: {validated}.");
    }

    public async Task<ProductReviewCommandResult> ResetToDefaultsAsync()
    {
        ProductReviewCommandResult mutable = RequireSettingsChange();
        if (!mutable.Succeeded)
        {
            return Publish(mutable);
        }
        ProductReviewCommandResult director = _setDirectorEnabled(false);
        if (!director.Succeeded)
        {
            return Publish(director);
        }
        if (_captureTarget.IsWindow)
        {
            ProductReviewCommandResult target = await SwitchCaptureTargetAsync(
                CaptureTarget.FullScreen);
            if (!target.Succeeded)
            {
                return target;
            }
        }
        ProductSettings defaults = ProductSettings.Defaults;
        ProductSettingsApplyResult apply =
            ProductSettingsRuntimeAdapter.Apply(_native, defaults);
        if (!apply.Succeeded)
        {
            return Publish(ProductReviewCommandResult.Rejected(
                $"{apply.Operation} was rejected: {apply.Result}."));
        }
        _ = _hotkeys.SetUserEnabled(defaults.ManualHotkeysEnabled);
        _productState.ResetToDefaults();
        if (!TryPersist())
        {
            return Publish(ProductReviewCommandResult.Rejected(
                "Defaults were applied but could not be persisted."));
        }
        return Publish(ProductReviewCommandResult.Success(
            "Product settings restored to defaults."));
    }

    private async Task<ProductReviewCommandResult> SwitchCaptureTargetAsync(
        CaptureTarget target)
    {
        bool restoreDirector = _camera.Owner == CameraOwner.DirectorLite;
        if (restoreDirector)
        {
            ProductReviewCommandResult disabled = _setDirectorEnabled(false);
            if (!disabled.Succeeded)
            {
                return Publish(disabled);
            }
        }
        PreviewLifecycleResult stop = await _lifecycle.StopAsync();
        if (!stop.Succeeded)
        {
            return Publish(ProductReviewCommandResult.Rejected(
                $"Preview stop before capture switch failed: {stop.Error}"));
        }
        PreviewLifecycleResult configured =
            await _lifecycle.SetCaptureTargetAsync(target);
        if (!configured.Succeeded)
        {
            return Publish(ProductReviewCommandResult.Rejected(
                $"Capture target was rejected: {configured.Error}"));
        }
        PreviewLifecycleResult start = await _lifecycle.StartAsync(
            cameraEnabled: true,
            followEnabled: false,
            NativeMethods.CursorMode.SystemCursor);
        if (!start.Succeeded)
        {
            return Publish(ProductReviewCommandResult.Rejected(
                $"Preview restart after capture switch failed: {start.Error}"));
        }
        _captureTarget = target;
        if (restoreDirector)
        {
            ProductReviewCommandResult restored = _setDirectorEnabled(true);
            if (!restored.Succeeded)
            {
                return Publish(restored);
            }
        }
        return Publish(ProductReviewCommandResult.Success(
            target.IsWindow
                ? $"Capture target: {target.Title}."
                : "Capture target: full screen."));
    }

    private Task<ProductReviewCommandResult> ApplyAndCommitAsync(
        ProductSettings settings,
        string detail)
    {
        ProductReviewCommandResult mutable = RequireSettingsChange();
        if (!mutable.Succeeded)
        {
            return Task.FromResult(Publish(mutable));
        }
        ProductSettingsApplyResult apply =
            ProductSettingsRuntimeAdapter.Apply(_native, settings);
        ProductReviewCommandResult result = apply.Succeeded
            ? Commit(settings, detail)
            : Publish(ProductReviewCommandResult.Rejected(
                $"{apply.Operation} was rejected: {apply.Result}."));
        return Task.FromResult(result);
    }

    private ProductReviewCommandResult Commit(
        ProductSettings settings,
        string detail)
    {
        _productState.Set(settings);
        if (!TryPersist())
        {
            return Publish(ProductReviewCommandResult.Rejected(
                $"{detail} Runtime accepted it, but persistence failed."));
        }
        return Publish(ProductReviewCommandResult.Success(detail));
    }

    private bool TryPersist()
    {
        try
        {
            _productState.Persist();
            return true;
        }
        catch (Exception error) when (
            error is IOException or UnauthorizedAccessException or
                NotSupportedException)
        {
            return false;
        }
    }

    private ProductReviewCommandResult RequireSettingsChange()
    {
        ManagedRecordingSnapshot snapshot = _recording.CurrentSnapshot;
        return !snapshot.IsActive && !_recording.HasPendingOperation &&
            _lifecycle.IsPreviewing
                ? ProductReviewCommandResult.Success()
                : ProductReviewCommandResult.Rejected(
                    "Production settings are locked during a recording transition.");
    }

    private ProductReviewCommandResult Publish(
        ProductReviewCommandResult result)
    {
        _statusText = result.Detail;
        SnapshotChanged?.Invoke(BuildSnapshot());
        return result;
    }

    private ProductReviewSnapshot BuildSnapshot()
    {
        ProductSettings settings = _productState.Current;
        MicrophoneDeviceCatalog catalog = SafeMicrophoneCatalog();
        MicrophoneSelectionStatus microphone = SafeMicrophoneStatus();
        List<ProductReviewMicrophoneChoice> microphones =
        [
            new(
                DefaultMicrophoneId,
                string.IsNullOrWhiteSpace(catalog.DefaultDisplayName)
                    ? "Windows 默认麦克风"
                    : $"Windows 默认麦克风 — {catalog.DefaultDisplayName}",
                catalog.DefaultAvailable),
        ];
        microphones.AddRange(catalog.Devices.Select(device =>
            new ProductReviewMicrophoneChoice(
                device.EndpointId,
                device.DisplayName,
                true)));
        if (settings.MicrophoneSelection.Kind ==
                MicrophoneSelectionKind.ConcreteEndpoint &&
            !microphones.Any(choice => string.Equals(
                choice.Id,
                settings.MicrophoneSelection.EndpointId,
                StringComparison.Ordinal)))
        {
            microphones.Add(new ProductReviewMicrophoneChoice(
                settings.MicrophoneSelection.EndpointId,
                settings.MicrophoneSelection.DisplayName,
                false));
        }
        string selectedWindowId = _captureTarget.IsWindow
            ? WindowId(_captureTarget.WindowHandle)
            : string.Empty;
        bool canChange = !_recording.CurrentSnapshot.IsActive &&
            !_recording.HasPendingOperation && _lifecycle.IsPreviewing;
        return new ProductReviewSnapshot(
            _captureTarget.IsWindow
                ? ProductReviewCaptureTargetMode.Window
                : ProductReviewCaptureTargetMode.FullScreen,
            selectedWindowId,
            _windows.Select(choice => choice.Review).ToArray(),
            settings.MicrophoneEnabled,
            settings.MicrophoneSelection.Kind ==
                MicrophoneSelectionKind.WindowsDefault
                    ? DefaultMicrophoneId
                    : settings.MicrophoneSelection.EndpointId,
            microphone.Available,
            microphones,
            settings.SystemAudioEnabled,
            settings.MouseVisible,
            settings.ManualHotkeysEnabled,
            _hotkeys.State.ToString(),
            settings.AutoDirectorEnabled,
            _camera.Owner == CameraOwner.Manual && _lifecycle.IsPreviewing,
            MapManualZoom(),
            (ProductReviewStageOrientation)settings.StageOrientation,
            (ProductReviewStageLevel)settings.StageLevel,
            (ProductReviewBackgroundPreset)settings.BackgroundPreset,
            settings.BackgroundSource == ProductBackgroundSource.CustomImage,
            settings.CustomBackgroundPath ?? string.Empty,
            settings.OutputRoot ?? string.Empty,
            canChange,
            _statusText);
    }

    private ProductReviewManualZoom MapManualZoom()
    {
        double zoom = _camera.TargetZoom;
        if (Math.Abs(zoom - CameraSettings.StandardZoom) < 0.001)
        {
            return ProductReviewManualZoom.Standard;
        }
        if (Math.Abs(zoom - CameraSettings.StrongZoom) < 0.001)
        {
            return ProductReviewManualZoom.Strong;
        }
        return ProductReviewManualZoom.Wide;
    }

    private void RefreshWindowChoices()
    {
        _windows = WindowCaptureSelector.Enumerate()
            .Select(choice => new ResolvedWindowChoice(
                choice,
                new ProductReviewWindowChoice(
                    WindowId(choice.Handle),
                    TryGetProcessName(choice.Handle),
                    choice.Title)))
            .ToArray();
    }

    private MicrophoneDeviceCatalog SafeMicrophoneCatalog()
    {
        try
        {
            return _recording.GetMicrophoneDevices();
        }
        catch
        {
            return MicrophoneDeviceCatalog.Empty;
        }
    }

    private MicrophoneSelectionStatus SafeMicrophoneStatus()
    {
        try
        {
            return _recording.GetMicrophoneSelection();
        }
        catch
        {
            return MicrophoneSelectionStatus.UnavailableDefault;
        }
    }

    private static string WindowId(nint handle) =>
        unchecked((ulong)handle.ToInt64()).ToString("X16");

    private static string TryGetProcessName(nint handle)
    {
        try
        {
            _ = GetWindowThreadProcessId(handle, out uint processId);
            if (processId != 0)
            {
                using Process process = Process.GetProcessById((int)processId);
                if (!string.IsNullOrWhiteSpace(process.ProcessName))
                {
                    return process.ProcessName;
                }
            }
        }
        catch (Exception error) when (
            error is ArgumentException or InvalidOperationException or
                System.ComponentModel.Win32Exception)
        {
        }
        return "unknown-process";
    }

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(
        nint window,
        out uint processId);
}
