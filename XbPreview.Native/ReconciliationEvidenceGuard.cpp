#include "ReconciliationEvidenceGuard.h"

#include "RecordingSessionIdentity.h"
#include "SessionPathSafety.h"

#include <new>
#include <utility>
#include <vector>

namespace xbpreview
{
    namespace
    {
        bool ValidHandle(const HANDLE value) noexcept
        {
            return value != nullptr && value != INVALID_HANDLE_VALUE;
        }

        ReconciliationEvidenceGuardResult GuardResult(
            const ReconciliationEvidenceGuardStatus status,
            const HRESULT hresult,
            const std::uint64_t expectedRevision,
            const std::optional<std::uint64_t> observedRevision =
                std::nullopt) noexcept
        {
            ReconciliationEvidenceGuardResult result{};
            result.status = status;
            result.diagnosticHResult = hresult;
            result.expectedRevision = expectedRevision;
            result.observedRevision = observedRevision;
            return result;
        }

        ReconciliationEvidenceGuardStatus MapLeaseFailure(
            const SessionLifetimeOwnerAcquireStatus status) noexcept
        {
            switch (status)
            {
            case SessionLifetimeOwnerAcquireStatus::AlreadyOwned:
                return ReconciliationEvidenceGuardStatus::ActiveOwner;
            case SessionLifetimeOwnerAcquireStatus::EvidenceMissing:
                return ReconciliationEvidenceGuardStatus::OwnerEvidenceMissing;
            case SessionLifetimeOwnerAcquireStatus::UnsafePath:
            case SessionLifetimeOwnerAcquireStatus::InvalidInput:
                return ReconciliationEvidenceGuardStatus::PathUnsafe;
            case SessionLifetimeOwnerAcquireStatus::Inaccessible:
                return ReconciliationEvidenceGuardStatus::PathInaccessible;
            case SessionLifetimeOwnerAcquireStatus::IoFailure:
            case SessionLifetimeOwnerAcquireStatus::Unavailable:
                return ReconciliationEvidenceGuardStatus::IoFailure;
            default:
                return ReconciliationEvidenceGuardStatus::Unknown;
            }
        }

        ReconciliationEvidenceGuardStatus MapTransactionFailure(
            const SessionManifestCompareExchangeResult& result) noexcept
        {
            if (result.semanticIssue ==
                SessionManifestSemanticIssue::PathPolicyViolation)
            {
                return ReconciliationEvidenceGuardStatus::PathUnsafe;
            }
            switch (result.status)
            {
            case SessionManifestCompareExchangeStatus::RevisionMismatch:
                return ReconciliationEvidenceGuardStatus::RevisionMismatch;
            case SessionManifestCompareExchangeStatus::UnsupportedSchema:
            case SessionManifestCompareExchangeStatus::MalformedManifest:
            case SessionManifestCompareExchangeStatus::SemanticInvalid:
                return ReconciliationEvidenceGuardStatus::ManifestUnsupported;
            case SessionManifestCompareExchangeStatus::NotFound:
                return ReconciliationEvidenceGuardStatus::ManifestNotEligible;
            case SessionManifestCompareExchangeStatus::Inaccessible:
                return ReconciliationEvidenceGuardStatus::PathInaccessible;
            case SessionManifestCompareExchangeStatus::ConcurrentChange:
                return ReconciliationEvidenceGuardStatus::ConcurrentChange;
            case SessionManifestCompareExchangeStatus::AtomicWriteFailure:
            case SessionManifestCompareExchangeStatus::IoFailure:
                return ReconciliationEvidenceGuardStatus::IoFailure;
            default:
                return ReconciliationEvidenceGuardStatus::Unknown;
            }
        }

