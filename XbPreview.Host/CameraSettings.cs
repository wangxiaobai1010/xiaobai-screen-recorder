namespace XbPreview.Host;

internal static class CameraSettings
{
    internal const double WideZoom = 1.0;
    internal const double StandardZoom = 1.6;
    internal const double StrongZoom = 2.0;
    internal const double MaxSupportedZoom = StrongZoom;
    // Four seconds leaves enough time to read or point after a click while
    // keeping the first Director Lite return-to-wide easy to observe and A/B.
    internal const double DirectorLiteInactivitySeconds = 4.0;
    internal const double SpringAngularFrequency = 14.0;
    internal const double MaximumDeltaSeconds = 0.032;
    internal const double ZoomStopPositionEpsilon = 1e-4;
    internal const double ZoomStopVelocityEpsilon = 1e-3;
    internal const double CenterStopPositionEpsilon = 2e-5;
    internal const double CenterStopVelocityEpsilon = 5e-4;

    // Retained only as state metadata for the native diagnostics ABI. Motion
    // is driven exclusively by real delta time and the spring state.
    internal const double NominalSettleSeconds = 0.48;
    internal const int UpdateRateHz = 120;
}
