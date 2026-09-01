#pragma once

#include "GStreamerAudioMode.h"

#include <chrono>
#include <cstdint>
#include <limits>
#include <memory>
#include <string>

#if defined(XBPREVIEW_NATIVE_TESTS)
#include <windows.h>
#include "RecordingStorageSafety.h"
#endif

namespace xbpreview
{
    class GStreamerMicrophoneDeviceBinding;

    enum class AudioProgramMode
    {
        None,
        SystemOnly,
        MicrophoneOnly,
        Dual,
    };

#if defined(XBPREVIEW_NATIVE_TESTS)
    // Compiled out of the product DLL. These events only let the native test
    // process observe already-existing lifecycle boundaries.
    struct VideoEncoderLifecycleTestHooks final
    {
        HANDLE stoppingReached{};
        HANDLE continueAfterStopping{};
        HANDLE beforeFinalizeReached{};
        HANDLE continueBeforeFinalize{};
        HANDLE readyToPublishReached{};
        HANDLE continueAfterReadyToPublish{};
        HANDLE publishMoveReached{};
        HANDLE continueAfterPublishMove{};
        bool forceLifetimeOwnerAcquireFailure{};
        bool overrideStartStorageFacts{};
        RecordingStorageStatus startStorageStatus{
            RecordingStorageStatus::Ready };
        HRESULT startStorageHResult{ S_OK };
        std::uint64_t startFreeBytes{};
        bool overrideRuntimeStorageFacts{};
        RecordingStorageStatus runtimeStorageStatus{
            RecordingStorageStatus::Ready };
        HRESULT runtimeStorageHResult{ S_OK };
        std::uint64_t runtimeFreeBytes{};
        bool checkRuntimeStorageEverySample{};
        bool retainGStreamerAudioProofMaterials{};
        std::uint64_t injectWriteFailureAfterSubmittedFrames{
            (std::numeric_limits<std::uint64_t>::max)() };
        HRESULT injectedWriteFailureHResult{ S_OK };
    };
#endif

    inline constexpr std::uint32_t VideoEncoderNv12PoolSize = 6;
    inline constexpr std::uint32_t VideoEncoderNominalFrameRateNumerator = 60;
    inline constexpr std::uint32_t VideoEncoderNominalFrameRateDenominator = 1;
    inline constexpr std::int64_t VideoEncoderNominalFrameDuration100ns = 166667;
    inline constexpr std::uint32_t VideoEncoderDefaultFrameRate = 30;

    [[nodiscard]] constexpr bool IsSupportedVideoEncoderFrameRate(
        const std::uint32_t framesPerSecond) noexcept
    {
        return framesPerSecond == 30 || framesPerSecond == 60;
    }

    [[nodiscard]] constexpr std::int64_t VideoEncoderCfrTime100ns(
        const std::uint64_t frameIndex,
        const std::uint32_t framesPerSecond) noexcept
    {
        return IsSupportedVideoEncoderFrameRate(framesPerSecond)
            ? static_cast<std::int64_t>(
                (frameIndex / framesPerSecond) * 10'000'000ull +
                ((frameIndex % framesPerSecond) * 10'000'000ull) /
                    framesPerSecond)
            : 0;
    }

    [[nodiscard]] constexpr std::int64_t VideoEncoderCfrDuration100ns(
        const std::uint64_t frameIndex,
        const std::uint32_t framesPerSecond) noexcept
    {
        return VideoEncoderCfrTime100ns(frameIndex + 1, framesPerSecond) -
            VideoEncoderCfrTime100ns(frameIndex, framesPerSecond);
    }

    [[nodiscard]] constexpr std::uint64_t
        VideoEncoderCfrMissedDeadlineCount(
            const std::int64_t lateness100ns,
            const std::int64_t frameDuration100ns) noexcept
    {
        return lateness100ns >= frameDuration100ns &&
            frameDuration100ns > 0
            ? static_cast<std::uint64_t>(
                lateness100ns / frameDuration100ns)
            : 0;
    }

    enum class VideoEncoderFaultInjection
    {
        None,
        UnsupportedAfterOutputFileCreated,
        WorkerExceptionAfterOutputFileCreated,
        FinalizeFailureAfterWrite,
        ValidationFailureAfterFinalize,
        PublishConflictAtTarget,
        SnapshotExceptionAfterPublish,
        WorkingIdentityCaptureFailure,
        PostPublishIdentityVerificationFailure,
        AudioInitializationFailure
    };

    struct VideoEncoderConfiguration
    {
        bool enabled{};
        bool publishOnStop{};
        std::wstring sessionId;
        std::wstring outputDirectory;
        std::wstring workingPath;
        std::wstring plannedFinalPath;
        std::wstring diagnosticDirectory;
        std::uint32_t frameRate{ VideoEncoderDefaultFrameRate };
        std::uint32_t bitrate{ 8'000'000 };
        std::chrono::milliseconds trackedReturnTimeout{ 10'000 };
        bool audioEnabled{ true };
        GStreamerAudioMode audioMode{ GStreamerAudioMode::Dual };
        std::shared_ptr<GStreamerMicrophoneDeviceBinding> microphoneDevice;
        VideoEncoderFaultInjection faultInjection{};
#if defined(XBPREVIEW_NATIVE_TESTS)
        VideoEncoderLifecycleTestHooks* lifecycleTestHooks{};
#endif
    };

    [[nodiscard]] VideoEncoderConfiguration ReadVideoEncoderConfiguration(
        const std::wstring& diagnosticDirectory,
        const std::wstring& sessionId);
    [[nodiscard]] VideoEncoderConfiguration CreateRecordingConfiguration(
        const std::wstring& diagnosticDirectory,
        const std::wstring& sessionId);
    [[nodiscard]] VideoEncoderConfiguration CreateRecordingConfiguration(
        const std::wstring& diagnosticDirectory,
        const std::wstring& sessionId,
        const std::wstring& managedOutputRoot);
    void ApplyAudioProgramMode(
        VideoEncoderConfiguration& configuration,
        AudioProgramMode mode) noexcept;
}
