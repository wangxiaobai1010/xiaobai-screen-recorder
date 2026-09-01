#include "RenderFrameTap.h"

#include <windows.h>

#include <algorithm>
#include <array>
#include <atomic>
#include <condition_variable>
#include <cstdlib>
#include <cwctype>
#include <deque>
#include <filesystem>
#include <fstream>
#include <iomanip>
#include <mutex>
#include <sstream>
#include <thread>
#include <utility>
#include <vector>

namespace xbpreview
{
    namespace
    {
        enum class TapSlotState : std::uint8_t
        {
            Free,
            Queued,
            ConsumerOwned
        };

        struct TapSlot
        {
            winrt::com_ptr<ID3D11Texture2D> texture;
            TapSlotState state{ TapSlotState::Free };
            std::uint64_t leaseToken{};
        };

        struct QueuedTapFrame
        {
            std::shared_ptr<RenderFrameTapGeneration> generation;
            std::uint32_t slot{};
            std::uint64_t leaseToken{};
            TappedFrameMetadata metadata{};
        };

        struct TapGenerationEvent
        {
            std::uint64_t oldGeneration{};
            std::uint64_t newGeneration{};
            std::uint32_t oldOutstanding{};
            std::uint32_t oldQueued{};
            std::uint32_t width{};
            std::uint32_t height{};
        };

        template<typename T>
        void AtomicMaximum(std::atomic<T>& target, const T value) noexcept
        {
            auto current = target.load(std::memory_order_relaxed);
            while (current < value &&
                !target.compare_exchange_weak(
                    current,
                    value,
                    std::memory_order_relaxed))
            {
            }
        }

        std::int64_t QueryPerformanceCounterValue() noexcept
        {
            LARGE_INTEGER value{};
            return QueryPerformanceCounter(&value) ? value.QuadPart : 0;
        }

        std::wstring ReadEnvironment(const wchar_t* const name) noexcept
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

        bool IsEnabledValue(std::wstring value) noexcept
        {
            std::transform(
                value.begin(),
                value.end(),
                value.begin(),
                [](const wchar_t character)
                {
                    return static_cast<wchar_t>(towlower(character));
                });
            return value == L"1" ||
                value == L"true" ||
                value == L"normal" ||
                value == L"slow";
        }

        std::chrono::milliseconds ReadConsumerDelay(
            const std::wstring& mode) noexcept
        {
            auto delay = mode == L"slow" ? 120L : 0L;
            const auto configured = ReadEnvironment(
                L"XB_PREVIEW_DIAGNOSTIC_TAP_DELAY_MS");
            if (!configured.empty())
            {
                wchar_t* end{};
                const auto parsed = wcstol(configured.c_str(), &end, 10);
                if (end != configured.c_str() && *end == L'\0')
                {
                    delay = (std::clamp)(parsed, 0L, 5000L);
                }
            }
            return std::chrono::milliseconds(delay);
        }

        const char* ConsumerModeName(
            const RenderFrameTapConsumerMode mode) noexcept
        {
            return mode == RenderFrameTapConsumerMode::Slow
                ? "slow"
                : "normal";
        }
    }

    struct RenderFrameTapGeneration
    {
        std::uint64_t id{};
        OutputCanvasDescription description{};
        std::array<TapSlot, RenderFrameTapPoolSize> slots{};
        bool hasLastTimestamp{};
        std::int64_t lastTimestamp100ns{};
    };

    struct RenderFrameTapSharedState
    {
        mutable std::mutex mutex;
        std::condition_variable condition;
        std::atomic<bool> enabled{};
        std::atomic<bool> stopping{};
        bool configuredEnabled{};
        winrt::com_ptr<ID3D11Device> device;
        winrt::com_ptr<ID3D11DeviceContext> context;
        std::shared_ptr<RenderFrameTapGeneration> current;
        std::vector<std::shared_ptr<RenderFrameTapGeneration>> retired;
        std::array<QueuedTapFrame, RenderFrameTapQueueCapacity> queue{};
        std::uint32_t queueHead{};
        std::uint32_t queueTail{};
        std::uint32_t queueCount{};
        std::uint64_t generationSeed{};
        std::uint64_t nextFrameSequence{};
        std::uint64_t nextLeaseToken{};
        RenderFrameTapConsumerMode consumerMode{
            RenderFrameTapConsumerMode::Normal };
        std::chrono::milliseconds consumerDelay{};
        RenderFrameTapConsumerKind consumerKind{
            RenderFrameTapConsumerKind::None };
        std::atomic<bool> consumerStopRequested{};
        bool drainQueuedFrames{};
        std::wstring diagnosticLogPath;
        std::array<TapGenerationEvent, 32> generationEvents{};
        std::uint32_t generationEventCount{};
        std::uint64_t generationEventsDropped{};

