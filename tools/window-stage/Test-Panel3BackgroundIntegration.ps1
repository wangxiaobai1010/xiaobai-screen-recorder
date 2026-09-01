[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$base = '6782b7be3cda7c36c3a0c38102ad8081cd51c8e1'

function Require([bool]$Condition, [string]$Message) {
    if (-not $Condition) {
        throw "Panel3 background gate failed: $Message"
    }
    Write-Host "PASS: $Message"
}

function Read-Source([string]$RelativePath) {
    return Get-Content -LiteralPath (Join-Path $root $RelativePath) `
        -Raw -Encoding UTF8
}

$panel = Read-Source 'XbPreview.Avalonia\Views\Panels\Stage3DPanelView.axaml'
$panelCode = Read-Source 'XbPreview.Avalonia\Views\Panels\Stage3DPanelView.axaml.cs'
$backgroundState = Read-Source 'XbPreview.Avalonia\Views\Panels\Stage3DPanelBackgroundState.cs'
$controller = Read-Source 'XbPreview.Host\Stage3DPanelBackgroundController.cs'
$host = Read-Source 'XbPreview.Host\StructuralAvaloniaShellHost.cs'
$renderer = Read-Source 'XbPreview.Native\PreviewRenderer.cpp'
$art01 = ([char]0x5e7b).ToString() + ([char]0x5f69) + '01'
$art02 = ([char]0x5e7b).ToString() + ([char]0x5f69) + '02'
$custom = ([char]0x81ea).ToString() + ([char]0x5b9a) + ([char]0x4e49)
$customCommand = $custom + ([char]0x2026)

Require ($panel.Contains('x:Name="BackgroundPresetSelector"') -and
    ([regex]::Matches($panel, 'x:Name="BackgroundPresetSelector"').Count -eq 1) -and
    $panel.Contains('capture-target-choice')) `
    'Panel3 has one canonical styled background ComboBox'
Require ($panelCode.Contains('"Warm"') -and
    $panelCode.Contains('"' + $art01 + '"') -and
    $panelCode.Contains('"' + $art02 + '"') -and
    $panelCode.Contains('"' + $customCommand + '"')) `
    'selector exposes exactly the three frozen presets plus custom command'
Require ([regex]::IsMatch(
        $backgroundState,
        '\?\s*' + [regex]::Escape('"' + $custom + '"')) -and
    -not $backgroundState.Contains('CustomImagePath.Substring')) `
    'custom selection presentation is the custom label and never a file path'
Require ($host.Contains('using OpenFileDialog dialog = new()') -and
    $host.Contains('*.png;*.jpg;*.jpeg;*.bmp') -and
    $host.Contains('dialog.ShowDialog(this)')) `
    'custom action opens the standard Windows image picker'
Require ($controller.Contains('IWindowShowcaseBackgroundCommands') -and
    -not $controller.Contains('SetWindowShowcasePose') -and
    -not $controller.Contains('SetCameraState')) `
    'background controller has no 2.5D pose or Panel2 camera authority'
Require ($controller.Contains('selectedPath is null') -and
    $controller.Contains('session.SetWindowShowcaseCustomBackground(validated)')) `
    'cancel and decode failure preserve the authoritative selection'
Require ($host.Contains('RecordingReviewState.Recording') -and
    $host.Contains('RecordingReviewState.Paused') -and
    $host.Contains('UpdateBackgroundActionsPresentation')) `
    'Recording and Paused lock the background selector'

$renderStart = $renderer.IndexOf(
    'constexpr std::array<float, 4> blackClearColor',
    [StringComparison]::Ordinal)
$renderEnd = $renderer.IndexOf(
    '// OutputCanvas is complete here',
    $renderStart,
    [StringComparison]::Ordinal)
Require ($renderStart -ge 0 -and $renderEnd -gt $renderStart) `
    'OutputCanvas render interval is locatable'
$frameComposition = $renderer.Substring(
    $renderStart,
    $renderEnd - $renderStart)
$backgroundDraw = $frameComposition.IndexOf(
    'windowShowcaseBackground.TextureTransforms()',
    [StringComparison]::Ordinal)
$windowCardDraw = $frameComposition.IndexOf(
    'DrawWindowCardContentPass(',
    [StringComparison]::Ordinal)
Require ($backgroundDraw -ge 0 -and $windowCardDraw -gt $backgroundDraw) `
    'Stage Background is rendered before Window Card content'
Require ($renderer.Contains('frameTap_.ObserveAndCopy(') -and
    $renderer.Contains('outputCanvas_.Texture()') -and
    $renderer.Contains('previewFrameExport_.Publish(') -and
    $renderer.Contains('videoEncoder_.Start(') -and
    $renderer.Contains('frameTap_,')) `
    'Preview and encoder consume the same completed OutputCanvas truth'

$protected = @(
    'XbPreview.Native/WindowStageTransform.h',
    'XbPreview.Native/WindowShowcaseMotionController.h',
    'XbPreview.Native/WindowStagePunchOverlay.h',
    'XbPreview.Native/WindowCardShadowPass.h'
)
foreach ($path in $protected) {
    $expected = (& git -C $root rev-parse "${base}:$path").Trim()
    $actual = (& git -C $root hash-object (Join-Path $root $path)).Trim()
    Require ($LASTEXITCODE -eq 0 -and $actual -eq $expected) `
        "frozen core unchanged: $path"
}

$nativeBackgroundDiff = @(& git -C $root diff --name-only $base -- `
    'XbPreview.Native/WindowShowcaseBackgroundPreset.h' `
    'XbPreview.Native/PreviewRenderer.cpp' `
    'XbPreview.Native/PreviewRenderer.h' `
    'XbPreview.Native/PreviewEngine.cpp' `
    'XbPreview.Native/PreviewEngine.h')
Require ($LASTEXITCODE -eq 0 -and $nativeBackgroundDiff.Count -eq 0) `
    'frozen native background renderer and OutputCanvas seam are unchanged'

$panel1Diff = @(& git -C $root diff --name-only $base -- `
    'XbPreview.Avalonia/Views/Panels/CapturePanelView.axaml' `
    'XbPreview.Avalonia/Views/Panels/CapturePanelView.axaml.cs' `
    'XbPreview.Host/Panel1PreparationAdapter.cs' `
    'XbPreview.Host/Panel1PreparationPolicy.cs')
Require ($LASTEXITCODE -eq 0 -and $panel1Diff.Count -eq 0) `
    'Panel1 diff is zero'

Write-Host 'PANEL3 BACKGROUND STATIC GATES PASS'
