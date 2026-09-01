using System.Diagnostics;
using System.Runtime.InteropServices;
using XbPreview.Host;

namespace XbPreview.Managed.Tests;

internal static class P26D2BRecoveryActor
{
    private const uint EvidenceMagic = 0x314D3244;
    private const uint EvidenceVersion = 1;
    private const int EvidenceSize = 96;
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
        "FILE_ID",
    ];

    private enum ActorPhase : uint
    {
        Candidate = 1,
        Recover = 2,
        PostRecoveryPresentation = 3,
        AlreadyReconciled = 4,
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8, Size = EvidenceSize)]
    private struct EvidenceV1
    {
        internal uint Magic;
        internal uint StructSize;
        internal uint Version;
        internal uint Phase;
        internal uint ProcessId;
        internal uint ParentProcessId;
        internal int RecoveryStatus;
        internal int DiagnosticHResult;
        internal ulong ExpectedRevision;
        internal ulong ObservedRevision;
        internal uint ObservedRevisionAvailable;
        internal uint GuardStatusAvailable;
        internal int GuardStatus;
        internal uint CasStatusAvailable;
        internal int CasStatus;
        internal int Classification;
        internal int CandidateState;
        internal uint CanTryRecovery;
        internal uint ConfirmedRecovered;
        internal uint ForbiddenUserTermDetected;
        internal uint Reserved0;
    }

    internal static int Run(string[] args)
    {
        Require(args.Length == 9, "D2B managed actor argument count");
        Require(Enum.TryParse(args[1], out ActorPhase phase) &&
            Enum.IsDefined(phase), "D2B managed actor phase");
        string diagnosticDirectory = Path.GetFullPath(args[2]);
        string sessionId = args[3];
        Require(Guid.TryParseExact(sessionId, "D", out Guid parsed) &&
            string.Equals(
                sessionId,
                parsed.ToString("D").ToUpperInvariant(),
                StringComparison.Ordinal),
            "D2B managed actor canonical SessionId");
        Require(ulong.TryParse(args[4], out ulong expectedRevision) &&
            expectedRevision != 0, "D2B managed actor expected revision");
        string evidencePath = Path.GetFullPath(args[5]);
        string readyEventName = args[6];
        string continueEventName = args[7];
        Require(uint.TryParse(args[8], out uint parentProcessId) &&
            parentProcessId != 0, "D2B managed actor parent PID");
        using Process parent = Process.GetProcessById((int)parentProcessId);
        Require(!parent.HasExited, "D2B managed actor parent is alive");
        using EventWaitHandle ready = EventWaitHandle.OpenExisting(
            readyEventName);
        using EventWaitHandle proceed = EventWaitHandle.OpenExisting(
            continueEventName);

        EvidenceV1 evidence = NewEvidence(
            phase, parentProcessId, expectedRevision);
        switch (phase)
        {
            case ActorPhase.Candidate:
                FillCandidate(
                    diagnosticDirectory, sessionId, ref evidence);
                break;
            case ActorPhase.Recover:
                FillRecovery(
                    diagnosticDirectory,
                    sessionId,
                    expectedRevision,
                    requireAlready: false,
                    ref evidence);
                break;
            case ActorPhase.PostRecoveryPresentation:
                FillPostRecoveryPresentation(
                    diagnosticDirectory, sessionId, ref evidence);
                break;
            case ActorPhase.AlreadyReconciled:
                FillRecovery(
                    diagnosticDirectory,
                    sessionId,
                    expectedRevision,
                    requireAlready: true,
                    ref evidence);
                break;
            default:
                throw new InvalidOperationException("Unsupported D2B phase.");
        }

        PublishEvidence(evidencePath, evidence);
        Require(ready.Set(), "signal D2B managed actor evidence ready");
        Require(proceed.WaitOne(TimeSpan.FromSeconds(30)),
            "D2B managed actor continuation timeout");
        return 0;
    }

    private static EvidenceV1 NewEvidence(
        ActorPhase phase,
        uint parentProcessId,
        ulong expectedRevision) => new()
    {
        Magic = EvidenceMagic,
        StructSize = EvidenceSize,
        Version = EvidenceVersion,
        Phase = (uint)phase,
        ProcessId = (uint)Environment.ProcessId,
        ParentProcessId = parentProcessId,
        RecoveryStatus = -1,
        ExpectedRevision = expectedRevision,
        GuardStatus = -1,
        CasStatus = -1,
        Classification = -1,
        CandidateState = -1,
    };

    private static void FillCandidate(
        string diagnosticDirectory,
        string sessionId,
        ref EvidenceV1 evidence)
    {
        (StartupInspectionResult result, HistoricalSessionInspection session) =
            InspectOne(diagnosticDirectory, sessionId);
        UserRecoveryPresentation presentation = UserRecoveryPresentation.Create(
            CompletedSnapshot(result));
        UserRecoveryCandidate candidate = presentation.Candidates.Single(
            value => value.SessionId == sessionId);
        Require(session.Classification ==
                HistoricalSessionClassification.PublishOutcomeUnprovenRetain &&
            session.OwnerState ==
                HistoricalSessionOwnerState.InactiveLeaseReleased &&
            session.ObservedRevision.HasValue &&
            candidate.State == UserRecoveryCandidateState.CanTryRecovery &&
            candidate.CanTryRecovery,
            "formal presentation exposes only an explicit recovery candidate");
        evidence.ObservedRevisionAvailable = 1;
        evidence.ObservedRevision = session.ObservedRevision.GetValueOrDefault();
        evidence.Classification = (int)session.Classification;
        evidence.CandidateState = (int)candidate.State;
        evidence.CanTryRecovery = 1;
        evidence.ConfirmedRecovered = 0;
        evidence.ForbiddenUserTermDetected = ContainsForbiddenUserText(
            presentation, candidate) ? 1u : 0u;
        Require(evidence.ForbiddenUserTermDetected == 0,
            "candidate presentation contains no engineering-only terms");
    }

    private static void FillRecovery(
        string diagnosticDirectory,
        string sessionId,
        ulong expectedRevision,
        bool requireAlready,
        ref EvidenceV1 evidence)
    {
        if (!requireAlready)
        {
            (StartupInspectionResult initial,
                HistoricalSessionInspection session) =
                InspectOne(diagnosticDirectory, sessionId);
            UserRecoveryCandidate candidate = UserRecoveryPresentation.Create(
                CompletedSnapshot(initial)).Candidates.Single(
                    value => value.SessionId == sessionId);
            Require(session.Classification ==
                    HistoricalSessionClassification.
                        PublishOutcomeUnprovenRetain &&
                candidate.CanTryRecovery,
                "recovery actor revalidates the actionable candidate");
        }

        NativeNarrowRecoveryService service = new(diagnosticDirectory);
        NarrowRecoveryResult result = service.Recover(
            sessionId, expectedRevision, CancellationToken.None);
        NarrowRecoveryStatus expectedStatus = requireAlready
            ? NarrowRecoveryStatus.AlreadyReconciled
            : NarrowRecoveryStatus.Reconciled;
        Require(result.Status == expectedStatus,
            "official Managed Narrow bridge returned the expected status");
        if (!requireAlready)
        {
            Require(result.GuardStatus ==
                    NativeMethods.NarrowReconciliationGuardStatusV1.
                        EvidenceComplete &&
                result.CasStatus ==
                    NativeMethods.NarrowReconciliationCasStatusV1.Succeeded,
                "official bridge completed Guard and expected-revision CAS");
        }

        evidence.RecoveryStatus = (int)result.Status;
        evidence.DiagnosticHResult = result.DiagnosticHResult;
        evidence.ExpectedRevision = result.ExpectedRevision;
        if (result.ObservedRevision.HasValue)
        {
            evidence.ObservedRevisionAvailable = 1;
            evidence.ObservedRevision = result.ObservedRevision.Value;
        }
        if (result.GuardStatus.HasValue)
        {
            evidence.GuardStatusAvailable = 1;
            evidence.GuardStatus = (int)result.GuardStatus.Value;
        }
        if (result.CasStatus.HasValue)
        {
            evidence.CasStatusAvailable = 1;
            evidence.CasStatus = (int)result.CasStatus.Value;
        }
    }

    private static void FillPostRecoveryPresentation(
        string diagnosticDirectory,
        string sessionId,
        ref EvidenceV1 evidence)
    {
        (StartupInspectionResult result, HistoricalSessionInspection session) =
            InspectOne(diagnosticDirectory, sessionId);
        Require(session.Classification ==
            HistoricalSessionClassification.ReconciledCompletedConsistent,
            "post-recovery formal scan confirms reconciled Session");
        UserRecoveryPresentation presentation = UserRecoveryPresentation.Create(
            CompletedSnapshot(result), sessionId);
        UserRecoveryCandidate candidate = presentation.Candidates.Single(
            value => value.SessionId == sessionId);
        Require(candidate.State == UserRecoveryCandidateState.Recovered &&
            !candidate.CanTryRecovery,
            "recovered user fact is emitted only after confirmed rescan");
        evidence.ObservedRevisionAvailable =
            session.ObservedRevision.HasValue ? 1u : 0u;
        evidence.ObservedRevision = session.ObservedRevision.GetValueOrDefault();
        evidence.Classification = (int)session.Classification;
        evidence.CandidateState = (int)candidate.State;
        evidence.CanTryRecovery = 0;
        evidence.ConfirmedRecovered = 1;
        evidence.ForbiddenUserTermDetected = ContainsForbiddenUserText(
            presentation, candidate) ? 1u : 0u;
        Require(evidence.ForbiddenUserTermDetected == 0,
            "recovered presentation contains no engineering-only terms");
    }

    private static (
        StartupInspectionResult Result,
        HistoricalSessionInspection Session) InspectOne(
            string diagnosticDirectory,
            string sessionId)
    {
        StartupInspectionResult result =
            new NativeHistoricalSessionInspector(diagnosticDirectory).
                Inspect(CancellationToken.None);
        Require(result.Status == HistoricalSessionScanStatus.Success,
            "D2B formal historical scan succeeds");
        HistoricalSessionInspection session = result.Sessions.Single(
            value => value.SessionId == sessionId);
        return (result, session);
    }

    private static StartupInspectionSnapshot CompletedSnapshot(
        StartupInspectionResult result) => new(
            1,
            StartupInspectionState.Completed,
            result,
            null);

    private static bool ContainsForbiddenUserText(
        UserRecoveryPresentation presentation,
        UserRecoveryCandidate candidate)
    {
        string[] values =
        [
            presentation.NoticeText,
            candidate.Title,
            candidate.StatusText,
        ];
        return values.Any(value => ForbiddenUserTerms.Any(term =>
            value.Contains(term, StringComparison.OrdinalIgnoreCase)));
    }

    private static void PublishEvidence(string path, EvidenceV1 evidence)
    {
        Require(Marshal.SizeOf<EvidenceV1>() == EvidenceSize,
            "D2B managed evidence layout");
        byte[] bytes = new byte[EvidenceSize];
        MemoryMarshal.Write(bytes.AsSpan(), in evidence);
        using FileStream stream = new(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            4096,
            FileOptions.WriteThrough);
        stream.Write(bytes);
        stream.Flush(flushToDisk: true);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
