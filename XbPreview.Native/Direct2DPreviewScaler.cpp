#include "Direct2DPreviewScaler.h"

#include "Letterbox.h"

#include <d2d1effects.h>
#include <dxgi.h>

namespace xbpreview
{
    HRESULT Direct2DPreviewScaler::Initialize(
        ID3D11Device* const d3dDevice) noexcept
    {
        if (d3dDevice == nullptr)
        {
            return E_INVALIDARG;
        }

        Shutdown();
        D2D1_FACTORY_OPTIONS factoryOptions{};
        auto result = D2D1CreateFactory(
            D2D1_FACTORY_TYPE_SINGLE_THREADED,
            __uuidof(ID2D1Factory1),
            &factoryOptions,
            factory_.put_void());
        if (FAILED(result))
        {
            Shutdown();
            return result;
        }

        winrt::com_ptr<IDXGIDevice> dxgiDevice;
        result = d3dDevice->QueryInterface(
            __uuidof(IDXGIDevice),
            dxgiDevice.put_void());
        if (FAILED(result))
        {
            Shutdown();
            return result;
        }

        result = factory_->CreateDevice(dxgiDevice.get(), device_.put());
        if (FAILED(result))
        {
            Shutdown();
            return result;
        }
        result = device_->CreateDeviceContext(
            D2D1_DEVICE_CONTEXT_OPTIONS_NONE,
            context_.put());
        if (FAILED(result))
        {
            Shutdown();
            return result;
        }
        result = context_->CreateEffect(CLSID_D2D1Scale, scaleEffect_.put());
        if (FAILED(result))
        {
            Shutdown();
            return result;
        }

        constexpr auto interpolation =
            D2D1_SCALE_INTERPOLATION_MODE_HIGH_QUALITY_CUBIC;
        result = scaleEffect_->SetValue(
            D2D1_SCALE_PROP_INTERPOLATION_MODE,
            interpolation);
        if (FAILED(result))
        {
            Shutdown();
        }
        return result;
    }

    HRESULT Direct2DPreviewScaler::CreateTargetBitmap(
        ID3D11Texture2D* const targetTexture,
        ID2D1Bitmap1** const targetBitmap) noexcept
    {
        if (context_ == nullptr || targetTexture == nullptr ||
            targetBitmap == nullptr)
        {
            return E_INVALIDARG;
        }
        *targetBitmap = nullptr;

        D3D11_TEXTURE2D_DESC description{};
        targetTexture->GetDesc(&description);
        if (description.Width == 0 || description.Height == 0 ||
            description.Format != DXGI_FORMAT_B8G8R8A8_UNORM ||
            description.SampleDesc.Count != 1)
        {
            return E_INVALIDARG;
        }

        winrt::com_ptr<IDXGISurface> surface;
        auto result = targetTexture->QueryInterface(
            __uuidof(IDXGISurface),
            surface.put_void());
        if (FAILED(result))
        {
            return result;
        }

        D2D1_BITMAP_PROPERTIES1 properties{};
        properties.pixelFormat = {
            DXGI_FORMAT_B8G8R8A8_UNORM,
            D2D1_ALPHA_MODE_IGNORE
        };
        properties.dpiX = 96.0f;
        properties.dpiY = 96.0f;
        properties.bitmapOptions =
            D2D1_BITMAP_OPTIONS_TARGET | D2D1_BITMAP_OPTIONS_CANNOT_DRAW;
        return context_->CreateBitmapFromDxgiSurface(
            surface.get(),
            &properties,
            targetBitmap);
    }

    HRESULT Direct2DPreviewScaler::Render(
        ID3D11Texture2D* const sourceTexture,
        ID2D1Bitmap1* const targetBitmap,
        const std::uint32_t targetWidth,
        const std::uint32_t targetHeight) noexcept
    {
        if (context_ == nullptr || scaleEffect_ == nullptr ||
            sourceTexture == nullptr || targetBitmap == nullptr ||
            targetWidth == 0 || targetHeight == 0)
        {
            return E_INVALIDARG;
        }

        auto result = EnsureSourceBitmap(sourceTexture);
        if (FAILED(result))
        {
            return result;
        }

        XbLetterboxRect letterbox{};
        if (!CalculateLetterbox(
                sourceDescription_.Width,
                sourceDescription_.Height,
                targetWidth,
                targetHeight,
                letterbox))
        {
            return E_INVALIDARG;
        }

        const D2D1_VECTOR_2F scale{
            letterbox.width / static_cast<float>(sourceDescription_.Width),
            letterbox.height / static_cast<float>(sourceDescription_.Height)
        };
        result = scaleEffect_->SetValue(D2D1_SCALE_PROP_SCALE, scale);
        if (FAILED(result))
        {
            return result;
        }
        scaleEffect_->SetInput(0, sourceBitmap_.get());

        context_->SetTarget(targetBitmap);
        context_->BeginDraw();
        constexpr D2D1_COLOR_F black{ 0.0f, 0.0f, 0.0f, 1.0f };
        context_->Clear(&black);
        const D2D1_POINT_2F offset{ letterbox.x, letterbox.y };
        context_->DrawImage(scaleEffect_.get(), &offset);
        result = context_->EndDraw();
        context_->SetTarget(nullptr);
        return result;
    }

    void Direct2DPreviewScaler::Shutdown() noexcept
    {
        if (context_ != nullptr)
        {
            context_->SetTarget(nullptr);
        }
        if (scaleEffect_ != nullptr)
        {
            scaleEffect_->SetInput(0, nullptr);
        }
        sourceIdentity_ = nullptr;
        sourceDescription_ = {};
        sourceBitmap_ = nullptr;
        scaleEffect_ = nullptr;
        context_ = nullptr;
        device_ = nullptr;
        factory_ = nullptr;
    }

    HRESULT Direct2DPreviewScaler::EnsureSourceBitmap(
        ID3D11Texture2D* const sourceTexture) noexcept
    {
        D3D11_TEXTURE2D_DESC description{};
        sourceTexture->GetDesc(&description);
        if (description.Width == 0 || description.Height == 0 ||
            description.Format != DXGI_FORMAT_B8G8R8A8_UNORM ||
            description.SampleDesc.Count != 1)
        {
            return E_INVALIDARG;
        }
        if (sourceBitmap_ != nullptr && sourceIdentity_ == sourceTexture &&
            sourceDescription_.Width == description.Width &&
            sourceDescription_.Height == description.Height &&
            sourceDescription_.Format == description.Format)
        {
            return S_OK;
        }

        winrt::com_ptr<IDXGISurface> surface;
        auto result = sourceTexture->QueryInterface(
            __uuidof(IDXGISurface),
            surface.put_void());
        if (FAILED(result))
        {
            return result;
        }

        D2D1_BITMAP_PROPERTIES1 properties{};
        properties.pixelFormat = {
            DXGI_FORMAT_B8G8R8A8_UNORM,
            D2D1_ALPHA_MODE_IGNORE
        };
        properties.dpiX = 96.0f;
        properties.dpiY = 96.0f;
        properties.bitmapOptions = D2D1_BITMAP_OPTIONS_NONE;
        winrt::com_ptr<ID2D1Bitmap1> replacement;
        result = context_->CreateBitmapFromDxgiSurface(
            surface.get(),
            &properties,
            replacement.put());
        if (FAILED(result))
        {
            return result;
        }

        scaleEffect_->SetInput(0, nullptr);
        sourceBitmap_ = std::move(replacement);
        sourceIdentity_ = sourceTexture;
        sourceDescription_ = description;
        return S_OK;
    }
}
