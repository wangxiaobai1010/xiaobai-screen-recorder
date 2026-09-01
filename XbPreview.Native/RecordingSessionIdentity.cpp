#include "RecordingSessionIdentity.h"

#include <objbase.h>

#include <algorithm>
#include <array>
#include <cwctype>
#include <new>

namespace xbpreview
{
    namespace
    {
        constexpr std::size_t BracedSessionIdLength = 38;

        bool IsHexDigit(const wchar_t value) noexcept
        {
            return (value >= L'0' && value <= L'9') ||
                (value >= L'a' && value <= L'f') ||
                (value >= L'A' && value <= L'F');
        }

        bool HasCanonicalGuidShape(
            const std::wstring_view value,
            const std::size_t offset) noexcept
        {
            if (value.size() < offset + CanonicalRecordingSessionIdLength)
            {
                return false;
            }
            for (std::size_t index = 0;
                index < CanonicalRecordingSessionIdLength;
                ++index)
            {
                const auto character = value[offset + index];
                const auto hyphen = index == 8 || index == 13 ||
                    index == 18 || index == 23;
                if ((hyphen && character != L'-') ||
                    (!hyphen && !IsHexDigit(character)))
                {
                    return false;
                }
            }
            return true;
        }

        bool TryParseSessionGuid(
            const std::wstring_view value,
            GUID& guid) noexcept
        {
            std::array<wchar_t, BracedSessionIdLength + 1> braced{};
            if (value.size() == CanonicalRecordingSessionIdLength)
            {
                if (!HasCanonicalGuidShape(value, 0))
                {
                    return false;
                }
                braced[0] = L'{';
                std::copy(value.begin(), value.end(), braced.begin() + 1);
                braced[BracedSessionIdLength - 1] = L'}';
            }
            else if (value.size() == BracedSessionIdLength &&
                value.front() == L'{' && value.back() == L'}')
            {
                if (!HasCanonicalGuidShape(value, 1))
                {
                    return false;
                }
                std::copy(value.begin(), value.end(), braced.begin());
            }
            else
            {
                return false;
            }
            return SUCCEEDED(CLSIDFromString(braced.data(), &guid));
        }
    }

    std::wstring FormatCanonicalRecordingSessionId(const GUID& value)
    {
        std::array<wchar_t, BracedSessionIdLength + 2> buffer{};
        const auto length = StringFromGUID2(
            value,
            buffer.data(),
            static_cast<int>(buffer.size()));
        if (length != static_cast<int>(BracedSessionIdLength + 1) ||
            buffer.front() != L'{' ||
            buffer[BracedSessionIdLength - 1] != L'}')
        {
            return {};
        }

        std::wstring result(
            buffer.data() + 1,
            CanonicalRecordingSessionIdLength);
        std::transform(
            result.begin(),
            result.end(),
            result.begin(),
            [](const wchar_t character)
            {
                return static_cast<wchar_t>(towupper(character));
            });
        return result;
    }

    HRESULT NormalizeRecordingSessionId(
        const std::wstring_view value,
        std::wstring& canonical) noexcept
    {
        try
        {
            GUID guid{};
            if (!TryParseSessionGuid(value, guid))
            {
                canonical.clear();
                return HRESULT_FROM_WIN32(ERROR_INVALID_DATA);
            }
            auto formatted = FormatCanonicalRecordingSessionId(guid);
            if (formatted.size() != CanonicalRecordingSessionIdLength)
            {
                canonical.clear();
                return HRESULT_FROM_WIN32(ERROR_INVALID_DATA);
            }
            canonical = std::move(formatted);
            return S_OK;
        }
        catch (const std::bad_alloc&)
        {
            canonical.clear();
            return E_OUTOFMEMORY;
        }
        catch (...)
        {
            canonical.clear();
            return E_UNEXPECTED;
        }
    }

    bool RecordingSessionIdsEqual(
        const std::wstring_view left,
        const std::wstring_view right) noexcept
    {
        std::wstring canonicalLeft;
        std::wstring canonicalRight;
        return SUCCEEDED(NormalizeRecordingSessionId(left, canonicalLeft)) &&
            SUCCEEDED(NormalizeRecordingSessionId(right, canonicalRight)) &&
            canonicalLeft == canonicalRight;
    }

    bool IsCanonicalRecordingSessionId(
        const std::wstring_view value) noexcept
    {
        std::wstring canonical;
        return SUCCEEDED(NormalizeRecordingSessionId(value, canonical)) &&
            value == canonical;
    }
}
