#include "HistoricalSessionScanInterop.h"

#include "RecordingOutputRoot.h"
#include "SessionInspection.h"

#include <algorithm>
#include <cwchar>
#include <filesystem>
#include <limits>
#include <memory>
#include <utility>
#include <vector>

namespace xbpreview::interop
{
    namespace
    {
        struct HistoricalSessionStrings final
        {
            std::wstring sessionId;
            std::wstring workingCandidatePath;
            std::wstring plannedFinalCandidatePath;
            std::wstring publishedCandidatePath;
            std::wstring sessionDirectory;
            std::wstring manifestPath;
        };

        struct HistoricalSessionScanData final
        {
            explicit HistoricalSessionScanData(SessionScanResult&& value)
                : scan(std::move(value))
            {
                mediaOutputRoot = scan.roots.mediaOutputRoot.native();
                sessionsRoot = scan.roots.sessionsRoot.native();
                sessionStrings.reserve(scan.sessions.size());
                for (const auto& session : scan.sessions)
                {
                    HistoricalSessionStrings strings{};
                    strings.sessionId = session.sessionId;
                    strings.workingCandidatePath =
                        session.working.candidatePath.native();
                    strings.plannedFinalCandidatePath =
                        session.plannedFinal.candidatePath.native();
                    strings.publishedCandidatePath =
                        session.published.candidatePath.native();
                    strings.sessionDirectory =
                        session.sessionDirectory.native();
                    strings.manifestPath = session.manifestPath.native();
                    sessionStrings.push_back(std::move(strings));
                }
            }

            SessionScanResult scan;
            std::wstring mediaOutputRoot;
            std::wstring sessionsRoot;
            std::vector<HistoricalSessionStrings> sessionStrings;
        };

        HistoricalSessionScanData* FromHandle(
            const XbHistoricalSessionScanHandle handle) noexcept
        {
            return static_cast<HistoricalSessionScanData*>(handle);
        }

        std::int32_t MapScanStatus(const SessionScanStatus value) noexcept
        {
            switch (value)
            {
            case SessionScanStatus::Success:
                return XbHistoricalSessionScanStatusV1_Success;
            case SessionScanStatus::SessionsRootAbsent:
                return XbHistoricalSessionScanStatusV1_SessionsRootAbsent;
            case SessionScanStatus::SessionsRootInaccessible:
                return XbHistoricalSessionScanStatusV1_SessionsRootInaccessible;
            case SessionScanStatus::SessionsRootUnsafe:
                return XbHistoricalSessionScanStatusV1_SessionsRootUnsafe;
            case SessionScanStatus::IoFailure:
                return XbHistoricalSessionScanStatusV1_IoFailure;
            case SessionScanStatus::PartialTruncated:
                return XbHistoricalSessionScanStatusV1_PartialTruncated;
            }
            return XbHistoricalSessionScanStatusV1_IoFailure;
        }

        std::int32_t MapClassification(
            const SessionClassification value) noexcept
        {
            switch (value)
            {
            case SessionClassification::CompletedConsistent:
                return XbHistoricalSessionClassificationV1_CompletedConsistent;
            case SessionClassification::ReconciledCompletedConsistent:
                return XbHistoricalSessionClassificationV1_ReconciledCompletedConsistent;
            case SessionClassification::PublishedMetadataNeedsReconciliation:
                return XbHistoricalSessionClassificationV1_PublishedMetadataNeedsReconciliation;
            case SessionClassification::PublishOutcomeUnprovenRetain:
                return XbHistoricalSessionClassificationV1_PublishOutcomeUnprovenRetain;
            case SessionClassification::ReadyToPublishWorkingPreserved:
                return XbHistoricalSessionClassificationV1_ReadyToPublishWorkingPreserved;
            case SessionClassification::IncompleteWithWorkingMedia:
                return XbHistoricalSessionClassificationV1_IncompleteWithWorkingMedia;
            case SessionClassification::IncompleteNoMediaRetain:
                return XbHistoricalSessionClassificationV1_IncompleteNoMediaRetain;
            case SessionClassification::PublishFailedWorkingPreserved:
                return XbHistoricalSessionClassificationV1_PublishFailedWorkingPreserved;
            case SessionClassification::FinalizeOrValidationFailedWorkingPreserved:
                return XbHistoricalSessionClassificationV1_FinalizeOrValidationFailedWorkingPreserved;
            case SessionClassification::ManifestCorrupt:
                return XbHistoricalSessionClassificationV1_ManifestCorrupt;
            case SessionClassification::ManifestMissing:
                return XbHistoricalSessionClassificationV1_ManifestMissing;
            case SessionClassification::FilesystemConflict:
                return XbHistoricalSessionClassificationV1_FilesystemConflict;
            case SessionClassification::UnknownRetain:
                return XbHistoricalSessionClassificationV1_UnknownRetain;
            case SessionClassification::UserCancelled:
                return XbHistoricalSessionClassificationV1_UserCancelled;
            }
            return XbHistoricalSessionClassificationV1_UnknownRetain;
        }

