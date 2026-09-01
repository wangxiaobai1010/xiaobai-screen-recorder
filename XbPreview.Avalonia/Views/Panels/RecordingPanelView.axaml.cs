using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using XbPreview.Avalonia.Contracts;
using XbPreview.Avalonia.Localization;

namespace XbPreview.Avalonia.Views.Panels;

public sealed class RecordingTitlePointerEventArgs(
    PixelPoint screenPoint,
    bool leftButtonPressed) : EventArgs
{
    public PixelPoint ScreenPoint { get; } = screenPoint;

    public bool LeftButtonPressed { get; } = leftButtonPressed;
}

public sealed partial class RecordingPanelView : UserControl, IDisposable
{
    private IRecordingPanelController? _controller;
    private RecordingPanelPresentationState _presentation =
        RecordingPanelPresentationState.Initial;
    private IPointer? _capturedTitlePointer;
    private bool _applyingCaptureVisibilityPresentation;
    private bool _applyingResolutionPresentation;
    private bool _disposed;

    public RecordingPanelView()
        : this(isFloating: false)
    {
    }

    public RecordingPanelView(bool isFloating)
    {
        InitializeComponent();
        if (UiLanguage.Resolve(
                null,
                global::System.Globalization.CultureInfo.CurrentUICulture) ==
            UiLanguage.English)
        {
            const double englishCommandFontSize = 12.0;
            RecordingTitleIcon.Margin =
                new global::Avalonia.Thickness(52.9248, -2, 0, 0);
            PauseResumeRecordingButton.FontSize = englishCommandFontSize;
            RestartRecordingButton.FontSize = englishCommandFontSize;
            StopRecordingButton.FontSize = englishCommandFontSize;
        }
        FloatingCloseButton.IsVisible = isFloating;
        TrayInFrameToggle.Click += (_, _) =>
            RequestTrayInFrameChange(TrayInFrameToggle.IsChecked == true);
        ChooseRecordingFolderButton.Click += (_, _) =>
            _controller?.ChooseOutputRoot();
        ResolutionSelector.SelectionChanged += async (_, _) =>
            await RequestResolutionChangeAsync();
        FrameRate30Button.Click += (_, _) =>
            _controller?.SetFrameRate(RecordingFrameRateMode.Fps30);
        FrameRate60Button.Click += (_, _) =>
            _controller?.SetFrameRate(RecordingFrameRateMode.Fps60);
        StartRecordingButton.Click += async (_, _) =>
            await RunCommandAsync(static controller =>
                controller.StartAsync());
        PauseResumeRecordingButton.Click += async (_, _) =>
            await RunCommandAsync(static controller =>
                controller.PauseOrResumeAsync());
        StopRecordingButton.Click += async (_, _) =>
            await RunCommandAsync(static controller =>
                controller.StopAsync());
        RestartRecordingButton.Click += (_, _) =>
            _controller?.ShowRestartConfirmation();
        ContinueCurrentRecordingButton.Click += (_, _) =>
            _controller?.DismissRestartConfirmation();
        DiscardCurrentRecordingButton.Click += async (_, _) =>
            await RunCommandAsync(static controller =>
                controller.DiscardCurrentRecordingAsync());
        ReturnToRecordingReadyButton.Click += (_, _) =>
            _controller?.ReturnToRecordingReady();
        OpenRecordingButton.Click += (_, _) =>
            _controller?.OpenRecording();
        OpenRecordingFolderButton.Click += (_, _) =>
            _controller?.OpenRecordingFolder();
        FloatingCloseButton.Click += (_, _) =>
            ReturnHomeRequested?.Invoke(this, EventArgs.Empty);
        TitleDragHandle.PointerPressed += OnTitlePointerPressed;
        TitleDragHandle.PointerMoved += OnTitlePointerMoved;
        TitleDragHandle.PointerReleased += OnTitlePointerReleased;
        TitleDragHandle.PointerCaptureLost += OnTitlePointerCaptureLost;
        ApplyPresentation(_presentation);
    }

    public event EventHandler<RecordingTitlePointerEventArgs>?
        TitlePointerPressed;

