using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Avalonia;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using XbPreview.Avalonia.Controls;
using XbPreview.Avalonia.Views;
using AvaloniaBorder = Avalonia.Controls.Border;
using AvaloniaControl = Avalonia.Controls.Control;
using AvaloniaPanel = Avalonia.Controls.Panel;
using AvaloniaTemplatedControl = Avalonia.Controls.Primitives.TemplatedControl;

namespace XbPreview.Host;

internal sealed record StructuralShellPerformanceGateRequest(
    string EvidencePath,
    string DiagnosticDirectory,
    bool PresentationOnly,
    bool NavigationDiagnosisOnly,
    bool NavigationPrecheckOnly,
    bool MoveDiagnosisOnly,
    bool MoveValidationOnly,
    bool WhiteFrameClassificationOnly,
    bool WhiteFramePrecheckOnly)
{
    internal static StructuralShellPerformanceGateRequest Parse(
        string[] args,
        int selectorIndex)
    {
        if (selectorIndex < 0 || selectorIndex + 1 >= args.Length ||
            string.IsNullOrWhiteSpace(args[selectorIndex + 1]))
        {
            throw new ArgumentException(
                "--skill-ui-structural-gate requires an evidence JSON path.");
        }

        string evidencePath = Path.GetFullPath(args[selectorIndex + 1]);
        string? evidenceDirectory = Path.GetDirectoryName(evidencePath);
        if (string.IsNullOrWhiteSpace(evidenceDirectory))
        {
            throw new ArgumentException("Evidence path has no directory.");
        }

        return new StructuralShellPerformanceGateRequest(
            evidencePath,
            Path.Combine(evidenceDirectory, "diagnostic-logs"),
            args.Contains(
                "--gpu-presentation-precheck",
                StringComparer.OrdinalIgnoreCase),
            args.Contains(
                "--navigation-whiteframe-diagnosis",
                StringComparer.OrdinalIgnoreCase),
            args.Contains(
                "--navigation-whiteframe-precheck",
                StringComparer.OrdinalIgnoreCase),
            args.Contains(
                "--move-only-diagnosis",
                StringComparer.OrdinalIgnoreCase),
            args.Contains(
                "--move-only-validation",
                StringComparer.OrdinalIgnoreCase),
            args.Contains(
                "--whiteframe-classification",
                StringComparer.OrdinalIgnoreCase),
            args.Contains(
                "--whiteframe-precheck",
                StringComparer.OrdinalIgnoreCase));
    }
}

internal enum StructuralShellPhase
{
    None,
    Idle,
    Move,
    Resize,
    Maximize,
    Restore,
    Navigation,
}

internal sealed class StructuralShellPerformanceGate
{
    private const double StallThresholdMilliseconds = 50.0;
    private static readonly (double X, double Y)[] WhiteFrameProbePoints =
    {
        (0.18, 0.18),
        (0.50, 0.24),
        (0.50, 0.50),
        (0.20, 0.82),
        (0.80, 0.82),
        (0.01, 0.99),
        (0.99, 0.99),
    };
    private readonly StructuralShellPerformanceGateRequest _request;
    private readonly List<string> _failures = new();
    private readonly Dictionary<string, object?> _facts = new(
        StringComparer.Ordinal);
    private readonly List<WhiteFrameSample> _whiteFrameSamples = new();
    private readonly List<object> _cursorFacts = new();
    private readonly long _timelineStart = Stopwatch.GetTimestamp();
    private readonly DateTimeOffset _timelineStartUtc = DateTimeOffset.UtcNow;
    private DispatcherPhaseSnapshot? _moveResponsivenessSnapshot;
    private object? _moveGateFact;
    private int _cursorFlickerCount;
    private int _unexpectedCursorSwitchCount;
    private int _invalidPixelReadCount;
    private bool _captureFlaggedFrames;
    private int _classificationFlaggedSequence;
    private readonly List<FlaggedFrameArtifact> _classificationFrames = new();
    private readonly List<TargetVisibleSnapshot> _targetVisibleSnapshots = new();

    internal StructuralShellPerformanceGate(
        StructuralShellPerformanceGateRequest request)
    {
        _request = request;
    }

