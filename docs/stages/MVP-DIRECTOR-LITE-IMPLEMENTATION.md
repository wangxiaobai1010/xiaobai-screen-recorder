# MVP Director Lite Implementation

## 1. REUSED CAMERA ASSETS

Director Lite is a managed camera-policy addition. It reuses:

- `FixedTargetCameraController` for target changes, velocity continuity, and the
  accepted critical-damping motion with Zoom/Fixed Center omega 14.
- `CameraUpdateService` and `ComfortZoneTracker` for the existing Follow path
  with omega 13. There is no Director-specific Follow implementation.
- `CameraCursorTarget` for physical desktop cursor observation and normalized
  primary-display coordinates.
- `CameraMath.ClampView` for legal crop-center bounds and edge clamping.
- the existing `CameraState` native submission path, OutputCanvas renderer,
  WGC capture, frame tap, encoder, recording, and audio paths without ABI or
  native changes.

## 2. MATURE-WORLD AUDIT

Project inspection found the thinnest camera entry at the managed
`FixedTargetCameraController`, above rendering/capture/encoding. The frozen
P2.4.1 report already records a read-only Cap comparison and deliberately
keeps only the general velocity-continuity principle; no Cap AGPL source or
timeline/director structure is copied. Cap's current public repository still
separates capture/render/editor concerns, which supports keeping Director
policy outside the media engine: <https://github.com/CapSoftware/Cap>.

Windows offers two plausible global observation routes:

1. Primary: Raw Input registered for mouse usage with `RIDEV_INPUTSINK`.
   Microsoft documents background delivery through `WM_INPUT`, explicit
   registration/removal, and mouse button transitions including
   `RI_MOUSE_LEFT_BUTTON_DOWN`:
   <https://learn.microsoft.com/windows/win32/inputdev/about-raw-input>,
   <https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-registerrawinputdevices>,
   <https://learn.microsoft.com/windows/win32/api/winuser/ns-winuser-rawmouse>.
2. Fallback only: `WH_MOUSE_LL`. Microsoft requires a message loop, warns that
   slow callbacks can be silently removed, and recommends Raw Input for most
   monitoring cases:
   <https://learn.microsoft.com/windows/win32/winmsg/lowlevelmouseproc>.

The implementation selects Raw Input. No low-level hook was added. OBS was
useful only as an architecture check: mature capture systems keep input/source
policy separate from the core compositing/output API; no OBS code was copied:
<https://github.com/obsproject/obs-studio>.

Audit answers:

- A: `FixedTargetCameraController` is the thin target/zoom entry; existing
  Snapshot/CameraUpdateService and ComfortZoneTracker remain the motion path.
- B: desktop physical pixels use the existing primary-display normalization;
  full-screen capture has identity OutputCanvas, then `ClampView` establishes
  the legal crop center.
- C: observe `WM_INPUT` using `RIDEV_INPUTSINK`, never setting `RIDEV_NOLEGACY`
  or `RIDEV_CAPTUREMOUSE`, and never returning a suppression decision.
- D: existing cursor observation is enough for coordinates and Follow but not
  for global button edges; the thin Raw Input adapter supplies only that edge.
- E: ownership belongs in the managed camera controller, not renderer, WGC,
  encoder, recording, or audio.

## 3. CAMERA OWNERSHIP

`CameraOwner` contains exactly `Manual` and `DirectorLite`. Startup defaults to
Manual. Every manual button and F9/F10 reaches the existing `Execute` method,
which rejects the command before reading a cursor target when DirectorLite
owns the camera. Enabling or disabling Director clears Director pending state,
sets a smooth Wide target through the existing spring, and changes the single
owner. Preview exit resets owner to Manual and Wide.

## 4. CLICK OBSERVATION

`RawMouseInputObserver` registers mouse Raw Input only while Director Lite is
enabled. `MainForm.WndProc` lets normal window processing continue; the adapter
does not swallow, rewrite, synthesize, or redirect input. `RIDEV_INPUTSINK`
keeps observation active while Preview is minimized, hidden, or not foreground.
Disable, Stop, and Close unregister the device; registration/read failures
retain the Win32 error for diagnostics.

