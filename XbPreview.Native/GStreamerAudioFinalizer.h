#pragma once

#include "GStreamerAudioMode.h"

#include <windows.h>

#include <chrono>
#include <cstdint>
#include <filesystem>
#include <string>
#include <vector>

namespace xbpreview
{
    struct GStreamerAudioValidationFacts
    {
        std::uint32_t sampleRate{};
        std::uint32_t channels{};
        std::uint64_t decodedFrames{};
        std::uint64_t decodedVideoSamples{};
        std::uint32_t peakAbsolutePcm16{};
        double rmsPcm16{};
        double dcPcm16{};
        std::uint64_t saturatedSamples{};
        std::int64_t audioDuration100ns{};
        bool nativeVideoH264{};
        bool nativeAudioAac{};
        bool audioReachedEndOfStream{};
        bool videoDecoded{};
        double integratedLufs{};
        double truePeakDbtp{};
        bool finalLoudnessValidated{};
    };

    struct GStreamerAudioLoudnessMeasurement
    {
        double integratedLufs{};
        double truePeakDbtp{};
        double loudnessRange{};
        double threshold{};
        double targetOffset{};
        bool valid{};
    };

    struct GStreamerAudioFinalizeRequest
    {
        GStreamerAudioMode mode{ GStreamerAudioMode::None };
        std::filesystem::path videoPath;
        std::filesystem::path systemFlacPath;
        std::filesystem::path microphoneFlacPath;
        std::filesystem::path outputPath;
        std::int64_t expectedDuration100ns{};
        std::chrono::milliseconds timeout{ std::chrono::minutes(5) };
    };

    struct GStreamerAudioFinalizeResult
    {
        HRESULT hresult{ E_PENDING };
        HRESULT validationHResult{ E_PENDING };
        DWORD exitCode{ STILL_ACTIVE };
        std::string stderrText;
        GStreamerAudioValidationFacts validation{};
        bool processStarted{};
        bool timedOut{};
        bool processTreeTerminated{};
        bool outputCreated{};
        bool validated{};
        bool microphoneMasteringApplied{};
        bool dualMixApplied{};
    };

    [[nodiscard]] std::filesystem::path
        ResolveGStreamerAudioFfmpegPath() noexcept;
    [[nodiscard]] bool GStreamerAudioFinalizeStorageSufficient(
        std::uint64_t freeBytes,
        std::uint64_t videoBytes) noexcept;
    [[nodiscard]] std::vector<std::wstring>
        BuildGStreamerAudioFfmpegArguments(
            const GStreamerAudioFinalizeRequest& request,
            const GStreamerAudioLoudnessMeasurement& microphoneMeasurement = {},
            const GStreamerAudioLoudnessMeasurement& programMeasurement = {});
    [[nodiscard]] GStreamerAudioFinalizeResult FinalizeGStreamerAudio(
        const GStreamerAudioFinalizeRequest& request) noexcept;
    [[nodiscard]] HRESULT ValidateGStreamerAudioMp4(
        const std::filesystem::path& path,
        std::int64_t expectedDuration100ns,
        GStreamerAudioValidationFacts& facts) noexcept;
}
