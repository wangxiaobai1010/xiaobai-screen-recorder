#include "VideoEncoderConsumer.h"

#include "GStreamerAudioCore.h"
#include "GStreamerAudioFinalizer.h"
#include "GStreamerMicrophoneDeviceMonitor.h"
#include "PersistentFileIdentity.h"
#include "D3D11Nv12Converter.h"
#include "MfH264SinkWriterSession.h"
#include "Nv12TrackedTexturePool.h"
#include "RecordingOutputRoot.h"
#include "RecordingStorageSafety.h"
#include "SessionLifetimeOwner.h"
#include "SessionManifest.h"
#include "SessionPathSafety.h"
#include "VideoEncoderDiagnostics.h"
#include "VideoEncoderTimestamp.h"

#include <windows.h>
#include <mferror.h>

#include <atomic>
#include <chrono>
#include <cmath>
#include <filesystem>
#include <limits>
#include <mutex>
#include <new>
#include <stdexcept>
#include <string_view>
#include <system_error>
#include <thread>
#include <utility>
#include <vector>

namespace xbpreview
{
    namespace
    {
        enum class WorkerFailureStage : std::uint32_t
        {
            None,
            ComInitialization,
            OutputReservation,
            PipelineStartup,
            NoAcceptedFrames,
            VideoConversion,
            SampleCreation,
            SampleWrite,
            Storage,
            Finalize,
            TrackedReturn,
            RuntimeValidation,
            WorkingFile,
            Publish,
            BadAllocation,
            StandardException,
            UnknownException,
            Join
        };

        struct WorkerOutcome
        {
            bool workerExited{};
            bool readyForPublishCandidate{};
            bool outputOwnedBySession{};
            bool writeSampleAttempted{};
            bool finalizeAttempted{};
            HRESULT finalizeHResult{ E_PENDING };
            bool validationAttempted{};
            HRESULT validationHResult{ E_PENDING };
            HRESULT failureHResult{ S_OK };
            WorkerFailureStage failureStage{ WorkerFailureStage::None };
            std::uint32_t finalizeCount{};
            std::uint32_t residualOutstanding{};
            std::uint64_t framesSubmitted{};
            wchar_t outputPath[260]{};
        };

        struct PublishOutcome
        {
            bool attempted{};
            bool succeeded{};
            HRESULT hresult{ E_PENDING };
            wchar_t publishedPath[260]{};
        };

        struct OutputCleanupOutcome
        {
            bool attempted{};
            bool succeeded{};
            HRESULT hresult{ S_OK };
        };

        [[nodiscard]] std::int64_t QueryPerformanceCounterValue() noexcept
        {
            LARGE_INTEGER value{};
            return QueryPerformanceCounter(&value) ? value.QuadPart : 0;
        }

        [[nodiscard]] std::int64_t PerformanceCounterTicksFromNanoseconds(
            const std::int64_t nanoseconds,
            const std::int64_t frequency) noexcept
        {
            if (nanoseconds <= 0 || frequency <= 0)
            {
                return 0;
            }
            constexpr std::int64_t NanosecondsPerSecond = 1'000'000'000;
            const auto seconds = nanoseconds / NanosecondsPerSecond;
            const auto remainder = nanoseconds % NanosecondsPerSecond;
            return seconds * frequency +
                (remainder * frequency) / NanosecondsPerSecond;
        }

        [[nodiscard]] bool SameExactWindowsPath(
            const std::filesystem::path& left,
            const std::filesystem::path& right) noexcept
        {
            return !left.empty() && !right.empty() &&
                _wcsicmp(left.c_str(), right.c_str()) == 0;
        }

        class CancellationDeleteHandle final
        {
        public:
            explicit CancellationDeleteHandle(const HANDLE value) noexcept
                : value_(value)
            {
            }

            ~CancellationDeleteHandle()
            {
                if (Valid())
                {
                    (void)CloseHandle(value_);
                }
            }

            CancellationDeleteHandle(const CancellationDeleteHandle&) = delete;
            CancellationDeleteHandle& operator=(
                const CancellationDeleteHandle&) = delete;

            [[nodiscard]] bool Valid() const noexcept
            {
                return value_ != nullptr && value_ != INVALID_HANDLE_VALUE;
            }

            [[nodiscard]] HANDLE Get() const noexcept
            {
                return value_;
            }

            [[nodiscard]] HRESULT CloseNow() noexcept
            {
                const auto value = std::exchange(
                    value_, INVALID_HANDLE_VALUE);
                return CloseHandle(value)
                    ? S_OK
                    : HRESULT_FROM_WIN32(GetLastError());
            }

        private:
            HANDLE value_{ INVALID_HANDLE_VALUE };
        };

