# FFmpeg source companion

| Field | Value |
| --- | --- |
| Archive filename | `xiaobai-recorder-1.0.0-ffmpeg-corresponding-source.tar.xz` |
| Archive size | 818,718,304 bytes (780.791 MiB) |
| Archive SHA-256 | `1D31B5C39C6F24F983E869AE4DCE5374A5E9EEFBF0647A28BA4C2D8ADCAA93A0` |
| FFmpeg identity | `n8.1.2-34-g9b6c8969e0-20260809` |
| FFmpeg commit | `9b6c8969e05b4f0b29f0f85cd501be6b3e582e6b` |
| Builder commit | `2437e7b868da3c11872367b15f3c613b87c24819` |
| FFTW version | 3.3.11 (`93ed4c786934aec9946f8dda4b4e3eb08f8be41c`) |
| Chromaprint source identity | Commit `ab48115481c14873eb870e7a88334550c68d36c1` |
| Dependency records total | 111 |
| Covered | 94 |
| Justified exclusions | 17 |
| Unresolved | 0 |
| Source-manifest model | Group-level component/source identities, compact record hashes, Cargo package checksums, retained-archive SHA-256 values, and one final archive SHA-256; no exhaustive expanded-file hash list. |
| GitHub Release companion intent | Publish beside the Xiaobai Recorder 1.0.0 binary distribution as the FFmpeg Corresponding Source companion. |
| Archive size gate | PASS: 818,718,304 bytes is below the 2 GiB per-asset limit. |

The companion self-audit and archive-structure smoke check passed. The archive
contains the exact FFmpeg and BtbN builder identities, FFTW/Chromaprint and
the remaining source-relevant static closure, the 271-crate rav1e closure,
applicable license/notice groups, build/control records, and the builder patch
set. The combined frozen FFmpeg/FFTW payload is carried on the engineering
distribution basis recorded in `../decisions/FFMPEG-DISPOSITION.md`.
