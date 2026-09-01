#pragma once

#include "WindowStageTransform.h"

#include <algorithm>
#include <cmath>
#include <cstdint>
#include <cwchar>

namespace xbpreview
{
    // This candidate selector is intentionally presentation-only. It owns no
    // camera or Stage state; it derives one transient card transform from the
    // current Content Camera zoom and the current Layer 4 base pose.
    enum class WindowStagePunchCandidate : std::uint8_t
    {
        Disabled,
        Light,
        Showcase,
        Strong
    };

    struct WindowStagePunchTuning
    {
        // Fraction of the base pose's remaining scale headroom at the two
        // existing Manual Zoom endpoints. Rotation, perspective and placement
        // deliberately have no Punch tuning.
        float standardHeadroomFraction{};
        float strongHeadroomFraction{};

        [[nodiscard]] bool IsValid() const noexcept
        {
            return std::isfinite(standardHeadroomFraction) &&
                std::isfinite(strongHeadroomFraction) &&
                standardHeadroomFraction > 0.0f &&
                standardHeadroomFraction < strongHeadroomFraction &&
                strongHeadroomFraction < 1.0f;
        }
    };

    inline constexpr double WindowStagePunchWideZoom = 1.0;
    inline constexpr double WindowStagePunchStandardZoom = 1.6;
    inline constexpr double WindowStagePunchStrongZoom = 2.0;

    inline constexpr WindowStagePunchTuning WindowStagePunchLight{
        0.12f, 0.24f };
    inline constexpr WindowStagePunchTuning WindowStagePunchShowcase{
        0.18f, 0.36f };
    inline constexpr WindowStagePunchTuning WindowStagePunchStrong{
        0.30f, 0.44f };

    [[nodiscard]] inline bool IsKnownWindowStagePunchCandidate(
        const WindowStagePunchCandidate candidate) noexcept
    {
        return candidate == WindowStagePunchCandidate::Disabled ||
            candidate == WindowStagePunchCandidate::Light ||
            candidate == WindowStagePunchCandidate::Showcase ||
            candidate == WindowStagePunchCandidate::Strong;
    }

    [[nodiscard]] inline bool ResolveWindowStagePunchTuning(
        const WindowStagePunchCandidate candidate,
        WindowStagePunchTuning& tuning) noexcept
    {
        switch (candidate)
        {
        case WindowStagePunchCandidate::Light:
            tuning = WindowStagePunchLight;
            return true;
        case WindowStagePunchCandidate::Showcase:
            tuning = WindowStagePunchShowcase;
            return true;
        case WindowStagePunchCandidate::Strong:
            tuning = WindowStagePunchStrong;
            return true;
        case WindowStagePunchCandidate::Disabled:
            return false;
        }
        return false;
    }

    [[nodiscard]] inline bool TryParseWindowStagePunchCandidate(
        const wchar_t* const value,
        WindowStagePunchCandidate& candidate) noexcept
    {
        if (value == nullptr)
        {
            return false;
        }
        if (_wcsicmp(value, L"A") == 0)
        {
            candidate = WindowStagePunchCandidate::Light;
            return true;
        }
        if (_wcsicmp(value, L"B") == 0)
        {
            candidate = WindowStagePunchCandidate::Showcase;
            return true;
        }
        if (_wcsicmp(value, L"C") == 0)
        {
            candidate = WindowStagePunchCandidate::Strong;
            return true;
        }
        return false;
    }

    [[nodiscard]] inline bool ResolveWindowStagePunchProgress(
        const WindowStagePunchTuning& tuning,
        const double appliedZoom,
        float& progress) noexcept
    {
        progress = 0.0f;
        if (!tuning.IsValid() || !std::isfinite(appliedZoom) ||
            appliedZoom < WindowStagePunchWideZoom ||
            appliedZoom > WindowStagePunchStrongZoom)
        {
            return false;
        }
        if (appliedZoom <= WindowStagePunchWideZoom)
        {
            return true;
        }
        if (appliedZoom <= WindowStagePunchStandardZoom)
        {
            const auto phase = static_cast<float>(
                (appliedZoom - WindowStagePunchWideZoom) /
                (WindowStagePunchStandardZoom - WindowStagePunchWideZoom));
            progress = tuning.standardHeadroomFraction * phase;
            return true;
        }

        const auto phase = static_cast<float>(
            (appliedZoom - WindowStagePunchStandardZoom) /
            (WindowStagePunchStrongZoom - WindowStagePunchStandardZoom));
        progress = tuning.standardHeadroomFraction +
            ((tuning.strongHeadroomFraction -
                tuning.standardHeadroomFraction) * phase);
        return true;
    }

    [[nodiscard]] inline bool ComposeWindowStagePunchOverlay(
        const WindowStageTransformParameters& basePose,
        const WindowStagePunchCandidate candidate,
        const double appliedZoom,
        WindowStageTransformParameters& presentation) noexcept
    {
        presentation = basePose;
        if (!basePose.IsValid() ||
            !IsKnownWindowStagePunchCandidate(candidate) ||
            !std::isfinite(appliedZoom) ||
            appliedZoom < WindowStagePunchWideZoom ||
            appliedZoom > WindowStagePunchStrongZoom)
        {
            return false;
        }
        if (candidate == WindowStagePunchCandidate::Disabled ||
            basePose.IsIdentity() ||
            appliedZoom <= WindowStagePunchWideZoom)
        {
            return true;
        }

        WindowStagePunchTuning tuning{};
        float progress{};
        if (!ResolveWindowStagePunchTuning(candidate, tuning) ||
            !ResolveWindowStagePunchProgress(tuning, appliedZoom, progress))
        {
            return false;
        }

        const auto scaleHeadroom = 1.0f - basePose.scale;
        presentation.scale = (std::clamp)(
            basePose.scale + (scaleHeadroom * progress),
            basePose.scale,
            1.0f);
        return presentation.IsValid();
    }
}
