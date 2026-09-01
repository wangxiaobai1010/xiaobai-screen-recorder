#include "PreviewEngine.h"

#include "CursorCoordinateMapper.h"
#include "Letterbox.h"
#include "RecordingStorageSafety.h"
#include "RecordingVideoBitratePolicy.h"
#include "RecordingSessionIdentity.h"
#include "SessionPathSafety.h"
#include "WindowStageComposer.h"

#include <windows.graphics.capture.interop.h>
#include <windows.graphics.directx.direct3d11.interop.h>
#include <psapi.h>
#include <winrt/Windows.Foundation.Metadata.h>

#include <algorithm>
#include <array>
#include <chrono>
#include <cstring>
#include <cwctype>
#include <filesystem>
#include <limits>
#include <sstream>

namespace
{
    constexpr DWORD WdaExcludeFromCapture = 0x00000011;
    constexpr auto StartTimeout = std::chrono::seconds(15);

    bool IsLocalAbsolutePath(const std::filesystem::path& path) noexcept
    {
        try
        {
            if (path.empty() || !path.is_absolute())
            {
                return false;
            }
            const auto root = path.root_path().wstring();
            if (root.empty())
            {
                return false;
            }
            const auto type = GetDriveTypeW(root.c_str());
            return type != DRIVE_REMOTE && type != DRIVE_NO_ROOT_DIR &&
                type != DRIVE_UNKNOWN;
        }
        catch (...)
        {
            return false;
        }
    }

    bool IsSupportedCustomBackgroundPath(const wchar_t* const value) noexcept
    {
        if (value == nullptr || *value == L'\0')
        {
            return false;
        }
        try
        {
            const std::filesystem::path path(value);
            auto extension = path.extension().wstring();
            std::transform(
                extension.begin(), extension.end(), extension.begin(),
                [](const wchar_t character)
                {
                    return static_cast<wchar_t>(towlower(character));
                });
            std::error_code error;
            return IsLocalAbsolutePath(path) &&
                (extension == L".png" || extension == L".jpg" ||
                    extension == L".jpeg" || extension == L".bmp") &&
                std::filesystem::is_regular_file(path, error) && !error;
        }
        catch (...)
        {
            return false;
        }
    }

    std::wstring HresultMessage(const HRESULT result)
    {
        wchar_t* rawMessage = nullptr;
        const auto length = FormatMessageW(
            FORMAT_MESSAGE_ALLOCATE_BUFFER |
            FORMAT_MESSAGE_FROM_SYSTEM |
            FORMAT_MESSAGE_IGNORE_INSERTS,
            nullptr,
            static_cast<DWORD>(result),
            MAKELANGID(LANG_NEUTRAL, SUBLANG_DEFAULT),
            reinterpret_cast<wchar_t*>(&rawMessage),
            0,
            nullptr);

        std::wstring message;
        if (length != 0 && rawMessage != nullptr)
        {
            message.assign(rawMessage, rawMessage + length);
            while (!message.empty() &&
                (message.back() == L'\r' || message.back() == L'\n' ||
                    message.back() == L' '))
            {
                message.pop_back();
            }
        }
        LocalFree(rawMessage);
        return message;
    }

    std::string NarrowForLog(const std::wstring& value)
    {
        if (value.empty())
        {
            return {};
        }

        const auto required = WideCharToMultiByte(
            CP_UTF8,
            WC_ERR_INVALID_CHARS,
            value.data(),
            static_cast<int>(value.size()),
            nullptr,
            0,
            nullptr,
            nullptr);
        if (required <= 0)
        {
            return {};
        }

        std::string result(static_cast<std::size_t>(required), '\0');
        WideCharToMultiByte(
            CP_UTF8,
            WC_ERR_INVALID_CHARS,
            value.data(),
            static_cast<int>(value.size()),
            result.data(),
            required,
            nullptr,
            nullptr);
        return result;
    }
}

namespace xbpreview
{
    PreviewEngine::PreviewEngine(
        const HWND previewHwnd,
        const HWND exclusionHwnd,
        const XbPreviewCreateOptions& options)
        : previewHwnd_(previewHwnd),
        exclusionHwnd_(exclusionHwnd),
        allowWarp_(options.allowWarp != 0),
        framePoolBufferCount_((std::clamp)(options.framePoolBufferCount, 2u, 4u)),
        statsIntervalMilliseconds_((std::clamp)(
            options.statsIntervalMilliseconds,
            250u,
            5000u)),
        diagnosticLogDirectory_(
            options.diagnosticLogDirectory != nullptr
            ? options.diagnosticLogDirectory
            : L""),
        audioEndpointLevelMonitor_(
            [this]
            {
                return GetAudioEndpointLevelAssignment();
            })
    {
        LARGE_INTEGER frequency{};
        if (!QueryPerformanceFrequency(&frequency) || frequency.QuadPart <= 0)
        {
            throw winrt::hresult_error(E_FAIL, L"QueryPerformanceFrequency 失败。");
        }
        qpcFrequency_ = frequency.QuadPart;

        RECT client{};
        if (!GetClientRect(previewHwnd_, &client))
        {
            throw winrt::hresult_error(
                HRESULT_FROM_WIN32(GetLastError()),
                L"无法读取预览窗口尺寸。");
        }
        requestedPreviewWidth_ = static_cast<std::uint32_t>(
            (std::max)(1L, client.right - client.left));
        requestedPreviewHeight_ = static_cast<std::uint32_t>(
            (std::max)(1L, client.bottom - client.top));

        stats_.structSize = sizeof(XbPreviewStats);
        stats_.apiVersion = XB_PREVIEW_API_VERSION;
        stats_.state = XbPreviewState_Stopped;
        stats_.previewWidth = requestedPreviewWidth_;
        stats_.previewHeight = requestedPreviewHeight_;
        ResetCursorStats();
        ApplyWindowDisplayAffinity();
        (void)microphoneDeviceMonitor_.Start();
        // Endpoint metering is best-effort observation. Its failure must not
        // become Preview or Recording startup failure.
        (void)audioEndpointLevelMonitor_.Start();
    }

    PreviewEngine::~PreviewEngine()
    {
        Stop();
        microphonePreflightLevelMonitor_.Stop();
        audioEndpointLevelMonitor_.Stop();
        microphoneDeviceMonitor_.Stop();
    }

    XbPreviewResult PreviewEngine::Start() noexcept
    {
        try
        {
            std::unique_lock lock(lifecycleMutex_);
            if (!stateMachine_.BeginStart())
            {
                SetError(
                    XbPreviewResult_InvalidState,
                    L"只有 Stopped 状态可以 Start；请先 Stop。");
                return XbPreviewResult_InvalidState;
            }

            const auto geometryResult = sessionGeometryStore_.Activate();
            if (geometryResult != XbPreviewResult_Ok)
            {
                stateMachine_.MarkStopped();
                SetError(
                    geometryResult,
                    L"Start requires a valid configured SessionGeometryV1.");
                return geometryResult;
            }

            ResetSessionStats();
            ResetCursorStats();
            stopRequested_.store(false);
            acceptingFrames_.store(false);
            captureClosed_.store(false);
            callbackFailed_.store(false);
            firstFrameDiagnosticWritten_.store(false);
            SetState(XbPreviewState_Starting);

            if (worker_.joinable())
            {
                lock.unlock();
                worker_.join();
                lock.lock();
            }

            worker_ = std::thread(&PreviewEngine::WorkerMain, this);
            const auto completed = lifecycleCondition_.wait_for(
                lock,
                StartTimeout,
                [this]
                {
                    const auto state = stateMachine_.State();
                    return state == XbPreviewState_Running ||
                        state == XbPreviewState_Error;
                });

            if (!completed)
            {
                stopRequested_.store(true);
                acceptingFrames_.store(false);
                frameCondition_.notify_all();
                lock.unlock();
                if (worker_.joinable())
                {
                    worker_.join();
                }
                lock.lock();
                stateMachine_.MarkStopped();
                sessionGeometryStore_.EndSession();
                SetState(XbPreviewState_Stopped);
                SetError(
                    XbPreviewResult_Timeout,
                    L"启动 WGC 预览超过 15 秒，已安全停止。");
                return XbPreviewResult_Timeout;
            }

            if (stateMachine_.State() == XbPreviewState_Error)
            {
                const auto result = static_cast<XbPreviewResult>(stats_.lastResult);
                lock.unlock();
                if (worker_.joinable())
                {
                    worker_.join();
                }
                return result;
            }

            StartMicPreflightLocked();
            return XbPreviewResult_Ok;
        }
        catch (...)
        {
            SetError(
                XbPreviewResult_NativeFailure,
                L"Start 的本机状态切换发生未知异常。");
            return XbPreviewResult_NativeFailure;
        }
    }

    XbPreviewResult PreviewEngine::Stop() noexcept
    {
        try
        {
            {
                std::lock_guard lock(lifecycleMutex_);
                if (stateMachine_.State() == XbPreviewState_Stopped &&
                    !worker_.joinable())
                {
                    return XbPreviewResult_Ok;
                }

                stateMachine_.BeginStop();
                SetState(XbPreviewState_Stopping);
                acceptingFrames_.store(false);
                stopRequested_.store(true);
                microphonePreflightLevelMonitor_.Stop();
            }

            frameCondition_.notify_all();
            lifecycleCondition_.notify_all();

            if (worker_.joinable() &&
                worker_.get_id() != std::this_thread::get_id())
            {
                worker_.join();
            }

            {
                std::lock_guard lock(lifecycleMutex_);
                activeMicrophoneDevice_.reset();
                activeSystemAudioEndpointId_.clear();
                stateMachine_.MarkStopped();
                sessionGeometryStore_.EndSession();
                SetState(XbPreviewState_Stopped);
            }
            lifecycleCondition_.notify_all();
            return XbPreviewResult_Ok;
        }
        catch (...)
        {
            SetError(
                XbPreviewResult_NativeFailure,
                L"Stop 发生未知异常；已继续执行 best-effort 清理。");
            return XbPreviewResult_NativeFailure;
        }
    }

    XbPreviewResult PreviewEngine::StartRecording()
    {
        std::lock_guard lock(lifecycleMutex_);
        if (stateMachine_.State() != XbPreviewState_Running)
        {
            constexpr auto message =
                L"Recording requires an active Preview session.";
            renderer_.RecordRecordingFailure(
                XbPreviewResult_InvalidState,
                HRESULT_FROM_WIN32(ERROR_INVALID_STATE),
                message);
            SetError(XbPreviewResult_InvalidState, message);
            return XbPreviewResult_InvalidState;
        }

        GUID sessionGuid{};
        const auto guidResult = CoCreateGuid(&sessionGuid);
        if (FAILED(guidResult))
        {
            renderer_.RecordRecordingFailure(
                XbPreviewResult_NativeFailure,
                guidResult,
                L"Failed to allocate a recording session identifier.");
            SetErrorFromHresult(
                XbPreviewResult_NativeFailure,
                guidResult,
                L"CoCreateGuid for recording");
            return XbPreviewResult_NativeFailure;
        }

        std::wstring recordingOutputRoot;
        std::uint32_t recordingFrameRate{};
        {
            std::lock_guard productLock(productConfigurationMutex_);
            recordingOutputRoot = productRecordingOutputRoot_;
            recordingFrameRate = productRecordingFrameRate_;
        }
        auto configuration = CreateRecordingConfiguration(
            diagnosticLogDirectory_,
            GuidToString(sessionGuid),
            recordingOutputRoot);
        configuration.frameRate = recordingFrameRate;
        configuration.bitrate = RecordingVideoTargetBitrate(
            activeCrop_.outputWidth,
            activeCrop_.outputHeight,
            configuration.frameRate);
        if (configuration.bitrate == 0)
        {
            renderer_.RecordRecordingFailure(
                XbPreviewResult_InvalidArgument,
                E_INVALIDARG,
                L"The recording video bitrate policy rejected the locked "
                    L"OutputCanvas dimensions or frame rate.");
            SetError(
                XbPreviewResult_InvalidArgument,
                L"The locked recording video configuration is invalid.");
            StartMicPreflightLocked();
            return XbPreviewResult_InvalidArgument;
        }
        if (recordingAudioProgramMode_.has_value())
        {
            ApplyAudioProgramMode(
                configuration, *recordingAudioProgramMode_);
        }
        const auto microphoneRequired =
            configuration.audioMode == GStreamerAudioMode::MicrophoneOnly ||
            configuration.audioMode == GStreamerAudioMode::Dual;
        const auto systemRequired =
            configuration.audioMode == GStreamerAudioMode::SystemOnly ||
            configuration.audioMode == GStreamerAudioMode::Dual;
        // Recording handoff is synchronous and ordered: the idle-only source
        // reaches NULL, its worker joins, and its GstDevice reference is
        // released before the formal graph may create a microphone source.
        microphonePreflightLevelMonitor_.Stop();
        activeMicrophoneDevice_.reset();
        activeSystemAudioEndpointId_.clear();
        if (systemRequired)
        {
            // Lock the endpoint identity selected by the existing GStreamer
            // monitor for the same recording boundary. The capture graph is
            // unchanged and continues to own its activation.
            const auto endpoints = microphoneDeviceMonitor_.Snapshot();
            if (endpoints.defaultSystemAvailable)
            {
                activeSystemAudioEndpointId_ =
                    endpoints.defaultSystemEndpointId;
            }
        }
        if (microphoneRequired)
        {
            // Reuse the one product GstDevice resolver for both preflight and
            // recording. The exact retained binding is passed to the existing
            // formal graph, which still creates and verifies its own source.
            activeMicrophoneDevice_ = microphoneSelectionKind_ ==
                    XbMicrophoneSelectionKindV1_WindowsDefault
                ? microphoneDeviceMonitor_.LockDefault()
                : microphoneDeviceMonitor_.LockEndpoint(
                    microphoneSelectionEndpointId_);
            if (activeMicrophoneDevice_ == nullptr)
            {
                constexpr auto message =
                    L"MicUnavailableAtStart: 当前选择的麦克风不可用，请重新连接或选择其他麦克风。";
                renderer_.RecordRecordingFailure(
                    XbPreviewResult_DeviceLost,
                    HRESULT_FROM_WIN32(ERROR_NOT_FOUND),
                    message);
                SetError(XbPreviewResult_DeviceLost, message);
                StartMicPreflightLocked();
                return XbPreviewResult_DeviceLost;
            }
            configuration.microphoneDevice = activeMicrophoneDevice_;
        }
        if (!configuration.enabled)
        {
            renderer_.RecordRecordingFailure(
                XbPreviewResult_NativeFailure,
                E_FAIL,
                L"Failed to create the recording output configuration.");
            SetError(
                XbPreviewResult_NativeFailure,
                L"Failed to create the recording output configuration.");
            StartMicPreflightLocked();
            return XbPreviewResult_NativeFailure;
        }
        const auto result = renderer_.StartRecording(configuration);
        if (result != XbPreviewResult_Ok)
        {
            activeMicrophoneDevice_.reset();
            activeSystemAudioEndpointId_.clear();
            SetError(result, L"Native recording could not be started.");
            StartMicPreflightLocked();
        }
        return result;
    }

