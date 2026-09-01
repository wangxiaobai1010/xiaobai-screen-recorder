[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Hwnd,

    [Parameter(Mandatory = $true)]
    [string]$Label,

    [Parameter(Mandatory = $true)]
    [string]$NativeDirectory,

    [ValidateSet('Resize', 'Lifecycle')]
    [string]$Mode = 'Resize',

    [ValidateSet('Identity', 'Persistent25D')]
    [string]$PresentationMode = 'Identity',

    [ValidateSet('RIGHT', 'LEFT', 'FRONT')]
    [string]$MotionDirection = 'RIGHT',

    [ValidateSet('LEVEL_1', 'LEVEL_2', 'LEVEL_3')]
    [string]$MotionStrength = 'LEVEL_2',

    [string]$FinalMp4Path = '',

    [ValidateRange(5, 30)]
    [int]$LifecycleSeconds = 12
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

$interop = @'
using System;
using System.Runtime.InteropServices;
using System.Text;

public static class WindowTargetAuditInterop
{
    public const uint ApiVersion = 0x00040003;
    public const uint GaRoot = 2;
    public const uint GwOwner = 4;
    public const uint DwmwaExtendedFrameBounds = 9;
    public const uint DwmwaCloaked = 14;
    public const int SwRestore = 9;
    public const int SwMaximize = 3;
    public const uint SwpNoZOrder = 0x0004;
    public const uint SwpNoActivate = 0x0010;

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct WINDOWPLACEMENT
    {
        public uint Length;
        public uint Flags;
        public uint ShowCmd;
        public POINT MinPosition;
        public POINT MaxPosition;
        public RECT NormalPosition;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct CreateOptions
    {
        public uint StructSize;
        public uint ApiVersion;
        public ulong ExclusionWindow;
        public uint AllowWarp;
        public uint FramePoolBufferCount;
        public uint StatsIntervalMilliseconds;
        public uint Reserved0;
        public IntPtr DiagnosticLogDirectory;
        public ulong Reserved1;
        public ulong Reserved2;
        public ulong Reserved3;
        public ulong Reserved4;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct SessionGeometry
    {
        public uint StructSize;
        public uint Version;
        public int SourceWidth;
        public int SourceHeight;
        public int CaptureLeft;
        public int CaptureTop;
        public int CaptureWidth;
        public int CaptureHeight;
        public int OutputWidth;
        public int OutputHeight;
        public ulong GeometryRevision;
        public uint Flags;
        public uint Reserved0;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8, Size = 1080)]
    public struct PreviewStats
    {
        public uint StructSize;
        public uint ApiVersion;
        public int State;
        public uint Flags;
        public ulong SessionIdHigh;
        public ulong SessionIdLow;
        public ulong CaptureFrameCount;
        public ulong PresentFrameCount;
        public ulong DroppedFrameCount;
        public ulong FramePoolRecreateCount;
        public ulong SwapChainResizeCount;
        public double CaptureFps;
        public double PresentFps;
        public double RecentLatencyMilliseconds;
        public double P50LatencyMilliseconds;
        public double P95LatencyMilliseconds;
        public double MaxLatencyMilliseconds;
        public uint CaptureWidth;
        public uint CaptureHeight;
        public uint PreviewWidth;
        public uint PreviewHeight;
        public int LastResult;
        public int DeviceRemovedReason;
        public int WdaResult;
        public uint WdaLastError;
        public uint UsedWarp;
        public uint HdrDetected;
        public long LastSystemRelativeTime100ns;
        public long LastFrameArrivalQpc;
        public long LastPresentBeforeQpc;
        public long LastPresentAfterQpc;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode, Pack = 8, Size = 2856)]
    public struct RecordingSnapshot
    {
        public uint StructSize;
        public uint ApiVersion;
        public int State;
        public int LastResult;
        public long StartUtc100ns;
        public long Elapsed100ns;
        public uint OutputSuccess;
        public uint FinalizeAttempted;
        public int FinalizeHResult;
        public int FailureHResult;
        public uint FinalizeCount;
        public uint ActiveEncoder;
        public uint ResidualOutstanding;
        public uint OutputCleanupAttempted;
        public uint OutputCleanupSucceeded;
        public int OutputCleanupHResult;
        public ulong FramesSubmitted;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string SessionId;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string OutputPath;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string ErrorMessage;
        public ulong Reserved1;
        public ulong Reserved2;
        public ulong Reserved3;
        public ulong Reserved4;
        public uint ReadyToPublish;
        public uint Published;
        public uint PublishAttempted;
        public int PublishHResult;
        public uint ValidationAttempted;
        public int ValidationHResult;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string WorkingPath;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string PlannedFinalPath;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string PublishedPath;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    public static extern bool SetDllDirectory(string path);

    [DllImport("user32.dll")]
    public static extern bool IsWindow(IntPtr window);

    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(IntPtr window);

    [DllImport("user32.dll")]
    public static extern bool IsIconic(IntPtr window);

    [DllImport("user32.dll")]
    public static extern bool IsZoomed(IntPtr window);

    [DllImport("user32.dll")]
    public static extern IntPtr GetAncestor(IntPtr window, uint flags);

    [DllImport("user32.dll")]
    public static extern IntPtr GetWindow(IntPtr window, uint command);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(
        IntPtr window,
        out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetWindowTextLength(IntPtr window);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetWindowText(
        IntPtr window,
        StringBuilder value,
        int maximumCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetClassName(
        IntPtr window,
        StringBuilder value,
        int maximumCount);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    public static extern IntPtr GetWindowLongPtr(IntPtr window, int index);

    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr window, out RECT rect);

    [DllImport("user32.dll")]
    public static extern bool SetWindowPos(
        IntPtr window,
        IntPtr insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    [DllImport("user32.dll")]
    public static extern bool ShowWindowAsync(IntPtr window, int command);

    [DllImport("user32.dll")]
    public static extern bool GetWindowPlacement(
        IntPtr window,
        ref WINDOWPLACEMENT placement);

    [DllImport("user32.dll")]
    public static extern bool SetWindowPlacement(
        IntPtr window,
        ref WINDOWPLACEMENT placement);

    [DllImport("dwmapi.dll")]
    public static extern int DwmGetWindowAttribute(
        IntPtr window,
        uint attribute,
        out uint value,
        int size);

    [DllImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")]
    public static extern int DwmGetWindowRectAttribute(
        IntPtr window,
        uint attribute,
        out RECT value,
        int size);

    [DllImport("XbPreview.Native.dll", CallingConvention = CallingConvention.StdCall)]
    public static extern int XbPreview_Create(
        IntPtr previewWindow,
        ref CreateOptions options,
        out IntPtr handle);

    [DllImport("XbPreview.Native.dll", CallingConvention = CallingConvention.StdCall)]
    public static extern int XbPreview_SetSessionGeometry(
        IntPtr handle,
        ref SessionGeometry geometry);

    [DllImport("XbPreview.Native.dll", CallingConvention = CallingConvention.StdCall)]
    public static extern int XbPreview_SetCaptureTarget(
        IntPtr handle,
        int targetKind,
        ulong windowHandle);

    [DllImport("XbPreview.Native.dll", CallingConvention = CallingConvention.StdCall)]
    public static extern int XbPreview_Start(IntPtr handle);

    [DllImport("XbPreview.Native.dll", CallingConvention = CallingConvention.StdCall)]
    public static extern int XbPreview_SetAudioProgramMode(
        IntPtr handle,
        int mode);

    [DllImport("XbPreview.Native.dll", CallingConvention = CallingConvention.StdCall)]
    public static extern int XbPreview_StartRecording(IntPtr handle);

    [DllImport("XbPreview.Native.dll", CallingConvention = CallingConvention.StdCall)]
    public static extern int XbPreview_StopRecording(IntPtr handle);

    [DllImport("XbPreview.Native.dll", CallingConvention = CallingConvention.StdCall)]
    public static extern int XbPreview_GetRecordingSnapshot(
        IntPtr handle,
        ref RecordingSnapshot snapshot);

    [DllImport("XbPreview.Native.dll", CallingConvention = CallingConvention.StdCall)]
    public static extern int XbPreview_Stop(IntPtr handle);

    [DllImport("XbPreview.Native.dll", CallingConvention = CallingConvention.StdCall)]
    public static extern int XbPreview_GetStats(
        IntPtr handle,
        ref PreviewStats stats);

    [DllImport("XbPreview.Native.dll", CallingConvention = CallingConvention.StdCall)]
    public static extern int XbPreview_Destroy(ref IntPtr handle);
}
'@

Add-Type -TypeDefinition $interop -Language CSharp

function Require([bool]$Condition, [string]$Message) {
    if (-not $Condition) {
        throw $Message
    }
}

function Hold-Phase([int]$DefaultMilliseconds, [int]$RecordingMilliseconds) {
    $milliseconds = if ([string]::IsNullOrWhiteSpace($FinalMp4Path)) {
        $DefaultMilliseconds
    }
    else {
        $RecordingMilliseconds
    }
    Start-Sleep -Milliseconds $milliseconds
    [Windows.Forms.Application]::DoEvents()
}

function Test-LiveProgress($Current, $Previous, [bool]$RequireSizeChange) {
    if ($null -eq $Current -or
        $Current.State -ne 2 -or
        $Current.LastResult -ne 0 -or
        $Current.CaptureFrameCount -eq 0 -or
        $Current.PresentFrameCount -eq 0 -or
        $Current.CaptureWidth -eq 0 -or
        $Current.CaptureHeight -eq 0) {
        return $false
    }
    if ($null -eq $Previous) {
        return $true
    }

    $progressed =
        $Current.CaptureFrameCount -gt $Previous.CaptureFrameCount -and
        $Current.PresentFrameCount -gt $Previous.PresentFrameCount -and
        $Current.LastSystemRelativeTime100ns -gt
            $Previous.LastSystemRelativeTime100ns -and
        $Current.LastFrameArrivalQpc -gt $Previous.LastFrameArrivalQpc
    $sizeChanged =
        $Current.CaptureWidth -ne $Previous.CaptureWidth -or
        $Current.CaptureHeight -ne $Previous.CaptureHeight
    $framePoolUpdated =
        $Current.FramePoolRecreateCount -gt
            $Previous.FramePoolRecreateCount
    return $progressed -and
        (-not $RequireSizeChange -or ($sizeChanged -and $framePoolUpdated))
}

function Require-LiveProgress(
    [string]$Phase,
    $Current,
    $Previous,
    [bool]$RequireSizeChange) {
    Require (Test-LiveProgress $Current $Previous $RequireSizeChange) (
        '{0}: expected Running/0 with advancing capture, timestamp, arrival, ' +
        'Present{1}; observed state={2}, result={3}, capture={4}, present={5}, ' +
        'size={6}x{7}, pool={8}' -f
            $Phase,
            $(if ($RequireSizeChange) { ', ContentSize, and FramePool' } else { '' }),
            $Current.State,
            $Current.LastResult,
            $Current.CaptureFrameCount,
            $Current.PresentFrameCount,
            $Current.CaptureWidth,
            $Current.CaptureHeight,
            $Current.FramePoolRecreateCount)
}

function Format-Hwnd([IntPtr]$Value) {
    return '0x{0:X}' -f [uint64]$Value.ToInt64()
}

function Format-Rect($Rect) {
    return '{0},{1},{2},{3}' -f $Rect.Left, $Rect.Top, $Rect.Right, $Rect.Bottom
}

function Read-Stats([IntPtr]$Handle) {
    $stats = New-Object WindowTargetAuditInterop+PreviewStats
    $stats.StructSize = 1080
    $stats.ApiVersion = [WindowTargetAuditInterop]::ApiVersion
    $result = [WindowTargetAuditInterop]::XbPreview_GetStats(
        $Handle,
        [ref]$stats)
    Require ($result -eq 0) "XbPreview_GetStats failed: $result"
    return $stats
}

function Read-RecordingSnapshot([IntPtr]$Handle) {
    $snapshot = New-Object WindowTargetAuditInterop+RecordingSnapshot
    $snapshot.StructSize = 2856
    $snapshot.ApiVersion = [WindowTargetAuditInterop]::ApiVersion
    $result = [WindowTargetAuditInterop]::XbPreview_GetRecordingSnapshot(
        $Handle,
        [ref]$snapshot)
    Require ($result -eq 0) "XbPreview_GetRecordingSnapshot failed: $result"
    return $snapshot
}

function Wait-Capture(
    [IntPtr]$Handle,
    $Previous,
    [bool]$RequireSizeChange) {
    $timer = [Diagnostics.Stopwatch]::StartNew()
    $observed = $null
    while ($timer.Elapsed -lt [TimeSpan]::FromSeconds(6)) {
        [Windows.Forms.Application]::DoEvents()
        $observed = Read-Stats $Handle
        if ($observed.LastResult -ne 0) {
            throw "Capture failed while waiting for live progress: $($observed.LastResult)"
        }
        if (Test-LiveProgress $observed $Previous $RequireSizeChange) {
            Start-Sleep -Milliseconds 250
            [Windows.Forms.Application]::DoEvents()
            $confirmed = Read-Stats $Handle
            Require-LiveProgress `
                'confirmed capture progress' `
                $confirmed `
                $Previous `
                $RequireSizeChange
            return $confirmed
        }
        Start-Sleep -Milliseconds 50
    }
    Require-LiveProgress `
        'capture progress timeout' `
        $observed `
        $Previous `
        $RequireSizeChange
    return $observed
}

function Wait-WindowSize([IntPtr]$Window, [int]$Width, [int]$Height) {
    $timer = [Diagnostics.Stopwatch]::StartNew()
    while ($timer.Elapsed -lt [TimeSpan]::FromSeconds(3)) {
        $rect = New-Object WindowTargetAuditInterop+RECT
        if ([WindowTargetAuditInterop]::GetWindowRect($Window, [ref]$rect) -and
            [Math]::Abs(($rect.Right - $rect.Left) - $Width) -le 8 -and
            [Math]::Abs(($rect.Bottom - $rect.Top) - $Height) -le 8) {
            return
        }
        [Windows.Forms.Application]::DoEvents()
        Start-Sleep -Milliseconds 50
    }
}

function Wait-WindowState([IntPtr]$Window, [bool]$Maximized) {
    $timer = [Diagnostics.Stopwatch]::StartNew()
    while ($timer.Elapsed -lt [TimeSpan]::FromSeconds(3)) {
        if (-not [WindowTargetAuditInterop]::IsIconic($Window) -and
            [WindowTargetAuditInterop]::IsZoomed($Window) -eq $Maximized) {
            return
        }
        [Windows.Forms.Application]::DoEvents()
        Start-Sleep -Milliseconds 50
    }
}

function Move-Target(
    [IntPtr]$Window,
    [int]$X,
    [int]$Y,
    [int]$Width,
    [int]$Height) {
    $flags = [WindowTargetAuditInterop]::SwpNoZOrder -bor
        [WindowTargetAuditInterop]::SwpNoActivate
    Require ([WindowTargetAuditInterop]::SetWindowPos(
        $Window,
        [IntPtr]::Zero,
        $X,
        $Y,
        $Width,
        $Height,
        $flags)) "SetWindowPos failed for ${Width}x${Height}"
    Wait-WindowSize $Window $Width $Height
}

function Write-Snapshot(
    [string]$Phase,
    [IntPtr]$Window,
    $Stats,
    $Previous,
    [string]$TargetScreenshot,
    [string]$PreviewScreenshot) {
    $rect = New-Object WindowTargetAuditInterop+RECT
    $dwmRect = New-Object WindowTargetAuditInterop+RECT
    $null = [WindowTargetAuditInterop]::GetWindowRect($Window, [ref]$rect)
    $dwmResult = [WindowTargetAuditInterop]::DwmGetWindowRectAttribute(
        $Window,
        [WindowTargetAuditInterop]::DwmwaExtendedFrameBounds,
        [ref]$dwmRect,
        [Runtime.InteropServices.Marshal]::SizeOf($dwmRect))
    $cloaked = [uint32]0
    $cloakedResult = [WindowTargetAuditInterop]::DwmGetWindowAttribute(
        $Window,
        [WindowTargetAuditInterop]::DwmwaCloaked,
        [ref]$cloaked,
        4)
    $size = '{0}x{1}' -f $Stats.CaptureWidth, $Stats.CaptureHeight
    $changed = $null -eq $Previous -or
        $Stats.CaptureWidth -ne $Previous.CaptureWidth -or
        $Stats.CaptureHeight -ne $Previous.CaptureHeight
    $presentation = Get-PresentationEvidence `
        $Stats.CaptureWidth $Stats.CaptureHeight 1920 1080
    $observedLayer4State = if ($PresentationMode -eq 'Identity') {
        'not-enabled'
    }
    elseif ($Stats.State -eq 2 -and $Stats.LastResult -eq 0) {
        'STAY'
    }
    else {
        'NOT-REACHED'
    }
    $observedLayer4StateEvidence = if ($observedLayer4State -eq 'STAY') {
        'First sample is >=650ms after first frame; Motion A enter is 360ms; unsignaled Return event leaves frozen controller in exact STAY.'
    }
    elseif ($observedLayer4State -eq 'NOT-REACHED') {
        'Engine was no longer Running at the first >=650ms sample; Native timeline must determine the pre-STAY failure.'
    }
    else {
        'Identity control; Motion A selector absent.'
    }
    [pscustomobject]@{
        record = 'phase'
        label = $Label
        phase = $Phase
        hwnd = Format-Hwnd $Window
        windowRect = Format-Rect $rect
        dwmExtendedFrameBounds = if ($dwmResult -eq 0) { Format-Rect $dwmRect } else { $null }
        isVisible = [WindowTargetAuditInterop]::IsWindowVisible($Window)
        isIconic = [WindowTargetAuditInterop]::IsIconic($Window)
        isZoomed = [WindowTargetAuditInterop]::IsZoomed($Window)
        cloaked = if ($cloakedResult -eq 0) { $cloaked } else { $null }
        contentSizeAndFramePool = $size
        sourceTextureSize = $size
        sourceTextureEvidence = 'code-proven synchronous equality'
        windowStageInputSize = $size
        windowStageEvidence = 'code-proven synchronous equality'
        previewRendererInputSize = $size
        outputCanvasSize = '1920x1080'
        presentationMode = $PresentationMode
        requestedLayer4State = $presentation.layer4State
        layer4State = $observedLayer4State
        layer4StateEvidence = $observedLayer4StateEvidence
        pose = $presentation.pose
        flatCardBounds = $presentation.flatCardBounds
        transformedQuad = $presentation.transformedQuad
        transformedCardBounds = $presentation.transformedCardBounds
        transformedShadowBounds = $presentation.transformedShadowBounds
        contentBoundsFinite = $presentation.contentBoundsFinite
        contentInsideCanvas = $presentation.contentInsideCanvas
        shadowSupportFinite = $presentation.shadowSupportFinite
        shadowSupportInsideCanvas =
            $presentation.shadowSupportInsideCanvas
        shadowSupportOverscan = $presentation.shadowSupportOverscan
        compositionAcceptedByBoundsPolicy =
            $presentation.compositionAcceptedByBoundsPolicy
        sizeChangedFromPrevious = $changed
        captureFrames = $Stats.CaptureFrameCount
        lastSystemRelativeTime100ns = $Stats.LastSystemRelativeTime100ns
        lastFrameArrivalQpc = $Stats.LastFrameArrivalQpc
        presentFrames = $Stats.PresentFrameCount
        droppedFrames = $Stats.DroppedFrameCount
        framePoolRecreateCount = $Stats.FramePoolRecreateCount
        state = $Stats.State
        lastResult = $Stats.LastResult
        targetScreenshot = $TargetScreenshot
        previewScreenshot = $PreviewScreenshot
        previewScreenshotLimit =
            'WDA_EXCLUDEFROMCAPTURE makes screen-copy non-authoritative for Preview pixels; use Native frame/present evidence.'
        targetScreenshotSha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $TargetScreenshot).Hash
        previewScreenshotSha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $PreviewScreenshot).Hash
    } | ConvertTo-Json -Compress
}

function Get-PresentationEvidence(
    [uint32]$SourceWidth,
    [uint32]$SourceHeight,
    [uint32]$CanvasWidth,
    [uint32]$CanvasHeight) {
    $availableWidth = [double]$CanvasWidth * 0.90
    $availableHeight = [double]$CanvasHeight * 0.90
    $scale = [Math]::Min(
        $availableWidth / [double]$SourceWidth,
        $availableHeight / [double]$SourceHeight)
    $cardWidth = [double]$SourceWidth * $scale
    $cardHeight = [double]$SourceHeight * $scale
    $cardLeft = ([double]$CanvasWidth - $cardWidth) * 0.5
    $cardTop = ([double]$CanvasHeight - $cardHeight) * 0.5
    $cardRight = $cardLeft + $cardWidth
    $cardBottom = $cardTop + $cardHeight

    if ($PresentationMode -eq 'Identity') {
        return [pscustomobject]@{
            layer4State = 'not-enabled'
            layer4StateEvidence = 'Identity control; Motion A selector absent'
            pose = 'Identity'
            flatCardBounds = [pscustomobject]@{
                left = $cardLeft; top = $cardTop
                right = $cardRight; bottom = $cardBottom
            }
            transformedQuad = $null
            transformedCardBounds = [pscustomobject]@{
                left = $cardLeft; top = $cardTop
                right = $cardRight; bottom = $cardBottom
            }
            transformedShadowBounds = $null
            contentBoundsFinite = $true
            contentInsideCanvas = $true
            shadowSupportFinite = $true
            shadowSupportInsideCanvas = $true
            shadowSupportOverscan = $false
            compositionAcceptedByBoundsPolicy = $true
        }
    }

    $pose = switch ($MotionDirection) {
        'LEFT' {
            switch ($MotionStrength) {
                'LEVEL_1' {
                    [pscustomobject]@{
                        direction = 'LEFT'; strength = 'LEVEL_1'; scale = 0.88
                        horizontalPlacementFraction = -0.025
                        verticalPlacementFraction = -0.018
                        rotationXDegrees = -6.0; rotationYDegrees = -18.0
                        perspectiveDepth = 0.90
                    }
                }
                'LEVEL_3' {
                    [pscustomobject]@{
                        direction = 'LEFT'; strength = 'LEVEL_3'; scale = 0.77
                        horizontalPlacementFraction = -0.060
                        verticalPlacementFraction = -0.028
                        rotationXDegrees = -10.0; rotationYDegrees = -30.0
                        perspectiveDepth = 1.10
                    }
                }
                default {
                    [pscustomobject]@{
                        direction = 'LEFT'; strength = 'LEVEL_2'; scale = 0.83
                        horizontalPlacementFraction = -0.040
                        verticalPlacementFraction = -0.022
                        rotationXDegrees = -8.0; rotationYDegrees = -24.0
                        perspectiveDepth = 1.00
                    }
                }
            }
        }
        'FRONT' {
            switch ($MotionStrength) {
                'LEVEL_1' {
                    [pscustomobject]@{
                        direction = 'FRONT'; strength = 'LEVEL_1'; scale = 0.94
                        horizontalPlacementFraction = 0.0
                        verticalPlacementFraction = -0.008
                        rotationXDegrees = -3.0; rotationYDegrees = 0.0
                        perspectiveDepth = 0.70
                    }
                }
                'LEVEL_3' {
                    [pscustomobject]@{
                        direction = 'FRONT'; strength = 'LEVEL_3'; scale = 0.86
                        horizontalPlacementFraction = 0.0
                        verticalPlacementFraction = -0.016
                        rotationXDegrees = -7.0; rotationYDegrees = 0.0
                        perspectiveDepth = 1.00
                    }
                }
                default {
                    [pscustomobject]@{
                        direction = 'FRONT'; strength = 'LEVEL_2'; scale = 0.90
                        horizontalPlacementFraction = 0.0
                        verticalPlacementFraction = -0.012
                        rotationXDegrees = -5.0; rotationYDegrees = 0.0
                        perspectiveDepth = 0.85
                    }
                }
            }
        }
        default {
            [pscustomobject]@{
                direction = 'RIGHT'; strength = 'LEVEL_2'; scale = 0.83
                horizontalPlacementFraction = 0.040
                verticalPlacementFraction = -0.022
                rotationXDegrees = -8.0; rotationYDegrees = 24.0
                perspectiveDepth = 1.00
            }
        }
    }
    $rotationX = $pose.rotationXDegrees * [Math]::PI / 180.0
    $rotationY = $pose.rotationYDegrees * [Math]::PI / 180.0
    $depthReference = [Math]::Max($cardWidth, $cardHeight)
    $centerX = $cardLeft + ($cardWidth * 0.5) +
        ($pose.horizontalPlacementFraction * $CanvasWidth)
    $centerY = $cardTop + ($cardHeight * 0.5) +
        ($pose.verticalPlacementFraction * $CanvasHeight)
    $centerNdcX = (2.0 * $centerX / $CanvasWidth) - 1.0
    $centerNdcY = 1.0 - (2.0 * $centerY / $CanvasHeight)
    $halfWidth = $cardWidth * 0.5
    $halfHeight = $cardHeight * 0.5
    $localCorners = @(
        [pscustomobject]@{ x = -$halfWidth; y = -$halfHeight },
        [pscustomobject]@{ x = $halfWidth; y = -$halfHeight },
        [pscustomobject]@{ x = -$halfWidth; y = $halfHeight },
        [pscustomobject]@{ x = $halfWidth; y = $halfHeight }
    )
    $names = @('TL', 'TR', 'BL', 'BR')
    $points = @()
    for ($index = 0; $index -lt $localCorners.Count; $index++) {
        $localX = [double]$localCorners[$index].x
        $localYDown = [double]$localCorners[$index].y
        $x = $localX * $pose.scale
        $yUp = -$localYDown * $pose.scale
        $afterXDepth = $yUp * [Math]::Sin($rotationX)
        $rotatedX = ($x * [Math]::Cos($rotationY)) +
            ($afterXDepth * [Math]::Sin($rotationY))
        $rotatedYUp = $yUp * [Math]::Cos($rotationX)
        $rotatedDepth = (-$x * [Math]::Sin($rotationY)) +
            ($afterXDepth * [Math]::Cos($rotationY))
        $w = 1.0 - ($pose.perspectiveDepth * $rotatedDepth / $depthReference)
        $clipX = ($centerNdcX * $w) + (2.0 * $rotatedX / $CanvasWidth)
        $clipY = ($centerNdcY * $w) + (2.0 * $rotatedYUp / $CanvasHeight)
        $points += [pscustomobject]@{
            corner = $names[$index]
            x = ((($clipX / $w) + 1.0) * 0.5 * $CanvasWidth)
            y = ((1.0 - ($clipY / $w)) * 0.5 * $CanvasHeight)
            homogeneousW = $w
        }
    }
    $left = ($points | Measure-Object -Property x -Minimum).Minimum
    $top = ($points | Measure-Object -Property y -Minimum).Minimum
    $right = ($points | Measure-Object -Property x -Maximum).Maximum
    $bottom = ($points | Measure-Object -Property y -Maximum).Maximum

    $coverage = ($cardWidth * $cardHeight) /
        ([double]$CanvasWidth * $CanvasHeight)
    $normalized = [Math]::Max(0.0, [Math]::Min(
        1.0, ($coverage - 0.30) / (0.75 - 0.30)))
    $shadowStrength = $normalized * $normalized *
        (3.0 - (2.0 * $normalized))
    $shadowVerticalOffset = 5.0 + ((14.0 - 5.0) * $shadowStrength)
    $shadowSoftness = 42.0 + ((34.0 - 42.0) * $shadowStrength)
    $shadowCorners = @(
        [pscustomobject]@{
            x = -$halfWidth - $shadowSoftness
            y = -$halfHeight + $shadowVerticalOffset - $shadowSoftness
        },
        [pscustomobject]@{
            x = $halfWidth + $shadowSoftness
            y = -$halfHeight + $shadowVerticalOffset - $shadowSoftness
        },
        [pscustomobject]@{
            x = -$halfWidth - $shadowSoftness
            y = $halfHeight + $shadowVerticalOffset + $shadowSoftness
        },
        [pscustomobject]@{
            x = $halfWidth + $shadowSoftness
            y = $halfHeight + $shadowVerticalOffset + $shadowSoftness
        }
    )
    $shadowPoints = @()
    for ($index = 0; $index -lt $shadowCorners.Count; $index++) {
        $localX = [double]$shadowCorners[$index].x
        $localYDown = [double]$shadowCorners[$index].y
        $x = $localX * $pose.scale
        $yUp = -$localYDown * $pose.scale
        $afterXDepth = $yUp * [Math]::Sin($rotationX)
        $rotatedX = ($x * [Math]::Cos($rotationY)) +
            ($afterXDepth * [Math]::Sin($rotationY))
        $rotatedYUp = $yUp * [Math]::Cos($rotationX)
        $rotatedDepth = (-$x * [Math]::Sin($rotationY)) +
            ($afterXDepth * [Math]::Cos($rotationY))
        $w = 1.0 - ($pose.perspectiveDepth * $rotatedDepth / $depthReference)
        $clipX = ($centerNdcX * $w) + (2.0 * $rotatedX / $CanvasWidth)
        $clipY = ($centerNdcY * $w) + (2.0 * $rotatedYUp / $CanvasHeight)
        $shadowPoints += [pscustomobject]@{
            corner = $names[$index]
            x = ((($clipX / $w) + 1.0) * 0.5 * $CanvasWidth)
            y = ((1.0 - ($clipY / $w)) * 0.5 * $CanvasHeight)
            homogeneousW = $w
        }
    }
    $shadowLeft = ($shadowPoints | Measure-Object -Property x -Minimum).Minimum
    $shadowTop = ($shadowPoints | Measure-Object -Property y -Minimum).Minimum
    $shadowRight = ($shadowPoints | Measure-Object -Property x -Maximum).Maximum
    $shadowBottom = ($shadowPoints | Measure-Object -Property y -Maximum).Maximum
    $contentFinite = @($left, $top, $right, $bottom) |
        Where-Object {
            [double]::IsNaN($_) -or [double]::IsInfinity($_)
        } |
        Measure-Object |
        Select-Object -ExpandProperty Count
    $contentFinite = $contentFinite -eq 0 -and
        $right -gt $left -and $bottom -gt $top
    $shadowFinite = @(
        $shadowLeft, $shadowTop, $shadowRight, $shadowBottom) |
        Where-Object {
            [double]::IsNaN($_) -or [double]::IsInfinity($_)
        } |
        Measure-Object |
        Select-Object -ExpandProperty Count
    $shadowFinite = $shadowFinite -eq 0 -and
        $shadowRight -gt $shadowLeft -and $shadowBottom -gt $shadowTop
    $contentInside = $contentFinite -and
        $left -ge -0.01 -and $top -ge -0.01 -and
        $right -le ($CanvasWidth + 0.01) -and
        $bottom -le ($CanvasHeight + 0.01)
    $shadowInside = $shadowFinite -and
        $shadowLeft -ge -0.01 -and $shadowTop -ge -0.01 -and
        $shadowRight -le ($CanvasWidth + 0.01) -and
        $shadowBottom -le ($CanvasHeight + 0.01)
    return [pscustomobject]@{
        layer4State = 'STAY'
        layer4StateEvidence =
            'Motion A enter=360ms; samples begin after 650ms; Return event remains unsignaled; frozen STAY assigns exact target each frame'
        pose = $pose
        flatCardBounds = [pscustomobject]@{
            left = $cardLeft; top = $cardTop
            right = $cardRight; bottom = $cardBottom
        }
        transformedQuad = $points
        transformedCardBounds = [pscustomobject]@{
            left = $left; top = $top; right = $right; bottom = $bottom
        }
        transformedShadowBounds = [pscustomobject]@{
            left = $shadowLeft; top = $shadowTop
            right = $shadowRight; bottom = $shadowBottom
        }
        contentBoundsFinite = $contentFinite
        contentInsideCanvas = $contentInside
        shadowSupportFinite = $shadowFinite
        shadowSupportInsideCanvas = $shadowInside
        shadowSupportOverscan = $shadowFinite -and -not $shadowInside
        compositionAcceptedByBoundsPolicy =
            $contentInside -and $shadowFinite
    }
}

function Save-WindowContextScreenshot(
    [IntPtr]$Window,
    [string]$Path) {
    $rect = New-Object WindowTargetAuditInterop+RECT
    Require ([WindowTargetAuditInterop]::GetWindowRect(
        $Window,
        [ref]$rect)) 'GetWindowRect failed before screenshot.'
    $margin = 12
    $left = $rect.Left - $margin
    $top = $rect.Top - $margin
    $width = ($rect.Right - $rect.Left) + (2 * $margin)
    $height = ($rect.Bottom - $rect.Top) + (2 * $margin)
    $bitmap = New-Object Drawing.Bitmap $width, $height
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.CopyFromScreen(
            $left,
            $top,
            0,
            0,
            (New-Object Drawing.Size $width, $height),
            [Drawing.CopyPixelOperation]::SourceCopy)
        $bitmap.Save($Path, [Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        $graphics.Dispose()
        $bitmap.Dispose()
    }
}

function Save-PhaseScreenshots(
    [string]$Phase,
    [IntPtr]$TargetWindow,
    [IntPtr]$PreviewWindow,
    [string]$Directory) {
    $targetPath = Join-Path $Directory ("$Phase-target.png")
    $previewPath = Join-Path $Directory ("$Phase-preview.png")
    $form.TopMost = $false
    [Windows.Forms.Application]::DoEvents()
    Save-WindowContextScreenshot $TargetWindow $targetPath
    $form.TopMost = $true
    $form.BringToFront()
    [Windows.Forms.Application]::DoEvents()
    Start-Sleep -Milliseconds 100
    Save-WindowContextScreenshot $PreviewWindow $previewPath
    $form.TopMost = $false
    [Windows.Forms.Application]::DoEvents()
    return [pscustomobject]@{
        target = $targetPath
        preview = $previewPath
    }
}

function Write-LifecycleSample(
    [string]$Phase,
    [int]$ElapsedMilliseconds,
    [IntPtr]$Window,
    $Stats,
    $PreviousStats,
    [uint32]$ExpectedProcessId,
    [string]$ExpectedClass) {
    $sampleProcessId = [uint32]0
    $null = [WindowTargetAuditInterop]::GetWindowThreadProcessId(
        $Window,
        [ref]$sampleProcessId)
    $sampleClass = New-Object Text.StringBuilder 512
    $null = [WindowTargetAuditInterop]::GetClassName(
        $Window,
        $sampleClass,
        $sampleClass.Capacity)
    $sampleRoot = [WindowTargetAuditInterop]::GetAncestor(
        $Window,
        [WindowTargetAuditInterop]::GaRoot)
    $sampleOwner = [WindowTargetAuditInterop]::GetWindow(
        $Window,
        [WindowTargetAuditInterop]::GwOwner)
    $frameDelta = if ($null -eq $PreviousStats) {
        $Stats.CaptureFrameCount
    }
    else {
        $Stats.CaptureFrameCount - $PreviousStats.CaptureFrameCount
    }
    $presentDelta = if ($null -eq $PreviousStats) {
        $Stats.PresentFrameCount
    }
    else {
        $Stats.PresentFrameCount - $PreviousStats.PresentFrameCount
    }
    [pscustomobject]@{
        record = 'lifecycle-sample'
        label = $Label
        phase = $Phase
        elapsedMilliseconds = $ElapsedMilliseconds
        hwnd = Format-Hwnd $Window
        isWindow = [WindowTargetAuditInterop]::IsWindow($Window)
        isVisible = [WindowTargetAuditInterop]::IsWindowVisible($Window)
        isIconic = [WindowTargetAuditInterop]::IsIconic($Window)
        root = Format-Hwnd $sampleRoot
        owner = Format-Hwnd $sampleOwner
        processId = $sampleProcessId
        windowClass = $sampleClass.ToString()
        targetIdentityUnchanged =
            $sampleRoot -eq $Window -and
            $sampleOwner -eq [IntPtr]::Zero -and
            $sampleProcessId -eq $ExpectedProcessId -and
            $sampleClass.ToString() -eq $ExpectedClass
        state = $Stats.State
        lastResult = $Stats.LastResult
        captureItemClosedProxy = $Stats.LastResult -eq -20
        captureFrames = $Stats.CaptureFrameCount
        captureFrameDelta = $frameDelta
        presentFrames = $Stats.PresentFrameCount
        presentFrameDelta = $presentDelta
        lastSystemRelativeTime100ns = $Stats.LastSystemRelativeTime100ns
        systemRelativeTimeAdvanced = $null -eq $PreviousStats -or
            $Stats.LastSystemRelativeTime100ns -gt
                $PreviousStats.LastSystemRelativeTime100ns
        lastFrameArrivalQpc = $Stats.LastFrameArrivalQpc
        frameArrivalQpcAdvanced = $null -eq $PreviousStats -or
            $Stats.LastFrameArrivalQpc -gt $PreviousStats.LastFrameArrivalQpc
        captureSize = '{0}x{1}' -f $Stats.CaptureWidth, $Stats.CaptureHeight
        framePoolRecreateCount = $Stats.FramePoolRecreateCount
    } | ConvertTo-Json -Compress
}

if ($Hwnd.StartsWith('0x', [StringComparison]::OrdinalIgnoreCase)) {
    $numericHwnd = [Convert]::ToUInt64($Hwnd.Substring(2), 16)
}
else {
    $numericHwnd = [Convert]::ToUInt64($Hwnd, 10)
}
$target = [IntPtr]([int64]$numericHwnd)

Require (Test-Path -LiteralPath $NativeDirectory -PathType Container) `
    "Native directory does not exist: $NativeDirectory"
Require ([WindowTargetAuditInterop]::SetDllDirectory($NativeDirectory)) `
    "SetDllDirectory failed: $NativeDirectory"
Require ([WindowTargetAuditInterop]::IsWindow($target)) 'Target is not a window.'
Require ([WindowTargetAuditInterop]::IsWindowVisible($target)) 'Target is not visible.'
Require ([WindowTargetAuditInterop]::GetAncestor(
    $target,
    [WindowTargetAuditInterop]::GaRoot) -eq $target) 'Target is not its own GA_ROOT.'

$titleLength = [WindowTargetAuditInterop]::GetWindowTextLength($target)
$title = New-Object Text.StringBuilder ([Math]::Max(1, $titleLength + 1))
$null = [WindowTargetAuditInterop]::GetWindowText($target, $title, $title.Capacity)
Require (-not [string]::IsNullOrWhiteSpace($title.ToString())) 'Target has a blank title.'
$cloakedAtSelection = [uint32]0
$cloakedResultAtSelection = [WindowTargetAuditInterop]::DwmGetWindowAttribute(
    $target,
    [WindowTargetAuditInterop]::DwmwaCloaked,
    [ref]$cloakedAtSelection,
    4)
Require ($cloakedResultAtSelection -ne 0 -or $cloakedAtSelection -eq 0) `
    'Target is DWM cloaked.'

$processId = [uint32]0
$null = [WindowTargetAuditInterop]::GetWindowThreadProcessId(
    $target,
    [ref]$processId)
$className = New-Object Text.StringBuilder 512
$null = [WindowTargetAuditInterop]::GetClassName(
    $target,
    $className,
    $className.Capacity)
$process = Get-Process -Id $processId

$placement = New-Object WindowTargetAuditInterop+WINDOWPLACEMENT
$placement.Length = [Runtime.InteropServices.Marshal]::SizeOf($placement)
Require ([WindowTargetAuditInterop]::GetWindowPlacement(
    $target,
    [ref]$placement)) 'GetWindowPlacement failed.'

$diagnosticLeaf = '{0}-{1}-{2}' -f
    ([DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss')),
    $Label,
    [Guid]::NewGuid().ToString('N')
$diagnosticDirectory = if ([string]::IsNullOrWhiteSpace($FinalMp4Path)) {
    Join-Path ([IO.Path]::GetTempPath()) (
        'xbpreview-window-target-resize-audit\' + $diagnosticLeaf)
}
else {
    # RecordingOutputRoot deliberately resolves four parents from this exact
    # Release layout: diagnostic-logs -> x64 -> Release -> bin -> artifacts.
    Join-Path $NativeDirectory 'diagnostic-logs'
}
$null = New-Item -ItemType Directory -Path $diagnosticDirectory -Force
$form = New-Object Windows.Forms.Form
$panel = New-Object Windows.Forms.Panel
$handle = [IntPtr]::Zero
$started = $false
$recordingStarted = $false
$recordingSnapshot = $null
$enterEventSignaled = $false
$preEnterIdentityMilliseconds = 0
$returnEventSignaled = $false
$diagnosticPointer = [IntPtr]::Zero
$motionPresetVariable = 'XB_PREVIEW_TEST_WINDOW_SHOWCASE_MOTION_PRESET'
$motionEnterVariable = 'XB_PREVIEW_TEST_WINDOW_SHOWCASE_MOTION_ENTER_EVENT'
$motionReturnVariable = 'XB_PREVIEW_TEST_WINDOW_SHOWCASE_MOTION_RETURN_EVENT'
$motionDirectionVariable = 'XB_PREVIEW_TEST_WINDOW_SHOWCASE_MOTION_DIRECTION'
$motionStrengthVariable = 'XB_PREVIEW_TEST_WINDOW_SHOWCASE_MOTION_STRENGTH'
$staticDirectionVariable = 'XB_PREVIEW_TEST_WINDOW_STAGE_25D_DIRECTION'
$staticStrengthVariable = 'XB_PREVIEW_TEST_WINDOW_STAGE_25D_STRENGTH'
$savedMotionPreset = [Environment]::GetEnvironmentVariable(
    $motionPresetVariable, [EnvironmentVariableTarget]::Process)
$savedMotionEnter = [Environment]::GetEnvironmentVariable(
    $motionEnterVariable, [EnvironmentVariableTarget]::Process)
$savedMotionReturn = [Environment]::GetEnvironmentVariable(
    $motionReturnVariable, [EnvironmentVariableTarget]::Process)
$savedMotionDirection = [Environment]::GetEnvironmentVariable(
    $motionDirectionVariable, [EnvironmentVariableTarget]::Process)
$savedMotionStrength = [Environment]::GetEnvironmentVariable(
    $motionStrengthVariable, [EnvironmentVariableTarget]::Process)
$savedStaticDirection = [Environment]::GetEnvironmentVariable(
    $staticDirectionVariable, [EnvironmentVariableTarget]::Process)
$savedStaticStrength = [Environment]::GetEnvironmentVariable(
    $staticStrengthVariable, [EnvironmentVariableTarget]::Process)
$enterEvent = $null
$returnEvent = $null

try {
    [Environment]::SetEnvironmentVariable(
        $staticDirectionVariable, $null, [EnvironmentVariableTarget]::Process)
    [Environment]::SetEnvironmentVariable(
        $staticStrengthVariable, $null, [EnvironmentVariableTarget]::Process)
    if ($PresentationMode -eq 'Persistent25D') {
        Require ($Mode -eq 'Resize') `
            'Persistent25D is supported only for the short Resize sequence.'
        $returnEventName =
            'Local\XbPreview.WindowTargetAudit.Return.' +
            [Guid]::NewGuid().ToString('N')
        $returnEvent = [System.Threading.EventWaitHandle]::new(
            $false,
            [System.Threading.EventResetMode]::ManualReset,
            $returnEventName)
        if (-not [string]::IsNullOrWhiteSpace($FinalMp4Path)) {
            $enterEventName =
                'Local\XbPreview.WindowTargetAudit.Enter.' +
                [Guid]::NewGuid().ToString('N')
            $enterEvent = [System.Threading.EventWaitHandle]::new(
                $false,
                [System.Threading.EventResetMode]::ManualReset,
                $enterEventName)
            [Environment]::SetEnvironmentVariable(
                $motionEnterVariable,
                $enterEventName,
                [EnvironmentVariableTarget]::Process)
        }
        else {
            [Environment]::SetEnvironmentVariable(
                $motionEnterVariable, $null, [EnvironmentVariableTarget]::Process)
        }
        [Environment]::SetEnvironmentVariable(
            $motionPresetVariable, 'A', [EnvironmentVariableTarget]::Process)
        [Environment]::SetEnvironmentVariable(
            $motionReturnVariable,
            $returnEventName,
            [EnvironmentVariableTarget]::Process)
        [Environment]::SetEnvironmentVariable(
            $motionDirectionVariable,
            $MotionDirection,
            [EnvironmentVariableTarget]::Process)
        [Environment]::SetEnvironmentVariable(
            $motionStrengthVariable,
            $MotionStrength,
            [EnvironmentVariableTarget]::Process)
    }
    else {
        [Environment]::SetEnvironmentVariable(
            $motionPresetVariable, $null, [EnvironmentVariableTarget]::Process)
        [Environment]::SetEnvironmentVariable(
            $motionEnterVariable, $null, [EnvironmentVariableTarget]::Process)
        [Environment]::SetEnvironmentVariable(
            $motionReturnVariable, $null, [EnvironmentVariableTarget]::Process)
        [Environment]::SetEnvironmentVariable(
            $motionDirectionVariable, $null, [EnvironmentVariableTarget]::Process)
        [Environment]::SetEnvironmentVariable(
            $motionStrengthVariable, $null, [EnvironmentVariableTarget]::Process)
    }

    $form.Text = "XbPreview resize audit - $Label"
    $form.StartPosition = [Windows.Forms.FormStartPosition]::Manual
    $form.Location = New-Object Drawing.Point 1280, 700
    $form.ClientSize = New-Object Drawing.Size 600, 338
    $panel.Dock = [Windows.Forms.DockStyle]::Fill
    $form.Controls.Add($panel)
    $form.Show()
    [Windows.Forms.Application]::DoEvents()

    $diagnosticPointer = [Runtime.InteropServices.Marshal]::StringToHGlobalUni(
        $diagnosticDirectory)
    $options = New-Object WindowTargetAuditInterop+CreateOptions
    $options.StructSize = 72
    $options.ApiVersion = [WindowTargetAuditInterop]::ApiVersion
    $options.ExclusionWindow = [uint64]$form.Handle.ToInt64()
    $options.AllowWarp = 1
    $options.FramePoolBufferCount = 2
    $options.StatsIntervalMilliseconds = 1000
    $options.DiagnosticLogDirectory = $diagnosticPointer
    $result = [WindowTargetAuditInterop]::XbPreview_Create(
        $panel.Handle,
        [ref]$options,
        [ref]$handle)
    Require ($result -eq 0) "XbPreview_Create failed: $result"

    $geometry = New-Object WindowTargetAuditInterop+SessionGeometry
    $geometry.StructSize = 56
    $geometry.Version = 1
    $geometry.SourceWidth = 1920
    $geometry.SourceHeight = 1080
    $geometry.CaptureWidth = 1920
    $geometry.CaptureHeight = 1080
    $geometry.OutputWidth = 1920
    $geometry.OutputHeight = 1080
    $geometry.GeometryRevision = 1
    $result = [WindowTargetAuditInterop]::XbPreview_SetSessionGeometry(
        $handle,
        [ref]$geometry)
    Require ($result -eq 0) "XbPreview_SetSessionGeometry failed: $result"
    $result = [WindowTargetAuditInterop]::XbPreview_SetCaptureTarget(
        $handle,
        1,
        $numericHwnd)
    Require ($result -eq 0) "XbPreview_SetCaptureTarget failed: $result"

    $null = [WindowTargetAuditInterop]::ShowWindowAsync(
        $target,
        [WindowTargetAuditInterop]::SwRestore)
    Wait-WindowState $target $false

    $workArea = [Windows.Forms.Screen]::FromHandle($target).WorkingArea
    $normalWidth = [Math]::Min(900, [Math]::Max(640, $workArea.Width - 120))
    $wideWidth = [Math]::Min(1400, [Math]::Max($normalWidth + 200, $workArea.Width - 80))
    $narrowWidth = [Math]::Min(700, $normalWidth - 100)
    $height = [Math]::Min(700, [Math]::Max(520, $workArea.Height - 140))
    $x = $workArea.Left + 40
    $y = $workArea.Top + 40

    Move-Target $target $x $y $normalWidth $height
    $result = [WindowTargetAuditInterop]::XbPreview_Start($handle)
    Require ($result -eq 0) "XbPreview_Start failed: $result"
    $started = $true
    if (-not [string]::IsNullOrWhiteSpace($FinalMp4Path)) {
        Require ($PresentationMode -eq 'Persistent25D' -and $Mode -eq 'Resize') `
            'Final MP4 capture requires Persistent25D Resize mode.'
        $result = [WindowTargetAuditInterop]::XbPreview_SetAudioProgramMode(
            $handle,
            0)
        Require ($result -eq 0) "XbPreview_SetAudioProgramMode(None) failed: $result"
        $result = [WindowTargetAuditInterop]::XbPreview_StartRecording($handle)
        Require ($result -eq 0) "XbPreview_StartRecording failed: $result"
        $recordingStarted = $true
    }

    if ($Mode -eq 'Lifecycle') {
        $initial = Wait-Capture $handle $null $false
        Write-LifecycleSample `
            'started' 0 $target $initial $null $processId $className.ToString()
        $previous = $initial
        $allSamplesAdvanced = $true
        $closedObserved = $initial.LastResult -eq -20
        $screenshots = @()
        $screenshotSeconds = @(1, [Math]::Floor($LifecycleSeconds / 2), $LifecycleSeconds) |
            Select-Object -Unique
        for ($second = 1; $second -le $LifecycleSeconds; $second++) {
            Start-Sleep -Seconds 1
            [Windows.Forms.Application]::DoEvents()
            $current = Read-Stats $handle
            Require-LiveProgress `
                "steady second $second" `
                $current `
                $previous `
                $false
            Write-LifecycleSample `
                'steady' ($second * 1000) $target $current $previous `
                $processId $className.ToString()
            $advanced =
                $current.CaptureFrameCount -gt $previous.CaptureFrameCount -and
                $current.LastSystemRelativeTime100ns -gt
                    $previous.LastSystemRelativeTime100ns -and
                $current.LastFrameArrivalQpc -gt $previous.LastFrameArrivalQpc
            if (-not $advanced) {
                $allSamplesAdvanced = $false
            }
            if ($current.LastResult -eq -20) {
                $closedObserved = $true
            }
            if ($second -in $screenshotSeconds) {
                $screenshotPath = Join-Path $diagnosticDirectory (
                    'screen-{0:D2}s.png' -f $second)
                Save-WindowContextScreenshot $target $screenshotPath
                $screenshots += $screenshotPath
            }
            $previous = $current
        }

        Move-Target $target $x $y $wideWidth $height
        $afterResize = Wait-Capture $handle $previous $true
        Write-LifecycleSample `
            'after-resize' (($LifecycleSeconds + 1) * 1000) `
            $target $afterResize $previous $processId $className.ToString()
        if ($afterResize.LastResult -eq -20) {
            $closedObserved = $true
        }
        Require $allSamplesAdvanced `
            'Identity lifecycle control stopped advancing.'
        Require (-not $closedObserved) `
            'Identity lifecycle control observed CaptureItem.Closed proxy.'
        $lifecycleSummary = [pscustomobject]@{
            record = 'lifecycle-summary'
            label = $Label
            hwnd = Format-Hwnd $target
            seconds = $LifecycleSeconds
            startCaptureFrames = $initial.CaptureFrameCount
            endCaptureFrames = $afterResize.CaptureFrameCount
            startPresentFrames = $initial.PresentFrameCount
            endPresentFrames = $afterResize.PresentFrameCount
            startSystemRelativeTime100ns = $initial.LastSystemRelativeTime100ns
            endSystemRelativeTime100ns = $afterResize.LastSystemRelativeTime100ns
            startFrameArrivalQpc = $initial.LastFrameArrivalQpc
            endFrameArrivalQpc = $afterResize.LastFrameArrivalQpc
            allSteadySamplesAdvanced = $allSamplesAdvanced
            captureItemClosedProxyObserved = $closedObserved
            finalState = $afterResize.State
            finalLastResult = $afterResize.LastResult
            finalFramePoolRecreateCount = $afterResize.FramePoolRecreateCount
            screenshots = $screenshots
        }
        $lifecycleSummary | ConvertTo-Json -Compress
    }
    else {
        $normal = Wait-Capture $handle $null $false
        if ($PresentationMode -eq 'Persistent25D') {
            if ($recordingStarted -and $null -ne $enterEvent) {
                $identityStart = $normal
                $identityTimer = [Diagnostics.Stopwatch]::StartNew()
                while ($identityTimer.ElapsedMilliseconds -lt 2000) {
                    Start-Sleep -Milliseconds 50
                    [Windows.Forms.Application]::DoEvents()
                    $identitySample = Read-Stats $handle
                    Require ($identitySample.State -eq 2 -and
                        $identitySample.LastResult -eq 0) (
                        'Pre-Enter Identity hold failed: elapsed={0}ms, ' +
                        'state={1}, result={2}' -f
                            $identityTimer.ElapsedMilliseconds,
                            $identitySample.State,
                            $identitySample.LastResult)
                }
                Require-LiveProgress `
                    'recorded pre-Enter Identity hold' `
                    $identitySample `
                    $identityStart `
                    $false
                $preEnterIdentityMilliseconds =
                    $identityTimer.ElapsedMilliseconds
                $null = $enterEvent.Set()
                $enterEventSignaled = $true
                $normal = $identitySample
            }
            $transitionStart = $normal
            $transitionTimer = [Diagnostics.Stopwatch]::StartNew()
            while ($transitionTimer.ElapsedMilliseconds -lt 650) {
                Start-Sleep -Milliseconds 25
                [Windows.Forms.Application]::DoEvents()
                $transitionSample = Read-Stats $handle
                Require ($transitionSample.State -eq 2 -and
                    $transitionSample.LastResult -eq 0) (
                    'Motion A failed before exact STAY: elapsed={0}ms, ' +
                    'state={1}, result={2}' -f
                        $transitionTimer.ElapsedMilliseconds,
                        $transitionSample.State,
                        $transitionSample.LastResult)
            }
            Require-LiveProgress `
                'Identity -> 360ms Transition -> exact persistent STAY' `
                $transitionSample `
                $transitionStart `
                $false
            $normal = $transitionSample
        }
        Require-LiveProgress 'normal' $normal $null $false
        $normalShots = Save-PhaseScreenshots `
            'normal' $target $form.Handle $diagnosticDirectory
        Write-Snapshot `
            'normal' $target $normal $null `
            $normalShots.target $normalShots.preview

        Move-Target $target $x $y $wideWidth $height
        $wide = Wait-Capture $handle $normal $true
        Hold-Phase 350 2000
        $wide = Read-Stats $handle
        Require-LiveProgress 'wider' $wide $normal $true
        $wideShots = Save-PhaseScreenshots `
            'wider' $target $form.Handle $diagnosticDirectory
        Write-Snapshot `
            'wider' $target $wide $normal `
            $wideShots.target $wideShots.preview

        Move-Target $target $x $y $narrowWidth $height
        $narrow = Wait-Capture $handle $wide $true
        Hold-Phase 350 2000
        $narrow = Read-Stats $handle
        Require-LiveProgress 'narrower' $narrow $wide $true
        $narrowShots = Save-PhaseScreenshots `
            'narrower' $target $form.Handle $diagnosticDirectory
        Write-Snapshot `
            'narrower' $target $narrow $wide `
            $narrowShots.target $narrowShots.preview

        $null = [WindowTargetAuditInterop]::ShowWindowAsync(
            $target,
            [WindowTargetAuditInterop]::SwMaximize)
        Wait-WindowState $target $true
        $maximized = Wait-Capture $handle $narrow $true
        Hold-Phase 350 2000
        $maximized = Read-Stats $handle
        Require-LiveProgress 'maximized' $maximized $narrow $true
        $maximizedShots = Save-PhaseScreenshots `
            'maximized' $target $form.Handle $diagnosticDirectory
        Write-Snapshot `
            'maximized' $target $maximized $narrow `
            $maximizedShots.target $maximizedShots.preview

        $null = [WindowTargetAuditInterop]::ShowWindowAsync(
            $target,
            [WindowTargetAuditInterop]::SwRestore)
        Wait-WindowState $target $false
        $restored = Wait-Capture $handle $maximized $true
        Hold-Phase 350 4000
        $restored = Read-Stats $handle
        Require-LiveProgress 'restored' $restored $maximized $true
        $restoredShots = Save-PhaseScreenshots `
            'restored' $target $form.Handle $diagnosticDirectory
        Write-Snapshot `
            'restored' $target $restored $maximized `
            $restoredShots.target $restoredShots.preview

        Start-Sleep -Milliseconds 500
        [Windows.Forms.Application]::DoEvents()
        $postRestore = Read-Stats $handle
        Require-LiveProgress `
            'post-restore persistent liveness' `
            $postRestore `
            $restored `
            $false
        Write-LifecycleSample `
            'post-restore-liveness' 500 $target $postRestore $restored `
            $processId $className.ToString()

        if ($recordingStarted) {
            $null = $returnEvent.Set()
            $returnEventSignaled = $true
            Hold-Phase 500 2380
            $afterReturn = Read-Stats $handle
            Require-LiveProgress `
                'explicit 380ms Return then 2s Identity hold' `
                $afterReturn `
                $postRestore `
                $false

            $result = [WindowTargetAuditInterop]::XbPreview_StopRecording($handle)
            $recordingStarted = $false
            Require ($result -eq 0) "XbPreview_StopRecording failed: $result"
            $recordingSnapshot = Read-RecordingSnapshot $handle
            Require ($recordingSnapshot.State -eq 4) (
                'Recording did not complete: state={0}, result={1}, error={2}' -f
                    $recordingSnapshot.State,
                    $recordingSnapshot.LastResult,
                    $recordingSnapshot.ErrorMessage)
            Require ($recordingSnapshot.OutputSuccess -eq 1 -and
                $recordingSnapshot.FinalizeAttempted -eq 1 -and
                $recordingSnapshot.FinalizeCount -eq 1 -and
                $recordingSnapshot.ActiveEncoder -eq 0 -and
                $recordingSnapshot.ResidualOutstanding -eq 0 -and
                $recordingSnapshot.ValidationAttempted -eq 1 -and
                $recordingSnapshot.ValidationHResult -eq 0 -and
                $recordingSnapshot.Published -eq 1 -and
                $recordingSnapshot.PublishHResult -eq 0) `
                'Recording finalization, validation, publish, or teardown evidence failed.'
            Require (Test-Path -LiteralPath $recordingSnapshot.PublishedPath -PathType Leaf) `
                "Published MP4 is missing: $($recordingSnapshot.PublishedPath)"
            $finalDirectory = Split-Path -Parent $FinalMp4Path
            $null = New-Item -ItemType Directory -Path $finalDirectory -Force
            Copy-Item -LiteralPath $recordingSnapshot.PublishedPath `
                -Destination $FinalMp4Path -Force
            Require (Test-Path -LiteralPath $FinalMp4Path -PathType Leaf) `
                "Final MP4 copy is missing: $FinalMp4Path"
        }
    }

    $result = [WindowTargetAuditInterop]::XbPreview_Stop($handle)
    $started = $false
    Require ($result -eq 0) "XbPreview_Stop failed: $result"

    [pscustomobject]@{
        record = 'summary'
        label = $Label
        hwnd = Format-Hwnd $target
        process = $process.ProcessName
        executable = $process.Path
        processId = $processId
        windowClass = $className.ToString()
        owner = Format-Hwnd ([WindowTargetAuditInterop]::GetWindow(
            $target,
            [WindowTargetAuditInterop]::GwOwner))
        root = Format-Hwnd ([WindowTargetAuditInterop]::GetAncestor(
            $target,
            [WindowTargetAuditInterop]::GaRoot))
        style = '0x{0:X}' -f [uint64]([WindowTargetAuditInterop]::GetWindowLongPtr(
            $target,
            -16).ToInt64())
        exStyle = '0x{0:X}' -f [uint64]([WindowTargetAuditInterop]::GetWindowLongPtr(
            $target,
            -20).ToInt64())
        selectorEligible = $true
        createForWindow = 'success'
        mode = $Mode
        presentationMode = $PresentationMode
        motionDirection = $MotionDirection
        motionStrength = $MotionStrength
        gateResult = 'PASS'
        enterEventSignaled = $enterEventSignaled
        preEnterIdentityMilliseconds = $preEnterIdentityMilliseconds
        returnEventSignaled = $returnEventSignaled
        finalMp4Path = $FinalMp4Path
        recordingState = if ($null -eq $recordingSnapshot) {
            'not-requested'
        }
        else {
            $recordingSnapshot.State
        }
        framesSubmitted = if ($null -eq $recordingSnapshot) {
            0
        }
        else {
            $recordingSnapshot.FramesSubmitted
        }
        phases = if ($Mode -eq 'Resize') { 5 } else { $LifecycleSeconds + 2 }
        diagnosticDirectory = $diagnosticDirectory
    } | ConvertTo-Json -Compress
}
finally {
    if ($recordingStarted -and $handle -ne [IntPtr]::Zero) {
        $null = [WindowTargetAuditInterop]::XbPreview_StopRecording($handle)
    }
    if ($started -and $handle -ne [IntPtr]::Zero) {
        $null = [WindowTargetAuditInterop]::XbPreview_Stop($handle)
    }
    if ($handle -ne [IntPtr]::Zero) {
        $null = [WindowTargetAuditInterop]::XbPreview_Destroy([ref]$handle)
    }
    if ($diagnosticPointer -ne [IntPtr]::Zero) {
        [Runtime.InteropServices.Marshal]::FreeHGlobal($diagnosticPointer)
    }
    if ($null -ne $enterEvent) {
        $enterEvent.Dispose()
    }
    if ($null -ne $returnEvent) {
        $returnEvent.Dispose()
    }
    [Environment]::SetEnvironmentVariable(
        $motionPresetVariable,
        $savedMotionPreset,
        [EnvironmentVariableTarget]::Process)
    [Environment]::SetEnvironmentVariable(
        $motionEnterVariable,
        $savedMotionEnter,
        [EnvironmentVariableTarget]::Process)
    [Environment]::SetEnvironmentVariable(
        $motionReturnVariable,
        $savedMotionReturn,
        [EnvironmentVariableTarget]::Process)
    [Environment]::SetEnvironmentVariable(
        $motionDirectionVariable,
        $savedMotionDirection,
        [EnvironmentVariableTarget]::Process)
    [Environment]::SetEnvironmentVariable(
        $motionStrengthVariable,
        $savedMotionStrength,
        [EnvironmentVariableTarget]::Process)
    [Environment]::SetEnvironmentVariable(
        $staticDirectionVariable,
        $savedStaticDirection,
        [EnvironmentVariableTarget]::Process)
    [Environment]::SetEnvironmentVariable(
        $staticStrengthVariable,
        $savedStaticStrength,
        [EnvironmentVariableTarget]::Process)
    $panel.Dispose()
    $form.Dispose()
    $null = [WindowTargetAuditInterop]::SetWindowPlacement(
        $target,
        [ref]$placement)
}
