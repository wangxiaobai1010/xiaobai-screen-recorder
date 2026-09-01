using System.Diagnostics;
using XbPreview.Avalonia.Views;
using XbPreview.Host;

namespace XbPreview.Managed.Tests;

internal static class RecoveryUiContractTests
{
    private const string CandidateId =
        "27000000-0000-4000-8000-00000000A101";
    private const string PreservedId =
        "27000000-0000-4000-8000-00000000A102";
    private const string CandidatePath = @"E:\隔离审查\候选.mp4";

    internal static async Task RunAsync()
    {
        int completed = 0;

        UserRecoveryPresentation empty = UserRecoveryPresentation.Create(
            Snapshot());
        Require(!FormalRecoveryBannerPresentation.Create(
                empty,
                RecoveryAttemptSnapshot.NotStarted,
                dismissed: false).Visible,
            "no candidate keeps the formal banner absent");
        completed++;

        UserRecoveryPresentation one = UserRecoveryPresentation.Create(
            Snapshot(ActionableSession()));
        FormalRecoveryBannerPresentation oneBanner =
            FormalRecoveryBannerPresentation.Create(
                one,
                RecoveryAttemptSnapshot.NotStarted,
                dismissed: false);
        StructuralRecoveryBannerPresentation oneView = View(oneBanner);
        Require(oneBanner.Visible && oneBanner.Candidates.Count == 1 &&
            oneBanner.Candidates.Single().ShowTryRecovery &&
            oneBanner.Candidates.Single().CanOpenFolder &&
            oneBanner.NoticeText == "发现一段未正常结束的录制" &&
            oneView.IsCompactSingle &&
            oneView.BodyText ==
                "原始录制文件已为你保留，可以尝试恢复。",
            "one candidate drives one compact summary and the same actions");
        completed++;

        UserRecoveryPresentation multiple = UserRecoveryPresentation.Create(
            Snapshot(
                ActionableSession(),
                Session(
                    PreservedId,
                    HistoricalSessionClassification.IncompleteWithWorkingMedia,
                    workingExists: true,
                    path: @"E:\隔离审查\保留.partial.mp4")));
        FormalRecoveryBannerPresentation multipleBanner =
            FormalRecoveryBannerPresentation.Create(
                multiple,
                RecoveryAttemptSnapshot.NotStarted,
                dismissed: false);
        StructuralRecoveryBannerPresentation multipleView =
            View(multipleBanner);
        Require(multipleBanner.Candidates.Count == 2 &&
            multipleBanner.NoticeText == "发现 2 段需要处理的历史录制" &&
            !multipleView.IsCompactSingle &&
            multipleView.Candidates.All(candidate =>
                !string.IsNullOrWhiteSpace(candidate.Title) &&
                !string.IsNullOrWhiteSpace(candidate.StatusText)),
            "multiple candidates retain the mature exact-count light list");
        completed++;

        FormalRecoveryBannerPresentation dismissed =
            FormalRecoveryBannerPresentation.Create(
                one,
                RecoveryAttemptSnapshot.NotStarted,
                dismissed: true);
        Require(!dismissed.Visible && one.Visible &&
            one.Candidates.Single().SessionId == CandidateId,
            "dismiss hides only this presentation and preserves candidate truth");
        completed++;

        Dictionary<string, string> failureCopy = new(StringComparer.Ordinal)
        {
            [CandidateId] =
                "暂时无法自动处理这段录制，但文件不会被删除。",
        };
        UserRecoveryPresentation failedSource = UserRecoveryPresentation.Create(
            Snapshot(ActionableSession()),
            statusOverrides: failureCopy);
        RecoveryAttemptSnapshot failedAttempt = new(
            1,
            RecoveryAttemptState.Completed,
            CandidateId,
            RecoveryResult(NarrowRecoveryStatus.GuardRejected),
            null,
            false,
            failureCopy[CandidateId],
            null);
        FormalRecoveryBannerCandidate failedCandidate =
            FormalRecoveryBannerPresentation.Create(
                failedSource,
                failedAttempt,
                dismissed: false).Candidates.Single();
        Require(!failedCandidate.ShowTryRecovery &&
            failedCandidate.CanOpenFolder &&
            failedCandidate.Candidate.StatusText.Contains(
                "文件不会被删除", StringComparison.Ordinal),
            "backend failure truth remains calm and keeps folder navigation");
        completed++;

        await VerifyPositiveCoordinatorConfirmationAsync(
            one.Candidates.Single());
        completed++;

        ProcessStartInfo? observedStart = null;
        RecoveryFolderNavigator navigator = new(start => observedStart = start);
        navigator.OpenContainingFolder(CandidatePath);
        Require(observedStart is not null &&
            observedStart.FileName == "explorer.exe" &&
            observedStart.UseShellExecute &&
            observedStart.Arguments == $"/select,\"{CandidatePath}\"",
            "open folder is an Explorer-only navigation request");
        completed++;

        string repository = Environment.CurrentDirectory;
        string xaml = File.ReadAllText(Path.Combine(
            repository,
            "XbPreview.Avalonia",
            "Views",
            "StructuralShellView.axaml"));
        string host = File.ReadAllText(Path.Combine(
            repository,
            "XbPreview.Host",
            "StructuralAvaloniaShellHost.cs"));
        string view = File.ReadAllText(Path.Combine(
            repository,
            "XbPreview.Avalonia",
            "Views",
            "StructuralShellView.axaml.cs"));
        Require(xaml.Contains("HomeSurface\" RowDefinitions=\"*,225\"",
                StringComparison.Ordinal) &&
            xaml.Contains("x:Name=\"RecoveryBanner\"",
                StringComparison.Ordinal) &&
            xaml.Contains("x:Name=\"RecoveryBodyText\"",
                StringComparison.Ordinal) &&
            xaml.Contains("x:Name=\"RecoverySingleLayout\"",
                StringComparison.Ordinal) &&
            xaml.Contains("x:Name=\"RecoverySingleCandidateActions\"",
                StringComparison.Ordinal) &&
            xaml.Contains("x:Name=\"RecoverySingleDismissButton\"",
                StringComparison.Ordinal) &&
            xaml.Contains("TextTrimming=\"CharacterEllipsis\"",
                StringComparison.Ordinal) &&
            xaml.Contains("IsVisible=\"False\"",
                StringComparison.Ordinal) &&
            view.Contains(
                "RecoverySingleCandidateActions.Children.Add(",
                StringComparison.Ordinal) &&
            view.Contains("includeCopy: false", StringComparison.Ordinal) &&
            view.Contains("includeCopy: true", StringComparison.Ordinal) &&
            view.Contains("Content = \"不再提醒\"",
                StringComparison.Ordinal) &&
            view.Contains("RecoveryDismissReminderRequested?.Invoke(",
                StringComparison.Ordinal) &&
            view.Contains(
                "RecoveryMultipleLayout.IsVisible = " +
                    "!presentation.IsCompactSingle",
                StringComparison.Ordinal) &&
            host.Contains("TryScheduleStartupInspection(",
                StringComparison.Ordinal) &&
            host.Contains(
                "_recordingFixedHomeAdapter.CanonicalOutputRoot",
                StringComparison.Ordinal) &&
            host.Contains(
                "NativeHistoricalSessionInspector.ForOutputRoot(",
                StringComparison.Ordinal) &&
            host.Contains(
                "NativeNarrowRecoveryService.ForOutputRoot(",
                StringComparison.Ordinal) &&
            !host.Contains("TryScheduleStartupInspection(logDirectory);",
                StringComparison.Ordinal) &&
            host.Contains("RecoveryActionCoordinator recoveryActions = new(",
                StringComparison.Ordinal) &&
            host.Contains("_productState.TryDismissRecoveryReminder(",
                StringComparison.Ordinal) &&
            !host.Contains("Task.Delay(", StringComparison.Ordinal),
            "formal host keeps compact single-row actions, the fixed deck, " +
                "collapsed banner, and once-only async seams");
        completed++;

        Require(completed == 8,
            "complete formal Recovery UI contract matrix");
        RecoveryDismissPersistenceContractTests.Run();
        Console.WriteLine($"RECOVERY_UI_CONTRACT_MATRIX={completed}");
    }