    internal static void WriteStartupFailure(
        StructuralShellPerformanceGateRequest request,
        Exception error)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(error);
        Environment.ExitCode = 1;
        WriteEvidence(
            request.EvidencePath,
            new
            {
                Status = "FAIL",
                Stage = "startup",
                Failures = new[] { error.ToString() },
                CompletedUtc = DateTimeOffset.UtcNow,
            });
    }

    internal async Task RunAsync(StructuralAvaloniaShellHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        try
        {
            await Task.Run(() => RunCoreAsync(host));
        }
        catch (Exception error)
        {
            _failures.Add(error.ToString());
        }
        finally
        {
            string status = _failures.Count == 0 ? "PASS" : "FAIL";
            if (status != "PASS")
            {
                Environment.ExitCode = 1;
            }
            WriteEvidence(
                _request.EvidencePath,
                new
                {
                    Status = status,
                    ThresholdMilliseconds = StallThresholdMilliseconds,
                    Failures = _failures,
                    Facts = _facts,
                    CompletedUtc = DateTimeOffset.UtcNow,
                });
        }
    }

    private async Task RunCoreAsync(StructuralAvaloniaShellHost host)
    {
        Directory.CreateDirectory(_request.DiagnosticDirectory);
        Directory.CreateDirectory(
            Path.GetDirectoryName(_request.EvidencePath)!);

        await WaitForGpuPresentationAsync(host);
        if (_request.PresentationOnly)
        {
            return;
        }
        if (_request.WhiteFrameClassificationOnly)
        {
            await RunWhiteFrameClassificationAsync(host);
            return;
        }
        if (_request.WhiteFramePrecheckOnly)
        {
            await RunWhiteFramePrecheckAsync(host);
            return;
        }
        if (_request.NavigationDiagnosisOnly ||
            _request.NavigationPrecheckOnly)
        {
            await RunNavigationWhiteFramePrecheckAsync(
                host,
                includeInteractionProbe: _request.NavigationDiagnosisOnly);
            return;
        }
        if (_request.MoveDiagnosisOnly || _request.MoveValidationOnly)
        {
            await RunMoveOnlyDiagnosticsAsync(
                host,
                requireResponsiveMove: _request.MoveValidationOnly);
            return;
        }
        DispatcherHeartbeat heartbeat = new();
        heartbeat.Start();
        System.Drawing.Point originalCursor = default;
        _ = NativeGateMethods.GetCursorPos(out originalCursor);
        System.Drawing.Rectangle originalBounds = await OnFormAsync(
            host,
            () => host.Bounds);

        try
        {
            System.Drawing.Rectangle testBounds = await PrepareNormalWindowAsync(
                host);
            await MeasureIdleAsync(heartbeat);
            await MeasureMoveAsync(host, heartbeat, testBounds);
            await MeasureResizeAsync(host, heartbeat, testBounds);
            await MeasureMaximizeRestoreAsync(host, heartbeat, testBounds);
            await MeasureNavigationAsync(host, heartbeat);
            await ValidateDpiLayoutsAsync(host);
            await ValidateCrossDpiAsync(host, testBounds);
            await ValidateWindowsScreenshotAsync(host);

            NativeMethods.PreviewStats stats = await OnFormAsync(
                host,
                () => host.NativeSession.GetStats());
            GpuPreviewPresentationDiagnostics presentation =
                await OnFormAsync(
                    host,
                    () => host.ShellView.PreviewControl
                        .PresentationDiagnostics);
            NativeMethods.GpuExportFrameV1 export = default;
            bool hasExport = await OnFormAsync(
                host,
                () => host.NativeSession.TryGetGpuExportFrame(out export));

            bool gpuPath =
                hasExport &&
                export.SharedHandle != 0 &&
                presentation.PresentationActive &&
                presentation.LastCompletedGeneration > 0 &&
                host.ShellView.PreviewControl.InteropStatus.StartsWith(
                    "PASS",
                    StringComparison.Ordinal);
            Require(gpuPath, "Real shared-handle GPU preview was not active.");
            Require(
                string.Equals(
                    host.CompositionMode,
                    "LowLatencyDxgiSwapChain",
                    StringComparison.Ordinal),
                "LowLatencyDxgiSwapChain was not the configured mode.");

            _facts["Gpu"] = new
            {
                PathActive = gpuPath,
                host.CompositionMode,
                CpuFrameCopyCount = 0,
                stats.CaptureFps,
                stats.PresentFps,
                P50LatencyMilliseconds = stats.P50LatencyMilliseconds,
                P95LatencyMilliseconds = stats.P95LatencyMilliseconds,
                stats.MaxLatencyMilliseconds,
                stats.CaptureFrameCount,
                stats.PresentFrameCount,
                stats.DroppedFrameCount,
                SharedHandle = hasExport ? export.SharedHandle : 0,
                ExportSlot = hasExport ? export.SlotIndex : 0,
                ExportGeneration = hasExport ? export.FrameGeneration : 0,
                ExportSkippedFrames = hasExport
                    ? export.SkippedFrameCount
                    : 0,
                presentation.LastCompletedGeneration,
                host.ShellView.PreviewControl.InteropStatus,
                host.ShellView.PreviewControl.DeviceCompatibility,
                host.ShellView.PreviewControl.AdapterLuidMatch,
            };
        }
        finally
        {
            heartbeat.SetPhase(StructuralShellPhase.None);
            await heartbeat.StopAsync();
            _ = NativeGateMethods.SetCursorPos(
                originalCursor.X,
                originalCursor.Y);
            if (!host.IsDisposed)
            {
                await OnFormAsync(
                    host,
                    () =>
                    {
                        host.WindowState = FormWindowState.Normal;
                        host.Bounds = originalBounds;
                        return true;
                    });
            }
        }

        Dictionary<string, object> dispatcher = new(
            StringComparer.Ordinal);
        foreach (StructuralShellPhase phase in Enum.GetValues<
                     StructuralShellPhase>())
        {
            if (phase == StructuralShellPhase.None)
            {
                continue;
            }
            DispatcherPhaseSnapshot snapshot =
                phase == StructuralShellPhase.Move &&
                _moveResponsivenessSnapshot.HasValue
                    ? _moveResponsivenessSnapshot.Value
                    : heartbeat.Snapshot(phase);
            Require(
                snapshot.SampleCount > 0,
                $"Dispatcher phase {phase} produced no samples.");
            Require(
                snapshot.StallCount == 0,
                $"Dispatcher phase {phase} exceeded 50 ms: " +
                $"count={snapshot.StallCount}, max={snapshot.MaxMilliseconds:F3}.");
            dispatcher[phase.ToString()] = snapshot;
        }
        _facts["Dispatcher"] = dispatcher;
        _facts["MoveGate"] = _moveGateFact;
        _facts["Cursor"] = new
        {
            ResizeCursorFlicker = _cursorFlickerCount,
            UnexpectedCursorSwitch = _unexpectedCursorSwitchCount,
            Samples = _cursorFacts,
        };
        _facts["WhiteFrame"] = new
        {
            Detected = _whiteFrameSamples.Count > 0,
            InvalidReadCount = _invalidPixelReadCount,
            Samples = _whiteFrameSamples,
            Timeline = WhiteFrameTimelineFact(),
        };
        Require(
            _cursorFlickerCount == 0,
            $"Resize cursor flicker count was {_cursorFlickerCount}.");
        Require(
            _unexpectedCursorSwitchCount == 0,
            "Unexpected resize cursor switches were observed.");
        Require(
            _whiteFrameSamples.Count == 0,
            "A full-white client sample was observed.");
        Require(
            _invalidPixelReadCount == 0,
            "An invalid white-frame pixel read was observed.");
    }

    private async Task WaitForGpuPresentationAsync(
        StructuralAvaloniaShellHost host)
    {
        long deadline = Stopwatch.GetTimestamp() +
            (long)(Stopwatch.Frequency * 20.0);
        GpuPresentationSnapshot? first = null;
        GpuPresentationSnapshot? last = null;
        while (Stopwatch.GetTimestamp() < deadline)
        {
            GpuPresentationSnapshot snapshot = await OnFormAsync(
                host,
                () => CaptureGpuPresentationSnapshot(host));
            first ??= snapshot;
            last = snapshot;
            if (snapshot.InteropStatus.StartsWith(
                    "PASS",
                    StringComparison.Ordinal) &&
                snapshot.NativeProducerActive &&
                snapshot.ControlAttached &&
                snapshot.ControlVisible &&
                snapshot.BoundsWidth > 0 && snapshot.BoundsHeight > 0 &&
                snapshot.FrameSourceAttached &&
                snapshot.HasExport && snapshot.SharedHandle != 0 &&
                snapshot.Presentation.PresentationActive &&
                snapshot.Presentation.LastExportGeneration > 0 &&
                snapshot.Presentation.LastCompletedGeneration > 0)
            {
                _facts["GpuPresentationPrecheck"] =
                    GpuPresentationFact(first ?? snapshot, snapshot);
                return;
            }
            if (snapshot.InteropStatus.StartsWith(
                "FAIL",
                StringComparison.Ordinal))
            {
                _facts["GpuPresentationPrecheck"] =
                    GpuPresentationFact(first ?? snapshot, snapshot);
                throw new InvalidOperationException(
                    "GPU preview initialization failed: " +
                    $"{snapshot.InteropStatus}; {snapshot.StartupError}");
            }
            await Task.Delay(50);
        }
        if (first is { } initial && last is { } final)
        {
            _facts["GpuPresentationPrecheck"] =
                GpuPresentationFact(initial, final);
        }
        throw new TimeoutException(
            "Real Avalonia GPU preview did not present within 20 seconds.");
    }

    private static GpuPresentationSnapshot CaptureGpuPresentationSnapshot(
        StructuralAvaloniaShellHost host)
    {
        GpuPreviewControl preview = host.ShellView.PreviewControl;
        GpuPreviewPresentationDiagnostics presentation =
            preview.PresentationDiagnostics;
        NativeMethods.GpuExportFrameV1 export = default;
        bool hasExport = host.NativeSession.TryGetGpuExportFrame(out export);
        return new GpuPresentationSnapshot(
            preview.InteropStatus,
            preview.StartupError,
            presentation.HasPresentationSource,
            preview.IsVisible,
            preview.Bounds.Width,
            preview.Bounds.Height,
            ReferenceEquals(preview.FrameSource, host.FrameSource),
            host.NativeSession.GpuStreamActive,
            host.NativeSession.GpuStreamGeneration,
            hasExport,
            hasExport ? export.SharedHandle : 0,
            hasExport ? export.Width : 0,
            hasExport ? export.Height : 0,
            hasExport ? export.FrameGeneration : 0,
            hasExport ? export.AdapterLuidLow : 0,
            hasExport ? export.AdapterLuidHigh : 0,
            presentation,
            preview.DeviceCompatibility,
            preview.AdapterLuidMatch);
    }

    private static object GpuPresentationFact(
        GpuPresentationSnapshot first,
        GpuPresentationSnapshot last) => new
    {
        ControlAttached = last.ControlAttached,
        ControlVisible = last.ControlVisible,
        ControlBounds = new
        {
            Width = last.BoundsWidth,
            Height = last.BoundsHeight,
        },
        last.FrameSourceAttached,
        last.NativeProducerActive,
        InitialStreamId = first.StreamId,
        FinalStreamId = last.StreamId,
        InitialFrameId = first.FrameId,
        FinalFrameId = last.FrameId,
        StreamIdActive = last.StreamId > 0,
        FrameIdAdvancing = last.FrameId > first.FrameId,
        SharedHandleValid = last.HasExport && last.SharedHandle != 0,
        last.SharedHandle,
        last.Width,
        last.Height,
        AdapterLuid = $"{last.AdapterLuidHigh:X8}:" +
            $"{last.AdapterLuidLow:X8}",
        SurfaceCreated = last.Presentation.PresentationActive,
        CompositionUpdateRequested =
            last.Presentation.CompositionRequests > 0,
        FirstFrameImported = last.Presentation.LastExportGeneration > 0,
        FirstFramePresented = last.Presentation.LastCompletedGeneration > 0,
        last.InteropStatus,
        last.StartupError,
        last.DeviceCompatibility,
        last.AdapterLuidMatch,
        Presentation = last.Presentation,
        CpuFrameCopyCount = 0,
    };

    private sealed record GpuPresentationSnapshot(
        string InteropStatus,
        string? StartupError,
        bool ControlAttached,
        bool ControlVisible,
        double BoundsWidth,
        double BoundsHeight,
        bool FrameSourceAttached,
        bool NativeProducerActive,
        ulong StreamId,
        bool HasExport,
        ulong SharedHandle,
        uint Width,
        uint Height,
        ulong FrameId,
        uint AdapterLuidLow,
        int AdapterLuidHigh,
        GpuPreviewPresentationDiagnostics Presentation,
        string DeviceCompatibility,
        bool? AdapterLuidMatch);

    private static async Task<System.Drawing.Rectangle>
        PrepareNormalWindowAsync(StructuralAvaloniaShellHost host)
    {
        return await OnFormAsync(
            host,
            () =>
            {
                host.WindowState = FormWindowState.Normal;
                System.Drawing.Rectangle work =
                    System.Windows.Forms.Screen.FromHandle(host.Handle)
                        .WorkingArea;
                int minimumWidth = Math.Max(760, host.MinimumSize.Width);
                int minimumHeight = Math.Max(520, host.MinimumSize.Height);
                int width = Math.Min(
                    Math.Max(minimumWidth + 120, 980),
                    Math.Max(minimumWidth, work.Width - 120));
                int height = Math.Min(
                    Math.Max(minimumHeight + 100, 680),
                    Math.Max(minimumHeight, work.Height - 120));
                System.Drawing.Rectangle bounds = new(
                    work.Left + Math.Max(0, (work.Width - width) / 2),
                    work.Top + Math.Max(0, (work.Height - height) / 2),
                    width,
                    height);
                host.Bounds = bounds;
                host.Activate();
                _ = NativeGateMethods.SetForegroundWindow(host.Handle);
                return host.Bounds;
            });
    }

    private async Task MeasureIdleAsync(DispatcherHeartbeat heartbeat)
    {
        heartbeat.SetPhase(StructuralShellPhase.Idle);
        await Task.Delay(1400);
        heartbeat.SetPhase(StructuralShellPhase.None);
    }

    private async Task MeasureMoveAsync(
        StructuralAvaloniaShellHost host,
        DispatcherHeartbeat heartbeat,
        System.Drawing.Rectangle testBounds)
    {
        await ResetBoundsAsync(host, testBounds);
        WindowMoveMessageProbe messageProbe = new();
        await OnFormAsync(
            host,
            () =>
            {
                messageProbe.Attach(host.Handle);
                return true;
            });
        RawIntervalHeartbeat raw = new();
        WindowMessageHeartbeat uiSentinel = new(host.Handle);
        GpuPresentationSnapshot beforeGpu = await OnFormAsync(
            host,
            () => CaptureGpuPresentationSnapshot(host));
        try
        {
            raw.Start();
            uiSentinel.Start();
            heartbeat.SetPhase(StructuralShellPhase.Move);
            try
            {
                await DragWindowAsync(
                    host,
                    NativeGateMethods.HtCaption,
                    new System.Drawing.Point(90, 45),
                    validateResizeCursor: false,
                    label: "move");
                await SampleForAsync(host, "move", 350);
            }
            finally
            {
                heartbeat.SetPhase(StructuralShellPhase.None);
                await raw.StopAsync();
                await uiSentinel.StopAsync();
            }

            GpuPresentationSnapshot afterGpu = await OnFormAsync(
                host,
                () => CaptureGpuPresentationSnapshot(host));
            ProbeLatencySnapshot rawSnapshot = raw.Snapshot();
            ProbeLatencySnapshot sentinelSnapshot = uiSentinel.Snapshot();
            WindowMoveMessageSnapshot messageSnapshot =
                messageProbe.Snapshot();
            bool gpuFramesContinue =
                afterGpu.FrameId > beforeGpu.FrameId &&
                afterGpu.Presentation.LastExportGeneration >=
                    beforeGpu.Presentation.LastExportGeneration;
            _moveResponsivenessSnapshot = new DispatcherPhaseSnapshot(
                sentinelSnapshot.SampleCount,
                sentinelSnapshot.StallCount,
                sentinelSnapshot.MaxMilliseconds);
            _moveGateFact = new
            {
                ProbeType =
                    "modal-loop-compatible bounded HWND message round-trip",
                ThresholdMilliseconds = StallThresholdMilliseconds,
                RawSampleInterval = rawSnapshot,
                AvaloniaDispatcherScheduling =
                    heartbeat.Snapshot(StructuralShellPhase.Move),
                UiThreadSentinel = sentinelSnapshot,
                WindowMessages = messageSnapshot,
                GpuFramesContinue = gpuFramesContinue,
                GpuBefore = beforeGpu,
                GpuAfter = afterGpu,
            };
            Require(
                messageSnapshot.EnterSizeMoveCount > 0 &&
                messageSnapshot.ExitSizeMoveCount > 0,
                "Move Gate did not enter and exit the Windows interactive " +
                "move modal loop.");
            Require(
                messageSnapshot.HandlerStallCount == 0,
                "A move window-message handler exceeded 50 ms: " +
                $"max={messageSnapshot.HandlerMaxMilliseconds:F3}.");
            Require(
                gpuFramesContinue,
                "GPU frames did not continue through the Move Gate.");
        }
        finally
        {
            await OnFormAsync(
                host,
                () =>
                {
                    messageProbe.Detach();
                    return true;
                });
        }
    }

    private async Task RunMoveOnlyDiagnosticsAsync(
        StructuralAvaloniaShellHost host,
        bool requireResponsiveMove)
    {
        const int runCount = 3;
        TimeSpan duration = TimeSpan.FromSeconds(12);
        List<object> runs = new();
        bool allResponsive = true;
        System.Drawing.Point originalCursor = default;
        _ = NativeGateMethods.GetCursorPos(out originalCursor);
        System.Drawing.Rectangle originalBounds = await OnFormAsync(
            host,
            () => host.Bounds);

        try
        {
            for (int run = 1; run <= runCount; ++run)
            {
                System.Drawing.Rectangle testBounds =
                    await PrepareNormalWindowAsync(host);
                WindowMoveMessageProbe messageProbe = new();
                await OnFormAsync(
                    host,
                    () =>
                    {
                        messageProbe.Attach(host.Handle);
                        return true;
                    });

                DispatcherHeartbeat dispatcher = new();
                RawIntervalHeartbeat raw = new();
                WindowMessageHeartbeat uiSentinel = new(host.Handle);
                GpuPresentationSnapshot beforeGpu = await OnFormAsync(
                    host,
                    () => CaptureGpuPresentationSnapshot(host));
                System.Drawing.Rectangle beforeBounds = await OnFormAsync(
                    host,
                    () => host.Bounds);

                dispatcher.Start();
                raw.Start();
                uiSentinel.Start();
                dispatcher.SetPhase(StructuralShellPhase.Move);
                try
                {
                    await MoveWindowContinuouslyAsync(
                        host,
                        duration,
                        messageProbe);
                }
                finally
                {
                    dispatcher.SetPhase(StructuralShellPhase.None);
                    await dispatcher.StopAsync();
                    await raw.StopAsync();
                    await uiSentinel.StopAsync();
                }

                GpuPresentationSnapshot afterGpu = await OnFormAsync(
                    host,
                    () => CaptureGpuPresentationSnapshot(host));
                System.Drawing.Rectangle afterBounds = await OnFormAsync(
                    host,
                    () => host.Bounds);
                TimedLatencySample[] dispatcherSamples =
                    dispatcher.Samples(StructuralShellPhase.Move);
                DispatcherPhaseSnapshot dispatcherSnapshot =
                    dispatcher.Snapshot(StructuralShellPhase.Move);
                ProbeLatencySnapshot rawSnapshot = raw.Snapshot();
                ProbeLatencySnapshot sentinelSnapshot =
                    uiSentinel.Snapshot();
                WindowMoveMessageSnapshot messageSnapshot =
                    messageProbe.Snapshot();
                object[] spikes = dispatcherSamples
                    .Where(sample =>
                        sample.LatencyMilliseconds >
                            StallThresholdMilliseconds)
                    .Select((sample, index) => (object)new
                    {
                        Spike = index + 1,
                        Timestamp = sample.CallbackTimestampUtc,
                        MeasuredInterval = sample.LatencyMilliseconds,
                        WindowPositionBefore = beforeBounds,
                        WindowPositionAfter = afterBounds,
                        MoveState = new
                        {
                            AtQueue = messageProbe.WasInMoveAt(
                                sample.QueuedTicks),
                            AtCallback = messageProbe.WasInMoveAt(
                                sample.CallbackTicks),
                        },
                        UiThreadTrace = new
                        {
                            BoundedWindowMessageSentinelMax =
                                sentinelSnapshot.MaxMilliseconds,
                            messageSnapshot.HandlerMaxMilliseconds,
                        },
                        DispatcherCallback = sample,
                        WindowMessages = messageProbe.Around(
                            sample.QueuedTicks,
                            sample.CallbackTicks),
                        GpuFrameState = new
                        {
                            Before = beforeGpu,
                            After = afterGpu,
                        },
                        CompositionState = new
                        {
                            Before = beforeGpu.Presentation,
                            After = afterGpu.Presentation,
                        },
                    })
                    .ToArray();
                bool gpuFramesContinue =
                    afterGpu.FrameId > beforeGpu.FrameId &&
                    afterGpu.Presentation.LastExportGeneration >=
                        beforeGpu.Presentation.LastExportGeneration;
                bool runResponsive =
                    sentinelSnapshot.SampleCount > 0 &&
                    sentinelSnapshot.StallCount == 0 &&
                    messageSnapshot.HandlerStallCount == 0 &&
                    messageSnapshot.EnterSizeMoveCount > 0 &&
                    messageSnapshot.ExitSizeMoveCount > 0 &&
                    gpuFramesContinue;
                allResponsive &= runResponsive;
                runs.Add(new
                {
                    Run = run,
                    DurationMilliseconds = duration.TotalMilliseconds,
                    WindowPositionBefore = beforeBounds,
                    WindowPositionAfter = afterBounds,
                    RawSampleInterval = rawSnapshot,
                    AvaloniaDispatcherSentinel = dispatcherSnapshot,
                    DispatcherSpikes = spikes,
                    UiThreadWindowMessageSentinel = sentinelSnapshot,
                    WindowMessages = messageSnapshot,
                    Gpu = new
                    {
                        FramesContinue = gpuFramesContinue,
                        Before = beforeGpu,
                        After = afterGpu,
                    },
                    ProductMoveStall = !runResponsive,
                });

                if (requireResponsiveMove)
                {
                    Require(
                        runResponsive,
                        $"Move validation run {run} failed: " +
                        $"uiSentinelMax={sentinelSnapshot.MaxMilliseconds:F3}, " +
                        $"handlerMax={messageSnapshot.HandlerMaxMilliseconds:F3}, " +
                        $"gpuFramesContinue={gpuFramesContinue}.");
                }

                await OnFormAsync(
                    host,
                    () =>
                    {
                        messageProbe.Detach();
                        host.Bounds = testBounds;
                        return true;
                    });
                await Task.Delay(250);
            }
        }
        finally
        {
            _ = NativeGateMethods.SetCursorPos(
                originalCursor.X,
                originalCursor.Y);
            if (!host.IsDisposed)
            {
                await OnFormAsync(
                    host,
                    () =>
                    {
                        host.WindowState = FormWindowState.Normal;
                        host.Bounds = originalBounds;
                        return true;
                    });
            }
        }

        _facts["MoveOnly"] = new
        {
            RunCount = runCount,
            DurationPerRunMilliseconds = duration.TotalMilliseconds,
            ProbeType =
                "Avalonia Dispatcher.Post plus modal-loop-compatible " +
                "bounded HWND message round-trip",
            Classification = allResponsive
                ? "MOVE-GATE-SCHEDULING-ARTIFACT"
                : "PRODUCT-STALL-NOT-EXCLUDED",
            ProductMoveStall = !allResponsive,
            Runs = runs,
        };
    }

    private async Task MoveWindowContinuouslyAsync(
        StructuralAvaloniaShellHost host,
        TimeSpan duration,
        WindowMoveMessageProbe messageProbe)
    {
        System.Drawing.Rectangle bounds = await OnFormAsync(
            host,
            () => host.Bounds);
        System.Drawing.Point start = HitPoint(
            bounds,
            NativeGateMethods.HtCaption);
        await OnFormAsync(
            host,
            () =>
            {
                host.Activate();
                return NativeGateMethods.SetForegroundWindow(host.Handle);
            });
        _ = NativeGateMethods.SetCursorPos(start.X, start.Y);
        await Task.Delay(250);
        int actualHit = unchecked((int)(long)NativeGateMethods.SendMessageW(
            host.Handle,
            NativeGateMethods.WmNcHitTest,
            nint.Zero,
            MakeLParam(start.X, start.Y)));
        Require(
            actualHit == NativeGateMethods.HtCaption,
            $"move-only hit-test expected caption, got {actualHit}.");

        bool mouseDown = false;
        for (int attempt = 1; attempt <= 3 && !messageProbe.InMove; ++attempt)
        {
            await OnFormAsync(
                host,
                () =>
                {
                    host.Activate();
                    return NativeGateMethods.SetForegroundWindow(host.Handle);
                });
            _ = NativeGateMethods.SetCursorPos(start.X, start.Y);
            await Task.Delay(180);
            NativeGateMethods.MouseLeftDown();
            mouseDown = true;
            _ = NativeGateMethods.SetCursorPos(start.X + 12, start.Y + 2);
            long enterDeadline = Stopwatch.GetTimestamp() +
                Stopwatch.Frequency / 2;
            while (!messageProbe.InMove &&
                   Stopwatch.GetTimestamp() < enterDeadline)
            {
                await Task.Delay(10);
            }
            if (!messageProbe.InMove)
            {
                NativeGateMethods.MouseLeftUp();
                mouseDown = false;
                _ = NativeGateMethods.SetCursorPos(start.X, start.Y);
                await Task.Delay(180);
            }
        }
        Require(
            messageProbe.InMove,
            "Move-only run did not enter WM_ENTERSIZEMOVE.");

        Stopwatch elapsed = Stopwatch.StartNew();
        int step = 0;
        try
        {
            while (elapsed.Elapsed < duration)
            {
                int cycle = step % 120;
                double triangle = cycle < 60
                    ? -1.0 + cycle / 30.0
                    : 3.0 - cycle / 30.0;
                int x = start.X + (int)Math.Round(110.0 * triangle);
                int y = start.Y + (int)Math.Round(
                    24.0 * Math.Sin(step * Math.PI / 30.0));
                _ = NativeGateMethods.SetCursorPos(x, y);
                ++step;
                await Task.Delay(18);
            }
            _ = NativeGateMethods.SetCursorPos(start.X, start.Y);
            await Task.Delay(18);
        }
        finally
        {
            if (mouseDown)
            {
                NativeGateMethods.MouseLeftUp();
                await Task.Delay(100);
            }
        }
    }

    private async Task MeasureResizeAsync(
        StructuralAvaloniaShellHost host,
        DispatcherHeartbeat heartbeat,
        System.Drawing.Rectangle testBounds)
    {
        (int Hit, System.Drawing.Point Delta, string Label)[] cases =
        {
            (NativeGateMethods.HtLeft, new(46, 0), "left"),
            (NativeGateMethods.HtRight, new(-46, 0), "right"),
            (NativeGateMethods.HtTop, new(0, 38), "top"),
            (NativeGateMethods.HtBottom, new(0, -38), "bottom"),
            (NativeGateMethods.HtTopLeft, new(38, 30), "top-left"),
            (NativeGateMethods.HtTopRight, new(-38, 30), "top-right"),
            (NativeGateMethods.HtBottomLeft, new(38, -30), "bottom-left"),
            (NativeGateMethods.HtBottomRight, new(-38, -30),
                "bottom-right"),
        };

        heartbeat.SetPhase(StructuralShellPhase.Resize);
        foreach ((int hit, System.Drawing.Point delta, string label) in cases)
        {
            await ResetBoundsAsync(host, testBounds);
            await DragWindowAsync(
                host,
                hit,
                delta,
                validateResizeCursor: true,
                label);
            await SampleForAsync(host, $"resize-{label}", 130);
        }

        await ResetBoundsAsync(host, testBounds);
        await DragWindowAsync(
            host,
            NativeGateMethods.HtBottomRight,
            new System.Drawing.Point(-70, -52),
            validateResizeCursor: true,
            label: "continuous-shrink-enlarge",
            returnToStart: true,
            steps: 28);
        await SampleForAsync(host, "resize-continuous", 250);
        heartbeat.SetPhase(StructuralShellPhase.None);
    }

    private async Task MeasureMaximizeRestoreAsync(
        StructuralAvaloniaShellHost host,
        DispatcherHeartbeat heartbeat,
        System.Drawing.Rectangle testBounds)
    {
        await ResetBoundsAsync(host, testBounds);
        heartbeat.SetPhase(StructuralShellPhase.Maximize);
        await OnFormAsync(
            host,
            () =>
            {
                host.WindowState = FormWindowState.Maximized;
                return true;
            });
        await SampleForAsync(host, "maximize", 750);
        heartbeat.SetPhase(StructuralShellPhase.None);

        heartbeat.SetPhase(StructuralShellPhase.Restore);
        await OnFormAsync(
            host,
            () =>
            {
                host.WindowState = FormWindowState.Normal;
                return true;
            });
        await SampleForAsync(host, "restore", 750);
        heartbeat.SetPhase(StructuralShellPhase.None);
    }

    private async Task MeasureNavigationAsync(
        StructuralAvaloniaShellHost host,
        DispatcherHeartbeat heartbeat)
    {
        heartbeat.SetPhase(StructuralShellPhase.Navigation);
        await OnFormAsync(
            host,
            () =>
            {
                host.ShellView.ShowSettings();
                return host.ShellView.SettingsVisible;
            });
        await SampleForAsync(host, "navigation-settings", 450);
        bool settingsVisible = await OnFormAsync(
            host,
            () => host.ShellView.SettingsVisible);
        Require(settingsVisible, "Settings surface did not become visible.");

        await OnFormAsync(
            host,
            () =>
            {
                host.ShellView.ShowHome();
                return !host.ShellView.SettingsVisible;
            });
        await SampleForAsync(host, "navigation-home", 450);
        bool homeVisible = await OnFormAsync(
            host,
            () => !host.ShellView.SettingsVisible);
        Require(homeVisible, "Home surface did not return.");
        heartbeat.SetPhase(StructuralShellPhase.None);
        _facts["Navigation"] = new
        {
            SameAvaloniaRoot = true,
            settingsVisible,
            homeVisible,
            WinFormsSettingsSurface = false,
        };
    }

    private async Task RunNavigationWhiteFramePrecheckAsync(
        StructuralAvaloniaShellHost host,
        bool includeInteractionProbe)
    {
        DispatcherHeartbeat heartbeat = new();
        heartbeat.Start();
        List<object> transitions = new();
        System.Drawing.Rectangle originalBounds = await OnFormAsync(
            host,
            () => host.Bounds);
        try
        {
            heartbeat.SetPhase(StructuralShellPhase.Navigation);
            for (int iteration = 1; iteration <= 5; ++iteration)
            {
                transitions.Add(await MeasureNavigationTransitionAsync(
                    host,
                    iteration,
                    toSettings: true));
                await SampleForAsync(
                    host,
                    $"navigation-{iteration}-settings",
                    100);
                transitions.Add(await MeasureNavigationTransitionAsync(
                    host,
                    iteration,
                    toSettings: false));
                await SampleForAsync(
                    host,
                    $"navigation-{iteration}-home",
                    100);
            }
            heartbeat.SetPhase(StructuralShellPhase.None);

            if (includeInteractionProbe)
            {
                System.Drawing.Rectangle testBounds =
                    await PrepareNormalWindowAsync(host);
                await DragWindowAsync(
                    host,
                    NativeGateMethods.HtCaption,
                    new System.Drawing.Point(48, 32),
                    validateResizeCursor: false,
                    label: "diagnosis-move",
                    steps: 8);
                await ResetBoundsAsync(host, testBounds);
                await DragWindowAsync(
                    host,
                    NativeGateMethods.HtRight,
                    new System.Drawing.Point(-32, 0),
                    validateResizeCursor: true,
                    label: "diagnosis-resize-right",
                    steps: 8);
            }
        }
        finally
        {
            heartbeat.SetPhase(StructuralShellPhase.None);
            await heartbeat.StopAsync();
            if (!host.IsDisposed)
            {
                await OnFormAsync(
                    host,
                    () =>
                    {
                        host.ShellView.ShowHome();
                        host.WindowState = FormWindowState.Normal;
                        host.Bounds = originalBounds;
                        return true;
                    });
            }
        }

        DispatcherPhaseSnapshot navigation =
            heartbeat.Snapshot(StructuralShellPhase.Navigation);
        _facts["NavigationPrecheck"] = new
        {
            Roundtrips = 5,
            navigation.SampleCount,
            navigation.StallCount,
            navigation.MaxMilliseconds,
            Transitions = transitions,
        };
        _facts["WhiteFramePrecheck"] = WhiteFrameTimelineFact();
        Require(
            navigation.StallCount == 0,
            "Navigation precheck exceeded 50 ms: " +
            $"count={navigation.StallCount}, " +
            $"max={navigation.MaxMilliseconds:F3}.");
        Require(
            _whiteFrameSamples.Count == 0,
            "Navigation/white-frame precheck observed full-white samples.");
    }

    private async Task RunWhiteFrameClassificationAsync(
        StructuralAvaloniaShellHost host)
    {
        System.Drawing.Rectangle originalBounds = await OnFormAsync(
            host,
            () => host.Bounds);
        System.Drawing.Point originalCursor = default;
        _ = NativeGateMethods.GetCursorPos(out originalCursor);
        List<object> pointMaps = new();
        DispatcherHeartbeat heartbeat = new();
        heartbeat.Start();
        Form? controlledOcclusion = null;
        int naturalFlaggedCount = 0;
        string[] operationWarnings = Array.Empty<string>();
        _captureFlaggedFrames = true;
        try
        {
            System.Drawing.Rectangle testBounds = await PrepareNormalWindowAsync(
                host);
            pointMaps.Add(await CaptureWhiteFramePointMapAsync(host, "HOME"));

            // Reproduce the exact operation order that produced the 255 legacy
            // desktop-DC hits, but do not run the unrelated DPI/screenshot or
            // final Structural Gate assertions in this diagnostic route.
            await MeasureMoveAsync(host, heartbeat, testBounds);
            await MeasureResizeAsync(host, heartbeat, testBounds);
            await MeasureMaximizeRestoreAsync(host, heartbeat, testBounds);
            naturalFlaggedCount = _whiteFrameSamples.Count;
            operationWarnings = _failures.ToArray();
            _failures.Clear();

            for (int attempt = 1;
                 attempt <= 5 && naturalFlaggedCount == 0;
                 ++attempt)
            {
                await ResetBoundsAsync(host, testBounds);
                await DragWindowAsync(
                    host,
                    NativeGateMethods.HtBottomRight,
                    new System.Drawing.Point(-70, -52),
                    validateResizeCursor: false,
                    label: $"natural-repro-continuous-{attempt}",
                    returnToStart: true,
                    steps: 28);
                await SampleForAsync(
                    host,
                    $"natural-repro-continuous-{attempt}-settled",
                    250);
                naturalFlaggedCount = _whiteFrameSamples.Count;
            }

            if (naturalFlaggedCount == 0)
            {
                controlledOcclusion = await ShowControlledOcclusionAsync(host);
                await SampleForAsync(
                    host,
                    "controlled-occlusion-home-first",
                    350);
                _targetVisibleSnapshots.Add(
                    await CaptureTargetVisibleSnapshotAsync(
                        host,
                        controlledOcclusion,
                        "home-first"));
            }

            await OnFormAsync(
                host,
                () =>
                {
                    host.ShellView.ShowSettings();
                    return host.ShellView.SettingsVisible;
                });
            await Task.Delay(120);
            pointMaps.Add(await CaptureWhiteFramePointMapAsync(
                host,
                "SETTINGS"));
            await SampleForAsync(
                host,
                controlledOcclusion is null
                    ? "classification-settings"
                    : "controlled-occlusion-settings",
                500);
            if (controlledOcclusion is not null)
            {
                _targetVisibleSnapshots.Add(
                    await CaptureTargetVisibleSnapshotAsync(
                        host,
                        controlledOcclusion,
                        "settings-middle"));
            }

            await OnFormAsync(
                host,
                () =>
                {
                    host.ShellView.ShowHome();
                    return !host.ShellView.SettingsVisible;
                });
            await Task.Delay(120);
            pointMaps.Add(await CaptureWhiteFramePointMapAsync(
                host,
                "HOME-AFTER-SETTINGS"));
            await SampleForAsync(
                host,
                controlledOcclusion is null
                    ? "classification-home-final"
                    : "controlled-occlusion-home-final",
                500);
            if (controlledOcclusion is not null)
            {
                _targetVisibleSnapshots.Add(
                    await CaptureTargetVisibleSnapshotAsync(
                        host,
                        controlledOcclusion,
                        "home-last"));
            }
        }
        finally
        {
            _captureFlaggedFrames = false;
            if (controlledOcclusion is not null &&
                !controlledOcclusion.IsDisposed)
            {
                await OnFormAsync(
                    host,
                    () =>
                    {
                        controlledOcclusion.Close();
                        controlledOcclusion.Dispose();
                        return true;
                    });
            }
            heartbeat.SetPhase(StructuralShellPhase.None);
            await heartbeat.StopAsync();
            _ = NativeGateMethods.SetCursorPos(
                originalCursor.X,
                originalCursor.Y);
            if (!host.IsDisposed)
            {
                await OnFormAsync(
                    host,
                    () =>
                    {
                        host.ShellView.ShowHome();
                        host.WindowState = FormWindowState.Normal;
                        host.Bounds = originalBounds;
                        return true;
                    });
            }
        }

        FlaggedFrameArtifact? first = _classificationFrames.FirstOrDefault();
        FlaggedFrameArtifact? middle = _classificationFrames.Count == 0
            ? null
            : _classificationFrames[(_classificationFrames.Count - 1) / 2];
        FlaggedFrameArtifact? last = _classificationFrames.LastOrDefault();
        _facts["WhiteFrameClassification"] = new
        {
            NaturalReplayFlaggedCount = naturalFlaggedCount,
            ControlledOcclusionUsed = controlledOcclusion is not null,
            ControlledOcclusionFlaggedCount =
                _whiteFrameSamples.Count - naturalFlaggedCount,
            OperationWarnings = operationWarnings,
            LegacyDesktopDetectorFlaggedCount = _whiteFrameSamples.Count,
            InvalidReadCount = _invalidPixelReadCount,
            PointMaps = pointMaps,
            FirstFlaggedFrame = first,
            MiddleFlaggedFrame = middle,
            LastFlaggedFrame = last,
            CapturedFrames = _classificationFrames,
            TargetVisibleSnapshots = _targetVisibleSnapshots,
            HostBackground = ColorText(host.BackColor),
            RootBackground = "#FAF8F5 (SkillRecorder.Brush.Deck)",
            DesktopDcSemantics =
                "GetDC(0) observes the visible desktop, including occlusion.",
        };
        _facts["WhiteFrame"] = new
        {
            Detected = _whiteFrameSamples.Count > 0,
            InvalidReadCount = _invalidPixelReadCount,
            Samples = _whiteFrameSamples,
            Timeline = WhiteFrameTimelineFact(),
        };
    }

    private async Task<TargetVisibleSnapshot> CaptureTargetVisibleSnapshotAsync(
        StructuralAvaloniaShellHost host,
        Form overlay,
        string label)
    {
        await OnFormAsync(
            host,
            () =>
            {
                overlay.Hide();
                host.Activate();
                return NativeGateMethods.SetForegroundWindow(host.Handle);
            });
        await Task.Delay(120);
        try
        {
            if (!NativeGateMethods.GetClientRect(
                    host.Handle,
                    out NativeRect client))
            {
                throw new InvalidOperationException(
                    "Target-visible snapshot could not read client bounds.");
            }
            NativePoint origin = new(0, 0);
            if (!NativeGateMethods.ClientToScreen(host.Handle, ref origin))
            {
                throw new InvalidOperationException(
                    "Target-visible snapshot could not map client origin.");
            }
            int width = client.Right - client.Left;
            int height = client.Bottom - client.Top;
            string directory = Path.Combine(
                Path.GetDirectoryName(_request.EvidencePath)!,
                "flagged-frames");
            Directory.CreateDirectory(directory);
            string path = Path.Combine(
                directory,
                $"target-visible-{label}.png");
            string[] colors;
            using (System.Drawing.Bitmap bitmap = new(width, height))
            {
                using System.Drawing.Graphics graphics =
                    System.Drawing.Graphics.FromImage(bitmap);
                graphics.CopyFromScreen(
                    origin.X,
                    origin.Y,
                    0,
                    0,
                    new System.Drawing.Size(width, height),
                    System.Drawing.CopyPixelOperation.SourceCopy);
                colors = ReadBitmapColors(bitmap, width, height);
                bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            }
            GpuPreviewPresentationDiagnostics presentation = await OnFormAsync(
                host,
                () => host.ShellView.PreviewControl.PresentationDiagnostics);
            StructuralShellLayoutSnapshot layout = await OnFormAsync(
                host,
                () => host.ShellView.CaptureLayoutSnapshot());
            ProbeWindowOwner[] owners = WhiteFrameProbePoints
                .Select((normalized, index) => DescribePointOwner(
                    host.Handle,
                    index + 1,
                    origin.X + (int)(width * normalized.X),
                    origin.Y + (int)(height * normalized.Y)))
                .ToArray();
            return new TargetVisibleSnapshot(
                label,
                DateTimeOffset.UtcNow.ToString("O"),
                path,
                host.ShellView.SettingsVisible ? "SETTINGS" : "HOME",
                colors,
                owners,
                RectFact(layout.Root),
                RectFact(layout.Preview),
                RectFact(layout.Deck),
                presentation);
        }
        finally
        {
            await OnFormAsync(
                host,
                () =>
                {
                    overlay.Show();
                    overlay.BringToFront();
                    return true;
                });
            await Task.Delay(80);
        }
    }

    private static Task<Form> ShowControlledOcclusionAsync(
        StructuralAvaloniaShellHost host) => OnFormAsync(
        host,
        () =>
        {
            if (!NativeGateMethods.GetClientRect(
                    host.Handle,
                    out NativeRect client))
            {
                throw new InvalidOperationException(
                    "Controlled occlusion could not read the client bounds.");
            }
            NativePoint origin = new(0, 0);
            if (!NativeGateMethods.ClientToScreen(host.Handle, ref origin))
            {
                throw new InvalidOperationException(
                    "Controlled occlusion could not map the client origin.");
            }
            Form overlay = new()
            {
                Text = "White Frame Detector Controlled Occlusion",
                FormBorderStyle = FormBorderStyle.None,
                StartPosition = FormStartPosition.Manual,
                Bounds = new System.Drawing.Rectangle(
                    origin.X,
                    origin.Y,
                    client.Right - client.Left,
                    client.Bottom - client.Top),
                BackColor = System.Drawing.Color.FromArgb(252, 252, 252),
                TopMost = true,
                ShowInTaskbar = false,
            };
            overlay.Show();
            overlay.BringToFront();
            return overlay;
        });

    private async Task RunWhiteFramePrecheckAsync(
        StructuralAvaloniaShellHost host)
    {
        const int rounds = 5;
        System.Drawing.Rectangle originalBounds = await OnFormAsync(
            host,
            () => host.Bounds);
        System.Drawing.Point originalCursor = default;
        _ = NativeGateMethods.GetCursorPos(out originalCursor);
        try
        {
            System.Drawing.Rectangle testBounds = await PrepareNormalWindowAsync(
                host);
            for (int round = 1; round <= rounds; ++round)
            {
                await ResetBoundsAsync(host, testBounds);
                await DragWindowAsync(
                    host,
                    NativeGateMethods.HtBottomRight,
                    new System.Drawing.Point(-70, -52),
                    validateResizeCursor: false,
                    label: $"whiteframe-precheck-{round}",
                    returnToStart: true,
                    steps: 28);
                await SampleForAsync(
                    host,
                    $"whiteframe-precheck-{round}-settled",
                    250);
            }
        }
        finally
        {
            _ = NativeGateMethods.SetCursorPos(
                originalCursor.X,
                originalCursor.Y);
            if (!host.IsDisposed)
            {
                await OnFormAsync(
                    host,
                    () =>
                    {
                        host.WindowState = FormWindowState.Normal;
                        host.Bounds = originalBounds;
                        return true;
                    });
            }
        }

        _facts["WhiteFramePrecheck"] = new
        {
            Operation = "continuous-shrink-enlarge",
            Rounds = rounds,
            FlaggedWhiteFrameCount = _whiteFrameSamples.Count,
            RealClientGapCount = _whiteFrameSamples.Count,
            InvalidReadCount = _invalidPixelReadCount,
            Timeline = WhiteFrameTimelineFact(),
        };
        Require(
            _whiteFrameSamples.Count == 0,
            "White-frame precheck observed a real client presentation gap.");
        Require(
            _invalidPixelReadCount == 0,
            "White-frame precheck observed an invalid pixel read.");
    }

    private static Task<object> CaptureWhiteFramePointMapAsync(
        StructuralAvaloniaShellHost host,
        string viewLabel) => OnFormAsync(
        host,
        () =>
        {
            StructuralShellView view = host.ShellView;
            StructuralShellLayoutSnapshot layout = view.CaptureLayoutSnapshot();
            NativeGateMethods.GetClientRect(host.Handle, out NativeRect client);
            NativePoint origin = new(0, 0);
            _ = NativeGateMethods.ClientToScreen(host.Handle, ref origin);
            int clientWidth = client.Right - client.Left;
            int clientHeight = client.Bottom - client.Top;
            object[] points = WhiteFrameProbePoints
                .Select((normalized, index) =>
                {
                    global::Avalonia.Point dipPoint = new(
                        layout.Root.Width * normalized.X,
                        layout.Root.Height * normalized.Y);
                    AvaloniaControl? hit = view
                        .GetVisualDescendants()
                        .OfType<AvaloniaControl>()
                        .Where(control => IsEffectivelyVisible(control) &&
                            RectInRoot(view, control).Contains(dipPoint))
                        .LastOrDefault();
                    List<string> visualPath = new();
                    SolidColorBrush? nearestBackground = null;
                    for (AvaloniaControl? current = hit;
                         current is not null;
                         current = current.GetVisualParent() as AvaloniaControl)
                    {
                        if (!string.IsNullOrWhiteSpace(current.Name))
                        {
                            visualPath.Add(
                                $"{current.GetType().Name}#{current.Name}");
                        }
                        else if (visualPath.Count == 0)
                        {
                            visualPath.Add(current.GetType().Name);
                        }
                        nearestBackground ??= BackgroundOf(current)
                            as SolidColorBrush;
                    }

                    bool preview = !view.SettingsVisible &&
                        layout.Preview.Contains(dipPoint);
                    string expectedElement = preview
                        ? "GpuPreviewControl / PreviewFrame"
                        : visualPath.Count == 0
                            ? "UNRESOLVED"
                            : string.Join(" <- ", visualPath);
                    string expectedColor = preview
                        ? "DYNAMIC GPU COMPOSITION"
                        : nearestBackground is null
                            ? "TRANSPARENT / INHERITED"
                            : ColorText(nearestBackground.Color);
                    bool legitimateLightSurface = !preview &&
                        nearestBackground is not null &&
                        nearestBackground.Color.A == byte.MaxValue &&
                        nearestBackground.Color.R >= 240 &&
                        nearestBackground.Color.G >= 240 &&
                        nearestBackground.Color.B >= 237;
                    return (object)new
                    {
                        Point = index + 1,
                        NormalizedX = normalized.X,
                        NormalizedY = normalized.Y,
                        ClientX = (int)(clientWidth * normalized.X),
                        ClientY = (int)(clientHeight * normalized.Y),
                        ScreenX = origin.X + (int)(clientWidth * normalized.X),
                        ScreenY = origin.Y + (int)(clientHeight * normalized.Y),
                        AvaloniaX = dipPoint.X,
                        AvaloniaY = dipPoint.Y,
                        CurrentView = view.SettingsVisible
                            ? "SETTINGS"
                            : "HOME",
                        ExpectedElement = expectedElement,
                        ExpectedColor = expectedColor,
                        LegitimateLightSurface = legitimateLightSurface,
                    };
                })
                .ToArray();
            return (object)new
            {
                RequestedView = viewLabel,
                ActualView = view.SettingsVisible ? "SETTINGS" : "HOME",
                Client = new { Width = clientWidth, Height = clientHeight },
                Layout = new
                {
                    Root = RectFact(layout.Root),
                    Home = RectFact(layout.Home),
                    Preview = RectFact(layout.Preview),
                    Deck = RectFact(layout.Deck),
                },
                Points = points,
            };
        });

    private static IBrush? BackgroundOf(AvaloniaControl control) =>
        control switch
    {
        AvaloniaBorder border => border.Background,
        AvaloniaPanel panel => panel.Background,
        AvaloniaTemplatedControl templated => templated.Background,
        _ => null,
    };

    private static Rect RectInRoot(
        StructuralShellView root,
        AvaloniaControl control)
    {
        Matrix? transform = control.TransformToVisual(root);
        return transform is { } matrix
            ? new Rect(control.Bounds.Size).TransformToAABB(matrix)
            : default;
    }

    private static bool IsEffectivelyVisible(AvaloniaControl control)
    {
        for (global::Avalonia.Visual? current = control;
             current is not null;
             current = current.GetVisualParent())
        {
            if (current is AvaloniaControl currentControl &&
                (!currentControl.IsVisible || currentControl.Opacity <= 0))
            {
                return false;
            }
        }
        return true;
    }

    private static string ColorText(System.Drawing.Color color) =>
        $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    private static string ColorText(global::Avalonia.Media.Color color) =>
        $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";

    private async Task<object> MeasureNavigationTransitionAsync(
        StructuralAvaloniaShellHost host,
        int iteration,
        bool toSettings)
    {
        TaskCompletionSource<long> firstLayout = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler? layoutHandler = null;
        long queued = Stopwatch.GetTimestamp();
        NavigationActionSnapshot action = await OnFormAsync(
            host,
            () =>
            {
                StructuralShellView view = host.ShellView;
                GpuPreviewPresentationDiagnostics before =
                    view.PreviewControl.PresentationDiagnostics;
                layoutHandler = (_, _) =>
                {
                    view.LayoutUpdated -= layoutHandler;
                    firstLayout.TrySetResult(Stopwatch.GetTimestamp());
                };
                view.LayoutUpdated += layoutHandler;
                long started = Stopwatch.GetTimestamp();
                if (toSettings)
                {
                    view.ShowSettings();
                }
                else
                {
                    view.ShowHome();
                }
                long visibleStateSet = Stopwatch.GetTimestamp();
                return new NavigationActionSnapshot(
                    started,
                    visibleStateSet,
                    view.SettingsVisible,
                    before);
            });

        long renderTurn = await Dispatcher.UIThread.InvokeAsync(
            Stopwatch.GetTimestamp,
            DispatcherPriority.Render);
        long layout = firstLayout.Task.IsCompletedSuccessfully
            ? firstLayout.Task.Result
            : 0;
        if (layout == 0)
        {
            await OnFormAsync(
                host,
                () =>
                {
                    if (layoutHandler is not null)
                    {
                        host.ShellView.LayoutUpdated -= layoutHandler;
                    }
                    return true;
                });
        }
        GpuPreviewPresentationDiagnostics after = await OnFormAsync(
            host,
            () => host.ShellView.PreviewControl.PresentationDiagnostics);
        long ended = Stopwatch.GetTimestamp();
        long layoutBasis = layout == 0 ? action.VisibleStateSet : layout;
        return new
        {
            Iteration = iteration,
            Direction = toSettings
                ? "HomeToSettings"
                : "SettingsToHome",
            NavStartUtc = TimestampUtc(queued),
            ActionStartUtc = TimestampUtc(action.Started),
            FirstLayoutUtc = layout == 0 ? null : TimestampUtc(layout),
            FirstPaintUtc = TimestampUtc(renderTurn),
            FirstVisibleContentUtc = TimestampUtc(renderTurn),
            NavEndUtc = TimestampUtc(ended),
            QueueMilliseconds = ElapsedMilliseconds(queued, action.Started),
            VisibilitySwitchMilliseconds = ElapsedMilliseconds(
                action.Started,
                action.VisibleStateSet),
            FirstLayoutMilliseconds = layout == 0
                ? (double?)null
                : ElapsedMilliseconds(action.VisibleStateSet, layout),
            CompositionMilliseconds = ElapsedMilliseconds(
                layoutBasis,
                renderTurn),
            TotalMilliseconds = ElapsedMilliseconds(queued, ended),
            ViewCreationMilliseconds = 0,
            XamlLoadMilliseconds = 0,
            SettingsImageDecodeMilliseconds = 0,
            SettingsImageReferenced = false,
            SettingsVisible = action.SettingsVisible,
            GpuPreviewRemainedAttached =
                action.Before.HasPresentationSource &&
                after.HasPresentationSource &&
                !after.ShutdownStarted,
            GpuBefore = action.Before,
            GpuAfter = after,
        };
    }

    private async Task ValidateDpiLayoutsAsync(
        StructuralAvaloniaShellHost host)
    {
        await OnFormAsync(
            host,
            () =>
            {
                host.ShellView.ShowHome();
                return true;
            });
        await Task.Delay(120);
        StructuralShellLayoutSnapshot logical = await OnFormAsync(
            host,
            () => host.ShellView.CaptureLayoutSnapshot());
        List<object> scales = new();
        foreach (double scale in new[] { 1.00, 1.25, 1.50, 2.00 })
        {
            bool logicalPass = ValidateLogicalLayout(logical, scale,
                out object fact);
            Require(logicalPass, $"DPI layout projection {scale:F2} failed.");
            scales.Add(fact);
        }
        _facts["DpiLogical"] = scales;
    }

    private static bool ValidateLogicalLayout(
        StructuralShellLayoutSnapshot snapshot,
        double scale,
        out object fact)
    {
        Rect root = snapshot.Root;
        Rect home = snapshot.Home;
        Rect preview = snapshot.Preview;
        Rect deck = snapshot.Deck;
        bool finite = new[]
        {
            root.X, root.Y, root.Width, root.Height,
            home.X, home.Y, home.Width, home.Height,
            preview.X, preview.Y, preview.Width, preview.Height,
            deck.X, deck.Y, deck.Width, deck.Height,
        }.All(double.IsFinite);
        bool nonnegative = new[] { root, home, preview, deck }.All(
            rectangle => rectangle.X >= 0 && rectangle.Y >= 0 &&
                rectangle.Width > 0 && rectangle.Height > 0);
        bool contained = Contains(root, home) && Contains(home, preview) &&
            Contains(home, deck);
        bool separated = preview.Bottom <= deck.Top + 0.01;
        int physicalRootWidth = checked((int)Math.Round(root.Width * scale));
        int physicalRootHeight = checked((int)Math.Round(root.Height * scale));
        int physicalPreviewBottom = checked((int)Math.Round(
            preview.Bottom * scale));
        int physicalDeckTop = checked((int)Math.Round(deck.Top * scale));
        int resizeBand = Math.Max(1, checked((int)Math.Round(8 * scale)));
        bool cursorGeometry = resizeBand * 2 < physicalRootWidth &&
            resizeBand * 2 < physicalRootHeight;
        bool physicalSeparated = physicalPreviewBottom <=
            physicalDeckTop + 1;
        bool pass = finite && nonnegative && contained && separated &&
            physicalSeparated && cursorGeometry;
        fact = new
        {
            Scale = scale,
            Pass = pass,
            Logical = new
            {
                Root = RectFact(root),
                Home = RectFact(home),
                Preview = RectFact(preview),
                Deck = RectFact(deck),
            },
            PhysicalRoot = new
            {
                Width = physicalRootWidth,
                Height = physicalRootHeight,
            },
            physicalPreviewBottom,
            physicalDeckTop,
            CursorEdgeBand = resizeBand,
            finite,
            nonnegative,
            contained,
            separated,
            physicalSeparated,
            cursorGeometry,
        };
        return pass;
    }

    private async Task ValidateCrossDpiAsync(
        StructuralAvaloniaShellHost host,
        System.Drawing.Rectangle returnBounds)
    {
        List<object> monitors = new();
        HashSet<uint> distinctDpi = new();
        foreach (System.Windows.Forms.Screen screen in
                 System.Windows.Forms.Screen.AllScreens)
        {
            System.Drawing.Rectangle work = screen.WorkingArea;
            await OnFormAsync(
                host,
                () =>
                {
                    host.WindowState = FormWindowState.Normal;
                    host.Location = new System.Drawing.Point(
                        work.Left + Math.Max(0,
                            (work.Width - host.Width) / 2),
                        work.Top + Math.Max(0,
                            (work.Height - host.Height) / 2));
                    return true;
                });
            await Task.Delay(250);
            uint dpi = NativeGateMethods.GetDpiForWindow(host.Handle);
            distinctDpi.Add(dpi);
            monitors.Add(new
            {
                screen.DeviceName,
                Dpi = dpi,
                Bounds = new
                {
                    work.Left,
                    work.Top,
                    work.Width,
                    work.Height,
                },
            });
        }
        await ResetBoundsAsync(host, returnBounds);
        _facts["CrossDpi"] = new
        {
            Status = distinctDpi.Count > 1
                ? "PROVEN"
                : "NOT PROVEN / ENVIRONMENT",
            Monitors = monitors,
        };
    }

    private async Task ValidateWindowsScreenshotAsync(
        StructuralAvaloniaShellHost host)
    {
        nint hwnd = host.Handle;
        object before = VisibilityFact(hwnd);
        _ = NativeGateMethods.SetForegroundWindow(hwnd);
        NativeGateMethods.SendWindowsShiftS();
        await Task.Delay(650);
        object during = VisibilityFact(hwnd);
        NativeGateMethods.SendEscape();
        await Task.Delay(350);
        object after = VisibilityFact(hwnd);
        bool pass = NativeGateMethods.IsWindowVisible(hwnd) &&
            !NativeGateMethods.IsIconic(hwnd);
        Require(pass, "Windows screenshot activation minimized the shell.");
        _facts["WindowsScreenshot"] = new
        {
            Pass = pass,
            Before = before,
            During = during,
            After = after,
        };
        _facts["WeChatAltA"] = new
        {
            Status = "NOT PROVEN",
            Reason = "No safe, task-owned WeChat main window was established.",
        };
    }

    private async Task DragWindowAsync(
        StructuralAvaloniaShellHost host,
        int hitTest,
        System.Drawing.Point delta,
        bool validateResizeCursor,
        string label,
        bool returnToStart = false,
        int steps = 16)
    {
        System.Drawing.Rectangle bounds = await OnFormAsync(
            host,
            () => host.Bounds);
        System.Drawing.Point start = HitPoint(bounds, hitTest);
        nint expectedCursor = ExpectedCursor(hitTest);
        _ = NativeGateMethods.SetForegroundWindow(host.Handle);
        _ = NativeGateMethods.SetCursorPos(start.X, start.Y);
        await Task.Delay(120);
        int actualHit = unchecked((int)(long)NativeGateMethods.SendMessageW(
            host.Handle,
            NativeGateMethods.WmNcHitTest,
            nint.Zero,
            MakeLParam(start.X, start.Y)));
        Require(
            actualHit == hitTest,
            $"{label} hit-test expected {hitTest}, got {actualHit}.");

        bool cursorLocked = !validateResizeCursor ||
            await WaitForCursorAsync(expectedCursor, 500);
        Require(cursorLocked, $"{label} resize cursor never stabilized.");
        bool? lastExpected = null;
        int unexpected = 0;
        int flicker = 0;

        NativeGateMethods.MouseLeftDown();
        try
        {
            for (int index = 1; index <= steps; ++index)
            {
                double progress = (double)index / steps;
                System.Drawing.Point point = new(
                    start.X + (int)Math.Round(delta.X * progress),
                    start.Y + (int)Math.Round(delta.Y * progress));
                _ = NativeGateMethods.SetCursorPos(point.X, point.Y);
                await Task.Delay(18);
                SampleWhiteFrame(host, label);
                if (validateResizeCursor && cursorLocked)
                {
                    bool currentExpected = CursorIs(expectedCursor);
                    if (!currentExpected)
                    {
                        ++unexpected;
                    }
                    if (lastExpected.HasValue &&
                        lastExpected.Value != currentExpected)
                    {
                        ++flicker;
                    }
                    lastExpected = currentExpected;
                }
            }

            if (returnToStart)
            {
                for (int index = steps - 1; index >= 0; --index)
                {
                    double progress = (double)index / steps;
                    System.Drawing.Point point = new(
                        start.X + (int)Math.Round(delta.X * progress),
                        start.Y + (int)Math.Round(delta.Y * progress));
                    _ = NativeGateMethods.SetCursorPos(point.X, point.Y);
                    await Task.Delay(18);
                    SampleWhiteFrame(host, label);
                    if (validateResizeCursor && cursorLocked)
                    {
                        bool currentExpected = CursorIs(expectedCursor);
                        if (!currentExpected)
                        {
                            ++unexpected;
                        }
                        if (lastExpected.HasValue &&
                            lastExpected.Value != currentExpected)
                        {
                            ++flicker;
                        }
                        lastExpected = currentExpected;
                    }
                }
            }
        }
        finally
        {
            NativeGateMethods.MouseLeftUp();
        }

        _cursorFlickerCount += flicker;
        _unexpectedCursorSwitchCount += unexpected;
        _cursorFacts.Add(new
        {
            Label = label,
            ExpectedHitTest = hitTest,
            ActualHitTest = actualHit,
            CursorStable = cursorLocked,
            Flicker = flicker,
            Unexpected = unexpected,
        });
    }

    private async Task SampleForAsync(
        StructuralAvaloniaShellHost host,
        string label,
        int milliseconds)
    {
        int samples = Math.Max(1, milliseconds / 25);
        for (int index = 0; index < samples; ++index)
        {
            SampleWhiteFrame(host, label);
            await Task.Delay(25);
        }
    }

    private void SampleWhiteFrame(
        StructuralAvaloniaShellHost host,
        string label)
    {
        nint hwnd = host.Handle;
        if (!NativeGateMethods.GetClientRect(hwnd, out NativeRect client) ||
            client.Right <= client.Left || client.Bottom <= client.Top)
        {
            return;
        }
        NativePoint origin = new(0, 0);
        if (!NativeGateMethods.ClientToScreen(hwnd, ref origin))
        {
            return;
        }

        int width = client.Right - client.Left;
        int height = client.Bottom - client.Top;
        nint dc = NativeGateMethods.GetDC(nint.Zero);
        if (dc == nint.Zero)
        {
            return;
        }
        try
        {
            bool allWhite = true;
            List<uint> colors = new(WhiteFrameProbePoints.Length);
            int invalidReadCount = 0;
            foreach ((double x, double y) in WhiteFrameProbePoints)
            {
                uint color = NativeGateMethods.GetPixel(
                    dc,
                    origin.X + (int)(width * x),
                    origin.Y + (int)(height * y));
                colors.Add(color);
                if (color == uint.MaxValue)
                {
                    ++invalidReadCount;
                    ++_invalidPixelReadCount;
                    allWhite = false;
                    break;
                }
                byte red = (byte)(color & 0xff);
                byte green = (byte)((color >> 8) & 0xff);
                byte blue = (byte)((color >> 16) & 0xff);
                if (red < 250 || green < 250 || blue < 250)
                {
                    allWhite = false;
                    break;
                }
            }
            if (allWhite)
            {
                long timestamp = Stopwatch.GetTimestamp();
                _whiteFrameSamples.Add(new WhiteFrameSample(
                    label,
                    TimestampUtc(timestamp),
                    ElapsedMilliseconds(_timelineStart, timestamp),
                    invalidReadCount,
                    colors.Select(static color => $"0x{color:X8}").ToArray()));
                if (_captureFlaggedFrames)
                {
                    _classificationFrames.Add(CaptureFlaggedFrame(
                        hwnd,
                        origin,
                        width,
                        height,
                        label,
                        timestamp,
                        colors));
                }
            }
        }
        finally
        {
            _ = NativeGateMethods.ReleaseDC(nint.Zero, dc);
        }
    }

    private FlaggedFrameArtifact CaptureFlaggedFrame(
        nint hwnd,
        NativePoint origin,
        int width,
        int height,
        string label,
        long timestamp,
        IReadOnlyList<uint> desktopColors)
    {
        int sequence = ++_classificationFlaggedSequence;
        string captureDirectory = Path.Combine(
            Path.GetDirectoryName(_request.EvidencePath)!,
            "flagged-frames");
        Directory.CreateDirectory(captureDirectory);
        string safeLabel = string.Concat(label.Select(character =>
            char.IsLetterOrDigit(character) || character == '-'
                ? character
                : '_'));
        string stem = $"flagged-{sequence:D4}-{safeLabel}";
        string screenPath = Path.Combine(
            captureDirectory,
            stem + "-screen-visible.png");
        string clientPath = Path.Combine(
            captureDirectory,
            stem + "-target-client.png");

        bool screenCaptured = false;
        bool clientCaptured = false;
        string? screenError = null;
        string? clientError = null;
        string[] screenImageColors = Array.Empty<string>();
        string[] clientImageColors = Array.Empty<string>();
        string[] clientDcColors = ReadClientDcColors(hwnd, width, height);
        ProbeWindowOwner[] ownersAtDetection = WhiteFrameProbePoints
            .Select((normalized, index) => DescribePointOwner(
                hwnd,
                index + 1,
                origin.X + (int)(width * normalized.X),
                origin.Y + (int)(height * normalized.Y)))
            .ToArray();
        _ = NativeGateMethods.GetWindowRect(
            hwnd,
            out NativeRect windowRectAtDetection);
        try
        {
            using System.Drawing.Bitmap screen = new(width, height);
            using (System.Drawing.Graphics graphics =
                   System.Drawing.Graphics.FromImage(screen))
            {
                graphics.CopyFromScreen(
                    origin.X,
                    origin.Y,
                    0,
                    0,
                    new System.Drawing.Size(width, height),
                    System.Drawing.CopyPixelOperation.SourceCopy);
            }
            screenImageColors = ReadBitmapColors(screen, width, height);
            screen.Save(
                screenPath,
                System.Drawing.Imaging.ImageFormat.Png);
            screenCaptured = true;
        }
        catch (Exception error)
        {
            screenError = error.ToString();
        }

        try
        {
            using System.Drawing.Bitmap client = new(width, height);
            using (System.Drawing.Graphics graphics =
                   System.Drawing.Graphics.FromImage(client))
            {
                nint targetDc = graphics.GetHdc();
                try
                {
                    clientCaptured = NativeGateMethods.PrintWindow(
                        hwnd,
                        targetDc,
                        NativeGateMethods.PwClientOnly |
                            NativeGateMethods.PwRenderFullContent);
                }
                finally
                {
                    graphics.ReleaseHdc(targetDc);
                }
            }
            clientImageColors = ReadBitmapColors(client, width, height);
            client.Save(
                clientPath,
                System.Drawing.Imaging.ImageFormat.Png);
        }
        catch (Exception error)
        {
            clientError = error.ToString();
            clientCaptured = false;
        }

        ProbeWindowOwner[] ownersAfterCapture = WhiteFrameProbePoints
            .Select((normalized, index) => DescribePointOwner(
                hwnd,
                index + 1,
                origin.X + (int)(width * normalized.X),
                origin.Y + (int)(height * normalized.Y)))
            .ToArray();
        return new FlaggedFrameArtifact(
            sequence,
            label,
            TimestampUtc(timestamp),
            ElapsedMilliseconds(_timelineStart, timestamp),
            screenPath,
            clientPath,
            screenCaptured,
            clientCaptured,
            screenError,
            clientError,
            desktopColors.Select(ColorRefText).ToArray(),
            screenImageColors,
            clientImageColors,
            clientDcColors,
            ownersAtDetection,
            ownersAfterCapture,
            NativeRectFact(windowRectAtDetection),
            NativeWindowRectFact(hwnd),
            new { origin.X, origin.Y, width, height });
    }

    private static string[] ReadClientDcColors(
        nint hwnd,
        int width,
        int height)
    {
        nint dc = NativeGateMethods.GetDC(hwnd);
        if (dc == nint.Zero)
        {
            return Array.Empty<string>();
        }
        try
        {
            return WhiteFrameProbePoints
                .Select(normalized => ColorRefText(NativeGateMethods.GetPixel(
                    dc,
                    (int)(width * normalized.X),
                    (int)(height * normalized.Y))))
                .ToArray();
        }
        finally
        {
            _ = NativeGateMethods.ReleaseDC(hwnd, dc);
        }
    }

    private static string[] ReadBitmapColors(
        System.Drawing.Bitmap bitmap,
        int width,
        int height) => WhiteFrameProbePoints
        .Select(normalized =>
        {
            int x = Math.Clamp((int)(width * normalized.X), 0, width - 1);
            int y = Math.Clamp((int)(height * normalized.Y), 0, height - 1);
            return ColorText(bitmap.GetPixel(x, y));
        })
        .ToArray();

    private static ProbeWindowOwner DescribePointOwner(
        nint host,
        int point,
        int screenX,
        int screenY)
    {
        nint window = NativeGateMethods.WindowFromPoint(
            new NativePoint(screenX, screenY));
        nint root = window == nint.Zero
            ? nint.Zero
            : NativeGateMethods.GetAncestor(
                window,
                NativeGateMethods.GaRoot);
        StringBuilder title = new(512);
        StringBuilder className = new(256);
        if (root != nint.Zero)
        {
            _ = NativeGateMethods.GetWindowTextW(
                root,
                title,
                title.Capacity);
            _ = NativeGateMethods.GetClassNameW(
                root,
                className,
                className.Capacity);
        }
        _ = NativeGateMethods.GetWindowThreadProcessId(
            root,
            out uint processId);
        string processName;
        try
        {
            processName = processId == 0
                ? string.Empty
                : Process.GetProcessById((int)processId).ProcessName;
        }
        catch
        {
            processName = "UNAVAILABLE";
        }
        return new ProbeWindowOwner(
            point,
            screenX,
            screenY,
            $"0x{window:X}",
            $"0x{root:X}",
            root == host,
            title.ToString(),
            className.ToString(),
            processId,
            processName);
    }

    private static string ColorRefText(uint color)
    {
        if (color == uint.MaxValue)
        {
            return "INVALID";
        }
        byte red = (byte)(color & 0xff);
        byte green = (byte)((color >> 8) & 0xff);
        byte blue = (byte)((color >> 16) & 0xff);
        return $"#{red:X2}{green:X2}{blue:X2}";
    }

    private static object NativeWindowRectFact(nint hwnd)
    {
        _ = NativeGateMethods.GetWindowRect(hwnd, out NativeRect rect);
        return NativeRectFact(rect);
    }

    private static object NativeRectFact(NativeRect rect) => new
    {
        rect.Left,
        rect.Top,
        rect.Right,
        rect.Bottom,
        Width = rect.Right - rect.Left,
        Height = rect.Bottom - rect.Top,
    };

    private object WhiteFrameTimelineFact()
    {
        WhiteFrameSample[] samples = _whiteFrameSamples.ToArray();
        Dictionary<string, int> byOperation = samples
            .GroupBy(sample => sample.Operation, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Count(),
                StringComparer.Ordinal);
        double longest = 0;
        double runStart = 0;
        double previous = 0;
        for (int index = 0; index < samples.Length; ++index)
        {
            double current = samples[index].ElapsedMilliseconds;
            if (index == 0 || current - previous > 60)
            {
                runStart = current;
            }
            longest = Math.Max(longest, current - runStart);
            previous = current;
        }
        return new
        {
            Count = samples.Length,
            ByOperation = byOperation,
            FirstTimestampUtc = samples.Length == 0
                ? null
                : samples[0].TimestampUtc,
            LastTimestampUtc = samples.Length == 0
                ? null
                : samples[^1].TimestampUtc,
            LongestContiguousMilliseconds = longest,
            TotalInvalidReadCount = _invalidPixelReadCount,
            InvalidReadSamples = samples.Count(
                sample => sample.InvalidReadCount > 0),
            Samples = samples,
        };
    }

    private string TimestampUtc(long timestamp) =>
        (_timelineStartUtc + Stopwatch.GetElapsedTime(
            _timelineStart,
            timestamp)).ToString("O");

    private static double ElapsedMilliseconds(long start, long end) =>
        Stopwatch.GetElapsedTime(start, end).TotalMilliseconds;

    private readonly record struct NavigationActionSnapshot(
        long Started,
        long VisibleStateSet,
        bool SettingsVisible,
        GpuPreviewPresentationDiagnostics Before);

    private sealed record WhiteFrameSample(
        string Operation,
        string TimestampUtc,
        double ElapsedMilliseconds,
        int InvalidReadCount,
        string[] Colors);

    private sealed record FlaggedFrameArtifact(
        int Sequence,
        string Operation,
        string TimestampUtc,
        double ElapsedMilliseconds,
        string ScreenVisiblePath,
        string TargetClientPath,
        bool ScreenCaptureSucceeded,
        bool TargetClientCaptureSucceeded,
        string? ScreenCaptureError,
        string? TargetClientCaptureError,
        string[] DesktopDcColors,
        string[] ScreenImageColors,
        string[] TargetClientImageColors,
        string[] TargetClientDcColors,
        ProbeWindowOwner[] PointOwnersAtDetection,
        ProbeWindowOwner[] PointOwnersAfterCapture,
        object TargetWindowRectAtDetection,
        object TargetWindowRectAfterCapture,
        object ClientProbeGeometry);

    private sealed record ProbeWindowOwner(
        int Point,
        int ScreenX,
        int ScreenY,
        string Window,
        string RootWindow,
        bool RootIsTargetHost,
        string RootTitle,
        string RootClass,
        uint ProcessId,
        string ProcessName);

    private sealed record TargetVisibleSnapshot(
        string Label,
        string TimestampUtc,
        string Path,
        string CurrentView,
        string[] Colors,
        ProbeWindowOwner[] PointOwners,
        object RootBounds,
        object PreviewBounds,
        object DeckBounds,
        GpuPreviewPresentationDiagnostics Presentation);

    private static async Task ResetBoundsAsync(
        StructuralAvaloniaShellHost host,
        System.Drawing.Rectangle bounds)
    {
        await OnFormAsync(
            host,
            () =>
            {
                host.WindowState = FormWindowState.Normal;
                host.Bounds = bounds;
                host.Activate();
                return true;
            });
        await Task.Delay(140);
    }

    private static async Task<bool> WaitForCursorAsync(
        nint expected,
        int timeoutMilliseconds)
    {
        long deadline = Stopwatch.GetTimestamp() +
            (long)(Stopwatch.Frequency * timeoutMilliseconds / 1000.0);
        while (Stopwatch.GetTimestamp() < deadline)
        {
            if (CursorIs(expected))
            {
                return true;
            }
            await Task.Delay(20);
        }
        return false;
    }

    private static bool CursorIs(nint expected)
    {
        NativeCursorInfo cursor = new()
        {
            Size = Marshal.SizeOf<NativeCursorInfo>(),
        };
        return NativeGateMethods.GetCursorInfo(ref cursor) &&
            cursor.Cursor == expected;
    }

    private static System.Drawing.Point HitPoint(
        System.Drawing.Rectangle bounds,
        int hit)
    {
        int left = bounds.Left + 2;
        int right = bounds.Right - 3;
        int top = bounds.Top + 2;
        int bottom = bounds.Bottom - 3;
        int middleX = bounds.Left + bounds.Width / 2;
        int middleY = bounds.Top + bounds.Height / 2;
        return hit switch
        {
            NativeGateMethods.HtCaption => new(middleX, bounds.Top + 14),
            NativeGateMethods.HtLeft => new(left, middleY),
            NativeGateMethods.HtRight => new(right, middleY),
            NativeGateMethods.HtTop => new(middleX, top),
            NativeGateMethods.HtBottom => new(middleX, bottom),
            NativeGateMethods.HtTopLeft => new(left, top),
            NativeGateMethods.HtTopRight => new(right, top),
            NativeGateMethods.HtBottomLeft => new(left, bottom),
            NativeGateMethods.HtBottomRight => new(right, bottom),
            _ => throw new ArgumentOutOfRangeException(nameof(hit)),
        };
    }

    private static nint ExpectedCursor(int hit) => hit switch
    {
        NativeGateMethods.HtLeft or NativeGateMethods.HtRight =>
            NativeGateMethods.SizeWeCursor,
        NativeGateMethods.HtTop or NativeGateMethods.HtBottom =>
            NativeGateMethods.SizeNsCursor,
        NativeGateMethods.HtTopLeft or NativeGateMethods.HtBottomRight =>
            NativeGateMethods.SizeNwseCursor,
        NativeGateMethods.HtTopRight or NativeGateMethods.HtBottomLeft =>
            NativeGateMethods.SizeNeswCursor,
        _ => nint.Zero,
    };

    private static nint MakeLParam(int x, int y) => unchecked((nint)(
        (uint)(ushort)x | ((uint)(ushort)y << 16)));

    private static bool Contains(Rect outer, Rect inner) =>
        inner.Left >= outer.Left - 0.01 &&
        inner.Top >= outer.Top - 0.01 &&
        inner.Right <= outer.Right + 0.01 &&
        inner.Bottom <= outer.Bottom + 0.01;

    private static object RectFact(Rect rectangle) => new
    {
        rectangle.X,
        rectangle.Y,
        rectangle.Width,
        rectangle.Height,
        rectangle.Right,
        rectangle.Bottom,
    };

    private static object VisibilityFact(nint hwnd) => new
    {
        Visible = NativeGateMethods.IsWindowVisible(hwnd),
        IsIconic = NativeGateMethods.IsIconic(hwnd),
    };

    private void Require(bool condition, string failure)
    {
        if (!condition)
        {
            _failures.Add(failure);
        }
    }

    private static Task<T> OnFormAsync<T>(
        StructuralAvaloniaShellHost host,
        Func<T> action)
    {
        TaskCompletionSource<T> completion = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        if (host.IsDisposed)
        {
            completion.SetException(new ObjectDisposedException(
                nameof(StructuralAvaloniaShellHost)));
            return completion.Task;
        }
        try
        {
            host.BeginInvoke((Action)(() =>
            {
                try
                {
                    completion.TrySetResult(action());
                }
                catch (Exception error)
                {
                    completion.TrySetException(error);
                }
            }));
        }
        catch (Exception error)
        {
            completion.TrySetException(error);
        }
        return completion.Task;
    }

    private static void WriteEvidence(string path, object evidence)
    {
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }
        string temporaryPath = path + ".tmp";
        File.WriteAllText(
            temporaryPath,
            JsonSerializer.Serialize(
                evidence,
                new JsonSerializerOptions { WriteIndented = true }),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.Move(temporaryPath, path, overwrite: true);
    }

}

