[CmdletBinding()]
param(
    [ValidateRange(3, 15)]
    [int]$EndToEndSeconds = 5
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repository = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$artifacts = Join-Path $repository 'artifacts'
$output = Join-Path $artifacts 'bin\Release\x64'
$testExe = Join-Path $output 'XbPreview.GStreamer.Tests.exe'
$managedTests = Join-Path $output 'XbPreview.Managed.Tests.exe'
$longRun = Join-Path $output 'XbPreview.LongRun.exe'
$gateRoot = Join-Path $artifacts 'gate\gstreamer-mic-selector'
$package = Join-Path $artifacts 'package\win-x64'
$vswhere = 'C:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe'

function Assert-True([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}

function Assert-LastExitCode([string]$Message) {
    if ($LASTEXITCODE -ne 0) { throw "$Message (exit $LASTEXITCODE)." }
}

function Write-Gate([string]$Name) {
    Write-Host "`n=== $Name ==="
}

Write-Gate 'Clean preflight, exact 4fc3757 baseline ancestry, and Release x64'
$dirty = @(git -C $repository status --porcelain --untracked-files=all)
Assert-LastExitCode 'git status failed'
Assert-True ($dirty.Count -eq 0) 'Lifecycle gate requires a clean worktree.'
git -C $repository merge-base --is-ancestor `
    4fc3757651ef9396eb89f1ad69e19d3dc71be0da HEAD
Assert-LastExitCode 'Candidate is not based on exact human-PASS commit 4fc3757'
$msbuild = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild `
    -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
Assert-True (-not [string]::IsNullOrWhiteSpace($msbuild)) 'MSBuild was not found.'
& $msbuild (Join-Path $repository 'XbPreview.P1D-A1.sln') `
    /t:Build /p:Configuration=Release /p:Platform=x64 /m /v:minimal
Assert-LastExitCode 'Release x64 build failed'

Write-Gate 'Frozen audio and concrete-device contracts'
& $testExe --contract
Assert-LastExitCode 'GStreamer contract gate failed'
foreach ($frozen in @(
    @('system', 'C7F3248FF3F9C76F4BC30E2B692D974C6C17CCB1E606A959B833B4454886DC47'),
    @('microphone', '88052152E93D9D0424C10684E69B649E3B51859BDE1E97BB2C09BF8B6C41FA03'),
    @('dual', '4EBBA5A6B1EA177B34B3CCA8E6120A8AEB140B7E1A6FDF67B9FF9BB8E3F1D368'))) {
    $description = (& $testExe "--describe-$($frozen[0])") -join "`n"
    Assert-LastExitCode "$($frozen[0]) description probe failed"
    $sha256 = [Security.Cryptography.SHA256]::Create()
    try {
        $hash = ([BitConverter]::ToString($sha256.ComputeHash(
            [Text.Encoding]::UTF8.GetBytes($description)))).Replace('-', '')
    }
    finally {
        $sha256.Dispose()
    }
    Assert-True ($hash -eq $frozen[1]) `
        "$($frozen[0]) frozen pipeline changed: $hash."
}
$coreSource = Get-Content -LiteralPath `
    (Join-Path $repository 'XbPreview.Native\GStreamerAudioCore.cpp') `
    -Raw -Encoding UTF8
Assert-True ($coreSource.Contains(
        'micDevice->CreateElement("mic_source")') -and
    $coreSource.Contains('config.microphoneDevice->EndpointId()') -and
    -not $coreSource.Contains('setString("mic_source", "device"')) `
    'Formal microphone path is not exclusively bound through concrete GstDevice create_element.'
$monitorSource = Get-Content -LiteralPath `
    (Join-Path $repository 'XbPreview.Native\GStreamerMicrophoneDeviceMonitor.cpp') `
    -Raw -Encoding UTF8
Assert-True ($monitorSource.Contains(
        'gst_device_monitor_add_filter(') -and
    $monitorSource.Contains('"Audio/Source"') -and
    $monitorSource.Contains('GST_MESSAGE_DEVICE_ADDED') -and
    $monitorSource.Contains('GST_MESSAGE_DEVICE_REMOVED') -and
    $monitorSource.Contains('gst_device_create_element(device_, name)')) `
    'Official GstDeviceMonitor/GstDevice hotplug adapter is incomplete.'
$uiSource = Get-Content -LiteralPath `
    (Join-Path $repository 'XbPreview.Host\MainForm.cs') -Raw -Encoding UTF8
Assert-True ($uiSource.Contains('MicrophoneDeviceChoice') -and
    $uiSource.Contains('_microphoneDeviceStatusLabel.Text') -and
    $uiSource.Contains('status.Available') -and
    $uiSource.Contains('MicrophoneSelectionSettings.Save') -and
    $uiSource.Contains('MicrophoneAvailabilityContract.UserMessage')) `
    'Formal UI does not expose selection, actual device, or unavailable state.'
Assert-True (-not [regex]::IsMatch(
    $coreSource,
    'gain-control=|compression-gain-db=|target-level-dbfs=|agate|expander|acompressor|volume\s*=',
    'IgnoreCase')) 'A frozen DSP/mastering parameter was changed in the capture core.'
& $managedTests --mvp-audio-gstreamer
Assert-LastExitCode 'Managed single-audio-owner contract failed'
& $managedTests --microphone-selector-abi
Assert-LastExitCode 'Managed/native microphone selector ABI gate failed'

Write-Gate 'Real connected GstDevice, removal seam, and next-Start re-probe'
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss-fff'
$lifecycleDirectory = Join-Path $gateRoot "device-$stamp"
New-Item -ItemType Directory -Force -Path $lifecycleDirectory | Out-Null
& $testExe --microphone-device-lifecycle $lifecycleDirectory 1
Assert-LastExitCode 'Concrete GstDevice lifecycle seam failed'

Write-Gate 'Real catalog, exact create_element identity, explicit A refusal, and endpoint switch when present'
$selectorDirectory = Join-Path $gateRoot "selector-$stamp"
$selectorOutput = @(& $testExe --microphone-selector $selectorDirectory 1)
Assert-LastExitCode 'Concrete microphone selector gate failed'
$selectorOutput | Set-Content -LiteralPath `
    (Join-Path $selectorDirectory 'selector-output.txt') -Encoding UTF8
Assert-True (($selectorOutput -join "`n").Contains(
    'GStreamer microphone selector PASS') -and
    ($selectorOutput -join "`n") -match 'devices=[1-9][0-9]*') `
    'Selector output does not prove a real current device catalog.'
$selectorText = $selectorOutput -join "`n"
Assert-True ($selectorText -match 'firstId="([^"]+)"') `
    'Selector output does not expose the exact selected endpoint ID.'
$selectedEndpointId = $Matches[1]

Write-Gate 'MicOnly Start/Stop, MP4 validation, H.264/AAC, and P2.6 publish'
$runDirectory = Join-Path $gateRoot "e2e-$stamp"
$summary = Join-Path $runDirectory 'summary.json'
$snapshots = Join-Path $runDirectory 'snapshots.jsonl'
New-Item -ItemType Directory -Force -Path $runDirectory | Out-Null
$previousMode = $env:XB_PREVIEW_RECORDING_AUDIO_SOURCE
$previousEndpoint = $env:XB_PREVIEW_TEST_MICROPHONE_ENDPOINT_ID
$env:XB_PREVIEW_RECORDING_AUDIO_SOURCE = 'microphone'
$env:XB_PREVIEW_TEST_MICROPHONE_ENDPOINT_ID = $selectedEndpointId
try {
    & $longRun `
        --duration-seconds $EndToEndSeconds `
        --sample-interval-ms 250 `
        --output-directory $runDirectory `
        --run-id "gst-mic-device-$stamp" `
        --summary-json $summary `
        --snapshots-jsonl $snapshots
    $longRunExit = $LASTEXITCODE
}
finally {
    if ($null -eq $previousMode) {
        Remove-Item Env:XB_PREVIEW_RECORDING_AUDIO_SOURCE -ErrorAction SilentlyContinue
    }
    else {
        $env:XB_PREVIEW_RECORDING_AUDIO_SOURCE = $previousMode
    }
    if ($null -eq $previousEndpoint) {
        Remove-Item Env:XB_PREVIEW_TEST_MICROPHONE_ENDPOINT_ID -ErrorAction SilentlyContinue
    }
    else {
        $env:XB_PREVIEW_TEST_MICROPHONE_ENDPOINT_ID = $previousEndpoint
    }
}
$facts = Get-Content -LiteralPath $summary -Raw -Encoding UTF8 | ConvertFrom-Json
$knownWgcEnvironmentBlock = $longRunExit -ne 0 -and
    (($facts.Reasons -join "`n").Contains('0x80070424'))
if ($longRunExit -ne 0) {
    Assert-True $knownWgcEnvironmentBlock `
        "Focused MicOnly end-to-end failed outside the known WGC service environment block (exit $longRunExit)."
    Write-Warning 'Product E2E was blocked before Recording by existing WGC 0x80070424; direct real MicOnly selector capture passed.'
}
else {
    Assert-True ($facts.Verdict -eq 'PASS' -and
        $facts.TerminalSnapshot.OutputSuccess -and
        $facts.TerminalSnapshot.Published -and
        $facts.TerminalSnapshot.FinalizeCount -eq 1 -and
        $facts.TerminalSnapshot.ValidationHResult -ge 0 -and
        $facts.TerminalSnapshot.ResidualOutstanding -eq 0 -and
        $facts.SourceReaderValidation -eq 'PASS') `
        'MicOnly did not complete validated P2.6 safe publish.'
    $ffprobe = Join-Path $artifacts `
        'audio-v2-toolchain\ffmpeg-8.1-lgpl-shared\ffmpeg-n8.1-latest-win64-lgpl-shared-8.1\bin\ffprobe.exe'
    $streams = (& $ffprobe -v error -show_entries `
        'stream=codec_type,codec_name,sample_rate,channels' -of json -- `
        $facts.TerminalSnapshot.PublishedPath | ConvertFrom-Json).streams
    Assert-LastExitCode 'ffprobe failed for focused MicOnly output'
    $video = @($streams | Where-Object codec_type -eq 'video')
    $audio = @($streams | Where-Object codec_type -eq 'audio')
    Assert-True ($video.Count -eq 1 -and $video[0].codec_name -eq 'h264' -and
        $audio.Count -eq 1 -and $audio[0].codec_name -eq 'aac' -and
        [int]$audio[0].sample_rate -eq 48000 -and [int]$audio[0].channels -eq 2) `
        'Focused MicOnly output is not H.264 plus AAC 48 kHz stereo.'
    $encoderLog = Get-ChildItem -LiteralPath $runDirectory -Recurse -File `
        -Filter 'p2.4-encoder-*.jsonl' | Select-Object -First 1
    Assert-True ($null -ne $encoderLog) 'Encoder diagnostics were not found.'
    $diagnosticLines = @(Get-Content -LiteralPath $encoderLog.FullName -Encoding UTF8)
    Assert-True ($diagnosticLines.Count -gt 0) 'Encoder diagnostics are empty.'
    $diagnostics = $diagnosticLines[$diagnosticLines.Count - 1] | ConvertFrom-Json
    Assert-True ($diagnostics.GStreamerMicrophoneSessionBound -eq 1 -and
        $diagnostics.GStreamerMicrophoneSourceCreatedFromDevice -eq 1 -and
        $diagnostics.GStreamerMicrophoneElementIdentityMatches -eq 1 -and
        $diagnostics.GStreamerMicrophoneDeviceId -eq
            $diagnostics.GStreamerMicrophoneElementDeviceId -and
        $diagnostics.GStreamerMicrophoneDeviceId -eq $selectedEndpointId -and
        -not [string]::IsNullOrWhiteSpace(
            [string]$diagnostics.GStreamerMicrophoneDeviceId) -and
        -not [string]::IsNullOrWhiteSpace(
            [string]$diagnostics.GStreamerMicrophoneDeviceDisplayName) -and
        -not [string]::IsNullOrWhiteSpace(
            [string]$diagnostics.GStreamerMicrophoneDeviceProperties)) `
        'Formal recording diagnostics do not prove concrete GstDevice binding.'
}

Write-Gate 'Formal package and legacy runtime closure'
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File `
    (Join-Path $PSScriptRoot 'New-MvpAudioGStreamerPackage.ps1')
Assert-LastExitCode 'Formal package generation failed'
$manifest = Get-Content -LiteralPath `
    (Join-Path $package 'package-manifest.json') -Raw -Encoding UTF8 | ConvertFrom-Json
Assert-True ($manifest.audioRuntime -eq 'GStreamerOnly' -and
    $manifest.gstreamer.version -eq '1.28.6' -and
    $manifest.microphoneDeviceBinding.sourceCreation -eq
        'gst_device_create_element' -and
    $manifest.microphoneDeviceBinding.enumeration -eq
        'GstDeviceMonitor Audio/Source' -and
    $manifest.microphoneDeviceBinding.requiresConcreteDeviceAtStart -eq $true -and
    $manifest.microphoneDeviceBinding.defaultSourceFallback -eq $false -and
    $manifest.microphoneDeviceBinding.arbitraryCaptureEndpointFallback -eq $false -and
    $manifest.microphoneDeviceBinding.deviceEnumerationReimplemented -eq $false -and
    $manifest.microphoneDeviceBinding.wasapiReimplemented -eq $false -and
    $manifest.microphoneDeviceBinding.hotplugReimplemented -eq $false -and
    $manifest.microphoneDeviceBinding.hotReconnect -eq $false -and
    $manifest.microphoneDeviceBinding.perUserSettingsPackaged -eq $false -and
    $manifest.deployment.globalPathRequired -eq $false -and
    $manifest.deployment.globalGstPluginPathRequired -eq $false -and
    $manifest.deployment.developerAbsolutePathsPresent -eq $false) `
    'Package device binding or self-contained deployment contract failed.'
foreach ($legacy in @('NAudio', 'SoundFlow', 'miniaudioProductRuntime',
        'oldAudioV3V4', 'ffmpegAgateAmixSpeechPatch')) {
    Assert-True ($manifest.legacyAudioRuntime.$legacy -eq 'ABSENT') `
        "Legacy runtime $legacy is not ABSENT."
}
foreach ($startCount in $manifest.legacyAudioRuntime.startCounts.PSObject.Properties) {
    Assert-True ([int]$startCount.Value -eq 0) `
        "Legacy runtime $($startCount.Name) Start count is not zero."
}
$legacyFiles = @(Get-ChildItem -LiteralPath $package -Recurse -File |
    Where-Object Name -Match 'NAudio|SoundFlow|MiniaudioLoopback|miniaudio|AudioV[34]')
Assert-True ($legacyFiles.Count -eq 0) 'Legacy audio files are present in package.'
$packagedSettings = @(Get-ChildItem -LiteralPath $package -Recurse -File |
    Where-Object Name -Match 'microphone-selection|selected-microphone|endpoint-id')
Assert-True ($packagedSettings.Count -eq 0) `
    'A per-user or development-machine microphone setting was packaged.'
$wasapiEndpointPattern = '\{0\.0\.1\.00000000\}\.\{[0-9A-Fa-f-]{36}\}'
$packagedEndpointHits = @()
foreach ($file in Get-ChildItem -LiteralPath $package -File |
        Where-Object Name -Like 'XbPreview.*') {
    $bytes = [IO.File]::ReadAllBytes($file.FullName)
    if ([Text.Encoding]::ASCII.GetString($bytes) -match
            $wasapiEndpointPattern -or
        [Text.Encoding]::Unicode.GetString($bytes) -match
            $wasapiEndpointPattern) {
        $packagedEndpointHits += $file.FullName
    }
}
Assert-True ($packagedEndpointHits.Count -eq 0) `
    'A concrete development-machine WASAPI endpoint ID was compiled into the package.'
$badHashes = @()
foreach ($file in $manifest.files) {
    $path = Join-Path $package $file.path
    if (-not (Test-Path -LiteralPath $path) -or
        (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne
            $file.sha256) {
        $badHashes += $file.path
    }
}
Assert-True ($badHashes.Count -eq 0) 'Package manifest hash closure failed.'

Write-Gate 'Final clean-worktree check'
$finalDirty = @(git -C $repository status --porcelain --untracked-files=all)
Assert-LastExitCode 'final git status failed'
Assert-True ($finalDirty.Count -eq 0) 'Lifecycle gate changed the worktree.'
Write-Host 'MVP-AUDIO-GSTREAMER-MIC-SELECTOR-HUMAN-GATE-READY'
