using System.Drawing.Drawing2D;

namespace XbPreview.Host;

internal sealed class FormalUiMicSelectorPopover : Control
{
    internal const int CornerRadius = 13;
    internal const int RowHeight = 34;
    internal const int IconSize = 18;
    internal const int VerticalPadding = 4;
    internal const int ShadowInset = 3;

    internal static readonly Color BackgroundColor = Color.FromArgb(0xFF, 0xFE, 0xFC);
    internal static readonly Color BorderColor = Color.FromArgb(0xD8, 0xD0, 0xC8);
    internal static readonly Color HoverFill = Color.FromArgb(0xF3, 0xEF, 0xE9);

    private readonly IReadOnlyList<FormalUiMicDevicePresentationItem> _items;
    private int _hoveredIndex = -1;
    private string _selectedItemId;

    internal FormalUiMicSelectorPopover(
        IReadOnlyList<FormalUiMicDevicePresentationItem> items,
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
        Name = "MicSelectorPopover";
        AccessibleName = "麦克风设备列表";
        TabStop = false;
        Visible = false;
        Height = VerticalPadding * 2 + RowHeight * _items.Count + ShadowInset;
        UpdateAccessibleDescription();
    }

    internal event EventHandler<FormalUiMicDevicePresentationItem>? ItemSelected;

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
        if (rowIndex < 0)
        {
            return;
        }

        FormalUiMicDevicePresentationItem item = _items[rowIndex];
        SelectedItemId = item.Id;
        ItemSelected?.Invoke(this, item);
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

        for (int index = 0; index < _items.Count; index++)
        {
            DrawRow(e.Graphics, index, _items[index]);
        }
    }

    private void DrawRow(
        Graphics graphics,
        int index,
        FormalUiMicDevicePresentationItem item)
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

        Rectangle iconBounds = new(
            row.X + 11,
            row.Y + (row.Height - IconSize) / 2,
            IconSize,
            IconSize);
        using GraphicsPath iconPlate = FormalUiV4Drawing.RoundedRectangle(iconBounds, 5f);
        using SolidBrush iconPlateBrush = new(selected
            ? Color.FromArgb(0xFD, 0xF0, 0xED)
            : Color.FromArgb(0xF0, 0xEB, 0xE5));
        graphics.FillPath(iconPlateBrush, iconPlate);

        using Font iconFont = FormalUiV4Tokens.Icon(8.5f);
        TextRenderer.DrawText(
            graphics,
            "\uE720",
            iconFont,
            iconBounds,
            selected ? FormalUiV4Tokens.SelectedText : FormalUiV4Tokens.InkMuted,
            TextFormatFlags.HorizontalCenter |
                TextFormatFlags.VerticalCenter |
                TextFormatFlags.NoPadding);

        Rectangle titleBounds = new(
            iconBounds.Right + 7,
            row.Y,
            Math.Max(1, row.Right - iconBounds.Right - 16),
            row.Height);
        TextRenderer.DrawText(
            graphics,
            item.Name,
            Font,
            titleBounds,
            FormalUiV4Tokens.Ink,
            TextFormatFlags.Left |
                TextFormatFlags.VerticalCenter |
                TextFormatFlags.SingleLine |
                TextFormatFlags.EndEllipsis |
                TextFormatFlags.NoPadding);
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
        string selected = _items.First(item => item.Id == _selectedItemId).Name;
        string hover = _hoveredIndex >= 0 ? $"；悬停 {_items[_hoveredIndex].Name}" : string.Empty;
        AccessibleDescription = $"四个演示设备；当前选择 {selected}{hover}";
    }
}
