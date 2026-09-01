#pragma once

#include "VideoEncoderConfig.h"

#include <windows.h>

#include <array>
#include <cstddef>
#include <cstdint>
#include <string>
#include <vector>

namespace xbpreview
{
    inline constexpr std::size_t VideoCadenceTraceCapacity = 4096;

    enum class VideoCadenceDecision : std::uint8_t
    {
        Fresh,
        Duplicate,
        NoFrame,
        Missed
    };

    enum class VideoCadenceDuplicateClassification : std::uint8_t
    {
        NotDuplicate,
        PendingFutureArrivalEvidence,
        NormalSourceTickJitter,
        AvoidableHandoffLoss
    };

    struct VideoCadenceTraceRecord final
    {
        std::uint64_t recordOrdinal{};
        std::uint64_t tickIndex{};
        std::uint32_t selectedFps{};
        std::int64_t targetContentTime100ns{};
        std::int64_t actualWakeQpc{};
        std::int64_t scheduledDeadlineQpc{};
        std::int64_t deadlineErrorUs{};
        std::uint64_t pendingFrameSequence{};
        std::int64_t pendingSourceTimestamp100ns{};
        std::int64_t pendingEnqueueQpc{};
        std::uint64_t lastSubmittedFreshSequence{};
        std::int64_t lastSubmittedSourceTimestamp100ns{};
        std::uint64_t sourceArrivalsSincePreviousTick{};
        std::uint64_t pendingReplacementsSincePreviousTick{};
        std::uint64_t cadenceDropsSincePreviousTick{};
        std::uint64_t freshAvailableSequenceBeforeDeadline{};
        std::int64_t freshAvailableSourceTimestamp100ns{};
        std::int64_t freshAvailableEnqueueQpc{};
        VideoCadenceDecision decision{ VideoCadenceDecision::NoFrame };
        VideoCadenceDuplicateClassification duplicateClassification{
            VideoCadenceDuplicateClassification::NotDuplicate };
        bool freshAvailableBeforeDeadline{};
        bool missedDeadline{};
        bool dropThenNextTickDuplicate{};
    };

    struct VideoCadenceUnresolvedDuplicate final
    {
        std::uint64_t recordOrdinal{};
        std::uint64_t lastSubmittedFreshSequence{};
        std::int64_t scheduledDeadlineQpc{};
    };

    // A single worker owns this fixed-capacity store. Source/tick methods are
    // POD-only and noexcept; formatting and file I/O happen after worker join.
    struct VideoCadenceTraceBuffer final
    {
        void Reset(std::int64_t performanceCounterFrequency) noexcept;
        void ObserveSourceArrival(
            std::uint64_t sequence,
            std::int64_t sourceTimestamp100ns,
            std::int64_t enqueueQpc) noexcept;
        void ObservePendingReplacement() noexcept;
        void RecordTick(VideoCadenceTraceRecord record) noexcept;
        void FinalizeDuplicateClassifications() noexcept;

        [[nodiscard]] const VideoCadenceTraceRecord* FindRecord(
            std::uint64_t recordOrdinal) const noexcept;

        std::array<VideoCadenceTraceRecord, VideoCadenceTraceCapacity> records{};
        std::array<
            VideoCadenceUnresolvedDuplicate,
            VideoCadenceTraceCapacity> unresolvedDuplicates{};
        std::uint64_t totalTicks{};
        std::size_t recordCount{};
        std::size_t unresolvedHead{};
        std::size_t unresolvedCount{};
        std::uint64_t traceRecordsOverwritten{};
        std::uint64_t unresolvedDuplicateOverflows{};
        std::uint64_t fresh{};
        std::uint64_t duplicate{};
        std::uint64_t noFrame{};
        std::uint64_t missed{};
        std::uint64_t duplicateWithNoNewSourceAvailable{};
        std::uint64_t duplicateDespiteFreshAvailableBeforeDeadline{};
        std::uint64_t normalMultiSourceCadenceDrops{};
        std::uint64_t dropThenNextTickDuplicateCount{};
        std::uint64_t totalSourceArrivals{};
        std::uint64_t sourceArrivalsSincePreviousTick{};
        std::uint64_t pendingReplacementsSincePreviousTick{};
        std::uint64_t cadenceDropsSincePreviousTick{};
        std::uint64_t previousTickCadenceDrops{};
        std::int64_t qpcFrequency{};
        std::int64_t firstSourceArrivalQpc{};
        std::int64_t lastSourceArrivalQpc{};
        std::int64_t firstFreshOutputQpc{};
        std::int64_t lastFreshOutputQpc{};
        std::int64_t deadlineErrorSumUs{};
        std::int64_t maximumDeadlineErrorUs{};
    };

