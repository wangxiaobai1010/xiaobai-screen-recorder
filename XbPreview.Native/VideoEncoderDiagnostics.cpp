#include "VideoEncoderDiagnostics.h"

#include <algorithm>
#include <filesystem>
#include <fstream>
#include <iomanip>
#include <sstream>
#include <utility>

namespace xbpreview
{
    namespace
    {
        std::string Utf8(const std::wstring& value)
        {
            if (value.empty())
            {
                return {};
            }
            const auto size = WideCharToMultiByte(
                CP_UTF8, 0, value.data(), static_cast<int>(value.size()),
                nullptr, 0, nullptr, nullptr);
            if (size <= 0)
            {
                return {};
            }
            std::string result(static_cast<std::size_t>(size), '\0');
            WideCharToMultiByte(
                CP_UTF8, 0, value.data(), static_cast<int>(value.size()),
                result.data(), size, nullptr, nullptr);
            return result;
        }

        std::string JsonEscape(const std::string& value)
        {
            std::ostringstream stream;
            for (const unsigned char character : value)
            {
                switch (character)
                {
                case '"': stream << "\\\""; break;
                case '\\': stream << "\\\\"; break;
                case '\n': stream << "\\n"; break;
                case '\r': stream << "\\r"; break;
                case '\t': stream << "\\t"; break;
                default:
                    if (character < 0x20)
                    {
                        stream << "\\u" << std::hex << std::setw(4)
                            << std::setfill('0') << static_cast<int>(character);
                    }
                    else
                    {
                        stream << character;
                    }
                }
            }
            return stream.str();
        }

        std::string HResultText(const HRESULT value)
        {
            std::ostringstream stream;
            stream << "0x" << std::uppercase << std::hex << std::setw(8)
                << std::setfill('0') << static_cast<std::uint32_t>(value);
            return stream.str();
        }

        std::string BoundedCapabilityText(const std::string& value)
        {
            if (value.size() <= VideoEncoderCapabilityTextLimit)
            {
                return value;
            }
            return value.substr(0, VideoEncoderCapabilityTextLimit);
        }

        void WriteCapabilityText(
            std::ostream& stream,
            const std::string& value)
        {
            stream << '"' << JsonEscape(BoundedCapabilityText(value)) << '"';
        }

        void WriteMediaTypeDiagnostic(
            std::ostream& stream,
            const VideoEncoderMediaTypeDiagnostic& value)
        {
            stream << "{\"QueryAttempted\":"
                << (value.queryAttempted ? "true" : "false")
                << ",\"QueryHResult\":\"" << HResultText(value.queryHResult)
                << "\",\"MajorType\":{\"Value\":";
            WriteCapabilityText(stream, value.majorType);
            stream << ",\"HResult\":\""
                << HResultText(value.majorTypeHResult)
                << "\"},\"Subtype\":{\"Value\":";
            WriteCapabilityText(stream, value.subtype);
            stream << ",\"HResult\":\""
                << HResultText(value.subtypeHResult)
                << "\"},\"FrameSize\":{\"Width\":" << value.width
                << ",\"Height\":" << value.height
                << ",\"HResult\":\"" << HResultText(value.frameSizeHResult)
                << "\"},\"FrameRate\":{\"Numerator\":"
                << value.frameRateNumerator
                << ",\"Denominator\":" << value.frameRateDenominator
                << ",\"HResult\":\"" << HResultText(value.frameRateHResult)
                << "\"},\"AverageBitrate\":{\"Value\":"
                << value.averageBitrate
                << ",\"Present\":"
                << (SUCCEEDED(value.averageBitrateHResult) ? "true" : "false")
                << ",\"HResult\":\""
                << HResultText(value.averageBitrateHResult)
                << "\"},\"Mpeg2Profile\":{\"Value\":"
                << value.mpeg2Profile
                << ",\"Name\":";
            WriteCapabilityText(stream, value.mpeg2ProfileName);
            stream << ",\"Present\":"
                << (SUCCEEDED(value.mpeg2ProfileHResult) ? "true" : "false")
                << ",\"HResult\":\""
                << HResultText(value.mpeg2ProfileHResult) << "\"}}";
        }

        void WriteCodecApiReadback(
            std::ostream& stream,
            const VideoEncoderCodecApiReadbackDiagnostic& value)
        {
            stream << "{\"QueryAttempted\":"
                << (value.queryAttempted ? "true" : "false")
                << ",\"CodecApiHResult\":\""
                << HResultText(value.codecApiHResult)
                << "\",\"RateControl\":{\"Value\":";
            WriteCapabilityText(stream, value.rateControlValue);
            stream << ",\"Name\":";
            WriteCapabilityText(stream, value.rateControlName);
            stream << ",\"HResult\":\""
                << HResultText(value.rateControlHResult)
                << "\"},\"MeanBitrate\":{\"Value\":";
            WriteCapabilityText(stream, value.meanBitrate);
            stream << ",\"HResult\":\""
                << HResultText(value.meanBitrateHResult)
                << "\"},\"MaxBitrate\":{\"Value\":";
            WriteCapabilityText(stream, value.maxBitrate);
            stream << ",\"HResult\":\""
                << HResultText(value.maxBitrateHResult) << "\"}}";
        }

        void WriteBitrateNegotiation(
            std::ostream& stream,
            const VideoEncoderBitrateNegotiationDiagnostics& value)
        {
            stream << ",\"BitrateNegotiation\":{\"LifecycleMap\":{"
                << "\"SinkWriterCreated\":"
                << (value.sinkWriterCreated ? "true" : "false")
                << ",\"OutputMediaTypeCreated\":"
                << (value.outputMediaTypeCreated ? "true" : "false")
                << ",\"RequestedOutputCapturedBeforeAddStream\":"
                << (value.requestedOutputCapturedBeforeAddStream
                    ? "true" : "false")
                << ",\"AddStreamSucceeded\":"
                << (value.addStreamSucceeded ? "true" : "false")
                << ",\"SetInputMediaTypeHRESULT\":\""
                << HResultText(value.setInputMediaTypeHResult) << '"'
                << ",\"SetInputMediaTypeSucceeded\":"
                << (value.setInputMediaTypeSucceeded ? "true" : "false")
                << ",\"ActualTransformAvailablePreBegin\":"
                << (value.actualTransformAvailablePreBegin ? "true" : "false")
                << ",\"ActualTransformFirstAvailableAt\":";
            WriteCapabilityText(stream, value.actualTransformFirstAvailableAt);
            stream << ",\"BeginWritingSucceeded\":"
                << (value.beginWritingSucceeded ? "true" : "false")
                << ",\"FirstVideoSampleWritten\":"
                << (value.firstVideoSampleWritten ? "true" : "false")
                << "},\"RequestedMediaType\":";
            WriteMediaTypeDiagnostic(stream, value.requestedOutput);
            stream << ",\"NegotiatedMediaType\":{\"PreBegin\":{\"Input\":";
            WriteMediaTypeDiagnostic(stream, value.actualInputPreBegin);
            stream << ",\"Output\":";
            WriteMediaTypeDiagnostic(stream, value.actualOutputPreBegin);
            stream << "},\"PostBegin\":{\"Input\":";
            WriteMediaTypeDiagnostic(stream, value.actualInputPostBegin);
            stream << ",\"Output\":";
            WriteMediaTypeDiagnostic(stream, value.actualOutputPostBegin);
            stream << "}},\"CodecApiPreBegin\":";
            WriteCodecApiReadback(stream, value.codecApiPreBegin);
            stream << ",\"CodecApiPostBegin\":";
            WriteCodecApiReadback(stream, value.codecApiPostBegin);
            stream << ",\"CodecApiPostFirstSample\":";
            WriteCodecApiReadback(stream, value.codecApiPostFirstSample);
            stream << ",\"EncoderConfigStore\":{"
                << "\"ApiAvailable\":"
                << (value.encoderConfigStoreApiAvailable ? "true" : "false")
                << ",\"CurrentCodeUsesIt\":"
                << (value.currentCodeUsesEncoderConfigStore ? "true" : "false")
                << ",\"CurrentCodeOnlyUsesMediaTypeBitrate\":"
                << (value.currentCodeOnlyUsesMediaTypeBitrate
                    ? "true" : "false")
                << ",\"ModifiableNoProvesInitNotConfigurable\":"
                << (value.modifiableNoProvesInitNotConfigurable
                    ? "true" : "false")
                << ",\"RequestedRateControl\":";
            WriteCapabilityText(stream, value.requestedRateControl);
            stream << ",\"RequestedMeanBitRate\":"
                << value.requestedMeanBitrate
                << ",\"StoreCreationHRESULT\":\""
                << HResultText(value.storeCreationHResult)
                << "\",\"RateControlPropertySetHRESULT\":\""
                << HResultText(value.rateControlPropertySetHResult)
                << "\",\"MeanBitRatePropertySetHRESULT\":\""
                << HResultText(value.meanBitratePropertySetHResult)
                << "\",\"SinkWriterConfigAttachHRESULT\":\""
                << HResultText(value.sinkWriterConfigAttachHResult)
                << '"'
                << "},\"InputMediaTypeEncodingParameters\":{"
                << "\"Used\":"
                << (value.inputMediaTypeEncodingParametersUsed
                    ? "true" : "false")
                << ",\"RateControlPropertySetHRESULT\":\""
                << HResultText(value.inputParametersRateControlSetHResult)
                << "\",\"MeanBitRatePropertySetHRESULT\":\""
                << HResultText(value.inputParametersMeanBitrateSetHResult)
                << "\"}}";
        }