## 5. COORDINATE MAPPING

On `RI_MOUSE_LEFT_BUTTON_DOWN`, the application calls the established
`CameraCursorTarget.ReadPrimaryMonitorTarget`. It maps the cursor from desktop
physical pixels relative to the primary full-screen capture into normalized
camera coordinates. Director passes that point to the existing controller.
`CameraMath.ClampView(1.6, x, y)` clamps the applied center so the crop never
leaves OutputCanvas. Tests cover center, both opposing edges, and existing four
corner UV bounds. Director Lite is intentionally rejected for custom-region
Preview in this slice; window capture is not implemented.

## 6. DIRECTOR STATE

The state is limited to `Wide` and `Focused`. A Wide click targets 1.6x at the
click. A Focused click replaces the target while retaining 1.6x and inherited
position/velocity, so it neither snaps nor returns to 1.0x first. The automatic
path has no 2.0x command, timeline, semantic model, or additional easing.

## 7. INACTIVITY RETURN

`CameraSettings.DirectorLiteInactivitySeconds` is the single A/B adjustment
point and defaults to 4.0 seconds. The initial value is deliberately long
enough for a user to click and briefly read/point without immediate pull-back,
yet short enough to observe repeatedly in the 45–60 second human gate. It is a
first experiential baseline, not an AI-derived score. Every meaningful Raw
Input move/button/wheel and every click refreshes the QPC activity timestamp.
Snapshot detects timeout and sends the existing smooth Wide 1.0x target.

The completed human Gate accepted this timing. The frozen MVP value remains
4.0 seconds; no post-Gate camera-feel adjustment was made.

## 8. MANUAL MUTEX

Director ON rejects manual 1.6x, manual 2.0x, and therefore F9/F10 dispatch at
the common controller boundary. Director OFF restores both presets without
changing hotkey registration preferences. Owner transitions and Preview exit
clear Director focus and inactivity state. There is no temporary manual
takeover.

## 9. TESTS

PASS evidence:

```text
MSBuild XbPreview.P1D-A1.sln /m /t:Build /p:Configuration=Release /p:Platform=x64
Release x64 Build PASS

dotnet artifacts\bin\Release\x64\XbPreview.Managed.Tests.dll --director-lite
XbPreview.Managed.Tests PASS: MVP Director Lite

dotnet artifacts\bin\Release\x64\XbPreview.Managed.Tests.dll --camera-motion
XbPreview.Managed.Tests PASS: P2.4.1 camera motion

powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools\director-lite\Test-DirectorLite-RunnerStatic.ps1
Director Lite runner static PASS

git diff --check
PASS
```

The Director suite deterministically covers default/unique/restored ownership,
click focus, click coordinates, edge clamp, focused retarget without Wide,
activity refresh, inactivity Wide, manual preset mutex/recovery, target/timer
cleanup, Raw Input single registration, disabled processing, and Stop/Close
disposal. The original camera-motion suite covers the frozen manual camera and
Follow motion without regression.

The broad no-argument managed historical entry was also attempted. It stops at
the pre-existing assertion `P1c.2 frozen manifest exists` because that manifest
is absent from this repository baseline. This is outside the authorized slice;
the requested focused suites and Release x64 solution build pass.

Final independent revalidation on 2026-08-09 repeated the Release x64 build,
the Director deterministic suite, the P2.4.1 camera-motion suite, the runner
static check, and `git diff --check`; all passed.

## 10. CANDIDATE

The immutable candidate is the clean `main` commit with subject:

```text
feat(camera): add director lite click focus
```

The exact implementation candidate is
`3d966284a6b0942a68c9f8c97bf7b1aa6a381820`. Baseline was
`79cf4fd4d68ed3e767a5df7289726fd5636c4d1f` at tag
`mvp0-audio-core-pass-2026-08-09`.

The Gate Host binary was built at 17:39:54 +08:00, after the latest modified
candidate Host source at 17:38:07 and before the immutable candidate commit at
17:41:53. The Gate then ran from clean `main`. Camera logs from the resulting
sessions contain Director enter, 1.6x retarget, inactivity-Wide, and disable
events. This source/build/log ordering rules out a stale pre-Director binary
or a different dirty candidate.

