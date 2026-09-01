[CmdletBinding()]
param(
    [switch]$Smoke
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$binaryRoot = Join-Path $repoRoot 'artifacts\bin\Release\x64'
$hostExe = Join-Path $binaryRoot 'XbPreview.Host.exe'
$diagnosticRoot = Join-Path $binaryRoot 'diagnostic-logs'
$smokeRoot = Join-Path $repoRoot `
    'artifacts\manual-zoom-punch-follow-launcher'

function Require([bool]$Condition, [string]$Message) {
    if (-not $Condition) {
        throw "2.5D PUNCH FOLLOW LAUNCHER FAIL: $Message"
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
    throw "2.5D PUNCH FOLLOW LAUNCHER FAIL: timed out waiting for $Description."
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
            # The native logger may be appending while sampled.
        }
    }
    return $null
}

function Wait-Zoom([string]$P0Path, [double]$Zoom) {
    return Wait-Until `
        { Get-LastSummary $P0Path } `
        { param($value)
            $value.state -eq 2 -and $value.deviceRemovedReason -eq 0 -and
            [Math]::Abs([double]$value.nativeAppliedZoom - $Zoom) -le 0.005 } `
        "applied zoom $Zoom" 15
}

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

function Get-VisibleButton(
    [System.Windows.Automation.AutomationElement]$Root,
    [string]$Token) {
    $condition = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::Button)
    foreach ($button in @($Root.FindAll(
        [System.Windows.Automation.TreeScope]::Descendants,
        $condition))) {
        if ($button.Current.Name.Contains($Token) -and
            $button.Current.IsEnabled -and -not $button.Current.IsOffscreen) {
            return $button
        }
    }
    return $null
}

function Invoke-ZoomButton(
    [System.Windows.Automation.AutomationElement]$Root,
    [string]$Token) {
    $button = Wait-Until `
        { Get-VisibleButton $Root $Token } `
        { param($value) $null -ne $value } `
        "visible enabled $Token Manual Zoom button"
    $pattern = $button.GetCurrentPattern(
        [System.Windows.Automation.InvokePattern]::Pattern)
    $pattern.Invoke()
}

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Windows.Forms
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class PunchFollowHumanNative
{
    [DllImport("user32.dll")]
    public static extern bool SetCursorPos(int x, int y);
}
'@

Require (Test-Path -LiteralPath $hostExe -PathType Leaf) `
    "Release Host is missing: $hostExe"
Require (@(Get-Process XbPreview.Host -ErrorAction SilentlyContinue).Count -eq 0) `
    'close every existing XbPreview.Host before using this launcher'
$null = New-Item -ItemType Directory -Path $diagnosticRoot -Force

$environment = @{
    XB_PREVIEW_TEST_WINDOW_SHOWCASE_MOTION_PRESET = 'A'
    XB_PREVIEW_TEST_WINDOW_SHOWCASE_MOTION_DIRECTION = 'RIGHT'
    XB_PREVIEW_TEST_WINDOW_SHOWCASE_MOTION_STRENGTH = 'LEVEL_2'
    XB_PREVIEW_TEST_WINDOW_STAGE_MANUAL_PUNCH_CANDIDATE = 'B'
}
$environmentNames = @(
    'XB_PREVIEW_TEST_WINDOW_SHOWCASE_MOTION_PRESET',
    'XB_PREVIEW_TEST_WINDOW_SHOWCASE_MOTION_ENTER_EVENT',
    'XB_PREVIEW_TEST_WINDOW_SHOWCASE_MOTION_RETURN_EVENT',
    'XB_PREVIEW_TEST_WINDOW_SHOWCASE_MOTION_DIRECTION',
    'XB_PREVIEW_TEST_WINDOW_SHOWCASE_MOTION_STRENGTH',
    'XB_PREVIEW_TEST_WINDOW_STAGE_MANUAL_PUNCH_CANDIDATE',
    'XB_PREVIEW_TEST_WINDOW_STAGE_25D_DIRECTION',
    'XB_PREVIEW_TEST_WINDOW_STAGE_25D_STRENGTH')
$savedEnvironment = @{}
foreach ($name in $environmentNames) {
    $savedEnvironment[$name] = [Environment]::GetEnvironmentVariable(
        $name, 'Process')
}

$eventId = [Guid]::NewGuid().ToString('N')
$enterName = "Local\XbPreview.HumanPunch.Enter.$eventId"
$returnName = "Local\XbPreview.HumanPunch.Return.$eventId"
$enterEvent = [Threading.EventWaitHandle]::new(
    $false, [Threading.EventResetMode]::ManualReset, $enterName)
$returnEvent = [Threading.EventWaitHandle]::new(
    $false, [Threading.EventResetMode]::ManualReset, $returnName)
$environment.XB_PREVIEW_TEST_WINDOW_SHOWCASE_MOTION_ENTER_EVENT = $enterName
$environment.XB_PREVIEW_TEST_WINDOW_SHOWCASE_MOTION_RETURN_EVENT = $returnName

$beforeP0 = @(Get-ChildItem -LiteralPath $diagnosticRoot `
    -Filter 'p0-*.jsonl' -File -ErrorAction SilentlyContinue |
    ForEach-Object FullName)
