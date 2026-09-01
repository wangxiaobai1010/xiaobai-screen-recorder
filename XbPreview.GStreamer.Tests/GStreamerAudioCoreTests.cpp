#include "GStreamerAudioCore.h"
#include "GStreamerAudioFinalizer.h"
#include "GStreamerMicrophoneDeviceMonitor.h"
#include "MicPreflightLevelMonitor.h"

#include <gst/gst.h>
#include <windows.h>

#include <chrono>
#include <cmath>
#include <filesystem>
#include <fstream>
#include <iomanip>
#include <initializer_list>
#include <iostream>
#include <limits>
#include <mutex>
#include <sstream>
#include <stdexcept>
#include <string>
#include <string_view>
#include <thread>
#include <vector>

namespace xbpreview
{
    double TestMicPreflightMaximumDb(
        const GstStructure* const structure,
        const char* const field) noexcept;
}

namespace
{
    void Require(const bool condition, const char* const message)
    {
        if (!condition) throw std::runtime_error(message);
    }

    bool Contains(
        const std::string_view text,
        const std::string_view value) noexcept
    {
        return text.find(value) != std::string_view::npos;
    }

    bool Contains(
        const std::wstring_view text,
        const std::wstring_view value) noexcept
    {
        return text.find(value) != std::wstring_view::npos;
    }

    std::size_t Count(
        const std::string_view text,
        const std::string_view value) noexcept
    {
        std::size_t count{};
        auto position = text.find(value);
        while (position != std::string_view::npos)
        {
            ++count;
            position = text.find(value, position + value.size());
        }
        return count;
    }

    std::size_t Count(
        const std::wstring_view text,
        const std::wstring_view value) noexcept
    {
        std::size_t count{};
        auto position = text.find(value);
        while (position != std::wstring_view::npos)
        {
            ++count;
            position = text.find(value, position + value.size());
        }
        return count;
    }

    xbpreview::GStreamerAudioMode ParseMode(const std::wstring_view value)
    {
        if (value == L"system")
            return xbpreview::GStreamerAudioMode::SystemOnly;
        if (value == L"microphone" || value == L"mic")
            return xbpreview::GStreamerAudioMode::MicrophoneOnly;
        if (value == L"dual") return xbpreview::GStreamerAudioMode::Dual;
        throw std::invalid_argument("mode must be system, microphone, or dual");
    }

    std::shared_ptr<xbpreview::GStreamerMicrophoneDeviceBinding>
        LockDefaultMicrophone()
    {
        xbpreview::GStreamerMicrophoneDeviceMonitor monitor;
        Require(SUCCEEDED(monitor.Start()),
            "GstDeviceMonitor did not start for microphone selection");
        const auto snapshot = monitor.Snapshot();
        Require(snapshot.monitorActive && snapshot.defaultAvailable &&
            !snapshot.devices.empty(),
            "Windows default did not resolve to a concrete GstDevice");
        auto binding = monitor.LockDefault();
        Require(binding != nullptr && !binding->EndpointId().empty(),
            "default concrete GstDevice binding is unavailable");
        monitor.Stop();
        return binding;
    }

    HRESULT StartAudio(
        xbpreview::GStreamerAudioCore& core,
        const xbpreview::GStreamerAudioMode mode,
        const std::filesystem::path& directory,
        const bool injectInitializationFailure = false,
        const bool simulateMissingMicrophone = false,
        std::shared_ptr<xbpreview::GStreamerMicrophoneDeviceBinding>
            microphone = nullptr)
    {
        const auto wantsMicrophone =
            mode == xbpreview::GStreamerAudioMode::MicrophoneOnly ||
            mode == xbpreview::GStreamerAudioMode::Dual;
        if (wantsMicrophone && !simulateMissingMicrophone &&
            microphone == nullptr)
        {
            microphone = LockDefaultMicrophone();
        }
        xbpreview::GStreamerAudioConfig config{};
        config.mode = mode;
        config.workingDirectory = directory;
        config.injectInitializationFailure = injectInitializationFailure;
        config.simulateMissingMicrophone = simulateMissingMicrophone;
        config.microphoneDevice = std::move(microphone);
        return core.Start(config);
    }

    HRESULT StartWithUnavailableSelectedEndpoint(
        xbpreview::GStreamerAudioCore& core,
        const std::filesystem::path& directory,
        std::shared_ptr<xbpreview::GStreamerMicrophoneDeviceBinding> microphone)
    {
        xbpreview::GStreamerAudioConfig config{};
        config.mode = xbpreview::GStreamerAudioMode::MicrophoneOnly;
        config.workingDirectory = directory;
        config.simulateMissingMicrophoneEndpointId =
            microphone->EndpointId();
        config.microphoneDevice = std::move(microphone);
        return core.Start(config);
    }

    std::wstring Utf8ToWideForTest(const char* const value)
    {
        if (value == nullptr || *value == '\0') return {};
        const auto size = MultiByteToWideChar(
            CP_UTF8, MB_ERR_INVALID_CHARS, value, -1, nullptr, 0);
        Require(size > 1, "UTF-8 device identity is invalid");
        std::wstring result(static_cast<std::size_t>(size), L'\0');
        Require(MultiByteToWideChar(
                CP_UTF8, MB_ERR_INVALID_CHARS, value, -1,
                result.data(), size) != 0,
            "UTF-8 device identity conversion failed");
        result.resize(static_cast<std::size_t>(size - 1));
        return result;
    }

    std::string WideToUtf8ForTest(const std::wstring_view value)
    {
        if (value.empty()) return {};
        const auto size = WideCharToMultiByte(
            CP_UTF8, WC_ERR_INVALID_CHARS,
            value.data(), static_cast<int>(value.size()),
            nullptr, 0, nullptr, nullptr);
        Require(size > 0, "wide device identity is invalid");
        std::string result(static_cast<std::size_t>(size), '\0');
        Require(WideCharToMultiByte(
                CP_UTF8, WC_ERR_INVALID_CHARS,
                value.data(), static_cast<int>(value.size()),
                result.data(), size, nullptr, nullptr) != 0,
            "wide device identity conversion failed");
        return result;
    }

    struct TestDeviceProperties
    {
        bool parsed{};
        bool defaultPresent{};
        bool isDefault{};
        std::wstring actualId;
    };

    TestDeviceProperties ReadTestDeviceProperties(
        const xbpreview::GStreamerMicrophoneDeviceBinding& binding)
    {
        TestDeviceProperties result{};
        const auto serialized = WideToUtf8ForTest(binding.Properties());
        if (serialized.empty()) return result;
        gchar* end{};
        auto* const properties =
            gst_structure_from_string(serialized.c_str(), &end);
        if (properties == nullptr) return result;
        result.parsed = true;
        if (const auto* const actualId =
                gst_structure_get_string(properties, "device.actual-id"))
        {
            result.actualId = Utf8ToWideForTest(actualId);
        }
        gboolean isDefault{};
        if (gst_structure_get_boolean(
                properties, "device.default", &isDefault))
        {
            result.defaultPresent = true;
            result.isDefault = isDefault != FALSE;
        }
        gst_structure_free(properties);
        return result;
    }

    std::wstring JoinArguments(const std::vector<std::wstring>& arguments)
    {
        std::wstring joined;
        for (const auto& argument : arguments)
        {
            joined.append(argument);
            joined.push_back(L'\n');
        }
        return joined;
    }

    GstStructure* LevelStructureWithList(
        const std::initializer_list<double> values)
    {
        auto* const structure = gst_structure_new_empty("level");
        Require(structure != nullptr,
            "could not allocate GstValueList level structure");
        GValue list = G_VALUE_INIT;
        g_value_init(&list, GST_TYPE_LIST);
        for (const auto value : values)
        {
            GValue channel = G_VALUE_INIT;
            g_value_init(&channel, G_TYPE_DOUBLE);
            g_value_set_double(&channel, value);
            gst_value_list_append_value(&list, &channel);
            g_value_unset(&channel);
        }
        gst_structure_take_value(structure, "peak", &list);
        return structure;
    }

    GstStructure* LevelStructureWithLegacyArray(
        const std::initializer_list<double> values)
    {
        auto* const structure = gst_structure_new_empty("level");
        Require(structure != nullptr,
            "could not allocate GValueArray level structure");
#pragma warning(push)
#pragma warning(disable: 4996) // TEST-ONLY: construct runtime level value type.
        auto* const array = g_value_array_new(
            static_cast<guint>(values.size()));
        Require(array != nullptr, "could not allocate GValueArray");
        for (const auto value : values)
        {
            GValue channel = G_VALUE_INIT;
            g_value_init(&channel, G_TYPE_DOUBLE);
            g_value_set_double(&channel, value);
            (void)g_value_array_append(array, &channel);
            g_value_unset(&channel);
        }
        GValue container = G_VALUE_INIT;
        g_value_init(&container, G_TYPE_VALUE_ARRAY);
#pragma warning(pop)
        g_value_take_boxed(&container, array);
        gst_structure_take_value(structure, "peak", &container);
        return structure;
    }

