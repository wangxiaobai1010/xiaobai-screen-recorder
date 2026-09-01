#include "MfH264SinkWriterSession.h"

#include "MfAacAudioStream.h"

#include <codecapi.h>
#include <icodecapi.h>
#include <mfapi.h>
#include <mferror.h>
#include <mftransform.h>

#include <algorithm>
#include <charconv>
#include <chrono>
#include <cstring>
#include <filesystem>
#include <sstream>

namespace xbpreview
{
    namespace
    {
        struct CodecPropertyDefinition final
        {
            const char* name;
            const GUID* api;
        };

        const std::array<
            CodecPropertyDefinition,
            VideoEncoderCapabilityPropertyCapacity> CodecProperties{
            CodecPropertyDefinition{
                "CODECAPI_AVEncCommonRateControlMode",
                &CODECAPI_AVEncCommonRateControlMode },
            CodecPropertyDefinition{
                "CODECAPI_AVEncCommonQuality",
                &CODECAPI_AVEncCommonQuality },
            CodecPropertyDefinition{
                "CODECAPI_AVEncVideoEncodeQP",
                &CODECAPI_AVEncVideoEncodeQP },
            CodecPropertyDefinition{
                "CODECAPI_AVEncCommonQualityVsSpeed",
                &CODECAPI_AVEncCommonQualityVsSpeed },
            CodecPropertyDefinition{
                "CODECAPI_AVEncCommonMeanBitRate",
                &CODECAPI_AVEncCommonMeanBitRate },
            CodecPropertyDefinition{
                "CODECAPI_AVEncCommonMaxBitRate",
                &CODECAPI_AVEncCommonMaxBitRate },
            CodecPropertyDefinition{
                "CODECAPI_AVEncMPVProfile",
                &CODECAPI_AVEncMPVProfile },
            CodecPropertyDefinition{
                "CODECAPI_AVEncMPVLevel",
                &CODECAPI_AVEncMPVLevel },
            CodecPropertyDefinition{
                "CODECAPI_AVEncVideoMaxKeyframeDistance",
                &CODECAPI_AVEncVideoMaxKeyframeDistance },
            CodecPropertyDefinition{
                "CODECAPI_AVEncMPVDefaultBPictureCount",
                &CODECAPI_AVEncMPVDefaultBPictureCount },
        };

        std::string Utf8(const wchar_t* const value)
        {
            if (value == nullptr || value[0] == L'\0')
            {
                return {};
            }
            const auto length = static_cast<int>(wcslen(value));
            const auto size = WideCharToMultiByte(
                CP_UTF8, 0, value, length, nullptr, 0, nullptr, nullptr);
            if (size <= 0)
            {
                return {};
            }
            std::string result(static_cast<std::size_t>(size), '\0');
            WideCharToMultiByte(
                CP_UTF8, 0, value, length, result.data(), size,
                nullptr, nullptr);
            if (result.size() > VideoEncoderCapabilityTextLimit)
            {
                result.resize(VideoEncoderCapabilityTextLimit);
            }
            return result;
        }

        std::string GuidText(const GUID& value)
        {
            wchar_t buffer[64]{};
            return StringFromGUID2(value, buffer, ARRAYSIZE(buffer)) > 0
                ? Utf8(buffer)
                : "UNKNOWN";
        }

        std::string VariantText(const VARIANT& value)
        {
            switch (value.vt)
            {
            case VT_EMPTY: return "EMPTY";
            case VT_NULL: return "NULL";
            case VT_UI1: return std::to_string(value.bVal);
            case VT_UI2: return std::to_string(value.uiVal);
            case VT_UI4: return std::to_string(value.ulVal);
            case VT_UI8: return std::to_string(value.ullVal);
            case VT_I1: return std::to_string(value.cVal);
            case VT_I2: return std::to_string(value.iVal);
            case VT_I4: return std::to_string(value.lVal);
            case VT_I8: return std::to_string(value.llVal);
            case VT_INT: return std::to_string(value.intVal);
            case VT_UINT: return std::to_string(value.uintVal);
            case VT_BOOL: return value.boolVal == VARIANT_TRUE ? "true" : "false";
            case VT_R4: return std::to_string(value.fltVal);
            case VT_R8: return std::to_string(value.dblVal);
            case VT_BSTR: return Utf8(value.bstrVal);
            default: return "UNSUPPORTED_VARIANT_TYPE_" +
                std::to_string(value.vt);
            }
        }

        std::string AttributeText(
            IMFAttributes* const attributes,
            const GUID& key)
        {
            if (attributes == nullptr)
            {
                return {};
            }
            PROPVARIANT value{};
            PropVariantInit(&value);
            const auto result = attributes->GetItem(key, &value);
            std::string text;
            if (SUCCEEDED(result))
            {
                switch (value.vt)
                {
                case VT_LPWSTR: text = Utf8(value.pwszVal); break;
                case VT_BSTR: text = Utf8(value.bstrVal); break;
                case VT_UI4: text = std::to_string(value.ulVal); break;
                case VT_UI8: text = std::to_string(value.uhVal.QuadPart); break;
                case VT_I4: text = std::to_string(value.lVal); break;
                case VT_CLSID:
                    if (value.puuid != nullptr)
                    {
                        text = GuidText(*value.puuid);
                    }
                    break;
                default: break;
                }
            }
            PropVariantClear(&value);
            return text;
        }

        void QueryCodecProperty(
            ICodecAPI* const codec,
            const CodecPropertyDefinition& definition,
            VideoEncoderCodecPropertyDiagnostic& result)
        {
            result = {};
            result.property = definition.name;
            result.isSupportedHResult = codec->IsSupported(definition.api);
            result.isSupported = result.isSupportedHResult == S_OK;
            if (!result.isSupported)
            {
                return;
            }

            result.isModifiableHResult = codec->IsModifiable(definition.api);
            result.isModifiable = result.isModifiableHResult == S_OK;

            VARIANT current{};
            VariantInit(&current);
            result.currentValueHResult = codec->GetValue(
                definition.api, &current);
            if (SUCCEEDED(result.currentValueHResult))
            {
                result.currentValue = VariantText(current);
            }
            VariantClear(&current);

            VARIANT minimum{};
            VARIANT maximum{};
            VARIANT step{};
            VariantInit(&minimum);
            VariantInit(&maximum);
            VariantInit(&step);
            result.rangeHResult = codec->GetParameterRange(
                definition.api, &minimum, &maximum, &step);
            if (SUCCEEDED(result.rangeHResult))
            {
                result.rangeMinimum = VariantText(minimum);
                result.rangeMaximum = VariantText(maximum);
                result.rangeStep = VariantText(step);
            }
            VariantClear(&minimum);
            VariantClear(&maximum);
            VariantClear(&step);

            VARIANT* values{};
            ULONG valueCount{};
            result.possibleValuesHResult = codec->GetParameterValues(
                definition.api, &values, &valueCount);
            if (SUCCEEDED(result.possibleValuesHResult) && values != nullptr)
            {
                try
                {
                    const auto storedCount = (std::min)(
                        static_cast<std::size_t>(valueCount),
                        VideoEncoderCapabilityPossibleValueLimit);
                    result.possibleValues.reserve(storedCount);
                    for (std::size_t index = 0; index < storedCount; ++index)
                    {
                        result.possibleValues.push_back(
                            VariantText(values[index]));
                    }
                    result.possibleValuesTruncated =
                        valueCount > VideoEncoderCapabilityPossibleValueLimit;
                }
                catch (...)
                {
                    for (ULONG index = 0; index < valueCount; ++index)
                    {
                        VariantClear(&values[index]);
                    }
                    CoTaskMemFree(values);
                    throw;
                }
                for (ULONG index = 0; index < valueCount; ++index)
                {
                    VariantClear(&values[index]);
                }
                CoTaskMemFree(values);
            }
        }

