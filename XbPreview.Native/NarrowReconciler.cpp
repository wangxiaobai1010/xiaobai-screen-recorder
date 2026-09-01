#include "NarrowReconciler.h"

#include "PersistentFileIdentity.h"
#include "RecordingSessionIdentity.h"

#include <array>
#include <cstdio>
#include <limits>
#include <new>
#include <system_error>
#include <utility>

namespace xbpreview
{
    namespace
    {
        constexpr std::uint64_t MaximumExactJsonInteger =
            9'007'199'254'740'991ull;

        NarrowReconciliationSourceResult SourceResult(
            const NarrowReconciliationSourceStatus status,
            const HRESULT hresult) noexcept
        {
            return { status, hresult };
        }

        NarrowReconciliationMutationValidationResult MutationResult(
            const NarrowReconciliationMutationValidationStatus status,
            const HRESULT hresult) noexcept
        {
            return { status, hresult };
        }

        NarrowReconciliationResult ReconciliationResult(
            const NarrowReconciliationStatus status,
            const HRESULT hresult,
            const std::uint64_t expectedRevision,
            const std::optional<std::uint64_t> observedRevision =
                std::nullopt) noexcept
        {
            NarrowReconciliationResult result{};
            result.status = status;
            result.diagnosticHResult = hresult;
            result.expectedRevision = expectedRevision;
            result.observedRevision = observedRevision;
            return result;
        }

        bool OperationFactsEqual(
            const SessionManifestOperationFacts& left,
            const SessionManifestOperationFacts& right) noexcept
        {
            return left.attempted == right.attempted &&
                left.hresult == right.hresult;
        }

        bool ImmutableFieldsEqual(
            const SessionManifest& left,
            const SessionManifest& right) noexcept
        {
            return left.writerStrategy == right.writerStrategy &&
                left.sessionId == right.sessionId &&
                left.createdAtUtc == right.createdAtUtc &&
                left.workingPath == right.workingPath &&
                left.plannedFinalPath == right.plannedFinalPath &&
                left.publishedPath == right.publishedPath &&
                left.workingFileOwnedBySession ==
                    right.workingFileOwnedBySession &&
                left.writeSampleAttempted == right.writeSampleAttempted &&
                left.frameSubmitted == right.frameSubmitted &&
                left.workerExited == right.workerExited &&
                left.recordingResourcesReleased ==
                    right.recordingResourcesReleased &&
                left.residualOutstanding == right.residualOutstanding &&
                OperationFactsEqual(left.finalize, right.finalize) &&
                left.finalize.count == right.finalize.count &&
                OperationFactsEqual(left.validation, right.validation) &&
                left.validation.passed == right.validation.passed &&
                OperationFactsEqual(left.publish, right.publish) &&
                left.publish.published == right.publish.published &&
                left.workingFileIdentity.attempted ==
                    right.workingFileIdentity.attempted &&
                left.workingFileIdentity.captured ==
                    right.workingFileIdentity.captured &&
                left.workingFileIdentity.volumeIdentity ==
                    right.workingFileIdentity.volumeIdentity &&
                left.workingFileIdentity.fileId ==
                    right.workingFileIdentity.fileId &&
                left.workingFileIdentity.hresult ==
                    right.workingFileIdentity.hresult &&
                left.postPublishIdentityVerification.attempted ==
                    right.postPublishIdentityVerification.attempted &&
                left.postPublishIdentityVerification.matched ==
                    right.postPublishIdentityVerification.matched &&
                left.postPublishIdentityVerification.hresult ==
                    right.postPublishIdentityVerification.hresult &&
                left.errorCategory == right.errorCategory &&
                left.errorCode == right.errorCode &&
                left.errorMessage == right.errorMessage;
        }

        bool ReconciliationFactsDefault(
            const SessionManifestReconciliationFacts& value) noexcept
        {
            return !value.reconciled &&
                value.kind == SessionManifestReconciliationKind::None &&
                value.sourceRevision == 0 &&
                value.reconciledAtUtc.empty() &&
                value.evidenceKind ==
                    SessionManifestReconciliationEvidenceKind::None &&
                !value.originalPublishResultKnown &&
                value.confirmedFinalPath.empty();
        }

