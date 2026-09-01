#pragma once

#include "XbPreviewApi.h"
#include "CameraTransform.h"
#include "CropTransform.h"
#include "CustomCursorRenderer.h"
#include "DiagnosticLogger.h"
#include "OutputCanvasTarget.h"
#include "PreviewFrameExport.h"
#include "RenderFrameTap.h"
#include "VideoEncoderConfig.h"
#include "VideoEncoderConsumer.h"
#include "WindowShowcaseBackgroundPreset.h"
#include "WindowShowcaseMotionController.h"
#include "WindowStagePunchOverlay.h"
#include "WindowStageTransform.h"

#include <d3d11.h>
#include <dxgi1_6.h>
#include <windows.graphics.directx.direct3d11.interop.h>
#include <winrt/Windows.Graphics.DirectX.Direct3D11.h>
#include <winrt/base.h>

#include <array>
#include <chrono>
#include <cstdint>
#include <mutex>
#include <string>

namespace xbpreview
{
    struct WindowCardShadowComposition;
    class PreviewRenderer final
    {
    public:
        PreviewRenderer() = default;
        PreviewRenderer(const PreviewRenderer&) = delete;
        PreviewRenderer& operator=(const PreviewRenderer&) = delete;

        void Initialize(
            HWND previewHwnd,
            HMONITOR captureMonitor,
            std::uint32_t previewWidth,
            std::uint32_t previewHeight,
            bool allowWarp,
            const RenderFrameTapConfiguration& frameTapConfiguration,
            const VideoEncoderConfiguration& videoEncoderConfiguration,
            StartupDiagnostics& startupDiagnostics);

        bool Resize(std::uint32_t width, std::uint32_t height);

        bool SetGpuExportTargetSize(
            std::uint32_t width,
            std::uint32_t height) noexcept;

        void InitializeCustomCursorLayer();

        HRESULT RenderFrame(
            ID3D11Texture2D* capturedTexture,
            std::uint32_t contentWidth,
            std::uint32_t contentHeight,
            const CropTransform& crop,
            const CameraTransform& camera,
            const CursorDrawCommand* cursorCommand,
            bool windowStage,
            bool presentationEnabled,
            const RenderFrameTapTimestamp& frameTimestamp,
            CursorRenderResult& cursorResult,
            bool& occluded);

        void Shutdown() noexcept;

        XbPreviewResult StartRecording(
            const VideoEncoderConfiguration& configuration);
        XbPreviewResult PauseRecording();
        XbPreviewResult ResumeRecording();
        XbPreviewResult StopRecording();
        XbPreviewResult CancelRecording();
        void GetRecordingSnapshot(XbRecordingSnapshot& snapshot) const;
        XbPreviewResult SetAudioControls(
            const XbAudioControlsV1& controls) noexcept;
        XbPreviewResult SetWindowStagePose(
            WindowStageDirection direction,
            WindowStageStrength strength) noexcept;
        XbPreviewResult SetWindowShowcasePose(
            WindowStageDirection direction,
            WindowStageStrength strength) noexcept;
        XbPreviewResult RequestWindowShowcaseReturn() noexcept;
        XbPreviewResult SetWindowShowcaseInactive() noexcept;
        XbPreviewResult SetWindowShowcaseBackgroundPreset(
            WindowShowcaseBackgroundPreset preset) noexcept;
        XbPreviewResult SetWindowShowcaseCustomBackground(
            const std::wstring& validatedLocalPath) noexcept;
        void GetAudioControlSnapshot(
            XbAudioControlSnapshotV1& snapshot) const noexcept;
        bool GetGpuExportFrame(
            XbPreviewGpuExportFrameV1& snapshot) const noexcept;
        void RecordRecordingFailure(
            XbPreviewResult result,
            HRESULT hresult,
            const wchar_t* message);

        [[nodiscard]] winrt::Windows::Graphics::DirectX::Direct3D11::IDirect3DDevice
            WinRtDevice() const
        {
            return winRtDevice_;
        }

        [[nodiscard]] ID3D11Device* Device() const noexcept
        {
            return device_.get();
        }

        [[nodiscard]] ID3D11DeviceContext* Context() const noexcept
        {
            return context_.get();
        }

        [[nodiscard]] const std::wstring& AdapterName() const noexcept
        {
            return adapterName_;
        }

        [[nodiscard]] bool UsedWarp() const noexcept
        {
            return usedWarp_;
        }

        [[nodiscard]] bool HdrDetected() const noexcept
        {
            return hdrDetected_;
        }

        [[nodiscard]] HRESULT DeviceRemovedReason() const noexcept;

