using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace XbPreview.Host;

/// <summary>
/// Stops the real body from repeating transparent custom-paint work while the
/// opaque resize proxy owns the visible body. It is toggled only on the UI
/// thread and is released before final layout/render.
/// </summary>
internal static class FormalUiV4ResizeBodyPaintGate
{
    private static bool _isFrozen;

    internal static bool IsFrozen => _isFrozen;

    internal static void Freeze() => _isFrozen = true;

    internal static void Thaw() => _isFrozen = false;
}

/// <summary>
/// A single-layer, opaque bitmap surface used only while the native window is
/// inside an interactive sizing loop. The bitmap is captured once per sizing
/// session and is never regenerated from OnPaint or OnResize.
/// </summary>
internal sealed class FormalUiResizeProxySurface : Control
{
    private const long MaximumSnapshotPixels = 24_000_000;
    private Bitmap? _snapshot;

    internal FormalUiResizeProxySurface()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.Opaque |
            ControlStyles.ResizeRedraw |
            ControlStyles.UserPaint,
            true);
        SetStyle(
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.SupportsTransparentBackColor,
            false);
        BackColor = FormalUiV4Tokens.ShellBottom;
        TabStop = false;
        Visible = false;
    }

    internal bool TryCapture(Control target, out double elapsedMilliseconds, out string? error)
    {
        ArgumentNullException.ThrowIfNull(target);
        Stopwatch timer = Stopwatch.StartNew();
        Bitmap? snapshot = null;
        try
        {
            Size size = target.ClientSize;
            if (size.Width < 1 || size.Height < 1)
            {
                error = $"Invalid snapshot size {size.Width}x{size.Height}.";
                elapsedMilliseconds = timer.Elapsed.TotalMilliseconds;
                return false;
            }
            long pixels = (long)size.Width * size.Height;
            if (pixels > MaximumSnapshotPixels)
            {
                error = $"Snapshot pixel limit exceeded: {pixels}.";
                elapsedMilliseconds = timer.Elapsed.TotalMilliseconds;
                return false;
            }

            FormalUiV4ResizeProbe.BeginSnapshotCapture();
            snapshot = new Bitmap(
                size.Width,
                size.Height,
                PixelFormat.Format32bppPArgb);
            using (Graphics graphics = Graphics.FromImage(snapshot))
            {
                graphics.Clear(BackColor);
            }
            target.DrawToBitmap(snapshot, new Rectangle(Point.Empty, size));

            Bitmap? previous = _snapshot;
            _snapshot = snapshot;
            snapshot = null;
            previous?.Dispose();
            error = null;
            elapsedMilliseconds = timer.Elapsed.TotalMilliseconds;
            Invalidate();
            return true;
        }
        catch (Exception exception) when (
            exception is ArgumentException or ExternalException or InvalidOperationException or OutOfMemoryException)
        {
            error = $"{exception.GetType().Name}: {exception.Message}";
            elapsedMilliseconds = timer.Elapsed.TotalMilliseconds;
            return false;
        }
        finally
        {
            FormalUiV4ResizeProbe.EndSnapshotCapture();
            snapshot?.Dispose();
        }
    }

    internal void ClearSnapshot()
    {
        Bitmap? snapshot = _snapshot;
        _snapshot = null;
        snapshot?.Dispose();
    }

    internal bool HasSnapshot => _snapshot is not null;

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        FormalUiV4ResizeProbe.RecordProxyBackgroundPaint();
        // Opaque: OnPaint clears the invalid region without asking the parent
        // to repaint the expensive transparent body underneath this surface.
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        FormalUiV4ResizeProbe.RecordProxyPaint();
        Bitmap? snapshot = _snapshot;
        if (snapshot is null || ClientSize.Width < 1 || ClientSize.Height < 1)
        {
            e.Graphics.Clear(BackColor);
            FormalUiV4ResizeProbe.RecordProxyFramePresented(hasSnapshot: false);
            return;
        }

        e.Graphics.CompositingQuality = CompositingQuality.HighSpeed;
        e.Graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
        e.Graphics.PixelOffsetMode = PixelOffsetMode.Half;
        e.Graphics.SmoothingMode = SmoothingMode.None;
        e.Graphics.DrawImage(
            snapshot,
            ClientRectangle,
            0,
            0,
            snapshot.Width,
            snapshot.Height,
            GraphicsUnit.Pixel);
        FormalUiV4ResizeProbe.RecordProxyFramePresented(hasSnapshot: true);
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        FormalUiV4ResizeProbe.RecordProxyVisibleChanged(Visible);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            ClearSnapshot();
        }
        base.Dispose(disposing);
    }
}

