#pragma once

#include "XbPreviewApi.h"

#include <filesystem>
#include <fstream>
#include <functional>
#include <mutex>
#include <optional>
#include <string>
#include <string_view>
#include <type_traits>
#include <vector>

#include <winrt/base.h>

namespace xbpreview
{
    struct StartupStepDescriptor
    {
        std::string stage;
        std::string operation;
        std::string apiName;
        std::string sourceFile;
        std::uint32_t sourceLine{};
        std::optional<std::uint32_t> deviceFlagsRequested;
        std::optional<std::uint32_t> attemptIndex;
        std::optional<std::string> fallbackFrom;
    };

    struct StartupDiagnosticRecord
    {
        std::string event;
        std::optional<std::string> sessionGuid;
        StartupStepDescriptor step;
        std::uint32_t threadId{};
        std::int64_t qpc{};
        std::string utc;
        double elapsedMilliseconds{};
        std::optional<bool> encoderEnabled;
        std::string result;
        std::optional<HRESULT> hresult;
        std::optional<std::uint32_t> win32Code;
        std::optional<std::string> exceptionType;
        std::optional<std::string> exceptionMessage;
    };

    struct StartupDiagnosticSnapshot
    {
        std::optional<std::string> sessionGuid;
        std::string lastCompletedStage{ "unknown" };
        std::string activeStage{ "Uninstrumented" };
        std::string activeOperation{ "unknown" };
        std::string activeApiName{ "unknown" };
        std::string activeSourceFile{ "unknown" };
        std::uint32_t activeSourceLine{};
        std::optional<HRESULT> originalHresult;
        std::string originalExceptionType{ "unknown" };
        std::string originalExceptionMessage{ "unknown" };
        std::uint32_t workerThreadId{};
        std::optional<bool> encoderEnabled;
        std::optional<std::uint32_t> requestedDeviceFlags;
        bool fallbackAttempted{};
        std::string fallbackResult{ "not-applicable" };
        bool cleanupStarted{};
        bool cleanupCompleted{};
    };

    class DiagnosticLogger final
    {
    public:
        DiagnosticLogger() = default;
        DiagnosticLogger(const DiagnosticLogger&) = delete;
        DiagnosticLogger& operator=(const DiagnosticLogger&) = delete;

        bool Open(
            const std::wstring& directory,
            const std::wstring& sessionId,
            std::wstring& error);

        void WriteEvent(
            std::string_view eventName,
            XbPreviewState state,
            std::string_view detail = {});

        void WriteStartupDiagnostic(const StartupDiagnosticRecord& record);
        void WriteStartupSummary(const StartupDiagnosticSnapshot& snapshot);

        void WriteSummary(const XbPreviewStats& stats);
        void Close();

        [[nodiscard]] std::wstring FilePath() const;

        static std::string UtcTimestamp();
        static std::string ToUtf8(std::wstring_view value);

    private:
        void CloseUnlocked();
        static std::string Escape(std::string_view value);

        mutable std::mutex mutex_;
        std::ofstream stream_;
        std::wstring filePath_;
    };

    class StartupDiagnostics final
    {
    public:
        StartupDiagnostics() = default;
        StartupDiagnostics(const StartupDiagnostics&) = delete;
        StartupDiagnostics& operator=(const StartupDiagnostics&) = delete;

        void Reset();
        void Attach(
            DiagnosticLogger& logger,
            std::string sessionGuid);
        void SetEncoderEnabled(bool enabled);

