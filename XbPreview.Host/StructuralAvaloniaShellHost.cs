using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Threading;
using Avalonia.Win32.Interoperability;
using XbPreview.Avalonia.Contracts;
using XbPreview.Avalonia.Localization;
using XbPreview.Avalonia.Views;
using XbPreview.Avalonia.Views.Panels;

namespace XbPreview.Host;

/// <summary>
/// Thin WinForms owner for the structural Avalonia spike. The visible product
/// surface remains inside one Avalonia root;
/// this form only supplies the native, resizable top-level window required by
/// the already-proven preview lifecycle.
/// </summary>
internal sealed class StructuralAvaloniaShellHost : Form
{
    private const uint WsPopup = 0x8000_0000;
    private const uint WsDisabled = 0x0800_0000;
    private const uint WsExToolWindow = 0x0000_0080;
    private const uint WsExNoActivate = 0x0800_0000;
    private const int WmActivateApp = 0x001C;

    private readonly StructuralShellPerformanceGateRequest? _gateRequest;
    private readonly Func<string, IStartupSessionInspector>
        _startupInspectorFactory;
    private readonly Func<string, IUserRecoveryService> _recoveryServiceFactory;
    private readonly RecoveryFolderNavigator _recoveryFolderNavigator;
    private readonly GpuPreviewFrameSource _frameSource = new();
    private readonly RecorderCaptureVisibilityController
        _captureVisibilityController = new();
    private readonly PreviewCursorRingForm _previewCursorRing = new();
    private readonly System.Windows.Forms.Timer _previewCursorRingTimer = new()
    {
        Interval = 16,
    };
    private readonly System.Windows.Forms.Timer _recordingSnapshotTimer = new()
    {
        Interval = 100,
    };
    private readonly IDisposable _captureVisibilityRegistration;
    private readonly StructuralShellView _shellView;
    private readonly WinFormsAvaloniaControlHost _avaloniaHost;
    private readonly ProductState _productState = new();
    private readonly DirectorPanelActionAdapter _directorActionAdapter;
    private readonly Stage3DPanelActionAdapter _stage3DActionAdapter;
    private readonly Stage3DPanelBackgroundAdapter _stage3DBackgroundAdapter;
    private readonly CaptureFixedHomeAdapter _captureFixedHomeAdapter;
    private readonly DirectorFixedHomeAdapter _directorFixedHomeAdapter;
    private readonly Stage3DFixedHomeFloatingAdapter
        _stage3DFixedHomeFloatingAdapter;
    private readonly RecordingFixedHomeFloatingAdapter
        _recordingFixedHomeFloatingAdapter;
    private readonly FixedTargetCameraController _cameraController = new();
    private PreviewLifecycleController? _lifecycle;
    private RecordingController? _recordingController;
    private ProductionRecordingAdapter? _recordingAdapter;
    private RecordingFixedHomeAdapter? _recordingFixedHomeAdapter;
    private RecordingResolutionCoordinator? _recordingResolutionCoordinator;
    private StartupInspectionCoordinator? _startupInspection;
    private RecoveryActionCoordinator? _recoveryActions;
    private CaptureTargetCoordinator? _captureCoordinator;
    private Panel1PreparationAdapter? _panel1PreparationAdapter;
    private NativePreviewSession? _nativeSession;
    private CameraDiagnosticLogger? _cameraLogger;
    private ComfortZoneDiagnosticLogger? _followLogger;
    private nint _bootstrapHwnd;
    private bool _startupStarted;
    private bool _closeCleanupStarted;
    private bool _closeCleanupComplete;
    private bool _restartAfterClose;
    private bool _captureVisibilityDisposed;
    private bool _cursorPresentationDisposed;
    private bool _mouseHiddenCommandPending;
    private bool _recordingUiActive;
    private bool _recoveryPresentationDismissed;
    private string? _confirmedRecoveredSessionId;
    private StartupInspectionSnapshot _latestStartupInspectionSnapshot =
        StartupInspectionSnapshot.NotStarted;
    private readonly Dictionary<string, string> _recoveryStatusOverrides =
        new(StringComparer.Ordinal);

    private static System.Drawing.Icon LoadProductIcon()
    {
        using Stream iconStream = typeof(StructuralAvaloniaShellHost).Assembly
            .GetManifestResourceStream(
                "XbPreview.Host.Assets.XiaobaiLu.AppIcon.ico") ??
            throw new InvalidOperationException(
                "The XiaobaiLu application icon resource is missing.");
        using System.Drawing.Icon sourceIcon = new(iconStream);
        return (System.Drawing.Icon)sourceIcon.Clone();
    }

    internal StructuralAvaloniaShellHost(
        StructuralShellPerformanceGateRequest? gateRequest = null)
        : this(
            gateRequest,
            static effectiveOutputRoot =>
                NativeHistoricalSessionInspector.ForOutputRoot(
                    effectiveOutputRoot),
            static effectiveOutputRoot =>
                NativeNarrowRecoveryService.ForOutputRoot(
                    effectiveOutputRoot),
            new RecoveryFolderNavigator())
    {
    }