        HRESULT ReadFinalPath(
            const HANDLE handle,
            std::wstring& finalPath)
        {
            const auto required = GetFinalPathNameByHandleW(
                handle,
                nullptr,
                0,
                FILE_NAME_NORMALIZED | VOLUME_NAME_DOS);
            if (required == 0)
            {
                return HRESULT_FROM_WIN32(GetLastError());
            }
            std::vector<wchar_t> buffer(
                static_cast<std::size_t>(required) + 1);
            const auto written = GetFinalPathNameByHandleW(
                handle,
                buffer.data(),
                static_cast<DWORD>(buffer.size()),
                FILE_NAME_NORMALIZED | VOLUME_NAME_DOS);
            if (written == 0 || written >= buffer.size())
            {
                return HRESULT_FROM_WIN32(
                    written == 0 ? GetLastError() : ERROR_INSUFFICIENT_BUFFER);
            }
            finalPath.assign(buffer.data(), written);
            return S_OK;
        }

        ReconciliationEvidenceGuardResult WorkingAbsenceResult(
            const RecordingOutputRootResolution& roots,
            const std::filesystem::path& workingPath,
            const HANDLE mediaRootHandle,
            const std::uint64_t expectedRevision,
            const std::optional<std::uint64_t> observedRevision)
        {
            if (!ValidHandle(mediaRootHandle) ||
                workingPath.parent_path() != roots.mediaOutputRoot)
            {
                return GuardResult(
                    ReconciliationEvidenceGuardStatus::PathUnsafe,
                    E_ACCESSDENIED,
                    expectedRevision,
                    observedRevision);
            }
            const auto handle = CreateFileW(
                workingPath.c_str(),
                FILE_READ_ATTRIBUTES | FILE_READ_DATA,
                FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
                nullptr,
                OPEN_EXISTING,
                FILE_FLAG_OPEN_REPARSE_POINT,
                nullptr);
            if (!ValidHandle(handle))
            {
                const auto error = GetLastError();
                if (error == ERROR_FILE_NOT_FOUND)
                {
                    return GuardResult(
                        ReconciliationEvidenceGuardStatus::EvidenceComplete,
                        S_OK,
                        expectedRevision,
                        observedRevision);
                }
                return GuardResult(
                    ReconciliationEvidenceGuardStatus::WorkingAbsenceUnproven,
                    HRESULT_FROM_WIN32(error),
                    expectedRevision,
                    observedRevision);
            }
            (void)CloseHandle(handle);
            return GuardResult(
                ReconciliationEvidenceGuardStatus::WorkingStillPresent,
                HRESULT_FROM_WIN32(ERROR_FILE_EXISTS),
                expectedRevision,
                observedRevision);
        }

