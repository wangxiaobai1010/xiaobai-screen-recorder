#pragma once

#include <cstddef>
#include <cstdint>
#include <windows.h>

#if defined(XB_PREVIEW_NATIVE_EXPORTS)
#define XB_PREVIEW_API __declspec(dllexport)
#else
#define XB_PREVIEW_API __declspec(dllimport)
#endif

#define XB_PREVIEW_CALL __stdcall
#define XB_PREVIEW_API_VERSION 0x00040005u
#define XB_HISTORICAL_SESSION_SCAN_ABI_VERSION_V1 0x00010001u
#define XB_HISTORICAL_SESSION_SCAN_MAX_ENTRIES_V1 1024u
#define XB_NARROW_RECONCILIATION_ABI_VERSION_V1 0x00010001u
#define XB_AUDIO_CONTROLS_ABI_VERSION_V1 0x00010001u
#define XB_MICROPHONE_DEVICE_ABI_VERSION_V1 0x00010001u
#define XB_GPU_EXPORT_ABI_VERSION_V1 0x00010001u

using XbPreviewHandle = void*;
using XbHistoricalSessionScanHandle = void*;

enum XbPreviewResult : std::int32_t
{
    XbPreviewResult_Ok = 0,
    XbPreviewResult_InvalidArgument = -1,
    XbPreviewResult_InvalidWindow = -2,
    XbPreviewResult_AbiMismatch = -3,
    XbPreviewResult_InvalidHandle = -4,
    XbPreviewResult_InvalidState = -5,
    XbPreviewResult_Timeout = -6,
    XbPreviewResult_WgcUnsupported = -7,
    XbPreviewResult_HdrUnsupported = -8,
    XbPreviewResult_NativeFailure = -9,
    XbPreviewResult_DeviceLost = -10,
    XbPreviewResult_InvalidCameraState = -11,
    XbPreviewResult_StaleCameraState = -12,
    XbPreviewResult_CursorModeUnavailable = -13,
    XbPreviewResult_UnsupportedStructVersion = -14,
    XbPreviewResult_InvalidGeometry = -15,
    XbPreviewResult_StaleRevision = -16,
    XbPreviewResult_RevisionConflict = -17,
    XbPreviewResult_GeometrySourceMismatch = -18,
    XbPreviewResult_InsufficientBuffer = -19,
    XbPreviewResult_WindowTargetClosed = -20,
};

enum XbCaptureTargetKind : std::int32_t
{
    XbCaptureTargetKind_Monitor = 0,
    XbCaptureTargetKind_Window = 1,
};

// Product-facing Window Stage selectors. These values map only to the
// existing frozen 3-direction / 3-level transform table.
enum XbWindowStageOrientation : std::int32_t
{
    XbWindowStageOrientation_Left = 0,
    XbWindowStageOrientation_Front = 1,
    XbWindowStageOrientation_Right = 2,
};

enum XbWindowStageLevel : std::int32_t
{
    XbWindowStageLevel_Level1 = 0,
    XbWindowStageLevel_Level2 = 1,
    XbWindowStageLevel_Level3 = 2,
};

enum XbWindowShowcaseBackgroundPreset : std::int32_t
{
    XbWindowShowcaseBackgroundPreset_Warm = 0,
    XbWindowShowcaseBackgroundPreset_Art01 = 1,
    XbWindowShowcaseBackgroundPreset_Art001 = 2,
};

enum XbAudioProgramMode : std::int32_t
{
    XbAudioProgramMode_None = 0,
    XbAudioProgramMode_SystemOnly = 1,
    XbAudioProgramMode_MicrophoneOnly = 2,
    XbAudioProgramMode_Dual = 3,
};

enum XbAudioEndpointLevelFlagsV1 : std::uint64_t
{
    XbAudioEndpointLevelFlagsV1_SystemSourceEnabled = 1ull << 0,
    XbAudioEndpointLevelFlagsV1_MicrophoneSourceEnabled = 1ull << 1,
    XbAudioEndpointLevelFlagsV1_SystemMeterAvailable = 1ull << 2,
    XbAudioEndpointLevelFlagsV1_MicrophoneMeterAvailable = 1ull << 3,
};

enum XbMicrophoneSelectionKindV1 : std::int32_t
{
    XbMicrophoneSelectionKindV1_WindowsDefault = 0,
    XbMicrophoneSelectionKindV1_ConcreteEndpoint = 1,
};

enum XbHistoricalSessionScanStatusV1 : std::int32_t
{
    XbHistoricalSessionScanStatusV1_Success = 0,
    XbHistoricalSessionScanStatusV1_SessionsRootAbsent = 1,
    XbHistoricalSessionScanStatusV1_SessionsRootInaccessible = 2,
    XbHistoricalSessionScanStatusV1_SessionsRootUnsafe = 3,
    XbHistoricalSessionScanStatusV1_IoFailure = 4,
    XbHistoricalSessionScanStatusV1_PartialTruncated = 5,
};

enum XbHistoricalSessionClassificationV1 : std::int32_t
{
    XbHistoricalSessionClassificationV1_CompletedConsistent = 0,
    XbHistoricalSessionClassificationV1_ReconciledCompletedConsistent = 1,
    XbHistoricalSessionClassificationV1_PublishedMetadataNeedsReconciliation = 2,
    XbHistoricalSessionClassificationV1_PublishOutcomeUnprovenRetain = 3,
    XbHistoricalSessionClassificationV1_ReadyToPublishWorkingPreserved = 4,
    XbHistoricalSessionClassificationV1_IncompleteWithWorkingMedia = 5,
    XbHistoricalSessionClassificationV1_IncompleteNoMediaRetain = 6,
    XbHistoricalSessionClassificationV1_PublishFailedWorkingPreserved = 7,
    XbHistoricalSessionClassificationV1_FinalizeOrValidationFailedWorkingPreserved = 8,
    XbHistoricalSessionClassificationV1_ManifestCorrupt = 9,
    XbHistoricalSessionClassificationV1_ManifestMissing = 10,
    XbHistoricalSessionClassificationV1_FilesystemConflict = 11,
    XbHistoricalSessionClassificationV1_UnknownRetain = 12,
    XbHistoricalSessionClassificationV1_UserCancelled = 13,
};

enum XbHistoricalSessionSeverityV1 : std::int32_t
{
    XbHistoricalSessionSeverityV1_Info = 0,
    XbHistoricalSessionSeverityV1_Attention = 1,
    XbHistoricalSessionSeverityV1_RecoveryCandidate = 2,
    XbHistoricalSessionSeverityV1_CriticalRetain = 3,
};

