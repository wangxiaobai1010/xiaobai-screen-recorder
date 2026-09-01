using System.Runtime.InteropServices;

namespace XbPreview.Host;

internal sealed unsafe class NativeNarrowRecoveryService :
    IUserRecoveryService
{
    private readonly string _rootPath;
    private readonly bool _useExplicitOutputRoot;

    internal NativeNarrowRecoveryService(string diagnosticLogDirectory)
        : this(diagnosticLogDirectory, useExplicitOutputRoot: false)
    {
    }

    private NativeNarrowRecoveryService(
        string rootPath,
        bool useExplicitOutputRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        _rootPath = rootPath;
        _useExplicitOutputRoot = useExplicitOutputRoot;
    }

    internal static NativeNarrowRecoveryService ForOutputRoot(
        string effectiveOutputRoot) => new(
            effectiveOutputRoot,
            useExplicitOutputRoot: true);

    public NarrowRecoveryResult Recover(
        string canonicalSessionId,
        ulong expectedRevision,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalSessionId);
        if (!Guid.TryParseExact(canonicalSessionId, "D", out Guid parsed) ||
            !string.Equals(
                canonicalSessionId,
                parsed.ToString("D").ToUpperInvariant(),
                StringComparison.Ordinal) ||
            expectedRevision == 0)
        {
            throw new ArgumentException(
                "Recovery request requires a canonical SessionId and revision.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        ValidateAbi();
        nint rootPath = Marshal.StringToHGlobalUni(_rootPath);
        nint sessionId = Marshal.StringToHGlobalUni(canonicalSessionId);
        try
        {
            NativeMethods.NarrowReconciliationResultV1 result = new()
            {
                StructSize = (uint)sizeof(
                    NativeMethods.NarrowReconciliationResultV1),
                AbiVersion = NativeMethods.NarrowReconciliationAbiVersionV1,
            };
            Ensure(
                Reconcile(rootPath, sessionId, expectedRevision, ref result),
                "execute explicit narrow recovery request");
            ValidateResult(result, expectedRevision);
            cancellationToken.ThrowIfCancellationRequested();
            return new NarrowRecoveryResult(
                (NarrowRecoveryStatus)result.Status,
                result.DiagnosticHResult,
                result.ExpectedRevision,
                result.ObservedRevisionAvailable != 0
                    ? result.ObservedRevision
                    : null,
                result.GuardStatusAvailable != 0
                    ? result.GuardStatus
                    : null,
                result.CasStatusAvailable != 0
                    ? result.CasStatus
                    : null);
        }
        finally
        {
            Marshal.FreeHGlobal(sessionId);
            Marshal.FreeHGlobal(rootPath);
        }
    }

    private NativeMethods.Result Reconcile(
        nint rootPath,
        nint sessionId,
        ulong expectedRevision,
        ref NativeMethods.NarrowReconciliationResultV1 result)
    {
        if (_useExplicitOutputRoot)
        {
            NativeMethods.NarrowReconciliationOutputRootOptionsV1 options = new()
            {
                StructSize = (uint)sizeof(
                    NativeMethods.NarrowReconciliationOutputRootOptionsV1),
                AbiVersion = NativeMethods.NarrowReconciliationAbiVersionV1,
                MediaOutputRoot = rootPath,
                CanonicalSessionId = sessionId,
                ExpectedRevision = expectedRevision,
            };
            return NativeMethods.XbPreview_ReconcileNarrowSessionForOutputRootV1(
                in options,
                ref result);
        }

        NativeMethods.NarrowReconciliationOptionsV1 legacyOptions = new()
        {
            StructSize = (uint)sizeof(
                NativeMethods.NarrowReconciliationOptionsV1),
            AbiVersion = NativeMethods.NarrowReconciliationAbiVersionV1,
            DiagnosticLogDirectory = rootPath,
            CanonicalSessionId = sessionId,
            ExpectedRevision = expectedRevision,
        };
        return NativeMethods.XbPreview_ReconcileNarrowSessionV1(
            in legacyOptions,
            ref result);
    }

    private static void ValidateAbi()
    {
        NativeMethods.ValidateManagedLayout();
        uint apiVersion = NativeMethods.XbPreview_GetApiVersion();
        if (apiVersion != NativeMethods.ApiVersion)
        {
            throw new InvalidOperationException(
                $"Narrow recovery requires Native API " +
                $"0x{NativeMethods.ApiVersion:X8}; actual=0x{apiVersion:X8}.");
        }

        NativeMethods.NarrowReconciliationAbiLayoutV1 layout = new()
        {
            StructSize = (uint)sizeof(
                NativeMethods.NarrowReconciliationAbiLayoutV1),
            AbiVersion = NativeMethods.NarrowReconciliationAbiVersionV1,
        };
        Ensure(
            NativeMethods.XbPreview_GetNarrowReconciliationAbiLayoutV1(
                ref layout),
            "read narrow recovery ABI layout");
        if (layout.StructSize !=
                NativeMethods.ExpectedNarrowReconciliationAbiLayoutV1Size ||
            layout.AbiVersion !=
                NativeMethods.NarrowReconciliationAbiVersionV1 ||
            layout.PointerSize != 8 || layout.Packing != 8 ||
            layout.WcharSize != 2 ||
            layout.OptionsSize !=
                NativeMethods.ExpectedNarrowReconciliationOptionsV1Size ||
            layout.ResultSize !=
                NativeMethods.ExpectedNarrowReconciliationResultV1Size ||
            layout.Reserved0 != 0)
        {
            throw new InvalidOperationException(
                "Native/Managed narrow recovery ABI mismatch.");
        }
    }

    private static void ValidateResult(
        NativeMethods.NarrowReconciliationResultV1 result,
        ulong expectedRevision)
    {
        if (result.StructSize !=
                NativeMethods.ExpectedNarrowReconciliationResultV1Size ||
            result.AbiVersion !=
                NativeMethods.NarrowReconciliationAbiVersionV1 ||
            !Enum.IsDefined(result.Status) ||
            result.ExpectedRevision != expectedRevision ||
            !IsBoolean32(result.ObservedRevisionAvailable) ||
            !IsBoolean32(result.GuardStatusAvailable) ||
            !IsBoolean32(result.CasStatusAvailable) ||
            (result.GuardStatusAvailable != 0 &&
                !Enum.IsDefined(result.GuardStatus)) ||
            (result.CasStatusAvailable != 0 &&
                !Enum.IsDefined(result.CasStatus)) ||
            result.Reserved0 != 0 || result.Reserved1 != 0)
        {
            throw new InvalidOperationException(
                "Native narrow recovery result failed closed validation.");
        }
    }

    private static bool IsBoolean32(uint value) => value is 0 or 1;

    private static void Ensure(NativeMethods.Result result, string operation)
    {
        if (result != NativeMethods.Result.Ok)
        {
            throw new InvalidOperationException(
                $"Native {operation} failed: {result} ({(int)result}).");
        }
    }
}