        std::atomic<std::uint64_t> framesObservedAtTapPoint{};
        std::atomic<std::uint64_t> framesCopied{};
        std::atomic<std::uint64_t> framesEnqueued{};
        std::atomic<std::uint64_t> framesConsumed{};
        std::atomic<std::uint64_t> framesReturned{};
        std::atomic<std::uint64_t> framesDroppedNoFreeSlot{};
        std::atomic<std::uint64_t> framesDroppedQueueFull{};
        std::atomic<std::uint64_t> framesDroppedGenerationMismatch{};
        std::atomic<std::uint64_t> framesDroppedDisabledOrStopping{};
        std::atomic<std::uint64_t> framesDroppedLockBusy{};
        std::atomic<std::uint64_t> timestampValidCount{};
        std::atomic<std::uint64_t> timestampMissingCount{};
        std::atomic<std::uint64_t> timestampRegressionCount{};
        std::atomic<std::uint32_t> queueDepthCurrent{};
        std::atomic<std::uint32_t> queueDepthHighWatermark{};
        std::atomic<std::uint32_t> freeSlotsCurrent{};
        std::atomic<std::uint32_t> consumerOwnedCurrent{};
        std::atomic<std::uint32_t> outstandingCurrent{};
        std::atomic<std::uint32_t> outstandingHighWatermark{};
        std::atomic<std::uint64_t> generationChangeCount{};
        std::atomic<std::uint64_t> staleFramesFlushed{};
        std::atomic<std::uint64_t> lateReturnsFromOldGeneration{};
        std::atomic<std::uint64_t> doubleReturnDetected{};
        std::atomic<std::uint64_t> invalidStateTransitionDetected{};
        std::atomic<std::uint64_t> texturesCreated{};
        std::atomic<std::uint32_t> outstandingAtShutdown{};
        std::atomic<std::uint64_t> shutdownDurationMicroseconds{};

        void UpdateCountsLocked() noexcept
        {
            std::uint32_t freeSlots{};
            std::uint32_t consumerOwned{};
            if (current)
            {
                for (const auto& slot : current->slots)
                {
                    freeSlots += slot.state == TapSlotState::Free ? 1u : 0u;
                    consumerOwned +=
                        slot.state == TapSlotState::ConsumerOwned ? 1u : 0u;
                }
            }
            for (const auto& generation : retired)
            {
                for (const auto& slot : generation->slots)
                {
                    consumerOwned +=
                        slot.state == TapSlotState::ConsumerOwned ? 1u : 0u;
                }
            }
            const auto outstanding = queueCount + consumerOwned;
            freeSlotsCurrent.store(freeSlots, std::memory_order_relaxed);
            consumerOwnedCurrent.store(consumerOwned, std::memory_order_relaxed);
            queueDepthCurrent.store(queueCount, std::memory_order_relaxed);
            outstandingCurrent.store(outstanding, std::memory_order_relaxed);
            AtomicMaximum(queueDepthHighWatermark, queueCount);
            AtomicMaximum(outstandingHighWatermark, outstanding);
        }

        void PruneRetiredLocked()
        {
            retired.erase(
                std::remove_if(
                    retired.begin(),
                    retired.end(),
                    [](const auto& generation)
                    {
                        return std::none_of(
                            generation->slots.begin(),
                            generation->slots.end(),
                            [](const TapSlot& slot)
                            {
                                return slot.state != TapSlotState::Free;
                            });
                    }),
                retired.end());
        }

        void Return(
            const std::shared_ptr<RenderFrameTapGeneration>& generation,
            const std::uint32_t slotIndex,
            const std::uint64_t leaseToken) noexcept
        {
            std::lock_guard lock(mutex);
            if (!generation || slotIndex >= generation->slots.size())
            {
                ++invalidStateTransitionDetected;
                return;
            }
            auto& slot = generation->slots[slotIndex];
            if (slot.state != TapSlotState::ConsumerOwned ||
                slot.leaseToken != leaseToken)
            {
                ++invalidStateTransitionDetected;
                return;
            }
            if (!current || current->id != generation->id)
            {
                ++lateReturnsFromOldGeneration;
            }
            slot.state = TapSlotState::Free;
            slot.leaseToken = 0;
            ++framesReturned;
            PruneRetiredLocked();
            UpdateCountsLocked();
            condition.notify_all();
        }
    };

