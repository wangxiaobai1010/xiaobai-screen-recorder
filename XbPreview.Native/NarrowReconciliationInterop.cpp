#include "NarrowReconciliationInterop.h"

#include "NarrowReconciler.h"
#include "RecordingOutputRoot.h"
#include "RecordingSessionIdentity.h"

#include <filesystem>
#include <string_view>

namespace xbpreview::interop
{
    namespace
    {
        std::int32_t MapStatus(
            const NarrowReconciliationStatus value) noexcept
        {
            switch (value)
            {
            case NarrowReconciliationStatus::Reconciled:
                return XbNarrowReconciliationStatusV1_Reconciled;
            case NarrowReconciliationStatus::AlreadyReconciled:
                return XbNarrowReconciliationStatusV1_AlreadyReconciled;
            case NarrowReconciliationStatus::NotEligibleState:
                return XbNarrowReconciliationStatusV1_NotEligibleState;
            case NarrowReconciliationStatus::InvalidSourceFacts:
                return XbNarrowReconciliationStatusV1_InvalidSourceFacts;
            case NarrowReconciliationStatus::SemanticConflict:
                return XbNarrowReconciliationStatusV1_SemanticConflict;
            case NarrowReconciliationStatus::GuardRejected:
                return XbNarrowReconciliationStatusV1_GuardRejected;
            case NarrowReconciliationStatus::RevisionChanged:
                return XbNarrowReconciliationStatusV1_RevisionChanged;
            case NarrowReconciliationStatus::ConcurrentChange:
                return XbNarrowReconciliationStatusV1_ConcurrentChange;
            case NarrowReconciliationStatus::ImmutableFieldViolation:
                return XbNarrowReconciliationStatusV1_ImmutableFieldViolation;
            case NarrowReconciliationStatus::UnsupportedSchema:
                return XbNarrowReconciliationStatusV1_UnsupportedSchema;
            case NarrowReconciliationStatus::EvidenceInsufficient:
                return XbNarrowReconciliationStatusV1_EvidenceInsufficient;
            case NarrowReconciliationStatus::CasFailed:
                return XbNarrowReconciliationStatusV1_CasFailed;
            case NarrowReconciliationStatus::IoFailure:
                return XbNarrowReconciliationStatusV1_IoFailure;
            case NarrowReconciliationStatus::Unknown:
                return XbNarrowReconciliationStatusV1_Unknown;
            }
            return XbNarrowReconciliationStatusV1_Unknown;
        }

        std::int32_t MapGuardStatus(
            const ReconciliationEvidenceGuardStatus value) noexcept
        {
            switch (value)
            {
            case ReconciliationEvidenceGuardStatus::EvidenceComplete:
                return XbNarrowReconciliationGuardStatusV1_EvidenceComplete;
            case ReconciliationEvidenceGuardStatus::ActiveOwner:
                return XbNarrowReconciliationGuardStatusV1_ActiveOwner;
            case ReconciliationEvidenceGuardStatus::OwnerEvidenceMissing:
                return XbNarrowReconciliationGuardStatusV1_OwnerEvidenceMissing;
            case ReconciliationEvidenceGuardStatus::RevisionMismatch:
                return XbNarrowReconciliationGuardStatusV1_RevisionMismatch;
            case ReconciliationEvidenceGuardStatus::ManifestNotEligible:
                return XbNarrowReconciliationGuardStatusV1_ManifestNotEligible;
            case ReconciliationEvidenceGuardStatus::ManifestUnsupported:
                return XbNarrowReconciliationGuardStatusV1_ManifestUnsupported;
            case ReconciliationEvidenceGuardStatus::PathUnsafe:
                return XbNarrowReconciliationGuardStatusV1_PathUnsafe;
            case ReconciliationEvidenceGuardStatus::PathInaccessible:
                return XbNarrowReconciliationGuardStatusV1_PathInaccessible;
            case ReconciliationEvidenceGuardStatus::WorkingStillPresent:
                return XbNarrowReconciliationGuardStatusV1_WorkingStillPresent;
            case ReconciliationEvidenceGuardStatus::WorkingAbsenceUnproven:
                return XbNarrowReconciliationGuardStatusV1_WorkingAbsenceUnproven;
            case ReconciliationEvidenceGuardStatus::FinalMissing:
                return XbNarrowReconciliationGuardStatusV1_FinalMissing;
            case ReconciliationEvidenceGuardStatus::FinalUnsafe:
                return XbNarrowReconciliationGuardStatusV1_FinalUnsafe;
            case ReconciliationEvidenceGuardStatus::IdentityMissing:
                return XbNarrowReconciliationGuardStatusV1_IdentityMissing;
            case ReconciliationEvidenceGuardStatus::IdentityMismatch:
                return XbNarrowReconciliationGuardStatusV1_IdentityMismatch;
            case ReconciliationEvidenceGuardStatus::HardLinkAmbiguous:
                return XbNarrowReconciliationGuardStatusV1_HardLinkAmbiguous;
            case ReconciliationEvidenceGuardStatus::ConcurrentChange:
                return XbNarrowReconciliationGuardStatusV1_ConcurrentChange;
            case ReconciliationEvidenceGuardStatus::IoFailure:
                return XbNarrowReconciliationGuardStatusV1_IoFailure;
            case ReconciliationEvidenceGuardStatus::Unknown:
                return XbNarrowReconciliationGuardStatusV1_Unknown;
            }
            return XbNarrowReconciliationGuardStatusV1_Unknown;
        }

