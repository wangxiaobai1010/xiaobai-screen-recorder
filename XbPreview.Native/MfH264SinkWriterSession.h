#pragma once

#include "MfAacAudioStream.h"
#include "VideoEncoderDiagnostics.h"

#include <codecapi.h>
#include <d3d11.h>
#include <mfapi.h>
#include <mferror.h>
#include <mfidl.h>
#include <mfreadwrite.h>
#include <propsys.h>
#include <winrt/base.h>

#include <cstdint>
#include <string>
#include <vector>

namespace xbpreview
{
    inline constexpr std::uint32_t
        VideoEncoderQuickRuntimeValidationSampleLimit = 8;

    enum class H264EncoderStartupVariant : std::uint8_t
    {
        DiagnosticCbrConfigStore,
        DiagnosticNoConfigStore,
        DiagnosticMeanOnlyConfigStore,
        ProductionInputMediaTypeParametersCbr
    };

    struct H264EncoderStartupConfiguration final
    {
        std::uint32_t rateControlMode{};
        std::uint32_t meanBitrate{};
    };

    [[nodiscard]] constexpr H264EncoderStartupConfiguration
        CreateH264EncoderStartupConfiguration(
            const std::uint32_t bitrate) noexcept
    {
        return {
            static_cast<std::uint32_t>(
                eAVEncCommonRateControlMode_CBR),
            bitrate };
    }

    [[nodiscard]] inline PROPERTYKEY H264EncoderConfigPropertyKey(
        const GUID& property) noexcept
    {
        return { property, 0 };
    }

    [[nodiscard]] inline HRESULT SetH264EncoderConfigStoreUInt32(
        IPropertyStore* const store,
        const GUID& property,
        const std::uint32_t value) noexcept
    {
        if (store == nullptr)
        {
            return E_POINTER;
        }
        const auto key = H264EncoderConfigPropertyKey(property);
        PROPVARIANT variant{};
        PropVariantInit(&variant);
        variant.vt = VT_UI4;
        variant.ulVal = value;
        return store->SetValue(key, variant);
    }

    template <typename PropertySetter>
    [[nodiscard]] HRESULT ApplyH264EncoderStartupConfiguration(
        const H264EncoderStartupConfiguration& configuration,
        PropertySetter&& setProperty,
        HRESULT& rateControlPropertySetResult,
        HRESULT& meanBitratePropertySetResult)
    {
        rateControlPropertySetResult = setProperty(
            CODECAPI_AVEncCommonRateControlMode,
            configuration.rateControlMode);
        if (FAILED(rateControlPropertySetResult))
        {
            meanBitratePropertySetResult = E_PENDING;
            return rateControlPropertySetResult;
        }
        meanBitratePropertySetResult = setProperty(
            CODECAPI_AVEncCommonMeanBitRate,
            configuration.meanBitrate);
        return meanBitratePropertySetResult;
    }

    [[nodiscard]] inline HRESULT VerifyProductionHardwareEncoder(
        VideoEncoderDiagnostics& diagnostics) noexcept
    {
        diagnostics.productionHardwareEncoderRequired = true;
        const auto hardwareVerified =
            diagnostics.encoderCapabilities.actualTransformObtained &&
            diagnostics.encoderCapabilities.hardwareSoftwareVerdict ==
                "HARDWARE";
        diagnostics.actualHardwareEncoderVerified = hardwareVerified;
        diagnostics.softwareFallbackDetected =
            diagnostics.encoderCapabilities.hardwareSoftwareVerdict ==
                "SOFTWARE";
        diagnostics.softwareFallbackRejected =
            !hardwareVerified && diagnostics.softwareFallbackDetected;
        if (hardwareVerified)
        {
            diagnostics.hardwareEncoderVerificationHResult = S_OK;
            return S_OK;
        }

        const auto verificationResult = FAILED(
            diagnostics.encoderCapabilities.probeHResult)
            ? diagnostics.encoderCapabilities.probeHResult
            : MF_E_TOPO_CODEC_NOT_FOUND;
        diagnostics.hardwareEncoderVerificationHResult = verificationResult;
        diagnostics.failureStage = "VerifyHardwareVideoEncoder";
        diagnostics.failureHResult = verificationResult;
        return verificationResult;
    }

