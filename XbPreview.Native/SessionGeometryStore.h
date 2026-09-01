#pragma once

#include "XbPreviewApi.h"

#include <mutex>
#include <optional>

namespace xbpreview
{
    class SessionGeometryStore final
    {
    public:
        XbPreviewResult Configure(
            const XbPreviewSessionGeometryV1& value,
            XbPreviewState state) noexcept;

        XbPreviewResult Activate() noexcept;

        void EndSession() noexcept;

        [[nodiscard]] bool HasConfigured() const noexcept;

        [[nodiscard]] std::optional<XbPreviewSessionGeometryV1>
            ConfiguredSnapshot() const noexcept;

        [[nodiscard]] std::optional<XbPreviewSessionGeometryV1>
            ActiveSnapshot() const noexcept;

        [[nodiscard]] bool ActiveSourceMatches(
            std::int32_t width,
            std::int32_t height) const noexcept;

        [[nodiscard]] static XbPreviewResult Validate(
            const XbPreviewSessionGeometryV1& value) noexcept;

    private:
        [[nodiscard]] static bool SameKnownGeometry(
            const XbPreviewSessionGeometryV1& left,
            const XbPreviewSessionGeometryV1& right) noexcept;

        [[nodiscard]] static XbPreviewSessionGeometryV1 Canonicalize(
            const XbPreviewSessionGeometryV1& value) noexcept;

        mutable std::mutex mutex_;
        std::optional<XbPreviewSessionGeometryV1> configured_;
        std::optional<XbPreviewSessionGeometryV1> active_;
    };
}