    void TestMicPreflightLevelParser()
    {
        constexpr double Tolerance = 0.000001;
        constexpr double SilenceDbForTest = -120.0;
        std::wstring runtimeError;
        Require(SUCCEEDED(
                xbpreview::EnsureGStreamerAudioRuntime(runtimeError)),
            "GStreamer runtime unavailable for parser fixtures");

        auto* const list = LevelStructureWithList({ -48.0, -41.2 });
        const auto listMaximum =
            xbpreview::TestMicPreflightMaximumDb(list, "peak");
        gst_structure_free(list);
        Require(std::abs(listMaximum - (-41.2)) <= Tolerance,
            "GstValueList MaximumDb behavior regressed");

        auto* const array =
            LevelStructureWithLegacyArray({ -48.0, -41.2 });
        const auto arrayMaximum =
            xbpreview::TestMicPreflightMaximumDb(array, "peak");
        gst_structure_free(array);
        Require(std::abs(arrayMaximum - (-41.2)) <= Tolerance,
            "GValueArray MaximumDb did not select the loudest channel");

        auto* const silence =
            LevelStructureWithLegacyArray({ -120.0, -120.0 });
        const auto silenceMaximum =
            xbpreview::TestMicPreflightMaximumDb(silence, "peak");
        gst_structure_free(silence);
        Require(silenceMaximum == SilenceDbForTest,
            "GValueArray silence behavior changed");

        auto* const malformed = gst_structure_new(
            "level", "peak", G_TYPE_STRING, "unsupported", nullptr);
        Require(malformed != nullptr,
            "could not allocate malformed parser fixture");
        const auto malformedMaximum =
            xbpreview::TestMicPreflightMaximumDb(malformed, "peak");
        const auto missingMaximum =
            xbpreview::TestMicPreflightMaximumDb(malformed, "rms");
        gst_structure_free(malformed);
        Require(malformedMaximum == SilenceDbForTest &&
            missingMaximum == SilenceDbForTest,
            "malformed or missing level values did not fail safe");

        std::cout
            << "MIC_PREFLIGHT_PARSER GstValueList=-41.2 PASS\n"
            << "MIC_PREFLIGHT_PARSER GValueArray=-41.2 PASS\n"
            << "MIC_PREFLIGHT_PARSER GValueArraySilence=-120 PASS\n"
            << "MIC_PREFLIGHT_PARSER MalformedOrMissing=-120 PASS\n"
            << "MIC_PREFLIGHT_LEVEL_PARSER_DETERMINISTIC_PASS\n";
    }

    void TestStaticContract()
    {
        using xbpreview::GStreamerAudioMode;
        Require(xbpreview::GStreamerAudioVersionMajor == 1 &&
            xbpreview::GStreamerAudioVersionMinor == 28 &&
            xbpreview::GStreamerAudioVersionMicro == 6,
            "GStreamer version pin is exactly 1.28.6");
        Require(xbpreview::GStreamerAudioPipelineDescription(
            GStreamerAudioMode::None) == nullptr,
            "None creates no audio pipeline");

        const std::string_view system{
            xbpreview::GStreamerAudioPipelineDescription(
                GStreamerAudioMode::SystemOnly) };
        const std::string_view microphone{
            xbpreview::GStreamerAudioPipelineDescription(
                GStreamerAudioMode::MicrophoneOnly) };
        const std::string_view dual{
            xbpreview::GStreamerAudioPipelineDescription(
                GStreamerAudioMode::Dual) };

        Require(Contains(system, "wasapi2src") &&
            Contains(system, "loopback=true") &&
            Contains(system, "continue-on-error=true") &&
            Contains(system, "queue") && Contains(system, "audioconvert") &&
            Contains(system, "audioresample") &&
            Contains(system, "rate=48000") && Contains(system, "flacenc") &&
            Contains(system, "filesink name=system_sink") &&
            !Contains(system, "webrtcdsp") && !Contains(system, "audiomixer"),
            "SystemOnly pipeline has the frozen GStreamer route");
        Require(!Contains(microphone, "wasapi2src") &&
            Contains(microphone, "mic_device_guard") &&
            Contains(microphone, "drop-mode=transform-to-gap") &&
            Contains(microphone, "webrtcdsp") &&
            Contains(microphone, "noise-suppression=true") &&
            Contains(microphone, "noise-suppression-level=moderate") &&
            Contains(microphone, "high-pass-filter=true") &&
            !Contains(microphone, "gain-control=") &&
            !Contains(microphone, "gain-control-mode=") &&
            !Contains(microphone, "compression-gain-db=") &&
            !Contains(microphone, "target-level-dbfs=") &&
            !Contains(microphone, "limiter=") &&
            Contains(microphone, "echo-cancel=false") &&
            Contains(microphone, "rate=48000") &&
            Contains(microphone, "filesink name=mic_sink") &&
            !Contains(microphone, "audiomixer"),
            "MicrophoneOnly pipeline has the frozen WebRTC DSP route");
        Require(!Contains(dual, "audiomixer") &&
            Contains(dual, "loopback=true") &&
            Count(dual, "wasapi2src") == 1 &&
            Contains(dual, "mic_device_guard") &&
            Contains(dual, "drop-mode=transform-to-gap") &&
            Contains(dual, "webrtcdsp") && Contains(dual, "rate=48000") &&
            Count(dual, "flacenc") == 2 &&
            Contains(dual, "filesink name=system_sink") &&
            Contains(dual, "filesink name=mic_sink") &&
            !Contains(dual, "gain-control=") &&
            !Contains(dual, "gain-control-mode=") &&
            !Contains(dual, "compression-gain-db=") &&
            !Contains(dual, "target-level-dbfs=") &&
            !Contains(dual, "limiter="),
            "Dual pipeline preserves independent system and microphone FLACs");
        for (const auto route : { system, microphone, dual })
        {
            Require(!Contains(route, "agate") && !Contains(route, "amix") &&
                !Contains(route, "loudnorm") &&
                !Contains(route, "compressor") &&
                !Contains(route, "noisegate"),
                "GStreamer routes contain no legacy FFmpeg/custom DSP chain");
        }

        const std::string_view preflight{
            xbpreview::MicPreflightPipelineDescription() };
        Require(Contains(preflight, "product-selected GstDevice source") &&
            Contains(preflight, "level interval=75000000") &&
            Contains(preflight, "fakesink") &&
            !Contains(preflight, "flacenc") &&
            !Contains(preflight, "filesink") &&
            !Contains(preflight, "webrtcdsp") &&
            !Contains(preflight, "audiomixer") &&
            !Contains(preflight, "loudnorm"),
            "preflight is the thin source-level-fakesink route");
    }

    xbpreview::MicPreflightLevelSnapshot WaitForPreflight(
        const xbpreview::MicPreflightLevelMonitor& preflight,
        const bool requireLevelMessage,
        const std::chrono::seconds timeout = std::chrono::seconds(10))
    {
        const auto deadline = std::chrono::steady_clock::now() + timeout;
        auto snapshot = preflight.Snapshot();
        while (std::chrono::steady_clock::now() < deadline)
        {
            snapshot = preflight.Snapshot();
            if (snapshot.pipelineState ==
                xbpreview::MicPreflightPipelineState::Failed)
            {
                throw std::runtime_error(
                    "microphone preflight entered Failed state");
            }
            if (snapshot.running && snapshot.available &&
                (!requireLevelMessage || snapshot.levelMessageCount > 0))
            {
                return snapshot;
            }
            std::this_thread::sleep_for(std::chrono::milliseconds(25));
        }
        throw std::runtime_error(
            requireLevelMessage
                ? "microphone preflight produced no level messages"
                : "microphone preflight did not reach Playing");
    }

