#include "PreviewRenderer.h"

#include "Letterbox.h"
#include "WindowCardShadowPass.h"
#include "WindowShowcaseBackgroundPreset.h"
#include "WindowShowcaseMotionController.h"
#include "WindowStageComposer.h"
#include "WindowStagePunchOverlay.h"
#include "WindowStageTransform.h"

#include <d3d10_1.h>
#include <d3dcompiler.h>
#include <wincodec.h>

#include <algorithm>
#include <array>
#include <chrono>
#include <cstring>
#include <cwctype>
#include <filesystem>
#include <limits>
#include <stdexcept>
#include <string>
#include <vector>

namespace
{
    std::int64_t QueryQpc() noexcept
    {
        LARGE_INTEGER value{};
        return QueryPerformanceCounter(&value) ? value.QuadPart : 0;
    }

    constexpr char ShaderSource[] = R"(
Texture2D SourceTexture : register(t0);
SamplerState LinearSampler : register(s0);
cbuffer TransformBuffer : register(b0)
{
    float4 CameraUv;
    float4 CropUv;
};

struct VertexOutput
{
    float4 position : SV_Position;
    float2 uv : TEXCOORD0;
};

VertexOutput VSMain(uint vertexId : SV_VertexID)
{
    static const float2 positions[3] =
    {
        float2(-1.0f, -1.0f),
        float2(-1.0f,  3.0f),
        float2( 3.0f, -1.0f)
    };

    static const float2 uvs[3] =
    {
        float2(0.0f, 1.0f),
        float2(0.0f, -1.0f),
        float2(2.0f, 1.0f)
    };

    VertexOutput output;
    output.position = float4(positions[vertexId], 0.0f, 1.0f);
    output.uv = uvs[vertexId];
    return output;
}

float4 PSMain(VertexOutput input) : SV_Target
{
    const float2 regionLocalUv =
        CameraUv.xy + (input.uv * CameraUv.zw);
    const float2 sourceUv =
        CropUv.xy + (regionLocalUv * CropUv.zw);
    return SourceTexture.Sample(LinearSampler, sourceUv);
}
)";

    bool IsHdrColorSpace(const DXGI_COLOR_SPACE_TYPE colorSpace) noexcept
    {
        return colorSpace == DXGI_COLOR_SPACE_RGB_FULL_G2084_NONE_P2020 ||
            colorSpace == DXGI_COLOR_SPACE_RGB_STUDIO_G2084_NONE_P2020 ||
            colorSpace == DXGI_COLOR_SPACE_RGB_FULL_G10_NONE_P709;
    }

    struct EnvironmentValue
    {
        bool present{};
        std::wstring value;
    };

    EnvironmentValue ReadEnvironment(const wchar_t* const name)
    {
        SetLastError(ERROR_SUCCESS);
        const auto required = GetEnvironmentVariableW(name, nullptr, 0);
        if (required == 0)
        {
            const auto error = GetLastError();
            if (error == ERROR_ENVVAR_NOT_FOUND)
            {
                return {};
            }
            if (error == ERROR_SUCCESS)
            {
                return { true, {} };
            }
            throw winrt::hresult_error(
                HRESULT_FROM_WIN32(error),
                L"Unable to read the Window Stage 2.5D test selector.");
        }

        EnvironmentValue result{ true, std::wstring(required, L'\0') };
        const auto written = GetEnvironmentVariableW(
            name,
            result.value.data(),
            static_cast<DWORD>(result.value.size()));
        if (written == 0)
        {
            if (GetLastError() == ERROR_SUCCESS)
            {
                result.value.clear();
                return result;
            }
            throw winrt::hresult_error(
                HRESULT_FROM_WIN32(GetLastError()),
                L"Unable to read the Window Stage 2.5D test selector.");
        }
        if (written >= result.value.size())
        {
            throw winrt::hresult_error(
                HRESULT_FROM_WIN32(ERROR_INSUFFICIENT_BUFFER),
                L"Unable to read the Window Stage 2.5D test selector.");
        }
        result.value.resize(written);
        return result;
    }

    xbpreview::WindowShowcaseBackgroundPreset
        ReadWindowShowcaseBackgroundSelector()
    {
        // This startup-only selector is read before the renderer is created.
        // There is no mutable Recording-time background path.
        const auto value = ReadEnvironment(
            L"XB_PREVIEW_TEST_WINDOW_SHOWCASE_BACKGROUND_PRESET");
        if (!value.present || value.value == L"WARM")
        {
            return xbpreview::WindowShowcaseBackgroundPreset::Warm;
        }
        if (value.value == L"ART" || value.value == L"ART01")
        {
            return xbpreview::WindowShowcaseBackgroundPreset::Art01;
        }
        if (value.value == L"ART001")
        {
            return xbpreview::WindowShowcaseBackgroundPreset::Art001;
        }
        throw winrt::hresult_invalid_argument(
            L"Invalid Window Showcase Background selector. Expected WARM, "
            L"ART01, or ART001.");
    }

    std::wstring PackagedWindowShowcaseArtPath(
        const xbpreview::WindowShowcaseBackgroundPreset preset)
    {
        std::array<wchar_t, 32768> executablePath{};
        const auto written = GetModuleFileNameW(
            nullptr,
            executablePath.data(),
            static_cast<DWORD>(executablePath.size()));
        if (written == 0 || written >= executablePath.size())
        {
            throw winrt::hresult_error(
                HRESULT_FROM_WIN32(
                    written == 0 ? GetLastError() : ERROR_INSUFFICIENT_BUFFER),
                L"Unable to resolve the packaged ART background path.");
        }
        std::wstring path(executablePath.data(), written);
        const auto separator = path.find_last_of(L"\\/");
        if (separator == std::wstring::npos)
        {
            throw winrt::hresult_invalid_argument(
                L"Unable to resolve the packaged ART background directory.");
        }
        path.resize(separator + 1);
        const auto relativePath =
            xbpreview::WindowShowcaseArtAssetRelativePath(preset);
        if (relativePath.empty())
        {
            throw winrt::hresult_invalid_argument(
                L"The selected Window Showcase Background is not ART.");
        }
        path.append(relativePath);
        return path;
    }

    bool IsSupportedStaticBackgroundPath(
        const std::filesystem::path& path) noexcept
    {
        auto extension = path.extension().wstring();
        std::transform(
            extension.begin(), extension.end(), extension.begin(),
            [](const wchar_t value)
            {
                return static_cast<wchar_t>(towlower(value));
            });
        return extension == L".png" || extension == L".jpg" ||
            extension == L".jpeg" || extension == L".bmp";
    }

    bool IsExistingLocalStaticBackground(
        const std::wstring& value) noexcept
    {
        try
        {
            const std::filesystem::path path(value);
            if (value.empty() || !path.is_absolute() ||
                !IsSupportedStaticBackgroundPath(path))
            {
                return false;
            }
            std::error_code error;
            if (!std::filesystem::is_regular_file(path, error) || error)
            {
                return false;
            }
            std::array<wchar_t, MAX_PATH> volume{};
            if (!GetVolumePathNameW(
                    path.c_str(), volume.data(),
                    static_cast<DWORD>(volume.size())))
            {
                return false;
            }
            const auto driveType = GetDriveTypeW(volume.data());
            return driveType != DRIVE_REMOTE &&
                driveType != DRIVE_NO_ROOT_DIR &&
                driveType != DRIVE_UNKNOWN;
        }
        catch (...)
        {
            return false;
        }
    }

    xbpreview::WindowStageTransformParameters ReadWindowStageTransformSelector()
    {
        // Deliberately test-only: no ABI or product-setting surface is added.
        const auto directionValue = ReadEnvironment(
            L"XB_PREVIEW_TEST_WINDOW_STAGE_25D_DIRECTION");
        const auto strengthValue = ReadEnvironment(
            L"XB_PREVIEW_TEST_WINDOW_STAGE_25D_STRENGTH");
        if (!directionValue.present && !strengthValue.present)
        {
            return xbpreview::WindowStageIdentityTransform;
        }
        if (directionValue.present != strengthValue.present)
        {
            throw winrt::hresult_invalid_argument(
                L"Window Stage 2.5D test direction and strength must be "
                L"set together.");
        }

        xbpreview::WindowStageDirection direction{};
        xbpreview::WindowStageStrength strength{};
        if (!xbpreview::TryParseWindowStageDirection(
                directionValue.value.c_str(), direction) ||
            !xbpreview::TryParseWindowStageStrength(
                strengthValue.value.c_str(), strength))
        {
            throw winrt::hresult_invalid_argument(
                L"Invalid Window Stage 2.5D test selector. Direction must be "
                L"LEFT, FRONT, or RIGHT and strength must be LEVEL_1, "
                L"LEVEL_2, or LEVEL_3.");
        }

        xbpreview::WindowStageTransformParameters transform{};
        if (!xbpreview::ResolveWindowStageTransform(
                direction, strength, transform))
        {
            throw winrt::hresult_invalid_argument(
                L"Unable to resolve the Window Stage 2.5D test selector.");
        }
        return transform;
    }

    xbpreview::WindowStagePunchCandidate ReadWindowStagePunchSelector()
    {
        // Deliberately test-only for this visual A/B/C round. Enabling the
        // selector represents Manual ownership in the harness; the derived
        // overlay itself never writes Content Camera or base Stage state.
        const auto value = ReadEnvironment(
            L"XB_PREVIEW_TEST_WINDOW_STAGE_MANUAL_PUNCH_CANDIDATE");
        if (!value.present)
        {
            return xbpreview::WindowStagePunchCandidate::Disabled;
        }

        xbpreview::WindowStagePunchCandidate candidate{};
        if (!xbpreview::TryParseWindowStagePunchCandidate(
                value.value.c_str(), candidate))
        {
            throw winrt::hresult_invalid_argument(
                L"Invalid Manual Zoom Punch-in candidate. Expected A, B, or C.");
        }
        return candidate;
    }

    struct WindowShowcaseMotionTestSelector
    {
        bool enabled{};
        xbpreview::WindowShowcaseMotionPreset preset{
            xbpreview::WindowShowcaseMotionPreset::A };
        xbpreview::WindowStageTransformParameters target{};
        std::wstring enterEventName;
        std::wstring returnEventName;
    };

    WindowShowcaseMotionTestSelector ReadWindowShowcaseMotionSelector()
    {
        // Deliberately test-only: this selector starts exactly one showcase
        // pass when Window Capture first produces a frame. It is not a product
        // trigger, UI setting, recording event, or Director-owned command.
        const auto presetValue = ReadEnvironment(
            L"XB_PREVIEW_TEST_WINDOW_SHOWCASE_MOTION_PRESET");
        if (!presetValue.present)
        {
            return {};
        }

        xbpreview::WindowShowcaseMotionPreset preset{};
        if (!xbpreview::TryParseWindowShowcaseMotionPreset(
                presetValue.value.c_str(), preset) ||
            preset != xbpreview::WindowShowcaseMotionPreset::A)
        {
            throw winrt::hresult_invalid_argument(
                L"Invalid Window Showcase Motion test selector. Persistent "
                L"Pose accepts only the selected preset A.");
        }

        const auto returnEventValue = ReadEnvironment(
            L"XB_PREVIEW_TEST_WINDOW_SHOWCASE_MOTION_RETURN_EVENT");
        if (!returnEventValue.present || returnEventValue.value.empty())
        {
            throw winrt::hresult_invalid_argument(
                L"Window Showcase Motion persistent-pose smoke requires a "
                L"test-only Return event name.");
        }
        const auto enterEventValue = ReadEnvironment(
            L"XB_PREVIEW_TEST_WINDOW_SHOWCASE_MOTION_ENTER_EVENT");
        if (enterEventValue.present && enterEventValue.value.empty())
        {
            throw winrt::hresult_invalid_argument(
                L"Window Showcase Motion test-only Enter event name cannot "
                L"be empty.");
        }

        const auto directionValue = ReadEnvironment(
            L"XB_PREVIEW_TEST_WINDOW_SHOWCASE_MOTION_DIRECTION");
        const auto strengthValue = ReadEnvironment(
            L"XB_PREVIEW_TEST_WINDOW_SHOWCASE_MOTION_STRENGTH");
        xbpreview::WindowStageDirection direction{
            xbpreview::WindowStageDirection::Right };
        xbpreview::WindowStageStrength strength{
            xbpreview::WindowStageStrength::Level2 };
        if (directionValue.present != strengthValue.present)
        {
            throw winrt::hresult_invalid_argument(
                L"Window Showcase Motion direction and strength must be "
                L"set together.");
        }
        if (directionValue.present &&
            (!xbpreview::TryParseWindowStageDirection(
                directionValue.value.c_str(), direction) ||
                !xbpreview::TryParseWindowStageStrength(
                    strengthValue.value.c_str(), strength)))
        {
            throw winrt::hresult_invalid_argument(
                L"Invalid Window Showcase Motion target. Direction must be "
                L"LEFT, FRONT, or RIGHT and strength must be LEVEL_1, "
                L"LEVEL_2, or LEVEL_3.");
        }

        xbpreview::WindowStageTransformParameters target{};
        if (!xbpreview::ResolveWindowStageTransform(
                direction, strength, target))
        {
            throw winrt::hresult_invalid_argument(
                L"Unable to resolve the frozen Window Showcase Motion "
                L"target.");
        }
        return {
            true,
            preset,
            target,
            enterEventValue.present ? enterEventValue.value : std::wstring{},
            returnEventValue.value
        };
    }
}

