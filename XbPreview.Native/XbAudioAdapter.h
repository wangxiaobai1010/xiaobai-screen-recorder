#pragma once

#include <windows.h>

#include <cstdint>
#include <memory>
#include <string>
#include <vector>

namespace xbpreview
{
    enum class XbAudioAdapterMode : std::uint32_t
    {
        None = 0,
        SystemOnly,
        MicrophoneOnly,
        Dual,
    };

    enum class XbAudioAdapterState : std::uint32_t
    {
        Idle = 0,
        Starting,
        Running,
        Stopped,
        Failed,
    };

    struct XbAudioAdapterSnapshot final
    {
        XbAudioAdapterState state{ XbAudioAdapterState::Idle };
        XbAudioAdapterMode mode{ XbAudioAdapterMode::None };
        HRESULT lastHResult{ S_OK };
        bool mediaFoundationStarted{};
        bool captureRunning{};
        bool postStopDrainAvailable{};
        std::uint32_t sampleRate{ 48'000 };
        std::uint32_t channels{ 2 };
        std::uint32_t bitsPerSample{ 16 };
        std::uint64_t pullCount{};
        std::uint64_t pcmBytesDelivered{};
    };

    // Thin ownership adapter around the vendored ScreenRecorderLib audio block.
    // The calling worker must already be initialized as a COM MTA. This class
    // deliberately owns no clock, capture, mixing, resampling, DSP, encoding,
    // fallback, or device-selection policy.
    class XbAudioAdapter final
    {
    public:
        XbAudioAdapter();
        ~XbAudioAdapter();

        XbAudioAdapter(const XbAudioAdapter&) = delete;
        XbAudioAdapter& operator=(const XbAudioAdapter&) = delete;

        [[nodiscard]] HRESULT Start(
            XbAudioAdapterMode mode,
            const std::wstring& microphoneEndpointId,
            const std::wstring& renderEndpointId) noexcept;

        [[nodiscard]] HRESULT PullMixedPcm(
            std::uint64_t duration100ns,
            std::vector<std::uint8_t>& mixedPcm) noexcept;

        [[nodiscard]] HRESULT ClearRecordedPcm() noexcept;

        [[nodiscard]] HRESULT Stop() noexcept;
        [[nodiscard]] HRESULT FinishStop() noexcept;

        [[nodiscard]] XbAudioAdapterSnapshot Snapshot() const noexcept;

    private:
        struct Impl;
        std::unique_ptr<Impl> impl_;
    };
}