        std::int32_t MapSeverity(
            const SessionInspectionSeverity value) noexcept
        {
            switch (value)
            {
            case SessionInspectionSeverity::Info:
                return XbHistoricalSessionSeverityV1_Info;
            case SessionInspectionSeverity::Attention:
                return XbHistoricalSessionSeverityV1_Attention;
            case SessionInspectionSeverity::RecoveryCandidate:
                return XbHistoricalSessionSeverityV1_RecoveryCandidate;
            case SessionInspectionSeverity::CriticalRetain:
                return XbHistoricalSessionSeverityV1_CriticalRetain;
            }
            return XbHistoricalSessionSeverityV1_CriticalRetain;
        }

        std::uint64_t MapReasons(const SessionInspectionReason value) noexcept
        {
            std::uint64_t result{};
#define XB_MAP_REASON(name) \
            if (HasSessionInspectionReason( \
                    value, SessionInspectionReason::name)) \
            { \
                result |= XbHistoricalSessionReasonV1_##name; \
            }
            XB_MAP_REASON(FinalMissing)
            XB_MAP_REASON(WorkingAndFinalBothPresent)
            XB_MAP_REASON(PathOutsideRoot)
            XB_MAP_REASON(ReparsePoint)
            XB_MAP_REASON(IdentityMismatch)
            XB_MAP_REASON(ManifestIoError)
            XB_MAP_REASON(UnsupportedSchema)
            XB_MAP_REASON(LiveOwnerUnknown)
            XB_MAP_REASON(NoMediaProven)
            XB_MAP_REASON(MediaSubmitted)
            XB_MAP_REASON(FinalizeFailed)
            XB_MAP_REASON(ValidationFailed)
            XB_MAP_REASON(PublishFailed)
            XB_MAP_REASON(PublishIdentityUnavailable)
            XB_MAP_REASON(InventoryIncomplete)
            XB_MAP_REASON(ManifestMissing)
            XB_MAP_REASON(ManifestMalformed)
            XB_MAP_REASON(PathInaccessible)
            XB_MAP_REASON(TypeMismatch)
            XB_MAP_REASON(ConcurrentChange)
            XB_MAP_REASON(UnknownState)
            XB_MAP_REASON(LiveOwnerActive)
            XB_MAP_REASON(LifetimeOwnerEvidenceMissing)
#undef XB_MAP_REASON
            return result;
        }

        std::int32_t MapParseStatus(
            const SessionManifestParseStatus value) noexcept
        {
            switch (value)
            {
            case SessionManifestParseStatus::Valid:
                return XbHistoricalSessionParseStatusV1_Valid;
            case SessionManifestParseStatus::NotFound:
                return XbHistoricalSessionParseStatusV1_NotFound;
            case SessionManifestParseStatus::Inaccessible:
                return XbHistoricalSessionParseStatusV1_Inaccessible;
            case SessionManifestParseStatus::MalformedJson:
                return XbHistoricalSessionParseStatusV1_MalformedJson;
            case SessionManifestParseStatus::UnsupportedSchema:
                return XbHistoricalSessionParseStatusV1_UnsupportedSchema;
            case SessionManifestParseStatus::SemanticInvalid:
                return XbHistoricalSessionParseStatusV1_SemanticInvalid;
            case SessionManifestParseStatus::UnknownOrFutureState:
                return XbHistoricalSessionParseStatusV1_UnknownOrFutureState;
            case SessionManifestParseStatus::IoFailure:
                return XbHistoricalSessionParseStatusV1_IoFailure;
            }
            return XbHistoricalSessionParseStatusV1_IoFailure;
        }

