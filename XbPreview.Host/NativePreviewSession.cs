using System.Runtime.InteropServices;
using System.Text;

namespace XbPreview.Host;

internal readonly record struct NativeGpuStreamReadStamp(
    ulong StreamGeneration,
    long TransitionSequence);

internal sealed unsafe class NativePreviewSession : IPreviewNativeSession
{
    private XbPreviewSafeHandle? _handle;
    private readonly uint _nativeApiVersion;
    private readonly object _gpuStreamTransitionGate = new();
    private bool _disposed;
    private bool _stopRequired;
    private long _gpuStreamGeneration;
    private long _gpuStreamTransitionSequence;
    private int _gpuStreamActive;

    private NativePreviewSession(
        XbPreviewSafeHandle handle,
        uint nativeApiVersion)
    {
        _handle = handle;
        _nativeApiVersion = nativeApiVersion;
    }

    internal static NativePreviewSession Create(
        nint renderWindow,
        nint exclusionWindow,
        string diagnosticLogDirectory)
    {
        NativeMethods.ValidateManagedLayout();

        uint nativeVersion = NativeMethods.XbPreview_GetApiVersion();
        if ((nativeVersion & 0xFFFF_0000U) !=
            (NativeMethods.ApiVersion & 0xFFFF_0000U))
        {
            throw new InvalidOperationException(
                $"Native API 主版本不匹配：0x{nativeVersion:X8}。");
        }

        NativeMethods.AbiLayout nativeLayout = new()
        {
            StructSize = (uint)sizeof(NativeMethods.AbiLayout),
            ApiVersion = NativeMethods.ApiVersion,
        };
        Ensure(
            NativeMethods.XbPreview_GetAbiLayout(ref nativeLayout),
            "读取 native ABI layout");
        if (nativeLayout.PointerSize != 8 ||
            nativeLayout.Packing != 8 ||
            nativeLayout.CreateOptionsSize != NativeMethods.ExpectedCreateOptionsSize ||
            nativeLayout.StatsSize != NativeMethods.ExpectedStatsSize ||
            nativeLayout.LetterboxRectSize != NativeMethods.ExpectedLetterboxRectSize ||
            nativeLayout.CameraStateSize != NativeMethods.ExpectedCameraStateSize ||
            nativeLayout.CursorStatsSize != NativeMethods.ExpectedCursorStatsSize ||
            nativeLayout.RecordingSnapshotSize !=
                NativeMethods.ExpectedRecordingSnapshotSize ||
            nativeLayout.WcharSize != 2)
        {
            throw new InvalidOperationException(
                "Native/C# ABI layout 不一致，已拒绝创建预览。");
        }

        nint logDirectoryPointer = Marshal.StringToHGlobalUni(
            diagnosticLogDirectory);
        try
        {
            NativeMethods.CreateOptions options = new()
            {
                StructSize = (uint)sizeof(NativeMethods.CreateOptions),
                ApiVersion = NativeMethods.ApiVersion,
                ExclusionWindow = unchecked((ulong)exclusionWindow.ToInt64()),
                AllowWarp = 1,
                FramePoolBufferCount = 2,
                StatsIntervalMilliseconds = 1000,
                DiagnosticLogDirectory = logDirectoryPointer,
            };

            NativeMethods.Result result = NativeMethods.XbPreview_Create(
                renderWindow,
                in options,
                out nint rawHandle);
            if (result != NativeMethods.Result.Ok)
            {
                string detail = NativeMethods.ReadCreateError();
                throw new InvalidOperationException(
                    $"创建 native P0 引擎失败：{result}。{detail}");
            }

            return new NativePreviewSession(
                new XbPreviewSafeHandle(rawHandle),
                nativeVersion);
        }
        finally
        {
            Marshal.FreeHGlobal(logDirectoryPointer);
        }
    }

    public NativeMethods.Result Start()
    {
        lock (_gpuStreamTransitionGate)
        {
            ThrowIfDisposed();
            BeginGpuStreamTransition(startingNewStream: true);
            NativeMethods.Result result = NativeMethods.Result.NativeFailure;
            try
            {
                _stopRequired = true;
                result = NativeMethods.XbPreview_Start(_handle!);
                return result;
            }
            finally
            {
                EndGpuStreamTransition(result == NativeMethods.Result.Ok);
            }
        }
    }

