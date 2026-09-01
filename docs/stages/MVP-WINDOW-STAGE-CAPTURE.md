# MVP Window Stage Capture

Status: **MVP-WINDOW-STAGE-CAPTURE-GATE-READY**. This is an immutable-candidate
gate handoff, not FINAL PASS. 3D Window Motion is intentionally not included.

## 1. MATURE ROUTE

The existing `PreviewEngine` capture-item creation point was the correct seam.
`CreateForMonitor` and `CreateForWindow` now select a target before the same
`CreateFreeThreaded` frame pool, `FrameArrived` single-frame slot, GPU renderer,
OutputCanvas, RenderFrameTap, encoder, audio timeline, and Safe Publish path.
There is no second capture engine, controller, frame tap, device, encoder, or
clock.

Official semantics used for the implementation:

- [`IGraphicsCaptureItemInterop::CreateForWindow`](https://learn.microsoft.com/en-us/windows/win32/api/windows.graphics.capture.interop/nf-windows-graphics-capture-interop-igraphicscaptureiteminterop-createforwindow)
  creates a `GraphicsCaptureItem` for one HWND.
- [`GraphicsCaptureItem.Closed`](https://learn.microsoft.com/en-us/uwp/api/windows.graphics.capture.graphicscaptureitem.closed)
  is raised when the selected target ends (including apps that replace their
  HWND while appearing to remain open).
- Microsoft’s [screen-capture guidance](https://learn.microsoft.com/en-us/windows/apps/develop/media-authoring-processing/screen-capture)
  defines `SystemRelativeTime` as QPC time, requires `ContentSize` sub-rectangle
  handling to avoid undefined surface pixels, and recommends frame-pool
  `Recreate` for size changes.
- [`IsCursorCaptureEnabled`](https://learn.microsoft.com/en-us/uwp/api/windows.graphics.capture.graphicscapturesession.iscursorcaptureenabled)
  controls whether WGC includes the cursor.

Audit answers: (A) replace only capture-item creation; (B) yes, monitor and
window share one WGC acquisition engine; (C) keep the `ID3D11Texture2D` and use
`CopySubresourceRegion`, never CPU readback; (D) use per-frame `ContentSize`,
drop only the transition frame, recreate the pool, and retain fixed OutputCanvas;
(E) map `Closed` to explicit `WindowTargetClosed`, then existing encoder
Stop/Finalize/Publish during renderer shutdown; (F) use a minimal title-only HWND
selector. No fallback selector is needed for this MVP.

## 2. CAPTURE TARGET ABSTRACTION

`CaptureTarget` has exactly two kinds: `Monitor` (the default) and
`Window(HWND)`. The native setter accepts changes only while Preview is stopped.
The managed shell stops Preview before a target change and refuses changes while
Recording is active. Existing custom-region code remains product-disabled.

## 3. WINDOW SELECTION

The shell shows `录制范围: 全屏 / 窗口`. Window mode enumerates visible,
uncloaked, titled, top-level HWNDs and displays only their titles. Windows owned
by the recorder process are filtered in managed selection and independently
rejected by native validation. Refresh/reselect is supported; thumbnails,
search, favorites, history, and persistence are absent.

## 4. WGC HWND CAPTURE

Window mode calls `IGraphicsCaptureItemInterop::CreateForWindow`. From that point
on it uses the pre-existing `CreateFreeThreaded`, FrameArrived callback gate,
latest-frame slot, worker, D3D11 device/context, GPU composition, FrameTap,
encoder, recording controller, and publish ownership.

## 5. CONTENTSIZE / RESIZE

Every pending frame carries its real `frame.ContentSize`. The renderer creates
an engine-owned GPU texture at that exact size and copies only the valid
`ContentSize` box with `CopySubresourceRegion`; undefined pool-surface edges are
never sampled. When size differs from the current pool, the checked-out frame is
closed before `Recreate`, pending frames are cleared, and capture resumes.
Window-card placement is recalculated from the new aspect ratio. OutputCanvas,
RenderFrameTap generation, encoder dimensions, and Recording ownership do not
change or restart.

## 6. TIMESTAMPS

Window frames retain `Direct3D11CaptureFrame.SystemRelativeTime().count()` and
enter the existing `RenderFrameTapTimestamp` / `VideoEncoderTimestamp` path.
No stopwatch, UI clock, wall clock, or second video timeline was added. Existing
WASAPI QPC/session audio mapping is unchanged.

## 7. WINDOW CARD STAGE

Window mode clears the fixed OutputCanvas, calculates one `WindowCardPlacement`,
and renders the WGC texture into that viewport. The centralized maximum width/
height fraction is `0.90`. Placement preserves aspect ratio, is centered, does
not crop, and depends only on source content size plus fixed output size. Moving
the physical HWND therefore cannot move the final card.

## 8. CLEAN BACKGROUND

The stable BGRA8 OutputCanvas clear color is the explicit UNORM/sRGB component
value `(243, 240, 234, 255)`, equivalent to `#F3F0EA` in the current non-sRGB
UNORM render target. Full-screen capture retains its previous black clear and
full-canvas draw semantics.

## 9. CURSOR

Window mode chooses WGC cursor capture and never initializes/draws the existing
custom cursor layer. This guarantees one cursor and avoids creating a third
cursor system. Full-screen mode keeps the existing System/Custom selection and
fallback policy unchanged.

## 10. DIRECTOR MAPPING

Raw Input remains the single Director event source. In window mode a desktop
point must be inside the selected HWND’s DWM extended physical frame bounds. It
is then normalized into target-window coordinates; because the flat card is an
aspect-preserving affine viewport, the same normalized coordinate addresses the
card and OutputCanvas camera. Outside clicks/activity are rejected before focus,
retarget, or inactivity extension. Manual 1.6x/2.0x and Follow use the same
window-normalized source space; controls clicked outside the target use center
as the safe Manual target.

## 11. TARGET CLOSE

The existing `GraphicsCaptureItem.Closed` registration now distinguishes window
targets. It stops accepting frames, records `WindowTargetClosed`, and wakes the
worker. Worker shutdown invokes the existing encoder `StopAndJoin`, which owns
Finalize, validation, and Safe Publish, retaining valid media already recorded.
The UI reports `目标窗口已关闭` and allows reselection; it never silently falls
back to full screen or repeats an old frame as success.

## 12. MINIMIZE LIMITATION

Perfect minimized-window capture remains out of scope. `IsIconic` is exposed as
`WindowTargetMinimized`; the UI warns that the target should be restored. The
product does not change system settings, force-restore another app, or claim
continuous capture that WGC did not produce.

## 13. FULL SCREEN REGRESSION

Monitor selection remains the default. Its `CreateForMonitor`, crop validation,
camera, cursor policy, black OutputCanvas composition, Preview, background
recording, audio, encoder, and Safe Publish paths remain in place. The only
shared renderer change is a stricter GPU copy of the real valid content box.

## 14. 3D TRANSFORM SEAM

`WindowCardPlacement` is the only stage-placement concept. Today it resolves to
a centered 2D viewport. A later slice can replace that placement draw with a
textured quad/perspective transform inside the same compositor without changing
WGC acquisition, timestamps, audio, RecordingController, FrameTap, encoder, or
Safe Publish. No 3D engine or inverse transform was added.

## 15. TESTS

Release x64 solution build: **PASS**, 0 warnings / 0 errors.

Targeted automated results:

- Native `--window-stage`: **PASS** (default monitor, card fit/center/resize,
  background, camera transforms, cursor policy, timestamp policy, audio
  foundations).
- Managed `--window-stage`: **PASS** (ABI/export, self-window rejection,
  ordinary-window facts, outside-click rejection, target-close/minimize facts,
  Director/hotkeys, audio-control regression).
- Managed `--resizable-director-monitor`: **PASS**.
- Managed `--director-lite`: **PASS**.
- Managed `--minimal-shell`: **PASS** (recording/Stop/Finalize/Publish shell
  policy regression).
- Native and managed `--p2.8e-audio-controls`: **PASS**, including meter,
  silence, system/microphone mute, timeline continuity, gain, and clamp checks.

The requested 60-minute run, full P2.6 crash matrix, full-history regression,
and 3D tests were not run.

## 16. CANDIDATE

The immutable candidate subject is `feat(mvp): add window stage capture` on
`main`. It contains product code, targeted tests, this report, and the sole human
gate runner. No push, tag, merge, or 3D work is part of this candidate.

## 17. HUMAN GATE

Run from an elevated PowerShell after confirming the worktree is clean:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\window-stage\Run-WindowStageCapture-InteractiveGate.ps1
```

The runner enforces `main`, clean immutable HEAD, and Release x64 availability;
then guides the single 2–4 minute Window Stage path: selection, clean stage,
move, resize, Manual, F9/F10, Director Soft/Strong, retarget/Return Wide,
outside click, Stop, and Open Video. Optional target-close checking is last.

`HUMAN CHECK MP4: <printed by the runner after the user completes the gate>`

## 18. VERDICT

**MVP-WINDOW-STAGE-CAPTURE-GATE-READY**

Automated/build evidence is green and the only remaining acceptance action is
the explicitly prepared human Gate. Do not call this FINAL PASS until the human
answers the eight requested experience questions from the produced MP4.
