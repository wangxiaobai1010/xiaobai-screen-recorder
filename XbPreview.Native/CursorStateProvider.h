#pragma once

#include "CursorCaptureState.h"

#include <cstdint>

namespace xbpreview
{
    class CursorStateProvider final
    {
    public:
        [[nodiscard]] CursorSample Sample(
            const MonitorPixelRect& monitor) noexcept;

        void Reset() noexcept
        {
            sequence_ = 0;
        }

    private:
        std::uint64_t sequence_{};
    };
}
