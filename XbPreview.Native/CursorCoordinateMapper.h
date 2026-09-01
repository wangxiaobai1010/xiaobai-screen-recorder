#pragma once

#include "CameraTransform.h"
#include "CursorCaptureState.h"

#include <cmath>
#include <cstdint>

namespace xbpreview
{
    [[nodiscard]] inline CursorMappedRect MapCursorToPreview(
        const CursorSample& sample,
        const CursorShape& shape,
        const MonitorPixelRect& monitor,
        const std::uint32_t captureWidth,
        const std::uint32_t captureHeight,
        const CameraTransform& camera,
        const XbLetterboxRect& letterbox) noexcept
    {
        CursorMappedRect mapped{};
        mapped.viewportX = letterbox.x;
        mapped.viewportY = letterbox.y;
        mapped.viewportWidth = letterbox.width;
        mapped.viewportHeight = letterbox.height;

        if (!sample.querySucceeded ||
            !sample.visible ||
            !sample.insideMonitor ||
            captureWidth == 0 ||
            captureHeight == 0 ||
            shape.width == 0 ||
            shape.height == 0 ||
            camera.width <= 0.0f ||
            camera.height <= 0.0f ||
            !std::isfinite(camera.appliedZoom) ||
            camera.appliedZoom <= 0.0 ||
            letterbox.width <= 0.0f ||
            letterbox.height <= 0.0f)
        {
            return mapped;
        }

        mapped.sourceX = static_cast<double>(
            sample.screenX - monitor.left) / captureWidth;
        mapped.sourceY = static_cast<double>(
            sample.screenY - monitor.top) / captureHeight;
        mapped.cameraViewLeft = camera.left;
        mapped.cameraViewTop = camera.top;
        mapped.cameraViewWidth = camera.width;
        mapped.cameraViewHeight = camera.height;
        mapped.outputHotspotX =
            (mapped.sourceX - mapped.cameraViewLeft) /
            mapped.cameraViewWidth;
        mapped.outputHotspotY =
            (mapped.sourceY - mapped.cameraViewTop) /
            mapped.cameraViewHeight;

        // Resolve the transformed hotspot first. Apply camera zoom exactly
        // once below to both the cursor image and its hotspot offset.
        mapped.transformedHotspotXPixels =
            static_cast<double>(letterbox.x) +
            mapped.outputHotspotX * letterbox.width;
        mapped.transformedHotspotYPixels =
            static_cast<double>(letterbox.y) +
            mapped.outputHotspotY * letterbox.height;
        mapped.baseDrawWidthPixels =
            static_cast<double>(shape.width) *
            letterbox.width / captureWidth;
        mapped.baseDrawHeightPixels =
            static_cast<double>(shape.height) *
            letterbox.height / captureHeight;
        mapped.baseHotspotOffsetXPixels =
            static_cast<double>(shape.hotspotX) *
            letterbox.width / captureWidth;
        mapped.baseHotspotOffsetYPixels =
            static_cast<double>(shape.hotspotY) *
            letterbox.height / captureHeight;

        const auto cursorScale = camera.appliedZoom;
        mapped.drawWidthPixels =
            mapped.baseDrawWidthPixels * cursorScale;
        mapped.drawHeightPixels =
            mapped.baseDrawHeightPixels * cursorScale;
        mapped.hotspotOffsetXPixels =
            mapped.baseHotspotOffsetXPixels * cursorScale;
        mapped.hotspotOffsetYPixels =
            mapped.baseHotspotOffsetYPixels * cursorScale;
        mapped.drawLeftPixels =
            mapped.transformedHotspotXPixels -
            mapped.hotspotOffsetXPixels;
        mapped.drawTopPixels =
            mapped.transformedHotspotYPixels -
            mapped.hotspotOffsetYPixels;

        mapped.left =
            (mapped.drawLeftPixels - letterbox.x) / letterbox.width;
        mapped.top =
            (mapped.drawTopPixels - letterbox.y) / letterbox.height;
        mapped.width = mapped.drawWidthPixels / letterbox.width;
        mapped.height = mapped.drawHeightPixels / letterbox.height;

        mapped.valid =
            std::isfinite(mapped.sourceX) &&
            std::isfinite(mapped.sourceY) &&
            std::isfinite(mapped.left) &&
            std::isfinite(mapped.top) &&
            std::isfinite(mapped.width) &&
            std::isfinite(mapped.height) &&
            mapped.width > 0.0 &&
            mapped.height > 0.0;
        mapped.intersectsCamera = mapped.valid &&
            mapped.left < 1.0 &&
            mapped.top < 1.0 &&
            mapped.left + mapped.width > 0.0 &&
            mapped.top + mapped.height > 0.0;
        return mapped;
    }
}
