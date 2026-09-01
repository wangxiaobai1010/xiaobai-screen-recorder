using System.Diagnostics;
using System.Runtime.InteropServices;
using XbPreview.Host;

namespace XbPreview.Managed.Tests;

internal static unsafe class RecoveryRootAlignmentTests
{
    private sealed record AdapterRootSnapshot(
        string CanonicalOutputRoot,
        string PresentedOutputRoot);

    internal static void Run()
    {
        VerifyFormalHostUsesOneEffectiveRoot();
        VerifyNormalCustomRoot();
        VerifyDefaultRoot();
        VerifyReparseRootFallsBack();
        VerifyProbeFailureAndInvalidRootFallback();
        VerifyExplicitNarrowRecovery();
        Console.WriteLine("RECOVERY_ROOT_ALIGNMENT_CONTRACTS=5");
    }

    internal static void RunNormalCustomRootContract()
    {
        VerifyFormalHostUsesOneEffectiveRoot();
        VerifyNormalCustomRoot();
        Console.WriteLine("RECOVERY_NORMAL_CUSTOM_ROOT=PASS");
    }

    internal static void RunRemainingRootContracts()
    {
        VerifyDefaultRoot();
        VerifyReparseRootFallsBack();
        VerifyProbeFailureAndInvalidRootFallback();
        VerifyExplicitNarrowRecovery();
        Console.WriteLine("RECOVERY_REMAINING_ROOT_CONTRACTS=4");
    }

    internal static void RunLegacyDiagnosticAbiRegression()
    {
        VerifyManagedAbiLayouts();

        string root = CreateCaseRoot("legacy-diagnostic-v1");
        string diagnosticDirectory = Path.Combine(
            root, "a", "b", "c", "d");
        string mediaRoot = Path.Combine(root, "p2.5a-recordings");
        Directory.CreateDirectory(diagnosticDirectory);
        string sessionId = CreateMalformedSession(mediaRoot);
        string manifestPath = Path.Combine(
            mediaRoot, "sessions", sessionId, "manifest.json");
        byte[] originalManifest = File.ReadAllBytes(manifestPath);
        try
        {
            string observedRoot = QueryScanRoot(
                diagnosticDirectory,
                useExplicitOutputRoot: false);
            Require(PathEquals(observedRoot, mediaRoot),
                "legacy historical V1 still derives p2.5a-recordings " +
                "from the diagnostic directory");

            StartupInspectionResult scan =
                new NativeHistoricalSessionInspector(diagnosticDirectory).
                    Inspect(CancellationToken.None);
            Require(
                scan.Status == HistoricalSessionScanStatus.Success &&
                scan.Sessions.Single().SessionId == sessionId &&
                scan.Sessions.Single().Classification ==
                    HistoricalSessionClassification.ManifestCorrupt,
                "legacy historical V1 still scans its diagnostic-derived root");

            NarrowRecoveryResult recovery =
                new NativeNarrowRecoveryService(diagnosticDirectory).Recover(
                    sessionId,
                    1,
                    CancellationToken.None);
            Require(
                recovery.Status == NarrowRecoveryStatus.SemanticConflict,
                "legacy narrow V1 still reconciles its diagnostic-derived root");
            VerifyLegacyReservedFieldsRemainRejected(
                diagnosticDirectory,
                sessionId);
            Require(
                File.ReadAllBytes(manifestPath).SequenceEqual(originalManifest),
                "legacy ABI regression does not mutate malformed evidence");
        }
        finally
        {
            DeleteTree(root);
        }

        Console.WriteLine("HISTORICAL_DIAGNOSTIC_V1_REGRESSION=PASS");
    }

    private static void VerifyNormalCustomRoot()
    {
        string root = CreateCaseRoot("custom");
        string customRoot = Path.Combine(root, "custom-output");
        string defaultRoot = Path.Combine(root, "safe-default");
        string diagnosticDirectory = Path.Combine(root, "diagnostic");
        Directory.CreateDirectory(customRoot);
        Directory.CreateDirectory(defaultRoot);
        Directory.CreateDirectory(diagnosticDirectory);
        try
        {
            ProductState state = PersistedState(root, customRoot);
            AdapterRootSnapshot resolved = ResolveWithRealNative(
                state,
                defaultRoot,
                diagnosticDirectory);
            Require(
                PathEquals(resolved.CanonicalOutputRoot, customRoot) &&
                PathEquals(resolved.PresentedOutputRoot, customRoot),
                "normal persisted custom root is the recording and UI truth");
            VerifyExplicitRootFlow(
                resolved.CanonicalOutputRoot,
                verifyNarrowRecovery: false);
        }
        finally
        {
            DeleteTree(root);
        }
    }

