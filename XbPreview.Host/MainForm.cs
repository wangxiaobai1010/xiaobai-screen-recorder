using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;

namespace XbPreview.Host;

internal sealed class MainForm : Form
{
    private const int FixedHeaderHeight = 238;
    private const int FixedCommandStripHeight = 64;
    private const int RecordingStripHeight = 48;
    private readonly Panel _previewSurface;
    private readonly Button _startButton;
    private readonly Button _stopButton;
    private readonly Button _standardCameraButton;
    private readonly Button _strongCameraButton;
    private readonly Button _recordingStandardCameraButton;
    private readonly Button _recordingStrongCameraButton;
    private readonly Button _hotkeyToggleButton;
    private readonly Button _selectRegionButton;
    private readonly Button _fullScreenButton;
    private readonly Button _startRecordingButton;
    private readonly Button _stopRecordingButton;
    private readonly Button _openVideoButton;
    private readonly Button _openRecordingFolderButton;
    private readonly ComboBox _captureModeSelector;
    private readonly ComboBox _windowSelector;
    private readonly Button _refreshWindowsButton;
    private readonly CheckBox _systemAudioEnabled;
    private readonly CheckBox _microphoneEnabled;
    private readonly ComboBox _microphoneDeviceSelector;
    private readonly Label _microphoneDeviceStatusLabel;
    private readonly CheckBox _directorEnabled;
    private readonly RadioButton _softStrengthRadio;
    private readonly RadioButton _strongStrengthRadio;
    private readonly CheckBox _cameraEnabled;
    private readonly CheckBox _followEnabled;
    private readonly CheckBox _customCursorEnabled;
    private readonly Label _hotkeyStatusLabel;
    private readonly Label _hotkeyHelpLabel;
    private readonly Label _cameraLabel;
    private readonly Label _followLabel;
    private readonly Label _cursorLabel;
    private readonly Label _wdaLabel;
    private readonly Label _rangeLabel;
    private readonly Label _recordingStateLabel;
    private readonly Label _recordingDurationLabel;
    private readonly Label _recordingPathLabel;
    private readonly Label _recordingErrorLabel;
    private readonly Label _countdownLabel;
    private readonly Label _recordingModeLabel;
    private readonly Label _captureSafetyLabel;
    private Label? _manualCameraPrompt;
    private Label? _directorStrengthPrompt;
    private Control? _shellPreviewCard;
    private readonly TextBox _recordingPathBox;
    private readonly ToolTip _recordingPathToolTip = new();
    private readonly TableLayoutPanel _recoveryNoticePanel;
    private readonly Label _recoveryNoticeLabel;
    private readonly Button _recoveryViewButton;
    private readonly FlowLayoutPanel _recoveryListPanel;
    private readonly ToolTip _recoveryPathToolTip = new();
    private readonly TextBox _statusBox;
    private readonly System.Windows.Forms.Timer _statsTimer;
    private readonly FixedTargetCameraController _cameraController = new();
    private readonly DisplayGeometryProvider _displayGeometryProvider = new();
    private readonly RegionSelectionController _regionSelectionController;
    private readonly ManagedCloseCoordinator _managedCloseCoordinator = new();
    private readonly RawMouseInputObserver _directorInput = new();
    private readonly MinimalRecordingShellActionGate _shellActions = new();
    private readonly bool _directorLiteRequested;
    private readonly DirectorFocusStrength _directorFocusStrengthRequested;
    private readonly Func<string, IStartupSessionInspector>
        _startupInspectorFactory;
    private readonly Func<string, IUserRecoveryService>
        _recoveryServiceFactory;
    private PreviewLifecycleController? _lifecycle;
    private RecordingController? _recordingController;
    private StartupInspectionCoordinator? _startupInspection;
    private RecoveryActionCoordinator? _recoveryActions;
    private CameraDiagnosticLogger? _cameraLogger;
    private ComfortZoneDiagnosticLogger? _followLogger;
    private HotkeyService? _hotkeys;
    private bool _closing;
    private bool _automaticStartAttempted;
    private bool _closeCleanupStarted;
    private bool _closeCleanupComplete;
    private bool _suppressFollowToggle;
    private bool _overlayTransaction;
    private bool _suppressDirectorToggle;
    private bool _windowExclusionSucceeded;
    private CancellationTokenSource? _countdownCancellation;
    private CaptureDisplaySnapshot? _confirmedDisplay;
    private CaptureRegion? _confirmedRegion;
    private SessionGeometry? _lastSessionGeometry;
    private string _cameraLastError = "none";
    private string _followLastError = "none";
    private long _lastCameraUiQpc;
    private long _lastFollowUiQpc;
    private string? _diagnosticLogDirectory;
    private double _lastEngineStopDurationMs;
    private double _lastLifecycleCloseDurationMs;
    private ManagedCloseDiagnostics? _closeDiagnostics;
    private string? _lastSessionGuid;
    private long _startupGeneration;
    private int _startupInspectionScheduleAttempted;
    private bool _recoveryListExpanded;
    private string? _confirmedRecoveredSessionId;
    private StartupInspectionSnapshot _latestStartupInspectionSnapshot =
        StartupInspectionSnapshot.NotStarted;
    private readonly Dictionary<string, string> _recoveryStatusOverrides =
        new(StringComparer.Ordinal);
    private ManagedRecordingSnapshot _recordingUiSnapshot =
        ManagedRecordingSnapshot.Idle;
    private CaptureTarget _selectedCaptureTarget = CaptureTarget.FullScreen;
    private bool _suppressCaptureSelection;
    private bool _windowTargetClosedNotified;
    private bool _suppressMicrophoneDeviceSelection;
    private ulong _microphoneCatalogGeneration = ulong.MaxValue;
    private MicrophoneSelection _microphoneSelection =
        MicrophoneSelectionSettings.Load();

    internal MainForm()
        : this(false, DirectorFocusStrength.Soft)
    {
    }

    internal MainForm(
        bool directorLiteRequested = false,
        DirectorFocusStrength directorFocusStrength =
            DirectorFocusStrength.Soft)
        : this(
            static diagnosticDirectory =>
                new NativeHistoricalSessionInspector(diagnosticDirectory),
            static diagnosticDirectory =>
                new NativeNarrowRecoveryService(diagnosticDirectory),
            directorLiteRequested,
            directorFocusStrength)
    {
    }

    internal MainForm(
        Func<string, IStartupSessionInspector> startupInspectorFactory)
        : this(
            startupInspectorFactory,
            static diagnosticDirectory =>
                new NativeNarrowRecoveryService(diagnosticDirectory),
            false)
    {
    }

