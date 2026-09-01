#include "CursorDiagnosticLogger.h"

#include <windows.h>

#include <iomanip>
#include <sstream>

namespace
{
    std::string EscapeJson(const std::string& value)
    {
        std::string escaped;
        escaped.reserve(value.size());
        for (const auto character : value)
        {
            switch (character)
            {
            case '\\': escaped += "\\\\"; break;
            case '"': escaped += "\\\""; break;
            case '\n': escaped += "\\n"; break;
            case '\r': escaped += "\\r"; break;
            case '\t': escaped += "\\t"; break;
            default: escaped += character; break;
            }
        }
        return escaped;
    }

    const char* ModeName(const std::int32_t value) noexcept
    {
        return value == XbCursorMode_CustomCursor
            ? "CustomCursor"
            : "SystemCursor";
    }

    const char* FallbackName(const std::int32_t value) noexcept
    {
        switch (value)
        {
        case XbCursorFallbackReason_ApiUnavailable:
            return "ApiUnavailable";
        case XbCursorFallbackReason_CustomRendererInitializationFailed:
            return "CustomRendererInitializationFailed";
        case XbCursorFallbackReason_WgcSettingFailed:
            return "WgcSettingFailed";
        case XbCursorFallbackReason_WgcReadbackMismatch:
            return "WgcReadbackMismatch";
        default:
            return "None";
        }
    }

    const char* ShapeFormatName(const std::uint32_t value) noexcept
    {
        switch (value)
        {
        case XbCursorShapeKind_ColorAlpha:
            return "ColorAlpha";
        case XbCursorShapeKind_ColorMask:
            return "ColorMask";
        case XbCursorShapeKind_MonochromeAndXor:
            return "MonochromeAndXor";
        case XbCursorShapeKind_BuiltInFallbackArrow:
            return "BuiltInFallbackArrow";
        default:
            return "None";
        }
    }
}

namespace xbpreview
{
    CursorDiagnosticRecord::CursorDiagnosticRecord() noexcept
        : shapeConversion(
            CursorShapeConversionDiagnosticChannel::Consume())
    {
    }

    CursorDiagnosticLogger::~CursorDiagnosticLogger()
    {
        Close();
    }

    bool CursorDiagnosticLogger::Open(
        const std::wstring& directory,
        const std::wstring& sessionId,
        std::wstring& error) noexcept
    {
        Close();
        try
        {
            std::filesystem::path base = directory.empty()
                ? std::filesystem::current_path() / L"diagnostic-logs"
                : std::filesystem::path(directory);
            std::filesystem::create_directories(base);
            SYSTEMTIME local{};
            GetLocalTime(&local);
            wchar_t name[512]{};
            swprintf_s(
                name,
                L"p1c-cursor-%04u%02u%02u-%02u%02u%02u-%03u-%ls.jsonl",
                local.wYear,
                local.wMonth,
                local.wDay,
                local.wHour,
                local.wMinute,
                local.wSecond,
                local.wMilliseconds,
                sessionId.c_str());
            const auto path = base / name;
            stream_.open(path, std::ios::binary | std::ios::out | std::ios::trunc);
            if (!stream_)
            {
                error = L"无法创建 P1c cursor JSONL。";
                return false;
            }
            filePath_ = path.wstring();
            closing_ = false;
            open_ = true;
            queueDropCount_.store(0);
            writer_ = std::thread(&CursorDiagnosticLogger::WriterMain, this);
            return true;
        }
        catch (const std::exception&)
        {
            error = L"创建 P1c cursor JSONL 时发生标准异常。";
            return false;
        }
        catch (...)
        {
            error = L"创建 P1c cursor JSONL 时发生未知异常。";
            return false;
        }
    }

    void CursorDiagnosticLogger::Enqueue(
        CursorDiagnosticRecord record) noexcept
    {
        try
        {
            {
                std::lock_guard lock(mutex_);
                if (!open_ || closing_)
                {
                    return;
                }
                if (queue_.size() >= MaximumQueueDepth)
                {
                    ++queueDropCount_;
                    return;
                }
                queue_.push_back(std::move(record));
            }
            condition_.notify_one();
        }
        catch (...)
        {
            ++queueDropCount_;
        }
    }

    void CursorDiagnosticLogger::Close() noexcept
    {
        {
            std::lock_guard lock(mutex_);
            if (!open_)
            {
                return;
            }
            closing_ = true;
        }
        condition_.notify_all();
        if (writer_.joinable())
        {
            writer_.join();
        }
        if (stream_)
        {
            stream_.flush();
            stream_.close();
        }
        {
            std::lock_guard lock(mutex_);
            queue_.clear();
            open_ = false;
            closing_ = false;
        }
    }

