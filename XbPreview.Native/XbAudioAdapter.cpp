#include "XbAudioAdapter.h"

#include "screenrecorderlib-audio/AudioManager.h"
#include "screenrecorderlib-audio/CommonTypes.h"

#include <mfapi.h>
#include <mferror.h>

#include <limits>
#include <mutex>
#include <new>
#include <type_traits>
#include <utility>

namespace xbpreview
{
    namespace
    {
        constexpr std::uint32_t XbAudioSampleRate = 48'000;
        constexpr std::uint32_t XbAudioChannels = 2;
        constexpr std::uint32_t XbAudioBitsPerSample = 16;

        bool UsesMicrophone(const XbAudioAdapterMode mode) noexcept
        {
            return mode == XbAudioAdapterMode::MicrophoneOnly ||
                mode == XbAudioAdapterMode::Dual;
        }

        bool UsesSystemAudio(const XbAudioAdapterMode mode) noexcept
        {
            return mode == XbAudioAdapterMode::SystemOnly ||
                mode == XbAudioAdapterMode::Dual;
        }

        bool IsCaptureMode(const XbAudioAdapterMode mode) noexcept
        {
            return UsesMicrophone(mode) || UsesSystemAudio(mode);
        }
    }

    struct XbAudioAdapter::Impl final
    {
        ~Impl()
        {
            ShutdownNoThrow();
        }

        HRESULT Start(
            const XbAudioAdapterMode requestedMode,
            const std::wstring& microphoneEndpointId,
            const std::wstring& renderEndpointId) noexcept
        {
            try
            {
                std::lock_guard lock(mutex);
                if (!IsCaptureMode(requestedMode))
                {
                    lastHResult = E_INVALIDARG;
                    return lastHResult;
                }
                if (captureRunning || state == XbAudioAdapterState::Starting)
                {
                    lastHResult = HRESULT_FROM_WIN32(ERROR_BUSY);
                    return lastHResult;
                }

                // A new Start explicitly abandons an unconsumed post-Stop tail.
                // This is lifecycle cleanup only; no audio data is transformed.
                const auto priorShutdownResult = ReleaseRuntimeLocked();
                if (FAILED(priorShutdownResult))
                {
                    state = XbAudioAdapterState::Failed;
                    lastHResult = priorShutdownResult;
                    return lastHResult;
                }

                mode = requestedMode;
                state = XbAudioAdapterState::Starting;
                lastHResult = S_OK;
                pullCount = 0;
                pcmBytesDelivered = 0;
                postStopDrainAvailable = false;

                const auto startupResult =
                    MFStartup(MF_VERSION, MFSTARTUP_LITE);
                if (FAILED(startupResult))
                {
                    state = XbAudioAdapterState::Failed;
                    lastHResult = startupResult;
                    return lastHResult;
                }
                mediaFoundationStarted = true;

                options = std::make_shared<AUDIO_OPTIONS>();
                options->SetAudioEnabled(true);
                options->SetAudioChannels(XbAudioChannels);
                options->SetInputDeviceEnabled(
                    UsesMicrophone(requestedMode));
                options->SetOutputDeviceEnabled(
                    UsesSystemAudio(requestedMode));
                options->SetInputDevice(microphoneEndpointId);
                options->SetOutputDevice(renderEndpointId);
                options->SetInputVolume(1.0f);
                options->SetOutputVolume(1.0f);

                if (options->GetAudioSamplesPerSecond() !=
                        XbAudioSampleRate ||
                    options->GetAudioChannels() != XbAudioChannels ||
                    options->GetAudioBitsPerSample() !=
                        XbAudioBitsPerSample)
                {
                    return FailStartLocked(E_UNEXPECTED);
                }

                audioManager = std::make_unique<AudioManager>();
                const auto initializeResult =
                    audioManager->Initialize(options);
                if (FAILED(initializeResult))
                {
                    return FailStartLocked(initializeResult);
                }

                const auto startResult = audioManager->StartCapture();
                if (startResult != S_OK)
                {
                    return FailStartLocked(
                        FAILED(startResult) ? startResult : E_FAIL);
                }

                captureRunning = true;
                state = XbAudioAdapterState::Running;
                lastHResult = S_OK;
                return S_OK;
            }
            catch (const std::bad_alloc&)
            {
                return FailStartAfterException(E_OUTOFMEMORY);
            }
            catch (...)
            {
                return FailStartAfterException(E_UNEXPECTED);
            }
        }

