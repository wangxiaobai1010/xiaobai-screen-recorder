using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using XbPreview.Host;

namespace XbPreview.LongRun;

internal interface ILongRunFileOperations
{
    string GetFullPath(string path);
    string? GetDirectoryName(string path);
    void CreateDirectory(string path);
    bool FileExists(string path);
    bool DirectoryExists(string path);
    long GetFileLength(string path);
    Stream CreateNewWrite(string path, FileShare share);
    Stream OpenRead(string path);
    IEnumerable<string> ReadLines(string path);
    void FlushToDisk(Stream stream);
    void MoveNoOverwrite(string sourcePath, string destinationPath);
    void DeleteFile(string path);
}

internal sealed class PhysicalLongRunFileOperations : ILongRunFileOperations
{
    internal static PhysicalLongRunFileOperations Instance { get; } = new();

    private PhysicalLongRunFileOperations()
    {
    }

    public string GetFullPath(string path) => Path.GetFullPath(path);

    public string? GetDirectoryName(string path) => Path.GetDirectoryName(path);

    public void CreateDirectory(string path) => Directory.CreateDirectory(path);

    public bool FileExists(string path) => File.Exists(path);

    public bool DirectoryExists(string path) => Directory.Exists(path);

    public long GetFileLength(string path) => new FileInfo(path).Length;

    public Stream CreateNewWrite(string path, FileShare share) => new FileStream(
        path,
        FileMode.CreateNew,
        FileAccess.Write,
        share);

    public Stream OpenRead(string path) => new FileStream(
        path,
        FileMode.Open,
        FileAccess.Read,
        FileShare.Read);

    public IEnumerable<string> ReadLines(string path) => File.ReadLines(path);

    public void FlushToDisk(Stream stream)
    {
        stream.Flush();
        if (stream is FileStream fileStream)
        {
            fileStream.Flush(flushToDisk: true);
        }
    }

    public void MoveNoOverwrite(string sourcePath, string destinationPath) =>
        File.Move(sourcePath, destinationPath, overwrite: false);

    public void DeleteFile(string path) => File.Delete(path);
}

internal sealed record BinaryFingerprint(
    string ModuleName,
    string LoadEvidence,
    string Path,
    long Size,
    DateTime LastWriteTimeUtc,
    string Sha256,
    bool FromExpectedReleaseX64Directory);

internal sealed record LoadedModuleEvidence(
    List<BinaryFingerprint> Modules,
    bool Complete,
    string Error)
{
    internal bool IsCompleteAndConsistent
    {
        get
        {
            string[] expectedNames =
            [
                "XbPreview.LongRun.exe",
                "XbPreview.Host.dll",
                "XbPreview.Native.dll",
            ];
            return Complete &&
                string.IsNullOrEmpty(Error) &&
                Modules is not null &&
                Modules.Count == expectedNames.Length &&
                Modules.Select(module => module.ModuleName)
                    .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                    .SequenceEqual(
                        expectedNames.OrderBy(
                            name => name,
                            StringComparer.OrdinalIgnoreCase),
                        StringComparer.OrdinalIgnoreCase) &&
                Modules.All(module =>
                    !string.IsNullOrWhiteSpace(module.LoadEvidence) &&
                    Path.IsPathFullyQualified(module.Path) &&
                    module.Size > 0 &&
                    module.LastWriteTimeUtc != default &&
                    module.Sha256.Length == 64 &&
                    module.Sha256.All(Uri.IsHexDigit) &&
                    module.FromExpectedReleaseX64Directory);
        }
    }

    internal static LoadedModuleEvidence Capture() => Capture(
        expectedDirectoryOverride: null);

    internal static LoadedModuleEvidence Capture(
        string? expectedDirectoryOverride)
    {
        string expectedDirectory = Path.GetFullPath(
            expectedDirectoryOverride ?? Path.Combine(
                RepositoryFacts.FindRepositoryRoot(),
                "artifacts",
                "bin",
                "Release",
                "x64"))
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        List<BinaryFingerprint> modules = [];
        List<string> errors = [];
        try
        {
            using Process process = Process.GetCurrentProcess();
            process.Refresh();
            string? executable = process.MainModule?.FileName;
            AddModule(
                modules,
                errors,
                "XbPreview.LongRun.exe",
                "Process.GetCurrentProcess().MainModule.FileName",
                executable,
                expectedDirectory);

            string hostAssembly = typeof(PreviewLifecycleController)
                .Assembly.Location;
            AddModule(
                modules,
                errors,
                "XbPreview.Host.dll",
                "typeof(PreviewLifecycleController).Assembly.Location",
                hostAssembly,
                expectedDirectory);

            process.Refresh();
            string? nativeModule = null;
            foreach (ProcessModule module in process.Modules)
            {
                if (string.Equals(
                    Path.GetFileName(module.FileName),
                    "XbPreview.Native.dll",
                    StringComparison.OrdinalIgnoreCase))
                {
                    nativeModule = module.FileName;
                    break;
                }
            }
            AddModule(
                modules,
                errors,
                "XbPreview.Native.dll",
                "Process.GetCurrentProcess().Modules",
                nativeModule,
                expectedDirectory);
        }
        catch (Exception error)
        {
            errors.Add($"Module enumeration failed: {error.GetType().Name}: {error.Message}");
        }

        bool complete = errors.Count == 0 &&
            modules.Count == 3 &&
            modules.All(module => module.FromExpectedReleaseX64Directory);
        return new LoadedModuleEvidence(
            modules,
            complete,
            string.Join("; ", errors));
    }

    private static void AddModule(
        List<BinaryFingerprint> modules,
        List<string> errors,
        string expectedName,
        string loadEvidence,
        string? actualPath,
        string expectedDirectory)
    {
        if (string.IsNullOrWhiteSpace(actualPath))
        {
            errors.Add($"{expectedName} is not loaded.");
            return;
        }
        try
        {
            string fullPath = Path.GetFullPath(actualPath);
            if (!string.Equals(
                Path.GetFileName(fullPath),
                expectedName,
                StringComparison.OrdinalIgnoreCase))
            {
                errors.Add($"Expected {expectedName}, loaded {fullPath}.");
                return;
            }
            FileInfo file = new(fullPath);
            using FileStream stream = file.OpenRead();
            string directory = Path.GetFullPath(file.DirectoryName ?? string.Empty)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            bool expectedDirectoryMatch = string.Equals(
                directory,
                expectedDirectory,
                StringComparison.OrdinalIgnoreCase);
            modules.Add(new BinaryFingerprint(
                expectedName,
                loadEvidence,
                file.FullName,
                file.Length,
                file.LastWriteTimeUtc,
                Convert.ToHexString(SHA256.HashData(stream)),
                expectedDirectoryMatch));
            if (!expectedDirectoryMatch)
            {
                errors.Add(
                    $"{expectedName} loaded outside expected Release x64 directory: {file.FullName}");
            }
        }
        catch (Exception error)
        {
            errors.Add(
                $"{expectedName} fingerprint failed: {error.GetType().Name}: {error.Message}");
        }
    }
}

internal sealed record GitWorkspaceFacts(
    string HeadCommit,
    string Branch,
    string StatusPorcelainV1,
    bool Clean)
{
    internal static GitWorkspaceFacts Capture(string repositoryRoot)
    {
        string head = RepositoryFacts.RunGit(repositoryRoot, "rev-parse", "HEAD").Trim();
        string branch = RepositoryFacts.RunGit(repositoryRoot, "branch", "--show-current").Trim();
        string status = RepositoryFacts.RunGit(
            repositoryRoot,
            "status",
            "--porcelain=v1",
            "--untracked-files=all");
        return new GitWorkspaceFacts(head, branch, status, status.Length == 0);
    }
}

internal sealed record GitReproducibility(
    GitWorkspaceFacts? Start,
    GitWorkspaceFacts? End,
    bool HeadUnchanged,
    bool BranchUnchanged,
    bool StatusByteForByteUnchanged,
    bool EvidencePathsGitSafe,
    bool Passed,
    string Error)
{
    internal bool IsCompleteAndConsistent =>
        Passed &&
        string.IsNullOrEmpty(Error) &&
        EvidencePathsGitSafe &&
        Start is not null &&
        End is not null &&
        Start.Clean &&
        End.Clean &&
        Start.StatusPorcelainV1.Length == 0 &&
        End.StatusPorcelainV1.Length == 0 &&
        HeadUnchanged &&
        BranchUnchanged &&
        StatusByteForByteUnchanged &&
        string.Equals(
            Start.HeadCommit,
            End.HeadCommit,
            StringComparison.Ordinal) &&
        string.Equals(Start.Branch, End.Branch, StringComparison.Ordinal) &&
        string.Equals(
            Start.StatusPorcelainV1,
            End.StatusPorcelainV1,
            StringComparison.Ordinal);

    internal static GitReproducibility Compare(
        GitWorkspaceFacts? start,
        GitWorkspaceFacts? end,
        string? captureError,
        bool evidencePathsGitSafe)
    {
        bool head = start is not null && end is not null &&
            string.Equals(start.HeadCommit, end.HeadCommit, StringComparison.Ordinal);
        bool branch = start is not null && end is not null &&
            string.Equals(start.Branch, end.Branch, StringComparison.Ordinal);
        bool status = start is not null && end is not null &&
            string.Equals(
                start.StatusPorcelainV1,
                end.StatusPorcelainV1,
                StringComparison.Ordinal);
        bool passed = string.IsNullOrEmpty(captureError) &&
            start?.Clean == true &&
            end?.Clean == true &&
            head && branch && status && evidencePathsGitSafe;
        return new GitReproducibility(
            start,
            end,
            head,
            branch,
            status,
            evidencePathsGitSafe,
            passed,
            captureError ?? string.Empty);
    }
}