    internal MainForm(
        Func<string, IStartupSessionInspector> startupInspectorFactory,
        Func<string, IUserRecoveryService> recoveryServiceFactory,
        bool directorLiteRequested = false,
        DirectorFocusStrength directorFocusStrength =
            DirectorFocusStrength.Soft)
    {
        _startupInspectorFactory = startupInspectorFactory ??
            throw new ArgumentNullException(nameof(startupInspectorFactory));
        _recoveryServiceFactory = recoveryServiceFactory ??
            throw new ArgumentNullException(nameof(recoveryServiceFactory));
        _directorLiteRequested = directorLiteRequested;
        _directorFocusStrengthRequested = directorFocusStrength;
        _directorInput.ActivityObserved += OnDirectorPointerActivity;
        Text = "小白录屏器";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(900, 760);
        MinimumSize = new Size(640, 640);
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = true;
        BackColor = Color.FromArgb(242, 239, 232);
        Font = new Font("Segoe UI", 9.5f);
        KeyPreview = true;

        _startButton = NewButton("启动预览");
        _stopButton = NewButton("停止预览");
        _stopButton.Enabled = false;
        _standardCameraButton = NewButton("重点放大  1.6x");
        _standardCameraButton.Enabled = false;
        _strongCameraButton = NewButton("重点放大  2.0x");
        _strongCameraButton.Enabled = false;
        _recordingStandardCameraButton = NewButton("重点放大  1.6x");
        _recordingStandardCameraButton.Enabled = false;
        _recordingStrongCameraButton = NewButton("重点放大  2.0x");
        _recordingStrongCameraButton.Enabled = false;
        _hotkeyToggleButton = NewButton("启用镜头快捷键");
        _hotkeyToggleButton.Enabled = false;
        _selectRegionButton = NewButton("选择/重新选择区域");
        _fullScreenButton = NewButton("切换为全屏录制");
        _startRecordingButton = NewButton("开始录制");
        _startRecordingButton.Enabled = false;
        _stopRecordingButton = NewButton("停止录制");
        _stopRecordingButton.Enabled = false;
        _openVideoButton = NewButton("打开视频");
        _openVideoButton.Enabled = false;
        _openRecordingFolderButton = NewButton("打开文件夹");
        _openRecordingFolderButton.Enabled = false;
        _captureModeSelector = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 96,
        };
        _captureModeSelector.Items.AddRange(["全屏", "窗口"]);
        _captureModeSelector.SelectedIndex = 0;
        _windowSelector = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 360,
            Visible = false,
        };
        _refreshWindowsButton = NewButton("刷新窗口");
        StyleSecondaryButton(_refreshWindowsButton, 88);
        _refreshWindowsButton.Visible = false;
        _systemAudioEnabled = NewProductToggle("电脑声音", isChecked: true);
        _microphoneEnabled = NewProductToggle("麦克风", isChecked: true);
        _microphoneDeviceSelector = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 300,
            Enabled = false,
        };
        _microphoneDeviceStatusLabel = NewDiagnosticLabel(
            "当前麦克风：正在枚举…");
        _microphoneDeviceStatusLabel.AutoSize = true;
        _directorEnabled = NewProductToggle(
            "自动跟随重点",
            isChecked: directorLiteRequested);
        _softStrengthRadio = NewStrengthRadio("柔和  1.6x", isChecked: true);
        _strongStrengthRadio = NewStrengthRadio("强调  2.0x", isChecked: false);
        if (directorFocusStrength == DirectorFocusStrength.Strong)
        {
            _softStrengthRadio.Checked = false;
            _strongStrengthRadio.Checked = true;
        }
        _recordingStateLabel = NewDiagnosticLabel("准备就绪");
        _recordingDurationLabel = NewDiagnosticLabel("已录时长：00:00:00");
        _recordingPathLabel = NewDiagnosticLabel("输出：—");
        _recordingErrorLabel = NewDiagnosticLabel(string.Empty);
        _countdownLabel = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Segoe UI Semibold", 30.0f),
            ForeColor = Color.FromArgb(42, 41, 38),
            Text = string.Empty,
        };
        _recordingModeLabel = NewDiagnosticLabel("手动重点放大");
        _captureSafetyLabel = NewDiagnosticLabel(string.Empty);
        _captureSafetyLabel.ForeColor = Color.FromArgb(115, 77, 31);
        _recordingPathBox = new TextBox
        {
            ReadOnly = true,
            Width = 360,
            Text = "—",
            TabStop = true,
        };
        foreach (Label label in new[]
        {
            _recordingStateLabel,
            _recordingDurationLabel,
            _recordingPathLabel,
            _recordingErrorLabel,
        })
        {
            label.AutoSize = true;
            label.Dock = DockStyle.None;
            label.Margin = new Padding(4, 6, 4, 0);
        }
        _recoveryNoticeLabel = new Label
        {
            AutoSize = true,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(4, 8, 8, 4),
        };
        _recoveryViewButton = NewButton("查看");
        _recoveryViewButton.Margin = new Padding(4, 2, 4, 2);
        FlowLayoutPanel recoveryHeader = new()
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(8, 3, 8, 3),
        };
        recoveryHeader.Controls.AddRange(
            [_recoveryNoticeLabel, _recoveryViewButton]);
        _recoveryListPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Padding = new Padding(12, 2, 12, 6),
            Visible = false,
        };
        _recoveryNoticePanel = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Color.FromArgb(238, 246, 255),
            Margin = new Padding(8, 0, 8, 4),
            Padding = Padding.Empty,
            Visible = false,
        };
        _recoveryNoticePanel.RowStyles.Add(
            new RowStyle(SizeType.AutoSize));
        _recoveryNoticePanel.RowStyles.Add(
            new RowStyle(SizeType.AutoSize));
        _recoveryNoticePanel.Controls.Add(recoveryHeader, 0, 0);
        _recoveryNoticePanel.Controls.Add(_recoveryListPanel, 0, 1);
        ApplyRegionCaptureProductPolicy();
        _regionSelectionController =
            new RegionSelectionController(_displayGeometryProvider);
        _cameraEnabled = new CheckBox
        {
            Text = "启用固定目标相机",
            Checked = true,
            AutoSize = true,
            Padding = new Padding(10, 9, 0, 0),
        };
        _followEnabled = new CheckBox
        {
            Text = "启用舒适区跟随",
            Checked = true,
            AutoSize = true,
            Padding = new Padding(10, 9, 0, 0),
        };
        _customCursorEnabled = new CheckBox
        {
            Text = "使用自绘鼠标",
            Checked = true,
            AutoSize = true,
            Padding = new Padding(10, 9, 0, 0),
        };
        _hotkeyStatusLabel = NewDiagnosticLabel("镜头快捷键：未启用");
        _hotkeyHelpLabel = NewDiagnosticLabel(
            "启动预览后可手动开启，当前未占用F9和F10");
        _cameraLabel = NewDiagnosticLabel(
            "Camera: Wide 1.0000 @ (0.5000, 0.5000)");
        _followLabel = NewDiagnosticLabel("Follow: WaitingForZoom");
        _cursorLabel = NewDiagnosticLabel(
            "Cursor: requested=CustomCursor; actual=尚未启动");
        _wdaLabel = NewDiagnosticLabel(
            "WDA_EXCLUDEFROMCAPTURE：尚未检查");
        _rangeLabel = NewDiagnosticLabel("当前范围：主显示器全屏");

        FlowLayoutPanel commandBar = new()
        {
            Dock = DockStyle.Fill,
            AutoSize = false,
            AutoScroll = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(8),
        };
        commandBar.Controls.AddRange(
            [
                _startButton,
                _stopButton,
                _standardCameraButton,
                _strongCameraButton,
                _hotkeyToggleButton,
                _selectRegionButton,
                _fullScreenButton,
                _cameraEnabled,
                _followEnabled,
                _customCursorEnabled,
            ]);

        FlowLayoutPanel recordingBar = new()
        {
            Dock = DockStyle.Fill,
            AutoSize = false,
            AutoScroll = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(8, 5, 8, 3),
        };
        recordingBar.Controls.AddRange(
            [
                _startRecordingButton,
                _stopRecordingButton,
                _openVideoButton,
                _openRecordingFolderButton,
                _recordingStateLabel,
                _recordingDurationLabel,
                _recordingPathLabel,
                _recordingPathBox,
                _recordingErrorLabel,
            ]);

        TableLayoutPanel diagnosticBar = new()
        {
            Dock = DockStyle.Fill,
            AutoSize = false,
            ColumnCount = 2,
            RowCount = 4,
            Padding = new Padding(8, 2, 8, 4),
            CellBorderStyle = TableLayoutPanelCellBorderStyle.Single,
        };
        diagnosticBar.ColumnStyles.Add(
            new ColumnStyle(SizeType.Percent, 50));
        diagnosticBar.ColumnStyles.Add(
            new ColumnStyle(SizeType.Percent, 50));
        for (int row = 0; row < 4; row++)
        {
            diagnosticBar.RowStyles.Add(
                new RowStyle(SizeType.Percent, 25));
        }
        diagnosticBar.Controls.Add(_hotkeyStatusLabel, 0, 0);
        diagnosticBar.Controls.Add(_hotkeyHelpLabel, 1, 0);
        diagnosticBar.Controls.Add(_cameraLabel, 0, 1);
        diagnosticBar.Controls.Add(_followLabel, 1, 1);
        diagnosticBar.Controls.Add(_cursorLabel, 0, 2);
        diagnosticBar.Controls.Add(_wdaLabel, 1, 2);
        diagnosticBar.Controls.Add(_rangeLabel, 0, 3);
        diagnosticBar.SetColumnSpan(_rangeLabel, 2);

        TableLayoutPanel fixedHeader = new()
        {
            Dock = DockStyle.Fill,
            AutoSize = false,
            ColumnCount = 1,
            RowCount = 3,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
        };
        fixedHeader.RowStyles.Add(
            new RowStyle(SizeType.Absolute, FixedCommandStripHeight));
        fixedHeader.RowStyles.Add(
            new RowStyle(SizeType.Absolute, RecordingStripHeight));
        fixedHeader.RowStyles.Add(
            new RowStyle(SizeType.Percent, 100));
        fixedHeader.Controls.Add(commandBar, 0, 0);
        fixedHeader.Controls.Add(recordingBar, 0, 1);
        fixedHeader.Controls.Add(diagnosticBar, 0, 2);

        _previewSurface = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Black,
            Margin = new Padding(8),
            BorderStyle = BorderStyle.FixedSingle,
        };
        _statusBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Font = new Font(FontFamily.GenericMonospace, 9.0f),
            BackColor = SystemColors.Window,
            TabStop = false,
        };

        TableLayoutPanel layout = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
        };
        layout.RowStyles.Add(
            new RowStyle(SizeType.Absolute, FixedHeaderHeight));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 225));
        layout.Controls.Add(fixedHeader, 0, 0);
        layout.Controls.Add(_recoveryNoticePanel, 0, 1);
        layout.Controls.Add(_previewSurface, 0, 2);
        layout.Controls.Add(_statusBox, 0, 3);
        Controls.Add(layout);
        Controls.Clear();
        Controls.Add(BuildMinimalProductLayout());

        _startButton.Click += OnStartButtonClick;
        _stopButton.Click += OnStopButtonClick;
        _startRecordingButton.Click += OnStartRecordingButtonClick;
        _stopRecordingButton.Click += OnStopRecordingButtonClick;
        _openVideoButton.Click += OnOpenVideoButtonClick;
        _openRecordingFolderButton.Click += OnOpenRecordingFolderButtonClick;
        _captureModeSelector.SelectedIndexChanged += OnCaptureModeChanged;
        _windowSelector.SelectedIndexChanged += OnWindowSelectionChanged;
        _refreshWindowsButton.Click += (_, _) => RefreshWindowChoices();
        _recoveryViewButton.Click += OnRecoveryViewButtonClick;
        _systemAudioEnabled.CheckedChanged += OnAudioSelectionChanged;
        _microphoneEnabled.CheckedChanged += OnAudioSelectionChanged;
        _microphoneDeviceSelector.SelectedIndexChanged +=
            OnMicrophoneDeviceSelectionChanged;
        _directorEnabled.CheckedChanged += OnDirectorEnabledChanged;
        _softStrengthRadio.CheckedChanged += OnDirectorStrengthChanged;
        _strongStrengthRadio.CheckedChanged += OnDirectorStrengthChanged;
        _standardCameraButton.Click += (_, _) =>
            ExecuteCameraCommand(CameraCommand.ToggleStandardCloseUp);
        _strongCameraButton.Click += (_, _) =>
            ExecuteCameraCommand(CameraCommand.ToggleStrongCloseUp);
        _recordingStandardCameraButton.Click += (_, _) =>
            ExecuteCameraCommand(CameraCommand.ToggleStandardCloseUp);
        _recordingStrongCameraButton.Click += (_, _) =>
            ExecuteCameraCommand(CameraCommand.ToggleStrongCloseUp);
        _hotkeyToggleButton.Click += (_, _) => ToggleCameraHotkeys();
        _selectRegionButton.Click += async (_, _) =>
        {
            try
            {
                await ProductFeatures.TryExecuteRegionCaptureCommandAsync(
                    SelectCustomRegionAsync);
            }
            catch (Exception error)
            {
                ShowFatalError("区域 Geometry 配置失败", error);
            }
        };
        _fullScreenButton.Click += async (_, _) =>
        {
            try
            {
                await ProductFeatures.TryExecuteRegionCaptureCommandAsync(
                    RestoreFullScreenAsync);
            }
            catch (Exception error)
            {
                ShowFatalError("全屏 Geometry 配置失败", error);
            }
        };
        _cameraEnabled.CheckedChanged += (_, _) =>
        {
            _cameraController.SetEnabled(
                _cameraEnabled.Checked,
                Stopwatch.GetTimestamp());
            AppendStatus(_cameraEnabled.Checked
                ? "固定目标相机已启用。"
                : "固定目标相机已禁用；已回退为 P0 全景预览。");
        };
        _followEnabled.CheckedChanged += (_, _) =>
        {
            if (_suppressFollowToggle)
            {
                return;
            }
            _lifecycle?.SetFollowEnabled(_followEnabled.Checked);
            AppendStatus(_followEnabled.Checked
                ? "舒适区跟随已启用；不会瞬移，越过新边界后才跟随。"
                : "舒适区跟随已禁用；保留当前中心和固定目标镜头。");
        };
        _customCursorEnabled.CheckedChanged += async (_, _) =>
        {
            if (_lifecycle?.State is not (
                PreviewLifecycleState.Stopped or
                PreviewLifecycleState.Error))
            {
                return;
            }
            await ConfigureSelectedCursorModeAsync();
        };
        _previewSurface.SizeChanged += OnPreviewSurfaceSizeChanged;
        FormClosing += OnFormClosing;
        FormClosed += OnFormClosed;

        _statsTimer = new System.Windows.Forms.Timer { Interval = 500 };
        _statsTimer.Tick += (_, _) =>
        {
            RefreshStats();
            RefreshMicrophoneDevices();
        };
        UpdateProductShellControls(_recordingUiSnapshot);
        WriteManagedStartupDiagnostic(new ManagedStartupDiagnosticEvent
        {
            ManagedStage = "MainFormConstructed",
            LifecycleState = PreviewLifecycleState.NotInitialized.ToString(),
            Result = "success",
        });
    }

    private Control BuildMinimalProductLayout()
    {
        Color paper = Color.FromArgb(242, 239, 232);
        Color card = Color.FromArgb(250, 248, 243);
        Color ink = Color.FromArgb(42, 41, 38);
        Color line = Color.FromArgb(188, 184, 174);

        TableLayoutPanel content = new()
        {
            Dock = DockStyle.Fill,
            AutoSize = false,
            ColumnCount = 1,
            RowCount = 5,
            Padding = new Padding(10),
            BackColor = paper,
        };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 120));
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 154));
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        TableLayoutPanel heading = new()
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 1,
            Margin = new Padding(0, 0, 0, 6),
        };
        Label title = NewProductLabel("小白录屏器", 18.0f, FontStyle.Bold);
        Label subtitle = NewProductLabel(
            "全屏录制 · 声音与镜头，都在开始前准备好",
            9.0f,
            FontStyle.Regular);
        subtitle.ForeColor = Color.FromArgb(96, 93, 86);
        heading.Controls.Add(title);
        heading.Controls.Add(subtitle);
        FlowLayoutPanel captureRow = NewProductFlow();
        captureRow.WrapContents = false;
        captureRow.Controls.Add(NewProductLabel(
            "录制范围",
            9.0f,
            FontStyle.Bold));
        captureRow.Controls.Add(_captureModeSelector);
        captureRow.Controls.Add(_windowSelector);
        captureRow.Controls.Add(_refreshWindowsButton);
        heading.Controls.Add(captureRow);
        content.Controls.Add(heading, 0, 0);

        TableLayoutPanel cameraCard = NewProductCard(card, line, 1);
        cameraCard.Dock = DockStyle.Fill;
        cameraCard.AutoSize = false;
        cameraCard.RowCount = 3;
        cameraCard.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        cameraCard.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        cameraCard.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        cameraCard.Margin = new Padding(0, 0, 0, 6);
        FlowLayoutPanel directorRow = NewProductFlow();
        directorRow.WrapContents = false;
        directorRow.Controls.Add(NewProductLabel("镜头", 10.0f, FontStyle.Bold));
        directorRow.Controls.Add(_directorEnabled);
        directorRow.Controls.Add(NewProductLabel("导演强度", 8.5f, FontStyle.Regular));
        directorRow.Controls.AddRange([_softStrengthRadio, _strongStrengthRadio]);
        cameraCard.Controls.Add(directorRow, 0, 0);
        _manualCameraPrompt = NewProductLabel(
            "重点放大",
            8.5f,
            FontStyle.Regular);
        FlowLayoutPanel manualRow = NewProductFlow();
        manualRow.WrapContents = false;
        manualRow.Controls.Add(_manualCameraPrompt);
        manualRow.Controls.AddRange(
            [_standardCameraButton, _strongCameraButton, _hotkeyToggleButton]);
        manualRow.Controls.Add(_recordingModeLabel);
        cameraCard.Controls.Add(manualRow, 0, 1);
        _directorStrengthPrompt = NewProductLabel(
            "镜头快捷键",
            8.5f,
            FontStyle.Regular);
        FlowLayoutPanel hotkeyRow = NewProductFlow();
        hotkeyRow.WrapContents = false;
        _hotkeyStatusLabel.AutoSize = true;
        _hotkeyStatusLabel.Dock = DockStyle.None;
        _hotkeyHelpLabel.AutoSize = true;
        _hotkeyHelpLabel.Dock = DockStyle.None;
        hotkeyRow.Controls.Add(_directorStrengthPrompt);
        hotkeyRow.Controls.AddRange([_hotkeyStatusLabel, _hotkeyHelpLabel]);
        cameraCard.Controls.Add(hotkeyRow, 0, 2);
        content.Controls.Add(cameraCard, 0, 1);

        TableLayoutPanel previewCard = NewProductCard(card, line, 1);
        previewCard.Dock = DockStyle.Fill;
        previewCard.AutoSize = false;
        previewCard.RowCount = 2;
        previewCard.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        previewCard.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        previewCard.MinimumSize = new Size(0, 180);
        previewCard.Margin = new Padding(0, 0, 0, 6);
        previewCard.Controls.Add(NewProductLabel(
            "画面预览",
            9.5f,
            FontStyle.Bold), 0, 0);
        _previewSurface.Dock = DockStyle.Fill;
        _previewSurface.Margin = new Padding(0, 7, 0, 0);
        previewCard.Controls.Add(_previewSurface, 0, 1);
        _shellPreviewCard = previewCard;
        content.Controls.Add(previewCard, 0, 2);

        TableLayoutPanel recordingCard = NewProductCard(card, line, 1);
        recordingCard.Dock = DockStyle.Fill;
        recordingCard.AutoSize = false;
        recordingCard.RowCount = 4;
        recordingCard.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        recordingCard.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        recordingCard.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        recordingCard.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        recordingCard.Margin = new Padding(0, 0, 0, 6);
        FlowLayoutPanel stateRow = NewProductFlow();
        stateRow.WrapContents = false;
        stateRow.Controls.AddRange([_recordingStateLabel, _recordingDurationLabel]);
        stateRow.Controls.Add(NewProductLabel("声音", 8.5f, FontStyle.Regular));
        stateRow.Controls.AddRange([_systemAudioEnabled, _microphoneEnabled]);
        recordingCard.Controls.Add(stateRow, 0, 0);
        FlowLayoutPanel microphoneRow = NewProductFlow();
        microphoneRow.WrapContents = false;
        microphoneRow.Controls.Add(NewProductLabel(
            "麦克风设备",
            8.5f,
            FontStyle.Regular));
        microphoneRow.Controls.Add(_microphoneDeviceSelector);
        microphoneRow.Controls.Add(_microphoneDeviceStatusLabel);
        recordingCard.Controls.Add(microphoneRow, 0, 1);
        _countdownLabel.Height = 48;
        _countdownLabel.Visible = false;
        recordingCard.Controls.Add(_countdownLabel, 0, 2);

        FlowLayoutPanel actions = NewProductFlow();
        actions.WrapContents = false;
        StylePrimaryButton(_startRecordingButton);
        StyleStopButton(_stopRecordingButton);
        _startRecordingButton.Size = new Size(160, 42);
        _stopRecordingButton.Size = new Size(150, 42);
        StyleSecondaryButton(_openVideoButton, 100);
        StyleSecondaryButton(_openRecordingFolderButton, 100);
        actions.Controls.AddRange(
            [
                _startRecordingButton,
                _stopRecordingButton,
                _openVideoButton,
                _openRecordingFolderButton,
            ]);
        recordingCard.Controls.Add(actions, 0, 2);

        FlowLayoutPanel resultRow = NewProductFlow();
        resultRow.WrapContents = false;
        _recordingPathBox.Dock = DockStyle.None;
        _recordingPathBox.Margin = new Padding(0, 0, 8, 0);
        _recordingPathBox.BackColor = Color.FromArgb(246, 244, 238);
        _recordingPathBox.BorderStyle = BorderStyle.FixedSingle;
        resultRow.Controls.AddRange(
            [_recordingPathLabel, _recordingPathBox, _recordingErrorLabel,
                _captureSafetyLabel]);
        recordingCard.Controls.Add(resultRow, 0, 3);
        content.Controls.Add(recordingCard, 0, 3);

        _recoveryNoticePanel.Margin = new Padding(0);
        content.Controls.Add(_recoveryNoticePanel, 0, 4);

        foreach (Label label in new[]
        {
            _recordingStateLabel,
            _recordingDurationLabel,
            _recordingModeLabel,
            _recordingPathLabel,
            _recordingErrorLabel,
            _microphoneDeviceStatusLabel,
            _captureSafetyLabel,
        })
        {
            label.AutoSize = true;
            label.Dock = DockStyle.None;
            label.Padding = new Padding(0, 4, 12, 4);
            label.ForeColor = ink;
        }
        _recordingErrorLabel.ForeColor = Color.FromArgb(168, 48, 42);
        _captureSafetyLabel.ForeColor = Color.FromArgb(115, 77, 31);

        StyleSecondaryButton(_standardCameraButton, 106);
        StyleSecondaryButton(_strongCameraButton, 106);
        StyleSecondaryButton(_hotkeyToggleButton, 132);
        UpdateProductToggleAppearance(_systemAudioEnabled, "电脑声音");
        UpdateProductToggleAppearance(_microphoneEnabled, "麦克风");
        UpdateProductToggleAppearance(_directorEnabled, "自动跟随重点");

        return content;
    }

    private static TableLayoutPanel NewProductCard(
        Color backColor,
        Color borderColor,
        int columnCount)
    {
        TableLayoutPanel card = new()
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = columnCount,
            Padding = new Padding(12, 10, 12, 10),
            BackColor = backColor,
            CellBorderStyle = TableLayoutPanelCellBorderStyle.None,
        };
        card.Paint += (_, e) =>
            ControlPaint.DrawBorder(e.Graphics, card.ClientRectangle, borderColor,
                ButtonBorderStyle.Solid);
        return card;
    }

    private static FlowLayoutPanel NewProductFlow() => new()
    {
        Dock = DockStyle.Top,
        AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink,
        FlowDirection = FlowDirection.LeftToRight,
        WrapContents = true,
        Margin = new Padding(0, 4, 0, 0),
        Padding = Padding.Empty,
    };

    private static Label NewProductLabel(
        string text,
        float size,
        FontStyle style) => new()
    {
        AutoSize = true,
        Text = text,
        Font = new Font("Segoe UI", size, style),
        ForeColor = Color.FromArgb(42, 41, 38),
        Margin = Padding.Empty,
    };

    private static void StylePrimaryButton(Button button)
    {
        button.AutoSize = false;
        button.Size = new Size(210, 48);
        button.Text = "开始录制";
        button.Font = new Font("Segoe UI Semibold", 11.0f);
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 1;
        button.BackColor = Color.FromArgb(42, 41, 38);
        button.ForeColor = Color.White;
        button.Margin = new Padding(0, 4, 8, 4);
    }

    private static void StyleStopButton(Button button)
    {
        button.AutoSize = false;
        button.Size = new Size(150, 48);
        button.Text = "停止并保存";
        button.Font = new Font("Segoe UI Semibold", 10.0f);
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 1;
        button.BackColor = Color.FromArgb(174, 47, 43);
        button.ForeColor = Color.White;
        button.Margin = new Padding(0, 4, 8, 4);
    }

    private static void StyleSecondaryButton(Button button, int width)
    {
        button.AutoSize = false;
        button.Size = new Size(width, 38);
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 1;
        button.FlatAppearance.BorderColor = Color.FromArgb(146, 142, 133);
        button.BackColor = Color.FromArgb(246, 244, 238);
        button.ForeColor = Color.FromArgb(42, 41, 38);
        button.Margin = new Padding(0, 4, 8, 4);
        button.Padding = Padding.Empty;
    }

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);
        long startupGeneration = Interlocked.Increment(
            ref _startupGeneration);
        WriteManagedStartupDiagnostic(new ManagedStartupDiagnosticEvent
        {
            ManagedStage = "MainForm.OnShown",
            LifecycleState =
                (_lifecycle?.State ?? PreviewLifecycleState.NotInitialized).
                    ToString(),
            Result = "begin",
        });
        try
        {
            _previewSurface.CreateControl();
            if (!IsHandleCreated ||
                !_previewSurface.IsHandleCreated ||
                _previewSurface.ClientSize.Width <= 0 ||
                _previewSurface.ClientSize.Height <= 0)
            {
                throw new InvalidOperationException(
                    "Preview window handles or client size are not ready.");
            }
            _windowExclusionSucceeded = false;
            _captureSafetyLabel.Text = "正在确认控制窗口录制排除能力";
            string logDirectory = Path.Combine(
                AppContext.BaseDirectory,
                "diagnostic-logs");
            Directory.CreateDirectory(logDirectory);
            _diagnosticLogDirectory = logDirectory;
            ManagedStartupDiagnostics.Configure(logDirectory);
            WriteManagedStartupDiagnostic(new ManagedStartupDiagnosticEvent
            {
                ManagedStage = "PreviewSurfaceHandleConfirmed",
                LifecycleState = PreviewLifecycleState.NotInitialized.ToString(),
                Result = "success",
            });
            _cameraLogger = new CameraDiagnosticLogger(logDirectory);
            _followLogger = new ComfortZoneDiagnosticLogger(logDirectory);
            _hotkeys = new HotkeyService(Handle);
            _lifecycle = new PreviewLifecycleController(
                () => NativePreviewSession.Create(
                    _previewSurface.Handle,
                    Handle,
                    logDirectory),
                (session, followEnabled) => new CameraUpdateService(
                    _cameraController,
                    session,
                    _cameraLogger!,
                    _followLogger!,
                    followEnabled,
                    ReadActiveCaptureCursorObservation),
                _cameraController,
                SetHotkeyPreviewAvailable,
                (state, result, detail) =>
                    _cameraLogger?.Write(state, result, detail: detail),
                writeStartupDiagnostic: WriteManagedStartupDiagnostic);
            _lifecycle.StateChanged += OnPreviewLifecycleStateChanged;
            _lifecycle.CameraStatePublished += OnCameraStatePublished;
            _lifecycle.FollowStatePublished += OnFollowStatePublished;
            PreviewLifecycleResult initialized =
                await _lifecycle.InitializeAsync();
            PreviewLifecycleController lifecycle = _lifecycle;
            if (!CanContinueStartup(startupGeneration, lifecycle))
            {
                return;
            }
            if (!initialized.Succeeded)
            {
                throw new InvalidOperationException(
                    initialized.Error ?? "Preview lifecycle initialization failed.");
            }
            TryScheduleStartupInspection(logDirectory);
            _recordingController =
                lifecycle.GetOrCreateRecordingController();
            InitializeMicrophoneSelection();
            RefreshRecordingUi();
            await lifecycle.RequestResizeAsync(
                _previewSurface.ClientSize.Width,
                _previewSurface.ClientSize.Height);
            if (!CanContinueStartup(startupGeneration, lifecycle))
            {
                return;
            }
            SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
            _statsTimer.Start();
            AppendStatus(
                $"预览已就绪；F9/F10 尚未注册。Camera log: {_cameraLogger.LogFilePath}; " +
                $"Follow log: {_followLogger.LogFilePath}");
            RefreshStats();
            if (!CanContinueStartup(startupGeneration, lifecycle))
            {
                return;
            }
            if (!_automaticStartAttempted)
            {
                _automaticStartAttempted = true;
                WriteManagedStartupDiagnostic(
                    new ManagedStartupDiagnosticEvent
                    {
                        ManagedStage = "AutomaticStartRequested",
                        LifecycleState = _lifecycle.State.ToString(),
                        Result = "begin",
                    });
                await StartPreviewAsync(isAutomatic: true);
            }
        }
        catch (Exception error)
        {
            if (CanReportStartupFailure(startupGeneration))
            {
                ShowFatalError("初始化预览失败", error);
            }
        }
    }

    private bool CanContinueStartup(
        long startupGeneration,
        PreviewLifecycleController lifecycle) =>
        Volatile.Read(ref _startupGeneration) == startupGeneration &&
        !_closing &&
        !IsDisposed &&
        !Disposing &&
        ReferenceEquals(_lifecycle, lifecycle);

    private bool CanReportStartupFailure(long startupGeneration) =>
        Volatile.Read(ref _startupGeneration) == startupGeneration &&
        !_closing &&
        !IsDisposed &&
        !Disposing;

    private static async Task ObserveStartupInspectionAsync(
        Task<StartupInspectionSnapshot> task)
    {
        _ = await task.ConfigureAwait(false);
        // The coordinator converts every outcome into an immutable terminal
        // snapshot. Awaiting here observes the task without coupling Preview
        // startup or RecordingController to historical inspection.
    }

    private void TryScheduleStartupInspection(string diagnosticDirectory)
    {
        if (Interlocked.CompareExchange(
                ref _startupInspectionScheduleAttempted,
                1,
                0) != 0)
        {
            return;
        }

        StartupInspectionCoordinator? startupInspection = null;
        try
        {
            startupInspection = new StartupInspectionCoordinator(
                _startupInspectorFactory(diagnosticDirectory));
            _startupInspection = startupInspection;
            startupInspection.SnapshotChanged +=
                OnStartupInspectionSnapshotChanged;
            _ = ObserveStartupInspectionAsync(
                startupInspection.StartAsync());
            TryCreateRecoveryActions(diagnosticDirectory);
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
            TryRecordStartupInspection(new StartupInspectionSnapshot(
                0,
                StartupInspectionState.Failed,
                null,
                $"{error.GetType().Name}: {error.Message}"));
        }
    }

    private void TryCreateRecoveryActions(string diagnosticDirectory)
    {
        if (_recoveryActions is not null)
        {
            return;
        }
        try
        {
            RecoveryActionCoordinator actions = new(
                _recoveryServiceFactory(diagnosticDirectory),
                _startupInspectorFactory(diagnosticDirectory));
            actions.SnapshotChanged += OnRecoveryAttemptSnapshotChanged;
            _recoveryActions = actions;
        }
        catch (Exception error)
        {
            Debug.WriteLine(
                $"Historical recording recovery setup failed: {error}");
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
                $"Historical Session inspection cleanup failed: {error}");
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
        if (InvokeRequired)
        {
            try
            {
                BeginInvoke((Action)(() =>
                {
                    if (CanDeliverStartupInspection(source, snapshot))
                    {
                        TryRecordStartupInspection(snapshot);
                    }
                }));
            }
            catch (InvalidOperationException) when (
                IsDisposed || Disposing || _closing)
            {
            }
            return;
        }

        if (CanDeliverStartupInspection(source, snapshot))
        {
            TryRecordStartupInspection(snapshot);
        }
    }

    private bool CanDeliverStartupInspection(
        StartupInspectionCoordinator? source,
        StartupInspectionSnapshot snapshot) =>
        source is not null &&
        ReferenceEquals(_startupInspection, source) &&
        source.CurrentSnapshot == snapshot &&
        !_closing &&
        !IsDisposed &&
        !Disposing;

    private void RecordStartupInspection(StartupInspectionSnapshot snapshot)
    {
        _latestStartupInspectionSnapshot = snapshot;
        StartupInspectionResult? result = snapshot.Result;
        WriteManagedStartupDiagnostic(new ManagedStartupDiagnosticEvent
        {
            ManagedStage = $"HistoricalSessionInspection.{snapshot.State}",
            LifecycleState =
                (_lifecycle?.State ?? PreviewLifecycleState.NotInitialized).
                    ToString(),
            NativeHResult = result is null
                ? null
                : $"0x{unchecked((uint)result.DiagnosticHResult):X8}",
            Result = snapshot.Error ??
                (result is null
                    ? snapshot.State.ToString()
                    : $"{result.Status}; sessions={result.SessionCount}; " +
                      $"truncated={result.Truncated}"),
        });
        RenderRecoveryPresentation();
    }

    private void TryRecordStartupInspection(
        StartupInspectionSnapshot snapshot)
    {
        try
        {
            RecordStartupInspection(snapshot);
        }
        catch (Exception error)
        {
            Debug.WriteLine(
                $"Historical Session inspection diagnostic failed: {error}");
        }
    }

    private void OnRecoveryViewButtonClick(object? sender, EventArgs e)
    {
        _recoveryListExpanded = !_recoveryListExpanded;
        _recoveryListPanel.Visible = _recoveryListExpanded;
        _recoveryViewButton.Text = _recoveryListExpanded ? "收起" : "查看";
    }

    private void RenderRecoveryPresentation()
    {
        UserRecoveryPresentation presentation =
            UserRecoveryPresentation.Create(
                _latestStartupInspectionSnapshot,
                _confirmedRecoveredSessionId,
                _recoveryStatusOverrides);
        foreach (Control control in
            _recoveryListPanel.Controls.Cast<Control>().ToArray())
        {
            control.Dispose();
        }
        _recoveryListPanel.Controls.Clear();
        _recoveryNoticePanel.Visible = presentation.Visible;
        if (!presentation.Visible)
        {
            _recoveryListExpanded = false;
            _recoveryListPanel.Visible = false;
            _recoveryNoticeLabel.Text = string.Empty;
            _recoveryViewButton.Text = "查看";
            return;
        }

        _recoveryNoticeLabel.Text = presentation.NoticeText;
        RecoveryAttemptSnapshot attempt =
            _recoveryActions?.CurrentSnapshot ??
            RecoveryAttemptSnapshot.NotStarted;
        bool recoveryRunning =
            attempt.State == RecoveryAttemptState.Running;
        foreach (UserRecoveryCandidate candidate in presentation.Candidates)
        {
            FlowLayoutPanel row = new()
            {
                AutoSize = true,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
                Margin = new Padding(0, 2, 0, 4),
                Padding = Padding.Empty,
            };
            Label text = new()
            {
                AutoSize = true,
                Text = $"{candidate.Title}：{candidate.StatusText}",
                Margin = new Padding(0, 7, 8, 0),
            };
            row.Controls.Add(text);
            if (!string.IsNullOrEmpty(candidate.DisplaySafePath))
            {
                Label path = new()
                {
                    AutoSize = true,
                    AutoEllipsis = true,
                    MaximumSize = new Size(430, 28),
                    Text = candidate.DisplaySafePath,
                    Margin = new Padding(0, 7, 8, 0),
                };
                _recoveryPathToolTip.SetToolTip(
                    path,
                    candidate.DisplaySafePath);
                row.Controls.Add(path);
            }
            bool failedThisRun = attempt.IsTerminal &&
                !attempt.ConfirmedRecovered &&
                string.Equals(
                    attempt.SessionId,
                    candidate.SessionId,
                    StringComparison.Ordinal);
            if (candidate.CanTryRecovery && _recoveryActions is not null &&
                !failedThisRun)
            {
                Button action = NewButton(
                    recoveryRunning ? "正在检查…" : "尝试恢复");
                action.Enabled = !recoveryRunning;
                action.Tag = candidate;
                action.Click += OnTryRecoveryButtonClick;
                row.Controls.Add(action);
            }
            _recoveryListPanel.Controls.Add(row);
        }
        _recoveryListPanel.Visible = _recoveryListExpanded;
        _recoveryViewButton.Text = _recoveryListExpanded ? "收起" : "查看";
    }

    private async void OnTryRecoveryButtonClick(object? sender, EventArgs e)
    {
        if (_closing || IsDisposed || Disposing ||
            sender is not Button { Tag: UserRecoveryCandidate candidate } ||
            _recoveryActions is null)
        {
            return;
        }
        try
        {
            _ = await _recoveryActions.StartAsync(candidate);
        }
        catch (Exception error)
        {
            if (!_closing && !IsDisposed && !Disposing)
            {
                _recoveryStatusOverrides[candidate.SessionId] =
                    "暂时无法读取这段录制，请稍后再试。文件不会被删除。";
                RenderRecoveryPresentation();
            }
            Debug.WriteLine($"Explicit recovery request failed: {error}");
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
        if (InvokeRequired)
        {
            try
            {
                BeginInvoke((Action)(() =>
                {
                    if (CanDeliverRecoveryAttempt(source, snapshot))
                    {
                        RecordRecoveryAttempt(snapshot);
                    }
                }));
            }
            catch (InvalidOperationException) when (
                IsDisposed || Disposing || _closing)
            {
            }
            return;
        }
        if (CanDeliverRecoveryAttempt(source, snapshot))
        {
            RecordRecoveryAttempt(snapshot);
        }
    }

    private bool CanDeliverRecoveryAttempt(
        RecoveryActionCoordinator? source,
        RecoveryAttemptSnapshot snapshot) =>
        source is not null &&
        ReferenceEquals(_recoveryActions, source) &&
        source.CurrentSnapshot == snapshot &&
        !_closing && !IsDisposed && !Disposing;

    private void RecordRecoveryAttempt(RecoveryAttemptSnapshot snapshot)
    {
        NarrowRecoveryResult? native = snapshot.NativeResult;
        WriteManagedStartupDiagnostic(new ManagedStartupDiagnosticEvent
        {
            ManagedStage = $"ExplicitRecovery.{snapshot.State}",
            LifecycleState =
                (_lifecycle?.State ?? PreviewLifecycleState.NotInitialized).
                    ToString(),
            NativeHResult = native is null
                ? null
                : $"0x{unchecked((uint)native.DiagnosticHResult):X8}",
            Result = native?.Status.ToString() ??
                snapshot.Error ?? snapshot.State.ToString(),
        });

        if (snapshot.State == RecoveryAttemptState.Running)
        {
            _recoveryStatusOverrides[snapshot.SessionId] =
                snapshot.UserMessage;
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

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        uint virtualKey = (uint)(keyData & Keys.KeyCode);
        if (HotkeyBindings.TryResolveVirtualKey(
            virtualKey,
            out HotkeyBinding binding))
        {
            // Enabled keys are delivered by WM_HOTKEY. When disabled, do not
            // consume or dispatch the local key route: F9/F10 belong to other
            // software again.
            if (_hotkeys?.CanDispatch(binding) == true)
            {
                return true;
            }
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void WndProc(ref Message message)
    {
        _directorInput.ProcessMessage(message.Msg, message.LParam);
        if (message.Msg == HotkeyService.WmHotkey)
        {
            if (HotkeyBindings.TryResolveId(
                message.WParam.ToInt32(),
                out HotkeyBinding binding))
            {
                if (_hotkeys?.CanDispatch(binding) == true)
                {
                    ExecuteCameraCommand(binding.Command);
                }
                // Ignore queued WM_HOTKEY messages that arrive after disable.
                return;
            }
        }
        base.WndProc(ref message);
    }

    private async void OnStartButtonClick(object? sender, EventArgs e)
    {
        try
        {
            await StartPreviewAsync(isAutomatic: false);
        }
        catch (Exception error)
        {
            ShowFatalError("启动预览生命周期失败", error);
        }
    }

    private async void OnStopButtonClick(object? sender, EventArgs e)
    {
        try
        {
            await StopPreviewAsync();
        }
        catch (Exception error)
        {
            ShowFatalError("停止预览生命周期失败", error);
        }
    }

    private void OnPreviewLifecycleStateChanged(
        PreviewLifecycleSnapshot snapshot)
    {
        if (IsDisposed || Disposing || _closing)
        {
            return;
        }
        if (InvokeRequired)
        {
            BeginInvoke(() => OnPreviewLifecycleStateChanged(snapshot));
            return;
        }

        UpdateLifecycleControls();
        if (snapshot.State == PreviewLifecycleState.Error &&
            !string.IsNullOrWhiteSpace(snapshot.LastError))
        {
            WriteManagedStartupDiagnostic(
                new ManagedStartupDiagnosticEvent
                {
                    ManagedStage = "UiEnteredErrorState",
                    LifecycleState = snapshot.State.ToString(),
                    RetryAvailable = _startButton.Enabled,
                    Result = "error-visible-retry-available",
                });
            AppendStatus($"Preview lifecycle Error：{snapshot.LastError}");
        }
    }

    private void UpdateLifecycleControls()
    {
        PreviewLifecycleState state =
            _lifecycle?.State ?? PreviewLifecycleState.NotInitialized;
        bool canStart =
            !_closing &&
            !_overlayTransaction &&
            (state is PreviewLifecycleState.Stopped or
                PreviewLifecycleState.Error);
        _startButton.Enabled = canStart;
        _stopButton.Enabled =
            !_closing &&
            !_overlayTransaction &&
            !RecordingBlocksPreviewChanges() &&
            (state is PreviewLifecycleState.Starting or
                PreviewLifecycleState.Previewing);
        _customCursorEnabled.Enabled =
            state is PreviewLifecycleState.Stopped or
                PreviewLifecycleState.Error;
        bool customRegionActive =
            _lifecycle?.IsCustomRegionPreview == true;
        bool cameraCommandsEnabled =
            state == PreviewLifecycleState.Previewing &&
            _cameraEnabled.Checked &&
            !customRegionActive;
        _standardCameraButton.Enabled = cameraCommandsEnabled;
        _strongCameraButton.Enabled = cameraCommandsEnabled;
        _cameraEnabled.Enabled = !customRegionActive;
        _followEnabled.Enabled = !customRegionActive;
        _customCursorEnabled.Enabled &=
            !customRegionActive;
        bool captureTargetChangeEnabled = !_closing &&
            !RecordingBlocksPreviewChanges() &&
            state is PreviewLifecycleState.Stopped or
                PreviewLifecycleState.Previewing or
                PreviewLifecycleState.Error;
        _captureModeSelector.Enabled = captureTargetChangeEnabled;
        _windowSelector.Enabled = captureTargetChangeEnabled;
        _refreshWindowsButton.Enabled = captureTargetChangeEnabled;
        RegionCaptureUiPolicy regionPolicy =
            ProductFeatures.RegionCaptureUi;
        _selectRegionButton.Enabled =
            regionPolicy.Enabled && CanSelectRegion();
        _fullScreenButton.Enabled =
            regionPolicy.Enabled &&
            CanSelectRegion() &&
            _lifecycle?.CurrentRangeMode ==
                CaptureRangeMode.CustomRegion;
        UpdateHotkeyUi();
        UpdateRecordingControls();
    }

    private async Task StartPreviewAsync(bool isAutomatic)
    {
        if (_lifecycle is null ||
            _closing ||
            _overlayTransaction)
        {
            return;
        }

        SessionStartPlan plan;
        try
        {
            CaptureDisplaySnapshot currentDisplay =
                _displayGeometryProvider.ReadPrimaryDisplay();
            CaptureRangeMode userRangeMode =
                ProductFeatures.ResolveUserCaptureRangeMode(
                    _lifecycle.CurrentRangeMode);
            plan = SessionGeometryPlanner.CreateStartPlan(
                userRangeMode,
                currentDisplay,
                userRangeMode == CaptureRangeMode.CustomRegion
                    ? _confirmedDisplay
                    : null,
                userRangeMode == CaptureRangeMode.CustomRegion
                    ? _confirmedRegion
                    : null,
                _overlayTransaction);
        }
        catch (Exception error)
        {
            if (_lifecycle.CurrentRangeMode ==
                CaptureRangeMode.CustomRegion)
            {
                ClearCustomRegion(
                    "显示配置已经变化，旧自定义区域已清除，请重新选择。");
            }
            AppendStatus($"SessionGeometry 创建失败：{error.Message}");
            MessageBox.Show(
                this,
                error.Message,
                "录制范围不可用",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            RefreshStats();
            return;
        }

        if (!plan.StartNativePreview)
        {
            AppendStatus(plan.Message);
            RefreshStats();
            return;
        }

        PreviewLifecycleResult geometryResult =
            await _lifecycle.SetDesiredGeometryAsync(plan.Geometry);
        if (!geometryResult.Succeeded)
        {
            AppendStatus(
                $"SessionGeometry 未接受：{geometryResult.Status}; " +
                $"{geometryResult.Error}");
            RefreshStats();
            return;
        }
        _lastSessionGeometry = plan.Geometry;

        _startButton.Enabled = false;
        AppendStatus(isAutomatic
            ? "正在自动启动全屏预览…"
            : "正在启动预览…");
        PreviewLifecycleResult result = await _lifecycle.StartAsync(
            _cameraEnabled.Checked,
            _followEnabled.Checked,
            SelectedCursorMode());
        ApplyCommittedGeometryFromController();
        if (_lifecycle.State == PreviewLifecycleState.Previewing)
        {
            if (_directorLiteRequested)
            {
                if (_cameraController.SetDirectorFocusStrength(
                    _directorFocusStrengthRequested,
                    out string focusStatus))
                {
                    AppendStatus(focusStatus);
                    SetDirectorLiteEnabled(true);
                }
                else
                {
                    AppendStatus(focusStatus);
                }
            }
            AppendStatus(
                "预览中 · 未录制；WGC/GPU 全屏预览已启动；镜头快捷键默认关闭，" +
                "倍率按钮可直接使用。");
        }
        else
        {
            AppendStatus(
                $"Start 未进入 Previewing：{result.Status}; {result.Error}");
        }
        RefreshStats();
    }

    private async Task StopPreviewAsync()
    {
        if (_lifecycle is null || _overlayTransaction)
        {
            return;
        }

        SetDirectorLiteEnabled(false);

        AppendStatus("停止预览：已释放F9和F10。");
        PreviewLifecycleResult result = await _lifecycle.StopAsync();
        AppendStatus(
            result.Succeeded
                ? $"Stopped：{result.Status}"
                : $"Stop 失败：{result.Error}");
        RefreshStats();
    }

    private void OnAudioSelectionChanged(object? sender, EventArgs e)
    {
        if (_closing)
        {
            return;
        }
        _recordingErrorLabel.Text = string.Empty;
        UpdateProductToggleAppearance(_systemAudioEnabled, "电脑声音");
        UpdateProductToggleAppearance(_microphoneEnabled, "麦克风");
    }

    private void InitializeMicrophoneSelection()
    {
        RecordingController? controller = _recordingController;
        if (controller is null)
        {
            return;
        }
        NativeMethods.Result result =
            controller.SetMicrophoneSelection(_microphoneSelection);
        if (result != NativeMethods.Result.Ok)
        {
            throw new InvalidOperationException(
                $"麦克风选择未被 native 接受：{result}");
        }
        RefreshMicrophoneDevices(force: true);
    }

    private void RefreshMicrophoneDevices(bool force = false)
    {
        RecordingController? controller = _recordingController;
        if (controller is null || _closing)
        {
            return;
        }
        try
        {
            MicrophoneDeviceCatalog catalog =
                controller.GetMicrophoneDevices();
            if (force || catalog.Generation != _microphoneCatalogGeneration)
            {
                RebuildMicrophoneDeviceChoices(catalog);
                _microphoneCatalogGeneration = catalog.Generation;
            }
            MicrophoneSelectionStatus status =
                controller.GetMicrophoneSelection();
            string displayName = !string.IsNullOrWhiteSpace(status.DisplayName)
                ? status.DisplayName
                : !string.IsNullOrWhiteSpace(_microphoneSelection.DisplayName)
                    ? _microphoneSelection.DisplayName
                    : "Windows 默认麦克风";
            _microphoneDeviceStatusLabel.Text = status.Available
                ? $"当前麦克风：{displayName}"
                : $"当前麦克风：{displayName}（不可用）";
            _microphoneDeviceStatusLabel.ForeColor = status.Available
                ? Color.FromArgb(42, 41, 38)
                : Color.FromArgb(168, 48, 42);
        }
        catch (Exception error)
        {
            Debug.WriteLine($"Microphone device refresh failed: {error}");
            _microphoneDeviceStatusLabel.Text =
                "当前麦克风：设备枚举不可用";
            _microphoneDeviceStatusLabel.ForeColor =
                Color.FromArgb(168, 48, 42);
        }
    }

    private void RebuildMicrophoneDeviceChoices(
        MicrophoneDeviceCatalog catalog)
    {
        _suppressMicrophoneDeviceSelection = true;
        try
        {
            List<MicrophoneDeviceChoice> choices =
            [
                new MicrophoneDeviceChoice(
                    new MicrophoneSelection(
                        MicrophoneSelectionKind.WindowsDefault,
                        string.Empty,
                        catalog.DefaultDisplayName),
                    catalog.DefaultAvailable),
            ];
            choices.AddRange(catalog.Devices.Select(device =>
                new MicrophoneDeviceChoice(
                    new MicrophoneSelection(
                        MicrophoneSelectionKind.ConcreteEndpoint,
                        device.EndpointId,
                        device.DisplayName),
                    true)));

            if (_microphoneSelection.Kind ==
                    MicrophoneSelectionKind.ConcreteEndpoint &&
                !choices.Any(choice =>
                    choice.Selection.Kind ==
                        MicrophoneSelectionKind.ConcreteEndpoint &&
                    string.Equals(
                        choice.Selection.EndpointId,
                        _microphoneSelection.EndpointId,
                        StringComparison.Ordinal)))
            {
                choices.Add(new MicrophoneDeviceChoice(
                    _microphoneSelection,
                    false));
            }

            _microphoneDeviceSelector.BeginUpdate();
            try
            {
                _microphoneDeviceSelector.Items.Clear();
                _microphoneDeviceSelector.Items.AddRange(choices.ToArray());
                int selectedIndex = choices.FindIndex(choice =>
                    choice.Selection.Kind == _microphoneSelection.Kind &&
                    (_microphoneSelection.Kind ==
                            MicrophoneSelectionKind.WindowsDefault ||
                        string.Equals(
                            choice.Selection.EndpointId,
                            _microphoneSelection.EndpointId,
                            StringComparison.Ordinal)));
                _microphoneDeviceSelector.SelectedIndex =
                    selectedIndex >= 0 ? selectedIndex : 0;
            }
            finally
            {
                _microphoneDeviceSelector.EndUpdate();
            }
        }
        finally
        {
            _suppressMicrophoneDeviceSelection = false;
        }
    }

    private void OnMicrophoneDeviceSelectionChanged(
        object? sender,
        EventArgs e)
    {
        if (_suppressMicrophoneDeviceSelection || _closing ||
            _microphoneDeviceSelector.SelectedItem is not
                MicrophoneDeviceChoice choice ||
            _recordingController is null)
        {
            return;
        }
        NativeMethods.Result result =
            _recordingController.SetMicrophoneSelection(choice.Selection);
        if (result != NativeMethods.Result.Ok)
        {
            _recordingErrorLabel.Text =
                $"无法选择该麦克风：{result}";
            RefreshMicrophoneDevices(force: true);
            return;
        }
        _microphoneSelection = choice.Selection;
        try
        {
            MicrophoneSelectionSettings.Save(_microphoneSelection);
            _recordingErrorLabel.Text = string.Empty;
        }
        catch (Exception error) when (
            error is IOException or UnauthorizedAccessException)
        {
            _recordingErrorLabel.Text =
                "麦克风已切换，但无法保存用户选择。";
        }
        RefreshMicrophoneDevices(force: true);
    }

    private void OnDirectorEnabledChanged(object? sender, EventArgs e)
    {
        if (_suppressDirectorToggle || _closing)
        {
            return;
        }
        bool active = _shellActions.CountdownActive ||
            _recordingUiSnapshot.IsActive;
        if (active)
        {
            SynchronizeDirectorToggle(
                _cameraController.Owner == CameraOwner.DirectorLite);
            return;
        }

        bool accepted;
        if (_directorEnabled.Checked)
        {
            DirectorFocusStrength strength = _strongStrengthRadio.Checked
                ? DirectorFocusStrength.Strong
                : DirectorFocusStrength.Soft;
            accepted = _cameraController.SetDirectorFocusStrength(
                strength,
                out string strengthStatus);
            AppendStatus(strengthStatus);
            accepted = accepted && SetDirectorLiteEnabled(true);
        }
        else
        {
            accepted = SetDirectorLiteEnabled(false);
        }
        if (!accepted)
        {
            SynchronizeDirectorToggle(
                _cameraController.Owner == CameraOwner.DirectorLite);
        }
        UpdateProductShellControls(_recordingUiSnapshot);
    }

    private void OnDirectorStrengthChanged(object? sender, EventArgs e)
    {
        RadioButton? selected = sender as RadioButton;
        if (selected?.Checked != true || _suppressDirectorToggle || _closing)
        {
            return;
        }
        if (_shellActions.CountdownActive || _recordingUiSnapshot.IsActive)
        {
            SynchronizeStrengthSelection(
                _cameraController.DirectorFocusStrength);
            return;
        }

        DirectorFocusStrength strength = _strongStrengthRadio.Checked
            ? DirectorFocusStrength.Strong
            : DirectorFocusStrength.Soft;
        bool directorWasEnabled =
            _cameraController.Owner == CameraOwner.DirectorLite;
        if (directorWasEnabled && !SetDirectorLiteEnabled(false))
        {
            SynchronizeStrengthSelection(
                _cameraController.DirectorFocusStrength);
            return;
        }
        bool accepted = _cameraController.SetDirectorFocusStrength(
            strength,
            out string status);
        AppendStatus(status);
        if (directorWasEnabled)
        {
            accepted = accepted && SetDirectorLiteEnabled(true);
        }
        if (!accepted)
        {
            SynchronizeStrengthSelection(
                _cameraController.DirectorFocusStrength);
        }
        UpdateProductShellControls(_recordingUiSnapshot);
    }

    private void SynchronizeDirectorToggle(bool enabled)
    {
        _suppressDirectorToggle = true;
        _directorEnabled.Checked = enabled;
        _suppressDirectorToggle = false;
        UpdateProductToggleAppearance(_directorEnabled, "自动跟随重点");
    }

    private void SynchronizeStrengthSelection(
        DirectorFocusStrength strength)
    {
        _suppressDirectorToggle = true;
        _softStrengthRadio.Checked = strength == DirectorFocusStrength.Soft;
        _strongStrengthRadio.Checked = strength == DirectorFocusStrength.Strong;
        _suppressDirectorToggle = false;
    }

    private async void OnStartRecordingButtonClick(
        object? sender,
        EventArgs e)
    {
        RecordingController? controller = _recordingController;
        if (controller is null || _closing ||
            _lifecycle?.IsPreviewing != true ||
            !_shellActions.TryBeginCountdown())
        {
            return;
        }

        _countdownCancellation?.Dispose();
        _countdownCancellation = new CancellationTokenSource();
        CancellationToken cancellationToken = _countdownCancellation.Token;
        try
        {
            NativeMethods.AudioProgramMode audioMode =
                MinimalRecordingShellPolicy.NativeAudioMode(
                    _systemAudioEnabled.Checked,
                    _microphoneEnabled.Checked);
            NativeMethods.Result audioModeResult =
                controller.SetAudioProgramMode(audioMode);
            if (audioModeResult != NativeMethods.Result.Ok)
            {
                throw new InvalidOperationException(
                    $"声音模式未被接受：{audioModeResult}");
            }
            if (_microphoneEnabled.Checked)
            {
                MicrophoneSelectionStatus microphone =
                    controller.GetMicrophoneSelection();
                if (!microphone.Available)
                {
                    _recordingErrorLabel.Text =
                        MicrophoneAvailabilityContract.UserMessage;
                    AppendStatus(MicrophoneAvailabilityContract.UserMessage);
                    RefreshMicrophoneDevices(force: true);
                    return;
                }
            }
            RefreshRecordingUi();
            for (int value = 3; value >= 1; value--)
            {
                cancellationToken.ThrowIfCancellationRequested();
                _countdownLabel.Text = value.ToString();
                _countdownLabel.Visible = true;
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            }
            cancellationToken.ThrowIfCancellationRequested();
            PrepareProductWindowForRecording();
            ManagedRecordingSnapshot snapshot = await controller.StartAsync();
            RefreshRecordingUi(snapshot);
            if (!snapshot.IsActive)
            {
                RestoreProductWindowAfterRecording();
            }
        }
        catch (OperationCanceledException)
        {
            AppendStatus("录制倒计时已取消。");
        }
        catch (Exception error)
        {
            AppendStatus($"开始录制失败：{error.Message}");
            _recordingErrorLabel.Text = "无法开始录制，请重试。";
            RestoreProductWindowAfterRecording();
        }
        finally
        {
            _countdownLabel.Visible = false;
            _countdownLabel.Text = string.Empty;
            _shellActions.EndCountdown();
            _countdownCancellation?.Dispose();
            _countdownCancellation = null;
            RefreshRecordingUi();
        }
    }

    private async void OnStopRecordingButtonClick(
        object? sender,
        EventArgs e)
    {
        RecordingController? controller = _recordingController;
        if (controller is null || _closing)
        {
            return;
        }
        if (!_shellActions.TryRequestStop())
        {
            return;
        }

        _stopRecordingButton.Enabled = false;
        try
        {
            ManagedRecordingSnapshot snapshot = await controller.StopAsync();
            RefreshRecordingUi(snapshot);
        }
        catch (Exception error)
        {
            AppendStatus($"停止录制失败：{error.Message}");
            _recordingErrorLabel.Text = "停止或保存失败，请保留恢复材料。";
            RefreshRecordingUi();
        }
        finally
        {
            RestoreProductWindowAfterRecording();
        }
    }

    private void PrepareProductWindowForRecording()
    {
        // Recording state must not own the Director Monitor's window state.
        // Capture exclusion is reported separately and never converts Start
        // into an implicit minimize/resize operation.
        if (!_windowExclusionSucceeded)
        {
            _captureSafetyLabel.Text =
                "控制窗口排除未确认；窗口尺寸保持不变";
        }
    }

    private async void OnCaptureModeChanged(object? sender, EventArgs e)
    {
        if (_suppressCaptureSelection)
        {
            return;
        }
        bool windowMode = _captureModeSelector.SelectedIndex == 1;
        if (windowMode && RecordingBlocksPreviewChanges())
        {
            AppendStatus("录制期间不能切换全屏/窗口捕获。");
            RestoreCaptureSelectionUi();
            return;
        }
        _windowSelector.Visible = windowMode;
        _refreshWindowsButton.Visible = windowMode;
        if (windowMode)
        {
            RefreshWindowChoices();
            AppendStatus("请选择一个要录制的应用窗口。");
            return;
        }
        try
        {
            await ApplyCaptureTargetAsync(CaptureTarget.FullScreen);
        }
        catch (Exception error)
        {
            AppendStatus($"切换全屏捕获失败：{error.Message}");
            RestoreCaptureSelectionUi();
        }
    }

    private async void OnWindowSelectionChanged(object? sender, EventArgs e)
    {
        if (_suppressCaptureSelection ||
            _windowSelector.SelectedItem is not WindowCaptureChoice choice)
        {
            return;
        }
        try
        {
            await ApplyCaptureTargetAsync(new CaptureTarget(
                CaptureTargetKind.Window,
                choice.Handle,
                choice.Title));
        }
        catch (Exception error)
        {
            AppendStatus($"选择窗口失败：{error.Message}");
            RestoreCaptureSelectionUi();
        }
    }

    private void RefreshWindowChoices()
    {
        nint selectedHandle = _selectedCaptureTarget.WindowHandle;
        _suppressCaptureSelection = true;
        try
        {
            _windowSelector.Items.Clear();
            foreach (WindowCaptureChoice choice in WindowCaptureSelector.Enumerate())
            {
                _windowSelector.Items.Add(choice);
                if (choice.Handle == selectedHandle)
                {
                    _windowSelector.SelectedItem = choice;
                }
            }
        }
        finally
        {
            _suppressCaptureSelection = false;
        }
    }

    private async Task ApplyCaptureTargetAsync(CaptureTarget target)
    {
        PreviewLifecycleController? lifecycle = _lifecycle;
        if (lifecycle is null || _closing)
        {
            return;
        }
        if (RecordingBlocksPreviewChanges())
        {
            AppendStatus("录制期间不能切换全屏/窗口捕获。");
            RestoreCaptureSelectionUi();
            return;
        }

        SetDirectorLiteEnabled(false);
        if (lifecycle.State is PreviewLifecycleState.Previewing or
            PreviewLifecycleState.Error)
        {
            PreviewLifecycleResult stop = await lifecycle.StopAsync();
            if (!stop.Succeeded)
            {
                AppendStatus($"切换捕获目标前停止 Preview 失败：{stop.Error}");
                RestoreCaptureSelectionUi();
                return;
            }
        }

        PreviewLifecycleResult configured =
            await lifecycle.SetCaptureTargetAsync(target);
        if (!configured.Succeeded)
        {
            AppendStatus($"窗口捕获目标未接受：{configured.Error}");
            RestoreCaptureSelectionUi();
            return;
        }

        _selectedCaptureTarget = target;
        _windowTargetClosedNotified = false;
        RestoreCaptureSelectionUi();
        AppendStatus(target.IsWindow
            ? $"录制范围已切换为窗口：{target.Title}"
            : "录制范围已切换为全屏。");
        await StartPreviewAsync(isAutomatic: false);
    }

    private void RestoreCaptureSelectionUi()
    {
        _suppressCaptureSelection = true;
        try
        {
            _captureModeSelector.SelectedIndex =
                _selectedCaptureTarget.IsWindow ? 1 : 0;
            _windowSelector.Visible = _selectedCaptureTarget.IsWindow;
            _refreshWindowsButton.Visible = _selectedCaptureTarget.IsWindow;
            if (_selectedCaptureTarget.IsWindow)
            {
                for (int index = 0; index < _windowSelector.Items.Count; index++)
                {
                    if (_windowSelector.Items[index] is WindowCaptureChoice choice &&
                        choice.Handle == _selectedCaptureTarget.WindowHandle)
                    {
                        _windowSelector.SelectedIndex = index;
                        break;
                    }
                }
            }
        }
        finally
        {
            _suppressCaptureSelection = false;
        }
    }

    private void RestoreProductWindowAfterRecording()
    {
        // Symmetric no-op: Stop preserves the user's current bounds/state.
    }

    private void OnOpenVideoButtonClick(object? sender, EventArgs e)
    {
        ManagedRecordingSnapshot snapshot =
            _recordingController?.CurrentSnapshot ??
            ManagedRecordingSnapshot.Idle;
        if (!RecordingOutputActions.CanOpenVideo(snapshot))
        {
            RefreshRecordingUi(snapshot);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(snapshot.PublishedPath)
            {
                UseShellExecute = true,
            });
        }
        catch (Exception error)
        {
            AppendStatus($"打开录制视频失败：{error.Message}");
        }
    }

    private void OnOpenRecordingFolderButtonClick(
        object? sender,
        EventArgs e)
    {
        ManagedRecordingSnapshot snapshot =
            _recordingController?.CurrentSnapshot ??
            ManagedRecordingSnapshot.Idle;
        if (!RecordingOutputActions.CanOpenFolder(snapshot))
        {
            RefreshRecordingUi(snapshot);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe")
            {
                Arguments = $"/select,\"{snapshot.PublishedPath}\"",
                UseShellExecute = true,
            });
        }
        catch (Exception error)
        {
            AppendStatus($"打开录制文件夹失败：{error.Message}");
        }
    }

    private bool RecordingBlocksPreviewChanges() =>
        _recordingController?.CurrentSnapshot.IsActive == true;

    private void UpdateRecordingControls()
    {
        UpdateRecordingControls(_recordingUiSnapshot);
    }

    private void UpdateRecordingControls(ManagedRecordingSnapshot snapshot)
    {
        bool pending = _recordingController?.HasPendingOperation == true;
        bool previewing = _lifecycle?.IsPreviewing == true;
        RecordingOutputPresentation output =
            RecordingOutputActions.Describe(snapshot);
        MinimalShellControlState shell =
            MinimalRecordingShellPolicy.Resolve(
                snapshot,
                _shellActions.CountdownActive,
                previewing,
                pending,
                _cameraController.Owner == CameraOwner.DirectorLite);
        _startRecordingButton.Enabled = !_closing && shell.CanStart;
        _stopRecordingButton.Enabled = !_closing && shell.CanStop;
        _openVideoButton.Enabled = !_closing && output.CanOpenVideo;
        _openRecordingFolderButton.Enabled =
            !_closing && output.CanOpenFolder;
        // Keep the thin control strip geometrically stable across all phases.
        _startRecordingButton.Visible = true;
        _stopRecordingButton.Visible = true;
        _openVideoButton.Visible = true;
        _openRecordingFolderButton.Visible = true;
        UpdateProductShellControls(snapshot);
    }

    private void UpdateProductShellControls(ManagedRecordingSnapshot snapshot)
    {
        bool pending = _recordingController?.HasPendingOperation == true;
        bool previewing = _lifecycle?.IsPreviewing == true;
        bool directorEnabled =
            _cameraController.Owner == CameraOwner.DirectorLite;
        MinimalShellControlState shell =
            MinimalRecordingShellPolicy.Resolve(
                snapshot,
                _shellActions.CountdownActive,
                previewing,
                pending,
                directorEnabled);

        _systemAudioEnabled.Enabled = !_closing && shell.CanChangeAudio;
        _microphoneEnabled.Enabled = !_closing && shell.CanChangeAudio;
        _microphoneDeviceSelector.Enabled =
            !_closing && shell.CanChangeAudio &&
            _recordingController is not null;
        _directorEnabled.Enabled = !_closing && shell.CanChangeDirector;
        _softStrengthRadio.Enabled = !_closing && shell.CanChangeStrength;
        _strongStrengthRadio.Enabled = !_closing && shell.CanChangeStrength;
        // Manual and Director controls share a fixed-height strip. Only their
        // enabled state changes; visibility never reflows the Preview.
        _softStrengthRadio.Visible = true;
        _strongStrengthRadio.Visible = true;
        _standardCameraButton.Visible = true;
        _strongCameraButton.Visible = true;
        _standardCameraButton.Enabled =
            !_closing && shell.CanUseManualCamera;
        _strongCameraButton.Enabled =
            !_closing && shell.CanUseManualCamera;
        bool compactManual =
            (shell.Phase is MinimalShellPhase.Recording or
                MinimalShellPhase.Stopping) &&
            !directorEnabled;
        _recordingStandardCameraButton.Visible = compactManual;
        _recordingStrongCameraButton.Visible = compactManual;
        _recordingStandardCameraButton.Enabled =
            !_closing && shell.CanUseManualCamera;
        _recordingStrongCameraButton.Enabled =
            !_closing && shell.CanUseManualCamera;

        _recordingStateLabel.Text = shell.Phase == MinimalShellPhase.Recording
            ? "● REC · 正在录制"
            : shell.StateText;
        _recordingStateLabel.ForeColor = shell.Phase switch
        {
            MinimalShellPhase.Recording or MinimalShellPhase.Failed =>
                Color.FromArgb(174, 47, 43),
            _ => Color.FromArgb(42, 41, 38),
        };
        _recordingDurationLabel.Text =
            $"已录时长  {FormatRecordingDuration(shell.Elapsed)}";
        UpdateCameraModeLabel();
        ApplyCompactRecordingPresentation(
            shell.Phase is MinimalShellPhase.Recording or
                MinimalShellPhase.Stopping);
        UpdateProductToggleAppearance(_systemAudioEnabled, "电脑声音");
        UpdateProductToggleAppearance(_microphoneEnabled, "麦克风");
        UpdateProductToggleAppearance(_directorEnabled, "自动跟随重点");
    }

    private void ApplyCompactRecordingPresentation(bool compact)
    {
        // The monitor is already large before recording. Recording transitions
        // update text/enabled state only and intentionally do not reflow it.
    }

    private void RefreshRecordingUi(
        ManagedRecordingSnapshot? knownSnapshot = null)
    {
        RecordingController? controller = _recordingController;
        if (controller is null && knownSnapshot is null)
        {
            UpdateRecordingControls();
            return;
        }

        ManagedRecordingSnapshot snapshot = knownSnapshot ??
            controller!.RefreshSnapshot();
        _recordingUiSnapshot = snapshot;
        _shellActions.Observe(snapshot);
        RecordingOutputPresentation output =
            RecordingOutputActions.Describe(snapshot);
        _recordingPathBox.Text = output.PathText;
        _recordingPathLabel.Text = output.StatusText;
        _recordingPathToolTip.SetToolTip(
            _recordingPathBox,
            output.PathText == "—" ? string.Empty : output.PathText);
        MinimalShellControlState shell = MinimalRecordingShellPolicy.Resolve(
            snapshot,
            _shellActions.CountdownActive,
            _lifecycle?.IsPreviewing == true,
            controller?.HasPendingOperation == true,
            _cameraController.Owner == CameraOwner.DirectorLite);
        _recordingPathLabel.Visible = shell.ShowCompletion || shell.ShowFailure;
        _recordingPathBox.Visible = shell.ShowCompletion || shell.ShowFailure;
        _recordingErrorLabel.Text = shell.ShowFailure
            ? snapshot.State == ManagedRecordingState.Failed
                ? $"录制失败：{DescribeRecordingFailure(snapshot)}"
                : "录制没有完成安全发布，请保留恢复材料。"
            : !string.IsNullOrWhiteSpace(snapshot.ErrorMessage)
                ? $"提醒：{DescribeRecordingFailure(snapshot)}"
                : string.Empty;
        UpdateRecordingControls(snapshot);
        if (snapshot.IsActive)
        {
            _stopButton.Enabled = false;
            _selectRegionButton.Enabled = false;
            _fullScreenButton.Enabled = false;
        }
    }

    private static string FormatRecordingDuration(TimeSpan elapsed)
    {
        long totalHours = Math.Max(0, (long)elapsed.TotalHours);
        return $"{totalHours:00}:{elapsed.Minutes:00}:{elapsed.Seconds:00}";
    }

    private static string DescribeRecordingFailure(
        ManagedRecordingSnapshot snapshot)
    {
        string detail = RecordingFailurePresentation.Describe(snapshot);
        if (snapshot.OutputCleanupAttempted &&
            !snapshot.OutputCleanupSucceeded)
        {
            detail +=
                $"；输出清理 0x{snapshot.OutputCleanupHResult:X8}";
        }
        const int MaxUiErrorLength = 160;
        return detail.Length <= MaxUiErrorLength
            ? detail
            : detail[..MaxUiErrorLength] + "…";
    }

    private void ToggleCameraHotkeys()
    {
        if (_lifecycle?.IsPreviewing != true ||
            _hotkeys?.CanToggle != true)
        {
            return;
        }

        if (_hotkeys.UserEnabled)
        {
            _hotkeys.Disable();
            AppendStatus(
                "镜头快捷键已关闭；F9、F10已交还其他软件，当前镜头状态保持不变。");
        }
        else
        {
            HotkeyRegistrationResult result = _hotkeys.Enable();
            if (result.Succeeded)
            {
                AppendStatus(
                    "镜头快捷键已开启：F9=1.6x标准特写，F10=2.0x强特写。");
            }
            else if (result.FailedBinding is HotkeyBinding failed)
            {
                AppendStatus(
                    $"镜头快捷键保持关闭：{failed.DisplayName} 注册失败，" +
                    $"Windows错误码={result.WindowsErrorCode}；" +
                    "仍可使用1.6x和2.0x界面按钮。");
            }
        }

        UpdateHotkeyUi();
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

    private async Task SelectCustomRegionAsync()
    {
        if (_lifecycle is null || _closing || !CanSelectRegion())
        {
            return;
        }

        PreviewLifecycleResult transaction =
            await _lifecycle.ReconfigureRegionAsync(
                (request, cancellationToken) =>
                {
                    _overlayTransaction = true;
                    UpdateLifecycleControls();
                    try
                    {
                        RegionSelectionResult result =
                            _regionSelectionController.SelectRegion(
                                this,
                                request.InitialSelection,
                                cancellationToken);
                        WindowDisplayAffinityResult wda =
                            _regionSelectionController.LastWdaResult;
                        AppendStatus(wda.Succeeded
                            ? "Region selector WDA_EXCLUDEFROMCAPTURE: succeeded."
                            : $"Region selector WDA warning: Windows error {wda.WindowsErrorCode}.");
                        if (result.Confirmed)
                        {
                            SessionGeometry geometry = SessionGeometry.Create(
                                result.Display!,
                                result.Region!.Value,
                                OutputCanvas.CreateIdentity(
                                    result.Region.Value));
                            return GeometrySelectionResult.Confirmed(geometry);
                        }
                        return result.CancelReason ==
                            RegionSelectionCancelReason.Error
                                ? GeometrySelectionResult.Failed(
                                    result.Detail ??
                                    "Region selection failed safely.")
                                : GeometrySelectionResult.Cancelled();
                    }
                    finally
                    {
                        _overlayTransaction = false;
                    }
                },
                CurrentRequestedRuntimeSettings());

        ApplyCommittedGeometryFromController();
        if (transaction.Status == PreviewLifecycleOperationStatus.Failed)
        {
            AppendStatus(
                $"The new region was not applied; rollback was attempted: {transaction.Error}");
        }
        else if (transaction.Status ==
            PreviewLifecycleOperationStatus.Succeeded)
        {
            SessionGeometry? current = _lifecycle.CurrentGeometry;
            if (current is not null &&
                _lifecycle.CurrentRangeMode ==
                    CaptureRangeMode.CustomRegion)
            {
                AppendStatus(
                    $"Custom region Preview is active: {current.CaptureRegion.Width} x {current.CaptureRegion.Height}; Camera=Wide 1.0; Follow=Off; Cursor=SystemCursor.");
            }
        }
        UpdateRangeUi();
        RefreshStats();
    }

    private async Task RestoreFullScreenAsync()
    {
        if (_lifecycle is null || _closing || _overlayTransaction)
        {
            return;
        }

        try
        {
            SessionGeometry fullScreen =
                SessionGeometry.CreateFullScreen(
                    _displayGeometryProvider.ReadPrimaryDisplay());
            PreviewLifecycleResult result =
                await _lifecycle.ReconfigureGeometryAsync(
                    fullScreen,
                    CurrentRequestedRuntimeSettings());
            ApplyCommittedGeometryFromController();
            AppendStatus(result.Succeeded
                ? "Full-screen Preview is active; Camera and Follow controls are available."
                : $"Full-screen reconfiguration failed; rollback was attempted: {result.Error}");
        }
        catch (Exception error)
        {
            AppendStatus(
                $"Reading or applying full-screen geometry failed: {error.Message}");
        }
        UpdateRangeUi();
        RefreshStats();
    }

    private PreviewRuntimeSettings CurrentRequestedRuntimeSettings() =>
        new(
            _cameraEnabled.Checked,
            _followEnabled.Checked,
            SelectedCursorMode(),
            CameraCommandsAvailable: true);

    private void ApplyCommittedGeometryFromController()
    {
        SessionGeometry? committed = _lifecycle?.CurrentGeometry;
        if (committed is null)
        {
            return;
        }
        _lastSessionGeometry = committed;
        if (_lifecycle?.CurrentRangeMode ==
            CaptureRangeMode.CustomRegion)
        {
            _confirmedDisplay = committed.CaptureDisplay;
            _confirmedRegion = committed.CaptureRegion;
        }
        else
        {
            _confirmedDisplay = null;
            _confirmedRegion = null;
        }
    }

    private void ApplyRegionCaptureProductPolicy()
    {
        RegionCaptureUiPolicy policy = ProductFeatures.RegionCaptureUi;
        _selectRegionButton.Visible = policy.Visible;
        _selectRegionButton.Enabled = policy.Enabled;
        _selectRegionButton.TabStop = policy.TabStop;
        _fullScreenButton.Visible = policy.Visible;
        _fullScreenButton.Enabled = policy.Enabled;
        _fullScreenButton.TabStop = policy.TabStop;
    }

    private bool CanSelectRegion() =>
        ProductFeatures.RegionCaptureEnabled &&
        !RecordingBlocksPreviewChanges() &&
        _lifecycle?.CanReconfigureRegion == true &&
        RegionSelectionAvailability.CanSelectRegion(
            _closing,
            _lifecycle?.State ?? PreviewLifecycleState.NotInitialized,
            _overlayTransaction);

#if false // P1d-a2.3: retained only as non-compiled P1d-a2.2 flow reference.
    private async Task SelectCustomRegionLegacyAsync()
    {
        if (_lifecycle is null)
        {
            return;
        }

        if (!_lifecycle.TryReadStats(
            out NativeMethods.PreviewStats stats,
            out _,
            out string? readError))
        {
            AppendStatus(
                $"无法读取预览状态：{readError ?? "生命周期事务正在执行"}");
            return;
        }
        if (!CanSelectRegion(stats.State))
        {
            AppendStatus("区域选择只允许在预览完全停止且无其他Overlay事务时打开。");
            return;
        }

        _overlayTransaction = true;
        _startButton.Enabled = false;
        _stopButton.Enabled = false;
        _selectRegionButton.Enabled = false;
        _fullScreenButton.Enabled = false;
        try
        {
            RegionSelectionResult result =
                _regionSelectionController.SelectRegion(this, _confirmedRegion);
            WindowDisplayAffinityResult wda =
                _regionSelectionController.LastWdaResult;
            AppendStatus(wda.Succeeded
                ? "选区Overlay WDA_EXCLUDEFROMCAPTURE：成功。"
                : $"选区Overlay WDA_EXCLUDEFROMCAPTURE：警告，Windows错误码={wda.WindowsErrorCode}。");

            if (result.Confirmed)
            {
                SessionGeometry geometry = SessionGeometry.Create(
                    result.Display!,
                    result.Region!.Value,
                    OutputCanvas.CreateIdentity(result.Region.Value));
                PreviewLifecycleResult geometryResult =
                    await _lifecycle.ReconfigureGeometryAsync(
                        geometry,
                        CurrentRequestedRuntimeSettings());
                if (!geometryResult.Succeeded)
                {
                    AppendStatus(
                        $"自定义区域 Geometry 未接受：{geometryResult.Error}");
                    return;
                }
                _confirmedDisplay = result.Display;
                _confirmedRegion = result.Region;
                _lastSessionGeometry = geometry;
                AppendStatus(
                    $"已确认物理选区：{result.Region.Value.Width} × " +
                    $"{result.Region.Value.Height}；Geometry 已保存为待应用配置。" +
                    "本阶段 Renderer 仍显示全屏。");
            }
            else if (result.CancelReason == RegionSelectionCancelReason.DisplayChanged)
            {
                AppendStatus("显示配置发生变化，本轮选择已取消；请重新选择。");
                MessageBox.Show(
                    this,
                    "显示配置已变化，本轮区域没有提交，请重新选择。",
                    "显示配置变化",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
            else if (result.CancelReason == RegionSelectionCancelReason.Error)
            {
                AppendStatus($"区域选择已安全取消：{result.Detail}");
            }
            else
            {
                AppendStatus("区域选择已取消；保留原有录制范围。");
            }
        }
        finally
        {
            _overlayTransaction = false;
            UpdateRangeUi();
            RefreshStats();
        }
    }

    private async Task RestoreFullScreenLegacyAsync()
    {
        if (_lifecycle is null ||
            _closing ||
            _lifecycle.IsPreviewing ||
            _overlayTransaction)
        {
            return;
        }
        try
        {
            SessionGeometry geometry = SessionGeometry.CreateFullScreen(
                _displayGeometryProvider.ReadPrimaryDisplay());
            PreviewLifecycleResult geometryResult =
                await _lifecycle.ReconfigureGeometryAsync(
                    geometry,
                    CurrentRequestedRuntimeSettings());
            if (!geometryResult.Succeeded)
            {
                AppendStatus($"全屏 Geometry 未接受：{geometryResult.Error}");
                return;
            }
            _confirmedDisplay = null;
            _confirmedRegion = null;
            _lastSessionGeometry = geometry;
        }
        catch (Exception error)
        {
            _lastSessionGeometry = null;
            AppendStatus($"读取主显示器全屏范围失败：{error.Message}");
        }
        AppendStatus("录制范围已恢复为当前主显示器全屏。");
        UpdateRangeUi();
        RefreshStats();
    }

    private bool CanSelectRegion(NativeMethods.PreviewState state) =>
        RegionSelectionAvailability.CanSelectRegion(
            _closing,
            _lifecycle?.IsPreviewing == true,
            state == NativeMethods.PreviewState.Stopped,
            _overlayTransaction);
#endif

    private void UpdateRangeUi()
    {
        SessionGeometry? committed = _lifecycle?.CurrentGeometry;
        if (committed is not null)
        {
            CaptureRegion active = committed.CaptureRegion;
            _rangeLabel.Text =
                _lifecycle?.CurrentRangeMode ==
                    CaptureRangeMode.CustomRegion
                ? $"Current range: custom region; size: {active.Width} x {active.Height} physical pixels; Camera=Wide 1.0; Follow=Off."
                : $"Current range: primary display full screen; size: {active.Width} x {active.Height} physical pixels.";
            return;
        }
        if (_lifecycle?.CurrentRangeMode ==
                CaptureRangeMode.CustomRegion &&
            _confirmedRegion is CaptureRegion region)
        {
            _rangeLabel.Text =
                $"当前范围：自定义区域；尺寸：{region.Width} × " +
                $"{region.Height} 物理像素；比例：" +
                $"{region.Width / (double)region.Height:F4}:1；" +
                "Geometry 将在下一次 Start 前提交；本阶段画面仍保持全屏。";
        }
        else
        {
            _rangeLabel.Text =
                "当前范围：主显示器全屏";
        }
    }

    private void ClearCustomRegion(string message)
    {
        _confirmedDisplay = null;
        _confirmedRegion = null;
        _lastSessionGeometry = null;
        AppendStatus(message);
        UpdateRangeUi();
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        if (IsDisposed || Disposing)
        {
            return;
        }
        if (InvokeRequired)
        {
            BeginInvoke(() => OnDisplaySettingsChanged(sender, e));
            return;
        }
        if (_lifecycle?.IsCustomRegionPreview == true)
        {
            AppendStatus(
                "Display settings changed while a custom region is active. The committed region remains visible in UI; Native source-size validation will reject unsafe sampling, and the user can reselect.");
            return;
        }
        if (_lifecycle?.CurrentRangeMode ==
            CaptureRangeMode.CustomRegion)
        {
            ClearCustomRegion(
                "显示配置已变化：已确认的自定义区域不再可用，未进行自动缩放或位置猜测。");
        }
    }

    private void ExecuteCameraCommand(CameraCommand command)
    {
        if (_closing ||
            _lifecycle?.IsCustomRegionPreview == true)
        {
            return;
        }
        try
        {
            bool accepted = _cameraController.Execute(
                command,
                ReadActiveCaptureCameraTarget,
                Stopwatch.GetTimestamp(),
                out string status);
            AppendStatus(status);
            if (accepted)
            {
                UpdateCameraModeLabel();
            }
        }
        catch (Exception error)
        {
            _cameraController.SetEnabled(false, Stopwatch.GetTimestamp());
            _cameraEnabled.Checked = false;
            AppendStatus(
                $"镜头命令目标读取失败，已隔离并回退全景：{error.Message}");
        }
    }

    internal bool SetDirectorLiteEnabled(bool enabled)
    {
        long nowQpc = Stopwatch.GetTimestamp();
        if (enabled)
        {
            if (_closing ||
                _lifecycle?.IsPreviewing != true ||
                _lifecycle.IsCustomRegionPreview)
            {
                AppendStatus(
                    "Director Lite requires a running full-screen Preview.");
                SynchronizeDirectorToggle(false);
                return false;
            }
            if (!_directorInput.Start(Handle))
            {
                AppendStatus(
                    $"Director Lite Raw Input registration failed: " +
                    $"Win32={_directorInput.LastWindowsError}.");
                SynchronizeDirectorToggle(false);
                return false;
            }
        }

        bool accepted = _cameraController.SetDirectorLiteEnabled(
            enabled,
            nowQpc,
            out string status);
        if (!enabled || !accepted)
        {
            _directorInput.Stop();
        }
        AppendStatus(status);
        SynchronizeDirectorToggle(
            accepted && enabled ||
            !accepted && _cameraController.Owner == CameraOwner.DirectorLite);
        bool directorOwnsCamera =
            _cameraController.Owner == CameraOwner.DirectorLite;
        _hotkeys?.SetDirectorOwnsCamera(directorOwnsCamera);
        UpdateHotkeyUi();
        UpdateCameraModeLabel();
        UpdateProductShellControls(_recordingUiSnapshot);
        return accepted;
    }

    private void OnDirectorPointerActivity(RawPointerActivity activity)
    {
        if (_closing ||
            _cameraController.Owner != CameraOwner.DirectorLite)
        {
            return;
        }

        long nowQpc = Stopwatch.GetTimestamp();
        CameraPoint windowTarget = default;
        if (_selectedCaptureTarget.IsWindow &&
            !WindowCaptureSelector.TryMapCurrentCursor(
                _selectedCaptureTarget.WindowHandle,
                out windowTarget))
        {
            // Raw Input is process-wide. Window mode must not retarget or
            // extend focus because of activity in another app or the desktop.
            return;
        }
        if (activity.IsLeftButtonDown)
        {
            _cameraController.HandleDirectorPointerActivity(nowQpc);
            try
            {
                _cameraController.HandleDirectorLeftClick(
                    _selectedCaptureTarget.IsWindow
                        ? windowTarget
                        : CameraCursorTarget.ReadPrimaryMonitorTarget(),
                    nowQpc,
                    out string status);
                AppendStatus(status);
            }
            catch (Exception error)
            {
                AppendStatus(
                    $"Director Lite click position read failed: {error.Message}");
            }
        }
        else
        {
            _cameraController.HandleDirectorPointerActivity(nowQpc);
        }
    }

    private void OnCameraStatePublished(
        CameraState state,
        NativeMethods.Result result)
    {
        if (IsDisposed || _closing)
        {
            return;
        }
        bool important = state.Event != "tick" ||
            result != NativeMethods.Result.Ok ||
            state.TimestampQpc - _lastCameraUiQpc >= Stopwatch.Frequency / 20;
        if (!important)
        {
            return;
        }
        _lastCameraUiQpc = state.TimestampQpc;
        BeginInvoke(() =>
        {
            _cameraLabel.Text =
                $"Camera: {state.Mode} z={state.Zoom:F4} c=({state.CenterX:F4},{state.CenterY:F4}) " +
                $"target=({state.TargetX:F4},{state.TargetY:F4}) p={state.TransitionProgress:F3} " +
                $"seq={state.Sequence} clamp={state.ClampX}/{state.ClampY}";
            UpdateCameraModeLabel();
            if (result != NativeMethods.Result.Ok &&
                result != NativeMethods.Result.InvalidState)
            {
                _cameraLastError =
                    $"{result}: {_lifecycle?.LastError ?? "native error"}";
                AppendStatus($"Camera native 提交失败：{_cameraLastError}");
            }
            else
            {
                _cameraLastError = "none";
            }
        });
    }

    private void OnFollowStatePublished(ComfortZoneFollowStep follow)
    {
        if (IsDisposed || _closing)
        {
            return;
        }
        long nowQpc = Stopwatch.GetTimestamp();
        bool important =
            follow.Event != "tick" ||
            follow.Error is not null ||
            nowQpc - _lastFollowUiQpc >= Stopwatch.Frequency / 20;
        if (!important)
        {
            return;
        }
        _lastFollowUiQpc = nowQpc;
        BeginInvoke(() =>
        {
            _followLabel.Text =
                $"Follow: {follow.State} enabled={follow.FollowEnabled} " +
                $"desired=({follow.DesiredCenter.X:F4},{follow.DesiredCenter.Y:F4}) " +
                $"v=({follow.VelocityX:F4},{follow.VelocityY:F4})";
            if (follow.Error is not null)
            {
                _followLastError = follow.Error;
                _suppressFollowToggle = true;
                _followEnabled.Checked = false;
                _suppressFollowToggle = false;
                AppendStatus(
                    $"舒适区跟随已隔离并退回固定目标镜头：{follow.Error}");
            }
            else if (follow.State != FollowState.ErrorFallback)
            {
                _followLastError = "none";
            }
        });
    }

    private async void OnPreviewSurfaceSizeChanged(
        object? sender,
        EventArgs e)
    {
        if (_lifecycle is null ||
            _previewSurface.ClientSize.Width <= 0 ||
            _previewSurface.ClientSize.Height <= 0)
        {
            return;
        }
        try
        {
            PreviewLifecycleResult result =
                await _lifecycle.RequestResizeAsync(
                    _previewSurface.ClientSize.Width,
                    _previewSurface.ClientSize.Height);
            if (result.Status == PreviewLifecycleOperationStatus.Failed)
            {
                AppendStatus($"Resize 失败：{result.Error}");
            }
        }
        catch (Exception error)
        {
            AppendStatus($"Resize 生命周期异常：{error.Message}");
        }
    }

    private void RefreshStats()
    {
        if (_lifecycle is null || _closing)
        {
            return;
        }
        try
        {
            RefreshRecordingUi();
            if (!_lifecycle.TryReadStats(
                out NativeMethods.PreviewStats stats,
                out NativeMethods.CursorStats cursor,
                out string? readError))
            {
                UpdateLifecycleControls();
                if (!string.IsNullOrWhiteSpace(readError))
                {
                    AppendStatus($"Stats 读取失败：{readError}");
                }
                return;
            }
            _lastSessionGuid = FormatSessionGuid(
                stats.SessionIdHigh,
                stats.SessionIdLow) ?? _lastSessionGuid;
            _startButton.Enabled =
                (_lifecycle.State is
                    PreviewLifecycleState.Stopped or
                    PreviewLifecycleState.Error) &&
                stats.State == NativeMethods.PreviewState.Stopped &&
                !_overlayTransaction;
            _stopButton.Enabled =
                !_overlayTransaction &&
                !RecordingBlocksPreviewChanges() &&
                (_lifecycle.State is
                    PreviewLifecycleState.Starting or
                    PreviewLifecycleState.Previewing);
            _customCursorEnabled.Enabled =
                _lifecycle.State is
                    PreviewLifecycleState.Stopped or
                    PreviewLifecycleState.Error;
            bool cameraCommandsEnabled =
                _lifecycle.IsPreviewing &&
                _cameraEnabled.Checked &&
                !_lifecycle.IsCustomRegionPreview;
            _standardCameraButton.Enabled = cameraCommandsEnabled;
            _strongCameraButton.Enabled = cameraCommandsEnabled;
            _cameraEnabled.Enabled = !_lifecycle.IsCustomRegionPreview;
            _followEnabled.Enabled = !_lifecycle.IsCustomRegionPreview;
            _customCursorEnabled.Enabled &=
                !_lifecycle.IsCustomRegionPreview;
            bool captureTargetChangeEnabled = !_closing &&
                !RecordingBlocksPreviewChanges() &&
                _lifecycle.State is PreviewLifecycleState.Stopped or
                    PreviewLifecycleState.Previewing or
                    PreviewLifecycleState.Error;
            _captureModeSelector.Enabled = captureTargetChangeEnabled;
            _windowSelector.Enabled = captureTargetChangeEnabled;
            _refreshWindowsButton.Enabled = captureTargetChangeEnabled;
            RegionCaptureUiPolicy regionPolicy =
                ProductFeatures.RegionCaptureUi;
            _selectRegionButton.Enabled =
                regionPolicy.Enabled && CanSelectRegion();
            _fullScreenButton.Enabled =
                regionPolicy.Enabled &&
                CanSelectRegion() &&
                _lifecycle.CurrentRangeMode ==
                    CaptureRangeMode.CustomRegion;
            UpdateRecordingControls();
            UpdateHotkeyUi();
            _cursorLabel.Text =
                $"Cursor: {CursorModeText.Describe(cursor.RequestedMode, cursor.ActualMode, cursor.FallbackReason)}";
            _wdaLabel.Text = stats.Flags.HasFlag(NativeMethods.StatsFlags.WdaApplied)
                ? "WDA_EXCLUDEFROMCAPTURE：成功"
                : $"WDA_EXCLUDEFROMCAPTURE：失败 ({stats.WdaResult}, {stats.WdaLastError})";
            if (stats.Flags.HasFlag(NativeMethods.StatsFlags.WdaApplied))
            {
                _windowExclusionSucceeded = true;
                _captureSafetyLabel.Text = "控制窗口已从录制画面排除";
            }
            else if (stats.Flags.HasFlag(NativeMethods.StatsFlags.WdaFailed))
            {
                _windowExclusionSucceeded = false;
                _captureSafetyLabel.Text =
                    "录制开始后将最小化窗口；可从任务栏返回停止";
            }
            if (stats.LastResult == NativeMethods.Result.WindowTargetClosed &&
                !_windowTargetClosedNotified)
            {
                _windowTargetClosedNotified = true;
                _captureSafetyLabel.Text =
                    "目标窗口已关闭；已安全停止并保留有效录制内容";
                AppendStatus(
                    "WindowTargetClosed：目标窗口已关闭，录制已走安全 Finalize / Publish。可重新选择窗口。");
            }
            else if (stats.Flags.HasFlag(
                NativeMethods.StatsFlags.WindowTargetMinimized))
            {
                _captureSafetyLabel.Text =
                    "目标窗口已最小化；MVP 不保证持续捕获，请恢复目标窗口";
            }
            _statusBox.Lines =
            [
                $"State={stats.State}; Flags={stats.Flags}; Adapter={stats.GetAdapterName()}",
                $"Capture={stats.CaptureFrameCount} @ {stats.CaptureFps:F2} FPS; Present={stats.PresentFrameCount} @ {stats.PresentFps:F2} FPS; Dropped={stats.DroppedFrameCount}",
                $"Latency P50/P95/Max={stats.P50LatencyMilliseconds:F2}/{stats.P95LatencyMilliseconds:F2}/{stats.MaxLatencyMilliseconds:F2} ms",
                $"Capture={stats.CaptureWidth}x{stats.CaptureHeight}; Preview={stats.PreviewWidth}x{stats.PreviewHeight}; PoolRebuild={stats.FramePoolRecreateCount}; Resize={stats.SwapChainResizeCount}",
                $"CameraUpdates={stats.CameraUpdateCount} @ {stats.CameraUpdateRate:F1}/s; InvalidFallbacks={stats.InvalidCameraStateFallbackCount}; AppliedSeq={stats.NativeLastAppliedSequence}",
                $"NativeCamera={stats.NativeAppliedMode}; enabled={stats.NativeCameraEnabled}; zoom={stats.NativeAppliedZoom:F4}; center=({stats.NativeAppliedCenterX:F4},{stats.NativeAppliedCenterY:F4})",
                $"CameraLastError={_cameraLastError}",
                $"CameraOwner={_cameraController.Owner}; DirectorState={_cameraController.DirectorState}; DirectorFocusStrength={_cameraController.DirectorFocusStrength}; DirectorInputActive={_directorInput.IsActive}; DirectorInputLastWin32={_directorInput.LastWindowsError}",
                $"FollowEnabled={_followEnabled.Checked}; FollowLastError={_followLastError}; FollowLogQueueDrops={_followLogger?.QueueDropCount ?? 0}; FollowLogError={_followLogger?.BackgroundError ?? "none"}",
                $"Hotkeys={_hotkeys?.State}; F9Registered={_hotkeys?.IsRegistered(HotkeyBindings.Standard) == true}; F10Registered={_hotkeys?.IsRegistered(HotkeyBindings.Strong) == true}",
                $"CursorMode={cursor.ActualMode}; WgcIncluded={cursor.SystemCursorIncluded}; CustomLayer={cursor.CustomCursorLayerActive}; LastDraw={cursor.LastFrameDrawn}; Shape={cursor.ShapeKind} {cursor.ShapeWidth}x{cursor.ShapeHeight} hot=({cursor.HotspotX},{cursor.HotspotY})",
                $"CursorSamples/Draws={cursor.SampleCount}/{cursor.DrawCount}; CacheHit/Miss={cursor.ShapeCacheHitCount}/{cursor.ShapeCacheMissCount}; Uploads={cursor.TextureUploadCount}; ShapeFallbacks={cursor.BuiltInFallbackCount}; GetCursorFailures={cursor.GetCursorInfoFailureCount}",
                $"WGC log={stats.GetLogFilePath()}",
                $"Camera log={_cameraLogger?.LogFilePath}",
                $"Follow log={_followLogger?.LogFilePath}",
                $"Cursor log={cursor.GetLogFilePath()}",
                $"Range={_lifecycle.CurrentRangeMode}; Geometry={FormatSessionGeometry(_lastSessionGeometry)}; Overlay={_overlayTransaction}",
            ];
        }
        catch (Exception error)
        {
            AppendStatus($"Stats 读取失败：{error.Message}");
        }
    }

    private async void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        WriteManagedStartupDiagnostic(new ManagedStartupDiagnosticEvent
        {
            ManagedStage = "MainForm.FormClosing",
            LifecycleState =
                (_lifecycle?.State ?? PreviewLifecycleState.NotInitialized).
                    ToString(),
            Result = "begin",
        });
        DateTimeOffset closeRequestUtc = DateTimeOffset.UtcNow;
        long closeRequestTimestamp = Stopwatch.GetTimestamp();
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
        _closing = true;
        Interlocked.Increment(ref _startupGeneration);
        _startupInspection?.RequestCancellation();
        _recoveryActions?.RequestCancellation();
        await _managedCloseCoordinator.TryExecuteAsync(
            _lastSessionGuid,
            PrepareForImmediateClose,
            Hide,
            () => Visible,
            () => IsHandleCreated,
            CleanupAsync,
            diagnostics =>
            {
                _closeDiagnostics = diagnostics;
                _closeCleanupComplete = true;
            },
            PostFinalClose,
            closeRequestUtc,
            closeRequestTimestamp);
    }

    private void OnFormClosed(object? sender, FormClosedEventArgs e)
    {
        WriteManagedStartupDiagnostic(new ManagedStartupDiagnosticEvent
        {
            ManagedStage = "MainForm.FormClosed",
            LifecycleState =
                (_lifecycle?.State ?? PreviewLifecycleState.Disposed).
                    ToString(),
            Result = "success",
        });
        ManagedStartupDiagnostics.Close();
    }

    private void WriteManagedStartupDiagnostic(
        ManagedStartupDiagnosticEvent diagnostic)
    {
        if (IsHandleCreated && InvokeRequired)
        {
            BeginInvoke(() => WriteManagedStartupDiagnostic(diagnostic));
            return;
        }

        bool mainHandleCreated = IsHandleCreated;
        bool previewHandleCreated = _previewSurface.IsHandleCreated;
        ManagedStartupDiagnostics.Write(diagnostic with
        {
            MainFormIsHandleCreated = mainHandleCreated,
            MainFormHandle = mainHandleCreated ? Handle.ToInt64() : null,
            PreviewSurfaceIsHandleCreated = previewHandleCreated,
            PreviewSurfaceHandle = previewHandleCreated
                ? _previewSurface.Handle.ToInt64()
                : null,
            Visible = Visible,
            WindowState = WindowState.ToString(),
            IsDisposed = IsDisposed,
            Disposing = Disposing,
            LifecycleState = diagnostic.LifecycleState ??
                (_lifecycle?.State ?? PreviewLifecycleState.NotInitialized).
                    ToString(),
            RetryAvailable = diagnostic.RetryAvailable ??
                _startButton.Enabled,
        });
    }

    private async Task CleanupAsync()
    {
        _statsTimer.Stop();
        _countdownCancellation?.Cancel();
        _countdownCancellation?.Dispose();
        _countdownCancellation = null;
        _shellActions.EndCountdown();
        SetDirectorLiteEnabled(false);
        _directorInput.ActivityObserved -= OnDirectorPointerActivity;
        _directorInput.Dispose();
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        if (_recordingController is not null &&
            !_recordingController.IsDisposed)
        {
            bool wasActive =
                _recordingController.CurrentSnapshot.IsActive;
            ManagedRecordingSnapshot final =
                await _recordingController.StopForCloseAsync();
            if (wasActive && final.State == ManagedRecordingState.Failed)
            {
                MessageBox.Show(
                    DescribeRecordingFailure(final),
                    "录制停止失败",
                    MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            }
        }
        RecoveryActionCoordinator? recoveryActions = _recoveryActions;
        if (recoveryActions is not null)
        {
            recoveryActions.SnapshotChanged -=
                OnRecoveryAttemptSnapshotChanged;
            await recoveryActions.CancelAndWaitAsync();
            await recoveryActions.DisposeAsync();
            if (ReferenceEquals(_recoveryActions, recoveryActions))
            {
                _recoveryActions = null;
            }
        }
        StartupInspectionCoordinator? startupInspection =
            _startupInspection;
        if (startupInspection is not null)
        {
            startupInspection.SnapshotChanged -=
                OnStartupInspectionSnapshotChanged;
            await startupInspection.CancelAndWaitAsync();
            await startupInspection.DisposeAsync();
            if (ReferenceEquals(_startupInspection, startupInspection))
            {
                _startupInspection = null;
            }
        }
        if (_lifecycle is not null)
        {
            _lifecycle.StateChanged -= OnPreviewLifecycleStateChanged;
            _lifecycle.CameraStatePublished -= OnCameraStatePublished;
            _lifecycle.FollowStatePublished -= OnFollowStatePublished;
            await _lifecycle.DisposeAsync();
            _lastEngineStopDurationMs =
                _lifecycle.LastEngineStopDurationMs;
            _lastLifecycleCloseDurationMs =
                _lifecycle.LastLifecycleCloseDurationMs;
            _lifecycle = null;
        }
        _recordingController = null;
        _hotkeys?.Dispose();
        _hotkeys = null;
        _cameraLogger?.Dispose();
        _cameraLogger = null;
        _followLogger?.Dispose();
        _followLogger = null;
    }

    private void PrepareForImmediateClose()
    {
        ControlBox = false;
        UseWaitCursor = true;
        _startButton.Enabled = false;
        _stopButton.Enabled = false;
        _standardCameraButton.Enabled = false;
        _strongCameraButton.Enabled = false;
        _recordingStandardCameraButton.Enabled = false;
        _recordingStrongCameraButton.Enabled = false;
        _hotkeyToggleButton.Enabled = false;
        _selectRegionButton.Enabled = false;
        _fullScreenButton.Enabled = false;
        _startRecordingButton.Enabled = false;
        _stopRecordingButton.Enabled = false;
        _openVideoButton.Enabled = false;
        _openRecordingFolderButton.Enabled = false;
    }

    private void PostFinalClose()
    {
        if (IsDisposed || Disposing)
        {
            return;
        }
        if (IsHandleCreated)
        {
            BeginInvoke(Close);
            return;
        }
        Close();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        if (_closeDiagnostics is not null)
        {
            _closeDiagnostics.MarkFormClosed(DateTimeOffset.UtcNow);
            WriteCloseLifecycleDiagnostic(_closeDiagnostics);
        }
        base.OnFormClosed(e);
    }

    private void WriteCloseLifecycleDiagnostic(
        ManagedCloseDiagnostics diagnostics)
    {
        if (string.IsNullOrWhiteSpace(_diagnosticLogDirectory))
        {
            return;
        }
        try
        {
            string path = Path.Combine(
                _diagnosticLogDirectory,
                $"p2.4-close-{DateTime.UtcNow:yyyyMMdd-HHmmssfff}-{Guid.NewGuid():N}.jsonl");
            string json = JsonSerializer.Serialize(new
            {
                @event = "p2.4-close-summary",
                diagnostics.SessionGuid,
                diagnostics.ManagedCloseRequestUtc,
                diagnostics.ImmediateHideRequestedUtc,
                diagnostics.ImmediateHideAppliedUtc,
                diagnostics.VisibleAfterHide,
                diagnostics.HandleCreatedAfterHide,
                diagnostics.CleanupStartUtc,
                diagnostics.CleanupEndUtc,
                diagnostics.FinalClosePostedUtc,
                diagnostics.FormClosedUtc,
                diagnostics.VisibleCloseLatencyMs,
                diagnostics.CleanupDurationMs,
                diagnostics.ManagedCloseDurationMs,
                diagnostics.CloseRequestToFormClosedMs,
                LifecycleCloseDurationMs = _lastLifecycleCloseDurationMs,
                EngineStopDurationMs = _lastEngineStopDurationMs,
                diagnostics.CleanupInvocationCount,
                diagnostics.HideInvocationCount,
                diagnostics.FinalCloseInvocationCount,
                diagnostics.ClosingFeedbackShown,
                diagnostics.CleanupSucceeded,
                diagnostics.CleanupExceptionType,
                CloseCompletedUtc = DateTime.UtcNow,
            });
            File.WriteAllText(
                path,
                json + Environment.NewLine,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
        catch
        {
            // Closing must remain fail-safe even if diagnostic I/O is unavailable.
        }
    }

    private static string? FormatSessionGuid(ulong high, ulong low)
    {
        if (high == 0 && low == 0)
        {
            return null;
        }
        byte[] bytes = new byte[16];
        BitConverter.TryWriteBytes(bytes.AsSpan(0, 8), high);
        BitConverter.TryWriteBytes(bytes.AsSpan(8, 8), low);
        return new Guid(bytes).ToString("D").ToUpperInvariant();
    }

    private static string FormatSessionGeometry(SessionGeometry? geometry) =>
        geometry is null
            ? "none"
            : $"{geometry.CaptureRegion.Width}x{geometry.CaptureRegion.Height} " +
              $"@({geometry.CaptureRegion.Left},{geometry.CaptureRegion.Top}) " +
              $"canvas={geometry.OutputCanvas.Width}x{geometry.OutputCanvas.Height}/" +
              $"{geometry.OutputCanvas.ScaleMode}";

    private void ShowFatalError(string title, Exception error)
    {
        AppendStatus($"{title}: {error}");
        MessageBox.Show(
            this,
            $"{title}\r\n{error.Message}\r\n\r\n" +
            "请使用“停止预览”按钮，或关闭窗口安全退出。",
            "预览错误",
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);
    }

    private void AppendStatus(string message)
    {
        string line = $"[{DateTime.Now:HH:mm:ss.fff}] {message}";
        _statusBox.AppendText(line + Environment.NewLine);
    }

    private void UpdateHotkeyUi()
    {
        HotkeyActivationState state =
            _hotkeys?.State ?? HotkeyActivationState.NotAvailable;
        _hotkeyToggleButton.Enabled =
            _lifecycle?.IsPreviewing == true &&
            _hotkeys?.CanToggle == true;

        switch (state)
        {
            case HotkeyActivationState.Enabled:
                _hotkeyStatusLabel.Text = "镜头快捷键：开";
                _hotkeyToggleButton.Text = "关闭镜头快捷键";
                _hotkeyHelpLabel.Text =
                    "F9：切换 1.6x / 全景；F10：切换 2.0x / 全景";
                break;
            case HotkeyActivationState.Failed:
                _hotkeyStatusLabel.Text = "镜头快捷键：不可用 / 冲突";
                _hotkeyToggleButton.Text = "关闭镜头快捷键";
                HotkeyRegistrationResult failure = _hotkeys!.LastResult;
                _hotkeyHelpLabel.Text =
                    $"{failure.FailedBinding?.DisplayName ?? "快捷键"}注册失败，" +
                    $"Windows错误码={failure.WindowsErrorCode}；" +
                    "仍可使用1.6x和2.0x界面按钮";
                break;
            case HotkeyActivationState.SuspendedByDirector:
                _hotkeyStatusLabel.Text = _hotkeys?.UserEnabled == true
                    ? "镜头快捷键：导演模式暂时停用"
                    : "镜头快捷键：关";
                _hotkeyToggleButton.Text = _hotkeys?.UserEnabled == true
                    ? "关闭镜头快捷键"
                    : "启用镜头快捷键";
                _hotkeyHelpLabel.Text = _hotkeys?.UserEnabled == true
                    ? "F9/F10 已暂停；关闭导演模式后自动恢复"
                    : "F9/F10 未占用；导演模式独占镜头";
                break;
            case HotkeyActivationState.Disabled:
                _hotkeyStatusLabel.Text = "镜头快捷键：关";
                _hotkeyToggleButton.Text = "启用镜头快捷键";
                _hotkeyHelpLabel.Text =
                    "F9/F10 未占用；1.6x / 2.0x 按钮仍可使用";
                break;
            default:
                _hotkeyStatusLabel.Text = "镜头快捷键：未启用";
                _hotkeyToggleButton.Text = "启用镜头快捷键";
                _hotkeyHelpLabel.Text =
                    "启动预览后可手动开启，当前未占用F9和F10";
                break;
        }
    }

    private CameraPoint ReadActiveCaptureCameraTarget()
    {
        if (!_selectedCaptureTarget.IsWindow)
        {
            return CameraCursorTarget.ReadPrimaryMonitorTarget();
        }
        return WindowCaptureSelector.TryMapCurrentCursor(
            _selectedCaptureTarget.WindowHandle,
            out CameraPoint target)
            ? target
            : new CameraPoint(0.5, 0.5);
    }

    private CameraCursorObservation ReadActiveCaptureCursorObservation()
    {
        CameraCursorObservation desktop =
            CameraCursorTarget.ReadPrimaryMonitorObservation();
        if (!_selectedCaptureTarget.IsWindow ||
            !desktop.GetCursorPosResult)
        {
            return desktop;
        }
        bool inside = WindowCaptureSelector.TryMapDesktopPoint(
            _selectedCaptureTarget.WindowHandle,
            desktop.ScreenX,
            desktop.ScreenY,
            out CameraPoint target);
        return desktop with
        {
            NormalizedX = inside ? target.X : 0.5,
            NormalizedY = inside ? target.Y : 0.5,
            InsidePrimaryMonitor = inside,
        };
    }

    private void UpdateCameraModeLabel()
    {
        if (_cameraController.Owner == CameraOwner.DirectorLite)
        {
            bool strong = _cameraController.DirectorFocusStrength ==
                DirectorFocusStrength.Strong;
            _recordingModeLabel.Text = strong
                ? "自动跟随重点 · 强调 2.0x"
                : "自动跟随重点 · 柔和 1.6x";
            return;
        }

        double targetZoom = _cameraController.TargetZoom;
        _recordingModeLabel.Text = targetZoom >=
            (CameraSettings.StrongZoom + CameraSettings.StandardZoom) / 2.0
                ? "当前镜头：2.0x"
                : targetZoom >=
                    (CameraSettings.StandardZoom + CameraSettings.WideZoom) / 2.0
                    ? "当前镜头：1.6x"
                    : "当前镜头：Wide 1.0x";
    }

    private NativeMethods.CursorMode SelectedCursorMode() =>
        _customCursorEnabled.Checked
            ? NativeMethods.CursorMode.CustomCursor
            : NativeMethods.CursorMode.SystemCursor;

    private async Task ConfigureSelectedCursorModeAsync()
    {
        if (_lifecycle is null)
        {
            return;
        }

        try
        {
            NativeMethods.CursorMode selected = SelectedCursorMode();
            PreviewLifecycleResult result =
                await _lifecycle.SetCursorModeAsync(selected);
            if (!result.Succeeded)
            {
                AppendStatus($"Cursor 模式设置失败：{result.Error}");
                return;
            }
            AppendStatus(
                selected == NativeMethods.CursorMode.CustomCursor
                    ? "已请求 CustomCursor；Start 前 native 将验证 WGC cursor 排除能力，失败则回退 SystemCursor。"
                    : "已选择 SystemCursor；WGC 保留系统鼠标，自绘层关闭。");
        }
        catch (Exception error)
        {
            AppendStatus($"Cursor 模式生命周期异常：{error.Message}");
        }
    }

    private static Button NewButton(string text) =>
        new()
        {
            Text = text,
            AutoSize = true,
            Padding = new Padding(12, 4, 12, 4),
        };

    private static CheckBox NewProductToggle(
        string text,
        bool isChecked) => new()
    {
        Text = text,
        Checked = isChecked,
        Appearance = Appearance.Button,
        AutoSize = false,
        Size = new Size(128, 38),
        TextAlign = ContentAlignment.MiddleCenter,
        FlatStyle = FlatStyle.Flat,
        Margin = new Padding(0, 3, 7, 3),
        Cursor = Cursors.Hand,
    };

    private static RadioButton NewStrengthRadio(
        string text,
        bool isChecked) => new()
    {
        Text = text,
        Checked = isChecked,
        Appearance = Appearance.Button,
        AutoSize = false,
        Size = new Size(106, 36),
        TextAlign = ContentAlignment.MiddleCenter,
        FlatStyle = FlatStyle.Flat,
        FlatAppearance =
        {
            BorderSize = 1,
            BorderColor = Color.FromArgb(146, 142, 133),
            CheckedBackColor = Color.FromArgb(218, 214, 203),
        },
        BackColor = Color.FromArgb(246, 244, 238),
        ForeColor = Color.FromArgb(42, 41, 38),
        Margin = new Padding(0, 3, 7, 3),
        Cursor = Cursors.Hand,
    };

    private static void UpdateProductToggleAppearance(
        CheckBox toggle,
        string label)
    {
        toggle.Text = $"{label}  {(toggle.Checked ? "开" : "关")}";
        toggle.FlatAppearance.BorderSize = 1;
        toggle.FlatAppearance.BorderColor = toggle.Checked
            ? Color.FromArgb(73, 83, 68)
            : Color.FromArgb(146, 142, 133);
        toggle.BackColor = toggle.Checked
            ? Color.FromArgb(218, 222, 209)
            : Color.FromArgb(246, 244, 238);
        toggle.ForeColor = Color.FromArgb(42, 41, 38);
    }

    private static Label NewDiagnosticLabel(string text) =>
        new()
        {
            Text = text,
            AutoSize = false,
            AutoEllipsis = true,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(8, 2, 8, 2),
            Margin = Padding.Empty,
        };
}