    XbPreviewResult PreviewEngine::PauseRecording()
    {
        std::lock_guard lock(lifecycleMutex_);
        if (stateMachine_.State() != XbPreviewState_Running)
        {
            return XbPreviewResult_InvalidState;
        }
        const auto result = renderer_.PauseRecording();
        if (result != XbPreviewResult_Ok)
        {
            SetError(result, L"Native recording pause command was rejected.");
        }
        return result;
    }

    XbPreviewResult PreviewEngine::ResumeRecording()
    {
        std::lock_guard lock(lifecycleMutex_);
        if (stateMachine_.State() != XbPreviewState_Running)
        {
            return XbPreviewResult_InvalidState;
        }
        const auto result = renderer_.ResumeRecording();
        if (result != XbPreviewResult_Ok)
        {
            SetError(result, L"Native recording resume command was rejected.");
        }
        return result;
    }

    XbPreviewResult PreviewEngine::SetAudioProgramMode(
        const XbAudioProgramMode mode) noexcept
    {
        AudioProgramMode selected{};
        switch (mode)
        {
        case XbAudioProgramMode_None:
            selected = AudioProgramMode::None;
            break;
        case XbAudioProgramMode_SystemOnly:
            selected = AudioProgramMode::SystemOnly;
            break;
        case XbAudioProgramMode_MicrophoneOnly:
            selected = AudioProgramMode::MicrophoneOnly;
            break;
        case XbAudioProgramMode_Dual:
            selected = AudioProgramMode::Dual;
            break;
        default:
            return XbPreviewResult_InvalidArgument;
        }

        std::lock_guard lock(lifecycleMutex_);
        if (stateMachine_.State() != XbPreviewState_Running)
        {
            return XbPreviewResult_InvalidState;
        }
        XbRecordingSnapshot snapshot{};
        renderer_.GetRecordingSnapshot(snapshot);
        if (snapshot.state == XbRecordingState_Starting ||
            snapshot.state == XbRecordingState_Recording ||
            snapshot.state == XbRecordingState_Pausing ||
            snapshot.state == XbRecordingState_Paused ||
            snapshot.state == XbRecordingState_Resuming ||
            snapshot.state == XbRecordingState_Stopping)
        {
            return XbPreviewResult_InvalidState;
        }
        recordingAudioProgramMode_ = selected;
        StartMicPreflightLocked();
        return XbPreviewResult_Ok;
    }

    XbPreviewResult PreviewEngine::StopRecording()
    {
        std::lock_guard lock(lifecycleMutex_);
        const auto result = renderer_.StopRecording();
        activeMicrophoneDevice_.reset();
        activeSystemAudioEndpointId_.clear();
        // renderer_.StopRecording() is synchronous through the existing audio
        // core teardown/finalization boundary. Only after it returns may idle
        // preflight reacquire the selected GstDevice.
        StartMicPreflightLocked();
        if (result != XbPreviewResult_Ok)
        {
            SetError(result, L"Native recording stop or finalize failed.");
        }
        return result;
    }

    XbPreviewResult PreviewEngine::CancelRecording()
    {
        std::lock_guard lock(lifecycleMutex_);
        const auto result = renderer_.CancelRecording();
        activeMicrophoneDevice_.reset();
        activeSystemAudioEndpointId_.clear();
        // Cancellation shares the synchronous recording teardown boundary.
        // Only after it returns may idle preflight reacquire the selected
        // microphone device.
        StartMicPreflightLocked();
        if (result != XbPreviewResult_Ok)
        {
            SetError(
                result,
                L"Native recording cancellation or cleanup failed.");
        }
        return result;
    }

    XbPreviewResult PreviewEngine::GetRecordingSnapshot(
        XbRecordingSnapshot& snapshot) const
    {
        renderer_.GetRecordingSnapshot(snapshot);
        return XbPreviewResult_Ok;
    }

    XbPreviewResult PreviewEngine::SetAudioControls(
        const XbAudioControlsV1& controls) noexcept
    {
        return renderer_.SetAudioControls(controls);
    }

    XbPreviewResult PreviewEngine::GetAudioControlSnapshot(
        XbAudioControlSnapshotV1& snapshot) const noexcept
    {
        renderer_.GetAudioControlSnapshot(snapshot);
        const auto levels = audioEndpointLevelMonitor_.Snapshot();
        const auto microphone = microphonePreflightLevelMonitor_.Snapshot();
        const auto recordingMicrophoneObserved = levels.microphoneEnabled;
        snapshot.systemPeakAbsolutePcm16 =
            levels.systemPeakAbsolutePcm16;
        snapshot.microphonePeakAbsolutePcm16 =
            recordingMicrophoneObserved
                ? levels.microphonePeakAbsolutePcm16
                : microphone.peakAbsolutePcm16;
        // IAudioMeterInformation exposes a peak only. Preserve the existing
        // RMS field's idle/preflight meaning instead of presenting peak as RMS.
        snapshot.microphoneRmsPcm16 = recordingMicrophoneObserved
            ? 0.0
            : microphone.rmsPcm16;
        snapshot.endpointLevelFlags =
            (levels.systemEnabled
                ? XbAudioEndpointLevelFlagsV1_SystemSourceEnabled
                : 0ull) |
            ((recordingMicrophoneObserved || microphone.enabled)
                ? XbAudioEndpointLevelFlagsV1_MicrophoneSourceEnabled
                : 0ull) |
            (levels.systemAvailable
                ? XbAudioEndpointLevelFlagsV1_SystemMeterAvailable
                : 0ull) |
            ((recordingMicrophoneObserved
                    ? levels.microphoneAvailable
                    : microphone.available)
                ? XbAudioEndpointLevelFlagsV1_MicrophoneMeterAvailable
                : 0ull);
        return XbPreviewResult_Ok;
    }

    XbPreviewResult PreviewEngine::GetMicrophoneDeviceList(
        XbMicrophoneDeviceListV1& list) const noexcept
    {
        try
        {
            const auto catalog = microphoneDeviceMonitor_.Snapshot();
            XbMicrophoneDeviceListV1 value{};
            value.structSize = sizeof(value);
            value.abiVersion = XB_MICROPHONE_DEVICE_ABI_VERSION_V1;
            value.generation = catalog.generation;
            value.deviceCount = static_cast<std::uint32_t>(
                (std::min)(
                    catalog.devices.size(),
                    static_cast<std::size_t>(
                        (std::numeric_limits<std::uint32_t>::max)())));
            value.monitorActive = catalog.monitorActive ? 1u : 0u;
            value.defaultAvailable = catalog.defaultAvailable ? 1u : 0u;
            value.deviceAddedCount = catalog.deviceAddedCount;
            value.deviceRemovedCount = catalog.deviceRemovedCount;
            wcsncpy_s(
                value.defaultEndpointId,
                catalog.defaultEndpointId.c_str(), _TRUNCATE);
            wcsncpy_s(
                value.defaultDisplayName,
                catalog.defaultDisplayName.c_str(), _TRUNCATE);
            list = value;
            return XbPreviewResult_Ok;
        }
        catch (...)
        {
            return XbPreviewResult_NativeFailure;
        }
    }

    XbPreviewResult PreviewEngine::GetMicrophoneDevice(
        XbMicrophoneDeviceV1& device) const noexcept
    {
        try
        {
            const auto catalog = microphoneDeviceMonitor_.Snapshot();
            if (device.generation != catalog.generation)
                return XbPreviewResult_RevisionConflict;
            if (device.index >= catalog.devices.size())
                return XbPreviewResult_InvalidArgument;
            const auto index = device.index;
            XbMicrophoneDeviceV1 value{};
            value.structSize = sizeof(value);
            value.abiVersion = XB_MICROPHONE_DEVICE_ABI_VERSION_V1;
            value.generation = catalog.generation;
            value.index = index;
            value.available = 1;
            wcsncpy_s(
                value.endpointId,
                catalog.devices[index].endpointId.c_str(), _TRUNCATE);
            wcsncpy_s(
                value.displayName,
                catalog.devices[index].displayName.c_str(), _TRUNCATE);
            device = value;
            return XbPreviewResult_Ok;
        }
        catch (...)
        {
            return XbPreviewResult_NativeFailure;
        }
    }

    XbPreviewResult PreviewEngine::SetMicrophoneSelection(
        const XbMicrophoneSelectionV1& selection) noexcept
    {
        try
        {
            if (selection.kind !=
                    XbMicrophoneSelectionKindV1_WindowsDefault &&
                selection.kind !=
                    XbMicrophoneSelectionKindV1_ConcreteEndpoint)
            {
                return XbPreviewResult_InvalidArgument;
            }
            const std::wstring endpointId(
                selection.endpointId,
                wcsnlen_s(selection.endpointId, 512));
            if (selection.kind ==
                    XbMicrophoneSelectionKindV1_ConcreteEndpoint &&
                endpointId.empty())
            {
                return XbPreviewResult_InvalidArgument;
            }
            std::lock_guard lock(lifecycleMutex_);
            XbRecordingSnapshot recording{};
            renderer_.GetRecordingSnapshot(recording);
            if (recording.state == XbRecordingState_Starting ||
                recording.state == XbRecordingState_Recording ||
                recording.state == XbRecordingState_Pausing ||
                recording.state == XbRecordingState_Paused ||
                recording.state == XbRecordingState_Resuming ||
                recording.state == XbRecordingState_Stopping)
            {
                return XbPreviewResult_InvalidState;
            }
            // Device switch ordering is stop/release, mutate product choice,
            // then resolve and start one replacement preflight owner.
            microphonePreflightLevelMonitor_.Stop();
            microphoneSelectionKind_ =
                static_cast<XbMicrophoneSelectionKindV1>(selection.kind);
            if (microphoneSelectionKind_ ==
                XbMicrophoneSelectionKindV1_WindowsDefault)
            {
                // A default choice must not retain any hidden concrete
                // identity. Start resolves and locks the current default.
                microphoneSelectionEndpointId_.clear();
                microphoneSelectionDisplayName_.clear();
            }
            else
            {
                microphoneSelectionEndpointId_ = endpointId;
                microphoneSelectionDisplayName_.assign(
                    selection.displayName,
                    wcsnlen_s(selection.displayName, 256));
            }
            activeMicrophoneDevice_.reset();
            StartMicPreflightLocked();
            return XbPreviewResult_Ok;
        }
        catch (...)
        {
            return XbPreviewResult_NativeFailure;
        }
    }

    XbPreviewResult PreviewEngine::GetMicrophoneSelection(
        XbMicrophoneSelectionSnapshotV1& snapshot) const noexcept
    {
        try
        {
            std::lock_guard lock(lifecycleMutex_);
            auto binding = activeMicrophoneDevice_;
            if (binding == nullptr)
            {
                binding = microphoneSelectionKind_ ==
                        XbMicrophoneSelectionKindV1_WindowsDefault
                    ? microphoneDeviceMonitor_.LockDefault()
                    : microphoneDeviceMonitor_.LockEndpoint(
                        microphoneSelectionEndpointId_);
            }
            XbMicrophoneSelectionSnapshotV1 value{};
            value.structSize = sizeof(value);
            value.abiVersion = XB_MICROPHONE_DEVICE_ABI_VERSION_V1;
            value.kind = microphoneSelectionKind_;
            value.available = binding != nullptr &&
                microphoneDeviceMonitor_.Contains(binding->EndpointId())
                ? 1u
                : 0u;
            value.sessionLocked = activeMicrophoneDevice_ != nullptr ? 1u : 0u;
            const auto endpointId = binding != nullptr
                ? binding->EndpointId()
                : microphoneSelectionEndpointId_;
            const auto displayName = binding != nullptr
                ? binding->DisplayName()
                : microphoneSelectionDisplayName_;
            wcsncpy_s(
                value.endpointId, endpointId.c_str(), _TRUNCATE);
            wcsncpy_s(
                value.displayName, displayName.c_str(), _TRUNCATE);
            snapshot = value;
            return XbPreviewResult_Ok;
        }
        catch (...)
        {
            return XbPreviewResult_NativeFailure;
        }
    }