    private static void VerifyDefaultRoot()
    {
        string root = CreateCaseRoot("default");
        string defaultRoot = Path.Combine(root, "safe-default");
        string diagnosticDirectory = Path.Combine(root, "diagnostic");
        Directory.CreateDirectory(defaultRoot);
        Directory.CreateDirectory(diagnosticDirectory);
        try
        {
            ProductState state = PersistedState(root, outputRoot: null);
            AdapterRootSnapshot resolved = ResolveWithRealNative(
                state,
                defaultRoot,
                diagnosticDirectory);
            Require(
                state.Current.OutputRoot is null &&
                PathEquals(resolved.CanonicalOutputRoot, defaultRoot) &&
                PathEquals(resolved.PresentedOutputRoot, defaultRoot),
                "missing setting uses the existing formal default root");
            VerifyExplicitRootFlow(
                resolved.CanonicalOutputRoot,
                verifyNarrowRecovery: false);
        }
        finally
        {
            DeleteTree(root);
        }
    }

    private static void VerifyReparseRootFallsBack()
    {
        string root = CreateCaseRoot("reparse");
        string targetRoot = Path.Combine(root, "junction-target");
        string reparseRoot = Path.Combine(root, "junction-output");
        string defaultRoot = Path.Combine(root, "safe-default");
        string diagnosticDirectory = Path.Combine(root, "diagnostic");
        Directory.CreateDirectory(targetRoot);
        Directory.CreateDirectory(defaultRoot);
        Directory.CreateDirectory(diagnosticDirectory);
        CreateDirectoryJunction(reparseRoot, targetRoot);
        try
        {
            ProductState state = PersistedState(root, reparseRoot);
            Require(
                PathEquals(state.Current.OutputRoot!, reparseRoot),
                "managed persisted-path validation still presents the " +
                "reparse case to the formal native probe");

            AdapterRootSnapshot resolved = ResolveWithRealNative(
                state,
                defaultRoot,
                diagnosticDirectory);
            Require(
                PathEquals(resolved.CanonicalOutputRoot, defaultRoot) &&
                PathEquals(resolved.PresentedOutputRoot, defaultRoot) &&
                Directory.Exists(targetRoot),
                "SessionPathSafety-unsafe reparse root follows the existing " +
                "native rejection and adapter fallback flow");
            VerifyExplicitRootFlow(
                resolved.CanonicalOutputRoot,
                verifyNarrowRecovery: false);
        }
        finally
        {
            DeleteJunction(reparseRoot);
            DeleteTree(root);
        }
    }

    private static void VerifyProbeFailureAndInvalidRootFallback()
    {
        string root = CreateCaseRoot("probe-fallback");
        string rejectedRoot = Path.Combine(root, "probe-rejected-output");
        string defaultRoot = Path.Combine(root, "safe-default");
        string nonexistentRoot = Path.Combine(root, "does-not-exist");
        Directory.CreateDirectory(rejectedRoot);
        Directory.CreateDirectory(defaultRoot);
        try
        {
            ProductState state = PersistedState(root, rejectedRoot);
            ProbeNativeSession native = new(
                NativeMethods.Result.NativeFailure,
                NativeMethods.Result.Ok);
            AdapterRootSnapshot resolved = ResolveWithAdapter(
                native,
                state,
                defaultRoot,
                owner: null);
            Require(
                native.RecordingOutputRoots.Count == 2 &&
                PathEquals(native.RecordingOutputRoots[0]!, rejectedRoot) &&
                PathEquals(native.RecordingOutputRoots[1]!, defaultRoot) &&
                PathEquals(resolved.CanonicalOutputRoot, defaultRoot) &&
                PathEquals(resolved.PresentedOutputRoot, defaultRoot),
                "existing native probe failure keeps the mature default " +
                "fallback and UI truth");
            Require(
                !ProductPathContract.TryValidateOutputRoot(
                    nonexistentRoot,
                    out _),
                "nonexistent output root remains rejected by the existing " +
                "managed path contract");
            VerifyExplicitRootFlow(
                resolved.CanonicalOutputRoot,
                verifyNarrowRecovery: false);
        }
        finally
        {
            DeleteTree(root);
        }
    }

