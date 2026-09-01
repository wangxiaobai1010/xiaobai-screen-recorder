#include "SessionInspection.h"

#include "RecordingSessionIdentity.h"

#include <windows.h>

#include <algorithm>
#include <new>
#include <utility>

namespace xbpreview
{
    namespace
    {
        class UniqueFindHandle final
        {
        public:
            explicit UniqueFindHandle(const HANDLE value) noexcept
                : value_(value)
            {
            }

            ~UniqueFindHandle()
            {
                if (value_ != INVALID_HANDLE_VALUE)
                {
                    (void)FindClose(value_);
                }
            }

            UniqueFindHandle(const UniqueFindHandle&) = delete;
            UniqueFindHandle& operator=(const UniqueFindHandle&) = delete;

            [[nodiscard]] bool Valid() const noexcept
            {
                return value_ != INVALID_HANDLE_VALUE;
            }

            [[nodiscard]] HANDLE Get() const noexcept { return value_; }

        private:
            HANDLE value_{ INVALID_HANDLE_VALUE };
        };

        SessionScanResult ScanFailure(
            const RecordingOutputRootResolution& roots,
            const SessionInspectionOptions& options,
            const SessionScanStatus status,
            const HRESULT result) noexcept
        {
            SessionScanResult scan{};
            scan.status = status;
            scan.diagnosticHResult = result;
            scan.maximumEntries = options.maximumEntries;
            try
            {
                scan.roots = roots;
            }
            catch (...)
            {
                // This helper is also used from the outer bad_alloc boundary.
                // The failure status and HRESULT remain available even when
                // copying diagnostic paths is no longer possible.
            }
            return scan;
        }

        bool MissingHResult(const HRESULT result) noexcept
        {
            return HRESULT_FACILITY(result) == FACILITY_WIN32 &&
                (HRESULT_CODE(result) == ERROR_FILE_NOT_FOUND ||
                    HRESULT_CODE(result) == ERROR_PATH_NOT_FOUND);
        }

        InspectedFilesystemState MapFilesystemState(
            const PathSafetyOutcome outcome) noexcept
        {
            switch (outcome)
            {
            case PathSafetyOutcome::SafeForReadOnlyInspection:
                return InspectedFilesystemState::Exists;
            case PathSafetyOutcome::Absent:
                return InspectedFilesystemState::Absent;
            case PathSafetyOutcome::ParentAbsent:
                return InspectedFilesystemState::ParentAbsent;
            case PathSafetyOutcome::Inaccessible:
                return InspectedFilesystemState::Inaccessible;
            case PathSafetyOutcome::OutsideTrustedRoot:
                return InspectedFilesystemState::OutsideTrustedRoot;
            case PathSafetyOutcome::ReparseEncountered:
                return InspectedFilesystemState::ReparseEncountered;
            case PathSafetyOutcome::TypeMismatch:
                return InspectedFilesystemState::TypeMismatch;
            case PathSafetyOutcome::InvalidInput:
            case PathSafetyOutcome::UnsupportedPathForm:
            case PathSafetyOutcome::TrustedRootInvalid:
                return InspectedFilesystemState::Invalid;
            case PathSafetyOutcome::IoFailure:
                return InspectedFilesystemState::IoFailure;
            default:
                return InspectedFilesystemState::Unknown;
            }
        }

        InspectedPathFacts InspectMediaPath(
            const RecordingOutputRootResolution& roots,
            const std::filesystem::path& candidate)
        {
            InspectedPathFacts facts{};
            facts.candidatePath = candidate;
            if (candidate.empty())
            {
                facts.state = InspectedFilesystemState::NotProvided;
                return facts;
            }
            facts.safety = InspectRecordingMediaPathForReadOnly(
                roots, candidate);
            facts.state = MapFilesystemState(facts.safety.outcome);
            facts.size = facts.safety.candidateSize;
            return facts;
        }

        bool IsExists(const InspectedPathFacts& facts) noexcept
        {
            return facts.state == InspectedFilesystemState::Exists;
        }

        bool IsAbsent(const InspectedPathFacts& facts) noexcept
        {
            return facts.state == InspectedFilesystemState::Absent;
        }

