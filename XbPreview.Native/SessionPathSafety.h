#pragma once

#include "PersistentFileIdentity.h"
#include "RecordingOutputRoot.h"

#include <windows.h>

#include <cstdint>
#include <filesystem>
#include <optional>
#include <string>
#include <string_view>

namespace xbpreview
{
    enum class PathSafetyOutcome
    {
        SafeForReadOnlyInspection,
        Absent,
        ParentAbsent,
        Inaccessible,
        OutsideTrustedRoot,
        ReparseEncountered,
        UnsupportedPathForm,
        InvalidInput,
        TrustedRootInvalid,
        TypeMismatch,
        IoFailure,
        Unknown
    };

    enum class PathSafetyObjectType
    {
        Unknown,
        RegularFile,
        Directory,
        ReparsePoint,
        Other
    };

    enum class PathSafetyExpectedType
    {
        Any,
        RegularFile,
        Directory
    };

    enum class PathSafetyProbeStage
    {
        None,
        InputValidation,
        TrustedRootOpen,
        TrustedRootFacts,
        CandidateContainment,
        CandidateChain,
        CandidateOpen,
        CandidateFacts
    };

    using PathHandleIdentity = PersistentFileIdentity;

    struct PathSafetyResult final
    {
        PathSafetyOutcome outcome{ PathSafetyOutcome::Unknown };
        PathSafetyProbeStage stage{ PathSafetyProbeStage::None };
        HRESULT diagnosticHResult{ E_UNEXPECTED };
        bool trustedRootValidated{};
        bool candidateExists{};
        bool parentChainVerified{};
        bool containmentProven{};
        bool reparseDetected{};
        std::uint32_t reparseTag{};
        PathSafetyObjectType candidateType{ PathSafetyObjectType::Unknown };
        std::wstring trustedRootFinalPath;
        std::wstring candidateFinalPath;
        std::wstring diagnosticPath;
        std::wstring reparsePath;
        PathHandleIdentity trustedRootIdentity;
        PathHandleIdentity candidateIdentity;
        std::optional<std::uint64_t> candidateSize;

        [[nodiscard]] bool SafeForReadOnlyInspection() const noexcept
        {
            return outcome ==
                PathSafetyOutcome::SafeForReadOnlyInspection;
        }
    };

    // This is a point-in-time authorization for read-only inspection only.
    // It is never authorization to delete, rename, overwrite, publish, repair,
    // or reconcile. Every future mutation must reopen handles and revalidate
    // path, reparse, containment, and identity at operation time.
    [[nodiscard]] PathSafetyResult InspectPathForReadOnly(
        const std::filesystem::path& trustedRoot,
        const std::filesystem::path& candidatePath,
        PathSafetyExpectedType expectedType =
            PathSafetyExpectedType::Any) noexcept;

    // Session directories are derived only from the shared SessionsRoot and a
    // canonical SessionId; no Manifest-provided directory path is accepted.
    [[nodiscard]] PathSafetyResult InspectCanonicalSessionDirectoryForReadOnly(
        const RecordingOutputRootResolution& roots,
        std::wstring_view canonicalSessionId) noexcept;

    // Manifest media paths are untrusted candidates checked against the shared
    // MediaOutputRoot. This wrapper never reads or mutates media contents.
    [[nodiscard]] PathSafetyResult InspectRecordingMediaPathForReadOnly(
        const RecordingOutputRootResolution& roots,
        const std::filesystem::path& manifestCandidatePath) noexcept;
}
