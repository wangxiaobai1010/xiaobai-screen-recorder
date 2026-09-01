#include "AudioEndpointLevelMonitor.h"

#include <endpointvolume.h>
#include <mmdeviceapi.h>
#include <wrl/client.h>

#include <algorithm>
#include <atomic>
#include <chrono>
#include <cmath>
#include <condition_variable>
#include <mutex>
#include <new>
#include <thread>
#include <utility>

namespace xbpreview
{
    namespace
    {
        using Microsoft::WRL::ComPtr;

        constexpr auto SampleInterval = std::chrono::milliseconds(50);
        constexpr auto RebindDelay = std::chrono::seconds(1);

        struct EndpointMeter final
        {
            std::wstring endpointId;
            ComPtr<IAudioMeterInformation> meter;
            std::chrono::steady_clock::time_point nextBind{};

            void Assign(
                const std::wstring& value,
                const std::chrono::steady_clock::time_point now) noexcept
            {
                if (endpointId == value) return;
                endpointId = value;
                meter.Reset();
                nextBind = now;
            }

            void Bind(
                IMMDeviceEnumerator* const enumerator,
                const std::chrono::steady_clock::time_point now) noexcept
            {
                if (meter || endpointId.empty() || enumerator == nullptr ||
                    now < nextBind)
                {
                    return;
                }

                nextBind = now + RebindDelay;
                ComPtr<IMMDevice> endpoint;
                if (FAILED(enumerator->GetDevice(
                        endpointId.c_str(), endpoint.GetAddressOf())))
                {
                    return;
                }
                DWORD state{};
                if (FAILED(endpoint->GetState(&state)) ||
                    (state & DEVICE_STATE_ACTIVE) == 0)
                {
                    return;
                }

                ComPtr<IAudioMeterInformation> next;
                if (FAILED(endpoint->Activate(
                        __uuidof(IAudioMeterInformation),
                        CLSCTX_ALL,
                        nullptr,
                        reinterpret_cast<void**>(next.GetAddressOf()))))
                {
                    return;
                }
                meter = std::move(next);
            }

            float Sample(
                const std::chrono::steady_clock::time_point now,
                bool& available) noexcept
            {
                available = false;
                if (!meter) return 0.0f;

                float peak{};
                if (FAILED(meter->GetPeakValue(&peak)) ||
                    !std::isfinite(peak))
                {
                    meter.Reset();
                    nextBind = now + RebindDelay;
                    return 0.0f;
                }
                available = true;
                return (std::clamp)(peak, 0.0f, 1.0f);
            }
        };
    }

    std::uint32_t NormalizedEndpointPeakToAbsolutePcm16(
        const float peak) noexcept
    {
        if (!std::isfinite(peak)) return 0;
        const auto normalized = (std::clamp)(peak, 0.0f, 1.0f);
        const auto equivalent = std::lround(
            static_cast<double>(normalized) * 32768.0);
        return static_cast<std::uint32_t>((std::clamp)(
            equivalent, 0L, 32768L));
    }

    struct AudioEndpointLevelMonitor::Impl final
    {
        explicit Impl(AssignmentProvider value)
            : provider(std::move(value))
        {
        }

        AudioEndpointLevelAssignment ReadAssignment() const noexcept
        {
            try
            {
                return provider ? provider() : AudioEndpointLevelAssignment{};
            }
            catch (...)
            {
                return {};
            }
        }

        void Publish(
            const AudioEndpointLevelAssignment& assignment,
            const float microphonePeak,
            const bool microphoneAvailable,
            const float systemPeak,
            const bool systemAvailable) noexcept
        {
            std::lock_guard lock(snapshotMutex);
            snapshot.microphonePeakAbsolutePcm16 =
                NormalizedEndpointPeakToAbsolutePcm16(microphonePeak);
            snapshot.systemPeakAbsolutePcm16 =
                NormalizedEndpointPeakToAbsolutePcm16(systemPeak);
            snapshot.microphoneAvailable = microphoneAvailable;
            snapshot.systemAvailable = systemAvailable;
            snapshot.microphoneEnabled = assignment.microphoneEnabled;
            snapshot.systemEnabled = assignment.systemEnabled;
        }