        bool ReconciliationTargetShapeValid(
            const SessionManifest& target) noexcept
        {
            return target.schemaVersion ==
                    SessionManifestReconciledSchemaVersion &&
                target.state ==
                    SessionManifestState::ReconciledCompleted &&
                target.reconciliation.reconciled &&
                target.reconciliation.kind ==
                    SessionManifestReconciliationKind::
                        FinalAtPlannedPathSamePersistentFileV1 &&
                target.reconciliation.sourceRevision > 0 &&
                target.reconciliation.sourceRevision <
                    MaximumExactJsonInteger &&
                target.revision ==
                    target.reconciliation.sourceRevision + 1 &&
                !target.updatedAtUtc.empty() &&
                target.updatedAtUtc ==
                    target.reconciliation.reconciledAtUtc &&
                target.reconciliation.evidenceKind ==
                    SessionManifestReconciliationEvidenceKind::
                        MaintenanceLeaseCasHeldFinalIdentityV1 &&
                !target.reconciliation.originalPublishResultKnown &&
                !target.reconciliation.confirmedFinalPath.empty() &&
                target.reconciliation.confirmedFinalPath ==
                    target.plannedFinalPath &&
                !target.publish.attempted &&
                !target.publish.published &&
                !target.publish.hresult.has_value() &&
                target.publishedPath.empty();
        }

        std::wstring CaptureUtcNowText()
        {
            FILETIME fileTime{};
            GetSystemTimePreciseAsFileTime(&fileTime);
            SYSTEMTIME systemTime{};
            if (!FileTimeToSystemTime(&fileTime, &systemTime))
            {
                throw std::system_error(
                    static_cast<int>(GetLastError()),
                    std::system_category());
            }
            ULARGE_INTEGER ticks{};
            ticks.LowPart = fileTime.dwLowDateTime;
            ticks.HighPart = fileTime.dwHighDateTime;
            const auto fraction = static_cast<unsigned long>(
                ticks.QuadPart % 10'000'000ull);
            wchar_t buffer[64]{};
            swprintf_s(
                buffer,
                L"%04hu-%02hu-%02huT%02hu:%02hu:%02hu.%07luZ",
                systemTime.wYear,
                systemTime.wMonth,
                systemTime.wDay,
                systemTime.wHour,
                systemTime.wMinute,
                systemTime.wSecond,
                fraction);
            return buffer;
        }

        NarrowReconciliationStatus StatusFromSource(
            const NarrowReconciliationSourceStatus status) noexcept
        {
            switch (status)
            {
            case NarrowReconciliationSourceStatus::NotEligibleState:
                return NarrowReconciliationStatus::NotEligibleState;
            case NarrowReconciliationSourceStatus::InvalidSourceFacts:
                return NarrowReconciliationStatus::InvalidSourceFacts;
            case NarrowReconciliationSourceStatus::SemanticConflict:
                return NarrowReconciliationStatus::SemanticConflict;
            default:
                return NarrowReconciliationStatus::Unknown;
            }
        }

        NarrowReconciliationResult MapParseFailure(
            const SessionManifestParseResult& parsed,
            const std::uint64_t expectedRevision) noexcept
        {
            switch (parsed.status)
            {
            case SessionManifestParseStatus::UnsupportedSchema:
            case SessionManifestParseStatus::UnknownOrFutureState:
                return ReconciliationResult(
                    NarrowReconciliationStatus::UnsupportedSchema,
                    parsed.diagnosticHResult,
                    expectedRevision);
            case SessionManifestParseStatus::MalformedJson:
            case SessionManifestParseStatus::SemanticInvalid:
                return ReconciliationResult(
                    NarrowReconciliationStatus::SemanticConflict,
                    parsed.diagnosticHResult,
                    expectedRevision);
            case SessionManifestParseStatus::NotFound:
                return ReconciliationResult(
                    NarrowReconciliationStatus::EvidenceInsufficient,
                    parsed.diagnosticHResult,
                    expectedRevision);
            case SessionManifestParseStatus::Inaccessible:
            case SessionManifestParseStatus::IoFailure:
                return ReconciliationResult(
                    NarrowReconciliationStatus::IoFailure,
                    parsed.diagnosticHResult,
                    expectedRevision);
            default:
                return ReconciliationResult(
                    NarrowReconciliationStatus::Unknown,
                    E_UNEXPECTED,
                    expectedRevision);
            }
        }

