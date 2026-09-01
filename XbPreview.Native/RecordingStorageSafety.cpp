#include "RecordingStorageSafety.h"

#include <filesystem>
#include <iterator>
#include <limits>
#include <system_error>

namespace xbpreview
{
    namespace
    {
        constexpr std::uint64_t MiB = 1024ull * 1024ull;

        std::uint64_t BytesForSeconds(
            const std::uint32_t bitrate,
            const std::uint64_t seconds) noexcept
        {
            const auto bytesPerSecond =
                (static_cast<std::uint64_t>(bitrate) + 7ull) / 8ull;
            if (bytesPerSecond >
                (std::numeric_limits<std::uint64_t>::max)() / seconds)
            {
                return (std::numeric_limits<std::uint64_t>::max)();
            }
            return bytesPerSecond * seconds;
        }

        RecordingStorageStatus ClassifyIoFailure(
            const DWORD error) noexcept
        {
            switch (error)
            {
            case ERROR_ACCESS_DENIED:
            case ERROR_WRITE_PROTECT:
            case ERROR_PRIVILEGE_NOT_HELD:
                return RecordingStorageStatus::DestinationNotWritable;
            case ERROR_FILE_NOT_FOUND:
            case ERROR_PATH_NOT_FOUND:
            case ERROR_NOT_READY:
            case ERROR_DEVICE_NOT_CONNECTED:
            case ERROR_BAD_NETPATH:
                return RecordingStorageStatus::DestinationUnavailable;
            default:
                return RecordingStorageStatus::QueryFailure;
            }
        }

        RecordingStorageFacts QueryFacts(
            const std::wstring& outputDirectory,
            const std::uint32_t bitrate) noexcept
        {
            RecordingStorageFacts result{};
            result.thresholds = ComputeRecordingStorageThresholds(bitrate);
            try
            {
                const std::filesystem::path directory(outputDirectory);
                if (outputDirectory.empty() || !directory.is_absolute())
                {
                    result.status = RecordingStorageStatus::InvalidDestination;
                    result.hresult = E_INVALIDARG;
                    return result;
                }

                wchar_t volume[MAX_PATH]{};
                if (!GetVolumePathNameW(
                        directory.c_str(), volume,
                        static_cast<DWORD>(std::size(volume))))
                {
                    const auto error = GetLastError();
                    result.status = ClassifyIoFailure(error);
                    result.hresult = HRESULT_FROM_WIN32(error);
                    return result;
                }
                result.volumeRoot = volume;
                ULARGE_INTEGER available{};
                if (!GetDiskFreeSpaceExW(
                        volume, &available, nullptr, nullptr))
                {
                    const auto error = GetLastError();
                    result.status = ClassifyIoFailure(error);
                    result.hresult = HRESULT_FROM_WIN32(error);
                    return result;
                }
                result.freeBytesAvailable = available.QuadPart;
                result.hresult = S_OK;
                result.status = EvaluateRecordingStorageSpace(
                    available.QuadPart, bitrate, false);
                return result;
            }
            catch (const std::bad_alloc&)
            {
                result.status = RecordingStorageStatus::QueryFailure;
                result.hresult = E_OUTOFMEMORY;
            }
            catch (...)
            {
                result.status = RecordingStorageStatus::QueryFailure;
                result.hresult = E_UNEXPECTED;
            }
            return result;
        }
    }

    RecordingStorageThresholds ComputeRecordingStorageThresholds(
        const std::uint32_t bitrate) noexcept
    {
        RecordingStorageThresholds result{};
        result.fixedMarginBytes = 128ull * MiB;
        const auto criticalPayload = BytesForSeconds(bitrate, 30);
        const auto warningPayload = BytesForSeconds(bitrate, 120);
        const auto startupPayload = BytesForSeconds(bitrate, 300);
        result.criticalBytes = result.fixedMarginBytes + criticalPayload;
        result.warningBytes = result.fixedMarginBytes + warningPayload;
        result.startupBytes = result.fixedMarginBytes + startupPayload;
        return result;
    }

    RecordingStorageStatus EvaluateRecordingStorageSpace(
        const std::uint64_t freeBytesAvailable,
        const std::uint32_t bitrate,
        const bool starting) noexcept
    {
        const auto thresholds = ComputeRecordingStorageThresholds(bitrate);
        if (starting && freeBytesAvailable < thresholds.startupBytes)
        {
            return RecordingStorageStatus::CriticalSpace;
        }
        if (freeBytesAvailable <= thresholds.criticalBytes)
        {
            return RecordingStorageStatus::CriticalSpace;
        }
        if (freeBytesAvailable <= thresholds.warningBytes)
        {
            return RecordingStorageStatus::LowSpaceWarning;
        }
        return RecordingStorageStatus::Ready;
    }

    RecordingStorageFacts QueryRecordingStorageRuntime(
        const std::wstring& outputDirectory,
        const std::uint32_t bitrate) noexcept
    {
        return QueryFacts(outputDirectory, bitrate);
    }

