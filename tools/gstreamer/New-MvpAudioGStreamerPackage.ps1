[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [switch]$SkipBuild,
    [string]$GStreamerRoot,
    [string]$FfmpegRoot,
    [string]$PublishPath,
    [string]$PackagePath,
    [string]$VcRedistRoot
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$version = '1.28.6'
$repository = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$artifacts = Join-Path $repository 'artifacts'
$sdk = if ([string]::IsNullOrWhiteSpace($GStreamerRoot)) {
    Join-Path $artifacts "sdk\gstreamer-$version"
} else {
    [IO.Path]::GetFullPath($GStreamerRoot)
}
$output = Join-Path $artifacts "bin\$Configuration\x64"
$publish = if ([string]::IsNullOrWhiteSpace($PublishPath)) {
    Join-Path $artifacts "publish\$Configuration\win-x64"
} else {
    [IO.Path]::GetFullPath($PublishPath)
}
$package = if ([string]::IsNullOrWhiteSpace($PackagePath)) {
    Join-Path $artifacts 'package\win-x64'
} else {
    [IO.Path]::GetFullPath($PackagePath)
}
$ffmpeg = if ([string]::IsNullOrWhiteSpace($FfmpegRoot)) {
    Join-Path $artifacts 'audio-v2-toolchain\ffmpeg-8.1-lgpl-shared\ffmpeg-n8.1-latest-win64-lgpl-shared-8.1'
} else {
    [IO.Path]::GetFullPath($FfmpegRoot)
}
$vswhere = 'C:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe'

$plugins = @(
    'gstcoreelements.dll',
    'gstwasapi2.dll',
    'gstaudioconvert.dll',
    'gstaudioresample.dll',
    'gstwebrtcdsp.dll',
    'gstflac.dll',
    'gstlevel.dll'
)
$pluginInventory = @(
    [ordered]@{ file = 'gstcoreelements.dll'; module = 'gstreamer'; elements = @('queue', 'filesink'); license = 'LGPL-2.0-or-later' },
    [ordered]@{ file = 'gstwasapi2.dll'; module = 'gst-plugins-bad'; elements = @('wasapi2src'); license = 'LGPL-2.0-or-later' },
    [ordered]@{ file = 'gstaudioconvert.dll'; module = 'gst-plugins-base'; elements = @('audioconvert'); license = 'LGPL-2.0-or-later' },
    [ordered]@{ file = 'gstaudioresample.dll'; module = 'gst-plugins-base'; elements = @('audioresample'); license = 'LGPL-2.0-or-later' },
    [ordered]@{ file = 'gstwebrtcdsp.dll'; module = 'gst-plugins-bad'; elements = @('webrtcdsp'); license = 'LGPL-2.0-or-later; bundled WebRTC audio processing: BSD-3-Clause' },
    [ordered]@{ file = 'gstflac.dll'; module = 'gst-plugins-good'; elements = @('flacenc'); license = 'LGPL-2.0-or-later; FLAC: BSD-3-Clause' },
    [ordered]@{ file = 'gstlevel.dll'; module = 'gst-plugins-good'; elements = @('level'); license = 'LGPL-2.0-or-later' }
)
$runtimeDlls = @(
    'gstreamer-1.0-0.dll', 'gstbase-1.0-0.dll', 'gstaudio-1.0-0.dll',
    'gsttag-1.0-0.dll', 'gstbadaudio-1.0-0.dll', 'glib-2.0-0.dll',
    'gobject-2.0-0.dll', 'gmodule-2.0-0.dll', 'intl-8.dll',
    'orc-0.4-0.dll', 'z-1.dll', 'ffi-7.dll', 'pcre2-8-0.dll',
    'FLAC-8.dll', 'ogg-0.dll'
)
$runtimeInventory = @(
    [ordered]@{ files = @('gstreamer-1.0-0.dll', 'gstbase-1.0-0.dll', 'gstaudio-1.0-0.dll', 'gsttag-1.0-0.dll', 'gstbadaudio-1.0-0.dll'); component = 'GStreamer libraries'; license = 'LGPL-2.0-or-later' },
    [ordered]@{ files = @('glib-2.0-0.dll', 'gobject-2.0-0.dll', 'gmodule-2.0-0.dll'); component = 'GLib'; license = 'LGPL-2.0-or-later' },
    [ordered]@{ files = @('intl-8.dll'); component = 'proxy-libintl'; license = 'LGPL-2.0-or-later' },
    [ordered]@{ files = @('orc-0.4-0.dll'); component = 'ORC'; license = 'BSD-2-Clause' },
    [ordered]@{ files = @('z-1.dll'); component = 'zlib'; license = 'Zlib' },
    [ordered]@{ files = @('ffi-7.dll'); component = 'libffi'; license = 'MIT' },
    [ordered]@{ files = @('pcre2-8-0.dll'); component = 'PCRE2'; license = 'BSD-3-Clause' },
    [ordered]@{ files = @('FLAC-8.dll'); component = 'FLAC'; license = 'BSD-3-Clause' },
    [ordered]@{ files = @('ogg-0.dll'); component = 'libogg'; license = 'BSD-3-Clause' }
)
$ffmpegFiles = @(
    'ffmpeg.exe', 'avcodec-62.dll', 'avdevice-62.dll', 'avfilter-11.dll',
    'avformat-62.dll', 'avutil-60.dll', 'swresample-6.dll', 'swscale-9.dll'
)

if (-not (Test-Path (Join-Path $sdk 'include\gstreamer-1.0\gst\gst.h'))) {
    throw "Pinned GStreamer SDK is missing. Run tools\gstreamer\Install-GStreamer-1.28.6.ps1."
}
if (-not (Test-Path (Join-Path $ffmpeg 'bin\ffmpeg.exe'))) {
    throw 'Pinned LGPL FFmpeg mux runtime is missing.'
}

if (-not $SkipBuild) {
    $msbuild = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
    if (-not $msbuild) { throw 'MSBuild was not found.' }
    & $msbuild (Join-Path $repository 'XbPreview.Native\XbPreview.Native.vcxproj') /t:Build "/p:Configuration=$Configuration" /p:Platform=x64 "/p:GStreamerRoot=$sdk" "/p:FfmpegMuxRuntimeRoot=$ffmpeg" /m /v:minimal
    if ($LASTEXITCODE -ne 0) { throw 'Native build failed.' }
    $resolvedPublish = [IO.Path]::GetFullPath($publish)
    $resolvedArtifactsForPublish = [IO.Path]::GetFullPath($artifacts).TrimEnd('\') + '\'
    if (-not $resolvedPublish.StartsWith($resolvedArtifactsForPublish, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Unsafe publish path: $resolvedPublish"
    }
    if (Test-Path -LiteralPath $resolvedPublish) {
        Remove-Item -LiteralPath $resolvedPublish -Recurse -Force
    }
    dotnet publish (Join-Path $repository 'XbPreview.Host\XbPreview.Host.csproj') -c $Configuration -r win-x64 --self-contained true -p:Platform=x64 -p:DebugType=None -p:DebugSymbols=false "-p:OutputPath=$resolvedPublish\" -o $resolvedPublish
    if ($LASTEXITCODE -ne 0) { throw 'Self-contained host publish failed.' }
}

$resolvedArtifacts = [IO.Path]::GetFullPath($artifacts).TrimEnd('\') + '\'
$resolvedPackage = [IO.Path]::GetFullPath($package)
if (-not $resolvedPackage.StartsWith($resolvedArtifacts, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Unsafe package path: $resolvedPackage"
}
if (Test-Path -LiteralPath $resolvedPackage) {
    Remove-Item -LiteralPath $resolvedPackage -Recurse -Force
}
New-Item -ItemType Directory -Force -Path @(
    $resolvedPackage,
    (Join-Path $resolvedPackage 'gstreamer\plugins'),
    (Join-Path $resolvedPackage 'gstreamer\gio-modules'),
    (Join-Path $resolvedPackage 'ffmpeg'),
    (Join-Path $resolvedPackage 'licenses\gstreamer'),
    (Join-Path $resolvedPackage 'licenses\ffmpeg'),
    (Join-Path $resolvedPackage 'licenses\screenrecorderlib'),
    (Join-Path $resolvedPackage 'assets')
) | Out-Null

Copy-Item -Path (Join-Path $publish '*') -Destination $resolvedPackage -Recurse -Force
# Debug symbols are build evidence, not distributable runtime. Portable/native PDBs
# may carry developer source paths, so the formal package deliberately excludes them.
Get-ChildItem -LiteralPath $resolvedPackage -Recurse -Filter '*.pdb' -File |
    Remove-Item -Force
Copy-Item -LiteralPath (Join-Path $output 'XbPreview.Native.dll') -Destination $resolvedPackage -Force
foreach ($name in $runtimeDlls) {
    Copy-Item -LiteralPath (Join-Path $sdk "bin\$name") -Destination $resolvedPackage -Force
}
foreach ($name in $plugins) {
    Copy-Item -LiteralPath (Join-Path $sdk "lib\gstreamer-1.0\$name") -Destination (Join-Path $resolvedPackage 'gstreamer\plugins') -Force
}
foreach ($name in $ffmpegFiles) {
    Copy-Item -LiteralPath (Join-Path $ffmpeg "bin\$name") -Destination (Join-Path $resolvedPackage 'ffmpeg') -Force
}
Copy-Item -LiteralPath (Join-Path $ffmpeg 'LICENSE.txt') -Destination (Join-Path $resolvedPackage 'ffmpeg\LICENSE.txt') -Force

$licenseSelections = [ordered]@{
    'gstreamer-1.0' = @('LGPL-2.0-or-later.txt', 'README-LICENSE-INFO.txt')
    'gst-plugins-base-1.0' = @('LGPL-2.0-or-later.txt', 'README-LICENSE-INFO.txt')
    'gst-plugins-bad-1.0' = @('LGPL-2.0-or-later.txt', 'README-LICENSE-INFO.txt')
    'glib' = @('LGPL-2.0-or-later.txt', 'README-LICENSE-INFO.txt')
    'libffi' = @('LICENSE', 'README-LICENSE-INFO.txt')
    'orc' = @('COPYING', 'README-LICENSE-INFO.txt')
    'pcre2' = @('LICENCE', 'README-LICENSE-INFO.txt')
    'proxy-libintl' = @('LGPL-2.0-or-later.txt', 'README-LICENSE-INFO.txt')
    'zlib' = @('README', 'README-LICENSE-INFO.txt')
    'flac' = @('COPYING.Xiph', 'README-LICENSE-INFO.txt')
    'libogg' = @('COPYING', 'README-LICENSE-INFO.txt')
    'webrtc-audio-processing' = @('COPYING', 'README-LICENSE-INFO.txt')
}
foreach ($component in $licenseSelections.Keys) {
    $destination = Join-Path $resolvedPackage "licenses\gstreamer\$component"
    New-Item -ItemType Directory -Force -Path $destination | Out-Null
    foreach ($name in $licenseSelections[$component]) {
        Copy-Item -LiteralPath (Join-Path $sdk "share\licenses\$component\$name") -Destination $destination -Force
    }
}
$goodPluginLicenseDestination = Join-Path $resolvedPackage 'licenses\gstreamer\gst-plugins-good-1.0'
New-Item -ItemType Directory -Force -Path $goodPluginLicenseDestination | Out-Null
Copy-Item -LiteralPath (Join-Path $sdk 'share\licenses\gstreamer-1.0\LGPL-2.0-or-later.txt') -Destination (Join-Path $goodPluginLicenseDestination 'LGPL-2.0-or-later.txt') -Force
Copy-Item -LiteralPath (Join-Path $repository 'docs\licenses\GSTREAMER-AUDIO-THIRD-PARTY.md') -Destination (Join-Path $resolvedPackage 'licenses\GSTREAMER-AUDIO-THIRD-PARTY.md') -Force
Copy-Item -LiteralPath (Join-Path $repository 'third_party\ffmpeg\SOURCE.md') -Destination (Join-Path $resolvedPackage 'licenses\ffmpeg\SOURCE.md') -Force
Copy-Item -LiteralPath (Join-Path $repository 'third_party\screenrecorderlib-audio\LICENSE-SCREENRECORDERLIB.txt') -Destination (Join-Path $resolvedPackage 'licenses\screenrecorderlib\LICENSE-SCREENRECORDERLIB.txt') -Force
Copy-Item -LiteralPath (Join-Path $repository 'third_party\screenrecorderlib-audio\LICENSE-MICROSOFT-WINDOWS-CLASSIC-SAMPLES.txt') -Destination (Join-Path $resolvedPackage 'licenses\screenrecorderlib\LICENSE-MICROSOFT-WINDOWS-CLASSIC-SAMPLES.txt') -Force
Copy-Item -LiteralPath (Join-Path $repository 'third_party\screenrecorderlib-audio\SOURCE.md') -Destination (Join-Path $resolvedPackage 'licenses\screenrecorderlib\SOURCE.md') -Force
Copy-Item -LiteralPath (Join-Path $repository 'LICENSE') -Destination (Join-Path $resolvedPackage 'LICENSE') -Force
$productAssetNames = @(
    (([string][char]0x5E7B) + ([char]0x5F69) + '01.png'),
    (([string][char]0x5E7B) + ([char]0x5F69) + '02.png')
)
foreach ($assetName in $productAssetNames) {
    Copy-Item -LiteralPath (Join-Path $repository "assets\$assetName") -Destination (Join-Path $resolvedPackage "assets\$assetName") -Force
}
if (Test-Path -LiteralPath (Join-Path $repository 'docs\release\XIAOBAI-RECORDER-1.0.0-ASSET-PROVENANCE.md')) {
    Copy-Item -LiteralPath (Join-Path $repository 'docs\release\XIAOBAI-RECORDER-1.0.0-ASSET-PROVENANCE.md') -Destination (Join-Path $resolvedPackage 'licenses\PRODUCT-ASSET-PROVENANCE.md') -Force
}

$redist = $VcRedistRoot
if ([string]::IsNullOrWhiteSpace($redist)) {
    $installation = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
    if ([string]::IsNullOrWhiteSpace($installation)) { throw 'Visual Studio C++ tools were not found.' }
    $redist = Join-Path $installation 'VC\Redist\MSVC\14.44.35112\x64\Microsoft.VC143.CRT'
}
$redist = [IO.Path]::GetFullPath($redist)
$vcRuntimeFiles = [ordered]@{
    'msvcp140.dll' = '0F885B509A685D2BBFA652FED26B5FB31D88FBDAB0A978C641D1C7B8AA460AA9'
    'vcruntime140.dll' = 'D5E4D9A3E835FA679450145D6A7D94E36573A509317111904D9B3712C30D9066'
    'vcruntime140_1.dll' = '1F2D41C4AA5DB0BC33EBF7B66D72943A817D7CE6CBE880502A9403823633093F'
}
foreach ($name in $vcRuntimeFiles.Keys) {
    $source = Join-Path $redist $name
    if (-not (Test-Path -LiteralPath $source -PathType Leaf)) {
        throw "Pinned Visual C++ app-local runtime file was not found: $source"
    }
    $fileVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo($source).FileVersion
    if ($fileVersion -ne '14.44.35211.0') {
        throw "Visual C++ runtime version drift for $name. Expected 14.44.35211.0; received $fileVersion."
    }
    $actualHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $source).Hash
    if ($actualHash -ne $vcRuntimeFiles[$name]) {
        throw "Visual C++ runtime hash drift for $name. Expected $($vcRuntimeFiles[$name]); received $actualHash."
    }
    Copy-Item -LiteralPath $source -Destination $resolvedPackage -Force
}

$legacyAudioPattern = '(?i)NAudio|SoundFlow|MiniaudioLoopback|libminiaudio|AudioV[234]|agate|amix'
$legacyPayload = Get-ChildItem -LiteralPath $resolvedPackage -Recurse -File |
    Where-Object { $_.Name -match $legacyAudioPattern }
if ($legacyPayload) {
    throw "Legacy audio payload reached the formal package: $($legacyPayload.FullName -join ', ')"
}
$packagedUserSettings = @(Get-ChildItem -LiteralPath $resolvedPackage -Recurse -File |
    Where-Object { $_.Name -match '(?i)microphone-selection|selected-microphone|endpoint-id' })
if ($packagedUserSettings.Count -ne 0) {
    throw "Per-user microphone settings reached the formal package: $($packagedUserSettings.FullName -join ', ')"
}
$wasapiEndpointPattern = '\{0\.0\.1\.00000000\}\.\{[0-9A-Fa-f-]{36}\}'
$packagedEndpointHits = @()
foreach ($file in Get-ChildItem -LiteralPath $resolvedPackage -File |
        Where-Object Name -Like 'XbPreview.*') {
    $bytes = [IO.File]::ReadAllBytes($file.FullName)
    $ascii = [Text.Encoding]::ASCII.GetString($bytes)
    $unicode = [Text.Encoding]::Unicode.GetString($bytes)
    if ($ascii -match $wasapiEndpointPattern -or
        $unicode -match $wasapiEndpointPattern) {
        $packagedEndpointHits += $file.FullName
    }
}
if ($packagedEndpointHits.Count -ne 0) {
    throw "A development-machine WASAPI endpoint ID reached the formal package: $($packagedEndpointHits -join ', ')"
}
$textExtensions = @('.json', '.config', '.xml', '.md', '.txt')
$developerPathPattern = '(?i)[A-Z]:\\(?:Users\\|[^\r\n]*xiaobai-screen-recorder)'
$developerPathHits = Get-ChildItem -LiteralPath $resolvedPackage -Recurse -File |
    Where-Object { $textExtensions -contains $_.Extension } |
    Where-Object { (Get-Content -LiteralPath $_.FullName -Raw) -match $developerPathPattern }
if ($developerPathHits) {
    throw "Developer absolute path reached the formal package: $($developerPathHits.FullName -join ', ')"
}
$firstPartyBinaries = @('XbPreview.Host.exe', 'XbPreview.Host.dll', 'XbPreview.Native.dll')
$binaryDeveloperPathHits = foreach ($name in $firstPartyBinaries) {
    $candidate = Join-Path $resolvedPackage $name
    if (Test-Path -LiteralPath $candidate) {
        $binaryText = [Text.Encoding]::GetEncoding(28591).GetString([IO.File]::ReadAllBytes($candidate))
        if ($binaryText -match '(?i)[A-Z]:\\Users\\' -or $binaryText.Contains('xiaobai-screen-recorder')) {
            $candidate
        }
    }
}
if ($binaryDeveloperPathHits) {
    throw "Developer absolute path reached a first-party binary: $($binaryDeveloperPathHits -join ', ')"
}

$files = Get-ChildItem -LiteralPath $resolvedPackage -Recurse -File | Sort-Object FullName | ForEach-Object {
    [ordered]@{
        path = $_.FullName.Substring($resolvedPackage.Length + 1).Replace('\', '/')
        bytes = $_.Length
        sha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $_.FullName).Hash
    }
}
$manifest = [ordered]@{
    product = 'XbPreview MVP Audio Core'
    architecture = 'win-x64'
    audioRuntime = 'GStreamerOnly'
    microphoneDeviceBinding = [ordered]@{
        enumeration = 'GstDeviceMonitor Audio/Source'
        hotplug = 'GstDeviceMonitor DEVICE_ADDED and DEVICE_REMOVED'
        identity = 'WASAPI device.id plus concrete GstDevice retained per session'
        friendlyNameUse = 'display only'
        selection = @('WindowsDefault', 'ConcreteEndpoint')
        windowsDefaultResolution = 'resolve device.actual-id to a concrete GstDevice at Start'
        sessionDefaultFollowing = $false
        sourceCreation = 'gst_device_create_element'
        requiresConcreteDeviceAtStart = $true
        defaultSourceFallback = $false
        arbitraryCaptureEndpointFallback = $false
        deviceEnumerationReimplemented = $false
        wasapiReimplemented = $false
        hotplugReimplemented = $false
        hotReconnect = $false
        perUserSettings = '%LOCALAPPDATA%/XbPreview/settings/microphone-selection.json'
        perUserSettingsPackaged = $false
        unavailableError = 'MicUnavailableAtStart'
        removalPolicy = 'DEVICE_REMOVED closes transform-to-gap valve; no reconnect until next Start'
    }
    gstreamer = [ordered]@{
        version = $version
        distribution = 'MSVC x86_64'
        installerSha256 = '059251444D1267B486EBA390B18D25FED87E10315E72F757EC6C7E912FA746B5'
        plugins = $plugins
        pluginInventory = $pluginInventory
        runtimeInventory = $runtimeInventory
    }
    ffmpeg = [ordered]@{
        role = 'File mastering: two-pass loudnorm, Dual unity normalized amix, AAC encode, H.264 copy, MP4 mux'
        audioFilters = $true
        mastering = [ordered]@{
            microphone = 'two-pass loudnorm I=-16:TP=-3.0:LRA=7 before AAC'
            dualMix = "amix weights='1 1' normalize=1, then two-pass loudnorm I=-16:TP=-3.0:LRA=7 before AAC"
            systemOnly = 'no loudness filter; direct AAC encode/mux'
            finalDecodedMp4TruePeakMaximumDbtp = -1.5
            customDsp = $false
            agate = $false
            expander = $false
        }
        files = $ffmpegFiles
        license = 'LGPL-3.0-or-later build; bundled LICENSE.txt'
    }
    legacyAudioRuntime = [ordered]@{
        NAudio = 'ABSENT'
        SoundFlow = 'ABSENT'
        miniaudioProductRuntime = 'ABSENT'
        oldAudioV3V4 = 'ABSENT'
        ffmpegAgateAmixSpeechPatch = 'ABSENT'
        startCounts = [ordered]@{
            NAudio = 0
            SoundFlow = 0
            miniaudio = 0
            oldAudioV3V4 = 0
            ffmpegAgateAmixSpeechPatch = 0
        }
    }
    deployment = [ordered]@{
        pluginSearchPath = 'gstreamer/plugins (resolved app-relative)'
        globalPathRequired = $false
        globalGstPluginPathRequired = $false
        developerAbsolutePathsPresent = $false
    }
    files = $files
}
$manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $resolvedPackage 'package-manifest.json') -Encoding UTF8
Write-Host "Package created: $resolvedPackage"
