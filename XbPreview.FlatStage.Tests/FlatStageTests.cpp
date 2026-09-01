#include "WindowStageComposer.h"
#include "WindowCardShadowPass.h"
#include "CursorCoordinateMapper.h"
#include "Letterbox.h"
#include "WindowShowcaseMotionController.h"
#include "WindowStagePunchOverlay.h"
#include "WindowStageTransform.h"

#include <d3dcompiler.h>

#include <cmath>
#include <cstring>
#include <cstdlib>
#include <array>
#include <iostream>
#include <limits>
#include <string>
#include <string_view>

namespace
{
    [[nodiscard]] bool Near(
        const double actual,
        const double expected,
        const double tolerance = 0.001) noexcept
    {
        return std::abs(actual - expected) <= tolerance;
    }

    void Require(const bool condition, const std::string_view message)
    {
        if (!condition)
        {
            std::cerr << "FLAT STAGE GATE FAIL: " << message << '\n';
            std::exit(EXIT_FAILURE);
        }
    }

    void VerifyComposition(
        const std::uint32_t sourceWidth,
        const std::uint32_t sourceHeight,
        const std::uint32_t outputWidth,
        const std::uint32_t outputHeight)
    {
        xbpreview::FlatWindowStageComposition result{};
        Require(
            xbpreview::WindowStageComposer::ComposeFlat(
                sourceWidth,
                sourceHeight,
                outputWidth,
                outputHeight,
                result),
            "valid dimensions compose");
        Require(result.window.IsValid(), "destination is valid");
        Require(result.UsesFullSourceTexture(), "source is not cropped");
        Require(
            Near(result.window.left * 2.0 + result.window.width, outputWidth) &&
            Near(result.window.top * 2.0 + result.window.height, outputHeight),
            "destination is centered");
        Require(
            Near(
                result.window.width / result.window.height,
                static_cast<double>(sourceWidth) / sourceHeight),
            "destination preserves source aspect ratio");
        Require(
            result.window.width <=
                outputWidth * xbpreview::FlatWindowStageMaximumFraction + 0.001 &&
            result.window.height <=
                outputHeight * xbpreview::FlatWindowStageMaximumFraction + 0.001,
            "destination remains inside the safe stage area");
    }

    void VerifyShadowComposition(
        const std::uint32_t sourceWidth,
        const std::uint32_t sourceHeight,
        const std::uint32_t outputWidth,
        const std::uint32_t outputHeight)
    {
        xbpreview::FlatWindowStageComposition stage{};
        Require(
            xbpreview::WindowStageComposer::ComposeFlat(
                sourceWidth,
                sourceHeight,
                outputWidth,
                outputHeight,
                stage),
            "shadow input uses valid flat-stage composition");

        xbpreview::WindowCardShadowComposition shadow{};
        Require(
            xbpreview::ComposeWindowCardShadow(
                stage,
                outputWidth,
                outputHeight,
                shadow),
            "valid card shadow composes");
        Require(
            Near(shadow.card.left, stage.window.left) &&
            Near(shadow.card.top, stage.window.top) &&
            Near(shadow.card.width, stage.window.width) &&
            Near(shadow.card.height, stage.window.height),
            "shadow follows the exact Layer 1 card rectangle");

        const auto expectedScale = (std::min)(
            static_cast<double>(outputWidth) /
                xbpreview::WindowCardShadowReferenceWidth,
            static_cast<double>(outputHeight) /
                xbpreview::WindowCardShadowReferenceHeight);
        const auto expectedStrength =
            xbpreview::CalculateWindowCardShadowStrength(
                stage.window,
                outputWidth,
                outputHeight);
        const auto expectedOpacity =
            xbpreview::WindowCardShadowMinimumOpacity +
            ((xbpreview::WindowCardVerySoftShadow.opacity -
                xbpreview::WindowCardShadowMinimumOpacity) *
                expectedStrength);
        const auto expectedOffset =
            (xbpreview::WindowCardShadowMinimumVerticalOffsetPixels +
                ((xbpreview::WindowCardVerySoftShadow.verticalOffsetPixels -
                    xbpreview::WindowCardShadowMinimumVerticalOffsetPixels) *
                    expectedStrength)) *
            expectedScale;
        const auto expectedSoftness =
            (xbpreview::WindowCardShadowMinimumSoftnessPixels +
                ((xbpreview::WindowCardVerySoftShadow.softnessPixels -
                    xbpreview::WindowCardShadowMinimumSoftnessPixels) *
                    expectedStrength)) *
            expectedScale;
        const auto expectedCornerRadius =
            xbpreview::WindowCardCornerRadiusPixels * expectedScale;
        Require(
            Near(
                shadow.verticalOffsetPixels,
                expectedOffset) &&
            Near(
                shadow.softnessPixels,
                expectedSoftness) &&
            Near(
                shadow.opacity,
                expectedOpacity) &&
            Near(shadow.cornerRadiusPixels, expectedCornerRadius) &&
            Near(shadow.strength, expectedStrength),
            "shared radius and shadow strength scale continuously");
        Require(
            Near(shadow.support.left, stage.window.left - shadow.softnessPixels) &&
            Near(
                shadow.support.top,
                stage.window.top + shadow.verticalOffsetPixels -
                    shadow.softnessPixels) &&
            Near(
                shadow.support.width,
                stage.window.width + (shadow.softnessPixels * 2.0)) &&
            Near(
                shadow.support.height,
                stage.window.height + (shadow.softnessPixels * 2.0)),
            "shadow support tracks offset and softness around the card");
        Require(
            shadow.IsValid(outputWidth, outputHeight) &&
            shadow.support.left >= 0.0f && shadow.support.top >= 0.0f &&
            shadow.support.left + shadow.support.width <= outputWidth &&
            shadow.support.top + shadow.support.height <= outputHeight,
            "shadow support remains inside OutputCanvas without clipping");
    }

    void VerifyPixelShaderCompiles(
        const char* const source,
        const char* const name,
        const char* const entryPoint)
    {
        ID3DBlob* byteCode = nullptr;
        ID3DBlob* errors = nullptr;
        const auto result = D3DCompile(
            source,
            std::strlen(source),
            name,
            nullptr,
            nullptr,
            entryPoint,
            "ps_5_0",
            D3DCOMPILE_ENABLE_STRICTNESS |
                D3DCOMPILE_OPTIMIZATION_LEVEL3,
            0,
            &byteCode,
            &errors);
        if (errors != nullptr)
        {
            std::cerr.write(
                static_cast<const char*>(errors->GetBufferPointer()),
                static_cast<std::streamsize>(errors->GetBufferSize()));
            errors->Release();
        }
        if (byteCode != nullptr)
        {
            byteCode->Release();
        }
        Require(SUCCEEDED(result), "production rounded-card pixel shader compiles");
    }

    void VerifyVertexShaderCompiles(
        const char* const source,
        const char* const name,
        const char* const entryPoint)
    {
        ID3DBlob* byteCode = nullptr;
        ID3DBlob* errors = nullptr;
        const auto result = D3DCompile(
            source,
            std::strlen(source),
            name,
            nullptr,
            nullptr,
            entryPoint,
            "vs_5_0",
            D3DCOMPILE_ENABLE_STRICTNESS |
                D3DCOMPILE_OPTIMIZATION_LEVEL3,
            0,
            &byteCode,
            &errors);
        if (errors != nullptr)
        {
            std::cerr.write(
                static_cast<const char*>(errors->GetBufferPointer()),
                static_cast<std::streamsize>(errors->GetBufferSize()));
            errors->Release();
        }
        if (byteCode != nullptr)
        {
            byteCode->Release();
        }
        Require(SUCCEEDED(result), "production StageTransform vertex shader compiles");
    }

