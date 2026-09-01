[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$version = '1.28.6'
$architecture = 'msvc-x86_64'
$installerSha256 = '059251444D1267B486EBA390B18D25FED87E10315E72F757EC6C7E912FA746B5'
$url = "https://gstreamer.freedesktop.org/data/pkg/windows/$version/msvc/gstreamer-1.0-msvc-x86_64-$version.exe"
$repository = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$cache = Join-Path $repository "artifacts\cache\gstreamer-$version"
$sdk = Join-Path $repository "artifacts\sdk\gstreamer-$version"
$installer = Join-Path $cache "gstreamer-1.0-$architecture-$version.exe"
$installLog = Join-Path $cache "install-$version.log"
$header = Join-Path $sdk 'include\gstreamer-1.0\gst\gst.h'
$importLibrary = Join-Path $sdk 'lib\gstreamer-1.0.lib'
$versionHeader = Join-Path $sdk 'include\gstreamer-1.0\gst\gstversion.h'

if ((Test-Path -LiteralPath $header) -and
    (Test-Path -LiteralPath $importLibrary) -and
    (Test-Path -LiteralPath $versionHeader)) {
    Write-Host "Pinned GStreamer SDK already present: $sdk"
    exit 0
}

New-Item -ItemType Directory -Force -Path $cache | Out-Null
if (-not (Test-Path $installer)) {
    Invoke-WebRequest -UseBasicParsing -Uri $url -OutFile $installer
}
$actualSha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $installer).Hash
if ($actualSha256 -ne $installerSha256) {
    throw "GStreamer installer SHA-256 mismatch. Expected $installerSha256; received $actualSha256."
}

New-Item -ItemType Directory -Force -Path $sdk | Out-Null
$installerArguments = @(
    '/SP-',
    '/VERYSILENT',
    '/NORESTART',
    '/CURRENTUSER',
    '/TYPE=devel',
    '/TASKS=""',
    ('/DIR="{0}"' -f $sdk),
    ('/LOG="{0}"' -f $installLog)
)
$process = Start-Process -FilePath $installer -ArgumentList $installerArguments -PassThru -WindowStyle Hidden
$timeoutMilliseconds = [int][TimeSpan]::FromMinutes(10).TotalMilliseconds
if (-not $process.WaitForExit($timeoutMilliseconds)) {
    & "$env:SystemRoot\System32\taskkill.exe" /PID $process.Id /T /F | Out-Null
    throw "GStreamer $version installation timed out after 10 minutes. The installer process tree was terminated. See $installLog."
}

if ($process.ExitCode -ne 0 -or
    -not (Test-Path -LiteralPath $header) -or
    -not (Test-Path -LiteralPath $importLibrary) -or
    -not (Test-Path -LiteralPath $versionHeader) -or
    -not (Test-Path -LiteralPath $installLog)) {
    throw "GStreamer $version development runtime installation failed with exit code $($process.ExitCode)."
}

Write-Host "Installed GStreamer $version $architecture at $sdk"
Write-Host "Installer log: $installLog"
