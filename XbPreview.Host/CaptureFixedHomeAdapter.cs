using System.Diagnostics;
using Avalonia;
using XbPreview.Avalonia.Views;
using XbPreview.Avalonia.Views.Panels;

namespace XbPreview.Host;

internal enum CaptureReturnHomeSource
{
    DragHome = 0,
    PlaceholderButton = 1,
    FloatingClose = 2,
    OwnerClosing = 3,
}
/// <summary>
/// The deliberately narrow two-state adapter for Capture / Slot 1. It owns
/// no general docking graph and never positions a window from pointer moves.
/// </summary>
internal sealed class CaptureFixedHomeAdapter : IDisposable
{
    private readonly StructuralAvaloniaShellHost _owner;
    private readonly StructuralShellView _shellView;
    private readonly RecorderCaptureVisibilityController
        _captureVisibilityController;
    private IPanel1PreparationController? _preparationController;
    private CaptureFloatingSession? _session;
    private CapturePanelView? _gestureView;
    private PixelPoint _pressScreenPoint;
    private PixelRect? _moveHomeBounds;
    private bool _overOwnHome;
    private bool _startingDetach;
    private bool _returnQueued;
    private bool _disposed;

    internal CaptureFixedHomeAdapter(
        StructuralAvaloniaShellHost owner,
        StructuralShellView shellView,
        RecorderCaptureVisibilityController captureVisibilityController)
    {
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        _shellView = shellView ??
            throw new ArgumentNullException(nameof(shellView));
        _captureVisibilityController = captureVisibilityController ??
            throw new ArgumentNullException(
                nameof(captureVisibilityController));

        AttachView(_shellView.DockedCaptureView);
        _shellView.CaptureReturnHomeRequested +=
            OnPlaceholderReturnHomeRequested;
    }

    internal bool IsFloating => _session is not null;

    internal nint FloatingHwnd =>
        _session is { Form.IsHandleCreated: true } session
            ? session.Form.Handle
            : nint.Zero;