namespace xbpreview
{
    void PreviewRenderer::Initialize(
        const HWND previewHwnd,
        const HMONITOR captureMonitor,
        const std::uint32_t previewWidth,
        const std::uint32_t previewHeight,
        const bool allowWarp,
        const RenderFrameTapConfiguration& frameTapConfiguration,
        const VideoEncoderConfiguration& videoEncoderConfiguration,
        StartupDiagnostics& startupDiagnostics)
    {
        startupDiagnostics_ = &startupDiagnostics;
        startupInstrumentationActive_ = true;
        direct2dFailureReported_ = false;
        XB_STARTUP_STEP(
            startupDiagnostics,
            "RendererInput",
            "ValidatePreviewWindowAndSize",
            "IsWindow",
            [&]
            {
                if (!IsWindow(previewHwnd) ||
                    previewWidth == 0 || previewHeight == 0)
                {
                    throw winrt::hresult_invalid_argument();
                }
            });

        previewHwnd_ = previewHwnd;
        previewWidth_ = previewWidth;
        previewHeight_ = previewHeight;
        windowShowcaseBackgroundPreset_ =
            ReadWindowShowcaseBackgroundSelector();
        windowShowcaseCustomBackground_ = false;
        windowShowcaseBackgroundWidth_ = WindowShowcaseArtPixelWidth;
        windowShowcaseBackgroundHeight_ = WindowShowcaseArtPixelHeight;
        windowStageTransform_ = ReadWindowStageTransformSelector();
        windowStagePunchCandidate_ = ReadWindowStagePunchSelector();
        const auto showcaseMotion = ReadWindowShowcaseMotionSelector();
        if (showcaseMotion.enabled && !windowStageTransform_.IsIdentity())
        {
            throw winrt::hresult_invalid_argument(
                L"Window Showcase Motion cannot be combined with a static "
                L"Window Stage 2.5D test selector.");
        }
        windowShowcaseMotionEnabled_ = showcaseMotion.enabled;
        windowShowcaseMotionStarted_ = false;
        windowShowcaseMotionPreset_ = showcaseMotion.preset;
        windowShowcaseMotionTarget_ = showcaseMotion.target;
        windowShowcaseMotionController_.Reset();
        if (showcaseMotion.enabled)
        {
            if (!showcaseMotion.enterEventName.empty())
            {
                windowShowcaseMotionEnterEvent_ = OpenEventW(
                    SYNCHRONIZE,
                    FALSE,
                    showcaseMotion.enterEventName.c_str());
                if (windowShowcaseMotionEnterEvent_ == nullptr)
                {
                    throw winrt::hresult_error(
                        HRESULT_FROM_WIN32(GetLastError()),
                        L"Unable to open the Window Showcase Motion test-only "
                        L"Enter event.");
                }
            }
            windowShowcaseMotionReturnEvent_ = OpenEventW(
                SYNCHRONIZE,
                FALSE,
                showcaseMotion.returnEventName.c_str());
            if (windowShowcaseMotionReturnEvent_ == nullptr)
            {
                throw winrt::hresult_error(
                    HRESULT_FROM_WIN32(GetLastError()),
                    L"Unable to open the Window Showcase Motion test-only "
                    L"Return event.");
            }
        }

        CreateDevice(
            captureMonitor,
            allowWarp,
            true);
        XB_STARTUP_STEP(
            startupDiagnostics,
            "Direct2DPreview",
            "InitializeBuiltInScaleEffect",
            "IDXGIDevice/ID2D1Device/CLSID_D2D1Scale",
            [&]
            {
                winrt::check_hresult(
                    previewFrameExport_.Initialize(device_.get()));
            });
        if (IsWindowShowcaseArtPreset(windowShowcaseBackgroundPreset_))
        {
            XB_STARTUP_STEP(
                startupDiagnostics,
                "Background",
                "LoadStaticArtBackground",
                "WIC/D3D11",
                [&]
                {
                    LoadWindowShowcaseArtBackground();
                });
        }
        XB_STARTUP_STEP(
            startupDiagnostics,
            "SwapChain",
            "InitializePreviewSwapChain",
            "PreviewRenderer::CreateSwapChain",
            [&]
            {
                CreateSwapChain(previewHwnd, previewWidth, previewHeight);
            });
        XB_STARTUP_STEP(
            startupDiagnostics,
            "Shaders",
            "InitializePreviewShaders",
            "PreviewRenderer::CreateShaders",
            [&]
            {
                CreateShaders();
            });
        auto effectiveTapConfiguration = frameTapConfiguration;
        if (videoDeviceStatus_.videoSupportDeviceCreated &&
            videoDeviceStatus_.multithreadProtectionEnabled &&
            !frameTapConfiguration.enabled)
        {
            effectiveTapConfiguration.enabled = true;
            effectiveTapConfiguration.startDiagnosticConsumer = false;
        }
        XB_STARTUP_STEP(
            startupDiagnostics,
            "FrameTap",
            "InitializeRenderFrameTap",
            "RenderFrameTap::Initialize",
            [&]
            {
                frameTap_.Initialize(
                    device_.get(), context_.get(), effectiveTapConfiguration);
            });
        XB_STARTUP_STEP(
            startupDiagnostics,
            "EncoderBoundary",
            videoEncoderConfiguration.enabled
                ? "RegisterEncoderConsumerDeferredUntilFirstFrame"
                : "KeepEncoderConsumerDisabled",
            "VideoEncoderConsumer::Start",
            [&]
            {
                videoEncoder_.Start(
                    frameTap_,
                    device_.get(),
                    context_.get(),
                    videoEncoderConfiguration,
                    videoDeviceStatus_);
            });
        startupInstrumentationActive_ = false;
    }

    bool PreviewRenderer::Resize(const std::uint32_t width, const std::uint32_t height)
    {
        if (width == 0 || height == 0)
        {
            return false;
        }

        if (width == previewWidth_ && height == previewHeight_)
        {
            return false;
        }

        ID3D11RenderTargetView* nullTarget = nullptr;
        context_->OMSetRenderTargets(1, &nullTarget, nullptr);
        previewRenderTargetView_ = nullptr;
        context_->Flush();

        winrt::check_hresult(swapChain_->ResizeBuffers(
            0,
            width,
            height,
            DXGI_FORMAT_UNKNOWN,
            0));

        previewWidth_ = width;
        previewHeight_ = height;
        CreateBackBuffer();
        return true;
    }

    bool PreviewRenderer::SetGpuExportTargetSize(
        const std::uint32_t width,
        const std::uint32_t height) noexcept
    {
        return previewFrameExport_.SetTargetSize(width, height);
    }

    void PreviewRenderer::InitializeCustomCursorLayer()
    {
        customCursorRenderer_.Initialize(device_.get(), context_.get());
    }

    XbPreviewResult PreviewRenderer::SetWindowStagePose(
        const WindowStageDirection direction,
        const WindowStageStrength strength) noexcept
    {
        WindowStageTransformParameters transform{};
        if (!ResolveWindowStageTransform(direction, strength, transform))
        {
            return XbPreviewResult_InvalidArgument;
        }
        std::lock_guard lock(visualMutex_);
        windowStageTransform_ = transform;
        windowShowcaseMotionEnabled_ = false;
        windowShowcaseMotionStarted_ = false;
        windowShowcaseMotionController_.Reset();
        return XbPreviewResult_Ok;
    }

    XbPreviewResult PreviewRenderer::SetWindowShowcasePose(
        const WindowStageDirection direction,
        const WindowStageStrength strength) noexcept
    {
        WindowStageTransformParameters target{};
        if (!ResolveWindowStageTransform(direction, strength, target))
        {
            return XbPreviewResult_InvalidArgument;
        }

        std::lock_guard lock(visualMutex_);

        // Product Manual Zoom owns the frozen B overlay. It derives only a
        // transient presentation scale from appliedZoom and never replaces the
        // selected base pose or the persistent motion owner.
        const auto targetUnchanged =
            windowShowcaseMotionTarget_.scale == target.scale &&
            windowShowcaseMotionTarget_.horizontalPlacementFraction ==
                target.horizontalPlacementFraction &&
            windowShowcaseMotionTarget_.verticalPlacementFraction ==
                target.verticalPlacementFraction &&
            windowShowcaseMotionTarget_.rotationXDegrees ==
                target.rotationXDegrees &&
            windowShowcaseMotionTarget_.rotationYDegrees ==
                target.rotationYDegrees &&
            windowShowcaseMotionTarget_.perspectiveDepth ==
                target.perspectiveDepth;
        windowStagePunchCandidate_ = WindowStagePunchCandidate::Showcase;
        windowShowcaseMotionPreset_ = WindowShowcaseMotionPreset::A;
        windowShowcaseMotionTarget_ = target;

        if (!windowShowcaseMotionEnabled_)
        {
            windowShowcaseMotionEnabled_ = true;
            windowShowcaseMotionStarted_ = false;
            windowShowcaseMotionController_.Reset();
            windowStageTransform_ = WindowStageIdentityTransform;
            return XbPreviewResult_Ok;
        }

        // Before the first Window Capture frame there is no running segment;
        // only retarget the frozen first-frame entrance. Once active, Start()
        // samples CurrentTransform() and therefore changes pose without an
        // Identity jump, flash, or loss of persistent motion.
        if (!windowShowcaseMotionStarted_)
        {
            return XbPreviewResult_Ok;
        }
        const auto motionState = windowShowcaseMotionController_.State();
        if (targetUnchanged &&
            (motionState == WindowShowcaseMotionState::Transition ||
                motionState == WindowShowcaseMotionState::Stay))
        {
            return XbPreviewResult_Ok;
        }

        const auto now = std::chrono::steady_clock::now();
        const auto elapsedMilliseconds =
            std::chrono::duration<double, std::milli>(
                now - windowShowcaseMotionStart_).count();
        if (!windowShowcaseMotionController_.Start(
                target,
                WindowShowcaseMotionPreset::A,
                elapsedMilliseconds))
        {
            return XbPreviewResult_InvalidArgument;
        }
        windowStageTransform_ =
            windowShowcaseMotionController_.CurrentTransform();
        return XbPreviewResult_Ok;
    }

