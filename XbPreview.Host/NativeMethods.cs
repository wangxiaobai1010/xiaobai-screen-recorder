using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;
using System.Text;

namespace XbPreview.Host;

internal static unsafe class NativeMethods
{
    internal const string DllName = "XbPreview.Native.dll";
    internal const uint ApiVersion = 0x0004_0005;
    internal const uint HistoricalSessionScanAbiVersionV1 = 0x0001_0001;
    internal const uint NarrowReconciliationAbiVersionV1 = 0x0001_0001;
    internal const uint AudioControlsAbiVersionV1 = 0x0001_0001;
    internal const uint MicrophoneDeviceAbiVersionV1 = 0x0001_0001;
    internal const uint GpuExportAbiVersionV1 = 0x0001_0001;
    internal const uint HistoricalSessionScanMaximumEntriesV1 = 1024;
    internal const int ExpectedHistoricalSessionScanAbiLayoutV1Size = 32;
    internal const int ExpectedHistoricalSessionScanOptionsV1Size = 40;
    internal const int ExpectedHistoricalSessionScanOutputRootOptionsV1Size = 40;
    internal const int ExpectedHistoricalSessionScanSummaryV1Size = 64;
    internal const int ExpectedHistoricalSessionItemV1Size = 192;
    internal const int ExpectedNarrowReconciliationAbiLayoutV1Size = 32;
    internal const int ExpectedNarrowReconciliationOptionsV1Size = 48;
    internal const int ExpectedNarrowReconciliationOutputRootOptionsV1Size = 48;
    internal const int ExpectedNarrowReconciliationResultV1Size = 64;
    internal const int ExpectedCreateOptionsSize = 72;
    internal const int ExpectedStatsSize = 1080;
    internal const int ExpectedAbiLayoutSize = 44;
    internal const int ExpectedLetterboxRectSize = 16;
    internal const int ExpectedCameraStateSize = 120;
    internal const int ExpectedCursorStatsSize = 944;
    internal const int ExpectedSessionGeometryV1Size = 56;
    internal const int ExpectedRecordingSnapshotSize = 2856;
    internal const int ExpectedAudioControlsV1Size = 40;
    internal const int ExpectedAudioControlSnapshotV1Size = 144;
    internal const int ExpectedMicrophoneDeviceListV1Size = 1576;
    internal const int ExpectedMicrophoneDeviceV1Size = 1560;
    internal const int ExpectedMicrophoneSelectionV1Size = 1552;
    internal const int ExpectedMicrophoneSelectionSnapshotV1Size = 1560;
    internal const int ExpectedGpuExportFrameV1Size = 72;

    internal enum Result : int
    {
        Ok = 0,
        InvalidArgument = -1,
        InvalidWindow = -2,
        AbiMismatch = -3,
        InvalidHandle = -4,
        InvalidState = -5,
        Timeout = -6,
        WgcUnsupported = -7,
        HdrUnsupported = -8,
        NativeFailure = -9,
        DeviceLost = -10,
        InvalidCameraState = -11,
        StaleCameraState = -12,
        CursorModeUnavailable = -13,
        UnsupportedStructVersion = -14,
        InvalidGeometry = -15,
        StaleRevision = -16,
        RevisionConflict = -17,
        GeometrySourceMismatch = -18,
        InsufficientBuffer = -19,
        WindowTargetClosed = -20,
    }

    internal enum CaptureTargetKind : int
    {
        Monitor = 0,
        Window = 1,
    }

    internal enum WindowStageOrientation : int
    {
        Left = 0,
        Front = 1,
        Right = 2,
    }

    internal enum WindowStageLevel : int
    {
        Level1 = 0,
        Level2 = 1,
        Level3 = 2,
    }

    internal enum WindowShowcaseBackgroundPreset : int
    {
        Warm = 0,
        Art01 = 1,
        Art001 = 2,
    }

    internal enum AudioProgramMode : int
    {
        None = 0,
        SystemOnly = 1,
        MicrophoneOnly = 2,
        Dual = 3,
    }

    [Flags]
    internal enum AudioEndpointLevelFlagsV1 : ulong
    {
        None = 0,
        SystemSourceEnabled = 1ul << 0,
        MicrophoneSourceEnabled = 1ul << 1,
        SystemMeterAvailable = 1ul << 2,
        MicrophoneMeterAvailable = 1ul << 3,
    }

    internal enum MicrophoneSelectionKindV1 : int
    {
        WindowsDefault = 0,
        ConcreteEndpoint = 1,
    }

    internal enum PreviewState : int
    {
        Stopped = 0,
        Starting = 1,
        Running = 2,
        Stopping = 3,
        Error = 4,
    }

    internal enum RecordingState : int
    {
        Idle = 0,
        Starting = 1,
        Recording = 2,
        Stopping = 3,
        Completed = 4,
        Failed = 5,
        Pausing = 6,
        Paused = 7,
        Resuming = 8,
        UserCancelled = 9,
    }

    internal enum HistoricalSessionScanStringFieldV1 : int
    {
        MediaOutputRoot = 0,
        SessionsRoot = 1,
    }

    internal enum HistoricalSessionScanStatusV1 : int
    {
        Success = 0,
        SessionsRootAbsent = 1,
        SessionsRootInaccessible = 2,
        SessionsRootUnsafe = 3,
        IoFailure = 4,
        PartialTruncated = 5,
    }

    internal enum HistoricalSessionClassificationV1 : int
    {
        CompletedConsistent = 0,
        ReconciledCompletedConsistent = 1,
        PublishedMetadataNeedsReconciliation = 2,
        PublishOutcomeUnprovenRetain = 3,
        ReadyToPublishWorkingPreserved = 4,
        IncompleteWithWorkingMedia = 5,
        IncompleteNoMediaRetain = 6,
        PublishFailedWorkingPreserved = 7,
        FinalizeOrValidationFailedWorkingPreserved = 8,
        ManifestCorrupt = 9,
        ManifestMissing = 10,
        FilesystemConflict = 11,
        UnknownRetain = 12,
        UserCancelled = 13,
    }

