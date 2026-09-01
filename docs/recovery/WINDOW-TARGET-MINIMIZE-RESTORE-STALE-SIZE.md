# Window Target Minimize / Restore Stale Size Recovery

## Status

`WINDOW-TARGET-RESTORE-SIZE-FROZEN`

The defect reproduces on the clean frozen baseline
`c9cb0a495c01e567d93e6219aa69c00c2cc4cd9e`. It is therefore a baseline
Window Capture lifecycle bug, not an Edge AA / MSAA candidate-only regression.
The minimal production fix and the single targeted Gate are complete. The
automated Gate passed, and the user subsequently completed the human acceptance
smoke with all three restore cycles passing. No target-window reselection was
needed. This delivery freezes that accepted result; no push was performed.

## Frozen baseline and isolation

- Branch: `fix/window-restore-stale-size`
- HEAD/base: `c9cb0a495c01e567d93e6219aa69c00c2cc4cd9e`
- Worktree: `E:\小白录屏器\worktrees\window-restore-stale-size`
- The dirty Edge AA / MSAA candidate was not modified, reset, cleaned, stashed,
  or reused.
- Manual Zoom remained `Wide 1.0x`; Director Lite and Content Camera were not
  used in either reproduction or verification.

## Baseline reproduction

The Release Host selected one ordinary isolated Chrome top-level window through
the product's Window selector. The target HWND remained the same throughout the
three cycles. The sequence was:

1. Establish normal Window Capture at `986x693` ContentSize.
2. Minimize the Chrome target and wait for capture state to stabilize.
3. Restore it, resize it, and wait for the preview to refit.
4. Repeat minimize -> restore -> resize two more times without reselecting the
   target.

The first restore/resize reproduced the defect. The second and third cycles
confirmed that the existing session had stopped consuming frames and remained
stale.

### Baseline HWND / ContentSize / FramePool timeline

| Phase | Target HWND state | Win32 rect | Content / capture size | Frames capture / present | FramePool recreate | Result |
|---|---|---:|---:|---:|---:|---|
| Initial | visible, restored | `1000x700@10,10` | `986x693` | `3761 / 3761` | `0` | Running |
| Cycle 1 minimized | visible, iconic | `160x28@-32000,-32000` | `986x693` | `3770 / 3770` | `0` | No illegal sentinel propagated |
| Cycle 1 restored + larger | visible, restored | `1180x760@120,105` | `1166x753` | `3897 / 3892` | `1` | `NativeFailure (-9)` |
| Cycle 2 minimized | visible, iconic | `160x28@-32000,-32000` | stale `1166x753` | `3897 / 3892` | `1` | No more frames |
| Cycle 2 restored + smaller | visible, restored | `820x600@160,130` | stale `1166x753` | `3897 / 3892` | `1` | Still stopped |
| Cycle 3 restored + resized | visible, restored | `1040x680@200,155` | stale `1166x753` | `3897 / 3892` | `1` | Still stopped |

The baseline p0 log records:

- target: `kind=Window;hwnd=0x1A098C`;
- first frame: `986x693`;
- restored resize: one `frame-pool-recreate`;
- immediately afterward: `HRESULT=0x887A0001` (`DXGI_ERROR_INVALID_CALL`);
- the worker then emitted `stopped` and frame counts never advanced again.

Baseline evidence:
`artifacts/bin/Release/x64/diagnostic-logs/p0-20260813-192043-837BACC0-2E78-4A60-BF3A-6B98B25B6872.jsonl`.

## Root cause classification

Classification: **C. STALE-FRAME-OR-TEXTURE**.

The failure is not `FRAMEPOOL-STALE-SIZE`: restored `ContentSize` reached
`1166x753`, and the existing FramePool recreated to that size. It is not
`RESIZE-REFIT-NOT-PROPAGATED`: the worker failed before it could render a valid
new-generation source surface; no Stage, viewport, or OutputCanvas state was
shown retaining the old dimensions. It is not `MINIMIZE-SENTINEL-STATE`: the
Win32 minimized rect was `160x28@-32000,-32000`, while capture resources stayed
at the last valid `986x693` size.

