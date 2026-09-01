#include "GStreamerMicrophoneDeviceMonitor.h"

#include "GStreamerAudioCore.h"

#include <gst/gst.h>

#include <algorithm>
#include <atomic>
#include <chrono>
#include <mutex>
#include <new>
#include <thread>
#include <utility>

namespace xbpreview
{
    namespace
    {
        constexpr auto MonitorStartTimeout = std::chrono::seconds(5);

        std::wstring Utf8ToWide(const char* const value)
        {
            if (value == nullptr || *value == '\0') return {};
            const auto size = MultiByteToWideChar(
                CP_UTF8, MB_ERR_INVALID_CHARS, value, -1, nullptr, 0);
            if (size <= 1) return {};
            std::wstring result(static_cast<std::size_t>(size), L'\0');
            if (MultiByteToWideChar(
                    CP_UTF8, MB_ERR_INVALID_CHARS, value, -1,
                    result.data(), size) == 0)
            {
                return {};
            }
            result.resize(static_cast<std::size_t>(size - 1));
            return result;
        }

        struct DeviceFacts
        {
            GstDevice* device{};
            std::wstring id;
            std::wstring actualId;
            std::wstring displayName;
            std::wstring properties;
            bool isDefault{};
            bool loopback{};
        };

        DeviceFacts ReadDeviceFacts(GstDevice* const device)
        {
            DeviceFacts result{};
            result.device = device;
            if (auto* const displayName = gst_device_get_display_name(device))
            {
                result.displayName = Utf8ToWide(displayName);
                g_free(displayName);
            }
            auto* const properties = gst_device_get_properties(device);
            if (properties == nullptr) return result;
            if (auto* const serialized = gst_structure_to_string(properties))
            {
                result.properties = Utf8ToWide(serialized);
                g_free(serialized);
            }
            if (const auto* const id =
                    gst_structure_get_string(properties, "device.id"))
            {
                result.id = Utf8ToWide(id);
            }
            if (const auto* const id =
                    gst_structure_get_string(properties, "device.actual-id"))
            {
                result.actualId = Utf8ToWide(id);
            }
            gboolean value{};
            if (gst_structure_get_boolean(
                    properties, "device.default", &value))
            {
                result.isDefault = value != FALSE;
            }
            value = FALSE;
            if (gst_structure_get_boolean(
                    properties, "wasapi2.device.loopback", &value))
            {
                result.loopback = value != FALSE;
            }
            gst_structure_free(properties);
            return result;
        }

        std::wstring GStreamerMessageError(GstMessage* const message)
        {
            GError* error{};
            gchar* debug{};
            gst_message_parse_error(message, &error, &debug);
            std::wstring result = error && error->message
                ? Utf8ToWide(error->message)
                : L"GstDeviceMonitor failed.";
            if (error) g_error_free(error);
            if (debug) g_free(debug);
            return result;
        }
    }

    GStreamerMicrophoneDeviceBinding::GStreamerMicrophoneDeviceBinding(
        GstDevice* const device,
        std::wstring endpointId,
        std::wstring displayName,
        std::wstring properties)
        : device_(device != nullptr
            ? GST_DEVICE(gst_object_ref(device))
            : nullptr),
        endpointId_(std::move(endpointId)),
        displayName_(std::move(displayName)),
        properties_(std::move(properties))
    {
    }

    GStreamerMicrophoneDeviceBinding::~GStreamerMicrophoneDeviceBinding()
    {
        if (device_ != nullptr)
        {
            gst_object_unref(device_);
            device_ = nullptr;
        }
    }

    const std::wstring& GStreamerMicrophoneDeviceBinding::EndpointId()
        const noexcept
    {
        return endpointId_;
    }

    const std::wstring& GStreamerMicrophoneDeviceBinding::DisplayName()
        const noexcept
    {
        return displayName_;
    }

    const std::wstring& GStreamerMicrophoneDeviceBinding::Properties()
        const noexcept
    {
        return properties_;
    }

    GstElement* GStreamerMicrophoneDeviceBinding::CreateElement(
        const char* const name) const noexcept
    {
        return device_ != nullptr
            ? gst_device_create_element(device_, name)
            : nullptr;
    }