    enum class VideoEncoderState
    {
        Disabled,
        Starting,
        Running,
        Stopping,
        Finalizing,
        Completed,
        Failed,
        Unsupported,
        UserCancelled
    };

    inline constexpr std::size_t VideoEncoderCapabilityPropertyCapacity = 10;
    inline constexpr std::size_t VideoEncoderCapabilityPossibleValueLimit = 16;
    inline constexpr std::size_t VideoEncoderCapabilityTextLimit = 256;
    inline constexpr std::size_t VideoEncoderCapabilityJsonByteLimit = 64 * 1024;

    struct VideoEncoderCodecPropertyDiagnostic final
    {
        std::string property;
        HRESULT isSupportedHResult{ E_PENDING };
        bool isSupported{};
        HRESULT isModifiableHResult{ E_PENDING };
        bool isModifiable{};
        HRESULT currentValueHResult{ E_PENDING };
        std::string currentValue{ "N/A" };
        HRESULT rangeHResult{ E_PENDING };
        std::string rangeMinimum{ "N/A" };
        std::string rangeMaximum{ "N/A" };
        std::string rangeStep{ "N/A" };
        HRESULT possibleValuesHResult{ E_PENDING };
        std::vector<std::string> possibleValues;
        bool possibleValuesTruncated{};
    };

    struct VideoEncoderMediaTypeDiagnostic final
    {
        bool queryAttempted{};
        HRESULT queryHResult{ E_PENDING };
        HRESULT majorTypeHResult{ E_PENDING };
        std::string majorType{ "N/A" };
        HRESULT subtypeHResult{ E_PENDING };
        std::string subtype{ "N/A" };
        HRESULT frameSizeHResult{ E_PENDING };
        std::uint32_t width{};
        std::uint32_t height{};
        HRESULT frameRateHResult{ E_PENDING };
        std::uint32_t frameRateNumerator{};
        std::uint32_t frameRateDenominator{};
        HRESULT averageBitrateHResult{ E_PENDING };
        std::uint32_t averageBitrate{};
        HRESULT mpeg2ProfileHResult{ E_PENDING };
        std::uint32_t mpeg2Profile{};
        std::string mpeg2ProfileName{ "N/A" };
    };

    struct VideoEncoderCodecApiReadbackDiagnostic final
    {
        bool queryAttempted{};
        HRESULT codecApiHResult{ E_PENDING };
        HRESULT rateControlHResult{ E_PENDING };
        std::string rateControlValue{ "N/A" };
        std::string rateControlName{ "N/A" };
        HRESULT meanBitrateHResult{ E_PENDING };
        std::string meanBitrate{ "N/A" };
        HRESULT maxBitrateHResult{ E_PENDING };
        std::string maxBitrate{ "N/A" };
    };

