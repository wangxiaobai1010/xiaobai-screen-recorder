using XbPreview.Avalonia.Contracts;

namespace XbPreview.Host;

// Thin managed adapter over the NativePreviewSession owned by the existing
// PreviewLifecycleController. It owns neither the session nor GPU resources.
internal sealed class GpuPreviewFrameSource : IGpuPreviewFrameSource
{
    private NativePreviewSession? _session;
    private long _presentationSize;

    internal void Attach(NativePreviewSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (Interlocked.CompareExchange(ref _session, session, null) is not null)
        {
            throw new InvalidOperationException(
                "A GPU preview native session is already attached.");
        }

        ulong packed = unchecked((ulong)Volatile.Read(ref _presentationSize));
        if (packed != 0 && session.SetGpuExportTargetSize(
                checked((int)(packed & uint.MaxValue)),
                checked((int)(packed >> 32))) != NativeMethods.Result.Ok)
        {
            Interlocked.Exchange(ref _session, null);
            throw new InvalidOperationException(
                "The Direct2D GPU preview target size was rejected.");
        }
    }

    internal void Detach()
    {
        Interlocked.Exchange(ref _session, null);
    }

    public bool SetPresentationSize(uint pixelWidth, uint pixelHeight)
    {
        if (pixelWidth == 0 || pixelHeight == 0 ||
            pixelWidth > 32768 || pixelHeight > 32768)
        {
            return false;
        }

        ulong packed = pixelWidth | ((ulong)pixelHeight << 32);
        Interlocked.Exchange(ref _presentationSize, unchecked((long)packed));
        NativePreviewSession? session = Volatile.Read(ref _session);
        if (session is null)
        {
            return true;
        }
        try
        {
            return session.SetGpuExportTargetSize(
                checked((int)pixelWidth),
                checked((int)pixelHeight)) == NativeMethods.Result.Ok;
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
    }

    public bool TryGetLatestFrame(out GpuPreviewFrame frame)
    {
        NativePreviewSession? session = Volatile.Read(ref _session);
        if (session is null ||
            !session.TryBeginGpuFrameRead(
                out NativeGpuStreamReadStamp stream))
        {
            frame = default;
            return false;
        }

        try
        {
            if (!session.TryGetGpuExportFrame(
                    out NativeMethods.GpuExportFrameV1 native) ||
                !session.IsGpuFrameReadCurrent(stream))
            {
                frame = default;
                return false;
            }

            frame = Map(stream.StreamGeneration, native);
            return true;
        }
        catch (ObjectDisposedException)
        {
            frame = default;
            return false;
        }
    }

    public bool IsCurrentStream(ulong streamGeneration)
    {
        NativePreviewSession? session = Volatile.Read(ref _session);
        return session is not null &&
            session.IsGpuStreamCurrent(streamGeneration);
    }

    private static GpuPreviewFrame Map(
        ulong streamGeneration,
        NativeMethods.GpuExportFrameV1 frame) => new(
            streamGeneration,
            frame.SharedHandle,
            frame.Width,
            frame.Height,
            frame.Format,
            frame.SlotIndex,
            frame.ResourceGeneration,
            frame.FrameGeneration,
            frame.SkippedFrameCount,
            frame.AdapterLuidLow,
            frame.AdapterLuidHigh,
            frame.RendererGeneration);
}