    void RunMicPreflightGates(const std::filesystem::path& root)
    {
        std::filesystem::create_directories(root);
        xbpreview::GStreamerMicrophoneDeviceMonitor catalog;
        Require(SUCCEEDED(catalog.Start()),
            "product microphone device monitor did not start");
        const auto devices = catalog.Snapshot();
        Require(devices.monitorActive && devices.defaultAvailable &&
            !devices.devices.empty(),
            "product microphone catalog has no concrete default binding");
        auto first = catalog.LockDefault();
        Require(first != nullptr,
            "product default microphone binding is unavailable");

        std::shared_ptr<xbpreview::GStreamerMicrophoneDeviceBinding> second;
        for (const auto& candidate : devices.devices)
        {
            if (candidate.endpointId != first->EndpointId())
            {
                second = catalog.LockEndpoint(candidate.endpointId);
                if (second != nullptr) break;
            }
        }

        xbpreview::MicPreflightLevelMonitor preflight;
        Require(SUCCEEDED(preflight.Start(first, first->EndpointId())),
            "idle preflight worker could not start");
        const auto firstRunning = WaitForPreflight(preflight, true);
        Require(firstRunning.sourceCreatedFromDevice &&
            firstRunning.elementIdentityMatches &&
            firstRunning.selectedEndpointId == first->EndpointId() &&
            firstRunning.elementEndpointId == first->EndpointId(),
            "idle preflight did not use the selected product GstDevice identity");
        std::cout << "GATE 1 idle selected-mic source + level messages PASS: messages="
            << firstRunning.levelMessageCount
            << ", peakPcm16=" << firstRunning.peakAbsolutePcm16
            << ", rmsPcm16=" << firstRunning.rmsPcm16 << '\n';

        preflight.Stop();
        const auto firstStopped = preflight.Snapshot();
        Require(firstStopped.resourcesReleased && !firstStopped.running &&
            !firstStopped.available &&
            firstStopped.peakAbsolutePcm16 == 0 &&
            firstStopped.rmsPcm16 == 0.0,
            "device switch did not synchronously release microphone A");
        if (second != nullptr)
        {
            Require(SUCCEEDED(preflight.Start(second, second->EndpointId())),
                "device B preflight worker could not start");
            const auto secondRunning = WaitForPreflight(preflight, true);
            Require(secondRunning.elementIdentityMatches &&
                secondRunning.selectedEndpointId == second->EndpointId() &&
                secondRunning.elementEndpointId == second->EndpointId() &&
                secondRunning.selectedEndpointId !=
                    firstRunning.selectedEndpointId,
                "device A-to-B switch did not rebind exact product identity");
            std::cout << "GATE 2 device A->B stop/release/rebind PASS\n";
        }
        else
        {
            Require(SUCCEEDED(preflight.Start(first, first->EndpointId())),
                "single-device rebind worker could not start");
            const auto rebound = WaitForPreflight(preflight, true);
            Require(rebound.elementIdentityMatches &&
                rebound.completedReleaseCount >= 1,
                "single-device stop/release/rebind contract failed");
            std::cout << "GATE 2 stop/release/rebind PASS (single-device catalog; distinct B not available)\n";
            second = first;
        }

        preflight.Stop();
        const auto handoff = preflight.Snapshot();
        Require(handoff.resourcesReleased && !handoff.running &&
            !handoff.available,
            "preflight was not fully released before formal recording start");

        const auto formalDirectory = root / L"formal-handoff";
        std::filesystem::create_directories(formalDirectory);
        xbpreview::GStreamerAudioCore formal;
        xbpreview::GStreamerAudioConfig config{};
        config.mode = xbpreview::GStreamerAudioMode::MicrophoneOnly;
        config.workingDirectory = std::filesystem::absolute(formalDirectory);
        config.microphoneDevice = second;
        Require(SUCCEEDED(formal.Start(config)),
            "formal microphone graph could not start after preflight release");
        const auto formalRunning = formal.Snapshot();
        Require(formalRunning.micActive &&
            formalRunning.micSourceCreatedFromDevice &&
            formalRunning.micElementIdentityMatches &&
            formalRunning.micDeviceId == second->EndpointId(),
            "formal graph did not retain the preflight-selected product identity");
        Require(!preflight.Snapshot().running,
            "preflight and formal microphone graph overlapped");
        std::cout << "GATE 3 preflight Stop/Join/Dispose before formal mic start PASS\n";

        std::this_thread::sleep_for(std::chrono::milliseconds(400));
        Require(SUCCEEDED(formal.Stop()),
            "formal microphone graph did not stop cleanly");
        const auto formalStopped = formal.Snapshot();
        Require(formalStopped.filesClosed && formalStopped.busThreadExited &&
            formalStopped.pipelineState ==
                xbpreview::GStreamerAudioPipelineState::Stopped,
            "formal microphone graph resources were not fully released");
        Require(SUCCEEDED(preflight.Start(second, second->EndpointId())),
            "preflight could not restart after formal graph release");
        const auto returned = WaitForPreflight(preflight, true);
        Require(returned.running && returned.available &&
            returned.elementIdentityMatches &&
            formalStopped.pipelineState ==
                xbpreview::GStreamerAudioPipelineState::Stopped,
            "preflight restarted before formal capture release");
        std::cout << "GATE 4 formal stop/release before idle preflight restart PASS\n";
        std::cout << "MAX_SIMULTANEOUS_MIC_CAPTURE_OWNERS=1\n";

        preflight.Stop();
        catalog.Stop();
        std::error_code cleanupError;
        std::filesystem::remove(formalDirectory / L"mic.flac", cleanupError);
        cleanupError.clear();
        std::filesystem::remove(formalDirectory, cleanupError);
    }

    bool ContainsOrdinalIgnoreCase(
        const std::wstring_view text,
        const std::wstring_view value) noexcept
    {
        if (value.empty()) return true;
        if (value.size() > text.size()) return false;
        for (std::size_t index = 0;
             index + value.size() <= text.size();
             ++index)
        {
            if (CompareStringOrdinal(
                    text.data() + index,
                    static_cast<int>(value.size()),
                    value.data(),
                    static_cast<int>(value.size()),
                    TRUE) == CSTR_EQUAL)
            {
                return true;
            }
        }
        return false;
    }

    struct RawBufferTimingSample
    {
        GstClockTime pts{ GST_CLOCK_TIME_NONE };
        GstClockTime duration{ GST_CLOCK_TIME_NONE };
    };

    struct RawBufferProbeFacts
    {
        std::mutex mutex;
        std::uint64_t bufferCount{};
        std::uint64_t totalBytes{};
        std::uint32_t mappedBufferCount{};
        bool anyMappedByteNonzero{};
        std::string caps;
        std::vector<RawBufferTimingSample> timings;
    };

    GstPadProbeReturn ProbeRawSourceBuffer(
        GstPad* const pad,
        GstPadProbeInfo* const info,
        gpointer const userData)
    {
        if ((GST_PAD_PROBE_INFO_TYPE(info) & GST_PAD_PROBE_TYPE_BUFFER) == 0)
            return GST_PAD_PROBE_OK;
        auto* const buffer = GST_PAD_PROBE_INFO_BUFFER(info);
        if (buffer == nullptr) return GST_PAD_PROBE_OK;
        auto& facts = *static_cast<RawBufferProbeFacts*>(userData);
        std::lock_guard lock(facts.mutex);
        ++facts.bufferCount;
        facts.totalBytes += gst_buffer_get_size(buffer);
        if (facts.caps.empty())
        {
            auto* const caps = gst_pad_get_current_caps(pad);
            if (caps != nullptr)
            {
                if (auto* const text = gst_caps_to_string(caps))
                {
                    facts.caps = text;
                    g_free(text);
                }
                gst_caps_unref(caps);
            }
        }
        if (facts.timings.size() < 5)
        {
            facts.timings.push_back({
                GST_BUFFER_PTS(buffer), GST_BUFFER_DURATION(buffer) });
        }
        constexpr std::uint32_t MaximumMappedBuffers = 8;
        if (facts.mappedBufferCount < MaximumMappedBuffers)
        {
            GstMapInfo map{};
            if (gst_buffer_map(buffer, &map, GST_MAP_READ))
            {
                ++facts.mappedBufferCount;
                facts.anyMappedByteNonzero =
                    facts.anyMappedByteNonzero ||
                    std::any_of(
                        map.data, map.data + map.size,
                        [](const guint8 value) { return value != 0; });
                gst_buffer_unmap(buffer, &map);
            }
        }
        return GST_PAD_PROBE_OK;
    }

#pragma warning(push)
#pragma warning(disable: 4996) // TEST-ONLY: inspect legacy GValueArray messages.
    guint DiagnosticValueCount(const GValue* const value) noexcept
    {
        if (value == nullptr) return 0;
        if (GST_VALUE_HOLDS_LIST(value))
            return gst_value_list_get_size(value);
        if (GST_VALUE_HOLDS_ARRAY(value))
            return gst_value_array_get_size(value);
        if (G_VALUE_HOLDS(value, G_TYPE_VALUE_ARRAY))
        {
            const auto* const array = static_cast<const GValueArray*>(
                g_value_get_boxed(value));
            return array != nullptr ? array->n_values : 0;
        }
        return 0;
    }

    const GValue* DiagnosticValueAt(
        const GValue* const value,
        const guint index) noexcept
    {
        if (value == nullptr) return nullptr;
        if (GST_VALUE_HOLDS_LIST(value))
            return gst_value_list_get_value(value, index);
        if (GST_VALUE_HOLDS_ARRAY(value))
            return gst_value_array_get_value(value, index);
        if (G_VALUE_HOLDS(value, G_TYPE_VALUE_ARRAY))
        {
            auto* const array = static_cast<GValueArray*>(
                g_value_get_boxed(value));
            return array != nullptr
                ? g_value_array_get_nth(array, index)
                : nullptr;
        }
        return nullptr;
    }
#pragma warning(pop)

    double DiagnosticMaximumDb(
        const GstStructure* const structure,
        const char* const field) noexcept
    {
        const auto* const values = gst_structure_get_value(structure, field);
        auto result = -std::numeric_limits<double>::infinity();
        const auto count = DiagnosticValueCount(values);
        for (guint index = 0; index < count; ++index)
        {
            const auto* const value = DiagnosticValueAt(values, index);
            if (value != nullptr && G_VALUE_HOLDS_DOUBLE(value))
                result = (std::max)(result, g_value_get_double(value));
        }
        return result;
    }

    double ListOnlyMaximumDb(
        const GstStructure* const structure,
        const char* const field) noexcept
    {
        constexpr double SilenceDb = -120.0;
        const auto* const list = gst_structure_get_value(structure, field);
        if (list == nullptr || !GST_VALUE_HOLDS_LIST(list))
            return SilenceDb;
        auto result = -std::numeric_limits<double>::infinity();
        const auto channels = gst_value_list_get_size(list);
        for (guint index = 0; index < channels; ++index)
        {
            const auto* const value = gst_value_list_get_value(list, index);
            if (value != nullptr && G_VALUE_HOLDS_DOUBLE(value))
                result = (std::max)(result, g_value_get_double(value));
        }
        return std::isfinite(result) ? result : SilenceDb;
    }

    std::string DiagnosticSerializedValue(
        const GstStructure* const structure,
        const char* const field)
    {
        const auto* const value = gst_structure_get_value(structure, field);
        if (value == nullptr) return "<absent>";
        auto* const serialized = gst_value_serialize(value);
        if (serialized == nullptr) return "<unserializable>";
        std::string result(serialized);
        g_free(serialized);
        return result;
    }

    std::string DiagnosticValueType(
        const GstStructure* const structure,
        const char* const field)
    {
        const auto* const value = gst_structure_get_value(structure, field);
        return value != nullptr ? G_VALUE_TYPE_NAME(value) : "<absent>";
    }

    std::string DiagnosticElementProperties(GstElement* const element)
    {
        std::ostringstream output;
        guint count{};
        auto** const properties = g_object_class_list_properties(
            G_OBJECT_GET_CLASS(element), &count);
        bool first = true;
        for (guint index = 0; index < count; ++index)
        {
            auto* const property = properties[index];
            if ((property->flags & G_PARAM_READABLE) == 0) continue;
            GValue value = G_VALUE_INIT;
            g_value_init(&value, G_PARAM_SPEC_VALUE_TYPE(property));
            g_object_get_property(G_OBJECT(element), property->name, &value);
            auto* const text = g_strdup_value_contents(&value);
            if (!first) output << "; ";
            output << property->name << '='
                << (text != nullptr ? text : "<unprintable>");
            first = false;
            if (text != nullptr) g_free(text);
            g_value_unset(&value);
        }
        g_free(properties);
        return output.str();
    }

