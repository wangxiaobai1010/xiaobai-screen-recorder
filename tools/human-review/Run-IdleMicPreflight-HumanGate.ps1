[CmdletBinding()]
param(
    [ValidateRange(1, 120)]
    [int]$Seconds = 20,

    # Empty means the product's persisted selection. A display-name fragment
    # such as Kakaboom or OSK218 selects the matching product catalog entry.
    [string]$Microphone = '',

    # Kakaboom is Nonzero. OSK218 is NearSilent under the frozen real-device
    # behavior. These are TEST-ONLY observations; no product threshold changes.
    [ValidateSet('Nonzero', 'NearSilent')]
    [string]$Expectation = 'Nonzero',

    # Automation-only proof that the readout opens and receives level messages;
    # the real human command intentionally requires a nonzero spoken level.
    [switch]$Smoke
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$expectedHead = 'b23c6b2bb65f6dc92b36c35699f7a8233a39bce0'
$expectedBranch = 'integration/mic-prerecord-meter-v1'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$testExe = Join-Path $repoRoot `
    'artifacts\bin\Release\x64\XbPreview.GStreamer.Tests.exe'
$testSource = Join-Path $repoRoot `
    'XbPreview.GStreamer.Tests\GStreamerAudioCoreTests.cpp'
$settingsPath = Join-Path $env:LOCALAPPDATA `
    'XbPreview\settings\microphone-selection.json'

function Require([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}

Push-Location $repoRoot
try {
    $worktree = (git rev-parse --show-toplevel).Trim()
    $branch = (git branch --show-current).Trim()
    $head = (git rev-parse HEAD).Trim()
    Require ($branch -eq $expectedBranch) `
        "Expected branch $expectedBranch; received $branch."
    Require ($head -eq $expectedHead) `
        "Expected foundation HEAD $expectedHead; received $head."
    git diff --quiet HEAD -- XbPreview.Native XbPreview.Host XbPreview.Avalonia
    Require ($LASTEXITCODE -eq 0) `
        'Production Native/Host/Avalonia files differ from the foundation HEAD.'
    Require (Test-Path -LiteralPath $testExe -PathType Leaf) `
        "Test executable is missing: $testExe"
    Require ((Get-Item -LiteralPath $testExe).LastWriteTimeUtc -ge
        (Get-Item -LiteralPath $testSource).LastWriteTimeUtc) `
        'Test executable is older than the TEST-ONLY live readout source; rebuild it first.'

    $selector = $Microphone.Trim()
    if ([string]::IsNullOrWhiteSpace($selector)) {
        $selector = 'windows-default'
        if (Test-Path -LiteralPath $settingsPath -PathType Leaf) {
            try {
                $saved = Get-Content -LiteralPath $settingsPath -Raw |
                    ConvertFrom-Json
                if ($saved.Selection -eq 'concrete-endpoint' -and
                    -not [string]::IsNullOrWhiteSpace($saved.EndpointId)) {
                    $selector = [string]$saved.EndpointId
                }
            }
            catch {
                Write-Warning `
                    'Saved microphone selection is unreadable; using Windows default.'
            }
        }
    }

    $displayCommand = if ([string]::IsNullOrWhiteSpace($Microphone)) {
        ".\tools\human-review\Run-IdleMicPreflight-HumanGate.ps1 -Seconds $Seconds"
    }
    else {
        ".\tools\human-review\Run-IdleMicPreflight-HumanGate.ps1 -Microphone `"$Microphone`" -Seconds $Seconds -Expectation $Expectation"
    }
    $arguments = @('--mic-preflight-live', $Seconds, $selector)
    if (-not $Smoke) {
        if ($Expectation -eq 'Nonzero') {
            $arguments += '--require-nonzero'
        }
        else {
            $arguments += '--expect-near-silent'
        }
    }

    Write-Host "WORKTREE = $worktree" -ForegroundColor Cyan
    Write-Host "BRANCH = $branch"
    Write-Host "HEAD = $head"
    Write-Host "RUN COMMAND = powershell.exe -NoProfile -ExecutionPolicy Bypass -File $displayCommand"
    Write-Host "TEST COMMAND = $testExe $($arguments -join ' ')"
    Write-Host 'RECORDING STARTED = NO'
    if ($Smoke) {
        Write-Host 'SUCCESS SIGN = Level messages arrive; final marker is IDLE_MIC_PREFLIGHT_READOUT_SMOKE_PASS.' -ForegroundColor Yellow
    }
    elseif ($Expectation -eq 'Nonzero') {
        Write-Host 'SUCCESS SIGN = Speak normally; peak_pcm16 and rms_pcm16 become nonzero; final marker is IDLE_MIC_PREFLIGHT_HUMAN_PASS.' -ForegroundColor Yellow
    }
    else {
        Write-Host 'SUCCESS SIGN = Device is enumerated and inactive input remains near silent (peak <= 32, RMS <= 16 PCM16); final marker is IDLE_MIC_PREFLIGHT_NEAR_SILENT_PASS.' -ForegroundColor Yellow
    }
    Write-Host ''

    & $testExe @arguments
    Require ($LASTEXITCODE -eq 0) `
        "Idle Mic Preflight human readout failed with exit $LASTEXITCODE."
}
finally {
    Pop-Location
}
