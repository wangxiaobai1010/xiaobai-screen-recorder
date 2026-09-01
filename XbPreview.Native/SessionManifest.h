#pragma once

#include <windows.h>

#include <cstdint>
#include <filesystem>
#include <optional>
#include <string>

namespace xbpreview
{
    inline constexpr std::uint32_t SessionManifestLegacySchemaVersion = 1;
    inline constexpr std::uint32_t SessionManifestSchemaVersion = 2;
    inline constexpr std::uint32_t SessionManifestReconciledSchemaVersion = 3;

    enum class SessionManifestState
    {
        Created,
        Starting,
        Recording,
        Stopping,
        ReadyToPublish,
        Published,
        Completed,
        Failed,
        Unknown,
        ReconciledCompleted,
        UserCancelled
    };

    enum class SessionManifestErrorCategory
    {
        None,
        Recording,
        Finalize,
        Validation,
        Publish,
        ManifestPersistence,
        UnknownCrash
    };

    struct SessionManifestOperationFacts
    {
        bool attempted{};
        std::optional<HRESULT> hresult;
    };

    struct SessionManifestFinalizeFacts final : SessionManifestOperationFacts
    {
        std::uint32_t count{};
    };

    struct SessionManifestValidationFacts final : SessionManifestOperationFacts
    {
        bool passed{};
    };

    struct SessionManifestPublishFacts final : SessionManifestOperationFacts
    {
        bool published{};
    };

    struct SessionManifestFileIdentityEvidence final
    {
        bool attempted{};
        bool captured{};
        std::wstring volumeIdentity;
        std::wstring fileId;
        std::optional<HRESULT> hresult;
    };

    struct SessionManifestPostPublishIdentityVerification final
    {
        bool attempted{};
        bool matched{};
        std::optional<HRESULT> hresult;
    };

    enum class SessionManifestReconciliationKind
    {
        None,
        FinalAtPlannedPathSamePersistentFileV1
    };

    enum class SessionManifestReconciliationEvidenceKind
    {
        None,
        MaintenanceLeaseCasHeldFinalIdentityV1
    };

    struct SessionManifestReconciliationFacts final
    {
        bool reconciled{};
        SessionManifestReconciliationKind kind{
            SessionManifestReconciliationKind::None };
        std::uint64_t sourceRevision{};
        std::wstring reconciledAtUtc;
        SessionManifestReconciliationEvidenceKind evidenceKind{
            SessionManifestReconciliationEvidenceKind::None };
        bool originalPublishResultKnown{};
        std::wstring confirmedFinalPath;
    };

    struct SessionManifest final
    {
        std::uint32_t schemaVersion{ SessionManifestSchemaVersion };
        std::uint64_t revision{};
        std::wstring writerStrategy{ L"mf-sinkwriter-standard-mp4-v1" };
        std::wstring sessionId;
        std::wstring createdAtUtc;
        std::wstring updatedAtUtc;
        std::wstring workingPath;
        std::wstring plannedFinalPath;
        std::wstring publishedPath;
        SessionManifestState state{ SessionManifestState::Created };
        bool workingFileOwnedBySession{};
        bool writeSampleAttempted{};
        bool frameSubmitted{};
        bool workerExited{};
        bool recordingResourcesReleased{};
        std::uint32_t residualOutstanding{};
        SessionManifestFinalizeFacts finalize;
        SessionManifestValidationFacts validation;
        SessionManifestPublishFacts publish;
        SessionManifestFileIdentityEvidence workingFileIdentity;
        SessionManifestPostPublishIdentityVerification
            postPublishIdentityVerification;
        SessionManifestReconciliationFacts reconciliation;
        SessionManifestErrorCategory errorCategory{
            SessionManifestErrorCategory::None };
        std::optional<HRESULT> errorCode;
        std::wstring errorMessage;
    };

    enum class SessionManifestParseStatus
    {
        Valid,
        NotFound,
        Inaccessible,
        MalformedJson,
        UnsupportedSchema,
        SemanticInvalid,
        UnknownOrFutureState,
        IoFailure
    };

    enum class SessionManifestSemanticIssue
    {
        None,
        SessionIdentityMismatch,
        PathPolicyViolation,
        PublishedPathMismatch,
        Other
    };

    struct SessionManifestParseResult final
    {
        SessionManifestParseStatus status{
            SessionManifestParseStatus::IoFailure };
        HRESULT diagnosticHResult{ E_UNEXPECTED };
        std::optional<std::uint32_t> observedSchemaVersion;
        SessionManifestSemanticIssue semanticIssue{
            SessionManifestSemanticIssue::None };
    };

    // Pure Win32/HRESULT classification used by the file reader and its
    // deterministic IoFailure contract tests.
    [[nodiscard]] SessionManifestParseStatus
        ClassifySessionManifestReadFailure(HRESULT result) noexcept;

    enum class SessionManifestCompareExchangeStatus
    {
        Ready,
        Succeeded,
        RevisionMismatch,
        NotFound,
        Inaccessible,
        UnsupportedSchema,
        MalformedManifest,
        SemanticInvalid,
        ConcurrentChange,
        AtomicWriteFailure,
        IoFailure,
        InvalidInput,
        Inactive
    };

