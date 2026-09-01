# Frozen GStreamer / GLib build and linkage record

## Decision

- Frozen runtime composition: **PASS**. The exact allowlist contains no GPL-only or restricted GStreamer runtime component.
- Compliance-evidence closure: **BLOCKED**. Exact source inputs have been identified and acquired for review, but no durable, hashed corresponding-source companion/source offer has been published beside the installer candidate. The exact Cerbero-requested WebRTC `2.1` `.tar.gz` (SHA-256 `35E86B986D02EA15F3D04741A1A5A735BA399BC0FAC0EE089C39480E35FC3253`) also remains unavailable from its origin; the official GStreamer mirror `.tar.xz` is source-tree-equivalent by version/tag evidence but not byte-identical.

This record is evidence only. It does not alter or rebuild the frozen runtime.

## Binary and build identity

- GStreamer runtime/plugins: `1.28.6`.
- Cerbero tag: `1.28.6`; commit `59548269f4fd0f701818f0bafdb102959ec81e65`.
- GStreamer tag: `1.28.6`; commit `2d3e05cbdad68e47d645f548899b432dc9fb4473`.
- GLib: `2.82.4`.
- Official Windows installer checksum evidence: `gstreamer-1.0-msvc-x86_64-1.28.6.exe` SHA-256 `059251444D1267B486EBA390B18D25FED87E10315E72F757EC6C7E912FA746B5`.
- The frozen application contains only the seven plugins and fifteen support DLLs below, not the complete SDK.

## Exact frozen 22-file mapping

| Frozen file | Bytes | SHA-256 | Source unit and effective license |
|---|---:|---|---|
| `gstreamer/plugins/gstcoreelements.dll` | 434688 | `7107089BBD228FCB7AF9BD7B0F1A6551F80EBAD22F4DF7163F679956219037E8` | GStreamer core 1.28.6; LGPL-2.0-or-later; release COPYING LGPL-2.1 |
| `gstreamer/plugins/gstwasapi2.dll` | 207872 | `333B737842662A2525E3B71730AC9490AB4BC36E261A7CB69FC35E733E592241` | gst-plugins-bad 1.28.6; LGPL-2.0-or-later |
| `gstreamer/plugins/gstaudioconvert.dll` | 37888 | `6B181FC0118919117C3399E4393D7DF1630AADD5BB91A587D05589823806AFCF` | gst-plugins-base 1.28.6; LGPL-2.0-or-later |
| `gstreamer/plugins/gstaudioresample.dll` | 35840 | `7581B2B3A21B52DA3F1BDA0E0370D0E378952DF2FC40E744C0089A5982CAA231` | gst-plugins-base 1.28.6; LGPL-2.0-or-later |
| `gstreamer/plugins/gstwebrtcdsp.dll` | 1538048 | `6C4EF85040286453D8AD91C4C1DC8A5AAD79D3364C573BA1A1F8E2D4E8F91737` | gst-plugins-bad 1.28.6, LGPL-2.1-or-later; WebRTC 2.1 BSD-3-Clause and Abseil Apache-2.0 statically incorporated |
| `gstreamer/plugins/gstflac.dll` | 67584 | `F73A689248CF0B474E510D281BA71C2EFE6E5095A241410CDFD3B80BE7982C36` | gst-plugins-good 1.28.6, LGPL-2.0-or-later; dynamically uses FLAC |
| `gstreamer/plugins/gstlevel.dll` | 32768 | `BCBD3537E205A31DE9AFDFEC890BE3F8C2C25F3F7218A71C558A961893A56792` | gst-plugins-good 1.28.6; LGPL-2.0-or-later |
| `gstreamer-1.0-0.dll` | 1428480 | `39B872CFD8B56C91274371117F2015FD75FC888FA84E6492B76B81DF7BEE1851` | GStreamer core 1.28.6; LGPL-2.0-or-later; release COPYING LGPL-2.1 |
| `gstbase-1.0-0.dll` | 519168 | `60C6CD23E68EDB4A0FC7E3E16D323A655F33A3768BF0DE6FEE6B30986A1E6B29` | GStreamer core 1.28.6; LGPL |
| `gstaudio-1.0-0.dll` | 578048 | `0C296D74C8C30A6F3505F6E603794240E469C45A14264D673782FD2FE7BFBA5E` | gst-plugins-base 1.28.6; LGPL |
| `gsttag-1.0-0.dll` | 268800 | `CF2AE72687971F4C6B05505949A657A256D51118F73CD984C9F3C14E3D32A828` | gst-plugins-base 1.28.6; LGPL |
| `gstbadaudio-1.0-0.dll` | 62464 | `46BECD7EBC92C63B734C146FB0BE80F01C23DDF2DA84C17B92BF42C683D67D54` | gst-plugins-bad 1.28.6; LGPL |
| `glib-2.0-0.dll` | 1438720 | `619CA0C9DAEB04A2FDEE24D59AD6054FFFE418AF4840D76DF051C2FA8828E93C` | GLib 2.82.4; LGPL-2.1-or-later |
| `gmodule-2.0-0.dll` | 26112 | `CA2873934BF7963644E6AB5841BFFC55EE113DBB7579D15A3C3FE80899DAE91C` | GLib 2.82.4; LGPL-2.1-or-later |
| `gobject-2.0-0.dll` | 361472 | `EAEF117A5D4E7A886F31F22B873F647AECC758EDDFC8738108C796992752D2DD` | GLib 2.82.4; LGPL-2.1-or-later |
| `intl-8.dll` | 13824 | `A3D0B4ACB339F733E9E6B632237AE340385CCABCA4704C23BB0308207BDEC196` | proxy-libintl 0.5; LGPL-2.0-or-later |
| `orc-0.4-0.dll` | 492544 | `FFB2EDBB794008D5A5264AC1DA33A81D9A526D1E806350B4BCAE2330B927A3A6` | ORC 0.4.42; BSD-2-Clause plus BSD-3-Clause example terms |
| `z-1.dll` | 93696 | `D9B2D176090BAFE2E0F9698624916D825F06273A994C2A9BB492B1D01BFE5C81` | zlib 1.3.1; Zlib |
| `ffi-7.dll` | 31744 | `B86BDB26B2422AC84199D5602C5CA717BD9F7184DE84E987B1706BE2E1FE240E` | GStreamer meson-ports libffi 3.2.9999.5; MIT |
| `pcre2-8-0.dll` | 348672 | `3AD537716233406D00A751B7595F7E7B1FE023D0FC4C54863046186D6961E9E3` | PCRE2 10.42; BSD-3-Clause-style PCRE2 licence/exemption |
| `FLAC-8.dll` | 327168 | `BA06AE1CEB7979C64BD6C5E6995E038C264BA214FA358842995F05A90E177181` | FLAC library 1.4.3; Xiph BSD-3-Clause |
| `ogg-0.dll` | 34304 | `A01DE0420CF5746F2418ECAB4A5DC34A65F070E50FD74F3E69108E2D904F5354` | libogg 1.3.5; Xiph BSD-3-Clause |

