#pragma once

#include "VideoEncoderConfig.h"

#include <array>
#include <cstddef>
#include <cstdint>

namespace xbpreview
{
    enum class VideoTimestampPrepareResult
    {
        Prepared,
        Missing,
        Regression
    };

    struct VideoSourceTimelineSample
    {
        std::int64_t sampleTime100ns{};
        std::int64_t duration100ns{ VideoEncoderNominalFrameDuration100ns };
        std::int64_t endTime100ns{ VideoEncoderNominalFrameDuration100ns };
    };

    struct VideoContentTimelineSample
    {
        std::int64_t sampleTime100ns{};
        std::int64_t duration100ns{ VideoEncoderNominalFrameDuration100ns };
        std::int64_t endTime100ns{ VideoEncoderNominalFrameDuration100ns };
    };

    struct VideoSinkTimelineSample
    {
        std::int64_t sampleTime100ns{};
        std::int64_t duration100ns{ VideoEncoderNominalFrameDuration100ns };
        std::int64_t endTime100ns{ VideoEncoderNominalFrameDuration100ns };
    };

    struct VideoTimestampCandidate
    {
        VideoTimestampPrepareResult result{
            VideoTimestampPrepareResult::Missing };
        VideoSourceTimelineSample sourceTimeline{};
        VideoContentTimelineSample contentTimeline{};
        VideoSinkTimelineSample videoSinkTimeline{};
        bool durationFromHistory{};
        bool gapObserved{};

    private:
        std::uint64_t resetGeneration_{};
        std::uint64_t projectionGeneration_{};
        std::uint64_t sourceObservationSequence_{};
        std::uint64_t committedVideoSinkCount_{};

        friend class VideoEncoderTimestamp;
    };

    struct VideoCfrFrameTiming
    {
        std::uint64_t frameIndex{};
        std::int64_t sampleTime100ns{};
        std::int64_t duration100ns{};
        std::int64_t endTime100ns{};
    };

    class VideoCfrCadence final
    {
    public:
        explicit VideoCfrCadence(
            std::uint32_t framesPerSecond = VideoEncoderDefaultFrameRate)
            noexcept;
        [[nodiscard]] VideoCfrFrameTiming PrepareNext() const noexcept;
        [[nodiscard]] bool Commit(
            const VideoCfrFrameTiming& timing) noexcept;
        void Reset(std::uint32_t framesPerSecond) noexcept;
        [[nodiscard]] std::uint32_t FramesPerSecond() const noexcept;
        [[nodiscard]] std::uint64_t NextFrameIndex() const noexcept;

    private:
        std::uint32_t framesPerSecond_{ VideoEncoderDefaultFrameRate };
        std::uint64_t nextFrameIndex_{};
    };

    class VideoEncoderTimestamp final
    {
    public:
        [[nodiscard]] VideoTimestampCandidate Prepare(
            bool timestampValid,
            std::int64_t rawTimestamp100ns) noexcept;
        [[nodiscard]] VideoTimestampCandidate PrepareCfr(
            const VideoCfrFrameTiming& timing) noexcept;
        [[nodiscard]] bool Commit(
            const VideoTimestampCandidate& candidate) noexcept;
        void BeginExcludedInterval() noexcept;
        void EndExcludedInterval() noexcept;
        void Reset() noexcept;

        [[nodiscard]] std::int64_t CurrentSourceDuration100ns() const noexcept;
        [[nodiscard]] std::int64_t TotalExcludedDuration100ns() const noexcept;
        [[nodiscard]] bool HasSourceTimelineOrigin() const noexcept;
        [[nodiscard]] bool HasCommittedVideoSinkTimestamp() const noexcept;

    private:
        [[nodiscard]] std::int64_t EstimateSourceDuration() const noexcept;

        bool hasSourceTimelineOrigin_{};
        std::int64_t firstSourceTimestamp100ns_{};
        std::int64_t lastObservedSourceTimestamp100ns_{};
        bool hasCommittedVideoSinkTimestamp_{};
        std::int64_t lastCommittedVideoSinkSampleTime100ns_{};
        bool hasCommittedContentTimeline_{};
        std::int64_t lastCommittedContentEndTime100ns_{};
        std::int64_t totalExcludedDuration100ns_{};
        bool excludedIntervalOpen_{};
        bool resumeProjectionPending_{};
        std::array<std::int64_t, 9> cadenceDeltas_{};
        std::size_t cadenceCount_{};
        std::size_t cadenceWriteIndex_{};
        std::uint64_t resetGeneration_{ 1 };
        std::uint64_t projectionGeneration_{ 1 };
        std::uint64_t sourceObservationSequence_{};
        std::uint64_t committedVideoSinkCount_{};
    };
}
