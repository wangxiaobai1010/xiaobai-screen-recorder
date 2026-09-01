# FFmpeg build and corresponding-source evidence

## Frozen binary identity

| Field | Evidence |
| --- | --- |
| Build string | `n8.1.2-34-g9b6c8969e0-20260809` |
| Frozen archive name | `ffmpeg-n8.1-latest-win64-lgpl-shared-8.1.zip` |
| Frozen archive SHA-256 | `6D4AFE797A68AF283ED42254827027F7D56940BA6C9E37EBED9C0E87A9E0C54C` |
| Runtime payload | `ffmpeg.exe`, seven FFmpeg shared DLLs, and `LICENSE.txt` |
| FFmpeg source | `release/8.1` at `9b6c8969e05b4f0b29f0f85cd501be6b3e582e6b` |
| Builder | BtbN/FFmpeg-Builds release `autobuild-2026-08-09-13-03` |
| Builder source | `2437e7b868da3c11872367b15f3c613b87c24819` |
| Xiaobai source modifications | No. Frozen payload hashes are the upstream-builder bytes locked by the release input. |
| Builder modifications | AOM patch `0001-Fall-back-to-built-in-vmaf-model-on-load-failure.patch`; aribb24 patches `12.patch`, `13.patch`, and `17.patch`. |

The eight runtime binary hashes and the bundled license hash match the frozen
release lock. The rolling `latest` archive is no longer available upstream.
The immutable sibling asset recorded by the historical release was
`ffmpeg-n8.1.2-34-g9b6c8969e0-win64-lgpl-shared-8.1.zip`, SHA-256
`2936E5449886641B4279CA3FC554B678C8E9A2D20DD0C0A34FE7208B254A0905`.
BtbN's `util/repack_latest.sh` explains why the renamed/recompressed floating
asset has a different archive hash.

## Exact configuration

```text
--prefix=/ffbuild/prefix --pkg-config-flags=--static --pkg-config=pkg-config --cross-prefix=x86_64-w64-mingw32- --arch=x86_64 --target-os=mingw32 --enable-version3 --disable-debug --enable-shared --disable-static --disable-w32threads --enable-pthreads --enable-iconv --enable-zlib --enable-libxml2 --enable-libvmaf --enable-fontconfig --enable-libharfbuzz --enable-libfreetype --enable-libfribidi --enable-vulkan --enable-libshaderc --enable-libvorbis --disable-libxcb --disable-xlib --disable-libpulse --enable-gmp --enable-lzma --enable-liblcevc-dec --enable-opencl --enable-amf --enable-libaom --enable-libaribb24 --disable-avisynth --enable-chromaprint --enable-libdav1d --disable-libdavs2 --disable-libdvdread --disable-libdvdnav --disable-libfdk-aac --enable-ffnvcodec --enable-cuda-llvm --disable-frei0r --enable-libgme --enable-libkvazaar --enable-libaribcaption --enable-libass --enable-libbluray --enable-libjxl --enable-libmp3lame --enable-libopus --enable-libplacebo --enable-librist --enable-libssh --enable-libtheora --enable-libvpx --enable-libwebp --enable-libzmq --enable-lv2 --enable-libvpl --enable-openal --enable-liboapv --enable-libopencore-amrnb --enable-libopencore-amrwb --enable-libopenh264 --enable-libopenjpeg --enable-libopenmpt --enable-librav1e --disable-librubberband --enable-schannel --enable-sdl2 --enable-libsnappy --enable-libsoxr --enable-libsrt --enable-libsvtav1 --enable-libtwolame --enable-libuavs3d --disable-libdrm --enable-vaapi --disable-libvidstab --enable-libvvenc --disable-whisper --disable-libx264 --disable-libx265 --disable-libxavs2 --disable-libxvid --enable-libzimg --enable-libzvbi --extra-cflags=-DLIBTWOLAME_STATIC --extra-cxxflags= --extra-libs=-lgomp --extra-ldflags=-pthread --extra-ldexeflags= --cc=x86_64-w64-mingw32-gcc --cxx=x86_64-w64-mingw32-g++ --ar=x86_64-w64-mingw32-gcc-ar --ranlib=x86_64-w64-mingw32-gcc-ranlib --nm=x86_64-w64-mingw32-gcc-nm --extra-version=20260809
```

