# Window Stage 2.5D - Three-Direction Nine-Pose Human Freeze

Status: **WINDOW-STAGE-25D-9POSE-HUMAN-FROZEN**

Freeze date: 2026-08-14

Branch: `feature/window-stage-left-front-motion`

Mechanical-freeze parent:
`e54b76b975cd2aefbcc5da0f32ad91fb415cb0a8`
(`window-stage-25d-shadow-bounds-pass-2026-08-14`).

This freeze records already-completed human dynamic acceptance for the full
Window Stage 2.5D direction/strength matrix. It does not retune a pose, change
Motion, change Shadow, or add product UI. The only runtime wiring in this
freeze is test-only environment-controlled selection and synchronization used
by the comparison harness.

## Frozen pose matrix

The accepted product matrix is:

- `RIGHT x LEVEL_1 / LEVEL_2 / LEVEL_3`;
- `LEFT x LEVEL_1 / LEVEL_2 / LEVEL_3`;
- `FRONT x LEVEL_1 / LEVEL_2 / LEVEL_3`.

The production pose table remains exactly the Layer 3 frozen table. RIGHT is
the historical side-view baseline. LEFT remains its exact geometric mirror:
Scale, Y, Rotation X, and Perspective retain the matching RIGHT level's value,
while X and Rotation Y change sign. FRONT remains centered with X = 0 and
Rotation Y = 0; its three strengths express only the symmetric backward-tilt
progression.

## Frozen Motion behavior

The production Motion implementation remains exactly the Layer 4 baseline:

- 360 ms Enter;
- quintic smootherstep easing;
- persistent `STAY` with no automatic Return;
- explicit 380 ms Return;
- no spring, overshoot, breathing, or idle motion.

The comparison-only Enter event delays the already-frozen controller start
until the recording harness signals it. When the test-only event is absent,
the existing production behavior is unchanged.

## Existing dynamic evidence

The following already-generated product recordings are preserved as the
LEFT and FRONT three-level dynamic evidence:

| Direction | Strength | Artifact | Existing strict decode |
|---|---|---|---|
| LEFT | LEVEL_1 | `artifacts/window-stage-left-3level/01_LEFT_LEVEL1.mp4` | PASS |
| LEFT | LEVEL_2 | `artifacts/window-stage-left-3level/02_LEFT_LEVEL2.mp4` | PASS |
| LEFT | LEVEL_3 | `artifacts/window-stage-left-3level/03_LEFT_LEVEL3.mp4` | PASS |
| FRONT | LEVEL_1 | `artifacts/window-stage-front-3level/01_FRONT_LEVEL1.mp4` | PASS |
| FRONT | LEVEL_2 | `artifacts/window-stage-front-3level/02_FRONT_LEVEL2.mp4` | PASS |
| FRONT | LEVEL_3 | `artifacts/window-stage-front-3level/03_FRONT_LEVEL3.mp4` | PASS |

These recordings and their strict decode checks were completed before this
mechanical freeze. They were not regenerated or decoded again in this round.

RIGHT remains the long-running primary dynamic baseline accepted by the
Layer 4 Persistent Motion human smoke.

## Human acceptance

Human dynamic verdict:
**WINDOW-STAGE-25D-3DIRECTION-9POSE-HUMAN-PASS**.

Xiaobai explicitly confirmed:

- RIGHT: all three strengths remain the established dynamic baseline;
- LEFT: all three strengths tilt in the correct direction, read as RIGHT's
  reverse mirror, and have acceptable slope;
- FRONT: all three strengths have the correct front trapezoid with no
  left/right skew; LEVEL_2 is intentionally lighter; LEVEL_3 has sufficient
  backward-fall presence; the full FRONT direction is accepted.

## Shadow presence audit

The latest read-only audit verdict is:
`SHADOW-PRESENT-BUT-TOO-SUBTLE`.

Its unique root-cause classification is comparison-target background/material
visual masking. Shadow is submitted through the same product draw path to the
same final OutputCanvas and MP4 for RIGHT, LEFT, and FRONT. There is no
direction-specific omission, clipping failure, or comparison-renderer branch.

At the common four-second `STAY` sample, the measured mean-luminance drops in
the lower shadow band were:

- RIGHT: approximately 3.22%;
- LEFT: approximately 4.67%;
- FRONT: approximately 4.63%.

LEFT and FRONT are therefore not weaker than RIGHT. Shadow retuning remains
deferred until the formal background/visual stage; no dark-target-specific
Shadow change is part of this freeze.

## Included change scope

- test-only Motion target direction/strength selection;
- test-only controlled Enter synchronization for deterministic comparison
  recording;
- the focused LEFT/FRONT three-level Motion gate;
- the existing Window Target comparison/recording harness extensions;
- this human-validation freeze report.

## Frozen production-core audit

Against parent `e54b76b975cd2aefbcc5da0f32ad91fb415cb0a8`:

- production 2.5D pose table: diff 0;
- production Motion controller/timing/easing: diff 0;
- Shadow parameters: diff 0;
- Shadow Bounds/projection rules: diff 0;
- Window Capture/CreateForWindow production source: diff 0;
- Audio production source: diff 0;
- Encoder and RecordingController production source: diff 0;
- RenderFrameTap, Safe Publish, Recovery/Storage Safety, Director Lite, and
  Content Camera production source: diff 0.

The changed `PreviewRenderer` code is reachable only through explicit
`XB_PREVIEW_TEST_*` environment variables and does not add a product-facing
selector, UI, or ABI.

## Mechanical-freeze constraints

No pose tuning, Shadow tuning, Motion tuning, real MP4 rerun, nine-pose Gate,
Build, LongRun, AA work, MP4 selector work, screenshot-bug work, or Punch-in
work was performed during this round. No reset, clean, stash, push, tag
overwrite, or history rewrite is part of this freeze.

## Next stage

`Manual Zoom Punch-in`.

## Final freeze verdict

`WINDOW-STAGE-25D-9POSE-HUMAN-FROZEN`