        double Percentile(std::vector<double> values, const double percentile)
        {
            if (values.empty())
            {
                return 0.0;
            }
            std::sort(values.begin(), values.end());
            const auto position = percentile * (values.size() - 1);
            const auto lower = static_cast<std::size_t>(position);
            const auto upper = (std::min)(lower + 1, values.size() - 1);
            const auto fraction = position - lower;
            return values[lower] + (values[upper] - values[lower]) * fraction;
        }

        const char* VideoCadenceDecisionName(
            const VideoCadenceDecision decision) noexcept
        {
            switch (decision)
            {
            case VideoCadenceDecision::Fresh: return "FRESH";
            case VideoCadenceDecision::Duplicate: return "DUPLICATE";
            case VideoCadenceDecision::NoFrame: return "NO_FRAME";
            case VideoCadenceDecision::Missed: return "MISSED";
            default: return "NO_FRAME";
            }
        }

        const char* VideoCadenceDuplicateClassificationName(
            const VideoCadenceDuplicateClassification classification) noexcept
        {
            switch (classification)
            {
            case VideoCadenceDuplicateClassification::
                PendingFutureArrivalEvidence:
                return "PENDING_FUTURE_ARRIVAL_EVIDENCE";
            case VideoCadenceDuplicateClassification::NormalSourceTickJitter:
                return "NORMAL_SOURCE_TICK_JITTER";
            case VideoCadenceDuplicateClassification::AvoidableHandoffLoss:
                return "AVOIDABLE_HANDOFF_LOSS";
            case VideoCadenceDuplicateClassification::NotDuplicate:
            default:
                return "NOT_DUPLICATE";
            }
        }

        double EventRate(
            const std::uint64_t count,
            const std::int64_t firstQpc,
            const std::int64_t lastQpc,
            const std::int64_t qpcFrequency) noexcept
        {
            if (count < 2 || firstQpc <= 0 || lastQpc <= firstQpc ||
                qpcFrequency <= 0)
            {
                return 0.0;
            }
            return static_cast<double>(count - 1) *
                static_cast<double>(qpcFrequency) /
                static_cast<double>(lastQpc - firstQpc);
        }
    }

    void VideoCadenceTraceBuffer::Reset(
        const std::int64_t performanceCounterFrequency) noexcept
    {
        records.fill(VideoCadenceTraceRecord{});
        unresolvedDuplicates.fill(VideoCadenceUnresolvedDuplicate{});
        totalTicks = 0;
        recordCount = 0;
        unresolvedHead = 0;
        unresolvedCount = 0;
        traceRecordsOverwritten = 0;
        unresolvedDuplicateOverflows = 0;
        fresh = 0;
        duplicate = 0;
        noFrame = 0;
        missed = 0;
        duplicateWithNoNewSourceAvailable = 0;
        duplicateDespiteFreshAvailableBeforeDeadline = 0;
        normalMultiSourceCadenceDrops = 0;
        dropThenNextTickDuplicateCount = 0;
        totalSourceArrivals = 0;
        sourceArrivalsSincePreviousTick = 0;
        pendingReplacementsSincePreviousTick = 0;
        cadenceDropsSincePreviousTick = 0;
        previousTickCadenceDrops = 0;
        qpcFrequency = performanceCounterFrequency;
        firstSourceArrivalQpc = 0;
        lastSourceArrivalQpc = 0;
        firstFreshOutputQpc = 0;
        lastFreshOutputQpc = 0;
        deadlineErrorSumUs = 0;
        maximumDeadlineErrorUs = 0;
    }

    void VideoCadenceTraceBuffer::ObserveSourceArrival(
        const std::uint64_t sequence,
        const std::int64_t sourceTimestamp100ns,
        const std::int64_t enqueueQpc) noexcept
    {
        ++totalSourceArrivals;
        ++sourceArrivalsSincePreviousTick;
        if (enqueueQpc > 0)
        {
            if (firstSourceArrivalQpc == 0)
            {
                firstSourceArrivalQpc = enqueueQpc;
            }
            lastSourceArrivalQpc = enqueueQpc;
        }

        while (unresolvedCount > 0)
        {
            const auto unresolved = unresolvedDuplicates[unresolvedHead];
            if (sequence <= unresolved.lastSubmittedFreshSequence)
            {
                break;
            }
            const bool availableBeforeDeadline =
                enqueueQpc > 0 && unresolved.scheduledDeadlineQpc > 0 &&
                enqueueQpc <= unresolved.scheduledDeadlineQpc;
            const auto classification = availableBeforeDeadline
                ? VideoCadenceDuplicateClassification::AvoidableHandoffLoss
                : VideoCadenceDuplicateClassification::NormalSourceTickJitter;

            const auto retainedStart = totalTicks - recordCount;
            if (unresolved.recordOrdinal >= retainedStart &&
                unresolved.recordOrdinal < totalTicks)
            {
                auto& record = records[
                    unresolved.recordOrdinal % VideoCadenceTraceCapacity];
                if (record.recordOrdinal == unresolved.recordOrdinal)
                {
                    record.duplicateClassification = classification;
                    if (availableBeforeDeadline)
                    {
                        record.freshAvailableBeforeDeadline = true;
                        record.freshAvailableSequenceBeforeDeadline = sequence;
                        record.freshAvailableSourceTimestamp100ns =
                            sourceTimestamp100ns;
                        record.freshAvailableEnqueueQpc = enqueueQpc;
                    }
                }
            }
            if (availableBeforeDeadline)
            {
                ++duplicateDespiteFreshAvailableBeforeDeadline;
            }
            else
            {
                ++duplicateWithNoNewSourceAvailable;
            }
            unresolvedDuplicates[unresolvedHead] = {};
            unresolvedHead =
                (unresolvedHead + 1) % VideoCadenceTraceCapacity;
            --unresolvedCount;
        }
    }