    internal enum HistoricalSessionSeverityV1 : int
    {
        Info = 0,
        Attention = 1,
        RecoveryCandidate = 2,
        CriticalRetain = 3,
    }

    internal enum HistoricalSessionParseStatusV1 : int
    {
        Valid = 0,
        NotFound = 1,
        Inaccessible = 2,
        MalformedJson = 3,
        UnsupportedSchema = 4,
        SemanticInvalid = 5,
        UnknownOrFutureState = 6,
        IoFailure = 7,
    }

    internal enum HistoricalSessionSemanticIssueV1 : int
    {
        None = 0,
        SessionIdentityMismatch = 1,
        PathPolicyViolation = 2,
        PublishedPathMismatch = 3,
        Other = 4,
    }

    internal enum HistoricalSessionManifestStateV1 : int
    {
        Created = 0,
        Starting = 1,
        Recording = 2,
        Stopping = 3,
        ReadyToPublish = 4,
        Published = 5,
        Completed = 6,
        Failed = 7,
        Unknown = 8,
        ReconciledCompleted = 9,
        UserCancelled = 10,
    }

    internal enum HistoricalSessionOwnerStateV1 : int
    {
        ActiveOwned = 0,
        InactiveLeaseReleased = 1,
        EvidenceMissing = 2,
        UnsafePath = 3,
        Inaccessible = 4,
        IoFailure = 5,
        Unknown = 6,
    }

    internal enum HistoricalSessionFilesystemStateV1 : int
    {
        NotProvided = 0,
        Exists = 1,
        Absent = 2,
        ParentAbsent = 3,
        Inaccessible = 4,
        OutsideTrustedRoot = 5,
        ReparseEncountered = 6,
        Invalid = 7,
        TypeMismatch = 8,
        IoFailure = 9,
        Unknown = 10,
    }

    internal enum HistoricalSessionStringFieldV1 : int
    {
        SessionId = 0,
        WorkingCandidatePath = 1,
        PlannedFinalCandidatePath = 2,
        PublishedCandidatePath = 3,
        SessionDirectory = 4,
        ManifestPath = 5,
    }

    internal enum NarrowReconciliationStatusV1 : int
    {
        Reconciled = 0,
        AlreadyReconciled = 1,
        NotEligibleState = 2,
        InvalidSourceFacts = 3,
        SemanticConflict = 4,
        GuardRejected = 5,
        RevisionChanged = 6,
        ConcurrentChange = 7,
        ImmutableFieldViolation = 8,
        UnsupportedSchema = 9,
        EvidenceInsufficient = 10,
        CasFailed = 11,
        IoFailure = 12,
        Unknown = 13,
    }

    internal enum NarrowReconciliationGuardStatusV1 : int
    {
        EvidenceComplete = 0,
        ActiveOwner = 1,
        OwnerEvidenceMissing = 2,
        RevisionMismatch = 3,
        ManifestNotEligible = 4,
        ManifestUnsupported = 5,
        PathUnsafe = 6,
        PathInaccessible = 7,
        WorkingStillPresent = 8,
        WorkingAbsenceUnproven = 9,
        FinalMissing = 10,
        FinalUnsafe = 11,
        IdentityMissing = 12,
        IdentityMismatch = 13,
        HardLinkAmbiguous = 14,
        ConcurrentChange = 15,
        IoFailure = 16,
        Unknown = 17,
    }

    internal enum NarrowReconciliationCasStatusV1 : int
    {
        Ready = 0,
        Succeeded = 1,
        RevisionMismatch = 2,
        NotFound = 3,
        Inaccessible = 4,
        UnsupportedSchema = 5,
        MalformedManifest = 6,
        SemanticInvalid = 7,
        ConcurrentChange = 8,
        AtomicWriteFailure = 9,
        IoFailure = 10,
        InvalidInput = 11,
        Inactive = 12,
    }

    internal enum CursorMode : int
    {
        SystemCursor = 0,
        CustomCursor = 1,
    }

    internal enum CursorFallbackReason : int
    {
        None = 0,
        ApiUnavailable = 1,
        CustomRendererInitializationFailed = 2,
        WgcSettingFailed = 3,
        WgcReadbackMismatch = 4,
    }

    internal enum CursorShapeKind : uint
    {
        None = 0,
        ColorAlpha = 1,
        ColorMask = 2,
        MonochromeAndXor = 3,
        BuiltInFallbackArrow = 4,
    }

