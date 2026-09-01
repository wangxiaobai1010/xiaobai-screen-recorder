using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using XbPreview.Avalonia.Contracts;
using XbPreview.Avalonia.Controls;

namespace XbPreview.Avalonia.Views;

public partial class FormalHomeView : UserControl
{
    private IRecordingReviewController? _recordingController;
    private IProductReviewController? _productController;
    private bool _applyingProductSnapshot;

    public FormalHomeView()
    {
        InitializeComponent();
        StageOrientationSelector.ItemsSource =
            Enum.GetValues<ProductReviewStageOrientation>();
        StageLevelSelector.ItemsSource =
            Enum.GetValues<ProductReviewStageLevel>();
        BackgroundPresetSelector.ItemsSource =
            Enum.GetValues<ProductReviewBackgroundPreset>();

        SettingsButton.Click += (_, _) =>
            SettingsRequested?.Invoke(this, EventArgs.Empty);
        StartRecordingButton.Click += OnStartRecordingClick;
        PauseRecordingButton.Click += OnPauseRecordingClick;
        ResumeRecordingButton.Click += OnResumeRecordingClick;
        StopRecordingButton.Click += OnStopRecordingClick;
        RefreshWindowsButton.Click += (_, _) =>
            RunProductCommand(controller =>
                Task.FromResult(controller.RefreshDevices()));
        CaptureModeSelector.SelectionChanged += (_, _) =>
            OnCaptureSelectionChanged();
        WindowSelector.SelectionChanged += (_, _) =>
            OnWindowSelectionChanged();
        MicrophoneEnabledCheckBox.Click += (_, _) =>
            RunProductCommand(controller =>
                controller.SetMicrophoneEnabledAsync(
                    MicrophoneEnabledCheckBox.IsChecked == true));
        MicrophoneSelector.SelectionChanged += (_, _) =>
            OnMicrophoneSelectionChanged();
        SystemAudioCheckBox.Click += (_, _) =>
            RunProductCommand(controller =>
                controller.SetSystemAudioEnabledAsync(
                    SystemAudioCheckBox.IsChecked == true));
        CursorVisibleCheckBox.Click += (_, _) =>
            RunProductCommand(controller =>
                controller.SetCursorVisibleAsync(
                    CursorVisibleCheckBox.IsChecked == true));
        HotkeysEnabledCheckBox.Click += (_, _) =>
            RunProductCommand(controller =>
                controller.SetHotkeysEnabledAsync(
                    HotkeysEnabledCheckBox.IsChecked == true));
        AutoDirectorCheckBox.Click += (_, _) =>
            RunProductCommand(controller =>
                controller.SetAutoDirectorEnabledAsync(
                    AutoDirectorCheckBox.IsChecked == true));
        StandardZoomButton.Click += (_, _) =>
            RunProductCommand(controller => controller.ExecuteManualZoomAsync(
                ProductReviewManualZoom.Standard));
        StrongZoomButton.Click += (_, _) =>
            RunProductCommand(controller => controller.ExecuteManualZoomAsync(
                ProductReviewManualZoom.Strong));
        StageOrientationSelector.SelectionChanged += (_, _) =>
            OnStageSelectionChanged();
        StageLevelSelector.SelectionChanged += (_, _) =>
            OnStageSelectionChanged();
        BackgroundPresetSelector.SelectionChanged += (_, _) =>
            OnBackgroundSelectionChanged();
        ApplyCustomBackgroundButton.Click += (_, _) =>
            RunProductCommand(controller => controller.SetCustomBackgroundAsync(
                CustomBackgroundPathTextBox.Text ?? string.Empty));
        ApplyOutputRootButton.Click += (_, _) =>
            RunProductCommand(controller => controller.SetOutputRootAsync(
                OutputRootTextBox.Text));
    }

    public FormalHomeView(IGpuPreviewFrameSource frameSource)
        : this()
    {
        ArgumentNullException.ThrowIfNull(frameSource);
        GpuPreview.FrameSource = frameSource;
    }

    public event EventHandler? SettingsRequested;

    public GpuPreviewControl PreviewControl => GpuPreview;

    public void AttachRecordingController(
        IRecordingReviewController recordingController)
    {
        ArgumentNullException.ThrowIfNull(recordingController);
        DetachRecordingController();
        _recordingController = recordingController;
        _recordingController.SnapshotChanged += OnRecordingSnapshotChanged;
        ApplyRecordingSnapshot(_recordingController.CurrentSnapshot);
    }

    public void AttachProductController(
        IProductReviewController productController)
    {
        ArgumentNullException.ThrowIfNull(productController);
        DetachProductController();
        _productController = productController;
        _productController.SnapshotChanged += OnProductSnapshotChanged;
        ApplyProductSnapshot(_productController.CurrentSnapshot);
    }

