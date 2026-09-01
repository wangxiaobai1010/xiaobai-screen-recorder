#pragma once

#include <windows.h>

#include <mfidl.h>
#include <mfreadwrite.h>

#include <cstdint>
#include <string>
#include <vector>

namespace xbpreview
{
    inline constexpr std::uint32_t MfAacAudioSamplesPerSecond = 48'000;
    inline constexpr std::uint32_t MfAacAudioChannelCount = 2;
    inline constexpr std::uint32_t MfAacAudioBitsPerSample = 16;
    inline constexpr std::uint32_t MfAacAudioAverageBytesPerSecond = 12'000;

    class MfAacAudioStream final
    {
    public:
        [[nodiscard]] static HRESULT Configure(
            IMFSinkWriter* writer,
            DWORD& streamIndex) noexcept;
        [[nodiscard]] HRESULT WritePcm(
            IMFSinkWriter* writer,
            DWORD streamIndex,
            const std::vector<BYTE>& bytes,
            std::int64_t sampleTime100ns,
            std::int64_t sampleDuration100ns) noexcept;
        [[nodiscard]] static HRESULT ValidateOutput(
            const std::wstring& outputPath,
            std::uint32_t sampleLimit,
            bool requireEndOfStream) noexcept;
        void Reset() noexcept;

    private:
        [[nodiscard]] static HRESULT WriteSample(
            IMFSinkWriter* writer,
            DWORD streamIndex,
            const BYTE* bytes,
            DWORD byteCount,
            std::int64_t sampleTime100ns,
            std::int64_t sampleDuration100ns) noexcept;

        bool lastFrameHadAudio_{};
    };
}
