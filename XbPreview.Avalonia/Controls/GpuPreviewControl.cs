using Avalonia;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.Composition;
using Avalonia.Threading;
using Avalonia.VisualTree;
using XbPreview.Avalonia.Contracts;

namespace XbPreview.Avalonia.Controls;

public readonly record struct GpuPreviewPresentationDiagnostics(
    bool PresentationActive,
    bool OutstandingCompositionUpdate,
    int PendingPresentations,
    bool HasPresentationSource,
    bool IsVisible,
    ulong LastExportStreamGeneration,
    ulong LastExportGeneration,
    ulong LastCompletedStreamGeneration,
    ulong LastCompletedGeneration,
    ulong StreamTransitions,
    ulong CompositionRequests,
    ulong CompositionCallbacks,
    ulong CompletionCallbacks,
    bool ShutdownStarted);

public sealed class GpuPreviewControl : Control
{
    private sealed record ImportedImageEntry(
        ICompositionImportedGpuImage Image,
        ulong SharedHandle,
        uint Width,
        uint Height,
        uint Format,
        uint AdapterLuidLow,
        int AdapterLuidHigh);

    private const uint DxgiFormatB8G8R8A8Unorm = 87;
    private readonly Action _update;
    private readonly DispatcherTimer _presentationSizeTimer;
    private readonly Dictionary<
        (ulong Stream, ulong Generation, uint Slot),
        ImportedImageEntry> _imports = new();
    private readonly Dictionary<
        (ulong Stream, ulong Generation, uint Slot), Task>
        _presentTasks = new();
    private CompositionSurfaceVisual? _visual;
    private CompositionDrawingSurface? _surface;
    private ICompositionGpuInterop? _interop;
    private Compositor? _compositor;
    private bool _updateQueued;
    private bool _initializing;
    private bool _running;
    private Task? _shutdownTask;
    private ulong _lastSubmittedStreamGeneration;
    private ulong _lastSubmittedFrame;
    private ulong _lastPresentedStreamGeneration;
    private ulong _lastPresentedFrame;
    private ulong _streamTransitionCount;
    private ulong _compositionRequestCount;
    private ulong _compositionCallbackCount;
    private ulong _completionCallbackCount;
    private uint _pendingPresentationWidth;
    private uint _pendingPresentationHeight;
    private uint _reportedPresentationWidth;
    private uint _reportedPresentationHeight;

    public GpuPreviewControl()
    {
        _update = UpdateFrame;
        _presentationSizeTimer = new DispatcherTimer(
            TimeSpan.FromMilliseconds(120),
            DispatcherPriority.Background,
            OnPresentationSizeTimer);
        _presentationSizeTimer.Stop();
        ClipToBounds = true;

        // Route B's imported D3D11 image is vertically inverted only at the
        // UI presentation boundary. Production OutputCanvas stays unchanged.
        RenderTransform = new ScaleTransform(1, -1);
        RenderTransformOrigin = RelativePoint.Center;
    }

    public IGpuPreviewFrameSource? FrameSource { get; set; }

    public event EventHandler? StatusChanged;

    public string? StartupError { get; private set; }

    public string InteropStatus { get; private set; } = "INITIALIZING";

    public string DeviceCompatibility { get; private set; } = "UNKNOWN";

    public bool? AdapterLuidMatch { get; private set; }

    public ulong LastPresentedFrame => _lastPresentedFrame;

    public GpuPreviewPresentationDiagnostics PresentationDiagnostics => new(
        _running,
        _updateQueued,
        _presentTasks.Count,
        this.GetPresentationSource() is not null,
        IsVisible,
        _lastSubmittedStreamGeneration,
        _lastSubmittedFrame,
        _lastPresentedStreamGeneration,
        _lastPresentedFrame,
        _streamTransitionCount,
        _compositionRequestCount,
        _compositionCallbackCount,
        _completionCallbackCount,
        _shutdownTask is not null);

    protected override void OnAttachedToVisualTree(
        VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Initialize();
        QueuePresentationSizeUpdate();
    }

    protected override void OnDetachedFromLogicalTree(
        LogicalTreeAttachmentEventArgs e)
    {
        _presentationSizeTimer.Stop();
        _ = ShutdownAsync();
        base.OnDetachedFromLogicalTree(e);
    }

