[CmdletBinding()]
param(
    [string]$Repository,
    [string]$BaseHead = '09238f51ddcded3d65e11533444b86dfe43ca5d9'
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($Repository)) {
    $Repository = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
}

function Assert-True([bool]$Condition, [string]$Message) {
    if (-not $Condition) {
        throw $Message
    }
}

$meter = Get-Content -LiteralPath (
    Join-Path $Repository 'XbPreview.Native\AudioEndpointLevelMonitor.cpp') -Raw
$engine = Get-Content -LiteralPath (
    Join-Path $Repository 'XbPreview.Native\PreviewEngine.cpp') -Raw
$identity = Get-Content -LiteralPath (
    Join-Path $Repository 'XbPreview.Native\GStreamerMicrophoneDeviceMonitor.cpp') -Raw
$abi = Get-Content -LiteralPath (
    Join-Path $Repository 'XbPreview.Native\XbPreviewApi.h') -Raw
$identitySetError = [regex]::Match(
    $identity,
    'void SetError\(.*?(?=\s+void Reenumerate\()',
    [System.Text.RegularExpressions.RegexOptions]::Singleline).Value
$identityReenumerate = [regex]::Match(
    $identity,
    'void Reenumerate\(\).*?(?=\s+void BusMain\()',
    [System.Text.RegularExpressions.RegexOptions]::Singleline).Value

Assert-True ($meter.Contains('IAudioMeterInformation')) `
    'Endpoint meter does not activate IAudioMeterInformation.'
Assert-True ($meter.Contains('GetPeakValue')) `
    'Endpoint meter does not read GetPeakValue.'
Assert-True ($meter.Contains('SampleInterval = std::chrono::milliseconds(50)')) `
    'Endpoint meter is not fixed at the intended 20 Hz ceiling.'
Assert-True (-not ($meter -match '\bIAudioClient\b|\bIAudioCaptureClient\b|\bEnumAudioEndpoints\b|\bloopback\b|\bGStreamer\b')) `
    'Endpoint meter contains a capture, enumeration, loopback, or GStreamer path.'
Assert-True ($identity.Contains('defaultSystemEndpointId') -and
    $identity.Contains('GStreamerAudioCore::ResolveDevices')) `
    'System endpoint identity is not exposed from the existing resolver facts.'
Assert-True (-not $identitySetError.Contains(
    'defaultSystemEndpointId.clear();')) `
    'A microphone catalog error still clears the System endpoint identity.'
Assert-True ($identityReenumerate.Contains(
    'if (!nextSystemDefault.empty())') -and
    $identityReenumerate.Contains(
        'defaultSystemEndpointId = std::move(nextSystemDefault);')) `
    'A microphone-only catalog refresh still revokes the System endpoint identity.'
Assert-True ($engine.Contains('audioEndpointLevelMonitor_.Snapshot()') -and
    $engine.Contains('activeSystemAudioEndpointId_')) `
    'PreviewEngine does not publish cached independent endpoint facts.'
Assert-True ($engine.Contains(
    'result.microphoneEndpointId =') -and
    $engine.Contains('activeMicrophoneDevice_->EndpointId()') -and
    $engine.Contains('result.microphoneEnabled = true;')) `
    'Recording does not assign the already-locked Mic endpoint to the observer.'
Assert-True ($engine.Contains(
    '? levels.microphonePeakAbsolutePcm16') -and
    $engine.Contains('snapshot.microphoneRmsPcm16 = recordingMicrophoneObserved') -and
    $engine.Contains('? 0.0')) `
    'Recording Mic peak/RMS snapshot semantics are not explicit.'
Assert-True ($abi.Contains('XbAudioEndpointLevelFlagsV1_SystemMeterAvailable') -and
    $abi.Contains('XbAudioEndpointLevelFlagsV1_MicrophoneMeterAvailable')) `
    'Endpoint availability facts are missing from the ABI.'
Assert-True ($meter.Contains('meter.Reset();') -and
    $meter.Contains('return 0.0f;')) `
    'Meter failure does not deterministically fall back to unavailable zero.'

$changed = @(git -C $Repository diff --name-only $BaseHead --)
Assert-True ($LASTEXITCODE -eq 0) 'Unable to inspect the task diff.'
Assert-True (-not ($changed -contains 'XbPreview.Native/GStreamerAudioFinalizer.cpp')) `
    'GStreamerAudioFinalizer.cpp changed in the endpoint meter task.'
Assert-True (-not ($changed -contains 'XbPreview.Native/GStreamerAudioCore.cpp')) `
    'The formal GStreamer Recording graph changed in the endpoint meter task.'
Assert-True (-not ($changed -contains 'XbPreview.Native/VideoEncoderConsumer.cpp')) `
    'The formal Recording consumer changed in the endpoint meter task.'
Assert-True (-not ($changed -contains 'XbPreview.Native/XbPreviewApi.h')) `
    'The AudioControlSnapshotV1 ABI changed in the endpoint meter task.'

Write-Output 'PASS: locked Mic identity, observer isolation, stateful snapshot semantics, and unchanged Recording graph/ABI.'