    struct GStreamerMicrophoneDeviceMonitor::Impl
    {
        struct Entry
        {
            GStreamerMicrophoneDeviceInfo info;
            std::shared_ptr<GStreamerMicrophoneDeviceBinding> binding;
        };

        mutable std::mutex mutex;
        GstDeviceMonitor* monitor{};
        GstBus* bus{};
        std::thread busThread;
        std::atomic<bool> stopBus{};
        std::uint64_t generation{};
        std::uint32_t deviceAddedCount{};
        std::uint32_t deviceRemovedCount{};
        bool active{};
        std::wstring defaultEndpointId;
        std::wstring defaultSystemEndpointId;
        std::wstring errorText;
        std::vector<Entry> entries;

        void SetError(std::wstring value)
        {
            std::lock_guard lock(mutex);
            errorText = std::move(value);
            active = false;
            defaultEndpointId.clear();
            // A microphone catalog failure must not revoke the independently
            // resolved render endpoint. AudioEndpointLevelMonitor remains the
            // authority for whether this retained endpoint is actually active.
            entries.clear();
            ++generation;
        }

        void Reenumerate()
        {
            auto* const devices = gst_device_monitor_get_devices(monitor);
            std::vector<DeviceFacts> facts;
            for (auto* node = devices; node != nullptr; node = node->next)
            {
                facts.push_back(ReadDeviceFacts(GST_DEVICE(node->data)));
            }

            std::wstring nextDefault;
            std::wstring nextSystemDefault;
            std::vector<Entry> nextEntries;
            const auto defaultSystem = std::find_if(
                facts.begin(), facts.end(),
                [](const DeviceFacts& value)
                {
                    return value.loopback && value.isDefault;
                });
            if (defaultSystem != facts.end())
            {
                const auto concreteId = defaultSystem->actualId.empty()
                    ? defaultSystem->id
                    : defaultSystem->actualId;
                const auto concrete = std::find_if(
                    facts.begin(), facts.end(),
                    [&](const DeviceFacts& value)
                    {
                        return value.loopback && !value.isDefault &&
                            value.id == concreteId;
                    });
                const auto& resolved = concrete != facts.end()
                    ? *concrete
                    : *defaultSystem;
                if (resolved.device != nullptr && !concreteId.empty())
                {
                    // This mirrors GStreamerAudioCore::ResolveDevices and
                    // exposes its concrete render endpoint fact without a
                    // second enumerator or a second default-device policy.
                    nextSystemDefault = concrete != facts.end()
                        ? resolved.id
                        : concreteId;
                }
            }
            for (const auto& value : facts)
            {
                if (value.loopback) continue;
                if (value.isDefault)
                {
                    nextDefault = value.actualId.empty()
                        ? value.id
                        : value.actualId;
                    continue;
                }
                if (value.device == nullptr || value.id.empty()) continue;
                Entry entry{};
                entry.info.endpointId = value.id;
                entry.info.displayName = value.displayName.empty()
                    ? value.id
                    : value.displayName;
                entry.binding = std::shared_ptr<
                    GStreamerMicrophoneDeviceBinding>(
                        new GStreamerMicrophoneDeviceBinding(
                            value.device,
                            value.id,
                            entry.info.displayName,
                            value.properties));
                nextEntries.push_back(std::move(entry));
            }
            for (auto* node = devices; node != nullptr; node = node->next)
            {
                gst_object_unref(node->data);
            }
            g_list_free(devices);

            std::sort(
                nextEntries.begin(), nextEntries.end(),
                [](const Entry& left, const Entry& right)
                {
                    if (left.info.displayName != right.info.displayName)
                        return left.info.displayName < right.info.displayName;
                    return left.info.endpointId < right.info.endpointId;
                });

            std::lock_guard lock(mutex);
            defaultEndpointId = std::move(nextDefault);
            // Audio/Source hotplug can briefly publish a microphone-only
            // catalog. Keep the last resolved render identity through that
            // transition; a later render fact replaces it, while the endpoint
            // meter independently reports an unavailable/stale endpoint.
            if (!nextSystemDefault.empty())
            {
                defaultSystemEndpointId = std::move(nextSystemDefault);
            }
            entries = std::move(nextEntries);
            errorText.clear();
            ++generation;
        }

