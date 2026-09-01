# Window Stage Layer 2 - Window Card Shadow Freeze

Status: **WINDOW-STAGE-LAYER2-FROZEN**

Freeze date: 2026-08-13

Layer 1 frozen ancestor:
`9102a5fac8b9abcc1944c848ef3954b76eb0ef9d`
(`window-stage-layer1-pass-2026-08-13`).

This freeze records the accepted flat Window Card visual treatment. It does
not change the frozen Layer 1 aspect-fit, no-crop, center, resize, or temporary
`#F3F0EA` OutputCanvas background semantics.

## Frozen visual baseline

The single renderer-owned analytic D3D11 shadow pass uses card-area coverage
to derive a clamped continuous strength factor:

- coverage at or below 30%: opacity 5%, Y offset 5 px, softness 42 px;
- coverage at or above 75%: opacity 14%, Y offset 14 px, softness 34 px;
- between 30% and 75%: the same smoothstep factor continuously interpolates
  opacity and Y offset upward and softness downward;
- pixel dimensions use 1920x1080 as the reference canvas and scale by
  `min(OutputWidth / 1920, OutputHeight / 1080)`.

The Content Card and Shadow share one rounded-rectangle signed-distance
silhouette. Its restrained reference radius is 8 px at 1920x1080 and uses the
same OutputCanvas scale. The captured content remains on a single source
texture sample path; only the corner mask is composed over the flat stage.

The black-corner-spike diagnosis was **BOTH**: captured source corner pixels
remained visible through the former square content pass, while the former
axis-aligned rectangular Shadow silhouette could also remain visible outside a
rounded content corner. The shared silhouette removes both contributors
without color-keying, black-edge detection, CPU readback, or Capture changes.

## Automated evidence

- Release x64 solution build: PASS. A corrupt regenerable Native LTCG
  `.ipdb/.iobj` cache was removed only from
  `artifacts/obj/XbPreview.Native/Release/x64`; the successful link explicitly
  performed full LTCG compilation of 6,720 functions.
- `XbPreview.FlatStage.Tests.exe --card-shadow`: PASS for the frozen Shadow
  endpoints, smooth coverage response, shared 8 px rounded silhouette,
  square-corner-spike exclusion, continuous corner arc, resize continuity,
  unclipped support, production HLSL compilation, and a source-independent
  Shadow shader.
- `XbPreview.FlatStage.Tests.exe`: PASS for the frozen Layer 1 `#F3F0EA`
  background, centered aspect-fit, full-source no-crop, safe margins, and
  resize refit.
- Protected-module audit against the Layer 1 frozen ancestor: zero changes to
  Window Capture/CreateForWindow, Audio, Encoder, RenderFrameTap,
  RecordingController, Safe Publish, Recovery/Storage Safety, Director Lite,
  Content Camera, Camera Ownership, and `WindowStageComposer`.
- `git diff --check`: PASS before freeze.

No build, automated Gate, or smoke was repeated during the mechanical freeze
round; this report preserves the already accepted evidence.

## Human acceptance

Human visual smoke: **WINDOW-STAGE-LAYER2-HUMAN-PASS**.

Xiaobai's final decision was: "好了。就这样吧。"

The accepted real recording established that:

- the small Card shadow is materially lighter and no longer reads as a
  grey-black outline;
- all four black corner spikes are gone and the rounded transition is
  acceptable;
- the large Card retains the desired restrained floating weight;
- an approximately 27-second real recording was produced and opened and
  played normally;
- no further Shadow tuning is wanted for this baseline.

## Included scope

- Window Card analytic soft shadow;
- size-aware opacity, Y offset, and softness;
- shared rounded Content Card/Shadow silhouette and black-spike fix;
- one focused native Card/Shadow Gate and its project linkage;
- this recovery/freeze report.

## Explicitly absent

Layer 3 has not been entered. This freeze contains no 2.5D, Rotation X/Y,
perspective, tilt levels, Showcase, Showcase Motion, Enter/Hold/Return,
background presets, visual background redesign, UI, state machine, animation
system, second Shadow renderer, new Capture engine, or recording-lifecycle
ownership.

No push is part of this freeze.

## Final freeze verdict

`WINDOW-STAGE-LAYER2-FROZEN`
