namespace XbPreview.Host;

internal enum CameraMode
{
    Wide = 0,
    ZoomingIn = 1,
    ZoomedFixed = 2,
    ZoomingOut = 3,
    ErrorFallback = 4,
}

internal readonly record struct CameraState(
    ulong Sequence,
    long TimestampQpc,
    bool Enabled,
    CameraMode Mode,
    double Zoom,
    double CenterX,
    double CenterY,
    double TransitionProgress,
    double TargetX,
    double TargetY,
    bool ClampX,
    bool ClampY,
    double ElapsedSeconds,
    double AnimationStartZoom,
    double AnimationStartCenterX,
    double AnimationStartCenterY,
    double TransitionDurationSeconds,
    double EasedProgress,
    string Event)
{
    internal bool IsValid => CameraMath.IsValidState(this);

    internal static CameraState Wide(
        ulong sequence,
        long timestampQpc,
        CameraMode mode = CameraMode.Wide,
        string eventName = "wide") =>
        new(
            sequence,
            timestampQpc,
            false,
            mode,
            CameraSettings.WideZoom,
            0.5,
            0.5,
            1.0,
            0.5,
            0.5,
            false,
            false,
            0.0,
            CameraSettings.WideZoom,
            0.5,
            0.5,
            CameraSettings.NominalSettleSeconds,
            1.0,
            eventName);
}
