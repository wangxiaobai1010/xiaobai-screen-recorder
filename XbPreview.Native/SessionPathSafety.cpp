#include "SessionPathSafety.h"

#include "RecordingSessionIdentity.h"

#include <windows.h>

#include <algorithm>
#include <cwctype>
#include <new>
#include <utility>
#include <vector>

namespace xbpreview
{
    namespace
    {
        constexpr DWORD SharedReadWriteDelete =
            FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE;
        constexpr std::size_t MaximumSupportedPathCharacters = 32760;

        class UniqueHandle final
        {
        public:
            UniqueHandle() noexcept = default;
            explicit UniqueHandle(const HANDLE value) noexcept : value_(value) {}
            ~UniqueHandle()
            {
                if (Valid())
                {
                    (void)CloseHandle(value_);
                }
            }

            UniqueHandle(const UniqueHandle&) = delete;
            UniqueHandle& operator=(const UniqueHandle&) = delete;

            UniqueHandle(UniqueHandle&& other) noexcept
                : value_(std::exchange(other.value_, INVALID_HANDLE_VALUE))
            {
            }

            UniqueHandle& operator=(UniqueHandle&& other) noexcept
            {
                if (this != &other)
                {
                    if (Valid())
                    {
                        (void)CloseHandle(value_);
                    }
                    value_ = std::exchange(
                        other.value_, INVALID_HANDLE_VALUE);
                }
                return *this;
            }

            [[nodiscard]] HANDLE Get() const noexcept { return value_; }
            [[nodiscard]] bool Valid() const noexcept
            {
                return value_ != nullptr && value_ != INVALID_HANDLE_VALUE;
            }

        private:
            HANDLE value_{ INVALID_HANDLE_VALUE };
        };

        enum class SupportedPathStatus
        {
            Valid,
            InvalidInput,
            Unsupported
        };

        struct SupportedPath final
        {
            wchar_t driveLetter{};
            std::wstring normalized;
            std::vector<std::wstring> components;
        };

        struct OpenedPathFacts final
        {
            UniqueHandle handle;
            PathSafetyObjectType type{ PathSafetyObjectType::Unknown };
            bool reparse{};
            std::uint32_t reparseTag{};
            std::wstring finalPath;
            PathHandleIdentity identity;
            std::optional<std::uint64_t> size;
        };

        bool EqualInsensitive(
            const std::wstring_view left,
            const std::wstring_view right) noexcept
        {
            return left.size() == right.size() &&
                _wcsnicmp(left.data(), right.data(), left.size()) == 0;
        }

        std::wstring UpperAscii(std::wstring value)
        {
            std::transform(
                value.begin(), value.end(), value.begin(),
                [](const wchar_t character)
                {
                    if (character >= L'a' && character <= L'z')
                    {
                        return static_cast<wchar_t>(character - L'a' + L'A');
                    }
                    return character;
                });
            return value;
        }

        bool ReservedDeviceComponent(const std::wstring_view component)
        {
            const auto dot = component.find(L'.');
            const auto base = UpperAscii(std::wstring(component.substr(0, dot)));
            if (base == L"CON" || base == L"PRN" || base == L"AUX" ||
                base == L"NUL" || base == L"CONIN$" || base == L"CONOUT$")
            {
                return true;
            }
            if (base.size() == 4 &&
                (base.rfind(L"COM", 0) == 0 || base.rfind(L"LPT", 0) == 0) &&
                base[3] >= L'1' && base[3] <= L'9')
            {
                return true;
            }
            return false;
        }

        bool InvalidComponent(const std::wstring_view component)
        {
            if (component.empty() || component == L"." ||
                component == L".." || component.back() == L' ' ||
                component.back() == L'.' || ReservedDeviceComponent(component))
            {
                return true;
            }
            for (const auto character : component)
            {
                if (character < 32 || character == L'<' ||
                    character == L'>' || character == L':' ||
                    character == L'"' || character == L'/' ||
                    character == L'\\' || character == L'|' ||
                    character == L'?' || character == L'*')
                {
                    return true;
                }
            }
            return false;
        }