    void RunMicPreflightRawDiagnostic(
        const std::chrono::seconds duration,
        const std::wstring_view selector)
    {
        xbpreview::GStreamerMicrophoneDeviceMonitor catalog;
        Require(SUCCEEDED(catalog.Start()),
            "raw diagnostic microphone catalog did not start");
        const auto devices = catalog.Snapshot();
        std::vector<xbpreview::GStreamerMicrophoneDeviceInfo> matches;
        for (const auto& device : devices.devices)
        {
            if (device.endpointId == selector ||
                ContainsOrdinalIgnoreCase(device.displayName, selector))
            {
                matches.push_back(device);
            }
        }
        Require(matches.size() == 1,
            "raw diagnostic selector must match exactly one microphone");
        auto binding = catalog.LockEndpoint(matches.front().endpointId);
        Require(binding != nullptr,
            "raw diagnostic could not lock the exact GstDevice");

        std::wstring runtimeError;
        Require(SUCCEEDED(xbpreview::EnsureGStreamerAudioRuntime(runtimeError)),
            "raw diagnostic GStreamer runtime is unavailable");
        auto* const pipeline = gst_pipeline_new("test_only_preflight_raw_diag");
        auto* const source = binding->CreateElement(
            "test_only_preflight_raw_source");
        auto* const level = gst_element_factory_make(
            "level", "test_only_preflight_raw_level");
        auto* const sink = gst_element_factory_make(
            "fakesink", "test_only_preflight_raw_sink");
        Require(pipeline != nullptr && source != nullptr &&
            level != nullptr && sink != nullptr,
            "raw diagnostic elements could not be created");
        Require(g_object_class_find_property(
                G_OBJECT_GET_CLASS(source), "continue-on-error") != nullptr,
            "raw diagnostic source lacks continue-on-error");
        g_object_set(G_OBJECT(source), "continue-on-error", TRUE, nullptr);
        g_object_set(
            G_OBJECT(level),
            "interval", static_cast<GstClockTime>(75 * GST_MSECOND),
            "post-messages", TRUE,
            nullptr);
        g_object_set(G_OBJECT(sink), "sync", FALSE, nullptr);

        const auto* const factory = gst_element_get_factory(source);
        const auto* const factoryName = factory != nullptr
            ? gst_plugin_feature_get_name(GST_PLUGIN_FEATURE(factory))
            : "<none>";
        gchar* elementDevice{};
        g_object_get(G_OBJECT(source), "device", &elementDevice, nullptr);
        std::cout << "DIAG_REQUESTED_MIC = "
            << WideToUtf8ForTest(selector) << '\n'
            << "DIAG_RESOLVED_MIC = "
            << WideToUtf8ForTest(binding->DisplayName()) << '\n'
            << "DIAG_RESOLVED_ENDPOINT = "
            << WideToUtf8ForTest(binding->EndpointId()) << '\n'
            << "DIAG_SOURCE_FACTORY = " << factoryName << '\n'
            << "DIAG_SOURCE_DEVICE_PROPERTY = "
            << (elementDevice != nullptr ? elementDevice : "<absent>") << '\n'
            << "DIAG_SOURCE_PROPERTIES = "
            << DiagnosticElementProperties(source) << '\n';
        if (elementDevice != nullptr) g_free(elementDevice);

        gst_bin_add_many(GST_BIN(pipeline), source, level, sink, nullptr);
        Require(gst_element_link_many(source, level, sink, nullptr),
            "raw diagnostic preflight chain could not link");
        auto* const sourcePad = gst_element_get_static_pad(source, "src");
        Require(sourcePad != nullptr,
            "raw diagnostic source has no static src pad");
        auto* const availableCaps = gst_pad_query_caps(sourcePad, nullptr);
        if (availableCaps != nullptr)
        {
            auto* const text = gst_caps_to_string(availableCaps);
            std::cout << "DIAG_SOURCE_AVAILABLE_CAPS = "
                << (text != nullptr ? text : "<unprintable>") << '\n';
            if (text != nullptr) g_free(text);
            gst_caps_unref(availableCaps);
        }
        RawBufferProbeFacts rawFacts;
        const auto probeId = gst_pad_add_probe(
            sourcePad, GST_PAD_PROBE_TYPE_BUFFER,
            ProbeRawSourceBuffer, &rawFacts, nullptr);
        Require(probeId != 0, "raw source pad probe could not attach");
        auto* const bus = gst_element_get_bus(pipeline);
        Require(bus != nullptr, "raw diagnostic pipeline has no bus");

        const auto transition = gst_element_set_state(
            pipeline, GST_STATE_PLAYING);
        GstState state{};
        GstState pending{};
        const auto wait = transition == GST_STATE_CHANGE_FAILURE
            ? GST_STATE_CHANGE_FAILURE
            : gst_element_get_state(
                pipeline, &state, &pending, 8 * GST_SECOND);
        Require(wait != GST_STATE_CHANGE_FAILURE && state == GST_STATE_PLAYING,
            "raw diagnostic pipeline did not reach PLAYING");
        std::cout << "DIAG_PIPELINE_PLAYING = YES\n";

        std::uint64_t levelMessageCount{};
        std::uint32_t printedLevelMessages{};
        double rawPeakMaximum = -std::numeric_limits<double>::infinity();
        double rawRmsMaximum = -std::numeric_limits<double>::infinity();
        double listOnlyPeakMaximum = -std::numeric_limits<double>::infinity();
        double listOnlyRmsMaximum = -std::numeric_limits<double>::infinity();
        const auto deadline = std::chrono::steady_clock::now() + duration;
        while (std::chrono::steady_clock::now() < deadline)
        {
            auto* const message = gst_bus_timed_pop(bus, 50 * GST_MSECOND);
            if (message == nullptr) continue;
            if (GST_MESSAGE_TYPE(message) == GST_MESSAGE_ERROR)
            {
                GError* error{};
                gchar* debug{};
                gst_message_parse_error(message, &error, &debug);
                const std::string text = error != nullptr && error->message
                    ? error->message
                    : "raw diagnostic GStreamer error";
                if (error != nullptr) g_error_free(error);
                if (debug != nullptr) g_free(debug);
                gst_message_unref(message);
                throw std::runtime_error(text);
            }
            const auto* const structure = gst_message_get_structure(message);
            if (GST_MESSAGE_TYPE(message) == GST_MESSAGE_ELEMENT &&
                structure != nullptr &&
                gst_structure_has_name(structure, "level"))
            {
                ++levelMessageCount;
                const auto rawPeak =
                    DiagnosticMaximumDb(structure, "peak");
                const auto rawRms =
                    DiagnosticMaximumDb(structure, "rms");
                rawPeakMaximum = (std::max)(rawPeakMaximum, rawPeak);
                rawRmsMaximum = (std::max)(rawRmsMaximum, rawRms);
                listOnlyPeakMaximum = (std::max)(
                    listOnlyPeakMaximum,
                    ListOnlyMaximumDb(structure, "peak"));
                listOnlyRmsMaximum = (std::max)(
                    listOnlyRmsMaximum,
                    ListOnlyMaximumDb(structure, "rms"));
                if (printedLevelMessages < 5)
                {
                    ++printedLevelMessages;
                    auto* const rawStructure =
                        gst_structure_to_string(structure);
                    std::cout << "RAW_LEVEL[" << printedLevelMessages
                        << "].structure = "
                        << (rawStructure != nullptr
                            ? rawStructure
                            : "<unprintable>") << '\n'
                        << "RAW_LEVEL[" << printedLevelMessages
                        << "].peak_type = "
                        << DiagnosticValueType(structure, "peak") << '\n'
                        << "RAW_LEVEL[" << printedLevelMessages
                        << "].peak_db = "
                        << DiagnosticSerializedValue(structure, "peak") << '\n'
                        << "RAW_LEVEL[" << printedLevelMessages
                        << "].rms_type = "
                        << DiagnosticValueType(structure, "rms") << '\n'
                        << "RAW_LEVEL[" << printedLevelMessages
                        << "].rms_db = "
                        << DiagnosticSerializedValue(structure, "rms") << '\n'
                        << "RAW_LEVEL[" << printedLevelMessages
                        << "].decay_db = "
                        << DiagnosticSerializedValue(structure, "decay") << '\n'
                        << "RAW_LEVEL[" << printedLevelMessages
                        << "].channel_count = "
                        << DiagnosticValueCount(
                            gst_structure_get_value(structure, "peak")) << '\n';
                    if (rawStructure != nullptr) g_free(rawStructure);
                }
            }
            gst_message_unref(message);
        }

        (void)gst_element_set_state(pipeline, GST_STATE_NULL);
        (void)gst_element_get_state(
            pipeline, &state, &pending, 5 * GST_SECOND);
        gst_pad_remove_probe(sourcePad, probeId);
        RawBufferProbeFacts snapshot;
        {
            std::lock_guard lock(rawFacts.mutex);
            snapshot.bufferCount = rawFacts.bufferCount;
            snapshot.totalBytes = rawFacts.totalBytes;
            snapshot.mappedBufferCount = rawFacts.mappedBufferCount;
            snapshot.anyMappedByteNonzero = rawFacts.anyMappedByteNonzero;
            snapshot.caps = rawFacts.caps;
            snapshot.timings = rawFacts.timings;
        }
        gst_object_unref(sourcePad);
        gst_object_unref(bus);
        gst_object_unref(pipeline);
        binding.reset();
        catalog.Stop();

        std::cout << "PREFLIGHT_RAW_BUFFER_COUNT = "
            << snapshot.bufferCount << '\n'
            << "PREFLIGHT_RAW_TOTAL_BYTES = "
            << snapshot.totalBytes << '\n'
            << "PREFLIGHT_RAW_MAPPED_BUFFER_COUNT = "
            << snapshot.mappedBufferCount << '\n'
            << "PREFLIGHT_RAW_CAPS = "
            << (snapshot.caps.empty() ? "<none>" : snapshot.caps) << '\n';
        for (std::size_t index = 0; index < snapshot.timings.size(); ++index)
        {
            std::cout << "PREFLIGHT_RAW_TIMING[" << index << "] PTS="
                << snapshot.timings[index].pts << " DURATION="
                << snapshot.timings[index].duration << '\n';
        }
        std::cout << "PREFLIGHT_RAW_BUFFER_ALL_ZERO = "
            << (snapshot.mappedBufferCount > 0 &&
                    !snapshot.anyMappedByteNonzero ? "YES" : "NO") << '\n'
            << "PREFLIGHT_RAW_BUFFER_NONZERO = "
            << (snapshot.anyMappedByteNonzero ? "YES" : "NO") << '\n'
            << "RAW_LEVEL_MESSAGE_COUNT = " << levelMessageCount << '\n'
            << std::fixed << std::setprecision(6)
            << "RAW_LEVEL_PEAK_DB_MAX = " << rawPeakMaximum << '\n'
            << "RAW_LEVEL_RMS_DB_MAX = " << rawRmsMaximum << '\n'
            << "LIST_ONLY_PEAK_DB_MAX = " << listOnlyPeakMaximum << '\n'
            << "LIST_ONLY_RMS_DB_MAX = " << listOnlyRmsMaximum << '\n';
        const auto rawLevelNonSilent =
            std::isfinite(rawPeakMaximum) && rawPeakMaximum > -120.0 &&
            std::isfinite(rawRmsMaximum) && rawRmsMaximum > -120.0;
        const auto listOnlyConversionCorrect = rawLevelNonSilent &&
            std::abs(rawPeakMaximum - listOnlyPeakMaximum) < 0.000001 &&
            std::abs(rawRmsMaximum - listOnlyRmsMaximum) < 0.000001;
        std::cout << "LEVEL_CONVERSION_CORRECT = "
            << (listOnlyConversionCorrect ? "YES" : "NO") << '\n';
        if (snapshot.anyMappedByteNonzero && rawLevelNonSilent &&
            !listOnlyConversionCorrect)
        {
            std::cout << "ROOT_CAUSE_CLASS = B LEVEL PARSE-CONVERSION BUG\n";
        }
        else if (snapshot.mappedBufferCount > 0 &&
            !snapshot.anyMappedByteNonzero)
        {
            std::cout << "ROOT_CAUSE_CLASS = A SOURCE PCM REALLY SILENT\n";
        }
        else
        {
            std::cout << "ROOT_CAUSE_CLASS = C PREFLIGHT PARITY GAP\n";
        }
    }