enum XbHistoricalSessionReasonV1 : std::uint64_t
{
    XbHistoricalSessionReasonV1_None = 0,
    XbHistoricalSessionReasonV1_FinalMissing = 1ull << 0,
    XbHistoricalSessionReasonV1_WorkingAndFinalBothPresent = 1ull << 1,
    XbHistoricalSessionReasonV1_PathOutsideRoot = 1ull << 2,
    XbHistoricalSessionReasonV1_ReparsePoint = 1ull << 3,
    XbHistoricalSessionReasonV1_IdentityMismatch = 1ull << 4,
    XbHistoricalSessionReasonV1_ManifestIoError = 1ull << 5,
    XbHistoricalSessionReasonV1_UnsupportedSchema = 1ull << 6,
    XbHistoricalSessionReasonV1_LiveOwnerUnknown = 1ull << 7,
    XbHistoricalSessionReasonV1_NoMediaProven = 1ull << 8,
    XbHistoricalSessionReasonV1_MediaSubmitted = 1ull << 9,
    XbHistoricalSessionReasonV1_FinalizeFailed = 1ull << 10,
    XbHistoricalSessionReasonV1_ValidationFailed = 1ull << 11,
    XbHistoricalSessionReasonV1_PublishFailed = 1ull << 12,
    XbHistoricalSessionReasonV1_PublishIdentityUnavailable = 1ull << 13,
    XbHistoricalSessionReasonV1_InventoryIncomplete = 1ull << 14,
    XbHistoricalSessionReasonV1_ManifestMissing = 1ull << 15,
    XbHistoricalSessionReasonV1_ManifestMalformed = 1ull << 16,
    XbHistoricalSessionReasonV1_PathInaccessible = 1ull << 17,
    XbHistoricalSessionReasonV1_TypeMismatch = 1ull << 18,
    XbHistoricalSessionReasonV1_ConcurrentChange = 1ull << 19,
    XbHistoricalSessionReasonV1_UnknownState = 1ull << 20,
    XbHistoricalSessionReasonV1_LiveOwnerActive = 1ull << 21,
    XbHistoricalSessionReasonV1_LifetimeOwnerEvidenceMissing = 1ull << 22,
};

enum XbHistoricalSessionParseStatusV1 : std::int32_t
{
    XbHistoricalSessionParseStatusV1_Valid = 0,
    XbHistoricalSessionParseStatusV1_NotFound = 1,
    XbHistoricalSessionParseStatusV1_Inaccessible = 2,
    XbHistoricalSessionParseStatusV1_MalformedJson = 3,
    XbHistoricalSessionParseStatusV1_UnsupportedSchema = 4,
    XbHistoricalSessionParseStatusV1_SemanticInvalid = 5,
    XbHistoricalSessionParseStatusV1_UnknownOrFutureState = 6,
    XbHistoricalSessionParseStatusV1_IoFailure = 7,
};

enum XbHistoricalSessionSemanticIssueV1 : std::int32_t
{
    XbHistoricalSessionSemanticIssueV1_None = 0,
    XbHistoricalSessionSemanticIssueV1_SessionIdentityMismatch = 1,
    XbHistoricalSessionSemanticIssueV1_PathPolicyViolation = 2,
    XbHistoricalSessionSemanticIssueV1_PublishedPathMismatch = 3,
    XbHistoricalSessionSemanticIssueV1_Other = 4,
};

enum XbHistoricalSessionManifestStateV1 : std::int32_t
{
    XbHistoricalSessionManifestStateV1_Created = 0,
    XbHistoricalSessionManifestStateV1_Starting = 1,
    XbHistoricalSessionManifestStateV1_Recording = 2,
    XbHistoricalSessionManifestStateV1_Stopping = 3,
    XbHistoricalSessionManifestStateV1_ReadyToPublish = 4,
    XbHistoricalSessionManifestStateV1_Published = 5,
    XbHistoricalSessionManifestStateV1_Completed = 6,
    XbHistoricalSessionManifestStateV1_Failed = 7,
    XbHistoricalSessionManifestStateV1_Unknown = 8,
    XbHistoricalSessionManifestStateV1_ReconciledCompleted = 9,
    XbHistoricalSessionManifestStateV1_UserCancelled = 10,
};

enum XbHistoricalSessionOwnerStateV1 : std::int32_t
{
    XbHistoricalSessionOwnerStateV1_ActiveOwned = 0,
    XbHistoricalSessionOwnerStateV1_InactiveLeaseReleased = 1,
    XbHistoricalSessionOwnerStateV1_EvidenceMissing = 2,
    XbHistoricalSessionOwnerStateV1_UnsafePath = 3,
    XbHistoricalSessionOwnerStateV1_Inaccessible = 4,
    XbHistoricalSessionOwnerStateV1_IoFailure = 5,
    XbHistoricalSessionOwnerStateV1_Unknown = 6,
};

enum XbHistoricalSessionFilesystemStateV1 : std::int32_t
{
    XbHistoricalSessionFilesystemStateV1_NotProvided = 0,
    XbHistoricalSessionFilesystemStateV1_Exists = 1,
    XbHistoricalSessionFilesystemStateV1_Absent = 2,
    XbHistoricalSessionFilesystemStateV1_ParentAbsent = 3,
    XbHistoricalSessionFilesystemStateV1_Inaccessible = 4,
    XbHistoricalSessionFilesystemStateV1_OutsideTrustedRoot = 5,
    XbHistoricalSessionFilesystemStateV1_ReparseEncountered = 6,
    XbHistoricalSessionFilesystemStateV1_Invalid = 7,
    XbHistoricalSessionFilesystemStateV1_TypeMismatch = 8,
    XbHistoricalSessionFilesystemStateV1_IoFailure = 9,
    XbHistoricalSessionFilesystemStateV1_Unknown = 10,
};

enum XbHistoricalSessionScanStringFieldV1 : std::int32_t
{
    XbHistoricalSessionScanStringFieldV1_MediaOutputRoot = 0,
    XbHistoricalSessionScanStringFieldV1_SessionsRoot = 1,
};

enum XbHistoricalSessionStringFieldV1 : std::int32_t
{
    XbHistoricalSessionStringFieldV1_SessionId = 0,
    XbHistoricalSessionStringFieldV1_WorkingCandidatePath = 1,
    XbHistoricalSessionStringFieldV1_PlannedFinalCandidatePath = 2,
    XbHistoricalSessionStringFieldV1_PublishedCandidatePath = 3,
    XbHistoricalSessionStringFieldV1_SessionDirectory = 4,
    XbHistoricalSessionStringFieldV1_ManifestPath = 5,
};

enum XbNarrowReconciliationStatusV1 : std::int32_t
{
    XbNarrowReconciliationStatusV1_Reconciled = 0,
    XbNarrowReconciliationStatusV1_AlreadyReconciled = 1,
    XbNarrowReconciliationStatusV1_NotEligibleState = 2,
    XbNarrowReconciliationStatusV1_InvalidSourceFacts = 3,
    XbNarrowReconciliationStatusV1_SemanticConflict = 4,
    XbNarrowReconciliationStatusV1_GuardRejected = 5,
    XbNarrowReconciliationStatusV1_RevisionChanged = 6,
    XbNarrowReconciliationStatusV1_ConcurrentChange = 7,
    XbNarrowReconciliationStatusV1_ImmutableFieldViolation = 8,
    XbNarrowReconciliationStatusV1_UnsupportedSchema = 9,
    XbNarrowReconciliationStatusV1_EvidenceInsufficient = 10,
    XbNarrowReconciliationStatusV1_CasFailed = 11,
    XbNarrowReconciliationStatusV1_IoFailure = 12,
    XbNarrowReconciliationStatusV1_Unknown = 13,
};

