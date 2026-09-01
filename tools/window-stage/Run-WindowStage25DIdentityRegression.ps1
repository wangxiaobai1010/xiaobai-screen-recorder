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

Write-Host 'WINDOW-STAGE-25D IDENTITY REGRESSION' `
    -ForegroundColor White -BackgroundColor DarkBlue
& $flatStageGate '--layer2-identity'
if ($LASTEXITCODE -ne 0) {
    throw "Layer 2 Identity regression failed: $LASTEXITCODE"
}

$cases = @(
    [pscustomobject]@{ Label = 'Chrome-Identity'; Hwnd = $ChromeHwnd },
    [pscustomobject]@{ Label = 'Edge-Identity'; Hwnd = $EdgeHwnd }
)
foreach ($case in $cases) {
    Write-Host "Running tiny live Identity control: $($case.Label)" `
        -ForegroundColor Cyan
    & powershell.exe `
        -NoProfile `
        -ExecutionPolicy Bypass `
        -File $resizeAudit `
        -Hwnd $case.Hwnd `
        -Label $case.Label `
        -NativeDirectory $NativeDirectory `
        -Mode Lifecycle `
        -PresentationMode Identity `
        -LifecycleSeconds 5
    if ($LASTEXITCODE -ne 0) {
        throw "$($case.Label) live Identity control failed: $LASTEXITCODE"
    }
}

Write-Host `
    'WINDOW-STAGE-25D IDENTITY REGRESSION PASS: Chrome + Edge' `
    -ForegroundColor Green