/// <summary>
/// Environment-gated, test-only counters for the interactive resize probe.
/// The production path pays only a cached boolean branch.
/// </summary>
internal static class FormalUiV4ResizeProbe
{
    private readonly record struct TimelineEntry(
        int Sequence,
        string Event,
        string Phase,
        long Timestamp,
        double Milliseconds);

    private enum ProbePhase
    {
        None,
        Capture,
        Resize,
        Restore,
    }

    private const int GrGdiObjects = 0;
    private static readonly bool ProbeEnabled = string.Equals(
        Environment.GetEnvironmentVariable("XB_FORMAL_RESIZE_PROBE"),
        "1",
        StringComparison.Ordinal);
    private static readonly string? OutputPath =
        Environment.GetEnvironmentVariable("XB_FORMAL_RESIZE_PROBE_PATH");
    private static readonly bool DisableProxyForTest = string.Equals(
        Environment.GetEnvironmentVariable("XB_FORMAL_UI_RESIZE_PROXY_DISABLE"),
        "1",
        StringComparison.Ordinal);

    private static readonly HashSet<Size> DistinctSizes = new();
    private static readonly List<double> EdgeLagSamples = new();
    private static readonly int[] ShellPaint = new int[4];
    private static readonly int[] DeckPaint = new int[4];
    private static readonly int[] PreviewPaint = new int[4];
    private static readonly int[] ProxyPaint = new int[4];
    private static readonly int[] ProxyBackgroundPaint = new int[4];
    private static readonly int[] BodyLayout = new int[4];
    private static readonly int[] RootPaint = new int[4];
    private static readonly int[] WorkspacePaint = new int[4];
    private static readonly int[] FormBackgroundPaint = new int[4];
    private static readonly int[] SuppressedBodyPaint = new int[4];
    private static readonly List<TimelineEntry> Timeline = new();
    private static Stopwatch? _sessionTimer;
    private static bool _sessionActive;
    private static bool _firstProxyBlitRecorded;
    private static bool _workspaceEverHidden;
    private static ProbePhase _phase;
    private static int _sessionNumber;
    private static int _activeHitTest;
    private static int _gdiStart;
    private static int _timelineSequence;
    private static int _proxyVisibleTrueChanges;
    private static int _proxyVisibleFalseChanges;
    private static int _workspaceVisibleChanges;
    private static long _privateBytesStart;
    private static long _sessionStartTimestamp;

    internal static bool Enabled => ProbeEnabled;
    internal static bool ProxyDisabledForTest => DisableProxyForTest;

    internal static void BeginSession(int hitTest, Size initialSize)
    {
        if (!ProbeEnabled)
        {
            return;
        }

        _sessionActive = true;
        _phase = ProbePhase.None;
        _sessionNumber++;
        _activeHitTest = hitTest;
        Array.Clear(ShellPaint);
        Array.Clear(DeckPaint);
        Array.Clear(PreviewPaint);
        Array.Clear(ProxyPaint);
        Array.Clear(ProxyBackgroundPaint);
        Array.Clear(BodyLayout);
        Array.Clear(RootPaint);
        Array.Clear(WorkspacePaint);
        Array.Clear(FormBackgroundPaint);
        Array.Clear(SuppressedBodyPaint);
        DistinctSizes.Clear();
        EdgeLagSamples.Clear();
        Timeline.Clear();
        _firstProxyBlitRecorded = false;
        _workspaceEverHidden = false;
        _timelineSequence = 0;
        _proxyVisibleTrueChanges = 0;
        _proxyVisibleFalseChanges = 0;
        _workspaceVisibleChanges = 0;
        DistinctSizes.Add(initialSize);
        _gdiStart = GetCurrentGdiObjects();
        _privateBytesStart = GetPrivateBytes();
        _sessionStartTimestamp = Stopwatch.GetTimestamp();
        _sessionTimer = Stopwatch.StartNew();
        RecordEvent("EnterReceived");
    }