    void RunMicPreflightLiveReadout(
        const std::chrono::seconds duration,
        const std::wstring_view selector,
        const bool requireNonzero,
        const bool expectNearSilent)
    {
        xbpreview::GStreamerMicrophoneDeviceMonitor catalog;
        Require(SUCCEEDED(catalog.Start()),
            "product microphone device monitor did not start");
        const auto devices = catalog.Snapshot();
        Require(devices.monitorActive && !devices.devices.empty(),
            "product microphone catalog has no concrete devices");

        std::cout << "MIC_CATALOG_COUNT = " << devices.devices.size() << '\n';
        for (std::size_t index = 0; index < devices.devices.size(); ++index)
        {
            const auto& device = devices.devices[index];
            const auto catalogBinding = catalog.LockEndpoint(device.endpointId);
            Require(catalogBinding != nullptr,
                "catalog entry could not lock its exact GstDevice");
            const auto properties =
                ReadTestDeviceProperties(*catalogBinding);
            std::cout << "MIC[" << index << "].display_name = "
                << WideToUtf8ForTest(device.displayName) << '\n'
                << "MIC[" << index << "].device.default = ";
            if (properties.defaultPresent)
                std::cout << (properties.isDefault ? "true" : "false");
            else
                std::cout << "<absent>";
            std::cout << '\n'
                << "MIC[" << index << "].device.actual-id = "
                << (properties.actualId.empty()
                    ? "<absent>"
                    : WideToUtf8ForTest(properties.actualId)) << '\n'
                << "MIC[" << index << "].catalog_endpoint = "
                << WideToUtf8ForTest(device.endpointId) << '\n';
        }

        std::cout << "REQUESTED_MIC = "
            << (selector.empty()
                ? "<empty>"
                : WideToUtf8ForTest(selector)) << '\n';

        std::shared_ptr<xbpreview::GStreamerMicrophoneDeviceBinding> binding;
        std::wstring displayName;
        std::size_t selectorMatchCount{};
        bool usedRequestedDefault{};
        if (selector.empty() ||
            CompareStringOrdinal(
                selector.data(), static_cast<int>(selector.size()),
                L"windows-default", -1, TRUE) == CSTR_EQUAL)
        {
            usedRequestedDefault = true;
            binding = catalog.LockDefault();
            if (binding != nullptr)
            {
                const auto selected = std::find_if(
                    devices.devices.begin(), devices.devices.end(),
                    [&](const xbpreview::GStreamerMicrophoneDeviceInfo& value)
                    {
                        return value.endpointId == binding->EndpointId();
                    });
                displayName = selected != devices.devices.end()
                    ? selected->displayName
                    : binding->DisplayName();
            }
        }
        else
        {
            std::vector<xbpreview::GStreamerMicrophoneDeviceInfo> matches;
            for (const auto& device : devices.devices)
            {
                const auto endpointMatches = device.endpointId == selector;
                const auto displayMatches =
                    ContainsOrdinalIgnoreCase(device.displayName, selector);
                std::cout << "MATCH_EVAL display_name="
                    << WideToUtf8ForTest(device.displayName)
                    << " endpoint_exact="
                    << (endpointMatches ? "true" : "false")
                    << " display_contains="
                    << (displayMatches ? "true" : "false") << '\n';
                if (endpointMatches || displayMatches)
                {
                    matches.push_back(device);
                }
            }
            selectorMatchCount = matches.size();
            Require(matches.size() == 1,
                "microphone selector must match exactly one product device");
            binding = catalog.LockEndpoint(matches.front().endpointId);
            displayName = matches.front().displayName;
        }
        Require(binding != nullptr,
            "selected product microphone binding is unavailable");
        const auto resolvedProperties = ReadTestDeviceProperties(*binding);
        std::cout << "SELECTOR_MATCH_COUNT = " <<
            (usedRequestedDefault ? 1 : selectorMatchCount) << '\n'
            << "RESOLVED_MIC = "
            << WideToUtf8ForTest(binding->DisplayName()) << '\n'
            << "RESOLVED_ENDPOINT = "
            << WideToUtf8ForTest(binding->EndpointId()) << '\n'
            << "RESOLVED_DEVICE_ACTUAL_ID = "
            << (resolvedProperties.actualId.empty()
                ? "<absent>"
                : WideToUtf8ForTest(resolvedProperties.actualId)) << '\n'
            << "FALLBACK = NO\n";

        xbpreview::MicPreflightLevelMonitor preflight;
        Require(SUCCEEDED(preflight.Start(binding, binding->EndpointId())),
            "idle preflight worker could not start");
        const auto started = WaitForPreflight(preflight, false);
        Require(started.sourceCreatedFromDevice &&
            started.elementIdentityMatches,
            "idle preflight source identity mismatch");

        const auto pipelinePlaying = started.pipelineState ==
            xbpreview::MicPreflightPipelineState::Playing;
        std::cout << "PREFLIGHT_GST_DEVICE_NAME = "
            << WideToUtf8ForTest(binding->DisplayName()) << '\n'
            << "PREFLIGHT_GST_DEVICE_ACTUAL_ID = "
            << (resolvedProperties.actualId.empty()
                ? "<absent>"
                : WideToUtf8ForTest(resolvedProperties.actualId)) << '\n'
            << "GST_DEVICE_CREATE_ELEMENT_ENDPOINT = "
            << WideToUtf8ForTest(started.elementEndpointId) << '\n'
            << "GST_DEVICE_CREATE_ELEMENT_IDENTITY_MATCH = "
            << (started.elementIdentityMatches ? "YES" : "NO") << '\n'
            << "PIPELINE_PLAYING = "
            << (pipelinePlaying ? "YES" : "NO") << '\n'
            << "RECORDING_STARTED = NO\n";
        if (requireNonzero)
            std::cout << "Speak normally now. ";
        else if (expectNearSilent)
            std::cout << "Observe the inactive input without speaking. ";
        else
            std::cout << "Observe the test-only readout. ";
        std::cout << "Readout interval: 100 ms.\n\n";
        std::cout
            << "elapsed_ms available peak_pcm16 rms_pcm16 peak_db rms_db messages\n";

        const auto begin = std::chrono::steady_clock::now();
        const auto deadline = begin + duration;
        std::uint32_t maximumPeak{};
        double maximumRms{};
        std::uint64_t finalMessages{};
        while (std::chrono::steady_clock::now() < deadline)
        {
            const auto snapshot = preflight.Snapshot();
            Require(snapshot.pipelineState !=
                xbpreview::MicPreflightPipelineState::Failed,
                "idle preflight failed during live readout");
            maximumPeak = (std::max)(
                maximumPeak, snapshot.peakAbsolutePcm16);
            maximumRms = (std::max)(maximumRms, snapshot.rmsPcm16);
            finalMessages = snapshot.levelMessageCount;
            const auto elapsed = std::chrono::duration_cast<
                std::chrono::milliseconds>(
                    std::chrono::steady_clock::now() - begin).count();
            std::cout << elapsed << ' '
                << (snapshot.available ? 1 : 0) << ' '
                << snapshot.peakAbsolutePcm16 << ' '
                << snapshot.rmsPcm16 << ' '
                << snapshot.peakDb << ' '
                << snapshot.rmsDb << ' '
                << snapshot.levelMessageCount << '\n' << std::flush;
            std::this_thread::sleep_for(std::chrono::milliseconds(100));
        }

        const auto finalSnapshot = preflight.Snapshot();
        maximumPeak = (std::max)(
            maximumPeak, finalSnapshot.peakAbsolutePcm16);
        maximumRms = (std::max)(maximumRms, finalSnapshot.rmsPcm16);
        finalMessages = (std::max)(
            finalMessages, finalSnapshot.levelMessageCount);
        preflight.Stop();
        catalog.Stop();
        Require(finalMessages > 0,
            "idle preflight produced no level messages");
        const auto nonzeroLevel = maximumPeak > 0 && maximumRms > 0.0;
        std::cout << "LEVEL_MESSAGES = " << finalMessages << '\n'
            << "LEVEL_MESSAGE_STREAM_ACTIVE = YES\n"
            << "NONZERO_LEVEL = " << (nonzeroLevel ? "YES" : "NO") << '\n';
        const auto requestedKakaboom =
            ContainsOrdinalIgnoreCase(selector, L"Kakaboom");
        const auto resolvedKakaboom = ContainsOrdinalIgnoreCase(
            binding->DisplayName(), L"Kakaboom");
        const auto resolvedOsk218 = ContainsOrdinalIgnoreCase(
            binding->DisplayName(), L"OSK218");
        if (requestedKakaboom && (!resolvedKakaboom || resolvedOsk218))
            std::cout << "A1 INVALID TEST - WRONG DEVICE\n";
        else if (requestedKakaboom && !nonzeroLevel)
            std::cout << "KAKABOOM BOUND BUT SILENT\n";
        if (requireNonzero)
        {
            Require(nonzeroLevel,
                "speech did not produce a nonzero preflight level");
            std::cout << "IDLE_MIC_PREFLIGHT_HUMAN_PASS";
        }
        else if (expectNearSilent)
        {
            constexpr std::uint32_t NearSilentPeakPcm16 = 32;
            constexpr double NearSilentRmsPcm16 = 16.0;
            Require(maximumPeak <= NearSilentPeakPcm16 &&
                maximumRms <= NearSilentRmsPcm16,
                "inactive input exceeded the TEST-ONLY near-silent observation limits");
            std::cout << "IDLE_MIC_PREFLIGHT_NEAR_SILENT_PASS";
        }
        else
        {
            std::cout << "IDLE_MIC_PREFLIGHT_READOUT_SMOKE_PASS";
        }
        std::cout << " max_peak_pcm16=" << maximumPeak
            << " max_rms_pcm16=" << maximumRms
            << " level_messages=" << finalMessages << '\n';
    }