    struct VideoEncoderBitrateNegotiationDiagnostics final
    {
        bool sinkWriterCreated{};
        bool outputMediaTypeCreated{};
        bool requestedOutputCapturedBeforeAddStream{};
        bool addStreamSucceeded{};
        HRESULT setInputMediaTypeHResult{ E_PENDING };
        bool setInputMediaTypeSucceeded{};
        bool actualTransformAvailablePreBegin{};
        bool beginWritingSucceeded{};
        bool firstVideoSampleWritten{};
        std::string actualTransformFirstAvailableAt{ "NOT_OBSERVED" };
        VideoEncoderMediaTypeDiagnostic requestedOutput;
        VideoEncoderMediaTypeDiagnostic actualInputPreBegin;
        VideoEncoderMediaTypeDiagnostic actualOutputPreBegin;
        VideoEncoderMediaTypeDiagnostic actualInputPostBegin;
        VideoEncoderMediaTypeDiagnostic actualOutputPostBegin;
        VideoEncoderCodecApiReadbackDiagnostic codecApiPreBegin;
        VideoEncoderCodecApiReadbackDiagnostic codecApiPostBegin;
        VideoEncoderCodecApiReadbackDiagnostic codecApiPostFirstSample;
        bool encoderConfigStoreApiAvailable{ true };
        bool currentCodeUsesEncoderConfigStore{};
        bool currentCodeOnlyUsesMediaTypeBitrate{ true };
        bool modifiableNoProvesInitNotConfigurable{};
        std::string requestedRateControl{ "N/A" };
        std::uint32_t requestedMeanBitrate{};
        HRESULT storeCreationHResult{ E_PENDING };
        HRESULT rateControlPropertySetHResult{ E_PENDING };
        HRESULT meanBitratePropertySetHResult{ E_PENDING };
        HRESULT sinkWriterConfigAttachHResult{ E_PENDING };
        bool inputMediaTypeEncodingParametersUsed{};
        HRESULT inputParametersRateControlSetHResult{ E_PENDING };
        HRESULT inputParametersMeanBitrateSetHResult{ E_PENDING };
    };

    struct VideoEncoderCapabilityDiagnostics final
    {
        bool probeAttempted{};
        HRESULT probeHResult{ E_PENDING };
        HRESULT sinkWriterExHResult{ E_PENDING };
        bool sinkWriterExAvailable{};
        bool actualTransformObtained{};
        std::uint32_t transformIndex{};
        std::string transformCategory{ "UNKNOWN" };
        HRESULT transformAttributesHResult{ E_PENDING };
        HRESULT transformClsidAttributeHResult{ E_PENDING };
        std::string transformClsidAttribute{ "UNKNOWN / NOT EXPOSED" };
        HRESULT persistQueryInterfaceHResult{ E_PENDING };
        HRESULT persistGetClassIdHResult{ E_PENDING };
        std::string persistClsid{ "UNKNOWN / NOT EXPOSED" };
        std::string encoderClsid{ "UNKNOWN / NOT EXPOSED" };
        std::string encoderFriendlyName{ "UNKNOWN / NOT EXPOSED" };
        bool friendlyNameAttributeExposed{};
        std::string encoderVendor{ "UNKNOWN / NOT EXPOSED" };
        std::string hardwareUrl{ "UNKNOWN / NOT EXPOSED" };
        bool hardwareUrlAttributeExposed{};
        std::string hardwareVendorId{ "UNKNOWN / NOT EXPOSED" };
        bool hardwareVendorIdAttributeExposed{};
        bool asyncMarkerExposed{};
        bool asyncMarker{};
        std::string hardwareEvidence{ "UNKNOWN / NOT EXPOSED" };
        std::string softwareEvidence{ "UNKNOWN / NOT EXPOSED" };
        std::string hardwareSoftwareVerdict{ "UNKNOWN" };
        HRESULT outputMediaTypeHResult{ E_PENDING };
        std::string outputProfile{ "UNKNOWN / NOT EXPOSED" };
        std::string outputLevel{ "UNKNOWN / NOT EXPOSED" };
        HRESULT codecApiHResult{ E_PENDING };
        bool codecApiAvailable{};
        std::array<
            VideoEncoderCodecPropertyDiagnostic,
            VideoEncoderCapabilityPropertyCapacity> properties{};
        std::size_t propertyCount{};
        std::string currentRateControlMode{ "UNKNOWN" };
        std::vector<std::string> supportedRateControlModes;
        std::string rateControlModeEvidence{ "N/A" };
        std::string qualityBasedVbrCandidate{ "UNKNOWN" };
        std::string qpCandidate{ "UNKNOWN" };
    };