    void VideoCadenceTraceBuffer::ObservePendingReplacement() noexcept
    {
        ++pendingReplacementsSincePreviousTick;
        ++cadenceDropsSincePreviousTick;
        ++normalMultiSourceCadenceDrops;
    }

    void VideoCadenceTraceBuffer::RecordTick(
        VideoCadenceTraceRecord record) noexcept
    {
        record.recordOrdinal = totalTicks;
        record.sourceArrivalsSincePreviousTick =
            sourceArrivalsSincePreviousTick;
        record.pendingReplacementsSincePreviousTick =
            pendingReplacementsSincePreviousTick;
        record.cadenceDropsSincePreviousTick =
            cadenceDropsSincePreviousTick;
        record.dropThenNextTickDuplicate =
            previousTickCadenceDrops > 0 &&
            record.decision == VideoCadenceDecision::Duplicate;

        const auto slot = static_cast<std::size_t>(
            totalTicks % VideoCadenceTraceCapacity);
        records[slot] = record;
        if (recordCount < VideoCadenceTraceCapacity)
        {
            ++recordCount;
        }
        else
        {
            ++traceRecordsOverwritten;
        }
        ++totalTicks;

        deadlineErrorSumUs += record.deadlineErrorUs;
        maximumDeadlineErrorUs = (std::max)(
            maximumDeadlineErrorUs, record.deadlineErrorUs);
        switch (record.decision)
        {
        case VideoCadenceDecision::Fresh:
            ++fresh;
            if (record.actualWakeQpc > 0)
            {
                if (firstFreshOutputQpc == 0)
                {
                    firstFreshOutputQpc = record.actualWakeQpc;
                }
                lastFreshOutputQpc = record.actualWakeQpc;
            }
            break;
        case VideoCadenceDecision::Duplicate:
            ++duplicate;
            if (record.dropThenNextTickDuplicate)
            {
                ++dropThenNextTickDuplicateCount;
            }
            if (record.freshAvailableBeforeDeadline)
            {
                records[slot].duplicateClassification =
                    VideoCadenceDuplicateClassification::AvoidableHandoffLoss;
                ++duplicateDespiteFreshAvailableBeforeDeadline;
            }
            else
            {
                if (unresolvedCount == VideoCadenceTraceCapacity)
                {
                    const auto overflowed =
                        unresolvedDuplicates[unresolvedHead];
                    const auto retainedStart = totalTicks - recordCount;
                    if (overflowed.recordOrdinal >= retainedStart &&
                        overflowed.recordOrdinal < totalTicks)
                    {
                        auto& overflowedRecord = records[
                            overflowed.recordOrdinal %
                                VideoCadenceTraceCapacity];
                        if (overflowedRecord.recordOrdinal ==
                            overflowed.recordOrdinal)
                        {
                            overflowedRecord.duplicateClassification =
                                VideoCadenceDuplicateClassification::
                                    NormalSourceTickJitter;
                        }
                    }
                    ++duplicateWithNoNewSourceAvailable;
                    ++unresolvedDuplicateOverflows;
                    unresolvedHead =
                        (unresolvedHead + 1) % VideoCadenceTraceCapacity;
                    --unresolvedCount;
                }
                records[slot].duplicateClassification =
                    VideoCadenceDuplicateClassification::
                        PendingFutureArrivalEvidence;
                const auto tail = (unresolvedHead + unresolvedCount) %
                    VideoCadenceTraceCapacity;
                unresolvedDuplicates[tail] = {
                    record.recordOrdinal,
                    record.lastSubmittedFreshSequence,
                    record.scheduledDeadlineQpc
                };
                ++unresolvedCount;
            }
            break;
        case VideoCadenceDecision::Missed:
            ++missed;
            break;
        case VideoCadenceDecision::NoFrame:
        default:
            ++noFrame;
            break;
        }

        previousTickCadenceDrops = record.cadenceDropsSincePreviousTick;
        sourceArrivalsSincePreviousTick = 0;
        pendingReplacementsSincePreviousTick = 0;
        cadenceDropsSincePreviousTick = 0;
    }

    void VideoCadenceTraceBuffer::FinalizeDuplicateClassifications() noexcept
    {
        while (unresolvedCount > 0)
        {
            const auto unresolved = unresolvedDuplicates[unresolvedHead];
            const auto retainedStart = totalTicks - recordCount;
            if (unresolved.recordOrdinal >= retainedStart &&
                unresolved.recordOrdinal < totalTicks)
            {
                auto& record = records[
                    unresolved.recordOrdinal % VideoCadenceTraceCapacity];
                if (record.recordOrdinal == unresolved.recordOrdinal)
                {
                    record.duplicateClassification =
                        VideoCadenceDuplicateClassification::
                            NormalSourceTickJitter;
                }
            }
            ++duplicateWithNoNewSourceAvailable;
            unresolvedDuplicates[unresolvedHead] = {};
            unresolvedHead =
                (unresolvedHead + 1) % VideoCadenceTraceCapacity;
            --unresolvedCount;
        }
    }

    const VideoCadenceTraceRecord* VideoCadenceTraceBuffer::FindRecord(
        const std::uint64_t recordOrdinal) const noexcept
    {
        const auto retainedStart = totalTicks - recordCount;
        if (recordOrdinal < retainedStart || recordOrdinal >= totalTicks)
        {
            return nullptr;
        }
        const auto& record = records[
            recordOrdinal % VideoCadenceTraceCapacity];
        return record.recordOrdinal == recordOrdinal ? &record : nullptr;
    }

    const char* VideoEncoderStateName(const VideoEncoderState state) noexcept
    {
        switch (state)
        {
        case VideoEncoderState::Disabled: return "Disabled";
        case VideoEncoderState::Starting: return "Starting";
        case VideoEncoderState::Running: return "Running";
        case VideoEncoderState::Stopping: return "Stopping";
        case VideoEncoderState::Finalizing: return "Finalizing";
        case VideoEncoderState::Completed: return "Completed";
        case VideoEncoderState::Failed: return "Failed";
        case VideoEncoderState::Unsupported: return "Unsupported";
        case VideoEncoderState::UserCancelled: return "UserCancelled";
        default: return "Unknown";
        }
    }

    bool IsVideoEncoderStateTransitionAllowed(
        const VideoEncoderState from,
        const VideoEncoderState to) noexcept
    {
        if (from == to)
        {
            return true;
        }
        switch (from)
        {
        case VideoEncoderState::Disabled:
            return to == VideoEncoderState::Starting;
        case VideoEncoderState::Starting:
            return to == VideoEncoderState::Running ||
                to == VideoEncoderState::Failed ||
                to == VideoEncoderState::Unsupported;
        case VideoEncoderState::Running:
            return to == VideoEncoderState::Stopping ||
                to == VideoEncoderState::Failed;
        case VideoEncoderState::Stopping:
            return to == VideoEncoderState::Finalizing ||
                to == VideoEncoderState::UserCancelled ||
                to == VideoEncoderState::Failed;
        case VideoEncoderState::Finalizing:
            return to == VideoEncoderState::Completed ||
                to == VideoEncoderState::UserCancelled ||
                to == VideoEncoderState::Failed;
        case VideoEncoderState::Completed:
        case VideoEncoderState::Failed:
        case VideoEncoderState::Unsupported:
        case VideoEncoderState::UserCancelled:
        default:
            return false;
        }
    }

