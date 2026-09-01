#pragma once

#include <windows.h>

#include <cstdint>
#include <functional>
#include <memory>
#include <string>

namespace xbpreview
{
    struct AudioEndpointLevelAssignment final
    {
        std::wstring microphoneEndpointId;
        std::wstring systemEndpointId;
        bool microphoneEnabled{};
        bool systemEnabled{};
    };

    struct AudioEndpointLevelSnapshot final
    {
        std::uint32_t microphonePeakAbsolutePcm16{};
        std::uint32_t systemPeakAbsolutePcm16{};
        bool microphoneAvailable{};
        bool systemAvailable{};
        bool microphoneEnabled{};
        bool systemEnabled{};
    };

    [[nodiscard]] std::uint32_t
        NormalizedEndpointPeakToAbsolutePcm16(float peak) noexcept;

    // A single, read-only Core Audio endpoint observer. The provider supplies
    // endpoint identities already selected by product ownership; this class
    // deliberately performs no default-device selection or endpoint listing.
    class AudioEndpointLevelMonitor final
    {
    public:
        using AssignmentProvider =
            std::function<AudioEndpointLevelAssignment()>;

        explicit AudioEndpointLevelMonitor(AssignmentProvider provider);
        ~AudioEndpointLevelMonitor();

        AudioEndpointLevelMonitor(const AudioEndpointLevelMonitor&) = delete;
        AudioEndpointLevelMonitor& operator=(
            const AudioEndpointLevelMonitor&) = delete;

        [[nodiscard]] HRESULT Start() noexcept;
        void Stop() noexcept;
        [[nodiscard]] AudioEndpointLevelSnapshot Snapshot() const noexcept;

    private:
        struct Impl;
        std::unique_ptr<Impl> impl_;
    };
}
