namespace XbPreview.Host;

internal enum StartupInspectionState
{
    NotStarted,
    Running,
    Completed,
    Canceled,
    Failed,
}

internal enum HistoricalSessionScanStatus
{
    Success = 0,
    SessionsRootAbsent = 1,
    SessionsRootInaccessible = 2,
    SessionsRootUnsafe = 3,
    IoFailure = 4,
    PartialTruncated = 5,
}

internal enum HistoricalSessionClassification
{
    CompletedConsistent = 0,
    ReconciledCompletedConsistent = 1,
    PublishedMetadataNeedsReconciliation = 2,
    PublishOutcomeUnprovenRetain = 3,
    ReadyToPublishWorkingPreserved = 4,
    IncompleteWithWorkingMedia = 5,
    IncompleteNoMediaRetain = 6,
    PublishFailedWorkingPreserved = 7,
    FinalizeOrValidationFailedWorkingPreserved = 8,
    ManifestCorrupt = 9,
    ManifestMissing = 10,
    FilesystemConflict = 11,
    UnknownRetain = 12,
    UserCancelled = 13,
}

internal enum HistoricalSessionSeverity
{
    Info = 0,
    Attention = 1,
    RecoveryCandidate = 2,
    CriticalRetain = 3,
}

internal enum HistoricalSessionParseStatus
{
    Valid = 0,
    NotFound = 1,
    Inaccessible = 2,
    MalformedJson = 3,
    UnsupportedSchema = 4,
    SemanticInvalid = 5,
    UnknownOrFutureState = 6,
    IoFailure = 7,
}

internal enum HistoricalSessionOwnerState
{
    ActiveOwned = 0,
    InactiveLeaseReleased = 1,
    EvidenceMissing = 2,
    UnsafePath = 3,
    Inaccessible = 4,
    IoFailure = 5,
    Unknown = 6,
}

[Flags]
internal enum HistoricalSessionReason : ulong
{
    None = 0,
    FinalMissing = 1UL << 0,
    WorkingAndFinalBothPresent = 1UL << 1,
    PathOutsideRoot = 1UL << 2,
    ReparsePoint = 1UL << 3,
    IdentityMismatch = 1UL << 4,
    ManifestIoError = 1UL << 5,
    UnsupportedSchema = 1UL << 6,
    LiveOwnerUnknown = 1UL << 7,
    NoMediaProven = 1UL << 8,
    MediaSubmitted = 1UL << 9,
    FinalizeFailed = 1UL << 10,
    ValidationFailed = 1UL << 11,
    PublishFailed = 1UL << 12,
    PublishIdentityUnavailable = 1UL << 13,
    InventoryIncomplete = 1UL << 14,
    ManifestMissing = 1UL << 15,
    ManifestMalformed = 1UL << 16,
    PathInaccessible = 1UL << 17,
    TypeMismatch = 1UL << 18,
    ConcurrentChange = 1UL << 19,
    UnknownState = 1UL << 20,
    LiveOwnerActive = 1UL << 21,
    LifetimeOwnerEvidenceMissing = 1UL << 22,
}

internal sealed record HistoricalSessionInspection(
    string SessionId,
    ulong? ObservedRevision,
    HistoricalSessionClassification Classification,
    HistoricalSessionSeverity Severity,
    HistoricalSessionReason Reasons,
    bool RetainUserMedia,
    bool WorkingCandidateExists,
    bool FinalCandidateExists,
    string DisplaySafePath,
    HistoricalSessionParseStatus ManifestParseStatus,
    int ManifestDiagnosticHResult,
    HistoricalSessionOwnerState OwnerState,
    int OwnerDiagnosticHResult);

internal sealed class StartupInspectionResult
{
    internal StartupInspectionResult(
        HistoricalSessionScanStatus status,
        int diagnosticHResult,
        TimeSpan wallClockDuration,
        uint sessionCount,
        uint unrecognizedEntryCount,
        ulong entriesObserved,
        ulong maximumEntries,
        bool truncated,
        bool mediaWithoutSessionDirectoryBlindSpot,
        IEnumerable<HistoricalSessionInspection> sessions)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        Status = status;
        DiagnosticHResult = diagnosticHResult;
        WallClockDuration = wallClockDuration;
        SessionCount = sessionCount;
        UnrecognizedEntryCount = unrecognizedEntryCount;
        EntriesObserved = entriesObserved;
        MaximumEntries = maximumEntries;
        Truncated = truncated;
        MediaWithoutSessionDirectoryBlindSpot =
            mediaWithoutSessionDirectoryBlindSpot;
        Sessions = Array.AsReadOnly(sessions.ToArray());
    }

    internal HistoricalSessionScanStatus Status { get; }

    internal int DiagnosticHResult { get; }

    internal TimeSpan WallClockDuration { get; }

    internal uint SessionCount { get; }

    internal uint UnrecognizedEntryCount { get; }

    internal ulong EntriesObserved { get; }

    internal ulong MaximumEntries { get; }

    internal bool Truncated { get; }

    internal bool MediaWithoutSessionDirectoryBlindSpot { get; }

    internal IReadOnlyList<HistoricalSessionInspection> Sessions { get; }
}

internal sealed record StartupInspectionSnapshot(
    long Generation,
    StartupInspectionState State,
    StartupInspectionResult? Result,
    string? Error)
{
    internal static StartupInspectionSnapshot NotStarted { get; } =
        new(0, StartupInspectionState.NotStarted, null, null);

    internal bool IsTerminal => State is
        StartupInspectionState.Completed or
        StartupInspectionState.Canceled or
        StartupInspectionState.Failed;
}

internal interface IStartupSessionInspector
{
    StartupInspectionResult Inspect(CancellationToken cancellationToken);
}