    public NativeMethods.Result Stop()
    {
        lock (_gpuStreamTransitionGate)
        {
            if (_disposed || _handle is null || _handle.IsInvalid)
            {
                return NativeMethods.Result.Ok;
            }

            BeginGpuStreamTransition(startingNewStream: false);
            try
            {
                NativeMethods.Result result =
                    NativeMethods.XbPreview_Stop(_handle);
                _stopRequired = false;
                return result;
            }
            finally
            {
                EndGpuStreamTransition(active: false);
            }
        }
    }

    public NativeMethods.Result StartRecording()
    {
        ThrowIfDisposed();
        return NativeMethods.XbPreview_StartRecording(_handle!);
    }

    public NativeMethods.Result StopRecording()
    {
        ThrowIfDisposed();
        return NativeMethods.XbPreview_StopRecording(_handle!);
    }

    public NativeMethods.Result CancelRecording()
    {
        ThrowIfDisposed();
        return NativeMethods.XbPreview_CancelRecording(_handle!);
    }

    public NativeMethods.Result PauseRecording()
    {
        ThrowIfDisposed();
        EnsurePauseResumeSupported();
        return NativeMethods.XbPreview_PauseRecording(_handle!);
    }

    public NativeMethods.Result ResumeRecording()
    {
        ThrowIfDisposed();
        EnsurePauseResumeSupported();
        return NativeMethods.XbPreview_ResumeRecording(_handle!);
    }

    public NativeMethods.RecordingSnapshot GetRecordingSnapshot()
    {
        ThrowIfDisposed();
        NativeMethods.RecordingSnapshot snapshot = new()
        {
            StructSize = (uint)sizeof(NativeMethods.RecordingSnapshot),
            ApiVersion = NativeMethods.ApiVersion,
        };
        Ensure(
            NativeMethods.XbPreview_GetRecordingSnapshot(
                _handle!, ref snapshot),
            "read native recording snapshot");
        return snapshot;
    }

    public NativeMethods.Result SetAudioControls(
        bool systemMuted,
        bool microphoneMuted,
        double microphoneGainDb)
    {
        ThrowIfDisposed();
        NativeMethods.AudioControlsV1 controls = new()
        {
            StructSize = NativeMethods.ExpectedAudioControlsV1Size,
            AbiVersion = NativeMethods.AudioControlsAbiVersionV1,
            SystemMuted = systemMuted ? 1u : 0u,
            MicrophoneMuted = microphoneMuted ? 1u : 0u,
            MicrophoneGainDb = microphoneGainDb,
        };
        return NativeMethods.XbPreview_SetAudioControlsV1(
            _handle!, in controls);
    }

    public NativeMethods.AudioControlSnapshotV1 GetAudioControlSnapshot()
    {
        ThrowIfDisposed();
        NativeMethods.AudioControlSnapshotV1 snapshot = new()
        {
            StructSize = NativeMethods.ExpectedAudioControlSnapshotV1Size,
            AbiVersion = NativeMethods.AudioControlsAbiVersionV1,
        };
        Ensure(
            NativeMethods.XbPreview_GetAudioControlSnapshotV1(
                _handle!, ref snapshot),
            "read native audio control snapshot");
        return snapshot;
    }

    public NativeMethods.Result Resize(int width, int height)
    {
        ThrowIfDisposed();
        return NativeMethods.XbPreview_Resize(_handle!, width, height);
    }

    public NativeMethods.Result SetGpuExportTargetSize(int width, int height)
    {
        ThrowIfDisposed();
        return NativeMethods.XbPreview_SetGpuExportTargetSize(
            _handle!, width, height);
    }

    public NativeMethods.Result SetSessionGeometry(
        in SessionGeometryNativeV1 geometry)
    {
        ThrowIfDisposed();
        return NativeMethods.XbPreview_SetSessionGeometry(
            _handle!,
            in geometry);
    }

    public NativeMethods.Result SetCameraState(CameraState state)
    {
        ThrowIfDisposed();
        NativeMethods.NativeCameraState native = new()
        {
            StructSize = (uint)sizeof(NativeMethods.NativeCameraState),
            ApiVersion = NativeMethods.ApiVersion,
            Sequence = state.Sequence,
            TimestampQpc = state.TimestampQpc,
            Enabled = state.Enabled ? 1u : 0u,
            Mode = state.Mode,
            Zoom = state.Zoom,
            CenterX = state.CenterX,
            CenterY = state.CenterY,
            TransitionProgress = state.TransitionProgress,
            TargetX = state.TargetX,
            TargetY = state.TargetY,
            ClampX = state.ClampX ? 1u : 0u,
            ClampY = state.ClampY ? 1u : 0u,
        };
        return NativeMethods.XbPreview_SetCameraState(_handle!, in native);
    }