During a WGC window resize, an already queued frame can briefly pair the new
`Direct3D11CaptureFrame.ContentSize` with a surface from the old FramePool
generation. After the pool changed from `986x693` to `1166x753`, that transition
frame's underlying D3D11 texture was smaller than its declared ContentSize. The
renderer size guard returned `DXGI_ERROR_INVALID_CALL`; `PreviewEngine` treated
that transition frame as a fatal RenderFrame failure and stopped the sole
capture worker. The preview then remained on its last rendered texture.

The evidence tying the error to this transition is:

1. restored `ContentSize` and `captureWidth/Height` both changed to
   `1166x753`;
2. FramePool recreate occurred before the error;
3. no preview swap-chain resize or device removal occurred;
4. the fatal HRESULT is the renderer's source-surface/content-size invalid-call
   result;
5. dropping only this old-generation undersized surface eliminated the error,
   after which the same WGC session delivered and presented the next matching
   surface across original, larger, and smaller restores.

## Why reselecting the same window appeared to recover

`ApplyCaptureTargetAsync` stops the failed Preview, sets the same Window target,
and starts a fresh session whose initial FramePool is created at the target's
current restored size. That initialization bypasses the old-pool/new-ContentSize
transition frame, so the preview resumes. Reselection was therefore a side
effect that cleared the failed session, not a required product recovery action.
The fix does not automate or emulate reselection.

## Unique minimal fix

`PreviewEngine::ProcessPendingFrame` now reads the captured texture description
after acquiring the existing WGC frame. For Window Capture only, when the
surface is smaller than the frame's positive `ContentSize`, it closes and drops
that one resize-transition frame and waits for the already recreated FramePool's
next matching surface.

The change:

- reuses the existing WGC engine, CaptureItem, FramePool recreate path, worker,
  renderer, OutputCanvas, and session;
- creates no second engine, device, timer, polling restart, fallback capture
  API, or UI workaround;
- preserves the renderer's fatal validation for non-transition invalid input;
- does not apply zero or negative sizes to D3D resources;
- does not change application-minimized/background recording semantics.

The production boundary is uniquely limited to the 18-line guard in
`XbPreview.Native/PreviewEngine.cpp`. The Gate script and this recovery report
are verification/documentation only. No Stage, renderer, Manual Zoom, Edge AA,
audio, encoder, recording, or UI implementation was entered.

## Stage / refit / OutputCanvas timeline

OutputCanvas remains at its frozen output contract; Preview remained
`854x311` throughout the targeted Gate. The Window Stage consumes the current
valid `ContentSize` on every rendered frame and recomputes aspect-fit placement.
After the fix, valid frames were presented at `986x693`, `1166x753`, and
`806x593`, proving consecutive refit propagation without changing Stage layout,
shadow, pose, motion, or viewport code.

## WINDOW-RESTORE-SIZE-GATE

Result: **PASS**.

