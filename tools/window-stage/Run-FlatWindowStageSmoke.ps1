[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$hostExe = Join-Path $repoRoot 'artifacts\bin\Release\x64\XbPreview.Host.exe'
$requiredBranch = 'recovery/window-capture-fhero-rebuild'
$frozenCommit = 'ff0b19ec0b5574e0dcd9ec1162c5f18c0393f204'
$allowedChanges = @(
    'XbPreview.FlatStage.Tests/FlatStageTests.cpp',
    'XbPreview.FlatStage.Tests/XbPreview.FlatStage.Tests.vcxproj',
    'XbPreview.Native/PreviewRenderer.cpp',
    'XbPreview.Native/WindowCardPlacement.h',
    'XbPreview.Native/WindowStageComposer.h',
    'XbPreview.Native/XbPreview.Native.vcxproj',
    'XbPreview.P1D-A1.sln',
    'docs/recovery/WINDOW-STAGE-LAYER1-FLAT-STAGE-RESTORE.md',
    'tools/window-stage/Run-FlatWindowStageSmoke.ps1'
)

Push-Location $repoRoot
try {
    $branch = (& git branch --show-current).Trim()
    $head = (& git rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0 -or $branch -ne $requiredBranch) {
        throw "Expected branch $requiredBranch; actual branch is $branch."
    }
    if ($head -ne $frozenCommit) {
        throw "Expected Layer 1 candidate on frozen commit $frozenCommit; actual HEAD is $head."
    }

    $actualChanges = @(& git diff --name-only $frozenCommit --) +
        @(& git ls-files --others --exclude-standard)
    $unexpectedChanges = @($actualChanges | Sort-Object -Unique | Where-Object {
        $_.Replace('\', '/') -notin $allowedChanges
    })
    if ($unexpectedChanges.Count -ne 0) {
        throw "Unexpected candidate files: $($unexpectedChanges -join ', ')"
    }
    if (-not (Test-Path -LiteralPath $hostExe -PathType Leaf)) {
        throw "Release x64 executable is missing: $hostExe"
    }

    Write-Host 'FLAT WINDOW STAGE LAYER 1 HUMAN SMOKE' -ForegroundColor White -BackgroundColor DarkBlue
    Write-Host "Frozen HEAD: $head" -ForegroundColor DarkCyan
    Write-Host "Executable: $hostExe" -ForegroundColor DarkCyan
    Write-Host ''
    Write-Host 'Open one ordinary Chrome or VS Code window and keep it visible.' -ForegroundColor Yellow
    Read-Host 'Press Enter to launch the recorder' | Out-Null

    $process = Start-Process -FilePath $hostExe -PassThru
    Start-Sleep -Seconds 3
    if ($process.HasExited) {
        throw 'The recorder exited during startup.'
    }

    Write-Host ''
    Write-Host '1. Set capture range to Window and select the prepared Chrome or VS Code window.'
    Write-Host '2. Confirm the window is centered on the flat #F3F0EA background with safe margins.'
    Write-Host '3. Resize the target between wide and narrow shapes; confirm aspect-fit updates without crop or stretch.'
    Write-Host '4. Record for 20-30 seconds with the normal System Audio / Microphone settings, then Stop.'
    Write-Host '5. Open the final MP4 and check: no desktop leakage, clear text, normal audio, playable file, normal Stop.'
    Write-Host ''
    $finalMp4 = (Read-Host 'After Stop, paste the full final MP4 path').Trim()
    if (-not (Test-Path -LiteralPath $finalMp4 -PathType Leaf)) {
        throw "The reported final MP4 does not exist: $finalMp4"
    }
    $humanResult = (Read-Host 'Enter PASS only if every Layer 1 check passed').Trim()
    if ($humanResult -cne 'PASS') {
        throw 'Human Flat Window Stage smoke was not accepted.'
    }
    Write-Host "FLAT WINDOW STAGE LAYER 1 HUMAN SMOKE PASS: $finalMp4" -ForegroundColor Green
}
finally {
    Pop-Location
}
