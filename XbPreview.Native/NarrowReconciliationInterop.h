#pragma once

#include "XbPreviewApi.h"

namespace xbpreview::interop
{
    XbPreviewResult GetNarrowReconciliationAbiLayoutV1(
        XbNarrowReconciliationAbiLayoutV1* layout);

    XbPreviewResult ReconcileNarrowSessionV1(
        const XbNarrowReconciliationOptionsV1* options,
        XbNarrowReconciliationResultV1* result);

    XbPreviewResult ReconcileNarrowSessionForOutputRootV1(
        const XbNarrowReconciliationOutputRootOptionsV1* options,
        XbNarrowReconciliationResultV1* result);
}
