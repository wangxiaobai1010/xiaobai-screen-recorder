#include "DiagnosticLogger.h"

#include <windows.h>

#include <array>
#include <iomanip>
#include <sstream>

namespace
{
    void WriteOptionalString(
        std::ostream& stream,
        const std::optional<std::string>& value,
        const std::function<std::string(std::string_view)>& escape)
    {
        if (value)
        {
            stream << '"' << escape(*value) << '"';
        }
        else
        {
            stream << "null";
        }
    }

    void WriteOptionalUInt(
        std::ostream& stream,
        const std::optional<std::uint32_t> value)
    {
        if (value)
        {
            stream << *value;
        }
        else
        {
            stream << "null";
        }
    }

    void WriteOptionalBool(
        std::ostream& stream,
        const std::optional<bool> value)
    {
        if (value)
        {
            stream << (*value ? "true" : "false");
        }
        else
        {
            stream << "null";
        }
    }

    void WriteOptionalHresult(
        std::ostream& stream,
        const std::optional<HRESULT> value)
    {
        if (value)
        {
            std::ostringstream text;
            text << "0x" << std::uppercase << std::hex
                << std::setw(8) << std::setfill('0')
                << static_cast<std::uint32_t>(*value);
            stream << '"' << text.str() << '"';
        }
        else
        {
            stream << "null";
        }
    }
}

namespace xbpreview
{
    bool DiagnosticLogger::Open(
        const std::wstring& directory,
        const std::wstring& sessionId,
        std::wstring& error)
    {
        std::lock_guard lock(mutex_);
        CloseUnlocked();
        if (directory.empty())
        {
            return true;
        }

        try
        {
            const std::filesystem::path root(directory);
            std::filesystem::create_directories(root);

            SYSTEMTIME now{};
            GetSystemTime(&now);
            std::wostringstream fileName;
            fileName << L"p0-"
                << std::setfill(L'0')
                << std::setw(4) << now.wYear
                << std::setw(2) << now.wMonth
                << std::setw(2) << now.wDay << L"-"
                << std::setw(2) << now.wHour
                << std::setw(2) << now.wMinute
                << std::setw(2) << now.wSecond << L"-"
                << sessionId << L".jsonl";

            const auto path = root / fileName.str();
            stream_.open(path, std::ios::out | std::ios::binary | std::ios::trunc);
            if (!stream_)
            {
                error = L"无法创建诊断日志文件。";
                return false;
            }

            filePath_ = path.wstring();
            return true;
        }
        catch (const std::exception&)
        {
            error = L"创建诊断日志目录时发生异常。";
            return false;
        }
    }

    void DiagnosticLogger::WriteEvent(
        const std::string_view eventName,
        const XbPreviewState state,
        const std::string_view detail)
    {
        std::lock_guard lock(mutex_);
        if (!stream_)
        {
            return;
        }

        stream_ << "{\"type\":\"event\",\"timestamp\":\""
            << UtcTimestamp()
            << "\",\"event\":\"" << Escape(eventName)
            << "\",\"state\":" << static_cast<std::int32_t>(state)
            << ",\"detail\":\"" << Escape(detail)
            << "\"}\n";
        stream_.flush();
    }

