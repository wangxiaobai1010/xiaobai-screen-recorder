using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Avalonia;
using Avalonia.Win32.Interoperability;
using XbPreview.Avalonia.Contracts;
using XbPreview.Avalonia.Views;

namespace XbPreview.Host;

internal sealed class FormalAvaloniaHomeHost : Form
{
    private const uint WsPopup = 0x8000_0000;
    private const uint WsDisabled = 0x0800_0000;
    private const uint WsExToolWindow = 0x0000_0080;
    private const uint WsExNoActivate = 0x0800_0000;

    private readonly FormalHomeIntegrationGateRequest? _gateRequest;
    private readonly ProductState _productState;
    private readonly GpuPreviewFrameSource _frameSource = new();
    private readonly FormalHomeView _homeView;
    private readonly WinFormsAvaloniaControlHost _avaloniaHost;
    private readonly FormalUiSettingsView _settingsView;
    private readonly FixedTargetCameraController _cameraController = new();
    private readonly RawMouseInputObserver _directorInput = new();
    private PreviewLifecycleController? _lifecycle;
    private NativePreviewSession? _nativeSession;
    private RecordingController? _recordingController;
    private ProductionRecordingAdapter? _recordingAdapter;
    private ProductionHomeAdapter? _productAdapter;
    private FormalHomeIntegrationGate? _gate;
    private HotkeyService? _hotkeys;
    private CameraDiagnosticLogger? _cameraLogger;
    private ComfortZoneDiagnosticLogger? _followLogger;
    private nint _bootstrapHwnd;
    private string? _gpuDiagnosticPath;
    private bool _startupStarted;
    private bool _closeCleanupStarted;
    private bool _closeCleanupComplete;

    internal FormalAvaloniaHomeHost(
        FormalHomeIntegrationGateRequest? gateRequest = null)
    {
        _gateRequest = gateRequest;
        ProductSettingsStore store = gateRequest is null
            ? new ProductSettingsStore()
            : new ProductSettingsStore(
                gateRequest.SettingsPath,
                legacyMicrophonePath: string.Empty);
        _productState = new ProductState(store);

        Text = "Legacy Review · Production Home Technical Review";
        ClientSize = new System.Drawing.Size(1080, 720);
        MinimumSize = new System.Drawing.Size(940, 640);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimizeBox = true;
        MaximizeBox = true;

        _homeView = new FormalHomeView(_frameSource);
        _homeView.PreviewControl.StatusChanged += OnGpuStatusChanged;
        _homeView.SettingsRequested += OnSettingsRequested;
        _avaloniaHost = new WinFormsAvaloniaControlHost
        {
            Dock = DockStyle.Fill,
            Content = _homeView,
        };
        _settingsView = new FormalUiSettingsView
        {
            Dock = DockStyle.Fill,
            Visible = false,
        };
        _settingsView.BackRequested += OnSettingsBackRequested;
        _settingsView.ResetRequested += OnSettingsResetRequested;

        Controls.Add(_avaloniaHost);
        Controls.Add(_settingsView);
        _avaloniaHost.BringToFront();
        _directorInput.ActivityObserved += OnDirectorPointerActivity;
        Shown += OnShown;
        FormClosing += OnFormClosing;
    }

    internal static void SetupAvalonia()
    {
        AppBuilder.Configure<XbPreview.Avalonia.App>()
            .UsePlatformDetect()
            .With(new Win32PlatformOptions
            {
                CompositionMode = new[]
                {
                    Win32CompositionMode.LowLatencyDxgiSwapChain,
                },
            })
            .SetupWithoutStarting();
    }

    internal FormalHomeView HomeView => _homeView;

    internal WinFormsAvaloniaControlHost AvaloniaHost => _avaloniaHost;

    internal FormalUiSettingsView SettingsView => _settingsView;

    internal PreviewLifecycleController Lifecycle => _lifecycle ??
        throw new InvalidOperationException("Preview lifecycle is unavailable.");

