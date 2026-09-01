[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$binaryRoot = Join-Path $repoRoot 'artifacts\bin\Release\x64'
$hostExe = Join-Path $binaryRoot 'XbPreview.Host.exe'
$nativeDll = Join-Path $binaryRoot 'XbPreview.Native.dll'
$presetVariable = 'XB_PREVIEW_TEST_WINDOW_SHOWCASE_MOTION_PRESET'
$returnEventVariable = 'XB_PREVIEW_TEST_WINDOW_SHOWCASE_MOTION_RETURN_EVENT'
$directionVariable = 'XB_PREVIEW_TEST_WINDOW_STAGE_25D_DIRECTION'
$strengthVariable = 'XB_PREVIEW_TEST_WINDOW_STAGE_25D_STRENGTH'

if (-not (Test-Path -LiteralPath $hostExe -PathType Leaf)) {
    throw "Release x64 Host executable is missing: $hostExe"
}
if (-not (Test-Path -LiteralPath $nativeDll -PathType Leaf)) {
    throw "Release x64 Native runtime is missing: $nativeDll"
}

$runningHosts = @(Get-Process -Name 'XbPreview.Host' -ErrorAction SilentlyContinue)
if ($runningHosts.Count -ne 0) {
    throw 'Close every existing XbPreview.Host process before the persistent-pose smoke.'
}

$savedPreset = [Environment]::GetEnvironmentVariable(
    $presetVariable,
    [EnvironmentVariableTarget]::Process)
$savedReturnEvent = [Environment]::GetEnvironmentVariable(
    $returnEventVariable,
    [EnvironmentVariableTarget]::Process)
$savedDirection = [Environment]::GetEnvironmentVariable(
    $directionVariable,
    [EnvironmentVariableTarget]::Process)
$savedStrength = [Environment]::GetEnvironmentVariable(
    $strengthVariable,
    [EnvironmentVariableTarget]::Process)
$returnEventName = 'Local\XbPreview.ShowcaseMotion.Return.' + [Guid]::NewGuid().ToString('N')
$returnEvent = [System.Threading.EventWaitHandle]::new(
    $false,
    [System.Threading.EventResetMode]::ManualReset,
    $returnEventName)
$activeProcess = $null

Write-Host 'WINDOW SHOWCASE MOTION A PERSISTENT-POSE HUMAN SMOKE' -ForegroundColor White -BackgroundColor DarkBlue
Write-Host "Host executable: $hostExe" -ForegroundColor DarkCyan
Write-Host 'Selected Motion A: 360 ms smootherstep to frozen RIGHT x LEVEL_2.' -ForegroundColor Yellow
Write-Host 'There is no automatic Return. The target pose stays indefinitely.' -ForegroundColor Yellow

try {
    [Environment]::SetEnvironmentVariable(
        $presetVariable,
        'A',
        [EnvironmentVariableTarget]::Process)
    [Environment]::SetEnvironmentVariable(
        $returnEventVariable,
        $returnEventName,
        [EnvironmentVariableTarget]::Process)
    [Environment]::SetEnvironmentVariable(
        $directionVariable,
        $null,
        [EnvironmentVariableTarget]::Process)
    [Environment]::SetEnvironmentVariable(
        $strengthVariable,
        $null,
        [EnvironmentVariableTarget]::Process)

    $activeProcess = Start-Process -FilePath $hostExe -PassThru
    Start-Sleep -Seconds 3
    if ($activeProcess.HasExited) {
        throw 'The Host exited during persistent-pose smoke startup.'
    }

    Write-Host ''
    Write-Host '1. Set capture range to Window and select one ordinary Chrome or VS Code window.'
    Write-Host '2. Watch Identity transition to RIGHT x LEVEL_2 with Motion A.'
    Write-Host '3. Confirm content, rounded corners, and shadow move as one card.'
    Write-Host ''
    Write-Host 'The current pose will stay indefinitely. Enter RETURN only when you are ready to return to Identity.' -ForegroundColor Cyan
    do {
        $humanResult = (Read-Host 'Enter RETURN to request the explicit 380 ms Return').Trim()
    } while ($humanResult -cne 'RETURN')

    $null = $returnEvent.Set()
    Start-Sleep -Milliseconds 650
    if ($activeProcess.HasExited) {
        throw 'The Host exited before the explicit Return could be observed.'
    }

    Write-Host 'Explicit Return requested. Confirm the card is now exact Identity.' -ForegroundColor Cyan
    $identityResult = (Read-Host 'Enter PASS after confirming exact Identity').Trim()
    if ($identityResult -cne 'PASS') {
        throw 'Persistent-pose Return was not accepted by the human observer.'
    }

    if (-not $activeProcess.HasExited) {
        $null = $activeProcess.CloseMainWindow()
        if (-not $activeProcess.WaitForExit(5000)) {
            throw 'Close the recorder normally; the smoke will not force-stop it.'
        }
    }
    $activeProcess = $null
    Write-Host 'WINDOW SHOWCASE MOTION A PERSISTENT HUMAN SMOKE PASS' -ForegroundColor Green
}
finally {
    if ($null -ne $activeProcess -and -not $activeProcess.HasExited) {
        $null = $activeProcess.CloseMainWindow()
        $null = $activeProcess.WaitForExit(5000)
    }
    $returnEvent.Dispose()
    [Environment]::SetEnvironmentVariable(
        $presetVariable,
        $savedPreset,
        [EnvironmentVariableTarget]::Process)
    [Environment]::SetEnvironmentVariable(
        $returnEventVariable,
        $savedReturnEvent,
        [EnvironmentVariableTarget]::Process)
    [Environment]::SetEnvironmentVariable(
        $directionVariable,
        $savedDirection,
        [EnvironmentVariableTarget]::Process)
    [Environment]::SetEnvironmentVariable(
        $strengthVariable,
        $savedStrength,
        [EnvironmentVariableTarget]::Process)
}