    struct SessionManifestCompareExchangeResult final
    {
        SessionManifestCompareExchangeStatus status{
            SessionManifestCompareExchangeStatus::Inactive };
        HRESULT diagnosticHResult{ E_UNEXPECTED };
        std::uint64_t expectedRevision{};
        std::optional<std::uint64_t> observedRevision;
        SessionManifestSemanticIssue semanticIssue{
            SessionManifestSemanticIssue::None };

        [[nodiscard]] bool Ready() const noexcept
        {
            return status == SessionManifestCompareExchangeStatus::Ready &&
                SUCCEEDED(diagnosticHResult);
        }

        [[nodiscard]] bool Succeeded() const noexcept
        {
            return status ==
                    SessionManifestCompareExchangeStatus::Succeeded &&
                SUCCEEDED(diagnosticHResult);
        }
    };

    // Holds manifest.write.lock across an operation-time evidence critical
    // section. It is an internal native contract and is not part of the C ABI.
    class SessionManifestWriteTransaction final
    {
    public:
        SessionManifestWriteTransaction() noexcept = default;
        ~SessionManifestWriteTransaction();
        SessionManifestWriteTransaction(
            const SessionManifestWriteTransaction&) = delete;
        SessionManifestWriteTransaction& operator=(
            const SessionManifestWriteTransaction&) = delete;
        SessionManifestWriteTransaction(
            SessionManifestWriteTransaction&& other) noexcept;
        SessionManifestWriteTransaction& operator=(
            SessionManifestWriteTransaction&& other) noexcept;

        [[nodiscard]] bool Active() const noexcept;
        [[nodiscard]] std::uint64_t ExpectedRevision() const noexcept;
        [[nodiscard]] const SessionManifest& CurrentManifest() const noexcept;

        // The caller supplies a mutation based on CurrentManifest with the
        // current revision. On success this method increments exactly once,
        // atomically replaces manifest.json, and releases the write lock.
        [[nodiscard]] SessionManifestCompareExchangeResult CompareExchange(
            SessionManifest& manifest) noexcept;

        // Dedicated schema-2 ReadyToPublish -> schema-3
        // ReconciledCompleted metadata transition. Unlike the compatibility
        // CompareExchange path, the caller supplies the already-whitelisted
        // N+1 target (including its single captured timestamp).
        [[nodiscard]] SessionManifestCompareExchangeResult
            CompareExchangeNarrowReconciliation(
                SessionManifest& manifest) noexcept;
        void Reset() noexcept;

    private:
        friend class SessionManifestStore;

        HANDLE lockHandle_{ INVALID_HANDLE_VALUE };
        std::filesystem::path lockPath_;
        std::filesystem::path managedOutputRoot_;
        std::filesystem::path sessionDirectory_;
        std::filesystem::path manifestPath_;
        std::wstring sessionId_;
        SessionManifest current_;
        std::uint64_t expectedRevision_{};

        [[nodiscard]] SessionManifestCompareExchangeResult
            CompareExchangeImpl(
                SessionManifest& manifest,
                bool narrowReconciliation) noexcept;
    };

    class SessionManifestStore final
    {
    public:
        SessionManifestStore(
            std::filesystem::path managedOutputRoot,
            std::wstring sessionId);

        [[nodiscard]] const std::filesystem::path& ManagedOutputRoot() const
            noexcept;
        [[nodiscard]] const std::filesystem::path& SessionsRoot() const
            noexcept;
        [[nodiscard]] const std::filesystem::path& SessionDirectory() const
            noexcept;
        [[nodiscard]] const std::filesystem::path& ManifestPath() const
            noexcept;

        // Creates revision 1 without replacing an existing manifest.
        HRESULT CreateManifest(SessionManifest& manifest) noexcept;

        // Requires manifest.revision to match the current on-disk revision.
        // The saved revision is incremented exactly once on success.
        HRESULT UpdateManifest(SessionManifest& manifest) noexcept;

        // Returns a structured read result for inspection callers. The output
        // manifest is changed only when status is Valid.
        [[nodiscard]] SessionManifestParseResult ParseManifest(
            SessionManifest& manifest) const noexcept;

        // Compatibility wrapper for existing writer/lifecycle callers.
        HRESULT LoadManifest(SessionManifest& manifest) const noexcept;

        // Acquires manifest.write.lock, reloads and strictly validates the
        // current manifest under that lock, then compares its revision. The
        // returned transaction keeps the lock held for operation-time facts.
        [[nodiscard]] SessionManifestCompareExchangeResult
            BeginExpectedRevisionTransaction(
                std::uint64_t expectedRevision,
                SessionManifestWriteTransaction& transaction) const noexcept;

        // expectedRevision == nullopt means create-only. A value means replace
        // only when the current manifest has that exact revision and the new
        // manifest has expectedRevision + 1.
        HRESULT SaveAtomic(
            const SessionManifest& manifest,
            std::optional<std::uint64_t> expectedRevision) noexcept;

    private:
        std::filesystem::path managedOutputRoot_;
        std::filesystem::path sessionsRoot_;
        std::filesystem::path sessionDirectory_;
        std::filesystem::path manifestPath_;
        std::wstring sessionId_;
        HRESULT initializationHResult_{ E_UNEXPECTED };
    };
}
