namespace XbPreview.Host;

internal static class ComfortZoneSettings
{
    // Ratios are inherited from the v0.6d comfort-zone implementation that
    // was exercised on the target machine.
    internal const double WidthRatio = 0.42;
    internal const double HeightRatio = 0.36;

    // Exact critical damping remains time-consistent across update rates.
    internal const double AngularFrequency = 13.0;
    internal const double MaximumDeltaSeconds = 0.032;
    internal const double StopPositionEpsilon = 2e-5;
    internal const double StopVelocityEpsilon = 5e-4;
    internal const double BoundaryEpsilon = 1e-6;
    internal const bool UseHysteresis = false;
}
