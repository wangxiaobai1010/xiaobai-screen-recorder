[CmdletBinding()]
param(
    [string]$EvidenceRoot = ''
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repository = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$binRoot = Join-Path $repository 'artifacts\bin\Release\x64'
$nativeDll = Join-Path $binRoot 'XbPreview.Native.dll'
$ffprobe = Join-Path $repository (
    'artifacts\audio-v2-toolchain\ffmpeg-8.1-lgpl-shared\' +
    'ffmpeg-n8.1-latest-win64-lgpl-shared-8.1\bin\ffprobe.exe')
if (-not (Test-Path -LiteralPath $nativeDll -PathType Leaf) -or
    -not (Test-Path -LiteralPath $ffprobe -PathType Leaf)) {
    throw 'Release x64 Native DLL or pinned ffprobe runtime is missing.'
}

$runRoot = if ([string]::IsNullOrWhiteSpace($EvidenceRoot)) {
    Join-Path $repository (
        'artifacts\resolution-v1-encode-gate\' +
        (Get-Date -Format 'yyyyMMdd-HHmmss-fff') + '-' +
        [Guid]::NewGuid().ToString('N'))
} else {
    (Resolve-Path -LiteralPath $EvidenceRoot).Path
}
$diagnosticsRoot = Join-Path $runRoot 'diagnostics'
$recordingsRoot = Join-Path $runRoot 'recordings'
if ([string]::IsNullOrWhiteSpace($EvidenceRoot)) {
    New-Item -ItemType Directory -Force -Path @(
        $runRoot,
        $diagnosticsRoot,
        $recordingsRoot) | Out-Null
} elseif (-not (Test-Path -LiteralPath $diagnosticsRoot -PathType Container) -or
    -not (Test-Path -LiteralPath $recordingsRoot -PathType Container)) {
    throw 'Evidence root does not contain diagnostics and recordings.'
}

$source = @'
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

internal static class ResolutionV1EncodeHarness
{
    private const uint ApiVersion = 0x00040004;

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    private struct CreateOptions
    {
        internal uint StructSize;
        internal uint ApiVersion;
        internal ulong ExclusionWindow;
        internal uint AllowWarp;
        internal uint FramePoolBufferCount;
        internal uint StatsIntervalMilliseconds;
        internal uint Reserved0;
        internal IntPtr DiagnosticLogDirectory;
        internal ulong Reserved1;
        internal ulong Reserved2;
        internal ulong Reserved3;
        internal ulong Reserved4;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    private struct SessionGeometry
    {
        internal uint StructSize;
        internal uint Version;
        internal uint SourceWidth;
        internal uint SourceHeight;
        internal int CaptureLeft;
        internal int CaptureTop;
        internal uint CaptureWidth;
        internal uint CaptureHeight;
        internal uint OutputWidth;
        internal uint OutputHeight;
        internal ulong GeometryRevision;
        internal ulong Flags;
    }

    [DllImport("XbPreview.Native.dll", CallingConvention = CallingConvention.StdCall)]
    private static extern int XbPreview_Create(
        IntPtr previewHwnd, ref CreateOptions options, out IntPtr handle);
    [DllImport("XbPreview.Native.dll", CallingConvention = CallingConvention.StdCall)]
    private static extern int XbPreview_Start(IntPtr handle);
    [DllImport("XbPreview.Native.dll", CallingConvention = CallingConvention.StdCall)]
    private static extern int XbPreview_Stop(IntPtr handle);
    [DllImport("XbPreview.Native.dll", CallingConvention = CallingConvention.StdCall)]
    private static extern int XbPreview_Destroy(ref IntPtr handle);
    [DllImport("XbPreview.Native.dll", CallingConvention = CallingConvention.StdCall)]
    private static extern int XbPreview_SetSessionGeometry(
        IntPtr handle, ref SessionGeometry geometry);
    [DllImport("XbPreview.Native.dll", CallingConvention = CallingConvention.StdCall,
        CharSet = CharSet.Unicode)]
    private static extern int XbPreview_SetRecordingOutputRoot(
        IntPtr handle, string path);
    [DllImport("XbPreview.Native.dll", CallingConvention = CallingConvention.StdCall)]
    private static extern int XbPreview_SetAudioProgramMode(IntPtr handle, int mode);
    [DllImport("XbPreview.Native.dll", CallingConvention = CallingConvention.StdCall)]
    private static extern int XbPreview_SetRecordingFrameRate(
        IntPtr handle, uint framesPerSecond);
    [DllImport("XbPreview.Native.dll", CallingConvention = CallingConvention.StdCall)]
    private static extern int XbPreview_StartRecording(IntPtr handle);
    [DllImport("XbPreview.Native.dll", CallingConvention = CallingConvention.StdCall)]
    private static extern int XbPreview_StopRecording(IntPtr handle);
    [DllImport("XbPreview.Native.dll", CallingConvention = CallingConvention.StdCall)]
    private static extern int XbPreview_GetRecordingSnapshot(
        IntPtr handle, IntPtr snapshot);
    [DllImport("XbPreview.Native.dll", CallingConvention = CallingConvention.StdCall)]
    private static extern int XbPreview_GetStats(IntPtr handle, IntPtr stats);

    [STAThread]
    public static int Main(string[] args)
    {
        try
        {
            if (args.Length != 2)
            {
                throw new ArgumentException(
                    "Expected diagnostics root and recordings root.");
            }
            return Run(Path.GetFullPath(args[0]), Path.GetFullPath(args[1]));
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }

    private static int Run(string diagnosticsRoot, string recordingsRoot)
    {
        Directory.CreateDirectory(diagnosticsRoot);
        Directory.CreateDirectory(recordingsRoot);
        using (Form window = new Form())
        using (Panel surface = new Panel())
        {
            window.Text = "Resolution v1 automatic encode gate";
            window.ClientSize = new System.Drawing.Size(640, 360);
            surface.Parent = window;
            surface.Dock = DockStyle.Fill;
            window.Show();
            Application.DoEvents();

            int[,] cases = new int[,]
            {
                { 1920, 1080, 30 },
                { 1920, 1080, 60 },
                { 2560, 1440, 30 },
                { 2560, 1440, 60 },
                { 3840, 2160, 30 },
                { 3840, 2160, 60 },
            };
            for (int index = 0; index < cases.GetLength(0); index++)
            {
                RunCase(
                    window,
                    surface,
                    diagnosticsRoot,
                    recordingsRoot,
                    cases[index, 0],
                    cases[index, 1],
                    cases[index, 2],
                    index + 1);
            }
        }
        return 0;
    }

    private static void RunCase(
        Form window,
        Panel surface,
        string diagnosticsRoot,
        string recordingsRoot,
        int width,
        int height,
        int fps,
        int revision)
    {
        string name = width + "x" + height + "-" + fps;
        string diagnostics = Path.Combine(diagnosticsRoot, name);
        string recordings = Path.Combine(recordingsRoot, name);
        Directory.CreateDirectory(diagnostics);
        Directory.CreateDirectory(recordings);

        IntPtr handle = IntPtr.Zero;
        IntPtr diagnosticPointer = Marshal.StringToHGlobalUni(diagnostics);
        try
        {
            CreateOptions options = new CreateOptions();
            options.StructSize = 72;
            options.ApiVersion = ApiVersion;
            options.ExclusionWindow = unchecked((ulong)window.Handle.ToInt64());
            options.AllowWarp = 1;
            options.FramePoolBufferCount = 2;
            options.StatsIntervalMilliseconds = 250;
            options.DiagnosticLogDirectory = diagnosticPointer;
            Require(XbPreview_Create(surface.Handle, ref options, out handle),
                name + " create");

            System.Drawing.Rectangle bounds = Screen.PrimaryScreen.Bounds;
            SessionGeometry geometry = new SessionGeometry();
            geometry.StructSize = 56;
            geometry.Version = 1;
            geometry.SourceWidth = (uint)bounds.Width;
            geometry.SourceHeight = (uint)bounds.Height;
            geometry.CaptureLeft = bounds.Left;
            geometry.CaptureTop = bounds.Top;
            geometry.CaptureWidth = (uint)bounds.Width;
            geometry.CaptureHeight = (uint)bounds.Height;
            geometry.OutputWidth = (uint)width;
            geometry.OutputHeight = (uint)height;
            geometry.GeometryRevision = (ulong)revision;
            Require(XbPreview_SetSessionGeometry(handle, ref geometry),
                name + " geometry");
            Require(XbPreview_SetRecordingOutputRoot(handle, recordings),
                name + " output root");
            Require(XbPreview_SetRecordingFrameRate(handle, (uint)fps),
                name + " frame rate");
            Require(XbPreview_Start(handle), name + " preview start");
            WaitForFrames(handle, name);
            Require(XbPreview_SetAudioProgramMode(handle, 0),
                name + " video-only mode");
            Require(XbPreview_StartRecording(handle), name + " record start");
            PumpFor(TimeSpan.FromMilliseconds(1500));
            Require(XbPreview_StopRecording(handle), name + " record stop");
            ValidatePublishedSnapshot(handle, recordings, name);
            Require(XbPreview_Stop(handle), name + " preview stop");
            Console.WriteLine("RESOLUTION-V1-ENCODE-CASE = PASS; " + name);
        }
        finally
        {
            Marshal.FreeHGlobal(diagnosticPointer);
            if (handle != IntPtr.Zero)
            {
                XbPreview_Stop(handle);
                XbPreview_Destroy(ref handle);
            }
        }
    }

    private static void WaitForFrames(IntPtr handle, string name)
    {
        IntPtr stats = Marshal.AllocHGlobal(1080);
        try
        {
            Stopwatch timeout = Stopwatch.StartNew();
            while (timeout.Elapsed < TimeSpan.FromSeconds(12))
            {
                Zero(stats, 1080);
                Marshal.WriteInt32(stats, 0, 1080);
                Marshal.WriteInt32(stats, 4, unchecked((int)ApiVersion));
                Require(XbPreview_GetStats(handle, stats), name + " stats");
                if (Marshal.ReadInt64(stats, 32) > 0)
                {
                    return;
                }
                Application.DoEvents();
                Thread.Sleep(40);
            }
            throw new TimeoutException(name + " preview produced no frame.");
        }
        finally
        {
            Marshal.FreeHGlobal(stats);
        }
    }

    private static void ValidatePublishedSnapshot(
        IntPtr handle,
        string outputRoot,
        string name)
    {
        IntPtr snapshot = Marshal.AllocHGlobal(2856);
        try
        {
            Zero(snapshot, 2856);
            Marshal.WriteInt32(snapshot, 0, 2856);
            Marshal.WriteInt32(snapshot, 4, unchecked((int)ApiVersion));
            Require(XbPreview_GetRecordingSnapshot(handle, snapshot),
                name + " snapshot");
            int state = Marshal.ReadInt32(snapshot, 8);
            ulong frames = unchecked((ulong)Marshal.ReadInt64(snapshot, 72));
            int ready = Marshal.ReadInt32(snapshot, 1272);
            int published = Marshal.ReadInt32(snapshot, 1276);
            int publishHResult = Marshal.ReadInt32(snapshot, 1284);
            int validationHResult = Marshal.ReadInt32(snapshot, 1292);
            string path = Marshal.PtrToStringUni(
                IntPtr.Add(snapshot, 2336)) ?? string.Empty;
            if (state != 4 || frames == 0 || ready != 1 || published != 1 ||
                publishHResult != 0 || validationHResult != 0 ||
                !File.Exists(path) ||
                !string.Equals(Path.GetDirectoryName(path), outputRoot,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    name + " safe publish failed: state=" + state +
                    "; frames=" + frames + "; path=" + path);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(snapshot);
        }
    }

    private static void PumpFor(TimeSpan duration)
    {
        Stopwatch timer = Stopwatch.StartNew();
        while (timer.Elapsed < duration)
        {
            Application.DoEvents();
            Thread.Sleep(25);
        }
    }

    private static void Zero(IntPtr pointer, int length)
    {
        Marshal.Copy(new byte[length], 0, pointer, length);
    }

    private static void Require(int result, string operation)
    {
        if (result != 0)
        {
            throw new InvalidOperationException(
                operation + " failed: " + result);
        }
    }
}
'@

if ([string]::IsNullOrWhiteSpace($EvidenceRoot)) {
    $harness = Join-Path $binRoot 'ResolutionV1EncodeHarness.exe'
    if (Test-Path -LiteralPath $harness -PathType Leaf) {
        Remove-Item -LiteralPath $harness -Force
    }
    Add-Type -TypeDefinition $source `
        -Language CSharp `
        -ReferencedAssemblies @('System.Windows.Forms.dll','System.Drawing.dll') `
        -OutputAssembly $harness `
        -OutputType ConsoleApplication

    & $harness $diagnosticsRoot $recordingsRoot
    if ($LASTEXITCODE -ne 0) {
        throw "Resolution v1 native encode harness failed: $LASTEXITCODE"
    }
}

function Assert-True([bool]$Condition, [string]$Message) {
    if (-not $Condition) {
        throw $Message
    }
}

$cases = @(
    [pscustomobject]@{ Name = '1920x1080-30'; Width = 1920; Height = 1080; Fps = 30; Bitrate = 8000000 },
    [pscustomobject]@{ Name = '1920x1080-60'; Width = 1920; Height = 1080; Fps = 60; Bitrate = 8000000 },
    [pscustomobject]@{ Name = '2560x1440-30'; Width = 2560; Height = 1440; Fps = 30; Bitrate = 12000000 },
    [pscustomobject]@{ Name = '2560x1440-60'; Width = 2560; Height = 1440; Fps = 60; Bitrate = 12000000 },
    [pscustomobject]@{ Name = '3840x2160-30'; Width = 3840; Height = 2160; Fps = 30; Bitrate = 12000000 },
    [pscustomobject]@{ Name = '3840x2160-60'; Width = 3840; Height = 2160; Fps = 60; Bitrate = 12000000 }
)

$results = foreach ($case in $cases) {
    $caseDiagnostics = Join-Path $diagnosticsRoot $case.Name
    $log = Get-ChildItem -LiteralPath $caseDiagnostics `
        -Filter 'p2.4-encoder-*.jsonl' -File |
        Select-Object -Last 1
    Assert-True ($null -ne $log) "$($case.Name): encoder log missing."
    $summary = Get-Content -LiteralPath $log.FullName -Encoding UTF8 |
        ForEach-Object {
            try { $_ | ConvertFrom-Json } catch { $null }
        } |
        Where-Object event -eq 'p2.4-encoder-summary' |
        Select-Object -Last 1
    Assert-True ($null -ne $summary) "$($case.Name): encoder summary missing."
    $capabilityFile = Get-ChildItem -LiteralPath (
        Join-Path $recordingsRoot $case.Name) `
        -Filter 'video-encoder-capabilities.json' -File -Recurse |
        Select-Object -Last 1
    Assert-True ($null -ne $capabilityFile) `
        "$($case.Name): encoder capability evidence missing."
    $capability = Get-Content -Raw -Encoding UTF8 `
        -LiteralPath $capabilityFile.FullName | ConvertFrom-Json
    $negotiation = $capability.BitrateNegotiation

    Assert-True (
        $summary.OutputWidth -eq $case.Width -and
        $summary.OutputHeight -eq $case.Height) `
        "$($case.Name): encoder dimensions differ from OutputCanvas."
    Assert-True ($summary.OutputFormat -eq 'MP4/H264-NV12') `
        "$($case.Name): encoder format is not MP4/H264-NV12."
    Assert-True (
        $summary.SelectedFps -eq $case.Fps -and
        $summary.NominalFrameRateNumerator -eq $case.Fps -and
        $summary.NominalFrameRateDenominator -eq 1) `
        "$($case.Name): selected/nominal FPS mismatch."
    Assert-True (
        $summary.Bitrate -eq $case.Bitrate -and
        $capability.SessionContext.NominalBitrate -eq $case.Bitrate -and
        $negotiation.EncoderConfigStore.RequestedMeanBitRate -eq
            $case.Bitrate) `
        "$($case.Name): session policy target did not reach encoder configuration."
    Assert-True (
        $summary.EncoderState -eq 'Completed' -and
        $summary.OutputSuccess -eq 1 -and
        $summary.FinalizeAttempted -eq 1 -and
        $summary.FinalizeHResult -eq '0x00000000') `
        "$($case.Name): Finalize/output did not complete."
    Assert-True (
        $summary.HardwareTransformRequested -eq 1 -and
        $summary.DxgiDeviceManagerBound -eq 1 -and
        $summary.ProductionHardwareEncoderRequired -eq 1 -and
        $summary.ActualHardwareEncoderVerified -eq 1 -and
        $summary.SoftwareFallbackDetected -eq 0 -and
        $summary.SoftwareFallbackRejected -eq 0 -and
        $summary.HardwareEncoderVerificationHResult -eq '0x00000000' -and
        $capability.ProbeStatus.ActualTransformObtained -and
        $capability.Identity.HardwareSoftwareVerdict -eq 'HARDWARE' -and
        $capability.Identity.FriendlyName -eq 'NVIDIA H.264 Encoder MFT' -and
        $capability.HardwareEnforcement.Required -and
        $capability.HardwareEnforcement.Verified -and
        -not $capability.HardwareEnforcement.SoftwareFallbackDetected -and
        $capability.ICodecAPI.Available) `
        "$($case.Name): production hardware H.264 MFT was not verified."
    Assert-True (
        -not $negotiation.EncoderConfigStore.CurrentCodeUsesIt -and
        -not $negotiation.EncoderConfigStore.CurrentCodeOnlyUsesMediaTypeBitrate -and
        $negotiation.EncoderConfigStore.RequestedRateControl -eq 'CBR' -and
        $negotiation.InputMediaTypeEncodingParameters.Used -and
        $negotiation.InputMediaTypeEncodingParameters.RateControlPropertySetHRESULT -eq
            '0x00000000' -and
        $negotiation.InputMediaTypeEncodingParameters.MeanBitRatePropertySetHRESULT -eq
            '0x00000000') `
        "$($case.Name): SetInputMediaType encoding-parameters contract failed."
    foreach ($readback in @(
        $negotiation.CodecApiPreBegin,
        $negotiation.CodecApiPostBegin,
        $negotiation.CodecApiPostFirstSample)) {
        Assert-True (
            $readback.CodecApiHResult -eq '0x00000000' -and
            $readback.RateControl.Name -eq 'CBR' -and
            [long]$readback.MeanBitrate.Value -eq $case.Bitrate) `
            "$($case.Name): actual MFT CBR/MeanBitRate readback differs from policy."
    }
    Assert-True (
        $summary.VideoProcessorInputSupported -eq 1 -and
        $summary.VideoProcessorNv12OutputSupported -eq 1 -and
        $summary.FramesConvertedToNv12 -gt 0 -and
        $summary.FramesSubmittedToSinkWriter -gt 0) `
        "$($case.Name): BGRA/NV12/MF dimension path was not active."
    Assert-True (
        $summary.FramesDroppedNv12Starvation -eq 0 -and
        $summary.Nv12PoolStarvation -eq 0 -and
        $summary.VideoProcessorFailures -eq 0 -and
        $summary.WriteSampleFailures -eq 0) `
        "$($case.Name): NV12, VideoProcessor, or WriteSample failure."
    Assert-True (
        $summary.DeviceRemovedReason -eq '0x00000000' -and
        $summary.TrackedReturnTimedOut -eq 0 -and
        $summary.Nv12OutstandingCurrent -eq 0 -and
        $summary.TrackedCallbackAfterStop -eq 0 -and
        $summary.ConsumerConflict -eq 0) `
        "$($case.Name): resource/device shutdown invariant failed."
    Assert-True (
        $summary.SourceReaderValidation -eq 'PASS' -and
        $summary.DecodedFrameCount -gt 0 -and
        $summary.ValidatedLastPts -gt $summary.ValidatedFirstPts) `
        "$($case.Name): decoded MP4 timestamp validation failed."
    $floorDuration = [math]::Floor(10000000.0 / $case.Fps)
    $ceilingDuration = [math]::Ceiling(10000000.0 / $case.Fps)
    Assert-True (
        $summary.SampleDurationMin -ge $floorDuration -and
        $summary.SampleDurationMax -le $ceilingDuration) `
        "$($case.Name): CFR sample duration is outside floor/ceiling."
    $maximumMissed = [math]::Max(10, [math]::Ceiling($summary.OutputTicks * 0.30))
    Assert-True ($summary.MissedDeadlines -le $maximumMissed) (
        "$($case.Name): severe cadence regression; missed deadlines=" +
        "$($summary.MissedDeadlines)/$($summary.OutputTicks).")

    $mp4 = @(
        Get-ChildItem -LiteralPath (Join-Path $recordingsRoot $case.Name) `
            -Filter '*.mp4' -File
    )
    Assert-True ($mp4.Count -eq 1) `
        "$($case.Name): expected exactly one published MP4."
    $probeJson = & $ffprobe -v error -select_streams v:0 `
        -show_entries stream=codec_name,width,height,r_frame_rate,avg_frame_rate,time_base `
        -of json $mp4[0].FullName
    Assert-True ($LASTEXITCODE -eq 0) "$($case.Name): ffprobe failed."
    $probe = ($probeJson -join [Environment]::NewLine) | ConvertFrom-Json
    $stream = $probe.streams[0]
    Assert-True (
        $stream.codec_name -eq 'h264' -and
        $stream.width -eq $case.Width -and
        $stream.height -eq $case.Height -and
        $stream.r_frame_rate -eq "$($case.Fps)/1") `
        "$($case.Name): published MP4 stream contract mismatch."

    [pscustomobject]@{
        Case = $case.Name
        Encoder = $summary.EncoderFriendlyName
        EncoderIdentity = $capability.Identity.FriendlyName
        TargetBitrate = $case.Bitrate
        PreBeginMeanBitrate = [long]$negotiation.CodecApiPreBegin.MeanBitrate.Value
        PostBeginMeanBitrate = [long]$negotiation.CodecApiPostBegin.MeanBitrate.Value
        PostFirstSampleMeanBitrate = [long]$negotiation.CodecApiPostFirstSample.MeanBitrate.Value
        EncoderConfigStore = $negotiation.EncoderConfigStore
        Output = "$($stream.width)x$($stream.height)"
        Fps = $stream.r_frame_rate
        Frames = $summary.SubmittedFrames
        MissedDeadlines = $summary.MissedDeadlines
        OutputTicks = $summary.OutputTicks
        FrameTapDrops =
            $summary.TapFramesDroppedNoFreeSlot +
            $summary.TapFramesDroppedQueueFull +
            $summary.TapFramesDroppedGenerationMismatch
        FrameTapQueueHighWatermark = $summary.TapQueueDepthHighWatermark
        Nv12Starvation = $summary.FramesDroppedNv12Starvation
        WriteSampleFailures = $summary.WriteSampleFailures
        WriteSampleP95Ms = $summary.WriteSampleDurationP95
        WriteSampleMaxMs = $summary.WriteSampleDurationMax
        DeviceRemovedReason = $summary.DeviceRemovedReason
        Finalize = $summary.FinalizeHResult
        File = $mp4[0].FullName
        Bytes = $mp4[0].Length
    }
}

$evidencePath = Join-Path $runRoot 'resolution-v1-encode-results.json'
$results | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $evidencePath -Encoding UTF8
$results | Format-Table -AutoSize
Write-Host "RESOLUTION-V1-ENCODE-GATE = PASS; evidence=$evidencePath"
