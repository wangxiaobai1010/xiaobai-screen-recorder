#pragma once

#include "CursorCaptureState.h"

#include <list>
#include <memory>
#include <unordered_map>

namespace xbpreview
{
    class CursorShapeConverter final
    {
    public:
        [[nodiscard]] bool Convert(
            HCURSOR source,
            CursorShape& shape,
            std::int32_t& result,
            std::uint32_t& lastError) const noexcept;
    };

    class CursorShapeCache final
    {
    public:
        static constexpr std::size_t MaximumEntries = 32;

        CursorShapeCache();

        [[nodiscard]] CursorCacheResult Resolve(HCURSOR cursor) noexcept;
        void Clear() noexcept;

        [[nodiscard]] std::size_t Size() const noexcept
        {
            return entries_.size();
        }

    private:
        struct Entry
        {
            std::uintptr_t sourceHandle{};
            std::shared_ptr<const CursorShape> shape;
            bool builtInFallback{};
        };

        static std::shared_ptr<const CursorShape> CreateBuiltInArrow();

        CursorShapeConverter converter_;
        std::shared_ptr<const CursorShape> builtInArrow_;
        std::list<Entry> entries_;
        std::unordered_map<std::uintptr_t, std::list<Entry>::iterator> byHandle_;
        std::uint64_t nextShapeId_{ 2 };
        std::uint64_t nextGeneration_{ 1 };
    };
}
