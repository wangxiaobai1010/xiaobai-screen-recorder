[CmdletBinding()]
param(
    [ValidateSet('LEFT', 'FRONT', 'RIGHT')]
    [string]$Direction,

    [ValidateSet('LEVEL_1', 'LEVEL_2', 'LEVEL_3')]
    [string]$Strength,

    [switch]$All,

    [switch]$PreflightOnly
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0

$directionWasProvided = $PSBoundParameters.ContainsKey('Direction')
$strengthWasProvided = $PSBoundParameters.ContainsKey('Strength')
if ($All -and ($directionWasProvided -or $strengthWasProvided)) {
    throw '-All cannot be combined with -Direction or -Strength.'
}
if ($directionWasProvided -xor $strengthWasProvided) {
    throw '-Direction and -Strength must be provided together.'
}
if (-not $PreflightOnly -and -not $All -and -not $directionWasProvided) {
    throw 'Specify -All, or specify both -Direction and -Strength.'
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$binaryRoot = Join-Path $repoRoot 'artifacts\bin\Release\x64'
$hostExe = Join-Path $binaryRoot 'XbPreview.Host.exe'
$nativeDll = Join-Path $binaryRoot 'XbPreview.Native.dll'
$testExe = Join-Path $binaryRoot 'XbPreview.FlatStage.Tests.exe'
$directionVariable = 'XB_PREVIEW_TEST_WINDOW_STAGE_25D_DIRECTION'
$strengthVariable = 'XB_PREVIEW_TEST_WINDOW_STAGE_25D_STRENGTH'

function Invoke-StageGate {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Selector
    )

    & $testExe $Selector
    if ($LASTEXITCODE -ne 0) {
        throw "Stage Gate failed ($Selector) with exit code $LASTEXITCODE."
    }
}

if (-not (Test-Path -LiteralPath $testExe -PathType Leaf)) {
    throw "Release x64 Stage Gate executable is missing: $testExe"
}

Write-Host 'WINDOW STAGE 2.5D PREFLIGHT' -ForegroundColor White -BackgroundColor DarkBlue
Write-Host "Gate executable: $testExe" -ForegroundColor DarkCyan
Invoke-StageGate -Selector '--layer2-identity'
Invoke-StageGate -Selector '--stage-transform'
Write-Host 'Layer 2 Identity and StageTransform gates passed.' -ForegroundColor Green

if ($PreflightOnly) {
    Write-Host ''
    Write-Host 'Nine-pose human smoke command:' -ForegroundColor Cyan
    Write-Host "powershell.exe -NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`" -All"
    return
}
if (-not (Test-Path -LiteralPath $hostExe -PathType Leaf)) {
    throw "Release x64 Host executable is missing: $hostExe"
}
if (-not (Test-Path -LiteralPath $nativeDll -PathType Leaf)) {
    throw "Release x64 Native runtime is missing: $nativeDll"
}

$runningHosts = @(Get-Process -Name 'XbPreview.Host' -ErrorAction SilentlyContinue)
if ($runningHosts.Count -ne 0) {
    throw 'Close every existing XbPreview.Host process before the 2.5D smoke.'
}

$poses = if ($All) {
    foreach ($candidateDirection in @('LEFT', 'FRONT', 'RIGHT')) {
        foreach ($candidateStrength in @('LEVEL_1', 'LEVEL_2', 'LEVEL_3')) {
            [pscustomobject]@{
                Direction = $candidateDirection
                Strength = $candidateStrength
            }
        }
    }
}
else {
    @([pscustomobject]@{
        Direction = $Direction.ToUpperInvariant()
        Strength = $Strength.ToUpperInvariant()
    })
}

$savedDirection = [Environment]::GetEnvironmentVariable(
    $directionVariable,
    [EnvironmentVariableTarget]::Process)
$savedStrength = [Environment]::GetEnvironmentVariable(
    $strengthVariable,
    [EnvironmentVariableTarget]::Process)
$activeProcess = $null

Write-Host ''
Write-Host 'WINDOW STAGE 2.5D HUMAN SMOKE' -ForegroundColor White -BackgroundColor DarkBlue
Write-Host "Host executable: $hostExe" -ForegroundColor DarkCyan
Write-Host 'The same compiled Host will be restarted for every requested pose.' -ForegroundColor DarkCyan
Write-Warning 'FRONT LEVEL_1/2/3 are restrained candidates with no prior human-approved tuning. Review them as an A/B set; do not treat them as validated baselines.'
Write-Host 'LEFT and RIGHT at the same level must read as geometric mirrors. The card and its existing shadow must stay together.' -ForegroundColor Yellow

try {
    $poseNumber = 0
    foreach ($pose in $poses) {
        $poseNumber++
        [Environment]::SetEnvironmentVariable(
            $directionVariable,
            $pose.Direction,
            [EnvironmentVariableTarget]::Process)
        [Environment]::SetEnvironmentVariable(
            $strengthVariable,
            $pose.Strength,
            [EnvironmentVariableTarget]::Process)

        Write-Host ''
        Write-Host "Pose $poseNumber/$($poses.Count): $($pose.Direction) x $($pose.Strength)" -ForegroundColor Cyan
        $activeProcess = Start-Process -FilePath $hostExe -PassThru
        Start-Sleep -Seconds 3
        if ($activeProcess.HasExited) {
            throw "The Host exited during startup for $($pose.Direction) x $($pose.Strength)."
        }

        Write-Host '1. Set capture range to Window and select an ordinary Chrome or VS Code window.'
        Write-Host '2. Confirm the pose is static: no drift, zoom, orbit, entrance, or Showcase motion.'
        Write-Host '3. Confirm the full source remains visible and legible, with no crop, stretch, or desktop leakage.'
        Write-Host '4. Confirm the rounded card and its existing Layer 2 shadow transform as one object and remain unclipped.'
        if ($pose.Direction -eq 'FRONT') {
            Write-Host '5. Judge this unvalidated FRONT trapezoid candidate for restraint and usefulness.' -ForegroundColor Yellow
        }
        else {
            Write-Host '5. In an -All run, compare the matching opposite direction for mirror symmetry and matching strength.'
        }

        $humanResult = (Read-Host "Enter PASS to accept this visual observation, or anything else to stop").Trim()
        if ($humanResult -cne 'PASS') {
            throw "Human 2.5D smoke was not accepted for $($pose.Direction) x $($pose.Strength)."
        }

        if (-not $activeProcess.HasExited) {
            $null = $activeProcess.CloseMainWindow()
            if (-not $activeProcess.WaitForExit(5000)) {
                throw 'Close the recorder normally before continuing; the smoke will not force-stop a recorder that may still be finalizing.'
            }
        }
        $activeProcess = $null
    }

    Write-Host "WINDOW STAGE 2.5D HUMAN SMOKE PASS: $($poses.Count) pose(s)" -ForegroundColor Green
}
finally {
    if ($null -ne $activeProcess -and -not $activeProcess.HasExited) {
        $null = $activeProcess.CloseMainWindow()
        $null = $activeProcess.WaitForExit(5000)
    }
    [Environment]::SetEnvironmentVariable(
        $directionVariable,
        $savedDirection,
        [EnvironmentVariableTarget]::Process)
    [Environment]::SetEnvironmentVariable(
        $strengthVariable,
        $savedStrength,
        [EnvironmentVariableTarget]::Process)
}
