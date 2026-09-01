using System.Globalization;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using XbPreview.Host;

namespace XbPreview.Managed.Tests;

internal static class Program
{
    [STAThread]
    private static async Task<int> Main(string[] args)
    {
        try
        {
            if (args.Contains(
                "--localization-v1",
                StringComparer.OrdinalIgnoreCase))
            {
                LocalizationContractTests.Run();
                Console.WriteLine(
                    "XbPreview.Managed.Tests PASS: zh-CN/en localization contracts and shell smoke");
                return 0;
            }

            if (args.Contains(
                "--recovery-root-normal",
                StringComparer.OrdinalIgnoreCase))
            {
                RecoveryRootAlignmentTests.RunNormalCustomRootContract();
                Console.WriteLine(
                    "XbPreview.Managed.Tests PASS: normal custom recovery root");
                return 0;
            }

            if (args.Contains(
                "--recovery-root-remaining",
                StringComparer.OrdinalIgnoreCase))
            {
                RecoveryRootAlignmentTests.RunRemainingRootContracts();
                Console.WriteLine(
                    "XbPreview.Managed.Tests PASS: remaining recovery roots");
                return 0;
            }

            if (args.Contains(
                "--recovery-coordinator-confirmation",
                StringComparer.OrdinalIgnoreCase))
            {
                await RecoveryUiContractTests.
                    RunCoordinatorConfirmationContractAsync();
                Console.WriteLine(
                    "XbPreview.Managed.Tests PASS: recovery confirmation rescan");
                return 0;
            }

            if (args.Contains(
                "--recovery-root-alignment",
                StringComparer.OrdinalIgnoreCase))
            {
                RecoveryRootAlignmentTests.Run();
                Console.WriteLine(
                    "XbPreview.Managed.Tests PASS: Recovery root alignment");
                return 0;
            }

            if (args.Contains(
                "--historical-diagnostic-v1-regression",
                StringComparer.OrdinalIgnoreCase))
            {
                RecoveryRootAlignmentTests.
                    RunLegacyDiagnosticAbiRegression();
                Console.WriteLine(
                    "XbPreview.Managed.Tests PASS: historical diagnostic V1 regression");
                return 0;
            }

            if (args.Contains(
                "--normal-exit-code-contract",
                StringComparer.OrdinalIgnoreCase))
            {
                NormalExitCodeContractTests.Run();
                Console.WriteLine(
                    "XbPreview.Managed.Tests PASS: normal exit code contract");
                return 0;
            }

            if (args.Contains(
                "--recovery-ui-contract",
                StringComparer.OrdinalIgnoreCase))
            {
                await RecoveryUiContractTests.RunAsync();
                Console.WriteLine(
                    "XbPreview.Managed.Tests PASS: formal Recovery UI contract");
                return 0;
            }

            if (args.Contains(
                "--resolution-v1",
                StringComparer.OrdinalIgnoreCase))
            {
                ProductSettingsTests.Run();
                RecordingFixedHomeAdapterTests.Run();
                ResolutionV1ContractTests.Run();
                Console.WriteLine(
                    "XbPreview.Managed.Tests PASS: Resolution v1 managed/UI contracts");
                return 0;
            }

            if (args.Contains(
                "--frame-rate",
                StringComparer.OrdinalIgnoreCase))
            {
                TestExports();
                ProductSettingsTests.Run();
                RecordingFixedHomeAdapterTests.Run();
                FrameRateProductContractTests.Run();
                Console.WriteLine(
                    "XbPreview.Managed.Tests PASS: 30/60 FPS product contracts");
                return 0;
            }

            if (args.Contains(
                "--tray-in-frame",
                StringComparer.OrdinalIgnoreCase))
            {
                RecorderCaptureVisibilityControllerTests.Run();
                TrayInFrameUiContractTests.Run();
                Console.WriteLine(
                    "XbPreview.Managed.Tests PASS: TrayInFrame 7 gates");
                return 0;
            }

            if (args.Contains(
                "--audio-level-meter-ui",
                StringComparer.OrdinalIgnoreCase))
            {
                AudioLevelMeterPresentationTests.Run();
                Console.WriteLine(
                    "XbPreview.Managed.Tests PASS: Audio Level Meter UI v2 presentation");
                return 0;
            }

            if (args.Contains(
                "--panel4-recording-state",
                StringComparer.OrdinalIgnoreCase))
            {
                RecordingFixedHomeAdapterTests.Run();
                Console.WriteLine(
                    "XbPreview.Managed.Tests PASS: Panel 4 recording state");
                return 0;
            }

            if (args.Contains(
                "--panel4-cancel-recording",
                StringComparer.OrdinalIgnoreCase))
            {
                TestAbi();
                TestExports();
                await RecordingControllerTests.
                    RunPanel4CancelRecordingAsync();
                RecordingFixedHomeAdapterTests.Run();
                Panel4CancelUiContractTests.Run();
                UserCancelledRecoveryTests.Run();
                UserCancelledOwnershipContractTests.Run();
                Console.WriteLine(
                    "XbPreview.Managed.Tests PASS: Panel 4 UserCancelled lifecycle");
                return 0;
            }

            if (args.Contains(
                "--panel3-background",
                StringComparer.OrdinalIgnoreCase))
            {
                Stage3DPanelBackgroundTests.Run();
                Console.WriteLine(
                    "XbPreview.Managed.Tests PASS: Panel 3 background integration");
                return 0;
            }

            if (args.Contains(
                "--panel3-same-direction-return",
                StringComparer.OrdinalIgnoreCase))
            {
                Stage3DPanelInteractionTests.Run();
                Console.WriteLine(
                    "XbPreview.Managed.Tests PASS: Panel 3 same-direction Return");
                return 0;
            }

            if (args.Contains(
                "--panel1-preparation-policy",
                StringComparer.OrdinalIgnoreCase))
            {
                Panel1PreparationPolicyTests.Run();
                Console.WriteLine(
                    "XbPreview.Managed.Tests PASS: Panel 1 preparation policy");
                return 0;
            }

            if (args.Contains(
                "--system-core-audio-probe",
                StringComparer.OrdinalIgnoreCase))
            {
                SystemAudioDefaultRenderAvailability availability =
                    WindowsCoreAudioDefaultRenderProbe.Query();
                if (availability.Active &&
                    !availability.DefaultRenderPresent)
                {
                    throw new InvalidOperationException(
                        "An active endpoint must also be present.");
                }
                Console.WriteLine(
                    "XbPreview.Managed.Tests PASS: Windows Core Audio " +
                    $"default render present={availability.DefaultRenderPresent} " +
                    $"active={availability.Active}");
                return 0;
            }

            if (args.Contains(
                    "--cursor-visibility-gates",
                    StringComparer.OrdinalIgnoreCase))
            {
                TestExports();
                await PreviewLifecycleTests.RunCursorVisibilityGatesAsync();
                TestOperatorCursorRingGate();
                Console.WriteLine(
                    "CURSOR-VISIBILITY automated Gates 1-6 PASS");
                return 0;
            }

            const string pausePhaseCPrefix = "--pause-phase-c-gate-";
            string? pausePhaseCSelector = args.FirstOrDefault(argument =>
                argument.StartsWith(
                    pausePhaseCPrefix,
                    StringComparison.OrdinalIgnoreCase));
            if (pausePhaseCSelector is not null &&
                int.TryParse(
                    pausePhaseCSelector[pausePhaseCPrefix.Length..],
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out int pausePhaseCGate) &&
                pausePhaseCGate is >= 1 and <= 8)
            {
                await RecordingControllerTests.RunPausePhaseCGateAsync(
                    pausePhaseCGate);
                return 0;
            }

            if (args.Contains(
                "--microphone-selector-abi",
                StringComparer.OrdinalIgnoreCase))
            {
                TestMicrophoneSelectorAbi();
                Console.WriteLine(
                    "XbPreview.Managed.Tests PASS: microphone selector ABI");
                return 0;
            }

            if (args.Contains(
                "--mvp-audio-gstreamer",
                StringComparer.OrdinalIgnoreCase))
            {
                await RecordingControllerTests.RunMvpAudioGStreamerAsync();
                Console.WriteLine(
                    "XbPreview.Managed.Tests PASS: MVP GStreamer audio ownership");
                return 0;
            }

            if (args.Contains(
                "--mvp-audio-mode-routing",
                StringComparer.OrdinalIgnoreCase))
            {
                MinimalRecordingShellTests.Run();
                RecordingControllerTests.RunMvpAudioModeRouting();
                TestExports();
                Console.WriteLine(
                    "XbPreview.Managed.Tests PASS: MVP audio mode routing");
                return 0;
            }

            if (args.Contains(
                "--formal-product-contracts",
                StringComparer.OrdinalIgnoreCase))
            {
                TestAbi();
                TestExports();
                ProductSettingsTests.Run();
                Console.WriteLine(
                    "XbPreview.Managed.Tests PASS: formal product contracts");
                return 0;
            }

            if (args.Contains(
                "--resizable-director-monitor",
                StringComparer.OrdinalIgnoreCase))
            {
                MinimalRecordingShellTests.Run();
                TestLetterbox();
                TestCameraController();
                TestDualPresetStableTransitions();
                TestHotkeyBindings();
                TestHotkeyActivation();
                TestDirectorLite();
                TestFollowSmoother();
                await PreviewLifecycleTests.RunResizeAsync();
                Console.WriteLine(
                    "XbPreview.Managed.Tests PASS: MVP resizable director monitor");
                return 0;
            }

            if (args.Contains(
                "--window-stage",
                StringComparer.OrdinalIgnoreCase))
            {
                TestAbi();
                TestExports();
                TestWindowStageCaptureFoundations();
                TestDirectorLite();
                TestHotkeyBindings();
                Console.WriteLine(
                    "XbPreview.Managed.Tests PASS: MVP window stage capture");
                return 0;
            }

            if (args.Contains(
                "--window-capture-target-abi",
                StringComparer.OrdinalIgnoreCase))
            {
                TestWindowCaptureTargetAbi();
                Console.WriteLine(
                    "XbPreview.Managed.Tests PASS: Window Capture target / ABI");
                return 0;
            }

            int windowCaptureItemIndex = Array.FindIndex(
                args,
                static item => string.Equals(
                    item,
                    "--window-capture-item",
                    StringComparison.OrdinalIgnoreCase));
            if (windowCaptureItemIndex >= 0)
            {
                if (windowCaptureItemIndex + 1 >= args.Length)
                {
                    throw new ArgumentException(
                        "--window-capture-item requires a real external visible HWND.");
                }
                RunWindowCaptureItemGate(
                    args[windowCaptureItemIndex + 1]);
                Console.WriteLine(
                    "XbPreview.Managed.Tests PASS: real Window Capture item creation");
                return 0;
            }

            if (args.Contains(
                "--window-capture-start-stop-contract",
                StringComparer.OrdinalIgnoreCase))
            {
                await PreviewLifecycleTests.RunWindowCaptureContractAsync();
                Console.WriteLine(
                    "XbPreview.Managed.Tests PASS: Window Capture Start / Stop contract");
                return 0;
            }

            if (args.Contains(
                "--minimal-shell",
                StringComparer.OrdinalIgnoreCase))
            {
                MinimalRecordingShellTests.Run();
                Console.WriteLine(
                    "XbPreview.Managed.Tests PASS: MVP minimal recording shell");
                return 0;
            }

            if (args.Contains(
                "--p2.6e-storage-safety",
                StringComparer.OrdinalIgnoreCase))
            {
                await RecordingControllerTests.RunP26EAsync();
                Console.WriteLine(
                    "XbPreview.Managed.Tests PASS: P2.6E storage safety");
                return 0;
            }

            if (args.Length == 9 && string.Equals(
                args[0],
                "--p2.6d2b-managed-actor",
                StringComparison.OrdinalIgnoreCase))
            {
                return P26D2BRecoveryActor.Run(args);
            }

            if (args.Contains(
                "--p2.6c4b-recovery-fixture",
                StringComparer.OrdinalIgnoreCase))
            {
                RecoveryCandidateTests.ShowFixture();
                return 0;
            }

            if (args.Contains(
                "--p2.6c4b-user-recovery",
                StringComparer.OrdinalIgnoreCase))
            {
                await RecoveryCandidateTests.RunAsync();
                Console.WriteLine(
                    "XbPreview.Managed.Tests PASS: P2.6C-4B user recovery");
                return 0;
            }

            if (args.Contains(
                "--p2.6c4a-startup-inspection",
                StringComparer.OrdinalIgnoreCase))
            {
                await StartupInspectionTests.RunAsync();
                Console.WriteLine(
                    "XbPreview.Managed.Tests PASS: P2.6C-4A startup inspection");
                return 0;
            }

            if (args.Contains(
                "--p2.6a3-publish-mapping",
                StringComparer.OrdinalIgnoreCase))
            {
                await RecordingControllerTests.RunP26A3Async();
                Console.WriteLine(
                    "XbPreview.Managed.Tests PASS: P2.6A-3 publish mapping");
                return 0;
            }

            if (args.Contains(
                "--p2.5b-recording-controller",
                StringComparer.OrdinalIgnoreCase))
            {
                await RecordingControllerTests.RunAsync();
                Console.WriteLine(
                    "XbPreview.Managed.Tests PASS: P2.5B managed recording controller");
                return 0;
            }

            if (args.Contains("--p2.5a-interop", StringComparer.OrdinalIgnoreCase))
            {
                TestAbi();
                TestExports();
                Console.WriteLine(
                    "XbPreview.Managed.Tests PASS: P2.5A native interop");
                return 0;
            }

            if (args.Contains("--camera-motion", StringComparer.OrdinalIgnoreCase))
            {
                TestCameraMath();
                TestCriticalDampedCameraMotion();
                TestCameraController();
                TestReverseContinuity();
                TestStateEdges();
                TestDualPresetStableTransitions();
                TestDualPresetMonotonicity();
                TestDualPresetMidTransitionSwitches();
                TestHotkeyBindings();
                TestComfortZoneMath();
                TestFollowSmoother();
                TestFollowStateIntegration();
                Console.WriteLine(
                    "XbPreview.Managed.Tests PASS: P2.4.1 camera motion");
                return 0;
            }

            if (args.Contains("--director-lite", StringComparer.OrdinalIgnoreCase))
            {
                TestDirectorLite();
                TestRawMouseInputObserver();
                Console.WriteLine(
                    "XbPreview.Managed.Tests PASS: MVP Director Lite");
                return 0;
            }

            TestAbi();
            TestExports();
            TestCursorModeText();
            TestInvalidWindow();
            TestLetterbox();
            TestCameraMath();
            TestCameraController();
            TestReverseContinuity();
            TestStateEdges();
            TestDualPresetStableTransitions();
            TestDualPresetMonotonicity();
            TestDualPresetMidTransitionSwitches();
            TestHotkeyBindings();
            TestHotkeyActivation();
            TestComfortZoneMath();
            TestFollowSmoother();
            TestFollowStateIntegration();
            TestDirectorLite();
            TestRawMouseInputObserver();
            TestFrozenP1c2Baseline();
            TestRegionSelectionModels();
            TestRegionSelectionMath();
            TestRegionSelectionStateAndStartPolicy();
            await ProductFeaturesTests.RunAsync();
            ProductSettingsTests.Run();
            await PreviewLifecycleTests.RunAsync();
            await SessionGeometryTests.RunAsync();
            Console.WriteLine(
                "XbPreview.Managed.Tests PASS: P1d-a2.5a " +
                "region-capture user entry sealed");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(
                $"XbPreview.Managed.Tests FAIL: P1d-a2.5a " +
                $"region-capture user entry: {error}");
            return 1;
        }
    }

    private static void TestMicrophoneSelectorAbi()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "xbpreview-selector-abi",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            using Form window = new();
            using Panel surface = new()
            {
                Parent = window,
                Size = new System.Drawing.Size(320, 180),
            };
            window.CreateControl();
            surface.CreateControl();
            using NativePreviewSession session = NativePreviewSession.Create(
                surface.Handle,
                window.Handle,
                directory);
            MicrophoneDeviceCatalog catalog =
                session.GetMicrophoneDevices();
            Require(catalog.MonitorActive && catalog.DefaultAvailable &&
                catalog.Devices.Count > 0,
                "real GstDeviceMonitor catalog through managed ABI");