    namespace
    {
        std::uint32_t CountState(
            const std::shared_ptr<RenderFrameTapGeneration>& generation,
            const TapSlotState state) noexcept
        {
            if (!generation)
            {
                return 0;
            }
            return static_cast<std::uint32_t>(std::count_if(
                generation->slots.begin(),
                generation->slots.end(),
                [state](const TapSlot& slot)
                {
                    return slot.state == state;
                }));
        }

        void FlushQueueLocked(RenderFrameTapSharedState& state) noexcept
        {
            while (state.queueCount > 0)
            {
                auto& queued = state.queue[state.queueHead];
                if (queued.generation &&
                    queued.slot < queued.generation->slots.size())
                {
                    auto& slot = queued.generation->slots[queued.slot];
                    if (slot.state == TapSlotState::Queued &&
                        slot.leaseToken == queued.leaseToken)
                    {
                        slot.state = TapSlotState::Free;
                        slot.leaseToken = 0;
                        ++state.staleFramesFlushed;
                    }
                    else
                    {
                        ++state.invalidStateTransitionDetected;
                    }
                }
                queued = {};
                state.queueHead =
                    (state.queueHead + 1) % RenderFrameTapQueueCapacity;
                --state.queueCount;
            }
            state.queueHead = 0;
            state.queueTail = 0;
            state.UpdateCountsLocked();
        }

        std::shared_ptr<RenderFrameTapGeneration> CreateGeneration(
            RenderFrameTapSharedState& state,
            const OutputCanvasDescription& description)
        {
            auto generation = std::make_shared<RenderFrameTapGeneration>();
            generation->id = ++state.generationSeed;
            generation->description = description;

            D3D11_TEXTURE2D_DESC textureDescription{};
            textureDescription.Width = description.width;
            textureDescription.Height = description.height;
            textureDescription.MipLevels = 1;
            textureDescription.ArraySize = 1;
            textureDescription.Format = description.format;
            textureDescription.SampleDesc.Count = 1;
            textureDescription.Usage = D3D11_USAGE_DEFAULT;
            textureDescription.BindFlags =
                D3D11_BIND_RENDER_TARGET | D3D11_BIND_SHADER_RESOURCE;
            for (auto& slot : generation->slots)
            {
                winrt::check_hresult(state.device->CreateTexture2D(
                    &textureDescription,
                    nullptr,
                    slot.texture.put()));
                ++state.texturesCreated;
            }
            return generation;
        }

        bool SwitchGenerationLocked(
            RenderFrameTapSharedState& state,
            const OutputCanvasDescription& description)
        {
            state.PruneRetiredLocked();
            const auto oldGeneration = state.current;
            const auto oldOutstanding = CountState(
                oldGeneration,
                TapSlotState::ConsumerOwned);
            const auto oldQueued = state.queueCount;
            if (oldOutstanding > 0 && state.retired.size() >= 2)
            {
                return false;
            }

            auto next = CreateGeneration(state, description);
            FlushQueueLocked(state);
            if (oldGeneration && oldOutstanding > 0)
            {
                state.retired.push_back(oldGeneration);
            }
            const auto oldId = oldGeneration ? oldGeneration->id : 0;
            const auto newId = next->id;
            state.current = std::move(next);
            ++state.generationChangeCount;

            TapGenerationEvent event{
                oldId,
                newId,
                oldOutstanding,
                oldQueued,
                description.width,
                description.height
            };
            if (state.generationEventCount < state.generationEvents.size())
            {
                state.generationEvents[state.generationEventCount++] = event;
            }
            else
            {
                ++state.generationEventsDropped;
            }
            state.UpdateCountsLocked();
            return true;
        }

        std::optional<GpuFrameLease> TryAcquireLocked(
            const std::shared_ptr<RenderFrameTapSharedState>& state)
        {
            if (state->queueCount == 0)
            {
                return std::nullopt;
            }
            auto queued = std::move(state->queue[state->queueHead]);
            state->queue[state->queueHead] = {};
            state->queueHead =
                (state->queueHead + 1) % RenderFrameTapQueueCapacity;
            --state->queueCount;

            if (!queued.generation ||
                queued.slot >= queued.generation->slots.size())
            {
                ++state->invalidStateTransitionDetected;
                state->UpdateCountsLocked();
                return std::nullopt;
            }
            auto& slot = queued.generation->slots[queued.slot];
            if (slot.state != TapSlotState::Queued ||
                slot.leaseToken != queued.leaseToken)
            {
                ++state->invalidStateTransitionDetected;
                state->UpdateCountsLocked();
                return std::nullopt;
            }
            slot.state = TapSlotState::ConsumerOwned;
            ++state->framesConsumed;
            state->UpdateCountsLocked();
            return GpuFrameLease(
                state,
                queued.generation,
                queued.slot,
                queued.leaseToken,
                queued.metadata);
        }

