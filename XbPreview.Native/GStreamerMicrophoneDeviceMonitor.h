#pragma once

#include <windows.h>

#include <cstdint>
#include <memory>
#include <string>
#include <vector>

typedef struct _GstDevice GstDevice;
typedef struct _GstElement GstElement;

namespace xbpreview
{
    struct GStreamerMicrophoneDeviceInfo
    {
        std::wstring endpointId;
        std::wstring displayName;
    };

    struct GStreamerMicrophoneDeviceSnapshot
    {
        std::uint64_t generation{};
        std::uint32_t deviceAddedCount{};
        std::uint32_t deviceRemovedCount{};
        bool monitorActive{};
        bool defaultAvailable{};
        std::wstring defaultEndpointId;
        std::wstring defaultDisplayName;
        bool defaultSystemAvailable{};
        std::wstring defaultSystemEndpointId;
        std::wstring errorText;
        std::vector<GStreamerMicrophoneDeviceInfo> devices;
    };

    // Owns a strong reference to the exact GstDevice selected for one
    // recording Session. A hotplug refresh may replace the UI catalog, but it
    // cannot replace this binding or make the Session follow a new default.
    class GStreamerMicrophoneDeviceBinding final
    {
    public:
        ~GStreamerMicrophoneDeviceBinding();

        GStreamerMicrophoneDeviceBinding(
            const GStreamerMicrophoneDeviceBinding&) = delete;
        GStreamerMicrophoneDeviceBinding& operator=(
            const GStreamerMicrophoneDeviceBinding&) = delete;

        [[nodiscard]] const std::wstring& EndpointId() const noexcept;
        [[nodiscard]] const std::wstring& DisplayName() const noexcept;
        [[nodiscard]] const std::wstring& Properties() const noexcept;
        [[nodiscard]] GstElement* CreateElement(const char* name) const noexcept;

    private:
        friend class GStreamerMicrophoneDeviceMonitor;

        GStreamerMicrophoneDeviceBinding(
            GstDevice* device,
            std::wstring endpointId,
            std::wstring displayName,
            std::wstring properties);

        GstDevice* device_{};
        std::wstring endpointId_;
        std::wstring displayName_;
        std::wstring properties_;
    };

    class GStreamerMicrophoneDeviceMonitor final
    {
    public:
        GStreamerMicrophoneDeviceMonitor();
        ~GStreamerMicrophoneDeviceMonitor();

        GStreamerMicrophoneDeviceMonitor(
            const GStreamerMicrophoneDeviceMonitor&) = delete;
        GStreamerMicrophoneDeviceMonitor& operator=(
            const GStreamerMicrophoneDeviceMonitor&) = delete;

        [[nodiscard]] HRESULT Start() noexcept;
        void Stop() noexcept;
        [[nodiscard]] GStreamerMicrophoneDeviceSnapshot Snapshot() const;
        [[nodiscard]] std::shared_ptr<GStreamerMicrophoneDeviceBinding>
            LockDefault() const;
        [[nodiscard]] std::shared_ptr<GStreamerMicrophoneDeviceBinding>
            LockEndpoint(const std::wstring& endpointId) const;
        [[nodiscard]] bool Contains(
            const std::wstring& endpointId) const noexcept;

    private:
        struct Impl;
        std::unique_ptr<Impl> impl_;
    };
}