            MicrophoneDevice first = catalog.Devices[0];
            Require(session.SetMicrophoneSelection(new MicrophoneSelection(
                    MicrophoneSelectionKind.ConcreteEndpoint,
                    first.EndpointId,
                    first.DisplayName)) == NativeMethods.Result.Ok,
                "managed explicit microphone selection reaches native");
            MicrophoneSelectionStatus selected =
                session.GetMicrophoneSelection();
            Require(selected.Available &&
                selected.Kind == MicrophoneSelectionKind.ConcreteEndpoint &&
                selected.EndpointId == first.EndpointId &&
                selected.DisplayName == first.DisplayName,
                "native selection status preserves exact endpoint identity: " +
                $"available={selected.Available}; kind={selected.Kind}; " +
                $"expectedId={first.EndpointId}; actualId={selected.EndpointId}; " +
                $"expectedName={first.DisplayName}; actualName={selected.DisplayName}");

            string absent = $"{{test-absent-{Guid.NewGuid():N}}}";
            Require(session.SetMicrophoneSelection(new MicrophoneSelection(
                    MicrophoneSelectionKind.ConcreteEndpoint,
                    absent,
                    "Unavailable test microphone")) ==
                        NativeMethods.Result.Ok &&
                !session.GetMicrophoneSelection().Available &&
                session.GetMicrophoneDevices().Devices.Count > 0,
                "missing selected A remains unavailable while other real devices exist");

            Require(session.SetMicrophoneSelection(
                    MicrophoneSelection.WindowsDefault) ==
                        NativeMethods.Result.Ok &&
                session.GetMicrophoneSelection().Available,
                "Windows default resolves to the current concrete GstDevice");
            Console.WriteLine(
                $"MICROPHONE-SELECTOR-ABI devices={catalog.Devices.Count}; " +
                $"default={catalog.DefaultDisplayName}; " +
                $"selected={first.DisplayName}");
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private static unsafe void TestAbi()
    {
        NativeMethods.ValidateManagedLayout();
        Require(
            NativeMethods.XbPreview_GetApiVersion() == NativeMethods.ApiVersion,
            "API version");
        NativeMethods.AbiLayout layout = new()
        {
            StructSize = (uint)sizeof(NativeMethods.AbiLayout),
            ApiVersion = NativeMethods.ApiVersion,
        };
        Require(
            NativeMethods.XbPreview_GetAbiLayout(ref layout) == NativeMethods.Result.Ok,
            "GetAbiLayout");
        Require(layout.PointerSize == 8 && layout.Packing == 8, "x64 pack-8 ABI");
        Require(layout.CreateOptionsSize == sizeof(NativeMethods.CreateOptions), "options size");
        Require(layout.StatsSize == sizeof(NativeMethods.PreviewStats), "stats size");
        Require(layout.CameraStateSize == sizeof(NativeMethods.NativeCameraState), "camera size");
        Require(layout.CursorStatsSize == sizeof(NativeMethods.CursorStats), "cursor size");
        Require(
            layout.RecordingSnapshotSize ==
                sizeof(NativeMethods.RecordingSnapshot),
            "recording snapshot size");
        Require(layout.LetterboxRectSize == sizeof(NativeMethods.LetterboxRect), "letterbox size");
        Require(
            Marshal.OffsetOf<NativeMethods.PreviewStats>(
                nameof(NativeMethods.PreviewStats.CameraUpdateCount)).ToInt32() == 208 &&
            Marshal.OffsetOf<NativeMethods.PreviewStats>(
                nameof(NativeMethods.PreviewStats.AdapterName)).ToInt32() == 272 &&
            Marshal.OffsetOf<NativeMethods.PreviewStats>(
                nameof(NativeMethods.PreviewStats.LogFilePath)).ToInt32() == 528,
            "P1a stats offsets");
        Require(
            Marshal.OffsetOf<NativeMethods.NativeCameraState>(
                nameof(NativeMethods.NativeCameraState.Sequence)).ToInt32() == 8 &&
            Marshal.OffsetOf<NativeMethods.NativeCameraState>(
                nameof(NativeMethods.NativeCameraState.Zoom)).ToInt32() == 32 &&
            Marshal.OffsetOf<NativeMethods.NativeCameraState>(
                nameof(NativeMethods.NativeCameraState.TargetX)).ToInt32() == 64,
            "camera offsets");
        Require(
            sizeof(NativeMethods.CursorStats) == 944 &&
            Marshal.OffsetOf<NativeMethods.CursorStats>(
                nameof(NativeMethods.CursorStats.CursorSequence)).ToInt32() == 72 &&
            Marshal.OffsetOf<NativeMethods.CursorStats>(
                nameof(NativeMethods.CursorStats.SourceX)).ToInt32() == 200 &&
            Marshal.OffsetOf<NativeMethods.CursorStats>(
                nameof(NativeMethods.CursorStats.ShapeId)).ToInt32() == 360 &&
            Marshal.OffsetOf<NativeMethods.CursorStats>(
                nameof(NativeMethods.CursorStats.LogFilePath)).ToInt32() == 392,
            "cursor stats offsets");
        Require(
            NativeMethods.ApiVersion == 0x0004_0005 &&
            sizeof(NativeMethods.RecordingSnapshot) == 2856 &&
            Marshal.SizeOf<NativeMethods.RecordingSnapshot>() == 2856 &&
            typeof(NativeMethods.RecordingSnapshot).
                StructLayoutAttribute?.CharSet == CharSet.Unicode &&
            Marshal.OffsetOf<NativeMethods.RecordingSnapshot>(
                nameof(NativeMethods.RecordingSnapshot.StartUtc100ns)).ToInt32() == 16 &&
            Marshal.OffsetOf<NativeMethods.RecordingSnapshot>(
                nameof(NativeMethods.RecordingSnapshot.OutputCleanupAttempted)).ToInt32() == 60 &&
            Marshal.OffsetOf<NativeMethods.RecordingSnapshot>(
                nameof(NativeMethods.RecordingSnapshot.OutputCleanupSucceeded)).ToInt32() == 64 &&
            Marshal.OffsetOf<NativeMethods.RecordingSnapshot>(
                nameof(NativeMethods.RecordingSnapshot.OutputCleanupHResult)).ToInt32() == 68 &&
            Marshal.OffsetOf<NativeMethods.RecordingSnapshot>(
                nameof(NativeMethods.RecordingSnapshot.SessionId)).ToInt32() == 80 &&
            Marshal.OffsetOf<NativeMethods.RecordingSnapshot>(
                nameof(NativeMethods.RecordingSnapshot.OutputPath)).ToInt32() == 208 &&
            Marshal.OffsetOf<NativeMethods.RecordingSnapshot>(
                nameof(NativeMethods.RecordingSnapshot.ErrorMessage)).ToInt32() == 728 &&
            Marshal.OffsetOf<NativeMethods.RecordingSnapshot>(
                nameof(NativeMethods.RecordingSnapshot.ReadyToPublish)).ToInt32() == 1272 &&
            Marshal.OffsetOf<NativeMethods.RecordingSnapshot>(
                nameof(NativeMethods.RecordingSnapshot.Published)).ToInt32() == 1276 &&
            Marshal.OffsetOf<NativeMethods.RecordingSnapshot>(
                nameof(NativeMethods.RecordingSnapshot.PublishAttempted)).ToInt32() == 1280 &&
            Marshal.OffsetOf<NativeMethods.RecordingSnapshot>(
                nameof(NativeMethods.RecordingSnapshot.PublishHResult)).ToInt32() == 1284 &&
            Marshal.OffsetOf<NativeMethods.RecordingSnapshot>(
                nameof(NativeMethods.RecordingSnapshot.ValidationAttempted)).ToInt32() == 1288 &&
            Marshal.OffsetOf<NativeMethods.RecordingSnapshot>(
                nameof(NativeMethods.RecordingSnapshot.ValidationHResult)).ToInt32() == 1292 &&
            Marshal.OffsetOf<NativeMethods.RecordingSnapshot>(
                nameof(NativeMethods.RecordingSnapshot.WorkingPath)).ToInt32() == 1296 &&
            Marshal.OffsetOf<NativeMethods.RecordingSnapshot>(
                nameof(NativeMethods.RecordingSnapshot.PlannedFinalPath)).ToInt32() == 1816 &&
            Marshal.OffsetOf<NativeMethods.RecordingSnapshot>(
                nameof(NativeMethods.RecordingSnapshot.PublishedPath)).ToInt32() == 2336,
            "recording snapshot ABI");

        NativeMethods.AbiLayout oldVersion = new()
        {
            StructSize = (uint)sizeof(NativeMethods.AbiLayout),
            ApiVersion = 0x0003_0001,
        };
        Require(
            NativeMethods.XbPreview_GetAbiLayout(ref oldVersion) ==
                NativeMethods.Result.AbiMismatch,
            "old API major rejects new managed ABI layout");
        NativeMethods.AbiLayout oldSize = new()
        {
            StructSize = 40,
            ApiVersion = NativeMethods.ApiVersion,
        };
        Require(
            NativeMethods.XbPreview_GetAbiLayout(ref oldSize) ==
                NativeMethods.Result.AbiMismatch,
            "new API rejects old managed ABI layout size");

        RecordingControllerTests.
            RecordingSnapshotMarshalPreservesUnicodePaths();
    }

    private static void TestExports()
    {
        nint library = NativeLibrary.Load(
            Path.Combine(AppContext.BaseDirectory, NativeMethods.DllName));
        try
        {
            string[] exports =
            [
                "XbPreview_GetApiVersion", "XbPreview_GetAbiLayout",
                "XbPreview_GetHistoricalSessionScanAbiLayoutV1",
                "XbPreview_BeginHistoricalSessionScanV1",
                "XbPreview_BeginHistoricalSessionScanForOutputRootV1",
                "XbPreview_GetHistoricalSessionV1",
                "XbPreview_GetHistoricalSessionScanStringV1",
                "XbPreview_GetHistoricalSessionStringV1",
                "XbPreview_DestroyHistoricalSessionScanV1",
                "XbPreview_GetNarrowReconciliationAbiLayoutV1",
                "XbPreview_ReconcileNarrowSessionV1",
                "XbPreview_ReconcileNarrowSessionForOutputRootV1",
                "XbPreview_Create", "XbPreview_Start", "XbPreview_Stop",
                "XbPreview_StartRecording", "XbPreview_StopRecording",
                "XbPreview_CancelRecording",
                "XbPreview_SetAudioProgramMode",
                "XbPreview_GetRecordingSnapshot",
                "XbPreview_SetAudioControlsV1",
                "XbPreview_GetAudioControlSnapshotV1",
                "XbPreview_Resize", "XbPreview_SetGpuExportTargetSize",
                "XbPreview_SetSessionGeometry",
                "XbPreview_SetCameraState",
                "XbPreview_SetCursorMode",
                "XbPreview_SetRecordCursorVisible",
                "XbPreview_GetRecordCursorVisible",
                "XbPreview_SetCaptureTarget",
                "XbPreview_SetWindowStagePose",
                "XbPreview_SetWindowShowcasePose",
                "XbPreview_SetWindowShowcaseBackgroundPreset",
                "XbPreview_SetWindowShowcaseCustomBackground",
                "XbPreview_SetRecordingOutputRoot",
                "XbPreview_SetRecordingFrameRate",
                "XbPreview_GetCursorStats",
                "XbPreview_GetStats", "XbPreview_GetLastError",
                "XbPreview_Destroy", "XbPreview_CalculateLetterbox",
            ];
            foreach (string export in exports)
            {
                Require(NativeLibrary.TryGetExport(library, export, out _), $"missing {export}");
            }
        }
        finally
        {
            NativeLibrary.Free(library);
        }
    }

    private static void TestCursorModeText()
    {
        string custom = CursorModeText.Describe(
            NativeMethods.CursorMode.CustomCursor,
            NativeMethods.CursorMode.CustomCursor,
            NativeMethods.CursorFallbackReason.None);
        Require(
            custom.Contains("actual=CustomCursor", StringComparison.Ordinal) &&
            !custom.Contains("fallback=", StringComparison.Ordinal),
            "custom cursor status text");

        string fallback = CursorModeText.Describe(
            NativeMethods.CursorMode.CustomCursor,
            NativeMethods.CursorMode.SystemCursor,
            NativeMethods.CursorFallbackReason.ApiUnavailable);
        Require(
            fallback.Contains("actual=SystemCursor", StringComparison.Ordinal) &&
            fallback.Contains("fallback=ApiUnavailable", StringComparison.Ordinal),
            "cursor fallback status text");
    }