enum XbNarrowReconciliationGuardStatusV1 : std::int32_t
{
    XbNarrowReconciliationGuardStatusV1_EvidenceComplete = 0,
    XbNarrowReconciliationGuardStatusV1_ActiveOwner = 1,
    XbNarrowReconciliationGuardStatusV1_OwnerEvidenceMissing = 2,
    XbNarrowReconciliationGuardStatusV1_RevisionMismatch = 3,
    XbNarrowReconciliationGuardStatusV1_ManifestNotEligible = 4,
    XbNarrowReconciliationGuardStatusV1_ManifestUnsupported = 5,
    XbNarrowReconciliationGuardStatusV1_PathUnsafe = 6,
    XbNarrowReconciliationGuardStatusV1_PathInaccessible = 7,
    XbNarrowReconciliationGuardStatusV1_WorkingStillPresent = 8,
    XbNarrowReconciliationGuardStatusV1_WorkingAbsenceUnproven = 9,
    XbNarrowReconciliationGuardStatusV1_FinalMissing = 10,
    XbNarrowReconciliationGuardStatusV1_FinalUnsafe = 11,
    XbNarrowReconciliationGuardStatusV1_IdentityMissing = 12,
    XbNarrowReconciliationGuardStatusV1_IdentityMismatch = 13,
    XbNarrowReconciliationGuardStatusV1_HardLinkAmbiguous = 14,
    XbNarrowReconciliationGuardStatusV1_ConcurrentChange = 15,
    XbNarrowReconciliationGuardStatusV1_IoFailure = 16,
    XbNarrowReconciliationGuardStatusV1_Unknown = 17,
};

enum XbNarrowReconciliationCasStatusV1 : std::int32_t
{
    XbNarrowReconciliationCasStatusV1_Ready = 0,
    XbNarrowReconciliationCasStatusV1_Succeeded = 1,
    XbNarrowReconciliationCasStatusV1_RevisionMismatch = 2,
    XbNarrowReconciliationCasStatusV1_NotFound = 3,
    XbNarrowReconciliationCasStatusV1_Inaccessible = 4,
    XbNarrowReconciliationCasStatusV1_UnsupportedSchema = 5,
    XbNarrowReconciliationCasStatusV1_MalformedManifest = 6,
    XbNarrowReconciliationCasStatusV1_SemanticInvalid = 7,
    XbNarrowReconciliationCasStatusV1_ConcurrentChange = 8,
    XbNarrowReconciliationCasStatusV1_AtomicWriteFailure = 9,
    XbNarrowReconciliationCasStatusV1_IoFailure = 10,
    XbNarrowReconciliationCasStatusV1_InvalidInput = 11,
    XbNarrowReconciliationCasStatusV1_Inactive = 12,
};

enum XbPreviewState : std::int32_t
{
    XbPreviewState_Stopped = 0,
    XbPreviewState_Starting = 1,
    XbPreviewState_Running = 2,
    XbPreviewState_Stopping = 3,
    XbPreviewState_Error = 4,
};

enum XbRecordingState : std::int32_t
{
    XbRecordingState_Idle = 0,
    XbRecordingState_Starting = 1,
    XbRecordingState_Recording = 2,
    XbRecordingState_Stopping = 3,
    XbRecordingState_Completed = 4,
    XbRecordingState_Failed = 5,
    XbRecordingState_Pausing = 6,
    XbRecordingState_Paused = 7,
    XbRecordingState_Resuming = 8,
    XbRecordingState_UserCancelled = 9,
};

enum XbPreviewStatsFlags : std::uint32_t
{
    XbPreviewStatsFlags_None = 0,
    XbPreviewStatsFlags_WdaApplied = 1u << 0,
    XbPreviewStatsFlags_WdaFailed = 1u << 1,
    XbPreviewStatsFlags_UsingWarp = 1u << 2,
    XbPreviewStatsFlags_HdrDetected = 1u << 3,
    XbPreviewStatsFlags_Occluded = 1u << 4,
    XbPreviewStatsFlags_Minimized = 1u << 5,
    XbPreviewStatsFlags_WindowTargetMinimized = 1u << 6,
};

enum XbCursorMode : std::int32_t
{
    XbCursorMode_SystemCursor = 0,
    XbCursorMode_CustomCursor = 1,
};

enum XbCursorFallbackReason : std::int32_t
{
    XbCursorFallbackReason_None = 0,
    XbCursorFallbackReason_ApiUnavailable = 1,
    XbCursorFallbackReason_CustomRendererInitializationFailed = 2,
    XbCursorFallbackReason_WgcSettingFailed = 3,
    XbCursorFallbackReason_WgcReadbackMismatch = 4,
};

enum XbCursorShapeKind : std::uint32_t
{
    XbCursorShapeKind_None = 0,
    XbCursorShapeKind_ColorAlpha = 1,
    XbCursorShapeKind_ColorMask = 2,
    XbCursorShapeKind_MonochromeAndXor = 3,
    XbCursorShapeKind_BuiltInFallbackArrow = 4,
};

#pragma pack(push, 8)

struct XbPreviewCreateOptions
{
    std::uint32_t structSize;
    std::uint32_t apiVersion;
    std::uint64_t exclusionWindow;
    std::uint32_t allowWarp;
    std::uint32_t framePoolBufferCount;
    std::uint32_t statsIntervalMilliseconds;
    std::uint32_t reserved0;
    const wchar_t* diagnosticLogDirectory;
    std::uint64_t reserved1;
    std::uint64_t reserved2;
    std::uint64_t reserved3;
    std::uint64_t reserved4;
};

struct XbPreviewStats
{
    std::uint32_t structSize;
    std::uint32_t apiVersion;
    std::int32_t state;
    std::uint32_t flags;
    std::uint64_t sessionIdHigh;
    std::uint64_t sessionIdLow;
    std::uint64_t captureFrameCount;
    std::uint64_t presentFrameCount;
    std::uint64_t droppedFrameCount;
    std::uint64_t framePoolRecreateCount;
    std::uint64_t swapChainResizeCount;
    double captureFps;
    double presentFps;
    double recentLatencyMilliseconds;
    double p50LatencyMilliseconds;
    double p95LatencyMilliseconds;
    double maxLatencyMilliseconds;
    std::uint32_t captureWidth;
    std::uint32_t captureHeight;
    std::uint32_t previewWidth;
    std::uint32_t previewHeight;
    std::int32_t lastResult;
    std::int32_t deviceRemovedReason;
    std::int32_t wdaResult;
    std::uint32_t wdaLastError;
    std::uint32_t usedWarp;
    std::uint32_t hdrDetected;
    std::int64_t lastSystemRelativeTime100ns;
    std::int64_t lastFrameArrivalQpc;
    std::int64_t lastPresentBeforeQpc;
    std::int64_t lastPresentAfterQpc;
    std::uint64_t workingSetBytes;
    std::uint64_t privateBytes;
    std::uint64_t cameraUpdateCount;
    std::uint64_t invalidCameraStateFallbackCount;
    std::uint64_t nativeLastAppliedSequence;
    double cameraUpdateRate;
    double nativeAppliedZoom;
    double nativeAppliedCenterX;
    double nativeAppliedCenterY;
    std::int32_t nativeAppliedMode;
    std::uint32_t nativeCameraEnabled;
    wchar_t adapterName[128];
    wchar_t logFilePath[260];
    std::uint64_t reserved1;
    std::uint64_t reserved2;
    std::uint64_t reserved3;
    std::uint64_t reserved4;
};

