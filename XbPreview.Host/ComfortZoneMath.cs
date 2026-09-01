namespace XbPreview.Host;

internal readonly record struct ComfortZoneBounds(
    double Left,
    double Right,
    double Top,
    double Bottom);

internal readonly record struct ComfortZoneCalculation(
    bool IsValid,
    bool FollowAllowed,
    CameraPoint DesiredCenter,
    ComfortZoneBounds Bounds,
    bool OutsideLeft,
    bool OutsideRight,
    bool OutsideTop,
    bool OutsideBottom,
    bool FollowActiveX,
    bool FollowActiveY,
    bool ClampX,
    bool ClampY);

internal static class ComfortZoneMath
{
    internal static ComfortZoneCalculation Calculate(
        double zoom,
        CameraPoint currentCenter,
        CameraPoint cursor,
        double widthRatio = ComfortZoneSettings.WidthRatio,
        double heightRatio = ComfortZoneSettings.HeightRatio)
    {
        if (!CameraMath.IsFinite(zoom) ||
            !CameraMath.IsFinite(currentCenter.X) ||
            !CameraMath.IsFinite(currentCenter.Y) ||
            !CameraMath.IsFinite(cursor.X) ||
            !CameraMath.IsFinite(cursor.Y) ||
            !CameraMath.IsFinite(widthRatio) ||
            !CameraMath.IsFinite(heightRatio) ||
            widthRatio is < 0.0 or > 1.0 ||
            heightRatio is < 0.0 or > 1.0)
        {
            return Invalid(currentCenter);
        }

        CameraView current = CameraMath.ClampView(
            zoom,
            currentCenter.X,
            currentCenter.Y);
        if (zoom <= CameraSettings.WideZoom + ComfortZoneSettings.BoundaryEpsilon)
        {
            return new ComfortZoneCalculation(
                true,
                false,
                new CameraPoint(0.5, 0.5),
                new ComfortZoneBounds(0.0, 1.0, 0.0, 1.0),
                false,
                false,
                false,
                false,
                false,
                false,
                current.ClampX,
                current.ClampY);
        }

        double viewWidth = 1.0 / current.Zoom;
        double viewHeight = 1.0 / current.Zoom;
        double halfWidth = viewWidth * widthRatio / 2.0;
        double halfHeight = viewHeight * heightRatio / 2.0;
        ComfortZoneBounds bounds = new(
            current.CenterX - halfWidth,
            current.CenterX + halfWidth,
            current.CenterY - halfHeight,
            current.CenterY + halfHeight);

        double epsilon = ComfortZoneSettings.BoundaryEpsilon;
        bool outsideLeft = cursor.X < bounds.Left - epsilon;
        bool outsideRight = cursor.X > bounds.Right + epsilon;
        bool outsideTop = cursor.Y < bounds.Top - epsilon;
        bool outsideBottom = cursor.Y > bounds.Bottom + epsilon;

        double desiredX = outsideLeft
            ? cursor.X + halfWidth
            : outsideRight
                ? cursor.X - halfWidth
                : current.CenterX;
        double desiredY = outsideTop
            ? cursor.Y + halfHeight
            : outsideBottom
                ? cursor.Y - halfHeight
                : current.CenterY;
        CameraView desired = CameraMath.ClampView(
            current.Zoom,
            desiredX,
            desiredY);

        return new ComfortZoneCalculation(
            true,
            true,
            new CameraPoint(desired.CenterX, desired.CenterY),
            bounds,
            outsideLeft,
            outsideRight,
            outsideTop,
            outsideBottom,
            outsideLeft || outsideRight,
            outsideTop || outsideBottom,
            desired.ClampX,
            desired.ClampY);
    }

    private static ComfortZoneCalculation Invalid(CameraPoint center)
    {
        double x = CameraMath.IsFinite(center.X)
            ? CameraMath.Clamp(center.X, 0.0, 1.0)
            : 0.5;
        double y = CameraMath.IsFinite(center.Y)
            ? CameraMath.Clamp(center.Y, 0.0, 1.0)
            : 0.5;
        return new ComfortZoneCalculation(
            false,
            false,
            new CameraPoint(x, y),
            new ComfortZoneBounds(x, x, y, y),
            false,
            false,
            false,
            false,
            false,
            false,
            false,
            false);
    }
}
