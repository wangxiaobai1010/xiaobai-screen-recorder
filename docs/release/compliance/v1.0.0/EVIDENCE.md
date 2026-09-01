# Xiaobai Recorder 1.0.0 third-party compliance evidence

## Review result

`OVERALL = PARTIAL-BLOCKED`

`THIRD-PARTY-NOTICES READY TO GENERATE = NO`

This is an engineering evidence record for the frozen `win-x64` application stage. It is not the final aggregate notice. The review closes the ordinary managed/runtime identities and the exact GStreamer binary composition, but it deliberately fails closed on two release matters:

1. The frozen BtbN FFmpeg archive is labeled LGPL, yet its exact scripts and frozen binary strings establish static FFTW 3.3.11 through Chromaprint. FFTW is GPL-2.0-or-later, `--enable-gpl` is absent, and only an LGPLv3 text is shipped. No commercial FFTW distribution rights or reviewed GPL distribution basis was found.
2. The exact GStreamer/GLib sources and corrected notices are now identified, but the planned durable corresponding-source companion, relink/replacement instructions, and final asset hash have not been assembled or published. The Cerbero-requested WebRTC `.tar.gz` is also unavailable from its origin; the retained official GStreamer mirror `.tar.xz` is version/tag-equivalent but not byte-identical.

No product, build, release-foundation, app-stage, or installer file was changed.

## Frozen evidence foundation

| Field | Frozen value |
|---|---|
| Product / RID | Xiaobai Recorder 1.0.0 / `win-x64` |
| Evidence foundation commit | `fe1299b109fd9e5ff99bb89f7a2c3eb5c94f44a1` |
| Frozen app manifest | `artifacts/packaging/xiaobai-recorder-1.0.0/app-manifest.json` |
| Manifest SHA-256 | `171067C3302DAFB59553F8AB70F8BF266F0F002E4636C2BE10D880E7C6E1BA78` |
| Manifest records | 579 unique paths; 100% SHA-256 coverage; 0 PDB |
| Runtime inventory evidence | `component-records/RUNTIME-INVENTORY.json` |
| Exact NuGet/package evidence | `component-records/NUGET-PACKAGES.json` |
| Download ledger | `component-records/DOWNLOADS.json` |
| License integrity ledger | `component-records/LICENSE-INVENTORY.json` |

The runtime inventory partitions every one of the 579 frozen paths exactly once. It uses the generated app manifest as truth, not abstract project references. The principal runtime counts are: .NET 459; Avalonia 23; ANGLE 1; FFmpeg 8; GStreamer plugins 7 plus support DLLs 15; SkiaSharp 2; HarfBuzzSharp 2; MicroCom.Runtime 1; Tmds.DBus.Protocol 1; System.IO.Pipelines 1; VC++ 3; and the attributed ScreenRecorderLib derivative binary 1. Packaged evidence and first-party/meta files account for the remainder.

## Component decisions

Every path below is relative to this evidence directory. Exact runtime paths and hashes are in `RUNTIME-INVENTORY.json`; exact source/package hashes and machine-readable closure states are in `SOURCE-MANIFEST.json`.

### .NET 8.0.29

- **WHAT WE REDISTRIBUTE:** 179 `Microsoft.NETCore.App.Runtime.win-x64` files and 280 `Microsoft.WindowsDesktop.App.Runtime.win-x64` files.
- **WHAT LICENSE APPLIES:** MIT for Microsoft runtime code; component-specific terms in the exact .NET third-party notice.
- **WHERE THE EVIDENCE CAME FROM:** exact 8.0.29 NuGet packages; runtime commit `18fd75c847399745c43b5970fec840ba71064e80`; WindowsDesktop commit `580168d2b82abec4ec1460077b8e62c016c031c4`; byte-reversal mapping of all 459 manifest paths.
- **LICENSE EVIDENCE:** `licenses/dotnet/Microsoft.NETCore.App.Runtime.win-x64/LICENSE.TXT`, `licenses/dotnet/Microsoft.NETCore.App.Runtime.win-x64/THIRD-PARTY-NOTICES.TXT`, and `licenses/dotnet/Microsoft.WindowsDesktop.App.Runtime.win-x64/LICENSE`.
- **WHAT SOURCE COMPANION WILL BE PROVIDED:** none required by the selected license terms. The exact package/source identities remain in the manifest.
- **CLOSURE:** **PASS**. The exact WindowsDesktop package contains no separate ThirdPartyNotices file; this is recorded rather than substituted from another servicing version.

### Avalonia and ANGLE