        ReconciliationEvidenceGuardResult ValidateHeldRegularFile(
            const RecordingOutputRootResolution& roots,
            const std::filesystem::path& finalPath,
            const HANDLE finalHandle,
            PersistentFileIdentity& finalIdentity,
            std::wstring& finalResolvedPath,
            const std::uint64_t expectedRevision,
            const std::optional<std::uint64_t> observedRevision)
        {
            FILE_ATTRIBUTE_TAG_INFO attributes{};
            if (!GetFileInformationByHandleEx(
                    finalHandle,
                    FileAttributeTagInfo,
                    &attributes,
                    sizeof(attributes)))
            {
                return GuardResult(
                    ReconciliationEvidenceGuardStatus::IoFailure,
                    HRESULT_FROM_WIN32(GetLastError()),
                    expectedRevision,
                    observedRevision);
            }
            if ((attributes.FileAttributes & FILE_ATTRIBUTE_REPARSE_POINT) != 0 ||
                (attributes.FileAttributes & FILE_ATTRIBUTE_DIRECTORY) != 0)
            {
                return GuardResult(
                    ReconciliationEvidenceGuardStatus::FinalUnsafe,
                    HRESULT_FROM_WIN32(ERROR_REPARSE_TAG_INVALID),
                    expectedRevision,
                    observedRevision);
            }
            SetLastError(ERROR_SUCCESS);
            if (GetFileType(finalHandle) != FILE_TYPE_DISK)
            {
                const auto error = GetLastError();
                return GuardResult(
                    ReconciliationEvidenceGuardStatus::FinalUnsafe,
                    HRESULT_FROM_WIN32(error == ERROR_SUCCESS
                        ? ERROR_FILE_INVALID
                        : error),
                    expectedRevision,
                    observedRevision);
            }
            auto result = ReadPersistentFileIdentity(
                finalHandle, finalIdentity);
            if (FAILED(result))
            {
                return GuardResult(
                    ReconciliationEvidenceGuardStatus::IoFailure,
                    result,
                    expectedRevision,
                    observedRevision);
            }
            result = ReadFinalPath(finalHandle, finalResolvedPath);
            if (FAILED(result))
            {
                return GuardResult(
                    ReconciliationEvidenceGuardStatus::IoFailure,
                    result,
                    expectedRevision,
                    observedRevision);
            }
            if (!finalIdentity.fileSizeBytes.has_value() ||
                *finalIdentity.fileSizeBytes == 0)
            {
                return GuardResult(
                    ReconciliationEvidenceGuardStatus::FinalUnsafe,
                    HRESULT_FROM_WIN32(ERROR_FILE_INVALID),
                    expectedRevision,
                    observedRevision);
            }
            const auto stableSafety = InspectRecordingMediaPathForReadOnly(
                roots, finalPath);
            if (!stableSafety.SafeForReadOnlyInspection() ||
                !SamePersistentFileIdentity(
                    finalIdentity, stableSafety.candidateIdentity))
            {
                const auto status = stableSafety.outcome ==
                        PathSafetyOutcome::Inaccessible
                    ? ReconciliationEvidenceGuardStatus::PathInaccessible
                    : ReconciliationEvidenceGuardStatus::FinalUnsafe;
                return GuardResult(
                    status,
                    FAILED(stableSafety.diagnosticHResult)
                        ? stableSafety.diagnosticHResult
                        : HRESULT_FROM_WIN32(ERROR_INVALID_DATA),
                    expectedRevision,
                    observedRevision);
            }
            auto success = GuardResult(
                ReconciliationEvidenceGuardStatus::EvidenceComplete,
                S_OK,
                expectedRevision,
                observedRevision);
            success.finalIdentity = finalIdentity;
            return success;
        }
    }

    ReconciliationEvidenceGuard::~ReconciliationEvidenceGuard()
    {
        Reset();
    }

    ReconciliationEvidenceGuard::ReconciliationEvidenceGuard(
        ReconciliationEvidenceGuard&& other) noexcept
        : roots_(std::move(other.roots_)),
          sessionId_(std::move(other.sessionId_)),
          workingPath_(std::move(other.workingPath_)),
          finalPath_(std::move(other.finalPath_)),
          finalResolvedPath_(std::move(other.finalResolvedPath_)),
          maintenanceLease_(std::move(other.maintenanceLease_)),
          manifestTransaction_(std::move(other.manifestTransaction_)),
          mediaRootHandle_(std::exchange(
              other.mediaRootHandle_, INVALID_HANDLE_VALUE)),
          finalHandle_(std::exchange(
              other.finalHandle_, INVALID_HANDLE_VALUE)),
          mediaRootIdentity_(other.mediaRootIdentity_),
          finalIdentity_(other.finalIdentity_),
          evidenceComplete_(other.evidenceComplete_)
    {
        other.evidenceComplete_ = false;
    }

