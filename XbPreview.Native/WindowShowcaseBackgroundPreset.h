#pragma once

#include <array>
#include <cmath>
#include <cstdint>
#include <string_view>

namespace xbpreview
{
    enum class WindowShowcaseBackgroundPreset : std::uint32_t
    {
        Warm = 0,
        Art01 = 1,
        Art001 = 2
    };

    enum class WindowShowcaseBackgroundKind : std::uint32_t
    {
        Solid = 0,
        StaticTexture = 1
    };

    inline constexpr std::array<float, 4> WindowShowcaseWarmBackgroundSrgb{
        233.0f / 255.0f,
        229.0f / 255.0f,
        222.0f / 255.0f,
        1.0f
    };
    inline constexpr std::wstring_view WindowShowcaseArt01AssetRelativePath{
        L"assets\\幻彩01.png"
    };
    inline constexpr std::wstring_view WindowShowcaseArt001AssetRelativePath{
        L"assets\\幻彩02.png"
    };
    inline constexpr std::uint32_t WindowShowcaseArtPixelWidth = 1672;
    inline constexpr std::uint32_t WindowShowcaseArtPixelHeight = 941;

    struct WindowShowcaseBackgroundComposition
    {
        WindowShowcaseBackgroundPreset preset{
            WindowShowcaseBackgroundPreset::Warm };
        WindowShowcaseBackgroundKind kind{
            WindowShowcaseBackgroundKind::Solid };
        std::array<float, 4> solidSrgb{
            WindowShowcaseWarmBackgroundSrgb };
        float textureOriginU{};
        float textureOriginV{};
        float textureScaleU{ 1.0f };
        float textureScaleV{ 1.0f };
        std::uint32_t outputWidth{};
        std::uint32_t outputHeight{};

        [[nodiscard]] bool IsFixedOutputCanvasBackground() const noexcept
        {
            return outputWidth > 0 && outputHeight > 0 &&
                std::isfinite(textureOriginU) &&
                std::isfinite(textureOriginV) &&
                std::isfinite(textureScaleU) &&
                std::isfinite(textureScaleV) &&
                textureOriginU >= 0.0f && textureOriginV >= 0.0f &&
                textureScaleU > 0.0f && textureScaleU <= 1.0f &&
                textureScaleV > 0.0f && textureScaleV <= 1.0f;
        }

        [[nodiscard]] std::array<float, 8> TextureTransforms() const noexcept
        {
            return {
                0.0f, 0.0f, 1.0f, 1.0f,
                textureOriginU, textureOriginV,
                textureScaleU, textureScaleV
            };
        }
    };

    [[nodiscard]] inline constexpr bool IsWindowShowcaseArtPreset(
        const WindowShowcaseBackgroundPreset preset) noexcept
    {
        return preset == WindowShowcaseBackgroundPreset::Art01 ||
            preset == WindowShowcaseBackgroundPreset::Art001;
    }

    [[nodiscard]] inline constexpr std::wstring_view
        WindowShowcaseArtAssetRelativePath(
            const WindowShowcaseBackgroundPreset preset) noexcept
    {
        return preset == WindowShowcaseBackgroundPreset::Art01
            ? WindowShowcaseArt01AssetRelativePath
            : preset == WindowShowcaseBackgroundPreset::Art001
                ? WindowShowcaseArt001AssetRelativePath
                : std::wstring_view{};
    }

    [[nodiscard]] inline bool ResolveWindowShowcaseBackground(
        const WindowShowcaseBackgroundPreset preset,
        const std::uint32_t outputWidth,
        const std::uint32_t outputHeight,
        WindowShowcaseBackgroundComposition& composition) noexcept
    {
        if (outputWidth == 0 || outputHeight == 0)
        {
            return false;
        }

        composition = {};
        composition.preset = preset;
        composition.outputWidth = outputWidth;
        composition.outputHeight = outputHeight;
        if (preset == WindowShowcaseBackgroundPreset::Warm)
        {
            return composition.IsFixedOutputCanvasBackground();
        }
        if (!IsWindowShowcaseArtPreset(preset))
        {
            return false;
        }

        composition.kind = WindowShowcaseBackgroundKind::StaticTexture;
        const auto sourceAspect = static_cast<double>(
            WindowShowcaseArtPixelWidth) / WindowShowcaseArtPixelHeight;
        const auto outputAspect =
            static_cast<double>(outputWidth) / outputHeight;
        if (sourceAspect > outputAspect)
        {
            composition.textureScaleU =
                static_cast<float>(outputAspect / sourceAspect);
            composition.textureOriginU =
                (1.0f - composition.textureScaleU) * 0.5f;
        }
        else if (sourceAspect < outputAspect)
        {
            composition.textureScaleV =
                static_cast<float>(sourceAspect / outputAspect);
            composition.textureOriginV =
                (1.0f - composition.textureScaleV) * 0.5f;
        }
        return composition.IsFixedOutputCanvasBackground();
    }

    // Reuses the frozen Layer 5 cover/crop semantics for a decoded user image.
    // The preset field remains a visual fallback label; no frozen asset or
    // palette value is modified by this overload.
    [[nodiscard]] inline bool ResolveWindowShowcaseTextureBackground(
        const std::uint32_t sourceWidth,
        const std::uint32_t sourceHeight,
        const std::uint32_t outputWidth,
        const std::uint32_t outputHeight,
        WindowShowcaseBackgroundComposition& composition) noexcept
    {
        if (sourceWidth == 0 || sourceHeight == 0 ||
            outputWidth == 0 || outputHeight == 0)
        {
            return false;
        }

        composition = {};
        composition.kind = WindowShowcaseBackgroundKind::StaticTexture;
        composition.outputWidth = outputWidth;
        composition.outputHeight = outputHeight;
        const auto sourceAspect =
            static_cast<double>(sourceWidth) / sourceHeight;
        const auto outputAspect =
            static_cast<double>(outputWidth) / outputHeight;
        if (sourceAspect > outputAspect)
        {
            composition.textureScaleU =
                static_cast<float>(outputAspect / sourceAspect);
            composition.textureOriginU =
                (1.0f - composition.textureScaleU) * 0.5f;
        }
        else if (sourceAspect < outputAspect)
        {
            composition.textureScaleV =
                static_cast<float>(sourceAspect / outputAspect);
            composition.textureOriginV =
                (1.0f - composition.textureScaleV) * 0.5f;
        }
        return composition.IsFixedOutputCanvasBackground();
    }
}