        HRESULT PullMixedPcm(
            const std::uint64_t duration100ns,
            std::vector<std::uint8_t>& mixedPcm) noexcept
        {
            mixedPcm.clear();
            if (duration100ns == 0)
            {
                return E_INVALIDARG;
            }

            try
            {
                std::lock_guard lock(mutex);
                const bool isPostStopDrain =
                    !captureRunning && postStopDrainAvailable;
                if (audioManager == nullptr ||
                    (!captureRunning && !isPostStopDrain))
                {
                    lastHResult = MF_E_INVALIDREQUEST;
                    return lastHResult;
                }

                if (captureRunning)
                {
                    const auto captureResult =
                        audioManager->GetCaptureResult();
                    if (FAILED(captureResult))
                    {
                        state = XbAudioAdapterState::Failed;
                        lastHResult = captureResult;
                        return captureResult;
                    }
                }

                static_assert(
                    std::is_same_v<BYTE, std::uint8_t>,
                    "The adapter requires Windows BYTE to be uint8_t.");
                auto bytes = audioManager->GrabAudioFrame(duration100ns);
                if (captureRunning)
                {
                    const auto captureResult =
                        audioManager->GetCaptureResult();
                    if (FAILED(captureResult))
                    {
                        state = XbAudioAdapterState::Failed;
                        lastHResult = captureResult;
                        return captureResult;
                    }
                }
                mixedPcm = std::move(bytes);
                ++pullCount;
                if (pcmBytesDelivered <=
                    (std::numeric_limits<std::uint64_t>::max)() -
                        mixedPcm.size())
                {
                    pcmBytesDelivered +=
                        static_cast<std::uint64_t>(mixedPcm.size());
                }
                else
                {
                    pcmBytesDelivered =
                        (std::numeric_limits<std::uint64_t>::max)();
                }

                HRESULT releaseResult = S_OK;
                if (isPostStopDrain)
                {
                    postStopDrainAvailable = false;
                    releaseResult = ReleaseRuntimeLocked();
                }
                lastHResult = releaseResult;
                if (FAILED(releaseResult))
                {
                    state = XbAudioAdapterState::Failed;
                }
                return releaseResult;
            }
            catch (const std::bad_alloc&)
            {
                return FailPull(E_OUTOFMEMORY);
            }
            catch (...)
            {
                return FailPull(E_UNEXPECTED);
            }
        }

        HRESULT ClearRecordedPcm() noexcept
        {
            try
            {
                std::lock_guard lock(mutex);
                if (!captureRunning || audioManager == nullptr)
                {
                    lastHResult = MF_E_INVALIDREQUEST;
                    return lastHResult;
                }
                const auto before = audioManager->GetCaptureResult();
                if (FAILED(before))
                {
                    state = XbAudioAdapterState::Failed;
                    lastHResult = before;
                    return before;
                }
                audioManager->ClearRecordedBytes();
                const auto after = audioManager->GetCaptureResult();
                if (FAILED(after))
                {
                    state = XbAudioAdapterState::Failed;
                    lastHResult = after;
                    return after;
                }
                lastHResult = S_OK;
                return S_OK;
            }
            catch (const std::bad_alloc&)
            {
                state = XbAudioAdapterState::Failed;
                lastHResult = E_OUTOFMEMORY;
                return lastHResult;
            }
            catch (...)
            {
                state = XbAudioAdapterState::Failed;
                lastHResult = E_UNEXPECTED;
                return lastHResult;
            }
        }

        HRESULT Stop() noexcept
        {
            try
            {
                std::lock_guard lock(mutex);
                if (audioManager == nullptr || !captureRunning)
                {
                    return S_FALSE;
                }

                const auto stopResult = audioManager->StopCapture();
                captureRunning = false;
                postStopDrainAvailable = true;
                lastHResult = stopResult;
                state = FAILED(stopResult)
                    ? XbAudioAdapterState::Failed
                    : XbAudioAdapterState::Stopped;
                return stopResult;
            }
            catch (const std::bad_alloc&)
            {
                return FailStop(E_OUTOFMEMORY);
            }
            catch (...)
            {
                return FailStop(E_UNEXPECTED);
            }
        }

        HRESULT FinishStop() noexcept
        {
            try
            {
                std::lock_guard lock(mutex);
                if (captureRunning)
                {
                    lastHResult = MF_E_INVALIDREQUEST;
                    return lastHResult;
                }
                if (audioManager == nullptr && !mediaFoundationStarted)
                {
                    return S_FALSE;
                }
                postStopDrainAvailable = false;
                const auto result = ReleaseRuntimeLocked();
                lastHResult = result;
                state = FAILED(result)
                    ? XbAudioAdapterState::Failed
                    : XbAudioAdapterState::Stopped;
                return result;
            }
            catch (...)
            {
                state = XbAudioAdapterState::Failed;
                lastHResult = E_UNEXPECTED;
                return lastHResult;
            }
        }

