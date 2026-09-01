using System.Globalization;
using System.Text.RegularExpressions;
using XbPreview.Host;

namespace XbPreview.LongRun;

internal sealed record LongRunOptions(
    int DurationSeconds,
    int SampleIntervalMilliseconds,
    string OutputBaseDirectory,
    string RunId,
    string RunDirectory,
    string SummaryJsonPath,
    string SnapshotsJsonlPath,
    int? CancelAfterSeconds)
{
    private static readonly Regex ValidRunId = new(
        "^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$",
        RegexOptions.CultureInvariant);

    internal static bool TryParse(
        string[] args,
        string repositoryRoot,
        out LongRunOptions? options,
        out string error)
    {
        options = null;
        error = string.Empty;
        Dictionary<string, string> values = new(StringComparer.Ordinal);
        HashSet<string> known = new(StringComparer.Ordinal)
        {
            "--duration-seconds",
            "--sample-interval-ms",
            "--output-directory",
            "--run-id",
            "--summary-json",
            "--snapshots-jsonl",
            "--cancel-after-seconds",
        };

        for (int index = 0; index < args.Length; index += 2)
        {
            string name = args[index];
            if (!known.Contains(name))
            {
                error = $"Unknown argument: {name}";
                return false;
            }
            if (index + 1 >= args.Length || args[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                error = $"Missing value for {name}.";
                return false;
            }
            if (!values.TryAdd(name, args[index + 1]))
            {
                error = $"Duplicate argument: {name}";
                return false;
            }
        }

        if (!TryPositiveInt(values, "--duration-seconds", required: true, out int duration, out error) ||
            !TryPositiveInt(values, "--sample-interval-ms", required: false, out int interval, out error) ||
            !TryOptionalPositiveInt(values, "--cancel-after-seconds", out int? cancelAfter, out error))
        {
            return false;
        }
        if (interval == 0)
        {
            interval = 1000;
        }
        if (interval > 60_000)
        {
            error = "--sample-interval-ms must not exceed 60000.";
            return false;
        }
        if (cancelAfter.HasValue && cancelAfter.Value >= duration)
        {
            error = "--cancel-after-seconds must be less than --duration-seconds.";
            return false;
        }

        string runId = values.GetValueOrDefault("--run-id") ??
            $"run-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}"[..40];
        if (!ValidRunId.IsMatch(runId))
        {
            error = "--run-id must be 1-64 ASCII letters, digits, dot, underscore, or hyphen and start with a letter or digit.";
            return false;
        }

        try
        {
            string outputBase = Path.GetFullPath(
                values.GetValueOrDefault("--output-directory") ??
                Path.Combine(repositoryRoot, "artifacts", "p2.5-long-run"));
            if (File.Exists(outputBase))
            {
                error = $"Output directory is an existing file: {outputBase}";
                return false;
            }
            string runDirectory = Path.Combine(outputBase, runId);
            if (Directory.Exists(runDirectory) || File.Exists(runDirectory))
            {
                error = $"Run output already exists; overwrite is forbidden: {runDirectory}";
                return false;
            }

            string summary = Path.GetFullPath(
                values.GetValueOrDefault("--summary-json") ??
                Path.Combine(runDirectory, "summary.json"));
            string snapshots = Path.GetFullPath(
                values.GetValueOrDefault("--snapshots-jsonl") ??
                Path.Combine(runDirectory, "snapshots.jsonl"));
            if (string.Equals(summary, snapshots, StringComparison.OrdinalIgnoreCase))
            {
                error = "--summary-json and --snapshots-jsonl must be different files.";
                return false;
            }
            if (File.Exists(summary) || Directory.Exists(summary) ||
                File.Exists(snapshots) || Directory.Exists(snapshots))
            {
                error = "Evidence output already exists; overwrite is forbidden.";
                return false;
            }
            if (!RepositoryFacts.IsEvidencePathGitSafe(repositoryRoot, runDirectory) ||
                !RepositoryFacts.IsEvidencePathGitSafe(repositoryRoot, summary) ||
                !RepositoryFacts.IsEvidencePathGitSafe(repositoryRoot, snapshots))
            {
                error = "Evidence paths inside the repository must be Git-ignored.";
                return false;
            }

            string prospectiveMp4 = Path.Combine(
                runDirectory,
                "p2.5a-recordings",
                "00000000-0000-0000-0000-000000000000.mp4");
            if (prospectiveMp4.Length >= 260)
            {
                error = $"Output path is too long for the current Native ABI ({prospectiveMp4.Length} characters).";
                return false;
            }

            options = new LongRunOptions(
                duration,
                interval,
                outputBase,
                runId,
                runDirectory,
                summary,
                snapshots,
                cancelAfter);
            return true;
        }
        catch (Exception pathError) when (
            pathError is ArgumentException or NotSupportedException or PathTooLongException)
        {
            error = $"Invalid output path: {pathError.Message}";
            return false;
        }
    }

    private static bool TryPositiveInt(
        IReadOnlyDictionary<string, string> values,
        string name,
        bool required,
        out int value,
        out string error)
    {
        value = 0;
        error = string.Empty;
        if (!values.TryGetValue(name, out string? text))
        {
            if (required)
            {
                error = $"{name} is required.";
                return false;
            }
            return true;
        }
        if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value) || value <= 0)
        {
            error = $"{name} must be a positive integer.";
            return false;
        }
        return true;
    }

    private static bool TryOptionalPositiveInt(
        IReadOnlyDictionary<string, string> values,
        string name,
        out int? value,
        out string error)
    {
        value = null;
        error = string.Empty;
        if (!values.TryGetValue(name, out string? text))
        {
            return true;
        }
        if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int parsed) || parsed <= 0)
        {
            error = $"{name} must be a positive integer.";
            return false;
        }
        value = parsed;
        return true;
    }
}