    void TestDeviceLifecycleAdapter()
    {
        xbpreview::GStreamerAudioCore core;
        core.TestLockMicrophoneDeviceId("locked-mic");
        core.TestInjectDeviceEvent("new-default-mic", true);
        auto snapshot = core.Snapshot();
        Require(!snapshot.micDisconnected &&
            snapshot.micDeviceId == L"locked-mic" &&
            snapshot.micSessionBound &&
            snapshot.micSourceCreatedFromDevice,
            "new devices do not switch the locked recording microphone");
        core.TestInjectDeviceEvent("other-mic", false);
        Require(!core.Snapshot().micDisconnected,
            "removing another device does not affect the active microphone");
        core.TestInjectDeviceEvent("locked-mic", false);
        snapshot = core.Snapshot();
        Require(snapshot.micDisconnected &&
            snapshot.micSourceDataBlocked &&
            snapshot.micDeviceId == L"locked-mic",
            "removing the active device reports MicDisconnected and blocks its PCM without reconnect");
    }

    void TestInitializationFailure(const std::filesystem::path& root)
    {
        const auto directory = root / L"injected-initialization-failure";
        std::filesystem::create_directories(directory);
        xbpreview::GStreamerAudioCore core;
        const auto result = StartAudio(
            core,
            xbpreview::GStreamerAudioMode::SystemOnly,
            directory,
            true);
        const auto snapshot = core.Snapshot();
        Require(FAILED(result) &&
            snapshot.pipelineState ==
                xbpreview::GStreamerAudioPipelineState::Failed &&
            !std::filesystem::exists(directory / L"system.flac") &&
            !std::filesystem::exists(directory / L"mic.flac"),
            "initialization failure is explicit and creates no fake media");
    }

    void TestNoneAndMissingMicrophone(const std::filesystem::path& root)
    {
        xbpreview::GStreamerAudioCore none;
        Require(SUCCEEDED(StartAudio(
                none, xbpreview::GStreamerAudioMode::None, {})) &&
            none.Snapshot().pipelineState ==
                xbpreview::GStreamerAudioPipelineState::Stopped &&
            none.Snapshot().audioWorkingPath.empty(),
            "None succeeds without creating an audio pipeline or sidecar");

        const auto directory = root / L"missing-microphone";
        std::filesystem::create_directories(directory);
        xbpreview::GStreamerAudioCore missing;
        const auto result = StartAudio(
            missing,
            xbpreview::GStreamerAudioMode::MicrophoneOnly,
            directory,
            false,
            true);
        const auto snapshot = missing.Snapshot();
        Require(result == HRESULT_FROM_WIN32(ERROR_NOT_FOUND) &&
            snapshot.lastGStreamerError == L"MicUnavailableAtStart" &&
            snapshot.pipelineState ==
                xbpreview::GStreamerAudioPipelineState::Failed &&
            snapshot.micActive &&
            !snapshot.deviceMonitorActive &&
            !snapshot.micSessionBound &&
            !snapshot.micSourceCreatedFromDevice &&
            snapshot.systemWorkingPath.empty() &&
            snapshot.microphoneWorkingPath.empty() &&
            !std::filesystem::exists(directory / L"system.flac") &&
            !std::filesystem::exists(directory / L"mic.flac"),
            "unresolved default pseudo-device fails before pipeline/audio creation");
    }

    void RunCapture(
        const xbpreview::GStreamerAudioMode mode,
        const std::filesystem::path& directory,
        const std::chrono::seconds duration)
    {
        Require(directory.is_absolute(), "capture output path is absolute");
        std::filesystem::create_directories(directory);
        xbpreview::GStreamerAudioCore core;
        const auto start = StartAudio(core, mode, directory);
        if (FAILED(start))
        {
            const auto failed = core.Snapshot();
            std::wcerr << L"GStreamer Start failed: 0x" << std::hex << start
                << L" " << failed.lastGStreamerError << L'\n';
            throw std::runtime_error("real GStreamer capture did not start");
        }
        auto snapshot = core.Snapshot();
        Require(snapshot.pipelineState ==
                xbpreview::GStreamerAudioPipelineState::Playing &&
            snapshot.deviceMonitorActive,
            "real pipeline and GstDeviceMonitor are active");
        if (mode == xbpreview::GStreamerAudioMode::MicrophoneOnly ||
            mode == xbpreview::GStreamerAudioMode::Dual)
        {
            Require(snapshot.micActive && !snapshot.micDeviceId.empty() &&
                !snapshot.micDeviceDisplayName.empty() &&
                !snapshot.micDeviceProperties.empty() &&
                snapshot.micSessionBound &&
                snapshot.micSourceCreatedFromDevice,
                "real concrete GstDevice creates and binds the microphone source at Start");
        }
        if (mode == xbpreview::GStreamerAudioMode::Dual)
        {
            Require(snapshot.systemActive &&
                snapshot.dualSourcesIndependent &&
                !snapshot.mixerVolumesFixedAtUnity,
                "dual GStreamer sources remain independent before FFmpeg");
        }
        std::this_thread::sleep_for(duration);
        const auto stop = core.Stop();
        snapshot = core.Snapshot();
        Require(SUCCEEDED(stop) && snapshot.endOfStreamObserved &&
            snapshot.filesClosed && snapshot.busThreadExited &&
            !snapshot.deviceMonitorActive &&
            snapshot.pipelineState ==
                xbpreview::GStreamerAudioPipelineState::Stopped,
            "Stop observes EOS, closes FLAC, exits buses, and releases monitor");
        if (mode == xbpreview::GStreamerAudioMode::SystemOnly)
        {
            Require(snapshot.systemWorkingPath == directory / L"system.flac" &&
                snapshot.microphoneWorkingPath.empty() &&
                snapshot.audioWorkingPath == snapshot.systemWorkingPath,
                "SystemOnly closes only system.flac");
        }
        else if (mode == xbpreview::GStreamerAudioMode::MicrophoneOnly)
        {
            Require(snapshot.systemWorkingPath.empty() &&
                snapshot.microphoneWorkingPath == directory / L"mic.flac" &&
                snapshot.audioWorkingPath == snapshot.microphoneWorkingPath,
                "MicrophoneOnly closes only mic.flac");
        }
        else
        {
            Require(snapshot.systemWorkingPath == directory / L"system.flac" &&
                snapshot.microphoneWorkingPath == directory / L"mic.flac" &&
                snapshot.audioWorkingPath.empty() &&
                snapshot.dualSourcesIndependent,
                "Dual closes independent system.flac and mic.flac");
        }
        std::wcout << L"{\"mode\":\""
            << xbpreview::GStreamerAudioModeName(mode)
            << L"\",\"systemPath\":\""
            << snapshot.systemWorkingPath.wstring()
            << L"\",\"micPath\":\""
            << snapshot.microphoneWorkingPath.wstring()
            << L"\",\"micDisconnected\":"
            << (snapshot.micDisconnected ? 1 : 0) << L"}\n";
    }