        std::int32_t MapSemanticIssue(
            const SessionManifestSemanticIssue value) noexcept
        {
            switch (value)
            {
            case SessionManifestSemanticIssue::None:
                return XbHistoricalSessionSemanticIssueV1_None;
            case SessionManifestSemanticIssue::SessionIdentityMismatch:
                return XbHistoricalSessionSemanticIssueV1_SessionIdentityMismatch;
            case SessionManifestSemanticIssue::PathPolicyViolation:
                return XbHistoricalSessionSemanticIssueV1_PathPolicyViolation;
            case SessionManifestSemanticIssue::PublishedPathMismatch:
                return XbHistoricalSessionSemanticIssueV1_PublishedPathMismatch;
            case SessionManifestSemanticIssue::Other:
                return XbHistoricalSessionSemanticIssueV1_Other;
            }
            return XbHistoricalSessionSemanticIssueV1_Other;
        }

        std::int32_t MapManifestState(
            const SessionManifestState value) noexcept
        {
            switch (value)
            {
            case SessionManifestState::Created:
                return XbHistoricalSessionManifestStateV1_Created;
            case SessionManifestState::Starting:
                return XbHistoricalSessionManifestStateV1_Starting;
            case SessionManifestState::Recording:
                return XbHistoricalSessionManifestStateV1_Recording;
            case SessionManifestState::Stopping:
                return XbHistoricalSessionManifestStateV1_Stopping;
            case SessionManifestState::ReadyToPublish:
                return XbHistoricalSessionManifestStateV1_ReadyToPublish;
            case SessionManifestState::Published:
                return XbHistoricalSessionManifestStateV1_Published;
            case SessionManifestState::Completed:
                return XbHistoricalSessionManifestStateV1_Completed;
            case SessionManifestState::Failed:
                return XbHistoricalSessionManifestStateV1_Failed;
            case SessionManifestState::Unknown:
                return XbHistoricalSessionManifestStateV1_Unknown;
            case SessionManifestState::ReconciledCompleted:
                return XbHistoricalSessionManifestStateV1_ReconciledCompleted;
            case SessionManifestState::UserCancelled:
                return XbHistoricalSessionManifestStateV1_UserCancelled;
            }
            return XbHistoricalSessionManifestStateV1_Unknown;
        }

        std::int32_t MapOwnerState(
            const SessionLifetimeOwnerProbeState value) noexcept
        {
            switch (value)
            {
            case SessionLifetimeOwnerProbeState::ActiveOwned:
                return XbHistoricalSessionOwnerStateV1_ActiveOwned;
            case SessionLifetimeOwnerProbeState::InactiveLeaseReleased:
                return XbHistoricalSessionOwnerStateV1_InactiveLeaseReleased;
            case SessionLifetimeOwnerProbeState::EvidenceMissing:
                return XbHistoricalSessionOwnerStateV1_EvidenceMissing;
            case SessionLifetimeOwnerProbeState::UnsafePath:
                return XbHistoricalSessionOwnerStateV1_UnsafePath;
            case SessionLifetimeOwnerProbeState::Inaccessible:
                return XbHistoricalSessionOwnerStateV1_Inaccessible;
            case SessionLifetimeOwnerProbeState::IoFailure:
                return XbHistoricalSessionOwnerStateV1_IoFailure;
            case SessionLifetimeOwnerProbeState::Unknown:
                return XbHistoricalSessionOwnerStateV1_Unknown;
            }
            return XbHistoricalSessionOwnerStateV1_Unknown;
        }