    private:
        void CreateDevice(
            HMONITOR monitor,
            bool allowWarp,
            bool requestVideoSupport);
        void CreateSwapChain(HWND previewHwnd, std::uint32_t width, std::uint32_t height);
        void CreateBackBuffer();
        void CreateShaders();
        void LoadWindowShowcaseArtBackground();
        void LoadWindowShowcaseTexture(
            const std::wstring& path,
            bool requireFrozenDimensions,
            winrt::com_ptr<ID3D11Texture2D>& texture,
            winrt::com_ptr<ID3D11ShaderResourceView>& view,
            std::uint32_t& width,
            std::uint32_t& height);
        void EnsureSourceTexture(const D3D11_TEXTURE2D_DESC& sourceDescription);
        void DrawFullscreenPass(
            ID3D11RenderTargetView* target,
            ID3D11ShaderResourceView* source,
            const D3D11_VIEWPORT& viewport,
            const std::array<float, 8>& transforms);
        void DrawWindowCardContentPass(
            ID3D11RenderTargetView* target,
            ID3D11ShaderResourceView* source,
            const D3D11_VIEWPORT& viewport,
            const std::array<float, 8>& transforms,
            const WindowCardShadowComposition& card);
        void DrawWindowCardShadowPass(
            ID3D11RenderTargetView* target,
            const WindowCardShadowComposition& shadow);
        void DrawTransformedWindowCardContentPass(
            ID3D11RenderTargetView* target,
            ID3D11ShaderResourceView* source,
            const D3D11_VIEWPORT& viewport,
            const std::array<float, 8>& transforms,
            const WindowCardShadowComposition& card,
            const WindowStageTransformComposition& stageTransform);
        void DrawTransformedWindowCardShadowPass(
            ID3D11RenderTargetView* target,
            const D3D11_VIEWPORT& viewport,
            const WindowCardShadowComposition& shadow,
            const WindowStageTransformComposition& stageTransform);
        void DetectHdr(IDXGIOutput* output);

        static winrt::com_ptr<IDXGIAdapter1> FindAdapterForMonitor(
            HMONITOR monitor,
            winrt::com_ptr<IDXGIOutput>& matchingOutput,
            StartupDiagnostics& startupDiagnostics);

        winrt::com_ptr<ID3DBlob> CompileShader(
            const char* source,
            const char* entryPoint,
            const char* profile);

        HWND previewHwnd_{};
        std::uint32_t previewWidth_{};
        std::uint32_t previewHeight_{};
        std::uint32_t sourceWidth_{};
        std::uint32_t sourceHeight_{};
        DXGI_FORMAT sourceFormat_{ DXGI_FORMAT_UNKNOWN };
        bool usedWarp_{};
        bool hdrDetected_{};
        std::wstring adapterName_;

        winrt::com_ptr<ID3D11Device> device_;
        winrt::com_ptr<ID3D11DeviceContext> context_;
        winrt::Windows::Graphics::DirectX::Direct3D11::IDirect3DDevice winRtDevice_{ nullptr };
        winrt::com_ptr<IDXGISwapChain1> swapChain_;
        winrt::com_ptr<ID3D11RenderTargetView> previewRenderTargetView_;
        winrt::com_ptr<ID3D11Texture2D> sourceTexture_;
        winrt::com_ptr<ID3D11ShaderResourceView> sourceView_;
        winrt::com_ptr<ID3D11Texture2D> windowShowcaseArtTexture_;
        winrt::com_ptr<ID3D11ShaderResourceView> windowShowcaseArtView_;
        winrt::com_ptr<ID3D11VertexShader> vertexShader_;
        winrt::com_ptr<ID3D11VertexShader> windowStageQuadVertexShader_;
        winrt::com_ptr<ID3D11PixelShader> pixelShader_;
        winrt::com_ptr<ID3D11PixelShader> windowCardContentPixelShader_;
        winrt::com_ptr<ID3D11PixelShader> windowCardShadowPixelShader_;
        winrt::com_ptr<ID3D11PixelShader> windowStageShadowPixelShader_;
        winrt::com_ptr<ID3D11Buffer> cameraConstantBuffer_;
        winrt::com_ptr<ID3D11Buffer> windowCardContentConstantBuffer_;
        winrt::com_ptr<ID3D11Buffer> windowCardShadowConstantBuffer_;
        winrt::com_ptr<ID3D11Buffer> windowStageQuadConstantBuffer_;
        winrt::com_ptr<ID3D11Buffer> windowStageShadowConstantBuffer_;
        winrt::com_ptr<ID3D11SamplerState> sampler_;
        winrt::com_ptr<ID3D11RasterizerState> rasterizer_;
        winrt::com_ptr<ID3D11BlendState> windowCardShadowBlendState_;
        OutputCanvasTarget outputCanvas_;
        PreviewFrameExport previewFrameExport_;
        RenderFrameTap frameTap_;
        VideoEncoderConsumer videoEncoder_;
        VideoDeviceSetupStatus videoDeviceStatus_{};
        mutable std::mutex recordingMutex_;
        mutable std::mutex visualMutex_;
        CustomCursorRenderer customCursorRenderer_;
        StartupDiagnostics* startupDiagnostics_{};
        bool startupInstrumentationActive_{};
        bool direct2dFailureReported_{};
        WindowShowcaseBackgroundPreset windowShowcaseBackgroundPreset_{
            WindowShowcaseBackgroundPreset::Warm };
        bool windowShowcaseCustomBackground_{};
        std::uint32_t windowShowcaseBackgroundWidth_{
            WindowShowcaseArtPixelWidth };
        std::uint32_t windowShowcaseBackgroundHeight_{
            WindowShowcaseArtPixelHeight };
        WindowStageTransformParameters windowStageTransform_{};
        WindowStagePunchCandidate windowStagePunchCandidate_{
            WindowStagePunchCandidate::Disabled };
        bool windowShowcaseMotionEnabled_{};
        bool windowShowcaseMotionStarted_{};
        HANDLE windowShowcaseMotionEnterEvent_{};
        HANDLE windowShowcaseMotionReturnEvent_{};
        WindowShowcaseMotionPreset windowShowcaseMotionPreset_{
            WindowShowcaseMotionPreset::A };
        WindowStageTransformParameters windowShowcaseMotionTarget_{};
        WindowShowcaseMotionController windowShowcaseMotionController_;
        std::chrono::steady_clock::time_point windowShowcaseMotionStart_{};
    };
}