    internal static async Task RunCoordinatorConfirmationContractAsync()
    {
        UserRecoveryCandidate candidate = UserRecoveryPresentation.Create(
            Snapshot(ActionableSession())).Candidates.Single();
        await VerifyPositiveCoordinatorConfirmationAsync(candidate);
        Console.WriteLine("RECOVERY_COORDINATOR_CONFIRMATION=PASS");
    }

    private static async Task VerifyPositiveCoordinatorConfirmationAsync(
        UserRecoveryCandidate candidate)
    {
        ImmediateRecoveryService service = new(
            RecoveryResult(NarrowRecoveryStatus.Reconciled));
        FixedInspector rescan = new(Result(Session(
            CandidateId,
            HistoricalSessionClassification.ReconciledCompletedConsistent,
            revision: 2,
            finalExists: true,
            path: CandidatePath)));
        await using RecoveryActionCoordinator coordinator = new(
            service,
            rescan);
        RecoveryAttemptSnapshot result = await coordinator.StartAsync(
            candidate);
        Require(service.CallCount == 1 && rescan.CallCount == 1 &&
            result.ConfirmedRecovered,
            "try recovery uses the existing action coordinator and rescan truth");
    }

    private static StructuralRecoveryBannerPresentation View(
        FormalRecoveryBannerPresentation banner) => new(
        banner.NoticeText,
        banner.Candidates.Select(candidate =>
            new StructuralRecoveryCandidatePresentation(
                candidate.Candidate.SessionId,
                candidate.Candidate.Title,
                candidate.Candidate.StatusText,
                candidate.Candidate.DisplaySafePath,
                candidate.ShowTryRecovery,
                candidate.RecoveryRunning,
                candidate.CanOpenFolder)).ToArray());

