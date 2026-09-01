#include "MicPreflightLevelMonitor.h"

#include "GStreamerAudioCore.h"
#include "GStreamerMicrophoneDeviceMonitor.h"

#include <gst/gst.h>

#include <algorithm>
#include <atomic>
#include <chrono>
#include <cmath>
#include <limits>
#include <mutex>
#include <new>
#include <thread>
#include <utility>

namespace xbpreview
{
    namespace
    {
        constexpr auto PipelineStartTimeout = std::chrono::seconds(8);
        constexpr GstClockTime LevelInterval = 75 * GST_MSECOND;
        constexpr double SilenceDb = -120.0;

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

        std::wstring GStreamerErrorText(GstMessage* const message)
        {
            GError* error{};
            gchar* debug{};
            gst_message_parse_error(message, &error, &debug);
            std::wstring result = error && error->message
                ? Utf8ToWide(error->message)
                : L"GStreamer microphone preflight failed.";
            if (debug && *debug)
            {
                if (!result.empty()) result += L" | ";
                result += Utf8ToWide(debug);
            }
            if (error) g_error_free(error);
            if (debug) g_free(debug);
            return result;
        }

        double MaximumDb(
            const GstStructure* const structure,
            const char* const field) noexcept
        {
            const auto* const container =
                gst_structure_get_value(structure, field);
            if (container == nullptr) return SilenceDb;
            auto result = -std::numeric_limits<double>::infinity();
            const auto include = [&](const GValue* const value)
            {
                if (value != nullptr && G_VALUE_HOLDS_DOUBLE(value))
                    result = (std::max)(result, g_value_get_double(value));
            };
            if (GST_VALUE_HOLDS_LIST(container))
            {
                const auto channels = gst_value_list_get_size(container);
                for (guint index = 0; index < channels; ++index)
                {
                    include(gst_value_list_get_value(container, index));
                }
            }
            else
            {
#pragma warning(push)
#pragma warning(disable: 4996) // GStreamer level uses legacy GValueArray.
                if (!G_VALUE_HOLDS(container, G_TYPE_VALUE_ARRAY))
                {
#pragma warning(pop)
                    return SilenceDb;
                }
                const auto* const array = static_cast<const GValueArray*>(
                    g_value_get_boxed(container));
                if (array == nullptr) return SilenceDb;
                for (guint index = 0; index < array->n_values; ++index)
                    include(&array->values[index]);
            }
            return std::isfinite(result) ? result : SilenceDb;
        }

        double DbToPcm16(const double value) noexcept
        {
            if (!std::isfinite(value) || value <= SilenceDb) return 0.0;
            // The mature level element owns RMS/peak analysis. This is only a
            // unit conversion into the existing PCM16-scaled snapshot ABI.
            const auto linear = std::pow(
                10.0, (std::min)(0.0, value) / 20.0);
            return (std::clamp)(linear * 32767.0, 0.0, 32767.0);
        }

        GstClockTime GstTimeout(const std::chrono::milliseconds value) noexcept
        {
            return static_cast<GstClockTime>(value.count()) * GST_MSECOND;
        }
    }

#if defined(XBPREVIEW_GSTREAMER_AUDIO_TESTS)
    double TestMicPreflightMaximumDb(
        const GstStructure* const structure,
        const char* const field) noexcept
    {
        return MaximumDb(structure, field);
    }
#endif

    const char* MicPreflightPipelineDescription() noexcept
    {
        return "product-selected GstDevice source ! level interval=75000000 "
            "post-messages=true ! fakesink sync=false";
    }

    struct MicPreflightLevelMonitor::Impl final
    {
        mutable std::mutex mutex;
        MicPreflightLevelSnapshot snapshot{};
        std::shared_ptr<GStreamerMicrophoneDeviceBinding> device;
        std::thread worker;
        std::atomic<bool> stopRequested{ false };

        void SetFailure(const HRESULT result, std::wstring message)
        {
            std::lock_guard lock(mutex);
            snapshot.running = false;
            snapshot.available = false;
            snapshot.pipelineState = MicPreflightPipelineState::Failed;
            snapshot.terminalHResult = result;
            snapshot.lastGStreamerError = std::move(message);
        }