    public void DetachRecordingController()
    {
        if (_recordingController is not null)
        {
            _recordingController.SnapshotChanged -= OnRecordingSnapshotChanged;
            _recordingController = null;
        }
        ApplyRecordingSnapshot(RecordingReviewSnapshot.Idle);
        StartRecordingButton.IsEnabled = false;
    }

    public void DetachProductController()
    {
        if (_productController is not null)
        {
            _productController.SnapshotChanged -= OnProductSnapshotChanged;
            _productController = null;
        }
    }

    public async Task ShutdownPreviewAsync()
    {
        DetachProductController();
        DetachRecordingController();
        await GpuPreview.ShutdownAsync();
    }

    private async void OnStartRecordingClick(
        object? sender,
        RoutedEventArgs e) => await RunRecordingCommandAsync(
            controller => controller.StartAsync());

    private async void OnPauseRecordingClick(
        object? sender,
        RoutedEventArgs e) => await RunRecordingCommandAsync(
            controller => controller.PauseAsync());

    private async void OnResumeRecordingClick(
        object? sender,
        RoutedEventArgs e) => await RunRecordingCommandAsync(
            controller => controller.ResumeAsync());

    private async void OnStopRecordingClick(
        object? sender,
        RoutedEventArgs e) => await RunRecordingCommandAsync(
            controller => controller.StopAsync());

    private async Task RunRecordingCommandAsync(
        Func<IRecordingReviewController, Task> command)
    {
        IRecordingReviewController? controller = _recordingController;
        if (controller is null)
        {
            return;
        }
        try
        {
            await command(controller);
        }
        catch (Exception error)
        {
            RecordingErrorText.Text = error.Message;
        }
    }

    private async void RunProductCommand(
        Func<IProductReviewController, Task<ProductReviewCommandResult>> command)
    {
        if (_applyingProductSnapshot || _productController is null)
        {
            return;
        }
        try
        {
            ProductReviewCommandResult result =
                await command(_productController);
            ProductStatusText.Text = result.Detail;
        }
        catch (Exception error)
        {
            ProductStatusText.Text = error.Message;
        }
    }

    private void OnCaptureSelectionChanged()
    {
        if (_applyingProductSnapshot || _productController is null)
        {
            return;
        }
        bool window = CaptureModeSelector.SelectedIndex == 1;
        WindowSelector.IsVisible = window;
        RefreshWindowsButton.IsVisible = window;
        if (!window)
        {
            RunProductCommand(controller =>
                controller.SetCaptureTargetFullScreenAsync());
        }
        else
        {
            _ = _productController.RefreshDevices();
        }
    }

    private void OnWindowSelectionChanged()
    {
        if (_applyingProductSnapshot ||
            WindowSelector.SelectedItem is not ProductReviewWindowChoice choice)
        {
            return;
        }
        RunProductCommand(controller =>
            controller.SetCaptureTargetWindowAsync(choice.Id));
    }

    private void OnMicrophoneSelectionChanged()
    {
        if (_applyingProductSnapshot ||
            MicrophoneSelector.SelectedItem is not
                ProductReviewMicrophoneChoice choice)
        {
            return;
        }
        RunProductCommand(controller =>
            controller.SetMicrophoneSelectionAsync(choice.Id));
    }

    private void OnStageSelectionChanged()
    {
        if (_applyingProductSnapshot ||
            StageOrientationSelector.SelectedItem is not
                ProductReviewStageOrientation orientation ||
            StageLevelSelector.SelectedItem is not ProductReviewStageLevel level)
        {
            return;
        }
        RunProductCommand(controller =>
            controller.SetStagePoseAsync(orientation, level));
    }

    private void OnBackgroundSelectionChanged()
    {
        if (_applyingProductSnapshot ||
            BackgroundPresetSelector.SelectedItem is not
                ProductReviewBackgroundPreset preset)
        {
            return;
        }
        RunProductCommand(controller =>
            controller.SetBackgroundPresetAsync(preset));
    }

