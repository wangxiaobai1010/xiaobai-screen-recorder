[CmdletBinding()]
param(
    [string]$OutputDirectory = ''
)

$ErrorActionPreference = 'Stop'
$runner = Join-Path $PSScriptRoot 'Run-ManualZoomPunchInABC.ps1'
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
    $OutputDirectory = Join-Path $repoRoot `
        'artifacts\manual-zoom-punch-in-b-3direction'
}

& $runner -OutputDirectory $OutputDirectory `
    -BThreeDirection -SystemAudioOnRight
if (-not $?) {
    throw 'WINDOW-STAGE-MANUAL-ZOOM-PUNCH-IN-B-3DIRECTION media Gate failed.'
}

$dtsGate = Join-Path $PSScriptRoot 'Test-StrictVideoDts.ps1'
$media = @(
    '01_RIGHT_B_PUNCH.mp4',
    '02_FRONT_B_PUNCH.mp4',
    '03_LEFT_B_PUNCH.mp4'
) | ForEach-Object { Join-Path $OutputDirectory $_ }
& $dtsGate -InputPath $media
if (-not $?) {
    throw 'WINDOW-STAGE-MANUAL-ZOOM-PUNCH-IN-B-3DIRECTION strict DTS Gate failed.'
}
