namespace XbPreview.Host;

internal readonly record struct CameraPoint(double X, double Y);

internal readonly record struct CameraView(
    double Zoom,
    double CenterX,
    double CenterY,
    bool ClampX,
    bool ClampY);

internal readonly record struct CameraUv(
    double Left,
    double Top,
    double Width,
    double Height);

internal static class CameraMath
{
    internal static double Clamp(double value, double minimum, double maximum) =>
        Math.Min(maximum, Math.Max(minimum, value));

    internal static double SmoothStep(double progress)
    {
        double t = Clamp(progress, 0.0, 1.0);
        return t * t * (3.0 - (2.0 * t));
    }

    internal static double Lerp(double start, double end, double progress) =>
        start + ((end - start) * progress);

    internal static bool AdvanceCriticalDamped(
        ref double value,
        ref double velocity,
        double target,
        double angularFrequency,
        double deltaSeconds,
        double maximumDeltaSeconds,
        double stopPositionEpsilon,
        double stopVelocityEpsilon)
    {
        if (!IsFinite(value) ||
            !IsFinite(velocity) ||
            !IsFinite(target) ||
            !IsFinite(angularFrequency) ||
            angularFrequency <= 0.0)
        {
            value = IsFinite(target) ? target : 0.5;
            velocity = 0.0;
            return true;
        }
        if (!IsFinite(deltaSeconds) || deltaSeconds <= 0.0)
        {
            return Math.Abs(value - target) <= stopPositionEpsilon &&
                Math.Abs(velocity) <= stopVelocityEpsilon;
        }

        double delta = Math.Min(deltaSeconds, maximumDeltaSeconds);
        double offset = value - target;
        double decay = Math.Exp(-angularFrequency * delta);
        double temp = (velocity + (angularFrequency * offset)) * delta;
        double nextOffset = (offset + temp) * decay;
        double nextVelocity =
            (velocity - (angularFrequency * temp)) * decay;
        double nextValue = target + nextOffset;

        if (!IsFinite(nextValue) || !IsFinite(nextVelocity))
        {
            value = target;
            velocity = 0.0;
            return true;
        }

        double direction = target - value;
        bool crossed =
            (direction > 0.0 && nextValue > target) ||
            (direction < 0.0 && nextValue < target) ||
            (direction == 0.0 && nextValue != target);
        if (crossed ||
            (Math.Abs(nextValue - target) <= stopPositionEpsilon &&
             Math.Abs(nextVelocity) <= stopVelocityEpsilon))
        {
            value = target;
            velocity = 0.0;
            return true;
        }

        value = nextValue;
        velocity = nextVelocity;
        return false;
    }

    internal static CameraPoint NormalizeCursor(
        int cursorX,
        int cursorY,
        int monitorLeft,
        int monitorTop,
        int captureWidth,
        int captureHeight)
    {
        if (captureWidth <= 0 || captureHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(captureWidth),
                "Capture dimensions must be positive.");
        }

        return new CameraPoint(
            Clamp((cursorX - monitorLeft) / (double)captureWidth, 0.0, 1.0),
            Clamp((cursorY - monitorTop) / (double)captureHeight, 0.0, 1.0));
    }

    internal static CameraView ClampView(
        double zoom,
        double requestedCenterX,
        double requestedCenterY)
    {
        double safeZoom = Clamp(
            FiniteOr(zoom, CameraSettings.WideZoom),
            CameraSettings.WideZoom,
            CameraSettings.MaxSupportedZoom);
        if (safeZoom <= CameraSettings.WideZoom + 1e-9)
        {
            return new CameraView(CameraSettings.WideZoom, 0.5, 0.5, false, false);
        }

        double halfView = 0.5 / safeZoom;
        double rawX = FiniteOr(requestedCenterX, 0.5);
        double rawY = FiniteOr(requestedCenterY, 0.5);
        double centerX = Clamp(rawX, halfView, 1.0 - halfView);
        double centerY = Clamp(rawY, halfView, 1.0 - halfView);
        return new CameraView(
            safeZoom,
            centerX,
            centerY,
            Math.Abs(centerX - rawX) > 1e-12,
            Math.Abs(centerY - rawY) > 1e-12);
    }

    internal static CameraUv ToUv(CameraView view)
    {
        CameraView safe = ClampView(view.Zoom, view.CenterX, view.CenterY);
        double size = 1.0 / safe.Zoom;
        return new CameraUv(
            safe.CenterX - (size / 2.0),
            safe.CenterY - (size / 2.0),
            size,
            size);
    }

    internal static bool IsFinite(double value) =>
        !double.IsNaN(value) && !double.IsInfinity(value);

    internal static bool IsValidState(CameraState state) =>
        IsFinite(state.Zoom) &&
        IsFinite(state.CenterX) &&
        IsFinite(state.CenterY) &&
        IsFinite(state.TargetX) &&
        IsFinite(state.TargetY) &&
        IsFinite(state.TransitionProgress) &&
        state.Zoom >= CameraSettings.WideZoom &&
        state.Zoom <= CameraSettings.MaxSupportedZoom &&
        state.CenterX is >= 0.0 and <= 1.0 &&
        state.CenterY is >= 0.0 and <= 1.0 &&
        state.TargetX is >= 0.0 and <= 1.0 &&
        state.TargetY is >= 0.0 and <= 1.0 &&
        state.TransitionProgress is >= 0.0 and <= 1.0;

    private static double FiniteOr(double value, double fallback) =>
        IsFinite(value) ? value : fallback;
}