struct XbPreviewGpuExportFrameV1
{
    std::uint32_t structSize;
    std::uint32_t version;
    std::uint64_t sharedHandle;
    std::uint32_t width;
    std::uint32_t height;
    std::uint32_t format;
    std::uint32_t slotIndex;
    std::uint64_t resourceGeneration;
    std::uint64_t frameGeneration;
    std::uint64_t skippedFrameCount;
    std::uint32_t adapterLuidLow;
    std::int32_t adapterLuidHigh;
    std::uint64_t rendererGeneration;
};

struct XbPreviewAbiLayout
{
    std::uint32_t structSize;
    std::uint32_t apiVersion;
    std::uint32_t pointerSize;
    std::uint32_t packing;
    std::uint32_t createOptionsSize;
    std::uint32_t statsSize;
    std::uint32_t letterboxRectSize;
    std::uint32_t wcharSize;
    std::uint32_t cameraStateSize;
    std::uint32_t cursorStatsSize;
    std::uint32_t recordingSnapshotSize;
};

struct XbCameraState
{
    std::uint32_t structSize;
    std::uint32_t apiVersion;
    std::uint64_t sequence;
    std::int64_t timestampQpc;
    std::uint32_t enabled;
    std::int32_t mode;
    double zoom;
    double centerX;
    double centerY;
    double transitionProgress;
    double targetX;
    double targetY;
    std::uint32_t clampX;
    std::uint32_t clampY;
    std::uint64_t reserved1;
    std::uint64_t reserved2;
    std::uint64_t reserved3;
    std::uint64_t reserved4;
};

constexpr std::uint32_t XB_PREVIEW_SESSION_GEOMETRY_VERSION_1 = 1;

struct XbPreviewSessionGeometryV1
{
    std::uint32_t structSize;
    std::uint32_t version;
    std::int32_t sourceWidth;
    std::int32_t sourceHeight;
    std::int32_t captureLeft;
    std::int32_t captureTop;
    std::int32_t captureWidth;
    std::int32_t captureHeight;
    std::int32_t outputWidth;
    std::int32_t outputHeight;
    std::uint64_t geometryRevision;
    std::uint32_t flags;
    std::uint32_t reserved0;
};

struct XbLetterboxRect
{
    float x;
    float y;
    float width;
    float height;
};

struct XbCursorStats
{
    std::uint32_t structSize;
    std::uint32_t apiVersion;
    std::int32_t requestedMode;
    std::int32_t actualMode;
    std::int32_t fallbackReason;
    std::uint32_t wgcCursorPropertyAvailable;
    std::uint32_t systemCursorIncluded;
    std::uint32_t customCursorLayerActive;
    std::uint32_t lastFrameDrawn;
    std::uint32_t cursorVisible;
    std::uint32_t cursorInsideMonitor;
    std::int32_t wgcCursorSettingResult;
    std::uint32_t wgcCursorSettingLastError;
    std::int32_t getCursorInfoResult;
    std::uint32_t getCursorInfoLastError;
    std::int32_t shapeConversionResult;
    std::uint32_t shapeConversionLastError;
    std::uint32_t shapeKind;
    std::uint64_t cursorSequence;
    std::uint64_t sampleCount;
    std::uint64_t drawCount;
    std::uint64_t hiddenSkipCount;
    std::uint64_t outsideMonitorSkipCount;
    std::uint64_t outsideCameraSkipCount;
    std::uint64_t getCursorInfoFailureCount;
    std::uint64_t shapeCacheHitCount;
    std::uint64_t shapeCacheMissCount;
    std::uint64_t textureUploadCount;
    std::uint64_t shapeConversionFailureCount;
    std::uint64_t builtInFallbackCount;
    std::uint64_t xorApproximationPixelCount;
    std::uint64_t diagnosticQueueDropCount;
    std::int64_t timestampQpc;
    std::int32_t screenX;
    std::int32_t screenY;
    double sourceX;
    double sourceY;
    double cameraViewLeft;
    double cameraViewTop;
    double cameraViewWidth;
    double cameraViewHeight;
    double outputHotspotX;
    double outputHotspotY;
    double outputLeft;
    double outputTop;
    double outputWidth;
    double outputHeight;
    double zoom;
    double centerX;
    double centerY;
    double viewportX;
    double viewportY;
    double viewportWidth;
    double viewportHeight;
    double lastRenderDurationMilliseconds;
    std::uint64_t shapeId;
    std::uint64_t shapeGeneration;
    std::uint32_t shapeWidth;
    std::uint32_t shapeHeight;
    std::uint32_t hotspotX;
    std::uint32_t hotspotY;
    wchar_t logFilePath[260];
    std::uint64_t reserved1;
    std::uint64_t reserved2;
    std::uint64_t reserved3;
    std::uint64_t reserved4;
};

struct XbAudioControlsV1
{
    std::uint32_t structSize;
    std::uint32_t abiVersion;
    std::uint32_t systemMuted;
    std::uint32_t microphoneMuted;
    double microphoneGainDb;
    std::uint64_t reserved1;
    std::uint64_t reserved2;
};

struct XbAudioControlSnapshotV1
{
    std::uint32_t structSize;
    std::uint32_t abiVersion;
    std::uint32_t systemMuted;
    std::uint32_t microphoneMuted;
    double microphoneGainDb;
    double microphoneGainLinear;
    double programHeadroomCoefficient;
    std::uint64_t controlRevision;
    std::uint64_t pendingControlRevision;
    std::uint32_t systemPeakAbsolutePcm16;
    std::uint32_t microphonePeakAbsolutePcm16;
    std::uint32_t microphonePostGainPeakAbsolutePcm16;
    std::uint32_t programPeakAbsolutePcm16;
    double systemRmsPcm16;
    double microphoneRmsPcm16;
    double microphonePostGainRmsPcm16;
    std::uint64_t microphonePostGainOverloadSamples;
    std::uint64_t outputClampSamples;
    std::uint64_t outputFrames;
    std::uint64_t outputBlocks;
    std::uint32_t meterWindowFrames;
    std::uint32_t microphoneGainParameterClamped;
    std::uint64_t endpointLevelFlags;
};

struct XbMicrophoneDeviceListV1
{
    std::uint32_t structSize;
    std::uint32_t abiVersion;
    std::uint64_t generation;
    std::uint32_t deviceCount;
    std::uint32_t monitorActive;
    std::uint32_t defaultAvailable;
    std::uint32_t deviceAddedCount;
    std::uint32_t deviceRemovedCount;
    std::uint32_t reserved0;
    wchar_t defaultEndpointId[512];
    wchar_t defaultDisplayName[256];
};

struct XbMicrophoneDeviceV1
{
    std::uint32_t structSize;
    std::uint32_t abiVersion;
    std::uint64_t generation;
    std::uint32_t index;
    std::uint32_t available;
    wchar_t endpointId[512];
    wchar_t displayName[256];
};

struct XbMicrophoneSelectionV1
{
    std::uint32_t structSize;
    std::uint32_t abiVersion;
    std::int32_t kind;
    std::uint32_t reserved0;
    wchar_t endpointId[512];
    wchar_t displayName[256];
};

struct XbMicrophoneSelectionSnapshotV1
{
    std::uint32_t structSize;
    std::uint32_t abiVersion;
    std::int32_t kind;
    std::uint32_t available;
    std::uint32_t sessionLocked;
    std::uint32_t reserved0;
    wchar_t endpointId[512];
    wchar_t displayName[256];
};