internal sealed record ProcessMetrics(
    DateTimeOffset CapturedUtc,
    string Phase,
    bool Available,
    long? WorkingSet,
    long? PrivateMemorySize,
    int? HandleCount,
    int? ThreadCount,
    string Error)
{
    internal static ProcessMetrics Read(string phase)
    {
        try
        {
            using Process process = Process.GetCurrentProcess();
            process.Refresh();
            return new ProcessMetrics(
                DateTimeOffset.UtcNow,
                phase,
                true,
                process.WorkingSet64,
                process.PrivateMemorySize64,
                process.HandleCount,
                process.Threads.Count,
                string.Empty);
        }
        catch (Exception error)
        {
            return new ProcessMetrics(
                DateTimeOffset.UtcNow,
                phase,
                false,
                null,
                null,
                null,
                null,
                $"{error.GetType().Name}: {error.Message}");
        }
    }
}

internal sealed record RelatedProcessEvidence(
    DateTimeOffset CapturedUtc,
    List<string> Processes,
    bool NoneFound,
    string Error)
{
    private static readonly HashSet<string> Names = new(
        StringComparer.OrdinalIgnoreCase)
    {
        "XbPreview.Host",
        "XbPreview.Managed.Tests",
        "XbPreview.Native.Tests",
        "XbPreview.LongRun",
    };

    internal static RelatedProcessEvidence Capture(int excludedProcessId)
    {
        try
        {
            List<string> found = [];
            foreach (Process process in Process.GetProcesses())
            {
                using (process)
                {
                    if (process.Id != excludedProcessId &&
                        Names.Contains(process.ProcessName))
                    {
                        found.Add($"{process.ProcessName}({process.Id})");
                    }
                }
            }
            found.Sort(StringComparer.Ordinal);
            return new RelatedProcessEvidence(
                DateTimeOffset.UtcNow,
                found,
                found.Count == 0,
                string.Empty);
        }
        catch (Exception error)
        {
            return new RelatedProcessEvidence(
                DateTimeOffset.UtcNow,
                [],
                false,
                $"{error.GetType().Name}: {error.Message}");
        }
    }
}

internal sealed record LongRunSample(
    string SampleType,
    DateTimeOffset UtcTimestamp,
    double WallElapsedSeconds,
    string RecordingState,
    string SessionGuid,
    long NativePtsElapsed100ns,
    string OutputPath,
    ulong FramesSubmitted,
    bool ActiveEncoder,
    bool FinalizeAttempted,
    int FinalizeHResult,
    uint FinalizeCount,
    bool OutputSuccess,
    int FailureHResult,
    bool OutputCleanupAttempted,
    bool OutputCleanupSucceeded,
    int OutputCleanupHResult,
    uint ResidualOutstanding,
    string PreviewState,
    long? WorkingSet,
    long? PrivateMemorySize,
    int? HandleCount,
    int? ThreadCount);

internal sealed record EvidenceFileValidation(
    bool WriterClosed,
    bool Exists,
    bool NonEmpty,
    bool EveryLineParsed,
    int ParsedLineCount,
    int ExpectedLineCount,
    int TerminalSampleCount,
    bool Passed,
    string Error);

internal sealed record GateResult(
    string Name,
    bool Required,
    bool Passed,
    string Detail);

internal sealed record GateEvaluation(
    List<GateResult> Gates,
    bool Passed)
{
    internal static IReadOnlyList<string> ExpectedGateNames { get; } =
    [
        "target-wall-duration",
        "session-guid",
        "session-consistent-across-samples",
        "final-native-pts-positive",
        "pts-monotonic",
        "state-sequence-legal",
        "terminal-completed",
        "terminal-snapshot-reread",
        "finalize-attempted",
        "finalize-count-one",
        "finalize-hresult-success",
        "output-success",
        "source-reader-validation",
        "output-path-absolute-mp4",
        "mp4-exists-nonzero",
        "snapshots-jsonl",
        "native-residual-zero",
        "native-encoder-inactive",
        "preview-closed",
        "related-processes-zero",
        "loaded-modules",
        "git-reproducibility",
        "process-metrics-complete",
    ];

    internal static GateEvaluation Create(
        bool canceled,
        int durationSeconds,
        double actualWallSeconds,
        ManagedRecordingSnapshot terminal,
        bool terminalSnapshotReadAfterStop,
        string sourceReaderValidation,
        EvidenceFileValidation jsonl,
        RunObservations observations,
        PreviewCloseEvidence? previewClose,
        RelatedProcessEvidence? relatedProcessesAfterPreviewClose,
        RelatedProcessEvidence? relatedProcessesAtStart,
        RelatedProcessEvidence? relatedProcessesAtEnd,
        LoadedModuleEvidence? modules,
        GitReproducibility git,
        ProcessMetrics? baseline,
        ProcessMetrics? final,
        ILongRunFileOperations? fileOperations = null)
    {
        ILongRunFileOperations operations = fileOperations ??
            PhysicalLongRunFileOperations.Instance;
        bool validGuid = Guid.TryParse(terminal.SessionId, out Guid sessionId) &&
            sessionId != Guid.Empty;
        bool validOutputPath = false;
        bool mp4Exists = false;
        long mp4Size = 0;
        try
        {
            validOutputPath = !string.IsNullOrWhiteSpace(terminal.PublishedPath) &&
                Path.IsPathFullyQualified(terminal.PublishedPath) &&
                string.Equals(
                    Path.GetExtension(terminal.PublishedPath),
                    ".mp4",
                    StringComparison.OrdinalIgnoreCase);
            if (validOutputPath)
            {
                mp4Exists = operations.FileExists(terminal.PublishedPath);
                mp4Size = mp4Exists
                    ? operations.GetFileLength(terminal.PublishedPath)
                    : 0;
            }
        }
        catch
        {
            validOutputPath = false;
        }

        bool processMetricsComplete = baseline?.Available == true &&
            final?.Available == true &&
            observations.AllProcessMetricsAvailable;
        List<GateResult> gates =
        [
            Gate(
                "target-wall-duration",
                !canceled,
                canceled || actualWallSeconds >= durationSeconds,
                canceled
                    ? "Not required for a canceled run."
                    : $"actual={actualWallSeconds:F6}s required={durationSeconds}s"),
            Gate("session-guid", true, validGuid, terminal.SessionId),
            Gate(
                "session-consistent-across-samples",
                true,
                observations.SessionConsistent,
                observations.SessionConsistent.ToString()),
            Gate("final-native-pts-positive", true, terminal.Elapsed.Ticks > 0, terminal.Elapsed.Ticks.ToString()),
            Gate("pts-monotonic", true, observations.PtsMonotonic, observations.PtsMonotonic.ToString()),
            Gate("state-sequence-legal", true, observations.StateSequenceLegal, observations.StateSequenceLegal.ToString()),
            Gate("terminal-completed", true, terminal.State == ManagedRecordingState.Completed, terminal.State.ToString()),
            Gate("terminal-snapshot-reread", true, terminalSnapshotReadAfterStop, terminalSnapshotReadAfterStop.ToString()),
            Gate("finalize-attempted", true, terminal.FinalizeAttempted, terminal.FinalizeAttempted.ToString()),
            Gate("finalize-count-one", true, terminal.FinalizeCount == 1, terminal.FinalizeCount.ToString()),
            Gate("finalize-hresult-success", true, terminal.FinalizeHResult >= 0, $"0x{terminal.FinalizeHResult:X8}"),
            Gate(
                "output-success",
                true,
                terminal.OutputSuccess &&
                    terminal.ReadyToPublish &&
                    terminal.Published &&
                    terminal.PublishAttempted &&
                    terminal.PublishHResult >= 0 &&
                    terminal.ValidationAttempted &&
                    terminal.ValidationHResult >= 0,
                $"outputSuccess={terminal.OutputSuccess}; " +
                    $"ready={terminal.ReadyToPublish}; " +
                    $"published={terminal.Published}; " +
                    $"publishAttempted={terminal.PublishAttempted}; " +
                    $"publishHResult=0x{terminal.PublishHResult:X8}; " +
                    $"validationAttempted={terminal.ValidationAttempted}; " +
                    $"validationHResult=0x{terminal.ValidationHResult:X8}"),
            Gate("source-reader-validation", true, string.Equals(sourceReaderValidation, "PASS", StringComparison.Ordinal), sourceReaderValidation),
            Gate("output-path-absolute-mp4", true, validOutputPath, terminal.PublishedPath),
            Gate("mp4-exists-nonzero", true, mp4Exists && mp4Size > 0, $"exists={mp4Exists}; size={mp4Size}"),
            Gate("snapshots-jsonl", true, jsonl.Passed, jsonl.Error.Length == 0 ? $"lines={jsonl.ParsedLineCount}" : jsonl.Error),
            Gate("native-residual-zero", true, terminal.ResidualOutstanding == 0, terminal.ResidualOutstanding.ToString()),
            Gate("native-encoder-inactive", true, !terminal.ActiveEncoder, terminal.ActiveEncoder.ToString()),
            Gate(
                "preview-closed",
                true,
                previewClose?.Passed == true &&
                    relatedProcessesAfterPreviewClose?.NoneFound == true,
                $"close={previewClose?.Describe() ?? "unavailable"}; " +
                    $"processesAfterClose={relatedProcessesAfterPreviewClose?.NoneFound}; " +
                    $"processError={relatedProcessesAfterPreviewClose?.Error ?? "unavailable"}"),
            Gate(
                "related-processes-zero",
                true,
                relatedProcessesAtStart?.NoneFound == true &&
                    relatedProcessesAtEnd?.NoneFound == true,
                $"start={relatedProcessesAtStart?.NoneFound}; end={relatedProcessesAtEnd?.NoneFound}; " +
                    $"startError={relatedProcessesAtStart?.Error ?? "unavailable"}; " +
                    $"endError={relatedProcessesAtEnd?.Error ?? "unavailable"}"),
            Gate("loaded-modules", true, modules?.IsCompleteAndConsistent == true, modules?.Error ?? "unavailable"),
            Gate("git-reproducibility", true, git.IsCompleteAndConsistent, git.Error.Length == 0 ? $"startClean={git.Start?.Clean}; endClean={git.End?.Clean}" : git.Error),
            Gate("process-metrics-complete", true, processMetricsComplete, processMetricsComplete.ToString()),
        ];
        return new GateEvaluation(
            gates,
            gates.Where(gate => gate.Required).All(gate => gate.Passed));
    }

    private static GateResult Gate(
        string name,
        bool required,
        bool passed,
        string detail) => new(name, required, passed, detail);
}

