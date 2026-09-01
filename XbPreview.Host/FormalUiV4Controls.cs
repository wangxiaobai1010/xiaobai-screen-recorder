using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace XbPreview.Host;

internal static class FormalUiV4Tokens
{
    internal static readonly Color ShellTop = Color.FromArgb(0xFB, 0xF9, 0xF6);
    internal static readonly Color ShellBottom = Color.FromArgb(0xF2, 0xEE, 0xE8);
    internal static readonly Color Canvas = ShellTop;
    internal static readonly Color Surface = Color.FromArgb(0xFC, 0xFA, 0xF7);
    internal static readonly Color SurfaceRaised = Color.FromArgb(0xFF, 0xFE, 0xFC);
    internal static readonly Color SurfaceMuted = Color.FromArgb(0xF5, 0xF1, 0xEB);
    internal static readonly Color SurfaceBottom = Color.FromArgb(0xF4, 0xF0, 0xEA);
    internal static readonly Color Ink = Color.FromArgb(0x24, 0x23, 0x21);
    internal static readonly Color InkMuted = Color.FromArgb(0x74, 0x70, 0x6A);
    internal static readonly Color Border = Color.FromArgb(0xD5, 0xCE, 0xC5);
    internal static readonly Color DeckBorder = Color.FromArgb(0xD9, 0xD2, 0xC9);
    internal static readonly Color ControlBorder = Color.FromArgb(0xDD, 0xD6, 0xCE);
    internal static readonly Color ControlBottom = Color.FromArgb(0xF2, 0xED, 0xE7);
    internal static readonly Color SelectedFill = Color.FromArgb(0xF7, 0xDF, 0xD9);
    internal static readonly Color SelectedBorder = Color.FromArgb(0xED, 0x92, 0x85);
    internal static readonly Color SelectedText = Color.FromArgb(0xE0, 0x35, 0x1F);
    internal static readonly Color Accent = Color.FromArgb(0xE6, 0x49, 0x32);
    internal static readonly Color AccentTop = Color.FromArgb(0xEA, 0x50, 0x3A);
    internal static readonly Color AccentBottom = Color.FromArgb(0xD8, 0x3A, 0x28);
    internal static readonly Color AccentPressed = Color.FromArgb(0xC9, 0x31, 0x22);
    internal static readonly Color AccentBorder = Color.FromArgb(0xC7, 0x2F, 0x20);
    internal static readonly Color RecordHoverTop = Color.FromArgb(0xF4, 0x5B, 0x46);
    internal static readonly Color RecordHoverBottom = Color.FromArgb(0xE4, 0x44, 0x31);
    internal static readonly Color RecordHoverBorder = Color.FromArgb(0xD9, 0x3A, 0x28);
    internal static readonly Color RecordHoverDepth = Color.FromArgb(0xBC, 0x34, 0x26);
    // Microsoft Skill Recorder v0.5.0, staged src/App.css blob
    // b13256a4a821822226b5cfe02f93fae0aa0bd94f. Keep these flat colors exact.
    internal static readonly Color SkillRecorderSecondary = Color.FromArgb(0xF3, 0xF1, 0xED); // hsl(40, 19%, 94%)
    internal static readonly Color SkillRecorderSecondaryHover = Color.White; // filter: brightness(1.12)
    internal static readonly Color SkillRecorderText = Color.FromArgb(0x24, 0x24, 0x24); // hsl(0, 0%, 14%)
    internal static readonly Color SkillRecorderTextHover = Color.FromArgb(0x28, 0x28, 0x28); // filter: brightness(1.12)
    internal static readonly Color SkillRecorderPrimary = Color.FromArgb(0xE0, 0x35, 0x1F); // hsl(7, 76%, 50%)
    internal static readonly Color SkillRecorderPrimaryHover = Color.FromArgb(0xC1, 0x2E, 0x1A); // hsl(7, 76%, 43%)
    internal static readonly Color SkillRecorderDanger = Color.FromArgb(0xE8, 0x2C, 0x17); // hsl(6, 82%, 50%)
    internal static readonly Color SkillRecorderDangerHover = Color.FromArgb(0xF6, 0x2F, 0x18); // filter: brightness(1.06)
    internal static readonly Color SkillRecorderFocus = SkillRecorderPrimary;
    internal static readonly Color ToggleOff = Color.FromArgb(0xD8, 0xD3, 0xCD);
    internal static readonly Color MonitorTop = Color.FromArgb(0x2D, 0x2C, 0x2A);
    internal static readonly Color MonitorBottom = Color.FromArgb(0x20, 0x1F, 0x1E);
    internal static readonly Color MonitorBorder = Color.FromArgb(0x4D, 0x4A, 0x46);
    internal static readonly Color DisplayTop = Color.FromArgb(0x1D, 0x1C, 0x1B);
    internal static readonly Color DisplayBottom = Color.FromArgb(0x17, 0x17, 0x16);

    internal const int TitleBarHeight = 42;
    internal const int OuterPadding = 16;
    internal const int CardGap = 10;
    internal const int ShellRadius = 22;
    internal const int PreviewRadius = 17;
    internal const int CardRadius = 13;
    internal const int ControlRadius = 9;
    internal const int CardTopPadding = 8;
    internal const int CardBottomPadding = 10;
    internal const int CardHeadingHeight = 25;
    internal const int PreparationTargetHeight = 52;
    internal const int PreparationCursorHeight = 22;
    internal const int PreparationAudioHeight = 48;
    internal const int PreparationAudioInputHeight = 35;
    internal const int PreparationBodyRequiredHeight =
        PreparationTargetHeight +
        PreparationCursorHeight +
        PreparationAudioHeight * 2;
    internal const int PreparationCardRequiredHeight =
        CardTopPadding +
        CardHeadingHeight +
        PreparationBodyRequiredHeight +
        CardBottomPadding;
    internal const int ConsoleBottomMargin = 14;
    internal const int ConsoleHeight =
        PreparationCardRequiredHeight + ConsoleBottomMargin;
    internal const int ConsoleUsableHeight = PreparationCardRequiredHeight;

    internal static Font Ui(float size, FontStyle style = FontStyle.Regular) =>
        new("Microsoft YaHei UI", size, style, GraphicsUnit.Point);

    internal static Font Icon(float size) =>
        new("Segoe Fluent Icons", size, FontStyle.Regular, GraphicsUnit.Point);
}

internal enum V4ButtonColorScheme
{
    LegacyReview,
    SkillRecorderSecondary,
    SkillRecorderPrimary,
    SkillRecorderDanger,
}

internal static class FormalUiV4Drawing
{
    internal static void Prepare(Graphics graphics)
    {
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.CompositingQuality = CompositingQuality.HighQuality;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
    }

    internal static GraphicsPath RoundedRectangle(RectangleF bounds, float radius)
    {
        GraphicsPath path = new();
        if (bounds.Width <= 0f || bounds.Height <= 0f)
        {
            return path;
        }

        float clampedRadius = Math.Clamp(
            radius,
            .5f,
            Math.Min(bounds.Width, bounds.Height) / 2f);
        float diameter = clampedRadius * 2f;
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    internal static Color Blend(Color first, Color second, float amount)
    {
        amount = Math.Clamp(amount, 0f, 1f);
        return Color.FromArgb(
            (int)(first.A + (second.A - first.A) * amount),
            (int)(first.R + (second.R - first.R) * amount),
            (int)(first.G + (second.G - first.G) * amount),
            (int)(first.B + (second.B - first.B) * amount));
    }

    internal static void FillVerticalGradient(
        Graphics graphics,
        GraphicsPath path,
        RectangleF bounds,
        Color top,
        Color bottom)
    {
        using LinearGradientBrush gradient = new(bounds, top, bottom, LinearGradientMode.Vertical);
        graphics.FillPath(gradient, path);
    }
}

internal sealed class V4ShellSurface : Panel
{
    private Color? _titleBandColor;

    internal V4ShellSurface()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.UserPaint,
            true);
        BackColor = FormalUiV4Tokens.ShellTop;
    }

    internal Color? TitleBandColor
    {
        get => _titleBandColor;
        set
        {
            if (_titleBandColor == value)
            {
                return;
            }

            _titleBandColor = value;
            Invalidate();
        }
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        FormalUiV4ResizeProbe.RecordShellPaint();
        FormalUiV4Drawing.Prepare(e.Graphics);
        if (Width < 1 || Height < 1)
        {
            return;
        }

        using LinearGradientBrush shell = new(
            ClientRectangle,
            FormalUiV4Tokens.ShellTop,
            FormalUiV4Tokens.ShellBottom,
            LinearGradientMode.Vertical);
        e.Graphics.FillRectangle(shell, ClientRectangle);

        if (TitleBandColor is Color titleBandColor)
        {
            int titleBandHeight = Math.Min(
                Height,
                Math.Max(1, (int)Math.Round(
                    FormalUiV4Tokens.TitleBarHeight * DeviceDpi / 96f)));
            using SolidBrush titleBand = new(titleBandColor);
            e.Graphics.FillRectangle(titleBand, 0, 0, Width, titleBandHeight);
        }

        using Pen topLight = new(Color.FromArgb(180, 255, 255, 255));
        e.Graphics.DrawLine(topLight, 18, 1, Math.Max(18, Width - 19), 1);
    }
}