internal static class LongRunOptionsTests
{
    internal static int Run(string repositoryRoot)
    {
        string unique = $"args-{Guid.NewGuid():N}";
        string output = Path.Combine(repositoryRoot, "artifacts", "p2.5-long-run-parser-tests");
        ExpectSuccess(["--duration-seconds", "15", "--run-id", unique, "--output-directory", output], repositoryRoot);
        ExpectSuccess(["--duration-seconds", "15", "--sample-interval-ms", "250", "--run-id", unique + "-2", "--output-directory", output], repositoryRoot);
        ExpectFailure([], repositoryRoot);
        ExpectFailure(["--duration-seconds", "0"], repositoryRoot);
        ExpectFailure(["--duration-seconds", "-1"], repositoryRoot);
        ExpectFailure(["--duration-seconds", "abc"], repositoryRoot);
        ExpectFailure(["--duration-seconds", "999999999999999999999"], repositoryRoot);
        ExpectFailure(["--duration-seconds", "15", "--unknown", "x"], repositoryRoot);
        ExpectFailure(["--duration-seconds", "15", "--duration-seconds", "16"], repositoryRoot);
        ExpectFailure(["--duration-seconds", "15", "--run-id", "../escape"], repositoryRoot);
        ExpectFailure(["--duration-seconds", "15", "--cancel-after-seconds", "15"], repositoryRoot);
        ExpectFailure(["--duration-seconds", "15", "--run-id", unique + "-unsafe", "--output-directory", Path.Combine(repositoryRoot, "long-run-unignored")], repositoryRoot);
        if (!LongRunOptions.TryParse(
                ["--duration-seconds", "15", "--run-id", unique + "-relative", "--output-directory", "artifacts\\p2.5-relative-test"],
                repositoryRoot,
                out LongRunOptions? relative,
                out string relativeError) ||
            relative is null ||
            !Path.IsPathFullyQualified(relative.RunDirectory) ||
            !Path.IsPathFullyQualified(relative.SummaryJsonPath) ||
            !Path.IsPathFullyQualified(relative.SnapshotsJsonlPath))
        {
            throw new InvalidOperationException(
                $"Relative paths were not normalized to absolute paths: {relativeError}");
        }
        string existingRun = unique + "-existing";
        string existingPath = Path.Combine(output, existingRun);
        Directory.CreateDirectory(existingPath);
        try
        {
            ExpectFailure(["--duration-seconds", "15", "--run-id", existingRun, "--output-directory", output], repositoryRoot);
        }
        finally
        {
            Directory.Delete(existingPath);
        }
        Console.WriteLine("LONG-RUN-ARGUMENT-TESTS: PASS (14 cases)");
        return 0;
    }

    private static void ExpectSuccess(string[] args, string root)
    {
        if (!LongRunOptions.TryParse(args, root, out _, out string error))
        {
            throw new InvalidOperationException($"Expected parse success: {error}");
        }
    }

    private static void ExpectFailure(string[] args, string root)
    {
        if (LongRunOptions.TryParse(args, root, out _, out _))
        {
            throw new InvalidOperationException("Expected parse failure.");
        }
    }
}