        void HandleLevelMessage(GstMessage* const message)
        {
            const auto* const structure = gst_message_get_structure(message);
            if (structure == nullptr ||
                !gst_structure_has_name(structure, "level"))
            {
                return;
            }
            const auto peakDb = MaximumDb(structure, "peak");
            const auto rmsDb = MaximumDb(structure, "rms");
            const auto peakPcm16 = DbToPcm16(peakDb);
            const auto rmsPcm16 = DbToPcm16(rmsDb);
            std::lock_guard lock(mutex);
            if (!snapshot.running || !snapshot.available) return;
            snapshot.peakDb = peakDb;
            snapshot.rmsDb = rmsDb;
            snapshot.peakAbsolutePcm16 = static_cast<std::uint32_t>(
                std::lround(peakPcm16));
            snapshot.rmsPcm16 = rmsPcm16;
            ++snapshot.levelMessageCount;
        }

        void WorkerMain() noexcept
        {
            GstElement* pipeline{};
            GstBus* bus{};
            try
            {
                std::shared_ptr<GStreamerMicrophoneDeviceBinding> selected;
                {
                    std::lock_guard lock(mutex);
                    selected = device;
                }

                std::wstring runtimeError;
                auto result = EnsureGStreamerAudioRuntime(runtimeError);
                if (FAILED(result))
                {
                    SetFailure(result, std::move(runtimeError));
                }
                else
                {
                    auto* const levelFactory =
                        gst_element_factory_find("level");
                    auto* const sinkFactory =
                        gst_element_factory_find("fakesink");
                    if (levelFactory == nullptr || sinkFactory == nullptr)
                    {
                        if (levelFactory) gst_object_unref(levelFactory);
                        if (sinkFactory) gst_object_unref(sinkFactory);
                        SetFailure(
                            HRESULT_FROM_WIN32(ERROR_MOD_NOT_FOUND),
                            L"Required GStreamer preflight element is missing: level or fakesink.");
                    }
                    else
                    {
                        gst_object_unref(levelFactory);
                        gst_object_unref(sinkFactory);
                        pipeline = gst_pipeline_new("mic_preflight_pipeline");
                        auto* const source = selected
                            ? selected->CreateElement("mic_preflight_source")
                            : nullptr;
                        auto* const level = gst_element_factory_make(
                            "level", "mic_preflight_level");
                        auto* const sink = gst_element_factory_make(
                            "fakesink", "mic_preflight_sink");
                        if (pipeline == nullptr || source == nullptr ||
                            level == nullptr || sink == nullptr)
                        {
                            if (source) gst_object_unref(source);
                            if (level) gst_object_unref(level);
                            if (sink) gst_object_unref(sink);
                            SetFailure(
                                E_FAIL,
                                L"GStreamer microphone preflight elements could not be created.");
                        }
                        else
                        {
                            const auto* const continueOnError =
                                g_object_class_find_property(
                                    G_OBJECT_GET_CLASS(source),
                                    "continue-on-error");
                            if (continueOnError == nullptr)
                            {
                                gst_object_unref(source);
                                gst_object_unref(level);
                                gst_object_unref(sink);
                                SetFailure(
                                    E_FAIL,
                                    L"GstDevice created an unsupported microphone source.");
                            }
                            else
                            {
                                g_object_set(
                                    G_OBJECT(source),
                                    "continue-on-error", TRUE,
                                    nullptr);
                                gchar* elementEndpoint{};
                                g_object_get(
                                    G_OBJECT(source),
                                    "device", &elementEndpoint,
                                    nullptr);
                                const auto elementEndpointId =
                                    Utf8ToWide(elementEndpoint);
                                if (elementEndpoint) g_free(elementEndpoint);
                                const auto identityMatches = selected &&
                                    elementEndpointId == selected->EndpointId();
                                {
                                    std::lock_guard lock(mutex);
                                    snapshot.sourceCreatedFromDevice = true;
                                    snapshot.elementEndpointId =
                                        elementEndpointId;
                                    snapshot.elementIdentityMatches =
                                        identityMatches;
                                }
                                if (!identityMatches)
                                {
                                    gst_object_unref(source);
                                    gst_object_unref(level);
                                    gst_object_unref(sink);
                                    SetFailure(
                                        E_FAIL,
                                        L"Preflight source identity does not match the selected endpoint.");
                                }
                                else
                                {
                                    g_object_set(
                                        G_OBJECT(level),
                                        "interval", LevelInterval,
                                        "post-messages", TRUE,
                                        nullptr);
                                    g_object_set(
                                        G_OBJECT(sink),
                                        "sync", FALSE,
                                        nullptr);
                                    gst_bin_add_many(
                                        GST_BIN(pipeline),
                                        source, level, sink,
                                        nullptr);
                                    if (!gst_element_link_many(
                                            source, level, sink, nullptr))
                                    {
                                        SetFailure(
                                            E_FAIL,
                                            L"GStreamer microphone preflight elements could not be linked.");
                                    }
                                    else
                                    {
                                        bus = gst_element_get_bus(pipeline);
                                        if (bus == nullptr)
                                        {
                                            SetFailure(
                                                E_FAIL,
                                                L"GStreamer microphone preflight bus is unavailable.");
                                        }
                                        else
                                        {
                                            const auto transition =
                                                gst_element_set_state(
                                                    pipeline,
                                                    GST_STATE_PLAYING);
                                            GstState state{};
                                            GstState pending{};
                                            const auto wait = transition ==
                                                    GST_STATE_CHANGE_FAILURE
                                                ? GST_STATE_CHANGE_FAILURE
                                                : gst_element_get_state(
                                                    pipeline,
                                                    &state,
                                                    &pending,
                                                    GstTimeout(
                                                        std::chrono::duration_cast<
                                                            std::chrono::milliseconds>(
                                                                PipelineStartTimeout)));
                                            if (wait ==
                                                    GST_STATE_CHANGE_FAILURE ||
                                                state != GST_STATE_PLAYING)
                                            {
                                                SetFailure(
                                                    HRESULT_FROM_WIN32(
                                                        ERROR_NOT_READY),
                                                    L"GStreamer microphone preflight did not reach PLAYING.");
                                            }
                                            else
                                            {
                                                {
                                                    std::lock_guard lock(mutex);
                                                    snapshot.running = true;
                                                    snapshot.available = true;
                                                    snapshot.pipelineState =
                                                        MicPreflightPipelineState::Playing;
                                                    snapshot.terminalHResult =
                                                        S_OK;
                                                    snapshot.lastGStreamerError.clear();
                                                }
                                                while (!stopRequested.load())
                                                {
                                                    auto* const message =
                                                        gst_bus_timed_pop(
                                                            bus,
                                                            25 * GST_MSECOND);
                                                    if (message == nullptr)
                                                        continue;
                                                    const auto type =
                                                        GST_MESSAGE_TYPE(message);
                                                    if (type ==
                                                        GST_MESSAGE_ELEMENT)
                                                    {
                                                        HandleLevelMessage(
                                                            message);
                                                    }
                                                    else if (type ==
                                                        GST_MESSAGE_ERROR)
                                                    {
                                                        SetFailure(
                                                            E_FAIL,
                                                            GStreamerErrorText(
                                                                message));
                                                        gst_message_unref(
                                                            message);
                                                        break;
                                                    }
                                                    gst_message_unref(message);
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (const std::bad_alloc&)
            {
                SetFailure(
                    E_OUTOFMEMORY,
                    L"Microphone preflight allocation failed.");
            }
            catch (...)
            {
                SetFailure(
                    E_UNEXPECTED,
                    L"Microphone preflight worker failed.");
            }

            if (pipeline != nullptr)
            {
                (void)gst_element_set_state(pipeline, GST_STATE_NULL);
                GstState state{};
                GstState pending{};
                (void)gst_element_get_state(
                    pipeline, &state, &pending, 5 * GST_SECOND);
            }
            if (bus != nullptr) gst_object_unref(bus);
            if (pipeline != nullptr) gst_object_unref(pipeline);
            {
                std::lock_guard lock(mutex);
                device.reset();
                snapshot.running = false;
                snapshot.available = false;
                snapshot.resourcesReleased = true;
                ++snapshot.completedReleaseCount;
                if (stopRequested.load())
                {
                    snapshot.pipelineState =
                        MicPreflightPipelineState::Stopped;
                    snapshot.terminalHResult = S_OK;
                    snapshot.lastGStreamerError.clear();
                }
            }
        }
    };

    MicPreflightLevelMonitor::MicPreflightLevelMonitor()
        : impl_(std::make_unique<Impl>())
    {
    }

    MicPreflightLevelMonitor::~MicPreflightLevelMonitor()
    {
        Stop();
    }

    HRESULT MicPreflightLevelMonitor::Start(
        std::shared_ptr<GStreamerMicrophoneDeviceBinding> device,
        const std::wstring& requestedEndpointId) noexcept
    {
        Stop();
        try
        {
            std::lock_guard lock(impl_->mutex);
            ++impl_->snapshot.startRequestCount;
            impl_->snapshot.enabled = true;
            impl_->snapshot.running = false;
            impl_->snapshot.available = false;
            impl_->snapshot.sourceCreatedFromDevice = false;
            impl_->snapshot.elementIdentityMatches = false;
            impl_->snapshot.resourcesReleased = true;
            impl_->snapshot.pipelineState =
                MicPreflightPipelineState::Starting;
            impl_->snapshot.selectedEndpointId = device
                ? device->EndpointId()
                : requestedEndpointId;
            impl_->snapshot.elementEndpointId.clear();
            impl_->snapshot.lastGStreamerError.clear();
            impl_->snapshot.terminalHResult = S_OK;
            impl_->snapshot.peakAbsolutePcm16 = 0;
            impl_->snapshot.rmsPcm16 = 0.0;
            impl_->snapshot.peakDb = SilenceDb;
            impl_->snapshot.rmsDb = SilenceDb;
            impl_->snapshot.levelMessageCount = 0;
            if (device == nullptr)
            {
                impl_->snapshot.pipelineState =
                    MicPreflightPipelineState::Failed;
                impl_->snapshot.terminalHResult =
                    HRESULT_FROM_WIN32(ERROR_NOT_FOUND);
                impl_->snapshot.lastGStreamerError =
                    L"Selected microphone is unavailable.";
                return impl_->snapshot.terminalHResult;
            }
            impl_->device = std::move(device);
            impl_->snapshot.resourcesReleased = false;
            impl_->stopRequested.store(false);
            try
            {
                impl_->worker = std::thread(&Impl::WorkerMain, impl_.get());
            }
            catch (...)
            {
                impl_->device.reset();
                impl_->snapshot.resourcesReleased = true;
                impl_->snapshot.pipelineState =
                    MicPreflightPipelineState::Failed;
                impl_->snapshot.terminalHResult = E_FAIL;
                impl_->snapshot.lastGStreamerError =
                    L"Microphone preflight worker could not start.";
                return E_FAIL;
            }
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

    void MicPreflightLevelMonitor::Stop() noexcept
    {
        if (!impl_) return;
        impl_->stopRequested.store(true);
        if (impl_->worker.joinable() &&
            impl_->worker.get_id() != std::this_thread::get_id())
        {
            impl_->worker.join();
        }
        std::lock_guard lock(impl_->mutex);
        impl_->device.reset();
        impl_->snapshot.enabled = false;
        impl_->snapshot.running = false;
        impl_->snapshot.available = false;
        impl_->snapshot.resourcesReleased = true;
        impl_->snapshot.pipelineState = MicPreflightPipelineState::Stopped;
        impl_->snapshot.terminalHResult = S_OK;
        impl_->snapshot.lastGStreamerError.clear();
        impl_->snapshot.peakAbsolutePcm16 = 0;
        impl_->snapshot.rmsPcm16 = 0.0;
        impl_->snapshot.peakDb = SilenceDb;
        impl_->snapshot.rmsDb = SilenceDb;
    }

    MicPreflightLevelSnapshot MicPreflightLevelMonitor::Snapshot()
        const noexcept
    {
        try
        {
            std::lock_guard lock(impl_->mutex);
            return impl_->snapshot;
        }
        catch (...)
        {
            MicPreflightLevelSnapshot result{};
            result.pipelineState = MicPreflightPipelineState::Failed;
            result.terminalHResult = E_OUTOFMEMORY;
            return result;
        }
    }
}
