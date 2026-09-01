using System.Diagnostics;
using System.Runtime.InteropServices;

namespace XbPreview.Host;

internal sealed unsafe class NativeHistoricalSessionInspector :
    IStartupSessionInspector
{
    private const uint MaximumStringLength = 32 * 1024;
    private readonly string _rootPath;
    private readonly bool _useExplicitOutputRoot;

    internal NativeHistoricalSessionInspector(string diagnosticLogDirectory)
        : this(diagnosticLogDirectory, useExplicitOutputRoot: false)
    {
    }

    private NativeHistoricalSessionInspector(
        string rootPath,
        bool useExplicitOutputRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        _rootPath = rootPath;
        _useExplicitOutputRoot = useExplicitOutputRoot;
    }

    internal static NativeHistoricalSessionInspector ForOutputRoot(
        string effectiveOutputRoot) => new(
            effectiveOutputRoot,
            useExplicitOutputRoot: true);

    public StartupInspectionResult Inspect(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateAbi();

        long started = Stopwatch.GetTimestamp();
        nint rootPath = Marshal.StringToHGlobalUni(_rootPath);
        nint scanHandle = nint.Zero;
        try
        {
            NativeMethods.HistoricalSessionScanSummaryV1 summary = new()
            {
                StructSize = (uint)sizeof(
                    NativeMethods.HistoricalSessionScanSummaryV1),
                AbiVersion = NativeMethods.HistoricalSessionScanAbiVersionV1,
            };

            Ensure(
                BeginScan(rootPath, out scanHandle, ref summary),
                "begin historical Session scan");
            if (scanHandle == nint.Zero)
            {
                throw new InvalidOperationException(
                    "Native historical Session scan returned a null handle.");
            }
            cancellationToken.ThrowIfCancellationRequested();
            ValidateSummary(summary);

            List<HistoricalSessionInspection> sessions = new(
                checked((int)summary.SessionCount));
            for (uint index = 0; index < summary.SessionCount; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                sessions.Add(ReadSession(scanHandle, index, cancellationToken));
            }

            return new StartupInspectionResult(
                (HistoricalSessionScanStatus)summary.Status,
                summary.DiagnosticHResult,
                Stopwatch.GetElapsedTime(started),
                summary.SessionCount,
                summary.UnrecognizedEntryCount,
                summary.EntriesObserved,
                summary.MaximumEntries,
                summary.Truncated != 0,
                summary.MediaWithoutSessionDirectoryBlindSpot != 0,
                sessions);
        }
        finally
        {
            Marshal.FreeHGlobal(rootPath);
            if (scanHandle != nint.Zero)
            {
                NativeMethods.Result destroy =
                    NativeMethods.XbPreview_DestroyHistoricalSessionScanV1(
                        ref scanHandle);
                Ensure(destroy, "destroy historical Session scan");
            }
        }
    }

    private NativeMethods.Result BeginScan(
        nint rootPath,
        out nint scanHandle,
        ref NativeMethods.HistoricalSessionScanSummaryV1 summary)
    {
        if (_useExplicitOutputRoot)
        {
            NativeMethods.HistoricalSessionScanOutputRootOptionsV1 options = new()
            {
                StructSize = (uint)sizeof(
                    NativeMethods.HistoricalSessionScanOutputRootOptionsV1),
                AbiVersion = NativeMethods.HistoricalSessionScanAbiVersionV1,
                MediaOutputRoot = rootPath,
                MaximumEntries =
                    NativeMethods.HistoricalSessionScanMaximumEntriesV1,
            };
            return NativeMethods.
                XbPreview_BeginHistoricalSessionScanForOutputRootV1(
                    in options,
                    out scanHandle,
                    ref summary);
        }

        NativeMethods.HistoricalSessionScanOptionsV1 legacyOptions = new()
        {
            StructSize = (uint)sizeof(
                NativeMethods.HistoricalSessionScanOptionsV1),
            AbiVersion = NativeMethods.HistoricalSessionScanAbiVersionV1,
            DiagnosticLogDirectory = rootPath,
            MaximumEntries =
                NativeMethods.HistoricalSessionScanMaximumEntriesV1,
        };
        return NativeMethods.XbPreview_BeginHistoricalSessionScanV1(
            in legacyOptions,
            out scanHandle,
            ref summary);
    }

    private static HistoricalSessionInspection ReadSession(
        nint scanHandle,
        uint index,
        CancellationToken cancellationToken)
    {
        NativeMethods.HistoricalSessionItemV1 item = new()
        {
            StructSize = (uint)sizeof(NativeMethods.HistoricalSessionItemV1),
            AbiVersion = NativeMethods.HistoricalSessionScanAbiVersionV1,
        };
        Ensure(
            NativeMethods.XbPreview_GetHistoricalSessionV1(
                scanHandle,
                index,
                ref item),
            $"read historical Session item {index}");
        ValidateItem(item, index);

        cancellationToken.ThrowIfCancellationRequested();
        string sessionId = ReadItemString(
            scanHandle,
            index,
            NativeMethods.HistoricalSessionStringFieldV1.SessionId);
        if (!Guid.TryParseExact(sessionId, "D", out Guid parsedSessionId) ||
            !string.Equals(
                sessionId,
                parsedSessionId.ToString("D").ToUpperInvariant(),
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Historical Session item {index} has a non-canonical SessionId.");
        }
        bool workingExists = item.WorkingFilesystemState ==
            NativeMethods.HistoricalSessionFilesystemStateV1.Exists;
        bool plannedFinalExists = item.PlannedFinalFilesystemState ==
            NativeMethods.HistoricalSessionFilesystemStateV1.Exists;
        bool publishedExists = item.PublishedFilesystemState ==
            NativeMethods.HistoricalSessionFilesystemStateV1.Exists;

        string displaySafePath = string.Empty;
        if (publishedExists)
        {
            displaySafePath = ReadItemString(
                scanHandle,
                index,
                NativeMethods.HistoricalSessionStringFieldV1.
                    PublishedCandidatePath);
        }
        else if (plannedFinalExists)
        {
            displaySafePath = ReadItemString(
                scanHandle,
                index,
                NativeMethods.HistoricalSessionStringFieldV1.
                    PlannedFinalCandidatePath);
        }
        else if (workingExists)
        {
            displaySafePath = ReadItemString(
                scanHandle,
                index,
                NativeMethods.HistoricalSessionStringFieldV1.
                    WorkingCandidatePath);
        }
        if ((workingExists || plannedFinalExists || publishedExists) &&
            (string.IsNullOrEmpty(displaySafePath) ||
             displaySafePath.IndexOf('\0') >= 0 ||
             !Path.IsPathFullyQualified(displaySafePath)))
        {
            throw new InvalidOperationException(
                $"Historical Session item {index} returned a non-absolute " +
                "display path.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        return new HistoricalSessionInspection(
            sessionId,
            item.ObservedRevisionAvailable != 0
                ? item.ObservedRevision
                : null,
            (HistoricalSessionClassification)item.Classification,
            (HistoricalSessionSeverity)item.Severity,
            (HistoricalSessionReason)item.Reasons,
            RetainUserMedia: true,
            WorkingCandidateExists: workingExists,
            FinalCandidateExists: plannedFinalExists || publishedExists,
            DisplaySafePath: displaySafePath,
            (HistoricalSessionParseStatus)item.ManifestParseStatus,
            item.ManifestParseHResult,
            (HistoricalSessionOwnerState)item.OwnerState,
            item.OwnerHResult);
    }

    private static void ValidateAbi()
    {
        NativeMethods.ValidateManagedLayout();
        uint apiVersion = NativeMethods.XbPreview_GetApiVersion();
        if (apiVersion != NativeMethods.ApiVersion)
        {
            throw new InvalidOperationException(
                $"Historical Session scan requires Native API " +
                $"0x{NativeMethods.ApiVersion:X8}; actual=0x{apiVersion:X8}.");
        }

        NativeMethods.HistoricalSessionScanAbiLayoutV1 layout = new()
        {
            StructSize = (uint)sizeof(
                NativeMethods.HistoricalSessionScanAbiLayoutV1),
            AbiVersion = NativeMethods.HistoricalSessionScanAbiVersionV1,
        };
        Ensure(
            NativeMethods.XbPreview_GetHistoricalSessionScanAbiLayoutV1(
                ref layout),
            "read historical Session scan ABI layout");
        if (layout.StructSize !=
                NativeMethods.ExpectedHistoricalSessionScanAbiLayoutV1Size ||
            layout.AbiVersion !=
                NativeMethods.HistoricalSessionScanAbiVersionV1 ||
            layout.PointerSize != 8 ||
            layout.Packing != 8 ||
            layout.WcharSize != 2 ||
            layout.OptionsSize !=
                NativeMethods.ExpectedHistoricalSessionScanOptionsV1Size ||
            layout.SummarySize !=
                NativeMethods.ExpectedHistoricalSessionScanSummaryV1Size ||
            layout.ItemSize !=
                NativeMethods.ExpectedHistoricalSessionItemV1Size)
        {
            throw new InvalidOperationException(
                "Native/Managed historical Session scan ABI mismatch.");
        }
    }

    private static void ValidateSummary(
        NativeMethods.HistoricalSessionScanSummaryV1 summary)
    {
        if (summary.StructSize !=
                NativeMethods.ExpectedHistoricalSessionScanSummaryV1Size ||
            summary.AbiVersion !=
                NativeMethods.HistoricalSessionScanAbiVersionV1 ||
            !Enum.IsDefined(summary.Status) ||
            summary.SessionCount >
                NativeMethods.HistoricalSessionScanMaximumEntriesV1 ||
            summary.SessionCount + (ulong)summary.UnrecognizedEntryCount >
                summary.EntriesObserved ||
            summary.MaximumEntries >
                NativeMethods.HistoricalSessionScanMaximumEntriesV1 ||
            !IsBoolean32(summary.Truncated) ||
            !IsBoolean32(summary.MediaWithoutSessionDirectoryBlindSpot) ||
            summary.Reserved1 != 0 ||
            summary.Reserved2 != 0)
        {
            throw new InvalidOperationException(
                "Native historical Session scan summary is invalid.");
        }
    }

    private static void ValidateItem(
        NativeMethods.HistoricalSessionItemV1 item,
        uint index)
    {
        if (item.StructSize != NativeMethods.ExpectedHistoricalSessionItemV1Size ||
            item.AbiVersion != NativeMethods.HistoricalSessionScanAbiVersionV1 ||
            !Enum.IsDefined(item.Classification) ||
            !Enum.IsDefined(item.Severity) ||
            !Enum.IsDefined(item.ManifestParseStatus) ||
            !Enum.IsDefined(item.ManifestSemanticIssue) ||
            !Enum.IsDefined(item.ManifestState) ||
            !Enum.IsDefined(item.OwnerState) ||
            !Enum.IsDefined(item.WorkingFilesystemState) ||
            !Enum.IsDefined(item.PlannedFinalFilesystemState) ||
            !Enum.IsDefined(item.PublishedFilesystemState) ||
            (item.Reasons & ~((1UL << 23) - 1)) != 0 ||
            !IsBoolean32(item.ObservedSchemaVersionAvailable) ||
            !IsBoolean32(item.ObservedRevisionAvailable) ||
            !IsBoolean32(item.ManifestAvailable) ||
            !IsBoolean32(item.ManifestRevisionStable) ||
            !IsBoolean32(item.WorkingSizeAvailable) ||
            !IsBoolean32(item.PlannedFinalSizeAvailable) ||
            !IsBoolean32(item.PublishedSizeAvailable) ||
            !IsBoolean32(item.PersistentWorkingIdentityAvailable) ||
            !IsBoolean32(item.PersistentIdentityComparisonAttempted) ||
            !IsBoolean32(item.StrongIdentityMatch) ||
            !IsBoolean32(item.DeleteAllowed) ||
            !IsBoolean32(item.ReconciliationAuthorized) ||
            item.DeleteAllowed != 0 ||
            item.ReconciliationAuthorized != 0 ||
            item.Reserved0 != 0 ||
            item.Reserved1 != 0 ||
            item.Reserved2 != 0 ||
            item.Reserved3 != 0 ||
            item.Reserved4 != 0 ||
            item.Reserved5 != 0 ||
            item.Reserved6 != 0)
        {
            throw new InvalidOperationException(
                $"Native historical Session item {index} failed closed validation.");
        }
    }

    private static string ReadItemString(
        nint scanHandle,
        uint index,
        NativeMethods.HistoricalSessionStringFieldV1 field)
    {
        NativeMethods.Result probe =
            NativeMethods.XbPreview_GetHistoricalSessionStringV1(
                scanHandle,
                index,
                field,
                null,
                0,
                out uint requiredLength);
        if (probe is not (
                NativeMethods.Result.Ok or
                NativeMethods.Result.InsufficientBuffer))
        {
            Ensure(probe, $"probe historical Session string {field}");
        }
        if (requiredLength == 0 || requiredLength > MaximumStringLength)
        {
            throw new InvalidOperationException(
                $"Historical Session string {field} length is invalid: " +
                $"{requiredLength}.");
        }

        char[] buffer = new char[requiredLength];
        fixed (char* pointer = buffer)
        {
            Ensure(
                NativeMethods.XbPreview_GetHistoricalSessionStringV1(
                    scanHandle,
                    index,
                    field,
                    pointer,
                    (uint)buffer.Length,
                    out uint actualRequired),
                $"read historical Session string {field}");
            if (actualRequired != requiredLength || buffer[^1] != '\0')
            {
                throw new InvalidOperationException(
                    $"Historical Session string {field} changed while reading.");
            }
        }
        return new string(buffer, 0, checked((int)requiredLength - 1));
    }

    private static void Ensure(NativeMethods.Result result, string operation)
    {
        if (result != NativeMethods.Result.Ok)
        {
            throw new InvalidOperationException(
                $"Failed to {operation}: {result}.");
        }
    }

    private static bool IsBoolean32(uint value) => value is 0 or 1;
}
