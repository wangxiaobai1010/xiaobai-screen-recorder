# Window Stage Layer 3 - 2.5D Static Poses Freeze

Status: **WINDOW-STAGE-LAYER3-25D-FROZEN**

Freeze date: 2026-08-13

Layer 2 frozen parent:
`9ef74f96782b1ebf5e750269aca93bdfd876c19e`
(`window-stage-layer2-pass-2026-08-13`).

This freeze records the accepted static Window Stage 2.5D placement layer.
Layer 3 owns only how the already-composed Window Card is placed. It does not
change Content Camera framing, Layer 1 layout, or the Layer 2 rounded-card and
shadow parameters.

## Frozen static-pose model

The internal model exposes three directions (`LEFT`, `FRONT`, `RIGHT`) and
three strengths (`LEVEL_1`, `LEVEL_2`, `LEVEL_3`). An exact Identity fallback
continues to use the frozen Layer 2 rendering path.

The historical human-tuned side baseline is `RIGHT` in the current coordinate
system. `LEFT` is its geometric mirror: horizontal placement and Rotation Y
change sign, while Scale, vertical placement, Rotation X, and Perspective keep
the matching level's magnitude. There is no separately hand-tuned LEFT table.

The formally frozen parameters are:

| Direction | Strength | Scale | X | Y | Rotation X | Rotation Y | Perspective |
|---|---|---:|---:|---:|---:|---:|---:|
| RIGHT | LEVEL_1 | 0.88 | +0.025 | -0.018 | -6 deg | +18 deg | 0.90 |
| RIGHT | LEVEL_2 | 0.83 | +0.040 | -0.022 | -8 deg | +24 deg | 1.00 |
| RIGHT | LEVEL_3 | 0.77 | +0.060 | -0.028 | -10 deg | +30 deg | 1.10 |
| LEFT | LEVEL_1 | 0.88 | -0.025 | -0.018 | -6 deg | -18 deg | 0.90 |
| LEFT | LEVEL_2 | 0.83 | -0.040 | -0.022 | -8 deg | -24 deg | 1.00 |
| LEFT | LEVEL_3 | 0.77 | -0.060 | -0.028 | -10 deg | -30 deg | 1.10 |
| FRONT | LEVEL_1 | 0.94 | 0 | -0.008 | -3 deg | 0 deg | 0.70 |
| FRONT | LEVEL_2 | 0.90 | 0 | -0.012 | -5 deg | 0 deg | 0.85 |
| FRONT | LEVEL_3 | 0.86 | 0 | -0.016 | -7 deg | 0 deg | 1.00 |

FRONT is a horizontally symmetric trapezoid with no one-sided yaw. Its three
values began as restrained candidates and are now part of the frozen Layer 3
baseline after human acceptance of all three strengths.

## Frozen rendering behavior

- the Stage Transform is applied after the frozen Window Card layout;
- non-Identity poses use a homogeneous six-vertex D3D11 quad;
- texture coordinates use perspective-correct interpolation;
- the rounded Content Card silhouette follows the Stage Transform;
- the existing Layer 2 shadow support is projected by the same transform;
- Identity uses the unchanged Layer 2 card and shadow draw path;
- the smoke selector remains test-only and does not add product UI or ABI.

## Automated evidence

- Release x64 solution build: PASS;
- the regenerable corrupt
  `artifacts/obj/XbPreview.Native/Release/x64/XbPreview.Native.ipdb` was the
  only cache file removed during Layer 3 build-cache recovery;
- the corresponding `XbPreview.Native.iobj` was preserved;
- the successful linker fell back to full compilation and produced a new
  `XbPreview.Native.dll`;
- frozen DLL SHA-256:
  `05E0F6B45AD8CF6AE96B00053C22D98544741D50994D1BF349229EC082A7BB03`;
- `XbPreview.FlatStage.Tests.exe --stage-transform`: PASS for the exact
  nine-pose table, recovered RIGHT geometry, mirrored LEFT, symmetric FRONT,
  monotonic strength, safe content/shadow bounds, and production shaders;
- `XbPreview.FlatStage.Tests.exe --layer2-identity`: PASS for exact flat
  layout, full UV, frozen rounded-card/shadow fields, card/support corners,
  and homogeneous W=1;
- protected-module audit against the Layer 2 frozen parent: PASS;
- Layer 1 `WindowStageComposer.h` blob remained
  `24146c018a12c199a20c3d207011f0c900ba2f08`;
- Layer 2 `WindowCardShadowPass.h` blob remained
  `b5928eda4098cf1c1922165d4042365610de47fb`;
- `git diff --check`: PASS before freeze.

No product test, automated Gate, ancestor Gate, Release build, or human smoke
was repeated during this mechanical freeze round. This report preserves the
already accepted evidence.

## Human acceptance

Human visual smoke: **WINDOW-STAGE-LAYER3-25D-HUMAN-PASS**.

Xiaobai personally reviewed all nine static poses:

- LEFT LEVEL_1 / LEVEL_2 / LEVEL_3: PASS;
- FRONT LEVEL_1 / LEVEL_2 / LEVEL_3: PASS;
- RIGHT LEVEL_1 / LEVEL_2 / LEVEL_3: PASS.

The final pose was viewed live and explicitly accepted even though no
screenshot was retained. A screenshot is not a freeze requirement, and the
nine-pose smoke was not repeated.

## Included scope

- `StageDirection` and `StageStrength`;
- `WindowStageTransform` and exact Identity fallback;
- the mirrored LEFT/RIGHT relationship;
- the symmetric FRONT trapezoid definition;
- the frozen nine-pose parameter table;
- homogeneous quad projection and perspective-correct UV;
- transformed rounded Content Card and shared transformed Shadow;
- the nine-pose test-only smoke tool;
- the focused StageTransform and Layer 2 Identity Gates;
- this recovery/freeze report.

## Explicitly absent

Layer 4 has not been entered. This freeze contains no Showcase Motion,
Enter/Hold/Return, spring, easing, animation timeline, keyframe, or automatic
motion.

Layer 5 has not been entered. This freeze contains no Background Preset,
animated color treatment, Warm/Dark background, or visual background redesign.

There is no formal direction/strength UI, RecordingStyle setting, Capture
change, Camera ownership change, or recording-lifecycle change. No push is
part of this freeze.

## Final freeze verdict

`WINDOW-STAGE-LAYER3-25D-FROZEN`