internal readonly record struct DispatcherPhaseSnapshot(
    int SampleCount,
    int StallCount,
    double MaxMilliseconds);

internal readonly record struct TimedLatencySample(
    long QueuedTicks,
    long CallbackTicks,
    string QueuedTimestampUtc,
    string CallbackTimestampUtc,
    double LatencyMilliseconds);

internal sealed class DispatcherHeartbeat
{
    private readonly ConcurrentDictionary<
        StructuralShellPhase,
        ConcurrentQueue<TimedLatencySample>> _samples = new();
    private readonly CancellationTokenSource _stop = new();
    private Task? _pump;
    private int _phase;

    internal void Start()
    {
        _pump ??= Task.Run(PumpAsync);
    }

    internal void SetPhase(StructuralShellPhase phase)
    {
        Volatile.Write(ref _phase, (int)phase);
    }

    internal DispatcherPhaseSnapshot Snapshot(StructuralShellPhase phase)
    {
        if (!_samples.TryGetValue(
                phase,
                out ConcurrentQueue<TimedLatencySample>? values))
        {
            return default;
        }
        TimedLatencySample[] snapshot = values.ToArray();
        return new DispatcherPhaseSnapshot(
            snapshot.Length,
            snapshot.Count(value => value.LatencyMilliseconds > 50.0),
            snapshot.Length == 0
                ? 0
                : snapshot.Max(value => value.LatencyMilliseconds));
    }

