#pragma once

#include "WindowCardShadowPass.h"

#include <algorithm>
#include <array>
#include <cmath>
#include <cstdint>
#include <cwchar>

namespace xbpreview
{
    // Layer 3 owns only the static placement of the already-composed Window
    // Card. It deliberately has no timing, scene, camera, or motion state.
    enum class WindowStageDirection : std::uint8_t
    {
        Left,
        Front,
        Right
    };

    enum class WindowStageStrength : std::uint8_t
    {
        Level1,
        Level2,
        Level3
    };

    struct WindowStageTransformParameters
    {
        float scale{ 1.0f };
        float horizontalPlacementFraction{};
        float verticalPlacementFraction{};
        float rotationXDegrees{};
        float rotationYDegrees{};
        float perspectiveDepth{};

        [[nodiscard]] bool IsValid() const noexcept
        {
            return std::isfinite(scale) &&
                std::isfinite(horizontalPlacementFraction) &&
                std::isfinite(verticalPlacementFraction) &&
                std::isfinite(rotationXDegrees) &&
                std::isfinite(rotationYDegrees) &&
                std::isfinite(perspectiveDepth) &&
                scale > 0.0f && scale <= 1.0f &&
                std::fabs(horizontalPlacementFraction) <= 0.25f &&
                std::fabs(verticalPlacementFraction) <= 0.25f &&
                std::fabs(rotationXDegrees) <= 45.0f &&
                std::fabs(rotationYDegrees) <= 45.0f &&
                perspectiveDepth >= 0.0f && perspectiveDepth <= 2.0f;
        }

        [[nodiscard]] bool IsIdentity() const noexcept
        {
            return scale == 1.0f &&
                horizontalPlacementFraction == 0.0f &&
                verticalPlacementFraction == 0.0f &&
                rotationXDegrees == 0.0f &&
                rotationYDegrees == 0.0f &&
                perspectiveDepth == 0.0f;
        }
    };

    inline constexpr WindowStageTransformParameters WindowStageIdentityTransform{};

    // Historical human-tuned side-view baseline D/E/F. In this coordinate
    // system +X moves right and +RotationY gives the card normal a positive X
    // component, so the recovered side is RIGHT. LEFT is derived below by one
    // exact geometric mirror; it is not separately hand tuned.
    inline constexpr std::array<WindowStageTransformParameters, 3>
        WindowStageHistoricalRightTransforms{
            WindowStageTransformParameters{
                0.88f, 0.025f, -0.018f, -6.0f, 18.0f, 0.90f },
            WindowStageTransformParameters{
                0.83f, 0.040f, -0.022f, -8.0f, 24.0f, 1.00f },
            WindowStageTransformParameters{
                0.77f, 0.060f, -0.028f, -10.0f, 30.0f, 1.10f }
        };

    // FRONT has no recovered human-approved parameters. These are restrained,
    // independent A/B candidates: zero yaw, centered X, and symmetric pitch
    // perspective. They must remain labelled unvalidated until human review.
    inline constexpr std::array<WindowStageTransformParameters, 3>
        WindowStageFrontCandidateTransforms{
            WindowStageTransformParameters{
                0.94f, 0.0f, -0.008f, -3.0f, 0.0f, 0.70f },
            WindowStageTransformParameters{
                0.90f, 0.0f, -0.012f, -5.0f, 0.0f, 0.85f },
            WindowStageTransformParameters{
                0.86f, 0.0f, -0.016f, -7.0f, 0.0f, 1.00f }
        };

    [[nodiscard]] inline std::size_t WindowStageStrengthIndex(
        const WindowStageStrength strength) noexcept
    {
        switch (strength)
        {
        case WindowStageStrength::Level1:
            return 0;
        case WindowStageStrength::Level2:
            return 1;
        case WindowStageStrength::Level3:
            return 2;
        }
        return 0;
    }

    [[nodiscard]] inline bool IsKnownWindowStageStrength(
        const WindowStageStrength strength) noexcept
    {
        return strength == WindowStageStrength::Level1 ||
            strength == WindowStageStrength::Level2 ||
            strength == WindowStageStrength::Level3;
    }