    class MfH264SinkWriterSession final
    {
    public:
        MfH264SinkWriterSession() = default;
        ~MfH264SinkWriterSession();
        MfH264SinkWriterSession(const MfH264SinkWriterSession&) = delete;
        MfH264SinkWriterSession& operator=(const MfH264SinkWriterSession&) = delete;

        [[nodiscard]] HRESULT Start(
            ID3D11Device* device,
            std::uint32_t width,
            std::uint32_t height,
            std::uint32_t bitrate,
            const std::wstring& outputPath,
            VideoEncoderDiagnostics& diagnostics) noexcept;
        [[nodiscard]] HRESULT Start(
            ID3D11Device* device,
            std::uint32_t width,
            std::uint32_t height,
            std::uint32_t framesPerSecond,
            std::uint32_t bitrate,
            const std::wstring& outputPath,
            VideoEncoderDiagnostics& diagnostics,
            bool audioEnabled) noexcept;
        // Test-only seam used by the deterministic factorial harness. Normal
        // product callers continue through Start. The production input-media-
        // type variant remains available here for the focused hardware gate.
        [[nodiscard]] HRESULT StartForDiagnostics(
            ID3D11Device* device,
            std::uint32_t width,
            std::uint32_t height,
            std::uint32_t framesPerSecond,
            std::uint32_t bitrate,
            const std::wstring& outputPath,
            VideoEncoderDiagnostics& diagnostics,
            H264EncoderStartupVariant startupVariant) noexcept;
        [[nodiscard]] HRESULT WriteSample(
            IMFSample* sample,
            double& durationMilliseconds) noexcept;
        [[nodiscard]] HRESULT WriteAudioPcm(
            const std::vector<BYTE>& bytes,
            std::int64_t sampleTime100ns,
            std::int64_t sampleDuration100ns) noexcept;
        [[nodiscard]] HRESULT Finalize(
            VideoEncoderDiagnostics& diagnostics) noexcept;
        [[nodiscard]] HRESULT QuickRuntimeValidation(
            VideoEncoderDiagnostics& diagnostics) noexcept;
        [[nodiscard]] HRESULT FullTestValidation(
            VideoEncoderDiagnostics& diagnostics) noexcept;
        void Shutdown() noexcept;

    private:
        [[nodiscard]] HRESULT StartCore(
            ID3D11Device* device,
            std::uint32_t width,
            std::uint32_t height,
            std::uint32_t framesPerSecond,
            std::uint32_t bitrate,
             const std::wstring& outputPath,
             VideoEncoderDiagnostics& diagnostics,
             bool audioEnabled,
             H264EncoderStartupVariant startupVariant,
             bool diagnosticIdentityLifecycleProbe) noexcept;
        void ProbeSelectedEncoder(VideoEncoderDiagnostics& diagnostics) noexcept;
        void ProbeBitrateLifecycle(
            VideoEncoderDiagnostics& diagnostics,
            bool postBegin,
            bool postFirstSample) noexcept;
        [[nodiscard]] HRESULT ValidateSourceReader(
            VideoEncoderDiagnostics& diagnostics,
            std::uint32_t sampleLimit,
            bool requireEndOfStream,
            const char* validationMode) noexcept;

        winrt::com_ptr<IMFDXGIDeviceManager> deviceManager_;
        winrt::com_ptr<IMFByteStream> byteStream_;
        winrt::com_ptr<IMFMediaSink> mediaSink_;
        winrt::com_ptr<IMFSinkWriter> writer_;
        MfAacAudioStream audioStream_;
        DWORD videoStreamIndex_{};
        DWORD audioStreamIndex_{};
        UINT resetToken_{};
        std::wstring outputPath_;
        std::uint32_t width_{};
        std::uint32_t height_{};
        VideoEncoderDiagnostics* diagnostics_{};
        bool postFirstSampleProbeAttempted_{};
        bool mfStarted_{};
        bool beganWriting_{};
        bool finalized_{};
        bool audioEnabled_{};
        bool diagnosticIdentityLifecycleProbe_{};
    };
}