        SupportedPathStatus ParseSupportedPath(
            const std::filesystem::path& input,
            SupportedPath& output)
        {
            auto value = input.native();
            if (value.empty())
            {
                return SupportedPathStatus::InvalidInput;
            }
            const auto asciiDrive =
                (value[0] >= L'A' && value[0] <= L'Z') ||
                (value[0] >= L'a' && value[0] <= L'z');
            if (value.size() > MaximumSupportedPathCharacters ||
                value.find(L'/') != std::wstring::npos ||
                value.size() < 3 || !asciiDrive ||
                value[1] != L':' || value[2] != L'\\')
            {
                return SupportedPathStatus::Unsupported;
            }

            std::size_t trailingSeparators{};
            while (value.size() > 3 && value.back() == L'\\')
            {
                value.pop_back();
                ++trailingSeparators;
            }
            if (trailingSeparators > 1)
            {
                return SupportedPathStatus::Unsupported;
            }

            output = {};
            output.driveLetter = static_cast<wchar_t>(towupper(value[0]));
            value[0] = output.driveLetter;
            output.normalized = value;

            std::size_t start = 3;
            while (start < value.size())
            {
                const auto separator = value.find(L'\\', start);
                const auto length = separator == std::wstring::npos
                    ? value.size() - start
                    : separator - start;
                const auto component = value.substr(start, length);
                if (InvalidComponent(component))
                {
                    return SupportedPathStatus::Unsupported;
                }
                output.components.push_back(component);
                if (separator == std::wstring::npos)
                {
                    break;
                }
                start = separator + 1;
                if (start == value.size())
                {
                    return SupportedPathStatus::Unsupported;
                }
            }
            return SupportedPathStatus::Valid;
        }

        PathSafetyResult Result(
            const PathSafetyOutcome outcome,
            const PathSafetyProbeStage stage,
            const HRESULT hresult) noexcept
        {
            PathSafetyResult result{};
            result.outcome = outcome;
            result.stage = stage;
            result.diagnosticHResult = hresult;
            return result;
        }

        bool MissingError(const DWORD error) noexcept
        {
            return error == ERROR_FILE_NOT_FOUND ||
                error == ERROR_PATH_NOT_FOUND;
        }

        bool InaccessibleError(const DWORD error) noexcept
        {
            return error == ERROR_ACCESS_DENIED ||
                error == ERROR_SHARING_VIOLATION ||
                error == ERROR_LOCK_VIOLATION ||
                error == ERROR_PRIVILEGE_NOT_HELD ||
                error == ERROR_NETWORK_ACCESS_DENIED;
        }

        PathSafetyOutcome OpenFailureOutcome(const DWORD error) noexcept
        {
            if (InaccessibleError(error))
            {
                return PathSafetyOutcome::Inaccessible;
            }
            if (error == ERROR_INVALID_NAME || error == ERROR_BAD_PATHNAME ||
                error == ERROR_FILENAME_EXCED_RANGE)
            {
                return PathSafetyOutcome::UnsupportedPathForm;
            }
            return PathSafetyOutcome::IoFailure;
        }

        UniqueHandle OpenPathObject(
            const std::wstring& path,
            const DWORD desiredAccess = FILE_READ_ATTRIBUTES) noexcept
        {
            return UniqueHandle(CreateFileW(
                path.c_str(),
                desiredAccess,
                SharedReadWriteDelete,
                nullptr,
                OPEN_EXISTING,
                FILE_FLAG_BACKUP_SEMANTICS | FILE_FLAG_OPEN_REPARSE_POINT,
                nullptr));
        }

        HRESULT ReadFinalPath(
            const HANDLE handle,
            std::wstring& finalPath)
        {
            const auto required = GetFinalPathNameByHandleW(
                handle,
                nullptr,
                0,
                FILE_NAME_NORMALIZED | VOLUME_NAME_DOS);
            if (required == 0)
            {
                return HRESULT_FROM_WIN32(GetLastError());
            }
            std::vector<wchar_t> buffer(
                static_cast<std::size_t>(required) + 1);
            const auto written = GetFinalPathNameByHandleW(
                handle,
                buffer.data(),
                static_cast<DWORD>(buffer.size()),
                FILE_NAME_NORMALIZED | VOLUME_NAME_DOS);
            if (written == 0 || written >= buffer.size())
            {
                return HRESULT_FROM_WIN32(
                    written == 0 ? GetLastError() : ERROR_INSUFFICIENT_BUFFER);
            }
            finalPath.assign(buffer.data(), written);
            return S_OK;
        }