        void ThreadMain() noexcept
        {
            const auto initialized = CoInitializeEx(
                nullptr, COINIT_MULTITHREADED);
            const auto uninitialize = SUCCEEDED(initialized);

            ComPtr<IMMDeviceEnumerator> enumerator;
            auto nextEnumeratorBind = std::chrono::steady_clock::now();
            EndpointMeter microphone;
            EndpointMeter system;

            while (!stopRequested.load(std::memory_order_acquire))
            {
                const auto iterationStart = std::chrono::steady_clock::now();
                const auto assignment = ReadAssignment();
                microphone.Assign(
                    assignment.microphoneEndpointId, iterationStart);
                system.Assign(assignment.systemEndpointId, iterationStart);

                if (SUCCEEDED(initialized) && !enumerator &&
                    iterationStart >= nextEnumeratorBind)
                {
                    nextEnumeratorBind = iterationStart + RebindDelay;
                    (void)CoCreateInstance(
                        __uuidof(MMDeviceEnumerator),
                        nullptr,
                        CLSCTX_INPROC_SERVER,
                        __uuidof(IMMDeviceEnumerator),
                        reinterpret_cast<void**>(
                            enumerator.GetAddressOf()));
                }

                microphone.Bind(enumerator.Get(), iterationStart);
                system.Bind(enumerator.Get(), iterationStart);

                bool microphoneAvailable{};
                bool systemAvailable{};
                const auto microphonePeak = microphone.Sample(
                    iterationStart, microphoneAvailable);
                const auto systemPeak = system.Sample(
                    iterationStart, systemAvailable);
                Publish(
                    assignment,
                    microphonePeak,
                    microphoneAvailable,
                    systemPeak,
                    systemAvailable);

                std::unique_lock lock(waitMutex);
                waitCondition.wait_until(
                    lock,
                    iterationStart + SampleInterval,
                    [this]
                    {
                        return stopRequested.load(
                            std::memory_order_acquire);
                    });
            }

            microphone.meter.Reset();
            system.meter.Reset();
            enumerator.Reset();
            if (uninitialize) CoUninitialize();
        }

        AssignmentProvider provider;
        mutable std::mutex snapshotMutex;
        AudioEndpointLevelSnapshot snapshot{};
        std::atomic<bool> stopRequested{};
        std::mutex waitMutex;
        std::condition_variable waitCondition;
        std::thread thread;
    };

    AudioEndpointLevelMonitor::AudioEndpointLevelMonitor(
        AssignmentProvider provider)
        : impl_(std::make_unique<Impl>(std::move(provider)))
    {
    }

    AudioEndpointLevelMonitor::~AudioEndpointLevelMonitor()
    {
        Stop();
    }

    HRESULT AudioEndpointLevelMonitor::Start() noexcept
    {
        if (impl_->thread.joinable()) return S_OK;
        impl_->stopRequested.store(false, std::memory_order_release);
        try
        {
            impl_->thread = std::thread(&Impl::ThreadMain, impl_.get());
            return S_OK;
        }
        catch (const std::bad_alloc&)
        {
            return E_OUTOFMEMORY;
        }
        catch (...)
        {
            return E_FAIL;
        }
    }

    void AudioEndpointLevelMonitor::Stop() noexcept
    {
        if (!impl_) return;
        impl_->stopRequested.store(true, std::memory_order_release);
        impl_->waitCondition.notify_all();
        if (impl_->thread.joinable() &&
            impl_->thread.get_id() != std::this_thread::get_id())
        {
            impl_->thread.join();
        }
        std::lock_guard lock(impl_->snapshotMutex);
        impl_->snapshot = {};
    }

    AudioEndpointLevelSnapshot AudioEndpointLevelMonitor::Snapshot()
        const noexcept
    {
        std::lock_guard lock(impl_->snapshotMutex);
        return impl_->snapshot;
    }
}
