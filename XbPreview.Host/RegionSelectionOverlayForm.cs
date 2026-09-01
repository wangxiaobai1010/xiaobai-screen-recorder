using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace XbPreview.Host;

internal sealed class RegionSelectionOverlayForm : Form
{
    private const int WsExToolWindow = 0x00000080;
    private const int WsExLayered = 0x00080000;
    private const int UlwAlpha = 0x00000002;
    private const byte AcSrcOver = 0x00;
    private const byte AcSrcAlpha = 0x01;
    private const int OutsideMaskAlpha = 166;
    private const int InsideHitAlpha = 2;

    private readonly CaptureDisplaySnapshot _display;
    private readonly DisplayGeometryProvider _displayProvider;
    private readonly RegionSelectionStateMachine _stateMachine = new();
    private CaptureRegion? _selectedRegion;
    private CaptureRegion _operationStartRegion;
    private PhysicalPixelPoint _operationStartPoint;
    private PhysicalPixelPoint _drawAnchor;
    private RegionResizeHandle _activeHandle;
    private RegionAspectMode _aspectMode = RegionAspectMode.Free;
    private bool _closingByCommand;

    internal RegionSelectionOverlayForm(
        CaptureDisplaySnapshot display,
        DisplayGeometryProvider displayProvider,
        CaptureRegion? existingRegion)
    {
        _display = display;
        _displayProvider = displayProvider;
        _selectedRegion = existingRegion;

        Text = "选择录制区域遮罩";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;
        Location = new Point(display.DesktopLeft, display.DesktopTop);
        ClientSize = new Size(display.Width, display.Height);
        TopMost = true;
        ShowInTaskbar = false;
        KeyPreview = true;
        Cursor = Cursors.Cross;

        if (_selectedRegion.HasValue)
        {
            _ = _stateMachine.TryTransition(RegionSelectionState.Drawing);
            _ = _stateMachine.TryTransition(RegionSelectionState.Selected);
        }
    }

    internal event EventHandler? VisualStateChanged;
    internal event EventHandler? InteractionStarted;

    internal CaptureRegion? SelectedRegion => _selectedRegion;
    internal bool HasSelection =>
        RegionSelectionAvailability.HasSelection(
            _selectedRegion,
            _stateMachine.State);
    internal RegionSelectionState SelectionState => _stateMachine.State;
    internal RegionAspectMode AspectMode => _aspectMode;
    internal bool DisplayChanged { get; private set; }
    internal WindowDisplayAffinityResult WdaResult { get; private set; }