        void BusMain() noexcept
        {
            try
            {
                while (!stopBus.load())
                {
                    auto* const message = gst_bus_timed_pop(
                        bus, 200 * GST_MSECOND);
                    if (message == nullptr) continue;
                    const auto type = GST_MESSAGE_TYPE(message);
                    if (type == GST_MESSAGE_DEVICE_ADDED ||
                        type == GST_MESSAGE_DEVICE_REMOVED)
                    {
                        {
                            std::lock_guard lock(mutex);
                            if (type == GST_MESSAGE_DEVICE_ADDED)
                                ++deviceAddedCount;
                            else
                                ++deviceRemovedCount;
                        }
                        // GstDeviceMonitor is the source of truth. Re-reading
                        // its current catalog after either official hotplug
                        // message avoids maintaining a second device scanner.
                        Reenumerate();
                    }
                    else if (type == GST_MESSAGE_ERROR)
                    {
                        SetError(GStreamerMessageError(message));
                    }
                    gst_message_unref(message);
                }
            }
            catch (const std::bad_alloc&)
            {
                SetError(L"GstDeviceMonitor catalog allocation failed.");
            }
            catch (...)
            {
                SetError(L"GstDeviceMonitor bus adapter failed.");
            }
        }
    };

    GStreamerMicrophoneDeviceMonitor::GStreamerMicrophoneDeviceMonitor()
        : impl_(std::make_unique<Impl>())
    {
    }

    GStreamerMicrophoneDeviceMonitor::~GStreamerMicrophoneDeviceMonitor()
    {
        Stop();
    }

    HRESULT GStreamerMicrophoneDeviceMonitor::Start() noexcept
    {
        try
        {
            std::wstring errorText;
            const auto initialized = EnsureGStreamerAudioRuntime(errorText);
            if (FAILED(initialized))
            {
                impl_->SetError(std::move(errorText));
                return initialized;
            }
            {
                std::lock_guard lock(impl_->mutex);
                if (impl_->active) return S_OK;
            }

            impl_->monitor = gst_device_monitor_new();
            if (impl_->monitor == nullptr)
            {
                impl_->SetError(L"gst_device_monitor_new failed.");
                return E_FAIL;
            }
            auto* const caps = gst_caps_new_empty_simple("audio/x-raw");
            const auto filter = gst_device_monitor_add_filter(
                impl_->monitor, "Audio/Source", caps);
            gst_caps_unref(caps);
            if (filter == 0 || !gst_device_monitor_start(impl_->monitor))
            {
                impl_->SetError(
                    L"GstDeviceMonitor could not start Audio/Source.");
                Stop();
                return E_FAIL;
            }
            impl_->bus = gst_device_monitor_get_bus(impl_->monitor);
            if (impl_->bus == nullptr)
            {
                impl_->SetError(L"GstDeviceMonitor bus is unavailable.");
                Stop();
                return E_FAIL;
            }

            const auto deadline = std::chrono::steady_clock::now() +
                MonitorStartTimeout;
            bool started{};
            while (std::chrono::steady_clock::now() < deadline)
            {
                auto* const message = gst_bus_timed_pop(
                    impl_->bus, 100 * GST_MSECOND);
                if (message == nullptr) continue;
                const auto type = GST_MESSAGE_TYPE(message);
                if (type == GST_MESSAGE_DEVICE_MONITOR_STARTED)
                {
                    gboolean success{};
                    gst_message_parse_device_monitor_started(message, &success);
                    started = success != FALSE;
                    gst_message_unref(message);
                    break;
                }
                if (type == GST_MESSAGE_ERROR)
                {
                    impl_->SetError(GStreamerMessageError(message));
                    gst_message_unref(message);
                    Stop();
                    return E_FAIL;
                }
                gst_message_unref(message);
            }
            if (!started)
            {
                impl_->SetError(
                    L"GstDeviceMonitor initial enumeration timed out.");
                Stop();
                return HRESULT_FROM_WIN32(ERROR_TIMEOUT);
            }

            impl_->Reenumerate();
            {
                std::lock_guard lock(impl_->mutex);
                impl_->active = true;
            }
            impl_->stopBus.store(false);
            impl_->busThread = std::thread(&Impl::BusMain, impl_.get());
            return S_OK;
        }
        catch (const std::bad_alloc&)
        {
            impl_->SetError(L"GstDeviceMonitor allocation failed.");
            Stop();
            return E_OUTOFMEMORY;
        }
        catch (...)
        {
            impl_->SetError(L"GstDeviceMonitor start failed.");
            Stop();
            return E_FAIL;
        }
    }

