using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using XbPreview.Avalonia.Localization;

namespace XbPreview.Avalonia.Views.Panels;

public sealed class Stage3DTitlePointerEventArgs(
    PixelPoint screenPoint,
    bool leftButtonPressed) : EventArgs
{
    public PixelPoint ScreenPoint { get; } = screenPoint;

    public bool LeftButtonPressed { get; } = leftButtonPressed;
}

public sealed class Stage3DPoseRequestedEventArgs(
    Stage3DPanelInteractionCommand command) : EventArgs
{
    public Stage3DPanelInteractionCommand Command { get; } =
        command ?? throw new ArgumentNullException(nameof(command));

    public Stage3DPanelOrientation Orientation => Command.Orientation;

    public Stage3DPanelLevel Level => Command.Level;

    public bool IsActive => Command.IsActive;
}

public sealed class Stage3DBackgroundPresetRequestedEventArgs(
    Stage3DPanelBackgroundPreset preset) : EventArgs
{
    public Stage3DPanelBackgroundPreset Preset { get; } = preset;
}

public sealed partial class Stage3DPanelView : UserControl, IDisposable
{
    private sealed record BackgroundChoiceItem(
        Stage3DPanelBackgroundChoice Choice,
        string DisplayName);

    private static readonly BackgroundChoiceItem[] BackgroundChoices =
    [
        new(Stage3DPanelBackgroundChoice.Warm, "Warm"),
        new(Stage3DPanelBackgroundChoice.Art01, Strings.Get("Fantasy01")),
        new(Stage3DPanelBackgroundChoice.Art001, Strings.Get("Fantasy02")),
        new(Stage3DPanelBackgroundChoice.Custom, Strings.Get("CustomEllipsis")),
    ];

    private readonly Stage3DPanelPresentationState _presentationState;
    private readonly Stage3DPanelBackgroundState _backgroundState;
    private IPointer? _capturedTitlePointer;
    private bool _applyingBackgroundPresentation;
    private bool _disposed;

    public Stage3DPanelView()
        : this(
            new Stage3DPanelPresentationState(),
            new Stage3DPanelBackgroundState(),
            isFloating: false)
    {
    }

    public Stage3DPanelView(
        Stage3DPanelPresentationState presentationState,
        Stage3DPanelBackgroundState? backgroundState = null,
        bool isFloating = false)
    {
        ArgumentNullException.ThrowIfNull(presentationState);
        _presentationState = presentationState;
        _backgroundState = backgroundState ??
            new Stage3DPanelBackgroundState();

        InitializeComponent();
        if (UiLanguage.Resolve(
                null,
                global::System.Globalization.CultureInfo.CurrentUICulture) ==
            UiLanguage.English)
        {
            StageTitleIcon.Margin =
                new global::Avalonia.Thickness(61.0322, -2, 0, 0);
        }
        FloatingCloseButton.IsVisible = isFloating;

        BackgroundPresetSelector.ItemsSource = BackgroundChoices;

        LeftOrientationButton.Click += (_, _) => RequestDirection(
            Stage3DPanelOrientation.Left);
        FrontOrientationButton.Click += (_, _) => RequestDirection(
            Stage3DPanelOrientation.Front);
        RightOrientationButton.Click += (_, _) => RequestDirection(
            Stage3DPanelOrientation.Right);
        Level1Button.Click += (_, _) => RequestLevel(Stage3DPanelLevel.Level1);
        Level2Button.Click += (_, _) => RequestLevel(Stage3DPanelLevel.Level2);
        Level3Button.Click += (_, _) => RequestLevel(Stage3DPanelLevel.Level3);
        BackgroundPresetSelector.SelectionChanged +=
            OnBackgroundSelectionChanged;
        FloatingCloseButton.Click += (_, _) =>
            ReturnHomeRequested?.Invoke(this, EventArgs.Empty);
        TitleDragHandle.PointerPressed += OnTitlePointerPressed;
        TitleDragHandle.PointerMoved += OnTitlePointerMoved;
        TitleDragHandle.PointerReleased += OnTitlePointerReleased;
        TitleDragHandle.PointerCaptureLost += OnTitlePointerCaptureLost;

        _presentationState.Changed += OnPresentationStateChanged;
        _backgroundState.Changed += OnBackgroundStateChanged;
        ApplyPresentationState();
        ApplyBackgroundState();
    }

    public event EventHandler<Stage3DPoseRequestedEventArgs>? PoseRequested;

    public event EventHandler<Stage3DBackgroundPresetRequestedEventArgs>?
        BackgroundPresetRequested;

    public event EventHandler? CustomBackgroundPickerRequested;

    public event EventHandler<Stage3DTitlePointerEventArgs>?
        TitlePointerPressed;

    public event EventHandler<Stage3DTitlePointerEventArgs>?
        TitlePointerMoved;

    public event EventHandler? TitlePointerReleased;

    public event EventHandler? ReturnHomeRequested;

    public Stage3DPanelPresentationState PresentationState =>
        _presentationState;