        HRESULT ReadOpenedPathFacts(OpenedPathFacts& facts)
        {
            BY_HANDLE_FILE_INFORMATION information{};
            if (!GetFileInformationByHandle(
                    facts.handle.Get(), &information))
            {
                return HRESULT_FROM_WIN32(GetLastError());
            }
            facts.reparse =
                (information.dwFileAttributes &
                    FILE_ATTRIBUTE_REPARSE_POINT) != 0;
            if (facts.reparse)
            {
                FILE_ATTRIBUTE_TAG_INFO tagInfo{};
                if (!GetFileInformationByHandleEx(
                        facts.handle.Get(),
                        FileAttributeTagInfo,
                        &tagInfo,
                        sizeof(tagInfo)))
                {
                    return HRESULT_FROM_WIN32(GetLastError());
                }
                facts.reparseTag = tagInfo.ReparseTag;
                facts.type = PathSafetyObjectType::ReparsePoint;
            }
            else if ((information.dwFileAttributes &
                FILE_ATTRIBUTE_DIRECTORY) != 0)
            {
                facts.type = PathSafetyObjectType::Directory;
            }
            else
            {
                SetLastError(NO_ERROR);
                const auto fileType = GetFileType(facts.handle.Get());
                if (fileType == FILE_TYPE_DISK)
                {
                    facts.type = PathSafetyObjectType::RegularFile;
                    facts.size =
                        (static_cast<std::uint64_t>(
                            information.nFileSizeHigh) << 32) |
                        information.nFileSizeLow;
                }
                else if (fileType == FILE_TYPE_UNKNOWN && GetLastError() != NO_ERROR)
                {
                    return HRESULT_FROM_WIN32(GetLastError());
                }
                else
                {
                    facts.type = PathSafetyObjectType::Other;
                }
            }

            const auto identityResult = ReadPersistentFileIdentity(
                facts.handle.Get(), facts.identity);
            if (FAILED(identityResult))
            {
                return identityResult;
            }
            return ReadFinalPath(facts.handle.Get(), facts.finalPath);
        }

        std::wstring DriveRoot(const SupportedPath& path)
        {
            return std::wstring{ path.driveLetter, L':', L'\\' };
        }

        std::wstring AppendComponent(
            std::wstring current,
            const std::wstring& component)
        {
            if (current.size() > 3 && current.back() != L'\\')
            {
                current.push_back(L'\\');
            }
            current.append(component);
            return current;
        }

        bool ComponentPrefix(
            const SupportedPath& root,
            const SupportedPath& candidate) noexcept
        {
            if (towupper(root.driveLetter) != towupper(candidate.driveLetter) ||
                candidate.components.size() < root.components.size())
            {
                return false;
            }
            for (std::size_t index = 0; index < root.components.size(); ++index)
            {
                if (!EqualInsensitive(
                        root.components[index], candidate.components[index]))
                {
                    return false;
                }
            }
            return true;
        }

        bool ParseFinalPath(
            const std::wstring& finalPath,
            SupportedPath& parsed)
        {
            constexpr std::wstring_view extendedPrefix = L"\\\\?\\";
            constexpr std::wstring_view extendedUncPrefix = L"\\\\?\\UNC\\";
            if (finalPath.size() >= extendedUncPrefix.size() &&
                EqualInsensitive(
                    std::wstring_view(finalPath).substr(
                        0, extendedUncPrefix.size()),
                    extendedUncPrefix))
            {
                return false;
            }
            std::wstring normalized = finalPath;
            if (normalized.size() >= extendedPrefix.size() &&
                EqualInsensitive(
                    std::wstring_view(normalized).substr(
                        0, extendedPrefix.size()),
                    extendedPrefix))
            {
                normalized.erase(0, extendedPrefix.size());
            }
            return ParseSupportedPath(normalized, parsed) ==
                SupportedPathStatus::Valid;
        }

        bool ExpectedTypeMatches(
            const PathSafetyExpectedType expected,
            const PathSafetyObjectType actual) noexcept
        {
            switch (expected)
            {
            case PathSafetyExpectedType::Any:
                return actual == PathSafetyObjectType::RegularFile ||
                    actual == PathSafetyObjectType::Directory;
            case PathSafetyExpectedType::RegularFile:
                return actual == PathSafetyObjectType::RegularFile;
            case PathSafetyExpectedType::Directory:
                return actual == PathSafetyObjectType::Directory;
            default:
                return false;
            }
        }

