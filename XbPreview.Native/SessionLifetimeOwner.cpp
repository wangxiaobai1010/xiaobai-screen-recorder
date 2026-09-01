#include "SessionLifetimeOwner.h"

#include "RecordingSessionIdentity.h"
#include "PersistentFileIdentity.h"
#include "SessionPathSafety.h"

#include <new>
#include <utility>

namespace xbpreview
{
    namespace
    {
        HRESULT LastErrorHResult() noexcept
        {
            const auto error = GetLastError();
            return HRESULT_FROM_WIN32(error == ERROR_SUCCESS
                ? ERROR_GEN_FAILURE
                : error);
        }

        bool ValidHandle(const HANDLE value) noexcept
        {
            return value != nullptr && value != INVALID_HANDLE_VALUE;
        }

        std::filesystem::path BuildOwnerPath(
            const RecordingOutputRootResolution& roots,
            const std::wstring_view sessionId)
        {
            return roots.sessionsRoot / std::wstring(sessionId) /
                SessionLifetimeOwnerFileName;
        }

        HRESULT ValidateOwnerFileHandle(const HANDLE handle) noexcept
        {
            FILE_ATTRIBUTE_TAG_INFO attributes{};
            if (!GetFileInformationByHandleEx(
                    handle,
                    FileAttributeTagInfo,
                    &attributes,
                    sizeof(attributes)))
            {
                return LastErrorHResult();
            }
            if ((attributes.FileAttributes & FILE_ATTRIBUTE_DIRECTORY) != 0 ||
                (attributes.FileAttributes & FILE_ATTRIBUTE_REPARSE_POINT) != 0)
            {
                return HRESULT_FROM_WIN32(ERROR_REPARSE_TAG_INVALID);
            }
            return S_OK;
        }

        SessionLifetimeOwnerProbeResult ProbeResult(
            const SessionLifetimeOwnerProbeState state,
            const HRESULT result,
            std::filesystem::path path = {}) noexcept
        {
            SessionLifetimeOwnerProbeResult value{};
            value.state = state;
            value.diagnosticHResult = result;
            try
            {
                value.ownerPath = std::move(path);
            }
            catch (...)
            {
                value.state = SessionLifetimeOwnerProbeState::IoFailure;
                value.diagnosticHResult = E_OUTOFMEMORY;
            }
            return value;
        }
    }

    SessionLifetimeOwner::~SessionLifetimeOwner()
    {
        Release();
    }

    SessionLifetimeOwner::SessionLifetimeOwner(
        SessionLifetimeOwner&& other) noexcept
        : handle_(std::exchange(other.handle_, INVALID_HANDLE_VALUE)),
          sessionDirectoryHandle_(std::exchange(
              other.sessionDirectoryHandle_, INVALID_HANDLE_VALUE)),
          ownerPath_(std::move(other.ownerPath_))
    {
    }

    SessionLifetimeOwner& SessionLifetimeOwner::operator=(
        SessionLifetimeOwner&& other) noexcept
    {
        if (this != &other)
        {
            Release();
            handle_ = std::exchange(other.handle_, INVALID_HANDLE_VALUE);
            sessionDirectoryHandle_ = std::exchange(
                other.sessionDirectoryHandle_, INVALID_HANDLE_VALUE);
            ownerPath_ = std::move(other.ownerPath_);
        }
        return *this;
    }

    SessionLifetimeOwnerAcquireResult SessionLifetimeOwner::Acquire(
        const RecordingOutputRootResolution& roots,
        const std::wstring_view canonicalSessionId) noexcept
    {
        return AcquireImpl(roots, canonicalSessionId, true);
    }

    SessionLifetimeOwnerAcquireResult SessionLifetimeOwner::AcquireExisting(
        const RecordingOutputRootResolution& roots,
        const std::wstring_view canonicalSessionId) noexcept
    {
        return AcquireImpl(roots, canonicalSessionId, false);
    }

