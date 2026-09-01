#pragma once

#include "GStreamerAudioMode.h"

#include <windows.h>

#include <filesystem>
#include <memory>
#include <string>

namespace xbpreview
{
    class GStreamerMicrophoneDeviceBinding;

    inline constexpr unsigned GStreamerAudioVersionMajor = 1;
    inline constexpr unsigned GStreamerAudioVersionMinor = 28;
    inline constexpr unsigned GStreamerAudioVersionMicro = 6;

    enum class GStreamerAudioPipelineState
    {
        Idle,
        Starting,
        Playing,
        Paused,
        EndOfStream,
        Stopped,
        Failed,
    };

    struct GStreamerAudioConfig
    {
        GStreamerAudioMode mode{ GStreamerAudioMode::None };
        std::filesystem::path workingDirectory;
        bool injectInitializationFailure{};
#if defined(XBPREVIEW_GSTREAMER_AUDIO_TESTS)
        bool simulateMissingMicrophone{};
        std::wstring simulateMissingMicrophoneEndpointId;
#endif
        std::shared_ptr<GStreamerMicrophoneDeviceBinding> microphoneDevice;
    };

    struct GStreamerAudioSnapshot
    {
        GStreamerAudioMode audioMode{ GStreamerAudioMode::None };
        bool systemActive{};
        bool micActive{};
        std::wstring micDeviceId;
        std::wstring micDeviceDisplayName;
        std::wstring micDeviceProperties;
        std::wstring micElementDeviceId;
        bool micSessionBound{};
        bool micSourceCreatedFromDevice{};
        bool micElementIdentityMatches{};
        bool micDisconnected{};
        bool micSourceDataBlocked{};
        GStreamerAudioPipelineState pipelineState{
            GStreamerAudioPipelineState::Idle };
        std::wstring lastGStreamerError;
        std::filesystem::path audioWorkingPath;
        std::filesystem::path systemWorkingPath;
        std::filesystem::path microphoneWorkingPath;
        HRESULT terminalHResult{ S_OK };
        bool deviceMonitorActive{};
        bool endOfStreamObserved{};
        bool filesClosed{};
        bool busThreadExited{};
        bool mixerVolumesFixedAtUnity{};
        bool dualSourcesIndependent{};
    };

    [[nodiscard]] const char* GStreamerAudioModeName(
        GStreamerAudioMode mode) noexcept;
    [[nodiscard]] const char* GStreamerAudioPipelineStateName(
        GStreamerAudioPipelineState state) noexcept;
    [[nodiscard]] const char* GStreamerAudioPipelineDescription(
        GStreamerAudioMode mode) noexcept;
    [[nodiscard]] HRESULT EnsureGStreamerAudioRuntime(
        std::wstring& errorText) noexcept;

    class GStreamerAudioCore final
    {
    public:
        GStreamerAudioCore();
        ~GStreamerAudioCore();
        GStreamerAudioCore(const GStreamerAudioCore&) = delete;
        GStreamerAudioCore& operator=(const GStreamerAudioCore&) = delete;

        [[nodiscard]] HRESULT Start(
            const GStreamerAudioConfig& config) noexcept;
        [[nodiscard]] HRESULT Pause() noexcept;
        [[nodiscard]] HRESULT Resume() noexcept;
        [[nodiscard]] HRESULT Stop() noexcept;
        [[nodiscard]] GStreamerAudioSnapshot Snapshot() const noexcept;

#if defined(XBPREVIEW_GSTREAMER_AUDIO_TESTS)
        void TestLockMicrophoneDeviceId(const std::string& deviceId);
        void TestInjectDeviceEvent(
            const std::string& deviceId,
            bool added);
        void TestInjectLockedMicrophoneRemoval();
#endif

    private:
        struct Impl;
        std::unique_ptr<Impl> impl_;
    };
}
