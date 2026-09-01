using System.Diagnostics;
using Avalonia;
using XbPreview.Avalonia.Views;
using XbPreview.Avalonia.Views.Panels;

namespace XbPreview.Host;

internal enum Stage3DReturnHomeSource
{
    DragHome = 0,
    PlaceholderButton = 1,
    FloatingClose = 2,
    OwnerClosing = 3,
}

/// <summary>
/// The narrow two-state Fixed-Home adapter for Panel 3 / Slot 3. Both live
/// views borrow the same action and background adapters; this type owns no
/// Stage, background, renderer, ProductSettings, or general docking state.
/// </summary>
internal sealed class Stage3DFixedHomeFloatingAdapter : IDisposable
{
    private readonly StructuralAvaloniaShellHost _owner;
    private readonly StructuralShellView _shellView;
    private readonly RecorderCaptureVisibilityController
        _captureVisibilityController;
    private readonly Stage3DPanelActionAdapter _actionAdapter;
    private readonly Stage3DPanelBackgroundAdapter _backgroundAdapter;
    private Stage3DFloatingSession? _session;
    private Stage3DPanelView? _gestureView;
    private PixelPoint _pressScreenPoint;
    private PixelRect? _moveHomeBounds;
    private bool _overOwnHome;
    private bool _startingDetach;
    private bool _returnQueued;
    private bool _disposed;

    internal Stage3DFixedHomeFloatingAdapter(
        StructuralAvaloniaShellHost owner,
        StructuralShellView shellView,
        RecorderCaptureVisibilityController captureVisibilityController,
        Stage3DPanelActionAdapter actionAdapter,
        Stage3DPanelBackgroundAdapter backgroundAdapter)
    {
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        _shellView = shellView ??
            throw new ArgumentNullException(nameof(shellView));
        _captureVisibilityController = captureVisibilityController ??
            throw new ArgumentNullException(
                nameof(captureVisibilityController));
        _actionAdapter = actionAdapter ??
            throw new ArgumentNullException(nameof(actionAdapter));
        _backgroundAdapter = backgroundAdapter ??
            throw new ArgumentNullException(nameof(backgroundAdapter));

        AttachView(_shellView.DockedStage3DView);
        _shellView.Stage3DReturnHomeRequested +=
            OnPlaceholderReturnHomeRequested;
    }

    internal bool IsFloating => _session is not null;

    internal nint FloatingHwnd =>
        _session is { Form.IsHandleCreated: true } session
            ? session.Form.Handle
            : nint.Zero;

    internal void ReturnHome(Stage3DReturnHomeSource source)
    {
        _ = source;
        Stage3DFloatingSession? session = _session;
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
            _shellView.SetStage3DFloatingPresentation(floating: false);
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
        try
        {
            _shellView.Stage3DReturnHomeRequested -=
                OnPlaceholderReturnHomeRequested;
            DetachView(_shellView.DockedStage3DView);
            Stage3DFloatingSession? session = _session;
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
            _shellView.SetStage3DFloatingPresentation(floating: false);
        }
    }

    private void AttachView(Stage3DPanelView view)
    {
        _actionAdapter.AttachView(view);
        _backgroundAdapter.AttachView(view);
        view.TitlePointerPressed += OnTitlePointerPressed;
        view.TitlePointerMoved += OnTitlePointerMoved;
        view.TitlePointerReleased += OnTitlePointerReleased;
        view.ReturnHomeRequested += OnFloatingReturnHomeRequested;
    }

    private void DetachView(Stage3DPanelView view)
    {
        view.TitlePointerPressed -= OnTitlePointerPressed;
        view.TitlePointerMoved -= OnTitlePointerMoved;
        view.TitlePointerReleased -= OnTitlePointerReleased;
        view.ReturnHomeRequested -= OnFloatingReturnHomeRequested;
        _backgroundAdapter.DetachView(view);
        _actionAdapter.DetachView(view);
    }

    private void AttachFloatingSession(Stage3DFloatingSession session)
    {
        AttachView(session.View);
        session.Form.NativeMoving += OnNativeMoving;
        session.Form.NativeMoveExited += OnNativeMoveExited;
        session.Form.ReturnHomeRequested += OnFloatingReturnHomeRequested;
    }