    [Flags]
    internal enum StatsFlags : uint
    {
        None = 0,
        WdaApplied = 1U << 0,
        WdaFailed = 1U << 1,
        UsingWarp = 1U << 2,
        HdrDetected = 1U << 3,
        Occluded = 1U << 4,
        Minimized = 1U << 5,
        WindowTargetMinimized = 1U << 6,
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    internal struct CreateOptions
    {
        internal uint StructSize;
        internal uint ApiVersion;
        internal ulong ExclusionWindow;
        internal uint AllowWarp;
        internal uint FramePoolBufferCount;
        internal uint StatsIntervalMilliseconds;
        internal uint Reserved0;
        internal nint DiagnosticLogDirectory;
        internal ulong Reserved1;
        internal ulong Reserved2;
        internal ulong Reserved3;
        internal ulong Reserved4;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    internal unsafe struct PreviewStats
    {
        internal uint StructSize;
        internal uint ApiVersion;
        internal PreviewState State;
        internal StatsFlags Flags;
        internal ulong SessionIdHigh;
        internal ulong SessionIdLow;
        internal ulong CaptureFrameCount;
        internal ulong PresentFrameCount;
        internal ulong DroppedFrameCount;
        internal ulong FramePoolRecreateCount;
        internal ulong SwapChainResizeCount;
        internal double CaptureFps;
        internal double PresentFps;
        internal double RecentLatencyMilliseconds;
        internal double P50LatencyMilliseconds;
        internal double P95LatencyMilliseconds;
        internal double MaxLatencyMilliseconds;
        internal uint CaptureWidth;
        internal uint CaptureHeight;
        internal uint PreviewWidth;
        internal uint PreviewHeight;
        internal Result LastResult;
        internal int DeviceRemovedReason;
        internal int WdaResult;
        internal uint WdaLastError;
        internal uint UsedWarp;
        internal uint HdrDetected;
        internal long LastSystemRelativeTime100ns;
        internal long LastFrameArrivalQpc;
        internal long LastPresentBeforeQpc;
        internal long LastPresentAfterQpc;
        internal ulong WorkingSetBytes;
        internal ulong PrivateBytes;
        internal ulong CameraUpdateCount;
        internal ulong InvalidCameraStateFallbackCount;
        internal ulong NativeLastAppliedSequence;
        internal double CameraUpdateRate;
        internal double NativeAppliedZoom;
        internal double NativeAppliedCenterX;
        internal double NativeAppliedCenterY;
        internal CameraMode NativeAppliedMode;
        internal uint NativeCameraEnabled;
        internal fixed char AdapterName[128];
        internal fixed char LogFilePath[260];
        internal ulong Reserved1;
        internal ulong Reserved2;
        internal ulong Reserved3;
        internal ulong Reserved4;

        internal string GetAdapterName()
        {
            fixed (char* value = AdapterName)
            {
                return new string(value);
            }
        }

        internal string GetLogFilePath()
        {
            fixed (char* value = LogFilePath)
            {
                return new string(value);
            }
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    internal struct GpuExportFrameV1
    {
        internal uint StructSize;
        internal uint Version;
        internal ulong SharedHandle;
        internal uint Width;
        internal uint Height;
        internal uint Format;
        internal uint SlotIndex;
        internal ulong ResourceGeneration;
        internal ulong FrameGeneration;
        internal ulong SkippedFrameCount;
        internal uint AdapterLuidLow;
        internal int AdapterLuidHigh;
        internal ulong RendererGeneration;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    internal struct AbiLayout
    {
        internal uint StructSize;
        internal uint ApiVersion;
        internal uint PointerSize;
        internal uint Packing;
        internal uint CreateOptionsSize;
        internal uint StatsSize;
        internal uint LetterboxRectSize;
        internal uint WcharSize;
        internal uint CameraStateSize;
        internal uint CursorStatsSize;
        internal uint RecordingSnapshotSize;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    internal struct HistoricalSessionScanAbiLayoutV1
    {
        internal uint StructSize;
        internal uint AbiVersion;
        internal uint PointerSize;
        internal uint Packing;
        internal uint WcharSize;
        internal uint OptionsSize;
        internal uint SummarySize;
        internal uint ItemSize;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    internal struct HistoricalSessionScanOptionsV1
    {
        internal uint StructSize;
        internal uint AbiVersion;
        internal nint DiagnosticLogDirectory;
        internal uint MaximumEntries;
        internal uint Reserved0;
        internal ulong Reserved1;
        internal ulong Reserved2;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    internal struct HistoricalSessionScanOutputRootOptionsV1
    {
        internal uint StructSize;
        internal uint AbiVersion;
        internal nint MediaOutputRoot;
        internal uint MaximumEntries;
        internal uint Reserved0;
        internal ulong Reserved1;
        internal ulong Reserved2;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    internal struct HistoricalSessionScanSummaryV1
    {
        internal uint StructSize;
        internal uint AbiVersion;
        internal HistoricalSessionScanStatusV1 Status;
        internal int DiagnosticHResult;
        internal uint SessionCount;
        internal uint UnrecognizedEntryCount;
        internal ulong EntriesObserved;
        internal ulong MaximumEntries;
        internal uint Truncated;
        internal uint MediaWithoutSessionDirectoryBlindSpot;
        internal ulong Reserved1;
        internal ulong Reserved2;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    internal struct HistoricalSessionItemV1
    {
        internal uint StructSize;
        internal uint AbiVersion;
        internal HistoricalSessionClassificationV1 Classification;
        internal HistoricalSessionSeverityV1 Severity;
        internal ulong Reasons;
        internal HistoricalSessionParseStatusV1 ManifestParseStatus;
        internal int ManifestParseHResult;
        internal HistoricalSessionSemanticIssueV1 ManifestSemanticIssue;
        internal HistoricalSessionManifestStateV1 ManifestState;
        internal uint ObservedSchemaVersion;
        internal uint ObservedSchemaVersionAvailable;
        internal ulong ObservedRevision;
        internal uint ObservedRevisionAvailable;
        internal uint ManifestAvailable;
        internal uint ManifestRevisionStable;
        internal HistoricalSessionOwnerStateV1 OwnerState;
        internal int OwnerHResult;
        internal uint Reserved0;
        internal HistoricalSessionFilesystemStateV1 WorkingFilesystemState;
        internal int WorkingHResult;
        internal uint WorkingSizeAvailable;
        internal uint Reserved1;
        internal ulong WorkingSize;
        internal HistoricalSessionFilesystemStateV1 PlannedFinalFilesystemState;
        internal int PlannedFinalHResult;
        internal uint PlannedFinalSizeAvailable;
        internal uint Reserved2;
        internal ulong PlannedFinalSize;
        internal HistoricalSessionFilesystemStateV1 PublishedFilesystemState;
        internal int PublishedHResult;
        internal uint PublishedSizeAvailable;
        internal uint Reserved3;
        internal ulong PublishedSize;
        internal uint PersistentWorkingIdentityAvailable;
        internal uint PersistentIdentityComparisonAttempted;
        internal uint StrongIdentityMatch;
        internal uint DeleteAllowed;
        internal uint ReconciliationAuthorized;
        internal uint Reserved4;
        internal ulong Reserved5;
        internal ulong Reserved6;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    internal struct NarrowReconciliationAbiLayoutV1
    {
        internal uint StructSize;
        internal uint AbiVersion;
        internal uint PointerSize;
        internal uint Packing;
        internal uint WcharSize;
        internal uint OptionsSize;
        internal uint ResultSize;
        internal uint Reserved0;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    internal struct NarrowReconciliationOptionsV1
    {
        internal uint StructSize;
        internal uint AbiVersion;
        internal nint DiagnosticLogDirectory;
        internal nint CanonicalSessionId;
        internal ulong ExpectedRevision;
        internal ulong Reserved0;
        internal ulong Reserved1;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    internal struct NarrowReconciliationOutputRootOptionsV1
    {
        internal uint StructSize;
        internal uint AbiVersion;
        internal nint MediaOutputRoot;
        internal nint CanonicalSessionId;
        internal ulong ExpectedRevision;
        internal ulong Reserved0;
        internal ulong Reserved1;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    internal struct NarrowReconciliationResultV1
    {
        internal uint StructSize;
        internal uint AbiVersion;
        internal NarrowReconciliationStatusV1 Status;
        internal int DiagnosticHResult;
        internal ulong ExpectedRevision;
        internal ulong ObservedRevision;
        internal uint ObservedRevisionAvailable;
        internal NarrowReconciliationGuardStatusV1 GuardStatus;
        internal uint GuardStatusAvailable;
        internal NarrowReconciliationCasStatusV1 CasStatus;
        internal uint CasStatusAvailable;
        internal uint Reserved0;
        internal ulong Reserved1;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    internal struct NativeCameraState
    {
        internal uint StructSize;
        internal uint ApiVersion;
        internal ulong Sequence;
        internal long TimestampQpc;
        internal uint Enabled;
        internal CameraMode Mode;
        internal double Zoom;
        internal double CenterX;
        internal double CenterY;
        internal double TransitionProgress;
        internal double TargetX;
        internal double TargetY;
        internal uint ClampX;
        internal uint ClampY;
        internal ulong Reserved1;
        internal ulong Reserved2;
        internal ulong Reserved3;
        internal ulong Reserved4;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    internal struct LetterboxRect
    {
        internal float X;
        internal float Y;
        internal float Width;
        internal float Height;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    internal unsafe struct CursorStats
    {
        internal uint StructSize;
        internal uint ApiVersion;
        internal CursorMode RequestedMode;
        internal CursorMode ActualMode;
        internal CursorFallbackReason FallbackReason;
        internal uint WgcCursorPropertyAvailable;
        internal uint SystemCursorIncluded;
        internal uint CustomCursorLayerActive;
        internal uint LastFrameDrawn;
        internal uint CursorVisible;
        internal uint CursorInsideMonitor;
        internal int WgcCursorSettingResult;
        internal uint WgcCursorSettingLastError;
        internal int GetCursorInfoResult;
        internal uint GetCursorInfoLastError;
        internal int ShapeConversionResult;
        internal uint ShapeConversionLastError;
        internal CursorShapeKind ShapeKind;
        internal ulong CursorSequence;
        internal ulong SampleCount;
        internal ulong DrawCount;
        internal ulong HiddenSkipCount;
        internal ulong OutsideMonitorSkipCount;
        internal ulong OutsideCameraSkipCount;
        internal ulong GetCursorInfoFailureCount;
        internal ulong ShapeCacheHitCount;
        internal ulong ShapeCacheMissCount;
        internal ulong TextureUploadCount;
        internal ulong ShapeConversionFailureCount;
        internal ulong BuiltInFallbackCount;
        internal ulong XorApproximationPixelCount;
        internal ulong DiagnosticQueueDropCount;
        internal long TimestampQpc;
        internal int ScreenX;
        internal int ScreenY;
        internal double SourceX;
        internal double SourceY;
        internal double CameraViewLeft;
        internal double CameraViewTop;
        internal double CameraViewWidth;
        internal double CameraViewHeight;
        internal double OutputHotspotX;
        internal double OutputHotspotY;
        internal double OutputLeft;
        internal double OutputTop;
        internal double OutputWidth;
        internal double OutputHeight;
        internal double Zoom;
        internal double CenterX;
        internal double CenterY;
        internal double ViewportX;
        internal double ViewportY;
        internal double ViewportWidth;
        internal double ViewportHeight;
        internal double LastRenderDurationMilliseconds;
        internal ulong ShapeId;
        internal ulong ShapeGeneration;
        internal uint ShapeWidth;
        internal uint ShapeHeight;
        internal uint HotspotX;
        internal uint HotspotY;
        internal fixed char LogFilePath[260];
        internal ulong Reserved1;
        internal ulong Reserved2;
        internal ulong Reserved3;
        internal ulong Reserved4;

        internal string GetLogFilePath()
        {
            fixed (char* value = LogFilePath)
            {
                return new string(value);
            }
        }
    }

    [StructLayout(
        LayoutKind.Sequential,
        Pack = 8,
        CharSet = CharSet.Unicode)]
    internal unsafe struct RecordingSnapshot
    {
        internal uint StructSize;
        internal uint ApiVersion;
        internal RecordingState State;
        internal Result LastResult;
        internal long StartUtc100ns;
        internal long Elapsed100ns;
        internal uint OutputSuccess;
        internal uint FinalizeAttempted;
        internal int FinalizeHResult;
        internal int FailureHResult;
        internal uint FinalizeCount;
        internal uint ActiveEncoder;
        internal uint ResidualOutstanding;
        internal uint OutputCleanupAttempted;
        internal uint OutputCleanupSucceeded;
        internal int OutputCleanupHResult;
        internal ulong FramesSubmitted;
        internal fixed char SessionId[64];
        // Legacy P2.5 direct-output path. P2.6 publication code must use the
        // explicit path facts below instead of assigning it a new meaning.
        internal fixed char OutputPath[260];
        internal fixed char ErrorMessage[256];
        // Existing 64-bit reserved slots assigned by Pause Phase A.
        internal ulong PauseCount;
        internal ulong TotalPaused100ns;
        internal ulong Reserved3;
        internal ulong Reserved4;
        internal uint ReadyToPublish;
        internal uint Published;
        internal uint PublishAttempted;
        internal int PublishHResult;
        internal uint ValidationAttempted;
        internal int ValidationHResult;
        internal fixed char WorkingPath[260];
        internal fixed char PlannedFinalPath[260];
        internal fixed char PublishedPath[260];

        internal string GetSessionId()
        {
            fixed (char* value = SessionId)
            {
                return new string(value);
            }
        }

        internal string GetOutputPath()
        {
            fixed (char* value = OutputPath)
            {
                return new string(value);
            }
        }

        internal string GetWorkingPath()
        {
            fixed (char* value = WorkingPath)
            {
                return new string(value);
            }
        }

        internal string GetPlannedFinalPath()
        {
            fixed (char* value = PlannedFinalPath)
            {
                return new string(value);
            }
        }

        internal string GetPublishedPath()
        {
            fixed (char* value = PublishedPath)
            {
                return new string(value);
            }
        }

        internal string GetErrorMessage()
        {
            fixed (char* value = ErrorMessage)
            {
                return new string(value);
            }
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    internal struct AudioControlsV1
    {
        internal uint StructSize;
        internal uint AbiVersion;
        internal uint SystemMuted;
        internal uint MicrophoneMuted;
        internal double MicrophoneGainDb;
        internal ulong Reserved1;
        internal ulong Reserved2;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    internal struct AudioControlSnapshotV1
    {
        internal uint StructSize;
        internal uint AbiVersion;
        internal uint SystemMuted;
        internal uint MicrophoneMuted;
        internal double MicrophoneGainDb;
        internal double MicrophoneGainLinear;
        internal double ProgramHeadroomCoefficient;
        internal ulong ControlRevision;
        internal ulong PendingControlRevision;
        internal uint SystemPeakAbsolutePcm16;
        internal uint MicrophonePeakAbsolutePcm16;
        internal uint MicrophonePostGainPeakAbsolutePcm16;
        internal uint ProgramPeakAbsolutePcm16;
        internal double SystemRmsPcm16;
        internal double MicrophoneRmsPcm16;
        internal double MicrophonePostGainRmsPcm16;
        internal ulong MicrophonePostGainOverloadSamples;
        internal ulong OutputClampSamples;
        internal ulong OutputFrames;
        internal ulong OutputBlocks;
        internal uint MeterWindowFrames;
        internal uint MicrophoneGainParameterClamped;
        internal AudioEndpointLevelFlagsV1 EndpointLevelFlags;
    }

    [StructLayout(
        LayoutKind.Sequential,
        Pack = 8,
        CharSet = CharSet.Unicode)]
    internal unsafe struct MicrophoneDeviceListV1
    {
        internal uint StructSize;
        internal uint AbiVersion;
        internal ulong Generation;
        internal uint DeviceCount;
        internal uint MonitorActive;
        internal uint DefaultAvailable;
        internal uint DeviceAddedCount;
        internal uint DeviceRemovedCount;
        internal uint Reserved0;
        internal fixed char DefaultEndpointId[512];
        internal fixed char DefaultDisplayName[256];

        internal string GetDefaultEndpointId()
        {
            fixed (char* value = DefaultEndpointId)
            {
                return new string(value);
            }
        }

        internal string GetDefaultDisplayName()
        {
            fixed (char* value = DefaultDisplayName)
            {
                return new string(value);
            }
        }
    }

    [StructLayout(
        LayoutKind.Sequential,
        Pack = 8,
        CharSet = CharSet.Unicode)]
    internal unsafe struct MicrophoneDeviceV1
    {
        internal uint StructSize;
        internal uint AbiVersion;
        internal ulong Generation;
        internal uint Index;
        internal uint Available;
        internal fixed char EndpointId[512];
        internal fixed char DisplayName[256];

        internal string GetEndpointId()
        {
            fixed (char* value = EndpointId)
            {
                return new string(value);
            }
        }

        internal string GetDisplayName()
        {
            fixed (char* value = DisplayName)
            {
                return new string(value);
            }
        }
    }

    [StructLayout(
        LayoutKind.Sequential,
        Pack = 8,
        CharSet = CharSet.Unicode)]
    internal unsafe struct MicrophoneSelectionV1
    {
        internal uint StructSize;
        internal uint AbiVersion;
        internal MicrophoneSelectionKindV1 Kind;
        internal uint Reserved0;
        internal fixed char EndpointId[512];
        internal fixed char DisplayName[256];

        internal void SetEndpointId(string value)
        {
            fixed (char* target = EndpointId)
            {
                CopyString(value, target, 512);
            }
        }

        internal void SetDisplayName(string value)
        {
            fixed (char* target = DisplayName)
            {
                CopyString(value, target, 256);
            }
        }
    }

    [StructLayout(
        LayoutKind.Sequential,
        Pack = 8,
        CharSet = CharSet.Unicode)]
    internal unsafe struct MicrophoneSelectionSnapshotV1
    {
        internal uint StructSize;
        internal uint AbiVersion;
        internal MicrophoneSelectionKindV1 Kind;
        internal uint Available;
        internal uint SessionLocked;
        internal uint Reserved0;
        internal fixed char EndpointId[512];
        internal fixed char DisplayName[256];

        internal string GetEndpointId()
        {
            fixed (char* value = EndpointId)
            {
                return new string(value);
            }
        }

        internal string GetDisplayName()
        {
            fixed (char* value = DisplayName)
            {
                return new string(value);
            }
        }
    }

    private static void CopyString(string value, char* target, int capacity)
    {
        new Span<char>(target, capacity).Clear();
        ReadOnlySpan<char> source = value.AsSpan();
        source[..Math.Min(source.Length, capacity - 1)].CopyTo(
            new Span<char>(target, capacity - 1));
    }

    [DllImport(DllName, CallingConvention = CallingConvention.StdCall, ExactSpelling = true)]
    internal static extern uint XbPreview_GetApiVersion();

    [DllImport(DllName, CallingConvention = CallingConvention.StdCall, ExactSpelling = true)]
    internal static extern Result XbPreview_GetAbiLayout(ref AbiLayout layout);

    [DllImport(DllName, CallingConvention = CallingConvention.StdCall, ExactSpelling = true)]
    internal static extern Result XbPreview_GetHistoricalSessionScanAbiLayoutV1(
        ref HistoricalSessionScanAbiLayoutV1 layout);

    [DllImport(DllName, CallingConvention = CallingConvention.StdCall, ExactSpelling = true)]
    internal static extern Result XbPreview_BeginHistoricalSessionScanV1(
        in HistoricalSessionScanOptionsV1 options,
        out nint scanHandle,
        ref HistoricalSessionScanSummaryV1 summary);

    [DllImport(DllName, CallingConvention = CallingConvention.StdCall, ExactSpelling = true)]
    internal static extern Result
        XbPreview_BeginHistoricalSessionScanForOutputRootV1(
            in HistoricalSessionScanOutputRootOptionsV1 options,
            out nint scanHandle,
            ref HistoricalSessionScanSummaryV1 summary);

    [DllImport(DllName, CallingConvention = CallingConvention.StdCall, ExactSpelling = true)]
    internal static extern Result XbPreview_GetHistoricalSessionV1(
        nint scanHandle,
        uint index,
        ref HistoricalSessionItemV1 item);

    [DllImport(DllName, CallingConvention = CallingConvention.StdCall, ExactSpelling = true)]
    internal static extern Result XbPreview_GetHistoricalSessionScanStringV1(
        nint scanHandle,
        HistoricalSessionScanStringFieldV1 field,
        char* buffer,
        uint bufferLength,
        out uint requiredLength);

    [DllImport(DllName, CallingConvention = CallingConvention.StdCall, ExactSpelling = true)]
    internal static extern Result XbPreview_GetHistoricalSessionStringV1(
        nint scanHandle,
        uint index,
        HistoricalSessionStringFieldV1 field,
        char* buffer,
        uint bufferLength,
        out uint requiredLength);

    [DllImport(DllName, CallingConvention = CallingConvention.StdCall, ExactSpelling = true)]
    internal static extern Result XbPreview_DestroyHistoricalSessionScanV1(
        ref nint scanHandle);

    [DllImport(DllName, CallingConvention = CallingConvention.StdCall, ExactSpelling = true)]
    internal static extern Result XbPreview_GetNarrowReconciliationAbiLayoutV1(
        ref NarrowReconciliationAbiLayoutV1 layout);

    [DllImport(DllName, CallingConvention = CallingConvention.StdCall, ExactSpelling = true)]
    internal static extern Result XbPreview_ReconcileNarrowSessionV1(
        in NarrowReconciliationOptionsV1 options,
        ref NarrowReconciliationResultV1 result);

    [DllImport(DllName, CallingConvention = CallingConvention.StdCall, ExactSpelling = true)]
    internal static extern Result XbPreview_ReconcileNarrowSessionForOutputRootV1(
        in NarrowReconciliationOutputRootOptionsV1 options,
        ref NarrowReconciliationResultV1 result);

    [DllImport(DllName, CallingConvention = CallingConvention.StdCall, ExactSpelling = true)]
    internal static extern Result XbPreview_Create(
        nint previewHwnd,
        in CreateOptions options,
        out nint handle);

    [DllImport(DllName, CallingConvention = CallingConvention.StdCall, ExactSpelling = true)]
    internal static extern Result XbPreview_Start(XbPreviewSafeHandle handle);

    [DllImport(DllName, CallingConvention = CallingConvention.StdCall, ExactSpelling = true)]
    internal static extern Result XbPreview_Stop(XbPreviewSafeHandle handle);

    [DllImport(DllName, CallingConvention = CallingConvention.StdCall, ExactSpelling = true)]
    internal static extern Result XbPreview_StartRecording(
        XbPreviewSafeHandle handle);

    [DllImport(DllName, CallingConvention = CallingConvention.StdCall, ExactSpelling = true)]
    internal static extern Result XbPreview_PauseRecording(
        XbPreviewSafeHandle handle);

    [DllImport(DllName, CallingConvention = CallingConvention.StdCall, ExactSpelling = true)]
    internal static extern Result XbPreview_ResumeRecording(
        XbPreviewSafeHandle handle);

    [DllImport(DllName, CallingConvention = CallingConvention.StdCall, ExactSpelling = true)]
    internal static extern Result XbPreview_SetAudioProgramMode(
        XbPreviewSafeHandle handle,
        AudioProgramMode mode);

    [DllImport(DllName, CallingConvention = CallingConvention.StdCall, ExactSpelling = true)]
    internal static extern Result XbPreview_StopRecording(
        XbPreviewSafeHandle handle);

    [DllImport(DllName, CallingConvention = CallingConvention.StdCall, ExactSpelling = true)]
    internal static extern Result XbPreview_CancelRecording(
        XbPreviewSafeHandle handle);

    [DllImport(DllName, CallingConvention = CallingConvention.StdCall, ExactSpelling = true)]
    internal static extern Result XbPreview_GetRecordingSnapshot(
        XbPreviewSafeHandle handle,
        ref RecordingSnapshot snapshot);

    [DllImport(DllName, CallingConvention = CallingConvention.StdCall, ExactSpelling = true)]
    internal static extern Result XbPreview_SetAudioControlsV1(
        XbPreviewSafeHandle handle,
        in AudioControlsV1 controls);

    [DllImport(DllName, CallingConvention = CallingConvention.StdCall, ExactSpelling = true)]
    internal static extern Result XbPreview_GetAudioControlSnapshotV1(
        XbPreviewSafeHandle handle,
        ref AudioControlSnapshotV1 snapshot);

    [DllImport(DllName, CallingConvention = CallingConvention.StdCall, ExactSpelling = true)]
    internal static extern Result XbPreview_GetMicrophoneDeviceListV1(
        XbPreviewSafeHandle handle,
        ref MicrophoneDeviceListV1 list);

    [DllImport(DllName, CallingConvention = CallingConvention.StdCall, ExactSpelling = true)]
    internal static extern Result XbPreview_GetMicrophoneDeviceV1(
        XbPreviewSafeHandle handle,
        ref MicrophoneDeviceV1 device);

    [DllImport(DllName, CallingConvention = CallingConvention.StdCall, ExactSpelling = true)]
    internal static extern Result XbPreview_SetMicrophoneSelectionV1(
        XbPreviewSafeHandle handle,
        in MicrophoneSelectionV1 selection);

    [DllImport(DllName, CallingConvention = CallingConvention.StdCall, ExactSpelling = true)]
    internal static extern Result XbPreview_GetMicrophoneSelectionV1(
        XbPreviewSafeHandle handle,
        ref MicrophoneSelectionSnapshotV1 snapshot);

    [DllImport(DllName, CallingConvention = CallingConvention.StdCall, ExactSpelling = true)]
    internal static extern Result XbPreview_Resize(
        XbPreviewSafeHandle handle,
        int width,
        int height);

    [DllImport(DllName, CallingConvention = CallingConvention.StdCall, ExactSpelling = true)]
    internal static extern Result XbPreview_SetGpuExportTargetSize(
        XbPreviewSafeHandle handle,
        int width,
        int height);

    [DllImport(DllName, CallingConvention = CallingConvention.StdCall, ExactSpelling = true)]
    internal static extern Result XbPreview_SetSessionGeometry(
        XbPreviewSafeHandle handle,
        in SessionGeometryNativeV1 geometry);

    [DllImport(DllName, CallingConvention = CallingConvention.StdCall, ExactSpelling = true)]
    internal static extern Result XbPreview_SetCameraState(
        XbPreviewSafeHandle handle,
        in NativeCameraState cameraState);

    [DllImport(DllName, CallingConvention = CallingConvention.StdCall, ExactSpelling = true)]
    internal static extern Result XbPreview_SetCursorMode(
        XbPreviewSafeHandle handle,
        CursorMode cursorMode);

    [DllImport(DllName, CallingConvention = CallingConvention.StdCall, ExactSpelling = true)]
    internal static extern Result XbPreview_SetRecordCursorVisible(
        XbPreviewSafeHandle handle,
        uint visible);

    [DllImport(DllName, CallingConvention = CallingConvention.StdCall, ExactSpelling = true)]
    internal static extern Result XbPreview_GetRecordCursorVisible(
        XbPreviewSafeHandle handle,
        out uint requestedVisible,
        out uint appliedVisible,
        out ulong revision);

    [DllImport(DllName, CallingConvention = CallingConvention.StdCall, ExactSpelling = true)]
    internal static extern Result XbPreview_SetCaptureTarget(
        XbPreviewSafeHandle handle,
        CaptureTargetKind targetKind,
        ulong windowHandle);

    [DllImport(DllName, CallingConvention = CallingConvention.StdCall, ExactSpelling = true)]
    internal static extern Result XbPreview_SetWindowStagePose(
        XbPreviewSafeHandle handle,
        WindowStageOrientation orientation,
        WindowStageLevel level);

    [DllImport(DllName, CallingConvention = CallingConvention.StdCall, ExactSpelling = true)]
    internal static extern Result XbPreview_SetWindowShowcasePose(
        XbPreviewSafeHandle handle,
        WindowStageOrientation orientation,
        WindowStageLevel level,
        uint active);

    [DllImport(DllName, CallingConvention = CallingConvention.StdCall, ExactSpelling = true)]
    internal static extern Result XbPreview_SetWindowShowcaseBackgroundPreset(
        XbPreviewSafeHandle handle,
        WindowShowcaseBackgroundPreset preset);

    [DllImport(
        DllName,
        CallingConvention = CallingConvention.StdCall,
        ExactSpelling = true,
        CharSet = CharSet.Unicode)]
    internal static extern Result XbPreview_SetWindowShowcaseCustomBackground(
        XbPreviewSafeHandle handle,
        string validatedLocalPath);

    [DllImport(
        DllName,
        CallingConvention = CallingConvention.StdCall,
        ExactSpelling = true,
        CharSet = CharSet.Unicode)]
    internal static extern Result XbPreview_SetRecordingOutputRoot(
        XbPreviewSafeHandle handle,
        string? validatedLocalPath);

    [DllImport(DllName, CallingConvention = CallingConvention.StdCall, ExactSpelling = true)]
    internal static extern Result XbPreview_SetRecordingFrameRate(
        XbPreviewSafeHandle handle,
        uint framesPerSecond);

    [DllImport(DllName, CallingConvention = CallingConvention.StdCall, ExactSpelling = true)]
    internal static extern Result XbPreview_GetCursorStats(
        XbPreviewSafeHandle handle,
        ref CursorStats stats);

    [DllImport(DllName, CallingConvention = CallingConvention.StdCall, ExactSpelling = true)]
    internal static extern Result XbPreview_GetStats(
        XbPreviewSafeHandle handle,
        ref PreviewStats stats);

    [DllImport(DllName, CallingConvention = CallingConvention.StdCall, ExactSpelling = true)]
    internal static extern Result XbPreview_GetGpuExportFrameV1(
        XbPreviewSafeHandle handle,
        ref GpuExportFrameV1 frame);

    [DllImport(
        DllName,
        CallingConvention = CallingConvention.StdCall,
        ExactSpelling = true,
        CharSet = CharSet.Unicode)]
    internal static extern Result XbPreview_GetLastError(
        XbPreviewSafeHandle handle,
        StringBuilder buffer,
        uint bufferLength);

    [DllImport(
        DllName,
        CallingConvention = CallingConvention.StdCall,
        ExactSpelling = true,
        CharSet = CharSet.Unicode,
        EntryPoint = "XbPreview_GetLastError")]
    internal static extern Result XbPreview_GetLastErrorRaw(
        nint handle,
        StringBuilder buffer,
        uint bufferLength);

    [DllImport(DllName, CallingConvention = CallingConvention.StdCall, ExactSpelling = true)]
    internal static extern Result XbPreview_Destroy(ref nint handle);

    [DllImport(DllName, CallingConvention = CallingConvention.StdCall, ExactSpelling = true)]
    internal static extern Result XbPreview_CalculateLetterbox(
        uint sourceWidth,
        uint sourceHeight,
        uint destinationWidth,
        uint destinationHeight,
        out LetterboxRect rect);

    internal static void ValidateManagedLayout()
    {
        if (sizeof(CreateOptions) != ExpectedCreateOptionsSize ||
            sizeof(PreviewStats) != ExpectedStatsSize ||
            sizeof(AbiLayout) != ExpectedAbiLayoutSize ||
            sizeof(LetterboxRect) != ExpectedLetterboxRectSize ||
            sizeof(NativeCameraState) != ExpectedCameraStateSize ||
            sizeof(CursorStats) != ExpectedCursorStatsSize ||
            sizeof(RecordingSnapshot) != ExpectedRecordingSnapshotSize ||
            sizeof(AudioControlsV1) != ExpectedAudioControlsV1Size ||
            sizeof(AudioControlSnapshotV1) !=
                ExpectedAudioControlSnapshotV1Size ||
            sizeof(MicrophoneDeviceListV1) !=
                ExpectedMicrophoneDeviceListV1Size ||
            sizeof(MicrophoneDeviceV1) !=
                ExpectedMicrophoneDeviceV1Size ||
            sizeof(MicrophoneSelectionV1) !=
                ExpectedMicrophoneSelectionV1Size ||
            sizeof(MicrophoneSelectionSnapshotV1) !=
                ExpectedMicrophoneSelectionSnapshotV1Size ||
            sizeof(GpuExportFrameV1) != ExpectedGpuExportFrameV1Size ||
            sizeof(HistoricalSessionScanAbiLayoutV1) !=
                ExpectedHistoricalSessionScanAbiLayoutV1Size ||
            sizeof(HistoricalSessionScanOptionsV1) !=
                ExpectedHistoricalSessionScanOptionsV1Size ||
            sizeof(HistoricalSessionScanOutputRootOptionsV1) !=
                ExpectedHistoricalSessionScanOutputRootOptionsV1Size ||
            sizeof(HistoricalSessionScanSummaryV1) !=
                ExpectedHistoricalSessionScanSummaryV1Size ||
            sizeof(HistoricalSessionItemV1) !=
                ExpectedHistoricalSessionItemV1Size ||
            sizeof(NarrowReconciliationAbiLayoutV1) !=
                ExpectedNarrowReconciliationAbiLayoutV1Size ||
            sizeof(NarrowReconciliationOptionsV1) !=
                ExpectedNarrowReconciliationOptionsV1Size ||
            sizeof(NarrowReconciliationOutputRootOptionsV1) !=
                ExpectedNarrowReconciliationOutputRootOptionsV1Size ||
            sizeof(NarrowReconciliationResultV1) !=
                ExpectedNarrowReconciliationResultV1Size ||
            Marshal.SizeOf<SessionGeometryNativeV1>() !=
                ExpectedSessionGeometryV1Size)
        {
            throw new InvalidOperationException(
                "C# P/Invoke 结构大小与 ABI v3 常量不一致。");
        }
    }

    internal static string ReadCreateError()
    {
        StringBuilder buffer = new(1024);
        _ = XbPreview_GetLastErrorRaw(
            nint.Zero,
            buffer,
            (uint)buffer.Capacity);
        return buffer.ToString();
    }
}

internal sealed class XbPreviewSafeHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    private XbPreviewSafeHandle()
        : base(ownsHandle: true)
    {
    }

    internal XbPreviewSafeHandle(nint value)
        : base(ownsHandle: true)
    {
        SetHandle(value);
    }

    protected override bool ReleaseHandle()
    {
        nint value = handle;
        NativeMethods.Result result = NativeMethods.XbPreview_Destroy(ref value);
        handle = nint.Zero;
        return result == NativeMethods.Result.Ok;
    }
}