    XbPreviewResult PreviewRenderer::RequestWindowShowcaseReturn() noexcept
    {
        std::lock_guard lock(visualMutex_);
        windowStagePunchCandidate_ = WindowStagePunchCandidate::Showcase;

        // A same-direction click before the first Window frame cancels the
        // pending entrance while the card is still exactly Identity.
        if (!windowShowcaseMotionEnabled_ || !windowShowcaseMotionStarted_)
        {
            windowShowcaseMotionEnabled_ = false;
            windowShowcaseMotionStarted_ = false;
            windowShowcaseMotionController_.Reset();
            windowStageTransform_ = WindowStageIdentityTransform;
            return XbPreviewResult_Ok;
        }

        const auto now = std::chrono::steady_clock::now();
        const auto elapsedMilliseconds =
            std::chrono::duration<double, std::milli>(
                now - windowShowcaseMotionStart_).count();
        if (!windowShowcaseMotionController_.RequestReturn(
                elapsedMilliseconds))
        {
            return XbPreviewResult_InvalidArgument;
        }
        windowStageTransform_ =
            windowShowcaseMotionController_.CurrentTransform();
        return XbPreviewResult_Ok;
    }

    XbPreviewResult PreviewRenderer::SetWindowShowcaseInactive() noexcept
    {
        std::lock_guard lock(visualMutex_);
        windowStagePunchCandidate_ = WindowStagePunchCandidate::Showcase;
        windowShowcaseMotionEnabled_ = false;
        windowShowcaseMotionStarted_ = false;
        windowShowcaseMotionController_.Reset();
        windowStageTransform_ = WindowStageIdentityTransform;
        return XbPreviewResult_Ok;
    }

    XbPreviewResult PreviewRenderer::SetWindowShowcaseBackgroundPreset(
        const WindowShowcaseBackgroundPreset preset) noexcept
    {
        if (preset != WindowShowcaseBackgroundPreset::Warm &&
            !IsWindowShowcaseArtPreset(preset))
        {
            return XbPreviewResult_InvalidArgument;
        }
        try
        {
            winrt::com_ptr<ID3D11Texture2D> texture;
            winrt::com_ptr<ID3D11ShaderResourceView> view;
            std::uint32_t width = WindowShowcaseArtPixelWidth;
            std::uint32_t height = WindowShowcaseArtPixelHeight;
            if (IsWindowShowcaseArtPreset(preset))
            {
                LoadWindowShowcaseTexture(
                    PackagedWindowShowcaseArtPath(preset),
                    true,
                    texture,
                    view,
                    width,
                    height);
            }
            std::lock_guard lock(visualMutex_);
            windowShowcaseBackgroundPreset_ = preset;
            windowShowcaseCustomBackground_ = false;
            windowShowcaseBackgroundWidth_ = width;
            windowShowcaseBackgroundHeight_ = height;
            windowShowcaseArtTexture_ = std::move(texture);
            windowShowcaseArtView_ = std::move(view);
            return XbPreviewResult_Ok;
        }
        catch (...)
        {
            // The active texture remains untouched because replacement
            // resources are committed only after a complete decode/upload.
            return XbPreviewResult_NativeFailure;
        }
    }

    XbPreviewResult PreviewRenderer::SetWindowShowcaseCustomBackground(
        const std::wstring& validatedLocalPath) noexcept
    {
        if (!IsExistingLocalStaticBackground(validatedLocalPath))
        {
            return XbPreviewResult_InvalidArgument;
        }
        try
        {
            const auto initialized = CoInitializeEx(
                nullptr, COINIT_MULTITHREADED);
            const auto uninitialize = initialized == S_OK ||
                initialized == S_FALSE;
            if (FAILED(initialized) && initialized != RPC_E_CHANGED_MODE)
            {
                return XbPreviewResult_NativeFailure;
            }

            winrt::com_ptr<ID3D11Texture2D> texture;
            winrt::com_ptr<ID3D11ShaderResourceView> view;
            std::uint32_t width{};
            std::uint32_t height{};
            try
            {
                LoadWindowShowcaseTexture(
                    validatedLocalPath,
                    false,
                    texture,
                    view,
                    width,
                    height);
            }
            catch (...)
            {
                if (uninitialize)
                {
                    CoUninitialize();
                }
                throw;
            }
            if (uninitialize)
            {
                CoUninitialize();
            }

            std::lock_guard lock(visualMutex_);
            windowShowcaseBackgroundPreset_ =
                WindowShowcaseBackgroundPreset::Warm;
            windowShowcaseCustomBackground_ = true;
            windowShowcaseBackgroundWidth_ = width;
            windowShowcaseBackgroundHeight_ = height;
            windowShowcaseArtTexture_ = std::move(texture);
            windowShowcaseArtView_ = std::move(view);
            return XbPreviewResult_Ok;
        }
        catch (...)
        {
            return XbPreviewResult_NativeFailure;
        }
    }

    HRESULT PreviewRenderer::RenderFrame(
        ID3D11Texture2D* const capturedTexture,
        const std::uint32_t contentWidth,
        const std::uint32_t contentHeight,
        const CropTransform& crop,
        const CameraTransform& camera,
        const CursorDrawCommand* const cursorCommand,
        const bool windowStage,
        const bool presentationEnabled,
        const RenderFrameTapTimestamp& frameTimestamp,
        CursorRenderResult& cursorResult,
        bool& occluded)
    {
        occluded = false;
        cursorResult = {};
        if (capturedTexture == nullptr || contentWidth == 0 || contentHeight == 0)
        {
            return E_INVALIDARG;
        }

        D3D11_TEXTURE2D_DESC sourceDescription{};
        capturedTexture->GetDesc(&sourceDescription);
        if (sourceDescription.Format != DXGI_FORMAT_B8G8R8A8_UNORM ||
            sourceDescription.Width < contentWidth ||
            sourceDescription.Height < contentHeight)
        {
            return DXGI_ERROR_INVALID_CALL;
        }

        D3D11_TEXTURE2D_DESC contentDescription = sourceDescription;
        contentDescription.Width = contentWidth;
        contentDescription.Height = contentHeight;
        EnsureSourceTexture(contentDescription);

        // Copy only ContentSize. A WGC pool surface can be larger after a
        // window resize and its remaining pixels are explicitly undefined.
        // This remains a GPU-to-GPU copy: there is no Map or CPU readback.
        const D3D11_BOX contentBox{
            0, 0, 0,
            contentWidth, contentHeight, 1
        };
        context_->CopySubresourceRegion(
            sourceTexture_.get(), 0, 0, 0, 0,
            capturedTexture, 0, &contentBox);

        const OutputCanvasDescription outputDescription{
            crop.outputWidth,
            crop.outputHeight,
            DXGI_FORMAT_B8G8R8A8_UNORM
        };
        outputCanvas_.Ensure(device_.get(), outputDescription);

        FlatWindowStageComposition flatWindowStage{};
        WindowShowcaseBackgroundComposition windowShowcaseBackground{};
        WindowCardShadowComposition windowCardShadow{};
        WindowStageTransformComposition windowStageTransform{};
        WindowStageTransformParameters selectedStageTransform{};
        WindowStagePunchCandidate selectedPunchCandidate{
            WindowStagePunchCandidate::Disabled };
        WindowShowcaseBackgroundPreset selectedBackgroundPreset{
            WindowShowcaseBackgroundPreset::Warm };
        bool selectedCustomBackground{};
        std::uint32_t selectedBackgroundWidth{};
        std::uint32_t selectedBackgroundHeight{};
        winrt::com_ptr<ID3D11ShaderResourceView> selectedBackgroundView;
        {
            std::lock_guard visualLock(visualMutex_);
            if (windowStage && windowShowcaseMotionEnabled_)
            {
                const auto now = std::chrono::steady_clock::now();
                const auto enterRequested =
                    windowShowcaseMotionEnterEvent_ == nullptr ||
                    WaitForSingleObject(
                        windowShowcaseMotionEnterEvent_, 0) == WAIT_OBJECT_0;
                if (!windowShowcaseMotionStarted_ && enterRequested)
                {
                    windowShowcaseMotionStart_ = now;
                    if (!windowShowcaseMotionController_.Start(
                            windowShowcaseMotionTarget_,
                            windowShowcaseMotionPreset_,
                            0.0))
                    {
                        return E_INVALIDARG;
                    }
                    windowShowcaseMotionStarted_ = true;
                }
                if (windowShowcaseMotionStarted_)
                {
                    const auto elapsedMilliseconds =
                        std::chrono::duration<double, std::milli>(
                            now - windowShowcaseMotionStart_).count();
                    if (windowShowcaseMotionReturnEvent_ != nullptr &&
                        WaitForSingleObject(
                            windowShowcaseMotionReturnEvent_,
                            0) == WAIT_OBJECT_0 &&
                        !windowShowcaseMotionController_.RequestReturn(
                            elapsedMilliseconds))
                    {
                        return E_INVALIDARG;
                    }
                    if (!windowShowcaseMotionController_.Update(
                            elapsedMilliseconds))
                    {
                        return E_INVALIDARG;
                    }
                    windowStageTransform_ =
                        windowShowcaseMotionController_.CurrentTransform();
                }
            }
            selectedStageTransform = windowStageTransform_;
            selectedPunchCandidate = windowStagePunchCandidate_;
            selectedBackgroundPreset = windowShowcaseBackgroundPreset_;
            selectedCustomBackground = windowShowcaseCustomBackground_;
            selectedBackgroundWidth = windowShowcaseBackgroundWidth_;
            selectedBackgroundHeight = windowShowcaseBackgroundHeight_;
            selectedBackgroundView = windowShowcaseArtView_;
        }
        WindowStageTransformParameters windowStagePresentationTransform{};
        if (windowStage && !ComposeWindowStagePunchOverlay(
                selectedStageTransform,
                selectedPunchCandidate,
                camera.appliedZoom,
                windowStagePresentationTransform))
        {
            return E_INVALIDARG;
        }
        if (windowStage && !WindowStageComposer::ComposeFlat(
                contentWidth,
                contentHeight,
                outputDescription.width,
                outputDescription.height,
                flatWindowStage))
        {
            return E_INVALIDARG;
        }
        const auto backgroundResolved = selectedCustomBackground
            ? ResolveWindowShowcaseTextureBackground(
                selectedBackgroundWidth,
                selectedBackgroundHeight,
                outputDescription.width,
                outputDescription.height,
                windowShowcaseBackground)
            : ResolveWindowShowcaseBackground(
                selectedBackgroundPreset,
                outputDescription.width,
                outputDescription.height,
                windowShowcaseBackground);
        if (windowStage && !backgroundResolved)
        {
            return E_INVALIDARG;
        }
        if (windowStage && !ComposeWindowCardShadow(
                flatWindowStage,
                outputDescription.width,
                outputDescription.height,
                windowCardShadow))
        {
            return E_INVALIDARG;
        }
        if (windowStage && !ComposeWindowStageTransform(
                flatWindowStage,
                windowCardShadow,
                outputDescription.width,
                outputDescription.height,
                windowStagePresentationTransform,
                windowStageTransform))
        {
            return E_INVALIDARG;
        }

        constexpr std::array<float, 4> blackClearColor{
            0.0f, 0.0f, 0.0f, 1.0f
        };
        const float* const clearColor = windowStage &&
            windowShowcaseBackground.kind ==
                WindowShowcaseBackgroundKind::Solid
            ? windowShowcaseBackground.solidSrgb.data()
            : blackClearColor.data();
        context_->ClearRenderTargetView(
            outputCanvas_.RenderTargetView(),
            clearColor);

        const D3D11_VIEWPORT backgroundViewport{
            0.0f,
            0.0f,
            static_cast<float>(outputDescription.width),
            static_cast<float>(outputDescription.height),
            0.0f,
            1.0f
        };
        if (windowStage &&
            windowShowcaseBackground.kind ==
                WindowShowcaseBackgroundKind::StaticTexture)
        {
            if (selectedBackgroundView == nullptr)
            {
                return E_UNEXPECTED;
            }
            DrawFullscreenPass(
                outputCanvas_.RenderTargetView(),
                selectedBackgroundView.get(),
                backgroundViewport,
                windowShowcaseBackground.TextureTransforms());
        }

        auto outputViewport = backgroundViewport;
        if (windowStage)
        {
            outputViewport.TopLeftX = flatWindowStage.window.left;
            outputViewport.TopLeftY = flatWindowStage.window.top;
            outputViewport.Width = flatWindowStage.window.width;
            outputViewport.Height = flatWindowStage.window.height;
        }
        else
        {
            XbLetterboxRect contentViewport{};
            if (!CalculateLetterbox(
                    crop.captureWidth,
                    crop.captureHeight,
                    outputDescription.width,
                    outputDescription.height,
                    contentViewport))
            {
                return E_INVALIDARG;
            }
            outputViewport.TopLeftX = contentViewport.x;
            outputViewport.TopLeftY = contentViewport.y;
            outputViewport.Width = contentViewport.width;
            outputViewport.Height = contentViewport.height;
        }
        const std::array<float, 8> compositionTransforms{
            camera.left,
            camera.top,
            camera.width,
            camera.height,
            windowStage ? flatWindowStage.sourceOriginU : crop.originU,
            windowStage ? flatWindowStage.sourceOriginV : crop.originV,
            windowStage ? flatWindowStage.sourceScaleU : crop.scaleU,
            windowStage ? flatWindowStage.sourceScaleV : crop.scaleV
        };
        if (windowStage && windowStageTransform.identity)
        {
            DrawWindowCardShadowPass(
                outputCanvas_.RenderTargetView(),
                windowCardShadow);
        }
        else if (windowStage)
        {
            const D3D11_VIEWPORT transformedViewport{
                0.0f,
                0.0f,
                static_cast<float>(outputDescription.width),
                static_cast<float>(outputDescription.height),
                0.0f,
                1.0f
            };
            DrawTransformedWindowCardShadowPass(
                outputCanvas_.RenderTargetView(),
                transformedViewport,
                windowCardShadow,
                windowStageTransform);
        }
        if (windowStage && windowStageTransform.identity)
        {
            DrawWindowCardContentPass(
                outputCanvas_.RenderTargetView(),
                sourceView_.get(),
                outputViewport,
                compositionTransforms,
                windowCardShadow);
        }
        else if (windowStage)
        {
            const D3D11_VIEWPORT transformedViewport{
                0.0f,
                0.0f,
                static_cast<float>(outputDescription.width),
                static_cast<float>(outputDescription.height),
                0.0f,
                1.0f
            };
            DrawTransformedWindowCardContentPass(
                outputCanvas_.RenderTargetView(),
                sourceView_.get(),
                transformedViewport,
                compositionTransforms,
                windowCardShadow,
                windowStageTransform);
        }
        else
        {
            DrawFullscreenPass(
                outputCanvas_.RenderTargetView(),
                sourceView_.get(),
                outputViewport,
                compositionTransforms);
        }

        if (cursorCommand != nullptr)
        {
            cursorResult = customCursorRenderer_.Draw(
                *cursorCommand,
                outputViewport);
        }

        // OutputCanvas is complete here, including the product cursor layer.
        // Unbind it before the independent GPU copy consumers read it.
        ID3D11RenderTargetView* nullConsumerTarget = nullptr;
        context_->OMSetRenderTargets(1, &nullConsumerTarget, nullptr);
        if (frameTap_.Enabled())
        {
            frameTap_.ObserveAndCopy(
                outputCanvas_.Texture(),
                outputDescription,
                frameTimestamp);
        }
        const auto gpuPreviewPublished = previewFrameExport_.Publish(
            device_.get(),
            context_.get(),
            outputCanvas_.Texture());
        if (!gpuPreviewPublished && !direct2dFailureReported_ &&
            startupDiagnostics_ != nullptr)
        {
            const auto direct2dResult = previewFrameExport_.LastResult();
            if (FAILED(direct2dResult))
            {
                direct2dFailureReported_ = true;
                (void)startupDiagnostics_->RunHresult(
                    StartupStepDescriptor{
                        "Direct2DPreview",
                        "RenderBuiltInScaleEffect",
                        "ID2D1DeviceContext::DrawImage/EndDraw",
                        __FILE__,
                        static_cast<std::uint32_t>(__LINE__) },
                    [direct2dResult]
                    {
                        return direct2dResult;
                    });
            }
        }

        if (!presentationEnabled)
        {
            return S_OK;
        }

        context_->ClearRenderTargetView(
            previewRenderTargetView_.get(),
            blackClearColor.data());
        XbLetterboxRect letterbox{};
        if (!CalculateLetterbox(
            outputDescription.width,
            outputDescription.height,
            previewWidth_,
            previewHeight_,
            letterbox))
        {
            return E_INVALIDARG;
        }
        const D3D11_VIEWPORT previewViewport{
            letterbox.x,
            letterbox.y,
            letterbox.width,
            letterbox.height,
            0.0f,
            1.0f
        };
        constexpr std::array<float, 8> previewIdentityTransforms{
            0.0f, 0.0f, 1.0f, 1.0f,
            0.0f, 0.0f, 1.0f, 1.0f
        };
        DrawFullscreenPass(
            previewRenderTargetView_.get(),
            outputCanvas_.ShaderResourceView(),
            previewViewport,
            previewIdentityTransforms);

        const auto result = swapChain_->Present(1, 0);
        if (result == DXGI_STATUS_OCCLUDED)
        {
            occluded = true;
            return S_OK;
        }

        return result;
    }

