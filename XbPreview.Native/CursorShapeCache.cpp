#include "CursorShapeCache.h"

#include <algorithm>
#include <chrono>
#include <limits>
#include <vector>

namespace
{
    class OwnedIcon final
    {
    public:
        explicit OwnedIcon(const HICON value) noexcept : value_(value) {}
        ~OwnedIcon()
        {
            if (value_ != nullptr)
            {
                DestroyIcon(value_);
            }
        }
        OwnedIcon(const OwnedIcon&) = delete;
        OwnedIcon& operator=(const OwnedIcon&) = delete;
        [[nodiscard]] HICON Get() const noexcept { return value_; }

    private:
        HICON value_{};
    };

    class OwnedBitmap final
    {
    public:
        OwnedBitmap() = default;
        explicit OwnedBitmap(const HBITMAP value) noexcept : value_(value) {}
        ~OwnedBitmap()
        {
            if (value_ != nullptr)
            {
                DeleteObject(value_);
            }
        }
        OwnedBitmap(const OwnedBitmap&) = delete;
        OwnedBitmap& operator=(const OwnedBitmap&) = delete;

    private:
        HBITMAP value_{};
    };

    class OwnedDc final
    {
    public:
        OwnedDc() noexcept : value_(CreateCompatibleDC(nullptr)) {}
        ~OwnedDc()
        {
            if (value_ != nullptr)
            {
                DeleteDC(value_);
            }
        }
        OwnedDc(const OwnedDc&) = delete;
        OwnedDc& operator=(const OwnedDc&) = delete;
        [[nodiscard]] HDC Get() const noexcept { return value_; }

    private:
        HDC value_{};
    };

    struct MonoBitmapInfo
    {
        BITMAPINFOHEADER header{};
        RGBQUAD colors[2]{};
    };

    [[nodiscard]] bool ReadColorBitmap(
        const HDC dc,
        const HBITMAP bitmap,
        const std::uint32_t width,
        const std::uint32_t height,
        std::vector<std::uint32_t>& pixels,
        std::uint32_t& lastError) noexcept
    {
        BITMAPINFO info{};
        info.bmiHeader.biSize = sizeof(BITMAPINFOHEADER);
        info.bmiHeader.biWidth = static_cast<LONG>(width);
        info.bmiHeader.biHeight = -static_cast<LONG>(height);
        info.bmiHeader.biPlanes = 1;
        info.bmiHeader.biBitCount = 32;
        info.bmiHeader.biCompression = BI_RGB;
        pixels.assign(
            static_cast<std::size_t>(width) * height,
            0);
        SetLastError(ERROR_SUCCESS);
        const auto lines = GetDIBits(
            dc,
            bitmap,
            0,
            height,
            pixels.data(),
            &info,
            DIB_RGB_COLORS);
        if (lines != static_cast<int>(height))
        {
            lastError = GetLastError();
            return false;
        }
        return true;
    }

    [[nodiscard]] bool ReadMonoBitmap(
        const HDC dc,
        const HBITMAP bitmap,
        const std::uint32_t width,
        const std::uint32_t height,
        std::vector<std::uint8_t>& bits,
        std::uint32_t& stride,
        std::uint32_t& lastError) noexcept
    {
        stride = ((width + 31u) / 32u) * 4u;
        bits.assign(static_cast<std::size_t>(stride) * height, 0);
        MonoBitmapInfo info{};
        info.header.biSize = sizeof(BITMAPINFOHEADER);
        info.header.biWidth = static_cast<LONG>(width);
        info.header.biHeight = -static_cast<LONG>(height);
        info.header.biPlanes = 1;
        info.header.biBitCount = 1;
        info.header.biCompression = BI_RGB;
        info.colors[0] = RGBQUAD{ 0, 0, 0, 0 };
        info.colors[1] = RGBQUAD{ 255, 255, 255, 0 };
        SetLastError(ERROR_SUCCESS);
        const auto lines = GetDIBits(
            dc,
            bitmap,
            0,
            height,
            bits.data(),
            reinterpret_cast<BITMAPINFO*>(&info),
            DIB_RGB_COLORS);
        if (lines != static_cast<int>(height))
        {
            lastError = GetLastError();
            return false;
        }
        return true;
    }