    SessionLifetimeOwnerAcquireResult SessionLifetimeOwner::AcquireImpl(
        const RecordingOutputRootResolution& roots,
        const std::wstring_view canonicalSessionId,
        const bool createIfMissing) noexcept
    {
        SessionLifetimeOwnerAcquireResult result{};
        Release();
        if (!roots.Succeeded() ||
            !IsCanonicalRecordingSessionId(canonicalSessionId))
        {
            result.status = SessionLifetimeOwnerAcquireStatus::InvalidInput;
            result.diagnosticHResult = E_INVALIDARG;
            return result;
        }
        try
        {
            result.ownerPath = BuildOwnerPath(roots, canonicalSessionId);
            // Complete every allocating operation before opening the kernel
            // lease so an allocation failure cannot strand a live handle.
            ownerPath_ = result.ownerPath;
            const auto directorySafety =
                InspectCanonicalSessionDirectoryForReadOnly(
                    roots, canonicalSessionId);
            if (!directorySafety.SafeForReadOnlyInspection())
            {
                result.status = SessionLifetimeOwnerAcquireStatus::UnsafePath;
                result.diagnosticHResult =
                    directorySafety.diagnosticHResult;
                ownerPath_.clear();
                return result;
            }

            // Maintenance keeps the canonical Session directory itself open
            // without delete sharing. This binds the subsequent OPEN_EXISTING
            // owner lease to the directory identity that Path Safety proved.
            if (!createIfMissing)
            {
                SECURITY_ATTRIBUTES directorySecurity{};
                directorySecurity.nLength = sizeof(directorySecurity);
                directorySecurity.bInheritHandle = FALSE;
                const auto sessionDirectory =
                    roots.sessionsRoot / std::wstring(canonicalSessionId);
                sessionDirectoryHandle_ = CreateFileW(
                    sessionDirectory.c_str(),
                    FILE_READ_ATTRIBUTES,
                    FILE_SHARE_READ,
                    &directorySecurity,
                    OPEN_EXISTING,
                    FILE_FLAG_BACKUP_SEMANTICS |
                        FILE_FLAG_OPEN_REPARSE_POINT,
                    nullptr);
                if (!ValidHandle(sessionDirectoryHandle_))
                {
                    const auto error = GetLastError();
                    result.status = error == ERROR_ACCESS_DENIED ||
                            error == ERROR_SHARING_VIOLATION ||
                            error == ERROR_LOCK_VIOLATION
                        ? SessionLifetimeOwnerAcquireStatus::Inaccessible
                        : SessionLifetimeOwnerAcquireStatus::IoFailure;
                    result.diagnosticHResult = HRESULT_FROM_WIN32(error);
                    ownerPath_.clear();
                    return result;
                }
                FILE_ATTRIBUTE_TAG_INFO directoryAttributes{};
                PersistentFileIdentity directoryIdentity{};
                const auto directoryValid =
                    GetFileInformationByHandleEx(
                        sessionDirectoryHandle_,
                        FileAttributeTagInfo,
                        &directoryAttributes,
                        sizeof(directoryAttributes)) != FALSE &&
                    (directoryAttributes.FileAttributes &
                        FILE_ATTRIBUTE_DIRECTORY) != 0 &&
                    (directoryAttributes.FileAttributes &
                        FILE_ATTRIBUTE_REPARSE_POINT) == 0 &&
                    SUCCEEDED(ReadPersistentFileIdentity(
                        sessionDirectoryHandle_, directoryIdentity));
                const auto stableDirectorySafety =
                    InspectCanonicalSessionDirectoryForReadOnly(
                        roots, canonicalSessionId);
                if (!directoryValid ||
                    !stableDirectorySafety.SafeForReadOnlyInspection() ||
                    !SamePersistentFileIdentity(
                        directoryIdentity,
                        stableDirectorySafety.candidateIdentity))
                {
                    (void)CloseHandle(sessionDirectoryHandle_);
                    sessionDirectoryHandle_ = INVALID_HANDLE_VALUE;
                    result.status =
                        SessionLifetimeOwnerAcquireStatus::UnsafePath;
                    result.diagnosticHResult = E_ACCESSDENIED;
                    ownerPath_.clear();
                    return result;
                }
            }

            SECURITY_ATTRIBUTES security{};
            security.nLength = sizeof(security);
            security.bInheritHandle = FALSE;
            SetLastError(ERROR_SUCCESS);
            const auto handle = CreateFileW(
                ownerPath_.c_str(),
                GENERIC_READ | GENERIC_WRITE,
                0,
                &security,
                createIfMissing ? OPEN_ALWAYS : OPEN_EXISTING,
                FILE_ATTRIBUTE_HIDDEN | FILE_ATTRIBUTE_NOT_CONTENT_INDEXED |
                    FILE_FLAG_OPEN_REPARSE_POINT,
                nullptr);
            if (!ValidHandle(handle))
            {
                const auto error = GetLastError();
                if (error == ERROR_SHARING_VIOLATION ||
                    error == ERROR_LOCK_VIOLATION)
                {
                    result.status =
                        SessionLifetimeOwnerAcquireStatus::AlreadyOwned;
                }
                else if (!createIfMissing &&
                    (error == ERROR_FILE_NOT_FOUND ||
                        error == ERROR_PATH_NOT_FOUND))
                {
                    result.status =
                        SessionLifetimeOwnerAcquireStatus::EvidenceMissing;
                }
                else if (error == ERROR_ACCESS_DENIED ||
                    error == ERROR_PRIVILEGE_NOT_HELD ||
                    error == ERROR_NETWORK_ACCESS_DENIED)
                {
                    result.status =
                        SessionLifetimeOwnerAcquireStatus::Inaccessible;
                }
                else
                {
                    result.status = createIfMissing
                        ? SessionLifetimeOwnerAcquireStatus::Unavailable
                        : SessionLifetimeOwnerAcquireStatus::IoFailure;
                }
                result.diagnosticHResult = HRESULT_FROM_WIN32(error);
                if (ValidHandle(sessionDirectoryHandle_))
                {
                    (void)CloseHandle(sessionDirectoryHandle_);
                    sessionDirectoryHandle_ = INVALID_HANDLE_VALUE;
                }
                ownerPath_.clear();
                return result;
            }

            const auto createdNew = createIfMissing &&
                GetLastError() != ERROR_ALREADY_EXISTS;
            // CREATE/OPEN plus share mode zero is the atomic lease event. A
            // newly created normal leaf needs no fallible post-create step;
            // this avoids a file-exists/lease-never-established window.
            if (!createdNew)
            {
                const auto handleValidation =
                    ValidateOwnerFileHandle(handle);
                if (FAILED(handleValidation))
                {
                    (void)CloseHandle(handle);
                    result.status =
                        SessionLifetimeOwnerAcquireStatus::UnsafePath;
                    result.diagnosticHResult = handleValidation;
                    if (ValidHandle(sessionDirectoryHandle_))
                    {
                        (void)CloseHandle(sessionDirectoryHandle_);
                        sessionDirectoryHandle_ = INVALID_HANDLE_VALUE;
                    }
                    ownerPath_.clear();
                    return result;
                }
            }

            handle_ = handle;
            result.status = SessionLifetimeOwnerAcquireStatus::Acquired;
            result.diagnosticHResult = S_OK;
            return result;
        }
        catch (const std::bad_alloc&)
        {
            result.status = SessionLifetimeOwnerAcquireStatus::Unavailable;
            result.diagnosticHResult = E_OUTOFMEMORY;
            if (ValidHandle(sessionDirectoryHandle_))
            {
                (void)CloseHandle(sessionDirectoryHandle_);
                sessionDirectoryHandle_ = INVALID_HANDLE_VALUE;
            }
            ownerPath_.clear();
            return result;
        }
        catch (...)
        {
            result.status = SessionLifetimeOwnerAcquireStatus::Unavailable;
            result.diagnosticHResult = E_UNEXPECTED;
            if (ValidHandle(sessionDirectoryHandle_))
            {
                (void)CloseHandle(sessionDirectoryHandle_);
                sessionDirectoryHandle_ = INVALID_HANDLE_VALUE;
            }
            ownerPath_.clear();
            return result;
        }
    }