struct XbRecordingSnapshot
{
    std::uint32_t structSize;
    std::uint32_t apiVersion;
    std::int32_t state;
    std::int32_t lastResult;
    std::int64_t startUtc100ns;
    std::int64_t elapsed100ns;
    std::uint32_t outputSuccess;
    std::uint32_t finalizeAttempted;
    std::int32_t finalizeHResult;
    std::int32_t failureHResult;
    std::uint32_t finalizeCount;
    std::uint32_t activeEncoder;
    std::uint32_t residualOutstanding;
    std::uint32_t outputCleanupAttempted;
    std::uint32_t outputCleanupSucceeded;
    std::int32_t outputCleanupHResult;
    std::uint64_t framesSubmitted;
    wchar_t sessionId[64];
    // Legacy P2.5 direct-output path. It retains its original meaning until
    // P2.6A-2 switches production consumers to the explicit path facts below.
    // It must not be interpreted as Working, PlannedFinal, or Published.
    wchar_t outputPath[260];
    wchar_t errorMessage[256];
    // Phase A assigns these existing 64-bit reserved slots without changing
    // the binary layout. They remain zero until Pause worker plumbing lands.
    std::uint64_t pauseCount;
    std::uint64_t totalPaused100ns;
    std::uint64_t reserved3;
    std::uint64_t reserved4;
    std::uint32_t readyToPublish;
    std::uint32_t published;
    std::uint32_t publishAttempted;
    std::int32_t publishHResult;
    std::uint32_t validationAttempted;
    std::int32_t validationHResult;
    wchar_t workingPath[260];
    wchar_t plannedFinalPath[260];
    wchar_t publishedPath[260];
};

struct XbHistoricalSessionScanAbiLayoutV1
{
    std::uint32_t structSize;
    std::uint32_t abiVersion;
    std::uint32_t pointerSize;
    std::uint32_t packing;
    std::uint32_t wcharSize;
    std::uint32_t optionsSize;
    std::uint32_t summarySize;
    std::uint32_t itemSize;
};

struct XbHistoricalSessionScanOptionsV1
{
    std::uint32_t structSize;
    std::uint32_t abiVersion;
    const wchar_t* diagnosticLogDirectory;
    std::uint32_t maximumEntries;
    std::uint32_t reserved0;
    std::uint64_t reserved1;
    std::uint64_t reserved2;
};

// Additive explicit-root entry options. The legacy diagnostic-derived V1
// options above remain frozen for historical callers.
struct XbHistoricalSessionScanOutputRootOptionsV1
{
    std::uint32_t structSize;
    std::uint32_t abiVersion;
    const wchar_t* mediaOutputRoot;
    std::uint32_t maximumEntries;
    std::uint32_t reserved0;
    std::uint64_t reserved1;
    std::uint64_t reserved2;
};

struct XbHistoricalSessionScanSummaryV1
{
    std::uint32_t structSize;
    std::uint32_t abiVersion;
    std::int32_t status;
    std::int32_t diagnosticHResult;
    std::uint32_t sessionCount;
    std::uint32_t unrecognizedEntryCount;
    std::uint64_t entriesObserved;
    std::uint64_t maximumEntries;
    std::uint32_t truncated;
    std::uint32_t mediaWithoutSessionDirectoryBlindSpot;
    std::uint64_t reserved1;
    std::uint64_t reserved2;
};

struct XbHistoricalSessionItemV1
{
    std::uint32_t structSize;
    std::uint32_t abiVersion;
    std::int32_t classification;
    std::int32_t severity;
    std::uint64_t reasons;
    std::int32_t manifestParseStatus;
    std::int32_t manifestParseHResult;
    std::int32_t manifestSemanticIssue;
    std::int32_t manifestState;
    std::uint32_t observedSchemaVersion;
    std::uint32_t observedSchemaVersionAvailable;
    std::uint64_t observedRevision;
    std::uint32_t observedRevisionAvailable;
    std::uint32_t manifestAvailable;
    std::uint32_t manifestRevisionStable;
    std::int32_t ownerState;
    std::int32_t ownerHResult;
    std::uint32_t reserved0;
    std::int32_t workingFilesystemState;
    std::int32_t workingHResult;
    std::uint32_t workingSizeAvailable;
    std::uint32_t reserved1;
    std::uint64_t workingSize;
    std::int32_t plannedFinalFilesystemState;
    std::int32_t plannedFinalHResult;
    std::uint32_t plannedFinalSizeAvailable;
    std::uint32_t reserved2;
    std::uint64_t plannedFinalSize;
    std::int32_t publishedFilesystemState;
    std::int32_t publishedHResult;
    std::uint32_t publishedSizeAvailable;
    std::uint32_t reserved3;
    std::uint64_t publishedSize;
    std::uint32_t persistentWorkingIdentityAvailable;
    std::uint32_t persistentIdentityComparisonAttempted;
    std::uint32_t strongIdentityMatch;
    std::uint32_t deleteAllowed;
    std::uint32_t reconciliationAuthorized;
    std::uint32_t reserved4;
    std::uint64_t reserved5;
    std::uint64_t reserved6;
};

struct XbNarrowReconciliationAbiLayoutV1
{
    std::uint32_t structSize;
    std::uint32_t abiVersion;
    std::uint32_t pointerSize;
    std::uint32_t packing;
    std::uint32_t wcharSize;
    std::uint32_t optionsSize;
    std::uint32_t resultSize;
    std::uint32_t reserved0;
};

struct XbNarrowReconciliationOptionsV1
{
    std::uint32_t structSize;
    std::uint32_t abiVersion;
    const wchar_t* diagnosticLogDirectory;
    const wchar_t* canonicalSessionId;
    std::uint64_t expectedRevision;
    std::uint64_t reserved0;
    std::uint64_t reserved1;
};

// Additive explicit-root entry options. The legacy diagnostic-derived V1
// options above remain frozen for historical callers.
struct XbNarrowReconciliationOutputRootOptionsV1
{
    std::uint32_t structSize;
    std::uint32_t abiVersion;
    const wchar_t* mediaOutputRoot;
    const wchar_t* canonicalSessionId;
    std::uint64_t expectedRevision;
    std::uint64_t reserved0;
    std::uint64_t reserved1;
};

struct XbNarrowReconciliationResultV1
{
    std::uint32_t structSize;
    std::uint32_t abiVersion;
    std::int32_t status;
    std::int32_t diagnosticHResult;
    std::uint64_t expectedRevision;
    std::uint64_t observedRevision;
    std::uint32_t observedRevisionAvailable;
    std::int32_t guardStatus;
    std::uint32_t guardStatusAvailable;
    std::int32_t casStatus;
    std::uint32_t casStatusAvailable;
    std::uint32_t reserved0;
    std::uint64_t reserved1;
};

#pragma pack(pop)

static_assert(sizeof(void*) == 8, "P0 only supports x64.");
static_assert(sizeof(wchar_t) == 2, "The ABI requires Windows UTF-16 wchar_t.");
static_assert(sizeof(XbPreviewCreateOptions) == 72, "Unexpected XbPreviewCreateOptions layout.");
static_assert(sizeof(XbPreviewStats) == 1080, "Unexpected XbPreviewStats layout.");
static_assert(sizeof(XbPreviewAbiLayout) == 44, "Unexpected XbPreviewAbiLayout layout.");
static_assert(sizeof(XbCameraState) == 120, "Unexpected XbCameraState layout.");
static_assert(
    sizeof(XbPreviewSessionGeometryV1) == 56,
    "Unexpected XbPreviewSessionGeometryV1 layout.");
