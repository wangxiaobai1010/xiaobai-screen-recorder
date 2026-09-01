using System.Diagnostics;
using XbPreview.Avalonia.Views;
using XbPreview.Avalonia.Localization;

namespace XbPreview.Host;

/// <summary>
/// Mechanical extraction of the frozen MainForm capture-target switch order.
/// Capture target state remains owned by PreviewLifecycleController.
/// </summary>
internal sealed class CaptureTargetCoordinator : IStructuralCaptureCommands
{
    private readonly PreviewLifecycleController _lifecycle;
    private readonly FixedTargetCameraController _cameraController;
    private readonly ProductState _productState;
    private readonly Func<bool> _isClosing;

    internal CaptureTargetCoordinator(
        PreviewLifecycleController lifecycle,
        FixedTargetCameraController cameraController,
        ProductState productState,
        Func<bool> isClosing)
    {
        _lifecycle = lifecycle ??
            throw new ArgumentNullException(nameof(lifecycle));
        _cameraController = cameraController ??
            throw new ArgumentNullException(nameof(cameraController));
        _productState = productState ??
            throw new ArgumentNullException(nameof(productState));
        _isClosing = isClosing ??
            throw new ArgumentNullException(nameof(isClosing));
    }

    public StructuralCaptureTargetPresentation CurrentTarget =>
        Map(_lifecycle.CurrentCaptureTarget);

    public async Task<IReadOnlyList<StructuralCaptureWindowChoice>>
        EnumerateWindowsAsync()
    {
        IReadOnlyList<WindowCaptureChoice> windows = await Task.Run(
            WindowCaptureSelector.Enumerate);
        return windows
            .Select(static choice => new StructuralCaptureWindowChoice(
                choice.Handle,
                choice.Title))
            .ToArray();
    }

    public Task<StructuralCaptureCommandResult> SetFullScreenAsync() =>
        SwitchCaptureTargetAsync(CaptureTarget.FullScreen);

    public Task<StructuralCaptureCommandResult> SetWindowAsync(
        StructuralCaptureWindowChoice choice) =>
        SwitchCaptureTargetAsync(new CaptureTarget(
            CaptureTargetKind.Window,
            choice.Handle,
            choice.Title));

    private async Task<StructuralCaptureCommandResult> SwitchCaptureTargetAsync(
        CaptureTarget target)
    {
        if (_isClosing())
        {
            return Rejected(Strings.Get("CaptureWindowClosing"));
        }

        // The structural route has no Director input owner, but preserves the
        // frozen switch rule that Director camera ownership is released first.
        _ = _cameraController.SetDirectorLiteEnabled(
            false,
            Stopwatch.GetTimestamp(),
            out _);

        if (_lifecycle.State is PreviewLifecycleState.Previewing or
            PreviewLifecycleState.Error)
        {
            PreviewLifecycleResult stop = await _lifecycle.StopAsync();
            if (!stop.Succeeded)
            {
                return Rejected(
                    Strings.Format("StopPreviewFailed", stop.Error));
            }
        }

        PreviewLifecycleResult configured =
            await _lifecycle.SetCaptureTargetAsync(target);
        if (!configured.Succeeded)
        {
            return Rejected(Strings.Format("WindowTargetRejected", configured.Error));
        }

        CaptureDisplaySnapshot display =
            new DisplayGeometryProvider().ReadPrimaryDisplay();
        SessionGeometry baseGeometry =
            SessionGeometry.CreateFullScreen(display);
        SessionGeometry resolvedGeometry =
            RecordingResolutionPolicy.CreatePlan(
                _productState.Current.RecordingResolutionMode,
                baseGeometry).Geometry;
        PreviewLifecycleResult geometry =
            await _lifecycle.SetDesiredGeometryAsync(
                resolvedGeometry);
        if (!geometry.Succeeded)
        {
            return Rejected(Strings.Format("CaptureCanvasRejected", geometry.Error));
        }

        PreviewLifecycleResult start = await _lifecycle.StartAsync(
            cameraEnabled: true,
            followEnabled: false,
            NativeMethods.CursorMode.SystemCursor);
        if (!start.Succeeded)
        {
            return Rejected(Strings.Format("StartPreviewFailed", start.Error));
        }

        return StructuralCaptureCommandResult.Success(target.IsWindow
            ? Strings.Format("CurrentCaptureWindow", target.Title)
            : Strings.Get("CurrentCaptureFullScreen"));
    }

    private static StructuralCaptureTargetPresentation Map(
        CaptureTarget target) => new(
            target.IsWindow,
            target.WindowHandle,
            target.Title);

    private static StructuralCaptureCommandResult Rejected(string detail) =>
        StructuralCaptureCommandResult.Rejected(detail);
}
