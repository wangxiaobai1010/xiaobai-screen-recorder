using System.Drawing.Drawing2D;

namespace XbPreview.Host;

internal sealed class FormalUiSettingsView : Panel
{
    private const int RailWidthLogical = 188;
    private const string ResetButtonText = "恢复默认设置";
    private static readonly Color OperationGuidePaper = Color.FromArgb(0xED, 0xE5, 0xDA);
    private readonly V4ChromeButton _backButton;
    private readonly SettingsOutlineButton _resetButton;
    private readonly SettingsOutlineButton _privacyButton;
    private readonly V4Toggle _freeControlDeckToggle;
    private readonly System.Windows.Forms.Timer _resetFeedbackTimer;
    private readonly Image? _operationGuideImage;
    private SettingsContentView _activeContentView;

    internal FormalUiSettingsView()
    {
        Name = "FormalSettingsView";
        AccessibleName = "设置";
        AccessibleDescription = "旧版评审界面的常规、隐私与安全以及版本信息";
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        BackColor = FormalUiV4Tokens.Canvas;
        Margin = Padding.Empty;
        Padding = Padding.Empty;

        _backButton = new V4ChromeButton
        {
            Name = "SettingsBackButton",
            AccessibleName = "返回",
            Text = "←  返回",
            Font = FormalUiV4Tokens.Ui(9f),
            SizeSurfaceToText = true,
            TabStop = true,
        };
        _backButton.Click += (_, _) => BackRequested?.Invoke(this, EventArgs.Empty);

        _resetButton = new SettingsOutlineButton
        {
            Name = "ResetDefaultsButton",
            AccessibleName = ResetButtonText,
            AccessibleDescription = "直接恢复展示用默认设置，不写入持久化配置",
            Text = ResetButtonText,
            Font = FormalUiV4Tokens.Ui(8.5f, FontStyle.Bold),
            TabStop = true,
        };
        _resetButton.Click += (_, _) => ResetRequested?.Invoke(this, EventArgs.Empty);

        _privacyButton = new SettingsOutlineButton
        {
            Name = "PrivacySafetyButton",
            AccessibleName = "隐私与安全",
            AccessibleDescription = "打开隐私与安全内容页",
            Text = "隐私与安全",
            Font = FormalUiV4Tokens.Ui(8.5f, FontStyle.Bold),
            TabStop = true,
        };
        _privacyButton.Click += (_, _) => SetActiveContentView(
            _activeContentView == SettingsContentView.Privacy
                ? SettingsContentView.OperationGuide
                : SettingsContentView.Privacy);

        _freeControlDeckToggle = new V4Toggle(isOn: true)
        {
            Name = "FreeControlDeckToggle",
            AccessibleName = "自由控制舱",
            BackColor = FormalUiV4Tokens.Surface,
            TabStop = true,
        };
        UpdateControlDeckAccessibility();
        _freeControlDeckToggle.Click += (_, _) =>
        {
            UpdateControlDeckAccessibility();
            Invalidate();
        };

        _operationGuideImage = LoadOperationGuideImage();
        Controls.Add(_backButton);
        Controls.Add(_resetButton);
        Controls.Add(_privacyButton);
        Controls.Add(_freeControlDeckToggle);

        _resetFeedbackTimer = new System.Windows.Forms.Timer { Interval = 1600 };
        _resetFeedbackTimer.Tick += (_, _) =>
        {
            _resetFeedbackTimer.Stop();
            _resetButton.Text = ResetButtonText;
            _resetButton.AccessibleName = ResetButtonText;
            _resetButton.Invalidate();
        };
    }

    internal event EventHandler? BackRequested;
    internal event EventHandler? ResetRequested;
    internal bool ResetDefaultsRequested { get; private set; }
    internal string ActiveContentName =>
        _activeContentView == SettingsContentView.Privacy ? "隐私页" : "默认图页";
    internal void FocusBackButton() => _backButton.Focus();

    internal void ShowDefaultContent()
    {
        SetActiveContentView(SettingsContentView.OperationGuide);
    }

    internal void ApplyPresentationDefaults()
    {
        ResetDefaultsRequested = true;
        _freeControlDeckToggle.IsOn = true;
        UpdateControlDeckAccessibility();
        SetActiveContentView(SettingsContentView.OperationGuide);

        _resetButton.Text = "已恢复默认设置";
        _resetButton.AccessibleName = "已恢复默认设置";
        _resetFeedbackTimer.Stop();
        _resetFeedbackTimer.Start();
        _resetButton.Invalidate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _resetFeedbackTimer.Dispose();
            _operationGuideImage?.Dispose();
        }
        base.Dispose(disposing);
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        int pad = ScaleLogical(18);
        _backButton.Bounds = new Rectangle(
            pad - ScaleLogical(8), ScaleLogical(7), ScaleLogical(86), ScaleLogical(34));