internal sealed class V4RoundedPanel : Panel
{
    internal V4RoundedPanel()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.UserPaint |
            ControlStyles.SupportsTransparentBackColor,
            true);
        BackColor = Color.Transparent;
    }

    internal Color FillColor { get; set; } = FormalUiV4Tokens.Surface;
    internal Color BorderColor { get; set; } = FormalUiV4Tokens.Border;
    internal int CornerRadius { get; set; } = FormalUiV4Tokens.CardRadius;
    internal bool DrawSoftShadow { get; set; } = true;

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        if (FormalUiV4ResizeBodyPaintGate.IsFrozen)
        {
            FormalUiV4ResizeProbe.RecordSuppressedBodyPaint();
            return;
        }
        base.OnPaintBackground(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (FormalUiV4ResizeBodyPaintGate.IsFrozen)
        {
            FormalUiV4ResizeProbe.RecordSuppressedBodyPaint();
            return;
        }
        FormalUiV4ResizeProbe.RecordDeckPaint();
        base.OnPaint(e);
        FormalUiV4Drawing.Prepare(e.Graphics);
        if (Width < 4 || Height < 4)
        {
            return;
        }

        RectangleF face = new(1f, 1f, Width - 3f, Height - 4f);
        if (DrawSoftShadow)
        {
            RectangleF shadow = new(2f, 3f, Width - 4f, Height - 4f);
            using GraphicsPath shadowPath = FormalUiV4Drawing.RoundedRectangle(shadow, CornerRadius);
            using SolidBrush shadowBrush = new(Color.FromArgb(22, 74, 57, 43));
            e.Graphics.FillPath(shadowBrush, shadowPath);
        }

        using GraphicsPath facePath = FormalUiV4Drawing.RoundedRectangle(face, CornerRadius);
        using Pen border = new(BorderColor, 1f);
        FormalUiV4Drawing.FillVerticalGradient(
            e.Graphics,
            facePath,
            face,
            FillColor,
            FormalUiV4Tokens.SurfaceBottom);
        e.Graphics.DrawPath(border, facePath);

        RectangleF inner = new(2f, 2f, Width - 5f, Height - 6f);
        using GraphicsPath innerPath = FormalUiV4Drawing.RoundedRectangle(
            inner,
            Math.Max(2, CornerRadius - 1));
        GraphicsState state = e.Graphics.Save();
        e.Graphics.SetClip(new RectangleF(0, 0, Width, Math.Max(1, Height / 2f)));
        using Pen highlight = new(Color.FromArgb(165, 255, 255, 255), 1f);
        e.Graphics.DrawPath(highlight, innerPath);
        e.Graphics.Restore(state);
    }
}

internal sealed class V4StyledButton : Control
{
    private bool _hovered;
    private bool _pressed;
    private bool _selected;
    private bool _accent;
    private bool _dropDownExpanded;