    void SessionLifetimeOwner::Release() noexcept
    {
        if (ValidHandle(handle_))
        {
            (void)CloseHandle(handle_);
        }
        handle_ = INVALID_HANDLE_VALUE;
        if (ValidHandle(sessionDirectoryHandle_))
        {
            (void)CloseHandle(sessionDirectoryHandle_);
        }
        sessionDirectoryHandle_ = INVALID_HANDLE_VALUE;
        ownerPath_.clear();
    }

    bool SessionLifetimeOwner::Acquired() const noexcept
    {
        return ValidHandle(handle_);
    }

    bool SessionLifetimeOwner::HandleIsInheritable() const noexcept
    {
        if (!Acquired()) return false;
        DWORD flags{};
        return GetHandleInformation(handle_, &flags) != FALSE &&
            (flags & HANDLE_FLAG_INHERIT) != 0;
    }

    const std::filesystem::path& SessionLifetimeOwner::OwnerPath() const noexcept
    {
        return ownerPath_;
    }

    SessionLifetimeOwnerProbeState
        ClassifySessionLifetimeOwnerProbeOpenFailure(const DWORD error) noexcept
    {
        switch (error)
        {
        case ERROR_SHARING_VIOLATION:
        case ERROR_LOCK_VIOLATION:
            return SessionLifetimeOwnerProbeState::ActiveOwned;
        case ERROR_FILE_NOT_FOUND:
        case ERROR_PATH_NOT_FOUND:
            return SessionLifetimeOwnerProbeState::EvidenceMissing;
        case ERROR_ACCESS_DENIED:
        case ERROR_PRIVILEGE_NOT_HELD:
        case ERROR_NETWORK_ACCESS_DENIED:
            return SessionLifetimeOwnerProbeState::Inaccessible;
        default:
            return SessionLifetimeOwnerProbeState::IoFailure;
        }
    }

