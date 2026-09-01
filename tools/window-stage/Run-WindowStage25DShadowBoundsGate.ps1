[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$ChromeHwnd,

    [Parameter(Mandatory = $true)]
    [string]$EdgeHwnd,

    [string]$NativeDirectory
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
if ([string]::IsNullOrWhiteSpace($NativeDirectory)) {
    $NativeDirectory = Join-Path $repoRoot 'artifacts\bin\Release\x64'
}
$flatStageGate = Join-Path `
    $NativeDirectory `
    'XbPreview.FlatStage.Tests.exe'
$resizeAudit = Join-Path `
    $repoRoot `
    'tools\window-capture\Invoke-WindowTargetResizeAudit.ps1'

if (-not (Test-Path -LiteralPath $flatStageGate -PathType Leaf)) {
    throw "Release x64 FlatStage Gate is missing: $flatStageGate"
}
if (-not (Test-Path -LiteralPath $resizeAudit -PathType Leaf)) {
    throw "Window resize audit is missing: $resizeAudit"
}

Write-Host 'WINDOW-STAGE-25D-SHADOW-BOUNDS-GATE' `
    -ForegroundColor White -BackgroundColor DarkBlue
& $flatStageGate '--window-stage-25d-shadow-bounds'
if ($LASTEXITCODE -ne 0) {
    throw "Deterministic shadow-bounds Gate failed: $LASTEXITCODE"
}

$cases = @(
    [pscustomobject]@{ Label = 'Chrome-RIGHT-L2'; Hwnd = $ChromeHwnd },
    [pscustomobject]@{ Label = 'Edge-RIGHT-L2'; Hwnd = $EdgeHwnd }
)
foreach ($case in $cases) {
    Write-Host "Running live persistent-pose case: $($case.Label)" `
        -ForegroundColor Cyan
    & powershell.exe `
        -NoProfile `
        -ExecutionPolicy Bypass `
        -File $resizeAudit `
        -Hwnd $case.Hwnd `
        -Label $case.Label `
        -NativeDirectory $NativeDirectory `
        -Mode Resize `
        -PresentationMode Persistent25D
    if ($LASTEXITCODE -ne 0) {
        throw "$($case.Label) live persistent-pose Gate failed: $LASTEXITCODE"
    }
}

Write-Host `
    'WINDOW-STAGE-25D-SHADOW-BOUNDS-GATE PASS: Chrome + Edge' `
    -ForegroundColor Green
