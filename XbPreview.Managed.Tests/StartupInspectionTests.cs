using System.Diagnostics;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows.Forms;
using XbPreview.Host;

namespace XbPreview.Managed.Tests;

internal static class StartupInspectionTests
{
    private const string CompletedSessionId =
        "27000000-0000-4000-8000-000000ABCDEF";
    private const string ReconciledSessionId =
        "27000000-0000-4000-8000-000000000402";
    private const string RetainSessionId =
        "27000000-0000-4000-8000-000000000403";
    private const string CorruptSessionId =
        "27000000-0000-4000-8000-000000000404";
    private const string UnsupportedSessionId =
        "27000000-0000-4000-8000-000000000406";

    internal static async Task RunAsync()
    {
        string root = Path.Combine(
            Directory.GetCurrentDirectory(),
            "artifacts",
            $"p2.6c4a-managed-startup-inspection-中文-{Environment.ProcessId}");
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }

        int completedCases = 0;
        try
        {
            VerifyManagedAndNativeAbi();
            completedCases++;

            string absentDiagnostic = CreateDiagnosticDirectory(root, "absent");
            StartupInspectionResult absent =
                new NativeHistoricalSessionInspector(absentDiagnostic).
                    Inspect(CancellationToken.None);
            string absentSessions = SessionsRoot(absentDiagnostic);
            Require(
                absent.Status == HistoricalSessionScanStatus.SessionsRootAbsent &&
                absent.SessionCount == 0 &&
                absent.Sessions.Count == 0 &&
                !Directory.Exists(absentSessions),
                "absent SessionsRoot is a successful non-mutating bridge result");
            completedCases++;

            string emptyDiagnostic = CreateDiagnosticDirectory(root, "empty");
            Directory.CreateDirectory(SessionsRoot(emptyDiagnostic));
            StartupInspectionResult empty =
                new NativeHistoricalSessionInspector(emptyDiagnostic).
                    Inspect(CancellationToken.None);
            Require(
                empty.Status == HistoricalSessionScanStatus.Success &&
                empty.SessionCount == 0 && empty.Sessions.Count == 0,
                "empty SessionsRoot crosses the formal Native/Managed bridge");
            completedCases++;

            VerifyActualHostAssemblyBridge(root);
            completedCases++;

            string fixtureDiagnostic = CreateDiagnosticDirectory(root, "mixed");
            RunNativeFixtureHelper(fixtureDiagnostic);
            string mediaRoot = MediaRoot(fixtureDiagnostic);
            Dictionary<string, ManifestEvidence> manifestsBefore =
                CaptureManifestEvidence(mediaRoot);
            Dictionary<string, byte[]> mediaBefore = CaptureMediaHashes(mediaRoot);

            StartupInspectionResult mixed =
                new NativeHistoricalSessionInspector(fixtureDiagnostic).
                    Inspect(CancellationToken.None);
            Require(
                mixed.Status == HistoricalSessionScanStatus.Success &&
                mixed.SessionCount == 5 && mixed.Sessions.Count == 5,
                "mixed historical Sessions are independently returned");
            completedCases++;

            HistoricalSessionInspection unsupported =
                RequireSession(mixed, UnsupportedSessionId);
            Require(
                unsupported.Classification ==
                    HistoricalSessionClassification.UnknownRetain &&
                unsupported.ManifestParseStatus ==
                    HistoricalSessionParseStatus.UnsupportedSchema &&
                unsupported.RetainUserMedia,
                "unsupported schema is mapped distinctly and retained");
            completedCases++;

            HistoricalSessionInspection completed =
                RequireSession(mixed, CompletedSessionId);
            Require(
                completed.Classification ==
                    HistoricalSessionClassification.CompletedConsistent &&
                completed.RetainUserMedia &&
                completed.FinalCandidateExists &&
                completed.DisplaySafePath.EndsWith(
                    $"{CompletedSessionId}.mp4", StringComparison.OrdinalIgnoreCase),
                "CompletedConsistent DTO mapping uses the real bridge path");
            completedCases++;

            HistoricalSessionInspection reconciled =
                RequireSession(mixed, ReconciledSessionId);
            Require(
                reconciled.Classification ==
                    HistoricalSessionClassification.
                        ReconciledCompletedConsistent &&
                reconciled.Classification !=
                    HistoricalSessionClassification.CompletedConsistent &&
                reconciled.RetainUserMedia,
                "reconciled history remains distinct from Native runtime Completed");
            completedCases++;

            HistoricalSessionInspection retain =
                RequireSession(mixed, RetainSessionId);
            Require(
                retain.Classification ==
                    HistoricalSessionClassification.PublishOutcomeUnprovenRetain &&
                retain.RetainUserMedia && retain.FinalCandidateExists,
                "unproven publish outcome is surfaced as read-only retain");
            completedCases++;

            HistoricalSessionInspection corrupt =
                RequireSession(mixed, CorruptSessionId);
            Require(
                corrupt.Classification ==
                    HistoricalSessionClassification.ManifestCorrupt &&
                corrupt.ManifestParseStatus ==
                    HistoricalSessionParseStatus.MalformedJson &&
                corrupt.RetainUserMedia,
                "corrupt Manifest is observable and does not fail startup");
            completedCases++;

            Require(
                SameManifestEvidence(
                    manifestsBefore, CaptureManifestEvidence(mediaRoot)) &&
                SameHashes(mediaBefore, CaptureMediaHashes(mediaRoot)),
                "formal Managed inspection changes no Manifest revision/bytes or media");
            completedCases++;

            await VerifyCoordinatorSingleFlightAsync();
            completedCases++;

            await VerifyBackendFailureDoesNotBlockPreviewAsync();
            completedCases++;

            await VerifyCloseBeforeCompletionHasNoLateCallbackAsync();
            completedCases++;

            VerifyRealMainFormStartupOrchestration();
            completedCases++;

            Require(completedCases == 15,
                "complete P2.6C-4A managed startup inspection matrix");
            Console.WriteLine(
                $"P2.6C-4A_MANAGED_STARTUP_INSPECTION_MATRIX={completedCases}");
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
            Require(!Directory.Exists(root),
                "managed startup inspection releases all fixture handles");
        }
    }

    private static unsafe void VerifyManagedAndNativeAbi()
    {
        NativeMethods.ValidateManagedLayout();
        Require(
            NativeMethods.XbPreview_GetApiVersion() ==
                NativeMethods.ApiVersion &&
            NativeMethods.ApiVersion == 0x0004_0004,
            "actual Native DLL exposes the expected API version");
        Require(
            sizeof(NativeMethods.HistoricalSessionScanAbiLayoutV1) == 32 &&
            sizeof(NativeMethods.HistoricalSessionScanOptionsV1) == 40 &&
            sizeof(NativeMethods.HistoricalSessionScanSummaryV1) == 64 &&
            sizeof(NativeMethods.HistoricalSessionItemV1) == 192 &&
            Marshal.OffsetOf<NativeMethods.HistoricalSessionItemV1>(
                nameof(NativeMethods.HistoricalSessionItemV1.Reasons)).ToInt32() == 16 &&
            Marshal.OffsetOf<NativeMethods.HistoricalSessionItemV1>(
                nameof(NativeMethods.HistoricalSessionItemV1.ObservedRevision)).ToInt32() == 48 &&
            Marshal.OffsetOf<NativeMethods.HistoricalSessionItemV1>(
                nameof(NativeMethods.HistoricalSessionItemV1.WorkingSize)).ToInt32() == 96 &&
            Marshal.OffsetOf<NativeMethods.HistoricalSessionItemV1>(
                nameof(NativeMethods.HistoricalSessionItemV1.Reserved5)).ToInt32() == 176,
            "Managed historical scan layout is exact Pack=8");

        NativeMethods.HistoricalSessionScanAbiLayoutV1 layout = new()
        {
            StructSize = (uint)sizeof(
                NativeMethods.HistoricalSessionScanAbiLayoutV1),
            AbiVersion = NativeMethods.HistoricalSessionScanAbiVersionV1,
        };
        Require(
            NativeMethods.XbPreview_GetHistoricalSessionScanAbiLayoutV1(
                ref layout) == NativeMethods.Result.Ok &&
            layout.PointerSize == 8 && layout.Packing == 8 &&
            layout.WcharSize == 2 &&
            layout.OptionsSize == 40 && layout.SummarySize == 64 &&
            layout.ItemSize == 192,
            "Native and Managed scan ABI layout agree");
    }

    private static void VerifyActualHostAssemblyBridge(string root)
    {
        string hostPath = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "XbPreview.Host.dll"));
        Require(File.Exists(hostPath), "actual Host DLL exists beside test binary");
        byte[] hash = SHA256.HashData(File.ReadAllBytes(hostPath));
        Require(hash.Any(value => value != 0), "actual Host DLL has a real fingerprint");

        Assembly assembly = Assembly.LoadFrom(hostPath);
        Require(
            string.Equals(
                Path.GetFullPath(assembly.Location), hostPath,
                StringComparison.OrdinalIgnoreCase),
            "reflection smoke test loaded the actual Host DLL path");
        Type inspectorType = assembly.GetType(
            "XbPreview.Host.NativeHistoricalSessionInspector",
            throwOnError: true)!;
        ConstructorInfo constructor = inspectorType.GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            [typeof(string)],
            modifiers: null)!;
        string diagnostic = CreateDiagnosticDirectory(root, "actual-host-dll");
        object inspector = constructor.Invoke([diagnostic]);
        MethodInfo inspect = inspectorType.GetMethod(
            "Inspect", BindingFlags.Instance | BindingFlags.Public)!;
        object result = inspect.Invoke(inspector, [CancellationToken.None])!;
        object status = result.GetType().GetProperty(
            "Status", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(result)!;
        object count = result.GetType().GetProperty(
            "SessionCount", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(result)!;
        Require(
            status.ToString() == nameof(
                HistoricalSessionScanStatus.SessionsRootAbsent) &&
            Convert.ToUInt32(count) == 0,
            "actual Host DLL executed its formal Native bridge");
        Console.WriteLine(
            $"P2.6C-4A_HOST_DLL={hostPath};SHA256={Convert.ToHexString(hash)}");
    }

    private static void RunNativeFixtureHelper(string diagnosticLogDirectory)
    {
        string executable = Path.Combine(
            AppContext.BaseDirectory, "XbPreview.Native.Tests.exe");
        Require(File.Exists(executable),
            "Native fixture helper executable exists in the Release output");
        ProcessStartInfo start = new()
        {
            FileName = executable,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        start.ArgumentList.Add("--p2.6c4a-fixture-helper");
        start.ArgumentList.Add(diagnosticLogDirectory);
        using Process process = Process.Start(start) ??
            throw new InvalidOperationException(
                "Failed to start Native startup-inspection fixture helper.");
        Require(process.WaitForExit(30_000),
            "Native fixture helper exits without force termination");
        Require(process.ExitCode == 0,
            $"Native fixture helper exit code is zero, actual={process.ExitCode}");
    }

    private static async Task VerifyCoordinatorSingleFlightAsync()
    {
        ControlledInspector inspector = new();
        await using StartupInspectionCoordinator coordinator = new(inspector);
        Task<StartupInspectionSnapshot> first = coordinator.StartAsync();
        Require(inspector.Entered.Wait(5_000),
            "single-flight inspector entered");
        Task<StartupInspectionSnapshot> second = coordinator.StartAsync();
        Require(ReferenceEquals(first, second),
            "concurrent startup inspection shares one in-flight Task");
        inspector.Release.Set();
        StartupInspectionSnapshot terminal = await first;
        Require(
            terminal.State == StartupInspectionState.Completed &&
            terminal.Result is not null && inspector.CallCount == 1,
            "single-flight inspection publishes one terminal fact");
    }

    private static async Task VerifyBackendFailureDoesNotBlockPreviewAsync()
    {
        await using (StartupInspectionCoordinator coordinator =
            new(new ThrowingInspector()))
        {
            StartupInspectionSnapshot terminal = await coordinator.StartAsync();
            Require(
                terminal.State == StartupInspectionState.Failed &&
                terminal.Error?.Contains(
                    nameof(IOException), StringComparison.Ordinal) == true,
                "scanner backend failure becomes a diagnostic terminal result");
        }

        PreviewLifecycleTests.Harness preview = new();
        await preview.InitializeAsync();
        PreviewLifecycleResult started = await preview.StartAsync();
        Require(
            started.Succeeded &&
            preview.Controller.State == PreviewLifecycleState.Previewing,
            "startup inspection failure does not block Preview lifecycle");
        await preview.Controller.DisposeAsync();
    }

    private static async Task VerifyCloseBeforeCompletionHasNoLateCallbackAsync()
    {
        ControlledInspector inspector = new();
        StartupInspectionCoordinator coordinator = new(inspector);
        int terminalCallbacks = 0;
        coordinator.SnapshotChanged += (_, snapshot) =>
        {
            if (snapshot.IsTerminal)
            {
                Interlocked.Increment(ref terminalCallbacks);
            }
        };
        Task<StartupInspectionSnapshot> run = coordinator.StartAsync();
        Require(inspector.Entered.Wait(5_000),
            "close-race inspector entered");
        Task close = coordinator.DisposeAsync().AsTask();
        Require(inspector.CancellationObserved.Wait(5_000),
            "close requests startup inspection cancellation");
        Require(!close.IsCompleted,
            "close waits for the in-flight inspector to acknowledge exit");
        inspector.Release.Set();
        await close;
        StartupInspectionSnapshot terminal = await run;
        await Task.Delay(50);
        Require(
            terminal.State == StartupInspectionState.Canceled &&
            terminalCallbacks == 0,
            "close-before-completion suppresses every late terminal callback");
    }

    private static void VerifyRealMainFormStartupOrchestration()
    {
        Exception? failure = null;
        using ManualResetEventSlim finished = new(false);
        Thread thread = new(() =>
        {
            StartupInspectionHostProxy? proxy = null;
            Form? form = null;
            try
            {
                Require(Thread.CurrentThread.GetApartmentState() ==
                    ApartmentState.STA,
                    "real MainForm startup test owns one STA thread");
                int staThreadId = Environment.CurrentManagedThreadId;
                string hostPath = Path.Combine(
                    AppContext.BaseDirectory, "XbPreview.Host.dll");
                Assembly host = Assembly.LoadFrom(hostPath);
                Type mainFormType = host.GetType(
                    "XbPreview.Host.MainForm", throwOnError: true)!;
                Type inspectorInterface = host.GetType(
                    "XbPreview.Host.IStartupSessionInspector",
                    throwOnError: true)!;
                object proxyObject = DispatchProxy.Create(
                    inspectorInterface,
                    typeof(StartupInspectionHostProxy));
                proxy = (StartupInspectionHostProxy)proxyObject;

                Type factoryType = typeof(Func<,>).MakeGenericType(
                    typeof(string), inspectorInterface);
                ParameterExpression diagnostic = Expression.Parameter(
                    typeof(string), "diagnosticDirectory");
                Delegate factory = Expression.Lambda(
                    factoryType,
                    Expression.Convert(
                        Expression.Constant(proxyObject), inspectorInterface),
                    diagnostic).Compile();
                ConstructorInfo constructor = mainFormType.GetConstructor(
                    BindingFlags.Instance | BindingFlags.NonPublic,
                    binder: null,
                    [factoryType],
                    modifiers: null) ?? throw new InvalidOperationException(
                        "Injected MainForm constructor was not found.");
                form = (Form)(constructor.Invoke([factory]) ??
                    throw new InvalidOperationException(
                        "Injected MainForm construction failed."));
                FieldInfo coordinatorField = mainFormType.GetField(
                    "_startupInspection",
                    BindingFlags.Instance | BindingFlags.NonPublic) ??
                    throw new InvalidOperationException(
                        "MainForm startup coordinator field was not found.");
                FieldInfo recordingControllerField = mainFormType.GetField(
                    "_recordingController",
                    BindingFlags.Instance | BindingFlags.NonPublic) ??
                    throw new InvalidOperationException(
                        "MainForm recording controller field was not found.");
                FieldInfo automaticStartField = mainFormType.GetField(
                    "_automaticStartAttempted",
                    BindingFlags.Instance | BindingFlags.NonPublic) ??
                    throw new InvalidOperationException(
                        "MainForm automatic-start field was not found.");
                automaticStartField.SetValue(form, true);
                MethodInfo schedule = mainFormType.GetMethod(
                    "TryScheduleStartupInspection",
                    BindingFlags.Instance | BindingFlags.NonPublic) ??
                    throw new InvalidOperationException(
                        "MainForm startup schedule method was not found.");
                FieldInfo diagnosticField = mainFormType.GetField(
                    "_diagnosticLogDirectory",
                    BindingFlags.Instance | BindingFlags.NonPublic) ??
                    throw new InvalidOperationException(
                        "MainForm diagnostic directory field was not found.");

                Exception? scenarioFailure = null;
                bool scenarioFinished = false;
                bool formClosed = false;
                using ApplicationContext context = new(form);

                async void ExecuteScenario()
                {
                    try
                    {
                        await WaitUntilOnUiAsync(
                            () => proxy.Entered.IsSet,
                            TimeSpan.FromSeconds(30),
                            "real MainForm schedules inspection after initialization");
                        await WaitUntilOnUiAsync(
                            () => recordingControllerField.GetValue(form) is not null,
                            TimeSpan.FromSeconds(30),
                            "MainForm finishes non-WGC initialization while scan is blocked");
                        bool uiPing = false;
                        form.BeginInvoke((Action)(() => uiPing = true));
                        await WaitUntilOnUiAsync(
                            () => uiPing,
                            TimeSpan.FromSeconds(5),
                            "MainForm message loop remains responsive during scan");
                        Require(
                            !proxy.Release.IsSet &&
                            proxy.InspectThreadId != staThreadId,
                            "historical scan runs off the STA and never blocks Host startup");

                        object coordinator = coordinatorField.GetValue(form) ??
                            throw new InvalidOperationException(
                                "MainForm did not retain its startup coordinator.");
                        MethodInfo startAsync = coordinator.GetType().GetMethod(
                            "StartAsync",
                            BindingFlags.Instance | BindingFlags.NonPublic) ??
                            throw new InvalidOperationException(
                                "Coordinator StartAsync was not found.");
                        object first = startAsync.Invoke(coordinator, null)!;
                        object second = startAsync.Invoke(coordinator, null)!;
                        Require(ReferenceEquals(first, second),
                            "real MainForm coordinator Start is single-flight");

                        string diagnosticDirectory =
                            (string?)diagnosticField.GetValue(form) ??
                            throw new InvalidOperationException(
                                "MainForm diagnostic directory was unavailable.");
                        schedule.Invoke(form, [diagnosticDirectory]);
                        schedule.Invoke(form, [diagnosticDirectory]);
                        await Task.Delay(100);
                        Require(proxy.CallCount == 1,
                            "repeated MainForm schedule attempts invoke one inspector");

                        form.Close();
                        await WaitUntilOnUiAsync(
                            () => proxy.CancellationObserved.IsSet,
                            TimeSpan.FromSeconds(30),
                            "MainForm close cancels the in-flight inspection");
                        proxy.Release.Set();
                        scenarioFinished = true;
                    }
                    catch (Exception error)
                    {
                        scenarioFailure = error;
                        proxy.Release.Set();
                        context.ExitThread();
                    }
                }

                FormClosedEventHandler closed = (_, _) => formClosed = true;
                EventHandler shown = (_, _) =>
                    form.BeginInvoke((Action)ExecuteScenario);
                form.FormClosed += closed;
                form.Shown += shown;
                try
                {
                    form.Show();
                    Application.Run(context);
                }
                finally
                {
                    form.Shown -= shown;
                    form.FormClosed -= closed;
                    proxy.Release.Set();
                    if (!form.IsDisposed)
                    {
                        form.Dispose();
                    }
                }

                Require(scenarioFinished,
                    "real MainForm startup scenario completes");
                Require(formClosed,
                    "real MainForm closes after inspection cancellation completes");
                Require(proxy.CallCount == 1,
                    "real MainForm creates no second startup scan");
                if (scenarioFailure is not null)
                {
                    throw new InvalidOperationException(
                        "Real MainForm startup orchestration failed.",
                        scenarioFailure);
                }
            }
            catch (Exception error)
            {
                failure = error;
                proxy?.Release.Set();
            }
            finally
            {
                finished.Set();
            }
        })
        {
            IsBackground = true,
            Name = "P2.6C-4A real MainForm STA",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Require(finished.Wait(TimeSpan.FromSeconds(90)),
            "real MainForm startup STA exits within the controlled timeout");
        Require(thread.Join(TimeSpan.FromSeconds(5)),
            "real MainForm startup STA has no residual UI thread");
        if (failure is not null)
        {
            throw new InvalidOperationException(
                "P2.6C-4A real MainForm startup test failed.", failure);
        }
    }

    private static T GetActualMainFormField<T>(
        Type mainFormType,
        object form,
        string name)
        where T : class
    {
        return mainFormType.GetField(
                name,
                BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(form)
            as T ?? throw new InvalidOperationException(
                $"MainForm field {name} was unavailable.");
    }

    private static async Task WaitUntilOnUiAsync(
        Func<bool> predicate,
        TimeSpan timeout,
        string message)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        while (!predicate())
        {
            if (stopwatch.Elapsed >= timeout)
            {
                throw new TimeoutException(message);
            }
            await Task.Delay(50);
        }
    }

    private static StartupInspectionResult EmptyResult() =>
        new(
            HistoricalSessionScanStatus.Success,
            0,
            TimeSpan.Zero,
            0,
            0,
            0,
            NativeMethods.HistoricalSessionScanMaximumEntriesV1,
            false,
            true,
            Array.Empty<HistoricalSessionInspection>());

    private static HistoricalSessionInspection RequireSession(
        StartupInspectionResult result,
        string sessionId)
    {
        HistoricalSessionInspection? session = result.Sessions.SingleOrDefault(
            candidate => string.Equals(
                candidate.SessionId, sessionId,
                StringComparison.OrdinalIgnoreCase));
        return session ?? throw new InvalidOperationException(
            $"Managed startup inspection did not return Session {sessionId}.");
    }

    private static string CreateDiagnosticDirectory(string root, string name)
    {
        string path = Path.Combine(
            root, name, "artifacts", "bin", "Release", "x64",
            "diagnostic-logs");
        Directory.CreateDirectory(path);
        return Path.GetFullPath(path);
    }

    private static string ArtifactsRoot(string diagnosticLogDirectory)
    {
        DirectoryInfo? current = new(diagnosticLogDirectory);
        for (int index = 0; index < 4; index++)
        {
            current = current.Parent ?? throw new InvalidOperationException(
                "Diagnostic path does not satisfy the shared OutputRoot contract.");
        }
        return current.FullName;
    }

    private static string MediaRoot(string diagnosticLogDirectory) =>
        Path.Combine(ArtifactsRoot(diagnosticLogDirectory), "p2.5a-recordings");

    private static string SessionsRoot(string diagnosticLogDirectory) =>
        Path.Combine(MediaRoot(diagnosticLogDirectory), "sessions");

    private static Dictionary<string, ManifestEvidence> CaptureManifestEvidence(
        string mediaRoot)
    {
        return Directory.EnumerateFiles(
                Path.Combine(mediaRoot, "sessions"),
                "manifest.json",
                SearchOption.AllDirectories)
            .ToDictionary(
                Path.GetFullPath,
                path =>
                {
                    byte[] bytes = File.ReadAllBytes(path);
                    ulong? revision = null;
                    try
                    {
                        using JsonDocument document = JsonDocument.Parse(bytes);
                        if (document.RootElement.TryGetProperty(
                                "revision", out JsonElement value) &&
                            value.TryGetUInt64(out ulong parsed))
                        {
                            revision = parsed;
                        }
                    }
                    catch (JsonException)
                    {
                    }
                    return new ManifestEvidence(
                        SHA256.HashData(bytes), revision, bytes.Length);
                },
                StringComparer.OrdinalIgnoreCase);
    }

    private static Dictionary<string, byte[]> CaptureMediaHashes(string mediaRoot) =>
        Directory.EnumerateFiles(mediaRoot, "*.mp4", SearchOption.TopDirectoryOnly)
            .ToDictionary(
                Path.GetFullPath,
                path => SHA256.HashData(File.ReadAllBytes(path)),
                StringComparer.OrdinalIgnoreCase);

    private static bool SameManifestEvidence(
        IReadOnlyDictionary<string, ManifestEvidence> left,
        IReadOnlyDictionary<string, ManifestEvidence> right) =>
        left.Count == right.Count && left.All(pair =>
            right.TryGetValue(pair.Key, out ManifestEvidence? value) &&
            pair.Value.Revision == value.Revision &&
            pair.Value.Length == value.Length &&
            pair.Value.Hash.AsSpan().SequenceEqual(value.Hash));

    private static bool SameHashes(
        IReadOnlyDictionary<string, byte[]> left,
        IReadOnlyDictionary<string, byte[]> right) =>
        left.Count == right.Count && left.All(pair =>
            right.TryGetValue(pair.Key, out byte[]? value) &&
            pair.Value.AsSpan().SequenceEqual(value));

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(
                $"P2.6C-4A startup inspection test failed: {message}");
        }
    }

    private sealed record ManifestEvidence(
        byte[] Hash,
        ulong? Revision,
        int Length);

    private sealed class ControlledInspector : IStartupSessionInspector
    {
        internal ManualResetEventSlim Entered { get; } = new(false);
        internal ManualResetEventSlim Release { get; } = new(false);
        internal ManualResetEventSlim CancellationObserved { get; } = new(false);
        internal int CallCount => Volatile.Read(ref _callCount);
        private int _callCount;

        public StartupInspectionResult Inspect(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _callCount);
            using CancellationTokenRegistration registration =
                cancellationToken.Register(CancellationObserved.Set);
            Entered.Set();
            Release.Wait();
            cancellationToken.ThrowIfCancellationRequested();
            return EmptyResult();
        }
    }

    private sealed class ThrowingInspector : IStartupSessionInspector
    {
        public StartupInspectionResult Inspect(CancellationToken cancellationToken) =>
            throw new IOException("controlled scanner backend failure");
    }
}