    std::string SerializeVideoEncoderCapabilities(
        const VideoEncoderDiagnostics& d) noexcept
    {
        try
        {
            const auto& c = d.encoderCapabilities;
            std::ostringstream stream;
            stream << "{\"SchemaVersion\":2"
                << ",\"ProbeMode\":\"READ_ONLY\""
                << ",\"SerializationStatus\":\"COMPLETE\""
                << ",\"SessionContext\":{\"SelectedFps\":" << d.selectedFps
                << ",\"OutputWidth\":" << d.outputWidth
                << ",\"OutputHeight\":" << d.outputHeight
                << ",\"NominalBitrate\":" << d.bitrate << '}'
                << ",\"HardwareEnforcement\":{\"Required\":"
                << (d.productionHardwareEncoderRequired ? "true" : "false")
                << ",\"Verified\":"
                << (d.actualHardwareEncoderVerified ? "true" : "false")
                << ",\"SoftwareFallbackDetected\":"
                << (d.softwareFallbackDetected ? "true" : "false")
                << ",\"SoftwareFallbackRejected\":"
                << (d.softwareFallbackRejected ? "true" : "false")
                << ",\"HResult\":\""
                << HResultText(d.hardwareEncoderVerificationHResult) << "\"}"
                << ",\"ProbeStatus\":{\"Attempted\":"
                << (c.probeAttempted ? "true" : "false")
                << ",\"HResult\":\"" << HResultText(c.probeHResult) << '"'
                << ",\"SinkWriterExAvailable\":"
                << (c.sinkWriterExAvailable ? "true" : "false")
                << ",\"SinkWriterExHResult\":\""
                << HResultText(c.sinkWriterExHResult) << '"'
                << ",\"ActualTransformObtained\":"
                << (c.actualTransformObtained ? "true" : "false")
                << ",\"TransformIndex\":" << c.transformIndex
                << ",\"TransformCategory\":";
            WriteCapabilityText(stream, c.transformCategory);
            stream << '}'
                << ",\"Identity\":{\"Clsid\":";
            WriteCapabilityText(stream, c.encoderClsid);
            stream << ",\"FriendlyName\":";
            WriteCapabilityText(stream, c.encoderFriendlyName);
            stream << ",\"Vendor\":";
            WriteCapabilityText(stream, c.encoderVendor);
            stream << ",\"HardwareUrl\":";
            WriteCapabilityText(stream, c.hardwareUrl);
            stream << ",\"HardwareVendorId\":";
            WriteCapabilityText(stream, c.hardwareVendorId);
            stream << ",\"AsyncMarkerExposed\":"
                << (c.asyncMarkerExposed ? "true" : "false")
                << ",\"AsyncMarker\":" << (c.asyncMarker ? "true" : "false")
                << ",\"HardwareEvidence\":";
            WriteCapabilityText(stream, c.hardwareEvidence);
            stream << ",\"SoftwareEvidence\":";
            WriteCapabilityText(stream, c.softwareEvidence);
            stream << ",\"HardwareSoftwareVerdict\":";
            WriteCapabilityText(stream, c.hardwareSoftwareVerdict);
            stream << ",\"TransformAttributesHResult\":\""
                << HResultText(c.transformAttributesHResult) << '"'
                << ",\"OutputMediaTypeHResult\":\""
                << HResultText(c.outputMediaTypeHResult) << '"'
                << ",\"OutputProfile\":";
            WriteCapabilityText(stream, c.outputProfile);
            stream << ",\"OutputLevel\":";
            WriteCapabilityText(stream, c.outputLevel);
            stream << '}'
                << ",\"ICodecAPI\":{\"Available\":"
                << (c.codecApiAvailable ? "true" : "false")
                << ",\"HResult\":\"" << HResultText(c.codecApiHResult) << '"'
                << ",\"CurrentRateControlMode\":";
            WriteCapabilityText(stream, c.currentRateControlMode);
            stream << ",\"SupportedRateControlModes\":[";
            const auto modeCount = (std::min)(
                c.supportedRateControlModes.size(),
                VideoEncoderCapabilityPossibleValueLimit);
            for (std::size_t index = 0; index < modeCount; ++index)
            {
                if (index != 0)
                {
                    stream << ',';
                }
                WriteCapabilityText(stream, c.supportedRateControlModes[index]);
            }
            stream << "]"
                << ",\"RateControlModeEvidence\":";
            WriteCapabilityText(stream, c.rateControlModeEvidence);
            stream << '}'
                << ",\"Candidates\":{\"QualityBasedVbr\":";
            WriteCapabilityText(stream, c.qualityBasedVbrCandidate);
            stream << ",\"Qp\":";
            WriteCapabilityText(stream, c.qpCandidate);
            stream << '}'
                << ",\"Properties\":[";
            const auto propertyCount = (std::min)(
                c.propertyCount,
                VideoEncoderCapabilityPropertyCapacity);
            for (std::size_t index = 0; index < propertyCount; ++index)
            {
                if (index != 0)
                {
                    stream << ',';
                }
                const auto& property = c.properties[index];
                stream << "{\"Property\":";
                WriteCapabilityText(stream, property.property);
                stream << ",\"IsSupported\":"
                    << (property.isSupported ? "true" : "false")
                    << ",\"IsSupportedHResult\":\""
                    << HResultText(property.isSupportedHResult) << '"'
                    << ",\"IsModifiable\":"
                    << (property.isModifiable ? "true" : "false")
                    << ",\"IsModifiableHResult\":\""
                    << HResultText(property.isModifiableHResult) << '"'
                    << ",\"CurrentValue\":";
                WriteCapabilityText(stream, property.currentValue);
                stream << ",\"CurrentValueHResult\":\""
                    << HResultText(property.currentValueHResult) << '"'
                    << ",\"Range\":{\"Minimum\":";
                WriteCapabilityText(stream, property.rangeMinimum);
                stream << ",\"Maximum\":";
                WriteCapabilityText(stream, property.rangeMaximum);
                stream << ",\"Step\":";
                WriteCapabilityText(stream, property.rangeStep);
                stream << ",\"HResult\":\""
                    << HResultText(property.rangeHResult) << "\"}"
                    << ",\"PossibleValues\":[";
                const auto valueCount = (std::min)(
                    property.possibleValues.size(),
                    VideoEncoderCapabilityPossibleValueLimit);
                for (std::size_t valueIndex = 0;
                    valueIndex < valueCount; ++valueIndex)
                {
                    if (valueIndex != 0)
                    {
                        stream << ',';
                    }
                    WriteCapabilityText(
                        stream, property.possibleValues[valueIndex]);
                }
                stream << "]"
                    << ",\"PossibleValuesHResult\":\""
                    << HResultText(property.possibleValuesHResult) << '"'
                    << ",\"PossibleValuesTruncated\":"
                    << ((property.possibleValuesTruncated ||
                        property.possibleValues.size() >
                            VideoEncoderCapabilityPossibleValueLimit)
                        ? "true" : "false")
                    << '}';
            }
            stream << ']';
            WriteBitrateNegotiation(stream, d.bitrateNegotiation);
            stream << '}';
            auto payload = stream.str();
            if (payload.size() <= VideoEncoderCapabilityJsonByteLimit)
            {
                return payload;
            }
            std::ostringstream bounded;
            bounded << "{\"SchemaVersion\":2"
                << ",\"ProbeMode\":\"READ_ONLY\""
                << ",\"SerializationStatus\":\"BOUNDED_OVERFLOW\""
                << ",\"SessionContext\":{\"SelectedFps\":" << d.selectedFps
                << ",\"OutputWidth\":" << d.outputWidth
                << ",\"OutputHeight\":" << d.outputHeight
                << ",\"NominalBitrate\":" << d.bitrate << '}';
            bounded << ",\"ProbeStatus\":{\"Attempted\":"
                << (c.probeAttempted ? "true" : "false")
                << ",\"HResult\":\"" << HResultText(c.probeHResult)
                << "\",\"ActualTransformObtained\":"
                << (c.actualTransformObtained ? "true" : "false")
                << '}';
            WriteBitrateNegotiation(bounded, d.bitrateNegotiation);
            bounded << '}';
            return bounded.str();
        }
        catch (...)
        {
            return {};
        }
    }

