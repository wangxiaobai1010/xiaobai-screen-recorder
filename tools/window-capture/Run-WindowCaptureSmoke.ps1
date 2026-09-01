[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$hostExe = Join-Path $repoRoot 'artifacts\bin\Release\x64\XbPreview.Host.exe'
$testExe = Join-Path $repoRoot 'artifacts\bin\Release\x64\XbPreview.Managed.Tests.exe'
$requiredBranch = 'recovery/window-capture-fhero-rebuild'
$requiredHead = 'aec61b95f8c8266e93c95a954ece8099d8f41dab'
$allowedChanges = @(
    'XbPreview.Managed.Tests/PreviewLifecycleTests.cs',
    'XbPreview.Managed.Tests/Program.cs',
    'XbPreview.Native/PreviewEngine.cpp',
    'XbPreview.Native/PreviewEngine.h',
    'docs/recovery/WINDOW-CAPTURE-FHERO-CLEAN-REBUILD.md',
    'tools/window-capture/Run-WindowCaptureSmoke.ps1'
)

Push-Location $repoRoot
try {
    $branch = (& git branch --show-current).Trim()
    $head = (& git rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0 -or $branch -ne $requiredBranch) {
        throw "Expected branch $requiredBranch; actual branch is $branch."
    }
    if ($head -ne $requiredHead) {
        throw "Expected uncommitted candidate on base $requiredHead; actual HEAD is $head."
    }
    $actualChanges = @(& git status --porcelain=v1 -uall | ForEach-Object {
        $_.Substring(3).Replace('\', '/')
    })
    $unexpectedChanges = @($actualChanges | Where-Object {
        $_ -notin $allowedChanges
    })
    $missingChanges = @($allowedChanges | Where-Object {
        $_ -notin $actualChanges
    })
    if ($unexpectedChanges.Count -ne 0 -or $missingChanges.Count -ne 0) {
        throw "Candidate source scope does not match the reviewed six-file set. " +
            "Unexpected=[$($unexpectedChanges -join ', ')]; " +
            "Missing=[$($missingChanges -join ', ')]."
    }
    if (-not (Test-Path -LiteralPath $hostExe -PathType Leaf)) {
        throw "Release x64 executable is missing: $hostExe"
    }
    if (-not (Test-Path -LiteralPath $testExe -PathType Leaf)) {
        throw "Release x64 Gate executable is missing: $testExe"
    }

    Write-Host "WINDOW CAPTURE HUMAN SMOKE" -ForegroundColor White -BackgroundColor DarkBlue
    Write-Host "Candidate: $head" -ForegroundColor DarkCyan
    Write-Host "Executable: $hostExe" -ForegroundColor DarkCyan
    Write-Host ''
    Write-Host 'Before continuing, open one ordinary, visible top-level application window.' -ForegroundColor Yellow
    Read-Host 'Press Enter to enumerate real visible window candidates' | Out-Null

    $visibleWindows = Get-Process | Where-Object {
        $_.MainWindowHandle -ne [IntPtr]::Zero -and
        -not [string]::IsNullOrWhiteSpace($_.MainWindowTitle)
    } | Sort-Object -Property MainWindowTitle | ForEach-Object {
        [pscustomobject]@{
            PID = $_.Id
            HWND = '0x{0:X}' -f [uint64]$_.MainWindowHandle.ToInt64()
            Title = $_.MainWindowTitle
        }
    }
    if (-not $visibleWindows) {
        throw 'No real visible top-level window is available. Open one and rerun this Gate.'
    }
    $visibleWindows | Format-Table -AutoSize | Out-Host
    Write-Host 'Keep the selected target restored and visible while the item Gate runs.' -ForegroundColor Yellow
    $targetHwnd = (Read-Host 'Enter the HWND of the external window to prove CreateForWindow').Trim()
    if ($targetHwnd -notmatch '^(?:0[xX][0-9A-Fa-f]+|[0-9]+)$' -or
        $targetHwnd -match '^(?:0[xX]0+|0+)$') {
        throw "HWND must be a non-zero decimal or 0x-prefixed hexadecimal value: $targetHwnd"
    }

    & $testExe '--window-capture-item' $targetHwnd
    if ($LASTEXITCODE -ne 0) {
        throw "The real Window Capture item-creation Gate failed with exit code $LASTEXITCODE."
    }

    Read-Host 'Item creation PASS. Press Enter to launch the recorder' | Out-Null

    $process = Start-Process -FilePath $hostExe -PassThru
    Start-Sleep -Seconds 3
    if ($process.HasExited) {
        throw 'The recorder exited during startup.'
    }

    Write-Host ''
    Write-Host '1. Set the capture range to Window and select the visible window you opened.'
    Write-Host '2. Record for 20-30 seconds, then use the normal Stop action.'
    Write-Host '3. Open the final MP4.'
    Write-Host '4. Check only: correct window, no desktop leakage, continuous video, normal audio,'
    Write-Host '   playable file after Stop, and no remaining recorder/device resources.'
    Write-Host ''
    $finalMp4 = Read-Host 'After Stop, paste the full final MP4 path'
    if (-not (Test-Path -LiteralPath $finalMp4 -PathType Leaf)) {
        throw "The reported final MP4 does not exist: $finalMp4"
    }
    $process.WaitForExit(10000) | Out-Null
    $process.Refresh()
    if (-not $process.HasExited) {
        throw 'The recorder process is still running. Close it and rerun the human check.'
    }
    $humanResult = Read-Host 'Enter PASS only if all six checks pass'
    if ($humanResult -cne 'PASS') {
        throw 'Human Window Capture smoke was not accepted.'
    }
    Write-Host "WINDOW CAPTURE HUMAN SMOKE PASS: $finalMp4" -ForegroundColor Green
}
finally {
    Pop-Location
}