    [[nodiscard]] bool BitAt(
        const std::vector<std::uint8_t>& bits,
        const std::uint32_t stride,
        const std::uint32_t x,
        const std::uint32_t y) noexcept
    {
        const auto value = bits[
            static_cast<std::size_t>(y) * stride + (x / 8u)];
        return (value & (0x80u >> (x % 8u))) != 0;
    }

    [[nodiscard]] std::uint32_t Premultiply(const std::uint32_t pixel) noexcept
    {
        const auto alpha = (pixel >> 24) & 0xffu;
        const auto red = (pixel >> 16) & 0xffu;
        const auto green = (pixel >> 8) & 0xffu;
        const auto blue = pixel & 0xffu;
        const auto multiply = [alpha](const std::uint32_t channel)
        {
            return (channel * alpha + 127u) / 255u;
        };
        return (alpha << 24) |
            (multiply(red) << 16) |
            (multiply(green) << 8) |
            multiply(blue);
    }

}

namespace xbpreview
{
    namespace
    {
        thread_local CursorShapeConversionDiagnostic
            pendingConversionDiagnostic{};

        void PublishConversionDiagnostic(
            const CursorCacheResult& result) noexcept
        {
            CursorShapeConversionDiagnostic diagnostic{};
            diagnostic.cacheHit = result.cacheHit;
            diagnostic.cacheMiss = result.cacheMiss;
            diagnostic.conversionOccurred = result.conversionOccurred;
            diagnostic.conversionSucceeded = result.conversionSucceeded;
            diagnostic.conversionDurationMilliseconds =
                result.conversionDurationMilliseconds;
            diagnostic.conversionResult = result.conversionResult;
            diagnostic.conversionLastError =
                result.conversionLastError;
            CursorShapeConversionDiagnosticChannel::Publish(diagnostic);
        }
    }

    void CursorShapeConversionDiagnosticChannel::Publish(
        const CursorShapeConversionDiagnostic& diagnostic) noexcept
    {
        pendingConversionDiagnostic = diagnostic;
    }

    CursorShapeConversionDiagnostic
        CursorShapeConversionDiagnosticChannel::Consume() noexcept
    {
        const auto diagnostic = pendingConversionDiagnostic;
        pendingConversionDiagnostic = {};
        return diagnostic;
    }

    void CursorShapeConversionDiagnosticChannel::Reset() noexcept
    {
        pendingConversionDiagnostic = {};
    }

