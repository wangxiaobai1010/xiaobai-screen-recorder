# Window Capture F-Hero Clean Rebuild

Status: `WINDOW-CAPTURE-LAYER0-FROZEN`

This is the locally frozen Layer 0 Window Capture baseline reconstructed from
the frozen audio base and accepted by a real human recording. It is not, and
must not be described as, a restoration of F-Hero source code. F-Hero is used
only as a behavior anchor.

## Baseline and evidence boundary

- Base and current uncommitted HEAD:
  `aec61b95f8c8266e93c95a954ece8099d8f41dab`
- Base subject: `feat(audio): freeze ScreenRecorderLib WASAPI subsystem`
- Branch: `recovery/window-capture-fhero-rebuild`
- Worktree: `E:\小白录屏器\worktrees\window-capture-fhero-rebuild`
- The regression snapshot was read only. Its 11 SHA-listed evidence files were
  re-hashed successfully, and its 20 listed untracked source/config copies were
  accounted for. No snapshot source, configuration, or binary was copied.
- Neither Git history/reflog nor the snapshot proves an exact F-Hero source
  commit. Later black-edge, target-lifecycle, Window Stage, Showcase, encoder,
  audio, and camera changes therefore were not imported.

## Clean-base route

The fixed audio base already contains the complete required route:

`CaptureTarget(Window, HWND)` -> `XbPreview_SetCaptureTarget` ->
`IGraphicsCaptureItemInterop::CreateForWindow(HWND)` -> the existing
free-threaded frame pool -> `FrameArrived` -> the existing D3D11 renderer and
fixed OutputCanvas -> `RenderFrameTap` -> the existing H.264 and frozen audio ->
existing validation and Safe Publish.

Monitor remains the default and still selects `CreateForMonitor` at the same
capture-item seam. Window creation failure follows the existing explicit error
path; there is no Window-to-Monitor fallback, second capture engine, CPU
readback, PrintWindow, BitBlt, or Desktop Duplication fallback.

## Candidate-only changes

| File | Why it changed | Provenance and protected boundary |
| --- | --- | --- |
| `XbPreview.Native/PreviewEngine.cpp` | Emit capture kind/HWND after the existing diagnostic logger opens; emit the first WGC frame dimensions once. | Mechanical observation at the base WGC seam. It does not change CaptureItem creation, FramePool, textures, renderer, OutputCanvas, timestamps, encoding, audio, or lifecycle behavior. |
| `XbPreview.Native/PreviewEngine.h` | Hold one private atomic flag for the one-shot first-frame diagnostic. | Private diagnostic state only; no public ABI/layout change. |
| `XbPreview.Managed.Tests/Program.cs` | Add narrow CaptureTarget/ABI and real external-HWND item-creation Gate entries. | The item Gate uses the existing Host model/ABI and actual Native `CreateForWindow`; it rejects non-selectable or recorder-owned targets and creates no hidden target substitute. Production ownership is unchanged. |
| `XbPreview.Managed.Tests/PreviewLifecycleTests.cs` | Add the minimal Window/Monitor Start/Stop contract Gate with a deterministic fake session. | Test-only mechanical contract: target before Start, no active retarget/fallback, one Stop/Finalize, existing Validate/Publish facts, and unchanged Monitor default. `RecordingController` production source is untouched. |
| `tools/window-capture/Run-WindowCaptureSmoke.ps1` | Provide the single human entry: enumerate a real visible HWND, run the real item Gate, then visibly launch the Release recorder. | Harness only. It does not alter product behavior or manufacture a hidden capture target. |
| `docs/recovery/WINDOW-CAPTURE-FHERO-CLEAN-REBUILD.md` | Record provenance, scope, verification, and the human stop point. | Documentation only. |

## Verification results and budget

The authorized verification budget was respected:

1. Exactly one clean Release x64 solution rebuild:

   `MSBuild XbPreview.P1D-A1.sln /m /restore /t:Rebuild /p:Configuration=Release /p:Platform=x64 /v:minimal`

   Result: PASS, exit code 0. There were no errors; the only two warnings were
   existing unused-parameter warnings in frozen
   `third_party/screenrecorderlib-audio/log.h`.

2. `--window-capture-target-abi`: PASS, exit code 0.

3. `--window-capture-start-stop-contract`: PASS, exit code 0.

4. `--window-capture-item <real-visible-external-HWND>`: PASS on a real visible
   Google Chrome window. The first human run exposed and then received a
   harness-only diagnostic-path correction; its Native evidence already proved
   the real HWND reached `CreateCaptureItemForWindow`, the shared
   `CreateFreeThreadedFramePool`, `StartCapture`, a positive first-frame
   size/count, and Native Stop. No hidden or fabricated HWND was used.

After the item-Gate message-loop correction, only the Managed.Tests harness was
incrementally compiled with `dotnet build --no-restore`; it passed with zero
warnings and zero errors. No second solution build was performed.

The clean worktree had no checked-in local SDK payload. For the build only, its
ignored `artifacts` tree contains directory junctions to the existing pinned
GStreamer 1.28.6 and LGPL FFmpeg 8.1 dependency caches in the read-only incident
worktree. MSBuild read those dependency caches and copied their runtime payload
into the new Release output; no incident source/configuration was imported or
modified.

No LongRun, frozen audio, Director, Showcase, DPI, multi-monitor, MP4 stress,
A/V/EOS, or ancestor matrix Gate was run by Codex.

## Protected and excluded scope audit

The protected-path diff count against the fixed base is zero. This covers audio,
ScreenRecorderLib donor, AAC, H.264, PreviewRenderer, RenderFrameTap,
OutputCanvas ownership, RecordingController, VideoEncoderTimestamp,
RecordingStorageSafety, SessionManifest, Validate, Safe Publish, Recovery,
storage safety, Director Lite, camera ownership, and Manual/Follow.

The complete added-code scan found no forbidden PrintWindow, BitBlt, Desktop
Duplication, Window Stage/Card, background, shadow, rounded-corner, perspective,
2.5D, Showcase, RecordingStyle, target-reconfiguration/rollback,
pre-minimized-guard, automatic target replacement, multi-window, multi-monitor,
or DPI implementation. Base behavior bearing similar names remains byte-for-byte
untouched.

Before the mechanical local freeze, HEAD still equaled the fixed base and the
branch had no upstream. The freeze creates one local commit and the annotated
tag `window-capture-layer0-pass-2026-08-13`; it performs no push or merge and
does not modify the pre-existing base tag
`mvp-audio-subsystem-pass-2026-08-12`.

## Human acceptance result

Release executable:

`E:\小白录屏器\worktrees\window-capture-fhero-rebuild\artifacts\bin\Release\x64\XbPreview.Host.exe`

Xiaobai completed the real interactive Window Capture Gate and explicitly
reported `WINDOW-CAPTURE-REBUILD-HUMAN-PASS` with these observed facts:

1. a real Google Chrome window was captured successfully and was the correct
   target;
2. the recorded picture updated normally while the browser window was resized;
3. no real desktop content leaked into the recording;
4. the final MP4 played normally;
5. system audio and microphone audio were both present and normal; and
6. combined system plus microphone audio had no obvious noise.

This freezes only pure Layer 0 Window Capture. It does not restore or authorize
Window Stage/Card, backgrounds, shadows, rounded corners, black-edge shaders,
2.5D, rotation, perspective, Showcase, presets, RecordingStyle, or other visual
layers.