    void DiagnosticLogger::WriteStartupDiagnostic(
        const StartupDiagnosticRecord& record)
    {
        std::lock_guard lock(mutex_);
        if (!stream_)
        {
            return;
        }

        const auto escape = [](const std::string_view value)
        {
            return Escape(value);
        };
        stream_ << std::fixed << std::setprecision(3)
            << "{\"type\":\"startup-diagnostic\""
            << ",\"SessionGuid\":";
        WriteOptionalString(stream_, record.sessionGuid, escape);
        stream_ << ",\"Event\":\"" << Escape(record.event) << '"'
            << ",\"Stage\":\"" << Escape(record.step.stage) << '"'
            << ",\"Operation\":\"" << Escape(record.step.operation) << '"'
            << ",\"ApiName\":\"" << Escape(record.step.apiName) << '"'
            << ",\"SourceFile\":\"" << Escape(record.step.sourceFile) << '"'
            << ",\"SourceLine\":" << record.step.sourceLine
            << ",\"ThreadId\":" << record.threadId
            << ",\"Qpc\":" << record.qpc
            << ",\"Utc\":\"" << Escape(record.utc) << '"'
            << ",\"ElapsedMs\":" << record.elapsedMilliseconds
            << ",\"EncoderEnabled\":";
        WriteOptionalBool(stream_, record.encoderEnabled);
        stream_ << ",\"DeviceFlagsRequested\":";
        WriteOptionalUInt(stream_, record.step.deviceFlagsRequested);
        stream_ << ",\"AttemptIndex\":";
        WriteOptionalUInt(stream_, record.step.attemptIndex);
        stream_ << ",\"FallbackFrom\":";
        WriteOptionalString(stream_, record.step.fallbackFrom, escape);
        stream_ << ",\"Result\":\"" << Escape(record.result) << '"'
            << ",\"HResultHex\":";
        WriteOptionalHresult(stream_, record.hresult);
        stream_ << ",\"Win32Code\":";
        WriteOptionalUInt(stream_, record.win32Code);
        stream_ << ",\"ExceptionType\":";
        WriteOptionalString(stream_, record.exceptionType, escape);
        stream_ << ",\"ExceptionMessage\":";
        WriteOptionalString(stream_, record.exceptionMessage, escape);
        stream_ << "}\n";
        stream_.flush();
    }

    void DiagnosticLogger::WriteStartupSummary(
        const StartupDiagnosticSnapshot& snapshot)
    {
        std::lock_guard lock(mutex_);
        if (!stream_)
        {
            return;
        }

        stream_ << "{\"type\":\"startup-summary\""
            << ",\"SessionGuid\":";
        WriteOptionalString(stream_, snapshot.sessionGuid, [](const std::string_view value)
        {
            return Escape(value);
        });
        stream_ << ",\"Utc\":\"" << UtcTimestamp() << '"'
            << ",\"LastCompletedStage\":\""
            << Escape(snapshot.lastCompletedStage) << '"'
            << ",\"ActiveStage\":\"" << Escape(snapshot.activeStage) << '"'
            << ",\"ActiveOperation\":\""
            << Escape(snapshot.activeOperation) << '"'
            << ",\"ActiveApiName\":\""
            << Escape(snapshot.activeApiName) << '"'
            << ",\"ActiveSourceFile\":\""
            << Escape(snapshot.activeSourceFile) << '"'
            << ",\"ActiveSourceLine\":" << snapshot.activeSourceLine
            << ",\"OriginalHResult\":";
        WriteOptionalHresult(stream_, snapshot.originalHresult);
        stream_ << ",\"OriginalExceptionType\":\""
            << Escape(snapshot.originalExceptionType) << '"'
            << ",\"OriginalExceptionMessage\":\""
            << Escape(snapshot.originalExceptionMessage) << '"'
            << ",\"WorkerThreadId\":" << snapshot.workerThreadId
            << ",\"EncoderEnabled\":";
        WriteOptionalBool(stream_, snapshot.encoderEnabled);
        stream_ << ",\"RequestedDeviceFlags\":";
        WriteOptionalUInt(stream_, snapshot.requestedDeviceFlags);
        stream_ << ",\"FallbackAttempted\":"
            << (snapshot.fallbackAttempted ? "true" : "false")
            << ",\"FallbackResult\":\""
            << Escape(snapshot.fallbackResult) << '"'
            << ",\"CleanupStarted\":"
            << (snapshot.cleanupStarted ? "true" : "false")
            << ",\"CleanupCompleted\":"
            << (snapshot.cleanupCompleted ? "true" : "false")
            << "}\n";
        stream_.flush();
    }

