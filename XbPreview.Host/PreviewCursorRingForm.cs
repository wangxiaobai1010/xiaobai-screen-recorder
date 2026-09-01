using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace XbPreview.Host;

/// <summary>
/// Capture-excluded presentation copy of the frozen operator cursor ring.
/// It is an owned top-level window so it can appear over the GPU Preview
/// without becoming part of OutputCanvas or any encoder input.
/// </summary>
internal sealed class PreviewCursorRingForm : Form
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
    private static readonly nint HwndTop = nint.Zero;
    private bool _captureExclusionVerified;

    internal PreviewCursorRingForm()
    {
        AutoScaleMode = AutoScaleMode.None;
        ClientSize = new Size(Diameter, Diameter);
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.Magenta;
        TransparencyKey = Color.Magenta;
        Opacity = 0.72;
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

    internal OperatorRingActivationResult VerifyCaptureExclusion(
        bool force = false)
    {
        if (_captureExclusionVerified && !force)
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
            _captureExclusionVerified = false;
            return new OperatorRingActivationResult(
                false,
                set.WindowsErrorCode,
                0);
        }

        WindowDisplayAffinityResult read =
            WindowDisplayAffinity.TryRead(window, out uint affinity);
        _captureExclusionVerified = read.Succeeded &&
            affinity == WindowDisplayAffinity.ExcludeFromCapture;
        return new OperatorRingActivationResult(
            _captureExclusionVerified,
            read.WindowsErrorCode,
            affinity);
    }

    internal OperatorRingActivationResult ShowAt(
        IWin32Window owner,
        Point screenCenter)
    {
        OperatorRingActivationResult exclusion = VerifyCaptureExclusion();
        if (!exclusion.Succeeded)
        {
            HideRing();
            return exclusion;
        }

        if (!Visible)
        {
            Show(owner);
        }
        _ = SetWindowPos(
            Handle,
            HwndTop,
            screenCenter.X - Diameter / 2,
            screenCenter.Y - Diameter / 2,
            Diameter,
            Diameter,
            SwpNoActivate | SwpShowWindow);
        return exclusion;
    }

    internal void HideRing() => Hide();

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using Pen pen = new(Color.FromArgb(255, 238, 167, 92), 2.5f);
        e.Graphics.DrawEllipse(
            pen,
            2.5f,
            2.5f,
            Diameter - 5.0f,
            Diameter - 5.0f);
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