internal sealed class LongRunSummary
{
    public string ToolVersion { get; set; } = "2.0";
    public int ProcessId { get; set; }
    public string ProcessName { get; set; } = string.Empty;
    public string ProcessRole { get; set; } =
        "XbPreview.LongRun hosts the formal Preview lifecycle and RecordingController in-process.";
    public LongRunOptions? Parameters { get; set; }
    public LoadedModuleEvidence? LoadedModules { get; set; }
    public GitReproducibility? Git { get; set; }
    public DateTimeOffset StartUtc { get; set; }
    public DateTimeOffset EndUtc { get; set; }
    public double ActualWallDurationSeconds { get; set; }
    public long FinalNativePts100ns { get; set; }
    public string SessionGuid { get; set; } = string.Empty;
    public string Mp4Path { get; set; } = string.Empty;
    public long? Mp4Size { get; set; }
    public bool StateSequenceLegal { get; set; }
    public bool PtsMonotonic { get; set; }
    public int SampleCount { get; set; }
    public int MissedSampleCount { get; set; }
    public EvidenceFileValidation? SnapshotsJsonl { get; set; }
    public ProcessMetrics? ProcessStart { get; set; }
    public ProcessMetrics? ProcessMaximum { get; set; }
    public ProcessMetrics? ProcessEnd { get; set; }
    public RelatedProcessEvidence? RelatedProcessesAtStart { get; set; }
    public RelatedProcessEvidence? RelatedProcessesAtEnd { get; set; }
    public RelatedProcessEvidence? RelatedProcessesBeforePreviewClose { get; set; }
    public RelatedProcessEvidence? RelatedProcessesAfterPreviewClose { get; set; }
    public DateTimeOffset? StopRequestedUtc { get; set; }
    public DateTimeOffset? FinalizeCompletedUtc { get; set; }
    public double? FinalizeDurationMilliseconds { get; set; }
    public bool TerminalSnapshotReadAfterStop { get; set; }
    public ManagedRecordingSnapshot? TerminalSnapshot { get; set; }
    public string SourceReaderValidation { get; set; } = "unavailable";
    public bool PreviewClosedNormally { get; set; }
    public PreviewCloseEvidence? PreviewClose { get; set; }
    public LongRunEndReason EndReason { get; set; }
    public bool CancellationObserved { get; set; }
    public bool DurationTargetReached { get; set; }
    public bool RuntimeFailureObserved { get; set; }
    public GateEvaluation? GateMatrix { get; set; }
    public string SummaryPublishProtocol { get; set; } =
        "CreateNew temporary file, flush-to-disk, parse, atomic Move without overwrite, reopen and parse final file.";
    public int ExitCode { get; set; }
    public string Verdict { get; set; } = "BLOCKED";
    public List<string> Reasons { get; set; } = [];
}

internal sealed record LongRunPublicationFacts(
    EvidenceFileValidation Jsonl,
    IReadOnlyList<LongRunSample> Samples,
    LongRunSample TerminalSample,
    long Mp4Size,
    string SourceReaderValidation,
    GateEvaluation RecomputedGates);

