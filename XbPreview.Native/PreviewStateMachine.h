#pragma once

#include "XbPreviewApi.h"

namespace xbpreview
{
    class PreviewStateMachine final
    {
    public:
        [[nodiscard]] XbPreviewState State() const noexcept
        {
            return state_;
        }

        bool BeginStart() noexcept
        {
            if (state_ != XbPreviewState_Stopped)
            {
                return false;
            }

            state_ = XbPreviewState_Starting;
            return true;
        }

        bool MarkRunning() noexcept
        {
            if (state_ != XbPreviewState_Starting)
            {
                return false;
            }

            state_ = XbPreviewState_Running;
            return true;
        }

        bool BeginStop() noexcept
        {
            if (state_ == XbPreviewState_Stopped)
            {
                return false;
            }

            state_ = XbPreviewState_Stopping;
            return true;
        }

        void MarkStopped() noexcept
        {
            state_ = XbPreviewState_Stopped;
        }

        void MarkError() noexcept
        {
            state_ = XbPreviewState_Error;
        }

    private:
        XbPreviewState state_{ XbPreviewState_Stopped };
    };
}