    internal NativePreviewSession NativeSession => _nativeSession ??
        throw new InvalidOperationException("Native preview session is unavailable.");

    internal RecordingController RecordingController => _recordingController ??
        throw new InvalidOperationException("Recording controller is unavailable.");

    internal ProductionRecordingAdapter RecordingAdapter => _recordingAdapter ??
        throw new InvalidOperationException("Recording adapter is unavailable.");

    internal ProductionHomeAdapter ProductAdapter => _productAdapter ??
        throw new InvalidOperationException("Product adapter is unavailable.");

    internal ProductState ProductState => _productState;

    internal bool SettingsVisible => _settingsView.Visible;

    internal void ShowSettings()
    {
        _settingsView.ShowDefaultContent();
        _settingsView.Visible = true;
        _settingsView.BringToFront();
        _settingsView.FocusBackButton();
    }

    internal void ShowHome()
    {
        _settingsView.Visible = false;
        _avaloniaHost.BringToFront();
        _avaloniaHost.Focus();
    }

    internal void PerformSettingsBack()
    {
        Control? button = _settingsView.Controls.Find(
            "SettingsBackButton",
            searchAllChildren: true).FirstOrDefault();
        if (button is Button value)
        {
            value.PerformClick();
        }
        else
        {
            ShowHome();
        }
    }

    internal void PerformSettingsReset()
    {
        Control? button = _settingsView.Controls.Find(
            "ResetDefaultsButton",
            searchAllChildren: true).FirstOrDefault();
        if (button is Button value)
        {
            value.PerformClick();
        }
        else
        {
            throw new InvalidOperationException(
                "Formal Settings reset button was unavailable.");
        }
    }

