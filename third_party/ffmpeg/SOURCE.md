# FFmpeg development-build record

Audio V2 uses FFmpeg only after recording stops. No FFmpeg binary is committed
or declared release-ready by this candidate; installer bundling remains a
separate license/package closure item.

- Provider linked by the official FFmpeg download page: BtbN FFmpeg Builds
- Archive: `ffmpeg-n8.1-latest-win64-lgpl-shared-8.1.zip`
- Downloaded build version: `n8.1.2-34-g9b6c8969e0-20260809`
- Archive SHA-256: `6d4afe797a68af283ed42254827027f7d56940ba6c9e37ebed9c0e87a9e0c54c`
- Architecture/linkage: Windows x64, shared libraries
- Configuration audit: `--enable-version3 --enable-shared --disable-static`;
  no `--enable-gpl` or `--enable-nonfree`; `libx264`, `libx265`, and `libxvid`
  are explicitly disabled.
- Candidate classification: recorded dev-only LGPL build, not a formal product
  distribution dependency.

Before product distribution, pin the exact archive, reproduce the complete
configure flags and corresponding source, include LGPL notices and license,
permit replacement/relinking of the shared libraries, and complete a separate
dependency/legal review. See `docs/stages/MVP-AUDIO-V2-FLAC-FFMPEG.md` for the
full configure line captured by the proof.