internal static class LongRunEvidenceSchema
{
    internal static JsonSerializerOptions JsonOptions { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    private static readonly HashSet<string> SampleProperties = new(
        StringComparer.Ordinal)
    {
        "sampleType",
        "utcTimestamp",
        "wallElapsedSeconds",
        "recordingState",
        "sessionGuid",
        "nativePtsElapsed100ns",
        "outputPath",
        "framesSubmitted",
        "activeEncoder",
        "finalizeAttempted",
        "finalizeHResult",
        "finalizeCount",
        "outputSuccess",
        "failureHResult",
        "outputCleanupAttempted",
        "outputCleanupSucceeded",
        "outputCleanupHResult",
        "residualOutstanding",
        "previewState",
        "workingSet",
        "privateMemorySize",
        "handleCount",
        "threadCount",
    };

    private static readonly HashSet<string> SummaryProperties = new(
        StringComparer.Ordinal)
    {
        "toolVersion",
        "processId",
        "processName",
        "processRole",
        "parameters",
        "loadedModules",
        "git",
        "startUtc",
        "endUtc",
        "actualWallDurationSeconds",
        "finalNativePts100ns",
        "sessionGuid",
        "mp4Path",
        "mp4Size",
        "stateSequenceLegal",
        "ptsMonotonic",
        "sampleCount",
        "missedSampleCount",
        "snapshotsJsonl",
        "processStart",
        "processMaximum",
        "processEnd",
        "relatedProcessesAtStart",
        "relatedProcessesAtEnd",
        "relatedProcessesBeforePreviewClose",
        "relatedProcessesAfterPreviewClose",
        "stopRequestedUtc",
        "finalizeCompletedUtc",
        "finalizeDurationMilliseconds",
        "terminalSnapshotReadAfterStop",
        "terminalSnapshot",
        "sourceReaderValidation",
        "previewClosedNormally",
        "previewClose",
        "endReason",
        "cancellationObserved",
        "durationTargetReached",
        "runtimeFailureObserved",
        "gateMatrix",
        "summaryPublishProtocol",
        "exitCode",
        "verdict",
        "reasons",
    };

    internal static bool TryDeserializeSample(
        JsonElement root,
        out LongRunSample sample,
        out string error)
    {
        sample = default!;
        if (!TryValidateExactProperties(root, SampleProperties, out error))
        {
            return false;
        }
        foreach (string name in new[]
                 {
                     "sampleType", "utcTimestamp", "recordingState",
                     "sessionGuid", "outputPath", "previewState",
                 })
        {
            if (root.GetProperty(name).ValueKind != JsonValueKind.String)
            {
                error = $"Property '{name}' must be a JSON string.";
                return false;
            }
        }
        foreach (string name in new[]
                 {
                     "wallElapsedSeconds", "nativePtsElapsed100ns",
                     "framesSubmitted", "finalizeHResult", "finalizeCount",
                     "failureHResult", "outputCleanupHResult",
                     "residualOutstanding", "workingSet",
                     "privateMemorySize", "handleCount", "threadCount",
                 })
        {
            if (root.GetProperty(name).ValueKind != JsonValueKind.Number)
            {
                error = $"Property '{name}' must be a non-null JSON number.";
                return false;
            }
        }
        foreach (string name in new[]
                 {
                     "activeEncoder", "finalizeAttempted", "outputSuccess",
                     "outputCleanupAttempted", "outputCleanupSucceeded",
                 })
        {
            JsonValueKind kind = root.GetProperty(name).ValueKind;
            if (kind is not (JsonValueKind.True or JsonValueKind.False))
            {
                error = $"Property '{name}' must be a JSON boolean.";
                return false;
            }
        }

        try
        {
            LongRunSample? value = root.Deserialize<LongRunSample>(JsonOptions);
            if (value is null)
            {
                error = "LongRunSample deserialized to null.";
                return false;
            }
            if (value.SampleType is not ("periodic" or "terminal"))
            {
                error = $"Invalid sampleType '{value.SampleType}'.";
                return false;
            }
            if (value.UtcTimestamp == default ||
                !double.IsFinite(value.WallElapsedSeconds) ||
                value.WallElapsedSeconds < 0)
            {
                error = "Sample timestamp or wall elapsed value is invalid.";
                return false;
            }
            if (!Enum.TryParse(
                    value.RecordingState,
                    ignoreCase: false,
                    out ManagedRecordingState recordingState) ||
                !Enum.IsDefined(recordingState))
            {
                error = $"Invalid recording state '{value.RecordingState}'.";
                return false;
            }
            if (!Enum.TryParse(
                    value.PreviewState,
                    ignoreCase: false,
                    out PreviewLifecycleState previewState) ||
                !Enum.IsDefined(previewState))
            {
                error = $"Invalid Preview state '{value.PreviewState}'.";
                return false;
            }
            if (!Guid.TryParse(value.SessionGuid, out Guid session) ||
                session == Guid.Empty)
            {
                error = $"Invalid Session GUID '{value.SessionGuid}'.";
                return false;
            }
            if (value.NativePtsElapsed100ns < 0 ||
                string.IsNullOrWhiteSpace(value.OutputPath) ||
                !Path.IsPathFullyQualified(value.OutputPath) ||
                !string.Equals(
                    Path.GetExtension(value.OutputPath),
                    ".mp4",
                    StringComparison.OrdinalIgnoreCase) ||
                value.WorkingSet is null or < 0 ||
                value.PrivateMemorySize is null or < 0 ||
                value.HandleCount is null or < 0 ||
                value.ThreadCount is null or < 0)
            {
                error = "Sample PTS, output path, or process metrics are invalid.";
                return false;
            }
            if (value.SampleType == "terminal")
            {
                if (recordingState is not (
                        ManagedRecordingState.Completed or
                        ManagedRecordingState.Failed) ||
                    value.ActiveEncoder)
                {
                    error = "Terminal sample has a nonterminal state or active Encoder.";
                    return false;
                }
                if (recordingState == ManagedRecordingState.Completed &&
                    (!value.OutputSuccess ||
                     !value.FinalizeAttempted ||
                     value.FinalizeCount != 1 ||
                     value.FinalizeHResult < 0 ||
                     value.FailureHResult < 0 ||
                     value.OutputCleanupAttempted ||
                     value.OutputCleanupSucceeded ||
                     value.OutputCleanupHResult < 0 ||
                     value.ResidualOutstanding != 0 ||
                     value.NativePtsElapsed100ns <= 0 ||
                     value.FramesSubmitted == 0))
                {
                    error = "Completed terminal sample has inconsistent Finalize or output facts.";
                    return false;
                }
                if (recordingState == ManagedRecordingState.Failed &&
                    value.OutputSuccess)
                {
                    error = "Failed terminal sample cannot report OutputSuccess.";
                    return false;
                }
            }
            else if (recordingState is
                     ManagedRecordingState.Completed or
                     ManagedRecordingState.Failed)
            {
                error = "Periodic sample cannot carry a terminal Recording state.";
                return false;
            }
            sample = value;
            error = string.Empty;
            return true;
        }
        catch (Exception exception)
        {
            error = $"LongRunSample deserialize failed: {exception.GetType().Name}: {exception.Message}";
            return false;
        }
    }

    internal static bool TryDeserializeSummary(
        JsonElement root,
        out LongRunSummary summary,
        out string error)
    {
        summary = default!;
        if (!TryValidateExactProperties(root, SummaryProperties, out error))
        {
            return false;
        }
        try
        {
            LongRunSummary? value = root.Deserialize<LongRunSummary>(JsonOptions);
            if (value is null)
            {
                error = "LongRunSummary deserialized to null.";
                return false;
            }
            if (!TryValidateSummary(value, out error))
            {
                return false;
            }
            summary = value;
            return true;
        }
        catch (Exception exception)
        {
            error = $"LongRunSummary deserialize failed: {exception.GetType().Name}: {exception.Message}";
            return false;
        }
    }

    internal static bool TryValidateSummary(
        LongRunSummary summary,
        out string error)
    {
        error = string.Empty;
        if (summary.Parameters is null ||
            summary.Git is null ||
            summary.SnapshotsJsonl is null ||
            summary.GateMatrix is null ||
            summary.TerminalSnapshot is null ||
            summary.Reasons is null)
        {
            error = "Summary is missing a required evidence object.";
            return false;
        }
        if (!TryExpectedVerdict(
                summary.ExitCode,
                out string expectedVerdict) ||
            !string.Equals(
                summary.Verdict,
                expectedVerdict,
                StringComparison.Ordinal))
        {
            error = $"Summary Verdict/ExitCode mismatch: verdict={summary.Verdict}; exit={summary.ExitCode}.";
            return false;
        }

        HashSet<string> expected = new(
            GateEvaluation.ExpectedGateNames,
            StringComparer.Ordinal);
        HashSet<string> seen = new(StringComparer.Ordinal);
        if (summary.GateMatrix.Gates is null ||
            summary.GateMatrix.Gates.Count != expected.Count)
        {
            error = $"Summary Gate matrix must contain exactly {expected.Count} entries.";
            return false;
        }
        foreach (GateResult gate in summary.GateMatrix.Gates)
        {
            if (gate is null ||
                string.IsNullOrWhiteSpace(gate.Name) ||
                gate.Detail is null ||
                !expected.Contains(gate.Name) ||
                !seen.Add(gate.Name))
            {
                error = "Summary Gate matrix contains a missing, duplicate, or unknown Gate.";
                return false;
            }
            if (gate.Name != "target-wall-duration" && !gate.Required)
            {
                error = $"Required Gate '{gate.Name}' was marked optional.";
                return false;
            }
        }
        GateResult durationGate = summary.GateMatrix.Gates.Single(
            gate => gate.Name == "target-wall-duration");
        bool durationGateShouldBeRequired =
            summary.EndReason != LongRunEndReason.CancellationRequested;
        if (durationGate.Required != durationGateShouldBeRequired)
        {
            error =
                "target-wall-duration Required flag disagrees with the atomic end reason.";
            return false;
        }
        bool computedGateResult = summary.GateMatrix.Gates
            .Where(gate => gate.Required)
            .All(gate => gate.Passed);
        if (summary.GateMatrix.Passed != computedGateResult)
        {
            error = "GateMatrix.Passed disagrees with its Required Gate entries.";
            return false;
        }

        bool successfulVerdict = summary.ExitCode is
            (int)LongRunExitCode.Pass or
            (int)LongRunExitCode.CanceledSafely;
        if ((summary.EndReason == LongRunEndReason.CancellationRequested &&
             !summary.CancellationObserved) ||
            (summary.EndReason == LongRunEndReason.DurationReached &&
             !summary.DurationTargetReached) ||
            (summary.EndReason == LongRunEndReason.RuntimeFailure &&
             !summary.RuntimeFailureObserved))
        {
            error = "Summary end-reason facts are internally inconsistent.";
            return false;
        }
        if (successfulVerdict &&
            (!summary.GateMatrix.Passed ||
             summary.LoadedModules?.IsCompleteAndConsistent != true ||
             !summary.Git.IsCompleteAndConsistent ||
             summary.SnapshotsJsonl.Passed != true ||
             summary.SnapshotsJsonl.ParsedLineCount != summary.SampleCount ||
             summary.SnapshotsJsonl.ExpectedLineCount != summary.SampleCount ||
             summary.SnapshotsJsonl.TerminalSampleCount != 1 ||
             summary.ProcessStart?.Available != true ||
             summary.ProcessMaximum?.Available != true ||
             summary.ProcessEnd?.Available != true ||
             summary.RelatedProcessesAtStart?.NoneFound != true ||
             summary.RelatedProcessesAtEnd?.NoneFound != true ||
             summary.RelatedProcessesBeforePreviewClose?.NoneFound != true ||
             summary.RelatedProcessesAfterPreviewClose?.NoneFound != true ||
             summary.StopRequestedUtc is null ||
             summary.FinalizeCompletedUtc is null ||
             summary.FinalizeDurationMilliseconds is null or < 0 ||
             !summary.TerminalSnapshotReadAfterStop ||
             summary.TerminalSnapshot.State != ManagedRecordingState.Completed ||
             !summary.TerminalSnapshot.OutputSuccess ||
             !summary.TerminalSnapshot.ReadyToPublish ||
             !summary.TerminalSnapshot.Published ||
             !summary.TerminalSnapshot.PublishAttempted ||
             summary.TerminalSnapshot.PublishHResult < 0 ||
             !summary.PreviewClosedNormally ||
              summary.PreviewClose?.Passed != true ||
              summary.RuntimeFailureObserved ||
              (summary.ExitCode == (int)LongRunExitCode.Pass &&
               (summary.EndReason != LongRunEndReason.DurationReached ||
                !summary.DurationTargetReached ||
                summary.CancellationObserved)) ||
              (summary.ExitCode == (int)LongRunExitCode.CanceledSafely &&
               (!summary.CancellationObserved ||
                summary.EndReason is not (
                    LongRunEndReason.CancellationRequested or
                    LongRunEndReason.DurationReached))) ||
             !string.Equals(
                 summary.SourceReaderValidation,
                 "PASS",
                 StringComparison.Ordinal) ||
             summary.Mp4Size is null or <= 0 ||
             summary.FinalNativePts100ns <= 0 ||
             summary.SampleCount <= 0 ||
             !summary.StateSequenceLegal ||
             !summary.PtsMonotonic ||
             !string.Equals(
                 summary.SessionGuid,
                 summary.TerminalSnapshot.SessionId,
                 StringComparison.Ordinal) ||
             !string.Equals(
                 summary.Mp4Path,
                 summary.TerminalSnapshot.PublishedPath,
                 StringComparison.Ordinal)))
        {
            error =
                "PASS/CANCELED-SAFELY summary contains incomplete or " +
                $"contradictory evidence: gates={summary.GateMatrix.Passed}; " +
                $"modules={summary.LoadedModules?.IsCompleteAndConsistent}; " +
                $"git={summary.Git.IsCompleteAndConsistent}; " +
                $"jsonl={summary.SnapshotsJsonl.Passed}; " +
                $"terminal={summary.TerminalSnapshot.State}; " +
                $"outputSuccess={summary.TerminalSnapshot.OutputSuccess}; " +
                $"ready={summary.TerminalSnapshot.ReadyToPublish}; " +
                $"published={summary.TerminalSnapshot.Published}; " +
                $"publishAttempted={summary.TerminalSnapshot.PublishAttempted}; " +
                $"publishHResult=0x{summary.TerminalSnapshot.PublishHResult:X8}; " +
                $"mp4PathMatch={string.Equals(summary.Mp4Path, summary.TerminalSnapshot.PublishedPath, StringComparison.Ordinal)}.";
            return false;
        }
        if (successfulVerdict &&
            !TryValidateSuccessfulSummaryFacts(summary, out error))
        {
            return false;
        }
        return true;
    }

    internal static bool TryValidatePublicationEvidence(
        LongRunSummary summary,
        ILongRunFileOperations? fileOperations,
        out LongRunPublicationFacts? facts,
        out string error)
    {
        facts = null;
        if (!TryValidateSummary(summary, out error))
        {
            return false;
        }
        if (summary.ExitCode is not (
                (int)LongRunExitCode.Pass or
                (int)LongRunExitCode.CanceledSafely))
        {
            return true;
        }

        ILongRunFileOperations operations = fileOperations ??
            PhysicalLongRunFileOperations.Instance;
        try
        {
            LongRunOptions parameters = summary.Parameters!;
            ManagedRecordingSnapshot terminalSnapshot =
                summary.TerminalSnapshot!;
            List<LongRunSample> samples = [];
            EvidenceFileValidation jsonl = EvidenceWriter.ValidateJsonl(
                parameters.SnapshotsJsonlPath,
                summary.SampleCount,
                writerClosed: true,
                fileOperations: operations,
                expectedSessionGuid: summary.SessionGuid,
                sampleObserver: samples.Add);
            if (!jsonl.Passed)
            {
                error = $"Published JSONL revalidation failed: {jsonl.Error}";
                return false;
            }
            if (!Equals(summary.SnapshotsJsonl, jsonl))
            {
                error =
                    "Summary JSONL facts do not match the independently reread JSONL file.";
                return false;
            }

            LongRunSample? terminalSample = samples.SingleOrDefault(
                sample => sample.SampleType == "terminal");
            if (terminalSample is null ||
                !TerminalSampleMatchesSnapshot(
                    terminalSample,
                    terminalSnapshot))
            {
                error =
                    "Published JSONL terminal sample does not match TerminalSnapshot.";
                return false;
            }
            if (terminalSample.WallElapsedSeconds !=
                summary.ActualWallDurationSeconds)
            {
                error =
                    "Published JSONL terminal wall duration does not match Summary.";
                return false;
            }
            if (samples.Any(sample =>
                    !string.Equals(
                        sample.OutputPath,
                        terminalSnapshot.OutputPath,
                        StringComparison.Ordinal) ||
                    !string.Equals(
                        sample.PreviewState,
                        PreviewLifecycleState.Previewing.ToString(),
                        StringComparison.Ordinal)))
            {
                error =
                    "Published JSONL changed output path or was sampled outside Previewing.";
                return false;
            }

            RunObservations observations = new();
            observations.AddProcessMetrics(summary.ProcessStart!);
            foreach (LongRunSample sample in samples)
            {
                observations.Add(sample);
            }
            observations.AddProcessMetrics(summary.ProcessEnd!);
            if (observations.SampleCount != summary.SampleCount ||
                observations.PtsMonotonic != summary.PtsMonotonic ||
                observations.StateSequenceLegal !=
                    summary.StateSequenceLegal ||
                !observations.SessionConsistent)
            {
                error =
                    "Summary sample aggregates do not match the independently reread JSONL.";
                return false;
            }
            ProcessMetrics? recomputedMaximum =
                observations.CalculateMaximum();
            if (!ProcessMaximumMatches(
                    summary.ProcessMaximum,
                    recomputedMaximum))
            {
                error =
                    "Summary process maximum does not match baseline, JSONL, and final facts.";
                return false;
            }

            string outputPath = terminalSnapshot.PublishedPath;
            if (!Path.IsPathFullyQualified(outputPath) ||
                !string.Equals(
                    Path.GetExtension(outputPath),
                    ".mp4",
                    StringComparison.OrdinalIgnoreCase) ||
                !operations.FileExists(outputPath))
            {
                error =
                    "Published MP4 path is not an existing absolute .mp4 file.";
                return false;
            }
            string expectedOutputDirectory = operations.GetFullPath(
                Path.Combine(
                    parameters.RunDirectory,
                    "p2.5a-recordings"))
                .TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar);
            string actualOutputDirectory = operations.GetFullPath(
                operations.GetDirectoryName(outputPath) ?? string.Empty)
                .TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar);
            if (!string.Equals(
                    expectedOutputDirectory,
                    actualOutputDirectory,
                    StringComparison.OrdinalIgnoreCase))
            {
                error =
                    "Published MP4 is outside this run's recording directory.";
                return false;
            }
            long mp4Size = operations.GetFileLength(outputPath);
            if (mp4Size <= 0 || summary.Mp4Size != mp4Size)
            {
                error =
                    $"Published MP4 size mismatch: actual={mp4Size}; summary={summary.Mp4Size}.";
                return false;
            }

            string diagnosticsDirectory = operations.GetFullPath(
                Path.Combine(
                    parameters.RunDirectory,
                    "diagnostic-logs",
                    "level-1",
                    "level-2",
                    "level-3"));
            string sourceReaderValidation =
                EvidenceWriter.ReadSourceReaderValidation(
                    diagnosticsDirectory,
                    terminalSnapshot.SessionId,
                    operations);
            if (!string.Equals(
                    sourceReaderValidation,
                    "PASS",
                    StringComparison.Ordinal) ||
                !string.Equals(
                    sourceReaderValidation,
                    summary.SourceReaderValidation,
                    StringComparison.Ordinal))
            {
                error =
                    "SourceReader validation was not independently reread as PASS.";
                return false;
            }

            GateEvaluation recomputedGates = GateEvaluation.Create(
                summary.EndReason ==
                    LongRunEndReason.CancellationRequested,
                parameters.DurationSeconds,
                summary.ActualWallDurationSeconds,
                terminalSnapshot,
                summary.TerminalSnapshotReadAfterStop,
                sourceReaderValidation,
                jsonl,
                observations,
                summary.PreviewClose,
                summary.RelatedProcessesAfterPreviewClose,
                summary.RelatedProcessesAtStart,
                summary.RelatedProcessesAtEnd,
                summary.LoadedModules,
                summary.Git!,
                summary.ProcessStart,
                summary.ProcessEnd,
                operations);
            if (!recomputedGates.Passed ||
                !GateResultsMatch(
                    summary.GateMatrix!,
                    recomputedGates,
                    out error))
            {
                if (error.Length == 0)
                {
                    error =
                        "Independently recomputed publication Gates did not pass.";
                }
                return false;
            }

            facts = new LongRunPublicationFacts(
                jsonl,
                samples.AsReadOnly(),
                terminalSample,
                mp4Size,
                sourceReaderValidation,
                recomputedGates);
            error = string.Empty;
            return true;
        }
        catch (Exception exception)
        {
            error =
                $"Publication evidence revalidation failed: " +
                $"{exception.GetType().Name}: {exception.Message}";
            return false;
        }
    }

    private static bool TryValidateSuccessfulSummaryFacts(
        LongRunSummary summary,
        out string error)
    {
        LongRunOptions parameters = summary.Parameters!;
        ManagedRecordingSnapshot terminal = summary.TerminalSnapshot!;
        EvidenceFileValidation jsonl = summary.SnapshotsJsonl!;
        error = string.Empty;
        if (summary.StartUtc == default ||
            summary.EndUtc < summary.StartUtc ||
            !double.IsFinite(summary.ActualWallDurationSeconds) ||
            summary.ActualWallDurationSeconds < 0 ||
            summary.MissedSampleCount < 0 ||
            parameters.DurationSeconds <= 0 ||
            parameters.SampleIntervalMilliseconds <= 0 ||
            !Path.IsPathFullyQualified(parameters.RunDirectory) ||
            !Path.IsPathFullyQualified(parameters.SummaryJsonPath) ||
            !Path.IsPathFullyQualified(parameters.SnapshotsJsonlPath))
        {
            error = "Successful Summary contains invalid timing, count, or path parameters.";
            return false;
        }
        if (!jsonl.WriterClosed ||
            !jsonl.Exists ||
            !jsonl.NonEmpty ||
            !jsonl.EveryLineParsed ||
            !jsonl.Passed ||
            jsonl.ParsedLineCount != summary.SampleCount ||
            jsonl.ExpectedLineCount != summary.SampleCount ||
            jsonl.TerminalSampleCount != 1 ||
            !string.IsNullOrEmpty(jsonl.Error))
        {
            error = "Successful Summary contains incomplete JSONL facts.";
            return false;
        }
        if (terminal.State != ManagedRecordingState.Completed ||
            terminal.LastResult != NativeMethods.Result.Ok ||
            terminal.StartUtc is null ||
            terminal.Elapsed.Ticks <= 0 ||
            string.IsNullOrWhiteSpace(terminal.SessionId) ||
            string.IsNullOrWhiteSpace(terminal.OutputPath) ||
            string.IsNullOrWhiteSpace(terminal.PublishedPath) ||
            !string.IsNullOrEmpty(terminal.ErrorMessage) ||
            !terminal.OutputSuccess ||
            !terminal.ReadyToPublish ||
            !terminal.Published ||
            !terminal.PublishAttempted ||
            terminal.PublishHResult < 0 ||
            !terminal.FinalizeAttempted ||
            terminal.FinalizeHResult < 0 ||
            terminal.FailureHResult < 0 ||
            terminal.FinalizeCount != 1 ||
            terminal.ActiveEncoder ||
            terminal.ResidualOutstanding != 0 ||
            terminal.OutputCleanupAttempted ||
            terminal.OutputCleanupSucceeded ||
            terminal.OutputCleanupHResult < 0 ||
            terminal.FramesSubmitted == 0)
        {
            error = "Successful Summary contains contradictory terminal recording facts.";
            return false;
        }
        if (summary.StopRequestedUtc is null ||
            summary.FinalizeCompletedUtc is null ||
            summary.StopRequestedUtc > summary.FinalizeCompletedUtc ||
            summary.FinalizeCompletedUtc > summary.EndUtc ||
            summary.FinalizeDurationMilliseconds is null or < 0 ||
            summary.FinalNativePts100ns != terminal.Elapsed.Ticks ||
            !string.Equals(
                summary.SessionGuid,
                terminal.SessionId,
                StringComparison.Ordinal) ||
            !string.Equals(
                summary.Mp4Path,
                terminal.PublishedPath,
                StringComparison.Ordinal) ||
            !Path.IsPathFullyQualified(summary.Mp4Path) ||
            !string.Equals(
                Path.GetExtension(summary.Mp4Path),
                ".mp4",
                StringComparison.OrdinalIgnoreCase))
        {
            error = "Successful Summary timing or terminal identity facts disagree.";
            return false;
        }
        if (!ProcessMetricsAreComplete(summary.ProcessStart) ||
            !ProcessMetricsAreComplete(summary.ProcessMaximum) ||
            !ProcessMetricsAreComplete(summary.ProcessEnd) ||
            !RelatedProcessesAreClear(summary.RelatedProcessesAtStart) ||
            !RelatedProcessesAreClear(summary.RelatedProcessesAtEnd) ||
            !RelatedProcessesAreClear(
                summary.RelatedProcessesBeforePreviewClose) ||
            !RelatedProcessesAreClear(
                summary.RelatedProcessesAfterPreviewClose))
        {
            error = "Successful Summary contains incomplete process evidence.";
            return false;
        }
        return true;
    }

    private static bool TerminalSampleMatchesSnapshot(
        LongRunSample sample,
        ManagedRecordingSnapshot snapshot) =>
        string.Equals(
            sample.RecordingState,
            snapshot.State.ToString(),
            StringComparison.Ordinal) &&
        string.Equals(
            sample.SessionGuid,
            snapshot.SessionId,
            StringComparison.Ordinal) &&
        sample.NativePtsElapsed100ns == snapshot.Elapsed.Ticks &&
        string.Equals(
            sample.OutputPath,
            snapshot.OutputPath,
            StringComparison.Ordinal) &&
        sample.FramesSubmitted == snapshot.FramesSubmitted &&
        sample.ActiveEncoder == snapshot.ActiveEncoder &&
        sample.FinalizeAttempted == snapshot.FinalizeAttempted &&
        sample.FinalizeHResult == snapshot.FinalizeHResult &&
        sample.FinalizeCount == snapshot.FinalizeCount &&
        sample.OutputSuccess == snapshot.OutputSuccess &&
        sample.FailureHResult == snapshot.FailureHResult &&
        sample.OutputCleanupAttempted ==
            snapshot.OutputCleanupAttempted &&
        sample.OutputCleanupSucceeded ==
            snapshot.OutputCleanupSucceeded &&
        sample.OutputCleanupHResult ==
            snapshot.OutputCleanupHResult &&
        sample.ResidualOutstanding == snapshot.ResidualOutstanding;

    private static bool ProcessMetricsAreComplete(ProcessMetrics? metrics) =>
        metrics is not null &&
        metrics.CapturedUtc != default &&
        !string.IsNullOrWhiteSpace(metrics.Phase) &&
        metrics.Available &&
        metrics.WorkingSet is >= 0 &&
        metrics.PrivateMemorySize is >= 0 &&
        metrics.HandleCount is >= 0 &&
        metrics.ThreadCount is >= 0 &&
        string.IsNullOrEmpty(metrics.Error);

    private static bool RelatedProcessesAreClear(
        RelatedProcessEvidence? evidence) =>
        evidence is not null &&
        evidence.CapturedUtc != default &&
        evidence.Processes is not null &&
        evidence.Processes.Count == 0 &&
        evidence.NoneFound &&
        string.IsNullOrEmpty(evidence.Error);

    private static bool ProcessMaximumMatches(
        ProcessMetrics? summary,
        ProcessMetrics? recomputed) =>
        ProcessMetricsAreComplete(summary) &&
        ProcessMetricsAreComplete(recomputed) &&
        summary!.WorkingSet == recomputed!.WorkingSet &&
        summary.PrivateMemorySize == recomputed.PrivateMemorySize &&
        summary.HandleCount == recomputed.HandleCount &&
        summary.ThreadCount == recomputed.ThreadCount;

    private static bool GateResultsMatch(
        GateEvaluation declared,
        GateEvaluation recomputed,
        out string error)
    {
        error = string.Empty;
        Dictionary<string, GateResult> declaredByName =
            declared.Gates.ToDictionary(
                gate => gate.Name,
                StringComparer.Ordinal);
        foreach (GateResult actual in recomputed.Gates)
        {
            if (!declaredByName.TryGetValue(
                    actual.Name,
                    out GateResult? reported) ||
                reported.Required != actual.Required ||
                reported.Passed != actual.Passed)
            {
                error =
                    $"Gate '{actual.Name}' disagrees with independently recomputed facts.";
                return false;
            }
        }
        if (declared.Passed != recomputed.Passed)
        {
            error =
                "GateMatrix.Passed disagrees with independently recomputed facts.";
            return false;
        }
        return true;
    }

    internal static bool TryExpectedVerdict(
        int exitCode,
        out string verdict)
    {
        if (!Enum.IsDefined(typeof(LongRunExitCode), exitCode))
        {
            verdict = string.Empty;
            return false;
        }
        verdict = (LongRunExitCode)exitCode switch
        {
            LongRunExitCode.Pass => "PASS",
            LongRunExitCode.CanceledSafely => "CANCELED-SAFELY",
            _ => "BLOCKED",
        };
        return true;
    }

    internal static bool IsLegalRecordingTransition(
        string prior,
        string current)
    {
        if (prior == current)
        {
            return true;
        }
        return (prior, current) switch
        {
            ("Idle", "Starting" or "Recording" or "Failed") => true,
            ("Starting", "Recording" or "Failed") => true,
            ("Recording", "Stopping" or "Completed" or "Failed") => true,
            ("Stopping", "Completed" or "Failed") => true,
            _ => false,
        };
    }

    private static bool TryValidateExactProperties(
        JsonElement root,
        HashSet<string> expected,
        out string error)
    {
        error = string.Empty;
        if (root.ValueKind != JsonValueKind.Object)
        {
            error = "JSON root is not an object.";
            return false;
        }
        HashSet<string> seen = new(StringComparer.Ordinal);
        foreach (JsonProperty property in root.EnumerateObject())
        {
            if (!expected.Contains(property.Name) || !seen.Add(property.Name))
            {
                error = $"JSON contains duplicate or unknown property '{property.Name}'.";
                return false;
            }
        }
        if (seen.Count != expected.Count)
        {
            string missing = string.Join(
                ", ",
                expected.Where(name => !seen.Contains(name)).OrderBy(name => name));
            error = $"JSON is missing required properties: {missing}.";
            return false;
        }
        return true;
    }
}