    void PreviewRenderer::Shutdown() noexcept
    {
        std::lock_guard recordingLock(recordingMutex_);
        videoEncoder_.StopAndJoin();
        frameTap_.Shutdown();
        customCursorRenderer_.Shutdown();
        if (context_)
        {
            context_->ClearState();
            context_->Flush();
        }

        previewFrameExport_.Shutdown();

        rasterizer_ = nullptr;
        windowCardShadowBlendState_ = nullptr;
        sampler_ = nullptr;
        windowCardContentPixelShader_ = nullptr;
        windowCardShadowPixelShader_ = nullptr;
        windowStageShadowPixelShader_ = nullptr;
        pixelShader_ = nullptr;
        windowCardContentConstantBuffer_ = nullptr;
        windowCardShadowConstantBuffer_ = nullptr;
        windowStageQuadConstantBuffer_ = nullptr;
        windowStageShadowConstantBuffer_ = nullptr;
        cameraConstantBuffer_ = nullptr;
        windowStageQuadVertexShader_ = nullptr;
        vertexShader_ = nullptr;
        outputCanvas_.Shutdown();
        sourceView_ = nullptr;
        sourceTexture_ = nullptr;
        windowShowcaseArtView_ = nullptr;
        windowShowcaseArtTexture_ = nullptr;
        previewRenderTargetView_ = nullptr;
        swapChain_ = nullptr;
        winRtDevice_ = nullptr;
        context_ = nullptr;
        device_ = nullptr;
        adapterName_.clear();
        sourceWidth_ = 0;
        sourceHeight_ = 0;
        sourceFormat_ = DXGI_FORMAT_UNKNOWN;
        previewHwnd_ = nullptr;
        videoDeviceStatus_ = {};
        windowShowcaseBackgroundPreset_ =
            WindowShowcaseBackgroundPreset::Warm;
        windowShowcaseCustomBackground_ = false;
        windowShowcaseBackgroundWidth_ = WindowShowcaseArtPixelWidth;
        windowShowcaseBackgroundHeight_ = WindowShowcaseArtPixelHeight;
        windowStageTransform_ = WindowStageIdentityTransform;
        windowStagePunchCandidate_ = WindowStagePunchCandidate::Disabled;
        windowShowcaseMotionEnabled_ = false;
        windowShowcaseMotionStarted_ = false;
        if (windowShowcaseMotionEnterEvent_ != nullptr)
        {
            CloseHandle(windowShowcaseMotionEnterEvent_);
            windowShowcaseMotionEnterEvent_ = nullptr;
        }
        if (windowShowcaseMotionReturnEvent_ != nullptr)
        {
            CloseHandle(windowShowcaseMotionReturnEvent_);
            windowShowcaseMotionReturnEvent_ = nullptr;
        }
        windowShowcaseMotionPreset_ = WindowShowcaseMotionPreset::A;
        windowShowcaseMotionTarget_ = WindowStageIdentityTransform;
        windowShowcaseMotionController_.Reset();
        windowShowcaseMotionStart_ = {};
    }

    XbPreviewResult PreviewRenderer::StartRecording(
        const VideoEncoderConfiguration& configuration)
    {
        std::lock_guard lock(recordingMutex_);
        return videoEncoder_.Start(
            frameTap_,
            device_.get(),
            context_.get(),
            configuration,
            videoDeviceStatus_);
    }

    XbPreviewResult PreviewRenderer::PauseRecording()
    {
        std::lock_guard lock(recordingMutex_);
        return videoEncoder_.RequestVideoPause();
    }

    XbPreviewResult PreviewRenderer::ResumeRecording()
    {
        std::lock_guard lock(recordingMutex_);
        return videoEncoder_.RequestVideoResume();
    }

    XbPreviewResult PreviewRenderer::StopRecording()
    {
        std::lock_guard lock(recordingMutex_);
        return videoEncoder_.StopAndJoin(
            RecordingTerminationDisposition::Publish);
    }

    XbPreviewResult PreviewRenderer::CancelRecording()
    {
        std::lock_guard lock(recordingMutex_);
        return videoEncoder_.StopAndJoin(
            RecordingTerminationDisposition::UserCancelled);
    }

    void PreviewRenderer::GetRecordingSnapshot(
        XbRecordingSnapshot& snapshot) const
    {
        videoEncoder_.GetSnapshot(snapshot);
    }

    XbPreviewResult PreviewRenderer::SetAudioControls(
        const XbAudioControlsV1& controls) noexcept
    {
        return videoEncoder_.SetAudioControls(controls);
    }

    void PreviewRenderer::GetAudioControlSnapshot(
        XbAudioControlSnapshotV1& snapshot) const noexcept
    {
        videoEncoder_.GetAudioControlSnapshot(snapshot);
    }

    bool PreviewRenderer::GetGpuExportFrame(
        XbPreviewGpuExportFrameV1& snapshot) const noexcept
    {
        return previewFrameExport_.GetSnapshot(snapshot);
    }

    void PreviewRenderer::RecordRecordingFailure(
        const XbPreviewResult result,
        const HRESULT hresult,
        const wchar_t* const message)
    {
        std::lock_guard lock(recordingMutex_);
        videoEncoder_.RecordExternalFailure(result, hresult, message);
    }

    HRESULT PreviewRenderer::DeviceRemovedReason() const noexcept
    {
        return device_ ? device_->GetDeviceRemovedReason() : S_OK;
    }

