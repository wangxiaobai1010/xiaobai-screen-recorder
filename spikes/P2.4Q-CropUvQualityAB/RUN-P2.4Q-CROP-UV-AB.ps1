[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$msbuild = 'C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe'
$project = Join-Path $PSScriptRoot 'P2.QualityAB.CropUv.vcxproj'
$exe = Join-Path $root 'artifacts\spikes\P2.4Q\bin\Release\x64\P2.QualityAB.CropUv.exe'
$runId = [guid]::NewGuid().ToString('D').ToUpperInvariant()
$output = Join-Path $root "artifacts\p2.4-quality-ab\crop-uv\$runId"

if ((Split-Path -Leaf $root) -cne 'p2-4-outputcanvas-encoding-prototype') {
    throw 'Crop UV quality A/B must run from the canonical P2.4 working copy.'
}
$processes = @(Get-Process -ErrorAction SilentlyContinue | Where-Object {
    $_.ProcessName -like 'XbPreview*' -or $_.ProcessName -like 'P2.QualityAB*'
})
if ($processes.Count -ne 0) {
    throw "Residual Preview/quality Spike process count: $($processes.Count)"
}
if (-not (Test-Path -LiteralPath $msbuild -PathType Leaf)) {
    throw "MSBuild not found: $msbuild"
}

& $msbuild $project /m /t:Build /p:Configuration=Release /p:Platform=x64 /v:minimal
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
New-Item -ItemType Directory -Path $output | Out-Null
& $exe --output-dir $output --run-id $runId
$result = $LASTEXITCODE
if ($result -ne 0) { exit $result }

$required = @(
    'source-reference.png',
    'A-outputcanvas-baseline.png',
    'B-outputcanvas-candidate.png',
    'A-baseline.mp4',
    'B-candidate.mp4',
    'A-decoded-baseline.png',
    'B-decoded-candidate.png',
    'metrics.json',
    'quality-ab-report.md',
    'commands.txt',
    'run-summary.json',
    'index.html'
)
foreach ($name in $required) {
    $path = Join-Path $output $name
    if (-not (Test-Path -LiteralPath $path -PathType Leaf) -or
        (Get-Item -LiteralPath $path).Length -eq 0) {
        throw "Required quality A/B artifact is missing or empty: $name"
    }
}
$summary = Get-Content -Raw -Encoding UTF8 -LiteralPath (
    Join-Path $output 'run-summary.json') | ConvertFrom-Json
if ($summary.Result -ne 'PASS-UV-ROOT-CAUSE-CONFIRMED') {
    throw "Invalid quality A/B conclusion: $($summary.Result)"
}
$metrics = Get-Content -Raw -Encoding UTF8 -LiteralPath (
    Join-Path $output 'metrics.json') | ConvertFrom-Json
$candidate = $metrics.SourceVsCandidateOutput
$decoded = $metrics.SourceVsCandidateDecoded
if ([int64]$candidate.MismatchPixels -ne 0 -or
    [int64]$candidate.ExactMatchPixels -ne [int64]$candidate.TotalPixels -or
    [double]$candidate.RMSE -ne 0.0 -or
    @($candidate.MaxAbsoluteErrorBGRA | Where-Object { [int]$_ -ne 0 }).Count -ne 0 -or
    [double]$candidate.OnePixelLineRetentionPercent -ne 100.0 -or
    [double]$candidate.CheckerContrast -ne 255.0) {
    throw 'Product CropTransform candidate is not pixel-exact at Wide 1.0x.'
}
if ([double]$decoded.PSNR -lt 39.0 -or
    [double]$decoded.OnePixelLineRetentionPercent -lt 99.0) {
    throw 'Product CropTransform encoded quality regressed below the accepted candidate band.'
}
if ([bool]$summary.CandidateUsesProductCropTransform -ne $true -or
    [int]$summary.SubmittedA -ne [int]$summary.ReturnedA -or
    [int]$summary.SubmittedB -ne [int]$summary.ReturnedB) {
    throw 'Product candidate identity or tracked-frame balance is invalid.'
}
$residual = @(Get-Process -ErrorAction SilentlyContinue | Where-Object {
    $_.ProcessName -like 'XbPreview*' -or $_.ProcessName -like 'P2.QualityAB*'
})
if ($residual.Count -ne 0) {
    throw "Residual Preview/quality Spike process count after run: $($residual.Count)"
}
Write-Host "P2.4Q_RESULT=$($summary.Result)"
Write-Host "P2.4Q_RUN_ID=$runId"
Write-Host "P2.4Q_ARTIFACTS=$output"
Write-Host "P2.4Q_PRODUCT_EXACT_MATCH_PERCENT=$($candidate.ExactMatchPercent)"
Write-Host "P2.4Q_PRODUCT_MISMATCH_PIXELS=$($candidate.MismatchPixels)"
Write-Host "P2.4Q_PRODUCT_DECODED_PSNR=$($decoded.PSNR)"
Write-Host "P2.4Q_PRODUCT_DECODED_1PX_RETENTION=$($decoded.OnePixelLineRetentionPercent)"
exit 0
