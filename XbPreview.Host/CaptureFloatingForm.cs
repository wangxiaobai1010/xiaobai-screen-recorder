using System.Runtime.InteropServices;
using Avalonia.Win32.Interoperability;
using XbPreview.Avalonia.Views.Panels;

namespace XbPreview.Host;

internal sealed class CaptureNativeMovingEventArgs(
    System.Drawing.Rectangle windowBounds) : EventArgs
{
    internal System.Drawing.Rectangle WindowBounds { get; } = windowBounds;
}

/// <summary>
/// A real, borderless, resizable WinForms top-level HWND whose only visible
/// content is a second live Avalonia Capture view.
/// </summary>
internal sealed class CaptureFloatingForm : Form
{
    private const int WmNcHitTest = 0x0084;
    private const int WmNcLeftButtonDown = 0x00A1;
    private const int WmMoving = 0x0216;
    private const int WmEnterSizeMove = 0x0231;
    private const int WmExitSizeMove = 0x0232;
    private const int HtClient = 1;
    private const int HtCaption = 2;
    private const int HtLeft = 10;
    private const int HtRight = 11;
    private const int HtTop = 12;
    private const int HtTopLeft = 13;
    private const int HtTopRight = 14;
    private const int HtBottom = 15;
    private const int HtBottomLeft = 16;
    private const int HtBottomRight = 17;
    private const int DwmWindowCornerPreference = 33;
    private const int DwmWindowCornerRound = 2;
    private const int WsExToolWindow = 0x0000_0080;
    private const int SmCxSizeFrame = 32;
    private const int SmCySizeFrame = 33;
    private const int SmCxPaddedBorder = 92;

    private readonly WinFormsAvaloniaControlHost _avaloniaHost;
    private readonly Dictionary<int, CaptureResizeGrip> _resizeGrips = [];
    private bool _nativeMoveActive;
    private bool _nativeMoveEntered;
    private bool _nativeMoveExited;
    private bool _allowClose;

    internal CaptureFloatingForm(
        CapturePanelView captureView,
        System.Drawing.Rectangle initialBounds)
    {
        ArgumentNullException.ThrowIfNull(captureView);
        if (initialBounds.Width <= 0 || initialBounds.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(initialBounds),
                "Capture Home bounds must have a positive size.");
        }

        Text = "录制准备";
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = System.Drawing.Color.White;
        FormBorderStyle = FormBorderStyle.None;
        ShowIcon = false;
        ShowInTaskbar = false;
        TopMost = true;
        ControlBox = false;
        MinimizeBox = false;
        MaximizeBox = false;
        StartPosition = FormStartPosition.Manual;
        Bounds = initialBounds;