        std::int32_t MapFilesystemState(
            const InspectedFilesystemState value) noexcept
        {
            switch (value)
            {
            case InspectedFilesystemState::NotProvided:
                return XbHistoricalSessionFilesystemStateV1_NotProvided;
            case InspectedFilesystemState::Exists:
                return XbHistoricalSessionFilesystemStateV1_Exists;
            case InspectedFilesystemState::Absent:
                return XbHistoricalSessionFilesystemStateV1_Absent;
            case InspectedFilesystemState::ParentAbsent:
                return XbHistoricalSessionFilesystemStateV1_ParentAbsent;
            case InspectedFilesystemState::Inaccessible:
                return XbHistoricalSessionFilesystemStateV1_Inaccessible;
            case InspectedFilesystemState::OutsideTrustedRoot:
                return XbHistoricalSessionFilesystemStateV1_OutsideTrustedRoot;
            case InspectedFilesystemState::ReparseEncountered:
                return XbHistoricalSessionFilesystemStateV1_ReparseEncountered;
            case InspectedFilesystemState::Invalid:
                return XbHistoricalSessionFilesystemStateV1_Invalid;
            case InspectedFilesystemState::TypeMismatch:
                return XbHistoricalSessionFilesystemStateV1_TypeMismatch;
            case InspectedFilesystemState::IoFailure:
                return XbHistoricalSessionFilesystemStateV1_IoFailure;
            case InspectedFilesystemState::Unknown:
                return XbHistoricalSessionFilesystemStateV1_Unknown;
            }
            return XbHistoricalSessionFilesystemStateV1_Unknown;
        }

        std::uint32_t Bool32(const bool value) noexcept
        {
            return value ? 1u : 0u;
        }

        void FillSummary(
            const SessionScanResult& scan,
            XbHistoricalSessionScanSummaryV1& value) noexcept
        {
            value = {};
            value.structSize = sizeof(value);
            value.abiVersion =
                XB_HISTORICAL_SESSION_SCAN_ABI_VERSION_V1;
            value.status = MapScanStatus(scan.status);
            value.diagnosticHResult = scan.diagnosticHResult;
            value.sessionCount =
                static_cast<std::uint32_t>(scan.sessions.size());
            value.unrecognizedEntryCount = static_cast<std::uint32_t>(
                scan.unrecognizedEntries.size());
            value.entriesObserved =
                static_cast<std::uint64_t>(scan.entriesObserved);
            value.maximumEntries =
                static_cast<std::uint64_t>(scan.maximumEntries);
            value.truncated = Bool32(scan.truncated);
            value.mediaWithoutSessionDirectoryBlindSpot = Bool32(
                scan.mediaWithoutSessionDirectoryBlindSpot);
        }

        void FillPathFacts(
            const InspectedPathFacts& facts,
            std::int32_t& state,
            std::int32_t& hresult,
            std::uint32_t& sizeAvailable,
            std::uint64_t& size) noexcept
        {
            state = MapFilesystemState(facts.state);
            hresult = facts.safety.diagnosticHResult;
            sizeAvailable = Bool32(facts.size.has_value());
            size = facts.size.value_or(0);
        }

        void FillItem(
            const SessionInspectionResult& source,
            XbHistoricalSessionItemV1& value) noexcept
        {
            value = {};
            value.structSize = sizeof(value);
            value.abiVersion =
                XB_HISTORICAL_SESSION_SCAN_ABI_VERSION_V1;
            value.classification = MapClassification(source.classification);
            value.severity = MapSeverity(source.severity);
            value.reasons = MapReasons(source.reasons);
            value.manifestParseStatus =
                MapParseStatus(source.manifestParse.status);
            value.manifestParseHResult =
                source.manifestParse.diagnosticHResult;
            value.manifestSemanticIssue =
                MapSemanticIssue(source.manifestParse.semanticIssue);
            value.manifestState = XbHistoricalSessionManifestStateV1_Unknown;
            value.observedSchemaVersionAvailable = Bool32(
                source.manifestParse.observedSchemaVersion.has_value());
            value.observedSchemaVersion =
                source.manifestParse.observedSchemaVersion.value_or(0);
            value.observedRevisionAvailable = Bool32(
                source.observedRevision.has_value());
            value.observedRevision = source.observedRevision.value_or(0);
            value.manifestAvailable = Bool32(source.manifest.has_value());
            value.manifestRevisionStable = Bool32(
                source.manifestRevisionStable);
            if (source.manifest.has_value())
            {
                value.manifestState = MapManifestState(source.manifest->state);
            }
            value.ownerState = MapOwnerState(source.lifetimeOwner.state);
            value.ownerHResult = source.lifetimeOwner.diagnosticHResult;
            FillPathFacts(
                source.working,
                value.workingFilesystemState,
                value.workingHResult,
                value.workingSizeAvailable,
                value.workingSize);
            FillPathFacts(
                source.plannedFinal,
                value.plannedFinalFilesystemState,
                value.plannedFinalHResult,
                value.plannedFinalSizeAvailable,
                value.plannedFinalSize);
            FillPathFacts(
                source.published,
                value.publishedFilesystemState,
                value.publishedHResult,
                value.publishedSizeAvailable,
                value.publishedSize);
            value.persistentWorkingIdentityAvailable = Bool32(
                source.persistentWorkingIdentityAvailable);
            value.persistentIdentityComparisonAttempted = Bool32(
                source.persistentIdentityComparisonAttempted);
            value.strongIdentityMatch = Bool32(source.strongIdentityMatch);
            value.deleteAllowed = Bool32(source.deleteAllowed);
            value.reconciliationAuthorized = Bool32(
                source.reconciliationAuthorized);
        }

