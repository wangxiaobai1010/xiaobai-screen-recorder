#pragma once

#include "RecordingOutputRoot.h"
#include "SessionManifest.h"
#include "SessionPathSafety.h"
#include "SessionLifetimeOwner.h"

#include <windows.h>

#include <cstddef>
#include <cstdint>
#include <filesystem>
#include <optional>
#include <string>
#include <vector>

namespace xbpreview
{
    enum class SessionScanStatus
    {
        Success,
        SessionsRootAbsent,
        SessionsRootInaccessible,
        SessionsRootUnsafe,
        IoFailure,
        PartialTruncated
    };

    enum class SessionClassification
    {
        CompletedConsistent,
        ReconciledCompletedConsistent,
        PublishedMetadataNeedsReconciliation,
        PublishOutcomeUnprovenRetain,
        ReadyToPublishWorkingPreserved,
        IncompleteWithWorkingMedia,
        IncompleteNoMediaRetain,
        PublishFailedWorkingPreserved,
        FinalizeOrValidationFailedWorkingPreserved,
        ManifestCorrupt,
        ManifestMissing,
        FilesystemConflict,
        UnknownRetain,
        UserCancelled
    };

    enum class SessionInspectionSeverity
    {
        Info,
        Attention,
        RecoveryCandidate,
        CriticalRetain
    };

    enum class SessionInspectionReason : std::uint64_t
    {
        None = 0,
        FinalMissing = 1ull << 0,
        WorkingAndFinalBothPresent = 1ull << 1,
        PathOutsideRoot = 1ull << 2,
        ReparsePoint = 1ull << 3,
        IdentityMismatch = 1ull << 4,
        ManifestIoError = 1ull << 5,
        UnsupportedSchema = 1ull << 6,
        LiveOwnerUnknown = 1ull << 7,
        NoMediaProven = 1ull << 8,
        MediaSubmitted = 1ull << 9,
        FinalizeFailed = 1ull << 10,
        ValidationFailed = 1ull << 11,
        PublishFailed = 1ull << 12,
        PublishIdentityUnavailable = 1ull << 13,
        InventoryIncomplete = 1ull << 14,
        ManifestMissing = 1ull << 15,
        ManifestMalformed = 1ull << 16,
        PathInaccessible = 1ull << 17,
        TypeMismatch = 1ull << 18,
        ConcurrentChange = 1ull << 19,
        UnknownState = 1ull << 20,
        LiveOwnerActive = 1ull << 21,
        LifetimeOwnerEvidenceMissing = 1ull << 22
    };

    constexpr SessionInspectionReason operator|(
        const SessionInspectionReason left,
        const SessionInspectionReason right) noexcept
    {
        return static_cast<SessionInspectionReason>(
            static_cast<std::uint64_t>(left) |
            static_cast<std::uint64_t>(right));
    }

    constexpr SessionInspectionReason& operator|=(
        SessionInspectionReason& left,
        const SessionInspectionReason right) noexcept
    {
        left = left | right;
        return left;
    }

    constexpr bool HasSessionInspectionReason(
        const SessionInspectionReason value,
        const SessionInspectionReason flag) noexcept
    {
        return (static_cast<std::uint64_t>(value) &
            static_cast<std::uint64_t>(flag)) != 0;
    }

    enum class InspectedFilesystemState
    {
        NotProvided,
        Exists,
        Absent,
        ParentAbsent,
        Inaccessible,
        OutsideTrustedRoot,
        ReparseEncountered,
        Invalid,
        TypeMismatch,
        IoFailure,
        Unknown
    };

    struct InspectedPathFacts final
    {
        std::filesystem::path candidatePath;
        InspectedFilesystemState state{
            InspectedFilesystemState::NotProvided };
        PathSafetyResult safety;
        std::optional<std::uint64_t> size;
    };

    enum class UnrecognizedSessionEntryKind
    {
        NonCanonicalDirectory,
        NonDirectory,
        ReparseEntry
    };

    struct UnrecognizedSessionEntry final
    {
        std::wstring name;
        std::filesystem::path path;
        UnrecognizedSessionEntryKind kind{
            UnrecognizedSessionEntryKind::NonDirectory };
    };

    struct SessionInspectionResult final
    {
        std::wstring sessionId;
        std::filesystem::path sessionDirectory;
        PathSafetyResult sessionDirectorySafety;
        std::filesystem::path manifestPath;
        PathSafetyResult manifestPathSafety;
        SessionManifestParseResult manifestParse;
        std::optional<SessionManifest> manifest;
        bool manifestRevisionStable{};
        std::optional<std::uint64_t> observedRevision;
        SessionLifetimeOwnerProbeResult lifetimeOwner;
        bool persistentWorkingIdentityAvailable{};
        bool persistentIdentityComparisonAttempted{};
        bool strongIdentityMatch{};
        InspectedPathFacts working;
        InspectedPathFacts plannedFinal;
        InspectedPathFacts published;
        SessionClassification classification{
            SessionClassification::UnknownRetain };
        SessionInspectionSeverity severity{
            SessionInspectionSeverity::CriticalRetain };
        SessionInspectionReason reasons{ SessionInspectionReason::None };
        bool deleteAllowed{};
        bool reconciliationAuthorized{};
    };

    struct SessionInspectionOptions final
    {
        std::size_t maximumEntries{ 1024 };
    };

    struct SessionScanResult final
    {
        SessionScanStatus status{ SessionScanStatus::IoFailure };
        HRESULT diagnosticHResult{ E_UNEXPECTED };
        RecordingOutputRootResolution roots;
        PathSafetyResult sessionsRootSafety;
        std::size_t entriesObserved{};
        std::size_t maximumEntries{};
        bool truncated{};
        bool mediaWithoutSessionDirectoryBlindSpot{ true };
        std::vector<SessionInspectionResult> sessions;
        std::vector<UnrecognizedSessionEntry> unrecognizedEntries;
    };

    // Strictly read-only: this function never creates a directory, writes a
    // Manifest, changes revision/state, publishes, deletes, renames, repairs,
    // or authorizes reconciliation. Results are point-in-time observations.
    [[nodiscard]] SessionScanResult ScanHistoricalRecordingSessions(
        const RecordingOutputRootResolution& roots,
        const SessionInspectionOptions& options = {}) noexcept;
}
