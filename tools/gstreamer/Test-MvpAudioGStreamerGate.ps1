[CmdletBinding()]
param(
    [ValidateRange(3, 30)]
    [int]$CaptureSeconds = 4,
    [ValidateRange(5, 60)]
    [int]$EndToEndSeconds = 8
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repository = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$artifacts = Join-Path $repository 'artifacts'
$gateRoot = Join-Path $artifacts 'gate\gstreamer-audio'
$systemSoundFixture = Join-Path $gateRoot "smooth-system-smoke-$PID.wav"
$package = Join-Path $artifacts 'package\win-x64'
$configuration = 'Release'
$output = Join-Path $artifacts "bin\$configuration\x64"
$testExe = Join-Path $output 'XbPreview.GStreamer.Tests.exe'
$managedTests = Join-Path $output 'XbPreview.Managed.Tests.exe'
$longRun = Join-Path $output 'XbPreview.LongRun.exe'
$ffmpegRoot = Join-Path $artifacts 'audio-v2-toolchain\ffmpeg-8.1-lgpl-shared\ffmpeg-n8.1-latest-win64-lgpl-shared-8.1'
$ffprobe = Join-Path $ffmpegRoot 'bin\ffprobe.exe'
$ffmpeg = Join-Path $ffmpegRoot 'bin\ffmpeg.exe'
$sdk = Join-Path $artifacts 'sdk\gstreamer-1.28.6'
$gstInspect = Join-Path $sdk 'bin\gst-inspect-1.0.exe'
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

function Get-MediaStreams([string]$Path) {
    $json = & $ffprobe -v error -show_entries 'stream=codec_type,codec_name,sample_rate,channels,duration' -of json -- $Path
    Assert-LastExitCode "ffprobe failed for $Path"
    return ($json | ConvertFrom-Json).streams
}

function Get-LoudnessFacts([string]$Path) {
    $previousErrorAction = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        $lines = @(& $ffmpeg -nostdin -hide_banner -loglevel info -i $Path `
            -map '0:a:0' -af 'loudnorm=I=-16:TP=-3.0:LRA=7:print_format=json' `
            -f null NUL 2>&1)
        $ffmpegExitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previousErrorAction
    }
    Assert-True ($ffmpegExitCode -eq 0) `
        "FFmpeg loudness validation failed for $Path (exit $ffmpegExitCode)."
    $text = $lines -join "`n"
    $matches = [regex]::Matches($text, '(?ms)\{\s*"input_i"\s*:.*?\}')
    Assert-True ($matches.Count -gt 0) "FFmpeg returned no loudnorm JSON for $Path."
    $json = $matches[$matches.Count - 1].Value | ConvertFrom-Json
    $integrated = [double]::Parse(
        [string]$json.input_i, [Globalization.CultureInfo]::InvariantCulture)
    $truePeak = [double]::Parse(
        [string]$json.input_tp, [Globalization.CultureInfo]::InvariantCulture)
    Assert-True (-not [double]::IsNaN($integrated) -and
        -not [double]::IsInfinity($integrated) -and
        -not [double]::IsNaN($truePeak) -and
        -not [double]::IsInfinity($truePeak)) `
        "Non-finite loudness facts were reported for $Path."
    return [pscustomobject]@{
        IntegratedLufs = $integrated
        TruePeakDbtp = $truePeak
    }
}

function Start-SystemSound([int]$Seconds) {
    Assert-True (Test-Path -LiteralPath $systemSoundFixture) `
        'Smooth system-audio fixture is missing.'
    $fixtureLiteral = $systemSoundFixture.Replace("'", "''")
    $script = @"
`$player = New-Object System.Media.SoundPlayer '$fixtureLiteral'
`$player.Load()
`$until = [DateTime]::UtcNow.AddSeconds($Seconds)
while ([DateTime]::UtcNow -lt `$until) {
    `$player.PlaySync()
}
"@
    $encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($script))
    return Start-Process -FilePath 'powershell.exe' -ArgumentList @(
        '-NoProfile', '-ExecutionPolicy', 'Bypass', '-EncodedCommand', $encoded
    ) -WindowStyle Hidden -PassThru
}

function Invoke-EndToEnd([string]$Mode) {
    $stamp = Get-Date -Format 'yyyyMMdd-HHmmss-fff'
    $runId = "gst-$($Mode.ToLowerInvariant())-$stamp"
    $directory = Join-Path $gateRoot "e2e-$($Mode.ToLowerInvariant())-$stamp"
    $summary = Join-Path $directory 'summary.json'
    $snapshots = Join-Path $directory 'snapshots.jsonl'
    New-Item -ItemType Directory -Force -Path $directory | Out-Null

    $previousMode = $env:XB_PREVIEW_RECORDING_AUDIO_SOURCE
    $env:XB_PREVIEW_RECORDING_AUDIO_SOURCE = $Mode.ToLowerInvariant()
    $sound = Start-SystemSound ($EndToEndSeconds + 4)
    try {
        & $longRun `
            --duration-seconds $EndToEndSeconds `
            --sample-interval-ms 250 `
            --output-directory $directory `
            --run-id $runId `
            --summary-json $summary `
            --snapshots-jsonl $snapshots
        Assert-LastExitCode "End-to-end $Mode recording failed"
    }
    finally {
        if ($null -eq $previousMode) {
            Remove-Item Env:XB_PREVIEW_RECORDING_AUDIO_SOURCE -ErrorAction SilentlyContinue
        }
        else {
            $env:XB_PREVIEW_RECORDING_AUDIO_SOURCE = $previousMode
        }
        if (-not $sound.HasExited) {
            $sound.WaitForExit(5000) | Out-Null
        }
        $sound.Dispose()
    }

    $facts = Get-Content -LiteralPath $summary -Raw -Encoding UTF8 | ConvertFrom-Json
    Assert-True ($facts.Verdict -eq 'PASS') "$Mode summary verdict was not PASS."
    Assert-True ($facts.TerminalSnapshot.OutputSuccess -and
        $facts.TerminalSnapshot.Published -and
        $facts.TerminalSnapshot.FinalizeAttempted -and
        $facts.TerminalSnapshot.FinalizeHResult -ge 0 -and
        $facts.TerminalSnapshot.ValidationAttempted -and
        $facts.TerminalSnapshot.ValidationHResult -ge 0 -and
        $facts.TerminalSnapshot.ResidualOutstanding -eq 0) `
        "$Mode did not satisfy Finalize/Validate/Publish/residual-zero."

    $streams = @(Get-MediaStreams $facts.TerminalSnapshot.PublishedPath)
    $video = @($streams | Where-Object codec_type -eq 'video')
    $audio = @($streams | Where-Object codec_type -eq 'audio')
    Assert-True ($video.Count -eq 1 -and $video[0].codec_name -eq 'h264') `
        "$Mode output does not contain exactly one H.264 video stream."
    if ($Mode -eq 'None') {
        Assert-True ($audio.Count -eq 0) 'None unexpectedly produced an audio stream.'
    }
    else {
        Assert-True ($audio.Count -eq 1 -and
            $audio[0].codec_name -eq 'aac' -and
            [int]$audio[0].sample_rate -eq 48000 -and
            [int]$audio[0].channels -eq 2) `
            "$Mode output does not contain exactly one AAC 48 kHz stereo stream."
        if ($Mode -in @('Microphone', 'Dual')) {
            $loudness = Get-LoudnessFacts $facts.TerminalSnapshot.PublishedPath
            Assert-True ($loudness.TruePeakDbtp -le -1.5) `
                "$Mode final true peak exceeds -1.5 dBTP: $($loudness.TruePeakDbtp)."
            Write-Host "$Mode real-device loudness finite/TP PASS: I=$($loudness.IntegratedLufs) LUFS; TP=$($loudness.TruePeakDbtp) dBTP"
        }
    }
}

Write-Gate 'Preflight and clean-worktree reproducibility'
$dirty = @(git -C $repository status --porcelain --untracked-files=all)
Assert-LastExitCode 'git status failed'
Assert-True ($dirty.Count -eq 0) 'The GStreamer gate must run from a clean worktree.'
Assert-True (Test-Path -LiteralPath $vswhere) 'vswhere was not found.'
Assert-True (Test-Path -LiteralPath $ffprobe) 'Pinned ffprobe was not found.'
Assert-True (Test-Path -LiteralPath $ffmpeg) 'Pinned ffmpeg was not found.'
Assert-True (Test-Path -LiteralPath $gstInspect) 'Pinned gst-inspect was not found.'
New-Item -ItemType Directory -Force -Path $gateRoot | Out-Null
& $ffmpeg -nostdin -hide_banner -loglevel error -n `
    -f lavfi -i 'sine=frequency=440:sample_rate=48000:duration=1' `
    -ac 2 -c:a pcm_s16le $systemSoundFixture
Assert-LastExitCode 'Smooth system-audio fixture generation failed'

$msbuild = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
Assert-True (-not [string]::IsNullOrWhiteSpace($msbuild)) 'MSBuild was not found.'

Write-Gate 'Build and focused contracts (D/E/F/G/H/I)'
& $msbuild (Join-Path $repository 'XbPreview.P1D-A1.sln') /t:Build /p:Configuration=Release /p:Platform=x64 /m /v:minimal
Assert-LastExitCode 'Release solution build failed'
& $testExe --contract
Assert-LastExitCode 'GStreamer native contract gate failed'
$systemDescription = (& $testExe --describe-system) -join "`n"
Assert-LastExitCode 'SystemOnly description probe failed'
$sha256 = [Security.Cryptography.SHA256]::Create()
try {
    $systemHashBytes = $sha256.ComputeHash(
        [Text.Encoding]::UTF8.GetBytes($systemDescription))
}
finally {
    $sha256.Dispose()
}
$systemHash = ([BitConverter]::ToString($systemHashBytes)).Replace('-', '')
Assert-True ($systemHash -eq 'C7F3248FF3F9C76F4BC30E2B692D974C6C17CCB1E606A959B833B4454886DC47') `
    "SystemOnly pipeline contract hash changed: $systemHash."
& $managedTests --mvp-audio-gstreamer
Assert-LastExitCode 'Managed GStreamer ownership gate failed'
& $longRun --self-test-arguments
Assert-LastExitCode 'Long-run argument self-test failed'
& $longRun --self-test-evidence-gates
Assert-LastExitCode 'Long-run evidence self-test failed'
& $longRun --self-test-final-hardening
Assert-LastExitCode 'Long-run final-hardening self-test failed'

Write-Gate 'Static single-core and FFmpeg boundary'
$productFiles = Get-ChildItem -LiteralPath @(
    (Join-Path $repository 'XbPreview.Native'),
    (Join-Path $repository 'XbPreview.Host'),
    (Join-Path $repository 'XbPreview.LongRun'),
    (Join-Path $repository 'XbPreview.Managed.Tests')
) -Recurse -File | Where-Object Extension -in @('.h', '.cpp', '.cs', '.vcxproj', '.csproj')
$forbidden = '\bNAudio\b|\bSoundFlow\b|\bminiaudio\b|\bAudioCaptureSource\b|\bAudioProgramMixer\b|\bAudioTimeline\b|\bAudioSidecar\w*\b|\bSystemAudioCapture\b|\bMicrophoneCaptureProcessingProfile\b|\bMicNoiseDiagnosticTap\b|\bAudioV2\w*\b|\bAudioV3\w*\b'
$legacyHits = @($productFiles | Select-String -Pattern $forbidden -CaseSensitive:$false)
Assert-True ($legacyHits.Count -eq 0) ('Legacy audio runtime references remain: ' + ($legacyHits -join '; '))
$legacyThirdPartyFiles = @(
    Get-ChildItem -LiteralPath @(
        (Join-Path $repository 'third_party\miniaudio'),
        (Join-Path $repository 'third_party\libflac')
    ) -Recurse -File -ErrorAction SilentlyContinue
)
Assert-True ($legacyThirdPartyFiles.Count -eq 0) `
    'Legacy miniaudio/custom-libFLAC files still exist.'

$finalizerSource = Get-Content -LiteralPath (Join-Path $repository 'XbPreview.Native\GStreamerAudioFinalizer.cpp') -Raw -Encoding UTF8
foreach ($required in @(
    'L"0:v:0"', 'L"1:a:0"', 'L"copy"', 'L"aac"', 'L"192k"',
    'L"48000"', 'L"2"', 'L"-shortest"', 'L"+faststart"',
    'L"loudnorm=I=-16:TP=-3.0:LRA=7"',
    'measured_I=', 'measured_TP=', 'measured_LRA=', 'measured_thresh=',
    'offset=', 'linear=true',
    "amix=inputs=2:weights='1 1':normalize=1"
)) {
    Assert-True $finalizerSource.Contains($required) "FFmpeg boundary is missing $required."
}
$systemFinalizeBlock = [regex]::Match(
    $finalizerSource,
    '(?s)case GStreamerAudioMode::SystemOnly:(.*?)case GStreamerAudioMode::MicrophoneOnly:').Groups[1].Value
Assert-True (-not [regex]::IsMatch(
    $systemFinalizeBlock, 'loudnorm|amix|filter_complex|L"-af"', 'IgnoreCase')) `
    'SystemOnly finalization unexpectedly contains loudness or mixing filters.'
Assert-True (-not [regex]::IsMatch(
    $finalizerSource,
    'agate|expander|acompressor|compand|noisegate|volume\s*=|0\.65|0\.8',
    'IgnoreCase')) 'A forbidden custom/legacy audio filter or attenuation was found.'

Write-Gate 'Deterministic FFmpeg two-pass mastering seam'
$fixtureStamp = Get-Date -Format 'yyyyMMdd-HHmmss-fff'
$fixture = Join-Path $gateRoot "mastering-fixture-$fixtureStamp"
New-Item -ItemType Directory -Force -Path $fixture | Out-Null
$fixtureVideo = Join-Path $fixture 'video.mp4'
$fixtureMic = Join-Path $fixture 'mic.flac'
$fixtureSystem = Join-Path $fixture 'system.flac'
& $ffmpeg -nostdin -hide_banner -loglevel error -n `
    -f lavfi -i 'color=c=black:s=640x360:r=30:d=4' `
    -an -c:v libopenh264 $fixtureVideo
Assert-LastExitCode 'H.264 mastering fixture generation failed'
& $ffmpeg -nostdin -hide_banner -loglevel error -n `
    -f lavfi -i 'sine=frequency=220:sample_rate=48000:duration=4' `
    -c:a flac $fixtureMic
Assert-LastExitCode 'Microphone FLAC mastering fixture generation failed'
& $ffmpeg -nostdin -hide_banner -loglevel error -n `
    -f lavfi -i 'sine=frequency=660:sample_rate=48000:duration=4' `
    -ac 2 -c:a flac $fixtureSystem
Assert-LastExitCode 'System FLAC mastering fixture generation failed'

$fixtureMicOutput = Join-Path $fixture 'microphone-final.mp4'
$fixtureMicJson = (& $testExe --finalize-fixture microphone `
    $fixtureVideo '-' $fixtureMic $fixtureMicOutput 40000000) -join "`n"
Assert-LastExitCode 'Microphone two-pass finalizer fixture failed'
$fixtureMicFacts = $fixtureMicJson | ConvertFrom-Json
Assert-True ($fixtureMicFacts.hresult -ge 0 -and
    $fixtureMicFacts.validationHResult -ge 0 -and
    $fixtureMicFacts.microphoneMastering -eq 1 -and
    $fixtureMicFacts.dualMix -eq 0 -and
    $fixtureMicFacts.loudnessValidated -eq 1 -and
    $fixtureMicFacts.integratedLufs -ge -17.0 -and
    $fixtureMicFacts.integratedLufs -le -15.0 -and
    $fixtureMicFacts.truePeakDbtp -le -1.5) `
    'Microphone deterministic mastering did not reach the fixed product target.'

$fixtureDualOutput = Join-Path $fixture 'dual-final.mp4'
$fixtureDualJson = (& $testExe --finalize-fixture dual `
    $fixtureVideo $fixtureSystem $fixtureMic $fixtureDualOutput 40000000) -join "`n"