    void PreviewEngine::RecordRecordingBoundaryFailure(
        const XbPreviewResult result,
        const HRESULT hresult,
        const wchar_t* const message)
    {
        renderer_.RecordRecordingFailure(result, hresult, message);
    }

    XbPreviewResult PreviewEngine::Resize(
        const std::int32_t width,
        const std::int32_t height) noexcept
    {
        if (width <= 0 || height <= 0 || width > 32768 || height > 32768)
        {
            SetError(
                XbPreviewResult_InvalidArgument,
                L"Resize 宽高必须位于 1..32768。");
            return XbPreviewResult_InvalidArgument;
        }

        {
            std::lock_guard lock(resizeMutex_);
            requestedPreviewWidth_ = static_cast<std::uint32_t>(width);
            requestedPreviewHeight_ = static_cast<std::uint32_t>(height);
            ++requestedResizeGeneration_;
        }
        frameCondition_.notify_all();
        return XbPreviewResult_Ok;
    }

    XbPreviewResult PreviewEngine::SetGpuExportTargetSize(
        const std::int32_t width,
        const std::int32_t height) noexcept
    {
        if (width <= 0 || height <= 0 || width > 32768 || height > 32768)
        {
            SetError(
                XbPreviewResult_InvalidArgument,
                L"GPU preview export target size is invalid.");
            return XbPreviewResult_InvalidArgument;
        }
        return renderer_.SetGpuExportTargetSize(
                static_cast<std::uint32_t>(width),
                static_cast<std::uint32_t>(height))
            ? XbPreviewResult_Ok
            : XbPreviewResult_InvalidArgument;
    }

    XbPreviewResult PreviewEngine::SetSessionGeometry(
        const XbPreviewSessionGeometryV1& geometry) noexcept
    {
        std::lock_guard lock(lifecycleMutex_);
        const auto result = sessionGeometryStore_.Configure(
            geometry,
            stateMachine_.State());
        if (result != XbPreviewResult_Ok)
        {
            SetError(
                result,
                L"SessionGeometryV1 configuration was rejected.");
        }
        return result;
    }

    XbPreviewResult PreviewEngine::GetStats(XbPreviewStats& stats) const noexcept
    {
        std::lock_guard lock(statsMutex_);
        stats = stats_;
        stats.structSize = sizeof(XbPreviewStats);
        stats.apiVersion = XB_PREVIEW_API_VERSION;
        return XbPreviewResult_Ok;
    }

    XbPreviewResult PreviewEngine::GetGpuExportFrame(
        XbPreviewGpuExportFrameV1& snapshot) const noexcept
    {
        return renderer_.GetGpuExportFrame(snapshot)
            ? XbPreviewResult_Ok
            : XbPreviewResult_InvalidState;
    }

    XbPreviewResult PreviewEngine::SetCameraState(
        const XbCameraState& cameraState) noexcept
    {
        if (!IsValidCameraState(cameraState))
        {
            (void)cameraStateStore_.Update(cameraState);
            {
                std::lock_guard lock(statsMutex_);
                ++stats_.invalidCameraStateFallbackCount;
            }
            SetError(
                XbPreviewResult_InvalidCameraState,
                L"Camera state invalid; native renderer switched to full-view fallback.");
            return XbPreviewResult_InvalidCameraState;
        }

        {
            std::lock_guard lock(lifecycleMutex_);
            if (stateMachine_.State() != XbPreviewState_Running)
            {
                return XbPreviewResult_InvalidState;
            }
        }

        const auto updateResult = cameraStateStore_.Update(cameraState);
        if (updateResult != XbPreviewResult_Ok)
        {
            return updateResult;
        }
        {
            std::lock_guard lock(statsMutex_);
            ++stats_.cameraUpdateCount;
        }
        return XbPreviewResult_Ok;
    }

    XbPreviewResult PreviewEngine::SetCursorMode(
        const XbCursorMode cursorMode) noexcept
    {
        if (!IsValidCursorMode(cursorMode))
        {
            return XbPreviewResult_InvalidArgument;
        }

        {
            std::lock_guard lock(lifecycleMutex_);
            if (stateMachine_.State() != XbPreviewState_Stopped)
            {
                return XbPreviewResult_InvalidState;
            }
            requestedCursorMode_ = cursorMode;
        }

        std::lock_guard lock(cursorStatsMutex_);
        cursorStats_.requestedMode = cursorMode;
        cursorStats_.actualMode = XbCursorMode_SystemCursor;
        cursorStats_.systemCursorIncluded = 1;
        cursorStats_.customCursorLayerActive = 0;
        return XbPreviewResult_Ok;
    }

    XbPreviewResult PreviewEngine::SetRecordCursorVisible(
        const bool visible) noexcept
    {
        try
        {
            std::lock_guard lifecycleLock(lifecycleMutex_);
            const auto state = stateMachine_.State();
            if (state == XbPreviewState_Starting ||
                state == XbPreviewState_Stopping)
            {
                return XbPreviewResult_InvalidState;
            }

            const auto previous = recordCursorVisible_.load(
                std::memory_order_acquire);
            if (state != XbPreviewState_Running)
            {
                recordCursorVisible_.store(visible, std::memory_order_release);
                appliedRecordCursorVisible_.store(
                    visible, std::memory_order_release);
                if (previous != visible)
                {
                    cursorPresentationRevision_.fetch_add(
                        1, std::memory_order_acq_rel);
                }
                return XbPreviewResult_Ok;
            }

            if (cursorModeDecision_.actual == XbCursorMode_CustomCursor)
            {
                recordCursorVisible_.store(visible, std::memory_order_release);
                appliedRecordCursorVisible_.store(
                    visible, std::memory_order_release);
                if (previous != visible)
                {
                    cursorPresentationRevision_.fetch_add(
                        1, std::memory_order_acq_rel);
                }
                return XbPreviewResult_Ok;
            }

            bool propertyAvailable = false;
            {
                std::lock_guard statsLock(cursorStatsMutex_);
                propertyAvailable =
                    cursorStats_.wgcCursorPropertyAvailable != 0;
            }
            if (!propertyAvailable || !captureSession_)
            {
                cursorPresentationFailureCount_.fetch_add(
                    1, std::memory_order_acq_rel);
                SetError(
                    XbPreviewResult_CursorModeUnavailable,
                    L"WGC cursor presentation cannot change on this session.");
                return XbPreviewResult_CursorModeUnavailable;
            }

            HRESULT settingResult = S_OK;
            std::uint32_t settingLastError = ERROR_SUCCESS;
            bool applied = appliedRecordCursorVisible_.load(
                std::memory_order_acquire);
            try
            {
                captureSession_.IsCursorCaptureEnabled(visible);
                applied = captureSession_.IsCursorCaptureEnabled();
            }
            catch (const winrt::hresult_error& error)
            {
                settingResult = error.code();
                settingLastError = HRESULT_FACILITY(settingResult) ==
                    FACILITY_WIN32
                    ? HRESULT_CODE(settingResult)
                    : ERROR_SUCCESS;
                try
                {
                    applied = captureSession_.IsCursorCaptureEnabled();
                }
                catch (...)
                {
                }
            }
            catch (...)
            {
                settingResult = E_FAIL;
            }

            {
                std::lock_guard statsLock(cursorStatsMutex_);
                cursorStats_.wgcCursorSettingResult = settingResult;
                cursorStats_.wgcCursorSettingLastError = settingLastError;
                cursorStats_.systemCursorIncluded = applied ? 1u : 0u;
            }
            appliedRecordCursorVisible_.store(
                applied, std::memory_order_release);
            if (FAILED(settingResult) || applied != visible)
            {
                cursorPresentationFailureCount_.fetch_add(
                    1, std::memory_order_acq_rel);
                SetError(
                    XbPreviewResult_CursorModeUnavailable,
                    L"WGC rejected the requested runtime cursor visibility.");
                return XbPreviewResult_CursorModeUnavailable;
            }

            recordCursorVisible_.store(visible, std::memory_order_release);
            if (previous != visible)
            {
                cursorPresentationRevision_.fetch_add(
                    1, std::memory_order_acq_rel);
            }
            return XbPreviewResult_Ok;
        }
        catch (...)
        {
            cursorPresentationFailureCount_.fetch_add(
                1, std::memory_order_acq_rel);
            SetError(
                XbPreviewResult_CursorModeUnavailable,
                L"Record cursor visibility change failed unexpectedly.");
            return XbPreviewResult_CursorModeUnavailable;
        }
    }

    XbPreviewResult PreviewEngine::GetRecordCursorVisible(
        std::uint32_t& requestedVisible,
        std::uint32_t& appliedVisible,
        std::uint64_t& revision) const noexcept
    {
        requestedVisible = recordCursorVisible_.load(
            std::memory_order_acquire) ? 1u : 0u;
        appliedVisible = appliedRecordCursorVisible_.load(
            std::memory_order_acquire) ? 1u : 0u;
        revision = cursorPresentationRevision_.load(
            std::memory_order_acquire);
        return XbPreviewResult_Ok;
    }

    XbPreviewResult PreviewEngine::GetCursorStats(
        XbCursorStats& stats) const noexcept
    {
        std::lock_guard lock(cursorStatsMutex_);
        stats = cursorStats_;
        stats.structSize = sizeof(XbCursorStats);
        stats.apiVersion = XB_PREVIEW_API_VERSION;
        stats.reserved1 = recordCursorVisible_.load(
            std::memory_order_acquire) ? 1u : 0u;
        stats.reserved2 = appliedRecordCursorVisible_.load(
            std::memory_order_acquire) ? 1u : 0u;
        stats.reserved3 = cursorPresentationRevision_.load(
            std::memory_order_acquire);
        stats.reserved4 = cursorPresentationFailureCount_.load(
            std::memory_order_acquire);
        return XbPreviewResult_Ok;
    }

    XbPreviewResult PreviewEngine::SetCaptureTarget(
        const XbCaptureTargetKind targetKind,
        const HWND window) noexcept
    {
        if (!IsValidCaptureTargetKind(targetKind))
        {
            return XbPreviewResult_InvalidArgument;
        }

        std::lock_guard lock(lifecycleMutex_);
        if (stateMachine_.State() != XbPreviewState_Stopped)
        {
            return XbPreviewResult_InvalidState;
        }

        if (targetKind == XbCaptureTargetKind_Monitor)
        {
            captureTarget_ = {};
            return XbPreviewResult_Ok;
        }

        DWORD processId{};
        if (window == nullptr || !IsWindow(window) ||
            GetAncestor(window, GA_ROOT) != window ||
            !IsWindowVisible(window) ||
            GetWindowThreadProcessId(window, &processId) == 0 ||
            processId == GetCurrentProcessId())
        {
            return XbPreviewResult_InvalidWindow;
        }

        captureTarget_ = CaptureTarget{
            XbCaptureTargetKind_Window,
            window
        };
        return XbPreviewResult_Ok;
    }

    XbPreviewResult PreviewEngine::SetWindowStagePose(
        const XbWindowStageOrientation orientation,
        const XbWindowStageLevel level) noexcept
    {
        WindowStageDirection direction{};
        switch (orientation)
        {
        case XbWindowStageOrientation_Left:
            direction = WindowStageDirection::Left;
            break;
        case XbWindowStageOrientation_Front:
            direction = WindowStageDirection::Front;
            break;
        case XbWindowStageOrientation_Right:
            direction = WindowStageDirection::Right;
            break;
        default:
            return XbPreviewResult_InvalidArgument;
        }
        WindowStageStrength strength{};
        switch (level)
        {
        case XbWindowStageLevel_Level1:
            strength = WindowStageStrength::Level1;
            break;
        case XbWindowStageLevel_Level2:
            strength = WindowStageStrength::Level2;
            break;
        case XbWindowStageLevel_Level3:
            strength = WindowStageStrength::Level3;
            break;
        default:
            return XbPreviewResult_InvalidArgument;
        }

        std::lock_guard lifecycleLock(lifecycleMutex_);
        const auto state = stateMachine_.State();
        if (state != XbPreviewState_Stopped && state != XbPreviewState_Running)
        {
            return XbPreviewResult_InvalidState;
        }
        std::lock_guard productLock(productConfigurationMutex_);
        if (state == XbPreviewState_Running)
        {
            const auto result = renderer_.SetWindowStagePose(
                direction, strength);
            if (result != XbPreviewResult_Ok)
            {
                SetError(result, L"The frozen Window Stage pose was rejected.");
                return result;
            }
        }
        productStageDirection_ = direction;
        productStageStrength_ = strength;
        return XbPreviewResult_Ok;
    }

