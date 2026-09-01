#pragma once

#include "AudioEndpointLevelMonitor.h"
#include "DiagnosticLogger.h"
#include "GStreamerMicrophoneDeviceMonitor.h"
#include "CameraTransform.h"
#include "CaptureTarget.h"
#include "CameraStateStore.h"
#include "CursorCaptureState.h"
#include "CursorDiagnosticLogger.h"
#include "CursorMode.h"
#include "CursorShapeCache.h"
#include "CursorStateProvider.h"
#include "CropTransform.h"
#include "LatencyStatistics.h"
#include "MicPreflightLevelMonitor.h"
#include "PreviewRenderer.h"
#include "SessionGeometryStore.h"
#include "PreviewStateMachine.h"
#include "XbPreviewApi.h"

#include <winrt/Windows.Foundation.h>
#include <winrt/Windows.Graphics.Capture.h>

#include <atomic>
#include <condition_variable>
#include <cstdint>
#include <memory>
#include <mutex>
#include <optional>
#include <string>
#include <thread>

namespace xbpreview
{
    class PreviewEngine final
    {
    public:
        PreviewEngine(
            HWND previewHwnd,
            HWND exclusionHwnd,
            const XbPreviewCreateOptions& options);
        ~PreviewEngine();

        PreviewEngine(const PreviewEngine&) = delete;
        PreviewEngine& operator=(const PreviewEngine&) = delete;

        XbPreviewResult Start() noexcept;
        XbPreviewResult Stop() noexcept;
        XbPreviewResult StartRecording();
        XbPreviewResult PauseRecording();
        XbPreviewResult ResumeRecording();
        XbPreviewResult SetAudioProgramMode(
            XbAudioProgramMode mode) noexcept;
        XbPreviewResult StopRecording();
        XbPreviewResult CancelRecording();
        XbPreviewResult GetRecordingSnapshot(
            XbRecordingSnapshot& snapshot) const;
        XbPreviewResult SetAudioControls(
            const XbAudioControlsV1& controls) noexcept;
        XbPreviewResult GetAudioControlSnapshot(
            XbAudioControlSnapshotV1& snapshot) const noexcept;
        XbPreviewResult GetMicrophoneDeviceList(
            XbMicrophoneDeviceListV1& list) const noexcept;
        XbPreviewResult GetMicrophoneDevice(
            XbMicrophoneDeviceV1& device) const noexcept;
        XbPreviewResult SetMicrophoneSelection(
            const XbMicrophoneSelectionV1& selection) noexcept;
        XbPreviewResult GetMicrophoneSelection(
            XbMicrophoneSelectionSnapshotV1& snapshot) const noexcept;
        void RecordRecordingBoundaryFailure(
            XbPreviewResult result,
            HRESULT hresult,
            const wchar_t* message);
        XbPreviewResult Resize(std::int32_t width, std::int32_t height) noexcept;
        XbPreviewResult SetGpuExportTargetSize(
            std::int32_t width,
            std::int32_t height) noexcept;
        XbPreviewResult SetSessionGeometry(
            const XbPreviewSessionGeometryV1& geometry) noexcept;
        XbPreviewResult SetCameraState(const XbCameraState& cameraState) noexcept;
        XbPreviewResult SetCursorMode(XbCursorMode cursorMode) noexcept;
        XbPreviewResult SetRecordCursorVisible(bool visible) noexcept;
        XbPreviewResult GetRecordCursorVisible(
            std::uint32_t& requestedVisible,
            std::uint32_t& appliedVisible,
            std::uint64_t& revision) const noexcept;
        XbPreviewResult SetCaptureTarget(
            XbCaptureTargetKind targetKind,
            HWND window) noexcept;
        XbPreviewResult SetWindowStagePose(
            XbWindowStageOrientation orientation,
            XbWindowStageLevel level) noexcept;
        XbPreviewResult SetWindowShowcasePose(
            XbWindowStageOrientation orientation,
            XbWindowStageLevel level,
            std::uint32_t active) noexcept;
        XbPreviewResult SetWindowShowcaseBackgroundPreset(
            XbWindowShowcaseBackgroundPreset preset) noexcept;
        XbPreviewResult SetWindowShowcaseCustomBackground(
            const wchar_t* validatedLocalPath);
        XbPreviewResult SetRecordingOutputRoot(
            const wchar_t* validatedLocalPath);
        XbPreviewResult SetRecordingFrameRate(
            std::uint32_t framesPerSecond) noexcept;
        XbPreviewResult GetStats(XbPreviewStats& stats) const noexcept;
        XbPreviewResult GetGpuExportFrame(
            XbPreviewGpuExportFrameV1& snapshot) const noexcept;
        XbPreviewResult GetCursorStats(XbCursorStats& stats) const noexcept;
        std::wstring LastError() const;