    [[nodiscard]] inline bool ResolveWindowStageTransform(
        const WindowStageDirection direction,
        const WindowStageStrength strength,
        WindowStageTransformParameters& transform) noexcept
    {
        if (!IsKnownWindowStageStrength(strength))
        {
            return false;
        }
        const auto index = WindowStageStrengthIndex(strength);
        switch (direction)
        {
        case WindowStageDirection::Right:
            transform = WindowStageHistoricalRightTransforms[index];
            return true;
        case WindowStageDirection::Left:
            transform = WindowStageHistoricalRightTransforms[index];
            transform.horizontalPlacementFraction =
                -transform.horizontalPlacementFraction;
            transform.rotationYDegrees = -transform.rotationYDegrees;
            return true;
        case WindowStageDirection::Front:
            transform = WindowStageFrontCandidateTransforms[index];
            return true;
        }
        return false;
    }

    [[nodiscard]] inline bool TryParseWindowStageDirection(
        const wchar_t* const value,
        WindowStageDirection& direction) noexcept
    {
        if (value == nullptr)
        {
            return false;
        }
        if (_wcsicmp(value, L"LEFT") == 0)
        {
            direction = WindowStageDirection::Left;
            return true;
        }
        if (_wcsicmp(value, L"FRONT") == 0)
        {
            direction = WindowStageDirection::Front;
            return true;
        }
        if (_wcsicmp(value, L"RIGHT") == 0)
        {
            direction = WindowStageDirection::Right;
            return true;
        }
        return false;
    }

    [[nodiscard]] inline bool TryParseWindowStageStrength(
        const wchar_t* const value,
        WindowStageStrength& strength) noexcept
    {
        if (value == nullptr)
        {
            return false;
        }
        if (_wcsicmp(value, L"LEVEL_1") == 0)
        {
            strength = WindowStageStrength::Level1;
            return true;
        }
        if (_wcsicmp(value, L"LEVEL_2") == 0)
        {
            strength = WindowStageStrength::Level2;
            return true;
        }
        if (_wcsicmp(value, L"LEVEL_3") == 0)
        {
            strength = WindowStageStrength::Level3;
            return true;
        }
        return false;
    }

    struct WindowStageClipPoint
    {
        float x{};
        float y{};
        float z{};
        float w{ 1.0f };

        [[nodiscard]] bool IsValid() const noexcept
        {
            return std::isfinite(x) && std::isfinite(y) &&
                std::isfinite(z) && std::isfinite(w) && w >= 0.25f;
        }

        [[nodiscard]] float PixelX(const std::uint32_t canvasWidth) const noexcept
        {
            return ((x / w) + 1.0f) * 0.5f * canvasWidth;
        }

        [[nodiscard]] float PixelY(const std::uint32_t canvasHeight) const noexcept
        {
            return (1.0f - (y / w)) * 0.5f * canvasHeight;
        }
    };

    struct WindowStagePixelBounds
    {
        float left{};
        float top{};
        float right{};
        float bottom{};

        [[nodiscard]] bool IsFiniteNonEmpty() const noexcept
        {
            return std::isfinite(left) && std::isfinite(top) &&
                std::isfinite(right) && std::isfinite(bottom) &&
                right > left && bottom > top;
        }

        [[nodiscard]] bool IsInside(
            const std::uint32_t canvasWidth,
            const std::uint32_t canvasHeight,
            const float tolerance = 0.01f) const noexcept
        {
            return IsFiniteNonEmpty() &&
                left >= -tolerance && top >= -tolerance &&
                right <= static_cast<float>(canvasWidth) + tolerance &&
                bottom <= static_cast<float>(canvasHeight) + tolerance;
        }
    };

    struct WindowStageQuad
    {
        // TL, TR, BL, BR. The vertex shader maps this to two triangles.
        std::array<WindowStageClipPoint, 4> corners{};

        [[nodiscard]] bool IsValid() const noexcept
        {
            return std::all_of(
                corners.begin(), corners.end(),
                [](const WindowStageClipPoint& point)
                {
                    return point.IsValid();
                });
        }

        [[nodiscard]] WindowStagePixelBounds PixelBounds(
            const std::uint32_t canvasWidth,
            const std::uint32_t canvasHeight) const noexcept
        {
            WindowStagePixelBounds bounds{
                corners[0].PixelX(canvasWidth),
                corners[0].PixelY(canvasHeight),
                corners[0].PixelX(canvasWidth),
                corners[0].PixelY(canvasHeight)
            };
            for (std::size_t index = 1; index < corners.size(); ++index)
            {
                const auto x = corners[index].PixelX(canvasWidth);
                const auto y = corners[index].PixelY(canvasHeight);
                bounds.left = (std::min)(bounds.left, x);
                bounds.top = (std::min)(bounds.top, y);
                bounds.right = (std::max)(bounds.right, x);
                bounds.bottom = (std::max)(bounds.bottom, y);
            }
            return bounds;
        }
    };

