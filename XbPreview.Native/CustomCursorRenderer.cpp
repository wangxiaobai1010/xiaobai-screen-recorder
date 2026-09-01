#include "CustomCursorRenderer.h"

#include <d3dcompiler.h>

#include <array>
#include <chrono>
#include <cstring>

namespace
{
    constexpr char CursorShaderSource[] = R"(
Texture2D CursorTexture : register(t0);
SamplerState PointSampler : register(s0);
cbuffer CursorRectBuffer : register(b0)
{
    float4 CursorRect;
};

struct VertexOutput
{
    float4 position : SV_Position;
    float2 uv : TEXCOORD0;
};

VertexOutput VSMain(uint vertexId : SV_VertexID)
{
    static const float2 uv[6] =
    {
        float2(0.0f, 0.0f),
        float2(1.0f, 0.0f),
        float2(0.0f, 1.0f),
        float2(0.0f, 1.0f),
        float2(1.0f, 0.0f),
        float2(1.0f, 1.0f)
    };
    const float2 p = CursorRect.xy + uv[vertexId] * CursorRect.zw;
    VertexOutput output;
    output.position = float4(
        -1.0f + 2.0f * p.x,
         1.0f - 2.0f * p.y,
         0.0f,
         1.0f);
    output.uv = uv[vertexId];
    return output;
}

float4 PSMain(VertexOutput input) : SV_Target
{
    return CursorTexture.Sample(PointSampler, input.uv);
}
)";

    winrt::com_ptr<ID3DBlob> Compile(
        const char* const entry,
        const char* const profile)
    {
        winrt::com_ptr<ID3DBlob> byteCode;
        winrt::com_ptr<ID3DBlob> errors;
        winrt::check_hresult(D3DCompile(
            CursorShaderSource,
            std::strlen(CursorShaderSource),
            "XbPreview.P1c.Cursor",
            nullptr,
            nullptr,
            entry,
            profile,
            D3DCOMPILE_ENABLE_STRICTNESS | D3DCOMPILE_OPTIMIZATION_LEVEL3,
            0,
            byteCode.put(),
            errors.put()));
        return byteCode;
    }
}

namespace xbpreview
{
    void CustomCursorRenderer::Initialize(
        ID3D11Device* const device,
        ID3D11DeviceContext* const context)
    {
        if (device == nullptr || context == nullptr)
        {
            throw winrt::hresult_invalid_argument();
        }
        Shutdown();
        device_.copy_from(device);
        context_.copy_from(context);

        const auto vertexByteCode = Compile("VSMain", "vs_5_0");
        const auto pixelByteCode = Compile("PSMain", "ps_5_0");
        winrt::check_hresult(device_->CreateVertexShader(
            vertexByteCode->GetBufferPointer(),
            vertexByteCode->GetBufferSize(),
            nullptr,
            vertexShader_.put()));
        winrt::check_hresult(device_->CreatePixelShader(
            pixelByteCode->GetBufferPointer(),
            pixelByteCode->GetBufferSize(),
            nullptr,
            pixelShader_.put()));

        D3D11_BUFFER_DESC buffer{};
        buffer.ByteWidth = 16;
        buffer.Usage = D3D11_USAGE_DEFAULT;
        buffer.BindFlags = D3D11_BIND_CONSTANT_BUFFER;
        winrt::check_hresult(device_->CreateBuffer(
            &buffer,
            nullptr,
            rectConstantBuffer_.put()));

        D3D11_SAMPLER_DESC sampler{};
        sampler.Filter = D3D11_FILTER_MIN_MAG_MIP_POINT;
        sampler.AddressU = D3D11_TEXTURE_ADDRESS_CLAMP;
        sampler.AddressV = D3D11_TEXTURE_ADDRESS_CLAMP;
        sampler.AddressW = D3D11_TEXTURE_ADDRESS_CLAMP;
        sampler.ComparisonFunc = D3D11_COMPARISON_NEVER;
        sampler.MinLOD = 0.0f;
        sampler.MaxLOD = D3D11_FLOAT32_MAX;
        winrt::check_hresult(device_->CreateSamplerState(
            &sampler,
            sampler_.put()));

        D3D11_BLEND_DESC blend{};
        blend.RenderTarget[0].BlendEnable = TRUE;
        blend.RenderTarget[0].SrcBlend = D3D11_BLEND_ONE;
        blend.RenderTarget[0].DestBlend = D3D11_BLEND_INV_SRC_ALPHA;
        blend.RenderTarget[0].BlendOp = D3D11_BLEND_OP_ADD;
        blend.RenderTarget[0].SrcBlendAlpha = D3D11_BLEND_ONE;
        blend.RenderTarget[0].DestBlendAlpha = D3D11_BLEND_INV_SRC_ALPHA;
        blend.RenderTarget[0].BlendOpAlpha = D3D11_BLEND_OP_ADD;
        blend.RenderTarget[0].RenderTargetWriteMask =
            D3D11_COLOR_WRITE_ENABLE_ALL;
        winrt::check_hresult(device_->CreateBlendState(
            &blend,
            blendState_.put()));

        D3D11_RASTERIZER_DESC rasterizer{};
        rasterizer.FillMode = D3D11_FILL_SOLID;
        rasterizer.CullMode = D3D11_CULL_NONE;
        rasterizer.DepthClipEnable = TRUE;
        rasterizer.ScissorEnable = FALSE;
        winrt::check_hresult(device_->CreateRasterizerState(
            &rasterizer,
            rasterizer_.put()));
        initialized_ = true;
    }

