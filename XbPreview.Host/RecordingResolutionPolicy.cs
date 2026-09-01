namespace XbPreview.Host;

internal readonly record struct ContentViewport(
    double X,
    double Y,
    double Width,
    double Height)
{
    internal bool IsValid =>
        double.IsFinite(X) && double.IsFinite(Y) &&
        double.IsFinite(Width) && double.IsFinite(Height) &&
        X >= 0.0 && Y >= 0.0 && Width > 0.0 && Height > 0.0;
}

internal readonly record struct RecordingResolutionPlan(
    RecordingResolutionMode Mode,
    SessionGeometry Geometry,
    ContentViewport ContentViewport,
    bool UpscalesSource);

/// <summary>
/// The sole managed policy that turns the persisted user intent into final
/// OutputCanvas pixels. Native receives only the existing geometry ABI.
/// </summary>
internal static class RecordingResolutionPolicy
{
    internal static RecordingResolutionMode Normalize(
        RecordingResolutionMode mode) => Enum.IsDefined(mode)
            ? mode
            : RecordingResolutionMode.Original;

    internal static RecordingResolutionPlan CreatePlan(
        RecordingResolutionMode mode,
        SessionGeometry currentCompositionGeometry)
    {
        ArgumentNullException.ThrowIfNull(currentCompositionGeometry);
        RecordingResolutionMode normalized = Normalize(mode);
        CaptureRegion capture = currentCompositionGeometry.CaptureRegion;
        OutputCanvas output = normalized switch
        {
            RecordingResolutionMode.Fhd1080 =>
                OutputCanvas.CreateExplicit(1920, 1080),
            RecordingResolutionMode.Qhd1440 =>
                OutputCanvas.CreateExplicit(2560, 1440),
            RecordingResolutionMode.Uhd2160 =>
                OutputCanvas.CreateExplicit(3840, 2160),
            _ => OutputCanvas.CreateIdentity(capture),
        };
        SessionGeometry geometry = SessionGeometry.Create(
            currentCompositionGeometry.CaptureDisplay,
            capture,
            output);
        ContentViewport viewport = CalculateContainViewport(
            capture.Width,
            capture.Height,
            output.Width,
            output.Height);
        return new RecordingResolutionPlan(
            normalized,
            geometry,
            viewport,
            IsUpscaling(
                capture.Width,
                capture.Height,
                viewport.Width,
                viewport.Height));
    }

    internal static ContentViewport CalculateContainViewport(
        int sourceWidth,
        int sourceHeight,
        int outputWidth,
        int outputHeight,
        double maximumOutputFraction = 1.0)
    {
        if (sourceWidth <= 0 || sourceHeight <= 0 ||
            outputWidth <= 0 || outputHeight <= 0 ||
            !double.IsFinite(maximumOutputFraction) ||
            maximumOutputFraction <= 0.0 || maximumOutputFraction > 1.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sourceWidth),
                "Contain geometry requires positive pixel dimensions and a " +
                "maximum fraction in (0, 1].");
        }

        double availableWidth = outputWidth * maximumOutputFraction;
        double availableHeight = outputHeight * maximumOutputFraction;
        double scale = Math.Min(
            availableWidth / sourceWidth,
            availableHeight / sourceHeight);
        double width = sourceWidth * scale;
        double height = sourceHeight * scale;
        return new ContentViewport(
            (outputWidth - width) * 0.5,
            (outputHeight - height) * 0.5,
            width,
            height);
    }

    internal static bool IsUpscaling(
        int sourceWidth,
        int sourceHeight,
        double viewportWidth,
        double viewportHeight)
    {
        if (sourceWidth <= 0 || sourceHeight <= 0 ||
            viewportWidth <= 0.0 || viewportHeight <= 0.0)
        {
            return false;
        }
        double scale = Math.Min(
            viewportWidth / sourceWidth,
            viewportHeight / sourceHeight);
        return scale > 1.000_001;
    }
}

internal readonly record struct RecordingResolutionChangeResult(
    bool Succeeded,
    string Error)
{
    internal static RecordingResolutionChangeResult Success() =>
        new(true, string.Empty);

    internal static RecordingResolutionChangeResult Failed(string error) =>
        new(false, error ?? string.Empty);
}

internal interface IRecordingResolutionCommands
{
    RecordingResolutionMode CurrentMode { get; }
    bool CurrentSelectionUpscales { get; }
    Task<RecordingResolutionChangeResult> SetResolutionAsync(
        RecordingResolutionMode mode);
}

