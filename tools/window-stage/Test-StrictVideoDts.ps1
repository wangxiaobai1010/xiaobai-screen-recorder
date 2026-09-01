[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [string[]]$InputPath = @(),

    [string]$FfprobePath = '',

    [switch]$SelfTest
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Require([bool]$Condition, [string]$Message) {
    if (-not $Condition) {
        throw "STRICT-DTS-GATE FAIL: $Message"
    }
}

function Measure-DtsSequence(
    [object[]]$Packets,
    [string]$Label,
    [object]$Stream = $null) {
    Require ($Packets.Count -gt 0) "$Label has no video packets"

    [long]$firstDts = 0
    [long]$lastDts = 0
    [long]$previousDts = 0
    [long]$minimumPositiveDelta = [long]::MaxValue
    [long]$maximumPositiveDelta = [long]::MinValue
    [int]$equalCount = 0
    [int]$decreasingCount = 0
    $firstEqual = $null
    $firstDecrease = $null

    for ($index = 0; $index -lt $Packets.Count; $index++) {
        $packet = $Packets[$index]
        Require ($null -ne $packet.dts -and "$($packet.dts)" -ne 'N/A') `
            "$Label packet $index has no DTS"
        [long]$currentDts = $packet.dts
        if ($index -eq 0) {
            $firstDts = $currentDts
        }
        else {
            [long]$delta = $currentDts - $previousDts
            if ($delta -eq 0) {
                $equalCount++
                if ($null -eq $firstEqual) {
                    $firstEqual = [pscustomobject]@{
                        PacketIndex = $index
                        PreviousDts = $previousDts
                        Dts = $currentDts
                        Pts = $packet.pts
                        DtsTime = $packet.dts_time
                        PtsTime = $packet.pts_time
                    }
                }
            }
            elseif ($delta -lt 0) {
                $decreasingCount++
                if ($null -eq $firstDecrease) {
                    $firstDecrease = [pscustomobject]@{
                        PacketIndex = $index
                        PreviousDts = $previousDts
                        Dts = $currentDts
                        Pts = $packet.pts
                        DtsTime = $packet.dts_time
                        PtsTime = $packet.pts_time
                    }
                }
            }
            else {
                $minimumPositiveDelta = [Math]::Min(
                    $minimumPositiveDelta, $delta)
                $maximumPositiveDelta = [Math]::Max(
                    $maximumPositiveDelta, $delta)
            }
        }
        $previousDts = $currentDts
        $lastDts = $currentDts
    }

    return [pscustomobject]@{
        Path = $Label
        PacketCount = $Packets.Count
        EqualDtsCount = $equalCount
        DecreasingDtsCount = $decreasingCount
        FirstDts = $firstDts
        LastDts = $lastDts
        MinimumPositiveDtsDelta = if (
            $minimumPositiveDelta -eq [long]::MaxValue) { $null }
            else { $minimumPositiveDelta }
        MaximumPositiveDtsDelta = if (
            $maximumPositiveDelta -eq [long]::MinValue) { $null }
            else { $maximumPositiveDelta }
        FirstEqual = $firstEqual
        FirstDecrease = $firstDecrease
        Codec = if ($null -ne $Stream) { $Stream.codec_name } else { $null }
        Profile = if ($null -ne $Stream) { $Stream.profile } else { $null }
        HasBFrames = if ($null -ne $Stream) { $Stream.has_b_frames } else { $null }
        TimeBase = if ($null -ne $Stream) { $Stream.time_base } else { $null }
    }
}

function Assert-StrictDts([object]$Audit) {
    if ($Audit.EqualDtsCount -ne 0 -or
        $Audit.DecreasingDtsCount -ne 0) {
        $first = if ($null -ne $Audit.FirstDecrease) {
            $Audit.FirstDecrease
        }
        else {
            $Audit.FirstEqual
        }
        throw ("STRICT-DTS-GATE FAIL: {0}: current DTS must be greater than previous DTS; packet={1}, previous={2}, current={3}, equal={4}, decreasing={5}" -f
            $Audit.Path,
            $first.PacketIndex,
            $first.PreviousDts,
            $first.Dts,
            $Audit.EqualDtsCount,
            $Audit.DecreasingDtsCount)
    }
}

if ($SelfTest) {
    $equalOnlyFixture = @(
        [pscustomobject]@{ dts = 0; pts = 0; dts_time = '0'; pts_time = '0' },
        [pscustomobject]@{ dts = 1000; pts = 1000; dts_time = '0.016667'; pts_time = '0.016667' },
        [pscustomobject]@{ dts = 1000; pts = 1000; dts_time = '0.016667'; pts_time = '0.016667' },
        [pscustomobject]@{ dts = 2000; pts = 2000; dts_time = '0.033333'; pts_time = '0.033333' }
    )
    $fixtureAudit = Measure-DtsSequence $equalOnlyFixture `
        'equal-dts-only-negative-fixture'
    Require ($fixtureAudit.EqualDtsCount -eq 1) `
        'equal-DTS fixture did not contain exactly one equality'
    Require ($fixtureAudit.DecreasingDtsCount -eq 0) `
        'equal-DTS fixture unexpectedly contained a regression'
    $rejected = $false
    try {
        Assert-StrictDts $fixtureAudit
    }
    catch {
        $rejected = $_.Exception.Message.Contains(
            'current DTS must be greater than previous DTS')
    }
    Require $rejected 'equal-DTS negative fixture was not rejected'
    Write-Output 'EQUAL-DTS-NEGATIVE-FIXTURE = PASS'
}

if ($InputPath.Count -eq 0) {
    Require $SelfTest 'provide -InputPath or -SelfTest'
    Write-Output 'STRICT-DTS-GATE = PASS'
    exit 0
}

if ([string]::IsNullOrWhiteSpace($FfprobePath)) {
    $repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
    $toolchainRoot = Join-Path $repoRoot 'artifacts\audio-v2-toolchain'
    $FfprobePath = Get-ChildItem -LiteralPath $toolchainRoot `
        -Filter 'ffprobe.exe' -File -Recurse |
        Select-Object -First 1 -ExpandProperty FullName
}
Require (-not [string]::IsNullOrWhiteSpace($FfprobePath) -and
    (Test-Path -LiteralPath $FfprobePath -PathType Leaf)) `
    'pinned ffprobe.exe was not found'

$audits = @()
foreach ($candidate in $InputPath) {
    $path = (Resolve-Path -LiteralPath $candidate).Path
    $jsonText = & $FfprobePath -v error -select_streams 'v:0' `
        -show_packets -show_streams `
        -show_entries `
            'stream=codec_name,profile,has_b_frames,time_base:packet=pts,dts,pts_time,dts_time' `
        -of json -- $path
    Require ($LASTEXITCODE -eq 0) "ffprobe failed for $path"
    $probe = $jsonText | ConvertFrom-Json
    Require ($null -ne $probe.streams -and $probe.streams.Count -eq 1) `
        "$path does not have exactly one selected video stream"
    $audits += Measure-DtsSequence @($probe.packets) $path $probe.streams[0]
}

$audits | ConvertTo-Json -Depth 6
foreach ($audit in $audits) {
    Assert-StrictDts $audit
}
Write-Output 'STRICT-DTS-GATE = PASS'