internal sealed record AtomicSummaryWriteResult(
    bool Passed,
    string FinalPath,
    long? FinalSize,
    string Error);

internal static class LongRunResultPublisher
{
    internal static LongRunExitCode Publish(
        string summaryPath,
        LongRunSummary summary,
        LongRunExitCode intendedExitCode,
        TextWriter output,
        TextWriter error,
        ILongRunFileOperations? fileOperations = null)
    {
        ILongRunFileOperations operations = fileOperations ??
            PhysicalLongRunFileOperations.Instance;
        AtomicSummaryWriteResult? publish = null;
        try
        {
            string consistencyError = string.Empty;
            if (summary.ExitCode != (int)intendedExitCode ||
                !LongRunEvidenceSchema.TryExpectedVerdict(
                    (int)intendedExitCode,
                    out string intendedVerdict) ||
                !string.Equals(
                    summary.Verdict,
                    intendedVerdict,
                    StringComparison.Ordinal) ||
                !LongRunEvidenceSchema.TryValidateSummary(
                    summary,
                    out consistencyError))
            {
                string detail = string.IsNullOrWhiteSpace(consistencyError)
                    ? $"Summary Verdict/Exit mismatch: verdict={summary.Verdict}; " +
                      $"summaryExit={summary.ExitCode}; intendedExit={(int)intendedExitCode}."
                    : consistencyError;
                WriteBlocked(error, detail);
                return LongRunExitCode.SummaryPublishFailed;
            }
            if (!LongRunEvidenceSchema.TryValidatePublicationEvidence(
                    summary,
                    operations,
                    out _,
                    out consistencyError))
            {
                WriteBlocked(error, consistencyError);
                return LongRunExitCode.SummaryPublishFailed;
            }

            publish = EvidenceWriter.PublishSummaryAtomically(
                summaryPath,
                summary,
                operations);
            if (!publish.Passed)
            {
                WriteBlocked(error, publish.Error);
                return LongRunExitCode.SummaryPublishFailed;
            }

            output.WriteLine(
                $"LONG-RUN-RESULT: {summary.Verdict}; " +
                $"exit={(int)intendedExitCode}; summary={publish.FinalPath}");
            return intendedExitCode;
        }
        catch (Exception exception)
        {
            string cleanupError = string.Empty;
            if (publish?.Passed == true)
            {
                EvidenceWriter.RemovePublishedEvidence(
                    publish.FinalPath,
                    operations,
                    out cleanupError);
            }
            string detail =
                $"{exception.GetType().Name}: {exception.Message}";
            if (!string.IsNullOrWhiteSpace(cleanupError))
            {
                detail += $"; {cleanupError}";
            }
            WriteBlocked(error, detail);
            return LongRunExitCode.SummaryPublishFailed;
        }
    }

