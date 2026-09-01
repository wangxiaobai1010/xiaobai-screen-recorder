#pragma once

#include "WindowStageComposer.h"

#include <algorithm>
#include <array>
#include <cmath>
#include <cstdint>

namespace xbpreview
{
    inline constexpr float WindowCardShadowReferenceWidth = 1920.0f;
    inline constexpr float WindowCardShadowReferenceHeight = 1080.0f;

    struct WindowCardShadowParameters
    {
        float opacity{};
        float verticalOffsetPixels{};
        float softnessPixels{};

        [[nodiscard]] bool IsValid() const noexcept
        {
            return std::isfinite(opacity) &&
                std::isfinite(verticalOffsetPixels) &&
                std::isfinite(softnessPixels) &&
                opacity > 0.0f && opacity <= 1.0f &&
                verticalOffsetPixels >= 0.0f &&
                softnessPixels > 0.0f;
        }
    };

    // Candidate A: a restrained, flat-stage shadow. There is deliberately no
    // X offset, spread, card transform, perspective, motion, or source-image
    // sampling in this Layer 2 treatment.
    inline constexpr WindowCardShadowParameters WindowCardVerySoftShadow{
        0.14f,
        14.0f,
        34.0f
    };
    inline constexpr float WindowCardShadowMinimumOpacity = 0.05f;
    inline constexpr float WindowCardShadowMinimumVerticalOffsetPixels = 5.0f;
    inline constexpr float WindowCardShadowMinimumSoftnessPixels = 42.0f;
    inline constexpr float WindowCardShadowSmallCoverage = 0.30f;
    inline constexpr float WindowCardShadowLargeCoverage = 0.75f;
    inline constexpr float WindowCardCornerRadiusPixels = 8.0f;

    [[nodiscard]] inline float CalculateWindowCardShadowStrength(
        const FlatWindowStageDestination& card,
        const std::uint32_t outputWidth,
        const std::uint32_t outputHeight) noexcept
    {
        if (!card.IsValid() || outputWidth == 0 || outputHeight == 0)
        {
            return 0.0f;
        }

        const auto canvasArea =
            static_cast<double>(outputWidth) * outputHeight;
        const auto cardArea =
            static_cast<double>(card.width) * card.height;
        const auto coverage = static_cast<float>(cardArea / canvasArea);
        const auto normalized = (std::clamp)(
            (coverage - WindowCardShadowSmallCoverage) /
                (WindowCardShadowLargeCoverage -
                    WindowCardShadowSmallCoverage),
            0.0f,
            1.0f);

        // Smoothstep is continuous at both clamped endpoints, so resizing a
        // card cannot create a visible parameter jump or require stored state.
        return normalized * normalized * (3.0f - (2.0f * normalized));
    }

    struct WindowCardShadowComposition
    {
        FlatWindowStageDestination card{};
        FlatWindowStageDestination support{};
        float opacity{};
        float verticalOffsetPixels{};
        float softnessPixels{};
        float strength{};
        float cornerRadiusPixels{};

        [[nodiscard]] bool IsValid(
            const std::uint32_t outputWidth,
            const std::uint32_t outputHeight) const noexcept
        {
            if (!card.IsValid() || !support.IsValid() ||
                outputWidth == 0 || outputHeight == 0 ||
                !std::isfinite(opacity) ||
                !std::isfinite(verticalOffsetPixels) ||
                !std::isfinite(softnessPixels) ||
                !std::isfinite(strength) ||
                !std::isfinite(cornerRadiusPixels) ||
                opacity <= 0.0f || opacity > 1.0f ||
                verticalOffsetPixels < 0.0f || softnessPixels <= 0.0f ||
                cornerRadiusPixels <= 0.0f ||
                cornerRadiusPixels > (std::min)(card.width, card.height) * 0.5f)
            {
                return false;
            }

            const auto canvasWidth = static_cast<float>(outputWidth);
            const auto canvasHeight = static_cast<float>(outputHeight);
            return card.left >= 0.0f && card.top >= 0.0f &&
                support.left >= 0.0f && support.top >= 0.0f &&
                card.left + card.width <= canvasWidth &&
                card.top + card.height <= canvasHeight &&
                support.left + support.width <= canvasWidth &&
                support.top + support.height <= canvasHeight &&
                strength >= 0.0f && strength <= 1.0f;
        }
    };

    [[nodiscard]] inline bool ComposeWindowCardShadow(
        const FlatWindowStageComposition& stage,
        const std::uint32_t outputWidth,
        const std::uint32_t outputHeight,
        WindowCardShadowComposition& composition,
        const WindowCardShadowParameters& parameters =
            WindowCardVerySoftShadow) noexcept
    {
        if (!stage.window.IsValid() || !stage.UsesFullSourceTexture() ||
            outputWidth == 0 || outputHeight == 0 ||
            !parameters.IsValid())
        {
            return false;
        }

        const auto outputScale = (std::min)(
            static_cast<float>(outputWidth) / WindowCardShadowReferenceWidth,
            static_cast<float>(outputHeight) / WindowCardShadowReferenceHeight);
        const auto strength = CalculateWindowCardShadowStrength(
            stage.window,
            outputWidth,
            outputHeight);
        const auto opacity = WindowCardShadowMinimumOpacity +
            ((parameters.opacity - WindowCardShadowMinimumOpacity) * strength);
        const auto verticalOffset =
            (WindowCardShadowMinimumVerticalOffsetPixels +
                ((parameters.verticalOffsetPixels -
                    WindowCardShadowMinimumVerticalOffsetPixels) * strength)) *
            outputScale;
        const auto softness =
            (WindowCardShadowMinimumSoftnessPixels +
                ((parameters.softnessPixels -
                    WindowCardShadowMinimumSoftnessPixels) * strength)) *
            outputScale;
        const auto cornerRadius = WindowCardCornerRadiusPixels * outputScale;

        WindowCardShadowComposition candidate{
            stage.window,
            FlatWindowStageDestination{
                stage.window.left - softness,
                stage.window.top + verticalOffset - softness,
                stage.window.width + (softness * 2.0f),
                stage.window.height + (softness * 2.0f)
            },
            opacity,
            verticalOffset,
            softness,
            strength,
            cornerRadius
        };
        if (!candidate.IsValid(outputWidth, outputHeight))
        {
            return false;
        }

        composition = candidate;
        return true;
    }