    public event EventHandler<RecordingTitlePointerEventArgs>?
        TitlePointerMoved;

    public event EventHandler? TitlePointerReleased;

    public event EventHandler? ReturnHomeRequested;

    public RecordingPanelPresentationState CurrentPresentation =>
        _presentation;

    public void AttachController(IRecordingPanelController controller)
    {
        ArgumentNullException.ThrowIfNull(controller);
        ObjectDisposedException.ThrowIf(_disposed, this);
        DetachController();
        _controller = controller;
        _controller.StateChanged += OnStateChanged;
        ApplyPresentation(_controller.CurrentState);
    }

    public void DetachController()
    {
        if (_controller is not null)
        {
            _controller.StateChanged -= OnStateChanged;
            _controller = null;
        }
        ApplyPresentation(RecordingPanelPresentationState.Initial);
    }

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
        DetachController();
    }

    private async Task RunCommandAsync(
        Func<IRecordingPanelController, Task> command)
    {
        IRecordingPanelController? controller = _controller;
        if (controller is not null)
        {
            await command(controller);
        }
    }

    private void OnStateChanged(RecordingPanelPresentationState state)
    {
        if (_disposed)
        {
            return;
        }
        if (Dispatcher.UIThread.CheckAccess())
        {
            ApplyPresentation(state);
            return;
        }
        Dispatcher.UIThread.Post(() => ApplyPresentation(state));
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
            new RecordingTitlePointerEventArgs(
                this.PointToScreen(point.Position),
                leftButtonPressed: true));
        e.Handled = true;
    }

    private void OnTitlePointerMoved(object? sender, PointerEventArgs e)
    {
        if (_disposed || !ReferenceEquals(_capturedTitlePointer, e.Pointer))
        {
            return;
        }

        PointerPoint point = e.GetCurrentPoint(this);
        TitlePointerMoved?.Invoke(
            this,
            new RecordingTitlePointerEventArgs(
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

    private void ApplyPresentation(RecordingPanelPresentationState state)
    {
        bool confirmationWasVisible =
            RestartRecordingConfirmation.IsVisible;
        _presentation = state;
        RecordingIdlePresentation.IsVisible = state.IdlePresentationVisible &&
            !state.RestartConfirmationVisible;
        RecordingActivePresentation.IsVisible =
            state.ActivePresentationVisible &&
            !state.RestartConfirmationVisible;
        RecordingCompletedPresentation.IsVisible =
            state.CompletedPresentationVisible &&
            !state.RestartConfirmationVisible;
        TrayInFrameControl.IsVisible =
            (state.IdlePresentationVisible ||
                state.ActivePresentationVisible) &&
            !state.RestartConfirmationVisible;
        RestartRecordingConfirmation.IsVisible =
            state.RestartConfirmationVisible;
        RecordingSectionTitleText.Text = state.Title;

        RecordingLiveStatus.IsVisible =
            state.RecordingState == RecordingReviewState.Recording;
        RecordingTransitionStatus.IsVisible =
            state.ActivePresentationVisible && !RecordingLiveStatus.IsVisible;
        RecordingPauseGlyph.IsVisible =
            state.RecordingState == RecordingReviewState.Paused;
        RecordingStateText.Text = state.StatusText;
        RecordingElapsedLabel.IsVisible = state.TimerVisible;
        RecordingElapsedText.IsVisible = state.TimerVisible;
        RecordingElapsedText.Text = state.ElapsedText;
        CompletedRecordingElapsedText.Text = state.ElapsedText;
        ApplyTimerColor(state.TimerColor);

        RecordingOutputPathText.Text = state.ReadyOutputPathText;
        ToolTip.SetTip(RecordingOutputPathText, state.CanonicalOutputRoot);

        RecordingErrorText.Text = state.ErrorMessage;
        RecordingErrorText.IsVisible = state.ErrorVisible;
        ToolTip.SetTip(RecordingErrorText, state.ErrorMessage);

        StartRecordingButton.IsEnabled = state.CanStart;
        ToolTip.SetTip(
            StartRecordingButton,
            string.IsNullOrWhiteSpace(state.StartToolTip)
                ? null
                : state.StartToolTip);
        ChooseRecordingFolderButton.IsEnabled = state.CanChangePath;
        _applyingResolutionPresentation = true;
        try
        {
            ResolutionSelector.SelectedIndex =
                (int)state.ResolutionChoice;
            ResolutionSelector.IsEnabled = state.CanChangeResolution;
            ToolTip.SetTip(
                ResolutionSelector,
                string.IsNullOrWhiteSpace(state.ResolutionToolTip)
                    ? null
                    : state.ResolutionToolTip);
        }
        finally
        {
            _applyingResolutionPresentation = false;
        }
        FrameRate30Button.Classes.Set(
            "selected",
            state.FrameRateMode == RecordingFrameRateMode.Fps30);
        FrameRate60Button.Classes.Set(
            "selected",
            state.FrameRateMode == RecordingFrameRateMode.Fps60);
        FrameRate30Button.IsEnabled = state.CanChangeFrameRate;
        FrameRate60Button.IsEnabled = state.CanChangeFrameRate;
        RecordingActiveCommands.IsVisible = state.ActiveCommandsVisible;
        PauseResumeRecordingButton.Content = state.PauseResumeText;
        PauseResumeRecordingButton.IsEnabled =
            state.CanPause || state.CanResume;
        StopRecordingButton.IsEnabled = state.CanStop;
        RestartRecordingButton.IsEnabled = state.CanRestart;
        ContinueCurrentRecordingButton.IsEnabled =
            state.CanDismissRestartConfirmation;
        DiscardCurrentRecordingButton.IsEnabled =
            state.CanDiscardCurrentRecording;
        OpenRecordingButton.IsEnabled = state.CanOpenVideo;
        OpenRecordingFolderButton.IsEnabled = state.CanOpenFolder;
        ReturnToRecordingReadyButton.IsEnabled =
            state.CanDismissCompletion;

        _applyingCaptureVisibilityPresentation = true;
        try
        {
            TrayInFrameToggle.IsChecked = state.TrayInFrame;
            TrayInFrameToggle.IsEnabled = state.CanToggleTrayInFrame;
            string trayInFrameTip =
                string.IsNullOrWhiteSpace(state.CaptureAffinityResult)
                    ? state.TrayInFrame
                        ? XbPreview.Avalonia.Localization.Strings.Get("TrayEnabled")
                        : XbPreview.Avalonia.Localization.Strings.Get("TrayDisabled")
                    : state.CaptureAffinityResult;
            ToolTip.SetTip(
                TrayInFrameToggle,
                trayInFrameTip);
        }
        finally
        {
            _applyingCaptureVisibilityPresentation = false;
        }

        if (!confirmationWasVisible && state.RestartConfirmationVisible)
        {
            _ = ContinueCurrentRecordingButton.Focus();
        }
    }

    private void ApplyTimerColor(RecordingPanelTimerColor color)
    {
        RecordingElapsedText.Classes.Set(
            "recording-timer",
            color == RecordingPanelTimerColor.Orange);
        RecordingElapsedText.Classes.Set(
            "paused-timer",
            color == RecordingPanelTimerColor.Gray);
        RecordingElapsedText.Classes.Set(
            "stopped-timer",
            color == RecordingPanelTimerColor.Black);
        CompletedRecordingElapsedText.Classes.Set(
            "stopped-timer",
            color == RecordingPanelTimerColor.Black);
    }

    private void RequestTrayInFrameChange(bool trayInFrame)
    {
        if (_applyingCaptureVisibilityPresentation)
        {
            return;
        }

        _controller?.SetTrayInFrame(trayInFrame);
    }

    private async Task RequestResolutionChangeAsync()
    {
        if (_applyingResolutionPresentation || _controller is null)
        {
            return;
        }

        RecordingResolutionChoice choice = ResolutionSelector.SelectedIndex
            switch
            {
                1 => RecordingResolutionChoice.Fhd1080,
                2 => RecordingResolutionChoice.Qhd1440,
                3 => RecordingResolutionChoice.Uhd2160,
                _ => RecordingResolutionChoice.Original,
            };
        await _controller.SetResolutionAsync(choice);
    }
}
