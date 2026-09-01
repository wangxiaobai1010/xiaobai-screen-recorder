[CmdletBinding()]
param(
    [switch]$HumanSmoke
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$hostExe = Join-Path $repoRoot 'artifacts\bin\Release\x64\XbPreview.Host.exe'
$requiredBranch = 'fix/window-restore-stale-size'
$requiredHead = 'c9cb0a495c01e567d93e6219aa69c00c2cc4cd9e'
$allowedChanges = @(
    'XbPreview.Native/PreviewEngine.cpp',
    'docs/recovery/WINDOW-TARGET-MINIMIZE-RESTORE-STALE-SIZE.md',
    'tools/window-capture/Run-WindowRestoreSizeGate.ps1'
)

function Require([bool]$Condition, [string]$Message) {
    if (-not $Condition) {
        throw "WINDOW-RESTORE-SIZE-GATE FAIL: $Message"
    }
}

function Get-LastCompleteSummary([string]$Path) {
    $lines = @(Get-Content -LiteralPath $Path -Tail 100)
    for ($index = $lines.Count - 1; $index -ge 0; $index--) {
        try {
            $value = $lines[$index] | ConvertFrom-Json -ErrorAction Stop
            if ($value.type -eq 'summary') {
                return $value
            }
        }
        catch {
            # The logger may be appending the final line while it is sampled.
        }
    }
    return $null
}

function Wait-Until(
    [scriptblock]$Probe,
    [scriptblock]$Accept,
    [string]$Description,
    [int]$TimeoutSeconds = 15) {
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    do {
        $value = & $Probe
        if ($null -ne $value -and (& $Accept $value)) {
            return $value
        }
        Start-Sleep -Milliseconds 200
    } while ((Get-Date) -lt $deadline)
    throw "WINDOW-RESTORE-SIZE-GATE FAIL: timed out waiting for $Description."
}

function Get-WindowSnapshot([IntPtr]$Handle) {
    $rect = New-Object WindowRestoreGateNative+RECT
    Require ([WindowRestoreGateNative]::GetWindowRect($Handle, [ref]$rect)) `
        'GetWindowRect failed for the target HWND'
    [pscustomobject]@{
        Visible = [WindowRestoreGateNative]::IsWindowVisible($Handle)
        Iconic = [WindowRestoreGateNative]::IsIconic($Handle)
        Left = $rect.Left
        Top = $rect.Top
        Width = $rect.Right - $rect.Left
        Height = $rect.Bottom - $rect.Top
    }
}

function New-EvidenceSample(
    [string]$Phase,
    [IntPtr]$TargetHandle,
    [object]$Summary) {
    $window = Get-WindowSnapshot $TargetHandle
    [pscustomobject]@{
        AtUtc = (Get-Date).ToUniversalTime().ToString('O')
        Phase = $Phase
        TargetVisible = $window.Visible
        TargetIconic = $window.Iconic
        TargetWindow = "$($window.Width)x$($window.Height)@$($window.Left),$($window.Top)"
        CaptureSize = "$($Summary.captureWidth)x$($Summary.captureHeight)"
        PreviewSize = "$($Summary.previewWidth)x$($Summary.previewHeight)"
        CaptureFrameCount = [uint64]$Summary.captureFrameCount
        PresentFrameCount = [uint64]$Summary.presentFrameCount
        DroppedFrameCount = [uint64]$Summary.droppedFrameCount
        FramePoolRecreateCount = [uint64]$Summary.framePoolRecreateCount
        SwapChainResizeCount = [uint64]$Summary.swapChainResizeCount
        Flags = [uint32]$Summary.flags
        State = [int]$Summary.state
    }
}

function Get-ComboBoxes([System.Windows.Automation.AutomationElement]$Root) {
    $condition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::ComboBox)
    return $Root.FindAll(
        [System.Windows.Automation.TreeScope]::Descendants,
        $condition)
}

function Select-NativeComboBoxIndex(
    [System.Windows.Automation.AutomationElement]$Combo,
    [int]$Index) {
    $handle = [IntPtr]$Combo.Current.NativeWindowHandle
    Require ($handle -ne [IntPtr]::Zero) 'ComboBox has no native HWND'
    $selection = [WindowRestoreGateNative]::SendMessage(
        $handle, 0x014E, [IntPtr]$Index, [IntPtr]::Zero)
    Require ($selection.ToInt64() -ne -1) `
        "CB_SETCURSEL rejected index $Index"
    $parent = [WindowRestoreGateNative]::GetParent($handle)
    $controlId = [WindowRestoreGateNative]::GetDlgCtrlID($handle)
    Require ($parent -ne [IntPtr]::Zero -and $controlId -ne 0) `
        'could not resolve ComboBox parent/control ID'
    $selectionChangedWParam = [IntPtr](
        ($controlId -band 0xFFFF) -bor (1 -shl 16))
    [void][WindowRestoreGateNative]::SendMessage(
        $parent, 0x0111, $selectionChangedWParam, $handle)
}

function Find-NativeComboBoxItemIndex(
    [System.Windows.Automation.AutomationElement]$Combo,
    [string]$Token) {
    $handle = [IntPtr]$Combo.Current.NativeWindowHandle
    Require ($handle -ne [IntPtr]::Zero) 'ComboBox has no native HWND'
    $count = [WindowRestoreGateNative]::SendMessage(
        $handle, 0x0146, [IntPtr]::Zero, [IntPtr]::Zero).ToInt32()
    Require ($count -gt 0) 'target ComboBox is empty'
    $matches = @()
    for ($index = 0; $index -lt $count; $index++) {
        $length = [WindowRestoreGateNative]::SendMessage(
            $handle, 0x0149, [IntPtr]$index, [IntPtr]::Zero).ToInt32()
        if ($length -lt 0) {
            continue
        }
        $text = [Text.StringBuilder]::new($length + 1)
        [void][WindowRestoreGateNative]::SendMessageText(
            $handle, 0x0148, [IntPtr]$index, $text)
        if ($text.ToString().Contains($Token)) {
            $matches += $index
        }
    }
    Require ($matches.Count -eq 1) `
        "expected one target ComboBox item containing the token; found $($matches.Count)"
    return $matches[0]
}

Push-Location $repoRoot
try {
    $branch = (& git branch --show-current).Trim()
    $head = (& git rev-parse HEAD).Trim()
    Require ($LASTEXITCODE -eq 0 -and $branch -eq $requiredBranch) `
        "expected branch $requiredBranch; actual branch is $branch"
    Require ($head -eq $requiredHead) `
        "expected base $requiredHead; actual HEAD is $head"

    $actualChanges = @(& git status --porcelain=v1 -uall | ForEach-Object {
        $_.Substring(3).Replace('\', '/')
    })
    $unexpectedChanges = @($actualChanges | Where-Object {
        $_ -notin $allowedChanges
    })
    Require ($unexpectedChanges.Count -eq 0) `
        "unexpected candidate changes: [$($unexpectedChanges -join ', ')]"
    Require (Test-Path -LiteralPath $hostExe -PathType Leaf) `
        "Release Host is missing: $hostExe"

    if ($HumanSmoke) {
        Write-Host 'WINDOW TARGET RESTORE SIZE HUMAN SMOKE' `
            -ForegroundColor White -BackgroundColor DarkBlue
        Write-Host "Executable: $hostExe" -ForegroundColor DarkCyan
        Write-Host 'Open one ordinary Chrome window, then:' -ForegroundColor Yellow
        Write-Host '1. Select Window capture and that Chrome window; keep camera at Wide 1.0x.'
        Write-Host '2. Confirm the initial preview is correctly fitted.'
        Write-Host '3. Minimize, restore, and resize Chrome wider then narrower.'
        Write-Host '4. Repeat minimize -> restore twice more without reselecting the target.'
        Write-Host '5. PASS only if every restore automatically refits to the current size.'
        $process = Start-Process -FilePath $hostExe `
            -WorkingDirectory (Split-Path -Parent $hostExe) -PassThru
        $process.WaitForExit()
        $answer = Read-Host 'Enter PASS only if all five checks passed'
        Require ($answer -ceq 'PASS') 'human smoke was not accepted'
        Write-Host 'WINDOW-TARGET-RESTORE-SIZE-HUMAN-PASS' -ForegroundColor Green
        exit 0
    }

    Require (@(Get-Process XbPreview.Host -ErrorAction SilentlyContinue).Count -eq 0) `
        'close every existing XbPreview.Host process before the Gate'

    Add-Type -AssemblyName UIAutomationClient
    Add-Type -AssemblyName UIAutomationTypes
    Add-Type @'
using System;
using System.Text;
using System.Runtime.InteropServices;
public static class WindowRestoreGateNative
{
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")]
    public static extern bool ShowWindowAsync(IntPtr hWnd, int command);
    [DllImport("user32.dll")]
    public static extern bool IsIconic(IntPtr hWnd);
    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")]
    public static extern bool MoveWindow(
        IntPtr hWnd, int x, int y, int width, int height, bool repaint);
    [DllImport("user32.dll")]
    public static extern IntPtr SendMessage(
        IntPtr hWnd, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", EntryPoint = "SendMessageW",
        CharSet = CharSet.Unicode)]
    public static extern IntPtr SendMessageText(
        IntPtr hWnd, uint message, IntPtr wParam, StringBuilder text);
    [DllImport("user32.dll")]
    public static extern IntPtr GetParent(IntPtr hWnd);
    [DllImport("user32.dll")]
    public static extern int GetDlgCtrlID(IntPtr hWnd);
}
'@

    $chromePath = @(
        'C:\Program Files\Google\Chrome\Application\chrome.exe',
        'C:\Program Files (x86)\Google\Chrome\Application\chrome.exe'
    ) | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    Require (-not [string]::IsNullOrWhiteSpace($chromePath)) `
        'Google Chrome is not installed in a standard location'

    $gateId = [Guid]::NewGuid().ToString('N')
    $titleToken = "XB Restore Size Gate $gateId"
    $evidenceRoot = Join-Path $repoRoot `
        "artifacts\window-restore-size-gate\$gateId"
    $profile = Join-Path $evidenceRoot 'chrome-profile'
    $htmlPath = Join-Path $evidenceRoot 'target.html'
    New-Item -ItemType Directory -Force -Path $profile | Out-Null
    [IO.File]::WriteAllText(
        $htmlPath,
        "<title>$titleToken</title><body style='background:#f4f0e8;" +
        "font:32px sans-serif'><h1>$titleToken</h1><p>Resize target</p></body>",
        [Text.UTF8Encoding]::new($false))
    $targetUri = ([Uri]$htmlPath).AbsoluteUri

    $hostProcess = $null
    $chrome = $null
    $targetProcess = $null
    $samples = [Collections.Generic.List[object]]::new()
    try {
        $chrome = Start-Process -FilePath $chromePath -ArgumentList @(
            "--user-data-dir=$profile",
            '--no-first-run',
            '--disable-default-apps',
            '--new-window',
            '--window-size=1000,700',
            $targetUri
        ) -PassThru
        $targetProcess = Wait-Until `
            { Get-Process chrome -ErrorAction SilentlyContinue | Where-Object {
                $_.MainWindowHandle -ne [IntPtr]::Zero -and
                $_.MainWindowTitle.Contains($titleToken)
            } | Select-Object -First 1 } `
            { param($value) $null -ne $value } `
            'the isolated Chrome target window'
        $targetHandle = [IntPtr]$targetProcess.MainWindowHandle

        $diagnosticDirectory = Join-Path `
            (Split-Path -Parent $hostExe) 'diagnostic-logs'
        $p0Before = @(
            Get-ChildItem -LiteralPath $diagnosticDirectory -Filter 'p0-*.jsonl' `
                -ErrorAction SilentlyContinue | ForEach-Object FullName)
        $hostProcess = Start-Process -FilePath $hostExe `
            -WorkingDirectory (Split-Path -Parent $hostExe) -PassThru
        $hostProcess = Wait-Until `
            { $hostProcess.Refresh(); if (-not $hostProcess.HasExited -and
                    $hostProcess.MainWindowHandle -ne [IntPtr]::Zero) {
                    $hostProcess
                } } `
            { param($value) $null -ne $value } `
            'the Release Host window'
        $hostHandle = [IntPtr]$hostProcess.MainWindowHandle
        $root = [System.Windows.Automation.AutomationElement]::FromHandle($hostHandle)

        $defaultP0 = Wait-Until `
            { Get-ChildItem -LiteralPath $diagnosticDirectory `
                -Filter 'p0-*.jsonl' -ErrorAction SilentlyContinue |
                Where-Object { $_.FullName -notin $p0Before } |
                Sort-Object LastWriteTime | Select-Object -Last 1 } `
            { param($value) $null -ne $value } `
            'the automatic default capture diagnostic log'
        [void](Wait-Until `
            { Get-LastCompleteSummary $defaultP0.FullName } `
            { param($value) $value.state -eq 2 -and
                $value.captureFrameCount -gt 10 -and
                $value.presentFrameCount -gt 10 } `
            'the automatic default preview to finish starting')

        $combos = Wait-Until `
            { Get-ComboBoxes $root } `
            { param($value) $value.Count -ge 2 } `
            'the capture range selector'
        $range = $combos.Item(0)
        # The range selector is fixed as Full screen (0), Window (1). Use the
        # stable product index and send the same CBN_SELCHANGE notification as
        # a user selection. This avoids UIA's provider-dependent popup list.
        Select-NativeComboBoxIndex $range 1

        $combos = Wait-Until `
            { Get-ComboBoxes $root } `
            { param($value) $value.Count -ge 3 } `
            'the target window selector'
        $targetSelector = $combos.Item(1)
        $targetIndex = Find-NativeComboBoxItemIndex $targetSelector $titleToken
        Select-NativeComboBoxIndex $targetSelector $targetIndex
        $targetSelectionCount = 1

        $p0Path = Wait-Until `
            { @(Get-ChildItem -LiteralPath $diagnosticDirectory `
                    -Filter 'p0-*.jsonl' -ErrorAction SilentlyContinue |
                    Where-Object { $_.FullName -notin $p0Before } |
                    Sort-Object LastWriteTime) } `
            { param($value) @($value).Count -ge 2 } `
            'the selected-window WGC diagnostic log'
        $p0Path = @($p0Path)[-1].FullName
        $p0AfterSelection = @(
            Get-ChildItem -LiteralPath $diagnosticDirectory -Filter 'p0-*.jsonl' |
                Where-Object { $_.FullName -notin $p0Before })

        $initial = Wait-Until `
            { Get-LastCompleteSummary $p0Path } `
            { param($value) $value.state -eq 2 -and
                $value.captureWidth -gt 0 -and $value.captureHeight -gt 0 -and
                $value.captureFrameCount -gt 10 -and
                $value.presentFrameCount -gt 10 } `
            'the initial normal-size preview'
        $initialWindow = Get-WindowSnapshot $targetHandle
        $borderWidth = $initialWindow.Width - [int]$initial.captureWidth
        $borderHeight = $initialWindow.Height - [int]$initial.captureHeight
        Require ($borderWidth -ge 0 -and $borderHeight -ge 0) `
            'initial ContentSize exceeds the real target window bounds'
        $samples.Add((New-EvidenceSample 'initial-normal' $targetHandle $initial))

        $restoreSizes = @(
            [pscustomobject]@{ Name = 'restored-original'; Width = 1000; Height = 700 },
            [pscustomobject]@{ Name = 'restored-larger'; Width = 1180; Height = 760 },
            [pscustomobject]@{ Name = 'restored-smaller'; Width = 820; Height = 600 }
        )
        foreach ($size in $restoreSizes) {
            $beforeMinimize = Get-LastCompleteSummary $p0Path
            Require ([WindowRestoreGateNative]::ShowWindowAsync($targetHandle, 6)) `
                "could not minimize target for $($size.Name)"
            $minimized = Wait-Until `
                { Get-LastCompleteSummary $p0Path } `
                { param($value) [WindowRestoreGateNative]::IsIconic($targetHandle) -and
                    (([uint32]$value.flags -band 64) -ne 0) } `
                "the target-minimized state for $($size.Name)"
            Require ($minimized.captureWidth -gt 0 -and $minimized.captureHeight -gt 0) `
                'a minimized sentinel became an invalid capture resource size'
            $samples.Add((New-EvidenceSample `
                "$($size.Name)-minimized" $targetHandle $minimized))

            Require ([WindowRestoreGateNative]::ShowWindowAsync($targetHandle, 9)) `
                "could not restore target for $($size.Name)"
            Start-Sleep -Milliseconds 300
            Require ([WindowRestoreGateNative]::MoveWindow(
                $targetHandle,
                120,
                100,
                $size.Width,
                $size.Height,
                $true)) "could not resize target for $($size.Name)"
            $expectedWidth = $size.Width - $borderWidth
            $expectedHeight = $size.Height - $borderHeight
            $restored = Wait-Until `
                { Get-LastCompleteSummary $p0Path } `
                { param($value)
                    -not [WindowRestoreGateNative]::IsIconic($targetHandle) -and
                    (([uint32]$value.flags -band 64) -eq 0) -and
                    [Math]::Abs([int]$value.captureWidth - $expectedWidth) -le 2 -and
                    [Math]::Abs([int]$value.captureHeight - $expectedHeight) -le 2 -and
                    [uint64]$value.captureFrameCount -gt
                        [uint64]$beforeMinimize.captureFrameCount -and
                    [uint64]$value.presentFrameCount -gt
                        [uint64]$beforeMinimize.presentFrameCount } `
                "the current ContentSize after $($size.Name)"
            $restoredFrameCount = [uint64]$restored.captureFrameCount
            $continued = Wait-Until `
                { Get-LastCompleteSummary $p0Path } `
                { param($value) [uint64]$value.captureFrameCount -gt
                    ($restoredFrameCount + 10) -and $value.state -eq 2 } `
                "continuous frames after $($size.Name)"
            $samples.Add((New-EvidenceSample $size.Name $targetHandle $continued))
        }

        Require ($targetSelectionCount -eq 1) `
            'the target was reselected during restore recovery'
        $p0After = @(Get-ChildItem -LiteralPath $diagnosticDirectory `
            -Filter 'p0-*.jsonl' | Where-Object { $_.FullName -notin $p0Before })
        Require ($p0After.Count -eq $p0AfterSelection.Count -and
            $p0After.FullName -contains $p0Path) `
            'restore created a second capture engine/session'

        $completeRecords = [Collections.Generic.List[object]]::new()
        foreach ($line in Get-Content -LiteralPath $p0Path) {
            try {
                $completeRecords.Add(($line | ConvertFrom-Json -ErrorAction Stop))
            }
            catch {
                # Ignore only an in-progress final logger line.
            }
        }
        $fatalEvents = @($completeRecords | Where-Object {
            $_.type -eq 'event' -and $_.event -in @('error', 'stopped')
        })
        Require ($fatalEvents.Count -eq 0) `
            'the existing capture session stopped or reported a fatal error'
        $invalidSizes = @($completeRecords | Where-Object {
            $_.type -eq 'summary' -and
            ($_.captureWidth -le 0 -or $_.captureHeight -le 0)
        })
        Require ($invalidSizes.Count -eq 0) `
            'an invalid 0x0/extreme sentinel size reached capture resources'
        Require (-not [WindowRestoreGateNative]::IsIconic($hostHandle) -and
            [WindowRestoreGateNative]::IsWindowVisible($hostHandle)) `
            'the Gate changed recorder-window background/presentation state'

        $result = [pscustomobject]@{
            Gate = 'WINDOW-RESTORE-SIZE-GATE'
            Result = 'PASS'
            Base = $requiredHead
            TargetSelectionCount = $targetSelectionCount
            WindowCaptureSessionCount = 1
            CaptureLogCountAtWindowSelection = $p0AfterSelection.Count
            CaptureLogCountAfterRestoreCycles = $p0After.Count
            RecordingOrRecorderBackgroundSemanticsChanged = $false
            Samples = $samples
            DiagnosticLogFileName = [IO.Path]::GetFileName($p0Path)
        }
        $resultPath = Join-Path $evidenceRoot 'window-restore-size-gate.json'
        [IO.File]::WriteAllText(
            $resultPath,
            ($result | ConvertTo-Json -Depth 8),
            [Text.UTF8Encoding]::new($false))
        Write-Host 'WINDOW-RESTORE-SIZE-GATE PASS' -ForegroundColor Green
        Write-Host "Evidence: $resultPath" -ForegroundColor DarkCyan
        $samples | Format-Table -AutoSize | Out-Host
    }
    finally {
        if ($null -ne $hostProcess -and -not $hostProcess.HasExited) {
            [void]$hostProcess.CloseMainWindow()
            [void]$hostProcess.WaitForExit(15000)
        }
        if ($null -ne $targetProcess -and -not $targetProcess.HasExited) {
            [void]$targetProcess.CloseMainWindow()
            [void]$targetProcess.WaitForExit(15000)
        }
    }
}
finally {
    Pop-Location
}