    internal TimedLatencySample[] Samples(StructuralShellPhase phase) =>
        _samples.TryGetValue(
            phase,
            out ConcurrentQueue<TimedLatencySample>? values)
            ? values.ToArray()
            : Array.Empty<TimedLatencySample>();

    internal async Task StopAsync()
    {
        _stop.Cancel();
        if (_pump is not null)
        {
            try
            {
                await _pump;
            }
            catch (OperationCanceledException)
            {
            }
        }
        _stop.Dispose();
    }

    private async Task PumpAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            StructuralShellPhase phase = (StructuralShellPhase)Volatile.Read(
                ref _phase);
            long queued = Stopwatch.GetTimestamp();
            TaskCompletionSource completion = new(
                TaskCreationOptions.RunContinuationsAsynchronously);
            Dispatcher.UIThread.Post(
                () =>
                {
                    long callback = Stopwatch.GetTimestamp();
                    TimedLatencySample sample = new(
                        queued,
                        callback,
                        TimestampUtc(queued),
                        TimestampUtc(callback),
                        Stopwatch.GetElapsedTime(queued, callback)
                            .TotalMilliseconds);
                    if (phase != StructuralShellPhase.None)
                    {
                        _samples.GetOrAdd(
                            phase,
                            static _ =>
                                new ConcurrentQueue<TimedLatencySample>())
                            .Enqueue(sample);
                    }
                    completion.TrySetResult();
                },
                DispatcherPriority.Send);
            await completion.Task.WaitAsync(
                TimeSpan.FromSeconds(2),
                _stop.Token);
            await Task.Delay(8, _stop.Token);
        }
    }

    private static string TimestampUtc(long timestamp)
    {
        long delta = timestamp - Stopwatch.GetTimestamp();
        return DateTimeOffset.UtcNow
            .AddSeconds((double)delta / Stopwatch.Frequency)
            .ToString("O");
    }
}

