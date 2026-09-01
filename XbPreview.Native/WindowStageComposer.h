#pragma once

#include "WindowShowcaseBackgroundPreset.h"

#include <algorithm>
#include <array>
#include <cmath>
#include <cstdint>

namespace xbpreview
{
    inline constexpr float FlatWindowStageMaximumFraction = 0.90f;
    inline constexpr auto FlatWindowStageBackgroundSrgb =
        WindowShowcaseWarmBackgroundSrgb;

    struct FlatWindowStageDestination
    {
        float left{};
        float top{};
        float width{};
        float height{};

        [[nodiscard]] bool IsValid() const noexcept
        {
            return std::isfinite(left) && std::isfinite(top) &&
                std::isfinite(width) && std::isfinite(height) &&
                left >= 0.0f && top >= 0.0f &&
                width > 0.0f && height > 0.0f;
        }
    };

    struct FlatWindowStageComposition
    {
        std::array<float, 4> backgroundSrgb{};
        FlatWindowStageDestination window{};
        float sourceOriginU{};
        float sourceOriginV{};
        float sourceScaleU{ 1.0f };
        float sourceScaleV{ 1.0f };

        [[nodiscard]] bool UsesFullSourceTexture() const noexcept
        {
            return sourceOriginU == 0.0f && sourceOriginV == 0.0f &&
                sourceScaleU == 1.0f && sourceScaleV == 1.0f;
        }
    };

    class WindowStageComposer final
    {
    public:
        [[nodiscard]] static bool ComposeFlat(
            const std::uint32_t sourceWidth,
            const std::uint32_t sourceHeight,
            const std::uint32_t outputWidth,
            const std::uint32_t outputHeight,
            FlatWindowStageComposition& composition,
            const float maximumStageFraction =
                FlatWindowStageMaximumFraction) noexcept
        {
            if (sourceWidth == 0 || sourceHeight == 0 ||
                outputWidth == 0 || outputHeight == 0 ||
                !std::isfinite(maximumStageFraction) ||
                maximumStageFraction <= 0.0f ||
                maximumStageFraction > 1.0f)
            {
                return false;
            }

            const auto availableWidth =
                static_cast<double>(outputWidth) * maximumStageFraction;
            const auto availableHeight =
                static_cast<double>(outputHeight) * maximumStageFraction;
            const auto scale = (std::min)(
                availableWidth / sourceWidth,
                availableHeight / sourceHeight);
            const auto width = static_cast<float>(sourceWidth * scale);
            const auto height = static_cast<float>(sourceHeight * scale);

            composition = FlatWindowStageComposition{
                FlatWindowStageBackgroundSrgb,
                FlatWindowStageDestination{
                    (static_cast<float>(outputWidth) - width) * 0.5f,
                    (static_cast<float>(outputHeight) - height) * 0.5f,
                    width,
                    height
                },
                0.0f,
                0.0f,
                1.0f,
                1.0f
            };
            return composition.window.IsValid() &&
                composition.UsesFullSourceTexture();
        }
    };
}