Assert-LastExitCode 'Dual two-pass finalizer fixture failed'
$fixtureDualFacts = $fixtureDualJson | ConvertFrom-Json
Assert-True ($fixtureDualFacts.hresult -ge 0 -and
    $fixtureDualFacts.validationHResult -ge 0 -and
    $fixtureDualFacts.microphoneMastering -eq 1 -and
    $fixtureDualFacts.dualMix -eq 1 -and
    $fixtureDualFacts.loudnessValidated -eq 1 -and
    $fixtureDualFacts.integratedLufs -ge -17.0 -and
    $fixtureDualFacts.integratedLufs -le -15.0 -and
    $fixtureDualFacts.truePeakDbtp -le -1.5) `
    'Dual deterministic mastering did not reach the fixed product target.'

Write-Gate 'Private package and license closure (J)'
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'New-MvpAudioGStreamerPackage.ps1')
Assert-LastExitCode 'Private package construction failed'
$manifestPath = Join-Path $package 'package-manifest.json'
$manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
Assert-True ($manifest.gstreamer.version -eq '1.28.6' -and
    $manifest.gstreamer.distribution -eq 'MSVC x86_64') 'Package GStreamer pin is not exact.'
Assert-True ($manifest.audioRuntime -eq 'GStreamerOnly' -and
    $manifest.ffmpeg.audioFilters -eq $true -and
    $manifest.ffmpeg.mastering.microphone -eq
        'two-pass loudnorm I=-16:TP=-3.0:LRA=7 before AAC' -and
    $manifest.ffmpeg.mastering.dualMix -eq
        "amix weights='1 1' normalize=1, then two-pass loudnorm I=-16:TP=-3.0:LRA=7 before AAC" -and
    $manifest.ffmpeg.mastering.systemOnly -eq
        'no loudness filter; direct AAC encode/mux' -and
    $manifest.ffmpeg.mastering.finalDecodedMp4TruePeakMaximumDbtp -eq -1.5 -and
    $manifest.ffmpeg.mastering.customDsp -eq $false -and
    $manifest.ffmpeg.mastering.agate -eq $false -and
    $manifest.ffmpeg.mastering.expander -eq $false) `
    'Package FFmpeg mastering policy is not exact.'
