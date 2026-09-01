using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Windows.Forms;
using XbPreview.Avalonia.Contracts;
using XbPreview.Avalonia.Views;
using XbPreview.Host;

namespace XbPreview.Managed.Tests;

internal static class RecoveryCandidateTests
{
    private const string CandidateId =
        "27000000-0000-4000-8000-000000004B10";
    private const string PreservedId =
        "27000000-0000-4000-8000-000000004B11";
    private const string CorruptId =
        "27000000-0000-4000-8000-000000004B12";
    private const string UnsupportedId =
        "27000000-0000-4000-8000-000000004B13";
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

    internal static async Task RunAsync()
    {
        int completedCases = 0;
        void Pass() => completedCases++;

        UserRecoveryPresentation empty = UserRecoveryPresentation.Create(
            Snapshot());
        Require(!empty.Visible && empty.Candidates.Count == 0,
            "1 zero attention Sessions hide the notice");
        Pass();

        UserRecoveryCandidate candidate = Candidate();
        UserRecoveryPresentation one = UserRecoveryPresentation.Create(
            Snapshot(Session(
                CandidateId,
                HistoricalSessionClassification.PublishOutcomeUnprovenRetain,
                revision: 1,
                finalExists: true,
                path: @"E:\测试\候选.mp4")));
        Require(
            one.Visible && one.AttentionCount == 1 &&
            one.NoticeText == "发现 1 段未正常结束的录制",
            "2 one attention candidate has singular user copy");
        Pass();

        UserRecoveryPresentation multiple = UserRecoveryPresentation.Create(
            Snapshot(
                Session(
                    CandidateId,
                    HistoricalSessionClassification.PublishOutcomeUnprovenRetain,
                    revision: 1,
                    finalExists: true),
                Session(
                    PreservedId,
                    HistoricalSessionClassification.IncompleteWithWorkingMedia,
                    workingExists: true)));
        Require(
            multiple.AttentionCount == 2 &&
            multiple.NoticeText == "发现 2 段需要处理的历史录制",
            "3 multiple attention candidates report the exact count");
        Pass();

        UserRecoveryPresentation reconciledHidden =
            UserRecoveryPresentation.Create(
                Snapshot(Session(
                    CandidateId,
                    HistoricalSessionClassification.
                        ReconciledCompletedConsistent,
                    revision: 2,
                    finalExists: true)));
        Require(!reconciledHidden.Visible,
            "4 reconciled history is not a pending recovery candidate");
        Pass();

        UserRecoveryCandidate preserved = multiple.Candidates.Single(value =>
            value.SessionId == PreservedId);
        Require(
            preserved.State == UserRecoveryCandidateState.RecordingPreserved &&
            preserved.StatusText.Contains("文件已为你保留", StringComparison.Ordinal) &&
            !preserved.CanTryRecovery,
            "5 working media is described as preserved, never recovered");
        Pass();

        UserRecoveryPresentation retained = UserRecoveryPresentation.Create(
            Snapshot(Session(
                CorruptId,
                HistoricalSessionClassification.ManifestCorrupt)));
        Require(
            retained.Candidates.Single().State ==
                UserRecoveryCandidateState.NeedsAttentionRetained &&
            retained.Candidates.Single().StatusText.Contains(
                "文件不会被删除", StringComparison.Ordinal),
            "6 unknown or unreadable facts use calm retain copy");
        Pass();

        Require(
            AllUserText(one, multiple, retained).All(text =>
                ForbiddenUserTerms.All(term =>
                    !text.Contains(term, StringComparison.OrdinalIgnoreCase))),
            "7 user-visible copy contains no engineering state names");
        Pass();

        Require(
            one.Candidates.Single().CanTryRecovery &&
            one.Candidates.Single().State ==
                UserRecoveryCandidateState.CanTryRecovery,
            "8 the one supported Native classification exposes Try Recovery");
        Pass();

        Require(
            !retained.Candidates.Single().CanTryRecovery &&
            !preserved.CanTryRecovery,
            "9 unsupported Sessions expose no recovery action");
        Pass();

        UserRecoveryPresentation active = UserRecoveryPresentation.Create(
            Snapshot(Session(
                CandidateId,
                HistoricalSessionClassification.PublishOutcomeUnprovenRetain,
                revision: 1,
                owner: HistoricalSessionOwnerState.ActiveOwned)));
        Require(!active.Candidates.Single().CanTryRecovery,
            "10 live owner is observation-only and cannot be preempted");
        Pass();

        UserRecoveryPresentation noSafePath = UserRecoveryPresentation.Create(
            Snapshot(Session(
                CorruptId,
                HistoricalSessionClassification.FilesystemConflict,
                path: string.Empty)));
        Require(noSafePath.Candidates.Single().DisplaySafePath.Length == 0,
            "11 absent display-safe path stays absent in presentation");
        Pass();

        UserRecoveryPresentation recovered = UserRecoveryPresentation.Create(
            Snapshot(Session(
                CandidateId,
                HistoricalSessionClassification.ReconciledCompletedConsistent,
                revision: 2,
                finalExists: true)),
            CandidateId);
        Require(
            recovered.Visible && recovered.AttentionCount == 0 &&
            recovered.Candidates.Single().State ==
                UserRecoveryCandidateState.Recovered &&
            recovered.NoticeText == "录像已找回并确认保存。",
            "12 success presentation requires a confirmed rescan highlight");
        Pass();

        VerifyManagedAbi();
        Pass();

        string root = Path.Combine(
            Directory.GetCurrentDirectory(),
            "artifacts",
            $"p2.6c4b-managed-recovery-中文-{Environment.ProcessId}");
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
        try
        {
            string diagnostic = CreateDiagnosticDirectory(root);
            RunNativeFixtureHelper(diagnostic);
            string mediaRoot = MediaRoot(diagnostic);
            StartupInspectionResult initial =
                new NativeHistoricalSessionInspector(diagnostic).
                    Inspect(CancellationToken.None);
            Require(initial.SessionCount == 4 && initial.Sessions.Count == 4,
                "14 actual Native fixture crosses the formal scan bridge");
            Pass();

            HistoricalSessionInspection actualCandidate = initial.Sessions.Single(
                session => session.SessionId == CandidateId);
            Require(
                actualCandidate.Classification ==
                    HistoricalSessionClassification.PublishOutcomeUnprovenRetain &&
                actualCandidate.ObservedRevision.HasValue &&
                UserRecoveryPresentation.Create(
                    CompletedSnapshot(initial)).Candidates.Single(value =>
                        value.SessionId == CandidateId).CanTryRecovery,
                "15 only the formal Native candidate becomes user-actionable");
            Pass();

            Dictionary<string, byte[]> mediaBefore = CaptureMedia(mediaRoot);
            string unsupportedManifest = Path.Combine(
                mediaRoot, "sessions", UnsupportedId, "manifest.json");
            byte[] unsupportedBefore = File.ReadAllBytes(unsupportedManifest);
            NativeNarrowRecoveryService service = new(diagnostic);
            NarrowRecoveryResult native = service.Recover(
                CandidateId,
                actualCandidate.ObservedRevision!.Value,
                CancellationToken.None);
            Require(native.Status == NarrowRecoveryStatus.Reconciled,
                "16 explicit Managed request calls the one Native Session");
            Pass();

            Require(SameMedia(mediaBefore, CaptureMedia(mediaRoot)),
                "17 recovery changes no media bytes");
            Pass();

            Require(File.ReadAllBytes(unsupportedManifest).SequenceEqual(
                unsupportedBefore),
                "18 unsupported Session Manifest bytes remain unchanged");
            Pass();

            StartupInspectionResult rescan =
                new NativeHistoricalSessionInspector(diagnostic).
                    Inspect(CancellationToken.None);
            Require(
                rescan.Sessions.Single(session =>
                    session.SessionId == CandidateId).Classification ==
                        HistoricalSessionClassification.
                            ReconciledCompletedConsistent,
                "19 user success fact comes only from a read-only rescan");
            Pass();

            NarrowRecoveryResult already = service.Recover(
                CandidateId,
                actualCandidate.ObservedRevision.Value,
                CancellationToken.None);
            Require(already.Status == NarrowRecoveryStatus.AlreadyReconciled,
                "20 repeated explicit request returns AlreadyReconciled");
            Pass();
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
            Require(!Directory.Exists(root),
                "managed recovery fixtures release all handles");
        }

        await VerifySpecifiedSessionAndSingleFlightAsync();
        Pass();
        await VerifyReconciledRescanAsync(NarrowRecoveryStatus.Reconciled);
        Pass();
        await VerifyReconciledRescanAsync(
            NarrowRecoveryStatus.AlreadyReconciled);
        Pass();
        await VerifySafeFailureAsync(
            NarrowRecoveryStatus.GuardRejected,
            "当前情况发生了变化");
        Pass();
        await VerifySafeFailureAsync(
            NarrowRecoveryStatus.RevisionChanged,
            "当前情况发生了变化");
        Pass();
        await VerifySafeFailureAsync(
            NarrowRecoveryStatus.IoFailure,
            "请稍后再试");
        Pass();
        await VerifyGlobalSingleFlightAsync();
        Pass();
        await VerifyCloseCancellationAsync();
        Pass();

        Require(
            typeof(UserRecoveryPresentation).GetMethods(
                BindingFlags.Static | BindingFlags.NonPublic).All(method =>
                    !method.Name.Contains("Manifest", StringComparison.Ordinal) &&
                    !method.Name.Contains("Delete", StringComparison.Ordinal) &&
                    !method.Name.Contains("Move", StringComparison.Ordinal)) &&
            typeof(RecoveryActionCoordinator).GetFields(
                BindingFlags.Instance | BindingFlags.NonPublic).All(field =>
                    !field.FieldType.Name.Contains(
                        "Manifest", StringComparison.Ordinal)),
            "29 Managed presentation and orchestration contain no Manifest writer");
        Pass();

        Require(candidate.SessionId == CandidateId &&
            candidate.ObservedRevision == 1 && candidate.CanTryRecovery,
            "30 presentation carries identity/revision but does not display them");
        Pass();

        Require(
            UserRecoveryPresentation.Create(
                new StartupInspectionSnapshot(
                    1,
                    StartupInspectionState.Running,
                    null,
                    null)).Visible == false,
            "31 notice appears only after a terminal scan result arrives");
        Pass();

        Require(
            typeof(StartupInspectionSnapshot) !=
                typeof(RecoveryAttemptSnapshot) &&
            typeof(IStartupSessionInspector) != typeof(IUserRecoveryService),
            "32 scan observation and mutation request remain separate contracts");
        Pass();

        VerifyActualHostNoticeWiring();
        Pass();

        Require(completedCases == 33,
            "complete P2.6C-4B Managed recovery-candidate matrix");
        Console.WriteLine(
            $"P2.6C-4B_MANAGED_USER_RECOVERY_MATRIX={completedCases}");
    }

