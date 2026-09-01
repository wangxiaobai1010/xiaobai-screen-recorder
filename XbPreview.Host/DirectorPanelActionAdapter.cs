using System.Diagnostics;
using XbPreview.Avalonia.Views.Panels;

namespace XbPreview.Host;

/// <summary>
/// Bridges both live Director views to the one frozen camera runtime. The
/// views own no commands, hotkeys, raw input, or persisted preferences.
/// </summary>
internal sealed class DirectorPanelActionAdapter : IDisposable
{
    private readonly object _gate = new();
    private readonly DirectorPanelPresentationState _presentationState;
    private readonly FixedTargetCameraController _cameraController;
    private readonly ProductState _productState;
    private readonly RawMouseInputObserver _directorInput;
    private readonly HashSet<DirectorPanelView> _views = [];
    private PreviewLifecycleController? _lifecycle;
    private HotkeyService? _hotkeys;
    private nint _window;
    private bool _previewAvailable;
    private bool _initialized;
    private bool _disposed;

    internal DirectorPanelActionAdapter(
        DirectorPanelPresentationState presentationState,
        FixedTargetCameraController cameraController,
        ProductState? productState = null,
        RawMouseInputObserver? directorInput = null)
    {
        _presentationState = presentationState ??
            throw new ArgumentNullException(nameof(presentationState));
        _cameraController = cameraController ??
            throw new ArgumentNullException(nameof(cameraController));
        _productState = productState ?? new ProductState();
        _directorInput = directorInput ?? new RawMouseInputObserver();
        _directorInput.ActivityObserved += OnDirectorPointerActivity;

        lock (_gate)
        {
            PublishPresentationUnsafe();
        }
    }

    internal void AttachView(DirectorPanelView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!ReferenceEquals(
                    view.PresentationState,
                    _presentationState))
            {
                throw new InvalidOperationException(
                    "Every Director view must use the authoritative shared state.");
            }
            if (!_views.Add(view))
            {
                return;
            }