static_assert(sizeof(XbLetterboxRect) == 16, "Unexpected XbLetterboxRect layout.");
static_assert(sizeof(XbPreviewGpuExportFrameV1) == 72);
static_assert(sizeof(XbCursorStats) == 944, "Unexpected XbCursorStats layout.");
static_assert(sizeof(XbAudioControlsV1) == 40);
static_assert(sizeof(XbAudioControlSnapshotV1) == 144);
static_assert(sizeof(XbMicrophoneDeviceListV1) == 1576);
static_assert(sizeof(XbMicrophoneDeviceV1) == 1560);
static_assert(sizeof(XbMicrophoneSelectionV1) == 1552);
static_assert(sizeof(XbMicrophoneSelectionSnapshotV1) == 1560);
static_assert(
    sizeof(XbRecordingSnapshot) == 2856,
    "Unexpected XbRecordingSnapshot layout.");
static_assert(sizeof(XbHistoricalSessionScanAbiLayoutV1) == 32);
static_assert(sizeof(XbHistoricalSessionScanOptionsV1) == 40);
static_assert(sizeof(XbHistoricalSessionScanOutputRootOptionsV1) == 40);
static_assert(sizeof(XbHistoricalSessionScanSummaryV1) == 64);
static_assert(sizeof(XbHistoricalSessionItemV1) == 192);
static_assert(sizeof(XbNarrowReconciliationAbiLayoutV1) == 32);
static_assert(sizeof(XbNarrowReconciliationOptionsV1) == 48);
static_assert(sizeof(XbNarrowReconciliationOutputRootOptionsV1) == 48);
static_assert(sizeof(XbNarrowReconciliationResultV1) == 64);
static_assert(offsetof(XbPreviewCreateOptions, exclusionWindow) == 8);
static_assert(offsetof(XbPreviewCreateOptions, diagnosticLogDirectory) == 32);
static_assert(offsetof(XbPreviewStats, captureFrameCount) == 32);
static_assert(offsetof(XbPreviewStats, captureFps) == 72);
static_assert(offsetof(XbPreviewStats, captureWidth) == 120);
static_assert(offsetof(XbPreviewStats, lastSystemRelativeTime100ns) == 160);
static_assert(offsetof(XbPreviewStats, cameraUpdateCount) == 208);
static_assert(offsetof(XbPreviewStats, adapterName) == 272);
static_assert(offsetof(XbPreviewStats, logFilePath) == 528);
static_assert(offsetof(XbCameraState, sequence) == 8);
static_assert(offsetof(XbCameraState, zoom) == 32);
static_assert(offsetof(XbCameraState, targetX) == 64);
static_assert(offsetof(XbPreviewSessionGeometryV1, sourceWidth) == 8);
static_assert(offsetof(XbPreviewSessionGeometryV1, captureLeft) == 16);
static_assert(offsetof(XbPreviewSessionGeometryV1, outputWidth) == 32);
static_assert(offsetof(XbPreviewSessionGeometryV1, geometryRevision) == 40);
static_assert(offsetof(XbPreviewSessionGeometryV1, flags) == 48);
static_assert(offsetof(XbCursorStats, cursorSequence) == 72);
static_assert(offsetof(XbCursorStats, sourceX) == 200);
static_assert(offsetof(XbCursorStats, shapeId) == 360);
static_assert(offsetof(XbCursorStats, logFilePath) == 392);
static_assert(offsetof(XbPreviewAbiLayout, recordingSnapshotSize) == 40);
static_assert(offsetof(XbRecordingSnapshot, startUtc100ns) == 16);
static_assert(offsetof(XbRecordingSnapshot, outputCleanupAttempted) == 60);
static_assert(offsetof(XbRecordingSnapshot, outputCleanupSucceeded) == 64);
static_assert(offsetof(XbRecordingSnapshot, outputCleanupHResult) == 68);
static_assert(offsetof(XbRecordingSnapshot, sessionId) == 80);
static_assert(offsetof(XbRecordingSnapshot, outputPath) == 208);
static_assert(offsetof(XbRecordingSnapshot, errorMessage) == 728);
static_assert(offsetof(XbRecordingSnapshot, pauseCount) == 1240);
static_assert(offsetof(XbRecordingSnapshot, totalPaused100ns) == 1248);
static_assert(offsetof(XbRecordingSnapshot, readyToPublish) == 1272);
static_assert(offsetof(XbRecordingSnapshot, published) == 1276);
static_assert(offsetof(XbRecordingSnapshot, publishAttempted) == 1280);
static_assert(offsetof(XbRecordingSnapshot, publishHResult) == 1284);
static_assert(offsetof(XbRecordingSnapshot, validationAttempted) == 1288);
static_assert(offsetof(XbRecordingSnapshot, validationHResult) == 1292);
static_assert(offsetof(XbRecordingSnapshot, workingPath) == 1296);
static_assert(offsetof(XbRecordingSnapshot, plannedFinalPath) == 1816);
static_assert(offsetof(XbRecordingSnapshot, publishedPath) == 2336);
static_assert(offsetof(XbHistoricalSessionScanAbiLayoutV1, itemSize) == 28);
static_assert(offsetof(XbHistoricalSessionScanOptionsV1, diagnosticLogDirectory) == 8);
static_assert(offsetof(XbHistoricalSessionScanOptionsV1, maximumEntries) == 16);
static_assert(offsetof(XbHistoricalSessionScanOptionsV1, reserved1) == 24);
static_assert(offsetof(XbHistoricalSessionScanOptionsV1, reserved2) == 32);
static_assert(offsetof(
    XbHistoricalSessionScanOutputRootOptionsV1, mediaOutputRoot) == 8);
static_assert(offsetof(
    XbHistoricalSessionScanOutputRootOptionsV1, maximumEntries) == 16);
static_assert(offsetof(
    XbHistoricalSessionScanOutputRootOptionsV1, reserved1) == 24);
static_assert(offsetof(
    XbHistoricalSessionScanOutputRootOptionsV1, reserved2) == 32);
