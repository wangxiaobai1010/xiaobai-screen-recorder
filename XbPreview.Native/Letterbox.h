#pragma once

#include "XbPreviewApi.h"

#include <algorithm>

namespace xbpreview
{
    inline bool CalculateLetterbox(
        const std::uint32_t sourceWidth,
        const std::uint32_t sourceHeight,
        const std::uint32_t destinationWidth,
        const std::uint32_t destinationHeight,
        XbLetterboxRect& result) noexcept
    {
        if (sourceWidth == 0 || sourceHeight == 0 ||
            destinationWidth == 0 || destinationHeight == 0)
        {
            result = {};
            return false;
        }

        const auto scaleX = static_cast<double>(destinationWidth) / sourceWidth;
        const auto scaleY = static_cast<double>(destinationHeight) / sourceHeight;
        const auto scale = (std::min)(scaleX, scaleY);
        const auto width = static_cast<float>(sourceWidth * scale);
        const auto height = static_cast<float>(sourceHeight * scale);

        result.x = (static_cast<float>(destinationWidth) - width) * 0.5f;
        result.y = (static_cast<float>(destinationHeight) - height) * 0.5f;
        result.width = width;
        result.height = height;
        return true;
    }
}