    public NativeMethods.Result SetCursorMode(
        NativeMethods.CursorMode cursorMode)
    {
        ThrowIfDisposed();
        return NativeMethods.XbPreview_SetCursorMode(_handle!, cursorMode);
    }

    public NativeMethods.Result SetRecordCursorVisible(bool visible)
    {
        ThrowIfDisposed();
        return NativeMethods.XbPreview_SetRecordCursorVisible(
            _handle!, visible ? 1u : 0u);
    }

    public RecordCursorVisibilitySnapshot GetRecordCursorVisible()
    {
        ThrowIfDisposed();
        NativeMethods.Result result =
            NativeMethods.XbPreview_GetRecordCursorVisible(
                _handle!,
                out uint requestedVisible,
                out uint appliedVisible,
                out ulong revision);
        Ensure(result, "GetRecordCursorVisible");
        return new RecordCursorVisibilitySnapshot(
            requestedVisible != 0,
            appliedVisible != 0,
            revision);
    }

    public NativeMethods.Result SetAudioProgramMode(
        NativeMethods.AudioProgramMode mode)
    {
        ThrowIfDisposed();
        return NativeMethods.XbPreview_SetAudioProgramMode(_handle!, mode);
    }

    public MicrophoneDeviceCatalog GetMicrophoneDevices()
    {
        ThrowIfDisposed();
        for (int attempt = 0; attempt < 3; attempt++)
        {
            NativeMethods.MicrophoneDeviceListV1 list = new()
            {
                StructSize = NativeMethods.ExpectedMicrophoneDeviceListV1Size,
                AbiVersion = NativeMethods.MicrophoneDeviceAbiVersionV1,
            };
            Ensure(
                NativeMethods.XbPreview_GetMicrophoneDeviceListV1(
                    _handle!, ref list),
                "读取麦克风设备列表");
            List<MicrophoneDevice> devices = new((int)list.DeviceCount);
            bool retry = false;
            for (uint index = 0; index < list.DeviceCount; index++)
            {
                NativeMethods.MicrophoneDeviceV1 device = new()
                {
                    StructSize = NativeMethods.ExpectedMicrophoneDeviceV1Size,
                    AbiVersion = NativeMethods.MicrophoneDeviceAbiVersionV1,
                    Generation = list.Generation,
                    Index = index,
                };
                NativeMethods.Result result =
                    NativeMethods.XbPreview_GetMicrophoneDeviceV1(
                        _handle!, ref device);
                if (result == NativeMethods.Result.RevisionConflict)
                {
                    retry = true;
                    break;
                }
                Ensure(result, "读取麦克风设备");
                devices.Add(new MicrophoneDevice(
                    device.GetEndpointId(),
                    device.GetDisplayName()));
            }
            if (retry)
            {
                continue;
            }
            return new MicrophoneDeviceCatalog(
                list.Generation,
                list.MonitorActive != 0,
                list.DefaultAvailable != 0,
                list.GetDefaultEndpointId(),
                list.GetDefaultDisplayName(),
                list.DeviceAddedCount,
                list.DeviceRemovedCount,
                devices);
        }
        throw new InvalidOperationException(
            "麦克风设备列表在读取期间持续变化，请重试。");
    }

    public NativeMethods.Result SetMicrophoneSelection(
        MicrophoneSelection selection)
    {
        ThrowIfDisposed();
        NativeMethods.MicrophoneSelectionV1 native = new()
        {
            StructSize = NativeMethods.ExpectedMicrophoneSelectionV1Size,
            AbiVersion = NativeMethods.MicrophoneDeviceAbiVersionV1,
            Kind = (NativeMethods.MicrophoneSelectionKindV1)selection.Kind,
        };
        native.SetEndpointId(selection.EndpointId);
        native.SetDisplayName(selection.DisplayName);
        return NativeMethods.XbPreview_SetMicrophoneSelectionV1(
            _handle!, in native);
    }