        bool ParseInteger(const std::string& text, std::int64_t& value) noexcept
        {
            const auto parsed = std::from_chars(
                text.data(), text.data() + text.size(), value);
            return parsed.ec == std::errc{} &&
                parsed.ptr == text.data() + text.size();
        }

        const char* RateControlModeName(const std::int64_t value) noexcept
        {
            switch (value)
            {
            case eAVEncCommonRateControlMode_CBR: return "CBR";
            case eAVEncCommonRateControlMode_PeakConstrainedVBR:
                return "PeakConstrainedVBR";
            case eAVEncCommonRateControlMode_UnconstrainedVBR:
                return "UnconstrainedVBR";
            case eAVEncCommonRateControlMode_Quality: return "Quality";
            case eAVEncCommonRateControlMode_LowDelayVBR: return "LowDelayVBR";
            case eAVEncCommonRateControlMode_GlobalVBR: return "GlobalVBR";
            case eAVEncCommonRateControlMode_GlobalLowDelayVBR:
                return "GlobalLowDelayVBR";
            default: return nullptr;
            }
        }

        bool IsMicrosoftSoftwareH264Encoder(const CLSID& value) noexcept
        {
            // CLSID_CMSH264EncoderMFT from wmcodecdsp.h. Keep this local so the
            // diagnostic runner does not acquire another link dependency.
            constexpr CLSID MicrosoftSoftwareH264Encoder{
                0x6ca50344,
                0x051a,
                0x4ded,
                { 0x97, 0x79, 0xa4, 0x33, 0x05, 0x16, 0x5e, 0x35 } };
            return IsEqualCLSID(value, MicrosoftSoftwareH264Encoder) != FALSE;
        }

        const char* H264ProfileName(const std::uint32_t value) noexcept
        {
            switch (value)
            {
            case eAVEncH264VProfile_Base: return "Baseline";
            case eAVEncH264VProfile_Main: return "Main";
            case eAVEncH264VProfile_High: return "High";
            case eAVEncH264VProfile_ConstrainedBase:
                return "Constrained Baseline";
            case eAVEncH264VProfile_ConstrainedHigh:
                return "Constrained High";
            default: return "UNRECOGNIZED";
            }
        }

        void ReadMediaType(
            IMFMediaType* const mediaType,
            const HRESULT queryResult,
            VideoEncoderMediaTypeDiagnostic& result)
        {
            result = {};
            result.queryAttempted = true;
            result.queryHResult = queryResult;
            if (FAILED(queryResult) || mediaType == nullptr)
            {
                return;
            }

            GUID value{};
            result.majorTypeHResult = mediaType->GetGUID(MF_MT_MAJOR_TYPE, &value);
            if (SUCCEEDED(result.majorTypeHResult))
            {
                result.majorType = GuidText(value);
            }
            result.subtypeHResult = mediaType->GetGUID(MF_MT_SUBTYPE, &value);
            if (SUCCEEDED(result.subtypeHResult))
            {
                result.subtype = GuidText(value);
            }
            result.frameSizeHResult = MFGetAttributeSize(
                mediaType, MF_MT_FRAME_SIZE, &result.width, &result.height);
            result.frameRateHResult = MFGetAttributeRatio(
                mediaType,
                MF_MT_FRAME_RATE,
                &result.frameRateNumerator,
                &result.frameRateDenominator);
            result.averageBitrateHResult = mediaType->GetUINT32(
                MF_MT_AVG_BITRATE, &result.averageBitrate);
            result.mpeg2ProfileHResult = mediaType->GetUINT32(
                MF_MT_MPEG2_PROFILE, &result.mpeg2Profile);
            if (SUCCEEDED(result.mpeg2ProfileHResult))
            {
                result.mpeg2ProfileName = H264ProfileName(result.mpeg2Profile);
            }
        }

        HRESULT ReadCurrentMediaTypes(
            IMFTransform* const transform,
            VideoEncoderMediaTypeDiagnostic& input,
            VideoEncoderMediaTypeDiagnostic& output)
        {
            if (transform == nullptr)
            {
                ReadMediaType(nullptr, E_POINTER, input);
                ReadMediaType(nullptr, E_POINTER, output);
                return E_POINTER;
            }

            DWORD inputCount{};
            DWORD outputCount{};
            auto result = transform->GetStreamCount(&inputCount, &outputCount);
            if (FAILED(result) || inputCount == 0 || outputCount == 0 ||
                inputCount > 32 || outputCount > 32)
            {
                const auto failure = FAILED(result) ? result : E_UNEXPECTED;
                ReadMediaType(nullptr, failure, input);
                ReadMediaType(nullptr, failure, output);
                return failure;
            }

            std::vector<DWORD> inputIds(inputCount);
            std::vector<DWORD> outputIds(outputCount);
            result = transform->GetStreamIDs(
                inputCount, inputIds.data(), outputCount, outputIds.data());
            DWORD inputId{};
            DWORD outputId{};
            if (SUCCEEDED(result))
            {
                inputId = inputIds[0];
                outputId = outputIds[0];
            }
            else if (result != E_NOTIMPL)
            {
                ReadMediaType(nullptr, result, input);
                ReadMediaType(nullptr, result, output);
                return result;
            }

            winrt::com_ptr<IMFMediaType> inputType;
            const auto inputResult = transform->GetInputCurrentType(
                inputId, inputType.put());
            ReadMediaType(inputType.get(), inputResult, input);
            winrt::com_ptr<IMFMediaType> outputType;
            const auto outputResult = transform->GetOutputCurrentType(
                outputId, outputType.put());
            ReadMediaType(outputType.get(), outputResult, output);
            return FAILED(outputResult) ? outputResult : inputResult;
        }

        void ReadCodecValue(
            ICodecAPI* const codec,
            const GUID& property,
            HRESULT& queryResult,
            std::string& value)
        {
            VARIANT current{};
            VariantInit(&current);
            queryResult = codec->GetValue(&property, &current);
            if (SUCCEEDED(queryResult))
            {
                value = VariantText(current);
            }
            VariantClear(&current);
        }