    SessionLifetimeOwnerProbeResult ProbeSessionLifetimeOwner(
        const RecordingOutputRootResolution& roots,
        const std::wstring_view canonicalSessionId) noexcept
    {
        if (!roots.Succeeded() ||
            !IsCanonicalRecordingSessionId(canonicalSessionId))
        {
            return ProbeResult(
                SessionLifetimeOwnerProbeState::Unknown, E_INVALIDARG);
        }
        try
        {
            auto ownerPath = BuildOwnerPath(roots, canonicalSessionId);
            const auto directorySafety =
                InspectCanonicalSessionDirectoryForReadOnly(
                    roots, canonicalSessionId);
            if (!directorySafety.SafeForReadOnlyInspection())
            {
                const auto state = directorySafety.outcome ==
                        PathSafetyOutcome::Inaccessible
                    ? SessionLifetimeOwnerProbeState::Inaccessible
                    : directorySafety.outcome == PathSafetyOutcome::IoFailure
                        ? SessionLifetimeOwnerProbeState::IoFailure
                        : SessionLifetimeOwnerProbeState::UnsafePath;
                return ProbeResult(
                    state, directorySafety.diagnosticHResult,
                    std::move(ownerPath));
            }

            SECURITY_ATTRIBUTES security{};
            security.nLength = sizeof(security);
            security.bInheritHandle = FALSE;
            const auto handle = CreateFileW(
                ownerPath.c_str(),
                GENERIC_READ,
                FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
                &security,
                OPEN_EXISTING,
                FILE_ATTRIBUTE_NORMAL | FILE_FLAG_OPEN_REPARSE_POINT,
                nullptr);
            if (!ValidHandle(handle))
            {
                const auto error = GetLastError();
                return ProbeResult(
                    ClassifySessionLifetimeOwnerProbeOpenFailure(error),
                    HRESULT_FROM_WIN32(error),
                    std::move(ownerPath));
            }

            const auto validation = ValidateOwnerFileHandle(handle);
            (void)CloseHandle(handle);
            if (FAILED(validation))
            {
                return ProbeResult(
                    SessionLifetimeOwnerProbeState::UnsafePath,
                    validation,
                    std::move(ownerPath));
            }
            return ProbeResult(
                SessionLifetimeOwnerProbeState::InactiveLeaseReleased,
                S_OK,
                std::move(ownerPath));
        }
        catch (const std::bad_alloc&)
        {
            return ProbeResult(
                SessionLifetimeOwnerProbeState::IoFailure,
                E_OUTOFMEMORY);
        }
        catch (...)
        {
            return ProbeResult(
                SessionLifetimeOwnerProbeState::Unknown,
                E_UNEXPECTED);
        }
    }
}