        void CopyRootFacts(
            PathSafetyResult& result,
            const OpenedPathFacts& rootFacts)
        {
            result.trustedRootValidated = true;
            result.trustedRootFinalPath = rootFacts.finalPath;
            result.trustedRootIdentity = rootFacts.identity;
        }

        PathSafetyResult InspectPathForReadOnlyImpl(
            const std::filesystem::path& trustedRoot,
            const std::filesystem::path& candidatePath,
            const PathSafetyExpectedType expectedType)
        {
            SupportedPath root{};
            SupportedPath candidate{};
            const auto rootStatus = ParseSupportedPath(trustedRoot, root);
            const auto candidateStatus = ParseSupportedPath(
                candidatePath, candidate);
            if (rootStatus == SupportedPathStatus::InvalidInput ||
                candidateStatus == SupportedPathStatus::InvalidInput)
            {
                return Result(
                    PathSafetyOutcome::InvalidInput,
                    PathSafetyProbeStage::InputValidation,
                    E_INVALIDARG);
            }
            if (rootStatus != SupportedPathStatus::Valid ||
                candidateStatus != SupportedPathStatus::Valid)
            {
                return Result(
                    PathSafetyOutcome::UnsupportedPathForm,
                    PathSafetyProbeStage::InputValidation,
                    HRESULT_FROM_WIN32(ERROR_NOT_SUPPORTED));
            }

            PathSafetyResult result{};
            result.stage = PathSafetyProbeStage::TrustedRootOpen;
            std::wstring current = DriveRoot(root);
            OpenedPathFacts rootFacts{};
            for (std::size_t index = 0;
                index <= root.components.size(); ++index)
            {
                if (index > 0)
                {
                    current = AppendComponent(
                        std::move(current), root.components[index - 1]);
                }
                auto handle = OpenPathObject(current);
                if (!handle.Valid())
                {
                    const auto error = GetLastError();
                    result.outcome = MissingError(error)
                        ? PathSafetyOutcome::TrustedRootInvalid
                        : OpenFailureOutcome(error);
                    result.diagnosticHResult = HRESULT_FROM_WIN32(error);
                    result.diagnosticPath = current;
                    return result;
                }
                OpenedPathFacts facts{};
                facts.handle = std::move(handle);
                const auto factResult = ReadOpenedPathFacts(facts);
                if (FAILED(factResult))
                {
                    result.outcome = PathSafetyOutcome::IoFailure;
                    result.stage = PathSafetyProbeStage::TrustedRootFacts;
                    result.diagnosticHResult = factResult;
                    result.diagnosticPath = current;
                    return result;
                }
                if (facts.reparse)
                {
                    result.outcome = PathSafetyOutcome::ReparseEncountered;
                    result.stage = PathSafetyProbeStage::TrustedRootFacts;
                    result.diagnosticHResult = E_ACCESSDENIED;
                    result.reparseDetected = true;
                    result.reparseTag = facts.reparseTag;
                    result.reparsePath = current;
                    result.diagnosticPath = current;
                    return result;
                }
                const auto lastRootComponent = index == root.components.size();
                if (!lastRootComponent &&
                    facts.type != PathSafetyObjectType::Directory)
                {
                    result.outcome = PathSafetyOutcome::TrustedRootInvalid;
                    result.stage = PathSafetyProbeStage::TrustedRootFacts;
                    result.diagnosticHResult = E_INVALIDARG;
                    result.diagnosticPath = current;
                    return result;
                }
                if (lastRootComponent)
                {
                    rootFacts = std::move(facts);
                }
            }
            if (rootFacts.type != PathSafetyObjectType::Directory)
            {
                result.outcome = PathSafetyOutcome::TrustedRootInvalid;
                result.stage = PathSafetyProbeStage::TrustedRootFacts;
                result.diagnosticHResult = E_INVALIDARG;
                return result;
            }
            SupportedPath rootFinal{};
            if (!ParseFinalPath(rootFacts.finalPath, rootFinal))
            {
                result.outcome = PathSafetyOutcome::UnsupportedPathForm;
                result.stage = PathSafetyProbeStage::TrustedRootFacts;
                result.diagnosticHResult = HRESULT_FROM_WIN32(
                    ERROR_NOT_SUPPORTED);
                return result;
            }
            CopyRootFacts(result, rootFacts);

            if (!ComponentPrefix(root, candidate))
            {
                result.outcome = PathSafetyOutcome::OutsideTrustedRoot;
                result.stage = PathSafetyProbeStage::CandidateContainment;
                result.diagnosticHResult = E_ACCESSDENIED;
                return result;
            }

            OpenedPathFacts candidateFacts{};
            if (candidate.components.size() == root.components.size())
            {
                candidateFacts.type = rootFacts.type;
                candidateFacts.finalPath = rootFacts.finalPath;
                candidateFacts.identity = rootFacts.identity;
            }
            else
            {
                current = root.normalized;
                for (std::size_t index = root.components.size();
                    index < candidate.components.size(); ++index)
                {
                    current = AppendComponent(
                        std::move(current), candidate.components[index]);
                    const auto last =
                        index + 1 == candidate.components.size();
                    const auto desiredAccess =
                        last && expectedType ==
                            PathSafetyExpectedType::RegularFile
                        ? FILE_READ_ATTRIBUTES | FILE_READ_DATA
                        : FILE_READ_ATTRIBUTES;
                    auto handle = OpenPathObject(current, desiredAccess);
                    if (!handle.Valid())
                    {
                        const auto error = GetLastError();
                        result.stage = last
                            ? PathSafetyProbeStage::CandidateOpen
                            : PathSafetyProbeStage::CandidateChain;
                        result.diagnosticHResult = HRESULT_FROM_WIN32(error);
                        result.diagnosticPath = current;
                        if (MissingError(error))
                        {
                            result.outcome = last
                                ? PathSafetyOutcome::Absent
                                : PathSafetyOutcome::ParentAbsent;
                            result.parentChainVerified = last;
                            result.containmentProven = last;
                        }
                        else
                        {
                            result.outcome = OpenFailureOutcome(error);
                        }
                        return result;
                    }
                    OpenedPathFacts facts{};
                    facts.handle = std::move(handle);
                    const auto factResult = ReadOpenedPathFacts(facts);
                    if (FAILED(factResult))
                    {
                        result.outcome = factResult == E_ACCESSDENIED
                            ? PathSafetyOutcome::Inaccessible
                            : PathSafetyOutcome::IoFailure;
                        result.stage = last
                            ? PathSafetyProbeStage::CandidateFacts
                            : PathSafetyProbeStage::CandidateChain;
                        result.diagnosticHResult = factResult;
                        result.diagnosticPath = current;
                        return result;
                    }
                    if (facts.reparse)
                    {
                        result.outcome = PathSafetyOutcome::ReparseEncountered;
                        result.stage = last
                            ? PathSafetyProbeStage::CandidateFacts
                            : PathSafetyProbeStage::CandidateChain;
                        result.diagnosticHResult = E_ACCESSDENIED;
                        result.reparseDetected = true;
                        result.reparseTag = facts.reparseTag;
                        result.reparsePath = current;
                        result.diagnosticPath = current;
                        result.candidateExists = last;
                        result.candidateType = last
                            ? PathSafetyObjectType::ReparsePoint
                            : PathSafetyObjectType::Unknown;
                        return result;
                    }
                    SupportedPath openedFinal{};
                    if (!ParseFinalPath(facts.finalPath, openedFinal))
                    {
                        result.outcome =
                            PathSafetyOutcome::UnsupportedPathForm;
                        result.stage = last
                            ? PathSafetyProbeStage::CandidateFacts
                            : PathSafetyProbeStage::CandidateChain;
                        result.diagnosticHResult = HRESULT_FROM_WIN32(
                            ERROR_NOT_SUPPORTED);
                        result.diagnosticPath = current;
                        return result;
                    }
                    if (!ComponentPrefix(rootFinal, openedFinal) ||
                        !facts.identity.available ||
                        facts.identity.volumeSerialNumber !=
                            rootFacts.identity.volumeSerialNumber)
                    {
                        result.outcome =
                            PathSafetyOutcome::OutsideTrustedRoot;
                        result.stage =
                            PathSafetyProbeStage::CandidateContainment;
                        result.diagnosticHResult = E_ACCESSDENIED;
                        result.diagnosticPath = current;
                        return result;
                    }
                    if (!last && facts.type != PathSafetyObjectType::Directory)
                    {
                        result.outcome = PathSafetyOutcome::TypeMismatch;
                        result.stage = PathSafetyProbeStage::CandidateChain;
                        result.diagnosticHResult = E_INVALIDARG;
                        result.diagnosticPath = current;
                        return result;
                    }
                    if (last)
                    {
                        candidateFacts = std::move(facts);
                    }
                }
            }

            result.candidateExists = true;
            result.parentChainVerified = true;
            result.candidateType = candidateFacts.type;
            result.candidateFinalPath = candidateFacts.finalPath;
            result.candidateIdentity = candidateFacts.identity;
            result.candidateSize = candidateFacts.size;

            SupportedPath candidateFinal{};
            if (!ParseFinalPath(candidateFacts.finalPath, candidateFinal))
            {
                result.outcome = PathSafetyOutcome::UnsupportedPathForm;
                result.stage = PathSafetyProbeStage::CandidateFacts;
                result.diagnosticHResult = HRESULT_FROM_WIN32(
                    ERROR_NOT_SUPPORTED);
                return result;
            }
            if (!ComponentPrefix(rootFinal, candidateFinal) ||
                !rootFacts.identity.available ||
                !candidateFacts.identity.available ||
                rootFacts.identity.volumeSerialNumber !=
                    candidateFacts.identity.volumeSerialNumber)
            {
                result.outcome = PathSafetyOutcome::OutsideTrustedRoot;
                result.stage = PathSafetyProbeStage::CandidateContainment;
                result.diagnosticHResult = E_ACCESSDENIED;
                return result;
            }
            result.containmentProven = true;

            if (!ExpectedTypeMatches(expectedType, candidateFacts.type))
            {
                result.outcome = PathSafetyOutcome::TypeMismatch;
                result.stage = PathSafetyProbeStage::CandidateFacts;
                result.diagnosticHResult = E_INVALIDARG;
                return result;
            }

            result.outcome = PathSafetyOutcome::SafeForReadOnlyInspection;
            result.stage = PathSafetyProbeStage::None;
            result.diagnosticHResult = S_OK;
            return result;
        }
    }