        void ReadCodecApiReadback(
            IMFTransform* const transform,
            VideoEncoderCodecApiReadbackDiagnostic& result)
        {
            result = {};
            result.queryAttempted = true;
            if (transform == nullptr)
            {
                result.codecApiHResult = E_POINTER;
                return;
            }
            winrt::com_ptr<ICodecAPI> codec;
            result.codecApiHResult = transform->QueryInterface(
                IID_PPV_ARGS(codec.put()));
            if (!codec)
            {
                return;
            }
            ReadCodecValue(
                codec.get(),
                CODECAPI_AVEncCommonRateControlMode,
                result.rateControlHResult,
                result.rateControlValue);
            std::int64_t rateControl{};
            if (SUCCEEDED(result.rateControlHResult) &&
                ParseInteger(result.rateControlValue, rateControl))
            {
                const auto name = RateControlModeName(rateControl);
                result.rateControlName = name == nullptr
                    ? "UNRECOGNIZED_" + std::to_string(rateControl)
                    : name;
            }
            ReadCodecValue(
                codec.get(),
                CODECAPI_AVEncCommonMeanBitRate,
                result.meanBitrateHResult,
                result.meanBitrate);
            ReadCodecValue(
                codec.get(),
                CODECAPI_AVEncCommonMaxBitRate,
                result.maxBitrateHResult,
                result.maxBitrate);
        }

        HRESULT GetSelectedEncoder(
            IMFSinkWriter* const writer,
            const DWORD streamIndex,
            const DWORD transformIndex,
            IMFTransform** const encoder)
        {
            if (writer == nullptr || encoder == nullptr)
            {
                return E_POINTER;
            }
            *encoder = nullptr;
            winrt::com_ptr<IMFSinkWriterEx> writerEx;
            auto result = writer->QueryInterface(IID_PPV_ARGS(writerEx.put()));
            if (FAILED(result))
            {
                return result;
            }
            GUID category{};
            result = writerEx->GetTransformForStream(
                streamIndex, transformIndex, &category, encoder);
            if (SUCCEEDED(result) &&
                (category != MFT_CATEGORY_VIDEO_ENCODER || *encoder == nullptr))
            {
                if (*encoder != nullptr)
                {
                    (*encoder)->Release();
                }
                *encoder = nullptr;
                return MF_E_INVALIDREQUEST;
            }
            return result;
        }

        bool AddRateControlMode(
            VideoEncoderCapabilityDiagnostics& capabilities,
            const std::int64_t value)
        {
            const auto name = RateControlModeName(value);
            if (name == nullptr)
            {
                return false;
            }
            if (std::find(
                    capabilities.supportedRateControlModes.begin(),
                    capabilities.supportedRateControlModes.end(),
                    name) == capabilities.supportedRateControlModes.end())
            {
                capabilities.supportedRateControlModes.emplace_back(name);
            }
            return value == eAVEncCommonRateControlMode_Quality;
        }

        bool PopulateRateControlEvidence(
            VideoEncoderCapabilityDiagnostics& capabilities)
        {
            const auto& property = capabilities.properties[0];
            std::int64_t current{};
            if (SUCCEEDED(property.currentValueHResult) &&
                ParseInteger(property.currentValue, current))
            {
                const auto name = RateControlModeName(current);
                capabilities.currentRateControlMode = name == nullptr
                    ? "UNRECOGNIZED_" + std::to_string(current)
                    : name;
            }

            bool qualitySupported{};
            if (SUCCEEDED(property.possibleValuesHResult) &&
                !property.possibleValues.empty())
            {
                capabilities.rateControlModeEvidence = "POSSIBLE_VALUES";
                for (const auto& text : property.possibleValues)
                {
                    std::int64_t value{};
                    if (ParseInteger(text, value))
                    {
                        qualitySupported =
                            AddRateControlMode(capabilities, value) ||
                            qualitySupported;
                    }
                }
                return qualitySupported;
            }

            std::int64_t minimum{};
            std::int64_t maximum{};
            std::int64_t step{};
            if (SUCCEEDED(property.rangeHResult) &&
                ParseInteger(property.rangeMinimum, minimum) &&
                ParseInteger(property.rangeMaximum, maximum) &&
                ParseInteger(property.rangeStep, step) && step > 0)
            {
                capabilities.rateControlModeEvidence = "RANGE";
                for (std::int64_t value = eAVEncCommonRateControlMode_CBR;
                    value <= eAVEncCommonRateControlMode_GlobalLowDelayVBR;
                    ++value)
                {
                    if (value >= minimum && value <= maximum &&
                        (static_cast<std::uint64_t>(value) -
                            static_cast<std::uint64_t>(minimum)) %
                            static_cast<std::uint64_t>(step) == 0)
                    {
                        qualitySupported =
                            AddRateControlMode(capabilities, value) ||
                            qualitySupported;
                    }
                }
                return qualitySupported;
            }
            return false;
        }

        void DecideQualityCandidates(
            VideoEncoderCapabilityDiagnostics& capabilities)
        {
            if (!capabilities.codecApiAvailable)
            {
                return;
            }
            const auto& rateControl = capabilities.properties[0];
            const auto& quality = capabilities.properties[1];
            const auto& qp = capabilities.properties[2];
            const auto qualityModeSupported = rateControl.isSupported
                ? PopulateRateControlEvidence(capabilities)
                : false;
            if (!rateControl.isSupported || !quality.isSupported ||
                !quality.isModifiable)
            {
                capabilities.qualityBasedVbrCandidate = "NO";
            }
            else
            {
                capabilities.qualityBasedVbrCandidate = qualityModeSupported
                    ? "YES"
                    : capabilities.rateControlModeEvidence == "N/A"
                        ? "UNKNOWN"
                        : "NO";
            }

            if (!qp.isSupported || !qp.isModifiable)
            {
                capabilities.qpCandidate = "NO";
            }
            else if (SUCCEEDED(qp.currentValueHResult) &&
                SUCCEEDED(qp.rangeHResult))
            {
                capabilities.qpCandidate = "YES";
            }
        }

        void ReadOutputProfileAndLevel(
            IMFTransform* const transform,
            VideoEncoderCapabilityDiagnostics& capabilities)
        {
            DWORD inputCount{};
            DWORD outputCount{};
            capabilities.outputMediaTypeHResult = transform->GetStreamCount(
                &inputCount, &outputCount);
            if (FAILED(capabilities.outputMediaTypeHResult) || outputCount == 0)
            {
                return;
            }
            if (inputCount > 32 || outputCount > 32)
            {
                capabilities.outputMediaTypeHResult = E_UNEXPECTED;
                return;
            }
            std::vector<DWORD> inputIds(inputCount);
            std::vector<DWORD> outputIds(outputCount);
            auto idsResult = transform->GetStreamIDs(
                inputCount, inputIds.data(), outputCount, outputIds.data());
            DWORD outputId{};
            if (SUCCEEDED(idsResult))
            {
                outputId = outputIds[0];
            }
            else if (idsResult != E_NOTIMPL)
            {
                capabilities.outputMediaTypeHResult = idsResult;
                return;
            }
            winrt::com_ptr<IMFMediaType> outputType;
            capabilities.outputMediaTypeHResult = transform->GetOutputCurrentType(
                outputId, outputType.put());
            if (FAILED(capabilities.outputMediaTypeHResult))
            {
                return;
            }
            UINT32 profile{};
            if (SUCCEEDED(outputType->GetUINT32(MF_MT_MPEG2_PROFILE, &profile)))
            {
                capabilities.outputProfile = std::to_string(profile);
            }
            UINT32 level{};
            if (SUCCEEDED(outputType->GetUINT32(MF_MT_MPEG2_LEVEL, &level)))
            {
                capabilities.outputLevel = std::to_string(level);
            }
        }
    }