    void PreviewRenderer::CreateDevice(
        const HMONITOR monitor,
        const bool allowWarp,
        const bool requestVideoSupport)
    {
        auto& startupDiagnostics = *startupDiagnostics_;
        winrt::com_ptr<IDXGIOutput> matchingOutput;
        auto selectedAdapter = FindAdapterForMonitor(
            monitor,
            matchingOutput,
            startupDiagnostics);
        XB_STARTUP_STEP(
            startupDiagnostics,
            "DxgiDiscovery",
            "DetectOutputHdr",
            "PreviewRenderer::DetectHdr",
            [&]
            {
                DetectHdr(matchingOutput.get());
            });

        constexpr std::array featureLevels{
            D3D_FEATURE_LEVEL_11_1,
            D3D_FEATURE_LEVEL_11_0
        };
        D3D_FEATURE_LEVEL selectedFeatureLevel{};
        videoDeviceStatus_ = {};
        videoDeviceStatus_.videoSupportRequested = requestVideoSupport;

        const auto createDevice = [&](const UINT flags, const bool warpFallback)
        {
            device_ = nullptr;
            context_ = nullptr;
            usedWarp_ = false;
            auto result = D3D11CreateDevice(
                selectedAdapter.get(),
                selectedAdapter ? D3D_DRIVER_TYPE_UNKNOWN : D3D_DRIVER_TYPE_HARDWARE,
                nullptr,
                flags,
                featureLevels.data(),
                static_cast<UINT>(featureLevels.size()),
                D3D11_SDK_VERSION,
                device_.put(),
                &selectedFeatureLevel,
                context_.put());
            if (FAILED(result) && warpFallback)
            {
                device_ = nullptr;
                context_ = nullptr;
                result = D3D11CreateDevice(
                    nullptr,
                    D3D_DRIVER_TYPE_WARP,
                    nullptr,
                    flags,
                    featureLevels.data(),
                    static_cast<UINT>(featureLevels.size()),
                    D3D11_SDK_VERSION,
                    device_.put(),
                    &selectedFeatureLevel,
                    context_.put());
                usedWarp_ = SUCCEEDED(result);
            }
            return result;
        };

        HRESULT result = E_FAIL;
        if (requestVideoSupport)
        {
            const auto videoFlags = static_cast<UINT>(
                D3D11_CREATE_DEVICE_BGRA_SUPPORT |
                D3D11_CREATE_DEVICE_VIDEO_SUPPORT);
            result = XB_STARTUP_HRESULT_STEP(
                startupDiagnostics,
                "D3dDevice",
                "CreateVideoSupportDevice",
                "D3D11CreateDevice",
                std::optional<std::uint32_t>{ videoFlags },
                std::optional<std::uint32_t>{ 1u },
                std::nullopt,
                [&]
                {
                    return createDevice(videoFlags, false);
                });
            videoDeviceStatus_.videoDeviceCreationResult = result;
            if (SUCCEEDED(result))
            {
                videoDeviceStatus_.videoSupportDeviceCreated = true;
                try
                {
                    auto multithread = XB_STARTUP_STEP(
                        startupDiagnostics,
                        "D3dDevice",
                        "QueryMultithreadProtection",
                        "QueryInterface<ID3D10Multithread>",
                        [&]
                        {
                            return device_.as<ID3D10Multithread>();
                        });
                    videoDeviceStatus_.multithreadProtectionAvailable = true;
                    XB_STARTUP_STEP(
                        startupDiagnostics,
                        "D3dDevice",
                        "EnableMultithreadProtection",
                        "ID3D10Multithread::SetMultithreadProtected",
                        [&]
                        {
                            multithread->SetMultithreadProtected(TRUE);
                        });
                    videoDeviceStatus_.multithreadProtectionEnabled =
                        multithread->GetMultithreadProtected() == TRUE;
                }
                catch (...)
                {
                    videoDeviceStatus_.multithreadProtectionAvailable = false;
                    videoDeviceStatus_.multithreadProtectionEnabled = false;
                    videoDeviceStatus_.videoDeviceCreationResult = E_NOINTERFACE;
                }
                if (!videoDeviceStatus_.multithreadProtectionEnabled &&
                    SUCCEEDED(videoDeviceStatus_.videoDeviceCreationResult))
                {
                    videoDeviceStatus_.videoDeviceCreationResult = E_FAIL;
                }
            }
            if (FAILED(result) ||
                !videoDeviceStatus_.multithreadProtectionEnabled)
            {
                device_ = nullptr;
                context_ = nullptr;
                videoDeviceStatus_.videoSupportDeviceCreated = false;
                const auto bgraFlags = static_cast<UINT>(
                    D3D11_CREATE_DEVICE_BGRA_SUPPORT);
                StartupStepDescriptor fallbackStep{
                    "D3dDevice",
                    "FallbackToBgraDevice",
                    "D3D11CreateDevice",
                    __FILE__,
                    static_cast<std::uint32_t>(__LINE__),
                    bgraFlags,
                    2u,
                    "D3D11_CREATE_DEVICE_BGRA_SUPPORT|"
                    "D3D11_CREATE_DEVICE_VIDEO_SUPPORT"
                };
                startupDiagnostics.FallbackBegin(fallbackStep);
                result = XB_STARTUP_HRESULT_STEP(
                    startupDiagnostics,
                    "D3dDevice",
                    "CreateBgraFallbackDevice",
                    "D3D11CreateDevice",
                    std::optional<std::uint32_t>{ bgraFlags },
                    std::optional<std::uint32_t>{ 2u },
                    std::optional<std::string>{
                        "D3D11_CREATE_DEVICE_BGRA_SUPPORT|"
                        "D3D11_CREATE_DEVICE_VIDEO_SUPPORT" },
                    [&]
                    {
                        return createDevice(bgraFlags, allowWarp);
                    });
                if (SUCCEEDED(result))
                {
                    startupDiagnostics.FallbackSuccess(
                        fallbackStep,
                        result);
                }
                else
                {
                    startupDiagnostics.FallbackFailure(
                        fallbackStep,
                        result);
                }
            }
        }
        else
        {
            const auto bgraFlags = static_cast<UINT>(
                D3D11_CREATE_DEVICE_BGRA_SUPPORT);
            result = XB_STARTUP_HRESULT_STEP(
                startupDiagnostics,
                "D3dDevice",
                "CreateBgraPreviewDevice",
                "D3D11CreateDevice",
                std::optional<std::uint32_t>{ bgraFlags },
                std::optional<std::uint32_t>{ 1u },
                std::nullopt,
                [&]
                {
                    return createDevice(bgraFlags, allowWarp);
                });
        }

        winrt::check_hresult(result);
        XB_STARTUP_STEP(
            startupDiagnostics,
            "D3dDevice",
            "ValidateFeatureLevelAndImmediateContext",
            "D3D11CreateDevice outputs",
            [&]
            {
                if (selectedFeatureLevel < D3D_FEATURE_LEVEL_11_0 ||
                    !device_ || !context_)
                {
            throw winrt::hresult_error(E_NOTIMPL, L"P0 需要 D3D feature level 11.0。");
                }
            });

        auto dxgiDevice = XB_STARTUP_STEP(
            startupDiagnostics,
            "D3dInterop",
            "QueryDxgiDevice",
            "QueryInterface<IDXGIDevice>",
            [&]
            {
                return device_.as<IDXGIDevice>();
            });
        winrt::com_ptr<IInspectable> inspectableDevice;
        XB_STARTUP_STEP(
            startupDiagnostics,
            "D3dInterop",
            "CreateWinRtD3dDevice",
            "CreateDirect3D11DeviceFromDXGIDevice",
            [&]
            {
                winrt::check_hresult(
                    CreateDirect3D11DeviceFromDXGIDevice(
                        dxgiDevice.get(),
                        inspectableDevice.put()));
            });
        winRtDevice_ = XB_STARTUP_STEP(
            startupDiagnostics,
            "D3dInterop",
            "QueryWinRtD3dDevice",
            "QueryInterface<IDirect3DDevice>",
            [&]
            {
                return inspectableDevice.as<
                    winrt::Windows::Graphics::DirectX::Direct3D11::
                        IDirect3DDevice>();
            });

        winrt::com_ptr<IDXGIAdapter> actualAdapter;
        XB_STARTUP_STEP(
            startupDiagnostics,
            "DxgiDiscovery",
            "ReadActualDeviceAdapter",
            "IDXGIDevice::GetAdapter",
            [&]
            {
                winrt::check_hresult(
                    dxgiDevice->GetAdapter(actualAdapter.put()));
            });
        DXGI_ADAPTER_DESC description{};
        XB_STARTUP_STEP(
            startupDiagnostics,
            "DxgiDiscovery",
            "ReadAdapterDescription",
            "IDXGIAdapter::GetDesc",
            [&]
            {
                winrt::check_hresult(
                    actualAdapter->GetDesc(&description));
            });
        adapterName_ = description.Description;
        if (usedWarp_)
        {
            adapterName_ += L" [WARP fallback]";
        }
    }

    void PreviewRenderer::CreateSwapChain(
        const HWND previewHwnd,
        const std::uint32_t width,
        const std::uint32_t height)
    {
        auto& startupDiagnostics = *startupDiagnostics_;
        auto dxgiDevice = XB_STARTUP_STEP(
            startupDiagnostics,
            "SwapChain",
            "QueryDxgiDeviceForSwapChain",
            "QueryInterface<IDXGIDevice>",
            [&]
            {
                return device_.as<IDXGIDevice>();
            });
        winrt::com_ptr<IDXGIAdapter> adapter;
        XB_STARTUP_STEP(
            startupDiagnostics,
            "SwapChain",
            "GetSwapChainAdapter",
            "IDXGIDevice::GetAdapter",
            [&]
            {
                winrt::check_hresult(
                    dxgiDevice->GetAdapter(adapter.put()));
            });
        winrt::com_ptr<IDXGIFactory2> factory;
        XB_STARTUP_STEP(
            startupDiagnostics,
            "SwapChain",
            "GetSwapChainFactory",
            "IDXGIAdapter::GetParent",
            [&]
            {
                winrt::check_hresult(
                    adapter->GetParent(IID_PPV_ARGS(factory.put())));
            });

        DXGI_SWAP_CHAIN_DESC1 description{};
        description.Width = width;
        description.Height = height;
        description.Format = DXGI_FORMAT_B8G8R8A8_UNORM;
        description.Stereo = FALSE;
        description.SampleDesc.Count = 1;
        description.BufferUsage = DXGI_USAGE_RENDER_TARGET_OUTPUT;
        description.BufferCount = 2;
        description.Scaling = DXGI_SCALING_STRETCH;
        description.SwapEffect = DXGI_SWAP_EFFECT_FLIP_DISCARD;
        description.AlphaMode = DXGI_ALPHA_MODE_IGNORE;
        description.Flags = 0;

        XB_STARTUP_STEP(
            startupDiagnostics,
            "SwapChain",
            "CreateSwapChainForPreviewHwnd",
            "IDXGIFactory2::CreateSwapChainForHwnd",
            [&]
            {
                winrt::check_hresult(factory->CreateSwapChainForHwnd(
                    device_.get(),
                    previewHwnd,
                    &description,
                    nullptr,
                    nullptr,
                    swapChain_.put()));
            });
        XB_STARTUP_STEP(
            startupDiagnostics,
            "SwapChain",
            "DisableAltEnter",
            "IDXGIFactory::MakeWindowAssociation",
            [&]
            {
                static_cast<void>(factory->MakeWindowAssociation(
                    previewHwnd,
                    DXGI_MWA_NO_ALT_ENTER));
            });
        XB_STARTUP_STEP(
            startupDiagnostics,
            "SwapChain",
            "CreateSwapChainBackBufferResources",
            "PreviewRenderer::CreateBackBuffer",
            [&]
            {
                CreateBackBuffer();
            });
    }