    XbPreviewResult PreviewEngine::SetWindowShowcasePose(
        const XbWindowStageOrientation orientation,
        const XbWindowStageLevel level,
        const std::uint32_t active) noexcept
    {
        if (active > 1)
        {
            return XbPreviewResult_InvalidArgument;
        }
        WindowStageDirection direction{};
        switch (orientation)
        {
        case XbWindowStageOrientation_Left:
            direction = WindowStageDirection::Left;
            break;
        case XbWindowStageOrientation_Front:
            direction = WindowStageDirection::Front;
            break;
        case XbWindowStageOrientation_Right:
            direction = WindowStageDirection::Right;
            break;
        default:
            return XbPreviewResult_InvalidArgument;
        }

        WindowStageStrength strength{};
        switch (level)
        {
        case XbWindowStageLevel_Level1:
            strength = WindowStageStrength::Level1;
            break;
        case XbWindowStageLevel_Level2:
            strength = WindowStageStrength::Level2;
            break;
        case XbWindowStageLevel_Level3:
            strength = WindowStageStrength::Level3;
            break;
        default:
            return XbPreviewResult_InvalidArgument;
        }

        std::lock_guard lifecycleLock(lifecycleMutex_);
        const auto state = stateMachine_.State();
        if (state != XbPreviewState_Stopped && state != XbPreviewState_Running)
        {
            return XbPreviewResult_InvalidState;
        }
        std::lock_guard productLock(productConfigurationMutex_);
        if (state == XbPreviewState_Running)
        {
            const auto result = active != 0
                ? renderer_.SetWindowShowcasePose(direction, strength)
                : renderer_.RequestWindowShowcaseReturn();
            if (result != XbPreviewResult_Ok)
            {
                SetError(
                    result,
                    L"The frozen Window Showcase pose was rejected.");
                return result;
            }
        }
        productStageDirection_ = direction;
        productStageStrength_ = strength;
        productStageActive_ = active != 0;
        return XbPreviewResult_Ok;
    }

    XbPreviewResult PreviewEngine::SetWindowShowcaseBackgroundPreset(
        const XbWindowShowcaseBackgroundPreset preset) noexcept
    {
        WindowShowcaseBackgroundPreset selected{};
        switch (preset)
        {
        case XbWindowShowcaseBackgroundPreset_Warm:
            selected = WindowShowcaseBackgroundPreset::Warm;
            break;
        case XbWindowShowcaseBackgroundPreset_Art01:
            selected = WindowShowcaseBackgroundPreset::Art01;
            break;
        case XbWindowShowcaseBackgroundPreset_Art001:
            selected = WindowShowcaseBackgroundPreset::Art001;
            break;
        default:
            return XbPreviewResult_InvalidArgument;
        }

        std::lock_guard lifecycleLock(lifecycleMutex_);
        const auto state = stateMachine_.State();
        if (state != XbPreviewState_Stopped && state != XbPreviewState_Running)
        {
            return XbPreviewResult_InvalidState;
        }
        std::lock_guard productLock(productConfigurationMutex_);
        if (state == XbPreviewState_Running)
        {
            const auto result =
                renderer_.SetWindowShowcaseBackgroundPreset(selected);
            if (result != XbPreviewResult_Ok)
            {
                SetError(
                    result,
                    L"The packaged background preset could not be loaded; "
                    L"the prior active background remains selected.");
                return result;
            }
        }
        productBackgroundPreset_ = selected;
        productCustomBackground_ = false;
        productCustomBackgroundPath_.clear();
        return XbPreviewResult_Ok;
    }

    XbPreviewResult PreviewEngine::SetWindowShowcaseCustomBackground(
        const wchar_t* const validatedLocalPath)
    {
        if (!IsSupportedCustomBackgroundPath(validatedLocalPath))
        {
            SetError(
                XbPreviewResult_InvalidArgument,
                L"Custom background must be an existing local PNG, JPEG, "
                L"or BMP file.");
            return XbPreviewResult_InvalidArgument;
        }

        std::lock_guard lifecycleLock(lifecycleMutex_);
        const auto state = stateMachine_.State();
        if (state != XbPreviewState_Stopped && state != XbPreviewState_Running)
        {
            return XbPreviewResult_InvalidState;
        }
        std::lock_guard productLock(productConfigurationMutex_);
        if (state == XbPreviewState_Running)
        {
            const auto result = renderer_.SetWindowShowcaseCustomBackground(
                validatedLocalPath);
            if (result != XbPreviewResult_Ok)
            {
                SetError(
                    result,
                    L"Custom background decode failed; the prior active "
                    L"background remains selected.");
                return result;
            }
        }
        productCustomBackground_ = true;
        productCustomBackgroundPath_ = validatedLocalPath;
        return XbPreviewResult_Ok;
    }

    XbPreviewResult PreviewEngine::SetRecordingOutputRoot(
        const wchar_t* const validatedLocalPath)
    {
        std::wstring selected = validatedLocalPath == nullptr
            ? std::wstring{}
            : std::wstring(validatedLocalPath);
        if (!selected.empty())
        {
            const std::filesystem::path path(selected);
            if (!IsLocalAbsolutePath(path))
            {
                SetError(
                    XbPreviewResult_InvalidArgument,
                    L"Recording output root must be an absolute local path.");
                return XbPreviewResult_InvalidArgument;
            }
            const auto storage = ProbeRecordingStorageForStart(
                selected, RecordingVideoBitrate);
            if (!storage.CanStart())
            {
                SetError(
                    XbPreviewResult_NativeFailure,
                    RecordingStorageUserMessage(storage.status));
                return XbPreviewResult_NativeFailure;
            }
            const auto pathSafety = InspectPathForReadOnly(
                path,
                path,
                PathSafetyExpectedType::Directory);
            if (!pathSafety.SafeForReadOnlyInspection())
            {
                SetError(
                    XbPreviewResult_NativeFailure,
                    RecordingStorageUserMessage(
                        RecordingStorageStatus::DestinationNotWritable));
                return XbPreviewResult_NativeFailure;
            }
            std::error_code error;
            selected = std::filesystem::weakly_canonical(path, error).wstring();
            if (error || selected.empty())
            {
                return XbPreviewResult_InvalidArgument;
            }
        }

        std::lock_guard lifecycleLock(lifecycleMutex_);
        const auto state = stateMachine_.State();
        if (state != XbPreviewState_Stopped && state != XbPreviewState_Running)
        {
            return XbPreviewResult_InvalidState;
        }
        if (state == XbPreviewState_Running)
        {
            XbRecordingSnapshot snapshot{};
            renderer_.GetRecordingSnapshot(snapshot);
            if (snapshot.state == XbRecordingState_Starting ||
                snapshot.state == XbRecordingState_Recording ||
                snapshot.state == XbRecordingState_Stopping ||
                snapshot.state == XbRecordingState_Pausing ||
                snapshot.state == XbRecordingState_Paused ||
                snapshot.state == XbRecordingState_Resuming)
            {
                return XbPreviewResult_InvalidState;
            }
        }
        std::lock_guard productLock(productConfigurationMutex_);
        productRecordingOutputRoot_ = std::move(selected);
        return XbPreviewResult_Ok;
    }

    XbPreviewResult PreviewEngine::SetRecordingFrameRate(
        const std::uint32_t framesPerSecond) noexcept
    {
        if (!IsSupportedVideoEncoderFrameRate(framesPerSecond))
        {
            return XbPreviewResult_InvalidArgument;
        }
        std::lock_guard lifecycleLock(lifecycleMutex_);
        const auto state = stateMachine_.State();
        if (state != XbPreviewState_Stopped && state != XbPreviewState_Running)
        {
            return XbPreviewResult_InvalidState;
        }
        if (state == XbPreviewState_Running)
        {
            XbRecordingSnapshot snapshot{};
            renderer_.GetRecordingSnapshot(snapshot);
            if (snapshot.state == XbRecordingState_Starting ||
                snapshot.state == XbRecordingState_Recording ||
                snapshot.state == XbRecordingState_Stopping ||
                snapshot.state == XbRecordingState_Pausing ||
                snapshot.state == XbRecordingState_Paused ||
                snapshot.state == XbRecordingState_Resuming)
            {
                return XbPreviewResult_InvalidState;
            }
        }
        std::lock_guard productLock(productConfigurationMutex_);
        productRecordingFrameRate_ = framesPerSecond;
        return XbPreviewResult_Ok;
    }

    std::wstring PreviewEngine::LastError() const
    {
        std::lock_guard lock(errorMutex_);
        return lastError_;
    }

    void PreviewEngine::WorkerMain() noexcept
    {
        bool apartmentInitialized = false;
        startupDiagnostics_.Reset();
        try
        {
            XB_STARTUP_STEP(
                startupDiagnostics_,
                "WorkerFoundation",
                "InitializeWorkerApartment",
                "winrt::init_apartment",
                [&]
                {
                    winrt::init_apartment(
                        winrt::apartment_type::multi_threaded);
                });
            apartmentInitialized = true;
            InitializeWorkerResources();

            XB_STARTUP_STEP(
                startupDiagnostics_,
                "WorkerReady",
                "CommitRunningState",
                "PreviewStateMachine::MarkRunning",
                [&]
                {
                std::lock_guard lock(lifecycleMutex_);
                if (!stateMachine_.MarkRunning())
                {
                    throw winrt::hresult_error(
                        E_UNEXPECTED,
                        L"启动完成前状态已经改变。");
                }
                SetState(XbPreviewState_Running);
                });
            logger_.WriteEvent("running", XbPreviewState_Running);
            startupDiagnostics_.WriteSummary();
            lifecycleCondition_.notify_all();

            WorkerLoop();
            ShutdownWorkerResources();

            {
                std::lock_guard lock(lifecycleMutex_);
                stateMachine_.MarkStopped();
                SetState(XbPreviewState_Stopped);
            }
            lifecycleCondition_.notify_all();
        }
        catch (const winrt::hresult_error& error)
        {
            startupDiagnostics_.CaptureUnhandled(
                error.code(),
                "winrt::hresult_error",
                DiagnosticLogger::ToUtf8(error.message()));
            auto result = XbPreviewResult_NativeFailure;
            {
                std::lock_guard lock(statsMutex_);
                if (stats_.lastResult == XbPreviewResult_HdrUnsupported ||
                    stats_.lastResult == XbPreviewResult_WgcUnsupported ||
                    stats_.lastResult == XbPreviewResult_WindowTargetClosed ||
                    stats_.lastResult ==
                        XbPreviewResult_GeometrySourceMismatch)
                {
                    result = static_cast<XbPreviewResult>(stats_.lastResult);
                }
            }
            if (error.code() == DXGI_ERROR_DEVICE_REMOVED ||
                error.code() == DXGI_ERROR_DEVICE_RESET ||
                error.code() == DXGI_ERROR_DEVICE_HUNG)
            {
                result = XbPreviewResult_DeviceLost;
            }
            SetErrorFromHresult(
                result,
                error.code(),
                error.message().c_str());
            ShutdownWorkerResources();
            {
                std::lock_guard lock(lifecycleMutex_);
                stateMachine_.MarkError();
                SetState(XbPreviewState_Error);
            }
            lifecycleCondition_.notify_all();
        }
        catch (const std::exception& error)
        {
            startupDiagnostics_.CaptureUnhandled(
                std::nullopt,
                "std::exception",
                error.what());
            std::wstring message = L"原生 worker 异常：";
            const auto text = error.what();
            const auto length = static_cast<int>(std::strlen(text));
            const auto required = MultiByteToWideChar(
                CP_UTF8,
                0,
                text,
                length,
                nullptr,
                0);
            if (required > 0)
            {
                std::wstring converted(static_cast<std::size_t>(required), L'\0');
                MultiByteToWideChar(
                    CP_UTF8,
                    0,
                    text,
                    length,
                    converted.data(),
                    required);
                message += converted;
            }
            SetError(XbPreviewResult_NativeFailure, message);
            ShutdownWorkerResources();
            {
                std::lock_guard lock(lifecycleMutex_);
                stateMachine_.MarkError();
                SetState(XbPreviewState_Error);
            }
            lifecycleCondition_.notify_all();
        }
        catch (...)
        {
            startupDiagnostics_.CaptureUnhandled(
                std::nullopt,
                "unknown",
                "unknown");
            SetError(
                XbPreviewResult_NativeFailure,
                L"原生 worker 发生未知异常。");
            ShutdownWorkerResources();
            {
                std::lock_guard lock(lifecycleMutex_);
                stateMachine_.MarkError();
                SetState(XbPreviewState_Error);
            }
            lifecycleCondition_.notify_all();
        }

        if (apartmentInitialized)
        {
            winrt::uninit_apartment();
        }
    }

