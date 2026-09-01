# Window Stage Layer 1 — Flat Stage Restore

Status: **WINDOW-STAGE-LAYER1-FROZEN**

This candidate starts at the frozen Window Capture Layer 0 commit
`ff0b19ec0b5574e0dcd9ec1162c5f18c0393f204` and restores only the flat
composition stage. It is intentionally uncommitted and untagged until a human
records and accepts a real target window. That hold is now satisfied: human
acceptance is complete, and Xiaobai has explicitly approved freezing this
Layer 1 baseline.

## Scope

`WindowStageComposer` owns only deterministic composition facts:

- fixed `#F3F0EA` OutputCanvas background;
- centered destination rectangle;
- aspect-fit scaling with a maximum 90% width/height stage fraction;
- full source UVs (`0,0,1,1`) for no-crop composition;
- recomputation from each frame's current content size, preserving fixed
  OutputCanvas dimensions after a target resize.

The renderer consumes those facts at its existing OutputCanvas seam. The WGC
window texture remains the sole input and the existing RenderFrameTap continues
to observe the completed OutputCanvas.

## Mechanical restore

The frozen ancestor already contained equivalent placement math under the
historical `WindowCardPlacement` name. This restore mechanically gives that math
the narrower `WindowStageComposer` ownership boundary and adds an independent
native Gate which compiles and calls the same production header. The render
order remains: clear fixed OutputCanvas, set flat destination viewport, draw the
full window texture, then hand the completed OutputCanvas to RenderFrameTap.

## Automated evidence

- Build-cache recovery removed only the proven regenerable
  `XbPreview.Native.ipdb` and matching `XbPreview.Native.iobj` from
  `artifacts/obj/XbPreview.Native/Release/x64`. The subsequent Release x64
  solution build explicitly fell back to full LTCG compilation, regenerated
  6,684 functions, and produced `XbPreview.Native.dll`, Host, and test outputs:
  PASS.
- `XbPreview.FlatStage.Tests.exe`: PASS for fixed background, centered
  aspect-fit, full-source no-crop, safe margins, and landscape-to-portrait
  resize refit.
- `XbPreview.Managed.Tests.exe --window-capture-target-abi`: PASS as the one
  minimal Window Capture Layer 0 regression Gate.
- `git diff --check`: PASS.
- Protected-module audit against the frozen commit: zero changes to WGC target
  creation, Audio, Encoder, RenderFrameTap, RecordingController, timestamps,
  Safe Publish, manifest/recovery/storage, Director, Content Camera, and Camera
  Ownership.

No product source was changed during build-cache recovery. No real media was
generated and no human smoke was run.

## Human acceptance

Human smoke: **PASS**.

The accepted real Chrome recording established that:

- Chrome entered the flat stage correctly;
- the background was the current light/warm-white safety background;
- wide and narrow window shapes both refitted correctly after resize;
- aspect-fit remained correct without visible stretch or crop;
- no real desktop pixels leaked into the stage;
- the final MP4 was viewable.

Xiaobai explicitly approved freezing this result. The current `#F3F0EA`
light/warm-white background is only the temporary Layer 1 verification
background; it is not a final Showcase background decision.

## Explicitly absent

Layer 2 and later capabilities are not restored: shadow, rounded visual effect,
WindowCardTransform, Rotation X/Y, perspective, vertex-shader 2.5D, Showcase or
Showcase Motion, Enter/Hold/Return, background presets, RecordingStyle,
Director-stage linkage, black-edge shader/detection, CPU readback, fallback
capture engines, PrintWindow, BitBlt, Desktop Duplication fallback, and new UI or
product copy.

Layer 2 has not been entered. No Layer 2 behavior is part of this freeze.
