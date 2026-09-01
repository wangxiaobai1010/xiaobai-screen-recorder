# ScreenRecorderLib audio vendor provenance

This directory vendors the complete ScreenRecorderLib v6.6.0 native audio
subsystem behind its existing `AudioManager` contract. It is intentionally an
independent static library and does not include ScreenRecorderLib's video,
encoder, or recording-manager graph.

## ScreenRecorderLib source

- Official repository: <https://github.com/sskodje/ScreenRecorderLib>
- Release/tag: `v6.6.0`
- Pinned commit: `39ad1e2f1750fa06669b73743dbbaa25371dec21`
- License: `LICENSE-SCREENRECORDERLIB.txt` (MIT)

Vendored without audio-algorithm changes:

- `AudioManager.h/.cpp`
- `WASAPINotify.h/.cpp`
- `WWMFResampler.h/.cpp`
- `CoreAudio.util.h/.cpp`
- `DynamicWait.h/.cpp`
- `Log.h/.cpp`

Mechanical build-boundary extractions:

- `CommonTypes.h` retains only the donor's `AUDIO_OPTIONS` definition.
- `Util.h` retains only `RETURN_ON_BAD_HR` and `s2ws`, the helpers used by the
  audio sources.
- `cleanup.h` retains only the RAII helpers referenced by the audio sources.
- `LogGlobals.cpp` moves the donor's unchanged debug/release log defaults out
  of `RecordingManager.cpp`, avoiding a dependency on the donor video graph.

These extractions remove unrelated video types and includes only. Audio
defaults, device policy, mixer/downmix behavior, volume behavior, resampler,
capture lifecycle, notification behavior, and Start/Stop ordering are not
changed.

Mechanical integration observations are deliberately limited to HRESULT
facts and do not alter capture or audio data:

- `WASAPICapture::GetCaptureResult` exposes the replacement transport's
  existing atomic terminal result as a read-only value.
- `AudioManager::GetCaptureResult` aggregates those read-only facts for the
  enabled legs so a failed capture cannot be mistaken for valid silence.
- `AudioManager::ConfigureAudioCapture` still performs the donor's output-then-
  input best-effort sequence, but retains the first failed HRESULT instead of
  allowing the later leg to overwrite it with success.

## Microsoft WASAPI replacement source

- Official repository: <https://github.com/microsoft/Windows-classic-samples>
- Pinned commit: `77f217b3f89d4dac7864a62cc91ff7b569f26a50`
- Primary reference sample:
  `Samples/Win7Samples/multimedia/audio/CaptureSharedTimerDriven`
- Packet-drain and loopback reference:
  `Samples/ApplicationLoopback/cpp/LoopbackCapture.cpp`
- License: `LICENSE-MICROSOFT-WINDOWS-CLASSIC-SAMPLES.txt` (MIT)

`WASAPICapture.h/.cpp` are the previously validated compatibility layer whose
capture transport is adapted from that pinned Microsoft sample. Their public
surface is the one required by the MIT-licensed `AudioManager` and
`WASAPINotify` callers. They do not copy the legacy ScreenRecorderLib
`WASAPICapture` implementation whose upstream license could not be closed.

## Consumer requirements

- Windows x64, MSVC v143, C++17, dynamic CRT (`/MD` or `/MDd`).
- ATL headers (`atlbase.h`) and Windows 10 SDK.
- Media Foundation must be initialized by the host before use and shut down
  only after all capture/resampler objects are destroyed.
- The recording thread must initialize COM (MTA).
- Link the final binary with: `avrt.lib`, `ole32.lib`, `propsys.lib`,
  `mfplat.lib`, `mf.lib`, `mfuuid.lib`, and `wmcodecdspuuid.lib`.