    internal void AttachPreparationController(
        IPanel1PreparationController controller)
    {
        ArgumentNullException.ThrowIfNull(controller);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_preparationController is not null)
        {
            throw new InvalidOperationException(
                "Panel 1 preparation controller is already attached.");
        }
        _preparationController = controller;
    }

    internal void ReturnHome(CaptureReturnHomeSource source)
    {
        _ = source;
        CaptureFloatingSession? session = _session;
        if (session is null)
        {
            return;
        }

        _session = null;
        _returnQueued = false;
        _overOwnHome = false;
        _gestureView = null;
        _moveHomeBounds = null;
        DetachFloatingSession(session);
        try
        {
            session.Dispose();
        }
        finally
        {
            _shellView.SetCaptureFloatingPresentation(floating: false);
            if (!_disposed && !_owner.IsDisposed && !_owner.Disposing)
            {
                _owner.Activate();
                _owner.AvaloniaHost.Focus();
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _preparationController = null;
        try
        {
            _shellView.CaptureReturnHomeRequested -=
                OnPlaceholderReturnHomeRequested;
            DetachView(_shellView.DockedCaptureView);
            CaptureFloatingSession? session = _session;
            _session = null;
            if (session is not null)
            {
                DetachFloatingSession(session);
                session.Dispose();
            }
        }
        finally
        {
            _moveHomeBounds = null;
            _overOwnHome = false;
            _shellView.SetCaptureFloatingPresentation(floating: false);
        }
    }

    private void AttachView(CapturePanelView view)
    {
        view.TitlePointerPressed += OnTitlePointerPressed;
        view.TitlePointerMoved += OnTitlePointerMoved;
        view.TitlePointerReleased += OnTitlePointerReleased;
        view.ReturnHomeRequested += OnFloatingReturnHomeRequested;
    }

    private void DetachView(CapturePanelView view)
    {
        view.TitlePointerPressed -= OnTitlePointerPressed;
        view.TitlePointerMoved -= OnTitlePointerMoved;
        view.TitlePointerReleased -= OnTitlePointerReleased;
        view.ReturnHomeRequested -= OnFloatingReturnHomeRequested;
    }

    private void AttachFloatingSession(CaptureFloatingSession session)
    {
        AttachView(session.View);
        session.View.RecorderOwnedPopupOpened += OnRecorderOwnedPopupOpened;
        session.Form.NativeMoving += OnNativeMoving;
        session.Form.NativeMoveExited += OnNativeMoveExited;
        session.Form.ReturnHomeRequested += OnFloatingReturnHomeRequested;
    }

    private void DetachFloatingSession(CaptureFloatingSession session)
    {
        DetachView(session.View);
        session.View.RecorderOwnedPopupOpened -= OnRecorderOwnedPopupOpened;
        session.Form.NativeMoving -= OnNativeMoving;
        session.Form.NativeMoveExited -= OnNativeMoveExited;
        session.Form.ReturnHomeRequested -= OnFloatingReturnHomeRequested;
    }

    private void OnTitlePointerPressed(
        object? sender,
        CaptureTitlePointerEventArgs e)
    {
        if (_disposed || !e.LeftButtonPressed ||
            sender is not CapturePanelView view)
        {
            return;
        }
        _gestureView = view;
        _pressScreenPoint = e.ScreenPoint;
    }

    private void OnTitlePointerMoved(
        object? sender,
        CaptureTitlePointerEventArgs e)
    {
        if (_disposed || sender is not CapturePanelView view ||
            !ReferenceEquals(_gestureView, view))
        {
            return;
        }
        if (!e.LeftButtonPressed)
        {
            _gestureView = null;
            return;
        }
        if (!HasCrossedSystemDragThreshold(
                _pressScreenPoint,
                e.ScreenPoint))
        {
            return;
        }

        _gestureView = null;
        view.ReleaseTitlePointerCapture();
        if (ReferenceEquals(view, _shellView.DockedCaptureView))
        {
            StartDetachAndNativeMove();
            return;
        }
        if (_session is { } session && ReferenceEquals(view, session.View))
        {
            _moveHomeBounds = _shellView.TryGetCaptureHomeScreenBounds(
                out PixelRect currentHomeBounds)
                    ? currentHomeBounds
                    : null;
            _ = session.Form.BeginNativeMove();
        }
    }

    private void OnTitlePointerReleased(object? sender, EventArgs e)
    {
        if (ReferenceEquals(sender, _gestureView))
        {
            _gestureView = null;
        }
    }

    private void StartDetachAndNativeMove()
    {
        if (_disposed || _startingDetach || _session is not null ||
            _preparationController is not { } preparationController ||
            !_shellView.TryGetCaptureHomeScreenBounds(
                out PixelRect homeBounds))
        {
            return;
        }

        _startingDetach = true;
        CaptureFloatingSession? candidate = null;
        try
        {
            CapturePanelView floatingView = new(isFloating: true);
            floatingView.AttachPreparationController(preparationController);
            CaptureFloatingForm floatingForm = new(
                floatingView,
                ToDrawingRectangle(homeBounds));

            // Window topology is final before registration and hidden handle
            // creation. The floating controller deliberately has no WinForms
            // Owner so main-window minimize/focus changes cannot hide it.
            IDisposable registration = _captureVisibilityController
                .RegisterTopLevelWindow(
                    floatingForm,
                    RecorderCaptureWindowRole.FloatingTray);
            candidate = new CaptureFloatingSession(
                floatingForm,
                floatingView,
                registration);
            AttachFloatingSession(candidate);

            nint hiddenHwnd = floatingForm.Handle;
            RecorderCaptureVisibilityResult initialPolicy =
                _captureVisibilityController.LastResult;
            if (hiddenHwnd == nint.Zero || !initialPolicy.Succeeded)
            {
                throw new InvalidOperationException(
                    "Capture floating HWND capture policy failed before show: " +
                    $"{initialPolicy.Failure}; " +
                    $"Win32={initialPolicy.WindowsErrorCode}.");
            }

            RecorderCaptureVisibilityResult verifiedPolicy =
                _captureVisibilityController.TrySetTrayInFrame(
                    _captureVisibilityController.TrayInFrame);
            if (!verifiedPolicy.Succeeded)
            {
                throw new InvalidOperationException(
                    "Capture floating HWND capture readback failed before show: " +
                    $"{verifiedPolicy.Failure}; " +
                    $"Win32={verifiedPolicy.WindowsErrorCode}.");
            }

            _session = candidate;
            candidate = null;
            floatingForm.Show();
            _shellView.SetCaptureFloatingPresentation(floating: true);
            _moveHomeBounds = homeBounds;
            if (!floatingForm.BeginNativeMove())
            {
                ReturnHome(CaptureReturnHomeSource.FloatingClose);
            }
        }
        catch (Exception error)
        {
            Debug.WriteLine(
                $"Capture native detach rejected: {error}");
            if (_session is { } session)
            {
                _session = null;
                DetachFloatingSession(session);
                session.Dispose();
            }
            if (candidate is not null)
            {
                DetachFloatingSession(candidate);
                candidate.Dispose();
            }
            _moveHomeBounds = null;
            _shellView.SetCaptureFloatingPresentation(floating: false);
        }
        finally
        {
            _startingDetach = false;
        }
    }

    private void OnNativeMoving(
        object? sender,
        CaptureNativeMovingEventArgs e)
    {
        if (_disposed || _session is null ||
            _moveHomeBounds is not PixelRect homeBounds)
        {
            SetOwnHomeHighlight(false);
            return;
        }

        System.Drawing.Rectangle home = ToDrawingRectangle(homeBounds);
        System.Drawing.Size dragSize = SystemInformation.DragSize;
        home.Inflate(dragSize.Width * 2, dragSize.Height * 2);
        bool overOwnHome = home.Contains(Cursor.Position) &&
            home.IntersectsWith(e.WindowBounds);
        SetOwnHomeHighlight(overOwnHome);
    }

    private void OnRecorderOwnedPopupOpened(object? sender, EventArgs e)
    {
        if (!_disposed)
        {
            _ = _captureVisibilityController
                .TryRefreshTopLevelWindows();
        }
    }

    private void OnNativeMoveExited(object? sender, EventArgs e)
    {
        bool returnHome = _overOwnHome;
        _moveHomeBounds = null;
        SetOwnHomeHighlight(false);
        if (returnHome)
        {
            QueueReturnHome(CaptureReturnHomeSource.DragHome);
        }
    }

    private void OnPlaceholderReturnHomeRequested(
        object? sender,
        EventArgs e) => QueueReturnHome(
            CaptureReturnHomeSource.PlaceholderButton);

    private void OnFloatingReturnHomeRequested(
        object? sender,
        EventArgs e) => QueueReturnHome(
            CaptureReturnHomeSource.FloatingClose);

    private void QueueReturnHome(CaptureReturnHomeSource source)
    {
        if (_disposed || _session is null || _returnQueued)
        {
            return;
        }
        _returnQueued = true;
        try
        {
            _owner.BeginInvoke((Action)(() =>
            {
                _returnQueued = false;
                ReturnHome(source);
            }));
        }
        catch (InvalidOperationException) when (
            _owner.IsDisposed || _owner.Disposing)
        {
            _returnQueued = false;
        }
    }

    private void SetOwnHomeHighlight(bool highlighted)
    {
        if (_overOwnHome == highlighted)
        {
            return;
        }
        _overOwnHome = highlighted;
        _shellView.SetCaptureHomeHighlighted(highlighted);
    }

    private static bool HasCrossedSystemDragThreshold(
        PixelPoint press,
        PixelPoint current)
    {
        System.Drawing.Size threshold = SystemInformation.DragSize;
        long doubledDeltaX = Math.Abs((long)current.X - press.X) * 2;
        long doubledDeltaY = Math.Abs((long)current.Y - press.Y) * 2;
        return doubledDeltaX >= threshold.Width ||
            doubledDeltaY >= threshold.Height;
    }

    private static System.Drawing.Rectangle ToDrawingRectangle(
        PixelRect rect) => new(rect.X, rect.Y, rect.Width, rect.Height);
}