    void PreviewEngine::InitializeWorkerResources()
    {
        XB_STARTUP_STEP(
            startupDiagnostics_,
            "WorkerFoundation",
            "CreateSessionGuid",
            "CoCreateGuid",
            [&]
            {
                winrt::check_hresult(CoCreateGuid(&sessionGuid_));
            });
        sessionGuidString_ = GuidToString(sessionGuid_);

        {
            std::array<std::uint64_t, 2> words{};
            static_assert(sizeof(words) == sizeof(sessionGuid_));
            std::memcpy(words.data(), &sessionGuid_, sizeof(sessionGuid_));
            std::lock_guard lock(statsMutex_);
            stats_.sessionIdHigh = words[0];
            stats_.sessionIdLow = words[1];
        }

        std::wstring logError;
        XB_STARTUP_STEP(
            startupDiagnostics_,
            "Diagnostics",
            "CreateP0DiagnosticLogger",
            "DiagnosticLogger::Open",
            [&]
            {
                if (!logger_.Open(
                        diagnosticLogDirectory_,
                        sessionGuidString_,
                        logError))
                {
                    throw winrt::hresult_error(E_FAIL, logError);
                }
            });
        startupDiagnostics_.Attach(
            logger_,
            DiagnosticLogger::ToUtf8(sessionGuidString_));
        {
            std::lock_guard lock(statsMutex_);
            const auto logFilePath = logger_.FilePath();
            wcsncpy_s(
                stats_.logFilePath,
                logFilePath.c_str(),
                _TRUNCATE);
        }
        XB_STARTUP_STEP(
            startupDiagnostics_,
            "Diagnostics",
            "WriteStartingEvent",
            "DiagnosticLogger::WriteEvent",
            [&]
            {
                logger_.WriteEvent("starting", XbPreviewState_Starting);
            });
        {
            std::ostringstream detail;
            detail << "kind="
                << (captureTarget_.IsWindow() ? "Window" : "Monitor")
                << ";hwnd=0x" << std::uppercase << std::hex
                << reinterpret_cast<std::uintptr_t>(captureTarget_.window);
            logger_.WriteEvent(
                "capture-target",
                XbPreviewState_Starting,
                detail.str());
        }

        std::wstring cursorLogError;
        const bool cursorLogReady = cursorLogger_.Open(
            diagnosticLogDirectory_,
            sessionGuidString_,
            cursorLogError);
        if (cursorLogReady)
        {
            std::lock_guard lock(cursorStatsMutex_);
            wcsncpy_s(
                cursorStats_.logFilePath,
                cursorLogger_.FilePath().c_str(),
                _TRUNCATE);
        }
        else
        {
            logger_.WriteEvent(
                "cursor-log-open-failed",
                XbPreviewState_Starting);
        }

        XB_STARTUP_STEP(
            startupDiagnostics_,
            "WgcCapability",
            "CheckGraphicsCaptureSupport",
            "GraphicsCaptureSession::IsSupported",
            [&]
            {
                if (!winrt::Windows::Graphics::Capture::
                        GraphicsCaptureSession::IsSupported())
                {
                    SetError(
                        XbPreviewResult_WgcUnsupported,
                        L"当前系统不支持 Windows.Graphics.Capture。");
                    throw winrt::hresult_error(
                        E_NOTIMPL,
                        L"当前系统不支持 Windows.Graphics.Capture。");
                }
            });

        std::uint32_t previewWidth{};
        std::uint32_t previewHeight{};
        {
            std::lock_guard lock(resizeMutex_);
            previewWidth = requestedPreviewWidth_;
            previewHeight = requestedPreviewHeight_;
            appliedResizeGeneration_ = requestedResizeGeneration_;
        }

        const auto monitor = PrimaryMonitor();
        MONITORINFO monitorInfo{};
        monitorInfo.cbSize = sizeof(monitorInfo);
        XB_STARTUP_STEP(
            startupDiagnostics_,
            "CaptureTarget",
            "ReadPrimaryMonitorInfo",
            "GetMonitorInfoW",
            [&]
            {
                if (!GetMonitorInfoW(monitor, &monitorInfo))
                {
                    throw winrt::hresult_error(
                        HRESULT_FROM_WIN32(GetLastError()),
                        L"无法读取主显示器物理像素边界。");
                }
            });
        captureMonitorRect_ = MonitorPixelRect{
            monitorInfo.rcMonitor.left,
            monitorInfo.rcMonitor.top,
            monitorInfo.rcMonitor.right,
            monitorInfo.rcMonitor.bottom
        };
        const auto frameTapConfiguration = XB_STARTUP_STEP(
            startupDiagnostics_,
            "Configuration",
            "ReadRenderFrameTapConfiguration",
            "ReadRenderFrameTapConfiguration",
            [&]
            {
                return ReadRenderFrameTapConfiguration(
                    diagnosticLogDirectory_,
                    sessionGuidString_);
            });
        const auto videoEncoderConfiguration = XB_STARTUP_STEP(
            startupDiagnostics_,
            "Configuration",
            "ReadVideoEncoderConfiguration",
            "ReadVideoEncoderConfiguration",
            [&]
            {
                return ReadVideoEncoderConfiguration(
                    diagnosticLogDirectory_,
                    sessionGuidString_);
            });
        startupDiagnostics_.SetEncoderEnabled(
            videoEncoderConfiguration.enabled);
        XB_STARTUP_STEP(
            startupDiagnostics_,
            "Renderer",
            "InitializePreviewRenderer",
            "PreviewRenderer::Initialize",
            [&]
            {
                renderer_.Initialize(
                    previewHwnd_,
                    monitor,
                    previewWidth,
                    previewHeight,
                    allowWarp_,
                    frameTapConfiguration,
                    videoEncoderConfiguration,
                    startupDiagnostics_);
            });

        {
            std::lock_guard productLock(productConfigurationMutex_);
            const auto poseResult = productStageActive_
                ? renderer_.SetWindowShowcasePose(
                    productStageDirection_, productStageStrength_)
                : renderer_.SetWindowShowcaseInactive();
            if (poseResult != XbPreviewResult_Ok)
            {
                throw winrt::hresult_invalid_argument(
                    L"The configured Window Stage pose is invalid.");
            }
            const auto backgroundResult = productCustomBackground_
                ? renderer_.SetWindowShowcaseCustomBackground(
                    productCustomBackgroundPath_)
                : renderer_.SetWindowShowcaseBackgroundPreset(
                    productBackgroundPreset_);
            if (backgroundResult != XbPreviewResult_Ok)
            {
                // Decode/load failure is explicitly non-fatal. Preserve the
                // renderer's prior safe background and normalize the product
                // selection to Warm so Preview and Recording cannot diverge.
                (void)renderer_.SetWindowShowcaseBackgroundPreset(
                    WindowShowcaseBackgroundPreset::Warm);
                productBackgroundPreset_ =
                    WindowShowcaseBackgroundPreset::Warm;
                productCustomBackground_ = false;
                productCustomBackgroundPath_.clear();
                SetError(
                    backgroundResult,
                    L"Configured background load failed; Warm is active.");
            }
        }

        {
            std::lock_guard lock(statsMutex_);
            stats_.usedWarp = renderer_.UsedWarp() ? 1u : 0u;
            stats_.hdrDetected = renderer_.HdrDetected() ? 1u : 0u;
            if (renderer_.UsedWarp())
            {
                stats_.flags |= XbPreviewStatsFlags_UsingWarp;
            }
            if (renderer_.HdrDetected())
            {
                stats_.flags |= XbPreviewStatsFlags_HdrDetected;
            }
            wcsncpy_s(
                stats_.adapterName,
                renderer_.AdapterName().c_str(),
                _TRUNCATE);
        }

        XB_STARTUP_STEP(
            startupDiagnostics_,
            "RendererPolicy",
            "ValidateSdrOutput",
            "PreviewRenderer::HdrDetected",
            [&]
            {
        if (renderer_.HdrDetected())
        {
            SetError(
                XbPreviewResult_HdrUnsupported,
                L"P0 只保证 SDR；主显示器检测到 HDR/高级颜色，未启动 BGRA8 预览。");
            throw winrt::hresult_error(
                E_NOTIMPL,
                L"P0 只保证 SDR；请暂时关闭主显示器 HDR 后测试。");
        }
            });

        const auto interop = XB_STARTUP_STEP(
            startupDiagnostics_,
            "WgcCaptureItem",
            "GetCaptureItemActivationFactory",
            "winrt::get_activation_factory<IGraphicsCaptureItemInterop>",
            ([&]
            {
                return winrt::get_activation_factory<
                    winrt::Windows::Graphics::Capture::GraphicsCaptureItem,
                    IGraphicsCaptureItemInterop>();
            }));
        if (captureTarget_.IsWindow())
        {
            XB_STARTUP_STEP(
                startupDiagnostics_,
                "WgcCaptureItem",
                "CreateCaptureItemForWindow",
                "IGraphicsCaptureItemInterop::CreateForWindow",
                [&]
                {
                    winrt::check_hresult(interop->CreateForWindow(
                        captureTarget_.window,
                        winrt::guid_of<winrt::Windows::Graphics::Capture::
                            GraphicsCaptureItem>(),
                        winrt::put_abi(captureItem_)));
                });
        }
        else
        {
            XB_STARTUP_STEP(
                startupDiagnostics_,
                "WgcCaptureItem",
                "CreateCaptureItemForMonitor",
                "IGraphicsCaptureItemInterop::CreateForMonitor",
                [&]
                {
                    winrt::check_hresult(interop->CreateForMonitor(
                        monitor,
                        winrt::guid_of<winrt::Windows::Graphics::Capture::
                            GraphicsCaptureItem>(),
                        winrt::put_abi(captureItem_)));
                });
        }

        captureSize_ = XB_STARTUP_STEP(
            startupDiagnostics_,
            "WgcCaptureItem",
            "ReadCaptureItemSize",
            "GraphicsCaptureItem::Size",
            [&]
            {
                return captureItem_.Size();
            });
        XB_STARTUP_STEP(
            startupDiagnostics_,
            "SessionGeometry",
            "ResolveActiveCropTransform",
            "SessionGeometryStore::ActiveSnapshot/ResolveCropTransform",
            [&]
            {
        activeGeometry_ = sessionGeometryStore_.ActiveSnapshot();
        if (!activeGeometry_.has_value() ||
            !ResolveCropTransform(*activeGeometry_, activeCrop_) ||
            (!captureTarget_.IsWindow() &&
                !sessionGeometryStore_.ActiveSourceMatches(
                    captureSize_.Width,
                    captureSize_.Height)))
        {
            SetError(
                XbPreviewResult_GeometrySourceMismatch,
                L"Active SessionGeometry source size does not match the current WGC frame source.");
            throw winrt::hresult_invalid_argument(
                L"Active SessionGeometry source mismatch.");
        }
            });
        XB_STARTUP_STEP(
            startupDiagnostics_,
            "CaptureTarget",
            "ValidateCaptureItemSize",
            "GraphicsCaptureItem::Size",
            [&]
            {
        if (captureSize_.Width <= 0 || captureSize_.Height <= 0)
        {
            throw winrt::hresult_error(E_FAIL, L"主显示器捕获尺寸无效。");
        }
            });

        {
            std::lock_guard lock(statsMutex_);
            stats_.captureWidth = static_cast<std::uint32_t>(captureSize_.Width);
            stats_.captureHeight = static_cast<std::uint32_t>(captureSize_.Height);
            stats_.previewWidth = previewWidth;
            stats_.previewHeight = previewHeight;
        }

        framePool_ = XB_STARTUP_STEP(
            startupDiagnostics_,
            "WgcRuntime",
            "CreateFreeThreadedFramePool",
            "Direct3D11CaptureFramePool::CreateFreeThreaded",
            [&]
            {
                return winrt::Windows::Graphics::Capture::
                    Direct3D11CaptureFramePool::CreateFreeThreaded(
                        renderer_.WinRtDevice(),
                        winrt::Windows::Graphics::DirectX::
                            DirectXPixelFormat::B8G8R8A8UIntNormalized,
                        static_cast<std::int32_t>(framePoolBufferCount_),
                        captureSize_);
            });

        const auto callbackGate = std::make_shared<CallbackGate>();
        callbackGate->Activate(this);
        callbackGate_ = callbackGate;
        frameArrivedToken_ = XB_STARTUP_STEP(
            startupDiagnostics_,
            "WgcRuntime",
            "RegisterFrameArrived",
            "Direct3D11CaptureFramePool::FrameArrived",
            [&]
            {
                return framePool_.FrameArrived([callbackGate](
                const winrt::Windows::Graphics::Capture::Direct3D11CaptureFramePool& sender,
                const winrt::Windows::Foundation::IInspectable&) noexcept
            {
                if (const auto owner = callbackGate->Enter())
                {
                    owner->OnFrameArrived(sender);
                    callbackGate->Leave();
                }
                    });
            });
        captureClosedToken_ = XB_STARTUP_STEP(
            startupDiagnostics_,
            "WgcRuntime",
            "RegisterCaptureItemClosed",
            "GraphicsCaptureItem::Closed",
            [&]
            {
                return captureItem_.Closed([callbackGate](
                const winrt::Windows::Graphics::Capture::GraphicsCaptureItem&,
                const winrt::Windows::Foundation::IInspectable&) noexcept
            {
                if (const auto owner = callbackGate->Enter())
                {
                    owner->OnCaptureClosed();
                    callbackGate->Leave();
                }
                    });
            });

        captureSession_ = XB_STARTUP_STEP(
            startupDiagnostics_,
            "WgcRuntime",
            "CreateCaptureSession",
            "Direct3D11CaptureFramePool::CreateCaptureSession",
            [&]
            {
                return framePool_.CreateCaptureSession(captureItem_);
            });
        XB_STARTUP_STEP(
            startupDiagnostics_,
            "WgcRuntime",
            "ConfigureCaptureSessionCursor",
            "GraphicsCaptureSession::IsCursorCaptureEnabled",
            [&]
            {
                ConfigureCaptureSessionCursorMode();
            });
        WriteCursorDiagnostic("starting");
        acceptingFrames_.store(true);
        XB_STARTUP_STEP(
            startupDiagnostics_,
            "WgcRuntime",
            "StartCapture",
            "GraphicsCaptureSession::StartCapture",
            [&]
            {
                captureSession_.StartCapture();
            });

        lastRateQpc_ = QueryQpc();
        previousCaptureCount_ = 0;
        previousPresentCount_ = 0;
    }

