[CmdletBinding()]
param(
    [string]$BaseCommit = 'bbfeac9e8e49aea2f9b831f0379de163752d23a3',
    [string]$FinalTruthCommit = 'ab3ab9f4691dd19dcdfe380a6b919f1f3dbba261',
    [switch]$SkipRuntime
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repository = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$binaryRoot = Join-Path $repository 'artifacts\bin\Release\x64'

function Require([bool]$Condition, [string]$Message) {
    if (-not $Condition) {
        throw "PANEL3 FINAL SHOWCASE GATE FAIL: $Message"
    }
}

function Git-Text([string[]]$Arguments) {
    $value = (& git -C $repository @Arguments) -join "`n"
    Require ($LASTEXITCODE -eq 0) "git $($Arguments -join ' ') failed"
    return $value.Trim()
}

Require ((Git-Text @('rev-parse', "$BaseCommit^{}")) -eq $BaseCommit) `
    'current product base does not resolve exactly'
Require ((Git-Text @('rev-parse', "$FinalTruthCommit^{}")) -eq
    $FinalTruthCommit) 'final truth does not resolve exactly'
& git -C $repository merge-base --is-ancestor $FinalTruthCommit $BaseCommit
Require ($LASTEXITCODE -eq 0) 'final truth is not in current product ancestry'

$protectedCore = @(
    'XbPreview.Native/WindowStageTransform.h',
    'XbPreview.Native/WindowShowcaseMotionController.h',
    'XbPreview.Native/WindowStagePunchOverlay.h',
    'XbPreview.Native/WindowCardShadowPass.h',
    'XbPreview.Native/WindowStageComposer.h')
foreach ($path in $protectedCore) {
    $expected = Git-Text @('rev-parse', "$BaseCommit`:$path")
    $actual = Git-Text @('hash-object', "$(Join-Path $repository $path)")
    Require ($actual -eq $expected) "protected core changed: $path"
}
Require ((Git-Text @(
        'hash-object',
        "$(Join-Path $repository 'XbPreview.Native/WindowStagePunchOverlay.h')")) -eq
    (Git-Text @(
        'rev-parse',
        "$FinalTruthCommit`:XbPreview.Native/WindowStagePunchOverlay.h"))) `
    'exact final Punch B implementation is not present'

$changed = @(Git-Text @('diff', '--name-only', $BaseCommit, '--') -split "`n")
$protectedProductPatterns = @(
    'Panel1',
    'CapturePanelView',
    'CaptureFixedHomeAdapter',
    'CaptureFloating',
    'Audio',
    'Recording',
    'Finalize',
    'GStreamer',
    'VideoEncoder',
    'DirectorPanelView',
    'DirectorPanelActionAdapter')