internal readonly record struct ProbeLatencySample(
    long StartedTicks,
    long CompletedTicks,
    string StartedTimestampUtc,
    string CompletedTimestampUtc,
    double LatencyMilliseconds,
    bool Succeeded);

internal readonly record struct ProbeLatencySnapshot(
    int SampleCount,
    int StallCount,
    double MaxMilliseconds,
    ProbeLatencySample[] Spikes);

internal sealed class RawIntervalHeartbeat
{
    private readonly ConcurrentQueue<ProbeLatencySample> _samples = new();
    private readonly CancellationTokenSource _stop = new();
    private Task? _pump;

    internal void Start()
    {
        _pump ??= Task.Run(PumpAsync);
    }

    internal ProbeLatencySnapshot Snapshot() => Snapshot(_samples.ToArray());

    internal async Task StopAsync()
    {
        _stop.Cancel();
        if (_pump is not null)
        {
            try
            {
                await _pump;
            }
            catch (OperationCanceledException)
            {
            }
        }
        _stop.Dispose();
    }

    private async Task PumpAsync()
    {
        long previous = Stopwatch.GetTimestamp();
        while (!_stop.IsCancellationRequested)
        {
            await Task.Delay(8, _stop.Token);
            long current = Stopwatch.GetTimestamp();
            _samples.Enqueue(new ProbeLatencySample(
                previous,
                current,
                ProbeTimestamps.ToUtc(previous),
                ProbeTimestamps.ToUtc(current),
                Stopwatch.GetElapsedTime(previous, current)
                    .TotalMilliseconds,
                Succeeded: true));
            previous = current;
        }
    }