        bool IsUnsafe(const InspectedPathFacts& facts) noexcept
        {
            return facts.state ==
                    InspectedFilesystemState::OutsideTrustedRoot ||
                facts.state ==
                    InspectedFilesystemState::ReparseEncountered ||
                facts.state == InspectedFilesystemState::Invalid ||
                facts.state == InspectedFilesystemState::TypeMismatch;
        }

        bool IsUncertain(const InspectedPathFacts& facts) noexcept
        {
            return facts.state == InspectedFilesystemState::ParentAbsent ||
                facts.state == InspectedFilesystemState::Inaccessible ||
                facts.state == InspectedFilesystemState::IoFailure ||
                facts.state == InspectedFilesystemState::Unknown;
        }

        bool SameObservedObject(
            const InspectedPathFacts& left,
            const InspectedPathFacts& right) noexcept
        {
            return SamePersistentFileIdentity(
                left.safety.candidateIdentity,
                right.safety.candidateIdentity);
        }

        void AddPathReasons(
            const InspectedPathFacts& facts,
            SessionInspectionReason& reasons) noexcept
        {
            switch (facts.state)
            {
            case InspectedFilesystemState::OutsideTrustedRoot:
                reasons |= SessionInspectionReason::PathOutsideRoot;
                break;
            case InspectedFilesystemState::ReparseEncountered:
                reasons |= SessionInspectionReason::ReparsePoint;
                break;
            case InspectedFilesystemState::Inaccessible:
                reasons |= SessionInspectionReason::PathInaccessible;
                break;
            case InspectedFilesystemState::TypeMismatch:
                reasons |= SessionInspectionReason::TypeMismatch;
                break;
            case InspectedFilesystemState::ParentAbsent:
            case InspectedFilesystemState::IoFailure:
            case InspectedFilesystemState::Unknown:
                reasons |= SessionInspectionReason::ManifestIoError;
                break;
            default:
                break;
            }
        }

        void SetClassification(
            SessionInspectionResult& result,
            const SessionClassification classification,
            const SessionInspectionSeverity severity) noexcept
        {
            result.classification = classification;
            result.severity = severity;
            result.deleteAllowed = false;
            result.reconciliationAuthorized = false;
        }

