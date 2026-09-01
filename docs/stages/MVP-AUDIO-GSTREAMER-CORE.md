# MVP Audio Core — GStreamer 1.28.6

## Decision

The MVP capture and microphone-processing runtime is owned entirely by the private GStreamer 1.28.6 MSVC x86_64 runtime. NAudio, miniaudio, the custom WASAPI capture/timeline/mixer/PCM-to-FLAC path, custom DSP, and the legacy FFmpeg `agate`/expander speech patch are not part of the product runtime. Mature FFmpeg file mastering is the only post-capture audio boundary. The abandoned SoundFlow experiment is preserved as `docs/stages/abandoned/MVP-AUDIO-SOUNDFLOW-NO-GO.patch` (SHA-256 `4A1E5553235264A53DCA418F4CAC5A18C5767E0FE650429A5D97B26FC179D79A`).

Selector construction starts from exact commit `4fc3757651ef9396eb89f1ad69e19d3dc71be0da`. That commit's human-gate record says the SystemOnly, MicOnly, and Dual listening checks had passed. The later failed hidden-persistence commit `b4e83d747a94752e74c13be1169dea724325e20c` remains preserved on `candidate/mvp-audio-soundflow-core`; it is not the selector baseline.

## Frozen pipelines

- `SystemOnly`: concrete `wasapi2src` loopback (`continue-on-error=true`) → `queue` → `audioconvert` → `audioresample` → interleaved S16LE/48 kHz/stereo → `flacenc` → `system.flac`.
- `MicrophoneOnly`: a concrete `GstDevice` creates its configured `wasapi2src` through `gst_device_create_element()`; the source feeds a normally open lifecycle `valve`, then `queue` → `audioconvert` → `audioresample` → interleaved S16LE/48 kHz/mono → `webrtcdsp` → `flacenc` → `mic.flac`.
- `Dual`: the frozen loopback source and the concrete-`GstDevice` microphone source feed independent queues and independent FLAC encoders; microphone DSP is applied before `mic.flac`, while loopback remains untouched in `system.flac`. GStreamer does not premix the sources.
- `None`: `Start` succeeds without constructing an audio pipeline or creating an audio file.

The microphone DSP settings keep noise suppression enabled at moderate level, the high-pass filter enabled, and echo cancellation disabled. Gain control is intentionally not overridden, so GStreamer 1.28.6 `webrtcdsp` uses its upstream defaults (`gain-control=true`, adaptive-digital mode, compression gain 9 dB, target level 3 dBFS, limiter enabled). There is no ducking and no product-defined gain-control parameter.

## Device lifecycle

The application owns one long-lived GStreamer `GstDeviceMonitor` filtered to `Audio/Source`. Its initial `gst_device_monitor_get_devices()` result and subsequent official `DEVICE_ADDED` / `DEVICE_REMOVED` bus messages are the only microphone catalog source. The UI presents one explicit “Windows 默认麦克风” choice plus every non-loopback, non-default concrete `GstDevice` FriendlyName. FriendlyName is display-only. Concrete identity is the WASAPI `device.id`; the default pseudo-device's `device.actual-id` is resolved to an entry in the same concrete catalog.

The selected choice is visible and is stored per user at `%LOCALAPPDATA%\XbPreview\settings\microphone-selection.json`. A clean user has the `WindowsDefault` choice and no endpoint ID. A concrete selection stores its exact endpoint ID and display label. This file is never copied into the package. No development-machine endpoint or FriendlyName is compiled into the product.

At Start, `WindowsDefault` is resolved at that moment and the resulting concrete `GstDevice` strong reference is locked for the Session. A concrete selection locks only the matching current `GstDevice`. That exact strong reference is passed through the recording configuration to `GStreamerAudioCore`, which verifies that the endpoint is still present, calls `gst_device_create_element()`, and verifies that the created `wasapi2src.device` equals the selected `device.id`. There is no default-source or arbitrary-capture-endpoint fallback. If the selected device is absent or element creation/identity verification fails, Start returns `MicUnavailableAtStart`, creates no microphone pipeline/track, and never enters Recording. The UI message is “当前选择的麦克风不可用，请重新连接或选择其他麦克风。”.

`DEVICE_ADDED` is observed but never changes the running pipeline. Removing another device has no effect. Removing the locked microphone marks `MicDisconnectedDuringRecording` and closes the normally open lifecycle `valve` in `transform-to-gap` mode so invalid endpoint PCM cannot flow downstream; system/video continue and the application does not reconnect. The next recording creates a new monitor, re-enumerates, retains the newly present concrete `GstDevice`, and creates a fresh source from it.

The UI polls only the already-maintained monitor snapshot. It never scans Windows itself. During a Session it displays the locked FriendlyName even if the Windows default changes. If that endpoint is removed, the same selected item remains selected and is marked “不可用”; other devices remain available in the list but are never substituted. A user selection is the only way to change to another endpoint.

### Fixed lifecycle defect

The failed candidate had three concrete defects: it parsed a default `wasapi2src` into the formal microphone pipeline and only assigned a provider ID string afterward; its selector fell back to the default pseudo-device when `device.actual-id` no longer resolved to a currently enumerated concrete device; and its missing-microphone contract test returned early through `simulateMissingMicrophone` before formal device resolution. Consequently the automatic gate could pass while the product still entered Recording after physical unplug. The fixed path removes the parsed microphone source, rejects an unresolved pseudo-device, creates the source only from the retained concrete `GstDevice`, and drives the missing-device test through the same resolver.

