# GStreamer / GLib source companion candidate

## Candidate identity

- `ARCHIVE FILENAME = xiaobai-recorder-1.0.0-gstreamer-corresponding-source.tar.xz`
- `SIZE = 35709296 bytes`
- `SHA256 = A1BBAFC3EF8248547CB41D855886CFA8C10250A0ADCA4FCC8B8816091DA7FE93`
- `RELEASE-COMPANION INTENT = durable corresponding source, exact build-control inputs, notices, and LGPL replacement/debugging guidance for the unchanged frozen GStreamer/GLib runtime`

## Runtime and build identity

- `GSTREAMER = 1.28.6`
- `GSTREAMER SOURCE COMMIT = 2d3e05cbdad68e47d645f548899b432dc9fb4473`
- `CERBERO = 1.28.6`
- `CERBERO COMMIT = 59548269f4fd0f701818f0bafdb102959ec81e65`
- `CONFIGURATION = native Windows x86_64 MSVC / config/win64.cbc`
- `PLUGIN SET COUNT = 7`
- `SUPPORT DLL SET COUNT = 15`
- `RUNTIME MAPPING COUNT = 22`
- `SOURCE-MANIFEST RECORD COUNT = 16`
- `UNRESOLVED = 0`

### Frozen plugins

`gstcoreelements.dll`, `gstwasapi2.dll`, `gstaudioconvert.dll`, `gstaudioresample.dll`, `gstwebrtcdsp.dll`, `gstflac.dll`, `gstlevel.dll`.

### Frozen support DLLs

`gstreamer-1.0-0.dll`, `gstbase-1.0-0.dll`, `gstaudio-1.0-0.dll`, `gsttag-1.0-0.dll`, `gstbadaudio-1.0-0.dll`, `glib-2.0-0.dll`, `gmodule-2.0-0.dll`, `gobject-2.0-0.dll`, `intl-8.dll`, `orc-0.4-0.dll`, `z-1.dll`, `ffi-7.dll`, `pcre2-8-0.dll`, `FLAC-8.dll`, `ogg-0.dll`.

## Source component list

1. GStreamer core 1.28.6
2. gst-plugins-base 1.28.6
3. gst-plugins-good 1.28.6
4. gst-plugins-bad 1.28.6
5. GLib 2.82.4
6. proxy-libintl 0.5
7. ORC 0.4.42
8. zlib 1.3.1
9. libffi meson-3.2.9999.5
10. PCRE2 10.42
11. FLAC 1.4.3
12. libogg 1.3.5
13. WebRTC Audio Processing 2.1
14. Abseil 20240722.0
15. Abseil WrapDB patch 20240722.0-3
16. Cerbero 1.28.6 build-control snapshot

## WebRTC disposition

- `VERSION = 2.1`
- `SOURCE-EQUIVALENT = YES`
- `SOURCE IDENTITY = tag v2.1; commit 846fe90a289f58b7c9303a635142aa2c7caa93e5`
- `INCLUDED MIRROR SHA256 = AE9302824B2038D394F10213CAB05312C564A038434269F11DBF68F511F9F9FE`
- `CERBERO-REQUESTED TRANSPORT SHA256 = 35E86B986D02EA15F3D04741A1A5A735BA399BC0FAC0EE089C39480E35FC3253`

The official GStreamer mirror archive is not byte-identical to the unavailable Cerbero-requested `.tar.gz`. Source equivalence is established by the recorded upstream tag/commit identity, embedded 2.1 project/release identity, exact Abseil wrap pins, license evidence, and clean applicability of both Cerbero patches.

## Closure

- `RUNTIME MAPPING = 100%`
- `SOURCE CLOSURE = 100%`
- `LICENSE CLOSURE = 100%`
- `BUILD CONTROL = PASS`
- `XIAOBAI SOURCE MODIFICATIONS = NONE`
- `CERBERO/UPSTREAM PATCHES = COVERED`
- `ARCHIVE STRUCTURE = PASS`

The candidate is ready for independent source-companion review. Publication beside a release and any legal conclusion remain separate release actions.
