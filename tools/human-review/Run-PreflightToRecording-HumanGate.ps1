[CmdletBinding()]
param(
    [ValidateRange(10, 15)]
    [int]$RecordingSeconds = 12,

    # Validates the candidate, executable, output path contract and prints the
    # complete human procedure without launching the GUI.
    [switch]$PreflightOnly
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$expectedHead = 'b23c6b2bb65f6dc92b36c35699f7a8233a39bce0'
$expectedBranch = 'integration/mic-prerecord-meter-v1'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$hostExe = Join-Path $repoRoot 'artifacts\bin\Release\x64\XbPreview.Host.exe'
$defaultOutputRoot = Join-Path $repoRoot 'artifacts\p2.5a-recordings'
$productSettingsPath = Join-Path $env:LOCALAPPDATA `
    'XbPreview\settings\product-settings.json'

function Require([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}

function Get-OutputRoots {
    $roots = [Collections.Generic.List[string]]::new()
    $roots.Add([IO.Path]::GetFullPath($defaultOutputRoot))
    if (Test-Path -LiteralPath $productSettingsPath -PathType Leaf) {
        try {
            $document = Get-Content -LiteralPath $productSettingsPath -Raw |
                ConvertFrom-Json
            $configured = [string]$document.Settings.OutputRoot
            if (-not [string]::IsNullOrWhiteSpace($configured) -and
                [IO.Path]::IsPathFullyQualified($configured) -and
                -not $roots.Contains($configured)) {
                $roots.Add([IO.Path]::GetFullPath($configured))
            }
        }
        catch {
            Write-Warning 'Product output setting is unreadable; scanning the default root.'
        }
    }
    return $roots
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
    Require (Test-Path -LiteralPath $hostExe -PathType Leaf) `
        "Release x64 product Host is missing: $hostExe"

    Write-Host "WORKTREE = $worktree" -ForegroundColor Cyan
    Write-Host "BRANCH = $branch"
    Write-Host "HEAD = $head"
    Write-Host 'RUN COMMAND = powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\human-review\Run-PreflightToRecording-HumanGate.ps1'
    Write-Host "TEST COMMAND = $hostExe --module1-capture-review"
    Write-Host "RECORDING DURATION = $RecordingSeconds seconds per microphone"
    Write-Host "DEFAULT MP4 ROOT = $defaultOutputRoot"
    Write-Host 'HOW TO OPEN = After each Stop completes, click Open Video in the Recording panel. The script also prints both absolute MP4 paths.'
    Write-Host ''
    Write-Host 'HUMAN STEPS (keep this exact order):' -ForegroundColor Yellow
    Write-Host '1. Entry A1: Kakaboom must show nonzero spoken levels and pass REQUIRE-NONZERO. Let Entry A1 exit so it releases the device.'
    Write-Host '2. Entry A2: OSK218 must be enumerated and its inactive input must remain at zero or near zero; it must not be required to produce a nonzero level. Let Entry A2 exit.'
    Write-Host '3. In Panel 1, turn System Sound OFF, select Kakaboom, and turn Microphone ON. Wait 3 seconds for Idle Preflight.'
    Write-Host "4. Click START, speak normally for $RecordingSeconds seconds, click STOP, wait for completion, then click Open Video and listen."
    Write-Host '5. Return to Panel 1, select OSK218, keep Microphone ON, and wait 3 seconds for the idle A-to-B rebind.'
    Write-Host "6. Click START again, observe/record for $RecordingSeconds seconds, click STOP, wait for completion, then click Open Video and listen."
    Write-Host '7. After listening to both files, close the Xiaobai Screen Recorder window.'
    Write-Host 'SUCCESS SIGN = Both final MP4 files publish and open; Kakaboom voice is normal with no new electrical noise; OSK218 preserves its frozen near-silent behavior with no new electronic/electrical noise. OSK218 is not required to record normal speech.' -ForegroundColor Yellow
    Write-Host ''

    if ($PreflightOnly) {
        Write-Host 'PREFLIGHT_TO_RECORDING_ENTRY_PREFLIGHT_PASS' -ForegroundColor Green
        return
    }

    $started = Get-Date
    $rootsBefore = @(Get-OutputRoots)
    $process = Start-Process -FilePath $hostExe `
        -ArgumentList '--module1-capture-review' -PassThru
    $process.WaitForExit()
    Require ($process.ExitCode -eq 0) `
        "Product Host exited with code $($process.ExitCode)."

    $roots = @($rootsBefore + @(Get-OutputRoots) | Select-Object -Unique)
    $videos = @($roots |
        Where-Object { Test-Path -LiteralPath $_ -PathType Container } |
        ForEach-Object {
            Get-ChildItem -LiteralPath $_ -Filter *.mp4 -File -Recurse `
                -ErrorAction SilentlyContinue
        } |
        Where-Object LastWriteTime -ge $started |
        Sort-Object LastWriteTime)
    Require ($videos.Count -ge 2) `
        "Expected two newly published MP4 files; found $($videos.Count)."

    $kakaboomVideo = $videos[0]
    $osk218Video = $videos[1]
    Write-Host "KAKABOOM FINAL MP4 = $($kakaboomVideo.FullName)" `
        -ForegroundColor Green
    Write-Host "OSK218 FINAL MP4 = $($osk218Video.FullName)" `
        -ForegroundColor Green
    Write-Host "OPEN KAKABOOM = Start-Process -FilePath `"$($kakaboomVideo.FullName)`""
    Write-Host "OPEN OSK218 = Start-Process -FilePath `"$($osk218Video.FullName)`""

    $kakaboomResult = Read-Host `
        'Kakaboom final MP4 voice normal and no new electrical noise? Type PASS'
    $osk218Result = Read-Host `
        'OSK218 final MP4 preserves frozen near-silence with no new electronic/electrical noise? Type PASS'
    Require ($kakaboomResult.Trim() -eq 'PASS' -and
        $osk218Result.Trim() -eq 'PASS') `
        'The human audio-quality gate did not receive both PASS answers.'
    Write-Host 'PREFLIGHT_TO_RECORDING_HUMAN_PASS' -ForegroundColor Green
}
finally {
    Pop-Location
}