    internal static void BeginSnapshotCapture()
    {
        if (_sessionActive)
        {
            _phase = ProbePhase.Capture;
            RecordEvent("SnapshotCaptureBegin");
        }
    }

    internal static void EndSnapshotCapture()
    {
        if (_sessionActive && _phase == ProbePhase.Capture)
        {
            RecordEvent("SnapshotCaptureEnd");
            _phase = ProbePhase.None;
        }
    }

    internal static void BeginActiveResize()
    {
        if (_sessionActive)
        {
            _phase = ProbePhase.Resize;
            RecordEvent("ActiveReady");
        }
    }

    internal static void BeginRestore()
    {
        if (_sessionActive)
        {
            _phase = ProbePhase.Restore;
            RecordEvent("ExitBegin");
        }
    }

    internal static void RecordShellPaint()
    {
        if (_sessionActive)
        {
            ShellPaint[(int)_phase]++;
        }
    }

    internal static void RecordDeckPaint()
    {
        if (_sessionActive)
        {
            DeckPaint[(int)_phase]++;
        }
    }

    internal static void RecordPreviewPaint()
    {
        if (_sessionActive)
        {
            PreviewPaint[(int)_phase]++;
        }
    }

    internal static void RecordProxyPaint()
    {
        if (_sessionActive)
        {
            ProxyPaint[(int)_phase]++;
        }
    }

    internal static void RecordProxyBackgroundPaint()
    {
        if (_sessionActive)
        {
            ProxyBackgroundPaint[(int)_phase]++;
        }
    }

    internal static void RecordProxyFramePresented(bool hasSnapshot)
    {
        if (!_sessionActive || !hasSnapshot || _firstProxyBlitRecorded)
        {
            return;
        }

        _firstProxyBlitRecorded = true;
        RecordEvent("ProxyFirstBlitComplete");
    }

    internal static void RecordProxyVisibleChanged(bool visible)
    {
        if (!_sessionActive)
        {
            return;
        }

        if (visible)
        {
            _proxyVisibleTrueChanges++;
            RecordEvent("ProxyVisibleTrue");
        }
        else
        {
            _proxyVisibleFalseChanges++;
            RecordEvent("ProxyVisibleFalse");
        }
    }

    internal static void RecordBodyLayout()
    {
        if (_sessionActive)
        {
            BodyLayout[(int)_phase]++;
        }
    }

    internal static void RecordRootPaint()
    {
        if (_sessionActive)
        {
            RootPaint[(int)_phase]++;
        }
    }

    internal static void RecordWorkspacePaint()
    {
        if (_sessionActive)
        {
            WorkspacePaint[(int)_phase]++;
        }
    }

    internal static void RecordFormBackgroundPaint()
    {
        if (_sessionActive)
        {
            FormBackgroundPaint[(int)_phase]++;
        }
    }

    internal static void RecordWorkspaceVisibleChanged(bool visible)
    {
        if (!_sessionActive)
        {
            return;
        }

        _workspaceVisibleChanges++;
        _workspaceEverHidden |= !visible;
        RecordEvent(visible ? "WorkspaceVisibleTrue" : "WorkspaceVisibleFalse");
    }

    internal static void RecordSuppressedBodyPaint()
    {
        if (_sessionActive)
        {
            SuppressedBodyPaint[(int)_phase]++;
        }
    }