/// <summary>
/// Applies an idle-only resolution selection through the mature preview
/// geometry transaction and persists it only after the new Preview is live.
/// </summary>
internal sealed class RecordingResolutionCoordinator :
    IRecordingResolutionCommands,
    IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly PreviewLifecycleController _lifecycle;
    private readonly ProductState _productState;
    private readonly RecordingController _recordingController;
    private bool _disposed;

    internal RecordingResolutionCoordinator(
        PreviewLifecycleController lifecycle,
        ProductState productState,
        RecordingController recordingController)
    {
        _lifecycle = lifecycle ??
            throw new ArgumentNullException(nameof(lifecycle));
        _productState = productState ??
            throw new ArgumentNullException(nameof(productState));
        _recordingController = recordingController ??
            throw new ArgumentNullException(nameof(recordingController));
    }

    public RecordingResolutionMode CurrentMode =>
        RecordingResolutionPolicy.Normalize(
            _productState.Current.RecordingResolutionMode);

    public bool CurrentSelectionUpscales
    {
        get
        {
            RecordingResolutionMode mode = CurrentMode;
            if (mode == RecordingResolutionMode.Original)
            {
                return false;
            }
            SessionGeometry? current =
                _lifecycle.CurrentGeometry ?? _lifecycle.DesiredGeometry;
            if (current is null)
            {
                return false;
            }
            int sourceWidth = current.CaptureRegion.Width;
            int sourceHeight = current.CaptureRegion.Height;
            if (_lifecycle.TryReadStats(
                out NativeMethods.PreviewStats stats,
                    out _,
                    out _) &&
                stats.CaptureWidth is > 0 and <= int.MaxValue &&
                stats.CaptureHeight is > 0 and <= int.MaxValue)
            {
                sourceWidth = (int)stats.CaptureWidth;
                sourceHeight = (int)stats.CaptureHeight;
            }
            ContentViewport viewport =
                RecordingResolutionPolicy.CalculateContainViewport(
                    sourceWidth,
                    sourceHeight,
                    current.OutputCanvas.Width,
                    current.OutputCanvas.Height);
            return RecordingResolutionPolicy.IsUpscaling(
                sourceWidth,
                sourceHeight,
                viewport.Width,
                viewport.Height);
        }
    }

    public async Task<RecordingResolutionChangeResult> SetResolutionAsync(
        RecordingResolutionMode mode)
    {
        RecordingResolutionMode selected =
            RecordingResolutionPolicy.Normalize(mode);
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_recordingController.CurrentSnapshot.IsActive)
            {
                return RecordingResolutionChangeResult.Failed(
                    "录制进行中不能切换输出分辨率。");
            }
            SessionGeometry? current =
                _lifecycle.CurrentGeometry ?? _lifecycle.DesiredGeometry;
            if (current is null)
            {
                return RecordingResolutionChangeResult.Failed(
                    "当前输出画布尚未就绪。");
            }

            ProductSettings previous = _productState.Current;
            RecordingResolutionPlan candidate =
                RecordingResolutionPolicy.CreatePlan(selected, current);
            PreviewLifecycleResult applied =
                await _lifecycle.ReconfigureGeometryAsync(
                    candidate.Geometry,
                    _lifecycle.CurrentRuntimeSettings).ConfigureAwait(false);
            if (!applied.Succeeded)
            {
                return RecordingResolutionChangeResult.Failed(
                    $"分辨率切换失败：{applied.Error}");
            }

            try
            {
                _productState.Set(previous with
                {
                    RecordingResolutionMode = selected,
                });
                _productState.Persist();
                return RecordingResolutionChangeResult.Success();
            }
            catch (Exception error)
            {
                _productState.Set(previous);
                RecordingResolutionPlan rollback =
                    RecordingResolutionPolicy.CreatePlan(
                        previous.RecordingResolutionMode,
                        current);
                PreviewLifecycleResult restored =
                    await _lifecycle.ReconfigureGeometryAsync(
                        rollback.Geometry,
                        _lifecycle.CurrentRuntimeSettings).ConfigureAwait(false);
                string rollbackDetail = restored.Succeeded
                    ? string.Empty
                    : $"；恢复原分辨率失败：{restored.Error}";
                return RecordingResolutionChangeResult.Failed(
                    $"分辨率设置持久化失败：{error.Message}{rollbackDetail}");
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        _disposed = true;
    }
}