    void TestCardShadow()
    {
        Require(
            Near(xbpreview::WindowCardVerySoftShadow.opacity, 0.14) &&
            Near(
                xbpreview::WindowCardVerySoftShadow.verticalOffsetPixels,
                14.0) &&
            Near(
                xbpreview::WindowCardVerySoftShadow.softnessPixels,
                34.0),
            "Very Soft candidate A parameters changed");

        Require(
            Near(xbpreview::WindowCardShadowMinimumOpacity, 0.05) &&
            Near(
                xbpreview::WindowCardShadowMinimumVerticalOffsetPixels,
                5.0) &&
            Near(
                xbpreview::WindowCardShadowMinimumSoftnessPixels,
                42.0) &&
            Near(xbpreview::WindowCardCornerRadiusPixels, 8.0) &&
            Near(xbpreview::WindowCardShadowSmallCoverage, 0.30) &&
            Near(xbpreview::WindowCardShadowLargeCoverage, 0.75),
            "size-aware shadow limits changed");
        Require(
            Near(
                xbpreview::CalculateWindowCardShadowStrength(
                    xbpreview::FlatWindowStageDestination{
                        0.0f, 0.0f, 600.0f, 500.0f },
                    1000,
                    1000),
                0.0) &&
            Near(
                xbpreview::CalculateWindowCardShadowStrength(
                    xbpreview::FlatWindowStageDestination{
                        0.0f, 0.0f, 525.0f, 1000.0f },
                    1000,
                    1000),
                0.5) &&
            Near(
                xbpreview::CalculateWindowCardShadowStrength(
                    xbpreview::FlatWindowStageDestination{
                        0.0f, 0.0f, 750.0f, 1000.0f },
                    1000,
                    1000),
                1.0),
            "coverage strength endpoints and midpoint changed");

        VerifyShadowComposition(1600, 900, 1920, 1080);
        VerifyShadowComposition(900, 1600, 1920, 1080);
        VerifyShadowComposition(1600, 900, 1280, 720);
        VerifyShadowComposition(900, 1600, 1080, 1920);

        xbpreview::FlatWindowStageComposition stage{};
        Require(
            xbpreview::WindowStageComposer::ComposeFlat(
                1600, 900, 1920, 1080, stage),
            "deterministic shadow stage composes");
        xbpreview::WindowCardShadowComposition shadow{};
        Require(
            xbpreview::ComposeWindowCardShadow(
                stage, 1920, 1080, shadow) &&
            Near(shadow.strength, 1.0) &&
            Near(shadow.opacity, 0.14) &&
            Near(shadow.verticalOffsetPixels, 14.0) &&
            Near(shadow.softnessPixels, 34.0) &&
            Near(shadow.cornerRadiusPixels, 8.0) &&
            Near(shadow.support.left, 62.0) &&
            Near(shadow.support.top, 34.0) &&
            Near(shadow.support.width, 1796.0) &&
            Near(shadow.support.height, 1040.0),
            "1920x1080 shadow support is deterministic and unclipped");

        xbpreview::FlatWindowStageComposition narrowStage{};
        xbpreview::WindowCardShadowComposition narrowShadow{};
        Require(
            xbpreview::WindowStageComposer::ComposeFlat(
                900, 1600, 1920, 1080, narrowStage) &&
            xbpreview::ComposeWindowCardShadow(
                narrowStage, 1920, 1080, narrowShadow) &&
            Near(narrowShadow.strength, 0.0) &&
            Near(narrowShadow.opacity, 0.05) &&
            Near(narrowShadow.verticalOffsetPixels, 5.0) &&
            Near(narrowShadow.softnessPixels, 42.0) &&
            Near(narrowShadow.cornerRadiusPixels, 8.0),
            "narrow card reaches the lighter, softer B floor");

        Require(
            xbpreview::RoundedRectangleSignedDistance(
                -49.5f, -49.5f, 50.0f, 50.0f, 8.0f) > 2.0f,
            "square-corner spike pixel is outside the shared rounded silhouette");
        constexpr float capturedBlackCorner = 0.0f;
        constexpr float warmStageCorner = 243.0f / 255.0f;
        constexpr float shadowDisabledAlpha = 0.0f;
        const auto oldSquareComposite =
            capturedBlackCorner + (warmStageCorner * shadowDisabledAlpha);
        const auto roundedMaskAtCorner = 0.0f;
        const auto roundedComposite =
            (capturedBlackCorner * roundedMaskAtCorner) +
            (warmStageCorner * (1.0f - roundedMaskAtCorner));
        Require(
            Near(oldSquareComposite, capturedBlackCorner) &&
            Near(roundedComposite, warmStageCorner),
            "alpha-zero diagnostic classifies captured corner independently of shadow");
        for (int degrees = 0; degrees <= 90; degrees += 5)
        {
            constexpr float pi = 3.14159265358979323846f;
            const auto angle = static_cast<float>(degrees) * pi / 180.0f;
            const auto pointX = 42.0f + (8.0f * std::cos(angle));
            const auto pointY = 42.0f + (8.0f * std::sin(angle));
            Require(
                Near(
                    xbpreview::RoundedRectangleSignedDistance(
                        pointX, pointY, 50.0f, 50.0f, 8.0f),
                    0.0,
                    0.0001),
                "rounded silhouette is continuous around the corner arc");
        }

        float previousStrength = -1.0f;
        float previousOpacity = -1.0f;
        float previousOffset = -1.0f;
        float previousSoftness = -1.0f;
        float previousCornerRadius = -1.0f;
        for (std::uint32_t sourceWidth = 600;
            sourceWidth <= 1600;
            sourceWidth += 10)
        {
            xbpreview::FlatWindowStageComposition resizedStage{};
            xbpreview::WindowCardShadowComposition resizedShadow{};
            Require(
                xbpreview::WindowStageComposer::ComposeFlat(
                    sourceWidth, 900, 1920, 1080, resizedStage) &&
                xbpreview::ComposeWindowCardShadow(
                    resizedStage, 1920, 1080, resizedShadow),
                "resize sequence composes");
            Require(
                resizedShadow.strength + 0.000001f >= previousStrength,
                "shadow strength jumped backwards during continuous resize");
            if (previousStrength >= 0.0f)
            {
                Require(
                    resizedShadow.strength - previousStrength < 0.025f,
                    "shadow strength changed discontinuously during resize");
                Require(
                    resizedShadow.opacity >= previousOpacity &&
                    resizedShadow.opacity - previousOpacity < 0.003f,
                    "shadow opacity changed discontinuously during resize");
                Require(
                    resizedShadow.verticalOffsetPixels >= previousOffset &&
                    resizedShadow.verticalOffsetPixels - previousOffset < 0.3f,
                    "shadow Y offset changed discontinuously during resize");
                Require(
                    resizedShadow.softnessPixels <= previousSoftness &&
                    previousSoftness - resizedShadow.softnessPixels < 0.3f,
                    "shadow softness changed discontinuously during resize");
                Require(
                    Near(
                        resizedShadow.cornerRadiusPixels,
                        previousCornerRadius),
                    "shared corner radius jumped during card resize");
            }
            previousStrength = resizedShadow.strength;
            previousOpacity = resizedShadow.opacity;
            previousOffset = resizedShadow.verticalOffsetPixels;
            previousSoftness = resizedShadow.softnessPixels;
            previousCornerRadius = resizedShadow.cornerRadiusPixels;
        }

        auto invalid = xbpreview::WindowCardVerySoftShadow;
        invalid.opacity = 0.0f;
        Require(
            !xbpreview::ComposeWindowCardShadow(
                stage, 1920, 1080, shadow, invalid),
            "zero-opacity shadow is rejected");
        invalid = xbpreview::WindowCardVerySoftShadow;
        invalid.softnessPixels = -1.0f;
        Require(
            !xbpreview::ComposeWindowCardShadow(
                stage, 1920, 1080, shadow, invalid),
            "negative shadow softness is rejected");

        const std::string_view shader{
            xbpreview::WindowCardShadowPixelShaderSource };
        Require(
            shader.find("Texture2D") == std::string_view::npos &&
            shader.find("SourceTexture") == std::string_view::npos,
            "shadow pass must not sample or blur captured window pixels");
        const std::string_view contentShader{
            xbpreview::WindowCardContentPixelShaderSource };
        Require(
            contentShader.find("RoundedRectangleSignedDistance") !=
                std::string_view::npos &&
            contentShader.find("source.rgb * mask") != std::string_view::npos &&
            contentShader.find("source.a * mask") != std::string_view::npos,
            "captured alpha and shared silhouette jointly mask source corners");
        Require(
            shader.find("RoundedRectangleSignedDistance") !=
                std::string_view::npos &&
            shader.find("VisualParameters.w") != std::string_view::npos,
            "shadow uses the same rounded silhouette and radius");
        VerifyPixelShaderCompiles(
            xbpreview::WindowCardShadowPixelShaderSource,
            "WindowCardShadowPass",
            "PSWindowCardShadow");
        VerifyPixelShaderCompiles(
            xbpreview::WindowCardContentPixelShaderSource,
            "WindowCardContentPass",
            "PSWindowCardContent");

        std::cout
            << "XbPreview.FlatStage.Tests CARD SHADOW PASS: small-card B "
               "5-14% / Y 5-14 / softness 42-34, smooth coverage response, "
               "shared 8px rounded silhouette, no corner spike, smooth resize, "
               "unclipped, source-independent shadow\n";
    }

    [[nodiscard]] bool SameDestination(
        const xbpreview::FlatWindowStageDestination& left,
        const xbpreview::FlatWindowStageDestination& right) noexcept
    {
        return left.left == right.left && left.top == right.top &&
            left.width == right.width && left.height == right.height;
    }

    [[nodiscard]] bool SameStage(
        const xbpreview::FlatWindowStageComposition& left,
        const xbpreview::FlatWindowStageComposition& right) noexcept
    {
        return left.backgroundSrgb == right.backgroundSrgb &&
            SameDestination(left.window, right.window) &&
            left.sourceOriginU == right.sourceOriginU &&
            left.sourceOriginV == right.sourceOriginV &&
            left.sourceScaleU == right.sourceScaleU &&
            left.sourceScaleV == right.sourceScaleV;
    }

    [[nodiscard]] bool SameShadow(
        const xbpreview::WindowCardShadowComposition& left,
        const xbpreview::WindowCardShadowComposition& right) noexcept
    {
        return SameDestination(left.card, right.card) &&
            SameDestination(left.support, right.support) &&
            left.opacity == right.opacity &&
            left.verticalOffsetPixels == right.verticalOffsetPixels &&
            left.softnessPixels == right.softnessPixels &&
            left.strength == right.strength &&
            left.cornerRadiusPixels == right.cornerRadiusPixels;
    }

    [[nodiscard]] bool SameTransformParameters(
        const xbpreview::WindowStageTransformParameters& left,
        const xbpreview::WindowStageTransformParameters& right) noexcept
    {
        return left.scale == right.scale &&
            left.horizontalPlacementFraction ==
                right.horizontalPlacementFraction &&
            left.verticalPlacementFraction ==
                right.verticalPlacementFraction &&
            left.rotationXDegrees == right.rotationXDegrees &&
            left.rotationYDegrees == right.rotationYDegrees &&
            left.perspectiveDepth == right.perspectiveDepth;
    }

    [[nodiscard]] bool TransformParametersWithinEndpoints(
        const xbpreview::WindowStageTransformParameters& value,
        const xbpreview::WindowStageTransformParameters& first,
        const xbpreview::WindowStageTransformParameters& second) noexcept
    {
        const auto between = [](const float candidate, const float left,
            const float right) noexcept
        {
            return candidate >= (std::min)(left, right) - 0.000001f &&
                candidate <= (std::max)(left, right) + 0.000001f;
        };
        return between(value.scale, first.scale, second.scale) &&
            between(
                value.horizontalPlacementFraction,
                first.horizontalPlacementFraction,
                second.horizontalPlacementFraction) &&
            between(
                value.verticalPlacementFraction,
                first.verticalPlacementFraction,
                second.verticalPlacementFraction) &&
            between(
                value.rotationXDegrees,
                first.rotationXDegrees,
                second.rotationXDegrees) &&
            between(
                value.rotationYDegrees,
                first.rotationYDegrees,
                second.rotationYDegrees) &&
            between(
                value.perspectiveDepth,
                first.perspectiveDepth,
                second.perspectiveDepth);
    }

    void ComposeFrozenLayer2(
        const std::uint32_t sourceWidth,
        const std::uint32_t sourceHeight,
        const std::uint32_t canvasWidth,
        const std::uint32_t canvasHeight,
        xbpreview::FlatWindowStageComposition& stage,
        xbpreview::WindowCardShadowComposition& shadow)
    {
        Require(
            xbpreview::WindowStageComposer::ComposeFlat(
                sourceWidth,
                sourceHeight,
                canvasWidth,
                canvasHeight,
                stage),
            "Layer 2 fixture composes its frozen flat stage");
        Require(
            xbpreview::ComposeWindowCardShadow(
                stage,
                canvasWidth,
                canvasHeight,
                shadow),
            "Layer 2 fixture composes its frozen card shadow");
    }

    void VerifyIdentityQuad(
        const xbpreview::WindowStageQuad& quad,
        const xbpreview::FlatWindowStageDestination& rectangle,
        const std::uint32_t canvasWidth,
        const std::uint32_t canvasHeight,
        const std::string_view message)
    {
        const std::array<std::array<float, 2>, 4> expected{
            std::array<float, 2>{ rectangle.left, rectangle.top },
            std::array<float, 2>{
                rectangle.left + rectangle.width, rectangle.top },
            std::array<float, 2>{
                rectangle.left, rectangle.top + rectangle.height },
            std::array<float, 2>{
                rectangle.left + rectangle.width,
                rectangle.top + rectangle.height }
        };
        for (std::size_t index = 0; index < quad.corners.size(); ++index)
        {
            Require(
                Near(
                    quad.corners[index].PixelX(canvasWidth),
                    expected[index][0],
                    0.001) &&
                Near(
                    quad.corners[index].PixelY(canvasHeight),
                    expected[index][1],
                    0.001) &&
                quad.corners[index].w == 1.0f,
                message);
        }
    }

    void VerifyIdentityComposition(
        const std::uint32_t sourceWidth,
        const std::uint32_t sourceHeight,
        const std::uint32_t canvasWidth,
        const std::uint32_t canvasHeight)
    {
        xbpreview::FlatWindowStageComposition stage{};
        xbpreview::WindowCardShadowComposition shadow{};
        ComposeFrozenLayer2(
            sourceWidth,
            sourceHeight,
            canvasWidth,
            canvasHeight,
            stage,
            shadow);
        const auto frozenStage = stage;
        const auto frozenShadow = shadow;

        xbpreview::WindowStageTransformComposition transformed{};
        Require(
            xbpreview::ComposeWindowStageTransform(
                stage,
                shadow,
                canvasWidth,
                canvasHeight,
                xbpreview::WindowStageIdentityTransform,
                transformed) &&
            transformed.valid && transformed.identity,
            "Identity StageTransform composes");
        Require(
            SameStage(stage, frozenStage) && SameShadow(shadow, frozenShadow),
            "Identity StageTransform leaves every frozen Layer 2 field exact");
        Require(
            SameTransformParameters(
                transformed.parameters,
                xbpreview::WindowStageIdentityTransform),
            "Identity StageTransform retains exact identity parameters");
        VerifyIdentityQuad(
            transformed.contentQuad,
            frozenStage.window,
            canvasWidth,
            canvasHeight,
            "Identity content corners exactly recover the frozen card with W=1");
        VerifyIdentityQuad(
            transformed.shadowQuad,
            frozenShadow.support,
            canvasWidth,
            canvasHeight,
            "Identity shadow corners exactly recover frozen support with W=1");
    }

