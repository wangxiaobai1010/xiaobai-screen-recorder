#include "GStreamerAudioFinalizer.h"

#include <mfapi.h>
#include <mferror.h>
#include <mfidl.h>
#include <mfreadwrite.h>
#include <winrt/base.h>

#include <algorithm>
#include <cerrno>
#include <cmath>
#include <cstdlib>
#include <filesystem>
#include <iomanip>
#include <limits>
#include <locale>
#include <sstream>
#include <string>
#include <utility>
#include <vector>

namespace xbpreview
{
    namespace
    {
        constexpr std::size_t MaximumRetainedStderrBytes = 64 * 1024;
        constexpr std::int64_t DurationTolerance100ns = 2'000'000;
        constexpr auto LoudnormTarget = L"loudnorm=I=-16:TP=-3.0:LRA=7";
        constexpr double MicrophoneMasteringSafetyFloorLufs = -60.0;
        constexpr double ProgramMasteringSafetyFloorLufs = -60.0;

        struct FfmpegProcessResult
        {
            HRESULT hresult{ E_PENDING };
            DWORD exitCode{ STILL_ACTIVE };
            std::string stderrText;
            bool processStarted{};
            bool timedOut{};
            bool processTreeTerminated{};
        };

        std::wstring ReadEnvironment(const wchar_t* const name)
        {
            const auto required = GetEnvironmentVariableW(name, nullptr, 0);
            if (required == 0) return {};
            std::wstring value(required, L'\0');
            const auto written = GetEnvironmentVariableW(
                name, value.data(), static_cast<DWORD>(value.size()));
            if (written == 0 || written >= value.size()) return {};
            value.resize(written);
            return value;
        }

