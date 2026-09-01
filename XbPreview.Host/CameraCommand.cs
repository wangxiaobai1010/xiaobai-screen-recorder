namespace XbPreview.Host;

internal enum CameraCommand
{
    ToggleStandardCloseUp = 1,
    ToggleStrongCloseUp = 2,
}

internal enum CameraPreset
{
    Wide = 0,
    Standard = 1,
    Strong = 2,
}

internal static class CameraCommandDefinition
{
    internal static CameraPreset TargetPreset(CameraCommand command) =>
        command switch
        {
            CameraCommand.ToggleStandardCloseUp => CameraPreset.Standard,
            CameraCommand.ToggleStrongCloseUp => CameraPreset.Strong,
            _ => throw new ArgumentOutOfRangeException(nameof(command)),
        };

    internal static double TargetZoom(CameraPreset preset) =>
        preset switch
        {
            CameraPreset.Wide => CameraSettings.WideZoom,
            CameraPreset.Standard => CameraSettings.StandardZoom,
            CameraPreset.Strong => CameraSettings.StrongZoom,
            _ => throw new ArgumentOutOfRangeException(nameof(preset)),
        };
}