    private async void OnShown(object? sender, EventArgs e)
    {
        if (_startupStarted)
        {
            return;
        }
        _startupStarted = true;

        try
        {
            string logDirectory = _gateRequest?.DiagnosticDirectory ??
                Path.Combine(AppContext.BaseDirectory, "diagnostic-logs");
            Directory.CreateDirectory(logDirectory);
            _gpuDiagnosticPath = Path.Combine(
                logDirectory,
                $"formal-avalonia-home-" +
                $"{DateTime.Now:yyyyMMdd-HHmmss-fff}.jsonl");
            _bootstrapHwnd = CreateBootstrapWindow();
            _cameraLogger = new CameraDiagnosticLogger(logDirectory);
            _followLogger = new ComfortZoneDiagnosticLogger(logDirectory);
            _hotkeys = new HotkeyService(Handle);

            _lifecycle = new PreviewLifecycleController(
                () =>
                {
                    NativePreviewSession session = NativePreviewSession.Create(
                        _bootstrapHwnd,
                        Handle,
                        logDirectory);
                    _nativeSession = session;
                    _frameSource.Attach(session);
                    return session;
                },
                (session, followEnabled) => new CameraUpdateService(
                    _cameraController,
                    session,
                    _cameraLogger!,
                    _followLogger!,
                    followEnabled),
                _cameraController,
                SetHotkeyPreviewAvailable,
                (state, result, detail) =>
                    _cameraLogger?.Write(state, result, detail: detail));

            EnsureSucceeded(
                await _lifecycle.InitializeAsync(),
                "Initialize preview lifecycle");
            EnsureSucceeded(
                await _lifecycle.RequestResizeAsync(16, 16),
                "Resize hidden bootstrap surface");
            CaptureDisplaySnapshot display =
                new DisplayGeometryProvider().ReadPrimaryDisplay();
            EnsureSucceeded(
                await _lifecycle.SetDesiredGeometryAsync(
                    SessionGeometry.CreateFullScreen(display)),
                "Set full-screen production geometry");
            EnsureSucceeded(
                await _lifecycle.StartAsync(
                    cameraEnabled: true,
                    followEnabled: false,
                    NativeMethods.CursorMode.SystemCursor),
                "Start production preview lifecycle");

            _recordingController =
                _lifecycle.GetOrCreateRecordingController();
            _recordingAdapter =
                new ProductionRecordingAdapter(_recordingController);
            _recordingAdapter.SnapshotChanged += OnRecordingSnapshotChanged;
            _homeView.AttachRecordingController(_recordingAdapter);

            _productAdapter = new ProductionHomeAdapter(
                _productState,
                _lifecycle,
                NativeSession,
                _recordingController,
                _cameraController,
                _hotkeys,
                SetDirectorEnabled,
                ReadCameraTarget);
            _homeView.AttachProductController(_productAdapter);
            ProductReviewCommandResult initialized =
                await _productAdapter.InitializeAsync();
            if (!initialized.Succeeded)
            {
                throw new InvalidOperationException(initialized.Detail);
            }
            WriteRecordingDiagnostic(
                _recordingAdapter.CurrentSnapshot,
                "before-start");

            WriteGpuDiagnostic(new
            {
                @event = "formal-home-production-preview-started",
                processId = Environment.ProcessId,
                hiddenBootstrapHwnd = _bootstrapHwnd.ToInt64(),
                compositionMode =
                    nameof(Win32CompositionMode.LowLatencyDxgiSwapChain),
                avaloniaInterop = _homeView.PreviewControl.InteropStatus,
                hostIdentity = Identity(this),
                avaloniaHostIdentity = Identity(_avaloniaHost),
                homeIdentity = Identity(_homeView),
                lifecycleIdentity = Identity(_lifecycle),
                nativeIdentity = Identity(_nativeSession),
                recordingIdentity = Identity(_recordingController),
                utc = DateTimeOffset.UtcNow,
            });

            if (_gateRequest is not null)
            {
                _gate = new FormalHomeIntegrationGate(_gateRequest);
                await _gate.RunAsync(this);
            }
        }
        catch (Exception error)
        {
            Text = "Legacy Review · Production Home ATTENTION";
            WriteGpuDiagnostic(new
            {
                @event = "formal-home-production-start-failed",
                error = error.ToString(),
                utc = DateTimeOffset.UtcNow,
            });
            if (_gateRequest is not null)
            {
                _gate ??= new FormalHomeIntegrationGate(_gateRequest);
                _gate.RecordFailure(error);
                Close();
            }
        }
    }

    private async void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (_closeCleanupComplete)
        {
            return;
        }
        e.Cancel = true;
        if (_closeCleanupStarted)
        {
            return;
        }
        _closeCleanupStarted = true;

