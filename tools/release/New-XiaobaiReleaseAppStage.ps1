[CmdletBinding()]
param(
    [string]$ReleaseLockPath,
    [string]$ComplianceSpecPath,
    [string]$GStreamerRoot,
    [string]$GStreamerInstallerPath,
    [string]$FfmpegRoot,
    [string]$FfmpegArchivePath,
    [string]$VcRedistRoot,
    [string]$NuGetPackagesRoot,
    [string]$MsBuildPath = 'C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe',
    [switch]$ResumeFromVerifiedBuildAndRestore,
    [switch]$ResumeFromVerifiedPublish,
    [switch]$ResumeFromVerifiedAppStage,
    [switch]$ThirdPartyNoticesCandidate,
    [switch]$ReleaseFoundationCandidateSelfTest
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$env:AVALONIA_TELEMETRY_OPTOUT = '1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'

$script:Utf8NoBom = New-Object Text.UTF8Encoding($false)

function Assert-True {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw $Message }
}

function Assert-Equal {
    param($Actual, $Expected, [string]$Label)
    if ([string]$Actual -cne [string]$Expected) {
        throw "VERSION-DRIFT: $Label expected '$Expected'; received '$Actual'."
    }
}

function Get-Sha256 {
    param([string]$Path)
    (Get-FileHash -Algorithm SHA256 -LiteralPath $Path).Hash
}

function Get-Sha512Base64 {
    param([string]$Path)
    $algorithm = [Security.Cryptography.SHA512]::Create()
    try {
        $stream = [IO.File]::OpenRead($Path)
        try { [Convert]::ToBase64String($algorithm.ComputeHash($stream)) }
        finally { $stream.Dispose() }
    }
    finally { $algorithm.Dispose() }
}

function Assert-FileHash {
    param([string]$Path, [string]$Expected, [string]$Label)
    Assert-True (Test-Path -LiteralPath $Path -PathType Leaf) "Required input is missing: $Path"
    $actual = Get-Sha256 $Path
    if ($actual -ne $Expected) {
        throw "VERSION-DRIFT: $Label SHA-256 expected $Expected; received $actual."
    }
}

function Read-JsonFile {
    param([string]$Path)
    Assert-True (Test-Path -LiteralPath $Path -PathType Leaf) "Required JSON file is missing: $Path"
    Get-Content -Raw -Encoding UTF8 -LiteralPath $Path | ConvertFrom-Json
}

function Write-Utf8File {
    param([string]$Path, [string]$Content)
    [IO.File]::WriteAllText($Path, $Content, $script:Utf8NoBom)
}

