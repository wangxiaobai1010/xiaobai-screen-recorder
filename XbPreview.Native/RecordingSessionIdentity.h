#pragma once

#include <windows.h>

#include <cstddef>
#include <string>
#include <string_view>

namespace xbpreview
{
    inline constexpr std::size_t CanonicalRecordingSessionIdLength = 36;

    // The writer form is the uppercase 8-4-4-4-12 GUID text without braces.
    // An empty result means the GUID could not be formatted.
    [[nodiscard]] std::wstring FormatCanonicalRecordingSessionId(
        const GUID& value);

    // Readers accept canonical text plus braced/case variants, then normalize
    // to the one writer form above. No whitespace or alternate GUID syntax is
    // accepted.
    [[nodiscard]] HRESULT NormalizeRecordingSessionId(
        std::wstring_view value,
        std::wstring& canonical) noexcept;

    [[nodiscard]] bool RecordingSessionIdsEqual(
        std::wstring_view left,
        std::wstring_view right) noexcept;

    [[nodiscard]] bool IsCanonicalRecordingSessionId(
        std::wstring_view value) noexcept;
}
