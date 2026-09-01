#pragma once

#include "XbPreviewApi.h"

namespace xbpreview::interop
{
    XbPreviewResult GetHistoricalSessionScanAbiLayoutV1(
        XbHistoricalSessionScanAbiLayoutV1* layout);

    XbPreviewResult BeginHistoricalSessionScanV1(
        const XbHistoricalSessionScanOptionsV1* options,
        XbHistoricalSessionScanHandle* scanHandle,
        XbHistoricalSessionScanSummaryV1* summary);

    XbPreviewResult BeginHistoricalSessionScanForOutputRootV1(
        const XbHistoricalSessionScanOutputRootOptionsV1* options,
        XbHistoricalSessionScanHandle* scanHandle,
        XbHistoricalSessionScanSummaryV1* summary);

    XbPreviewResult GetHistoricalSessionV1(
        XbHistoricalSessionScanHandle scanHandle,
        std::uint32_t index,
        XbHistoricalSessionItemV1* item);

    XbPreviewResult GetHistoricalSessionScanStringV1(
        XbHistoricalSessionScanHandle scanHandle,
        XbHistoricalSessionScanStringFieldV1 field,
        wchar_t* buffer,
        std::uint32_t bufferLength,
        std::uint32_t* requiredLength);

    XbPreviewResult GetHistoricalSessionStringV1(
        XbHistoricalSessionScanHandle scanHandle,
        std::uint32_t index,
        XbHistoricalSessionStringFieldV1 field,
        wchar_t* buffer,
        std::uint32_t bufferLength,
        std::uint32_t* requiredLength);

    XbPreviewResult DestroyHistoricalSessionScanV1(
        XbHistoricalSessionScanHandle* scanHandle) noexcept;
}