    private static void WriteBlocked(TextWriter error, string detail)
    {
        try
        {
            error.WriteLine(
                $"LONG-RUN-RESULT: BLOCKED; " +
                $"exit={(int)LongRunExitCode.SummaryPublishFailed}; " +
                $"summary-publish-error={detail}");
        }
        catch
        {
            // A broken diagnostic stream cannot change the fail-closed exit.
        }
    }
}

internal sealed class EvidenceWriter : IDisposable
{
    private readonly ILongRunFileOperations _fileOperations;
    private readonly string _finalSnapshotsPath;
    private readonly string _temporarySnapshotsPath;
    private readonly StreamWriter _snapshotWriter;
    private bool _closed;
    private bool _published;
    private EvidenceFileValidation? _finalValidation;
    private string? _expectedSessionGuid;
    private LongRunSample? _expectedTerminalSample;

    internal EvidenceWriter(
        string snapshotsPath,
        ILongRunFileOperations? fileOperations = null)
    {
        _fileOperations = fileOperations ??
            PhysicalLongRunFileOperations.Instance;
        Stream? stream = null;
        StreamWriter? writer = null;
        try
        {
            _finalSnapshotsPath = _fileOperations.GetFullPath(snapshotsPath);
            string directory = _fileOperations.GetDirectoryName(
                _finalSnapshotsPath) ?? throw new InvalidOperationException(
                    "Snapshot JSONL parent directory is unavailable.");
            _fileOperations.CreateDirectory(directory);
            if (_fileOperations.FileExists(_finalSnapshotsPath) ||
                _fileOperations.DirectoryExists(_finalSnapshotsPath))
            {
                throw new IOException(
                    "Final Snapshot JSONL path already exists; overwrite is forbidden.");
            }
            _temporarySnapshotsPath = Path.Combine(
                directory,
                $".{Path.GetFileName(_finalSnapshotsPath)}.incomplete-{Guid.NewGuid():N}.tmp");
            stream = _fileOperations.CreateNewWrite(
                _temporarySnapshotsPath,
                FileShare.Read);
            writer = new StreamWriter(stream, new UTF8Encoding(false))
            {
                AutoFlush = true,
            };
            stream = null;
            _snapshotWriter = writer;
            writer = null;
        }
        catch (Exception error)
        {
            writer?.Dispose();
            stream?.Dispose();
            throw new InvalidOperationException(
                $"Snapshot JSONL initialization failed: " +
                $"{error.GetType().Name}: {error.Message}",
                error);
        }
    }

    internal void WriteSample(LongRunSample sample)
    {
        if (_closed)
        {
            throw new InvalidOperationException("Snapshot JSONL writer is closed.");
        }
        _expectedSessionGuid ??= sample.SessionGuid;
        if (sample.SampleType == "terminal")
        {
            _expectedTerminalSample = sample;
        }
        _snapshotWriter.WriteLine(JsonSerializer.Serialize(
            sample,
            LongRunEvidenceSchema.JsonOptions));
    }

