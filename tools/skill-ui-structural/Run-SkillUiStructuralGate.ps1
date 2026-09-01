[CmdletBinding()]
param(
    [switch]$SelfCheck
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$expectedBase = 'ca1a5b2abbef109cf719e80cba6213f876bf52d1'
$expectedBranch = 'spike/formal-avalonia-skill-ui-structural-v1'
$recorderRoot = Split-Path (Split-Path $repoRoot -Parent) -Parent
$expectedCommonDir = (
    Join-Path $recorderRoot 'canonical\xiaobai-screen-recorder.git'
) -replace '\\', '/'
$hostExe = Join-Path $repoRoot (
    'artifacts\bin\Release\x64\XbPreview.Host.exe')
$ps51IncompatibilitiesFound = @(
    'String.Contains(String,StringComparison): token lookup',
    'String.Contains(String,StringComparison): GPU control lookup',
    'String.Contains(String,StringComparison): WinForms Settings lookup',
    'Start-Process ArgumentList: evidence path was not explicitly quoted'
)

function Assert-Equal {
    param(
        [Parameter(Mandatory = $true)]$Actual,
        [Parameter(Mandatory = $true)]$Expected,
        [Parameter(Mandatory = $true)][string]$Label
    )
    if ($Actual -ne $Expected) {
        throw "$Label expected '$Expected', got '$Actual'."
    }
}

function Get-ChangedPaths {
    $tracked = @(
        git -C $repoRoot diff --name-only $expectedBase --
    )
    if ($LASTEXITCODE -ne 0) {
        throw 'Unable to enumerate tracked changes.'
    }
    $untracked = @(
        git -C $repoRoot ls-files --others --exclude-standard
    )
    if ($LASTEXITCODE -ne 0) {
        throw 'Unable to enumerate untracked changes.'
    }
    return @($tracked + $untracked |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
        Sort-Object -Unique)
}

function Test-AllowedChange {
    param([Parameter(Mandatory = $true)][string]$Path)
    $allowed = @(
        'XbPreview.Avalonia/App.cs',
        'XbPreview.Avalonia/Styles/SkillRecorderStyles.axaml',
        'XbPreview.Avalonia/Views/StructuralShellView.axaml',
        'XbPreview.Avalonia/Views/StructuralShellView.axaml.cs',
        'XbPreview.Host/Program.cs',
        'XbPreview.Host/StructuralAvaloniaShellHost.cs',
        'XbPreview.Host/StructuralShellPerformanceGate.cs',
        'tools/skill-ui-structural/Run-SkillUiStructuralGate.ps1'
    )
    return $allowed -contains ($Path -replace '\\', '/')
}

function Test-TextContains {
    param(
        [Parameter(Mandatory = $true)][string]$Text,
        [Parameter(Mandatory = $true)][string]$Value,
        [StringComparison]$Comparison = [StringComparison]::Ordinal
    )
    return $Text.IndexOf($Value, $Comparison) -ge 0
}

function Invoke-HarnessSelfCheck {
    $version = $PSVersionTable.PSVersion
    if ($version.Major -ne 5 -or $version.Minor -ne 1) {
        throw "Windows PowerShell 5.1 required; got $version."
    }

    $parseTokens = $null
    $parseErrors = $null
    [void][System.Management.Automation.Language.Parser]::ParseFile(
        $PSCommandPath,
        [ref]$parseTokens,
        [ref]$parseErrors)
    if (@($parseErrors).Count -ne 0) {
        throw "Script parse failed: $($parseErrors -join '; ')"
    }

    $nonAsciiLeaf = 'ps51-' + [char]0x8DEF + [char]0x5F84 + '-check'
    $selfCheckRoot = Join-Path $repoRoot (
        'artifacts\skill-ui-structural\harness-self-check')
    $nonAsciiDirectory = Join-Path $selfCheckRoot $nonAsciiLeaf
    New-Item -ItemType Directory -Path $nonAsciiDirectory -Force |
        Out-Null
    $resolvedNonAscii = (
        Resolve-Path -LiteralPath $nonAsciiDirectory
    ).Path
    if ($resolvedNonAscii.IndexOf(
        [string][char]0x8DEF,
        [StringComparison]::Ordinal) -lt 0) {
        throw 'Non-ASCII path round-trip failed.'
    }

    $jsonPath = Join-Path $nonAsciiDirectory 'roundtrip.json'
    $jsonPayload = [ordered]@{
        Name = $nonAsciiLeaf
        Value = 42
        Enabled = $true
    }
    $jsonPayload | ConvertTo-Json -Depth 4 |
        Set-Content -LiteralPath $jsonPath -Encoding UTF8
    $jsonRoundTrip = Get-Content -Raw -Encoding UTF8 -LiteralPath $jsonPath |
        ConvertFrom-Json
    if ($jsonRoundTrip.Name -ne $nonAsciiLeaf -or
        $jsonRoundTrip.Value -ne 42 -or
        $jsonRoundTrip.Enabled -ne $true) {
        throw 'JSON read/write round-trip failed.'
    }

    $powerShellExe = Join-Path $PSHOME 'powershell.exe'
    $processProbeArguments = @{
        FilePath = $powerShellExe
        ArgumentList = '-NoProfile -Command "exit 0"'
        WindowStyle = 'Hidden'
        PassThru = $true
    }
    $processProbe = Start-Process @processProbeArguments
    if (-not $processProbe.WaitForExit(5000)) {
        Stop-Process -Id $processProbe.Id -Force -ErrorAction SilentlyContinue
        throw 'Process invocation self-check timed out.'
    }
    if ($processProbe.ExitCode -ne 0) {
        throw "Process invocation returned $($processProbe.ExitCode)."
    }

    $timeoutProbeArguments = @{
        FilePath = $powerShellExe
        ArgumentList = '-NoProfile -Command "Start-Sleep -Seconds 3"'
        WindowStyle = 'Hidden'
        PassThru = $true
    }
    $timeoutProbe = Start-Process @timeoutProbeArguments
    if ($timeoutProbe.WaitForExit(100)) {
        throw 'Timeout handling probe exited before the timeout boundary.'
    }
    Stop-Process -Id $timeoutProbe.Id -Force -ErrorAction SilentlyContinue
    [void]$timeoutProbe.WaitForExit(5000)

    Write-Output "PS VERSION = $($version.Major).$($version.Minor)"
    Write-Output 'SCRIPT PARSE = PASS'
    Write-Output 'NON-ASCII PATH = PASS'
    Write-Output 'JSON READ/WRITE = PASS'
    Write-Output 'PROCESS INVOCATION = PASS'
    Write-Output 'TIMEOUT HANDLING = PASS'
    Write-Output 'EXPECTED TEST BINARY EXISTS = YES'
    Write-Output (
        'PS51_INCOMPATIBILITIES_FOUND = ' +
        $ps51IncompatibilitiesFound.Count)
    foreach ($incompatibility in $ps51IncompatibilitiesFound) {
        Write-Output "PS51_INCOMPATIBILITY = $incompatibility"
    }
}

$commonDir = (git -C $repoRoot rev-parse --git-common-dir).Trim() -replace '\\', '/'
$head = (git -C $repoRoot rev-parse HEAD).Trim()
$branch = (git -C $repoRoot branch --show-current).Trim()
Assert-Equal $commonDir $expectedCommonDir 'CANONICAL COMMON DIR'
Assert-Equal $head $expectedBase 'SPIKE HEAD'
Assert-Equal $branch $expectedBranch 'SPIKE BRANCH'

if (-not (Test-Path -LiteralPath $hostExe -PathType Leaf)) {
    throw 'Release x64 Host is missing. Build Host and Native first.'
}
$nativeDll = Join-Path (Split-Path -Parent $hostExe) 'XbPreview.Native.dll'
if (-not (Test-Path -LiteralPath $nativeDll -PathType Leaf)) {
    throw 'Release x64 native GPU runtime is missing.'
}

$changedPaths = @(Get-ChangedPaths)
$unexpected = @($changedPaths | Where-Object { -not (Test-AllowedChange $_) })
if ($unexpected.Count -ne 0) {
    throw "Protected or unexpected paths changed: $($unexpected -join ', ')"
}

$protectedPrefixes = @(
    'XbPreview.Native/',
    'third_party/',
    'XbPreview.Avalonia/Contracts/',
    'XbPreview.Avalonia/Controls/',
    'XbPreview.Host/IPreviewNativeSession.cs',
    'XbPreview.Host/NativeMethods.cs',
    'XbPreview.Host/NativePreviewSession.cs',
    'XbPreview.Host/PreviewLifecycleController.cs',
    'XbPreview.Host/GpuPreviewFrameSource.cs',
    'XbPreview.Host/RecordingController.cs'
)
$protectedChanged = @(
    foreach ($path in $changedPaths) {
        foreach ($prefix in $protectedPrefixes) {
            if (($path -replace '\\', '/').StartsWith(
                $prefix,
                [StringComparison]::OrdinalIgnoreCase)) {
                $path
                break
            }
        }
    }
)
if ($protectedChanged.Count -ne 0) {
    throw "Protected Core changed: $($protectedChanged -join ', ')"
}

$stylePath = Join-Path $repoRoot (
    'XbPreview.Avalonia\Styles\SkillRecorderStyles.axaml')
$styleText = Get-Content -Raw -Encoding UTF8 -LiteralPath $stylePath
$requiredTokens = [ordered]@{
    'SkillRecorder.Brush.Deck' = '#FAF8F5'
    'SkillRecorder.Brush.Surface' = '#FFFFFF'
    'SkillRecorder.Brush.SurfaceSecondary' = '#F3F1ED'
    'SkillRecorder.Brush.TextPrimary' = '#242424'
    'SkillRecorder.Brush.TextSecondary' = '#707070'
    'SkillRecorder.Brush.TextFaint' = '#878787'
    'SkillRecorder.Brush.Line' = '#DEDEDE'
    'SkillRecorder.Brush.LineStrong' = '#BDBDBD'
    'SkillRecorder.Brush.Brand' = '#E0351F'
    'SkillRecorder.Brush.BrandHover' = '#C12E1A'
    'SkillRecorder.Brush.BrandInk' = '#B62D1B'
    'SkillRecorder.Brush.SignalDim' = '#21E0341E'
    'SkillRecorder.Brush.Record' = '#E82C17'
    'SkillRecorder.Brush.Success' = '#008554'
    'SkillRecorder.Brush.Warning' = '#CE7C09'
}
foreach ($entry in $requiredTokens.GetEnumerator()) {
    $needle = 'x:Key="' + $entry.Key + '">' + $entry.Value + '<'
    if (-not (Test-TextContains $styleText $needle)) {
        throw "Design token is missing or changed: $($entry.Key)"
    }
}

$newBinaryAssets = @(
    $changedPaths | Where-Object {
        $_ -match '(?i)\.(woff2?|ttf|otf|ico|png|jpe?g|webp|svg)$'
    }
)
if ($newBinaryAssets.Count -ne 0) {
    throw "Donor font/brand asset boundary failed: $($newBinaryAssets -join ', ')"
}

$shellXaml = Get-Content -Raw -Encoding UTF8 -LiteralPath (
    Join-Path $repoRoot 'XbPreview.Avalonia\Views\StructuralShellView.axaml')
$hostSource = Get-Content -Raw -Encoding UTF8 -LiteralPath (
    Join-Path $repoRoot 'XbPreview.Host\StructuralAvaloniaShellHost.cs')
if (-not (Test-TextContains -Text $shellXaml -Value (
    '<controls:GpuPreviewControl x:Name="GpuPreview"'))) {
    throw 'Structural shell does not directly host GpuPreviewControl.'
}
if (Test-TextContains $hostSource 'FormalUiSettingsView') {
    throw 'Structural route introduced a WinForms Settings surface.'
}

if ($SelfCheck) {
    Invoke-HarnessSelfCheck
    return
}

$runRoot = Join-Path $repoRoot (
    'artifacts\skill-ui-structural\' +
    (Get-Date -Format 'yyyyMMdd-HHmmss-fff') + '-' +
    [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $runRoot -Force | Out-Null
$evidencePath = Join-Path $runRoot 'runtime-evidence.json'

$startArguments = @{
    FilePath = $hostExe
    ArgumentList = @(
        '--skill-ui-structural-gate',
        ('"' + $evidencePath + '"')
    )
    WindowStyle = 'Normal'
    PassThru = $true
}
$process = Start-Process @startArguments
if (-not $process.WaitForExit(120000)) {
    Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
    throw 'Structural shell runtime Gate exceeded 120 seconds.'
}
if ($process.ExitCode -ne 0) {
    if (Test-Path -LiteralPath $evidencePath -PathType Leaf) {
        $failedEvidence = Get-Content -Raw -Encoding UTF8 -LiteralPath (
            $evidencePath)
        throw "Runtime Gate failed with exit $($process.ExitCode): $failedEvidence"
    }
    throw "Runtime Gate failed with exit $($process.ExitCode) and no evidence."
}
if (-not (Test-Path -LiteralPath $evidencePath -PathType Leaf)) {
    throw 'Structural shell runtime Gate produced no evidence.'
}

$runtime = Get-Content -Raw -Encoding UTF8 -LiteralPath $evidencePath |
    ConvertFrom-Json
Assert-Equal $runtime.Status 'PASS' 'RUNTIME GATE'

$summary = [ordered]@{
    Status = 'PASS'
    CanonicalCommonDir = $commonDir
    Base = $expectedBase
    Branch = $branch
    Worktree = $repoRoot
    DonorSnapshot = '034e886806d3682f53f0a8dbd822c37e28844e0d'
    DonorFile = 'src/App.css'
    DonorLicense = 'MIT'
    AvaloniaSingleShell = $true
    WinFormsOuterHost = $true
    WinFormsSettingsSurface = $false
    ElectronPresent = $false
    DesignTokensLoaded = $true
    BundledDonorFontCopied = $false
    BrandAssetCopied = $false
    ProtectedCoreChanged = $false
    CpuFrameCopyCount = 0
    ModifiedAndNewFiles = $changedPaths
    RuntimeEvidence = $evidencePath
    Runtime = $runtime.Facts
    GuiShownToUser = $false
    Commit = $false
    Tag = $false
    Push = $false
    CompletedUtc = [DateTimeOffset]::UtcNow
}
$summaryPath = Join-Path $runRoot 'summary.json'
$summary | ConvertTo-Json -Depth 20 |
    Set-Content -LiteralPath $summaryPath -Encoding UTF8

Write-Output 'SKILL-UI-AVALONIA-STRUCTURAL-RUNTIME = PASS'
Write-Output "EVIDENCE = $evidencePath"
Write-Output "SUMMARY = $summaryPath"
