using System.Drawing.Drawing2D;

namespace XbPreview.Host;

internal sealed class FormalUiBackgroundSelectorPopover : Control
{
    internal const int CornerRadius = 13;
    internal const int RowHeight = 34;
    internal const int SwatchSize = 18;
    internal const int VerticalPadding = 4;
    internal const int ShadowInset = 3;

    internal static readonly Color BackgroundColor = Color.FromArgb(0xFF, 0xFE, 0xFC);
    internal static readonly Color BorderColor = Color.FromArgb(0xD8, 0xD0, 0xC8);
    internal static readonly Color HoverFill = Color.FromArgb(0xF3, 0xEF, 0xE9);

    private readonly IReadOnlyList<FormalUiBackgroundPresentationItem> _items;
    private int _hoveredIndex = -1;
    private string _selectedItemId;

    internal FormalUiBackgroundSelectorPopover(
        IReadOnlyList<FormalUiBackgroundPresentationItem> items,
        string selectedItemId)
    {
        _items = items;
        _selectedItemId = selectedItemId;
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.UserPaint |
            ControlStyles.SupportsTransparentBackColor,
            true);
        BackColor = Color.Transparent;
        Cursor = Cursors.Hand;
        Font = FormalUiV4Tokens.Ui(7.6f);
        Name = "BackgroundSelectorPopover";
        AccessibleName = "背景选择器";
        TabStop = false;
        Visible = false;
        Height = VerticalPadding * 2 + RowHeight * _items.Count + ShadowInset;
        UpdateAccessibleDescription();
    }

    internal event EventHandler<FormalUiBackgroundPresentationItem>? ItemInvoked;

    internal string SelectedItemId
    {
        get => _selectedItemId;
        set
        {
            if (_selectedItemId == value)
            {
                return;
            }

            _selectedItemId = value;
            UpdateAccessibleDescription();
            Invalidate();
        }
    }

    internal int HoveredIndex => _hoveredIndex;

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int hoveredIndex = GetRowIndex(e.Location);
        if (_hoveredIndex == hoveredIndex)
        {
            return;
        }

        _hoveredIndex = hoveredIndex;
        UpdateAccessibleDescription();
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (_hoveredIndex == -1)
        {
            return;
        }

        _hoveredIndex = -1;
        UpdateAccessibleDescription();
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button != MouseButtons.Left)
        {
            return;
        }

        int rowIndex = GetRowIndex(e.Location);
        if (rowIndex >= 0)
        {
            ItemInvoked?.Invoke(this, _items[rowIndex]);
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        FormalUiV4Drawing.Prepare(e.Graphics);
        if (Width < 12 || Height < 12)
        {
            return;
        }

        RectangleF shadowBounds = new(2.5f, 3.5f, Width - 5f, Height - 6f);
        using GraphicsPath shadowPath = FormalUiV4Drawing.RoundedRectangle(
            shadowBounds,
            CornerRadius + 1f);
        using SolidBrush shadowBrush = new(Color.FromArgb(20, 0x63, 0x55, 0x49));
        e.Graphics.FillPath(shadowBrush, shadowPath);

        RectangleF faceBounds = new(1.5f, 1.5f, Width - 5f, Height - 6f);
        using GraphicsPath facePath = FormalUiV4Drawing.RoundedRectangle(faceBounds, CornerRadius);
        using SolidBrush faceBrush = new(BackgroundColor);
        using Pen borderPen = new(BorderColor, 1f);
        e.Graphics.FillPath(faceBrush, facePath);
        e.Graphics.DrawPath(borderPen, facePath);

        RectangleF highlightBounds = new(
            faceBounds.X + 1f,
            faceBounds.Y + 1f,
            faceBounds.Width - 2f,
            faceBounds.Height - 2f);
        using GraphicsPath highlightPath = FormalUiV4Drawing.RoundedRectangle(
            highlightBounds,
            CornerRadius - 1f);
        GraphicsState highlightState = e.Graphics.Save();
        e.Graphics.SetClip(new RectangleF(0, 0, Width, 22f));
        using Pen highlightPen = new(Color.FromArgb(180, Color.White), 1f);
        e.Graphics.DrawPath(highlightPen, highlightPath);
        e.Graphics.Restore(highlightState);

        for (int index = 0; index < _items.Count; index++)
        {
            DrawRow(e.Graphics, index, _items[index]);
        }
    }

    private void DrawRow(
        Graphics graphics,
        int index,
        FormalUiBackgroundPresentationItem item)
    {
        Rectangle row = GetRowBounds(index);
        bool selected = item.Id == _selectedItemId;
        bool hovered = index == _hoveredIndex;
        RectangleF rowFace = new(row.X + 5f, row.Y + 2f, row.Width - 10f, row.Height - 4f);

        if (selected || hovered)
        {
            using GraphicsPath rowPath = FormalUiV4Drawing.RoundedRectangle(rowFace, 9f);
            using SolidBrush rowBrush = new(selected ? FormalUiV4Tokens.SelectedFill : HoverFill);
            graphics.FillPath(rowBrush, rowPath);
            if (selected)
            {
                using Pen selectedBorder = new(FormalUiV4Tokens.SelectedBorder, 1f);
                graphics.DrawPath(selectedBorder, rowPath);
            }
        }

        Rectangle swatchBounds = new(
            row.X + 11,
            row.Y + (row.Height - SwatchSize) / 2,
            SwatchSize,
            SwatchSize);
        DrawSwatch(graphics, swatchBounds, item.Swatch, selected);

        Rectangle titleBounds = new(
            swatchBounds.Right + 7,
            row.Y,
            Math.Max(1, row.Right - swatchBounds.Right - 16),
            row.Height);
        TextRenderer.DrawText(
            graphics,
            item.DisplayName,
            Font,
            titleBounds,
            FormalUiV4Tokens.Ink,
            TextFormatFlags.Left |
                TextFormatFlags.VerticalCenter |
                TextFormatFlags.SingleLine |
                TextFormatFlags.EndEllipsis |
                TextFormatFlags.NoPadding);
    }

    private static void DrawSwatch(
        Graphics graphics,
        Rectangle bounds,
        FormalUiBackgroundSwatch swatch,
        bool selected)
    {
        Color outline = selected ? FormalUiV4Tokens.SelectedBorder : Color.FromArgb(0xD6, 0xCE, 0xC5);
        if (swatch == FormalUiBackgroundSwatch.Warm)
        {
            using SolidBrush warmBrush = new(Color.FromArgb(0xF5, 0xEB, 0xDA));
            using Pen outlinePen = new(outline, 1f);
            graphics.FillEllipse(warmBrush, bounds);
            graphics.DrawEllipse(outlinePen, bounds);
            return;
        }

        using GraphicsPath swatchPath = FormalUiV4Drawing.RoundedRectangle(bounds, 5f);
        if (swatch == FormalUiBackgroundSwatch.CustomImage)
        {
            using SolidBrush plateBrush = new(Color.FromArgb(0xF0, 0xEB, 0xE5));
            using Pen outlinePen = new(outline, 1f);
            graphics.FillPath(plateBrush, swatchPath);
            graphics.DrawPath(outlinePen, swatchPath);
            using Pen picturePen = new(
                selected ? FormalUiV4Tokens.SelectedText : FormalUiV4Tokens.InkMuted,
                1.2f);
            RectangleF frame = new(bounds.X + 4.5f, bounds.Y + 4.5f, bounds.Width - 9f, bounds.Height - 9f);
            graphics.DrawRectangle(picturePen, frame.X, frame.Y, frame.Width, frame.Height);
            graphics.DrawLines(picturePen,
            [
                new PointF(frame.Left + 1f, frame.Bottom - 1f),
                new PointF(frame.Left + 3.5f, frame.Top + 4f),
                new PointF(frame.Left + 5.5f, frame.Top + 6f),
                new PointF(frame.Right - 1f, frame.Top + 2.5f),
            ]);
            return;
        }

        Color top = swatch == FormalUiBackgroundSwatch.Fantasy01
            ? Color.FromArgb(0xF5, 0xCC, 0xD8)
            : Color.FromArgb(0xC7, 0xDD, 0xFA);
        Color bottom = swatch == FormalUiBackgroundSwatch.Fantasy01
            ? Color.FromArgb(0xC8, 0xDB, 0xF5)
            : Color.FromArgb(0xF2, 0xCF, 0xE9);
        using LinearGradientBrush gradient = new(bounds, top, bottom, LinearGradientMode.ForwardDiagonal);
        using Pen gradientOutline = new(outline, 1f);
        graphics.FillPath(gradient, swatchPath);
        graphics.DrawPath(gradientOutline, swatchPath);
    }

    private Rectangle GetRowBounds(int index) => new(
        ShadowInset,
        VerticalPadding + index * RowHeight,
        Math.Max(1, Width - ShadowInset * 2),
        RowHeight);

    private int GetRowIndex(Point location)
    {
        int contentY = location.Y - VerticalPadding;
        if (contentY < 0)
        {
            return -1;
        }

        int index = contentY / RowHeight;
        return index >= 0 && index < _items.Count ? index : -1;
    }

    private void UpdateAccessibleDescription()
    {
        string selected = _items.First(item => item.Id == _selectedItemId).DisplayName;
        string hover = _hoveredIndex >= 0 ? $"；悬停 {_items[_hoveredIndex].DisplayName}" : string.Empty;
        AccessibleDescription = $"四个背景选项；当前选择 {selected}{hover}";
    }
}
