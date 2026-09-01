using System.Runtime.InteropServices;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace XbPreview.Host;

internal sealed class FormalUiV4Form : Form, IMessageFilter
{
    private const int WmNcHitTest = 0x0084;
    private const int WmNcLButtonDown = 0x00A1;
    private const int WmSize = 0x0005;
    private const int WmSetRedraw = 0x000B;
    private const int WmSysCommand = 0x0112;
    private const int WmCancelMode = 0x001F;
    private const int WmEnterSizeMove = 0x0231;
    private const int WmExitSizeMove = 0x0232;
    private const int ScCommandMask = 0xFFF0;
    private const int ScSize = 0xF000;
    private const int ScMove = 0xF010;
    private const int ScMinimize = 0xF020;
    private const int ScRestore = 0xF120;
    private const int SizeMinimized = 1;
    private const uint RedrawInvalidate = 0x0001;
    private const uint RedrawErase = 0x0004;
    private const uint RedrawAllChildren = 0x0080;
    private const uint RedrawUpdateNow = 0x0100;
    private const uint RedrawFrame = 0x0400;
    private const int HtCaption = 0x0002;
    private const int HtLeft = 10;
    private const int HtRight = 11;
    private const int HtTop = 12;
    private const int HtTopLeft = 13;
    private const int HtTopRight = 14;
    private const int HtBottom = 15;
    private const int HtBottomLeft = 16;
    private const int HtBottomRight = 17;
    private const int ResizeSideZoneLogicalPixels = 13;
    private const int ResizeCornerZoneLogicalPixels = 20;
    private const int DwmWindowCornerPreference = 33;
    private const int DwmWindowBorderColor = 34;
    private const int DwmWindowCornerRound = 2;
    private const int DwmColorDefault = unchecked((int)0xFFFFFFFF);
    private const int SettingsWindowBorderColor = 0x00DDE6ED;
    private static readonly Color SettingsTitleBandColor = Color.FromArgb(0xED, 0xE6, 0xDD);

    private readonly V4ChromeButton _maximizeButton;
    private readonly V4ShellSurface _resizeHost;
    private readonly TableLayoutPanel _rootLayout;
    private readonly TableLayoutPanel _workspace;
    private readonly FormalUiResizeProxySurface _resizeProxy;
    private readonly FormalUiHomeRevealSurface _homeRevealSurface;
    private readonly Control _titleBar;
    private readonly Control _titleChromeHost;
    private readonly V4PreviewPanel _preview;
    private readonly RecordingDeckView _recordingDeck;
    private readonly System.Windows.Forms.Timer _recordingPresentationTimer;
    private readonly System.Diagnostics.Stopwatch _recordingPresentationClock = new();
    private readonly Dictionary<int, V4ResizeGrip> _resizeGrips = new();
    private readonly FormalUiWindowTargetPresentationState _windowTargetState = new();
    private readonly WindowSelectorDeckView _windowSelectorDeck;
    private readonly FormalUiWindowSelectorPopover _windowSelectorPopover;
    private readonly FormalUiMicDevicePresentationState _micDeviceState = new();
    private readonly MicSelectorDeckView _micSelectorDeck;
    private readonly FormalUiMicSelectorPopover _micSelectorPopover;
    private readonly FormalUiBackgroundPresentationState _backgroundState = new();
    private readonly BackgroundSelectorDeckView _backgroundSelectorDeck;
    private readonly FormalUiBackgroundSelectorPopover _backgroundSelectorPopover;
    private readonly FormalUiSettingsView _settingsView;
    private readonly bool _openWindowSelectorForHumanReview;
    private readonly bool _openMicSelectorForHumanReview;
    private readonly bool _micNoDeviceForHumanReview;
    private readonly bool _micDeviceReturnForHumanReview;
    private readonly bool _openBackgroundSelectorForHumanReview;
    private readonly bool _openSettingsForHumanReview;
    private FormalUiRecordingPresentationState _recordingPresentationState;
    private FormalUiRecordingPresentationState? _lastRenderedRecordingPresentationState;
    private string? _lastRenderedRecordingTimeText;
    private TimeSpan _completedDuration;
    private bool _completedOpenFolderClicked;
    private bool _completedOpenVideoClicked;
    private bool _insideRecordingPresentationTimerTick;
    private int _recordingPresentationTimerTickCount;
    private int _recordingPresentationTimerRenderCount;
    private int _recordingPresentationSameSecondSkipCount;
    private int _recordingPresentationPreviewTextAssignmentCount;
    private int _recordingPresentationDeckTextAssignmentCount;
    private int _recordingPresentationTimerRelatedLayoutCount;
    private bool _messageFilterInstalled;
    private bool _isInteractiveResize;
    private bool _isExitingInteractiveResize;
    private bool _rootLayoutSuspended;
    private bool _bodyLayoutSuspended;
    private bool _bodyPaintFrozen;
    private bool _resizeProxyActive;
    private bool _wasMinimized;
    private bool _atomicHomeRevealActive;
    private int _pendingResizeHitTest;
    private int _interactiveResizeHitTest;
    private int _bodyLeftInset;
    private int _bodyTopInset;
    private int _bodyRightInset;
    private int _bodyBottomInset;
    private double _snapshotMilliseconds;
    private string? _snapshotError;

    internal FormalUiV4Form()
        : this(
            openWindowSelectorForHumanReview: false,
            openMicSelectorForHumanReview: false,
            micNoDeviceForHumanReview: false,
            micDeviceReturnForHumanReview: false,
            openBackgroundSelectorForHumanReview: false,
            openSettingsForHumanReview: false)
    {
    }