    private static void VerifyExplicitNarrowRecovery()
    {
        string root = CreateCaseRoot("explicit-narrow");
        string customRoot = Path.Combine(root, "custom-output");
        string defaultRoot = Path.Combine(root, "safe-default");
        string diagnosticDirectory = Path.Combine(root, "diagnostic");
        Directory.CreateDirectory(customRoot);
        Directory.CreateDirectory(defaultRoot);
        Directory.CreateDirectory(diagnosticDirectory);
        try
        {
            ProductState state = PersistedState(root, customRoot);
            AdapterRootSnapshot resolved = ResolveWithRealNative(
                state,
                defaultRoot,
                diagnosticDirectory);
            Require(
                PathEquals(resolved.CanonicalOutputRoot, customRoot),
                "explicit narrow fixture uses the writer-accepted custom root");
            VerifyExplicitRootFlow(
                resolved.CanonicalOutputRoot,
                verifyNarrowRecovery: true);
        }
        finally
        {
            DeleteTree(root);
        }
    }

    private static void VerifyExplicitRootFlow(
        string effectiveRoot,
        bool verifyNarrowRecovery)
    {
        string sessionId = CreateMalformedSession(effectiveRoot);
        string manifestPath = Path.Combine(
            effectiveRoot, "sessions", sessionId, "manifest.json");
        byte[] originalManifest = File.ReadAllBytes(manifestPath);

        string scanRoot = QueryScanRoot(
            effectiveRoot,
            useExplicitOutputRoot: true);
        Require(
            PathEquals(scanRoot, effectiveRoot),
            "explicit historical ABI scans the exact effective root");

        NativeHistoricalSessionInspector inspector =
            NativeHistoricalSessionInspector.ForOutputRoot(effectiveRoot);
        StartupInspectionResult scan = inspector.Inspect(
            CancellationToken.None);
        Require(
            scan.Sessions.Any(value => value.SessionId == sessionId),
            $"effective-root scan did not return fixture Session {sessionId}; " +
            $"root={effectiveRoot}, status={scan.Status}, " +
            $"hresult=0x{scan.DiagnosticHResult:X8}, " +
            $"sessions={scan.SessionCount}, " +
            $"observed={scan.EntriesObserved}, " +
            $"unrecognized={scan.UnrecognizedEntryCount}, " +
            $"mediaExists={Directory.Exists(effectiveRoot)}, " +
            $"sessionsExists={Directory.Exists(Path.Combine(effectiveRoot, "sessions"))}, " +
            $"sessionExists={Directory.Exists(Path.GetDirectoryName(manifestPath))}, " +
            $"manifestExists={File.Exists(manifestPath)}, " +
            $"openChain={DescribeReadOnlyOpenChain(Path.Combine(effectiveRoot, "sessions"))}");
        HistoricalSessionInspection candidate = scan.Sessions.Single(
            value => value.SessionId == sessionId);
        Require(
            scan.Status == HistoricalSessionScanStatus.Success &&
            candidate.Classification ==
                HistoricalSessionClassification.ManifestCorrupt,
            "startup scan observes evidence only from the effective root");

        if (!verifyNarrowRecovery)
        {
            return;
        }

        NarrowRecoveryResult recovery =
            NativeNarrowRecoveryService.ForOutputRoot(effectiveRoot).Recover(
                sessionId,
                1,
                CancellationToken.None);
        Require(
            recovery.Status == NarrowRecoveryStatus.SemanticConflict,
            "narrow recovery observes the same malformed manifest root");

        StartupInspectionResult rescan = inspector.Inspect(
            CancellationToken.None);
        Require(
            rescan.Sessions.Single(value => value.SessionId == sessionId).
                Classification ==
                    HistoricalSessionClassification.ManifestCorrupt &&
            File.ReadAllBytes(manifestPath).SequenceEqual(originalManifest),
            "confirmation inspector is pinned to the same root and the " +
            "negative recovery path is non-mutating");
    }

    private static AdapterRootSnapshot ResolveWithRealNative(
        ProductState state,
        string defaultRoot,
        string diagnosticDirectory)
    {
        using Form owner = new();
        using Panel surface = new()
        {
            Parent = owner,
            Size = new System.Drawing.Size(320, 180),
        };
        owner.CreateControl();
        surface.CreateControl();
        using NativePreviewSession native = NativePreviewSession.Create(
            surface.Handle,
            owner.Handle,
            diagnosticDirectory);
        return ResolveWithAdapter(native, state, defaultRoot, owner);
    }

