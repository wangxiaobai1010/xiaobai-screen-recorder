#include "PreviewFrameExport.h"

#include <utility>

namespace
{
    constexpr std::uint32_t ExportSlotCount = 3;
}

namespace xbpreview
{
    HRESULT PreviewFrameExport::Initialize(
        ID3D11Device* const device) noexcept
    {
        return scaler_.Initialize(device);
    }

    bool PreviewFrameExport::SetTargetSize(
        const std::uint32_t width,
        const std::uint32_t height) noexcept
    {
        if (width == 0 || height == 0 || width > 32768 || height > 32768)
        {
            return false;
        }
        targetSize_.store(
            static_cast<std::uint64_t>(width) |
                (static_cast<std::uint64_t>(height) << 32),
            std::memory_order_release);
        return true;
    }

    bool PreviewFrameExport::Publish(
        ID3D11Device* const device,
        ID3D11DeviceContext* const context,
        ID3D11Texture2D* const completedOutputCanvas) noexcept
    {
        if (device == nullptr || context == nullptr ||
            completedOutputCanvas == nullptr)
        {
            lastResult_.store(E_INVALIDARG, std::memory_order_release);
            return false;
        }

        try
        {
            D3D11_TEXTURE2D_DESC sourceDescription{};
            completedOutputCanvas->GetDesc(&sourceDescription);
            if (sourceDescription.Width == 0 || sourceDescription.Height == 0 ||
                sourceDescription.Format != DXGI_FORMAT_B8G8R8A8_UNORM ||
                sourceDescription.SampleDesc.Count != 1)
            {
                lastResult_.store(E_INVALIDARG, std::memory_order_release);
                return false;
            }

            const auto targetSize = targetSize_.load(std::memory_order_acquire);
            const auto targetWidth = static_cast<std::uint32_t>(targetSize);
            const auto targetHeight = static_cast<std::uint32_t>(targetSize >> 32);
            if (targetWidth == 0 || targetHeight == 0)
            {
                lastResult_.store(S_FALSE, std::memory_order_release);
                MarkSkipped();
                return false;
            }

            ++rendererGeneration_;
            if (slots_.empty() ||
                activeDescription_.Width != targetWidth ||
                activeDescription_.Height != targetHeight ||
                activeDescription_.Format != sourceDescription.Format)
            {
                const auto recreateResult =
                    RecreatePool(device, targetWidth, targetHeight);
                if (FAILED(recreateResult))
                {
                    lastResult_.store(
                        recreateResult, std::memory_order_release);
                    MarkSkipped();
                    return false;
                }
            }

            // The compositor may have one image in flight. Reuse that slot
            // only after key 0 returns, so renderer/encoder never wait and UI
            // never builds a queue of stale frames.
            const auto firstSlot = outstandingSlot_.value_or(0);
            const auto slotsToTry = outstandingSlot_.has_value()
                ? 1u
                : static_cast<std::uint32_t>(slots_.size());
            for (std::uint32_t attempt = 0; attempt < slotsToTry; ++attempt)
            {
                const auto slotIndex = static_cast<std::uint32_t>(
                    (firstSlot + attempt) % slots_.size());
                auto& slot = slots_[slotIndex];

                // Route B's native writer is zero-wait. A busy UI consumer
                // drops this UI export without delaying render or encode.
                if (slot.keyedMutex->AcquireSync(0, 0) != S_OK)
                {
                    continue;
                }

                // Submit the completed D3D11 OutputCanvas before Direct2D
                // consumes the same-device DXGI surface. No CPU wait/readback
                // is introduced.
                context->Flush();
                const auto scaleResult = scaler_.Render(
                    completedOutputCanvas,
                    slot.d2dTarget.get(),
                    targetWidth,
                    targetHeight);
                if (FAILED(scaleResult))
                {
                    (void)slot.keyedMutex->ReleaseSync(0);
                    lastResult_.store(scaleResult, std::memory_order_release);
                    MarkSkipped();
                    return false;
                }
                const auto releaseResult = slot.keyedMutex->ReleaseSync(1);
                if (FAILED(releaseResult))
                {
                    lastResult_.store(releaseResult, std::memory_order_release);
                    MarkSkipped();
                    return false;
                }
                ++frameGeneration_;
                outstandingSlot_ = slotIndex;

                XbPreviewGpuExportFrameV1 published{};
                published.structSize = sizeof(published);
                published.version = XB_GPU_EXPORT_ABI_VERSION_V1;
                published.sharedHandle = static_cast<std::uint64_t>(
                    reinterpret_cast<std::uintptr_t>(slot.sharedHandle));
                published.width = targetWidth;
                published.height = targetHeight;
                published.format = static_cast<std::uint32_t>(
                    sourceDescription.Format);
                published.slotIndex = slotIndex;
                published.resourceGeneration = resourceGeneration_;
                published.frameGeneration = frameGeneration_;
                published.skippedFrameCount = skippedFrameCount_;
                published.adapterLuidLow = adapterLuid_.LowPart;
                published.adapterLuidHigh = adapterLuid_.HighPart;
                published.rendererGeneration = rendererGeneration_;
                {
                    std::lock_guard lock(snapshotMutex_);
                    snapshot_ = published;
                }
                lastResult_.store(S_OK, std::memory_order_release);
                return true;
            }
            lastResult_.store(
                DXGI_ERROR_WAS_STILL_DRAWING,
                std::memory_order_release);
        }
        catch (...)
        {
            // UI presentation is a best-effort consumer of OutputCanvas.
            lastResult_.store(E_FAIL, std::memory_order_release);
        }

        MarkSkipped();
        return false;
    }

