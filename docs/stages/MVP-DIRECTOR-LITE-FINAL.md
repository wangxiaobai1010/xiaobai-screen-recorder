# MVP Director Lite Final

## 1. MVP RANGE

The frozen range has a default and one closed optional emphasis level:

```text
Wide 1.0x
-> click
-> Focus Soft 1.6x (default) or Strong 2.0x (optional)
-> Follow
-> focused click retarget at the selected Focus strength
-> 4 seconds of inactivity
-> Return Wide 1.0x
```

Strong is a user-selectable emphasis level, not an automatic semantic decision.
No post-Gate camera-feel change was made.

## 2. CAMERA OWNERSHIP

`CameraOwner` has one active value: `Manual` or `DirectorLite`. Director ON
rejects manual 1.6x, manual 2.0x, F9, and F10 at their shared controller
boundary. Director OFF and Preview Stop clear Director focus/timer state,
return ownership to Manual, and restore both manual presets.

Focus strength may be selected only while ownership is Manual. Director enable
locks the selected strength for that session; there is no runtime strength
switch or temporary manual takeover.

## 3. REUSED MOTION BASE

Director Lite supplies policy and targets only. Zoom and Fixed Center continue
through the accepted P2.4.1 omega-14 critical-damped controller. Pointer Follow
continues through the existing omega-13 `CameraUpdateService` and
`ComfortZoneTracker`. There is no second easing, spring, Follow, or camera
submission pipeline.

## 4. INPUT OBSERVATION

Mouse observation uses Raw Input with `RIDEV_INPUTSINK`. It does not use a
low-level hook and never swallows, mutates, redirects, synthesizes, or simulates
user input. Enable registers once; disable, Stop, and Close unregister. Close
also unsubscribes the event and disposes the observer.

## 5. AUTOMATED EVIDENCE

- Release x64 solution build: PASS.
- Director deterministic tests: PASS.
- P2.4.1 camera-motion tests: PASS.
- Director Runner static test: PASS.
- Director Focus Strength A/B Runner static test: PASS.
- `git diff --check`: PASS.
- Sensitive-subsystem diff check: no Capture, Encoder, Audio,
  `RecordingController`, or Safe Publish semantic changes.

The implementation candidate is
`3d966284a6b0942a68c9f8c97bf7b1aa6a381820`. Its Gate Host binary was built
after the final candidate Host source modification and before the candidate
commit, then run from clean `main`. Session camera logs record Director focus,
1.6x retarget, inactivity-Wide, and disable events.

The Focus Strength implementation candidate is
`9d76ed0e6d02271ced6479fa2fb48b6661adf5a8`. Deterministic coverage passes for
default Soft 1.6x, optional Strong 2.0x, enabled-session locking, Manual/F9/F10
mutex, Soft/Strong retarget, Strong four-corner clamp, Strong Follow crop
bounds, 4-second inactivity, Disable, Stop, and observer disposal.

## 6. HUMAN EVIDENCE

The completed real-user Gate covered left click push-in, right click retarget,
normal pointer Follow, inactivity Return Wide, Stop/Finalize/Publish, and
normal Close. Two complete `HUMAN CHECK MP4` Runner paths were reported. The
user's final conclusion was “正常的”. Close diagnostics record successful
single-invocation cleanup and `MainForm.FormClosed`, accepting the observer
lifecycle. The 4-second inactivity value remains frozen.

The completed Focus Strength A/B added one Soft 1.6x session and one Strong
2.0x session, each through the full Interactive Runner path with a published
`HUMAN CHECK` MP4. Camera logs confirm maximum zoom 1.6000 and 2.0000 plus
retarget and inactivity-Wide events; both Close paths completed cleanup once.
The user's final conclusion was “没什么大毛病，可以”, so both strengths PASS.

## 7. DEFERRED DIRECTOR FEATURES

Deferred beyond Director Lite: AI/semantic importance, stable
text-input handling, complex cross-region reframing, advanced anti-frequent-
zoom rules, Camera Timeline, post-recording camera edits, and temporary manual
takeover inside Director.

Window capture plus clean background remains in MVP after Director Lite and the
minimal product shell. Custom-region capture does not enter MVP.

## 8. FINAL VERDICT

`DIRECTOR-LITE-FINAL-PASS`

`DIRECTOR-FOCUS-STRENGTH-FINAL-PASS`

Manual and DirectorLite ownership are strictly mutually exclusive. This is an
MVP Director Lite verdict, not `P4-FULL-DIRECTOR-PASS`. Director Lite is frozen.
Soft 1.6x is the default and Strong 2.0x is the optional emphasis level. The
original tag `mvp-director-lite-pass-2026-08-09` remains the initial 1.6x
baseline. The next and only MVP mainline is the minimal screen-recording
product shell.