    internal FormalUiV4Form(
        bool openWindowSelectorForHumanReview,
        bool openMicSelectorForHumanReview,
        bool micNoDeviceForHumanReview,
        bool micDeviceReturnForHumanReview,
        bool openBackgroundSelectorForHumanReview,
        bool openSettingsForHumanReview)
    {
        _openWindowSelectorForHumanReview = openWindowSelectorForHumanReview;
        _openMicSelectorForHumanReview = openMicSelectorForHumanReview;
        _micNoDeviceForHumanReview = micNoDeviceForHumanReview;
        _micDeviceReturnForHumanReview = micDeviceReturnForHumanReview;
        _openBackgroundSelectorForHumanReview = openBackgroundSelectorForHumanReview;
        _openSettingsForHumanReview = openSettingsForHumanReview;
        Text = "Legacy UI";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(910, 635);
        MinimumSize = new Size(840, 600);
        BackColor = FormalUiV4Tokens.Border;
        ForeColor = FormalUiV4Tokens.Ink;
        Font = FormalUiV4Tokens.Ui(9f);
        FormBorderStyle = FormBorderStyle.None;
        Padding = new Padding(1);
        AutoScaleMode = AutoScaleMode.Dpi;
        DoubleBuffered = true;
        KeyPreview = true;

        TableLayoutPanel root = new()
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            ColumnCount = 1,
            RowCount = 2,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, FormalUiV4Tokens.TitleBarHeight));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        _rootLayout = root;

        Control titleBar = BuildTitleBar(
            out V4ChromeButton maximizeButton,
            out Control titleChromeHost);
        _maximizeButton = maximizeButton;
        _titleBar = titleBar;
        _titleChromeHost = titleChromeHost;

        TableLayoutPanel workspace = new()
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            ColumnCount = 1,
            RowCount = 2,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
        };
        workspace.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        workspace.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        workspace.RowStyles.Add(new RowStyle(
            SizeType.Absolute,
            FormalUiV4Tokens.ConsoleHeight));
        V4PreviewPanel preview = BuildPreview();
        Control console = BuildConsole(
            out RecordingDeckView recordingDeck,
            out WindowSelectorDeckView windowSelectorDeck,
            out MicSelectorDeckView micSelectorDeck,
            out BackgroundSelectorDeckView backgroundSelectorDeck);
        _preview = preview;
        _recordingDeck = recordingDeck;
        _windowSelectorDeck = windowSelectorDeck;
        _micSelectorDeck = micSelectorDeck;
        _backgroundSelectorDeck = backgroundSelectorDeck;
        workspace.Controls.Add(preview, 0, 0);
        workspace.Controls.Add(console, 0, 1);
        _workspace = workspace;
        if (FormalUiV4ResizeProbe.Enabled)
        {
            AttachLayoutProbe(workspace);
            root.Paint += (_, _) => FormalUiV4ResizeProbe.RecordRootPaint();
            workspace.Paint += (_, _) => FormalUiV4ResizeProbe.RecordWorkspacePaint();
            workspace.VisibleChanged += (_, _) =>
                FormalUiV4ResizeProbe.RecordWorkspaceVisibleChanged(workspace.Visible);
        }

        root.Controls.Add(titleBar, 0, 0);
        root.Controls.Add(workspace, 0, 1);
        _settingsView = new FormalUiSettingsView
        {
            Dock = DockStyle.Fill,
            Visible = false,
        };
        _settingsView.BackRequested += (_, _) => CloseSettingsView();
        _settingsView.ResetRequested += (_, _) => ApplyPresentationReset();
        root.Controls.Add(_settingsView, 0, 1);
        V4ShellSurface shell = new()
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
        };
        _resizeHost = shell;
        _resizeProxy = new FormalUiResizeProxySurface();
        _homeRevealSurface = new FormalUiHomeRevealSurface();
        shell.Controls.Add(root);
        shell.Controls.Add(_homeRevealSurface);
        shell.Controls.Add(_resizeProxy);
        _resizeProxy.BringToFront();
        InstallResizeGrips(shell);
        shell.Controls.Add(titleChromeHost);
        LayoutTitleChrome();
        titleChromeHost.BringToFront();

        _windowSelectorPopover = new FormalUiWindowSelectorPopover(
            _windowTargetState.Items,
            _windowTargetState.SelectedWindow.Id);
        _windowSelectorPopover.ItemSelected += (_, item) => SelectPresentationWindow(item);
        shell.Controls.Add(_windowSelectorPopover);
        WireWindowSelectorPresentation();

        _micSelectorPopover = new FormalUiMicSelectorPopover(
            _micDeviceState.Items,
            _micDeviceState.SelectedDevice.Id);
        _micSelectorPopover.ItemSelected += (_, item) => SelectPresentationMicDevice(item);
        shell.Controls.Add(_micSelectorPopover);
        WireMicSelectorPresentation();

        _backgroundSelectorPopover = new FormalUiBackgroundSelectorPopover(
            _backgroundState.Items,
            _backgroundState.ActiveItemId);
        _backgroundSelectorPopover.ItemInvoked += (_, item) => InvokeBackgroundItem(item);
        shell.Controls.Add(_backgroundSelectorPopover);
        WireBackgroundSelectorPresentation();

        Controls.Add(shell);

        // HUMAN REVIEW / PRESENTATION STATE ONLY. This clock deliberately does
        // not connect to Production RecordingController or its timeline.
        _recordingPresentationTimer = new System.Windows.Forms.Timer
        {
            Interval = 250,
        };
        _recordingPresentationTimer.Tick += (_, _) => OnRecordingPresentationTimerTick();
        root.Layout += RecordRecordingPresentationTimerLayout;
        workspace.Layout += RecordRecordingPresentationTimerLayout;
        preview.Layout += RecordRecordingPresentationTimerLayout;
        recordingDeck.Root.Layout += RecordRecordingPresentationTimerLayout;
        recordingDeck.StartButton.Click += (_, _) => StartPresentationRecording();
        recordingDeck.PauseResumeButton.Click += (_, _) => TogglePresentationPause();
        recordingDeck.StopButton.Click += (_, _) => StopPresentationRecording();
        recordingDeck.OpenFolderButton.Click += (_, _) =>
            RecordCompletedPresentationInteraction(openVideo: false);
        recordingDeck.OpenVideoButton.Click += (_, _) =>
            RecordCompletedPresentationInteraction(openVideo: true);
        UpdateRecordingPresentationUi();
        UpdateMicPresentationUi();
        UpdateBackgroundSelectorUi();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        if (!_messageFilterInstalled)
        {
            Application.AddMessageFilter(this);
            _messageFilterInstalled = true;
        }
        int preference = DwmWindowCornerRound;
        _ = DwmSetWindowAttribute(
            Handle,
            DwmWindowCornerPreference,
            ref preference,
            sizeof(int));
        ApplyWindowBorderColor(settingsVisible: false);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        MaximizedBounds = Screen.FromHandle(Handle).WorkingArea;
        LayoutResizeGrips();
        if (_micNoDeviceForHumanReview || _micDeviceReturnForHumanReview)
        {
            SetMicDeviceAvailable(false);
        }
        if (_micDeviceReturnForHumanReview)
        {
            SetMicDeviceAvailable(true);
        }
        if (_openWindowSelectorForHumanReview)
        {
            _windowTargetState.SelectWindowMode();
            _windowSelectorDeck.FullScreenButton.Selected = false;
            _windowSelectorDeck.WindowButton.Selected = true;
            OpenWindowSelectorPopover();
        }
        else if (_openMicSelectorForHumanReview)
        {
            OpenMicSelectorPopover();
        }
        else if (_openBackgroundSelectorForHumanReview)
        {
            OpenBackgroundSelectorPopover();
        }
        else if (_openSettingsForHumanReview)
        {
            OpenSettingsView();
        }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        if (_messageFilterInstalled)
        {
            Application.RemoveMessageFilter(this);
            _messageFilterInstalled = false;
        }
        _recordingPresentationTimer.Stop();
        _recordingPresentationTimer.Dispose();
        _recordingPresentationClock.Stop();
        base.OnFormClosed(e);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        FormalUiV4ResizeProbe.RecordFormBackgroundPaint();
        base.OnPaintBackground(e);
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        if (_maximizeButton is not null)
        {
            _maximizeButton.Text = WindowState == FormWindowState.Maximized ? "\uE923" : "\uE922";
        }
        if (_isInteractiveResize && WindowState != FormWindowState.Normal)
        {
            ExitInteractiveResizeProxy("WindowStateChanged", renderFinal: true);
        }

        if (_windowSelectorPopover is not null && _windowSelectorPopover.Visible)
        {
            LayoutWindowSelectorPopover();
        }
        if (_micSelectorPopover is not null && _micSelectorPopover.Visible)
        {
            LayoutMicSelectorPopover();
        }
        if (_backgroundSelectorPopover is not null && _backgroundSelectorPopover.Visible)
        {
            LayoutBackgroundSelectorPopover();
        }
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape &&
            (_windowSelectorPopover.Visible ||
             _micSelectorPopover.Visible ||
             _backgroundSelectorPopover.Visible))
        {
            CloseWindowSelectorPopover();
            CloseMicSelectorPopover();
            CloseBackgroundSelectorPopover();
            return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    public bool PreFilterMessage(ref Message message)
    {
        const int WmLButtonDown = 0x0201;
        const int WmRButtonDown = 0x0204;
        const int WmMButtonDown = 0x0207;
        if ((!_windowSelectorPopover.Visible &&
             !_micSelectorPopover.Visible &&
             !_backgroundSelectorPopover.Visible) ||
            (message.Msg != WmLButtonDown &&
             message.Msg != WmRButtonDown &&
             message.Msg != WmMButtonDown &&
             message.Msg != WmNcLButtonDown))
        {
            return false;
        }

        Control? target = Control.FromHandle(message.HWnd);
        if ((_windowSelectorPopover.Visible &&
             (ReferenceEquals(target, _windowSelectorPopover) ||
              ReferenceEquals(target, _windowSelectorDeck.WindowButton))) ||
            (_micSelectorPopover.Visible &&
             (ReferenceEquals(target, _micSelectorPopover) ||
              ReferenceEquals(target, _micSelectorDeck.Selector))) ||
            (_backgroundSelectorPopover.Visible &&
             (ReferenceEquals(target, _backgroundSelectorPopover) ||
              ReferenceEquals(target, _backgroundSelectorDeck.Selector))))
        {
            return false;
        }

        CloseWindowSelectorPopover();
        CloseMicSelectorPopover();
        CloseBackgroundSelectorPopover();
        return false;
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        ExitInteractiveResizeProxy("DpiChanged", renderFinal: true);
        base.OnDpiChanged(e);
        LayoutResizeGrips();
    }

    protected override void OnDeactivate(EventArgs e)
    {
        CloseWindowSelectorPopover();
        CloseMicSelectorPopover();
        CloseBackgroundSelectorPopover();
        ExitInteractiveResizeProxy("Deactivate", renderFinal: true);
        base.OnDeactivate(e);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        ExitInteractiveResizeProxy("FormClosing", renderFinal: false);
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            ExitInteractiveResizeProxy("Dispose", renderFinal: false);
        }
        base.Dispose(disposing);
    }

    protected override void WndProc(ref Message message)
    {
        int messageId = message.Msg;
        if (messageId == WmSize)
        {
            int sizeType = unchecked((int)(long)message.WParam);
            if (sizeType == SizeMinimized)
            {
                _wasMinimized = true;
                base.WndProc(ref message);
                return;
            }

            bool restoringHome = _wasMinimized &&
                _workspace is not null &&
                _workspace.Visible;
            _wasMinimized = false;
            if (restoringHome)
            {
                bool redrawHeld = BeginAtomicHomeReveal();
                try
                {
                    base.WndProc(ref message);
                    PrepareHomeForAtomicReveal();
                }
                finally
                {
                    CompleteAtomicHomeReveal(redrawHeld);
                }
                return;
            }
        }

        if (messageId == WmSysCommand)
        {
            int command = unchecked((int)(long)message.WParam);
            int commandType = command & ScCommandMask;
            if (commandType == ScRestore &&
                _wasMinimized &&
                _workspace is not null &&
                _workspace.Visible)
            {
                _wasMinimized = false;
                bool redrawHeld = BeginAtomicHomeReveal();
                try
                {
                    base.WndProc(ref message);
                    PrepareHomeForAtomicReveal();
                }
                finally
                {
                    CompleteAtomicHomeReveal(redrawHeld);
                }
                _pendingResizeHitTest = 0;
                return;
            }

            if (commandType == ScMinimize &&
                _workspace is not null &&
                _workspace.Visible)
            {
                PrepareHomeRevealSnapshot();
                ShowHomeRevealSurface();
            }
            else if (commandType == ScSize)
            {
                _pendingResizeHitTest = GetSystemCommandResizeHitTest(command & 0xF);
            }
            else if (commandType == ScMove)
            {
                _pendingResizeHitTest = 0;
            }
        }
        if (messageId == WmNcLButtonDown)
        {
            int hitTest = unchecked((int)(long)message.WParam);
            _pendingResizeHitTest = IsResizeHitTest(hitTest) ? hitTest : 0;
        }

        if (messageId == WmEnterSizeMove)
        {
            if (_pendingResizeHitTest != 0 && WindowState == FormWindowState.Normal)
            {
                EnterInteractiveResizeProxy(_pendingResizeHitTest);
            }
            base.WndProc(ref message);
            return;
        }

        if (messageId == WmExitSizeMove)
        {
            ExitInteractiveResizeProxy("WM_EXITSIZEMOVE", renderFinal: true);
            _pendingResizeHitTest = 0;
            base.WndProc(ref message);
            return;
        }

        if (messageId == WmCancelMode && _isInteractiveResize)
        {
            ExitInteractiveResizeProxy("WM_CANCELMODE", renderFinal: true);
        }

        base.WndProc(ref message);
        if (messageId == WmNcLButtonDown || messageId == WmSysCommand)
        {
            _pendingResizeHitTest = 0;
        }
        if (messageId != WmNcHitTest ||
            WindowState == FormWindowState.Maximized ||
            (int)message.Result != 1)
        {
            return;
        }

        message.Result = (IntPtr)GetResizeHitTest(PointToClient(Cursor.Position));
    }

    private static bool IsResizeHitTest(int hitTest) =>
        hitTest is >= HtLeft and <= HtBottomRight;

    private static int GetSystemCommandResizeHitTest(int sizingEdge) =>
        sizingEdge switch
        {
            1 => HtLeft,
            2 => HtRight,
            3 => HtTop,
            4 => HtTopLeft,
            5 => HtTopRight,
            6 => HtBottom,
            7 => HtBottomLeft,
            8 => HtBottomRight,
            _ => HtBottomRight,
        };

    private int GetResizeHitTest(Point cursor)
    {
        int side = ScaleLogicalPixels(ResizeSideZoneLogicalPixels);
        int corner = ScaleLogicalPixels(ResizeCornerZoneLogicalPixels);
        bool cornerLeft = cursor.X < corner;
        bool cornerRight = cursor.X >= ClientSize.Width - corner;
        bool cornerTop = cursor.Y < corner;
        bool cornerBottom = cursor.Y >= ClientSize.Height - corner;
        bool left = cursor.X < side;
        bool right = cursor.X >= ClientSize.Width - side;
        bool top = cursor.Y < side;
        bool bottom = cursor.Y >= ClientSize.Height - side;
        return cornerTop && cornerLeft ? HtTopLeft :
            cornerTop && cornerRight ? HtTopRight :
            cornerBottom && cornerLeft ? HtBottomLeft :
            cornerBottom && cornerRight ? HtBottomRight :
            left ? HtLeft :
            right ? HtRight :
            top ? HtTop :
            bottom ? HtBottom : 1;
    }

    private void InstallResizeGrips(Control host)
    {
        AddResizeGrip(host, HtLeft, Cursors.SizeWE);
        AddResizeGrip(host, HtRight, Cursors.SizeWE);
        AddResizeGrip(host, HtTop, Cursors.SizeNS);
        AddResizeGrip(host, HtBottom, Cursors.SizeNS);
        AddResizeGrip(host, HtTopLeft, Cursors.SizeNWSE);
        AddResizeGrip(host, HtTopRight, Cursors.SizeNESW);
        AddResizeGrip(host, HtBottomLeft, Cursors.SizeNESW);
        AddResizeGrip(host, HtBottomRight, Cursors.SizeNWSE);
        host.SizeChanged += (_, _) =>
        {
            LayoutResizeGrips();
            if (_isInteractiveResize)
            {
                UpdateInteractiveResizeProxyBounds();
            }
        };
    }

    private void EnterInteractiveResizeProxy(int hitTest)
    {
        if (_isInteractiveResize || _isExitingInteractiveResize ||
            WindowState != FormWindowState.Normal ||
            _workspace.IsDisposed || !_workspace.Visible)
        {
            return;
        }

        _isInteractiveResize = true;
        _interactiveResizeHitTest = hitTest;
        _snapshotMilliseconds = 0d;
        _snapshotError = null;
        FormalUiV4ResizeProbe.BeginSession(hitTest, Size);

        try
        {
            if (FormalUiV4ResizeProbe.ProxyDisabledForTest)
            {
                FormalUiV4ResizeProbe.BeginActiveResize();
                FormalUiV4ResizeProbe.RecordSize(Size, Bounds, Cursor.Position);
                return;
            }

            Rectangle bodyBounds = GetWorkspaceBoundsInResizeHost();
            _bodyLeftInset = bodyBounds.Left;
            _bodyTopInset = bodyBounds.Top;
            _bodyRightInset = Math.Max(0, _resizeHost.ClientSize.Width - bodyBounds.Right);
            _bodyBottomInset = Math.Max(0, _resizeHost.ClientSize.Height - bodyBounds.Bottom);

            if (!_resizeProxy.TryCapture(
                    _workspace,
                    out _snapshotMilliseconds,
                    out _snapshotError))
            {
                ExitInteractiveResizeProxy("SnapshotFailed", renderFinal: false);
                return;
            }

            FormalUiV4ResizeProbe.RecordEvent("SnapshotReady");
            _resizeProxyActive = true;
            UpdateInteractiveResizeProxyBounds();
            _resizeProxy.Visible = true;
            _resizeProxy.Update();
            FormalUiV4ResizeProbe.RecordEvent("ProxyFirstFrameUpdateComplete");

            FormalUiV4ResizeBodyPaintGate.Freeze();
            _bodyPaintFrozen = true;
            FormalUiV4ResizeProbe.RecordEvent("BodyPaintFrozen");
            _rootLayout.SuspendLayout();
            _rootLayoutSuspended = true;
            FormalUiV4ResizeProbe.RecordEvent("RootLayoutSuspended");
            _workspace.SuspendLayout();
            _bodyLayoutSuspended = true;
            FormalUiV4ResizeProbe.RecordEvent("WorkspaceLayoutSuspended");
            FormalUiV4ResizeProbe.BeginActiveResize();
            FormalUiV4ResizeProbe.RecordSize(Size, Bounds, Cursor.Position);
        }
        catch (Exception exception) when (
            exception is ArgumentException or ExternalException or InvalidOperationException or OutOfMemoryException)
        {
            _snapshotError = $"{exception.GetType().Name}: {exception.Message}";
            ExitInteractiveResizeProxy("EnterFailed", renderFinal: true);
        }
    }

    private void UpdateInteractiveResizeProxyBounds()
    {
        if (!_isInteractiveResize || _resizeHost.IsDisposed || _resizeProxy.IsDisposed)
        {
            return;
        }

        if (_resizeProxyActive)
        {
            int width = Math.Max(
                1,
                _resizeHost.ClientSize.Width - _bodyLeftInset - _bodyRightInset);
            int height = Math.Max(
                1,
                _resizeHost.ClientSize.Height - _bodyTopInset - _bodyBottomInset);
            Rectangle bounds = new(_bodyLeftInset, _bodyTopInset, width, height);
            if (_resizeProxy.Bounds != bounds)
            {
                _resizeProxy.Bounds = bounds;
            }
        }

        FormalUiV4ResizeProbe.RecordSize(Size, Bounds, Cursor.Position);
    }

    private void ExitInteractiveResizeProxy(string reason, bool renderFinal)
    {
        if ((!_isInteractiveResize && !_resizeProxyActive &&
                !_rootLayoutSuspended && !_bodyLayoutSuspended && !_bodyPaintFrozen) ||
            _isExitingInteractiveResize)
        {
            return;
        }

        _isExitingInteractiveResize = true;
        bool proxyWasActive = _resizeProxyActive;
        FormalUiV4ResizeProbe.BeginRestore();
        try
        {
            if (_bodyPaintFrozen)
            {
                FormalUiV4ResizeBodyPaintGate.Thaw();
                _bodyPaintFrozen = false;
                FormalUiV4ResizeProbe.RecordEvent("BodyPaintThawed");
            }
            if (_rootLayoutSuspended && !_rootLayout.IsDisposed)
            {
                _rootLayout.ResumeLayout(performLayout: renderFinal);
                _rootLayoutSuspended = false;
                FormalUiV4ResizeProbe.RecordEvent("RootLayoutResumed");
            }
            if (_bodyLayoutSuspended && !_workspace.IsDisposed)
            {
                _workspace.ResumeLayout(performLayout: renderFinal);
                _bodyLayoutSuspended = false;
                FormalUiV4ResizeProbe.RecordEvent("WorkspaceLayoutResumed");
            }

            if (renderFinal && !_workspace.IsDisposed && _workspace.IsHandleCreated)
            {
                _workspace.Invalidate(invalidateChildren: true);
                _workspace.Update();
                FormalUiV4ResizeProbe.RecordEvent("RealBodyUpdateComplete");
            }

            if (!_resizeProxy.IsDisposed)
            {
                _resizeProxy.Visible = false;
            }
            _resizeProxyActive = false;
        }
        finally
        {
            if (_bodyPaintFrozen)
            {
                FormalUiV4ResizeBodyPaintGate.Thaw();
                _bodyPaintFrozen = false;
            }
            if (_rootLayoutSuspended && !_rootLayout.IsDisposed)
            {
                try
                {
                    _rootLayout.ResumeLayout(performLayout: false);
                }
                catch (InvalidOperationException)
                {
                    // Cleanup remains fail-open: the real workspace stays visible.
                }
                _rootLayoutSuspended = false;
            }
            if (_bodyLayoutSuspended && !_workspace.IsDisposed)
            {
                try
                {
                    _workspace.ResumeLayout(performLayout: false);
                }
                catch (InvalidOperationException)
                {
                    // Cleanup remains fail-open: the real workspace stays visible.
                }
                _bodyLayoutSuspended = false;
            }

            if (!_resizeProxy.IsDisposed)
            {
                _resizeProxy.Visible = false;
            }
            _resizeProxyActive = false;

            if (!_resizeProxy.IsDisposed)
            {
                _resizeProxy.ClearSnapshot();
                FormalUiV4ResizeProbe.RecordEvent("SnapshotCleared");
            }

            _isInteractiveResize = false;
            _interactiveResizeHitTest = 0;
            _pendingResizeHitTest = 0;
            FormalUiV4ResizeProbe.EndSession(
                reason,
                proxyWasActive,
                _snapshotMilliseconds,
                _snapshotError,
                !_resizeProxy.IsDisposed && _resizeProxy.Visible,
                !_workspace.IsDisposed && _workspace.Visible,
                _rootLayoutSuspended,
                _bodyLayoutSuspended,
                !_resizeProxy.IsDisposed && _resizeProxy.HasSnapshot);
            _isExitingInteractiveResize = false;
        }
    }

    private Rectangle GetWorkspaceBoundsInResizeHost()
    {
        Rectangle screenBounds = _workspace.RectangleToScreen(_workspace.ClientRectangle);
        return _resizeHost.RectangleToClient(screenBounds);
    }

    private static void AttachLayoutProbe(Control root)
    {
        root.Layout += (_, _) => FormalUiV4ResizeProbe.RecordBodyLayout();
        foreach (Control child in root.Controls)
        {
            AttachLayoutProbe(child);
        }
    }

    private void AddResizeGrip(Control host, int hitTest, Cursor cursor)
    {
        V4ResizeGrip grip = new(cursor);
        grip.MouseDown += (_, e) => BeginWindowResize(hitTest, e);
        _resizeGrips.Add(hitTest, grip);
        host.Controls.Add(grip);
        grip.BringToFront();
    }

    private void LayoutResizeGrips()
    {
        if (_resizeHost is null || _resizeGrips.Count != 8)
        {
            return;
        }

        bool visible = WindowState == FormWindowState.Normal;
        foreach (V4ResizeGrip grip in _resizeGrips.Values)
        {
            if (grip.Visible != visible)
            {
                grip.Visible = visible;
            }
        }
        if (!visible || _resizeHost.ClientSize.Width < 1 || _resizeHost.ClientSize.Height < 1)
        {
            LayoutTitleChrome();
            return;
        }

        int side = ScaleLogicalPixels(ResizeSideZoneLogicalPixels);
        int corner = ScaleLogicalPixels(ResizeCornerZoneLogicalPixels);
        int outsideLeft = Math.Max(0, _resizeHost.Left);
        int outsideTop = Math.Max(0, _resizeHost.Top);
        int outsideRight = Math.Max(0, ClientSize.Width - _resizeHost.Right);
        int outsideBottom = Math.Max(0, ClientSize.Height - _resizeHost.Bottom);
        int leftSide = Math.Max(1, side - outsideLeft);
        int rightSide = Math.Max(1, side - outsideRight);
        int topSide = Math.Max(1, side - outsideTop);
        int bottomSide = Math.Max(1, side - outsideBottom);
        int leftCorner = Math.Max(leftSide, corner - outsideLeft);
        int rightCorner = Math.Max(rightSide, corner - outsideRight);
        int topCorner = Math.Max(topSide, corner - outsideTop);
        int bottomCorner = Math.Max(bottomSide, corner - outsideBottom);
        int width = _resizeHost.ClientSize.Width;
        int height = _resizeHost.ClientSize.Height;

        SetResizeGripBounds(HtTopLeft, 0, 0, leftCorner, topCorner);
        SetResizeGripBounds(HtTopRight, width - rightCorner, 0, rightCorner, topCorner);
        SetResizeGripBounds(HtBottomLeft, 0, height - bottomCorner, leftCorner, bottomCorner);
        SetResizeGripBounds(
            HtBottomRight,
            width - rightCorner,
            height - bottomCorner,
            rightCorner,
            bottomCorner);
        SetResizeGripBounds(HtTop, leftCorner, 0, width - leftCorner - rightCorner, topSide);
        SetResizeGripBounds(
            HtBottom,
            leftCorner,
            height - bottomSide,
            width - leftCorner - rightCorner,
            bottomSide);
        SetResizeGripBounds(HtLeft, 0, topCorner, leftSide, height - topCorner - bottomCorner);
        SetResizeGripBounds(
            HtRight,
            width - rightSide,
            topCorner,
            rightSide,
            height - topCorner - bottomCorner);
        LayoutTitleChrome();
    }

    private void LayoutTitleChrome()
    {
        if (_resizeHost is null || _titleChromeHost is null)
        {
            return;
        }

        int rightInset = ScaleLogicalPixels(12);
        Point location = new(
            Math.Max(0, _resizeHost.ClientSize.Width - rightInset - _titleChromeHost.Width),
            0);
        if (_titleChromeHost.Location != location)
        {
            _titleChromeHost.Location = location;
        }
    }

    private void SetResizeGripBounds(int hitTest, int x, int y, int width, int height)
    {
        V4ResizeGrip grip = _resizeGrips[hitTest];
        Rectangle bounds = new(x, y, Math.Max(1, width), Math.Max(1, height));
        if (grip.Bounds != bounds)
        {
            grip.Bounds = bounds;
        }
    }

    private int ScaleLogicalPixels(int logicalPixels) =>
        Math.Max(1, (int)Math.Round(logicalPixels * DeviceDpi / 96f));

    private void StartPresentationRecording()
    {
        if (_recordingPresentationState != FormalUiRecordingPresentationState.Idle)
        {
            return;
        }

        CloseWindowSelectorPopover();
        CloseMicSelectorPopover();
        CloseBackgroundSelectorPopover();
        _completedDuration = TimeSpan.Zero;
        _completedOpenFolderClicked = false;
        _completedOpenVideoClicked = false;
        ResetRecordingPresentationTimerDiagnostics();
        _recordingPresentationState = FormalUiRecordingPresentationState.Recording;
        _recordingPresentationClock.Restart();
        _recordingPresentationTimer.Start();
        UpdateRecordingPresentationUi();
    }

    private void TogglePresentationPause()
    {
        if (_recordingPresentationState == FormalUiRecordingPresentationState.Recording)
        {
            CloseWindowSelectorPopover();
            CloseMicSelectorPopover();
            CloseBackgroundSelectorPopover();
            _recordingPresentationClock.Stop();
            _recordingPresentationTimer.Stop();
            _recordingPresentationState = FormalUiRecordingPresentationState.Paused;
        }
        else if (_recordingPresentationState == FormalUiRecordingPresentationState.Paused)
        {
            CloseWindowSelectorPopover();
            CloseMicSelectorPopover();
            CloseBackgroundSelectorPopover();
            _recordingPresentationState = FormalUiRecordingPresentationState.Recording;
            _recordingPresentationClock.Start();
            _recordingPresentationTimer.Start();
        }

        UpdateRecordingPresentationUi();
    }

    private void StopPresentationRecording()
    {
        if (_recordingPresentationState is not (
            FormalUiRecordingPresentationState.Recording or
            FormalUiRecordingPresentationState.Paused))
        {
            return;
        }

        CloseWindowSelectorPopover();
        CloseMicSelectorPopover();
        CloseBackgroundSelectorPopover();
        _recordingPresentationTimer.Stop();
        _recordingPresentationClock.Stop();
        _completedDuration = _recordingPresentationClock.Elapsed;
        _recordingPresentationState = FormalUiRecordingPresentationState.Completed;
        UpdateRecordingPresentationUi();
    }

    private void UpdateRecordingPresentationUi()
    {
        TimeSpan elapsed = _recordingPresentationState ==
            FormalUiRecordingPresentationState.Completed
                ? _completedDuration
                : _recordingPresentationClock.Elapsed;
        string elapsedText = FormatPresentationElapsed(elapsed);
        bool stateChanged = _lastRenderedRecordingPresentationState !=
            _recordingPresentationState;
        bool displayedSecondChanged = !string.Equals(
            _lastRenderedRecordingTimeText,
            elapsedText,
            StringComparison.Ordinal);
        if (!stateChanged && !displayedSecondChanged)
        {
            if (_insideRecordingPresentationTimerTick)
            {
                _recordingPresentationSameSecondSkipCount++;
            }
            return;
        }

        _lastRenderedRecordingPresentationState = _recordingPresentationState;
        _lastRenderedRecordingTimeText = elapsedText;
        if (_insideRecordingPresentationTimerTick)
        {
            _recordingPresentationTimerRenderCount++;
        }

        if (_preview.SetPresentationState(_recordingPresentationState, elapsedText) &&
            _insideRecordingPresentationTimerTick)
        {
            _recordingPresentationPreviewTextAssignmentCount++;
        }
        if (_recordingDeck.ShowState(_recordingPresentationState, elapsedText) &&
            _insideRecordingPresentationTimerTick)
        {
            _recordingPresentationDeckTextAssignmentCount++;
        }
        if (stateChanged)
        {
            UpdateMicPresentationUi();
        }
        else
        {
            UpdatePresentationAccessibility();
        }
    }

    private void OnRecordingPresentationTimerTick()
    {
        _recordingPresentationTimerTickCount++;
        _insideRecordingPresentationTimerTick = true;
        try
        {
            UpdateRecordingPresentationUi();
        }
        finally
        {
            _insideRecordingPresentationTimerTick = false;
        }
    }

    private void RecordRecordingPresentationTimerLayout(object? sender, LayoutEventArgs e)
    {
        if (_insideRecordingPresentationTimerTick)
        {
            _recordingPresentationTimerRelatedLayoutCount++;
        }
    }

    private void ResetRecordingPresentationTimerDiagnostics()
    {
        _recordingPresentationTimerTickCount = 0;
        _recordingPresentationTimerRenderCount = 0;
        _recordingPresentationSameSecondSkipCount = 0;
        _recordingPresentationPreviewTextAssignmentCount = 0;
        _recordingPresentationDeckTextAssignmentCount = 0;
        _recordingPresentationTimerRelatedLayoutCount = 0;
    }

    private void RecordCompletedPresentationInteraction(bool openVideo)
    {
        if (_recordingPresentationState != FormalUiRecordingPresentationState.Completed)
        {
            return;
        }

        if (openVideo)
        {
            _completedOpenVideoClicked = true;
        }
        else
        {
            _completedOpenFolderClicked = true;
        }

        System.Diagnostics.Debug.WriteLine(
            openVideo
                ? "HUMAN REVIEW: Open Video clicked; production shell launch = NO."
                : "HUMAN REVIEW: Open Folder clicked; production shell launch = NO.");
        UpdatePresentationAccessibility();
    }

    // TEST / HUMAN REVIEW ONLY. This is intentionally not exposed in the UI.
    internal void ResetPresentationToIdle()
    {
        _recordingPresentationTimer.Stop();
        _recordingPresentationClock.Reset();
        _completedDuration = TimeSpan.Zero;
        _completedOpenFolderClicked = false;
        _completedOpenVideoClicked = false;
        _recordingPresentationState = FormalUiRecordingPresentationState.Idle;
        UpdateRecordingPresentationUi();
    }

    private void WireWindowSelectorPresentation()
    {
        _windowSelectorDeck.FullScreenButton.Click += (_, _) =>
        {
            CloseMicSelectorPopover();
            CloseBackgroundSelectorPopover();
            if (_recordingPresentationState != FormalUiRecordingPresentationState.Idle)
            {
                return;
            }

            _windowTargetState.SelectFullScreen();
            _windowSelectorDeck.FullScreenButton.Selected = true;
            _windowSelectorDeck.WindowButton.Selected = false;
            CloseWindowSelectorPopover();
        };

        _windowSelectorDeck.WindowButton.Click += (_, _) =>
        {
            CloseMicSelectorPopover();
            CloseBackgroundSelectorPopover();
            if (_recordingPresentationState != FormalUiRecordingPresentationState.Idle)
            {
                return;
            }

            _windowTargetState.SelectWindowMode();
            _windowSelectorDeck.FullScreenButton.Selected = false;
            _windowSelectorDeck.WindowButton.Selected = true;
            if (_windowSelectorPopover.Visible)
            {
                CloseWindowSelectorPopover();
            }
            else
            {
                OpenWindowSelectorPopover();
            }
        };
    }

    private void OpenWindowSelectorPopover()
    {
        if (_recordingPresentationState != FormalUiRecordingPresentationState.Idle)
        {
            return;
        }

        CloseMicSelectorPopover();
        CloseBackgroundSelectorPopover();
        _windowSelectorPopover.SelectedItemId = _windowTargetState.SelectedWindow.Id;
        LayoutWindowSelectorPopover();
        _windowSelectorPopover.Visible = true;
        _windowSelectorPopover.BringToFront();
        _windowSelectorDeck.WindowButton.DropDownExpanded = true;
        _windowSelectorDeck.WindowButton.AccessibleDescription =
            $"窗口选择器已展开，当前选择 {_windowTargetState.SelectedWindow.Title}";
    }

    private void CloseWindowSelectorPopover()
    {
        if (_windowSelectorPopover is null)
        {
            return;
        }

        _windowSelectorPopover.Visible = false;
        _windowSelectorDeck.WindowButton.DropDownExpanded = false;
        _windowSelectorDeck.WindowButton.AccessibleDescription =
            $"窗口选择器已收起，当前选择 {_windowTargetState.SelectedWindow.Title}";
        UpdatePresentationAccessibility();
    }

    private void SelectPresentationWindow(FormalUiWindowPresentationItem item)
    {
        if (_recordingPresentationState != FormalUiRecordingPresentationState.Idle)
        {
            CloseWindowSelectorPopover();
            return;
        }

        _windowTargetState.SelectWindow(item);
        _windowSelectorDeck.FullScreenButton.Selected = false;
        _windowSelectorDeck.WindowButton.Selected = true;
        _windowSelectorPopover.SelectedItemId = item.Id;
        CloseWindowSelectorPopover();
    }

    private void WireMicSelectorPresentation()
    {
        _micSelectorDeck.Selector.Click += (_, _) =>
        {
            CloseBackgroundSelectorPopover();
            if (_recordingPresentationState != FormalUiRecordingPresentationState.Idle ||
                !_micDeviceState.DeviceAvailable)
            {
                CloseMicSelectorPopover();
                return;
            }

            if (_micSelectorPopover.Visible)
            {
                CloseMicSelectorPopover();
            }
            else
            {
                OpenMicSelectorPopover();
            }
        };
        _micSelectorDeck.Toggle.Click += (_, _) =>
        {
            _micDeviceState.SetMicEnabled(_micSelectorDeck.Toggle.IsOn);
            UpdateMicPresentationUi();
        };
    }

    private void OpenMicSelectorPopover()
    {
        if (_recordingPresentationState != FormalUiRecordingPresentationState.Idle ||
            !_micDeviceState.DeviceAvailable)
        {
            return;
        }

        CloseWindowSelectorPopover();
        CloseBackgroundSelectorPopover();
        _micSelectorPopover.SelectedItemId = _micDeviceState.SelectedDevice.Id;
        LayoutMicSelectorPopover();
        _micSelectorPopover.Visible = true;
        _micSelectorPopover.BringToFront();
        _micSelectorDeck.Selector.DropDownExpanded = true;
        UpdatePresentationAccessibility();
    }

    private void CloseMicSelectorPopover()
    {
        if (_micSelectorPopover is null)
        {
            return;
        }

        _micSelectorPopover.Visible = false;
        _micSelectorDeck.Selector.DropDownExpanded = false;
        UpdatePresentationAccessibility();
    }

    private void SelectPresentationMicDevice(FormalUiMicDevicePresentationItem item)
    {
        if (_recordingPresentationState != FormalUiRecordingPresentationState.Idle ||
            !_micDeviceState.DeviceAvailable)
        {
            CloseMicSelectorPopover();
            return;
        }

        _micDeviceState.Select(item);
        _micSelectorPopover.SelectedItemId = _micDeviceState.SelectedDevice.Id;
        _micSelectorDeck.Selector.SetDeviceName(_micDeviceState.SelectedDevice.Name);
        CloseMicSelectorPopover();
        UpdatePresentationAccessibility();
    }

    private void WireBackgroundSelectorPresentation()
    {
        _backgroundSelectorDeck.Selector.Click += (_, _) =>
        {
            CloseWindowSelectorPopover();
            CloseMicSelectorPopover();
            if (_backgroundSelectorPopover.Visible)
            {
                CloseBackgroundSelectorPopover();
            }
            else
            {
                OpenBackgroundSelectorPopover();
            }
        };
    }

    private void OpenBackgroundSelectorPopover()
    {
        CloseWindowSelectorPopover();
        CloseMicSelectorPopover();
        _backgroundSelectorPopover.SelectedItemId = _backgroundState.ActiveItemId;
        LayoutBackgroundSelectorPopover();
        _backgroundSelectorPopover.Visible = true;
        _backgroundSelectorPopover.BringToFront();
        _backgroundSelectorDeck.Selector.DropDownExpanded = true;
        UpdateBackgroundSelectorUi();
        UpdatePresentationAccessibility();
    }

    private void CloseBackgroundSelectorPopover()
    {
        if (_backgroundSelectorPopover is null)
        {
            return;
        }

        _backgroundSelectorPopover.Visible = false;
        _backgroundSelectorDeck.Selector.DropDownExpanded = false;
        UpdatePresentationAccessibility();
    }

    private void InvokeBackgroundItem(FormalUiBackgroundPresentationItem item)
    {
        CloseBackgroundSelectorPopover();
        if (item.OpensFileDialog)
        {
            SelectPresentationCustomImage();
            return;
        }

        _backgroundState.SelectPreset(item);
        _backgroundSelectorPopover.SelectedItemId = _backgroundState.ActiveItemId;
        UpdateBackgroundSelectorUi();
        UpdatePresentationAccessibility();
    }

    private void SelectPresentationCustomImage()
    {
        using OpenFileDialog dialog = new()
        {
            Title = "选择自定义背景图片",
            Filter = "图片文件 (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp",
            FilterIndex = 1,
            CheckFileExists = true,
            CheckPathExists = true,
            Multiselect = false,
            RestoreDirectory = true,
        };
        if (dialog.ShowDialog(this) != DialogResult.OK ||
            !IsAllowedBackgroundImagePath(dialog.FileName))
        {
            return;
        }

        _backgroundState.SelectCustomImage(dialog.FileName);
        _backgroundSelectorPopover.SelectedItemId = _backgroundState.ActiveItemId;
        UpdateBackgroundSelectorUi();
        UpdatePresentationAccessibility();
    }

    private static bool IsAllowedBackgroundImagePath(string path)
    {
        string extension = Path.GetExtension(path);
        return File.Exists(path) &&
            (extension.Equals(".png", StringComparison.OrdinalIgnoreCase) ||
             extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
             extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase) ||
             extension.Equals(".bmp", StringComparison.OrdinalIgnoreCase));
    }

    private void UpdateBackgroundSelectorUi()
    {
        _backgroundSelectorDeck.Selector.Text = _backgroundState.SelectorDisplayName;
        _backgroundSelectorDeck.Selector.LeadingGlyph =
            _backgroundState.BackgroundMode == FormalUiBackgroundMode.CustomImage
                ? "\uE91B"
                : "\uE790";
        _backgroundSelectorDeck.Selector.AccessibleName =
            $"背景：{_backgroundState.SelectorDisplayName}";
        _backgroundSelectorDeck.Selector.AccessibleDescription =
            _backgroundState.BackgroundMode == FormalUiBackgroundMode.CustomImage
                ? $"自定义图片；{_backgroundState.SelectedCustomImagePath}"
                : $"背景预设；{_backgroundState.SelectedPreset.DisplayName}";
        _backgroundSelectorDeck.Selector.Invalidate();
    }

    private void SetMicDeviceAvailable(bool available)
    {
        _micDeviceState.SetDeviceAvailable(available);
        if (!available)
        {
            CloseMicSelectorPopover();
        }
        UpdateMicPresentationUi();
    }

    private void UpdateMicPresentationUi()
    {
        bool available = _micDeviceState.DeviceAvailable;
        bool locked = _recordingPresentationState != FormalUiRecordingPresentationState.Idle;
        _micSelectorDeck.Selector.VisualState = !available
            ? V4MicSelectorVisualState.NoDevice
            : locked
                ? V4MicSelectorVisualState.Locked
                : V4MicSelectorVisualState.Normal;
        _micSelectorDeck.Selector.SetDeviceName(_micDeviceState.SelectedDevice.Name);
        _micSelectorDeck.Toggle.IsOn = _micDeviceState.MicEnabled;
        _micSelectorDeck.Toggle.Enabled = available;
        _micSelectorDeck.Meter.Active = available && _micDeviceState.MicEnabled;
        _micSelectorDeck.Meter.Invalidate();
        if (locked || !available)
        {
            CloseMicSelectorPopover();
        }
        UpdatePresentationAccessibility();
    }

    private void UpdatePresentationAccessibility()
    {
        AccessibleDescription =
            $"RecordingState={_recordingPresentationState}; " +
            $"MicDeviceAvailable={_micDeviceState.DeviceAvailable}; " +
            $"MicEnabled={_micDeviceState.MicEnabled}; " +
            $"SelectedDevice={_micDeviceState.SelectedDevice.Name}; " +
            $"MicPopover={(_micSelectorPopover?.Visible == true ? "OPEN" : "CLOSED")}; " +
            $"WindowPopover={(_windowSelectorPopover?.Visible == true ? "OPEN" : "CLOSED")}; " +
            $"BackgroundMode={_backgroundState.BackgroundMode}; " +
            $"SelectedBackground={_backgroundState.SelectorDisplayName}; " +
            $"CustomImagePath={_backgroundState.SelectedCustomImagePath ?? "NONE"}; " +
            $"BackgroundPopover={(_backgroundSelectorPopover?.Visible == true ? "OPEN" : "CLOSED")}; " +
            $"SettingsView={(_settingsView?.Visible == true ? "OPEN" : "CLOSED")}; " +
            $"ResetDefaultsRequested={(_settingsView?.ResetDefaultsRequested == true)}; " +
            $"SettingsContent={_settingsView?.ActiveContentName ?? "NONE"}; " +
            $"CompletedDuration={FormatPresentationElapsed(_completedDuration)}; " +
            $"OpenFolderClicked={_completedOpenFolderClicked}; " +
            $"OpenVideoClicked={_completedOpenVideoClicked}; " +
            "CompletedResult=PRESENTATION ONLY; " +
            $"TimerTicks={_recordingPresentationTimerTickCount}; " +
            $"TimerRenders={_recordingPresentationTimerRenderCount}; " +
            $"SameSecondSkips={_recordingPresentationSameSecondSkipCount}; " +
            $"PreviewTextAssignments={_recordingPresentationPreviewTextAssignmentCount}; " +
            $"DeckTextAssignments={_recordingPresentationDeckTextAssignmentCount}; " +
            $"TimerRelatedLayouts={_recordingPresentationTimerRelatedLayoutCount}; " +
            $"PreviewStatusBounds={_preview.PresentationStatusBounds}; " +
            $"DeckTimerBounds={_recordingDeck.TimerBounds}; " +
            $"AtomicTimerPrepared={_recordingDeck.AtomicTimerPreparedFrameCount}; " +
            $"AtomicTimerPaints={_recordingDeck.AtomicTimerPaintCount}; " +
            $"AtomicTimerBuffers={_recordingDeck.AtomicTimerBufferAllocationCount}; " +
            $"AtomicTimerErase={_recordingDeck.AtomicTimerEraseBackgroundMessageCount}; " +
            $"AtomicPreviewPrepared={_preview.AtomicStatusPreparedFrameCount}; " +
            $"AtomicPreviewPaints={_preview.AtomicStatusPaintCount}; " +
            $"AtomicPreviewBuffers={_preview.AtomicStatusBufferAllocationCount}; " +
            $"AtomicPreviewErase={_preview.AtomicStatusEraseBackgroundMessageCount}";
    }

    private void LayoutMicSelectorPopover()
    {
        if (_resizeHost is null ||
            _micSelectorDeck is null ||
            _micSelectorPopover is null ||
            !_micSelectorDeck.Selector.IsHandleCreated)
        {
            return;
        }

        Rectangle cardBounds = _resizeHost.RectangleToClient(
            _micSelectorDeck.Card.RectangleToScreen(
                _micSelectorDeck.Card.ClientRectangle));
        Rectangle selectorBounds = _resizeHost.RectangleToClient(
            _micSelectorDeck.Selector.RectangleToScreen(
                _micSelectorDeck.Selector.ClientRectangle));
        int safeInset = ScaleLogicalPixels(ResizeSideZoneLogicalPixels);
        int width = Math.Max(176, cardBounds.Width - 10);
        int x = cardBounds.Left + 5;
        x = Math.Max(safeInset, Math.Min(x, _resizeHost.ClientSize.Width - safeInset - width));

        int preferredY = selectorBounds.Bottom + 4;
        int maximumY = _resizeHost.ClientSize.Height - safeInset - _micSelectorPopover.Height;
        int minimumY = FormalUiV4Tokens.TitleBarHeight + 2;
        int y = Math.Max(minimumY, Math.Min(preferredY, maximumY));
        _micSelectorPopover.Bounds = new Rectangle(x, y, width, _micSelectorPopover.Height);
    }

    private void LayoutWindowSelectorPopover()
    {
        if (_resizeHost is null ||
            _windowSelectorDeck is null ||
            _windowSelectorPopover is null ||
            !_windowSelectorDeck.WindowButton.IsHandleCreated)
        {
            return;
        }

        Rectangle cardBounds = _resizeHost.RectangleToClient(
            _windowSelectorDeck.Card.RectangleToScreen(
                _windowSelectorDeck.Card.ClientRectangle));
        Rectangle windowButtonBounds = _resizeHost.RectangleToClient(
            _windowSelectorDeck.WindowButton.RectangleToScreen(
                _windowSelectorDeck.WindowButton.ClientRectangle));
        int safeInset = ScaleLogicalPixels(ResizeSideZoneLogicalPixels);
        int width = Math.Max(176, cardBounds.Width - 10);
        int x = cardBounds.Left + 5;
        x = Math.Max(safeInset, Math.Min(x, _resizeHost.ClientSize.Width - safeInset - width));

        int preferredY = windowButtonBounds.Bottom + 4;
        int maximumY = _resizeHost.ClientSize.Height - safeInset - _windowSelectorPopover.Height;
        int minimumY = FormalUiV4Tokens.TitleBarHeight + 2;
        int y = Math.Max(minimumY, Math.Min(preferredY, maximumY));
        _windowSelectorPopover.Bounds = new Rectangle(
            x,
            y,
            width,
            _windowSelectorPopover.Height);
    }

    private void LayoutBackgroundSelectorPopover()
    {
        if (_resizeHost is null ||
            _backgroundSelectorDeck is null ||
            _backgroundSelectorPopover is null ||
            !_backgroundSelectorDeck.Selector.IsHandleCreated)
        {
            return;
        }

        Rectangle cardBounds = _resizeHost.RectangleToClient(
            _backgroundSelectorDeck.Card.RectangleToScreen(
                _backgroundSelectorDeck.Card.ClientRectangle));
        Rectangle selectorBounds = _resizeHost.RectangleToClient(
            _backgroundSelectorDeck.Selector.RectangleToScreen(
                _backgroundSelectorDeck.Selector.ClientRectangle));
        int safeInset = ScaleLogicalPixels(ResizeSideZoneLogicalPixels);
        int width = Math.Max(176, cardBounds.Width - 10);
        int x = cardBounds.Left + 5;
        x = Math.Max(safeInset, Math.Min(x, _resizeHost.ClientSize.Width - safeInset - width));

        int gap = 4;
        int minimumY = FormalUiV4Tokens.TitleBarHeight + 2;
        int maximumY = _resizeHost.ClientSize.Height - safeInset - _backgroundSelectorPopover.Height;
        int belowY = selectorBounds.Bottom + gap;
        int aboveY = selectorBounds.Top - gap - _backgroundSelectorPopover.Height;
        int y = belowY <= maximumY ? belowY : aboveY;
        y = Math.Max(minimumY, Math.Min(y, maximumY));
        _backgroundSelectorPopover.Bounds = new Rectangle(
            x,
            y,
            width,
            _backgroundSelectorPopover.Height);
    }

    private static string FormatPresentationElapsed(TimeSpan elapsed)
    {
        int hours = Math.Min(99, Math.Max(0, (int)elapsed.TotalHours));
        return $"{hours:00}:{elapsed.Minutes:00}:{elapsed.Seconds:00}";
    }

    private Control BuildTitleBar(
        out V4ChromeButton maximizeButton,
        out Control titleChromeHost)
    {
        TableLayoutPanel titleBar = new()
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            ColumnCount = 7,
            RowCount = 1,
            Margin = Padding.Empty,
            Padding = new Padding(16, 0, 12, 0),
        };
        titleBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 30));
        titleBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        titleBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 64));
        titleBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 1));
        titleBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 40));
        titleBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 40));
        titleBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 40));
        titleBar.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        V4LegacyWordmark wordmark = new()
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
        };
        V4ChromeButton settings = CreateChromeButton("设置", 64, 9f);
        settings.SizeSurfaceToText = true;
        Panel separator = new()
        {
            Dock = DockStyle.Fill,
            BackColor = FormalUiV4Tokens.ControlBorder,
            Margin = new Padding(0, 14, 0, 14),
        };
        V4ChromeButton minimize = CreateChromeButton("\uE921", 40, 9.5f);
        maximizeButton = CreateChromeButton("\uE922", 40, 9.5f);
        V4ChromeButton close = CreateChromeButton("\uE8BB", 40, 9.5f);
        close.Danger = true;

        settings.Click += (_, _) => OpenSettingsView();

        V4ChromeButton[] captionButtons =
        {
            settings,
            minimize,
            maximizeButton,
            close,
        };
        foreach (V4ChromeButton button in captionButtons)
        {
            button.TopResizePassThroughLogicalPixels = ResizeSideZoneLogicalPixels;
        }

        minimize.Click += (_, _) => WindowState = FormWindowState.Minimized;
        maximizeButton.Click += (_, _) => ToggleMaximize();
        close.Click += (_, _) => Close();

        titleBar.MouseDown += BeginWindowDrag;
        wordmark.MouseDown += BeginWindowDrag;
        titleBar.DoubleClick += (_, _) => ToggleMaximize();
        wordmark.DoubleClick += (_, _) => ToggleMaximize();

        titleBar.Controls.Add(wordmark, 0, 0);
        titleBar.SetColumnSpan(wordmark, 2);

        V4TitleChromeHost chromeHost = new()
        {
            BackColor = Color.Transparent,
            ColumnCount = 5,
            RowCount = 1,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            Size = new Size(185, FormalUiV4Tokens.TitleBarHeight),
            TopResizePassThroughLogicalPixels = ResizeSideZoneLogicalPixels,
        };
        chromeHost.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 64));
        chromeHost.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 1));
        chromeHost.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 40));
        chromeHost.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 40));
        chromeHost.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 40));
        chromeHost.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        chromeHost.Controls.Add(settings, 0, 0);
        chromeHost.Controls.Add(separator, 1, 0);
        chromeHost.Controls.Add(minimize, 2, 0);
        chromeHost.Controls.Add(maximizeButton, 3, 0);
        chromeHost.Controls.Add(close, 4, 0);
        titleChromeHost = chromeHost;
        return titleBar;
    }

    private void OpenSettingsView()
    {
        if (_recordingPresentationState is
            FormalUiRecordingPresentationState.Recording or
            FormalUiRecordingPresentationState.Paused)
        {
            return;
        }

        CloseWindowSelectorPopover();
        CloseMicSelectorPopover();
        CloseBackgroundSelectorPopover();
        PrepareHomeRevealSnapshot();
        ApplyWindowBorderColor(settingsVisible: true);
        ApplySettingsTitleBand(settingsVisible: true);
        _settingsView.ShowDefaultContent();
        _workspace.Visible = false;
        _settingsView.Visible = true;
        _settingsView.BringToFront();
        _settingsView.FocusBackButton();
        UpdatePresentationAccessibility();
    }

    private void CloseSettingsView()
    {
        bool redrawHeld = BeginAtomicHomeReveal();
        try
        {
            ApplyWindowBorderColor(settingsVisible: false);
            ApplySettingsTitleBand(settingsVisible: false);
            _settingsView.Visible = false;
            _workspace.Visible = true;
            _workspace.BringToFront();
            PrepareHomeForAtomicReveal();
            UpdatePresentationAccessibility();
        }
        finally
        {
            CompleteAtomicHomeReveal(redrawHeld);
        }
    }

    private bool BeginAtomicHomeReveal()
    {
        if (_atomicHomeRevealActive || !IsHandleCreated || IsDisposed)
        {
            return false;
        }

        _atomicHomeRevealActive = true;
        ShowHomeRevealSurface();
        _ = SendMessage(Handle, WmSetRedraw, IntPtr.Zero, IntPtr.Zero);
        return true;
    }

    private void PrepareHomeRevealSnapshot()
    {
        if (_resizeHost is null || _resizeHost.IsDisposed || !_resizeHost.IsHandleCreated)
        {
            return;
        }

        _ = _homeRevealSurface.TryCapture(_resizeHost);
    }

    private void ShowHomeRevealSurface()
    {
        if (!_homeRevealSurface.HasSnapshot || _resizeHost.IsDisposed)
        {
            return;
        }

        _homeRevealSurface.Bounds = _resizeHost.ClientRectangle;
        _homeRevealSurface.Visible = true;
        _homeRevealSurface.BringToFront();
        _homeRevealSurface.Update();
    }

    private void PrepareHomeForAtomicReveal()
    {
        if (_rootLayout is null || _workspace is null ||
            _rootLayout.IsDisposed || _workspace.IsDisposed)
        {
            return;
        }

        _rootLayout.PerformLayout();
        _workspace.PerformLayout();
    }

    private void CompleteAtomicHomeReveal(bool redrawHeld)
    {
        if (!redrawHeld)
        {
            return;
        }

        try
        {
            _ = SendMessage(Handle, WmSetRedraw, (IntPtr)1, IntPtr.Zero);
            if (_workspace.IsHandleCreated && !_workspace.IsDisposed)
            {
                _ = RedrawWindow(
                    _workspace.Handle,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    RedrawInvalidate |
                    RedrawErase |
                    RedrawAllChildren |
                    RedrawUpdateNow);
            }

            _homeRevealSurface.Visible = false;
            _ = RedrawWindow(
                Handle,
                IntPtr.Zero,
                IntPtr.Zero,
                RedrawInvalidate |
                RedrawErase |
                RedrawAllChildren |
                RedrawUpdateNow |
                RedrawFrame);
        }
        finally
        {
            _homeRevealSurface.Visible = false;
            _homeRevealSurface.ClearSnapshot();
            _atomicHomeRevealActive = false;
        }
    }

    private void ApplyWindowBorderColor(bool settingsVisible)
    {
        if (!IsHandleCreated)
        {
            return;
        }

        int color = settingsVisible
            ? SettingsWindowBorderColor
            : DwmColorDefault;
        _ = DwmSetWindowAttribute(
            Handle,
            DwmWindowBorderColor,
            ref color,
            sizeof(int));
    }

    private void ApplySettingsTitleBand(bool settingsVisible)
    {
        Color color = settingsVisible ? SettingsTitleBandColor : Color.Transparent;
        _resizeHost.TitleBandColor = settingsVisible ? SettingsTitleBandColor : null;
        BackColor = settingsVisible ? SettingsTitleBandColor : FormalUiV4Tokens.Border;
        _titleBar.BackColor = color;
        _titleChromeHost.BackColor = color;
        foreach (Control child in _titleBar.Controls)
        {
            child.BackColor = color;
        }
        _titleBar.Invalidate(true);
        _titleChromeHost.Invalidate(true);
    }

    private void ApplyPresentationReset()
    {
        if (!_settingsView.Visible)
        {
            return;
        }

        _settingsView.ApplyPresentationDefaults();
        UpdatePresentationAccessibility();
    }

    private static V4PreviewPanel BuildPreview()
    {
        return new V4PreviewPanel
        {
            Name = "FormalPreview",
            Dock = DockStyle.Fill,
            Margin = new Padding(
                FormalUiV4Tokens.OuterPadding,
                0,
                FormalUiV4Tokens.OuterPadding,
                10),
            PlaceholderVisible = true,
            PreviewAspectRatio = 16f / 9f,
        };
    }

    private static Control BuildConsole(
        out RecordingDeckView recordingDeck,
        out WindowSelectorDeckView windowSelectorDeck,
        out MicSelectorDeckView micSelectorDeck,
        out BackgroundSelectorDeckView backgroundSelectorDeck)
    {
        TableLayoutPanel console = new()
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            ColumnCount = 4,
            RowCount = 1,
            Margin = new Padding(
                FormalUiV4Tokens.OuterPadding,
                0,
                FormalUiV4Tokens.OuterPadding,
                FormalUiV4Tokens.ConsoleBottomMargin),
            Padding = Padding.Empty,
            MinimumSize = new Size(0, FormalUiV4Tokens.ConsoleUsableHeight),
        };
        console.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));
        console.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));
        console.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));
        console.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));
        console.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        Control preparation = BuildPreparationCard(out windowSelectorDeck, out micSelectorDeck);
        Control director = BuildDirectorCard();
        Control camera = BuildThreeDimensionalCard(out backgroundSelectorDeck);
        recordingDeck = BuildSaveCard();
        Control save = recordingDeck.Root;
        preparation.Margin = new Padding(0, 0, 7, 0);
        director.Margin = new Padding(3, 0, 5, 0);
        camera.Margin = new Padding(5, 0, 3, 0);
        save.Margin = new Padding(7, 0, 0, 0);
        console.Controls.Add(preparation, 0, 0);
        console.Controls.Add(director, 1, 0);
        console.Controls.Add(camera, 2, 0);
        console.Controls.Add(save, 3, 0);
        return console;
    }

    private static Control BuildPreparationCard(
        out WindowSelectorDeckView windowSelectorDeck,
        out MicSelectorDeckView micSelectorDeck)
    {
        TableLayoutPanel body = CreateCard("录制准备", "\uE714", out V4RoundedPanel card);
        card.MinimumSize = new Size(0, FormalUiV4Tokens.PreparationCardRequiredHeight);
        body.RowCount = 4;
        body.RowStyles.Add(new RowStyle(
            SizeType.Absolute,
            FormalUiV4Tokens.PreparationTargetHeight));
        body.RowStyles.Add(new RowStyle(
            SizeType.Absolute,
            FormalUiV4Tokens.PreparationCursorHeight));
        body.RowStyles.Add(new RowStyle(
            SizeType.Absolute,
            FormalUiV4Tokens.PreparationAudioHeight));
        body.RowStyles.Add(new RowStyle(
            SizeType.Absolute,
            FormalUiV4Tokens.PreparationAudioHeight));

        TableLayoutPanel target = new()
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            ColumnCount = 1,
            RowCount = 2,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
        };
        target.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        target.RowStyles.Add(new RowStyle(SizeType.Absolute, 18));
        target.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        target.Controls.Add(CreateCaption("捕获目标"), 0, 0);

        TableLayoutPanel choices = new()
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            ColumnCount = 2,
            RowCount = 1,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
        };
        choices.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        choices.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        V4StyledButton fullScreen = CreateChoice("全屏", true, "\uE740");
        V4StyledButton window = CreateChoice("窗口", false, "\uE737");
        fullScreen.Name = "FullScreenCaptureModeButton";
        fullScreen.AccessibleName = "全屏";
        window.Name = "WindowCaptureModeButton";
        window.AccessibleName = "窗口";
        fullScreen.Margin = new Padding(0, 0, 4, 0);
        window.Margin = new Padding(4, 0, 0, 0);
        window.ShowDropDown = true;
        choices.Controls.Add(fullScreen, 0, 0);
        choices.Controls.Add(window, 1, 0);
        target.Controls.Add(choices, 0, 1);

        body.Controls.Add(target, 0, 0);
        body.Controls.Add(CreateToggleLine("\uE962", "鼠标隐藏", false), 0, 1);
        body.Controls.Add(
            CreateMicrophoneLine(
                out V4MicDeviceSelector micSelector,
                out V4Toggle micToggle,
                out V4MeterPlaceholder micMeter),
            0,
            2);
        body.Controls.Add(CreateSystemAudioLine(), 0, 3);
        windowSelectorDeck = new WindowSelectorDeckView(card, fullScreen, window);
        micSelectorDeck = new MicSelectorDeckView(card, micSelector, micToggle, micMeter);
        return card;
    }

    private static Control BuildDirectorCard()
    {
        TableLayoutPanel body = CreateCard("导演控制", "\uE7F4", out V4RoundedPanel card);
        body.RowCount = 8;
        body.RowStyles.Add(new RowStyle(SizeType.Percent, 12f));
        body.RowStyles.Add(new RowStyle(SizeType.Percent, 12f));
        body.RowStyles.Add(new RowStyle(SizeType.Percent, 23f));
        body.RowStyles.Add(new RowStyle(SizeType.Percent, 15f));
        body.RowStyles.Add(new RowStyle(SizeType.Percent, 12f));
        body.RowStyles.Add(new RowStyle(SizeType.Percent, 5f));
        body.RowStyles.Add(new RowStyle(SizeType.Percent, 10f));
        body.RowStyles.Add(new RowStyle(SizeType.Percent, 11f));
        body.Controls.Add(CreateCaption("镜头"), 0, 0);
        body.Controls.Add(CreateBodyText("手动镜头", FontStyle.Regular), 0, 1);

        TableLayoutPanel cameraButtons = new()
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            ColumnCount = 2,
            RowCount = 1,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
        };
        cameraButtons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        cameraButtons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        V4StyledButton zoom16 = CreateChoice("1.6x", true);
        V4StyledButton zoom20 = CreateChoice("2.0x", false);
        zoom16.Margin = new Padding(0, 0, 5, 0);
        zoom20.Margin = new Padding(5, 0, 0, 0);
        WireExclusive(zoom16, zoom20);
        cameraButtons.Controls.Add(zoom16, 0, 0);
        cameraButtons.Controls.Add(zoom20, 1, 0);
        body.Controls.Add(cameraButtons, 0, 2);
        body.Controls.Add(CreateToggleLine(string.Empty, "镜头快捷键", true), 0, 3);
        body.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            Text = "F9 = 1.6x    F10 = 2.0x",
            TextAlign = ContentAlignment.MiddleLeft,
            Font = FormalUiV4Tokens.Ui(7.5f),
            ForeColor = FormalUiV4Tokens.InkMuted,
            Margin = Padding.Empty,
        }, 0, 4);
        body.Controls.Add(new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = FormalUiV4Tokens.ControlBorder,
            Margin = new Padding(0, 2, 0, 2),
        }, 0, 5);
        body.Controls.Add(CreateBodyText("自动镜头", FontStyle.Regular), 0, 6);
        body.Controls.Add(CreateToggleLine(string.Empty, "自动跟随重点", false), 0, 7);
        return card;
    }

    private static Control BuildThreeDimensionalCard(
        out BackgroundSelectorDeckView backgroundSelectorDeck)
    {
        TableLayoutPanel body = CreateCard("3D运镜", "\uE809", out V4RoundedPanel card);
        body.RowCount = 3;
        body.RowStyles.Add(new RowStyle(SizeType.Percent, 34f));
        body.RowStyles.Add(new RowStyle(SizeType.Percent, 34f));
        body.RowStyles.Add(new RowStyle(SizeType.Percent, 32f));

        V4StyledButton left = CreateChoice("左倾", true);
        V4StyledButton front = CreateChoice("正面", false);
        V4StyledButton right = CreateChoice("右倾", false);
        WireExclusive(left, front, right);
        body.Controls.Add(CreateSegmentGroup("展示角度", left, front, right), 0, 0);

        V4StyledButton light = CreateChoice("轻", true);
        V4StyledButton medium = CreateChoice("中", false);
        V4StyledButton strong = CreateChoice("强", false);
        WireExclusive(light, medium, strong);
        body.Controls.Add(CreateSegmentGroup("倾斜强度", light, medium, strong), 0, 1);

        TableLayoutPanel background = new()
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            ColumnCount = 1,
            RowCount = 2,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
        };
        background.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        background.RowStyles.Add(new RowStyle(SizeType.Absolute, 18));
        background.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        background.Controls.Add(CreateCaption("背景"), 0, 0);
        V4SelectBox backgroundSelector = new("暖白")
        {
            Name = "BackgroundSelector",
            AccessibleName = "背景：暖白",
            Dock = DockStyle.Fill,
            LeadingGlyph = "\uE790",
            Margin = Padding.Empty,
        };
        background.Controls.Add(backgroundSelector, 0, 1);
        body.Controls.Add(background, 0, 2);
        backgroundSelectorDeck = new BackgroundSelectorDeckView(card, backgroundSelector);
        return card;
    }

    private static RecordingDeckView BuildSaveCard()
    {
        TableLayoutPanel body = CreateCard(
            "保存 / 录制",
            "\uE8B7",
            out V4RoundedPanel card,
            out Label headingLabel);
        card.Name = "RecordingDeck";
        headingLabel.Name = "RecordingDeckHeading";
        body.RowCount = 1;
        body.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        Panel stateHost = new()
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
        };

        TableLayoutPanel idleContent = new()
        {
            Name = "RecordingDeckIdleContent",
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            ColumnCount = 1,
            RowCount = 3,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
        };
        idleContent.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        idleContent.RowStyles.Add(new RowStyle(SizeType.Absolute, 18));
        idleContent.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        idleContent.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        idleContent.Controls.Add(CreateCaption("保存位置"), 0, 0);
        idleContent.Controls.Add(new V4PathBox(@"D:\小白录屏\录制文件")
        {
            Name = "RecordingSavePath",
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
        }, 0, 1);
        V4RecordButton startButton = new()
        {
            Name = "PresentationStartButton",
            AccessibleName = "开始录制",
            Dock = DockStyle.Fill,
            Text = "开始录制",
            CornerRadius = 12,
            Margin = new Padding(0, 12, 0, 0),
        };
        idleContent.Controls.Add(startButton, 0, 2);

        TableLayoutPanel activeContent = new()
        {
            Name = "RecordingDeckActiveContent",
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            ColumnCount = 1,
            RowCount = 5,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            Visible = false,
        };
        activeContent.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        activeContent.RowStyles.Add(new RowStyle(SizeType.Absolute, 18));
        activeContent.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        activeContent.RowStyles.Add(new RowStyle(SizeType.Absolute, 18));
        activeContent.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        activeContent.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        activeContent.Controls.Add(CreateCaption("状态"), 0, 0);
        Label statusLabel = new()
        {
            Name = "RecordingDeckStatus",
            Dock = DockStyle.Fill,
            Text = "● 正在录制",
            TextAlign = ContentAlignment.MiddleLeft,
            Font = FormalUiV4Tokens.Ui(8.2f, FontStyle.Bold),
            ForeColor = FormalUiV4Tokens.AccentTop,
            Margin = Padding.Empty,
        };
        activeContent.Controls.Add(statusLabel, 0, 1);
        activeContent.Controls.Add(CreateCaption("已录制时间"), 0, 2);
        FormalUiStableTimerSurface timerSurface = new()
        {
            Name = "RecordingDeckTimer",
            Dock = DockStyle.Fill,
            Font = FormalUiV4Tokens.Ui(15.5f, FontStyle.Bold),
            ForeColor = FormalUiV4Tokens.AccentTop,
            Margin = Padding.Empty,
        };
        activeContent.Controls.Add(timerSurface, 0, 3);

        TableLayoutPanel actions = new()
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0, 10, 0, 0),
            Padding = Padding.Empty,
        };
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        actions.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        V4StyledButton pauseResumeButton = new()
        {
            Name = "PresentationPauseResumeButton",
            AccessibleName = "暂停",
            Dock = DockStyle.Fill,
            Text = "暂停",
            Font = FormalUiV4Tokens.Ui(9f, FontStyle.Bold),
            Margin = new Padding(0, 0, 5, 0),
        };
        V4StyledButton stopButton = new()
        {
            Name = "PresentationStopButton",
            AccessibleName = "停止",
            Dock = DockStyle.Fill,
            Text = "停止",
            Font = FormalUiV4Tokens.Ui(9f, FontStyle.Bold),
            Accent = true,
            Margin = new Padding(5, 0, 0, 0),
        };
        actions.Controls.Add(pauseResumeButton, 0, 0);
        actions.Controls.Add(stopButton, 1, 0);
        activeContent.Controls.Add(actions, 0, 4);

        TableLayoutPanel completedContent = new()
        {
            Name = "RecordingDeckCompletedContent",
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            ColumnCount = 1,
            RowCount = 7,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            Visible = false,
        };
        completedContent.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        completedContent.RowStyles.Add(new RowStyle(SizeType.Absolute, 14));
        completedContent.RowStyles.Add(new RowStyle(SizeType.Absolute, 18));
        completedContent.RowStyles.Add(new RowStyle(SizeType.Absolute, 14));
        completedContent.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        completedContent.RowStyles.Add(new RowStyle(SizeType.Absolute, 18));
        completedContent.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));
        completedContent.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        completedContent.Controls.Add(CreateCaption("状态"), 0, 0);
        Label completedStatusLabel = new()
        {
            Name = "RecordingDeckCompletedStatus",
            AccessibleName = "状态：已保存（演示）",
            Dock = DockStyle.Fill,
            Text = "已保存",
            TextAlign = ContentAlignment.MiddleLeft,
            Font = FormalUiV4Tokens.Ui(8.2f, FontStyle.Bold),
            ForeColor = FormalUiV4Tokens.InkMuted,
            Margin = Padding.Empty,
        };
        completedContent.Controls.Add(completedStatusLabel, 0, 1);
        completedContent.Controls.Add(CreateCaption("录制时长"), 0, 2);
        Label completedTimerLabel = new()
        {
            Name = "RecordingDeckCompletedTimer",
            Dock = DockStyle.Fill,
            Text = "00:00:00",
            TextAlign = ContentAlignment.MiddleLeft,
            Font = FormalUiV4Tokens.Ui(14.5f, FontStyle.Bold),
            ForeColor = FormalUiV4Tokens.Ink,
            AutoSize = false,
            Margin = Padding.Empty,
        };
        completedContent.Controls.Add(completedTimerLabel, 0, 3);
        Label completedDirectoryLabel = new()
        {
            Name = "RecordingDeckCompletedDirectory",
            AccessibleName = "演示保存目录",
            Dock = DockStyle.Fill,
            Text = FormalUiRecordingCompletedPresentation.CompletedDirectory,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = FormalUiV4Tokens.Ui(7.2f),
            ForeColor = FormalUiV4Tokens.InkMuted,
            AutoEllipsis = true,
            Margin = Padding.Empty,
        };
        completedContent.Controls.Add(completedDirectoryLabel, 0, 4);
        Label completedFileNameLabel = new()
        {
            Name = "RecordingDeckCompletedFileName",
            AccessibleName = "演示文件名",
            Dock = DockStyle.Fill,
            Text = FormalUiRecordingCompletedPresentation.CompletedFileName,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = FormalUiV4Tokens.Ui(7.2f, FontStyle.Bold),
            ForeColor = FormalUiV4Tokens.Ink,
            AutoEllipsis = true,
            Margin = Padding.Empty,
        };
        completedContent.Controls.Add(completedFileNameLabel, 0, 5);

        TableLayoutPanel completedActions = new()
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0, 7, 0, 0),
            Padding = Padding.Empty,
        };
        completedActions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        completedActions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        completedActions.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        V4StyledButton openFolderButton = new()
        {
            Name = "PresentationOpenFolderButton",
            AccessibleName = "打开文件夹（仅演示）",
            Dock = DockStyle.Fill,
            Text = "打开文件夹",
            Font = FormalUiV4Tokens.Ui(8.2f, FontStyle.Bold),
            Margin = new Padding(0, 0, 4, 0),
        };
        V4StyledButton openVideoButton = new()
        {
            Name = "PresentationOpenVideoButton",
            AccessibleName = "打开视频（仅演示）",
            Dock = DockStyle.Fill,
            Text = "打开视频",
            Font = FormalUiV4Tokens.Ui(8.2f, FontStyle.Bold),
            Accent = true,
            Margin = new Padding(4, 0, 0, 0),
        };
        completedActions.Controls.Add(openFolderButton, 0, 0);
        completedActions.Controls.Add(openVideoButton, 1, 0);
        completedContent.Controls.Add(completedActions, 0, 6);

        stateHost.Controls.Add(completedContent);
        stateHost.Controls.Add(activeContent);
        stateHost.Controls.Add(idleContent);
        idleContent.BringToFront();
        body.Controls.Add(stateHost, 0, 0);
        return new RecordingDeckView(
            card,
            headingLabel,
            idleContent,
            activeContent,
            completedContent,
            statusLabel,
            timerSurface,
            completedTimerLabel,
            startButton,
            pauseResumeButton,
            stopButton,
            openFolderButton,
            openVideoButton);
    }

    private static TableLayoutPanel CreateCard(
        string title,
        string iconGlyph,
        out V4RoundedPanel card)
    {
        return CreateCard(title, iconGlyph, out card, out _);
    }

    private static TableLayoutPanel CreateCard(
        string title,
        string iconGlyph,
        out V4RoundedPanel card,
        out Label headingLabel)
    {
        card = new V4RoundedPanel
        {
            Dock = DockStyle.Fill,
            FillColor = FormalUiV4Tokens.Surface,
            BorderColor = FormalUiV4Tokens.DeckBorder,
            CornerRadius = FormalUiV4Tokens.CardRadius,
            DrawSoftShadow = true,
            Padding = new Padding(
                13,
                FormalUiV4Tokens.CardTopPadding,
                13,
                FormalUiV4Tokens.CardBottomPadding),
        };
        TableLayoutPanel content = new()
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            ColumnCount = 1,
            RowCount = 2,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
        };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        content.RowStyles.Add(new RowStyle(
            SizeType.Absolute,
            FormalUiV4Tokens.CardHeadingHeight));
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        TableLayoutPanel heading = new()
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            ColumnCount = 2,
            RowCount = 1,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
        };
        heading.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 30));
        heading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        heading.Controls.Add(CreateGlyphLabel(iconGlyph, 10.5f), 0, 0);
        headingLabel = new Label
        {
            Dock = DockStyle.Fill,
            Text = title,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = FormalUiV4Tokens.Ui(9.6f, FontStyle.Bold),
            ForeColor = FormalUiV4Tokens.Ink,
            Margin = Padding.Empty,
        };
        heading.Controls.Add(headingLabel, 1, 0);

        TableLayoutPanel body = new()
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            ColumnCount = 1,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
        };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        content.Controls.Add(heading, 0, 0);
        content.Controls.Add(body, 0, 1);
        card.Controls.Add(content);
        return body;
    }

    private static Control CreateMicrophoneLine(
        out V4MicDeviceSelector selector,
        out V4Toggle toggle,
        out V4MeterPlaceholder meter)
    {
        TableLayoutPanel row = CreateInputToggleMeterLine(
            "\uE720",
            out TableLayoutPanel inputArea,
            out meter);
        selector = new V4MicDeviceSelector("麦克风 (Realtek(R) Audio)")
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
        };
        toggle = new V4Toggle(true)
        {
            Name = "MicToggle",
            AccessibleName = "麦克风开关",
            Anchor = AnchorStyles.Right,
            Margin = Padding.Empty,
        };
        inputArea.Controls.Add(selector, 0, 0);
        inputArea.Controls.Add(toggle, 1, 0);
        return row;
    }

    private static Control CreateSystemAudioLine()
    {
        TableLayoutPanel row = CreateInputToggleMeterLine(
            "\uE767",
            out TableLayoutPanel inputArea,
            out _);
        inputArea.Controls.Add(CreateBodyText("系统声音", FontStyle.Regular), 0, 0);
        inputArea.Controls.Add(new V4Toggle(false)
        {
            Anchor = AnchorStyles.Right,
            Margin = Padding.Empty,
        }, 1, 0);
        return row;
    }

    private static TableLayoutPanel CreateInputToggleMeterLine(
        string icon,
        out TableLayoutPanel inputArea,
        out V4MeterPlaceholder meter)
    {
        TableLayoutPanel row = new()
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            ColumnCount = 2,
            RowCount = 1,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 30));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        row.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        row.Controls.Add(CreateGlyphLabel(icon, 10f), 0, 0);

        TableLayoutPanel stack = new()
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            ColumnCount = 1,
            RowCount = 2,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
        };
        stack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        stack.RowStyles.Add(new RowStyle(
            SizeType.Absolute,
            FormalUiV4Tokens.PreparationAudioInputHeight));
        stack.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        inputArea = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            ColumnCount = 2,
            RowCount = 1,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
        };
        inputArea.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        inputArea.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 46));
        inputArea.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        stack.Controls.Add(inputArea, 0, 0);
        meter = new V4MeterPlaceholder
        {
            Dock = DockStyle.Top,
            Margin = new Padding(0, 1, 0, 0),
        };
        stack.Controls.Add(meter, 0, 1);
        row.Controls.Add(stack, 1, 0);
        return row;
    }

    private static Control CreateToggleLine(string icon, string text, bool isOn)
    {
        TableLayoutPanel row = new()
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            ColumnCount = string.IsNullOrEmpty(icon) ? 2 : 3,
            RowCount = 1,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
        };
        if (!string.IsNullOrEmpty(icon))
        {
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 30));
            row.Controls.Add(CreateGlyphLabel(icon, 10f), 0, 0);
        }
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 46));
        int labelColumn = string.IsNullOrEmpty(icon) ? 0 : 1;
        row.Controls.Add(CreateBodyText(text, FontStyle.Regular), labelColumn, 0);
        row.Controls.Add(new V4Toggle(isOn)
        {
            Anchor = AnchorStyles.Right,
            Margin = Padding.Empty,
        }, labelColumn + 1, 0);
        return row;
    }

    private static Control CreateSegmentGroup(string caption, params V4StyledButton[] buttons)
    {
        TableLayoutPanel group = new()
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            ColumnCount = 1,
            RowCount = 2,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
        };
        group.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        group.RowStyles.Add(new RowStyle(SizeType.Absolute, 18));
        group.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        group.Controls.Add(CreateCaption(caption), 0, 0);

        TableLayoutPanel segments = new()
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            ColumnCount = buttons.Length,
            RowCount = 1,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
        };
        for (int index = 0; index < buttons.Length; index++)
        {
            segments.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / buttons.Length));
            buttons[index].Dock = DockStyle.Fill;
            buttons[index].Margin = new Padding(index == 0 ? 0 : 2, 0, index == buttons.Length - 1 ? 0 : 2, 0);
            segments.Controls.Add(buttons[index], index, 0);
        }
        group.Controls.Add(segments, 0, 1);
        return group;
    }

    private static V4StyledButton CreateChoice(
        string text,
        bool selected,
        string iconGlyph = "") => new()
    {
        Dock = DockStyle.Fill,
        Text = text,
        IconGlyph = iconGlyph,
        Selected = selected,
        Margin = Padding.Empty,
    };

    private static void WireExclusive(params V4StyledButton[] choices)
    {
        foreach (V4StyledButton choice in choices)
        {
            choice.Click += (_, _) =>
            {
                foreach (V4StyledButton candidate in choices)
                {
                    candidate.Selected = ReferenceEquals(candidate, choice);
                }
            };
        }
    }

    private static Label CreateGlyphLabel(string glyph, float size) => new()
    {
        Dock = DockStyle.Fill,
        Text = glyph,
        TextAlign = ContentAlignment.MiddleLeft,
        Font = FormalUiV4Tokens.Icon(size),
        ForeColor = FormalUiV4Tokens.InkMuted,
        Margin = Padding.Empty,
    };

    private static Label CreateCaption(string text) => new()
    {
        Dock = DockStyle.Fill,
        Text = text,
        TextAlign = ContentAlignment.MiddleLeft,
        Font = FormalUiV4Tokens.Ui(7.6f),
        ForeColor = FormalUiV4Tokens.InkMuted,
        Margin = Padding.Empty,
    };

    private static Label CreateBodyText(string text, FontStyle style) => new()
    {
        Dock = DockStyle.Fill,
        Text = text,
        TextAlign = ContentAlignment.MiddleLeft,
        Font = FormalUiV4Tokens.Ui(8f, style),
        ForeColor = FormalUiV4Tokens.Ink,
        Margin = Padding.Empty,
    };

    private static V4ChromeButton CreateChromeButton(string text, int width, float size)
    {
        V4ChromeButton button = new()
        {
            Dock = DockStyle.Fill,
            Width = width,
            Text = text,
            ForeColor = FormalUiV4Tokens.InkMuted,
            Font = text == "设置"
                ? FormalUiV4Tokens.Ui(8.6f, FontStyle.Bold)
                : FormalUiV4Tokens.Icon(size),
            Margin = Padding.Empty,
            TabStop = false,
            Cursor = Cursors.Hand,
        };
        return button;
    }

    private sealed class RecordingDeckView
    {
        private readonly Label _headingLabel;
        private readonly Control _idleContent;
        private readonly Control _activeContent;
        private readonly Control _completedContent;
        private readonly Label _statusLabel;
        private readonly FormalUiStableTimerSurface _timerSurface;
        private readonly Label _completedTimerLabel;
        private FormalUiRecordingPresentationState? _visibleState;

        internal RecordingDeckView(
            Control root,
            Label headingLabel,
            Control idleContent,
            Control activeContent,
            Control completedContent,
            Label statusLabel,
            FormalUiStableTimerSurface timerSurface,
            Label completedTimerLabel,
            V4RecordButton startButton,
            V4StyledButton pauseResumeButton,
            V4StyledButton stopButton,
            V4StyledButton openFolderButton,
            V4StyledButton openVideoButton)
        {
            Root = root;
            _headingLabel = headingLabel;
            _idleContent = idleContent;
            _activeContent = activeContent;
            _completedContent = completedContent;
            _statusLabel = statusLabel;
            _timerSurface = timerSurface;
            _completedTimerLabel = completedTimerLabel;
            StartButton = startButton;
            PauseResumeButton = pauseResumeButton;
            StopButton = stopButton;
            OpenFolderButton = openFolderButton;
            OpenVideoButton = openVideoButton;
        }

        internal Control Root { get; }
        internal V4RecordButton StartButton { get; }
        internal V4StyledButton PauseResumeButton { get; }
        internal V4StyledButton StopButton { get; }
        internal V4StyledButton OpenFolderButton { get; }
        internal V4StyledButton OpenVideoButton { get; }
        internal Rectangle TimerBounds => _timerSurface.Bounds;
        internal int AtomicTimerPreparedFrameCount => _timerSurface.PreparedFrameCount;
        internal int AtomicTimerPaintCount => _timerSurface.PaintCount;
        internal int AtomicTimerBufferAllocationCount => _timerSurface.BufferAllocationCount;
        internal int AtomicTimerEraseBackgroundMessageCount =>
            _timerSurface.EraseBackgroundMessageCount;

        internal bool ShowState(
            FormalUiRecordingPresentationState state,
            string elapsedText)
        {
            bool isIdle = state == FormalUiRecordingPresentationState.Idle;
            bool isCompleted = state == FormalUiRecordingPresentationState.Completed;
            bool stateChanged = _visibleState != state;
            if (stateChanged)
            {
                _visibleState = state;
                _headingLabel.Text = isIdle
                    ? "保存 / 录制"
                    : isCompleted
                        ? "录制完成"
                        : "录制中";
                _idleContent.Visible = isIdle;
                _activeContent.Visible = !isIdle && !isCompleted;
                _completedContent.Visible = isCompleted;
                if (isIdle)
                {
                    _idleContent.BringToFront();
                }
                else if (isCompleted)
                {
                    _completedContent.BringToFront();
                }
                else
                {
                    bool isRecording =
                        state == FormalUiRecordingPresentationState.Recording;
                    string statusText = isRecording ? "● 正在录制" : "Ⅱ 已暂停";
                    Color stateColor = isRecording
                        ? FormalUiV4Tokens.AccentTop
                        : FormalUiV4Tokens.InkMuted;
                    _statusLabel.Text = statusText;
                    _statusLabel.AccessibleName = statusText;
                    _statusLabel.ForeColor = stateColor;
                    PauseResumeButton.Text = isRecording ? "暂停" : "继续";
                    PauseResumeButton.AccessibleName = PauseResumeButton.Text;
                    _activeContent.BringToFront();
                }
            }

            if (isIdle)
            {
                return false;
            }
            if (isCompleted)
            {
                if (string.Equals(
                    _completedTimerLabel.Text,
                    elapsedText,
                    StringComparison.Ordinal))
                {
                    return false;
                }
                _completedTimerLabel.Text = elapsedText;
                _completedTimerLabel.AccessibleName =
                    $"录制时长：{_completedTimerLabel.Text}";
                return true;
            }

            Color timerColor = state == FormalUiRecordingPresentationState.Recording
                ? FormalUiV4Tokens.AccentTop
                : FormalUiV4Tokens.InkMuted;
            return _timerSurface.SetFrame(elapsedText, timerColor);
        }
    }

    private sealed class WindowSelectorDeckView
    {
        internal WindowSelectorDeckView(
            Control card,
            V4StyledButton fullScreenButton,
            V4StyledButton windowButton)
        {
            Card = card;
            FullScreenButton = fullScreenButton;
            WindowButton = windowButton;
        }

        internal Control Card { get; }
        internal V4StyledButton FullScreenButton { get; }
        internal V4StyledButton WindowButton { get; }
    }

    private sealed class MicSelectorDeckView
    {
        internal MicSelectorDeckView(
            Control card,
            V4MicDeviceSelector selector,
            V4Toggle toggle,
            V4MeterPlaceholder meter)
        {
            Card = card;
            Selector = selector;
            Toggle = toggle;
            Meter = meter;
        }

        internal Control Card { get; }
        internal V4MicDeviceSelector Selector { get; }
        internal V4Toggle Toggle { get; }
        internal V4MeterPlaceholder Meter { get; }
    }

    private sealed class BackgroundSelectorDeckView
    {
        internal BackgroundSelectorDeckView(Control card, V4SelectBox selector)
        {
            Card = card;
            Selector = selector;
        }

        internal Control Card { get; }
        internal V4SelectBox Selector { get; }
    }

    private void BeginWindowDrag(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left || WindowState == FormWindowState.Maximized)
        {
            return;
        }
        ReleaseCapture();
        SendMessage(Handle, WmNcLButtonDown, (IntPtr)HtCaption, IntPtr.Zero);
    }

    private void BeginWindowResize(int hitTest, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left || WindowState != FormWindowState.Normal)
        {
            return;
        }
        ReleaseCapture();
        SendMessage(Handle, WmNcLButtonDown, (IntPtr)hitTest, IntPtr.Zero);
    }

    private void ToggleMaximize()
    {
        WindowState = WindowState == FormWindowState.Maximized
            ? FormWindowState.Normal
            : FormWindowState.Maximized;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(
        IntPtr window,
        int message,
        IntPtr parameter,
        IntPtr data);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RedrawWindow(
        IntPtr window,
        IntPtr updateRectangle,
        IntPtr updateRegion,
        uint flags);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        IntPtr window,
        int attribute,
        ref int value,
        int valueSize);
}

