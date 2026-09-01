#include "Nv12TrackedTexturePool.h"

#include <mfapi.h>

#include <array>
#include <atomic>
#include <condition_variable>
#include <mutex>

namespace xbpreview
{
    namespace
    {
        enum class Nv12SlotState : std::uint8_t
        {
            Free,
            GpuProducing,
            SubmittedToMf
        };

        struct Nv12Slot
        {
            winrt::com_ptr<ID3D11Texture2D> texture;
            winrt::com_ptr<ID3D11VideoProcessorOutputView> outputView;
            Nv12SlotState state{ Nv12SlotState::Free };
            std::uint64_t token{};
        };
    }

    struct Nv12PoolSharedState
    {
        mutable std::mutex mutex;
        std::condition_variable condition;
        std::array<Nv12Slot, VideoEncoderNv12PoolSize> slots{};
        bool stopping{};
        std::uint32_t outstanding{};
        std::uint32_t highWatermark{};
        std::uint64_t tokenSeed{};
        std::uint64_t callbackCount{};
        std::uint64_t callbackAfterStop{};
        std::uint64_t doubleReturn{};
        std::uint64_t invalidStateTransition{};
        std::uint64_t starvation{};
    };

    namespace
    {
        class TrackedReturnCallback final : public IMFAsyncCallback
        {
        public:
            TrackedReturnCallback(
                std::weak_ptr<Nv12PoolSharedState> state,
                const std::size_t index,
                const std::uint64_t token) noexcept
                : state_(std::move(state)), index_(index), token_(token)
            {
            }

            HRESULT STDMETHODCALLTYPE QueryInterface(
                REFIID iid,
                void** const object) override
            {
                if (object == nullptr)
                {
                    return E_POINTER;
                }
                if (iid == __uuidof(IUnknown) || iid == __uuidof(IMFAsyncCallback))
                {
                    *object = static_cast<IMFAsyncCallback*>(this);
                    AddRef();
                    return S_OK;
                }
                *object = nullptr;
                return E_NOINTERFACE;
            }

            ULONG STDMETHODCALLTYPE AddRef() override
            {
                return ++references_;
            }

            ULONG STDMETHODCALLTYPE Release() override
            {
                const auto remaining = --references_;
                if (remaining == 0)
                {
                    delete this;
                }
                return remaining;
            }

            HRESULT STDMETHODCALLTYPE GetParameters(DWORD*, DWORD*) override
            {
                return E_NOTIMPL;
            }

            HRESULT STDMETHODCALLTYPE Invoke(IMFAsyncResult*) override
            {
                const auto state = state_.lock();
                if (!state)
                {
                    return S_OK;
                }
                {
                    std::lock_guard lock(state->mutex);
                    if (index_ >= state->slots.size())
                    {
                        ++state->invalidStateTransition;
                        return S_OK;
                    }
                    auto& slot = state->slots[index_];
                    if (slot.state != Nv12SlotState::SubmittedToMf ||
                        slot.token != token_)
                    {
                        ++state->doubleReturn;
                        return S_OK;
                    }
                    slot.state = Nv12SlotState::Free;
                    slot.token = 0;
                    if (state->outstanding == 0)
                    {
                        ++state->invalidStateTransition;
                    }
                    else
                    {
                        --state->outstanding;
                    }
                    ++state->callbackCount;
                    if (state->stopping)
                    {
                        ++state->callbackAfterStop;
                    }
                }
                state->condition.notify_all();
                return S_OK;
            }

        private:
            std::atomic<ULONG> references_{ 1 };
            std::weak_ptr<Nv12PoolSharedState> state_;
            std::size_t index_{};
            std::uint64_t token_{};
        };
    }

    Nv12TrackedTexturePool::~Nv12TrackedTexturePool()
    {
        Shutdown();
    }

    void Nv12TrackedTexturePool::Initialize(
        ID3D11Device* const device,
        ID3D11VideoDevice* const videoDevice,
        ID3D11VideoProcessorEnumerator* const enumerator,
        const std::uint32_t width,
        const std::uint32_t height)
    {
        Shutdown();
        if (device == nullptr || videoDevice == nullptr || enumerator == nullptr ||
            width == 0 || height == 0 || (width & 1u) != 0 || (height & 1u) != 0)
        {
            throw winrt::hresult_invalid_argument();
        }
        auto state = std::make_shared<Nv12PoolSharedState>();
        D3D11_TEXTURE2D_DESC description{};
        description.Width = width;
        description.Height = height;
        description.MipLevels = 1;
        description.ArraySize = 1;
        description.Format = DXGI_FORMAT_NV12;
        description.SampleDesc.Count = 1;
        description.Usage = D3D11_USAGE_DEFAULT;
        description.BindFlags = D3D11_BIND_RENDER_TARGET;

        D3D11_VIDEO_PROCESSOR_OUTPUT_VIEW_DESC viewDescription{};
        viewDescription.ViewDimension = D3D11_VPOV_DIMENSION_TEXTURE2D;
        viewDescription.Texture2D.MipSlice = 0;
        for (auto& slot : state->slots)
        {
            winrt::check_hresult(device->CreateTexture2D(
                &description, nullptr, slot.texture.put()));
            winrt::check_hresult(videoDevice->CreateVideoProcessorOutputView(
                slot.texture.get(), enumerator, &viewDescription,
                slot.outputView.put()));
        }
        state_ = std::move(state);
    }

