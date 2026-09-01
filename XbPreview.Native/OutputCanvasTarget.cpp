#include "OutputCanvasTarget.h"

namespace xbpreview
{
    bool OutputCanvasTarget::Ensure(
        ID3D11Device* const device,
        const OutputCanvasDescription& description)
    {
        if (device == nullptr || !IsValidOutputCanvas(description))
        {
            throw winrt::hresult_invalid_argument();
        }
        if (texture_ && SameOutputCanvas(description_, description))
        {
            return false;
        }

        D3D11_TEXTURE2D_DESC textureDescription{};
        textureDescription.Width = description.width;
        textureDescription.Height = description.height;
        textureDescription.MipLevels = 1;
        textureDescription.ArraySize = 1;
        textureDescription.Format = description.format;
        textureDescription.SampleDesc.Count = 1;
        textureDescription.Usage = D3D11_USAGE_DEFAULT;
        textureDescription.BindFlags =
            D3D11_BIND_RENDER_TARGET | D3D11_BIND_SHADER_RESOURCE;

        winrt::com_ptr<ID3D11Texture2D> texture;
        winrt::check_hresult(device->CreateTexture2D(
            &textureDescription,
            nullptr,
            texture.put()));

        winrt::com_ptr<ID3D11RenderTargetView> renderTargetView;
        winrt::check_hresult(device->CreateRenderTargetView(
            texture.get(),
            nullptr,
            renderTargetView.put()));

        winrt::com_ptr<ID3D11ShaderResourceView> shaderResourceView;
        winrt::check_hresult(device->CreateShaderResourceView(
            texture.get(),
            nullptr,
            shaderResourceView.put()));

        texture_ = std::move(texture);
        renderTargetView_ = std::move(renderTargetView);
        shaderResourceView_ = std::move(shaderResourceView);
        description_ = description;
        ++generation_;
        return true;
    }

    void OutputCanvasTarget::Shutdown() noexcept
    {
        shaderResourceView_ = nullptr;
        renderTargetView_ = nullptr;
        texture_ = nullptr;
        description_ = {};
        generation_ = 0;
    }
}