    protected override void OnPropertyChanged(
        AvaloniaPropertyChangedEventArgs change)
    {
        if (change.Property == BoundsProperty)
        {
            QueuePresentationSizeUpdate();
            QueueNextFrame();
        }
        base.OnPropertyChanged(change);
    }

    public Task ShutdownAsync() => _shutdownTask ??= ShutdownCoreAsync();

    private async Task ShutdownCoreAsync()
    {
        _running = false;
        _presentationSizeTimer.Stop();

        Task[] pending = _presentTasks.Values.ToArray();
        if (pending.Length > 0)
        {
            try
            {
                await Task.WhenAll(pending);
            }
            catch
            {
                // Imported resources still need best-effort cleanup.
            }
        }

        _surface?.Dispose();
        _surface = null;
        foreach (ImportedImageEntry entry in _imports.Values)
        {
            try
            {
                await entry.Image.DisposeAsync();
            }
            catch
            {
                // Renderer shutdown follows even if compositor cleanup fails.
            }
        }
        _imports.Clear();
        _presentTasks.Clear();
        _visual = null;
        _interop = null;
        _compositor = null;
    }

    private async void Initialize()
    {
        if (_running || _initializing || _shutdownTask is not null)
        {
            return;
        }

        _initializing = true;
        try
        {
            var selfVisual = ElementComposition.GetElementVisual(this) ??
                throw new InvalidOperationException(
                    "Avalonia element composition visual is unavailable.");
            _compositor = selfVisual.Compositor;
            _surface = _compositor.CreateDrawingSurface();
            _visual = _compositor.CreateSurfaceVisual();
            _visual.Size = new(Bounds.Width, Bounds.Height);
            _visual.Surface = _surface;
            ElementComposition.SetElementChildVisual(this, _visual);

            _interop = await _compositor.TryGetCompositionGpuInterop() ??
                throw new NotSupportedException(
                    "The current Avalonia backend has no GPU interop.");
            string handleType = KnownPlatformGraphicsExternalImageHandleTypes
                .D3D11TextureGlobalSharedHandle;
            if (!_interop.SupportedImageHandleTypes.Contains(handleType))
            {
                throw new NotSupportedException(
                    "D3D11 global shared-handle import is unsupported.");
            }
            CompositionGpuImportedImageSynchronizationCapabilities sync =
                _interop.GetSynchronizationCapabilities(handleType);
            if (!sync.HasFlag(
                CompositionGpuImportedImageSynchronizationCapabilities.KeyedMutex))
            {
                throw new NotSupportedException(
                    "D3D11 keyed-mutex synchronization is unsupported.");
            }

            InteropStatus = $"PASS; {handleType}; synchronization={sync}";
            _running = true;
            StatusChanged?.Invoke(this, EventArgs.Empty);
            QueueNextFrame();
        }
        catch (Exception error)
        {
            StartupError = error.ToString();
            InteropStatus = "FAIL";
            StatusChanged?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            _initializing = false;
        }
    }