    struct alignas(16) WindowCardShadowShaderConstants
    {
        std::array<float, 4> cardRectangle{};
        std::array<float, 4> visualParameters{};
    };

    static_assert(
        sizeof(WindowCardShadowShaderConstants) == 32,
        "Window card shadow constants must match two HLSL float4 values.");

    struct alignas(16) WindowCardContentShaderConstants
    {
        std::array<float, 4> cameraUv{};
        std::array<float, 4> cropUv{};
        std::array<float, 4> cardMask{};
    };

    static_assert(
        sizeof(WindowCardContentShaderConstants) == 48,
        "Window card content constants must match three HLSL float4 values.");

    [[nodiscard]] inline float RoundedRectangleSignedDistance(
        const float pointX,
        const float pointY,
        const float halfWidth,
        const float halfHeight,
        const float radius) noexcept
    {
        const auto innerHalfWidth = (std::max)(halfWidth - radius, 0.0f);
        const auto innerHalfHeight = (std::max)(halfHeight - radius, 0.0f);
        const auto edgeX = std::abs(pointX) - innerHalfWidth;
        const auto edgeY = std::abs(pointY) - innerHalfHeight;
        const auto outsideX = (std::max)(edgeX, 0.0f);
        const auto outsideY = (std::max)(edgeY, 0.0f);
        return std::sqrt((outsideX * outsideX) + (outsideY * outsideY)) +
            (std::min)((std::max)(edgeX, edgeY), 0.0f) - radius;
    }

#define XB_WINDOW_CARD_ROUNDED_RECT_SDF R"(
float RoundedRectangleSignedDistance(
    const float2 localPosition,
    const float2 halfSize,
    const float radius)
{
    const float2 innerHalfSize = max(halfSize - radius, 0.0f);
    const float2 edgeDistance = abs(localPosition) - innerHalfSize;
    return length(max(edgeDistance, 0.0f)) +
        min(max(edgeDistance.x, edgeDistance.y), 0.0f) - radius;
}
)"

    // The signed-distance field is evaluated directly in OutputCanvas pixel
    // coordinates. The finite smoothstep support avoids a blur texture and
    // keeps the captured window on its existing single-sample content path.
    inline constexpr char WindowCardShadowPixelShaderSource[] = R"(
cbuffer WindowCardShadowBuffer : register(b0)
{
    float4 CardRectangle;
    float4 VisualParameters;
};

struct VertexOutput
{
    float4 position : SV_Position;
    float2 uv : TEXCOORD0;
};

)" XB_WINDOW_CARD_ROUNDED_RECT_SDF R"(

float4 PSWindowCardShadow(VertexOutput input) : SV_Target
{
    const float opacity = VisualParameters.x;
    const float verticalOffset = VisualParameters.y;
    const float softness = max(VisualParameters.z, 0.001f);
    const float cornerRadius = max(VisualParameters.w, 0.001f);
    const float2 center =
        CardRectangle.xy + (CardRectangle.zw * 0.5f) +
        float2(0.0f, verticalOffset);
    const float signedDistance = RoundedRectangleSignedDistance(
        input.position.xy - center,
        CardRectangle.zw * 0.5f,
        cornerRadius);
    const float coverage =
        1.0f - smoothstep(-softness, softness, signedDistance);
    return float4(0.0f, 0.0f, 0.0f, opacity * coverage);
}
)";

    inline constexpr char WindowCardContentPixelShaderSource[] = R"(
Texture2D SourceTexture : register(t0);
SamplerState LinearSampler : register(s0);
cbuffer TransformBuffer : register(b0)
{
    float4 CameraUv;
    float4 CropUv;
    float4 CardMask;
};

struct VertexOutput
{
    float4 position : SV_Position;
    float2 uv : TEXCOORD0;
};

)" XB_WINDOW_CARD_ROUNDED_RECT_SDF R"(

float4 PSWindowCardContent(VertexOutput input) : SV_Target
{
    const float2 regionLocalUv =
        CameraUv.xy + (input.uv * CameraUv.zw);
    const float2 sourceUv =
        CropUv.xy + (regionLocalUv * CropUv.zw);
    const float4 source = SourceTexture.Sample(LinearSampler, sourceUv);
    const float2 cardSize = CardMask.xy;
    const float radius = max(CardMask.z, 0.001f);
    const float signedDistance = RoundedRectangleSignedDistance(
        (input.uv - 0.5f) * cardSize,
        cardSize * 0.5f,
        radius);
    const float mask = 1.0f - smoothstep(-0.75f, 0.75f, signedDistance);
    return float4(source.rgb * mask, source.a * mask);
}
)";

#undef XB_WINDOW_CARD_ROUNDED_RECT_SDF
}