Command:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File ".\tools\window-capture\Run-WindowRestoreSizeGate.ps1"
```

The Gate used one isolated ordinary Chrome window and selected it exactly once.
It observed one Window Capture session through all three restore cycles. The
Host started with its normal automatic full-screen Preview; that expected
pre-selection session and the single selected Window session account for the
two p0 files. Their counts were unchanged during all restore cycles.

| Phase | Target state / rect | Capture size | Frames capture / present | Dropped | Pool recreate | State |
|---|---|---:|---:|---:|---:|---:|
| Initial normal | restored, `1000x700` | `986x693` | `60 / 60` | `0` | `0` | Running |
| Original minimized | iconic, `160x28@-32000` | `986x693` | `74 / 74` | `0` | `0` | Running |
| Restored original | restored, `1000x700` | `986x693` | `191 / 188` | `3` | `2` | Running |
| Larger minimized | iconic, `160x28@-32000` | `986x693` | `204 / 201` | `3` | `2` | Running |
| Restored larger | restored, `1180x760` | `1166x753` | `322 / 317` | `5` | `3` | Running |
| Smaller minimized | iconic, `160x28@-32000` | `1166x753` | `329 / 324` | `5` | `3` | Running |
| Restored smaller | restored, `820x600` | `806x593` | `439 / 433` | `6` | `4` | Running |

Gate assertions included:

- initial valid size and restore to original/larger/smaller sizes;
- continuous capture and Present advancement after every restore;
- no stale source size remaining;
- exactly one target selection and one Window Capture session;
- no fatal `error` or `stopped` event before Gate completion;
- no zero/negative resource size and no propagation of the Win32 minimized
  `160x28` sentinel into capture dimensions;
- recorder HWND remained visible/restored; recording and application-background
  semantics were not changed.

The `stopped` event at the end of the fixed p0 log is the Gate's normal Host
shutdown after all assertions passed. The final shutdown summary still reports
`capture=806x593`, `453 / 446` frames, and `deviceRemovedReason=0`.

Evidence:

- `artifacts/window-restore-size-gate/9a279332ca2f4377b163196889929e72/window-restore-size-gate-validated.json`
- `artifacts/bin/Release/x64/diagnostic-logs/p0-20260813-193726-7A5ECBC1-E476-4F68-B0AB-F7C09933A018.jsonl`

## Build and product artifact

The clean frozen worktree completed the one Release x64 solution build with the
pinned local GStreamer 1.28.6 SDK and pinned FFmpeg runtime. The first invocation
stopped before native compilation because ignored SDK artifacts were absent from
the new worktree; the same build was continued with the already installed pinned
dependencies. It completed with exit code 0. No second solution Rebuild was run.
After the source edit, only the affected Native Release project was compiled
incrementally to update the DLL.

- Host EXE:
  `E:\小白录屏器\worktrees\window-restore-stale-size\artifacts\bin\Release\x64\XbPreview.Host.exe`
- Native DLL:
  `E:\小白录屏器\worktrees\window-restore-stale-size\artifacts\bin\Release\x64\XbPreview.Native.dll`
- `XbPreview.Host.exe` SHA-256:
  `A2A8101F54AAEAA9AD6DCFB04C13469B080B2235A94FE86F888690FEDBA6C95F`
- `XbPreview.Native.dll` SHA-256:
  `95286691220B2C31C3F9E72F151EF8F63A0E6AC7F20F134BCAB4160ABF1A651E`
- Release retains `<WholeProgramOptimization>true</WholeProgramOptimization>`
  and `LinkTimeCodeGeneration=UseLinkTimeCodeGeneration`.
- No `/LTCG:INCREMENTAL` or `LTCGOUT` is configured.

## Human acceptance smoke

Result: **3 / 3 PASS (user-performed)**.

The user completed three normal -> minimize -> restore cycles, including wider
and narrower target sizes. Every restore automatically refit to the current
target size, and the original selected HWND remained active without reselection.
The freeze step did not rerun this human smoke.

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "E:\小白录屏器\worktrees\window-restore-stale-size\tools\window-capture\Run-WindowRestoreSizeGate.ps1" -HumanSmoke
```

This command remains documented only as the reproducible acceptance entry; it
was not executed again during mechanical freeze.

## Modified files

- `XbPreview.Native/PreviewEngine.cpp` — drop only an undersized old-pool WGC
  resize-transition surface instead of terminating the worker.
- `tools/window-capture/Run-WindowRestoreSizeGate.ps1` — single targeted real
  Chrome Gate and optional human acceptance entry.
- `docs/recovery/WINDOW-TARGET-MINIMIZE-RESTORE-STALE-SIZE.md` — this report.

## Protected-module diff audit

| Protected area | Diff |
|---|---:|
| Layer 1 layout / `WindowStageComposer` | 0 |
| Layer 2 rounded card / shadow | 0 |
| Layer 3 nine-pose transform | 0 |
| Layer 4 360 ms Enter / persistent STAY / 380 ms Return | 0 |
| `PreviewRenderer` / viewport / OutputCanvas | 0 |
| Manual Zoom / Content Camera / Director Lite | 0 |
| Audio | 0 |
| Encoder / RecordingController / Safe Publish | 0 |
| UI | 0 |
| Edge AA / MSAA / Background Preset | 0 |

Window Capture semantics are preserved: the selected HWND remains owned by one
existing WGC session, target minimize/restore does not create a second capture
engine, and recorder minimize/hide background recording behavior is untouched.

## Git audit

- `git diff --check`: PASS (exit `0`).
- Formal freeze commit message:
  `fix(capture): ignore stale transition frame after restore`.
- Annotated freeze tag: `window-target-restore-size-pass-2026-08-14`.
- No push.
- Worktree is expected to be clean after the formal commit and annotated tag are
  created and verified.