Assert-True ($manifest.legacyAudioRuntime.NAudio -eq 'ABSENT' -and
    $manifest.legacyAudioRuntime.SoundFlow -eq 'ABSENT' -and
    $manifest.legacyAudioRuntime.miniaudioProductRuntime -eq 'ABSENT' -and
    $manifest.legacyAudioRuntime.oldAudioV3V4 -eq 'ABSENT' -and
    $manifest.legacyAudioRuntime.ffmpegAgateAmixSpeechPatch -eq 'ABSENT' -and
    $manifest.legacyAudioRuntime.startCounts.NAudio -eq 0 -and
    $manifest.legacyAudioRuntime.startCounts.SoundFlow -eq 0 -and
    $manifest.legacyAudioRuntime.startCounts.miniaudio -eq 0 -and
    $manifest.legacyAudioRuntime.startCounts.oldAudioV3V4 -eq 0 -and
    $manifest.legacyAudioRuntime.startCounts.ffmpegAgateAmixSpeechPatch -eq 0) `
    'Package manifest does not prove all legacy Audio runtime Start counts are zero.'
$expectedPlugins = @(
    'gstaudioconvert.dll', 'gstaudioresample.dll',
    'gstcoreelements.dll', 'gstflac.dll', 'gstwasapi2.dll', 'gstwebrtcdsp.dll'
)
$actualPlugins = @(Get-ChildItem -LiteralPath (Join-Path $package 'gstreamer\plugins') -File | Select-Object -ExpandProperty Name | Sort-Object)
Assert-True (@(Compare-Object $expectedPlugins $actualPlugins).Count -eq 0) `
    'Package plugin allowlist is not the exact six-file set.'
