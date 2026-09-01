#pragma once

#include "CursorCaptureState.h"

#include <d3d11.h>
#include <winrt/base.h>

#include <cstdint>

namespace xbpreview
{
    class CustomCursorRenderer final
    {
    public:
        CustomCursorRenderer() = default;
        CustomCursorRenderer(const CustomCursorRenderer&) = delete;
        CustomCursorRenderer& operator=(const CustomCursorRenderer&) = delete;

        void Initialize(ID3D11Device* device, ID3D11DeviceContext* context);

        [[nodiscard]] CursorRenderResult Draw(
            const CursorDrawCommand& command,
            const D3D11_VIEWPORT& viewport) noexcept;

        void Shutdown() noexcept;

        [[nodiscard]] bool IsInitialized() const noexcept
        {
            return initialized_;
        }

    private:
        HRESULT EnsureTexture(const CursorShape& shape, bool& uploaded) noexcept;

        bool initialized_{};
        std::uint64_t textureShapeId_{};
        std::uint64_t textureGeneration_{};
        winrt::com_ptr<ID3D11Device> device_;
        winrt::com_ptr<ID3D11DeviceContext> context_;
        winrt::com_ptr<ID3D11VertexShader> vertexShader_;
        winrt::com_ptr<ID3D11PixelShader> pixelShader_;
        winrt::com_ptr<ID3D11Buffer> rectConstantBuffer_;
        winrt::com_ptr<ID3D11SamplerState> sampler_;
        winrt::com_ptr<ID3D11BlendState> blendState_;
        winrt::com_ptr<ID3D11RasterizerState> rasterizer_;
        winrt::com_ptr<ID3D11Texture2D> shapeTexture_;
        winrt::com_ptr<ID3D11ShaderResourceView> shapeView_;
    };
}
