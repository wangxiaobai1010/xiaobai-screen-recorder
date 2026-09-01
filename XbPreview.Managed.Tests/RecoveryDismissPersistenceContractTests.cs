using XbPreview.Host;

namespace XbPreview.Managed.Tests;

internal static class RecoveryDismissPersistenceContractTests
{
    private const string SessionA =
        "28000000-0000-4000-8000-00000000A101";
    private const string SessionB =
        "28000000-0000-4000-8000-00000000A102";

    internal static void Run()
    {
        MissingFieldLoadsAsEmpty();
        DismissPersistsAcrossReload();
        MultipleCandidatesFilterOneThenAll();
        LaterDismissIsRuntimeOnly();
        OpenFolderDoesNotAcknowledge();
        DismissDoesNotMutateRecoveryTruthOrMedia();
        SaveFailureKeepsCandidateVisible();
        Console.WriteLine("RECOVERY_DISMISS_PERSISTENCE_CASES=7");
    }

    private static void MissingFieldLoadsAsEmpty()
    {
        using TemporaryDirectory temporary = new("missing-field");
        string settingsPath = temporary.File("product-settings.json");
        ProductSettingsStore store = new(settingsPath, string.Empty);
        store.Save(ProductSettings.Defaults with
        {
            AutoDirectorEnabled = true,
        });
        string json = File.ReadAllText(settingsPath);
        File.WriteAllText(
            settingsPath,
            RemoveJsonProperty(json, "RecoveryDismissedSessionIds"));

        ProductSettings loaded = store.Load();
        Require(
            loaded.RecoveryDismissedSessionIds.Length == 0 &&
                loaded.AutoDirectorEnabled,
            "CASE A: an old settings document loads a missing dismissed field " +
                "as empty without resetting other settings");
    }

    private static void DismissPersistsAcrossReload()
    {
        using TemporaryDirectory temporary = new("reload");
        ProductSettingsStore store = new(
            temporary.File("product-settings.json"),
            string.Empty);
        ProductState state = new(store);
        Require(state.TryDismissRecoveryReminder($"  {SessionA}  "),
            "CASE B: dismiss saves successfully");

        ProductSettings reloaded = new ProductState(store).Current;
        Require(
            reloaded.RecoveryDismissedSessionIds.SequenceEqual([SessionA]) &&
                !Presentation(reloaded, Candidate(SessionA)).Visible,
            "CASE B: the exact trimmed SessionId persists and remains hidden " +
                "after settings reload");
    }

    private static void MultipleCandidatesFilterOneThenAll()
    {
        using TemporaryDirectory temporary = new("multiple");
        ProductSettingsStore store = new(
            temporary.File("product-settings.json"),
            string.Empty);
        ProductState state = new(store);
        HistoricalSessionInspection a = Candidate(SessionA);
        HistoricalSessionInspection b = Candidate(SessionB);

        Require(state.TryDismissRecoveryReminder(SessionA),
            "CASE C: Session A saves");
        UserRecoveryPresentation oneLeft = Presentation(state.Current, a, b);
        FormalRecoveryBannerPresentation oneLeftBanner =
            FormalRecoveryBannerPresentation.Create(
                oneLeft,
                RecoveryAttemptSnapshot.NotStarted,
                dismissed: false);
        Require(
            oneLeftBanner.Visible &&
                oneLeftBanner.Candidates.Count == 1 &&
                oneLeftBanner.Candidates.Single().Candidate.SessionId ==
                    SessionB,
            "CASE C: dismissing A leaves B visible and keeps the banner");

        Require(state.TryDismissRecoveryReminder(SessionB),
            "CASE C: Session B saves");
        UserRecoveryPresentation noneLeft = Presentation(state.Current, a, b);
        Require(
            !noneLeft.Visible &&
                !FormalRecoveryBannerPresentation.Create(
                    noneLeft,
                    RecoveryAttemptSnapshot.NotStarted,
                    dismissed: false).Visible,
            "CASE C: dismissing all candidates hides the banner");
    }

    private static void LaterDismissIsRuntimeOnly()
    {
        using TemporaryDirectory temporary = new("later");
        ProductSettingsStore store = new(
            temporary.File("product-settings.json"),
            string.Empty);
        store.Save(ProductSettings.Defaults);
        UserRecoveryPresentation source = Presentation(
            store.Load(),
            Candidate(SessionA));

        Require(
            !FormalRecoveryBannerPresentation.Create(
                source,
                RecoveryAttemptSnapshot.NotStarted,
                dismissed: true).Visible,
            "CASE D: later hides the current presentation");
        ProductSettings reloaded = store.Load();
        Require(
            reloaded.RecoveryDismissedSessionIds.Length == 0 &&
                FormalRecoveryBannerPresentation.Create(
                    Presentation(reloaded, Candidate(SessionA)),
                    RecoveryAttemptSnapshot.NotStarted,
                    dismissed: false).Visible,
            "CASE D: later writes no SessionId and a fresh presentation can " +
                "show the candidate again");
    }