        XbPreviewResult CopyUtf16(
            const std::wstring& source,
            wchar_t* const buffer,
            const std::uint32_t bufferLength,
            std::uint32_t* const requiredLength) noexcept
        {
            if (requiredLength == nullptr ||
                (buffer == nullptr && bufferLength != 0))
            {
                return XbPreviewResult_InvalidArgument;
            }
            if (source.size() >=
                static_cast<std::size_t>(
                    (std::numeric_limits<std::uint32_t>::max)()))
            {
                return XbPreviewResult_NativeFailure;
            }

            const auto required =
                static_cast<std::uint32_t>(source.size() + 1);
            *requiredLength = required;
            if (buffer == nullptr)
            {
                return XbPreviewResult_Ok;
            }
            if (bufferLength < required)
            {
                if (bufferLength != 0)
                {
                    buffer[0] = L'\0';
                }
                return XbPreviewResult_InsufficientBuffer;
            }

            if (!source.empty())
            {
                std::wmemcpy(buffer, source.data(), source.size());
            }
            buffer[source.size()] = L'\0';
            return XbPreviewResult_Ok;
        }

        XbPreviewResult BeginHistoricalSessionScanWithRoots(
            const RecordingOutputRootResolution& roots,
            const std::uint32_t maximumEntries,
            XbHistoricalSessionScanHandle* const scanHandle,
            XbHistoricalSessionScanSummaryV1* const summary)
        {
            if (!roots.Succeeded())
            {
                return roots.status == RecordingOutputRootStatus::InvalidInput
                    ? XbPreviewResult_InvalidArgument
                    : XbPreviewResult_NativeFailure;
            }

            SessionInspectionOptions scanOptions{};
            scanOptions.maximumEntries = maximumEntries;
            auto scan = ScanHistoricalRecordingSessions(roots, scanOptions);
            if (scan.sessions.size() >
                    (std::numeric_limits<std::uint32_t>::max)() ||
                scan.unrecognizedEntries.size() >
                    (std::numeric_limits<std::uint32_t>::max)())
            {
                return XbPreviewResult_NativeFailure;
            }

            auto data = std::make_unique<HistoricalSessionScanData>(
                std::move(scan));
            XbHistoricalSessionScanSummaryV1 result{};
            FillSummary(data->scan, result);
            *summary = result;
            *scanHandle = data.release();
            return XbPreviewResult_Ok;
        }
    }