    private void DetachFloatingSession(Stage3DFloatingSession session)
    {
        DetachView(session.View);
        session.Form.NativeMoving -= OnNativeMoving;
        session.Form.NativeMoveExited -= OnNativeMoveExited;
        session.Form.ReturnHomeRequested -= OnFloatingReturnHomeRequested;
    }

    private void OnTitlePointerPressed(
        object? sender,
        Stage3DTitlePointerEventArgs e)
    {
        if (_disposed || !e.LeftButtonPressed ||
            sender is not Stage3DPanelView view)
        {
            return;
        }
        _gestureView = view;
        _pressScreenPoint = e.ScreenPoint;
    }

    private void OnTitlePointerMoved(
        object? sender,
        Stage3DTitlePointerEventArgs e)
    {
        if (_disposed || sender is not Stage3DPanelView view ||
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
        if (ReferenceEquals(view, _shellView.DockedStage3DView))
        {
            StartDetachAndNativeMove();
            return;
        }
        if (_session is { } session && ReferenceEquals(view, session.View))
        {
            _moveHomeBounds = _shellView.TryGetStage3DHomeScreenBounds(
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
            !_shellView.TryGetStage3DHomeScreenBounds(
                out PixelRect homeBounds))
        {
            return;
        }

        _startingDetach = true;
        Stage3DFloatingSession? candidate = null;
        try
        {
            Stage3DPanelView floatingView = new(
                _shellView.Stage3DPresentationState,
                _shellView.Stage3DBackgroundState,
                isFloating: true);
            Stage3DFloatingForm floatingForm = new(
                floatingView,
                ToDrawingRectangle(homeBounds));

            // Finalize topology while hidden, then register and verify capture
            // exclusion before the HWND is allowed to show even one frame.
            IDisposable registration = _captureVisibilityController
                .RegisterTopLevelWindow(
                    floatingForm,
                    RecorderCaptureWindowRole.FloatingTray);
            candidate = new Stage3DFloatingSession(
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
                    "Panel 3 floating HWND capture policy failed before show: " +
                    $"{initialPolicy.Failure}; " +
                    $"Win32={initialPolicy.WindowsErrorCode}.");
            }

            RecorderCaptureVisibilityResult verifiedPolicy =
                _captureVisibilityController.TrySetTrayInFrame(
                    _captureVisibilityController.TrayInFrame);
            if (!verifiedPolicy.Succeeded)
            {
                throw new InvalidOperationException(
                    "Panel 3 floating HWND capture readback failed before show: " +
                    $"{verifiedPolicy.Failure}; " +
                    $"Win32={verifiedPolicy.WindowsErrorCode}.");
            }

            _session = candidate;
            candidate = null;
            floatingForm.Show();
            _shellView.SetStage3DFloatingPresentation(floating: true);
            _moveHomeBounds = homeBounds;
            if (!floatingForm.BeginNativeMove())
            {
                ReturnHome(Stage3DReturnHomeSource.FloatingClose);
            }
        }
        catch (Exception error)
        {
            Debug.WriteLine($"Panel 3 native detach rejected: {error}");
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
            _shellView.SetStage3DFloatingPresentation(floating: false);
        }
        finally
        {
            _startingDetach = false;
        }
    }

    private void OnNativeMoving(
        object? sender,
        Stage3DNativeMovingEventArgs e)
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

    private void OnNativeMoveExited(object? sender, EventArgs e)
    {
        bool returnHome = _overOwnHome;
        _moveHomeBounds = null;
        SetOwnHomeHighlight(false);
        if (returnHome)
        {
            QueueReturnHome(Stage3DReturnHomeSource.DragHome);
        }
    }

    private void OnPlaceholderReturnHomeRequested(
        object? sender,
        EventArgs e) => QueueReturnHome(
            Stage3DReturnHomeSource.PlaceholderButton);

    private void OnFloatingReturnHomeRequested(
        object? sender,
        EventArgs e) => QueueReturnHome(
            Stage3DReturnHomeSource.FloatingClose);

    private void QueueReturnHome(Stage3DReturnHomeSource source)
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
        _shellView.SetStage3DHomeHighlighted(highlighted);
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
