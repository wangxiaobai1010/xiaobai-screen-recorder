#pragma once

#include "XbPreviewApi.h"

#include <windows.h>

namespace xbpreview
{
    struct CaptureTarget
    {
        XbCaptureTargetKind kind{ XbCaptureTargetKind_Monitor };
        HWND window{};

        [[nodiscard]] bool IsWindow() const noexcept
        {
            return kind == XbCaptureTargetKind_Window;
        }
    };

    inline bool IsValidCaptureTargetKind(
        const XbCaptureTargetKind kind) noexcept
    {
        return kind == XbCaptureTargetKind_Monitor ||
            kind == XbCaptureTargetKind_Window;
    }
}