    ReconciliationEvidenceGuard& ReconciliationEvidenceGuard::operator=(
        ReconciliationEvidenceGuard&& other) noexcept
    {
        if (this != &other)
        {
            Reset();
            roots_ = std::move(other.roots_);
            sessionId_ = std::move(other.sessionId_);
            workingPath_ = std::move(other.workingPath_);
            finalPath_ = std::move(other.finalPath_);
            finalResolvedPath_ = std::move(other.finalResolvedPath_);
            maintenanceLease_ = std::move(other.maintenanceLease_);
            manifestTransaction_ = std::move(other.manifestTransaction_);
            mediaRootHandle_ = std::exchange(
                other.mediaRootHandle_, INVALID_HANDLE_VALUE);
            finalHandle_ = std::exchange(
                other.finalHandle_, INVALID_HANDLE_VALUE);
            mediaRootIdentity_ = other.mediaRootIdentity_;
            finalIdentity_ = other.finalIdentity_;
            evidenceComplete_ = other.evidenceComplete_;
            other.evidenceComplete_ = false;
        }
        return *this;
    }

    ReconciliationEvidenceGuardResult ReconciliationEvidenceGuard::Acquire(
        const RecordingOutputRootResolution& roots,
        const std::wstring_view canonicalSessionId,
        const std::uint64_t expectedRevision,
        ReconciliationEvidenceGuard& guard) noexcept
    {
        guard.Reset();
        try
        {
            if (!roots.Succeeded() ||
                !IsCanonicalRecordingSessionId(canonicalSessionId) ||
                expectedRevision == 0)
            {
                return GuardResult(
                    ReconciliationEvidenceGuardStatus::PathUnsafe,
                    E_INVALIDARG,
                    expectedRevision);
            }

            ReconciliationEvidenceGuard candidate{};
            candidate.roots_ = roots;
            candidate.sessionId_ = canonicalSessionId;
            const auto lease = candidate.maintenanceLease_.AcquireExisting(
                roots, canonicalSessionId);
            if (!lease.Acquired())
            {
                return GuardResult(
                    MapLeaseFailure(lease.status),
                    lease.diagnosticHResult,
                    expectedRevision);
            }

            SessionManifestStore store(
                roots.mediaOutputRoot,
                candidate.sessionId_);
            const auto transaction = store.BeginExpectedRevisionTransaction(
                expectedRevision,
                candidate.manifestTransaction_);
            if (!transaction.Ready())
            {
                return GuardResult(
                    MapTransactionFailure(transaction),
                    transaction.diagnosticHResult,
                    expectedRevision,
                    transaction.observedRevision);
            }
            const auto& manifest =
                candidate.manifestTransaction_.CurrentManifest();
            const auto observedRevision = manifest.revision;
            if (manifest.schemaVersion != SessionManifestSchemaVersion ||
                manifest.state != SessionManifestState::ReadyToPublish ||
                manifest.sessionId != candidate.sessionId_)
            {
                return GuardResult(
                    ReconciliationEvidenceGuardStatus::ManifestNotEligible,
                    HRESULT_FROM_WIN32(ERROR_INVALID_STATE),
                    expectedRevision,
                    observedRevision);
            }
            if (!manifest.workingFileIdentity.attempted ||
                !manifest.workingFileIdentity.captured)
            {
                return GuardResult(
                    ReconciliationEvidenceGuardStatus::IdentityMissing,
                    HRESULT_FROM_WIN32(ERROR_NOT_FOUND),
                    expectedRevision,
                    observedRevision);
            }

            candidate.workingPath_ = manifest.workingPath;
            candidate.finalPath_ = manifest.plannedFinalPath;
            const auto rootSafety = InspectPathForReadOnly(
                roots.mediaOutputRoot,
                roots.mediaOutputRoot,
                PathSafetyExpectedType::Directory);
            if (!rootSafety.SafeForReadOnlyInspection())
            {
                return GuardResult(
                    rootSafety.outcome == PathSafetyOutcome::Inaccessible
                        ? ReconciliationEvidenceGuardStatus::PathInaccessible
                        : ReconciliationEvidenceGuardStatus::PathUnsafe,
                    rootSafety.diagnosticHResult,
                    expectedRevision,
                    observedRevision);
            }
            SECURITY_ATTRIBUTES security{};
            security.nLength = sizeof(security);
            security.bInheritHandle = FALSE;
            candidate.mediaRootHandle_ = CreateFileW(
                roots.mediaOutputRoot.c_str(),
                FILE_READ_ATTRIBUTES,
                FILE_SHARE_READ,
                &security,
                OPEN_EXISTING,
                FILE_FLAG_BACKUP_SEMANTICS | FILE_FLAG_OPEN_REPARSE_POINT,
                nullptr);
            if (!ValidHandle(candidate.mediaRootHandle_))
            {
                return GuardResult(
                    ReconciliationEvidenceGuardStatus::PathInaccessible,
                    HRESULT_FROM_WIN32(GetLastError()),
                    expectedRevision,
                    observedRevision);
            }
            auto identityResult = ReadPersistentFileIdentity(
                candidate.mediaRootHandle_, candidate.mediaRootIdentity_);
            const auto stableRootSafety = InspectPathForReadOnly(
                roots.mediaOutputRoot,
                roots.mediaOutputRoot,
                PathSafetyExpectedType::Directory);
            if (FAILED(identityResult) ||
                !stableRootSafety.SafeForReadOnlyInspection() ||
                !SamePersistentFileIdentity(
                    candidate.mediaRootIdentity_,
                    stableRootSafety.candidateIdentity))
            {
                return GuardResult(
                    ReconciliationEvidenceGuardStatus::PathUnsafe,
                    FAILED(identityResult)
                        ? identityResult
                        : E_ACCESSDENIED,
                    expectedRevision,
                    observedRevision);
            }

            const auto firstWorkingAbsence = WorkingAbsenceResult(
                roots,
                candidate.workingPath_,
                candidate.mediaRootHandle_,
                expectedRevision,
                observedRevision);
            if (!firstWorkingAbsence.EvidenceComplete())
            {
                return firstWorkingAbsence;
            }

            const auto finalSafety = InspectRecordingMediaPathForReadOnly(
                roots, candidate.finalPath_);
            if (!finalSafety.SafeForReadOnlyInspection())
            {
                const auto status = finalSafety.outcome == PathSafetyOutcome::Absent
                    ? ReconciliationEvidenceGuardStatus::FinalMissing
                    : finalSafety.outcome == PathSafetyOutcome::Inaccessible
                        ? ReconciliationEvidenceGuardStatus::PathInaccessible
                        : finalSafety.outcome == PathSafetyOutcome::TypeMismatch
                            ? ReconciliationEvidenceGuardStatus::FinalUnsafe
                            : ReconciliationEvidenceGuardStatus::PathUnsafe;
                return GuardResult(
                    status,
                    finalSafety.diagnosticHResult,
                    expectedRevision,
                    observedRevision);
            }
            candidate.finalHandle_ = CreateFileW(
                candidate.finalPath_.c_str(),
                FILE_READ_ATTRIBUTES | FILE_READ_DATA,
                FILE_SHARE_READ,
                &security,
                OPEN_EXISTING,
                FILE_FLAG_OPEN_REPARSE_POINT,
                nullptr);
            if (!ValidHandle(candidate.finalHandle_))
            {
                const auto error = GetLastError();
                return GuardResult(
                    error == ERROR_FILE_NOT_FOUND || error == ERROR_PATH_NOT_FOUND
                        ? ReconciliationEvidenceGuardStatus::FinalMissing
                        : error == ERROR_ACCESS_DENIED ||
                                error == ERROR_SHARING_VIOLATION
                            ? ReconciliationEvidenceGuardStatus::PathInaccessible
                            : ReconciliationEvidenceGuardStatus::IoFailure,
                    HRESULT_FROM_WIN32(error),
                    expectedRevision,
                    observedRevision);
            }
            auto heldResult = ValidateHeldRegularFile(
                roots,
                candidate.finalPath_,
                candidate.finalHandle_,
                candidate.finalIdentity_,
                candidate.finalResolvedPath_,
                expectedRevision,
                observedRevision);
            if (!heldResult.EvidenceComplete()) return heldResult;

            PersistentFileIdentity persisted{};
            if (!ParseVolumeIdentityCanonical(
                    manifest.workingFileIdentity.volumeIdentity,
                    persisted.volumeSerialNumber) ||
                !ParseFileIdCanonical(
                    manifest.workingFileIdentity.fileId,
                    persisted.fileId))
            {
                return GuardResult(
                    ReconciliationEvidenceGuardStatus::IdentityMissing,
                    HRESULT_FROM_WIN32(ERROR_INVALID_DATA),
                    expectedRevision,
                    observedRevision);
            }
            persisted.available = true;
            if (!SamePersistentFileIdentity(
                    persisted, candidate.finalIdentity_))
            {
                return GuardResult(
                    ReconciliationEvidenceGuardStatus::IdentityMismatch,
                    HRESULT_FROM_WIN32(ERROR_FILE_INVALID),
                    expectedRevision,
                    observedRevision);
            }
            if (candidate.finalIdentity_.hardLinkCount != 1)
            {
                return GuardResult(
                    ReconciliationEvidenceGuardStatus::HardLinkAmbiguous,
                    HRESULT_FROM_WIN32(ERROR_TOO_MANY_LINKS),
                    expectedRevision,
                    observedRevision);
            }

            candidate.evidenceComplete_ = true;
            auto success = GuardResult(
                ReconciliationEvidenceGuardStatus::EvidenceComplete,
                S_OK,
                expectedRevision,
                observedRevision);
            success.finalIdentity = candidate.finalIdentity_;
            guard = std::move(candidate);
            return success;
        }
        catch (const std::bad_alloc&)
        {
            return GuardResult(
                ReconciliationEvidenceGuardStatus::IoFailure,
                E_OUTOFMEMORY,
                expectedRevision);
        }
        catch (...)
        {
            return GuardResult(
                ReconciliationEvidenceGuardStatus::Unknown,
                E_UNEXPECTED,
                expectedRevision);
        }
    }