    private static void OpenFolderDoesNotAcknowledge()
    {
        using TemporaryDirectory temporary = new("open-folder");
        ProductSettingsStore store = new(
            temporary.File("product-settings.json"),
            string.Empty);
        store.Save(ProductSettings.Defaults);
        ProductState state = new(store);
        int navigationCount = 0;
        RecoveryFolderNavigator navigator = new(_ => navigationCount++);

        navigator.OpenContainingFolder(temporary.File("candidate.mp4"));
        ProductSettings reloaded = new ProductState(store).Current;
        Require(
            navigationCount == 1 &&
                state.Current.RecoveryDismissedSessionIds.Length == 0 &&
                reloaded.RecoveryDismissedSessionIds.Length == 0,
            "CASE E: opening the folder leaves acknowledgement state unchanged");
    }

    private static void DismissDoesNotMutateRecoveryTruthOrMedia()
    {
        using TemporaryDirectory temporary = new("no-media-mutation");
        string manifestPath = temporary.File("session-manifest.json");
        string mediaPath = temporary.File("candidate.partial.mp4");
        byte[] manifestBefore = [0x7b, 0x7d];
        byte[] mediaBefore = [0x00, 0x00, 0x00, 0x18, 0x66, 0x74, 0x79, 0x70];
        File.WriteAllBytes(manifestPath, manifestBefore);
        File.WriteAllBytes(mediaPath, mediaBefore);
        HistoricalSessionInspection candidate = Candidate(
            SessionA,
            mediaPath);
        HistoricalSessionClassification classificationBefore =
            candidate.Classification;
        ProductState state = new(new ProductSettingsStore(
            temporary.File("product-settings.json"),
            string.Empty));

        Require(state.TryDismissRecoveryReminder(SessionA),
            "CASE F: isolated dismissal saves");
        Require(
            File.ReadAllBytes(manifestPath).SequenceEqual(manifestBefore) &&
                File.ReadAllBytes(mediaPath).SequenceEqual(mediaBefore) &&
                candidate.Classification == classificationBefore &&
                !Presentation(state.Current, candidate).Visible,
            "CASE F: dismiss changes only UI visibility, not manifest, media, " +
                "or classification");
    }

    private static void SaveFailureKeepsCandidateVisible()
    {
        string parentlessPath = $"settings-{Guid.NewGuid():N}.json";
        ProductState state = new(new ProductSettingsStore(
            parentlessPath,
            string.Empty));
        Require(
            !state.TryDismissRecoveryReminder(SessionA) &&
                state.Current.RecoveryDismissedSessionIds.Length == 0 &&
                Presentation(state.Current, Candidate(SessionA)).Visible,
            "save failure does not pretend the dismissal succeeded");
    }

    private static UserRecoveryPresentation Presentation(
        ProductSettings settings,
        params HistoricalSessionInspection[] sessions) =>
        UserRecoveryPresentation.Create(
            Snapshot(sessions),
            dismissedSessionIds: settings.RecoveryDismissedSessionIds);

    private static HistoricalSessionInspection Candidate(
        string sessionId,
        string path = @"E:\isolated\candidate.mp4") => new(
            sessionId,
            1,
            HistoricalSessionClassification.PublishOutcomeUnprovenRetain,
            HistoricalSessionSeverity.Attention,
            HistoricalSessionReason.None,
            RetainUserMedia: true,
            WorkingCandidateExists: false,
            FinalCandidateExists: true,
            DisplaySafePath: path,
            HistoricalSessionParseStatus.Valid,
            0,
            HistoricalSessionOwnerState.InactiveLeaseReleased,
            0);

    private static StartupInspectionSnapshot Snapshot(
        params HistoricalSessionInspection[] sessions) => new(
            1,
            StartupInspectionState.Completed,
            new StartupInspectionResult(
                HistoricalSessionScanStatus.Success,
                0,
                TimeSpan.Zero,
                (uint)sessions.Length,
                0,
                (ulong)sessions.Length,
                1024,
                false,
                false,
                sessions),
            null);

    private static string RemoveJsonProperty(string json, string property)
    {
        string[] lines = json.Split(
            ["\r\n", "\n"],
            StringSplitOptions.None);
        int index = Array.FindIndex(lines, line => line.Contains(
            $"\"{property}\"",
            StringComparison.Ordinal));
        Require(index >= 0, $"fixture contains {property}");
        int end = index;
        int bracketDepth = 0;
        do
        {
            bracketDepth += lines[end].Count(character => character == '[');
            bracketDepth -= lines[end].Count(character => character == ']');
            end++;
        }
        while (end < lines.Length && bracketDepth > 0);
        List<string> remaining = lines.ToList();
        remaining.RemoveRange(index, end - index);
        if (index > 0 &&
            remaining[index - 1].TrimEnd().EndsWith(','))
        {
            remaining[index - 1] =
                remaining[index - 1].TrimEnd().TrimEnd(',');
        }
        return string.Join(Environment.NewLine, remaining);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(
                $"Recovery dismiss persistence test failed: {message}");
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private readonly string _path;

        internal TemporaryDirectory(string suffix)
        {
            _path = Path.Combine(
                Path.GetTempPath(),
                "xbpreview-recovery-dismiss-tests",
                $"{suffix}-{Guid.NewGuid():N}");
            Directory.CreateDirectory(_path);
        }

        internal string File(string name) => Path.Combine(_path, name);

        public void Dispose() => Directory.Delete(_path, recursive: true);
    }
}
