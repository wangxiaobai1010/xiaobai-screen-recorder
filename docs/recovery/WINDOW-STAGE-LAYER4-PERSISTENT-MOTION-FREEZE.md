# Window Stage Layer 4 - Persistent Showcase Motion Freeze

Status: **WINDOW-STAGE-LAYER4-PERSISTENT-MOTION-FROZEN**

Freeze date: 2026-08-14

Layer 3 frozen parent:
`c015e985e4149c6f8ab9a8e3efa46dd021190f29`
(`window-stage-layer3-25d-pass-2026-08-13`).

This freeze records the accepted Window Stage transition layer. Layer 4 owns
only how the already-frozen Stage Transform moves from its current value to a
requested frozen pose and back to Identity. It does not redefine any Layer 3
pose, Layer 2 card/shadow property, Layer 1 layout, Content Camera behavior, or
recording lifecycle.

## Exploration and final selection

The first human rhythm exploration presented A, B, and C using the same
frozen `RIGHT x LEVEL_2` target and the same Identity-to-target-to-Identity
path. Only timing and deterministic easing differed. Xiaobai selected Motion A
for its motion feel.

The early A/B/C experiment used `Enter -> Hold -> Auto Return`; Motion A's
historical Hold value was 900 ms. Human selection did not freeze that demo
lifecycle. The automatic Hold-to-Return transition was subsequently removed.

The accepted product semantics are:

- `IDLE`: exact Identity;
- `TRANSITION`: current transform to the requested frozen target;
- `STAY`: exact target for an unlimited duration;
- `RETURN`: current transform to exact Identity after an explicit request;
- completion of Return: `IDLE` and exact Identity.

**NO AUTO RETURN.** A target pose remains active until a new explicit state
request is received.

## Frozen motion parameters

- selected preset: Motion A;
- enter duration: 360 ms;
- enter easing: quintic smootherstep;
- target behavior: persistent `STAY`, indefinitely;
- explicit Return duration: 380 ms;
- Return easing: quintic smootherstep;
- overshoot, spring, bounce, elastic, physics, breathing, and idle motion:
  absent.

The 900 ms Hold value remains only as historical A/B/C exploration metadata.
It does not drive the persistent Controller state machine.

## Frozen continuity and ownership

`WindowShowcaseMotionController` is header-only and pure CPU. It owns elapsed
time, timing/easing selection, state, and current/start/target
`WindowStageTransformParameters`. It owns no D3D11 device, GPU resource,
Capture object, Audio object, Encoder, file, or recording lifecycle.

Every new Transition or Return first samples the actual current transform and
uses that value as the next segment origin. This freezes continuity for:

- explicit Return during a Transition;
- explicit Return while staying at the exact target;
- future retargeting from the current transform without an Identity jump.

The accepted smoke target is resolved directly through
`ResolveWindowStageTransform(WindowStageDirection::Right,
WindowStageStrength::Level2)`. Layer 4 contains no duplicate Layer 3 pose
numbers. Content, rounded corners, and the existing size-aware shadow continue
to consume the same current Stage Transform as one Window Card object.

## Automated evidence preserved for this freeze

- Release x64 solution build: PASS;
- regenerated `XbPreview.Native.dll`: PASS;
- frozen DLL SHA-256:
  `35E9BEEC082ED1AB1EDB82B3CFBE9B756717CE8320AFC420C82608EF3615B41D`;
- Persistent Motion Gate: PASS for exact
  `IDLE/TRANSITION/STAY/RETURN` endpoints, Motion A's 360 ms Enter,
  indefinitely persistent Stay, no automatic Return, explicit 380 ms Return,
  no overshoot, and continuous Return from mid-Transition and Stay;
- Layer 3 minimal regression: PASS for exact Identity and the directly
  resolved frozen `RIGHT x LEVEL_2` pose;
- protected-module diff audit: NONE;
- `git diff --check`: PASS before freeze.

No Release build, automated Gate, ancestor Gate, or human smoke was repeated
during this mechanical freeze round. This report preserves the already
accepted evidence.

## Human acceptance

Human Persistent Motion smoke:
**WINDOW-STAGE-LAYER4-PERSISTENT-MOTION-HUMAN-PASS**.

Xiaobai explicitly reported **PASS** after personally verifying:

1. exact Identity / front presentation;
2. Motion A transitioning smoothly to frozen `RIGHT x LEVEL_2`;
3. the target pose staying active indefinitely;
4. no automatic recovery to Identity after waiting;
5. PowerShell accepting the explicit `RETURN` request;
6. the approximately 380 ms smooth Return to exact Identity;
7. the overall persistent behavior operating normally.

The test-only smoke remains the human observation entry. It starts only Motion
A, waits indefinitely at the frozen target, and signals Return only after the
human explicitly enters `RETURN`.

## Included scope

- `WindowShowcaseMotionController`;
- `IDLE / TRANSITION / STAY / RETURN` state model;
- Motion A 360 ms quintic-smootherstep Enter;
- persistent unlimited Stay;
- explicit 380 ms quintic-smootherstep Return;
- current-transform continuity;
- direct consumption of frozen Layer 3 poses;
- focused Persistent Motion and Layer 3 minimal Gates;
- test-only Persistent Motion human smoke;
- this recovery/freeze report.

## Explicitly absent and deferred

- Manual Zoom Punch-in and 1.6x / 2.0x Stage approach behavior have not been
  entered;
- 2.5D edge antialiasing, slanted-edge jaggies, and rounded-corner smoothing
  have not been addressed;
- the recurring `XbPreview.Native.ipdb` corruption root cause has not been
  audited;
- Background Preset, animated color treatment, and Layer 5 have not been
  entered;
- formal Motion UI, automatic recording trigger, mouse trigger, Director
  trigger, Timeline, and Keyframe Editor are absent;
- no Window Capture, Audio, Encoder, Safe Publish, Recovery, Content Camera,
  or Camera Ownership behavior is changed;
- no push is part of this freeze.

## Recorded follow-up order (not started)

1. independently audit the recurring `XbPreview.Native.ipdb` corruption;
2. address 2.5D slanted-edge and rounded-corner antialiasing;
3. explore Manual Zoom Punch-in approach-feel A/B/C.

## Final freeze verdict

`WINDOW-STAGE-LAYER4-PERSISTENT-MOTION-FROZEN`