    PathSafetyResult InspectPathForReadOnly(
        const std::filesystem::path& trustedRoot,
        const std::filesystem::path& candidatePath,
        const PathSafetyExpectedType expectedType) noexcept
    {
        try
        {
            return InspectPathForReadOnlyImpl(
                trustedRoot, candidatePath, expectedType);
        }
        catch (const std::bad_alloc&)
        {
            return Result(
                PathSafetyOutcome::IoFailure,
                PathSafetyProbeStage::None,
                E_OUTOFMEMORY);
        }
        catch (...)
        {
            return Result(
                PathSafetyOutcome::IoFailure,
                PathSafetyProbeStage::None,
                E_UNEXPECTED);
        }
    }

    PathSafetyResult InspectCanonicalSessionDirectoryForReadOnly(
        const RecordingOutputRootResolution& roots,
        const std::wstring_view canonicalSessionId) noexcept
    {
        try
        {
            if (!roots.Succeeded() ||
                !IsCanonicalRecordingSessionId(canonicalSessionId))
            {
                return Result(
                    PathSafetyOutcome::InvalidInput,
                    PathSafetyProbeStage::InputValidation,
                    E_INVALIDARG);
            }
            return InspectPathForReadOnly(
                roots.sessionsRoot,
                roots.sessionsRoot / std::wstring(canonicalSessionId),
                PathSafetyExpectedType::Directory);
        }
        catch (const std::bad_alloc&)
        {
            return Result(
                PathSafetyOutcome::IoFailure,
                PathSafetyProbeStage::None,
                E_OUTOFMEMORY);
        }
        catch (...)
        {
            return Result(
                PathSafetyOutcome::IoFailure,
                PathSafetyProbeStage::None,
                E_UNEXPECTED);
        }
    }

    PathSafetyResult InspectRecordingMediaPathForReadOnly(
        const RecordingOutputRootResolution& roots,
        const std::filesystem::path& manifestCandidatePath) noexcept
    {
        if (!roots.Succeeded())
        {
            return Result(
                PathSafetyOutcome::InvalidInput,
                PathSafetyProbeStage::InputValidation,
                E_INVALIDARG);
        }
        return InspectPathForReadOnly(
            roots.mediaOutputRoot,
            manifestCandidatePath,
            PathSafetyExpectedType::RegularFile);
    }
}