    private static AdapterRootSnapshot ResolveWithAdapter(
        IPreviewNativeSession native,
        ProductState state,
        string defaultRoot,
        Form? owner)
    {
        bool ownsOwner = owner is null;
        owner ??= new Form();
        RecordingController recording = new(native);
        ProductionRecordingAdapter commands = new(recording);
        using RecorderCaptureVisibilityController capture = new();
        RecordingFixedHomeAdapter? adapter = null;
        try
        {
            adapter = new RecordingFixedHomeAdapter(
                owner,
                commands,
                recording,
                native,
                state,
                new FixedResolutionCommands(),
                capture,
                defaultRoot);
            return new AdapterRootSnapshot(
                adapter.CanonicalOutputRoot,
                adapter.CurrentState.CanonicalOutputRoot);
        }
        finally
        {
            adapter?.Dispose();
            recording.DisposeAsync().AsTask().GetAwaiter().GetResult();
            if (ownsOwner)
            {
                owner.Dispose();
            }
        }
    }

    private static ProductState PersistedState(
        string caseRoot,
        string? outputRoot)
    {
        ProductSettingsStore store = new(
            Path.Combine(caseRoot, "settings", "product-settings.json"),
            legacyMicrophonePath: string.Empty);
        ProductState state = new(store);
        if (outputRoot is not null)
        {
            Require(
                state.TrySetOutputRoot(outputRoot),
                "isolated persisted output root is accepted by managed settings");
            state.Persist();
            state = new ProductState(store);
            Require(
                PathEquals(state.Current.OutputRoot!, outputRoot),
                "isolated output root survives a fresh settings reload");
        }
        return state;
    }

    private static string CreateMalformedSession(string mediaRoot)
    {
        string sessionId = Guid.NewGuid().ToString("D").ToUpperInvariant();
        string sessionDirectory = Path.Combine(
            mediaRoot, "sessions", sessionId);
        Directory.CreateDirectory(sessionDirectory);
        File.WriteAllText(
            Path.Combine(sessionDirectory, "manifest.json"),
            "{");
        return sessionId;
    }

    private static string QueryScanRoot(
        string rootPath,
        bool useExplicitOutputRoot)
    {
        nint rootPointer = Marshal.StringToHGlobalUni(rootPath);
        nint scanHandle = nint.Zero;
        try
        {
            NativeMethods.HistoricalSessionScanSummaryV1 summary = new()
            {
                StructSize = (uint)sizeof(
                    NativeMethods.HistoricalSessionScanSummaryV1),
                AbiVersion = NativeMethods.HistoricalSessionScanAbiVersionV1,
            };
            NativeMethods.Result begin;
            if (useExplicitOutputRoot)
            {
                NativeMethods.HistoricalSessionScanOutputRootOptionsV1
                    options = new()
                    {
                        StructSize = (uint)sizeof(
                            NativeMethods.
                                HistoricalSessionScanOutputRootOptionsV1),
                        AbiVersion =
                            NativeMethods.HistoricalSessionScanAbiVersionV1,
                        MediaOutputRoot = rootPointer,
                        MaximumEntries =
                            NativeMethods.
                                HistoricalSessionScanMaximumEntriesV1,
                    };
                begin = NativeMethods.
                    XbPreview_BeginHistoricalSessionScanForOutputRootV1(
                        in options,
                        out scanHandle,
                        ref summary);
            }
            else
            {
                NativeMethods.HistoricalSessionScanOptionsV1 options = new()
                {
                    StructSize = (uint)sizeof(
                        NativeMethods.HistoricalSessionScanOptionsV1),
                    AbiVersion =
                        NativeMethods.HistoricalSessionScanAbiVersionV1,
                    DiagnosticLogDirectory = rootPointer,
                    MaximumEntries =
                        NativeMethods.HistoricalSessionScanMaximumEntriesV1,
                };
                begin = NativeMethods.XbPreview_BeginHistoricalSessionScanV1(
                    in options,
                    out scanHandle,
                    ref summary);
            }
            Ensure(begin, "begin scan for root query");
            Require(scanHandle != nint.Zero, "root query scan handle is valid");

            NativeMethods.Result probe =
                NativeMethods.XbPreview_GetHistoricalSessionScanStringV1(
                    scanHandle,
                    NativeMethods.HistoricalSessionScanStringFieldV1.
                        MediaOutputRoot,
                    null,
                    0,
                    out uint requiredLength);
            Require(
                (probe is NativeMethods.Result.Ok or
                    NativeMethods.Result.InsufficientBuffer) &&
                requiredLength > 1,
                "scan-root string probe reports a bounded required length");
            char[] buffer = new char[requiredLength];
            fixed (char* pointer = buffer)
            {
                Ensure(
                    NativeMethods.XbPreview_GetHistoricalSessionScanStringV1(
                        scanHandle,
                        NativeMethods.HistoricalSessionScanStringFieldV1.
                            MediaOutputRoot,
                        pointer,
                        (uint)buffer.Length,
                        out uint actualRequired),
                    "read scan media output root");
                Require(
                    actualRequired == requiredLength && buffer[^1] == '\0',
                    "scan-root string is stable and null terminated");
            }
            return new string(buffer, 0, checked((int)requiredLength - 1));
        }
        finally
        {
            Marshal.FreeHGlobal(rootPointer);
            if (scanHandle != nint.Zero)
            {
                Ensure(
                    NativeMethods.XbPreview_DestroyHistoricalSessionScanV1(
                        ref scanHandle),
                    "destroy root query scan");
            }
        }
    }