    void DiagnosticLogger::WriteSummary(const XbPreviewStats& stats)
    {
        std::lock_guard lock(mutex_);
        if (!stream_)
        {
            return;
        }

        double arrivalToPresentLatencyMilliseconds{};
        LARGE_INTEGER frequency{};
        if (stats.lastPresentAfterQpc >= stats.lastFrameArrivalQpc &&
            stats.lastFrameArrivalQpc > 0 &&
            QueryPerformanceFrequency(&frequency) &&
            frequency.QuadPart > 0)
        {
            arrivalToPresentLatencyMilliseconds =
                static_cast<double>(
                    stats.lastPresentAfterQpc - stats.lastFrameArrivalQpc) *
                1000.0 /
                static_cast<double>(frequency.QuadPart);
        }

        stream_ << std::fixed << std::setprecision(3)
            << "{\"type\":\"summary\""
            << ",\"timestamp\":\"" << UtcTimestamp() << "\""
            << ",\"sessionIdHigh\":" << stats.sessionIdHigh
            << ",\"sessionIdLow\":" << stats.sessionIdLow
            << ",\"state\":" << stats.state
            << ",\"flags\":" << stats.flags
            << ",\"captureFrameCount\":" << stats.captureFrameCount
            << ",\"presentFrameCount\":" << stats.presentFrameCount
            << ",\"droppedFrameCount\":" << stats.droppedFrameCount
            << ",\"captureFps\":" << stats.captureFps
            << ",\"presentFps\":" << stats.presentFps
            << ",\"wgcSystemRelativeTime100ns\":" << stats.lastSystemRelativeTime100ns
            << ",\"frameArrivalQpc\":" << stats.lastFrameArrivalQpc
            << ",\"presentBeforeQpc\":" << stats.lastPresentBeforeQpc
            << ",\"presentAfterQpc\":" << stats.lastPresentAfterQpc
            << ",\"arrivalToPresentLatencyMs\":"
            << arrivalToPresentLatencyMilliseconds
            << ",\"estimatedSoftwareLatencyMs\":" << stats.recentLatencyMilliseconds
            << ",\"p50LatencyMs\":" << stats.p50LatencyMilliseconds
            << ",\"p95LatencyMs\":" << stats.p95LatencyMilliseconds
            << ",\"maxLatencyMs\":" << stats.maxLatencyMilliseconds
            << ",\"captureWidth\":" << stats.captureWidth
            << ",\"captureHeight\":" << stats.captureHeight
            << ",\"previewWidth\":" << stats.previewWidth
            << ",\"previewHeight\":" << stats.previewHeight
            << ",\"framePoolRecreateCount\":" << stats.framePoolRecreateCount
            << ",\"swapChainResizeCount\":" << stats.swapChainResizeCount
            << ",\"workingSetBytes\":" << stats.workingSetBytes
            << ",\"privateBytes\":" << stats.privateBytes
            << ",\"cameraUpdateCount\":" << stats.cameraUpdateCount
            << ",\"invalidCameraStateFallbackCount\":"
            << stats.invalidCameraStateFallbackCount
            << ",\"nativeLastAppliedSequence\":"
            << stats.nativeLastAppliedSequence
            << ",\"cameraUpdateRate\":" << stats.cameraUpdateRate
            << ",\"nativeAppliedZoom\":" << stats.nativeAppliedZoom
            << ",\"nativeAppliedCenterX\":" << stats.nativeAppliedCenterX
            << ",\"nativeAppliedCenterY\":" << stats.nativeAppliedCenterY
            << ",\"nativeAppliedMode\":" << stats.nativeAppliedMode
            << ",\"nativeCameraEnabled\":" << stats.nativeCameraEnabled
            << ",\"deviceRemovedReason\":" << stats.deviceRemovedReason
            << ",\"wdaResult\":" << stats.wdaResult
            << ",\"wdaLastError\":" << stats.wdaLastError
            << ",\"usedWarp\":" << stats.usedWarp
            << ",\"hdrDetected\":" << stats.hdrDetected
            << ",\"adapter\":\"" << Escape(ToUtf8(stats.adapterName))
            << "\"}\n";
        stream_.flush();
    }

    void DiagnosticLogger::Close()
    {
        std::lock_guard lock(mutex_);
        CloseUnlocked();
    }

    std::wstring DiagnosticLogger::FilePath() const
    {
        std::lock_guard lock(mutex_);
        return filePath_;
    }

    void DiagnosticLogger::CloseUnlocked()
    {
        if (stream_)
        {
            stream_.flush();
            stream_.close();
        }

        filePath_.clear();
    }

