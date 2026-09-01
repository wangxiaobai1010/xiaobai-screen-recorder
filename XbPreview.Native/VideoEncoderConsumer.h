#pragma once

#include "RenderFrameTap.h"
#include "VideoEncoderConfig.h"
#include "XbPreviewApi.h"

#include <d3d11.h>

#include <atomic>
#include <cstdint>
#include <memory>

namespace xbpreview
{
    enum class RecordingTerminationDisposition : std::uint32_t
    {
        Unspecified,
        Publish,
        UserCancelled,
    };

    struct VideoDeviceSetupStatus
    {
        bool videoSupportRequested{};
        bool videoSupportDeviceCreated{};
        bool multithreadProtectionAvailable{};
        bool multithreadProtectionEnabled{};
        HRESULT videoDeviceCreationResult{ S_OK };
    };

    enum class VideoPauseWorkerPhase : std::uint32_t
    {
        Running,
        PauseRequested,
        Paused,
        ResumeRequested,
        Resuming,
        Stopping,
    };

    enum class VideoPauseFrameDisposition : std::uint32_t
    {
        Process,
        DiscardPaused,
        DiscardStaleResume,
        Stop,
    };

    struct VideoPauseWorkerSnapshot
    {
        VideoPauseWorkerPhase phase{ VideoPauseWorkerPhase::Running };
        std::uint64_t pauseRequests{};
        std::uint64_t videoPauseAcks{};
        std::uint64_t resumeRequests{};
        std::uint64_t videoResumeAcks{};
        std::uint64_t pausedFramesDiscarded{};
        std::uint64_t staleResumeFramesDiscarded{};
        std::uint64_t committedVideoSamples{};
        std::uint64_t lastCommittedFrameSequence{};
        std::uint64_t lastPauseCutoffSequence{};
        std::uint64_t lastResumeCutoffSequence{};
        std::uint64_t firstResumedFrameSequence{};
        std::uint64_t terminalStopTransitions{};
    };

    enum class AudioPauseWorkerPhase : std::uint32_t
    {
        Running,
        PauseRequested,
        Paused,
        ResumeRequested,
        Resuming,
        Stopping,
    };

    struct AudioPauseWorkerSnapshot
    {
        AudioPauseWorkerPhase phase{ AudioPauseWorkerPhase::Running };
        std::uint64_t pauseRequests{};
        std::uint64_t audioPauseAcks{};
        std::uint64_t resumeRequests{};
        std::uint64_t audioResumeAcks{};
        std::uint64_t fifoClearCalls{};
        std::uint64_t initialPauseClearCalls{};
        std::uint64_t pausedWakeClearCalls{};
        std::uint64_t finalResumeClearCalls{};
        std::uint64_t audioFramesWrittenAtPause{};
        std::uint64_t audioFramesWrittenAtResume{};
        std::uint64_t terminalStopTransitions{};
        bool discardGateActive{};
    };

    // Internal-only B2 audio barrier. The worker supplies the result of the
    // real XbAudioAdapter FIFO clear before advancing either edge. Keeping the
    // audio phase independent from the video phase makes the full A/V barrier
    // observable without exposing a premature public Pause contract.
    class AudioPauseWorkerControl final
    {
    public:
        void Reset() noexcept
        {
            phase_.store(AudioPauseWorkerPhase::Running);
            pauseRequests_.store(0);
            audioPauseAcks_.store(0);
            resumeRequests_.store(0);
            audioResumeAcks_.store(0);
            fifoClearCalls_.store(0);
            initialPauseClearCalls_.store(0);
            pausedWakeClearCalls_.store(0);
            finalResumeClearCalls_.store(0);
            audioFramesWrittenAtPause_.store(0);
            audioFramesWrittenAtResume_.store(0);
            terminalStopTransitions_.store(0);
            discardGateActive_.store(false);
        }

        [[nodiscard]] bool RequestPause() noexcept
        {
            auto expected = AudioPauseWorkerPhase::Running;
            if (!phase_.compare_exchange_strong(
                    expected,
                    AudioPauseWorkerPhase::PauseRequested,
                    std::memory_order_acq_rel))
            {
                return false;
            }
            pauseRequests_.fetch_add(1, std::memory_order_relaxed);
            return true;
        }

        [[nodiscard]] bool BeginDiscardAfterInitialClear(
            const std::uint64_t audioFramesWritten,
            const bool fifoWasCleared) noexcept
        {
            if (phase_.load(std::memory_order_acquire) !=
                    AudioPauseWorkerPhase::PauseRequested ||
                discardGateActive_.exchange(true, std::memory_order_acq_rel))
            {
                return false;
            }
            audioFramesWrittenAtPause_.store(
                audioFramesWritten, std::memory_order_release);
            if (fifoWasCleared)
            {
                fifoClearCalls_.fetch_add(1, std::memory_order_relaxed);
                initialPauseClearCalls_.fetch_add(
                    1, std::memory_order_relaxed);
            }
            return true;
        }