            view.ManualZoomRequested += OnManualZoomRequested;
            view.HotkeysEnabledChangeRequested +=
                OnHotkeysEnabledChangeRequested;
            view.AutoDirectorEnabledChangeRequested +=
                OnAutoDirectorEnabledChangeRequested;
        }
    }

    internal void DetachView(DirectorPanelView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        lock (_gate)
        {
            if (!_views.Remove(view))
            {
                return;
            }

            view.ManualZoomRequested -= OnManualZoomRequested;
            view.HotkeysEnabledChangeRequested -=
                OnHotkeysEnabledChangeRequested;
            view.AutoDirectorEnabledChangeRequested -=
                OnAutoDirectorEnabledChangeRequested;
        }
    }

    internal void Initialize(
        nint window,
        PreviewLifecycleController lifecycle)
    {
        ArgumentNullException.ThrowIfNull(lifecycle);
        if (window == nint.Zero)
        {
            throw new ArgumentException(
                "Director hotkeys require a real top-level HWND.",
                nameof(window));
        }

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_initialized)
            {
                if (_window != window || !ReferenceEquals(_lifecycle, lifecycle))
                {
                    throw new InvalidOperationException(
                        "Director actions are already attached to another runtime.");
                }
                return;
            }

            _window = window;
            _lifecycle = lifecycle;
            _previewAvailable = _previewAvailable || lifecycle.IsPreviewing;
            _hotkeys = new HotkeyService(window);
            _hotkeys.SetPreviewAvailable(_previewAvailable);

            ProductSettings settings = _productState.Current;
            HotkeyRegistrationResult hotkeyResult =
                _hotkeys.SetUserEnabled(settings.ManualHotkeysEnabled);
            if (hotkeyResult.State == HotkeyActivationState.Failed)
            {
                Debug.WriteLine(
                    "Director hotkey registration failed: " +
                    hotkeyResult.WindowsErrorCode);
            }

            _initialized = true;
            EnsureFrozenFollowEnabledUnsafe();
            if (settings.AutoDirectorEnabled &&
                !TrySetAutoDirectorRuntimeUnsafe(
                    enabled: true,
                    out string autoStatus))
            {
                Debug.WriteLine(
                    $"Persisted Auto Director could not start: {autoStatus}");
                CommitSettingsUnsafe(settings with
                {
                    AutoDirectorEnabled = false,
                });
            }
            else
            {
                // F9/F10 are Manual takeover actions in the current product
                // contract, so their user-enabled registrations remain live
                // while Auto owns the camera.
                _hotkeys.SetDirectorOwnsCamera(false);
            }

            PublishPresentationUnsafe();
        }
    }

    internal void SetPreviewAvailable(
        bool available,
        bool changesPresentation = true)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _previewAvailable = available;
            if (_hotkeys is null)
            {
                PublishPresentationUnsafe(
                    preserveActionAvailability: !changesPresentation,
                    preserveHotkeyPresentation:
                        !changesPresentation && !available);
                return;
            }

            _hotkeys.SetPreviewAvailable(available);
            if (available)
            {
                EnsureFrozenFollowEnabledUnsafe();
                HotkeyRegistrationResult hotkeyResult =
                    _hotkeys.SetUserEnabled(
                        _productState.Current.ManualHotkeysEnabled);
                if (hotkeyResult.State == HotkeyActivationState.Failed)
                {
                    Debug.WriteLine(
                        "Director hotkey registration failed: " +
                        hotkeyResult.WindowsErrorCode);
                }
            }
            if (!available)
            {
                _directorInput.Stop();
                _hotkeys.SetDirectorOwnsCamera(false);
            }
            else if (_productState.Current.AutoDirectorEnabled &&
                !TrySetAutoDirectorRuntimeUnsafe(
                    enabled: true,
                    out string autoStatus))
            {
                Debug.WriteLine(
                    $"Auto Director restore was rejected: {autoStatus}");
                CommitSettingsUnsafe(_productState.Current with
                {
                    AutoDirectorEnabled = false,
                });
            }

            PublishPresentationUnsafe(
                preserveActionAvailability: !changesPresentation,
                preserveHotkeyPresentation:
                    !changesPresentation && !available);
        }
    }

    internal bool ProcessWindowMessage(
        int message,
        nint wParam,
        nint lParam)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return false;
            }

            _ = _directorInput.ProcessMessage(message, lParam);
            if (message != HotkeyService.WmHotkey ||
                !HotkeyBindings.TryResolveId(
                    wParam.ToInt32(),
                    out HotkeyBinding binding))
            {
                return false;
            }

            if (_hotkeys?.CanDispatch(binding) == true)
            {
                ExecuteManualZoomUnsafe(binding.Command);
            }
            return true;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            foreach (DirectorPanelView view in _views.ToArray())
            {
                view.ManualZoomRequested -= OnManualZoomRequested;
                view.HotkeysEnabledChangeRequested -=
                    OnHotkeysEnabledChangeRequested;
                view.AutoDirectorEnabledChangeRequested -=
                    OnAutoDirectorEnabledChangeRequested;
            }
            _views.Clear();

            if (_cameraController.Owner == CameraOwner.DirectorLite)
            {
                _ = _cameraController.SetDirectorLiteEnabled(
                    false,
                    Stopwatch.GetTimestamp(),
                    out _);
            }
            _directorInput.ActivityObserved -= OnDirectorPointerActivity;
            _directorInput.Dispose();
            _hotkeys?.SetDirectorOwnsCamera(false);
            _hotkeys?.SetPreviewAvailable(false);
            _hotkeys?.Dispose();
            _hotkeys = null;
            _lifecycle = null;
            _window = nint.Zero;
            _previewAvailable = false;
            _initialized = false;
            PublishPresentationUnsafe();
            _disposed = true;
        }
    }

    private void OnManualZoomRequested(
        object? sender,
        DirectorManualZoomRequestedEventArgs e)
    {
        lock (_gate)
        {
            if (_disposed || !_initialized || !_previewAvailable)
            {
                PublishPresentationUnsafe();
                return;
            }

            CameraCommand command = e.Zoom switch
            {
                DirectorPanelManualZoom.Standard =>
                    CameraCommand.ToggleStandardCloseUp,
                DirectorPanelManualZoom.Strong =>
                    CameraCommand.ToggleStrongCloseUp,
                _ => throw new ArgumentOutOfRangeException(
                    nameof(e),
                    e.Zoom,
                    "Wide is runtime readback, not a Director button action."),
            };
            ExecuteManualZoomUnsafe(command);
        }
    }

    private void OnHotkeysEnabledChangeRequested(
        object? sender,
        DirectorToggleRequestedEventArgs e)
    {
        lock (_gate)
        {
            if (_disposed || !_initialized || !_previewAvailable ||
                _hotkeys is null)
            {
                PublishPresentationUnsafe();
                return;
            }

            HotkeyRegistrationResult result =
                _hotkeys.SetUserEnabled(e.Enabled);
            CommitSettingsUnsafe(_productState.Current with
            {
                ManualHotkeysEnabled = e.Enabled,
            });
            if (result.State == HotkeyActivationState.Failed)
            {
                Debug.WriteLine(
                    "Director hotkey registration failed: " +
                    result.WindowsErrorCode);
            }
            PublishPresentationUnsafe();
        }
    }

    private void OnAutoDirectorEnabledChangeRequested(
        object? sender,
        DirectorToggleRequestedEventArgs e)
    {
        lock (_gate)
        {
            if (_disposed || !_initialized || !_previewAvailable)
            {
                PublishPresentationUnsafe();
                return;
            }

            bool accepted = TrySetAutoDirectorRuntimeUnsafe(
                e.Enabled,
                out string status);
            Debug.WriteLine($"Director AUTO: {status}");
            if (accepted)
            {
                CommitSettingsUnsafe(_productState.Current with
                {
                    AutoDirectorEnabled =
                        _cameraController.Owner == CameraOwner.DirectorLite,
                });
            }
            PublishPresentationUnsafe();
        }
    }

    private void ExecuteManualZoomUnsafe(CameraCommand command)
    {
        EnsureFrozenFollowEnabledUnsafe();

        bool autoPreferenceEnabled =
            _productState.Current.AutoDirectorEnabled;
        if (_cameraController.Owner == CameraOwner.DirectorLite)
        {
            if (!TrySetAutoDirectorRuntimeUnsafe(
                    enabled: false,
                    out string takeoverStatus))
            {
                Debug.WriteLine(
                    $"Director manual takeover rejected: {takeoverStatus}");
                PublishPresentationUnsafe();
                return;
            }
            Debug.WriteLine(
                $"Director manual takeover: {takeoverStatus}");
        }
        if (autoPreferenceEnabled)
        {
            CommitSettingsUnsafe(_productState.Current with
            {
                AutoDirectorEnabled = false,
            });
        }
        if (_cameraController.Owner != CameraOwner.Manual)
        {
            PublishPresentationUnsafe();
            return;
        }

        bool accepted;
        string status;
        try
        {
            accepted = _cameraController.Execute(
                command,
                ReadCurrentCameraTargetUnsafe,
                Stopwatch.GetTimestamp(),
                out status);
        }
        catch (Exception error)
        {
            accepted = false;
            status = error.Message;
        }
        Debug.WriteLine(
            $"Director manual command {command}: {accepted}; {status}");
        PublishPresentationUnsafe();
    }

    private bool TrySetAutoDirectorRuntimeUnsafe(
        bool enabled,
        out string status)
    {
        if (!_initialized || _hotkeys is null ||
            (enabled && !_previewAvailable))
        {
            status = "Director runtime is not ready.";
            return false;
        }
        if (enabled && !_directorInput.Start(_window))
        {
            status =
                "Auto Director input registration failed: " +
                _directorInput.LastWindowsError;
            return false;
        }

        bool accepted = _cameraController.SetDirectorLiteEnabled(
            enabled,
            Stopwatch.GetTimestamp(),
            out status);
        if (!enabled || !accepted)
        {
            _directorInput.Stop();
        }
        // The camera controller remains the sole owner authority. Hotkeys are
        // Manual takeover entries, not a second owner, so Auto must not
        // unregister or suspend the user's F9/F10 preference.
        _hotkeys.SetDirectorOwnsCamera(false);
        return accepted;
    }

    private void EnsureFrozenFollowEnabledUnsafe()
    {
        if (_initialized && _previewAvailable)
        {
            // Reuse the accepted CameraUpdateService/ComfortZoneTracker path.
            // Capture-target restart creates a fresh service with Follow off;
            // this existing lifecycle seam restores the product default.
            _lifecycle?.SetFollowEnabled(true);
        }
    }

    private void OnDirectorPointerActivity(RawPointerActivity activity)
    {
        lock (_gate)
        {
            if (_disposed || !_initialized ||
                _cameraController.Owner != CameraOwner.DirectorLite)
            {
                return;
            }

            CaptureTarget target = _lifecycle?.CurrentCaptureTarget ??
                CaptureTarget.FullScreen;
            CameraPoint windowTarget = default;
            if (target.IsWindow &&
                !WindowCaptureSelector.TryMapCurrentCursor(
                    target.WindowHandle,
                    out windowTarget))
            {
                // Raw Input is process-wide. Activity outside the selected
                // window must not retarget or extend Director focus.
                return;
            }

            long now = Stopwatch.GetTimestamp();
            if (activity.IsLeftButtonDown)
            {
                _cameraController.HandleDirectorPointerActivity(now);
                try
                {
                    _cameraController.HandleDirectorLeftClick(
                        target.IsWindow
                            ? windowTarget
                            : CameraCursorTarget
                                .ReadPrimaryMonitorTarget(),
                        now,
                        out _);
                }
                catch (Exception error)
                {
                    Debug.WriteLine(
                        "Director click position read failed: " + error);
                }
            }
            else
            {
                _cameraController.HandleDirectorPointerActivity(now);
            }
        }
    }

    private CameraPoint ReadCurrentCameraTargetUnsafe()
    {
        CaptureTarget target = _lifecycle?.CurrentCaptureTarget ??
            CaptureTarget.FullScreen;
        if (!target.IsWindow)
        {
            return CameraCursorTarget.ReadPrimaryMonitorTarget();
        }
        return WindowCaptureSelector.TryMapCurrentCursor(
            target.WindowHandle,
            out CameraPoint point)
                ? point
                : new CameraPoint(0.5, 0.5);
    }

    private void CommitSettingsUnsafe(ProductSettings settings)
    {
        _productState.Set(settings);
        try
        {
            _productState.Persist();
        }
        catch (Exception error) when (
            error is IOException or UnauthorizedAccessException or
                NotSupportedException)
        {
            Debug.WriteLine(
                $"Director preference persistence failed: {error}");
        }
    }

    private void PublishPresentationUnsafe(
        bool preserveActionAvailability = false,
        bool preserveHotkeyPresentation = false)
    {
        ProductSettings settings = _productState.Current;
        bool hotkeysEnabled = preserveHotkeyPresentation
            ? _presentationState.Snapshot.HotkeysEnabled
            : _hotkeys?.UserEnabled ?? settings.ManualHotkeysEnabled;
        if (!preserveHotkeyPresentation &&
            _hotkeys?.State == HotkeyActivationState.Failed)
        {
            hotkeysEnabled = false;
        }

        bool actionsEnabled = preserveActionAvailability
            ? _presentationState.Snapshot.ActionsEnabled
            : _initialized && _previewAvailable;
        CameraOwner owner = _cameraController.Owner;
        bool autoEnabled = owner == CameraOwner.DirectorLite;
        _presentationState.Apply(new DirectorPanelPresentationSnapshot(
            owner == CameraOwner.Manual
                ? MapManualZoomUnsafe()
                : DirectorPanelManualZoom.Wide,
            hotkeysEnabled,
            autoEnabled,
            actionsEnabled,
            actionsEnabled));
    }

    private DirectorPanelManualZoom MapManualZoomUnsafe()
    {
        double zoom = _cameraController.TargetZoom;
        if (Math.Abs(zoom - CameraSettings.StandardZoom) < 0.001)
        {
            return DirectorPanelManualZoom.Standard;
        }
        if (Math.Abs(zoom - CameraSettings.StrongZoom) < 0.001)
        {
            return DirectorPanelManualZoom.Strong;
        }
        return DirectorPanelManualZoom.Wide;
    }
}