internal sealed class FormalUiHomeRevealSurface : Control
{
    private const long MaximumSnapshotPixels = 24_000_000;
    private Bitmap? _snapshot;

    internal FormalUiHomeRevealSurface()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.Opaque |
            ControlStyles.UserPaint,
            true);
        SetStyle(
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.SupportsTransparentBackColor,
            false);
        BackColor = FormalUiV4Tokens.ShellBottom;
        TabStop = false;
        Visible = false;
    }

    internal bool HasSnapshot => _snapshot is not null;

    internal bool TryCapture(Control target)
    {
        Bitmap? snapshot = null;
        try
        {
            Size size = target.ClientSize;
            if (size.Width < 1 || size.Height < 1 ||
                (long)size.Width * size.Height > MaximumSnapshotPixels)
            {
                return false;
            }

            snapshot = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppPArgb);
            using (Graphics graphics = Graphics.FromImage(snapshot))
            {
                graphics.Clear(BackColor);
            }
            target.DrawToBitmap(snapshot, new Rectangle(Point.Empty, size));

            Bitmap? previous = _snapshot;
            _snapshot = snapshot;
            snapshot = null;
            previous?.Dispose();
            Invalidate();
            return true;
        }
        catch (Exception exception) when (
            exception is ArgumentException or ExternalException or InvalidOperationException or OutOfMemoryException)
        {
            return false;
        }
        finally
        {
            snapshot?.Dispose();
        }
    }

    internal void ClearSnapshot()
    {
        Bitmap? snapshot = _snapshot;
        _snapshot = null;
        snapshot?.Dispose();
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Bitmap? snapshot = _snapshot;
        if (snapshot is null)
        {
            e.Graphics.Clear(BackColor);
            return;
        }

        e.Graphics.CompositingQuality = CompositingQuality.HighSpeed;
        e.Graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
        e.Graphics.PixelOffsetMode = PixelOffsetMode.Half;
        e.Graphics.SmoothingMode = SmoothingMode.None;
        e.Graphics.DrawImage(
            snapshot,
            ClientRectangle,
            0,
            0,
            snapshot.Width,
            snapshot.Height,
            GraphicsUnit.Pixel);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            ClearSnapshot();
        }
        base.Dispose(disposing);
    }
}

internal sealed class V4TitleChromeHost : TableLayoutPanel
{
    private const int WmNcHitTest = 0x0084;
    private const int HtTransparent = -1;

    internal int TopResizePassThroughLogicalPixels { get; set; }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == WmNcHitTest &&
            TopResizePassThroughLogicalPixels > 0 &&
            FindForm()?.WindowState == FormWindowState.Normal)
        {
            int passThroughHeight = Math.Max(
                1,
                (int)Math.Round(
                    TopResizePassThroughLogicalPixels * DeviceDpi / 96f));
            Point cursor = PointToClient(Cursor.Position);
            if (cursor.Y >= 0 && cursor.Y < passThroughHeight)
            {
                message.Result = (IntPtr)HtTransparent;
                return;
            }
        }

        base.WndProc(ref message);
    }
}

internal sealed class V4ResizeGrip : Control
{
    internal V4ResizeGrip(Cursor cursor)
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.SupportsTransparentBackColor |
            ControlStyles.UserPaint,
            true);
        SetStyle(ControlStyles.Selectable, false);
        BackColor = Color.Transparent;
        Cursor = cursor;
        TabStop = false;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
    }
}
