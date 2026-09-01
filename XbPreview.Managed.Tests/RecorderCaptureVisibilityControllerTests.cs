using XbPreview.Host;

namespace XbPreview.Managed.Tests;

internal static class RecorderCaptureVisibilityControllerTests
{
    internal static void Run()
    {
        OffExcludesMainAndFloatingInEveryStablePhase();
        OnIdleRestoresWholeRecorderCapture();
        StartTransitionExcludesMainBeforeFloatingAndPreservesIntent();
        PausedRuntimeToggleOnlyChangesFloating();
        StopBackToIdleRestoresWholeRecorderCapture();
        NewAndRecreatedWindowsInheritRolePolicy();
    }

    private static void OffExcludesMainAndFloatingInEveryStablePhase()
    {
        FakeAffinityPlatform platform = new();
        using RecorderCaptureVisibilityController controller = new(platform);
        using TestForm main = new();
        using TestForm floating = new();
        using IDisposable mainRegistration = controller.RegisterTopLevelWindow(
            main,
            RecorderCaptureWindowRole.MainRecorderWindow);
        using IDisposable floatingRegistration =
            controller.RegisterTopLevelWindow(
                floating,
                RecorderCaptureWindowRole.FloatingTray);
        _ = main.Handle;
        _ = floating.Handle;

        foreach (RecorderCapturePhase phase in new[]
                 {
                     RecorderCapturePhase.Idle,
                     RecorderCapturePhase.Recording,
                     RecorderCapturePhase.Paused,
                 })
        {
            Require(controller.TrySetRecordingPhase(phase).Succeeded,
                $"TrayInFrame OFF applies in {phase}");
            RequireAffinity(
                platform,
                main,
                WindowDisplayAffinity.ExcludeFromCapture);
            RequireAffinity(
                platform,
                floating,
                WindowDisplayAffinity.ExcludeFromCapture);
        }
    }

    private static void OnIdleRestoresWholeRecorderCapture()
    {
        FakeAffinityPlatform platform = new();
        using RecorderCaptureVisibilityController controller = new(platform);
        using TestForm main = new();
        using TestForm floating = new();
        using Form mainPopup = new() { Owner = main };
        using Form floatingPopup = new() { Owner = floating };
        using Form unresolvedOwner = new();
        using Form unresolvedPopup = new() { Owner = unresolvedOwner };
        using IDisposable mainRegistration = controller.RegisterTopLevelWindow(
            main,
            RecorderCaptureWindowRole.MainRecorderWindow);
        using IDisposable floatingRegistration =
            controller.RegisterTopLevelWindow(
                floating,
                RecorderCaptureWindowRole.FloatingTray);
        _ = main.Handle;
        _ = floating.Handle;
        _ = mainPopup.Handle;
        _ = floatingPopup.Handle;
        _ = unresolvedOwner.Handle;
        _ = unresolvedPopup.Handle;

        RecorderCaptureVisibilityResult enabled =
            controller.TrySetTrayInFrame(true);
        Require(enabled.Succeeded && controller.TrayInFrame &&
            controller.Phase == RecorderCapturePhase.Idle,
            "Idle TrayInFrame ON restores the single user intent");
        RequireAffinity(platform, main, WindowDisplayAffinity.AllowCapture);
        RequireAffinity(platform, floating, WindowDisplayAffinity.AllowCapture);
        RequireAffinity(platform, mainPopup, WindowDisplayAffinity.AllowCapture);
        RequireAffinity(
            platform,
            floatingPopup,
            WindowDisplayAffinity.AllowCapture);
        RequireAffinity(
            platform,
            unresolvedPopup,
            WindowDisplayAffinity.ExcludeFromCapture);
    }