    std::optional<std::size_t> Nv12TrackedTexturePool::TryAcquire() noexcept
    {
        const auto state = state_;
        if (!state)
        {
            return std::nullopt;
        }
        std::lock_guard lock(state->mutex);
        if (state->stopping)
        {
            return std::nullopt;
        }
        for (std::size_t index = 0; index < state->slots.size(); ++index)
        {
            auto& slot = state->slots[index];
            if (slot.state == Nv12SlotState::Free)
            {
                slot.state = Nv12SlotState::GpuProducing;
                slot.token = ++state->tokenSeed;
                return index;
            }
        }
        ++state->starvation;
        return std::nullopt;
    }

    ID3D11Texture2D* Nv12TrackedTexturePool::Texture(
        const std::size_t index) const noexcept
    {
        const auto state = state_;
        return state && index < state->slots.size()
            ? state->slots[index].texture.get()
            : nullptr;
    }

    ID3D11VideoProcessorOutputView* Nv12TrackedTexturePool::OutputView(
        const std::size_t index) const noexcept
    {
        const auto state = state_;
        return state && index < state->slots.size()
            ? state->slots[index].outputView.get()
            : nullptr;
    }

    HRESULT Nv12TrackedTexturePool::CreateTrackedSample(
        const std::size_t index,
        const std::int64_t sampleTime100ns,
        const std::int64_t sampleDuration100ns,
        IMFSample** const sample) noexcept
    {
        if (sample == nullptr)
        {
            return E_POINTER;
        }
        *sample = nullptr;
        const auto state = state_;
        if (!state || index >= state->slots.size())
        {
            return E_INVALIDARG;
        }

        winrt::com_ptr<ID3D11Texture2D> texture;
        std::uint64_t token{};
        {
            std::lock_guard lock(state->mutex);
            auto& slot = state->slots[index];
            if (slot.state != Nv12SlotState::GpuProducing)
            {
                ++state->invalidStateTransition;
                return MF_E_INVALIDREQUEST;
            }
            texture = slot.texture;
            token = slot.token;
        }

        try
        {
            winrt::com_ptr<IMFMediaBuffer> buffer;
            winrt::check_hresult(MFCreateDXGISurfaceBuffer(
                __uuidof(ID3D11Texture2D), texture.get(), 0, FALSE,
                buffer.put()));
            DWORD length{};
            winrt::check_hresult(buffer->GetMaxLength(&length));
            winrt::check_hresult(buffer->SetCurrentLength(length));

            winrt::com_ptr<IMFTrackedSample> tracked;
            winrt::check_hresult(MFCreateTrackedSample(tracked.put()));
            auto mediaSample = tracked.as<IMFSample>();
            winrt::check_hresult(mediaSample->AddBuffer(buffer.get()));
            winrt::check_hresult(mediaSample->SetSampleTime(sampleTime100ns));
            winrt::check_hresult(mediaSample->SetSampleDuration(
                sampleDuration100ns));
            winrt::com_ptr<IMFAsyncCallback> callback;
            callback.attach(new TrackedReturnCallback(state, index, token));
            winrt::check_hresult(tracked->SetAllocator(callback.get(), nullptr));
            {
                std::lock_guard lock(state->mutex);
                auto& slot = state->slots[index];
                if (slot.state != Nv12SlotState::GpuProducing ||
                    slot.token != token)
                {
                    ++state->invalidStateTransition;
                    return MF_E_INVALIDREQUEST;
                }
                slot.state = Nv12SlotState::SubmittedToMf;
                ++state->outstanding;
                state->highWatermark = (std::max)(
                    state->highWatermark, state->outstanding);
            }
            *sample = mediaSample.detach();
            return S_OK;
        }
        catch (const winrt::hresult_error& error)
        {
            CancelProducing(index);
            return error.code();
        }
        catch (...)
        {
            CancelProducing(index);
            return E_FAIL;
        }
    }

    void Nv12TrackedTexturePool::CancelProducing(
        const std::size_t index) noexcept
    {
        const auto state = state_;
        if (!state || index >= state->slots.size())
        {
            return;
        }
        {
            std::lock_guard lock(state->mutex);
            auto& slot = state->slots[index];
            if (slot.state == Nv12SlotState::GpuProducing)
            {
                slot.state = Nv12SlotState::Free;
                slot.token = 0;
            }
        }
        state->condition.notify_all();
    }

    void Nv12TrackedTexturePool::MarkStopping() noexcept
    {
        const auto state = state_;
        if (!state)
        {
            return;
        }
        {
            std::lock_guard lock(state->mutex);
            state->stopping = true;
        }
        state->condition.notify_all();
    }

    bool Nv12TrackedTexturePool::WaitForAllReturned(
        const std::chrono::milliseconds timeout) noexcept
    {
        const auto state = state_;
        if (!state)
        {
            return true;
        }
        std::unique_lock lock(state->mutex);
        return state->condition.wait_for(
            lock, timeout, [&] { return state->outstanding == 0; });
    }

    Nv12PoolDiagnostics Nv12TrackedTexturePool::Diagnostics() const noexcept
    {
        Nv12PoolDiagnostics result{};
        const auto state = state_;
        if (!state)
        {
            return result;
        }
        std::lock_guard lock(state->mutex);
        result.highWatermark = state->highWatermark;
        result.outstanding = state->outstanding;
        result.callbackCount = state->callbackCount;
        result.callbackAfterStop = state->callbackAfterStop;
        result.doubleReturn = state->doubleReturn;
        result.invalidStateTransition = state->invalidStateTransition;
        result.starvation = state->starvation;
        return result;
    }

    void Nv12TrackedTexturePool::Shutdown() noexcept
    {
        MarkStopping();
        state_.reset();
    }
}
