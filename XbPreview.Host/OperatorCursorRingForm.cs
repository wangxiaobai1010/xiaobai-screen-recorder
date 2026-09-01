using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace XbPreview.Host;

internal readonly record struct OperatorRingActivationResult(
    bool Succeeded,
    int WindowsErrorCode,
    uint AppliedAffinity);

internal sealed class OperatorCursorRingForm : Form
{
    private const int Diameter = 34;
    private const int WsExTransparent = 0x00000020;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExLayered = 0x00080000;
    private const int WsExNoActivate = 0x08000000;
    private const int WmNcHitTest = 0x0084;
    private const int WmMouseActivate = 0x0021;
    private const int HtTransparent = -1;
    private const int MaNoActivate = 3;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpShowWindow = 0x0040;
    private static readonly nint HwndTopmost = new(-1);

    private readonly System.Windows.Forms.Timer _followTimer;
    private bool _exclusionVerified;

    internal OperatorCursorRingForm()
    {
        AutoScaleMode = AutoScaleMode.None;
        ClientSize = new Size(Diameter, Diameter);
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        BackColor = Color.Magenta;
        TransparencyKey = Color.Magenta;
        Opacity = 0.72;
        _followTimer = new System.Windows.Forms.Timer { Interval = 16 };
        _followTimer.Tick += (_, _) => MoveToCursor();
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            CreateParams parameters = base.CreateParams;
            parameters.ExStyle |= WsExTransparent |
                WsExToolWindow |
                WsExLayered |
                WsExNoActivate;
            return parameters;
        }
    }

    internal OperatorRingActivationResult VerifyCaptureExclusion()
    {
        if (_exclusionVerified)
        {
            return new OperatorRingActivationResult(
                true,
                0,
                WindowDisplayAffinity.ExcludeFromCapture);
        }

        CreateControl();
        nint window = Handle;
        WindowDisplayAffinityResult set =
            WindowDisplayAffinity.TryExclude(window);
        if (!set.Succeeded)
        {
            return new OperatorRingActivationResult(
                false, set.WindowsErrorCode, 0);
        }

        WindowDisplayAffinityResult read =
            WindowDisplayAffinity.TryRead(window, out uint affinity);
        _exclusionVerified = read.Succeeded &&
            affinity == WindowDisplayAffinity.ExcludeFromCapture;
        return new OperatorRingActivationResult(
            _exclusionVerified,
            read.WindowsErrorCode,
            affinity);
    }

    internal OperatorRingActivationResult ShowForOperator()
    {
        OperatorRingActivationResult exclusion = VerifyCaptureExclusion();
        if (!exclusion.Succeeded)
        {
            HideFromOperator();
            return exclusion;
        }

        MoveToCursor();
        if (!Visible)
        {
            Show();
        }
        _followTimer.Start();
        return exclusion;
    }

    internal void HideFromOperator()
    {
        _followTimer.Stop();
        Hide();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using Pen pen = new(Color.FromArgb(255, 238, 167, 92), 2.5f);
        e.Graphics.DrawEllipse(pen, 2.5f, 2.5f, Diameter - 5.0f, Diameter - 5.0f);
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == WmNcHitTest)
        {
            message.Result = HtTransparent;
            return;
        }
        if (message.Msg == WmMouseActivate)
        {
            message.Result = MaNoActivate;
            return;
        }
        base.WndProc(ref message);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _followTimer.Dispose();
        }
        base.Dispose(disposing);
    }

    private void MoveToCursor()
    {
        Point cursor = Cursor.Position;
        _ = SetWindowPos(
            Handle,
            HwndTopmost,
            cursor.X - Diameter / 2,
            cursor.Y - Diameter / 2,
            Diameter,
            Diameter,
            SwpNoActivate | SwpShowWindow);
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        nint window,
        nint insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);
}