        NarrowReconciliationResult MapGuardFailure(
            const ReconciliationEvidenceGuardResult& guard) noexcept
        {
            NarrowReconciliationStatus status{};
            switch (guard.status)
            {
            case ReconciliationEvidenceGuardStatus::RevisionMismatch:
            case ReconciliationEvidenceGuardStatus::ConcurrentChange:
                status = NarrowReconciliationStatus::RevisionChanged;
                break;
            case ReconciliationEvidenceGuardStatus::ActiveOwner:
                status = NarrowReconciliationStatus::GuardRejected;
                break;
            case ReconciliationEvidenceGuardStatus::IoFailure:
            case ReconciliationEvidenceGuardStatus::Unknown:
                status = NarrowReconciliationStatus::IoFailure;
                break;
            default:
                status = NarrowReconciliationStatus::EvidenceInsufficient;
                break;
            }
            auto result = ReconciliationResult(
                status,
                guard.diagnosticHResult,
                guard.expectedRevision,
                guard.observedRevision);
            result.guardStatus = guard.status;
            return result;
        }

        NarrowReconciliationResult ReloadAfterRevisionChange(
            const SessionManifestStore& store,
            const SessionManifest& source,
            const std::uint64_t expectedRevision,
            const HRESULT fallbackHResult) noexcept
        {
            SessionManifest current{};
            const auto parsed = store.ParseManifest(current);
            if (parsed.status != SessionManifestParseStatus::Valid)
            {
                return MapParseFailure(parsed, expectedRevision);
            }
            if (NarrowReconciliationTargetMatchesSource(current, source))
            {
                return ReconciliationResult(
                    NarrowReconciliationStatus::AlreadyReconciled,
                    S_OK,
                    expectedRevision,
                    current.revision);
            }
            return ReconciliationResult(
                NarrowReconciliationStatus::RevisionChanged,
                fallbackHResult,
                expectedRevision,
                current.revision);
        }
    }

    NarrowReconciliationSourceResult EvaluateNarrowReconciliationSource(
        const SessionManifest& source) noexcept
    {
        try
        {
            if (source.schemaVersion != SessionManifestSchemaVersion ||
                source.state != SessionManifestState::ReadyToPublish)
            {
                return SourceResult(
                    NarrowReconciliationSourceStatus::NotEligibleState,
                    HRESULT_FROM_WIN32(ERROR_INVALID_STATE));
            }
            if (!ReconciliationFactsDefault(source.reconciliation) ||
                source.publish.attempted || source.publish.published ||
                source.publish.hresult.has_value() ||
                !source.publishedPath.empty() ||
                source.postPublishIdentityVerification.attempted ||
                source.postPublishIdentityVerification.matched ||
                source.postPublishIdentityVerification.hresult.has_value() ||
                source.errorCategory != SessionManifestErrorCategory::None ||
                source.errorCode.has_value() ||
                !source.errorMessage.empty())
            {
                return SourceResult(
                    NarrowReconciliationSourceStatus::SemanticConflict,
                    HRESULT_FROM_WIN32(ERROR_INVALID_DATA));
            }
            std::uint64_t volume{};
            std::array<std::uint8_t, 16> fileId{};
            if (!source.workingFileOwnedBySession ||
                !source.writeSampleAttempted || !source.frameSubmitted ||
                !source.workerExited ||
                !source.recordingResourcesReleased ||
                source.residualOutstanding != 0 ||
                !source.finalize.attempted || source.finalize.count != 1 ||
                !source.finalize.hresult.has_value() ||
                *source.finalize.hresult != S_OK ||
                !source.validation.attempted ||
                !source.validation.passed ||
                !source.validation.hresult.has_value() ||
                *source.validation.hresult != S_OK ||
                !source.workingFileIdentity.attempted ||
                !source.workingFileIdentity.captured ||
                !source.workingFileIdentity.hresult.has_value() ||
                *source.workingFileIdentity.hresult != S_OK ||
                !ParseVolumeIdentityCanonical(
                    source.workingFileIdentity.volumeIdentity, volume) ||
                !ParseFileIdCanonical(
                    source.workingFileIdentity.fileId, fileId))
            {
                return SourceResult(
                    NarrowReconciliationSourceStatus::InvalidSourceFacts,
                    HRESULT_FROM_WIN32(ERROR_INVALID_DATA));
            }
            return SourceResult(
                NarrowReconciliationSourceStatus::Eligible,
                S_OK);
        }
        catch (const std::bad_alloc&)
        {
            return SourceResult(
                NarrowReconciliationSourceStatus::InvalidSourceFacts,
                E_OUTOFMEMORY);
        }
        catch (...)
        {
            return SourceResult(
                NarrowReconciliationSourceStatus::SemanticConflict,
                E_UNEXPECTED);
        }
    }