    std::string DiagnosticLogger::UtcTimestamp()
    {
        SYSTEMTIME now{};
        GetSystemTime(&now);
        std::ostringstream stream;
        stream << std::setfill('0')
            << std::setw(4) << now.wYear << '-'
            << std::setw(2) << now.wMonth << '-'
            << std::setw(2) << now.wDay << 'T'
            << std::setw(2) << now.wHour << ':'
            << std::setw(2) << now.wMinute << ':'
            << std::setw(2) << now.wSecond << '.'
            << std::setw(3) << now.wMilliseconds << 'Z';
        return stream.str();
    }

    std::string DiagnosticLogger::Escape(const std::string_view value)
    {
        std::string escaped;
        escaped.reserve(value.size());
        for (const char character : value)
        {
            switch (character)
            {
            case '\\':
                escaped += "\\\\";
                break;
            case '"':
                escaped += "\\\"";
                break;
            case '\n':
                escaped += "\\n";
                break;
            case '\r':
                escaped += "\\r";
                break;
            case '\t':
                escaped += "\\t";
                break;
            default:
                escaped += character;
                break;
            }
        }

        return escaped;
    }

    std::string DiagnosticLogger::ToUtf8(const std::wstring_view value)
    {
        if (value.empty())
        {
            return {};
        }

        const auto required = WideCharToMultiByte(
            CP_UTF8,
            WC_ERR_INVALID_CHARS,
            value.data(),
            static_cast<int>(value.size()),
            nullptr,
            0,
            nullptr,
            nullptr);
        if (required <= 0)
        {
            return {};
        }

        std::string result(static_cast<std::size_t>(required), '\0');
        WideCharToMultiByte(
            CP_UTF8,
            WC_ERR_INVALID_CHARS,
            value.data(),
            static_cast<int>(value.size()),
            result.data(),
            required,
            nullptr,
            nullptr);
        return result;
    }

    void StartupDiagnostics::Reset()
    {
        std::lock_guard lock(mutex_);
        logger_ = nullptr;
        sessionGuid_.reset();
        encoderEnabled_.reset();
        snapshot_ = {};
        pending_.clear();
    }

    void StartupDiagnostics::Attach(
        DiagnosticLogger& logger,
        std::string sessionGuid)
    {
        std::vector<StartupDiagnosticRecord> pending;
        {
            std::lock_guard lock(mutex_);
            logger_ = &logger;
            sessionGuid_ = std::move(sessionGuid);
            snapshot_.sessionGuid = sessionGuid_;
            for (auto& record : pending_)
            {
                record.sessionGuid = sessionGuid_;
            }
            pending.swap(pending_);
        }
        for (const auto& record : pending)
        {
            logger.WriteStartupDiagnostic(record);
        }
    }

    void StartupDiagnostics::SetEncoderEnabled(const bool enabled)
    {
        std::lock_guard lock(mutex_);
        encoderEnabled_ = enabled;
        snapshot_.encoderEnabled = enabled;
    }

    std::int64_t StartupDiagnostics::Begin(
        const StartupStepDescriptor& step)
    {
        const auto qpc = QueryQpc();
        StartupDiagnosticRecord record{};
        record.event = "startup-step-begin";
        record.step = step;
        record.threadId = GetCurrentThreadId();
        record.qpc = qpc;
        record.utc = DiagnosticLogger::UtcTimestamp();
        record.result = "begin";
        {
            std::lock_guard lock(mutex_);
            snapshot_.activeStage = step.stage;
            snapshot_.activeOperation = step.operation;
            snapshot_.activeApiName = step.apiName;
            snapshot_.activeSourceFile = step.sourceFile;
            snapshot_.activeSourceLine = step.sourceLine;
            snapshot_.workerThreadId = record.threadId;
            snapshot_.requestedDeviceFlags = step.deviceFlagsRequested;
        }
        Emit(std::move(record));
        return qpc;
    }