        ManagedRecordingSnapshot beforeClose =
            _recordingController?.CurrentSnapshot ??
                ManagedRecordingSnapshot.Idle;
        ManagedRecordingSnapshot afterClose = beforeClose;
        try
        {
            // Borrower-first close: stop the Avalonia compositor pump, drain
            // pending updates, and dispose imported GPU resources.
            await _homeView.ShutdownPreviewAsync();

            // The Production recording owner then finalizes, validates, and
            // safe-publishes active Recording and Paused sessions. Paused
            // close goes directly through StopForCloseAsync; no Resume route.
            if (_recordingController is not null)
            {
                afterClose = await _recordingController.StopForCloseAsync();
            }

            _ = SetDirectorEnabled(false);
            if (_lifecycle is not null)
            {
                await _lifecycle.DisposeAsync();
                _lifecycle = null;
            }
        }
        catch (Exception error)
        {
            _gate?.RecordFailure(error);
            WriteGpuDiagnostic(new
            {
                @event = "formal-home-production-close-failed",
                error = error.ToString(),
                utc = DateTimeOffset.UtcNow,
            });
        }
        finally
        {
            _gate?.RecordClose(
                beforeClose,
                afterClose,
                _recordingAdapter?.ResumeCommandCount ?? 0,
                _homeView.PreviewControl.LastPresentedFrame);
            if (_recordingAdapter is not null)
            {
                _recordingAdapter.SnapshotChanged -=
                    OnRecordingSnapshotChanged;
                _recordingAdapter = null;
            }
            _productAdapter = null;
            _recordingController = null;
            _frameSource.Detach();
            _nativeSession = null;
            _hotkeys?.Dispose();
            _hotkeys = null;
            _directorInput.ActivityObserved -= OnDirectorPointerActivity;
            _directorInput.Dispose();
            _cameraLogger?.Dispose();
            _cameraLogger = null;
            _followLogger?.Dispose();
            _followLogger = null;
            if (_bootstrapHwnd != nint.Zero)
            {
                _ = DestroyWindow(_bootstrapHwnd);
                _bootstrapHwnd = nint.Zero;
            }
            _gate?.WriteEvidence();
            _closeCleanupComplete = true;
            BeginInvoke(Close);
        }
    }

    protected override void WndProc(ref Message message)
    {
        _directorInput.ProcessMessage(message.Msg, message.LParam);
        if (message.Msg == HotkeyService.WmHotkey &&
            HotkeyBindings.TryResolveId(
                message.WParam.ToInt32(),
                out HotkeyBinding binding) &&
            _hotkeys?.CanDispatch(binding) == true)
        {
            _ = ProductAdapter.ExecuteManualZoomAsync(
                binding.Command == CameraCommand.ToggleStandardCloseUp
                    ? ProductReviewManualZoom.Standard
                    : ProductReviewManualZoom.Strong);
            return;
        }
        base.WndProc(ref message);
    }

    private void OnSettingsRequested(object? sender, EventArgs e) =>
        ShowSettings();

    private void OnSettingsBackRequested(object? sender, EventArgs e) =>
        ShowHome();

    private async void OnSettingsResetRequested(object? sender, EventArgs e)
    {
        if (_productAdapter is null)
        {
            return;
        }
        ProductReviewCommandResult reset =
            await _productAdapter.ResetToDefaultsAsync();
        if (reset.Succeeded)
        {
            _settingsView.ApplyPresentationDefaults();
        }
    }

    private ProductReviewCommandResult SetDirectorEnabled(bool enabled)
    {
        if (enabled && !_directorInput.Start(Handle))
        {
            return ProductReviewCommandResult.Rejected(
                $"Auto Director input registration failed: " +
                $"{_directorInput.LastWindowsError}.");
        }
        bool accepted = _cameraController.SetDirectorLiteEnabled(
            enabled,
            Stopwatch.GetTimestamp(),
            out string status);
        if (!enabled || !accepted)
        {
            _directorInput.Stop();
        }
        bool directorOwnsCamera =
            _cameraController.Owner == CameraOwner.DirectorLite;
        _hotkeys?.SetDirectorOwnsCamera(directorOwnsCamera);
        return accepted
            ? ProductReviewCommandResult.Success(status)
            : ProductReviewCommandResult.Rejected(status);
    }

    private void OnDirectorPointerActivity(RawPointerActivity activity)
    {
        if (_cameraController.Owner != CameraOwner.DirectorLite)
        {
            return;
        }
        long now = Stopwatch.GetTimestamp();
        if (activity.IsLeftButtonDown)
        {
            _cameraController.HandleDirectorLeftClick(
                ReadCameraTarget(
                    _productAdapter?.CaptureTarget ?? CaptureTarget.FullScreen),
                now,
                out _);
        }
        else
        {
            _cameraController.HandleDirectorPointerActivity(now);
        }
    }

    private static CameraPoint ReadCameraTarget(CaptureTarget target)
    {
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

    private void SetHotkeyPreviewAvailable(bool available)
    {
        if (_hotkeys is null)
        {
            return;
        }
        if (available)
        {
            _hotkeys.SetDirectorOwnsCamera(
                _cameraController.Owner == CameraOwner.DirectorLite);
            _hotkeys.SetPreviewAvailable(true);
        }
        else
        {
            _hotkeys.SetPreviewAvailable(false);
            _hotkeys.SetDirectorOwnsCamera(false);
        }
    }

    private void OnGpuStatusChanged(object? sender, EventArgs e)
    {
        WriteGpuDiagnostic(new
        {
            @event = "avalonia-gpu-interop-status",
            status = _homeView.PreviewControl.InteropStatus,
            deviceCompatibility =
                _homeView.PreviewControl.DeviceCompatibility,
            adapterLuidMatch = _homeView.PreviewControl.AdapterLuidMatch,
            error = _homeView.PreviewControl.StartupError,
            utc = DateTimeOffset.UtcNow,
        });
    }

    private void OnRecordingSnapshotChanged(
        RecordingReviewSnapshot snapshot)
    {
        WriteRecordingDiagnostic(snapshot, "state-forwarded");
        _productAdapter?.RefreshState(
            $"Recording state: {snapshot.State}.");
    }

    private void WriteRecordingDiagnostic(
        RecordingReviewSnapshot snapshot,
        string marker)
    {
        WriteGpuDiagnostic(new
        {
            @event = "production-recording-review",
            marker,
            state = snapshot.State.ToString(),
            snapshot.CommandPending,
            snapshot.SessionId,
            snapshot.OutputPath,
            snapshot.ErrorMessage,
            elapsedMilliseconds = snapshot.Elapsed.TotalMilliseconds,
            snapshot.FramesSubmitted,
            snapshot.PauseCount,
            totalPausedMilliseconds = snapshot.TotalPaused.TotalMilliseconds,
            snapshot.ActiveEncoder,
            snapshot.OutputSuccess,
            snapshot.FinalizeAttempted,
            snapshot.FinalizeHResult,
            snapshot.FinalizeCount,
            snapshot.ReadyToPublish,
            snapshot.Published,
            snapshot.PublishAttempted,
            snapshot.PublishHResult,
            snapshot.ValidationAttempted,
            snapshot.ValidationHResult,
            previewNativeIdentity = Identity(_nativeSession),
            recordingOwner =
                "PreviewLifecycleController.GetOrCreateRecordingController",
            utc = DateTimeOffset.UtcNow,
        });
    }

    private void WriteGpuDiagnostic(object entry)
    {
        if (string.IsNullOrWhiteSpace(_gpuDiagnosticPath))
        {
            return;
        }
        try
        {
            File.AppendAllText(
                _gpuDiagnosticPath,
                JsonSerializer.Serialize(entry) + Environment.NewLine,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
        catch
        {
            // Diagnostics cannot own or interrupt the presentation lifecycle.
        }
    }

    internal static string Identity(object? value) => value is null
        ? "none"
        : $"{value.GetType().Name}@" +
            $"{RuntimeHelpers.GetHashCode(value):X8}";

    private static void EnsureSucceeded(
        PreviewLifecycleResult result,
        string operation)
    {
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                $"{operation} failed: {result.Status}; {result.Error}");
        }
    }

    private static nint CreateBootstrapWindow()
    {
        nint hwnd = CreateWindowExW(
            WsExToolWindow | WsExNoActivate,
            "STATIC",
            "Legacy Review formal GPU preview bootstrap",
            WsPopup | WsDisabled,
            -32000,
            -32000,
            16,
            16,
            nint.Zero,
            nint.Zero,
            nint.Zero,
            nint.Zero);
        if (hwnd == nint.Zero)
        {
            throw new InvalidOperationException(
                $"CreateWindowExW failed: {Marshal.GetLastWin32Error()}");
        }
        return hwnd;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateWindowExW(
        uint extendedStyle,
        string className,
        string windowName,
        uint style,
        int x,
        int y,
        int width,
        int height,
        nint parent,
        nint menu,
        nint instance,
        nint parameter);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(nint hwnd);
}
