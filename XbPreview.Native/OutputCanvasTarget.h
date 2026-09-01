#pragma once

#include <d3d11.h>
#include <winrt/base.h>

#include <cstdint>

namespace xbpreview
{
    struct OutputCanvasDescription
    {
        std::uint32_t width{};
        std::uint32_t height{};
        DXGI_FORMAT format{ DXGI_FORMAT_B8G8R8A8_UNORM };
    };

    [[nodiscard]] inline bool IsValidOutputCanvas(
        const OutputCanvasDescription& value) noexcept
    {
        return value.width > 0 &&
            value.height > 0 &&
            value.format == DXGI_FORMAT_B8G8R8A8_UNORM;
    }

    [[nodiscard]] inline bool SameOutputCanvas(
        const OutputCanvasDescription& left,
        const OutputCanvasDescription& right) noexcept
    {
        return left.width == right.width &&
            left.height == right.height &&
            left.format == right.format;
    }

    class OutputCanvasTarget final
    {
    public:
        OutputCanvasTarget() = default;
        OutputCanvasTarget(const OutputCanvasTarget&) = delete;
        OutputCanvasTarget& operator=(const OutputCanvasTarget&) = delete;

        bool Ensure(
            ID3D11Device* device,
            const OutputCanvasDescription& description);

        void Shutdown() noexcept;

        [[nodiscard]] ID3D11Texture2D* Texture() const noexcept
        {
            return texture_.get();
        }

        [[nodiscard]] ID3D11RenderTargetView* RenderTargetView() const noexcept
        {
            return renderTargetView_.get();
        }

        [[nodiscard]] ID3D11ShaderResourceView* ShaderResourceView() const noexcept
        {
            return shaderResourceView_.get();
        }

        [[nodiscard]] const OutputCanvasDescription& Description() const noexcept
        {
            return description_;
        }

        [[nodiscard]] std::uint64_t Generation() const noexcept
        {
            return generation_;
        }

    private:
        OutputCanvasDescription description_{};
        std::uint64_t generation_{};
        winrt::com_ptr<ID3D11Texture2D> texture_;
        winrt::com_ptr<ID3D11RenderTargetView> renderTargetView_;
        winrt::com_ptr<ID3D11ShaderResourceView> shaderResourceView_;
    };
}