        Rectangle content = GetContentBounds();
        Rectangle rail = new(content.Left, content.Top, GetRailWidth(content.Width), content.Height);
        _resetButton.Bounds = new Rectangle(
            rail.Left + ScaleLogical(16),
            rail.Top + ScaleLogical(61),
            rail.Width - ScaleLogical(32),
            ScaleLogical(38));
        _privacyButton.Bounds = new Rectangle(
            rail.Left + ScaleLogical(16),
            rail.Top + ScaleLogical(153),
            rail.Width - ScaleLogical(32),
            ScaleLogical(38));
        _freeControlDeckToggle.Bounds = new Rectangle(
            rail.Right - ScaleLogical(16) - ScaleLogical(40),
            rail.Top + ScaleLogical(315),
            ScaleLogical(40),
            ScaleLogical(22));
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        if (FormalUiV4ResizeBodyPaintGate.IsFrozen)
        {
            FormalUiV4ResizeProbe.RecordSuppressedBodyPaint();
            return;
        }

        using LinearGradientBrush background = new(
            ClientRectangle,
            FormalUiV4Tokens.ShellTop,
            FormalUiV4Tokens.ShellBottom,
            LinearGradientMode.Vertical);
        e.Graphics.FillRectangle(background, ClientRectangle);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (FormalUiV4ResizeBodyPaintGate.IsFrozen)
        {
            FormalUiV4ResizeProbe.RecordSuppressedBodyPaint();
            return;
        }

        base.OnPaint(e);
        FormalUiV4Drawing.Prepare(e.Graphics);
        Rectangle content = GetContentBounds();
        if (content.Width < ScaleLogical(420) || content.Height < ScaleLogical(360))
        {
            return;
        }

        int gap = ScaleLogical(12);
        Rectangle rail = new(content.Left, content.Top, GetRailWidth(content.Width), content.Height);
        Rectangle contentHost = new(
            rail.Right + gap,
            content.Top,
            Math.Max(1, content.Right - rail.Right - gap),
            content.Height);