        void ConsumerMain(const std::shared_ptr<RenderFrameTapSharedState>& state)
        {
            for (;;)
            {
                std::optional<GpuFrameLease> lease;
                {
                    std::unique_lock lock(state->mutex);
                    state->condition.wait(
                        lock,
                        [&]
                        {
                            return state->stopping.load() ||
                                state->consumerStopRequested ||
                                state->queueCount > 0;
                        });
                    if (state->queueCount == 0 &&
                        (state->stopping.load() ||
                            state->consumerStopRequested))
                    {
                        break;
                    }
                    lease = TryAcquireLocked(state);
                }
                if (!lease)
                {
                    continue;
                }
                if (state->consumerDelay.count() > 0)
                {
                    std::unique_lock lock(state->mutex);
                    state->condition.wait_for(
                        lock,
                        state->consumerDelay,
                        [&]
                        {
                            return state->stopping.load();
                        });
                }
                lease->Return();
            }
        }

        RenderFrameTapDiagnostics Snapshot(
            const std::shared_ptr<RenderFrameTapSharedState>& state) noexcept
        {
        RenderFrameTapDiagnostics result{};
            if (!state)
            {
                return result;
            }
            std::lock_guard lock(state->mutex);
            result.tapEnabled = state->configuredEnabled;
            result.generation = state->current
                ? state->current->id
                : state->generationSeed;
            result.framesObservedAtTapPoint = state->framesObservedAtTapPoint.load();
            result.framesCopied = state->framesCopied.load();
            result.framesEnqueued = state->framesEnqueued.load();
            result.framesConsumed = state->framesConsumed.load();
            result.framesReturned = state->framesReturned.load();
            result.framesDroppedNoFreeSlot = state->framesDroppedNoFreeSlot.load();
            result.framesDroppedQueueFull = state->framesDroppedQueueFull.load();
            result.framesDroppedGenerationMismatch =
                state->framesDroppedGenerationMismatch.load();
            result.framesDroppedDisabledOrStopping =
                state->framesDroppedDisabledOrStopping.load();
            result.framesDroppedLockBusy = state->framesDroppedLockBusy.load();
            result.timestampValidCount = state->timestampValidCount.load();
            result.timestampMissingCount = state->timestampMissingCount.load();
            result.timestampRegressionCount = state->timestampRegressionCount.load();
            result.queueDepthCurrent = state->queueDepthCurrent.load();
            result.queueDepthHighWatermark = state->queueDepthHighWatermark.load();
            result.freeSlotsCurrent = state->freeSlotsCurrent.load();
            result.consumerOwnedCurrent = state->consumerOwnedCurrent.load();
            result.outstandingCurrent = state->outstandingCurrent.load();
            result.outstandingHighWatermark = state->outstandingHighWatermark.load();
            result.generationChangeCount = state->generationChangeCount.load();
            result.staleFramesFlushed = state->staleFramesFlushed.load();
            result.lateReturnsFromOldGeneration =
                state->lateReturnsFromOldGeneration.load();
            result.doubleReturnDetected = state->doubleReturnDetected.load();
            result.invalidStateTransitionDetected =
                state->invalidStateTransitionDetected.load();
            result.texturesCreated = state->texturesCreated.load();
            result.outstandingAtShutdown = state->outstandingAtShutdown.load();
            result.shutdownDurationMilliseconds =
                static_cast<double>(state->shutdownDurationMicroseconds.load()) /
                1000.0;
            return result;
        }