    internal V4StyledButton()
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
        Font = FormalUiV4Tokens.Ui(8.4f);
        Cursor = Cursors.Hand;
        TabStop = false;
        Height = 34;
    }

    internal bool Selected
    {
        get => _selected;
        set
        {
            _selected = value;
            Invalidate();
        }
    }

    internal bool Accent
    {
        get => _accent;
        set
        {
            _accent = value;
            Invalidate();
        }
    }

    internal string IconGlyph { get; set; } = string.Empty;
    internal V4ButtonColorScheme ColorScheme { get; set; }
    internal bool ShowDropDown { get; set; }
    internal bool DropDownExpanded
    {
        get => _dropDownExpanded;
        set
        {
            if (_dropDownExpanded == value)
            {
                return;
            }

            _dropDownExpanded = value;
            Invalidate();
        }
    }
    internal int CornerRadius { get; set; } = FormalUiV4Tokens.ControlRadius;

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
            if (TabStop)
            {
                Focus();
            }
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

    protected override void OnEnabledChanged(EventArgs e)
    {
        base.OnEnabledChanged(e);
        Invalidate();
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        if (FormalUiV4ResizeBodyPaintGate.IsFrozen)
        {
            FormalUiV4ResizeProbe.RecordSuppressedBodyPaint();
            return;
        }
        base.OnPaintBackground(e);
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
        if (Width < 4 || Height < 4)
        {
            return;
        }

        if (ColorScheme != V4ButtonColorScheme.LegacyReview)
        {
            DrawSkillRecorderVariant(e.Graphics);
            return;
        }

        RectangleF face = new(1f, _pressed ? 2f : 1f, Width - 3f, Height - 4f);

        Color fillTop;
        Color fillBottom;
        Color border;
        Color foreground;
        if (!Enabled)
        {
            fillTop = Color.FromArgb(0xF2, 0xEF, 0xEA);
            fillBottom = Color.FromArgb(0xEA, 0xE6, 0xE0);
            border = Color.FromArgb(0xE2, 0xDC, 0xD5);
            foreground = Color.FromArgb(0xA9, 0xA4, 0x9E);
        }
        else if (Accent)
        {
            fillTop = _pressed ? FormalUiV4Tokens.AccentPressed : FormalUiV4Tokens.AccentTop;
            fillBottom = _pressed ? FormalUiV4Tokens.AccentPressed : FormalUiV4Tokens.AccentBottom;
            border = FormalUiV4Tokens.AccentBorder;
            foreground = Color.White;
        }
        else if (Selected)
        {
            fillTop = _pressed
                ? FormalUiV4Drawing.Blend(FormalUiV4Tokens.SelectedFill, FormalUiV4Tokens.SelectedBorder, .15f)
                : FormalUiV4Tokens.SelectedFill;
            fillBottom = FormalUiV4Drawing.Blend(fillTop, FormalUiV4Tokens.SelectedBorder, .08f);
            border = FormalUiV4Tokens.SelectedBorder;
            foreground = FormalUiV4Tokens.SelectedText;
        }
        else
        {
            fillTop = _hovered ? FormalUiV4Tokens.SurfaceRaised : Color.FromArgb(0xF8, 0xF5, 0xF0);
            fillBottom = _hovered ? Color.FromArgb(0xF8, 0xF4, 0xEE) : FormalUiV4Tokens.ControlBottom;
            if (_pressed)
            {
                fillTop = Color.FromArgb(0xEE, 0xE9, 0xE2);
                fillBottom = Color.FromArgb(0xE8, 0xE2, 0xDB);
            }
            border = _hovered
                ? Color.FromArgb(0xD4, 0xCC, 0xC4)
                : FormalUiV4Tokens.ControlBorder;
            foreground = FormalUiV4Tokens.Ink;
        }

        RectangleF depthRect = new(face.Left, face.Top + 2f, face.Width, face.Height);
        using GraphicsPath depthPath = FormalUiV4Drawing.RoundedRectangle(depthRect, CornerRadius);
        using SolidBrush depth = new(Accent
            ? Color.FromArgb(64, 139, 38, 28)
            : Color.FromArgb(22, 81, 63, 48));
        e.Graphics.FillPath(depth, depthPath);

        using GraphicsPath path = FormalUiV4Drawing.RoundedRectangle(face, CornerRadius);
        using Pen pen = new(border, 1f);
        FormalUiV4Drawing.FillVerticalGradient(e.Graphics, path, face, fillTop, fillBottom);
        e.Graphics.DrawPath(pen, path);

        RectangleF inner = new(face.X + 1f, face.Y + 1f, face.Width - 2f, face.Height - 2f);
        using GraphicsPath innerPath = FormalUiV4Drawing.RoundedRectangle(
            inner,
            Math.Max(2, CornerRadius - 1));
        GraphicsState state = e.Graphics.Save();
        e.Graphics.SetClip(new RectangleF(0, 0, Width, Math.Max(1, face.Top + face.Height / 2f)));
        using Pen topHighlight = new(
            Accent ? Color.FromArgb(110, 255, 255, 255) : Color.FromArgb(205, 255, 255, 255),
            1f);
        e.Graphics.DrawPath(topHighlight, innerPath);
        e.Graphics.Restore(state);

        int dropdownWidth = ShowDropDown ? 22 : 0;
        int iconWidth = string.IsNullOrEmpty(IconGlyph) ? 0 : 24;
        Rectangle content = Rectangle.Round(face);
        content.Inflate(-6, 0);
        content.Width -= dropdownWidth;
        if (iconWidth > 0)
        {
            Rectangle iconBounds = new(content.Left + 2, content.Top, iconWidth, content.Height);
            using Font iconFont = FormalUiV4Tokens.Icon(10f);
            TextRenderer.DrawText(
                e.Graphics,
                IconGlyph,
                iconFont,
                iconBounds,
                foreground,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            content.X += iconWidth;
            content.Width -= iconWidth;
        }

        TextRenderer.DrawText(
            e.Graphics,
            Text,
            Font,
            content,
            foreground,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);

        if (ShowDropDown)
        {
            Rectangle arrowBounds = new((int)face.Right - 23, (int)face.Top, 18, (int)face.Height);
            using Font arrowFont = FormalUiV4Tokens.Icon(8f);
            TextRenderer.DrawText(
                e.Graphics,
                DropDownExpanded ? "\uE70E" : "\uE70D",
                arrowFont,
                arrowBounds,
                foreground,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }

    private void DrawSkillRecorderVariant(Graphics graphics)
    {
        bool interactiveHover = Enabled && _hovered;
        Color fill;
        Color foreground;
        Color shadow = Color.Transparent;
        switch (ColorScheme)
        {
            case V4ButtonColorScheme.SkillRecorderPrimary:
                fill = interactiveHover
                    ? FormalUiV4Tokens.SkillRecorderPrimaryHover
                    : FormalUiV4Tokens.SkillRecorderPrimary;
                foreground = Color.White;
                shadow = Color.FromArgb(
                    interactiveHover ? 128 : 102,
                    224,
                    52,
                    30);
                break;
            case V4ButtonColorScheme.SkillRecorderDanger:
                fill = interactiveHover
                    ? FormalUiV4Tokens.SkillRecorderDangerHover
                    : FormalUiV4Tokens.SkillRecorderDanger;
                foreground = Color.White;
                shadow = Color.FromArgb(
                    interactiveHover ? 140 : 115,
                    232,
                    44,
                    23);
                break;
            default:
                fill = interactiveHover
                    ? FormalUiV4Tokens.SkillRecorderSecondaryHover
                    : FormalUiV4Tokens.SkillRecorderSecondary;
                foreground = interactiveHover
                    ? FormalUiV4Tokens.SkillRecorderTextHover
                    : FormalUiV4Tokens.SkillRecorderText;
                break;
        }

        if (!Enabled)
        {
            const int disabledOpacity = 115; // button:disabled { opacity: 0.45; }
            fill = Color.FromArgb(disabledOpacity, fill);
            foreground = Color.FromArgb(disabledOpacity, foreground);
            shadow = Color.FromArgb(
                (int)Math.Round(shadow.A * .45),
                shadow.R,
                shadow.G,
                shadow.B);
        }

        RectangleF face = new(2f, _pressed ? 3f : 2f, Width - 5f, Height - 6f);
        if (shadow.A > 0)
        {
            RectangleF shadowBounds = new(face.X, face.Y + 2f, face.Width, face.Height);
            using GraphicsPath shadowPath = FormalUiV4Drawing.RoundedRectangle(shadowBounds, CornerRadius);
            using SolidBrush shadowBrush = new(shadow);
            graphics.FillPath(shadowBrush, shadowPath);
        }

        using GraphicsPath facePath = FormalUiV4Drawing.RoundedRectangle(face, CornerRadius);
        using SolidBrush faceBrush = new(fill);
        graphics.FillPath(faceBrush, facePath);
        TextRenderer.DrawText(
            graphics,
            Text,
            Font,
            Rectangle.Round(face),
            foreground,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);

        if (Focused)
        {
            RectangleF focusBounds = RectangleF.Inflate(face, -1.5f, -1.5f);
            using GraphicsPath focusPath = FormalUiV4Drawing.RoundedRectangle(
                focusBounds,
                Math.Max(2, CornerRadius - 1));
            using Pen focus = new(FormalUiV4Tokens.SkillRecorderFocus, 2f);
            graphics.DrawPath(focus, focusPath);
        }
    }
}

internal sealed class V4Toggle : Control
{
    private bool _isOn;
    private bool _hovered;
    private bool _pressed;

    internal V4Toggle(bool isOn = false)
    {
        _isOn = isOn;
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.UserPaint |
            ControlStyles.SupportsTransparentBackColor,
            true);
        BackColor = Color.Transparent;
        Cursor = Cursors.Hand;
        TabStop = false;
        Size = new Size(40, 22);
    }

    internal bool IsOn
    {
        get => _isOn;
        set
        {
            _isOn = value;
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
        _pressed = e.Button == MouseButtons.Left;
        Invalidate();
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

    protected override void OnClick(EventArgs e)
    {
        if (!Enabled)
        {
            return;
        }

        IsOn = !IsOn;
        base.OnClick(e);
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        base.OnEnabledChanged(e);
        Cursor = Enabled ? Cursors.Hand : Cursors.Default;
        Invalidate();
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        if (FormalUiV4ResizeBodyPaintGate.IsFrozen)
        {
            FormalUiV4ResizeProbe.RecordSuppressedBodyPaint();
            return;
        }
        base.OnPaintBackground(e);
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
        RectangleF track = new(1f, 2f, Width - 2f, Height - 4f);
        Color trackTop = !Enabled
            ? Color.FromArgb(0xE5, 0xE0, 0xD9)
            : IsOn
            ? (_pressed ? FormalUiV4Tokens.AccentPressed : FormalUiV4Tokens.AccentTop)
            : (_hovered ? Color.FromArgb(0xCE, 0xC8, 0xC1) : FormalUiV4Tokens.ToggleOff);
        Color trackBottom = !Enabled
            ? Color.FromArgb(0xD9, 0xD3, 0xCC)
            : IsOn
            ? FormalUiV4Tokens.AccentBottom
            : Color.FromArgb(0xC7, 0xC1, 0xBA);
        using GraphicsPath path = FormalUiV4Drawing.RoundedRectangle(track, track.Height / 2f);
        FormalUiV4Drawing.FillVerticalGradient(e.Graphics, path, track, trackTop, trackBottom);
        using Pen trackBorder = new(!Enabled
            ? Color.FromArgb(0xD2, 0xCB, 0xC3)
            : IsOn
            ? Color.FromArgb(0xCE, 0x3A, 0x28)
            : Color.FromArgb(0xC5, 0xBF, 0xB8));
        e.Graphics.DrawPath(trackBorder, path);

        RectangleF trackInset = new(track.X + 1f, track.Y + 1f, track.Width - 2f, track.Height - 2f);
        using GraphicsPath insetPath = FormalUiV4Drawing.RoundedRectangle(trackInset, trackInset.Height / 2f);
        GraphicsState clip = e.Graphics.Save();
        e.Graphics.SetClip(new RectangleF(0, 0, Width, Height / 2f + 1f));
        using Pen trackHighlight = new(Color.FromArgb(IsOn ? 70 : 115, 255, 255, 255));
        e.Graphics.DrawPath(trackHighlight, insetPath);
        e.Graphics.Restore(clip);

        float knobSize = track.Height - 4f;
        float knobX = IsOn ? track.Right - knobSize - 2f : track.Left + 2f;
        RectangleF knob = new(knobX, track.Top + (_pressed ? 3f : 2f), knobSize, knobSize);
        using SolidBrush shadow = new(Color.FromArgb(44, 54, 45, 38));
        e.Graphics.FillEllipse(shadow, knob.X, knob.Y + 1.5f, knob.Width, knob.Height);
        using LinearGradientBrush knobBrush = new(
            knob,
            Color.White,
            Color.FromArgb(0xF1, 0xEE, 0xEA),
            LinearGradientMode.Vertical);
        e.Graphics.FillEllipse(knobBrush, knob);
        using Pen knobBorder = new(Color.FromArgb(0xD5, 0xCF, 0xC8));
        e.Graphics.DrawEllipse(knobBorder, knob);
    }
}

internal sealed class V4SelectBox : Control
{
    private bool _hovered;
    private bool _dropDownExpanded;

    internal V4SelectBox(string text)
    {
        Text = text;
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.UserPaint |
            ControlStyles.SupportsTransparentBackColor,
            true);
        BackColor = Color.Transparent;
        Font = FormalUiV4Tokens.Ui(8.2f);
        ForeColor = FormalUiV4Tokens.Ink;
        Cursor = Cursors.Hand;
        Height = 34;
        TabStop = false;
    }

    internal string LeadingGlyph { get; set; } = string.Empty;

    internal bool DropDownExpanded
    {
        get => _dropDownExpanded;
        set
        {
            if (_dropDownExpanded == value)
            {
                return;
            }

            _dropDownExpanded = value;
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
        Invalidate();
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        if (FormalUiV4ResizeBodyPaintGate.IsFrozen)
        {
            FormalUiV4ResizeProbe.RecordSuppressedBodyPaint();
            return;
        }
        base.OnPaintBackground(e);
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
        RectangleF face = new(1f, 1f, Width - 3f, Height - 3f);
        using GraphicsPath path = FormalUiV4Drawing.RoundedRectangle(face, FormalUiV4Tokens.ControlRadius);
        Color top = _hovered ? FormalUiV4Tokens.SurfaceRaised : Color.FromArgb(0xF8, 0xF5, 0xF0);
        Color bottom = _hovered ? Color.FromArgb(0xF7, 0xF2, 0xEC) : FormalUiV4Tokens.ControlBottom;
        using Pen border = new(
            _hovered ? Color.FromArgb(0xD4, 0xCC, 0xC4) : FormalUiV4Tokens.ControlBorder,
            1f);
        FormalUiV4Drawing.FillVerticalGradient(e.Graphics, path, face, top, bottom);
        e.Graphics.DrawPath(border, path);

        using Pen highlight = new(Color.FromArgb(185, 255, 255, 255));
        e.Graphics.DrawLine(highlight, 10, 2, Math.Max(10, Width - 11), 2);

        int left = 10;
        if (!string.IsNullOrEmpty(LeadingGlyph))
        {
            using Font leadingFont = FormalUiV4Tokens.Icon(9.5f);
            TextRenderer.DrawText(
                e.Graphics,
                LeadingGlyph,
                leadingFont,
                new Rectangle(8, 0, 22, Height),
                FormalUiV4Tokens.InkMuted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            left = 33;
        }
        TextRenderer.DrawText(
            e.Graphics,
            Text,
            Font,
            new Rectangle(left, 0, Width - left - 29, Height),
            ForeColor,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
                TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        using Font arrowFont = FormalUiV4Tokens.Icon(8f);
        TextRenderer.DrawText(
            e.Graphics,
            DropDownExpanded ? "\uE70E" : "\uE70D",
            arrowFont,
            new Rectangle(Width - 28, 0, 20, Height),
            FormalUiV4Tokens.InkMuted,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }
}

internal enum V4MicSelectorVisualState
{
    Normal,
    Locked,
    NoDevice,
}

internal sealed class V4MicDeviceSelector : Control
{
    private bool _hovered;
    private V4MicSelectorVisualState _visualState;
    private bool _dropDownExpanded;

    internal V4MicDeviceSelector(string deviceName)
    {
        Text = deviceName;
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.UserPaint |
            ControlStyles.SupportsTransparentBackColor,
            true);
        BackColor = Color.Transparent;
        Font = FormalUiV4Tokens.Ui(8.2f);
        ForeColor = FormalUiV4Tokens.Ink;
        Cursor = Cursors.Hand;
        Height = 34;
        TabStop = false;
        Name = "MicDeviceSelector";
        AccessibleName = "当前麦克风设备";
        UpdateAccessibleDescription();
    }

    internal V4MicSelectorVisualState VisualState
    {
        get => _visualState;
        set
        {
            if (_visualState == value)
            {
                return;
            }

            _visualState = value;
            Cursor = value == V4MicSelectorVisualState.Normal ? Cursors.Hand : Cursors.Default;
            UpdateAccessibleDescription();
            Invalidate();
        }
    }

    internal bool DropDownExpanded
    {
        get => _dropDownExpanded;
        set
        {
            if (_dropDownExpanded == value)
            {
                return;
            }

            _dropDownExpanded = value;
            UpdateAccessibleDescription();
            Invalidate();
        }
    }

    internal void SetDeviceName(string deviceName)
    {
        Text = deviceName;
        UpdateAccessibleDescription();
        Invalidate();
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
        Invalidate();
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        if (FormalUiV4ResizeBodyPaintGate.IsFrozen)
        {
            FormalUiV4ResizeProbe.RecordSuppressedBodyPaint();
            return;
        }
        base.OnPaintBackground(e);
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
        RectangleF face = new(1f, 1f, Width - 3f, Height - 3f);
        using GraphicsPath path = FormalUiV4Drawing.RoundedRectangle(face, FormalUiV4Tokens.ControlRadius);
        bool normal = VisualState == V4MicSelectorVisualState.Normal;
        Color top = VisualState switch
        {
            V4MicSelectorVisualState.Locked => Color.FromArgb(0xF2, 0xEF, 0xEB),
            V4MicSelectorVisualState.NoDevice => Color.FromArgb(0xFB, 0xF6, 0xE9),
            _ => _hovered ? FormalUiV4Tokens.SurfaceRaised : Color.FromArgb(0xF8, 0xF5, 0xF0),
        };
        Color bottom = VisualState switch
        {
            V4MicSelectorVisualState.Locked => Color.FromArgb(0xE9, 0xE5, 0xE0),
            V4MicSelectorVisualState.NoDevice => Color.FromArgb(0xF4, 0xED, 0xDA),
            _ => _hovered ? Color.FromArgb(0xF7, 0xF2, 0xEC) : FormalUiV4Tokens.ControlBottom,
        };
        Color borderColor = VisualState == V4MicSelectorVisualState.NoDevice
            ? Color.FromArgb(0xDD, 0xC9, 0x9B)
            : normal && _hovered
                ? Color.FromArgb(0xD4, 0xCC, 0xC4)
                : FormalUiV4Tokens.ControlBorder;
        FormalUiV4Drawing.FillVerticalGradient(e.Graphics, path, face, top, bottom);
        using Pen border = new(borderColor, 1f);
        e.Graphics.DrawPath(border, path);

        int left = 10;
        if (VisualState == V4MicSelectorVisualState.NoDevice)
        {
            using Font warningFont = FormalUiV4Tokens.Icon(9f);
            TextRenderer.DrawText(
                e.Graphics,
                "\uE7BA",
                warningFont,
                new Rectangle(7, 0, 22, Height),
                Color.FromArgb(0xB7, 0x82, 0x24),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            left = 30;
        }

        string displayText = VisualState == V4MicSelectorVisualState.NoDevice
            ? "未检测到麦克风设备"
            : Text;
        Color textColor = VisualState == V4MicSelectorVisualState.Locked
            ? Color.FromArgb(0x8D, 0x88, 0x82)
            : VisualState == V4MicSelectorVisualState.NoDevice
                ? Color.FromArgb(0x8B, 0x72, 0x43)
                : ForeColor;
        TextRenderer.DrawText(
            e.Graphics,
            displayText,
            Font,
            new Rectangle(left, 0, Math.Max(1, Width - left - 29), Height),
            textColor,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
                TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);

        string trailingGlyph = VisualState switch
        {
            V4MicSelectorVisualState.Locked => "\uE72E",
            V4MicSelectorVisualState.NoDevice => "",
            _ => DropDownExpanded ? "\uE70E" : "\uE70D",
        };
        if (!string.IsNullOrEmpty(trailingGlyph))
        {
            using Font trailingFont = FormalUiV4Tokens.Icon(8f);
            TextRenderer.DrawText(
                e.Graphics,
                trailingGlyph,
                trailingFont,
                new Rectangle(Width - 28, 0, 20, Height),
                VisualState == V4MicSelectorVisualState.Locked
                    ? Color.FromArgb(0x94, 0x8E, 0x87)
                    : FormalUiV4Tokens.InkMuted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }

    private void UpdateAccessibleDescription()
    {
        AccessibleDescription = VisualState switch
        {
            V4MicSelectorVisualState.Locked => $"已锁定；当前选择 {Text}",
            V4MicSelectorVisualState.NoDevice => "未检测到麦克风设备；选择器已禁用",
            _ => $"{(DropDownExpanded ? "已展开" : "已收起")}；当前选择 {Text}",
        };
    }
}

internal sealed class V4PathBox : Control
{
    internal V4PathBox(string path)
    {
        Text = path;
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.UserPaint |
            ControlStyles.SupportsTransparentBackColor,
            true);
        BackColor = Color.Transparent;
        ForeColor = Color.FromArgb(0x9B, 0x98, 0x95);
        Font = FormalUiV4Tokens.Ui(8.2f);
        Height = 36;
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        if (FormalUiV4ResizeBodyPaintGate.IsFrozen)
        {
            FormalUiV4ResizeProbe.RecordSuppressedBodyPaint();
            return;
        }
        base.OnPaintBackground(e);
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
        RectangleF face = new(1f, 1f, Width - 3f, Height - 3f);
        using GraphicsPath path = FormalUiV4Drawing.RoundedRectangle(face, FormalUiV4Tokens.ControlRadius);
        using LinearGradientBrush fill = new(
            face,
            Color.FromArgb(0xF4, 0xF0, 0xEA),
            Color.FromArgb(0xEC, 0xE7, 0xE0),
            LinearGradientMode.Vertical);
        using Pen border = new(FormalUiV4Tokens.ControlBorder, 1f);
        e.Graphics.FillPath(fill, path);
        e.Graphics.DrawPath(border, path);

        using Pen innerShadow = new(Color.FromArgb(50, 111, 92, 77));
        e.Graphics.DrawLine(innerShadow, 10, 2, Math.Max(10, Width - 11), 2);

        Rectangle separator = new(Width - 45, 6, 1, Height - 12);
        using SolidBrush separatorBrush = new(FormalUiV4Tokens.ControlBorder);
        e.Graphics.FillRectangle(separatorBrush, separator);
        TextRenderer.DrawText(
            e.Graphics,
            Text,
            Font,
            new Rectangle(12, 0, Width - 62, Height),
            ForeColor,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
                TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        using Font folderFont = FormalUiV4Tokens.Icon(10f);
        TextRenderer.DrawText(
            e.Graphics,
            "\uE8B7",
            folderFont,
            new Rectangle(Width - 42, 0, 36, Height),
            FormalUiV4Tokens.Ink,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }
}

internal sealed class V4MeterPlaceholder : Control
{
    internal V4MeterPlaceholder()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.UserPaint |
            ControlStyles.SupportsTransparentBackColor,
            true);
        BackColor = Color.Transparent;
        Height = 7;
        MinimumSize = new Size(36, 7);
    }

    internal bool Active { get; set; } = true;

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        if (FormalUiV4ResizeBodyPaintGate.IsFrozen)
        {
            FormalUiV4ResizeProbe.RecordSuppressedBodyPaint();
            return;
        }
        base.OnPaintBackground(e);
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
        const int segmentWidth = 5;
        const int gap = 2;
        int segmentCount = Math.Max(1, (Width + gap) / (segmentWidth + gap));
        int active = Active ? Math.Max(1, (int)Math.Round(segmentCount * .58f)) : 0;
        float totalWidth = segmentCount * segmentWidth + (segmentCount - 1) * gap;
        float startX = Math.Max(0f, (Width - totalWidth) / 2f);
        for (int index = 0; index < segmentCount; index++)
        {
            Color color = index < active
                ? Color.FromArgb(0x3C, 0x39, 0x36)
                : Color.FromArgb(0xD7, 0xD1, 0xCA);
            if (Active && index == segmentCount - 1)
            {
                color = Color.FromArgb(0xDA, 0xB3, 0xAA);
            }
            using SolidBrush segment = new(color);
            e.Graphics.FillRectangle(
                segment,
                startX + index * (segmentWidth + gap),
                2f,
                segmentWidth,
                3f);
        }
    }
}

// FORMAL UI / PRESENTATION ONLY.
// A complete timer frame is prepared off-screen and the front/back bitmaps are
// reused. The opaque surface never exposes its parent between old and new text.
internal sealed class FormalUiStableTimerSurface : Control
{
    private const int WmEraseBackground = 0x0014;
    private Bitmap? _backgroundFrame;
    private Bitmap? _frontFrame;
    private Bitmap? _backFrame;
    private string _displayText = "00:00:00";
    private Color _displayColor = FormalUiV4Tokens.AccentTop;
    private bool _frontFrameReady;

    internal FormalUiStableTimerSurface()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.Opaque |
            ControlStyles.ResizeRedraw |
            ControlStyles.UserPaint,
            true);
        BackColor = FormalUiV4Tokens.Surface;
        AccessibleName = _displayText;
        TabStop = false;
    }

    internal int PreparedFrameCount { get; private set; }
    internal int PaintCount { get; private set; }
    internal int BufferAllocationCount { get; private set; }
    internal int EraseBackgroundMessageCount { get; private set; }

    internal bool SetFrame(string text, Color color)
    {
        if (string.Equals(_displayText, text, StringComparison.Ordinal) &&
            _displayColor == color &&
            _frontFrame is not null)
        {
            return false;
        }

        _displayText = text;
        _displayColor = color;
        AccessibleName = text;
        EnsureBuffers();
        if (_backFrame is null)
        {
            return false;
        }

        RenderFrame(_backFrame);
        (_frontFrame, _backFrame) = (_backFrame, _frontFrame);
        _frontFrameReady = true;
        PreparedFrameCount++;
        Invalidate();
        return true;
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == WmEraseBackground)
        {
            EraseBackgroundMessageCount++;
        }
        base.WndProc(ref message);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        // Opaque + AllPaintingInWmPaint: the prepared frame owns every pixel.
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (FormalUiV4ResizeBodyPaintGate.IsFrozen)
        {
            FormalUiV4ResizeProbe.RecordSuppressedBodyPaint();
            return;
        }

        EnsureFrontFrame();
        if (_frontFrame is not null)
        {
            e.Graphics.DrawImageUnscaled(_frontFrame, Point.Empty);
        }
        PaintCount++;
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        DisposeBuffers();
        base.OnSizeChanged(e);
    }

    protected override void OnLocationChanged(EventArgs e)
    {
        DisposeBuffers();
        base.OnLocationChanged(e);
    }

    protected override void OnFontChanged(EventArgs e)
    {
        DisposeBuffers();
        base.OnFontChanged(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            DisposeBuffers();
        }
        base.Dispose(disposing);
    }

    private void EnsureFrontFrame()
    {
        EnsureBuffers();
        if (_frontFrameReady || _frontFrame is null || _backFrame is null)
        {
            return;
        }
        RenderFrame(_backFrame);
        (_frontFrame, _backFrame) = (_backFrame, _frontFrame);
        _frontFrameReady = true;
    }

    private void EnsureBuffers()
    {
        if (ClientSize.Width < 1 || ClientSize.Height < 1)
        {
            return;
        }
        if (_backgroundFrame?.Size == ClientSize &&
            _frontFrame?.Size == ClientSize &&
            _backFrame?.Size == ClientSize)
        {
            return;
        }

        DisposeBuffers();
        _backgroundFrame = CreateBuffer();
        _frontFrame = CreateBuffer();
        _backFrame = CreateBuffer();
        BufferAllocationCount += 3;
        RenderBackground(_backgroundFrame);
    }

    private Bitmap CreateBuffer()
    {
        Bitmap buffer = new(
            ClientSize.Width,
            ClientSize.Height,
            PixelFormat.Format32bppRgb);
        buffer.SetResolution(DeviceDpi, DeviceDpi);
        return buffer;
    }

    private void RenderBackground(Bitmap target)
    {
        Color top = FormalUiV4Tokens.Surface;
        Color bottom = FormalUiV4Tokens.SurfaceBottom;
        RectangleF gradientBounds = ClientRectangle;
        V4RoundedPanel? card = FindCard();
        if (card is not null && card.IsHandleCreated && IsHandleCreated)
        {
            int topInCard = card.PointToClient(PointToScreen(Point.Empty)).Y;
            top = card.FillColor;
            gradientBounds = new RectangleF(
                0f,
                1f - topInCard,
                Math.Max(1f, Width),
                Math.Max(1f, card.Height - 4f));
        }

        using Graphics graphics = Graphics.FromImage(target);
        FormalUiV4Drawing.Prepare(graphics);
        using LinearGradientBrush background = new(
            gradientBounds,
            top,
            bottom,
            LinearGradientMode.Vertical);
        graphics.FillRectangle(background, ClientRectangle);
    }

    private void RenderFrame(Bitmap target)
    {
        if (_backgroundFrame is null)
        {
            return;
        }
        using Graphics graphics = Graphics.FromImage(target);
        graphics.DrawImageUnscaled(_backgroundFrame, Point.Empty);
        TextFormatFlags flags =
            TextFormatFlags.VerticalCenter |
            TextFormatFlags.SingleLine |
            TextFormatFlags.NoPrefix |
            TextFormatFlags.PreserveGraphicsClipping;

        // GDI ClearType cannot be composited safely from a transparent text
        // background in an image buffer. Supplying the exact opaque gradient
        // color for each scanline retains the Label rendering path without
        // flattening the card material behind the glyphs.
        for (int y = 0; y < ClientSize.Height; y++)
        {
            Color backgroundColor = _backgroundFrame.GetPixel(0, y);
            GraphicsState state = graphics.Save();
            try
            {
                graphics.SetClip(
                    new Rectangle(0, y, ClientSize.Width, 1),
                    CombineMode.Intersect);
                TextRenderer.DrawText(
                    graphics,
                    _displayText,
                    Font,
                    ClientRectangle,
                    _displayColor,
                    backgroundColor,
                    flags);
            }
            finally
            {
                graphics.Restore(state);
            }
        }
    }

    private V4RoundedPanel? FindCard()
    {
        for (Control? current = Parent; current is not null; current = current.Parent)
        {
            if (current is V4RoundedPanel card)
            {
                return card;
            }
        }
        return null;
    }

    private void DisposeBuffers()
    {
        _backgroundFrame?.Dispose();
        _frontFrame?.Dispose();
        _backFrame?.Dispose();
        _backgroundFrame = null;
        _frontFrame = null;
        _backFrame = null;
        _frontFrameReady = false;
    }
}

internal sealed class V4PreviewPanel : Panel
{
    private const int WmEraseBackground = 0x0014;
    private bool _placeholderVisible = true;
    private float _previewAspectRatio = 16f / 9f;
    private FormalUiRecordingPresentationState _presentationState =
        (FormalUiRecordingPresentationState)(-1);
    private string _presentationElapsedText = string.Empty;
    private readonly Font _statusRegularFont;
    private readonly Font _statusBoldFont;
    private readonly SolidBrush _statusHaloBrush;
    private readonly SolidBrush _statusIndicatorBrush;
    private Bitmap? _statusBackgroundFrame;
    private Bitmap? _statusFrontFrame;
    private Bitmap? _statusBackFrame;
    private Rectangle _bufferedStatusBounds;
    private bool _atomicStatusPaintPending;

    internal V4PreviewPanel()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.UserPaint |
            ControlStyles.SupportsTransparentBackColor,
            true);
        BackColor = Color.Transparent;
        Text = "预览中";
        AccessibleName = Text;
        _statusRegularFont = FormalUiV4Tokens.Ui(8.2f);
        _statusBoldFont = FormalUiV4Tokens.Ui(8.2f, FontStyle.Bold);
        _statusHaloBrush = new SolidBrush(Color.FromArgb(44, FormalUiV4Tokens.Accent));
        _statusIndicatorBrush = new SolidBrush(FormalUiV4Tokens.AccentTop);
    }

    internal int AtomicStatusPreparedFrameCount { get; private set; }
    internal int AtomicStatusPaintCount { get; private set; }
    internal int AtomicStatusBufferAllocationCount { get; private set; }
    internal int AtomicStatusEraseBackgroundMessageCount { get; private set; }

    internal bool PlaceholderVisible
    {
        get => _placeholderVisible;
        set
        {
            _placeholderVisible = value;
            DisposeStatusBuffers();
            Invalidate();
        }
    }

    internal float PreviewAspectRatio
    {
        get => _previewAspectRatio;
        set
        {
            if (!float.IsFinite(value) || value <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }
            _previewAspectRatio = value;
            DisposeStatusBuffers();
            Invalidate();
        }
    }

    internal bool SetPresentationState(
        FormalUiRecordingPresentationState state,
        string elapsedText)
    {
        if (_presentationState == state &&
            string.Equals(_presentationElapsedText, elapsedText, StringComparison.Ordinal))
        {
            return false;
        }

        _presentationState = state;
        _presentationElapsedText = elapsedText;
        string statusText = state switch
        {
            FormalUiRecordingPresentationState.Recording =>
                $"REC {elapsedText}",
            FormalUiRecordingPresentationState.Paused =>
                $"Ⅱ 已暂停 {elapsedText}",
            FormalUiRecordingPresentationState.Completed =>
                $"✓ 已完成 {elapsedText}",
            _ => "预览中",
        };
        AccessibleName = statusText;
        PrepareAtomicStatusFrame();
        _atomicStatusPaintPending = true;
        Invalidate(_bufferedStatusBounds);
        return true;
    }

    internal Rectangle PresentationStatusBounds => GetPresentationStatusBounds();

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == WmEraseBackground && _atomicStatusPaintPending)
        {
            AtomicStatusEraseBackgroundMessageCount++;
        }
        base.WndProc(ref message);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        if (FormalUiV4ResizeBodyPaintGate.IsFrozen)
        {
            FormalUiV4ResizeProbe.RecordSuppressedBodyPaint();
            return;
        }
        FormalUiV4ResizeProbe.RecordPreviewPaint();
        Rectangle statusBounds = GetPresentationStatusBounds();
        if (_statusFrontFrame is not null &&
            _bufferedStatusBounds == statusBounds &&
            statusBounds.Contains(e.ClipRectangle))
        {
            e.Graphics.DrawImageUnscaled(_statusFrontFrame, statusBounds.Location);
            AtomicStatusPaintCount++;
            _atomicStatusPaintPending = false;
            return;
        }

        base.OnPaintBackground(e);
        FormalUiV4Drawing.Prepare(e.Graphics);
        if (Width < 20 || Height < 20)
        {
            return;
        }

        RectangleF recess = new(0f, 1f, Width - 1f, Height - 2f);
        using GraphicsPath recessPath = FormalUiV4Drawing.RoundedRectangle(
            recess,
            FormalUiV4Tokens.PreviewRadius + 2f);
        using SolidBrush recessBrush = new(Color.FromArgb(45, 91, 71, 56));
        e.Graphics.FillPath(recessBrush, recessPath);

        RectangleF shadow = new(2f, 4f, Width - 4f, Height - 5f);
        using GraphicsPath shadowPath = FormalUiV4Drawing.RoundedRectangle(shadow, 18f);
        using SolidBrush shadowBrush = new(Color.FromArgb(52, 35, 28, 24));
        e.Graphics.FillPath(shadowBrush, shadowPath);

        RectangleF face = new(1f, 1f, Width - 3f, Height - 5f);
        using GraphicsPath facePath = FormalUiV4Drawing.RoundedRectangle(
            face,
            FormalUiV4Tokens.PreviewRadius);
        FormalUiV4Drawing.FillVerticalGradient(
            e.Graphics,
            facePath,
            face,
            FormalUiV4Tokens.MonitorTop,
            FormalUiV4Tokens.MonitorBottom);
        using Pen border = new(FormalUiV4Tokens.MonitorBorder, 1f);
        e.Graphics.DrawPath(border, facePath);

        RectangleF bezelInner = new(5f, 5f, Width - 11f, Height - 12f);
        using GraphicsPath bezelInnerPath = FormalUiV4Drawing.RoundedRectangle(
            bezelInner,
            FormalUiV4Tokens.PreviewRadius - 3f);
        using Pen bezelHighlight = new(Color.FromArgb(125, 143, 137, 129), 1f);
        e.Graphics.DrawPath(bezelHighlight, bezelInnerPath);

        RectangleF display = new(7f, 7f, Width - 15f, Height - 16f);
        using GraphicsPath displayPath = FormalUiV4Drawing.RoundedRectangle(
            display,
            FormalUiV4Tokens.PreviewRadius - 5f);
        FormalUiV4Drawing.FillVerticalGradient(
            e.Graphics,
            displayPath,
            display,
            FormalUiV4Tokens.DisplayTop,
            FormalUiV4Tokens.DisplayBottom);
        using Pen displayBorder = new(Color.FromArgb(0x10, 0x10, 0x0F), 1f);
        e.Graphics.DrawPath(displayBorder, displayPath);

        if (!PlaceholderVisible)
        {
            return;
        }

        RectangleF contentContainer = new(
            display.Left + 3f,
            display.Top + 3f,
            display.Width - 6f,
            display.Height - 6f);
        RectangleF contentView = CalculateAspectFit(contentContainer, PreviewAspectRatio);
        using GraphicsPath contentPath = FormalUiV4Drawing.RoundedRectangle(
            contentView,
            Math.Max(4f, FormalUiV4Tokens.PreviewRadius - 8f));
        FormalUiV4Drawing.FillVerticalGradient(
            e.Graphics,
            contentPath,
            contentView,
            Color.FromArgb(0x24, 0x22, 0x20),
            Color.FromArgb(0x19, 0x18, 0x17));
        using Pen contentBorder = new(Color.FromArgb(105, 92, 86, 80), 1f);
        e.Graphics.DrawPath(contentBorder, contentPath);

        GraphicsState clipped = e.Graphics.Save();
        e.Graphics.SetClip(contentPath);
        RectangleF glowBounds = new(
            contentView.Left + contentView.Width * .13f,
            contentView.Top + contentView.Height * .08f,
            Math.Max(80f, contentView.Width * .28f),
            Math.Max(70f, contentView.Height * .45f));
        using GraphicsPath glowPath = new();
        glowPath.AddEllipse(glowBounds);
        using PathGradientBrush glow = new(glowPath)
        {
            CenterColor = Color.FromArgb(29, 205, 74, 52),
            SurroundColors = new[] { Color.FromArgb(0, 205, 74, 52) },
        };
        e.Graphics.FillPath(glow, glowPath);

        using SolidBrush dot = new(Color.FromArgb(18, 235, 224, 214));
        float dotStartX = contentView.Left + contentView.Width * .57f;
        for (float y = contentView.Top + 34f; y < contentView.Bottom - 24f; y += 15f)
        {
            for (float x = dotStartX; x < contentView.Right - 24f; x += 15f)
            {
                float taper = 1f - (x - dotStartX) / Math.Max(1f, contentView.Right - dotStartX);
                if (((int)((x + y) / 15f) & 1) == 0 || taper > .55f)
                {
                    e.Graphics.FillEllipse(dot, x, y, 1.2f, 1.2f);
                }
            }
        }
        e.Graphics.Restore(clipped);

        DrawCornerGuides(e.Graphics, contentView);
        if (_statusFrontFrame is not null && _bufferedStatusBounds == statusBounds)
        {
            e.Graphics.DrawImageUnscaled(_statusFrontFrame, statusBounds.Location);
            AtomicStatusPaintCount++;
            _atomicStatusPaintPending = false;
        }
        else
        {
            DrawStatus(e.Graphics, contentView);
        }
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        DisposeStatusBuffers();
        base.OnSizeChanged(e);
        if (!string.IsNullOrEmpty(_presentationElapsedText))
        {
            PrepareAtomicStatusFrame();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            DisposeStatusBuffers();
            _statusRegularFont.Dispose();
            _statusBoldFont.Dispose();
            _statusHaloBrush.Dispose();
            _statusIndicatorBrush.Dispose();
        }
        base.Dispose(disposing);
    }

    internal static RectangleF CalculateAspectFit(RectangleF container, float sourceRatio)
    {
        if (container.Width <= 0f || container.Height <= 0f ||
            !float.IsFinite(sourceRatio) || sourceRatio <= 0f)
        {
            return RectangleF.Empty;
        }

        float containerRatio = container.Width / container.Height;
        float width;
        float height;
        if (containerRatio > sourceRatio)
        {
            height = container.Height;
            width = height * sourceRatio;
        }
        else
        {
            width = container.Width;
            height = width / sourceRatio;
        }

        return new RectangleF(
            container.Left + (container.Width - width) / 2f,
            container.Top + (container.Height - height) / 2f,
            width,
            height);
    }

    private static void DrawCornerGuides(Graphics graphics, RectangleF display)
    {
        const float length = 17f;
        const float inset = 14f;
        using Pen guide = new(Color.FromArgb(118, 183, 178, 170), 1f);
        float left = display.Left + inset;
        float right = display.Right - inset;
        float top = display.Top + inset;
        float bottom = display.Bottom - inset;

        graphics.DrawLine(guide, left, top, left + length, top);
        graphics.DrawLine(guide, left, top, left, top + length);
        graphics.DrawLine(guide, right - length, top, right, top);
        graphics.DrawLine(guide, right, top, right, top + length);
        graphics.DrawLine(guide, left, bottom, left + length, bottom);
        graphics.DrawLine(guide, left, bottom - length, left, bottom);
        graphics.DrawLine(guide, right - length, bottom, right, bottom);
        graphics.DrawLine(guide, right, bottom - length, right, bottom);
    }

    private void DrawStatus(Graphics graphics, RectangleF display) =>
        DrawStatus(graphics, display, Point.Empty);

    private void DrawStatus(Graphics graphics, RectangleF display, Point offset)
    {
        if (_presentationState == FormalUiRecordingPresentationState.Completed)
        {
            TextRenderer.DrawText(
                graphics,
                $"✓ 已完成 {_presentationElapsedText}",
                _statusBoldFont,
                new Rectangle(
                    (int)display.Left + 23 + offset.X,
                    (int)display.Top + 13 + offset.Y,
                    180,
                    18),
                Color.FromArgb(0xD5, 0xD1, 0xCA),
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            return;
        }

        if (_presentationState == FormalUiRecordingPresentationState.Paused)
        {
            TextRenderer.DrawText(
                graphics,
                $"Ⅱ 已暂停 {_presentationElapsedText}",
                _statusBoldFont,
                new Rectangle(
                    (int)display.Left + 23 + offset.X,
                    (int)display.Top + 13 + offset.Y,
                    180,
                    18),
                Color.FromArgb(0xC3, 0xBF, 0xB9),
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            return;
        }

        RectangleF light = new(
            display.Left + 23f + offset.X,
            display.Top + 19f + offset.Y,
            6f,
            6f);
        graphics.FillEllipse(
            _statusHaloBrush,
            light.X - 2f,
            light.Y - 2f,
            light.Width + 4f,
            light.Height + 4f);
        graphics.FillEllipse(_statusIndicatorBrush, light);

        bool isRecording =
            _presentationState == FormalUiRecordingPresentationState.Recording;
        TextRenderer.DrawText(
            graphics,
            isRecording
                ? $"REC {_presentationElapsedText}"
                : "预览中",
            isRecording ? _statusBoldFont : _statusRegularFont,
            new Rectangle(
                (int)light.Right + 7,
                (int)display.Top + 13 + offset.Y,
                isRecording ? 160 : 70,
                18),
            Color.FromArgb(0xCE, 0xCA, 0xC4),
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }

    private void PrepareAtomicStatusFrame()
    {
        EnsureStatusBuffers();
        if (_statusBackgroundFrame is null || _statusBackFrame is null)
        {
            return;
        }

        using Graphics graphics = Graphics.FromImage(_statusBackFrame);
        graphics.DrawImageUnscaled(_statusBackgroundFrame, Point.Empty);
        RectangleF contentView = GetContentView();
        DrawStatus(
            graphics,
            contentView,
            new Point(-_bufferedStatusBounds.X, -_bufferedStatusBounds.Y));
        (_statusFrontFrame, _statusBackFrame) =
            (_statusBackFrame, _statusFrontFrame);
        AtomicStatusPreparedFrameCount++;
    }

    private void EnsureStatusBuffers()
    {
        Rectangle bounds = GetPresentationStatusBounds();
        if (bounds.Width < 1 || bounds.Height < 1)
        {
            return;
        }
        if (_statusBackgroundFrame?.Size == bounds.Size &&
            _statusFrontFrame?.Size == bounds.Size &&
            _statusBackFrame?.Size == bounds.Size &&
            _bufferedStatusBounds == bounds)
        {
            return;
        }

        DisposeStatusBuffers();
        _bufferedStatusBounds = bounds;
        _statusBackgroundFrame = CreateStatusBuffer(bounds.Size);
        _statusFrontFrame = CreateStatusBuffer(bounds.Size);
        _statusBackFrame = CreateStatusBuffer(bounds.Size);
        AtomicStatusBufferAllocationCount += 3;
        RenderStatusBackground(_statusBackgroundFrame, bounds);
    }

    private Bitmap CreateStatusBuffer(Size size)
    {
        Bitmap buffer = new(size.Width, size.Height);
        buffer.SetResolution(DeviceDpi, DeviceDpi);
        return buffer;
    }

    private void RenderStatusBackground(Bitmap target, Rectangle bounds)
    {
        using Graphics graphics = Graphics.FromImage(target);
        FormalUiV4Drawing.Prepare(graphics);
        GraphicsState translated = graphics.Save();
        graphics.TranslateTransform(-bounds.X, -bounds.Y);
        RectangleF contentView = GetContentView();
        using GraphicsPath contentPath = FormalUiV4Drawing.RoundedRectangle(
            contentView,
            Math.Max(4f, FormalUiV4Tokens.PreviewRadius - 8f));
        FormalUiV4Drawing.FillVerticalGradient(
            graphics,
            contentPath,
            contentView,
            Color.FromArgb(0x24, 0x22, 0x20),
            Color.FromArgb(0x19, 0x18, 0x17));

        RectangleF glowBounds = new(
            contentView.Left + contentView.Width * .13f,
            contentView.Top + contentView.Height * .08f,
            Math.Max(80f, contentView.Width * .28f),
            Math.Max(70f, contentView.Height * .45f));
        using GraphicsPath glowPath = new();
        glowPath.AddEllipse(glowBounds);
        using PathGradientBrush glow = new(glowPath)
        {
            CenterColor = Color.FromArgb(29, 205, 74, 52),
            SurroundColors = new[] { Color.FromArgb(0, 205, 74, 52) },
        };
        graphics.FillPath(glow, glowPath);
        DrawCornerGuides(graphics, contentView);
        graphics.Restore(translated);
    }

    private RectangleF GetContentView()
    {
        RectangleF display = new(7f, 7f, Width - 15f, Height - 16f);
        RectangleF contentContainer = new(
            display.Left + 3f,
            display.Top + 3f,
            display.Width - 6f,
            display.Height - 6f);
        return CalculateAspectFit(contentContainer, PreviewAspectRatio);
    }

    private void DisposeStatusBuffers()
    {
        _statusBackgroundFrame?.Dispose();
        _statusFrontFrame?.Dispose();
        _statusBackFrame?.Dispose();
        _statusBackgroundFrame = null;
        _statusFrontFrame = null;
        _statusBackFrame = null;
        _bufferedStatusBounds = Rectangle.Empty;
    }

    private Rectangle GetPresentationStatusBounds()
    {
        RectangleF contentView = GetContentView();
        Rectangle statusBounds = Rectangle.Ceiling(new RectangleF(
            contentView.Left + 19f,
            contentView.Top + 9f,
            207f,
            26f));
        return Rectangle.Intersect(ClientRectangle, statusBounds);
    }
}

internal sealed class V4LegacyWordmark : Control
{
    private const string PrefixText = "Legacy U";

    internal V4LegacyWordmark()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.UserPaint |
            ControlStyles.SupportsTransparentBackColor,
            true);
        BackColor = Color.Transparent;
        ForeColor = FormalUiV4Tokens.Ink;
        Font = FormalUiV4Tokens.Ui(10.8f, FontStyle.Bold);
        Text = "Legacy UI";
        AccessibleName = Text;
        TabStop = false;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        FormalUiV4Drawing.Prepare(e.Graphics);
        TextFormatFlags flags =
            TextFormatFlags.Left |
            TextFormatFlags.VerticalCenter |
            TextFormatFlags.NoPadding |
            TextFormatFlags.NoPrefix;
        Size prefixSize = TextRenderer.MeasureText(
            e.Graphics,
            PrefixText,
            Font,
            new Size(int.MaxValue, Math.Max(1, Height)),
            flags);
        Rectangle prefixBounds = new(2, 0, prefixSize.Width + 1, Height);
        TextRenderer.DrawText(
            e.Graphics,
            PrefixText,
            Font,
            prefixBounds,
            ForeColor,
            flags);

        float scale = DeviceDpi / 96f;
        float outerDiameter = 10.5f * scale;
        float ringLeft = prefixBounds.Left + prefixSize.Width + 1.25f * scale;
        RectangleF outerRing = new(
            ringLeft,
            (Height - outerDiameter) / 2f,
            outerDiameter,
            outerDiameter);
        float innerInset = 2.55f * scale;
        RectangleF innerRing = RectangleF.Inflate(
            outerRing,
            -innerInset,
            -innerInset);
        using Pen outerPen = new(
            FormalUiV4Tokens.AccentTop,
            Math.Max(1f, 1.05f * scale));
        using Pen innerPen = new(
            FormalUiV4Tokens.AccentTop,
            Math.Max(1f, .9f * scale));
        e.Graphics.DrawEllipse(outerPen, outerRing);
        e.Graphics.DrawEllipse(innerPen, innerRing);
    }
}

