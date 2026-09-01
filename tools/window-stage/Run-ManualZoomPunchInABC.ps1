[CmdletBinding()]
param(
    [string]$OutputDirectory = '',
    [switch]$BThreeDirection,
    [switch]$SystemAudioOnRight
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$binaryRoot = Join-Path $repoRoot 'artifacts\bin\Release\x64'
$hostExe = Join-Path $binaryRoot 'XbPreview.Host.exe'
$diagnosticRoot = Join-Path $binaryRoot 'diagnostic-logs'
$recordingRoot = Join-Path $repoRoot 'artifacts\p2.5a-recordings'
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $relativeOutput = if ($BThreeDirection) {
        'artifacts\manual-zoom-punch-in-b-3direction'
    }
    else {
        'artifacts\manual-zoom-punch-in-abc'
    }
    $OutputDirectory = Join-Path $repoRoot $relativeOutput
}
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)

function Require([bool]$Condition, [string]$Message) {
    if (-not $Condition) {
        throw "MANUAL-ZOOM-PUNCH-IN-ABC FAIL: $Message"
    }
}

Require (-not $SystemAudioOnRight -or $BThreeDirection) `
    '-SystemAudioOnRight requires -BThreeDirection'

function Stop-TestProcess([Diagnostics.Process]$Process) {
    if ($null -eq $Process -or $Process.HasExited) {
        return
    }
    $null = $Process.CloseMainWindow()
    if (-not $Process.WaitForExit(15000)) {
        $Process.Kill()
        [void]$Process.WaitForExit(5000)
    }
}

function Wait-Until(
    [scriptblock]$Probe,
    [scriptblock]$Accept,
    [string]$Description,
    [int]$TimeoutSeconds = 20) {
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    do {
        $value = & $Probe
        if ($null -ne $value -and (& $Accept $value)) {
            return $value
        }
        Start-Sleep -Milliseconds 200
    } while ((Get-Date) -lt $deadline)
    throw "MANUAL-ZOOM-PUNCH-IN-ABC FAIL: timed out waiting for $Description."
}

function Get-LastSummary([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        return $null
    }
    $lines = @(Get-Content -LiteralPath $Path -Tail 120)
    for ($index = $lines.Count - 1; $index -ge 0; $index--) {
        try {
            $value = $lines[$index] | ConvertFrom-Json -ErrorAction Stop
            if ($value.type -eq 'summary') {
                return $value
            }
        }
        catch {
            # The native logger may be appending its final line while sampled.
        }
    }
    return $null
}

function Get-Controls(
    [System.Windows.Automation.AutomationElement]$Root,
    [System.Windows.Automation.ControlType]$Type) {
    $condition = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        $Type)
    return $Root.FindAll(
        [System.Windows.Automation.TreeScope]::Descendants,
        $condition)
}

function Find-ControlByName(
    [System.Windows.Automation.AutomationElement]$Root,
    [System.Windows.Automation.ControlType]$Type,
    [string]$Name,
    [switch]$StartsWith) {
    foreach ($control in @(Get-Controls $Root $Type)) {
        $candidate = $control.Current.Name
        if ((!$StartsWith -and $candidate -ceq $Name) -or
            ($StartsWith -and $candidate.StartsWith(
                $Name, [StringComparison]::Ordinal))) {
            return $control
        }
    }
    return $null
}

function Invoke-Button(
    [System.Windows.Automation.AutomationElement]$Root,
    [string]$Name) {
    $button = Wait-Until `
        { Find-ControlByName $Root `
            ([System.Windows.Automation.ControlType]::Button) $Name } `
        { param($value) $null -ne $value -and $value.Current.IsEnabled } `
        "enabled button '$Name'"
    $pattern = $button.GetCurrentPattern(
        [System.Windows.Automation.InvokePattern]::Pattern)
    $pattern.Invoke()
}

function Set-ToggleState(
    [System.Windows.Automation.AutomationElement]$Root,
    [string]$NamePrefix,
    [bool]$Enabled) {
    $desiredState = if ($Enabled) {
        [System.Windows.Automation.ToggleState]::On
    }
    else {
        [System.Windows.Automation.ToggleState]::Off
    }
    $toggle = Wait-Until `
        { Find-ControlByName $Root `
            ([System.Windows.Automation.ControlType]::CheckBox) `
            $NamePrefix -StartsWith } `
        { param($value) $null -ne $value } `
        "toggle '$NamePrefix'"
    $pattern = $toggle.GetCurrentPattern(
        [System.Windows.Automation.TogglePattern]::Pattern)
    if ($pattern.Current.ToggleState -ne $desiredState) {
        $pattern.Toggle()
    }
    [void](Wait-Until `
        {
            $current = Find-ControlByName $Root `
                ([System.Windows.Automation.ControlType]::CheckBox) `
                $NamePrefix -StartsWith
            if ($null -eq $current) { return $null }
            $currentPattern = $current.GetCurrentPattern(
                [System.Windows.Automation.TogglePattern]::Pattern)
            return $currentPattern.Current.ToggleState
        } `
        { param($value) $value -eq $desiredState } `
        "toggle '$NamePrefix' state $desiredState")
}

function Select-NativeComboBoxIndex(
    [System.Windows.Automation.AutomationElement]$Combo,
    [int]$Index) {
    $handle = [IntPtr]$Combo.Current.NativeWindowHandle
    Require ($handle -ne [IntPtr]::Zero) 'ComboBox has no native HWND'
    $selection = [PunchGateNative]::SendMessage(
        $handle, 0x014E, [IntPtr]$Index, [IntPtr]::Zero)
    Require ($selection.ToInt64() -ne -1) "CB_SETCURSEL rejected $Index"
    $parent = [PunchGateNative]::GetParent($handle)
    $controlId = [PunchGateNative]::GetDlgCtrlID($handle)
    $selectionChanged = [IntPtr](
        ($controlId -band 0xFFFF) -bor (1 -shl 16))
    [void][PunchGateNative]::SendMessage(
        $parent, 0x0111, $selectionChanged, $handle)
}

function Find-NativeComboBoxItemIndex(
    [System.Windows.Automation.AutomationElement]$Combo,
    [string]$Token) {
    $handle = [IntPtr]$Combo.Current.NativeWindowHandle
    $count = [PunchGateNative]::SendMessage(
        $handle, 0x0146, [IntPtr]::Zero, [IntPtr]::Zero).ToInt32()
    $matches = @()
    for ($index = 0; $index -lt $count; $index++) {
        $length = [PunchGateNative]::SendMessage(
            $handle, 0x0149, [IntPtr]$index, [IntPtr]::Zero).ToInt32()
        if ($length -lt 0) { continue }
        $text = [Text.StringBuilder]::new($length + 1)
        [void][PunchGateNative]::SendMessageText(
            $handle, 0x0148, [IntPtr]$index, $text)
        if ($text.ToString().Contains($Token)) { $matches += $index }
    }
    Require ($matches.Count -eq 1) `
        "expected one target item containing '$Token'; found $($matches.Count)"
    return $matches[0]
}

function Send-ManualHotkey([ValidateSet('F9', 'F10')][string]$Key) {
    $virtualKey = if ($Key -eq 'F9') { 0x78 } else { 0x79 }
    [PunchGateNative]::keybd_event([byte]$virtualKey, 0, 0, [UIntPtr]::Zero)
    [PunchGateNative]::keybd_event([byte]$virtualKey, 0, 2, [UIntPtr]::Zero)
}

function Wait-CameraZoom([string]$P0Path, [double]$Zoom) {
    return Wait-Until `
        { Get-LastSummary $P0Path } `
        { param($value)
            $value.state -eq 2 -and $value.deviceRemovedReason -eq 0 -and
            [Math]::Abs([double]$value.nativeAppliedZoom - $Zoom) -le 0.005 } `
        "native camera zoom $Zoom" 15
}

function Wait-LiveAfter(
    [string]$P0Path,
    [UInt64]$CaptureFrames,
    [int]$ExpectedWidth = 0,
    [int]$ExpectedHeight = 0) {
    return Wait-Until `
        { Get-LastSummary $P0Path } `
        { param($value)
            $sizeMatches = $ExpectedWidth -eq 0 -or
                ([Math]::Abs([int]$value.captureWidth - $ExpectedWidth) -le 3 -and
                 [Math]::Abs([int]$value.captureHeight - $ExpectedHeight) -le 3)
            $value.state -eq 2 -and $value.deviceRemovedReason -eq 0 -and
            [UInt64]$value.captureFrameCount -gt $CaptureFrames -and $sizeMatches } `
        'live advancing capture after target state change' 20
}

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type @'
using System;
using System.Runtime.InteropServices;
using System.Text;
public static class PunchGateNative
{
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")]
    public static extern bool MoveWindow(
        IntPtr hWnd, int x, int y, int width, int height, bool repaint);
    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")]
    public static extern bool ShowWindowAsync(IntPtr hWnd, int command);
    [DllImport("user32.dll")]
    public static extern bool IsZoomed(IntPtr hWnd);
    [DllImport("user32.dll")]
    public static extern IntPtr SendMessage(
        IntPtr hWnd, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", EntryPoint = "SendMessageW", CharSet = CharSet.Unicode)]
    public static extern IntPtr SendMessageText(
        IntPtr hWnd, uint message, IntPtr wParam, StringBuilder text);
    [DllImport("user32.dll")]
    public static extern IntPtr GetParent(IntPtr hWnd);
    [DllImport("user32.dll")]
    public static extern int GetDlgCtrlID(IntPtr hWnd);
    [DllImport("user32.dll")]
    public static extern void keybd_event(
        byte virtualKey, byte scanCode, uint flags, UIntPtr extraInfo);
}
'@

function Start-PunchHost(
    [string]$Candidate,
    [string]$TitleToken,
    [System.Threading.EventWaitHandle]$EnterEvent,
    [System.Threading.EventWaitHandle]$ReturnEvent,
    [ValidateSet('RIGHT', 'FRONT', 'LEFT')]
    [string]$Direction = 'RIGHT') {
    $variables = @{
        XB_PREVIEW_TEST_WINDOW_SHOWCASE_MOTION_PRESET = 'A'
        XB_PREVIEW_TEST_WINDOW_SHOWCASE_MOTION_ENTER_EVENT = $EnterEvent.SafeWaitHandle.DangerousGetHandle()
        XB_PREVIEW_TEST_WINDOW_SHOWCASE_MOTION_RETURN_EVENT = $ReturnEvent.SafeWaitHandle.DangerousGetHandle()
        XB_PREVIEW_TEST_WINDOW_SHOWCASE_MOTION_DIRECTION = $Direction
        XB_PREVIEW_TEST_WINDOW_SHOWCASE_MOTION_STRENGTH = 'LEVEL_2'
        XB_PREVIEW_TEST_WINDOW_STAGE_MANUAL_PUNCH_CANDIDATE = $Candidate
    }
    # Event names, not handles, are exported below by the caller.
    foreach ($name in $variables.Keys) {
        if ($name -notmatch '_EVENT$') {
            [Environment]::SetEnvironmentVariable(
                $name, [string]$variables[$name], 'Process')
        }
    }

    $beforeP0 = @(Get-ChildItem -LiteralPath $diagnosticRoot `
        -Filter 'p0-*.jsonl' -File -ErrorAction SilentlyContinue |
        ForEach-Object FullName)
    $process = Start-Process -FilePath $hostExe `
        -WorkingDirectory $binaryRoot -PassThru
    try {
        $process = Wait-Until `
            { $process.Refresh(); if (-not $process.HasExited -and
                    $process.MainWindowHandle -ne [IntPtr]::Zero) { $process } } `
            { param($value) $null -ne $value } `
            'the Release Host window'
        $root = [System.Windows.Automation.AutomationElement]::FromHandle(
            [IntPtr]$process.MainWindowHandle)

        [void](Wait-Until `
            { Get-ChildItem -LiteralPath $diagnosticRoot -Filter 'p0-*.jsonl' -File |
                Where-Object FullName -notin $beforeP0 |
                Sort-Object LastWriteTime -Descending | Where-Object {
                    $summary = Get-LastSummary $_.FullName
                    $null -ne $summary -and $summary.state -eq 2 -and
                    $summary.captureFrameCount -gt 10 -and
                    $summary.presentFrameCount -gt 10
                } | Select-Object -First 1 } `
            { param($value) $null -ne $value } `
            'initial monitor preview before target selection')

    $combos = Wait-Until `
        { Get-Controls $root ([System.Windows.Automation.ControlType]::ComboBox) } `
        { param($value) $value.Count -ge 2 } `
        'capture range selector'
    Select-NativeComboBoxIndex $combos.Item(0) 1
    $combos = Wait-Until `
        { Get-Controls $root ([System.Windows.Automation.ControlType]::ComboBox) } `
        { param($value) $value.Count -ge 3 } `
        'target window selector'
    $targetIndex = Find-NativeComboBoxItemIndex $combos.Item(1) $TitleToken
    $beforeTargetP0 = @(Get-ChildItem -LiteralPath $diagnosticRoot `
        -Filter 'p0-*.jsonl' -File -ErrorAction SilentlyContinue |
        ForEach-Object FullName)
    Select-NativeComboBoxIndex $combos.Item(1) $targetIndex

        $p0 = Wait-Until `
        { Get-ChildItem -LiteralPath $diagnosticRoot -Filter 'p0-*.jsonl' -File |
            Where-Object FullName -notin $beforeTargetP0 |
            Sort-Object LastWriteTime -Descending | Where-Object {
                $summary = Get-LastSummary $_.FullName
                $null -ne $summary -and $summary.state -eq 2 -and
                $summary.captureFrameCount -gt 10 -and
                $summary.presentFrameCount -gt 10
            } | Select-Object -First 1 } `
        { param($value) $null -ne $value -and
            $null -ne (Get-LastSummary $value.FullName) } `
        'selected-window native diagnostic log'
    [void](Wait-Until `
        { Get-LastSummary $p0.FullName } `
        { param($value) $value.state -eq 2 -and
            $value.captureFrameCount -gt 10 -and
            $value.presentFrameCount -gt 10 } `
        'selected-window live preview')
    Invoke-Button $root '启用镜头快捷键'
        [pscustomobject]@{
            Process = $process
            Root = $root
            P0Path = $p0.FullName
        }
    }
    catch {
        Stop-TestProcess $process
        throw
    }
}

function Invoke-DirectionalResizeSmoke(
    [string]$P0Path,
    [IntPtr]$TargetHandle,
    [object]$Initial,
    [string]$Direction) {
    $samples = [Collections.Generic.List[object]]::new()
    $previous = $Initial
    foreach ($size in @(
        [pscustomobject]@{
            Name = 'larger'; Width = 1180; Height = 760
            CaptureWidth = 1166; CaptureHeight = 753
        },
        [pscustomobject]@{
            Name = 'smaller'; Width = 820; Height = 600
            CaptureWidth = 806; CaptureHeight = 593
        })) {
        Require ([PunchGateNative]::MoveWindow(
            $TargetHandle, 100, 80, $size.Width, $size.Height, $true)) `
            "$Direction could not resize $($size.Name)"
        $sample = Wait-LiveAfter `
            $P0Path ([UInt64]$previous.captureFrameCount) `
            $size.CaptureWidth $size.CaptureHeight
        Require (
            [Math]::Abs([double]$sample.nativeAppliedZoom - 1.6) -le 0.005 -and
            $sample.deviceRemovedReason -eq 0) `
            "$Direction $($size.Name) lost B Punch or device health"
        $samples.Add([pscustomobject]@{
            Phase = $size.Name
            CaptureFrames = $sample.captureFrameCount
            PresentFrames = $sample.presentFrameCount
            CaptureSize = "$($sample.captureWidth)x$($sample.captureHeight)"
            Zoom = $sample.nativeAppliedZoom
            DeviceRemovedReason = $sample.deviceRemovedReason
        })
        $previous = $sample
        Start-Sleep -Milliseconds 500
    }

    [void][PunchGateNative]::ShowWindowAsync($TargetHandle, 3)
    [void](Wait-Until `
        { [PunchGateNative]::IsZoomed($TargetHandle) } `
        { param($value) [bool]$value } `
        "$Direction target maximize state")
    $maximized = Wait-LiveAfter `
        $P0Path ([UInt64]$previous.captureFrameCount)
    Require (
        [PunchGateNative]::IsZoomed($TargetHandle) -and
        $maximized.captureWidth -gt $previous.captureWidth -and
        $maximized.captureHeight -gt $previous.captureHeight -and
        [Math]::Abs([double]$maximized.nativeAppliedZoom - 1.6) -le 0.005 -and
        $maximized.deviceRemovedReason -eq 0) `
        "$Direction maximize did not produce larger live capture geometry"
    $samples.Add([pscustomobject]@{
        Phase = 'maximize'
        CaptureFrames = $maximized.captureFrameCount
        PresentFrames = $maximized.presentFrameCount
        CaptureSize = "$($maximized.captureWidth)x$($maximized.captureHeight)"
        Zoom = $maximized.nativeAppliedZoom
        DeviceRemovedReason = $maximized.deviceRemovedReason
    })
    Start-Sleep -Milliseconds 500

    [void][PunchGateNative]::ShowWindowAsync($TargetHandle, 9)
    [void](Wait-Until `
        { -not [PunchGateNative]::IsZoomed($TargetHandle) } `
        { param($value) [bool]$value } `
        "$Direction target restore state")
    Require ([PunchGateNative]::MoveWindow(
        $TargetHandle, 100, 80, 1000, 700, $true)) `
        "$Direction could not restore shared target bounds"
    $restored = Wait-LiveAfter `
        $P0Path ([UInt64]$maximized.captureFrameCount) 986 693
    Require (
        -not [PunchGateNative]::IsZoomed($TargetHandle) -and
        [Math]::Abs([double]$restored.nativeAppliedZoom - 1.6) -le 0.005 -and
        $restored.deviceRemovedReason -eq 0) `
        "$Direction restore lost normal state, B Punch, or device health"
    $samples.Add([pscustomobject]@{
        Phase = 'restore'
        CaptureFrames = $restored.captureFrameCount
        PresentFrames = $restored.presentFrameCount
        CaptureSize = "$($restored.captureWidth)x$($restored.captureHeight)"
        Zoom = $restored.nativeAppliedZoom
        DeviceRemovedReason = $restored.deviceRemovedReason
    })
    Start-Sleep -Milliseconds 500
    return $samples
}

function Invoke-RecordedCandidate(
    [string]$Candidate,
    [string]$FileName,
    [string]$TitleToken,
    [ValidateSet('RIGHT', 'FRONT', 'LEFT')]
    [string]$Direction = 'RIGHT',
    [IntPtr]$TargetHandle = [IntPtr]::Zero,
    [switch]$IncludeResizeSmoke,
    [switch]$SystemAudio,
    [string]$TonePath = '') {
    $eventId = [Guid]::NewGuid().ToString('N')
    $enterName = "Local\XbPreview.Punch.Enter.$eventId"
    $returnName = "Local\XbPreview.Punch.Return.$eventId"
    $enterEvent = [Threading.EventWaitHandle]::new(
        $false, [Threading.EventResetMode]::ManualReset, $enterName)
    $returnEvent = [Threading.EventWaitHandle]::new(
        $false, [Threading.EventResetMode]::ManualReset, $returnName)
    [Environment]::SetEnvironmentVariable(
        'XB_PREVIEW_TEST_WINDOW_SHOWCASE_MOTION_ENTER_EVENT', $enterName, 'Process')
    [Environment]::SetEnvironmentVariable(
        'XB_PREVIEW_TEST_WINDOW_SHOWCASE_MOTION_RETURN_EVENT', $returnName, 'Process')
    $session = $null
    $audioPlayer = $null
    try {
        $session = Start-PunchHost `
            $Candidate $TitleToken $enterEvent $returnEvent $Direction
        Set-ToggleState $session.Root '电脑声音' ([bool]$SystemAudio)
        Set-ToggleState $session.Root '麦克风' $false
        if ($SystemAudio) {
            Require (-not [string]::IsNullOrWhiteSpace($TonePath) -and
                (Test-Path -LiteralPath $TonePath -PathType Leaf)) `
                "candidate $Candidate system-audio tone is missing"
            $audioPlayer = [System.Media.SoundPlayer]::new($TonePath)
            $audioPlayer.Load()
            $audioPlayer.PlayLooping()
            Start-Sleep -Milliseconds 500
        }
        $beforeMedia = @(Get-ChildItem -LiteralPath $recordingRoot `
            -Filter '*.mp4' -File -Recurse -ErrorAction SilentlyContinue |
            ForEach-Object FullName)
        Invoke-Button $session.Root '开始录制'
        [void](Wait-Until `
            { Get-ChildItem -LiteralPath $recordingRoot -Filter '*.partial.mp4' `
                -File -Recurse -ErrorAction SilentlyContinue |
                Where-Object FullName -notin $beforeMedia |
                Select-Object -First 1 } `
            { param($value) $null -ne $value } `
            "candidate $Candidate recording start" 15)

        Start-Sleep -Milliseconds 2000
        $null = $enterEvent.Set()
        Start-Sleep -Milliseconds 650
        Start-Sleep -Milliseconds 2000

        Send-ManualHotkey F9
        $standard = Wait-CameraZoom $session.P0Path 1.6
        Start-Sleep -Milliseconds 2000
        $resizeSmoke = @()
        if ($IncludeResizeSmoke) {
            Require ($TargetHandle -ne [IntPtr]::Zero) `
                "$Direction resize smoke requires a real target HWND"
            $resizeSmoke = @(
                Invoke-DirectionalResizeSmoke `
                    $session.P0Path $TargetHandle $standard $Direction)
        }
        Send-ManualHotkey F9
        $wideAfterStandard = Wait-CameraZoom $session.P0Path 1.0
        Start-Sleep -Milliseconds 2000

        Send-ManualHotkey F10
        $strong = Wait-CameraZoom $session.P0Path 2.0
        Start-Sleep -Milliseconds 2000
        Send-ManualHotkey F10
        $wideAfterStrong = Wait-CameraZoom $session.P0Path 1.0
        Start-Sleep -Milliseconds 2000

        $null = $returnEvent.Set()
        Start-Sleep -Milliseconds 2380
        [void][PunchGateNative]::ShowWindowAsync(
            [IntPtr]$session.Process.MainWindowHandle, 9)
        Invoke-Button $session.Root '停止并保存'
        $published = Wait-Until `
            { Get-ChildItem -LiteralPath $recordingRoot -Filter '*.mp4' `
                -File -Recurse -ErrorAction SilentlyContinue |
                Where-Object {
                    $_.FullName -notin $beforeMedia -and
                    $_.Name -notlike '*.partial.mp4'
                } | Sort-Object LastWriteTime | Select-Object -Last 1 } `
            { param($value) $null -ne $value -and $value.Length -gt 0 } `
            "candidate $Candidate published MP4" 30
        if ($null -ne $audioPlayer) {
            $audioPlayer.Stop()
            $audioPlayer.Dispose()
            $audioPlayer = $null
        }
        $destination = Join-Path $OutputDirectory $FileName
        Copy-Item -LiteralPath $published.FullName -Destination $destination -Force
        Require (Test-Path -LiteralPath $destination -PathType Leaf) `
            "candidate $Candidate final MP4 copy is missing"
        return [pscustomobject]@{
            Candidate = $Candidate
            Direction = $Direction
            Path = $destination
            P0Path = $session.P0Path
            StandardZoom = $standard.nativeAppliedZoom
            StrongZoom = $strong.nativeAppliedZoom
            WideAfterStandard = $wideAfterStandard.nativeAppliedZoom
            WideAfterStrong = $wideAfterStrong.nativeAppliedZoom
            FinalCaptureFrames = $wideAfterStrong.captureFrameCount
            FinalState = $wideAfterStrong.state
            DeviceRemovedReason = $wideAfterStrong.deviceRemovedReason
            ResizeSmoke = $resizeSmoke
        }
    }
    finally {
        if ($null -ne $audioPlayer) {
            $audioPlayer.Stop()
            $audioPlayer.Dispose()
        }
        if ($null -ne $session) {
            Stop-TestProcess $session.Process
        }
        $enterEvent.Dispose()
        $returnEvent.Dispose()
    }
}

function Invoke-BResizeSmoke([string]$TitleToken, [IntPtr]$TargetHandle) {
    $eventId = [Guid]::NewGuid().ToString('N')
    $enterName = "Local\XbPreview.Punch.BSmoke.Enter.$eventId"
    $returnName = "Local\XbPreview.Punch.BSmoke.Return.$eventId"
    $enterEvent = [Threading.EventWaitHandle]::new(
        $false, [Threading.EventResetMode]::ManualReset, $enterName)
    $returnEvent = [Threading.EventWaitHandle]::new(
        $false, [Threading.EventResetMode]::ManualReset, $returnName)
    [Environment]::SetEnvironmentVariable(
        'XB_PREVIEW_TEST_WINDOW_SHOWCASE_MOTION_ENTER_EVENT', $enterName, 'Process')
    [Environment]::SetEnvironmentVariable(
        'XB_PREVIEW_TEST_WINDOW_SHOWCASE_MOTION_RETURN_EVENT', $returnName, 'Process')
    $session = $null
    try {
        $session = Start-PunchHost 'B' $TitleToken $enterEvent $returnEvent
        $null = $enterEvent.Set()
        Start-Sleep -Milliseconds 650
        Send-ManualHotkey F9
        $initial = Wait-CameraZoom $session.P0Path 1.6
        $samples = [Collections.Generic.List[object]]::new()

        foreach ($size in @(
            [pscustomobject]@{ Name = 'larger'; Width = 1180; Height = 760 },
            [pscustomobject]@{ Name = 'smaller'; Width = 820; Height = 600 })) {
            Require ([PunchGateNative]::MoveWindow(
                $TargetHandle, 100, 80, $size.Width, $size.Height, $true)) `
                "B smoke could not resize $($size.Name)"
            $sample = Wait-LiveAfter `
                $session.P0Path ([UInt64]$initial.captureFrameCount)
            $samples.Add([pscustomobject]@{
                Phase = $size.Name
                CaptureFrames = $sample.captureFrameCount
                CaptureSize = "$($sample.captureWidth)x$($sample.captureHeight)"
                Zoom = $sample.nativeAppliedZoom
            })
            $initial = $sample
        }

        [void][PunchGateNative]::ShowWindowAsync($TargetHandle, 3)
        [void](Wait-Until `
            { [PunchGateNative]::IsZoomed($TargetHandle) } `
            { param($value) [bool]$value } `
            'B smoke target maximize state')
        $maximized = Wait-LiveAfter `
            $session.P0Path ([UInt64]$initial.captureFrameCount)
        Require ([PunchGateNative]::IsZoomed($TargetHandle)) `
            'B smoke target did not maximize'
        $samples.Add([pscustomobject]@{
            Phase = 'maximize'; CaptureFrames = $maximized.captureFrameCount
            CaptureSize = "$($maximized.captureWidth)x$($maximized.captureHeight)"
            Zoom = $maximized.nativeAppliedZoom
        })

        [void][PunchGateNative]::ShowWindowAsync($TargetHandle, 9)
        [void](Wait-Until `
            { -not [PunchGateNative]::IsZoomed($TargetHandle) } `
            { param($value) [bool]$value } `
            'B smoke target restore state')
        Require ([PunchGateNative]::MoveWindow(
            $TargetHandle, 100, 80, 1000, 700, $true)) `
            'B smoke could not restore normal target bounds'
        $restored = Wait-LiveAfter `
            $session.P0Path ([UInt64]$maximized.captureFrameCount) 986 693
        Require (-not [PunchGateNative]::IsZoomed($TargetHandle)) `
            'B smoke target remained maximized after restore'
        $samples.Add([pscustomobject]@{
            Phase = 'restore'; CaptureFrames = $restored.captureFrameCount
            CaptureSize = "$($restored.captureWidth)x$($restored.captureHeight)"
            Zoom = $restored.nativeAppliedZoom
        })

        Send-ManualHotkey F9
        $null = Wait-CameraZoom $session.P0Path 1.0
        $null = $returnEvent.Set()
        Start-Sleep -Milliseconds 500
        return $samples
    }
    finally {
        if ($null -ne $session) {
            Stop-TestProcess $session.Process
        }
        $enterEvent.Dispose()
        $returnEvent.Dispose()
    }
}

Require (Test-Path -LiteralPath $hostExe -PathType Leaf) `
    "Release Host is missing: $hostExe"
Require (@(Get-Process XbPreview.Host -ErrorAction SilentlyContinue).Count -eq 0) `
    'close every existing XbPreview.Host process before the Punch Gate'
$chromePath = @(
    'C:\Program Files\Google\Chrome\Application\chrome.exe',
    'C:\Program Files (x86)\Google\Chrome\Application\chrome.exe'
) | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
Require (-not [string]::IsNullOrWhiteSpace($chromePath)) `
    'Google Chrome is not installed in a standard location'
$null = New-Item -ItemType Directory -Path $OutputDirectory -Force
$null = New-Item -ItemType Directory -Path $diagnosticRoot -Force

$savedEnvironment = @{}
$environmentNames = @(
    'XB_PREVIEW_TEST_WINDOW_SHOWCASE_MOTION_PRESET',
    'XB_PREVIEW_TEST_WINDOW_SHOWCASE_MOTION_ENTER_EVENT',
    'XB_PREVIEW_TEST_WINDOW_SHOWCASE_MOTION_RETURN_EVENT',
    'XB_PREVIEW_TEST_WINDOW_SHOWCASE_MOTION_DIRECTION',
    'XB_PREVIEW_TEST_WINDOW_SHOWCASE_MOTION_STRENGTH',
    'XB_PREVIEW_TEST_WINDOW_STAGE_MANUAL_PUNCH_CANDIDATE',
    'XB_PREVIEW_TEST_WINDOW_STAGE_25D_DIRECTION',
    'XB_PREVIEW_TEST_WINDOW_STAGE_25D_STRENGTH')
foreach ($name in $environmentNames) {
    $savedEnvironment[$name] = [Environment]::GetEnvironmentVariable(
        $name, 'Process')
}
[Environment]::SetEnvironmentVariable(
    'XB_PREVIEW_TEST_WINDOW_STAGE_25D_DIRECTION', $null, 'Process')
[Environment]::SetEnvironmentVariable(
    'XB_PREVIEW_TEST_WINDOW_STAGE_25D_STRENGTH', $null, 'Process')

$gateId = [Guid]::NewGuid().ToString('N')
$titleToken = "XB Manual Zoom Punch Target $gateId"
$fixtureRoot = Join-Path $OutputDirectory 'fixture'
$profile = Join-Path $fixtureRoot 'chrome-profile'
$htmlPath = Join-Path $fixtureRoot 'target.html'
$null = New-Item -ItemType Directory -Path $profile -Force
$tonePath = ''
if ($SystemAudioOnRight) {
    $ffmpegPath = Join-Path $repoRoot `
        'artifacts\audio-v2-toolchain\ffmpeg-8.1-lgpl-shared\ffmpeg-n8.1-latest-win64-lgpl-shared-8.1\bin\ffmpeg.exe'
    Require (Test-Path -LiteralPath $ffmpegPath -PathType Leaf) `
        "pinned ffmpeg is missing: $ffmpegPath"
    $tonePath = Join-Path $fixtureRoot 'right-system-audio-tone.wav'
    & $ffmpegPath -nostdin -hide_banner -loglevel error -y `
        -f lavfi -i 'sine=frequency=440:sample_rate=48000:duration=2' `
        -ac 2 -c:a pcm_s16le $tonePath
    Require ($LASTEXITCODE -eq 0 -and
        (Test-Path -LiteralPath $tonePath -PathType Leaf) -and
        (Get-Item -LiteralPath $tonePath).Length -gt 0) `
        'RIGHT SystemOnly tone generation failed'
}
[IO.File]::WriteAllText(
    $htmlPath,
    "<title>$titleToken</title><body style='margin:0;background:#f7f3eb;" +
    "font-family:Segoe UI,sans-serif;color:#25231f'><main style='padding:64px'>" +
    "<p style='letter-spacing:.18em;color:#8c6b45'>WINDOW PRODUCT DEMO</p>" +
    "<h1 style='font-size:64px;margin:16px 0'>Manual Zoom Punch-in</h1>" +
    "<p style='font-size:28px;max-width:900px'>The same real target, content, " +
    ($(if ($BThreeDirection) {
        "window size, background, motion, and B script for RIGHT / FRONT / LEFT."
    }
    else {
        "window size, background, motion, and camera script for A / B / C."
    })) + "</p>" +
    "<div style='display:flex;gap:24px;margin-top:48px'>" +
    "<div style='padding:28px;background:#fff;border-radius:18px'>1.6x Standard</div>" +
    "<div style='padding:28px;background:#2a2926;color:#fff;border-radius:18px'>" +
    "2.0x Strong</div></div></main></body>",
    [Text.UTF8Encoding]::new($false))

$chrome = $null
$targetProcess = $null
try {
    $chrome = Start-Process -FilePath $chromePath -ArgumentList @(
        "--user-data-dir=$profile", '--no-first-run', '--disable-default-apps',
        '--new-window', '--window-size=1000,700', ([Uri]$htmlPath).AbsoluteUri
    ) -PassThru
    $targetProcess = Wait-Until `
        { Get-Process chrome -ErrorAction SilentlyContinue | Where-Object {
            $_.MainWindowHandle -ne [IntPtr]::Zero -and
            $_.MainWindowTitle.Contains($titleToken)
        } | Select-Object -First 1 } `
        { param($value) $null -ne $value } `
        'isolated Chrome target'
    $targetHandle = [IntPtr]$targetProcess.MainWindowHandle
    Require ([PunchGateNative]::MoveWindow(
        $targetHandle, 100, 80, 1000, 700, $true)) `
        'could not establish the shared target initial bounds'

    if ($BThreeDirection) {
        $results = @(
            Invoke-RecordedCandidate 'B' '01_RIGHT_B_PUNCH.mp4' `
                $titleToken 'RIGHT' $targetHandle -IncludeResizeSmoke `
                -SystemAudio:$SystemAudioOnRight -TonePath $tonePath
            Invoke-RecordedCandidate 'B' '02_FRONT_B_PUNCH.mp4' `
                $titleToken 'FRONT' $targetHandle
            Invoke-RecordedCandidate 'B' '03_LEFT_B_PUNCH.mp4' `
                $titleToken 'LEFT' $targetHandle -IncludeResizeSmoke
        )
        $summary = [pscustomobject]@{
            Status = 'WINDOW-STAGE-MANUAL-ZOOM-PUNCH-IN-B-3DIRECTION-MEDIA-PASS'
            TargetApplication = $targetProcess.Path
            TargetInitialBounds = '1000x700@100,80'
            Directions = 'RIGHT/FRONT/LEFT'
            Strength = 'LEVEL_2'
            Candidate = 'B/SHOWCASE'
            StandardHeadroom = 0.18
            StrongHeadroom = 0.36
            Motion = 'A: 360ms smootherstep, persistent STAY, explicit 380ms Return'
            Results = $results
        }
    }
    else {
        $results = @(
            Invoke-RecordedCandidate 'A' '01_PUNCH_A_LIGHT.mp4' $titleToken
            Invoke-RecordedCandidate 'B' '02_PUNCH_B_SHOWCASE.mp4' $titleToken
            Invoke-RecordedCandidate 'C' '03_PUNCH_C_STRONG.mp4' $titleToken
        )
        $resizeSmoke = Invoke-BResizeSmoke $titleToken $targetHandle
        $summary = [pscustomobject]@{
            Status = 'WINDOW-STAGE-MANUAL-ZOOM-PUNCH-IN-ABC-MEDIA-PASS'
            TargetApplication = $targetProcess.Path
            TargetInitialBounds = '1000x700@100,80'
            Direction = 'RIGHT'
            Strength = 'LEVEL_2'
            Motion = 'A: 360ms smootherstep, persistent STAY, explicit 380ms Return'
            Results = $results
            BResizeSmoke = $resizeSmoke
        }
    }
    $summary | ConvertTo-Json -Depth 8 |
        Set-Content -LiteralPath (Join-Path $OutputDirectory 'gate-summary.json') `
        -Encoding UTF8
    $summary | ConvertTo-Json -Depth 8
}
finally {
    foreach ($name in $environmentNames) {
        [Environment]::SetEnvironmentVariable(
            $name, $savedEnvironment[$name], 'Process')
    }
    if ($null -ne $targetProcess) {
        Stop-TestProcess $targetProcess
    }
    if ($null -ne $chrome -and
        ($null -eq $targetProcess -or $chrome.Id -ne $targetProcess.Id)) {
        Stop-TestProcess $chrome
    }
}
