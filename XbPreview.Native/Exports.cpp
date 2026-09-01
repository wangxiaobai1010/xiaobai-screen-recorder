#include "PreviewEngine.h"

#include "HistoricalSessionScanInterop.h"
#include "Letterbox.h"
#include "NarrowReconciliationInterop.h"
#include "XbPreviewApi.h"

#include <memory>
#include <new>
#include <exception>
#include <string>

namespace
{
    thread_local std::wstring LastApiError;

    void SetApiError(const std::wstring& message)
    {
        LastApiError = message;
    }

    void SetApiErrorNoThrow(const wchar_t* const message) noexcept
    {
        try
        {
            LastApiError = message == nullptr ? L"" : message;
        }
        catch (...)
        {
            // Diagnostics are best effort at a C ABI exception boundary.
        }
    }

    xbpreview::PreviewEngine* EngineFromHandle(const XbPreviewHandle handle) noexcept
    {
        return static_cast<xbpreview::PreviewEngine*>(handle);
    }

    bool IsCurrentProcessWindow(const HWND window) noexcept
    {
        if (!IsWindow(window))
        {
            return false;
        }

        DWORD processId{};
        GetWindowThreadProcessId(window, &processId);
        return processId == GetCurrentProcessId();
    }

    bool IsCompatibleVersion(const std::uint32_t version) noexcept
    {
        return (version & 0xFFFF0000u) ==
            (XB_PREVIEW_API_VERSION & 0xFFFF0000u);
    }

    void RecordRecordingBoundaryFailure(
        xbpreview::PreviewEngine* const engine,
        const HRESULT hresult,
        const wchar_t* const message) noexcept
    {
        if (engine == nullptr)
        {
            return;
        }
        try
        {
            engine->RecordRecordingBoundaryFailure(
                XbPreviewResult_NativeFailure, hresult, message);
        }
        catch (...)
        {
            // The C ABI boundary must remain non-throwing even if publishing
            // the diagnostic Snapshot cannot acquire its lock.
        }
    }
}