        std::int32_t MapCasStatus(
            const SessionManifestCompareExchangeStatus value) noexcept
        {
            switch (value)
            {
            case SessionManifestCompareExchangeStatus::Ready:
                return XbNarrowReconciliationCasStatusV1_Ready;
            case SessionManifestCompareExchangeStatus::Succeeded:
                return XbNarrowReconciliationCasStatusV1_Succeeded;
            case SessionManifestCompareExchangeStatus::RevisionMismatch:
                return XbNarrowReconciliationCasStatusV1_RevisionMismatch;
            case SessionManifestCompareExchangeStatus::NotFound:
                return XbNarrowReconciliationCasStatusV1_NotFound;
            case SessionManifestCompareExchangeStatus::Inaccessible:
                return XbNarrowReconciliationCasStatusV1_Inaccessible;
            case SessionManifestCompareExchangeStatus::UnsupportedSchema:
                return XbNarrowReconciliationCasStatusV1_UnsupportedSchema;
            case SessionManifestCompareExchangeStatus::MalformedManifest:
                return XbNarrowReconciliationCasStatusV1_MalformedManifest;
            case SessionManifestCompareExchangeStatus::SemanticInvalid:
                return XbNarrowReconciliationCasStatusV1_SemanticInvalid;
            case SessionManifestCompareExchangeStatus::ConcurrentChange:
                return XbNarrowReconciliationCasStatusV1_ConcurrentChange;
            case SessionManifestCompareExchangeStatus::AtomicWriteFailure:
                return XbNarrowReconciliationCasStatusV1_AtomicWriteFailure;
            case SessionManifestCompareExchangeStatus::IoFailure:
                return XbNarrowReconciliationCasStatusV1_IoFailure;
            case SessionManifestCompareExchangeStatus::InvalidInput:
                return XbNarrowReconciliationCasStatusV1_InvalidInput;
            case SessionManifestCompareExchangeStatus::Inactive:
                return XbNarrowReconciliationCasStatusV1_Inactive;
            }
            return XbNarrowReconciliationCasStatusV1_Inactive;
        }

        void FillResult(
            const NarrowReconciliationResult& source,
            XbNarrowReconciliationResultV1& destination) noexcept
        {
            destination = {};
            destination.structSize = sizeof(destination);
            destination.abiVersion =
                XB_NARROW_RECONCILIATION_ABI_VERSION_V1;
            destination.status = MapStatus(source.status);
            destination.diagnosticHResult = source.diagnosticHResult;
            destination.expectedRevision = source.expectedRevision;
            if (source.observedRevision.has_value())
            {
                destination.observedRevision = *source.observedRevision;
                destination.observedRevisionAvailable = 1;
            }
            if (source.guardStatus.has_value())
            {
                destination.guardStatus = MapGuardStatus(*source.guardStatus);
                destination.guardStatusAvailable = 1;
            }
            if (source.casStatus.has_value())
            {
                destination.casStatus = MapCasStatus(*source.casStatus);
                destination.casStatusAvailable = 1;
            }
        }

        XbPreviewResult ReconcileNarrowSessionWithRoots(
            const RecordingOutputRootResolution& roots,
            const wchar_t* const canonicalSessionId,
            const std::uint64_t expectedRevision,
            XbNarrowReconciliationResultV1* const result)
        {
            if (!roots.Succeeded())
            {
                return roots.status == RecordingOutputRootStatus::InvalidInput
                    ? XbPreviewResult_InvalidArgument
                    : XbPreviewResult_NativeFailure;
            }

            const auto reconciliation = ReconcileNarrowSession(
                roots,
                std::wstring_view(canonicalSessionId),
                expectedRevision);
            FillResult(reconciliation, *result);
            return XbPreviewResult_Ok;
        }
    }