    public Stage3DPanelBackgroundState BackgroundState => _backgroundState;

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
        _backgroundState.Changed -= OnBackgroundStateChanged;
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
            new Stage3DTitlePointerEventArgs(
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
            new Stage3DTitlePointerEventArgs(
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

    private void RequestDirection(Stage3DPanelOrientation orientation) =>
        Request(Stage3DPanelInteraction.DirectionClick(
            _presentationState.Snapshot,
            orientation));

    private void RequestLevel(Stage3DPanelLevel level) =>
        Request(Stage3DPanelInteraction.LevelClick(
            _presentationState.Snapshot,
            level));

    private void Request(Stage3DPanelInteractionCommand command)
    {
        if (!_disposed && _presentationState.Snapshot.ActionsEnabled)
        {
            PoseRequested?.Invoke(
                this,
                new Stage3DPoseRequestedEventArgs(command));
        }
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

    private void OnBackgroundStateChanged(object? sender, EventArgs e)
    {
        if (_disposed)
        {
            return;
        }
        if (Dispatcher.UIThread.CheckAccess())
        {
            ApplyBackgroundState();
            return;
        }
        Dispatcher.UIThread.Post(ApplyBackgroundState);
    }

    private void OnBackgroundSelectionChanged(
        object? sender,
        SelectionChangedEventArgs e)
    {
        if (_disposed || _applyingBackgroundPresentation ||
            !_backgroundState.Snapshot.ActionsEnabled ||
            BackgroundPresetSelector.SelectedItem is not
                BackgroundChoiceItem selected)
        {
            return;
        }

        try
        {
            if (selected.Choice == Stage3DPanelBackgroundChoice.Custom)
            {
                CustomBackgroundPickerRequested?.Invoke(
                    this,
                    EventArgs.Empty);
                return;
            }

            Stage3DPanelBackgroundPreset preset = selected.Choice switch
            {
                Stage3DPanelBackgroundChoice.Art01 =>
                    Stage3DPanelBackgroundPreset.Art01,
                Stage3DPanelBackgroundChoice.Art001 =>
                    Stage3DPanelBackgroundPreset.Art001,
                _ => Stage3DPanelBackgroundPreset.Warm,
            };
            BackgroundPresetRequested?.Invoke(
                this,
                new Stage3DBackgroundPresetRequestedEventArgs(preset));
        }
        finally
        {
            // The authoritative state is unchanged on picker cancellation or
            // decode failure, so the selection box returns to the real active
            // background instead of displaying the transient command item.
            ApplyBackgroundState();
        }
    }

    private void ApplyPresentationState()
    {
        if (_disposed)
        {
            return;
        }

        Stage3DPanelPresentationSnapshot snapshot =
            _presentationState.Snapshot;
        LeftOrientationButton.Classes.Set(
            "selected",
            snapshot.IsActive &&
                snapshot.Orientation == Stage3DPanelOrientation.Left);
        FrontOrientationButton.Classes.Set(
            "selected",
            snapshot.IsActive &&
                snapshot.Orientation == Stage3DPanelOrientation.Front);
        RightOrientationButton.Classes.Set(
            "selected",
            snapshot.IsActive &&
                snapshot.Orientation == Stage3DPanelOrientation.Right);
        Level1Button.Classes.Set(
            "selected",
            snapshot.Level == Stage3DPanelLevel.Level1);
        Level2Button.Classes.Set(
            "selected",
            snapshot.Level == Stage3DPanelLevel.Level2);
        Level3Button.Classes.Set(
            "selected",
            snapshot.Level == Stage3DPanelLevel.Level3);

        LeftOrientationButton.IsEnabled = snapshot.ActionsEnabled;
        FrontOrientationButton.IsEnabled = snapshot.ActionsEnabled;
        RightOrientationButton.IsEnabled = snapshot.ActionsEnabled;
        Level1Button.IsEnabled = snapshot.ActionsEnabled;
        Level2Button.IsEnabled = snapshot.ActionsEnabled;
        Level3Button.IsEnabled = snapshot.ActionsEnabled;
    }

    private void ApplyBackgroundState()
    {
        if (_disposed)
        {
            return;
        }

        Stage3DPanelBackgroundSnapshot snapshot = _backgroundState.Snapshot;
        _applyingBackgroundPresentation = true;
        try
        {
            BackgroundPresetSelector.SelectedItem = snapshot.Source ==
                Stage3DPanelBackgroundSource.CustomImage
                    ? null
                    : BackgroundChoices[(int)snapshot.Preset];
            BackgroundPresetSelector.PlaceholderText =
                snapshot.PresentationText;
        }
        finally
        {
            _applyingBackgroundPresentation = false;
        }

        BackgroundPresetSelector.Classes.Set("selected", true);
        BackgroundPresetSelector.IsEnabled = snapshot.ActionsEnabled;
        BackgroundStatusText.Text = snapshot.StatusText;
        BackgroundStatusText.IsVisible =
            !string.IsNullOrWhiteSpace(snapshot.StatusText);
    }
}