        void WriteDiagnosticSummary(
            const std::shared_ptr<RenderFrameTapSharedState>& state) noexcept
        {
            if (!state || state->diagnosticLogPath.empty())
            {
                return;
            }
            try
            {
                std::filesystem::create_directories(
                    std::filesystem::path(state->diagnosticLogPath).parent_path());
                std::ofstream stream(
                    std::filesystem::path(state->diagnosticLogPath),
                    std::ios::out | std::ios::trunc);
                if (!stream)
                {
                    return;
                }
                const auto diagnostics = Snapshot(state);
                stream << std::fixed << std::setprecision(3)
                    << "{\"event\":\"tap-summary\""
                    << ",\"TapEnabled\":" << (diagnostics.tapEnabled ? 1 : 0)
                    << ",\"ConsumerMode\":\""
                    << ConsumerModeName(state->consumerMode) << '"'
                    << ",\"ConsumerDelayMs\":"
                    << state->consumerDelay.count()
                    << ",\"PoolSize\":" << diagnostics.poolSize
                    << ",\"QueueCapacity\":" << diagnostics.queueCapacity
                    << ",\"Generation\":" << diagnostics.generation
                    << ",\"FramesObservedAtTapPoint\":" << diagnostics.framesObservedAtTapPoint
                    << ",\"FramesCopied\":" << diagnostics.framesCopied
                    << ",\"FramesEnqueued\":" << diagnostics.framesEnqueued
                    << ",\"FramesConsumed\":" << diagnostics.framesConsumed
                    << ",\"FramesReturned\":" << diagnostics.framesReturned
                    << ",\"FramesDroppedNoFreeSlot\":" << diagnostics.framesDroppedNoFreeSlot
                    << ",\"FramesDroppedQueueFull\":" << diagnostics.framesDroppedQueueFull
                    << ",\"FramesDroppedGenerationMismatch\":" << diagnostics.framesDroppedGenerationMismatch
                    << ",\"FramesDroppedDisabledOrStopping\":" << diagnostics.framesDroppedDisabledOrStopping
                    << ",\"FramesDroppedLockBusy\":" << diagnostics.framesDroppedLockBusy
                    << ",\"TimestampValidCount\":" << diagnostics.timestampValidCount
                    << ",\"TimestampMissingCount\":" << diagnostics.timestampMissingCount
                    << ",\"TimestampRegressionCount\":" << diagnostics.timestampRegressionCount
                    << ",\"QueueDepthCurrent\":" << diagnostics.queueDepthCurrent
                    << ",\"QueueDepthHighWatermark\":" << diagnostics.queueDepthHighWatermark
                    << ",\"FreeSlotsCurrent\":" << diagnostics.freeSlotsCurrent
                    << ",\"ConsumerOwnedCurrent\":" << diagnostics.consumerOwnedCurrent
                    << ",\"OutstandingCurrent\":" << diagnostics.outstandingCurrent
                    << ",\"OutstandingHighWatermark\":" << diagnostics.outstandingHighWatermark
                    << ",\"GenerationChangeCount\":" << diagnostics.generationChangeCount
                    << ",\"StaleFramesFlushed\":" << diagnostics.staleFramesFlushed
                    << ",\"LateReturnsFromOldGeneration\":" << diagnostics.lateReturnsFromOldGeneration
                    << ",\"DoubleReturnDetected\":" << diagnostics.doubleReturnDetected
                    << ",\"InvalidStateTransitionDetected\":" << diagnostics.invalidStateTransitionDetected
                    << ",\"TexturesCreated\":" << diagnostics.texturesCreated
                    << ",\"OutstandingAtShutdown\":" << diagnostics.outstandingAtShutdown
                    << ",\"ShutdownDurationMs\":" << diagnostics.shutdownDurationMilliseconds
                    << ",\"GenerationEventsDropped\":" << state->generationEventsDropped
                    << ",\"GenerationEvents\":[";
                for (std::uint32_t index = 0;
                    index < state->generationEventCount;
                    ++index)
                {
                    if (index > 0)
                    {
                        stream << ',';
                    }
                    const auto& event = state->generationEvents[index];
                    stream << "{\"OldGeneration\":" << event.oldGeneration
                        << ",\"NewGeneration\":" << event.newGeneration
                        << ",\"OldOutstanding\":" << event.oldOutstanding
                        << ",\"OldQueued\":" << event.oldQueued
                        << ",\"Width\":" << event.width
                        << ",\"Height\":" << event.height << '}';
                }
                stream << "]}\n";
            }
            catch (...)
            {
            }
        }
    }

    struct RenderFrameTap::Impl
    {
        std::shared_ptr<RenderFrameTapSharedState> state;
        std::thread consumer;
        std::uint64_t generationSeed{};
    };

