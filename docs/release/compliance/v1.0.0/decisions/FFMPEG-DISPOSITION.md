# FFmpeg disposition

| Field | Decision |
| --- | --- |
| Decision | `KEEP_CURRENT_FFMPEG` |
| Frozen binary | `n8.1.2-34-g9b6c8969e0-20260809` |
| Why not LGPL-only | FFTW 3.3.11 object code is statically incorporated in `avformat-62.dll` through Chromaprint. |
| Engineering distribution basis | Effective GPL-3.0-or-later for the combined frozen FFmpeg/FFTW payload, preserving original component notices. |
| Xiaobai Recorder license | MIT |
| Technical boundary | Xiaobai invokes `ffmpeg.exe` as a separate process; it does not directly link or load libav. |
| Product code change | None |
| Runtime change | None |
| Release condition | Corresponding Source and notices must accompany public binary distribution. |

This is an engineering compliance decision, not legal advice.