    private static HistoricalSessionInspection ActionableSession() => Session(
        CandidateId,
        HistoricalSessionClassification.PublishOutcomeUnprovenRetain,
        revision: 1,
        finalExists: true,
        path: CandidatePath);

    private static HistoricalSessionInspection Session(
        string sessionId,
        HistoricalSessionClassification classification,
        ulong? revision = null,
        bool workingExists = false,
        bool finalExists = false,
        string path = "") => new(
            sessionId,
            revision,
            classification,
            HistoricalSessionSeverity.Attention,
            HistoricalSessionReason.None,
            RetainUserMedia: true,
            WorkingCandidateExists: workingExists,
            FinalCandidateExists: finalExists,
            DisplaySafePath: path,
            HistoricalSessionParseStatus.Valid,
            0,
            HistoricalSessionOwnerState.InactiveLeaseReleased,
            0);

    private static StartupInspectionSnapshot Snapshot(
        params HistoricalSessionInspection[] sessions) => new(
            1,
            StartupInspectionState.Completed,
            Result(sessions),
            null);

    private static StartupInspectionResult Result(
        params HistoricalSessionInspection[] sessions) => new(
            HistoricalSessionScanStatus.Success,
            0,
            TimeSpan.Zero,
            (uint)sessions.Length,
            0,
            (ulong)sessions.Length,
            1024,
            false,
            false,
            sessions);

    private static NarrowRecoveryResult RecoveryResult(
        NarrowRecoveryStatus status) => new(
            status,
            0,
            1,
            1,
            null,
            null);

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed class ImmediateRecoveryService(
        NarrowRecoveryResult result) : IUserRecoveryService
    {
        internal int CallCount { get; private set; }

        public NarrowRecoveryResult Recover(
            string canonicalSessionId,
            ulong expectedRevision,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            return result;
        }
    }

    private sealed class FixedInspector(
        StartupInspectionResult result) : IStartupSessionInspector
    {
        internal int CallCount { get; private set; }

        public StartupInspectionResult Inspect(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            return result;
        }
    }
}