    internal EvidenceFileValidation CloseAndValidate(int expectedLineCount)
    {
        if (_finalValidation is not null)
        {
            return _finalValidation;
        }
        bool closedCleanly = TryCloseSnapshotWriter(out string closeError);
        if (!closedCleanly)
        {
            _finalValidation = FailedJsonlValidation(
                expectedLineCount,
                closeError.Length == 0
                    ? "JSONL writer did not close."
                    : closeError);
            TryDeleteTemporary();
            return _finalValidation;
        }

        bool moveAttempted = false;
        try
        {
            EvidenceFileValidation temporary = ValidateJsonl(
                _temporarySnapshotsPath,
                expectedLineCount,
                writerClosed: true,
                fileOperations: _fileOperations,
                expectedSessionGuid: _expectedSessionGuid,
                expectedTerminal: _expectedTerminalSample);
            if (!temporary.Passed)
            {
                _finalValidation = temporary with
                {
                    Error = $"Temporary JSONL validation failed: {temporary.Error}",
                    Passed = false,
                };
                TryDeleteTemporary();
                return _finalValidation;
            }
            if (_fileOperations.FileExists(_finalSnapshotsPath) ||
                _fileOperations.DirectoryExists(_finalSnapshotsPath))
            {
                throw new IOException(
                    "Final Snapshot JSONL path appeared before publication; overwrite is forbidden.");
            }
            moveAttempted = true;
            _fileOperations.MoveNoOverwrite(
                _temporarySnapshotsPath,
                _finalSnapshotsPath);
            _published = true;
            EvidenceFileValidation final = ValidateJsonl(
                _finalSnapshotsPath,
                expectedLineCount,
                writerClosed: true,
                fileOperations: _fileOperations,
                expectedSessionGuid: _expectedSessionGuid,
                expectedTerminal: _expectedTerminalSample);
            if (!final.Passed)
            {
                throw new InvalidDataException(
                    $"Final JSONL readback failed: {final.Error}");
            }
            _finalValidation = final;
            return final;
        }
        catch (Exception error)
        {
            string cleanupError = string.Empty;
            bool moveCompleted = _published ||
                MoveAppearsCompletedAfterException(moveAttempted);
            if (moveCompleted)
            {
                RemovePublishedEvidence(
                    _finalSnapshotsPath,
                    _fileOperations,
                    out cleanupError);
            }
            TryDeleteTemporary();
            string diagnostic =
                $"JSONL publication failed: {error.GetType().Name}: {error.Message}";
            if (!string.IsNullOrWhiteSpace(cleanupError))
            {
                diagnostic += $"; {cleanupError}";
            }
            _finalValidation = FailedJsonlValidation(
                expectedLineCount,
                diagnostic);
            return _finalValidation;
        }
    }

    internal static EvidenceFileValidation ValidateJsonl(
        string path,
        int expectedLineCount,
        bool writerClosed,
        string priorError = "",
        ILongRunFileOperations? fileOperations = null,
        string? expectedSessionGuid = null,
        LongRunSample? expectedTerminal = null,
        Action<LongRunSample>? sampleObserver = null)
    {
        ILongRunFileOperations operations = fileOperations ??
            PhysicalLongRunFileOperations.Instance;
        bool exists = false;
        bool nonEmpty = false;
        bool parsed = true;
        int lineCount = 0;
        int terminalCount = 0;
        string? expectedSession = null;
        string? priorState = null;
        long priorPts = -1;
        bool terminalSeen = false;
        LongRunSample? parsedTerminal = null;
        List<string> errors = [];
        if (!string.IsNullOrWhiteSpace(priorError))
        {
            errors.Add(priorError);
        }
        if (!writerClosed)
        {
            errors.Add("JSONL writer was not closed.");
        }
        try
        {
            string fullPath = operations.GetFullPath(path);
            exists = operations.FileExists(fullPath);
            nonEmpty = exists && operations.GetFileLength(fullPath) > 0;
            if (exists)
            {
                foreach (string line in operations.ReadLines(fullPath))
                {
                    if (string.IsNullOrWhiteSpace(line))
                    {
                        parsed = false;
                        errors.Add($"JSONL line {lineCount + 1} is empty.");
                        break;
                    }
                    lineCount++;
                    using JsonDocument document = JsonDocument.Parse(line);
                    if (!LongRunEvidenceSchema.TryDeserializeSample(
                            document.RootElement,
                            out LongRunSample sample,
                            out string schemaError))
                    {
                        parsed = false;
                        errors.Add(
                            $"JSONL line {lineCount} schema failed: {schemaError}");
                        break;
                    }
                    if (expectedSession is null)
                    {
                        expectedSession = sample.SessionGuid;
                    }
                    else if (!string.Equals(
                                 expectedSession,
                                 sample.SessionGuid,
                                 StringComparison.Ordinal))
                    {
                        parsed = false;
                        errors.Add($"JSONL line {lineCount} changed Session GUID.");
                        break;
                    }
                    if (sample.NativePtsElapsed100ns < priorPts)
                    {
                        parsed = false;
                        errors.Add($"JSONL line {lineCount} regressed Native PTS.");
                        break;
                    }
                    if (priorState is not null &&
                        !LongRunEvidenceSchema.IsLegalRecordingTransition(
                            priorState,
                            sample.RecordingState))
                    {
                        parsed = false;
                        errors.Add(
                            $"JSONL line {lineCount} has illegal state transition " +
                            $"{priorState}->{sample.RecordingState}.");
                        break;
                    }
                    if (terminalSeen)
                    {
                        parsed = false;
                        errors.Add("JSONL contains a sample after its terminal sample.");
                        break;
                    }
                    priorPts = sample.NativePtsElapsed100ns;
                    priorState = sample.RecordingState;
                    if (sample.SampleType == "terminal")
                    {
                        terminalCount++;
                        terminalSeen = true;
                        parsedTerminal = sample;
                    }
                    sampleObserver?.Invoke(sample);
                }
            }
        }
        catch (Exception error)
        {
            parsed = false;
            errors.Add($"JSONL parse failed: {error.GetType().Name}: {error.Message}");
        }
        if (!exists)
        {
            errors.Add("JSONL file does not exist.");
        }
        if (!nonEmpty)
        {
            errors.Add("JSONL file is empty.");
        }
        if (lineCount != expectedLineCount)
        {
            errors.Add($"JSONL line count {lineCount} != expected {expectedLineCount}.");
        }
        if (terminalCount != 1)
        {
            errors.Add($"JSONL terminal sample count is {terminalCount}, expected 1.");
        }
        if (!string.IsNullOrWhiteSpace(expectedSessionGuid) &&
            !string.Equals(
                expectedSession,
                expectedSessionGuid,
                StringComparison.Ordinal))
        {
            errors.Add(
                $"JSONL Session '{expectedSession ?? "unavailable"}' does not match " +
                $"the run Session '{expectedSessionGuid}'.");
        }
        if (expectedTerminal is not null)
        {
            string expectedCanonical = JsonSerializer.Serialize(
                expectedTerminal,
                LongRunEvidenceSchema.JsonOptions);
            string actualCanonical = parsedTerminal is null
                ? string.Empty
                : JsonSerializer.Serialize(
                    parsedTerminal,
                    LongRunEvidenceSchema.JsonOptions);
            if (!string.Equals(
                    expectedCanonical,
                    actualCanonical,
                    StringComparison.Ordinal))
            {
                errors.Add(
                    "JSONL terminal sample does not match the terminal Snapshot written by the run.");
            }
        }
        bool passed = writerClosed && exists && nonEmpty && parsed &&
            lineCount == expectedLineCount && terminalCount == 1 &&
            errors.Count == 0;
        return new EvidenceFileValidation(
            writerClosed,
            exists,
            nonEmpty,
            parsed,
            lineCount,
            expectedLineCount,
            terminalCount,
            passed,
            string.Join("; ", errors));
    }

    internal static AtomicSummaryWriteResult PublishSummaryAtomically(
        string finalPath,
        LongRunSummary summary,
        ILongRunFileOperations? fileOperations = null)
    {
        ILongRunFileOperations operations = fileOperations ??
            PhysicalLongRunFileOperations.Instance;
        string fullFinalPath = finalPath;
        string temporaryPath = string.Empty;
        bool finalPublished = false;
        bool moveAttempted = false;
        try
        {
            fullFinalPath = operations.GetFullPath(finalPath);
            string declaredSummaryPath = operations.GetFullPath(
                summary.Parameters?.SummaryJsonPath ?? string.Empty);
            if (!string.Equals(
                    fullFinalPath,
                    declaredSummaryPath,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "Publication path does not match Summary parameters.");
            }
            string directory = operations.GetDirectoryName(fullFinalPath) ??
                throw new InvalidOperationException(
                    "Summary parent directory is unavailable.");
            operations.CreateDirectory(directory);
            if (operations.FileExists(fullFinalPath) ||
                operations.DirectoryExists(fullFinalPath))
            {
                throw new IOException(
                    "Final summary path already exists; overwrite is forbidden.");
            }
            if (!LongRunEvidenceSchema.TryValidateSummary(
                    summary,
                    out string summaryError))
            {
                throw new InvalidDataException(summaryError);
            }
            if (!LongRunEvidenceSchema.TryValidatePublicationEvidence(
                    summary,
                    operations,
                    out _,
                    out string publicationError))
            {
                throw new InvalidDataException(publicationError);
            }

            temporaryPath = Path.Combine(
                directory,
                $".{Path.GetFileName(fullFinalPath)}.incomplete-{Guid.NewGuid():N}.tmp");
            using (Stream stream = operations.CreateNewWrite(
                       temporaryPath,
                       FileShare.Read))
            {
                JsonSerializer.Serialize(
                    stream,
                    summary,
                    new JsonSerializerOptions(
                        LongRunEvidenceSchema.JsonOptions)
                    {
                        WriteIndented = true,
                    });
                operations.FlushToDisk(stream);
            }
            ValidateSummaryFile(
                temporaryPath,
                summary,
                operations);
            moveAttempted = true;
            operations.MoveNoOverwrite(temporaryPath, fullFinalPath);
            finalPublished = true;
            long finalSize = ValidateSummaryFile(
                fullFinalPath,
                summary,
                operations);
            return new AtomicSummaryWriteResult(
                true,
                fullFinalPath,
                finalSize,
                string.Empty);
        }
        catch (Exception error)
        {
            string diagnostic = $"{error.GetType().Name}: {error.Message}";
            bool moveCompleted = finalPublished ||
                MoveAppearsCompletedAfterException(
                    moveAttempted,
                    temporaryPath,
                    fullFinalPath,
                    operations);
            if (moveCompleted)
            {
                RemovePublishedEvidence(
                    fullFinalPath,
                    operations,
                    out string cleanupError);
                if (!string.IsNullOrWhiteSpace(cleanupError))
                {
                    diagnostic += $"; {cleanupError}";
                }
            }
            if (!string.IsNullOrWhiteSpace(temporaryPath))
            {
                TryDelete(temporaryPath, operations, out string temporaryError);
                if (!string.IsNullOrWhiteSpace(temporaryError))
                {
                    diagnostic += $"; {temporaryError}";
                }
            }
            return new AtomicSummaryWriteResult(
                false,
                fullFinalPath,
                null,
                diagnostic);
        }
    }

