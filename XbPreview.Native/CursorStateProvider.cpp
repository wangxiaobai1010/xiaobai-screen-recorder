#include "CursorStateProvider.h"

#include <windows.h>

namespace xbpreview
{
    CursorSample CursorStateProvider::Sample(
        const MonitorPixelRect& monitor) noexcept
    {
        CursorSample sample{};
        sample.sequence = ++sequence_;

        LARGE_INTEGER now{};
        if (QueryPerformanceCounter(&now))
        {
            sample.timestampQpc = now.QuadPart;
        }

        CURSORINFO info{};
        info.cbSize = sizeof(info);
        SetLastError(ERROR_SUCCESS);
        sample.querySucceeded = GetCursorInfo(&info) != FALSE;
        sample.lastError = sample.querySucceeded
            ? ERROR_SUCCESS
            : GetLastError();
        if (!sample.querySucceeded)
        {
            return sample;
        }

        sample.visible = (info.flags & CURSOR_SHOWING) != 0;
        sample.screenX = info.ptScreenPos.x;
        sample.screenY = info.ptScreenPos.y;
        sample.cursorHandle = reinterpret_cast<std::uintptr_t>(info.hCursor);
        sample.insideMonitor =
            sample.screenX >= monitor.left &&
            sample.screenX < monitor.right &&
            sample.screenY >= monitor.top &&
            sample.screenY < monitor.bottom;
        return sample;
    }
}