        HRESULT ReadCancellationDeleteFinalPath(
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
                    written == 0
                    ? GetLastError()
                    : ERROR_INSUFFICIENT_BUFFER);
            }
            finalPath.assign(buffer.data(), written);
            return S_OK;
        }

        HRESULT DeleteSessionFileWithOperationTimeEvidence(
            const std::filesystem::path& trustedRoot,
            const std::filesystem::path& candidatePath,
            const PersistentFileIdentity* const expectedIdentity) noexcept
        {
            try
            {
                if (trustedRoot.empty() || candidatePath.empty())
                {
                    return E_INVALIDARG;
                }

                CancellationDeleteHandle rootHandle(CreateFileW(
                    trustedRoot.c_str(),
                    FILE_READ_ATTRIBUTES,
                    FILE_SHARE_READ,
                    nullptr,
                    OPEN_EXISTING,
                    FILE_FLAG_BACKUP_SEMANTICS |
                        FILE_FLAG_OPEN_REPARSE_POINT,
                    nullptr));
                if (!rootHandle.Valid())
                {
                    return HRESULT_FROM_WIN32(GetLastError());
                }

                CancellationDeleteHandle candidateHandle(CreateFileW(
                    candidatePath.c_str(),
                    DELETE | FILE_READ_ATTRIBUTES,
                    FILE_SHARE_READ,
                    nullptr,
                    OPEN_EXISTING,
                    FILE_FLAG_OPEN_REPARSE_POINT,
                    nullptr));
                if (!candidateHandle.Valid())
                {
                    const auto error = GetLastError();
                    return error == ERROR_FILE_NOT_FOUND ||
                        error == ERROR_PATH_NOT_FOUND
                        ? S_OK
                        : HRESULT_FROM_WIN32(error);
                }

                BY_HANDLE_FILE_INFORMATION rootInformation{};
                BY_HANDLE_FILE_INFORMATION candidateInformation{};
                if (!GetFileInformationByHandle(
                        rootHandle.Get(), &rootInformation) ||
                    !GetFileInformationByHandle(
                        candidateHandle.Get(), &candidateInformation))
                {
                    return HRESULT_FROM_WIN32(GetLastError());
                }
                if ((rootInformation.dwFileAttributes &
                        FILE_ATTRIBUTE_DIRECTORY) == 0 ||
                    (rootInformation.dwFileAttributes &
                        FILE_ATTRIBUTE_REPARSE_POINT) != 0 ||
                    (candidateInformation.dwFileAttributes &
                        (FILE_ATTRIBUTE_DIRECTORY |
                            FILE_ATTRIBUTE_REPARSE_POINT)) != 0)
                {
                    return E_ACCESSDENIED;
                }
                SetLastError(NO_ERROR);
                const auto candidateType = GetFileType(candidateHandle.Get());
                if (candidateType != FILE_TYPE_DISK)
                {
                    return candidateType == FILE_TYPE_UNKNOWN &&
                        GetLastError() != NO_ERROR
                        ? HRESULT_FROM_WIN32(GetLastError())
                        : HRESULT_FROM_WIN32(ERROR_FILE_INVALID);
                }

                PersistentFileIdentity heldRootIdentity{};
                PersistentFileIdentity heldCandidateIdentity{};
                auto result = ReadPersistentFileIdentity(
                    rootHandle.Get(), heldRootIdentity);
                if (FAILED(result))
                {
                    return result;
                }
                result = ReadPersistentFileIdentity(
                    candidateHandle.Get(), heldCandidateIdentity);
                if (FAILED(result))
                {
                    return result;
                }
                if (heldCandidateIdentity.hardLinkCount != 1)
                {
                    return HRESULT_FROM_WIN32(ERROR_TOO_MANY_LINKS);
                }
                if (expectedIdentity != nullptr &&
                    !SamePersistentFileIdentity(
                        *expectedIdentity, heldCandidateIdentity))
                {
                    return HRESULT_FROM_WIN32(ERROR_FILE_INVALID);
                }

                std::wstring heldRootFinalPath;
                std::wstring heldCandidateFinalPath;
                result = ReadCancellationDeleteFinalPath(
                    rootHandle.Get(), heldRootFinalPath);
                if (FAILED(result))
                {
                    return result;
                }
                result = ReadCancellationDeleteFinalPath(
                    candidateHandle.Get(), heldCandidateFinalPath);
                if (FAILED(result))
                {
                    return result;
                }

                // Re-probe the complete path chain while both no-delete-share
                // handles remain held. The mutation below targets this same
                // candidate handle, never a later path lookup.
                const auto safety = InspectPathForReadOnly(
                    trustedRoot,
                    candidatePath,
                    PathSafetyExpectedType::RegularFile);
                if (!safety.SafeForReadOnlyInspection())
                {
                    return FAILED(safety.diagnosticHResult)
                        ? safety.diagnosticHResult
                        : E_ACCESSDENIED;
                }
                if (!SamePersistentFileIdentity(
                        heldRootIdentity, safety.trustedRootIdentity) ||
                    !SamePersistentFileIdentity(
                        heldCandidateIdentity, safety.candidateIdentity) ||
                    safety.candidateIdentity.hardLinkCount != 1 ||
                    _wcsicmp(
                        heldRootFinalPath.c_str(),
                        safety.trustedRootFinalPath.c_str()) != 0 ||
                    _wcsicmp(
                        heldCandidateFinalPath.c_str(),
                        safety.candidateFinalPath.c_str()) != 0)
                {
                    return HRESULT_FROM_WIN32(ERROR_FILE_INVALID);
                }

                BY_HANDLE_FILE_INFORMATION operationInformation{};
                if (!GetFileInformationByHandle(
                        candidateHandle.Get(), &operationInformation))
                {
                    return HRESULT_FROM_WIN32(GetLastError());
                }
                if ((operationInformation.dwFileAttributes &
                        (FILE_ATTRIBUTE_DIRECTORY |
                            FILE_ATTRIBUTE_REPARSE_POINT)) != 0)
                {
                    return HRESULT_FROM_WIN32(ERROR_FILE_INVALID);
                }
                PersistentFileIdentity operationIdentity{};
                result = ReadPersistentFileIdentity(
                    candidateHandle.Get(), operationIdentity);
                if (FAILED(result))
                {
                    return result;
                }
                std::wstring operationFinalPath;
                result = ReadCancellationDeleteFinalPath(
                    candidateHandle.Get(), operationFinalPath);
                if (FAILED(result))
                {
                    return result;
                }
                if (operationIdentity.hardLinkCount != 1 ||
                    !SamePersistentFileIdentity(
                        heldCandidateIdentity, operationIdentity) ||
                    !SamePersistentFileIdentity(
                        safety.candidateIdentity, operationIdentity) ||
                    (expectedIdentity != nullptr &&
                        !SamePersistentFileIdentity(
                            *expectedIdentity, operationIdentity)) ||
                    _wcsicmp(
                        operationFinalPath.c_str(),
                        safety.candidateFinalPath.c_str()) != 0)
                {
                    return HRESULT_FROM_WIN32(ERROR_FILE_INVALID);
                }
                PersistentFileIdentity operationRootIdentity{};
                result = ReadPersistentFileIdentity(
                    rootHandle.Get(), operationRootIdentity);
                if (FAILED(result))
                {
                    return result;
                }
                std::wstring operationRootFinalPath;
                result = ReadCancellationDeleteFinalPath(
                    rootHandle.Get(), operationRootFinalPath);
                if (FAILED(result) ||
                    !SamePersistentFileIdentity(
                        heldRootIdentity, operationRootIdentity) ||
                    !SamePersistentFileIdentity(
                        safety.trustedRootIdentity,
                        operationRootIdentity) ||
                    _wcsicmp(
                        operationRootFinalPath.c_str(),
                        safety.trustedRootFinalPath.c_str()) != 0)
                {
                    return FAILED(result)
                        ? result
                        : HRESULT_FROM_WIN32(ERROR_FILE_INVALID);
                }

                FILE_DISPOSITION_INFO disposition{};
                disposition.DeleteFile = TRUE;
                if (!SetFileInformationByHandle(
                        candidateHandle.Get(),
                        FileDispositionInfo,
                        &disposition,
                        sizeof(disposition)))
                {
                    return HRESULT_FROM_WIN32(GetLastError());
                }
                return candidateHandle.CloseNow();
            }
            catch (const std::bad_alloc&)
            {
                return E_OUTOFMEMORY;
            }
            catch (...)
            {
                return E_UNEXPECTED;
            }
        }

        class ConsumerRegistrationGuard final
        {
        public:
            explicit ConsumerRegistrationGuard(RenderFrameTap& tap) noexcept
                : tap_(&tap)
            {
            }

            ~ConsumerRegistrationGuard()
            {
                if (tap_ != nullptr)
                {
                    tap_->UnregisterConsumer(
                        RenderFrameTapConsumerKind::Encoder);
                }
            }

            void Release() noexcept
            {
                tap_ = nullptr;
            }

        private:
            RenderFrameTap* tap_{};
        };

        const wchar_t* FailureStageMessage(
            const WorkerFailureStage stage) noexcept
        {
            switch (stage)
            {
            case WorkerFailureStage::ComInitialization:
                return L"Encoder COM initialization failed.";
            case WorkerFailureStage::OutputReservation:
                return L"Recording output reservation failed.";
            case WorkerFailureStage::PipelineStartup:
                return L"Video pipeline startup failed.";
            case WorkerFailureStage::NoAcceptedFrames:
                return L"Recording stopped before an accepted frame.";
            case WorkerFailureStage::VideoConversion:
                return L"Video conversion failed.";
            case WorkerFailureStage::SampleCreation:
                return L"Encoded sample creation failed.";
            case WorkerFailureStage::SampleWrite:
                return L"Encoded sample write failed.";
            case WorkerFailureStage::Storage:
                return L"Recording stopped because the destination became unavailable or could not accept more data.";
            case WorkerFailureStage::Finalize:
                return L"Recording finalize failed.";
            case WorkerFailureStage::TrackedReturn:
                return L"Tracked encoder resources did not return.";
            case WorkerFailureStage::RuntimeValidation:
                return L"Final MP4 validation failed.";
            case WorkerFailureStage::WorkingFile:
                return L"Recording working file is missing or empty.";
            case WorkerFailureStage::Publish:
                return L"Recording output publish failed.";
            case WorkerFailureStage::BadAllocation:
                return L"Encoder worker allocation failed.";
            case WorkerFailureStage::StandardException:
                return L"Encoder worker raised a native exception.";
            case WorkerFailureStage::UnknownException:
                return L"Encoder worker raised an unknown exception.";
            case WorkerFailureStage::Join:
                return L"Encoder worker join failed.";
            default:
                return L"Recording failed.";
            }
        }

        const wchar_t* FailureMessage(
            const WorkerFailureStage stage,
            const HRESULT) noexcept
        {
            return FailureStageMessage(stage);
        }

        WorkerFailureStage FailureStageFromText(
            const std::string_view stage) noexcept
        {
            if (stage == "CoInitializeEx") return WorkerFailureStage::ComInitialization;
            if (stage == "ReserveOutput") return WorkerFailureStage::OutputReservation;
            if (stage == "StartVideoPipeline" || stage == "ValidateOutputGeometry")
                return WorkerFailureStage::PipelineStartup;
            if (stage == "NoAcceptedFrames") return WorkerFailureStage::NoAcceptedFrames;
            if (stage == "VideoProcessorBlt") return WorkerFailureStage::VideoConversion;
            if (stage == "CreateTrackedSample") return WorkerFailureStage::SampleCreation;
            if (stage == "WriteSample") return WorkerFailureStage::SampleWrite;
            if (stage == "WriteAudioSample") return WorkerFailureStage::SampleWrite;
            if (stage == "AudioCapture" || stage == "GStreamerAudioCapture")
                return WorkerFailureStage::PipelineStartup;
            if (stage == "Storage") return WorkerFailureStage::Storage;
            if (stage == "Finalize") return WorkerFailureStage::Finalize;
            if (stage == "TrackedReturnTimeout") return WorkerFailureStage::TrackedReturn;
            if (stage == "QuickRuntimeValidation") return WorkerFailureStage::RuntimeValidation;
            return WorkerFailureStage::StandardException;
        }

        const char* ManifestStateName(
            const SessionManifestState state) noexcept
        {
            switch (state)
            {
            case SessionManifestState::Created: return "Created";
            case SessionManifestState::Starting: return "Starting";
            case SessionManifestState::Recording: return "Recording";
            case SessionManifestState::Stopping: return "Stopping";
            case SessionManifestState::ReadyToPublish: return "ReadyToPublish";
            case SessionManifestState::Published: return "Published";
            case SessionManifestState::Completed: return "Completed";
            case SessionManifestState::Failed: return "Failed";
            case SessionManifestState::Unknown: return "Unknown";
            case SessionManifestState::ReconciledCompleted:
                return "ReconciledCompleted";
            case SessionManifestState::UserCancelled: return "UserCancelled";
            default: return "Unavailable";
            }
        }

        SessionManifestErrorCategory ManifestErrorCategory(
            const WorkerFailureStage stage) noexcept
        {
            switch (stage)
            {
            case WorkerFailureStage::Finalize:
                return SessionManifestErrorCategory::Finalize;
            case WorkerFailureStage::RuntimeValidation:
            case WorkerFailureStage::WorkingFile:
                return SessionManifestErrorCategory::Validation;
            case WorkerFailureStage::Publish:
                return SessionManifestErrorCategory::Publish;
            default:
                return SessionManifestErrorCategory::Recording;
            }
        }

        void RecordFailure(
            VideoEncoderDiagnostics& diagnostics,
            const char* const stage,
            const HRESULT result)
        {
            diagnostics.failureStage = stage;
            diagnostics.failureHResult = result;
            if (!IsVideoEncoderStateTransitionAllowed(
                    diagnostics.encoderState, VideoEncoderState::Failed))
            {
                ++diagnostics.invalidStateTransitionDetected;
            }
            diagnostics.encoderState = VideoEncoderState::Failed;
            diagnostics.outputSuccess = false;
        }

        bool IsUnsupportedResult(const HRESULT result) noexcept
        {
            return result == DXGI_ERROR_UNSUPPORTED ||
                result == MF_E_INVALIDMEDIATYPE || result == E_NOTIMPL;
        }

        std::int64_t UtcNow100ns() noexcept
        {
            FILETIME value{};
            GetSystemTimePreciseAsFileTime(&value);
            ULARGE_INTEGER combined{};
            combined.LowPart = value.dwLowDateTime;
            combined.HighPart = value.dwHighDateTime;
            return static_cast<std::int64_t>(combined.QuadPart);
        }

        HRESULT ReserveSessionOutput(
            const std::wstring& path,
            bool& ownedBySession) noexcept
        {
            ownedBySession = false;
            const auto file = CreateFileW(
                path.c_str(),
                GENERIC_WRITE,
                0,
                nullptr,
                CREATE_NEW,
                FILE_ATTRIBUTE_NORMAL,
                nullptr);
            if (file == INVALID_HANDLE_VALUE)
            {
                return HRESULT_FROM_WIN32(GetLastError());
            }
            ownedBySession = true;
            if (!CloseHandle(file))
            {
                return HRESULT_FROM_WIN32(GetLastError());
            }
            return S_OK;
        }

        bool IsStartupGarbageFailure(
            const WorkerFailureStage stage) noexcept
        {
            return stage == WorkerFailureStage::OutputReservation ||

                stage == WorkerFailureStage::PipelineStartup ||
                stage == WorkerFailureStage::BadAllocation ||
                stage == WorkerFailureStage::StandardException ||
                stage == WorkerFailureStage::UnknownException;
        }

        OutputCleanupOutcome DeleteStartupGarbage(
            const WorkerOutcome& worker,
            const bool published) noexcept
        {
            OutputCleanupOutcome result{};
            if (!worker.outputOwnedBySession ||
                worker.outputPath[0] == L'\0' || published ||
                worker.writeSampleAttempted || worker.framesSubmitted != 0 ||
                worker.validationAttempted ||
                !IsStartupGarbageFailure(worker.failureStage))
            {
                return result;
            }
            result.attempted = true;
            if (DeleteFileW(worker.outputPath))
            {
                result.succeeded = true;
                return result;
            }
            const auto error = GetLastError();
            if (error == ERROR_FILE_NOT_FOUND || error == ERROR_PATH_NOT_FOUND)
            {
                result.succeeded = true;
                return result;
            }
            result.hresult = HRESULT_FROM_WIN32(error);
            return result;
        }

        HRESULT ValidateWorkingFileForPublish(
            const wchar_t* const path) noexcept
        {
            if (path == nullptr || path[0] == L'\0')
            {
                return E_INVALIDARG;
            }
            WIN32_FILE_ATTRIBUTE_DATA attributes{};
            if (!GetFileAttributesExW(
                    path,
                    GetFileExInfoStandard,
                    &attributes))
            {
                return HRESULT_FROM_WIN32(GetLastError());
            }
            if ((attributes.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) != 0)
            {
                return HRESULT_FROM_WIN32(ERROR_FILE_INVALID);
            }
            ULARGE_INTEGER size{};
            size.HighPart = attributes.nFileSizeHigh;
            size.LowPart = attributes.nFileSizeLow;
            return size.QuadPart == 0
                ? HRESULT_FROM_WIN32(ERROR_FILE_INVALID)
                : S_OK;
        }

        PublishOutcome PublishSessionOutput(
            const wchar_t* const workingPath,
            const std::wstring& plannedFinalPath) noexcept
        {
            PublishOutcome result{};
            result.attempted = true;
            if (workingPath == nullptr || workingPath[0] == L'\0' ||
                plannedFinalPath.empty())
            {
                result.hresult = E_INVALIDARG;
                return result;
            }
            if (!MoveFileExW(
                    workingPath,
                    plannedFinalPath.c_str(),
                    MOVEFILE_WRITE_THROUGH))
            {
                result.hresult = HRESULT_FROM_WIN32(GetLastError());
                return result;
            }
            result.succeeded = true;
            result.hresult = S_OK;
            wcsncpy_s(
                result.publishedPath,
                plannedFinalPath.c_str(),
                _TRUNCATE);
            return result;
        }

        void CreatePublishConflictForTest(
            const std::wstring& plannedFinalPath) noexcept
        {
            constexpr char marker[] =
                "preexisting-final-must-not-be-overwritten";
            const auto file = CreateFileW(
                plannedFinalPath.c_str(),
                GENERIC_WRITE,
                0,
                nullptr,
                CREATE_NEW,
                FILE_ATTRIBUTE_NORMAL,
                nullptr);
            if (file == INVALID_HANDLE_VALUE)
            {
                return;
            }
            DWORD written{};
            (void)WriteFile(
                file,
                marker,
                static_cast<DWORD>(sizeof(marker) - 1),
                &written,
                nullptr);
            (void)CloseHandle(file);
        }
    }

    struct VideoEncoderConsumer::Impl
    {
        RenderFrameTap* tap{};
        winrt::com_ptr<ID3D11Device> device;
        winrt::com_ptr<ID3D11DeviceContext> context;
        VideoEncoderConfiguration configuration{};
        VideoDeviceSetupStatus deviceStatus{};
        VideoEncoderDiagnostics diagnostics{};
        VideoCadenceTraceBuffer cadenceTrace{};
        std::thread thread;
        std::atomic<bool> stopRequested{};
        std::atomic<bool> running{};
        std::atomic<RecordingTerminationDisposition> terminationDisposition{
            RecordingTerminationDisposition::Unspecified };
        VideoPauseWorkerControl pauseControl;
        AudioPauseWorkerControl audioPauseControl;
        std::atomic<std::int64_t> elapsed100ns{};
        std::atomic<bool> manifestRecordingBoundaryObserved{};
        mutable std::mutex snapshotMutex;
        std::mutex manifestMutex;
        std::mutex stopMutex;
        mutable std::mutex audioControlMutex;
        std::uint64_t audioControlRevision{};
        XbRecordingSnapshot snapshot{};
        WorkerOutcome outcome{};
        bool terminalPublished{};
        PersistentFileIdentity workingFileIdentity;
        SessionLifetimeOwner lifetimeOwner;
        GStreamerAudioCore audioCore;
        GStreamerAudioFinalizeResult audioFinalize{};
        std::filesystem::path audioFinalizePartialPath;
        std::filesystem::path audioVideoBackupPath;
        SessionLifetimeOwnerAcquireResult lifetimeOwnerAcquisition;
        std::unique_ptr<SessionManifestStore> manifestStore;
        SessionManifest manifest{};
        bool manifestWritable{};
        bool manifestStoppingPersisted{};
        bool cadenceTraceWritten{};
        std::chrono::steady_clock::time_point nextStorageCheck{};

        void PersistAudioFinalizeStderr() noexcept
        {
            if (!manifestStore || audioFinalize.stderrText.empty())
            {
                return;
            }
            const auto path = manifestStore->SessionDirectory() /
                L"ffmpeg.stderr.txt";
            const auto file = CreateFileW(
                path.c_str(), GENERIC_WRITE, FILE_SHARE_READ, nullptr,
                CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
            if (file == INVALID_HANDLE_VALUE)
            {
                return;
            }
            DWORD written{};
            const auto size = static_cast<DWORD>((std::min)(
                audioFinalize.stderrText.size(),
                static_cast<std::size_t>((std::numeric_limits<DWORD>::max)())));
            (void)WriteFile(
                file, audioFinalize.stderrText.data(), size,
                &written, nullptr);
            (void)FlushFileBuffers(file);
            (void)CloseHandle(file);
        }

        HRESULT PrepareGStreamerAudioFinalCandidate() noexcept
        {
            if (!configuration.audioEnabled)
            {
                return S_OK;
            }
            try
            {
                if (!manifestStore)
                {
                    return E_UNEXPECTED;
                }
                const auto audio = audioCore.Snapshot();
                if (!audio.filesClosed || FAILED(audio.terminalHResult))
                {
                    return FAILED(audio.terminalHResult)
                        ? audio.terminalHResult
                        : HRESULT_FROM_WIN32(ERROR_NOT_READY);
                }
                const auto workingPath =
                    std::filesystem::path(configuration.workingPath);
                std::error_code error;
                const auto videoBytes = std::filesystem::file_size(
                    workingPath, error);
                if (error || videoBytes == 0)
                {
                    return HRESULT_FROM_WIN32(ERROR_FILE_INVALID);
                }
                ULARGE_INTEGER freeBytes{};
                if (!GetDiskFreeSpaceExW(
                        configuration.outputDirectory.c_str(),
                        &freeBytes, nullptr, nullptr))
                {
                    return HRESULT_FROM_WIN32(GetLastError());
                }
                if (!GStreamerAudioFinalizeStorageSufficient(
                        freeBytes.QuadPart, videoBytes))
                {
                    return HRESULT_FROM_WIN32(ERROR_DISK_FULL);
                }

                audioVideoBackupPath = manifestStore->SessionDirectory() /
                    L"video-gstreamer.intermediate.mp4";
                audioFinalizePartialPath =
                    std::filesystem::path(configuration.outputDirectory) /
                    (configuration.sessionId +
                        L".gstreamer-audio.partial.mp4");
                if (!CopyFileW(
                        workingPath.c_str(),
                        audioVideoBackupPath.c_str(),
                        TRUE))
                {
                    return HRESULT_FROM_WIN32(GetLastError());
                }

                GStreamerAudioFinalizeRequest request{};
                request.mode = audio.audioMode;
                request.videoPath = workingPath;
                request.systemFlacPath = audio.systemWorkingPath;
                request.microphoneFlacPath = audio.microphoneWorkingPath;
                request.outputPath = audioFinalizePartialPath;
                request.expectedDuration100ns = elapsed100ns.load();
                const auto durationMilliseconds =
                    request.expectedDuration100ns / 10'000;
                request.timeout = (std::max)(
                    std::chrono::duration_cast<std::chrono::milliseconds>(
                        std::chrono::minutes(5)),
                    std::chrono::milliseconds(durationMilliseconds * 2));
                // Exact 4fc3757 boundary: GStreamer owns capture, WebRTC
                // microphone processing and lossless source tracks. FFmpeg
                // owns file mastering, Dual mixing, AAC and H.264 stream copy.
                audioFinalize = FinalizeGStreamerAudio(request);
                PersistAudioFinalizeStderr();
                if (FAILED(audioFinalize.hresult))
                {
                    return audioFinalize.hresult;
                }
                if (!MoveFileExW(
                        audioFinalizePartialPath.c_str(),
                        workingPath.c_str(),
                        MOVEFILE_REPLACE_EXISTING |
                            MOVEFILE_WRITE_THROUGH))
                {
                    return HRESULT_FROM_WIN32(GetLastError());
                }
                return S_OK;
            }
            catch (const std::bad_alloc&)
            {
                return E_OUTOFMEMORY;
            }
            catch (...)
            {
                return E_FAIL;
            }
        }

        void CleanupGStreamerAudioMaterialsAfterPublish() noexcept
        {
#if defined(XBPREVIEW_NATIVE_TESTS)
            if (configuration.lifecycleTestHooks != nullptr &&
                configuration.lifecycleTestHooks->
                    retainGStreamerAudioProofMaterials)
            {
                return;
            }
#endif
            if (!configuration.audioEnabled)
            {
                return;
            }
            const auto audio = audioCore.Snapshot();
            if (!audio.systemWorkingPath.empty())
            {
                (void)DeleteFileW(audio.systemWorkingPath.c_str());
            }
            if (!audio.microphoneWorkingPath.empty())
            {
                (void)DeleteFileW(audio.microphoneWorkingPath.c_str());
            }
            if (!audioVideoBackupPath.empty())
            {
                (void)DeleteFileW(audioVideoBackupPath.c_str());
            }
        }

        OutputCleanupOutcome CleanupUserCancelledMaterials(
            const WorkerOutcome& worker,
            const PersistentFileIdentityCapture& persistedIdentity) noexcept
        {
            OutputCleanupOutcome result{};
            result.attempted = true;
            result.succeeded = true;

            const auto recordFailure = [&result](const HRESULT hresult) noexcept
            {
                if (result.succeeded)
                {
                    result.hresult = FAILED(hresult) ? hresult : E_FAIL;
                }
                result.succeeded = false;
            };
            const auto deleteExactPath =
                [&recordFailure](
                    const std::filesystem::path& actual,
                    const std::filesystem::path& expected,
                    const std::filesystem::path& plannedFinal,
                    const std::filesystem::path& trustedRoot,
                    const PersistentFileIdentity* const
                        expectedIdentity) noexcept
            {
                if (actual.empty())
                {
                    return;
                }
                if (!SameExactWindowsPath(actual, expected) ||
                    SameExactWindowsPath(actual, plannedFinal))
                {
                    recordFailure(HRESULT_FROM_WIN32(ERROR_INVALID_DATA));
                    return;
                }
                const auto deletionResult =
                    DeleteSessionFileWithOperationTimeEvidence(
                        trustedRoot,
                        actual,
                        expectedIdentity);
                if (FAILED(deletionResult))
                {
                    recordFailure(deletionResult);
                }
            };

            try
            {
                const auto configuredWorkingPath =
                    std::filesystem::path(configuration.workingPath);
                const auto plannedFinalPath =
                    std::filesystem::path(configuration.plannedFinalPath);
                const auto roots =
                    ResolveRecordingOutputRootsFromManagedRoot(
                        std::filesystem::path(
                            configuration.outputDirectory));
                if (!roots.Succeeded())
                {
                    recordFailure(FAILED(roots.hresult)
                        ? roots.hresult
                        : HRESULT_FROM_WIN32(ERROR_INVALID_STATE));
                    return result;
                }
                const auto expectedSessionDirectory =
                    roots.sessionsRoot / configuration.sessionId;
                const auto expectedOwnerPath = expectedSessionDirectory /
                    SessionLifetimeOwnerFileName;
                std::filesystem::path sessionDirectory;
                {
                    std::lock_guard manifestLock(manifestMutex);
                    if (!manifestStore ||
                        !lifetimeOwner.Acquired() ||
                        !SameExactWindowsPath(
                            lifetimeOwner.OwnerPath(), expectedOwnerPath) ||
                        manifest.schemaVersion !=
                            SessionManifestSchemaVersion ||
                        manifest.state !=
                            SessionManifestState::UserCancelled ||
                        manifest.sessionId != configuration.sessionId ||
                        !SameExactWindowsPath(
                            std::filesystem::path(manifest.workingPath),
                            configuredWorkingPath) ||
                        !SameExactWindowsPath(
                            std::filesystem::path(manifest.plannedFinalPath),
                            plannedFinalPath) ||
                        !manifest.workingFileOwnedBySession ||
                        !manifest.workingFileIdentity.attempted ||
                        manifest.workingFileIdentity.captured !=
                            persistedIdentity.Succeeded())
                    {
                        recordFailure(
                            HRESULT_FROM_WIN32(ERROR_INVALID_STATE));
                        return result;
                    }
                    sessionDirectory = manifestStore->SessionDirectory();
                }
                if (!SameExactWindowsPath(
                        sessionDirectory, expectedSessionDirectory))
                {
                    recordFailure(HRESULT_FROM_WIN32(ERROR_INVALID_DATA));
                    return result;
                }

                const auto workingPath =
                    std::filesystem::path(worker.outputPath);
                if (!worker.outputOwnedBySession || workingPath.empty() ||
                    !SameExactWindowsPath(
                        workingPath, configuredWorkingPath) ||
                    SameExactWindowsPath(workingPath, plannedFinalPath))
                {
                    recordFailure(HRESULT_FROM_WIN32(ERROR_INVALID_DATA));
                }
                else if (!persistedIdentity.Succeeded())
                {
                    recordFailure(FAILED(persistedIdentity.hresult)
                        ? persistedIdentity.hresult
                        : HRESULT_FROM_WIN32(ERROR_NOT_READY));
                }
                else
                {
                    deleteExactPath(
                        workingPath,
                        configuredWorkingPath,
                        plannedFinalPath,
                        roots.mediaOutputRoot,
                        &persistedIdentity.identity);
                }

                if (configuration.audioEnabled)
                {
                    const auto audio = audioCore.Snapshot();
                    deleteExactPath(
                        audio.systemWorkingPath,
                        sessionDirectory / L"system.flac",
                        plannedFinalPath,
                        roots.sessionsRoot,
                        nullptr);
                    deleteExactPath(
                        audio.microphoneWorkingPath,
                        sessionDirectory / L"mic.flac",
                        plannedFinalPath,
                        roots.sessionsRoot,
                        nullptr);
                }
                deleteExactPath(
                    audioFinalizePartialPath,
                    std::filesystem::path(configuration.outputDirectory) /
                        (configuration.sessionId +
                            L".gstreamer-audio.partial.mp4"),
                    plannedFinalPath,
                    roots.mediaOutputRoot,
                    nullptr);
                deleteExactPath(
                    audioVideoBackupPath,
                    sessionDirectory / L"video-gstreamer.intermediate.mp4",
                    plannedFinalPath,
                    roots.sessionsRoot,
                    nullptr);
            }
            catch (const std::bad_alloc&)
            {
                recordFailure(E_OUTOFMEMORY);
            }
            catch (...)
            {
                recordFailure(E_UNEXPECTED);
            }
            return result;
        }

        bool GStreamerAudioCleanupAllowed() noexcept
        {
            std::lock_guard lock(manifestMutex);
            return manifestWritable &&
                manifest.state == SessionManifestState::Completed &&
                manifest.postPublishIdentityVerification.matched;
        }

        void UpdateAudioCaptureDiagnostics() noexcept
        {
            const auto audio = audioCore.Snapshot();
            diagnostics.audioBackend = configuration.audioEnabled
                ? "GStreamer-1.28.6/wasapi2src/WebRTC-DSP/FFmpeg"
                : "Disabled";
            diagnostics.audioMode = GStreamerAudioModeName(audio.audioMode);
            diagnostics.gStreamerAudioMode =
                GStreamerAudioModeName(audio.audioMode);
            diagnostics.gStreamerSystemActive = audio.systemActive;
            diagnostics.gStreamerMicrophoneActive = audio.micActive;
            diagnostics.gStreamerMicrophoneDeviceId = audio.micDeviceId;
            diagnostics.gStreamerMicrophoneDeviceDisplayName =
                audio.micDeviceDisplayName;
            diagnostics.gStreamerMicrophoneDeviceProperties =
                audio.micDeviceProperties;
            diagnostics.gStreamerMicrophoneElementDeviceId =
                audio.micElementDeviceId;
            diagnostics.gStreamerMicrophoneSessionBound =
                audio.micSessionBound;
            diagnostics.gStreamerMicrophoneSourceCreatedFromDevice =
                audio.micSourceCreatedFromDevice;
            diagnostics.gStreamerMicrophoneElementIdentityMatches =
                audio.micElementIdentityMatches;
            diagnostics.micDisconnectedDuringRecording =
                audio.micDisconnected;
            diagnostics.gStreamerMicrophoneSourceDataBlocked =
                audio.micSourceDataBlocked;
            diagnostics.gStreamerPipelineState =
                GStreamerAudioPipelineStateName(audio.pipelineState);
            diagnostics.gStreamerLastError = audio.lastGStreamerError;
            diagnostics.gStreamerAudioWorkingPath =
                audio.audioWorkingPath.wstring();
            diagnostics.gStreamerSystemWorkingPath =
                audio.systemWorkingPath.wstring();
            diagnostics.gStreamerMicrophoneWorkingPath =
                audio.microphoneWorkingPath.wstring();
            diagnostics.gStreamerTerminalHResult = audio.terminalHResult;
            diagnostics.gStreamerDeviceMonitorActive =
                audio.deviceMonitorActive;
            diagnostics.gStreamerEndOfStreamObserved =
                audio.endOfStreamObserved;
            diagnostics.gStreamerFilesClosed = audio.filesClosed;
            diagnostics.gStreamerBusThreadExited = audio.busThreadExited;
            diagnostics.gStreamerMixerVolumesFixedAtUnity =
                audio.mixerVolumesFixedAtUnity;
            diagnostics.gStreamerDualSourcesIndependent =
                audio.dualSourcesIndependent;
            diagnostics.outputFormat = configuration.audioEnabled
                ? "MP4/H264-NV12+AAC-GSTREAMER-FLAC-48K"
                : "MP4/H264-NV12";
        }

        Impl()
        {
            snapshot.structSize = sizeof(snapshot);
            snapshot.apiVersion = XB_PREVIEW_API_VERSION;
            snapshot.state = XbRecordingState_Idle;
            snapshot.lastResult = XbPreviewResult_Ok;
            snapshot.finalizeHResult = E_PENDING;
            snapshot.failureHResult = S_OK;
            snapshot.outputCleanupHResult = S_OK;
            snapshot.publishHResult = E_PENDING;
            snapshot.validationHResult = E_PENDING;
        }

        bool Active() const
        {
            std::lock_guard lock(snapshotMutex);
            return snapshot.state == XbRecordingState_Starting ||
                snapshot.state == XbRecordingState_Recording ||
                snapshot.state == XbRecordingState_Pausing ||
                snapshot.state == XbRecordingState_Paused ||
                snapshot.state == XbRecordingState_Resuming ||
                snapshot.state == XbRecordingState_Stopping;
        }

        RecordingTerminationDisposition ClaimTerminationDisposition(
            const RecordingTerminationDisposition requested) noexcept
        {
            auto expected = RecordingTerminationDisposition::Unspecified;
            if (terminationDisposition.compare_exchange_strong(
                    expected,
                    requested,
                    std::memory_order_acq_rel,
                    std::memory_order_acquire))
            {
                return requested;
            }
            return expected;
        }

        [[nodiscard]] bool UserCancellationWon() const noexcept
        {
            return terminationDisposition.load(std::memory_order_acquire) ==
                RecordingTerminationDisposition::UserCancelled;
        }

        void PrepareSnapshot(const VideoEncoderConfiguration& value)
        {
            std::lock_guard lock(snapshotMutex);
            snapshot = {};
            snapshot.structSize = sizeof(snapshot);
            snapshot.apiVersion = XB_PREVIEW_API_VERSION;
            snapshot.state = XbRecordingState_Starting;
            snapshot.lastResult = XbPreviewResult_Ok;
            snapshot.finalizeHResult = E_PENDING;
            snapshot.failureHResult = S_OK;
            snapshot.outputCleanupHResult = S_OK;
            snapshot.publishHResult = E_PENDING;
            snapshot.validationHResult = E_PENDING;
            snapshot.activeEncoder = 1;
            wcsncpy_s(snapshot.sessionId, value.sessionId.c_str(), _TRUNCATE);
            // P2.6A-2 keeps the legacy OutputPath ABI as the actual direct
            // output for compatibility. It therefore aliases WorkingPath
            // until native publish is introduced; it is never the planned
            // final path used to configure the SinkWriter.
            wcsncpy_s(
                snapshot.outputPath,
                value.workingPath.c_str(),
                _TRUNCATE);
            wcsncpy_s(
                snapshot.workingPath,
                value.workingPath.c_str(),
                _TRUNCATE);
            wcsncpy_s(
                snapshot.plannedFinalPath,
                value.plannedFinalPath.c_str(),
                _TRUNCATE);
            elapsed100ns.store(0);
            outcome = {};
            terminalPublished = false;
            terminationDisposition.store(
                RecordingTerminationDisposition::Unspecified,
                std::memory_order_release);
            workingFileIdentity = {};
            audioFinalize = {};
            audioFinalizePartialPath.clear();
            audioVideoBackupPath.clear();
            lifetimeOwnerAcquisition = {};
        }

#if defined(XBPREVIEW_NATIVE_TESTS)
        static void ObserveLifecycleBoundary(
            const HANDLE reached,
            const HANDLE continueEvent) noexcept
        {
            if (reached != nullptr) (void)SetEvent(reached);
            if (continueEvent != nullptr)
            {

                (void)WaitForSingleObject(continueEvent, 30'000);
            }
        }
#endif

        void ReleaseLifetimeOwner() noexcept
        {
            lifetimeOwner.Release();
        }

        void RecordManifestResult(
            const HRESULT result,
            const bool created) noexcept
        {
            ++diagnostics.manifestWriteAttempts;
            if (SUCCEEDED(result))
            {
                ++diagnostics.manifestWriteSuccesses;
                diagnostics.manifestCreated =
                    diagnostics.manifestCreated || created;
                diagnostics.manifestLastPersistedRevision = manifest.revision;
                diagnostics.manifestLastPersistedState =
                    ManifestStateName(manifest.state);
                return;
            }
            ++diagnostics.manifestWriteFailures;
            if (SUCCEEDED(diagnostics.manifestFirstFailureHResult))
            {
                diagnostics.manifestFirstFailureHResult = result;
            }
            diagnostics.manifestLastFailureHResult = result;
            // Manifest persistence is best effort for media safety. A failed
            // revision disables later writes for this Session so no higher
            // state can skip or overwrite the last durable revision.
            manifestWritable = false;
        }

        void PrepareManifest(const VideoEncoderConfiguration& value) noexcept
        {
            std::lock_guard lock(manifestMutex);
            manifestStore.reset();
            lifetimeOwner.Release();
            manifest = {};
            manifestWritable = false;
            manifestStoppingPersisted = false;
            manifestRecordingBoundaryObserved.store(false);
            diagnostics.manifestEnabled = value.publishOnStop;
            diagnostics.manifestPath.clear();
            diagnostics.manifestCreated = false;
            diagnostics.manifestWriteAttempts = 0;
            diagnostics.manifestWriteSuccesses = 0;
            diagnostics.manifestWriteFailures = 0;
            diagnostics.manifestLastPersistedRevision = 0;
            diagnostics.manifestLastPersistedState = "Unavailable";
            diagnostics.manifestFirstFailureHResult = S_OK;
            diagnostics.manifestLastFailureHResult = S_OK;
            if (!value.publishOnStop)
            {
                return;
            }
            try
            {
                manifestStore = std::make_unique<SessionManifestStore>(
                    std::filesystem::path(value.outputDirectory),
                    value.sessionId);
                diagnostics.manifestPath =
                    manifestStore->ManifestPath().wstring();
                manifest.sessionId = value.sessionId;
                manifest.workingPath = value.workingPath;
                manifest.plannedFinalPath = value.plannedFinalPath;
                manifest.state = SessionManifestState::Created;
                auto result = manifestStore->CreateManifest(manifest);
                manifestWritable = SUCCEEDED(result);
                RecordManifestResult(result, true);
                if (!manifestWritable)
                {
                    return;
                }
                const auto roots =
                    ResolveRecordingOutputRootsFromManagedRoot(
                        std::filesystem::path(value.outputDirectory));
#if defined(XBPREVIEW_NATIVE_TESTS)
                if (value.lifecycleTestHooks != nullptr &&
                    value.lifecycleTestHooks->forceLifetimeOwnerAcquireFailure)
                {
                    lifetimeOwnerAcquisition.status =
                        SessionLifetimeOwnerAcquireStatus::Unavailable;
                    lifetimeOwnerAcquisition.diagnosticHResult =
                        HRESULT_FROM_WIN32(ERROR_ACCESS_DENIED);
                }
                else
#endif
                {
                    lifetimeOwnerAcquisition = lifetimeOwner.Acquire(
                        roots, value.sessionId);
                }
                diagnostics.lifetimeOwnerAcquireAttempted = true;
                diagnostics.lifetimeOwnerAcquired =
                    lifetimeOwnerAcquisition.Acquired();
                diagnostics.lifetimeOwnerAcquireHResult =
                    lifetimeOwnerAcquisition.diagnosticHResult;
                diagnostics.lifetimeOwnerPath =
                    lifetimeOwnerAcquisition.ownerPath.wstring();
                manifest.state = SessionManifestState::Starting;
                result = manifestStore->UpdateManifest(manifest);
                RecordManifestResult(result, false);
            }
            catch (const std::bad_alloc&)
            {
                RecordManifestResult(E_OUTOFMEMORY, false);
            }
            catch (...)
            {
                RecordManifestResult(E_UNEXPECTED, false);
            }
        }

        void PersistManifestWorkingOwned() noexcept
        {
            std::lock_guard lock(manifestMutex);
            if (!manifestWritable || manifest.workingFileOwnedBySession)
            {
                return;
            }
            manifest.workingFileOwnedBySession = true;
            const auto result = manifestStore->UpdateManifest(manifest);
            RecordManifestResult(result, false);
        }

        void PersistManifestRecording() noexcept
        {
            if (manifestRecordingBoundaryObserved.exchange(true))
            {
                return;
            }
            std::lock_guard lock(manifestMutex);
            if (!manifestWritable)
            {
                return;
            }
            manifest.writeSampleAttempted = true;
            manifest.frameSubmitted = true;
            if (manifest.state == SessionManifestState::Starting)
            {
                manifest.state = SessionManifestState::Recording;
            }
            const auto result = manifestStore->UpdateManifest(manifest);
            RecordManifestResult(result, false);
        }

        void PersistManifestStopping() noexcept
        {
            std::lock_guard lock(manifestMutex);
            if (!manifestWritable || manifestStoppingPersisted)
            {
                return;
            }
            if (manifest.state == SessionManifestState::Starting ||
                manifest.state == SessionManifestState::Recording)
            {
                manifest.state = SessionManifestState::Stopping;
            }
            const auto result = manifestStore->UpdateManifest(manifest);
            if (SUCCEEDED(result))
            {
                manifestStoppingPersisted = true;
            }
            RecordManifestResult(result, false);
        }

        static void ApplyManifestWorkerFacts(
            SessionManifest& target,
            const WorkerOutcome& worker) noexcept
        {
            target.workingFileOwnedBySession =
                target.workingFileOwnedBySession ||
                worker.outputOwnedBySession;
            target.writeSampleAttempted =
                target.writeSampleAttempted ||
                worker.writeSampleAttempted;
            target.frameSubmitted =
                target.frameSubmitted || worker.framesSubmitted != 0;
            target.workerExited = worker.workerExited;
            target.residualOutstanding = worker.residualOutstanding;
            target.recordingResourcesReleased = worker.workerExited &&
                worker.residualOutstanding == 0;
            target.finalize.attempted = worker.finalizeAttempted;
            target.finalize.count = worker.finalizeCount;
            target.finalize.hresult = worker.finalizeAttempted
                ? std::optional<HRESULT>(worker.finalizeHResult)
                : std::nullopt;
            target.validation.attempted = worker.validationAttempted;
            target.validation.passed = worker.validationAttempted &&
                SUCCEEDED(worker.validationHResult);
            target.validation.hresult = worker.validationAttempted
                ? std::optional<HRESULT>(worker.validationHResult)
                : std::nullopt;
        }

        void ApplyManifestWorkerFacts(const WorkerOutcome& worker) noexcept
        {
            ApplyManifestWorkerFacts(manifest, worker);
        }

        void PersistManifestReady(const WorkerOutcome& worker) noexcept
        {
            std::lock_guard lock(manifestMutex);
            if (!manifestWritable)
            {
                return;
            }
            ApplyManifestWorkerFacts(worker);
            PersistentFileIdentityCapture capture{};
            if (configuration.faultInjection ==
                VideoEncoderFaultInjection::WorkingIdentityCaptureFailure)
            {
                capture.hresult = HRESULT_FROM_WIN32(ERROR_ACCESS_DENIED);
            }
            else
            {
                capture = CapturePersistentFileIdentity(
                    std::filesystem::path(worker.outputPath));
            }
            manifest.workingFileIdentity.attempted = true;
            manifest.workingFileIdentity.captured = capture.Succeeded();
            manifest.workingFileIdentity.hresult = capture.hresult;
            manifest.workingFileIdentity.volumeIdentity = capture.Succeeded()
                ? FormatVolumeIdentityCanonical(
                    capture.identity.volumeSerialNumber)
                : L"";
            manifest.workingFileIdentity.fileId = capture.Succeeded()
                ? FormatFileIdCanonical(capture.identity.fileId)
                : L"";
            workingFileIdentity = capture.Succeeded()
                ? capture.identity
                : PersistentFileIdentity{};
            manifest.state = SessionManifestState::ReadyToPublish;
            const auto result = manifestStore->UpdateManifest(manifest);
            RecordManifestResult(result, false);
        }

        void PersistManifestPublished(
            const WorkerOutcome& worker,
            const PublishOutcome& publish,
            const PersistentFileIdentityCapture& publishedIdentity) noexcept
        {
            std::lock_guard lock(manifestMutex);
            if (!manifestWritable)
            {
                return;
            }
            try
            {
                ApplyManifestWorkerFacts(worker);
                manifest.publish.attempted = publish.attempted;
                manifest.publish.published = publish.succeeded;
                manifest.publish.hresult = publish.attempted
                    ? std::optional<HRESULT>(publish.hresult)
                    : std::nullopt;
                manifest.publishedPath = publish.succeeded
                    ? publish.publishedPath
                    : L"";
                manifest.postPublishIdentityVerification.attempted = true;
                manifest.postPublishIdentityVerification.matched =
                    publishedIdentity.Succeeded() &&
                    SamePersistentFileIdentity(
                        workingFileIdentity,
                        publishedIdentity.identity);
                manifest.postPublishIdentityVerification.hresult =
                    publishedIdentity.hresult;
                manifest.state = SessionManifestState::Published;
                auto result = manifestStore->UpdateManifest(manifest);
                RecordManifestResult(result, false);
                if (!manifestWritable)
                {
                    return;
                }
                manifest.state = SessionManifestState::Completed;
                result = manifestStore->UpdateManifest(manifest);
                RecordManifestResult(result, false);
            }
            catch (const std::bad_alloc&)
            {
                RecordManifestResult(E_OUTOFMEMORY, false);
            }
            catch (...)
            {
                RecordManifestResult(E_UNEXPECTED, false);
            }
        }

        void PersistManifestFailed(
            const WorkerOutcome& worker,
            const bool readyToPublish,
            const PublishOutcome& publish) noexcept
        {
            std::lock_guard lock(manifestMutex);
            if (!manifestWritable)
            {
                return;
            }
            try
            {
                ApplyManifestWorkerFacts(worker);
                if (publish.attempted)
                {
                    manifest.publish.attempted = true;

                    manifest.publish.published = false;
                    manifest.publish.hresult = publish.hresult;
                }
                const auto stage = readyToPublish && publish.attempted
                    ? WorkerFailureStage::Publish
                    : worker.failureStage;
                const auto failure = readyToPublish && publish.attempted
                    ? publish.hresult
                    : worker.failureHResult;
                manifest.state = SessionManifestState::Failed;
                manifest.errorCategory = ManifestErrorCategory(stage);
                manifest.errorCode = failure;
                manifest.errorMessage = FailureMessage(stage, failure);
                const auto result = manifestStore->UpdateManifest(manifest);
                RecordManifestResult(result, false);
            }
            catch (const std::bad_alloc&)
            {
                RecordManifestResult(E_OUTOFMEMORY, false);
            }
            catch (...)
            {
                RecordManifestResult(E_UNEXPECTED, false);
            }
        }

        HRESULT PersistManifestUserCancelled(
            const WorkerOutcome& worker,
            PersistentFileIdentityCapture& identityCapture) noexcept
        {
            identityCapture = {};
            std::lock_guard lock(manifestMutex);
            if (!manifestWritable || !manifestStore)
            {
                return FAILED(diagnostics.manifestLastFailureHResult)
                    ? diagnostics.manifestLastFailureHResult
                    : HRESULT_FROM_WIN32(ERROR_WRITE_FAULT);
            }
            try
            {
                const auto workingPath =
                    std::filesystem::path(worker.outputPath);
                const auto configuredWorkingPath =
                    std::filesystem::path(configuration.workingPath);
                const auto plannedFinalPath =
                    std::filesystem::path(configuration.plannedFinalPath);
                if (!worker.outputOwnedBySession || workingPath.empty() ||
                    !SameExactWindowsPath(
                        workingPath, configuredWorkingPath) ||
                    manifest.sessionId != configuration.sessionId ||
                    !SameExactWindowsPath(
                        std::filesystem::path(manifest.workingPath),
                        configuredWorkingPath) ||
                    !SameExactWindowsPath(
                        std::filesystem::path(manifest.plannedFinalPath),
                        plannedFinalPath) ||
                    SameExactWindowsPath(workingPath, plannedFinalPath))
                {
                    return HRESULT_FROM_WIN32(ERROR_INVALID_DATA);
                }

                if (configuration.faultInjection ==
                    VideoEncoderFaultInjection::
                        WorkingIdentityCaptureFailure)
                {
                    identityCapture.hresult =
                        HRESULT_FROM_WIN32(ERROR_ACCESS_DENIED);
                }
                else
                {
                    identityCapture = CapturePersistentFileIdentity(
                        workingPath);
                }
                if (!identityCapture.Succeeded() &&
                    SUCCEEDED(identityCapture.hresult))
                {
                    identityCapture.hresult =
                        HRESULT_FROM_WIN32(ERROR_NOT_READY);
                }

                auto candidate = manifest;
                ApplyManifestWorkerFacts(candidate, worker);
                candidate.schemaVersion = SessionManifestSchemaVersion;
                candidate.workingFileOwnedBySession = true;
                candidate.publish = {};
                candidate.publishedPath.clear();
                candidate.workingFileIdentity.attempted = true;
                candidate.workingFileIdentity.captured =
                    identityCapture.Succeeded();
                candidate.workingFileIdentity.hresult =
                    identityCapture.hresult;
                candidate.workingFileIdentity.volumeIdentity =
                    identityCapture.Succeeded()
                    ? FormatVolumeIdentityCanonical(
                        identityCapture.identity.volumeSerialNumber)
                    : L"";
                candidate.workingFileIdentity.fileId =
                    identityCapture.Succeeded()
                    ? FormatFileIdCanonical(identityCapture.identity.fileId)
                    : L"";
                candidate.postPublishIdentityVerification = {};
                candidate.errorCategory =
                    SessionManifestErrorCategory::None;
                candidate.errorCode.reset();
                candidate.errorMessage.clear();
                candidate.state = SessionManifestState::UserCancelled;
                const auto result = manifestStore->UpdateManifest(candidate);
                if (SUCCEEDED(result))
                {
                    manifest = std::move(candidate);
                    workingFileIdentity = identityCapture.Succeeded()
                        ? identityCapture.identity
                        : PersistentFileIdentity{};
                }
                RecordManifestResult(result, false);
                return result;
            }
            catch (const std::bad_alloc&)
            {
                RecordManifestResult(E_OUTOFMEMORY, false);
                return E_OUTOFMEMORY;
            }
            catch (...)
            {
                RecordManifestResult(E_UNEXPECTED, false);
                return E_UNEXPECTED;
            }
        }

        void PersistManifestStartFailure(
            const HRESULT hresult,
            const wchar_t* const message) noexcept
        {
            std::lock_guard lock(manifestMutex);
            if (!manifestWritable)
            {
                return;
            }
            try
            {
                manifest.state = SessionManifestState::Failed;
                manifest.errorCategory =
                    SessionManifestErrorCategory::Recording;
                manifest.errorCode = hresult;
                manifest.errorMessage = message == nullptr
                    ? L"Recording start failed."
                    : message;
                const auto result = manifestStore->UpdateManifest(manifest);
                RecordManifestResult(result, false);
            }
            catch (const std::bad_alloc&)
            {
                RecordManifestResult(E_OUTOFMEMORY, false);
            }
            catch (...)
            {
                RecordManifestResult(E_UNEXPECTED, false);
            }
        }

        void PublishRecordingStarted(const std::wstring& workingPath)
        {
            std::lock_guard lock(snapshotMutex);
            if (snapshot.startUtc100ns == 0)
            {
                snapshot.startUtc100ns = UtcNow100ns();
            }
            if (snapshot.state == XbRecordingState_Starting)
            {
                snapshot.state = XbRecordingState_Recording;
            }
            wcsncpy_s(snapshot.outputPath, workingPath.c_str(), _TRUNCATE);
            wcsncpy_s(snapshot.workingPath, workingPath.c_str(), _TRUNCATE);
        }

        [[nodiscard]] bool PublishPauseRequested()
        {
            std::lock_guard lock(snapshotMutex);
            if (snapshot.state != XbRecordingState_Recording)
            {
                return false;
            }
            snapshot.state = XbRecordingState_Pausing;
            return true;
        }

        void PublishPauseAcknowledged()
        {
            std::lock_guard lock(snapshotMutex);
            if (snapshot.state != XbRecordingState_Pausing)
            {
                return;
            }
            snapshot.state = XbRecordingState_Paused;
            ++snapshot.pauseCount;
        }

        [[nodiscard]] bool PublishResumeRequested()
        {
            std::lock_guard lock(snapshotMutex);
            if (snapshot.state != XbRecordingState_Paused)
            {
                return false;
            }
            snapshot.state = XbRecordingState_Resuming;
            return true;
        }

        void PublishResumeAcknowledged(
            const std::int64_t totalPausedDuration100ns)
        {
            std::lock_guard lock(snapshotMutex);
            if (snapshot.state != XbRecordingState_Resuming)
            {
                return;
            }
            snapshot.totalPaused100ns = static_cast<std::uint64_t>(
                (std::max)(std::int64_t{}, totalPausedDuration100ns));
            snapshot.state = XbRecordingState_Recording;
        }

        void PublishStopping()
        {
            std::lock_guard lock(snapshotMutex);
            if (snapshot.state == XbRecordingState_Starting ||
                snapshot.state == XbRecordingState_Recording ||
                snapshot.state == XbRecordingState_Pausing ||
                snapshot.state == XbRecordingState_Paused ||
                snapshot.state == XbRecordingState_Resuming)
            {
                snapshot.state = XbRecordingState_Stopping;
            }
        }

        void PublishStorageStatus(
            const RecordingStorageStatus status,
            const HRESULT hresult,
            const bool stopping)
        {
            if (stopping)
            {
                // A native storage stop is already a terminal action. Claim
                // the normal disposition before exposing Stopping so a later
                // user cancellation cannot convert it into a discard.
                (void)ClaimTerminationDisposition(
                    RecordingTerminationDisposition::Publish);
            }
            std::lock_guard lock(snapshotMutex);
            if (stopping &&
                (snapshot.state == XbRecordingState_Starting ||
                    snapshot.state == XbRecordingState_Recording ||
                    snapshot.state == XbRecordingState_Pausing ||
                    snapshot.state == XbRecordingState_Paused ||
                    snapshot.state == XbRecordingState_Resuming))
            {
                snapshot.state = XbRecordingState_Stopping;
            }
            snapshot.failureHResult = hresult;
            wcsncpy_s(
                snapshot.errorMessage,
                RecordingStorageUserMessage(status),
                _TRUNCATE);
        }

        RecordingStorageFacts StartStorageFacts() const noexcept
        {
#if defined(XBPREVIEW_NATIVE_TESTS)
            if (configuration.lifecycleTestHooks != nullptr &&
                configuration.lifecycleTestHooks->overrideStartStorageFacts)
            {
                RecordingStorageFacts result{};
                result.status = configuration.lifecycleTestHooks->
                    startStorageStatus;
                result.hresult = configuration.lifecycleTestHooks->
                    startStorageHResult;
                result.freeBytesAvailable = configuration.lifecycleTestHooks->
                    startFreeBytes;
                result.thresholds = ComputeRecordingStorageThresholds(
                    configuration.bitrate);
                return result;
            }
#endif
            return ProbeRecordingStorageForStart(
                configuration.outputDirectory, configuration.bitrate);
        }

        RecordingStorageFacts RuntimeStorageFacts() const noexcept
        {
#if defined(XBPREVIEW_NATIVE_TESTS)
            if (configuration.lifecycleTestHooks != nullptr &&
                configuration.lifecycleTestHooks->overrideRuntimeStorageFacts)
            {
                RecordingStorageFacts result{};
                result.status = configuration.lifecycleTestHooks->
                    runtimeStorageStatus;
                result.hresult = configuration.lifecycleTestHooks->
                    runtimeStorageHResult;
                result.freeBytesAvailable = configuration.lifecycleTestHooks->
                    runtimeFreeBytes;
                result.thresholds = ComputeRecordingStorageThresholds(
                    configuration.bitrate);
                return result;
            }
#endif
            return QueryRecordingStorageRuntime(
                configuration.outputDirectory, configuration.bitrate);
        }

        bool ShouldCheckRuntimeStorage() noexcept
        {
#if defined(XBPREVIEW_NATIVE_TESTS)
            if (configuration.lifecycleTestHooks != nullptr &&
                configuration.lifecycleTestHooks->checkRuntimeStorageEverySample)
            {
                return true;
            }
#endif
            const auto now = std::chrono::steady_clock::now();
            if (now < nextStorageCheck)
            {
                return false;
            }
            nextStorageCheck = now + std::chrono::seconds(5);
            return true;
        }

        void UpdateElapsed(const std::int64_t value) noexcept
        {
            auto previous = elapsed100ns.load();
            while (value > previous &&
                !elapsed100ns.compare_exchange_weak(previous, value))
            {
            }
        }

        void PublishFailure(
            const XbPreviewResult result,
            const HRESULT hresult,
            const wchar_t* const message)
        {
            std::lock_guard lock(snapshotMutex);
            snapshot.state = XbRecordingState_Failed;
            snapshot.lastResult = result;
            snapshot.failureHResult = hresult;
            snapshot.outputCleanupHResult = S_OK;
            snapshot.activeEncoder = 0;
            if (message != nullptr)
            {
                wcsncpy_s(snapshot.errorMessage, message, _TRUNCATE);
            }
            terminalPublished = true;
        }

        void PublishExternalFailure(
            const XbPreviewResult result,
            const HRESULT hresult,
            const wchar_t* const message)
        {
            std::lock_guard lock(snapshotMutex);
            snapshot = {};
            snapshot.structSize = sizeof(snapshot);
            snapshot.apiVersion = XB_PREVIEW_API_VERSION;
            snapshot.state = XbRecordingState_Failed;
            snapshot.lastResult = result;
            snapshot.finalizeHResult = E_PENDING;
            snapshot.failureHResult = hresult;
            snapshot.outputCleanupHResult = S_OK;
            snapshot.publishHResult = E_PENDING;
            snapshot.validationHResult = E_PENDING;
            if (message != nullptr)
            {
                wcsncpy_s(snapshot.errorMessage, message, _TRUNCATE);
            }
            elapsed100ns.store(0);
            outcome = {};
            terminalPublished = true;
        }

        void PublishTerminal(
            const WorkerOutcome& worker,
            const bool readyToPublish,
            const PublishOutcome& publish,
            const OutputCleanupOutcome& cleanup)
        {
            std::lock_guard lock(snapshotMutex);
            const auto completed = readyToPublish && publish.succeeded;
            snapshot.state = completed
                ? XbRecordingState_Completed
                : XbRecordingState_Failed;
            snapshot.lastResult = completed
                ? XbPreviewResult_Ok
                : XbPreviewResult_NativeFailure;
            snapshot.elapsed100ns = elapsed100ns.load();
            snapshot.outputSuccess = completed ? 1u : 0u;
            snapshot.finalizeAttempted = worker.finalizeAttempted ? 1u : 0u;
            snapshot.finalizeHResult = worker.finalizeHResult;
            snapshot.validationAttempted =
                worker.validationAttempted ? 1u : 0u;
            snapshot.validationHResult = worker.validationHResult;
            snapshot.failureHResult = worker.failureHResult;
            snapshot.finalizeCount = worker.finalizeCount;
            snapshot.activeEncoder = 0;
            snapshot.residualOutstanding = worker.residualOutstanding;
            snapshot.outputCleanupAttempted = cleanup.attempted ? 1u : 0u;
            snapshot.outputCleanupSucceeded = cleanup.succeeded ? 1u : 0u;
            snapshot.outputCleanupHResult = cleanup.hresult;
            snapshot.framesSubmitted = worker.framesSubmitted;
            snapshot.readyToPublish = readyToPublish ? 1u : 0u;
            snapshot.published = publish.succeeded ? 1u : 0u;
            snapshot.publishAttempted = publish.attempted ? 1u : 0u;
            snapshot.publishHResult = publish.hresult;
            if (worker.outputPath[0] != L'\0')
            {
                wcsncpy_s(
                    snapshot.outputPath,
                    worker.outputPath,
                    _TRUNCATE);
                wcsncpy_s(
                    snapshot.workingPath,
                    worker.outputPath,
                    _TRUNCATE);
            }
            if (publish.succeeded)
            {
                wcsncpy_s(
                    snapshot.publishedPath,
                    publish.publishedPath,
                    _TRUNCATE);
            }
            if (!completed)
            {
                const auto failureStage = readyToPublish && publish.attempted
                    ? WorkerFailureStage::Publish
                    : worker.failureStage;
                snapshot.failureHResult =
                    readyToPublish && publish.attempted
                    ? publish.hresult
                    : worker.failureHResult;
                wcsncpy_s(
                    snapshot.errorMessage,
                    FailureMessage(failureStage, snapshot.failureHResult),
                    _TRUNCATE);
            }
            else
            {
                snapshot.failureHResult = S_OK;
                snapshot.errorMessage[0] = L'\0';
            }
            terminalPublished = true;
        }

        void PublishUserCancelledTerminal(
            const WorkerOutcome& worker,
            const OutputCleanupOutcome& cleanup)
        {
            std::lock_guard lock(snapshotMutex);
            snapshot.state = XbRecordingState_UserCancelled;
            snapshot.lastResult = cleanup.succeeded
                ? XbPreviewResult_Ok
                : XbPreviewResult_NativeFailure;
            snapshot.elapsed100ns = elapsed100ns.load();
            snapshot.outputSuccess = 0;
            snapshot.finalizeAttempted = worker.finalizeAttempted ? 1u : 0u;
            snapshot.finalizeHResult = worker.finalizeHResult;
            snapshot.validationAttempted =
                worker.validationAttempted ? 1u : 0u;
            snapshot.validationHResult = worker.validationHResult;
            snapshot.failureHResult = cleanup.succeeded
                ? S_OK
                : cleanup.hresult;
            snapshot.finalizeCount = worker.finalizeCount;
            snapshot.activeEncoder = 0;
            snapshot.residualOutstanding = worker.residualOutstanding;
            snapshot.outputCleanupAttempted = cleanup.attempted ? 1u : 0u;
            snapshot.outputCleanupSucceeded = cleanup.succeeded ? 1u : 0u;
            snapshot.outputCleanupHResult = cleanup.hresult;
            snapshot.framesSubmitted = worker.framesSubmitted;
            snapshot.readyToPublish = 0;
            snapshot.published = 0;
            snapshot.publishAttempted = 0;
            snapshot.publishHResult = E_PENDING;
            snapshot.publishedPath[0] = L'\0';
            if (worker.outputPath[0] != L'\0')
            {
                wcsncpy_s(
                    snapshot.outputPath,
                    worker.outputPath,
                    _TRUNCATE);
                wcsncpy_s(
                    snapshot.workingPath,
                    worker.outputPath,
                    _TRUNCATE);
            }
            if (cleanup.succeeded)
            {
                snapshot.errorMessage[0] = L'\0';
            }
            else
            {
                wcsncpy_s(
                    snapshot.errorMessage,
                    L"Recording was cancelled, but one or more session-owned temporary files could not be safely removed.",
                    _TRUNCATE);
            }
            terminalPublished = true;
        }

        void PublishCancellationPersistenceFailure(
            const WorkerOutcome& worker,
            const HRESULT persistenceHResult)
        {
            std::lock_guard lock(snapshotMutex);
            snapshot.state = XbRecordingState_Failed;
            snapshot.lastResult = XbPreviewResult_NativeFailure;
            snapshot.elapsed100ns = elapsed100ns.load();
            snapshot.outputSuccess = 0;
            snapshot.finalizeAttempted = worker.finalizeAttempted ? 1u : 0u;
            snapshot.finalizeHResult = worker.finalizeHResult;
            snapshot.validationAttempted =
                worker.validationAttempted ? 1u : 0u;
            snapshot.validationHResult = worker.validationHResult;
            snapshot.failureHResult = FAILED(persistenceHResult)
                ? persistenceHResult
                : E_FAIL;
            snapshot.finalizeCount = worker.finalizeCount;
            snapshot.activeEncoder = 0;
            snapshot.residualOutstanding = worker.residualOutstanding;
            snapshot.outputCleanupAttempted = 0;
            snapshot.outputCleanupSucceeded = 0;
            snapshot.outputCleanupHResult = S_OK;
            snapshot.framesSubmitted = worker.framesSubmitted;
            snapshot.readyToPublish = 0;
            snapshot.published = 0;
            snapshot.publishAttempted = 0;
            snapshot.publishHResult = E_PENDING;
            snapshot.publishedPath[0] = L'\0';
            if (worker.outputPath[0] != L'\0')
            {
                wcsncpy_s(
                    snapshot.outputPath,
                    worker.outputPath,
                    _TRUNCATE);
                wcsncpy_s(
                    snapshot.workingPath,
                    worker.outputPath,
                    _TRUNCATE);
            }
            wcsncpy_s(
                snapshot.errorMessage,
                L"Recording cancellation could not be persisted. Recovery materials were retained.",
                _TRUNCATE);
            terminalPublished = true;
        }

        void CopySnapshot(XbRecordingSnapshot& value) const
        {
            std::lock_guard lock(snapshotMutex);
            value = snapshot;
            if (value.state == XbRecordingState_Recording ||
                value.state == XbRecordingState_Pausing ||
                value.state == XbRecordingState_Paused ||
                value.state == XbRecordingState_Resuming ||
                value.state == XbRecordingState_Stopping)
            {
                value.elapsed100ns = elapsed100ns.load();
            }
            value.structSize = sizeof(value);
            value.apiVersion = XB_PREVIEW_API_VERSION;
        }


        void WriteSummary() noexcept
        {
            const auto pause = pauseControl.Snapshot();
            diagnostics.pauseRequests = pause.pauseRequests;
            diagnostics.videoPauseAcks = pause.videoPauseAcks;
            diagnostics.resumeRequests = pause.resumeRequests;
            diagnostics.videoResumeAcks = pause.videoResumeAcks;
            diagnostics.pausedFramesDiscarded =
                pause.pausedFramesDiscarded;
            diagnostics.staleResumeFramesDiscarded =
                pause.staleResumeFramesDiscarded;
            diagnostics.lastPauseCutoffSequence =
                pause.lastPauseCutoffSequence;
            diagnostics.lastResumeCutoffSequence =
                pause.lastResumeCutoffSequence;
            diagnostics.firstResumedFrameSequence =
                pause.firstResumedFrameSequence;
            const auto audioPause = audioPauseControl.Snapshot();
            diagnostics.audioPauseAcks = audioPause.audioPauseAcks;
            diagnostics.audioResumeAcks = audioPause.audioResumeAcks;
            diagnostics.audioPauseFifoClearCalls =
                audioPause.fifoClearCalls;
            diagnostics.audioInitialPauseClearCalls =
                audioPause.initialPauseClearCalls;
            diagnostics.audioPausedWakeClearCalls =
                audioPause.pausedWakeClearCalls;
            diagnostics.audioFinalResumeClearCalls =
                audioPause.finalResumeClearCalls;
            diagnostics.audioFramesWrittenAtPause =
                audioPause.audioFramesWrittenAtPause;
            diagnostics.audioFramesWrittenAtResume =
                audioPause.audioFramesWrittenAtResume;
            diagnostics.audioPauseTerminalStopTransitions =
                audioPause.terminalStopTransitions;
            diagnostics.audioPauseDiscardGateActive =
                audioPause.discardGateActive;
            cadenceTrace.FinalizeDuplicateClassifications();
            if (!cadenceTraceWritten && manifestStore &&
                cadenceTrace.totalTicks > 0)
            {
                cadenceTraceWritten = WriteVideoCadenceTrace(
                    manifestStore->SessionDirectory().wstring(),
                    cadenceTrace);
            }
            if (manifestStore && diagnostics.encoderCapabilities.probeAttempted)
            {
                (void)WriteVideoEncoderCapabilities(
                    manifestStore->SessionDirectory().wstring(),
                    diagnostics);
            }
            WriteVideoEncoderSummary(
                configuration.diagnosticDirectory,
                diagnostics);
        }

        void RunWorker(const HRESULT comResult)
        {
            D3D11Nv12Converter converter;
            Nv12TrackedTexturePool pool;
            MfH264SinkWriterSession sink;
            VideoEncoderTimestamp timestamp;
            VideoCfrCadence cadence(configuration.frameRate);
            std::optional<GpuFrameLease> latestFrame;
            bool latestFrameIsFresh{};
            bool cadenceArmed{};
            std::chrono::steady_clock::time_point nextOutputDeadline{};
            std::uint64_t lastSubmittedFreshSequence{};
            std::int64_t lastSubmittedFreshTimestamp100ns{};
            bool pipelineStarted{};
            bool terminalFailure = FAILED(comResult);
            bool unsupported{};
            UpdateAudioCaptureDiagnostics();
            nextStorageCheck = std::chrono::steady_clock::now() +
                std::chrono::seconds(5);
            if (terminalFailure)
            {
                RecordFailure(diagnostics, "CoInitializeEx", comResult);
            }

            const auto releaseLatestFrame = [&]() noexcept
            {
                if (latestFrame)
                {
                    latestFrame->Return();
                    ++diagnostics.leaseReturnCount;
                    latestFrame.reset();
                }
                latestFrameIsFresh = false;
            };

            const auto failAudioPauseBoundary = [&]
            (
                const HRESULT result,
                const char* const stopReason)
            {
                RecordFailure(diagnostics, "GStreamerAudioPause", result);
                diagnostics.stopReason = stopReason;
                terminalFailure = true;
                if (tap != nullptr)
                {
                    tap->RequestConsumerStop(
                        RenderFrameTapConsumerKind::Encoder, false);
                }
            };

            const auto transitionGStreamerAudio = [&]
            (
                const bool pause,
                const char* const stopReason) -> bool
            {
                if (!configuration.audioEnabled)
                {
                    return true;
                }
                const auto transitionResult = pause
                    ? audioCore.Pause()
                    : audioCore.Resume();
                UpdateAudioCaptureDiagnostics();
                if (FAILED(transitionResult))
                {
                    failAudioPauseBoundary(transitionResult, stopReason);
                    return false;
                }
                return true;
            };

            const auto observeVideoPauseBoundary = [&]()
            {
                if (stopRequested.load(std::memory_order_acquire))
                {
                    return false;
                }
                const bool audioPipelineExists = configuration.audioEnabled;
                if (pauseControl.Phase() ==
                        VideoPauseWorkerPhase::PauseRequested &&
                    audioPauseControl.Phase() ==
                        AudioPauseWorkerPhase::PauseRequested)
                {
                    // Thin lifecycle adapter only: pause the historical
                    // GStreamer graph before publishing either half of the
                    // existing full A/V Pause barrier.
                    if (!transitionGStreamerAudio(
                            true, "GStreamerAudioPauseFailed"))
                    {
                        return false;
                    }
                    if (stopRequested.load(std::memory_order_acquire))
                    {
                        return false;
                    }
                    if (!audioPauseControl.BeginDiscardAfterInitialClear(
                            0, audioPipelineExists))
                    {
                        if (stopRequested.load(std::memory_order_acquire))
                        {
                            return false;
                        }
                        failAudioPauseBoundary(
                            E_UNEXPECTED, "AudioPauseDiscardGateFailed");
                        return false;
                    }
                    timestamp.BeginExcludedInterval();
                    releaseLatestFrame();
                    cadenceArmed = false;
                    if (!pauseControl.AcknowledgePauseAtBoundary())
                    {
                        if (stopRequested.load(std::memory_order_acquire))
                        {
                            return false;
                        }
                        failAudioPauseBoundary(
                            E_UNEXPECTED, "VideoPauseAcknowledgeFailed");
                        return false;
                    }
                    if (!audioPauseControl.AcknowledgePause())
                    {
                        if (stopRequested.load(std::memory_order_acquire))
                        {
                            return false;
                        }
                        failAudioPauseBoundary(
                            E_UNEXPECTED, "AudioPauseAcknowledgeFailed");
                        return false;
                    }
                    PublishPauseAcknowledged();
                }
                if (stopRequested.load(std::memory_order_acquire))
                {
                    return false;
                }
                if (pauseControl.Phase() ==
                        VideoPauseWorkerPhase::ResumeRequested &&
                    audioPauseControl.Phase() ==
                        AudioPauseWorkerPhase::ResumeRequested)
                {
                    // Resume the same historical graph at the current video
                    // boundary; VideoEncoderTimestamp remains the sole owner
                    // of excluded-duration and strict DTS semantics.
                    if (!transitionGStreamerAudio(
                            false, "GStreamerAudioResumeFailed"))
                    {
                        return false;
                    }
                    if (stopRequested.load(std::memory_order_acquire))
                    {
                        return false;
                    }
                    if (!audioPauseControl.BeginResumeAfterFinalClear(
                            audioPipelineExists) ||
                        !pauseControl.BeginResumeAtBoundary())
                    {
                        if (stopRequested.load(std::memory_order_acquire))
                        {
                            return false;
                        }
                        failAudioPauseBoundary(
                            E_UNEXPECTED, "AudioVideoResumeGateFailed");
                        return false;
                    }
                    timestamp.EndExcludedInterval();
                }
                return !stopRequested.load(std::memory_order_acquire) &&
                    pauseControl.Phase() !=
                        VideoPauseWorkerPhase::Stopping;
            };

            bool generationChanged{};
            const auto retainNewestSourceFrame =
                [&](std::optional<GpuFrameLease>& incoming) -> bool
            {
                if (!incoming)
                {
                    return false;
                }
                ++diagnostics.inputFramesReceived;
                const auto metadata = incoming->Metadata();
                diagnostics.tapGenerationAtEnd = metadata.generation;
                const auto pauseDisposition =
                    pauseControl.ClassifyFrame(metadata.frameSequence);
                if (pauseDisposition == VideoPauseFrameDisposition::Stop ||
                    pauseDisposition ==
                        VideoPauseFrameDisposition::DiscardPaused ||
                    pauseDisposition ==
                        VideoPauseFrameDisposition::DiscardStaleResume)
                {
                    incoming->Return();
                    ++diagnostics.leaseReturnCount;
                    incoming.reset();
                    return false;
                }
                if (pipelineStarted &&
                    (metadata.generation != diagnostics.tapGenerationAtStart ||
                        metadata.width != diagnostics.outputWidth ||
                        metadata.height != diagnostics.outputHeight ||
                        metadata.format != DXGI_FORMAT_B8G8R8A8_UNORM))
                {
                    ++diagnostics.framesDroppedGenerationMismatch;
                    ++diagnostics.generationChangeCount;
                    ++diagnostics.inputFramesRejected;
                    incoming->Return();
                    ++diagnostics.leaseReturnCount;
                    incoming.reset();
                    diagnostics.stopReason = "GenerationChanged";
                    generationChanged = true;
                    tap->RequestConsumerStop(
                        RenderFrameTapConsumerKind::Encoder, false);
                    return false;
                }
                const auto sourceObservation = timestamp.Prepare(
                    metadata.timestampValid,
                    metadata.systemRelativeTime100ns);
                if (sourceObservation.result ==
                    VideoTimestampPrepareResult::Missing)
                {
                    ++diagnostics.framesDroppedTimestampMissing;
                    ++diagnostics.inputFramesRejected;
                    incoming->Return();
                    ++diagnostics.leaseReturnCount;
                    incoming.reset();
                    return false;
                }
                if (sourceObservation.result ==
                    VideoTimestampPrepareResult::Regression)
                {
                    ++diagnostics.framesDroppedTimestampRegression;
                    ++diagnostics.inputFramesRejected;
                    incoming->Return();
                    ++diagnostics.leaseReturnCount;
                    incoming.reset();
                    return false;
                }

                cadenceTrace.ObserveSourceArrival(
                    metadata.frameSequence,
                    metadata.systemRelativeTime100ns,
                    metadata.enqueueQpc);

                if (latestFrame)
                {
                    if (latestFrameIsFresh)
                    {
                        ++diagnostics.cadenceDroppedSourceFrames;
                        cadenceTrace.ObservePendingReplacement();
                    }
                    latestFrame->Return();
                    ++diagnostics.leaseReturnCount;
                }
                latestFrame = std::move(incoming);
                latestFrameIsFresh = true;
                diagnostics.lastInputTimestamp =
                    metadata.systemRelativeTime100ns;
                if (!cadenceArmed)
                {
                    cadenceArmed = true;
                    nextOutputDeadline = std::chrono::steady_clock::now();
                }
                return true;
            };

            while (!terminalFailure && !unsupported)
            {
                if (!observeVideoPauseBoundary())
                {
                    break;
                }
                auto wait = std::chrono::milliseconds(100);
                const auto beforeWait = std::chrono::steady_clock::now();
                if (latestFrame && cadenceArmed &&
                    beforeWait < nextOutputDeadline)
                {
                    wait = (std::min)(
                        wait,
                        std::chrono::ceil<std::chrono::milliseconds>(
                            nextOutputDeadline - beforeWait));
                }
                auto incoming = tap->WaitAcquire(
                    RenderFrameTapConsumerKind::Encoder, wait);
                if (!incoming)
                {
                    if (stopRequested.load() ||
                        tap->ActiveConsumer() !=
                            RenderFrameTapConsumerKind::Encoder)
                    {
                        break;
                    }
                }
                else if (!observeVideoPauseBoundary())
                {
                    incoming->Return();
                    ++diagnostics.leaseReturnCount;
                    break;
                }
                if (incoming)
                {
                    (void)retainNewestSourceFrame(incoming);
                }
                if (generationChanged || stopRequested.load())
                {
                    break;
                }
                if (!latestFrame || !cadenceArmed ||
                    std::chrono::steady_clock::now() < nextOutputDeadline)
                {
                    continue;
                }

                // Drain only the bounded tap queue at the due boundary. Each
                // replacement keeps the newest source frame and returns the
                // older lease immediately; no unbounded FIFO is introduced.
                for (;;)
                {
                    auto queued = tap->WaitAcquire(
                        RenderFrameTapConsumerKind::Encoder,
                        std::chrono::milliseconds(0));
                    if (!queued)
                    {
                        break;
                    }
                    (void)retainNewestSourceFrame(queued);
                    if (generationChanged || stopRequested.load())
                    {
                        break;
                    }
                }
                if (generationChanged || stopRequested.load() || !latestFrame)
                {
                    break;
                }

                auto lease = std::move(latestFrame);
                latestFrame.reset();
                const bool outputUsesFreshFrame = latestFrameIsFresh;
                latestFrameIsFresh = false;
                const auto metadata = lease->Metadata();
                ++diagnostics.outputTicks;
                if (pipelineStarted && ShouldCheckRuntimeStorage())
                {
                    const auto storage = RuntimeStorageFacts();
                    if (storage.status ==
                        RecordingStorageStatus::LowSpaceWarning)
                    {
                        PublishStorageStatus(
                            storage.status, storage.hresult, false);
                    }
                    else if (storage.status ==
                        RecordingStorageStatus::CriticalSpace)
                    {
                        lease->Return();
                        ++diagnostics.leaseReturnCount;
                        diagnostics.stopReason = "StorageCriticalStop";
                        PublishStorageStatus(
                            storage.status,
                            HRESULT_FROM_WIN32(ERROR_DISK_FULL),
                            true);
                        stopRequested.store(true);
                        tap->RequestConsumerStop(
                            RenderFrameTapConsumerKind::Encoder, false);
                        break;
                    }
                    else if (storage.status != RecordingStorageStatus::Ready)
                    {
                        lease->Return();
                        ++diagnostics.leaseReturnCount;
                        diagnostics.stopReason = "StorageUnavailable";
                        RecordFailure(diagnostics, "Storage", storage.hresult);
                        PublishStorageStatus(
                            storage.status, storage.hresult, true);
                        terminalFailure = true;
                        stopRequested.store(true);
                        tap->RequestConsumerStop(
                            RenderFrameTapConsumerKind::Encoder, false);
                        break;
                    }
                }
                if (stopRequested.load(std::memory_order_acquire))
                {
                    lease->Return();
                    ++diagnostics.leaseReturnCount;
                    break;
                }

                const auto cfrTiming = cadence.PrepareNext();
                const auto frameInterval = std::chrono::nanoseconds(
                    cfrTiming.duration100ns * 100);
                const auto scheduledOutputDeadline = nextOutputDeadline;
                const auto outputNow = std::chrono::steady_clock::now();
                const auto actualWakeQpc = QueryPerformanceCounterValue();
                const auto deadlineErrorNanoseconds =
                    std::chrono::duration_cast<std::chrono::nanoseconds>(
                        outputNow - scheduledOutputDeadline).count();
                const auto scheduledDeadlineQpc = actualWakeQpc -
                    PerformanceCounterTicksFromNanoseconds(
                        deadlineErrorNanoseconds,
                        cadenceTrace.qpcFrequency);
                bool missedDeadline{};
                if (outputNow >= nextOutputDeadline + frameInterval)
                {
                    const auto lateNanoseconds =
                        std::chrono::duration_cast<std::chrono::nanoseconds>(
                            outputNow - nextOutputDeadline).count();
                    const auto missed =
                        VideoEncoderCfrMissedDeadlineCount(
                            lateNanoseconds / 100,
                            cfrTiming.duration100ns);
                    diagnostics.missedDeadlines += (std::max)(
                        std::uint64_t{ 1 }, missed);
                    missedDeadline = true;
                    // Rebase the wall-clock cadence at now. The content frame
                    // index is not advanced here, so no catch-up burst can be
                    // manufactured after an encoder stall.
                    nextOutputDeadline = outputNow;
                }
                VideoCadenceTraceRecord cadenceTraceRecord{};
                cadenceTraceRecord.tickIndex = cfrTiming.frameIndex;
                cadenceTraceRecord.selectedFps = configuration.frameRate;
                cadenceTraceRecord.targetContentTime100ns =
                    cfrTiming.sampleTime100ns;
                cadenceTraceRecord.actualWakeQpc = actualWakeQpc;
                cadenceTraceRecord.scheduledDeadlineQpc =
                    scheduledDeadlineQpc;
                cadenceTraceRecord.deadlineErrorUs =
                    deadlineErrorNanoseconds / 1'000;
                cadenceTraceRecord.pendingFrameSequence =
                    metadata.frameSequence;
                cadenceTraceRecord.pendingSourceTimestamp100ns =
                    metadata.systemRelativeTime100ns;
                cadenceTraceRecord.pendingEnqueueQpc = metadata.enqueueQpc;
                cadenceTraceRecord.lastSubmittedFreshSequence =
                    lastSubmittedFreshSequence;
                cadenceTraceRecord.lastSubmittedSourceTimestamp100ns =
                    lastSubmittedFreshTimestamp100ns;
                cadenceTraceRecord.freshAvailableBeforeDeadline =
                    metadata.frameSequence > lastSubmittedFreshSequence &&
                    metadata.enqueueQpc > 0 && scheduledDeadlineQpc > 0 &&
                    metadata.enqueueQpc <= scheduledDeadlineQpc;
                if (cadenceTraceRecord.freshAvailableBeforeDeadline)
                {
                    cadenceTraceRecord.freshAvailableSequenceBeforeDeadline =
                        metadata.frameSequence;
                    cadenceTraceRecord.freshAvailableSourceTimestamp100ns =
                        metadata.systemRelativeTime100ns;
                    cadenceTraceRecord.freshAvailableEnqueueQpc =
                        metadata.enqueueQpc;
                }
                cadenceTraceRecord.missedDeadline = missedDeadline;
                const auto timing = timestamp.PrepareCfr(cfrTiming);
                if (timing.result != VideoTimestampPrepareResult::Prepared)
                {
                    lease->Return();
                    ++diagnostics.leaseReturnCount;
                    RecordFailure(
                        diagnostics, "TimestampPrepareCfr", E_UNEXPECTED);
                    terminalFailure = true;
                    tap->RequestConsumerStop(
                        RenderFrameTapConsumerKind::Encoder, false);
                    continue;
                }

                if (!pipelineStarted)
                {
                    diagnostics.outputWidth = metadata.width;
                    diagnostics.outputHeight = metadata.height;
                    diagnostics.tapGenerationAtStart = metadata.generation;
                    diagnostics.tapGenerationAtEnd = metadata.generation;
                    diagnostics.firstInputTimestamp =
                        metadata.systemRelativeTime100ns;
                    if (metadata.width == 0 || metadata.height == 0 ||
                        (metadata.width & 1u) != 0 ||
                        (metadata.height & 1u) != 0)
                    {
                        ++diagnostics.framesDroppedOddGeometry;
                        ++diagnostics.inputFramesRejected;
                        lease->Return();
                        ++diagnostics.leaseReturnCount;
                        diagnostics.encoderState = VideoEncoderState::Unsupported;
                        diagnostics.stopReason = "OddOutputGeometry";
                        diagnostics.failureStage = "ValidateOutputGeometry";
                        diagnostics.failureHResult = E_INVALIDARG;
                        unsupported = true;
                        tap->RequestConsumerStop(
                            RenderFrameTapConsumerKind::Encoder, false);
                        continue;
                    }
                    try
                    {
                        converter.Initialize(
                            device.get(), context.get(),
                            metadata.width, metadata.height,
                            configuration.frameRate);
                        diagnostics.videoProcessorInputSupported =
                            converter.BgraInputSupported();
                        diagnostics.videoProcessorNv12OutputSupported =
                            converter.Nv12OutputSupported();
                        pool.Initialize(
                            device.get(), converter.VideoDevice(),
                            converter.Enumerator(),
                            metadata.width, metadata.height);
                        std::filesystem::create_directories(
                            configuration.outputDirectory);
                        diagnostics.outputPath = configuration.workingPath;
                        wcsncpy_s(
                            outcome.outputPath,
                            diagnostics.outputPath.c_str(),
                            _TRUNCATE);
                        const auto reserveResult = ReserveSessionOutput(
                            diagnostics.outputPath,
                            outcome.outputOwnedBySession);
                        if (FAILED(reserveResult))
                        {
                            diagnostics.failureStage = "ReserveOutput";
                            throw winrt::hresult_error(reserveResult);
                        }
                        PersistManifestWorkingOwned();
                        if (configuration.faultInjection ==
                            VideoEncoderFaultInjection::
                                UnsupportedAfterOutputFileCreated)
                        {
                            throw winrt::hresult_error(MF_E_INVALIDMEDIATYPE);
                        }
                        if (configuration.faultInjection ==
                            VideoEncoderFaultInjection::
                                WorkerExceptionAfterOutputFileCreated)
                        {
                            throw std::runtime_error(
                                "Injected encoder worker exception.");
                        }
                        const auto startResult = sink.Start(
                            device.get(), metadata.width, metadata.height,
                            configuration.frameRate,
                            configuration.bitrate,
                            diagnostics.outputPath,
                            diagnostics,
                            false);
                        if (FAILED(startResult))
                        {
                            throw winrt::hresult_error(startResult);
                        }
                        if (!IsVideoEncoderStateTransitionAllowed(
                                diagnostics.encoderState,
                                VideoEncoderState::Running))
                        {
                            ++diagnostics.invalidStateTransitionDetected;
                        }
                        diagnostics.encoderState = VideoEncoderState::Running;
                        diagnostics.stopReason = "PreviewStopped";
                        pipelineStarted = true;
                        PublishRecordingStarted(diagnostics.outputPath);
                    }
                    catch (const winrt::hresult_error& error)
                    {
                        lease->Return();
                        ++diagnostics.leaseReturnCount;
                        if (IsUnsupportedResult(error.code()))
                        {
                            diagnostics.encoderState = VideoEncoderState::Unsupported;
                            diagnostics.stopReason = "VideoPipelineUnsupported";
                            diagnostics.failureStage = "StartVideoPipeline";
                            diagnostics.failureHResult = error.code();
                            unsupported = true;
                        }
                        else
                        {
                            RecordFailure(
                                diagnostics,
                                diagnostics.failureStage == "AudioCapture"
                                    ? "AudioCapture"
                                    : "StartVideoPipeline",
                                error.code());
                            terminalFailure = true;
                        }
                        tap->RequestConsumerStop(
                            RenderFrameTapConsumerKind::Encoder, false);
                        continue;
                    }
                    catch (const std::bad_alloc&)
                    {
                        lease->Return();
                        ++diagnostics.leaseReturnCount;
                        throw;
                    }
                    catch (const std::exception&)
                    {
                        lease->Return();
                        ++diagnostics.leaseReturnCount;
                        throw;
                    }
                    catch (...)
                    {
                        lease->Return();
                        ++diagnostics.leaseReturnCount;
                        throw;
                    }
                }

                diagnostics.lastInputTimestamp =
                    metadata.systemRelativeTime100ns;

                const auto slot = pool.TryAcquire();
                if (!slot)
                {
                    ++diagnostics.framesDroppedNv12Starvation;
                    ++diagnostics.inputFramesRejected;
                    ++diagnostics.missedDeadlines;
                    latestFrame = std::move(lease);
                    latestFrameIsFresh = outputUsesFreshFrame;
                    nextOutputDeadline =
                        std::chrono::steady_clock::now() + frameInterval;
                    cadenceTraceRecord.decision =
                        VideoCadenceDecision::Missed;
                    cadenceTraceRecord.missedDeadline = true;
                    cadenceTrace.RecordTick(cadenceTraceRecord);
                    continue;
                }
                if (stopRequested.load(std::memory_order_acquire))
                {
                    pool.CancelProducing(*slot);
                    lease->Return();
                    ++diagnostics.leaseReturnCount;
                    break;
                }
                const auto conversionResult = converter.Convert(
                    lease->Texture(), metadata.generation, metadata.poolSlot,
                    pool.OutputView(*slot));
                if (FAILED(conversionResult))
                {
                    pool.CancelProducing(*slot);
                    lease->Return();
                    ++diagnostics.leaseReturnCount;
                    ++diagnostics.videoProcessorFailures;
                    ++diagnostics.inputFramesRejected;
                    RecordFailure(
                        diagnostics, "VideoProcessorBlt", conversionResult);
                    terminalFailure = true;
                    tap->RequestConsumerStop(
                        RenderFrameTapConsumerKind::Encoder, false);
                    continue;
                }
                ++diagnostics.framesConvertedToNv12;
                // Keep the consumer-owned BGRA lease stable across cadence
                // ticks. It is returned only when a newer source frame wins,
                // Pause begins, or the worker stops. Producer copies and this
                // conversion remain ordered on the same protected context.
                latestFrame = std::move(lease);

                if (stopRequested.load(std::memory_order_acquire))
                {
                    pool.CancelProducing(*slot);
                    break;
                }

                winrt::com_ptr<IMFSample> sample;
                const auto sampleResult = pool.CreateTrackedSample(
                    *slot,

                    timing.videoSinkTimeline.sampleTime100ns,
                    timing.videoSinkTimeline.duration100ns,
                    sample.put());
                if (FAILED(sampleResult))
                {
                    pool.CancelProducing(*slot);
                    ++diagnostics.framesRejectedBySinkWriter;
                    ++diagnostics.inputFramesRejected;
                    RecordFailure(
                        diagnostics, "CreateTrackedSample", sampleResult);
                    terminalFailure = true;
                    tap->RequestConsumerStop(
                        RenderFrameTapConsumerKind::Encoder, false);
                    continue;
                }
                if (stopRequested.load(std::memory_order_acquire))
                {
                    break;
                }
                double writeDuration{};
                outcome.writeSampleAttempted = true;
                HRESULT writeResult{};
#if defined(XBPREVIEW_NATIVE_TESTS)
                if (configuration.lifecycleTestHooks != nullptr &&
                    diagnostics.framesSubmittedToSinkWriter >=
                        configuration.lifecycleTestHooks->
                            injectWriteFailureAfterSubmittedFrames)
                {
                    writeResult = configuration.lifecycleTestHooks->
                        injectedWriteFailureHResult;
                }
                else
#endif
                {
                    writeResult = sink.WriteSample(
                        sample.get(), writeDuration);
                }
                diagnostics.writeSampleDurationsMs.push_back(writeDuration);
                if (FAILED(writeResult))
                {
                    ++diagnostics.writeSampleFailures;
                    ++diagnostics.framesRejectedBySinkWriter;
                    ++diagnostics.inputFramesRejected;
                    RecordFailure(diagnostics, "WriteSample", writeResult);
                    if (IsStorageFailureHResult(writeResult))
                    {
                        PublishStorageStatus(
                            RecordingStorageStatus::DestinationUnavailable,
                            writeResult,
                            true);
                    }
                    terminalFailure = true;
                    tap->RequestConsumerStop(
                        RenderFrameTapConsumerKind::Encoder, false);
                    continue;
                }
                if (!timestamp.Commit(timing))
                {
                    ++diagnostics.framesRejectedBySinkWriter;
                    ++diagnostics.inputFramesRejected;
                    RecordFailure(diagnostics, "TimestampCommit", E_UNEXPECTED);
                    terminalFailure = true;
                    tap->RequestConsumerStop(
                        RenderFrameTapConsumerKind::Encoder, false);
                    continue;
                }
                if (!cadence.Commit(cfrTiming))
                {
                    ++diagnostics.framesRejectedBySinkWriter;
                    RecordFailure(diagnostics, "CfrCadenceCommit", E_UNEXPECTED);
                    terminalFailure = true;
                    tap->RequestConsumerStop(
                        RenderFrameTapConsumerKind::Encoder, false);
                    continue;
                }
                nextOutputDeadline += frameInterval;
                if (diagnostics.framesSubmittedToSinkWriter == 0)
                {
                    diagnostics.firstSampleTime =
                        timing.videoSinkTimeline.sampleTime100ns;
                }
                ++diagnostics.framesSubmittedToSinkWriter;
                diagnostics.submittedFrames =
                    diagnostics.framesSubmittedToSinkWriter;
                if (outputUsesFreshFrame)
                {
                    ++diagnostics.freshFrames;
                    cadenceTraceRecord.decision =
                        VideoCadenceDecision::Fresh;
                }
                else
                {
                    ++diagnostics.duplicatedFrames;
                    cadenceTraceRecord.decision =
                        VideoCadenceDecision::Duplicate;
                }
                cadenceTrace.RecordTick(cadenceTraceRecord);
                if (outputUsesFreshFrame)
                {
                    lastSubmittedFreshSequence = metadata.frameSequence;
                    lastSubmittedFreshTimestamp100ns =
                        metadata.systemRelativeTime100ns;
                }
                const bool resumedVideoCommitted =
                    pauseControl.FrameCommitted(metadata.frameSequence);
                diagnostics.lastSampleTime =
                    timing.videoSinkTimeline.sampleTime100ns;
                if (diagnostics.sampleDurationMin == 0)
                {
                    diagnostics.sampleDurationMin =
                        timing.videoSinkTimeline.duration100ns;
                }
                diagnostics.sampleDurationMin = (std::min)(
                    diagnostics.sampleDurationMin,
                    timing.videoSinkTimeline.duration100ns);
                diagnostics.sampleDurationMax = (std::max)(
                    diagnostics.sampleDurationMax,
                    timing.videoSinkTimeline.duration100ns);
                diagnostics.durationEstimateSource = "CFR-Rational";
                diagnostics.lastFrameDurationEstimated = false;
                if (resumedVideoCommitted &&
                    !audioPauseControl.AcknowledgeResume(0))
                {
                    if (stopRequested.load(std::memory_order_acquire))
                    {
                        continue;
                    }
                    RecordFailure(
                        diagnostics,
                        "AudioResumeAcknowledge",
                        E_UNEXPECTED);
                    diagnostics.stopReason =
                        "AudioResumeAcknowledgeFailed";
                    terminalFailure = true;
                    tap->RequestConsumerStop(
                        RenderFrameTapConsumerKind::Encoder, false);
                    continue;
                }
                if (resumedVideoCommitted)
                {
                    PublishResumeAcknowledged(
                        timestamp.TotalExcludedDuration100ns());
                }
                PersistManifestRecording();
                UpdateElapsed(timing.contentTimeline.endTime100ns);
            }

            releaseLatestFrame();

            if (!terminalFailure && !unsupported && !pipelineStarted)
            {
                RecordFailure(diagnostics, "NoAcceptedFrames", MF_E_INVALID_TIMESTAMP);
                terminalFailure = true;
            }

            // Exact 4fc3757 lifecycle: GStreamer owns EOS, FLAC finalization,
            // DeviceMonitor shutdown and every audio resource. The current
            // video encoder remains the sole owner of strict DTS.
            if (configuration.audioEnabled)
            {
                const auto audioStopResult = audioCore.Stop();
                diagnostics.audioStopHResult = audioStopResult;
                diagnostics.audioCaptureStopped = SUCCEEDED(audioStopResult);
                UpdateAudioCaptureDiagnostics();
                if (FAILED(audioStopResult) && !terminalFailure)
                {
                    RecordFailure(
                        diagnostics,
                        "GStreamerAudioStop",
                        audioStopResult);
                    terminalFailure = true;
                }
            }
            UpdateAudioCaptureDiagnostics();

            const auto stopStarted = std::chrono::steady_clock::now();
            diagnostics.bgraOutstandingAtStop =
                tap->Diagnostics().outstandingCurrent;
            if (pipelineStarted)
            {
                if (diagnostics.encoderState != VideoEncoderState::Failed &&
                    diagnostics.encoderState != VideoEncoderState::Unsupported)
                {
                    if (!IsVideoEncoderStateTransitionAllowed(
                            diagnostics.encoderState,
                            VideoEncoderState::Stopping))
                    {
                        ++diagnostics.invalidStateTransitionDetected;
                    }
                    diagnostics.encoderState = VideoEncoderState::Stopping;
                }
                diagnostics.nv12OutstandingAtStop =
                    pool.Diagnostics().outstanding;
                if (!terminalFailure)
                {
#if defined(XBPREVIEW_NATIVE_TESTS)
                    if (configuration.lifecycleTestHooks != nullptr)
                    {
                        Impl::ObserveLifecycleBoundary(
                            configuration.lifecycleTestHooks->
                                beforeFinalizeReached,
                            configuration.lifecycleTestHooks->
                                continueBeforeFinalize);
                    }
#endif
                    if (!IsVideoEncoderStateTransitionAllowed(
                            diagnostics.encoderState,
                            VideoEncoderState::Finalizing))
                    {
                        ++diagnostics.invalidStateTransitionDetected;
                    }
                    diagnostics.encoderState = VideoEncoderState::Finalizing;
                }
                ++outcome.finalizeCount;
                auto finalizeResult = sink.Finalize(diagnostics);
                if (SUCCEEDED(finalizeResult) &&
                    configuration.faultInjection ==
                        VideoEncoderFaultInjection::FinalizeFailureAfterWrite)
                {
                    // Exercise the terminal failure policy after the real
                    // SinkWriter has released its samples and file handle.
                    // This keeps the fault deterministic without manufacturing
                    // an Encoder resource leak as part of the test itself.
                    finalizeResult = E_FAIL;
                    diagnostics.finalizeHResult = finalizeResult;
                }
                if (FAILED(finalizeResult))
                {
                    RecordFailure(diagnostics, "Finalize", finalizeResult);
                    terminalFailure = true;
                }
                pool.MarkStopping();
                const auto trackedReturnStarted =
                    std::chrono::steady_clock::now();
                const auto returned = pool.WaitForAllReturned(
                    configuration.trackedReturnTimeout);
                diagnostics.trackedReturnDurationMs =
                    std::chrono::duration<double, std::milli>(
                        std::chrono::steady_clock::now() -
                            trackedReturnStarted).count();
                diagnostics.trackedReturnTimedOut = !returned;
                if (!returned)
                {
                    RecordFailure(
                        diagnostics,
                        "TrackedReturnTimeout",
                        HRESULT_FROM_WIN32(WAIT_TIMEOUT));
                    terminalFailure = true;
                }
                const auto poolDiagnostics = pool.Diagnostics();
                diagnostics.nv12PoolHighWatermark = poolDiagnostics.highWatermark;
                diagnostics.nv12OutstandingCurrent = poolDiagnostics.outstanding;
                diagnostics.nv12OutstandingHighWatermark =
                    poolDiagnostics.highWatermark;
                diagnostics.trackedCallbackCount = poolDiagnostics.callbackCount;
                diagnostics.trackedCallbackAfterStop =
                    poolDiagnostics.callbackAfterStop;
                diagnostics.doubleReturnDetected = poolDiagnostics.doubleReturn;
                diagnostics.invalidStateTransitionDetected +=
                    poolDiagnostics.invalidStateTransition;
                diagnostics.nv12PoolStarvation = poolDiagnostics.starvation;
                diagnostics.residualOutstandingAtShutdown =
                    poolDiagnostics.outstanding;
                if (!terminalFailure && !UserCancellationWon())
                {
                    outcome.validationAttempted = true;
                    const auto validationResult =
                        configuration.faultInjection ==
                            VideoEncoderFaultInjection::
                                ValidationFailureAfterFinalize
                        ? E_FAIL
                        : sink.QuickRuntimeValidation(diagnostics);
                    outcome.validationHResult = validationResult;
                    if (FAILED(validationResult))
                    {
                        RecordFailure(
                            diagnostics, "QuickRuntimeValidation",
                            validationResult);
                        terminalFailure = true;
                    }
                }
                sink.Shutdown();
                pool.Shutdown();
                converter.Shutdown();
            }
            else
            {
                sink.Shutdown();
                pool.Shutdown();
                converter.Shutdown();
            }
            const auto readyForPublishCandidate =
                !terminalFailure && !unsupported;
            diagnostics.deviceRemovedReason = device
                ? device->GetDeviceRemovedReason()
                : E_POINTER;
            const auto tapDiagnostics = tap->Diagnostics();
            diagnostics.tapFramesObserved =
                tapDiagnostics.framesObservedAtTapPoint;
            diagnostics.tapFramesCopied = tapDiagnostics.framesCopied;
            diagnostics.tapFramesEnqueued = tapDiagnostics.framesEnqueued;
            diagnostics.tapFramesDroppedNoFreeSlot =
                tapDiagnostics.framesDroppedNoFreeSlot;
            diagnostics.tapFramesDroppedQueueFull =
                tapDiagnostics.framesDroppedQueueFull;
            diagnostics.cadenceDroppedSourceFrames +=
                tapDiagnostics.framesDroppedNoFreeSlot +
                tapDiagnostics.framesDroppedQueueFull;
            diagnostics.tapFramesDroppedGenerationMismatch =
                tapDiagnostics.framesDroppedGenerationMismatch;
            diagnostics.tapFramesDroppedDisabledOrStopping =
                tapDiagnostics.framesDroppedDisabledOrStopping;
            diagnostics.tapFramesDroppedLockBusy =
                tapDiagnostics.framesDroppedLockBusy;
            diagnostics.tapQueueDepthHighWatermark =
                tapDiagnostics.queueDepthHighWatermark;
            diagnostics.bgraOutstandingAtShutdown =
                tapDiagnostics.outstandingCurrent;
            diagnostics.stopDurationMs =
                std::chrono::duration<double, std::milli>(
                    std::chrono::steady_clock::now() - stopStarted).count();
            outcome.readyForPublishCandidate = readyForPublishCandidate;
            outcome.finalizeAttempted = diagnostics.finalizeAttempted;
            outcome.finalizeHResult = diagnostics.finalizeHResult;
            outcome.failureHResult = diagnostics.failureHResult;
            outcome.failureStage = readyForPublishCandidate
                ? WorkerFailureStage::None
                : FailureStageFromText(diagnostics.failureStage);
            outcome.residualOutstanding = (std::max)(
                diagnostics.residualOutstandingAtShutdown,
                diagnostics.bgraOutstandingAtShutdown);
            const auto audioSnapshot = audioCore.Snapshot();
            if (configuration.audioEnabled &&
                (!audioSnapshot.filesClosed ||
                 !audioSnapshot.busThreadExited))
            {
                outcome.residualOutstanding = (std::max)(
                    outcome.residualOutstanding,
                    static_cast<std::uint32_t>(1));
            }
            outcome.framesSubmitted =
                diagnostics.framesSubmittedToSinkWriter;
        }

        void ThreadMain() noexcept
        {
            running.store(true);
            auto comResult = CoInitializeEx(nullptr, COINIT_MULTITHREADED);
            const bool uninitializeCom = SUCCEEDED(comResult);
            if (comResult == RPC_E_CHANGED_MODE)
            {
                comResult = S_OK;
            }

            try
            {
                RunWorker(comResult);
            }
            catch (const std::bad_alloc&)
            {
                outcome.readyForPublishCandidate = false;
                outcome.failureHResult = E_OUTOFMEMORY;
                outcome.failureStage = WorkerFailureStage::BadAllocation;
            }
            catch (const std::exception&)
            {
                outcome.readyForPublishCandidate = false;
                outcome.failureHResult = E_FAIL;
                outcome.failureStage = WorkerFailureStage::StandardException;
            }
            catch (...)
            {
                outcome.readyForPublishCandidate = false;
                outcome.failureHResult = E_UNEXPECTED;
                outcome.failureStage = WorkerFailureStage::UnknownException;
            }

            (void)pauseControl.CancelForStop();
            (void)audioPauseControl.CancelForStop();
            if (tap != nullptr)
            {
                tap->RequestConsumerStop(
                    RenderFrameTapConsumerKind::Encoder, true);
                const auto tapDiagnostics = tap->Diagnostics();
                outcome.residualOutstanding = (std::max)(
                    outcome.residualOutstanding,
                    tapDiagnostics.outstandingCurrent);
                tap->UnregisterConsumer(
                    RenderFrameTapConsumerKind::Encoder);
            }
            context = nullptr;
            device = nullptr;
            if (uninitializeCom)
            {
                CoUninitialize();
            }
            outcome.workerExited = true;
            running.store(false);
        }

    };

    VideoEncoderConsumer::VideoEncoderConsumer()
        : impl_(std::make_unique<Impl>())
    {
    }

    VideoEncoderConsumer::~VideoEncoderConsumer()
    {
        StopAndJoin();
    }

    XbPreviewResult VideoEncoderConsumer::Start(
        RenderFrameTap& tap,
        ID3D11Device* const device,
        ID3D11DeviceContext* const immediateContext,
        const VideoEncoderConfiguration& configuration,
        const VideoDeviceSetupStatus& deviceStatus)
    {
        if (!configuration.enabled)
        {
            return XbPreviewResult_Ok;
        }
        if (!IsSupportedVideoEncoderFrameRate(configuration.frameRate))
        {
            return XbPreviewResult_InvalidArgument;
        }
        if (impl_->Active())
        {
            return XbPreviewResult_InvalidState;
        }
        (void)StopAndJoin();
        // Configuration/string copies are intentionally allowed to throw to
        // the C ABI boundary. This local guard only rolls back the one native
        // resource acquired before thread construction can fail.
        impl_->configuration = configuration;
        impl_->deviceStatus = deviceStatus;
        impl_->diagnostics = {};
        impl_->cadenceTraceWritten = false;
        auto& diagnostics = impl_->diagnostics;
        diagnostics.encoderEnabled = configuration.enabled;
        diagnostics.encoderSessionId = configuration.sessionId;
        diagnostics.encoderState = configuration.enabled
            ? VideoEncoderState::Starting
            : VideoEncoderState::Disabled;
        diagnostics.bitrate = configuration.bitrate;
        diagnostics.selectedFps = configuration.frameRate;
        diagnostics.nominalFrameRateNumerator = configuration.frameRate;
        diagnostics.nominalFrameRateDenominator = 1;
        diagnostics.nominalFrameDuration100ns =
            VideoEncoderCfrDuration100ns(0, configuration.frameRate);
        diagnostics.durationEstimateSource = "CFR-Rational";
        diagnostics.lastFrameDurationEstimated = false;
        LARGE_INTEGER performanceCounterFrequency{};
        impl_->cadenceTrace.Reset(
            QueryPerformanceFrequency(&performanceCounterFrequency)
                ? performanceCounterFrequency.QuadPart
                : 0);
        diagnostics.trackedReturnTimeoutMs = static_cast<std::uint32_t>(
            configuration.trackedReturnTimeout.count());
        diagnostics.audioBackend = configuration.audioEnabled
            ? "GStreamer-1.28.6/wasapi2src/WebRTC-DSP/FFmpeg"
            : "Disabled";
        diagnostics.audioMode = configuration.audioEnabled
            ? GStreamerAudioModeName(configuration.audioMode)
            : "None";
        diagnostics.gStreamerAudioMode = configuration.audioEnabled
            ? GStreamerAudioModeName(configuration.audioMode)
            : "None";
        diagnostics.outputFormat = configuration.audioEnabled
            ? "MP4/H264-NV12+AAC-GSTREAMER-FLAC-48K"
            : "MP4/H264-NV12";
        diagnostics.videoSupportRequested =
            deviceStatus.videoSupportRequested;
        diagnostics.videoSupportDeviceCreated =
            deviceStatus.videoSupportDeviceCreated;
        diagnostics.multithreadProtectionAvailable =
            deviceStatus.multithreadProtectionAvailable;
        diagnostics.multithreadProtectionEnabled =
            deviceStatus.multithreadProtectionEnabled;
        impl_->PrepareSnapshot(configuration);
        impl_->pauseControl.Reset();
        impl_->audioPauseControl.Reset();
        const auto storage = impl_->StartStorageFacts();
        constexpr std::uint64_t gstreamerAudioFinalizeReserve =
            512ull * 1024ull * 1024ull;
        const auto gstreamerAudioStorageUnsafe =
            configuration.audioEnabled && storage.CanStart() &&
            (storage.freeBytesAvailable < storage.thresholds.startupBytes ||
                storage.freeBytesAvailable - storage.thresholds.startupBytes <
                    gstreamerAudioFinalizeReserve);
        if (!storage.CanStart() || gstreamerAudioStorageUnsafe)
        {
            const auto failure = FAILED(storage.hresult)
                ? storage.hresult
                : HRESULT_FROM_WIN32(ERROR_DISK_FULL);
            diagnostics.encoderState = VideoEncoderState::Failed;
            diagnostics.stopReason = "StoragePreflightRejected";
            diagnostics.failureStage = "Storage";
            diagnostics.failureHResult = failure;
            impl_->PublishFailure(
                XbPreviewResult_NativeFailure,
                failure,
                gstreamerAudioStorageUnsafe
                    ? L"GStreamer Audio needs temporary space for FLAC sidecars, the original video, and the validated remux output."
                    : RecordingStorageUserMessage(storage.status));
            impl_->WriteSummary();
            return XbPreviewResult_NativeFailure;
        }
        impl_->PrepareManifest(configuration);
        if (!deviceStatus.videoSupportDeviceCreated ||
            !deviceStatus.multithreadProtectionEnabled ||
            device == nullptr || immediateContext == nullptr)
        {
            diagnostics.encoderState = VideoEncoderState::Unsupported;
            diagnostics.stopReason = "VideoDeviceUnavailable";
            diagnostics.failureStage = "CreateVideoCapableDevice";
            diagnostics.failureHResult =
                deviceStatus.videoDeviceCreationResult;
            impl_->PublishFailure(
                XbPreviewResult_NativeFailure,
                diagnostics.failureHResult,
                L"The active Preview device does not support recording.");
            impl_->PersistManifestStartFailure(
                diagnostics.failureHResult,
                L"The active Preview device does not support recording.");
            impl_->WriteSummary();
            impl_->ReleaseLifetimeOwner();
            return XbPreviewResult_NativeFailure;
        }
        GStreamerAudioConfig audioConfig{};
        audioConfig.mode = configuration.audioMode;
        if (impl_->manifestStore)
        {
            audioConfig.workingDirectory =
                impl_->manifestStore->SessionDirectory();
        }
        audioConfig.injectInitializationFailure =
            configuration.faultInjection ==
                VideoEncoderFaultInjection::AudioInitializationFailure;
        audioConfig.microphoneDevice = configuration.microphoneDevice;
        const auto audioStartResult = configuration.audioEnabled
            ? (impl_->manifestStore
                ? impl_->audioCore.Start(audioConfig)
                : E_UNEXPECTED)
            : S_OK;
        diagnostics.audioStartHResult = audioStartResult;
        diagnostics.audioCaptureStarted =
            configuration.audioEnabled && SUCCEEDED(audioStartResult);
        impl_->UpdateAudioCaptureDiagnostics();
        if (configuration.audioEnabled && FAILED(audioStartResult))
        {
            (void)impl_->audioCore.Stop();
            impl_->UpdateAudioCaptureDiagnostics();
            const auto audioFailure = impl_->audioCore.Snapshot();
            const auto microphone = configuration.audioMode ==
                GStreamerAudioMode::MicrophoneOnly;
            const auto dual = configuration.audioMode ==
                GStreamerAudioMode::Dual;
            const auto runtimeDependencyMissing =
                HRESULT_FACILITY(audioStartResult) == FACILITY_WIN32 &&
                HRESULT_CODE(audioStartResult) == ERROR_MOD_NOT_FOUND;
            const auto failureStage = runtimeDependencyMissing
                ? "ResolveGStreamerRuntimeDependency"
                : dual
                    ? "StartGStreamerDualPipeline"
                    : microphone
                        ? "StartGStreamerMicrophonePipeline"
                        : "StartGStreamerSystemPipeline";
            const auto failureMessage = runtimeDependencyMissing
                ? L"GStreamer 1.28.6 private audio runtime is incomplete."
                : (microphone || dual) &&
                    audioFailure.lastGStreamerError == L"MicUnavailableAtStart"
                    ? L"MicUnavailableAtStart"
                    : audioFailure.lastGStreamerError.c_str();
            RecordFailure(
                diagnostics, failureStage, audioStartResult);
            diagnostics.stopReason = runtimeDependencyMissing
                ? "GStreamerRuntimeDependencyMissing"
                : dual
                    ? "GStreamerDualAudioInitializationFailed"
                    : microphone
                        ? "MicrophoneNotAvailable"
                        : "SystemAudioInitializationFailed";
            impl_->PublishFailure(
                XbPreviewResult_NativeFailure,
                audioStartResult,
                failureMessage);
            impl_->PersistManifestStartFailure(
                audioStartResult,
                failureMessage);
            impl_->WriteSummary();
            impl_->ReleaseLifetimeOwner();
            return XbPreviewResult_NativeFailure;
        }
        if (!tap.RegisterConsumer(RenderFrameTapConsumerKind::Encoder))
        {
            (void)impl_->audioCore.Stop();
            impl_->UpdateAudioCaptureDiagnostics();
            diagnostics.encoderState = VideoEncoderState::Failed;
            diagnostics.stopReason = "ConsumerConflict";
            diagnostics.consumerConflict = true;
            diagnostics.tapConsumerMode = "Conflict";
            diagnostics.failureStage = "RegisterTapConsumer";
            diagnostics.failureHResult = HRESULT_FROM_WIN32(ERROR_BUSY);
            impl_->PublishFailure(
                XbPreviewResult_InvalidState,
                diagnostics.failureHResult,
                L"The RenderFrameTap already has an active consumer.");
            impl_->PersistManifestStartFailure(
                diagnostics.failureHResult,
                L"The RenderFrameTap already has an active consumer.");
            impl_->WriteSummary();
            impl_->ReleaseLifetimeOwner();
            return XbPreviewResult_InvalidState;
        }
        ConsumerRegistrationGuard registration(tap);
        diagnostics.tapConsumerMode = "EncoderConsumer";
        impl_->tap = &tap;
        impl_->device.copy_from(device);
        impl_->context.copy_from(immediateContext);
        impl_->stopRequested.store(false);
        try
        {
            impl_->thread = std::thread([this] { impl_->ThreadMain(); });
        }
        catch (const std::bad_alloc&)
        {
            (void)impl_->audioCore.Stop();
            impl_->UpdateAudioCaptureDiagnostics();
            impl_->tap = nullptr;
            impl_->device = nullptr;
            impl_->context = nullptr;
            impl_->PersistManifestStartFailure(
                E_OUTOFMEMORY,
                L"Encoder worker thread allocation failed.");
            impl_->ReleaseLifetimeOwner();
            throw;
        }
        catch (...)
        {
            (void)impl_->audioCore.Stop();
            impl_->UpdateAudioCaptureDiagnostics();
            impl_->tap = nullptr;
            impl_->device = nullptr;
            impl_->context = nullptr;
            impl_->PersistManifestStartFailure(
                E_UNEXPECTED,
                L"Encoder worker thread creation failed.");
            impl_->ReleaseLifetimeOwner();
            throw;
        }
        registration.Release();
        return XbPreviewResult_Ok;
    }

    XbPreviewResult VideoEncoderConsumer::StopAndJoin(
        const RecordingTerminationDisposition disposition) noexcept
    {
        if (!impl_)
        {
            return XbPreviewResult_Ok;
        }
        try
        {
            std::lock_guard stopLock(impl_->stopMutex);
            const auto selectedDisposition =
                impl_->ClaimTerminationDisposition(disposition);
            impl_->stopRequested.store(true);
            (void)impl_->pauseControl.CancelForStop();
            (void)impl_->audioPauseControl.CancelForStop();
            impl_->PublishStopping();
            if (impl_->tap)
            {
                impl_->tap->RequestConsumerStop(
                    RenderFrameTapConsumerKind::Encoder, true);
            }
            // Stop the media source first. Persistence is observational and
            // must never be the prerequisite for safe Stop/Finalize.
            impl_->PersistManifestStopping();
#if defined(XBPREVIEW_NATIVE_TESTS)
            if (impl_->configuration.lifecycleTestHooks != nullptr)
            {
                Impl::ObserveLifecycleBoundary(
                    impl_->configuration.lifecycleTestHooks->stoppingReached,
                    impl_->configuration.lifecycleTestHooks->
                        continueAfterStopping);
            }
#endif

            if (impl_->thread.joinable())
            {
                if (impl_->thread.get_id() == std::this_thread::get_id())
                {
                    impl_->outcome.readyForPublishCandidate = false;
                    impl_->outcome.failureHResult =
                        HRESULT_FROM_WIN32(ERROR_POSSIBLE_DEADLOCK);
                    impl_->outcome.failureStage = WorkerFailureStage::Join;
                    return XbPreviewResult_InvalidState;
                }
                const auto joinStarted = std::chrono::steady_clock::now();
                impl_->thread.join();
                impl_->diagnostics.encoderJoinDurationMs =
                    std::chrono::duration<double, std::milli>(
                        std::chrono::steady_clock::now() - joinStarted).count();
            }

            if (!impl_->terminalPublished && impl_->outcome.workerExited &&
                selectedDisposition ==
                    RecordingTerminationDisposition::UserCancelled)
            {
                PersistentFileIdentityCapture persistedIdentity{};
                const auto persistenceResult =
                    impl_->PersistManifestUserCancelled(
                        impl_->outcome,
                        persistedIdentity);
                if (FAILED(persistenceResult))
                {
                    impl_->diagnostics.outputDeleteAttempted = false;
                    impl_->diagnostics.outputDeleteSucceeded = false;
                    impl_->diagnostics.outputDeleteHResult = S_OK;
                    impl_->PublishCancellationPersistenceFailure(
                        impl_->outcome,
                        persistenceResult);
                    RecordFailure(
                        impl_->diagnostics,
                        "PersistUserCancelled",
                        persistenceResult);
                }
                else
                {
                    const auto cleanup =
                        impl_->CleanupUserCancelledMaterials(
                            impl_->outcome,
                            persistedIdentity);
                    impl_->diagnostics.outputDeleteAttempted =
                        cleanup.attempted;
                    impl_->diagnostics.outputDeleteSucceeded =
                        cleanup.succeeded;
                    impl_->diagnostics.outputDeleteHResult = cleanup.hresult;
                    impl_->diagnostics.outputSuccess = false;
                    if (cleanup.succeeded &&
                        impl_->diagnostics.encoderState !=
                            VideoEncoderState::Failed &&
                        impl_->diagnostics.encoderState !=
                            VideoEncoderState::Unsupported)
                    {
                        if (!IsVideoEncoderStateTransitionAllowed(
                                impl_->diagnostics.encoderState,
                                VideoEncoderState::UserCancelled))
                        {
                            ++impl_->diagnostics.
                                invalidStateTransitionDetected;
                        }
                        impl_->diagnostics.encoderState =
                            VideoEncoderState::UserCancelled;
                    }
                    impl_->PublishUserCancelledTerminal(
                        impl_->outcome,
                        cleanup);
                    if (!cleanup.succeeded)
                    {
                        RecordFailure(
                            impl_->diagnostics,
                            "UserCancelledCleanup",
                            cleanup.hresult);
                    }
                }
                impl_->WriteSummary();
                // The durable terminal fact (or retained recovery state) and
                // all cancellation cleanup decisions precede owner release.
                impl_->ReleaseLifetimeOwner();
            }

            if (!impl_->terminalPublished && impl_->outcome.workerExited)
            {
                auto readyToPublish =
                    impl_->outcome.readyForPublishCandidate &&
                    impl_->outcome.finalizeAttempted &&
                    impl_->outcome.finalizeCount == 1 &&
                    SUCCEEDED(impl_->outcome.finalizeHResult) &&
                    impl_->outcome.validationAttempted &&
                    SUCCEEDED(impl_->outcome.validationHResult) &&
                    impl_->outcome.residualOutstanding == 0;
                if (readyToPublish)
                {
                    const auto workingFileResult =
                        ValidateWorkingFileForPublish(
                            impl_->outcome.outputPath);
                    if (FAILED(workingFileResult))
                    {
                        readyToPublish = false;
                        impl_->outcome.readyForPublishCandidate = false;

                        impl_->outcome.failureHResult = workingFileResult;
                        impl_->outcome.failureStage =
                            WorkerFailureStage::WorkingFile;
                    }
                }
                if (readyToPublish && impl_->configuration.audioEnabled)
                {
                    const auto audioFinalizeResult =
                        impl_->PrepareGStreamerAudioFinalCandidate();
                    impl_->outcome.validationAttempted = true;
                    impl_->outcome.validationHResult = audioFinalizeResult;
                    if (FAILED(audioFinalizeResult))
                    {
                        readyToPublish = false;
                        impl_->outcome.readyForPublishCandidate = false;
                        impl_->outcome.failureHResult = audioFinalizeResult;
                        impl_->outcome.failureStage =
                            impl_->audioFinalize.outputCreated
                                ? WorkerFailureStage::RuntimeValidation
                                : WorkerFailureStage::Finalize;
                    }
                    else
                    {
                        impl_->diagnostics.sourceReaderValidation = "PASS";
                        const auto& audioValidation =
                            impl_->audioFinalize.validation;
                        impl_->diagnostics.gStreamerValidatedSampleRate =
                            audioValidation.sampleRate;
                        impl_->diagnostics.gStreamerValidatedChannels =
                            audioValidation.channels;
                        impl_->diagnostics.gStreamerDecodedAudioFrames =
                            audioValidation.decodedFrames;
                        impl_->diagnostics.gStreamerAudioPeakAbsolutePcm16 =
                            audioValidation.peakAbsolutePcm16;
                        impl_->diagnostics.gStreamerAudioRmsPcm16 =
                            audioValidation.rmsPcm16;
                        impl_->diagnostics.gStreamerAudioDcPcm16 =
                            audioValidation.dcPcm16;
                        impl_->diagnostics.gStreamerAudioSaturatedSamples =
                            audioValidation.saturatedSamples;
                        impl_->diagnostics.
                            gStreamerValidatedAudioDuration100ns =
                                audioValidation.audioDuration100ns;
                        impl_->diagnostics.
                            gStreamerValidatedAudioReachedEndOfStream =
                                audioValidation.audioReachedEndOfStream;
                        impl_->diagnostics.gStreamerFinalIntegratedLufs =
                            audioValidation.integratedLufs;
                        impl_->diagnostics.gStreamerFinalTruePeakDbtp =
                            audioValidation.truePeakDbtp;
                        impl_->diagnostics.gStreamerFinalLoudnessValidated =
                            audioValidation.finalLoudnessValidated;
                        impl_->diagnostics.
                            gStreamerMicrophoneMasteringApplied =
                                impl_->audioFinalize.
                                    microphoneMasteringApplied;
                        impl_->diagnostics.gStreamerDualMixApplied =
                            impl_->audioFinalize.dualMixApplied;
                        const auto finalWorkingResult =
                            ValidateWorkingFileForPublish(
                                impl_->outcome.outputPath);
                        if (FAILED(finalWorkingResult))
                        {
                            readyToPublish = false;
                            impl_->outcome.readyForPublishCandidate = false;
                            impl_->outcome.failureHResult = finalWorkingResult;
                            impl_->outcome.failureStage =
                                WorkerFailureStage::WorkingFile;
                        }
                    }
                }
                if (readyToPublish)
                {
                    // This call occurs only after worker exit and join, with
                    // Finalize/Validation successful and native residuals zero.
                    impl_->PersistManifestReady(impl_->outcome);
#if defined(XBPREVIEW_NATIVE_TESTS)
                    if (impl_->configuration.lifecycleTestHooks != nullptr)
                    {
                        Impl::ObserveLifecycleBoundary(
                            impl_->configuration.lifecycleTestHooks->
                                readyToPublishReached,
                            impl_->configuration.lifecycleTestHooks->
                                continueAfterReadyToPublish);
                    }
#endif
                }

                PublishOutcome publish{};
                PersistentFileIdentityCapture publishedIdentity{};
                if (readyToPublish)
                {
                    if (impl_->configuration.publishOnStop)
                    {
                        if (impl_->configuration.faultInjection ==
                            VideoEncoderFaultInjection::
                                PublishConflictAtTarget)
                        {
                            CreatePublishConflictForTest(
                                impl_->configuration.plannedFinalPath);
                        }
                        publish = PublishSessionOutput(
                            impl_->outcome.outputPath,
                            impl_->configuration.plannedFinalPath);
#if defined(XBPREVIEW_NATIVE_TESTS)
                        if (publish.succeeded &&
                            impl_->configuration.lifecycleTestHooks != nullptr)
                        {
                            Impl::ObserveLifecycleBoundary(
                                impl_->configuration.lifecycleTestHooks->
                                    publishMoveReached,
                                impl_->configuration.lifecycleTestHooks->
                                    continueAfterPublishMove);
                        }
#endif
                    }
                    else
                    {
                        // The opt-in P2.4 diagnostic encoder retains its
                        // historical direct-to-MP4 behavior. Formal recording
                        // configurations always set publishOnStop.
                        publish.succeeded = true;
                        publish.hresult = S_OK;
                        wcsncpy_s(
                            publish.publishedPath,
                            impl_->outcome.outputPath,
                            _TRUNCATE);
                    }
                    if (publish.succeeded)
                    {
                        if (impl_->configuration.faultInjection ==
                            VideoEncoderFaultInjection::
                                PostPublishIdentityVerificationFailure)
                        {
                            publishedIdentity.hresult =
                                HRESULT_FROM_WIN32(ERROR_ACCESS_DENIED);
                        }
                        else
                        {
                            publishedIdentity = CapturePersistentFileIdentity(
                                std::filesystem::path(publish.publishedPath));
                            if (publishedIdentity.Succeeded() &&
                                !impl_->workingFileIdentity.available)
                            {
                                publishedIdentity.hresult =
                                    HRESULT_FROM_WIN32(ERROR_NOT_READY);
                                publishedIdentity.identity = {};
                            }
                        }
                    }
                    if (publish.succeeded &&
                        impl_->configuration.faultInjection ==
                            VideoEncoderFaultInjection::
                                SnapshotExceptionAfterPublish)
                    {
                        // The durable rename fact is committed before any
                        // later Snapshot publication work. A post-move
                        // exception cannot route the final file through
                        // failure cleanup or negate Published.
                        try
                        {
                            throw E_UNEXPECTED;
                        }
                        catch (...)
                        {
                            // PublishOutcome is fixed storage. Preserve it and
                            // continue directly to terminal Snapshot publication.
                        }
                        // Preserve the durable Ready revision to model the
                        // crash window after rename but before Published
                        // metadata reaches stable storage.
                        impl_->manifestWritable = false;
                    }
                }

                const auto cleanup = DeleteStartupGarbage(
                    impl_->outcome,
                    publish.succeeded);
                impl_->diagnostics.outputDeleteAttempted = cleanup.attempted;
                impl_->diagnostics.outputDeleteSucceeded = cleanup.succeeded;
                impl_->diagnostics.outputDeleteHResult = cleanup.hresult;
                if (publish.succeeded)
                {
                    if (!IsVideoEncoderStateTransitionAllowed(
                            impl_->diagnostics.encoderState,
                            VideoEncoderState::Completed))
                    {
                        ++impl_->diagnostics.invalidStateTransitionDetected;
                    }
                    impl_->diagnostics.encoderState =
                        VideoEncoderState::Completed;
                    impl_->diagnostics.outputSuccess = true;
                }
                if (publish.succeeded)
                {
                    impl_->PersistManifestPublished(
                        impl_->outcome,
                        publish,
                        publishedIdentity);
                    if (impl_->GStreamerAudioCleanupAllowed())
                    {
                        impl_->CleanupGStreamerAudioMaterialsAfterPublish();
                    }
                }
                else
                {
                    impl_->PersistManifestFailed(
                        impl_->outcome,
                        readyToPublish,
                        publish);
                }
                impl_->PublishTerminal(
                    impl_->outcome,
                    readyToPublish,
                    publish,
                    cleanup);
                // Dynamic diagnostic strings are secondary. The fixed terminal
                // Snapshot is visible before any diagnostic formatting can fail.
                if (readyToPublish && !publish.succeeded)
                {
                    RecordFailure(
                        impl_->diagnostics,
                        "PublishOutput",
                        publish.hresult);
                }
                else if (impl_->outcome.failureStage ==
                    WorkerFailureStage::WorkingFile)
                {
                    RecordFailure(
                        impl_->diagnostics,
                        "WorkingFile",
                        impl_->outcome.failureHResult);
                }
                else if (!publish.succeeded &&
                    impl_->diagnostics.encoderState !=
                        VideoEncoderState::Failed)
                {
                    RecordFailure(
                        impl_->diagnostics,
                        "WorkerFailure",
                        impl_->outcome.failureHResult);
                }
                impl_->WriteSummary();
                // Release only after all media, cleanup, terminal Manifest,
                // Snapshot, and diagnostics mutations for this Session end.
                // The rendezvous file remains; handle lifetime is the fact.
                impl_->ReleaseLifetimeOwner();
            }
            impl_->tap = nullptr;
            XbRecordingSnapshot snapshot{};
            impl_->CopySnapshot(snapshot);
            return snapshot.state == XbRecordingState_Failed ||
                (snapshot.state == XbRecordingState_UserCancelled &&
                    snapshot.lastResult != XbPreviewResult_Ok)
                ? XbPreviewResult_NativeFailure
                : XbPreviewResult_Ok;
        }
        catch (const std::system_error&)
        {
            impl_->outcome.readyForPublishCandidate = false;
            impl_->outcome.failureHResult = E_FAIL;
            impl_->outcome.failureStage = WorkerFailureStage::Join;
            if (!impl_->thread.joinable() && !impl_->running.load())
            {
                impl_->ReleaseLifetimeOwner();
            }
            return XbPreviewResult_NativeFailure;
        }
        catch (...)
        {
            impl_->outcome.readyForPublishCandidate = false;
            impl_->outcome.failureHResult = E_UNEXPECTED;
            impl_->outcome.failureStage = WorkerFailureStage::Join;
            if (!impl_->thread.joinable() && !impl_->running.load())
            {
                impl_->ReleaseLifetimeOwner();
            }
            return XbPreviewResult_NativeFailure;
        }
    }

    XbPreviewResult VideoEncoderConsumer::RequestVideoPause() noexcept
    {
        try
        {
            std::lock_guard lock(impl_->stopMutex);
            if (!impl_->running.load(std::memory_order_acquire) ||
                impl_->stopRequested.load(std::memory_order_acquire))
            {
                return XbPreviewResult_InvalidState;
            }
            if (impl_->pauseControl.Phase() !=
                    VideoPauseWorkerPhase::Running ||
                impl_->audioPauseControl.Phase() !=
                    AudioPauseWorkerPhase::Running)
            {
                return XbPreviewResult_InvalidState;
            }
            if (!impl_->PublishPauseRequested())
            {
                return XbPreviewResult_InvalidState;
            }
            // The worker advances neither half until both requests are
            // visible, so this pair is an internal full-A/V request barrier.
            if (!impl_->audioPauseControl.RequestPause() ||
                !impl_->pauseControl.RequestPause())
            {
                return XbPreviewResult_NativeFailure;
            }
            return XbPreviewResult_Ok;
        }
        catch (...)
        {
            return XbPreviewResult_NativeFailure;
        }
    }

    XbPreviewResult VideoEncoderConsumer::RequestVideoResume() noexcept
    {
        try
        {
            std::lock_guard lock(impl_->stopMutex);
            if (!impl_->running.load(std::memory_order_acquire) ||
                impl_->stopRequested.load(std::memory_order_acquire) ||
                impl_->tap == nullptr)
            {
                return XbPreviewResult_InvalidState;
            }
            if (impl_->pauseControl.Phase() !=
                    VideoPauseWorkerPhase::Paused ||
                impl_->audioPauseControl.Phase() !=
                    AudioPauseWorkerPhase::Paused)
            {
                return XbPreviewResult_InvalidState;
            }
            // RenderFrameTap snapshots under its queue mutex. framesEnqueued
            // therefore gives a linearizable cutoff equal to the highest
            // frameSequence that existed when the Resume gate was opened.
            const auto cutoff =
                impl_->tap->Diagnostics().framesEnqueued;
            if (!impl_->PublishResumeRequested())
            {
                return XbPreviewResult_InvalidState;
            }
            if (!impl_->audioPauseControl.RequestResume() ||
                !impl_->pauseControl.RequestResume(cutoff))
            {
                return XbPreviewResult_NativeFailure;
            }
            return XbPreviewResult_Ok;
        }
        catch (...)
        {
            return XbPreviewResult_NativeFailure;
        }
    }

    VideoPauseWorkerSnapshot VideoEncoderConsumer::GetVideoPauseSnapshot()
        const noexcept
    {
        return impl_->pauseControl.Snapshot();
    }

    void VideoEncoderConsumer::GetSnapshot(
        XbRecordingSnapshot& snapshot) const
    {
        impl_->CopySnapshot(snapshot);
    }

    XbPreviewResult VideoEncoderConsumer::SetAudioControls(
        const XbAudioControlsV1& controls) noexcept
    {
        if (controls.systemMuted > 1 || controls.microphoneMuted > 1 ||
            !std::isfinite(controls.microphoneGainDb))
        {
            return XbPreviewResult_InvalidArgument;
        }
        // MVP GStreamer mixing is deliberately fixed at 1.0 / 1.0. The
        // compatibility ABI remains, but product-specific gain, ducking and
        // custom DSP controls are not part of this AudioCore.
        if (controls.systemMuted != 0 || controls.microphoneMuted != 0 ||
            controls.microphoneGainDb != 0.0)
        {
            return XbPreviewResult_InvalidArgument;
        }
        std::lock_guard lock(impl_->audioControlMutex);
        if (++impl_->audioControlRevision == 0)
            ++impl_->audioControlRevision;
        return XbPreviewResult_Ok;
    }

    void VideoEncoderConsumer::GetAudioControlSnapshot(
        XbAudioControlSnapshotV1& snapshot) const noexcept
    {
        std::uint64_t revision{};
        {
            std::lock_guard lock(impl_->audioControlMutex);
            revision = impl_->audioControlRevision;
        }
        snapshot = {};
        snapshot.structSize = sizeof(snapshot);
        snapshot.abiVersion = XB_AUDIO_CONTROLS_ABI_VERSION_V1;
        snapshot.microphoneGainLinear = 1.0;
        snapshot.programHeadroomCoefficient = 1.0;
        snapshot.controlRevision = revision;
        snapshot.pendingControlRevision = revision;
    }


    void VideoEncoderConsumer::RecordExternalFailure(
        const XbPreviewResult result,
        const HRESULT hresult,
        const wchar_t* const message)
    {
        if (!impl_->running.load())
        {
            impl_->tap = nullptr;
            impl_->device = nullptr;
            impl_->context = nullptr;
            impl_->PublishExternalFailure(result, hresult, message);
        }
    }

    bool VideoEncoderConsumer::Running() const noexcept
    {
        return impl_ && impl_->running.load();
    }
}
