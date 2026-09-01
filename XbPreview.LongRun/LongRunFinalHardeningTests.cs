using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using XbPreview.Host;

namespace XbPreview.LongRun;

internal static class LongRunFinalHardeningTests
{
    internal static async Task<int> RunAsync(string repositoryRoot)
    {
        string root = Path.Combine(
            repositoryRoot,
            "artifacts",
            "p2.5-long-run-final-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        int caseCount = 0;
        try
        {
            GitReproducibility git = TestRealGitCapture(root);
            caseCount++;
            LoadedModuleEvidence modules = TestRealLoadedModules(root);
            caseCount += 2;
            caseCount += TestJsonlSchema(root);
            caseCount += TestSummaryPublication(root, modules, git);
            caseCount += TestPreviewCloseEvidence(root, modules, git);
            caseCount += await TestEndReasonConcurrencyAsync();

        }
        finally
        {
            DeleteTestRoot(root);
        }
        Console.WriteLine(
            $"LONG-RUN-FINAL-HARDENING-TESTS: PASS ({caseCount} cases)");
        return 0;
    }

    private static GitReproducibility TestRealGitCapture(string root)
    {
        string gitRoot = Path.Combine(root, "git-capture");
        Directory.CreateDirectory(gitRoot);
        RunGit(gitRoot, "init", "--quiet");
        string tracked = Path.Combine(gitRoot, "tracked.txt");
        File.WriteAllText(
            tracked,
            "baseline\n",
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        RunGit(gitRoot, "add", "--", "tracked.txt");
        RunGit(
            gitRoot,
            "-c", "user.name=XbPreview LongRun Test",
            "-c", "user.email=longrun-test@invalid.example",
            "commit", "--quiet", "-m", "baseline");

        GitWorkspaceFacts cleanStart = GitWorkspaceFacts.Capture(gitRoot);
        Require(cleanStart.Clean, "Temporary Git baseline must be clean.");
        File.WriteAllText(
            tracked,
            "changed\n",
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        GitWorkspaceFacts dirtyEnd = GitWorkspaceFacts.Capture(gitRoot);
        GitReproducibility changed = GitReproducibility.Compare(
            cleanStart,
            dirtyEnd,
            captureError: null,
            evidencePathsGitSafe: true);
        Require(
            !dirtyEnd.Clean &&
            !changed.StatusByteForByteUnchanged &&
            !changed.Passed,
            "A real tracked-file Git change must block reproducibility.");

        File.WriteAllText(
            tracked,
            "baseline\n",
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        GitWorkspaceFacts cleanEnd = GitWorkspaceFacts.Capture(gitRoot);
        GitReproducibility restored = GitReproducibility.Compare(
            cleanStart,
            cleanEnd,
            captureError: null,
            evidencePathsGitSafe: true);
        Require(
            cleanEnd.Clean && restored.Passed,
            "Restored temporary Git facts must compare cleanly.");
        return restored;
    }

    private static LoadedModuleEvidence TestRealLoadedModules(string root)
    {
        string expectedDirectory = Path.GetFullPath(AppContext.BaseDirectory)
            .TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);
        string nativePath = Path.Combine(
            expectedDirectory,
            "XbPreview.Native.dll");
        Require(
            File.Exists(nativePath),
            $"Native test module is missing: {nativePath}");
        nint native = NativeLibrary.Load(nativePath);
        try
        {
            LoadedModuleEvidence current =
                LoadedModuleEvidence.Capture(expectedDirectory);
            Require(
                current.Complete &&
                current.Modules.Count == 3 &&
                current.Modules.All(module =>
                    module.FromExpectedReleaseX64Directory &&
                    File.Exists(module.Path) &&
                    module.Size > 0 &&
                    module.Sha256.Length == 64),
                $"Current loaded-module capture must be complete: {current.Error}");

            string wrongExpectedRoot = Path.Combine(
                root,
                "deliberately-wrong-module-root");
            LoadedModuleEvidence wrong =
                LoadedModuleEvidence.Capture(wrongExpectedRoot);
            Require(
                !wrong.Complete &&
                wrong.Modules.Count == 3 &&
                wrong.Modules.All(module =>
                    !module.FromExpectedReleaseX64Directory) &&
                wrong.Error.Contains(
                    "outside expected Release x64 directory",
                    StringComparison.Ordinal),
                "A real current-module capture against the wrong expected root must fail.");
            return current;
        }
        finally
        {
            NativeLibrary.Free(native);
        }
    }

    private static int TestJsonlSchema(string root)
    {
        string directory = Path.Combine(root, "jsonl-schema");
        Directory.CreateDirectory(directory);
        string session = Guid.NewGuid().ToString("D");
        string output = Path.GetFullPath(Path.Combine(directory, "output.mp4"));
        LongRunSample periodic = ValidPeriodicSample(session, output);
        LongRunSample terminal = ValidTerminalSample(session, output);

        string validPath = Path.Combine(directory, "valid.jsonl");
        using (EvidenceWriter writer = new(validPath))
        {
            writer.WriteSample(periodic);
            writer.WriteSample(terminal);
            EvidenceFileValidation valid = writer.CloseAndValidate(2);
            Require(valid.Passed, $"Valid JSONL must pass: {valid.Error}");
        }

        JsonObject missing = ToJsonObject(terminal);
        missing.Remove("framesSubmitted");
        ExpectInvalidJsonl(
            Path.Combine(directory, "missing-field.jsonl"),
            [ToJsonObject(periodic), missing]);

        JsonObject wrongType = ToJsonObject(terminal);
        wrongType["nativePtsElapsed100ns"] = "20000000";
        ExpectInvalidJsonl(
            Path.Combine(directory, "wrong-type.jsonl"),
            [ToJsonObject(periodic), wrongType]);

        JsonObject invalidSession = ToJsonObject(periodic);
        invalidSession["sessionGuid"] = "not-a-guid";
        ExpectInvalidJsonl(
            Path.Combine(directory, "invalid-session.jsonl"),
            [invalidSession, ToJsonObject(terminal)]);

        LongRunSample changedTerminal = terminal with
        {
            SessionGuid = Guid.NewGuid().ToString("D"),
        };
        ExpectInvalidJsonl(
            Path.Combine(directory, "changed-session.jsonl"),
            [ToJsonObject(periodic), ToJsonObject(changedTerminal)]);

        string otherSession = Guid.NewGuid().ToString("D");
        ExpectInvalidJsonl(
            Path.Combine(directory, "wrong-run-session.jsonl"),
            [
                ToJsonObject(periodic with { SessionGuid = otherSession }),
                ToJsonObject(terminal with { SessionGuid = otherSession }),
            ],
            expectedSessionGuid: session);

        ExpectInvalidJsonl(
            Path.Combine(directory, "missing-terminal.jsonl"),
            [ToJsonObject(periodic)]);
        ExpectInvalidJsonl(
            Path.Combine(directory, "duplicate-terminal.jsonl"),
            [
                ToJsonObject(periodic),
                ToJsonObject(terminal),
                ToJsonObject(terminal),
            ]);
        ExpectInvalidJsonl(
            Path.Combine(directory, "sample-after-terminal.jsonl"),
            [ToJsonObject(terminal), ToJsonObject(periodic)]);

        JsonObject nonterminalTerminal = ToJsonObject(terminal);
        nonterminalTerminal["recordingState"] = "Recording";
        ExpectInvalidJsonl(
            Path.Combine(directory, "terminal-state.jsonl"),
            [ToJsonObject(periodic), nonterminalTerminal]);
        return 10;
    }

    private static int TestSummaryPublication(
        string root,
        LoadedModuleEvidence modules,
        GitReproducibility git)
    {
        string directory = Path.Combine(root, "summary-publication");
        Directory.CreateDirectory(directory);
        LongRunSummary summary = ValidSummary(
            Path.Combine(directory, "schema-source"),
            modules,
            git);

        JsonObject missingSummaryField = JsonSerializer.SerializeToNode(
            summary,
            LongRunEvidenceSchema.JsonOptions)!.AsObject();
        missingSummaryField.Remove("loadedModules");
        using (JsonDocument missingDocument = JsonDocument.Parse(
                   missingSummaryField.ToJsonString(
                       LongRunEvidenceSchema.JsonOptions)))
        {
            Require(
                !LongRunEvidenceSchema.TryDeserializeSummary(
                    missingDocument.RootElement,
                    out _,
                    out _),
                "The formal Summary parser must reject a missing required field.");
        }

        string mismatchDirectory = Path.Combine(
            directory,
            "verdict-mismatch");
        LongRunSummary mismatchedVerdict = ValidSummary(
            mismatchDirectory,
            modules,
            git);
        mismatchedVerdict.ExitCode = (int)LongRunExitCode.OutputValidationFailed;
        ExpectPublishFailure(
            mismatchedVerdict.Parameters!.SummaryJsonPath,
            mismatchedVerdict,
            PhysicalLongRunFileOperations.Instance,
            expectedInvalidPublication: false);

        LongRunSummary contradictory = ValidSummary(
            Path.Combine(directory, "contradictory-pass"),
            modules,
            git);
        contradictory.TerminalSnapshot =
            contradictory.TerminalSnapshot! with { FinalizeCount = 2 };
        ExpectPublishFailure(
            contradictory.Parameters!.SummaryJsonPath,
            contradictory,
            PhysicalLongRunFileOperations.Instance,
            expectedInvalidPublication: false);

        string parentFile = Path.Combine(directory, "ordinary-parent-file");
        File.WriteAllText(parentFile, "not a directory");
        LongRunSummary invalidParent = ValidSummary(
            Path.Combine(directory, "ordinary-parent-source"),
            modules,
            git,
            Path.Combine(parentFile, "summary.json"));
        ExpectPublishFailure(
            invalidParent.Parameters!.SummaryJsonPath,
            invalidParent,
            PhysicalLongRunFileOperations.Instance,
            expectedInvalidPublication: false);

        string existingDirectory = Path.Combine(directory, "existing-target");
        LongRunSummary existingSummary = ValidSummary(
            existingDirectory,
            modules,
            git);
        string existing = existingSummary.Parameters!.SummaryJsonPath;
        byte[] original = Encoding.UTF8.GetBytes("existing-summary");
        File.WriteAllBytes(existing, original);
        ExpectPublishFailure(
            existing,
            existingSummary,
            PhysicalLongRunFileOperations.Instance,
            expectedInvalidPublication: false);
        Require(
            File.ReadAllBytes(existing).SequenceEqual(original),
            "Existing summary bytes must never be overwritten.");

        string successDirectory = Path.Combine(directory, "success");
        LongRunSummary successSummary = ValidSummary(
            successDirectory,
            modules,
            git);
        string success = successSummary.Parameters!.SummaryJsonPath;
        using (StringWriter output = new())
        using (StringWriter error = new())
        {
            LongRunExitCode result = LongRunResultPublisher.Publish(
                success,
                successSummary,
                LongRunExitCode.Pass,
                output,
                error);
            Require(
                result == LongRunExitCode.Pass &&
                File.Exists(success) &&
                output.ToString().Contains(
                    "LONG-RUN-RESULT: PASS; exit=0",
                    StringComparison.Ordinal) &&
                error.ToString().Length == 0,
                "Successful publication must read back before PASS/exit 0. " +
                    $"result={result}; output={output}; error={error}");
            using JsonDocument document = JsonDocument.Parse(
                File.ReadAllText(success, Encoding.UTF8));
            Require(
                LongRunEvidenceSchema.TryDeserializeSummary(
                    document.RootElement,
                    out _,
                    out string schemaError),
                $"Published summary must pass the formal parser: {schemaError}");
        }

        LongRunSummary sealedCancellationSummary = ValidSummary(
            Path.Combine(directory, "sealed-cancellation-success"),
            modules,
            git);
        sealedCancellationSummary.CancellationObserved = true;
        sealedCancellationSummary.ExitCode =
            (int)LongRunExitCode.CanceledSafely;
        sealedCancellationSummary.Verdict = "CANCELED-SAFELY";
        using (StringWriter output = new())
        using (StringWriter error = new())
        {
            LongRunExitCode result = LongRunResultPublisher.Publish(
                sealedCancellationSummary.Parameters!.SummaryJsonPath,
                sealedCancellationSummary,
                LongRunExitCode.CanceledSafely,
                output,
                error);
            Require(
                result == LongRunExitCode.CanceledSafely &&
                output.ToString().Contains(
                    "LONG-RUN-RESULT: CANCELED-SAFELY; exit=9",
                    StringComparison.Ordinal) &&
                error.ToString().Length == 0,
                "A cancellation observed before termination Seal must publish CANCELED-SAFELY while preserving Duration as the first end reason.");
        }

        LongRunSummary outputFailureSummary = ValidSummary(
            Path.Combine(directory, "result-output-failure"),
            modules,
            git);
        using (ThrowBeforeWriteTextWriter output = new())
        using (StringWriter error = new())
        {
            LongRunExitCode result = LongRunResultPublisher.Publish(
                outputFailureSummary.Parameters!.SummaryJsonPath,
                outputFailureSummary,
                LongRunExitCode.Pass,
                output,
                error);
            Require(
                result == LongRunExitCode.SummaryPublishFailed &&
                output.AttemptCount == 1 &&
                !File.Exists(outputFailureSummary.Parameters.SummaryJsonPath) &&
                error.ToString().Contains(
                    "LONG-RUN-RESULT: BLOCKED",
                    StringComparison.Ordinal),
                "A final result-output failure must quarantine the summary and return BLOCKED.");
        }

        foreach (InjectedFileFault fault in new[]
                 {
                     InjectedFileFault.Write,
                     InjectedFileFault.Flush,
                     InjectedFileFault.TemporaryReadBack,
                     InjectedFileFault.Move,
                     InjectedFileFault.MoveAfterSuccess,
                     InjectedFileFault.FinalReadBack,
                 })
        {
            string caseDirectory = Path.Combine(
                directory,
                "fault-" + fault.ToString().ToLowerInvariant());
            LongRunSummary faultSummary = ValidSummary(
                caseDirectory,
                modules,
                git);
            string final = faultSummary.Parameters!.SummaryJsonPath;
            ExpectPublishFailure(
                final,
                faultSummary,
                new FaultingFileOperations(fault),
                expectedInvalidPublication:
                    fault is InjectedFileFault.MoveAfterSuccess or
                        InjectedFileFault.FinalReadBack);
        }
        return 14;
    }

    private static int TestPreviewCloseEvidence(
        string root,
        LoadedModuleEvidence modules,
        GitReproducibility git)
    {
        PreviewCloseEvidence passed = new(
            true,
            PreviewLifecycleState.Disposed,
            string.Empty,
            string.Empty);
        Require(passed.Passed, "Disposed with no LastError must pass close evidence.");
        Require(
            !new PreviewCloseEvidence(
                true,
                PreviewLifecycleState.Disposed,
                "native stop failed",
                string.Empty).Passed,
            "Disposed with LastError must not pass close evidence.");
        Require(
            !new PreviewCloseEvidence(
                true,
                PreviewLifecycleState.Disposed,
                string.Empty,
                "IOException: close failed").Passed,
            "Disposed with a close exception must not pass close evidence.");
        Require(
            !new PreviewCloseEvidence(
                false,
                PreviewLifecycleState.Disposed,
                string.Empty,
                string.Empty).Passed,
            "A noncompleted Close invocation must not pass close evidence.");

        LongRunSummary summary = ValidSummary(
            Path.Combine(root, "preview-close-gate"),
            modules,
            git);
        ManagedRecordingSnapshot terminal = summary.TerminalSnapshot!;
        ProcessMetrics metrics = summary.ProcessStart!;
        RelatedProcessEvidence related = summary.RelatedProcessesAtEnd!;
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
            metrics);
        observations.Add(terminal, metrics);
        PreviewCloseEvidence disposedWithError = new(
            true,
            PreviewLifecycleState.Disposed,
            "native stop failed",
            string.Empty);
        GateEvaluation gate = GateEvaluation.Create(
            canceled: false,
            durationSeconds: 2,
            actualWallSeconds: 2.0,
            terminal,
            terminalSnapshotReadAfterStop: true,
            sourceReaderValidation: "PASS",
            summary.SnapshotsJsonl!,
            observations,
            disposedWithError,
            related,
            related,
            related,
            modules,
            git,
            metrics,
            metrics);
        GateResult closeGate = gate.Gates.Single(
            item => item.Name == "preview-closed");
        Require(
            !closeGate.Passed &&
            !gate.Passed &&
            LongRunRunner.ResolveExitCode(
                LongRunExitCode.Pass,
                gate.Passed,
                LongRunEndReason.DurationReached,
                runtimeFailureObserved: false) != LongRunExitCode.Pass,
            "Disposed + LastError must fail the formal close Gate and final verdict.");
        return 5;
    }

    private static async Task<int> TestEndReasonConcurrencyAsync()
    {
        int cases = 0;
        foreach (LongRunStage stage in new[]
                 {
                     LongRunStage.RecordingStarted,
                     LongRunStage.TimedRunCompleted,
                     LongRunStage.StopStarted,
                     LongRunStage.StopCompleted,
                     LongRunStage.TerminalSnapshotReadStarted,
                     LongRunStage.TerminalSnapshotRead,
                     LongRunStage.PreviewCloseStarted,
                     LongRunStage.PreviewCloseCompleted,
                 })
        {
            using CancellationTokenSource cancellation = new();
            using ManualResetEventSlim stageReached = new(false);
            using ManualResetEventSlim releaseStage = new(false);
            using LongRunTerminationCoordinator coordinator = new(
                cancellation.Token,
                observed =>
                {
                    if (observed == stage)
                    {
                        stageReached.Set();
                        releaseStage.Wait();
                    }
                });
            Task stageFlow = Task.Run(() => coordinator.NotifyStage(stage));
            try
            {
                Require(
                    stageReached.Wait(TimeSpan.FromSeconds(2)),
                    $"Production stage barrier was not reached for {stage}.");
                await Task.Run(cancellation.Cancel).WaitAsync(
                    TimeSpan.FromSeconds(2));
                Require(
                    SpinWait.SpinUntil(
                        () => coordinator.CancellationObserved,
                        TimeSpan.FromSeconds(2)),
                    $"Production cancellation registration did not observe {stage} cancellation.");
            }
            finally
            {
                releaseStage.Set();
            }
            await stageFlow.WaitAsync(TimeSpan.FromSeconds(2));
            LongRunExitCode result = LongRunRunner.ResolveExitCode(
                LongRunExitCode.Pass,
                gatesPassed: true,
                coordinator.EndReason,
                coordinator.RuntimeFailureObserved,
                coordinator.CancellationObserved);
            Require(
                coordinator.EndReason ==
                    LongRunEndReason.CancellationRequested &&
                coordinator.CancellationObserved &&
                !coordinator.MarkDurationReached() &&
                result == LongRunExitCode.CanceledSafely &&
                result != LongRunExitCode.Pass,
                $"Cancellation that wins at production stage {stage} must survive later duration/cleanup and never become PASS.");
            cases++;
        }

        using CancellationTokenSource lateCancellation = new();
        using LongRunTerminationCoordinator duration = new(
            lateCancellation.Token,
            stage =>
            {
                if (stage == LongRunStage.PreviewCloseStarted)
                {
                    lateCancellation.Cancel();
                }
            });
        bool durationWon = duration.CompleteTimedRun();
        duration.NotifyStage(LongRunStage.PreviewCloseStarted);
        lateCancellation.Cancel();
        Require(
            durationWon &&
            duration.EndReason == LongRunEndReason.DurationReached &&
            duration.CancellationObserved &&
            LongRunRunner.ResolveExitCode(
                LongRunExitCode.Pass,
                gatesPassed: true,
                duration.EndReason,
                duration.RuntimeFailureObserved,
                duration.CancellationObserved) ==
                    LongRunExitCode.CanceledSafely,
            "A cancellation observed during cleanup must never be published as ordinary PASS, even when Duration remains the first end reason.");
        cases++;

        using LongRunTerminationCoordinator runtimeFailure = new(
            CancellationToken.None);
        runtimeFailure.MarkRuntimeFailure();
        Require(
            runtimeFailure.RuntimeFailureObserved &&
            runtimeFailure.EndReason == LongRunEndReason.RuntimeFailure &&
            LongRunRunner.ResolveExitCode(
                LongRunExitCode.Pass,
                gatesPassed: true,
                runtimeFailure.EndReason,
                runtimeFailure.RuntimeFailureObserved) != LongRunExitCode.Pass,
            "A runtime failure latched first must not be relabeled as cancellation.");
        cases++;

        using LongRunTerminationCoordinator durationThenFailure = new(
            CancellationToken.None);
        Require(durationThenFailure.MarkDurationReached(),
            "Duration must win the duration-then-runtime test setup.");
        durationThenFailure.MarkRuntimeFailure();
        Require(
            durationThenFailure.EndReason == LongRunEndReason.DurationReached &&
            durationThenFailure.RuntimeFailureObserved &&
            LongRunRunner.ResolveExitCode(
                LongRunExitCode.Pass,
                gatesPassed: true,
                durationThenFailure.EndReason,
                durationThenFailure.RuntimeFailureObserved) !=
                    LongRunExitCode.Pass,
            "A runtime failure observed after duration must still fail closed.");
        cases++;

        using CancellationTokenSource racedCancellation = new();
        using LongRunTerminationCoordinator raced = new(
            racedCancellation.Token);
        using ManualResetEventSlim release = new(false);
        Task[] attempts =
        [
            Task.Run(() =>
            {
                release.Wait();
                raced.MarkDurationReached();
            }),
            Task.Run(() =>
            {
                release.Wait();
                racedCancellation.Cancel();
            }),
        ];
        release.Set();
        await Task.WhenAll(attempts).WaitAsync(TimeSpan.FromSeconds(2));
        LongRunExitCode racedResult = LongRunRunner.ResolveExitCode(
            LongRunExitCode.Pass,
            gatesPassed: true,
            raced.EndReason,
            raced.RuntimeFailureObserved,
            raced.CancellationObserved);
        Require(
            raced.CancellationObserved &&
            raced.EndReason is LongRunEndReason.DurationReached or
                LongRunEndReason.CancellationRequested &&
            racedResult == LongRunExitCode.CanceledSafely,
            "The actual CAS winner must remain unique while any observed cancellation forbids ordinary PASS.");
        cases++;

        using CancellationTokenSource disposedCancellation = new();
        LongRunTerminationCoordinator disposed = new(
            disposedCancellation.Token);
        disposed.Dispose();
        disposedCancellation.Cancel();
        Require(
            disposed.EndReason == LongRunEndReason.None &&
            !disposed.CancellationObserved,
            "A disposed production cancellation registration must not observe later cancellation.");
        cases++;

        List<string> diagnostics = [];
        using LongRunTerminationCoordinator observerFailure = new(
            CancellationToken.None,
            _ => throw new InvalidOperationException("stage observer fault"),
            diagnostics.Add);
        observerFailure.NotifyStage(LongRunStage.StopStarted);
        observerFailure.NotifyStage(LongRunStage.StopCompleted);
        Require(
            observerFailure.EndReason == LongRunEndReason.RuntimeFailure &&
            observerFailure.RuntimeFailureObserved &&
            diagnostics.Count == 1,
            "A production stage-observer failure must be recorded once and fail closed.");
        cases++;

        bool rejectedNone = false;
        try
        {
            _ = new LongRunEndReasonLatch().TrySet(LongRunEndReason.None);
        }
        catch (ArgumentOutOfRangeException)
        {
            rejectedNone = true;
        }
        Require(rejectedNone, "The None sentinel must never be latchable.");
        return cases + 1;
    }

    private static LongRunSample ValidPeriodicSample(
        string session,
        string output) => new(
        "periodic",
        DateTimeOffset.UtcNow,
        1.0,
        ManagedRecordingState.Recording.ToString(),
        session,
        TimeSpan.FromSeconds(1).Ticks,
        output,
        60,
        true,
        false,
        0,
        0,
        false,
        0,
        false,
        false,
        0,
        0,
        PreviewLifecycleState.Previewing.ToString(),
        100,
        200,
        10,
        4);

    private static LongRunSample ValidTerminalSample(
        string session,
        string output) => new(
        "terminal",
        DateTimeOffset.UtcNow,
        2.0,
        ManagedRecordingState.Completed.ToString(),
        session,
        TimeSpan.FromSeconds(2).Ticks,
        output,
        120,
        false,
        true,
        0,
        1,
        true,
        0,
        false,
        false,
        0,
        0,
        PreviewLifecycleState.Previewing.ToString(),
        120,
        220,
        11,
        5);

    private static LongRunSummary ValidSummary(
        string root,
        LoadedModuleEvidence modules,
        GitReproducibility git,
        string? finalSummaryPath = null)
    {
        root = Path.GetFullPath(root);
        Directory.CreateDirectory(root);
        string session = Guid.NewGuid().ToString("D");
        string recordingsDirectory = Path.Combine(
            root,
            "p2.5a-recordings");
        Directory.CreateDirectory(recordingsDirectory);
        string mp4 = Path.GetFullPath(Path.Combine(
            recordingsDirectory,
            session + ".mp4"));
        File.WriteAllBytes(mp4, [1, 2, 3, 4]);
        string snapshots = Path.Combine(root, "snapshots.jsonl");
        LongRunSample periodic = ValidPeriodicSample(session, mp4);
        LongRunSample terminalSample = ValidTerminalSample(session, mp4);
        EvidenceFileValidation jsonl;
        using (EvidenceWriter writer = new(snapshots))
        {
            writer.WriteSample(periodic);
            writer.WriteSample(terminalSample);
            jsonl = writer.CloseAndValidate(2);
        }
        Require(jsonl.Passed, $"Valid Summary JSONL setup failed: {jsonl.Error}");

        string diagnosticsDirectory = Path.Combine(
            root,
            "diagnostic-logs",
            "level-1",
            "level-2",
            "level-3");
        Directory.CreateDirectory(diagnosticsDirectory);
        File.WriteAllText(
            Path.Combine(
                diagnosticsDirectory,
                $"p2.4-encoder-{session}.jsonl"),
            "{\"SourceReaderValidation\":\"PASS\"}\n",
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        DateTimeOffset now = DateTimeOffset.UtcNow;
        ProcessMetrics startMetrics = new(
            now,
            "baseline",
            true,
            100,
            200,
            10,
            4,
            string.Empty);
        ProcessMetrics endMetrics = startMetrics with
        {
            CapturedUtc = now.AddSeconds(2),
            Phase = "final",
        };
        RelatedProcessEvidence related = new(
            now,
            [],
            true,
            string.Empty);
        ManagedRecordingSnapshot terminal = new(
            ManagedRecordingState.Completed,
            NativeMethods.Result.Ok,
            now.AddSeconds(-2),
            TimeSpan.FromSeconds(2),
            session,
            mp4,
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
            120,
            0,
            TimeSpan.Zero)
        {
            WorkingPath = mp4 + ".partial.mp4",
            PlannedFinalPath = mp4,
            PublishedPath = mp4,
            ReadyToPublish = true,
            Published = true,
            PublishAttempted = true,
            PublishHResult = 0,
            ValidationAttempted = true,
            ValidationHResult = 0,
        };
        RunObservations observations = new();
        observations.AddProcessMetrics(startMetrics);
        observations.Add(periodic);
        observations.Add(terminalSample);
        observations.AddProcessMetrics(endMetrics);
        PreviewCloseEvidence close = new(
            true,
            PreviewLifecycleState.Disposed,
            string.Empty,
            string.Empty);
        GateEvaluation gates = GateEvaluation.Create(
            canceled: false,
            durationSeconds: 2,
            actualWallSeconds: 2.0,
            terminal,
            terminalSnapshotReadAfterStop: true,
            sourceReaderValidation: "PASS",
            jsonl,
            observations,
            close,
            related,
            related,
            related,
            modules,
            git,
            startMetrics,
            endMetrics);
        Require(gates.Passed, "Valid Summary Gate setup must pass.");
        string summaryPath = Path.GetFullPath(
            finalSummaryPath ?? Path.Combine(root, "summary.json"));
        return new LongRunSummary
        {
            ProcessId = Environment.ProcessId,
            ProcessName = "XbPreview.LongRun",
            Parameters = new LongRunOptions(
                2,
                1000,
                Path.GetDirectoryName(root) ?? root,
                "final-hardening-test",
                root,
                summaryPath,
                snapshots,
                null),
            LoadedModules = modules,
            Git = git,
            StartUtc = now.AddSeconds(-2),
            EndUtc = now,
            ActualWallDurationSeconds = 2.0,
            FinalNativePts100ns = terminal.Elapsed.Ticks,
            SessionGuid = session,
            Mp4Path = mp4,
            Mp4Size = 4,
            StateSequenceLegal = true,
            PtsMonotonic = true,
            SampleCount = 2,
            MissedSampleCount = 0,
            SnapshotsJsonl = jsonl,
            ProcessStart = startMetrics,
            ProcessMaximum = observations.CalculateMaximum(),
            ProcessEnd = endMetrics,
            RelatedProcessesAtStart = related,
            RelatedProcessesAtEnd = related,
            RelatedProcessesBeforePreviewClose = related,
            RelatedProcessesAfterPreviewClose = related,
            StopRequestedUtc = now.AddMilliseconds(-100),
            FinalizeCompletedUtc = now.AddMilliseconds(-50),
            FinalizeDurationMilliseconds = 50,
            TerminalSnapshotReadAfterStop = true,
            TerminalSnapshot = terminal,
            SourceReaderValidation = "PASS",
            PreviewClosedNormally = true,
            PreviewClose = close,
            EndReason = LongRunEndReason.DurationReached,
            CancellationObserved = false,
            DurationTargetReached = true,
            RuntimeFailureObserved = false,
            GateMatrix = gates,
            ExitCode = (int)LongRunExitCode.Pass,
            Verdict = "PASS",
            Reasons = ["final hardening test"],
        };
    }

    private static JsonObject ToJsonObject(LongRunSample sample) =>
        JsonSerializer.SerializeToNode(
            sample,
            LongRunEvidenceSchema.JsonOptions)!.AsObject();

    private static void ExpectInvalidJsonl(
        string path,
        IReadOnlyList<JsonObject> lines,
        string? expectedSessionGuid = null)
    {
        File.WriteAllLines(
            path,
            lines.Select(line => line.ToJsonString(
                LongRunEvidenceSchema.JsonOptions)),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        EvidenceFileValidation validation = EvidenceWriter.ValidateJsonl(
            path,
            lines.Count,
            writerClosed: true,
            expectedSessionGuid: expectedSessionGuid);
        Require(
            !validation.Passed,
            $"Malformed JSONL must be rejected: {Path.GetFileName(path)}");
    }

    private static void ExpectPublishFailure(
        string finalPath,
        LongRunSummary summary,
        ILongRunFileOperations operations,
        bool expectedInvalidPublication)
    {
        using StringWriter output = new();
        using StringWriter error = new();
        LongRunExitCode result = LongRunResultPublisher.Publish(
            finalPath,
            summary,
            LongRunExitCode.Pass,
            output,
            error,
            operations);
        Require(
            result == LongRunExitCode.SummaryPublishFailed &&
            !output.ToString().Contains(
                "LONG-RUN-RESULT: PASS",
                StringComparison.Ordinal) &&
            error.ToString().Contains(
                "LONG-RUN-RESULT: BLOCKED",
                StringComparison.Ordinal),
            "A publication fault must fail closed with exit 12 and no PASS.");

        string? directory = Path.GetDirectoryName(finalPath);
        if (directory is null || !Directory.Exists(directory))
        {
            return;
        }
        string[] temporary = Directory.GetFiles(
            directory,
            "*.tmp",
            SearchOption.TopDirectoryOnly);
        Require(
            temporary.Length == 0,
            "A publication fault must not leave a temporary summary.");
        string[] invalid = Directory.GetFiles(
            directory,
            Path.GetFileName(finalPath) + ".invalid-*",
            SearchOption.TopDirectoryOnly);
        Require(
            invalid.Length == (expectedInvalidPublication ? 1 : 0),
            "Final readback failure must quarantine exactly one invalid publication.");
        if (expectedInvalidPublication)
        {
            Require(
                !File.Exists(finalPath),
                "A failed final readback must not leave the official summary path.");
        }
    }

    private static void RunGit(string workingDirectory, params string[] args)
    {
        ProcessStartInfo start = new("git")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (string argument in args)
        {
            start.ArgumentList.Add(argument);
        }
        using Process process = Process.Start(start) ??
            throw new InvalidOperationException("Unable to start temporary Git.");
        string standardOutput = process.StandardOutput.ReadToEnd();
        string standardError = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Temporary git {string.Join(' ', args)} failed: " +
                $"{standardError}{standardOutput}");
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void DeleteTestRoot(string root)
    {
        if (!Directory.Exists(root))
        {
            return;
        }
        foreach (string file in Directory.EnumerateFiles(
                     root,
                     "*",
                     SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }
        Directory.Delete(root, recursive: true);
    }

    private enum InjectedFileFault
    {
        Write,
        Flush,
        TemporaryReadBack,
        Move,
        MoveAfterSuccess,
        FinalReadBack,
    }

    private sealed class FaultingFileOperations : ILongRunFileOperations
    {
        private readonly InjectedFileFault _fault;
        private readonly ILongRunFileOperations _physical =
            PhysicalLongRunFileOperations.Instance;
        private int _readCount;

        internal FaultingFileOperations(InjectedFileFault fault) =>
            _fault = fault;

        public string GetFullPath(string path) => _physical.GetFullPath(path);

        public string? GetDirectoryName(string path) =>
            _physical.GetDirectoryName(path);

        public void CreateDirectory(string path) =>
            _physical.CreateDirectory(path);

        public bool FileExists(string path) => _physical.FileExists(path);

        public bool DirectoryExists(string path) =>
            _physical.DirectoryExists(path);

        public long GetFileLength(string path) =>
            _physical.GetFileLength(path);

        public Stream CreateNewWrite(string path, FileShare share)
        {
            Stream stream = _physical.CreateNewWrite(path, share);
            return _fault == InjectedFileFault.Write
                ? new ThrowingWriteStream(stream)
                : stream;
        }

        public Stream OpenRead(string path)
        {
            int read = Interlocked.Increment(ref _readCount);
            if (_fault == InjectedFileFault.TemporaryReadBack && read == 1)
            {
                throw new IOException("Injected temporary summary readback failure.");
            }
            if (_fault == InjectedFileFault.FinalReadBack && read == 2)
            {
                throw new IOException("Injected final summary readback failure.");
            }
            return _physical.OpenRead(path);
        }

        public IEnumerable<string> ReadLines(string path) =>
            _physical.ReadLines(path);

        public void FlushToDisk(Stream stream)
        {
            if (_fault == InjectedFileFault.Flush)
            {
                throw new IOException("Injected summary flush failure.");
            }
            _physical.FlushToDisk(stream);
        }

        public void MoveNoOverwrite(string sourcePath, string destinationPath)
        {
            if (_fault == InjectedFileFault.Move)
            {
                throw new IOException("Injected no-overwrite move failure.");
            }
            _physical.MoveNoOverwrite(sourcePath, destinationPath);
            if (_fault == InjectedFileFault.MoveAfterSuccess)
            {
                throw new IOException(
                    "Injected exception after a completed no-overwrite move.");
            }
        }

        public void DeleteFile(string path) => _physical.DeleteFile(path);
    }

    private sealed class ThrowingWriteStream : Stream
    {
        private readonly Stream _inner;

        internal ThrowingWriteStream(Stream inner) => _inner = inner;

        public override bool CanRead => _inner.CanRead;
        public override bool CanSeek => _inner.CanSeek;
        public override bool CanWrite => _inner.CanWrite;
        public override long Length => _inner.Length;
        public override long Position
        {
            get => _inner.Position;
            set => _inner.Position = value;
        }

        public override void Flush() => _inner.Flush();

        public override int Read(byte[] buffer, int offset, int count) =>
            _inner.Read(buffer, offset, count);

        public override long Seek(long offset, SeekOrigin origin) =>
            _inner.Seek(offset, origin);

        public override void SetLength(long value) => _inner.SetLength(value);

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new IOException("Injected summary write failure.");

        public override void Write(ReadOnlySpan<byte> buffer) =>
            throw new IOException("Injected summary write failure.");

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _inner.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    private sealed class ThrowBeforeWriteTextWriter : TextWriter
    {
        internal int AttemptCount { get; private set; }

        public override Encoding Encoding => Encoding.UTF8;

        public override void WriteLine(string? value)
        {
            AttemptCount++;
            throw new IOException("Injected final result output failure.");
        }
    }
}