        void Classify(SessionInspectionResult& result) noexcept
        {
            switch (result.lifetimeOwner.state)
            {
            case SessionLifetimeOwnerProbeState::ActiveOwned:
                result.reasons |= SessionInspectionReason::LiveOwnerActive;
                break;
            case SessionLifetimeOwnerProbeState::InactiveLeaseReleased:
                break;
            case SessionLifetimeOwnerProbeState::EvidenceMissing:
                result.reasons |=
                    SessionInspectionReason::LiveOwnerUnknown |
                    SessionInspectionReason::LifetimeOwnerEvidenceMissing;
                break;
            default:
                result.reasons |= SessionInspectionReason::LiveOwnerUnknown;
                break;
            }

            if (!result.sessionDirectorySafety.SafeForReadOnlyInspection())
            {
                if (result.sessionDirectorySafety.outcome ==
                    PathSafetyOutcome::ReparseEncountered)
                {
                    result.reasons |= SessionInspectionReason::ReparsePoint;
                }
                else if (result.sessionDirectorySafety.outcome ==
                    PathSafetyOutcome::OutsideTrustedRoot)
                {
                    result.reasons |= SessionInspectionReason::PathOutsideRoot;
                }
                else
                {
                    result.reasons |= SessionInspectionReason::ManifestIoError;
                }
                SetClassification(
                    result,
                    SessionClassification::FilesystemConflict,
                    SessionInspectionSeverity::CriticalRetain);
                return;
            }

            if (!result.manifestPathSafety.SafeForReadOnlyInspection() &&
                result.manifestPathSafety.outcome != PathSafetyOutcome::Absent)
            {
                if (result.manifestPathSafety.outcome ==
                    PathSafetyOutcome::Inaccessible)
                {
                    result.reasons |=
                        SessionInspectionReason::ManifestIoError |
                        SessionInspectionReason::PathInaccessible;
                    SetClassification(
                        result,
                        SessionClassification::UnknownRetain,
                        SessionInspectionSeverity::CriticalRetain);
                }
                else
                {
                    if (result.manifestPathSafety.outcome ==
                        PathSafetyOutcome::ReparseEncountered)
                    {
                        result.reasons |=
                            SessionInspectionReason::ReparsePoint;
                    }
                    else if (result.manifestPathSafety.outcome ==
                        PathSafetyOutcome::TypeMismatch)
                    {
                        result.reasons |=
                            SessionInspectionReason::TypeMismatch;
                    }
                    else
                    {
                        result.reasons |=
                            SessionInspectionReason::PathOutsideRoot;
                    }
                    SetClassification(
                        result,
                        SessionClassification::FilesystemConflict,
                        SessionInspectionSeverity::CriticalRetain);
                }
                return;
            }

            AddPathReasons(result.working, result.reasons);
            AddPathReasons(result.plannedFinal, result.reasons);
            AddPathReasons(result.published, result.reasons);
            if (IsUnsafe(result.working) || IsUnsafe(result.plannedFinal) ||
                IsUnsafe(result.published))
            {
                SetClassification(
                    result,
                    SessionClassification::FilesystemConflict,
                    SessionInspectionSeverity::CriticalRetain);
                return;
            }
            if (IsUncertain(result.working) ||
                IsUncertain(result.plannedFinal) ||
                IsUncertain(result.published))
            {
                SetClassification(
                    result,
                    SessionClassification::UnknownRetain,
                    SessionInspectionSeverity::CriticalRetain);
                return;
            }

            switch (result.manifestParse.status)
            {
            case SessionManifestParseStatus::NotFound:
                result.reasons |= SessionInspectionReason::ManifestMissing;
                SetClassification(
                    result,
                    SessionClassification::ManifestMissing,
                    SessionInspectionSeverity::CriticalRetain);
                return;
            case SessionManifestParseStatus::MalformedJson:
                result.reasons |= SessionInspectionReason::ManifestMalformed;
                SetClassification(
                    result,
                    SessionClassification::ManifestCorrupt,
                    SessionInspectionSeverity::CriticalRetain);
                return;
            case SessionManifestParseStatus::SemanticInvalid:
                if (result.manifestParse.semanticIssue ==
                    SessionManifestSemanticIssue::SessionIdentityMismatch)
                {
                    result.reasons |= SessionInspectionReason::IdentityMismatch;
                    SetClassification(
                        result,
                        SessionClassification::FilesystemConflict,
                        SessionInspectionSeverity::CriticalRetain);
                }
                else if (result.manifestParse.semanticIssue ==
                        SessionManifestSemanticIssue::PathPolicyViolation ||
                    result.manifestParse.semanticIssue ==
                        SessionManifestSemanticIssue::PublishedPathMismatch)
                {
                    result.reasons |=
                        result.manifestParse.semanticIssue ==
                            SessionManifestSemanticIssue::PathPolicyViolation
                        ? SessionInspectionReason::PathOutsideRoot
                        : SessionInspectionReason::IdentityMismatch;
                    SetClassification(
                        result,
                        SessionClassification::FilesystemConflict,
                        SessionInspectionSeverity::CriticalRetain);
                }
                else
                {
                    result.reasons |=
                        SessionInspectionReason::ManifestMalformed;
                    SetClassification(
                        result,
                        SessionClassification::ManifestCorrupt,
                        SessionInspectionSeverity::CriticalRetain);
                }
                return;
            case SessionManifestParseStatus::UnsupportedSchema:
                result.reasons |= SessionInspectionReason::UnsupportedSchema;
                SetClassification(
                    result,
                    SessionClassification::UnknownRetain,
                    SessionInspectionSeverity::CriticalRetain);
                return;
            case SessionManifestParseStatus::UnknownOrFutureState:
                result.reasons |= SessionInspectionReason::UnknownState;
                SetClassification(
                    result,
                    SessionClassification::UnknownRetain,
                    SessionInspectionSeverity::CriticalRetain);
                return;
            case SessionManifestParseStatus::Inaccessible:
            case SessionManifestParseStatus::IoFailure:
                result.reasons |= SessionInspectionReason::ManifestIoError;
                SetClassification(
                    result,
                    SessionClassification::UnknownRetain,
                    SessionInspectionSeverity::CriticalRetain);
                return;
            case SessionManifestParseStatus::Valid:
                break;
            default:
                SetClassification(
                    result,
                    SessionClassification::UnknownRetain,
                    SessionInspectionSeverity::CriticalRetain);
                return;
            }

            if (!result.manifest.has_value() ||
                !result.manifestRevisionStable)
            {
                result.reasons |= SessionInspectionReason::ConcurrentChange;
                SetClassification(
                    result,
                    SessionClassification::UnknownRetain,
                    SessionInspectionSeverity::CriticalRetain);
                return;
            }

            const auto& manifest = *result.manifest;
            const bool workingExists = IsExists(result.working);
            const bool finalExists = IsExists(result.plannedFinal);
            if (finalExists && IsExists(result.published) &&
                !SameObservedObject(result.plannedFinal, result.published))
            {
                result.reasons |= SessionInspectionReason::ConcurrentChange;
                SetClassification(
                    result,
                    SessionClassification::FilesystemConflict,
                    SessionInspectionSeverity::CriticalRetain);
                return;
            }
            if (workingExists && finalExists)
            {
                result.reasons |=
                    SessionInspectionReason::WorkingAndFinalBothPresent;
                SetClassification(
                    result,
                    SessionClassification::FilesystemConflict,
                    SessionInspectionSeverity::CriticalRetain);
                return;
            }

            if (manifest.state == SessionManifestState::UserCancelled)
            {
                if (finalExists)
                {
                    SetClassification(
                        result,
                        SessionClassification::FilesystemConflict,
                        SessionInspectionSeverity::CriticalRetain);
                }
                else
                {
                    SetClassification(
                        result,
                        SessionClassification::UserCancelled,
                        SessionInspectionSeverity::Info);
                }
                return;
            }

            if (manifest.publish.attempted && manifest.publish.hresult &&
                FAILED(*manifest.publish.hresult))
            {
                result.reasons |= SessionInspectionReason::PublishFailed;
                SetClassification(
                    result,
                    workingExists
                        ? SessionClassification::
                            PublishFailedWorkingPreserved
                        : SessionClassification::UnknownRetain,
                    workingExists
                        ? SessionInspectionSeverity::RecoveryCandidate
                        : SessionInspectionSeverity::CriticalRetain);
                return;
            }
            if (manifest.finalize.attempted && manifest.finalize.hresult &&
                FAILED(*manifest.finalize.hresult))
            {
                result.reasons |= SessionInspectionReason::FinalizeFailed;
                SetClassification(
                    result,
                    workingExists
                        ? SessionClassification::
                            FinalizeOrValidationFailedWorkingPreserved
                        : SessionClassification::UnknownRetain,
                    workingExists
                        ? SessionInspectionSeverity::RecoveryCandidate
                        : SessionInspectionSeverity::CriticalRetain);
                return;
            }
            if (manifest.validation.attempted &&
                (!manifest.validation.hresult ||
                    FAILED(*manifest.validation.hresult)))
            {
                result.reasons |= SessionInspectionReason::ValidationFailed;
                SetClassification(
                    result,
                    workingExists
                        ? SessionClassification::
                            FinalizeOrValidationFailedWorkingPreserved
                        : SessionClassification::UnknownRetain,
                    workingExists
                        ? SessionInspectionSeverity::RecoveryCandidate
                        : SessionInspectionSeverity::CriticalRetain);
                return;
            }

            const bool mediaSubmitted = manifest.writeSampleAttempted ||
                manifest.frameSubmitted || manifest.workingFileOwnedBySession;
            if (mediaSubmitted)
            {
                result.reasons |= SessionInspectionReason::MediaSubmitted;
            }

            switch (manifest.state)
            {
            case SessionManifestState::ReconciledCompleted:
                if (finalExists && !workingExists &&
                    result.persistentWorkingIdentityAvailable &&
                    result.persistentIdentityComparisonAttempted &&
                    result.strongIdentityMatch &&
                    result.plannedFinal.size.has_value() &&
                    *result.plannedFinal.size > 0 &&
                    result.plannedFinal.safety.candidateIdentity.available &&
                    result.plannedFinal.safety.candidateIdentity.hardLinkCount == 1)
                {
                    SetClassification(
                        result,
                        SessionClassification::
                            ReconciledCompletedConsistent,
                        SessionInspectionSeverity::Info);
                }
                else
                {
                    if (!finalExists)
                    {
                        result.reasons |=
                            SessionInspectionReason::FinalMissing;
                    }
                    if (!result.persistentWorkingIdentityAvailable ||
                        !result.persistentIdentityComparisonAttempted ||
                        !result.strongIdentityMatch ||
                        (result.plannedFinal.safety.candidateIdentity.available &&
                            result.plannedFinal.safety.candidateIdentity.
                                hardLinkCount != 1))
                    {
                        result.reasons |=
                            SessionInspectionReason::IdentityMismatch;
                    }
                    SetClassification(
                        result,
                        SessionClassification::FilesystemConflict,
                        SessionInspectionSeverity::CriticalRetain);
                }
                return;
            case SessionManifestState::Completed:
                if (manifest.publish.published && finalExists &&
                    !workingExists && IsExists(result.published))
                {
                    SetClassification(
                        result,
                        SessionClassification::CompletedConsistent,
                        SessionInspectionSeverity::Info);
                }
                else
                {
                    if (!finalExists)
                    {
                        result.reasons |= SessionInspectionReason::FinalMissing;
                    }
                    SetClassification(
                        result,
                        SessionClassification::FilesystemConflict,
                        SessionInspectionSeverity::CriticalRetain);
                }
                return;
            case SessionManifestState::Published:
                if (manifest.publish.published && finalExists &&
                    !workingExists && IsExists(result.published))
                {
                    SetClassification(
                        result,
                        SessionClassification::
                            PublishedMetadataNeedsReconciliation,
                        SessionInspectionSeverity::Attention);
                }
                else
                {
                    if (!finalExists)
                    {
                        result.reasons |= SessionInspectionReason::FinalMissing;
                    }
                    SetClassification(
                        result,
                        SessionClassification::FilesystemConflict,
                        SessionInspectionSeverity::CriticalRetain);
                }
                return;
            case SessionManifestState::ReadyToPublish:
                if (finalExists && !workingExists)
                {
                    result.reasons |=
                        SessionInspectionReason::PublishIdentityUnavailable;
                    SetClassification(
                        result,
                        SessionClassification::
                            PublishOutcomeUnprovenRetain,
                        SessionInspectionSeverity::CriticalRetain);
                }
                else if (workingExists && !finalExists)
                {
                    SetClassification(
                        result,
                        SessionClassification::
                            ReadyToPublishWorkingPreserved,
                        SessionInspectionSeverity::RecoveryCandidate);
                }
                else if (!workingExists && !finalExists && !mediaSubmitted)
                {
                    result.reasons |= SessionInspectionReason::NoMediaProven;
                    SetClassification(
                        result,
                        SessionClassification::IncompleteNoMediaRetain,
                        SessionInspectionSeverity::Attention);
                }
                else
                {
                    SetClassification(
                        result,
                        SessionClassification::FilesystemConflict,
                        SessionInspectionSeverity::CriticalRetain);
                }
                return;
            case SessionManifestState::Created:
            case SessionManifestState::Starting:
            case SessionManifestState::Recording:
            case SessionManifestState::Stopping:
            case SessionManifestState::Failed:
                if (finalExists)
                {
                    SetClassification(
                        result,
                        SessionClassification::FilesystemConflict,
                        SessionInspectionSeverity::CriticalRetain);
                }
                else if (workingExists)
                {
                    SetClassification(
                        result,
                        SessionClassification::IncompleteWithWorkingMedia,
                        SessionInspectionSeverity::RecoveryCandidate);
                }
                else if (!mediaSubmitted)
                {
                    result.reasons |= SessionInspectionReason::NoMediaProven;
                    SetClassification(
                        result,
                        SessionClassification::IncompleteNoMediaRetain,
                        SessionInspectionSeverity::Attention);
                }
                else
                {
                    SetClassification(
                        result,
                        SessionClassification::FilesystemConflict,
                        SessionInspectionSeverity::CriticalRetain);
                }
                return;
            case SessionManifestState::Unknown:
            default:
                result.reasons |= SessionInspectionReason::UnknownState;
                SetClassification(
                    result,
                    SessionClassification::UnknownRetain,
                    SessionInspectionSeverity::CriticalRetain);
                return;
            }
        }

