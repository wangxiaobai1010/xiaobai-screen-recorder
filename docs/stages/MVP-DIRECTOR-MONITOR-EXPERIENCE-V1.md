# MVP Director Monitor Experience v1

Status: `MVP-DIRECTOR-MONITOR-EXPERIENCE-GATE-READY`

This is a single human-experience Gate candidate, not a FINAL PASS.

## 1. CLICK-THROUGH NO-GO

- The `exp/preview-click-through-lab` result remains `PREVIEW-CLICK-THROUGH-LAB-NO-GO`.
- `main` was restored from clean `fb7e900`; lab commit `d918c97` was neither merged nor cherry-picked.
- No `WS_EX_TRANSPARENT`, inverse camera mapping, `SendInput`, mouse injection, or second-cursor workaround entered this candidate.
- Product decision: keep the large monitor and discard click-through.

## 2. LARGE MONITOR RETAINED

- The existing single `_previewSurface` and native renderer remain the Director Monitor.
- Standard resize, corner resize, maximize, restore, OutputCanvas presentation, and native letterbox/pillarbox behavior are retained.
- Camera motion, Follow, Director triggers, OutputCanvas, renderer, encoding, and capture paths are unchanged.

## 3. PREVIEW AREA REFINEMENT

- Preview is the only percentage-height row and receives all client space left after two fixed thin control strips.
- Outer padding was reduced from 18 px to 10 px; the separate range card and large setup stack no longer take vertical space from Preview.
- At the automated 900 x 760 client check, Preview is at least client width minus 60 px and at least 300 px high before Recording.
- At 1440 x 900, the measured Preview surface is 1396 x 522 and grows in both dimensions from the default window.
- Preview remains fill-docked. Native aspect-fit calculation continues to prevent stretching.

## 4. RECORDING SIZE CONTINUITY

- Recording state no longer collapses the heading/range/camera sections to create a second, larger Preview layout.
- Start/Stop update text and enabled state only; Preview bounds are identical before, during, and after the recording presentation transition.
- `PrepareProductWindowForRecording` and `RestoreProductWindowAfterRecording` do not own Form bounds or `WindowState`.
- A user-resized Normal window keeps its bounds across Start/Stop. A Maximized window remains Maximized.
- WDA status is still reported. If exclusion is not confirmed, the product now warns explicitly while preserving the user-owned monitor size; it does not silently minimize the Director Monitor.

## 5. CAMERA PANEL JITTER ROOT CAUSE

Root cause was WinForms layout reflow:

- the camera card and its parent rows used `AutoSize`;
- Manual/Director transitions changed `Visible` on the Manual buttons, Soft/Strong controls, and prompts;
- Recording changed `Visible` on whole setup sections;
- those visibility changes recalculated the percentage Preview row.

Resolution:

- camera controls now live in one fixed-height strip;
- Manual buttons, Director strength controls, shortcut status, and current-camera text keep stable layout slots;
- ownership changes modify `Enabled` and presentation text, not control visibility;
- recording controls also keep stable slots across Idle/Recording/Completed;
- tests prove Manual -> Director, Director -> Manual, Soft/Strong, shortcut status, Start, and Stop do not change Preview bounds.

No preview HWND/surface is recreated by these transitions.

## 6. HOTKEY PRODUCT RULE

- Existing semantics were read from `HotkeyBindings` and `FixedTargetCameraController`; they were not guessed or changed.
- `F9`: toggle 1.6x standard close-up <-> Wide 1.0x.
- `F10`: toggle 2.0x strong close-up <-> Wide 1.0x.
- The camera strip now exposes `镜头快捷键：开/关`, the real F9/F10 mappings, and an on/off button.
- Manual 1.6x and 2.0x buttons remain available as fallback when shortcuts are off or unavailable.
- The thinnest immediate state is shown as `当前镜头：Wide 1.0x`, `当前镜头：1.6x`, or `当前镜头：2.0x`.
- Director text is user-facing: `自动跟随重点 · 柔和 1.6x` or `自动跟随重点 · 强调 2.0x`; no ownership/debug terms are shown.