    internal static ProbeLatencySnapshot Snapshot(
        ProbeLatencySample[] samples)
    {
        ProbeLatencySample[] spikes = samples
            .Where(sample => sample.LatencyMilliseconds > 50.0)
            .ToArray();
        return new ProbeLatencySnapshot(
            samples.Length,
            spikes.Length,
            samples.Length == 0
                ? 0
                : samples.Max(sample => sample.LatencyMilliseconds),
            spikes);
    }
}

internal sealed class WindowMessageHeartbeat
{
    private readonly nint _window;
    private readonly ConcurrentQueue<ProbeLatencySample> _samples = new();
    private readonly CancellationTokenSource _stop = new();
    private Task? _pump;

    internal WindowMessageHeartbeat(nint window)
    {
        _window = window;
    }

    internal void Start()
    {
        _pump ??= Task.Run(PumpAsync);
    }

    internal ProbeLatencySnapshot Snapshot() =>
        RawIntervalHeartbeat.Snapshot(_samples.ToArray());

    internal async Task StopAsync()
    {
        _stop.Cancel();
        if (_pump is not null)
        {
            try
            {
                await _pump;
            }
            catch (OperationCanceledException)
            {
            }
        }
        _stop.Dispose();
    }

    private async Task PumpAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            long started = Stopwatch.GetTimestamp();
            bool succeeded = NativeGateMethods.SendMessageTimeoutW(
                _window,
                NativeGateMethods.WmMoveProbe,
                nint.Zero,
                nint.Zero,
                NativeGateMethods.SmtoBlock |
                    NativeGateMethods.SmtoAbortIfHung,
                2000,
                out _);
            long completed = Stopwatch.GetTimestamp();
            _samples.Enqueue(new ProbeLatencySample(
                started,
                completed,
                ProbeTimestamps.ToUtc(started),
                ProbeTimestamps.ToUtc(completed),
                Stopwatch.GetElapsedTime(started, completed)
                    .TotalMilliseconds,
                succeeded));
            await Task.Delay(8, _stop.Token);
        }
    }
}