    MfH264SinkWriterSession::~MfH264SinkWriterSession()
    {
        Shutdown();
    }

    void MfH264SinkWriterSession::ProbeSelectedEncoder(
        VideoEncoderDiagnostics& diagnostics) noexcept
    {
        auto& capabilities = diagnostics.encoderCapabilities;
        try
        {
            if (!capabilities.probeAttempted)
            {
                capabilities = {};
                capabilities.probeAttempted = true;
                capabilities.propertyCount = CodecProperties.size();
                for (std::size_t index = 0;
                    index < CodecProperties.size(); ++index)
                {
                    capabilities.properties[index].property =
                        CodecProperties[index].name;
                }
            }
            if (!writer_)
            {
                capabilities.probeHResult = MF_E_INVALIDREQUEST;
                return;
            }
            winrt::com_ptr<IMFSinkWriterEx> writerEx;
            capabilities.sinkWriterExHResult = writer_->QueryInterface(
                IID_PPV_ARGS(writerEx.put()));
            capabilities.sinkWriterExAvailable =
                SUCCEEDED(capabilities.sinkWriterExHResult);
            if (!writerEx)
            {
                capabilities.probeHResult = capabilities.sinkWriterExHResult;
                return;
            }

            constexpr DWORD TransformSearchLimit = 32;
            winrt::com_ptr<IMFTransform> encoder;
            for (DWORD index = 0; index < TransformSearchLimit; ++index)
            {
                GUID category{};
                winrt::com_ptr<IMFTransform> transform;
                const auto result = writerEx->GetTransformForStream(
                    videoStreamIndex_, index, &category, transform.put());
                if (FAILED(result))
                {
                    capabilities.probeHResult = result;
                    break;
                }
                if (category == MFT_CATEGORY_VIDEO_ENCODER)
                {
                    encoder = std::move(transform);
                    capabilities.transformIndex = index;
                    capabilities.transformCategory = GuidText(category);
                    break;
                }
            }
            if (!encoder)
            {
                return;
            }

            capabilities.actualTransformObtained = true;
            capabilities.probeHResult = S_OK;
            diagnostics.encoderIdentityStatus = "ActualTransformObtained";

            CLSID encoderClsid{};
            bool encoderClsidAvailable{};
            winrt::com_ptr<IMFAttributes> transformAttributes;
            capabilities.transformAttributesHResult = encoder->GetAttributes(
                transformAttributes.put());
            if (transformAttributes)
            {
                GUID clsid{};
                const auto clsidResult = transformAttributes->GetGUID(
                    MFT_TRANSFORM_CLSID_Attribute, &clsid);
                if (SUCCEEDED(clsidResult))
                {
                    encoderClsid = clsid;
                    encoderClsidAvailable = true;
                    capabilities.transformClsidAttributeHResult = clsidResult;
                    capabilities.transformClsidAttribute = GuidText(clsid);
                    capabilities.encoderClsid = GuidText(clsid);
                }
                else if (FAILED(
                    capabilities.transformClsidAttributeHResult))
                {
                    capabilities.transformClsidAttributeHResult = clsidResult;
                }
                const auto friendlyName = AttributeText(
                    transformAttributes.get(), MFT_FRIENDLY_NAME_Attribute);
                if (!friendlyName.empty())
                {
                    capabilities.friendlyNameAttributeExposed = true;
                    capabilities.encoderFriendlyName = friendlyName;
                    diagnostics.encoderFriendlyName = friendlyName;
                }
                const auto hardwareUrl = AttributeText(
                    transformAttributes.get(), MFT_ENUM_HARDWARE_URL_Attribute);
                if (!hardwareUrl.empty())
                {
                    capabilities.hardwareUrlAttributeExposed = true;
                    capabilities.hardwareUrl = hardwareUrl;
                }
                const auto vendorId = AttributeText(
                    transformAttributes.get(),
                    MFT_ENUM_HARDWARE_VENDOR_ID_Attribute);
                if (!vendorId.empty())
                {
                    capabilities.hardwareVendorIdAttributeExposed = true;
                    capabilities.hardwareVendorId = vendorId;
                    capabilities.encoderVendor = vendorId;
                }
                UINT32 asyncMarker{};
                if (SUCCEEDED(transformAttributes->GetUINT32(
                    MF_TRANSFORM_ASYNC, &asyncMarker)))
                {
                    capabilities.asyncMarkerExposed = true;
                    capabilities.asyncMarker = asyncMarker != FALSE;
                }
            }

            // Recover read-only registration identity through the transform's
            // COM class when the live attribute store does not expose it.
            if (!encoderClsidAvailable)
            {
                winrt::com_ptr<IPersist> persist;
                const auto persistQueryResult = encoder->QueryInterface(
                    IID_PPV_ARGS(persist.put()));
                capabilities.persistQueryInterfaceHResult = persistQueryResult;
                if (SUCCEEDED(persistQueryResult))
                {
                    const auto persistClassResult =
                        persist->GetClassID(&encoderClsid);
                    capabilities.persistGetClassIdHResult = persistClassResult;
                    if (SUCCEEDED(persistClassResult))
                    {
                        encoderClsidAvailable = true;
                        capabilities.persistClsid = GuidText(encoderClsid);
                        capabilities.encoderClsid = GuidText(encoderClsid);
                    }
                }
            }
            if (encoderClsidAvailable)
            {
                wchar_t* registeredName{};
                MFT_REGISTER_TYPE_INFO* inputTypes{};
                MFT_REGISTER_TYPE_INFO* outputTypes{};
                UINT32 inputTypeCount{};
                UINT32 outputTypeCount{};
                winrt::com_ptr<IMFAttributes> registeredAttributes;
                const auto infoResult = MFTGetInfo(
                    encoderClsid,
                    &registeredName,
                    &inputTypes,
                    &inputTypeCount,
                    &outputTypes,
                    &outputTypeCount,
                    registeredAttributes.put());
                if (SUCCEEDED(infoResult))
                {
                    const auto friendlyName = Utf8(registeredName);
                    if (!friendlyName.empty() &&
                        capabilities.encoderFriendlyName ==
                            "UNKNOWN / NOT EXPOSED")
                    {
                        capabilities.encoderFriendlyName = friendlyName;
                        diagnostics.encoderFriendlyName = friendlyName;
                    }
                    if (registeredAttributes)
                    {
                        const auto hardwareUrl = AttributeText(
                            registeredAttributes.get(),
                            MFT_ENUM_HARDWARE_URL_Attribute);
                        if (!hardwareUrl.empty() &&
                            capabilities.hardwareUrl ==
                                "UNKNOWN / NOT EXPOSED")
                        {
                            capabilities.hardwareUrl = hardwareUrl;
                        }
                        const auto vendorId = AttributeText(
                            registeredAttributes.get(),
                            MFT_ENUM_HARDWARE_VENDOR_ID_Attribute);
                        if (!vendorId.empty() &&
                            capabilities.hardwareVendorId ==
                                "UNKNOWN / NOT EXPOSED")
                        {
                            capabilities.hardwareVendorId = vendorId;
                            capabilities.encoderVendor = vendorId;
                        }
                    }
                }
                CoTaskMemFree(registeredName);
                CoTaskMemFree(inputTypes);
                CoTaskMemFree(outputTypes);
            }

            const auto hasHardwareEvidence =
                capabilities.hardwareUrl != "UNKNOWN / NOT EXPOSED" ||
                capabilities.hardwareVendorId != "UNKNOWN / NOT EXPOSED";
            const auto hasExplicitSoftwareEvidence =
                encoderClsidAvailable &&
                IsMicrosoftSoftwareH264Encoder(encoderClsid);
            if (hasExplicitSoftwareEvidence)
            {
                capabilities.hardwareEvidence = "NONE EXPOSED";
                capabilities.softwareEvidence =
                    "CLSID_CMSH264EncoderMFT";
                capabilities.hardwareSoftwareVerdict = "SOFTWARE";
                diagnostics.hardwareTransformSelected = "No";
            }
            else if (hasHardwareEvidence)
            {
                capabilities.hardwareEvidence = "MFT hardware attribute exposed";
                capabilities.softwareEvidence = "NONE EXPOSED";
                capabilities.hardwareSoftwareVerdict = "HARDWARE";
                diagnostics.hardwareTransformSelected = "Yes";
            }

            ReadOutputProfileAndLevel(encoder.get(), capabilities);

            winrt::com_ptr<ICodecAPI> codec;
            capabilities.codecApiHResult = encoder->QueryInterface(
                IID_PPV_ARGS(codec.put()));
            capabilities.codecApiAvailable =
                SUCCEEDED(capabilities.codecApiHResult);
            if (codec)
            {
                for (std::size_t index = 0;
                    index < CodecProperties.size(); ++index)
                {
                    QueryCodecProperty(
                        codec.get(), CodecProperties[index],
                        capabilities.properties[index]);
                }
                DecideQualityCandidates(capabilities);
            }
        }
        catch (const winrt::hresult_error& error)
        {
            capabilities.probeHResult = error.code();
        }
        catch (...)
        {
            capabilities.probeHResult = E_FAIL;
        }
    }