    RecordingStorageFacts ProbeRecordingStorageForStart(
        const std::wstring& outputDirectory,
        const std::uint32_t bitrate) noexcept
    {
        RecordingStorageFacts result{};
        result.thresholds = ComputeRecordingStorageThresholds(bitrate);
        try
        {
            const std::filesystem::path directory(outputDirectory);
            if (outputDirectory.empty() || !directory.is_absolute())
            {
                result.status = RecordingStorageStatus::InvalidDestination;
                result.hresult = E_INVALIDARG;
                return result;
            }
            std::error_code error;
            std::filesystem::create_directories(directory, error);
            if (error)
            {
                result.status = ClassifyIoFailure(
                    static_cast<DWORD>(error.value()));
                result.hresult = HRESULT_FROM_WIN32(error.value());
                return result;
            }

            wchar_t probeName[MAX_PATH]{};
            for (std::uint32_t attempt = 0; attempt < 8; ++attempt)
            {
                const auto leaf = L".xb-storage-probe-" +
                    std::to_wstring(GetCurrentProcessId()) + L"-" +
                    std::to_wstring(GetCurrentThreadId()) + L"-" +
                    std::to_wstring(GetTickCount64()) + L"-" +
                    std::to_wstring(attempt) + L".tmp";
                const auto path = directory / leaf;
                wcsncpy_s(probeName, path.c_str(), _TRUNCATE);
                const auto file = CreateFileW(
                    probeName, GENERIC_WRITE, 0, nullptr, CREATE_NEW,
                    FILE_ATTRIBUTE_TEMPORARY | FILE_FLAG_DELETE_ON_CLOSE,
                    nullptr);
                if (file == INVALID_HANDLE_VALUE)
                {
                    const auto win32 = GetLastError();
                    if (win32 == ERROR_FILE_EXISTS ||
                        win32 == ERROR_ALREADY_EXISTS)
                    {
                        continue;
                    }
                    result.status = ClassifyIoFailure(win32);
                    result.hresult = HRESULT_FROM_WIN32(win32);
                    return result;
                }
                const BYTE marker = 0x58;
                DWORD written{};
                const auto wrote = WriteFile(
                    file, &marker, sizeof(marker), &written, nullptr);
                const auto writeError = wrote ? ERROR_SUCCESS : GetLastError();
                const auto flushed = wrote && written == sizeof(marker) &&
                    FlushFileBuffers(file);
                const auto flushError = flushed ? ERROR_SUCCESS : GetLastError();
                const auto closed = CloseHandle(file);
                if (!wrote || written != sizeof(marker) || !flushed || !closed)
                {
                    const auto failure = writeError != ERROR_SUCCESS
                        ? writeError
                        : flushError != ERROR_SUCCESS
                            ? flushError
                            : GetLastError();
                    result.status = ClassifyIoFailure(failure);
                    result.hresult = HRESULT_FROM_WIN32(failure);
                    return result;
                }
                result = QueryFacts(outputDirectory, bitrate);
                if (SUCCEEDED(result.hresult))
                {
                    result.status = EvaluateRecordingStorageSpace(
                        result.freeBytesAvailable, bitrate, true);
                    if (result.status == RecordingStorageStatus::CriticalSpace)
                    {
                        result.hresult = HRESULT_FROM_WIN32(ERROR_DISK_FULL);
                    }
                }
                return result;
            }
            result.status = RecordingStorageStatus::DestinationNotWritable;
            result.hresult = HRESULT_FROM_WIN32(ERROR_FILE_EXISTS);
        }
        catch (const std::bad_alloc&)
        {
            result.status = RecordingStorageStatus::QueryFailure;
            result.hresult = E_OUTOFMEMORY;
        }
        catch (...)
        {
            result.status = RecordingStorageStatus::QueryFailure;
            result.hresult = E_UNEXPECTED;
        }
        return result;
    }

    bool IsStorageFailureHResult(const HRESULT value) noexcept
    {
        const auto code = HRESULT_CODE(value);
        return value == STG_E_MEDIUMFULL ||
            code == ERROR_DISK_FULL || code == ERROR_HANDLE_DISK_FULL ||
            code == ERROR_ACCESS_DENIED || code == ERROR_WRITE_PROTECT ||
            code == ERROR_PATH_NOT_FOUND || code == ERROR_FILE_NOT_FOUND ||
            code == ERROR_NOT_READY || code == ERROR_DEVICE_NOT_CONNECTED ||
            code == ERROR_IO_DEVICE;
    }

    const wchar_t* RecordingStorageUserMessage(
        const RecordingStorageStatus status) noexcept
    {
        switch (status)
        {
        case RecordingStorageStatus::LowSpaceWarning:
            return L"Recording storage is running low. Recording is still active.";
        case RecordingStorageStatus::CriticalSpace:
            return L"Recording storage is critically low. Stopping safely to preserve the video.";
        case RecordingStorageStatus::InvalidDestination:
            return L"The recording destination is invalid.";
        case RecordingStorageStatus::DestinationUnavailable:
            return L"The recording destination is unavailable.";
        case RecordingStorageStatus::DestinationNotWritable:
            return L"The recording destination is not writable.";
        case RecordingStorageStatus::QueryFailure:
            return L"The recording destination could not be verified safely.";
        default:
            return L"";
        }
    }
}
