# MVP Audio Subsystem Freeze

- Freeze date: 2026-08-12
- Branch: `feature/audio-screenrecorderlib-adapter`
- Pre-freeze ancestor: `ed6d9735eb604208c2d3fa7b273ef8cbe5f65c58`
- Annotated tag: `mvp-audio-subsystem-pass-2026-08-12`
- Frozen result: `AUDIO-SUBSYSTEM-INTEGRATION-XbAudioAdapter-PASS`

## Frozen subsystem

The MVP audio path is frozen at the first product-level human Gate that passed
on the target Windows machine:

```text
Microsoft MIT shared-mode WASAPI capture
  -> ScreenRecorderLib v6.6.0 AudioManager / resampler / mixer / lifecycle
  -> XbAudioAdapter
  -> PCM16 48 kHz stereo
  -> Media Foundation AAC stream
  -> the existing Media Foundation Sink Writer (H.264 + AAC MP4)
  -> existing Finalize / Validate / Safe Publish
```

The following behavior is frozen:

- Microsoft MIT `WASAPICapture` replacement for microphone capture and render
  endpoint loopback.
- ScreenRecorderLib `AudioManager`, device notification, resampler, mixer,
  Start/Stop and disposal behavior.
- Thin `XbAudioAdapter` boundary.
- `MfAacAudioStream` PCM-to-Media-Foundation handoff.
- The optional AAC stream beside the unchanged H.264 stream in the existing
  Sink Writer.
- The validated Dual behavior and unity donor volumes (`1.0` input and `1.0`
  output).

No further volume tuning, ducking, AGC, DSP, mixer changes, donor replacement,
or fallback audio stack is part of this freeze.

## Adapter and product boundary

`XbAudioAdapter` owns only:

- configuration-to-donor option mapping;
- Start, clear-at-recording-boundary, mixed-PCM pull, Stop and final release;
- aggregate state and HRESULT translation;
- paired Media Foundation runtime ownership required by the donor resampler.

It does not own capture, endpoint resolution, hotplug policy, resampling,
mixing, gain, DSP, audio clocks, AAC encoding, MP4 writing, or Safe Publish.
Those remain in the vendored donor block, Windows/Media Foundation, or the
existing recorder lifecycle as appropriate.

The H.264/NV12/D3D11 path and `VideoEncoderTimestamp` remain the video master.
Accepted video sample end-times request donor PCM; actual PCM frame counts
advance the audio cursor without changing video timestamps. Audio Stop and
EOS precede the single Sink Writer Finalize. Existing validation and Safe
Publish still decide whether the working MP4 may be published.

The old GStreamer audio source files and runtime staging remain as historical
assets. The frozen recording path does not call GStreamer capture or FFmpeg
audio remux. The GStreamer device monitor remains available to the existing UI
and explicit-endpoint selection path; default microphone resolution is owned
by the donor WASAPI path.

## Modified file inventory

### Product integration

- `XbPreview.Native/XbAudioAdapter.h`
- `XbPreview.Native/XbAudioAdapter.cpp`
- `XbPreview.Native/MfAacAudioStream.h`
- `XbPreview.Native/MfAacAudioStream.cpp`
- `XbPreview.Native/MfH264SinkWriterSession.h`
- `XbPreview.Native/MfH264SinkWriterSession.cpp`
- `XbPreview.Native/VideoEncoderConsumer.cpp`
- `XbPreview.Native/PreviewEngine.cpp`
- `XbPreview.Native/VideoEncoderDiagnostics.h`
- `XbPreview.Native/VideoEncoderDiagnostics.cpp`

### Build integration

- `XbPreview.Native/XbPreview.Native.vcxproj`
- `XbPreview.P1D-A1.sln`

### Vendored audio block and provenance

