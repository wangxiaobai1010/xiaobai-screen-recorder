#pragma once

#include <windows.h>

#include <array>
#include <cstdint>
#include <filesystem>
#include <optional>
#include <string>
#include <string_view>

namespace xbpreview
{
    struct PersistentFileIdentity final
    {
        bool available{};
        std::uint64_t volumeSerialNumber{};
        std::array<std::uint8_t, 16> fileId{};
        std::uint32_t hardLinkCount{};
        std::optional<std::uint64_t> fileSizeBytes;
    };

    struct PersistentFileIdentityCapture final
    {
        HRESULT hresult{ E_UNEXPECTED };
        PersistentFileIdentity identity;

        [[nodiscard]] bool Succeeded() const noexcept
        {
            return SUCCEEDED(hresult) && identity.available;
        }
    };

    [[nodiscard]] HRESULT ReadPersistentFileIdentity(
        HANDLE file,
        PersistentFileIdentity& identity) noexcept;

    [[nodiscard]] PersistentFileIdentityCapture CapturePersistentFileIdentity(
        const std::filesystem::path& path) noexcept;

    [[nodiscard]] bool SamePersistentFileIdentity(
        const PersistentFileIdentity& left,
        const PersistentFileIdentity& right) noexcept;

    [[nodiscard]] std::wstring FormatVolumeIdentityCanonical(
        std::uint64_t value);
    [[nodiscard]] std::wstring FormatFileIdCanonical(
        const std::array<std::uint8_t, 16>& value);

    [[nodiscard]] bool ParseVolumeIdentityCanonical(
        std::wstring_view text,
        std::uint64_t& value) noexcept;
    [[nodiscard]] bool ParseFileIdCanonical(
        std::wstring_view text,
        std::array<std::uint8_t, 16>& value) noexcept;
}