    XbPreviewResult GetHistoricalSessionScanAbiLayoutV1(
        XbHistoricalSessionScanAbiLayoutV1* const layout)
    {
        if (layout == nullptr)
        {
            return XbPreviewResult_InvalidArgument;
        }
        if (layout->structSize != sizeof(XbHistoricalSessionScanAbiLayoutV1) ||
            layout->abiVersion !=
                XB_HISTORICAL_SESSION_SCAN_ABI_VERSION_V1)
        {
            return XbPreviewResult_AbiMismatch;
        }

        *layout = {};
        layout->structSize = sizeof(*layout);
        layout->abiVersion = XB_HISTORICAL_SESSION_SCAN_ABI_VERSION_V1;
        layout->pointerSize = sizeof(void*);
        layout->packing = 8;
        layout->wcharSize = sizeof(wchar_t);
        layout->optionsSize = sizeof(XbHistoricalSessionScanOptionsV1);
        layout->summarySize = sizeof(XbHistoricalSessionScanSummaryV1);
        layout->itemSize = sizeof(XbHistoricalSessionItemV1);
        return XbPreviewResult_Ok;
    }

    XbPreviewResult BeginHistoricalSessionScanV1(
        const XbHistoricalSessionScanOptionsV1* const options,
        XbHistoricalSessionScanHandle* const scanHandle,
        XbHistoricalSessionScanSummaryV1* const summary)
    {
        if (scanHandle == nullptr)
        {
            return XbPreviewResult_InvalidArgument;
        }
        *scanHandle = nullptr;
        if (options == nullptr || summary == nullptr)
        {
            return XbPreviewResult_InvalidArgument;
        }
        if (options->structSize != sizeof(XbHistoricalSessionScanOptionsV1) ||
            options->abiVersion !=
                XB_HISTORICAL_SESSION_SCAN_ABI_VERSION_V1 ||
            summary->structSize != sizeof(XbHistoricalSessionScanSummaryV1) ||
            summary->abiVersion !=
                XB_HISTORICAL_SESSION_SCAN_ABI_VERSION_V1)
        {
            return XbPreviewResult_AbiMismatch;
        }
        if (options->diagnosticLogDirectory == nullptr ||
            options->diagnosticLogDirectory[0] == L'\0' ||
            options->maximumEntries == 0 ||
            options->maximumEntries >
                XB_HISTORICAL_SESSION_SCAN_MAX_ENTRIES_V1 ||
            options->reserved0 != 0 || options->reserved1 != 0 ||
            options->reserved2 != 0)
        {
            return XbPreviewResult_InvalidArgument;
        }

        const auto roots = ResolveRecordingOutputRoots(
            std::filesystem::path(options->diagnosticLogDirectory));
        return BeginHistoricalSessionScanWithRoots(
            roots, options->maximumEntries, scanHandle, summary);
    }

    XbPreviewResult BeginHistoricalSessionScanForOutputRootV1(
        const XbHistoricalSessionScanOutputRootOptionsV1* const options,
        XbHistoricalSessionScanHandle* const scanHandle,
        XbHistoricalSessionScanSummaryV1* const summary)
    {
        if (scanHandle == nullptr)
        {
            return XbPreviewResult_InvalidArgument;
        }
        *scanHandle = nullptr;
        if (options == nullptr || summary == nullptr)
        {
            return XbPreviewResult_InvalidArgument;
        }
        if (options->structSize !=
                sizeof(XbHistoricalSessionScanOutputRootOptionsV1) ||
            options->abiVersion !=
                XB_HISTORICAL_SESSION_SCAN_ABI_VERSION_V1 ||
            summary->structSize != sizeof(XbHistoricalSessionScanSummaryV1) ||
            summary->abiVersion !=
                XB_HISTORICAL_SESSION_SCAN_ABI_VERSION_V1)
        {
            return XbPreviewResult_AbiMismatch;
        }
        if (options->mediaOutputRoot == nullptr ||
            options->mediaOutputRoot[0] == L'\0' ||
            options->maximumEntries == 0 ||
            options->maximumEntries >
                XB_HISTORICAL_SESSION_SCAN_MAX_ENTRIES_V1 ||
            options->reserved0 != 0 || options->reserved1 != 0 ||
            options->reserved2 != 0)
        {
            return XbPreviewResult_InvalidArgument;
        }

        const auto roots = ResolveRecordingOutputRootsFromManagedRoot(
            std::filesystem::path(options->mediaOutputRoot));
        return BeginHistoricalSessionScanWithRoots(
            roots, options->maximumEntries, scanHandle, summary);
    }