All hashes come from the frozen 579-file app manifest. Embedded paths/PE versions corroborate the listed module versions.

## Linkage and relinking facts

`XbPreview.Native.dll` dynamically imports `gstreamer-1.0-0.dll`, `gobject-2.0-0.dll`, and `glib-2.0-0.dll`. The selected plugins dynamically import the applicable GStreamer/GLib support DLLs. `gstflac.dll` dynamically imports `FLAC-8.dll`; the latter dynamically imports `ogg-0.dll`. GLib dynamically uses PCRE2/proxy-libintl and GObject dynamically uses libffi.

WebRTC Audio Processing and Abseil are the exception: Cerbero forces the WebRTC library static on Windows and builds its Abseil subproject static; `gstwebrtcdsp.dll` has no WebRTC/Abseil DLL imports and embeds both source/version paths. Their permissive license notices are therefore included with this evidence.

The LGPL libraries remain DLL-separated from the application. A companion must document replacing the root support DLLs and files under `gstreamer/plugins/` with ABI-compatible rebuilt versions, and product terms must preserve the LGPL reverse-engineering-for-debugging allowance.

## Source inputs and Cerbero modifications

Exact downloaded source hashes are recorded in `DOWNLOADS.json`. The companion must preserve the entire Cerbero 1.28.6 snapshot and, at minimum:

- GLib patches `0001` through `0014`; Windows shared-library selection.
- proxy-libintl symbol-compatibility patch.
- zlib Meson port and `HAVE_UNISTD_H`/`DSTDC` patches.
- four PCRE2 Meson/options/versioning patches.
- FLAC Meson and Windows NEON patches.
- libogg Meson/export/library-name patches.
- WebRTC GCC 15 and MSVC denormal-assembler patches.
- Abseil WrapDB patch archive `20240722.0-3`.
- libffi's `meson-3.2.9999.5` source fork.

GStreamer core/base/good/bad and ORC have no Cerbero source patches. Xiaobai applied no patches to these third-party sources; the official Cerbero modifications above remain part of the corresponding build material.

## Exact notices selected for the reviewed runtime

Tracked exact/normalized copies include GStreamer core/base/good/bad COPYING texts, GLib LGPL-2.1-or-later, proxy-libintl, ORC, zlib, libffi, PCRE2, FLAC, libogg, WebRTC Audio Processing and its compiled bundled-code notices, and Abseil/WrapDB notices. Their source and tracked hashes are recorded in `LICENSE-INVENTORY.json`.

This replaces the earlier GNU Library GPL v2-only staged evidence and deliberately excludes the irrelevant `ext_sctp_usrsctp_LICENSE.md` reference: usrsctp is not in the frozen selected plugin/import closure.

## GPL/restricted assessment

**NO.** No `gst-libav`, `gst-plugins-ugly`, x264/x265, GPL codec package, FLAC tools, or other restricted/GPL-only GStreamer component appears in the exact 22-file frozen set. The words “good” and “bad” describe plugin maturity, not the license.

## Future release asset

`RELEASE ASSET NAME = xiaobai-recorder-1.0.0-gstreamer-corresponding-source.tar.xz`

`CONTENTS =` every hashed source archive in `DOWNLOADS.json`; the full Cerbero 1.28.6 snapshot/commit; Abseil WrapDB patch; Cerbero recipes/configuration/patches; Windows x86_64 MSVC/VS2022 rebuild instructions; exact licenses; artifact manifest; no-Xiaobai-modification statement; DLL/plugin replacement and LGPL debugging/relink instructions.

`SOURCE VERSION =` the exact versions/commits in this record.

`SHA256 = BLOCKED — asset is planned but has not been assembled or published.`

`WHY REQUIRED =` durable corresponding source/build material for the redistributed LGPL DLLs/plugins and complete notices for statically incorporated permissive code.

## Closure

`CLOSURE = BLOCKED`

Exact next step: assemble the planned asset from the recorded inputs, resolve or explicitly document acceptance of the WebRTC source-tree-equivalent mirror archive, independently review its contents/rebuild instructions, publish it beside the release, then record its final SHA-256.