- `third_party/screenrecorderlib-audio/.gitignore`
- `third_party/screenrecorderlib-audio/AudioManager.h`
- `third_party/screenrecorderlib-audio/AudioManager.cpp`
- `third_party/screenrecorderlib-audio/WASAPICapture.h`
- `third_party/screenrecorderlib-audio/WASAPICapture.cpp`
- `third_party/screenrecorderlib-audio/WASAPINotify.h`
- `third_party/screenrecorderlib-audio/WASAPINotify.cpp`
- `third_party/screenrecorderlib-audio/WWMFResampler.h`
- `third_party/screenrecorderlib-audio/WWMFResampler.cpp`
- `third_party/screenrecorderlib-audio/CoreAudio.util.h`
- `third_party/screenrecorderlib-audio/CoreAudio.util.cpp`
- `third_party/screenrecorderlib-audio/DynamicWait.h`
- `third_party/screenrecorderlib-audio/DynamicWait.cpp`
- `third_party/screenrecorderlib-audio/log.h`
- `third_party/screenrecorderlib-audio/Log.cpp`
- `third_party/screenrecorderlib-audio/LogGlobals.cpp`
- `third_party/screenrecorderlib-audio/CommonTypes.h`
- `third_party/screenrecorderlib-audio/Util.h`
- `third_party/screenrecorderlib-audio/cleanup.h`
- `third_party/screenrecorderlib-audio/ScreenRecorderLibAudio.vcxproj`
- `third_party/screenrecorderlib-audio/SOURCE.md`
- `third_party/screenrecorderlib-audio/LICENSE-SCREENRECORDERLIB.txt`
- `third_party/screenrecorderlib-audio/LICENSE-MICROSOFT-WINDOWS-CLASSIC-SAMPLES.txt`

### Freeze record

- `docs/stages/MVP-AUDIO-SUBSYSTEM-FREEZE.md`

No runtime media, build output, `.codex-tmp` harness, DLL, LIB, OBJ, PDB, EXE,
WAV, MP4, user recording, or unrelated worktree file is included in the
freeze commit.

## License attribution

### ScreenRecorderLib

- Project: ScreenRecorderLib
- Official repository: <https://github.com/sskodje/ScreenRecorderLib>
- Release: `v6.6.0`
- Pinned commit: `39ad1e2f1750fa06669b73743dbbaa25371dec21`
- License: MIT
- Copyright: `Copyright (c) 2017 Sverre Skodje`
- Attribution file: `third_party/screenrecorderlib-audio/LICENSE-SCREENRECORDERLIB.txt`

The vendored block contains `AudioManager`, notification, Media Foundation
resampling, Core Audio helpers, lifecycle and the directly required support
definitions. Mechanical audio-only support extraction and read-only HRESULT
exposure are documented in `third_party/screenrecorderlib-audio/SOURCE.md`.

### Microsoft Windows classic samples

- Project: Microsoft Windows classic samples
- Official repository: <https://github.com/microsoft/Windows-classic-samples>
- Pinned commit: `77f217b3f89d4dac7864a62cc91ff7b569f26a50`
- Primary sample: `Samples/Win7Samples/multimedia/audio/CaptureSharedTimerDriven`
- Packet-drain and loopback reference:
  `Samples/ApplicationLoopback/cpp/LoopbackCapture.cpp`
- License: MIT
- Copyright: `Copyright (c) Microsoft Corporation`
- Attribution file:
  `third_party/screenrecorderlib-audio/LICENSE-MICROSOFT-WINDOWS-CLASSIC-SAMPLES.txt`

`WASAPICapture.h/.cpp` preserve Microsoft attribution and are the clean
replacement for the legacy file whose provenance was not closed. They expose
the compatibility surface required by the MIT ScreenRecorderLib callers; the
unknown-license legacy implementation is not included.

### Product AAC handoff provenance

`XbPreview.Native/MfAacAudioStream.cpp` mechanically maps the MIT
ScreenRecorderLib v6.6.0 `OutputManager` AAC output media type, PCM input media
type, empty-frame policy, and Sink Writer sample handoff into the existing
product Sink Writer. The source records those donor method names at the
corresponding seams. This code is covered by the ScreenRecorderLib attribution
above; it does not import the donor video or recording-manager graph.

The two complete MIT texts and `SOURCE.md` must remain with redistributed
source or binary materials as applicable. Windows WASAPI, Media Foundation,
ATL and the Windows SDK are system/toolchain components and are not vendored
as third-party source in this change.

Distribution note: the existing historical `artifacts/package/win-x64`
output and packaging script do not yet contain or stage these two new license
files. This freeze closes source provenance and the product-level audio Gate;
it is not a binary-distribution compliance certification. A future formal
package must include both complete MIT license files (and may add a combined
third-party notice) before commercial distribution.

## Gate evidence

Gate media is local evidence and is intentionally not committed.

### 1. Microphone capture

- File: `C:\Users\Administrator\Desktop\WASAPICapture-Microsoft-MIT-MicGate.wav`
- SHA-256: `9C75AD2F7DD574867B0E13E5BA31FCA2B9EB9C2C96C9C5CF1060740C6E893C36`
- Format: PCM signed 16-bit little-endian, 48 kHz, stereo
- Duration: `20.000000 s`
- Human result: `Mic 正常`
- Verdict: PASS