internal sealed class V4ChromeButton : Control
{
    private const int WmNcHitTest = 0x0084;
    private const int HtTransparent = -1;
    private const int HoverSurfaceSize = 30;
    private const int HoverTextHorizontalPadding = 18;
    private const float HoverCornerRadius = 8f;

    private bool _hovered;
    private bool _pressed;

    internal V4ChromeButton()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.UserPaint |
            ControlStyles.SupportsTransparentBackColor,
            true);
        BackColor = Color.Transparent;
        ForeColor = FormalUiV4Tokens.InkMuted;
        Cursor = Cursors.Hand;
        TabStop = false;
    }

    internal bool Danger { get; set; }
    internal bool SizeSurfaceToText { get; set; }
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
        _pressed = e.Button == MouseButtons.Left;
        Invalidate();
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

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        FormalUiV4Drawing.Prepare(e.Graphics);
        Color fill = Danger && _hovered
            ? FormalUiV4Tokens.AccentBottom
            : _pressed
                ? Color.FromArgb(0xE9, 0xE4, 0xDD)
                : _hovered
                    ? Color.FromArgb(0xE3, 0xDD, 0xD6)
                    : Color.Transparent;

        Rectangle surfaceBounds = GetSurfaceBounds();

        if (fill.A > 0)
        {
            using SolidBrush background = new(fill);
            using GraphicsPath hoverPath = FormalUiV4Drawing.RoundedRectangle(
                surfaceBounds,
                HoverCornerRadius);
            e.Graphics.FillPath(background, hoverPath);
        }

        Color textColor = Danger && _hovered ? Color.White : ForeColor;
        TextRenderer.DrawText(
            e.Graphics,
            Text,
            Font,
            surfaceBounds,
            textColor,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }

    private Rectangle GetSurfaceBounds()
    {
        int surfaceWidth = HoverSurfaceSize;
        if (SizeSurfaceToText)
        {
            int textWidth = TextRenderer.MeasureText(
                Text,
                Font,
                System.Drawing.Size.Empty,
                TextFormatFlags.NoPadding).Width;
            surfaceWidth = Math.Max(
                HoverSurfaceSize,
                textWidth + HoverTextHorizontalPadding);
        }

        surfaceWidth = Math.Min(surfaceWidth, Math.Max(1, Width));
        if (((Width - surfaceWidth) & 1) != 0)
        {
            surfaceWidth += surfaceWidth < Width ? 1 : -1;
        }

        int surfaceHeight = Math.Min(HoverSurfaceSize, Math.Max(1, Height));
        return new Rectangle(
            (Width - surfaceWidth) / 2,
            (Height - surfaceHeight) / 2,
            surfaceWidth,
            surfaceHeight);
    }
}