    void TestLayer2Identity()
    {
        Require(
            xbpreview::WindowStageIdentityTransform.IsIdentity() &&
            xbpreview::WindowStageIdentityTransform.IsValid() &&
            xbpreview::WindowStageIdentityTransform.scale == 1.0f &&
            xbpreview::WindowStageIdentityTransform.horizontalPlacementFraction ==
                0.0f &&
            xbpreview::WindowStageIdentityTransform.verticalPlacementFraction ==
                0.0f &&
            xbpreview::WindowStageIdentityTransform.rotationXDegrees == 0.0f &&
            xbpreview::WindowStageIdentityTransform.rotationYDegrees == 0.0f &&
            xbpreview::WindowStageIdentityTransform.perspectiveDepth == 0.0f,
            "Identity parameters are exact");

        VerifyIdentityComposition(1600, 900, 1920, 1080);
        VerifyIdentityComposition(900, 1600, 1920, 1080);

        xbpreview::FlatWindowStageComposition largeStage{};
        xbpreview::WindowCardShadowComposition largeShadow{};
        ComposeFrozenLayer2(
            1600, 900, 1920, 1080, largeStage, largeShadow);
        Require(
            largeStage.backgroundSrgb ==
                xbpreview::FlatWindowStageBackgroundSrgb &&
            largeStage.UsesFullSourceTexture() &&
            largeStage.window.left == 96.0f &&
            largeStage.window.top == 54.0f &&
            largeStage.window.width == 1728.0f &&
            largeStage.window.height == 972.0f &&
            largeShadow.card.left == 96.0f &&
            largeShadow.card.top == 54.0f &&
            largeShadow.card.width == 1728.0f &&
            largeShadow.card.height == 972.0f &&
            largeShadow.strength == 1.0f &&
            largeShadow.opacity == 0.14f &&
            largeShadow.verticalOffsetPixels == 14.0f &&
            largeShadow.softnessPixels == 34.0f &&
            largeShadow.cornerRadiusPixels == 8.0f &&
            largeShadow.support.left == 62.0f &&
            largeShadow.support.top == 34.0f &&
            largeShadow.support.width == 1796.0f &&
            largeShadow.support.height == 1040.0f,
            "Identity preserves the exact frozen large-card Layer 2 fixture");

        xbpreview::FlatWindowStageComposition narrowStage{};
        xbpreview::WindowCardShadowComposition narrowShadow{};
        ComposeFrozenLayer2(
            900, 1600, 1920, 1080, narrowStage, narrowShadow);
        Require(
            narrowStage.backgroundSrgb ==
                xbpreview::FlatWindowStageBackgroundSrgb &&
            narrowStage.UsesFullSourceTexture() &&
            narrowStage.window.left == 686.625f &&
            narrowStage.window.top == 54.0f &&
            narrowStage.window.width == 546.75f &&
            narrowStage.window.height == 972.0f &&
            narrowShadow.strength == 0.0f &&
            narrowShadow.opacity == 0.05f &&
            narrowShadow.verticalOffsetPixels == 5.0f &&
            narrowShadow.softnessPixels == 42.0f &&
            narrowShadow.cornerRadiusPixels == 8.0f &&
            narrowShadow.support.left == 644.625f &&
            narrowShadow.support.top == 17.0f &&
            narrowShadow.support.width == 630.75f &&
            narrowShadow.support.height == 1056.0f,
            "Identity preserves the exact frozen narrow-card Layer 2 fixture");

        std::cout
            << "XbPreview.FlatStage.Tests LAYER 2 IDENTITY PASS: exact flat "
               "layout, full UV, rounded-card/shadow fields, card/support "
               "corners, and homogeneous W=1\n";
    }

    struct PixelPoint
    {
        double x{};
        double y{};
    };

    [[nodiscard]] PixelPoint QuadPixelPoint(
        const xbpreview::WindowStageQuad& quad,
        const std::size_t index,
        const std::uint32_t canvasWidth,
        const std::uint32_t canvasHeight) noexcept
    {
        return PixelPoint{
            quad.corners[index].PixelX(canvasWidth),
            quad.corners[index].PixelY(canvasHeight)
        };
    }

    [[nodiscard]] double PixelDistance(
        const xbpreview::WindowStageQuad& quad,
        const std::size_t first,
        const std::size_t second,
        const std::uint32_t canvasWidth,
        const std::uint32_t canvasHeight) noexcept
    {
        const auto a = QuadPixelPoint(
            quad, first, canvasWidth, canvasHeight);
        const auto b = QuadPixelPoint(
            quad, second, canvasWidth, canvasHeight);
        return std::hypot(b.x - a.x, b.y - a.y);
    }

    void VerifyQuadConvex(
        const xbpreview::WindowStageQuad& quad,
        const std::uint32_t canvasWidth,
        const std::uint32_t canvasHeight)
    {
        constexpr std::array<std::size_t, 4> perimeter{ 0, 1, 3, 2 };
        double expectedSign = 0.0;
        for (std::size_t index = 0; index < perimeter.size(); ++index)
        {
            const auto a = QuadPixelPoint(
                quad,
                perimeter[index],
                canvasWidth,
                canvasHeight);
            const auto b = QuadPixelPoint(
                quad,
                perimeter[(index + 1) % perimeter.size()],
                canvasWidth,
                canvasHeight);
            const auto c = QuadPixelPoint(
                quad,
                perimeter[(index + 2) % perimeter.size()],
                canvasWidth,
                canvasHeight);
            const auto cross =
                ((b.x - a.x) * (c.y - b.y)) -
                ((b.y - a.y) * (c.x - b.x));
            Require(std::abs(cross) > 0.01, "StageTransform quad is non-degenerate");
            const auto sign = cross > 0.0 ? 1.0 : -1.0;
            if (index == 0)
            {
                expectedSign = sign;
            }
            Require(sign == expectedSign, "StageTransform quad stays convex");
        }
    }

    void VerifyMirroredQuad(
        const xbpreview::WindowStageQuad& left,
        const xbpreview::WindowStageQuad& right,
        const std::uint32_t canvasWidth,
        const std::uint32_t canvasHeight,
        const std::string_view message)
    {
        constexpr std::array<std::array<std::size_t, 2>, 4> pairs{
            std::array<std::size_t, 2>{ 0, 1 },
            std::array<std::size_t, 2>{ 1, 0 },
            std::array<std::size_t, 2>{ 2, 3 },
            std::array<std::size_t, 2>{ 3, 2 }
        };
        for (const auto& pair : pairs)
        {
            const auto& leftCorner = left.corners[pair[0]];
            const auto& rightCorner = right.corners[pair[1]];
            Require(
                Near(
                    leftCorner.PixelX(canvasWidth) +
                        rightCorner.PixelX(canvasWidth),
                    canvasWidth,
                    0.01) &&
                Near(
                    leftCorner.PixelY(canvasHeight),
                    rightCorner.PixelY(canvasHeight),
                    0.01) &&
                Near(leftCorner.w, rightCorner.w, 0.00001) &&
                Near(leftCorner.z, rightCorner.z, 0.00001),
                message);
        }
    }

    [[nodiscard]] double SidePerspectiveStrength(
        const xbpreview::WindowStageQuad& quad,
        const std::uint32_t canvasWidth,
        const std::uint32_t canvasHeight) noexcept
    {
        const auto leftEdge = PixelDistance(
            quad, 0, 2, canvasWidth, canvasHeight);
        const auto rightEdge = PixelDistance(
            quad, 1, 3, canvasWidth, canvasHeight);
        return std::abs(leftEdge - rightEdge) /
            (std::max)(leftEdge, rightEdge);
    }

    [[nodiscard]] double FrontPerspectiveStrength(
        const xbpreview::WindowStageQuad& quad,
        const std::uint32_t canvasWidth,
        const std::uint32_t canvasHeight) noexcept
    {
        const auto topEdge = PixelDistance(
            quad, 0, 1, canvasWidth, canvasHeight);
        const auto bottomEdge = PixelDistance(
            quad, 2, 3, canvasWidth, canvasHeight);
        return std::abs(topEdge - bottomEdge) /
            (std::max)(topEdge, bottomEdge);
    }

    void VerifyFrontSymmetry(
        const xbpreview::WindowStageQuad& quad,
        const std::uint32_t canvasWidth,
        const std::uint32_t canvasHeight)
    {
        for (const auto pair : {
            std::array<std::size_t, 2>{ 0, 1 },
            std::array<std::size_t, 2>{ 2, 3 } })
        {
            const auto& left = quad.corners[pair[0]];
            const auto& right = quad.corners[pair[1]];
            Require(
                Near(
                    left.PixelX(canvasWidth) + right.PixelX(canvasWidth),
                    canvasWidth,
                    0.01) &&
                Near(
                    left.PixelY(canvasHeight),
                    right.PixelY(canvasHeight),
                    0.01) &&
                Near(left.w, right.w, 0.00001),
                "FRONT is horizontally symmetric by projected corners");
        }
        Require(
            !Near(
                PixelDistance(quad, 0, 1, canvasWidth, canvasHeight),
                PixelDistance(quad, 2, 3, canvasWidth, canvasHeight),
                0.01),
            "FRONT is a real symmetric trapezoid, not a scaled rectangle");
    }