extern "C"
{
    std::uint32_t XB_PREVIEW_CALL XbPreview_GetApiVersion() noexcept
    {
        return XB_PREVIEW_API_VERSION;
    }

    XbPreviewResult XB_PREVIEW_CALL XbPreview_GetAbiLayout(
        XbPreviewAbiLayout* const layout) noexcept
    {
        if (layout == nullptr)
        {
            SetApiError(L"XbPreviewAbiLayout 指针为空。");
            return XbPreviewResult_InvalidArgument;
        }

        if (layout->structSize != sizeof(XbPreviewAbiLayout) ||
            !IsCompatibleVersion(layout->apiVersion))
        {
            SetApiError(L"AbiLayout size or API major version mismatch.");
            return XbPreviewResult_AbiMismatch;
        }

        *layout = {};
        layout->structSize = sizeof(XbPreviewAbiLayout);
        layout->apiVersion = XB_PREVIEW_API_VERSION;
        layout->pointerSize = sizeof(void*);
        layout->packing = 8;
        layout->createOptionsSize = sizeof(XbPreviewCreateOptions);
        layout->statsSize = sizeof(XbPreviewStats);
        layout->letterboxRectSize = sizeof(XbLetterboxRect);
        layout->wcharSize = sizeof(wchar_t);
        layout->cameraStateSize = sizeof(XbCameraState);
        layout->cursorStatsSize = sizeof(XbCursorStats);
        layout->recordingSnapshotSize = sizeof(XbRecordingSnapshot);
        return XbPreviewResult_Ok;
    }

    XbPreviewResult XB_PREVIEW_CALL
        XbPreview_GetHistoricalSessionScanAbiLayoutV1(
            XbHistoricalSessionScanAbiLayoutV1* const layout) noexcept
    {
        try
        {
            return xbpreview::interop::
                GetHistoricalSessionScanAbiLayoutV1(layout);
        }
        catch (const std::bad_alloc&)
        {
            SetApiErrorNoThrow(
                L"Historical scan ABI layout allocation failure.");
            return XbPreviewResult_NativeFailure;
        }
        catch (const std::exception&)
        {
            SetApiErrorNoThrow(
                L"Historical scan ABI layout native exception.");
            return XbPreviewResult_NativeFailure;
        }
        catch (...)
        {
            SetApiErrorNoThrow(
                L"Historical scan ABI layout unknown exception.");
            return XbPreviewResult_NativeFailure;
        }
    }

    XbPreviewResult XB_PREVIEW_CALL XbPreview_BeginHistoricalSessionScanV1(
        const XbHistoricalSessionScanOptionsV1* const options,
        XbHistoricalSessionScanHandle* const scanHandle,
        XbHistoricalSessionScanSummaryV1* const summary) noexcept
    {
        try
        {
            return xbpreview::interop::BeginHistoricalSessionScanV1(
                options, scanHandle, summary);
        }
        catch (const std::bad_alloc&)
        {
            SetApiErrorNoThrow(
                L"Historical session scan allocation failure.");
            return XbPreviewResult_NativeFailure;
        }
        catch (const std::exception&)
        {
            SetApiErrorNoThrow(
                L"Historical session scan native exception.");
            return XbPreviewResult_NativeFailure;
        }
        catch (...)
        {
            SetApiErrorNoThrow(
                L"Historical session scan unknown exception.");
            return XbPreviewResult_NativeFailure;
        }
    }

    XbPreviewResult XB_PREVIEW_CALL
        XbPreview_BeginHistoricalSessionScanForOutputRootV1(
            const XbHistoricalSessionScanOutputRootOptionsV1* const options,
            XbHistoricalSessionScanHandle* const scanHandle,
            XbHistoricalSessionScanSummaryV1* const summary) noexcept
    {
        try
        {
            return xbpreview::interop::
                BeginHistoricalSessionScanForOutputRootV1(
                    options, scanHandle, summary);
        }
        catch (const std::bad_alloc&)
        {
            SetApiErrorNoThrow(
                L"Explicit-root historical session scan allocation failure.");
            return XbPreviewResult_NativeFailure;
        }
        catch (const std::exception&)
        {
            SetApiErrorNoThrow(
                L"Explicit-root historical session scan native exception.");
            return XbPreviewResult_NativeFailure;
        }
        catch (...)
        {
            SetApiErrorNoThrow(
                L"Explicit-root historical session scan unknown exception.");
            return XbPreviewResult_NativeFailure;
        }
    }

    XbPreviewResult XB_PREVIEW_CALL XbPreview_GetHistoricalSessionV1(
        const XbHistoricalSessionScanHandle scanHandle,
        const std::uint32_t index,
        XbHistoricalSessionItemV1* const item) noexcept
    {
        try
        {
            return xbpreview::interop::GetHistoricalSessionV1(
                scanHandle, index, item);
        }
        catch (const std::bad_alloc&)
        {
            SetApiErrorNoThrow(
                L"Historical session item allocation failure.");
            return XbPreviewResult_NativeFailure;
        }
        catch (const std::exception&)
        {
            SetApiErrorNoThrow(
                L"Historical session item native exception.");
            return XbPreviewResult_NativeFailure;
        }
        catch (...)
        {
            SetApiErrorNoThrow(
                L"Historical session item unknown exception.");
            return XbPreviewResult_NativeFailure;
        }
    }

    XbPreviewResult XB_PREVIEW_CALL
        XbPreview_GetHistoricalSessionScanStringV1(
            const XbHistoricalSessionScanHandle scanHandle,
            const XbHistoricalSessionScanStringFieldV1 field,
            wchar_t* const buffer,
            const std::uint32_t bufferLength,
            std::uint32_t* const requiredLength) noexcept
    {
        try
        {
            return xbpreview::interop::GetHistoricalSessionScanStringV1(
                scanHandle,
                field,
                buffer,
                bufferLength,
                requiredLength);
        }
        catch (const std::bad_alloc&)
        {
            SetApiErrorNoThrow(
                L"Historical scan string allocation failure.");
            return XbPreviewResult_NativeFailure;
        }
        catch (const std::exception&)
        {
            SetApiErrorNoThrow(
                L"Historical scan string native exception.");
            return XbPreviewResult_NativeFailure;
        }
        catch (...)
        {
            SetApiErrorNoThrow(
                L"Historical scan string unknown exception.");
            return XbPreviewResult_NativeFailure;
        }
    }

    XbPreviewResult XB_PREVIEW_CALL XbPreview_GetHistoricalSessionStringV1(
        const XbHistoricalSessionScanHandle scanHandle,
        const std::uint32_t index,
        const XbHistoricalSessionStringFieldV1 field,
        wchar_t* const buffer,
        const std::uint32_t bufferLength,
        std::uint32_t* const requiredLength) noexcept
    {
        try
        {
            return xbpreview::interop::GetHistoricalSessionStringV1(
                scanHandle,
                index,
                field,
                buffer,
                bufferLength,
                requiredLength);
        }
        catch (const std::bad_alloc&)
        {
            SetApiErrorNoThrow(
                L"Historical session string allocation failure.");
            return XbPreviewResult_NativeFailure;
        }
        catch (const std::exception&)
        {
            SetApiErrorNoThrow(
                L"Historical session string native exception.");
            return XbPreviewResult_NativeFailure;
        }
        catch (...)
        {
            SetApiErrorNoThrow(
                L"Historical session string unknown exception.");
            return XbPreviewResult_NativeFailure;
        }
    }

    XbPreviewResult XB_PREVIEW_CALL XbPreview_DestroyHistoricalSessionScanV1(
        XbHistoricalSessionScanHandle* const scanHandle) noexcept
    {
        try
        {
            return xbpreview::interop::DestroyHistoricalSessionScanV1(
                scanHandle);
        }
        catch (...)
        {
            if (scanHandle != nullptr)
            {
                *scanHandle = nullptr;
            }
            SetApiErrorNoThrow(
                L"Historical session scan destruction exception.");
            return XbPreviewResult_NativeFailure;
        }
    }

    XbPreviewResult XB_PREVIEW_CALL
        XbPreview_GetNarrowReconciliationAbiLayoutV1(
            XbNarrowReconciliationAbiLayoutV1* const layout) noexcept
    {
        try
        {
            return xbpreview::interop::
                GetNarrowReconciliationAbiLayoutV1(layout);
        }
        catch (const std::bad_alloc&)
        {
            SetApiErrorNoThrow(
                L"Narrow reconciliation ABI layout allocation failure.");
            return XbPreviewResult_NativeFailure;
        }
        catch (const std::exception&)
        {
            SetApiErrorNoThrow(
                L"Narrow reconciliation ABI layout native exception.");
            return XbPreviewResult_NativeFailure;
        }
        catch (...)
        {
            SetApiErrorNoThrow(
                L"Narrow reconciliation ABI layout unknown exception.");
            return XbPreviewResult_NativeFailure;
        }
    }

    XbPreviewResult XB_PREVIEW_CALL XbPreview_ReconcileNarrowSessionV1(
        const XbNarrowReconciliationOptionsV1* const options,
        XbNarrowReconciliationResultV1* const result) noexcept
    {
        try
        {
            return xbpreview::interop::ReconcileNarrowSessionV1(
                options, result);
        }
        catch (const std::bad_alloc&)
        {
            SetApiErrorNoThrow(
                L"Narrow reconciliation allocation failure.");
            return XbPreviewResult_NativeFailure;
        }
        catch (const std::exception&)
        {
            SetApiErrorNoThrow(
                L"Narrow reconciliation native exception.");
            return XbPreviewResult_NativeFailure;
        }
        catch (...)
        {
            SetApiErrorNoThrow(
                L"Narrow reconciliation unknown exception.");
            return XbPreviewResult_NativeFailure;
        }
    }

    XbPreviewResult XB_PREVIEW_CALL
        XbPreview_ReconcileNarrowSessionForOutputRootV1(
            const XbNarrowReconciliationOutputRootOptionsV1* const options,
            XbNarrowReconciliationResultV1* const result) noexcept
    {
        try
        {
            return xbpreview::interop::
                ReconcileNarrowSessionForOutputRootV1(options, result);
        }
        catch (const std::bad_alloc&)
        {
            SetApiErrorNoThrow(
                L"Explicit-root narrow reconciliation allocation failure.");
            return XbPreviewResult_NativeFailure;
        }
        catch (const std::exception&)
        {
            SetApiErrorNoThrow(
                L"Explicit-root narrow reconciliation native exception.");
            return XbPreviewResult_NativeFailure;
        }
        catch (...)
        {
            SetApiErrorNoThrow(
                L"Explicit-root narrow reconciliation unknown exception.");
            return XbPreviewResult_NativeFailure;
        }
    }

    XbPreviewResult XB_PREVIEW_CALL XbPreview_Create(
        const HWND previewHwnd,
        const XbPreviewCreateOptions* const options,
        XbPreviewHandle* const handle) noexcept
    {
        if (handle == nullptr)
        {
            SetApiError(L"输出 handle 指针为空。");
            return XbPreviewResult_InvalidArgument;
        }
        *handle = nullptr;

        if (options == nullptr ||
            options->structSize != sizeof(XbPreviewCreateOptions) ||
            !IsCompatibleVersion(options->apiVersion))
        {
            SetApiError(L"CreateOptions 大小或 API 主版本不匹配。");
            return XbPreviewResult_AbiMismatch;
        }

        const auto exclusionHwnd = reinterpret_cast<HWND>(
            static_cast<std::uintptr_t>(options->exclusionWindow));
        if (!IsCurrentProcessWindow(previewHwnd) ||
            !IsCurrentProcessWindow(exclusionHwnd) ||
            GetAncestor(exclusionHwnd, GA_ROOT) != exclusionHwnd)
        {
            SetApiError(
                L"preview HWND 必须有效；exclusion HWND 必须是本进程顶层窗口。");
            return XbPreviewResult_InvalidWindow;
        }

        try
        {
            auto engine = std::make_unique<xbpreview::PreviewEngine>(
                previewHwnd,
                exclusionHwnd,
                *options);
            *handle = engine.release();
            LastApiError.clear();
            return XbPreviewResult_Ok;
        }
        catch (const winrt::hresult_error& error)
        {
            std::wstring message = L"创建原生引擎失败：";
            message += error.message().c_str();
            SetApiError(message);
            return XbPreviewResult_NativeFailure;
        }
        catch (const std::exception&)
        {
            SetApiError(L"创建原生引擎发生标准 C++ 异常。");
            return XbPreviewResult_NativeFailure;
        }
        catch (...)
        {
            SetApiError(L"创建原生引擎发生未知异常。");
            return XbPreviewResult_NativeFailure;
        }
    }

    XbPreviewResult XB_PREVIEW_CALL XbPreview_Start(
        const XbPreviewHandle handle) noexcept
    {
        const auto engine = EngineFromHandle(handle);
        if (engine == nullptr)
        {
            SetApiError(L"Start handle 为空。");
            return XbPreviewResult_InvalidHandle;
        }
        return engine->Start();
    }

    XbPreviewResult XB_PREVIEW_CALL XbPreview_Stop(
        const XbPreviewHandle handle) noexcept
    {
        const auto engine = EngineFromHandle(handle);
        if (engine == nullptr)
        {
            return XbPreviewResult_Ok;
        }
        try
        {
            return engine->Stop();
        }
        catch (const std::bad_alloc&)
        {
            RecordRecordingBoundaryFailure(
                engine, E_OUTOFMEMORY,
                L"Preview stop allocation failed.");
            return XbPreviewResult_NativeFailure;
        }
        catch (const std::exception&)
        {
            RecordRecordingBoundaryFailure(
                engine, E_FAIL,
                L"Preview stop raised a native exception.");
            return XbPreviewResult_NativeFailure;
        }
        catch (...)
        {
            RecordRecordingBoundaryFailure(
                engine, E_UNEXPECTED,
                L"Preview stop raised an unknown exception.");
            return XbPreviewResult_NativeFailure;
        }
    }

    XbPreviewResult XB_PREVIEW_CALL XbPreview_StartRecording(
        const XbPreviewHandle handle) noexcept
    {
        const auto engine = EngineFromHandle(handle);
        try
        {
            if (engine == nullptr)
            {
                SetApiError(L"StartRecording handle is null.");
                return XbPreviewResult_InvalidHandle;
            }
            return engine->StartRecording();
        }
        catch (const std::bad_alloc&)
        {
            RecordRecordingBoundaryFailure(
                engine, E_OUTOFMEMORY,
                L"Recording start allocation failed at the C ABI boundary.");
            return XbPreviewResult_NativeFailure;
        }
        catch (const std::exception&)
        {
            RecordRecordingBoundaryFailure(
                engine, E_FAIL,
                L"Recording start raised a native exception at the C ABI boundary.");
            return XbPreviewResult_NativeFailure;
        }
        catch (...)
        {
            RecordRecordingBoundaryFailure(
                engine, E_UNEXPECTED,
                L"Recording start raised an unknown exception at the C ABI boundary.");
            return XbPreviewResult_NativeFailure;
        }
    }

    XbPreviewResult XB_PREVIEW_CALL XbPreview_PauseRecording(
        const XbPreviewHandle handle) noexcept
    {
        const auto engine = EngineFromHandle(handle);
        try
        {
            if (engine == nullptr)
            {
                SetApiError(L"PauseRecording handle is null.");
                return XbPreviewResult_InvalidHandle;
            }
            return engine->PauseRecording();
        }
        catch (const std::bad_alloc&)
        {
            RecordRecordingBoundaryFailure(
                engine, E_OUTOFMEMORY,
                L"Recording pause allocation failed at the C ABI boundary.");
            return XbPreviewResult_NativeFailure;
        }
        catch (const std::exception&)
        {
            RecordRecordingBoundaryFailure(
                engine, E_FAIL,
                L"Recording pause raised a standard exception at the C ABI boundary.");
            return XbPreviewResult_NativeFailure;
        }
        catch (...)
        {
            RecordRecordingBoundaryFailure(
                engine, E_UNEXPECTED,
                L"Recording pause raised an unknown exception at the C ABI boundary.");
            return XbPreviewResult_NativeFailure;
        }
    }

    XbPreviewResult XB_PREVIEW_CALL XbPreview_ResumeRecording(
        const XbPreviewHandle handle) noexcept
    {
        const auto engine = EngineFromHandle(handle);
        try
        {
            if (engine == nullptr)
            {
                SetApiError(L"ResumeRecording handle is null.");
                return XbPreviewResult_InvalidHandle;
            }
            return engine->ResumeRecording();
        }
        catch (const std::bad_alloc&)
        {
            RecordRecordingBoundaryFailure(
                engine, E_OUTOFMEMORY,
                L"Recording resume allocation failed at the C ABI boundary.");
            return XbPreviewResult_NativeFailure;
        }
        catch (const std::exception&)
        {
            RecordRecordingBoundaryFailure(
                engine, E_FAIL,
                L"Recording resume raised a standard exception at the C ABI boundary.");
            return XbPreviewResult_NativeFailure;
        }
        catch (...)
        {
            RecordRecordingBoundaryFailure(
                engine, E_UNEXPECTED,
                L"Recording resume raised an unknown exception at the C ABI boundary.");
            return XbPreviewResult_NativeFailure;
        }
    }

    XbPreviewResult XB_PREVIEW_CALL XbPreview_StopRecording(
        const XbPreviewHandle handle) noexcept
    {
        const auto engine = EngineFromHandle(handle);
        try
        {
            if (engine == nullptr)
            {
                SetApiError(L"StopRecording handle is null.");
                return XbPreviewResult_InvalidHandle;
            }
            return engine->StopRecording();
        }
        catch (const std::bad_alloc&)
        {
            RecordRecordingBoundaryFailure(
                engine, E_OUTOFMEMORY,
                L"Recording stop allocation failed at the C ABI boundary.");
            return XbPreviewResult_NativeFailure;
        }
        catch (const std::exception&)
        {
            RecordRecordingBoundaryFailure(
                engine, E_FAIL,
                L"Recording stop raised a native exception at the C ABI boundary.");
            return XbPreviewResult_NativeFailure;
        }
        catch (...)
        {
            RecordRecordingBoundaryFailure(
                engine, E_UNEXPECTED,
                L"Recording stop raised an unknown exception at the C ABI boundary.");
            return XbPreviewResult_NativeFailure;
        }
    }

    XbPreviewResult XB_PREVIEW_CALL XbPreview_CancelRecording(
        const XbPreviewHandle handle) noexcept
    {
        const auto engine = EngineFromHandle(handle);
        try
        {
            if (engine == nullptr)
            {
                SetApiError(L"CancelRecording handle is null.");
                return XbPreviewResult_InvalidHandle;
            }
            return engine->CancelRecording();
        }
        catch (const std::bad_alloc&)
        {
            RecordRecordingBoundaryFailure(
                engine, E_OUTOFMEMORY,
                L"Recording cancellation allocation failed at the C ABI boundary.");
            return XbPreviewResult_NativeFailure;
        }
        catch (const std::exception&)
        {
            RecordRecordingBoundaryFailure(
                engine, E_FAIL,
                L"Recording cancellation raised a native exception at the C ABI boundary.");
            return XbPreviewResult_NativeFailure;
        }
        catch (...)
        {
            RecordRecordingBoundaryFailure(
                engine, E_UNEXPECTED,
                L"Recording cancellation raised an unknown exception at the C ABI boundary.");
            return XbPreviewResult_NativeFailure;
        }
    }

    XbPreviewResult XB_PREVIEW_CALL XbPreview_GetRecordingSnapshot(
        const XbPreviewHandle handle,
        XbRecordingSnapshot* const snapshot) noexcept
    {
        const auto engine = EngineFromHandle(handle);
        try
        {
            if (engine == nullptr)
            {
                SetApiError(L"GetRecordingSnapshot handle is null.");
                return XbPreviewResult_InvalidHandle;
            }
            if (snapshot == nullptr ||
                snapshot->structSize != sizeof(XbRecordingSnapshot) ||
                !IsCompatibleVersion(snapshot->apiVersion))
            {
                SetApiError(
                    L"RecordingSnapshot size or API major version mismatch.");
                return XbPreviewResult_AbiMismatch;
            }
            XbRecordingSnapshot value{};
            const auto result = engine->GetRecordingSnapshot(value);
            if (result == XbPreviewResult_Ok)
            {
                *snapshot = value;
            }
            return result;
        }
        catch (const std::bad_alloc&)
        {
            RecordRecordingBoundaryFailure(
                engine, E_OUTOFMEMORY,
                L"Recording Snapshot allocation failed at the C ABI boundary.");
            return XbPreviewResult_NativeFailure;
        }
        catch (const std::exception&)
        {
            RecordRecordingBoundaryFailure(
                engine, E_FAIL,
                L"Recording Snapshot raised a native exception at the C ABI boundary.");
            return XbPreviewResult_NativeFailure;
        }
        catch (...)
        {
            RecordRecordingBoundaryFailure(
                engine, E_UNEXPECTED,
                L"Recording Snapshot raised an unknown exception at the C ABI boundary.");
            return XbPreviewResult_NativeFailure;
        }
    }

    XbPreviewResult XB_PREVIEW_CALL XbPreview_SetAudioControlsV1(
        const XbPreviewHandle handle,
        const XbAudioControlsV1* const controls) noexcept
    {
        const auto engine = EngineFromHandle(handle);
        if (engine == nullptr)
        {
            SetApiError(L"SetAudioControlsV1 handle is null.");
            return XbPreviewResult_InvalidHandle;
        }
        if (controls == nullptr)
        {
            SetApiError(L"AudioControlsV1 pointer is null.");
            return XbPreviewResult_InvalidArgument;
        }
        if (controls->structSize != sizeof(XbAudioControlsV1) ||
            controls->abiVersion != XB_AUDIO_CONTROLS_ABI_VERSION_V1)
        {
            SetApiError(L"AudioControlsV1 size or version mismatch.");
            return XbPreviewResult_AbiMismatch;
        }
        const auto result = engine->SetAudioControls(*controls);
        if (result != XbPreviewResult_Ok)
        {
            SetApiError(L"AudioControlsV1 values are invalid.");
        }
        return result;
    }

    XbPreviewResult XB_PREVIEW_CALL XbPreview_GetAudioControlSnapshotV1(
        const XbPreviewHandle handle,
        XbAudioControlSnapshotV1* const snapshot) noexcept
    {
        const auto engine = EngineFromHandle(handle);
        if (engine == nullptr)
        {
            SetApiError(L"GetAudioControlSnapshotV1 handle is null.");
            return XbPreviewResult_InvalidHandle;
        }
        if (snapshot == nullptr ||
            snapshot->structSize != sizeof(XbAudioControlSnapshotV1) ||
            snapshot->abiVersion != XB_AUDIO_CONTROLS_ABI_VERSION_V1)
        {
            SetApiError(
                L"AudioControlSnapshotV1 size or version mismatch.");
            return XbPreviewResult_AbiMismatch;
        }
        XbAudioControlSnapshotV1 value{};
        const auto result = engine->GetAudioControlSnapshot(value);
        if (result == XbPreviewResult_Ok)
        {
            *snapshot = value;
        }
        return result;
    }

    XbPreviewResult XB_PREVIEW_CALL XbPreview_GetMicrophoneDeviceListV1(
        const XbPreviewHandle handle,
        XbMicrophoneDeviceListV1* const list) noexcept
    {
        const auto engine = EngineFromHandle(handle);
        if (engine == nullptr)
        {
            SetApiError(L"GetMicrophoneDeviceListV1 handle is null.");
            return XbPreviewResult_InvalidHandle;
        }
        if (list == nullptr || list->structSize != sizeof(*list) ||
            list->abiVersion != XB_MICROPHONE_DEVICE_ABI_VERSION_V1)
        {
            SetApiError(L"MicrophoneDeviceListV1 size or version mismatch.");
            return XbPreviewResult_AbiMismatch;
        }
        return engine->GetMicrophoneDeviceList(*list);
    }

    XbPreviewResult XB_PREVIEW_CALL XbPreview_GetMicrophoneDeviceV1(
        const XbPreviewHandle handle,
        XbMicrophoneDeviceV1* const device) noexcept
    {
        const auto engine = EngineFromHandle(handle);
        if (engine == nullptr)
        {
            SetApiError(L"GetMicrophoneDeviceV1 handle is null.");
            return XbPreviewResult_InvalidHandle;
        }
        if (device == nullptr || device->structSize != sizeof(*device) ||
            device->abiVersion != XB_MICROPHONE_DEVICE_ABI_VERSION_V1)
        {
            SetApiError(L"MicrophoneDeviceV1 size or version mismatch.");
            return XbPreviewResult_AbiMismatch;
        }
        return engine->GetMicrophoneDevice(*device);
    }

    XbPreviewResult XB_PREVIEW_CALL XbPreview_SetMicrophoneSelectionV1(
        const XbPreviewHandle handle,
        const XbMicrophoneSelectionV1* const selection) noexcept
    {
        const auto engine = EngineFromHandle(handle);
        if (engine == nullptr)
        {
            SetApiError(L"SetMicrophoneSelectionV1 handle is null.");
            return XbPreviewResult_InvalidHandle;
        }
        if (selection == nullptr ||
            selection->structSize != sizeof(*selection) ||
            selection->abiVersion != XB_MICROPHONE_DEVICE_ABI_VERSION_V1)
        {
            SetApiError(L"MicrophoneSelectionV1 size or version mismatch.");
            return XbPreviewResult_AbiMismatch;
        }
        return engine->SetMicrophoneSelection(*selection);
    }

    XbPreviewResult XB_PREVIEW_CALL XbPreview_GetMicrophoneSelectionV1(
        const XbPreviewHandle handle,
        XbMicrophoneSelectionSnapshotV1* const snapshot) noexcept
    {
        const auto engine = EngineFromHandle(handle);
        if (engine == nullptr)
        {
            SetApiError(L"GetMicrophoneSelectionV1 handle is null.");
            return XbPreviewResult_InvalidHandle;
        }
        if (snapshot == nullptr || snapshot->structSize != sizeof(*snapshot) ||
            snapshot->abiVersion != XB_MICROPHONE_DEVICE_ABI_VERSION_V1)
        {
            SetApiError(
                L"MicrophoneSelectionSnapshotV1 size or version mismatch.");
            return XbPreviewResult_AbiMismatch;
        }
        return engine->GetMicrophoneSelection(*snapshot);
    }

    XbPreviewResult XB_PREVIEW_CALL XbPreview_Resize(
        const XbPreviewHandle handle,
        const std::int32_t width,
        const std::int32_t height) noexcept
    {
        const auto engine = EngineFromHandle(handle);
        if (engine == nullptr)
        {
            SetApiError(L"Resize handle 为空。");
            return XbPreviewResult_InvalidHandle;
        }
        return engine->Resize(width, height);
    }

    XbPreviewResult XB_PREVIEW_CALL XbPreview_SetGpuExportTargetSize(
        const XbPreviewHandle handle,
        const std::int32_t width,
        const std::int32_t height) noexcept
    {
        const auto engine = EngineFromHandle(handle);
        if (engine == nullptr)
        {
            SetApiError(L"SetGpuExportTargetSize handle is null.");
            return XbPreviewResult_InvalidHandle;
        }
        return engine->SetGpuExportTargetSize(width, height);
    }

    XbPreviewResult XB_PREVIEW_CALL XbPreview_SetSessionGeometry(
        const XbPreviewHandle handle,
        const XbPreviewSessionGeometryV1* const geometry) noexcept
    {
        const auto engine = EngineFromHandle(handle);
        if (engine == nullptr)
        {
            SetApiError(L"SetSessionGeometry handle is null.");
            return XbPreviewResult_InvalidHandle;
        }
        if (geometry == nullptr)
        {
            SetApiError(L"SessionGeometryV1 pointer is null.");
            return XbPreviewResult_InvalidArgument;
        }
        return engine->SetSessionGeometry(*geometry);
    }

    XbPreviewResult XB_PREVIEW_CALL XbPreview_SetCameraState(
        const XbPreviewHandle handle,
        const XbCameraState* const cameraState) noexcept
    {
        const auto engine = EngineFromHandle(handle);
        if (engine == nullptr)
        {
            SetApiError(L"SetCameraState handle is null.");
            return XbPreviewResult_InvalidHandle;
        }
        if (cameraState == nullptr)
        {
            SetApiError(L"Camera state pointer is null.");
            return XbPreviewResult_InvalidArgument;
        }
        return engine->SetCameraState(*cameraState);
    }

    XbPreviewResult XB_PREVIEW_CALL XbPreview_SetCursorMode(
        const XbPreviewHandle handle,
        const XbCursorMode cursorMode) noexcept
    {
        const auto engine = EngineFromHandle(handle);
        if (engine == nullptr)
        {
            SetApiError(L"SetCursorMode handle is null.");
            return XbPreviewResult_InvalidHandle;
        }
        return engine->SetCursorMode(cursorMode);
    }

    XbPreviewResult XB_PREVIEW_CALL XbPreview_SetRecordCursorVisible(
        const XbPreviewHandle handle,
        const std::uint32_t visible) noexcept
    {
        const auto engine = EngineFromHandle(handle);
        if (engine == nullptr)
        {
            SetApiError(L"SetRecordCursorVisible handle is null.");
            return XbPreviewResult_InvalidHandle;
        }
        if (visible > 1)
        {
            SetApiError(L"Record cursor visibility must be zero or one.");
            return XbPreviewResult_InvalidArgument;
        }
        return engine->SetRecordCursorVisible(visible != 0);
    }

    XbPreviewResult XB_PREVIEW_CALL XbPreview_GetRecordCursorVisible(
        const XbPreviewHandle handle,
        std::uint32_t* const requestedVisible,
        std::uint32_t* const appliedVisible,
        std::uint64_t* const revision) noexcept
    {
        const auto engine = EngineFromHandle(handle);
        if (engine == nullptr)
        {
            SetApiError(L"GetRecordCursorVisible handle is null.");
            return XbPreviewResult_InvalidHandle;
        }
        if (requestedVisible == nullptr || appliedVisible == nullptr ||
            revision == nullptr)
        {
            SetApiError(L"Record cursor visibility output pointer is null.");
            return XbPreviewResult_InvalidArgument;
        }
        return engine->GetRecordCursorVisible(
            *requestedVisible,
            *appliedVisible,
            *revision);
    }

    XbPreviewResult XB_PREVIEW_CALL XbPreview_SetAudioProgramMode(
        const XbPreviewHandle handle,
        const XbAudioProgramMode mode) noexcept
    {
        const auto engine = EngineFromHandle(handle);
        if (engine == nullptr)
        {
            SetApiError(L"SetAudioProgramMode handle is null.");
            return XbPreviewResult_InvalidHandle;
        }
        const auto result = engine->SetAudioProgramMode(mode);
        if (result != XbPreviewResult_Ok)
        {
            SetApiError(
                L"Audio program mode is invalid or recording is active.");
        }
        return result;
    }

    XbPreviewResult XB_PREVIEW_CALL XbPreview_SetCaptureTarget(
        const XbPreviewHandle handle,
        const XbCaptureTargetKind targetKind,
        const std::uint64_t windowHandle) noexcept
    {
        const auto engine = EngineFromHandle(handle);
        if (engine == nullptr)
        {
            SetApiError(L"SetCaptureTarget handle is null.");
            return XbPreviewResult_InvalidHandle;
        }
        return engine->SetCaptureTarget(
            targetKind,
            reinterpret_cast<HWND>(
                static_cast<std::uintptr_t>(windowHandle)));
    }

    XbPreviewResult XB_PREVIEW_CALL XbPreview_SetWindowStagePose(
        const XbPreviewHandle handle,
        const XbWindowStageOrientation orientation,
        const XbWindowStageLevel level) noexcept
    {
        const auto engine = EngineFromHandle(handle);
        if (engine == nullptr)
        {
            SetApiErrorNoThrow(L"SetWindowStagePose handle is null.");
            return XbPreviewResult_InvalidHandle;
        }
        return engine->SetWindowStagePose(orientation, level);
    }

    XbPreviewResult XB_PREVIEW_CALL XbPreview_SetWindowShowcasePose(
        const XbPreviewHandle handle,
        const XbWindowStageOrientation orientation,
        const XbWindowStageLevel level,
        const std::uint32_t active) noexcept
    {
        const auto engine = EngineFromHandle(handle);
        if (engine == nullptr)
        {
            SetApiErrorNoThrow(L"SetWindowShowcasePose handle is null.");
            return XbPreviewResult_InvalidHandle;
        }
        return engine->SetWindowShowcasePose(orientation, level, active);
    }

    XbPreviewResult XB_PREVIEW_CALL
        XbPreview_SetWindowShowcaseBackgroundPreset(
            const XbPreviewHandle handle,
            const XbWindowShowcaseBackgroundPreset preset) noexcept
    {
        const auto engine = EngineFromHandle(handle);
        if (engine == nullptr)
        {
            SetApiErrorNoThrow(
                L"SetWindowShowcaseBackgroundPreset handle is null.");
            return XbPreviewResult_InvalidHandle;
        }
        return engine->SetWindowShowcaseBackgroundPreset(preset);
    }

    XbPreviewResult XB_PREVIEW_CALL
        XbPreview_SetWindowShowcaseCustomBackground(
            const XbPreviewHandle handle,
            const wchar_t* const validatedLocalPath) noexcept
    {
        const auto engine = EngineFromHandle(handle);
        if (engine == nullptr)
        {
            SetApiErrorNoThrow(
                L"SetWindowShowcaseCustomBackground handle is null.");
            return XbPreviewResult_InvalidHandle;
        }
        try
        {
            return engine->SetWindowShowcaseCustomBackground(
                validatedLocalPath);
        }
        catch (const std::bad_alloc&)
        {
            SetApiErrorNoThrow(
                L"Custom background allocation failed.");
            return XbPreviewResult_NativeFailure;
        }
        catch (...)
        {
            SetApiErrorNoThrow(
                L"Custom background raised an unexpected exception.");
            return XbPreviewResult_NativeFailure;
        }
    }

    XbPreviewResult XB_PREVIEW_CALL XbPreview_SetRecordingOutputRoot(
        const XbPreviewHandle handle,
        const wchar_t* const validatedLocalPath) noexcept
    {
        const auto engine = EngineFromHandle(handle);
        if (engine == nullptr)
        {
            SetApiErrorNoThrow(L"SetRecordingOutputRoot handle is null.");
            return XbPreviewResult_InvalidHandle;
        }
        try
        {
            return engine->SetRecordingOutputRoot(validatedLocalPath);
        }
        catch (const std::bad_alloc&)
        {
            SetApiErrorNoThrow(L"Recording output root allocation failed.");
            return XbPreviewResult_NativeFailure;
        }
        catch (...)
        {
            SetApiErrorNoThrow(
                L"Recording output root raised an unexpected exception.");
            return XbPreviewResult_NativeFailure;
        }
    }

    XbPreviewResult XB_PREVIEW_CALL XbPreview_SetRecordingFrameRate(
        const XbPreviewHandle handle,
        const std::uint32_t framesPerSecond) noexcept
    {
        const auto engine = EngineFromHandle(handle);
        if (engine == nullptr)
        {
            SetApiErrorNoThrow(L"SetRecordingFrameRate handle is null.");
            return XbPreviewResult_InvalidHandle;
        }
        try
        {
            return engine->SetRecordingFrameRate(framesPerSecond);
        }
        catch (...)
        {
            SetApiErrorNoThrow(
                L"Recording frame rate raised an unexpected exception.");
            return XbPreviewResult_NativeFailure;
        }
    }

    XbPreviewResult XB_PREVIEW_CALL XbPreview_GetCursorStats(
        const XbPreviewHandle handle,
        XbCursorStats* const stats) noexcept
    {
        const auto engine = EngineFromHandle(handle);
        if (engine == nullptr)
        {
            SetApiError(L"GetCursorStats handle is null.");
            return XbPreviewResult_InvalidHandle;
        }
        if (stats == nullptr ||
            stats->structSize != sizeof(XbCursorStats) ||
            !IsCompatibleVersion(stats->apiVersion))
        {
            SetApiError(L"CursorStats size or API major version mismatch.");
            return XbPreviewResult_AbiMismatch;
        }

        XbCursorStats snapshot{};
        const auto result = engine->GetCursorStats(snapshot);
        if (result == XbPreviewResult_Ok)
        {
            *stats = snapshot;
        }
        return result;
    }

    XbPreviewResult XB_PREVIEW_CALL XbPreview_GetStats(
        const XbPreviewHandle handle,
        XbPreviewStats* const stats) noexcept
    {
        const auto engine = EngineFromHandle(handle);
        if (engine == nullptr)
        {
            SetApiError(L"GetStats handle 为空。");
            return XbPreviewResult_InvalidHandle;
        }
        if (stats == nullptr ||
            stats->structSize != sizeof(XbPreviewStats) ||
            !IsCompatibleVersion(stats->apiVersion))
        {
            SetApiError(L"Stats 大小或 API 主版本不匹配。");
            return XbPreviewResult_AbiMismatch;
        }

        XbPreviewStats snapshot{};
        const auto result = engine->GetStats(snapshot);
        if (result == XbPreviewResult_Ok)
        {
            *stats = snapshot;
        }
        return result;
    }

    XbPreviewResult XB_PREVIEW_CALL XbPreview_GetGpuExportFrameV1(
        const XbPreviewHandle handle,
        XbPreviewGpuExportFrameV1* const frame) noexcept
    {
        const auto engine = EngineFromHandle(handle);
        if (engine == nullptr)
        {
            SetApiError(L"GetGpuExportFrameV1 handle is null.");
            return XbPreviewResult_InvalidHandle;
        }
        if (frame == nullptr ||
            frame->structSize != sizeof(XbPreviewGpuExportFrameV1) ||
            frame->version != XB_GPU_EXPORT_ABI_VERSION_V1)
        {
            SetApiError(L"GPU export frame ABI mismatch.");
            return XbPreviewResult_AbiMismatch;
        }

        XbPreviewGpuExportFrameV1 snapshot{};
        const auto result = engine->GetGpuExportFrame(snapshot);
        if (result == XbPreviewResult_Ok)
        {
            *frame = snapshot;
        }
        return result;
    }

    XbPreviewResult XB_PREVIEW_CALL XbPreview_GetLastError(
        const XbPreviewHandle handle,
        wchar_t* const buffer,
        const std::uint32_t bufferLength) noexcept
    {
        if (buffer == nullptr || bufferLength == 0)
        {
            return XbPreviewResult_InvalidArgument;
        }

        const auto engine = EngineFromHandle(handle);
        const auto message = engine != nullptr
            ? engine->LastError()
            : LastApiError;
        wcsncpy_s(
            buffer,
            static_cast<std::size_t>(bufferLength),
            message.c_str(),
            _TRUNCATE);
        return XbPreviewResult_Ok;
    }

    XbPreviewResult XB_PREVIEW_CALL XbPreview_Destroy(
        XbPreviewHandle* const handle) noexcept
    {
        if (handle == nullptr)
        {
            return XbPreviewResult_InvalidArgument;
        }
        if (*handle == nullptr)
        {
            return XbPreviewResult_Ok;
        }

        auto engine = EngineFromHandle(*handle);
        *handle = nullptr;
        const auto stopResult = engine->Stop();
        delete engine;
        return stopResult;
    }

    XbPreviewResult XB_PREVIEW_CALL XbPreview_CalculateLetterbox(
        const std::uint32_t sourceWidth,
        const std::uint32_t sourceHeight,
        const std::uint32_t destinationWidth,
        const std::uint32_t destinationHeight,
        XbLetterboxRect* const rect) noexcept
    {
        if (rect == nullptr)
        {
            SetApiError(L"Letterbox 输出指针为空。");
            return XbPreviewResult_InvalidArgument;
        }

        if (!xbpreview::CalculateLetterbox(
            sourceWidth,
            sourceHeight,
            destinationWidth,
            destinationHeight,
            *rect))
        {
            SetApiError(L"Letterbox 宽高必须大于零。");
            return XbPreviewResult_InvalidArgument;
        }
        return XbPreviewResult_Ok;
    }
}