    private static void TestOperatorCursorRingGate()
    {
        using OperatorCursorRingForm ring = new();
        OperatorRingActivationResult prepared = ring.VerifyCaptureExclusion();
        Require(
            prepared.Succeeded &&
            prepared.AppliedAffinity ==
                WindowDisplayAffinity.ExcludeFromCapture,
            "Gate 6 ring capture exclusion readback");

        nint extendedStyle = GetWindowLongPtr(ring.Handle, -20);
        const long required =
            0x00000020L |
            0x00000080L |
            0x00080000L |
            0x08000000L;
        Require(
            (extendedStyle.ToInt64() & required) == required,
            "Gate 6 ring click-through/no-activate/tool-window styles");

        OperatorRingActivationResult shown = ring.ShowForOperator();
        Application.DoEvents();
        Require(shown.Succeeded && ring.Visible, "Gate 6 ring visible");
        ring.HideFromOperator();
        Application.DoEvents();
        Require(!ring.Visible, "Gate 6 ring hidden");
        Console.WriteLine(
            "Gate 6 PASS: operator ring WDA/style/readback contract");
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtr(nint window, int index);

    private static void TestWindowStageCaptureFoundations()
    {
        Require(
            CaptureTarget.FullScreen.Kind == CaptureTargetKind.Monitor &&
            CaptureTarget.FullScreen.WindowHandle == nint.Zero,
            "capture target defaults to full screen");
        Require(
            !WindowCaptureSelector.IsSelectableFacts(
                true, true, true, false, 42, 42, "Self"),
            "recorder-owned window cannot be selected");
        Require(
            !WindowCaptureSelector.IsSelectableFacts(
                true, false, true, false, 43, 42, "Hidden"),
            "invisible window cannot be selected");
        Require(
            WindowCaptureSelector.IsSelectableFacts(
                true, true, true, false, 43, 42, "Browser"),
            "ordinary external top-level window can be selected");
        Require(
            WindowCaptureSelector.TryNormalizePoint(
                150, 250, 100, 200, 300, 400, out CameraPoint center),
            "desktop click inside target maps");
        Near(center.X, 0.25, "window click normalized x", 1e-12);
        Near(center.Y, 0.25, "window click normalized y", 1e-12);
        Require(
            !WindowCaptureSelector.TryNormalizePoint(
                99, 250, 100, 200, 300, 400, out _),
            "window-external click is rejected before Director retarget");
        Require(
            NativeMethods.Result.WindowTargetClosed ==
                (NativeMethods.Result)(-20),
            "target close has an explicit product result");
        Require(
            NativeMethods.StatsFlags.WindowTargetMinimized ==
                (NativeMethods.StatsFlags)(1U << 6),
            "target minimize is an explicit warning fact");
    }

    private static void TestWindowCaptureTargetAbi()
    {
        TestAbi();
        TestExports();
        Require(
            (int)CaptureTargetKind.Monitor == 0 &&
            (int)CaptureTargetKind.Window == 1 &&
            (int)NativeMethods.CaptureTargetKind.Monitor == 0 &&
            (int)NativeMethods.CaptureTargetKind.Window == 1,
            "managed and native capture target kind values match");
        Require(
            CaptureTarget.FullScreen == new CaptureTarget(
                CaptureTargetKind.Monitor,
                nint.Zero,
                CaptureTarget.FullScreen.Title),
            "monitor remains the default capture target");
        Require(
            WindowCaptureSelector.IsSelectableFacts(
                true, true, true, false, 43, 42, "External window") &&
            !WindowCaptureSelector.IsSelectableFacts(
                true, true, true, false, 42, 42, "Recorder window"),
            "selector admits only an external visible top-level window");
    }

    private static void RunWindowCaptureItemGate(
        string windowHandleArgument)
    {
        nint targetWindow = ParseWindowHandle(windowHandleArgument);
        WindowCaptureChoice selected = WindowCaptureSelector.Enumerate()
            .FirstOrDefault(item => item.Handle == targetWindow);
        Require(
            selected.Handle == targetWindow,
            "the supplied HWND must be a real external visible top-level " +
            "window currently admitted by WindowCaptureSelector");

        string diagnosticDirectory = Path.Combine(
            Path.GetTempPath(),
            "xbpreview-window-capture-item",
            $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(diagnosticDirectory);

        using Form previewWindow = new()
        {
            Text = "XbPreview Window Capture item gate",
            StartPosition = FormStartPosition.CenterScreen,
            ClientSize = new System.Drawing.Size(720, 405),
        };
        using Panel previewSurface = new()
        {
            Parent = previewWindow,
            Dock = DockStyle.Fill,
        };
        previewWindow.Show();
        Application.DoEvents();

        using NativePreviewSession session = NativePreviewSession.Create(
            previewSurface.Handle,
            previewWindow.Handle,
            diagnosticDirectory);
        SessionGeometryNativeV1 geometry = new()
        {
            StructSize = SessionGeometryNativeV1.ExpectedSize,
            Version = SessionGeometryNativeV1.CurrentVersion,
            SourceWidth = 1920,
            SourceHeight = 1080,
            CaptureLeft = 0,
            CaptureTop = 0,
            CaptureWidth = 1920,
            CaptureHeight = 1080,
            OutputWidth = 1920,
            OutputHeight = 1080,
            GeometryRevision = 1,
        };
        Require(
            session.SetSessionGeometry(in geometry) ==
                NativeMethods.Result.Ok,
            "configure fixed output geometry before Window Capture Start");
        Require(
            session.SetCaptureTarget(new CaptureTarget(
                CaptureTargetKind.Window,
                targetWindow,
                selected.Title)) == NativeMethods.Result.Ok,
            "real external HWND reaches XbPreview_SetCaptureTarget");

        bool started = false;
        try
        {
            NativeMethods.Result startResult = session.Start();
            Require(
                startResult == NativeMethods.Result.Ok,
                $"native Window Capture Start: {startResult}; " +
                session.GetLastError());
            started = true;

            NativeMethods.PreviewStats observed = default;
            Stopwatch timeout = Stopwatch.StartNew();
            while (timeout.Elapsed < TimeSpan.FromSeconds(15))
            {
                Application.DoEvents();
                observed = session.GetStats();
                if (observed.CaptureFrameCount > 0 &&
                    observed.CaptureWidth > 0 &&
                    observed.CaptureHeight > 0)
                {
                    break;
                }
                Thread.Sleep(50);
            }
            bool firstFrameObserved =
                observed.CaptureFrameCount > 0 &&
                observed.CaptureWidth > 0 &&
                observed.CaptureHeight > 0;
            NativeMethods.Result stopResult = session.Stop();
            started = false;
            Require(
                stopResult == NativeMethods.Result.Ok,
                $"native Window Capture Stop: {stopResult}; " +
                session.GetLastError());

            string[] diagnosticLogPaths = Directory.GetFiles(
                diagnosticDirectory,
                "p0-*.jsonl",
                SearchOption.TopDirectoryOnly);
            Require(
                diagnosticLogPaths.Length == 1,
                "the Gate session produces exactly one P0 diagnostic log; " +
                $"directory={diagnosticDirectory}; " +
                $"count={diagnosticLogPaths.Length}");
            string diagnosticLogPath = diagnosticLogPaths[0];
            string[] diagnosticLines = File.ReadAllLines(diagnosticLogPath);
            Require(
                HasSuccessfulStartupStep(
                    diagnosticLines,
                    "CreateCaptureItemForWindow"),
                "CreateForWindow startup diagnostic succeeds");
            Require(
                HasSuccessfulStartupStep(
                    diagnosticLines,
                    "CreateFreeThreadedFramePool") &&
                HasSuccessfulStartupStep(diagnosticLines, "StartCapture"),
                "Window Capture converges on the shared frame pool and StartCapture");
            Require(
                firstFrameObserved,
                "CreateForWindow, FramePool, and StartCapture succeeded, but " +
                "the target produced no frame within 15 seconds. Restore the " +
                "window, keep it visible, and use its freshly enumerated HWND. " +
                $"target=0x{unchecked((ulong)targetWindow.ToInt64()):X}; " +
                $"flags={observed.Flags}");
            Require(
                diagnosticLines.Any(line =>
                    line.Contains(
                        "\"event\":\"capture-target\"",
                        StringComparison.Ordinal) &&
                    line.Contains("kind=Window", StringComparison.Ordinal)) &&
                diagnosticLines.Any(line => line.Contains(
                    "\"event\":\"first-frame\"",
                    StringComparison.Ordinal)),
                "target kind/HWND and first-frame dimensions are diagnosable");

            Console.WriteLine(
                $"WINDOW-CAPTURE-ITEM hwnd=0x" +
                $"{unchecked((ulong)targetWindow.ToInt64()):X}; " +
                $"frames={observed.CaptureFrameCount}; " +
                $"size={observed.CaptureWidth}x{observed.CaptureHeight}; " +
                $"diagnostic={diagnosticLogPath}");
        }
        finally
        {
            if (started)
            {
                _ = session.Stop();
            }
            previewWindow.Close();
        }
    }

    private static nint ParseWindowHandle(string value)
    {
        string normalized = value.Trim();
        NumberStyles style = NumberStyles.Integer;
        if (normalized.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[2..];
            style = NumberStyles.AllowHexSpecifier;
        }
        Require(
            ulong.TryParse(
                normalized,
                style,
                CultureInfo.InvariantCulture,
                out ulong raw) && raw != 0,
            "Window Capture HWND must be a non-zero decimal or 0x-prefixed value");
        return new nint(unchecked((long)raw));
    }

    private static bool HasSuccessfulStartupStep(
        IEnumerable<string> diagnosticLines,
        string operation) => diagnosticLines.Any(line =>
            line.Contains(
                $"\"Operation\":\"{operation}\"",
                StringComparison.Ordinal) &&
            line.Contains("\"Result\":\"success\"", StringComparison.Ordinal));

    private static unsafe void TestInvalidWindow()
    {
        NativeMethods.CreateOptions options = new()
        {
            StructSize = (uint)sizeof(NativeMethods.CreateOptions),
            ApiVersion = NativeMethods.ApiVersion,
            FramePoolBufferCount = 2,
            StatsIntervalMilliseconds = 1000,
        };
        Require(
            NativeMethods.XbPreview_Create(nint.Zero, in options, out nint handle) ==
                NativeMethods.Result.InvalidWindow,
            "invalid HWND");
        Require(handle == nint.Zero, "invalid HWND handle");
        StringBuilder one = new(1);
        Require(
            NativeMethods.XbPreview_GetLastErrorRaw(nint.Zero, one, 1) ==
                NativeMethods.Result.Ok && one.Length == 0,
            "bounded error string");
    }

    private static void TestLetterbox()
    {
        Require(
            NativeMethods.XbPreview_CalculateLetterbox(
                1920, 1080, 1000, 1000, out NativeMethods.LetterboxRect rect) ==
                NativeMethods.Result.Ok,
            "letterbox");
        Near(rect.X, 0.0, "letterbox x");
        Near(rect.Y, 218.75, "letterbox y");
        Near(rect.Width, 1000.0, "letterbox width");
        Near(rect.Height, 562.5, "letterbox height");
    }

    private static void TestCameraMath()
    {
        double previous = -1.0;
        for (int index = 0; index <= 100; index++)
        {
            double value = CameraMath.SmoothStep(index / 100.0);
            Require(value >= previous, "smoothstep monotonic");
            previous = value;
        }
        Near(CameraMath.SmoothStep(-1), 0, "smoothstep lower");
        Near(CameraMath.SmoothStep(2), 1, "smoothstep upper");

        CameraPoint normalized = CameraMath.NormalizeCursor(
            200, 100, -100, -50, 400, 200);
        Near(normalized.X, 0.75, "cursor x");
        Near(normalized.Y, 0.75, "cursor y");

        CameraView edge = CameraMath.ClampView(1.6, 0.0, 1.0);
        Near(edge.CenterX, 0.3125, "edge clamp x");
        Near(edge.CenterY, 0.6875, "edge clamp y");
        Require(edge.ClampX && edge.ClampY, "edge clamp flags");
        CameraUv uv = CameraMath.ToUv(edge);
        Near(uv.Left, 0.0, "UV left");
        Near(uv.Top, 0.375, "UV top");
        Near(uv.Width, 0.625, "UV width");
        Near(uv.Height, 0.625, "UV height");
        CameraView wide = CameraMath.ClampView(1.0, 0.0, 1.0);
        Near(wide.CenterX, 0.5, "wide center x");
        Near(wide.CenterY, 0.5, "wide center y");
        CameraView strong = CameraMath.ClampView(2.0, 0.0, 1.0);
        Near(strong.Zoom, 2.0, "strong zoom accepted");
        Near(strong.CenterX, 0.25, "strong edge clamp x");
        Near(strong.CenterY, 0.75, "strong edge clamp y");
        CameraUv strongUv = CameraMath.ToUv(strong);
        Near(strongUv.Width, 0.5, "strong UV width");
        Near(strongUv.Height, 0.5, "strong UV height");
        Near(
            CameraMath.ClampView(2.5, 0.5, 0.5).Zoom,
            CameraSettings.MaxSupportedZoom,
            "zoom above maximum safely clamps");
        foreach (CameraPoint corner in new[]
        {
            new CameraPoint(0, 0), new CameraPoint(1, 0),
            new CameraPoint(0, 1), new CameraPoint(1, 1),
        })
        {
            CameraUv cornerUv = CameraMath.ToUv(
                CameraMath.ClampView(1.6, corner.X, corner.Y));
            Require(
                cornerUv.Left >= 0 && cornerUv.Top >= 0 &&
                cornerUv.Left + cornerUv.Width <= 1.0 + 1e-12 &&
                cornerUv.Top + cornerUv.Height <= 1.0 + 1e-12,
                "corner UV bounded");
        }
        CameraView invalid = CameraMath.ClampView(double.NaN, double.PositiveInfinity, -1);
        Near(invalid.Zoom, 1.0, "invalid zoom fallback");
        CameraState strongState = CameraState.Wide(1, 1) with
        {
            Enabled = true,
            Mode = CameraMode.ZoomedFixed,
            Zoom = CameraSettings.StrongZoom,
        };
        Require(strongState.IsValid, "strong camera state valid");
        Require(
            !(strongState with { Zoom = 2.0001 }).IsValid,
            "camera state above maximum rejected");
        Near(invalid.CenterX, 0.5, "invalid center fallback");
    }

    private static void TestCriticalDampedCameraMotion()
    {
        double value = CameraSettings.WideZoom;
        double velocity = 0.0;
        bool settled = CameraMath.AdvanceCriticalDamped(
            ref value,
            ref velocity,
            CameraSettings.StandardZoom,
            CameraSettings.SpringAngularFrequency,
            1.0 / 120.0,
            CameraSettings.MaximumDeltaSeconds,
            CameraSettings.ZoomStopPositionEpsilon,
            CameraSettings.ZoomStopVelocityEpsilon);
        Require(!settled, "spring starts from rest without snapping");
        Require(value > CameraSettings.WideZoom, "spring responds immediately");
        Require(velocity > 0.0, "spring creates continuous velocity");

        (double value60, double velocity60) = SimulateScalarSpring(
            1.0 / 60.0,
            0.5,
            CameraSettings.StandardZoom);
        (double value120, double velocity120) = SimulateScalarSpring(
            1.0 / 120.0,
            0.5,
            CameraSettings.StandardZoom);
        Near(value60, value120, "spring 60/120Hz position equivalence", 1e-10);
        Near(velocity60, velocity120, "spring 60/120Hz velocity equivalence", 1e-9);

        double cappedValue = CameraSettings.WideZoom;
        double cappedVelocity = 0.0;
        CameraMath.AdvanceCriticalDamped(
            ref cappedValue,
            ref cappedVelocity,
            CameraSettings.StandardZoom,
            CameraSettings.SpringAngularFrequency,
            10.0,
            CameraSettings.MaximumDeltaSeconds,
            CameraSettings.ZoomStopPositionEpsilon,
            CameraSettings.ZoomStopVelocityEpsilon);
        double referenceValue = CameraSettings.WideZoom;
        double referenceVelocity = 0.0;
        CameraMath.AdvanceCriticalDamped(
            ref referenceValue,
            ref referenceVelocity,
            CameraSettings.StandardZoom,
            CameraSettings.SpringAngularFrequency,
            CameraSettings.MaximumDeltaSeconds,
            CameraSettings.MaximumDeltaSeconds,
            CameraSettings.ZoomStopPositionEpsilon,
            CameraSettings.ZoomStopVelocityEpsilon);
        Near(cappedValue, referenceValue, "large delta uses safe step cap", 1e-12);
        Near(cappedVelocity, referenceVelocity, "large delta velocity cap", 1e-12);
        Require(
            CameraMath.IsFinite(cappedValue) && CameraMath.IsFinite(cappedVelocity),
            "large delta remains finite");

        FixedTargetCameraController retarget = new(frequency: 120_000);
        retarget.SetPreviewRunning(true, 0);
        Near(retarget.ZoomVelocity, 0.0, "initial zoom velocity", 1e-12);
        retarget.Execute(
            CameraCommand.ToggleStandardCloseUp,
            new CameraPoint(0.7, 0.3),
            0,
            out _);
        CameraState beforeSwitch = retarget.Snapshot(12_000);
        double velocityBeforeSwitch = retarget.ZoomVelocity;
        Require(velocityBeforeSwitch > 0.0, "zoom has velocity before retarget");
        retarget.Execute(
            CameraCommand.ToggleStrongCloseUp,
            new CameraPoint(0.3, 0.7),
            12_000,
            out _);
        CameraState afterSwitch = retarget.Snapshot(12_000);
        Near(afterSwitch.Zoom, beforeSwitch.Zoom, "F9/F10 position continuity", 1e-12);
        Near(
            retarget.ZoomVelocity,
            velocityBeforeSwitch,
            "F9/F10 velocity continuity",
            1e-12);
        retarget.Snapshot(13_200);
        double velocityBeforeReverse = retarget.ZoomVelocity;
        CameraState beforeReverse = retarget.Snapshot(13_200);
        retarget.Execute(
            CameraCommand.ToggleStrongCloseUp,
            new CameraPoint(0.5, 0.5),
            13_200,
            out _);
        CameraState afterReverse = retarget.Snapshot(13_200);
        Near(afterReverse.Zoom, beforeReverse.Zoom, "reverse position continuity", 1e-12);
        Near(
            retarget.ZoomVelocity,
            velocityBeforeReverse,
            "reverse preserves inherited velocity",
            1e-12);
        Require(
            Math.Abs(retarget.ZoomVelocity) >
                CameraSettings.ZoomStopVelocityEpsilon,
            "reverse does not restart from zero velocity");

        FixedTargetCameraController sequence = new(frequency: 1000);
        sequence.SetPreviewRunning(true, 0);
        sequence.Execute(
            CameraCommand.ToggleStandardCloseUp,
            new CameraPoint(0.0, 1.0),
            0,
            out _);
        CameraState standard = sequence.Snapshot(1000);
        AssertPreset(
            standard,
            CameraSettings.StandardZoom,
            CameraMode.ZoomedFixed,
            "spring sequence standard");
        sequence.Execute(
            CameraCommand.ToggleStrongCloseUp,
            new CameraPoint(1.0, 0.0),
            1000,
            out _);
        CameraState strong = sequence.Snapshot(2000);
        AssertPreset(
            strong,
            CameraSettings.StrongZoom,
            CameraMode.ZoomedFixed,
            "spring sequence strong");
        sequence.Execute(
            CameraCommand.ToggleStrongCloseUp,
            new CameraPoint(0.5, 0.5),
            2000,
            out _);
        CameraState wide = sequence.Snapshot(3000);
        AssertWide(wide, "spring sequence wide");
        Near(sequence.ZoomVelocity, 0.0, "sequence clears zoom tail", 1e-12);
        Near(sequence.CenterVelocityX, 0.0, "sequence clears center x tail", 1e-12);
        Near(sequence.CenterVelocityY, 0.0, "sequence clears center y tail", 1e-12);
    }

    private static (double Value, double Velocity) SimulateScalarSpring(
        double deltaSeconds,
        double durationSeconds,
        double target)
    {
        double value = CameraSettings.WideZoom;
        double velocity = 0.0;
        int steps = (int)Math.Round(durationSeconds / deltaSeconds);
        for (int index = 0; index < steps; index++)
        {
            CameraMath.AdvanceCriticalDamped(
                ref value,
                ref velocity,
                target,
                CameraSettings.SpringAngularFrequency,
                deltaSeconds,
                CameraSettings.MaximumDeltaSeconds,
                CameraSettings.ZoomStopPositionEpsilon,
                CameraSettings.ZoomStopVelocityEpsilon);
        }
        return (value, velocity);
    }

    private static void TestCameraController()
    {
        FixedTargetCameraController controller = new(frequency: 250);
        controller.SetPreviewRunning(true, 0);
        Require(
            controller.Execute(
                CameraCommand.ToggleStandardCloseUp,
                new CameraPoint(0.9, 0.1),
                0,
                out _),
            "toggle in");
        double previousZoom = 1.0;
        for (long qpc = 0; qpc <= 240; qpc += 10)
        {
            CameraState state = controller.Snapshot(qpc);
            Require(state.Zoom >= previousZoom, "zoom-in monotonic");
            Require(CameraMath.IsValidState(state), "valid generated state");
            Near(state.TargetX, 0.9, "fixed target x");
            Near(state.TargetY, 0.1, "fixed target y");
            previousZoom = state.Zoom;
        }
        CameraState final = controller.Snapshot(1000);
        Require(final.Mode == CameraMode.ZoomedFixed, "fixed final mode");
        Near(final.Zoom, CameraSettings.StandardZoom, "fixed final zoom");
        Near(final.CenterX, 0.6875, "fixed final clamped x");
        Near(final.CenterY, 0.3125, "fixed final clamped y");

        controller.SetEnabled(false, 1100);
        CameraState disabled = controller.Snapshot(1100);
        Require(!disabled.Enabled && disabled.Mode == CameraMode.Wide, "disabled fallback");
        Near(disabled.Zoom, 1.0, "disabled zoom");

        FixedTargetCameraController largeDelta = new(frequency: 250);
        largeDelta.SetPreviewRunning(true, 0);
        largeDelta.Execute(
            CameraCommand.ToggleStandardCloseUp,
            new CameraPoint(0.6, 0.6),
            0,
            out _);
        Require(
            largeDelta.Snapshot(10_000).Mode == CameraMode.ZoomedFixed,
            "large delta completes safely");
    }

    private static void TestReverseContinuity()
    {
        FixedTargetCameraController controller = new(frequency: 250);
        controller.SetPreviewRunning(true, 0);
        controller.Execute(
            CameraCommand.ToggleStandardCloseUp,
            new CameraPoint(0.7, 0.3),
            0,
            out _);
        CameraState before = controller.Snapshot(100);
        controller.Execute(
            CameraCommand.ToggleStandardCloseUp,
            new CameraPoint(0.2, 0.8),
            100,
            out _);
        CameraState reversed = controller.Snapshot(100);
        Near(reversed.Zoom, before.Zoom, "reverse zoom continuity", 1e-12);
        Near(reversed.CenterX, before.CenterX, "reverse x continuity", 1e-12);
        Near(reversed.CenterY, before.CenterY, "reverse y continuity", 1e-12);
        double previousZoom = reversed.Zoom;
        for (long qpc = 110; qpc <= 340; qpc += 10)
        {
            CameraState state = controller.Snapshot(qpc);
            Require(state.Zoom <= previousZoom + 1e-12, "zoom-out monotonic");
            previousZoom = state.Zoom;
        }
        CameraState final = controller.Snapshot(1000);
        Require(final.Mode == CameraMode.Wide && !final.Enabled, "reverse reaches wide");
        Near(final.Zoom, 1.0, "reverse final zoom");
        Near(final.CenterX, 0.5, "reverse final x");
        Near(final.CenterY, 0.5, "reverse final y");
    }

    private static void TestStateEdges()
    {
        FixedTargetCameraController stopped = new(frequency: 250);
        Require(
            !stopped.Execute(
                CameraCommand.ToggleStandardCloseUp,
                new CameraPoint(0.5, 0.5),
                0,
                out _),
            "preview stopped ignores camera command");
        Require(
            !stopped.Execute(
                CameraCommand.ToggleStrongCloseUp,
                new CameraPoint(0.5, 0.5),
                0,
                out _),
            "preview stopped ignores strong command");

        FixedTargetCameraController disabled = new(frequency: 250);
        disabled.SetPreviewRunning(true, 0);
        disabled.SetEnabled(false, 0);
        Require(
            !disabled.Execute(
                CameraCommand.ToggleStrongCloseUp,
                new CameraPoint(0.5, 0.5),
                0,
                out _),
            "disabled camera ignores command");
        CameraState disabledState = disabled.Snapshot(1);
        Require(
            disabledState.Mode == CameraMode.Wide &&
            !disabledState.Enabled &&
            disabledState.Zoom == CameraSettings.WideZoom,
            "disabled camera remains strictly wide");

        FixedTargetCameraController reverseOut = new(frequency: 250);
        reverseOut.SetPreviewRunning(true, 0);
        reverseOut.Execute(
            CameraCommand.ToggleStandardCloseUp,
            new CameraPoint(0.6, 0.4),
            0,
            out _);
        Require(reverseOut.Snapshot(240).Mode == CameraMode.ZoomedFixed, "in complete");
        reverseOut.Execute(
            CameraCommand.ToggleStandardCloseUp,
            new CameraPoint(0.1, 0.1),
            300,
            out _);
        CameraState outMid = reverseOut.Snapshot(380);
        reverseOut.Execute(
            CameraCommand.ToggleStandardCloseUp,
            new CameraPoint(0.4, 0.7),
            380,
            out _);
        CameraState inAgain = reverseOut.Snapshot(380);
        Near(inAgain.Zoom, outMid.Zoom, "out-to-in zoom continuity", 1e-12);
        Near(inAgain.CenterX, outMid.CenterX, "out-to-in x continuity", 1e-12);
        Near(inAgain.TargetX, 0.4, "out-to-in locks fresh target");
        Near(inAgain.TargetY, 0.7, "out-to-in locks fresh target y");

        CameraState exit = reverseOut.PrepareForExit(400);
        Require(
            exit.Mode == CameraMode.Wide && !exit.Enabled,
            "UI cleanup fallback state");
        Near(exit.Zoom, 1.0, "UI cleanup zoom");

        FixedTargetCameraController variable = new(frequency: 250);
        variable.SetPreviewRunning(true, 0);
        variable.Execute(
            CameraCommand.ToggleStandardCloseUp,
            new CameraPoint(0.55, 0.45),
            0,
            out _);
        variable.Snapshot(7);
        variable.Snapshot(63);
        variable.Snapshot(181);
        CameraState variableFinal = variable.Snapshot(240);
        Require(variableFinal.Mode == CameraMode.ZoomedFixed, "variable delta duration");
        Near(
            variableFinal.Zoom,
            CameraSettings.StandardZoom,
            "variable delta endpoint");
    }

    private static void TestDualPresetStableTransitions()
    {
        FixedTargetCameraController controller = new(frequency: 250);
        controller.SetPreviewRunning(true, 0);
        int cursorReads = 0;

        CameraPoint ReadTarget(double x, double y)
        {
            cursorReads++;
            return new CameraPoint(x, y);
        }

        controller.Execute(
            CameraCommand.ToggleStandardCloseUp,
            () => ReadTarget(0.3, 0.4),
            0,
            out _);
        CameraState standard = controller.Snapshot(240);
        AssertPreset(
            standard,
            CameraSettings.StandardZoom,
            CameraMode.ZoomedFixed,
            "wide to standard");
        Require(standard.Event == "standard-complete", "standard completion event");

        controller.Execute(
            CameraCommand.ToggleStrongCloseUp,
            () => ReadTarget(0.8, 0.7),
            300,
            out _);
        CameraState standardToStrongStart = controller.Snapshot(300);
        Near(
            standardToStrongStart.Zoom,
            standard.Zoom,
            "standard-to-strong starts at applied zoom",
            1e-12);
        Require(
            standardToStrongStart.Event == "standard-to-strong",
            "standard-to-strong event");
        CameraState strong = controller.Snapshot(540);
        AssertPreset(
            strong,
            CameraSettings.StrongZoom,
            CameraMode.ZoomedFixed,
            "standard to strong");
        Require(strong.Event == "strong-complete", "strong completion event");

        controller.Execute(
            CameraCommand.ToggleStandardCloseUp,
            () => ReadTarget(0.2, 0.6),
            600,
            out _);
        CameraState strongToStandardStart = controller.Snapshot(600);
        Near(
            strongToStandardStart.Zoom,
            strong.Zoom,
            "strong-to-standard starts at applied zoom",
            1e-12);
        Require(
            strongToStandardStart.Event == "strong-to-standard",
            "strong-to-standard event");
        standard = controller.Snapshot(840);
        AssertPreset(
            standard,
            CameraSettings.StandardZoom,
            CameraMode.ZoomedFixed,
            "strong to standard");

        int readsBeforeExit = cursorReads;
        controller.Execute(
            CameraCommand.ToggleStandardCloseUp,
            () => ReadTarget(0.9, 0.9),
            900,
            out _);
        Require(cursorReads == readsBeforeExit, "standard exit does not read cursor");
        CameraState wide = controller.Snapshot(1140);
        AssertWide(wide, "standard to wide");

        controller.Execute(
            CameraCommand.ToggleStrongCloseUp,
            () => ReadTarget(0.75, 0.25),
            1200,
            out _);
        strong = controller.Snapshot(1440);
        AssertPreset(
            strong,
            CameraSettings.StrongZoom,
            CameraMode.ZoomedFixed,
            "wide to strong");

        readsBeforeExit = cursorReads;
        controller.Execute(
            CameraCommand.ToggleStrongCloseUp,
            () => ReadTarget(0.1, 0.1),
            1500,
            out _);
        Require(cursorReads == readsBeforeExit, "strong exit does not read cursor");
        wide = controller.Snapshot(1740);
        AssertWide(wide, "strong to wide");
        Require(cursorReads == 4, "every preset entry locks one fresh target");
    }

    private static void TestDualPresetMidTransitionSwitches()
    {
        FixedTargetCameraController controller = new(frequency: 250);
        controller.SetPreviewRunning(true, 0);

        controller.Execute(
            CameraCommand.ToggleStandardCloseUp,
            new CameraPoint(0.7, 0.3),
            0,
            out _);
        CameraState wideToStandardMid = controller.Snapshot(80);
        AssertContinuousRetarget(
            controller,
            CameraCommand.ToggleStandardCloseUp,
            new CameraPoint(0.1, 0.1),
            80,
            wideToStandardMid,
            "wide-to-standard same-key reversal");
        AssertWide(controller.Snapshot(320), "mid standard enter to wide");

        controller.Execute(
            CameraCommand.ToggleStandardCloseUp,
            new CameraPoint(0.6, 0.4),
            400,
            out _);
        CameraState standard = controller.Snapshot(640);
        controller.Execute(
            CameraCommand.ToggleStandardCloseUp,
            new CameraPoint(0.1, 0.1),
            700,
            out _);
        CameraState standardToWideMid = controller.Snapshot(780);
        AssertContinuousRetarget(
            controller,
            CameraCommand.ToggleStandardCloseUp,
            new CameraPoint(0.4, 0.6),
            780,
            standardToWideMid,
            "standard-to-wide same-key reversal");
        AssertPreset(
            controller.Snapshot(1020),
            CameraSettings.StandardZoom,
            CameraMode.ZoomedFixed,
            "mid standard exit to standard");

        FixedTargetCameraController strongController = new(frequency: 250);
        strongController.SetPreviewRunning(true, 0);
        strongController.Execute(
            CameraCommand.ToggleStrongCloseUp,
            new CameraPoint(0.8, 0.2),
            0,
            out _);
        CameraState wideToStrongMid = strongController.Snapshot(80);
        AssertContinuousRetarget(
            strongController,
            CameraCommand.ToggleStrongCloseUp,
            new CameraPoint(0.1, 0.1),
            80,
            wideToStrongMid,
            "wide-to-strong same-key reversal");
        AssertWide(
            strongController.Snapshot(320),
            "mid strong enter to wide");

        strongController.Execute(
            CameraCommand.ToggleStrongCloseUp,
            new CameraPoint(0.7, 0.3),
            400,
            out _);
        strongController.Snapshot(640);
        strongController.Execute(
            CameraCommand.ToggleStrongCloseUp,
            new CameraPoint(0.1, 0.1),
            700,
            out _);
        CameraState strongToWideMid = strongController.Snapshot(780);
        AssertContinuousRetarget(
            strongController,
            CameraCommand.ToggleStrongCloseUp,
            new CameraPoint(0.6, 0.4),
            780,
            strongToWideMid,
            "strong-to-wide same-key reversal");
        AssertPreset(
            strongController.Snapshot(1020),
            CameraSettings.StrongZoom,
            CameraMode.ZoomedFixed,
            "mid strong exit to strong");

        controller.Execute(
            CameraCommand.ToggleStrongCloseUp,
            new CameraPoint(0.8, 0.2),
            1100,
            out _);
        CameraState standardToStrongMid = controller.Snapshot(1180);
        AssertContinuousRetarget(
            controller,
            CameraCommand.ToggleStandardCloseUp,
            new CameraPoint(0.3, 0.7),
            1180,
            standardToStrongMid,
            "standard-to-strong retarget to standard");
        standard = controller.Snapshot(1420);
        AssertPreset(
            standard,
            CameraSettings.StandardZoom,
            CameraMode.ZoomedFixed,
            "cross-preset retarget reaches standard");

        controller.Execute(
            CameraCommand.ToggleStrongCloseUp,
            new CameraPoint(0.75, 0.25),
            1500,
            out _);
        CameraState strong = controller.Snapshot(1740);
        AssertPreset(
            strong,
            CameraSettings.StrongZoom,
            CameraMode.ZoomedFixed,
            "strong setup");
        controller.Execute(
            CameraCommand.ToggleStandardCloseUp,
            new CameraPoint(0.25, 0.75),
            1800,
            out _);
        CameraState strongToStandardMid = controller.Snapshot(1880);
        AssertContinuousRetarget(
            controller,
            CameraCommand.ToggleStrongCloseUp,
            new CameraPoint(0.65, 0.35),
            1880,
            strongToStandardMid,
            "strong-to-standard retarget to strong");
        AssertPreset(
            controller.Snapshot(2120),
            CameraSettings.StrongZoom,
            CameraMode.ZoomedFixed,
            "cross-preset retarget reaches strong");

        CameraState beforeRapid = controller.Snapshot(2140);
        AssertContinuousRetarget(
            controller,
            CameraCommand.ToggleStandardCloseUp,
            new CameraPoint(0.2, 0.8),
            2140,
            beforeRapid,
            "rapid F9");
        CameraState afterF9 = controller.Snapshot(2160);
        AssertContinuousRetarget(
            controller,
            CameraCommand.ToggleStrongCloseUp,
            new CameraPoint(0.8, 0.2),
            2160,
            afterF9,
            "rapid F10");
        CameraState afterF10 = controller.Snapshot(2180);
        AssertContinuousRetarget(
            controller,
            CameraCommand.ToggleStandardCloseUp,
            new CameraPoint(0.4, 0.6),
            2180,
            afterF10,
            "rapid F9 second");
        Require(
            controller.Snapshot(2420).Sequence > afterF10.Sequence,
            "rapid alternating sequence remains monotonic");
    }

    private static void TestDualPresetMonotonicity()
    {
        Run(
            CameraPreset.Wide,
            CameraCommand.ToggleStandardCloseUp,
            CameraSettings.StandardZoom,
            "wide to standard monotonic");
        Run(
            CameraPreset.Standard,
            CameraCommand.ToggleStandardCloseUp,
            CameraSettings.WideZoom,
            "standard to wide monotonic");
        Run(
            CameraPreset.Wide,
            CameraCommand.ToggleStrongCloseUp,
            CameraSettings.StrongZoom,
            "wide to strong monotonic");
        Run(
            CameraPreset.Strong,
            CameraCommand.ToggleStrongCloseUp,
            CameraSettings.WideZoom,
            "strong to wide monotonic");
        Run(
            CameraPreset.Standard,
            CameraCommand.ToggleStrongCloseUp,
            CameraSettings.StrongZoom,
            "standard to strong monotonic");
        Run(
            CameraPreset.Strong,
            CameraCommand.ToggleStandardCloseUp,
            CameraSettings.StandardZoom,
            "strong to standard monotonic");

        static void Run(
            CameraPreset initialPreset,
            CameraCommand command,
            double expectedFinalZoom,
            string message)
        {
            FixedTargetCameraController controller = new(frequency: 250);
            controller.SetPreviewRunning(true, 0);
            long transitionStart = 0;
            if (initialPreset != CameraPreset.Wide)
            {
                CameraCommand setup = initialPreset == CameraPreset.Standard
                    ? CameraCommand.ToggleStandardCloseUp
                    : CameraCommand.ToggleStrongCloseUp;
                controller.Execute(
                    setup,
                    new CameraPoint(0.5, 0.5),
                    0,
                    out _);
                controller.Snapshot(240);
                transitionStart = 300;
            }

            CameraState before = controller.Snapshot(transitionStart);
            controller.Execute(
                command,
                new CameraPoint(0.65, 0.35),
                transitionStart,
                out _);
            CameraState first = controller.Snapshot(transitionStart);
            Near(first.Zoom, before.Zoom, $"{message}: starts at current zoom", 1e-12);
            Near(
                first.CenterX,
                before.CenterX,
                $"{message}: starts at current center x",
                1e-12);
            Near(
                first.CenterY,
                before.CenterY,
                $"{message}: starts at current center y",
                1e-12);

            bool increasing = expectedFinalZoom > before.Zoom;
            double previousZoom = first.Zoom;
            for (long offset = 10; offset <= 240; offset += 10)
            {
                CameraState state = controller.Snapshot(transitionStart + offset);
                Require(state.IsValid, $"{message}: every sample valid");
                Require(
                    increasing
                        ? state.Zoom >= previousZoom - 1e-12
                        : state.Zoom <= previousZoom + 1e-12,
                    $"{message}: zoom direction");
                previousZoom = state.Zoom;
            }

            CameraState final = controller.Snapshot(transitionStart + 240);
            Near(final.Zoom, expectedFinalZoom, $"{message}: endpoint", 1e-12);
            if (expectedFinalZoom == CameraSettings.WideZoom)
            {
                AssertWide(final, message);
            }
            else
            {
                AssertPreset(
                    final,
                    expectedFinalZoom,
                    CameraMode.ZoomedFixed,
                    message);
            }
        }
    }

    private static void TestDirectorLite()
    {
        const long frequency = 1000;
        FixedTargetCameraController camera = new(frequency);
        Require(camera.Owner == CameraOwner.Manual, "director default owner manual");
        Require(
            camera.DirectorFocusStrength == DirectorFocusStrength.Soft,
            "director default focus strength soft");
        Near(
            CameraSettings.DirectorLiteInactivitySeconds,
            4.0,
            "director inactivity remains four seconds");
        camera.SetPreviewRunning(true, 0);

        Require(
            camera.Execute(
                CameraCommand.ToggleStandardCloseUp,
                new CameraPoint(0.25, 0.75),
                1,
                out _),
            "manual 1.6 works while director off");
        camera.Snapshot(1000);
        Require(
            camera.Execute(
                CameraCommand.ToggleStrongCloseUp,
                new CameraPoint(0.75, 0.25),
                1001,
                out _),
            "manual 2.0 works while director off");

        Require(
            camera.SetDirectorFocusStrength(
                DirectorFocusStrength.Soft,
                out _),
            "soft focus strength configures while manual");
        Require(
            camera.SetDirectorLiteEnabled(true, 1100, out _),
            "director enables");
        Require(
            camera.Owner == CameraOwner.DirectorLite &&
            camera.TargetZoom == CameraSettings.WideZoom &&
            !camera.HasDirectorFocusTarget,
            "director uniquely owns camera after wide handoff");
        Require(
            !camera.Execute(
                CameraCommand.ToggleStandardCloseUp,
                new CameraPoint(0.5, 0.5),
                1101,
                out _),
            "manual 1.6 rejected while director on");
        Require(
            !camera.Execute(
                CameraCommand.ToggleStrongCloseUp,
                new CameraPoint(0.5, 0.5),
                1102,
                out _),
            "manual 2.0 rejected while director on");
        Require(
            !camera.SetDirectorFocusStrength(
                DirectorFocusStrength.Strong,
                out string lockedStatus) &&
            lockedStatus.Contains("locked", StringComparison.Ordinal) &&
            camera.DirectorFocusStrength == DirectorFocusStrength.Soft,
            "enabled director rejects hot focus strength switch");

        Require(
            camera.HandleDirectorLeftClick(
                new CameraPoint(0.0, 1.0),
                1200,
                out _),
            "wide click focuses");
        CameraState focusStart = camera.Snapshot(1200);
        Require(
            camera.DirectorState == DirectorLiteState.Focused &&
            camera.TargetZoom == CameraSettings.StandardZoom,
            "click targets director 1.6");
        Near(focusStart.TargetX, 0.0, "director click target x");
        Near(focusStart.TargetY, 1.0, "director click target y");
        CameraState edgeFocused = camera.Snapshot(2200);
        Near(edgeFocused.CenterX, 0.3125, "director left edge clamped");
        Near(edgeFocused.CenterY, 0.6875, "director bottom edge clamped");

        Require(
            camera.HandleDirectorLeftClick(
                new CameraPoint(0.8, 0.2),
                2300,
                out _),
            "focused click retargets");
        CameraState retarget = camera.Snapshot(2300);
        Near(camera.TargetZoom, 1.6, "retarget stays 1.6");
        Near(retarget.Zoom, 1.6, "retarget does not return wide");
        Near(retarget.TargetX, 0.8, "retarget click x");
        Near(retarget.TargetY, 0.2, "retarget click y");

        Require(
            camera.HandleDirectorPointerActivity(6000),
            "pointer activity refreshes inactivity");
        camera.Snapshot(9999);
        Near(camera.TargetZoom, 1.6, "activity postpones return wide");
        CameraState returnWide = camera.Snapshot(10000);
        Near(camera.TargetZoom, 1.0, "inactivity targets wide");
        Require(
            returnWide.Event == "director-inactivity-wide" &&
            camera.DirectorState == DirectorLiteState.Wide &&
            !camera.HasDirectorFocusTarget,
            "inactivity clears director focus and timer state");

        Require(
            camera.SetDirectorLiteEnabled(false, 10100, out _),
            "director disables");
        Require(
            camera.Owner == CameraOwner.Manual &&
            !camera.HasDirectorFocusTarget &&
            camera.LastDirectorActivityQpc == 0,
            "manual handoff leaves no director target or timer");
        Require(
            camera.Execute(
                CameraCommand.ToggleStandardCloseUp,
                new CameraPoint(0.4, 0.6),
                10101,
                out _),
            "manual 1.6 restored after director");
        camera.Snapshot(11000);
        Require(
            camera.Execute(
                CameraCommand.ToggleStrongCloseUp,
                new CameraPoint(0.6, 0.4),
                11001,
                out _),
            "manual 2.0 restored after director");

        Require(
            camera.SetDirectorFocusStrength(
                DirectorFocusStrength.Strong,
                out _),
            "disable allows strong focus selection");
        Require(
            camera.DirectorFocusStrength == DirectorFocusStrength.Strong,
            "strong focus selection retained before enable");
        Require(
            camera.SetDirectorLiteEnabled(true, 11100, out _),
            "strong director enables");
        Require(
            !camera.Execute(
                CameraCommand.ToggleStandardCloseUp,
                new CameraPoint(0.5, 0.5),
                11101,
                out _) &&
            !camera.Execute(
                CameraCommand.ToggleStrongCloseUp,
                new CameraPoint(0.5, 0.5),
                11102,
                out _),
            "F9 and F10 remain rejected in strong director session");

        camera.HandleDirectorLeftClick(new CameraPoint(0.5, 0.5), 11200, out _);
        CameraState strongCenter = camera.Snapshot(12200);
        Near(camera.TargetZoom, 2.0, "strong click targets 2.0");
        Near(strongCenter.Zoom, 2.0, "strong center settles at 2.0");
        Near(strongCenter.CenterX, 0.5, "strong center x");
        Near(strongCenter.CenterY, 0.5, "strong center y");

        camera.HandleDirectorLeftClick(new CameraPoint(0.0, 0.5), 12300, out _);
        CameraState strongLeftRetarget = camera.Snapshot(12300);
        Near(
            strongLeftRetarget.Zoom,
            2.0,
            "strong left retarget does not return wide");
        Near(camera.TargetZoom, 2.0, "strong left retarget keeps 2.0 target");
        camera.Snapshot(13300);
        camera.HandleDirectorLeftClick(new CameraPoint(1.0, 0.5), 13400, out _);
        CameraState strongRightRetarget = camera.Snapshot(13400);
        Near(
            strongRightRetarget.Zoom,
            2.0,
            "strong cross-region retarget remains 2.0");
        Near(camera.TargetZoom, 2.0, "strong cross-region target stays 2.0");
        CameraState strongRight = camera.Snapshot(14400);

        ComfortZoneTracker strongFollow = new(enabled: true, frequency);
        ComfortZoneFollowStep strongInside = strongFollow.Update(
            strongRight,
            Cursor(strongRight.CenterX, strongRight.CenterY),
            0.016,
            1);
        Require(
            strongInside.State == FollowState.InsideComfortZone,
            "strong follow arms inside comfort zone");
        CameraState strongFollowState = strongRight;
        for (int index = 0; index < 120; index++)
        {
            ComfortZoneFollowStep step = strongFollow.Update(
                strongFollowState,
                Cursor(0.0, 1.0),
                0.032,
                index + 2);
            if (step.ShouldApplyCenter)
            {
                Require(
                    camera.TrySetZoomedCenter(
                        strongFollowState,
                        step.OutputCenter,
                        out strongFollowState),
                    "strong follow applies through existing controller");
            }
            Require(
                strongFollowState.CenterX is >= 0.25 and <= 0.75 &&
                strongFollowState.CenterY is >= 0.25 and <= 0.75,
                "strong follow remains inside legal crop center bounds");
        }

        CameraPoint[] strongCorners =
        [
            new CameraPoint(0.0, 0.0),
            new CameraPoint(1.0, 0.0),
            new CameraPoint(0.0, 1.0),
            new CameraPoint(1.0, 1.0),
        ];
        long cornerQpc = 14500;
        foreach (CameraPoint corner in strongCorners)
        {
            camera.HandleDirectorLeftClick(corner, cornerQpc, out _);
            CameraState cornerState = camera.Snapshot(cornerQpc + 1000);
            Near(cornerState.Zoom, 2.0, "strong corner stays 2.0");
            CameraUv uv = CameraMath.ToUv(
                CameraMath.ClampView(
                    cornerState.Zoom,
                    cornerState.CenterX,
                    cornerState.CenterY));
            Require(
                uv.Left >= -1e-12 && uv.Top >= -1e-12 &&
                uv.Left + uv.Width <= 1.0 + 1e-12 &&
                uv.Top + uv.Height <= 1.0 + 1e-12,
                "strong four-corner crop is legal");
            cornerQpc += 1100;
        }

        long lastStrongActivity = cornerQpc - 1100;
        camera.Snapshot(lastStrongActivity + 3999);
        Near(camera.TargetZoom, 2.0, "strong inactivity waits four seconds");
        CameraState strongWide = camera.Snapshot(lastStrongActivity + 4000);
        Near(camera.TargetZoom, 1.0, "strong inactivity returns wide");
        Require(
            strongWide.Event == "director-inactivity-wide",
            "strong inactivity uses existing return-wide path");

        Require(
            camera.SetDirectorLiteEnabled(
                false,
                lastStrongActivity + 4100,
                out _),
            "strong director disables");
        Require(
            camera.Owner == CameraOwner.Manual &&
            camera.DirectorState == DirectorLiteState.Wide &&
            camera.TargetZoom == CameraSettings.WideZoom &&
            camera.LastDirectorActivityQpc == 0,
            "strong disable returns wide manual ownership without timer");
        Require(
            camera.SetDirectorFocusStrength(
                DirectorFocusStrength.Soft,
                out _),
            "disabled director allows reselecting soft");

        camera.SetDirectorLiteEnabled(
            true,
            lastStrongActivity + 4200,
            out _);
        camera.HandleDirectorLeftClick(
            new CameraPoint(0.3, 0.7),
            lastStrongActivity + 4300,
            out _);
        camera.SetPreviewRunning(false, lastStrongActivity + 4400);
        Require(
            camera.Owner == CameraOwner.Manual &&
            camera.DirectorState == DirectorLiteState.Wide &&
            !camera.HasDirectorFocusTarget &&
            camera.LastDirectorActivityQpc == 0,
            "preview stop clears director ownership and state");
    }

    private static void TestRawMouseInputObserver()
    {
        FakeRawMouseInputApi api = new();
        int observations = 0;
        using (RawMouseInputObserver observer = new(api))
        {
            observer.ActivityObserved += activity =>
            {
                Require(activity.IsLeftButtonDown, "raw observer left click edge");
                observations++;
            };
            Require(observer.Start((nint)123), "raw observer starts");
            Require(observer.Start((nint)123), "raw observer start idempotent");
            Require(api.StartCount == 1, "raw observer has single ownership");
            api.NextActivity = new RawPointerActivity(true, true);
            Require(
                observer.ProcessMessage(RawMouseInputObserver.WmInput, (nint)9),
                "raw input message handled");
            Require(observations == 1, "raw click delivered exactly once");
            observer.Stop();
            Require(!observer.IsActive && api.StopCount == 1, "raw observer stops");
            Require(
                !observer.ProcessMessage(RawMouseInputObserver.WmInput, (nint)10),
                "stopped observer leaves no input processing");
        }
        Require(api.StopCount == 1, "disposed stopped observer has no residue");

        FakeRawMouseInputApi disposeApi = new();
        RawMouseInputObserver active = new(disposeApi);
        Require(active.Start((nint)456), "raw observer starts before close");
        active.Dispose();
        Require(
            !active.IsActive && disposeApi.StopCount == 1,
            "close disposal unregisters raw observer");
    }

    private static void TestHotkeyBindings()
    {
        Require(HotkeyBindings.All.Count == 2, "exactly two camera hotkeys");
        Require(
            HotkeyBindings.TryResolveVirtualKey(
                HotkeyBindings.VkF9,
                out HotkeyBinding f9) &&
            f9.Command == CameraCommand.ToggleStandardCloseUp,
            "F9 maps only to standard");
        Require(
            HotkeyBindings.TryResolveVirtualKey(
                HotkeyBindings.VkF10,
                out HotkeyBinding f10) &&
            f10.Command == CameraCommand.ToggleStrongCloseUp,
            "F10 maps only to strong");
        Require(
            HotkeyBindings.TryResolveId(HotkeyBindings.F9Id, out f9) &&
            f9.Command == CameraCommand.ToggleStandardCloseUp,
            "F9 id maps to standard");
        Require(
            HotkeyBindings.TryResolveId(HotkeyBindings.F10Id, out f10) &&
            f10.Command == CameraCommand.ToggleStrongCloseUp,
            "F10 id maps to strong");
        Require(
            HotkeyBindings.Standard.DisplayName.Contains("切换", StringComparison.Ordinal) &&
            HotkeyBindings.Standard.DisplayName.Contains("1.0x", StringComparison.Ordinal) &&
            HotkeyBindings.Strong.DisplayName.Contains("切换", StringComparison.Ordinal) &&
            HotkeyBindings.Strong.DisplayName.Contains("1.0x", StringComparison.Ordinal),
            "UI mapping describes the real F9/F10 toggle semantics");
        Require(
            !HotkeyBindings.TryResolveVirtualKey(0x77, out _),
            "F8 is unassigned");
        Require(
            !HotkeyBindings.TryResolveVirtualKey(0x7B, out _),
            "F12 is unassigned");
        Require(
            Enum.GetValues<CameraCommand>().Length == 2,
            "camera commands contain no exit command");
        Require(
            HotkeyBindings.All.Select(binding => binding.Id).Distinct().Count() ==
            HotkeyBindings.All.Count,
            "hotkey ids are unique");
        Require(
            HotkeyBindings.All.Select(binding => binding.VirtualKey).Distinct().Count() ==
            HotkeyBindings.All.Count,
            "hotkey virtual keys are unique");
    }

    private static void TestHotkeyActivation()
    {
        FakeHotkeyRegistrar registrar = new();
        using HotkeyService service = new((nint)123, registrar);

        Require(
            service.State == HotkeyActivationState.NotAvailable,
            "host hotkeys initially not available");
        Require(!service.CanToggle, "toggle unavailable before preview");
        Require(
            !service.Enable().Succeeded && registrar.RegisterCalls.Count == 0,
            "enable before preview does not register");
        Require(
            !service.IsRegistered(HotkeyBindings.Standard) &&
            !service.IsRegistered(HotkeyBindings.Strong),
            "F9/F10 absent before preview");
        Require(
            !service.CanDispatch(HotkeyBindings.Standard) &&
            !service.CanDispatch(HotkeyBindings.Strong),
            "default local/global dispatch gate closed");

        service.SetPreviewAvailable(true);
        Require(
            service.State == HotkeyActivationState.Disabled &&
            service.CanToggle,
            "preview start defaults disabled but toggle available");
        Require(
            registrar.RegisterCalls.Count == 0,
            "preview start does not auto-register");

        HotkeyRegistrationResult enabled = service.Enable();
        Require(
            enabled.Succeeded &&
            service.State == HotkeyActivationState.Enabled,
            "explicit enable succeeds");
        Require(
            service.IsRegistered(HotkeyBindings.Standard) &&
            service.IsRegistered(HotkeyBindings.Strong),
            "explicit enable atomically owns F9/F10");
        Require(
            service.CanDispatch(HotkeyBindings.Standard) &&
            service.CanDispatch(HotkeyBindings.Strong),
            "enabled local/global dispatch gate open");
        Require(
            registrar.RegisterCalls.SequenceEqual(
                [HotkeyBindings.F9Id, HotkeyBindings.F10Id]),
            "registration uses F9 then F10");

        int registerCount = registrar.RegisterCalls.Count;
        service.Enable();
        Require(
            registrar.RegisterCalls.Count == registerCount,
            "repeated enable is idempotent");

        FixedTargetCameraController camera = new();
        camera.SetPreviewRunning(true, 1);
        CameraState beforeDisable = camera.Snapshot(2);
        service.Disable();
        CameraState afterDisable = camera.Snapshot(2);
        Require(
            afterDisable.Sequence == beforeDisable.Sequence + 1 &&
            afterDisable.TimestampQpc == beforeDisable.TimestampQpc &&
            afterDisable.Enabled == beforeDisable.Enabled &&
            afterDisable.Mode == beforeDisable.Mode &&
            afterDisable.Zoom == beforeDisable.Zoom &&
            afterDisable.CenterX == beforeDisable.CenterX &&
            afterDisable.CenterY == beforeDisable.CenterY &&
            afterDisable.TargetX == beforeDisable.TargetX &&
            afterDisable.TargetY == beforeDisable.TargetY &&
            afterDisable.AnimationStartZoom == beforeDisable.AnimationStartZoom &&
            afterDisable.AnimationStartCenterX == beforeDisable.AnimationStartCenterX &&
            afterDisable.AnimationStartCenterY == beforeDisable.AnimationStartCenterY,
            "disable leaves camera geometry and timeline unchanged");
        Require(
            service.State == HotkeyActivationState.Disabled &&
            !service.IsRegistered(HotkeyBindings.Standard) &&
            !service.IsRegistered(HotkeyBindings.Strong),
            "disable releases both keys");
        Require(
            !service.CanDispatch(HotkeyBindings.Standard) &&
            !service.CanDispatch(HotkeyBindings.Strong),
            "disabled local/global and queued-message gate closed");
        Require(
            registrar.UnregisterCalls.SequenceEqual(
                [HotkeyBindings.F10Id, HotkeyBindings.F9Id]),
            "disable unregisters in reverse order");

        int unregisterCount = registrar.UnregisterCalls.Count;
        service.Disable();
        Require(
            registrar.UnregisterCalls.Count == unregisterCount,
            "repeated disable is idempotent");

        Require(
            camera.Execute(
                CameraCommand.ToggleStandardCloseUp,
                new CameraPoint(0.4, 0.4),
                3,
                out _),
            "UI camera command remains usable with hotkeys disabled");

        service.Enable();
        Require(
            service.CanDispatch(HotkeyBindings.Standard) &&
            service.CanDispatch(HotkeyBindings.Strong),
            "re-enable restores dispatch");
        service.SetPreviewAvailable(false);
        Require(
            service.State == HotkeyActivationState.NotAvailable &&
            !service.CanToggle &&
            !service.CanDispatch(HotkeyBindings.Standard) &&
            service.UserEnabled,
            "preview stop releases keys but retains the Session preference");

        int stoppedRegisterCount = registrar.RegisterCalls.Count;
        service.SetPreviewAvailable(true);
        Require(
            service.State == HotkeyActivationState.Enabled &&
            registrar.RegisterCalls.Count == stoppedRegisterCount + 2 &&
            service.CanDispatch(HotkeyBindings.Standard),
            "next Preview start restores the enabled Session preference");

        int directorUnregisterCount = registrar.UnregisterCalls.Count;
        service.SetDirectorOwnsCamera(true);
        Require(
            service.State == HotkeyActivationState.SuspendedByDirector &&
            service.UserEnabled && service.IsSuspendedByDirector &&
            !service.CanToggle &&
            !service.CanDispatch(HotkeyBindings.Standard) &&
            !service.CanDispatch(HotkeyBindings.Strong) &&
            registrar.UnregisterCalls.Count == directorUnregisterCount + 2,
            "Director temporarily releases F9/F10 without overwriting preference");
        int directorRegisterCount = registrar.RegisterCalls.Count;
        service.SetDirectorOwnsCamera(false);
        Require(
            service.State == HotkeyActivationState.Enabled &&
            service.UserEnabled &&
            service.CanDispatch(HotkeyBindings.Standard) &&
            service.CanDispatch(HotkeyBindings.Strong) &&
            registrar.RegisterCalls.Count == directorRegisterCount + 2,
            "Manual restores F9/F10 when the saved preference was on");

        service.Disable();
        Require(!service.UserEnabled, "explicit shortcut OFF updates preference");
        service.SetDirectorOwnsCamera(true);
        Require(
            service.State == HotkeyActivationState.SuspendedByDirector &&
            !service.UserEnabled && !service.CanDispatch(HotkeyBindings.Standard),
            "Director also owns the camera when saved preference was off");
        int offRegisterCount = registrar.RegisterCalls.Count;
        service.SetDirectorOwnsCamera(false);
        Require(
            service.State == HotkeyActivationState.Disabled &&
            !service.UserEnabled &&
            registrar.RegisterCalls.Count == offRegisterCount,
            "Manual keeps F9/F10 off when the saved preference was off");

        TestAtomicRegistrationFailure(
            [HotkeyBindings.Standard, HotkeyBindings.Strong],
            HotkeyBindings.F10Id,
            HotkeyBindings.Standard,
            "F9 rollback when F10 fails");
        TestAtomicRegistrationFailure(
            [HotkeyBindings.Strong, HotkeyBindings.Standard],
            HotkeyBindings.F9Id,
            HotkeyBindings.Strong,
            "F10 rollback when F9 fails");

        using (HotkeyService disabledDispose =
            new((nint)124, new FakeHotkeyRegistrar()))
        {
            disabledDispose.SetPreviewAvailable(true);
        }

        FakeHotkeyRegistrar enabledDisposeRegistrar = new();
        using (HotkeyService enabledDispose =
            new((nint)125, enabledDisposeRegistrar))
        {
            enabledDispose.SetPreviewAvailable(true);
            enabledDispose.Enable();
        }
        Require(
            enabledDisposeRegistrar.ActiveIds.Count == 0,
            "Dispose releases Enabled registrations");

        FakeHotkeyRegistrar failedDisposeRegistrar = new()
        {
            FailedRegistrationId = HotkeyBindings.F10Id,
            FailureErrorCode = 1409,
        };
        using (HotkeyService failedDispose =
            new((nint)126, failedDisposeRegistrar))
        {
            failedDispose.SetPreviewAvailable(true);
            failedDispose.Enable();
            Require(
                failedDispose.State == HotkeyActivationState.Failed,
                "failed state represented explicitly");
        }
        Require(
            failedDisposeRegistrar.ActiveIds.Count == 0,
            "Dispose is safe after Failed registration");
    }

    private static void TestAtomicRegistrationFailure(
        IReadOnlyList<HotkeyBinding> bindings,
        int failedId,
        HotkeyBinding expectedRollback,
        string message)
    {
        FakeHotkeyRegistrar registrar = new()
        {
            FailedRegistrationId = failedId,
            FailureErrorCode = 1409,
        };
        using HotkeyService service = new((nint)456, registrar, bindings);
        service.SetPreviewAvailable(true);
        HotkeyRegistrationResult result = service.Enable();

        Require(!result.Succeeded, $"{message}: result fails");
        Require(
            service.State == HotkeyActivationState.Failed &&
            !service.CanDispatch(HotkeyBindings.Standard) &&
            !service.CanDispatch(HotkeyBindings.Strong),
            $"{message}: no half-enabled state");
        Require(
            result.FailedBinding?.Id == failedId &&
            result.WindowsErrorCode == 1409,
            $"{message}: failed key and Windows error retained");
        Require(
            registrar.UnregisterCalls.Contains(expectedRollback.Id) &&
            registrar.ActiveIds.Count == 0,
            $"{message}: successful peer rolled back");
        FixedTargetCameraController camera = new();
        camera.SetPreviewRunning(true, 1);
        Require(
            camera.Execute(
                CameraCommand.ToggleStandardCloseUp,
                new CameraPoint(0.5, 0.5),
                2,
                out _),
            $"{message}: registration conflict does not disable UI camera fallback");
    }

    private static void TestComfortZoneMath()
    {
        CameraPoint center = new(0.5, 0.5);
        ComfortZoneCalculation centered = ComfortZoneMath.Calculate(
            1.6,
            center,
            center);
        Require(centered.IsValid && centered.FollowAllowed, "comfort center valid");
        Near(centered.Bounds.Left, 0.36875, "comfort left", 1e-12);
        Near(centered.Bounds.Right, 0.63125, "comfort right", 1e-12);
        Near(centered.Bounds.Top, 0.3875, "comfort top", 1e-12);
        Near(centered.Bounds.Bottom, 0.6125, "comfort bottom", 1e-12);
        Require(
            !centered.FollowActiveX && !centered.FollowActiveY,
            "cursor centered stays still");

        ComfortZoneCalculation strongCentered = ComfortZoneMath.Calculate(
            CameraSettings.StrongZoom,
            center,
            center);
        Require(
            strongCentered.IsValid && strongCentered.FollowAllowed,
            "strong comfort center valid");
        Near(strongCentered.Bounds.Left, 0.395, "strong comfort left", 1e-12);
        Near(strongCentered.Bounds.Right, 0.605, "strong comfort right", 1e-12);
        Near(strongCentered.Bounds.Top, 0.41, "strong comfort top", 1e-12);
        Near(strongCentered.Bounds.Bottom, 0.59, "strong comfort bottom", 1e-12);

        ComfortZoneCalculation inside = ComfortZoneMath.Calculate(
            1.6,
            center,
            new CameraPoint(0.55, 0.55));
        Near(inside.DesiredCenter.X, 0.5, "inside desired x", 1e-12);
        Near(inside.DesiredCenter.Y, 0.5, "inside desired y", 1e-12);

        foreach (CameraPoint boundary in new[]
        {
            new CameraPoint(centered.Bounds.Left, 0.5),
            new CameraPoint(centered.Bounds.Right, 0.5),
            new CameraPoint(0.5, centered.Bounds.Top),
            new CameraPoint(0.5, centered.Bounds.Bottom),
        })
        {
            ComfortZoneCalculation exact = ComfortZoneMath.Calculate(
                1.6,
                center,
                boundary);
            Require(
                !exact.FollowActiveX && !exact.FollowActiveY,
                "exact comfort boundary is inside");
        }

        ComfortZoneCalculation left = ComfortZoneMath.Calculate(
            1.6, center, new CameraPoint(0.2, 0.5));
        ComfortZoneCalculation right = ComfortZoneMath.Calculate(
            1.6, center, new CameraPoint(0.8, 0.5));
        ComfortZoneCalculation top = ComfortZoneMath.Calculate(
            1.6, center, new CameraPoint(0.5, 0.2));
        ComfortZoneCalculation bottom = ComfortZoneMath.Calculate(
            1.6, center, new CameraPoint(0.5, 0.8));
        Require(left.OutsideLeft && left.FollowActiveX, "outside left");
        Require(right.OutsideRight && right.FollowActiveX, "outside right");
        Require(top.OutsideTop && top.FollowActiveY, "outside top");
        Require(bottom.OutsideBottom && bottom.FollowActiveY, "outside bottom");
        Near(
            left.DesiredCenter.X - (left.Bounds.Right - 0.5),
            0.2,
            "left moves only enough",
            1e-12);
        Near(
            right.DesiredCenter.X + (right.Bounds.Right - 0.5),
            0.8,
            "right moves only enough",
            1e-12);
        Near(
            top.DesiredCenter.Y - (top.Bounds.Bottom - 0.5),
            0.2,
            "top moves only enough",
            1e-12);
        Near(
            bottom.DesiredCenter.Y + (bottom.Bounds.Bottom - 0.5),
            0.8,
            "bottom moves only enough",
            1e-12);

        ComfortZoneCalculation diagonal = ComfortZoneMath.Calculate(
            1.6,
            center,
            new CameraPoint(0.8, 0.8));
        Require(
            diagonal.FollowActiveX && diagonal.FollowActiveY,
            "diagonal follows both axes");

        foreach (CameraPoint corner in new[]
        {
            new CameraPoint(0, 0), new CameraPoint(1, 0),
            new CameraPoint(0, 1), new CameraPoint(1, 1),
        })
        {
            ComfortZoneCalculation edge = ComfortZoneMath.Calculate(
                1.6,
                center,
                corner);
            Require(
                edge.DesiredCenter.X is >= 0.3125 and <= 0.6875 &&
                edge.DesiredCenter.Y is >= 0.3125 and <= 0.6875,
                "four-edge desired center clamp");
        }

        CameraPoint portraitCursor = CameraMath.NormalizeCursor(
            600, 600, 0, 0, 800, 1200);
        ComfortZoneCalculation portrait = ComfortZoneMath.Calculate(
            1.6,
            center,
            portraitCursor);
        Require(
            portrait.IsValid &&
            CameraMath.IsFinite(portrait.DesiredCenter.X) &&
            CameraMath.IsFinite(portrait.DesiredCenter.Y),
            "non-16:9 normalized capture");

        ComfortZoneCalculation wide = ComfortZoneMath.Calculate(
            1.0,
            new CameraPoint(0.2, 0.8),
            new CameraPoint(1.0, 0.0));
        Require(wide.IsValid && !wide.FollowAllowed, "zoom=1 follow disabled");
        Near(wide.DesiredCenter.X, 0.5, "wide desired x", 1e-12);
        Near(wide.DesiredCenter.Y, 0.5, "wide desired y", 1e-12);

        Require(
            !ComfortZoneMath.Calculate(
                double.NaN,
                center,
                center).IsValid,
            "NaN zoom rejected");
        Require(
            !ComfortZoneMath.Calculate(
                double.PositiveInfinity,
                center,
                center).IsValid,
            "infinite zoom rejected");
        Require(
            !ComfortZoneMath.Calculate(
                1.6,
                center,
                new CameraPoint(double.NaN, 0.5)).IsValid,
            "non-finite cursor rejected");
    }

    private static void TestFollowSmoother()
    {
        CameraFollowSmoothResult at60 = SimulateFollow(1.0 / 60.0, 0.5);
        CameraFollowSmoothResult at120 = SimulateFollow(1.0 / 120.0, 0.5);
        Near(at60.Center.X, at120.Center.X, "frame-rate-independent center", 2e-5);
        Near(at60.VelocityX, at120.VelocityX, "frame-rate-independent velocity", 2e-4);

        CameraFollowSmoother monotonic = new();
        CameraPoint current = new(0.5, 0.5);
        double previous = current.X;
        for (int index = 0; index < 120; index++)
        {
            CameraFollowSmoothResult step = monotonic.Step(
                1.6,
                current,
                new CameraPoint(0.65, 0.5),
                true,
                false,
                1.0 / 120.0);
            Require(step.Center.X >= previous - 1e-12, "smoother monotonic");
            Require(step.Center.X <= 0.65 + 1e-12, "smoother no overshoot");
            Near(step.Center.Y, 0.5, "independent Y axis", 1e-12);
            previous = step.Center.X;
            current = step.Center;
        }
        Require(
            Math.Abs(monotonic.VelocityX) <=
                ComfortZoneSettings.StopVelocityEpsilon,
            "stationary velocity reaches zero");

        CameraFollowSmoother largeDelta = new();
        CameraFollowSmoothResult safe = largeDelta.Step(
            1.6,
            new CameraPoint(0.5, 0.5),
            new CameraPoint(0.65, 0.35),
            true,
            true,
            10.0);
        Require(
            safe.Center.X is >= 0.3125 and <= 0.6875 &&
            safe.Center.Y is >= 0.3125 and <= 0.6875,
            "large delta remains bounded");
        CameraFollowSmoother cappedReference = new();
        CameraFollowSmoothResult cappedStep = cappedReference.Step(
            1.6,
            new CameraPoint(0.5, 0.5),
            new CameraPoint(0.65, 0.35),
            true,
            true,
            ComfortZoneSettings.MaximumDeltaSeconds);
        Near(safe.Center.X, cappedStep.Center.X, "follow large delta cap x", 1e-12);
        Near(safe.Center.Y, cappedStep.Center.Y, "follow large delta cap y", 1e-12);
        Near(safe.VelocityX, cappedStep.VelocityX, "follow large delta cap velocity", 1e-12);

        CameraFollowSmoother movingTarget = new();
        CameraPoint movingCenter = new(0.5, 0.5);
        for (int index = 0; index < 30; index++)
        {
            CameraFollowSmoothResult moving = movingTarget.Step(
                1.6,
                movingCenter,
                new CameraPoint(0.55 + (index * 0.002), 0.5),
                true,
                false,
                1.0 / 120.0);
            movingCenter = moving.Center;
        }
        double movingVelocity = movingTarget.VelocityX;
        Require(movingVelocity > 0.0, "follow moving target has velocity state");
        CameraFollowSmoothResult retargeted = movingTarget.Step(
            1.6,
            movingCenter,
            new CameraPoint(0.67, 0.5),
            true,
            false,
            1.0 / 120.0);
        Require(
            retargeted.VelocityX > 0.0 && retargeted.Center.X > movingCenter.X,
            "follow retarget preserves continuous motion");

        CameraPoint stoppedCenter = retargeted.Center;
        double previousSpeed = Math.Abs(retargeted.VelocityX);
        bool sawVisibleDecay = false;
        CameraFollowSmoothResult stopped = retargeted;
        for (int index = 0; index < 240; index++)
        {
            stopped = movingTarget.Step(
                1.6,
                stoppedCenter,
                new CameraPoint(0.65, 0.5),
                true,
                false,
                1.0 / 120.0);
            double speed = Math.Abs(stopped.VelocityX);
            if (speed > ComfortZoneSettings.StopVelocityEpsilon &&
                speed < previousSpeed - 1e-6)
            {
                sawVisibleDecay = true;
            }
            previousSpeed = speed;
            stoppedCenter = stopped.Center;
        }
        Require(sawVisibleDecay, "follow stop includes continuous velocity decay");
        Near(stopped.Center.X, 0.65, "follow stop reaches target", 1e-12);
        Near(stopped.VelocityX, 0.0, "follow stop clears long tail", 1e-12);

        CameraFollowSmoother edge = new();
        CameraPoint edgeCurrent = new(0.5, 0.5);
        for (int index = 0; index < 10; index++)
        {
            CameraFollowSmoothResult step = edge.Step(
                1.6,
                edgeCurrent,
                new CameraPoint(1.0, 0.5),
                true,
                false,
                0.032);
            edgeCurrent = step.Center;
        }
        Near(edgeCurrent.X, 0.6875, "edge clamp center", 1e-12);
        Near(edge.VelocityX, 0.0, "edge clamp clears velocity", 1e-12);

        edge.Reset();
        Near(edge.VelocityX, 0.0, "disabled clears x velocity", 1e-12);
        Near(edge.VelocityY, 0.0, "disabled clears y velocity", 1e-12);
    }

    private static CameraFollowSmoothResult SimulateFollow(
        double deltaSeconds,
        double durationSeconds)
    {
        CameraFollowSmoother smoother = new();
        CameraPoint current = new(0.5, 0.5);
        int steps = (int)Math.Round(durationSeconds / deltaSeconds);
        CameraFollowSmoothResult result = default;
        for (int index = 0; index < steps; index++)
        {
            result = smoother.Step(
                1.6,
                current,
                new CameraPoint(0.65, 0.5),
                true,
                false,
                deltaSeconds);
            current = result.Center;
        }
        return result;
    }

    private static void TestFollowStateIntegration()
    {
        FixedTargetCameraController controller = new(frequency: 250);
        controller.SetPreviewRunning(true, 0);
        controller.Execute(
            CameraCommand.ToggleStandardCloseUp,
            new CameraPoint(0.5, 0.5),
            0,
            out _);
        CameraState zooming = controller.Snapshot(100);
        ComfortZoneTracker tracker = new(enabled: true, frequency: 1000);
        Require(
            !tracker.ShouldReadCursor(zooming),
            "ZoomingIn does not read continuous cursor");
        ComfortZoneFollowStep waiting = tracker.Update(
            zooming,
            null,
            0.01,
            100);
        Require(
            waiting.State == FollowState.WaitingForZoom &&
            !waiting.ShouldApplyCenter,
            "ZoomingIn does not follow");

        CameraState zoomed = controller.Snapshot(240);
        Require(zoomed.Mode == CameraMode.ZoomedFixed, "zoom completed");
        int zoomOutCursorReads = 0;
        controller.Execute(
            CameraCommand.ToggleStandardCloseUp,
            () =>
            {
                zoomOutCursorReads++;
                return new CameraPoint(0.1, 0.1);
            },
            241,
            out _);
        Require(
            zoomOutCursorReads == 0,
            "zoom-out does not read a new cursor target");
        controller.Execute(
            CameraCommand.ToggleStandardCloseUp,
            new CameraPoint(0.5, 0.5),
            241,
            out _);
        zoomed = controller.Snapshot(481);
        Require(
            zoomed.Mode == CameraMode.ZoomedFixed,
            "test returns to zoomed state");
        CameraCursorObservation insideCursor = Cursor(0.5, 0.5);
        ComfortZoneFollowStep inside = tracker.Update(
            zoomed,
            insideCursor,
            0.01,
            250);
        Require(
            inside.State == FollowState.InsideComfortZone &&
            !inside.ShouldApplyCenter,
            "inside comfort zone keeps fixed target");

        CameraCursorObservation outsideCursor = Cursor(0.8, 0.5);
        ComfortZoneFollowStep following = tracker.Update(
            zoomed,
            outsideCursor,
            0.032,
            282);
        Require(
            following.State == FollowState.Following &&
            following.ShouldApplyCenter &&
            following.OutputCenter.X > zoomed.CenterX,
            "outside comfort zone follows");
        Require(
            controller.TrySetZoomedCenter(
                zoomed,
                following.OutputCenter,
                out CameraState followed),
            "controller accepts current follow center");
        Near(
            followed.CenterX,
            following.OutputCenter.X,
            "controller follow center");

        controller.Execute(
            CameraCommand.ToggleStandardCloseUp,
            new CameraPoint(0.1, 0.1),
            300,
            out _);
        CameraState zoomingOut = controller.Snapshot(300);
        Require(
            zoomingOut.Mode == CameraMode.ZoomingOut,
            "follow standard command zooms out");
        Near(
            zoomingOut.CenterX,
            followed.CenterX,
            "zoom-out starts at current followed center",
            1e-12);
        ComfortZoneFollowStep noOutFollow = tracker.Update(
            zoomingOut,
            null,
            0.018,
            318);
        Require(
            noOutFollow.State == FollowState.WaitingForZoom &&
            !noOutFollow.ShouldApplyCenter,
            "ZoomingOut stops follow");

        ComfortZoneTracker disabled = new(enabled: false, frequency: 1000);
        ComfortZoneFollowStep disabledStep = disabled.Update(
            zoomed,
            outsideCursor,
            0.032,
            1);
        Require(
            disabledStep.State == FollowState.Disabled &&
            !disabledStep.ShouldApplyCenter,
            "ZoomedFixed disabled equals P1a");

        disabled.SetEnabled(true);
        ComfortZoneFollowStep rearming = disabled.Update(
            zoomed,
            outsideCursor,
            0.032,
            2);
        Require(
            rearming.State == FollowState.Rearming &&
            !rearming.ShouldApplyCenter,
            "re-enable has no instant movement");
        ComfortZoneFollowStep rearmed = disabled.Update(
            zoomed,
            insideCursor,
            0.032,
            3);
        Require(
            rearmed.State == FollowState.InsideComfortZone &&
            !rearmed.ShouldApplyCenter,
            "re-enable arms inside current zone");
        ComfortZoneFollowStep afterCrossing = disabled.Update(
            zoomed,
            outsideCursor,
            0.032,
            4);
        Require(
            afterCrossing.State == FollowState.Following &&
            afterCrossing.ShouldApplyCenter,
            "re-enabled follow starts after boundary crossing");

        ComfortZoneTracker outsideMonitor = new(true, 1000);
        ComfortZoneFollowStep paused = outsideMonitor.Update(
            zoomed,
            Cursor(1.2, 0.5, inside: false),
            0.032,
            1);
        Require(
            paused.State == FollowState.CursorOutsideMonitor &&
            !paused.ShouldApplyCenter &&
            outsideMonitor.Enabled,
            "cursor outside primary pauses follow");
        ComfortZoneFollowStep resumed = outsideMonitor.Update(
            zoomed,
            outsideCursor,
            0.032,
            2);
        Require(
            resumed.State == FollowState.Following,
            "cursor return resumes follow");

        ComfortZoneTracker failing = new(true, 1000);
        ComfortZoneFollowStep failed = failing.Update(
            zoomed,
            CursorFailure(5, "synthetic GetCursorPos failure"),
            0.032,
            1);
        Require(
            failed.State == FollowState.ErrorFallback &&
            !failed.FollowEnabled &&
            failed.FollowErrorCount == 1 &&
            !failed.ShouldApplyCenter,
            "GetCursorPos failure isolates follow");
        Require(
            zoomed.Mode == CameraMode.ZoomedFixed &&
            zoomed.Enabled,
            "follow error preserves P1a camera");

        CameraState wide = CameraState.Wide(100, 100);
        ComfortZoneTracker wideTracker = new(true, 1000);
        ComfortZoneFollowStep wideStep = wideTracker.Update(
            wide,
            null,
            0.032,
            100);
        Require(
            wideStep.State == FollowState.WaitingForZoom &&
            !wideStep.ShouldApplyCenter,
            "wide/preview stopped does not follow");
    }

    private static CameraCursorObservation Cursor(
        double normalizedX,
        double normalizedY,
        bool inside = true) =>
        new(
            true,
            (int)Math.Round(normalizedX * 1920),
            (int)Math.Round(normalizedY * 1080),
            normalizedX,
            normalizedY,
            inside,
            0,
            null);

    private static void TestFrozenP1c2Baseline()
    {
        string candidateRoot = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        string frozenRoot = Path.Combine(
            Directory.GetParent(candidateRoot)!.FullName,
            "p1c2-hotkey-toggle-prototype");
        string manifestPath = Path.Combine(
            frozenRoot,
            "P1C2-FROZEN-HASHES.txt");
        string candidateBaseline = Path.Combine(
            candidateRoot,
            "BASELINE-P1C2-HASHES.txt");
        Require(File.Exists(manifestPath), "P1c.2 frozen manifest exists");
        Require(File.Exists(candidateBaseline), "candidate baseline exists");
        Require(
            File.ReadAllBytes(manifestPath).SequenceEqual(
                File.ReadAllBytes(candidateBaseline)),
            "candidate baseline is exact P1c.2 manifest");

        Regex entry = new(
            "^([0-9A-Fa-f]{64}) \\*(.+)$",
            RegexOptions.CultureInvariant);
        List<(string Hash, string Relative)> records = [];
        foreach (string line in File.ReadLines(manifestPath))
        {
            Match match = entry.Match(line);
            if (match.Success)
            {
                records.Add((
                    match.Groups[1].Value.ToUpperInvariant(),
                    match.Groups[2].Value));
            }
        }
        Require(records.Count == 121, "P1c.2 frozen file count");
        string[] ordinal = records.Select(record => record.Relative).ToArray();
        Array.Sort(ordinal, StringComparer.Ordinal);
        Require(
            records.Select(record => record.Relative).SequenceEqual(
                ordinal,
                StringComparer.Ordinal),
            "P1c.2 manifest Ordinal order");
        foreach ((string expectedHash, string relative) in records)
        {
            string path = Path.Combine(frozenRoot, relative);
            Require(File.Exists(path), $"P1c.2 frozen file exists: {relative}");
            string actualHash = Convert.ToHexString(
                SHA256.HashData(File.ReadAllBytes(path)));
            Require(
                actualHash == expectedHash,
                $"P1c.2 frozen hash: {relative}");
        }
    }

    private static void TestRegionSelectionModels()
    {
        CaptureDisplaySnapshot display = TestDisplay();
        CaptureRegion full = display.FullRegion;
        Require(
            full.Left == 0 &&
            full.Top == 0 &&
            full.Width == 1920 &&
            full.Height == 1080 &&
            full.Right == full.Left + full.Width &&
            full.Bottom == full.Top + full.Height,
            "full region and half-open bounds");
        Require(full.Contains(0, 0), "half-open includes top-left");
        Require(!full.Contains(1920, 1080), "half-open excludes bottom-right");

        CaptureRegion odd = CaptureRegion.Create(
            17,
            19,
            1001,
            701,
            display.Width,
            display.Height);
        OutputCanvas canvas = OutputCanvas.CreateIdentity(odd);
        SessionGeometry geometry = SessionGeometry.Create(display, odd, canvas);
        Require(
            geometry.CaptureRegion.Width == 1001 &&
            geometry.CaptureRegion.Height == 701,
            "odd dimensions are preserved");
        Require(
            geometry.OutputCanvas.Width == odd.Width &&
            geometry.OutputCanvas.Height == odd.Height &&
            geometry.OutputCanvas.ScaleMode == OutputScaleMode.Identity,
            "Identity canvas equals region");
        Require(
            typeof(CaptureRegion) != typeof(OutputCanvas),
            "CaptureRegion and OutputCanvas are separate concepts");
        Require(
            typeof(SessionGeometry).GetProperties().All(
                property => property.SetMethod is null),
            "SessionGeometry has no mutable property setters");
        Require(
            typeof(SessionGeometry).GetProperties().All(
                property =>
                    property.PropertyType.Namespace != "System.Windows.Forms" &&
                    property.PropertyType.FullName !=
                        "System.Drawing.Rectangle"),
            "SessionGeometry contains no UI references");

        RegionSelectionResult confirmed =
            RegionSelectionResult.Confirm(display, odd);
        RegionSelectionResult cancelled = RegionSelectionResult.Cancel();
        Require(
            confirmed.Confirmed &&
            !confirmed.Cancelled &&
            confirmed.Region == odd &&
            confirmed.Display == display,
            "Confirm commits a new region");
        Require(
            cancelled.Cancelled &&
            !cancelled.Confirmed &&
            cancelled.Region is null &&
            cancelled.Display is null,
            "Cancel cannot carry a new region");
        Require(
            cancelled.ResolveDisplay(display) == display &&
            cancelled.ResolveRegion(odd) == odd,
            "Cancel retains the previous confirmed selection");
        CaptureRegion replacement = CaptureRegion.Create(
            30,
            40,
            800,
            600,
            display.Width,
            display.Height);
        RegionSelectionResult replacementResult =
            RegionSelectionResult.Confirm(display, replacement);
        Require(
            replacementResult.ResolveRegion(odd) == replacement,
            "Confirm replaces the previous region");
        Require(
            RegionSelectionResult.Cancel().Equals(cancelled),
            "repeated cancel is idempotent");
        Throws<ArgumentOutOfRangeException>(
            () => CaptureRegion.Create(1900, 1000, 100, 100, 1920, 1080),
            "out-of-source region rejected");
    }

    private static void TestRegionSelectionMath()
    {
        PhysicalPixelPoint a = new(100, 100);
        PhysicalPixelPoint b = new(300, 220);
        CaptureRegion expected = CaptureRegion.Create(
            100,
            100,
            200,
            120,
            1920,
            1080);
        foreach ((PhysicalPixelPoint start, PhysicalPixelPoint end) in new[]
        {
            (a, b),
            (new PhysicalPixelPoint(300, 100), new PhysicalPixelPoint(100, 220)),
            (new PhysicalPixelPoint(100, 220), new PhysicalPixelPoint(300, 100)),
            (b, a),
        })
        {
            Require(
                RegionSelectionMath.TryCreateFromDrag(
                    start,
                    end,
                    1920,
                    1080,
                    RegionAspectMode.Free,
                    out CaptureRegion actual) &&
                actual == expected,
                "four-direction drag normalization");
        }

        Require(
            !RegionSelectionMath.TryCreateFromDrag(
                new PhysicalPixelPoint(10, 10),
                new PhysicalPixelPoint(13, 13),
                1920,
                1080,
                RegionAspectMode.Free,
                out _),
            "sub-threshold drag rejected");
        Require(
            RegionSelectionMath.TryCreateFromDrag(
                new PhysicalPixelPoint(10, 10),
                new PhysicalPixelPoint(14, 14),
                1920,
                1080,
                RegionAspectMode.Free,
                out _),
            "four-physical-pixel drag activates");

        CaptureRegion moved = RegionSelectionMath.Move(
            expected,
            -500,
            2000,
            1920,
            1080);
        Require(
            moved.Left == 0 &&
            moved.Bottom == 1080 &&
            moved.Width == expected.Width &&
            moved.Height == expected.Height,
            "move clamps without resizing");

        Dictionary<RegionResizeHandle, PhysicalPixelPoint> resizePoints = new()
        {
            [RegionResizeHandle.Left] = new(50, 150),
            [RegionResizeHandle.Right] = new(350, 150),
            [RegionResizeHandle.Top] = new(150, 50),
            [RegionResizeHandle.Bottom] = new(150, 260),
            [RegionResizeHandle.TopLeft] = new(50, 50),
            [RegionResizeHandle.TopRight] = new(350, 50),
            [RegionResizeHandle.BottomLeft] = new(50, 260),
            [RegionResizeHandle.BottomRight] = new(350, 260),
        };
        foreach ((RegionResizeHandle handle, PhysicalPixelPoint point) in
            resizePoints)
        {
            CaptureRegion resized = RegionSelectionMath.Resize(
                expected,
                handle,
                point,
                1920,
                1080,
                RegionAspectMode.Free);
            Require(
                resized.IsWithin(1920, 1080) &&
                resized.Width > 0 &&
                resized.Height > 0,
                $"free resize valid: {handle}");

            CaptureRegion ratioA = RegionSelectionMath.Resize(
                expected,
                handle,
                point,
                1920,
                1080,
                RegionAspectMode.Ratio16By9);
            CaptureRegion ratioB = RegionSelectionMath.Resize(
                expected,
                handle,
                point,
                1920,
                1080,
                RegionAspectMode.Ratio16By9);
            Require(
                ratioA == ratioB && ratioA.IsWithin(1920, 1080),
                $"16:9 resize deterministic and bounded: {handle}");
        }

        Require(
            RegionSelectionMath.TryCreateFromDrag(
                new PhysicalPixelPoint(100, 100),
                new PhysicalPixelPoint(300, 300),
                1920,
                1080,
                RegionAspectMode.Ratio16By9,
                out CaptureRegion ratioCreated),
            "16:9 new selection");
        Require(
            ratioCreated.Width == 200 &&
            ratioCreated.Height == 112,
            "16:9 new selection uses inward integer rule");

        CaptureRegion freeOdd = CaptureRegion.Create(
            100,
            100,
            1001,
            701,
            1920,
            1080);
        CaptureRegion fittedA = RegionSelectionMath.FitLargest16By9Inside(
            freeOdd,
            1920,
            1080);
        CaptureRegion fittedB = RegionSelectionMath.FitLargest16By9Inside(
            freeOdd,
            1920,
            1080);
        Require(
            fittedA == fittedB &&
            fittedA.Left >= freeOdd.Left &&
            fittedA.Top >= freeOdd.Top &&
            fittedA.Right <= freeOdd.Right &&
            fittedA.Bottom <= freeOdd.Bottom,
            "free-to-16:9 is deterministic and inward-only");
        Require(
            freeOdd.Width == 1001 && freeOdd.Height == 701,
            "free odd dimensions are not evenized");

        Require(
            RegionSelectionMath.TryResolveExactSize(
                "1001",
                "701",
                RegionAspectMode.Free,
                ExactSizeEditedDimension.Width,
                1920,
                1080,
                out int exactWidth,
                out int exactHeight,
                out _) &&
            exactWidth == 1001 &&
            exactHeight == 701,
            "free exact size preserves independent odd dimensions");
        Require(
            RegionSelectionMath.TryResolveExactSize(
                "1600",
                "ignored",
                RegionAspectMode.Ratio16By9,
                ExactSizeEditedDimension.Width,
                1920,
                1080,
                out int ratioWidth,
                out int ratioHeight,
                out _) &&
            ratioWidth == 1600 &&
            ratioHeight == 900,
            "16:9 width edit deterministically calculates height");
        Require(
            RegionSelectionMath.TryResolveExactSize(
                "ignored",
                "900",
                RegionAspectMode.Ratio16By9,
                ExactSizeEditedDimension.Height,
                1920,
                1080,
                out ratioWidth,
                out ratioHeight,
                out _) &&
            ratioWidth == 1600 &&
            ratioHeight == 900,
            "16:9 height edit deterministically calculates width");
        Require(
            RegionSelectionMath.TryCalculateLinkedDimension(
                "1001",
                ExactSizeEditedDimension.Width,
                out int linkedHeight) &&
            linkedHeight == 563,
            "16:9 live width linkage uses inward integer rule");
        Require(
            RegionSelectionMath.TryCalculateLinkedDimension(
                "701",
                ExactSizeEditedDimension.Height,
                out int linkedWidth) &&
            linkedWidth == 1246,
            "16:9 live height linkage uses inward integer rule");

        CaptureRegion centeredSource = CaptureRegion.Create(
            800,
            400,
            200,
            100,
            1920,
            1080);
        CaptureRegion exactCentered = RegionSelectionMath.ApplyExactSize(
            centeredSource,
            400,
            300,
            1920,
            1080);
        Require(
            (exactCentered.Left * 2) + exactCentered.Width ==
                (centeredSource.Left * 2) + centeredSource.Width &&
            (exactCentered.Top * 2) + exactCentered.Height ==
                (centeredSource.Top * 2) + centeredSource.Height,
            "exact size preserves center when bounds permit");

        CaptureRegion nearLeftTop = CaptureRegion.Create(
            0,
            0,
            100,
            100,
            1920,
            1080);
        CaptureRegion leftTopApplied = RegionSelectionMath.ApplyExactSize(
            nearLeftTop,
            500,
            400,
            1920,
            1080);
        Require(
            leftTopApplied.Left == 0 &&
            leftTopApplied.Top == 0 &&
            leftTopApplied.Width == 500 &&
            leftTopApplied.Height == 400,
            "exact size translates inside left/top boundaries without shrinking");

        CaptureRegion nearRightBottom = CaptureRegion.Create(
            1820,
            980,
            100,
            100,
            1920,
            1080);
        CaptureRegion rightBottomApplied = RegionSelectionMath.ApplyExactSize(
            nearRightBottom,
            500,
            400,
            1920,
            1080);
        Require(
            rightBottomApplied.Right == 1920 &&
            rightBottomApplied.Bottom == 1080 &&
            rightBottomApplied.Width == 500 &&
            rightBottomApplied.Height == 400,
            "exact size translates inside right/bottom boundaries without shrinking");

        foreach ((string widthText, string heightText) in new[]
        {
            ("abc", "701"),
            ("0", "701"),
            ("-1", "701"),
            ("999999999999999999999", "701"),
        })
        {
            Require(
                !RegionSelectionMath.TryResolveExactSize(
                    widthText,
                    heightText,
                    RegionAspectMode.Free,
                    ExactSizeEditedDimension.Width,
                    1920,
                    1080,
                    out _,
                    out _,
                    out _),
                $"invalid exact size rejected: {widthText}");
        }
        CaptureRegion beforeRejectedSize = freeOdd;
        Require(
            !RegionSelectionMath.TryResolveExactSize(
                "1921",
                "1080",
                RegionAspectMode.Free,
                ExactSizeEditedDimension.Width,
                1920,
                1080,
                out _,
                out _,
                out string? oversizedError) &&
            oversizedError!.Contains("1920 × 1080", StringComparison.Ordinal) &&
            freeOdd == beforeRejectedSize,
            "oversized exact input is rejected without changing the region");
        Require(
            !RegionSelectionMath.TryResolveExactSize(
                "ignored",
                int.MaxValue.ToString(CultureInfo.InvariantCulture),
                RegionAspectMode.Ratio16By9,
                ExactSizeEditedDimension.Height,
                1920,
                1080,
                out _,
                out _,
                out string? linkedOverflowError) &&
            !string.IsNullOrWhiteSpace(linkedOverflowError),
            "16:9 linked integer overflow is rejected without an exception");

        (int fitWidth, int fitHeight) =
            RegionSelectionMath.Fit16By9Dimensions(int.MaxValue, int.MaxValue);
        Require(
            fitWidth > 0 &&
            fitHeight > 0 &&
            fitWidth <= int.MaxValue &&
            fitHeight <= int.MaxValue,
            "16:9 uses overflow-safe 64-bit intermediates");
    }

    private static void TestRegionSelectionStateAndStartPolicy()
    {
        RegionSelectionStateMachine state = new();
        Require(
            state.TryTransition(RegionSelectionState.Drawing) &&
            state.TryTransition(RegionSelectionState.Selected) &&
            state.TryTransition(RegionSelectionState.Moving) &&
            state.TryTransition(RegionSelectionState.Selected) &&
            state.TryTransition(RegionSelectionState.Resizing) &&
            state.TryTransition(RegionSelectionState.Selected) &&
            state.TryTransition(RegionSelectionState.Confirmed),
            "legal selection state transitions");
        Require(
            state.TryTransition(RegionSelectionState.Confirmed),
            "repeated Confirm is idempotent");
        Require(
            !state.TryTransition(RegionSelectionState.Drawing),
            "terminal Confirm rejects later transitions");

        RegionSelectionStateMachine cancelState = new();
        Require(
            cancelState.TryTransition(RegionSelectionState.Cancelled) &&
            cancelState.TryTransition(RegionSelectionState.Cancelled),
            "Esc/Alt+F4 Cancel transition is idempotent");
        RegionSelectionStateMachine illegal = new();
        Require(
            !illegal.TryTransition(RegionSelectionState.Moving) &&
            !illegal.TryTransition(RegionSelectionState.Resizing) &&
            !illegal.TryTransition(RegionSelectionState.Confirmed),
            "illegal state transitions rejected");

        Require(
            RegionSelectionAvailability.CanSelectRegion(
                false,
                false,
                true,
                false),
            "Stopped permits region selection");
        Require(
            !RegionSelectionAvailability.CanSelectRegion(
                false,
                true,
                false,
                false),
            "Running rejects region selection");
        Require(
            !RegionSelectionAvailability.CanSelectRegion(
                false,
                false,
                true,
                true),
            "active Overlay rejects second selection");

        CaptureDisplaySnapshot display = TestDisplay();
        CaptureRegion region = CaptureRegion.Create(
            100,
            100,
            1001,
            701,
            display.Width,
            display.Height);
        SessionStartPlan full = SessionGeometryPlanner.CreateStartPlan(
            CaptureRangeMode.FullScreen,
            display,
            null,
            null,
            false);
        Require(
            full.StartNativePreview &&
            full.Geometry.CaptureRegion == display.FullRegion,
            "full-screen Start keeps native path");

        SessionStartPlan custom = SessionGeometryPlanner.CreateStartPlan(
            CaptureRangeMode.CustomRegion,
            display,
            display,
            region,
            false);
        Require(
            custom.StartNativePreview &&
            custom.Geometry.CaptureRegion == region,
            "custom-region geometry can reach the native configuration chain");
        Throws<InvalidOperationException>(
            () => SessionGeometryPlanner.CreateStartPlan(
                CaptureRangeMode.CustomRegion,
                display,
                display,
                region,
                true),
            "Overlay transaction rejects Start");

        CaptureDisplaySnapshot changedDevice = CaptureDisplaySnapshot.Create(
            "\\\\.\\DISPLAY2",
            0,
            0,
            1920,
            1080,
            96,
            96);
        CaptureDisplaySnapshot changedBounds = CaptureDisplaySnapshot.Create(
            "\\\\.\\DISPLAY1",
            10,
            0,
            1920,
            1080,
            96,
            96);
        CaptureDisplaySnapshot changedDpi = CaptureDisplaySnapshot.Create(
            "\\\\.\\DISPLAY1",
            0,
            0,
            1920,
            1080,
            120,
            120);
        foreach (CaptureDisplaySnapshot changed in new[]
        {
            changedDevice,
            changedBounds,
            changedDpi,
        })
        {
            Throws<InvalidOperationException>(
                () => SessionGeometryPlanner.CreateStartPlan(
                    CaptureRangeMode.CustomRegion,
                    changed,
                    display,
                    region,
                    false),
                "changed display snapshot rejects custom Start");
        }
    }

    private static CaptureDisplaySnapshot TestDisplay() =>
        CaptureDisplaySnapshot.Create(
            "\\\\.\\DISPLAY1",
            0,
            0,
            1920,
            1080,
            96,
            96);

    private static void Throws<TException>(
        Action action,
        string message)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }
        throw new InvalidOperationException(message);
    }

