#pragma once

#include <cstdint>

namespace xbpreview
{
    inline constexpr std::uint32_t RecordingVideoBitrate = 12'000'000;

    [[nodiscard]] constexpr std::uint32_t RecordingVideoTargetBitrate(
        const std::uint32_t outputWidth,
        const std::uint32_t outputHeight,
        const std::uint32_t framesPerSecond) noexcept
    {
        if (outputWidth == 0 || outputHeight == 0 ||
            (framesPerSecond != 30 && framesPerSecond != 60))
        {
            return 0;
        }

        return RecordingVideoBitrate;
    }
}