        [[nodiscard]] bool AcknowledgePause() noexcept
        {
            if (!discardGateActive_.load(std::memory_order_acquire))
            {
                return false;
            }
            auto expected = AudioPauseWorkerPhase::PauseRequested;
            if (!phase_.compare_exchange_strong(
                    expected,
                    AudioPauseWorkerPhase::Paused,
                    std::memory_order_acq_rel))
            {
                return false;
            }
            audioPauseAcks_.fetch_add(1, std::memory_order_relaxed);
            return true;
        }

        void RecordPausedWakeClear() noexcept
        {
            if (phase_.load(std::memory_order_acquire) ==
                    AudioPauseWorkerPhase::Paused &&
                discardGateActive_.load(std::memory_order_acquire))
            {
                fifoClearCalls_.fetch_add(1, std::memory_order_relaxed);
                pausedWakeClearCalls_.fetch_add(1, std::memory_order_relaxed);
            }
        }

        [[nodiscard]] bool RequestResume() noexcept
        {
            auto expected = AudioPauseWorkerPhase::Paused;
            if (!phase_.compare_exchange_strong(
                    expected,
                    AudioPauseWorkerPhase::ResumeRequested,
                    std::memory_order_acq_rel))
            {
                return false;
            }
            resumeRequests_.fetch_add(1, std::memory_order_relaxed);
            return true;
        }

        [[nodiscard]] bool BeginResumeAfterFinalClear(
            const bool fifoWasCleared) noexcept
        {
            auto expected = AudioPauseWorkerPhase::ResumeRequested;
            if (!phase_.compare_exchange_strong(
                    expected,
                    AudioPauseWorkerPhase::Resuming,
                    std::memory_order_acq_rel))
            {
                return false;
            }
            if (fifoWasCleared)
            {
                fifoClearCalls_.fetch_add(1, std::memory_order_relaxed);
                finalResumeClearCalls_.fetch_add(
                    1, std::memory_order_relaxed);
            }
            discardGateActive_.store(false, std::memory_order_release);
            return true;
        }

        [[nodiscard]] bool AcknowledgeResume(
            const std::uint64_t audioFramesWritten) noexcept
        {
            auto expected = AudioPauseWorkerPhase::Resuming;
            if (!phase_.compare_exchange_strong(
                    expected,
                    AudioPauseWorkerPhase::Running,
                    std::memory_order_acq_rel))
            {
                return false;
            }
            audioFramesWrittenAtResume_.store(
                audioFramesWritten, std::memory_order_release);
            audioResumeAcks_.fetch_add(1, std::memory_order_relaxed);
            return true;
        }

        [[nodiscard]] bool CancelForStop() noexcept
        {
            const auto previous = phase_.exchange(
                AudioPauseWorkerPhase::Stopping,
                std::memory_order_acq_rel);
            discardGateActive_.store(false, std::memory_order_release);
            if (previous == AudioPauseWorkerPhase::Stopping)
            {
                return false;
            }
            terminalStopTransitions_.fetch_add(1, std::memory_order_relaxed);
            return true;
        }

        [[nodiscard]] AudioPauseWorkerPhase Phase() const noexcept
        {
            return phase_.load(std::memory_order_acquire);
        }

        [[nodiscard]] AudioPauseWorkerSnapshot Snapshot() const noexcept
        {
            AudioPauseWorkerSnapshot value{};
            value.phase = phase_.load(std::memory_order_acquire);
            value.pauseRequests = pauseRequests_.load();
            value.audioPauseAcks = audioPauseAcks_.load();
            value.resumeRequests = resumeRequests_.load();
            value.audioResumeAcks = audioResumeAcks_.load();
            value.fifoClearCalls = fifoClearCalls_.load();
            value.initialPauseClearCalls = initialPauseClearCalls_.load();
            value.pausedWakeClearCalls = pausedWakeClearCalls_.load();
            value.finalResumeClearCalls = finalResumeClearCalls_.load();
            value.audioFramesWrittenAtPause =
                audioFramesWrittenAtPause_.load();
            value.audioFramesWrittenAtResume =
                audioFramesWrittenAtResume_.load();
            value.terminalStopTransitions = terminalStopTransitions_.load();
            value.discardGateActive = discardGateActive_.load();
            return value;
        }