    void CursorDiagnosticLogger::WriterMain() noexcept
    {
        try
        {
            for (;;)
            {
                CursorDiagnosticRecord record{};
                {
                    std::unique_lock lock(mutex_);
                    condition_.wait(
                        lock,
                        [this]
                        {
                            return closing_ || !queue_.empty();
                        });
                    if (queue_.empty())
                    {
                        if (closing_)
                        {
                            break;
                        }
                        continue;
                    }
                    record = std::move(queue_.front());
                    queue_.pop_front();
                }
                WriteRecord(stream_, record);
            }
        }
        catch (...)
        {
            ++queueDropCount_;
        }
    }

    void CursorDiagnosticLogger::WriteRecord(
        std::ofstream& stream,
        const CursorDiagnosticRecord& record)
    {
        const auto& c = record.cursor;
        const auto& p = record.preview;
        const auto& conversion = record.shapeConversion;
        const auto finalDrawWidthPixels =
            c.outputWidth * c.viewportWidth;
        const auto finalDrawHeightPixels =
            c.outputHeight * c.viewportHeight;
        const auto finalHotspotOffsetXPixels =
            (c.outputHotspotX - c.outputLeft) * c.viewportWidth;
        const auto finalHotspotOffsetYPixels =
            (c.outputHotspotY - c.outputTop) * c.viewportHeight;
        const auto baseDrawWidthPixels = p.captureWidth > 0
            ? static_cast<double>(c.shapeWidth) *
                c.viewportWidth / p.captureWidth
            : 0.0;
        const auto baseDrawHeightPixels = p.captureHeight > 0
            ? static_cast<double>(c.shapeHeight) *
                c.viewportHeight / p.captureHeight
            : 0.0;
        const auto baseHotspotOffsetXPixels = p.captureWidth > 0
            ? static_cast<double>(c.hotspotX) *
                c.viewportWidth / p.captureWidth
            : 0.0;
        const auto baseHotspotOffsetYPixels = p.captureHeight > 0
            ? static_cast<double>(c.hotspotY) *
                c.viewportHeight / p.captureHeight
            : 0.0;
        const auto transformedHotspotXPixels =
            c.viewportX + c.outputHotspotX * c.viewportWidth;
        const auto transformedHotspotYPixels =
            c.viewportY + c.outputHotspotY * c.viewportHeight;
        stream << std::setprecision(15)
            << "{\"event\":\"" << EscapeJson(record.event) << "\""
            << ",\"timestampUtcFileTime100ns\":"
            << record.timestampUtcFileTime100ns
            << ",\"sessionIdHigh\":" << p.sessionIdHigh
            << ",\"sessionIdLow\":" << p.sessionIdLow
            << ",\"timestampQpc\":" << c.timestampQpc
            << ",\"cursorSequence\":" << c.cursorSequence
            << ",\"requestedMode\":\"" << ModeName(c.requestedMode) << "\""
            << ",\"actualMode\":\"" << ModeName(c.actualMode) << "\""
            << ",\"fallbackReason\":\"" << FallbackName(c.fallbackReason) << "\""
            << ",\"wgcCursorPropertyAvailable\":" << c.wgcCursorPropertyAvailable
            << ",\"systemCursorIncluded\":" << c.systemCursorIncluded
            << ",\"customCursorLayerActive\":" << c.customCursorLayerActive
            << ",\"customCursorDrawn\":" << c.customCursorLayerActive
            << ",\"lastFrameDrawIssued\":" << c.lastFrameDrawn
            << ",\"cursorOwnershipExclusive\":"
            << ((c.systemCursorIncluded != c.customCursorLayerActive) ? 1 : 0)
            << ",\"cursorVisible\":" << c.cursorVisible
            << ",\"cursorInsideMonitor\":" << c.cursorInsideMonitor
            << ",\"screenX\":" << c.screenX
            << ",\"screenY\":" << c.screenY
            << ",\"sourceX\":" << c.sourceX
            << ",\"sourceY\":" << c.sourceY
            << ",\"cameraViewLeft\":" << c.cameraViewLeft
            << ",\"cameraViewTop\":" << c.cameraViewTop
            << ",\"cameraViewWidth\":" << c.cameraViewWidth
            << ",\"cameraViewHeight\":" << c.cameraViewHeight
            << ",\"zoom\":" << c.zoom
            << ",\"centerX\":" << c.centerX
            << ",\"centerY\":" << c.centerY
            << ",\"outputHotspotX\":" << c.outputHotspotX
            << ",\"outputHotspotY\":" << c.outputHotspotY
            << ",\"outputLeft\":" << c.outputLeft
            << ",\"outputTop\":" << c.outputTop
            << ",\"outputWidth\":" << c.outputWidth
            << ",\"outputHeight\":" << c.outputHeight
            << ",\"viewportX\":" << c.viewportX
            << ",\"viewportY\":" << c.viewportY
            << ",\"viewportWidth\":" << c.viewportWidth
            << ",\"viewportHeight\":" << c.viewportHeight
            << ",\"captureWidth\":" << p.captureWidth
            << ",\"captureHeight\":" << p.captureHeight
            << ",\"drawLeftPx\":"
            << (c.viewportX + c.outputLeft * c.viewportWidth)
            << ",\"drawTopPx\":"
            << (c.viewportY + c.outputTop * c.viewportHeight)
            << ",\"baseDrawWidthPx\":" << baseDrawWidthPixels
            << ",\"baseDrawHeightPx\":" << baseDrawHeightPixels
            << ",\"finalDrawWidthPx\":" << finalDrawWidthPixels
            << ",\"finalDrawHeightPx\":" << finalDrawHeightPixels
            << ",\"baseHotspotOffsetXPx\":"
            << baseHotspotOffsetXPixels
            << ",\"baseHotspotOffsetYPx\":"
            << baseHotspotOffsetYPixels
            << ",\"finalHotspotOffsetXPx\":"
            << finalHotspotOffsetXPixels
            << ",\"finalHotspotOffsetYPx\":"
            << finalHotspotOffsetYPixels
            << ",\"transformedHotspotXPx\":"
            << transformedHotspotXPixels
            << ",\"transformedHotspotYPx\":"
            << transformedHotspotYPixels
            << ",\"drawWidthPx\":" << finalDrawWidthPixels
            << ",\"drawHeightPx\":" << finalDrawHeightPixels
            << ",\"hotspotOffsetXPx\":"
            << finalHotspotOffsetXPixels
            << ",\"hotspotOffsetYPx\":"
            << finalHotspotOffsetYPixels
            << ",\"shapeId\":" << c.shapeId
            << ",\"shapeHandleId\":" << c.shapeId
            << ",\"shapeGeneration\":" << c.shapeGeneration
            << ",\"shapeKind\":" << c.shapeKind
            << ",\"shapeFormat\":\""
            << ShapeFormatName(c.shapeKind) << "\""
            << ",\"shapeWidth\":" << c.shapeWidth
            << ",\"shapeHeight\":" << c.shapeHeight
            << ",\"hotspotX\":" << c.hotspotX
            << ",\"hotspotY\":" << c.hotspotY
            << ",\"sampleCount\":" << c.sampleCount
            << ",\"drawCount\":" << c.drawCount
            << ",\"hiddenSkipCount\":" << c.hiddenSkipCount
            << ",\"outsideMonitorSkipCount\":" << c.outsideMonitorSkipCount
            << ",\"outsideCameraSkipCount\":" << c.outsideCameraSkipCount
            << ",\"getCursorInfoFailureCount\":" << c.getCursorInfoFailureCount
            << ",\"shapeCacheHitCount\":" << c.shapeCacheHitCount
            << ",\"shapeCacheMissCount\":" << c.shapeCacheMissCount
            << ",\"textureUploadCount\":" << c.textureUploadCount
            << ",\"shapeConversionFailureCount\":" << c.shapeConversionFailureCount
            << ",\"builtInFallbackCount\":" << c.builtInFallbackCount
            << ",\"xorApproximationPixelCount\":" << c.xorApproximationPixelCount
            << ",\"diagnosticQueueDropCount\":" << c.diagnosticQueueDropCount
            << ",\"getCursorInfoResult\":" << c.getCursorInfoResult
            << ",\"getCursorInfoLastError\":" << c.getCursorInfoLastError
            << ",\"cacheHit\":"
            << (conversion.cacheHit ? "true" : "false")
            << ",\"cacheMiss\":"
            << (conversion.cacheMiss ? "true" : "false")
            << ",\"shapeConversionOccurred\":"
            << (conversion.conversionOccurred ? "true" : "false")
            << ",\"shapeConversionDurationMs\":"
            << conversion.conversionDurationMilliseconds
            << ",\"conversionSucceeded\":"
            << (conversion.conversionSucceeded ? "true" : "false")
            << ",\"conversionErrorCode\":"
            << conversion.conversionResult
            << ",\"shapeConversionResult\":" << c.shapeConversionResult
            << ",\"shapeConversionLastError\":" << c.shapeConversionLastError
            << ",\"wgcCursorSettingResult\":" << c.wgcCursorSettingResult
            << ",\"wgcCursorSettingLastError\":" << c.wgcCursorSettingLastError
            << ",\"cursorRenderDurationMs\":" << c.lastRenderDurationMilliseconds
            << ",\"cursorRenderResult\":" << static_cast<std::uint32_t>(c.reserved1)
            << ",\"captureFps\":" << p.captureFps
            << ",\"presentFps\":" << p.presentFps
            << ",\"p50LatencyMs\":" << p.p50LatencyMilliseconds
            << ",\"p95LatencyMs\":" << p.p95LatencyMilliseconds
            << ",\"maxLatencyMs\":" << p.maxLatencyMilliseconds
            << ",\"captureFrameCount\":" << p.captureFrameCount
            << ",\"presentFrameCount\":" << p.presentFrameCount
            << ",\"droppedFrameCount\":" << p.droppedFrameCount
            << ",\"framePoolRecreateCount\":" << p.framePoolRecreateCount
            << ",\"swapChainResizeCount\":" << p.swapChainResizeCount
            << ",\"previewState\":" << p.state
            << ",\"lastResult\":" << p.lastResult
            << "}\n";
    }
}