## 11. INTERACTIVE GATE

Run exactly one command from an Interactive Administrator PowerShell:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "E:\小白录屏器\xiaobai-screen-recorder\tools\director-lite\Run-DirectorLite-InteractiveGate.ps1"
```

The 45–60 second runner checks the clean main candidate, launches the normal
full-screen Preview with Director Lite enabled, retains the passed default
system/microphone path, presents the frozen Chinese prompts, and prints
`HUMAN CHECK MP4` plus camera/follow diagnostics. The user starts/stops through
the existing RecordingController UI, so Finalize/Publish remains the formal
path. Normal application close verifies observer cleanup.

Human acceptance is limited to push-in naturalness/accuracy, left-right
retarget naturalness, unchanged Follow feel, return-Wide timing, jitter/snap,
and manual restoration. A/B may change only the inactivity constant.

The human Gate is complete and is not to be repeated. The user completed the
normal Start/Stop/Finalize/Publish Runner path, observed left click push-in,
right click retarget, normal pointer Follow, inactivity return to Wide, Stop,
and normal application close, and concluded “正常的”. Two `HUMAN CHECK MP4`
runner results were reported. The close diagnostics record one cleanup
invocation, successful cleanup, and `MainForm.FormClosed`; observer disable,
unregister, event unsubscribe, and disposal are therefore accepted for the
MVP lifecycle.

## 12. DEFERRED DIRECTOR FEATURES

At the tagged 1.6x FINAL baseline, deferred Director features were:
AI/semantic importance, automatic 2.0x, stable
text-input handling, complex cross-region reframing, advanced anti-frequent-
zoom rules, Camera Timeline, post-recording camera edits, and temporary manual
takeover inside Director.

Section 14 introduces only the closed 1.6x/2.0x strength choice as a later
Gate-ready extension; it does not rewrite the historical FINAL verdict.

Window capture plus clean background remains in MVP, but follows Director Lite
and the minimal product shell. Custom-region capture is outside MVP.

## 13. VERDICT

`DIRECTOR-LITE-FINAL-PASS`

The frozen MVP range is Wide 1.0x -> click -> Focus 1.6x -> existing Follow ->
smooth 1.6x retarget -> 4-second inactivity -> existing smooth Return Wide.
Manual and DirectorLite camera ownership are strictly mutually exclusive.
This verdict is deliberately not `P4-FULL-DIRECTOR-PASS`.

Director Lite construction stops here. The next and only MVP mainline is the
minimal screen-recording product shell.

## 14. DIRECTOR FOCUS STRENGTH

### Product definition

The Director Lite focus target is now a closed semantic choice:

- `Soft` maps to the existing Standard preset at 1.6x and remains the default.
- `Strong` maps to the existing Strong preset at 2.0x.

This is not an arbitrary zoom input. The later MVP product shell should expose
the user language “放大程度：柔和 1.6x / 强调 2.0x”, not the engineering names
`CameraOwner` or `DirectorFocusStrength`.

### Reuse and control interface

`FixedTargetCameraController.SetDirectorFocusStrength` is the single internal
control entry. It accepts only the `Soft`/`Strong` enum while ownership is
Manual. Once Director Lite is enabled, the setter explicitly rejects changes
and retains the selected preset until Director is disabled. There is no
in-session manual takeover or delayed hot switch.

Both values use the existing `CameraPreset` definitions, omega-14 Zoom/Fixed
Center spring, camera clamp, retarget path, omega-13 Follow path, 4-second
inactivity Return Wide, and native camera submission pipeline. No Director-
specific 2.0x motion, easing, spring, Follow, owner, state machine, or camera
pipeline was added. Soft still enters and retargets through the same Standard
preset as the final baseline.

### Ownership and behavior

Camera ownership remains exactly `Manual` or `DirectorLite`. While Director
owns the camera, manual 1.6x, manual 2.0x, F9, and F10 remain rejected at the
shared controller boundary. Disable returns Wide, clears focus/activity state,
restores Manual ownership, and permits selection for the next Director session.

Soft behavior remains Wide 1.0x -> click -> Focus 1.6x -> Follow -> 1.6x
retarget -> 4-second inactivity -> Wide 1.0x. Strong uses the identical path at
2.0x. A focused click replaces only the target and retains the selected zoom;
it does not return to Wide before retargeting.

### Automated evidence

PASS evidence for this candidate:

```text
MSBuild XbPreview.P1D-A1.sln /m /t:Build /p:Configuration=Release /p:Platform=x64
Release x64 Build PASS