    XbPreviewResult GetHistoricalSessionV1(
        const XbHistoricalSessionScanHandle scanHandle,
        const std::uint32_t index,
        XbHistoricalSessionItemV1* const item)
    {
        const auto data = FromHandle(scanHandle);
        if (data == nullptr)
        {
            return XbPreviewResult_InvalidHandle;
        }
        if (item == nullptr)
        {
            return XbPreviewResult_InvalidArgument;
        }
        if (item->structSize != sizeof(XbHistoricalSessionItemV1) ||
            item->abiVersion !=
                XB_HISTORICAL_SESSION_SCAN_ABI_VERSION_V1)
        {
            return XbPreviewResult_AbiMismatch;
        }
        if (index >= data->scan.sessions.size())
        {
            return XbPreviewResult_InvalidArgument;
        }

        XbHistoricalSessionItemV1 result{};
        FillItem(data->scan.sessions[index], result);
        *item = result;
        return XbPreviewResult_Ok;
    }

    XbPreviewResult GetHistoricalSessionScanStringV1(
        const XbHistoricalSessionScanHandle scanHandle,
        const XbHistoricalSessionScanStringFieldV1 field,
        wchar_t* const buffer,
        const std::uint32_t bufferLength,
        std::uint32_t* const requiredLength)
    {
        const auto data = FromHandle(scanHandle);
        if (data == nullptr)
        {
            return XbPreviewResult_InvalidHandle;
        }

        switch (field)
        {
        case XbHistoricalSessionScanStringFieldV1_MediaOutputRoot:
            return CopyUtf16(
                data->mediaOutputRoot,
                buffer,
                bufferLength,
                requiredLength);
        case XbHistoricalSessionScanStringFieldV1_SessionsRoot:
            return CopyUtf16(
                data->sessionsRoot,
                buffer,
                bufferLength,
                requiredLength);
        }
        return XbPreviewResult_InvalidArgument;
    }

    XbPreviewResult GetHistoricalSessionStringV1(
        const XbHistoricalSessionScanHandle scanHandle,
        const std::uint32_t index,
        const XbHistoricalSessionStringFieldV1 field,
        wchar_t* const buffer,
        const std::uint32_t bufferLength,
        std::uint32_t* const requiredLength)
    {
        const auto data = FromHandle(scanHandle);
        if (data == nullptr)
        {
            return XbPreviewResult_InvalidHandle;
        }
        if (index >= data->sessionStrings.size())
        {
            return XbPreviewResult_InvalidArgument;
        }
        const auto& strings = data->sessionStrings[index];
        switch (field)
        {
        case XbHistoricalSessionStringFieldV1_SessionId:
            return CopyUtf16(
                strings.sessionId, buffer, bufferLength, requiredLength);
        case XbHistoricalSessionStringFieldV1_WorkingCandidatePath:
            return CopyUtf16(
                strings.workingCandidatePath,
                buffer,
                bufferLength,
                requiredLength);
        case XbHistoricalSessionStringFieldV1_PlannedFinalCandidatePath:
            return CopyUtf16(
                strings.plannedFinalCandidatePath,
                buffer,
                bufferLength,
                requiredLength);
        case XbHistoricalSessionStringFieldV1_PublishedCandidatePath:
            return CopyUtf16(
                strings.publishedCandidatePath,
                buffer,
                bufferLength,
                requiredLength);
        case XbHistoricalSessionStringFieldV1_SessionDirectory:
            return CopyUtf16(
                strings.sessionDirectory,
                buffer,
                bufferLength,
                requiredLength);
        case XbHistoricalSessionStringFieldV1_ManifestPath:
            return CopyUtf16(
                strings.manifestPath,
                buffer,
                bufferLength,
                requiredLength);
        }
        return XbPreviewResult_InvalidArgument;
    }

    XbPreviewResult DestroyHistoricalSessionScanV1(
        XbHistoricalSessionScanHandle* const scanHandle) noexcept
    {
        if (scanHandle == nullptr)
        {
            return XbPreviewResult_InvalidArgument;
        }
        const auto data = FromHandle(*scanHandle);
        *scanHandle = nullptr;
        delete data;
        return XbPreviewResult_Ok;
    }
}