    private:
        struct PendingFrame
        {
            winrt::Windows::Graphics::Capture::Direct3D11CaptureFrame frame{ nullptr };
            winrt::Windows::Graphics::SizeInt32 contentSize{};
            bool systemRelativeTimeValid{};
            std::int64_t systemRelativeTime100ns{};
            std::int64_t arrivalQpc{};
        };

        struct CallbackGate
        {
            PreviewEngine* Enter() noexcept
            {
                std::lock_guard lock(mutex);
                if (owner == nullptr)
                {
                    return nullptr;
                }
                ++activeCallbacks;
                return owner;
            }

            void Leave() noexcept
            {
                std::lock_guard lock(mutex);
                if (--activeCallbacks == 0)
                {
                    condition.notify_all();
                }
            }

            void Activate(PreviewEngine* const value) noexcept
            {
                std::lock_guard lock(mutex);
                owner = value;
            }

            void DeactivateAndWait() noexcept
            {
                std::unique_lock lock(mutex);
                owner = nullptr;
                condition.wait(
                    lock,
                    [this]
                    {
                        return activeCallbacks == 0;
                    });
            }

            std::mutex mutex;
            std::condition_variable condition;
            PreviewEngine* owner{};
            std::uint32_t activeCallbacks{};
        };

        void WorkerMain() noexcept;
        void InitializeWorkerResources();
        void ShutdownWorkerResources() noexcept;
        void WorkerLoop();
        void OnFrameArrived(
            const winrt::Windows::Graphics::Capture::Direct3D11CaptureFramePool& sender) noexcept;
        void OnCaptureClosed() noexcept;
        void ApplyPendingResize();
        void ProcessPendingFrame(PendingFrame&& pending);
        void RecreateFramePool(const winrt::Windows::Graphics::SizeInt32& size);
        void UpdateRatesAndDiagnostics(bool force);
        void UpdateWindowVisibility();
        void ApplyWindowDisplayAffinity() noexcept;
        void ResetSessionStats();
        void ResetCursorStats();
        void ConfigureCaptureSessionCursorMode();
        void WriteCursorDiagnostic(const char* event) noexcept;
        void SetState(XbPreviewState state) noexcept;
        void SetError(XbPreviewResult result, const std::wstring& message) noexcept;
        void SetErrorFromHresult(
            XbPreviewResult result,
            HRESULT hresult,
            const std::wstring& context) noexcept;
        void StartMicPreflightLocked() noexcept;
        [[nodiscard]] AudioEndpointLevelAssignment
            GetAudioEndpointLevelAssignment() const noexcept;

        [[nodiscard]] static HMONITOR PrimaryMonitor() noexcept;
        [[nodiscard]] static std::wstring GuidToString(const GUID& value);
        [[nodiscard]] static std::int64_t QueryQpc() noexcept;
        [[nodiscard]] double QpcToMilliseconds(std::int64_t ticks) const noexcept;
        [[nodiscard]] std::int64_t QpcTo100Nanoseconds(std::int64_t ticks) const noexcept;

        HWND previewHwnd_{};
        HWND exclusionHwnd_{};
        CaptureTarget captureTarget_{};
        bool allowWarp_{};
        std::uint32_t framePoolBufferCount_{ 2 };
        std::uint32_t statsIntervalMilliseconds_{ 1000 };
        std::wstring diagnosticLogDirectory_;
        std::optional<AudioProgramMode> recordingAudioProgramMode_;
        XbMicrophoneSelectionKindV1 microphoneSelectionKind_{
            XbMicrophoneSelectionKindV1_WindowsDefault };
        std::wstring microphoneSelectionEndpointId_;
        std::wstring microphoneSelectionDisplayName_;
        std::shared_ptr<GStreamerMicrophoneDeviceBinding>
            activeMicrophoneDevice_;
        std::wstring activeSystemAudioEndpointId_;
        GStreamerMicrophoneDeviceMonitor microphoneDeviceMonitor_;
        MicPreflightLevelMonitor microphonePreflightLevelMonitor_;
        AudioEndpointLevelMonitor audioEndpointLevelMonitor_;