- **WHAT WE REDISTRIBUTE:** 23 Avalonia managed DLLs and `av_libglesv2.dll`.
- **WHAT LICENSE APPLIES:** Avalonia MIT; ANGLE BSD-3-Clause.
- **WHERE THE EVIDENCE CAME FROM:** exact official CI/NuGet package bytes and nuspec repository metadata. Avalonia packages identify commit `18254778a6fb767971bafbffbbc14869facc050b`; ANGLE identifies commit `1c89805903c1482166356d3b950d474973180e61`.
- **LICENSE EVIDENCE:** `licenses/avalonia/MIT.html` and `licenses/angle/LICENSE`.
- **WHAT SOURCE COMPANION WILL BE PROVIDED:** none required. The exact package artifacts and source identities are retained in `NUGET-PACKAGES.json`.
- **CLOSURE:** **PASS**. GitHub no longer serves the historical Avalonia commit object, but the official packages conclusively fix that source identity and all frozen DLLs hash-match those packages.

### MicroCom.Runtime

- **WHAT WE REDISTRIBUTE:** `MicroCom.Runtime.dll` 0.11.6.
- **WHAT LICENSE APPLIES:** MIT; copyright 2021 Nikita Tsukanov.
- **WHERE THE EVIDENCE CAME FROM:** exact NuGet package SHA-256 `453AF6D34477B19BB2C62D17436E68C76A75915B7947509B1FF601B36D7E78D8` and repository commit `76785efcafd91b5902fd19dd11145f6dd655b7b4`.
- **LICENSE EVIDENCE:** `licenses/microcom.runtime/LICENSE`.
- **WHAT SOURCE COMPANION WILL BE PROVIDED:** none required.
- **CLOSURE:** **PASS**.

### Tmds.DBus.Protocol

- **WHAT WE REDISTRIBUTE:** `Tmds.DBus.Protocol.dll` 0.94.1; the frozen manifest confirms it is present.
- **WHAT LICENSE APPLIES:** MIT; the exact COPYING preserves Alp Toker, Other Contributors, and Tom Deseyn attributions.
- **WHERE THE EVIDENCE CAME FROM:** exact NuGet package SHA-256 `4F71D06AC40E725FFBCF1B79853DD93218695A84F23D36FAFC85F477A4C9580F` and source commit `b4a7fed0b878f74cb54f7cca84d2889af4e596ba`.
- **LICENSE EVIDENCE:** `licenses/tmds.dbus.protocol/COPYING`.
- **WHAT SOURCE COMPANION WILL BE PROVIDED:** none required.
- **CLOSURE:** **PASS**.

### Microsoft Visual C++ app-local runtime

- **WHAT WE REDISTRIBUTE:** `msvcp140.dll`, `vcruntime140.dll`, and `vcruntime140_1.dll`, each file version 14.44.35211.0 and locked by frozen SHA-256.
- **WHAT LICENSE APPLIES:** Microsoft Visual Studio redistribution terms.
- **WHERE THE EVIDENCE CAME FROM:** byte matches against the installed ordinary `Microsoft.VC143.CRT` redist set, plus official Microsoft app-local redistribution guidance and the VS 2022 REDIST-list/right statement recorded in `DOWNLOADS.json`.
- **LICENSE EVIDENCE:** official Microsoft documentation is retained as ignored download evidence; no Microsoft license text is added to the application because the reviewed guidance does not require doing so.
- **WHAT SOURCE COMPANION WILL BE PROVIDED:** none.
- **CLOSURE:** **PASS — FILES AUTHORIZED AS REDISTRIBUTABLE = YES**, conditional on a valid Visual Studio license and redistribution of the unmodified ordinary redist files. These are not debug/non-redist binaries.

### SkiaSharp and HarfBuzzSharp

- **WHAT WE REDISTRIBUTE:** `SkiaSharp.dll`, `libSkiaSharp.dll`, `HarfBuzzSharp.dll`, and `libHarfBuzzSharp.dll`.
- **WHAT LICENSE APPLIES:** MIT plus the exact native third-party notice corpus.
- **WHERE THE EVIDENCE CAME FROM:** exact NuGet packages; SkiaSharp commit `f568ac94dd768ef9a2f593537cfde2dd0d348ef5`; HarfBuzzSharp commit `2888c737ad016d584c74525e2d35db5097ea8576`.
- **LICENSE EVIDENCE:** `licenses/skiasharp-harfbuzzsharp/LICENSE.txt` and `licenses/skiasharp-harfbuzzsharp/THIRD-PARTY-NOTICES.txt`.
- **WHAT SOURCE COMPANION WILL BE PROVIDED:** none required.
- **CLOSURE:** **PASS**; previously gate-closed and reconfirmed against the frozen runtime.