    void PreviewEngine::ShutdownWorkerResources() noexcept
    {
        startupDiagnostics_.CleanupStarted();
        acceptingFrames_.store(false);
        const auto callbackGate = callbackGate_;
        if (callbackGate)
        {
            callbackGate->DeactivateAndWait();
        }
        try
        {
            if (framePool_ && frameArrivedToken_.value != 0)
            {
                framePool_.FrameArrived(frameArrivedToken_);
                frameArrivedToken_ = {};
            }
        }
        catch (...)
        {
        }
        try
        {
            if (captureItem_ && captureClosedToken_.value != 0)
            {
                captureItem_.Closed(captureClosedToken_);
                captureClosedToken_ = {};
            }
        }
        catch (...)
        {
        }
        try
        {
            if (captureSession_)
            {
                captureSession_.Close();
            }
        }
        catch (...)
        {
        }
        try
        {
            if (framePool_)
            {
                framePool_.Close();
            }
        }
        catch (...)
        {
        }

        {
            std::lock_guard lock(frameMutex_);
            pendingFrame_.reset();
        }

        captureSession_ = nullptr;
        framePool_ = nullptr;
        captureItem_ = nullptr;
        callbackGate_ = nullptr;
        activeGeometry_.reset();
        activeCrop_ = {};

        UpdateRatesAndDiagnostics(true);
        logger_.WriteEvent("stopped", XbPreviewState_Stopped);
        WriteCursorDiagnostic("stopped");
        cursorLogger_.Close();
        startupDiagnostics_.CleanupCompleted();
        startupDiagnostics_.WriteSummary();
        logger_.Close();
        cursorShapeCache_.Clear();
        cursorStateProvider_.Reset();
        renderer_.Shutdown();
    }

    void PreviewEngine::WorkerLoop()
    {
        while (!stopRequested_.load())
        {
            std::optional<PendingFrame> pending;
            {
                std::unique_lock lock(frameMutex_);
                frameCondition_.wait_for(
                    lock,
                    std::chrono::milliseconds(50),
                    [this]
                    {
                        if (stopRequested_.load() ||
                            captureClosed_.load() ||
                            callbackFailed_.load() ||
                            pendingFrame_.has_value())
                        {
                            return true;
                        }

                        std::lock_guard resizeLock(resizeMutex_);
                        return requestedResizeGeneration_ !=
                            appliedResizeGeneration_;
                    });
                if (pendingFrame_)
                {
                    pending = std::move(pendingFrame_);
                    pendingFrame_.reset();
                }
            }

            if (captureClosed_.load())
            {
                if (captureTarget_.IsWindow())
                {
                    SetError(
                        XbPreviewResult_WindowTargetClosed,
                        L"WindowTargetClosed: the selected target window has closed.");
                }
                throw winrt::hresult_error(
                    RO_E_CLOSED,
                    L"主显示器捕获目标已关闭。");
            }
            if (callbackFailed_.load())
            {
                throw winrt::hresult_error(
                    E_FAIL,
                    L"FrameArrived 回调失败。");
            }

            ApplyPendingResize();
            UpdateWindowVisibility();

            if (pending)
            {
                ProcessPendingFrame(std::move(*pending));
            }

            UpdateRatesAndDiagnostics(false);
        }
    }

    void PreviewEngine::OnFrameArrived(
        const winrt::Windows::Graphics::Capture::Direct3D11CaptureFramePool& sender) noexcept
    {
        if (!acceptingFrames_.load())
        {
            return;
        }

        try
        {
            auto frame = sender.TryGetNextFrame();
            if (!frame)
            {
                return;
            }
            PendingFrame pending{};
            pending.contentSize = frame.ContentSize();
            bool expected = false;
            if (firstFrameDiagnosticWritten_.compare_exchange_strong(
                    expected,
                    true))
            {
                std::ostringstream detail;
                detail << "width=" << pending.contentSize.Width
                    << ";height=" << pending.contentSize.Height;
                logger_.WriteEvent(
                    "first-frame",
                    XbPreviewState_Running,
                    detail.str());
            }
            pending.systemRelativeTime100ns =
                frame.SystemRelativeTime().count();
            pending.systemRelativeTimeValid =
                pending.systemRelativeTime100ns > 0;
            pending.arrivalQpc = QueryQpc();
            pending.frame = std::move(frame);
            const auto systemRelativeTime100ns =
                pending.systemRelativeTime100ns;
            const auto arrivalQpc = pending.arrivalQpc;

            bool replaced = false;
            {
                std::lock_guard lock(frameMutex_);
                if (!acceptingFrames_.load())
                {
                    return;
                }
                replaced = pendingFrame_.has_value();
                pendingFrame_ = std::move(pending);
            }

            {
                std::lock_guard lock(statsMutex_);
                ++stats_.captureFrameCount;
                if (replaced)
                {
                    ++stats_.droppedFrameCount;
                }
                stats_.lastSystemRelativeTime100ns =
                    systemRelativeTime100ns;
                stats_.lastFrameArrivalQpc = arrivalQpc;
            }
            frameCondition_.notify_one();
        }
        catch (...)
        {
            callbackFailed_.store(true);
            acceptingFrames_.store(false);
            frameCondition_.notify_all();
        }
    }

    void PreviewEngine::OnCaptureClosed() noexcept
    {
        captureClosed_.store(true);
        acceptingFrames_.store(false);
        frameCondition_.notify_all();
    }

    void PreviewEngine::ApplyPendingResize()
    {
        std::uint32_t width{};
        std::uint32_t height{};
        std::uint64_t generation{};
        {
            std::lock_guard lock(resizeMutex_);
            if (requestedResizeGeneration_ == appliedResizeGeneration_)
            {
                return;
            }
            width = requestedPreviewWidth_;
            height = requestedPreviewHeight_;
            generation = requestedResizeGeneration_;
        }

        if (renderer_.Resize(width, height))
        {
            {
                std::lock_guard lock(statsMutex_);
                stats_.previewWidth = width;
                stats_.previewHeight = height;
                ++stats_.swapChainResizeCount;
            }
            logger_.WriteEvent("swap-chain-resize", XbPreviewState_Running);
        }

        {
            std::lock_guard lock(resizeMutex_);
            appliedResizeGeneration_ = generation;
        }
    }

    void PreviewEngine::ProcessPendingFrame(PendingFrame&& pending)
    {
        if (!activeGeometry_.has_value() ||
            (!captureTarget_.IsWindow() &&
                (pending.contentSize.Width != activeGeometry_->sourceWidth ||
                 pending.contentSize.Height != activeGeometry_->sourceHeight)))
        {
            pending.frame.Close();
            SetError(
                XbPreviewResult_GeometrySourceMismatch,
                L"Active SessionGeometry no longer matches the WGC frame source.");
            throw winrt::hresult_invalid_argument(
                L"Active SessionGeometry source mismatch during rendering.");
        }

        if (pending.contentSize.Width <= 0 || pending.contentSize.Height <= 0)
        {
            pending.frame.Close();
            std::lock_guard lock(statsMutex_);
            ++stats_.droppedFrameCount;
            return;
        }

        if (pending.contentSize.Width != captureSize_.Width ||
            pending.contentSize.Height != captureSize_.Height)
        {
            // Recreate must not run while this frame still owns a pool buffer.
            pending.frame.Close();
            RecreateFramePool(pending.contentSize);
            std::lock_guard lock(statsMutex_);
            ++stats_.droppedFrameCount;
            return;
        }

        const auto access = pending.frame.Surface().as<
            Windows::Graphics::DirectX::Direct3D11::IDirect3DDxgiInterfaceAccess>();
        winrt::com_ptr<ID3D11Texture2D> capturedTexture;
        winrt::check_hresult(access->GetInterface(
            __uuidof(ID3D11Texture2D),
            capturedTexture.put_void()));

        D3D11_TEXTURE2D_DESC capturedDescription{};
        capturedTexture->GetDesc(&capturedDescription);
        if (captureTarget_.IsWindow() &&
            (capturedDescription.Width <
                static_cast<std::uint32_t>(pending.contentSize.Width) ||
             capturedDescription.Height <
                static_cast<std::uint32_t>(pending.contentSize.Height)))
        {
            // WGC can briefly pair the restored ContentSize with a surface
            // from the old pool generation. The pool has already been
            // recreated above; wait for its first matching surface instead
            // of treating this resize-transition frame as a fatal D3D error.
            pending.frame.Close();
            std::lock_guard lock(statsMutex_);
            ++stats_.droppedFrameCount;
            return;
        }

        CropTransform frameCrop = activeCrop_;
        if (captureTarget_.IsWindow())
        {
            frameCrop.originU = 0.0f;
            frameCrop.originV = 0.0f;
            frameCrop.scaleU = 1.0f;
            frameCrop.scaleV = 1.0f;
        }

        const auto cameraSnapshot = cameraStateStore_.Snapshot();
        const auto camera = ResolveCameraTransform(cameraSnapshot);
        const bool recordCursorVisible = recordCursorVisible_.load(
            std::memory_order_acquire);
        std::optional<CursorDrawCommand> cursorCommand;
        if (cursorModeDecision_.actual == XbCursorMode_CustomCursor)
        {
            const auto sample = cursorStateProvider_.Sample(captureMonitorRect_);
            {
                std::lock_guard lock(cursorStatsMutex_);
                ++cursorStats_.sampleCount;
                cursorStats_.cursorSequence = sample.sequence;
                cursorStats_.timestampQpc = sample.timestampQpc;
                cursorStats_.getCursorInfoResult =
                    sample.querySucceeded ? 1 : 0;
                cursorStats_.getCursorInfoLastError = sample.lastError;
                cursorStats_.cursorVisible = sample.visible ? 1u : 0u;
                cursorStats_.cursorInsideMonitor =
                    sample.insideMonitor ? 1u : 0u;
                cursorStats_.screenX = sample.screenX;
                cursorStats_.screenY = sample.screenY;
                cursorStats_.lastFrameDrawn = 0;
                cursorStats_.lastRenderDurationMilliseconds = 0.0;
                cursorStats_.reserved1 = 0;
                cursorStats_.zoom = camera.appliedZoom;
                cursorStats_.centerX = camera.appliedCenterX;
                cursorStats_.centerY = camera.appliedCenterY;
                if (!sample.querySucceeded)
                {
                    ++cursorStats_.getCursorInfoFailureCount;
                }
                else if (!sample.visible)
                {
                    ++cursorStats_.hiddenSkipCount;
                }
                else if (!sample.insideMonitor)
                {
                    ++cursorStats_.outsideMonitorSkipCount;
                }
            }

            if (sample.querySucceeded &&
                sample.visible &&
                sample.insideMonitor)
            {
                const auto cache = cursorShapeCache_.Resolve(
                    reinterpret_cast<HCURSOR>(sample.cursorHandle));
                if (cache.shape)
                {
                    XbLetterboxRect outputViewport{};
                    bool viewportResolved{};
                    if (captureTarget_.IsWindow())
                    {
                        FlatWindowStageComposition stage{};
                        viewportResolved = WindowStageComposer::ComposeFlat(
                            static_cast<std::uint32_t>(
                                pending.contentSize.Width),
                            static_cast<std::uint32_t>(
                                pending.contentSize.Height),
                            activeCrop_.outputWidth,
                            activeCrop_.outputHeight,
                            stage);
                        if (viewportResolved)
                        {
                            outputViewport = XbLetterboxRect{
                                stage.window.left,
                                stage.window.top,
                                stage.window.width,
                                stage.window.height
                            };
                        }
                    }
                    else
                    {
                        viewportResolved = CalculateLetterbox(
                            activeCrop_.captureWidth,
                            activeCrop_.captureHeight,
                            activeCrop_.outputWidth,
                            activeCrop_.outputHeight,
                            outputViewport);
                    }
                    if (!viewportResolved)
                    {
                        throw winrt::hresult_invalid_argument(
                            L"Cursor content viewport is invalid.");
                    }
                    const auto mapped = MapCursorToPreview(
                        sample,
                        *cache.shape,
                        captureMonitorRect_,
                        static_cast<std::uint32_t>(
                            pending.contentSize.Width),
                        static_cast<std::uint32_t>(
                            pending.contentSize.Height),
                        camera,
                        outputViewport);

                    {
                        std::lock_guard lock(cursorStatsMutex_);
                        cursorStats_.shapeConversionResult =
                            cache.conversionResult;
                        cursorStats_.shapeConversionLastError =
                            cache.conversionLastError;
                        cursorStats_.shapeKind = cache.shape->kind;
                        cursorStats_.shapeId = cache.shape->id;
                        cursorStats_.shapeGeneration =
                            cache.shape->generation;
                        cursorStats_.shapeWidth = cache.shape->width;
                        cursorStats_.shapeHeight = cache.shape->height;
                        cursorStats_.hotspotX = cache.shape->hotspotX;
                        cursorStats_.hotspotY = cache.shape->hotspotY;
                        cursorStats_.sourceX = mapped.sourceX;
                        cursorStats_.sourceY = mapped.sourceY;
                        cursorStats_.cameraViewLeft =
                            mapped.cameraViewLeft;
                        cursorStats_.cameraViewTop =
                            mapped.cameraViewTop;
                        cursorStats_.cameraViewWidth =
                            mapped.cameraViewWidth;
                        cursorStats_.cameraViewHeight =
                            mapped.cameraViewHeight;
                        cursorStats_.outputHotspotX =
                            mapped.outputHotspotX;
                        cursorStats_.outputHotspotY =
                            mapped.outputHotspotY;
                        cursorStats_.outputLeft = mapped.left;
                        cursorStats_.outputTop = mapped.top;
                        cursorStats_.outputWidth = mapped.width;
                        cursorStats_.outputHeight = mapped.height;
                        cursorStats_.viewportX = mapped.viewportX;
                        cursorStats_.viewportY = mapped.viewportY;
                        cursorStats_.viewportWidth =
                            mapped.viewportWidth;
                        cursorStats_.viewportHeight =
                            mapped.viewportHeight;
                        cursorStats_.shapeCacheHitCount +=
                            cache.cacheHit ? 1u : 0u;
                        cursorStats_.shapeCacheMissCount +=
                            cache.cacheMiss ? 1u : 0u;
                        if (cache.conversionFailed)
                        {
                            ++cursorStats_.shapeConversionFailureCount;
                        }
                        if (cache.usedBuiltInFallback)
                        {
                            ++cursorStats_.builtInFallbackCount;
                        }
                        if (cache.cacheMiss && !cache.conversionFailed)
                        {
                            cursorStats_.xorApproximationPixelCount +=
                                cache.shape->xorApproximationPixelCount;
                        }
                        if (!mapped.intersectsCamera)
                        {
                            ++cursorStats_.outsideCameraSkipCount;
                        }
                    }

                    if (recordCursorVisible &&
                        mapped.valid && mapped.intersectsCamera)
                    {
                        cursorCommand = CursorDrawCommand{
                            cache.shape,
                            mapped
                        };
                    }
                }
            }
        }

        const auto presentBefore = QueryQpc();
        const bool presentationEnabled = !previouslyMinimized_;
        bool occluded = false;
        CursorRenderResult cursorRender{};
        const auto presentResult = renderer_.RenderFrame(
            capturedTexture.get(),
            static_cast<std::uint32_t>(pending.contentSize.Width),
            static_cast<std::uint32_t>(pending.contentSize.Height),
            frameCrop,
            camera,
            cursorCommand ? &*cursorCommand : nullptr,
            captureTarget_.IsWindow(),
            presentationEnabled,
            RenderFrameTapTimestamp{
                pending.systemRelativeTimeValid,
                pending.systemRelativeTime100ns },
            cursorRender,
            occluded);
        const auto presentAfter = QueryQpc();
        pending.frame.Close();

        if (FAILED(presentResult))
        {
            const auto removedReason = renderer_.DeviceRemovedReason();
            {
                std::lock_guard lock(statsMutex_);
                stats_.deviceRemovedReason = static_cast<std::int32_t>(
                    FAILED(removedReason) ? removedReason : presentResult);
            }
            throw winrt::hresult_error(
                FAILED(removedReason) ? removedReason : presentResult,
                L"D3D11 render/Present 失败。");
        }

        if (cursorModeDecision_.actual == XbCursorMode_CustomCursor)
        {
            std::lock_guard lock(cursorStatsMutex_);
            cursorStats_.lastFrameDrawn = cursorRender.drawn ? 1u : 0u;
            cursorStats_.lastRenderDurationMilliseconds =
                cursorRender.durationMilliseconds;
            cursorStats_.reserved1 = static_cast<std::uint64_t>(
                static_cast<std::uint32_t>(cursorRender.result));
            if (cursorRender.drawn)
            {
                ++cursorStats_.drawCount;
            }
            if (cursorRender.textureUploaded)
            {
                ++cursorStats_.textureUploadCount;
            }
        }

        const auto softwareLatency =
            static_cast<double>(
                QpcTo100Nanoseconds(presentAfter) -
                pending.systemRelativeTime100ns) /
            10000.0;
        if (softwareLatency >= 0.0 && softwareLatency <= 10000.0)
        {
            latencyStatistics_.Add(softwareLatency);
        }

        {
            std::lock_guard lock(statsMutex_);
            stats_.lastPresentBeforeQpc = presentBefore;
            stats_.lastPresentAfterQpc = presentAfter;
            stats_.nativeLastAppliedSequence = camera.sequence;
            stats_.nativeAppliedZoom = camera.appliedZoom;
            stats_.nativeAppliedCenterX = camera.appliedCenterX;
            stats_.nativeAppliedCenterY = camera.appliedCenterY;
            stats_.nativeAppliedMode = camera.mode;
            stats_.nativeCameraEnabled = camera.enabled ? 1u : 0u;
            if (!presentationEnabled)
            {
                stats_.flags &= ~XbPreviewStatsFlags_Occluded;
            }
            else if (occluded)
            {
                stats_.flags |= XbPreviewStatsFlags_Occluded;
                ++stats_.droppedFrameCount;
            }
            else
            {
                stats_.flags &= ~XbPreviewStatsFlags_Occluded;
                ++stats_.presentFrameCount;
            }
        }
        WriteCursorDiagnostic("frame");
    }