    internal StructuralAvaloniaShellHost(
        StructuralShellPerformanceGateRequest? gateRequest,
        Func<string, IStartupSessionInspector> startupInspectorFactory,
        Func<string, IUserRecoveryService> recoveryServiceFactory,
        RecoveryFolderNavigator recoveryFolderNavigator)
    {
        _gateRequest = gateRequest;
        _startupInspectorFactory = startupInspectorFactory ??
            throw new ArgumentNullException(nameof(startupInspectorFactory));
        _recoveryServiceFactory = recoveryServiceFactory ??
            throw new ArgumentNullException(nameof(recoveryServiceFactory));
        _recoveryFolderNavigator = recoveryFolderNavigator ??
            throw new ArgumentNullException(nameof(recoveryFolderNavigator));

        Icon = LoadProductIcon();
        Text = Strings.BrandName;
        BackColor = System.Drawing.Color.FromArgb(250, 248, 245);
        FormBorderStyle = FormBorderStyle.Sizable;
        Size = new System.Drawing.Size(910, 635);
        MinimumSize = new System.Drawing.Size(860, 600);
        StartPosition = FormStartPosition.CenterScreen;
        MinimizeBox = true;
        MaximizeBox = true;

        _shellView = new StructuralShellView(_frameSource);
        string activeUiLanguage = UiLanguage.Resolve(
            null,
            global::System.Globalization.CultureInfo.CurrentUICulture);
        string persistedUiLanguage = UiLanguage.Resolve(
            _productState.Current.UiLanguage,
            global::System.Globalization.CultureInfo.InstalledUICulture);
        _shellView.ConfigureUiLanguage(
            activeUiLanguage,
            persistedUiLanguage);
        _shellView.UiLanguageRequested += OnUiLanguageRequested;
        _shellView.RestartNowRequested += OnRestartNowRequested;
        _captureVisibilityController.StateChanged +=
            OnCaptureVisibilityStateChanged;
        _previewCursorRingTimer.Tick += OnPreviewCursorRingTimerTick;
        _recordingSnapshotTimer.Tick += OnRecordingSnapshotTimerTick;
        _shellView.RecorderOwnedPopupOpened += OnRecorderOwnedPopupOpened;
        _shellView.RecoveryTryRequested += OnRecoveryTryRequested;
        _shellView.RecoveryOpenFolderRequested +=
            OnRecoveryOpenFolderRequested;
        _shellView.RecoveryDismissReminderRequested +=
            OnRecoveryDismissReminderRequested;
        _shellView.RecoveryDismissRequested += OnRecoveryDismissRequested;
        _captureVisibilityRegistration = _captureVisibilityController
            .RegisterTopLevelWindow(
                this,
                RecorderCaptureWindowRole.MainRecorderWindow);
        _shellView.ApplyMouseHiddenPresentation(
            mouseHidden: false,
            enabled: false,
            detail: Strings.Get("CursorInitializing"));

        _avaloniaHost = new WinFormsAvaloniaControlHost
        {
            Dock = DockStyle.Fill,
            Content = _shellView,
        };
        Controls.Add(_avaloniaHost);
        _avaloniaHost.BringToFront();
        _avaloniaHost.SizeChanged += OnAvaloniaHostSizeChanged;

        _captureFixedHomeAdapter = new CaptureFixedHomeAdapter(
            this,
            _shellView,
            _captureVisibilityController);
        _directorActionAdapter = new DirectorPanelActionAdapter(
            _shellView.DirectorPresentationState,
            _cameraController,
            _productState);
        _stage3DActionAdapter = new Stage3DPanelActionAdapter(
            _shellView.Stage3DPresentationState,
            _shellView.Stage3DView,
            () => _nativeSession);
        _stage3DBackgroundAdapter = new Stage3DPanelBackgroundAdapter(
            _shellView.Stage3DBackgroundState,
            _shellView.Stage3DView,
            _productState,
            () => _nativeSession,
            PickCustomBackground);
        _directorFixedHomeAdapter = new DirectorFixedHomeAdapter(
            this,
            _shellView,
            _captureVisibilityController,
            _directorActionAdapter);
        _stage3DFixedHomeFloatingAdapter =
            new Stage3DFixedHomeFloatingAdapter(
                this,
                _shellView,
                _captureVisibilityController,
                _stage3DActionAdapter,
                _stage3DBackgroundAdapter);
        _recordingFixedHomeFloatingAdapter =
            new RecordingFixedHomeFloatingAdapter(
                this,
                _shellView,
                _captureVisibilityController);

        Shown += OnShown;
        FormClosing += OnFormClosing;
        FormClosed += OnFormClosed;
    }

    private void OnUiLanguageRequested(
        object? sender,
        StructuralUiLanguageRequestedEventArgs e) =>
        e.Persisted = _productState.TrySetUiLanguage(e.Language);

    private void OnRestartNowRequested(object? sender, EventArgs e)
    {
        if (_recordingUiActive || _closeCleanupStarted ||
            IsDisposed || Disposing)
        {
            return;
        }

        _restartAfterClose = true;
        Close();
    }

    private void OnFormClosed(object? sender, FormClosedEventArgs e)
    {
        if (!_restartAfterClose || Environment.ExitCode != 0)
        {
            return;
        }

        _restartAfterClose = false;
        try
        {
            Process.Start(UiRestartContract.CreateRelaunchStartInfo(
                System.Windows.Forms.Application.ExecutablePath,
                AppContext.BaseDirectory));
        }
        catch
        {
            Environment.ExitCode = 1;
        }
    }

    internal StructuralShellView ShellView => _shellView;

    internal WinFormsAvaloniaControlHost AvaloniaHost => _avaloniaHost;

    internal GpuPreviewFrameSource FrameSource => _frameSource;

    internal RecorderCaptureVisibilityController CaptureVisibilityController =>
        _captureVisibilityController;

    internal DirectorFixedHomeAdapter DirectorFixedHomeAdapter =>
        _directorFixedHomeAdapter;

    internal CaptureFixedHomeAdapter CaptureFixedHomeAdapter =>
        _captureFixedHomeAdapter;

    internal Stage3DFixedHomeFloatingAdapter Stage3DFixedHomeFloatingAdapter =>
        _stage3DFixedHomeFloatingAdapter;

    internal RecordingFixedHomeFloatingAdapter
        RecordingFixedHomeFloatingAdapter =>
            _recordingFixedHomeFloatingAdapter;

    internal NativePreviewSession NativeSession => _nativeSession ??
        throw new InvalidOperationException("Native preview session is unavailable.");

    internal PreviewLifecycleController Lifecycle => _lifecycle ??
        throw new InvalidOperationException("Preview lifecycle is unavailable.");

    internal RecordingController RecordingController =>
        _recordingController ?? throw new InvalidOperationException(
            "Recording controller is unavailable.");

