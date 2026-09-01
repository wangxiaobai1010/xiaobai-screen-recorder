#pragma once

#include <cstdint>
#include <string>

#include <windows.h>

namespace xbpreview
{
    enum class RecordingStorageStatus : std::uint32_t
    {
        Ready,
        LowSpaceWarning,
        CriticalSpace,
        InvalidDestination,
        DestinationUnavailable,
        DestinationNotWritable,
        QueryFailure
    };

    struct RecordingStorageThresholds final
    {
        std::uint64_t fixedMarginBytes{};
        std::uint64_t criticalBytes{};
        std::uint64_t warningBytes{};
        std::uint64_t startupBytes{};
    };

    struct RecordingStorageFacts final
    {
        RecordingStorageStatus status{ RecordingStorageStatus::QueryFailure };
        HRESULT hresult{ E_FAIL };
        std::uint64_t freeBytesAvailable{};
        RecordingStorageThresholds thresholds{};
        std::wstring volumeRoot;

        [[nodiscard]] bool CanStart() const noexcept
        {
            return status == RecordingStorageStatus::Ready;
        }
    };

    [[nodiscard]] RecordingStorageThresholds ComputeRecordingStorageThresholds(
        std::uint32_t bitrate) noexcept;
    [[nodiscard]] RecordingStorageStatus EvaluateRecordingStorageSpace(
        std::uint64_t freeBytesAvailable,
        std::uint32_t bitrate,
        bool starting) noexcept;
    [[nodiscard]] RecordingStorageFacts ProbeRecordingStorageForStart(
        const std::wstring& outputDirectory,
        std::uint32_t bitrate) noexcept;
    [[nodiscard]] RecordingStorageFacts QueryRecordingStorageRuntime(
        const std::wstring& outputDirectory,
        std::uint32_t bitrate) noexcept;
    [[nodiscard]] bool IsStorageFailureHResult(HRESULT value) noexcept;
    [[nodiscard]] const wchar_t* RecordingStorageUserMessage(
        RecordingStorageStatus status) noexcept;
}
