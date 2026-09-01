namespace XbPreview.Managed.Tests;

internal static class UserCancelledOwnershipContractTests
{
    internal static void Run()
    {
        string sourcePath = Path.Combine(
            Environment.CurrentDirectory,
            "XbPreview.Native",
            "VideoEncoderConsumer.cpp");
        Require(File.Exists(sourcePath),
            "VideoEncoderConsumer.cpp must exist in the current workspace.");
        string source = File.ReadAllText(sourcePath);

        string operationTimeDelete = Slice(
            source,
            "HRESULT DeleteSessionFileWithOperationTimeEvidence(",
            "class ConsumerRegistrationGuard final");
        RequireAll(operationTimeDelete,
            "DELETE | FILE_READ_ATTRIBUTES",
            "FILE_FLAG_OPEN_REPARSE_POINT",
            "InspectPathForReadOnly(",
            "PathSafetyExpectedType::RegularFile",
            "SamePersistentFileIdentity(",
            "heldCandidateIdentity.hardLinkCount != 1",
            "operationIdentity.hardLinkCount != 1",
            "ReadCancellationDeleteFinalPath(",
            "safety.trustedRootFinalPath",
            "safety.candidateFinalPath",
            "SetFileInformationByHandle(",
            "FileDispositionInfo");
        Require(
            !operationTimeDelete.Contains(
                "DeleteFileW", StringComparison.Ordinal) &&
            !operationTimeDelete.Contains(
                "remove_all", StringComparison.OrdinalIgnoreCase) &&
            !operationTimeDelete.Contains(
                "RemoveDirectory", StringComparison.Ordinal),
            "Gate 5 deletion must target the held, revalidated file handle, " +
                "never perform a later pathname or recursive mutation.");

        string cleanup = Slice(
            source,
            "OutputCleanupOutcome CleanupUserCancelledMaterials(",
            "bool GStreamerAudioCleanupAllowed() noexcept");
        RequireAll(cleanup,
            "SessionManifestState::UserCancelled",
            "manifest.sessionId != configuration.sessionId",
            "manifest.workingFileOwnedBySession",
            "manifest.workingFileIdentity.attempted",
            "worker.outputOwnedBySession",
            "workingPath, configuredWorkingPath",
            "SameExactWindowsPath(workingPath, plannedFinalPath)",
            "DeleteSessionFileWithOperationTimeEvidence(",
            "&persistedIdentity.identity",
            "lifetimeOwner.Acquired()",
            "lifetimeOwner.OwnerPath(), expectedOwnerPath",
            "ResolveRecordingOutputRootsFromManagedRoot(",
            "roots.mediaOutputRoot",
            "roots.sessionsRoot",
            "if (configuration.audioEnabled)",
            "sessionDirectory / L\"system.flac\"",
            "sessionDirectory / L\"mic.flac\"",
            "L\".gstreamer-audio.partial.mp4\"",
            "sessionDirectory / L\"video-gstreamer.intermediate.mp4\"");
        Require(
            !cleanup.Contains("DeleteFileW", StringComparison.Ordinal) &&
            !cleanup.Contains("remove_all", StringComparison.OrdinalIgnoreCase) &&
            !cleanup.Contains("RemoveDirectory", StringComparison.Ordinal),
            "Gate 5 cleanup must route individually proven files through the " +
                "operation-time handle guard, never a pathname or directory scope.");
        RequireAll(source,
            "audioFinalize = {};",
            "audioFinalizePartialPath.clear();",
            "audioVideoBackupPath.clear();");

        string terminal = Slice(
            source,
            "XbPreviewResult VideoEncoderConsumer::StopAndJoin(",
            "XbPreviewResult VideoEncoderConsumer::RequestVideoPause()");
        int persist = terminal.IndexOf(
            "PersistManifestUserCancelled(", StringComparison.Ordinal);
        int cleanupCall = terminal.IndexOf(
            "CleanupUserCancelledMaterials(", StringComparison.Ordinal);
        int cancelledSnapshot = terminal.IndexOf(
            "PublishUserCancelledTerminal(", StringComparison.Ordinal);
        int normalAudioFinal = terminal.IndexOf(
            "PrepareGStreamerAudioFinalCandidate()", StringComparison.Ordinal);
        int normalPublish = terminal.IndexOf(
            "PublishSessionOutput(", StringComparison.Ordinal);
        Require(
            persist >= 0 && cleanupCall > persist &&
            cancelledSnapshot > cleanupCall &&
            normalAudioFinal > cancelledSnapshot &&
            normalPublish > normalAudioFinal &&
            terminal.Contains(
                "if (FAILED(persistenceResult))", StringComparison.Ordinal) &&
            terminal.Contains(
                "if (!impl_->terminalPublished && impl_->outcome.workerExited)",
                StringComparison.Ordinal),
            "Gate 5 must durably persist UserCancelled before exact cleanup and " +
                "finish the cancelled terminal before any normal finalizer/publish path.");

        Console.WriteLine(
            "PANEL4-CANCEL-GATE-5-OWNERSHIP-CLEANUP = PASS");
    }

    private static string Slice(
        string source,
        string startMarker,
        string endMarker)
    {
        int start = source.IndexOf(startMarker, StringComparison.Ordinal);
        int end = start < 0
            ? -1
            : source.IndexOf(endMarker, start, StringComparison.Ordinal);
        Require(start >= 0 && end > start,
            $"Could not locate source contract from '{startMarker}' to " +
                $"'{endMarker}'.");
        return source[start..end];
    }

    private static void RequireAll(string source, params string[] values)
    {
        string? missing = values.FirstOrDefault(value =>
            !source.Contains(value, StringComparison.Ordinal));
        Require(missing is null,
            $"Gate 5 ownership contract is missing '{missing}'.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