    public MicrophoneSelectionStatus GetMicrophoneSelection()
    {
        ThrowIfDisposed();
        NativeMethods.MicrophoneSelectionSnapshotV1 native = new()
        {
            StructSize =
                NativeMethods.ExpectedMicrophoneSelectionSnapshotV1Size,
            AbiVersion = NativeMethods.MicrophoneDeviceAbiVersionV1,
        };
        Ensure(
            NativeMethods.XbPreview_GetMicrophoneSelectionV1(
                _handle!, ref native),
            "读取麦克风选择状态");
        return new MicrophoneSelectionStatus(
            (MicrophoneSelectionKind)native.Kind,
            native.Available != 0,
            native.SessionLocked != 0,
            native.GetEndpointId(),
            native.GetDisplayName());
    }

    public NativeMethods.Result SetCaptureTarget(CaptureTarget target)
    {
        ThrowIfDisposed();
        return NativeMethods.XbPreview_SetCaptureTarget(
            _handle!,
            (NativeMethods.CaptureTargetKind)target.Kind,
            unchecked((ulong)target.WindowHandle.ToInt64()));
    }

    public NativeMethods.Result SetWindowStagePose(
        NativeMethods.WindowStageOrientation orientation,
        NativeMethods.WindowStageLevel level)
    {
        ThrowIfDisposed();
        return NativeMethods.XbPreview_SetWindowStagePose(
            _handle!, orientation, level);
    }

    public NativeMethods.Result SetWindowShowcasePose(
        NativeMethods.WindowStageOrientation orientation,
        NativeMethods.WindowStageLevel level,
        bool active)
    {
        ThrowIfDisposed();
        return NativeMethods.XbPreview_SetWindowShowcasePose(
            _handle!, orientation, level, active ? 1u : 0u);
    }

    public NativeMethods.Result SetWindowShowcaseBackgroundPreset(
        NativeMethods.WindowShowcaseBackgroundPreset preset)
    {
        ThrowIfDisposed();
        return NativeMethods.XbPreview_SetWindowShowcaseBackgroundPreset(
            _handle!, preset);
    }