        XbAudioAdapterSnapshot Snapshot() const noexcept
        {
            XbAudioAdapterSnapshot result{};
            try
            {
                std::lock_guard lock(mutex);
                result.state = state;
                result.mode = mode;
                result.lastHResult = lastHResult;
                result.mediaFoundationStarted = mediaFoundationStarted;
                result.captureRunning = captureRunning;
                result.postStopDrainAvailable = postStopDrainAvailable;
                result.pullCount = pullCount;
                result.pcmBytesDelivered = pcmBytesDelivered;
            }
            catch (...)
            {
                result.state = XbAudioAdapterState::Failed;
                result.lastHResult = E_UNEXPECTED;
            }
            return result;
        }

        HRESULT FailStartLocked(const HRESULT failure) noexcept
        {
            // StopCapture clears requested-running intent even when only one
            // of the donor's two capture clients started successfully.
            if (audioManager != nullptr)
            {
                try
                {
                    (void)audioManager->StopCapture();
                }
                catch (...)
                {
                }
            }
            captureRunning = false;
            postStopDrainAvailable = false;
            (void)ReleaseRuntimeLocked();
            state = XbAudioAdapterState::Failed;
            lastHResult = failure;
            return failure;
        }

        HRESULT FailStartAfterException(const HRESULT failure) noexcept
        {
            try
            {
                std::lock_guard lock(mutex);
                return FailStartLocked(failure);
            }
            catch (...)
            {
                state = XbAudioAdapterState::Failed;
                lastHResult = failure;
                return failure;
            }
        }

        HRESULT FailPull(const HRESULT failure) noexcept
        {
            try
            {
                std::lock_guard lock(mutex);
                if (!captureRunning && postStopDrainAvailable)
                {
                    postStopDrainAvailable = false;
                    (void)ReleaseRuntimeLocked();
                }
                lastHResult = failure;
                state = XbAudioAdapterState::Failed;
            }
            catch (...)
            {
            }
            return failure;
        }

        HRESULT FailStop(const HRESULT failure) noexcept
        {
            try
            {
                std::lock_guard lock(mutex);
                captureRunning = false;
                postStopDrainAvailable = audioManager != nullptr;
                state = XbAudioAdapterState::Failed;
                lastHResult = failure;
            }
            catch (...)
            {
            }
            return failure;
        }

        HRESULT ReleaseRuntimeLocked() noexcept
        {
            audioManager.reset();
            options.reset();
            captureRunning = false;
            postStopDrainAvailable = false;

            HRESULT result = S_OK;
            if (mediaFoundationStarted)
            {
                result = MFShutdown();
                mediaFoundationStarted = false;
            }
            return result;
        }

        void ShutdownNoThrow() noexcept
        {
            try
            {
                std::lock_guard lock(mutex);
                if (audioManager != nullptr && captureRunning)
                {
                    try
                    {
                        (void)audioManager->StopCapture();
                    }
                    catch (...)
                    {
                    }
                }
                (void)ReleaseRuntimeLocked();
            }
            catch (...)
            {
            }
        }

        mutable std::mutex mutex;
        std::shared_ptr<AUDIO_OPTIONS> options;
        std::unique_ptr<AudioManager> audioManager;
        XbAudioAdapterState state{ XbAudioAdapterState::Idle };
        XbAudioAdapterMode mode{ XbAudioAdapterMode::None };
        HRESULT lastHResult{ S_OK };
        bool mediaFoundationStarted{};
        bool captureRunning{};
        bool postStopDrainAvailable{};
        std::uint64_t pullCount{};
        std::uint64_t pcmBytesDelivered{};
    };

    XbAudioAdapter::XbAudioAdapter()
        : impl_(std::make_unique<Impl>())
    {
    }

    XbAudioAdapter::~XbAudioAdapter() = default;

    HRESULT XbAudioAdapter::Start(
        const XbAudioAdapterMode mode,
        const std::wstring& microphoneEndpointId,
        const std::wstring& renderEndpointId) noexcept
    {
        return impl_->Start(mode, microphoneEndpointId, renderEndpointId);
    }

    HRESULT XbAudioAdapter::PullMixedPcm(
        const std::uint64_t duration100ns,
        std::vector<std::uint8_t>& mixedPcm) noexcept
    {
        return impl_->PullMixedPcm(duration100ns, mixedPcm);
    }

    HRESULT XbAudioAdapter::ClearRecordedPcm() noexcept
    {
        return impl_->ClearRecordedPcm();
    }

    HRESULT XbAudioAdapter::Stop() noexcept
    {
        return impl_->Stop();
    }

    HRESULT XbAudioAdapter::FinishStop() noexcept
    {
        return impl_->FinishStop();
    }

    XbAudioAdapterSnapshot XbAudioAdapter::Snapshot() const noexcept
    {
        return impl_->Snapshot();
    }
}