    private static void
        StartTransitionExcludesMainBeforeFloatingAndPreservesIntent()
    {
        FakeAffinityPlatform platform = new();
        using RecorderCaptureVisibilityController controller = new(platform);
        using TestForm main = new();
        using TestForm floating = new();
        using IDisposable mainRegistration = controller.RegisterTopLevelWindow(
            main,
            RecorderCaptureWindowRole.MainRecorderWindow);
        using IDisposable floatingRegistration =
            controller.RegisterTopLevelWindow(
                floating,
                RecorderCaptureWindowRole.FloatingTray);
        _ = main.Handle;
        _ = floating.Handle;
        Require(controller.TrySetTrayInFrame(true).Succeeded,
            "Start-transition setup enables TrayInFrame");

        platform.Operations.Clear();
        RecorderCaptureVisibilityResult starting =
            controller.TrySetRecordingPhase(RecorderCapturePhase.Starting);
        Require(starting.Succeeded && controller.TrayInFrame,
            "Start transition preserves TrayInFrame ON");
        RequireAffinity(
            platform,
            main,
            WindowDisplayAffinity.ExcludeFromCapture);
        RequireAffinity(platform, floating, WindowDisplayAffinity.AllowCapture);
        int mainReadback = platform.Operations.FindIndex(operation =>
            operation == $"read:{main.Handle}");
        int floatingAllow = platform.Operations.FindIndex(operation =>
            operation == $"set:{floating.Handle}:00000000");
        Require(mainReadback >= 0 && floatingAllow > mainReadback,
            "Main exclusion readback completes before floating remains capturable");

        Require(controller.TrySetRecordingPhase(
                RecorderCapturePhase.Idle).Succeeded,
            "Failed-start setup returns to Idle");
        platform.FailNextReadHandle = main.Handle;
        starting = controller.TrySetRecordingPhase(
            RecorderCapturePhase.Starting);
        Require(!starting.Succeeded && controller.TrayInFrame &&
            starting.TrayInFrame &&
            controller.Phase == RecorderCapturePhase.Starting,
            "A failed start gate preserves TrayInFrame and fails closed");
        RequireAffinity(
            platform,
            floating,
            WindowDisplayAffinity.ExcludeFromCapture);
    }

    private static void PausedRuntimeToggleOnlyChangesFloating()
    {
        FakeAffinityPlatform platform = new();
        using RecorderCaptureVisibilityController controller = new(platform);
        using TestForm main = new();
        using TestForm floating = new();
        using IDisposable mainRegistration = controller.RegisterTopLevelWindow(
            main,
            RecorderCaptureWindowRole.MainRecorderWindow);
        using IDisposable floatingRegistration =
            controller.RegisterTopLevelWindow(
                floating,
                RecorderCaptureWindowRole.FloatingTray);
        _ = main.Handle;
        _ = floating.Handle;
        Require(controller.TrySetRecordingPhase(
                RecorderCapturePhase.Paused).Succeeded,
            "Paused phase applies");

        Require(controller.TrySetTrayInFrame(true).Succeeded,
            "Paused runtime toggle ON succeeds");
        RequireAffinity(
            platform,
            main,
            WindowDisplayAffinity.ExcludeFromCapture);
        RequireAffinity(platform, floating, WindowDisplayAffinity.AllowCapture);

        Require(controller.TrySetTrayInFrame(false).Succeeded,
            "Paused runtime toggle OFF succeeds");
        RequireAffinity(
            platform,
            main,
            WindowDisplayAffinity.ExcludeFromCapture);
        RequireAffinity(
            platform,
            floating,
            WindowDisplayAffinity.ExcludeFromCapture);

        Require(controller.TrySetTrayInFrame(true).Succeeded,
            "Paused runtime toggle can return ON");
        RequireAffinity(
            platform,
            main,
            WindowDisplayAffinity.ExcludeFromCapture);
        RequireAffinity(platform, floating, WindowDisplayAffinity.AllowCapture);
    }

    private static void StopBackToIdleRestoresWholeRecorderCapture()
    {
        FakeAffinityPlatform platform = new();
        using RecorderCaptureVisibilityController controller = new(platform);
        using TestForm main = new();
        using TestForm floating = new();
        using IDisposable mainRegistration = controller.RegisterTopLevelWindow(
            main,
            RecorderCaptureWindowRole.MainRecorderWindow);
        using IDisposable floatingRegistration =
            controller.RegisterTopLevelWindow(
                floating,
                RecorderCaptureWindowRole.FloatingTray);
        _ = main.Handle;
        _ = floating.Handle;
        Require(controller.TrySetTrayInFrame(true).Succeeded,
            "Stop-transition setup enables TrayInFrame");
        Require(controller.TrySetRecordingPhase(
                RecorderCapturePhase.Recording).Succeeded,
            "Recording phase applies");
        Require(controller.TrySetRecordingPhase(
                RecorderCapturePhase.Stopping).Succeeded,
            "Stopping remains fail-closed for main");
        RequireAffinity(
            platform,
            main,
            WindowDisplayAffinity.ExcludeFromCapture);
        RequireAffinity(platform, floating, WindowDisplayAffinity.AllowCapture);

        Require(controller.TrySetRecordingPhase(
                RecorderCapturePhase.Unstable).Succeeded,
            "Completion summary remains fail-closed for main");
        RequireAffinity(
            platform,
            main,
            WindowDisplayAffinity.ExcludeFromCapture);
        Require(controller.TrySetRecordingPhase(
                RecorderCapturePhase.Idle).Succeeded,
            "True return to recording preparation reapplies Idle policy");
        Require(controller.TrayInFrame,
            "Return to Idle does not mutate TrayInFrame");
        RequireAffinity(platform, main, WindowDisplayAffinity.AllowCapture);
        RequireAffinity(platform, floating, WindowDisplayAffinity.AllowCapture);
    }