$hostProcess = $null
try {
    foreach ($name in $environment.Keys) {
        [Environment]::SetEnvironmentVariable(
            $name, [string]$environment[$name], 'Process')
    }
    [Environment]::SetEnvironmentVariable(
        'XB_PREVIEW_TEST_WINDOW_STAGE_25D_DIRECTION', $null, 'Process')
    [Environment]::SetEnvironmentVariable(
        'XB_PREVIEW_TEST_WINDOW_STAGE_25D_STRENGTH', $null, 'Process')

    $hostProcess = Start-Process -FilePath $hostExe `
        -WorkingDirectory $binaryRoot -PassThru
    $hostProcess = Wait-Until `
        { $hostProcess.Refresh(); if (-not $hostProcess.HasExited -and
                $hostProcess.MainWindowHandle -ne [IntPtr]::Zero) {
                $hostProcess
            } } `
        { param($value) $null -ne $value } `
        'Release Host window'
    $root = [System.Windows.Automation.AutomationElement]::FromHandle(
        [IntPtr]$hostProcess.MainWindowHandle)
    $p0 = Wait-Until `
        { Get-ChildItem -LiteralPath $diagnosticRoot -Filter 'p0-*.jsonl' -File |
            Where-Object FullName -notin $beforeP0 |
            Sort-Object LastWriteTime -Descending | Where-Object {
                $summary = Get-LastSummary $_.FullName
                $null -ne $summary -and $summary.state -eq 2 -and
                $summary.captureFrameCount -gt 10 -and
                $summary.presentFrameCount -gt 10
            } | Select-Object -First 1 } `
        { param($value) $null -ne $value } `
        'live preview with test-only Stage/Punch wiring'

    $beforeEnter = Get-LastSummary $p0.FullName
    $null = $enterEvent.Set()
    $entered = Wait-Until `
        { Get-LastSummary $p0.FullName } `
        { param($value) $value.state -eq 2 -and
            [UInt64]$value.captureFrameCount -gt
                ([UInt64]$beforeEnter.captureFrameCount + 45) -and
            $value.deviceRemovedReason -eq 0 } `
        'RIGHT LEVEL_2 Enter and persistent live frames'

    if (-not $Smoke) {
        Write-Host ''
        Write-Host '2.5D Punch Follow human preview is ready.'
        Write-Host 'Pose: RIGHT LEVEL_2 (persistent STAY)'
        Write-Host 'Punch: B / SHOWCASE (1.6x=0.18, 2.0x=0.36)'
        Write-Host 'Camera and comfort-zone Cursor Follow are enabled.'
        Write-Host 'Click 1.6x or 2.0x, then move the mouse across the captured screen.'
        Write-Host 'Click the active zoom button again to return Wide; the card stays RIGHT L2.'
        Write-Host 'Close XbPreview.Host to finish and clean up this launcher.'
        [void]$hostProcess.WaitForExit()
    }
    else {
        $bounds = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
        Invoke-ZoomButton $root '1.6x'
        [void](Wait-Zoom $p0.FullName 1.6)
        Require ([PunchFollowHumanNative]::SetCursorPos(
            $bounds.Left + 80, $bounds.Top + 80)) `
            'SetCursorPos rejected the first 1.6x Follow point'
        Start-Sleep -Milliseconds 2200
        $standardFirst = Get-LastSummary $p0.FullName
        Require ([PunchFollowHumanNative]::SetCursorPos(
            $bounds.Right - 80, $bounds.Bottom - 80)) `
            'SetCursorPos rejected the second 1.6x Follow point'
        $standardSecond = Wait-Until `
            { Get-LastSummary $p0.FullName } `
            { param($value)
                $value.state -eq 2 -and
                [Math]::Abs([double]$value.nativeAppliedZoom - 1.6) -le 0.005 -and
                ([Math]::Abs([double]$value.nativeAppliedCenterX -
                    [double]$standardFirst.nativeAppliedCenterX) -gt 0.05 -or
                 [Math]::Abs([double]$value.nativeAppliedCenterY -
                    [double]$standardFirst.nativeAppliedCenterY) -gt 0.05) } `
            'real 1.6x Cursor Follow center movement' 15

        Invoke-ZoomButton $root '1.6x'
        $wideAfterStandard = Wait-Zoom $p0.FullName 1.0
        Invoke-ZoomButton $root '2.0x'
        [void](Wait-Zoom $p0.FullName 2.0)
        Require ([PunchFollowHumanNative]::SetCursorPos(
            $bounds.Right - 100, $bounds.Top + 100)) `
            'SetCursorPos rejected the first 2.0x Follow point'
        Start-Sleep -Milliseconds 2200
        $strongFirst = Get-LastSummary $p0.FullName
        Require ([PunchFollowHumanNative]::SetCursorPos(
            $bounds.Left + 100, $bounds.Bottom - 100)) `
            'SetCursorPos rejected the second 2.0x Follow point'
        $strongSecond = Wait-Until `
            { Get-LastSummary $p0.FullName } `
            { param($value)
                $value.state -eq 2 -and
                [Math]::Abs([double]$value.nativeAppliedZoom - 2.0) -le 0.005 -and
                ([Math]::Abs([double]$value.nativeAppliedCenterX -
                    [double]$strongFirst.nativeAppliedCenterX) -gt 0.05 -or
                 [Math]::Abs([double]$value.nativeAppliedCenterY -
                    [double]$strongFirst.nativeAppliedCenterY) -gt 0.05) } `
            'real 2.0x Cursor Follow center movement' 15

        Invoke-ZoomButton $root '2.0x'
        $wideAfterStrong = Wait-Zoom $p0.FullName 1.0
        $null = New-Item -ItemType Directory -Path $smokeRoot -Force
        $result = [pscustomobject]@{
            Status = 'WINDOW-STAGE-2.5D-PUNCH-FOLLOW-HUMAN-LAUNCHER-SMOKE-PASS'
            Host = $hostExe
            P0Path = $p0.FullName
            Direction = 'RIGHT'
            Strength = 'LEVEL_2'
            PersistentStay = $true
            Candidate = 'B/SHOWCASE'
            StandardZoom = $standardSecond.nativeAppliedZoom
            StandardCenterBefore = @(
                $standardFirst.nativeAppliedCenterX,
                $standardFirst.nativeAppliedCenterY)
            StandardCenterAfter = @(
                $standardSecond.nativeAppliedCenterX,
                $standardSecond.nativeAppliedCenterY)
            StrongZoom = $strongSecond.nativeAppliedZoom
            StrongCenterBefore = @(
                $strongFirst.nativeAppliedCenterX,
                $strongFirst.nativeAppliedCenterY)
            StrongCenterAfter = @(
                $strongSecond.nativeAppliedCenterX,
                $strongSecond.nativeAppliedCenterY)
            WideAfterStandard = $wideAfterStandard.nativeAppliedZoom
            WideAfterStrong = $wideAfterStrong.nativeAppliedZoom
            CaptureFrames = $wideAfterStrong.captureFrameCount
            DeviceRemovedReason = $wideAfterStrong.deviceRemovedReason
            ReturnEventSignaled = $false
        }
        $result | ConvertTo-Json -Depth 6 |
            Set-Content -LiteralPath (Join-Path $smokeRoot 'smoke-summary.json') `
            -Encoding UTF8
        $result | ConvertTo-Json -Depth 6
    }
}
finally {
    if ($Smoke -and $null -ne $hostProcess) {
        Stop-TestProcess $hostProcess
    }
    foreach ($name in $environmentNames) {
        [Environment]::SetEnvironmentVariable(
            $name, $savedEnvironment[$name], 'Process')
    }
    $enterEvent.Dispose()
    $returnEvent.Dispose()
}