    void MfH264SinkWriterSession::ProbeBitrateLifecycle(
        VideoEncoderDiagnostics& diagnostics,
        const bool postBegin,
        const bool postFirstSample) noexcept
    {
        auto& negotiation = diagnostics.bitrateNegotiation;
        auto& codecReadback = postFirstSample
            ? negotiation.codecApiPostFirstSample
            : postBegin
                ? negotiation.codecApiPostBegin
                : negotiation.codecApiPreBegin;
        try
        {
            winrt::com_ptr<IMFTransform> encoder;
            const auto transformResult = GetSelectedEncoder(
                writer_.get(),
                videoStreamIndex_,
                diagnostics.encoderCapabilities.transformIndex,
                encoder.put());
            if (FAILED(transformResult))
            {
                codecReadback = {};
                codecReadback.queryAttempted = true;
                codecReadback.codecApiHResult = transformResult;
                if (!postFirstSample)
                {
                    auto& input = postBegin
                        ? negotiation.actualInputPostBegin
                        : negotiation.actualInputPreBegin;
                    auto& output = postBegin
                        ? negotiation.actualOutputPostBegin
                        : negotiation.actualOutputPreBegin;
                    ReadMediaType(nullptr, transformResult, input);
                    ReadMediaType(nullptr, transformResult, output);
                }
                return;
            }

            if (negotiation.actualTransformFirstAvailableAt == "NOT_OBSERVED")
            {
                negotiation.actualTransformFirstAvailableAt = postFirstSample
                    ? "POST_FIRST_SUCCESSFUL_WRITE_SAMPLE"
                    : postBegin
                        ? "POST_BEGIN_WRITING"
                        : "POST_SET_INPUT_MEDIA_TYPE_PRE_BEGIN_WRITING";
            }
            if (!postBegin && !postFirstSample)
            {
                negotiation.actualTransformAvailablePreBegin = true;
            }
            if (!postFirstSample)
            {
                auto& input = postBegin
                    ? negotiation.actualInputPostBegin
                    : negotiation.actualInputPreBegin;
                auto& output = postBegin
                    ? negotiation.actualOutputPostBegin
                    : negotiation.actualOutputPreBegin;
                (void)ReadCurrentMediaTypes(encoder.get(), input, output);
            }
            ReadCodecApiReadback(encoder.get(), codecReadback);
        }
        catch (...)
        {
            codecReadback.queryAttempted = true;
            codecReadback.codecApiHResult = E_FAIL;
        }
    }

    HRESULT MfH264SinkWriterSession::Start(
        ID3D11Device* const device,
        const std::uint32_t width,
        const std::uint32_t height,
        const std::uint32_t bitrate,
        const std::wstring& outputPath,
        VideoEncoderDiagnostics& diagnostics) noexcept
    {
        return Start(
            device,
            width,
            height,
            VideoEncoderDefaultFrameRate,
            bitrate,
            outputPath,
            diagnostics,
            false);
    }

    HRESULT MfH264SinkWriterSession::Start(
        ID3D11Device* const device,
        const std::uint32_t width,
        const std::uint32_t height,
        const std::uint32_t framesPerSecond,
        const std::uint32_t bitrate,
        const std::wstring& outputPath,
        VideoEncoderDiagnostics& diagnostics,
        const bool audioEnabled) noexcept
    {
        return StartCore(
            device, width, height, framesPerSecond, bitrate, outputPath,
            diagnostics, audioEnabled,
            H264EncoderStartupVariant::
                ProductionInputMediaTypeParametersCbr,
            false);
    }

    HRESULT MfH264SinkWriterSession::StartForDiagnostics(
        ID3D11Device* const device,
        const std::uint32_t width,
        const std::uint32_t height,
        const std::uint32_t framesPerSecond,
        const std::uint32_t bitrate,
        const std::wstring& outputPath,
        VideoEncoderDiagnostics& diagnostics,
        const H264EncoderStartupVariant startupVariant) noexcept
    {
        return StartCore(
            device, width, height, framesPerSecond, bitrate, outputPath,
            diagnostics, false, startupVariant, true);
    }