    GpuFrameLease::GpuFrameLease(
        std::shared_ptr<RenderFrameTapSharedState> state,
        std::shared_ptr<RenderFrameTapGeneration> generation,
        const std::uint32_t slot,
        const std::uint64_t leaseToken,
        const TappedFrameMetadata& metadata) noexcept
        : state_(std::move(state)),
        generation_(std::move(generation)),
        slot_(slot),
        leaseToken_(leaseToken),
        metadata_(metadata),
        returned_(false)
    {
    }

    GpuFrameLease::~GpuFrameLease()
    {
        if (!returned_)
        {
            Return();
        }
    }

    GpuFrameLease::GpuFrameLease(GpuFrameLease&& other) noexcept
        : state_(std::move(other.state_)),
        generation_(std::move(other.generation_)),
        slot_(other.slot_),
        leaseToken_(other.leaseToken_),
        metadata_(other.metadata_),
        returned_(other.returned_)
    {
        other.returned_ = true;
    }

    GpuFrameLease& GpuFrameLease::operator=(GpuFrameLease&& other) noexcept
    {
        if (this != &other)
        {
            if (!returned_)
            {
                Return();
            }
            state_ = std::move(other.state_);
            generation_ = std::move(other.generation_);
            slot_ = other.slot_;
            leaseToken_ = other.leaseToken_;
            metadata_ = other.metadata_;
            returned_ = other.returned_;
            other.returned_ = true;
        }
        return *this;
    }

    GpuFrameLease::operator bool() const noexcept
    {
        return !returned_ && state_ && generation_;
    }

    ID3D11Texture2D* GpuFrameLease::Texture() const noexcept
    {
        if (!generation_ || slot_ >= generation_->slots.size())
        {
            return nullptr;
        }
        return generation_->slots[slot_].texture.get();
    }

    const TappedFrameMetadata& GpuFrameLease::Metadata() const noexcept
    {
        return metadata_;
    }

    void GpuFrameLease::Return() noexcept
    {
        if (returned_)
        {
            if (state_)
            {
                ++state_->doubleReturnDetected;
            }
            return;
        }
        returned_ = true;
        if (state_)
        {
            state_->Return(generation_, slot_, leaseToken_);
        }
    }

    RenderFrameTap::RenderFrameTap()
        : impl_(std::make_unique<Impl>())
    {
    }

    RenderFrameTap::~RenderFrameTap()
    {
        Shutdown();
    }

    void RenderFrameTap::Initialize(
        ID3D11Device* const device,
        ID3D11DeviceContext* const context,
        const RenderFrameTapConfiguration& configuration)
    {
        Shutdown();
        if (!configuration.enabled)
        {
            impl_->state.reset();
            return;
        }
        if (device == nullptr || context == nullptr)
        {
            throw winrt::hresult_invalid_argument();
        }

        auto state = std::make_shared<RenderFrameTapSharedState>();
        state->configuredEnabled = true;
        state->enabled.store(true);
        state->device.copy_from(device);
        state->context.copy_from(context);
        state->generationSeed = impl_->generationSeed;
        state->consumerMode = configuration.consumerMode;
        state->consumerDelay = configuration.consumerDelay;
        if (!configuration.diagnosticDirectory.empty() &&
            !configuration.sessionId.empty())
        {
            const auto fileName = L"p2.3b-tap-" +
                configuration.sessionId + L".jsonl";
            state->diagnosticLogPath = (
                std::filesystem::path(configuration.diagnosticDirectory) /
                fileName).wstring();
        }
        impl_->state = state;
        if (configuration.startDiagnosticConsumer)
        {
            state->consumerKind = RenderFrameTapConsumerKind::Diagnostic;
            impl_->consumer = std::thread(ConsumerMain, state);
        }
    }

    bool RenderFrameTap::Enabled() const noexcept
    {
        return impl_->state && impl_->state->enabled.load();
    }