    CursorRenderResult CustomCursorRenderer::Draw(
        const CursorDrawCommand& command,
        const D3D11_VIEWPORT& viewport) noexcept
    {
        CursorRenderResult result{};
        const auto started = std::chrono::steady_clock::now();
        if (!initialized_ ||
            !command.shape ||
            !command.mapped.valid ||
            !command.mapped.intersectsCamera)
        {
            result.result = E_INVALIDARG;
            return result;
        }

        result.result = EnsureTexture(*command.shape, result.textureUploaded);
        if (FAILED(result.result))
        {
            return result;
        }

        const std::array<float, 4> rect{
            static_cast<float>(command.mapped.left),
            static_cast<float>(command.mapped.top),
            static_cast<float>(command.mapped.width),
            static_cast<float>(command.mapped.height)
        };
        context_->UpdateSubresource(
            rectConstantBuffer_.get(),
            0,
            nullptr,
            rect.data(),
            0,
            0);

        ID3D11ShaderResourceView* view = shapeView_.get();
        ID3D11SamplerState* sampler = sampler_.get();
        ID3D11Buffer* buffer = rectConstantBuffer_.get();
        const std::array<float, 4> blendFactor{ 0, 0, 0, 0 };
        context_->RSSetViewports(1, &viewport);
        context_->RSSetState(rasterizer_.get());
        context_->OMSetBlendState(
            blendState_.get(),
            blendFactor.data(),
            0xffffffffu);
        context_->IASetInputLayout(nullptr);
        context_->IASetPrimitiveTopology(D3D11_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
        context_->VSSetShader(vertexShader_.get(), nullptr, 0);
        context_->PSSetShader(pixelShader_.get(), nullptr, 0);
        context_->PSSetShaderResources(0, 1, &view);
        context_->PSSetSamplers(0, 1, &sampler);
        context_->VSSetConstantBuffers(0, 1, &buffer);
        context_->Draw(6, 0);

        ID3D11ShaderResourceView* nullView = nullptr;
        context_->PSSetShaderResources(0, 1, &nullView);
        result.drawn = true;
        result.result = S_OK;
        result.durationMilliseconds =
            std::chrono::duration<double, std::milli>(
                std::chrono::steady_clock::now() - started).count();
        return result;
    }

    void CustomCursorRenderer::Shutdown() noexcept
    {
        shapeView_ = nullptr;
        shapeTexture_ = nullptr;
        rasterizer_ = nullptr;
        blendState_ = nullptr;
        sampler_ = nullptr;
        rectConstantBuffer_ = nullptr;
        pixelShader_ = nullptr;
        vertexShader_ = nullptr;
        context_ = nullptr;
        device_ = nullptr;
        textureShapeId_ = 0;
        textureGeneration_ = 0;
        initialized_ = false;
    }

    HRESULT CustomCursorRenderer::EnsureTexture(
        const CursorShape& shape,
        bool& uploaded) noexcept
    {
        uploaded = false;
        if (shapeTexture_ &&
            textureShapeId_ == shape.id &&
            textureGeneration_ == shape.generation)
        {
            return S_OK;
        }
        if (shape.width == 0 ||
            shape.height == 0 ||
            shape.premultipliedBgra.size() !=
                static_cast<std::size_t>(shape.width) * shape.height)
        {
            return E_INVALIDARG;
        }

        D3D11_TEXTURE2D_DESC description{};
        description.Width = shape.width;
        description.Height = shape.height;
        description.MipLevels = 1;
        description.ArraySize = 1;
        description.Format = DXGI_FORMAT_B8G8R8A8_UNORM;
        description.SampleDesc.Count = 1;
        description.Usage = D3D11_USAGE_IMMUTABLE;
        description.BindFlags = D3D11_BIND_SHADER_RESOURCE;

        D3D11_SUBRESOURCE_DATA data{};
        data.pSysMem = shape.premultipliedBgra.data();
        data.SysMemPitch = shape.width * sizeof(std::uint32_t);

        winrt::com_ptr<ID3D11Texture2D> texture;
        auto hr = device_->CreateTexture2D(
            &description,
            &data,
            texture.put());
        if (FAILED(hr))
        {
            return hr;
        }
        winrt::com_ptr<ID3D11ShaderResourceView> view;
        hr = device_->CreateShaderResourceView(
            texture.get(),
            nullptr,
            view.put());
        if (FAILED(hr))
        {
            return hr;
        }

        shapeTexture_ = std::move(texture);
        shapeView_ = std::move(view);
        textureShapeId_ = shape.id;
        textureGeneration_ = shape.generation;
        uploaded = true;
        return S_OK;
    }
}