    private static void VerifyManagedAbiLayouts()
    {
        Require(
            sizeof(NativeMethods.HistoricalSessionScanOptionsV1) == 40 &&
            sizeof(NativeMethods.
                HistoricalSessionScanOutputRootOptionsV1) == 40 &&
            sizeof(NativeMethods.HistoricalSessionScanSummaryV1) == 64 &&
            sizeof(NativeMethods.HistoricalSessionItemV1) == 192 &&
            sizeof(NativeMethods.NarrowReconciliationOptionsV1) == 48 &&
            sizeof(NativeMethods.
                NarrowReconciliationOutputRootOptionsV1) == 48 &&
            sizeof(NativeMethods.NarrowReconciliationResultV1) == 64,
            "legacy and additive explicit-root ABI sizes remain frozen");
        Require(
            Marshal.OffsetOf<NativeMethods.HistoricalSessionScanOptionsV1>(
                nameof(NativeMethods.HistoricalSessionScanOptionsV1.
                    DiagnosticLogDirectory)).ToInt32() == 8 &&
            Marshal.OffsetOf<
                NativeMethods.HistoricalSessionScanOutputRootOptionsV1>(
                    nameof(NativeMethods.
                        HistoricalSessionScanOutputRootOptionsV1.
                            MediaOutputRoot)).ToInt32() == 8 &&
            Marshal.OffsetOf<NativeMethods.NarrowReconciliationOptionsV1>(
                nameof(NativeMethods.NarrowReconciliationOptionsV1.
                    DiagnosticLogDirectory)).ToInt32() == 8 &&
            Marshal.OffsetOf<
                NativeMethods.NarrowReconciliationOutputRootOptionsV1>(
                    nameof(NativeMethods.
                        NarrowReconciliationOutputRootOptionsV1.
                            MediaOutputRoot)).ToInt32() == 8,
            "legacy diagnostic and additive output-root pointers retain " +
            "their frozen offsets");
    }

