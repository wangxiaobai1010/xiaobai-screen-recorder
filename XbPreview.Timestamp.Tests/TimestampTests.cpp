#include "MfH264SinkWriterSession.h"
#include "VideoEncoderTimestamp.h"
#include "VideoEncoderConsumer.h"
#include "VideoEncoderDiagnostics.h"
#include "RecordingStorageSafety.h"
#include "RecordingVideoBitratePolicy.h"
#include "XbPreviewApi.h"

#include <array>
#include <cstddef>
#include <cstdint>
#include <cstdlib>
#include <filesystem>
#include <fstream>
#include <iostream>
#include <iterator>
#include <memory>
#include <string>
#include <string_view>
#include <type_traits>
#include <utility>

namespace
{
    constexpr std::int64_t RawOrigin100ns = 50'000'000;
    constexpr std::int64_t MinimumVideoSinkStep100ns = 167;
    constexpr std::uint64_t AudioSamplesPerSecond = 48'000;
    constexpr std::uint64_t HundredNanosecondsPerSecond = 10'000'000;

    enum class PauseAudioMode
    {
        Microphone,
        System,
        Dual,
    };

    struct PauseAudioFifoModel
    {
        PauseAudioMode mode{ PauseAudioMode::Dual };
        bool captureRunning{ true };
        std::uint64_t microphoneFrames{};
        std::uint64_t systemFrames{};
        std::uint64_t microphoneFramesDiscarded{};
        std::uint64_t systemFramesDiscarded{};
        std::uint64_t writtenFrames{};
        std::uint64_t lastWriteStartFrame{};
        std::uint64_t lastWriteTarget100ns{};
        std::uint64_t writeCalls{};
        std::uint64_t clearCalls{};
        std::uint64_t stopCalls{};
        std::uint64_t postStopDrainCalls{};

        [[nodiscard]] bool MicrophoneEnabled() const noexcept
        {
            return mode == PauseAudioMode::Microphone ||
                mode == PauseAudioMode::Dual;
        }

        [[nodiscard]] bool SystemEnabled() const noexcept
        {
            return mode == PauseAudioMode::System ||
                mode == PauseAudioMode::Dual;
        }

        void Capture(
            const std::uint64_t microphone,
            const std::uint64_t system) noexcept
        {
            if (!captureRunning)
            {
                return;
            }
            if (MicrophoneEnabled())
            {
                microphoneFrames += microphone;
            }
            if (SystemEnabled())
            {
                systemFrames += system;
            }
        }

        void ClearRecordedPcm() noexcept
        {
            microphoneFramesDiscarded += microphoneFrames;
            systemFramesDiscarded += systemFrames;
            microphoneFrames = 0;
            systemFrames = 0;
            ++clearCalls;
        }

        void WriteThroughTarget(const std::uint64_t target100ns) noexcept
        {
            const auto targetFrames =
                (target100ns * AudioSamplesPerSecond +
                    HundredNanosecondsPerSecond - 1) /
                HundredNanosecondsPerSecond;
            if (targetFrames <= writtenFrames)
            {
                return;
            }
            lastWriteStartFrame = writtenFrames;
            lastWriteTarget100ns = target100ns;
            writtenFrames = targetFrames;
            microphoneFrames = 0;
            systemFrames = 0;
            ++writeCalls;
        }

        void StopAndDrain(const std::uint64_t target100ns) noexcept
        {
            if (!captureRunning)
            {
                return;
            }
            captureRunning = false;
            ++stopCalls;
            ++postStopDrainCalls;
            WriteThroughTarget(target100ns);
        }
    };

    void Require(const bool condition, const std::string_view message)
    {
        if (!condition)
        {
            std::cerr << "TIMESTAMP GATE FAIL: " << message << '\n';
            std::exit(EXIT_FAILURE);
        }
    }

    void RequirePrepared(
        const xbpreview::VideoTimestampCandidate& candidate,
        const std::string_view message)
    {
        Require(
            candidate.result ==
                xbpreview::VideoTimestampPrepareResult::Prepared,
            message);
    }

    std::uint64_t AudioFramesForDurationCeil(
        const std::uint64_t duration100ns) noexcept
    {
        return (duration100ns * AudioSamplesPerSecond +
            HundredNanosecondsPerSecond - 1) /
            HundredNanosecondsPerSecond;
    }

    std::int64_t AudioTimeFromFrames(const std::uint64_t frames) noexcept
    {
        return static_cast<std::int64_t>(
            frames * HundredNanosecondsPerSecond / AudioSamplesPerSecond);
    }