    void PreviewRenderer::CreateBackBuffer()
    {
        if (!startupInstrumentationActive_)
        {
            winrt::com_ptr<ID3D11Texture2D> backBuffer;
            winrt::check_hresult(
                swapChain_->GetBuffer(
                    0,
                    IID_PPV_ARGS(backBuffer.put())));
            winrt::check_hresult(device_->CreateRenderTargetView(
                backBuffer.get(),
                nullptr,
                previewRenderTargetView_.put()));
            return;
        }
        auto& startupDiagnostics = *startupDiagnostics_;
        winrt::com_ptr<ID3D11Texture2D> backBuffer;
        XB_STARTUP_STEP(
            startupDiagnostics,
            "SwapChain",
            "GetSwapChainBackBuffer",
            "IDXGISwapChain::GetBuffer",
            [&]
            {
                winrt::check_hresult(
                    swapChain_->GetBuffer(
                        0,
                        IID_PPV_ARGS(backBuffer.put())));
            });
        XB_STARTUP_STEP(
            startupDiagnostics,
            "SwapChain",
            "CreatePreviewRenderTargetView",
            "ID3D11Device::CreateRenderTargetView",
            [&]
            {
                winrt::check_hresult(device_->CreateRenderTargetView(
                    backBuffer.get(),
                    nullptr,
                    previewRenderTargetView_.put()));
            });
    }

    void PreviewRenderer::CreateShaders()
    {
        auto& startupDiagnostics = *startupDiagnostics_;
        const auto vertexByteCode =
            CompileShader(ShaderSource, "VSMain", "vs_5_0");
        const auto pixelByteCode =
            CompileShader(ShaderSource, "PSMain", "ps_5_0");
        const auto windowCardContentPixelByteCode = CompileShader(
            WindowCardContentPixelShaderSource,
            "PSWindowCardContent",
            "ps_5_0");
        const auto windowCardShadowPixelByteCode = CompileShader(
            WindowCardShadowPixelShaderSource,
            "PSWindowCardShadow",
            "ps_5_0");
        const auto windowStageQuadVertexByteCode = CompileShader(
            WindowStageQuadVertexShaderSource,
            "VSWindowStageQuad",
            "vs_5_0");
        const auto windowStageShadowPixelByteCode = CompileShader(
            WindowStageTransformedShadowPixelShaderSource,
            "PSWindowStageTransformedShadow",
            "ps_5_0");

        XB_STARTUP_STEP(
            startupDiagnostics,
            "Shaders",
            "CreateVertexShader",
            "ID3D11Device::CreateVertexShader",
            [&]
            {
                winrt::check_hresult(device_->CreateVertexShader(
                    vertexByteCode->GetBufferPointer(),
                    vertexByteCode->GetBufferSize(),
                    nullptr,
                    vertexShader_.put()));
            });
        XB_STARTUP_STEP(
            startupDiagnostics,
            "Shaders",
            "CreatePixelShader",
            "ID3D11Device::CreatePixelShader",
            [&]
            {
                winrt::check_hresult(device_->CreatePixelShader(
                    pixelByteCode->GetBufferPointer(),
                    pixelByteCode->GetBufferSize(),
                    nullptr,
                    pixelShader_.put()));
            });
        XB_STARTUP_STEP(
            startupDiagnostics,
            "Shaders",
            "CreateWindowStageQuadVertexShader",
            "ID3D11Device::CreateVertexShader",
            [&]
            {
                winrt::check_hresult(device_->CreateVertexShader(
                    windowStageQuadVertexByteCode->GetBufferPointer(),
                    windowStageQuadVertexByteCode->GetBufferSize(),
                    nullptr,
                    windowStageQuadVertexShader_.put()));
            });
        XB_STARTUP_STEP(
            startupDiagnostics,
            "Shaders",
            "CreateWindowCardContentPixelShader",
            "ID3D11Device::CreatePixelShader",
            [&]
            {
                winrt::check_hresult(device_->CreatePixelShader(
                    windowCardContentPixelByteCode->GetBufferPointer(),
                    windowCardContentPixelByteCode->GetBufferSize(),
                    nullptr,
                    windowCardContentPixelShader_.put()));
            });
        XB_STARTUP_STEP(
            startupDiagnostics,
            "Shaders",
            "CreateWindowStageShadowPixelShader",
            "ID3D11Device::CreatePixelShader",
            [&]
            {
                winrt::check_hresult(device_->CreatePixelShader(
                    windowStageShadowPixelByteCode->GetBufferPointer(),
                    windowStageShadowPixelByteCode->GetBufferSize(),
                    nullptr,
                    windowStageShadowPixelShader_.put()));
            });
        XB_STARTUP_STEP(
            startupDiagnostics,
            "Shaders",
            "CreateWindowCardShadowPixelShader",
            "ID3D11Device::CreatePixelShader",
            [&]
            {
                winrt::check_hresult(device_->CreatePixelShader(
                    windowCardShadowPixelByteCode->GetBufferPointer(),
                    windowCardShadowPixelByteCode->GetBufferSize(),
                    nullptr,
                    windowCardShadowPixelShader_.put()));
            });

        D3D11_BUFFER_DESC cameraBufferDescription{};
        cameraBufferDescription.ByteWidth = 32;
        cameraBufferDescription.Usage = D3D11_USAGE_DEFAULT;
        cameraBufferDescription.BindFlags = D3D11_BIND_CONSTANT_BUFFER;
        XB_STARTUP_STEP(
            startupDiagnostics,
            "Shaders",
            "CreateCameraConstantBuffer",
            "ID3D11Device::CreateBuffer",
            [&]
            {
                winrt::check_hresult(device_->CreateBuffer(
                    &cameraBufferDescription,
                    nullptr,
                    cameraConstantBuffer_.put()));
            });

        D3D11_BUFFER_DESC windowCardShadowBufferDescription{};
        windowCardShadowBufferDescription.ByteWidth =
            sizeof(WindowCardShadowShaderConstants);
        windowCardShadowBufferDescription.Usage = D3D11_USAGE_DEFAULT;
        windowCardShadowBufferDescription.BindFlags =
            D3D11_BIND_CONSTANT_BUFFER;
        XB_STARTUP_STEP(
            startupDiagnostics,
            "Shaders",
            "CreateWindowCardContentConstantBuffer",
            "ID3D11Device::CreateBuffer",
            [&]
            {
                D3D11_BUFFER_DESC description{};
                description.ByteWidth = sizeof(WindowCardContentShaderConstants);
                description.Usage = D3D11_USAGE_DEFAULT;
                description.BindFlags = D3D11_BIND_CONSTANT_BUFFER;
                winrt::check_hresult(device_->CreateBuffer(
                    &description,
                    nullptr,
                    windowCardContentConstantBuffer_.put()));
            });
        XB_STARTUP_STEP(
            startupDiagnostics,
            "Shaders",
            "CreateWindowCardShadowConstantBuffer",
            "ID3D11Device::CreateBuffer",
            [&]
            {
                winrt::check_hresult(device_->CreateBuffer(
                    &windowCardShadowBufferDescription,
                    nullptr,
                    windowCardShadowConstantBuffer_.put()));
            });
        XB_STARTUP_STEP(
            startupDiagnostics,
            "Shaders",
            "CreateWindowStageQuadConstantBuffer",
            "ID3D11Device::CreateBuffer",
            [&]
            {
                D3D11_BUFFER_DESC description{};
                description.ByteWidth = sizeof(WindowStageQuadShaderConstants);
                description.Usage = D3D11_USAGE_DEFAULT;
                description.BindFlags = D3D11_BIND_CONSTANT_BUFFER;
                winrt::check_hresult(device_->CreateBuffer(
                    &description,
                    nullptr,
                    windowStageQuadConstantBuffer_.put()));
            });
        XB_STARTUP_STEP(
            startupDiagnostics,
            "Shaders",
            "CreateWindowStageShadowConstantBuffer",
            "ID3D11Device::CreateBuffer",
            [&]
            {
                D3D11_BUFFER_DESC description{};
                description.ByteWidth =
                    sizeof(WindowStageTransformedShadowShaderConstants);
                description.Usage = D3D11_USAGE_DEFAULT;
                description.BindFlags = D3D11_BIND_CONSTANT_BUFFER;
                winrt::check_hresult(device_->CreateBuffer(
                    &description,
                    nullptr,
                    windowStageShadowConstantBuffer_.put()));
            });

        D3D11_SAMPLER_DESC samplerDescription{};
        samplerDescription.Filter = D3D11_FILTER_MIN_MAG_MIP_LINEAR;
        samplerDescription.AddressU = D3D11_TEXTURE_ADDRESS_CLAMP;
        samplerDescription.AddressV = D3D11_TEXTURE_ADDRESS_CLAMP;
        samplerDescription.AddressW = D3D11_TEXTURE_ADDRESS_CLAMP;
        samplerDescription.ComparisonFunc = D3D11_COMPARISON_NEVER;
        samplerDescription.MinLOD = 0.0f;
        samplerDescription.MaxLOD = D3D11_FLOAT32_MAX;
        XB_STARTUP_STEP(
            startupDiagnostics,
            "Shaders",
            "CreateLinearSamplerState",
            "ID3D11Device::CreateSamplerState",
            [&]
            {
                winrt::check_hresult(device_->CreateSamplerState(
                    &samplerDescription,
                    sampler_.put()));
            });

        D3D11_RASTERIZER_DESC rasterizerDescription{};
        rasterizerDescription.FillMode = D3D11_FILL_SOLID;
        rasterizerDescription.CullMode = D3D11_CULL_NONE;
        rasterizerDescription.DepthClipEnable = TRUE;
        XB_STARTUP_STEP(
            startupDiagnostics,
            "Shaders",
            "CreateRasterizerState",
            "ID3D11Device::CreateRasterizerState",
            [&]
            {
                winrt::check_hresult(device_->CreateRasterizerState(
                    &rasterizerDescription,
                    rasterizer_.put()));
            });

        D3D11_BLEND_DESC windowCardShadowBlendDescription{};
        auto& shadowBlend =
            windowCardShadowBlendDescription.RenderTarget[0];
        shadowBlend.BlendEnable = TRUE;
        shadowBlend.SrcBlend = D3D11_BLEND_ONE;
        shadowBlend.DestBlend = D3D11_BLEND_INV_SRC_ALPHA;
        shadowBlend.BlendOp = D3D11_BLEND_OP_ADD;
        shadowBlend.SrcBlendAlpha = D3D11_BLEND_ONE;
        shadowBlend.DestBlendAlpha = D3D11_BLEND_INV_SRC_ALPHA;
        shadowBlend.BlendOpAlpha = D3D11_BLEND_OP_ADD;
        shadowBlend.RenderTargetWriteMask = D3D11_COLOR_WRITE_ENABLE_ALL;
        XB_STARTUP_STEP(
            startupDiagnostics,
            "Shaders",
            "CreateWindowCardShadowBlendState",
            "ID3D11Device::CreateBlendState",
            [&]
            {
                winrt::check_hresult(device_->CreateBlendState(
                    &windowCardShadowBlendDescription,
                    windowCardShadowBlendState_.put()));
            });

    }