    void PreviewFrameExport::MarkSkipped() noexcept
    {
        ++skippedFrameCount_;
        std::lock_guard lock(snapshotMutex_);
        if (snapshot_.frameGeneration != 0)
        {
            snapshot_.skippedFrameCount = skippedFrameCount_;
            snapshot_.rendererGeneration = rendererGeneration_;
        }
    }

    bool PreviewFrameExport::GetSnapshot(
        XbPreviewGpuExportFrameV1& snapshot) const noexcept
    {
        std::lock_guard lock(snapshotMutex_);
        if (snapshot_.frameGeneration == 0 || snapshot_.sharedHandle == 0)
        {
            return false;
        }
        snapshot = snapshot_;
        return true;
    }

    void PreviewFrameExport::Shutdown() noexcept
    {
        {
            std::lock_guard lock(snapshotMutex_);
            snapshot_ = {};
        }
        retiredPools_.clear();
        slots_.clear();
        activeDescription_ = {};
        resourceGeneration_ = 0;
        frameGeneration_ = 0;
        rendererGeneration_ = 0;
        skippedFrameCount_ = 0;
        outstandingSlot_.reset();
        adapterLuid_ = {};
        lastResult_.store(S_FALSE, std::memory_order_release);
        scaler_.Shutdown();
    }

    HRESULT PreviewFrameExport::RecreatePool(
        ID3D11Device* const device,
        const std::uint32_t targetWidth,
        const std::uint32_t targetHeight)
    {
        std::vector<Slot> replacement;
        replacement.reserve(ExportSlotCount);

        D3D11_TEXTURE2D_DESC exportDescription{};
        exportDescription.Width = targetWidth;
        exportDescription.Height = targetHeight;
        exportDescription.MipLevels = 1;
        exportDescription.ArraySize = 1;
        exportDescription.Format = DXGI_FORMAT_B8G8R8A8_UNORM;
        exportDescription.SampleDesc = { 1, 0 };
        exportDescription.Usage = D3D11_USAGE_DEFAULT;
        exportDescription.BindFlags =
            D3D11_BIND_RENDER_TARGET | D3D11_BIND_SHADER_RESOURCE;
        exportDescription.CPUAccessFlags = 0;
        exportDescription.MiscFlags =
            D3D11_RESOURCE_MISC_SHARED_KEYEDMUTEX;

        for (std::uint32_t index = 0; index < ExportSlotCount; ++index)
        {
            Slot slot{};
            auto result = device->CreateTexture2D(
                &exportDescription, nullptr, slot.texture.put());
            if (FAILED(result))
            {
                return result;
            }
            result = scaler_.CreateTargetBitmap(
                slot.texture.get(), slot.d2dTarget.put());
            if (FAILED(result))
            {
                return result;
            }
            slot.keyedMutex = slot.texture.as<IDXGIKeyedMutex>();
            auto resource = slot.texture.as<IDXGIResource>();
            result = resource->GetSharedHandle(&slot.sharedHandle);
            if (FAILED(result) || slot.sharedHandle == nullptr)
            {
                return FAILED(result) ? result : E_HANDLE;
            }
            replacement.push_back(std::move(slot));
        }

        winrt::com_ptr<IDXGIDevice> dxgiDevice;
        winrt::check_hresult(device->QueryInterface(
            __uuidof(IDXGIDevice), dxgiDevice.put_void()));
        winrt::com_ptr<IDXGIAdapter> adapter;
        winrt::check_hresult(dxgiDevice->GetAdapter(adapter.put()));
        DXGI_ADAPTER_DESC adapterDescription{};
        winrt::check_hresult(adapter->GetDesc(&adapterDescription));
        adapterLuid_ = adapterDescription.AdapterLuid;

        if (!slots_.empty())
        {
            // GetSharedHandle returns a non-owning legacy handle. Keep prior
            // pools alive until renderer shutdown so imported Avalonia images
            // cannot outlive their renderer-owned textures.
            retiredPools_.push_back(std::move(slots_));
        }
        slots_ = std::move(replacement);
        activeDescription_ = exportDescription;
        ++resourceGeneration_;
        outstandingSlot_.reset();
        return S_OK;
    }
}