Assert-True (Test-Path (Join-Path $package 'licenses\GSTREAMER-AUDIO-THIRD-PARTY.md')) `
    'GStreamer third-party notice is missing from the package.'

$manifestFiles = @($manifest.files)
$payloadFiles = @(Get-ChildItem -LiteralPath $package -Recurse -File |
    Where-Object Name -ne 'package-manifest.json')
Assert-True ($manifestFiles.Count -eq $payloadFiles.Count) 'Package manifest file count does not match payload.'
foreach ($entry in $manifestFiles) {
    $file = Join-Path $package ($entry.path.Replace('/', '\'))
    Assert-True (Test-Path -LiteralPath $file) "Manifest payload is missing: $($entry.path)."
    $hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $file).Hash
    Assert-True ($hash -eq $entry.sha256) "Manifest SHA-256 mismatch: $($entry.path)."
}
$packageTextFiles = Get-ChildItem -LiteralPath $package -Recurse -File |
    Where-Object { $_.Name -ne 'package-manifest.json' -and
        $_.Extension -eq '.json' }
$packageLegacyHits = @($packageTextFiles | Select-String -Pattern 'NAudio|SoundFlow|miniaudio' -CaseSensitive:$false)
Assert-True ($packageLegacyHits.Count -eq 0) 'Package metadata contains a legacy audio runtime.'

$savedPath = $env:PATH
$savedSystemPlugin = $env:GST_PLUGIN_SYSTEM_PATH_1_0
$savedPlugin = $env:GST_PLUGIN_PATH_1_0
$savedRegistryFork = $env:GST_REGISTRY_FORK
$savedRegistry = $env:GST_REGISTRY_1_0
try {
    $env:PATH = "$package;$env:WINDIR\System32;$env:WINDIR"
    $env:GST_PLUGIN_SYSTEM_PATH_1_0 = Join-Path $package 'gstreamer\plugins'
    $env:GST_PLUGIN_PATH_1_0 = ''
    $env:GST_REGISTRY_FORK = 'no'
    $env:GST_REGISTRY_1_0 = Join-Path $gateRoot 'gst-registry-package.bin'
    $versionText = (& $gstInspect --version) -join "`n"
    Assert-LastExitCode 'gst-inspect version check failed'
    Assert-True ($versionText -match '1\.28\.6') 'gst-inspect is not exactly GStreamer 1.28.6.'
    foreach ($plugin in @('coreelements', 'wasapi2', 'audioconvert', 'audioresample', 'webrtcdsp', 'flac')) {
        $metadata = (& $gstInspect $plugin) -join "`n"
        Assert-LastExitCode "gst-inspect failed for $plugin"
        Assert-True ($metadata -match '(?im)^\s*License\s+LGPL\s*$') "$plugin is not reported as LGPL."
    }

    Push-Location $package
    try {
        $env:GST_PLUGIN_SYSTEM_PATH_1_0 = 'Z:\must-not-be-used\plugins'
        & (Join-Path $package 'XbPreview.Host.exe') --package-smoke
        Assert-LastExitCode 'Direct package EXE smoke failed'
    }
    finally {
        Pop-Location
    }
}
finally {
    $env:PATH = $savedPath
    $env:GST_PLUGIN_SYSTEM_PATH_1_0 = $savedSystemPlugin
    $env:GST_PLUGIN_PATH_1_0 = $savedPlugin
    $env:GST_REGISTRY_FORK = $savedRegistryFork
    $env:GST_REGISTRY_1_0 = $savedRegistry
}