    HRESULT BuildNarrowReconciliationTarget(
        const SessionManifest& source,
        const std::filesystem::path& confirmedFinalPath,
        const std::wstring& nowUtc,
        SessionManifest& target) noexcept
    {
        try
        {
            const auto eligible = EvaluateNarrowReconciliationSource(source);
            if (!eligible.Eligible()) return eligible.diagnosticHResult;
            if (source.revision == 0 ||
                source.revision >= MaximumExactJsonInteger ||
                nowUtc.empty() ||
                confirmedFinalPath.wstring() != source.plannedFinalPath)
            {
                return HRESULT_FROM_WIN32(ERROR_INVALID_DATA);
            }
            auto candidate = source;
            candidate.schemaVersion =
                SessionManifestReconciledSchemaVersion;
            candidate.revision = source.revision + 1;
            candidate.updatedAtUtc = nowUtc;
            candidate.state = SessionManifestState::ReconciledCompleted;
            candidate.reconciliation.reconciled = true;
            candidate.reconciliation.kind =
                SessionManifestReconciliationKind::
                    FinalAtPlannedPathSamePersistentFileV1;
            candidate.reconciliation.sourceRevision = source.revision;
            candidate.reconciliation.reconciledAtUtc = nowUtc;
            candidate.reconciliation.evidenceKind =
                SessionManifestReconciliationEvidenceKind::
                    MaintenanceLeaseCasHeldFinalIdentityV1;
            candidate.reconciliation.originalPublishResultKnown = false;
            candidate.reconciliation.confirmedFinalPath =
                confirmedFinalPath.wstring();
            const auto validation = ValidateNarrowReconciliationMutation(
                source, candidate);
            if (!validation.Valid()) return validation.diagnosticHResult;
            target = std::move(candidate);
            return S_OK;
        }
        catch (const std::bad_alloc&)
        {
            return E_OUTOFMEMORY;
        }
        catch (...)
        {
            return E_UNEXPECTED;
        }
    }

    NarrowReconciliationMutationValidationResult
        ValidateNarrowReconciliationMutation(
            const SessionManifest& source,
            const SessionManifest& target) noexcept
    {
        const auto sourceResult = EvaluateNarrowReconciliationSource(source);
        if (!sourceResult.Eligible())
        {
            const auto status = sourceResult.status ==
                    NarrowReconciliationSourceStatus::NotEligibleState
                ? NarrowReconciliationMutationValidationStatus::
                    NotEligibleState
                : sourceResult.status ==
                        NarrowReconciliationSourceStatus::InvalidSourceFacts
                    ? NarrowReconciliationMutationValidationStatus::
                        InvalidSourceFacts
                    : NarrowReconciliationMutationValidationStatus::
                        SemanticConflict;
            return MutationResult(status, sourceResult.diagnosticHResult);
        }
        if (!ReconciliationTargetShapeValid(target) ||
            target.reconciliation.sourceRevision != source.revision ||
            target.updatedAtUtc < source.updatedAtUtc)
        {
            return MutationResult(
                NarrowReconciliationMutationValidationStatus::
                    SemanticConflict,
                HRESULT_FROM_WIN32(ERROR_INVALID_DATA));
        }
        if (!ImmutableFieldsEqual(source, target))
        {
            return MutationResult(
                NarrowReconciliationMutationValidationStatus::
                    ImmutableFieldViolation,
                HRESULT_FROM_WIN32(ERROR_INVALID_DATA));
        }
        return MutationResult(
            NarrowReconciliationMutationValidationStatus::Valid,
            S_OK);
    }

