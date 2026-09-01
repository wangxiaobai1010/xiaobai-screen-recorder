#pragma once

#include <d3d11.h>
#include <winrt/base.h>

#include <cstdint>
#include <unordered_map>

namespace xbpreview
{
    class D3D11Nv12Converter final
    {
    public:
        void Initialize(
            ID3D11Device* device,
            ID3D11DeviceContext* immediateContext,
            std::uint32_t width,
            std::uint32_t height,
            std::uint32_t framesPerSecond);
        [[nodiscard]] HRESULT Convert(
            ID3D11Texture2D* bgraTexture,
            std::uint64_t generation,
            std::uint32_t slot,
            ID3D11VideoProcessorOutputView* nv12Output) noexcept;
        void Shutdown() noexcept;

        [[nodiscard]] ID3D11VideoDevice* VideoDevice() const noexcept;
        [[nodiscard]] ID3D11VideoProcessorEnumerator* Enumerator() const noexcept;
        [[nodiscard]] bool BgraInputSupported() const noexcept;
        [[nodiscard]] bool Nv12OutputSupported() const noexcept;

    private:
        struct InputViewEntry
        {
            ID3D11Texture2D* texture{};
            winrt::com_ptr<ID3D11VideoProcessorInputView> view;
        };

        winrt::com_ptr<ID3D11VideoDevice> videoDevice_;
        winrt::com_ptr<ID3D11VideoContext> videoContext_;
        winrt::com_ptr<ID3D11VideoProcessorEnumerator> enumerator_;
        winrt::com_ptr<ID3D11VideoProcessor> processor_;
        std::unordered_map<std::uint64_t, InputViewEntry> inputViews_;
        std::uint32_t width_{};
        std::uint32_t height_{};
        bool bgraInputSupported_{};
        bool nv12OutputSupported_{};
    };
}
