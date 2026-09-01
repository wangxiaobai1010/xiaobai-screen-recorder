using XbPreview.Host;
using System.Text.Json;

namespace XbPreview.Managed.Tests;

internal static class UserCancelledRecoveryTests
{
    internal static void Run()
    {
        if ((int)HistoricalSessionClassification.UserCancelled != 13 ||
            (int)NativeMethods.HistoricalSessionClassificationV1.
                UserCancelled != 13 ||
            (int)NativeMethods.HistoricalSessionManifestStateV1.
                UserCancelled != 10)
        {
            throw new InvalidOperationException(
                "UserCancelled historical ABI values must remain append-only.");
        }

        VerifyHiddenFromRecoveryPresentation(workingCandidateExists: false);
        VerifyHiddenFromRecoveryPresentation(workingCandidateExists: true);
        VerifyNativeRecoveryBoundary();
    }

    private static void VerifyNativeRecoveryBoundary()
    {
        string root = Path.GetFullPath(Path.Combine(
            Directory.GetCurrentDirectory(),
            "artifacts",
            $"panel4-cancel-recovery-{Environment.ProcessId}-{Guid.NewGuid():N}"));
        string diagnostic = Path.Combine(root, "a", "b", "c", "d");
        string mediaRoot = Path.Combine(root, "p2.5a-recordings");
        string sessionsRoot = Path.Combine(mediaRoot, "sessions");
        const string stoppingId =
            "4C710000-0000-4000-8000-000000000002";
        const string cancelledId =
            "4C710000-0000-4000-8000-000000000003";
        Directory.CreateDirectory(diagnostic);
        try
        {
            string stoppingWorking = CreateSessionFixture(
                sessionsRoot,
                mediaRoot,
                stoppingId,
                state: "Stopping",
                terminalResourcesReleased: false);
            string cancelledWorking = CreateSessionFixture(
                sessionsRoot,
                mediaRoot,
                cancelledId,
                state: "UserCancelled",
                terminalResourcesReleased: true);

            StartupInspectionResult beforeCleanup =
                new NativeHistoricalSessionInspector(diagnostic).
                    Inspect(CancellationToken.None);
            HistoricalSessionInspection stopping = beforeCleanup.Sessions.
                Single(value => value.SessionId == stoppingId);
            HistoricalSessionInspection cancelled = beforeCleanup.Sessions.
                Single(value => value.SessionId == cancelledId);
            UserRecoveryPresentation presentation =
                UserRecoveryPresentation.Create(new StartupInspectionSnapshot(
                    1,
                    StartupInspectionState.Completed,
                    beforeCleanup,
                    null));

            Require(
                File.Exists(stoppingWorking) &&
                stopping.Classification ==
                    HistoricalSessionClassification.IncompleteWithWorkingMedia &&
                presentation.Candidates.Any(value =>
                    value.SessionId == stoppingId),
                "Gate 4 pre-terminal Stopping remains a conservative recovery candidate");
            Require(
                File.Exists(cancelledWorking) &&
                cancelled.Classification ==
                    HistoricalSessionClassification.UserCancelled &&
                presentation.Candidates.All(value =>
                    value.SessionId != cancelledId),
                "Gate 4 durable UserCancelled with residue is not a crash recovery candidate");

            File.Delete(cancelledWorking);
            StartupInspectionResult afterCleanup =
                new NativeHistoricalSessionInspector(diagnostic).
                    Inspect(CancellationToken.None);
            HistoricalSessionInspection cancelledWithoutResidue =
                afterCleanup.Sessions.Single(value =>
                    value.SessionId == cancelledId);
            Require(
                cancelledWithoutResidue.Classification ==
                    HistoricalSessionClassification.UserCancelled &&
                !cancelledWithoutResidue.WorkingCandidateExists &&
                UserRecoveryPresentation.Create(
                    new StartupInspectionSnapshot(
                        2,
                        StartupInspectionState.Completed,
                        afterCleanup,
                        null)).Candidates.All(value =>
                            value.SessionId != cancelledId),
                "Gate 4 durable UserCancelled remains non-recoverable after cleanup");
            Console.WriteLine(
                "PANEL4-CANCEL-GATE-4-RECOVERY-DISTINCTION = PASS");
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static string CreateSessionFixture(
        string sessionsRoot,
        string mediaRoot,
        string sessionId,
        string state,
        bool terminalResourcesReleased)
    {
        string sessionDirectory = Path.Combine(sessionsRoot, sessionId);
        Directory.CreateDirectory(sessionDirectory);
        string workingPath = Path.Combine(
            mediaRoot, $"{sessionId}.partial.mp4");
        string finalPath = Path.Combine(mediaRoot, $"{sessionId}.mp4");
        Directory.CreateDirectory(mediaRoot);
        File.WriteAllBytes(workingPath, [0, 0, 0, 24, 102, 116, 121, 112]);

        Dictionary<string, object?> manifest = new()
        {
            ["schemaVersion"] = 2,
            ["revision"] = terminalResourcesReleased ? 5 : 4,
            ["writerStrategy"] = "mf-sinkwriter-standard-mp4-v1",
            ["sessionId"] = sessionId,
            ["createdAtUtc"] = "2026-08-26T00:00:00.0000000Z",
            ["updatedAtUtc"] = "2026-08-26T00:00:01.0000000Z",
            ["workingPath"] = workingPath,
            ["plannedFinalPath"] = finalPath,
            ["publishedPath"] = string.Empty,
            ["state"] = state,
            ["workingFileOwnedBySession"] = true,
            ["writeSampleAttempted"] = true,
            ["frameSubmitted"] = true,
            ["workerExited"] = terminalResourcesReleased,
            ["recordingResourcesReleased"] = terminalResourcesReleased,
            ["residualOutstanding"] = 0,
            ["finalize"] = new Dictionary<string, object?>
            {
                ["attempted"] = terminalResourcesReleased,
                ["count"] = terminalResourcesReleased ? 1 : 0,
                ["hresult"] = terminalResourcesReleased ? (int?)0 : null,
            },
            ["validation"] = new Dictionary<string, object?>
            {
                ["attempted"] = false,
                ["passed"] = false,
                ["hresult"] = null,
            },
            ["publish"] = new Dictionary<string, object?>
            {
                ["attempted"] = false,
                ["published"] = false,
                ["hresult"] = null,
            },
            ["workingFileIdentity"] = new Dictionary<string, object?>
            {
                ["attempted"] = terminalResourcesReleased,
                ["captured"] = false,
                ["volumeIdentity"] = string.Empty,
                ["fileId"] = string.Empty,
                ["hresult"] = terminalResourcesReleased
                    ? (int?)unchecked((int)0x80070005)
                    : null,
            },
            ["postPublishIdentityVerification"] =
                new Dictionary<string, object?>
                {
                    ["attempted"] = false,
                    ["matched"] = false,
                    ["hresult"] = null,
                },
            ["errorCategory"] = "None",
            ["errorCode"] = null,
            ["errorMessage"] = string.Empty,
        };
        string manifestPath = Path.Combine(sessionDirectory, "manifest.json");
        File.WriteAllText(
            manifestPath,
            JsonSerializer.Serialize(manifest));
        return workingPath;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void VerifyHiddenFromRecoveryPresentation(
        bool workingCandidateExists)
    {
        const string sessionId =
            "4c710000-0000-4000-8000-000000000001";
        HistoricalSessionInspection session = new(
            sessionId,
            7,
            HistoricalSessionClassification.UserCancelled,
            HistoricalSessionSeverity.Info,
            HistoricalSessionReason.None,
            RetainUserMedia: true,
            WorkingCandidateExists: workingCandidateExists,
            FinalCandidateExists: false,
            DisplaySafePath: workingCandidateExists
                ? @"E:\recordings\4c710000-0000-4000-8000-000000000001.partial.mp4"
                : string.Empty,
            HistoricalSessionParseStatus.Valid,
            0,
            HistoricalSessionOwnerState.InactiveLeaseReleased,
            0);
        StartupInspectionResult result = new(
            HistoricalSessionScanStatus.Success,
            0,
            TimeSpan.Zero,
            1,
            0,
            1,
            1024,
            truncated: false,
            mediaWithoutSessionDirectoryBlindSpot: false,
            [session]);
        UserRecoveryPresentation presentation =
            UserRecoveryPresentation.Create(new StartupInspectionSnapshot(
                1,
                StartupInspectionState.Completed,
                result,
                null));

        if (presentation.Visible || presentation.Candidates.Count != 0)
        {
            throw new InvalidOperationException(
                "Durable UserCancelled sessions must not be recovery candidates.");
        }
    }
}