    void StartupDiagnostics::Succeed(
        const StartupStepDescriptor& step,
        const std::int64_t startedQpc,
        const std::optional<HRESULT> result)
    {
        const auto qpc = QueryQpc();
        StartupDiagnosticRecord record{};
        record.event = "startup-step-success";
        record.step = step;
        record.threadId = GetCurrentThreadId();
        record.qpc = qpc;
        record.utc = DiagnosticLogger::UtcTimestamp();
        record.elapsedMilliseconds = ElapsedMilliseconds(startedQpc, qpc);
        record.result = "success";
        record.hresult = result;
        record.win32Code = Win32Code(result);
        {
            std::lock_guard lock(mutex_);
            snapshot_.lastCompletedStage = step.stage;
            snapshot_.activeStage = "not-applicable";
            snapshot_.activeOperation = "not-applicable";
            snapshot_.activeApiName = "not-applicable";
            snapshot_.activeSourceFile = "not-applicable";
            snapshot_.activeSourceLine = 0;
        }
        Emit(std::move(record));
    }

    void StartupDiagnostics::Fail(
        const StartupStepDescriptor& step,
        const std::int64_t startedQpc,
        const std::optional<HRESULT> result,
        std::string exceptionType,
        std::string exceptionMessage)
    {
        const auto qpc = QueryQpc();
        StartupDiagnosticRecord record{};
        record.event = "startup-step-failure";
        record.step = step;
        record.threadId = GetCurrentThreadId();
        record.qpc = qpc;
        record.utc = DiagnosticLogger::UtcTimestamp();
        record.elapsedMilliseconds = ElapsedMilliseconds(startedQpc, qpc);
        record.result = "failure";
        record.hresult = result;
        record.win32Code = Win32Code(result);
        record.exceptionType = exceptionType;
        record.exceptionMessage = exceptionMessage;
        {
            std::lock_guard lock(mutex_);
            if (!snapshot_.originalHresult &&
                snapshot_.originalExceptionType == "unknown")
            {
                snapshot_.activeStage = step.stage;
                snapshot_.activeOperation = step.operation;
                snapshot_.activeApiName = step.apiName;
                snapshot_.activeSourceFile = step.sourceFile;
                snapshot_.activeSourceLine = step.sourceLine;
                snapshot_.originalHresult = result;
                snapshot_.originalExceptionType = exceptionType;
                snapshot_.originalExceptionMessage = exceptionMessage;
            }
        }
        Emit(std::move(record));
    }

    void StartupDiagnostics::FallbackBegin(StartupStepDescriptor step)
    {
        StartupDiagnosticRecord record{};
        record.event = "startup-fallback-begin";
        record.step = std::move(step);
        record.threadId = GetCurrentThreadId();
        record.qpc = QueryQpc();
        record.utc = DiagnosticLogger::UtcTimestamp();
        record.result = "begin";
        {
            std::lock_guard lock(mutex_);
            snapshot_.fallbackAttempted = true;
            snapshot_.fallbackResult = "pending";
        }
        Emit(std::move(record));
    }

    void StartupDiagnostics::FallbackSuccess(
        StartupStepDescriptor step,
        const HRESULT result)
    {
        StartupDiagnosticRecord record{};
        record.event = "startup-fallback-success";
        record.step = std::move(step);
        record.threadId = GetCurrentThreadId();
        record.qpc = QueryQpc();
        record.utc = DiagnosticLogger::UtcTimestamp();
        record.result = "success";
        record.hresult = result;
        record.win32Code = Win32Code(result);
        {
            std::lock_guard lock(mutex_);
            snapshot_.fallbackResult = "success";
            snapshot_.lastCompletedStage = record.step.stage;
            snapshot_.activeStage = "not-applicable";
            snapshot_.activeOperation = "not-applicable";
            snapshot_.activeApiName = "not-applicable";
            snapshot_.activeSourceFile = "not-applicable";
            snapshot_.activeSourceLine = 0;
            snapshot_.originalHresult.reset();
            snapshot_.originalExceptionType = "unknown";
            snapshot_.originalExceptionMessage = "unknown";
        }
        Emit(std::move(record));
    }