    struct VideoEncoderDiagnostics
    {
        bool encoderEnabled{};
        std::wstring encoderSessionId;
        VideoEncoderState encoderState{ VideoEncoderState::Disabled };
        std::string stopReason{ "NotStarted" };
        std::wstring outputPath;
        bool outputSuccess{};
        std::uint32_t outputWidth{};
        std::uint32_t outputHeight{};
        std::string outputFormat{ "MP4/H264-NV12+AAC-PCM16-48K-STEREO" };
        std::uint32_t nominalFrameRateNumerator{
            VideoEncoderNominalFrameRateNumerator };
        std::uint32_t nominalFrameRateDenominator{
            VideoEncoderNominalFrameRateDenominator };
        std::int64_t nominalFrameDuration100ns{
            VideoEncoderNominalFrameDuration100ns };
        std::uint32_t bitrate{};
        std::uint32_t selectedFps{ VideoEncoderDefaultFrameRate };
        std::uint64_t outputTicks{};
        std::uint64_t submittedFrames{};
        std::uint64_t freshFrames{};
        std::uint64_t duplicatedFrames{};
        std::uint64_t cadenceDroppedSourceFrames{};
        std::uint64_t missedDeadlines{};
        bool videoSupportRequested{};
        bool videoSupportDeviceCreated{};
        bool multithreadProtectionAvailable{};
        bool multithreadProtectionEnabled{};
        bool videoProcessorInputSupported{};
        bool videoProcessorNv12OutputSupported{};
        std::string encoderIdentityStatus{ "Unknown" };
        std::string encoderFriendlyName;
        bool hardwareTransformRequested{ true };
        std::string hardwareTransformSelected{ "Unknown" };
        bool dxgiDeviceManagerBound{};
        bool productionHardwareEncoderRequired{};
        bool actualHardwareEncoderVerified{};
        bool softwareFallbackDetected{};
        bool softwareFallbackRejected{};
        HRESULT hardwareEncoderVerificationHResult{ E_PENDING };
        VideoEncoderCapabilityDiagnostics encoderCapabilities;
        VideoEncoderBitrateNegotiationDiagnostics bitrateNegotiation;
        std::string tapConsumerMode{ "Disabled" };
        std::uint64_t tapGenerationAtStart{};
        std::uint64_t tapGenerationAtEnd{};
        std::uint64_t inputFramesReceived{};
        std::uint64_t inputFramesRejected{};
        std::uint64_t framesDroppedTimestampMissing{};
        std::uint64_t framesDroppedTimestampRegression{};
        std::uint64_t framesDroppedOddGeometry{};
        std::uint64_t framesDroppedGenerationMismatch{};
        std::uint64_t framesDroppedNv12Starvation{};
        std::uint64_t framesConvertedToNv12{};
        std::uint64_t framesSubmittedToSinkWriter{};
        std::uint64_t framesRejectedBySinkWriter{};
        std::uint64_t pauseRequests{};
        std::uint64_t videoPauseAcks{};
        std::uint64_t resumeRequests{};
        std::uint64_t videoResumeAcks{};
        std::uint64_t pausedFramesDiscarded{};
        std::uint64_t staleResumeFramesDiscarded{};
        std::uint64_t lastPauseCutoffSequence{};
        std::uint64_t lastResumeCutoffSequence{};
        std::uint64_t firstResumedFrameSequence{};
        std::uint64_t audioPauseAcks{};
        std::uint64_t audioResumeAcks{};
        std::uint64_t audioPauseFifoClearCalls{};
        std::uint64_t audioInitialPauseClearCalls{};
        std::uint64_t audioPausedWakeClearCalls{};
        std::uint64_t audioFinalResumeClearCalls{};
        std::uint64_t audioFramesWrittenAtPause{};
        std::uint64_t audioFramesWrittenAtResume{};
        std::uint64_t audioPauseTerminalStopTransitions{};
        bool audioPauseDiscardGateActive{};
        std::uint64_t videoProcessorFailures{};
        std::uint64_t writeSampleFailures{};
        std::uint32_t nv12PoolSize{ VideoEncoderNv12PoolSize };
        std::uint32_t nv12PoolHighWatermark{};
        std::uint32_t nv12OutstandingCurrent{};
        std::uint32_t nv12OutstandingHighWatermark{};
        std::uint32_t nv12OutstandingAtStop{};
        std::uint64_t nv12PoolStarvation{};
        std::uint64_t trackedCallbackCount{};
        std::uint64_t trackedCallbackAfterStop{};
        std::uint64_t doubleReturnDetected{};
        std::uint64_t invalidStateTransitionDetected{};
        std::uint32_t trackedReturnTimeoutMs{};
        bool trackedReturnTimedOut{};
        std::int64_t firstInputTimestamp{};
        std::int64_t lastInputTimestamp{};
        std::int64_t firstSampleTime{};
        std::int64_t lastSampleTime{};
        std::int64_t sampleDurationMin{};
        std::int64_t sampleDurationMax{};
        std::string durationEstimateSource{ "Nominal60" };
        bool lastFrameDurationEstimated{ true };
        std::vector<double> writeSampleDurationsMs;
        std::string audioBackend{ "Disabled" };
        std::string audioMode{ "None" };
        HRESULT audioStartHResult{ E_PENDING };
        HRESULT audioStopHResult{ E_PENDING };
        bool audioCaptureStarted{};
        bool audioCaptureStopped{};
        std::uint64_t audioPcmBytesPulled{};
        std::uint64_t audioPcmFramesWritten{};
        std::uint64_t audioSamplesWritten{};
        std::uint64_t audioPaddingSamplesWritten{};
        std::uint64_t audioEmptySamplesSkipped{};
        std::string gStreamerAudioVersion{ "1.28.6" };
        std::string gStreamerAudioMode{ "None" };
        bool gStreamerSystemActive{};
        bool gStreamerMicrophoneActive{};
        std::wstring gStreamerMicrophoneDeviceId;
        std::wstring gStreamerMicrophoneDeviceDisplayName;
        std::wstring gStreamerMicrophoneDeviceProperties;
        std::wstring gStreamerMicrophoneElementDeviceId;
        bool gStreamerMicrophoneSessionBound{};
        bool gStreamerMicrophoneSourceCreatedFromDevice{};
        bool gStreamerMicrophoneElementIdentityMatches{};
        bool micDisconnectedDuringRecording{};
        bool gStreamerMicrophoneSourceDataBlocked{};
        std::string gStreamerPipelineState{ "Idle" };
        std::wstring gStreamerLastError;
        std::wstring gStreamerAudioWorkingPath;
        std::wstring gStreamerSystemWorkingPath;
        std::wstring gStreamerMicrophoneWorkingPath;
        HRESULT gStreamerTerminalHResult{ S_OK };
        bool gStreamerDeviceMonitorActive{};
        bool gStreamerEndOfStreamObserved{};
        bool gStreamerFilesClosed{};
        bool gStreamerBusThreadExited{};
        bool gStreamerMixerVolumesFixedAtUnity{};
        bool gStreamerDualSourcesIndependent{};
        std::uint32_t gStreamerValidatedSampleRate{};
        std::uint32_t gStreamerValidatedChannels{};
        std::uint64_t gStreamerDecodedAudioFrames{};
        std::uint32_t gStreamerAudioPeakAbsolutePcm16{};
        double gStreamerAudioRmsPcm16{};
        double gStreamerAudioDcPcm16{};
        std::uint64_t gStreamerAudioSaturatedSamples{};
        std::int64_t gStreamerValidatedAudioDuration100ns{};
        bool gStreamerValidatedAudioReachedEndOfStream{};
        double gStreamerFinalIntegratedLufs{};
        double gStreamerFinalTruePeakDbtp{};
        bool gStreamerFinalLoudnessValidated{};
        bool gStreamerMicrophoneMasteringApplied{};
        bool gStreamerDualMixApplied{};
        bool finalizeAttempted{};
        HRESULT finalizeHResult{ E_PENDING };
        double finalizeDurationMs{};
        double trackedReturnDurationMs{};
        bool outputFileExists{};
        std::uint64_t outputFileSize{};
        std::string sourceReaderValidation{ "NotRun" };
        std::string sourceReaderValidationMode{ "NotRun" };
        std::uint32_t validationSampleLimit{};
        std::uint64_t validationSamplesRead{};
        bool validationReachedEndOfStream{};
        double validationDurationMs{};
        std::uint64_t decodedFrameCount{};
        std::int64_t validatedFirstPts{};
        std::int64_t validatedLastPts{};
        std::int64_t validatedDuration100ns{};
        std::uint64_t leaseReturnCount{};
        std::uint64_t tapFramesObserved{};
        std::uint64_t tapFramesCopied{};
        std::uint64_t tapFramesEnqueued{};
        std::uint64_t tapFramesDroppedNoFreeSlot{};
        std::uint64_t tapFramesDroppedQueueFull{};
        std::uint64_t tapFramesDroppedGenerationMismatch{};
        std::uint64_t tapFramesDroppedDisabledOrStopping{};
        std::uint64_t tapFramesDroppedLockBusy{};
        std::uint32_t tapQueueDepthHighWatermark{};
        std::uint32_t bgraOutstandingAtStop{};
        std::uint32_t bgraOutstandingAtShutdown{};
        std::uint64_t generationChangeCount{};
        HRESULT deviceRemovedReason{ S_OK };
        double stopDurationMs{};
        double encoderJoinDurationMs{};
        bool consumerConflict{};
        std::uint32_t residualOutstandingAtShutdown{};
        HRESULT failureHResult{ S_OK };
        std::string failureStage;
        bool outputDeleteAttempted{};
        bool outputDeleteSucceeded{};
        HRESULT outputDeleteHResult{ S_OK };
        bool manifestEnabled{};
        std::wstring manifestPath;
        bool manifestCreated{};
        std::uint32_t manifestWriteAttempts{};
        std::uint32_t manifestWriteSuccesses{};
        std::uint32_t manifestWriteFailures{};
        std::uint64_t manifestLastPersistedRevision{};
        const char* manifestLastPersistedState{ "Unavailable" };
        HRESULT manifestFirstFailureHResult{ S_OK };
        HRESULT manifestLastFailureHResult{ S_OK };
        bool lifetimeOwnerAcquireAttempted{};
        bool lifetimeOwnerAcquired{};
        HRESULT lifetimeOwnerAcquireHResult{ E_PENDING };
        std::wstring lifetimeOwnerPath;
    };

    [[nodiscard]] const char* VideoEncoderStateName(
        VideoEncoderState state) noexcept;
    [[nodiscard]] bool IsVideoEncoderStateTransitionAllowed(
        VideoEncoderState from,
        VideoEncoderState to) noexcept;
    void WriteVideoEncoderSummary(
        const std::wstring& diagnosticDirectory,
        const VideoEncoderDiagnostics& diagnostics) noexcept;
    [[nodiscard]] std::string SerializeVideoEncoderCapabilities(
        const VideoEncoderDiagnostics& diagnostics) noexcept;
    [[nodiscard]] bool WriteVideoEncoderCapabilities(
        const std::wstring& sessionDirectory,
        const VideoEncoderDiagnostics& diagnostics) noexcept;
    [[nodiscard]] bool WriteVideoCadenceTrace(
        const std::wstring& sessionDirectory,
        const VideoCadenceTraceBuffer& trace) noexcept;
}
