#pragma once

#include "XbPreviewApi.h"

#include <algorithm>
#include <cmath>

namespace xbpreview
{
    inline constexpr double MaximumCameraZoom = 2.0;

    struct CameraTransform
    {
        float left{};
        float top{};
        float width{ 1.0f };
        float height{ 1.0f };
        double appliedZoom{ 1.0 };
        double appliedCenterX{ 0.5 };
        double appliedCenterY{ 0.5 };
        std::uint64_t sequence{};
        std::int32_t mode{};
        bool enabled{};
        bool fallback{};
    };

    inline bool IsFinite(const double value) noexcept
    {
        return std::isfinite(value);
    }

    inline bool IsValidCameraState(const XbCameraState& state) noexcept
    {
        return state.structSize == sizeof(XbCameraState) &&
            (state.apiVersion & 0xFFFF0000u) ==
                (XB_PREVIEW_API_VERSION & 0xFFFF0000u) &&
            (state.enabled == 0u || state.enabled == 1u) &&
            IsFinite(state.zoom) &&
            IsFinite(state.centerX) &&
            IsFinite(state.centerY) &&
            IsFinite(state.transitionProgress) &&
            IsFinite(state.targetX) &&
            IsFinite(state.targetY) &&
            state.zoom >= 1.0 && state.zoom <= MaximumCameraZoom &&
            state.centerX >= 0.0 && state.centerX <= 1.0 &&
            state.centerY >= 0.0 && state.centerY <= 1.0 &&
            state.transitionProgress >= 0.0 &&
            state.transitionProgress <= 1.0 &&
            state.targetX >= 0.0 && state.targetX <= 1.0 &&
            state.targetY >= 0.0 && state.targetY <= 1.0;
    }

    inline CameraTransform FullViewFallback(
        const std::uint64_t sequence = 0,
        const std::int32_t mode = 0) noexcept
    {
        CameraTransform output{};
        output.sequence = sequence;
        output.mode = mode;
        output.fallback = true;
        return output;
    }

    inline CameraTransform ResolveCameraTransform(
        const XbCameraState& state) noexcept
    {
        if (!IsValidCameraState(state))
        {
            return FullViewFallback(state.sequence, state.mode);
        }
        if (state.enabled == 0u || state.zoom <= 1.0)
        {
            auto output = FullViewFallback(state.sequence, state.mode);
            output.fallback = false;
            return output;
        }

        const double size = 1.0 / state.zoom;
        const double half = size / 2.0;
        const double centerX = std::clamp(state.centerX, half, 1.0 - half);
        const double centerY = std::clamp(state.centerY, half, 1.0 - half);

        CameraTransform output{};
        output.left = static_cast<float>(centerX - half);
        output.top = static_cast<float>(centerY - half);
        output.width = static_cast<float>(size);
        output.height = static_cast<float>(size);
        output.appliedZoom = state.zoom;
        output.appliedCenterX = centerX;
        output.appliedCenterY = centerY;
        output.sequence = state.sequence;
        output.mode = state.mode;
        output.enabled = true;
        return output;
    }
}