    bool NarrowReconciliationTargetsSemanticallyEquivalent(
        const SessionManifest& left,
        const SessionManifest& right) noexcept
    {
        try
        {
            return ReconciliationTargetShapeValid(left) &&
                ReconciliationTargetShapeValid(right) &&
                left.schemaVersion == right.schemaVersion &&
                left.revision == right.revision &&
                left.state == right.state &&
                left.reconciliation.sourceRevision ==
                    right.reconciliation.sourceRevision &&
                left.reconciliation.kind == right.reconciliation.kind &&
                left.reconciliation.evidenceKind ==
                    right.reconciliation.evidenceKind &&
                left.reconciliation.originalPublishResultKnown ==
                    right.reconciliation.originalPublishResultKnown &&
                left.reconciliation.confirmedFinalPath ==
                    right.reconciliation.confirmedFinalPath &&
                ImmutableFieldsEqual(left, right);
        }
        catch (...)
        {
            return false;
        }
    }

    bool NarrowReconciliationTargetMatchesSource(
        const SessionManifest& target,
        const SessionManifest& source) noexcept
    {
        try
        {
            if (!ReconciliationTargetShapeValid(target) ||
                !EvaluateNarrowReconciliationSource(source).Eligible() ||
                target.reconciliation.sourceRevision != source.revision ||
                target.revision != source.revision + 1)
            {
                return false;
            }
            auto comparison = source;
            comparison.schemaVersion =
                SessionManifestReconciledSchemaVersion;
            comparison.revision = target.revision;
            comparison.updatedAtUtc = target.updatedAtUtc;
            comparison.state = SessionManifestState::ReconciledCompleted;
            comparison.reconciliation = target.reconciliation;
            return NarrowReconciliationTargetsSemanticallyEquivalent(
                target, comparison);
        }
        catch (...)
        {
            return false;
        }
    }