    void RenderFrameTap::ObserveAndCopy(
        ID3D11Texture2D* const outputCanvas,
        const OutputCanvasDescription& description,
        const RenderFrameTapTimestamp& timestamp) noexcept
    {
        const auto state = impl_->state;
        if (!state || !state->enabled.load() || state->stopping.load() ||
            state->consumerStopRequested)
        {
            if (state)
            {
                ++state->framesDroppedDisabledOrStopping;
            }
            return;
        }
        ++state->framesObservedAtTapPoint;

        std::unique_lock lock(state->mutex, std::try_to_lock);
        if (!lock.owns_lock())
        {
            ++state->framesDroppedLockBusy;
            return;
        }
        if (!state->enabled.load() || state->stopping.load() ||
            state->consumerStopRequested)
        {
            ++state->framesDroppedDisabledOrStopping;
            return;
        }
        if (state->consumerKind == RenderFrameTapConsumerKind::None)
        {
            return;
        }
        if (outputCanvas == nullptr || !IsValidOutputCanvas(description))
        {
            ++state->framesDroppedGenerationMismatch;
            return;
        }

        D3D11_TEXTURE2D_DESC sourceDescription{};
        outputCanvas->GetDesc(&sourceDescription);
        if (sourceDescription.Width != description.width ||
            sourceDescription.Height != description.height ||
            sourceDescription.Format != description.format)
        {
            ++state->framesDroppedGenerationMismatch;
            return;
        }

        if (!state->current ||
            !SameOutputCanvas(state->current->description, description))
        {
            try
            {
                if (!SwitchGenerationLocked(*state, description))
                {
                    ++state->framesDroppedGenerationMismatch;
                    return;
                }
            }
            catch (...)
            {
                ++state->framesDroppedGenerationMismatch;
                return;
            }
            // The transition frame is intentionally not queued.
            ++state->framesDroppedGenerationMismatch;
            return;
        }

        if (state->queueCount >= RenderFrameTapQueueCapacity)
        {
            ++state->framesDroppedQueueFull;
            return;
        }
        const auto freeSlot = std::find_if(
            state->current->slots.begin(),
            state->current->slots.end(),
            [](const TapSlot& slot)
            {
                return slot.state == TapSlotState::Free;
            });
        if (freeSlot == state->current->slots.end())
        {
            ++state->framesDroppedNoFreeSlot;
            return;
        }

        const auto slotIndex = static_cast<std::uint32_t>(
            std::distance(state->current->slots.begin(), freeSlot));
        const auto frameSequence = ++state->nextFrameSequence;
        const auto leaseToken = ++state->nextLeaseToken;
        state->context->CopyResource(freeSlot->texture.get(), outputCanvas);
        ++state->framesCopied;

        freeSlot->state = TapSlotState::Queued;
        freeSlot->leaseToken = leaseToken;
        TappedFrameMetadata metadata{
            slotIndex,
            description.width,
            description.height,
            description.format,
            state->current->id,
            frameSequence,
            timestamp.valid,
            timestamp.systemRelativeTime100ns,
            QueryPerformanceCounterValue()
        };
        if (timestamp.valid)
        {
            ++state->timestampValidCount;
            if (state->current->hasLastTimestamp &&
                timestamp.systemRelativeTime100ns <
                    state->current->lastTimestamp100ns)
            {
                ++state->timestampRegressionCount;
            }
            state->current->hasLastTimestamp = true;
            state->current->lastTimestamp100ns =
                timestamp.systemRelativeTime100ns;
        }
        else
        {
            ++state->timestampMissingCount;
        }

        state->queue[state->queueTail] = QueuedTapFrame{
            state->current,
            slotIndex,
            leaseToken,
            metadata
        };
        state->queueTail =
            (state->queueTail + 1) % RenderFrameTapQueueCapacity;
        ++state->queueCount;
        ++state->framesEnqueued;
        state->UpdateCountsLocked();
        lock.unlock();
        state->condition.notify_one();
    }

    std::optional<GpuFrameLease> RenderFrameTap::TryAcquireForTest()
    {
        const auto state = impl_->state;
        if (!state)
        {
            return std::nullopt;
        }
        std::lock_guard lock(state->mutex);
        return TryAcquireLocked(state);
    }

    bool RenderFrameTap::RegisterConsumer(
        const RenderFrameTapConsumerKind consumer) noexcept
    {
        const auto state = impl_->state;
        if (!state || consumer == RenderFrameTapConsumerKind::None)
        {
            return false;
        }
        std::lock_guard lock(state->mutex);
        if (state->stopping.load() || state->consumerStopRequested ||
            state->consumerKind != RenderFrameTapConsumerKind::None)
        {
            return false;
        }
        state->consumerKind = consumer;
        return true;
    }

    std::optional<GpuFrameLease> RenderFrameTap::WaitAcquire(
        const RenderFrameTapConsumerKind consumer,
        const std::chrono::milliseconds timeout)
    {
        const auto state = impl_->state;
        if (!state || consumer == RenderFrameTapConsumerKind::None)
        {
            return std::nullopt;
        }
        std::unique_lock lock(state->mutex);
        state->condition.wait_for(
            lock,
            timeout,
            [&]
            {
                return state->queueCount > 0 || state->stopping.load() ||
                    state->consumerStopRequested ||
                    state->consumerKind != consumer;
            });
        if (state->consumerKind != consumer || state->stopping.load())
        {
            return std::nullopt;
        }
        if (state->consumerStopRequested)
        {
            if (!state->drainQueuedFrames || state->queueCount == 0)
            {
                return std::nullopt;
            }
        }
        return TryAcquireLocked(state);
    }

