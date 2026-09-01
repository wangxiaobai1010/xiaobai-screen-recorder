#pragma once

#include "XbPreviewApi.h"

#include <cstdint>
#include <memory>
#include <vector>

namespace xbpreview
{
    struct MonitorPixelRect
    {
        std::int32_t left{};
        std::int32_t top{};
        std::int32_t right{};
        std::int32_t bottom{};
    };

    struct CursorSample
    {
        std::uint64_t sequence{};
        std::int64_t timestampQpc{};
        bool querySucceeded{};
        std::uint32_t lastError{};
        bool visible{};
        bool insideMonitor{};
        std::int32_t screenX{};
        std::int32_t screenY{};
        std::uintptr_t cursorHandle{};
    };

    struct CursorShape
    {
        std::uint64_t id{};
        std::uint64_t generation{};
        std::uint32_t width{};
        std::uint32_t height{};
        std::uint32_t hotspotX{};
        std::uint32_t hotspotY{};
        XbCursorShapeKind kind{ XbCursorShapeKind_None };
        std::uint64_t xorApproximationPixelCount{};
        std::vector<std::uint32_t> premultipliedBgra;
    };

    struct CursorCacheResult
    {
        std::shared_ptr<const CursorShape> shape;
        bool cacheHit{};
        bool cacheMiss{};
        bool conversionOccurred{};
        bool conversionSucceeded{};
        bool conversionFailed{};
        bool usedBuiltInFallback{};
        double conversionDurationMilliseconds{};
        std::int32_t conversionResult{};
        std::uint32_t conversionLastError{};
    };

    struct CursorShapeConversionDiagnostic
    {
        bool cacheHit{};
        bool cacheMiss{};
        bool conversionOccurred{};
        bool conversionSucceeded{};
        double conversionDurationMilliseconds{};
        std::int32_t conversionResult{};
        std::uint32_t conversionLastError{};
    };

    class CursorShapeConversionDiagnosticChannel final
    {
    public:
        static void Publish(
            const CursorShapeConversionDiagnostic& diagnostic) noexcept;
        [[nodiscard]] static CursorShapeConversionDiagnostic Consume() noexcept;
        static void Reset() noexcept;
    };

    struct CursorMappedRect
    {
        bool valid{};
        bool intersectsCamera{};
        double sourceX{};
        double sourceY{};
        double cameraViewLeft{};
        double cameraViewTop{};
        double cameraViewWidth{ 1.0 };
        double cameraViewHeight{ 1.0 };
        double outputHotspotX{};
        double outputHotspotY{};
        double left{};
        double top{};
        double width{};
        double height{};
        double viewportX{};
        double viewportY{};
        double viewportWidth{};
        double viewportHeight{};
        double transformedHotspotXPixels{};
        double transformedHotspotYPixels{};
        double drawLeftPixels{};
        double drawTopPixels{};
        double baseDrawWidthPixels{};
        double baseDrawHeightPixels{};
        double drawWidthPixels{};
        double drawHeightPixels{};
        double baseHotspotOffsetXPixels{};
        double baseHotspotOffsetYPixels{};
        double hotspotOffsetXPixels{};
        double hotspotOffsetYPixels{};
    };

    struct CursorDrawCommand
    {
        std::shared_ptr<const CursorShape> shape;
        CursorMappedRect mapped;
    };

    struct CursorRenderResult
    {
        bool drawn{};
        bool textureUploaded{};
        HRESULT result{ S_OK };
        double durationMilliseconds{};
    };
}