    internal static void RecordEvent(string eventName)
    {
        if (!_sessionActive)
        {
            return;
        }

        long timestamp = Stopwatch.GetTimestamp();
        double milliseconds = _sessionStartTimestamp == 0
            ? 0d
            : (timestamp - _sessionStartTimestamp) * 1000d / Stopwatch.Frequency;
        Timeline.Add(new TimelineEntry(
            ++_timelineSequence,
            eventName,
            _phase.ToString(),
            timestamp,
            Math.Round(milliseconds, 3)));
    }

    internal static void RecordSize(Size size, Rectangle windowBounds, Point cursor)
    {
        if (!_sessionActive)
        {
            return;
        }

        DistinctSizes.Add(size);
        double lag = CalculateEdgeLag(_activeHitTest, windowBounds, cursor);
        if (double.IsFinite(lag))
        {
            EdgeLagSamples.Add(lag);
        }
    }

    internal static void EndSession(
        string reason,
        bool proxyActivated,
        double snapshotMilliseconds,
        string? snapshotError,
        bool finalProxyVisible,
        bool finalWorkspaceVisible,
        bool finalRootLayoutSuspended,
        bool finalBodyLayoutSuspended,
        bool finalSnapshotPresent)
    {
        if (!ProbeEnabled || !_sessionActive)
        {
            return;
        }

        RecordEvent("ExitCleanupDone");
        _sessionTimer?.Stop();
        double[] lag = EdgeLagSamples.OrderBy(value => value).ToArray();
        double average = lag.Length == 0 ? 0d : lag.Average();
        double p95 = lag.Length == 0
            ? 0d
            : lag[Math.Clamp((int)Math.Ceiling(lag.Length * .95d) - 1, 0, lag.Length - 1)];
        double maximum = lag.Length == 0 ? 0d : lag[^1];
        int gdiEnd = GetCurrentGdiObjects();
        long privateBytesEnd = GetPrivateBytes();

        object result = new
        {
            Session = _sessionNumber,
            Reason = reason,
            ProxyActivated = proxyActivated,
            SnapshotError = snapshotError,
            GestureMs = Math.Round(_sessionTimer?.Elapsed.TotalMilliseconds ?? 0d, 3),
            SnapshotMs = Math.Round(snapshotMilliseconds, 3),
            DistinctSizes = DistinctSizes.Count,
            CaptureShellPaint = ShellPaint[(int)ProbePhase.Capture],
            CaptureDeckPaint = DeckPaint[(int)ProbePhase.Capture],
            CapturePreviewPaint = PreviewPaint[(int)ProbePhase.Capture],
            CaptureBodyLayout = BodyLayout[(int)ProbePhase.Capture],
            EntryProxyPaint = ProxyPaint[(int)ProbePhase.None],
            EntryProxyBackgroundPaint = ProxyBackgroundPaint[(int)ProbePhase.None],
            EntryRootPaint = RootPaint[(int)ProbePhase.None],
            EntryWorkspacePaint = WorkspacePaint[(int)ProbePhase.None],
            EntryFormBackgroundPaint = FormBackgroundPaint[(int)ProbePhase.None],
            ShellPaint = ShellPaint[(int)ProbePhase.Resize],
            DeckPaint = DeckPaint[(int)ProbePhase.Resize],
            PreviewPaint = PreviewPaint[(int)ProbePhase.Resize],
            ProxyPaint = ProxyPaint[(int)ProbePhase.Resize],
            ProxyBackgroundPaint = ProxyBackgroundPaint[(int)ProbePhase.Resize],
            BodyLayout = BodyLayout[(int)ProbePhase.Resize],
            RootPaint = RootPaint[(int)ProbePhase.Resize],
            WorkspacePaint = WorkspacePaint[(int)ProbePhase.Resize],
            FormBackgroundPaint = FormBackgroundPaint[(int)ProbePhase.Resize],
            SuppressedBodyPaint = SuppressedBodyPaint[(int)ProbePhase.Resize],
            RestoreShellPaint = ShellPaint[(int)ProbePhase.Restore],
            RestoreDeckPaint = DeckPaint[(int)ProbePhase.Restore],
            RestorePreviewPaint = PreviewPaint[(int)ProbePhase.Restore],
            RestoreProxyPaint = ProxyPaint[(int)ProbePhase.Restore],
            RestoreProxyBackgroundPaint = ProxyBackgroundPaint[(int)ProbePhase.Restore],
            RestoreBodyLayout = BodyLayout[(int)ProbePhase.Restore],
            RestoreRootPaint = RootPaint[(int)ProbePhase.Restore],
            RestoreWorkspacePaint = WorkspacePaint[(int)ProbePhase.Restore],
            RestoreFormBackgroundPaint = FormBackgroundPaint[(int)ProbePhase.Restore],
            RestoreSuppressedBodyPaint = SuppressedBodyPaint[(int)ProbePhase.Restore],
            ProxyVisibleTrueChanges = _proxyVisibleTrueChanges,
            ProxyVisibleFalseChanges = _proxyVisibleFalseChanges,
            WorkspaceVisibleChanges = _workspaceVisibleChanges,
            WorkspaceEverHidden = _workspaceEverHidden,
            FirstProxyBlitCompleted = _firstProxyBlitRecorded,
            FinalProxyVisible = finalProxyVisible,
            FinalWorkspaceVisible = finalWorkspaceVisible,
            FinalRootLayoutSuspended = finalRootLayoutSuspended,
            FinalBodyLayoutSuspended = finalBodyLayoutSuspended,
            FinalSnapshotPresent = finalSnapshotPresent,
            EdgeLagSamples = lag.Length,
            EdgeLagAverage = Math.Round(average, 3),
            EdgeLagP95 = Math.Round(p95, 3),
            EdgeLagMax = Math.Round(maximum, 3),
            GdiStart = _gdiStart,
            GdiEnd = gdiEnd,
            GdiDelta = gdiEnd - _gdiStart,
            PrivateBytesStart = _privateBytesStart,
            PrivateBytesEnd = privateBytesEnd,
            PrivateBytesDelta = privateBytesEnd - _privateBytesStart,
            Timeline = Timeline.ToArray(),
        };

        TryAppend(JsonSerializer.Serialize(result));
        _sessionActive = false;
        _phase = ProbePhase.None;
        _sessionTimer = null;
        DistinctSizes.Clear();
        EdgeLagSamples.Clear();
        Timeline.Clear();
    }

