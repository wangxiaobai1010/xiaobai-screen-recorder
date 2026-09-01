#pragma once

#include <windows.h>

#include <filesystem>

namespace xbpreview
{
    enum class RecordingOutputRootStatus
    {
        Resolved,
        InvalidInput,
        Failure
    };

    struct RecordingOutputRootResolution final
    {
        RecordingOutputRootStatus status{
            RecordingOutputRootStatus::InvalidInput };
        HRESULT hresult{ E_INVALIDARG };
        std::filesystem::path mediaOutputRoot;
        std::filesystem::path sessionsRoot;

        [[nodiscard]] bool Succeeded() const noexcept
        {
            return status == RecordingOutputRootStatus::Resolved &&
                SUCCEEDED(hresult);
        }
    };

    // Preserves the existing artifacts/bin/<Configuration>/x64 layout rule.
    [[nodiscard]] std::filesystem::path ResolveArtifactsRoot(
        const std::filesystem::path& diagnosticDirectory);

    // Shared contract for the formal recording writer and future inspection.
    [[nodiscard]] RecordingOutputRootResolution ResolveRecordingOutputRoots(
        const std::filesystem::path& diagnosticDirectory) noexcept;

    // The current fixed product contract treats managedOutputRoot as the media
    // output root. The sessions root is its single "sessions" child.
    [[nodiscard]] RecordingOutputRootResolution
        ResolveRecordingOutputRootsFromManagedRoot(
            const std::filesystem::path& managedOutputRoot) noexcept;
}