## 7. HOTKEY CONFLICT / DIRECTOR MUTEX

- Shortcut preference is Session-scoped and defaults to the existing OFF behavior.
- OFF unregisters/ignores F9/F10 without changing the current camera and leaves UI camera buttons usable.
- ON atomically registers both keys. Partial registration failure rolls the peer key back.
- Registration failure is explicit as `镜头快捷键：不可用 / 冲突`, includes the failed real binding/Win32 error, and keeps UI fallback available.
- Director ownership releases both registrations and displays a temporary-pause state without changing the user preference.
- Returning to Manual restores F9/F10 only when the saved preference was ON; saved OFF remains OFF.
- Preview Stop releases registrations but keeps the Session preference; the next Preview Start restores it. Temporary Director ownership is cleared on Preview Stop.
- Frozen ownership remains intact: Director rejects manual commands, and recording keeps the existing ownership/strength lock.

## 8. TESTS

Passed on 2026-08-10:

- Visual Studio MSBuild full solution: `Release|x64` PASS.
- `XbPreview.Managed.Tests.exe --resizable-director-monitor` PASS.
  - dominant pre-recording Preview;
  - free resize plus maximize/restore;
  - Normal/Maximized Start/Stop continuity;
  - Manual/Director, Soft/Strong, shortcut, recording transition bounds stability;
  - real F9/F10 mapping and immediate camera state text;
  - shortcut OFF/ON, conflict rollback/fallback, Director pause/restore;
  - aspect fit and OutputCanvas/recording resize isolation.
- `XbPreview.Managed.Tests.exe --minimal-shell` PASS.
- `XbPreview.Managed.Tests.exe --director-lite` PASS (deterministic Director coverage).
- `XbPreview.Managed.Tests.exe --camera-motion` PASS.
- `git diff --check` PASS.

Not run by design: audio human Gate, 60-minute run, P2.6 crash matrix, full historical regression.

## 9. CANDIDATE

- Branch: `main`.
- Candidate subject: `feat(mvp): refine director monitor experience`.
- Immutable candidate SHA is the clean commit containing this report and is supplied in the Gate handoff.
- No push, tag, merge, window capture, 3D motion, or click-through work is part of this candidate.

## 10. HUMAN EXPERIENCE GATE

Run one 2-3 minute Gate from the Release x64 candidate. To preserve the frozen rule that ownership/strength is selected outside active recording, use two very short recording passes inside the one Gate:

1. Before Recording, resize to roughly half-screen. Confirm Preview is already large. Toggle camera shortcut status and Manual/Director controls; confirm Preview does not move.
2. Manual pass: leave Director OFF, turn camera shortcuts ON, start Recording, use real F9 and F10 without moving the pointer to the panel, and confirm both the large image and current-camera text respond. Stop normally.
3. Director pass: turn Director ON before Start, choose Soft then Strong and confirm no Preview layout jump, start Recording, confirm F9/F10 and Manual UI cannot take ownership, then Stop. Turn Director OFF and confirm the prior shortcut preference/registration returns.
4. Resize at half-screen, roughly 80%, Maximized, and Restored. Start/Stop must not change the selected bounds/state.
5. Close normally.

Ask only:

1. Preview 现在够不够大、够不够宽？
2. 录制前后是否不再突然大小跳变？
3. 点“镜头”区域时画面还抖不抖？
4. F9/F10 操作手动镜头是不是比点按钮舒服？
5. 不移动鼠标控制镜头后，Follow 构图是否更自然？
6. 大画面 + 快捷键 + 动态镜头是否开始有“爽感”？
7. 还有没有一个明显阻碍你玩的体验问题？

Do not ask for color or visual-polish feedback in this Gate.

## 11. VERDICT

`MVP-DIRECTOR-MONITOR-EXPERIENCE-GATE-READY`

Implementation, targeted evidence, immutable candidate preparation, and the single short human Gate are complete. This does not declare FINAL PASS and does not begin window capture.