    struct WindowStageTransformComposition
    {
        WindowStageTransformParameters parameters{};
        WindowStageQuad contentQuad{};
        WindowStageQuad shadowQuad{};
        bool identity{};
        bool valid{};
    };

    [[nodiscard]] inline bool ProjectWindowStageLocalPoint(
        const FlatWindowStageDestination& card,
        const std::uint32_t canvasWidth,
        const std::uint32_t canvasHeight,
        const WindowStageTransformParameters& transform,
        const float localX,
        const float localYDown,
        WindowStageClipPoint& point) noexcept
    {
        if (!card.IsValid() || canvasWidth == 0 || canvasHeight == 0 ||
            !transform.IsValid() || !std::isfinite(localX) ||
            !std::isfinite(localYDown))
        {
            return false;
        }

        constexpr double pi = 3.14159265358979323846;
        const auto rotationX = transform.rotationXDegrees * pi / 180.0;
        const auto rotationY = transform.rotationYDegrees * pi / 180.0;
        const auto x = static_cast<double>(localX) * transform.scale;
        const auto yUp = -static_cast<double>(localYDown) * transform.scale;
        const auto afterXDepth = yUp * std::sin(rotationX);
        const auto rotatedX =
            x * std::cos(rotationY) + afterXDepth * std::sin(rotationY);
        const auto rotatedYUp = yUp * std::cos(rotationX);
        const auto rotatedDepth =
            -x * std::sin(rotationY) + afterXDepth * std::cos(rotationY);
        const auto depthReference =
            static_cast<double>((std::max)(card.width, card.height));
        const auto homogeneousW = 1.0 -
            transform.perspectiveDepth * rotatedDepth / depthReference;
        if (!std::isfinite(homogeneousW) || homogeneousW < 0.25)
        {
            return false;
        }

        const auto centerX = card.left + card.width * 0.5 +
            transform.horizontalPlacementFraction * canvasWidth;
        const auto centerY = card.top + card.height * 0.5 +
            transform.verticalPlacementFraction * canvasHeight;
        const auto centerNdcX = 2.0 * centerX / canvasWidth - 1.0;
        const auto centerNdcY = 1.0 - 2.0 * centerY / canvasHeight;
        point = WindowStageClipPoint{
            static_cast<float>(centerNdcX * homogeneousW +
                2.0 * rotatedX / canvasWidth),
            static_cast<float>(centerNdcY * homogeneousW +
                2.0 * rotatedYUp / canvasHeight),
            static_cast<float>(0.5 * homogeneousW),
            static_cast<float>(homogeneousW)
        };
        return point.IsValid();
    }

    [[nodiscard]] inline bool ComposeWindowStageQuad(
        const FlatWindowStageDestination& card,
        const std::uint32_t canvasWidth,
        const std::uint32_t canvasHeight,
        const WindowStageTransformParameters& transform,
        const float localLeft,
        const float localTop,
        const float localRight,
        const float localBottom,
        WindowStageQuad& quad) noexcept
    {
        const std::array<std::array<float, 2>, 4> localCorners{
            std::array<float, 2>{ localLeft, localTop },
            std::array<float, 2>{ localRight, localTop },
            std::array<float, 2>{ localLeft, localBottom },
            std::array<float, 2>{ localRight, localBottom }
        };
        WindowStageQuad candidate{};
        for (std::size_t index = 0; index < localCorners.size(); ++index)
        {
            if (!ProjectWindowStageLocalPoint(
                    card, canvasWidth, canvasHeight, transform,
                    localCorners[index][0], localCorners[index][1],
                    candidate.corners[index]))
            {
                return false;
            }
        }
        if (!candidate.IsValid())
        {
            return false;
        }
        quad = candidate;
        return true;
    }