    protected override CreateParams CreateParams
    {
        get
        {
            CreateParams parameters = base.CreateParams;
            parameters.ExStyle |= WsExToolWindow | WsExLayered;
            return parameters;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        WdaResult = WindowDisplayAffinity.TryExclude(Handle);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        RefreshLayeredSurface();
        VisualStateChanged?.Invoke(this, EventArgs.Empty);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        Keys keyCode = keyData & Keys.KeyCode;
        if (keyCode == Keys.Escape)
        {
            CancelSelection();
            return true;
        }
        if (keyCode == Keys.Enter)
        {
            ConfirmSelection();
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left)
        {
            return;
        }

        InteractionStarted?.Invoke(this, EventArgs.Empty);
        PhysicalPixelPoint point = ReadPhysicalPoint();
        _operationStartPoint = point;
        _activeHandle = _selectedRegion.HasValue
            ? RegionSelectionMath.HitTest(
                _selectedRegion.Value,
                point,
                HandleRadiusPixels)
            : RegionResizeHandle.None;

        if (_activeHandle == RegionResizeHandle.Move)
        {
            _operationStartRegion = _selectedRegion!.Value;
            if (!_stateMachine.TryTransition(RegionSelectionState.Moving))
            {
                return;
            }
        }
        else if (_activeHandle != RegionResizeHandle.None)
        {
            _operationStartRegion = _selectedRegion!.Value;
            if (!_stateMachine.TryTransition(RegionSelectionState.Resizing))
            {
                return;
            }
        }
        else
        {
            _drawAnchor = point;
            _selectedRegion = null;
            if (!_stateMachine.TryTransition(RegionSelectionState.Drawing))
            {
                return;
            }
        }
        Capture = true;
        RefreshVisuals();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        PhysicalPixelPoint point = ReadPhysicalPoint();
        if (!Capture || e.Button != MouseButtons.Left)
        {
            UpdatePointerCursor(point);
            return;
        }

        switch (_stateMachine.State)
        {
            case RegionSelectionState.Drawing:
                _selectedRegion = RegionSelectionMath.TryCreateFromDrag(
                    _drawAnchor,
                    point,
                    _display.Width,
                    _display.Height,
                    _aspectMode,
                    out CaptureRegion drawn)
                        ? drawn
                        : null;
                break;
            case RegionSelectionState.Moving:
                _selectedRegion = RegionSelectionMath.Move(
                    _operationStartRegion,
                    point.X - _operationStartPoint.X,
                    point.Y - _operationStartPoint.Y,
                    _display.Width,
                    _display.Height);
                break;
            case RegionSelectionState.Resizing:
                _selectedRegion = RegionSelectionMath.Resize(
                    _operationStartRegion,
                    _activeHandle,
                    point,
                    _display.Width,
                    _display.Height,
                    _aspectMode);
                break;
        }
        RefreshVisuals();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button != MouseButtons.Left || !Capture)
        {
            return;
        }
        Capture = false;
        if (_stateMachine.State == RegionSelectionState.Drawing)
        {
            _ = _stateMachine.TryTransition(
                _selectedRegion.HasValue
                    ? RegionSelectionState.Selected
                    : RegionSelectionState.NoSelection);
        }
        else if (_stateMachine.State is
            RegionSelectionState.Moving or RegionSelectionState.Resizing)
        {
            _ = _stateMachine.TryTransition(RegionSelectionState.Selected);
        }
        RefreshVisuals();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_closingByCommand && DialogResult != DialogResult.OK)
        {
            _ = _stateMachine.TryTransition(RegionSelectionState.Cancelled);
            DialogResult = DialogResult.Cancel;
        }
        base.OnFormClosing(e);
    }

    internal void CancelForDisplayChange()
    {
        if (IsDisposed)
        {
            return;
        }
        if (InvokeRequired)
        {
            BeginInvoke(CancelForDisplayChange);
            return;
        }
        DisplayChanged = true;
        CancelSelection();
    }

    internal void SetAspectMode(RegionAspectMode mode)
    {
        _aspectMode = mode;
        if (mode == RegionAspectMode.Ratio16By9 &&
            _selectedRegion is CaptureRegion region)
        {
            _selectedRegion = RegionSelectionMath.FitLargest16By9Inside(
                region,
                _display.Width,
                _display.Height);
        }
        RefreshVisuals();
    }

    internal void BeginNewSelection()
    {
        if (_stateMachine.State == RegionSelectionState.Selected)
        {
            _ = _stateMachine.TryTransition(RegionSelectionState.Drawing);
            _ = _stateMachine.TryTransition(RegionSelectionState.NoSelection);
        }
        _selectedRegion = null;
        RefreshVisuals();
        Focus();
    }

    internal bool TryApplyExactSize(
        string widthText,
        string heightText,
        ExactSizeEditedDimension lastEditedDimension,
        out string? error)
    {
        if (_selectedRegion is not CaptureRegion current)
        {
            error = "请先选择一个录制区域。";
            return false;
        }
        if (!RegionSelectionMath.TryResolveExactSize(
            widthText,
            heightText,
            _aspectMode,
            lastEditedDimension,
            _display.Width,
            _display.Height,
            out int width,
            out int height,
            out error))
        {
            return false;
        }

        _selectedRegion = RegionSelectionMath.ApplyExactSize(
            current,
            width,
            height,
            _display.Width,
            _display.Height);
        RefreshVisuals();
        return true;
    }

    internal void ConfirmSelection()
    {
        if (!HasSelection ||
            !_stateMachine.TryTransition(RegionSelectionState.Confirmed))
        {
            return;
        }
        _closingByCommand = true;
        DialogResult = DialogResult.OK;
        Close();
    }

    internal void CancelSelection()
    {
        if (!_stateMachine.TryTransition(RegionSelectionState.Cancelled) &&
            _stateMachine.State != RegionSelectionState.Cancelled)
        {
            return;
        }
        _closingByCommand = true;
        DialogResult = DialogResult.Cancel;
        Close();
    }

    private PhysicalPixelPoint ReadPhysicalPoint()
    {
        PhysicalPixelPoint point =
            _displayProvider.ReadCursorRelativeTo(_display);
        return new PhysicalPixelPoint(
            Math.Clamp(point.X, 0, _display.Width),
            Math.Clamp(point.Y, 0, _display.Height));
    }

    private void UpdatePointerCursor(PhysicalPixelPoint point)
    {
        RegionResizeHandle handle = _selectedRegion.HasValue
            ? RegionSelectionMath.HitTest(
                _selectedRegion.Value,
                point,
                HandleRadiusPixels)
            : RegionResizeHandle.None;
        Cursor = handle switch
        {
            RegionResizeHandle.Move => Cursors.SizeAll,
            RegionResizeHandle.Left or RegionResizeHandle.Right => Cursors.SizeWE,
            RegionResizeHandle.Top or RegionResizeHandle.Bottom => Cursors.SizeNS,
            RegionResizeHandle.TopLeft or RegionResizeHandle.BottomRight =>
                Cursors.SizeNWSE,
            RegionResizeHandle.TopRight or RegionResizeHandle.BottomLeft =>
                Cursors.SizeNESW,
            _ => Cursors.Cross,
        };
    }

    private void RefreshVisuals()
    {
        if (IsHandleCreated)
        {
            RefreshLayeredSurface();
        }
        VisualStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void RefreshLayeredSurface()
    {
        if (!IsHandleCreated)
        {
            return;
        }

        int surfaceWidth = _display.Width;
        int surfaceHeight = _display.Height;
        using Bitmap surface = new(
            surfaceWidth,
            surfaceHeight,
            PixelFormat.Format32bppPArgb);
        using (Graphics graphics = Graphics.FromImage(surface))
        {
            graphics.CompositingMode = CompositingMode.SourceCopy;
            graphics.Clear(Color.Transparent);
            DrawMask(graphics, surfaceWidth, surfaceHeight);
            DrawSelectionChrome(graphics);
        }

        nint screenDc = GetDC(nint.Zero);
        nint memoryDc = CreateCompatibleDC(screenDc);
        nint bitmap = surface.GetHbitmap(Color.FromArgb(0));
        nint previous = SelectObject(memoryDc, bitmap);
        try
        {
            NativePoint destination = new(
                _display.DesktopLeft,
                _display.DesktopTop);
            NativeSize size = new(surfaceWidth, surfaceHeight);
            NativePoint source = new(0, 0);
            BlendFunction blend = new()
            {
                BlendOp = AcSrcOver,
                BlendFlags = 0,
                SourceConstantAlpha = 255,
                AlphaFormat = AcSrcAlpha,
            };
            if (!UpdateLayeredWindow(
                Handle,
                screenDc,
                ref destination,
                ref size,
                memoryDc,
                ref source,
                0,
                ref blend,
                UlwAlpha))
            {
                throw new Win32Exception(
                    Marshal.GetLastWin32Error(),
                    "Unable to update the region-selection overlay.");
            }
        }
        finally
        {
            _ = SelectObject(memoryDc, previous);
            _ = DeleteObject(bitmap);
            _ = DeleteDC(memoryDc);
            _ = ReleaseDC(nint.Zero, screenDc);
        }
    }

    private void DrawMask(
        Graphics graphics,
        int surfaceWidth,
        int surfaceHeight)
    {
        using Brush outsideMask = new SolidBrush(
            Color.FromArgb(OutsideMaskAlpha, 0, 0, 0));
        if (_selectedRegion is not CaptureRegion region)
        {
            graphics.FillRectangle(
                outsideMask,
                0,
                0,
                surfaceWidth,
                surfaceHeight);
            return;
        }

        graphics.FillRectangle(
            outsideMask,
            0,
            0,
            surfaceWidth,
            region.Top);
        graphics.FillRectangle(
            outsideMask,
            0,
            region.Bottom,
            surfaceWidth,
            Math.Max(0, surfaceHeight - region.Bottom));
        graphics.FillRectangle(
            outsideMask,
            0,
            region.Top,
            region.Left,
            region.Height);
        graphics.FillRectangle(
            outsideMask,
            region.Right,
            region.Top,
            Math.Max(0, surfaceWidth - region.Right),
            region.Height);

        // A tiny non-zero alpha preserves Overlay hit testing while leaving
        // the selected desktop content visually clear.
        using Brush selectedHitSurface = new SolidBrush(
            Color.FromArgb(InsideHitAlpha, 0, 0, 0));
        graphics.FillRectangle(
            selectedHitSurface,
            region.Left,
            region.Top,
            region.Width,
            region.Height);
    }

    private void DrawSelectionChrome(Graphics graphics)
    {
        if (_selectedRegion is not CaptureRegion region)
        {
            return;
        }
        graphics.CompositingMode = CompositingMode.SourceOver;
        graphics.SmoothingMode = SmoothingMode.None;
        Rectangle rectangle = new(
            region.Left,
            region.Top,
            Math.Max(1, region.Width - 1),
            Math.Max(1, region.Height - 1));
        using Pen border = new(Color.FromArgb(255, 255, 214, 48), 3.0f);
        graphics.DrawRectangle(border, rectangle);
        foreach (Point handle in HandlePoints(region))
        {
            Rectangle grip = new(
                handle.X - HandleRadiusPixels,
                handle.Y - HandleRadiusPixels,
                HandleRadiusPixels * 2,
                HandleRadiusPixels * 2);
            graphics.FillRectangle(Brushes.White, grip);
            graphics.DrawRectangle(Pens.Black, grip);
        }
    }

    private int HandleRadiusPixels =>
        Math.Max(6, checked((int)Math.Round(6.0 * DeviceDpi / 96.0)));

    private static IEnumerable<Point> HandlePoints(CaptureRegion region)
    {
        int centerX = region.Left + (region.Width / 2);
        int centerY = region.Top + (region.Height / 2);
        yield return new Point(region.Left, region.Top);
        yield return new Point(centerX, region.Top);
        yield return new Point(region.Right, region.Top);
        yield return new Point(region.Left, centerY);
        yield return new Point(region.Right, centerY);
        yield return new Point(region.Left, region.Bottom);
        yield return new Point(centerX, region.Bottom);
        yield return new Point(region.Right, region.Bottom);
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct NativePoint
    {
        internal readonly int X;
        internal readonly int Y;

        internal NativePoint(int x, int y)
        {
            X = x;
            Y = y;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct NativeSize
    {
        internal readonly int Width;
        internal readonly int Height;

        internal NativeSize(int width, int height)
        {
            Width = width;
            Height = height;
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct BlendFunction
    {
        internal byte BlendOp;
        internal byte BlendFlags;
        internal byte SourceConstantAlpha;
        internal byte AlphaFormat;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UpdateLayeredWindow(
        nint window,
        nint destinationDc,
        ref NativePoint destination,
        ref NativeSize size,
        nint sourceDc,
        ref NativePoint source,
        int colorKey,
        ref BlendFunction blend,
        int flags);

    [DllImport("user32.dll")]
    private static extern nint GetDC(nint window);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(nint window, nint dc);

    [DllImport("gdi32.dll")]
    private static extern nint CreateCompatibleDC(nint dc);

    [DllImport("gdi32.dll")]
    private static extern nint SelectObject(nint dc, nint value);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(nint value);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteDC(nint dc);
}