        std::filesystem::path ExpectedWorkingPath(
            const RecordingOutputRootResolution& roots,
            const std::wstring& sessionId)
        {
            return roots.mediaOutputRoot /
                (sessionId + L".partial.mp4");
        }

        std::filesystem::path ExpectedFinalPath(
            const RecordingOutputRootResolution& roots,
            const std::wstring& sessionId)
        {
            return roots.mediaOutputRoot / (sessionId + L".mp4");
        }

        SessionInspectionResult InspectSession(
            const RecordingOutputRootResolution& roots,
            const std::wstring& sessionId)
        {
            SessionInspectionResult result{};
            result.sessionId = sessionId;
            result.sessionDirectory = roots.sessionsRoot / sessionId;
            result.sessionDirectorySafety =
                InspectCanonicalSessionDirectoryForReadOnly(roots, sessionId);
            result.manifestPath = result.sessionDirectory / L"manifest.json";

            if (!result.sessionDirectorySafety.SafeForReadOnlyInspection())
            {
                Classify(result);
                return result;
            }

            result.lifetimeOwner = ProbeSessionLifetimeOwner(roots, sessionId);

            result.manifestPathSafety = InspectPathForReadOnly(
                result.sessionDirectory,
                result.manifestPath,
                PathSafetyExpectedType::RegularFile);

            SessionManifest parsed{};
            if (result.manifestPathSafety.SafeForReadOnlyInspection())
            {
                SessionManifestStore store(roots.mediaOutputRoot, sessionId);
                result.manifestParse = store.ParseManifest(parsed);
            }
            else if (result.manifestPathSafety.outcome ==
                PathSafetyOutcome::Absent)
            {
                result.manifestParse.status =
                    SessionManifestParseStatus::NotFound;
                result.manifestParse.diagnosticHResult =
                    result.manifestPathSafety.diagnosticHResult;
            }
            else if (result.manifestPathSafety.outcome ==
                PathSafetyOutcome::Inaccessible)
            {
                result.manifestParse.status =
                    SessionManifestParseStatus::Inaccessible;
                result.manifestParse.diagnosticHResult =
                    result.manifestPathSafety.diagnosticHResult;
            }
            else
            {
                result.manifestParse.status =
                    SessionManifestParseStatus::IoFailure;
                result.manifestParse.diagnosticHResult =
                    result.manifestPathSafety.diagnosticHResult;
            }

            if (result.manifestParse.status ==
                SessionManifestParseStatus::Valid)
            {
                result.observedRevision = parsed.revision;
                result.working = InspectMediaPath(
                    roots, parsed.workingPath);
                result.plannedFinal = InspectMediaPath(
                    roots, parsed.plannedFinalPath);
                result.published = InspectMediaPath(
                    roots, parsed.publishedPath);

                result.persistentWorkingIdentityAvailable =
                    parsed.workingFileIdentity.captured;
                if (result.persistentWorkingIdentityAvailable &&
                    result.plannedFinal.safety.candidateIdentity.available)
                {
                    PersistentFileIdentity persisted{};
                    persisted.available =
                        ParseVolumeIdentityCanonical(
                            parsed.workingFileIdentity.volumeIdentity,
                            persisted.volumeSerialNumber) &&
                        ParseFileIdCanonical(
                            parsed.workingFileIdentity.fileId,
                            persisted.fileId);
                    result.persistentIdentityComparisonAttempted =
                        persisted.available;
                    result.strongIdentityMatch =
                        SamePersistentFileIdentity(
                            persisted,
                            result.plannedFinal.safety.candidateIdentity);
                }

                SessionManifest stable{};
                SessionManifestStore store(roots.mediaOutputRoot, sessionId);
                const auto stableRead = store.ParseManifest(stable);
                result.manifestRevisionStable =
                    stableRead.status == SessionManifestParseStatus::Valid &&
                    stable.revision == parsed.revision;
                if (!result.manifestRevisionStable)
                {
                    result.reasons |=
                        SessionInspectionReason::ConcurrentChange;
                }
                result.manifest = std::move(parsed);
            }
            else
            {
                // Invalid/unavailable Manifest paths are never followed. Only
                // deterministic media leaves derived from the trusted root and
                // canonical directory identity are inspected.
                result.working = InspectMediaPath(
                    roots, ExpectedWorkingPath(roots, sessionId));
                result.plannedFinal = InspectMediaPath(
                    roots, ExpectedFinalPath(roots, sessionId));
            }

            Classify(result);
            return result;
        }