    bool WriteVideoEncoderCapabilities(
        const std::wstring& sessionDirectory,
        const VideoEncoderDiagnostics& diagnostics) noexcept
    {
        if (sessionDirectory.empty() ||
            !diagnostics.encoderCapabilities.probeAttempted)
        {
            return false;
        }
        try
        {
            const auto directory = std::filesystem::path(sessionDirectory);
            if (!std::filesystem::is_directory(directory))
            {
                return false;
            }
            const auto payload = SerializeVideoEncoderCapabilities(diagnostics);
            if (payload.empty() ||
                payload.size() > VideoEncoderCapabilityJsonByteLimit)
            {
                return false;
            }
            std::ofstream stream(
                directory / L"video-encoder-capabilities.json",
                std::ios::out | std::ios::trunc | std::ios::binary);
            if (!stream)
            {
                return false;
            }
            stream.write(payload.data(), static_cast<std::streamsize>(payload.size()));
            stream.put('\n');
            return stream.good();
        }
        catch (...)
        {
            return false;
        }
    }

    void WriteVideoEncoderSummary(
        const std::wstring& diagnosticDirectory,
        const VideoEncoderDiagnostics& d) noexcept
    {
        if (diagnosticDirectory.empty() || d.encoderSessionId.empty())
        {
            return;
        }
        try
        {
            std::filesystem::create_directories(diagnosticDirectory);
            const auto path = std::filesystem::path(diagnosticDirectory) /
                (L"p2.4-encoder-" + d.encoderSessionId + L".jsonl");
            std::ofstream stream(path, std::ios::out | std::ios::trunc);
            if (!stream)
            {
                return;
            }
            const auto writeP95 = Percentile(d.writeSampleDurationsMs, 0.95);
            const auto writeP50 = Percentile(d.writeSampleDurationsMs, 0.50);
            const auto writeMax = d.writeSampleDurationsMs.empty() ? 0.0 :
                *std::max_element(
                    d.writeSampleDurationsMs.begin(),
                    d.writeSampleDurationsMs.end());
            stream << std::fixed << std::setprecision(3)
                << "{\"event\":\"p2.4-encoder-summary\""
                << ",\"EncoderEnabled\":" << (d.encoderEnabled ? 1 : 0)
                << ",\"EncoderSessionId\":\"" << JsonEscape(Utf8(d.encoderSessionId)) << '"'
                << ",\"EncoderState\":\"" << VideoEncoderStateName(d.encoderState) << '"'
                << ",\"StopReason\":\"" << JsonEscape(d.stopReason) << '"'
                << ",\"OutputPath\":\"" << JsonEscape(Utf8(d.outputPath)) << '"'
                << ",\"OutputSuccess\":" << (d.outputSuccess ? 1 : 0)
                << ",\"OutputWidth\":" << d.outputWidth
                << ",\"OutputHeight\":" << d.outputHeight
                << ",\"OutputFormat\":\"" << d.outputFormat << '"'
                << ",\"NominalFrameRateNumerator\":" << d.nominalFrameRateNumerator
                << ",\"NominalFrameRateDenominator\":" << d.nominalFrameRateDenominator
                << ",\"NominalFrameDuration100ns\":" << d.nominalFrameDuration100ns
                << ",\"Bitrate\":" << d.bitrate
                << ",\"SelectedFps\":" << d.selectedFps
                << ",\"OutputTicks\":" << d.outputTicks
                << ",\"SubmittedFrames\":" << d.submittedFrames
                << ",\"FreshFrames\":" << d.freshFrames
                << ",\"DuplicatedFrames\":" << d.duplicatedFrames
                << ",\"CadenceDroppedSourceFrames\":"
                << d.cadenceDroppedSourceFrames
                << ",\"MissedDeadlines\":" << d.missedDeadlines
                << ",\"VideoSupportRequested\":" << (d.videoSupportRequested ? 1 : 0)
                << ",\"VideoSupportDeviceCreated\":" << (d.videoSupportDeviceCreated ? 1 : 0)
                << ",\"MultithreadProtectionAvailable\":" << (d.multithreadProtectionAvailable ? 1 : 0)
                << ",\"MultithreadProtectionEnabled\":" << (d.multithreadProtectionEnabled ? 1 : 0)
                << ",\"VideoProcessorInputSupported\":" << (d.videoProcessorInputSupported ? 1 : 0)
                << ",\"VideoProcessorNv12OutputSupported\":" << (d.videoProcessorNv12OutputSupported ? 1 : 0)
                << ",\"EncoderIdentityStatus\":\"" << d.encoderIdentityStatus << '"'
                << ",\"EncoderFriendlyName\":\"" << JsonEscape(d.encoderFriendlyName) << '"'
                << ",\"HardwareTransformRequested\":" << (d.hardwareTransformRequested ? 1 : 0)
                << ",\"HardwareTransformSelected\":\"" << d.hardwareTransformSelected << '"'
                << ",\"DxgiDeviceManagerBound\":" << (d.dxgiDeviceManagerBound ? 1 : 0)
                << ",\"ProductionHardwareEncoderRequired\":"
                << (d.productionHardwareEncoderRequired ? 1 : 0)
                << ",\"ActualHardwareEncoderVerified\":"
                << (d.actualHardwareEncoderVerified ? 1 : 0)
                << ",\"SoftwareFallbackDetected\":"
                << (d.softwareFallbackDetected ? 1 : 0)
                << ",\"SoftwareFallbackRejected\":"
                << (d.softwareFallbackRejected ? 1 : 0)
                << ",\"HardwareEncoderVerificationHResult\":\""
                << HResultText(d.hardwareEncoderVerificationHResult) << '"'
                << ",\"TapConsumerMode\":\"" << d.tapConsumerMode << '"'
                << ",\"TapGenerationAtStart\":" << d.tapGenerationAtStart
                << ",\"TapGenerationAtEnd\":" << d.tapGenerationAtEnd
                << ",\"InputFramesReceived\":" << d.inputFramesReceived
                << ",\"InputFramesRejected\":" << d.inputFramesRejected
                << ",\"FramesDroppedTimestampMissing\":" << d.framesDroppedTimestampMissing
                << ",\"FramesDroppedTimestampRegression\":" << d.framesDroppedTimestampRegression
                << ",\"FramesDroppedOddGeometry\":" << d.framesDroppedOddGeometry
                << ",\"FramesDroppedGenerationMismatch\":" << d.framesDroppedGenerationMismatch
                << ",\"FramesDroppedNv12Starvation\":" << d.framesDroppedNv12Starvation
                << ",\"FramesConvertedToNv12\":" << d.framesConvertedToNv12
                << ",\"FramesSubmittedToSinkWriter\":" << d.framesSubmittedToSinkWriter
                << ",\"FramesRejectedBySinkWriter\":" << d.framesRejectedBySinkWriter
                << ",\"PauseRequests\":" << d.pauseRequests
                << ",\"VideoPauseAcks\":" << d.videoPauseAcks
                << ",\"ResumeRequests\":" << d.resumeRequests
                << ",\"VideoResumeAcks\":" << d.videoResumeAcks
                << ",\"PausedFramesDiscarded\":" << d.pausedFramesDiscarded
                << ",\"StaleResumeFramesDiscarded\":"
                << d.staleResumeFramesDiscarded
                << ",\"LastPauseCutoffSequence\":"
                << d.lastPauseCutoffSequence
                << ",\"LastResumeCutoffSequence\":"
                << d.lastResumeCutoffSequence
                << ",\"FirstResumedFrameSequence\":"
                << d.firstResumedFrameSequence
                << ",\"AudioPauseAcks\":" << d.audioPauseAcks
                << ",\"AudioResumeAcks\":" << d.audioResumeAcks
                << ",\"AudioPauseFifoClearCalls\":"
                << d.audioPauseFifoClearCalls
                << ",\"AudioInitialPauseClearCalls\":"
                << d.audioInitialPauseClearCalls
                << ",\"AudioPausedWakeClearCalls\":"
                << d.audioPausedWakeClearCalls
                << ",\"AudioFinalResumeClearCalls\":"
                << d.audioFinalResumeClearCalls
                << ",\"AudioFramesWrittenAtPause\":"
                << d.audioFramesWrittenAtPause
                << ",\"AudioFramesWrittenAtResume\":"
                << d.audioFramesWrittenAtResume
                << ",\"AudioPauseTerminalStopTransitions\":"
                << d.audioPauseTerminalStopTransitions
                << ",\"AudioPauseDiscardGateActive\":"
                << (d.audioPauseDiscardGateActive ? 1 : 0)
                << ",\"VideoProcessorFailures\":" << d.videoProcessorFailures
                << ",\"WriteSampleFailures\":" << d.writeSampleFailures
                << ",\"Nv12PoolSize\":" << d.nv12PoolSize
                << ",\"Nv12PoolHighWatermark\":" << d.nv12PoolHighWatermark
                << ",\"Nv12OutstandingCurrent\":" << d.nv12OutstandingCurrent
                << ",\"Nv12OutstandingHighWatermark\":" << d.nv12OutstandingHighWatermark
                << ",\"Nv12OutstandingAtStop\":" << d.nv12OutstandingAtStop
                << ",\"Nv12PoolStarvation\":" << d.nv12PoolStarvation
                << ",\"TrackedCallbackCount\":" << d.trackedCallbackCount
                << ",\"TrackedCallbackAfterStop\":" << d.trackedCallbackAfterStop
                << ",\"TrackedReturnTimeoutMs\":" << d.trackedReturnTimeoutMs
                << ",\"TrackedReturnTimedOut\":" << (d.trackedReturnTimedOut ? 1 : 0)
                << ",\"FirstInputTimestamp\":" << d.firstInputTimestamp
                << ",\"LastInputTimestamp\":" << d.lastInputTimestamp
                << ",\"FirstSampleTime\":" << d.firstSampleTime
                << ",\"LastSampleTime\":" << d.lastSampleTime
                << ",\"SampleDurationMin\":" << d.sampleDurationMin
                << ",\"SampleDurationMax\":" << d.sampleDurationMax
                << ",\"DurationEstimateSource\":\"" << d.durationEstimateSource << '"'
                << ",\"LastFrameDurationEstimated\":" << (d.lastFrameDurationEstimated ? 1 : 0)
                << ",\"WriteSampleDurationP95\":" << writeP95
                << ",\"WriteSampleDurationP50\":" << writeP50
                << ",\"WriteSampleDurationMax\":" << writeMax
                << ",\"AudioBackend\":\"" << JsonEscape(d.audioBackend) << '"'
                << ",\"AudioMode\":\"" << JsonEscape(d.audioMode) << '"'
                << ",\"AudioStartHResult\":\"" << HResultText(d.audioStartHResult) << '"'
                << ",\"AudioStopHResult\":\"" << HResultText(d.audioStopHResult) << '"'
                << ",\"AudioCaptureStarted\":" << (d.audioCaptureStarted ? 1 : 0)
                << ",\"AudioCaptureStopped\":" << (d.audioCaptureStopped ? 1 : 0)
                << ",\"AudioPcmBytesPulled\":" << d.audioPcmBytesPulled
                << ",\"AudioPcmFramesWritten\":" << d.audioPcmFramesWritten
                << ",\"AudioSamplesWritten\":" << d.audioSamplesWritten
                << ",\"AudioPaddingSamplesWritten\":" << d.audioPaddingSamplesWritten
                << ",\"AudioEmptySamplesSkipped\":" << d.audioEmptySamplesSkipped
                << ",\"GStreamerAudioVersion\":\"" << d.gStreamerAudioVersion << '"'
                << ",\"GStreamerAudioMode\":\"" << d.gStreamerAudioMode << '"'
                << ",\"GStreamerSystemActive\":" << (d.gStreamerSystemActive ? 1 : 0)
                << ",\"GStreamerMicrophoneActive\":" << (d.gStreamerMicrophoneActive ? 1 : 0)
                << ",\"GStreamerMicrophoneDeviceId\":\"" << JsonEscape(Utf8(d.gStreamerMicrophoneDeviceId)) << '"'
                << ",\"GStreamerMicrophoneDeviceDisplayName\":\"" << JsonEscape(Utf8(d.gStreamerMicrophoneDeviceDisplayName)) << '"'
                << ",\"GStreamerMicrophoneDeviceProperties\":\"" << JsonEscape(Utf8(d.gStreamerMicrophoneDeviceProperties)) << '"'
                << ",\"GStreamerMicrophoneElementDeviceId\":\"" << JsonEscape(Utf8(d.gStreamerMicrophoneElementDeviceId)) << '"'
                << ",\"GStreamerMicrophoneSessionBound\":" << (d.gStreamerMicrophoneSessionBound ? 1 : 0)
                << ",\"GStreamerMicrophoneSourceCreatedFromDevice\":" << (d.gStreamerMicrophoneSourceCreatedFromDevice ? 1 : 0)
                << ",\"GStreamerMicrophoneElementIdentityMatches\":" << (d.gStreamerMicrophoneElementIdentityMatches ? 1 : 0)
                << ",\"MicDisconnectedDuringRecording\":" << (d.micDisconnectedDuringRecording ? 1 : 0)
                << ",\"GStreamerMicrophoneSourceDataBlocked\":" << (d.gStreamerMicrophoneSourceDataBlocked ? 1 : 0)
                << ",\"GStreamerPipelineState\":\"" << d.gStreamerPipelineState << '"'
                << ",\"GStreamerLastError\":\"" << JsonEscape(Utf8(d.gStreamerLastError)) << '"'
                << ",\"GStreamerAudioWorkingPath\":\"" << JsonEscape(Utf8(d.gStreamerAudioWorkingPath)) << '"'
                << ",\"GStreamerSystemWorkingPath\":\"" << JsonEscape(Utf8(d.gStreamerSystemWorkingPath)) << '"'
                << ",\"GStreamerMicrophoneWorkingPath\":\"" << JsonEscape(Utf8(d.gStreamerMicrophoneWorkingPath)) << '"'
                << ",\"GStreamerTerminalHResult\":\"" << HResultText(d.gStreamerTerminalHResult) << '"'
                << ",\"GStreamerDeviceMonitorActive\":" << (d.gStreamerDeviceMonitorActive ? 1 : 0)
                << ",\"GStreamerEndOfStreamObserved\":" << (d.gStreamerEndOfStreamObserved ? 1 : 0)
                << ",\"GStreamerFilesClosed\":" << (d.gStreamerFilesClosed ? 1 : 0)
                << ",\"GStreamerBusThreadExited\":" << (d.gStreamerBusThreadExited ? 1 : 0)
                << ",\"GStreamerMixerVolumesFixedAtUnity\":" << (d.gStreamerMixerVolumesFixedAtUnity ? 1 : 0)
                << ",\"GStreamerDualSourcesIndependent\":" << (d.gStreamerDualSourcesIndependent ? 1 : 0)
                << ",\"GStreamerValidatedSampleRate\":" << d.gStreamerValidatedSampleRate
                << ",\"GStreamerValidatedChannels\":" << d.gStreamerValidatedChannels
                << ",\"GStreamerDecodedAudioFrames\":" << d.gStreamerDecodedAudioFrames
                << ",\"GStreamerAudioPeakAbsolutePcm16\":" << d.gStreamerAudioPeakAbsolutePcm16
                << ",\"GStreamerAudioRmsPcm16\":" << d.gStreamerAudioRmsPcm16
                << ",\"GStreamerAudioDcPcm16\":" << d.gStreamerAudioDcPcm16
                << ",\"GStreamerAudioSaturatedSamples\":" << d.gStreamerAudioSaturatedSamples
                << ",\"GStreamerValidatedAudioDuration100ns\":" << d.gStreamerValidatedAudioDuration100ns
                << ",\"GStreamerValidatedAudioReachedEndOfStream\":" << (d.gStreamerValidatedAudioReachedEndOfStream ? 1 : 0)
                << ",\"GStreamerFinalIntegratedLufs\":" << d.gStreamerFinalIntegratedLufs
                << ",\"GStreamerFinalTruePeakDbtp\":" << d.gStreamerFinalTruePeakDbtp
                << ",\"GStreamerFinalLoudnessValidated\":" << (d.gStreamerFinalLoudnessValidated ? 1 : 0)
                << ",\"GStreamerMicrophoneMasteringApplied\":" << (d.gStreamerMicrophoneMasteringApplied ? 1 : 0)
                << ",\"GStreamerDualMixApplied\":" << (d.gStreamerDualMixApplied ? 1 : 0)
                << ",\"FinalizeAttempted\":" << (d.finalizeAttempted ? 1 : 0)
                << ",\"FinalizeHResult\":\"" << HResultText(d.finalizeHResult) << '"'
                << ",\"FinalizeDurationMs\":" << d.finalizeDurationMs
                << ",\"TrackedReturnDurationMs\":" << d.trackedReturnDurationMs
                << ",\"OutputFileExists\":" << (d.outputFileExists ? 1 : 0)
                << ",\"OutputFileSize\":" << d.outputFileSize
                << ",\"SourceReaderValidation\":\"" << d.sourceReaderValidation << '"'
                << ",\"SourceReaderValidationMode\":\"" << d.sourceReaderValidationMode << '"'
                << ",\"ValidationSampleLimit\":" << d.validationSampleLimit
                << ",\"ValidationSamplesRead\":" << d.validationSamplesRead
                << ",\"ValidationReachedEndOfStream\":" << (d.validationReachedEndOfStream ? 1 : 0)
                << ",\"ValidationDurationMs\":" << d.validationDurationMs
                << ",\"DecodedFrameCount\":" << d.decodedFrameCount
                << ",\"ValidatedFirstPts\":" << d.validatedFirstPts
                << ",\"ValidatedLastPts\":" << d.validatedLastPts
                << ",\"ValidatedDuration100ns\":" << d.validatedDuration100ns
                << ",\"LeaseReturnCount\":" << d.leaseReturnCount
                << ",\"TapFramesObserved\":" << d.tapFramesObserved
                << ",\"TapFramesCopied\":" << d.tapFramesCopied
                << ",\"TapFramesEnqueued\":" << d.tapFramesEnqueued
                << ",\"TapFramesDroppedNoFreeSlot\":" << d.tapFramesDroppedNoFreeSlot
                << ",\"TapFramesDroppedQueueFull\":" << d.tapFramesDroppedQueueFull
                << ",\"TapFramesDroppedGenerationMismatch\":" << d.tapFramesDroppedGenerationMismatch
                << ",\"TapFramesDroppedDisabledOrStopping\":" << d.tapFramesDroppedDisabledOrStopping
                << ",\"TapFramesDroppedLockBusy\":" << d.tapFramesDroppedLockBusy
                << ",\"TapQueueDepthHighWatermark\":" << d.tapQueueDepthHighWatermark
                << ",\"BgraOutstandingAtStop\":" << d.bgraOutstandingAtStop
                << ",\"BgraOutstandingAtShutdown\":" << d.bgraOutstandingAtShutdown
                << ",\"GenerationChangeCount\":" << d.generationChangeCount
                << ",\"DeviceRemovedReason\":\"" << HResultText(d.deviceRemovedReason) << '"'
                << ",\"StopDurationMs\":" << d.stopDurationMs
                << ",\"EncoderJoinDurationMs\":" << d.encoderJoinDurationMs
                << ",\"ConsumerConflict\":" << (d.consumerConflict ? 1 : 0)
                << ",\"DoubleReturnDetected\":" << d.doubleReturnDetected
                << ",\"InvalidStateTransitionDetected\":" << d.invalidStateTransitionDetected
                << ",\"ResidualOutstandingAtShutdown\":" << d.residualOutstandingAtShutdown
                << ",\"FailureStage\":\"" << JsonEscape(d.failureStage) << '"'
                << ",\"FailureHResult\":\"" << HResultText(d.failureHResult) << '"'
                << ",\"OutputDeleteAttempted\":" << (d.outputDeleteAttempted ? 1 : 0)
                << ",\"OutputDeleteSucceeded\":" << (d.outputDeleteSucceeded ? 1 : 0)
                << ",\"OutputDeleteHResult\":\"" << HResultText(d.outputDeleteHResult) << '"'
                << ",\"ManifestEnabled\":" << (d.manifestEnabled ? 1 : 0)
                << ",\"ManifestPath\":\"" << JsonEscape(Utf8(d.manifestPath)) << '"'
                << ",\"ManifestCreated\":" << (d.manifestCreated ? 1 : 0)
                << ",\"ManifestWriteAttempts\":" << d.manifestWriteAttempts
                << ",\"ManifestWriteSuccesses\":" << d.manifestWriteSuccesses
                << ",\"ManifestWriteFailures\":" << d.manifestWriteFailures
                << ",\"ManifestLastPersistedRevision\":" << d.manifestLastPersistedRevision
                << ",\"ManifestLastPersistedState\":\""
                << JsonEscape(d.manifestLastPersistedState == nullptr
                    ? "Unavailable"
                    : d.manifestLastPersistedState) << '"'
                << ",\"ManifestFirstFailureHResult\":\""
                << HResultText(d.manifestFirstFailureHResult) << '"'
                << ",\"ManifestLastFailureHResult\":\""
                << HResultText(d.manifestLastFailureHResult) << '"'
                << ",\"LifetimeOwnerAcquireAttempted\":"
                << (d.lifetimeOwnerAcquireAttempted ? 1 : 0)
                << ",\"LifetimeOwnerAcquired\":"
                << (d.lifetimeOwnerAcquired ? 1 : 0)
                << ",\"LifetimeOwnerAcquireHResult\":\""
                << HResultText(d.lifetimeOwnerAcquireHResult) << '"'
                << ",\"LifetimeOwnerPath\":\""
                << JsonEscape(Utf8(d.lifetimeOwnerPath)) << '"'
                << "}\n";
        }
        catch (...)
        {
        }
    }

