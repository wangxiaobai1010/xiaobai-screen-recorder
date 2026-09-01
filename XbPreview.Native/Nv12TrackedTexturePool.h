#pragma once

#include "VideoEncoderConfig.h"

#include <d3d11.h>
#include <mferror.h>
#include <mfidl.h>
#include <winrt/base.h>

#include <chrono>
#include <cstddef>
#include <cstdint>
#include <memory>
#include <optional>

namespace xbpreview
{
    struct Nv12PoolSharedState;

    struct Nv12PoolDiagnostics
    {
        std::uint32_t highWatermark{};
        std::uint32_t outstanding{};
        std::uint64_t callbackCount{};
        std::uint64_t callbackAfterStop{};
        std::uint64_t doubleReturn{};
        std::uint64_t invalidStateTransition{};
        std::uint64_t starvation{};
    };

    class Nv12TrackedTexturePool final
    {
    public:
        Nv12TrackedTexturePool() = default;
        ~Nv12TrackedTexturePool();
        Nv12TrackedTexturePool(const Nv12TrackedTexturePool&) = delete;
        Nv12TrackedTexturePool& operator=(const Nv12TrackedTexturePool&) = delete;

        void Initialize(
            ID3D11Device* device,
            ID3D11VideoDevice* videoDevice,
            ID3D11VideoProcessorEnumerator* enumerator,
            std::uint32_t width,
            std::uint32_t height);
        [[nodiscard]] std::optional<std::size_t> TryAcquire() noexcept;
        [[nodiscard]] ID3D11Texture2D* Texture(std::size_t index) const noexcept;
        [[nodiscard]] ID3D11VideoProcessorOutputView* OutputView(
            std::size_t index) const noexcept;
        [[nodiscard]] HRESULT CreateTrackedSample(
            std::size_t index,
            std::int64_t sampleTime100ns,
            std::int64_t sampleDuration100ns,
            IMFSample** sample) noexcept;
        void CancelProducing(std::size_t index) noexcept;
        void MarkStopping() noexcept;
        [[nodiscard]] bool WaitForAllReturned(
            std::chrono::milliseconds timeout) noexcept;
        [[nodiscard]] Nv12PoolDiagnostics Diagnostics() const noexcept;
        void Shutdown() noexcept;

    private:
        std::shared_ptr<Nv12PoolSharedState> state_;
    };
}
