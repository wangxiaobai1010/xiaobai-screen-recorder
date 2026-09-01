#include "GStreamerAudioCore.h"

#include "GStreamerMicrophoneDeviceMonitor.h"

#include <gst/gst.h>

#include <algorithm>
#include <atomic>
#include <chrono>
#include <condition_variable>
#include <cstdint>
#include <filesystem>
#include <mutex>
#include <new>
#include <string>
#include <thread>
#include <vector>

namespace xbpreview
{
    namespace
    {
        constexpr auto MonitorStartTimeout = std::chrono::seconds(5);
        constexpr auto PipelineStartTimeout = std::chrono::seconds(8);
        constexpr auto PipelineEosTimeout = std::chrono::seconds(15);

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

        std::filesystem::path CurrentModuleDirectory() noexcept
        {
            HMODULE module{};
            if (!GetModuleHandleExW(
                    GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS |
                        GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
                    reinterpret_cast<LPCWSTR>(&CurrentModuleDirectory),
                    &module))
            {
                return {};
            }
            std::wstring path(32768, L'\0');
            const auto length = GetModuleFileNameW(
                module, path.data(), static_cast<DWORD>(path.size()));
            if (length == 0 || length >= path.size()) return {};
            path.resize(length);
            return std::filesystem::path(path).parent_path();
        }

        bool SetPrivateEnvironment(
            const wchar_t* const name,
            const std::filesystem::path& value) noexcept
        {
            return SetEnvironmentVariableW(name, value.c_str()) != FALSE;
        }