    bool ReconciliationEvidenceGuard::EvidenceHeld() const noexcept
    {
        return evidenceComplete_ && maintenanceLease_.Acquired() &&
            manifestTransaction_.Active() &&
            ValidHandle(mediaRootHandle_) && ValidHandle(finalHandle_);
    }

    bool ReconciliationEvidenceGuard::FinalHandleHeld() const noexcept
    {
        return ValidHandle(finalHandle_);
    }

    const SessionManifest&
        ReconciliationEvidenceGuard::CurrentManifest() const noexcept
    {
        return manifestTransaction_.CurrentManifest();
    }

    const PersistentFileIdentity&
        ReconciliationEvidenceGuard::FinalIdentity() const noexcept
    {
        return finalIdentity_;
    }

    const std::filesystem::path&
        ReconciliationEvidenceGuard::ConfirmedFinalPath() const noexcept
    {
        return finalPath_;
    }

    ReconciliationEvidenceCommitResult
        ReconciliationEvidenceGuard::CompareExchange(
            SessionManifest& manifest) noexcept
    {
        return CompareExchangeImpl(manifest, false);
    }

    ReconciliationEvidenceCommitResult
        ReconciliationEvidenceGuard::
            CompareExchangeNarrowReconciliation(
                SessionManifest& manifest) noexcept
    {
        return CompareExchangeImpl(manifest, true);
    }

