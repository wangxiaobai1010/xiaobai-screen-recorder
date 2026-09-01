using System.Diagnostics;
using Avalonia;
using XbPreview.Avalonia.Views;
using XbPreview.Avalonia.Views.Panels;

namespace XbPreview.Host;

internal enum RecordingReturnHomeSource
{
    DragHome = 0,
    PlaceholderButton = 1,
    FloatingClose = 2,
    OwnerClosing = 3,
}

/// <summary>
/// The narrow two-state Fixed-Home adapter for Panel 4 / Slot 4. Both live
/// views borrow the same authoritative recording controller; this type owns
/// no RecordingController, production adapter, timer, or recording truth.
/// </summary>
internal sealed class RecordingFixedHomeFloatingAdapter : IDisposable
{
    private readonly StructuralAvaloniaShellHost _owner;
    private readonly StructuralShellView _shellView;
    private readonly RecorderCaptureVisibilityController
        _captureVisibilityController;
    private IRecordingPanelController? _controller;
    private RecordingFloatingSession? _session;
    private RecordingPanelView? _gestureView;
    private PixelPoint _pressScreenPoint;
    private PixelRect? _moveHomeBounds;
    private bool _overOwnHome;
    private bool _startingDetach;
    private bool _returnQueued;
    private bool _disposed;

    internal RecordingFixedHomeFloatingAdapter(
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
        AttachView(_shellView.DockedRecordingView);
        _shellView.RecordingReturnHomeRequested +=
            OnPlaceholderReturnHomeRequested;
    }

    internal bool IsFloating => _session is not null;

    internal nint FloatingHwnd =>
        _session is { Form.IsHandleCreated: true } session
            ? session.Form.Handle
            : nint.Zero;

    internal void AttachController(IRecordingPanelController controller)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(controller);
        if (_controller is not null)
        {
            throw new InvalidOperationException(
                "Panel 4 authoritative controller is already attached.");
        }

        _controller = controller;
        _shellView.AttachRecordingController(controller);
    }

    internal void ReturnHome(RecordingReturnHomeSource source)
    {
        _ = source;
        RecordingFloatingSession? session = _session;
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
            _shellView.SetRecordingFloatingPresentation(floating: false);
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
            _shellView.RecordingReturnHomeRequested -=
                OnPlaceholderReturnHomeRequested;
            DetachView(_shellView.DockedRecordingView);
            _shellView.DetachRecordingController();
            _controller = null;
            RecordingFloatingSession? session = _session;
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
            _shellView.SetRecordingFloatingPresentation(floating: false);
        }
    }

    private void AttachView(RecordingPanelView view)
    {
        view.TitlePointerPressed += OnTitlePointerPressed;
        view.TitlePointerMoved += OnTitlePointerMoved;
        view.TitlePointerReleased += OnTitlePointerReleased;
        view.ReturnHomeRequested += OnFloatingReturnHomeRequested;
    }

    private void DetachView(RecordingPanelView view)
    {
        view.TitlePointerPressed -= OnTitlePointerPressed;
        view.TitlePointerMoved -= OnTitlePointerMoved;
        view.TitlePointerReleased -= OnTitlePointerReleased;
        view.ReturnHomeRequested -= OnFloatingReturnHomeRequested;
    }

    private void AttachFloatingSession(RecordingFloatingSession session)
    {
        AttachView(session.View);
        session.Form.NativeMoving += OnNativeMoving;
        session.Form.NativeMoveExited += OnNativeMoveExited;
        session.Form.ReturnHomeRequested += OnFloatingReturnHomeRequested;
    }

    private void DetachFloatingSession(RecordingFloatingSession session)
    {
        DetachView(session.View);
        session.Form.NativeMoving -= OnNativeMoving;
        session.Form.NativeMoveExited -= OnNativeMoveExited;
        session.Form.ReturnHomeRequested -= OnFloatingReturnHomeRequested;
    }

    private void OnTitlePointerPressed(
        object? sender,
        RecordingTitlePointerEventArgs e)
    {
        if (_disposed || !e.LeftButtonPressed ||
            sender is not RecordingPanelView view)
        {
            return;
        }
        _gestureView = view;
        _pressScreenPoint = e.ScreenPoint;
    }

    private void OnTitlePointerMoved(
        object? sender,
        RecordingTitlePointerEventArgs e)
    {
        if (_disposed || sender is not RecordingPanelView view ||
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
        if (ReferenceEquals(view, _shellView.DockedRecordingView))
        {
            StartDetachAndNativeMove();
            return;
        }
        if (_session is { } session && ReferenceEquals(view, session.View))
        {
            _moveHomeBounds = _shellView.TryGetRecordingHomeScreenBounds(
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
            _controller is null ||
            !_shellView.TryGetRecordingHomeScreenBounds(
                out PixelRect homeBounds))
        {
            return;
        }

        _startingDetach = true;
        RecordingFloatingSession? candidate = null;
        try
        {
            RecordingPanelView floatingView = new(isFloating: true);
            floatingView.AttachController(_controller);
            RecordingFloatingForm floatingForm = new(
                floatingView,
                ToDrawingRectangle(homeBounds));

            // Finalize topology while hidden, then register and verify capture
            // exclusion before the HWND is allowed to show even one frame.
            IDisposable registration = _captureVisibilityController
                .RegisterTopLevelWindow(
                    floatingForm,
                    RecorderCaptureWindowRole.FloatingTray);
            candidate = new RecordingFloatingSession(
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
                    "Panel 4 floating HWND capture policy failed before show: " +
                    $"{initialPolicy.Failure}; " +
                    $"Win32={initialPolicy.WindowsErrorCode}.");
            }

            RecorderCaptureVisibilityResult verifiedPolicy =
                _captureVisibilityController.TrySetTrayInFrame(
                    _captureVisibilityController.TrayInFrame);
            if (!verifiedPolicy.Succeeded)
            {
                throw new InvalidOperationException(
                    "Panel 4 floating HWND capture readback failed before show: " +
                    $"{verifiedPolicy.Failure}; " +
                    $"Win32={verifiedPolicy.WindowsErrorCode}.");
            }

            _session = candidate;
            candidate = null;
            floatingForm.Show();
            _shellView.SetRecordingFloatingPresentation(floating: true);
            _moveHomeBounds = homeBounds;
            if (!floatingForm.BeginNativeMove())
            {
                ReturnHome(RecordingReturnHomeSource.FloatingClose);
            }
        }
        catch (Exception error)
        {
            Debug.WriteLine($"Panel 4 native detach rejected: {error}");
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
            _shellView.SetRecordingFloatingPresentation(floating: false);
        }
        finally
        {
            _startingDetach = false;
        }
    }

    private void OnNativeMoving(
        object? sender,
        RecordingNativeMovingEventArgs e)
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
            QueueReturnHome(RecordingReturnHomeSource.DragHome);
        }
    }

    private void OnPlaceholderReturnHomeRequested(
        object? sender,
        EventArgs e) => QueueReturnHome(
            RecordingReturnHomeSource.PlaceholderButton);

    private void OnFloatingReturnHomeRequested(
        object? sender,
        EventArgs e) => QueueReturnHome(
            RecordingReturnHomeSource.FloatingClose);

    private void QueueReturnHome(RecordingReturnHomeSource source)
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
        _shellView.SetRecordingHomeHighlighted(highlighted);
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