    internal ProductionRecordingAdapter RecordingAdapter =>
        _recordingAdapter ?? throw new InvalidOperationException(
            "Recording adapter is unavailable.");

    internal RecordingFixedHomeAdapter RecordingFixedHomeAdapter =>
        _recordingFixedHomeAdapter ?? throw new InvalidOperationException(
            "Panel 4 recording adapter is unavailable.");

    internal string CompositionMode =>
        nameof(Win32CompositionMode.LowLatencyDxgiSwapChain);

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == WmActivateApp && message.WParam != nint.Zero)
        {
            RefreshMicrophonesAfterExternalActivation();
        }
        if (_directorActionAdapter is { } adapter &&
            adapter.ProcessWindowMessage(
                message.Msg,
                message.WParam,
                message.LParam))
        {
            return;
        }
        base.WndProc(ref message);
    }

    private void OnAvaloniaHostSizeChanged(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(
            _shellView.InvalidateVisual,
            DispatcherPriority.Render);
    }

    private void SetDirectorPreviewAvailable(bool available)
    {
        bool changesPresentation =
            _recordingFixedHomeAdapter?
                .IsPassiveIdleConfigurationPending != true;
        SetDirectorPreviewAvailable(available, changesPresentation);
    }

    private void SetDirectorPreviewAvailable(
        bool available,
        bool changesPresentation)
    {
        if (IsDisposed || Disposing || !IsHandleCreated)
        {
            return;
        }
        if (InvokeRequired)
        {
            try
            {
                BeginInvoke((Action)(() =>
                    SetDirectorPreviewAvailable(
                        available,
                        changesPresentation)));
            }
            catch (InvalidOperationException)
            {
            }
            return;
        }

        _directorActionAdapter.SetPreviewAvailable(
            available,
            changesPresentation);
        SetStage3DPoseActionsAvailable(available, changesPresentation);
        _stage3DBackgroundAdapter.SetActionsEnabled(
            available && !_recordingUiActive,
            changesPresentation);
    }

    private void SetStage3DPoseActionsAvailable(
        bool previewAvailable,
        bool changesPresentation)
    {
        bool recordingAllowsPoseActions =
            Stage3DPanelActionController.RecordingAllowsPoseActions(
                _recordingFixedHomeAdapter?.CurrentState);
        (bool executionAllowed, bool effectivePresentationChange) =
            ResolveStage3DPoseAvailability(
                previewAvailable,
                recordingAllowsPoseActions,
                changesPresentation);
        _stage3DActionAdapter.SetActionsEnabled(
            executionAllowed,
            effectivePresentationChange);
    }

    internal static (bool ExecutionAllowed, bool ChangesPresentation)
        ResolveStage3DPoseAvailability(
            bool previewAvailable,
            bool recordingAllowsPoseActions,
            bool changesPresentation)
    {
        bool executionAllowed =
            previewAvailable && recordingAllowsPoseActions;
        bool preserveRecordingTransitionPresentation =
            previewAvailable && !recordingAllowsPoseActions;
        return (
            executionAllowed,
            changesPresentation && !preserveRecordingTransitionPresentation);
    }

    private async Task PrepareRecordingStartAsync()
    {
        if (_closeCleanupStarted)
        {
            throw new InvalidOperationException(
                Strings.Get("ShellClosingCannotStart"));
        }

        Panel1PreparationAdapter preparation =
            _panel1PreparationAdapter ?? throw new InvalidOperationException(
                "Panel 1 preparation runtime is unavailable.");

        RecorderCaptureVisibilityResult startingPolicy =
            _captureVisibilityController.TrySetRecordingPhase(
                RecorderCapturePhase.Starting);
        if (!startingPolicy.Succeeded)
        {
            preparation.CancelRecordingStart(
                _recordingAdapter?.CurrentSnapshot ??
                    RecordingReviewSnapshot.Idle);
            throw new InvalidOperationException(
                Strings.Format("RecorderExcludeFailed", startingPolicy.Failure,
                    $"Win32={startingPolicy.WindowsErrorCode}"));
        }

        try
        {
            await preparation.PrepareRecordingStartAsync()
                .ConfigureAwait(true);
        }
        catch
        {
            preparation.CancelRecordingStart(
                _recordingAdapter?.CurrentSnapshot ??
                    RecordingReviewSnapshot.Idle);
            throw;
        }

        RecorderCaptureVisibilityResult capturePolicy =
            _captureVisibilityController.TryRefreshTopLevelWindows();
        if (!capturePolicy.Succeeded)
        {
            preparation.CancelRecordingStart(
                _recordingAdapter?.CurrentSnapshot ??
                    RecordingReviewSnapshot.Idle);
            throw new InvalidOperationException(
                Strings.Format("TrayValidationFailed", capturePolicy.Failure,
                    $"Win32={capturePolicy.WindowsErrorCode}"));
        }

        _recordingUiActive = true;
        UpdateStage3DActionsPresentation();
    }

    private void OnRecordingSnapshotTimerTick(object? sender, EventArgs e)
    {
        if (_closeCleanupStarted || _recordingAdapter is null)
        {
            return;
        }
        try
        {
            _ = _recordingAdapter.RefreshSnapshot();
        }
        catch (Exception error)
        {
            _recordingFixedHomeAdapter?.ReportActionError(
                Strings.Format("ShellRecordingStateFailed", error.Message));
        }
    }

    private void OnRecordingSnapshotChanged(RecordingReviewSnapshot snapshot)
    {
        _panel1PreparationAdapter?.ApplyRecordingSnapshot(snapshot);
    }

    private void OnRecordingPresentationChanged(
        RecordingPanelPresentationState state)
    {
        if (_closeCleanupStarted || IsDisposed || Disposing)
        {
            return;
        }
        if (InvokeRequired)
        {
            try
            {
                BeginInvoke((Action)(() =>
                    OnRecordingPresentationChanged(state)));
            }
            catch (InvalidOperationException) when (
                _closeCleanupStarted || IsDisposed || Disposing)
            {
            }
            return;
        }

        _ = _captureVisibilityController.TrySetRecordingPhase(
            GetCapturePhase(state));
        _recordingUiActive = state.CommandPending || state.RecordingState is
            RecordingReviewState.Starting or
            RecordingReviewState.Recording or
            RecordingReviewState.Paused or
            RecordingReviewState.Stopping;
        _shellView.ApplyLanguageRecordingState(
            state.RecordingState,
            state.CommandPending);
        UpdateRecordingMutablePresentation();
    }

    private static RecorderCapturePhase GetCapturePhase(
        RecordingPanelPresentationState state)
    {
        if (state.IdlePresentationVisible)
        {
            return RecorderCapturePhase.Idle;
        }

        return state.RecordingState switch
        {
            RecordingReviewState.Starting => RecorderCapturePhase.Starting,
            RecordingReviewState.Recording => RecorderCapturePhase.Recording,
            RecordingReviewState.Paused => RecorderCapturePhase.Paused,
            RecordingReviewState.Stopping => RecorderCapturePhase.Stopping,
            _ => RecorderCapturePhase.Unstable,
        };
    }

    private void UpdateRecordingMutablePresentation()
    {
        UpdateStage3DActionsPresentation();
    }

    private void UpdateStage3DActionsPresentation()
    {
        bool available = _nativeSession is not null && !_closeCleanupStarted;
        SetStage3DPoseActionsAvailable(
            available,
            changesPresentation: true);
        _stage3DBackgroundAdapter.SetActionsEnabled(
            available && !_recordingUiActive);
    }

    private void RefreshMicrophonesAfterExternalActivation()
    {
        Panel1PreparationAdapter? preparation = _panel1PreparationAdapter;
        if (_closeCleanupStarted ||
            preparation is null ||
            !preparation.CurrentSnapshot.AudioControlsEnabled)
        {
            return;
        }

        _ = preparation.RefreshMicrophonesAsync(
            MicrophoneRefreshReason.PassiveLifecycle);
    }

    private string? PickCustomBackground()
    {
        if (_recordingUiActive || _nativeSession is null)
        {
            return null;
        }

        using OpenFileDialog dialog = new()
        {
            Title = Strings.Get("PickBackgroundTitle"),
            Filter =
                Strings.Get("ImageFiles") + "|" +
                "*.png;*.jpg;*.jpeg;*.bmp",
            CheckFileExists = true,
            CheckPathExists = true,
            Multiselect = false,
            RestoreDirectory = true,
        };
        return dialog.ShowDialog(this) == DialogResult.OK
            ? dialog.FileName
            : null;
    }

    private void OnRecorderOwnedPopupOpened(object? sender, EventArgs e)
    {
        _ = _captureVisibilityController.TryRefreshTopLevelWindows();
    }

    private async Task<Panel1MouseHiddenRuntimeResult>
        SetMouseHiddenRuntimeAsync(
            bool requested,
            bool previousMouseHidden)
    {
        PreviewLifecycleController? lifecycle = _lifecycle;
        if (_mouseHiddenCommandPending || lifecycle is null)
        {
            return new Panel1MouseHiddenRuntimeResult(
                false,
                previousMouseHidden,
                Strings.Get("CursorRuntimeUnavailable"));
        }

        _mouseHiddenCommandPending = true;
        try
        {
            if (requested)
            {
                OperatorRingActivationResult previewRing =
                    _previewCursorRing.VerifyCaptureExclusion();
                if (!previewRing.Succeeded)
                {
                    SetPreviewCursorRingEnabled(previousMouseHidden);
                    return new Panel1MouseHiddenRuntimeResult(
                        false,
                        previousMouseHidden,
                        Strings.Format("CursorGuideExcludeFailed",
                            $"Win32={previewRing.WindowsErrorCode}; " +
                            $"Affinity=0x{previewRing.AppliedAffinity:X8}"));
                }
            }

            PreviewLifecycleResult result =
                await lifecycle.SetRecordCursorVisibleAsync(
                    visible: !requested);
            if (!result.Succeeded)
            {
                SetPreviewCursorRingEnabled(previousMouseHidden);
                return new Panel1MouseHiddenRuntimeResult(
                    false,
                    previousMouseHidden,
                    Strings.Format("CursorRuntimeFailed", result.Status,
                        result.Error));
            }

            SetPreviewCursorRingEnabled(requested);
            return new Panel1MouseHiddenRuntimeResult(
                true,
                requested,
                requested
                    ? Strings.Get("CursorHiddenOn")
                    : Strings.Get("CursorHiddenOff"));
        }
        catch (Exception error)
        {
            SetPreviewCursorRingEnabled(previousMouseHidden);
            return new Panel1MouseHiddenRuntimeResult(
                false,
                previousMouseHidden,
                Strings.Format("CursorCommandFailed", error.Message));
        }
        finally
        {
            _mouseHiddenCommandPending = false;
        }
    }

    private void SetPreviewCursorRingEnabled(bool enabled)
    {
        if (enabled && !_closeCleanupStarted)
        {
            UpdatePreviewCursorRing();
            _previewCursorRingTimer.Start();
            return;
        }

        _previewCursorRingTimer.Stop();
        PublishPreviewCursorRing(visible: false, 0.0, 0.0);
    }

    private void OnPreviewCursorRingTimerTick(object? sender, EventArgs e) =>
        UpdatePreviewCursorRing();

    private void UpdatePreviewCursorRing()
    {
        PreviewLifecycleController? lifecycle = _lifecycle;
        if (_closeCleanupStarted || lifecycle is null ||
            !TryReadActiveCursorPoint(lifecycle, out CameraPoint point) ||
            !lifecycle.TryReadStats(
                out NativeMethods.PreviewStats stats,
                out _,
                out _) ||
            !TryMapCursorToPreview(point, stats, out CameraPoint previewPoint))
        {
            PublishPreviewCursorRing(visible: false, 0.0, 0.0);
            return;
        }

        PublishPreviewCursorRing(
            visible: true,
            previewPoint.X,
            previewPoint.Y);
    }

    private static bool TryReadActiveCursorPoint(
        PreviewLifecycleController lifecycle,
        out CameraPoint point)
    {
        CaptureTarget target = lifecycle.CurrentCaptureTarget;
        if (target.IsWindow)
        {
            return WindowCaptureSelector.TryMapCurrentCursor(
                target.WindowHandle,
                out point);
        }

        CameraCursorObservation observation =
            CameraCursorTarget.ReadPrimaryMonitorObservation();
        point = observation.Normalized;
        return observation.GetCursorPosResult && observation.InsidePrimaryMonitor;
    }

    private static bool TryMapCursorToPreview(
        CameraPoint sourcePoint,
        NativeMethods.PreviewStats stats,
        out CameraPoint previewPoint)
    {
        previewPoint = default;
        if (stats.CaptureWidth == 0 || stats.CaptureHeight == 0 ||
            stats.PreviewWidth == 0 || stats.PreviewHeight == 0)
        {
            return false;
        }

        double zoom = stats.NativeCameraEnabled != 0 &&
            double.IsFinite(stats.NativeAppliedZoom) &&
            stats.NativeAppliedZoom >= 1.0
                ? stats.NativeAppliedZoom
                : 1.0;
        double viewWidth = 1.0 / zoom;
        double viewHeight = 1.0 / zoom;
        double centerX = double.IsFinite(stats.NativeAppliedCenterX)
            ? stats.NativeAppliedCenterX
            : 0.5;
        double centerY = double.IsFinite(stats.NativeAppliedCenterY)
            ? stats.NativeAppliedCenterY
            : 0.5;
        double viewLeft = Math.Clamp(
            centerX - viewWidth / 2.0,
            0.0,
            1.0 - viewWidth);
        double viewTop = Math.Clamp(
            centerY - viewHeight / 2.0,
            0.0,
            1.0 - viewHeight);
        double cameraX = (sourcePoint.X - viewLeft) / viewWidth;
        double cameraY = (sourcePoint.Y - viewTop) / viewHeight;
        if (!double.IsFinite(cameraX) || !double.IsFinite(cameraY) ||
            cameraX < 0.0 || cameraX > 1.0 ||
            cameraY < 0.0 || cameraY > 1.0)
        {
            return false;
        }

        NativeMethods.Result result = NativeMethods.XbPreview_CalculateLetterbox(
            stats.CaptureWidth,
            stats.CaptureHeight,
            stats.PreviewWidth,
            stats.PreviewHeight,
            out NativeMethods.LetterboxRect letterbox);
        if (result != NativeMethods.Result.Ok ||
            letterbox.Width <= 0.0f || letterbox.Height <= 0.0f)
        {
            return false;
        }

        previewPoint = new CameraPoint(
            (letterbox.X + cameraX * letterbox.Width) / stats.PreviewWidth,
            (letterbox.Y + cameraY * letterbox.Height) / stats.PreviewHeight);
        return double.IsFinite(previewPoint.X) &&
            double.IsFinite(previewPoint.Y) &&
            previewPoint.X >= 0.0 && previewPoint.X <= 1.0 &&
            previewPoint.Y >= 0.0 && previewPoint.Y <= 1.0;
    }

    private void PublishPreviewCursorRing(
        bool visible,
        double normalizedX,
        double normalizedY)
    {
        void Apply()
        {
            if (!visible ||
                !_shellView.TryMapPreviewPointToScreen(
                    normalizedX,
                    normalizedY,
                    out PixelPoint screenPoint))
            {
                _previewCursorRing.HideRing();
                return;
            }

            OperatorRingActivationResult result = _previewCursorRing.ShowAt(
                this,
                new System.Drawing.Point(screenPoint.X, screenPoint.Y));
            if (!result.Succeeded)
            {
                _previewCursorRing.HideRing();
            }
        }
        if (Dispatcher.UIThread.CheckAccess())
        {
            Apply();
        }
        else
        {
            Dispatcher.UIThread.Post(Apply, DispatcherPriority.Render);
        }
    }

    private void OnCaptureVisibilityStateChanged(
        object? sender,
        EventArgs e)
    {
        if (_previewCursorRingTimer.Enabled)
        {
            _ = _previewCursorRing.VerifyCaptureExclusion(force: true);
        }
    }

    private static async Task ObserveStartupInspectionAsync(
        Task<StartupInspectionSnapshot> task)
    {
        _ = await task.ConfigureAwait(false);
        // The mature coordinator converts every outcome into an immutable
        // terminal snapshot. This observer keeps historical disk/native work
        // detached from Preview startup and the Avalonia UI thread.
    }

    private void TryScheduleStartupInspection(string effectiveOutputRoot)
    {
        if (_startupInspection is not null || _closeCleanupStarted)
        {
            return;
        }

        StartupInspectionCoordinator? startupInspection = null;
        try
        {
            startupInspection = new StartupInspectionCoordinator(
                _startupInspectorFactory(effectiveOutputRoot));
            startupInspection.SnapshotChanged +=
                OnStartupInspectionSnapshotChanged;
            _startupInspection = startupInspection;
            _ = ObserveStartupInspectionAsync(startupInspection.StartAsync());
        }
        catch (Exception error)
        {
            if (startupInspection is not null)
            {
                startupInspection.SnapshotChanged -=
                    OnStartupInspectionSnapshotChanged;
                startupInspection.RequestCancellation();
                _ = DisposeFailedStartupInspectionAsync(startupInspection);
            }
            _startupInspection = null;
            Debug.WriteLine(
                $"Formal startup recovery inspection setup failed: {error}");
            return;
        }

        try
        {
            RecoveryActionCoordinator recoveryActions = new(
                _recoveryServiceFactory(effectiveOutputRoot),
                _startupInspectorFactory(effectiveOutputRoot));
            recoveryActions.SnapshotChanged +=
                OnRecoveryAttemptSnapshotChanged;
            _recoveryActions = recoveryActions;
        }
        catch (Exception error)
        {
            Debug.WriteLine(
                $"Formal explicit recovery setup failed: {error}");
        }
    }

    private static async Task DisposeFailedStartupInspectionAsync(
        StartupInspectionCoordinator startupInspection)
    {
        try
        {
            await startupInspection.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception error)
        {
            Debug.WriteLine(
                $"Formal startup recovery inspection cleanup failed: {error}");
        }
    }

    private void OnStartupInspectionSnapshotChanged(
        StartupInspectionCoordinator source,
        StartupInspectionSnapshot snapshot)
    {
        if (!CanDeliverStartupInspection(source, snapshot))
        {
            return;
        }
        try
        {
            BeginInvoke(() =>
            {
                if (CanDeliverStartupInspection(source, snapshot))
                {
                    _latestStartupInspectionSnapshot = snapshot;
                    RenderRecoveryPresentation();
                }
            });
        }
        catch (InvalidOperationException) when (
            IsDisposed || Disposing || _closeCleanupStarted)
        {
        }
    }

    private bool CanDeliverStartupInspection(
        StartupInspectionCoordinator? source,
        StartupInspectionSnapshot snapshot) =>
        source is not null &&
        ReferenceEquals(_startupInspection, source) &&
        source.CurrentSnapshot == snapshot &&
        !_closeCleanupStarted && !IsDisposed && !Disposing;

    private void RenderRecoveryPresentation()
    {
        UserRecoveryPresentation source = UserRecoveryPresentation.Create(
            _latestStartupInspectionSnapshot,
            _confirmedRecoveredSessionId,
            _recoveryStatusOverrides,
            _productState.Current.RecoveryDismissedSessionIds);
        FormalRecoveryBannerPresentation banner =
            FormalRecoveryBannerPresentation.Create(
                source,
                _recoveryActions?.CurrentSnapshot ??
                    RecoveryAttemptSnapshot.NotStarted,
                _recoveryPresentationDismissed);
        if (!banner.Visible)
        {
            _shellView.ApplyRecoveryPresentation(null);
            return;
        }

        _shellView.ApplyRecoveryPresentation(
            new StructuralRecoveryBannerPresentation(
                banner.NoticeText,
                banner.Candidates.Select(candidate =>
                    new StructuralRecoveryCandidatePresentation(
                        candidate.Candidate.SessionId,
                        candidate.Candidate.Title,
                        candidate.Candidate.StatusText,
                        candidate.Candidate.DisplaySafePath,
                        candidate.ShowTryRecovery,
                        candidate.RecoveryRunning,
                        candidate.CanOpenFolder)).ToArray()));
    }

    private UserRecoveryCandidate? FindPresentedCandidate(string sessionId) =>
        UserRecoveryPresentation.Create(
                _latestStartupInspectionSnapshot,
                _confirmedRecoveredSessionId,
                _recoveryStatusOverrides,
                _productState.Current.RecoveryDismissedSessionIds)
            .Candidates.FirstOrDefault(candidate => string.Equals(
                candidate.SessionId,
                sessionId,
                StringComparison.Ordinal));

    private async void OnRecoveryTryRequested(
        object? sender,
        StructuralRecoveryCandidateEventArgs e)
    {
        if (_closeCleanupStarted || _recoveryPresentationDismissed ||
            _recoveryActions is null ||
            FindPresentedCandidate(e.SessionId) is not
                { CanTryRecovery: true } candidate)
        {
            return;
        }
        try
        {
            _ = await _recoveryActions.StartAsync(candidate);
        }
        catch (Exception error)
        {
            if (!_closeCleanupStarted)
            {
                _recoveryStatusOverrides[candidate.SessionId] =
                    Strings.Get("RecoveryReadFailureStatus");
                RenderRecoveryPresentation();
            }
            Debug.WriteLine($"Formal explicit recovery request failed: {error}");
        }
    }

    private void OnRecoveryOpenFolderRequested(
        object? sender,
        StructuralRecoveryCandidateEventArgs e)
    {
        UserRecoveryCandidate? candidate = FindPresentedCandidate(e.SessionId);
        if (_closeCleanupStarted || candidate is null ||
            string.IsNullOrWhiteSpace(candidate.DisplaySafePath))
        {
            return;
        }
        try
        {
            _recoveryFolderNavigator.OpenContainingFolder(
                candidate.DisplaySafePath);
        }
        catch (Exception error)
        {
            Debug.WriteLine($"Open recovery candidate folder failed: {error}");
        }
    }

    private void OnRecoveryDismissRequested(object? sender, EventArgs e)
    {
        _recoveryPresentationDismissed = true;
        RenderRecoveryPresentation();
    }

    private void OnRecoveryDismissReminderRequested(
        object? sender,
        StructuralRecoveryCandidateEventArgs e)
    {
        if (_closeCleanupStarted || FindPresentedCandidate(e.SessionId) is null)
        {
            return;
        }
        if (_productState.TryDismissRecoveryReminder(e.SessionId))
        {
            _recoveryStatusOverrides.Remove(e.SessionId);
            RenderRecoveryPresentation();
        }
    }

    private void OnRecoveryAttemptSnapshotChanged(
        RecoveryActionCoordinator source,
        RecoveryAttemptSnapshot snapshot)
    {
        if (!CanDeliverRecoveryAttempt(source, snapshot))
        {
            return;
        }
        try
        {
            BeginInvoke(() =>
            {
                if (CanDeliverRecoveryAttempt(source, snapshot))
                {
                    RecordRecoveryAttempt(snapshot);
                }
            });
        }
        catch (InvalidOperationException) when (
            IsDisposed || Disposing || _closeCleanupStarted)
        {
        }
    }

    private bool CanDeliverRecoveryAttempt(
        RecoveryActionCoordinator? source,
        RecoveryAttemptSnapshot snapshot) =>
        source is not null &&
        ReferenceEquals(_recoveryActions, source) &&
        source.CurrentSnapshot == snapshot &&
        !_closeCleanupStarted && !IsDisposed && !Disposing;

    private void RecordRecoveryAttempt(RecoveryAttemptSnapshot snapshot)
    {
        if (snapshot.State == RecoveryAttemptState.Running)
        {
            _recoveryStatusOverrides[snapshot.SessionId] = snapshot.UserMessage;
        }
        else if (snapshot.ConfirmedRecovered && snapshot.RescanResult is not null)
        {
            _confirmedRecoveredSessionId = snapshot.SessionId;
            _recoveryStatusOverrides.Remove(snapshot.SessionId);
            _latestStartupInspectionSnapshot = new StartupInspectionSnapshot(
                _latestStartupInspectionSnapshot.Generation + 1,
                StartupInspectionState.Completed,
                snapshot.RescanResult,
                null);
        }
        else if (!string.IsNullOrEmpty(snapshot.SessionId))
        {
            _recoveryStatusOverrides[snapshot.SessionId] = snapshot.UserMessage;
        }
        RenderRecoveryPresentation();
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
                Path.Combine(
                    Environment.GetFolderPath(
                        Environment.SpecialFolder.LocalApplicationData),
                    "XbPreview",
                    "bin",
                    "Release",
                    "x64",
                    "diagnostic-logs");
            Directory.CreateDirectory(logDirectory);

            _bootstrapHwnd = CreateBootstrapWindow();
            _cameraLogger = new CameraDiagnosticLogger(logDirectory);
            _followLogger = new ComfortZoneDiagnosticLogger(logDirectory);
            PreviewLifecycleController lifecycle = new(
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
                SetDirectorPreviewAvailable,
                (state, result, detail) =>
                    _cameraLogger?.Write(state, result, detail: detail));
            _lifecycle = lifecycle;

            PreviewLifecycleResult initialize = await lifecycle.InitializeAsync();
            if (_closeCleanupStarted)
            {
                return;
            }
            EnsureSucceeded(initialize, "Initialize preview lifecycle");

            PreviewLifecycleResult resize =
                await lifecycle.RequestResizeAsync(16, 16);
            if (_closeCleanupStarted)
            {
                return;
            }
            EnsureSucceeded(resize, "Resize hidden bootstrap surface");

            CaptureDisplaySnapshot display =
                new DisplayGeometryProvider().ReadPrimaryDisplay();
            SessionGeometry startupGeometry =
                RecordingResolutionPolicy.CreatePlan(
                    _productState.Current.RecordingResolutionMode,
                    SessionGeometry.CreateFullScreen(display)).Geometry;
            PreviewLifecycleResult geometry =
                await lifecycle.SetDesiredGeometryAsync(startupGeometry);
            if (_closeCleanupStarted)
            {
                return;
            }
            EnsureSucceeded(geometry, "Set full-screen preview geometry");

            PreviewLifecycleResult start =
                await lifecycle.StartAsync(
                    cameraEnabled: true,
                    followEnabled: false,
                    NativeMethods.CursorMode.SystemCursor);
            if (_closeCleanupStarted)
            {
                return;
            }
            EnsureSucceeded(start, "Start production preview lifecycle");

            string safeDefaultOutputRoot =
                ResolveFrozenDefaultOutputRoot(logDirectory);
            _recordingController = lifecycle.GetOrCreateRecordingController();
            _captureCoordinator = new CaptureTargetCoordinator(
                lifecycle,
                _cameraController,
                _productState,
                () => _closeCleanupStarted);
            Panel1PreparationAdapter preparation = new(
                _captureCoordinator,
                _recordingController,
                SetMouseHiddenRuntimeAsync);
            _panel1PreparationAdapter = preparation;
            await preparation.InitializeAsync();
            if (_closeCleanupStarted)
            {
                return;
            }
            _recordingAdapter = new ProductionRecordingAdapter(
                _recordingController,
                PrepareRecordingStartAsync);
            _recordingResolutionCoordinator =
                new RecordingResolutionCoordinator(
                    _lifecycle,
                    _productState,
                    _recordingController);
            _recordingFixedHomeAdapter = new RecordingFixedHomeAdapter(
                this,
                _recordingAdapter,
                _recordingController,
                NativeSession,
                _productState,
                _recordingResolutionCoordinator,
                _captureVisibilityController,
                safeDefaultOutputRoot);
            TryScheduleStartupInspection(
                _recordingFixedHomeAdapter.CanonicalOutputRoot);
            _recordingAdapter.SnapshotChanged += OnRecordingSnapshotChanged;
            _recordingFixedHomeAdapter.StateChanged +=
                OnRecordingPresentationChanged;
            OnRecordingPresentationChanged(
                _recordingFixedHomeAdapter.CurrentState);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (_closeCleanupStarted)
                {
                    return;
                }
                _recordingFixedHomeFloatingAdapter.AttachController(
                    _recordingFixedHomeAdapter);
                _shellView.AttachPanel1PreparationController(
                    preparation);
                _captureFixedHomeAdapter.AttachPreparationController(
                    preparation);
            });
            if (_closeCleanupStarted)
            {
                return;
            }
            _recordingSnapshotTimer.Start();
            _directorActionAdapter.Initialize(Handle, Lifecycle);
            _stage3DActionAdapter.SetActionsEnabled(true);
            EnsureNativeSucceeded(
                _stage3DBackgroundAdapter.Initialize(actionsEnabled: true),
                "Initialize Panel 3 background");

            if (_gateRequest is not null)
            {
                await new StructuralShellPerformanceGate(_gateRequest)
                    .RunAsync(this);
                Close();
            }
        }
        catch (Exception error)
        {
            Text = $"{Strings.BrandName} - ATTENTION";
            if (_gateRequest is not null)
            {
                StructuralShellPerformanceGate.WriteStartupFailure(
                    _gateRequest,
                    error);
            }
            Environment.ExitCode = 1;
            Close();
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
        _recordingSnapshotTimer.Stop();
        SetPreviewCursorRingEnabled(false);

        try
        {
            await DisposeRecoveryAsync();
            _captureFixedHomeAdapter.Dispose();
            _directorFixedHomeAdapter.Dispose();
            _stage3DFixedHomeFloatingAdapter.Dispose();
            _recordingFixedHomeFloatingAdapter.Dispose();
            _directorActionAdapter.Dispose();
            _stage3DActionAdapter.Dispose();
            _stage3DBackgroundAdapter.Dispose();
            _shellView.RecoveryTryRequested -= OnRecoveryTryRequested;
            _shellView.RecoveryOpenFolderRequested -=
                OnRecoveryOpenFolderRequested;
            _shellView.RecoveryDismissReminderRequested -=
                OnRecoveryDismissReminderRequested;
            _shellView.RecoveryDismissRequested -= OnRecoveryDismissRequested;
            _shellView.UiLanguageRequested -= OnUiLanguageRequested;
            _shellView.RestartNowRequested -= OnRestartNowRequested;
            _panel1PreparationAdapter?.Dispose();
            // Borrower-first shutdown: stop composition callbacks and release
            // imported shared textures before the native renderer owner exits.
            await _shellView.ShutdownPreviewAsync();
            if (_recordingController is not null)
            {
                _ = await _recordingController.StopForCloseAsync();
            }
            if (_lifecycle is not null)
            {
                await _lifecycle.DisposeAsync();
                _lifecycle = null;
            }
        }
        catch (Exception error)
        {
            if (_gateRequest is not null)
            {
                StructuralShellPerformanceGate.WriteStartupFailure(
                    _gateRequest,
                    error);
            }
            Environment.ExitCode = 1;
        }
        finally
        {
            _panel1PreparationAdapter?.Dispose();
            if (_recordingAdapter is not null)
            {
                _recordingAdapter.SnapshotChanged -=
                    OnRecordingSnapshotChanged;
                _recordingAdapter = null;
            }
            _recordingController = null;
            _frameSource.Detach();
            _panel1PreparationAdapter = null;
            _captureCoordinator = null;
            _nativeSession = null;
            _cameraLogger?.Dispose();
            _cameraLogger = null;
            _followLogger?.Dispose();
            _followLogger = null;
            if (_bootstrapHwnd != nint.Zero)
            {
                _ = DestroyWindow(_bootstrapHwnd);
                _bootstrapHwnd = nint.Zero;
            }
            DisposeCursorPresentation();
            _captureFixedHomeAdapter.Dispose();
            _directorFixedHomeAdapter.Dispose();
            _stage3DFixedHomeFloatingAdapter.Dispose();
            _recordingFixedHomeFloatingAdapter.Dispose();
            _directorActionAdapter.Dispose();
            _stage3DActionAdapter.Dispose();
            _stage3DBackgroundAdapter.Dispose();
            DisposeRecordingPresentation();
            DisposeCaptureVisibility();
            _closeCleanupComplete = true;
            BeginInvoke(Close);
        }
    }

    private async Task DisposeRecoveryAsync()
    {
        RecoveryActionCoordinator? recoveryActions = _recoveryActions;
        if (recoveryActions is not null)
        {
            recoveryActions.SnapshotChanged -=
                OnRecoveryAttemptSnapshotChanged;
            await recoveryActions.CancelAndWaitAsync();
            await recoveryActions.DisposeAsync();
            _recoveryActions = null;
        }

        StartupInspectionCoordinator? startupInspection = _startupInspection;
        if (startupInspection is not null)
        {
            startupInspection.SnapshotChanged -=
                OnStartupInspectionSnapshotChanged;
            await startupInspection.CancelAndWaitAsync();
            await startupInspection.DisposeAsync();
            _startupInspection = null;
        }
    }

    private void DisposeCaptureVisibility()
    {
        if (_captureVisibilityDisposed)
        {
            return;
        }

        _captureVisibilityDisposed = true;
        _shellView.RecorderOwnedPopupOpened -= OnRecorderOwnedPopupOpened;
        _captureVisibilityController.StateChanged -=
            OnCaptureVisibilityStateChanged;
        _captureVisibilityRegistration.Dispose();
        _captureVisibilityController.Dispose();
    }

    private void DisposeRecordingPresentation()
    {
        if (_recordingFixedHomeAdapter is not null)
        {
            _recordingFixedHomeAdapter.StateChanged -=
                OnRecordingPresentationChanged;
            _recordingFixedHomeAdapter.Dispose();
            _recordingFixedHomeAdapter = null;
        }
        _recordingResolutionCoordinator?.Dispose();
        _recordingResolutionCoordinator = null;
        _recordingSnapshotTimer.Tick -= OnRecordingSnapshotTimerTick;
        _recordingSnapshotTimer.Stop();
        _recordingSnapshotTimer.Dispose();
    }

    private void DisposeCursorPresentation()
    {
        if (_cursorPresentationDisposed)
        {
            return;
        }

        _cursorPresentationDisposed = true;
        _previewCursorRingTimer.Tick -= OnPreviewCursorRingTimerTick;
        _previewCursorRingTimer.Stop();
        _previewCursorRingTimer.Dispose();
        _previewCursorRing.HideRing();
        _previewCursorRing.Dispose();
    }

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

    private static void EnsureNativeSucceeded(
        NativeMethods.Result result,
        string operation)
    {
        if (result != NativeMethods.Result.Ok)
        {
            throw new InvalidOperationException(
                $"{operation} failed: {result}.");
        }
    }

    private static string ResolveFrozenDefaultOutputRoot(
        string diagnosticDirectory) => Path.GetFullPath(Path.Combine(
            diagnosticDirectory,
            "..",
            "..",
            "..",
            "..",
            "p2.5a-recordings"));

    private static nint CreateBootstrapWindow()
    {
        nint hwnd = CreateWindowExW(
            WsExToolWindow | WsExNoActivate,
            "STATIC",
            "Xiaobai structural GPU preview bootstrap",
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