    private:
        std::atomic<AudioPauseWorkerPhase> phase_{
            AudioPauseWorkerPhase::Running };
        std::atomic<std::uint64_t> pauseRequests_{};
        std::atomic<std::uint64_t> audioPauseAcks_{};
        std::atomic<std::uint64_t> resumeRequests_{};
        std::atomic<std::uint64_t> audioResumeAcks_{};
        std::atomic<std::uint64_t> fifoClearCalls_{};
        std::atomic<std::uint64_t> initialPauseClearCalls_{};
        std::atomic<std::uint64_t> pausedWakeClearCalls_{};
        std::atomic<std::uint64_t> finalResumeClearCalls_{};
        std::atomic<std::uint64_t> audioFramesWrittenAtPause_{};
        std::atomic<std::uint64_t> audioFramesWrittenAtResume_{};
        std::atomic<std::uint64_t> terminalStopTransitions_{};
        std::atomic<bool> discardGateActive_{};
    };

    // Internal-only B1 control plane. This type is intentionally absent from
    // XbPreviewApi.h: it coordinates the Video worker without claiming that
    // full A/V Pause is available to managed or C ABI consumers.
    class VideoPauseWorkerControl final
    {
    public:
        void Reset() noexcept
        {
            phase_.store(VideoPauseWorkerPhase::Running);
            pauseRequests_.store(0);
            videoPauseAcks_.store(0);
            resumeRequests_.store(0);
            videoResumeAcks_.store(0);
            pausedFramesDiscarded_.store(0);
            staleResumeFramesDiscarded_.store(0);
            committedVideoSamples_.store(0);
            lastCommittedFrameSequence_.store(0);
            lastPauseCutoffSequence_.store(0);
            lastResumeCutoffSequence_.store(0);
            firstResumedFrameSequence_.store(0);
            terminalStopTransitions_.store(0);
        }

        [[nodiscard]] bool RequestPause() noexcept
        {
            auto expected = VideoPauseWorkerPhase::Running;
            if (!phase_.compare_exchange_strong(
                    expected,
                    VideoPauseWorkerPhase::PauseRequested,
                    std::memory_order_acq_rel))
            {
                return false;
            }
            pauseRequests_.fetch_add(1, std::memory_order_relaxed);
            return true;
        }

        [[nodiscard]] bool AcknowledgePauseAtBoundary() noexcept
        {
            const auto cutoff =
                lastCommittedFrameSequence_.load(std::memory_order_acquire);
            auto expected = VideoPauseWorkerPhase::PauseRequested;
            if (!phase_.compare_exchange_strong(
                    expected,
                    VideoPauseWorkerPhase::Paused,
                    std::memory_order_acq_rel))
            {
                return false;
            }
            lastPauseCutoffSequence_.store(cutoff, std::memory_order_release);
            videoPauseAcks_.fetch_add(1, std::memory_order_relaxed);
            return true;
        }

        [[nodiscard]] bool RequestResume(
            const std::uint64_t cutoffSequence) noexcept
        {
            lastResumeCutoffSequence_.store(
                cutoffSequence, std::memory_order_relaxed);
            auto expected = VideoPauseWorkerPhase::Paused;
            if (!phase_.compare_exchange_strong(
                    expected,
                    VideoPauseWorkerPhase::ResumeRequested,
                    std::memory_order_acq_rel,
                    std::memory_order_acquire))
            {
                return false;
            }
            resumeRequests_.fetch_add(1, std::memory_order_relaxed);
            return true;
        }

        [[nodiscard]] bool BeginResumeAtBoundary() noexcept
        {
            auto expected = VideoPauseWorkerPhase::ResumeRequested;
            return phase_.compare_exchange_strong(
                expected,
                VideoPauseWorkerPhase::Resuming,
                std::memory_order_acq_rel);
        }

        [[nodiscard]] VideoPauseFrameDisposition ClassifyFrame(
            const std::uint64_t frameSequence) noexcept
        {
            const auto phase = phase_.load(std::memory_order_acquire);
            if (phase == VideoPauseWorkerPhase::Stopping)
            {
                return VideoPauseFrameDisposition::Stop;
            }
            if (phase == VideoPauseWorkerPhase::PauseRequested ||
                phase == VideoPauseWorkerPhase::Paused ||
                phase == VideoPauseWorkerPhase::ResumeRequested)
            {
                pausedFramesDiscarded_.fetch_add(
                    1, std::memory_order_relaxed);
                return VideoPauseFrameDisposition::DiscardPaused;
            }
            if (phase == VideoPauseWorkerPhase::Resuming &&
                frameSequence <= lastResumeCutoffSequence_.load(
                    std::memory_order_acquire))
            {
                staleResumeFramesDiscarded_.fetch_add(
                    1, std::memory_order_relaxed);
                return VideoPauseFrameDisposition::DiscardStaleResume;
            }
            return VideoPauseFrameDisposition::Process;
        }

