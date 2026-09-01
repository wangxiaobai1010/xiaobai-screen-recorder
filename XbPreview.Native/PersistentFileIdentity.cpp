#include "PersistentFileIdentity.h"

#include <algorithm>
#include <utility>

namespace xbpreview
{
    namespace
    {
        constexpr DWORD SharedReadWriteDelete =
            FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE;

        class UniqueHandle final
        {
        public:
            explicit UniqueHandle(const HANDLE value) noexcept : value_(value) {}
            ~UniqueHandle()
            {
                if (value_ != nullptr && value_ != INVALID_HANDLE_VALUE)
                {
                    (void)CloseHandle(value_);
                }
            }
            UniqueHandle(const UniqueHandle&) = delete;
            UniqueHandle& operator=(const UniqueHandle&) = delete;
            [[nodiscard]] HANDLE Get() const noexcept { return value_; }
            [[nodiscard]] bool Valid() const noexcept
            {
                return value_ != nullptr && value_ != INVALID_HANDLE_VALUE;
            }

        private:
            HANDLE value_{ INVALID_HANDLE_VALUE };
        };

        constexpr wchar_t HexDigit(const std::uint8_t value) noexcept
        {
            return value < 10
                ? static_cast<wchar_t>(L'0' + value)
                : static_cast<wchar_t>(L'A' + value - 10);
        }

        bool HexValue(const wchar_t value, std::uint8_t& output) noexcept
        {
            if (value >= L'0' && value <= L'9')
            {
                output = static_cast<std::uint8_t>(value - L'0');
                return true;
            }
            if (value >= L'A' && value <= L'F')
            {
                output = static_cast<std::uint8_t>(value - L'A' + 10);
                return true;
            }
            return false;
        }
    }

    HRESULT ReadPersistentFileIdentity(
        const HANDLE file,
        PersistentFileIdentity& identity) noexcept
    {
        identity = {};
        if (file == nullptr || file == INVALID_HANDLE_VALUE)
        {
            return E_INVALIDARG;
        }

        FILE_ID_INFO fileIdInfo{};
        if (!GetFileInformationByHandleEx(
                file, FileIdInfo, &fileIdInfo, sizeof(fileIdInfo)))
        {
            return HRESULT_FROM_WIN32(GetLastError());
        }

        BY_HANDLE_FILE_INFORMATION information{};
        if (!GetFileInformationByHandle(file, &information))
        {
            return HRESULT_FROM_WIN32(GetLastError());
        }

        identity.available = true;
        identity.volumeSerialNumber = fileIdInfo.VolumeSerialNumber;
        std::copy(
            std::begin(fileIdInfo.FileId.Identifier),
            std::end(fileIdInfo.FileId.Identifier),
            identity.fileId.begin());
        identity.hardLinkCount = information.nNumberOfLinks;
        if ((information.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) == 0)
        {
            identity.fileSizeBytes =
                (static_cast<std::uint64_t>(information.nFileSizeHigh) << 32) |
                information.nFileSizeLow;
        }
        return S_OK;
    }

    PersistentFileIdentityCapture CapturePersistentFileIdentity(
        const std::filesystem::path& path) noexcept
    {
        PersistentFileIdentityCapture result{};
        if (path.empty() || !path.is_absolute())
        {
            result.hresult = E_INVALIDARG;
            return result;
        }
        const UniqueHandle file(CreateFileW(
            path.c_str(),
            FILE_READ_ATTRIBUTES,
            SharedReadWriteDelete,
            nullptr,
            OPEN_EXISTING,
            FILE_FLAG_BACKUP_SEMANTICS,
            nullptr));
        if (!file.Valid())
        {
            result.hresult = HRESULT_FROM_WIN32(GetLastError());
            return result;
        }

        BY_HANDLE_FILE_INFORMATION information{};
        if (!GetFileInformationByHandle(file.Get(), &information))
        {
            result.hresult = HRESULT_FROM_WIN32(GetLastError());
            return result;
        }
        if ((information.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) != 0)
        {
            result.hresult = HRESULT_FROM_WIN32(ERROR_FILE_INVALID);
            return result;
        }
        SetLastError(NO_ERROR);
        const auto type = GetFileType(file.Get());
        if (type != FILE_TYPE_DISK)
        {
            result.hresult = type == FILE_TYPE_UNKNOWN && GetLastError() != NO_ERROR
                ? HRESULT_FROM_WIN32(GetLastError())
                : HRESULT_FROM_WIN32(ERROR_FILE_INVALID);
            return result;
        }
        result.hresult = ReadPersistentFileIdentity(file.Get(), result.identity);
        return result;
    }

    bool SamePersistentFileIdentity(
        const PersistentFileIdentity& left,
        const PersistentFileIdentity& right) noexcept
    {
        return left.available && right.available &&
            left.volumeSerialNumber == right.volumeSerialNumber &&
            left.fileId == right.fileId;
    }

    std::wstring FormatVolumeIdentityCanonical(const std::uint64_t value)
    {
        std::wstring result(16, L'0');
        for (std::size_t index = 0; index < result.size(); ++index)
        {
            const auto shift = static_cast<unsigned>((15 - index) * 4);
            result[index] = HexDigit(static_cast<std::uint8_t>(
                (value >> shift) & 0x0f));
        }
        return result;
    }

    std::wstring FormatFileIdCanonical(
        const std::array<std::uint8_t, 16>& value)
    {
        std::wstring result(32, L'0');
        for (std::size_t index = 0; index < value.size(); ++index)
        {
            result[index * 2] = HexDigit(
                static_cast<std::uint8_t>(value[index] >> 4));
            result[index * 2 + 1] = HexDigit(
                static_cast<std::uint8_t>(value[index] & 0x0f));
        }
        return result;
    }

    bool ParseVolumeIdentityCanonical(
        const std::wstring_view text,
        std::uint64_t& value) noexcept
    {
        if (text.size() != 16)
        {
            return false;
        }
        std::uint64_t candidate{};
        for (const auto character : text)
        {
            std::uint8_t nibble{};
            if (!HexValue(character, nibble))
            {
                return false;
            }
            candidate = (candidate << 4) | nibble;
        }
        value = candidate;
        return true;
    }

    bool ParseFileIdCanonical(
        const std::wstring_view text,
        std::array<std::uint8_t, 16>& value) noexcept
    {
        if (text.size() != 32)
        {
            return false;
        }
        std::array<std::uint8_t, 16> candidate{};
        for (std::size_t index = 0; index < candidate.size(); ++index)
        {
            std::uint8_t high{};
            std::uint8_t low{};
            if (!HexValue(text[index * 2], high) ||
                !HexValue(text[index * 2 + 1], low))
            {
                return false;
            }
            candidate[index] = static_cast<std::uint8_t>((high << 4) | low);
        }
        value = candidate;
        return true;
    }
}
