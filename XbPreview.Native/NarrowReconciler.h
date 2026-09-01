#pragma once

#include "RecordingOutputRoot.h"
#include "ReconciliationEvidenceGuard.h"
#include "SessionManifest.h"

#include <windows.h>

#include <cstdint>
#include <filesystem>
#include <optional>
#include <string>
#include <string_view>

namespace xbpreview
{
    enum class NarrowReconciliationSourceStatus
    {
        Eligible,
        NotEligibleState,
        InvalidSourceFacts,
        SemanticConflict
    };

    struct NarrowReconciliationSourceResult final
    {
        NarrowReconciliationSourceStatus status{
            NarrowReconciliationSourceStatus::SemanticConflict };
        HRESULT diagnosticHResult{ E_UNEXPECTED };

        [[nodiscard]] bool Eligible() const noexcept
        {
            return status == NarrowReconciliationSourceStatus::Eligible &&
                SUCCEEDED(diagnosticHResult);
        }
    };

    enum class NarrowReconciliationMutationValidationStatus
    {
        Valid,
        NotEligibleState,
        InvalidSourceFacts,
        SemanticConflict,
        ImmutableFieldViolation
    };

    struct NarrowReconciliationMutationValidationResult final
    {
        NarrowReconciliationMutationValidationStatus status{
            NarrowReconciliationMutationValidationStatus::SemanticConflict };
        HRESULT diagnosticHResult{ E_UNEXPECTED };

        [[nodiscard]] bool Valid() const noexcept
        {
            return status ==
                    NarrowReconciliationMutationValidationStatus::Valid &&
                SUCCEEDED(diagnosticHResult);
        }
    };

    enum class NarrowReconciliationStatus
    {
        Reconciled,
        AlreadyReconciled,
        NotEligibleState,
        InvalidSourceFacts,
        SemanticConflict,
        GuardRejected,
        RevisionChanged,
        ConcurrentChange,
        ImmutableFieldViolation,
        UnsupportedSchema,
        EvidenceInsufficient,
        CasFailed,
        IoFailure,
        Unknown
    };

    struct NarrowReconciliationResult final
    {
        NarrowReconciliationStatus status{
            NarrowReconciliationStatus::Unknown };
        HRESULT diagnosticHResult{ E_UNEXPECTED };
        std::uint64_t expectedRevision{};
        std::optional<std::uint64_t> observedRevision;
        std::optional<ReconciliationEvidenceGuardStatus> guardStatus;
        std::optional<SessionManifestCompareExchangeStatus> casStatus;

        [[nodiscard]] bool Reconciled() const noexcept
        {
            return status == NarrowReconciliationStatus::Reconciled &&
                SUCCEEDED(diagnosticHResult);
        }
    };

    // Internal deterministic scheduling observation only. The callback runs
    // after the initial structured read and before any Guard is acquired. It
    // cannot provide evidence, bypass validation, or authorize mutation.
    struct NarrowReconciliationExecutionHooks final
    {
        void (*afterInitialRead)(void* context) noexcept{};
        void* context{};
    };

    [[nodiscard]] NarrowReconciliationSourceResult
        EvaluateNarrowReconciliationSource(
            const SessionManifest& source) noexcept;

    [[nodiscard]] HRESULT BuildNarrowReconciliationTarget(
        const SessionManifest& source,
        const std::filesystem::path& confirmedFinalPath,
        const std::wstring& nowUtc,
        SessionManifest& target) noexcept;

    [[nodiscard]] NarrowReconciliationMutationValidationResult
        ValidateNarrowReconciliationMutation(
            const SessionManifest& source,
            const SessionManifest& target) noexcept;

    [[nodiscard]] bool NarrowReconciliationTargetsSemanticallyEquivalent(
        const SessionManifest& left,
        const SessionManifest& right) noexcept;

    [[nodiscard]] bool NarrowReconciliationTargetMatchesSource(
        const SessionManifest& target,
        const SessionManifest& source) noexcept;

    [[nodiscard]] NarrowReconciliationResult ReconcileNarrowSession(
        const RecordingOutputRootResolution& roots,
        std::wstring_view canonicalSessionId,
        std::uint64_t expectedRevision,
        const NarrowReconciliationExecutionHooks* hooks = nullptr) noexcept;
}
