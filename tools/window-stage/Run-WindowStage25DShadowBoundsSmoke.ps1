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
if (@(Get-Process -Name 'XbPreview.Host' -ErrorAction SilentlyContinue).Count -ne 0) {
    throw 'Close every existing XbPreview.Host process before this smoke.'
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

Write-Host 'WINDOW STAGE 2.5D SHADOW BOUNDS HUMAN SMOKE' `
    -ForegroundColor White -BackgroundColor DarkBlue
Write-Host "Release Host: $hostExe" -ForegroundColor DarkCyan
Write-Host `
    'Each fresh session runs Motion A: Identity -> 360ms RIGHT x LEVEL_2 -> persistent STAY.' `
    -ForegroundColor Yellow
Write-Host `
    'The Return event stays unsignaled; there is no automatic Return.' `
    -ForegroundColor Yellow

try {
    [Environment]::SetEnvironmentVariable(
        $presetVariable,
        'A',
        [EnvironmentVariableTarget]::Process)
    [Environment]::SetEnvironmentVariable(
        $directionVariable,
        $null,
        [EnvironmentVariableTarget]::Process)
    [Environment]::SetEnvironmentVariable(
        $strengthVariable,
        $null,
        [EnvironmentVariableTarget]::Process)

    foreach ($browser in @('CHROME', 'EDGE')) {
        $returnEventName =
            'Local\XbPreview.ShadowBoundsSmoke.Return.' +
            [Guid]::NewGuid().ToString('N')
        $returnEvent = [System.Threading.EventWaitHandle]::new(
            $false,
            [System.Threading.EventResetMode]::ManualReset,
            $returnEventName)
        $activeProcess = $null
        try {
            [Environment]::SetEnvironmentVariable(
                $returnEventVariable,
                $returnEventName,
                [EnvironmentVariableTarget]::Process)
            $activeProcess = Start-Process -FilePath $hostExe -PassThru
            Start-Sleep -Seconds 3
            if ($activeProcess.HasExited) {
                throw "$browser Host exited during startup."
            }

            Write-Host ''
            Write-Host "$browser CASE" `
                -ForegroundColor White -BackgroundColor DarkCyan
            Write-Host "1. Set capture range to Window and select a live $browser window."
            Write-Host '2. Confirm the yellow WGC border stays visible.'
            Write-Host '3. Confirm Identity enters exact RIGHT x LEVEL_2 in 360ms and then stays.'
            Write-Host '4. While still in STAY: resize larger, resize smaller, maximize, then restore.'
            Write-Host '5. After every step, confirm Preview changes live and never shows a stale last frame.'
            Write-Host '6. Wait once more; confirm there is no automatic Return and the border remains.'
            $accepted = (Read-Host "Enter ${browser}-PASS only after all six checks pass").Trim()
            if ($accepted -cne "${browser}-PASS") {
                throw "$browser persistent-pose smoke was not accepted."
            }
            if ($activeProcess.HasExited) {
                throw "$browser Host exited before human acceptance."
            }

            $null = $activeProcess.CloseMainWindow()
            if (-not $activeProcess.WaitForExit(5000)) {
                throw 'Close the recorder normally; this smoke will not force-stop it.'
            }
            $activeProcess = $null
        }
        finally {
            if ($null -ne $activeProcess -and -not $activeProcess.HasExited) {
                $null = $activeProcess.CloseMainWindow()
                $null = $activeProcess.WaitForExit(5000)
            }
            $returnEvent.Dispose()
        }
    }

    Write-Host `
        'WINDOW STAGE 2.5D SHADOW BOUNDS HUMAN SMOKE PASS' `
        -ForegroundColor Green
}
finally {
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