    void PreviewEngine::RecreateFramePool(
        const winrt::Windows::Graphics::SizeInt32& size)
    {
        acceptingFrames_.store(false);
        {
            std::lock_guard lock(frameMutex_);
            pendingFrame_.reset();
        }

        framePool_.Recreate(
            renderer_.WinRtDevice(),
            winrt::Windows::Graphics::DirectX::DirectXPixelFormat::
                B8G8R8A8UIntNormalized,
            static_cast<std::int32_t>(framePoolBufferCount_),
            size);
        captureSize_ = size;
        {
            std::lock_guard lock(statsMutex_);
            stats_.captureWidth = static_cast<std::uint32_t>(size.Width);
            stats_.captureHeight = static_cast<std::uint32_t>(size.Height);
            ++stats_.framePoolRecreateCount;
        }
        logger_.WriteEvent("frame-pool-recreate", XbPreviewState_Running);
        acceptingFrames_.store(true);
    }

    void PreviewEngine::UpdateRatesAndDiagnostics(const bool force)
    {
        const auto now = QueryQpc();
        const auto elapsedMilliseconds = QpcToMilliseconds(now - lastRateQpc_);
        if (!force &&
            elapsedMilliseconds <
                static_cast<double>(statsIntervalMilliseconds_))
        {
            return;
        }

        XbPreviewStats snapshot{};
        {
            std::lock_guard lock(statsMutex_);
            if (elapsedMilliseconds > 0.0)
            {
                stats_.captureFps =
                    (stats_.captureFrameCount - previousCaptureCount_) *
                    1000.0 / elapsedMilliseconds;
                stats_.presentFps =
                    (stats_.presentFrameCount - previousPresentCount_) *
                    1000.0 / elapsedMilliseconds;
                stats_.cameraUpdateRate =
                    (stats_.cameraUpdateCount - previousCameraUpdateCount_) *
                    1000.0 / elapsedMilliseconds;
            }
            previousCaptureCount_ = stats_.captureFrameCount;
            previousPresentCount_ = stats_.presentFrameCount;
            previousCameraUpdateCount_ = stats_.cameraUpdateCount;

            const auto latency = latencyStatistics_.Snapshot();
            stats_.recentLatencyMilliseconds = latency.recent;
            stats_.p50LatencyMilliseconds = latency.p50;
            stats_.p95LatencyMilliseconds = latency.p95;
            stats_.maxLatencyMilliseconds = latency.maximum;

            PROCESS_MEMORY_COUNTERS_EX memory{};
            if (GetProcessMemoryInfo(
                GetCurrentProcess(),
                reinterpret_cast<PROCESS_MEMORY_COUNTERS*>(&memory),
                sizeof(memory)))
            {
                stats_.workingSetBytes = memory.WorkingSetSize;
                stats_.privateBytes = memory.PrivateUsage;
            }
            snapshot = stats_;
        }

        lastRateQpc_ = now;
        logger_.WriteSummary(snapshot);
    }

    void PreviewEngine::UpdateWindowVisibility()
    {
        const auto minimized = IsIconic(exclusionHwnd_) != FALSE;
        const auto visible = IsWindowVisible(exclusionHwnd_) != FALSE;
        const auto treatedAsMinimized = minimized || !visible;

        {
            std::lock_guard lock(statsMutex_);
            if (treatedAsMinimized)
            {
                stats_.flags |= XbPreviewStatsFlags_Minimized;
            }
            else
            {
                stats_.flags &= ~XbPreviewStatsFlags_Minimized;
            }

            if (captureTarget_.IsWindow() &&
                IsIconic(captureTarget_.window) != FALSE)
            {
                stats_.flags |= XbPreviewStatsFlags_WindowTargetMinimized;
            }
            else
            {
                stats_.flags &= ~XbPreviewStatsFlags_WindowTargetMinimized;
            }
        }

        if (treatedAsMinimized != previouslyMinimized_)
        {
            logger_.WriteEvent(
                treatedAsMinimized ? "minimized-or-hidden" : "restored",
                XbPreviewState_Running);
            previouslyMinimized_ = treatedAsMinimized;
        }
    }

    void PreviewEngine::ApplyWindowDisplayAffinity() noexcept
    {
        SetLastError(ERROR_SUCCESS);
        const auto result = SetWindowDisplayAffinity(
            exclusionHwnd_,
            WdaExcludeFromCapture);
        const auto error = result ? ERROR_SUCCESS : GetLastError();

        std::lock_guard lock(statsMutex_);
        stats_.wdaResult = result ? 1 : 0;
        stats_.wdaLastError = error;
        if (result)
        {
            stats_.flags |= XbPreviewStatsFlags_WdaApplied;
            stats_.flags &= ~XbPreviewStatsFlags_WdaFailed;
        }
        else
        {
            stats_.flags |= XbPreviewStatsFlags_WdaFailed;
            stats_.flags &= ~XbPreviewStatsFlags_WdaApplied;
        }
    }

    void PreviewEngine::ResetSessionStats()
    {
        std::uint32_t previewWidth{};
        std::uint32_t previewHeight{};
        {
            std::lock_guard resizeLock(resizeMutex_);
            previewWidth = requestedPreviewWidth_;
            previewHeight = requestedPreviewHeight_;
        }

        {
            std::lock_guard errorLock(errorMutex_);
            lastError_.clear();
        }

        std::lock_guard statsLock(statsMutex_);
        const auto preservedFlags = stats_.flags &
            (XbPreviewStatsFlags_WdaApplied | XbPreviewStatsFlags_WdaFailed);
        const auto wdaResult = stats_.wdaResult;
        const auto wdaError = stats_.wdaLastError;

        stats_ = {};
        stats_.structSize = sizeof(XbPreviewStats);
        stats_.apiVersion = XB_PREVIEW_API_VERSION;
        stats_.state = XbPreviewState_Starting;
        stats_.flags = preservedFlags;
        stats_.wdaResult = wdaResult;
        stats_.wdaLastError = wdaError;
        stats_.previewWidth = previewWidth;
        stats_.previewHeight = previewHeight;
        latencyStatistics_.Clear();
        previousCaptureCount_ = 0;
        previousPresentCount_ = 0;
        previousCameraUpdateCount_ = 0;
    }

    void PreviewEngine::ResetCursorStats()
    {
        cursorModeDecision_ = {};
        cursorModeDecision_.requested = requestedCursorMode_;
        cursorStateProvider_.Reset();
        cursorShapeCache_.Clear();

        std::lock_guard lock(cursorStatsMutex_);
        cursorStats_ = {};
        cursorStats_.structSize = sizeof(XbCursorStats);
        cursorStats_.apiVersion = XB_PREVIEW_API_VERSION;
        cursorStats_.requestedMode = requestedCursorMode_;
        cursorStats_.actualMode = XbCursorMode_SystemCursor;
        cursorStats_.fallbackReason = XbCursorFallbackReason_None;
        cursorStats_.systemCursorIncluded = 1;
        cursorStats_.wgcCursorSettingResult = S_OK;
        cursorStats_.shapeConversionResult = S_OK;
        cursorStats_.zoom = 1.0;
        cursorStats_.centerX = 0.5;
        cursorStats_.centerY = 0.5;
    }