    private static void NewAndRecreatedWindowsInheritRolePolicy()
    {
        FakeAffinityPlatform platform = new();
        using RecorderCaptureVisibilityController controller = new(platform);
        using TestForm main = new();
        using IDisposable mainRegistration = controller.RegisterTopLevelWindow(
            main,
            RecorderCaptureWindowRole.MainRecorderWindow);
        _ = main.Handle;
        Require(controller.TrySetTrayInFrame(true).Succeeded,
            "TrayInFrame ON is accepted before a floating tray exists");
        RequireAffinity(platform, main, WindowDisplayAffinity.AllowCapture);

        using TestForm floating = new();
        using IDisposable floatingRegistration =
            controller.RegisterTopLevelWindow(
                floating,
                RecorderCaptureWindowRole.FloatingTray);
        _ = floating.Handle;
        RequireAffinity(platform, floating, WindowDisplayAffinity.AllowCapture);

        Require(controller.TrySetRecordingPhase(
                RecorderCapturePhase.Recording).Succeeded,
            "Recording policy applies before HWND recreation");
        floating.RecreateNativeHandle();
        RequireAffinity(platform, floating, WindowDisplayAffinity.AllowCapture);
        main.RecreateNativeHandle();
        RequireAffinity(
            platform,
            main,
            WindowDisplayAffinity.ExcludeFromCapture);
        Require(controller.TrayInFrame,
            "Existing, new, and recreated HWNDs preserve the single intent");

        Require(controller.TrySetTrayInFrame(false).Succeeded,
            "Recreated-window policy can return OFF");
        RequireAffinity(
            platform,
            main,
            WindowDisplayAffinity.ExcludeFromCapture);
        RequireAffinity(
            platform,
            floating,
            WindowDisplayAffinity.ExcludeFromCapture);
    }

    private static void RequireAffinity(
        FakeAffinityPlatform platform,
        Form form,
        uint expected)
    {
        Require(platform.Affinities.TryGetValue(form.Handle, out uint actual) &&
            actual == expected,
            $"HWND {form.Handle} affinity expected 0x{expected:X8}, " +
                $"actual 0x{actual:X8}");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed class TestForm : Form
    {
        internal void RecreateNativeHandle() => RecreateHandle();
    }

    private sealed class FakeAffinityPlatform :
        IRecorderCaptureAffinityPlatform
    {
        internal Dictionary<nint, uint> Affinities { get; } = [];

        internal List<string> Operations { get; } = [];

        internal nint FailNextSetHandle { get; set; }

        internal nint FailNextReadHandle { get; set; }

        public WindowDisplayAffinityResult TrySet(
            nint windowHandle,
            uint affinity)
        {
            Operations.Add($"set:{windowHandle}:{affinity:X8}");
            if (FailNextSetHandle == windowHandle)
            {
                FailNextSetHandle = nint.Zero;
                return new WindowDisplayAffinityResult(false, 5);
            }

            Affinities[windowHandle] = affinity;
            return new WindowDisplayAffinityResult(true, 0);
        }

        public WindowDisplayAffinityResult TryRead(
            nint windowHandle,
            out uint affinity)
        {
            Operations.Add($"read:{windowHandle}");
            Affinities.TryGetValue(windowHandle, out affinity);
            if (FailNextReadHandle == windowHandle)
            {
                FailNextReadHandle = nint.Zero;
                return new WindowDisplayAffinityResult(false, 5);
            }

            return new WindowDisplayAffinityResult(
                Affinities.ContainsKey(windowHandle),
                Affinities.ContainsKey(windowHandle) ? 0 : 87);
        }
    }
}
