# GStreamer Audio Third-Party Notice

This notice covers the private `win-x64` audio runtime shipped by the MVP Audio Core. It is an engineering inventory, not legal advice.

## Pinned distribution

- Product: GStreamer 1.28.6 for Windows, MSVC x86_64
- Upstream installer: `https://gstreamer.freedesktop.org/data/pkg/windows/1.28.6/msvc/gstreamer-1.0-msvc-x86_64-1.28.6.exe`
- Installer SHA-256: `059251444D1267B486EBA390B18D25FED87E10315E72F757EC6C7E912FA746B5`
- Loading policy: the application sets `GST_PLUGIN_SYSTEM_PATH_1_0` to its private `gstreamer/plugins` directory and clears the user/plugin search paths before `gst_init_check`.
- GPL-only GStreamer plugins and libraries are not selected or copied. In particular, the GPL-licensed FLAC command-line binaries are not packaged; `FLAC-8.dll` is the Xiph-licensed codec library used by the LGPL `gstflac.dll` plugin.

## Selected GStreamer plugins

| Packaged file | Required factory/elements | Upstream module | Effective license |
| --- | --- | --- | --- |
| `gstcoreelements.dll` | `queue`, `filesink` | GStreamer core plugins | LGPL-2.0-or-later |
| `gstwasapi2.dll` | `wasapi2src` | gst-plugins-bad | LGPL-2.0-or-later |
| `gstaudioconvert.dll` | `audioconvert` | gst-plugins-base | LGPL-2.0-or-later |
| `gstaudioresample.dll` | `audioresample` | gst-plugins-base | LGPL-2.0-or-later |
| `gstwebrtcdsp.dll` | `webrtcdsp` | gst-plugins-bad | LGPL-2.0-or-later; WebRTC audio-processing dependency uses its bundled BSD-style notice |
| `gstflac.dll` | `flacenc` | gst-plugins-good | LGPL-2.0-or-later; FLAC dependency uses the Xiph BSD-style license |
| `gstlevel.dll` | `level` | gst-plugins-good | LGPL-2.0-or-later |

The package allowlist is exactly the seven files above. `gst-inspect-1.0` from the pinned distribution reported `LGPL` for every selected plugin.

Microphone selection adds no library or plugin. It uses the GStreamer 1.28.6 core/device APIs `GstDeviceMonitor`, `GstDevice`, `gst_device_monitor_get_devices()`, `GST_MESSAGE_DEVICE_ADDED`, `GST_MESSAGE_DEVICE_REMOVED`, and `gst_device_create_element()` together with the already-packaged LGPL-2.0-or-later `gstwasapi2.dll` provider. No Windows device-enumeration or WASAPI implementation is added by the application.

## Runtime dependency closure

The selected plugins depend on the following app-local GStreamer/runtime DLLs:

`gstreamer-1.0-0.dll`, `gstbase-1.0-0.dll`, `gstaudio-1.0-0.dll`, `gsttag-1.0-0.dll`, `gstbadaudio-1.0-0.dll`, `glib-2.0-0.dll`, `gobject-2.0-0.dll`, `gmodule-2.0-0.dll`, `intl-8.dll`, `orc-0.4-0.dll`, `z-1.dll`, `ffi-7.dll`, `pcre2-8-0.dll`, `FLAC-8.dll`, and `ogg-0.dll`.

Corresponding notices are packaged for GStreamer core, gst-plugins-base, gst-plugins-bad, gst-plugins-good, GLib, libffi, ORC, PCRE2, proxy-libintl, zlib, FLAC, libogg, and WebRTC audio processing. Microsoft Visual C++ runtime DLLs are deployed app-locally from the installed VC143 redistributable directory.

## FFmpeg mux boundary

The private FFmpeg build reports `n8.1.2-34-g9b6c8969e0-20260809`, shared, `--enable-version3`, with GPL codec integrations such as x264/x265 disabled. Its bundled LGPLv3 license is packaged beside `ffmpeg.exe`. SystemOnly copies H.264 and encodes the untouched GStreamer `system.flac` to AAC at 48 kHz stereo. MicrophoneOnly uses FFmpeg's two-pass `loudnorm` at I=-16 LUFS, TP=-3.0 dBTP, LRA=7 before AAC. Dual applies that same microphone mastering to `mic.flac`, mixes it with untouched `system.flac` through `amix` with weights `1 1` and normalization enabled, then applies the same two-pass `loudnorm` to the mixed program. The final decoded AAC in the MP4 must remain at or below -1.5 dBTP. No `agate`, expander, custom limiter, custom DSP, or product-designed gain algorithm is used.

## Reproduction

Run `tools/gstreamer/Install-GStreamer-1.28.6.ps1` to acquire and verify the pinned SDK. Run `tools/gstreamer/New-MvpAudioGStreamerPackage.ps1` to create `artifacts/package/win-x64/package-manifest.json`, which records the plugin allowlist and the SHA-256 and byte size of every packaged payload file.