        [[nodiscard]] bool FrameCommitted(
            const std::uint64_t frameSequence) noexcept
        {
            lastCommittedFrameSequence_.store(
                frameSequence, std::memory_order_release);
            committedVideoSamples_.fetch_add(1, std::memory_order_relaxed);
            auto expected = VideoPauseWorkerPhase::Resuming;
            if (!phase_.compare_exchange_strong(
                    expected,
                    VideoPauseWorkerPhase::Running,
                    std::memory_order_acq_rel))
            {
                return false;
            }
            firstResumedFrameSequence_.store(
                frameSequence, std::memory_order_release);
            videoResumeAcks_.fetch_add(1, std::memory_order_relaxed);
            return true;
        }

        [[nodiscard]] bool CancelForStop() noexcept
        {
            const auto previous = phase_.exchange(
                VideoPauseWorkerPhase::Stopping,
                std::memory_order_acq_rel);
            if (previous == VideoPauseWorkerPhase::Stopping)
            {
                return false;
            }
            terminalStopTransitions_.fetch_add(1, std::memory_order_relaxed);
            return true;
        }

        [[nodiscard]] VideoPauseWorkerPhase Phase() const noexcept
        {
            return phase_.load(std::memory_order_acquire);
        }

        [[nodiscard]] VideoPauseWorkerSnapshot Snapshot() const noexcept
        {
            VideoPauseWorkerSnapshot value{};
            value.phase = phase_.load(std::memory_order_acquire);
            value.pauseRequests = pauseRequests_.load();
            value.videoPauseAcks = videoPauseAcks_.load();
            value.resumeRequests = resumeRequests_.load();
            value.videoResumeAcks = videoResumeAcks_.load();
            value.pausedFramesDiscarded = pausedFramesDiscarded_.load();
            value.staleResumeFramesDiscarded =
                staleResumeFramesDiscarded_.load();
            value.committedVideoSamples = committedVideoSamples_.load();
            value.lastCommittedFrameSequence =
                lastCommittedFrameSequence_.load();
            value.lastPauseCutoffSequence =
                lastPauseCutoffSequence_.load();
            value.lastResumeCutoffSequence =
                lastResumeCutoffSequence_.load();
            value.firstResumedFrameSequence =
                firstResumedFrameSequence_.load();
            value.terminalStopTransitions = terminalStopTransitions_.load();
            return value;
        }

    private:
        std::atomic<VideoPauseWorkerPhase> phase_{
            VideoPauseWorkerPhase::Running };
        std::atomic<std::uint64_t> pauseRequests_{};
        std::atomic<std::uint64_t> videoPauseAcks_{};
        std::atomic<std::uint64_t> resumeRequests_{};
        std::atomic<std::uint64_t> videoResumeAcks_{};
        std::atomic<std::uint64_t> pausedFramesDiscarded_{};
        std::atomic<std::uint64_t> staleResumeFramesDiscarded_{};
        std::atomic<std::uint64_t> committedVideoSamples_{};
        std::atomic<std::uint64_t> lastCommittedFrameSequence_{};
        std::atomic<std::uint64_t> lastPauseCutoffSequence_{};
        std::atomic<std::uint64_t> lastResumeCutoffSequence_{};
        std::atomic<std::uint64_t> firstResumedFrameSequence_{};
        std::atomic<std::uint64_t> terminalStopTransitions_{};
    };

    class VideoEncoderConsumer final
    {
    public:
        VideoEncoderConsumer();
        ~VideoEncoderConsumer();
        VideoEncoderConsumer(const VideoEncoderConsumer&) = delete;
        VideoEncoderConsumer& operator=(const VideoEncoderConsumer&) = delete;

        XbPreviewResult Start(
            RenderFrameTap& tap,
            ID3D11Device* device,
            ID3D11DeviceContext* immediateContext,
            const VideoEncoderConfiguration& configuration,
            const VideoDeviceSetupStatus& deviceStatus);
        XbPreviewResult StopAndJoin(
            RecordingTerminationDisposition disposition =
                RecordingTerminationDisposition::Publish) noexcept;
        XbPreviewResult RequestVideoPause() noexcept;
        XbPreviewResult RequestVideoResume() noexcept;
        [[nodiscard]] VideoPauseWorkerSnapshot GetVideoPauseSnapshot()
            const noexcept;
        void GetSnapshot(XbRecordingSnapshot& snapshot) const;
        XbPreviewResult SetAudioControls(
            const XbAudioControlsV1& controls) noexcept;
        void GetAudioControlSnapshot(
            XbAudioControlSnapshotV1& snapshot) const noexcept;
        void RecordExternalFailure(
            XbPreviewResult result,
            HRESULT hresult,
            const wchar_t* message);
        [[nodiscard]] bool Running() const noexcept;

    private:
        struct Impl;
        std::unique_ptr<Impl> impl_;
    };
}
