using System.Text.Json;
using System.Threading.Channels;

namespace XbPreview.Host;

internal sealed class ComfortZoneDiagnosticLogger : IDisposable
{
    private readonly Channel<FollowLogEntry> _channel;
    private readonly StreamWriter _writer;
    private readonly Task _writerTask;
    private int _disposed;
    private long _queueDropCount;
    private string? _backgroundError;

    internal ComfortZoneDiagnosticLogger(string directory)
    {
        Directory.CreateDirectory(directory);
        LogFilePath = Path.Combine(
            directory,
            $"p1b-follow-{DateTime.Now:yyyyMMdd-HHmmss-fff}.jsonl");
        _writer = new StreamWriter(
            new FileStream(
                LogFilePath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.Read),
            new System.Text.UTF8Encoding(false))
        {
            AutoFlush = true,
        };
        _channel = Channel.CreateBounded<FollowLogEntry>(
            new BoundedChannelOptions(8192)
            {
                SingleReader = true,
                SingleWriter = true,
                FullMode = BoundedChannelFullMode.Wait,
            });
        _writerTask = Task.Run(WriteLoopAsync);
    }

    internal string LogFilePath { get; }

    internal long QueueDropCount =>
        Interlocked.Read(ref _queueDropCount);

    internal string? BackgroundError =>
        Volatile.Read(ref _backgroundError);

    internal bool TryWrite(
        CameraState cameraState,
        ComfortZoneFollowStep follow,
        NativeMethods.Result nativeResult,
        NativeMethods.PreviewStats? nativeStats,
        string? detail)
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return false;
        }

        FollowLogEntry entry = new(
            DateTimeOffset.UtcNow,
            cameraState,
            follow,
            nativeResult,
            nativeStats,
            detail);
        if (_channel.Writer.TryWrite(entry))
        {
            return true;
        }

        Interlocked.Increment(ref _queueDropCount);
        return false;
    }

    private async Task WriteLoopAsync()
    {
        try
        {
            await foreach (FollowLogEntry entry in
                _channel.Reader.ReadAllAsync().ConfigureAwait(false))
            {
                CameraCursorObservation? cursor = entry.Follow.Cursor;
                NativeMethods.PreviewStats? stats = entry.NativeStats;
                string json = JsonSerializer.Serialize(new
                {
                    timestamp = entry.Timestamp,
                    cameraSequence = entry.CameraState.Sequence,
                    cameraMode = entry.CameraState.Mode.ToString(),
                    followState = entry.Follow.State.ToString(),
                    followEnabled = entry.Follow.FollowEnabled,
                    cursorScreenX = cursor?.ScreenX,
                    cursorScreenY = cursor?.ScreenY,
                    cursorNormalizedX = cursor?.NormalizedX,
                    cursorNormalizedY = cursor?.NormalizedY,
                    cursorInsideMonitor = cursor?.InsidePrimaryMonitor,
                    currentCenterX = entry.Follow.CurrentCenter.X,
                    currentCenterY = entry.Follow.CurrentCenter.Y,
                    desiredCenterX = entry.Follow.DesiredCenter.X,
                    desiredCenterY = entry.Follow.DesiredCenter.Y,
                    outputCenterX = entry.Follow.OutputCenter.X,
                    outputCenterY = entry.Follow.OutputCenter.Y,
                    comfortLeft = entry.Follow.Bounds.Left,
                    comfortRight = entry.Follow.Bounds.Right,
                    comfortTop = entry.Follow.Bounds.Top,
                    comfortBottom = entry.Follow.Bounds.Bottom,
                    outsideLeft = entry.Follow.OutsideLeft,
                    outsideRight = entry.Follow.OutsideRight,
                    outsideTop = entry.Follow.OutsideTop,
                    outsideBottom = entry.Follow.OutsideBottom,
                    followActiveX = entry.Follow.FollowActiveX,
                    followActiveY = entry.Follow.FollowActiveY,
                    velocityX = entry.Follow.VelocityX,
                    velocityY = entry.Follow.VelocityY,
                    deltaSeconds = entry.Follow.DeltaSeconds,
                    clampX = entry.Follow.ClampX,
                    clampY = entry.Follow.ClampY,
                    followErrorCount = entry.Follow.FollowErrorCount,
                    getCursorPosResult = cursor?.GetCursorPosResult,
                    getCursorPosLastError = cursor?.LastError,
                    followEvent = entry.Follow.Event,
                    nativeResult = entry.NativeResult.ToString(),
                    nativeLastAppliedSequence = stats?.NativeLastAppliedSequence,
                    appliedZoom = stats?.NativeAppliedZoom,
                    appliedCenterX = stats?.NativeAppliedCenterX,
                    appliedCenterY = stats?.NativeAppliedCenterY,
                    captureFps = stats?.CaptureFps,
                    presentFps = stats?.PresentFps,
                    latencyP50 = stats?.P50LatencyMilliseconds,
                    latencyP95 = stats?.P95LatencyMilliseconds,
                    dropped = stats?.DroppedFrameCount,
                    framePoolRecreate = stats?.FramePoolRecreateCount,
                    swapChainResize = stats?.SwapChainResizeCount,
                    logQueueDropCount = QueueDropCount,
                    error = entry.Follow.Error ?? entry.Detail,
                });
                await _writer.WriteLineAsync(json).ConfigureAwait(false);
            }
        }
        catch (Exception error)
        {
            Volatile.Write(ref _backgroundError, error.Message);
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }
        _channel.Writer.TryComplete();
        try
        {
            _writerTask.GetAwaiter().GetResult();
        }
        finally
        {
            _writer.Dispose();
        }
    }

    private readonly record struct FollowLogEntry(
        DateTimeOffset Timestamp,
        CameraState CameraState,
        ComfortZoneFollowStep Follow,
        NativeMethods.Result NativeResult,
        NativeMethods.PreviewStats? NativeStats,
        string? Detail);
}