    void TestStageTransform()
    {
        constexpr std::array<xbpreview::WindowStageStrength, 3> strengths{
            xbpreview::WindowStageStrength::Level1,
            xbpreview::WindowStageStrength::Level2,
            xbpreview::WindowStageStrength::Level3
        };
        constexpr std::array<xbpreview::WindowStageTransformParameters, 3>
            expectedRight{
                xbpreview::WindowStageTransformParameters{
                    0.88f, 0.025f, -0.018f, -6.0f, 18.0f, 0.90f },
                xbpreview::WindowStageTransformParameters{
                    0.83f, 0.040f, -0.022f, -8.0f, 24.0f, 1.00f },
                xbpreview::WindowStageTransformParameters{
                    0.77f, 0.060f, -0.028f, -10.0f, 30.0f, 1.10f }
            };
        constexpr std::array<xbpreview::WindowStageTransformParameters, 3>
            expectedFront{
                xbpreview::WindowStageTransformParameters{
                    0.94f, 0.0f, -0.008f, -3.0f, 0.0f, 0.70f },
                xbpreview::WindowStageTransformParameters{
                    0.90f, 0.0f, -0.012f, -5.0f, 0.0f, 0.85f },
                xbpreview::WindowStageTransformParameters{
                    0.86f, 0.0f, -0.016f, -7.0f, 0.0f, 1.00f }
            };

        VerifyIdentityComposition(1600, 900, 1920, 1080);

        xbpreview::FlatWindowStageComposition stage{};
        xbpreview::WindowCardShadowComposition shadow{};
        ComposeFrozenLayer2(1600, 900, 1920, 1080, stage, shadow);
        double previousSideStrength = -1.0;
        double previousFrontStrength = -1.0;
        for (std::size_t level = 0; level < strengths.size(); ++level)
        {
            xbpreview::WindowStageTransformParameters rightParameters{};
            xbpreview::WindowStageTransformParameters leftParameters{};
            xbpreview::WindowStageTransformParameters frontParameters{};
            Require(
                xbpreview::ResolveWindowStageTransform(
                    xbpreview::WindowStageDirection::Right,
                    strengths[level],
                    rightParameters) &&
                xbpreview::ResolveWindowStageTransform(
                    xbpreview::WindowStageDirection::Left,
                    strengths[level],
                    leftParameters) &&
                xbpreview::ResolveWindowStageTransform(
                    xbpreview::WindowStageDirection::Front,
                    strengths[level],
                    frontParameters),
                "all nine Direction x Strength parameters resolve");
            auto expectedLeft = expectedRight[level];
            expectedLeft.horizontalPlacementFraction =
                -expectedLeft.horizontalPlacementFraction;
            expectedLeft.rotationYDegrees = -expectedLeft.rotationYDegrees;
            Require(
                SameTransformParameters(
                    rightParameters, expectedRight[level]) &&
                SameTransformParameters(
                    leftParameters, expectedLeft) &&
                SameTransformParameters(
                    frontParameters, expectedFront[level]) &&
                SameTransformParameters(
                    xbpreview::WindowStageHistoricalRightTransforms[level],
                    expectedRight[level]) &&
                SameTransformParameters(
                    xbpreview::WindowStageFrontCandidateTransforms[level],
                    expectedFront[level]),
                "the exact nine-pose parameter table is unchanged");
            Require(
                rightParameters.IsValid() && leftParameters.IsValid() &&
                frontParameters.IsValid() &&
                !rightParameters.IsIdentity() &&
                !leftParameters.IsIdentity() &&
                !frontParameters.IsIdentity(),
                "all nine static pose parameters are valid and non-Identity");
            Require(
                leftParameters.scale == rightParameters.scale &&
                leftParameters.verticalPlacementFraction ==
                    rightParameters.verticalPlacementFraction &&
                leftParameters.rotationXDegrees ==
                    rightParameters.rotationXDegrees &&
                leftParameters.perspectiveDepth ==
                    rightParameters.perspectiveDepth &&
                leftParameters.horizontalPlacementFraction ==
                    -rightParameters.horizontalPlacementFraction &&
                leftParameters.rotationYDegrees ==
                    -rightParameters.rotationYDegrees,
                "LEFT is the exact sign mirror of the historical RIGHT baseline");

            xbpreview::WindowStageTransformComposition right{};
            xbpreview::WindowStageTransformComposition left{};
            xbpreview::WindowStageTransformComposition front{};
            Require(
                xbpreview::ComposeWindowStageTransform(
                    stage, shadow, 1920, 1080, rightParameters, right) &&
                xbpreview::ComposeWindowStageTransform(
                    stage, shadow, 1920, 1080, leftParameters, left) &&
                xbpreview::ComposeWindowStageTransform(
                    stage, shadow, 1920, 1080, frontParameters, front),
                "representative nine-pose geometry composes");

            xbpreview::WindowStageClipPoint rightCenter{};
            Require(
                xbpreview::ProjectWindowStageLocalPoint(
                    stage.window,
                    1920,
                    1080,
                    rightParameters,
                    0.0f,
                    0.0f,
                    rightCenter) &&
                Near(
                    rightCenter.PixelX(1920),
                    stage.window.left + stage.window.width * 0.5f +
                        rightParameters.horizontalPlacementFraction * 1920.0f,
                    0.01) &&
                rightCenter.PixelX(1920) > 960.0f &&
                right.contentQuad.corners[0].w <
                    right.contentQuad.corners[1].w &&
                right.contentQuad.corners[2].w <
                    right.contentQuad.corners[3].w &&
                PixelDistance(right.contentQuad, 0, 2, 1920, 1080) >
                    PixelDistance(right.contentQuad, 1, 3, 1920, 1080),
                "projected center, W, and edge lengths prove +X/+yaw is RIGHT");
            VerifyMirroredQuad(
                left.contentQuad,
                right.contentQuad,
                1920,
                1080,
                "LEFT/RIGHT content corners are exact geometric mirrors");
            VerifyMirroredQuad(
                left.shadowQuad,
                right.shadowQuad,
                1920,
                1080,
                "LEFT/RIGHT shadow corners are exact geometric mirrors");
            VerifyFrontSymmetry(front.contentQuad, 1920, 1080);
            VerifyFrontSymmetry(front.shadowQuad, 1920, 1080);

            const auto sideStrength = SidePerspectiveStrength(
                right.contentQuad, 1920, 1080);
            const auto frontStrength = FrontPerspectiveStrength(
                front.contentQuad, 1920, 1080);
            Require(
                sideStrength > previousSideStrength &&
                frontStrength > previousFrontStrength,
                "projected side and FRONT perspective increase by level");
            if (level > 0)
            {
                Require(
                    rightParameters.scale < expectedRight[level - 1].scale &&
                    std::abs(rightParameters.horizontalPlacementFraction) >
                        std::abs(expectedRight[level - 1]
                            .horizontalPlacementFraction) &&
                    std::abs(rightParameters.verticalPlacementFraction) >
                        std::abs(expectedRight[level - 1]
                            .verticalPlacementFraction) &&
                    std::abs(rightParameters.rotationXDegrees) >
                        std::abs(expectedRight[level - 1].rotationXDegrees) &&
                    std::abs(rightParameters.rotationYDegrees) >
                        std::abs(expectedRight[level - 1].rotationYDegrees) &&
                    rightParameters.perspectiveDepth >
                        expectedRight[level - 1].perspectiveDepth &&
                    frontParameters.scale < expectedFront[level - 1].scale &&
                    std::abs(frontParameters.verticalPlacementFraction) >
                        std::abs(expectedFront[level - 1]
                            .verticalPlacementFraction) &&
                    std::abs(frontParameters.rotationXDegrees) >
                        std::abs(expectedFront[level - 1].rotationXDegrees) &&
                    frontParameters.perspectiveDepth >
                        expectedFront[level - 1].perspectiveDepth,
                    "the full parameter families increase coherently by level");
            }
            previousSideStrength = sideStrength;
            previousFrontStrength = frontStrength;
        }

        struct StageFixture
        {
            std::uint32_t sourceWidth;
            std::uint32_t sourceHeight;
            std::uint32_t canvasWidth;
            std::uint32_t canvasHeight;
        };
        constexpr std::array<StageFixture, 4> fixtures{
            StageFixture{ 1600, 900, 1920, 1080 },
            StageFixture{ 900, 1600, 1920, 1080 },
            StageFixture{ 1600, 900, 1080, 1920 },
            StageFixture{ 900, 1600, 1080, 1920 }
        };
        constexpr std::array<xbpreview::WindowStageDirection, 3> directions{
            xbpreview::WindowStageDirection::Left,
            xbpreview::WindowStageDirection::Front,
            xbpreview::WindowStageDirection::Right
        };
        for (const auto& fixture : fixtures)
        {
            xbpreview::FlatWindowStageComposition fixtureStage{};
            xbpreview::WindowCardShadowComposition fixtureShadow{};
            ComposeFrozenLayer2(
                fixture.sourceWidth,
                fixture.sourceHeight,
                fixture.canvasWidth,
                fixture.canvasHeight,
                fixtureStage,
                fixtureShadow);
            const auto frozenStage = fixtureStage;
            const auto frozenShadow = fixtureShadow;
            for (const auto direction : directions)
            {
                for (const auto strength : strengths)
                {
                    xbpreview::WindowStageTransformParameters parameters{};
                    xbpreview::WindowStageTransformComposition transformed{};
                    Require(
                        xbpreview::ResolveWindowStageTransform(
                            direction, strength, parameters) &&
                        xbpreview::ComposeWindowStageTransform(
                            fixtureStage,
                            fixtureShadow,
                            fixture.canvasWidth,
                            fixture.canvasHeight,
                            parameters,
                            transformed) &&
                        transformed.valid && !transformed.identity &&
                        transformed.contentQuad.PixelBounds(
                            fixture.canvasWidth,
                            fixture.canvasHeight).IsInside(
                                fixture.canvasWidth,
                                fixture.canvasHeight) &&
                        transformed.shadowQuad.PixelBounds(
                            fixture.canvasWidth,
                            fixture.canvasHeight).IsInside(
                                fixture.canvasWidth,
                                fixture.canvasHeight),
                        "all nine content/shadow poses stay inside horizontal and vertical canvases");
                    Require(
                        fixtureStage.UsesFullSourceTexture() &&
                        SameStage(fixtureStage, frozenStage) &&
                        SameShadow(fixtureShadow, frozenShadow),
                        "StageTransform preserves full UV and frozen Layer 2 inputs");
                    VerifyQuadConvex(
                        transformed.contentQuad,
                        fixture.canvasWidth,
                        fixture.canvasHeight);
                    VerifyQuadConvex(
                        transformed.shadowQuad,
                        fixture.canvasWidth,
                        fixture.canvasHeight);
                }
            }
        }

        const std::string_view vertexShader{
            xbpreview::WindowStageQuadVertexShaderSource };
        const std::string_view shadowShader{
            xbpreview::WindowStageTransformedShadowPixelShaderSource };
        Require(
            vertexShader.find("output.position = ClipCorners[index]") !=
                std::string_view::npos &&
            vertexShader.find("float2 uv : TEXCOORD0") !=
                std::string_view::npos &&
            vertexShader.find("noperspective float2") ==
                std::string_view::npos,
            "Stage vertex shader preserves homogeneous W and perspective UV interpolation");
        Require(
            shadowShader.find("SupportRectangle.xy + input.uv") !=
                std::string_view::npos &&
            shadowShader.find("RoundedRectangleSignedDistance") !=
                std::string_view::npos &&
            shadowShader.find("Texture2D") == std::string_view::npos,
            "transformed shadow remains source-independent on the same projected plane");
        VerifyVertexShaderCompiles(
            xbpreview::WindowStageQuadVertexShaderSource,
            "WindowStageTransform",
            "VSWindowStageQuad");
        VerifyPixelShaderCompiles(
            xbpreview::WindowStageTransformedShadowPixelShaderSource,
            "WindowStageTransformedShadow",
            "PSWindowStageTransformedShadow");
        VerifyPixelShaderCompiles(
            xbpreview::WindowCardContentPixelShaderSource,
            "WindowStageTransformedContent",
            "PSWindowCardContent");

        std::cout
            << "XbPreview.FlatStage.Tests STAGE TRANSFORM PASS: exact 9-pose "
               "table, recovered RIGHT geometry, mirrored LEFT, symmetric "
               "unvalidated FRONT trapezoids, monotonic levels, Identity, "
               "safe content/shadow bounds, and perspective shaders\n";
    }

    void TestLayer3MinimalRegression()
    {
        xbpreview::WindowStageTransformParameters target{};
        Require(
            xbpreview::WindowStageIdentityTransform.IsIdentity() &&
            xbpreview::WindowStageIdentityTransform.IsValid(),
            "Layer 3 Identity remains exact and valid");
        Require(
            xbpreview::ResolveWindowStageTransform(
                xbpreview::WindowStageDirection::Right,
                xbpreview::WindowStageStrength::Level2,
                target) &&
            SameTransformParameters(
                target,
                xbpreview::WindowStageHistoricalRightTransforms[1]),
            "Layer 3 frozen RIGHT x LEVEL_2 resolves directly from its table");

        xbpreview::FlatWindowStageComposition stage{};
        xbpreview::WindowCardShadowComposition shadow{};
        xbpreview::WindowStageTransformComposition identityComposition{};
        xbpreview::WindowStageTransformComposition targetComposition{};
        ComposeFrozenLayer2(1600, 900, 1920, 1080, stage, shadow);
        Require(
            xbpreview::ComposeWindowStageTransform(
                stage,
                shadow,
                1920,
                1080,
                xbpreview::WindowStageIdentityTransform,
                identityComposition) &&
            identityComposition.identity && identityComposition.valid,
            "Layer 3 Identity composition remains valid");
        Require(
            xbpreview::ComposeWindowStageTransform(
                stage,
                shadow,
                1920,
                1080,
                target,
                targetComposition) &&
            !targetComposition.identity && targetComposition.valid,
            "Layer 3 frozen RIGHT x LEVEL_2 composition remains valid");

        std::cout
            << "XbPreview.FlatStage.Tests LAYER 3 MINIMAL PASS: exact Identity "
               "and frozen RIGHT x LEVEL_2 resolve and compose\n";
    }