        mutable std::mutex lifecycleMutex_;
        std::condition_variable lifecycleCondition_;
        PreviewStateMachine stateMachine_;
        SessionGeometryStore sessionGeometryStore_;
        std::thread worker_;
        std::atomic<bool> stopRequested_{ false };
        std::atomic<bool> acceptingFrames_{ false };
        std::atomic<bool> captureClosed_{ false };
        std::atomic<bool> callbackFailed_{ false };
        std::atomic<bool> firstFrameDiagnosticWritten_{ false };
        std::atomic<bool> recordCursorVisible_{ true };
        std::atomic<bool> appliedRecordCursorVisible_{ true };
        std::atomic<std::uint64_t> cursorPresentationRevision_{};
        std::atomic<std::uint64_t> cursorPresentationFailureCount_{};
        std::shared_ptr<CallbackGate> callbackGate_;

        mutable std::mutex productConfigurationMutex_;
        WindowStageDirection productStageDirection_{
            WindowStageDirection::Right };
        WindowStageStrength productStageStrength_{
            WindowStageStrength::Level2 };
        bool productStageActive_{ true };
        WindowShowcaseBackgroundPreset productBackgroundPreset_{
            WindowShowcaseBackgroundPreset::Warm };
        bool productCustomBackground_{};
        std::wstring productCustomBackgroundPath_;
        std::wstring productRecordingOutputRoot_;
        std::uint32_t productRecordingFrameRate_{
            VideoEncoderDefaultFrameRate };

        mutable std::mutex frameMutex_;
        std::condition_variable frameCondition_;
        std::optional<PendingFrame> pendingFrame_;

        mutable std::mutex resizeMutex_;
        std::uint32_t requestedPreviewWidth_{};
        std::uint32_t requestedPreviewHeight_{};
        std::uint64_t requestedResizeGeneration_{};
        std::uint64_t appliedResizeGeneration_{};

        mutable std::mutex statsMutex_;
        XbPreviewStats stats_{};
        CameraStateStore cameraStateStore_;
        mutable std::mutex cursorStatsMutex_;
        XbCursorStats cursorStats_{};
        XbCursorMode requestedCursorMode_{ XbCursorMode_SystemCursor };
        CursorModeDecision cursorModeDecision_{};
        mutable std::mutex errorMutex_;
        std::wstring lastError_;

        std::int64_t qpcFrequency_{};
        std::int64_t lastRateQpc_{};
        std::uint64_t previousCaptureCount_{};
        std::uint64_t previousPresentCount_{};
        std::uint64_t previousCameraUpdateCount_{};
        bool previouslyMinimized_{};

        GUID sessionGuid_{};
        std::wstring sessionGuidString_;
        LatencyStatistics latencyStatistics_;
        DiagnosticLogger logger_;
        StartupDiagnostics startupDiagnostics_;
        CursorDiagnosticLogger cursorLogger_;
        PreviewRenderer renderer_;
        CursorStateProvider cursorStateProvider_;
        CursorShapeCache cursorShapeCache_;
        MonitorPixelRect captureMonitorRect_{};

        winrt::Windows::Graphics::Capture::GraphicsCaptureItem captureItem_{ nullptr };
        winrt::Windows::Graphics::Capture::Direct3D11CaptureFramePool framePool_{ nullptr };
        winrt::Windows::Graphics::Capture::GraphicsCaptureSession captureSession_{ nullptr };
        winrt::event_token frameArrivedToken_{};
        winrt::event_token captureClosedToken_{};
        winrt::Windows::Graphics::SizeInt32 captureSize_{};
        std::optional<XbPreviewSessionGeometryV1> activeGeometry_;
        CropTransform activeCrop_{};
    };
}
