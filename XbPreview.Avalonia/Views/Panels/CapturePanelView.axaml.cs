using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using XbPreview.Avalonia.Localization;
using XbPreview.Avalonia.Views;

namespace XbPreview.Avalonia.Views.Panels;

public sealed class CaptureTitlePointerEventArgs(
    PixelPoint screenPoint,
    bool leftButtonPressed) : EventArgs
{
    public PixelPoint ScreenPoint { get; } = screenPoint;

    public bool LeftButtonPressed { get; } = leftButtonPressed;
}

public sealed partial class CapturePanelView : UserControl, IDisposable
{
    private static readonly IBrush InactiveMeterBrush =
        Brush.Parse("#202124");
    private static readonly IBrush ActiveMeterBrush =
        Brush.Parse("#D94B32");
    private readonly object _snapshotPresentationGate = new();
    private readonly Border[] _systemMeterSegments;
    private readonly Border[] _microphoneMeterSegments;
    private IPanel1PreparationController? _controller;
    private Panel1PreparationSnapshot? _pendingSnapshot;
    private Panel1PreparationSnapshot? _lastAppliedSnapshot;
    private IPointer? _capturedTitlePointer;
    private long _lastAppliedRevision = -1;
    private bool _snapshotApplyQueued;
    private bool _applyingPresentation;
    private bool _disposed;

    public CapturePanelView()
        : this(isFloating: false)
    {
    }

    public CapturePanelView(bool isFloating)
    {
        InitializeComponent();
        if (UiLanguage.Resolve(
                null,
                global::System.Globalization.CultureInfo.CurrentUICulture) ==
            UiLanguage.English)
        {
            CaptureTitleIcon.Margin =
                new global::Avalonia.Thickness(73.1865, -2, 0, 0);
        }
        ApplyEnglishSystemAudioLayout();
        _systemMeterSegments =
        [
            SystemMeterSegment01,
            SystemMeterSegment02,
            SystemMeterSegment03,
            SystemMeterSegment04,
            SystemMeterSegment05,
            SystemMeterSegment06,
            SystemMeterSegment07,
            SystemMeterSegment08,
            SystemMeterSegment09,
            SystemMeterSegment10,
            SystemMeterSegment11,
            SystemMeterSegment12,
        ];
        _microphoneMeterSegments =
        [
            MicrophoneMeterSegment01,
            MicrophoneMeterSegment02,
            MicrophoneMeterSegment03,
            MicrophoneMeterSegment04,
            MicrophoneMeterSegment05,
            MicrophoneMeterSegment06,
            MicrophoneMeterSegment07,
            MicrophoneMeterSegment08,
            MicrophoneMeterSegment09,
            MicrophoneMeterSegment10,
            MicrophoneMeterSegment11,
            MicrophoneMeterSegment12,
        ];
        FloatingCloseButton.IsVisible = isFloating;
        FullScreenCaptureButton.Click += async (_, _) =>
            await RunCaptureCommandAsync(static controller =>
                controller.SetFullScreenAsync());
        WindowCaptureSelector.DropDownOpened += async (_, _) =>
        {
            RecorderOwnedPopupOpened?.Invoke(this, EventArgs.Empty);
            await RefreshWindowChoicesAsync();
            RecorderOwnedPopupOpened?.Invoke(this, EventArgs.Empty);
        };
        WindowCaptureSelector.SelectionChanged += async (_, _) =>
            await ApplySelectedWindowAsync();
        MouseHiddenToggle.Click += async (_, _) =>
            await RunPreparationCommandAsync(controller =>
                controller.SetMouseHiddenAsync(
                    MouseHiddenToggle.IsChecked == true));
        MicrophoneSelector.DropDownOpened += (_, _) =>
            RecorderOwnedPopupOpened?.Invoke(this, EventArgs.Empty);
        MicrophoneSelector.SelectionChanged += async (_, _) =>
            await ApplySelectedMicrophoneAsync();
        MicrophoneEnabledToggle.Click += async (_, _) =>
            await RunPreparationCommandAsync(controller =>
                controller.SetMicrophoneEnabledAsync(
                    MicrophoneEnabledToggle.IsChecked == true));
        MicrophoneRefreshButton.Click += async (_, _) =>
            await RunPreparationCommandAsync(static controller =>
                controller.RefreshMicrophonesAsync());
        SystemAudioRefreshButton.Click += async (_, _) =>
            await RunPreparationCommandAsync(static controller =>
                controller.RefreshSystemAudioAvailabilityAsync());
        SystemAudioToggle.Click += async (_, _) =>
            await RunPreparationCommandAsync(controller =>
                controller.SetSystemAudioEnabledAsync(
                    SystemAudioToggle.IsChecked == true));
        FloatingCloseButton.Click += (_, _) =>
            ReturnHomeRequested?.Invoke(this, EventArgs.Empty);

        TitleDragHandle.PointerPressed += OnTitlePointerPressed;
        TitleDragHandle.PointerMoved += OnTitlePointerMoved;
        TitleDragHandle.PointerReleased += OnTitlePointerReleased;
        TitleDragHandle.PointerCaptureLost += OnTitlePointerCaptureLost;
    }