    NarrowReconciliationResult ReconcileNarrowSession(
        const RecordingOutputRootResolution& roots,
        const std::wstring_view canonicalSessionId,
        const std::uint64_t expectedRevision,
        const NarrowReconciliationExecutionHooks* const hooks) noexcept
    {
        try
        {
            if (!roots.Succeeded() ||
                !IsCanonicalRecordingSessionId(canonicalSessionId) ||
                expectedRevision == 0 ||
                expectedRevision >= MaximumExactJsonInteger)
            {
                return ReconciliationResult(
                    NarrowReconciliationStatus::EvidenceInsufficient,
                    E_INVALIDARG,
                    expectedRevision);
            }
            SessionManifestStore store(
                roots.mediaOutputRoot,
                std::wstring(canonicalSessionId));
            SessionManifest initial{};
            const auto parsed = store.ParseManifest(initial);
            if (parsed.status != SessionManifestParseStatus::Valid)
            {
                return MapParseFailure(parsed, expectedRevision);
            }
            if (hooks != nullptr && hooks->afterInitialRead != nullptr)
            {
                hooks->afterInitialRead(hooks->context);
            }
            if (initial.schemaVersion ==
                    SessionManifestReconciledSchemaVersion &&
                initial.state == SessionManifestState::ReconciledCompleted)
            {
                return ReconciliationResult(
                    NarrowReconciliationStatus::AlreadyReconciled,
                    S_OK,
                    expectedRevision,
                    initial.revision);
            }
            if (initial.schemaVersion != SessionManifestSchemaVersion)
            {
                return ReconciliationResult(
                    NarrowReconciliationStatus::UnsupportedSchema,
                    HRESULT_FROM_WIN32(ERROR_NOT_SUPPORTED),
                    expectedRevision,
                    initial.revision);
            }
            if (initial.revision != expectedRevision)
            {
                return ReconciliationResult(
                    NarrowReconciliationStatus::RevisionChanged,
                    HRESULT_FROM_WIN32(ERROR_REVISION_MISMATCH),
                    expectedRevision,
                    initial.revision);
            }

            ReconciliationEvidenceGuard guard;
            const auto acquired = ReconciliationEvidenceGuard::Acquire(
                roots,
                canonicalSessionId,
                expectedRevision,
                guard);
            if (!acquired.EvidenceComplete())
            {
                if (acquired.status ==
                        ReconciliationEvidenceGuardStatus::RevisionMismatch ||
                    acquired.status ==
                        ReconciliationEvidenceGuardStatus::ConcurrentChange)
                {
                    auto result = ReloadAfterRevisionChange(
                        store,
                        initial,
                        expectedRevision,
                        acquired.diagnosticHResult);
                    result.guardStatus = acquired.status;
                    return result;
                }
                return MapGuardFailure(acquired);
            }
            const auto source = guard.CurrentManifest();
            const auto eligibility = EvaluateNarrowReconciliationSource(source);
            if (!eligibility.Eligible())
            {
                guard.Reset();
                return ReconciliationResult(
                    StatusFromSource(eligibility.status),
                    eligibility.diagnosticHResult,
                    expectedRevision,
                    source.revision);
            }

            const auto nowUtc = CaptureUtcNowText();
            SessionManifest target{};
            const auto build = BuildNarrowReconciliationTarget(
                source,
                guard.ConfirmedFinalPath(),
                nowUtc,
                target);
            if (FAILED(build))
            {
                guard.Reset();
                return ReconciliationResult(
                    NarrowReconciliationStatus::SemanticConflict,
                    build,
                    expectedRevision,
                    source.revision);
            }
            const auto whitelist = ValidateNarrowReconciliationMutation(
                source, target);
            if (!whitelist.Valid())
            {
                guard.Reset();
                const auto status = whitelist.status ==
                        NarrowReconciliationMutationValidationStatus::
                            ImmutableFieldViolation
                    ? NarrowReconciliationStatus::ImmutableFieldViolation
                    : whitelist.status ==
                            NarrowReconciliationMutationValidationStatus::
                                InvalidSourceFacts
                        ? NarrowReconciliationStatus::InvalidSourceFacts
                        : NarrowReconciliationStatus::SemanticConflict;
                return ReconciliationResult(
                    status,
                    whitelist.diagnosticHResult,
                    expectedRevision,
                    source.revision);
            }

            const auto committed =
                guard.CompareExchangeNarrowReconciliation(target);
            if (committed.committed)
            {
                auto result = ReconciliationResult(
                    NarrowReconciliationStatus::Reconciled,
                    S_OK,
                    expectedRevision,
                    target.revision);
                result.guardStatus = committed.status;
                result.casStatus =
                    committed.manifestCompareExchange.status;
                return result;
            }
            if (committed.manifestCompareExchange.status ==
                    SessionManifestCompareExchangeStatus::RevisionMismatch ||
                committed.manifestCompareExchange.status ==
                    SessionManifestCompareExchangeStatus::ConcurrentChange)
            {
                auto result = ReloadAfterRevisionChange(
                    store,
                    source,
                    expectedRevision,
                    committed.diagnosticHResult);
                result.guardStatus = committed.status;
                result.casStatus =
                    committed.manifestCompareExchange.status;
                return result;
            }
            const auto failureStatus =
                committed.manifestCompareExchange.status ==
                        SessionManifestCompareExchangeStatus::
                            AtomicWriteFailure
                    ? NarrowReconciliationStatus::CasFailed
                    : committed.status ==
                            ReconciliationEvidenceGuardStatus::IoFailure
                        ? NarrowReconciliationStatus::IoFailure
                        : NarrowReconciliationStatus::CasFailed;
            auto result = ReconciliationResult(
                failureStatus,
                committed.diagnosticHResult,
                expectedRevision,
                committed.manifestCompareExchange.observedRevision);
            result.guardStatus = committed.status;
            result.casStatus = committed.manifestCompareExchange.status;
            return result;
        }
        catch (const std::bad_alloc&)
        {
            return ReconciliationResult(
                NarrowReconciliationStatus::IoFailure,
                E_OUTOFMEMORY,
                expectedRevision);
        }
        catch (...)
        {
            return ReconciliationResult(
                NarrowReconciliationStatus::Unknown,
                E_UNEXPECTED,
                expectedRevision);
        }
    }
}