    bool CursorShapeConverter::Convert(
        const HCURSOR source,
        CursorShape& shape,
        std::int32_t& result,
        std::uint32_t& lastError) const noexcept
    {
        result = S_OK;
        lastError = ERROR_SUCCESS;
        if (source == nullptr)
        {
            result = E_INVALIDARG;
            return false;
        }

        SetLastError(ERROR_SUCCESS);
        OwnedIcon copied(CopyIcon(source));
        if (copied.Get() == nullptr)
        {
            lastError = GetLastError();
            result = HRESULT_FROM_WIN32(
                lastError != ERROR_SUCCESS ? lastError : ERROR_INVALID_HANDLE);
            return false;
        }

        ICONINFO info{};
        SetLastError(ERROR_SUCCESS);
        if (!GetIconInfo(copied.Get(), &info))
        {
            lastError = GetLastError();
            result = HRESULT_FROM_WIN32(
                lastError != ERROR_SUCCESS ? lastError : ERROR_INVALID_DATA);
            return false;
        }
        OwnedBitmap mask(info.hbmMask);
        OwnedBitmap color(info.hbmColor);

        BITMAP maskDescription{};
        BITMAP colorDescription{};
        if (info.hbmMask == nullptr ||
            GetObjectW(
                info.hbmMask,
                sizeof(maskDescription),
                &maskDescription) != sizeof(maskDescription))
        {
            lastError = GetLastError();
            result = HRESULT_FROM_WIN32(
                lastError != ERROR_SUCCESS ? lastError : ERROR_INVALID_DATA);
            return false;
        }

        const bool hasColor = info.hbmColor != nullptr;
        if (hasColor &&
            GetObjectW(
                info.hbmColor,
                sizeof(colorDescription),
                &colorDescription) != sizeof(colorDescription))
        {
            lastError = GetLastError();
            result = HRESULT_FROM_WIN32(
                lastError != ERROR_SUCCESS ? lastError : ERROR_INVALID_DATA);
            return false;
        }

        const auto width = static_cast<std::uint32_t>(
            hasColor ? colorDescription.bmWidth : maskDescription.bmWidth);
        const auto rawMaskHeight = static_cast<std::uint32_t>(
            (std::max)(1L, std::abs(maskDescription.bmHeight)));
        const auto height = static_cast<std::uint32_t>(
            hasColor
            ? std::abs(colorDescription.bmHeight)
            : rawMaskHeight / 2u);
        if (width == 0 || height == 0 ||
            width > 512 || height > 512 ||
            (!hasColor && rawMaskHeight != height * 2u))
        {
            result = HRESULT_FROM_WIN32(ERROR_INVALID_DATA);
            lastError = ERROR_INVALID_DATA;
            return false;
        }

        OwnedDc dc;
        if (dc.Get() == nullptr)
        {
            lastError = GetLastError();
            result = HRESULT_FROM_WIN32(
                lastError != ERROR_SUCCESS ? lastError : ERROR_NOT_ENOUGH_MEMORY);
            return false;
        }

        std::vector<std::uint8_t> maskBits;
        std::uint32_t maskStride{};
        if (!ReadMonoBitmap(
            dc.Get(),
            info.hbmMask,
            width,
            rawMaskHeight,
            maskBits,
            maskStride,
            lastError))
        {
            result = HRESULT_FROM_WIN32(
                lastError != ERROR_SUCCESS ? lastError : ERROR_INVALID_DATA);
            return false;
        }

        shape.width = width;
        shape.height = height;
        shape.hotspotX = (std::min)(
            static_cast<std::uint32_t>(info.xHotspot),
            width - 1u);
        shape.hotspotY = (std::min)(
            static_cast<std::uint32_t>(info.yHotspot),
            height - 1u);
        shape.premultipliedBgra.assign(
            static_cast<std::size_t>(width) * height,
            0u);

        if (!hasColor)
        {
            shape.kind = XbCursorShapeKind_MonochromeAndXor;
            for (std::uint32_t y = 0; y < height; ++y)
            {
                for (std::uint32_t x = 0; x < width; ++x)
                {
                    const bool andMask = BitAt(maskBits, maskStride, x, y);
                    const bool xorMask = BitAt(
                        maskBits,
                        maskStride,
                        x,
                        y + height);
                    auto& output = shape.premultipliedBgra[
                        static_cast<std::size_t>(y) * width + x];
                    if (andMask && !xorMask)
                    {
                        output = 0u;
                    }
                    else if (!andMask && !xorMask)
                    {
                        output = 0xff000000u;
                    }
                    else
                    {
                        // Alpha blending cannot express destination inversion
                        // without reading/copying the back buffer. P1c uses a
                        // visible high-contrast approximation and counts it.
                        output = 0xffffffffu;
                        if (andMask)
                        {
                            ++shape.xorApproximationPixelCount;
                        }
                    }
                }
            }
            return true;
        }

        std::vector<std::uint32_t> colorPixels;
        if (!ReadColorBitmap(
            dc.Get(),
            info.hbmColor,
            width,
            height,
            colorPixels,
            lastError))
        {
            result = HRESULT_FROM_WIN32(
                lastError != ERROR_SUCCESS ? lastError : ERROR_INVALID_DATA);
            return false;
        }

        const bool hasAlpha = std::any_of(
            colorPixels.begin(),
            colorPixels.end(),
            [](const std::uint32_t pixel)
            {
                return (pixel & 0xff000000u) != 0;
            });
        shape.kind = hasAlpha
            ? XbCursorShapeKind_ColorAlpha
            : XbCursorShapeKind_ColorMask;
        for (std::uint32_t y = 0; y < height; ++y)
        {
            for (std::uint32_t x = 0; x < width; ++x)
            {
                const auto index =
                    static_cast<std::size_t>(y) * width + x;
                if (hasAlpha)
                {
                    shape.premultipliedBgra[index] =
                        Premultiply(colorPixels[index]);
                }
                else if (BitAt(maskBits, maskStride, x, y))
                {
                    shape.premultipliedBgra[index] = 0u;
                }
                else
                {
                    shape.premultipliedBgra[index] =
                        colorPixels[index] | 0xff000000u;
                }
            }
        }
        return true;
    }