    private void UpdateFrame()
    {
        ++_compositionCallbackCount;
        _updateQueued = false;
        if (!_running || _compositor is null || _surface is null ||
            _visual is null || _interop is null ||
            this.GetPresentationSource() is null)
        {
            return;
        }

        try
        {
            QueuePresentationSizeUpdate();
            GpuPreviewFrame? candidate =
                FrameSource?.TryGetLatestFrame(out GpuPreviewFrame frame) == true
                    ? frame
                    : null;
            UpdatePresentationGeometry(
                candidate?.Width ?? 0,
                candidate?.Height ?? 0);
            if (candidate is { } next &&
                (next.StreamGeneration != _lastSubmittedStreamGeneration ||
                    next.FrameGeneration > _lastSubmittedFrame))
            {
                if (next.Format != DxgiFormatB8G8R8A8Unorm)
                {
                    throw new InvalidOperationException(
                        $"Unsupported GPU preview DXGI format {next.Format}.");
                }

                string handleType =
                    KnownPlatformGraphicsExternalImageHandleTypes
                        .D3D11TextureGlobalSharedHandle;
                (ulong Stream, ulong Generation, uint Slot) key =
                    (next.StreamGeneration,
                        next.ResourceGeneration,
                        next.SlotIndex);
                if (!_imports.TryGetValue(key, out ImportedImageEntry? entry))
                {
                    ICompositionImportedGpuImage imported =
                        _interop.ImportImage(
                        new PlatformHandle(
                            unchecked((nint)next.SharedHandle),
                            handleType),
                        new PlatformGraphicsExternalImageProperties
                        {
                            Width = checked((int)next.Width),
                            Height = checked((int)next.Height),
                            Format = PlatformGraphicsExternalImageFormat
                                .B8G8R8A8UNorm,
                        });
                    entry = new ImportedImageEntry(
                        imported,
                        next.SharedHandle,
                        next.Width,
                        next.Height,
                        next.Format,
                        next.AdapterLuidLow,
                        next.AdapterLuidHigh);
                    _imports.Add(key, entry);
                }
                else if (entry.SharedHandle != next.SharedHandle ||
                    entry.Width != next.Width || entry.Height != next.Height ||
                    entry.Format != next.Format ||
                    entry.AdapterLuidLow != next.AdapterLuidLow ||
                    entry.AdapterLuidHigh != next.AdapterLuidHigh)
                {
                    throw new InvalidOperationException(
                        "GPU preview resource identity changed inside one " +
                        "stream/resource/slot key.");
                }

                DeviceCompatibility = DescribeDeviceCompatibility(next);
                string direct2DStatus =
                    "PASS; D3D11 global shared handle; keyed mutex; " +
                    "Microsoft Direct2D Scale Effect CLSID_D2D1Scale; " +
                    $"HIGH_QUALITY_CUBIC; target={next.Width}x{next.Height}";
                if (!string.Equals(
                        InteropStatus,
                        direct2DStatus,
                        StringComparison.Ordinal))
                {
                    InteropStatus = direct2DStatus;
                    StatusChanged?.Invoke(this, EventArgs.Empty);
                }
                if (next.StreamGeneration !=
                    _lastSubmittedStreamGeneration)
                {
                    if (_lastSubmittedStreamGeneration != 0)
                    {
                        ++_streamTransitionCount;
                    }
                    _lastSubmittedStreamGeneration = next.StreamGeneration;
                    _lastSubmittedFrame = 0;
                }
                _lastSubmittedFrame = next.FrameGeneration;
                Task present = _surface.UpdateWithKeyedMutexAsync(
                    entry.Image,
                    1,
                    0);
                _presentTasks[key] = present;
                _ = present.ContinueWith(
                    completed => Dispatcher.UIThread.Post(() =>
                        CompletePresent(
                            completed,
                            key,
                            next)),
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
            }
        }
        catch (Exception error)
        {
            StartupError = error.ToString();
            InteropStatus = "FAIL DURING COMPOSITION UPDATE";
            _running = false;
            StatusChanged?.Invoke(this, EventArgs.Empty);
        }

        QueueNextFrame();
    }

    private void QueuePresentationSizeUpdate()
    {
        if (_shutdownTask is not null)
        {
            return;
        }
        double renderScaling =
            this.GetPresentationSource()?.RenderScaling ?? 1.0;
        if (!double.IsFinite(renderScaling) || renderScaling <= 0 ||
            Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            return;
        }

        uint width = checked((uint)Math.Clamp(
            Math.Round(
                Bounds.Width * renderScaling,
                MidpointRounding.AwayFromZero),
            1,
            32768));
        uint height = checked((uint)Math.Clamp(
            Math.Round(
                Bounds.Height * renderScaling,
                MidpointRounding.AwayFromZero),
            1,
            32768));
        if ((width == _pendingPresentationWidth &&
                height == _pendingPresentationHeight) ||
            (width == _reportedPresentationWidth &&
                height == _reportedPresentationHeight))
        {
            return;
        }

        _pendingPresentationWidth = width;
        _pendingPresentationHeight = height;
        _presentationSizeTimer.Stop();
        _presentationSizeTimer.Start();
    }

    private void OnPresentationSizeTimer(object? sender, EventArgs e)
    {
        _presentationSizeTimer.Stop();
        if (_pendingPresentationWidth == 0 ||
            _pendingPresentationHeight == 0 ||
            FrameSource is null)
        {
            return;
        }
        if (!FrameSource.SetPresentationSize(
                _pendingPresentationWidth,
                _pendingPresentationHeight))
        {
            if (_shutdownTask is null)
            {
                StartupError = "The native Direct2D preview target rejected " +
                    $"{_pendingPresentationWidth}x" +
                    $"{_pendingPresentationHeight}.";
                InteropStatus = "FAIL DURING DIRECT2D TARGET RESIZE";
                StatusChanged?.Invoke(this, EventArgs.Empty);
            }
            return;
        }

        _reportedPresentationWidth = _pendingPresentationWidth;
        _reportedPresentationHeight = _pendingPresentationHeight;
    }

    private void UpdatePresentationGeometry(uint sourceWidth, uint sourceHeight)
    {
        if (_visual is null)
        {
            return;
        }

        double viewportWidth = Math.Max(0, Bounds.Width);
        double viewportHeight = Math.Max(0, Bounds.Height);
        if (sourceWidth == 0 || sourceHeight == 0 ||
            viewportWidth == 0 || viewportHeight == 0)
        {
            _visual.Offset = default;
            _visual.Size = new(viewportWidth, viewportHeight);
            return;
        }

        double scale = Math.Min(
            viewportWidth / sourceWidth,
            viewportHeight / sourceHeight);
        double width = sourceWidth * scale;
        double height = sourceHeight * scale;
        _visual.Offset = new System.Numerics.Vector3(
            (float)((viewportWidth - width) / 2),
            (float)((viewportHeight - height) / 2),
            0);
        _visual.Size = new(width, height);
    }

    private void CompletePresent(
        Task completed,
        (ulong Stream, ulong Generation, uint Slot) key,
        GpuPreviewFrame frame)
    {
        ++_completionCallbackCount;
        if (_presentTasks.TryGetValue(key, out Task? current) &&
            ReferenceEquals(current, completed))
        {
            _presentTasks.Remove(key);
        }
        bool currentStream =
            key.Stream == _lastSubmittedStreamGeneration &&
            FrameSource?.IsCurrentStream(key.Stream) == true;
        if (completed.IsFaulted)
        {
            if (!currentStream)
            {
                return;
            }
            StartupError = completed.Exception?.GetBaseException().ToString();
            InteropStatus = "FAIL DURING COMPOSITION UPDATE";
            StatusChanged?.Invoke(this, EventArgs.Empty);
            return;
        }
        if (!_running || !currentStream ||
            (_lastPresentedStreamGeneration == frame.StreamGeneration &&
                frame.FrameGeneration <= _lastPresentedFrame))
        {
            return;
        }

        _lastPresentedStreamGeneration = frame.StreamGeneration;
        _lastPresentedFrame = frame.FrameGeneration;
    }

    private string DescribeDeviceCompatibility(GpuPreviewFrame frame)
    {
        byte[]? compositorLuid = _interop?.DeviceLuid;
        if (compositorLuid is null || compositorLuid.Length < 8)
        {
            AdapterLuidMatch = null;
            return $"native LUID={frame.AdapterLuidHigh:X8}:" +
                $"{frame.AdapterLuidLow:X8}; compositor LUID unavailable; " +
                "successful import is the compatibility proof";
        }

        uint compositorLow = BitConverter.ToUInt32(compositorLuid, 0);
        int compositorHigh = BitConverter.ToInt32(compositorLuid, 4);
        AdapterLuidMatch = compositorLow == frame.AdapterLuidLow &&
            compositorHigh == frame.AdapterLuidHigh;
        return $"native LUID={frame.AdapterLuidHigh:X8}:" +
            $"{frame.AdapterLuidLow:X8}; compositor LUID=" +
            $"{compositorHigh:X8}:{compositorLow:X8}; " +
            $"match={AdapterLuidMatch}";
    }

    private void QueueNextFrame()
    {
        if (_running && !_updateQueued && _compositor is not null)
        {
            _updateQueued = true;
            ++_compositionRequestCount;
            _compositor.RequestCompositionUpdate(_update);
        }
    }
}
