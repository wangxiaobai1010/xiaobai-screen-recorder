#include "RecordingOutputRoot.h"

#include <new>
#include <system_error>

namespace xbpreview
{
    std::filesystem::path ResolveArtifactsRoot(
        const std::filesystem::path& diagnosticDirectory)
    {
        // diagnosticDirectory is artifacts/bin/<Configuration>/x64/diagnostic-logs.
        auto path = diagnosticDirectory;
        for (int index = 0; index < 4 && path.has_parent_path(); ++index)
        {
            path = path.parent_path();
        }
        return path;
    }

    RecordingOutputRootResolution ResolveRecordingOutputRoots(
        const std::filesystem::path& diagnosticDirectory) noexcept
    {
        try
        {
            if (diagnosticDirectory.empty() ||
                !diagnosticDirectory.is_absolute())
            {
                return {};
            }

            auto artifactsRoot = diagnosticDirectory;
            for (int index = 0; index < 4; ++index)
            {
                if (!artifactsRoot.has_parent_path())
                {
                    return {};
                }
                const auto parent = artifactsRoot.parent_path();
                if (parent == artifactsRoot || parent.empty())
                {
                    return {};
                }
                artifactsRoot = parent;
            }
            return ResolveRecordingOutputRootsFromManagedRoot(
                artifactsRoot / L"p2.5a-recordings");
        }
        catch (const std::bad_alloc&)
        {
            RecordingOutputRootResolution result{};
            result.status = RecordingOutputRootStatus::Failure;
            result.hresult = E_OUTOFMEMORY;
            return result;
        }
        catch (const std::filesystem::filesystem_error& error)
        {
            RecordingOutputRootResolution result{};
            result.status = RecordingOutputRootStatus::Failure;
            result.hresult = HRESULT_FROM_WIN32(
                static_cast<DWORD>(error.code().value()));
            return result;
        }
        catch (...)
        {
            RecordingOutputRootResolution result{};
            result.status = RecordingOutputRootStatus::Failure;
            result.hresult = E_UNEXPECTED;
            return result;
        }
    }

    RecordingOutputRootResolution ResolveRecordingOutputRootsFromManagedRoot(
        const std::filesystem::path& managedOutputRoot) noexcept
    {
        try
        {
            if (managedOutputRoot.empty() || !managedOutputRoot.is_absolute())
            {
                return {};
            }
            RecordingOutputRootResolution result{};
            result.status = RecordingOutputRootStatus::Resolved;
            result.hresult = S_OK;
            result.mediaOutputRoot = managedOutputRoot;
            result.sessionsRoot = result.mediaOutputRoot / L"sessions";
            return result;
        }
        catch (const std::bad_alloc&)
        {
            RecordingOutputRootResolution result{};
            result.status = RecordingOutputRootStatus::Failure;
            result.hresult = E_OUTOFMEMORY;
            return result;
        }
        catch (const std::filesystem::filesystem_error& error)
        {
            RecordingOutputRootResolution result{};
            result.status = RecordingOutputRootStatus::Failure;
            result.hresult = HRESULT_FROM_WIN32(
                static_cast<DWORD>(error.code().value()));
            return result;
        }
        catch (...)
        {
            RecordingOutputRootResolution result{};
            result.status = RecordingOutputRootStatus::Failure;
            result.hresult = E_UNEXPECTED;
            return result;
        }
    }
}