internal readonly record struct WindowMessageHandlerSample(
    uint Message,
    string MessageName,
    long StartedTicks,
    long CompletedTicks,
    string StartedTimestampUtc,
    string CompletedTimestampUtc,
    double HandlerMilliseconds,
    bool MoveStateBefore,
    bool MoveStateAfter,
    System.Drawing.Rectangle WindowPositionBefore,
    System.Drawing.Rectangle WindowPositionAfter);

internal readonly record struct WindowMoveMessageSnapshot(
    int EnterSizeMoveCount,
    int ExitSizeMoveCount,
    int MovingCount,
    int MoveCount,
    int WindowPosChangedCount,
    int HandlerStallCount,
    double HandlerMaxMilliseconds,
    WindowMessageHandlerSample[] LongestHandlers);

internal sealed class WindowMoveMessageProbe : NativeWindow
{
    private readonly ConcurrentQueue<WindowMessageHandlerSample> _samples =
        new();
    private int _moveState;

    internal bool InMove => Volatile.Read(ref _moveState) != 0;

    internal void Attach(nint handle)
    {
        AssignHandle(handle);
    }

    internal void Detach()
    {
        ReleaseHandle();
    }

    internal bool WasInMoveAt(long timestamp)
    {
        bool state = false;
        foreach (WindowMessageHandlerSample sample in _samples
                     .OrderBy(sample => sample.StartedTicks))
        {
            if (sample.StartedTicks > timestamp)
            {
                break;
            }
            if (sample.Message == NativeGateMethods.WmEnterSizeMove)
            {
                state = true;
            }
            else if (sample.Message == NativeGateMethods.WmExitSizeMove)
            {
                state = false;
            }
        }
        return state;
    }