internal static class LongRunEvidenceGateTests
{
    private static readonly ProcessMetrics Metrics = new(
        DateTimeOffset.UtcNow,
        "test",
        true,
        100,
        200,
        10,
        4,
        string.Empty);

    internal static int Run(string repositoryRoot)
    {
        string root = Path.Combine(
            repositoryRoot,
            "artifacts",
            "p2.5-long-run-gate-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string mp4 = Path.Combine(root, "valid.mp4");
            File.WriteAllBytes(mp4, [1, 2, 3, 4]);
            ManagedRecordingSnapshot terminal = ValidTerminal(mp4);
            RunObservations observations = ValidObservations(terminal);
            EvidenceFileValidation jsonl = CreateValidJsonl(
                root,
                terminal);
            GitWorkspaceFacts clean = new(
                "0123456789012345678901234567890123456789",
                "test/p2.5-long-run-harness",
                string.Empty,
                true);
            GitReproducibility git = GitReproducibility.Compare(
                clean,
                clean,
                null,
                evidencePathsGitSafe: true);
            LoadedModuleEvidence modules = new(
            [
                Module("XbPreview.LongRun.exe"),
                Module("XbPreview.Host.dll"),
                Module("XbPreview.Native.dll"),
            ], true, string.Empty);
            RelatedProcessEvidence related = new(
                DateTimeOffset.UtcNow,
                [],
                true,
                string.Empty);

            ExpectPass(Evaluate(
                false,
                15,
                15.01,
                terminal,
                true,
                "PASS",
                jsonl,
                observations,
                true,
                related,
                modules,
                git,
                Metrics,
                Metrics));
            ExpectBlocked(Evaluate(false, 15, 14.99, terminal, true, "PASS", jsonl, observations, true, related, modules, git, Metrics, Metrics));
            ExpectBlocked(Evaluate(false, 15, 15.01, terminal with { SessionId = string.Empty }, true, "PASS", jsonl, observations, true, related, modules, git, Metrics, Metrics));
            ExpectBlocked(Evaluate(false, 15, 15.01, terminal with { SessionId = "not-a-guid" }, true, "PASS", jsonl, observations, true, related, modules, git, Metrics, Metrics));
            RunObservations changedSession = new();
            changedSession.Add(
                terminal with
                {
                    State = ManagedRecordingState.Recording,
                    SessionId = Guid.NewGuid().ToString("D"),
                    Elapsed = TimeSpan.FromSeconds(1),
                },
                Metrics);
            changedSession.Add(terminal, Metrics);
            ExpectBlocked(Evaluate(false, 15, 15.01, terminal, true, "PASS", jsonl, changedSession, true, related, modules, git, Metrics, Metrics));
            ExpectBlocked(Evaluate(false, 15, 15.01, terminal with { Elapsed = TimeSpan.Zero }, true, "PASS", jsonl, observations, true, related, modules, git, Metrics, Metrics));

            RunObservations regressed = new();
            regressed.Add(terminal with { State = ManagedRecordingState.Recording, Elapsed = TimeSpan.FromSeconds(3) }, Metrics);
            regressed.Add(terminal with { State = ManagedRecordingState.Recording, Elapsed = TimeSpan.FromSeconds(2) }, Metrics);
            regressed.Add(terminal, Metrics);
            ExpectBlocked(Evaluate(false, 15, 15.01, terminal, true, "PASS", jsonl, regressed, true, related, modules, git, Metrics, Metrics));

            RunObservations illegalState = new();
            illegalState.Add(terminal with { State = ManagedRecordingState.Recording }, Metrics);
            illegalState.Add(terminal with { State = ManagedRecordingState.Idle }, Metrics);
            illegalState.Add(terminal, Metrics);
            ExpectBlocked(Evaluate(false, 15, 15.01, terminal, true, "PASS", jsonl, illegalState, true, related, modules, git, Metrics, Metrics));

            ExpectBlocked(Evaluate(false, 15, 15.01, terminal with { PublishedPath = Path.Combine(root, "missing.mp4") }, true, "PASS", jsonl, observations, true, related, modules, git, Metrics, Metrics));
            string zero = Path.Combine(root, "zero.mp4");
            using (File.Create(zero)) { }
            ExpectBlocked(Evaluate(false, 15, 15.01, terminal with { PublishedPath = zero }, true, "PASS", jsonl, observations, true, related, modules, git, Metrics, Metrics));
            ExpectBlocked(Evaluate(false, 15, 15.01, terminal with { Published = false }, true, "PASS", jsonl, observations, true, related, modules, git, Metrics, Metrics));
            ExpectBlocked(Evaluate(false, 15, 15.01, terminal with { PublishedPath = string.Empty }, true, "PASS", jsonl, observations, true, related, modules, git, Metrics, Metrics));

            EvidenceFileValidation unclosed = EvidenceWriter.ValidateJsonl(
                Path.Combine(root, "valid-snapshots.jsonl"),
                2,
                writerClosed: false);
            ExpectBlocked(Evaluate(false, 15, 15.01, terminal, true, "PASS", unclosed, observations, true, related, modules, git, Metrics, Metrics));
            string invalidJsonl = Path.Combine(root, "invalid.jsonl");
            File.WriteAllText(invalidJsonl, "{not-json}");
            EvidenceFileValidation invalid = EvidenceWriter.ValidateJsonl(
                invalidJsonl,
                1,
                writerClosed: true);
            ExpectBlocked(Evaluate(false, 15, 15.01, terminal, true, "PASS", invalid, observations, true, related, modules, git, Metrics, Metrics));

            ExpectBlocked(Evaluate(false, 15, 15.01, terminal, false, "PASS", jsonl, observations, true, related, modules, git, Metrics, Metrics));
            GitWorkspaceFacts dirty = clean with
            {
                StatusPorcelainV1 = " M changed.cs\n",
                Clean = false,
            };
            GitReproducibility dirtyGit = GitReproducibility.Compare(
                dirty,
                dirty,
                null,
                evidencePathsGitSafe: true);
            ExpectBlocked(Evaluate(false, 15, 15.01, terminal, true, "PASS", jsonl, observations, true, related, modules, dirtyGit, Metrics, Metrics));
            ExpectBlocked(Evaluate(false, 15, 15.01, terminal, true, "PASS", jsonl, observations, true, related, new LoadedModuleEvidence([], false, "mismatch"), git, Metrics, Metrics));
            ExpectBlocked(Evaluate(true, 15, 2.0, terminal with { OutputSuccess = false }, true, "PASS", jsonl, observations, true, related, modules, git, Metrics, Metrics));

            RunObservations resources = new();
            ProcessMetrics start = Metrics with { WorkingSet = 300, Phase = "baseline" };
            ProcessMetrics running = Metrics with { WorkingSet = 100, Phase = "running" };
            ProcessMetrics end = Metrics with { WorkingSet = 500, Phase = "final" };
            resources.AddProcessMetrics(start);
            resources.Add(terminal, running);
            resources.AddProcessMetrics(end);
            Require(
                resources.CalculateMaximum()?.WorkingSet == 500,
                "Resource maximum must include baseline, running, and final samples.");

            string existingSummary = Path.Combine(root, "existing-summary.json");
            File.WriteAllText(existingSummary, "existing");
            AtomicSummaryWriteResult rejected =
                EvidenceWriter.PublishSummaryAtomically(
                    existingSummary,
                    new LongRunSummary());
            Require(!rejected.Passed, "Existing summary must not be overwritten.");
            using StringWriter capturedOutput = new();
            using StringWriter capturedError = new();
            LongRunExitCode publishFailure = LongRunResultPublisher.Publish(
                existingSummary,
                new LongRunSummary { Verdict = "PASS" },
                LongRunExitCode.Pass,
                capturedOutput,
                capturedError);
            Require(
                publishFailure == LongRunExitCode.SummaryPublishFailed &&
                !capturedOutput.ToString().Contains(
                    "LONG-RUN-RESULT: PASS",
                    StringComparison.Ordinal) &&
                capturedError.ToString().Contains(
                    "LONG-RUN-RESULT: BLOCKED",
                    StringComparison.Ordinal),
                "Summary publication failure must return nonzero and never print PASS.");
            string atomicSummary = Path.Combine(root, "atomic-summary.json");
            AtomicSummaryWriteResult published =
                EvidenceWriter.PublishSummaryAtomically(
                    atomicSummary,
                    new LongRunSummary { Verdict = "BLOCKED" });
            Require(
                !published.Passed && !File.Exists(atomicSummary),
                "Incomplete summary schema must be rejected before publication.");

            Console.WriteLine(
                "LONG-RUN-EVIDENCE-GATE-TESTS: PASS (20 focused cases)");
            return 0;
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static GateEvaluation Evaluate(
        bool canceled,
        int durationSeconds,
        double wallSeconds,
        ManagedRecordingSnapshot terminal,
        bool terminalReread,
        string sourceReader,
        EvidenceFileValidation jsonl,
        RunObservations observations,
        bool previewClosed,
        RelatedProcessEvidence related,
        LoadedModuleEvidence modules,
        GitReproducibility git,
        ProcessMetrics start,
        ProcessMetrics end) =>
        GateEvaluation.Create(
            canceled,
            durationSeconds,
            wallSeconds,
            terminal,
            terminalReread,
            sourceReader,
            jsonl,
            observations,
            previewClosed
                ? new PreviewCloseEvidence(
                    true,
                    PreviewLifecycleState.Disposed,
                    string.Empty,
                    string.Empty)
                : PreviewCloseEvidence.NotAttempted,
            related,
            related,
            related,
            modules,
            git,
            start,
            end);

    private static ManagedRecordingSnapshot ValidTerminal(string path) => new(
        ManagedRecordingState.Completed,
        NativeMethods.Result.Ok,
        DateTimeOffset.UtcNow.AddSeconds(-15),
        TimeSpan.FromSeconds(15),
        Guid.NewGuid().ToString("D"),
        Path.GetFullPath(path + ".partial.mp4"),
        string.Empty,
        true,
        true,
        0,
        0,
        1,
        false,
        0,
        false,
        false,
        0,
        900,
        0,
        TimeSpan.Zero)
    {
        WorkingPath = Path.GetFullPath(path + ".partial.mp4"),
        PlannedFinalPath = Path.GetFullPath(path),
        PublishedPath = Path.GetFullPath(path),
        ReadyToPublish = true,
        Published = true,
        PublishAttempted = true,
        PublishHResult = 0,
        ValidationAttempted = true,
        ValidationHResult = 0,
    };

    private static BinaryFingerprint Module(string name) => new(
        name,
        "focused gate test",
        Path.GetFullPath(Path.Combine("artifacts", name)),
        1,
        DateTime.UtcNow,
        new string('0', 64),
        true);

    private static RunObservations ValidObservations(
        ManagedRecordingSnapshot terminal)
    {
        RunObservations observations = new();
        observations.Add(
            terminal with
            {
                State = ManagedRecordingState.Recording,
                Elapsed = TimeSpan.FromSeconds(1),
                OutputSuccess = false,
                FinalizeAttempted = false,
                FinalizeCount = 0,
                ActiveEncoder = true,
                ReadyToPublish = false,
                Published = false,
                PublishAttempted = false,
                PublishedPath = string.Empty,
            },
            Metrics);
        observations.Add(terminal, Metrics);
        return observations;
    }

    private static EvidenceFileValidation CreateValidJsonl(
        string root,
        ManagedRecordingSnapshot terminal)
    {
        string path = Path.Combine(root, "valid-snapshots.jsonl");
        using EvidenceWriter writer = new(path);
        writer.WriteSample(Sample(
            "periodic",
            terminal with
            {
                State = ManagedRecordingState.Recording,
                Elapsed = TimeSpan.FromSeconds(1),
            }));
        writer.WriteSample(Sample("terminal", terminal));
        return writer.CloseAndValidate(2);
    }

    private static LongRunSample Sample(
        string sampleType,
        ManagedRecordingSnapshot snapshot) => new(
            sampleType,
            DateTimeOffset.UtcNow,
            snapshot.Elapsed.TotalSeconds,
            snapshot.State.ToString(),
            snapshot.SessionId,
            snapshot.Elapsed.Ticks,
            snapshot.OutputPath,
            snapshot.FramesSubmitted,
            snapshot.ActiveEncoder,
            snapshot.FinalizeAttempted,
            snapshot.FinalizeHResult,
            snapshot.FinalizeCount,
            snapshot.OutputSuccess,
            snapshot.FailureHResult,
            snapshot.OutputCleanupAttempted,
            snapshot.OutputCleanupSucceeded,
            snapshot.OutputCleanupHResult,
            snapshot.ResidualOutstanding,
            "Previewing",
            Metrics.WorkingSet,
            Metrics.PrivateMemorySize,
            Metrics.HandleCount,
            Metrics.ThreadCount);

    private static void ExpectPass(GateEvaluation evaluation) =>
        Require(evaluation.Passed, "Expected gate evaluation to PASS.");

    private static void ExpectBlocked(GateEvaluation evaluation) =>
        Require(!evaluation.Passed, "Expected gate evaluation to BLOCK.");

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
