namespace XbPreview.Host;

internal enum CameraOwner
{
    Manual = 0,
    DirectorLite = 1,
}

internal enum DirectorLiteState
{
    Wide = 0,
    Focused = 1,
}

internal enum DirectorFocusStrength
{
    Soft = 0,
    Strong = 1,
}

internal static class DirectorFocusStrengthDefinition
{
    internal static CameraPreset TargetPreset(DirectorFocusStrength strength) =>
        strength switch
        {
            DirectorFocusStrength.Soft => CameraPreset.Standard,
            DirectorFocusStrength.Strong => CameraPreset.Strong,
            _ => throw new ArgumentOutOfRangeException(nameof(strength)),
        };
}
