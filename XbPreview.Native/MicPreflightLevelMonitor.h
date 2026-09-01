#pragma once

#include <windows.h>

#include <cstdint>
#include <memory>
#include <string>

namespace xbpreview
{
    class GStreamerMicrophoneDeviceBinding;

    enum class MicPreflightPipelineState
    {
        Idle,
        Starting,
        Playing,
        Stopped,
        Failed,
    };

    struct MicPreflightLevelSnapshot final
    {
        bool enabled{};
        bool running{};
        bool available{};
        bool sourceCreatedFromDevice{};
        bool elementIdentityMatches{};
        bool resourcesReleased{ true };
        MicPreflightPipelineState pipelineState{
            MicPreflightPipelineState::Idle };
        std::wstring selectedEndpointId;
        std::wstring elementEndpointId;
        std::wstring lastGStreamerError;
        HRESULT terminalHResult{ S_OK };
        std::uint32_t peakAbsolutePcm16{};
        double rmsPcm16{};
        double peakDb{-120.0};
        double rmsDb{-120.0};
        std::uint64_t levelMessageCount{};
        std::uint64_t startRequestCount{};
        std::uint64_t completedReleaseCount{};
    };

    [[nodiscard]] const char* MicPreflightPipelineDescription() noexcept;

    // Owns one idle-only microphone capture. The caller supplies the exact
    // product-selected GstDevice binding; this class performs no enumeration,
    // default-device resolution, fallback, encoding, or audio DSP.
    class MicPreflightLevelMonitor final
    {
    public:
        MicPreflightLevelMonitor();
        ~MicPreflightLevelMonitor();

        MicPreflightLevelMonitor(const MicPreflightLevelMonitor&) = delete;
        MicPreflightLevelMonitor& operator=(
            const MicPreflightLevelMonitor&) = delete;

        [[nodiscard]] HRESULT Start(
            std::shared_ptr<GStreamerMicrophoneDeviceBinding> device,
            const std::wstring& requestedEndpointId = {}) noexcept;
        void Stop() noexcept;
        [[nodiscard]] MicPreflightLevelSnapshot Snapshot() const noexcept;

    private:
        struct Impl;
        std::unique_ptr<Impl> impl_;
    };
}