    internal static void ShowFixture()
    {
        Exception? failure = null;
        Thread thread = new(() =>
        {
            try
            {
                Application.EnableVisualStyles();
                string hostPath = Path.Combine(
                    AppContext.BaseDirectory,
                    "XbPreview.Host.dll");
                Assembly hostAssembly = Assembly.LoadFrom(hostPath);
                Type formalHostType = hostAssembly.GetType(
                    "XbPreview.Host.FormalAvaloniaHomeHost",
                    throwOnError: true)!;
                formalHostType.GetMethod(
                    "SetupAvalonia",
                    BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(
                        null,
                        null);

                string isolatedRoot = Path.Combine(
                    Path.GetTempPath(),
                    "xiaobailu-recovery-ui-human-review");
                Directory.CreateDirectory(isolatedRoot);
                StructuralShellView shell = new(new FixtureFrameSource());
                shell.ApplyRecoveryPresentation(
                    new StructuralRecoveryBannerPresentation(
                        "发现一段未正常结束的录制",
                        new[]
                        {
                            new StructuralRecoveryCandidatePresentation(
                                CandidateId,
                                "未正常结束的录制",
                                "发现一段未正常结束的录制，可以尝试找回。",
                                Path.Combine(isolatedRoot, "候选.mp4"),
                                ShowTryRecovery: true,
                                RecoveryRunning: false,
                                CanOpenFolder: true),
                        }));

                Type avaloniaHostType = Type.GetType(
                    "Avalonia.Win32.Interoperability." +
                    "WinFormsAvaloniaControlHost, " +
                    "Avalonia.Win32.Interoperability",
                    throwOnError: true)!;
                Control avaloniaHost = (Control)Activator.CreateInstance(
                    avaloniaHostType)!;
                avaloniaHostType.GetProperty("Content")!.SetValue(
                    avaloniaHost,
                    shell);
                avaloniaHost.Dock = DockStyle.Fill;
                using Form form = new()
                {
                    Text = "小白录 Recovery UI 隔离审查",
                    Width = 910,
                    Height = 635,
                    MinimumSize = new Size(860, 600),
                    StartPosition = FormStartPosition.CenterScreen,
                };
                form.Controls.Add(avaloniaHost);
                Application.Run(form);
            }
            catch (Exception error)
            {
                failure = error;
            }
        })
        {
            Name = "P2.6C-4B fixture STA",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
        {
            throw new InvalidOperationException(
                "Recovery fixture failed.", failure);
        }
    }

    private sealed class FixtureFrameSource : IGpuPreviewFrameSource
    {
        public bool SetPresentationSize(uint pixelWidth, uint pixelHeight) =>
            true;

        public bool TryGetLatestFrame(out GpuPreviewFrame frame)
        {
            frame = default;
            return false;
        }

        public bool IsCurrentStream(ulong streamGeneration) => false;
    }

    private static void VerifyActualHostNoticeWiring()
    {
        Exception? failure = null;
        Thread thread = new(() =>
        {
            Form? form = null;
            try
            {
                Require(Thread.CurrentThread.GetApartmentState() ==
                    ApartmentState.STA,
                    "actual recovery notice test owns one STA");
                string hostPath = Path.Combine(
                    AppContext.BaseDirectory, "XbPreview.Host.dll");
                Assembly host = Assembly.LoadFrom(hostPath);
                Type mainFormType = host.GetType(
                    "XbPreview.Host.MainForm", throwOnError: true)!;
                form = (Form)(Activator.CreateInstance(
                    mainFormType, nonPublic: true) ??
                    throw new InvalidOperationException(
                        "Could not construct actual MainForm."));
                string diagnostic = Path.Combine(
                    Path.GetTempPath(),
                    $"p2.6c4b-ui-wiring-{Environment.ProcessId}");
                mainFormType.GetMethod(
                    "TryCreateRecoveryActions",
                    BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(
                        form, [diagnostic]);

                Type inspectionType = host.GetType(
                    "XbPreview.Host.HistoricalSessionInspection",
                    throwOnError: true)!;
                Type classificationType = host.GetType(
                    "XbPreview.Host.HistoricalSessionClassification",
                    throwOnError: true)!;
                Type severityType = host.GetType(
                    "XbPreview.Host.HistoricalSessionSeverity",
                    throwOnError: true)!;
                Type reasonType = host.GetType(
                    "XbPreview.Host.HistoricalSessionReason",
                    throwOnError: true)!;
                Type parseType = host.GetType(
                    "XbPreview.Host.HistoricalSessionParseStatus",
                    throwOnError: true)!;
                Type ownerType = host.GetType(
                    "XbPreview.Host.HistoricalSessionOwnerState",
                    throwOnError: true)!;
                ConstructorInfo inspectionConstructor =
                    inspectionType.GetConstructors(
                        BindingFlags.Instance |
                        BindingFlags.Public |
                        BindingFlags.NonPublic).Single(constructor =>
                            constructor.GetParameters().Length == 13);
                object inspection = inspectionConstructor.Invoke(
                [
                    CandidateId,
                    1UL,
                    Enum.Parse(
                        classificationType,
                        "PublishOutcomeUnprovenRetain"),
                    Enum.Parse(severityType, "RecoveryCandidate"),
                    Enum.ToObject(reasonType, 0UL),
                    true,
                    false,
                    true,
                    @"E:\测试\候选.mp4",
                    Enum.Parse(parseType, "Valid"),
                    0,
                    Enum.Parse(ownerType, "InactiveLeaseReleased"),
                    0,
                ]);
                Array sessions = Array.CreateInstance(inspectionType, 1);
                sessions.SetValue(inspection, 0);

                Type resultType = host.GetType(
                    "XbPreview.Host.StartupInspectionResult",
                    throwOnError: true)!;
                Type scanStatusType = host.GetType(
                    "XbPreview.Host.HistoricalSessionScanStatus",
                    throwOnError: true)!;
                ConstructorInfo resultConstructor =
                    resultType.GetConstructors(
                        BindingFlags.Instance | BindingFlags.NonPublic).Single();
                object result = resultConstructor.Invoke(
                [
                    Enum.Parse(scanStatusType, "Success"),
                    0,
                    TimeSpan.Zero,
                    1u,
                    0u,
                    1UL,
                    1024UL,
                    false,
                    false,
                    sessions,
                ]);

                Type snapshotType = host.GetType(
                    "XbPreview.Host.StartupInspectionSnapshot",
                    throwOnError: true)!;
                Type snapshotStateType = host.GetType(
                    "XbPreview.Host.StartupInspectionState",
                    throwOnError: true)!;
                object snapshot = snapshotType.GetConstructors(
                    BindingFlags.Instance |
                    BindingFlags.Public |
                    BindingFlags.NonPublic).Single(constructor =>
                        constructor.GetParameters().Length == 4).Invoke(
                [
                    1L,
                    Enum.Parse(snapshotStateType, "Completed"),
                    result,
                    null,
                ]);
                mainFormType.GetMethod(
                    "RecordStartupInspection",
                    BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(
                        form, [snapshot]);

                Label notice = (Label)mainFormType.GetField(
                    "_recoveryNoticeLabel",
                    BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(
                        form)!;
                FlowLayoutPanel list = (FlowLayoutPanel)mainFormType.GetField(
                    "_recoveryListPanel",
                    BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(
                        form)!;
                string[] texts = Descendants(list)
                    .Select(control => control.Text)
                    .Append(notice.Text)
                    .Where(text => !string.IsNullOrEmpty(text))
                    .ToArray();
                Require(
                    notice.Text == "发现 1 段未正常结束的录制" &&
                    Descendants(list).OfType<Button>().Single().Text ==
                        "尝试恢复" &&
                    texts.All(text => ForbiddenUserTerms.All(term =>
                        !text.Contains(
                            term, StringComparison.OrdinalIgnoreCase))),
                    "33 actual Host MainForm wires calm recovery copy and one action");

                Task cleanup = (Task)mainFormType.GetMethod(
                    "CleanupAsync",
                    BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(
                        form, null)!;
                cleanup.GetAwaiter().GetResult();
            }
            catch (Exception error)
            {
                failure = error;
            }
            finally
            {
                form?.Dispose();
            }
        })
        {
            IsBackground = true,
            Name = "P2.6C-4B actual MainForm notice STA",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Require(thread.Join(TimeSpan.FromSeconds(30)),
            "actual MainForm recovery notice STA exits");
        if (failure is not null)
        {
            throw new InvalidOperationException(
                "Actual MainForm recovery notice wiring failed.", failure);
        }
    }

    private static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (Control nested in Descendants(child))
            {
                yield return nested;
            }
        }
    }

    private static async Task VerifySpecifiedSessionAndSingleFlightAsync()
    {
        BlockingRecoveryService service = new(Result(
            NarrowRecoveryStatus.GuardRejected));
        FixedInspector inspector = new(Result());
        await using RecoveryActionCoordinator coordinator = new(
            service, inspector);
        UserRecoveryCandidate candidate = Candidate();
        Task<RecoveryAttemptSnapshot> first = coordinator.StartAsync(candidate);
        Require(service.Entered.Wait(5_000),
            "explicit recovery enters the fake Native boundary");
        Task<RecoveryAttemptSnapshot> second = coordinator.StartAsync(candidate);
        Require(ReferenceEquals(first, second),
            "rapid double-click shares one recovery Task");
        service.Release.Set();
        _ = await first;
        Require(service.CallCount == 1 &&
            service.SessionIds.Single() == CandidateId,
            "only the selected Session is requested once");
    }

    private static async Task VerifyReconciledRescanAsync(
        NarrowRecoveryStatus status)
    {
        ImmediateRecoveryService service = new(Result(status));
        FixedInspector inspector = new(Result(Session(
            CandidateId,
            HistoricalSessionClassification.ReconciledCompletedConsistent,
            revision: 2,
            finalExists: true)));
        await using RecoveryActionCoordinator coordinator = new(
            service, inspector);
        RecoveryAttemptSnapshot result = await coordinator.StartAsync(
            Candidate());
        Require(
            result.ConfirmedRecovered && result.RescanResult is not null &&
            inspector.CallCount == 1 &&
            result.UserMessage == "录像已找回并确认保存。",
            "success and AlreadyReconciled require confirming rescan");
    }

    private static async Task VerifySafeFailureAsync(
        NarrowRecoveryStatus status,
        string expectedText)
    {
        ImmediateRecoveryService service = new(Result(status));
        FixedInspector inspector = new(Result());
        await using RecoveryActionCoordinator coordinator = new(
            service, inspector);
        RecoveryAttemptSnapshot result = await coordinator.StartAsync(
            Candidate());
        Require(
            !result.ConfirmedRecovered && inspector.CallCount == 0 &&
            service.CallCount == 1 &&
            result.UserMessage.Contains(expectedText, StringComparison.Ordinal),
            "failure is retained without automatic retry or false success");
    }

    private static async Task VerifyGlobalSingleFlightAsync()
    {
        BlockingRecoveryService service = new(Result(
            NarrowRecoveryStatus.GuardRejected));
        await using RecoveryActionCoordinator coordinator = new(
            service, new FixedInspector(Result()));
        Task<RecoveryAttemptSnapshot> first = coordinator.StartAsync(Candidate());
        Require(service.Entered.Wait(5_000), "global single-flight entered");
        UserRecoveryCandidate secondCandidate = Candidate() with
        {
            SessionId = PreservedId,
        };
        Task<RecoveryAttemptSnapshot> second = coordinator.StartAsync(
            secondCandidate);
        Require(ReferenceEquals(first, second),
            "first version permits one recovery operation globally");
        service.Release.Set();
        _ = await first;
    }

    private static async Task VerifyCloseCancellationAsync()
    {
        BlockingRecoveryService service = new(Result(
            NarrowRecoveryStatus.GuardRejected));
        RecoveryActionCoordinator coordinator = new(
            service, new FixedInspector(Result()));
        int terminalCallbacks = 0;
        coordinator.SnapshotChanged += (_, snapshot) =>
        {
            if (snapshot.IsTerminal)
            {
                Interlocked.Increment(ref terminalCallbacks);
            }
        };
        _ = coordinator.StartAsync(Candidate());
        Require(service.Entered.Wait(5_000),
            "close test recovery entered");
        await coordinator.DisposeAsync();
        Require(service.CancellationObserved.IsSet && terminalCallbacks == 0,
            "close cancels/waits without a late UI callback");
    }

    private static unsafe void VerifyManagedAbi()
    {
        NativeMethods.ValidateManagedLayout();
        NativeMethods.NarrowReconciliationAbiLayoutV1 layout = new()
        {
            StructSize = (uint)sizeof(
                NativeMethods.NarrowReconciliationAbiLayoutV1),
            AbiVersion = NativeMethods.NarrowReconciliationAbiVersionV1,
        };
        Require(
            NativeMethods.XbPreview_GetApiVersion() == 0x0004_0003 &&
            sizeof(NativeMethods.NarrowReconciliationAbiLayoutV1) == 32 &&
            sizeof(NativeMethods.NarrowReconciliationOptionsV1) == 48 &&
            sizeof(NativeMethods.NarrowReconciliationResultV1) == 64 &&
            Marshal.OffsetOf<NativeMethods.NarrowReconciliationResultV1>(
                nameof(NativeMethods.NarrowReconciliationResultV1.
                    ObservedRevision)).ToInt32() == 24 &&
            NativeMethods.XbPreview_GetNarrowReconciliationAbiLayoutV1(
                ref layout) == NativeMethods.Result.Ok &&
            layout.OptionsSize == 48 && layout.ResultSize == 64,
            "13 Native/Managed narrow recovery ABI is exact Pack=8");
    }

    private static UserRecoveryCandidate Candidate() =>
        UserRecoveryPresentation.Create(Snapshot(Session(
            CandidateId,
            HistoricalSessionClassification.PublishOutcomeUnprovenRetain,
            revision: 1,
            finalExists: true))).Candidates.Single();

    private static HistoricalSessionInspection Session(
        string sessionId,
        HistoricalSessionClassification classification,
        ulong? revision = null,
        bool workingExists = false,
        bool finalExists = false,
        string path = "",
        HistoricalSessionOwnerState owner =
            HistoricalSessionOwnerState.InactiveLeaseReleased) =>
        new(
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
            owner,
            0);

    private static StartupInspectionSnapshot Snapshot(
        params HistoricalSessionInspection[] sessions) =>
        CompletedSnapshot(Result(sessions));

    private static StartupInspectionSnapshot CompletedSnapshot(
        StartupInspectionResult result) => new(
            1,
            StartupInspectionState.Completed,
            result,
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

    private static NarrowRecoveryResult Result(
        NarrowRecoveryStatus status) => new(
            status,
            status is NarrowRecoveryStatus.Reconciled or
                NarrowRecoveryStatus.AlreadyReconciled ? 0 : unchecked((int)0x80004005),
            1,
            1,
            null,
            null);

    private static IEnumerable<string> AllUserText(
        params UserRecoveryPresentation[] presentations) =>
        presentations.SelectMany(presentation =>
            new[] { presentation.NoticeText }.Concat(
                presentation.Candidates.SelectMany(candidate =>
                    new[] { candidate.Title, candidate.StatusText })));

    private static string CreateDiagnosticDirectory(string root)
    {
        string diagnostic = Path.Combine(
            root, "artifacts", "bin", "Release", "x64", "diagnostic-logs");
        Directory.CreateDirectory(diagnostic);
        return diagnostic;
    }

    private static string MediaRoot(string diagnostic)
    {
        DirectoryInfo? current = new(diagnostic);
        for (int index = 0; index < 4; index++)
        {
            current = current.Parent ?? throw new InvalidOperationException(
                "Diagnostic fixture does not have four parent levels.");
        }
        return Path.Combine(current.FullName, "p2.5a-recordings");
    }

    private static void RunNativeFixtureHelper(string diagnostic)
    {
        string executable = Path.Combine(
            AppContext.BaseDirectory, "XbPreview.Native.Tests.exe");
        ProcessStartInfo start = new()
        {
            FileName = executable,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        start.ArgumentList.Add("--p2.6c4b-fixture-helper");
        start.ArgumentList.Add(diagnostic);
        using Process process = Process.Start(start) ??
            throw new InvalidOperationException(
                "Failed to start Native recovery fixture helper.");
        Require(process.WaitForExit(30_000),
            "Native recovery fixture helper exits without force kill");
        Require(process.ExitCode == 0,
            $"Native recovery fixture helper exit={process.ExitCode}");
    }

    private static Dictionary<string, byte[]> CaptureMedia(string mediaRoot) =>
        Directory.EnumerateFiles(mediaRoot, "*.mp4", SearchOption.TopDirectoryOnly)
            .ToDictionary(
                path => Path.GetFileName(path) ??
                    throw new InvalidOperationException(
                        "Media fixture path has no file name."),
                path => SHA256.HashData(File.ReadAllBytes(path)),
                StringComparer.OrdinalIgnoreCase);

    private static bool SameMedia(
        IReadOnlyDictionary<string, byte[]> left,
        IReadOnlyDictionary<string, byte[]> right) =>
        left.Count == right.Count && left.All(pair =>
            right.TryGetValue(pair.Key, out byte[]? value) &&
            pair.Value.SequenceEqual(value));

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed class FixedInspector : IStartupSessionInspector
    {
        private readonly StartupInspectionResult _result;

        internal FixedInspector(StartupInspectionResult result)
        {
            _result = result;
        }

        internal int CallCount { get; private set; }

        public StartupInspectionResult Inspect(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            return _result;
        }
    }

    private sealed class ImmediateRecoveryService : IUserRecoveryService
    {
        private readonly NarrowRecoveryResult _result;

        internal ImmediateRecoveryService(NarrowRecoveryResult result)
        {
            _result = result;
        }

        internal int CallCount { get; private set; }

        public NarrowRecoveryResult Recover(
            string canonicalSessionId,
            ulong expectedRevision,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            return _result;
        }
    }

    private sealed class BlockingRecoveryService : IUserRecoveryService
    {
        private readonly NarrowRecoveryResult _result;
        private int _callCount;

        internal BlockingRecoveryService(NarrowRecoveryResult result)
        {
            _result = result;
        }

        internal ManualResetEventSlim Entered { get; } = new(false);
        internal ManualResetEventSlim Release { get; } = new(false);
        internal ManualResetEventSlim CancellationObserved { get; } = new(false);
        internal int CallCount => Volatile.Read(ref _callCount);
        internal List<string> SessionIds { get; } = [];

        public NarrowRecoveryResult Recover(
            string canonicalSessionId,
            ulong expectedRevision,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _callCount);
            lock (SessionIds)
            {
                SessionIds.Add(canonicalSessionId);
            }
            using CancellationTokenRegistration registration =
                cancellationToken.Register(CancellationObserved.Set);
            Entered.Set();
            Release.Wait(cancellationToken);
            return _result;
        }
    }
}