    void RunMicrophoneRestartCapture(
        const std::filesystem::path& directory,
        const std::chrono::seconds duration)
    {
        xbpreview::GStreamerAudioCore core;
        std::wstring firstDevice;
        for (const auto* const name : { L"first-start", L"next-start" })
        {
            const auto session = directory / name;
            std::filesystem::create_directories(session);
            Require(SUCCEEDED(StartAudio(
                    core,
                    xbpreview::GStreamerAudioMode::MicrophoneOnly,
                    session)),
                "microphone restart capture did not start");
            const auto started = core.Snapshot();
            Require(started.deviceMonitorActive &&
                !started.micDeviceId.empty() &&
                started.pipelineState ==
                    xbpreview::GStreamerAudioPipelineState::Playing,
                "each Start creates a monitor and resolves a concrete mic");
            if (firstDevice.empty()) firstDevice = started.micDeviceId;
            std::this_thread::sleep_for(duration);
            Require(SUCCEEDED(core.Stop()),
                "microphone restart capture did not stop");
            const auto stopped = core.Snapshot();
            Require(stopped.filesClosed && stopped.busThreadExited &&
                !stopped.deviceMonitorActive &&
                stopped.microphoneWorkingPath == session / L"mic.flac",
                "each Stop closes mic.flac and releases its monitor");
        }
        const auto final = core.Snapshot();
        Require(!firstDevice.empty() && !final.micDeviceId.empty(),
            "next Start re-enumerated a concrete microphone");
        std::cout << "GStreamer microphone next-Start re-enumeration PASS\n";
    }

    void RunMicrophoneDeviceLifecycle(
        const std::filesystem::path& directory,
        const std::chrono::seconds duration)
    {
        xbpreview::GStreamerAudioCore core;
        const auto connected = directory / L"connected";
        std::filesystem::create_directories(connected);
        Require(SUCCEEDED(StartAudio(
                core,
                xbpreview::GStreamerAudioMode::MicrophoneOnly,
                connected)),
            "connected concrete GstDevice did not start");
        const auto started = core.Snapshot();
        Require(started.deviceMonitorActive &&
            !started.micDeviceId.empty() &&
            !started.micDeviceDisplayName.empty() &&
            !started.micDeviceProperties.empty() &&
            started.micSessionBound &&
            started.micSourceCreatedFromDevice,
            "connected monitor/device/create_element facts are complete");

        core.TestInjectDeviceEvent("other-microphone", true);
        Require(!core.Snapshot().micDisconnected,
            "DEVICE_ADDED does not switch the session device");
        core.TestInjectLockedMicrophoneRemoval();
        const auto removed = core.Snapshot();
        Require(removed.micDisconnected &&
            removed.micSourceDataBlocked &&
            removed.micDeviceId == started.micDeviceId,
            "DEVICE_REMOVED blocks the locked source without reconnecting");
        std::this_thread::sleep_for(duration);
        Require(SUCCEEDED(core.Stop()),
            "removal-marked microphone session stopped safely");
        const auto stopped = core.Snapshot();
        Require(stopped.filesClosed && stopped.busThreadExited &&
            !stopped.deviceMonitorActive,
            "removal-marked session closes FLAC and releases its monitor");

        const auto reappeared = directory / L"reappeared";
        std::filesystem::create_directories(reappeared);
        Require(SUCCEEDED(StartAudio(
                core,
                xbpreview::GStreamerAudioMode::MicrophoneOnly,
                reappeared)),
            "next Start did not re-probe the current GstDevice list");
        const auto restarted = core.Snapshot();
        Require(restarted.deviceMonitorActive &&
            restarted.micSessionBound &&
            restarted.micSourceCreatedFromDevice &&
            !restarted.micDisconnected &&
            !restarted.micDeviceId.empty(),
            "next Start binds the newly probed concrete GstDevice");
        std::this_thread::sleep_for(duration);
        Require(SUCCEEDED(core.Stop()),
            "re-probed microphone session stopped safely");
        std::cout << "GStreamer concrete microphone device lifecycle PASS\n";
    }

    void RunMicrophoneSelectorGate(
        const std::filesystem::path& directory,
        const std::chrono::seconds duration)
    {
        xbpreview::GStreamerMicrophoneDeviceMonitor monitor;
        Require(SUCCEEDED(monitor.Start()),
            "selector GstDeviceMonitor did not start");
        const auto catalog = monitor.Snapshot();
        Require(catalog.monitorActive && catalog.defaultAvailable,
            "selector catalog has no concrete Windows default microphone");
        Require(!catalog.devices.empty(),
            "selector catalog has no real concrete microphone");

        for (const auto& device : catalog.devices)
        {
            auto binding = monitor.LockEndpoint(device.endpointId);
            Require(binding != nullptr &&
                binding->EndpointId() == device.endpointId &&
                binding->DisplayName() == device.displayName,
                "UI catalog identity does not map to the same GstDevice");
            auto* const element = binding->CreateElement("selector_probe");
            Require(element != nullptr,
                "catalog GstDevice could not create its wasapi2 source");
            gchar* elementDeviceId{};
            g_object_get(
                G_OBJECT(element), "device", &elementDeviceId, nullptr);
            const auto elementIdentity = Utf8ToWideForTest(elementDeviceId);
            if (elementDeviceId) g_free(elementDeviceId);
            gst_object_unref(element);
            Require(elementIdentity == device.endpointId,
                "gst_device_create_element identity differs from catalog endpoint");
        }

        auto first = monitor.LockDefault();
        Require(first != nullptr,
            "Windows default could not lock a concrete GstDevice");
        const auto secondInfo = std::find_if(
            catalog.devices.begin(), catalog.devices.end(),
            [&](const xbpreview::GStreamerMicrophoneDeviceInfo& value)
            {
                return value.endpointId != first->EndpointId();
            });
        auto second = secondInfo != catalog.devices.end()
            ? monitor.LockEndpoint(secondInfo->endpointId)
            : nullptr;

        const auto capture = [&](
            const wchar_t* const name,
            const std::shared_ptr<
                xbpreview::GStreamerMicrophoneDeviceBinding>& binding)
        {
            const auto output = directory / name;
            std::filesystem::create_directories(output);
            xbpreview::GStreamerAudioCore core;
            Require(SUCCEEDED(StartAudio(
                    core,
                    xbpreview::GStreamerAudioMode::MicrophoneOnly,
                    output,
                    false,
                    false,
                    binding)),
                "exact selected endpoint capture did not start");
            const auto started = core.Snapshot();
            Require(started.micDeviceId == binding->EndpointId() &&
                started.micElementDeviceId == binding->EndpointId() &&
                started.micElementIdentityMatches &&
                started.micSourceCreatedFromDevice,
                "Session source identity does not match selected GstDevice");
            std::this_thread::sleep_for(duration);
            Require(SUCCEEDED(core.Stop()),
                "exact selected endpoint capture did not stop");
        };

        capture(L"selected-a", first);

        const auto unavailable = directory / L"selected-a-unavailable";
        std::filesystem::create_directories(unavailable);
        xbpreview::GStreamerAudioCore rejected;
        const auto rejectedResult = StartWithUnavailableSelectedEndpoint(
            rejected, unavailable, first);
        const auto rejectedSnapshot = rejected.Snapshot();
        Require(rejectedResult == HRESULT_FROM_WIN32(ERROR_NOT_FOUND) &&
            rejectedSnapshot.lastGStreamerError == L"MicUnavailableAtStart" &&
            !std::filesystem::exists(unavailable / L"mic.flac") &&
            (second == nullptr || monitor.Contains(second->EndpointId())),
            "missing selected A did not reject without fallback");

        if (second != nullptr)
        {
            Require(second->EndpointId() != first->EndpointId(),
                "second concrete endpoint did not resolve exactly");
            capture(L"selected-b", second);
        }
        monitor.Stop();
        std::wcout << L"MICROPHONE-SELECTOR default=\""
            << first->DisplayName() << L"\" firstId=\""
            << first->EndpointId() << L"\" devices="
            << catalog.devices.size();
        if (second != nullptr)
        {
            std::wcout << L" second=\"" << second->DisplayName()
                << L"\" secondId=\"" << second->EndpointId() << L"\"";
        }
        else
        {
            std::wcout << L" second=\"PENDING-PHYSICAL-DEVICE\"";
        }
        std::wcout << L"\n";
        std::cout << "GStreamer microphone selector PASS\n";
    }