`--enable-gpl` and `--enable-nonfree` are absent. x264, x265, xavs2, and
xvid are explicitly disabled. The archive is labeled as BtbN's LGPL shared
variant and ships only the FFmpeg LGPLv3 text. That label is contradicted by
the exact dependency build described below: Chromaprint is built against and
statically exposes FFTW 3.3.11, which is GPL-2.0-or-later. This evidence must
not be represented as an LGPL-only closure.

## Linkage evidence

The PE import audit used Visual Studio `dumpbin` without executing FFmpeg.
`ffmpeg.exe` imports only the seven distributed FFmpeg DLLs and Windows/UCRT
libraries. The FFmpeg DLLs import only one another and Windows/UCRT libraries.
Therefore:

- the FFmpeg libraries themselves are shared and replaceable;
- no configured third-party sidecar DLL is redistributed;
- compiled external libraries are statically incorporated unless listed below
  as headers, dispatcher/shim, or Windows system API;
- the original linker maps/build logs are still needed for an exact
  object-to-DLL allocation.

## External dependency identity matrix

All source identities below come from builder snapshot
`2437e7b868da3c11872367b15f3c613b87c24819`. “Static” means no external DLL
was found and the build uses `--pkg-config-flags=--static`. The license corpus
for these exact revisions is not yet complete; it is part of the blocked
corresponding-source and license-basis review.

The normalized per-item matrix, including 111 direct, transitive, header,
dispatcher, toolchain, and system closure records with license classifications
and explicit unknowns, is `FFMPEG-DEPENDENCIES.json`.

