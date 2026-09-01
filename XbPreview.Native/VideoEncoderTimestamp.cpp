#include "VideoEncoderTimestamp.h"

#include <algorithm>

namespace xbpreview
{
    namespace
    {
        constexpr std::int64_t HundredNanosecondsPerSecond = 10'000'000;
        // The current 60/1 Media Foundation H.264/MP4 contract serializes
        // packet timestamps on a 60 kHz track time base. Two distinct WGC
        // timestamps closer than one track tick otherwise collapse to the
        // same packet DTS. This is the smallest 100-ns step that remains
        // representably distinct after that conversion.
        constexpr std::int64_t SinkWriterVideoTimescale = 60'000;
        constexpr std::int64_t MinimumRepresentableSampleStep100ns =
            (HundredNanosecondsPerSecond + SinkWriterVideoTimescale - 1) /
            SinkWriterVideoTimescale;
        constexpr std::int64_t MinimumCadenceDelta100ns = 80'000;  // 8 ms
        constexpr std::int64_t MaximumCadenceDelta100ns = 400'000; // 40 ms
        constexpr std::int64_t MaximumReportedDuration100ns = 333'333;
    }

    VideoCfrCadence::VideoCfrCadence(
        const std::uint32_t framesPerSecond) noexcept
    {
        Reset(framesPerSecond);
    }

    VideoCfrFrameTiming VideoCfrCadence::PrepareNext() const noexcept
    {
        VideoCfrFrameTiming result{};
        result.frameIndex = nextFrameIndex_;
        result.sampleTime100ns = VideoEncoderCfrTime100ns(
            nextFrameIndex_, framesPerSecond_);
        result.duration100ns = VideoEncoderCfrDuration100ns(
            nextFrameIndex_, framesPerSecond_);
        result.endTime100ns = result.sampleTime100ns + result.duration100ns;
        return result;
    }

    bool VideoCfrCadence::Commit(
        const VideoCfrFrameTiming& timing) noexcept
    {
        const auto expected = PrepareNext();
        if (timing.frameIndex != expected.frameIndex ||
            timing.sampleTime100ns != expected.sampleTime100ns ||
            timing.duration100ns != expected.duration100ns ||
            timing.endTime100ns != expected.endTime100ns)
        {
            return false;
        }
        ++nextFrameIndex_;
        return true;
    }

    void VideoCfrCadence::Reset(
        const std::uint32_t framesPerSecond) noexcept
    {
        framesPerSecond_ = IsSupportedVideoEncoderFrameRate(framesPerSecond)
            ? framesPerSecond
            : VideoEncoderDefaultFrameRate;
        nextFrameIndex_ = 0;
    }

    std::uint32_t VideoCfrCadence::FramesPerSecond() const noexcept
    {
        return framesPerSecond_;
    }

    std::uint64_t VideoCfrCadence::NextFrameIndex() const noexcept
    {
        return nextFrameIndex_;
    }

