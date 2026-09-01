#pragma once

#include "RecordingOutputRoot.h"

#include <windows.h>

#include <filesystem>
#include <string_view>

namespace xbpreview
{
    inline constexpr wchar_t SessionLifetimeOwnerFileName[] =
        L"session.owner.lock";

    enum class SessionLifetimeOwnerAcquireStatus
    {
        Acquired,
        AlreadyOwned,
        EvidenceMissing,
        UnsafePath,
        Inaccessible,
        IoFailure,
        InvalidInput,
        Unavailable
    };

    struct SessionLifetimeOwnerAcquireResult final
    {
        SessionLifetimeOwnerAcquireStatus status{
            SessionLifetimeOwnerAcquireStatus::Unavailable };
        HRESULT diagnosticHResult{ E_UNEXPECTED };
        std::filesystem::path ownerPath;

        [[nodiscard]] bool Acquired() const noexcept
        {
            return status == SessionLifetimeOwnerAcquireStatus::Acquired &&
                SUCCEEDED(diagnosticHResult);
        }
    };

    enum class SessionLifetimeOwnerProbeState
    {
        ActiveOwned,
        InactiveLeaseReleased,
        EvidenceMissing,
        UnsafePath,
        Inaccessible,
        IoFailure,
        Unknown
    };

    struct SessionLifetimeOwnerProbeResult final
    {
        SessionLifetimeOwnerProbeState state{
            SessionLifetimeOwnerProbeState::Unknown };
        HRESULT diagnosticHResult{ E_UNEXPECTED };
        std::filesystem::path ownerPath;
    };

    // The persistent owner file is only a deterministic rendezvous point.
    // Live ownership is established solely by this non-inheritable kernel
    // handle being held with share mode zero.
    class SessionLifetimeOwner final
    {
    public:
        SessionLifetimeOwner() noexcept = default;
        ~SessionLifetimeOwner();
        SessionLifetimeOwner(const SessionLifetimeOwner&) = delete;
        SessionLifetimeOwner& operator=(const SessionLifetimeOwner&) = delete;
        SessionLifetimeOwner(SessionLifetimeOwner&& other) noexcept;
        SessionLifetimeOwner& operator=(SessionLifetimeOwner&& other) noexcept;

        [[nodiscard]] SessionLifetimeOwnerAcquireResult Acquire(
            const RecordingOutputRootResolution& roots,
            std::wstring_view canonicalSessionId) noexcept;

        // Maintenance never creates owner evidence. A successful call holds
        // the same exclusive kernel lease used by the recording owner until
        // Release/destruction.
        [[nodiscard]] SessionLifetimeOwnerAcquireResult AcquireExisting(
            const RecordingOutputRootResolution& roots,
            std::wstring_view canonicalSessionId) noexcept;
        void Release() noexcept;

        [[nodiscard]] bool Acquired() const noexcept;
        [[nodiscard]] bool HandleIsInheritable() const noexcept;
        [[nodiscard]] const std::filesystem::path& OwnerPath() const noexcept;

    private:
        [[nodiscard]] SessionLifetimeOwnerAcquireResult AcquireImpl(
            const RecordingOutputRootResolution& roots,
            std::wstring_view canonicalSessionId,
            bool createIfMissing) noexcept;

        HANDLE handle_{ INVALID_HANDLE_VALUE };
        HANDLE sessionDirectoryHandle_{ INVALID_HANDLE_VALUE };
        std::filesystem::path ownerPath_;
    };

    // Used by both the probe and deterministic tests. AccessDenied is never
    // treated as proof that the lease is released.
    [[nodiscard]] SessionLifetimeOwnerProbeState
        ClassifySessionLifetimeOwnerProbeOpenFailure(DWORD error) noexcept;

    // Strictly read-only. This never creates, deletes, truncates, or writes the
    // owner file and never mutates a Manifest.
    [[nodiscard]] SessionLifetimeOwnerProbeResult ProbeSessionLifetimeOwner(
        const RecordingOutputRootResolution& roots,
        std::wstring_view canonicalSessionId) noexcept;
}