function Assert-ArtifactPath {
    param([string]$Path, [string]$ArtifactsRoot)
    $resolved = [IO.Path]::GetFullPath($Path)
    $prefix = [IO.Path]::GetFullPath($ArtifactsRoot).TrimEnd('\') + '\'
    Assert-True ($resolved.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) "Unsafe artifacts path: $resolved"
    $resolved
}

function Reset-ArtifactDirectory {
    param([string]$Path, [string]$ArtifactsRoot)
    $resolved = Assert-ArtifactPath $Path $ArtifactsRoot
    if (Test-Path -LiteralPath $resolved) {
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
    New-Item -ItemType Directory -Force -Path $resolved | Out-Null
    $resolved
}

function Copy-Material {
    param([string]$Source, [string]$Destination)
    if (-not (Test-Path -LiteralPath $Source -PathType Leaf)) { return }
    $parent = Split-Path -Parent $Destination
    New-Item -ItemType Directory -Force -Path $parent | Out-Null
    Copy-Item -LiteralPath $Source -Destination $Destination -Force
}

function Get-LockPackages {
    param([string[]]$LockPaths)
    $result = @{}
    foreach ($lockPath in $LockPaths) {
        $lock = Read-JsonFile $lockPath
        foreach ($framework in $lock.dependencies.PSObject.Properties) {
            foreach ($dependency in $framework.Value.PSObject.Properties) {
                $entry = $dependency.Value
                if ($entry.type -eq 'Project') { continue }
                if (-not ($entry.PSObject.Properties.Name -contains 'resolved') -or
                    -not ($entry.PSObject.Properties.Name -contains 'contentHash')) {
                    continue
                }
                $key = $dependency.Name.ToLowerInvariant()
                $candidate = [ordered]@{
                    id = $dependency.Name
                    version = [string]$entry.resolved
                    contentHash = [string]$entry.contentHash
                }
                if ($result.ContainsKey($key)) {
                    Assert-Equal $result[$key].version $candidate.version "NuGet lock $($candidate.id)"
                    Assert-Equal $result[$key].contentHash $candidate.contentHash "NuGet lock hash $($candidate.id)"
                } else {
                    $result[$key] = $candidate
                }
            }
        }
    }
    $result
}

function Test-AllFilesPresent {
    param([string]$Root, [object[]]$RelativePaths)
    foreach ($relativePath in $RelativePaths) {
        $candidate = Join-Path $Root ([string]$relativePath).Replace('/', '\')
        if (-not (Test-Path -LiteralPath $candidate -PathType Leaf)) { return $false }
    }
    $true
}

$repository = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$artifacts = Join-Path $repository 'artifacts'
if ([string]::IsNullOrWhiteSpace($ReleaseLockPath)) {
    $ReleaseLockPath = Join-Path $PSScriptRoot 'release-inputs.v1.0.0.json'
}
if ([string]::IsNullOrWhiteSpace($ComplianceSpecPath)) {
    $ComplianceSpecPath = Join-Path $PSScriptRoot 'release-compliance.v1.0.0.json'
}
if ([string]::IsNullOrWhiteSpace($GStreamerRoot)) {
    $GStreamerRoot = Join-Path $artifacts 'sdk\gstreamer-1.28.6'
}
if ([string]::IsNullOrWhiteSpace($GStreamerInstallerPath)) {
    $GStreamerInstallerPath = Join-Path $artifacts 'cache\gstreamer-1.28.6\gstreamer-1.0-msvc-x86_64-1.28.6.exe'
}
if ([string]::IsNullOrWhiteSpace($FfmpegRoot)) {
    $FfmpegRoot = Join-Path $artifacts 'audio-v2-toolchain\ffmpeg-8.1-lgpl-shared\ffmpeg-n8.1-latest-win64-lgpl-shared-8.1'
}
if ([string]::IsNullOrWhiteSpace($FfmpegArchivePath)) {
    $FfmpegArchivePath = Join-Path $artifacts 'audio-v2-toolchain\ffmpeg-n8.1-latest-win64-lgpl-shared-8.1.zip'
}
if ([string]::IsNullOrWhiteSpace($VcRedistRoot)) {
    $VcRedistRoot = 'C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\VC\Redist\MSVC\14.44.35112\x64\Microsoft.VC143.CRT'
}
if ([string]::IsNullOrWhiteSpace($NuGetPackagesRoot)) {
    $NuGetPackagesRoot = Join-Path $env:USERPROFILE '.nuget\packages'
}

$ReleaseLockPath = [IO.Path]::GetFullPath($ReleaseLockPath)
$ComplianceSpecPath = [IO.Path]::GetFullPath($ComplianceSpecPath)
$GStreamerRoot = [IO.Path]::GetFullPath($GStreamerRoot)
$GStreamerInstallerPath = [IO.Path]::GetFullPath($GStreamerInstallerPath)
$FfmpegRoot = [IO.Path]::GetFullPath($FfmpegRoot)
$FfmpegArchivePath = [IO.Path]::GetFullPath($FfmpegArchivePath)
$VcRedistRoot = [IO.Path]::GetFullPath($VcRedistRoot)
$NuGetPackagesRoot = [IO.Path]::GetFullPath($NuGetPackagesRoot)
$MsBuildPath = [IO.Path]::GetFullPath($MsBuildPath)

$lock = Read-JsonFile $ReleaseLockPath
$complianceSpec = Read-JsonFile $ComplianceSpecPath
$buildTimestamp = (Get-Date).ToUniversalTime().ToString('o')

# Source/product freeze preflight. The verified product source predates the
# release foundation, while approved compliance metadata may follow it.
$branch = (& git -C $repository branch --show-current).Trim()
$commit = (& git -C $repository rev-parse HEAD).Trim()
Assert-Equal $branch $lock.source.branch 'source branch'
$productCommit = [string]$lock.source.productCommit
$foundationCommit = [string]$lock.source.frozenFoundationCommit
$approvedMetadataDirectories = @($lock.source.approvedPostFoundationMetadata.directories | ForEach-Object { [string]$_ })
$approvedMetadataFiles = @($lock.source.approvedPostFoundationMetadata.files | ForEach-Object { [string]$_ })
Assert-Equal (($approvedMetadataDirectories | Sort-Object) -join "`n") 'docs/release/compliance/v1.0.0/' 'approved post-foundation metadata directories'
Assert-Equal (($approvedMetadataFiles | Sort-Object) -join "`n") 'THIRD-PARTY-NOTICES.md' 'approved post-foundation metadata files'

$resolvedProductCommit = @(& git -C $repository rev-parse --verify "$productCommit`^{commit}" 2>$null)
Assert-True ($LASTEXITCODE -eq 0 -and $resolvedProductCommit.Count -eq 1 -and $resolvedProductCommit[0].Trim() -eq $productCommit) "Frozen product commit is unavailable: $productCommit"
$resolvedFoundationCommit = @(& git -C $repository rev-parse --verify "$foundationCommit`^{commit}" 2>$null)
Assert-True ($LASTEXITCODE -eq 0 -and $resolvedFoundationCommit.Count -eq 1 -and $resolvedFoundationCommit[0].Trim() -eq $foundationCommit) "Frozen release foundation commit is unavailable: $foundationCommit"
& git -C $repository merge-base --is-ancestor $productCommit $foundationCommit
Assert-True ($LASTEXITCODE -eq 0) "PRODUCT-FOUNDATION-DRIFT: product commit $productCommit is not an ancestor of frozen foundation $foundationCommit."
& git -C $repository merge-base --is-ancestor $foundationCommit $commit
Assert-True ($LASTEXITCODE -eq 0) "PRODUCT-FOUNDATION-DRIFT: frozen foundation $foundationCommit is not an ancestor of current HEAD $commit."

$postFoundationPaths = @(& git -C $repository diff --name-only --no-renames "$foundationCommit..$commit" --)
Assert-True ($LASTEXITCODE -eq 0) 'Unable to inspect tracked paths after the frozen release foundation.'
$unexpectedPostFoundationPaths = @()
foreach ($changedPathValue in $postFoundationPaths) {
    $changedPath = ([string]$changedPathValue).Replace('\', '/')
    $approved = $approvedMetadataFiles -ccontains $changedPath
    if (-not $approved) {
        foreach ($directory in $approvedMetadataDirectories) {
            if ($changedPath.StartsWith($directory, [StringComparison]::Ordinal)) {
                $approved = $true
                break
            }
        }
    }
    if (-not $approved) { $unexpectedPostFoundationPaths += $changedPath }
}
Assert-True ($unexpectedPostFoundationPaths.Count -eq 0) "PRODUCT-FOUNDATION-DRIFT: post-foundation tracked paths are not approved compliance metadata: $($unexpectedPostFoundationPaths -join ', ')"

# Normal operation requires a clean tree. The two semantic candidate modes
# admit only the reviewed notice and the exact files that implement this lock.
Assert-True (-not $ReleaseFoundationCandidateSelfTest -or $ThirdPartyNoticesCandidate) 'ReleaseFoundationCandidateSelfTest requires ThirdPartyNoticesCandidate.'
$foundationSelfTestPaths = @(
    'tools/release/New-XiaobaiReleaseAppStage.ps1',
    'tools/release/release-inputs.v1.0.0.json',
    'packaging/README.md'
)
$allowedDirtyPaths = @()
if ($ReleaseFoundationCandidateSelfTest) { $allowedDirtyPaths += $foundationSelfTestPaths }
if ($ThirdPartyNoticesCandidate) { $allowedDirtyPaths += 'THIRD-PARTY-NOTICES.md' }
$unexpectedDirty = @()
$stagedDirty = @()
$dirtyEntries = @()
foreach ($line in @(& git -C $repository status --porcelain=v1 --untracked-files=all)) {
    if ([string]::IsNullOrWhiteSpace($line)) { continue }
    $status = $line.Substring(0, 2)
    $path = $line.Substring(3).Replace('\', '/')
    $dirtyEntries += [pscustomobject]@{ Status = $status; Path = $path }
    if ($status[0] -ne ' ' -and $status -ne '??') { $stagedDirty += $path }
    if ($allowedDirtyPaths -cnotcontains $path) { $unexpectedDirty += $path }
}
Assert-True ($stagedDirty.Count -eq 0) "PRODUCT-FREEZE-BLOCKED: staged paths are not accepted: $($stagedDirty -join ', ')"
Assert-True ($unexpectedDirty.Count -eq 0) "PRODUCT-FREEZE-BLOCKED: unexpected dirty paths: $($unexpectedDirty -join ', ')"
if ($ThirdPartyNoticesCandidate) {
    $noticePath = Join-Path $repository 'THIRD-PARTY-NOTICES.md'
    Assert-True (Test-Path -LiteralPath $noticePath -PathType Leaf) 'Reviewed THIRD-PARTY-NOTICES candidate is missing from the repository root.'
    $noticeEntries = @($dirtyEntries | Where-Object { $_.Path -ceq 'THIRD-PARTY-NOTICES.md' })
    Assert-True ($noticeEntries.Count -eq 1 -and $noticeEntries[0].Status -ceq '??') 'ThirdPartyNoticesCandidate requires exactly one untracked root THIRD-PARTY-NOTICES.md.'
}

$hostProject = Join-Path $repository 'XbPreview.Host\XbPreview.Host.csproj'
$avaloniaProject = Join-Path $repository 'XbPreview.Avalonia\XbPreview.Avalonia.csproj'
$nativeProject = Join-Path $repository 'XbPreview.Native\XbPreview.Native.vcxproj'
[xml]$hostProjectXml = Get-Content -Raw -Encoding UTF8 -LiteralPath $hostProject
[xml]$avaloniaProjectXml = Get-Content -Raw -Encoding UTF8 -LiteralPath $avaloniaProject
Assert-Equal $hostProjectXml.SelectSingleNode('/Project/PropertyGroup/TargetFramework').InnerText $lock.product.targetFramework '.NET target framework'
Assert-Equal $hostProjectXml.SelectSingleNode('/Project/PropertyGroup/Version').InnerText $lock.product.version 'product version'
foreach ($node in @($hostProjectXml.SelectNodes('//PackageReference')) + @($avaloniaProjectXml.SelectNodes('//PackageReference'))) {
    if ($node.Include -like 'Avalonia*') {
        Assert-Equal $node.Version $lock.nuget.avalonia "NuGet $($node.Include)"
    }
}
$globalJson = Read-JsonFile (Join-Path $repository 'global.json')
Assert-Equal $globalJson.sdk.version $lock.dotnet.sdkVersion '.NET SDK lock'
$actualSdkVersion = (& dotnet --version).Trim()
Assert-Equal $actualSdkVersion $lock.dotnet.sdkVersion '.NET SDK executable'

# NuGet packages.lock.json is the authoritative exact package graph. Verify every
# cached nupkg against its lock content hash before performing an offline restore.
$lockPaths = @(
    (Join-Path $repository 'XbPreview.Host\packages.lock.json'),
    (Join-Path $repository 'XbPreview.Avalonia\packages.lock.json')
)
$lockedPackages = Get-LockPackages $lockPaths
foreach ($package in $lockedPackages.Values) {
    $packageDirectory = Join-Path $NuGetPackagesRoot ($package.id.ToLowerInvariant() + '\' + $package.version.ToLowerInvariant())
    $nupkg = Join-Path $packageDirectory ($package.id.ToLowerInvariant() + '.' + $package.version.ToLowerInvariant() + '.nupkg')
    $nupkgHashRecord = $nupkg + '.sha512'
    $nupkgMetadata = Join-Path $packageDirectory '.nupkg.metadata'
    Assert-True (Test-Path -LiteralPath $nupkg -PathType Leaf) "Locked NuGet package is missing from the offline cache: $($package.id)/$($package.version)"
    Assert-True (Test-Path -LiteralPath $nupkgHashRecord -PathType Leaf) "NuGet package hash record is missing from the offline cache: $($package.id)/$($package.version)"
    Assert-True (Test-Path -LiteralPath $nupkgMetadata -PathType Leaf) "NuGet package metadata is missing from the offline cache: $($package.id)/$($package.version)"
    $actualContentHash = [string](Read-JsonFile $nupkgMetadata).contentHash
    Assert-Equal $actualContentHash $package.contentHash "NuGet content hash $($package.id)/$($package.version)"
    $expectedArchiveHash = (Get-Content -Raw -Encoding ASCII -LiteralPath $nupkgHashRecord).Trim()
    $actualArchiveHash = Get-Sha512Base64 $nupkg
    Assert-Equal $actualArchiveHash $expectedArchiveHash "NuGet archive hash $($package.id)/$($package.version)"
}
foreach ($runtimePackage in $lock.dotnet.runtimePackages) {
    $directory = Join-Path $NuGetPackagesRoot ($runtimePackage.id.ToLowerInvariant() + '\' + $runtimePackage.version)
    $nupkg = Join-Path $directory ($runtimePackage.id.ToLowerInvariant() + '.' + $runtimePackage.version + '.nupkg')
    Assert-FileHash $nupkg $runtimePackage.sha256 ".NET runtime package $($runtimePackage.id)"
}
$dotnetExecutable = (Get-Command dotnet.exe -ErrorAction Stop).Source
$dotnetRoot = Split-Path -Parent $dotnetExecutable
$appHostPath = Join-Path $dotnetRoot ([string]$lock.dotnet.appHostRelativePath).Replace('/', '\')
Assert-FileHash $appHostPath $lock.dotnet.appHostSha256 '.NET apphost pack'

# GStreamer input lock: installer provenance, header version, and exact narrow
# app-local support/plugin allowlists.
Assert-FileHash $GStreamerInstallerPath $lock.gstreamer.installerSha256 'GStreamer installer'
$versionHeader = Join-Path $GStreamerRoot 'include\gstreamer-1.0\gst\gstversion.h'
Assert-True (Test-Path -LiteralPath $versionHeader -PathType Leaf) "Pinned GStreamer version header is missing: $versionHeader"
$versionText = Get-Content -Raw -Encoding UTF8 -LiteralPath $versionHeader
Assert-True ($versionText -match '#define GST_VERSION_MAJOR \(1\)' -and
    $versionText -match '#define GST_VERSION_MINOR \(28\)' -and
    $versionText -match '#define GST_VERSION_MICRO \(6\)' -and
    $versionText -match '#define GST_VERSION_NANO \(0\)') 'VERSION-DRIFT: GStreamer headers are not exactly 1.28.6.'
foreach ($file in $lock.gstreamer.supportDlls) {
    Assert-FileHash (Join-Path $GStreamerRoot ('bin\' + $file.name)) $file.sha256 "GStreamer support $($file.name)"
}
foreach ($file in $lock.gstreamer.plugins) {
    Assert-FileHash (Join-Path $GStreamerRoot ('lib\gstreamer-1.0\' + $file.name)) $file.sha256 "GStreamer plugin $($file.name)"
}

# FFmpeg input lock: exact archive, payload, build identity, and configuration.
Assert-FileHash $FfmpegArchivePath $lock.ffmpeg.archiveSha256 'FFmpeg archive'
foreach ($file in $lock.ffmpeg.files) {
    $candidate = if ($file.name -eq 'LICENSE.txt') {
        Join-Path $FfmpegRoot $file.name
    } else {
        Join-Path $FfmpegRoot ('bin\' + $file.name)
    }
    Assert-FileHash $candidate $file.sha256 "FFmpeg $($file.name)"
}
$ffmpegVersionOutput = @(& (Join-Path $FfmpegRoot 'bin\ffmpeg.exe') -hide_banner -version 2>&1)
Assert-True ($LASTEXITCODE -eq 0) 'Pinned FFmpeg executable could not report its version.'
Assert-True (($ffmpegVersionOutput -join "`n") -match ('ffmpeg version ' + [regex]::Escape([string]$lock.ffmpeg.build))) 'VERSION-DRIFT: FFmpeg build identity differs from the release lock.'
$ffmpegConfiguration = ($ffmpegVersionOutput | Where-Object { [string]$_ -like 'configuration:*' } | Select-Object -First 1)
Assert-True (-not [string]::IsNullOrWhiteSpace([string]$ffmpegConfiguration)) 'FFmpeg configuration evidence is missing.'
Assert-True ([string]$ffmpegConfiguration -match '--enable-shared' -and [string]$ffmpegConfiguration -match '--disable-static') 'FFmpeg shared-build configuration drifted.'
Assert-True ([string]$ffmpegConfiguration -notmatch '(?:^|\s)--enable-gpl(?:\s|$)' -and [string]$ffmpegConfiguration -notmatch '(?:^|\s)--enable-nonfree(?:\s|$)') 'FFmpeg GPL/nonfree configuration is not allowed.'

# VC++ is an exact app-local file set. There is no newest-installed selection.
foreach ($file in $lock.vcRuntime.files) {
    $candidate = Join-Path $VcRedistRoot $file.name
    Assert-FileHash $candidate $file.sha256 "VC++ $($file.name)"
    $fileVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo($candidate).FileVersion
    Assert-Equal $fileVersion $lock.vcRuntime.fileVersion "VC++ file version $($file.name)"
}

foreach ($asset in $lock.assets) {
    Assert-FileHash (Join-Path $repository ([string]$asset.path).Replace('/', '\')) $asset.sha256 "product asset $($asset.path)"
}

Assert-True (Test-Path -LiteralPath $MsBuildPath -PathType Leaf) "MSBuild is missing: $MsBuildPath"
$nativeOutput = Assert-ArtifactPath (Join-Path $artifacts 'bin\Release\x64') $artifacts
$publish = Assert-ArtifactPath (Join-Path $artifacts 'publish\Release\win-x64') $artifacts
$releaseNuGetPackages = Assert-ArtifactPath (Join-Path $artifacts 'release-inputs\nuget-packages') $artifacts
$package = Join-Path $artifacts 'package\win-x64'
$releaseRoot = Join-Path $artifacts 'packaging\xiaobai-recorder-1.0.0'
$appStage = Join-Path $releaseRoot 'app'

if ($ResumeFromVerifiedBuildAndRestore -or $ResumeFromVerifiedPublish -or $ResumeFromVerifiedAppStage) {
    Assert-True (Test-Path -LiteralPath (Join-Path $nativeOutput 'XbPreview.Native.dll') -PathType Leaf) 'Verified native Release output is unavailable for resume.'
    Assert-True (Test-Path -LiteralPath $releaseNuGetPackages -PathType Container) 'Verified offline NuGet restore is unavailable for resume.'
} else {
    $nativeOutput = Reset-ArtifactDirectory $nativeOutput $artifacts
    $releaseNuGetPackages = Reset-ArtifactDirectory $releaseNuGetPackages $artifacts
    & $MsBuildPath $nativeProject /t:Rebuild /p:Configuration=Release /p:Platform=x64 "/p:GStreamerRoot=$GStreamerRoot" "/p:FfmpegMuxRuntimeRoot=$FfmpegRoot" /p:StageFfmpegMuxRuntime=true /m /v:minimal
    Assert-True ($LASTEXITCODE -eq 0) 'Native Release x64 build failed.'
    Assert-True (Test-Path -LiteralPath (Join-Path $nativeOutput 'XbPreview.Native.dll') -PathType Leaf) 'Native Release output is missing.'

    # Only the verified local package cache is a restore source. NuGet audit is
    # disabled here because this release assembly has no network dependency.
    & dotnet restore $hostProject -r $lock.product.rid --locked-mode --force-evaluate --packages $releaseNuGetPackages --source $NuGetPackagesRoot -p:NuGetAudit=false "-p:RuntimeFrameworkVersion=$($lock.product.runtimeVersion)" --verbosity minimal
    Assert-True ($LASTEXITCODE -eq 0) 'Offline locked NuGet restore failed.'
}

if ($ResumeFromVerifiedPublish -or $ResumeFromVerifiedAppStage) {
    Assert-True (Test-Path -LiteralPath (Join-Path $publish 'XiaobaiRecorder.exe') -PathType Leaf) 'Verified self-contained publish is unavailable for resume.'
    Assert-True (Test-Path -LiteralPath (Join-Path $publish 'XiaobaiRecorder.runtimeconfig.json') -PathType Leaf) 'Verified runtimeconfig is unavailable for resume.'
} else {
    $publish = Reset-ArtifactDirectory $publish $artifacts
    & dotnet publish $hostProject -c Release -r $lock.product.rid --self-contained true --no-restore -p:Platform=x64 -p:DebugType=None -p:DebugSymbols=false -p:Deterministic=true -p:ContinuousIntegrationBuild=true "-p:RuntimeFrameworkVersion=$($lock.product.runtimeVersion)" -o $publish
    Assert-True ($LASTEXITCODE -eq 0) 'Deterministic self-contained publish failed.'
}

$packageScript = Join-Path $repository 'tools\gstreamer\New-MvpAudioGStreamerPackage.ps1'
if ($ResumeFromVerifiedAppStage) {
    Assert-True (Test-Path -LiteralPath (Join-Path $appStage 'XiaobaiRecorder.exe') -PathType Leaf) 'Verified app-stage is unavailable for resume.'
} else {
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $packageScript -Configuration Release -SkipBuild -GStreamerRoot $GStreamerRoot -FfmpegRoot $FfmpegRoot -PublishPath $publish -PackagePath $package -VcRedistRoot $VcRedistRoot
    Assert-True ($LASTEXITCODE -eq 0) 'Deterministic approved runtime package assembly failed.'

    $appStage = Reset-ArtifactDirectory $appStage $artifacts
    Copy-Item -Path (Join-Path $package '*') -Destination $appStage -Recurse -Force

    # Stage only license/notice texts actually contained in the exact locked NuGet
    # packages. Missing reviewed material is intentionally left missing for the gate.
    $nugetMaterials = @(
        @('avalonia.angle.windows.natives', '2.1.27548.20260419', 'LICENSE', 'licenses\nuget\avalonia.angle.windows.natives\LICENSE'),
        @('skiasharp', '3.119.4', 'LICENSE.txt', 'licenses\nuget\skiasharp\LICENSE.txt'),
        @('skiasharp.nativeassets.win32', '3.119.4', 'LICENSE.txt', 'licenses\nuget\skiasharp.nativeassets.win32\LICENSE.txt'),
        @('skiasharp.nativeassets.win32', '3.119.4', 'THIRD-PARTY-NOTICES.txt', 'licenses\nuget\skiasharp.nativeassets.win32\THIRD-PARTY-NOTICES.txt'),
        @('harfbuzzsharp', '8.3.1.3', 'LICENSE.txt', 'licenses\nuget\harfbuzzsharp\LICENSE.txt'),
        @('harfbuzzsharp.nativeassets.win32', '8.3.1.3', 'LICENSE.txt', 'licenses\nuget\harfbuzzsharp.nativeassets.win32\LICENSE.txt'),
        @('harfbuzzsharp.nativeassets.win32', '8.3.1.3', 'THIRD-PARTY-NOTICES.txt', 'licenses\nuget\harfbuzzsharp.nativeassets.win32\THIRD-PARTY-NOTICES.txt'),
        @('system.io.pipelines', '8.0.0', 'LICENSE.TXT', 'licenses\nuget\system.io.pipelines\LICENSE.TXT'),
        @('system.io.pipelines', '8.0.0', 'THIRD-PARTY-NOTICES.TXT', 'licenses\nuget\system.io.pipelines\THIRD-PARTY-NOTICES.TXT'),
        @('microsoft.netcore.app.runtime.win-x64', '8.0.29', 'LICENSE.TXT', 'licenses\dotnet\Microsoft.NETCore.App.Runtime.win-x64\LICENSE.TXT'),
        @('microsoft.netcore.app.runtime.win-x64', '8.0.29', 'THIRD-PARTY-NOTICES.TXT', 'licenses\dotnet\Microsoft.NETCore.App.Runtime.win-x64\THIRD-PARTY-NOTICES.TXT'),
        @('microsoft.windowsdesktop.app.runtime.win-x64', '8.0.29', 'LICENSE', 'licenses\dotnet\Microsoft.WindowsDesktop.App.Runtime.win-x64\LICENSE')
    )
    foreach ($material in $nugetMaterials) {
        $source = Join-Path $releaseNuGetPackages ($material[0] + '\' + $material[1] + '\' + $material[2])
        Copy-Material $source (Join-Path $appStage $material[3])
    }
}

# Reviewed closure material is bridged only from the exact tracked v1.0.0
# compliance paths. The aggregate notice has one explicit untracked candidate
# mode; once committed, it follows the normal tracked-file path.
$optionalTrackedMaterials = @(
    @('THIRD-PARTY-NOTICES.md', 'THIRD-PARTY-NOTICES.md'),
    @('docs\release\compliance\v1.0.0\component-records\FFMPEG-BUILD.md', 'licenses\ffmpeg\RELEASE-BUILD-INFO.md'),
    @('docs\release\compliance\v1.0.0\component-records\GSTREAMER-BUILD.md', 'licenses\gstreamer\RELEASE-BUILD-INFO.md'),
    @('docs\release\compliance\v1.0.0\licenses\gstreamer\COPYING.gst-plugins-good', 'licenses\gstreamer\gst-plugins-good-1.0\README-LICENSE-INFO.txt'),
    @('docs\release\compliance\v1.0.0\licenses\avalonia\MIT.html', 'licenses\nuget\avalonia\LICENSE'),
    @('docs\release\compliance\v1.0.0\licenses\microcom.runtime\LICENSE', 'licenses\nuget\microcom.runtime\LICENSE'),
    @('docs\release\compliance\v1.0.0\licenses\tmds.dbus.protocol\COPYING', 'licenses\nuget\tmds.dbus.protocol\LICENSE'),
    @('docs\release\compliance\v1.0.0\EVIDENCE.md', 'licenses\dotnet\RELEASE-NOTICE-REVIEW.md'),
    @('docs\release\compliance\v1.0.0\EVIDENCE.md', 'licenses\vc-redist\REDISTRIBUTION.md')
)
foreach ($material in $optionalTrackedMaterials) {
    $sourcePath = Join-Path $repository $material[0]
    if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) { continue }
    $relativeSource = $material[0].Replace('\', '/')
    $trackedSource = @(& git -C $repository ls-files -- $relativeSource)
    $candidateNoticeAccepted = $ThirdPartyNoticesCandidate -and $relativeSource -ceq 'THIRD-PARTY-NOTICES.md'
    Assert-True ($candidateNoticeAccepted -or $trackedSource -ccontains $relativeSource) "Untracked release compliance material is not accepted: $relativeSource"
    Copy-Material $sourcePath (Join-Path $appStage $material[1])
}

# The companion archives remain separate release assets. Verify only their
# frozen recorded sizes, then bridge their already-reviewed compact manifests;
# do not rehash or regenerate either expanded source tree here.
$releaseCompanions = @(
    @('ffmpeg', 'xiaobai-recorder-1.0.0-ffmpeg-corresponding-source.tar.xz', [int64]818718304, 'docs\release\compliance\v1.0.0\component-records\FFMPEG-SOURCE-COMPANION.md'),
    @('gstreamer', 'xiaobai-recorder-1.0.0-gstreamer-corresponding-source.tar.xz', [int64]35709296, 'docs\release\compliance\v1.0.0\component-records\GSTREAMER-SOURCE-COMPANION.md')
)
$companionRoot = Join-Path $artifacts 'release-compliance\v1.0.0\release-companions'
foreach ($companion in $releaseCompanions) {
    $componentName = [string]$companion[0]
    $archiveName = [string]$companion[1]
    $expectedSize = [int64]$companion[2]
    $reviewRecord = Join-Path $repository ([string]$companion[3])
    $reviewRecordRelative = ([string]$companion[3]).Replace('\', '/')
    Assert-True (@(& git -C $repository ls-files -- $reviewRecordRelative) -ccontains $reviewRecordRelative) "Frozen companion review record is not tracked: $reviewRecordRelative"
    Assert-True (Test-Path -LiteralPath $reviewRecord -PathType Leaf) "Frozen companion review record is missing: $reviewRecord"
    $archivePath = Join-Path $companionRoot $archiveName
    Assert-True (Test-Path -LiteralPath $archivePath -PathType Leaf) "Reviewed $componentName release companion archive is missing: $archivePath"
    Assert-Equal (Get-Item -LiteralPath $archivePath).Length $expectedSize "$componentName release companion archive size"
    $sourceManifest = Join-Path $companionRoot ([IO.Path]::GetFileNameWithoutExtension([IO.Path]::GetFileNameWithoutExtension($archiveName)) + '\source-manifest.json')
    Assert-True (Test-Path -LiteralPath $sourceManifest -PathType Leaf) "Reviewed $componentName release companion manifest is missing: $sourceManifest"
    Copy-Material $sourceManifest (Join-Path $appStage ("release-companion-source\$componentName\SOURCE-MANIFEST.json"))
}

# Exact allowlist assertions after the bridge.
$actualPlugins = @(Get-ChildItem -LiteralPath (Join-Path $appStage 'gstreamer\plugins') -File | ForEach-Object Name | Sort-Object)
$expectedPlugins = @($lock.gstreamer.plugins | ForEach-Object name | Sort-Object)
Assert-True (($actualPlugins -join "`n") -ceq ($expectedPlugins -join "`n")) "Unexpected GStreamer plugin set. Expected [$($expectedPlugins -join ', ')]; received [$($actualPlugins -join ', ')]."
foreach ($file in $lock.gstreamer.supportDlls) {
    Assert-FileHash (Join-Path $appStage $file.name) $file.sha256 "app-stage GStreamer support $($file.name)"
}
foreach ($file in $lock.gstreamer.plugins) {
    Assert-FileHash (Join-Path $appStage ('gstreamer\plugins\' + $file.name)) $file.sha256 "app-stage GStreamer plugin $($file.name)"
}
$actualFfmpegFiles = @(Get-ChildItem -LiteralPath (Join-Path $appStage 'ffmpeg') -File | ForEach-Object Name | Sort-Object)
$expectedFfmpegFiles = @($lock.ffmpeg.files | ForEach-Object name | Sort-Object)
Assert-True (($actualFfmpegFiles -join "`n") -ceq ($expectedFfmpegFiles -join "`n")) "Unexpected FFmpeg file set. Expected [$($expectedFfmpegFiles -join ', ')]; received [$($actualFfmpegFiles -join ', ')]."
Assert-True (@(Get-ChildItem -LiteralPath $appStage -Recurse -File | Where-Object Name -In @('ffplay.exe', 'ffprobe.exe')).Count -eq 0) 'ffplay/ffprobe unexpectedly reached the app-stage.'
foreach ($file in $lock.ffmpeg.files) {
    Assert-FileHash (Join-Path $appStage ('ffmpeg\' + $file.name)) $file.sha256 "app-stage FFmpeg $($file.name)"
}
foreach ($file in $lock.vcRuntime.files) {
    Assert-FileHash (Join-Path $appStage $file.name) $file.sha256 "app-stage VC++ $($file.name)"
}
foreach ($asset in $lock.assets) {
    Assert-FileHash (Join-Path $appStage ([string]$asset.path).Replace('/', '\')) $asset.sha256 "app-stage product asset $($asset.path)"
}
Assert-True (Test-Path -LiteralPath (Join-Path $appStage 'LICENSE') -PathType Leaf) 'First-party LICENSE is missing from app-stage.'

$runtimeConfigPath = Join-Path $appStage 'XiaobaiRecorder.runtimeconfig.json'
$runtimeConfig = Read-JsonFile $runtimeConfigPath
$includedFrameworks = @($runtimeConfig.runtimeOptions.includedFrameworks)
Assert-True ($includedFrameworks.Count -gt 0) 'Self-contained runtime evidence is missing from runtimeconfig.json.'
foreach ($framework in $includedFrameworks) {
    Assert-Equal $framework.version $lock.product.runtimeVersion ".NET included framework $($framework.name)"
}

# Complete file-level manifest, outside app/ to avoid self-reference.
$manifestFiles = @(Get-ChildItem -LiteralPath $appStage -Recurse -File | Sort-Object FullName | ForEach-Object {
    [ordered]@{
        path = $_.FullName.Substring($appStage.Length + 1).Replace('\', '/')
        size = $_.Length
        sha256 = Get-Sha256 $_.FullName
    }
})
$manifest = [ordered]@{
    schemaVersion = 1
    product = $lock.product.name
    version = $lock.product.version
    rid = $lock.product.rid
    targetFramework = $lock.product.targetFramework
    sourceCommit = $productCommit
    frozenFoundationCommit = $foundationCommit
    releaseMetadataCommit = $commit
    buildTimestampUtc = $buildTimestamp
    dotnetRuntimeVersion = $lock.product.runtimeVersion
    nugetLockFiles = @('XbPreview.Host/packages.lock.json', 'XbPreview.Avalonia/packages.lock.json')
    fileCount = $manifestFiles.Count
    files = $manifestFiles
}
New-Item -ItemType Directory -Force -Path $releaseRoot | Out-Null
$manifestJsonPath = Join-Path $releaseRoot 'app-manifest.json'
$manifestTextPath = Join-Path $releaseRoot 'app-manifest.txt'
Write-Utf8File $manifestJsonPath (($manifest | ConvertTo-Json -Depth 8) + "`n")
$manifestLines = @(
    "Xiaobai Recorder 1.0.0 app-stage manifest",
    "RID: $($lock.product.rid)",
    "Product source commit: $productCommit",
    "Frozen release foundation commit: $foundationCommit",
    "Release metadata commit: $commit",
    "Build timestamp (UTC): $buildTimestamp",
    ".NET runtime: $($lock.product.runtimeVersion)",
    "File count: $($manifestFiles.Count)",
    '',
    'SHA-256                                                         Bytes  Relative path'
)
foreach ($file in $manifestFiles) {
    $manifestLines += ('{0} {1,13}  {2}' -f $file.sha256, $file.size, $file.path)
}
Write-Utf8File $manifestTextPath (($manifestLines -join "`n") + "`n")

# Fail-closed compliance gate. Presence is checked independently for runtime,
# license/notice, source/build information, and release-companion source.
$componentReports = @()
$blockers = @()
foreach ($component in $complianceSpec.components) {
    $runtimePresent = Test-AllFilesPresent $appStage @($component.runtimeFiles)
    $licensePresent = Test-AllFilesPresent $appStage @($component.licenseNoticeFiles)
    $sourcePresent = if ([bool]$component.sourceBuildInfoRequired) {
        Test-AllFilesPresent $appStage @($component.sourceBuildInfoFiles)
    } else { $true }
    $companionPresent = if ([bool]$component.releaseCompanionSourceRequired) {
        Test-AllFilesPresent $appStage @($component.releaseCompanionSourceFiles)
    } else { $true }
    $status = if ($runtimePresent -and $licensePresent -and $sourcePresent -and $companionPresent) { 'PASS' } else { 'BLOCKED' }
    $missingKinds = @()
    if (-not $runtimePresent) { $missingKinds += 'runtime file' }
    if (-not $licensePresent) { $missingKinds += 'license/notice' }
    if (-not $sourcePresent) { $missingKinds += 'source/build information' }
    if (-not $companionPresent) { $missingKinds += 'release-companion source' }
    $componentReports += [ordered]@{
        component = $component.name
        runtimeFilePresent = if ($runtimePresent) { 'PRESENT' } else { 'MISSING' }
        licenseNoticePresent = if ($licensePresent) { 'PRESENT' } else { 'MISSING' }
        sourceBuildInfoPresent = if (-not [bool]$component.sourceBuildInfoRequired) { 'NOT REQUIRED' } elseif ($sourcePresent) { 'PRESENT' } else { 'MISSING' }
        releaseCompanionSourcePresent = if (-not [bool]$component.releaseCompanionSourceRequired) { 'NOT REQUIRED' } elseif ($companionPresent) { 'PRESENT' } else { 'MISSING' }
        status = $status
    }
    if ($status -eq 'BLOCKED') {
        $blockers += [ordered]@{
            component = $component.name
            missing = $missingKinds -join ', '
            exactNextMaterialNeeded = $component.nextMaterial
            productCodeChangeNeeded = 'NO'
        }
    }
}
$thirdPartyNoticePresent = Test-Path -LiteralPath (Join-Path $appStage $complianceSpec.thirdPartyNoticesPath) -PathType Leaf
if (-not $thirdPartyNoticePresent) {
    $blockers += [ordered]@{
        component = 'THIRD-PARTY-NOTICES'
        missing = 'reviewed aggregate third-party notices file'
        exactNextMaterialNeeded = 'Create one reviewed THIRD-PARTY-NOTICES.md only after every component statement and source obligation is evidentially complete.'
        productCodeChangeNeeded = 'NO'
    }
}
$overallCompliance = if ($blockers.Count -eq 0) { 'PASS' } else { 'BLOCKED' }
$complianceReport = [ordered]@{
    schemaVersion = 1
    product = "$($lock.product.name) $($lock.product.version)"
    appStage = 'artifacts/packaging/xiaobai-recorder-1.0.0/app'
    generatedAtUtc = $buildTimestamp
    components = $componentReports
    thirdPartyNotices = if ($thirdPartyNoticePresent) { 'PRESENT' } else { 'MISSING' }
    blockers = $blockers
    overall = $overallCompliance
}
$complianceJsonPath = Join-Path $releaseRoot 'compliance-report.json'
$complianceTextPath = Join-Path $releaseRoot 'compliance-report.md'
Write-Utf8File $complianceJsonPath (($complianceReport | ConvertTo-Json -Depth 8) + "`n")
$complianceLines = @(
    '# Xiaobai Recorder 1.0.0 release compliance gate',
    '',
    '| Component | Runtime | License/notice | Source/build info | Companion source | Status |',
    '| --- | --- | --- | --- | --- | --- |'
)
foreach ($component in $componentReports) {
    $complianceLines += "| $($component.component) | $($component.runtimeFilePresent) | $($component.licenseNoticePresent) | $($component.sourceBuildInfoPresent) | $($component.releaseCompanionSourcePresent) | $($component.status) |"
}
$complianceLines += @('', "THIRD-PARTY-NOTICES.md: $($complianceReport.thirdPartyNotices)", '', "Overall: $overallCompliance", '', '## Remaining blockers', '')
if ($blockers.Count -eq 0) {
    $complianceLines += 'None.'
} else {
    foreach ($blocker in $blockers) {
        $complianceLines += "- $($blocker.component): missing $($blocker.missing). $($blocker.exactNextMaterialNeeded) Product code change needed: NO."
    }
}
Write-Utf8File $complianceTextPath (($complianceLines -join "`n") + "`n")

$result = [ordered]@{
    appStage = $appStage
    manifestJson = $manifestJsonPath
    manifestText = $manifestTextPath
    complianceJson = $complianceJsonPath
    complianceText = $complianceTextPath
    fileCount = $manifestFiles.Count
    compliance = $overallCompliance
    finalStatus = if ($overallCompliance -eq 'PASS') { 'RELEASE-APP-STAGE-FOUNDATION-READY' } else { 'RELEASE-APP-STAGE-FOUNDATION-READY-COMPLIANCE-BLOCKED' }
}
Write-Utf8File (Join-Path $releaseRoot 'release-result.json') (($result | ConvertTo-Json -Depth 4) + "`n")

Write-Host "App-stage: $appStage"
Write-Host "Manifest files: $($manifestFiles.Count)"
Write-Host "Compliance: $overallCompliance"
Write-Host $result.finalStatus
if ($overallCompliance -ne 'PASS') { exit 2 }