    private static double CalculateEdgeLag(int hitTest, Rectangle bounds, Point cursor)
    {
        double horizontal = hitTest is 10 or 13 or 16
            ? Math.Abs(cursor.X - bounds.Left)
            : hitTest is 11 or 14 or 17
                ? Math.Abs(cursor.X - bounds.Right)
                : double.NaN;
        double vertical = hitTest is 12 or 13 or 14
            ? Math.Abs(cursor.Y - bounds.Top)
            : hitTest is 15 or 16 or 17
                ? Math.Abs(cursor.Y - bounds.Bottom)
                : double.NaN;

        if (double.IsNaN(horizontal))
        {
            return vertical;
        }
        if (double.IsNaN(vertical))
        {
            return horizontal;
        }
        return Math.Sqrt(horizontal * horizontal + vertical * vertical);
    }

    private static int GetCurrentGdiObjects()
    {
        try
        {
            using Process process = Process.GetCurrentProcess();
            return GetGuiResources(process.Handle, GrGdiObjects);
        }
        catch
        {
            return -1;
        }
    }

    private static long GetPrivateBytes()
    {
        try
        {
            using Process process = Process.GetCurrentProcess();
            return process.PrivateMemorySize64;
        }
        catch
        {
            return -1;
        }
    }

    private static void TryAppend(string line)
    {
        if (string.IsNullOrWhiteSpace(OutputPath))
        {
            return;
        }

        try
        {
            string? directory = Path.GetDirectoryName(OutputPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }
            File.AppendAllText(OutputPath, line + Environment.NewLine);
        }
        catch
        {
            // Diagnostics must never interfere with resize cleanup.
        }
    }

    [DllImport("user32.dll")]
    private static extern int GetGuiResources(IntPtr process, int flags);
}