dotnet artifacts\bin\Release\x64\XbPreview.Managed.Tests.dll --director-lite
XbPreview.Managed.Tests PASS: MVP Director Lite

dotnet artifacts\bin\Release\x64\XbPreview.Managed.Tests.dll --camera-motion
XbPreview.Managed.Tests PASS: P2.4.1 camera motion

powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools\director-lite\Test-DirectorFocusStrength-RunnerStatic.ps1
Director Focus Strength A/B runner static PASS

powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools\director-lite\Test-DirectorLite-RunnerStatic.ps1
Director Lite runner static PASS
```

The focused suite verifies default Soft, Soft 1.6x, Strong 2.0x, enabled-session
setter rejection, post-disable reselection, Manual/F9/F10 mutex, Soft and Strong
retarget without Wide, exact 4-second inactivity, disable/Stop state cleanup,
and Raw Input dispose/unregister behavior. Strong additionally covers center,
all four corner clamps, left-to-right cross-region retarget, repeated existing
Follow updates within legal 2.0x crop-center bounds, and Return Wide. The
P2.4.1 suite preserves the frozen motion and Follow foundation.

### Candidate and A/B Runner

The immutable candidate subject is:

```text
feat(camera): add director focus strength
```

The immutable implementation candidate is
`9d76ed0e6d02271ced6479fa2fb48b6661adf5a8`. The existing annotated tag
`mvp-director-lite-pass-2026-08-09` remains untouched and continues to mark the
1.6x Director Lite FINAL baseline.

Run the only authorized human A/B entry from an Interactive Administrator
PowerShell:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "E:\小白录屏器\xiaobai-screen-recorder\tools\director-lite\Run-DirectorFocusStrength-InteractiveGate.ps1"
```

The Runner requires clean `main` and starts two separate short processes. A is
default Soft 1.6x; B is Strong 2.0x. Each uses identical left click, right
click, normal movement, and inactivity prompts and prints respectively:

```text
HUMAN CHECK SOFT MP4: <path>
HUMAN CHECK STRONG MP4: <path>
```

No focus-strength change occurs inside either Director session. Human judgment
is limited to retained 1.6x comfort, 2.0x emphasis/tightness, edge behavior,
2.0x Follow, cross-region retarget, and the unchanged 4-second Return Wide.
Omega and other camera-feel constants are frozen for this A/B.

### Human A/B evidence

The real-user A/B Gate is complete and is not to be repeated:

- Soft used the default 1.6x path and produced
  `B6142C57-3BF7-4626-AB59-91BC4D29996B.mp4`.
- Strong used the optional 2.0x path and produced
  `ACE922C6-9C63-42AB-AEA6-54E89D093FDF.mp4`.

The corresponding camera logs reach maximum zoom 1.6000 and 2.0000 and record
focus enter, focused retarget, and inactivity-Wide events. Both close summaries
record one cleanup invocation, successful cleanup, and `FormClosedUtc`.

The user's final A/B conclusion was “没什么大毛病，可以”. Soft 1.6x and Strong
2.0x therefore both pass the human Gate. Soft 1.6x remains the default; Strong
2.0x is the user-selectable emphasis level. No omega, Follow, clamp, retarget,
or 4-second inactivity tuning follows this acceptance.

### Extension verdict

`DIRECTOR-FOCUS-STRENGTH-FINAL-PASS`

The closed Soft/Strong extension is accepted and frozen. It does not replace
the original Director Lite 1.6x FINAL tag and adds no MVP product UI. The next
and only MVP mainline is the minimal screen-recording product shell.