internal sealed class V4RecordButton : Control
{
    private bool _hovered;
    private bool _pressed;

    internal V4RecordButton()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.UserPaint |
            ControlStyles.SupportsTransparentBackColor,
            true);
        BackColor = Color.Transparent;
        ForeColor = Color.White;
        Font = FormalUiV4Tokens.Ui(11.5f);
        Cursor = Cursors.Hand;
        TabStop = false;
    }

    internal int CornerRadius { get; set; } = 13;

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
        _pressed = e.Button == MouseButtons.Left;
        Invalidate();
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

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        if (FormalUiV4ResizeBodyPaintGate.IsFrozen)
        {
            FormalUiV4ResizeProbe.RecordSuppressedBodyPaint();
            return;
        }
        base.OnPaintBackground(e);
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
        if (Width < 8 || Height < 8)
        {
            return;
        }

        float faceTop = _pressed ? 2f : _hovered ? .5f : 1f;
        float baseTop = _hovered && !_pressed ? 4.5f : 4f;
        RectangleF basePlate = new(1f, baseTop, Width - 3f, Height - 6f);
        using GraphicsPath basePath = FormalUiV4Drawing.RoundedRectangle(basePlate, CornerRadius);
        using SolidBrush baseBrush = new(_hovered && !_pressed
            ? FormalUiV4Tokens.RecordHoverDepth
            : Color.FromArgb(0xA9, 0x2C, 0x20));
        e.Graphics.FillPath(baseBrush, basePath);

        RectangleF face = new(1f, faceTop, Width - 3f, Height - 7f);
        using GraphicsPath facePath = FormalUiV4Drawing.RoundedRectangle(face, CornerRadius);
        Color top = _pressed
            ? Color.FromArgb(0xD7, 0x43, 0x30)
            : _hovered
                ? FormalUiV4Tokens.RecordHoverTop
                : FormalUiV4Tokens.AccentTop;
        Color bottom = _pressed
            ? FormalUiV4Tokens.AccentPressed
            : _hovered
                ? FormalUiV4Tokens.RecordHoverBottom
                : FormalUiV4Tokens.AccentBottom;
        FormalUiV4Drawing.FillVerticalGradient(e.Graphics, facePath, face, top, bottom);
        using Pen border = new(
            _hovered && !_pressed
                ? FormalUiV4Tokens.RecordHoverBorder
                : FormalUiV4Tokens.AccentBorder,
            1f);
        e.Graphics.DrawPath(border, facePath);

        RectangleF inner = new(face.X + 2f, face.Y + 2f, face.Width - 4f, face.Height - 4f);
        using GraphicsPath innerPath = FormalUiV4Drawing.RoundedRectangle(inner, CornerRadius - 2f);
        GraphicsState state = e.Graphics.Save();
        e.Graphics.SetClip(new RectangleF(0, 0, Width, face.Top + face.Height / 2f));
        using Pen highlight = new(
            Color.FromArgb(_hovered && !_pressed ? 160 : _pressed ? 78 : 105, 255, 255, 255),
            1f);
        e.Graphics.DrawPath(highlight, innerPath);
        e.Graphics.Restore(state);

        float lensSize = Math.Min(17f, Math.Max(12f, face.Height * .27f));
        RectangleF lens = new(
            face.Left + 14f,
            face.Top + face.Height / 2f - lensSize / 2f,
            lensSize,
            lensSize);
        using SolidBrush lensWell = new(Color.FromArgb(72, 115, 26, 20));
        e.Graphics.FillEllipse(lensWell, lens.X - 2f, lens.Y - 1f, lens.Width + 4f, lens.Height + 4f);
        using SolidBrush lensFace = new(_hovered && !_pressed
            ? Color.FromArgb(0xFF, 0x50, 0x39)
            : Color.FromArgb(0xF7, 0x3E, 0x29));
        e.Graphics.FillEllipse(lensFace, lens);
        using Pen lensRing = new(Color.FromArgb(215, 255, 255, 255), 1.5f);
        e.Graphics.DrawEllipse(lensRing, lens);

        Rectangle textBounds = new(
            (int)lens.Right + 10,
            (int)face.Top,
            Math.Max(1, (int)(face.Right - lens.Right - 20)),
            (int)face.Height);
        TextRenderer.DrawText(
            e.Graphics,
            Text,
            Font,
            textBounds,
            Color.White,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }
}