Write-Gate 'Real GStreamer source pipelines (A/B/C/I)'
$captureStamp = Get-Date -Format 'yyyyMMdd-HHmmss-fff'
foreach ($capture in @(
    @{ name = 'system'; files = @(@{ name = 'system.flac'; channels = 2 }) },
    @{ name = 'microphone'; files = @(@{ name = 'mic.flac'; channels = 1 }) },
    @{ name = 'dual'; files = @(
        @{ name = 'system.flac'; channels = 2 },
        @{ name = 'mic.flac'; channels = 1 }
    ) }
)) {
    $directory = Join-Path $gateRoot "capture-$($capture.name)-$captureStamp"
    $sound = Start-SystemSound ($CaptureSeconds + 2)
    try {
        & $testExe --capture $capture.name $directory $CaptureSeconds
        Assert-LastExitCode "Real $($capture.name) GStreamer capture failed"
    }
    finally {
        if (-not $sound.HasExited) { $sound.WaitForExit(3000) | Out-Null }
        $sound.Dispose()
    }
    foreach ($expectedFile in $capture.files) {
        $flac = Join-Path $directory $expectedFile.name
        Assert-True ((Test-Path -LiteralPath $flac) -and (Get-Item -LiteralPath $flac).Length -gt 0) `
            "$($capture.name) did not produce nonempty $($expectedFile.name)."
        $audio = @(Get-MediaStreams $flac | Where-Object codec_type -eq 'audio')
        Assert-True ($audio.Count -eq 1 -and $audio[0].codec_name -eq 'flac' -and
            [int]$audio[0].sample_rate -eq 48000 -and
            [int]$audio[0].channels -eq $expectedFile.channels) `
            "$($capture.name) $($expectedFile.name) format is not frozen."
    }
    Assert-True (-not (Test-Path -LiteralPath (Join-Path $directory 'program.flac'))) `
        "$($capture.name) unexpectedly recreated the old mixed program.flac."
}

$restartDirectory = Join-Path $gateRoot "restart-microphone-$captureStamp"
& $testExe --restart-microphone $restartDirectory $CaptureSeconds
Assert-LastExitCode 'Next-Start microphone re-enumeration failed'
foreach ($name in @('first-start', 'next-start')) {
    $restartFlac = Join-Path $restartDirectory "$name\mic.flac"
    Assert-True ((Test-Path -LiteralPath $restartFlac) -and
        (Get-Item -LiteralPath $restartFlac).Length -gt 0) `
        "Microphone $name did not close a nonempty mic.flac."
}

Write-Gate 'End-to-end GStreamer to FLAC to AAC/MP4 (A/B/C/D)'
foreach ($mode in @('System', 'Microphone', 'Dual', 'None')) {
    Invoke-EndToEnd $mode
}

Write-Gate 'Restore formal package after package smoke'
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File `
    (Join-Path $PSScriptRoot 'New-MvpAudioGStreamerPackage.ps1') -SkipBuild
Assert-LastExitCode 'Final package regeneration failed'

Write-Gate 'Final clean-worktree check'
$dirty = @(git -C $repository status --porcelain --untracked-files=all)
Assert-LastExitCode 'final git status failed'
Assert-True ($dirty.Count -eq 0) 'Automatic gates changed tracked files or the worktree is not clean.'

Write-Host 'MVP-AUDIO-GSTREAMER-FINAL-HUMAN-GATE-READY'