    void TestFinalizerArguments(const std::filesystem::path& root)
    {
        using xbpreview::GStreamerAudioFinalizeRequest;
        using xbpreview::GStreamerAudioLoudnessMeasurement;
        using xbpreview::GStreamerAudioMode;
        const auto directory = root / L"finalizer-contract";
        std::filesystem::create_directories(directory);
        const auto video = directory / L"video.mp4";
        const auto system = directory / L"system.flac";
        const auto microphone = directory / L"mic.flac";
        for (const auto& path : { video, system, microphone })
        {
            std::ofstream stream(path, std::ios::binary | std::ios::trunc);
            stream.put('\0');
        }
        GStreamerAudioLoudnessMeasurement measurement{
            -35.83, -20.63, 1.25, -45.83, -0.12, true };

        GStreamerAudioFinalizeRequest request{};
        request.videoPath = video;
        request.outputPath = directory / L"output.mp4";
        request.expectedDuration100ns = 30'000'000;

        request.mode = GStreamerAudioMode::SystemOnly;
        request.systemFlacPath = system;
        const auto systemArguments = JoinArguments(
            xbpreview::BuildGStreamerAudioFfmpegArguments(request));
        Require(!Contains(systemArguments, L"loudnorm") &&
            !Contains(systemArguments, L"amix") &&
            !Contains(systemArguments, L"-af") &&
            Contains(systemArguments, L"copy") &&
            Contains(systemArguments, L"1:a:0"),
            "SystemOnly finalizer is filter-free H.264 stream-copy/AAC mux");

        request.mode = GStreamerAudioMode::MicrophoneOnly;
        request.systemFlacPath.clear();
        request.microphoneFlacPath = microphone;
        const auto microphoneArguments = JoinArguments(
            xbpreview::BuildGStreamerAudioFfmpegArguments(
                request, measurement));
        Require(Contains(microphoneArguments,
                L"loudnorm=I=-16:TP=-3.0:LRA=7") &&
            Contains(microphoneArguments, L"measured_I=-35.830000") &&
            Contains(microphoneArguments, L"linear=true") &&
            !Contains(microphoneArguments, L"dual_mono") &&
            !Contains(microphoneArguments, L"amix") &&
            !Contains(microphoneArguments, system.wstring()),
            "MicrophoneOnly finalizer uses measured two-pass loudnorm only");

        request.mode = GStreamerAudioMode::Dual;
        request.systemFlacPath = system;
        const auto dualArguments = JoinArguments(
            xbpreview::BuildGStreamerAudioFfmpegArguments(
                request, measurement, measurement));
        Require(Count(dualArguments,
                L"loudnorm=I=-16:TP=-3.0:LRA=7") == 2 &&
            Contains(dualArguments,
                L"amix=inputs=2:weights='1 1':normalize=1") &&
            Contains(dualArguments, system.wstring()) &&
            Contains(dualArguments, microphone.wstring()) &&
            !Contains(dualArguments, L"agate") &&
            !Contains(dualArguments, L"expander") &&
            !Contains(dualArguments, L"volume="),
            "Dual finalizer masters mic, unity-mixes, then masters program");
    }

    int RunFinalizeFixture(const int argc, wchar_t** const argv)
    {
        Require(argc == 8,
            "usage: --finalize-fixture <mode> <video> <system|-> <mic|-> <output> <duration100ns>");
        xbpreview::GStreamerAudioFinalizeRequest request{};
        request.mode = ParseMode(argv[2]);
        request.videoPath = std::filesystem::absolute(argv[3]);
        if (std::wstring_view(argv[4]) != L"-")
            request.systemFlacPath = std::filesystem::absolute(argv[4]);
        if (std::wstring_view(argv[5]) != L"-")
            request.microphoneFlacPath = std::filesystem::absolute(argv[5]);
        request.outputPath = std::filesystem::absolute(argv[6]);
        request.expectedDuration100ns = std::stoll(argv[7]);
        request.timeout = std::chrono::minutes(1);
        const auto result = xbpreview::FinalizeGStreamerAudio(request);
        std::cout << "{\"hresult\":" << result.hresult
            << ",\"validationHResult\":" << result.validationHResult
            << ",\"integratedLufs\":"
            << result.validation.integratedLufs
            << ",\"truePeakDbtp\":"
            << result.validation.truePeakDbtp
            << ",\"loudnessValidated\":"
            << (result.validation.finalLoudnessValidated ? 1 : 0)
            << ",\"microphoneMastering\":"
            << (result.microphoneMasteringApplied ? 1 : 0)
            << ",\"dualMix\":" << (result.dualMixApplied ? 1 : 0)
            << "}\n";
        if (FAILED(result.hresult))
        {
            std::cerr << result.stderrText << '\n';
            return 1;
        }
        return 0;
    }
}

int wmain(const int argc, wchar_t** const argv)
{
    try
    {
        TestStaticContract();
        TestDeviceLifecycleAdapter();
        if (argc == 2 &&
            std::wstring_view(argv[1]) ==
                L"--mic-preflight-parser-gates")
        {
            TestMicPreflightLevelParser();
            return 0;
        }
        if (argc >= 2 && std::wstring_view(argv[1]) == L"--finalize-fixture")
            return RunFinalizeFixture(argc, argv);
        if (argc >= 2 &&
            std::wstring_view(argv[1]) == L"--mic-preflight-gates")
        {
            Require(argc >= 3,
                "usage: --mic-preflight-gates <absolute-dir>");
            RunMicPreflightGates(std::filesystem::absolute(argv[2]));
            return 0;
        }
        if (argc >= 2 &&
            std::wstring_view(argv[1]) == L"--mic-preflight-live")
        {
            Require(argc >= 3,
                "usage: --mic-preflight-live <seconds> [device-name-or-endpoint] [--require-nonzero|--expect-near-silent]");
            const auto seconds = std::stoi(argv[2]);
            Require(seconds > 0 && seconds <= 120,
                "live readout duration must be between 1 and 120 seconds");
            std::wstring_view selector;
            bool requireNonzero{};
            bool expectNearSilent{};
            for (int index = 3; index < argc; ++index)
            {
                if (std::wstring_view(argv[index]) == L"--require-nonzero")
                    requireNonzero = true;
                else if (std::wstring_view(argv[index]) ==
                    L"--expect-near-silent")
                    expectNearSilent = true;
                else
                {
                    Require(selector.empty(),
                        "only one device selector may be supplied");
                    selector = argv[index];
                }
            }
            Require(!(requireNonzero && expectNearSilent),
                "--require-nonzero and --expect-near-silent are mutually exclusive");
            RunMicPreflightLiveReadout(
                std::chrono::seconds(seconds), selector,
                requireNonzero, expectNearSilent);
            return 0;
        }
        if (argc == 4 &&
            std::wstring_view(argv[1]) ==
                L"--mic-preflight-raw-diagnostic")
        {
            const auto seconds = std::stoi(argv[2]);
            Require(seconds > 0 && seconds <= 5,
                "raw diagnostic duration must be between 1 and 5 seconds");
            RunMicPreflightRawDiagnostic(
                std::chrono::seconds(seconds), argv[3]);
            return 0;
        }
        if (argc >= 2 &&
            std::wstring_view(argv[1]) == L"--restart-microphone")
        {
            Require(argc >= 3,
                "usage: --restart-microphone <absolute-dir> [seconds]");
            const auto seconds = argc >= 4 ? std::stoi(argv[3]) : 2;
            Require(seconds > 0 && seconds <= 30,
                "restart duration must be between 1 and 30 seconds");
            RunMicrophoneRestartCapture(
                std::filesystem::absolute(argv[2]),
                std::chrono::seconds(seconds));
            return 0;
        }
        if (argc >= 2 &&
            std::wstring_view(argv[1]) == L"--microphone-device-lifecycle")
        {
            Require(argc >= 3,
                "usage: --microphone-device-lifecycle <absolute-dir> [seconds]");
            const auto seconds = argc >= 4 ? std::stoi(argv[3]) : 1;
            Require(seconds > 0 && seconds <= 10,
                "lifecycle duration must be between 1 and 10 seconds");
            RunMicrophoneDeviceLifecycle(
                std::filesystem::absolute(argv[2]),
                std::chrono::seconds(seconds));
            return 0;
        }
        if (argc >= 2 &&
            std::wstring_view(argv[1]) == L"--microphone-selector")
        {
            Require(argc >= 3,
                "usage: --microphone-selector <absolute-dir> [seconds]");
            const auto seconds = argc >= 4 ? std::stoi(argv[3]) : 1;
            Require(seconds > 0 && seconds <= 10,
                "selector duration must be between 1 and 10 seconds");
            RunMicrophoneSelectorGate(
                std::filesystem::absolute(argv[2]),
                std::chrono::seconds(seconds));
            return 0;
        }
        if (argc == 2 && std::wstring_view(argv[1]) == L"--describe-system")
        {
            std::cout << xbpreview::GStreamerAudioPipelineDescription(
                xbpreview::GStreamerAudioMode::SystemOnly);
            return 0;
        }
        if (argc == 2 &&
            std::wstring_view(argv[1]) == L"--describe-microphone")
        {
            std::cout << xbpreview::GStreamerAudioPipelineDescription(
                xbpreview::GStreamerAudioMode::MicrophoneOnly);
            return 0;
        }
        if (argc == 2 && std::wstring_view(argv[1]) == L"--describe-dual")
        {
            std::cout << xbpreview::GStreamerAudioPipelineDescription(
                xbpreview::GStreamerAudioMode::Dual);
            return 0;
        }
        if (argc == 1 || std::wstring_view(argv[1]) == L"--contract")
        {
            const auto root = std::filesystem::current_path() /
                L"artifacts" / L"gate" / L"gstreamer-audio";
            TestFinalizerArguments(root);
            TestInitializationFailure(root);
            TestNoneAndMissingMicrophone(root);
            std::cout << "GStreamer audio contract PASS\n";
            return 0;
        }
        Require(argc >= 4 && std::wstring_view(argv[1]) == L"--capture",
            "usage: --capture <system|microphone|dual> <absolute-dir> [seconds]");
        const auto seconds = argc >= 5 ? std::stoi(argv[4]) : 5;
        Require(seconds > 0 && seconds <= 300,
            "capture duration must be between 1 and 300 seconds");
        RunCapture(
            ParseMode(argv[2]),
            std::filesystem::absolute(argv[3]),
            std::chrono::seconds(seconds));
        return 0;
    }
    catch (const std::exception& error)
    {
        std::cerr << "GStreamer audio test FAILED: " << error.what() << '\n';
        return 1;
    }
}
