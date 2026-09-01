#pragma once

#include <d2d1_1.h>
#include <d3d11.h>
#include <winrt/base.h>

#include <cstdint>

namespace xbpreview
{
    // Thin interop adapter over Microsoft's built-in Direct2D Scale effect.
    // It owns no sampling code and uses the renderer's existing D3D11 device.
    class Direct2DPreviewScaler final
    {
    public:
        Direct2DPreviewScaler() = default;
        Direct2DPreviewScaler(const Direct2DPreviewScaler&) = delete;
        Direct2DPreviewScaler& operator=(
            const Direct2DPreviewScaler&) = delete;

        HRESULT Initialize(ID3D11Device* device) noexcept;
        HRESULT CreateTargetBitmap(
            ID3D11Texture2D* targetTexture,
            ID2D1Bitmap1** targetBitmap) noexcept;
        HRESULT Render(
            ID3D11Texture2D* sourceTexture,
            ID2D1Bitmap1* targetBitmap,
            std::uint32_t targetWidth,
            std::uint32_t targetHeight) noexcept;
        void Shutdown() noexcept;

    private:
        HRESULT EnsureSourceBitmap(
            ID3D11Texture2D* sourceTexture) noexcept;

        winrt::com_ptr<ID2D1Factory1> factory_;
        winrt::com_ptr<ID2D1Device> device_;
        winrt::com_ptr<ID2D1DeviceContext> context_;
        winrt::com_ptr<ID2D1Effect> scaleEffect_;
        winrt::com_ptr<ID2D1Bitmap1> sourceBitmap_;
        ID3D11Texture2D* sourceIdentity_{};
        D3D11_TEXTURE2D_DESC sourceDescription_{};
    };
}