    ReconciliationEvidenceCommitResult
        ReconciliationEvidenceGuard::CompareExchangeImpl(
            SessionManifest& manifest,
            const bool narrowReconciliation) noexcept
    {
        ReconciliationEvidenceCommitResult result{};
        if (!EvidenceHeld())
        {
            result.status = ReconciliationEvidenceGuardStatus::Unknown;
            result.diagnosticHResult = HRESULT_FROM_WIN32(ERROR_INVALID_STATE);
            return result;
        }
        try
        {
            PersistentFileIdentity currentFinalIdentity{};
            std::wstring currentResolvedPath;
            const auto heldResult = ValidateHeldRegularFile(
                roots_,
                finalPath_,
                finalHandle_,
                currentFinalIdentity,
                currentResolvedPath,
                manifestTransaction_.ExpectedRevision(),
                manifestTransaction_.CurrentManifest().revision);
            if (!heldResult.EvidenceComplete() ||
                !SamePersistentFileIdentity(
                    currentFinalIdentity, finalIdentity_) ||
                currentFinalIdentity.fileSizeBytes !=
                    finalIdentity_.fileSizeBytes ||
                currentResolvedPath != finalResolvedPath_ ||
                !currentFinalIdentity.fileSizeBytes.has_value() ||
                *currentFinalIdentity.fileSizeBytes == 0)
            {
                result.status =
                    ReconciliationEvidenceGuardStatus::ConcurrentChange;
                result.diagnosticHResult = !heldResult.EvidenceComplete()
                    ? heldResult.diagnosticHResult
                    : HRESULT_FROM_WIN32(ERROR_FILE_INVALID);
                Reset();
                return result;
            }
            if (currentFinalIdentity.hardLinkCount != 1)
            {
                result.status =
                    ReconciliationEvidenceGuardStatus::HardLinkAmbiguous;
                result.diagnosticHResult =
                    HRESULT_FROM_WIN32(ERROR_TOO_MANY_LINKS);
                Reset();
                return result;
            }
            const auto working = WorkingAbsenceResult(
                roots_,
                workingPath_,
                mediaRootHandle_,
                manifestTransaction_.ExpectedRevision(),
                manifestTransaction_.CurrentManifest().revision);
            if (!working.EvidenceComplete())
            {
                result.status = working.status;
                result.diagnosticHResult = working.diagnosticHResult;
                Reset();
                return result;
            }

            result.manifestCompareExchange = narrowReconciliation
                ? manifestTransaction_.
                    CompareExchangeNarrowReconciliation(manifest)
                : manifestTransaction_.CompareExchange(manifest);
            result.committed = result.manifestCompareExchange.Succeeded();
            result.status = result.committed
                ? ReconciliationEvidenceGuardStatus::EvidenceComplete
                : MapTransactionFailure(result.manifestCompareExchange);
            result.diagnosticHResult =
                result.manifestCompareExchange.diagnosticHResult;
            Reset();
            return result;
        }
        catch (const std::bad_alloc&)
        {
            result.status = ReconciliationEvidenceGuardStatus::IoFailure;
            result.diagnosticHResult = E_OUTOFMEMORY;
            Reset();
            return result;
        }
        catch (...)
        {
            result.status = ReconciliationEvidenceGuardStatus::Unknown;
            result.diagnosticHResult = E_UNEXPECTED;
            Reset();
            return result;
        }
    }

    void ReconciliationEvidenceGuard::Reset() noexcept
    {
        evidenceComplete_ = false;
        // The write transaction is released first after the final CAS/no-op;
        // held filesystem objects then release before the lifetime lease.
        manifestTransaction_.Reset();
        if (ValidHandle(finalHandle_)) (void)CloseHandle(finalHandle_);
        finalHandle_ = INVALID_HANDLE_VALUE;
        if (ValidHandle(mediaRootHandle_)) (void)CloseHandle(mediaRootHandle_);
        mediaRootHandle_ = INVALID_HANDLE_VALUE;
        maintenanceLease_.Release();
        roots_ = {};
        sessionId_.clear();
        workingPath_.clear();
        finalPath_.clear();
        finalResolvedPath_.clear();
        mediaRootIdentity_ = {};
        finalIdentity_ = {};
    }
}