    VideoTimestampCandidate VideoEncoderTimestamp::Prepare(
        const bool timestampValid,
        const std::int64_t rawTimestamp100ns) noexcept
    {
        if (!timestampValid)
        {
            return {};
        }
        if (hasSourceTimelineOrigin_ &&
            rawTimestamp100ns <= lastObservedSourceTimestamp100ns_)
        {
            VideoTimestampCandidate result{};
            result.result = VideoTimestampPrepareResult::Regression;
            return result;
        }

        VideoTimestampCandidate result{};
        result.result = VideoTimestampPrepareResult::Prepared;
        const auto resumeProjectionPending = resumeProjectionPending_;
        if (!hasSourceTimelineOrigin_)
        {
            hasSourceTimelineOrigin_ = true;
            firstSourceTimestamp100ns_ = rawTimestamp100ns;
            lastObservedSourceTimestamp100ns_ = rawTimestamp100ns;
            result.sourceTimeline.sampleTime100ns = 0;
            result.sourceTimeline.duration100ns =
                VideoEncoderNominalFrameDuration100ns;
        }
        else
        {
            const auto sourceDelta100ns =
                rawTimestamp100ns - lastObservedSourceTimestamp100ns_;
            result.gapObserved =
                sourceDelta100ns > MaximumCadenceDelta100ns;
            if (!resumeProjectionPending &&
                sourceDelta100ns >= MinimumCadenceDelta100ns &&
                sourceDelta100ns <= MaximumCadenceDelta100ns)
            {
                cadenceDeltas_[cadenceWriteIndex_] = sourceDelta100ns;
                cadenceWriteIndex_ =
                    (cadenceWriteIndex_ + 1) % cadenceDeltas_.size();
                cadenceCount_ = (std::min)(
                    cadenceCount_ + 1, cadenceDeltas_.size());
            }
            lastObservedSourceTimestamp100ns_ = rawTimestamp100ns;
            result.sourceTimeline.sampleTime100ns =
                rawTimestamp100ns - firstSourceTimestamp100ns_;
            result.sourceTimeline.duration100ns = EstimateSourceDuration();
        }

        result.sourceTimeline.endTime100ns =
            result.sourceTimeline.sampleTime100ns +
            result.sourceTimeline.duration100ns;
        result.durationFromHistory = cadenceCount_ > 0;

        auto contentSampleTime100ns =
            result.sourceTimeline.sampleTime100ns -
            totalExcludedDuration100ns_;
        if (resumeProjectionPending)
        {
            const auto resumeContentTime100ns =
                hasCommittedContentTimeline_
                ? lastCommittedContentEndTime100ns_
                : 0;
            if (contentSampleTime100ns > resumeContentTime100ns)
            {
                totalExcludedDuration100ns_ +=
                    contentSampleTime100ns - resumeContentTime100ns;
                contentSampleTime100ns = resumeContentTime100ns;
            }
            resumeProjectionPending_ = false;
        }
        result.contentTimeline.sampleTime100ns = contentSampleTime100ns;
        result.contentTimeline.duration100ns =
            result.sourceTimeline.duration100ns;
        result.contentTimeline.endTime100ns =
            result.contentTimeline.sampleTime100ns +
            result.contentTimeline.duration100ns;

        result.videoSinkTimeline.sampleTime100ns =
            hasCommittedVideoSinkTimestamp_
            ? (std::max)(
                result.contentTimeline.sampleTime100ns,
                lastCommittedVideoSinkSampleTime100ns_ +
                    MinimumRepresentableSampleStep100ns)
            : result.contentTimeline.sampleTime100ns;
        // The container projection changes only the video serialization
        // position. Its duration still comes from the existing WGC cadence
        // estimate, but remains a distinct field so a shifted sink start can
        // never be used to infer source-clock elapsed time.
        result.videoSinkTimeline.duration100ns =
            result.sourceTimeline.duration100ns;
        result.videoSinkTimeline.endTime100ns =
            result.videoSinkTimeline.sampleTime100ns +
            result.videoSinkTimeline.duration100ns;

        ++sourceObservationSequence_;
        result.resetGeneration_ = resetGeneration_;
        result.projectionGeneration_ = projectionGeneration_;
        result.sourceObservationSequence_ = sourceObservationSequence_;
        result.committedVideoSinkCount_ = committedVideoSinkCount_;
        return result;
    }

    VideoTimestampCandidate VideoEncoderTimestamp::PrepareCfr(
        const VideoCfrFrameTiming& timing) noexcept
    {
        if (!hasSourceTimelineOrigin_ || excludedIntervalOpen_ ||
            timing.sampleTime100ns < 0 || timing.duration100ns <= 0 ||
            timing.endTime100ns !=
                timing.sampleTime100ns + timing.duration100ns)
        {
            return {};
        }
        if (hasCommittedContentTimeline_ &&
            timing.sampleTime100ns < lastCommittedContentEndTime100ns_)
        {
            VideoTimestampCandidate result{};
            result.result = VideoTimestampPrepareResult::Regression;
            return result;
        }

        VideoTimestampCandidate result{};
        result.result = VideoTimestampPrepareResult::Prepared;
        result.sourceTimeline.sampleTime100ns =
            lastObservedSourceTimestamp100ns_ - firstSourceTimestamp100ns_;
        result.sourceTimeline.duration100ns = EstimateSourceDuration();
        result.sourceTimeline.endTime100ns =
            result.sourceTimeline.sampleTime100ns +
            result.sourceTimeline.duration100ns;
        result.durationFromHistory = cadenceCount_ > 0;
        result.contentTimeline.sampleTime100ns = timing.sampleTime100ns;
        result.contentTimeline.duration100ns = timing.duration100ns;
        result.contentTimeline.endTime100ns = timing.endTime100ns;
        result.videoSinkTimeline.sampleTime100ns =
            hasCommittedVideoSinkTimestamp_
            ? (std::max)(
                timing.sampleTime100ns,
                lastCommittedVideoSinkSampleTime100ns_ +
                    MinimumRepresentableSampleStep100ns)
            : timing.sampleTime100ns;
        result.videoSinkTimeline.duration100ns = timing.duration100ns;
        result.videoSinkTimeline.endTime100ns =
            result.videoSinkTimeline.sampleTime100ns + timing.duration100ns;
        result.resetGeneration_ = resetGeneration_;
        result.projectionGeneration_ = projectionGeneration_;
        result.sourceObservationSequence_ = sourceObservationSequence_;
        result.committedVideoSinkCount_ = committedVideoSinkCount_;
        return result;
    }

