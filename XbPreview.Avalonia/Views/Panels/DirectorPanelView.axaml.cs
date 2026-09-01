using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;

namespace XbPreview.Avalonia.Views.Panels;

public sealed class DirectorTitlePointerEventArgs(
    PixelPoint screenPoint,
    bool leftButtonPressed) : EventArgs
{
    public PixelPoint ScreenPoint { get; } = screenPoint;

    public bool LeftButtonPressed { get; } = leftButtonPressed;
}

public sealed class DirectorManualZoomRequestedEventArgs(
    DirectorPanelManualZoom zoom) : EventArgs
{
    public DirectorPanelManualZoom Zoom { get; } = zoom;
}

public sealed class DirectorToggleRequestedEventArgs(bool enabled) : EventArgs
{
    public bool Enabled { get; } = enabled;
}

public sealed partial class DirectorPanelView : UserControl, IDisposable
{
    private readonly DirectorPanelPresentationState _presentationState;
    private IPointer? _capturedTitlePointer;
    private bool _disposed;

    public DirectorPanelView()
        : this(new DirectorPanelPresentationState(), isFloating: false)
    {
    }

    public DirectorPanelView(
        DirectorPanelPresentationState presentationState,
        bool isFloating)
    {
        ArgumentNullException.ThrowIfNull(presentationState);
        _presentationState = presentationState;

        InitializeComponent();
        FloatingCloseButton.IsVisible = isFloating;

        StandardZoomButton.Click += (_, _) =>
            ManualZoomRequested?.Invoke(
                this,
                new DirectorManualZoomRequestedEventArgs(
                    DirectorPanelManualZoom.Standard));
        StrongZoomButton.Click += (_, _) =>
            ManualZoomRequested?.Invoke(
                this,
                new DirectorManualZoomRequestedEventArgs(
                    DirectorPanelManualZoom.Strong));
        HotkeysToggle.Click += (_, _) =>
        {
            HotkeysEnabledChangeRequested?.Invoke(
                this,
                new DirectorToggleRequestedEventArgs(
                    HotkeysToggle.IsChecked == true));
            ApplyPresentationState();
        };
        AutoDirectorToggle.Click += (_, _) =>
        {
            AutoDirectorEnabledChangeRequested?.Invoke(
                this,
                new DirectorToggleRequestedEventArgs(
                    AutoDirectorToggle.IsChecked == true));
            ApplyPresentationState();
        };
        FloatingCloseButton.Click += (_, _) =>
            ReturnHomeRequested?.Invoke(this, EventArgs.Empty);

        TitleDragHandle.PointerPressed += OnTitlePointerPressed;
        TitleDragHandle.PointerMoved += OnTitlePointerMoved;
        TitleDragHandle.PointerReleased += OnTitlePointerReleased;
        TitleDragHandle.PointerCaptureLost += OnTitlePointerCaptureLost;

        _presentationState.Changed += OnPresentationStateChanged;
        ApplyPresentationState();
    }

    public event EventHandler<DirectorTitlePointerEventArgs>?
        TitlePointerPressed;

    public event EventHandler<DirectorTitlePointerEventArgs>?
        TitlePointerMoved;

    public event EventHandler? TitlePointerReleased;

    public event EventHandler? ReturnHomeRequested;

    public event EventHandler<DirectorManualZoomRequestedEventArgs>?
        ManualZoomRequested;

    public event EventHandler<DirectorToggleRequestedEventArgs>?
        HotkeysEnabledChangeRequested;

    public event EventHandler<DirectorToggleRequestedEventArgs>?
        AutoDirectorEnabledChangeRequested;

    public DirectorPanelPresentationState PresentationState =>
        _presentationState;

    public void ReleaseTitlePointerCapture()
    {
        IPointer? pointer = _capturedTitlePointer;
        _capturedTitlePointer = null;
        pointer?.Capture(null);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        ReleaseTitlePointerCapture();
        _presentationState.Changed -= OnPresentationStateChanged;
    }

    private void OnTitlePointerPressed(
        object? sender,
        PointerPressedEventArgs e)
    {
        PointerPoint point = e.GetCurrentPoint(this);
        if (_disposed || !point.Properties.IsLeftButtonPressed)
        {
            return;
        }

        _capturedTitlePointer = e.Pointer;
        e.Pointer.Capture(TitleDragHandle);
        TitlePointerPressed?.Invoke(
            this,
            new DirectorTitlePointerEventArgs(
                this.PointToScreen(point.Position),
                leftButtonPressed: true));
        e.Handled = true;
    }

    private void OnTitlePointerMoved(
        object? sender,
        PointerEventArgs e)
    {
        if (_disposed || !ReferenceEquals(_capturedTitlePointer, e.Pointer))
        {
            return;
        }

        PointerPoint point = e.GetCurrentPoint(this);
        TitlePointerMoved?.Invoke(
            this,
            new DirectorTitlePointerEventArgs(
                this.PointToScreen(point.Position),
                point.Properties.IsLeftButtonPressed));
        e.Handled = true;
    }

    private void OnTitlePointerReleased(
        object? sender,
        PointerReleasedEventArgs e)
    {
        if (!ReferenceEquals(_capturedTitlePointer, e.Pointer))
        {
            return;
        }

        ReleaseTitlePointerCapture();
        TitlePointerReleased?.Invoke(this, EventArgs.Empty);
        e.Handled = true;
    }

    private void OnTitlePointerCaptureLost(
        object? sender,
        PointerCaptureLostEventArgs e)
    {
        if (_capturedTitlePointer is null)
        {
            return;
        }
        _capturedTitlePointer = null;
        TitlePointerReleased?.Invoke(this, EventArgs.Empty);
    }

    private void OnPresentationStateChanged(object? sender, EventArgs e)
    {
        if (_disposed)
        {
            return;
        }
        if (Dispatcher.UIThread.CheckAccess())
        {
            ApplyPresentationState();
            return;
        }
        Dispatcher.UIThread.Post(ApplyPresentationState);
    }

    private void ApplyPresentationState()
    {
        if (_disposed)
        {
            return;
        }
        DirectorPanelPresentationSnapshot snapshot =
            _presentationState.Snapshot;
        StandardZoomButton.Classes.Set(
            "selected",
            snapshot.ManualZoom ==
                DirectorPanelManualZoom.Standard);
        StrongZoomButton.Classes.Set(
            "selected",
            snapshot.ManualZoom ==
                DirectorPanelManualZoom.Strong);
        StandardZoomButton.IsEnabled =
            snapshot.ActionsEnabled && snapshot.ManualControlsEnabled;
        StrongZoomButton.IsEnabled =
            snapshot.ActionsEnabled && snapshot.ManualControlsEnabled;
        HotkeysToggle.IsEnabled = snapshot.ActionsEnabled;
        HotkeysToggle.IsChecked = snapshot.HotkeysEnabled;
        AutoDirectorToggle.IsEnabled = snapshot.ActionsEnabled;
        AutoDirectorToggle.IsChecked = snapshot.AutoDirectorEnabled;
    }
}