        _avaloniaHost = new WinFormsAvaloniaControlHost
        {
            Dock = DockStyle.Fill,
            Content = captureView,
        };
        Controls.Add(_avaloniaHost);
        _avaloniaHost.BringToFront();
        InstallResizeGrips();
    }

    internal event EventHandler<CaptureNativeMovingEventArgs>? NativeMoving;

    internal event EventHandler? NativeMoveExited;

    internal event EventHandler? ReturnHomeRequested;

    protected override CreateParams CreateParams
    {
        get
        {
            CreateParams parameters = base.CreateParams;
            parameters.ExStyle |= WsExToolWindow;
            return parameters;
        }
    }

    internal bool BeginNativeMove()
    {
        if (IsDisposed || !IsHandleCreated || !Visible)
        {
            return false;
        }

        _nativeMoveActive = true;
        _nativeMoveEntered = false;
        _nativeMoveExited = false;
        Activate();
        System.Drawing.Point screenPoint = Cursor.Position;
        _ = ReleaseCapture();
        _ = SendMessageW(
            Handle,
            WmNcLeftButtonDown,
            (nint)HtCaption,
            PackScreenPoint(screenPoint));
        bool completed = _nativeMoveEntered && _nativeMoveExited;
        _nativeMoveActive = false;
        return completed;
    }

    internal void CloseForReturnHome()
    {
        _allowClose = true;
        if (!IsDisposed)
        {
            Close();
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        int preference = DwmWindowCornerRound;
        _ = DwmSetWindowAttribute(
            Handle,
            DwmWindowCornerPreference,
            ref preference,
            sizeof(int));
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_allowClose && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            ReturnHomeRequested?.Invoke(this, EventArgs.Empty);
            return;
        }
        base.OnFormClosing(e);
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == WmNcHitTest)
        {
            base.WndProc(ref message);
            if (message.Result == (nint)HtClient)
            {
                System.Drawing.Point screenPoint = UnpackScreenPoint(
                    message.LParam);
                System.Drawing.Point clientPoint = PointToClient(screenPoint);
                message.Result = (nint)HitTestResizeBorder(clientPoint);
            }
            return;
        }

        if (message.Msg == WmMoving && message.LParam != nint.Zero)
        {
            NativeRect nativeBounds = Marshal.PtrToStructure<NativeRect>(
                message.LParam);
            NativeMoving?.Invoke(
                this,
                new CaptureNativeMovingEventArgs(
                    System.Drawing.Rectangle.FromLTRB(
                        nativeBounds.Left,
                        nativeBounds.Top,
                        nativeBounds.Right,
                        nativeBounds.Bottom)));
        }

        if (message.Msg == WmEnterSizeMove && _nativeMoveActive)
        {
            _nativeMoveEntered = true;
        }

        base.WndProc(ref message);

        if (message.Msg == WmExitSizeMove && _nativeMoveActive)
        {
            _nativeMoveExited = true;
            _nativeMoveActive = false;
            NativeMoveExited?.Invoke(this, EventArgs.Empty);
        }
    }

    private void InstallResizeGrips()
    {
        AddResizeGrip(HtLeft, Cursors.SizeWE);
        AddResizeGrip(HtRight, Cursors.SizeWE);
        AddResizeGrip(HtTop, Cursors.SizeNS);
        AddResizeGrip(HtBottom, Cursors.SizeNS);
        AddResizeGrip(HtTopLeft, Cursors.SizeNWSE);
        AddResizeGrip(HtTopRight, Cursors.SizeNESW);
        AddResizeGrip(HtBottomLeft, Cursors.SizeNESW);
        AddResizeGrip(HtBottomRight, Cursors.SizeNWSE);
        _avaloniaHost.SizeChanged += (_, _) => LayoutResizeGrips();
        LayoutResizeGrips();
    }

    private void AddResizeGrip(int hitTest, Cursor cursor)
    {
        CaptureResizeGrip grip = new(cursor);
        grip.MouseDown += (_, e) => BeginNativeResize(hitTest, e);
        _resizeGrips.Add(hitTest, grip);
        _avaloniaHost.Controls.Add(grip);
        grip.BringToFront();
    }

    private void LayoutResizeGrips()
    {
        if (_resizeGrips.Count != 8 ||
            _avaloniaHost.ClientSize.Width < 1 ||
            _avaloniaHost.ClientSize.Height < 1)
        {
            return;
        }

        (int horizontal, int vertical) = ReadCurrentResizeBorderSize();
        int cornerWidth = Math.Max(horizontal, horizontal * 2);
        int cornerHeight = Math.Max(vertical, vertical * 2);
        int width = _avaloniaHost.ClientSize.Width;
        int height = _avaloniaHost.ClientSize.Height;

        SetResizeGripBounds(HtTopLeft, 0, 0, cornerWidth, cornerHeight);
        SetResizeGripBounds(
            HtTopRight,
            width - cornerWidth,
            0,
            cornerWidth,
            cornerHeight);
        SetResizeGripBounds(
            HtBottomLeft,
            0,
            height - cornerHeight,
            cornerWidth,
            cornerHeight);
        SetResizeGripBounds(
            HtBottomRight,
            width - cornerWidth,
            height - cornerHeight,
            cornerWidth,
            cornerHeight);
        SetResizeGripBounds(
            HtTop,
            cornerWidth,
            0,
            width - (cornerWidth * 2),
            vertical);
        SetResizeGripBounds(
            HtBottom,
            cornerWidth,
            height - vertical,
            width - (cornerWidth * 2),
            vertical);
        SetResizeGripBounds(
            HtLeft,
            0,
            cornerHeight,
            horizontal,
            height - (cornerHeight * 2));
        SetResizeGripBounds(
            HtRight,
            width - horizontal,
            cornerHeight,
            horizontal,
            height - (cornerHeight * 2));
    }

    private void SetResizeGripBounds(
        int hitTest,
        int x,
        int y,
        int width,
        int height)
    {
        _resizeGrips[hitTest].Bounds = new System.Drawing.Rectangle(
            Math.Max(0, x),
            Math.Max(0, y),
            Math.Max(1, width),
            Math.Max(1, height));
    }

    private void BeginNativeResize(int hitTest, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left || IsDisposed ||
            !IsHandleCreated || WindowState != FormWindowState.Normal)
        {
            return;
        }

        _nativeMoveActive = false;
        _ = ReleaseCapture();
        _ = SendMessageW(
            Handle,
            WmNcLeftButtonDown,
            (nint)hitTest,
            PackScreenPoint(Cursor.Position));
    }

    private (int Horizontal, int Vertical) ReadCurrentResizeBorderSize()
    {
        uint dpi = IsHandleCreated ? GetDpiForWindow(Handle) : 0;
        if (dpi == 0)
        {
            dpi = unchecked((uint)Math.Max(96, DeviceDpi));
        }
        int padded = GetSystemMetricsForDpi(SmCxPaddedBorder, dpi);
        int horizontal = GetSystemMetricsForDpi(SmCxSizeFrame, dpi) + padded;
        int vertical = GetSystemMetricsForDpi(SmCySizeFrame, dpi) + padded;
        if (horizontal <= 0 || vertical <= 0)
        {
            horizontal = SystemInformation.FrameBorderSize.Width;
            vertical = SystemInformation.FrameBorderSize.Height;
        }
        return (Math.Max(1, horizontal), Math.Max(1, vertical));
    }

    private int HitTestResizeBorder(System.Drawing.Point clientPoint)
    {
        (int horizontalBorder, int verticalBorder) =
            ReadCurrentResizeBorderSize();
        bool left = clientPoint.X >= 0 &&
            clientPoint.X < horizontalBorder;
        bool right = clientPoint.X < ClientSize.Width &&
            clientPoint.X >= ClientSize.Width - horizontalBorder;
        bool top = clientPoint.Y >= 0 &&
            clientPoint.Y < verticalBorder;
        bool bottom = clientPoint.Y < ClientSize.Height &&
            clientPoint.Y >= ClientSize.Height - verticalBorder;

        if (top && left)
        {
            return HtTopLeft;
        }
        if (top && right)
        {
            return HtTopRight;
        }
        if (bottom && left)
        {
            return HtBottomLeft;
        }
        if (bottom && right)
        {
            return HtBottomRight;
        }
        if (left)
        {
            return HtLeft;
        }
        if (right)
        {
            return HtRight;
        }
        if (top)
        {
            return HtTop;
        }
        return bottom ? HtBottom : HtClient;
    }

    private static nint PackScreenPoint(System.Drawing.Point point)
    {
        uint packed = unchecked((uint)(ushort)point.X) |
            (unchecked((uint)(ushort)point.Y) << 16);
        return unchecked((nint)packed);
    }

    private static System.Drawing.Point UnpackScreenPoint(nint packed)
    {
        long value = packed.ToInt64();
        return new System.Drawing.Point(
            unchecked((short)(value & 0xffff)),
            unchecked((short)((value >> 16) & 0xffff)));
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct NativeRect
    {
        internal readonly int Left;
        internal readonly int Top;
        internal readonly int Right;
        internal readonly int Bottom;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint SendMessageW(
        nint window,
        int message,
        nint wParam,
        nint lParam);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint window);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetricsForDpi(int index, uint dpi);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        nint window,
        int attribute,
        ref int value,
        int valueSize);
}

internal sealed class CaptureResizeGrip : Control
{
    internal CaptureResizeGrip(Cursor cursor)
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.SupportsTransparentBackColor |
            ControlStyles.UserPaint,
            true);
        SetStyle(ControlStyles.Selectable, false);
        BackColor = System.Drawing.Color.Transparent;
        Cursor = cursor;
        TabStop = false;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
    }
}