    [[nodiscard]] inline bool ComposeWindowStageTransform(
        const FlatWindowStageComposition& stage,
        const WindowCardShadowComposition& shadow,
        const std::uint32_t canvasWidth,
        const std::uint32_t canvasHeight,
        const WindowStageTransformParameters& transform,
        WindowStageTransformComposition& composition) noexcept
    {
        composition = {};
        if (!stage.window.IsValid() || !stage.UsesFullSourceTexture() ||
            !shadow.IsValid(canvasWidth, canvasHeight) ||
            !transform.IsValid())
        {
            return false;
        }

        const auto halfWidth = stage.window.width * 0.5f;
        const auto halfHeight = stage.window.height * 0.5f;
        WindowStageTransformComposition candidate{};
        candidate.parameters = transform;
        candidate.identity = transform.IsIdentity();
        if (!ComposeWindowStageQuad(
                stage.window, canvasWidth, canvasHeight, transform,
                -halfWidth, -halfHeight, halfWidth, halfHeight,
                candidate.contentQuad))
        {
            return false;
        }

        // This is the frozen Layer 2 support rectangle expressed in card-local
        // pixels, then sent through the exact same plane projection as content.
        if (!ComposeWindowStageQuad(
                stage.window, canvasWidth, canvasHeight, transform,
                -halfWidth - shadow.softnessPixels,
                -halfHeight + shadow.verticalOffsetPixels -
                    shadow.softnessPixels,
                halfWidth + shadow.softnessPixels,
                halfHeight + shadow.verticalOffsetPixels +
                    shadow.softnessPixels,
                candidate.shadowQuad))
        {
            return false;
        }

        // Content is logical card geometry and must remain fully on-canvas.
        // Shadow support is finite soft-effect geometry: perspective can move a
        // small part beyond the output while the card remains valid. The
        // renderer uses the full OutputCanvas viewport/render target, so D3D11
        // clips that overscan. Rejecting it here tears down an otherwise valid
        // capture session and leaves a stale last frame.
        candidate.valid = candidate.contentQuad.PixelBounds(
                canvasWidth, canvasHeight).IsInside(canvasWidth, canvasHeight) &&
            candidate.shadowQuad.PixelBounds(
                canvasWidth, canvasHeight).IsFiniteNonEmpty();
        if (!candidate.valid)
        {
            return false;
        }
        composition = candidate;
        return true;
    }

    struct alignas(16) WindowStageQuadShaderConstants
    {
        std::array<std::array<float, 4>, 4> clipCorners{};
    };

    static_assert(
        sizeof(WindowStageQuadShaderConstants) == 64,
        "Stage quad constants must match four HLSL float4 values.");

    struct alignas(16) WindowStageTransformedShadowShaderConstants
    {
        // Support local rectangle: left, top, width, height (Y points down).
        std::array<float, 4> supportRectangle{};
        // Card width, card height, vertical offset, corner radius.
        std::array<float, 4> cardGeometry{};
        // Opacity, softness, unused, unused.
        std::array<float, 4> visualParameters{};
    };

    static_assert(
        sizeof(WindowStageTransformedShadowShaderConstants) == 48,
        "Transformed shadow constants must match three HLSL float4 values.");

    inline constexpr char WindowStageQuadVertexShaderSource[] = R"(
cbuffer WindowStageQuadBuffer : register(b0)
{
    float4 ClipCorners[4];
};

struct VertexOutput
{
    float4 position : SV_Position;
    float2 uv : TEXCOORD0;
};

VertexOutput VSWindowStageQuad(uint vertexId : SV_VertexID)
{
    static const uint cornerIndex[6] = { 0, 1, 2, 2, 1, 3 };
    static const float2 uv[4] =
    {
        float2(0.0f, 0.0f),
        float2(1.0f, 0.0f),
        float2(0.0f, 1.0f),
        float2(1.0f, 1.0f)
    };
    const uint index = cornerIndex[vertexId];
    VertexOutput output;
    output.position = ClipCorners[index];
    // TEXCOORD intentionally has no noperspective modifier. D3D therefore
    // performs perspective-correct interpolation using SV_Position.w.
    output.uv = uv[index];
    return output;
}
)";

    inline constexpr char WindowStageTransformedShadowPixelShaderSource[] = R"(
cbuffer WindowStageTransformedShadowBuffer : register(b0)
{
    float4 SupportRectangle;
    float4 CardGeometry;
    float4 VisualParameters;
};

struct VertexOutput
{
    float4 position : SV_Position;
    float2 uv : TEXCOORD0;
};

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

float4 PSWindowStageTransformedShadow(VertexOutput input) : SV_Target
{
    const float2 localPosition =
        SupportRectangle.xy + input.uv * SupportRectangle.zw;
    const float2 cardSize = CardGeometry.xy;
    const float2 shadowCenter = float2(0.0f, CardGeometry.z);
    const float signedDistance = RoundedRectangleSignedDistance(
        localPosition - shadowCenter,
        cardSize * 0.5f,
        max(CardGeometry.w, 0.001f));
    const float softness = max(VisualParameters.y, 0.001f);
    const float coverage =
        1.0f - smoothstep(-softness, softness, signedDistance);
    return float4(0.0f, 0.0f, 0.0f, VisualParameters.x * coverage);
}
)";
}
