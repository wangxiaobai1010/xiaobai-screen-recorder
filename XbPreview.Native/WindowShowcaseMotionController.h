#pragma once

#include "WindowStageTransform.h"

#include <algorithm>
#include <cmath>
#include <cstdint>
#include <cwchar>

namespace xbpreview
{
    enum class WindowShowcaseMotionState : std::uint8_t
    {
        Idle,
        Transition,
        Stay,
        Return
    };

    enum class WindowShowcaseMotionPreset : std::uint8_t
    {
        A,
        B,
        C
    };

    enum class WindowShowcaseMotionEasing : std::uint8_t
    {
        SmootherStep,
        SmoothStep,
        CubicEaseInOut
    };

    struct WindowShowcaseMotionTiming
    {
        double enterMilliseconds{};
        double holdMilliseconds{};
        double returnMilliseconds{};
        WindowShowcaseMotionEasing enterEasing{};
        WindowShowcaseMotionEasing returnEasing{};

        [[nodiscard]] bool IsValid() const noexcept
        {
            return std::isfinite(enterMilliseconds) &&
                std::isfinite(holdMilliseconds) &&
                std::isfinite(returnMilliseconds) &&
                enterMilliseconds > 0.0 && holdMilliseconds >= 0.0 &&
                returnMilliseconds > 0.0;
        }
    };

    [[nodiscard]] inline bool ResolveWindowShowcaseMotionTiming(
        const WindowShowcaseMotionPreset preset,
        WindowShowcaseMotionTiming& timing) noexcept
    {
        switch (preset)
        {
        case WindowShowcaseMotionPreset::A:
            timing = WindowShowcaseMotionTiming{
                360.0,
                900.0,
                380.0,
                WindowShowcaseMotionEasing::SmootherStep,
                WindowShowcaseMotionEasing::SmootherStep
            };
            return true;
        case WindowShowcaseMotionPreset::B:
            timing = WindowShowcaseMotionTiming{
                260.0,
                700.0,
                300.0,
                WindowShowcaseMotionEasing::SmoothStep,
                WindowShowcaseMotionEasing::SmoothStep
            };
            return true;
        case WindowShowcaseMotionPreset::C:
            timing = WindowShowcaseMotionTiming{
                180.0,
                450.0,
                220.0,
                WindowShowcaseMotionEasing::CubicEaseInOut,
                WindowShowcaseMotionEasing::CubicEaseInOut
            };
            return true;
        }
        return false;
    }

    [[nodiscard]] inline bool TryParseWindowShowcaseMotionPreset(
        const wchar_t* const value,
        WindowShowcaseMotionPreset& preset) noexcept
    {
        if (value == nullptr)
        {
            return false;
        }
        if (_wcsicmp(value, L"A") == 0)
        {
            preset = WindowShowcaseMotionPreset::A;
            return true;
        }
        if (_wcsicmp(value, L"B") == 0)
        {
            preset = WindowShowcaseMotionPreset::B;
            return true;
        }
        if (_wcsicmp(value, L"C") == 0)
        {
            preset = WindowShowcaseMotionPreset::C;
            return true;
        }
        return false;
    }

    class WindowShowcaseMotionController final
    {
    public:
        WindowShowcaseMotionController() = default;
        WindowShowcaseMotionController(
            const WindowShowcaseMotionController&) = delete;
        WindowShowcaseMotionController& operator=(
            const WindowShowcaseMotionController&) = delete;

        void Reset(const double elapsedMilliseconds = 0.0) noexcept
        {
            state_ = WindowShowcaseMotionState::Idle;
            current_ = WindowStageIdentityTransform;
            segmentStart_ = WindowStageIdentityTransform;
            target_ = WindowStageIdentityTransform;
            timing_ = {};
            segmentStartMilliseconds_ = elapsedMilliseconds;
            lastElapsedMilliseconds_ = elapsedMilliseconds;
        }

        // A new segment always starts at CurrentTransform(). This is the small
        // ownership seam needed for future retargeting without an Identity jump.
        [[nodiscard]] bool Start(
            const WindowStageTransformParameters& target,
            const WindowShowcaseMotionTiming& timing,
            const double elapsedMilliseconds) noexcept
        {
            if (!target.IsValid() || !timing.IsValid() ||
                !std::isfinite(elapsedMilliseconds) ||
                elapsedMilliseconds < lastElapsedMilliseconds_)
            {
                return false;
            }
            if (!Update(elapsedMilliseconds))
            {
                return false;
            }

            segmentStart_ = current_;
            target_ = target;
            timing_ = timing;
            segmentStartMilliseconds_ = elapsedMilliseconds;
            lastElapsedMilliseconds_ = elapsedMilliseconds;
            state_ = WindowShowcaseMotionState::Transition;
            return true;
        }

        [[nodiscard]] bool Start(
            const WindowStageTransformParameters& target,
            const WindowShowcaseMotionPreset preset,
            const double elapsedMilliseconds) noexcept
        {
            WindowShowcaseMotionTiming timing{};
            return ResolveWindowShowcaseMotionTiming(preset, timing) &&
                Start(target, timing, elapsedMilliseconds);
        }

