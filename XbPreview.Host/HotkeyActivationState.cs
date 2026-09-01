namespace XbPreview.Host;

internal enum HotkeyActivationState
{
    NotAvailable = 0,
    Disabled = 1,
    Enabled = 2,
    Failed = 3,
    SuspendedByDirector = 4,
}