    void RenderFrameTap::RequestConsumerStop(
        const RenderFrameTapConsumerKind consumer,
        const bool drainQueuedFrames) noexcept
    {
        const auto state = impl_->state;
        if (!state)
        {
            return;
        }
        {
            std::lock_guard lock(state->mutex);
            if (state->consumerKind != consumer)
            {
                return;
            }
            state->consumerStopRequested = true;
            state->drainQueuedFrames = drainQueuedFrames;
            if (!drainQueuedFrames)
            {
                FlushQueueLocked(*state);
            }
        }
        state->condition.notify_all();
    }

    void RenderFrameTap::UnregisterConsumer(
        const RenderFrameTapConsumerKind consumer) noexcept
    {
        const auto state = impl_->state;
        if (!state)
        {
            return;
        }
        {
            std::lock_guard lock(state->mutex);
            if (state->consumerKind != consumer)
            {
                return;
            }
            FlushQueueLocked(*state);
            state->consumerKind = RenderFrameTapConsumerKind::None;
            state->consumerStopRequested = false;
            state->drainQueuedFrames = false;
        }
        state->condition.notify_all();
    }

    RenderFrameTapConsumerKind RenderFrameTap::ActiveConsumer() const noexcept
    {
        const auto state = impl_->state;
        if (!state)
        {
            return RenderFrameTapConsumerKind::None;
        }
        std::lock_guard lock(state->mutex);
        return state->consumerKind;
    }

    RenderFrameTapDiagnostics RenderFrameTap::Diagnostics() const noexcept
    {
        return Snapshot(impl_->state);
    }

    std::wstring RenderFrameTap::DiagnosticLogPath() const
    {
        return impl_->state ? impl_->state->diagnosticLogPath : std::wstring{};
    }

    void RenderFrameTap::Shutdown() noexcept
    {
        const auto state = impl_->state;
        if (!state)
        {
            return;
        }
        if (state->stopping.exchange(true))
        {
            return;
        }
        const auto started = std::chrono::steady_clock::now();
        state->enabled.store(false);
        {
            std::lock_guard lock(state->mutex);
            FlushQueueLocked(*state);
        }
        state->condition.notify_all();
        if (impl_->consumer.joinable() &&
            impl_->consumer.get_id() != std::this_thread::get_id())
        {
            impl_->consumer.join();
        }

        {
            std::lock_guard lock(state->mutex);
            state->UpdateCountsLocked();
            state->outstandingAtShutdown.store(
                state->outstandingCurrent.load());
            if (state->current)
            {
                const auto outstanding = CountState(
                    state->current,
                    TapSlotState::ConsumerOwned);
                if (outstanding > 0)
                {
                    state->retired.push_back(state->current);
                }
                state->current.reset();
            }
            state->PruneRetiredLocked();
            state->UpdateCountsLocked();
            state->context = nullptr;
            state->device = nullptr;
        }
        impl_->generationSeed = (std::max)(
            impl_->generationSeed,
            state->generationSeed);
        const auto elapsed = std::chrono::duration<double, std::micro>(
            std::chrono::steady_clock::now() - started).count();
        state->shutdownDurationMicroseconds.store(
            static_cast<std::uint64_t>((std::max)(0.0, elapsed)));
        WriteDiagnosticSummary(state);
    }

    RenderFrameTapConfiguration ReadRenderFrameTapConfiguration(
        const std::wstring& diagnosticDirectory,
        const std::wstring& sessionId) noexcept
    {
        RenderFrameTapConfiguration result{};
        const auto mode = ReadEnvironment(L"XB_PREVIEW_DIAGNOSTIC_TAP");
        result.enabled = IsEnabledValue(mode);
        result.startDiagnosticConsumer = true;
        result.consumerMode = mode == L"slow"
            ? RenderFrameTapConsumerMode::Slow
            : RenderFrameTapConsumerMode::Normal;
        result.consumerDelay = ReadConsumerDelay(mode);
        result.diagnosticDirectory = diagnosticDirectory;
        result.sessionId = sessionId;
        return result;
    }
}
