#pragma once

#include "CursorCaptureState.h"
#include "XbPreviewApi.h"

#include <atomic>
#include <condition_variable>
#include <cstdint>
#include <deque>
#include <filesystem>
#include <fstream>
#include <mutex>
#include <string>
#include <thread>

namespace xbpreview
{
    struct CursorDiagnosticRecord
    {
        CursorDiagnosticRecord() noexcept;

        std::string event;
        std::uint64_t timestampUtcFileTime100ns{};
        XbCursorStats cursor{};
        XbPreviewStats preview{};
        CursorShapeConversionDiagnostic shapeConversion{};
    };

    class CursorDiagnosticLogger final
    {
    public:
        CursorDiagnosticLogger() = default;
        ~CursorDiagnosticLogger();
        CursorDiagnosticLogger(const CursorDiagnosticLogger&) = delete;
        CursorDiagnosticLogger& operator=(const CursorDiagnosticLogger&) = delete;

        [[nodiscard]] bool Open(
            const std::wstring& directory,
            const std::wstring& sessionId,
            std::wstring& error) noexcept;
        void Enqueue(CursorDiagnosticRecord record) noexcept;
        void Close() noexcept;

        [[nodiscard]] const std::wstring& FilePath() const noexcept
        {
            return filePath_;
        }

        [[nodiscard]] std::uint64_t QueueDropCount() const noexcept
        {
            return queueDropCount_.load();
        }

    private:
        static constexpr std::size_t MaximumQueueDepth = 1024;

        void WriterMain() noexcept;
        static void WriteRecord(
            std::ofstream& stream,
            const CursorDiagnosticRecord& record);

        std::wstring filePath_;
        std::ofstream stream_;
        std::mutex mutex_;
        std::condition_variable condition_;
        std::deque<CursorDiagnosticRecord> queue_;
        std::thread writer_;
        bool closing_{};
        bool open_{};
        std::atomic<std::uint64_t> queueDropCount_{ 0 };
    };
}
