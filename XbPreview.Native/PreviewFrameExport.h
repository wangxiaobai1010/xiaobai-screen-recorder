#pragma once

#include "Direct2DPreviewScaler.h"
#include "XbPreviewApi.h"

#include <d3d11.h>
#include <dxgi.h>
#include <winrt/base.h>

#include <atomic>
#include <cstdint>
#include <mutex>
#include <optional>
#include <vector>

namespace xbpreview
{
    // PreviewRenderer owns every exported resource. UI consumers only borrow
    // the published legacy shared handles while the renderer remains alive.
    class PreviewFrameExport final
    {
    public:
        PreviewFrameExport() = default;
        PreviewFrameExport(const PreviewFrameExport&) = delete;
        PreviewFrameExport& operator=(const PreviewFrameExport&) = delete;

        HRESULT Initialize(ID3D11Device* device) noexcept;
        bool SetTargetSize(
            std::uint32_t width,
            std::uint32_t height) noexcept;
        bool Publish(
            ID3D11Device* device,
            ID3D11DeviceContext* context,
            ID3D11Texture2D* completedOutputCanvas) noexcept;

        bool GetSnapshot(XbPreviewGpuExportFrameV1& snapshot) const noexcept;
        [[nodiscard]] HRESULT LastResult() const noexcept
        {
            return lastResult_.load(std::memory_order_acquire);
        }
        void Shutdown() noexcept;

    private:
        struct Slot
        {
            winrt::com_ptr<ID3D11Texture2D> texture;
            winrt::com_ptr<IDXGIKeyedMutex> keyedMutex;
            winrt::com_ptr<ID2D1Bitmap1> d2dTarget;
            HANDLE sharedHandle{};
        };

        HRESULT RecreatePool(
            ID3D11Device* device,
            std::uint32_t targetWidth,
            std::uint32_t targetHeight);
        void MarkSkipped() noexcept;

        Direct2DPreviewScaler scaler_;
        std::atomic<std::uint64_t> targetSize_{};
        std::atomic<HRESULT> lastResult_{ S_FALSE };
        mutable std::mutex snapshotMutex_;
        XbPreviewGpuExportFrameV1 snapshot_{};
        std::vector<Slot> slots_;
        std::vector<std::vector<Slot>> retiredPools_;
        D3D11_TEXTURE2D_DESC activeDescription_{};
        std::uint64_t resourceGeneration_{};
        std::uint64_t frameGeneration_{};
        std::uint64_t rendererGeneration_{};
        std::uint64_t skippedFrameCount_{};
        std::optional<std::uint32_t> outstandingSlot_;
        LUID adapterLuid_{};
    };
}
