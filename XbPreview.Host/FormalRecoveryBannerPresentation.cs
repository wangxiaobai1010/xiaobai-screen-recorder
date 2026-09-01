using XbPreview.Avalonia.Localization;

namespace XbPreview.Host;

internal sealed record FormalRecoveryBannerCandidate(
    UserRecoveryCandidate Candidate,
    bool ShowTryRecovery,
    bool RecoveryRunning,
    bool CanOpenFolder);

internal sealed class FormalRecoveryBannerPresentation
{
    private FormalRecoveryBannerPresentation(
        string noticeText,
        IReadOnlyList<FormalRecoveryBannerCandidate> candidates)
    {
        NoticeText = noticeText;
        Candidates = candidates;
    }

    internal static FormalRecoveryBannerPresentation Empty { get; } = new(
        string.Empty,
        Array.Empty<FormalRecoveryBannerCandidate>());

    internal string NoticeText { get; }

    internal IReadOnlyList<FormalRecoveryBannerCandidate> Candidates { get; }

    internal bool Visible => Candidates.Count != 0;

    internal static FormalRecoveryBannerPresentation Create(
        UserRecoveryPresentation source,
        RecoveryAttemptSnapshot attempt,
        bool dismissed)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(attempt);
        if (dismissed || !source.Visible)
        {
            return Empty;
        }

        bool recoveryRunning = attempt.State == RecoveryAttemptState.Running;
        FormalRecoveryBannerCandidate[] candidates = source.Candidates
            .Select(candidate =>
            {
                bool attemptMatches = string.Equals(
                    attempt.SessionId,
                    candidate.SessionId,
                    StringComparison.Ordinal);
                bool failedThisRun = attemptMatches &&
                    attempt.IsTerminal &&
                    !attempt.ConfirmedRecovered;
                return new FormalRecoveryBannerCandidate(
                    candidate,
                    ShowTryRecovery: candidate.CanTryRecovery &&
                        !failedThisRun,
                    RecoveryRunning: recoveryRunning &&
                        candidate.CanTryRecovery,
                    CanOpenFolder: !string.IsNullOrWhiteSpace(
                        candidate.DisplaySafePath));
            })
            .ToArray();
        string noticeText = candidates.Length == 1 &&
            source.AttentionCount == 1
                ? Strings.Get("RecoveryFoundOne")
                : source.NoticeText;
        return new FormalRecoveryBannerPresentation(
            noticeText,
            Array.AsReadOnly(candidates));
    }
}