    void PreviewRenderer::DrawFullscreenPass(
        ID3D11RenderTargetView* const target,
        ID3D11ShaderResourceView* const source,
        const D3D11_VIEWPORT& viewport,
        const std::array<float, 8>& transforms)
    {
        if (target == nullptr ||
            source == nullptr ||
            viewport.Width <= 0.0f ||
            viewport.Height <= 0.0f)
        {
            throw winrt::hresult_invalid_argument();
        }

        context_->UpdateSubresource(
            cameraConstantBuffer_.get(),
            0,
            nullptr,
            transforms.data(),
            0,
            0);

        ID3D11RenderTargetView* renderTarget = target;
        ID3D11ShaderResourceView* shaderResource = source;
        ID3D11SamplerState* sampler = sampler_.get();
        ID3D11Buffer* constantBuffer = cameraConstantBuffer_.get();
        constexpr std::array<float, 4> opaqueBlendFactor{ 0, 0, 0, 0 };

        context_->OMSetRenderTargets(1, &renderTarget, nullptr);
        context_->OMSetBlendState(
            nullptr,
            opaqueBlendFactor.data(),
            0xffffffffu);
        context_->RSSetViewports(1, &viewport);
        context_->RSSetState(rasterizer_.get());
        context_->IASetInputLayout(nullptr);
        context_->IASetPrimitiveTopology(D3D11_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
        context_->VSSetShader(vertexShader_.get(), nullptr, 0);
        context_->PSSetShader(pixelShader_.get(), nullptr, 0);
        context_->PSSetShaderResources(0, 1, &shaderResource);
        context_->PSSetSamplers(0, 1, &sampler);
        context_->PSSetConstantBuffers(0, 1, &constantBuffer);
        context_->Draw(3, 0);

        ID3D11ShaderResourceView* nullView = nullptr;
        context_->PSSetShaderResources(0, 1, &nullView);
    }

    void PreviewRenderer::DrawWindowCardContentPass(
        ID3D11RenderTargetView* const target,
        ID3D11ShaderResourceView* const source,
        const D3D11_VIEWPORT& viewport,
        const std::array<float, 8>& transforms,
        const WindowCardShadowComposition& card)
    {
        if (target == nullptr || source == nullptr ||
            viewport.Width <= 0.0f || viewport.Height <= 0.0f ||
            !std::isfinite(card.cornerRadiusPixels) ||
            card.cornerRadiusPixels <= 0.0f ||
            card.cornerRadiusPixels >
                (std::min)(viewport.Width, viewport.Height) * 0.5f)
        {
            throw winrt::hresult_invalid_argument();
        }

        const WindowCardContentShaderConstants constants{
            { transforms[0], transforms[1], transforms[2], transforms[3] },
            { transforms[4], transforms[5], transforms[6], transforms[7] },
            {
                viewport.Width,
                viewport.Height,
                card.cornerRadiusPixels,
                0.0f
            }
        };
        context_->UpdateSubresource(
            windowCardContentConstantBuffer_.get(),
            0,
            nullptr,
            &constants,
            0,
            0);

        ID3D11RenderTargetView* renderTarget = target;
        ID3D11ShaderResourceView* shaderResource = source;
        ID3D11SamplerState* sampler = sampler_.get();
        ID3D11Buffer* constantBuffer =
            windowCardContentConstantBuffer_.get();
        constexpr std::array<float, 4> blendFactor{ 0, 0, 0, 0 };

        context_->OMSetRenderTargets(1, &renderTarget, nullptr);
        context_->OMSetBlendState(
            windowCardShadowBlendState_.get(),
            blendFactor.data(),
            0xffffffffu);
        context_->RSSetViewports(1, &viewport);
        context_->RSSetState(rasterizer_.get());
        context_->IASetInputLayout(nullptr);
        context_->IASetPrimitiveTopology(D3D11_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
        context_->VSSetShader(vertexShader_.get(), nullptr, 0);
        context_->PSSetShader(windowCardContentPixelShader_.get(), nullptr, 0);
        context_->PSSetShaderResources(0, 1, &shaderResource);
        context_->PSSetSamplers(0, 1, &sampler);
        context_->PSSetConstantBuffers(0, 1, &constantBuffer);
        context_->Draw(3, 0);

        ID3D11ShaderResourceView* nullView = nullptr;
        context_->PSSetShaderResources(0, 1, &nullView);
    }

    void PreviewRenderer::DrawWindowCardShadowPass(
        ID3D11RenderTargetView* const target,
        const WindowCardShadowComposition& shadow)
    {
        if (target == nullptr ||
            !shadow.card.IsValid() || !shadow.support.IsValid() ||
            !std::isfinite(shadow.opacity) ||
            !std::isfinite(shadow.verticalOffsetPixels) ||
            !std::isfinite(shadow.softnessPixels) ||
            !std::isfinite(shadow.cornerRadiusPixels) ||
            shadow.opacity <= 0.0f || shadow.opacity > 1.0f ||
            shadow.verticalOffsetPixels < 0.0f ||
            shadow.softnessPixels <= 0.0f ||
            shadow.cornerRadiusPixels <= 0.0f)
        {
            throw winrt::hresult_invalid_argument();
        }

        const WindowCardShadowShaderConstants constants{
            {
                shadow.card.left,
                shadow.card.top,
                shadow.card.width,
                shadow.card.height
            },
            {
                shadow.opacity,
                shadow.verticalOffsetPixels,
                shadow.softnessPixels,
                shadow.cornerRadiusPixels
            }
        };
        context_->UpdateSubresource(
            windowCardShadowConstantBuffer_.get(),
            0,
            nullptr,
            &constants,
            0,
            0);

        const D3D11_VIEWPORT viewport{
            shadow.support.left,
            shadow.support.top,
            shadow.support.width,
            shadow.support.height,
            0.0f,
            1.0f
        };
        ID3D11RenderTargetView* renderTarget = target;
        ID3D11Buffer* constantBuffer =
            windowCardShadowConstantBuffer_.get();
        constexpr std::array<float, 4> blendFactor{ 0, 0, 0, 0 };
        ID3D11ShaderResourceView* nullView = nullptr;

        context_->OMSetRenderTargets(1, &renderTarget, nullptr);
        context_->OMSetBlendState(
            windowCardShadowBlendState_.get(),
            blendFactor.data(),
            0xffffffffu);
        context_->RSSetViewports(1, &viewport);
        context_->RSSetState(rasterizer_.get());
        context_->IASetInputLayout(nullptr);
        context_->IASetPrimitiveTopology(D3D11_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
        context_->VSSetShader(vertexShader_.get(), nullptr, 0);
        context_->PSSetShader(windowCardShadowPixelShader_.get(), nullptr, 0);
        context_->PSSetShaderResources(0, 1, &nullView);
        context_->PSSetConstantBuffers(0, 1, &constantBuffer);
        context_->Draw(3, 0);
    }

    void PreviewRenderer::DrawTransformedWindowCardContentPass(
        ID3D11RenderTargetView* const target,
        ID3D11ShaderResourceView* const source,
        const D3D11_VIEWPORT& viewport,
        const std::array<float, 8>& transforms,
        const WindowCardShadowComposition& card,
        const WindowStageTransformComposition& stageTransform)
    {
        if (target == nullptr || source == nullptr ||
            viewport.Width <= 0.0f || viewport.Height <= 0.0f ||
            !card.card.IsValid() || !stageTransform.valid ||
            stageTransform.identity || !stageTransform.contentQuad.IsValid() ||
            !std::isfinite(card.cornerRadiusPixels) ||
            card.cornerRadiusPixels <= 0.0f ||
            card.cornerRadiusPixels >
                (std::min)(card.card.width, card.card.height) * 0.5f)
        {
            throw winrt::hresult_invalid_argument();
        }

        const WindowCardContentShaderConstants contentConstants{
            { transforms[0], transforms[1], transforms[2], transforms[3] },
            { transforms[4], transforms[5], transforms[6], transforms[7] },
            {
                card.card.width,
                card.card.height,
                card.cornerRadiusPixels,
                0.0f
            }
        };
        WindowStageQuadShaderConstants quadConstants{};
        for (std::size_t index = 0;
            index < stageTransform.contentQuad.corners.size(); ++index)
        {
            const auto& corner = stageTransform.contentQuad.corners[index];
            quadConstants.clipCorners[index] = {
                corner.x, corner.y, corner.z, corner.w
            };
        }
        context_->UpdateSubresource(
            windowCardContentConstantBuffer_.get(),
            0,
            nullptr,
            &contentConstants,
            0,
            0);
        context_->UpdateSubresource(
            windowStageQuadConstantBuffer_.get(),
            0,
            nullptr,
            &quadConstants,
            0,
            0);

        ID3D11RenderTargetView* renderTarget = target;
        ID3D11ShaderResourceView* shaderResource = source;
        ID3D11SamplerState* sampler = sampler_.get();
        ID3D11Buffer* vertexConstantBuffer =
            windowStageQuadConstantBuffer_.get();
        ID3D11Buffer* pixelConstantBuffer =
            windowCardContentConstantBuffer_.get();
        constexpr std::array<float, 4> blendFactor{ 0, 0, 0, 0 };

        context_->OMSetRenderTargets(1, &renderTarget, nullptr);
        context_->OMSetBlendState(
            windowCardShadowBlendState_.get(),
            blendFactor.data(),
            0xffffffffu);
        context_->RSSetViewports(1, &viewport);
        context_->RSSetState(rasterizer_.get());
        context_->IASetInputLayout(nullptr);
        context_->IASetPrimitiveTopology(D3D11_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
        context_->VSSetShader(windowStageQuadVertexShader_.get(), nullptr, 0);
        context_->VSSetConstantBuffers(0, 1, &vertexConstantBuffer);
        context_->PSSetShader(windowCardContentPixelShader_.get(), nullptr, 0);
        context_->PSSetShaderResources(0, 1, &shaderResource);
        context_->PSSetSamplers(0, 1, &sampler);
        context_->PSSetConstantBuffers(0, 1, &pixelConstantBuffer);
        context_->Draw(6, 0);

        ID3D11ShaderResourceView* nullView = nullptr;
        context_->PSSetShaderResources(0, 1, &nullView);
    }

    void PreviewRenderer::DrawTransformedWindowCardShadowPass(
        ID3D11RenderTargetView* const target,
        const D3D11_VIEWPORT& viewport,
        const WindowCardShadowComposition& shadow,
        const WindowStageTransformComposition& stageTransform)
    {
        if (target == nullptr ||
            viewport.Width <= 0.0f || viewport.Height <= 0.0f ||
            !shadow.card.IsValid() || !shadow.support.IsValid() ||
            !stageTransform.valid || stageTransform.identity ||
            !stageTransform.shadowQuad.IsValid() ||
            !std::isfinite(shadow.opacity) ||
            !std::isfinite(shadow.verticalOffsetPixels) ||
            !std::isfinite(shadow.softnessPixels) ||
            !std::isfinite(shadow.cornerRadiusPixels) ||
            shadow.opacity <= 0.0f || shadow.opacity > 1.0f ||
            shadow.verticalOffsetPixels < 0.0f ||
            shadow.softnessPixels <= 0.0f ||
            shadow.cornerRadiusPixels <= 0.0f)
        {
            throw winrt::hresult_invalid_argument();
        }

        const auto halfWidth = shadow.card.width * 0.5f;
        const auto halfHeight = shadow.card.height * 0.5f;
        const WindowStageTransformedShadowShaderConstants shadowConstants{
            {
                -halfWidth - shadow.softnessPixels,
                -halfHeight + shadow.verticalOffsetPixels -
                    shadow.softnessPixels,
                shadow.card.width + 2.0f * shadow.softnessPixels,
                shadow.card.height + 2.0f * shadow.softnessPixels
            },
            {
                shadow.card.width,
                shadow.card.height,
                shadow.verticalOffsetPixels,
                shadow.cornerRadiusPixels
            },
            { shadow.opacity, shadow.softnessPixels, 0.0f, 0.0f }
        };
        WindowStageQuadShaderConstants quadConstants{};
        for (std::size_t index = 0;
            index < stageTransform.shadowQuad.corners.size(); ++index)
        {
            const auto& corner = stageTransform.shadowQuad.corners[index];
            quadConstants.clipCorners[index] = {
                corner.x, corner.y, corner.z, corner.w
            };
        }
        context_->UpdateSubresource(
            windowStageShadowConstantBuffer_.get(),
            0,
            nullptr,
            &shadowConstants,
            0,
            0);
        context_->UpdateSubresource(
            windowStageQuadConstantBuffer_.get(),
            0,
            nullptr,
            &quadConstants,
            0,
            0);

        ID3D11RenderTargetView* renderTarget = target;
        ID3D11Buffer* vertexConstantBuffer =
            windowStageQuadConstantBuffer_.get();
        ID3D11Buffer* pixelConstantBuffer =
            windowStageShadowConstantBuffer_.get();
        constexpr std::array<float, 4> blendFactor{ 0, 0, 0, 0 };
        ID3D11ShaderResourceView* nullView = nullptr;

        context_->OMSetRenderTargets(1, &renderTarget, nullptr);
        context_->OMSetBlendState(
            windowCardShadowBlendState_.get(),
            blendFactor.data(),
            0xffffffffu);
        context_->RSSetViewports(1, &viewport);
        context_->RSSetState(rasterizer_.get());
        context_->IASetInputLayout(nullptr);
        context_->IASetPrimitiveTopology(D3D11_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
        context_->VSSetShader(windowStageQuadVertexShader_.get(), nullptr, 0);
        context_->VSSetConstantBuffers(0, 1, &vertexConstantBuffer);
        context_->PSSetShader(windowStageShadowPixelShader_.get(), nullptr, 0);
        context_->PSSetShaderResources(0, 1, &nullView);
        context_->PSSetConstantBuffers(0, 1, &pixelConstantBuffer);
        context_->Draw(6, 0);
    }

    void PreviewRenderer::EnsureSourceTexture(const D3D11_TEXTURE2D_DESC& sourceDescription)
    {
        if (sourceTexture_ &&
            sourceWidth_ == sourceDescription.Width &&
            sourceHeight_ == sourceDescription.Height &&
            sourceFormat_ == sourceDescription.Format)
        {
            return;
        }

        sourceView_ = nullptr;
        sourceTexture_ = nullptr;

        D3D11_TEXTURE2D_DESC ownedDescription{};
        ownedDescription.Width = sourceDescription.Width;
        ownedDescription.Height = sourceDescription.Height;
        ownedDescription.MipLevels = 1;
        ownedDescription.ArraySize = 1;
        ownedDescription.Format = sourceDescription.Format;
        ownedDescription.SampleDesc.Count = 1;
        ownedDescription.Usage = D3D11_USAGE_DEFAULT;
        ownedDescription.BindFlags = D3D11_BIND_SHADER_RESOURCE;
        ownedDescription.CPUAccessFlags = 0;
        ownedDescription.MiscFlags = 0;

        winrt::check_hresult(device_->CreateTexture2D(
            &ownedDescription,
            nullptr,
            sourceTexture_.put()));
        winrt::check_hresult(device_->CreateShaderResourceView(
            sourceTexture_.get(),
            nullptr,
            sourceView_.put()));

        sourceWidth_ = sourceDescription.Width;
        sourceHeight_ = sourceDescription.Height;
        sourceFormat_ = sourceDescription.Format;
    }

    void PreviewRenderer::LoadWindowShowcaseArtBackground()
    {
        const auto path = PackagedWindowShowcaseArtPath(
            windowShowcaseBackgroundPreset_);
        LoadWindowShowcaseTexture(
            path,
            true,
            windowShowcaseArtTexture_,
            windowShowcaseArtView_,
            windowShowcaseBackgroundWidth_,
            windowShowcaseBackgroundHeight_);
    }

    void PreviewRenderer::LoadWindowShowcaseTexture(
        const std::wstring& path,
        const bool requireFrozenDimensions,
        winrt::com_ptr<ID3D11Texture2D>& texture,
        winrt::com_ptr<ID3D11ShaderResourceView>& view,
        std::uint32_t& decodedWidth,
        std::uint32_t& decodedHeight)
    {
        winrt::com_ptr<IWICImagingFactory> factory;
        winrt::check_hresult(CoCreateInstance(
            CLSID_WICImagingFactory,
            nullptr,
            CLSCTX_INPROC_SERVER,
            IID_PPV_ARGS(factory.put())));

        winrt::com_ptr<IWICBitmapDecoder> decoder;
        winrt::check_hresult(factory->CreateDecoderFromFilename(
            path.c_str(),
            nullptr,
            GENERIC_READ,
            WICDecodeMetadataCacheOnLoad,
            decoder.put()));
        winrt::com_ptr<IWICBitmapFrameDecode> frame;
        winrt::check_hresult(decoder->GetFrame(0, frame.put()));

        UINT width{};
        UINT height{};
        winrt::check_hresult(frame->GetSize(&width, &height));
        if (width == 0 || height == 0 || width > 16384 || height > 16384 ||
            (requireFrozenDimensions &&
                (width != WindowShowcaseArtPixelWidth ||
                    height != WindowShowcaseArtPixelHeight)))
        {
            throw winrt::hresult_invalid_argument(
                L"The static background dimensions are unsupported.");
        }

        winrt::com_ptr<IWICFormatConverter> converter;
        winrt::check_hresult(factory->CreateFormatConverter(converter.put()));
        winrt::check_hresult(converter->Initialize(
            frame.get(),
            GUID_WICPixelFormat32bppBGRA,
            WICBitmapDitherTypeNone,
            nullptr,
            0.0,
            WICBitmapPaletteTypeCustom));

        constexpr std::size_t bytesPerPixel = 4;
        const auto rowPitch = static_cast<std::size_t>(width) * bytesPerPixel;
        const auto byteCount = rowPitch * height;
        constexpr std::size_t maximumDecodedBytes = 256ull * 1024ull * 1024ull;
        if (rowPitch > (std::numeric_limits<UINT>::max)() ||
            byteCount > (std::numeric_limits<UINT>::max)() ||
            byteCount > maximumDecodedBytes)
        {
            throw winrt::hresult_invalid_argument(
                L"The static background is too large to decode safely.");
        }
        std::vector<BYTE> pixels(byteCount);
        winrt::check_hresult(converter->CopyPixels(
            nullptr,
            static_cast<UINT>(rowPitch),
            static_cast<UINT>(byteCount),
            pixels.data()));

        D3D11_TEXTURE2D_DESC textureDescription{};
        textureDescription.Width = width;
        textureDescription.Height = height;
        textureDescription.MipLevels = 1;
        textureDescription.ArraySize = 1;
        textureDescription.Format = DXGI_FORMAT_B8G8R8A8_UNORM;
        textureDescription.SampleDesc.Count = 1;
        textureDescription.Usage = D3D11_USAGE_IMMUTABLE;
        textureDescription.BindFlags = D3D11_BIND_SHADER_RESOURCE;
        const D3D11_SUBRESOURCE_DATA initialData{
            pixels.data(),
            static_cast<UINT>(rowPitch),
            0
        };
        winrt::check_hresult(device_->CreateTexture2D(
            &textureDescription,
            &initialData,
            texture.put()));
        winrt::check_hresult(device_->CreateShaderResourceView(
            texture.get(),
            nullptr,
            view.put()));
        decodedWidth = width;
        decodedHeight = height;
    }

    void PreviewRenderer::DetectHdr(IDXGIOutput* const output)
    {
        hdrDetected_ = false;
        if (output == nullptr)
        {
            return;
        }

        winrt::com_ptr<IDXGIOutput6> detected;
        if (SUCCEEDED(output->QueryInterface(IID_PPV_ARGS(detected.put()))))
        {
            DXGI_OUTPUT_DESC1 description{};
            if (SUCCEEDED(detected->GetDesc1(&description)))
            {
                hdrDetected_ = IsHdrColorSpace(description.ColorSpace);
            }
        }
    }

    winrt::com_ptr<IDXGIAdapter1> PreviewRenderer::FindAdapterForMonitor(
        const HMONITOR monitor,
        winrt::com_ptr<IDXGIOutput>& matchingOutput,
        StartupDiagnostics& startupDiagnostics)
    {
        winrt::com_ptr<IDXGIFactory1> factory;
        XB_STARTUP_STEP(
            startupDiagnostics,
            "DxgiDiscovery",
            "CreateDxgiFactory",
            "CreateDXGIFactory1",
            [&]
            {
                winrt::check_hresult(
                    CreateDXGIFactory1(IID_PPV_ARGS(factory.put())));
            });

        for (UINT adapterIndex = 0;; ++adapterIndex)
        {
            winrt::com_ptr<IDXGIAdapter1> adapter;
            const auto adapterResult = factory->EnumAdapters1(adapterIndex, adapter.put());
            if (adapterResult == DXGI_ERROR_NOT_FOUND)
            {
                break;
            }
            XB_STARTUP_STEP(
                startupDiagnostics,
                "DxgiDiscovery",
                "EnumerateAdapter",
                "IDXGIFactory1::EnumAdapters1",
                [&]
                {
                    winrt::check_hresult(adapterResult);
                });

            for (UINT outputIndex = 0;; ++outputIndex)
            {
                winrt::com_ptr<IDXGIOutput> output;
                const auto outputResult = adapter->EnumOutputs(outputIndex, output.put());
                if (outputResult == DXGI_ERROR_NOT_FOUND)
                {
                    break;
                }
                XB_STARTUP_STEP(
                    startupDiagnostics,
                    "DxgiDiscovery",
                    "EnumerateOutput",
                    "IDXGIAdapter::EnumOutputs",
                    [&]
                    {
                        winrt::check_hresult(outputResult);
                    });

                DXGI_OUTPUT_DESC outputDescription{};
                XB_STARTUP_STEP(
                    startupDiagnostics,
                    "DxgiDiscovery",
                    "ReadOutputDescription",
                    "IDXGIOutput::GetDesc",
                    [&]
                    {
                        winrt::check_hresult(
                            output->GetDesc(&outputDescription));
                    });
                if (outputDescription.Monitor == monitor)
                {
                    matchingOutput = output;
                    return adapter;
                }
            }
        }

        return nullptr;
    }

    winrt::com_ptr<ID3DBlob> PreviewRenderer::CompileShader(
        const char* const source,
        const char* const entryPoint,
        const char* const profile)
    {
        winrt::com_ptr<ID3DBlob> byteCode;
        winrt::com_ptr<ID3DBlob> errors;
        const auto compile = [&]
        {
            return D3DCompile(
                source,
                std::strlen(source),
                "XbPreview.P0",
                nullptr,
                nullptr,
                entryPoint,
                profile,
                D3DCOMPILE_ENABLE_STRICTNESS |
                    D3DCOMPILE_OPTIMIZATION_LEVEL3,
                0,
                byteCode.put(),
                errors.put());
        };
        const std::string operation = std::string("CompileShader:") + entryPoint;
        const auto result = startupDiagnostics_->RunHresult(
            StartupStepDescriptor{
                "Shaders",
                operation,
                "D3DCompile",
                __FILE__,
                static_cast<std::uint32_t>(__LINE__) },
            compile);
        if (FAILED(result))
        {
            std::wstring message = L"无法编译最小 P0 GPU shader。";
            if (errors && errors->GetBufferPointer())
            {
                const auto text = static_cast<const char*>(errors->GetBufferPointer());
                const auto length = static_cast<int>(errors->GetBufferSize());
                const auto required = MultiByteToWideChar(
                    CP_UTF8,
                    0,
                    text,
                    length,
                    nullptr,
                    0);
                if (required > 0)
                {
                    std::wstring details(static_cast<std::size_t>(required), L'\0');
                    MultiByteToWideChar(
                        CP_UTF8,
                        0,
                        text,
                        length,
                        details.data(),
                        required);
                    message += L" ";
                    message += details;
                }
            }
            throw winrt::hresult_error(result, message);
        }

        return byteCode;
    }
}