        std::filesystem::path CurrentModuleDirectory()
        {
            HMODULE module{};
            if (!GetModuleHandleExW(
                    GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS |
                        GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
                    reinterpret_cast<LPCWSTR>(&CurrentModuleDirectory),
                    &module))
                return {};
            std::wstring path(32'768, L'\0');
            const auto written = GetModuleFileNameW(
                module, path.data(), static_cast<DWORD>(path.size()));
            if (written == 0 || written >= path.size()) return {};
            path.resize(written);
            return std::filesystem::path(path).parent_path();
        }

        bool IsRegularAbsolutePath(const std::filesystem::path& path)
        {
            std::error_code error;
            return path.is_absolute() &&
                std::filesystem::is_regular_file(path, error) && !error;
        }

        std::wstring QuoteWindowsArgument(const std::wstring& argument)
        {
            if (argument.empty()) return L"\"\"";
            if (argument.find_first_of(L" \t\n\v\"") == std::wstring::npos)
                return argument;
            std::wstring result{ L'\"' };
            std::size_t backslashes{};
            for (const auto character : argument)
            {
                if (character == L'\\')
                {
                    ++backslashes;
                    continue;
                }
                if (character == L'\"')
                {
                    result.append(backslashes * 2 + 1, L'\\');
                    result.push_back(L'\"');
                    backslashes = 0;
                    continue;
                }
                result.append(backslashes, L'\\');
                backslashes = 0;
                result.push_back(character);
            }
            result.append(backslashes * 2, L'\\');
            result.push_back(L'\"');
            return result;
        }

        std::wstring BuildCommandLine(
            const std::filesystem::path& executable,
            const std::vector<std::wstring>& arguments)
        {
            auto result = QuoteWindowsArgument(executable.wstring());
            for (const auto& argument : arguments)
            {
                result.push_back(L' ');
                result.append(QuoteWindowsArgument(argument));
            }
            return result;
        }

        void AppendPipe(HANDLE pipe, std::string& output) noexcept
        {
            for (;;)
            {
                DWORD available{};
                if (!PeekNamedPipe(pipe, nullptr, 0, nullptr, &available, nullptr) ||
                    available == 0)
                    return;
                char buffer[4096]{};
                DWORD read{};
                if (!ReadFile(
                        pipe, buffer,
                        (std::min)(available, static_cast<DWORD>(sizeof(buffer))),
                        &read, nullptr) || read == 0)
                    return;
                if (output.size() + read > MaximumRetainedStderrBytes)
                {
                    const auto overflow = output.size() + read -
                        MaximumRetainedStderrBytes;
                    output.erase(0, (std::min)(overflow, output.size()));
                }
                output.append(buffer, read);
            }
        }

        FfmpegProcessResult RunFfmpegProcess(
            const std::filesystem::path& executable,
            const std::vector<std::wstring>& arguments,
            const std::chrono::milliseconds timeout) noexcept
        {
            FfmpegProcessResult result{};
            HANDLE stderrRead{};
            HANDLE stderrWrite{};
            HANDLE nullInput{ INVALID_HANDLE_VALUE };
            HANDLE nullOutput{ INVALID_HANDLE_VALUE };
            HANDLE job{};
            PROCESS_INFORMATION process{};
            struct ResourceCleanup final
            {
                HANDLE& stderrRead;
                HANDLE& stderrWrite;
                HANDLE& nullInput;
                HANDLE& nullOutput;
                HANDLE& job;
                PROCESS_INFORMATION& process;
                ~ResourceCleanup()
                {
                    if (process.hThread) CloseHandle(process.hThread);
                    if (process.hProcess) CloseHandle(process.hProcess);
                    if (job) CloseHandle(job);
                    if (stderrRead) CloseHandle(stderrRead);
                    if (stderrWrite) CloseHandle(stderrWrite);
                    if (nullInput != INVALID_HANDLE_VALUE) CloseHandle(nullInput);
                    if (nullOutput != INVALID_HANDLE_VALUE) CloseHandle(nullOutput);
                }
            } cleanup{
                stderrRead, stderrWrite, nullInput, nullOutput, job, process };
            try
            {
                SECURITY_ATTRIBUTES security{ sizeof(security), nullptr, TRUE };
                if (!CreatePipe(&stderrRead, &stderrWrite, &security, 0) ||
                    !SetHandleInformation(stderrRead, HANDLE_FLAG_INHERIT, 0))
                {
                    result.hresult = HRESULT_FROM_WIN32(GetLastError());
                    return result;
                }
                nullInput = CreateFileW(
                    L"NUL", GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE,
                    &security, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr);
                nullOutput = CreateFileW(
                    L"NUL", GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE,
                    &security, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr);
                if (nullInput == INVALID_HANDLE_VALUE ||
                    nullOutput == INVALID_HANDLE_VALUE)
                {
                    result.hresult = HRESULT_FROM_WIN32(GetLastError());
                    return result;
                }
                job = CreateJobObjectW(nullptr, nullptr);
                if (!job)
                {
                    result.hresult = HRESULT_FROM_WIN32(GetLastError());
                    return result;
                }
                JOBOBJECT_EXTENDED_LIMIT_INFORMATION jobInfo{};
                jobInfo.BasicLimitInformation.LimitFlags =
                    JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;
                if (!SetInformationJobObject(
                        job, JobObjectExtendedLimitInformation,
                        &jobInfo, sizeof(jobInfo)))
                {
                    result.hresult = HRESULT_FROM_WIN32(GetLastError());
                    return result;
                }
                auto commandLine = BuildCommandLine(executable, arguments);
                STARTUPINFOW startup{};
                startup.cb = sizeof(startup);
                startup.dwFlags = STARTF_USESTDHANDLES;
                startup.hStdInput = nullInput;
                startup.hStdOutput = nullOutput;
                startup.hStdError = stderrWrite;
                if (!CreateProcessW(
                        executable.c_str(), commandLine.data(), nullptr, nullptr,
                        TRUE, CREATE_NO_WINDOW | CREATE_SUSPENDED,
                        nullptr, executable.parent_path().c_str(),
                        &startup, &process))
                {
                    result.hresult = HRESULT_FROM_WIN32(GetLastError());
                    return result;
                }
                result.processStarted = true;
                if (!AssignProcessToJobObject(job, process.hProcess))
                {
                    result.hresult = HRESULT_FROM_WIN32(GetLastError());
                    TerminateProcess(process.hProcess, 1);
                    result.processTreeTerminated = true;
                    return result;
                }
                if (ResumeThread(process.hThread) == static_cast<DWORD>(-1))
                {
                    result.hresult = HRESULT_FROM_WIN32(GetLastError());
                    TerminateJobObject(job, 1);
                    result.processTreeTerminated = true;
                    return result;
                }
                CloseHandle(stderrWrite);
                stderrWrite = nullptr;
                const auto started = std::chrono::steady_clock::now();
                for (;;)
                {
                    AppendPipe(stderrRead, result.stderrText);
                    const auto wait = WaitForSingleObject(process.hProcess, 10);
                    if (wait == WAIT_OBJECT_0) break;
                    if (wait == WAIT_FAILED)
                    {
                        result.hresult = HRESULT_FROM_WIN32(GetLastError());
                        TerminateJobObject(job, 1);
                        result.processTreeTerminated = true;
                        return result;
                    }
                    if (std::chrono::steady_clock::now() - started > timeout)
                    {
                        result.timedOut = true;
                        result.hresult = HRESULT_FROM_WIN32(WAIT_TIMEOUT);
                        TerminateJobObject(job, 1);
                        WaitForSingleObject(process.hProcess, 5'000);
                        result.processTreeTerminated = true;
                        return result;
                    }
                }
                AppendPipe(stderrRead, result.stderrText);
                if (!GetExitCodeProcess(process.hProcess, &result.exitCode))
                {
                    result.hresult = HRESULT_FROM_WIN32(GetLastError());
                    return result;
                }
                result.hresult = result.exitCode == 0
                    ? S_OK : HRESULT_FROM_WIN32(ERROR_PROCESS_ABORTED);
                return result;
            }
            catch (const std::bad_alloc&)
            {
                result.hresult = E_OUTOFMEMORY;
            }
            catch (...)
            {
                result.hresult = E_FAIL;
            }
            if (process.hProcess && result.exitCode == STILL_ACTIVE)
            {

                TerminateJobObject(job, 1);
                result.processTreeTerminated = true;
            }
            return result;
        }

        std::wstring LoudnessNumber(const double value)
        {
            if (!std::isfinite(value))
                throw std::invalid_argument("non-finite loudnorm measurement");
            std::wostringstream stream;
            stream.imbue(std::locale::classic());
            stream << std::fixed << std::setprecision(6) << value;
            return stream.str();
        }

        bool ReadLoudnormJsonNumber(
            const std::string& text,
            const char* const field,
            double& value,
            const bool requireFinite = true) noexcept
        {
            try
            {
                const std::string needle = std::string{ "\"" } + field + "\"";
                auto position = text.rfind(needle);
                if (position == std::string::npos) return false;
                position = text.find(':', position + needle.size());
                if (position == std::string::npos) return false;
                ++position;
                while (position < text.size() &&
                    (text[position] == ' ' || text[position] == '\t' ||
                        text[position] == '\r' || text[position] == '\n' ||
                        text[position] == '"'))
                {
                    ++position;
                }
                errno = 0;
                char* end{};
                value = std::strtod(text.c_str() + position, &end);
                return end != text.c_str() + position && errno != ERANGE &&
                    (!requireFinite || std::isfinite(value));
            }
            catch (...)
            {
                return false;
            }
        }

        bool ParseLoudnormMeasurement(
            const std::string& stderrText,
            GStreamerAudioLoudnessMeasurement& measurement) noexcept
        {
            measurement = {};
            measurement.valid =
                ReadLoudnormJsonNumber(
                    stderrText, "input_i", measurement.integratedLufs) &&
                ReadLoudnormJsonNumber(
                    stderrText, "input_tp", measurement.truePeakDbtp) &&
                ReadLoudnormJsonNumber(
                    stderrText, "input_lra", measurement.loudnessRange) &&
                ReadLoudnormJsonNumber(
                    stderrText, "input_thresh", measurement.threshold) &&
                ReadLoudnormJsonNumber(
                    stderrText, "target_offset", measurement.targetOffset);
            return measurement.valid;
        }

        bool MicrophoneMasteringEligible(
            const GStreamerAudioLoudnessMeasurement& measurement) noexcept
        {
            return measurement.valid &&
                std::isfinite(measurement.integratedLufs) &&
                measurement.integratedLufs >
                    MicrophoneMasteringSafetyFloorLufs;
        }

        bool ProgramMasteringEligible(
            const GStreamerAudioLoudnessMeasurement& measurement) noexcept
        {
            return measurement.valid &&
                std::isfinite(measurement.integratedLufs) &&
                measurement.integratedLufs > ProgramMasteringSafetyFloorLufs;
        }

        std::string DiagnosticLoudnessNumber(const double value)
        {
            if (std::isnan(value)) return "nan";
            if (std::isinf(value)) return std::signbit(value) ? "-inf" : "inf";
            std::ostringstream stream;
            stream.imbue(std::locale::classic());
            stream << std::setprecision(15) << value;
            return stream.str();
        }

        std::wstring LoudnormAnalysisFilter()
        {
            return std::wstring{ LoudnormTarget } + L":print_format=json";
        }

        std::wstring LoudnormSecondPassFilter(
            const GStreamerAudioLoudnessMeasurement& measurement)
        {
            if (!measurement.valid)
                throw std::invalid_argument("missing loudnorm first-pass facts");
            auto filter = std::wstring{ LoudnormTarget } +
                L":measured_I=" + LoudnessNumber(measurement.integratedLufs) +
                L":measured_TP=" + LoudnessNumber(measurement.truePeakDbtp) +
                L":measured_LRA=" + LoudnessNumber(measurement.loudnessRange) +
                L":measured_thresh=" + LoudnessNumber(measurement.threshold) +
                L":offset=" + LoudnessNumber(measurement.targetOffset) +
                L":linear=true";
            return filter;
        }

        std::vector<std::wstring> BuildSingleInputAnalysisArguments(
            const std::filesystem::path& path)
        {
            if (!IsRegularAbsolutePath(path))
                throw std::invalid_argument("invalid loudnorm analysis path");
            return {
                L"-nostdin", L"-hide_banner", L"-loglevel", L"info",
                L"-i", path.wstring(), L"-map", L"0:a:0",
                L"-af", LoudnormAnalysisFilter(),
                L"-f", L"null", L"NUL"
            };
        }

        std::vector<std::wstring> BuildDualProgramAnalysisArguments(
            const GStreamerAudioFinalizeRequest& request,
            const GStreamerAudioLoudnessMeasurement& microphoneMeasurement)
        {
            if (!IsRegularAbsolutePath(request.systemFlacPath) ||
                !IsRegularAbsolutePath(request.microphoneFlacPath))
            {
                throw std::invalid_argument("invalid dual FLAC paths");
            }
            const auto masterMicrophone =
                MicrophoneMasteringEligible(microphoneMeasurement);
            const auto microphoneFilter = masterMicrophone
                ? L"[1:a:0]" + LoudnormSecondPassFilter(
                    microphoneMeasurement) + L"[mic_mastered];"
                : std::wstring{};
            const auto microphoneInput = masterMicrophone
                ? L"[mic_mastered]" : L"[1:a:0]";
            const auto graph = microphoneFilter +
                L"[0:a:0]" + microphoneInput +
                L"amix=inputs=2:weights='1 1':normalize=1"
                L"[program];[program]" + LoudnormAnalysisFilter() +
                L"[measured]";
            return {
                L"-nostdin", L"-hide_banner", L"-loglevel", L"info",
                L"-i", request.systemFlacPath.wstring(),
                L"-i", request.microphoneFlacPath.wstring(),
                L"-filter_complex", graph, L"-map", L"[measured]",
                L"-f", L"null", L"NUL"
            };
        }

        HRESULT MeasureLoudness(
            const std::filesystem::path& executable,
            const std::vector<std::wstring>& arguments,
            const std::chrono::milliseconds timeout,
            FfmpegProcessResult& process,
            GStreamerAudioLoudnessMeasurement& measurement,
            const bool allowNonFiniteIntegratedLufs = false) noexcept
        {
            process = RunFfmpegProcess(executable, arguments, timeout);
            if (FAILED(process.hresult)) return process.hresult;
            if (ParseLoudnormMeasurement(process.stderrText, measurement))
                return S_OK;
            if (allowNonFiniteIntegratedLufs)
            {
                measurement = {};
                measurement.truePeakDbtp =
                    std::numeric_limits<double>::quiet_NaN();
                if (ReadLoudnormJsonNumber(
                        process.stderrText, "input_i",
                        measurement.integratedLufs, false) &&
                    !std::isfinite(measurement.integratedLufs))
                {
                    (void)ReadLoudnormJsonNumber(
                        process.stderrText, "input_tp",
                        measurement.truePeakDbtp, false);
                    measurement.valid = true;
                    return S_OK;
                }
            }
            return HRESULT_FROM_WIN32(ERROR_INVALID_DATA);
        }

        HRESULT NativeStreamFacts(
            IMFSourceReader* reader,
            GStreamerAudioValidationFacts& facts) noexcept
        {
            winrt::com_ptr<IMFMediaType> video;
            auto result = reader->GetNativeMediaType(
                static_cast<DWORD>(MF_SOURCE_READER_FIRST_VIDEO_STREAM),
                0, video.put());
            if (FAILED(result)) return result;
            GUID major{};
            GUID subtype{};
            result = video->GetGUID(MF_MT_MAJOR_TYPE, &major);
            if (FAILED(result)) return result;
            result = video->GetGUID(MF_MT_SUBTYPE, &subtype);
            if (FAILED(result)) return result;
            facts.nativeVideoH264 =
                major == MFMediaType_Video && subtype == MFVideoFormat_H264;

            winrt::com_ptr<IMFMediaType> audio;
            result = reader->GetNativeMediaType(
                static_cast<DWORD>(MF_SOURCE_READER_FIRST_AUDIO_STREAM),
                0, audio.put());
            if (FAILED(result)) return result;
            result = audio->GetGUID(MF_MT_MAJOR_TYPE, &major);
            if (FAILED(result)) return result;
            result = audio->GetGUID(MF_MT_SUBTYPE, &subtype);
            if (FAILED(result)) return result;
            facts.nativeAudioAac =
                major == MFMediaType_Audio && subtype == MFAudioFormat_AAC;
            return facts.nativeVideoH264 && facts.nativeAudioAac
                ? S_OK : MF_E_INVALIDMEDIATYPE;
        }
    }

    std::filesystem::path ResolveGStreamerAudioFfmpegPath() noexcept
    {
        try
        {
            auto candidate = CurrentModuleDirectory() /
                L"ffmpeg" / L"ffmpeg.exe";
            if (!IsRegularAbsolutePath(candidate)) return {};
            std::error_code error;
            candidate = std::filesystem::weakly_canonical(candidate, error);
            return error ? std::filesystem::path{} : candidate;
        }
        catch (...)
        {
            return {};
        }
    }

    bool GStreamerAudioFinalizeStorageSufficient(
        const std::uint64_t freeBytes,
        const std::uint64_t videoBytes) noexcept
    {
        constexpr std::uint64_t safetyMargin = 128ull * 1024ull * 1024ull;
        return videoBytes != 0 &&
            videoBytes <=
                ((std::numeric_limits<std::uint64_t>::max)() - safetyMargin) / 2 &&
            freeBytes >= videoBytes * 2 + safetyMargin;
    }

    std::vector<std::wstring> BuildGStreamerAudioFfmpegArguments(
        const GStreamerAudioFinalizeRequest& request,
        const GStreamerAudioLoudnessMeasurement& microphoneMeasurement,
        const GStreamerAudioLoudnessMeasurement& programMeasurement)
    {
        if (!IsRegularAbsolutePath(request.videoPath) ||
            !request.outputPath.is_absolute() ||
            request.expectedDuration100ns <= 0)
        {
            throw std::invalid_argument(
                "invalid GStreamer audio finalize paths");
        }

        std::vector<std::wstring> arguments{
            L"-nostdin", L"-hide_banner", L"-loglevel", L"error", L"-n",
            L"-i", request.videoPath.wstring()
        };
        switch (request.mode)
        {
        case GStreamerAudioMode::SystemOnly:
            if (!IsRegularAbsolutePath(request.systemFlacPath) ||
                !request.microphoneFlacPath.empty())
            {
                throw std::invalid_argument("invalid SystemOnly FLAC paths");
            }
            arguments.insert(arguments.end(), {
                L"-i", request.systemFlacPath.wstring(),
                L"-map", L"0:v:0", L"-map", L"1:a:0"
            });
            break;
        case GStreamerAudioMode::MicrophoneOnly:
            if (!request.systemFlacPath.empty() ||
                !IsRegularAbsolutePath(request.microphoneFlacPath))
            {
                throw std::invalid_argument("invalid MicrophoneOnly FLAC paths");
            }
            arguments.insert(arguments.end(), {
                L"-i", request.microphoneFlacPath.wstring(),
                L"-map", L"0:v:0", L"-map", L"1:a:0"
            });
            if (MicrophoneMasteringEligible(microphoneMeasurement))
            {
                arguments.insert(arguments.end(), {
                    L"-af", LoudnormSecondPassFilter(
                        microphoneMeasurement)
                });
            }
            break;
        case GStreamerAudioMode::Dual:
        {
            if (!IsRegularAbsolutePath(request.systemFlacPath) ||
                !IsRegularAbsolutePath(request.microphoneFlacPath))
            {
                throw std::invalid_argument("invalid Dual FLAC paths");
            }
            const auto masterMicrophone =
                MicrophoneMasteringEligible(microphoneMeasurement);
            const auto microphoneFilter = masterMicrophone
                ? L"[2:a:0]" + LoudnormSecondPassFilter(
                    microphoneMeasurement) + L"[mic_mastered];"
                : std::wstring{};
            const auto microphoneInput = masterMicrophone
                ? L"[mic_mastered]" : L"[2:a:0]";
            const auto masterProgram = ProgramMasteringEligible(programMeasurement);
            auto graph = microphoneFilter +
                L"[1:a:0]" + microphoneInput +
                L"amix=inputs=2:weights='1 1':normalize=1"
                L"[program]";
            auto programOutput = std::wstring{ L"[program]" };
            if (masterProgram)
            {
                graph.append(L";[program]");
                graph.append(LoudnormSecondPassFilter(programMeasurement));
                graph.append(L"[mastered]");
                programOutput = L"[mastered]";
            }
            arguments.insert(arguments.end(), {
                L"-i", request.systemFlacPath.wstring(),
                L"-i", request.microphoneFlacPath.wstring(),
                L"-filter_complex", graph,
                L"-map", L"0:v:0", L"-map", programOutput
            });
            break;
        }
        default:
            throw std::invalid_argument("invalid GStreamer audio mode");
        }
        arguments.insert(arguments.end(), {
            L"-c:v", L"copy",
            L"-c:a", L"aac", L"-b:a", L"192k",
            L"-ar", L"48000", L"-ac", L"2",
            L"-shortest", L"-movflags", L"+faststart",
            request.outputPath.wstring()
        });
        return arguments;
    }

    GStreamerAudioFinalizeResult FinalizeGStreamerAudio(
        const GStreamerAudioFinalizeRequest& request) noexcept
    {
        GStreamerAudioFinalizeResult result{};
        try
        {
            const auto executable = ResolveGStreamerAudioFfmpegPath();
            if (executable.empty())
            {
                result.hresult = HRESULT_FROM_WIN32(ERROR_FILE_NOT_FOUND);
                return result;
            }
            std::error_code fileError;
            if (std::filesystem::exists(request.outputPath, fileError) ||
                fileError)
            {
                result.hresult = HRESULT_FROM_WIN32(ERROR_FILE_EXISTS);
                return result;
            }

            const auto appendProcessFacts = [&](const char* const stage,
                                                const FfmpegProcessResult& process)
            {
                result.processStarted =
                    result.processStarted || process.processStarted;
                result.timedOut = result.timedOut || process.timedOut;
                result.processTreeTerminated = result.processTreeTerminated ||
                    process.processTreeTerminated;
                result.exitCode = process.exitCode;
                if (!process.stderrText.empty())
                {
                    if (!result.stderrText.empty()) result.stderrText.push_back('\n');
                    result.stderrText.append(stage);
                    result.stderrText.push_back('\n');
                    result.stderrText.append(process.stderrText);
                }
            };

            GStreamerAudioLoudnessMeasurement microphoneMeasurement{};
            GStreamerAudioLoudnessMeasurement programMeasurement{};
            if (request.mode == GStreamerAudioMode::MicrophoneOnly ||
                request.mode == GStreamerAudioMode::Dual)
            {
                FfmpegProcessResult microphoneAnalysis{};
                const auto analysisResult = MeasureLoudness(
                    executable,
                    BuildSingleInputAnalysisArguments(
                        request.microphoneFlacPath),
                    request.timeout,
                    microphoneAnalysis,
                    microphoneMeasurement,
                    true);
                appendProcessFacts("[mic loudnorm pass 1]", microphoneAnalysis);
                if (FAILED(analysisResult))
                {
                    result.hresult = analysisResult;
                    return result;
                }
                result.microphoneMasteringApplied =
                    MicrophoneMasteringEligible(microphoneMeasurement);
                if (!result.stderrText.empty())
                    result.stderrText.push_back('\n');
                result.stderrText.append("[mic mastering decision]\n");
                result.stderrText.append("MIC_MASTERING_DECISION=");
                result.stderrText.append(result.microphoneMasteringApplied
                    ? "NORMALIZE\n" : "BYPASS_NEAR_SILENCE\n");
                result.stderrText.append("MEASURED_I=");
                result.stderrText.append(DiagnosticLoudnessNumber(
                    microphoneMeasurement.integratedLufs));
                result.stderrText.append("\nMEASURED_TP=");
                result.stderrText.append(DiagnosticLoudnessNumber(
                    microphoneMeasurement.truePeakDbtp));
                result.stderrText.push_back('\n');
            }
            if (request.mode == GStreamerAudioMode::Dual)
            {
                FfmpegProcessResult programAnalysis{};
                const auto analysisResult = MeasureLoudness(
                    executable,
                    BuildDualProgramAnalysisArguments(
                        request, microphoneMeasurement),
                    request.timeout,
                    programAnalysis,
                    programMeasurement,
                    true);
                appendProcessFacts("[dual program loudnorm pass 1]", programAnalysis);
                if (FAILED(analysisResult))
                {
                    result.hresult = analysisResult;
                    return result;
                }
                if (!result.stderrText.empty())
                    result.stderrText.push_back('\n');
                result.stderrText.append("[program mastering decision]\n");
                result.stderrText.append("PROGRAM_MASTERING_DECISION=");
                result.stderrText.append(ProgramMasteringEligible(programMeasurement)
                    ? "NORMALIZE\n" : "BYPASS_NEAR_SILENCE\n");
                result.stderrText.append("PROGRAM_MEASURED_I=");
                result.stderrText.append(DiagnosticLoudnessNumber(
                    programMeasurement.integratedLufs));
                result.stderrText.push_back('\n');
                result.dualMixApplied = true;
            }
            const auto finalize = RunFfmpegProcess(
                executable,
                BuildGStreamerAudioFfmpegArguments(
                    request, microphoneMeasurement, programMeasurement),
                request.timeout);
            appendProcessFacts("[AAC/H.264 MP4 finalize]", finalize);
            if (FAILED(finalize.hresult))
            {
                result.hresult = finalize.hresult;
                return result;
            }
            result.outputCreated = IsRegularAbsolutePath(request.outputPath);
            if (!result.outputCreated)
            {
                result.hresult = HRESULT_FROM_WIN32(ERROR_FILE_INVALID);
                return result;
            }
            result.validationHResult = ValidateGStreamerAudioMp4(
                request.outputPath,
                request.expectedDuration100ns,
                result.validation);
            if (SUCCEEDED(result.validationHResult) &&
                (request.mode == GStreamerAudioMode::MicrophoneOnly ||
                    request.mode == GStreamerAudioMode::Dual))
            {
                FfmpegProcessResult finalAnalysis{};
                GStreamerAudioLoudnessMeasurement finalMeasurement{};
                const auto loudnessResult = MeasureLoudness(
                    executable,
                    BuildSingleInputAnalysisArguments(request.outputPath),
                    request.timeout,
                    finalAnalysis,
                    finalMeasurement);
                appendProcessFacts("[final program loudness validation]", finalAnalysis);
                if (FAILED(loudnessResult))
                {
                    result.validation.finalLoudnessValidated = false;
                }
                else
                {
                    result.validation.integratedLufs =
                        finalMeasurement.integratedLufs;
                    result.validation.truePeakDbtp =
                        finalMeasurement.truePeakDbtp;
                    result.validation.finalLoudnessValidated =
                        finalMeasurement.integratedLufs >= -17.0 &&
                        finalMeasurement.integratedLufs <= -15.0 &&
                        finalMeasurement.truePeakDbtp <= -1.5;
                }
            }
            result.validated = SUCCEEDED(result.validationHResult);
            result.hresult = result.validationHResult;
        }
        catch (const std::bad_alloc&)
        {
            result.hresult = E_OUTOFMEMORY;
        }
        catch (const std::invalid_argument&)
        {
            result.hresult = E_INVALIDARG;
        }
        catch (...)
        {
            result.hresult = E_FAIL;
        }
        return result;
    }

    HRESULT ValidateGStreamerAudioMp4(
        const std::filesystem::path& path,
        const std::int64_t expectedDuration100ns,
        GStreamerAudioValidationFacts& facts) noexcept
    {
        facts = {};
        bool mfStarted{};
        try
        {
            winrt::check_hresult(MFStartup(MF_VERSION, MFSTARTUP_FULL));
            mfStarted = true;
            winrt::com_ptr<IMFSourceReader> reader;
            winrt::check_hresult(MFCreateSourceReaderFromURL(
                path.c_str(), nullptr, reader.put()));
            winrt::check_hresult(NativeStreamFacts(reader.get(), facts));

            winrt::com_ptr<IMFMediaType> decodedAudio;
            winrt::check_hresult(MFCreateMediaType(decodedAudio.put()));
            winrt::check_hresult(decodedAudio->SetGUID(
                MF_MT_MAJOR_TYPE, MFMediaType_Audio));
            winrt::check_hresult(decodedAudio->SetGUID(
                MF_MT_SUBTYPE, MFAudioFormat_PCM));
            winrt::check_hresult(decodedAudio->SetUINT32(
                MF_MT_AUDIO_NUM_CHANNELS, 2));
            winrt::check_hresult(decodedAudio->SetUINT32(
                MF_MT_AUDIO_SAMPLES_PER_SECOND, 48'000));
            winrt::check_hresult(decodedAudio->SetUINT32(
                MF_MT_AUDIO_BITS_PER_SAMPLE, 16));
            winrt::check_hresult(reader->SetCurrentMediaType(
                static_cast<DWORD>(MF_SOURCE_READER_FIRST_AUDIO_STREAM),
                nullptr, decodedAudio.get()));
            winrt::com_ptr<IMFMediaType> negotiatedAudio;
            winrt::check_hresult(reader->GetCurrentMediaType(
                static_cast<DWORD>(MF_SOURCE_READER_FIRST_AUDIO_STREAM),
                negotiatedAudio.put()));
            winrt::check_hresult(negotiatedAudio->GetUINT32(
                MF_MT_AUDIO_SAMPLES_PER_SECOND, &facts.sampleRate));
            winrt::check_hresult(negotiatedAudio->GetUINT32(
                MF_MT_AUDIO_NUM_CHANNELS, &facts.channels));
            if (facts.sampleRate != 48'000 || facts.channels != 2)
                winrt::throw_hresult(MF_E_INVALIDMEDIATYPE);

            long double sum{};
            long double sumSquares{};
            std::uint64_t sampleCount{};
            std::int64_t lastEnd{};
            for (;;)
            {
                DWORD streamIndex{};
                DWORD flags{};
                LONGLONG timestamp{};
                winrt::com_ptr<IMFSample> sample;
                winrt::check_hresult(reader->ReadSample(
                    static_cast<DWORD>(MF_SOURCE_READER_FIRST_AUDIO_STREAM), 0,
                    &streamIndex, &flags, &timestamp, sample.put()));
                if (sample)
                {
                    LONGLONG duration{};
                    if (SUCCEEDED(sample->GetSampleDuration(&duration)))
                        lastEnd = (std::max)(lastEnd, timestamp + duration);
                    winrt::com_ptr<IMFMediaBuffer> buffer;
                    winrt::check_hresult(sample->ConvertToContiguousBuffer(
                        buffer.put()));
                    BYTE* bytes{};
                    DWORD length{};
                    winrt::check_hresult(buffer->Lock(&bytes, nullptr, &length));
                    if (length % sizeof(std::int16_t) != 0)
                    {
                        (void)buffer->Unlock();
                        winrt::throw_hresult(MF_E_INVALIDMEDIATYPE);
                    }
                    const auto* pcm = reinterpret_cast<const std::int16_t*>(bytes);
                    const auto count = length / sizeof(std::int16_t);
                    for (DWORD index = 0; index < count; ++index)
                    {
                        const auto value = static_cast<std::int32_t>(pcm[index]);
                        const auto magnitude = static_cast<std::uint32_t>(
                            value < 0 ? -value : value);
                        facts.peakAbsolutePcm16 = (std::max)(
                            facts.peakAbsolutePcm16, magnitude);
                        facts.saturatedSamples += magnitude >= 32'767 ? 1u : 0u;
                        sum += value;
                        sumSquares += static_cast<long double>(value) * value;
                    }
                    sampleCount += count;
                    winrt::check_hresult(buffer->Unlock());
                }
                if ((flags & MF_SOURCE_READERF_ENDOFSTREAM) != 0)
                {
                    facts.audioReachedEndOfStream = true;
                    break;
                }
            }
            facts.decodedFrames = sampleCount / facts.channels;
            facts.audioDuration100ns = lastEnd;
            if (sampleCount != 0)
            {
                facts.rmsPcm16 = std::sqrt(static_cast<double>(
                    sumSquares / sampleCount));
                facts.dcPcm16 = static_cast<double>(sum / sampleCount);
            }
            if (!facts.audioReachedEndOfStream || facts.decodedFrames == 0 ||
                facts.peakAbsolutePcm16 == 0 || facts.rmsPcm16 <= 0.0 ||
                !std::isfinite(facts.rmsPcm16) ||
                !std::isfinite(facts.dcPcm16) ||
                facts.saturatedSamples != 0 ||
                std::abs(facts.dcPcm16) > 2048.0 ||
                std::abs(facts.audioDuration100ns - expectedDuration100ns) >
                    DurationTolerance100ns)
                winrt::throw_hresult(HRESULT_FROM_WIN32(ERROR_INVALID_DATA));

            winrt::com_ptr<IMFSourceReader> videoReader;
            winrt::check_hresult(MFCreateSourceReaderFromURL(
                path.c_str(), nullptr, videoReader.put()));
            winrt::com_ptr<IMFMediaType> decodedVideo;
            winrt::check_hresult(MFCreateMediaType(decodedVideo.put()));
            winrt::check_hresult(decodedVideo->SetGUID(
                MF_MT_MAJOR_TYPE, MFMediaType_Video));
            winrt::check_hresult(decodedVideo->SetGUID(
                MF_MT_SUBTYPE, MFVideoFormat_NV12));
            winrt::check_hresult(videoReader->SetCurrentMediaType(
                static_cast<DWORD>(MF_SOURCE_READER_FIRST_VIDEO_STREAM),
                nullptr, decodedVideo.get()));
            for (std::uint32_t index = 0; index < 8; ++index)
            {
                DWORD streamIndex{};
                DWORD flags{};
                LONGLONG timestamp{};
                winrt::com_ptr<IMFSample> sample;
                winrt::check_hresult(videoReader->ReadSample(
                    static_cast<DWORD>(MF_SOURCE_READER_FIRST_VIDEO_STREAM), 0,
                    &streamIndex, &flags, &timestamp, sample.put()));
                facts.decodedVideoSamples += sample ? 1u : 0u;
                if ((flags & MF_SOURCE_READERF_ENDOFSTREAM) != 0) break;
            }
            facts.videoDecoded = facts.decodedVideoSamples != 0;
            if (!facts.videoDecoded)
                winrt::throw_hresult(HRESULT_FROM_WIN32(ERROR_INVALID_DATA));
            MFShutdown();
            return S_OK;
        }
        catch (const winrt::hresult_error& error)
        {
            if (mfStarted) MFShutdown();
            return error.code();
        }
        catch (...)
        {
            if (mfStarted) MFShutdown();
            return E_FAIL;
        }
    }
}