foreach ($pattern in $protectedProductPatterns) {
    Require (-not ($changed -match [regex]::Escape($pattern))) `
        "protected current-product area changed: $pattern"
}

$settings = Get-Content -LiteralPath `
    (Join-Path $repository 'XbPreview.Host\ProductSettings.cs') -Raw
$panel3Adapter = Get-Content -LiteralPath `
    (Join-Path $repository 'XbPreview.Host\Stage3DPanelActionAdapter.cs') -Raw
$panel3Controller = Get-Content -LiteralPath `
    (Join-Path $repository 'XbPreview.Host\Stage3DPanelActionController.cs') -Raw
Require ($settings.Contains('session.SetWindowShowcasePose(')) `
    'formal product settings do not use the frozen Showcase seam'
Require (-not $settings.Contains('session.SetWindowStagePose(')) `
    'failed Formal Stage ABI is still used by product settings'
Require ($panel3Controller.Contains('session.SetWindowShowcasePose(')) `
    'Panel 3 does not use the frozen Showcase seam'
Require (-not $panel3Adapter.Contains('SetWindowStagePose(') -and
    -not $panel3Controller.Contains('SetWindowStagePose(')) `
    'Panel 3 uses the failed Formal Stage ABI'
Require (-not $panel3Controller.Contains('SetCameraState(') -and
    -not $panel3Controller.Contains('WideZoom')) `
    'Panel 3 Return crosses into Panel 2 camera ownership'

$api = Get-Content -LiteralPath `
    (Join-Path $repository 'XbPreview.Native\XbPreviewApi.h') -Raw
$exports = Get-Content -LiteralPath `
    (Join-Path $repository 'XbPreview.Native\Exports.cpp') -Raw
Require (-not $api.Contains('XbPreview_RequestWindowShowcaseReturn') -and
    -not $exports.Contains('XbPreview_RequestWindowShowcaseReturn')) `
    'a second native Return API was added instead of reusing the Showcase seam'

$punch = Get-Content -LiteralPath `
    (Join-Path $repository 'XbPreview.Native\WindowStagePunchOverlay.h') -Raw
Require ($punch.Contains('WindowStagePunchShowcase{') -and
    $punch.Contains('0.18f, 0.36f')) `
    'exact Showcase 1.6x/2.0x headroom progression is missing'

$renderer = Get-Content -LiteralPath `
    (Join-Path $repository 'XbPreview.Native\PreviewRenderer.cpp') -Raw
$renderStart = $renderer.IndexOf('HRESULT PreviewRenderer::RenderFrame(')
Require ($renderStart -ge 0) 'RenderFrame implementation is missing'
$render = $renderer.Substring($renderStart)
$baseIndex = $render.IndexOf('selectedStageTransform = windowStageTransform_')
$punchIndex = $render.IndexOf('ComposeWindowStagePunchOverlay(')
$stageIndex = $render.IndexOf('ComposeWindowStageTransform(')
$canvasIndex = $render.IndexOf('outputCanvas_.RenderTargetView()')
$tapIndex = $render.IndexOf('frameTap_.ObserveAndCopy(')
$previewIndex = $render.IndexOf('previewFrameExport_.Publish(')
Require ($baseIndex -ge 0 -and $punchIndex -gt $baseIndex -and
    $stageIndex -gt $punchIndex -and $canvasIndex -gt $stageIndex) `
    'Base -> Punch -> Stage/Card -> OutputCanvas order changed'
Require ($tapIndex -gt $canvasIndex -and $previewIndex -gt $canvasIndex) `
    'Preview and encoder do not consume the completed OutputCanvas'

$presentation = Get-Content -LiteralPath (Join-Path $repository `
    'XbPreview.Avalonia\Views\Panels\Stage3DPanelPresentationState.cs') -Raw
Require ($presentation.Contains('Stage3DPanelOrientation.Right') -and
    $presentation.Contains('Stage3DPanelLevel.Level2')) `
    'Panel 3 does not reflect frozen RIGHT / LEVEL_2 initialization'

if (-not $SkipRuntime) {
    $flat = Join-Path $binaryRoot 'XbPreview.FlatStage.Tests.exe'
    $managed = Join-Path $binaryRoot 'XbPreview.Managed.Tests.exe'
    $timestamp = Join-Path $binaryRoot 'XbPreview.Timestamp.Tests.exe'
    Require (Test-Path -LiteralPath $flat -PathType Leaf) 'FlatStage test missing'
    Require (Test-Path -LiteralPath $managed -PathType Leaf) 'managed test missing'
    Require (Test-Path -LiteralPath $timestamp -PathType Leaf) `
        'timestamp test missing'

    $selectors = @(
        '--stage-transform',
        '--layer3-minimal',
        '--window-stage-25d-shadow-bounds',
        '--left-front-motion',
        '--showcase-motion',
        '--punch-overlay',
        '--punch-showcase-9pose')
    foreach ($selector in $selectors) {
        & $flat $selector
        Require ($LASTEXITCODE -eq 0) "FlatStage $selector failed"
    }
    & $managed --formal-product-contracts
    Require ($LASTEXITCODE -eq 0) 'formal product contracts failed'
    & $timestamp --timestamp-transaction
    Require ($LASTEXITCODE -eq 0) 'timestamp transaction failed'
    & $timestamp --source-sink-boundary
    Require ($LASTEXITCODE -eq 0) 'source/sink boundary failed'
}

Write-Output 'PANEL3 FINAL SHOWCASE INTEGRATION GATE = PASS'
Write-Output 'BASE=RIGHT/LEVEL_2 MOTION=A PUNCH=B 1.6x=0.18 2.0x=0.36 WIDE=BASE'
Write-Output 'PANEL2=UNCHANGED PANEL1/AUDIO/RECORDING/FINALIZE=UNCHANGED'
Write-Output 'PREVIEW/MP4=ONE OUTPUTCANVAS FAILED_FORMAL_ABI_USED=NO'