| Group | Exact source identities | Incorporation | License closure |
| --- | --- | --- | --- |
| Toolchain/runtime | MinGW-w64 `a556c6943c442465dc9a051bc6d3a6d452df4a1d`; configured GCC 15.2.0, binutils 2.46.0, MinGW-w64 13.0.0/UCRT, POSIX winpthreads, libgomp; crosstool-NG config origin `185f3483e0e5028357b131fb97c4331551a70a1e` | Static runtime pieces | BLOCKED in companion |
| Base/text | libiconv `9d19c66d0a1768cffcf497b2db70bf4018b578d7`; gnulib `103c922f47f8b0fb0503024783bdaff5016eea82`; zlib `e3dc0a85b7032e98380dec011bc8f2c2ee0d8fca`; FriBidi `069a7e3d31e6aa74f2068a8e0804106ce7906639`; GMP fork `9994908f090c694f8a152d660dc6852e0c48557a`; libogg `06a5e0262cdc28aa4ae6797627a783b5010440f0`; libxml2 `c8eaf2236ff16667970f96f3f01e119c99d38ab2`; xz `f3b5688159c60495f48db3942a36509671dfce89` | Static | BLOCKED in companion |
| Fonts/audio base | FreeType `5336c0d4da22a13dab3389eb153b12672fdf841c`; Fontconfig `939e33ee473d70a790c89b624385c2c0a5875a51`; HarfBuzz `9f2f03173b7fee860cc00d999857d09fa4a362e2`; libunibreak `3ce4bfa3129ff3738046a44a6db533d2ce25af2b`; Vorbis `e3c9861ff096d52378e131ff8c334552e09cdffa` | Static | BLOCKED in companion |
| Video quality/GPU | VMAF `e9909adb89306a270d9c78207bd12acf730279ad`; Vulkan-Headers `v1.4.356`; Vulkan-Shim-Loader `65b3936528cd92eb4ea3de485d03f858a3850484`; SPIRV-Cross `81fc2ea76c2b8018d4427c380961a6886cb3ce7d`; SPIRV-Headers `02c0394e57af6dfdda7f68973df6aa20fc3f5def` | Static or headers/shim | BLOCKED in companion |
| shaderc closure | shaderc `49a8724d561c13db22b52f99f2a0e2707a9a9e3c`; Abseil `dbf88f932096c7f7714356e919f04749eb87c3e9`; effcee `910ed15722d5d05c9d71ecf36c1a22243cb79b02`; glslang `ce138e2c2d6992b31ff4cd2e955904637785a881`; googletest `52eb8108c5bdec04579160ae17225d66034bd723`; re2 `927f5d53caf8111721e734cf24724686bb745f55`; SPIRV-Headers `daa093dd29aab8cbb6562b808370562f56e399fb`; SPIRV-Tools `d5bbf95d87dd6d2694fbf09acfb42a00c93575e8` | Static; exact linked subset needs link maps | BLOCKED in companion |
| GPU APIs | OpenCL-Headers `a98488062f50c77c3e2edaf9c4f8dca7c41781ec`; OpenCL-ICD-Loader `18fdcd58286376124f938948aa8ed156079c1c16`; AMF headers `6ec029531e356102aafe1e236cfd0ddf739939da`; nv-codec-headers `15ee32753c92faddbabbff11676779618fc6db7e`; libva `28388a091187e2ba9e99cb750ec7426f91f73cbb` | Headers/static dispatcher; drivers system-resolved | BLOCKED in companion |
| AV1/subtitle/fingerprint | AOM `43f2b6a99000057184332c8c2dd4bedc19fdec6f`; libpng `d1d0abeffede1cc898ddc3d0e600839cf026d749`; aribb24 `5e9be272f96e00f15a2f3c5f8ba7e124862aec38`; Chromaprint `ab48115481c14873eb870e7a88334550c68d36c1`; FFTW 3.3.11 at `93ed4c786934aec9946f8dda4b4e3eb08f8be41c`; dav1d `c150ba6c9b9be0956330a9ddfee33ad88f2b1bc5` | Static; Chromaprint's private link line is `-lfftw3 -lstdc++` | **FAIL: FFTW is GPL-2.0-or-later; static incorporation contradicts the LGPL-only label and absent `--enable-gpl`** |
| Media/codecs A | game-music-emu `dd3182a8bdae3ff761438632aace418fbcaed439`; Kvazaar `d6815293f34a094e26ba6c50b8644660ddc13e09`; LCEVCdec `a254bd474649e5dcd8182689ac414420bfe8d8c3`; libaribcaption `f9d8c50fe5e51c98d101f69d74591295cb568036`; libass `4a05d8127f525943ebf45fdc6497c9e665947f0d`; libudfread `139a2194525f2745b98a98e4d8fa627d07440176`; libbluray `8b4fb6e2562bb86601ea5a2c4140af6d8f3f1cf4` | Static | BLOCKED in companion |
| JPEG XL closure | Brotli `27cc9fe9a4aa8901be3b3ba29f2f09eaddb08f97`; LittleCMS2 `5cc0eec7ae8350dd0f6c4c07b077f78f18dfe970`; libjxl `f0a1c5ff9dbba51ffa932433ffb80e5e6b6e22a7` | Static | BLOCKED in companion |
| Audio/network A | LAME SVN revision `6531`; Opus `3da9f7a6db1c05c3996cb363a9d1931a978bf1be`; libplacebo `05ac2cca6571c04d06369a26825d207781b73f32`; mbedTLS `v4.1.0`; librist `6e2a5e341bbdbe4ca283f3f79e42db8be0f2e027`; libssh `689d7320644bbf06f77911a58d41a68e3e68675b`; OpenSSL `3.6.3` used transitively by libaribcaption/libssh/SRT | Static | BLOCKED in companion |
| Codecs B | Theora `28fd5ec77f0ad0e07a371cef1047828116f6bd8a`; VPX `8592391cdb3ef142c56d835788d71d6d4de36a63`; WebP `733c91e461c18cf1127c9ed0a80dccbcfed599d3`; ZeroMQ `2e8a6ccb414ca79636392604893c79c3b0d00dc4` | Static | BLOCKED in companion |
| LV2/lilv closure | lv2 `3c57dae600a5ad8d05acd53ee3490f7d91cb7be6`; serd `b6053bb2a4533e6a8e3821c1061f140d5e666938`; zix `17d632393f5addb6fa24dda2927b96515407cca7`; sord `4b5232bb91e7f2f1b84df384494d073fabbcd312`; sratom `9e8ee84eed502e6030ce8bb49694fd28542b08fc`; lilv `4b8f30055fa5cb4134a2eb45fc1555bc04289314` | Static | BLOCKED in companion |
| Codecs/platform C | oneVPL `d77f9195cf495b937631607333288fd917ae8939`; OpenAL Soft `3ac145cffddbd858f72efabdfbf676d355a34802`; OpenAPV `9f6fd2a7369db90acec67d99fc57724f1136fb84`; opencore-amr `7dba8c32238418ce0b316a852b2224df586ca896`; OpenH264 `a8e04adb69c79757da014007d4694684a64c7b74`; OpenJPEG `9dd4b3c98a78f50a48fb08f27bf198d4ae1d8528`; OpenMPT `4c3ba47c7977fe3016b474f8ed192e619ae85f7d` | Static | BLOCKED in companion |
| Rust/codec closure | rav1e `564ae3b0007ae2b06893fd7166bf88c5a84c5b63`; build executes mutable `cargo update cc` | Static | BLOCKED: exact historical `cc` crate unresolved |
| Media/codecs D | SDL `b8b3f5ef2001cbe7c11f62d41a9bf47d4a2d8b07`; libsamplerate `2ccde9568cca73c7b32c97fefca2e418c16ae5e3`; Snappy `3ac3722e1bee4b99860a282fb779e8e72fa18163`; soxr `945b592b70470e29f917f4de89b4281fbbd540c0`; SRT `c39196c9a568ae4e3289dd65cf54ba4154deb4a1`; SVT-AV1 `d3c4cb3947a8bfed0aa5a2be996b37bb117fa1bd`; TwoLAME `6fced852d4d5cfad58cf9dbe3ea619b08e87d398`; uavs3d `0e20d2c291853f196c68922a264bcd8471d75b68`; VVenC `7d60406c66fa1659b8df74dd8d62bc41d3c90157`; zimg `659c78a6c43536e6fc863c48cd89e77ce25e6008`; zvbi `41477c97c8edf7a01f1594b2a95b94f0117eed21` | Static | BLOCKED in companion |
| Windows transport | Schannel | Windows system API | No bundled third-party source |