### System.IO.Pipelines

- **WHAT WE REDISTRIBUTE:** `System.IO.Pipelines.dll` 8.0.0.
- **WHAT LICENSE APPLIES:** MIT plus exact package third-party notices.
- **WHERE THE EVIDENCE CAME FROM:** exact package SHA-256 `2DDA41D6CE2F433B0E3836B188AB2D2E4B39ED7F434C3E43E9C3F1F03135C301` and source commit `5535e31a712343a63f5d7d796cd874e563e5ac14`.
- **LICENSE EVIDENCE:** `licenses/system.io.pipelines/LICENSE.TXT` and `licenses/system.io.pipelines/THIRD-PARTY-NOTICES.TXT`.
- **WHAT SOURCE COMPANION WILL BE PROVIDED:** none required.
- **CLOSURE:** **PASS**; previously gate-closed and reconfirmed.

### ScreenRecorderLib derivative attribution

- **WHAT WE REDISTRIBUTE:** first-party `XbPreview.Native.dll` containing the documented MIT-derived audio subsystem.
- **WHAT LICENSE APPLIES:** ScreenRecorderLib MIT and Microsoft Windows classic samples MIT.
- **WHERE THE EVIDENCE CAME FROM:** vendored `third_party/screenrecorderlib-audio/SOURCE.md`; ScreenRecorderLib v6.6.0 commit `39ad1e2f1750fa06669b73743dbbaa25371dec21`; Microsoft sample commit `77f217b3f89d4dac7864a62cc91ff7b569f26a50`.
- **LICENSE EVIDENCE:** `licenses/screenrecorderlib/LICENSE-SCREENRECORDERLIB.txt` and `licenses/screenrecorderlib/LICENSE-MICROSOFT-WINDOWS-CLASSIC-SAMPLES.txt`.
- **WHAT SOURCE COMPANION WILL BE PROVIDED:** no third-party source companion required; the vendored provenance record remains authoritative.
- **CLOSURE:** **PASS**; previously gate-closed and consolidated here.

## FFmpeg closure

### Identity and linkage

- **WHAT WE REDISTRIBUTE:** `ffmpeg.exe` and seven shared libav DLLs listed in `SOURCE-MANIFEST.json`; the frozen archive SHA-256 is `6D4AFE797A68AF283ED42254827027F7D56940BA6C9E37EBED9C0E87A9E0C54C`.
- **SOURCE:** FFmpeg `release/8.1` commit `9b6c8969e05b4f0b29f0f85cd501be6b3e582e6b`; BtbN release `autobuild-2026-08-09-13-03`, builder commit `2437e7b868da3c11872367b15f3c613b87c24819`.
- **MODIFIED BY XIAOBAI:** **NO**. Frozen payload hashes are the locked upstream-builder bytes. BtbN's builder applies one AOM and three aribb24 patches, all identified in `FFMPEG-BUILD.md`.
- **LINKAGE:** `ffmpeg.exe` imports only the seven FFmpeg DLLs plus Windows/UCRT. The libav DLLs import only one another plus Windows/UCRT. FFmpeg itself is shared; enabled non-system dependencies are static, header/API shims, or compiler/runtime inputs because no third-party sidecar DLL is imported. Exact object-to-DLL allocation still needs original linker maps.

### License contradiction

The archive build line includes `--enable-version3`, `--enable-chromaprint`, and `--pkg-config-flags=--static`, but not `--enable-gpl` or `--enable-nonfree`. Builder `50-chromaprint.sh` selects `-DFFT_LIB=fftw3`, depends on FFTW 3.3.11 commit `93ed4c786934aec9946f8dda4b4e3eb08f8be41c`, and emits `Libs.private: -lfftw3 -lstdc++`. Frozen `avformat-62.dll` contains the matching FFTW version strings. Chromaprint's own guidance says the FFTW choice makes the resulting Chromaprint binary GPL. FFTW is GPL-2.0-or-later.

Accordingly:

- **ARCHIVE-CLAIMED LICENSE:** LGPL-3.0-or-later.
- **EVIDENCE-SUPPORTED COMBINED CONDITION:** likely GPL-3.0-or-later treatment (LGPLv3 plus FFTW GPL-2.0-or-later selecting GPLv3), subject to qualified legal review.
- **GPL/NONFREE:** GPL code **YES** through FFTW; nonfree flag **NO**.
- **LICENSE EVIDENCE:** `licenses/ffmpeg/LICENSE.txt` and `licenses/ffmpeg-builds/LICENSE` are authentic but insufficient for the actual static dependency closure.
- **CLOSURE:** **BLOCKED WITH ONE PRECISE REMAINING GAP:** establish a legally and technically reviewed distribution basis for the exact statically incorporated FFTW code. No commercial FFTW license was found, and GPL notices/source/offer are not prepared.

The complete configure line, external dependency/source/license matrix, PE findings, historical-input gaps, and exact future asset content are in `component-records/FFMPEG-BUILD.md` and `component-records/FFMPEG-DEPENDENCIES.json`.

### Planned FFmpeg source companion

`RELEASE ASSET NAME = xiaobai-recorder-1.0.0-ffmpeg-corresponding-source.tar.xz`

It must contain the exact FFmpeg commit; exact builder snapshot and four patches; every exact dependency checkout/archive and recursive gitlink; shaderc DEPS; rav1e Cargo crates and the historical post-`cargo update cc` lock; GCC/MinGW/libgomp/winpthreads sources and runtime exception; all license/PATENTS/NOTICE files; immutable container/apt/tool ledger; configure output, `config.log`, verbose logs, linker maps; SHA256SUMS; and a binary-to-source/link-mode manifest. If the frozen binary is distributed under a GPL basis, the asset/offer must implement the counsel-approved GPLv3-or-later corresponding-source treatment. Its SHA-256 is not yet available.

## GStreamer / GLib closure

### Exact frozen composition

- **PLUGINS (7):** `gstcoreelements.dll`, `gstwasapi2.dll`, `gstaudioconvert.dll`, `gstaudioresample.dll`, `gstwebrtcdsp.dll`, `gstflac.dll`, `gstlevel.dll`.
- **SUPPORT DLLS (15):** `gstreamer-1.0-0.dll`, `gstbase-1.0-0.dll`, `gstaudio-1.0-0.dll`, `gsttag-1.0-0.dll`, `gstbadaudio-1.0-0.dll`, `glib-2.0-0.dll`, `gmodule-2.0-0.dll`, `gobject-2.0-0.dll`, `intl-8.dll`, `orc-0.4-0.dll`, `z-1.dll`, `ffi-7.dll`, `pcre2-8-0.dll`, `FLAC-8.dll`, `ogg-0.dll`.
- **SOURCE:** GStreamer/Cerbero 1.28.6, Cerbero commit `59548269f4fd0f701818f0bafdb102959ec81e65`, GStreamer commit `2d3e05cbdad68e47d645f548899b432dc9fb4473`, GLib 2.82.4, plus exact dependency archives in `DOWNLOADS.json`.
- **GPL/RESTRICTED RUNTIME COMPONENT:** **NO**. “bad” is a maturity label, not a license. No gst-libav, gst-plugins-ugly, x264/x265, GPL FLAC tools, or restricted package appears in the exact allowlist/import closure.

The application and plugins use LGPL libraries through replaceable DLL boundaries. The exception is permissively licensed WebRTC Audio Processing 2.1 plus Abseil 20240722.0, which Cerbero statically incorporates into `gstwebrtcdsp.dll`. The evidence bundle now carries the correct GStreamer/GLib LGPL 2.1 texts and the WebRTC/Abseil bundled-code notices; it does not reuse the frozen stage's older GNU Library GPL v2-only text or irrelevant usrsctp reference.

### Component matrix

