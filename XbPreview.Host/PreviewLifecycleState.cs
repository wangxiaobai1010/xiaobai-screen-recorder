namespace XbPreview.Host;

internal enum PreviewLifecycleState
{
    NotInitialized,
    Stopped,
    Starting,
    Previewing,
    SelectingRegion,
    Reconfiguring,
    Stopping,
    Error,
    Closing,
    Disposed,
}

internal enum PreviewLifecycleOperationStatus
{
    Succeeded,
    NoChange,
    Rejected,
    Failed,
}

internal sealed record PreviewLifecycleSnapshot(
    PreviewLifecycleState State,
    string? LastError);

internal sealed record PreviewLifecycleResult(
    PreviewLifecycleOperationStatus Status,
    PreviewLifecycleState State,
    string? Error)
{
    internal bool Succeeded =>
        Status is PreviewLifecycleOperationStatus.Succeeded or
            PreviewLifecycleOperationStatus.NoChange;
}
