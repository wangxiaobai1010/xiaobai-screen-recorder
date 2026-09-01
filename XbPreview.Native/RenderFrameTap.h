#pragma once

#include "OutputCanvasTarget.h"

#include <d3d11.h>
#include <winrt/base.h>

#include <chrono>
#include <cstdint>
#include <memory>
#include <optional>
#include <string>

namespace xbpreview
{
    inline constexpr std::uint32_t RenderFrameTapPoolSize = 6;
    inline constexpr std::uint32_t RenderFrameTapQueueCapacity = 4;

    enum class RenderFrameTapConsumerMode
    {
        Normal,
        Slow
    };

    // Internal destructive-queue ownership. This is deliberately not part of
    // XbPreviewApi.h and therefore does not extend the public C ABI.
    enum class RenderFrameTapConsumerKind
    {
        None,
        Diagnostic,
        Encoder
    };

    struct RenderFrameTapConfiguration
    {
        bool enabled{};
        bool startDiagnosticConsumer{ true };
        RenderFrameTapConsumerMode consumerMode{
            RenderFrameTapConsumerMode::Normal };
        std::chrono::milliseconds consumerDelay{};
        std::wstring diagnosticDirectory;
        std::wstring sessionId;
    };

    struct RenderFrameTapTimestamp
    {
        bool valid{};
        std::int64_t systemRelativeTime100ns{};
    };

    struct TappedFrameMetadata
    {
        std::uint32_t poolSlot{};
        std::uint32_t width{};
        std::uint32_t height{};
        DXGI_FORMAT format{ DXGI_FORMAT_UNKNOWN };
        std::uint64_t generation{};
        std::uint64_t frameSequence{};
        bool timestampValid{};
        std::int64_t systemRelativeTime100ns{};
        std::int64_t enqueueQpc{};
    };

    struct RenderFrameTapDiagnostics
    {
        bool tapEnabled{};
        std::uint32_t poolSize{ RenderFrameTapPoolSize };
        std::uint32_t queueCapacity{ RenderFrameTapQueueCapacity };
        std::uint64_t generation{};
        std::uint64_t framesObservedAtTapPoint{};
        std::uint64_t framesCopied{};
        std::uint64_t framesEnqueued{};
        std::uint64_t framesConsumed{};
        std::uint64_t framesReturned{};
        std::uint64_t framesDroppedNoFreeSlot{};
        std::uint64_t framesDroppedQueueFull{};
        std::uint64_t framesDroppedGenerationMismatch{};
        std::uint64_t framesDroppedDisabledOrStopping{};
        std::uint64_t framesDroppedLockBusy{};
        std::uint64_t timestampValidCount{};
        std::uint64_t timestampMissingCount{};
        std::uint64_t timestampRegressionCount{};
        std::uint32_t queueDepthCurrent{};
        std::uint32_t queueDepthHighWatermark{};
        std::uint32_t freeSlotsCurrent{};
        std::uint32_t consumerOwnedCurrent{};
        std::uint32_t outstandingCurrent{};
        std::uint32_t outstandingHighWatermark{};
        std::uint64_t generationChangeCount{};
        std::uint64_t staleFramesFlushed{};
        std::uint64_t lateReturnsFromOldGeneration{};
        std::uint64_t doubleReturnDetected{};
        std::uint64_t invalidStateTransitionDetected{};
        std::uint64_t texturesCreated{};
        std::uint32_t outstandingAtShutdown{};
        double shutdownDurationMilliseconds{};
    };

    struct RenderFrameTapSharedState;
    struct RenderFrameTapGeneration;

    class GpuFrameLease final
    {
    public:
        GpuFrameLease() = default;
        ~GpuFrameLease();
        GpuFrameLease(const GpuFrameLease&) = delete;
        GpuFrameLease& operator=(const GpuFrameLease&) = delete;
        GpuFrameLease(GpuFrameLease&& other) noexcept;
        GpuFrameLease& operator=(GpuFrameLease&& other) noexcept;

        [[nodiscard]] explicit operator bool() const noexcept;
        [[nodiscard]] ID3D11Texture2D* Texture() const noexcept;
        [[nodiscard]] const TappedFrameMetadata& Metadata() const noexcept;
        void Return() noexcept;

        GpuFrameLease(
            std::shared_ptr<RenderFrameTapSharedState> state,
            std::shared_ptr<RenderFrameTapGeneration> generation,
            std::uint32_t slot,
            std::uint64_t leaseToken,
            const TappedFrameMetadata& metadata) noexcept;

    private:
        std::shared_ptr<RenderFrameTapSharedState> state_;
        std::shared_ptr<RenderFrameTapGeneration> generation_;
        std::uint32_t slot_{};
        std::uint64_t leaseToken_{};
        TappedFrameMetadata metadata_{};
        bool returned_{ true };
    };

    class RenderFrameTap final
    {
    public:
        RenderFrameTap();
        ~RenderFrameTap();
        RenderFrameTap(const RenderFrameTap&) = delete;
        RenderFrameTap& operator=(const RenderFrameTap&) = delete;

        void Initialize(
            ID3D11Device* device,
            ID3D11DeviceContext* context,
            const RenderFrameTapConfiguration& configuration);

        [[nodiscard]] bool Enabled() const noexcept;

        void ObserveAndCopy(
            ID3D11Texture2D* outputCanvas,
            const OutputCanvasDescription& description,
            const RenderFrameTapTimestamp& timestamp) noexcept;

        [[nodiscard]] std::optional<GpuFrameLease> TryAcquireForTest();
        [[nodiscard]] bool RegisterConsumer(
            RenderFrameTapConsumerKind consumer) noexcept;
        [[nodiscard]] std::optional<GpuFrameLease> WaitAcquire(
            RenderFrameTapConsumerKind consumer,
            std::chrono::milliseconds timeout);
        void RequestConsumerStop(
            RenderFrameTapConsumerKind consumer,
            bool drainQueuedFrames) noexcept;
        void UnregisterConsumer(RenderFrameTapConsumerKind consumer) noexcept;
        [[nodiscard]] RenderFrameTapConsumerKind ActiveConsumer() const noexcept;
        [[nodiscard]] RenderFrameTapDiagnostics Diagnostics() const noexcept;
        [[nodiscard]] std::wstring DiagnosticLogPath() const;
        void Shutdown() noexcept;

    private:
        struct Impl;
        std::unique_ptr<Impl> impl_;
    };

    [[nodiscard]] RenderFrameTapConfiguration ReadRenderFrameTapConfiguration(
        const std::wstring& diagnosticDirectory,
        const std::wstring& sessionId) noexcept;
}