| Component | What is redistributed | License | Exact source evidence | Closure |
|---|---|---|---|---|
| GStreamer core 1.28.6 | core DLL, GstBase, coreelements plugin | LGPL-2.0-or-later / release COPYING LGPL-2.1 | `gstreamer-1.28.6.tar.xz` `62B6B9F0…52CA` | BLOCKED: companion unpublished |
| gst-plugins-base 1.28.6 | GstAudio, GstTag, audioconvert, audioresample | LGPL-2.0-or-later | `gst-plugins-base-1.28.6.tar.xz` `0BA699C7…04AC` | BLOCKED: companion unpublished |
| gst-plugins-good 1.28.6 | FLAC and level plugins | LGPL-2.0-or-later | `gst-plugins-good-1.28.6.tar.xz` `B0C620A4…E2DF` | BLOCKED: companion unpublished |
| gst-plugins-bad 1.28.6 | GstBadAudio, WASAPI2, WebRTC DSP | LGPL; WebRTC DSP LGPL-2.1-or-later | `gst-plugins-bad-1.28.6.tar.xz` `6636F2C2…87C3` | BLOCKED: companion unpublished |
| GLib 2.82.4 | GLib, GObject, GModule | LGPL-2.1-or-later | `glib-2.82.4.tar.xz` `37DD0877…C709` | BLOCKED: companion unpublished |
| proxy-libintl 0.5 | `intl-8.dll` | LGPL-2.0-or-later | tag/commit `33934de09af6a6627eb44e310a8079df009abdbb` | BLOCKED: companion unpublished |
| ORC 0.4.42 | `orc-0.4-0.dll` | BSD-2-Clause plus preserved example terms | archive `7EC912AB…C90C` | PASS |
| zlib 1.3.1 | `z-1.dll` | Zlib | archive `9A93B2B7…DF23` | PASS |
| libffi meson-3.2.9999.5 | `ffi-7.dll` | MIT | archive `B4D40393…D227` | PASS |
| PCRE2 10.42 | `pcre2-8-0.dll` | PCRE2 BSD-style licence/exemption | archive `8D36CD8C…E840` | PASS |
| FLAC 1.4.3 library | `FLAC-8.dll` | Xiph BSD-3-Clause | archive `6C58E69C…5B70` | PASS |
| libogg 1.3.5 | `ogg-0.dll` | Xiph BSD-3-Clause | archive `C4D91BE3…6705` | PASS |
| WebRTC Audio Processing 2.1 | static in WebRTC DSP plugin | BSD-3-Clause plus bundled notices | tag `846fe90a…93e5`; mirror archive `AE930282…9FE` | PASS |
| Abseil 20240722.0 | static in WebRTC DSP plugin | Apache-2.0; WrapDB files MIT | source `F50E5AC3…AE3`; patch `12DD8DF1…F7B` | PASS |

Exact 22-file hashes, import edges, Cerbero patch list, selected licenses, and relinking facts are in `component-records/GSTREAMER-BUILD.md`.

### Planned GStreamer source companion

`RELEASE ASSET NAME = xiaobai-recorder-1.0.0-gstreamer-corresponding-source.tar.xz`

It must contain every source archive in `DOWNLOADS.json`; the full Cerbero 1.28.6 snapshot/commit; exact recipes, Windows x86_64 MSVC/VS2022 configuration, and patches; the Abseil WrapDB patch; exact license texts; modification statement (no Xiaobai source patches; official Cerbero patches applied); source/hash/consumer manifest; and DLL/plugin rebuild, replacement, and LGPL reverse-engineering-for-debugging instructions. The asset has not been assembled or published, so its SHA-256 is unavailable.

`GSTREAMER / GLIB CLOSURE = BLOCKED`

## License text integrity

`component-records/LICENSE-INVENTORY.json` records, for all 36 tracked license/notice files:

- component and version;
- official source URL or exact package;
- original file name;
- original/source SHA-256;
- tracked path and tracked SHA-256; and
- a normalized-text integrity result.

All 36 records pass ordinal text comparison after only CRLF/CR-to-LF normalization and ignoring terminal LF count. Standard license bodies were not paraphrased and no Xiaobai restriction was appended.

## Download integrity and custody

`component-records/DOWNLOADS.json` records all 30 retained network evidence artifacts with component, version, requested URL, resolved URL (or an explicit note when GitHub used an unretained transient signed CDN URL), relative file name, byte size, SHA-256, purpose, and `tracked: false`.

Every retained download is under `artifacts/release-compliance/v1.0.0/`. Those files remain ignored and untracked. No downloaded executable was run and no software was installed.

## Remaining blockers and exact next evidence

| Component | Missing | Why | Exact next evidence step |
|---|---|---|---|
| FFmpeg | Defensible license/distribution basis for static FFTW; exact dependency license/source corpus; immutable build inputs and link trace; final companion hash | Frozen “LGPL” identity conflicts with proven GPL FFTW incorporation | Obtain a qualified disposition for this exact binary (commercial FFTW rights or GPLv3-or-later treatment), then assemble and independently review the exact companion. Any replacement build is outside this task and requires separate authorization. |
| GStreamer / GLib | Published, hashed source companion and relink/replacement instructions; disposition of byte-different but source-equivalent WebRTC mirror archive | Exact sources are locally reviewed but no durable same-release source delivery exists | Assemble the planned asset from the ledger, independently review it, publish it beside the release, and record its SHA-256. |

Until both blockers close, the final aggregate `THIRD-PARTY-NOTICES.md` must not be generated and the installer must not be produced.