    private void OnRecordingSnapshotChanged(RecordingReviewSnapshot snapshot)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            ApplyRecordingSnapshot(snapshot);
            return;
        }
        Dispatcher.UIThread.Post(() => ApplyRecordingSnapshot(snapshot));
    }

    private void OnProductSnapshotChanged(ProductReviewSnapshot snapshot)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            ApplyProductSnapshot(snapshot);
            return;
        }
        Dispatcher.UIThread.Post(() => ApplyProductSnapshot(snapshot));
    }

    private void ApplyRecordingSnapshot(RecordingReviewSnapshot snapshot)
    {
        RecordingStateText.Text =
            $"STATE: {snapshot.State.ToString().ToUpperInvariant()}";
        RecordingOutputText.Text = string.IsNullOrWhiteSpace(snapshot.OutputPath)
            ? "OUTPUT: -"
            : $"OUTPUT: {snapshot.OutputPath}";
        RecordingErrorText.Text = snapshot.ErrorMessage;

        bool commandsAvailable =
            _recordingController is not null && !snapshot.CommandPending;
        StartRecordingButton.IsEnabled = commandsAvailable &&
            snapshot.State is RecordingReviewState.Idle or
                RecordingReviewState.Completed or RecordingReviewState.Failed;
        PauseRecordingButton.IsEnabled = commandsAvailable &&
            snapshot.State == RecordingReviewState.Recording;
        ResumeRecordingButton.IsEnabled = commandsAvailable &&
            snapshot.State == RecordingReviewState.Paused;
        StopRecordingButton.IsEnabled = commandsAvailable &&
            snapshot.State is RecordingReviewState.Recording or
                RecordingReviewState.Paused;
    }

    private void ApplyProductSnapshot(ProductReviewSnapshot snapshot)
    {
        _applyingProductSnapshot = true;
        try
        {
            CaptureModeSelector.SelectedIndex = snapshot.CaptureTargetMode ==
                ProductReviewCaptureTargetMode.Window ? 1 : 0;
            bool window = snapshot.CaptureTargetMode ==
                ProductReviewCaptureTargetMode.Window;
            WindowSelector.IsVisible = window;
            RefreshWindowsButton.IsVisible = window;
            WindowSelector.ItemsSource = snapshot.Windows;
            WindowSelector.SelectedItem = snapshot.Windows.FirstOrDefault(item =>
                string.Equals(
                    item.Id,
                    snapshot.SelectedWindowId,
                    StringComparison.Ordinal));

            MicrophoneEnabledCheckBox.IsChecked = snapshot.MicrophoneEnabled;
            MicrophoneSelector.ItemsSource = snapshot.Microphones;
            MicrophoneSelector.SelectedItem = snapshot.Microphones.FirstOrDefault(
                item => string.Equals(
                    item.Id,
                    snapshot.SelectedMicrophoneId,
                    StringComparison.Ordinal));
            MicrophoneAvailabilityText.Text = snapshot.SelectedMicrophoneAvailable
                ? "当前麦克风：可用"
                : "当前麦克风：不可用（其它功能不受影响）";
            SystemAudioCheckBox.IsChecked = snapshot.SystemAudioEnabled;
            CursorVisibleCheckBox.IsChecked = snapshot.CursorVisible;
            HotkeysEnabledCheckBox.IsChecked = snapshot.HotkeysEnabled;
            HotkeysEnabledCheckBox.Content =
                $"F9 / F10 快捷键 · {snapshot.HotkeyState}";
            AutoDirectorCheckBox.IsChecked = snapshot.AutoDirectorEnabled;
            StandardZoomButton.IsEnabled = snapshot.ManualCommandsEnabled;
            StrongZoomButton.IsEnabled = snapshot.ManualCommandsEnabled;
            ManualZoomText.Text = snapshot.ManualZoom switch
            {
                ProductReviewManualZoom.Standard => "1.6x",
                ProductReviewManualZoom.Strong => "2.0x",
                _ => "1.0x",
            };
            StageOrientationSelector.SelectedItem = snapshot.StageOrientation;
            StageLevelSelector.SelectedItem = snapshot.StageLevel;
            BackgroundPresetSelector.SelectedItem = snapshot.BackgroundPreset;
            CustomBackgroundPathTextBox.Text = snapshot.CustomBackgroundPath;
            OutputRootTextBox.Text = snapshot.OutputRoot;
            ProductStatusText.Text = snapshot.StatusText;

            CaptureModeSelector.IsEnabled = snapshot.SettingsChangeEnabled;
            WindowSelector.IsEnabled = snapshot.SettingsChangeEnabled;
            RefreshWindowsButton.IsEnabled = snapshot.SettingsChangeEnabled;
            MicrophoneEnabledCheckBox.IsEnabled = snapshot.SettingsChangeEnabled;
            MicrophoneSelector.IsEnabled = snapshot.SettingsChangeEnabled;
            SystemAudioCheckBox.IsEnabled = snapshot.SettingsChangeEnabled;
            CursorVisibleCheckBox.IsEnabled = snapshot.SettingsChangeEnabled;
            HotkeysEnabledCheckBox.IsEnabled = snapshot.SettingsChangeEnabled;
            AutoDirectorCheckBox.IsEnabled = snapshot.SettingsChangeEnabled;
            StageOrientationSelector.IsEnabled = snapshot.SettingsChangeEnabled;
            StageLevelSelector.IsEnabled = snapshot.SettingsChangeEnabled;
            BackgroundPresetSelector.IsEnabled = snapshot.SettingsChangeEnabled;
            CustomBackgroundPathTextBox.IsEnabled = snapshot.SettingsChangeEnabled;
            ApplyCustomBackgroundButton.IsEnabled = snapshot.SettingsChangeEnabled;
            OutputRootTextBox.IsEnabled = snapshot.SettingsChangeEnabled;
            ApplyOutputRootButton.IsEnabled = snapshot.SettingsChangeEnabled;
        }
        finally
        {
            _applyingProductSnapshot = false;
        }
    }
}