    HRESULT MfH264SinkWriterSession::StartCore(
        ID3D11Device* const device,
        const std::uint32_t width,
        const std::uint32_t height,
        const std::uint32_t framesPerSecond,
        const std::uint32_t bitrate,
        const std::wstring& outputPath,
        VideoEncoderDiagnostics& diagnostics,
        const bool audioEnabled,
        const H264EncoderStartupVariant startupVariant,
        const bool diagnosticIdentityLifecycleProbe) noexcept
    {
        Shutdown();
        if (device == nullptr || width == 0 || height == 0 ||
            outputPath.empty() ||
            !IsSupportedVideoEncoderFrameRate(framesPerSecond) ||
            startupVariant >
                H264EncoderStartupVariant::
                    ProductionInputMediaTypeParametersCbr)
        {
            return E_INVALIDARG;
        }
        try
        {
            diagnostics_ = &diagnostics;
            diagnosticIdentityLifecycleProbe_ =
                diagnosticIdentityLifecycleProbe;
            diagnostics.productionHardwareEncoderRequired = false;
            diagnostics.actualHardwareEncoderVerified = false;
            diagnostics.softwareFallbackDetected = false;
            diagnostics.softwareFallbackRejected = false;
            diagnostics.hardwareEncoderVerificationHResult = E_PENDING;
            try
            {
                diagnostics.bitrateNegotiation = {};
            }
            catch (...)
            {
                // Resetting optional diagnostic storage cannot fail recording.
            }
            const auto startupConfiguration =
                CreateH264EncoderStartupConfiguration(bitrate);
            auto& bitrateNegotiation = diagnostics.bitrateNegotiation;
            bitrateNegotiation.requestedMeanBitrate =
                startupConfiguration.meanBitrate;
            switch (startupVariant)
            {
            case H264EncoderStartupVariant::DiagnosticCbrConfigStore:
                bitrateNegotiation.requestedRateControl = "CBR";
                break;
            case H264EncoderStartupVariant::DiagnosticNoConfigStore:
                bitrateNegotiation.requestedRateControl = "NOT_SET";
                break;
            case H264EncoderStartupVariant::DiagnosticMeanOnlyConfigStore:
                bitrateNegotiation.requestedRateControl = "NOT_SET";
                break;
            case H264EncoderStartupVariant::
                ProductionInputMediaTypeParametersCbr:
                bitrateNegotiation.requestedRateControl = "CBR";
                break;
            }
            winrt::check_hresult(MFStartup(MF_VERSION, MFSTARTUP_FULL));
            mfStarted_ = true;

            winrt::check_hresult(MFCreateDXGIDeviceManager(
                &resetToken_, deviceManager_.put()));
            winrt::check_hresult(deviceManager_->ResetDevice(device, resetToken_));
            diagnostics.dxgiDeviceManagerBound = true;

            winrt::com_ptr<IMFAttributes> attributes;
            winrt::check_hresult(MFCreateAttributes(attributes.put(), 4));
            winrt::check_hresult(attributes->SetUnknown(
                MF_SINK_WRITER_D3D_MANAGER, deviceManager_.get()));
            winrt::check_hresult(attributes->SetUINT32(
                MF_READWRITE_ENABLE_HARDWARE_TRANSFORMS, TRUE));
            winrt::check_hresult(attributes->SetGUID(
                MF_TRANSCODE_CONTAINERTYPE,
                MFTranscodeContainerType_MPEG4));

            winrt::com_ptr<IMFAttributes> inputEncodingParameters;
            if (startupVariant ==
                    H264EncoderStartupVariant::DiagnosticCbrConfigStore ||
                startupVariant == H264EncoderStartupVariant::
                    DiagnosticMeanOnlyConfigStore)
            {
                winrt::com_ptr<IPropertyStore> propertyStore;
                bitrateNegotiation.storeCreationHResult =
                    PSCreateMemoryPropertyStore(
                        IID_PPV_ARGS(propertyStore.put()));
                winrt::check_hresult(
                    bitrateNegotiation.storeCreationHResult);
                if (startupVariant == H264EncoderStartupVariant::
                    DiagnosticCbrConfigStore)
                {
                    const auto propertyConfigurationResult =
                        ApplyH264EncoderStartupConfiguration(
                            startupConfiguration,
                            [&propertyStore](
                                const GUID& property,
                                const std::uint32_t value) noexcept
                            {
                                return SetH264EncoderConfigStoreUInt32(
                                    propertyStore.get(), property, value);
                            },
                            bitrateNegotiation.rateControlPropertySetHResult,
                            bitrateNegotiation.meanBitratePropertySetHResult);
                    winrt::check_hresult(propertyConfigurationResult);
                }
                else
                {
                    bitrateNegotiation.meanBitratePropertySetHResult =
                        SetH264EncoderConfigStoreUInt32(
                            propertyStore.get(),
                            CODECAPI_AVEncCommonMeanBitRate,
                            startupConfiguration.meanBitrate);
                    winrt::check_hresult(
                        bitrateNegotiation.meanBitratePropertySetHResult);
                }
                bitrateNegotiation.sinkWriterConfigAttachHResult =
                    attributes->SetUnknown(
                        MF_SINK_WRITER_ENCODER_CONFIG,
                        propertyStore.get());
                winrt::check_hresult(
                    bitrateNegotiation.sinkWriterConfigAttachHResult);
                bitrateNegotiation.currentCodeUsesEncoderConfigStore = true;
                bitrateNegotiation.currentCodeOnlyUsesMediaTypeBitrate = false;
            }
            else if (startupVariant == H264EncoderStartupVariant::
                ProductionInputMediaTypeParametersCbr)
            {
                winrt::check_hresult(MFCreateAttributes(
                    inputEncodingParameters.put(), 2));
                bitrateNegotiation.inputParametersRateControlSetHResult =
                    inputEncodingParameters->SetUINT32(
                        CODECAPI_AVEncCommonRateControlMode,
                        startupConfiguration.rateControlMode);
                winrt::check_hresult(
                    bitrateNegotiation.inputParametersRateControlSetHResult);
                bitrateNegotiation.inputParametersMeanBitrateSetHResult =
                    inputEncodingParameters->SetUINT32(
                        CODECAPI_AVEncCommonMeanBitRate,
                        startupConfiguration.meanBitrate);
                winrt::check_hresult(
                    bitrateNegotiation.inputParametersMeanBitrateSetHResult);
                bitrateNegotiation.inputMediaTypeEncodingParametersUsed = true;
                bitrateNegotiation.currentCodeOnlyUsesMediaTypeBitrate = false;
            }

            outputPath_ = outputPath;
            width_ = width;
            height_ = height;

            winrt::com_ptr<IMFMediaType> outputType;
            winrt::check_hresult(MFCreateMediaType(outputType.put()));
            diagnostics.bitrateNegotiation.outputMediaTypeCreated = true;
            winrt::check_hresult(outputType->SetGUID(
                MF_MT_MAJOR_TYPE, MFMediaType_Video));
            winrt::check_hresult(outputType->SetGUID(
                MF_MT_SUBTYPE, MFVideoFormat_H264));
            winrt::check_hresult(outputType->SetUINT32(
                MF_MT_AVG_BITRATE, bitrate));
            winrt::check_hresult(outputType->SetUINT32(
                MF_MT_INTERLACE_MODE, MFVideoInterlace_Progressive));
            winrt::check_hresult(outputType->SetUINT32(
                MF_MT_VIDEO_PRIMARIES, MFVideoPrimaries_BT709));
            winrt::check_hresult(outputType->SetUINT32(
                MF_MT_TRANSFER_FUNCTION, MFVideoTransFunc_709));
            winrt::check_hresult(outputType->SetUINT32(
                MF_MT_YUV_MATRIX, MFVideoTransferMatrix_BT709));
            winrt::check_hresult(outputType->SetUINT32(
                MF_MT_VIDEO_NOMINAL_RANGE, MFNominalRange_16_235));
            winrt::check_hresult(MFSetAttributeSize(
                outputType.get(), MF_MT_FRAME_SIZE, width, height));
            winrt::check_hresult(MFSetAttributeRatio(
                outputType.get(), MF_MT_FRAME_RATE,
                framesPerSecond,
                VideoEncoderNominalFrameRateDenominator));
            winrt::check_hresult(MFSetAttributeRatio(
                outputType.get(), MF_MT_PIXEL_ASPECT_RATIO, 1, 1));
            try
            {
                ReadMediaType(
                    outputType.get(),
                    S_OK,
                    diagnostics.bitrateNegotiation.requestedOutput);
                diagnostics.bitrateNegotiation.
                    requestedOutputCapturedBeforeAddStream = true;
            }
            catch (...)
            {
                // Requested-type diagnostics must never change AddStream or
                // recording success semantics.
                diagnostics.bitrateNegotiation.requestedOutput.queryAttempted =
                    true;
                diagnostics.bitrateNegotiation.requestedOutput.queryHResult =
                    E_FAIL;
            }

            winrt::check_hresult(MFCreateFile(
                MF_ACCESSMODE_WRITE,
                MF_OPENMODE_DELETE_IF_EXIST,
                MF_FILEFLAGS_NONE,
                outputPath_.c_str(),
                byteStream_.put()));
            DWORD byteStreamCapabilities{};
            winrt::check_hresult(byteStream_->GetCapabilities(
                &byteStreamCapabilities));
            if ((byteStreamCapabilities & MFBYTESTREAM_IS_WRITABLE) == 0 ||
                (byteStreamCapabilities & MFBYTESTREAM_IS_SEEKABLE) == 0)
            {
                winrt::check_hresult(MF_E_BYTESTREAM_NOT_SEEKABLE);
            }
            winrt::check_hresult(MFCreateFMPEG4MediaSink(
                byteStream_.get(), outputType.get(), nullptr,
                mediaSink_.put()));
            winrt::check_hresult(MFCreateSinkWriterFromMediaSink(
                mediaSink_.get(), attributes.get(), writer_.put()));
            diagnostics.bitrateNegotiation.sinkWriterCreated = true;
            videoStreamIndex_ = 0;

            winrt::com_ptr<IMFMediaType> inputType;
            winrt::check_hresult(MFCreateMediaType(inputType.put()));
            winrt::check_hresult(inputType->SetGUID(
                MF_MT_MAJOR_TYPE, MFMediaType_Video));
            winrt::check_hresult(inputType->SetGUID(
                MF_MT_SUBTYPE, MFVideoFormat_NV12));
            winrt::check_hresult(inputType->SetUINT32(
                MF_MT_INTERLACE_MODE, MFVideoInterlace_Progressive));
            winrt::check_hresult(inputType->SetUINT32(
                MF_MT_VIDEO_PRIMARIES, MFVideoPrimaries_BT709));
            winrt::check_hresult(inputType->SetUINT32(
                MF_MT_TRANSFER_FUNCTION, MFVideoTransFunc_709));
            winrt::check_hresult(inputType->SetUINT32(
                MF_MT_YUV_MATRIX, MFVideoTransferMatrix_BT709));
            winrt::check_hresult(inputType->SetUINT32(
                MF_MT_VIDEO_NOMINAL_RANGE, MFNominalRange_16_235));
            winrt::check_hresult(MFSetAttributeSize(
                inputType.get(), MF_MT_FRAME_SIZE, width, height));
            winrt::check_hresult(MFSetAttributeRatio(
                inputType.get(), MF_MT_FRAME_RATE,
                framesPerSecond,
                VideoEncoderNominalFrameRateDenominator));
            winrt::check_hresult(MFSetAttributeRatio(
                inputType.get(), MF_MT_PIXEL_ASPECT_RATIO, 1, 1));
            diagnostics.bitrateNegotiation.setInputMediaTypeHResult =
                writer_->SetInputMediaType(
                videoStreamIndex_, inputType.get(),
                inputEncodingParameters.get());
            winrt::check_hresult(
                diagnostics.bitrateNegotiation.setInputMediaTypeHResult);
            diagnostics.bitrateNegotiation.setInputMediaTypeSucceeded = true;

            // Read the actual encoder instantiated by this Sink Writer. The
            // probe is read-only; the production path uses its positive
            // hardware verdict to fail closed before BeginWriting.
            ProbeSelectedEncoder(diagnostics);
            ProbeBitrateLifecycle(diagnostics, false, false);

            if (startupVariant == H264EncoderStartupVariant::
                ProductionInputMediaTypeParametersCbr)
            {
                winrt::check_hresult(
                    VerifyProductionHardwareEncoder(diagnostics));
            }

            if (audioEnabled)
            {
                winrt::check_hresult(MfAacAudioStream::Configure(
                    writer_.get(), audioStreamIndex_));
                audioEnabled_ = true;
            }

            winrt::check_hresult(writer_->BeginWriting());
            beganWriting_ = true;
            diagnostics.bitrateNegotiation.beginWritingSucceeded = true;
            if (diagnosticIdentityLifecycleProbe_)
            {
                ProbeSelectedEncoder(diagnostics);
            }
            ProbeBitrateLifecycle(diagnostics, true, false);
            return S_OK;
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

    HRESULT MfH264SinkWriterSession::WriteSample(
        IMFSample* const sample,
        double& durationMilliseconds) noexcept
    {
        durationMilliseconds = 0.0;
        if (!writer_ || !beganWriting_ || finalized_ || sample == nullptr)
        {
            return MF_E_INVALIDREQUEST;
        }
        const auto started = std::chrono::steady_clock::now();
        const auto result = writer_->WriteSample(videoStreamIndex_, sample);
        durationMilliseconds = std::chrono::duration<double, std::milli>(
            std::chrono::steady_clock::now() - started).count();
        if (SUCCEEDED(result) && !postFirstSampleProbeAttempted_)
        {
            postFirstSampleProbeAttempted_ = true;
            if (diagnostics_ != nullptr)
            {
                diagnostics_->bitrateNegotiation.firstVideoSampleWritten = true;
                if (diagnosticIdentityLifecycleProbe_)
                {
                    ProbeSelectedEncoder(*diagnostics_);
                }
                ProbeBitrateLifecycle(*diagnostics_, true, true);
            }
        }
        return result;
    }

    HRESULT MfH264SinkWriterSession::WriteAudioPcm(
        const std::vector<BYTE>& bytes,
        const std::int64_t sampleTime100ns,
        const std::int64_t sampleDuration100ns) noexcept
    {
        if (!writer_ || !beganWriting_ || finalized_ || !audioEnabled_)
        {
            return MF_E_INVALIDREQUEST;
        }
        // Empty data is meaningful to the donor handoff: the audio stream
        // applies its original silent-padding/last-frame policy.
        return audioStream_.WritePcm(
            writer_.get(),
            audioStreamIndex_,
            bytes,
            sampleTime100ns,
            sampleDuration100ns);
    }

    HRESULT MfH264SinkWriterSession::Finalize(
        VideoEncoderDiagnostics& diagnostics) noexcept
    {
        if (diagnostics.finalizeAttempted)
        {
            return diagnostics.finalizeHResult;
        }
        diagnostics.finalizeAttempted = true;
        const auto started = std::chrono::steady_clock::now();
        HRESULT result = writer_ && beganWriting_ && !finalized_
            ? writer_->NotifyEndOfSegment(videoStreamIndex_)
            : MF_E_INVALIDREQUEST;
        if (SUCCEEDED(result) && audioEnabled_)
        {
            result = writer_->NotifyEndOfSegment(audioStreamIndex_);
        }
        if (SUCCEEDED(result))
        {
            result = writer_->Finalize();
        }
        writer_ = nullptr;
        const auto mediaSinkShutdownResult = mediaSink_
            ? mediaSink_->Shutdown()
            : S_OK;
        mediaSink_ = nullptr;
        byteStream_ = nullptr;
        if (SUCCEEDED(result))
        {
            result = mediaSinkShutdownResult;
        }
        diagnostics.finalizeDurationMs =
            std::chrono::duration<double, std::milli>(
                std::chrono::steady_clock::now() - started).count();
        diagnostics.finalizeHResult = result;
        finalized_ = SUCCEEDED(result);
        return result;
    }

    HRESULT MfH264SinkWriterSession::QuickRuntimeValidation(
        VideoEncoderDiagnostics& diagnostics) noexcept
    {
        return ValidateSourceReader(
            diagnostics,
            VideoEncoderQuickRuntimeValidationSampleLimit,
            false,
            "QuickRuntimeValidation");
    }

    HRESULT MfH264SinkWriterSession::FullTestValidation(
        VideoEncoderDiagnostics& diagnostics) noexcept
    {
        return ValidateSourceReader(
            diagnostics,
            0,
            true,
            "FullTestValidation");
    }

    HRESULT MfH264SinkWriterSession::ValidateSourceReader(
        VideoEncoderDiagnostics& diagnostics,
        const std::uint32_t sampleLimit,
        const bool requireEndOfStream,
        const char* const validationMode) noexcept
    {
        const auto started = std::chrono::steady_clock::now();
        diagnostics.sourceReaderValidation = "FAIL";
        diagnostics.sourceReaderValidationMode = validationMode;
        diagnostics.validationSampleLimit = sampleLimit;
        diagnostics.validationSamplesRead = 0;
        diagnostics.validationReachedEndOfStream = false;
        diagnostics.validationDurationMs = 0.0;
        diagnostics.decodedFrameCount = 0;
        diagnostics.validatedFirstPts = 0;
        diagnostics.validatedLastPts = 0;
        diagnostics.validatedDuration100ns = 0;
        const auto finish = [&](const HRESULT result) noexcept
        {
            diagnostics.validationDurationMs =
                std::chrono::duration<double, std::milli>(
                    std::chrono::steady_clock::now() - started).count();
            return result;
        };
        try
        {
            diagnostics.outputFileExists =
                std::filesystem::is_regular_file(outputPath_);
            diagnostics.outputFileSize = diagnostics.outputFileExists
                ? std::filesystem::file_size(outputPath_)
                : 0;
            if (!diagnostics.outputFileExists || diagnostics.outputFileSize == 0)
            {
                return finish(HRESULT_FROM_WIN32(ERROR_FILE_INVALID));
            }
            winrt::com_ptr<IMFSourceReader> reader;
            winrt::check_hresult(MFCreateSourceReaderFromURL(
                outputPath_.c_str(), nullptr, reader.put()));
            winrt::com_ptr<IMFMediaType> decodedType;
            winrt::check_hresult(MFCreateMediaType(decodedType.put()));
            winrt::check_hresult(decodedType->SetGUID(
                MF_MT_MAJOR_TYPE, MFMediaType_Video));
            winrt::check_hresult(decodedType->SetGUID(
                MF_MT_SUBTYPE, MFVideoFormat_NV12));
            winrt::check_hresult(reader->SetCurrentMediaType(
                static_cast<DWORD>(MF_SOURCE_READER_FIRST_VIDEO_STREAM),
                nullptr,
                decodedType.get()));
            winrt::com_ptr<IMFMediaType> activeType;
            winrt::check_hresult(reader->GetCurrentMediaType(
                static_cast<DWORD>(MF_SOURCE_READER_FIRST_VIDEO_STREAM),
                activeType.put()));
            UINT32 actualWidth{};
            UINT32 actualHeight{};
            winrt::check_hresult(MFGetAttributeSize(
                activeType.get(), MF_MT_FRAME_SIZE,
                &actualWidth, &actualHeight));
            if (actualWidth != width_ || actualHeight != height_)
            {
                return finish(MF_E_INVALIDMEDIATYPE);
            }

            bool haveTimestamp{};
            LONGLONG lastTimestamp{};
            for (;;)
            {
                DWORD flags{};
                LONGLONG timestamp{};
                winrt::com_ptr<IMFSample> sample;
                winrt::check_hresult(reader->ReadSample(
                    static_cast<DWORD>(MF_SOURCE_READER_FIRST_VIDEO_STREAM),
                    0, nullptr, &flags, &timestamp, sample.put()));
                if ((flags & MF_SOURCE_READERF_ENDOFSTREAM) != 0)
                {
                    diagnostics.validationReachedEndOfStream = true;
                    break;
                }
                if (!sample)
                {
                    continue;
                }
                if (haveTimestamp && timestamp < lastTimestamp)
                {
                    return finish(MF_E_INVALID_TIMESTAMP);
                }
                if (!haveTimestamp)
                {
                    diagnostics.validatedFirstPts = timestamp;
                    haveTimestamp = true;
                }
                lastTimestamp = timestamp;
                ++diagnostics.decodedFrameCount;
                ++diagnostics.validationSamplesRead;
                if (sampleLimit != 0 &&
                    diagnostics.validationSamplesRead >= sampleLimit)
                {
                    break;
                }
            }
            diagnostics.validatedLastPts = lastTimestamp;
            diagnostics.validatedDuration100ns = haveTimestamp
                ? lastTimestamp - diagnostics.validatedFirstPts
                : 0;
            if (diagnostics.decodedFrameCount == 0 ||
                (diagnostics.decodedFrameCount > 1 &&
                    diagnostics.validatedDuration100ns <= 0) ||
                (requireEndOfStream &&
                    !diagnostics.validationReachedEndOfStream))
            {
                return finish(MF_E_INVALID_TIMESTAMP);
            }

            if (audioEnabled_)
            {
                const auto audioResult = MfAacAudioStream::ValidateOutput(
                    outputPath_, sampleLimit, requireEndOfStream);
                if (FAILED(audioResult))
                {
                    return finish(audioResult);
                }
            }

            diagnostics.sourceReaderValidation = "PASS";
            return finish(S_OK);
        }
        catch (const winrt::hresult_error& error)
        {
            return finish(error.code());
        }
        catch (...)
        {
            return finish(E_FAIL);
        }
    }

    void MfH264SinkWriterSession::Shutdown() noexcept
    {
        writer_ = nullptr;
        if (mediaSink_)
        {
            (void)mediaSink_->Shutdown();
        }
        mediaSink_ = nullptr;
        byteStream_ = nullptr;
        deviceManager_ = nullptr;
        if (mfStarted_)
        {
            MFShutdown();
            mfStarted_ = false;
        }
        beganWriting_ = false;
        finalized_ = false;
        outputPath_.clear();
        width_ = 0;
        height_ = 0;
        diagnostics_ = nullptr;
        postFirstSampleProbeAttempted_ = false;
        videoStreamIndex_ = 0;
        audioStreamIndex_ = 0;
        audioEnabled_ = false;
        diagnosticIdentityLifecycleProbe_ = false;
        audioStream_.Reset();
    }
}