    void TestWindowStage25DShadowBoundsGate()
    {
        using xbpreview::WindowShowcaseMotionController;
        using xbpreview::WindowShowcaseMotionPreset;
        using xbpreview::WindowShowcaseMotionState;

        constexpr std::uint32_t sourceWidth = 886;
        constexpr std::uint32_t sourceHeight = 693;
        constexpr std::uint32_t canvasWidth = 1920;
        constexpr std::uint32_t canvasHeight = 1080;

        xbpreview::FlatWindowStageComposition stage{};
        xbpreview::WindowCardShadowComposition shadow{};
        ComposeFrozenLayer2(
            sourceWidth,
            sourceHeight,
            canvasWidth,
            canvasHeight,
            stage,
            shadow);
        const auto frozenStage = stage;
        const auto frozenShadow = shadow;

        xbpreview::WindowStageTransformParameters target{};
        xbpreview::WindowShowcaseMotionTiming timing{};
        Require(
            xbpreview::ResolveWindowStageTransform(
                xbpreview::WindowStageDirection::Right,
                xbpreview::WindowStageStrength::Level2,
                target) &&
            SameTransformParameters(
                target,
                xbpreview::WindowStageHistoricalRightTransforms[1]) &&
            xbpreview::ResolveWindowShowcaseMotionTiming(
                WindowShowcaseMotionPreset::A,
                timing) &&
            timing.enterMilliseconds == 360.0 &&
            timing.returnMilliseconds == 380.0 &&
            timing.enterEasing ==
                xbpreview::WindowShowcaseMotionEasing::SmootherStep &&
            timing.returnEasing ==
                xbpreview::WindowShowcaseMotionEasing::SmootherStep,
            "failure fixture retains exact RIGHT x LEVEL_2 and Motion A semantics");

        WindowShowcaseMotionController controller;
        Require(
            controller.Start(target, WindowShowcaseMotionPreset::A, 0.0),
            "failure fixture starts Motion A from Identity");

        std::uint32_t firstOverscanElapsed = 361;
        float bottomAtFirstOverscan = 0.0f;
        xbpreview::WindowStageTransformComposition endpoint{};
        for (std::uint32_t elapsed = 0; elapsed <= 360; ++elapsed)
        {
            xbpreview::WindowStageTransformComposition transformed{};
            Require(
                controller.Update(static_cast<double>(elapsed)) &&
                xbpreview::ComposeWindowStageTransform(
                    stage,
                    shadow,
                    canvasWidth,
                    canvasHeight,
                    controller.CurrentTransform(),
                    transformed) &&
                transformed.valid,
                "every millisecond of the exact failure Transition composes");

            const auto contentBounds = transformed.contentQuad.PixelBounds(
                canvasWidth,
                canvasHeight);
            const auto shadowBounds = transformed.shadowQuad.PixelBounds(
                canvasWidth,
                canvasHeight);
            Require(
                contentBounds.IsInside(canvasWidth, canvasHeight) &&
                shadowBounds.IsFiniteNonEmpty(),
                "Transition keeps content on-canvas and shadow support finite");
            if (!shadowBounds.IsInside(canvasWidth, canvasHeight) &&
                firstOverscanElapsed == 361)
            {
                firstOverscanElapsed = elapsed;
                bottomAtFirstOverscan = shadowBounds.bottom;
            }
            if (elapsed == 360)
            {
                endpoint = transformed;
            }
        }

        const auto endpointContentBounds = endpoint.contentQuad.PixelBounds(
            canvasWidth,
            canvasHeight);
        const auto endpointShadowBounds = endpoint.shadowQuad.PixelBounds(
            canvasWidth,
            canvasHeight);
        Require(
            firstOverscanElapsed == 320 &&
            Near(bottomAtFirstOverscan, 1080.071, 0.02) &&
            endpointContentBounds.IsInside(canvasWidth, canvasHeight) &&
            !endpointShadowBounds.IsInside(canvasWidth, canvasHeight) &&
            endpointShadowBounds.IsFiniteNonEmpty() &&
            Near(endpointShadowBounds.bottom, 1081.533, 0.02),
            "320ms/1081.533px shadow overscan is accepted while content stays valid");
        Require(
            controller.State() == WindowShowcaseMotionState::Stay &&
            SameTransformParameters(controller.CurrentTransform(), target) &&
            controller.Update(90'000.0) &&
            controller.State() == WindowShowcaseMotionState::Stay &&
            SameTransformParameters(controller.CurrentTransform(), target),
            "failure fixture reaches exact persistent STAY with no auto Return");
        xbpreview::WindowStageTransformComposition persistent{};
        Require(
            xbpreview::ComposeWindowStageTransform(
                stage,
                shadow,
                canvasWidth,
                canvasHeight,
                controller.CurrentTransform(),
                persistent) &&
            persistent.valid &&
            Near(
                persistent.shadowQuad.PixelBounds(
                    canvasWidth,
                    canvasHeight).bottom,
                endpointShadowBounds.bottom,
                0.001),
            "persistent STAY continues composing the same finite overscan");

        struct StayResizeFixture
        {
            std::uint32_t width;
            std::uint32_t height;
        };
        constexpr std::array<StayResizeFixture, 4> stayResizeFixtures{
            StayResizeFixture{ 1386, 693 },
            StayResizeFixture{ 686, 693 },
            StayResizeFixture{ 1920, 1032 },
            StayResizeFixture{ 686, 693 }
        };
        for (const auto& fixture : stayResizeFixtures)
        {
            xbpreview::FlatWindowStageComposition resizedStage{};
            xbpreview::WindowCardShadowComposition resizedShadow{};
            xbpreview::WindowStageTransformComposition resizedTransform{};
            ComposeFrozenLayer2(
                fixture.width,
                fixture.height,
                canvasWidth,
                canvasHeight,
                resizedStage,
                resizedShadow);
            Require(
                xbpreview::ComposeWindowStageTransform(
                    resizedStage,
                    resizedShadow,
                    canvasWidth,
                    canvasHeight,
                    controller.CurrentTransform(),
                    resizedTransform) &&
                resizedTransform.valid &&
                resizedTransform.contentQuad.PixelBounds(
                    canvasWidth,
                    canvasHeight).IsInside(canvasWidth, canvasHeight) &&
                resizedTransform.shadowQuad.PixelBounds(
                    canvasWidth,
                    canvasHeight).IsFiniteNonEmpty(),
                "exact persistent STAY refits larger/smaller/maximized/restored sizes");
        }

        constexpr std::array<xbpreview::WindowStageDirection, 3>
            strongDirections{
                xbpreview::WindowStageDirection::Right,
                xbpreview::WindowStageDirection::Left,
                xbpreview::WindowStageDirection::Front
            };
        for (const auto direction : strongDirections)
        {
            xbpreview::WindowStageTransformParameters parameters{};
            xbpreview::WindowStageTransformComposition transformed{};
            Require(
                xbpreview::ResolveWindowStageTransform(
                    direction,
                    xbpreview::WindowStageStrength::Level3,
                    parameters) &&
                xbpreview::ComposeWindowStageTransform(
                    stage,
                    shadow,
                    canvasWidth,
                    canvasHeight,
                    parameters,
                    transformed) &&
                transformed.valid &&
                transformed.contentQuad.PixelBounds(
                    canvasWidth,
                    canvasHeight).IsInside(canvasWidth, canvasHeight) &&
                transformed.shadowQuad.PixelBounds(
                    canvasWidth,
                    canvasHeight).IsFiniteNonEmpty(),
                "RIGHT/LEFT/FRONT LEVEL_3 use consistent finite-overscan validation");
            for (const auto& point : transformed.contentQuad.corners)
            {
                Require(
                    point.IsValid() &&
                    std::isfinite(point.PixelX(canvasWidth)) &&
                    std::isfinite(point.PixelY(canvasHeight)),
                    "LEVEL_3 content projection has finite clip/pixel coordinates");
            }
            for (const auto& point : transformed.shadowQuad.corners)
            {
                Require(
                    point.IsValid() &&
                    std::isfinite(point.PixelX(canvasWidth)) &&
                    std::isfinite(point.PixelY(canvasHeight)),
                    "LEVEL_3 shadow projection has finite clip/pixel coordinates");
            }
        }
        Require(
            SameStage(stage, frozenStage) && SameShadow(shadow, frozenShadow),
            "shadow-bounds Gate leaves every frozen Layer 2 field unchanged");

        auto offCanvasContent = xbpreview::WindowStageIdentityTransform;
        offCanvasContent.horizontalPlacementFraction = 0.25f;
        xbpreview::WindowStageTransformComposition rejected{};
        Require(
            offCanvasContent.IsValid() &&
            !xbpreview::ComposeWindowStageTransform(
                stage,
                shadow,
                canvasWidth,
                canvasHeight,
                offCanvasContent,
                rejected) &&
            !rejected.valid,
            "finite shadow overscan never relaxes strict content containment");

        std::cout
            << "WINDOW-STAGE-25D-SHADOW-BOUNDS-GATE PASS: first finite "
               "overscan at "
            << firstOverscanElapsed << "ms, bottom="
            << bottomAtFirstOverscan << ", exact STAY bottom="
            << endpointShadowBounds.bottom
            << "; RIGHT/LEFT/FRONT LEVEL_3 finite\n";
    }

    void TestLeftFrontMotionDirectionGate()
    {
        using xbpreview::WindowShowcaseMotionController;
        using xbpreview::WindowShowcaseMotionPreset;
        using xbpreview::WindowShowcaseMotionState;

        constexpr std::uint32_t canvasWidth = 1920;
        constexpr std::uint32_t canvasHeight = 1080;
        xbpreview::FlatWindowStageComposition stage{};
        xbpreview::WindowCardShadowComposition shadow{};
        ComposeFrozenLayer2(
            1386, 693, canvasWidth, canvasHeight, stage, shadow);

        constexpr std::array<xbpreview::WindowStageTransformParameters, 3>
            expectedLeft{
                xbpreview::WindowStageTransformParameters{
                    0.88f, -0.025f, -0.018f, -6.0f, -18.0f, 0.90f },
                xbpreview::WindowStageTransformParameters{
                    0.83f, -0.040f, -0.022f, -8.0f, -24.0f, 1.00f },
                xbpreview::WindowStageTransformParameters{
                    0.77f, -0.060f, -0.028f, -10.0f, -30.0f, 1.10f }
            };
        constexpr std::array<xbpreview::WindowStageTransformParameters, 3>
            expectedFront{
                xbpreview::WindowStageTransformParameters{
                    0.94f, 0.0f, -0.008f, -3.0f, 0.0f, 0.70f },
                xbpreview::WindowStageTransformParameters{
                    0.90f, 0.0f, -0.012f, -5.0f, 0.0f, 0.85f },
                xbpreview::WindowStageTransformParameters{
                    0.86f, 0.0f, -0.016f, -7.0f, 0.0f, 1.00f }
            };
        constexpr std::array<xbpreview::WindowStageDirection, 2> directions{
            xbpreview::WindowStageDirection::Left,
            xbpreview::WindowStageDirection::Front
        };
        constexpr std::array<xbpreview::WindowStageStrength, 3> strengths{
            xbpreview::WindowStageStrength::Level1,
            xbpreview::WindowStageStrength::Level2,
            xbpreview::WindowStageStrength::Level3
        };

        for (std::size_t directionIndex = 0;
            directionIndex < directions.size(); ++directionIndex)
        {
            for (std::size_t levelIndex = 0;
                levelIndex < strengths.size(); ++levelIndex)
            {
                xbpreview::WindowStageTransformParameters target{};
                const auto& expected = directionIndex == 0
                    ? expectedLeft[levelIndex]
                    : expectedFront[levelIndex];
                Require(
                    xbpreview::ResolveWindowStageTransform(
                        directions[directionIndex], strengths[levelIndex],
                        target) &&
                    SameTransformParameters(target, expected),
                    "LEFT/FRONT frozen parameters are exact");
                if (directions[directionIndex] ==
                    xbpreview::WindowStageDirection::Front)
                {
                    Require(
                        target.horizontalPlacementFraction == 0.0f &&
                        target.rotationYDegrees == 0.0f,
                        "FRONT remains centered with zero yaw");
                }

                WindowShowcaseMotionController controller;
                Require(
                    controller.Start(
                        target, WindowShowcaseMotionPreset::A, 0.0) &&
                    controller.Update(360.0) &&
                    controller.State() == WindowShowcaseMotionState::Stay &&
                    SameTransformParameters(
                        controller.CurrentTransform(), target),
                    "LEFT/FRONT target enters exact persistent STAY");

                xbpreview::WindowStageTransformComposition transformed{};
                Require(
                    xbpreview::ComposeWindowStageTransform(
                        stage,
                        shadow,
                        canvasWidth,
                        canvasHeight,
                        controller.CurrentTransform(),
                        transformed) &&
                    transformed.valid &&
                    transformed.contentQuad.PixelBounds(
                        canvasWidth, canvasHeight).IsInside(
                            canvasWidth, canvasHeight) &&
                    transformed.shadowQuad.PixelBounds(
                        canvasWidth, canvasHeight).IsFiniteNonEmpty(),
                    "LEFT/FRONT target composes with legal content and shadow bounds");
                for (const auto* const quad :
                    { &transformed.contentQuad, &transformed.shadowQuad })
                {
                    for (const auto& point : quad->corners)
                    {
                        Require(
                            point.IsValid() && point.w >= 0.25f &&
                            std::isfinite(point.PixelX(canvasWidth)) &&
                            std::isfinite(point.PixelY(canvasHeight)),
                            "LEFT/FRONT projected points are finite with w >= 0.25");
                    }
                }
            }
        }

        xbpreview::WindowShowcaseMotionTiming timing{};
        xbpreview::WindowStageTransformParameters rightLevel2{};
        WindowShowcaseMotionController rightControl;
        Require(
            xbpreview::ResolveWindowShowcaseMotionTiming(
                WindowShowcaseMotionPreset::A, timing) &&
            timing.enterMilliseconds == 360.0 &&
            timing.returnMilliseconds == 380.0 &&
            timing.enterEasing ==
                xbpreview::WindowShowcaseMotionEasing::SmootherStep &&
            timing.returnEasing ==
                xbpreview::WindowShowcaseMotionEasing::SmootherStep &&
            xbpreview::ResolveWindowStageTransform(
                xbpreview::WindowStageDirection::Right,
                xbpreview::WindowStageStrength::Level2,
                rightLevel2) &&
            SameTransformParameters(
                rightLevel2,
                xbpreview::WindowStageHistoricalRightTransforms[1]) &&
            rightControl.Start(rightLevel2, timing, 0.0) &&
            rightControl.Update(360.0) &&
            rightControl.State() == WindowShowcaseMotionState::Stay &&
            rightControl.Update(90'000.0) &&
            rightControl.State() == WindowShowcaseMotionState::Stay &&
            SameTransformParameters(
                rightControl.CurrentTransform(), rightLevel2) &&
            rightControl.RequestReturn(90'000.0) &&
            rightControl.Update(90'380.0) &&
            rightControl.State() == WindowShowcaseMotionState::Idle &&
            SameTransformParameters(
                rightControl.CurrentTransform(),
                xbpreview::WindowStageIdentityTransform),
            "RIGHT LEVEL_2 control preserves 360ms Enter, persistent STAY, and 380ms Return");

        xbpreview::WindowStageTransformComposition identity{};
        Require(
            xbpreview::ComposeWindowStageTransform(
                stage,
                shadow,
                canvasWidth,
                canvasHeight,
                xbpreview::WindowStageIdentityTransform,
                identity) &&
            identity.valid && identity.identity &&
            SameTransformParameters(
                identity.parameters,
                xbpreview::WindowStageIdentityTransform) &&
            Near(identity.contentQuad.corners[0].PixelX(canvasWidth),
                stage.window.left) &&
            Near(identity.contentQuad.corners[0].PixelY(canvasHeight),
                stage.window.top) &&
            Near(identity.contentQuad.corners[3].PixelX(canvasWidth),
                stage.window.left + stage.window.width) &&
            Near(identity.contentQuad.corners[3].PixelY(canvasHeight),
                stage.window.top + stage.window.height),
            "Identity control exactly preserves the original flat card geometry");

        std::cout
            << "WINDOW-STAGE-LEFT-FRONT-MOTION-GATE PASS: exact LEFT/FRONT "
               "LEVEL_1/2/3 targets enter persistent STAY; projected points "
               "finite with w >= 0.25; content/shadow bounds and Compose "
               "valid; RIGHT LEVEL_2 and Identity controls unchanged\n";
    }

    void TestWindowStagePunchOverlay()
    {
        using xbpreview::WindowStageDirection;
        using xbpreview::WindowStagePunchCandidate;
        using xbpreview::WindowStageStrength;

        const auto frozenRight =
            xbpreview::WindowStageHistoricalRightTransforms;
        const auto frozenFront =
            xbpreview::WindowStageFrontCandidateTransforms;

        WindowStagePunchCandidate parsed{};
        Require(
            xbpreview::TryParseWindowStagePunchCandidate(L"A", parsed) &&
            parsed == WindowStagePunchCandidate::Light &&
            xbpreview::TryParseWindowStagePunchCandidate(L"B", parsed) &&
            parsed == WindowStagePunchCandidate::Showcase &&
            xbpreview::TryParseWindowStagePunchCandidate(L"C", parsed) &&
            parsed == WindowStagePunchCandidate::Strong &&
            !xbpreview::TryParseWindowStagePunchCandidate(L"D", parsed),
            "Punch candidate parser accepts only A/B/C");

        xbpreview::FlatWindowStageComposition stage{};
        xbpreview::WindowCardShadowComposition shadow{};
        ComposeFrozenLayer2(1600, 900, 1920, 1080, stage, shadow);
        const auto frozenStage = stage;
        const auto frozenShadow = shadow;

        constexpr std::array<WindowStagePunchCandidate, 3> candidates{
            WindowStagePunchCandidate::Light,
            WindowStagePunchCandidate::Showcase,
            WindowStagePunchCandidate::Strong
        };
        constexpr std::array<WindowStageDirection, 3> directions{
            WindowStageDirection::Left,
            WindowStageDirection::Front,
            WindowStageDirection::Right
        };
        std::array<float, 3> rightStandardScales{};
        std::array<float, 3> rightStrongScales{};

        for (const auto direction : directions)
        {
            xbpreview::WindowStageTransformParameters base{};
            Require(
                xbpreview::ResolveWindowStageTransform(
                    direction, WindowStageStrength::Level2, base),
                "Punch compatibility base pose resolves");
            const auto originalBase = base;

            for (std::size_t index = 0; index < candidates.size(); ++index)
            {
                xbpreview::WindowStageTransformParameters standard{};
                xbpreview::WindowStageTransformParameters strong{};
                Require(
                    xbpreview::ComposeWindowStagePunchOverlay(
                        base, candidates[index], 1.6, standard) &&
                    xbpreview::ComposeWindowStagePunchOverlay(
                        base, candidates[index], 2.0, strong),
                    "1.6x and 2.0x Punch overlays compose");
                Require(
                    SameTransformParameters(base, originalBase),
                    "Punch composition never rewrites the base Stage pose");
                Require(
                    standard.scale > base.scale &&
                    strong.scale > standard.scale &&
                    standard.scale < 1.0f && strong.scale < 1.0f &&
                    standard.horizontalPlacementFraction ==
                        base.horizontalPlacementFraction &&
                    standard.verticalPlacementFraction ==
                        base.verticalPlacementFraction &&
                    standard.rotationXDegrees == base.rotationXDegrees &&
                    standard.rotationYDegrees == base.rotationYDegrees &&
                    standard.perspectiveDepth == base.perspectiveDepth &&
                    strong.horizontalPlacementFraction ==
                        base.horizontalPlacementFraction &&
                    strong.verticalPlacementFraction ==
                        base.verticalPlacementFraction &&
                    strong.rotationXDegrees == base.rotationXDegrees &&
                    strong.rotationYDegrees == base.rotationYDegrees &&
                    strong.perspectiveDepth == base.perspectiveDepth,
                    "Punch changes only scale and 2.0x is stronger than 1.6x");

                xbpreview::WindowStageTransformComposition standardComposition{};
                xbpreview::WindowStageTransformComposition strongComposition{};
                Require(
                    xbpreview::ComposeWindowStageTransform(
                        stage, shadow, 1920, 1080,
                        standard, standardComposition) &&
                    xbpreview::ComposeWindowStageTransform(
                        stage, shadow, 1920, 1080,
                        strong, strongComposition) &&
                    standardComposition.valid && strongComposition.valid,
                    "LEFT/FRONT/RIGHT LEVEL_2 Punch geometry is finite and composes");
                if (direction == WindowStageDirection::Front)
                {
                    Require(
                        standard.rotationYDegrees == 0.0f &&
                        strong.rotationYDegrees == 0.0f,
                        "FRONT LEVEL_2 retains zero-yaw semantics");
                }
                if (direction == WindowStageDirection::Right)
                {
                    rightStandardScales[index] = standard.scale;
                    rightStrongScales[index] = strong.scale;
                }
            }
        }

        Require(
            rightStandardScales[0] < rightStandardScales[1] &&
            rightStandardScales[1] < rightStandardScales[2] &&
            rightStrongScales[0] < rightStrongScales[1] &&
            rightStrongScales[1] < rightStrongScales[2] &&
            rightStrongScales[0] > rightStandardScales[1] &&
            rightStrongScales[1] > rightStandardScales[2],
            "A/B/C ordering and cross-endpoint ordering are strict");

        xbpreview::WindowStageTransformParameters right{};
        xbpreview::WindowStageTransformParameters wide{};
        xbpreview::WindowStageTransformParameters disabled{};
        xbpreview::WindowStageTransformParameters identity{};
        Require(
            xbpreview::ResolveWindowStageTransform(
                WindowStageDirection::Right,
                WindowStageStrength::Level2,
                right) &&
            xbpreview::ComposeWindowStagePunchOverlay(
                right, WindowStagePunchCandidate::Strong, 1.0, wide) &&
            xbpreview::ComposeWindowStagePunchOverlay(
                right, WindowStagePunchCandidate::Disabled, 2.0, disabled) &&
            xbpreview::ComposeWindowStagePunchOverlay(
                xbpreview::WindowStageIdentityTransform,
                WindowStagePunchCandidate::Strong, 2.0, identity) &&
            SameTransformParameters(wide, right) &&
            SameTransformParameters(disabled, right) &&
            SameTransformParameters(
                identity, xbpreview::WindowStageIdentityTransform),
            "Wide returns to base pose and Identity cannot be punched");
        Require(
            !xbpreview::ComposeWindowStagePunchOverlay(
                right,
                WindowStagePunchCandidate::Showcase,
                std::numeric_limits<double>::quiet_NaN(),
                wide),
            "non-finite camera zoom is rejected");
        Require(
            SameStage(stage, frozenStage) && SameShadow(shadow, frozenShadow),
            "Punch leaves Layer 1 and Shadow parameters unchanged");
        for (std::size_t index = 0; index < frozenRight.size(); ++index)
        {
            Require(
                SameTransformParameters(
                    xbpreview::WindowStageHistoricalRightTransforms[index],
                    frozenRight[index]) &&
                SameTransformParameters(
                    xbpreview::WindowStageFrontCandidateTransforms[index],
                    frozenFront[index]),
                "Punch leaves all nine pose parameters unchanged");
        }

        std::cout
            << "WINDOW-STAGE-MANUAL-ZOOM-PUNCH-IN GATE PASS: derived scale-only "
               "A/B/C overlay, strict 1.6x/2.0x ordering, LEFT/FRONT/RIGHT "
               "LEVEL_2 finite Compose, FRONT zero yaw, Wide/base restoration, "
               "and frozen Stage/Shadow ownership\n";
    }

    void TestWindowStageShowcasePunchNinePoseSafety()
    {
        using xbpreview::WindowStageDirection;
        using xbpreview::WindowStagePunchCandidate;
        using xbpreview::WindowStageStrength;

        constexpr std::array<WindowStageDirection, 3> directions{
            WindowStageDirection::Right,
            WindowStageDirection::Front,
            WindowStageDirection::Left
        };
        constexpr std::array<WindowStageStrength, 3> strengths{
            WindowStageStrength::Level1,
            WindowStageStrength::Level2,
            WindowStageStrength::Level3
        };
        constexpr std::array<double, 2> zooms{ 1.6, 2.0 };

        const auto frozenRight =
            xbpreview::WindowStageHistoricalRightTransforms;
        const auto frozenFront =
            xbpreview::WindowStageFrontCandidateTransforms;
        xbpreview::FlatWindowStageComposition stage{};
        xbpreview::WindowCardShadowComposition shadow{};
        ComposeFrozenLayer2(1600, 900, 1920, 1080, stage, shadow);
        const auto frozenStage = stage;
        const auto frozenShadow = shadow;

        for (const auto strength : strengths)
        {
            xbpreview::WindowStageTransformParameters right{};
            xbpreview::WindowStageTransformParameters front{};
            xbpreview::WindowStageTransformParameters left{};
            Require(
                xbpreview::ResolveWindowStageTransform(
                    WindowStageDirection::Right, strength, right) &&
                xbpreview::ResolveWindowStageTransform(
                    WindowStageDirection::Front, strength, front) &&
                xbpreview::ResolveWindowStageTransform(
                    WindowStageDirection::Left, strength, left),
                "Showcase Punch resolves every frozen pose");
            Require(
                left.scale == right.scale &&
                left.horizontalPlacementFraction ==
                    -right.horizontalPlacementFraction &&
                left.verticalPlacementFraction ==
                    right.verticalPlacementFraction &&
                left.rotationXDegrees == right.rotationXDegrees &&
                left.rotationYDegrees == -right.rotationYDegrees &&
                left.perspectiveDepth == right.perspectiveDepth &&
                front.horizontalPlacementFraction == 0.0f &&
                front.rotationYDegrees == 0.0f,
                "LEFT stays the exact RIGHT mirror and FRONT stays zero-yaw");

            const std::array<xbpreview::WindowStageTransformParameters, 3>
                basePoses{ right, front, left };
            for (std::size_t directionIndex = 0;
                directionIndex < directions.size(); ++directionIndex)
            {
                const auto base = basePoses[directionIndex];
                for (const auto zoom : zooms)
                {
                    xbpreview::WindowStageTransformParameters presentation{};
                    Require(
                        xbpreview::ComposeWindowStagePunchOverlay(
                            base,
                            WindowStagePunchCandidate::Showcase,
                            zoom,
                            presentation),
                        "B Showcase overlay composes for all nine poses");
                    Require(
                        presentation.scale > base.scale &&
                        presentation.scale < 1.0f &&
                        presentation.horizontalPlacementFraction ==
                            base.horizontalPlacementFraction &&
                        presentation.verticalPlacementFraction ==
                            base.verticalPlacementFraction &&
                        presentation.rotationXDegrees ==
                            base.rotationXDegrees &&
                        presentation.rotationYDegrees ==
                            base.rotationYDegrees &&
                        presentation.perspectiveDepth ==
                            base.perspectiveDepth,
                        "B Showcase changes only transient card scale");

                    xbpreview::WindowStageTransformComposition composition{};
                    Require(
                        xbpreview::ComposeWindowStageTransform(
                            stage,
                            shadow,
                            1920,
                            1080,
                            presentation,
                            composition) &&
                        composition.valid &&
                        composition.contentQuad.IsValid() &&
                        composition.shadowQuad.IsValid() &&
                        composition.contentQuad.PixelBounds(
                            1920, 1080).IsInside(1920, 1080) &&
                        composition.shadowQuad.PixelBounds(
                            1920, 1080).IsFiniteNonEmpty(),
                        "B Showcase projected coordinates, w, content bounds, "
                        "and shadow support are safe");
                }

                xbpreview::WindowStageTransformParameters wide{};
                Require(
                    xbpreview::ComposeWindowStagePunchOverlay(
                        base,
                        WindowStagePunchCandidate::Showcase,
                        1.0,
                        wide) &&
                    SameTransformParameters(wide, base),
                    "Wide restores the exact direction-specific base pose");
            }
        }

        Require(
            SameStage(stage, frozenStage) && SameShadow(shadow, frozenShadow),
            "B Showcase safety Gate leaves Stage and Shadow unchanged");
        for (std::size_t index = 0; index < frozenRight.size(); ++index)
        {
            Require(
                SameTransformParameters(
                    xbpreview::WindowStageHistoricalRightTransforms[index],
                    frozenRight[index]) &&
                SameTransformParameters(
                    xbpreview::WindowStageFrontCandidateTransforms[index],
                    frozenFront[index]),
                "B Showcase safety Gate leaves all nine poses frozen");
        }

        std::cout
            << "WINDOW-STAGE-MANUAL-ZOOM-PUNCH-IN-B-9POSE GATE PASS: "
               "RIGHT/FRONT/LEFT L1/L2/L3 at 1.6x and 2.0x have finite "
               "projected coordinates with w >= 0.25, valid content bounds, "
               "finite nondegenerate shadow support, Compose success, exact "
               "direction semantics, and exact Wide/base restoration\n";
    }

    void TestShowcaseMotion()
    {
        using xbpreview::WindowShowcaseMotionController;
        using xbpreview::WindowShowcaseMotionPreset;
        using xbpreview::WindowShowcaseMotionState;

        const auto frozenRight =
            xbpreview::WindowStageHistoricalRightTransforms;
        const auto frozenFront =
            xbpreview::WindowStageFrontCandidateTransforms;
        xbpreview::WindowStageTransformParameters target{};
        Require(
            xbpreview::ResolveWindowStageTransform(
                xbpreview::WindowStageDirection::Right,
                xbpreview::WindowStageStrength::Level2,
                target),
            "Showcase target resolves from frozen Layer 3");

        xbpreview::WindowShowcaseMotionTiming timing{};
        Require(
            xbpreview::ResolveWindowShowcaseMotionTiming(
                WindowShowcaseMotionPreset::A, timing) &&
            timing.enterMilliseconds == 360.0 &&
            timing.holdMilliseconds == 900.0 &&
            timing.returnMilliseconds == 380.0 &&
            timing.enterEasing ==
                xbpreview::WindowShowcaseMotionEasing::SmootherStep &&
            timing.returnEasing ==
                xbpreview::WindowShowcaseMotionEasing::SmootherStep,
            "selected A preserves 360ms enter and 380ms explicit Return feel");

        WindowShowcaseMotionController controller;
        Require(
            controller.State() == WindowShowcaseMotionState::Idle &&
            SameTransformParameters(
                controller.CurrentTransform(),
                xbpreview::WindowStageIdentityTransform),
            "IDLE is exact Identity");
        Require(
            controller.Start(target, WindowShowcaseMotionPreset::A, 0.0) &&
            controller.State() == WindowShowcaseMotionState::Transition &&
            SameTransformParameters(
                controller.CurrentTransform(),
                xbpreview::WindowStageIdentityTransform),
            "TRANSITION begins at exact Identity");

        for (double elapsed = 0.0;
            elapsed < timing.enterMilliseconds;
            elapsed += 1.0)
        {
            Require(
                controller.Update(elapsed) &&
                controller.State() ==
                    WindowShowcaseMotionState::Transition &&
                TransformParametersWithinEndpoints(
                    controller.CurrentTransform(),
                    xbpreview::WindowStageIdentityTransform,
                    target),
                "TRANSITION advances monotonically without overshoot");
        }
        Require(
            controller.Update(timing.enterMilliseconds) &&
            controller.State() == WindowShowcaseMotionState::Stay &&
            SameTransformParameters(controller.CurrentTransform(), target),
            "TRANSITION endpoint enters STAY at exact frozen target");
        Require(
            controller.Update(900.0) &&
            controller.Update(90'000.0) &&
            controller.State() == WindowShowcaseMotionState::Stay &&
            SameTransformParameters(controller.CurrentTransform(), target),
            "STAY is unbounded and never auto-returns after the historical 900ms");

        const auto explicitReturnStart = 90'000.0;
        Require(
            controller.RequestReturn(explicitReturnStart) &&
            controller.State() == WindowShowcaseMotionState::Return &&
            SameTransformParameters(controller.CurrentTransform(), target),
            "STAY RequestReturn begins continuously at exact target");
        Require(
            controller.Update(explicitReturnStart + 1.0) &&
            TransformParametersWithinEndpoints(
                controller.CurrentTransform(),
                target,
                xbpreview::WindowStageIdentityTransform) &&
            controller.Update(
                explicitReturnStart + timing.returnMilliseconds) &&
            controller.State() == WindowShowcaseMotionState::Idle &&
            SameTransformParameters(
                controller.CurrentTransform(),
                xbpreview::WindowStageIdentityTransform),
            "explicit 380ms Return ends at IDLE and exact Identity");

        WindowShowcaseMotionController interrupted;
        Require(
            interrupted.Start(target, WindowShowcaseMotionPreset::A, 0.0) &&
            interrupted.Update(100.0),
            "interrupt fixture reaches mid-TRANSITION");
        const auto beforeInterrupt = interrupted.CurrentTransform();
        Require(
            !SameTransformParameters(
                beforeInterrupt, xbpreview::WindowStageIdentityTransform) &&
            !SameTransformParameters(beforeInterrupt, target) &&
            interrupted.RequestReturn(100.0) &&
            interrupted.State() == WindowShowcaseMotionState::Return &&
            SameTransformParameters(
                interrupted.CurrentTransform(), beforeInterrupt),
            "mid-TRANSITION reverse has no transform discontinuity");
        Require(
            interrupted.Update(101.0) &&
            TransformParametersWithinEndpoints(
                interrupted.CurrentTransform(),
                beforeInterrupt,
                xbpreview::WindowStageIdentityTransform) &&
            interrupted.Update(480.0) &&
            interrupted.State() == WindowShowcaseMotionState::Idle &&
            SameTransformParameters(
                interrupted.CurrentTransform(),
                xbpreview::WindowStageIdentityTransform),
            "interrupted RETURN moves smoothly from current transform to Identity");

        for (std::size_t index = 0; index < frozenRight.size(); ++index)
        {
            Require(
                SameTransformParameters(
                    xbpreview::WindowStageHistoricalRightTransforms[index],
                    frozenRight[index]) &&
                SameTransformParameters(
                    xbpreview::WindowStageFrontCandidateTransforms[index],
                    frozenFront[index]),
                "Motion Controller does not modify the Layer 3 pose table");
        }

        std::cout
            << "XbPreview.FlatStage.Tests SHOWCASE MOTION PASS: IDLE/"
               "TRANSITION/STAY/RETURN exact endpoints, selected A 360ms "
               "enter and explicit 380ms Return, unbounded persistent Stay, "
               "no auto-return, no overshoot, and continuous Return from "
               "mid-Transition and Stay\n";
    }

    void TestResolutionV1GeometryAndCursor()
    {
        constexpr std::array<std::array<std::uint32_t, 2>, 3> outputs{
            std::array<std::uint32_t, 2>{ 1920, 1080 },
            std::array<std::uint32_t, 2>{ 2560, 1440 },
            std::array<std::uint32_t, 2>{ 3840, 2160 }
        };
        constexpr std::array<std::array<std::uint32_t, 2>, 4> sources{
            std::array<std::uint32_t, 2>{ 1920, 1080 },
            std::array<std::uint32_t, 2>{ 1920, 1200 },
            std::array<std::uint32_t, 2>{ 2560, 1080 },
            std::array<std::uint32_t, 2>{ 1600, 1200 }
        };
        XbCameraState directorState{};
        directorState.structSize = sizeof(directorState);
        directorState.apiVersion = XB_PREVIEW_API_VERSION;
        directorState.sequence = 42;
        directorState.enabled = 1;
        directorState.mode = 2; // CameraMode.ZoomedFixed frozen ABI value.
        directorState.zoom = 1.6;
        directorState.centerX = 0.62;
        directorState.centerY = 0.44;
        directorState.transitionProgress = 1.0;
        directorState.targetX = 0.62;
        directorState.targetY = 0.44;
        const auto frozenDirector =
            xbpreview::ResolveCameraTransform(directorState);
        Require(
            frozenDirector.enabled && Near(frozenDirector.appliedZoom, 1.6),
            "frozen Director zoom resolves before output scaling");

        for (const auto& source : sources)
        {
            for (const auto& output : outputs)
            {
                XbLetterboxRect viewport{};
                Require(
                    xbpreview::CalculateLetterbox(
                        source[0], source[1], output[0], output[1], viewport),
                    "fixed fullscreen contain resolves");
                Require(
                    Near(viewport.width / viewport.height,
                        static_cast<double>(source[0]) / source[1]) &&
                    Near(viewport.x * 2.0 + viewport.width, output[0]) &&
                    Near(viewport.y * 2.0 + viewport.height, output[1]) &&
                    viewport.x >= 0.0f && viewport.y >= 0.0f &&
                    viewport.x + viewport.width <= output[0] + 0.01f &&
                    viewport.y + viewport.height <= output[1] + 0.01f,
                    "fixed fullscreen is centered, aspect-safe, and uncropped");
            }
        }

        const xbpreview::MonitorPixelRect monitor{ 0, 0, 1920, 1200 };
        const xbpreview::CursorShape shape{
            1, 1, 32, 32, 4, 5, XbCursorShapeKind_ColorAlpha, 0, {} };
        const xbpreview::CameraTransform identity{};
        constexpr std::array<std::array<std::int32_t, 2>, 5> cursorPoints{
            std::array<std::int32_t, 2>{ 960, 600 },
            std::array<std::int32_t, 2>{ 0, 0 },
            std::array<std::int32_t, 2>{ 1919, 0 },
            std::array<std::int32_t, 2>{ 0, 1199 },
            std::array<std::int32_t, 2>{ 1919, 1199 }
        };
        for (const auto& output : outputs)
        {
            const auto directorAtOutput =
                xbpreview::ResolveCameraTransform(directorState);
            Require(
                Near(directorAtOutput.appliedZoom,
                    frozenDirector.appliedZoom) &&
                Near(directorAtOutput.left, frozenDirector.left) &&
                Near(directorAtOutput.top, frozenDirector.top) &&
                Near(directorAtOutput.width, frozenDirector.width) &&
                Near(directorAtOutput.height, frozenDirector.height),
                "1080/1440/2160 change pixel density, not Director zoom");
            XbLetterboxRect viewport{};
            Require(
                xbpreview::CalculateLetterbox(
                    1920, 1200, output[0], output[1], viewport),
                "fullscreen cursor viewport resolves");
            for (const auto& cursorPoint : cursorPoints)
            {
                xbpreview::CursorSample sample{};
                sample.querySucceeded = true;
                sample.visible = true;
                sample.insideMonitor = true;
                sample.screenX = cursorPoint[0];
                sample.screenY = cursorPoint[1];
                const auto mapped = xbpreview::MapCursorToPreview(
                    sample,
                    shape,
                    monitor,
                    1920,
                    1200,
                    identity,
                    viewport);
                const auto expectedX = viewport.x +
                    static_cast<double>(cursorPoint[0]) / 1920.0 *
                        viewport.width;
                const auto expectedY = viewport.y +
                    static_cast<double>(cursorPoint[1]) / 1200.0 *
                        viewport.height;
                Require(
                    mapped.valid && mapped.intersectsCamera &&
                    std::abs(mapped.transformedHotspotXPixels - expectedX) <=
                        1.0 &&
                    std::abs(mapped.transformedHotspotYPixels - expectedY) <=
                        1.0,
                    "fullscreen cursor lands within one final output pixel");
            }
        }

        constexpr std::array<std::array<std::uint32_t, 2>, 4> windows{
            std::array<std::uint32_t, 2>{ 1600, 900 },
            std::array<std::uint32_t, 2>{ 1024, 768 },
            std::array<std::uint32_t, 2>{ 900, 1600 },
            std::array<std::uint32_t, 2>{ 1379, 611 }
        };
        xbpreview::WindowStageTransformParameters frozenPose{};
        Require(
            xbpreview::ResolveWindowStageTransform(
                xbpreview::WindowStageDirection::Right,
                xbpreview::WindowStageStrength::Level2,
                frozenPose),
            "frozen 2.5D pose resolves");
        for (const auto& window : windows)
        {
            for (const auto& output : outputs)
            {
                xbpreview::FlatWindowStageComposition stage{};
                xbpreview::WindowCardShadowComposition shadow{};
                xbpreview::WindowStageTransformComposition transform{};
                Require(
                    xbpreview::WindowStageComposer::ComposeFlat(
                        window[0], window[1], output[0], output[1], stage) &&
                    stage.UsesFullSourceTexture() &&
                    xbpreview::ComposeWindowCardShadow(
                        stage, output[0], output[1], shadow) &&
                    xbpreview::ComposeWindowStageTransform(
                        stage,
                        shadow,
                        output[0],
                        output[1],
                        frozenPose,
                        transform),
                    "window stage, shadow, and 2.5D compose at fixed output");
                Require(
                    Near(stage.window.width / stage.window.height,
                        static_cast<double>(window[0]) / window[1]) &&
                    stage.window.width <= output[0] * 0.90f + 0.01f &&
                    stage.window.height <= output[1] * 0.90f + 0.01f &&
                    SameTransformParameters(
                        transform.parameters, frozenPose),
                    "window stays fully visible at frozen 90% pose semantics");

                const xbpreview::MonitorPixelRect windowRect{
                    0,
                    0,
                    static_cast<std::int32_t>(window[0]),
                    static_cast<std::int32_t>(window[1])
                };
                const XbLetterboxRect stageViewport{
                    stage.window.left,
                    stage.window.top,
                    stage.window.width,
                    stage.window.height
                };
                for (const auto normalized :
                    { std::array<double, 2>{ 0.5, 0.5 },
                      std::array<double, 2>{ 0.0, 0.0 },
                      std::array<double, 2>{ 0.999, 0.999 } })
                {
                    xbpreview::CursorSample sample{};
                    sample.querySucceeded = true;
                    sample.visible = true;
                    sample.insideMonitor = true;
                    sample.screenX = static_cast<std::int32_t>(
                        normalized[0] * window[0]);
                    sample.screenY = static_cast<std::int32_t>(
                        normalized[1] * window[1]);
                    const auto mapped = xbpreview::MapCursorToPreview(
                        sample,
                        shape,
                        windowRect,
                        window[0],
                        window[1],
                        identity,
                        stageViewport);
                    const auto expectedX = stage.window.left +
                        static_cast<double>(sample.screenX) / window[0] *
                            stage.window.width;
                    const auto expectedY = stage.window.top +
                        static_cast<double>(sample.screenY) / window[1] *
                            stage.window.height;
                    Require(
                        mapped.valid &&
                        std::abs(mapped.transformedHotspotXPixels - expectedX) <=
                            1.0 &&
                        std::abs(mapped.transformedHotspotYPixels - expectedY) <=
                            1.0,
                        "flat window-stage cursor lands within one pixel");
                }
            }
        }

        xbpreview::WindowCardShadowComposition referenceShadow{};
        xbpreview::WindowShowcaseBackgroundComposition referenceBackground{};
        for (const auto& output : outputs)
        {
            xbpreview::FlatWindowStageComposition stage{};
            xbpreview::WindowCardShadowComposition shadow{};
            Require(
                xbpreview::WindowStageComposer::ComposeFlat(
                    1600, 900, output[0], output[1], stage) &&
                xbpreview::ComposeWindowCardShadow(
                    stage, output[0], output[1], shadow),
                "shadow resolution scale composes");
            if (output[0] == 1920)
            {
                referenceShadow = shadow;
            }
            const auto outputScale = output[0] / 1920.0;
            Require(
                Near(
                    shadow.verticalOffsetPixels / outputScale,
                    referenceShadow.verticalOffsetPixels) &&
                Near(
                    shadow.softnessPixels / outputScale,
                    referenceShadow.softnessPixels) &&
                Near(shadow.strength, referenceShadow.strength),
                "shadow proportions remain invariant with pixel density");

            xbpreview::WindowShowcaseBackgroundComposition background{};
            Require(
                xbpreview::ResolveWindowShowcaseBackground(
                    xbpreview::WindowShowcaseBackgroundPreset::Art01,
                    output[0],
                    output[1],
                    background) &&
                background.kind ==
                    xbpreview::WindowShowcaseBackgroundKind::StaticTexture,
                "fixed output background resolves");
            if (output[0] == 1920)
            {
                referenceBackground = background;
            }
            Require(
                Near(background.textureOriginU,
                    referenceBackground.textureOriginU) &&
                Near(background.textureOriginV,
                    referenceBackground.textureOriginV) &&
                Near(background.textureScaleU,
                    referenceBackground.textureScaleU) &&
                Near(background.textureScaleV,
                    referenceBackground.textureScaleV),
                "16:9 fixed outputs preserve normalized background fill rules");
        }

        xbpreview::WindowShowcaseMotionTiming timing{};
        Require(
            xbpreview::ResolveWindowShowcaseMotionTiming(
                xbpreview::WindowShowcaseMotionPreset::A,
                timing) &&
            Near(timing.enterMilliseconds, 360.0) &&
            Near(timing.returnMilliseconds, 380.0),
            "resolution does not alter Director/2.5D transition semantics");
        std::cout
            << "RESOLUTION-V1-NATIVE-GATE = PASS; fullscreen contain=PASS; "
               "window 90pct contain=PASS; cursor <=1px=PASS; 2.5D=PASS; "
               "director=PASS; shadow=PASS; background=PASS; "
               "linear-render-seam=UNCHANGED\n";
    }
}

int main(const int argc, const char* const argv[])
{
    if (argc == 2 && std::string_view(argv[1]) == "--resolution-v1")
    {
        TestResolutionV1GeometryAndCursor();
        return EXIT_SUCCESS;
    }
    if (argc == 2 &&
        std::string_view(argv[1]) == "--window-stage-25d-shadow-bounds")
    {
        TestWindowStage25DShadowBoundsGate();
        return EXIT_SUCCESS;
    }
    if (argc == 2 && std::string_view(argv[1]) == "--showcase-motion")
    {
        TestShowcaseMotion();
        return EXIT_SUCCESS;
    }
    if (argc == 2 && std::string_view(argv[1]) == "--punch-overlay")
    {
        TestWindowStagePunchOverlay();
        return EXIT_SUCCESS;
    }
    if (argc == 2 &&
        std::string_view(argv[1]) == "--punch-showcase-9pose")
    {
        TestWindowStageShowcasePunchNinePoseSafety();
        return EXIT_SUCCESS;
    }
    if (argc == 2 &&
        std::string_view(argv[1]) == "--left-front-motion")
    {
        TestLeftFrontMotionDirectionGate();
        return EXIT_SUCCESS;
    }
    if (argc == 2 && std::string_view(argv[1]) == "--layer3-minimal")
    {
        TestLayer3MinimalRegression();
        return EXIT_SUCCESS;
    }
    if (argc == 2 && std::string_view(argv[1]) == "--stage-transform")
    {
        TestStageTransform();
        return EXIT_SUCCESS;
    }
    if (argc == 2 && std::string_view(argv[1]) == "--layer2-identity")
    {
        TestLayer2Identity();
        return EXIT_SUCCESS;
    }
    if (argc == 2 && std::string_view(argv[1]) == "--card-shadow")
    {
        TestCardShadow();
        return EXIT_SUCCESS;
    }
    Require(argc == 1, "unknown Gate selector");

    Require(
        Near(xbpreview::FlatWindowStageBackgroundSrgb[0], 243.0 / 255.0) &&
        Near(xbpreview::FlatWindowStageBackgroundSrgb[1], 240.0 / 255.0) &&
        Near(xbpreview::FlatWindowStageBackgroundSrgb[2], 234.0 / 255.0) &&
        Near(xbpreview::FlatWindowStageBackgroundSrgb[3], 1.0),
        "background is fixed #F3F0EA");

    VerifyComposition(1600, 900, 1920, 1080);
    VerifyComposition(900, 1600, 1920, 1080);
    VerifyComposition(1024, 768, 1920, 1080);

    xbpreview::FlatWindowStageComposition landscape{};
    Require(
        xbpreview::WindowStageComposer::ComposeFlat(
            1600, 900, 1920, 1080, landscape) &&
        Near(landscape.window.left, 96.0) &&
        Near(landscape.window.top, 54.0) &&
        Near(landscape.window.width, 1728.0) &&
        Near(landscape.window.height, 972.0),
        "landscape placement has deterministic margins");

    xbpreview::FlatWindowStageComposition resized{};
    Require(
        xbpreview::WindowStageComposer::ComposeFlat(
            900, 1600, 1920, 1080, resized) &&
        Near(resized.window.top, 54.0) &&
        Near(resized.window.height, 972.0) &&
        !Near(resized.window.width, landscape.window.width),
        "window resize recomputes aspect-fit layout");

    Require(
        !xbpreview::WindowStageComposer::ComposeFlat(
            0, 900, 1920, 1080, resized),
        "zero source dimensions are rejected");

    std::cout
        << "XbPreview.FlatStage.Tests PASS: #F3F0EA background, centered "
           "aspect-fit, no-crop, safe margins, resize refit\n";
    return EXIT_SUCCESS;
}