static_assert(offsetof(XbHistoricalSessionScanSummaryV1, status) == 8);
static_assert(offsetof(XbHistoricalSessionScanSummaryV1, sessionCount) == 16);
static_assert(offsetof(XbHistoricalSessionScanSummaryV1, entriesObserved) == 24);
static_assert(offsetof(XbHistoricalSessionScanSummaryV1, maximumEntries) == 32);
static_assert(offsetof(XbHistoricalSessionScanSummaryV1, truncated) == 40);
static_assert(offsetof(XbHistoricalSessionScanSummaryV1, reserved1) == 48);
static_assert(offsetof(XbHistoricalSessionScanSummaryV1, reserved2) == 56);
static_assert(offsetof(XbHistoricalSessionItemV1, reasons) == 16);
static_assert(offsetof(XbHistoricalSessionItemV1, manifestParseStatus) == 24);
static_assert(offsetof(XbHistoricalSessionItemV1, manifestState) == 36);
static_assert(offsetof(XbHistoricalSessionItemV1, observedSchemaVersion) == 40);
static_assert(offsetof(XbHistoricalSessionItemV1, observedRevision) == 48);
static_assert(offsetof(XbHistoricalSessionItemV1, manifestAvailable) == 60);
static_assert(offsetof(XbHistoricalSessionItemV1, ownerState) == 68);
static_assert(offsetof(XbHistoricalSessionItemV1, workingFilesystemState) == 80);
static_assert(offsetof(XbHistoricalSessionItemV1, workingSize) == 96);
static_assert(offsetof(XbHistoricalSessionItemV1, plannedFinalFilesystemState) == 104);
static_assert(offsetof(XbHistoricalSessionItemV1, plannedFinalSize) == 120);
static_assert(offsetof(XbHistoricalSessionItemV1, publishedFilesystemState) == 128);
static_assert(offsetof(XbHistoricalSessionItemV1, publishedSize) == 144);
static_assert(offsetof(XbHistoricalSessionItemV1, persistentWorkingIdentityAvailable) == 152);
static_assert(offsetof(XbHistoricalSessionItemV1, reconciliationAuthorized) == 168);
static_assert(offsetof(XbHistoricalSessionItemV1, reserved5) == 176);
static_assert(offsetof(XbHistoricalSessionItemV1, reserved6) == 184);
static_assert(offsetof(XbNarrowReconciliationOptionsV1, diagnosticLogDirectory) == 8);
static_assert(offsetof(XbNarrowReconciliationOptionsV1, canonicalSessionId) == 16);
static_assert(offsetof(XbNarrowReconciliationOptionsV1, expectedRevision) == 24);
static_assert(offsetof(
    XbNarrowReconciliationOutputRootOptionsV1, mediaOutputRoot) == 8);
static_assert(offsetof(
    XbNarrowReconciliationOutputRootOptionsV1, canonicalSessionId) == 16);
static_assert(offsetof(
    XbNarrowReconciliationOutputRootOptionsV1, expectedRevision) == 24);
static_assert(offsetof(XbNarrowReconciliationResultV1, status) == 8);
static_assert(offsetof(XbNarrowReconciliationResultV1, expectedRevision) == 16);
static_assert(offsetof(XbNarrowReconciliationResultV1, observedRevision) == 24);
static_assert(offsetof(XbNarrowReconciliationResultV1, guardStatus) == 36);
static_assert(offsetof(XbNarrowReconciliationResultV1, casStatus) == 44);
static_assert(offsetof(XbNarrowReconciliationResultV1, reserved1) == 56);