    XbPreviewResult GetNarrowReconciliationAbiLayoutV1(
        XbNarrowReconciliationAbiLayoutV1* const layout)
    {
        if (layout == nullptr)
        {
            return XbPreviewResult_InvalidArgument;
        }
        if (layout->structSize !=
                sizeof(XbNarrowReconciliationAbiLayoutV1) ||
            layout->abiVersion !=
                XB_NARROW_RECONCILIATION_ABI_VERSION_V1 ||
            layout->reserved0 != 0)
        {
            return XbPreviewResult_AbiMismatch;
        }

        *layout = {};
        layout->structSize = sizeof(*layout);
        layout->abiVersion = XB_NARROW_RECONCILIATION_ABI_VERSION_V1;
        layout->pointerSize = sizeof(void*);
        layout->packing = 8;
        layout->wcharSize = sizeof(wchar_t);
        layout->optionsSize = sizeof(XbNarrowReconciliationOptionsV1);
        layout->resultSize = sizeof(XbNarrowReconciliationResultV1);
        return XbPreviewResult_Ok;
    }

    XbPreviewResult ReconcileNarrowSessionV1(
        const XbNarrowReconciliationOptionsV1* const options,
        XbNarrowReconciliationResultV1* const result)
    {
        if (options == nullptr || result == nullptr)
        {
            return XbPreviewResult_InvalidArgument;
        }
        if (options->structSize !=
                sizeof(XbNarrowReconciliationOptionsV1) ||
            options->abiVersion !=
                XB_NARROW_RECONCILIATION_ABI_VERSION_V1 ||
            result->structSize != sizeof(XbNarrowReconciliationResultV1) ||
            result->abiVersion !=
                XB_NARROW_RECONCILIATION_ABI_VERSION_V1)
        {
            return XbPreviewResult_AbiMismatch;
        }
        if (options->diagnosticLogDirectory == nullptr ||
            options->diagnosticLogDirectory[0] == L'\0' ||
            options->canonicalSessionId == nullptr ||
            !IsCanonicalRecordingSessionId(options->canonicalSessionId) ||
            options->expectedRevision == 0 ||
            options->reserved0 != 0 || options->reserved1 != 0 ||
            result->reserved0 != 0 || result->reserved1 != 0)
        {
            return XbPreviewResult_InvalidArgument;
        }

        const auto roots = ResolveRecordingOutputRoots(
            std::filesystem::path(options->diagnosticLogDirectory));
        return ReconcileNarrowSessionWithRoots(
            roots,
            options->canonicalSessionId,
            options->expectedRevision,
            result);
    }

    XbPreviewResult ReconcileNarrowSessionForOutputRootV1(
        const XbNarrowReconciliationOutputRootOptionsV1* const options,
        XbNarrowReconciliationResultV1* const result)
    {
        if (options == nullptr || result == nullptr)
        {
            return XbPreviewResult_InvalidArgument;
        }
        if (options->structSize !=
                sizeof(XbNarrowReconciliationOutputRootOptionsV1) ||
            options->abiVersion !=
                XB_NARROW_RECONCILIATION_ABI_VERSION_V1 ||
            result->structSize != sizeof(XbNarrowReconciliationResultV1) ||
            result->abiVersion !=
                XB_NARROW_RECONCILIATION_ABI_VERSION_V1)
        {
            return XbPreviewResult_AbiMismatch;
        }
        if (options->mediaOutputRoot == nullptr ||
            options->mediaOutputRoot[0] == L'\0' ||
            options->canonicalSessionId == nullptr ||
            !IsCanonicalRecordingSessionId(options->canonicalSessionId) ||
            options->expectedRevision == 0 ||
            options->reserved0 != 0 || options->reserved1 != 0 ||
            result->reserved0 != 0 || result->reserved1 != 0)
        {
            return XbPreviewResult_InvalidArgument;
        }

        const auto roots = ResolveRecordingOutputRootsFromManagedRoot(
            std::filesystem::path(options->mediaOutputRoot));
        return ReconcileNarrowSessionWithRoots(
            roots,
            options->canonicalSessionId,
            options->expectedRevision,
            result);
    }
}