### 2. System loopback

- File: `C:\Users\Administrator\Desktop\WASAPICapture-Microsoft-MIT-SystemGate.wav`
- SHA-256: `20E7EE36FB7843CA65BCF90DD09A1232567E487D3AD5DE07C70BCE46F31D1E81`
- Format: PCM signed 16-bit little-endian, 48 kHz, stereo
- Duration: `20.010000 s`
- Human result: `System 正常`
- Verdict: PASS

### 3. Donor Dual mixer

- File: `C:\Users\Administrator\Desktop\WASAPICapture-Microsoft-MIT-DualGate.wav`
- Log: `C:\Users\Administrator\Desktop\WASAPICapture-Microsoft-MIT-DualGate.wav.donor.log`
- SHA-256: `7AAE68243A29E3EBEEADE2D2B16109C6B15A05D8B3DCDAA3C67FF78576BDAA77`
- Format: PCM signed 16-bit little-endian, 48 kHz, stereo
- Duration: `29.960854 s`
- Log facts: both `AudioOutputDevice` and `AudioInputDevice` reported started
- Human result: A; both sources present, clean, and Start/Stop normal
- Verdict: PASS

### 4. Final product MP4

- Session: `4F45589C-2BFD-4C13-95E6-075BB93B9F86`
- File: `artifacts/p2.5a-recordings/4F45589C-2BFD-4C13-95E6-075BB93B9F86.mp4`
- SHA-256: `1A092921BEA6A731422C50797E4E1B954AF7A48B253851F952C97D29B531256F`
- Size: `24,284,535 bytes`
- Duration: `51.285313 s`
- Video: H.264 Constrained Baseline, 1920x1080
- Audio: AAC-LC, 48 kHz, stereo, approximately 96 kb/s
- Human result: A; video, system audio, microphone audio and Dual are normal,
  with no obvious added noise
- Verdict: PASS

Manifest evidence:

- `state = Completed`
- worker exited and recording resources released
- `residualOutstanding = 0`
- Finalize attempted exactly once, `HRESULT = 0`
- validation passed, `HRESULT = 0`
- publish completed, `HRESULT = 0`
- post-publish file identity matched
- terminal error category `None`

Diagnostic evidence:

- backend `ScreenRecorderLib-6.6.0/Microsoft-WASAPI/MF-AAC`
- mode `Dual`
- audio Start/Stop `HRESULT = 0`
- `9,809,864` mixed PCM bytes pulled
- `2,461,426` PCM frames and `2,264` AAC input samples written
- zero audio write failures
- Source Reader validation PASS

A complete read/decode probe reached the end of both H.264 and AAC streams
with exit code 0. Automatic media inspection supplements, but does not replace,
the human A verdict.

## Freeze build verification

The following validation was rerun from the freeze worktree without cleaning,
resetting, restoring packages, or changing source:

- Release x64 solution build: PASS, exit code 0.
- `XbPreview.GStreamer.Tests.exe`: PASS, exit code 0.
- Managed `--mvp-audio-mode-routing`: PASS, exit code 0.
- Managed `--microphone-selector-abi`: PASS, exit code 0.
- `git diff --check`: PASS.
- Release `XbPreview.Native.dll` developer-path scan: no user or repository
  absolute path found.

The only build warning was `NU1900`, because the offline environment could not
query the NuGet vulnerability index. It did not affect compilation or tests.

## Frozen guarantees and non-goals

This tag freezes the evidence-backed MVP path, not every future audio feature.
It guarantees the tested Mic, System, Dual, PCM, AAC, MP4, Stop, Finalize,
Validate and Safe Publish behavior on the Gate machine and configuration.

It does not authorize or claim:

- microphone loudness optimization;
- ducking, AGC, noise suppression or other DSP;
- alternative donor or fallback selection;
- mixer/resampler tuning;
- deletion of old GStreamer evidence assets;
- long-duration, hotplug, every-device, or every-Windows-version certification;
- migration of historical packaging scripts or retirement of old runtime
  staging.

Any change to the frozen capture, mixer, resampler, volume, Adapter, AAC or
Dual behavior requires a new scoped stage and new human Gates.

## Final freeze verdict

`MVP-AUDIO-SUBSYSTEM-PASS-FROZEN`
