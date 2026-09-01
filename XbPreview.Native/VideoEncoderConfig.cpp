#include "VideoEncoderConfig.h"

#include "RecordingOutputRoot.h"
#include "RecordingSessionIdentity.h"

#include <windows.h>

#include <algorithm>
#include <cwctype>
#include <filesystem>
#include <stdexcept>

namespace xbpreview
{
    namespace
    {
        std::wstring ReadEnvironment(const wchar_t* const name)
        {
            const auto required = GetEnvironmentVariableW(name, nullptr, 0);
            if (required == 0)
            {
                return {};
            }
            std::wstring value(required, L'\0');
            const auto written = GetEnvironmentVariableW(
                name,
                value.data(),
                static_cast<DWORD>(value.size()));
            if (written == 0 || written >= value.size())
            {
                return {};
            }
            value.resize(written);
            return value;
        }

        bool IsEnabledValue(std::wstring value)
        {
            std::transform(
                value.begin(),
                value.end(),
                value.begin(),
                [](const wchar_t character)
                {
                    return static_cast<wchar_t>(towlower(character));
                });
            return value == L"1" || value == L"true";
        }

        GStreamerAudioMode ReadGStreamerAudioMode()
        {
            auto value = ReadEnvironment(
                L"XB_PREVIEW_RECORDING_AUDIO_SOURCE");
            std::transform(
                value.begin(),
                value.end(),
                value.begin(),
                [](const wchar_t character)
                {
                    return static_cast<wchar_t>(towlower(character));
                });
            if (value == L"microphone")
            {
                return GStreamerAudioMode::MicrophoneOnly;
            }
            if (value == L"dual" || value == L"system+microphone")
            {
                return GStreamerAudioMode::Dual;
            }
            if (value == L"system" || value == L"system-loopback")
            {
                return GStreamerAudioMode::SystemOnly;
            }
            return GStreamerAudioMode::Dual;
        }

        void AssignOutputPaths(
            VideoEncoderConfiguration& configuration,
            const std::filesystem::path& outputDirectory,
            const bool usePartialWorkingFile)
        {
            configuration.outputDirectory = outputDirectory.wstring();
            configuration.plannedFinalPath = (
                outputDirectory /
                (configuration.sessionId + L".mp4")).wstring();
            configuration.workingPath = usePartialWorkingFile
                ? (outputDirectory /
                    (configuration.sessionId + L".partial.mp4")).wstring()
                : configuration.plannedFinalPath;
        }
    }

    VideoEncoderConfiguration ReadVideoEncoderConfiguration(
        const std::wstring& diagnosticDirectory,
        const std::wstring& sessionId)
    {
        VideoEncoderConfiguration result{};
        result.enabled = IsEnabledValue(ReadEnvironment(
            L"XB_PREVIEW_DIAGNOSTIC_ENCODER"));
        result.sessionId = sessionId;
        result.diagnosticDirectory = diagnosticDirectory;
        result.audioMode = ReadGStreamerAudioMode();
        if (result.enabled)
        {
            try
            {
                AssignOutputPaths(
                    result,
                    ResolveArtifactsRoot(
                        std::filesystem::path(diagnosticDirectory)) /
                        L"p2.4-recordings",
                    false);
            }
            catch (...)
            {
                result.enabled = false;
            }
        }
        return result;
    }

    VideoEncoderConfiguration CreateRecordingConfiguration(
        const std::wstring& diagnosticDirectory,
        const std::wstring& sessionId)
    {
        return CreateRecordingConfiguration(
            diagnosticDirectory, sessionId, std::wstring{});
    }

    VideoEncoderConfiguration CreateRecordingConfiguration(
        const std::wstring& diagnosticDirectory,
        const std::wstring& sessionId,
        const std::wstring& managedOutputRoot)
    {
        VideoEncoderConfiguration result{};
        result.enabled = !sessionId.empty();
        result.publishOnStop = result.enabled;
        result.sessionId = sessionId;
        result.diagnosticDirectory = diagnosticDirectory;
        result.audioMode = ReadGStreamerAudioMode();
        const auto fault = ReadEnvironment(
            L"XB_PREVIEW_TEST_RECORDING_FAULT");
        if (fault == L"start-boundary-exception")
        {
            throw std::runtime_error(
                "Injected recording C ABI boundary exception.");
        }
        if (fault == L"unsupported-after-output-created")
        {
            result.faultInjection = VideoEncoderFaultInjection::
                UnsupportedAfterOutputFileCreated;
        }
        else if (fault == L"worker-exception-after-output-created")
        {
            result.faultInjection = VideoEncoderFaultInjection::
                WorkerExceptionAfterOutputFileCreated;
        }
        else if (fault == L"finalize-failure-after-write")
        {
            result.faultInjection = VideoEncoderFaultInjection::
                FinalizeFailureAfterWrite;
        }
        else if (fault == L"validation-failure-after-finalize")
        {
            result.faultInjection = VideoEncoderFaultInjection::
                ValidationFailureAfterFinalize;
        }
        else if (fault == L"publish-conflict-at-target")
        {
            result.faultInjection = VideoEncoderFaultInjection::
                PublishConflictAtTarget;
        }
        else if (fault == L"snapshot-exception-after-publish")
        {
            result.faultInjection = VideoEncoderFaultInjection::
                SnapshotExceptionAfterPublish;
        }
        else if (fault == L"working-identity-capture-failure")
        {
            result.faultInjection = VideoEncoderFaultInjection::
                WorkingIdentityCaptureFailure;
        }
        else if (fault == L"post-publish-identity-verification-failure")
        {
            result.faultInjection = VideoEncoderFaultInjection::
                PostPublishIdentityVerificationFailure;
        }
        else if (fault == L"audio-initialization-failure")
        {
            result.faultInjection = VideoEncoderFaultInjection::
                AudioInitializationFailure;
        }
        if (result.enabled)
        {
            const auto roots = managedOutputRoot.empty()
                ? ResolveRecordingOutputRoots(
                    std::filesystem::path(diagnosticDirectory))
                : ResolveRecordingOutputRootsFromManagedRoot(
                    std::filesystem::path(managedOutputRoot));
            if (!roots.Succeeded() ||
                !IsCanonicalRecordingSessionId(sessionId))
            {
                result.enabled = false;
                return result;
            }
            AssignOutputPaths(
                result,
                roots.mediaOutputRoot,
                true);
        }
        return result;
    }

    void ApplyAudioProgramMode(
        VideoEncoderConfiguration& configuration,
        const AudioProgramMode mode) noexcept
    {
        configuration.audioEnabled = mode != AudioProgramMode::None;
        switch (mode)
        {
        case AudioProgramMode::SystemOnly:
            configuration.audioMode = GStreamerAudioMode::SystemOnly;
            break;
        case AudioProgramMode::MicrophoneOnly:
            configuration.audioMode = GStreamerAudioMode::MicrophoneOnly;
            break;
        case AudioProgramMode::Dual:
            configuration.audioMode = GStreamerAudioMode::Dual;
            break;
        case AudioProgramMode::None:
        default:
            configuration.audioMode = GStreamerAudioMode::None;
            break;
        }
    }
}