    private static void AssertContinuousRetarget(
        FixedTargetCameraController controller,
        CameraCommand command,
        CameraPoint target,
        long nowQpc,
        CameraState before,
        string message)
    {
        Require(
            controller.Execute(command, target, nowQpc, out _),
            $"{message}: command accepted");
        CameraState after = controller.Snapshot(nowQpc);
        Near(after.Zoom, before.Zoom, $"{message}: zoom continuity", 1e-12);
        Near(after.CenterX, before.CenterX, $"{message}: x continuity", 1e-12);
        Near(after.CenterY, before.CenterY, $"{message}: y continuity", 1e-12);
        Require(
            after.Sequence > before.Sequence,
            $"{message}: sequence monotonic");
    }

    private static void AssertPreset(
        CameraState state,
        double expectedZoom,
        CameraMode expectedMode,
        string message)
    {
        Near(state.Zoom, expectedZoom, $"{message}: zoom", 1e-12);
        Require(state.Mode == expectedMode, $"{message}: mode");
        Require(state.Enabled, $"{message}: enabled");
        double halfView = 0.5 / expectedZoom;
        Require(
            state.CenterX >= halfView - 1e-12 &&
            state.CenterX <= 1.0 - halfView + 1e-12 &&
            state.CenterY >= halfView - 1e-12 &&
            state.CenterY <= 1.0 - halfView + 1e-12,
            $"{message}: legal center");
    }

