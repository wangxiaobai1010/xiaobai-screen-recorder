using System.Text.Json;

namespace XbPreview.Host;

internal sealed class CameraDiagnosticLogger : IDisposable
{
    private readonly object _gate = new();
    private readonly StreamWriter _writer;
    private bool _disposed;

    internal CameraDiagnosticLogger(string directory)
    {
        Directory.CreateDirectory(directory);
        LogFilePath = Path.Combine(
            directory,
            $"p1a-camera-{DateTime.Now:yyyyMMdd-HHmmss-fff}.jsonl");
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
    }

    internal string LogFilePath { get; }

    internal void Write(
        CameraState state,
        NativeMethods.Result result,
        NativeMethods.PreviewStats? nativeStats = null,
        string? detail = null)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }
            _writer.WriteLine(JsonSerializer.Serialize(new
            {
                timestampUtc = DateTimeOffset.UtcNow,
                state.Sequence,
                state.TimestampQpc,
                state.Event,
                mode = state.Mode.ToString(),
                state.Enabled,
                state.Zoom,
                state.CenterX,
                state.CenterY,
                state.TargetX,
                state.TargetY,
                state.TransitionProgress,
                state.ElapsedSeconds,
                state.AnimationStartZoom,
                state.AnimationStartCenterX,
                state.AnimationStartCenterY,
                state.TransitionDurationSeconds,
                state.EasedProgress,
                state.ClampX,
                state.ClampY,
                state.IsValid,
                nativeResult = result.ToString(),
                nativeLastAppliedSequence = nativeStats?.NativeLastAppliedSequence,
                nativeAppliedZoom = nativeStats?.NativeAppliedZoom,
                nativeAppliedCenterX = nativeStats?.NativeAppliedCenterX,
                nativeAppliedCenterY = nativeStats?.NativeAppliedCenterY,
                invalidStateFallbackCount = nativeStats?.InvalidCameraStateFallbackCount,
                cameraUpdateCount = nativeStats?.CameraUpdateCount,
                cameraUpdateRate = nativeStats?.CameraUpdateRate,
                renderFrameCount = nativeStats?.PresentFrameCount,
                captureFps = nativeStats?.CaptureFps,
                presentFps = nativeStats?.PresentFps,
                p50LatencyMs = nativeStats?.P50LatencyMilliseconds,
                p95LatencyMs = nativeStats?.P95LatencyMilliseconds,
                framePoolRecreate = nativeStats?.FramePoolRecreateCount,
                swapChainResize = nativeStats?.SwapChainResizeCount,
                deviceRemoved = nativeStats?.DeviceRemovedReason,
                detail,
            }));
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            _writer.Dispose();
        }
    }
}
