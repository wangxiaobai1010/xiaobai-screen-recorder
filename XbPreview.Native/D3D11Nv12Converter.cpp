#include "D3D11Nv12Converter.h"

#include "VideoEncoderConfig.h"

namespace xbpreview
{
    void D3D11Nv12Converter::Initialize(
        ID3D11Device* const device,
        ID3D11DeviceContext* const immediateContext,
        const std::uint32_t width,
        const std::uint32_t height,
        const std::uint32_t framesPerSecond)
    {
        Shutdown();
        if (device == nullptr || immediateContext == nullptr ||
            width == 0 || height == 0 || (width & 1u) != 0 ||
            (height & 1u) != 0 ||
            !IsSupportedVideoEncoderFrameRate(framesPerSecond))
        {
            throw winrt::hresult_invalid_argument();
        }
        winrt::com_ptr<ID3D11Device> deviceReference;
        deviceReference.copy_from(device);
        videoDevice_ = deviceReference.as<ID3D11VideoDevice>();
        winrt::com_ptr<ID3D11DeviceContext> contextReference;
        contextReference.copy_from(immediateContext);
        videoContext_ = contextReference.as<ID3D11VideoContext>();

        D3D11_VIDEO_PROCESSOR_CONTENT_DESC content{};
        content.InputFrameFormat = D3D11_VIDEO_FRAME_FORMAT_PROGRESSIVE;
        content.InputFrameRate.Numerator = framesPerSecond;
        content.InputFrameRate.Denominator = 1;
        content.InputWidth = width;
        content.InputHeight = height;
        content.OutputFrameRate.Numerator = framesPerSecond;
        content.OutputFrameRate.Denominator = 1;
        content.OutputWidth = width;
        content.OutputHeight = height;
        content.Usage = D3D11_VIDEO_USAGE_PLAYBACK_NORMAL;
        winrt::check_hresult(videoDevice_->CreateVideoProcessorEnumerator(
            &content, enumerator_.put()));

        UINT flags{};
        winrt::check_hresult(enumerator_->CheckVideoProcessorFormat(
            DXGI_FORMAT_B8G8R8A8_UNORM, &flags));
        bgraInputSupported_ =
            (flags & D3D11_VIDEO_PROCESSOR_FORMAT_SUPPORT_INPUT) != 0;
        flags = 0;
        winrt::check_hresult(enumerator_->CheckVideoProcessorFormat(
            DXGI_FORMAT_NV12, &flags));
        nv12OutputSupported_ =
            (flags & D3D11_VIDEO_PROCESSOR_FORMAT_SUPPORT_OUTPUT) != 0;
        if (!bgraInputSupported_ || !nv12OutputSupported_)
        {
            throw winrt::hresult_error(DXGI_ERROR_UNSUPPORTED);
        }
        winrt::check_hresult(videoDevice_->CreateVideoProcessor(
            enumerator_.get(), 0, processor_.put()));

        RECT rectangle{ 0, 0, static_cast<LONG>(width), static_cast<LONG>(height) };
        videoContext_->VideoProcessorSetStreamSourceRect(
            processor_.get(), 0, TRUE, &rectangle);
        videoContext_->VideoProcessorSetStreamDestRect(
            processor_.get(), 0, TRUE, &rectangle);
        videoContext_->VideoProcessorSetOutputTargetRect(
            processor_.get(), TRUE, &rectangle);
        D3D11_VIDEO_PROCESSOR_COLOR_SPACE inputColor{};
        inputColor.RGB_Range = 0;      // RGB full range, as validated by P2.2B.
        inputColor.Nominal_Range = 2;
        D3D11_VIDEO_PROCESSOR_COLOR_SPACE outputColor{};
        outputColor.YCbCr_Matrix = 1;  // BT.709.
        outputColor.Nominal_Range = 1; // Studio/limited range.
        videoContext_->VideoProcessorSetStreamColorSpace(
            processor_.get(), 0, &inputColor);
        videoContext_->VideoProcessorSetOutputColorSpace(
            processor_.get(), &outputColor);
        width_ = width;
        height_ = height;
    }

    HRESULT D3D11Nv12Converter::Convert(
        ID3D11Texture2D* const bgraTexture,
        const std::uint64_t generation,
        const std::uint32_t slot,
        ID3D11VideoProcessorOutputView* const nv12Output) noexcept
    {
        if (!processor_ || !videoContext_ || bgraTexture == nullptr ||
            nv12Output == nullptr)
        {
            return E_INVALIDARG;
        }
        try
        {
            const auto key = (generation << 8) ^ slot;
            auto& entry = inputViews_[key];
            if (!entry.view || entry.texture != bgraTexture)
            {
                entry = {};
                D3D11_VIDEO_PROCESSOR_INPUT_VIEW_DESC description{};
                description.ViewDimension = D3D11_VPIV_DIMENSION_TEXTURE2D;
                description.Texture2D.MipSlice = 0;
                description.Texture2D.ArraySlice = 0;
                winrt::check_hresult(videoDevice_->CreateVideoProcessorInputView(
                    bgraTexture,
                    enumerator_.get(),
                    &description,
                    entry.view.put()));
                entry.texture = bgraTexture;
            }
            D3D11_VIDEO_PROCESSOR_STREAM stream{};
            stream.Enable = TRUE;
            stream.OutputIndex = 0;
            stream.InputFrameOrField = 0;
            stream.PastFrames = 0;
            stream.FutureFrames = 0;
            stream.pInputSurface = entry.view.get();
            // VideoProcessorBlt only submits GPU work. The BGRA lease may be
            // returned after this call because producer CopyResource and this
            // Blt use the same multithread-protected immediate context.
            return videoContext_->VideoProcessorBlt(
                processor_.get(), nv12Output, 0, 1, &stream);
        }
        catch (const winrt::hresult_error& error)
        {
            return error.code();
        }
        catch (...)
        {
            return E_FAIL;
        }
    }

    void D3D11Nv12Converter::Shutdown() noexcept
    {
        inputViews_.clear();
        processor_ = nullptr;
        enumerator_ = nullptr;
        videoContext_ = nullptr;
        videoDevice_ = nullptr;
        width_ = 0;
        height_ = 0;
        bgraInputSupported_ = false;
        nv12OutputSupported_ = false;
    }

    ID3D11VideoDevice* D3D11Nv12Converter::VideoDevice() const noexcept
    {
        return videoDevice_.get();
    }

    ID3D11VideoProcessorEnumerator* D3D11Nv12Converter::Enumerator() const noexcept
    {
        return enumerator_.get();
    }

    bool D3D11Nv12Converter::BgraInputSupported() const noexcept
    {
        return bgraInputSupported_;
    }

    bool D3D11Nv12Converter::Nv12OutputSupported() const noexcept
    {
        return nv12OutputSupported_;
    }
}