    bool WriteVideoCadenceTrace(
        const std::wstring& sessionDirectory,
        const VideoCadenceTraceBuffer& trace) noexcept
    {
        if (sessionDirectory.empty() || trace.totalTicks == 0)
        {
            return false;
        }
        try
        {
            const auto directory = std::filesystem::path(sessionDirectory);
            if (!std::filesystem::is_directory(directory))
            {
                return false;
            }

            const auto tracePath = directory / L"video-cadence-trace.csv";
            std::ofstream traceStream(
                tracePath, std::ios::out | std::ios::trunc);
            if (!traceStream)
            {
                return false;
            }
            traceStream
                << "RecordOrdinal,TickIndex,SelectedFps,"
                << "TargetContentTime100ns,ActualWakeQpc,"
                << "ScheduledDeadlineQpc,DeadlineErrorUs,"
                << "PendingFrameSequence,PendingSourceTimestamp100ns,"
                << "PendingEnqueueQpc,LastSubmittedFreshSequence,"
                << "LastSubmittedSourceTimestamp100ns,"
                << "SourceArrivalsSincePreviousTick,"
                << "PendingReplacementsSincePreviousTick,"
                << "CadenceDropsSincePreviousTick,Decision,MissedDeadline,"
                << "FreshAvailableBeforeDeadline,"
                << "FreshAvailableSequenceBeforeDeadline,"
                << "FreshAvailableSourceTimestamp100ns,"
                << "FreshAvailableEnqueueQpc,DuplicateClassification,"
                << "DropThenNextTickDuplicate\n";
            const auto firstOrdinal = trace.totalTicks - trace.recordCount;
            std::vector<double> deadlineErrors;
            deadlineErrors.reserve(trace.recordCount);
            for (std::uint64_t ordinal = firstOrdinal;
                ordinal < trace.totalTicks;
                ++ordinal)
            {
                const auto* const record = trace.FindRecord(ordinal);
                if (record == nullptr)
                {
                    continue;
                }
                deadlineErrors.push_back(
                    static_cast<double>(record->deadlineErrorUs));
                traceStream
                    << record->recordOrdinal << ','
                    << record->tickIndex << ','
                    << record->selectedFps << ','
                    << record->targetContentTime100ns << ','
                    << record->actualWakeQpc << ','
                    << record->scheduledDeadlineQpc << ','
                    << record->deadlineErrorUs << ','
                    << record->pendingFrameSequence << ','
                    << record->pendingSourceTimestamp100ns << ','
                    << record->pendingEnqueueQpc << ','
                    << record->lastSubmittedFreshSequence << ','
                    << record->lastSubmittedSourceTimestamp100ns << ','
                    << record->sourceArrivalsSincePreviousTick << ','
                    << record->pendingReplacementsSincePreviousTick << ','
                    << record->cadenceDropsSincePreviousTick << ','
                    << VideoCadenceDecisionName(record->decision) << ','
                    << (record->missedDeadline ? 1 : 0) << ','
                    << (record->freshAvailableBeforeDeadline ? 1 : 0) << ','
                    << record->freshAvailableSequenceBeforeDeadline << ','
                    << record->freshAvailableSourceTimestamp100ns << ','
                    << record->freshAvailableEnqueueQpc << ','
                    << VideoCadenceDuplicateClassificationName(
                        record->duplicateClassification) << ','
                    << (record->dropThenNextTickDuplicate ? 1 : 0)
                    << '\n';
            }
            traceStream.close();
            if (!traceStream)
            {
                return false;
            }

            const auto submitted = trace.fresh + trace.duplicate;
            const auto duplicateRatio = submitted == 0
                ? 0.0
                : static_cast<double>(trace.duplicate) * 100.0 /
                    static_cast<double>(submitted);
            const auto sourceArrivalFps = EventRate(
                trace.totalSourceArrivals,
                trace.firstSourceArrivalQpc,
                trace.lastSourceArrivalQpc,
                trace.qpcFrequency);
            const auto freshOutputFps = EventRate(
                trace.fresh,
                trace.firstFreshOutputQpc,
                trace.lastFreshOutputQpc,
                trace.qpcFrequency);
            const auto meanDeadlineError = trace.totalTicks == 0
                ? 0.0
                : static_cast<double>(trace.deadlineErrorSumUs) /
                    static_cast<double>(trace.totalTicks);
            const auto p95DeadlineError =
                Percentile(std::move(deadlineErrors), 0.95);

            const auto summaryPath =
                directory / L"video-cadence-summary.json";
            std::ofstream summaryStream(
                summaryPath, std::ios::out | std::ios::trunc);
            if (!summaryStream)
            {
                return false;
            }
            summaryStream << std::fixed << std::setprecision(3)
                << "{\"TOTAL_TICKS\":" << trace.totalTicks
                << ",\"FRESH\":" << trace.fresh
                << ",\"DUPLICATE\":" << trace.duplicate
                << ",\"NO_FRAME\":" << trace.noFrame
                << ",\"MISSED\":" << trace.missed
                << ",\"DUPLICATE_RATIO\":" << duplicateRatio
                << ",\"DUPLICATE_WITH_NO_NEW_SOURCE_AVAILABLE\":"
                << trace.duplicateWithNoNewSourceAvailable
                << ",\"DUPLICATE_DESPITE_FRESH_AVAILABLE_BEFORE_DEADLINE\":"
                << trace.duplicateDespiteFreshAvailableBeforeDeadline
                << ",\"NORMAL_MULTI_SOURCE_CADENCE_DROPS\":"
                << trace.normalMultiSourceCadenceDrops
                << ",\"DROP_THEN_NEXT_TICK_DUPLICATE_COUNT\":"
                << trace.dropThenNextTickDuplicateCount
                << ",\"SOURCE_ARRIVAL_FPS\":" << sourceArrivalFps
                << ",\"FRESH_OUTPUT_FPS\":" << freshOutputFps
                << ",\"MEAN_DEADLINE_ERROR_US\":" << meanDeadlineError
                << ",\"P95_DEADLINE_ERROR_US\":" << p95DeadlineError
                << ",\"MAX_DEADLINE_ERROR_US\":"
                << trace.maximumDeadlineErrorUs
                << ",\"SOURCE_ARRIVALS\":" << trace.totalSourceArrivals
                << ",\"QPC_FREQUENCY\":" << trace.qpcFrequency
                << ",\"RING_CAPACITY\":" << VideoCadenceTraceCapacity
                << ",\"TRACE_RECORDS_RETAINED\":" << trace.recordCount
                << ",\"TRACE_RECORDS_OVERWRITTEN\":"
                << trace.traceRecordsOverwritten
                << ",\"UNRESOLVED_DUPLICATE_OVERFLOWS\":"
                << trace.unresolvedDuplicateOverflows
                << ",\"FPS_RATE_FORMULA\":\"(event_count-1)*qpc_frequency/(last_qpc-first_qpc)\""
                << "}\n";
            summaryStream.close();
            return static_cast<bool>(summaryStream);
        }
        catch (...)
        {
            return false;
        }
    }
}