    private static void VerifyLegacyReservedFieldsRemainRejected(
        string diagnosticDirectory,
        string sessionId)
    {
        nint diagnostic = Marshal.StringToHGlobalUni(diagnosticDirectory);
        nint session = Marshal.StringToHGlobalUni(sessionId);
        try
        {
            NativeMethods.HistoricalSessionScanOptionsV1 scanOptions = new()
            {
                StructSize = (uint)sizeof(
                    NativeMethods.HistoricalSessionScanOptionsV1),
                AbiVersion = NativeMethods.HistoricalSessionScanAbiVersionV1,
                DiagnosticLogDirectory = diagnostic,
                MaximumEntries = 1,
                Reserved1 = 1,
            };
            NativeMethods.HistoricalSessionScanSummaryV1 summary = new()
            {
                StructSize = (uint)sizeof(
                    NativeMethods.HistoricalSessionScanSummaryV1),
                AbiVersion = NativeMethods.HistoricalSessionScanAbiVersionV1,
            };
            NativeMethods.Result scanResult =
                NativeMethods.XbPreview_BeginHistoricalSessionScanV1(
                    in scanOptions,
                    out nint scanHandle,
                    ref summary);
            Require(
                scanResult == NativeMethods.Result.InvalidArgument &&
                scanHandle == nint.Zero,
                "legacy historical reserved fields remain fail-closed");

            NativeMethods.NarrowReconciliationOptionsV1 narrowOptions = new()
            {
                StructSize = (uint)sizeof(
                    NativeMethods.NarrowReconciliationOptionsV1),
                AbiVersion = NativeMethods.NarrowReconciliationAbiVersionV1,
                DiagnosticLogDirectory = diagnostic,
                CanonicalSessionId = session,
                ExpectedRevision = 1,
                Reserved0 = 1,
            };
            NativeMethods.NarrowReconciliationResultV1 narrowResult = new()
            {
                StructSize = (uint)sizeof(
                    NativeMethods.NarrowReconciliationResultV1),
                AbiVersion = NativeMethods.NarrowReconciliationAbiVersionV1,
            };
            Require(
                NativeMethods.XbPreview_ReconcileNarrowSessionV1(
                    in narrowOptions,
                    ref narrowResult) ==
                        NativeMethods.Result.InvalidArgument,
                "legacy narrow reserved fields remain fail-closed");
        }
        finally
        {
            Marshal.FreeHGlobal(session);
            Marshal.FreeHGlobal(diagnostic);
        }
    }