    public NativeMethods.Result SetWindowShowcaseCustomBackground(
        string validatedLocalPath)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(validatedLocalPath);
        return NativeMethods.XbPreview_SetWindowShowcaseCustomBackground(
            _handle!, validatedLocalPath);
    }

    public NativeMethods.Result SetRecordingOutputRoot(
        string? validatedLocalPath)
    {
        ThrowIfDisposed();
        return NativeMethods.XbPreview_SetRecordingOutputRoot(
            _handle!, validatedLocalPath);
    }

    public NativeMethods.Result SetRecordingFrameRate(uint framesPerSecond)
    {
        ThrowIfDisposed();
        return NativeMethods.XbPreview_SetRecordingFrameRate(
            _handle!, framesPerSecond);
    }

    public NativeMethods.CursorStats GetCursorStats()
    {
        ThrowIfDisposed();
        NativeMethods.CursorStats stats = new()
        {
            StructSize = (uint)sizeof(NativeMethods.CursorStats),
            ApiVersion = NativeMethods.ApiVersion,
        };
        Ensure(
            NativeMethods.XbPreview_GetCursorStats(_handle!, ref stats),
            "读取 native cursor stats");
        return stats;
    }

    public NativeMethods.PreviewStats GetStats()
    {
        ThrowIfDisposed();
        NativeMethods.PreviewStats stats = new()
        {
            StructSize = (uint)sizeof(NativeMethods.PreviewStats),
            ApiVersion = NativeMethods.ApiVersion,
        };
        Ensure(
            NativeMethods.XbPreview_GetStats(_handle!, ref stats),
            "读取 native stats");
        return stats;
    }

    public bool TryGetGpuExportFrame(
        out NativeMethods.GpuExportFrameV1 frame)
    {
        ThrowIfDisposed();
        frame = new NativeMethods.GpuExportFrameV1
        {
            StructSize = NativeMethods.ExpectedGpuExportFrameV1Size,
            Version = NativeMethods.GpuExportAbiVersionV1,
        };
        NativeMethods.Result result =
            NativeMethods.XbPreview_GetGpuExportFrameV1(_handle!, ref frame);
        if (result == NativeMethods.Result.InvalidState)
        {
            frame = default;
            return false;
        }
        Ensure(result, "read native GPU export frame");
        return true;
    }

    internal bool TryBeginGpuFrameRead(
        out NativeGpuStreamReadStamp stamp)
    {
        long sequence = Volatile.Read(ref _gpuStreamTransitionSequence);
        if ((sequence & 1) != 0 || Volatile.Read(ref _gpuStreamActive) == 0)
        {
            stamp = default;
            return false;
        }

        ulong generation = unchecked((ulong)Volatile.Read(
            ref _gpuStreamGeneration));
        if (sequence != Volatile.Read(ref _gpuStreamTransitionSequence) ||
            Volatile.Read(ref _gpuStreamActive) == 0)
        {
            stamp = default;
            return false;
        }

        stamp = new NativeGpuStreamReadStamp(generation, sequence);
        return true;
    }

    internal bool IsGpuFrameReadCurrent(
        NativeGpuStreamReadStamp stamp) =>
        Volatile.Read(ref _gpuStreamActive) != 0 &&
        unchecked((ulong)Volatile.Read(ref _gpuStreamGeneration)) ==
            stamp.StreamGeneration &&
        Volatile.Read(ref _gpuStreamTransitionSequence) ==
            stamp.TransitionSequence;

    internal bool IsGpuStreamCurrent(ulong streamGeneration)
    {
        long sequence = Volatile.Read(ref _gpuStreamTransitionSequence);
        return (sequence & 1) == 0 &&
            Volatile.Read(ref _gpuStreamActive) != 0 &&
            unchecked((ulong)Volatile.Read(ref _gpuStreamGeneration)) ==
                streamGeneration &&
            Volatile.Read(ref _gpuStreamTransitionSequence) == sequence;
    }

    internal ulong GpuStreamGeneration =>
        unchecked((ulong)Volatile.Read(ref _gpuStreamGeneration));

    internal bool GpuStreamActive =>
        Volatile.Read(ref _gpuStreamActive) != 0;

    public string GetLastError()
    {
        if (_disposed || _handle is null || _handle.IsInvalid)
        {
            return string.Empty;
        }

        StringBuilder buffer = new(2048);
        _ = NativeMethods.XbPreview_GetLastError(
            _handle,
            buffer,
            (uint)buffer.Capacity);
        return buffer.ToString();
    }

    public void Dispose()
    {
        lock (_gpuStreamTransitionGate)
        {
            if (_disposed)
            {
                return;
            }

            BeginGpuStreamTransition(startingNewStream: false);
            try
            {
                _disposed = true;
                if (_handle is not null)
                {
                    if (_stopRequired)
                    {
                        _ = NativeMethods.XbPreview_Stop(_handle);
                        _stopRequired = false;
                    }
                    _handle.Dispose();
                    _handle = null;
                }
            }
            finally
            {
                EndGpuStreamTransition(active: false);
            }
        }
    }

    private void BeginGpuStreamTransition(bool startingNewStream)
    {
        _ = Interlocked.Increment(ref _gpuStreamTransitionSequence);
        Volatile.Write(ref _gpuStreamActive, 0);
        if (startingNewStream)
        {
            _ = Interlocked.Increment(ref _gpuStreamGeneration);
        }
    }

    private void EndGpuStreamTransition(bool active)
    {
        Volatile.Write(ref _gpuStreamActive, active ? 1 : 0);
        _ = Interlocked.Increment(ref _gpuStreamTransitionSequence);
    }

    private static void Ensure(NativeMethods.Result result, string operation)
    {
        if (result != NativeMethods.Result.Ok)
        {
            throw new InvalidOperationException(
                $"{operation}失败：{result}。{NativeMethods.ReadCreateError()}");
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private void EnsurePauseResumeSupported()
    {
        if (!SupportsPauseResumeVersion(_nativeApiVersion))
        {
            const uint requiredVersion = 0x0004_0004U;
            throw new InvalidOperationException(
                $"Native Pause/Resume requires API 0x{requiredVersion:X8}; " +
                $"loaded 0x{_nativeApiVersion:X8}.");
        }
    }

    internal static bool SupportsPauseResumeVersion(uint nativeApiVersion)
    {
        const uint requiredVersion = 0x0004_0004U;
        return (nativeApiVersion & 0xFFFF_0000U) ==
                (requiredVersion & 0xFFFF_0000U) &&
            (nativeApiVersion & 0x0000_FFFFU) >=
                (requiredVersion & 0x0000_FFFFU);
    }
}