    private void ApplyEnglishSystemAudioLayout()
    {
        if (UiLanguage.Resolve(
                null,
                global::System.Globalization.CultureInfo.CurrentUICulture) !=
            UiLanguage.English)
        {
            return;
        }

        if (SystemAudioLabel.Parent is not Grid audioControlsGrid ||
            !ReferenceEquals(SystemAudioMeter.Parent, audioControlsGrid))
        {
            throw new InvalidOperationException(
                "English System audio controls must share their original Grid.");
        }

        // English-only System-row container. It does not mutate the shared
        // audio Grid columns and therefore cannot take width from the Mic row.
        audioControlsGrid.Children.Remove(SystemAudioLabel);
        audioControlsGrid.Children.Remove(SystemAudioMeter);
        Grid.SetRow(SystemAudioLabel, 0);
        Grid.SetColumn(SystemAudioLabel, 0);
        Grid.SetColumnSpan(SystemAudioLabel, 1);
        Grid.SetRow(SystemAudioMeter, 0);
        Grid.SetColumn(SystemAudioMeter, 2);
        Grid.SetColumnSpan(SystemAudioMeter, 1);
        SystemAudioMeter.Margin = new global::Avalonia.Thickness(0, 0, 2, 0);
        EnglishSystemAudioLayout.Children.Add(SystemAudioLabel);
        EnglishSystemAudioLayout.Children.Add(SystemAudioMeter);
        EnglishSystemAudioLayout.IsVisible = true;
    }

    public event EventHandler? RecorderOwnedPopupOpened;

    public event EventHandler<CaptureTitlePointerEventArgs>?
        TitlePointerPressed;

    public event EventHandler<CaptureTitlePointerEventArgs>?
        TitlePointerMoved;

    public event EventHandler? TitlePointerReleased;

    public event EventHandler? ReturnHomeRequested;

    public void AttachPreparationController(
        IPanel1PreparationController controller)
    {
        ArgumentNullException.ThrowIfNull(controller);
        if (_controller is not null)
        {
            throw new InvalidOperationException(
                "Panel 1 preparation controller is already attached.");
        }

        _controller = controller;
        controller.SnapshotChanged += OnSnapshotChanged;
        ApplySnapshot(controller.CurrentSnapshot);
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
        lock (_snapshotPresentationGate)
        {
            _pendingSnapshot = null;
        }
        ReleaseTitlePointerCapture();
        if (_controller is { } controller)
        {
            controller.SnapshotChanged -= OnSnapshotChanged;
            _controller = null;
        }
    }

    public void ApplyMouseHiddenPresentation(
        bool mouseHidden,
        bool enabled,
        string? detail = null)
    {
        if (_controller is not null)
        {
            return;
        }

        _applyingPresentation = true;
        try
        {
            MouseHiddenToggle.IsChecked = mouseHidden;
            MouseHiddenToggle.IsEnabled = enabled;
            ToolTip.SetTip(MouseHiddenToggle, detail);
        }
        finally
        {
            _applyingPresentation = false;
        }
    }