    private static void VerifyFormalHostUsesOneEffectiveRoot()
    {
        string repository = FindRepositoryRoot();
        string host = File.ReadAllText(Path.Combine(
            repository,
            "XbPreview.Host",
            "StructuralAvaloniaShellHost.cs")).Replace(
                "\r\n",
                "\n",
                StringComparison.Ordinal);
        int adapter = host.IndexOf(
            "_recordingFixedHomeAdapter = new RecordingFixedHomeAdapter(",
            StringComparison.Ordinal);
        int schedule = host.IndexOf(
            "TryScheduleStartupInspection(\n" +
                "                _recordingFixedHomeAdapter." +
                "CanonicalOutputRoot);",
            StringComparison.Ordinal);
        Require(
            adapter >= 0 && schedule > adapter &&
            Count(host, "_startupInspectorFactory(effectiveOutputRoot)") == 2 &&
            Count(host, "_recoveryServiceFactory(effectiveOutputRoot)") == 1 &&
            host.Contains(
                "NativeHistoricalSessionInspector.ForOutputRoot(",
                StringComparison.Ordinal) &&
            host.Contains(
                "NativeNarrowRecoveryService.ForOutputRoot(",
                StringComparison.Ordinal) &&
            !host.Contains(
                "TryScheduleStartupInspection(logDirectory);",
                StringComparison.Ordinal),
            "formal host resolves CanonicalOutputRoot before pinning scan, " +
            "recovery, and confirmation rescan to one value");
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? current = new(Environment.CurrentDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(
                    current.FullName,
                    "XbPreview.P1D-A1.sln")))
            {
                return current.FullName;
            }
            current = current.Parent;
        }
        throw new InvalidOperationException("Repository root was not found.");
    }

    private static int Count(string text, string value)
    {
        int count = 0;
        int offset = 0;
        while ((offset = text.IndexOf(
            value,
            offset,
            StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += value.Length;
        }
        return count;
    }

    private static void CreateDirectoryJunction(
        string junctionPath,
        string targetPath)
    {
        string command = Environment.GetEnvironmentVariable("ComSpec") ??
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.System),
                "cmd.exe");
        ProcessStartInfo start = new(command)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.ArgumentList.Add("/d");
        start.ArgumentList.Add("/c");
        start.ArgumentList.Add("mklink");
        start.ArgumentList.Add("/J");
        start.ArgumentList.Add(junctionPath);
        start.ArgumentList.Add(targetPath);
        using Process process = Process.Start(start) ?? throw new
            InvalidOperationException("Failed to start junction fixture tool.");
        if (!process.WaitForExit(10_000))
        {
            throw new InvalidOperationException(
                "Junction fixture tool did not exit in time.");
        }
        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
        Require(
            process.ExitCode == 0 && Directory.Exists(junctionPath),
            $"junction fixture creation failed: {output} {error}");
    }

    private static void DeleteJunction(string junctionPath)
    {
        if (Directory.Exists(junctionPath))
        {
            Directory.Delete(junctionPath, recursive: false);
        }
    }

    private static string CreateCaseRoot(string name)
    {
        string root = Path.GetFullPath(Path.Combine(
            Path.GetTempPath(),
            "xbpreview-recovery-root-alignment",
            $"{name}-{Environment.ProcessId}-{Guid.NewGuid():N}"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void DeleteTree(string root)
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static bool PathEquals(string left, string right) =>
        string.Equals(
            Path.GetFullPath(left).TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar),
            Path.GetFullPath(right).TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);

    private static string DescribeReadOnlyOpenChain(string candidatePath)
    {
        List<string> paths = [];
        DirectoryInfo? current = new(Path.GetFullPath(candidatePath));
        while (current is not null)
        {
            paths.Add(current.FullName);
            current = current.Parent;
        }
        paths.Reverse();

        List<string> observations = [];
        foreach (string path in paths)
        {
            nint handle = CreateFileW(
                path,
                0x00000080,
                0x00000007,
                nint.Zero,
                3,
                0x02200000,
                nint.Zero);
            if (handle == new nint(-1))
            {
                observations.Add(
                    $"{path}=OPEN_ERROR_{Marshal.GetLastWin32Error()}");
                break;
            }
            observations.Add($"{path}=OPEN_OK");
            _ = CloseHandle(handle);
        }
        return string.Join("|", observations);
    }

    [DllImport(
        "kernel32.dll",
        CharSet = CharSet.Unicode,
        ExactSpelling = true,
        SetLastError = true)]
    private static extern nint CreateFileW(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        nint securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        nint templateFile);

    [DllImport("kernel32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(nint handle);

    private static void Ensure(
        NativeMethods.Result result,
        string operation)
    {
        if (result != NativeMethods.Result.Ok)
        {
            throw new InvalidOperationException(
                $"Native {operation} failed: {result} ({(int)result}).");
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed class FixedResolutionCommands : IRecordingResolutionCommands
    {
        public RecordingResolutionMode CurrentMode =>
            RecordingResolutionMode.Original;

        public bool CurrentSelectionUpscales => false;

        public Task<RecordingResolutionChangeResult> SetResolutionAsync(
            RecordingResolutionMode mode) => Task.FromResult(
                RecordingResolutionChangeResult.Success());
    }

    private sealed class ProbeNativeSession : IPreviewNativeSession
    {
        private readonly Queue<NativeMethods.Result> _rootResults;

        internal ProbeNativeSession(params NativeMethods.Result[] rootResults)
        {
            _rootResults = new Queue<NativeMethods.Result>(rootResults);
        }

        internal List<string?> RecordingOutputRoots { get; } = [];

        public NativeMethods.Result SetRecordingOutputRoot(
            string? validatedLocalPath)
        {
            RecordingOutputRoots.Add(validatedLocalPath);
            return _rootResults.Count == 0
                ? NativeMethods.Result.Ok
                : _rootResults.Dequeue();
        }

        public NativeMethods.Result Start() => NativeMethods.Result.Ok;
        public NativeMethods.Result Stop() => NativeMethods.Result.Ok;
        public NativeMethods.Result Resize(int width, int height) =>
            NativeMethods.Result.Ok;
        public NativeMethods.Result SetSessionGeometry(
            in SessionGeometryNativeV1 geometry) => NativeMethods.Result.Ok;
        public NativeMethods.Result SetCameraState(CameraState state) =>
            NativeMethods.Result.Ok;
        public NativeMethods.Result SetCursorMode(
            NativeMethods.CursorMode cursorMode) => NativeMethods.Result.Ok;
        public NativeMethods.CursorStats GetCursorStats() => default;
        public NativeMethods.PreviewStats GetStats() => default;
        public NativeMethods.Result StartRecording() =>
            NativeMethods.Result.Ok;
        public NativeMethods.Result SetAudioProgramMode(
            NativeMethods.AudioProgramMode mode) => NativeMethods.Result.Ok;
        public NativeMethods.Result StopRecording() => NativeMethods.Result.Ok;
        public NativeMethods.RecordingSnapshot GetRecordingSnapshot() =>
            default;
        public NativeMethods.Result SetAudioControls(
            bool systemMuted,
            bool microphoneMuted,
            double microphoneGainDb) => NativeMethods.Result.Ok;
        public NativeMethods.AudioControlSnapshotV1
            GetAudioControlSnapshot() => default;
        public string GetLastError() => "isolated probe rejection";
        public void Dispose()
        {
        }
    }
}