        template<typename Action>
        decltype(auto) Run(
            StartupStepDescriptor step,
            Action&& action)
        {
            const auto startedQpc = Begin(step);
            try
            {
                if constexpr (std::is_void_v<std::invoke_result_t<Action>>)
                {
                    std::invoke(std::forward<Action>(action));
                    Succeed(step, startedQpc, std::nullopt);
                    return;
                }
                else
                {
                    decltype(auto) result =
                        std::invoke(std::forward<Action>(action));
                    Succeed(step, startedQpc, std::nullopt);
                    return result;
                }
            }
            catch (const winrt::hresult_error& error)
            {
                Fail(
                    step,
                    startedQpc,
                    error.code(),
                    "winrt::hresult_error",
                    DiagnosticLogger::ToUtf8(error.message()));
                throw;
            }
            catch (const std::exception& error)
            {
                Fail(
                    step,
                    startedQpc,
                    std::nullopt,
                    "std::exception",
                    error.what());
                throw;
            }
            catch (...)
            {
                Fail(
                    step,
                    startedQpc,
                    std::nullopt,
                    "unknown",
                    "unknown");
                throw;
            }
        }

        template<typename Action>
        HRESULT RunHresult(
            StartupStepDescriptor step,
            Action&& action)
        {
            const auto startedQpc = Begin(step);
            try
            {
                const HRESULT result =
                    std::invoke(std::forward<Action>(action));
                if (FAILED(result))
                {
                    Fail(
                        step,
                        startedQpc,
                        result,
                        "HRESULT",
                        "not-applicable");
                }
                else
                {
                    Succeed(step, startedQpc, result);
                }
                return result;
            }
            catch (const winrt::hresult_error& error)
            {
                Fail(
                    step,
                    startedQpc,
                    error.code(),
                    "winrt::hresult_error",
                    DiagnosticLogger::ToUtf8(error.message()));
                throw;
            }
            catch (const std::exception& error)
            {
                Fail(
                    step,
                    startedQpc,
                    std::nullopt,
                    "std::exception",
                    error.what());
                throw;
            }
            catch (...)
            {
                Fail(
                    step,
                    startedQpc,
                    std::nullopt,
                    "unknown",
                    "unknown");
                throw;
            }
        }

        void FallbackBegin(StartupStepDescriptor step);
        void FallbackSuccess(
            StartupStepDescriptor step,
            HRESULT result);
        void FallbackFailure(
            StartupStepDescriptor step,
            HRESULT result);
        void CleanupStarted();
        void CleanupCompleted();
        void CaptureUnhandled(
            std::optional<HRESULT> result,
            std::string exceptionType,
            std::string exceptionMessage);
        void WriteSummary();
        [[nodiscard]] StartupDiagnosticSnapshot Snapshot() const;

    private:
        std::int64_t Begin(const StartupStepDescriptor& step);
        void Succeed(
            const StartupStepDescriptor& step,
            std::int64_t startedQpc,
            std::optional<HRESULT> result);
        void Fail(
            const StartupStepDescriptor& step,
            std::int64_t startedQpc,
            std::optional<HRESULT> result,
            std::string exceptionType,
            std::string exceptionMessage);
        void Emit(StartupDiagnosticRecord record);
        static std::int64_t QueryQpc() noexcept;
        static double ElapsedMilliseconds(
            std::int64_t startedQpc,
            std::int64_t endedQpc) noexcept;
        static std::optional<std::uint32_t> Win32Code(
            std::optional<HRESULT> result) noexcept;

        mutable std::mutex mutex_;
        DiagnosticLogger* logger_{};
        std::optional<std::string> sessionGuid_;
        std::optional<bool> encoderEnabled_;
        StartupDiagnosticSnapshot snapshot_;
        std::vector<StartupDiagnosticRecord> pending_;
    };
}

#define XB_STARTUP_STEP(diagnostics, stage, operation, apiName, action) \
    (diagnostics).Run( \
        xbpreview::StartupStepDescriptor{ \
            (stage), (operation), (apiName), __FILE__, \
            static_cast<std::uint32_t>(__LINE__) }, \
        (action))

#define XB_STARTUP_HRESULT_STEP( \
    diagnostics, stage, operation, apiName, flags, attempt, fallback, action) \
    (diagnostics).RunHresult( \
        xbpreview::StartupStepDescriptor{ \
            (stage), (operation), (apiName), __FILE__, \
            static_cast<std::uint32_t>(__LINE__), \
            (flags), (attempt), (fallback) }, \
        (action))
