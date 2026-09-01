#pragma once

#include <algorithm>
#include <cmath>
#include <cstddef>
#include <deque>
#include <vector>

namespace xbpreview
{
    struct LatencySnapshot
    {
        double recent{};
        double p50{};
        double p95{};
        double maximum{};
    };

    class LatencyStatistics final
    {
    public:
        explicit LatencyStatistics(const std::size_t capacity = 2048)
            : capacity_((std::max)(capacity, std::size_t{ 1 }))
        {
        }

        void Add(const double milliseconds)
        {
            if (!std::isfinite(milliseconds) || milliseconds < 0.0)
            {
                return;
            }

            if (values_.size() == capacity_)
            {
                values_.pop_front();
            }

            values_.push_back(milliseconds);
        }

        [[nodiscard]] LatencySnapshot Snapshot() const
        {
            LatencySnapshot result{};
            if (values_.empty())
            {
                return result;
            }

            std::vector<double> sorted(values_.begin(), values_.end());
            std::sort(sorted.begin(), sorted.end());
            result.recent = values_.back();
            result.p50 = Percentile(sorted, 0.50);
            result.p95 = Percentile(sorted, 0.95);
            result.maximum = sorted.back();
            return result;
        }

        [[nodiscard]] std::size_t Count() const noexcept
        {
            return values_.size();
        }

        void Clear() noexcept
        {
            values_.clear();
        }

    private:
        static double Percentile(const std::vector<double>& sorted, const double fraction)
        {
            const auto rank = static_cast<std::size_t>(
                std::ceil(fraction * static_cast<double>(sorted.size())));
            const auto index = (std::max)(std::size_t{ 1 }, rank) - 1;
            return sorted[(std::min)(index, sorted.size() - 1)];
        }

        std::size_t capacity_;
        std::deque<double> values_;
    };
}