    bool VideoEncoderTimestamp::Commit(
        const VideoTimestampCandidate& candidate) noexcept
    {
        if (candidate.result != VideoTimestampPrepareResult::Prepared ||
            candidate.resetGeneration_ != resetGeneration_ ||
            candidate.projectionGeneration_ != projectionGeneration_ ||
            candidate.sourceObservationSequence_ !=
                sourceObservationSequence_ ||
            candidate.committedVideoSinkCount_ != committedVideoSinkCount_)
        {
            return false;
        }

        hasCommittedVideoSinkTimestamp_ = true;
        lastCommittedVideoSinkSampleTime100ns_ =
            candidate.videoSinkTimeline.sampleTime100ns;
        hasCommittedContentTimeline_ = true;
        lastCommittedContentEndTime100ns_ =
            candidate.contentTimeline.endTime100ns;
        ++committedVideoSinkCount_;
        return true;
    }

    void VideoEncoderTimestamp::BeginExcludedInterval() noexcept
    {
        if (excludedIntervalOpen_)
        {
            return;
        }
        excludedIntervalOpen_ = true;
        resumeProjectionPending_ = false;
        ++projectionGeneration_;
        if (projectionGeneration_ == 0)
        {
            projectionGeneration_ = 1;
        }
    }

    void VideoEncoderTimestamp::EndExcludedInterval() noexcept
    {
        if (!excludedIntervalOpen_)
        {
            return;
        }
        excludedIntervalOpen_ = false;
        resumeProjectionPending_ = true;
        ++projectionGeneration_;
        if (projectionGeneration_ == 0)
        {
            projectionGeneration_ = 1;
        }
    }

    void VideoEncoderTimestamp::Reset() noexcept
    {
        auto nextResetGeneration = resetGeneration_ + 1;
        if (nextResetGeneration == 0)
        {
            nextResetGeneration = 1;
        }
        *this = {};
        resetGeneration_ = nextResetGeneration;
    }

    std::int64_t VideoEncoderTimestamp::CurrentSourceDuration100ns() const noexcept
    {
        return EstimateSourceDuration();
    }

    std::int64_t VideoEncoderTimestamp::TotalExcludedDuration100ns() const noexcept
    {
        return totalExcludedDuration100ns_;
    }

    bool VideoEncoderTimestamp::HasSourceTimelineOrigin() const noexcept
    {
        return hasSourceTimelineOrigin_;
    }

    bool VideoEncoderTimestamp::HasCommittedVideoSinkTimestamp() const noexcept
    {
        return hasCommittedVideoSinkTimestamp_;
    }

    std::int64_t VideoEncoderTimestamp::EstimateSourceDuration() const noexcept
    {
        if (cadenceCount_ == 0)
        {
            return VideoEncoderNominalFrameDuration100ns;
        }
        auto values = cadenceDeltas_;
        std::sort(values.begin(), values.begin() + cadenceCount_);
        const auto median = values[cadenceCount_ / 2];
        return (std::clamp)(
            median,
            MinimumCadenceDelta100ns,
            MaximumReportedDuration100ns);
    }
}
