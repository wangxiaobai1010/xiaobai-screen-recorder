# Xiaobai Recorder 1.0.0 第三方软件声明 / Third-Party Notices

本声明适用于 Xiaobai Recorder 1.0.0（Windows x64）。Xiaobai Recorder 自有源代码采用 MIT 许可证，完整条款见随产品提供的 `LICENSE`。本应用包含或再分发若干第三方软件；各第三方软件仍分别受其自身许可证和署名要求约束。顶层 MIT 许可证不会把第三方组件重新许可为 MIT。

This notice applies to Xiaobai Recorder 1.0.0 for Windows x64. Xiaobai Recorder's own source code is licensed under the MIT License; see the included `LICENSE`. The application includes or redistributes third-party software whose licenses and attribution requirements remain separately applicable. The top-level MIT License does not relicense third-party components.

## Complete license and notice texts

The release application materials provide the preserved license and notice texts under `licenses/` and, for the frozen FFmpeg payload, at `ffmpeg/LICENSE.txt`. Component-specific pointers are listed below. The two Corresponding Source companions named in this notice are made available with the corresponding GitHub Release and include their applicable source, build, license, and notice materials. This aggregate notice summarizes the frozen distribution; it does not replace the complete texts.

## FFmpeg and FFTW

- **Component:** FFmpeg frozen binary identity `n8.1.2-34-g9b6c8969e0-20260809`.
- **Source identities:** FFmpeg commit `9b6c8969e05b4f0b29f0f85cd501be6b3e582e6b`; BtbN builder commit `2437e7b868da3c11872367b15f3c613b87c24819`.
- **Distribution disposition:** the frozen FFmpeg payload contains FFTW 3.3.11 object code statically incorporated into `avformat-62.dll` through Chromaprint. The distributed FFmpeg/FFTW aggregate is therefore treated on an effective GPL-3.0-or-later engineering distribution basis, while preserving the original license and attribution of each component. It is not described as simply an LGPL build, and this disposition does not change Xiaobai Recorder's own MIT license.
- **Technical boundary:** Xiaobai Recorder invokes `ffmpeg.exe` as a separate program and does not directly link or load libav.
- **License/build pointers:** `ffmpeg/LICENSE.txt`, `licenses/ffmpeg/RELEASE-BUILD-INFO.md`, and the Corresponding Source companion.
- **Corresponding Source:** `xiaobai-recorder-1.0.0-ffmpeg-corresponding-source.tar.xz`, expected SHA-256 `1D31B5C39C6F24F983E869AE4DCE5374A5E9EEFBF0647A28BA4C2D8ADCAA93A0`, available with the corresponding GitHub Release.

This is a factual engineering distribution notice, not legal advice.

## GStreamer / GLib audio runtime

- **Runtime:** GStreamer 1.28.6, consisting of exactly 7 selected plugins and 15 support DLLs.
- **Selected plugins (7):** `gstcoreelements.dll`, `gstwasapi2.dll`, `gstaudioconvert.dll`, `gstaudioresample.dll`, `gstwebrtcdsp.dll`, `gstflac.dll`, and `gstlevel.dll`.
- **Support DLLs (15):** `gstreamer-1.0-0.dll`, `gstbase-1.0-0.dll`, `gstaudio-1.0-0.dll`, `gsttag-1.0-0.dll`, `gstbadaudio-1.0-0.dll`, `glib-2.0-0.dll`, `gmodule-2.0-0.dll`, `gobject-2.0-0.dll`, `intl-8.dll`, `orc-0.4-0.dll`, `z-1.dll`, `ffi-7.dll`, `pcre2-8-0.dll`, `FLAC-8.dll`, and `ogg-0.dll`.
- **License model:** LGPL-covered GStreamer/GLib components plus the separately documented permissive dependencies listed below.
- **Frozen review:** GPL/restricted runtime component = **none found in the frozen distributed set**.
- **Technical model:** app-local dynamically linked/runtime-loaded GStreamer components. No unrelated GStreamer SDK plugins are included in this notice or in the frozen allowlist.
- **License/notice pointers:** `licenses/GSTREAMER-AUDIO-THIRD-PARTY.md` and the component subdirectories under `licenses/gstreamer/`.
- **Corresponding Source:** `xiaobai-recorder-1.0.0-gstreamer-corresponding-source.tar.xz`, expected SHA-256 `A1BBAFC3EF8248547CB41D855886CFA8C10250A0ADCA4FCC8B8816091DA7FE93`, available with the corresponding GitHub Release. The companion includes rebuild, compatible DLL/plugin replacement, and LGPL modification-debugging guidance.

The frozen GStreamer/GLib dependency closure is:

| Name | Version | License | License / attribution pointer |
| --- | --- | --- | --- |
| GStreamer core and selected gst-plugins modules | 1.28.6 | LGPL-2.0-or-later | `licenses/GSTREAMER-AUDIO-THIRD-PARTY.md`; `licenses/gstreamer/gstreamer-1.0/`, `gst-plugins-base-1.0/`, `gst-plugins-good-1.0/`, and `gst-plugins-bad-1.0/` |
| GLib | 2.82.4 | LGPL-2.1-or-later | `licenses/gstreamer/glib/`; GStreamer Corresponding Source companion |
| proxy-libintl | 0.5 | LGPL-2.0-or-later | `licenses/gstreamer/proxy-libintl/`; GStreamer Corresponding Source companion |
| ORC | 0.4.42 | BSD-2-Clause plus preserved example terms | `licenses/gstreamer/orc/`; GStreamer Corresponding Source companion |
| zlib | 1.3.1 | zlib License | `licenses/gstreamer/zlib/`; GStreamer Corresponding Source companion |
| libffi | meson-3.2.9999.5 | MIT | `licenses/gstreamer/libffi/`; GStreamer Corresponding Source companion |
| PCRE2 | 10.42 | PCRE2 BSD-style license and exemption | `licenses/gstreamer/pcre2/`; GStreamer Corresponding Source companion |
| FLAC library | 1.4.3 | Xiph BSD-3-Clause | `licenses/gstreamer/flac/`; GStreamer Corresponding Source companion |
| libogg | 1.3.5 | Xiph BSD-3-Clause | `licenses/gstreamer/libogg/`; GStreamer Corresponding Source companion |
| WebRTC Audio Processing | 2.1 | BSD-3-Clause plus preserved bundled-code notices | `licenses/gstreamer/webrtc-audio-processing/`; GStreamer Corresponding Source companion |
| Abseil and its WrapDB build files | 20240722.0; WrapDB 20240722.0-3 | Apache-2.0; WrapDB files MIT | GStreamer Corresponding Source companion |

## Other redistributed components

| Name | Version | License | License / attribution pointer |
| --- | --- | --- | --- |
| Avalonia | 12.2.999-cibuild0067907-alpha | MIT | `licenses/nuget/avalonia/LICENSE` |
| Avalonia ANGLE Windows natives | 2.1.27548.20260419 | BSD-3-Clause | `licenses/nuget/avalonia.angle.windows.natives/LICENSE` |
| SkiaSharp | 3.119.4 | MIT plus preserved native third-party notices | `licenses/nuget/skiasharp/LICENSE.txt`; `licenses/nuget/skiasharp.nativeassets.win32/THIRD-PARTY-NOTICES.txt` |
| HarfBuzzSharp | 8.3.1.3 | MIT plus preserved native third-party notices | `licenses/nuget/harfbuzzsharp/LICENSE.txt`; `licenses/nuget/harfbuzzsharp.nativeassets.win32/THIRD-PARTY-NOTICES.txt` |
| MicroCom.Runtime | 0.11.6 | MIT | `licenses/nuget/microcom.runtime/LICENSE` |
| System.IO.Pipelines | 8.0.0 | MIT plus exact package third-party notices | `licenses/nuget/system.io.pipelines/LICENSE.TXT`; `licenses/nuget/system.io.pipelines/THIRD-PARTY-NOTICES.TXT` |
| Tmds.DBus.Protocol | 0.94.1 | MIT | `licenses/nuget/tmds.dbus.protocol/LICENSE` |
| ScreenRecorderLib-derived audio subsystem | ScreenRecorderLib v6.6.0 source/derivative identity | MIT | `licenses/screenrecorderlib/LICENSE-SCREENRECORDERLIB.txt`; `licenses/screenrecorderlib/SOURCE.md` |
| Microsoft Windows classic sample-derived portions used by that audio subsystem | Frozen derivative identity recorded in the source provenance | MIT | `licenses/screenrecorderlib/LICENSE-MICROSOFT-WINDOWS-CLASSIC-SAMPLES.txt`; `licenses/screenrecorderlib/SOURCE.md` |

`XbPreview.Native.dll` is a Xiaobai Recorder binary containing the documented ScreenRecorderLib-derived audio subsystem. This attribution does not imply that the entire Xiaobai native implementation is ScreenRecorderLib.

## Microsoft .NET self-contained runtime

The self-contained distribution includes Microsoft/.NET runtime material and its corresponding license and third-party notices:

| Name | Version | License | License / attribution pointer |
| --- | --- | --- | --- |
| Microsoft.NETCore.App | 8.0.29 | MIT for Microsoft runtime code, with component-specific terms in the exact third-party notice | `licenses/dotnet/Microsoft.NETCore.App.Runtime.win-x64/LICENSE.TXT`; `licenses/dotnet/Microsoft.NETCore.App.Runtime.win-x64/THIRD-PARTY-NOTICES.TXT` |
| Microsoft.WindowsDesktop.App | 8.0.29 | MIT for Microsoft runtime code | `licenses/dotnet/Microsoft.WindowsDesktop.App.Runtime.win-x64/LICENSE` |

The reviewed release summary for this exact self-contained runtime set is at `licenses/dotnet/RELEASE-NOTICE-REVIEW.md`.

## Microsoft Visual C++ app-local runtime

The distribution includes the unmodified app-local Microsoft Visual C++ runtime files `msvcp140.dll`, `vcruntime140.dll`, and `vcruntime140_1.dll`, each at file version 14.44.35211.0. They are distributed under the applicable Microsoft Visual Studio redistribution terms. The frozen redistribution evidence is referenced at `licenses/vc-redist/REDISTRIBUTION.md`; Microsoft's terms are not restated or expanded here.