    void PreviewEngine::ConfigureCaptureSessionCursorMode()
    {
        const bool recordCursorVisible = recordCursorVisible_.load(
            std::memory_order_acquire);
        bool propertyAvailable = false;
        try
        {
            propertyAvailable =
                winrt::Windows::Foundation::Metadata::ApiInformation::
                    IsPropertyPresent(
                        L"Windows.Graphics.Capture.GraphicsCaptureSession",
                        L"IsCursorCaptureEnabled");
        }
        catch (...)
        {
            propertyAvailable = false;
        }

        // Window capture uses the WGC cursor. This preserves exactly one
        // cursor without creating a second desktop-to-card mapping path.
        if (captureTarget_.IsWindow())
        {
            HRESULT settingResult = S_OK;
            bool applied = true;
            if (propertyAvailable)
            {
                try
                {
                    captureSession_.IsCursorCaptureEnabled(recordCursorVisible);
                    applied = captureSession_.IsCursorCaptureEnabled();
                    if (applied != recordCursorVisible)
                    {
                        settingResult = E_FAIL;
                    }
                }
                catch (const winrt::hresult_error& error)
                {
                    settingResult = error.code();
                }
            }
            else if (!recordCursorVisible)
            {
                settingResult = E_NOTIMPL;
            }
            cursorModeDecision_ = {};
            cursorModeDecision_.requested = requestedCursorMode_;
            cursorModeDecision_.actual = XbCursorMode_SystemCursor;
            cursorModeDecision_.systemCursorIncluded = true;
            cursorModeDecision_.customCursorLayerActive = false;

            appliedRecordCursorVisible_.store(
                applied, std::memory_order_release);
            {
                std::lock_guard lock(cursorStatsMutex_);
                cursorStats_.requestedMode = requestedCursorMode_;
                cursorStats_.actualMode = XbCursorMode_SystemCursor;
                cursorStats_.fallbackReason = XbCursorFallbackReason_None;
                cursorStats_.wgcCursorPropertyAvailable =
                    propertyAvailable ? 1u : 0u;
                cursorStats_.systemCursorIncluded = applied ? 1u : 0u;
                cursorStats_.customCursorLayerActive = 0;
                cursorStats_.wgcCursorSettingResult = settingResult;
            }
            if (!recordCursorVisible &&
                (FAILED(settingResult) || applied))
            {
                cursorPresentationFailureCount_.fetch_add(
                    1, std::memory_order_acq_rel);
                throw winrt::hresult_error(
                    FAILED(settingResult) ? settingResult : E_FAIL,
                    L"Window capture cannot exclude the system cursor.");
            }
            return;
        }

        bool rendererReady =
            requestedCursorMode_ == XbCursorMode_SystemCursor;
        if (requestedCursorMode_ == XbCursorMode_CustomCursor)
        {
            try
            {
                renderer_.InitializeCustomCursorLayer();
                rendererReady = true;
            }
            catch (...)
            {
                rendererReady = false;
            }
        }

        HRESULT settingResult = S_OK;
        std::uint32_t settingLastError = ERROR_SUCCESS;
        bool settingSucceeded =
            requestedCursorMode_ == XbCursorMode_SystemCursor;
        bool readbackExcluded = false;
        if (requestedCursorMode_ == XbCursorMode_CustomCursor &&
            propertyAvailable &&
            rendererReady)
        {
            try
            {
                captureSession_.IsCursorCaptureEnabled(false);
                readbackExcluded =
                    !captureSession_.IsCursorCaptureEnabled();
                settingSucceeded = true;
            }
            catch (const winrt::hresult_error& error)
            {
                settingResult = error.code();
                settingLastError = HRESULT_FACILITY(settingResult) ==
                    FACILITY_WIN32
                    ? HRESULT_CODE(settingResult)
                    : ERROR_SUCCESS;
                settingSucceeded = false;
            }
            catch (...)
            {
                settingResult = E_FAIL;
                settingSucceeded = false;
            }
        }

        cursorModeDecision_ = ResolveCursorModePolicy(
            requestedCursorMode_,
            propertyAvailable,
            rendererReady,
            settingSucceeded,
            readbackExcluded);

        if (cursorModeDecision_.actual == XbCursorMode_SystemCursor &&
            propertyAvailable)
        {
            bool restored = false;
            try
            {
                captureSession_.IsCursorCaptureEnabled(true);
                restored = captureSession_.IsCursorCaptureEnabled();
            }
            catch (const winrt::hresult_error& error)
            {
                restored = false;
                if (requestedCursorMode_ == XbCursorMode_SystemCursor)
                {
                    settingResult = error.code();
                    settingLastError =
                        HRESULT_FACILITY(settingResult) == FACILITY_WIN32
                        ? HRESULT_CODE(settingResult)
                        : ERROR_SUCCESS;
                }
            }
            catch (...)
            {
                restored = false;
                if (requestedCursorMode_ == XbCursorMode_SystemCursor)
                {
                    settingResult = E_FAIL;
                }
            }

            if (!restored)
            {
                // The session has not started. Recreate it so the documented
                // P1b default (system cursor included) is restored without
                // ever enabling the custom layer at the same time.
                try
                {
                    captureSession_.Close();
                }
                catch (...)
                {
                }
                captureSession_ =
                    framePool_.CreateCaptureSession(captureItem_);
                try
                {
                    captureSession_.IsCursorCaptureEnabled(true);
                    restored = captureSession_.IsCursorCaptureEnabled();
                }
                catch (...)
                {
                    restored = false;
                }
            }
            cursorModeDecision_.systemCursorIncluded = true;
            cursorModeDecision_.customCursorLayerActive = false;
        }

        if (!CursorOwnershipIsExclusive(cursorModeDecision_))
        {
            cursorModeDecision_ = {};
            cursorModeDecision_.requested = requestedCursorMode_;
            cursorModeDecision_.actual = XbCursorMode_SystemCursor;
            cursorModeDecision_.systemCursorIncluded = true;
            cursorModeDecision_.fallback =
                XbCursorFallbackReason_WgcReadbackMismatch;
        }

        bool presentationApplied = recordCursorVisible;
        if (!recordCursorVisible &&
            cursorModeDecision_.actual == XbCursorMode_SystemCursor)
        {
            presentationApplied = true;
            settingResult = S_OK;
            settingLastError = ERROR_SUCCESS;
            if (propertyAvailable)
            {
                try
                {
                    captureSession_.IsCursorCaptureEnabled(false);
                    presentationApplied =
                        captureSession_.IsCursorCaptureEnabled();
                    if (presentationApplied)
                    {
                        settingResult = E_FAIL;
                    }
                }
                catch (const winrt::hresult_error& error)
                {
                    settingResult = error.code();
                }
                catch (...)
                {
                    settingResult = E_FAIL;
                }
            }
            else
            {
                settingResult = E_NOTIMPL;
            }
        }
        appliedRecordCursorVisible_.store(
            presentationApplied, std::memory_order_release);

        {
            std::lock_guard lock(cursorStatsMutex_);
            cursorStats_.requestedMode = cursorModeDecision_.requested;
            cursorStats_.actualMode = cursorModeDecision_.actual;
            cursorStats_.fallbackReason = cursorModeDecision_.fallback;
            cursorStats_.wgcCursorPropertyAvailable =
                propertyAvailable ? 1u : 0u;
            cursorStats_.systemCursorIncluded =
                cursorModeDecision_.actual == XbCursorMode_SystemCursor &&
                presentationApplied ? 1u : 0u;
            cursorStats_.customCursorLayerActive =
                cursorModeDecision_.customCursorLayerActive ? 1u : 0u;
            cursorStats_.wgcCursorSettingResult = settingResult;
            cursorStats_.wgcCursorSettingLastError = settingLastError;
        }
        if (!recordCursorVisible && presentationApplied)
        {
            cursorPresentationFailureCount_.fetch_add(
                1, std::memory_order_acq_rel);
            throw winrt::hresult_error(
                FAILED(settingResult) ? settingResult : E_FAIL,
                L"Capture session cannot exclude the system cursor.");
        }
    }

    void PreviewEngine::WriteCursorDiagnostic(
        const char* const event) noexcept
    {
        CursorDiagnosticRecord record{};
        record.event = event != nullptr ? event : "unknown";
        FILETIME now{};
        GetSystemTimePreciseAsFileTime(&now);
        ULARGE_INTEGER timestamp{};
        timestamp.LowPart = now.dwLowDateTime;
        timestamp.HighPart = now.dwHighDateTime;
        record.timestampUtcFileTime100ns = timestamp.QuadPart;
        {
            std::lock_guard lock(cursorStatsMutex_);
            cursorStats_.diagnosticQueueDropCount =
                cursorLogger_.QueueDropCount();
            record.cursor = cursorStats_;
        }
        {
            std::lock_guard lock(statsMutex_);
            record.preview = stats_;
        }
        cursorLogger_.Enqueue(std::move(record));
    }

    void PreviewEngine::SetState(const XbPreviewState state) noexcept
    {
        std::lock_guard lock(statsMutex_);
        stats_.state = state;
    }

    void PreviewEngine::SetError(
        const XbPreviewResult result,
        const std::wstring& message) noexcept
    {
        try
        {
            {
                std::lock_guard lock(errorMutex_);
                lastError_ = message;
            }
            XbPreviewState state{};
            {
                std::lock_guard lock(statsMutex_);
                stats_.lastResult = result;
                state = static_cast<XbPreviewState>(stats_.state);
            }
            logger_.WriteEvent(
                "error",
                state,
                NarrowForLog(message));
        }
        catch (...)
        {
            // Error reporting is best-effort and must never terminate a caller
            // that is already crossing a noexcept C ABI boundary.
        }
    }

    void PreviewEngine::SetErrorFromHresult(
        const XbPreviewResult result,
        const HRESULT hresult,
        const std::wstring& context) noexcept
    {
        try
        {
            std::wostringstream message;
            message << context
                << L" HRESULT=0x"
                << std::hex
                << static_cast<std::uint32_t>(hresult);
            const auto systemMessage = HresultMessage(hresult);
            if (!systemMessage.empty())
            {
                message << L" (" << systemMessage << L")";
            }
            SetError(result, message.str());
        }
        catch (...)
        {
            SetError(result, L"Native HRESULT failure.");
        }
    }

    AudioEndpointLevelAssignment
        PreviewEngine::GetAudioEndpointLevelAssignment() const noexcept
    {
        try
        {
            std::lock_guard lock(lifecycleMutex_);
            const auto endpoints = microphoneDeviceMonitor_.Snapshot();
            AudioEndpointLevelAssignment result{};
            if (recordingAudioProgramMode_.has_value())
            {
                result.systemEnabled =
                    *recordingAudioProgramMode_ == AudioProgramMode::SystemOnly ||
                    *recordingAudioProgramMode_ == AudioProgramMode::Dual;
            }
            if (activeMicrophoneDevice_ != nullptr)
            {
                // Observe the exact binding already locked for the formal
                // recording graph. This does not resolve or capture a device.
                result.microphoneEndpointId =
                    activeMicrophoneDevice_->EndpointId();
                result.microphoneEnabled = true;
            }

            result.systemEndpointId =
                !activeSystemAudioEndpointId_.empty()
                    ? activeSystemAudioEndpointId_
                    : endpoints.defaultSystemEndpointId;
            return result;
        }
        catch (...)
        {
            return {};
        }
    }

    void PreviewEngine::StartMicPreflightLocked() noexcept
    {
        const auto microphoneEnabled = recordingAudioProgramMode_.has_value() &&
            (*recordingAudioProgramMode_ == AudioProgramMode::MicrophoneOnly ||
                *recordingAudioProgramMode_ == AudioProgramMode::Dual);
        if (!microphoneEnabled ||
            stateMachine_.State() != XbPreviewState_Running)
        {
            microphonePreflightLevelMonitor_.Stop();
            return;
        }

        try
        {
            auto binding = microphoneSelectionKind_ ==
                    XbMicrophoneSelectionKindV1_WindowsDefault
                ? microphoneDeviceMonitor_.LockDefault()
                : microphoneDeviceMonitor_.LockEndpoint(
                    microphoneSelectionEndpointId_);
            const auto requestedEndpointId = binding != nullptr
                ? binding->EndpointId()
                : microphoneSelectionEndpointId_;
            // Best effort by contract: unavailability is cached for the meter
            // but never becomes Preview or Recording failure.
            (void)microphonePreflightLevelMonitor_.Start(
                std::move(binding), requestedEndpointId);
        }
        catch (...)
        {
            microphonePreflightLevelMonitor_.Stop();
        }
    }

    HMONITOR PreviewEngine::PrimaryMonitor() noexcept
    {
        return MonitorFromPoint(POINT{ 0, 0 }, MONITOR_DEFAULTTOPRIMARY);
    }

    std::wstring PreviewEngine::GuidToString(const GUID& value)
    {
        return FormatCanonicalRecordingSessionId(value);
    }

    std::int64_t PreviewEngine::QueryQpc() noexcept
    {
        LARGE_INTEGER value{};
        QueryPerformanceCounter(&value);
        return value.QuadPart;
    }

    double PreviewEngine::QpcToMilliseconds(const std::int64_t ticks) const noexcept
    {
        return static_cast<double>(ticks) * 1000.0 /
            static_cast<double>(qpcFrequency_);
    }

    std::int64_t PreviewEngine::QpcTo100Nanoseconds(
        const std::int64_t ticks) const noexcept
    {
        const auto value =
            static_cast<long double>(ticks) * 10000000.0L /
            static_cast<long double>(qpcFrequency_);
        return static_cast<std::int64_t>(value);
    }
}