    void StartupDiagnostics::FallbackFailure(
        StartupStepDescriptor step,
        const HRESULT result)
    {
        StartupDiagnosticRecord record{};
        record.event = "startup-fallback-failure";
        record.step = std::move(step);
        record.threadId = GetCurrentThreadId();
        record.qpc = QueryQpc();
        record.utc = DiagnosticLogger::UtcTimestamp();
        record.result = "failure";
        record.hresult = result;
        record.win32Code = Win32Code(result);
        record.exceptionType = "HRESULT";
        record.exceptionMessage = "not-applicable";
        {
            std::lock_guard lock(mutex_);
            snapshot_.fallbackResult = "failure";
            snapshot_.activeStage = record.step.stage;
            snapshot_.activeOperation = record.step.operation;
            snapshot_.activeApiName = record.step.apiName;
            snapshot_.activeSourceFile = record.step.sourceFile;
            snapshot_.activeSourceLine = record.step.sourceLine;
            snapshot_.originalHresult = result;
            snapshot_.originalExceptionType = "HRESULT";
            snapshot_.originalExceptionMessage = "not-applicable";
        }
        Emit(std::move(record));
    }

    void StartupDiagnostics::CleanupStarted()
    {
        std::lock_guard lock(mutex_);
        snapshot_.cleanupStarted = true;
    }

    void StartupDiagnostics::CleanupCompleted()
    {
        std::lock_guard lock(mutex_);
        snapshot_.cleanupCompleted = true;
    }

    void StartupDiagnostics::CaptureUnhandled(
        const std::optional<HRESULT> result,
        std::string exceptionType,
        std::string exceptionMessage)
    {
        std::lock_guard lock(mutex_);
        if (!snapshot_.originalHresult &&
            snapshot_.originalExceptionType == "unknown")
        {
            snapshot_.activeStage = "Uninstrumented";
            snapshot_.activeOperation = "unknown";
            snapshot_.activeApiName = "unknown";
            snapshot_.activeSourceFile = "unknown";
            snapshot_.activeSourceLine = 0;
            snapshot_.originalHresult = result;
            snapshot_.originalExceptionType = std::move(exceptionType);
            snapshot_.originalExceptionMessage =
                std::move(exceptionMessage);
        }
    }

    void StartupDiagnostics::WriteSummary()
    {
        DiagnosticLogger* logger{};
        StartupDiagnosticSnapshot snapshot;
        {
            std::lock_guard lock(mutex_);
            logger = logger_;
            snapshot = snapshot_;
        }
        if (logger)
        {
            logger->WriteStartupSummary(snapshot);
        }
    }

    StartupDiagnosticSnapshot StartupDiagnostics::Snapshot() const
    {
        std::lock_guard lock(mutex_);
        return snapshot_;
    }

    void StartupDiagnostics::Emit(StartupDiagnosticRecord record)
    {
        DiagnosticLogger* logger{};
        {
            std::lock_guard lock(mutex_);
            record.sessionGuid = sessionGuid_;
            record.encoderEnabled = encoderEnabled_;
            logger = logger_;
            if (!logger)
            {
                pending_.push_back(std::move(record));
                return;
            }
        }
        logger->WriteStartupDiagnostic(record);
    }

    std::int64_t StartupDiagnostics::QueryQpc() noexcept
    {
        LARGE_INTEGER value{};
        return QueryPerformanceCounter(&value) ? value.QuadPart : 0;
    }

    double StartupDiagnostics::ElapsedMilliseconds(
        const std::int64_t startedQpc,
        const std::int64_t endedQpc) noexcept
    {
        LARGE_INTEGER frequency{};
        if (startedQpc <= 0 || endedQpc < startedQpc ||
            !QueryPerformanceFrequency(&frequency) || frequency.QuadPart <= 0)
        {
            return 0.0;
        }
        return static_cast<double>(endedQpc - startedQpc) * 1000.0 /
            static_cast<double>(frequency.QuadPart);
    }

    std::optional<std::uint32_t> StartupDiagnostics::Win32Code(
        const std::optional<HRESULT> result) noexcept
    {
        if (!result || HRESULT_FACILITY(*result) != FACILITY_WIN32)
        {
            return std::nullopt;
        }
        return HRESULT_CODE(*result);
    }
}
