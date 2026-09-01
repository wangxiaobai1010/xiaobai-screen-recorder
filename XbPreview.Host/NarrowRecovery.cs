namespace XbPreview.Host;

internal enum NarrowRecoveryStatus
{
    Reconciled,
    AlreadyReconciled,
    NotEligibleState,
    InvalidSourceFacts,
    SemanticConflict,
    GuardRejected,
    RevisionChanged,
    ConcurrentChange,
    ImmutableFieldViolation,
    UnsupportedSchema,
    EvidenceInsufficient,
    CasFailed,
    IoFailure,
    Unknown,
}

internal sealed record NarrowRecoveryResult(
    NarrowRecoveryStatus Status,
    int DiagnosticHResult,
    ulong ExpectedRevision,
    ulong? ObservedRevision,
    NativeMethods.NarrowReconciliationGuardStatusV1? GuardStatus,
    NativeMethods.NarrowReconciliationCasStatusV1? CasStatus)
{
    internal bool RequiresConfirmationRescan => Status is
        NarrowRecoveryStatus.Reconciled or
        NarrowRecoveryStatus.AlreadyReconciled;
}

internal interface IUserRecoveryService
{
    NarrowRecoveryResult Recover(
        string canonicalSessionId,
        ulong expectedRevision,
        CancellationToken cancellationToken);
}

internal enum RecoveryAttemptState
{
    NotStarted,
    Running,
    Completed,
    Canceled,
    Failed,
}

internal sealed record RecoveryAttemptSnapshot(
    long Generation,
    RecoveryAttemptState State,
    string SessionId,
    NarrowRecoveryResult? NativeResult,
    StartupInspectionResult? RescanResult,
    bool ConfirmedRecovered,
    string UserMessage,
    string? Error)
{
    internal static RecoveryAttemptSnapshot NotStarted { get; } = new(
        0,
        RecoveryAttemptState.NotStarted,
        string.Empty,
        null,
        null,
        false,
        string.Empty,
        null);

    internal bool IsTerminal => State is
        RecoveryAttemptState.Completed or
        RecoveryAttemptState.Canceled or
        RecoveryAttemptState.Failed;
}