        HRESULT ConfigurePrivateRuntime() noexcept
        {
            try
            {
                const auto module = CurrentModuleDirectory();
                if (module.empty())
                    return HRESULT_FROM_WIN32(ERROR_PATH_NOT_FOUND);
                const auto runtime = module / L"gstreamer";
                const auto plugins = runtime / L"plugins";
                const auto gioModules = runtime / L"gio-modules";
                std::error_code error;
                if (!std::filesystem::is_directory(plugins, error) || error)
                    return HRESULT_FROM_WIN32(ERROR_PATH_NOT_FOUND);

                if (!SetPrivateEnvironment(
                        L"GST_PLUGIN_SYSTEM_PATH_1_0", plugins) ||
                    !SetEnvironmentVariableW(L"GST_PLUGIN_PATH_1_0", L"") ||
                    !SetEnvironmentVariableW(L"GST_PLUGIN_PATH", L"") ||
                    !SetEnvironmentVariableW(L"GST_REGISTRY_FORK", L"no") ||
                    !SetPrivateEnvironment(L"GIO_MODULE_DIR", gioModules))
                {
                    return HRESULT_FROM_WIN32(GetLastError());
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

        HRESULT EnsureGStreamerInitializedInternal(
            std::wstring& errorText) noexcept
        {
            static std::mutex mutex;
            static HRESULT result{ E_PENDING };
            static std::wstring retainedError;
            std::lock_guard lock(mutex);
            if (result != E_PENDING)
            {
                errorText = retainedError;
                return result;
            }

            result = ConfigurePrivateRuntime();
            if (FAILED(result))
            {
                retainedError = L"GStreamer private runtime is missing.";
                errorText = retainedError;
                return result;
            }

            GError* error{};
            if (!gst_init_check(nullptr, nullptr, &error))
            {
                retainedError = error && error->message
                    ? Utf8ToWide(error->message)
                    : L"gst_init_check failed.";
                if (error) g_error_free(error);
                result = E_FAIL;
                errorText = retainedError;
                return result;
            }

            guint major{};
            guint minor{};
            guint micro{};
            guint nano{};
            gst_version(&major, &minor, &micro, &nano);
            if (major != GStreamerAudioVersionMajor ||
                minor != GStreamerAudioVersionMinor ||
                micro != GStreamerAudioVersionMicro)
            {
                retainedError = L"GStreamer runtime version must be exactly 1.28.6.";
                result = HRESULT_FROM_WIN32(ERROR_REVISION_MISMATCH);
                errorText = retainedError;
                return result;
            }

            for (const auto* const factory : {
                    "wasapi2src", "queue", "audioconvert", "audioresample",
                    "valve", "webrtcdsp", "flacenc", "filesink" })
            {
                auto* const feature = gst_element_factory_find(factory);
                if (feature == nullptr)
                {
                    retainedError = L"Required GStreamer element is missing: " +
                        Utf8ToWide(factory);
                    result = HRESULT_FROM_WIN32(ERROR_MOD_NOT_FOUND);
                    errorText = retainedError;
                    return result;
                }
                gst_object_unref(feature);
            }

            result = S_OK;
            errorText.clear();
            return result;
        }

        struct DeviceFacts
        {
            GstDevice* device{};
            std::string id;
            std::string actualId;
            std::string displayName;
            std::string properties;
            bool isDefault{};
            bool loopback{};
        };

        DeviceFacts ReadDeviceFacts(GstDevice* const device)
        {
            DeviceFacts result{};
            result.device = device;
            if (auto* const displayName =
                    gst_device_get_display_name(device))
            {
                result.displayName = displayName;
                g_free(displayName);
            }
            auto* const properties = gst_device_get_properties(device);
            if (properties == nullptr) return result;
            if (auto* const serialized =
                    gst_structure_to_string(properties))
            {
                result.properties = serialized;
                g_free(serialized);
            }
            if (const auto* const id =
                    gst_structure_get_string(properties, "device.id"))
            {
                result.id = id;
            }
            if (const auto* const id =
                    gst_structure_get_string(properties, "device.actual-id"))
            {
                result.actualId = id;
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

        GstClockTime GstTimeout(const std::chrono::milliseconds value) noexcept
        {
            return static_cast<GstClockTime>(value.count()) * GST_MSECOND;
        }

        std::wstring GStreamerErrorText(
            GstMessage* const message,
            const bool warning)
        {
            GError* error{};
            gchar* debug{};
            if (warning)
                gst_message_parse_warning(message, &error, &debug);
            else
                gst_message_parse_error(message, &error, &debug);
            std::wstring result;
            if (error && error->message) result = Utf8ToWide(error->message);
            if (debug && *debug)
            {
                if (!result.empty()) result += L" | ";
                result += Utf8ToWide(debug);
            }
            if (error) g_error_free(error);
            if (debug) g_free(debug);
            return result;
        }
    }

    HRESULT EnsureGStreamerAudioRuntime(std::wstring& errorText) noexcept
    {
        return EnsureGStreamerInitializedInternal(errorText);
    }

    const char* GStreamerAudioModeName(const GStreamerAudioMode mode) noexcept
    {
        switch (mode)
        {
        case GStreamerAudioMode::SystemOnly: return "SystemOnly";
        case GStreamerAudioMode::MicrophoneOnly: return "MicrophoneOnly";
        case GStreamerAudioMode::Dual: return "Dual";
        default: return "None";
        }
    }

    const char* GStreamerAudioPipelineStateName(
        const GStreamerAudioPipelineState state) noexcept
    {
        switch (state)
        {
        case GStreamerAudioPipelineState::Idle: return "Idle";
        case GStreamerAudioPipelineState::Starting: return "Starting";
        case GStreamerAudioPipelineState::Playing: return "Playing";
        case GStreamerAudioPipelineState::Paused: return "Paused";
        case GStreamerAudioPipelineState::EndOfStream: return "EndOfStream";
        case GStreamerAudioPipelineState::Stopped: return "Stopped";
        case GStreamerAudioPipelineState::Failed: return "Failed";
        default: return "Unknown";
        }
    }

    const char* GStreamerAudioPipelineDescription(
        const GStreamerAudioMode mode) noexcept
    {
        constexpr auto systemOnly =
            "wasapi2src name=system_source loopback=true continue-on-error=true "
            "! queue name=system_queue ! audioconvert ! audioresample "
            "! audio/x-raw,format=S16LE,layout=interleaved,rate=48000,channels=2 "
            "! flacenc name=system_flac_encoder ! filesink name=system_sink";
        constexpr auto microphoneOnly =
            "valve name=mic_device_guard drop=false drop-mode=transform-to-gap "
            "! queue name=mic_queue ! audioconvert ! audioresample "
            "! audio/x-raw,format=S16LE,layout=interleaved,rate=48000,channels=1 "
            "! webrtcdsp name=mic_dsp noise-suppression=true "
            "noise-suppression-level=moderate high-pass-filter=true "
            "echo-cancel=false "
            "! flacenc name=mic_flac_encoder ! filesink name=mic_sink";
        constexpr auto dual =
            "wasapi2src name=system_source loopback=true continue-on-error=true "
            "! queue name=system_queue ! audioconvert ! audioresample "
            "! audio/x-raw,format=S16LE,layout=interleaved,rate=48000,channels=2 "
            "! flacenc name=system_flac_encoder ! filesink name=system_sink "
            "valve name=mic_device_guard drop=false drop-mode=transform-to-gap "
            "! queue name=mic_queue ! audioconvert ! audioresample "
            "! audio/x-raw,format=S16LE,layout=interleaved,rate=48000,channels=1 "
            "! webrtcdsp name=mic_dsp noise-suppression=true "
            "noise-suppression-level=moderate high-pass-filter=true "
            "echo-cancel=false "
            "! flacenc name=mic_flac_encoder ! filesink name=mic_sink";
        switch (mode)
        {
        case GStreamerAudioMode::SystemOnly: return systemOnly;
        case GStreamerAudioMode::MicrophoneOnly: return microphoneOnly;
        case GStreamerAudioMode::Dual: return dual;
        default: return nullptr;
        }
    }

    struct GStreamerAudioCore::Impl
    {
        mutable std::mutex mutex;
        std::condition_variable condition;
        GStreamerAudioSnapshot snapshot{};
        GstDeviceMonitor* monitor{};
        GstBus* monitorBus{};
        std::shared_ptr<GStreamerMicrophoneDeviceBinding> micDevice;
        GstDevice* systemDevice{};
        GstElement* pipeline{};
        GstElement* micDeviceGuard{};
        GstBus* pipelineBus{};
        std::thread busThread;
        std::atomic<bool> stopBus{};
        std::string micDeviceId;
        std::string systemDeviceId;

        void SetFailure(const HRESULT value, std::wstring message)
        {
            std::lock_guard lock(mutex);
            if (SUCCEEDED(snapshot.terminalHResult))
                snapshot.terminalHResult = value;
            if (!message.empty()) snapshot.lastGStreamerError = std::move(message);
            snapshot.pipelineState = GStreamerAudioPipelineState::Failed;
            condition.notify_all();
        }

        bool StartMonitor(std::wstring& errorText)
        {
            monitor = gst_device_monitor_new();
            if (monitor == nullptr)
            {
                errorText = L"gst_device_monitor_new failed.";
                return false;
            }
            auto* const caps = gst_caps_new_empty_simple("audio/x-raw");
            const auto filter = gst_device_monitor_add_filter(
                monitor, "Audio/Source", caps);
            gst_caps_unref(caps);
            if (filter == 0 || !gst_device_monitor_start(monitor))
            {
                errorText = L"GstDeviceMonitor could not start Audio/Source.";
                return false;
            }
            monitorBus = gst_device_monitor_get_bus(monitor);
            if (monitorBus == nullptr)
            {
                errorText = L"GstDeviceMonitor bus is unavailable.";
                return false;
            }

            const auto deadline = std::chrono::steady_clock::now() +
                MonitorStartTimeout;
            bool started{};
            while (std::chrono::steady_clock::now() < deadline)
            {
                auto* const message = gst_bus_timed_pop(
                    monitorBus, 100 * GST_MSECOND);
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
                    errorText = GStreamerErrorText(message, false);
                    gst_message_unref(message);
                    return false;
                }
                gst_message_unref(message);
            }
            if (!started)
            {
                errorText = L"GstDeviceMonitor initial enumeration timed out.";
                return false;
            }
            snapshot.deviceMonitorActive = true;
            return true;
        }

        bool ResolveDevices(
            const GStreamerAudioConfig& config,
            std::wstring& errorText)
        {
            auto* const devices = gst_device_monitor_get_devices(monitor);
            std::vector<DeviceFacts> facts;
            for (auto* node = devices; node != nullptr; node = node->next)
            {
                facts.push_back(ReadDeviceFacts(GST_DEVICE(node->data)));
            }
#if defined(XBPREVIEW_GSTREAMER_AUDIO_TESTS)
            if (config.simulateMissingMicrophone)
            {
                facts.erase(
                    std::remove_if(
                        facts.begin(), facts.end(),
                        [](const DeviceFacts& value)
                        {
                            return !value.loopback && !value.isDefault;
                        }),
                    facts.end());
            }
            if (!config.simulateMissingMicrophoneEndpointId.empty())
            {
                facts.erase(
                    std::remove_if(
                        facts.begin(), facts.end(),
                        [&](const DeviceFacts& value)
                        {
                            return Utf8ToWide(value.id.c_str()) ==
                                config.simulateMissingMicrophoneEndpointId;
                        }),
                    facts.end());
            }
#endif

            const auto select = [&](const bool loopback,
                                    GstDevice*& selected,
                                    std::string& selectedId)
            {
                const auto defaultDevice = std::find_if(
                    facts.begin(), facts.end(),
                    [loopback](const DeviceFacts& value)
                    {
                        return value.loopback == loopback && value.isDefault;
                    });
                if (defaultDevice == facts.end()) return false;
                const auto concreteId = defaultDevice->actualId.empty()
                    ? defaultDevice->id
                    : defaultDevice->actualId;
                const auto concrete = std::find_if(
                    facts.begin(), facts.end(),
                    [&](const DeviceFacts& value)
                    {
                        return value.loopback == loopback &&
                            !value.isDefault && value.id == concreteId;
                    });
                const auto& value = concrete != facts.end()
                    ? *concrete
                    : *defaultDevice;
                const auto boundId = concrete != facts.end()
                    ? value.id
                    : concreteId;
                if (value.device == nullptr || boundId.empty()) return false;
                selected = GST_DEVICE(gst_object_ref(value.device));
                selectedId = boundId;
                return true;
            };

            const auto wantsSystem =
                config.mode == GStreamerAudioMode::SystemOnly ||
                config.mode == GStreamerAudioMode::Dual;
            const auto wantsMic =
                config.mode == GStreamerAudioMode::MicrophoneOnly ||
                config.mode == GStreamerAudioMode::Dual;
            const auto systemOk = !wantsSystem ||
                select(true, systemDevice, systemDeviceId);
            bool micOk = !wantsMic;
            if (wantsMic && config.microphoneDevice != nullptr)
            {
                const auto endpointId =
                    config.microphoneDevice->EndpointId();
                const auto present = std::find_if(
                    facts.begin(), facts.end(),
                    [&](const DeviceFacts& value)
                    {
                        return !value.loopback && !value.isDefault &&
                            Utf8ToWide(value.id.c_str()) == endpointId;
                    });
                if (present != facts.end())
                {
                    micDevice = config.microphoneDevice;
                    micDeviceId = present->id;
                    micOk = true;
                }
            }
            for (auto* node = devices; node != nullptr; node = node->next)
            {
                gst_object_unref(node->data);
            }
            g_list_free(devices);

            if (!micOk)
            {
                errorText = L"MicUnavailableAtStart";
                return false;
            }
            if (!systemOk)
            {
                errorText = L"System audio loopback device is unavailable.";
                return false;
            }
            snapshot.micDeviceId = Utf8ToWide(micDeviceId.c_str());
            if (wantsMic)
            {
                snapshot.micDeviceDisplayName = micDevice->DisplayName();
                snapshot.micDeviceProperties = micDevice->Properties();
                snapshot.micSessionBound = micDevice != nullptr &&
                    !micDeviceId.empty();
            }
            return true;
        }

        bool BuildPipeline(
            const GStreamerAudioConfig& config,
            std::wstring& errorText)
        {
            GError* error{};
            pipeline = gst_parse_launch(
                GStreamerAudioPipelineDescription(config.mode), &error);
            if (pipeline == nullptr || error != nullptr)
            {
                errorText = error && error->message
                    ? Utf8ToWide(error->message)
                    : L"gst_parse_launch failed.";
                if (error) g_error_free(error);
                return false;
            }

            const auto setString = [&](const char* const elementName,
                                       const char* const property,
                                       const std::string& value)
            {
                auto* const element = gst_bin_get_by_name(
                    GST_BIN(pipeline), elementName);
                if (element == nullptr) return false;
                g_object_set(G_OBJECT(element), property, value.c_str(), nullptr);
                gst_object_unref(element);
                return true;
            };

            if (!systemDeviceId.empty() &&
                !setString("system_source", "device", systemDeviceId))
            {
                errorText = L"GStreamer system source is missing.";
                return false;
            }
            if (micDevice != nullptr)
            {
                auto* const source =
                    micDevice->CreateElement("mic_source");
                if (source == nullptr)
                {
                    errorText =
                        L"GstDevice could not create the microphone source.";
                    return false;
                }
                const auto* const continueOnError =
                    g_object_class_find_property(
                        G_OBJECT_GET_CLASS(source), "continue-on-error");
                if (continueOnError == nullptr)
                {
                    gst_object_unref(source);
                    errorText = L"GstDevice created an unsupported microphone source.";
                    return false;
                }
                g_object_set(
                    G_OBJECT(source), "continue-on-error", TRUE, nullptr);
                gchar* elementDeviceId{};
                g_object_get(
                    G_OBJECT(source), "device", &elementDeviceId, nullptr);
                snapshot.micElementDeviceId = Utf8ToWide(elementDeviceId);
                if (elementDeviceId) g_free(elementDeviceId);
                snapshot.micElementIdentityMatches =
                    snapshot.micElementDeviceId == snapshot.micDeviceId;
                if (!snapshot.micElementIdentityMatches)
                {
                    gst_object_unref(source);
                    errorText =
                        L"GstDevice element identity does not match the locked endpoint.";
                    return false;
                }
                auto* const guard = gst_bin_get_by_name(
                    GST_BIN(pipeline), "mic_device_guard");
                if (guard == nullptr)
                {
                    gst_object_unref(source);
                    errorText = L"GStreamer microphone device guard is missing.";
                    return false;
                }
                if (!gst_bin_add(GST_BIN(pipeline), source))
                {
                    gst_object_unref(guard);
                    gst_object_unref(source);
                    errorText = L"GstDevice microphone source could not join the pipeline.";
                    return false;
                }
                if (!gst_element_link(source, guard))
                {
                    (void)gst_bin_remove(GST_BIN(pipeline), source);
                    gst_object_unref(guard);
                    errorText = L"GstDevice microphone source could not be linked.";
                    return false;
                }
                micDeviceGuard = guard;
                snapshot.micSourceCreatedFromDevice = true;
            }
            const auto bindOutput = [&](const char* const elementName,
                                        const wchar_t* const fileName,
                                        std::filesystem::path& snapshotPath)
            {
                const auto path = config.workingDirectory / fileName;
                if (!setString(elementName, "location", path.u8string()))
                    return false;
                snapshotPath = path;
                return true;
            };
            if ((config.mode == GStreamerAudioMode::SystemOnly ||
                    config.mode == GStreamerAudioMode::Dual) &&
                !bindOutput(
                    "system_sink", L"system.flac",
                    snapshot.systemWorkingPath))
            {
                errorText = L"GStreamer system FLAC filesink is missing.";
                return false;
            }
            if ((config.mode == GStreamerAudioMode::MicrophoneOnly ||
                    config.mode == GStreamerAudioMode::Dual) &&
                !bindOutput(
                    "mic_sink", L"mic.flac",
                    snapshot.microphoneWorkingPath))
            {
                errorText = L"GStreamer microphone FLAC filesink is missing.";
                return false;
            }
            snapshot.dualSourcesIndependent =
                config.mode == GStreamerAudioMode::Dual &&
                !snapshot.systemWorkingPath.empty() &&
                !snapshot.microphoneWorkingPath.empty();
            snapshot.audioWorkingPath =
                config.mode == GStreamerAudioMode::SystemOnly
                    ? snapshot.systemWorkingPath
                    : config.mode == GStreamerAudioMode::MicrophoneOnly
                        ? snapshot.microphoneWorkingPath
                        : std::filesystem::path{};
            pipelineBus = gst_element_get_bus(pipeline);
            if (pipelineBus == nullptr)
            {
                errorText = L"GStreamer pipeline bus is unavailable.";
                return false;
            }
            return true;
        }

        void HandlePipelineMessage(GstMessage* const message)
        {
            switch (GST_MESSAGE_TYPE(message))
            {
            case GST_MESSAGE_ERROR:
                SetFailure(E_FAIL, GStreamerErrorText(message, false));
                break;
            case GST_MESSAGE_EOS:
            {
                std::lock_guard lock(mutex);
                snapshot.endOfStreamObserved = true;
                if (snapshot.pipelineState !=
                    GStreamerAudioPipelineState::Failed)
                {
                    snapshot.pipelineState =
                        GStreamerAudioPipelineState::EndOfStream;
                }
                condition.notify_all();
                break;
            }
            default:
                break;
            }
        }

        void HandleDeviceEvent(
            const std::string& deviceId,
            const bool added)
        {
            if (added || micDeviceId.empty() || deviceId != micDeviceId)
                return;
            std::lock_guard lock(mutex);
            snapshot.micDisconnected = true;
            snapshot.micSourceDataBlocked = true;
            if (micDeviceGuard != nullptr)
            {
                g_object_set(
                    G_OBJECT(micDeviceGuard), "drop", TRUE, nullptr);
            }
        }

        void HandleMonitorMessage(GstMessage* const message)
        {
            const auto type = GST_MESSAGE_TYPE(message);
            if (type != GST_MESSAGE_DEVICE_ADDED &&
                type != GST_MESSAGE_DEVICE_REMOVED)
            {
                return;
            }
            GstDevice* device{};
            if (type == GST_MESSAGE_DEVICE_ADDED)
                gst_message_parse_device_added(message, &device);
            else
                gst_message_parse_device_removed(message, &device);
            if (device == nullptr) return;
            const auto facts = ReadDeviceFacts(device);
            HandleDeviceEvent(facts.id, type == GST_MESSAGE_DEVICE_ADDED);
            gst_object_unref(device);
        }

        void BusLoop() noexcept
        {
            try
            {
                while (!stopBus.load())
                {
                    if (pipelineBus)
                    {
                        if (auto* const message = gst_bus_timed_pop(
                                pipelineBus, 25 * GST_MSECOND))
                        {
                            HandlePipelineMessage(message);
                            gst_message_unref(message);
                        }
                    }
                    if (monitorBus)
                    {
                        if (auto* const message = gst_bus_timed_pop(
                                monitorBus, 25 * GST_MSECOND))
                        {
                            HandleMonitorMessage(message);
                            gst_message_unref(message);
                        }
                    }
                }
            }
            catch (...)
            {
                SetFailure(E_UNEXPECTED, L"GStreamer bus adapter failed.");
            }
            std::lock_guard lock(mutex);
            snapshot.busThreadExited = true;
            condition.notify_all();
        }

        void Release() noexcept
        {
            stopBus.store(true);
            if (busThread.joinable() &&
                busThread.get_id() != std::this_thread::get_id())
            {
                busThread.join();
            }
            if (pipeline)
            {
                (void)gst_element_set_state(pipeline, GST_STATE_NULL);
            }
            if (pipelineBus)
            {
                gst_object_unref(pipelineBus);
                pipelineBus = nullptr;
            }
            if (micDeviceGuard)
            {
                gst_object_unref(micDeviceGuard);
                micDeviceGuard = nullptr;
            }
            if (pipeline)
            {
                gst_object_unref(pipeline);
                pipeline = nullptr;
            }
            if (monitor)
            {
                gst_device_monitor_stop(monitor);
            }
            if (monitorBus)
            {
                gst_object_unref(monitorBus);
                monitorBus = nullptr;
            }
            if (monitor)
            {
                gst_object_unref(monitor);
                monitor = nullptr;
            }
            micDevice.reset();
            if (systemDevice)
            {
                gst_object_unref(systemDevice);
                systemDevice = nullptr;
            }
            std::lock_guard lock(mutex);
            snapshot.deviceMonitorActive = false;
        }
    };

    GStreamerAudioCore::GStreamerAudioCore()
        : impl_(std::make_unique<Impl>())
    {
    }

    GStreamerAudioCore::~GStreamerAudioCore()
    {
        (void)Stop();
    }

    HRESULT GStreamerAudioCore::Start(
        const GStreamerAudioConfig& config) noexcept
    {
        if (config.mode != GStreamerAudioMode::None &&
            !config.workingDirectory.is_absolute())
        {
            return E_INVALIDARG;
        }
        (void)Stop();
        try
        {
            impl_ = std::make_unique<Impl>();
        }
        catch (const std::bad_alloc&)
        {
            return E_OUTOFMEMORY;
        }
        catch (...)
        {
            return E_FAIL;
        }
        {
            std::lock_guard lock(impl_->mutex);
            impl_->snapshot.audioMode = config.mode;
            impl_->snapshot.systemActive =
                config.mode == GStreamerAudioMode::SystemOnly ||
                config.mode == GStreamerAudioMode::Dual;
            impl_->snapshot.micActive =
                config.mode == GStreamerAudioMode::MicrophoneOnly ||
                config.mode == GStreamerAudioMode::Dual;
            impl_->snapshot.pipelineState =
                GStreamerAudioPipelineState::Starting;
            impl_->snapshot.terminalHResult = S_OK;
        }
        if (config.mode == GStreamerAudioMode::None)
        {
            std::lock_guard lock(impl_->mutex);
            impl_->snapshot.pipelineState =
                GStreamerAudioPipelineState::Stopped;
            impl_->snapshot.filesClosed = true;
            impl_->snapshot.busThreadExited = true;
            return S_OK;
        }
        if (config.injectInitializationFailure)
        {
            impl_->SetFailure(E_FAIL, L"Injected GStreamer initialization failure.");
            return E_FAIL;
        }

        std::error_code pathError;
        if (!std::filesystem::is_directory(
                config.workingDirectory, pathError) || pathError)
        {
            impl_->SetFailure(E_INVALIDARG, L"Audio working directory is invalid.");
            return E_INVALIDARG;
        }
        const auto outputExists = [&](const wchar_t* const name)
        {
            pathError.clear();
            const auto exists = std::filesystem::exists(
                config.workingDirectory / name, pathError);
            return exists || static_cast<bool>(pathError);
        };
        const auto systemRequired =
            config.mode == GStreamerAudioMode::SystemOnly ||
            config.mode == GStreamerAudioMode::Dual;
        const auto micRequired =
            config.mode == GStreamerAudioMode::MicrophoneOnly ||
            config.mode == GStreamerAudioMode::Dual;
        if ((systemRequired && outputExists(L"system.flac")) ||
            (micRequired && outputExists(L"mic.flac")))
        {
            const auto failure = pathError
                ? HRESULT_FROM_WIN32(pathError.value())
                : HRESULT_FROM_WIN32(ERROR_FILE_EXISTS);
            impl_->SetFailure(failure, L"Audio working file already exists.");
            return failure;
        }

        std::wstring errorText;
        auto result = EnsureGStreamerAudioRuntime(errorText);
        if (FAILED(result))
        {
            impl_->SetFailure(result, std::move(errorText));
            return result;
        }
        if (!impl_->StartMonitor(errorText))
        {
            result = E_FAIL;
            impl_->SetFailure(result, std::move(errorText));
            impl_->Release();
            return result;
        }
        if (!impl_->ResolveDevices(config, errorText))
        {
            result = errorText == L"MicUnavailableAtStart"
                ? HRESULT_FROM_WIN32(ERROR_NOT_FOUND)
                : E_FAIL;
            impl_->SetFailure(result, std::move(errorText));
            impl_->Release();
            return result;
        }
        if (!impl_->BuildPipeline(config, errorText))
        {
            result = E_FAIL;
            impl_->SetFailure(result, std::move(errorText));
            impl_->Release();
            return result;
        }

        const auto transition = gst_element_set_state(
            impl_->pipeline, GST_STATE_PLAYING);
        if (transition == GST_STATE_CHANGE_FAILURE)
        {
            result = E_FAIL;
            impl_->SetFailure(result, L"GStreamer pipeline rejected PLAYING.");
            impl_->Release();
            return result;
        }
        GstState state{};
        GstState pending{};
        const auto wait = gst_element_get_state(
            impl_->pipeline, &state, &pending,
            GstTimeout(std::chrono::duration_cast<std::chrono::milliseconds>(
                PipelineStartTimeout)));
        if (wait == GST_STATE_CHANGE_FAILURE || state != GST_STATE_PLAYING)
        {
            result = HRESULT_FROM_WIN32(ERROR_NOT_READY);
            impl_->SetFailure(result, L"GStreamer pipeline did not reach PLAYING.");
            impl_->Release();
            return result;
        }
        {
            std::lock_guard lock(impl_->mutex);
            impl_->snapshot.pipelineState =
                GStreamerAudioPipelineState::Playing;
        }
        try
        {
            impl_->busThread = std::thread([owner = impl_.get()]
            {
                owner->BusLoop();
            });
        }
        catch (...)
        {
            result = E_FAIL;
            impl_->SetFailure(result, L"GStreamer bus thread could not start.");
            impl_->Release();
            return result;
        }
        return S_OK;
    }

    HRESULT GStreamerAudioCore::Pause() noexcept
    {
        if (!impl_) return E_UNEXPECTED;
        GstElement* pipeline{};
        {
            std::lock_guard lock(impl_->mutex);
            if (FAILED(impl_->snapshot.terminalHResult))
                return impl_->snapshot.terminalHResult;
            if (impl_->snapshot.pipelineState ==
                GStreamerAudioPipelineState::Paused)
            {
                return S_OK;
            }
            if (impl_->snapshot.pipelineState !=
                    GStreamerAudioPipelineState::Playing ||
                impl_->pipeline == nullptr)
            {
                return HRESULT_FROM_WIN32(ERROR_INVALID_STATE);
            }
            pipeline = GST_ELEMENT(gst_object_ref(impl_->pipeline));
        }

        const auto transition = gst_element_set_state(
            pipeline, GST_STATE_PAUSED);
        GstState state{};
        GstState pending{};
        const auto wait = transition == GST_STATE_CHANGE_FAILURE
            ? GST_STATE_CHANGE_FAILURE
            : gst_element_get_state(
                pipeline, &state, &pending,
                GstTimeout(std::chrono::duration_cast<
                    std::chrono::milliseconds>(PipelineStartTimeout)));
        gst_object_unref(pipeline);
        if (wait == GST_STATE_CHANGE_FAILURE || state != GST_STATE_PAUSED)
        {
            const auto failure = HRESULT_FROM_WIN32(ERROR_NOT_READY);
            impl_->SetFailure(
                failure, L"GStreamer pipeline did not reach PAUSED.");
            return failure;
        }

        std::lock_guard lock(impl_->mutex);
        if (FAILED(impl_->snapshot.terminalHResult))
            return impl_->snapshot.terminalHResult;
        impl_->snapshot.pipelineState =
            GStreamerAudioPipelineState::Paused;
        return S_OK;
    }

    HRESULT GStreamerAudioCore::Resume() noexcept
    {
        if (!impl_) return E_UNEXPECTED;
        GstElement* pipeline{};
        {
            std::lock_guard lock(impl_->mutex);
            if (FAILED(impl_->snapshot.terminalHResult))
                return impl_->snapshot.terminalHResult;
            if (impl_->snapshot.pipelineState ==
                GStreamerAudioPipelineState::Playing)
            {
                return S_OK;
            }
            if (impl_->snapshot.pipelineState !=
                    GStreamerAudioPipelineState::Paused ||
                impl_->pipeline == nullptr)
            {
                return HRESULT_FROM_WIN32(ERROR_INVALID_STATE);
            }
            pipeline = GST_ELEMENT(gst_object_ref(impl_->pipeline));
        }

        const auto transition = gst_element_set_state(
            pipeline, GST_STATE_PLAYING);
        GstState state{};
        GstState pending{};
        const auto wait = transition == GST_STATE_CHANGE_FAILURE
            ? GST_STATE_CHANGE_FAILURE
            : gst_element_get_state(
                pipeline, &state, &pending,
                GstTimeout(std::chrono::duration_cast<
                    std::chrono::milliseconds>(PipelineStartTimeout)));
        gst_object_unref(pipeline);
        if (wait == GST_STATE_CHANGE_FAILURE || state != GST_STATE_PLAYING)
        {
            const auto failure = HRESULT_FROM_WIN32(ERROR_NOT_READY);
            impl_->SetFailure(
                failure, L"GStreamer pipeline did not resume PLAYING.");
            return failure;
        }

        std::lock_guard lock(impl_->mutex);
        if (FAILED(impl_->snapshot.terminalHResult))
            return impl_->snapshot.terminalHResult;
        impl_->snapshot.pipelineState =
            GStreamerAudioPipelineState::Playing;
        return S_OK;
    }

    HRESULT GStreamerAudioCore::Stop() noexcept
    {
        if (!impl_) return S_OK;
        GstElement* pipeline{};
        {
            std::lock_guard lock(impl_->mutex);
            pipeline = impl_->pipeline;
            if (pipeline == nullptr)
            {
                return impl_->snapshot.terminalHResult;
            }
        }

        bool paused{};
        {
            std::lock_guard lock(impl_->mutex);
            paused = impl_->snapshot.pipelineState ==
                GStreamerAudioPipelineState::Paused;
        }
        if (!gst_element_send_event(pipeline, gst_event_new_eos()))
        {
            impl_->SetFailure(E_FAIL, L"GStreamer EOS event was rejected.");
        }
        if (paused && gst_element_set_state(
                pipeline, GST_STATE_PLAYING) == GST_STATE_CHANGE_FAILURE)
        {
            impl_->SetFailure(
                E_FAIL, L"GStreamer paused pipeline could not drain EOS.");
        }
        {
            std::unique_lock lock(impl_->mutex);
            (void)impl_->condition.wait_for(
                lock, PipelineEosTimeout,
                [&]
                {
                    return impl_->snapshot.endOfStreamObserved ||
                        FAILED(impl_->snapshot.terminalHResult);
                });
            if (!impl_->snapshot.endOfStreamObserved &&
                SUCCEEDED(impl_->snapshot.terminalHResult))
            {
                impl_->snapshot.terminalHResult =
                    HRESULT_FROM_WIN32(WAIT_TIMEOUT);
                impl_->snapshot.lastGStreamerError =
                    L"GStreamer EOS timed out.";
                impl_->snapshot.pipelineState =
                    GStreamerAudioPipelineState::Failed;
            }
        }

        (void)gst_element_set_state(pipeline, GST_STATE_NULL);
        GstState state{};
        GstState pending{};
        (void)gst_element_get_state(
            pipeline, &state, &pending, 5 * GST_SECOND);
        impl_->Release();

        std::lock_guard lock(impl_->mutex);
        const auto outputValid = [](const std::filesystem::path& path)
        {
            if (path.empty()) return true;
            std::error_code error;
            const auto bytes = std::filesystem::file_size(path, error);
            return !error && bytes > 0;
        };
        impl_->snapshot.filesClosed =
            outputValid(impl_->snapshot.systemWorkingPath) &&
            outputValid(impl_->snapshot.microphoneWorkingPath) &&
            (!impl_->snapshot.systemWorkingPath.empty() ||
                !impl_->snapshot.microphoneWorkingPath.empty());
        if (!impl_->snapshot.filesClosed &&
            SUCCEEDED(impl_->snapshot.terminalHResult))
        {
            impl_->snapshot.terminalHResult =
                HRESULT_FROM_WIN32(ERROR_FILE_INVALID);
            impl_->snapshot.lastGStreamerError =
                L"GStreamer FLAC output is missing or empty.";
        }
        if (SUCCEEDED(impl_->snapshot.terminalHResult))
        {
            impl_->snapshot.pipelineState =
                GStreamerAudioPipelineState::Stopped;
        }
        else
        {
            impl_->snapshot.pipelineState =
                GStreamerAudioPipelineState::Failed;
        }
        return impl_->snapshot.terminalHResult;
    }

    GStreamerAudioSnapshot GStreamerAudioCore::Snapshot() const noexcept
    {
        if (!impl_) return {};
        std::lock_guard lock(impl_->mutex);
        return impl_->snapshot;
    }

#if defined(XBPREVIEW_GSTREAMER_AUDIO_TESTS)
    void GStreamerAudioCore::TestLockMicrophoneDeviceId(
        const std::string& deviceId)
    {
        std::lock_guard lock(impl_->mutex);
        impl_->micDeviceId = deviceId;
        impl_->snapshot.audioMode = GStreamerAudioMode::MicrophoneOnly;
        impl_->snapshot.micActive = true;
        impl_->snapshot.micDeviceId = Utf8ToWide(deviceId.c_str());
        impl_->snapshot.micSessionBound = true;
        impl_->snapshot.micSourceCreatedFromDevice = true;
    }

    void GStreamerAudioCore::TestInjectDeviceEvent(
        const std::string& deviceId,
        const bool added)
    {
        impl_->HandleDeviceEvent(deviceId, added);
    }

    void GStreamerAudioCore::TestInjectLockedMicrophoneRemoval()
    {
        impl_->HandleDeviceEvent(impl_->micDeviceId, false);
    }
#endif
}
