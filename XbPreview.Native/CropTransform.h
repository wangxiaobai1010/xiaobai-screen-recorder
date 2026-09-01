#pragma once

#include "XbPreviewApi.h"

#include <cmath>
#include <cstdint>

namespace xbpreview
{
    struct CropTransform
    {
        float originU{};
        float originV{};
        float scaleU{ 1.0f };
        float scaleV{ 1.0f };
        std::uint32_t captureWidth{};
        std::uint32_t captureHeight{};
        std::uint32_t outputWidth{};
        std::uint32_t outputHeight{};
    };

    struct SourceUv
    {
        float u{};
        float v{};
    };

    inline SourceUv MapRegionLocalToSource(
        const CropTransform& crop,
        const float localU,
        const float localV) noexcept
    {
        return SourceUv{
            crop.originU + localU * crop.scaleU,
            crop.originV + localV * crop.scaleV
        };
    }

    inline bool ResolveCropTransform(
        const XbPreviewSessionGeometryV1& geometry,
        CropTransform& output) noexcept
    {
        if (geometry.sourceWidth <= 0 ||
            geometry.sourceHeight <= 0 ||
            geometry.captureLeft < 0 ||
            geometry.captureTop < 0 ||
            geometry.captureWidth <= 0 ||
            geometry.captureHeight <= 0 ||
            geometry.outputWidth <= 0 ||
            geometry.outputHeight <= 0 ||
            static_cast<std::int64_t>(geometry.captureLeft) +
                geometry.captureWidth > geometry.sourceWidth ||
            static_cast<std::int64_t>(geometry.captureTop) +
                geometry.captureHeight > geometry.sourceHeight)
        {
            return false;
        }

        const auto sourceWidth = static_cast<double>(geometry.sourceWidth);
        const auto sourceHeight = static_cast<double>(geometry.sourceHeight);
        const auto originU =
            static_cast<double>(geometry.captureLeft) / sourceWidth;
        const auto originV =
            static_cast<double>(geometry.captureTop) / sourceHeight;
        const auto scaleU =
            static_cast<double>(geometry.captureWidth) / sourceWidth;
        const auto scaleV =
            static_cast<double>(geometry.captureHeight) / sourceHeight;

        output = CropTransform{
            static_cast<float>(originU),
            static_cast<float>(originV),
            static_cast<float>(scaleU),
            static_cast<float>(scaleV),
            static_cast<std::uint32_t>(geometry.captureWidth),
            static_cast<std::uint32_t>(geometry.captureHeight),
            static_cast<std::uint32_t>(geometry.outputWidth),
            static_cast<std::uint32_t>(geometry.outputHeight)
        };
        return std::isfinite(output.originU) &&
            std::isfinite(output.originV) &&
            std::isfinite(output.scaleU) &&
            std::isfinite(output.scaleV) &&
            output.originU >= 0.0f &&
            output.originV >= 0.0f &&
            output.scaleU >= 0.0f &&
            output.scaleV >= 0.0f &&
            output.originU + output.scaleU <= 1.0f &&
            output.originV + output.scaleV <= 1.0f;
    }
}
