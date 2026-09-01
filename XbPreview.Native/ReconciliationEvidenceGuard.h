#pragma once

#include "PersistentFileIdentity.h"
#include "RecordingOutputRoot.h"
#include "SessionLifetimeOwner.h"
#include "SessionManifest.h"

#include <windows.h>

#include <cstdint>
#include <filesystem>
#include <optional>
#include <string>
#include <string_view>

namespace xbpreview
{
    enum class ReconciliationEvidenceGuardStatus
    {
        EvidenceComplete,
        ActiveOwner,
        OwnerEvidenceMissing,
        RevisionMismatch,
        ManifestNotEligible,
        ManifestUnsupported,
        PathUnsafe,
        PathInaccessible,
        WorkingStillPresent,
        WorkingAbsenceUnproven,
        FinalMissing,
        FinalUnsafe,
        IdentityMissing,
        IdentityMismatch,
        HardLinkAmbiguous,
        ConcurrentChange,
        IoFailure,
        Unknown
    };

    struct ReconciliationEvidenceGuardResult final
    {
        ReconciliationEvidenceGuardStatus status{
            ReconciliationEvidenceGuardStatus::Unknown };
        HRESULT diagnosticHResult{ E_UNEXPECTED };
        std::uint64_t expectedRevision{};
        std::optional<std::uint64_t> observedRevision;
        PersistentFileIdentity finalIdentity;

        [[nodiscard]] bool EvidenceComplete() const noexcept
        {
            return status ==
                    ReconciliationEvidenceGuardStatus::EvidenceComplete &&
                SUCCEEDED(diagnosticHResult);
        }
    };

    struct ReconciliationEvidenceCommitResult final
    {
        ReconciliationEvidenceGuardStatus status{
            ReconciliationEvidenceGuardStatus::Unknown };
        HRESULT diagnosticHResult{ E_UNEXPECTED };
        bool committed{};
        SessionManifestCompareExchangeResult manifestCompareExchange;
    };

    // Explicit operation-time evidence layer for future narrow metadata
    // reconciliation. Construction never mutates a Manifest or media file.
    // EvidenceComplete means only that locks/handles currently authorize a
    // later CAS attempt; it never means the Session has been reconciled.
    class ReconciliationEvidenceGuard final
    {
    public:
        ReconciliationEvidenceGuard() noexcept = default;
        ~ReconciliationEvidenceGuard();
        ReconciliationEvidenceGuard(
            const ReconciliationEvidenceGuard&) = delete;
        ReconciliationEvidenceGuard& operator=(
            const ReconciliationEvidenceGuard&) = delete;
        ReconciliationEvidenceGuard(
            ReconciliationEvidenceGuard&& other) noexcept;
        ReconciliationEvidenceGuard& operator=(
            ReconciliationEvidenceGuard&& other) noexcept;

        [[nodiscard]] static ReconciliationEvidenceGuardResult Acquire(
            const RecordingOutputRootResolution& roots,
            std::wstring_view canonicalSessionId,
            std::uint64_t expectedRevision,
            ReconciliationEvidenceGuard& guard) noexcept;

        [[nodiscard]] bool EvidenceHeld() const noexcept;
        [[nodiscard]] bool FinalHandleHeld() const noexcept;
        [[nodiscard]] const SessionManifest& CurrentManifest() const noexcept;
        [[nodiscard]] const PersistentFileIdentity& FinalIdentity() const
            noexcept;
        [[nodiscard]] const std::filesystem::path& ConfirmedFinalPath() const
            noexcept;

        // Intended for controlled fixture tests in 3a-3 and the future 3b
        // narrow metadata operation. It always performs the second Working
        // absence check immediately before the manifest CAS.
        [[nodiscard]] ReconciliationEvidenceCommitResult CompareExchange(
            SessionManifest& manifest) noexcept;

        // Same operation-time revalidation as CompareExchange, followed by
        // the dedicated narrow schema-2 -> schema-3 CAS whitelist.
        [[nodiscard]] ReconciliationEvidenceCommitResult
            CompareExchangeNarrowReconciliation(
                SessionManifest& manifest) noexcept;

        void Reset() noexcept;

    private:
        RecordingOutputRootResolution roots_;
        std::wstring sessionId_;
        std::filesystem::path workingPath_;
        std::filesystem::path finalPath_;
        std::wstring finalResolvedPath_;
        SessionLifetimeOwner maintenanceLease_;
        SessionManifestWriteTransaction manifestTransaction_;
        HANDLE mediaRootHandle_{ INVALID_HANDLE_VALUE };
        HANDLE finalHandle_{ INVALID_HANDLE_VALUE };
        PersistentFileIdentity mediaRootIdentity_;
        PersistentFileIdentity finalIdentity_;
        bool evidenceComplete_{};

        [[nodiscard]] ReconciliationEvidenceCommitResult
            CompareExchangeImpl(
                SessionManifest& manifest,
                bool narrowReconciliation) noexcept;
    };
}