public class StartupInspectionHostProxy : DispatchProxy
{
    public ManualResetEventSlim Entered { get; } = new(false);
    public ManualResetEventSlim Release { get; } = new(false);
    public ManualResetEventSlim CancellationObserved { get; } = new(false);
    public int CallCount => Volatile.Read(ref _callCount);
    public int InspectThreadId => Volatile.Read(ref _inspectThreadId);
    private int _callCount;
    private int _inspectThreadId;

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        if (targetMethod?.Name != "Inspect" || args is null || args.Length != 1)
        {
            throw new NotSupportedException(
                $"Unexpected startup inspector method: {targetMethod?.Name}.");
        }

        Interlocked.Increment(ref _callCount);
        Volatile.Write(ref _inspectThreadId, Environment.CurrentManagedThreadId);
        CancellationToken cancellationToken = (CancellationToken)args[0]!;
        using CancellationTokenRegistration registration =
            cancellationToken.Register(CancellationObserved.Set);
        Entered.Set();
        Release.Wait();
        cancellationToken.ThrowIfCancellationRequested();
        return CreateEmptyResult(targetMethod.DeclaringType!.Assembly);
    }

    private static object CreateEmptyResult(Assembly host)
    {
        Type resultType = host.GetType(
            "XbPreview.Host.StartupInspectionResult", throwOnError: true)!;
        Type statusType = host.GetType(
            "XbPreview.Host.HistoricalSessionScanStatus", throwOnError: true)!;
        Type inspectionType = host.GetType(
            "XbPreview.Host.HistoricalSessionInspection", throwOnError: true)!;
        Array sessions = Array.CreateInstance(inspectionType, 0);
        ConstructorInfo constructor = resultType.GetConstructors(
            BindingFlags.Instance | BindingFlags.NonPublic).Single();
        return constructor.Invoke(
        [
            Enum.ToObject(statusType, 0),
            0,
            TimeSpan.Zero,
            0u,
            0u,
            0UL,
            1024UL,
            false,
            true,
            sessions,
        ]);
    }
}
