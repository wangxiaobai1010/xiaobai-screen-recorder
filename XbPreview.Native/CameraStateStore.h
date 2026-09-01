#pragma once

#include "CameraTransform.h"

#include <mutex>

namespace xbpreview
{
    class CameraStateStore final
    {
    public:
        CameraStateStore()
        {
            state_.structSize = sizeof(XbCameraState);
            state_.apiVersion = XB_PREVIEW_API_VERSION;
            state_.zoom = 1.0;
            state_.centerX = 0.5;
            state_.centerY = 0.5;
            state_.transitionProgress = 1.0;
            state_.targetX = 0.5;
            state_.targetY = 0.5;
            lastValid_ = state_;
        }

        XbPreviewResult Update(const XbCameraState& value) noexcept
        {
            std::lock_guard lock(mutex_);
            if (!IsValidCameraState(value))
            {
                const auto invalidSequence = value.sequence;
                state_ = {};
                state_.structSize = sizeof(XbCameraState);
                state_.apiVersion = XB_PREVIEW_API_VERSION;
                state_.sequence = invalidSequence;
                state_.zoom = 1.0;
                state_.centerX = 0.5;
                state_.centerY = 0.5;
                state_.transitionProgress = 1.0;
                state_.targetX = 0.5;
                state_.targetY = 0.5;
                return XbPreviewResult_InvalidCameraState;
            }
            if (value.sequence <= state_.sequence)
            {
                return XbPreviewResult_StaleCameraState;
            }
            state_ = value;
            lastValid_ = value;
            return XbPreviewResult_Ok;
        }

        [[nodiscard]] XbCameraState LastValidSnapshot() const noexcept
        {
            std::lock_guard lock(mutex_);
            return lastValid_;
        }

        [[nodiscard]] XbCameraState Snapshot() const noexcept
        {
            std::lock_guard lock(mutex_);
            return state_;
        }

    private:
        mutable std::mutex mutex_;
        XbCameraState state_{};
        XbCameraState lastValid_{};
    };
}