Explicitly disabled or not selected: Avisynth, davs2, libdvdread/libdvdnav,
FDK-AAC, frei0r, Rubber Band, DRM, vidstab, whisper.cpp, x264, x265, xavs2,
xvid, XCB/Xlib, and PulseAudio. Exact builder scripts establish that FFTW is
selected through Chromaprint, OpenSSL is used by libaribcaption/libssh/SRT,
and libsamplerate is selected through static SDL2 with
`SDL_LIBSAMPLERATE=ON`; none may be omitted from the closure.

## GPL foundation contradiction

Builder script `scripts.d/50-chromaprint.sh` depends on the FFTW build from
`scripts.d/25-fftw3.sh`, configures Chromaprint with `-DFFT_LIB=fftw3`, and
emits `Libs.private: -lfftw3 -lstdc++`. FFmpeg is configured with
`--enable-chromaprint`, `--pkg-config-flags=--static`, and no third-party DLL
sidecars are imported. Chromaprint upstream explicitly documents that choosing
FFTW makes the resulting Chromaprint binary GPL; FFTW 3.3.11 is
GPL-2.0-or-later. On the available evidence, the frozen payload therefore
contains GPL code while its build omits `--enable-gpl` and its archive ships
only LGPLv3 terms.

No Xiaobai source modification caused this condition: the frozen files are
the exact upstream-builder bytes. It nevertheless blocks redistribution as an
LGPL-only component. Closure would require one of: documented commercial FFTW
distribution rights covering these exact binaries; reviewed GPLv3-or-later
combined-work treatment with complete notices and corresponding source; or a
separately authorized replacement candidate built without FFTW. This evidence
task authorizes none of those product/foundation changes.

## Corresponding-source release plan

| Field | Planned value |
| --- | --- |
| Release asset name | `xiaobai-recorder-1.0.0-ffmpeg-corresponding-source.tar.xz` |
| Contents | FFmpeg at the full source SHA; exact BtbN builder snapshot and four patches; every builder download-cache checkout and submodule; shaderc DEPS; rav1e Cargo registry sources after the historical `cargo update cc`; GCC/binutils/MinGW-w64/libgomp/winpthreads source; generated configure output, `config.log`, verbose build log and linker maps; all exact license/notice files; `SHA256SUMS`; binary/source/link-mode manifest |
| Source version | FFmpeg `9b6c8969e05b4f0b29f0f85cd501be6b3e582e6b`; builder `2437e7b868da3c11872367b15f3c613b87c24819`; dependency revisions above |
| SHA-256 | BLOCKED — the asset has not been assembled and the FFTW/GPL distribution basis is unresolved |
| Build/config files | Exact configure line above, BtbN variant `win64-lgpl-shared`, builder scripts/patches, future logs/link maps |
| Modification record | Xiaobai: none; BtbN: four patches listed above |
| Why required | Durable corresponding source and relink/rebuild material for LGPL-covered code and a complete attribution/license corpus for statically incorporated dependencies |

## Closure

**BLOCKED WITH ONE PRECISE REMAINING GAP:** establish a legally and technically
reviewed distribution basis for the exact statically incorporated FFTW code.
The current LGPL-only label is contradicted by the exact builder configuration.
Any GPL/commercial-license disposition must also close the historical
crosstool-NG HEAD, rav1e `cc` resolution, exact dependency licenses, build
logs/linker maps, and final corresponding-source companion SHA-256.