    void TestTimestampTransaction()
    {
        xbpreview::VideoEncoderTimestamp timestamp;

        const auto first = timestamp.Prepare(true, RawOrigin100ns);
        RequirePrepared(first, "CASE A first frame prepares");
        Require(
            first.sourceTimeline.sampleTime100ns == 0 &&
                first.videoSinkTimeline.sampleTime100ns == 0,
            "CASE A starts both projections at zero");
        Require(
            timestamp.HasSourceTimelineOrigin() &&
                !timestamp.HasCommittedVideoSinkTimestamp(),
            "CASE A Prepare observes source without committing sink state");
        Require(timestamp.Commit(first), "CASE A Commit succeeds");
        Require(
            timestamp.HasCommittedVideoSinkTimestamp(),
            "CASE A successful write advances committed sink state");

        const auto poolStarved = timestamp.Prepare(true, RawOrigin100ns + 1);
        RequirePrepared(poolStarved, "CASE B pool-starved frame prepares");
        Require(
            poolStarved.sourceTimeline.sampleTime100ns == 1 &&
                poolStarved.videoSinkTimeline.sampleTime100ns ==
                    MinimumVideoSinkStep100ns,
            "CASE B near-duplicate candidate is source 1 / sink 167");

        const auto writeFailed = timestamp.Prepare(true, RawOrigin100ns + 2);
        RequirePrepared(writeFailed, "CASE C write-failed frame prepares");
        Require(
            writeFailed.sourceTimeline.sampleTime100ns == 2 &&
                writeFailed.videoSinkTimeline.sampleTime100ns ==
                    MinimumVideoSinkStep100ns,
            "CASE C uncommitted pool frame did not consume a sink slot");
        Require(
            !timestamp.Commit(poolStarved),
            "CASE B stale pool-starved candidate cannot be committed later");

        const auto nextWritten = timestamp.Prepare(true, RawOrigin100ns + 3);
        RequirePrepared(nextWritten, "CASE B/C next frame prepares");
        Require(
            nextWritten.sourceTimeline.sampleTime100ns == 3 &&
                nextWritten.videoSinkTimeline.sampleTime100ns ==
                    MinimumVideoSinkStep100ns,
            "CASE B/C failures preserve source truth and reuse sink 167");
        Require(
            !timestamp.Commit(writeFailed),
            "CASE C stale write-failed candidate cannot be committed later");
        Require(
            timestamp.Commit(nextWritten),
            "CASE B/C next successful frame commits from last real write");

        const auto sourceAhead = timestamp.Prepare(
            true, RawOrigin100ns + 1'000);
        RequirePrepared(sourceAhead, "CASE D source-ahead frame prepares");
        Require(
            sourceAhead.sourceTimeline.sampleTime100ns == 1'000 &&
                sourceAhead.videoSinkTimeline.sampleTime100ns == 1'000,
            "CASE D sink projection immediately rejoins source");
        Require(timestamp.Commit(sourceAhead), "CASE D Commit succeeds");

        timestamp.Reset();
        Require(
            !timestamp.HasSourceTimelineOrigin() &&
                !timestamp.HasCommittedVideoSinkTimestamp(),
            "CASE E Reset clears source observation and committed sink state");
        Require(
            timestamp.CurrentSourceDuration100ns() ==
                xbpreview::VideoEncoderNominalFrameDuration100ns,
            "CASE E Reset clears source cadence history");
        const auto restarted = timestamp.Prepare(true, RawOrigin100ns + 9'999);
        RequirePrepared(restarted, "CASE E new session prepares");
        Require(
            restarted.sourceTimeline.sampleTime100ns == 0 &&
                restarted.videoSinkTimeline.sampleTime100ns == 0,
            "CASE E new session establishes a fresh source origin");
        Require(timestamp.Commit(restarted), "CASE E new session commits");

        xbpreview::VideoEncoderTimestamp firstFrameDrop;
        const auto firstNotWritten =
            firstFrameDrop.Prepare(true, RawOrigin100ns);
        RequirePrepared(
            firstNotWritten, "first-frame drop candidate prepares");
        const auto firstActuallyWritten =
            firstFrameDrop.Prepare(true, RawOrigin100ns + 1);
        RequirePrepared(
            firstActuallyWritten, "first actual write candidate prepares");
        Require(
            firstActuallyWritten.sourceTimeline.sampleTime100ns == 1 &&
                firstActuallyWritten.videoSinkTimeline.sampleTime100ns == 1,
            "dropped first frame keeps source origin but consumes no sink floor");
        Require(
            firstFrameDrop.Commit(firstActuallyWritten),
            "first actual write commits its own sink candidate");

        std::cout
            << "TIMESTAMP-TRANSACTION-EVIDENCE source-after-drops=3 "
               "sink-after-drops=167 source-ahead=1000 reset=0/0\n"
            << "TIMESTAMP-TRANSACTION-GATE = PASS\n";
    }

    void TestSourceSinkBoundary()
    {
        xbpreview::VideoEncoderTimestamp timestamp;
        std::array<std::int64_t, 3> sourceSamples{};
        std::array<std::int64_t, 3> sourceEnds{};
        std::array<std::int64_t, 3> sinkSamples{};
        std::array<std::int64_t, 3> sinkEnds{};

        for (std::size_t index = 0; index < sourceSamples.size(); ++index)
        {
            const auto candidate = timestamp.Prepare(
                true,
                RawOrigin100ns + static_cast<std::int64_t>(index));
            RequirePrepared(candidate, "boundary candidate prepares");
            sourceSamples[index] =
                candidate.sourceTimeline.sampleTime100ns;
            sourceEnds[index] = candidate.sourceTimeline.endTime100ns;
            sinkSamples[index] =
                candidate.videoSinkTimeline.sampleTime100ns;
            sinkEnds[index] = candidate.videoSinkTimeline.endTime100ns;
            Require(timestamp.Commit(candidate), "boundary candidate commits");
        }

        Require(
            sourceSamples == std::array<std::int64_t, 3>{ 0, 1, 2 },
            "source timeline remains 0/1/2");
        Require(
            sinkSamples == std::array<std::int64_t, 3>{ 0, 167, 334 },
            "video Sink Writer timeline remains 0/167/334");
        Require(
            sourceEnds == std::array<std::int64_t, 3>{
                166'667, 166'668, 166'669 },
            "source end uses source sample plus source cadence duration");
        Require(
            sinkEnds == std::array<std::int64_t, 3>{
                166'667, 166'834, 167'001 },
            "sink end exposes the counterfactual floor drift");

        const auto firstAudioFrames = AudioFramesForDurationCeil(
            static_cast<std::uint64_t>(sourceEnds.front()));
        const auto audioCursor100ns = AudioTimeFromFrames(firstAudioFrames);
        Require(
            firstAudioFrames == 801 && audioCursor100ns == 166'875,
            "48 kHz first audio write advances to 166875");
        Require(
            sourceEnds[1] <= audioCursor100ns &&
                sourceEnds[2] <= audioCursor100ns,
            "second and third source targets request no additional audio");
        Require(
            sinkEnds[2] > audioCursor100ns,
            "third sink-floor end would incorrectly request additional audio");

        const auto audioTarget100ns = sourceEnds.back();
        const auto recordingSnapshotElapsed100ns = sourceEnds.back();
        const auto videoSinkWriterSampleTime100ns = sinkSamples.back();
        Require(
            audioTarget100ns == 166'669 &&
                recordingSnapshotElapsed100ns == 166'669,
            "Audio target and RecordingSnapshot use source end");
        Require(
            videoSinkWriterSampleTime100ns == 334,
            "video Sink Writer uses the sink-safe projection");
        Require(
            recordingSnapshotElapsed100ns != sinkEnds.back(),
            "RecordingSnapshot excludes sink-floor drift");

        std::cout
            << "SOURCE-SINK-BOUNDARY-EVIDENCE source=[0,1,2] "
               "sink=[0,167,334] sourceEnd=[166667,166668,166669] "
               "sinkEnd=[166667,166834,167001] audioCursor=166875 "
               "snapshot=166669\n"
            << "SOURCE-SINK-BOUNDARY-GATE = PASS\n";
    }

    void TestNoPauseEquivalence()
    {
        xbpreview::VideoEncoderTimestamp timestamp;
        constexpr std::array<std::int64_t, 4> RawOffsets{
            0, 1, 2, 1'000 };
        constexpr std::array<std::int64_t, 4> ExpectedSinkSamples{
            0, 167, 334, 1'000 };

        for (std::size_t index = 0; index < RawOffsets.size(); ++index)
        {
            const auto candidate = timestamp.Prepare(
                true, RawOrigin100ns + RawOffsets[index]);
            RequirePrepared(candidate, "no-pause candidate prepares");
            Require(
                candidate.sourceTimeline.sampleTime100ns ==
                    RawOffsets[index] &&
                candidate.contentTimeline.sampleTime100ns ==
                    RawOffsets[index] &&
                candidate.videoSinkTimeline.sampleTime100ns ==
                    ExpectedSinkSamples[index],
                "no-pause source/content/sink matches frozen projection");
            Require(
                candidate.contentTimeline.duration100ns ==
                    candidate.sourceTimeline.duration100ns &&
                candidate.contentTimeline.endTime100ns ==
                    candidate.sourceTimeline.endTime100ns,
                "no-pause content remains identical to source");
            Require(timestamp.Commit(candidate), "no-pause Commit succeeds");
        }
        Require(
            timestamp.TotalExcludedDuration100ns() == 0,
            "no-pause timeline excludes zero duration");
    }

    void TestSinglePauseExclusionAndResumeSeam()
    {
        xbpreview::VideoEncoderTimestamp timestamp;
        const auto first = timestamp.Prepare(true, RawOrigin100ns);
        RequirePrepared(first, "single-pause first frame prepares");
        Require(timestamp.Commit(first), "single-pause first frame commits");
        const auto committedContentEnd100ns =
            first.contentTimeline.endTime100ns;

        timestamp.BeginExcludedInterval();
        timestamp.EndExcludedInterval();
        const auto resumed = timestamp.Prepare(
            true, RawOrigin100ns + 2'000'000);
        RequirePrepared(resumed, "single-pause resume frame prepares");
        Require(
            resumed.sourceTimeline.sampleTime100ns == 2'000'000 &&
                resumed.gapObserved,
            "single-pause raw source preserves the real gap");
        Require(
            resumed.contentTimeline.sampleTime100ns ==
                committedContentEnd100ns,
            "single-pause content resumes at the committed content end");
        Require(
            timestamp.TotalExcludedDuration100ns() ==
                2'000'000 - committedContentEnd100ns,
            "single-pause duration is excluded cumulatively");
        Require(
            resumed.videoSinkTimeline.sampleTime100ns ==
                resumed.contentTimeline.sampleTime100ns,
            "single-pause sink projects from content, not raw source");
        Require(timestamp.Commit(resumed), "single-pause resume commits");

        const auto next = timestamp.Prepare(
            true,
            RawOrigin100ns + 2'000'000 +
                xbpreview::VideoEncoderNominalFrameDuration100ns);
        RequirePrepared(next, "single-pause next frame prepares");
        Require(
            next.contentTimeline.sampleTime100ns ==
                resumed.contentTimeline.endTime100ns,
            "single-pause content has no seam gap");
    }

    void TestMultiplePauseExclusion()
    {
        xbpreview::VideoEncoderTimestamp timestamp;
        const auto first = timestamp.Prepare(true, RawOrigin100ns);
        RequirePrepared(first, "multi-pause first frame prepares");
        Require(timestamp.Commit(first), "multi-pause first frame commits");

        timestamp.BeginExcludedInterval();
        timestamp.EndExcludedInterval();
        const auto firstResume = timestamp.Prepare(
            true, RawOrigin100ns + 2'000'000);
        RequirePrepared(firstResume, "multi-pause first resume prepares");
        Require(
            firstResume.contentTimeline.sampleTime100ns ==
                first.contentTimeline.endTime100ns,
            "multi-pause first seam is continuous");
        Require(
            timestamp.Commit(firstResume),
            "multi-pause first resume commits");

        timestamp.BeginExcludedInterval();
        timestamp.EndExcludedInterval();
        const auto secondResume = timestamp.Prepare(
            true, RawOrigin100ns + 5'000'000);
        RequirePrepared(secondResume, "multi-pause second resume prepares");
        Require(
            secondResume.sourceTimeline.sampleTime100ns == 5'000'000 &&
                secondResume.contentTimeline.sampleTime100ns ==
                    firstResume.contentTimeline.endTime100ns,
            "multi-pause second seam preserves source/content ownership");
        Require(
            timestamp.TotalExcludedDuration100ns() == 4'666'666,
            "multi-pause excluded duration accumulates exactly");
    }

    void TestPauseDeltaExcludedFromCadence()
    {
        xbpreview::VideoEncoderTimestamp timestamp;
        const auto first = timestamp.Prepare(true, RawOrigin100ns);
        RequirePrepared(first, "cadence first frame prepares");
        Require(timestamp.Commit(first), "cadence first frame commits");

        const auto established = timestamp.Prepare(
            true, RawOrigin100ns + 100'000);
        RequirePrepared(established, "cadence history frame prepares");
        Require(
            established.sourceTimeline.duration100ns == 100'000,
            "cadence history establishes 100000 duration");
        Require(timestamp.Commit(established), "cadence history frame commits");

        timestamp.BeginExcludedInterval();
        timestamp.EndExcludedInterval();
        const auto resumed = timestamp.Prepare(
            true, RawOrigin100ns + 500'000);
        RequirePrepared(resumed, "cadence resume frame prepares");
        Require(
            resumed.sourceTimeline.duration100ns == 100'000 &&
                resumed.contentTimeline.duration100ns == 100'000 &&
                timestamp.CurrentSourceDuration100ns() == 100'000,
            "pause delta does not enter cadence history");
        Require(
            resumed.contentTimeline.sampleTime100ns ==
                established.contentTimeline.endTime100ns &&
                timestamp.TotalExcludedDuration100ns() == 300'000,
            "cadence resume still applies the content seam");
    }

    void TestStrictDtsAndProjectionIsolation()
    {
        xbpreview::VideoEncoderTimestamp timestamp;
        std::array<std::int64_t, 3> sourceSamples{};
        std::array<std::int64_t, 3> contentSamples{};
        std::array<std::int64_t, 3> sinkSamples{};

        for (std::size_t index = 0; index < sourceSamples.size(); ++index)
        {
            const auto candidate = timestamp.Prepare(
                true,
                RawOrigin100ns + static_cast<std::int64_t>(index));
            RequirePrepared(candidate, "strict-DTS candidate prepares");
            sourceSamples[index] = candidate.sourceTimeline.sampleTime100ns;
            contentSamples[index] = candidate.contentTimeline.sampleTime100ns;
            sinkSamples[index] = candidate.videoSinkTimeline.sampleTime100ns;
            Require(timestamp.Commit(candidate), "strict-DTS candidate commits");
        }

        Require(
            sourceSamples == std::array<std::int64_t, 3>{ 0, 1, 2 } &&
                contentSamples == std::array<std::int64_t, 3>{ 0, 1, 2 },
            "sink-safe shift does not contaminate source or content");
        Require(
            sinkSamples == std::array<std::int64_t, 3>{ 0, 167, 334 },
            "strict sink DTS retains the 167x100ns floor");

        const auto equal = timestamp.Prepare(true, RawOrigin100ns + 2);
        const auto decreasing = timestamp.Prepare(true, RawOrigin100ns + 1);
        Require(
            equal.result == xbpreview::VideoTimestampPrepareResult::Regression &&
                decreasing.result ==
                    xbpreview::VideoTimestampPrepareResult::Regression,
            "equal and decreasing raw DTS produce no sink candidates");
    }

    void TestStopWhileExcludedProjectionOpen()
    {
        xbpreview::VideoEncoderTimestamp timestamp;
        const auto first = timestamp.Prepare(true, RawOrigin100ns);
        RequirePrepared(first, "logical-pause first frame prepares");
        Require(timestamp.Commit(first), "logical-pause first frame commits");

        timestamp.BeginExcludedInterval();
        Require(
            timestamp.HasSourceTimelineOrigin() &&
                timestamp.HasCommittedVideoSinkTimestamp() &&
                timestamp.CurrentSourceDuration100ns() ==
                    xbpreview::VideoEncoderNominalFrameDuration100ns &&
                timestamp.TotalExcludedDuration100ns() == 0,
            "opening a logical pause projection does not Reset timestamps");
    }

    void TestPauseAbiLayout()
    {
        static_assert(sizeof(XbRecordingSnapshot) == 2856);
        static_assert(alignof(XbRecordingSnapshot) == 8);
        static_assert(
            std::is_same_v<
                decltype(XbRecordingSnapshot::pauseCount),
                std::uint64_t>);
        static_assert(
            std::is_same_v<
                decltype(XbRecordingSnapshot::totalPaused100ns),
                std::uint64_t>);
        static_assert(offsetof(XbRecordingSnapshot, pauseCount) == 1240);
        static_assert(offsetof(XbRecordingSnapshot, totalPaused100ns) == 1248);
        static_assert(offsetof(XbRecordingSnapshot, readyToPublish) == 1272);
        static_assert(offsetof(XbRecordingSnapshot, publishedPath) == 2336);

        Require(
            XbRecordingState_Idle == 0 &&
                XbRecordingState_Starting == 1 &&
                XbRecordingState_Recording == 2 &&
                XbRecordingState_Stopping == 3 &&
                XbRecordingState_Completed == 4 &&
                XbRecordingState_Failed == 5,
            "existing recording state values remain unchanged");
        Require(
            XbRecordingState_Pausing == 6 &&
                XbRecordingState_Paused == 7 &&
                XbRecordingState_Resuming == 8,
            "Pause Phase A recording state values are frozen");
    }

    void TestPausePhaseA()
    {
        TestNoPauseEquivalence();
        TestSinglePauseExclusionAndResumeSeam();
        TestMultiplePauseExclusion();
        TestPauseDeltaExcludedFromCadence();
        TestStrictDtsAndProjectionIsolation();
        TestStopWhileExcludedProjectionOpen();
        TestPauseAbiLayout();

        std::cout
            << "PAUSE-PHASE-A-EVIDENCE no-pause=equivalent "
               "single/multiple=excluded resume=continuous cadence=isolated "
               "sink=0/167/334 abi=2856/1240/1248\n"
            << "PAUSE-PHASE-A-TIMESTAMP-GATE = PASS\n";
    }

    void TestVideoPauseBarrierGate()
    {
        xbpreview::VideoPauseWorkerControl control;
        Require(
            !control.FrameCommitted(10),
            "Gate 1 initial frame commits without Resume acknowledgement");
        Require(control.RequestPause(), "Gate 1 Pause request is accepted");

        // A frame already inside the commit path may finish before the worker
        // reaches its next safe boundary.
        Require(
            !control.FrameCommitted(11),
            "Gate 1 in-flight frame completes before Pause acknowledgement");
        Require(
            control.AcknowledgePauseAtBoundary(),
            "Gate 1 worker publishes Pause acknowledgement at boundary");
        const auto acknowledged = control.Snapshot();
        Require(
            acknowledged.phase ==
                    xbpreview::VideoPauseWorkerPhase::Paused &&
                acknowledged.videoPauseAcks == 1 &&
                acknowledged.committedVideoSamples == 2 &&
                acknowledged.lastPauseCutoffSequence == 11,
            "Gate 1 Pause cutoff freezes after the in-flight commit");

        Require(
            control.ClassifyFrame(12) ==
                xbpreview::VideoPauseFrameDisposition::DiscardPaused,
            "Gate 1 post-ack frame is discarded");
        Require(
            control.Snapshot().committedVideoSamples == 2,
            "Gate 1 submitted frame count cannot grow after acknowledgement");
        std::cout << "B1-GATE-1-PAUSE-BARRIER = PASS\n";
    }

    void TestVideoPausedDrainGate()
    {
        xbpreview::VideoPauseWorkerControl control;
        (void)control.FrameCommitted(1);
        Require(control.RequestPause(), "Gate 2 Pause request is accepted");
        Require(
            control.AcknowledgePauseAtBoundary(),
            "Gate 2 Pause acknowledgement is published");

        std::uint64_t leasesAcquired{};
        std::uint64_t leasesReturned{};
        for (std::uint64_t sequence = 2; sequence <= 65; ++sequence)
        {
            ++leasesAcquired;
            Require(
                control.ClassifyFrame(sequence) ==
                    xbpreview::VideoPauseFrameDisposition::DiscardPaused,
                "Gate 2 paused lease bypasses Timestamp/conversion/write");
            ++leasesReturned;
        }
        const auto snapshot = control.Snapshot();
        Require(
            leasesAcquired == leasesReturned &&
                snapshot.pausedFramesDiscarded == 64 &&
                snapshot.committedVideoSamples == 1,
            "Gate 2 paused leases drain and return without queue growth");
        std::cout << "B1-GATE-2-FRAMETAP-DRAIN = PASS\n";
    }

    void TestVideoResumeCutoffGate()
    {
        xbpreview::VideoPauseWorkerControl control;
        (void)control.FrameCommitted(10);
        Require(control.RequestPause(), "Gate 3 Pause request is accepted");
        Require(
            control.AcknowledgePauseAtBoundary(),
            "Gate 3 Pause acknowledgement is published");
        Require(
            control.RequestResume(25),
            "Gate 3 Resume captures the deterministic sequence cutoff");
        Require(
            control.BeginResumeAtBoundary(),
            "Gate 3 worker opens the Resume gate");

        for (const auto staleSequence :
            std::array<std::uint64_t, 3>{ 23, 24, 25 })
        {
            Require(
                control.ClassifyFrame(staleSequence) ==
                    xbpreview::VideoPauseFrameDisposition::DiscardStaleResume,
                "Gate 3 pre-cutoff lease is stale");
        }
        Require(
            control.ClassifyFrame(26) ==
                xbpreview::VideoPauseFrameDisposition::Process,
            "Gate 3 first accepted Resume sequence is strictly after cutoff");
        const auto snapshot = control.Snapshot();
        Require(
            snapshot.lastResumeCutoffSequence == 25 &&
                snapshot.staleResumeFramesDiscarded == 3 &&
                snapshot.videoResumeAcks == 0,
            "Gate 3 cutoff does not acknowledge Resume before commit");
        std::cout << "B1-GATE-3-RESUME-CUTOFF = PASS\n";
    }

    void TestVideoResumeTimestampGate()
    {
        xbpreview::VideoPauseWorkerControl control;
        xbpreview::VideoEncoderTimestamp timestamp;

        const auto first = timestamp.Prepare(true, RawOrigin100ns);
        RequirePrepared(first, "Gate 4 first frame prepares");
        Require(timestamp.Commit(first), "Gate 4 first frame commits");
        (void)control.FrameCommitted(1);

        const auto established = timestamp.Prepare(
            true, RawOrigin100ns + 100'000);
        RequirePrepared(established, "Gate 4 cadence frame prepares");
        Require(timestamp.Commit(established), "Gate 4 cadence frame commits");
        (void)control.FrameCommitted(2);

        Require(control.RequestPause(), "Gate 4 Pause request is accepted");
        Require(
            control.AcknowledgePauseAtBoundary(),
            "Gate 4 Pause acknowledgement is published");
        timestamp.BeginExcludedInterval();
        Require(control.RequestResume(8), "Gate 4 Resume request is accepted");
        Require(
            control.BeginResumeAtBoundary(),
            "Gate 4 Resume transition begins");
        timestamp.EndExcludedInterval();
        Require(
            control.ClassifyFrame(8) ==
                xbpreview::VideoPauseFrameDisposition::DiscardStaleResume &&
                control.ClassifyFrame(9) ==
                    xbpreview::VideoPauseFrameDisposition::Process,
            "Gate 4 only the post-cutoff frame reaches Timestamp");

        const auto resumed = timestamp.Prepare(
            true, RawOrigin100ns + 500'000);
        RequirePrepared(resumed, "Gate 4 Resume frame prepares");
        Require(
            resumed.sourceTimeline.sampleTime100ns == 500'000 &&
                resumed.contentTimeline.sampleTime100ns ==
                    established.contentTimeline.endTime100ns &&
                resumed.videoSinkTimeline.sampleTime100ns >
                    established.videoSinkTimeline.sampleTime100ns,
            "Gate 4 source gap/content seam/strict sink ownership holds");
        Require(
            resumed.sourceTimeline.duration100ns == 100'000 &&
                timestamp.CurrentSourceDuration100ns() == 100'000 &&
                timestamp.TotalExcludedDuration100ns() == 300'000,
            "Gate 4 pause delta is excluded from content and cadence");
        Require(
            control.Snapshot().videoResumeAcks == 0,
            "Gate 4 Prepare alone cannot acknowledge Resume");
        Require(timestamp.Commit(resumed), "Gate 4 resumed WriteSample commits");
        Require(
            control.FrameCommitted(9),
            "Gate 4 Resume acknowledgement follows successful commit");

        const auto equal = timestamp.Prepare(
            true, RawOrigin100ns + 500'000);
        const auto decreasing = timestamp.Prepare(
            true, RawOrigin100ns + 499'999);
        const auto snapshot = control.Snapshot();
        Require(
            snapshot.videoResumeAcks == 1 &&
                snapshot.firstResumedFrameSequence == 9 &&
                equal.result ==
                    xbpreview::VideoTimestampPrepareResult::Regression &&
                decreasing.result ==
                    xbpreview::VideoTimestampPrepareResult::Regression,
            "Gate 4 Resume ack and zero equal/decreasing DTS hold");
        std::cout << "B1-GATE-4-RESUME-TIMESTAMP = PASS\n";
    }

    void TestVideoPauseStopPriorityGate()
    {
        xbpreview::VideoPauseWorkerControl pausing;
        (void)pausing.FrameCommitted(1);
        Require(pausing.RequestPause(), "Gate 5 Pausing request is accepted");
        Require(
            pausing.CancelForStop() &&
                !pausing.AcknowledgePauseAtBoundary() &&
                !pausing.CancelForStop(),
            "Gate 5 Stop preempts Pausing and enters terminal path once");

        xbpreview::VideoPauseWorkerControl paused;
        (void)paused.FrameCommitted(1);
        Require(paused.RequestPause(), "Gate 5 Paused request is accepted");
        Require(
            paused.AcknowledgePauseAtBoundary(),
            "Gate 5 Paused state is acknowledged");
        Require(
            paused.CancelForStop() && !paused.RequestResume(4),
            "Gate 5 Stop while Paused never requires Resume");

        xbpreview::VideoPauseWorkerControl resuming;
        (void)resuming.FrameCommitted(1);
        Require(resuming.RequestPause(), "Gate 5 Resume setup Pause accepted");
        Require(
            resuming.AcknowledgePauseAtBoundary() &&
                resuming.RequestResume(4) &&
                resuming.BeginResumeAtBoundary(),
            "Gate 5 Resuming state is reached");
        Require(
            resuming.CancelForStop() &&
                resuming.ClassifyFrame(5) ==
                    xbpreview::VideoPauseFrameDisposition::Stop &&
                resuming.Snapshot().videoResumeAcks == 0,
            "Gate 5 Stop preempts Resuming before first commit");
        Require(
            pausing.Snapshot().terminalStopTransitions == 1 &&
                paused.Snapshot().terminalStopTransitions == 1 &&
                resuming.Snapshot().terminalStopTransitions == 1,
            "Gate 5 terminal Stop transition is unique without deadlock");
        std::cout << "B1-GATE-5-STOP-PRIORITY = PASS\n";
    }

    void TestVideoPauseNoPauseRegressionGate()
    {
        xbpreview::VideoPauseWorkerControl control;
        xbpreview::VideoEncoderTimestamp timestamp;
        std::array<std::int64_t, 3> source{};
        std::array<std::int64_t, 3> content{};
        std::array<std::int64_t, 3> sink{};

        for (std::size_t index = 0; index < source.size(); ++index)
        {
            const auto sequence = static_cast<std::uint64_t>(index + 1);
            Require(
                control.ClassifyFrame(sequence) ==
                    xbpreview::VideoPauseFrameDisposition::Process,
                "Gate 6 normal frame is processed");
            const auto candidate = timestamp.Prepare(
                true,
                RawOrigin100ns + static_cast<std::int64_t>(index));
            RequirePrepared(candidate, "Gate 6 normal timestamp prepares");
            source[index] = candidate.sourceTimeline.sampleTime100ns;
            content[index] = candidate.contentTimeline.sampleTime100ns;
            sink[index] = candidate.videoSinkTimeline.sampleTime100ns;
            Require(timestamp.Commit(candidate), "Gate 6 timestamp commits");
            (void)control.FrameCommitted(sequence);
        }
        const auto snapshot = control.Snapshot();
        Require(
            source == std::array<std::int64_t, 3>{ 0, 1, 2 } &&
                content == std::array<std::int64_t, 3>{ 0, 1, 2 } &&
                sink == std::array<std::int64_t, 3>{ 0, 167, 334 },
            "Gate 6 frozen no-Pause projection remains equivalent");
        Require(
            snapshot.phase == xbpreview::VideoPauseWorkerPhase::Running &&
                snapshot.pauseRequests == 0 &&
                snapshot.resumeRequests == 0 &&
                snapshot.pausedFramesDiscarded == 0 &&
                snapshot.staleResumeFramesDiscarded == 0 &&
                snapshot.committedVideoSamples == 3,
            "Gate 6 worker control is inert without Pause requests");
        std::cout << "B1-GATE-6-NO-PAUSE-REGRESSION = PASS\n";
    }

    void TestPausePhaseB1()
    {
        TestVideoPauseBarrierGate();
        TestVideoPausedDrainGate();
        TestVideoResumeCutoffGate();
        TestVideoResumeTimestampGate();
        TestVideoPauseStopPriorityGate();
        TestVideoPauseNoPauseRegressionGate();
        std::cout << "PAUSE-PHASE-B1-VIDEO-WORKER-GATE = PASS\n";
    }

    void TestAudioPauseFifoModeGate(
        const PauseAudioMode mode,
        const std::string_view gateName)
    {
        xbpreview::VideoPauseWorkerControl video;
        xbpreview::AudioPauseWorkerControl audio;
        PauseAudioFifoModel model{ mode };
        model.writtenFrames = 960;
        (void)video.FrameCommitted(1);

        Require(video.RequestPause(), "B2 FIFO gate Video Pause requested");
        Require(audio.RequestPause(), "B2 FIFO gate Audio Pause requested");
        model.Capture(480, 480);
        model.ClearRecordedPcm();
        Require(
            audio.BeginDiscardAfterInitialClear(
                model.writtenFrames, true),
            "B2 FIFO gate opens discard only after initial clear");
        Require(
            video.AcknowledgePauseAtBoundary(),
            "B2 FIFO gate publishes Video Ack");
        Require(
            audio.AcknowledgePause(),
            "B2 FIFO gate publishes Audio Ack last");

        for (std::uint64_t wake = 0; wake < 3; ++wake)
        {
            model.Capture(240, 240);
            model.ClearRecordedPcm();
            audio.RecordPausedWakeClear();
        }

        const auto snapshot = audio.Snapshot();
        const auto expectedMicrophoneDiscarded = model.MicrophoneEnabled()
            ? 1'200ull
            : 0ull;
        const auto expectedSystemDiscarded = model.SystemEnabled()
            ? 1'200ull
            : 0ull;
        Require(
            model.captureRunning && model.microphoneFrames == 0 &&
                model.systemFrames == 0 && model.writeCalls == 0 &&
                model.writtenFrames == 960 &&
                model.microphoneFramesDiscarded ==
                    expectedMicrophoneDiscarded &&
                model.systemFramesDiscarded == expectedSystemDiscarded,
            "B2 FIFO gate keeps capture alive, drains active FIFOs, and freezes writes");
        Require(
            snapshot.phase == xbpreview::AudioPauseWorkerPhase::Paused &&
                snapshot.audioPauseAcks == 1 &&
                snapshot.fifoClearCalls == 4 &&
                snapshot.initialPauseClearCalls == 1 &&
                snapshot.pausedWakeClearCalls == 3 &&
                snapshot.audioFramesWrittenAtPause == 960 &&
                snapshot.discardGateActive,
            "B2 FIFO gate diagnostics identify initial and repeated clears");
        std::cout << gateName << " = PASS\n";
    }

    void TestAudioPauseMicrophoneGate()
    {
        TestAudioPauseFifoModeGate(
            PauseAudioMode::Microphone,
            "B2-GATE-1-MICROPHONE-FIFO");
    }

    void TestAudioPauseSystemGate()
    {
        TestAudioPauseFifoModeGate(
            PauseAudioMode::System,
            "B2-GATE-2-SYSTEM-FIFO");
    }

    void TestAudioPauseDualGate()
    {
        TestAudioPauseFifoModeGate(
            PauseAudioMode::Dual,
            "B2-GATE-3-DUAL-FIFO");
    }

    void TestAudioVideoBarrierGate()
    {
        xbpreview::VideoPauseWorkerControl video;
        xbpreview::AudioPauseWorkerControl audio;
        PauseAudioFifoModel model{ PauseAudioMode::Dual };
        model.writtenFrames = 960;
        (void)video.FrameCommitted(10);
        Require(
            audio.RequestPause() && video.RequestPause(),
            "Gate 4 full A/V Pause request is accepted");

        model.Capture(480, 480);
        model.ClearRecordedPcm();
        Require(
            audio.BeginDiscardAfterInitialClear(
                model.writtenFrames, true),
            "Gate 4 discard gate follows the FIFO clear");
        Require(
            video.AcknowledgePauseAtBoundary(),
            "Gate 4 Video Ack follows the audio discard gate");
        Require(
            video.Snapshot().phase ==
                    xbpreview::VideoPauseWorkerPhase::Paused &&
                audio.Snapshot().phase ==
                    xbpreview::AudioPauseWorkerPhase::PauseRequested,
            "Gate 4 full Paused is not visible before Audio Ack");
        Require(
            audio.AcknowledgePause(),
            "Gate 4 Audio Ack completes the full Paused barrier");
        Require(
            video.Snapshot().phase ==
                    xbpreview::VideoPauseWorkerPhase::Paused &&
                audio.Snapshot().phase ==
                    xbpreview::AudioPauseWorkerPhase::Paused,
            "Gate 4 both controls are Paused only after both acknowledgements");

        Require(
            audio.RequestResume() && video.RequestResume(20),
            "Gate 4 full A/V Resume request is accepted");
        model.Capture(2'400, 2'400);
        model.ClearRecordedPcm();
        Require(
            audio.BeginResumeAfterFinalClear(true) &&
                video.BeginResumeAtBoundary(),
            "Gate 4 both Resume gates open after the final clear");
        Require(
            video.ClassifyFrame(21) ==
                    xbpreview::VideoPauseFrameDisposition::Process &&
                video.FrameCommitted(21),
            "Gate 4 first post-cutoff video sample commits");
        Require(
            video.Snapshot().phase ==
                    xbpreview::VideoPauseWorkerPhase::Running &&
                audio.Snapshot().phase ==
                    xbpreview::AudioPauseWorkerPhase::Resuming,
            "Gate 4 full Recording waits after Video Resume Ack");
        model.WriteThroughTarget(250'000);
        Require(
            audio.AcknowledgeResume(model.writtenFrames),
            "Gate 4 Audio Resume Ack follows the resumed audio write target");
        const auto audioSnapshot = audio.Snapshot();
        Require(
            audioSnapshot.phase ==
                    xbpreview::AudioPauseWorkerPhase::Running &&
                audioSnapshot.audioPauseAcks == 1 &&
                audioSnapshot.audioResumeAcks == 1 &&
                audioSnapshot.finalResumeClearCalls == 1 &&
                !audioSnapshot.discardGateActive,
            "Gate 4 full Recording is restored only after both Resume acknowledgements");
        std::cout << "B2-GATE-4-FULL-AV-BARRIER = PASS\n";
    }

    void TestAudioVideoResumeSeamGate()
    {
        xbpreview::VideoPauseWorkerControl video;
        xbpreview::AudioPauseWorkerControl audio;
        xbpreview::VideoEncoderTimestamp timestamp;
        PauseAudioFifoModel model{ PauseAudioMode::Dual };

        const auto first = timestamp.Prepare(true, RawOrigin100ns);
        RequirePrepared(first, "Gate 5 first frame prepares");
        Require(timestamp.Commit(first), "Gate 5 first frame commits");
        (void)video.FrameCommitted(1);
        model.WriteThroughTarget(
            static_cast<std::uint64_t>(
                first.contentTimeline.endTime100ns));

        const auto established = timestamp.Prepare(
            true, RawOrigin100ns + 100'000);
        RequirePrepared(established, "Gate 5 cadence frame prepares");
        Require(
            timestamp.Commit(established),
            "Gate 5 cadence frame commits");
        (void)video.FrameCommitted(2);
        model.WriteThroughTarget(
            static_cast<std::uint64_t>(
                established.contentTimeline.endTime100ns));

        Require(
            audio.RequestPause() && video.RequestPause(),
            "Gate 5 full Pause request is accepted");
        model.Capture(960, 960);
        model.ClearRecordedPcm();
        Require(
            audio.BeginDiscardAfterInitialClear(
                model.writtenFrames, true),
            "Gate 5 initial stale PCM is removed");
        timestamp.BeginExcludedInterval();
        Require(
            video.AcknowledgePauseAtBoundary() &&
                audio.AcknowledgePause(),
            "Gate 5 full Pause is acknowledged");
        for (int wake = 0; wake < 2; ++wake)
        {
            model.Capture(4'800, 4'800);
            model.ClearRecordedPcm();
            audio.RecordPausedWakeClear();
        }
        const auto frozenFrames = model.writtenFrames;

        Require(
            audio.RequestResume() && video.RequestResume(5),
            "Gate 5 full Resume request is accepted");
        model.Capture(2'400, 2'400);
        model.ClearRecordedPcm();
        Require(
            audio.BeginResumeAfterFinalClear(true) &&
                video.BeginResumeAtBoundary(),
            "Gate 5 final stale PCM clear opens Resume");
        timestamp.EndExcludedInterval();
        Require(
            video.ClassifyFrame(5) ==
                    xbpreview::VideoPauseFrameDisposition::DiscardStaleResume &&
                video.ClassifyFrame(6) ==
                    xbpreview::VideoPauseFrameDisposition::Process,
            "Gate 5 only a post-cutoff frame reaches the resumed seam");

        const auto resumed = timestamp.Prepare(
            true, RawOrigin100ns + 700'000);
        RequirePrepared(resumed, "Gate 5 resumed frame prepares");
        Require(
            resumed.contentTimeline.sampleTime100ns ==
                    established.contentTimeline.endTime100ns &&
                resumed.sourceTimeline.sampleTime100ns >
                    resumed.contentTimeline.sampleTime100ns,
            "Gate 5 video content timeline excludes the pause gap");
        Require(timestamp.Commit(resumed), "Gate 5 resumed video commits");
        Require(video.FrameCommitted(6), "Gate 5 Video Resume Ack commits");
        model.WriteThroughTarget(
            static_cast<std::uint64_t>(
                resumed.contentTimeline.endTime100ns));
        Require(
            audio.AcknowledgeResume(model.writtenFrames),
            "Gate 5 Audio Resume Ack follows content-targeted writing");

        const auto snapshot = audio.Snapshot();
        Require(
            model.lastWriteStartFrame == frozenFrames &&
                model.lastWriteTarget100ns ==
                    static_cast<std::uint64_t>(
                        resumed.contentTimeline.endTime100ns) &&
                model.lastWriteTarget100ns !=
                    static_cast<std::uint64_t>(
                        resumed.sourceTimeline.endTime100ns) &&
                model.microphoneFrames == 0 &&
                model.systemFrames == 0 && model.captureRunning,
            "Gate 5 audio resumes contiguously from its frozen cursor on Content time");
        Require(
            snapshot.audioFramesWrittenAtPause == frozenFrames &&
                snapshot.audioFramesWrittenAtResume ==
                    model.writtenFrames &&
                snapshot.fifoClearCalls == 4,
            "Gate 5 diagnostics prove frozen cursor and final clear");
        std::cout << "B2-GATE-5-AV-RESUME-SEAM = PASS\n";
    }

    void TestAudioPauseStopPriorityGate()
    {
        const auto exerciseStop = []
        (
            const xbpreview::AudioPauseWorkerPhase stopFrom)
        {
            xbpreview::VideoPauseWorkerControl video;
            xbpreview::AudioPauseWorkerControl audio;
            PauseAudioFifoModel model{ PauseAudioMode::Dual };
            model.writtenFrames = 960;
            (void)video.FrameCommitted(1);
            Require(
                audio.RequestPause() && video.RequestPause(),
                "Gate 6 Pause request setup succeeds");
            if (stopFrom != xbpreview::AudioPauseWorkerPhase::PauseRequested)
            {
                model.ClearRecordedPcm();
                Require(
                    audio.BeginDiscardAfterInitialClear(
                        model.writtenFrames, true) &&
                        video.AcknowledgePauseAtBoundary() &&
                        audio.AcknowledgePause(),
                    "Gate 6 Paused setup succeeds");
            }
            if (stopFrom == xbpreview::AudioPauseWorkerPhase::Resuming)
            {
                Require(
                    audio.RequestResume() && video.RequestResume(4),
                    "Gate 6 Resume request setup succeeds");
                model.ClearRecordedPcm();
                Require(
                    audio.BeginResumeAfterFinalClear(true) &&
                        video.BeginResumeAtBoundary(),
                    "Gate 6 Resuming setup succeeds");
            }
            Require(
                audio.Phase() == stopFrom,
                "Gate 6 reaches the requested preempted audio state");
            Require(
                audio.CancelForStop() && video.CancelForStop() &&
                    !audio.CancelForStop() && !video.CancelForStop(),
                "Gate 6 Stop preempts both controls exactly once");
            model.StopAndDrain(300'000);
            model.StopAndDrain(300'000);
            const auto audioSnapshot = audio.Snapshot();
            const auto videoSnapshot = video.Snapshot();
            Require(
                audioSnapshot.phase ==
                    xbpreview::AudioPauseWorkerPhase::Stopping &&
                videoSnapshot.phase ==
                    xbpreview::VideoPauseWorkerPhase::Stopping &&
                audioSnapshot.terminalStopTransitions == 1 &&
                videoSnapshot.terminalStopTransitions == 1 &&
                model.stopCalls == 1 &&
                model.postStopDrainCalls == 1 &&
                !model.captureRunning,
                "Gate 6 terminal Stop and post-Stop FIFO drain are unique");
        };

        exerciseStop(xbpreview::AudioPauseWorkerPhase::PauseRequested);
        exerciseStop(xbpreview::AudioPauseWorkerPhase::Paused);
        exerciseStop(xbpreview::AudioPauseWorkerPhase::Resuming);
        std::cout << "B2-GATE-6-STOP-PRIORITY = PASS\n";
    }

    void TestAudioPauseNoPauseRegressionGate()
    {
        xbpreview::VideoPauseWorkerControl video;
        xbpreview::AudioPauseWorkerControl audio;
        xbpreview::VideoEncoderTimestamp timestamp;
        PauseAudioFifoModel model{ PauseAudioMode::Dual };
        std::array<std::int64_t, 4> sourceEnd{};
        std::array<std::int64_t, 4> contentEnd{};

        for (std::size_t index = 0; index < sourceEnd.size(); ++index)
        {
            const auto sequence = static_cast<std::uint64_t>(index + 1);
            const auto candidate = timestamp.Prepare(
                true,
                RawOrigin100ns +
                    static_cast<std::int64_t>(index) * 100'000);
            RequirePrepared(candidate, "Gate 7 no-Pause frame prepares");
            sourceEnd[index] = candidate.sourceTimeline.endTime100ns;
            contentEnd[index] = candidate.contentTimeline.endTime100ns;
            Require(
                timestamp.Commit(candidate),
                "Gate 7 no-Pause timestamp commits");
            Require(
                video.ClassifyFrame(sequence) ==
                    xbpreview::VideoPauseFrameDisposition::Process,
                "Gate 7 no-Pause video frame is processed");
            (void)video.FrameCommitted(sequence);
            model.Capture(480, 480);
            model.WriteThroughTarget(
                static_cast<std::uint64_t>(
                    candidate.contentTimeline.endTime100ns));
        }

        const auto audioSnapshot = audio.Snapshot();
        const auto videoSnapshot = video.Snapshot();
        Require(
            sourceEnd == contentEnd && model.clearCalls == 0 &&
                model.writeCalls == sourceEnd.size() &&
                model.lastWriteTarget100ns ==
                    static_cast<std::uint64_t>(sourceEnd.back()),
            "Gate 7 Content-targeted audio is identical without Pause");
        Require(
            audioSnapshot.phase ==
                    xbpreview::AudioPauseWorkerPhase::Running &&
                audioSnapshot.pauseRequests == 0 &&
                audioSnapshot.resumeRequests == 0 &&
                audioSnapshot.audioPauseAcks == 0 &&
                audioSnapshot.audioResumeAcks == 0 &&
                videoSnapshot.pauseRequests == 0 &&
                videoSnapshot.committedVideoSamples == sourceEnd.size(),
            "Gate 7 both Pause controls are inert in the no-Pause path");
        std::cout << "B2-GATE-7-NO-PAUSE-REGRESSION = PASS\n";
    }

    void TestCfrCadence(const std::uint32_t framesPerSecond)
    {
        xbpreview::VideoCfrCadence cadence(framesPerSecond);
        xbpreview::VideoEncoderTimestamp timestamp;
        const auto source = timestamp.Prepare(true, RawOrigin100ns);
        RequirePrepared(source, "CFR source observation prepares");
        const auto expectedFrames =
            static_cast<std::uint64_t>(framesPerSecond) * 10;
        std::int64_t durationSum{};
        std::int64_t previousSinkTime{ -1 };
        for (std::uint64_t index = 0; index < expectedFrames; ++index)
        {
            const auto cfr = cadence.PrepareNext();
            Require(
                cfr.frameIndex == index &&
                cfr.sampleTime100ns ==
                    xbpreview::VideoEncoderCfrTime100ns(
                        index, framesPerSecond) &&
                cfr.duration100ns ==
                    xbpreview::VideoEncoderCfrDuration100ns(
                        index, framesPerSecond) &&
                (framesPerSecond == 30
                    ? cfr.duration100ns == 333'333 ||
                        cfr.duration100ns == 333'334
                    : cfr.duration100ns == 166'666 ||
                        cfr.duration100ns == 166'667),
                "CFR rational tick matches the selected cadence");
            const auto candidate = timestamp.PrepareCfr(cfr);
            RequirePrepared(candidate, "CFR timestamp prepares");
            Require(
                candidate.videoSinkTimeline.sampleTime100ns >
                    previousSinkTime,
                "CFR sink projection remains strictly increasing");
            previousSinkTime =
                candidate.videoSinkTimeline.sampleTime100ns;
            durationSum += cfr.duration100ns;
            Require(
                timestamp.Commit(candidate) && cadence.Commit(cfr),
                "CFR tick commits transactionally");
        }
        const auto boundary = cadence.PrepareNext();
        Require(
            cadence.NextFrameIndex() == expectedFrames &&
            boundary.sampleTime100ns == 10'000'000 * 10ll &&
            durationSum == 10'000'000 * 10ll,
            "10-second CFR frame count and rational duration are exact");
        std::cout << "CFR-" << framesPerSecond
            << "-FPS-10S frames=" << expectedFrames << " PASS\n";
    }

    void TestCfrPauseAndMissedDeadline()
    {
        xbpreview::VideoCfrCadence cadence(60);
        xbpreview::VideoEncoderTimestamp timestamp;
        RequirePrepared(
            timestamp.Prepare(true, RawOrigin100ns),
            "CFR Pause initial source prepares");
        const auto first = cadence.PrepareNext();
        const auto firstCandidate = timestamp.PrepareCfr(first);
        RequirePrepared(firstCandidate, "CFR Pause first output prepares");
        Require(
            timestamp.Commit(firstCandidate) && cadence.Commit(first),
            "CFR Pause first output commits");
        timestamp.BeginExcludedInterval();
        timestamp.EndExcludedInterval();
        Require(
            cadence.NextFrameIndex() == 1,
            "Pause emits no CFR sample and does not advance frame index");
        RequirePrepared(
            timestamp.Prepare(true, RawOrigin100ns + 5'000'000),
            "CFR Resume fresh source observes the real pause gap");
        const auto resumed = cadence.PrepareNext();
        const auto resumedCandidate = timestamp.PrepareCfr(resumed);
        RequirePrepared(resumedCandidate, "CFR Resume output prepares");
        Require(
            resumed.sampleTime100ns == first.endTime100ns &&
            resumedCandidate.contentTimeline.sampleTime100ns ==
                first.endTime100ns &&
            timestamp.TotalExcludedDuration100ns() > 0,
            "CFR Resume continues the content index while excluding Pause");

        const auto missed = xbpreview::VideoEncoderCfrMissedDeadlineCount(
            5'000'000,
            xbpreview::VideoEncoderCfrDuration100ns(1, 60));
        Require(
            missed >= 29 && cadence.NextFrameIndex() == 1,
            "500 ms lateness is diagnosed without advancing or bursting ticks");
        Require(
            timestamp.Commit(resumedCandidate) && cadence.Commit(resumed) &&
            cadence.NextFrameIndex() == 2,
            "deadline recovery submits only one current tick");
        std::cout << "CFR-PAUSE-MISSED-DEADLINE no-burst=PASS\n";
    }

    void TestCadenceDiagnosticTrace()
    {
        static_assert(xbpreview::VideoCadenceTraceCapacity >= 4096);
        static_assert(std::is_trivially_copyable_v<
            xbpreview::VideoCadenceTraceRecord>);
        static_assert(noexcept(
            std::declval<xbpreview::VideoCadenceTraceBuffer&>().RecordTick(
                std::declval<xbpreview::VideoCadenceTraceRecord>())));
        static_assert(noexcept(
            std::declval<xbpreview::VideoCadenceTraceBuffer&>().
                ObserveSourceArrival(0, 0, 0)));

        auto trace =
            std::make_unique<xbpreview::VideoCadenceTraceBuffer>();
        trace->Reset(10'000'000);
        const auto record = [&]
        (
            const std::uint64_t tick,
            const std::uint64_t pendingSequence,
            const std::uint64_t lastFreshSequence,
            const std::int64_t deadlineQpc,
            const xbpreview::VideoCadenceDecision decision)
        {
            xbpreview::VideoCadenceTraceRecord value{};
            value.tickIndex = tick;
            value.selectedFps = 60;
            value.targetContentTime100ns =
                xbpreview::VideoEncoderCfrTime100ns(tick, 60);
            value.actualWakeQpc = deadlineQpc + 5;
            value.scheduledDeadlineQpc = deadlineQpc;
            value.deadlineErrorUs = 1;
            value.pendingFrameSequence = pendingSequence;
            value.pendingSourceTimestamp100ns =
                static_cast<std::int64_t>(pendingSequence) * 1'000;
            value.lastSubmittedFreshSequence = lastFreshSequence;
            value.lastSubmittedSourceTimestamp100ns =
                static_cast<std::int64_t>(lastFreshSequence) * 1'000;
            value.decision = decision;
            trace->RecordTick(value);
        };

        trace->ObserveSourceArrival(1, 1'000, 90);
        record(0, 1, 0, 100, xbpreview::VideoCadenceDecision::Fresh);
        record(1, 1, 1, 200, xbpreview::VideoCadenceDecision::Duplicate);
        trace->ObserveSourceArrival(2, 2'000, 250);
        record(2, 2, 1, 260, xbpreview::VideoCadenceDecision::Fresh);
        record(3, 2, 2, 300, xbpreview::VideoCadenceDecision::Duplicate);
        // Acquisition happens later, but the producer-owned enqueue fact says
        // this frame was already available before tick 3's deadline.
        trace->ObserveSourceArrival(3, 3'000, 290);
        record(4, 3, 2, 320, xbpreview::VideoCadenceDecision::Fresh);
        trace->ObserveSourceArrival(4, 4'000, 350);
        trace->ObserveSourceArrival(5, 5'000, 360);
        trace->ObservePendingReplacement();
        record(5, 5, 3, 400, xbpreview::VideoCadenceDecision::Fresh);
        record(6, 5, 5, 500, xbpreview::VideoCadenceDecision::Duplicate);
        trace->FinalizeDuplicateClassifications();

        Require(
            trace->totalTicks == 7 && trace->fresh == 4 &&
                trace->duplicate == 3 &&
                trace->duplicateWithNoNewSourceAvailable == 2 &&
                trace->duplicateDespiteFreshAvailableBeforeDeadline == 1,
            "cadence trace classifies exact normal and avoidable duplicates");
        Require(
            trace->normalMultiSourceCadenceDrops == 1 &&
                trace->dropThenNextTickDuplicateCount == 1 &&
                trace->totalSourceArrivals == 5,
            "cadence trace preserves arrival, replacement, and drop chain facts");
        const auto* avoidable = trace->FindRecord(3);
        Require(
            avoidable != nullptr &&
                avoidable->freshAvailableBeforeDeadline &&
                avoidable->freshAvailableSequenceBeforeDeadline == 3 &&
                avoidable->duplicateClassification ==
                    xbpreview::VideoCadenceDuplicateClassification::
                        AvoidableHandoffLoss,
            "future acquisition evidence updates the exact duplicate tick");
        const auto* dropThenDuplicate = trace->FindRecord(6);
        Require(
            dropThenDuplicate != nullptr &&
                dropThenDuplicate->dropThenNextTickDuplicate,
            "cadence drop followed by next-tick duplicate is explicit");

        xbpreview::VideoCfrCadence cadence(60);
        auto semanticProbe =
            std::make_unique<xbpreview::VideoCadenceTraceBuffer>();
        semanticProbe->Reset(10'000'000);
        constexpr std::uint64_t SemanticProbeTicks = 120;
        for (std::uint64_t index = 0; index < SemanticProbeTicks; ++index)
        {
            const auto timing = cadence.PrepareNext();
            xbpreview::VideoCadenceTraceRecord value{};
            value.tickIndex = timing.frameIndex;
            value.targetContentTime100ns = timing.sampleTime100ns;
            value.decision = xbpreview::VideoCadenceDecision::Fresh;
            semanticProbe->RecordTick(value);
            Require(
                cadence.NextFrameIndex() == index,
                "trace record does not advance scheduler output count");
            Require(
                cadence.Commit(timing),
                "semantic probe commits the untouched CFR candidate");
        }
        Require(
            cadence.NextFrameIndex() == SemanticProbeTicks &&
                semanticProbe->totalTicks == SemanticProbeTicks,
            "trace count observes but does not own CFR cadence");

        auto ring =
            std::make_unique<xbpreview::VideoCadenceTraceBuffer>();
        ring->Reset(10'000'000);
        for (std::uint64_t index = 0;
            index <= xbpreview::VideoCadenceTraceCapacity;
            ++index)
        {
            xbpreview::VideoCadenceTraceRecord value{};
            value.tickIndex = index;
            value.decision = xbpreview::VideoCadenceDecision::Missed;
            ring->RecordTick(value);
        }
        Require(
            ring->recordCount == xbpreview::VideoCadenceTraceCapacity &&
                ring->traceRecordsOverwritten == 1 &&
                ring->FindRecord(0) == nullptr &&
                ring->FindRecord(xbpreview::VideoCadenceTraceCapacity) != nullptr,
            "fixed cadence ring overwrites deterministically without growth");
        std::cout
            << "CADENCE-DIAGNOSTICS ring=4096 semantic-diff=0 PASS\n";
    }

    void TestH264CapabilityContract()
    {
        xbpreview::VideoEncoderDiagnostics diagnostics{};
        diagnostics.selectedFps = 60;
        diagnostics.outputWidth = 1920;
        diagnostics.outputHeight = 1080;
        diagnostics.bitrate = 8'000'000;
        auto& capabilities = diagnostics.encoderCapabilities;
        capabilities.probeAttempted = true;
        capabilities.probeHResult = E_FAIL;
        capabilities.actualTransformObtained = false;
        capabilities.codecApiHResult = E_NOINTERFACE;
        capabilities.propertyCount = 1;
        auto& unsupported = capabilities.properties[0];
        unsupported.property = "CODECAPI_AVEncVideoEncodeQP";
        unsupported.isSupportedHResult = E_NOTIMPL;
        unsupported.isSupported = false;

        const auto first =
            xbpreview::SerializeVideoEncoderCapabilities(diagnostics);
        const auto second =
            xbpreview::SerializeVideoEncoderCapabilities(diagnostics);
        Require(
            !first.empty() && first == second &&
                first.size() <=
                    xbpreview::VideoEncoderCapabilityJsonByteLimit,
            "capability JSON is deterministic and bounded");
        Require(
            first.find("\"SelectedFps\":60") != std::string::npos &&
                first.find("\"OutputWidth\":1920") != std::string::npos &&
                first.find("\"OutputHeight\":1080") != std::string::npos &&
                first.find("\"NominalBitrate\":8000000") !=
                    std::string::npos,
            "capability JSON preserves only the selected encoding context");
        Require(
            first.find("\"ActualTransformObtained\":false") !=
                    std::string::npos &&
                first.find("\"IsSupported\":false") != std::string::npos &&
                first.find("\"CurrentValue\":\"N/A\"") !=
                    std::string::npos,
            "probe failure and unsupported property remain diagnostic facts");

        capabilities.propertyCount =
            xbpreview::VideoEncoderCapabilityPropertyCapacity;
        const std::string oversized(2'048, '\n');
        for (auto& property : capabilities.properties)
        {
            property.property = oversized;
            property.possibleValues.assign(
                xbpreview::VideoEncoderCapabilityPossibleValueLimit + 4,
                oversized);
        }
        const auto bounded =
            xbpreview::SerializeVideoEncoderCapabilities(diagnostics);
        Require(
            !bounded.empty() &&
                bounded.size() <=
                    xbpreview::VideoEncoderCapabilityJsonByteLimit &&
                bounded.find("\"SerializationStatus\":\"BOUNDED_OVERFLOW\"") !=
                    std::string::npos,
            "oversized capability evidence falls back to valid bounded JSON");
        std::cout
            << "H264-CAPABILITY read-only failure-safe bounded-json PASS\n";
    }

    void TestH264EncoderStartupConfigurationContract()
    {
        constexpr std::uint32_t targetBitrate = 14'000'000;
        const auto startup =
            xbpreview::CreateH264EncoderStartupConfiguration(targetBitrate);
        Require(
            startup.rateControlMode == static_cast<std::uint32_t>(
                eAVEncCommonRateControlMode_CBR) &&
                startup.meanBitrate == targetBitrate,
            "startup plan uses CBR and the immutable session bitrate");

        winrt::com_ptr<IMFAttributes> encodingParameters;
        const auto storeCreationResult = MFCreateAttributes(
            encodingParameters.put(), 2);
        HRESULT rateControlResult{ E_PENDING };
        HRESULT meanBitrateResult{ E_PENDING };
        const auto configurationResult = SUCCEEDED(storeCreationResult)
            ? xbpreview::ApplyH264EncoderStartupConfiguration(
                startup,
                [&encodingParameters](
                    const GUID& property,
                    const std::uint32_t value) noexcept
                {
                    return encodingParameters->SetUINT32(property, value);
                },
                rateControlResult,
                meanBitrateResult)
            : storeCreationResult;
        UINT32 propertyCount{};
        const auto countResult = SUCCEEDED(configurationResult)
            ? encodingParameters->GetCount(&propertyCount)
            : configurationResult;
        UINT32 storedRateControl{};
        UINT32 storedMeanBitrate{};
        const auto storedRateControlResult = SUCCEEDED(countResult)
            ? encodingParameters->GetUINT32(
                CODECAPI_AVEncCommonRateControlMode,
                &storedRateControl)
            : countResult;
        const auto storedMeanBitrateResult =
            SUCCEEDED(storedRateControlResult)
            ? encodingParameters->GetUINT32(
                CODECAPI_AVEncCommonMeanBitRate,
                &storedMeanBitrate)
            : storedRateControlResult;
        Require(
            storeCreationResult == S_OK && configurationResult == S_OK &&
                rateControlResult == S_OK && meanBitrateResult == S_OK &&
                countResult == S_OK && propertyCount == 2 &&
                storedRateControlResult == S_OK &&
                storedRateControl == startup.rateControlMode &&
                storedMeanBitrateResult == S_OK &&
                storedMeanBitrate == targetBitrate,
            "SetInputMediaType encoding parameters retain both UINT32 values");

        std::size_t calls{};
        const auto injectedFailure = HRESULT_FROM_WIN32(ERROR_WRITE_FAULT);
        rateControlResult = S_OK;
        meanBitrateResult = S_OK;
        const auto failureResult =
            xbpreview::ApplyH264EncoderStartupConfiguration(
                startup,
                [&](const GUID&, const std::uint32_t)
                {
                    ++calls;
                    return injectedFailure;
                },
                rateControlResult,
                meanBitrateResult);
        Require(
            failureResult == injectedFailure && calls == 1 &&
                rateControlResult == injectedFailure &&
                meanBitrateResult == E_PENDING,
            "startup configuration preserves exact failure and stops");

        xbpreview::VideoEncoderDiagnostics softwareFallback;
        softwareFallback.encoderCapabilities.actualTransformObtained = true;
        softwareFallback.encoderCapabilities.probeHResult = S_OK;
        softwareFallback.encoderCapabilities.hardwareSoftwareVerdict =
            "SOFTWARE";
        const auto fallbackResult =
            xbpreview::VerifyProductionHardwareEncoder(softwareFallback);
        Require(
            fallbackResult == MF_E_TOPO_CODEC_NOT_FOUND &&
                softwareFallback.productionHardwareEncoderRequired &&
                !softwareFallback.actualHardwareEncoderVerified &&
                softwareFallback.softwareFallbackDetected &&
                softwareFallback.softwareFallbackRejected &&
                softwareFallback.failureStage ==
                    "VerifyHardwareVideoEncoder" &&
                softwareFallback.failureHResult == fallbackResult,
            "actual software MFT is detected and rejected before recording");

        xbpreview::VideoEncoderDiagnostics hardwareEncoder;
        hardwareEncoder.encoderCapabilities.actualTransformObtained = true;
        hardwareEncoder.encoderCapabilities.probeHResult = S_OK;
        hardwareEncoder.encoderCapabilities.hardwareSoftwareVerdict =
            "HARDWARE";
        Require(
            xbpreview::VerifyProductionHardwareEncoder(hardwareEncoder) == S_OK &&
                hardwareEncoder.productionHardwareEncoderRequired &&
                hardwareEncoder.actualHardwareEncoderVerified &&
                !hardwareEncoder.softwareFallbackDetected &&
                !hardwareEncoder.softwareFallbackRejected,
            "actual hardware MFT satisfies the production gate");
        std::cout
            << "H264-ENCODER-INPUT-PARAMETERS CBR source-of-truth PASS\n";
    }

    void TestRecordingVideoBitratePolicy()
    {
        struct PolicyCase final
        {
            std::uint32_t width;
            std::uint32_t height;
            std::uint32_t fps;
            std::uint32_t bitrate;
        };
        constexpr std::array<PolicyCase, 10> cases{
            PolicyCase{ 1920, 1080, 30, 12'000'000 },
            PolicyCase{ 1920, 1080, 60, 12'000'000 },
            PolicyCase{ 2560, 1440, 30, 12'000'000 },
            PolicyCase{ 2560, 1440, 60, 12'000'000 },
            PolicyCase{ 3840, 2160, 30, 12'000'000 },
            PolicyCase{ 3840, 2160, 60, 12'000'000 },
            PolicyCase{ 2560, 1600, 30, 12'000'000 },
            PolicyCase{ 2560, 1600, 60, 12'000'000 },
            PolicyCase{ 3440, 1440, 30, 12'000'000 },
            PolicyCase{ 3440, 1440, 60, 12'000'000 },
        };
        for (const auto& value : cases)
        {
            Require(
                xbpreview::RecordingVideoTargetBitrate(
                    value.width, value.height, value.fps) == value.bitrate,
                "resolution x FPS bitrate policy is deterministic");
        }
        Require(
            xbpreview::RecordingVideoTargetBitrate(0, 1080, 60) == 0 &&
                xbpreview::RecordingVideoTargetBitrate(1920, 0, 60) == 0 &&
                xbpreview::RecordingVideoTargetBitrate(1920, 1080, 24) == 0,
            "invalid bitrate policy inputs fail closed");
        Require(
            xbpreview::RecordingVideoTargetBitrate(UINT32_MAX, UINT32_MAX, 60) ==
                xbpreview::RecordingVideoBitrate,
            "extreme bitrate policy input saturates without overflow");

        constexpr std::uint32_t legacyOutputRootBitrate = 8'000'000;
        const auto legacyThresholds =
            xbpreview::ComputeRecordingStorageThresholds(
                legacyOutputRootBitrate);
        const auto formalThresholds =
            xbpreview::ComputeRecordingStorageThresholds(
                xbpreview::RecordingVideoBitrate);
        const auto boundaryFreeBytes =
            legacyThresholds.startupBytes +
            (formalThresholds.startupBytes -
                legacyThresholds.startupBytes) / 2;
        Require(
            xbpreview::EvaluateRecordingStorageSpace(
                boundaryFreeBytes, legacyOutputRootBitrate, true) ==
                    xbpreview::RecordingStorageStatus::Ready &&
                xbpreview::EvaluateRecordingStorageSpace(
                    boundaryFreeBytes,
                    xbpreview::RecordingVideoBitrate,
                    true) == xbpreview::RecordingStorageStatus::CriticalSpace,
            "12M output-root preflight rejects the boundary allowed by stale 8M");

        std::ifstream engineFile(
            std::filesystem::path("XbPreview.Native") / "PreviewEngine.cpp",
            std::ios::binary);
        Require(engineFile.good(),
            "PreviewEngine.cpp is available for the product seam contract");
        const std::string engineSource{
            std::istreambuf_iterator<char>(engineFile),
            std::istreambuf_iterator<char>() };
        const auto startBegin = engineSource.find(
            "XbPreviewResult PreviewEngine::StartRecording()");
        const auto startEnd = engineSource.find(
            "XbPreviewResult PreviewEngine::PauseRecording()", startBegin);
        const auto outputRootBegin = engineSource.find(
            "XbPreviewResult PreviewEngine::SetRecordingOutputRoot(");
        const auto outputRootEnd = engineSource.find(
            "XbPreviewResult PreviewEngine::SetRecordingFrameRate(",
            outputRootBegin);
        Require(
            startBegin != std::string::npos &&
                startEnd != std::string::npos &&
                outputRootBegin != std::string::npos &&
                outputRootEnd != std::string::npos,
            "recording Start and output-root seams remain discoverable");
        const std::string_view startSource(
            engineSource.data() + startBegin, startEnd - startBegin);
        const std::string_view outputRootSource(
            engineSource.data() + outputRootBegin,
            outputRootEnd - outputRootBegin);
        Require(
            startSource.find(
                "configuration.bitrate = RecordingVideoTargetBitrate(") !=
                    std::string_view::npos &&
                outputRootSource.find(
                    "selected, RecordingVideoBitrate") !=
                    std::string_view::npos &&
                outputRootSource.find("8'000'000") ==
                    std::string_view::npos,
            "Start and output-root preflight share the formal bitrate truth");
        std::cout
            << "VIDEO-BITRATE-POLICY storage-estimate-12M PASS\n";
    }
}

int main(const int argc, const char* const argv[])
{
    Require(argc == 2, "provide one targeted Gate selector");
    const std::string_view selector(argv[1]);
    if (selector == "--timestamp-transaction")
    {
        TestTimestampTransaction();
        return EXIT_SUCCESS;
    }
    if (selector == "--source-sink-boundary")
    {
        TestSourceSinkBoundary();
        return EXIT_SUCCESS;
    }
    if (selector == "--pause-phase-a")
    {
        TestPausePhaseA();
        return EXIT_SUCCESS;
    }
    if (selector == "--pause-phase-b1")
    {
        TestPausePhaseB1();
        return EXIT_SUCCESS;
    }
    if (selector == "--pause-phase-b2-gate-1")
    {
        TestAudioPauseMicrophoneGate();
        return EXIT_SUCCESS;
    }
    if (selector == "--pause-phase-b2-gate-2")
    {
        TestAudioPauseSystemGate();
        return EXIT_SUCCESS;
    }
    if (selector == "--pause-phase-b2-gate-3")
    {
        TestAudioPauseDualGate();
        return EXIT_SUCCESS;
    }
    if (selector == "--pause-phase-b2-gate-4")
    {
        TestAudioVideoBarrierGate();
        return EXIT_SUCCESS;
    }
    if (selector == "--pause-phase-b2-gate-5")
    {
        TestAudioVideoResumeSeamGate();
        return EXIT_SUCCESS;
    }
    if (selector == "--pause-phase-b2-gate-6")
    {
        TestAudioPauseStopPriorityGate();
        return EXIT_SUCCESS;
    }
    if (selector == "--pause-phase-b2-gate-7")
    {
        TestAudioPauseNoPauseRegressionGate();
        return EXIT_SUCCESS;
    }
    if (selector == "--cfr-30")
    {
        TestCfrCadence(30);
        return EXIT_SUCCESS;
    }
    if (selector == "--cfr-60")
    {
        TestCfrCadence(60);
        return EXIT_SUCCESS;
    }
    if (selector == "--cfr-pause-missed-deadline")
    {
        TestCfrPauseAndMissedDeadline();
        return EXIT_SUCCESS;
    }
    if (selector == "--cadence-diagnostics")
    {
        TestCadenceDiagnosticTrace();
        return EXIT_SUCCESS;
    }
    if (selector == "--h264-capability-contract")
    {
        TestH264CapabilityContract();
        return EXIT_SUCCESS;
    }
    if (selector == "--h264-encoder-config-contract")
    {
        TestH264EncoderStartupConfigurationContract();
        return EXIT_SUCCESS;
    }
    if (selector == "--video-bitrate-policy")
    {
        TestRecordingVideoBitratePolicy();
        return EXIT_SUCCESS;
    }
    Require(false, "unknown targeted Gate selector");
    return EXIT_FAILURE;
}
