namespace XbPreview.Avalonia.Contracts;

public readonly record struct GpuPreviewFrame(
    ulong StreamGeneration,
    ulong SharedHandle,
    uint Width,
    uint Height,
    uint Format,
    uint SlotIndex,
    ulong ResourceGeneration,
    ulong FrameGeneration,
    ulong SkippedFrameCount,
    uint AdapterLuidLow,
    int AdapterLuidHigh,
    ulong RendererGeneration);

public interface IGpuPreviewFrameSource
{
    bool SetPresentationSize(uint pixelWidth, uint pixelHeight);

    bool TryGetLatestFrame(out GpuPreviewFrame frame);

    bool IsCurrentStream(ulong streamGeneration);
}