        bool AccessFailure(const DWORD error) noexcept
        {
            return error == ERROR_ACCESS_DENIED ||
                error == ERROR_SHARING_VIOLATION ||
                error == ERROR_LOCK_VIOLATION ||
                error == ERROR_PRIVILEGE_NOT_HELD;
        }
    }

    SessionScanResult ScanHistoricalRecordingSessions(
        const RecordingOutputRootResolution& roots,
        const SessionInspectionOptions& options) noexcept
    {
        try
        {
            if (!roots.Succeeded() || options.maximumEntries == 0)
            {
                return ScanFailure(
                    roots, options, SessionScanStatus::IoFailure, E_INVALIDARG);
            }
            const auto expectedRoots =
                ResolveRecordingOutputRootsFromManagedRoot(
                    roots.mediaOutputRoot);
            if (!expectedRoots.Succeeded() ||
                expectedRoots.mediaOutputRoot != roots.mediaOutputRoot ||
                expectedRoots.sessionsRoot != roots.sessionsRoot)
            {
                return ScanFailure(
                    roots,
                    options,
                    SessionScanStatus::SessionsRootUnsafe,
                    E_INVALIDARG);
            }

            SessionScanResult scan{};
            scan.roots = roots;
            scan.maximumEntries = options.maximumEntries;
            scan.sessionsRootSafety = InspectPathForReadOnly(
                roots.mediaOutputRoot,
                roots.sessionsRoot,
                PathSafetyExpectedType::Directory);
            if (!scan.sessionsRootSafety.SafeForReadOnlyInspection())
            {
                if (scan.sessionsRootSafety.outcome ==
                        PathSafetyOutcome::Absent ||
                    (scan.sessionsRootSafety.outcome ==
                        PathSafetyOutcome::TrustedRootInvalid &&
                        MissingHResult(
                            scan.sessionsRootSafety.diagnosticHResult)))
                {
                    scan.status = SessionScanStatus::SessionsRootAbsent;
                    scan.diagnosticHResult = S_OK;
                }
                else if (scan.sessionsRootSafety.outcome ==
                    PathSafetyOutcome::Inaccessible)
                {
                    scan.status =
                        SessionScanStatus::SessionsRootInaccessible;
                    scan.diagnosticHResult =
                        scan.sessionsRootSafety.diagnosticHResult;
                }
                else if (scan.sessionsRootSafety.outcome ==
                        PathSafetyOutcome::ReparseEncountered ||
                    scan.sessionsRootSafety.outcome ==
                        PathSafetyOutcome::OutsideTrustedRoot ||
                    scan.sessionsRootSafety.outcome ==
                        PathSafetyOutcome::TypeMismatch ||
                    scan.sessionsRootSafety.outcome ==
                        PathSafetyOutcome::UnsupportedPathForm ||
                    scan.sessionsRootSafety.outcome ==
                        PathSafetyOutcome::InvalidInput)
                {
                    scan.status = SessionScanStatus::SessionsRootUnsafe;
                    scan.diagnosticHResult =
                        scan.sessionsRootSafety.diagnosticHResult;
                }
                else
                {
                    scan.status = SessionScanStatus::IoFailure;
                    scan.diagnosticHResult =
                        scan.sessionsRootSafety.diagnosticHResult;
                }
                return scan;
            }

            const auto pattern = roots.sessionsRoot / L"*";
            WIN32_FIND_DATAW data{};
            UniqueFindHandle find(FindFirstFileW(pattern.c_str(), &data));
            if (!find.Valid())
            {
                const auto error = GetLastError();
                if (error == ERROR_FILE_NOT_FOUND)
                {
                    scan.status = SessionScanStatus::Success;
                    scan.diagnosticHResult = S_OK;
                }
                else if (AccessFailure(error))
                {
                    scan.status =
                        SessionScanStatus::SessionsRootInaccessible;
                    scan.diagnosticHResult = HRESULT_FROM_WIN32(error);
                }
                else
                {
                    scan.status = SessionScanStatus::IoFailure;
                    scan.diagnosticHResult = HRESULT_FROM_WIN32(error);
                }
                return scan;
            }

            bool more = true;
            while (more)
            {
                const std::wstring name(data.cFileName);
                if (name != L"." && name != L"..")
                {
                    if (scan.entriesObserved >= options.maximumEntries)
                    {
                        scan.truncated = true;
                        scan.status = SessionScanStatus::PartialTruncated;
                        scan.diagnosticHResult = S_FALSE;
                        break;
                    }
                    ++scan.entriesObserved;
                    const auto entryPath = roots.sessionsRoot / name;
                    const bool directory =
                        (data.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) != 0;
                    const bool reparse =
                        (data.dwFileAttributes &
                            FILE_ATTRIBUTE_REPARSE_POINT) != 0;
                    if (!directory || reparse ||
                        !IsCanonicalRecordingSessionId(name))
                    {
                        UnrecognizedSessionEntry entry{};
                        entry.name = name;
                        entry.path = entryPath;
                        entry.kind = reparse
                            ? UnrecognizedSessionEntryKind::ReparseEntry
                            : directory
                                ? UnrecognizedSessionEntryKind::
                                    NonCanonicalDirectory
                                : UnrecognizedSessionEntryKind::NonDirectory;
                        scan.unrecognizedEntries.push_back(std::move(entry));
                    }
                    else
                    {
                        try
                        {
                            scan.sessions.push_back(
                                InspectSession(roots, name));
                        }
                        catch (...)
                        {
                            SessionInspectionResult failure{};
                            failure.sessionId = name;
                            failure.sessionDirectory = entryPath;
                            failure.reasons |=
                                SessionInspectionReason::ManifestIoError |
                                SessionInspectionReason::InventoryIncomplete;
                            SetClassification(
                                failure,
                                SessionClassification::UnknownRetain,
                                SessionInspectionSeverity::CriticalRetain);
                            scan.sessions.push_back(std::move(failure));
                        }
                    }
                }

                more = FindNextFileW(find.Get(), &data) != FALSE;
                if (!more)
                {
                    const auto error = GetLastError();
                    if (error != ERROR_NO_MORE_FILES)
                    {
                        scan.status = scan.entriesObserved > 0
                            ? SessionScanStatus::PartialTruncated
                            : SessionScanStatus::IoFailure;
                        scan.truncated = scan.entriesObserved > 0;
                        scan.diagnosticHResult = HRESULT_FROM_WIN32(error);
                    }
                }
            }

            std::sort(
                scan.sessions.begin(), scan.sessions.end(),
                [](const auto& left, const auto& right)
                {
                    return left.sessionId < right.sessionId;
                });
            std::sort(
                scan.unrecognizedEntries.begin(),
                scan.unrecognizedEntries.end(),
                [](const auto& left, const auto& right)
                {
                    return left.name < right.name;
                });
            if (!scan.truncated && scan.status == SessionScanStatus::IoFailure &&
                scan.diagnosticHResult == E_UNEXPECTED)
            {
                scan.status = SessionScanStatus::Success;
                scan.diagnosticHResult = S_OK;
            }
            return scan;
        }
        catch (const std::bad_alloc&)
        {
            return ScanFailure(
                roots, options, SessionScanStatus::IoFailure, E_OUTOFMEMORY);
        }
        catch (...)
        {
            return ScanFailure(
                roots, options, SessionScanStatus::IoFailure, E_UNEXPECTED);
        }
    }
}