        [[nodiscard]] bool Update(
            const double elapsedMilliseconds) noexcept
        {
            if (!std::isfinite(elapsedMilliseconds) ||
                elapsedMilliseconds < lastElapsedMilliseconds_)
            {
                return false;
            }
            lastElapsedMilliseconds_ = elapsedMilliseconds;

            for (;;)
            {
                switch (state_)
                {
                case WindowShowcaseMotionState::Idle:
                    current_ = WindowStageIdentityTransform;
                    return true;

                case WindowShowcaseMotionState::Transition:
                    if (elapsedMilliseconds <
                        segmentStartMilliseconds_ + timing_.enterMilliseconds)
                    {
                        const auto progress =
                            (elapsedMilliseconds - segmentStartMilliseconds_) /
                            timing_.enterMilliseconds;
                        current_ = Interpolate(
                            segmentStart_,
                            target_,
                            ApplyEasing(progress, timing_.enterEasing));
                        return true;
                    }
                    current_ = target_;
                    state_ = WindowShowcaseMotionState::Stay;
                    return true;

                case WindowShowcaseMotionState::Stay:
                    current_ = target_;
                    return true;

                case WindowShowcaseMotionState::Return:
                    if (elapsedMilliseconds < segmentStartMilliseconds_ +
                        timing_.returnMilliseconds)
                    {
                        const auto progress =
                            (elapsedMilliseconds - segmentStartMilliseconds_) /
                            timing_.returnMilliseconds;
                        current_ = Interpolate(
                            segmentStart_,
                            WindowStageIdentityTransform,
                            ApplyEasing(progress, timing_.returnEasing));
                        return true;
                    }
                    current_ = WindowStageIdentityTransform;
                    segmentStart_ = current_;
                    target_ = current_;
                    state_ = WindowShowcaseMotionState::Idle;
                    return true;
                }
            }
        }

        // Interrupting TRANSITION or STAY first samples that exact instant, then
        // makes it the RETURN segment's origin. No target/Identity snap occurs.
        [[nodiscard]] bool RequestReturn(
            const double elapsedMilliseconds) noexcept
        {
            if (!Update(elapsedMilliseconds))
            {
                return false;
            }
            if (state_ == WindowShowcaseMotionState::Idle ||
                state_ == WindowShowcaseMotionState::Return)
            {
                return true;
            }
            segmentStart_ = current_;
            segmentStartMilliseconds_ = elapsedMilliseconds;
            state_ = WindowShowcaseMotionState::Return;
            return true;
        }

        [[nodiscard]] WindowShowcaseMotionState State() const noexcept
        {
            return state_;
        }

        [[nodiscard]] const WindowStageTransformParameters&
            CurrentTransform() const noexcept
        {
            return current_;
        }

        [[nodiscard]] double LastElapsedMilliseconds() const noexcept
        {
            return lastElapsedMilliseconds_;
        }

    private:
        [[nodiscard]] static double ApplyEasing(
            const double progress,
            const WindowShowcaseMotionEasing easing) noexcept
        {
            const auto t = (std::clamp)(progress, 0.0, 1.0);
            switch (easing)
            {
            case WindowShowcaseMotionEasing::SmootherStep:
                return t * t * t * (t * (t * 6.0 - 15.0) + 10.0);
            case WindowShowcaseMotionEasing::SmoothStep:
                return t * t * (3.0 - 2.0 * t);
            case WindowShowcaseMotionEasing::CubicEaseInOut:
                return t < 0.5
                    ? 4.0 * t * t * t
                    : 1.0 - std::pow(-2.0 * t + 2.0, 3.0) * 0.5;
            }
            return t;
        }

        [[nodiscard]] static float Mix(
            const float from,
            const float to,
            const double progress) noexcept
        {
            return static_cast<float>(
                static_cast<double>(from) +
                (static_cast<double>(to) - from) * progress);
        }

        [[nodiscard]] static WindowStageTransformParameters Interpolate(
            const WindowStageTransformParameters& from,
            const WindowStageTransformParameters& to,
            const double easedProgress) noexcept
        {
            // One eased scalar moves the complete frozen pose coherently. Layer
            // 4 never invents or independently retimes any Layer 3 field.
            return WindowStageTransformParameters{
                Mix(from.scale, to.scale, easedProgress),
                Mix(
                    from.horizontalPlacementFraction,
                    to.horizontalPlacementFraction,
                    easedProgress),
                Mix(
                    from.verticalPlacementFraction,
                    to.verticalPlacementFraction,
                    easedProgress),
                Mix(
                    from.rotationXDegrees,
                    to.rotationXDegrees,
                    easedProgress),
                Mix(
                    from.rotationYDegrees,
                    to.rotationYDegrees,
                    easedProgress),
                Mix(
                    from.perspectiveDepth,
                    to.perspectiveDepth,
                    easedProgress)
            };
        }

        WindowShowcaseMotionState state_{
            WindowShowcaseMotionState::Idle };
        WindowStageTransformParameters current_{
            WindowStageIdentityTransform };
        WindowStageTransformParameters segmentStart_{
            WindowStageIdentityTransform };
        WindowStageTransformParameters target_{
            WindowStageIdentityTransform };
        WindowShowcaseMotionTiming timing_{};
        double segmentStartMilliseconds_{};
        double lastElapsedMilliseconds_{};
    };
}
