#pragma once

#include "XbPreviewApi.h"

namespace xbpreview
{
    struct CursorModeDecision
    {
        XbCursorMode requested{ XbCursorMode_SystemCursor };
        XbCursorMode actual{ XbCursorMode_SystemCursor };
        XbCursorFallbackReason fallback{ XbCursorFallbackReason_None };
        bool systemCursorIncluded{ true };
        bool customCursorLayerActive{};
    };

    [[nodiscard]] inline bool IsValidCursorMode(
        const XbCursorMode mode) noexcept
    {
        return mode == XbCursorMode_SystemCursor ||
            mode == XbCursorMode_CustomCursor;
    }

    [[nodiscard]] inline CursorModeDecision ResolveCursorModePolicy(
        const XbCursorMode requested,
        const bool propertyAvailable,
        const bool customRendererReady,
        const bool settingSucceeded,
        const bool readbackExcluded) noexcept
    {
        CursorModeDecision decision{};
        decision.requested = requested;
        if (requested != XbCursorMode_CustomCursor)
        {
            return decision;
        }

        if (!propertyAvailable)
        {
            decision.fallback = XbCursorFallbackReason_ApiUnavailable;
            return decision;
        }
        if (!customRendererReady)
        {
            decision.fallback =
                XbCursorFallbackReason_CustomRendererInitializationFailed;
            return decision;
        }
        if (!settingSucceeded)
        {
            decision.fallback = XbCursorFallbackReason_WgcSettingFailed;
            return decision;
        }
        if (!readbackExcluded)
        {
            decision.fallback = XbCursorFallbackReason_WgcReadbackMismatch;
            return decision;
        }

        decision.actual = XbCursorMode_CustomCursor;
        decision.systemCursorIncluded = false;
        decision.customCursorLayerActive = true;
        return decision;
    }

    [[nodiscard]] inline bool CursorOwnershipIsExclusive(
        const CursorModeDecision& decision) noexcept
    {
        return decision.systemCursorIncluded !=
            decision.customCursorLayerActive;
    }
}