        DrawSettingsRail(e.Graphics, rail);
        if (_activeContentView == SettingsContentView.Privacy)
        {
            DrawPrivacySurface(e.Graphics, contentHost);
        }
        else
        {
            DrawOperationGuideSurface(e.Graphics, contentHost);
        }
    }

    private void DrawSettingsRail(Graphics graphics, Rectangle bounds)
    {
        DrawSurface(graphics, bounds, FormalUiV4Tokens.Surface);
        int x = bounds.Left + ScaleLogical(16);
        int width = bounds.Width - ScaleLogical(32);
        int right = bounds.Right - ScaleLogical(16);
        using Font eyebrow = FormalUiV4Tokens.Ui(7.3f, FontStyle.Bold);
        using Font title = FormalUiV4Tokens.Ui(10f, FontStyle.Bold);
        using Font body = FormalUiV4Tokens.Ui(8.1f);
        using Font controlLabel = FormalUiV4Tokens.Ui(8.6f, FontStyle.Bold);
        using Font version = FormalUiV4Tokens.Ui(8.4f, FontStyle.Bold);
        using Pen divider = new(FormalUiV4Tokens.ControlBorder);

        DrawText(graphics, "GENERAL", eyebrow,
            new Point(x, bounds.Top + ScaleLogical(16)), FormalUiV4Tokens.Accent);
        DrawText(graphics, "常规", title,
            new Point(x, bounds.Top + ScaleLogical(36)), FormalUiV4Tokens.Ink);

        int privacyDividerY = bounds.Top + ScaleLogical(114);
        graphics.DrawLine(divider, x, privacyDividerY, right, privacyDividerY);
        DrawText(graphics, "PRIVACY & SAFETY", eyebrow,
            new Point(x, bounds.Top + ScaleLogical(130)), FormalUiV4Tokens.InkMuted);

        int controlDividerY = bounds.Top + ScaleLogical(208);
        graphics.DrawLine(divider, x, controlDividerY, right, controlDividerY);
        DrawText(graphics, "CONTROL DECK", eyebrow,
            new Point(x, bounds.Top + ScaleLogical(224)), FormalUiV4Tokens.Accent);
        DrawText(graphics, "自由控制舱", title,
            new Point(x, bounds.Top + ScaleLogical(244)), FormalUiV4Tokens.Ink);
        TextRenderer.DrawText(
            graphics,
            "关闭后将锁定主机，无法将控制舱从主界面拖出。",
            body,
            new Rectangle(x, bounds.Top + ScaleLogical(270), width, ScaleLogical(38)),
            FormalUiV4Tokens.InkMuted,
            TextFormatFlags.WordBreak | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
        DrawText(graphics, "允许拖出", controlLabel,
            new Point(x, bounds.Top + ScaleLogical(318)), FormalUiV4Tokens.Ink);
        DrawText(graphics, _freeControlDeckToggle.IsOn ? "ON" : "OFF", eyebrow,
            new Point(bounds.Right - ScaleLogical(82), bounds.Top + ScaleLogical(321)),
            _freeControlDeckToggle.IsOn ? FormalUiV4Tokens.Accent : FormalUiV4Tokens.InkMuted);

        int aboutTop = bounds.Bottom - ScaleLogical(145);
        graphics.DrawLine(divider, x, aboutTop, right, aboutTop);
        DrawText(graphics, "ABOUT", eyebrow,
            new Point(x, aboutTop + ScaleLogical(15)), FormalUiV4Tokens.InkMuted);
        DrawText(graphics, "关于旧版评审界面", title,
            new Point(x, aboutTop + ScaleLogical(36)), FormalUiV4Tokens.Ink);
        DrawText(graphics, "v1.0", version,
            new Point(x, aboutTop + ScaleLogical(65)), FormalUiV4Tokens.Accent);
        TextRenderer.DrawText(
            graphics,
            "为 Windows 打造的成片导向录屏工具。",
            body,
            new Rectangle(x, aboutTop + ScaleLogical(91), width, ScaleLogical(38)),
            FormalUiV4Tokens.InkMuted,
            TextFormatFlags.WordBreak | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
    }

    private void DrawOperationGuideSurface(Graphics graphics, Rectangle bounds)
    {
        RectangleF face = new(bounds.X, bounds.Y, bounds.Width - 1, bounds.Height - 1);
        using GraphicsPath hostPath = FormalUiV4Drawing.RoundedRectangle(
            face, ScaleLogical(FormalUiV4Tokens.CardRadius));
        using SolidBrush paper = new(OperationGuidePaper);
        using Pen border = new(FormalUiV4Tokens.DeckBorder);
        GraphicsState state = graphics.Save();
        graphics.SetClip(hostPath);
        graphics.FillPath(paper, hostPath);

        int imageInset = ScaleLogical(7);
        Rectangle inner = new(
            bounds.Left + imageInset,
            bounds.Top + imageInset,
            bounds.Width - imageInset * 2,
            bounds.Height - imageInset * 2);
        if (_operationGuideImage is null || inner.Width <= 0 || inner.Height <= 0)
        {
            using Font body = FormalUiV4Tokens.Ui(8.4f);
            TextRenderer.DrawText(
                graphics,
                "操作说明图未找到",
                body,
                inner,
                FormalUiV4Tokens.InkMuted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                    TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
        }
        else
        {
            double scale = Math.Min(
                inner.Width / (double)_operationGuideImage.Width,
                inner.Height / (double)_operationGuideImage.Height);
            int width = Math.Max(1, (int)Math.Round(_operationGuideImage.Width * scale));
            int height = Math.Max(1, (int)Math.Round(_operationGuideImage.Height * scale));
            Rectangle destination = new(
                inner.Left + (inner.Width - width) / 2,
                inner.Top + (inner.Height - height) / 2,
                width,
                height);
            graphics.DrawImage(
                _operationGuideImage,
                destination,
                0,
                0,
                _operationGuideImage.Width,
                _operationGuideImage.Height,
                GraphicsUnit.Pixel);
        }

        graphics.Restore(state);
        graphics.DrawPath(border, hostPath);
    }

    private void DrawPrivacySurface(Graphics graphics, Rectangle bounds)
    {
        DrawSurface(graphics, bounds, FormalUiV4Tokens.Surface);
        int inset = ScaleLogical(20);
        int x = bounds.Left + inset;
        int width = bounds.Width - inset * 2;
        using Font eyebrow = FormalUiV4Tokens.Ui(7.3f, FontStyle.Bold);
        using Font title = FormalUiV4Tokens.Ui(14.2f, FontStyle.Bold);
        using Font subtitle = FormalUiV4Tokens.Ui(8.4f);
        using Font sectionTitle = FormalUiV4Tokens.Ui(9f, FontStyle.Bold);
        using Font body = FormalUiV4Tokens.Ui(8.2f);

        DrawText(graphics, "PRIVACY & SAFETY", eyebrow,
            new Point(x, bounds.Top + ScaleLogical(20)), FormalUiV4Tokens.Accent);
        DrawText(graphics, "隐私与安全", title,
            new Point(x, bounds.Top + ScaleLogical(42)), FormalUiV4Tokens.Ink);
        TextRenderer.DrawText(
            graphics,
            "了解录制、保存与数据处理边界。",
            subtitle,
            new Rectangle(x, bounds.Top + ScaleLogical(75), width, ScaleLogical(22)),
            FormalUiV4Tokens.InkMuted,
            TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);

        int groupsTop = bounds.Top + ScaleLogical(116);
        int available = Math.Max(4, bounds.Bottom - ScaleLogical(15) - groupsTop);
        int recordingHeight = (int)Math.Round(available * .29);
        int localHeight = (int)Math.Round(available * .23);
        int sensitiveHeight = (int)Math.Round(available * .23);
        int aiHeight = available - recordingHeight - localHeight - sensitiveHeight;
        PrivacySection[] sections =
        {
            new(
                "录制内容",
                "旧版评审界面仅记录你主动选择的屏幕或窗口，以及已开启的系统声音、麦克风声音和鼠标显示。镜头跟随仅使用鼠标位置判断画面重点。",
                recordingHeight),
            new(
                "本地保存",
                "录制文件保存在你选择的本地文件夹。停止录制后，旧版评审界面会完成文件验证与安全保存。",
                localHeight),
            new(
                "敏感内容",
                "录制前请确认画面中不包含不希望进入成片的聊天记录、账号信息、通知或其他私人内容。",
                sensitiveHeight),
            new(
                "AI 与数据",
                "当前录制与保存均在本地完成。Formal UI 不会自动上传录制内容，也未接入云端 AI 分析。",
                aiHeight),
        };

        int y = groupsTop;
        for (int index = 0; index < sections.Length; index++)
        {
            PrivacySection section = sections[index];
            DrawPrivacySection(
                graphics,
                new Rectangle(x, y, width, section.Height),
                section.Title,
                section.Body,
                sectionTitle,
                body,
                index < sections.Length - 1);
            y += section.Height;
        }
    }

    private static void DrawPrivacySection(
        Graphics graphics,
        Rectangle bounds,
        string title,
        string body,
        Font titleFont,
        Font bodyFont,
        bool drawDivider)
    {
        TextRenderer.DrawText(
            graphics,
            title,
            titleFont,
            new Rectangle(bounds.Left, bounds.Top, bounds.Width, 24),
            FormalUiV4Tokens.Ink,
            TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
        TextRenderer.DrawText(
            graphics,
            body,
            bodyFont,
            new Rectangle(bounds.Left, bounds.Top + 25, bounds.Width, Math.Max(1, bounds.Height - 33)),
            FormalUiV4Tokens.InkMuted,
            TextFormatFlags.WordBreak | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
        if (drawDivider)
        {
            using Pen divider = new(FormalUiV4Tokens.ControlBorder);
            graphics.DrawLine(divider, bounds.Left, bounds.Bottom - 7, bounds.Right, bounds.Bottom - 7);
        }
    }

    private void SetActiveContentView(SettingsContentView view)
    {
        _activeContentView = view;
        bool privacyVisible = view == SettingsContentView.Privacy;
        _privacyButton.Selected = privacyVisible;
        _privacyButton.AccessibleDescription = privacyVisible
            ? "关闭隐私与安全内容页并返回默认图页"
            : "打开隐私与安全内容页";
        AccessibleDescription = privacyVisible
            ? "Settings；当前显示隐私与安全内容页"
            : "Settings；当前显示操作说明图";
        Invalidate();
    }

    private void UpdateControlDeckAccessibility()
    {
        _freeControlDeckToggle.AccessibleDescription = _freeControlDeckToggle.IsOn
            ? "当前为开启；点击后关闭自由控制舱"
            : "当前为关闭；点击后开启自由控制舱";
    }

    private static Image? LoadOperationGuideImage()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "assets", "操作说明.png");
        if (!File.Exists(path))
        {
            return null;
        }

        using Image source = Image.FromFile(path);
        return new Bitmap(source);
    }

    private Rectangle GetContentBounds()
    {
        int horizontal = ScaleLogical(18);
        int top = ScaleLogical(51);
        int bottom = ScaleLogical(15);
        return new Rectangle(
            horizontal,
            top,
            Math.Max(1, ClientSize.Width - horizontal * 2),
            Math.Max(1, ClientSize.Height - top - bottom));
    }

    private int GetRailWidth(int contentWidth) =>
        Math.Min(ScaleLogical(RailWidthLogical), Math.Max(ScaleLogical(156), contentWidth / 3));

    private int ScaleLogical(int value) =>
        Math.Max(1, (int)Math.Round(value * DeviceDpi / 96f));

    private static void DrawText(Graphics graphics, string text, Font font, Point location, Color color) =>
        TextRenderer.DrawText(
            graphics,
            text,
            font,
            location,
            color,
            TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);

    private void DrawSurface(Graphics graphics, Rectangle bounds, Color fillColor)
    {
        RectangleF face = new(bounds.X, bounds.Y, bounds.Width - 1, bounds.Height - 1);
        using GraphicsPath path = FormalUiV4Drawing.RoundedRectangle(
            face, ScaleLogical(FormalUiV4Tokens.CardRadius));
        using SolidBrush fill = new(fillColor);
        using Pen border = new(FormalUiV4Tokens.DeckBorder);
        graphics.FillPath(fill, path);
        graphics.DrawPath(border, path);
    }

    private enum SettingsContentView
    {
        OperationGuide,
        Privacy,
    }

    private readonly record struct PrivacySection(string Title, string Body, int Height);
}

internal sealed class SettingsOutlineButton : Control
{
    private bool _hovered;
    private bool _pressed;
    private bool _selected;

    internal SettingsOutlineButton()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.Selectable |
            ControlStyles.UserPaint |
            ControlStyles.SupportsTransparentBackColor,
            true);
        BackColor = Color.Transparent;
        ForeColor = FormalUiV4Tokens.Ink;
        Cursor = Cursors.Hand;
        TabStop = true;
        AccessibleRole = AccessibleRole.PushButton;
    }

    internal bool Selected
    {
        get => _selected;
        set
        {
            if (_selected == value)
            {
                return;
            }

            _selected = value;
            Invalidate();
        }
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        _hovered = true;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hovered = false;
        _pressed = false;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Left)
        {
            _pressed = true;
            Focus();
            Invalidate();
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        _pressed = false;
        Invalidate();
    }

    protected override void OnMouseCaptureChanged(EventArgs e)
    {
        base.OnMouseCaptureChanged(e);
        _pressed = false;
        Invalidate();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (Enabled && e.KeyCode is Keys.Enter or Keys.Space)
        {
            OnClick(EventArgs.Empty);
            e.Handled = true;
        }
    }

    protected override void OnGotFocus(EventArgs e)
    {
        base.OnGotFocus(e);
        Invalidate();
    }

    protected override void OnLostFocus(EventArgs e)
    {
        base.OnLostFocus(e);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        FormalUiV4Drawing.Prepare(e.Graphics);
        if (Width < 4 || Height < 4)
        {
            return;
        }

        RectangleF face = new(1.5f, _pressed ? 2.5f : 1.5f, Width - 3f, Height - 3.5f);
        Color fillColor = Selected
            ? Color.FromArgb(0xFA, 0xE8, 0xE2)
            : _pressed
                ? Color.FromArgb(0xF6, 0xEC, 0xE7)
                : _hovered
                    ? Color.FromArgb(0xFF, 0xFD, 0xFA)
                    : Color.FromArgb(0xFD, 0xFA, 0xF6);
        Color borderColor = Selected || Focused
            ? FormalUiV4Tokens.Accent
            : _hovered
                ? Color.FromArgb(0xDD, 0x82, 0x73)
                : Color.FromArgb(0xD8, 0xA4, 0x9A);
        Color textColor = Selected ? FormalUiV4Tokens.SelectedText : FormalUiV4Tokens.Ink;

        using GraphicsPath path = FormalUiV4Drawing.RoundedRectangle(face, 10f);
        using SolidBrush fill = new(fillColor);
        using Pen border = new(borderColor, Focused ? 1.5f : 1f);
        e.Graphics.FillPath(fill, path);
        e.Graphics.DrawPath(border, path);
        TextRenderer.DrawText(
            e.Graphics,
            Text,
            Font,
            Rectangle.Round(face),
            textColor,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
    }
}
