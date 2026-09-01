namespace XbPreview.Host;

internal readonly record struct CameraFollowSmoothResult(
    CameraPoint Center,
    double VelocityX,
    double VelocityY,
    bool ClampX,
    bool ClampY);

internal sealed class CameraFollowSmoother
{
    private double _velocityX;
    private double _velocityY;

    internal double VelocityX => _velocityX;

    internal double VelocityY => _velocityY;

    internal CameraFollowSmoothResult Step(
        double zoom,
        CameraPoint current,
        CameraPoint target,
        bool activeX,
        bool activeY,
        double deltaSeconds)
    {
        if (!CameraMath.IsFinite(zoom) ||
            !CameraMath.IsFinite(current.X) ||
            !CameraMath.IsFinite(current.Y) ||
            !CameraMath.IsFinite(target.X) ||
            !CameraMath.IsFinite(target.Y))
        {
            Reset();
            CameraView fallback = CameraMath.ClampView(
                CameraSettings.WideZoom,
                0.5,
                0.5);
            return new CameraFollowSmoothResult(
                new CameraPoint(fallback.CenterX, fallback.CenterY),
                0.0,
                0.0,
                fallback.ClampX,
                fallback.ClampY);
        }

        double nextX = current.X;
        double nextY = current.Y;
        if (activeX)
        {
            AdvanceAxis(ref nextX, ref _velocityX, target.X, deltaSeconds);
        }
        else
        {
            _velocityX = 0.0;
        }
        if (activeY)
        {
            AdvanceAxis(ref nextY, ref _velocityY, target.Y, deltaSeconds);
        }
        else
        {
            _velocityY = 0.0;
        }

        CameraView clamped = CameraMath.ClampView(zoom, nextX, nextY);
        if (Math.Abs(clamped.CenterX - nextX) > 1e-12)
        {
            _velocityX = 0.0;
        }
        if (Math.Abs(clamped.CenterY - nextY) > 1e-12)
        {
            _velocityY = 0.0;
        }
        return new CameraFollowSmoothResult(
            new CameraPoint(clamped.CenterX, clamped.CenterY),
            _velocityX,
            _velocityY,
            clamped.ClampX,
            clamped.ClampY);
    }

    internal void Reset()
    {
        _velocityX = 0.0;
        _velocityY = 0.0;
    }

    private static void AdvanceAxis(
        ref double value,
        ref double velocity,
        double target,
        double deltaSeconds)
    {
        CameraMath.AdvanceCriticalDamped(
            ref value,
            ref velocity,
            target,
            ComfortZoneSettings.AngularFrequency,
            deltaSeconds,
            ComfortZoneSettings.MaximumDeltaSeconds,
            ComfortZoneSettings.StopPositionEpsilon,
            ComfortZoneSettings.StopVelocityEpsilon);
    }
}