    internal WindowMessageHandlerSample[] Around(long start, long end)
    {
        long padding = Stopwatch.Frequency / 20;
        return _samples
            .Where(sample =>
                sample.CompletedTicks >= start - padding &&
                sample.StartedTicks <= end + padding)
            .OrderBy(sample => sample.StartedTicks)
            .Take(24)
            .ToArray();
    }

    internal WindowMoveMessageSnapshot Snapshot()
    {
        WindowMessageHandlerSample[] samples = _samples.ToArray();
        WindowMessageHandlerSample[] moveHandlers = samples
            .Where(sample => IsMoveMessage(sample.Message))
            .ToArray();
        return new WindowMoveMessageSnapshot(
            samples.Count(sample =>
                sample.Message == NativeGateMethods.WmEnterSizeMove),
            samples.Count(sample =>
                sample.Message == NativeGateMethods.WmExitSizeMove),
            samples.Count(sample =>
                sample.Message == NativeGateMethods.WmMoving),
            samples.Count(sample =>
                sample.Message == NativeGateMethods.WmMove),
            samples.Count(sample =>
                sample.Message == NativeGateMethods.WmWindowPosChanged),
            moveHandlers.Count(sample => sample.HandlerMilliseconds > 50.0),
            moveHandlers.Length == 0
                ? 0
                : moveHandlers.Max(sample => sample.HandlerMilliseconds),
            moveHandlers
                .OrderByDescending(sample => sample.HandlerMilliseconds)
                .Take(12)
                .ToArray());
    }

    protected override void WndProc(ref Message message)
    {
        if (!IsTrackedMessage((uint)message.Msg))
        {
            base.WndProc(ref message);
            return;
        }

        uint messageId = (uint)message.Msg;
        long started = Stopwatch.GetTimestamp();
        bool stateBefore = Volatile.Read(ref _moveState) != 0;
        System.Drawing.Rectangle before = ReadWindowPosition();
        if (messageId == NativeGateMethods.WmEnterSizeMove)
        {
            Volatile.Write(ref _moveState, 1);
        }

        try
        {
            base.WndProc(ref message);
        }
        finally
        {
            if (messageId == NativeGateMethods.WmExitSizeMove)
            {
                Volatile.Write(ref _moveState, 0);
            }
            long completed = Stopwatch.GetTimestamp();
            _samples.Enqueue(new WindowMessageHandlerSample(
                messageId,
                MessageName(messageId),
                started,
                completed,
                ProbeTimestamps.ToUtc(started),
                ProbeTimestamps.ToUtc(completed),
                Stopwatch.GetElapsedTime(started, completed)
                    .TotalMilliseconds,
                stateBefore,
                Volatile.Read(ref _moveState) != 0,
                before,
                ReadWindowPosition()));
        }
    }

    private System.Drawing.Rectangle ReadWindowPosition()
    {
        return NativeGateMethods.GetWindowRect(Handle, out NativeRect rect)
            ? System.Drawing.Rectangle.FromLTRB(
                rect.Left,
                rect.Top,
                rect.Right,
                rect.Bottom)
            : System.Drawing.Rectangle.Empty;
    }

    private static bool IsTrackedMessage(uint message) =>
        message == NativeGateMethods.WmMoveProbe ||
        message == NativeGateMethods.WmEnterSizeMove ||
        message == NativeGateMethods.WmExitSizeMove ||
        IsMoveMessage(message);

    private static bool IsMoveMessage(uint message) =>
        message == NativeGateMethods.WmMoving ||
        message == NativeGateMethods.WmMove ||
        message == NativeGateMethods.WmWindowPosChanged;

    private static string MessageName(uint message) => message switch
    {
        NativeGateMethods.WmMoveProbe => "WM_APP_MOVE_SENTINEL",
        NativeGateMethods.WmEnterSizeMove => "WM_ENTERSIZEMOVE",
        NativeGateMethods.WmExitSizeMove => "WM_EXITSIZEMOVE",
        NativeGateMethods.WmMoving => "WM_MOVING",
        NativeGateMethods.WmMove => "WM_MOVE",
        NativeGateMethods.WmWindowPosChanged => "WM_WINDOWPOSCHANGED",
        _ => $"0x{message:X4}",
    };
}

internal static class ProbeTimestamps
{
    internal static string ToUtc(long timestamp)
    {
        long delta = timestamp - Stopwatch.GetTimestamp();
        return DateTimeOffset.UtcNow
            .AddSeconds((double)delta / Stopwatch.Frequency)
            .ToString("O");
    }
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeRect
{
    internal int Left;
    internal int Top;
    internal int Right;
    internal int Bottom;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativePoint
{
    internal int X;
    internal int Y;

    internal NativePoint(int x, int y)
    {
        X = x;
        Y = y;
    }
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeCursorInfo
{
    internal int Size;
    internal int Flags;
    internal nint Cursor;
    internal NativePoint ScreenPosition;
}

internal static class NativeGateMethods
{
    internal const uint WmMove = 0x0003;
    internal const uint WmWindowPosChanged = 0x0047;
    internal const int WmNcHitTest = 0x0084;
    internal const uint WmMoving = 0x0216;
    internal const uint WmEnterSizeMove = 0x0231;
    internal const uint WmExitSizeMove = 0x0232;
    internal const uint WmMoveProbe = 0x8000 + 0x51;
    internal const uint SmtoBlock = 0x0001;
    internal const uint SmtoAbortIfHung = 0x0002;
    internal const uint GaRoot = 2;
    internal const uint PwClientOnly = 0x0000_0001;
    internal const uint PwRenderFullContent = 0x0000_0002;
    internal const int HtCaption = 2;
    internal const int HtLeft = 10;
    internal const int HtRight = 11;
    internal const int HtTop = 12;
    internal const int HtTopLeft = 13;
    internal const int HtTopRight = 14;
    internal const int HtBottom = 15;
    internal const int HtBottomLeft = 16;
    internal const int HtBottomRight = 17;
    private const uint MouseEventLeftDown = 0x0002;
    private const uint MouseEventLeftUp = 0x0004;
    private const byte VkLeftWindows = 0x5B;
    private const byte VkShift = 0x10;
    private const byte VkS = 0x53;
    private const byte VkEscape = 0x1B;
    private const uint KeyEventKeyUp = 0x0002;

    internal static readonly nint SizeWeCursor = LoadCursorW(
        nint.Zero,
        (nint)32644);
    internal static readonly nint SizeNsCursor = LoadCursorW(
        nint.Zero,
        (nint)32645);
    internal static readonly nint SizeNwseCursor = LoadCursorW(
        nint.Zero,
        (nint)32642);
    internal static readonly nint SizeNeswCursor = LoadCursorW(
        nint.Zero,
        (nint)32643);

    internal static void MouseLeftDown() => mouse_event(
        MouseEventLeftDown,
        0,
        0,
        0,
        0);

    internal static void MouseLeftUp() => mouse_event(
        MouseEventLeftUp,
        0,
        0,
        0,
        0);

    internal static void SendWindowsShiftS()
    {
        keybd_event(VkLeftWindows, 0, 0, 0);
        keybd_event(VkShift, 0, 0, 0);
        keybd_event(VkS, 0, 0, 0);
        keybd_event(VkS, 0, KeyEventKeyUp, 0);
        keybd_event(VkShift, 0, KeyEventKeyUp, 0);
        keybd_event(VkLeftWindows, 0, KeyEventKeyUp, 0);
    }

    internal static void SendEscape()
    {
        keybd_event(VkEscape, 0, 0, 0);
        keybd_event(VkEscape, 0, KeyEventKeyUp, 0);
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetCursorPos(out System.Drawing.Point point);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    internal static extern nint SendMessageW(
        nint hwnd,
        uint message,
        nint wParam,
        nint lParam);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SendMessageTimeoutW(
        nint hwnd,
        uint message,
        nint wParam,
        nint lParam,
        uint flags,
        uint timeoutMilliseconds,
        out nuint result);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetCursorInfo(ref NativeCursorInfo cursorInfo);

    [DllImport("user32.dll")]
    internal static extern nint LoadCursorW(nint instance, nint cursorName);

    [DllImport("user32.dll")]
    internal static extern void mouse_event(
        uint flags,
        uint dx,
        uint dy,
        uint data,
        nuint extraInfo);

    [DllImport("user32.dll")]
    internal static extern void keybd_event(
        byte virtualKey,
        byte scanCode,
        uint flags,
        nuint extraInfo);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetForegroundWindow(nint hwnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsWindowVisible(nint hwnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsIconic(nint hwnd);

    [DllImport("user32.dll")]
    internal static extern nint WindowFromPoint(NativePoint point);

    [DllImport("user32.dll")]
    internal static extern nint GetAncestor(nint hwnd, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern int GetWindowTextW(
        nint hwnd,
        StringBuilder text,
        int maximumCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern int GetClassNameW(
        nint hwnd,
        StringBuilder className,
        int maximumCount);

    [DllImport("user32.dll")]
    internal static extern uint GetWindowThreadProcessId(
        nint hwnd,
        out uint processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool PrintWindow(
        nint hwnd,
        nint targetDc,
        uint flags);

    [DllImport("user32.dll")]
    internal static extern uint GetDpiForWindow(nint hwnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetClientRect(nint hwnd, out NativeRect rect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetWindowRect(nint hwnd, out NativeRect rect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ClientToScreen(
        nint hwnd,
        ref NativePoint point);

    [DllImport("user32.dll")]
    internal static extern nint GetDC(nint hwnd);

    [DllImport("user32.dll")]
    internal static extern int ReleaseDC(nint hwnd, nint dc);

    [DllImport("gdi32.dll")]
    internal static extern uint GetPixel(nint dc, int x, int y);
}
