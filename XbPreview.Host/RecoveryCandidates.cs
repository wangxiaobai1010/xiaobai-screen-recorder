using XbPreview.Avalonia.Localization;

namespace XbPreview.Host;

internal enum UserRecoveryCandidateState
{
    Recovered,
    CanTryRecovery,
    RecordingPreserved,
    NeedsAttentionRetained,
}

internal sealed record UserRecoveryCandidate(
    string SessionId,
    ulong? ObservedRevision,
    UserRecoveryCandidateState State,
    string Title,
    string StatusText,
    string DisplaySafePath,
    bool CanTryRecovery)
{
    internal bool NeedsAttention =>
        State != UserRecoveryCandidateState.Recovered;
}

internal sealed class UserRecoveryPresentation
{
    private static readonly string[] ForbiddenUserTerms =
    [
        "Manifest",
        "ReadyToPublish",
        "PublishOutcomeUnprovenRetain",
        "ReconciledCompleted",
        "reconciliation",
        "schema",
        "revision",
        "HRESULT",
    ];

    private UserRecoveryPresentation(
        string noticeText,
        IReadOnlyList<UserRecoveryCandidate> candidates)
    {
        NoticeText = noticeText;
        Candidates = candidates;
    }

    internal static UserRecoveryPresentation Empty { get; } =
        new(string.Empty, Array.Empty<UserRecoveryCandidate>());

    internal string NoticeText { get; }

    internal IReadOnlyList<UserRecoveryCandidate> Candidates { get; }

    internal bool Visible => Candidates.Count != 0;

    internal int AttentionCount => Candidates.Count(candidate =>
        candidate.NeedsAttention);

    internal static UserRecoveryPresentation Create(
        StartupInspectionSnapshot snapshot,
        string? confirmedRecoveredSessionId = null,
        IReadOnlyDictionary<string, string>? statusOverrides = null,
        IReadOnlyCollection<string>? dismissedSessionIds = null)
    {
        if (snapshot.State != StartupInspectionState.Completed ||
            snapshot.Result is null)
        {
            return Empty;
        }

        List<UserRecoveryCandidate> candidates = [];
        foreach (HistoricalSessionInspection session in snapshot.Result.Sessions)
        {
            if (dismissedSessionIds?.Contains(
                    session.SessionId,
                    StringComparer.Ordinal) == true)
            {
                continue;
            }
            UserRecoveryCandidate? candidate = MapSession(
                session,
                confirmedRecoveredSessionId,
                statusOverrides);
            if (candidate is not null)
            {
                ValidateUserText(candidate.Title);
                ValidateUserText(candidate.StatusText);
                candidates.Add(candidate);
            }
        }

        int attentionCount = candidates.Count(candidate =>
            candidate.NeedsAttention);
        string notice = attentionCount switch
        {
            0 when candidates.Count != 0 => Strings.Get("RecoveryRecoveredNotice"),
            1 => Strings.Get("RecoveryFoundOne"),
            _ when attentionCount > 1 =>
                Strings.Format("RecoveryFoundMany", attentionCount),
            _ => string.Empty,
        };
        ValidateUserText(notice);
        return candidates.Count == 0
            ? Empty
            : new UserRecoveryPresentation(
                notice,
                Array.AsReadOnly(candidates.ToArray()));
    }

    private static UserRecoveryCandidate? MapSession(
        HistoricalSessionInspection session,
        string? confirmedRecoveredSessionId,
        IReadOnlyDictionary<string, string>? statusOverrides)
    {
        if (session.Classification is
            HistoricalSessionClassification.CompletedConsistent or
            HistoricalSessionClassification.UserCancelled)
        {
            return null;
        }
        if (session.Classification ==
            HistoricalSessionClassification.ReconciledCompletedConsistent)
        {
            if (!string.Equals(
                session.SessionId,
                confirmedRecoveredSessionId,
                StringComparison.Ordinal))
            {
                return null;
            }
            return new UserRecoveryCandidate(
                session.SessionId,
                session.ObservedRevision,
                UserRecoveryCandidateState.Recovered,
                Strings.Get("RecoveryRecoveredTitle"),
                Strings.Get("RecoveryRecoveredNotice"),
                session.DisplaySafePath,
                CanTryRecovery: false);
        }

        string? statusOverride = null;
        _ = statusOverrides?.TryGetValue(
            session.SessionId,
            out statusOverride);
        bool canTry = session.Classification ==
                HistoricalSessionClassification.PublishOutcomeUnprovenRetain &&
            session.OwnerState ==
                HistoricalSessionOwnerState.InactiveLeaseReleased &&
            session.ObservedRevision.HasValue;
        if (canTry)
        {
            return new UserRecoveryCandidate(
                session.SessionId,
                session.ObservedRevision,
                UserRecoveryCandidateState.CanTryRecovery,
                Strings.Get("RecoveryInterruptedTitle"),
                statusOverride ??
                    Strings.Get("RecoveryCanTryStatus"),
                session.DisplaySafePath,
                CanTryRecovery: true);
        }

        bool workingPreserved = session.WorkingCandidateExists &&
            session.Classification is
                HistoricalSessionClassification.ReadyToPublishWorkingPreserved or
                HistoricalSessionClassification.IncompleteWithWorkingMedia or
                HistoricalSessionClassification.PublishFailedWorkingPreserved or
                HistoricalSessionClassification.
                    FinalizeOrValidationFailedWorkingPreserved;
        if (workingPreserved)
        {
            return new UserRecoveryCandidate(
                session.SessionId,
                session.ObservedRevision,
                UserRecoveryCandidateState.RecordingPreserved,
                Strings.Get("RecoveryInterruptedTitle"),
                statusOverride ??
                    Strings.Get("RecoveryPreservedStatus"),
                session.DisplaySafePath,
                CanTryRecovery: false);
        }

        return new UserRecoveryCandidate(
            session.SessionId,
            session.ObservedRevision,
            UserRecoveryCandidateState.NeedsAttentionRetained,
            Strings.Get("RecoveryAttentionTitle"),
            statusOverride ??
                Strings.Get("RecoveryRetainedStatus"),
            session.DisplaySafePath,
            CanTryRecovery: false);
    }

    private static void ValidateUserText(string value)
    {
        foreach (string forbidden in ForbiddenUserTerms)
        {
            if (value.Contains(forbidden, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "User recovery text contains an engineering-only term.");
            }
        }
    }
}