Stop sends EOS, observes EOS or a terminal bus error, transitions to `GST_STATE_NULL`, joins the bus adapter, stops and unreferences the monitor, unreferences all devices/pipeline/buses, and verifies the FLAC is closed and nonempty before finalization is allowed.

## Finalization boundary

GStreamer finishes all capture, WebRTC DSP, resampling, synchronization, and lossless FLAC writing. SystemOnly writes `system.flac` and goes directly to AAC/mux with no loudness filter. MicrophoneOnly writes `mic.flac`; FFmpeg measures it and performs a second `loudnorm` pass at I=-16 LUFS, TP=-3.0 dBTP, LRA=7 before AAC. Dual keeps independent `system.flac` and `mic.flac`; FFmpeg applies the identical two-pass microphone mastering, mixes the mastered microphone with the untouched system track using `amix` weights `1 1` with normalization enabled, then performs a second two-pass `loudnorm` on the mixed program before AAC. The -3.0 dBTP encoding target provides codec headroom; acceptance is always based on decoding the final MP4 AAC and requires True Peak <= -1.5 dBTP. H.264 is stream-copied. Media Foundation then validates native H.264/AAC streams, decodes audio and video, checks EOS, sample format, nonempty frames, clipping, DC offset, and duration before publish.

## Runtime contract

`GStreamerAudioCore` exposes `Start(config)`, `Stop()`, and `Snapshot()`. Snapshot facts include audio mode, active system/microphone sources, selected concrete microphone ID, created-element device ID, exact identity match, disconnect status, pipeline state, last GStreamer error, independent system/microphone FLAC paths, terminal HRESULT, monitor state, EOS, file-close state, bus-thread exit, and the Dual-source independence contract.

The product verifies `gst_version()` is exactly 1.28.6 and resolves all required factories before capture. Runtime lookup is private to the package; no system GStreamer installation, SDK, source tree, PATH entry, or machine-specific device ID is required.

## Automated evidence

The focused C++ gate (`XbPreview.GStreamer.Tests`) validates byte-stable SystemOnly, MicOnly, and Dual pipeline descriptions, exact DSP parameters, no legacy filter tokens, `None`, initialization failure, selected-endpoint rejection, device-added/removed policy, source-data blocking, and shutdown invariants. Its selector path enumerates the current real catalog, maps every UI identity back to the same `GstDevice`, creates each source with `gst_device_create_element()`, and compares the resulting element `device` value with the endpoint ID. It records one exact-device MicOnly capture. When a second real endpoint exists it also captures from that different ID; otherwise the output explicitly reports `PENDING-PHYSICAL-DEVICE` and the final human gate supplies the second device. Focused managed tests verify the four UI modes, clean-user Windows-default behavior, explicit LocalAppData round-trip, and removal of concrete identity when the user switches back to Windows default.

Private package construction and validation are performed by:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools\gstreamer\New-MvpAudioGStreamerPackage.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools\gstreamer\Test-MvpAudioGStreamerGate.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools\gstreamer\Test-MvpAudioGStreamerMicDeviceLifecycle.ps1
```

The focused selector gate writes evidence under `artifacts/gate/gstreamer-mic-selector` and validates the direct-run package at `artifacts/package/win-x64`. A successful run prints `MVP-AUDIO-GSTREAMER-MIC-SELECTOR-HUMAN-GATE-READY`. The already-passed SystemOnly, MicOnly, and Dual baseline processing parameters are not changed.

## Human gate command

From `artifacts/package/win-x64`, run `XbPreview.Host.exe` and perform only this selector lifecycle check:

1. Confirm the UI explicitly displays `当前麦克风：Microphone (OSK218)` (or the actual FriendlyName on this computer).
2. Record MicOnly for 5–8 seconds and confirm normal voice quality.
3. Without closing the application, unplug the current microphone.
4. Confirm the same selected device is marked unavailable and Start is rejected with “当前选择的麦克风不可用，请重新连接或选择其他麦克风。”.
5. Insert or select another real microphone, confirm its FriendlyName is shown, then complete a normal MicOnly recording.

This proves that the product binds user-visible concrete devices and is not tailored to one development microphone.

## COPY-FIRST compliance

- Reused official GStreamer 1.28.6 APIs: `GstDeviceMonitor`, `GstDevice`, `gst_device_monitor_add_filter()`, `gst_device_monitor_start()`, `gst_device_monitor_get_devices()`, `GST_MESSAGE_DEVICE_ADDED`, `GST_MESSAGE_DEVICE_REMOVED`, and `gst_device_create_element()`.
- Reused element/provider identity: `wasapi2src`, `device.id`, `device.actual-id`, and the concrete source element's `device` property.
- First-party code is limited to the WinForms ComboBox/status label, LocalAppData selection serialization, versioned C ABI structs, and a thin monitor-to-recording adapter.
- Device enumeration, WASAPI capture, and hotplug detection were not reimplemented.
- Hot reconnect was not implemented.
- No old audio or arbitrary microphone fallback exists.