    CursorShapeCache::CursorShapeCache()
        : builtInArrow_(CreateBuiltInArrow())
    {
    }

    CursorCacheResult CursorShapeCache::Resolve(
        const HCURSOR cursor) noexcept
    {
        CursorCacheResult result{};
        const auto key = reinterpret_cast<std::uintptr_t>(cursor);
        if (const auto found = byHandle_.find(key); found != byHandle_.end())
        {
            entries_.splice(entries_.begin(), entries_, found->second);
            result.shape = found->second->shape;
            result.cacheHit = true;
            result.usedBuiltInFallback = found->second->builtInFallback;
            PublishConversionDiagnostic(result);
            return result;
        }

        result.cacheMiss = true;
        result.conversionOccurred = true;
        CursorShape converted{};
        converted.id = nextShapeId_++;
        converted.generation = nextGeneration_++;
        const auto conversionStarted = std::chrono::steady_clock::now();
        result.conversionSucceeded = converter_.Convert(
            cursor,
            converted,
            result.conversionResult,
            result.conversionLastError);
        result.conversionDurationMilliseconds =
            std::chrono::duration<double, std::milli>(
                std::chrono::steady_clock::now() -
                conversionStarted).count();
        if (!result.conversionSucceeded)
        {
            result.shape = builtInArrow_;
            result.conversionFailed = true;
            result.usedBuiltInFallback = true;
            entries_.push_front(Entry{ key, builtInArrow_, true });
            byHandle_[key] = entries_.begin();
            if (entries_.size() > MaximumEntries)
            {
                const auto last = std::prev(entries_.end());
                byHandle_.erase(last->sourceHandle);
                entries_.erase(last);
            }
            PublishConversionDiagnostic(result);
            return result;
        }

        auto shape = std::make_shared<const CursorShape>(std::move(converted));
        entries_.push_front(Entry{ key, shape, false });
        byHandle_[key] = entries_.begin();
        if (entries_.size() > MaximumEntries)
        {
            const auto last = std::prev(entries_.end());
            byHandle_.erase(last->sourceHandle);
            entries_.erase(last);
        }
        result.shape = std::move(shape);
        PublishConversionDiagnostic(result);
        return result;
    }

    void CursorShapeCache::Clear() noexcept
    {
        byHandle_.clear();
        entries_.clear();
        nextShapeId_ = 2;
        nextGeneration_ = 1;
        CursorShapeConversionDiagnosticChannel::Reset();
    }

    std::shared_ptr<const CursorShape> CursorShapeCache::CreateBuiltInArrow()
    {
        auto shape = std::make_shared<CursorShape>();
        shape->id = 1;
        shape->generation = 1;
        shape->width = 24;
        shape->height = 24;
        shape->hotspotX = 0;
        shape->hotspotY = 0;
        shape->kind = XbCursorShapeKind_BuiltInFallbackArrow;
        shape->premultipliedBgra.assign(24u * 24u, 0u);

        for (std::uint32_t y = 0; y < 20; ++y)
        {
            const auto maxX = (std::min)(y / 2u + 1u, 9u);
            for (std::uint32_t x = 0; x <= maxX; ++x)
            {
                const bool outline =
                    x == 0 || x == maxX || y == 0 || y == 19;
                shape->premultipliedBgra[
                    static_cast<std::size_t>(y) * 24u + x] =
                    outline ? 0xff000000u : 0xffffffffu;
            }
        }
        for (std::uint32_t y = 12; y < 23; ++y)
        {
            for (std::uint32_t x = 7; x < 11; ++x)
            {
                shape->premultipliedBgra[
                    static_cast<std::size_t>(y) * 24u + x] =
                    (x == 7 || x == 10 || y == 22)
                    ? 0xff000000u
                    : 0xffffffffu;
            }
        }
        return shape;
    }
}