    private void OnSnapshotChanged(Panel1PreparationSnapshot snapshot)
    {
        bool queueApply;
        lock (_snapshotPresentationGate)
        {
            if (_disposed ||
                snapshot.PresentationRevision < _lastAppliedRevision ||
                (_pendingSnapshot is { } pending &&
                 snapshot.PresentationRevision < pending.PresentationRevision))
            {
                return;
            }
            _pendingSnapshot = snapshot;
            queueApply = !_snapshotApplyQueued;
            _snapshotApplyQueued = true;
        }
        if (queueApply)
        {
            Dispatcher.UIThread.Post(DrainLatestSnapshot);
        }
    }

    private void DrainLatestSnapshot()
    {
        while (true)
        {
            Panel1PreparationSnapshot? snapshot;
            lock (_snapshotPresentationGate)
            {
                snapshot = _pendingSnapshot;
                _pendingSnapshot = null;
                if (snapshot is null)
                {
                    _snapshotApplyQueued = false;
                    return;
                }
            }
            ApplySnapshot(snapshot);
        }
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
            new CaptureTitlePointerEventArgs(
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
            new CaptureTitlePointerEventArgs(
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

    private async Task RefreshWindowChoicesAsync()
    {
        IPanel1PreparationController? controller = _controller;
        if (controller is null ||
            !controller.CurrentSnapshot.CaptureControlsEnabled)
        {
            return;
        }

        try
        {
            await controller.EnumerateWindowsAsync();
        }
        catch (Exception)
        {
            ApplySnapshot(controller.CurrentSnapshot);
        }
    }

    private async Task ApplySelectedWindowAsync()
    {
        IPanel1PreparationController? controller = _controller;
        if (_applyingPresentation ||
            controller is null ||
            !controller.CurrentSnapshot.CaptureControlsEnabled ||
            WindowCaptureSelector.SelectedItem is not
                StructuralCaptureWindowChoice choice)
        {
            return;
        }

        await RunCaptureCommandAsync(current =>
            current.SetWindowAsync(choice));
    }

    private async Task ApplySelectedMicrophoneAsync()
    {
        IPanel1PreparationController? controller = _controller;
        if (_applyingPresentation ||
            controller is null ||
            !controller.CurrentSnapshot.AudioControlsEnabled ||
            MicrophoneSelector.SelectedItem is not
                Panel1MicrophoneChoice choice)
        {
            return;
        }

        await RunPreparationCommandAsync(current =>
            current.SelectMicrophoneAsync(choice));
    }

    private async Task RunCaptureCommandAsync(
        Func<IPanel1PreparationController,
            Task<StructuralCaptureCommandResult>> command)
    {
        IPanel1PreparationController? controller = _controller;
        if (controller is null ||
            !controller.CurrentSnapshot.CaptureControlsEnabled)
        {
            return;
        }

        try
        {
            await command(controller);
        }
        catch (Exception)
        {
            ApplySnapshot(controller.CurrentSnapshot);
        }
    }

    private async Task RunPreparationCommandAsync(
        Func<IPanel1PreparationController,
            Task<Panel1PreparationCommandResult>> command)
    {
        IPanel1PreparationController? controller = _controller;
        if (_applyingPresentation || controller is null)
        {
            return;
        }

        try
        {
            await command(controller);
        }
        catch (Exception)
        {
            ApplySnapshot(controller.CurrentSnapshot);
        }
    }

    private void ApplySnapshot(Panel1PreparationSnapshot snapshot)
    {
        if (_disposed || snapshot.PresentationRevision < _lastAppliedRevision)
        {
            return;
        }
        _lastAppliedRevision = snapshot.PresentationRevision;
        _applyingPresentation = true;
        try
        {
            Panel1PreparationSnapshot? previous = _lastAppliedSnapshot;
            if (CapturePresentationChanged(previous, snapshot))
            {
                ApplyCapturePresentation(snapshot);
            }
            if (CursorPresentationChanged(previous, snapshot))
            {
                ApplyCursorPresentation(snapshot);
            }
            if (MicrophonePresentationChanged(previous, snapshot))
            {
                ApplyMicrophonePresentation(snapshot);
            }
            if (SystemAudioPresentationChanged(previous, snapshot))
            {
                ApplySystemAudioPresentation(snapshot);
            }
            if (MeterPresentationChanged(previous, snapshot))
            {
                ApplyMeterPresentation(snapshot);
            }
            _lastAppliedSnapshot = snapshot;
        }
        finally
        {
            _applyingPresentation = false;
        }
    }

    private void ApplyCapturePresentation(Panel1PreparationSnapshot snapshot)
    {
        if (!ReferenceEquals(
            WindowCaptureSelector.ItemsSource,
            snapshot.WindowChoices))
        {
            WindowCaptureSelector.ItemsSource = snapshot.WindowChoices;
        }
        WindowCaptureSelector.SelectedItem = snapshot.CaptureTarget.IsWindow
            ? snapshot.WindowChoices.FirstOrDefault(choice =>
                choice.Handle == snapshot.CaptureTarget.WindowHandle)
            : null;
        CaptureStatusText.Text = snapshot.CaptureDetail;
        FullScreenCaptureButton.Classes.Set(
            "selected",
            !snapshot.CaptureTarget.IsWindow);
        WindowCaptureSelector.Classes.Set(
            "selected",
            snapshot.CaptureTarget.IsWindow);
        FullScreenCaptureButton.IsEnabled = snapshot.CaptureControlsEnabled;
        WindowCaptureSelector.IsEnabled = snapshot.CaptureControlsEnabled;
    }

    private void ApplyCursorPresentation(Panel1PreparationSnapshot snapshot)
    {
        MouseHiddenToggle.IsChecked = snapshot.MouseHidden;
        MouseHiddenToggle.IsEnabled = snapshot.CursorControlEnabled;
        ToolTip.SetTip(
            MouseHiddenToggle,
            string.IsNullOrWhiteSpace(snapshot.MouseHiddenDetail)
                ? snapshot.MouseHidden
                    ? XbPreview.Avalonia.Localization.Strings.Get(
                        "CursorPreviewHidden")
                    : XbPreview.Avalonia.Localization.Strings.Get(
                        "CursorPreviewVisible")
                : snapshot.MouseHiddenDetail);
    }

    private void ApplyMicrophonePresentation(
        Panel1PreparationSnapshot snapshot)
    {
        if (!ReferenceEquals(
            MicrophoneSelector.ItemsSource,
            snapshot.MicrophoneDevices))
        {
            MicrophoneSelector.ItemsSource = snapshot.MicrophoneDevices;
        }
        Panel1MicrophoneChoice? selectedMicrophone = snapshot.MicrophoneDevices
            .FirstOrDefault(choice => string.Equals(
                choice.Key,
                snapshot.SelectedMicrophoneKey,
                StringComparison.Ordinal));
        MicrophoneSelector.SelectedItem = selectedMicrophone;
        MicrophoneSelector.IsEnabled = snapshot.AudioControlsEnabled &&
            snapshot.MicrophoneAvailable;
        MicrophoneEnabledToggle.IsChecked = snapshot.MicrophoneEnabled;
        MicrophoneEnabledToggle.IsEnabled = snapshot.AudioControlsEnabled &&
            snapshot.MicrophoneAvailable &&
            snapshot.SelectedMicrophoneAvailable;
        MicrophoneRefreshButton.IsEnabled = snapshot.AudioControlsEnabled;
        ToolTip.SetTip(
            MicrophoneSelector,
            selectedMicrophone?.DisplayName ?? snapshot.MicrophoneDetail);
        ToolTip.SetTip(MicrophoneEnabledToggle, snapshot.MicrophoneDetail);
    }

    private void ApplySystemAudioPresentation(
        Panel1PreparationSnapshot snapshot)
    {
        SystemAudioToggle.IsChecked = snapshot.SystemAudioEnabled;
        SystemAudioToggle.IsEnabled = snapshot.AudioControlsEnabled &&
            snapshot.SystemAudioAvailable;
        SystemAudioRefreshButton.IsEnabled = snapshot.AudioControlsEnabled;
        ToolTip.SetTip(SystemAudioToggle, snapshot.SystemAudioDetail);
    }

    private void ApplyMeterPresentation(Panel1PreparationSnapshot snapshot)
    {
        ApplyMeterPresentation(
            SystemAudioMeter,
            _systemMeterSegments,
            snapshot.SystemMeterActiveSegments,
            snapshot.SystemAudioEnabled && snapshot.SystemMeterAvailable);
        ApplyMeterPresentation(
            MicrophoneMeter,
            _microphoneMeterSegments,
            snapshot.MicrophoneMeterActiveSegments,
            snapshot.MicrophoneEnabled && snapshot.MicrophoneMeterAvailable);
    }

    private static bool CapturePresentationChanged(
        Panel1PreparationSnapshot? previous,
        Panel1PreparationSnapshot current) =>
        previous is null ||
        previous.CaptureTarget != current.CaptureTarget ||
        !previous.WindowChoices.SequenceEqual(current.WindowChoices) ||
        previous.CaptureDetail != current.CaptureDetail ||
        previous.CaptureControlsEnabled != current.CaptureControlsEnabled;

    private static bool CursorPresentationChanged(
        Panel1PreparationSnapshot? previous,
        Panel1PreparationSnapshot current) =>
        previous is null ||
        previous.MouseHidden != current.MouseHidden ||
        previous.MouseHiddenDetail != current.MouseHiddenDetail ||
        previous.CursorControlEnabled != current.CursorControlEnabled;

    private static bool MicrophonePresentationChanged(
        Panel1PreparationSnapshot? previous,
        Panel1PreparationSnapshot current) =>
        previous is null ||
        previous.MicrophoneAvailable != current.MicrophoneAvailable ||
        previous.SelectedMicrophoneAvailable !=
            current.SelectedMicrophoneAvailable ||
        previous.MicrophoneEnabled != current.MicrophoneEnabled ||
        !previous.MicrophoneDevices.SequenceEqual(current.MicrophoneDevices) ||
        previous.SelectedMicrophoneKey != current.SelectedMicrophoneKey ||
        previous.MicrophoneDetail != current.MicrophoneDetail ||
        previous.AudioControlsEnabled != current.AudioControlsEnabled;

    private static bool SystemAudioPresentationChanged(
        Panel1PreparationSnapshot? previous,
        Panel1PreparationSnapshot current) =>
        previous is null ||
        previous.SystemAudioEnabled != current.SystemAudioEnabled ||
        previous.SystemAudioAvailable != current.SystemAudioAvailable ||
        previous.SystemAudioDetail != current.SystemAudioDetail ||
        previous.AudioControlsEnabled != current.AudioControlsEnabled;

    private static bool MeterPresentationChanged(
        Panel1PreparationSnapshot? previous,
        Panel1PreparationSnapshot current) =>
        previous is null ||
        previous.SystemMeterActiveSegments !=
            current.SystemMeterActiveSegments ||
        previous.SystemMeterAvailable != current.SystemMeterAvailable ||
        previous.SystemAudioEnabled != current.SystemAudioEnabled ||
        previous.MicrophoneMeterActiveSegments !=
            current.MicrophoneMeterActiveSegments ||
        previous.MicrophoneMeterAvailable !=
            current.MicrophoneMeterAvailable ||
        previous.MicrophoneEnabled != current.MicrophoneEnabled;

    private static void ApplyMeterPresentation(
        Control meter,
        IReadOnlyList<Border> segments,
        int activeSegments,
        bool enabled)
    {
        int active = enabled
            ? Math.Clamp(activeSegments, 0, segments.Count)
            : 0;
        meter.Opacity = enabled ? 1.0 : 0.28;
        for (int index = 0; index < segments.Count; index++)
        {
            bool isActive = index < active;
            segments[index].Background = isActive
                ? ActiveMeterBrush
                : InactiveMeterBrush;
            segments[index].Opacity = enabled ? 1.0 : 0.58;
        }
    }

    private static void ApplyAudioSourceIndicator(
        Control lamp,
        Control glow,
        Panel1AudioSourceIndicator indicator)
    {
        lamp.Classes.Set(
            "unavailable",
            indicator == Panel1AudioSourceIndicator.Unavailable);
        lamp.Classes.Set(
            "available",
            indicator == Panel1AudioSourceIndicator.Available);
        lamp.Classes.Set(
            "ready",
            indicator == Panel1AudioSourceIndicator.Ready);
        glow.Classes.Set(
            "ready",
            indicator == Panel1AudioSourceIndicator.Ready);
    }
}