    private static void AssertWide(CameraState state, string message)
    {
        Near(state.Zoom, CameraSettings.WideZoom, $"{message}: zoom", 1e-12);
        Near(state.CenterX, 0.5, $"{message}: center x", 1e-12);
        Near(state.CenterY, 0.5, $"{message}: center y", 1e-12);
        Require(state.Mode == CameraMode.Wide, $"{message}: mode");
        Require(!state.Enabled, $"{message}: disabled");
    }

    private static CameraCursorObservation CursorFailure(
        int error,
        string message) =>
        new(false, 0, 0, 0.0, 0.0, false, error, message);

    private sealed class FakeRawMouseInputApi : IRawMouseInputApi
    {
        internal int StartCount { get; private set; }
        internal int StopCount { get; private set; }
        internal RawPointerActivity NextActivity { get; set; }

        public bool Register(
            nint targetWindow,
            bool remove,
            out int windowsError)
        {
            if (remove)
            {
                StopCount++;
            }
            else
            {
                StartCount++;
            }
            windowsError = 0;
            return true;
        }

        public bool TryRead(
            nint rawInputHandle,
            out RawPointerActivity activity,
            out int windowsError)
        {
            activity = NextActivity;
            windowsError = 0;
            return true;
        }
    }

    private sealed class FakeHotkeyRegistrar : IHotkeyRegistrar
    {
        internal int? FailedRegistrationId { get; init; }
        internal int FailureErrorCode { get; init; }
        internal List<int> RegisterCalls { get; } = [];
        internal List<int> UnregisterCalls { get; } = [];
        internal HashSet<int> ActiveIds { get; } = [];

        public bool Register(
            nint window,
            int id,
            uint modifiers,
            uint virtualKey,
            out int windowsErrorCode)
        {
            RegisterCalls.Add(id);
            if (id == FailedRegistrationId)
            {
                windowsErrorCode = FailureErrorCode;
                return false;
            }

            windowsErrorCode = 0;
            ActiveIds.Add(id);
            return true;
        }

        public bool Unregister(nint window, int id)
        {
            UnregisterCalls.Add(id);
            return ActiveIds.Remove(id);
        }
    }

    private static void Near(double actual, double expected, string message, double tolerance = 0.01)
    {
        Require(Math.Abs(actual - expected) <= tolerance, $"{message}: {actual} != {expected}");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