    void GStreamerMicrophoneDeviceMonitor::Stop() noexcept
    {
        if (!impl_) return;
        impl_->stopBus.store(true);
        if (impl_->busThread.joinable() &&
            impl_->busThread.get_id() != std::this_thread::get_id())
        {
            impl_->busThread.join();
        }
        if (impl_->monitor != nullptr)
        {
            gst_device_monitor_stop(impl_->monitor);
        }
        if (impl_->bus != nullptr)
        {
            gst_object_unref(impl_->bus);
            impl_->bus = nullptr;
        }
        if (impl_->monitor != nullptr)
        {
            gst_object_unref(impl_->monitor);
            impl_->monitor = nullptr;
        }
        std::lock_guard lock(impl_->mutex);
        impl_->active = false;
        impl_->defaultEndpointId.clear();
        impl_->defaultSystemEndpointId.clear();
        impl_->entries.clear();
        ++impl_->generation;
    }

    GStreamerMicrophoneDeviceSnapshot
        GStreamerMicrophoneDeviceMonitor::Snapshot() const
    {
        GStreamerMicrophoneDeviceSnapshot result{};
        std::lock_guard lock(impl_->mutex);
        result.generation = impl_->generation;
        result.deviceAddedCount = impl_->deviceAddedCount;
        result.deviceRemovedCount = impl_->deviceRemovedCount;
        result.monitorActive = impl_->active;
        result.defaultEndpointId = impl_->defaultEndpointId;
        result.defaultSystemEndpointId = impl_->defaultSystemEndpointId;
        result.defaultSystemAvailable =
            !impl_->defaultSystemEndpointId.empty();
        result.errorText = impl_->errorText;
        for (const auto& entry : impl_->entries)
        {
            result.devices.push_back(entry.info);
            if (entry.info.endpointId == impl_->defaultEndpointId)
            {
                result.defaultAvailable = true;
                result.defaultDisplayName = entry.info.displayName;
            }
        }
        return result;
    }

    std::shared_ptr<GStreamerMicrophoneDeviceBinding>
        GStreamerMicrophoneDeviceMonitor::LockDefault() const
    {
        std::lock_guard lock(impl_->mutex);
        const auto selected = std::find_if(
            impl_->entries.begin(), impl_->entries.end(),
            [this](const Impl::Entry& value)
            {
                return value.info.endpointId == impl_->defaultEndpointId;
            });
        return selected != impl_->entries.end()
            ? selected->binding
            : nullptr;
    }

    std::shared_ptr<GStreamerMicrophoneDeviceBinding>
        GStreamerMicrophoneDeviceMonitor::LockEndpoint(
            const std::wstring& endpointId) const
    {
        std::lock_guard lock(impl_->mutex);
        const auto selected = std::find_if(
            impl_->entries.begin(), impl_->entries.end(),
            [&](const Impl::Entry& value)
            {
                return value.info.endpointId == endpointId;
            });
        return selected != impl_->entries.end()
            ? selected->binding
            : nullptr;
    }

    bool GStreamerMicrophoneDeviceMonitor::Contains(
        const std::wstring& endpointId) const noexcept
    {
        try
        {
            return LockEndpoint(endpointId) != nullptr;
        }
        catch (...)
        {
            return false;
        }
    }
}