extern "C"
{
    XB_PREVIEW_API std::uint32_t XB_PREVIEW_CALL XbPreview_GetApiVersion() noexcept;

    XB_PREVIEW_API XbPreviewResult XB_PREVIEW_CALL XbPreview_GetAbiLayout(
        XbPreviewAbiLayout* layout) noexcept;

    XB_PREVIEW_API XbPreviewResult XB_PREVIEW_CALL
        XbPreview_GetHistoricalSessionScanAbiLayoutV1(
            XbHistoricalSessionScanAbiLayoutV1* layout) noexcept;

    // Performs one bounded, strictly read-only scan and freezes its point-in-
    // time results in an opaque handle. No function in this ABI publishes,
    // reconciles, deletes, renames, or otherwise mutates Session/media state.
    XB_PREVIEW_API XbPreviewResult XB_PREVIEW_CALL
        XbPreview_BeginHistoricalSessionScanV1(
            const XbHistoricalSessionScanOptionsV1* options,
            XbHistoricalSessionScanHandle* scanHandle,
            XbHistoricalSessionScanSummaryV1* summary) noexcept;

    // Formal product entry: scans exactly the already-resolved effective
    // recording root. It reuses the frozen scanner and result ABI.
    XB_PREVIEW_API XbPreviewResult XB_PREVIEW_CALL
        XbPreview_BeginHistoricalSessionScanForOutputRootV1(
            const XbHistoricalSessionScanOutputRootOptionsV1* options,
            XbHistoricalSessionScanHandle* scanHandle,
            XbHistoricalSessionScanSummaryV1* summary) noexcept;

    XB_PREVIEW_API XbPreviewResult XB_PREVIEW_CALL
        XbPreview_GetHistoricalSessionV1(
            XbHistoricalSessionScanHandle scanHandle,
            std::uint32_t index,
            XbHistoricalSessionItemV1* item) noexcept;

    XB_PREVIEW_API XbPreviewResult XB_PREVIEW_CALL
        XbPreview_GetHistoricalSessionScanStringV1(
            XbHistoricalSessionScanHandle scanHandle,
            XbHistoricalSessionScanStringFieldV1 field,
            wchar_t* buffer,
            std::uint32_t bufferLength,
            std::uint32_t* requiredLength) noexcept;

    XB_PREVIEW_API XbPreviewResult XB_PREVIEW_CALL
        XbPreview_GetHistoricalSessionStringV1(
            XbHistoricalSessionScanHandle scanHandle,
            std::uint32_t index,
            XbHistoricalSessionStringFieldV1 field,
            wchar_t* buffer,
            std::uint32_t bufferLength,
            std::uint32_t* requiredLength) noexcept;

    XB_PREVIEW_API XbPreviewResult XB_PREVIEW_CALL
        XbPreview_DestroyHistoricalSessionScanV1(
            XbHistoricalSessionScanHandle* scanHandle) noexcept;

    // This ABI is the only user-triggered mutation bridge for the narrow
    // recovery transition. It always re-enters the native operation-time
    // Guard and expected-revision CAS; scan observations never authorize it.
    XB_PREVIEW_API XbPreviewResult XB_PREVIEW_CALL
        XbPreview_GetNarrowReconciliationAbiLayoutV1(
            XbNarrowReconciliationAbiLayoutV1* layout) noexcept;

    XB_PREVIEW_API XbPreviewResult XB_PREVIEW_CALL
        XbPreview_ReconcileNarrowSessionV1(
            const XbNarrowReconciliationOptionsV1* options,
            XbNarrowReconciliationResultV1* result) noexcept;

    // Formal product entry: reconciles against the same already-resolved
    // effective recording root used to discover the candidate.
    XB_PREVIEW_API XbPreviewResult XB_PREVIEW_CALL
        XbPreview_ReconcileNarrowSessionForOutputRootV1(
            const XbNarrowReconciliationOutputRootOptionsV1* options,
            XbNarrowReconciliationResultV1* result) noexcept;

    XB_PREVIEW_API XbPreviewResult XB_PREVIEW_CALL XbPreview_Create(
        HWND previewHwnd,
        const XbPreviewCreateOptions* options,
        XbPreviewHandle* handle) noexcept;

    XB_PREVIEW_API XbPreviewResult XB_PREVIEW_CALL XbPreview_Start(
        XbPreviewHandle handle) noexcept;

    XB_PREVIEW_API XbPreviewResult XB_PREVIEW_CALL XbPreview_Stop(
        XbPreviewHandle handle) noexcept;

    XB_PREVIEW_API XbPreviewResult XB_PREVIEW_CALL XbPreview_StartRecording(
        XbPreviewHandle handle) noexcept;

    XB_PREVIEW_API XbPreviewResult XB_PREVIEW_CALL XbPreview_PauseRecording(
        XbPreviewHandle handle) noexcept;

    XB_PREVIEW_API XbPreviewResult XB_PREVIEW_CALL XbPreview_ResumeRecording(
        XbPreviewHandle handle) noexcept;

    XB_PREVIEW_API XbPreviewResult XB_PREVIEW_CALL
        XbPreview_SetAudioProgramMode(
            XbPreviewHandle handle,
            XbAudioProgramMode mode) noexcept;

    XB_PREVIEW_API XbPreviewResult XB_PREVIEW_CALL XbPreview_StopRecording(
        XbPreviewHandle handle) noexcept;

    XB_PREVIEW_API XbPreviewResult XB_PREVIEW_CALL XbPreview_CancelRecording(
        XbPreviewHandle handle) noexcept;

    XB_PREVIEW_API XbPreviewResult XB_PREVIEW_CALL
        XbPreview_GetRecordingSnapshot(
            XbPreviewHandle handle,
            XbRecordingSnapshot* snapshot) noexcept;

    XB_PREVIEW_API XbPreviewResult XB_PREVIEW_CALL
        XbPreview_SetAudioControlsV1(
            XbPreviewHandle handle,
            const XbAudioControlsV1* controls) noexcept;

    XB_PREVIEW_API XbPreviewResult XB_PREVIEW_CALL
        XbPreview_GetAudioControlSnapshotV1(
            XbPreviewHandle handle,
            XbAudioControlSnapshotV1* snapshot) noexcept;

    XB_PREVIEW_API XbPreviewResult XB_PREVIEW_CALL
        XbPreview_GetMicrophoneDeviceListV1(
            XbPreviewHandle handle,
            XbMicrophoneDeviceListV1* list) noexcept;

    XB_PREVIEW_API XbPreviewResult XB_PREVIEW_CALL
        XbPreview_GetMicrophoneDeviceV1(
            XbPreviewHandle handle,
            XbMicrophoneDeviceV1* device) noexcept;

    XB_PREVIEW_API XbPreviewResult XB_PREVIEW_CALL
        XbPreview_SetMicrophoneSelectionV1(
            XbPreviewHandle handle,
            const XbMicrophoneSelectionV1* selection) noexcept;

    XB_PREVIEW_API XbPreviewResult XB_PREVIEW_CALL
        XbPreview_GetMicrophoneSelectionV1(
            XbPreviewHandle handle,
            XbMicrophoneSelectionSnapshotV1* snapshot) noexcept;

    XB_PREVIEW_API XbPreviewResult XB_PREVIEW_CALL XbPreview_Resize(
        XbPreviewHandle handle,
        std::int32_t width,
        std::int32_t height) noexcept;

    XB_PREVIEW_API XbPreviewResult XB_PREVIEW_CALL
        XbPreview_SetGpuExportTargetSize(
            XbPreviewHandle handle,
            std::int32_t width,
            std::int32_t height) noexcept;

    XB_PREVIEW_API XbPreviewResult XB_PREVIEW_CALL
        XbPreview_SetSessionGeometry(
            XbPreviewHandle handle,
            const XbPreviewSessionGeometryV1* geometry) noexcept;

    XB_PREVIEW_API XbPreviewResult XB_PREVIEW_CALL XbPreview_SetCameraState(
        XbPreviewHandle handle,
        const XbCameraState* cameraState) noexcept;

    XB_PREVIEW_API XbPreviewResult XB_PREVIEW_CALL XbPreview_SetCursorMode(
        XbPreviewHandle handle,
        XbCursorMode cursorMode) noexcept;

    XB_PREVIEW_API XbPreviewResult XB_PREVIEW_CALL
        XbPreview_SetRecordCursorVisible(
            XbPreviewHandle handle,
            std::uint32_t visible) noexcept;

    XB_PREVIEW_API XbPreviewResult XB_PREVIEW_CALL
        XbPreview_GetRecordCursorVisible(
            XbPreviewHandle handle,
            std::uint32_t* requestedVisible,
            std::uint32_t* appliedVisible,
            std::uint64_t* revision) noexcept;

    XB_PREVIEW_API XbPreviewResult XB_PREVIEW_CALL XbPreview_SetCaptureTarget(
        XbPreviewHandle handle,
        XbCaptureTargetKind targetKind,
        std::uint64_t windowHandle) noexcept;

    XB_PREVIEW_API XbPreviewResult XB_PREVIEW_CALL
        XbPreview_SetWindowStagePose(
            XbPreviewHandle handle,
            XbWindowStageOrientation orientation,
            XbWindowStageLevel level) noexcept;

    // Product Stage seam: active=1 retargets frozen Motion A; active=0 calls
    // its frozen RequestReturn without changing Manual Zoom/Punch ownership.
    XB_PREVIEW_API XbPreviewResult XB_PREVIEW_CALL
        XbPreview_SetWindowShowcasePose(
            XbPreviewHandle handle,
            XbWindowStageOrientation orientation,
            XbWindowStageLevel level,
            std::uint32_t active) noexcept;

    XB_PREVIEW_API XbPreviewResult XB_PREVIEW_CALL
        XbPreview_SetWindowShowcaseBackgroundPreset(
            XbPreviewHandle handle,
            XbWindowShowcaseBackgroundPreset preset) noexcept;

    // The path must name an existing local PNG, JPEG, or BMP file. A failed
    // decode never replaces the currently active background resource.
    XB_PREVIEW_API XbPreviewResult XB_PREVIEW_CALL
        XbPreview_SetWindowShowcaseCustomBackground(
            XbPreviewHandle handle,
            const wchar_t* validatedLocalPath) noexcept;

    // A null or empty path clears the override and restores the established
    // p2.5a-recordings root. A non-empty path is the final media output root;
    // working .partial and Safe Publish paths remain native-owned.
    XB_PREVIEW_API XbPreviewResult XB_PREVIEW_CALL
        XbPreview_SetRecordingOutputRoot(
            XbPreviewHandle handle,
            const wchar_t* validatedLocalPath) noexcept;

    // Sets the next recording session's immutable CFR output cadence. Only
    // 30 and 60 are accepted, and active recording phases reject changes.
    XB_PREVIEW_API XbPreviewResult XB_PREVIEW_CALL
        XbPreview_SetRecordingFrameRate(
            XbPreviewHandle handle,
            std::uint32_t framesPerSecond) noexcept;

    XB_PREVIEW_API XbPreviewResult XB_PREVIEW_CALL XbPreview_GetCursorStats(
        XbPreviewHandle handle,
        XbCursorStats* stats) noexcept;

    XB_PREVIEW_API XbPreviewResult XB_PREVIEW_CALL XbPreview_GetStats(
        XbPreviewHandle handle,
        XbPreviewStats* stats) noexcept;

    XB_PREVIEW_API XbPreviewResult XB_PREVIEW_CALL
        XbPreview_GetGpuExportFrameV1(
            XbPreviewHandle handle,
            XbPreviewGpuExportFrameV1* frame) noexcept;

    XB_PREVIEW_API XbPreviewResult XB_PREVIEW_CALL XbPreview_GetLastError(
        XbPreviewHandle handle,
        wchar_t* buffer,
        std::uint32_t bufferLength) noexcept;

    XB_PREVIEW_API XbPreviewResult XB_PREVIEW_CALL XbPreview_Destroy(
        XbPreviewHandle* handle) noexcept;

    XB_PREVIEW_API XbPreviewResult XB_PREVIEW_CALL XbPreview_CalculateLetterbox(
        std::uint32_t sourceWidth,
        std::uint32_t sourceHeight,
        std::uint32_t destinationWidth,
        std::uint32_t destinationHeight,
        XbLetterboxRect* rect) noexcept;
}