    internal static string ReadSourceReaderValidation(
        string diagnosticsDirectory,
        string sessionId,
        ILongRunFileOperations? fileOperations = null)
    {
        ILongRunFileOperations operations = fileOperations ??
            PhysicalLongRunFileOperations.Instance;
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return "unavailable";
        }
        try
        {
            string path = operations.GetFullPath(Path.Combine(
                diagnosticsDirectory,
                $"p2.4-encoder-{sessionId}.jsonl"));
            if (!operations.FileExists(path))
            {
                return "unavailable";
            }
            string? line = operations.ReadLines(path)
                .LastOrDefault(value => !string.IsNullOrWhiteSpace(value));
            if (line is null)
            {
                return "unavailable";
            }
            using JsonDocument document = JsonDocument.Parse(line);
            return document.RootElement.TryGetProperty(
                    "SourceReaderValidation",
                    out JsonElement value)
                ? value.GetString() ?? "unavailable"
                : "unavailable";
        }
        catch (Exception error)
        {
            return $"unavailable: {error.GetType().Name}";
        }
    }

    public void Dispose()
    {
        TryCloseSnapshotWriter(out _);
        if (!_published)
        {
            TryDeleteTemporary();
        }
    }

    internal static bool RemovePublishedEvidence(
        string path,
        ILongRunFileOperations fileOperations,
        out string error)
    {
        error = string.Empty;
        try
        {
            if (!fileOperations.FileExists(path))
            {
                return true;
            }
            string invalidPath = path +
                $".invalid-{DateTime.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}";
            fileOperations.MoveNoOverwrite(path, invalidPath);
            return true;
        }
        catch (Exception quarantineError)
        {
            try
            {
                if (fileOperations.FileExists(path))
                {
                    fileOperations.DeleteFile(path);
                }
                error =
                    $"Evidence quarantine failed but final path was deleted: " +
                    $"{quarantineError.GetType().Name}: {quarantineError.Message}";
                return true;
            }
            catch (Exception deleteError)
            {
                error =
                    $"Evidence cleanup failed; final path may remain: " +
                    $"quarantine={quarantineError.GetType().Name}: {quarantineError.Message}; " +
                    $"delete={deleteError.GetType().Name}: {deleteError.Message}";
                return false;
            }
        }
    }

    private static long ValidateSummaryFile(
        string path,
        LongRunSummary expected,
        ILongRunFileOperations fileOperations)
    {
        if (!fileOperations.FileExists(path))
        {
            throw new InvalidDataException("Summary file is missing.");
        }
        long size = fileOperations.GetFileLength(path);
        if (size <= 0)
        {
            throw new InvalidDataException("Summary file is empty.");
        }
        LongRunSummary actual;
        using (Stream stream = fileOperations.OpenRead(path))
        using (JsonDocument document = JsonDocument.Parse(stream))
        {
            if (!LongRunEvidenceSchema.TryDeserializeSummary(
                    document.RootElement,
                    out actual,
                    out string error))
            {
                throw new InvalidDataException(
                    $"Summary schema validation failed: {error}");
            }
        }
        string expectedCanonical = JsonSerializer.Serialize(
            expected,
            LongRunEvidenceSchema.JsonOptions);
        string actualCanonical = JsonSerializer.Serialize(
            actual,
            LongRunEvidenceSchema.JsonOptions);
        if (!string.Equals(
                expectedCanonical,
                actualCanonical,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "Summary readback does not match the object that was published.");
        }
        if (!LongRunEvidenceSchema.TryValidatePublicationEvidence(
                actual,
                fileOperations,
                out _,
                out string publicationError))
        {
            throw new InvalidDataException(
                $"Summary publication evidence failed: {publicationError}");
        }
        return size;
    }

    private EvidenceFileValidation FailedJsonlValidation(
        int expectedLineCount,
        string error) => new(
            _closed,
            false,
            false,
            false,
            0,
            expectedLineCount,
            0,
            false,
            error);

    private bool TryCloseSnapshotWriter(out string error)
    {
        List<string> errors = [];
        if (_closed)
        {
            error = string.Empty;
            return true;
        }
        try
        {
            _snapshotWriter.Flush();
            _fileOperations.FlushToDisk(_snapshotWriter.BaseStream);
        }
        catch (Exception exception)
        {
            errors.Add(
                $"JSONL flush failed: {exception.GetType().Name}: {exception.Message}");
        }
        try
        {
            _snapshotWriter.Dispose();
            _closed = true;
        }
        catch (Exception exception)
        {
            errors.Add(
                $"JSONL close failed: {exception.GetType().Name}: {exception.Message}");
            try
            {
                _snapshotWriter.BaseStream.Dispose();
                _closed = true;
            }
            catch (Exception streamException)
            {
                errors.Add(
                    $"JSONL stream close failed: {streamException.GetType().Name}: " +
                    streamException.Message);
            }
        }
        error = string.Join("; ", errors);
        return _closed && errors.Count == 0;
    }

    private bool MoveAppearsCompletedAfterException(bool moveAttempted) =>
        MoveAppearsCompletedAfterException(
            moveAttempted,
            _temporarySnapshotsPath,
            _finalSnapshotsPath,
            _fileOperations);

    private static bool MoveAppearsCompletedAfterException(
        bool moveAttempted,
        string temporaryPath,
        string finalPath,
        ILongRunFileOperations fileOperations)
    {
        if (!moveAttempted || string.IsNullOrWhiteSpace(temporaryPath))
        {
            return false;
        }
        try
        {
            // A failed no-overwrite move leaves its source in place.  Only the
            // source-absent/final-present combination can represent our move
            // completing before an injected post-operation exception.
            return !fileOperations.FileExists(temporaryPath) &&
                fileOperations.FileExists(finalPath);
        }
        catch
        {
            return false;
        }
    }

    private void TryDeleteTemporary()
    {
        TryDelete(
            _temporarySnapshotsPath,
            _fileOperations,
            out _);
    }

    private static bool TryDelete(
        string path,
        ILongRunFileOperations fileOperations,
        out string error)
    {
        error = string.Empty;
        try
        {
            if (fileOperations.FileExists(path))
            {
                fileOperations.DeleteFile(path);
            }
            return true;
        }
        catch (Exception exception)
        {
            error =
                $"Temporary evidence cleanup failed: " +
                $"{exception.GetType().Name}: {exception.Message}";
            return false;
        }
    }
}

internal sealed class RunObservations
{
    private readonly List<string> _states = [];
    private readonly List<ProcessMetrics> _processMetrics = [];
    private readonly HashSet<string> _sessionIds = new(StringComparer.Ordinal);
    private long _lastPts = -1;

    internal int SampleCount { get; private set; }
    internal bool PtsMonotonic { get; private set; } = true;
    internal bool StateSequenceLegal { get; private set; } = true;
    internal bool SessionConsistent =>
        _sessionIds.Count == 1 &&
        _sessionIds.All(value => !string.IsNullOrWhiteSpace(value));
    internal bool AllProcessMetricsAvailable =>
        _processMetrics.Count > 0 &&
        _processMetrics.All(metrics => metrics.Available);

    internal void Add(
        ManagedRecordingSnapshot snapshot,
        ProcessMetrics metrics)
    {
        SampleCount++;
        AddProcessMetrics(metrics);
        _sessionIds.Add(snapshot.SessionId);
        if (_lastPts > snapshot.Elapsed.Ticks)
        {
            PtsMonotonic = false;
        }
        _lastPts = snapshot.Elapsed.Ticks;
        string state = snapshot.State.ToString();
        if (_states.Count > 0 && !IsLegal(_states[^1], state))
        {
            StateSequenceLegal = false;
        }
        _states.Add(state);
    }

    internal void Add(LongRunSample sample)
    {
        SampleCount++;
        AddProcessMetrics(new ProcessMetrics(
            sample.UtcTimestamp,
            $"published-jsonl-{sample.SampleType}",
            true,
            sample.WorkingSet,
            sample.PrivateMemorySize,
            sample.HandleCount,
            sample.ThreadCount,
            string.Empty));
        _sessionIds.Add(sample.SessionGuid);
        if (_lastPts > sample.NativePtsElapsed100ns)
        {
            PtsMonotonic = false;
        }
        _lastPts = sample.NativePtsElapsed100ns;
        if (_states.Count > 0 &&
            !IsLegal(_states[^1], sample.RecordingState))
        {
            StateSequenceLegal = false;
        }
        _states.Add(sample.RecordingState);
    }

    internal void AddProcessMetrics(ProcessMetrics metrics) =>
        _processMetrics.Add(metrics);

    internal ProcessMetrics? CalculateMaximum()
    {
        ProcessMetrics[] available = _processMetrics
            .Where(metrics => metrics.Available)
            .ToArray();
        if (available.Length == 0)
        {
            return null;
        }
        return new ProcessMetrics(
            DateTimeOffset.UtcNow,
            "maximum-across-baseline-running-final",
            true,
            available.Max(metrics => metrics.WorkingSet!.Value),
            available.Max(metrics => metrics.PrivateMemorySize!.Value),
            available.Max(metrics => metrics.HandleCount!.Value),
            available.Max(metrics => metrics.ThreadCount!.Value),
            string.Empty);
    }

    private static bool IsLegal(string prior, string current)
    {
        if (prior == current)
        {
            return true;
        }
        return (prior, current) switch
        {
            ("Idle", "Starting" or "Recording" or "Failed") => true,
            ("Starting", "Recording" or "Failed") => true,
            ("Recording", "Stopping" or "Completed" or "Failed") => true,
            ("Stopping", "Completed" or "Failed") => true,
            _ => false,
        };
    }
}
