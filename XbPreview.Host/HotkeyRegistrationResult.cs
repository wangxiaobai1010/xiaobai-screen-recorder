namespace XbPreview.Host;

internal readonly record struct HotkeyRegistrationResult(
    bool Succeeded,
    HotkeyActivationState State,
    HotkeyBinding? FailedBinding,
    int WindowsErrorCode)
{
    internal static HotkeyRegistrationResult ForState(
        HotkeyActivationState state) =>
        new(state == HotkeyActivationState.Enabled, state, null, 0);

    internal static HotkeyRegistrationResult Failure(
        HotkeyBinding binding,
        int windowsErrorCode) =>
        new(false, HotkeyActivationState.Failed, binding, windowsErrorCode);
}
