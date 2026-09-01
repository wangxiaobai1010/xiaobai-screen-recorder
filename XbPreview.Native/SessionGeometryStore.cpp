#include "SessionGeometryStore.h"

#include <cstdint>

namespace xbpreview
{
    XbPreviewResult SessionGeometryStore::Configure(
        const XbPreviewSessionGeometryV1& value,
        const XbPreviewState state) noexcept
    {
        if (state != XbPreviewState_Stopped)
        {
            return XbPreviewResult_InvalidState;
        }

        const auto validation = Validate(value);
        if (validation != XbPreviewResult_Ok)
        {
            return validation;
        }

        const auto candidate = Canonicalize(value);
        std::lock_guard lock(mutex_);
        if (configured_.has_value())
        {
            if (candidate.geometryRevision < configured_->geometryRevision)
            {
                return XbPreviewResult_StaleRevision;
            }
            if (candidate.geometryRevision == configured_->geometryRevision)
            {
                return SameKnownGeometry(candidate, *configured_)
                    ? XbPreviewResult_Ok
                    : XbPreviewResult_RevisionConflict;
            }
        }

        configured_ = candidate;
        return XbPreviewResult_Ok;
    }

    XbPreviewResult SessionGeometryStore::Activate() noexcept
    {
        std::lock_guard lock(mutex_);
        if (!configured_.has_value())
        {
            return XbPreviewResult_InvalidGeometry;
        }
        active_ = configured_;
        return XbPreviewResult_Ok;
    }

    void SessionGeometryStore::EndSession() noexcept
    {
        std::lock_guard lock(mutex_);
        active_.reset();
    }

    bool SessionGeometryStore::HasConfigured() const noexcept
    {
        std::lock_guard lock(mutex_);
        return configured_.has_value();
    }

    std::optional<XbPreviewSessionGeometryV1>
        SessionGeometryStore::ConfiguredSnapshot() const noexcept
    {
        std::lock_guard lock(mutex_);
        return configured_;
    }

    std::optional<XbPreviewSessionGeometryV1>
        SessionGeometryStore::ActiveSnapshot() const noexcept
    {
        std::lock_guard lock(mutex_);
        return active_;
    }

    bool SessionGeometryStore::ActiveSourceMatches(
        const std::int32_t width,
        const std::int32_t height) const noexcept
    {
        std::lock_guard lock(mutex_);
        return active_.has_value() &&
            active_->sourceWidth == width &&
            active_->sourceHeight == height;
    }

    XbPreviewResult SessionGeometryStore::Validate(
        const XbPreviewSessionGeometryV1& value) noexcept
    {
        if (value.structSize < sizeof(XbPreviewSessionGeometryV1) ||
            value.version != XB_PREVIEW_SESSION_GEOMETRY_VERSION_1)
        {
            return XbPreviewResult_UnsupportedStructVersion;
        }
        if (value.flags != 0 || value.reserved0 != 0)
        {
            return XbPreviewResult_InvalidGeometry;
        }
        if (value.geometryRevision == 0 ||
            value.sourceWidth <= 0 ||
            value.sourceHeight <= 0 ||
            value.captureLeft < 0 ||
            value.captureTop < 0 ||
            value.captureWidth <= 0 ||
            value.captureHeight <= 0 ||
            value.outputWidth <= 0 ||
            value.outputHeight <= 0)
        {
            return XbPreviewResult_InvalidGeometry;
        }

        const auto right =
            static_cast<std::int64_t>(value.captureLeft) +
            static_cast<std::int64_t>(value.captureWidth);
        const auto bottom =
            static_cast<std::int64_t>(value.captureTop) +
            static_cast<std::int64_t>(value.captureHeight);
        if (right > value.sourceWidth || bottom > value.sourceHeight)
        {
            return XbPreviewResult_InvalidGeometry;
        }
        return XbPreviewResult_Ok;
    }

    bool SessionGeometryStore::SameKnownGeometry(
        const XbPreviewSessionGeometryV1& left,
        const XbPreviewSessionGeometryV1& right) noexcept
    {
        return left.version == right.version &&
            left.sourceWidth == right.sourceWidth &&
            left.sourceHeight == right.sourceHeight &&
            left.captureLeft == right.captureLeft &&
            left.captureTop == right.captureTop &&
            left.captureWidth == right.captureWidth &&
            left.captureHeight == right.captureHeight &&
            left.outputWidth == right.outputWidth &&
            left.outputHeight == right.outputHeight &&
            left.geometryRevision == right.geometryRevision &&
            left.flags == right.flags &&
            left.reserved0 == right.reserved0;
    }

    XbPreviewSessionGeometryV1 SessionGeometryStore::Canonicalize(
        const XbPreviewSessionGeometryV1& value) noexcept
    {
        auto result = value;
        result.structSize = sizeof(XbPreviewSessionGeometryV1);
        return result;
    }
}
