# Window Target Compatibility Matrix Audit

- Date: 2026-08-14 (Asia/Shanghai)
- Task: `WINDOW-TARGET-COMPATIBILITY-MATRIX-AUDIT`
- Branch: `audit/window-target-compatibility`
- Worktree: `E:\小白录屏器\worktrees\window-target-compatibility-audit`
- Frozen base: `acd06f23811322b8d4d915647abd058b45984182`
- Original audit status: `WINDOW-TARGET-COMPATIBILITY-ROOT-CAUSE-FOUND`
- Freeze status: `WINDOW-STAGE-25D-SHADOW-BOUNDS-FROZEN`
- Human acceptance: `WINDOW-STAGE-25D-SHADOW-BOUNDS-HUMAN-PASS`

## 1. Executive result

Two independent defects were identified. Neither needs nor permits an
application-name special case.

1. `MP4_SELECTION_CLASSIFICATION = C`
2. `MP4_SELECTION_ROOT_CAUSE = TRANSIENT-HWND-PLUS-STALE-SELECTOR-SNAPSHOT`
3. `EDGE_RESIZE_ROOT_CAUSE = F. OTHER / 25D-TRANSFORMED-SHADOW-BOUNDS-REJECTION`
4. `EDGE_LIFECYCLE_CLASSIFICATION = A. CAPTURE-SESSION-STOPS -> E. STALE-LAST-FRAME-AFTER-SESSION-END`
5. `EDGE-25D-SPECIFIC-PRESENTATION-FAILURE`
6. `CROSS-BROWSER-25D-PRESENTATION-FAILURE`
7. `TWO-INDEPENDENT-ISSUES`
8. Product-code diff during the pre-fix audit: `0`

The Edge symptom is not an ordinary resize-propagation failure. In Identity
mode Edge and Chrome both keep the WGC session alive, keep the yellow border,
advance frame sequence/timestamps, and propagate every tested resize. With
Motion A requesting persistent `RIGHT x LEVEL_2`, both browsers instead hit a
renderer-side `E_INVALIDARG` about 320-333 ms into the 360 ms Enter transition.
The Preview worker then tears down capture. The yellow border disappears,
frame sequence and timestamps stop, and any picture still visible in the
product Preview is the last cached/presented frame.

The exact presentation rejection is aspect-sensitive. For the common
`886x693` initial content size, the transformed content quad fits in the
`1920x1080` OutputCanvas, but the transformed Layer 2 shadow reaches
`y=1081.533`. `ComposeWindowStageTransform` requires the entire shadow bounds
to stay within the canvas (tolerance `0.01`), returns `false`, and causes
`PreviewRenderer::RenderFrame` to return `E_INVALIDARG`. This happens before
Layer 4 can enter STAY. The requested persistent pose therefore cannot remain
in STAY in this failing geometry; the runtime reaches Error first.

`EDGE-25D-SPECIFIC-PRESENTATION-FAILURE` describes the required C-vs-D result:
Edge Identity is healthy while Edge 2.5D fails. It is scenario-specific, not an
Edge process-name exception: Chrome shows the same 2.5D failure under identical
geometry.

## 2. Pre-fix baseline, isolation, and audit-only changes

At the time of the root-cause audit, before the bounded repair and freeze:

- The independent worktree was created clean at the exact frozen base.
- The dirty user worktree was not modified or cleaned.
- No unfrozen Edge AA, MSAA, analytic AA, or local-supersampling candidate was
  used as the product baseline.
- There was no commit, tag, or push.
- The only implementation added is the audit-only script
  `tools/window-capture/Invoke-WindowTargetResizeAudit.ps1`.
- The only report added is this file.
- Protected production modules, UI, Capture, PreviewEngine, PreviewRenderer,
  WindowStage, FramePool, Audio, Encoder, RenderFrameTap, and recording code
  have zero diff.

The script uses the public frozen Native ABI to create a short real WGC
session, moves the selected real target through the required phases, polls the
existing Stats, keeps a test-only Return event unsignaled, and saves audit
screenshots/logs under `%TEMP%`. It does not alter production behavior.

## 3. Selector rule and six-window matrix

The frozen selector uses `EnumWindows` and admits a window only when:

`IsWindow && IsWindowVisible && GA_ROOT == HWND && !DWM_CLOAKED && pid != 0 && pid != recorderPid && title is not blank`

It does not filter on `IsIconic`, style, or ex-style. The style fields below
are observations, not eligibility rules.

| Target | HWND; PID; executable | class; owner / parent / root | state and geometry at inspection | style / ex-style | Selector |
| --- | --- | --- | --- | --- | --- |
| Edge | `0x20274`; `5188`; `C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe` | `Chrome_WidgetWin_1`; `0 / 0 / 0x20274` | `IsWindow=1`; visible=1; iconic=0; cloaked=0; rect `103,116,1162,914`; DWM `110,116,1155,907` | `0x16CF0000 / 0x200100` | Listed; all facts pass; real `CreateForWindow` succeeds |
| Chrome | `0x30770`; `17860`; `C:\Program Files\Google\Chrome\Application\chrome.exe` | `Chrome_WidgetWin_1`; `0 / 0 / 0x30770` | `IsWindow=1`; visible=1; iconic=1; cloaked=0; minimized rect `-32000,-32000,-31840,-31972`; DWM `-31993,-32000,-31847,-31979` | `0x36CF0000 / 0x200100` | Listed because `IsIconic` is not a filter; capture succeeds after restore |
| VS Code | `0x30376`; `9072`; `D:\VSCODE\Microsoft VS Code\Code.exe` | `Chrome_WidgetWin_1`; `0 / 0 / 0x30376` | `IsWindow=1`; visible=1; iconic=0; cloaked=0; rect `624,115,1904,935`; DWM `631,115,1897,928` | `0x14C70000 / 0x200100` | Listed; all facts pass; resize control succeeds |
| WeChat | `0x20512`; `15908`; `D:\Weixin\Weixin.exe` | `Qt51514QWindowIcon`; `0 / 0 / 0x20512` | `IsWindow=1`; visible=1; iconic=1; cloaked=0; minimized rect | `0xB6C70000 / 0x100` | Listed; iconic is not filtered; human control is selectable and resizes |
| Youdao | `0x205D4`; `14524`; `E:\杂物\有道\Dict\YoudaoDict.exe` | `YodaoMainWndClass`; `0 / 0 / 0x205D4` | `IsWindow=1`; visible=1; iconic=1; cloaked=0; minimized rect | `0xB60FC000 / 0x100` | Listed; iconic is not filtered; human control is selectable and resizes |
| Current default MP4 window | inspection `0x260714`, reopened Gate `0xB060A`; shell PID `11524`; `C:\Windows\System32\ApplicationFrameHost.exe` | `ApplicationFrameWindow`; `0 / 0 / self` | inspection: `IsWindow=1`; visible=1; iconic=0; cloaked=0; rect `40,91,1256,1032`; DWM `47,91,1249,1025` | `0x94CF0000 / 0x200100` | Listed while alive; real CreateForWindow and first-frame Gate succeed |

The MP4 content process was `Microsoft.Media.Player.exe`, but that brand is not
used by the diagnosis or proposed rule. Its visible capturable surface was the
top-level `ApplicationFrameWindow` shell HWND. Hidden IME/MSCTF child windows
were not eligible targets.

## 4. Independent MP4 Selection defect

The audit opened a real MP4 produced by this project through the machine's
current Windows default association, without assuming a player name.

First instance:

1. New top-level HWND `0x260714` passed every selector fact.
2. Before the delayed Gate used the stored value, that HWND and its content
   process disappeared.
3. The Gate failed in `WindowCaptureSelector.Enumerate()` because the stored
   HWND no longer existed; only an unrelated cloaked shell window remained.

Reopened instance:

1. The same MP4 produced a new top-level HWND `0xB060A`.
2. Immediate enumeration listed it.
3. `IGraphicsCaptureItemInterop::CreateForWindow` succeeded.
4. The first real frame was `1202x934`.

This excludes A, B, D, E, and F from the required MP4 classification. The
failure is C: the UI holds an enumeration snapshot containing an HWND that can
be destroyed or replaced before selection is applied.

The generic code reason is:

- `RefreshWindowChoices()` rebuilds the ComboBox only on entry to Window mode
  or an explicit Refresh action.
- `OnWindowSelectionChanged()` applies the previously stored `choice.Handle`.
- The apply path does not revalidate `IsWindow`, root, cloak, title, and PID
  facts immediately before use.

`MP4_SELECTION_CLASSIFICATION = C`

`MP4_SELECTION_ROOT_CAUSE = TRANSIENT-HWND-PLUS-STALE-SELECTOR-SNAPSHOT`

## 5. Required 2x2 Identity vs Persistent 2.5D comparison

The two Identity runs are retained controls and were not rerun. The B/D runs
used the frozen test-only Motion A selector. A uniquely named manual-reset
Return event existed for the whole session and was never signaled. The frozen
target is exactly `RIGHT x LEVEL_2`:

`scale=0.83, placementX=+0.040, placementY=-0.022, rotationX=-8 deg, rotationY=+24 deg, perspectiveDepth=1.00`

| Scenario | Capture/session result | Required resize sequence | Presentation result |
| --- | --- | --- | --- |
| A. Chrome / Identity | Healthy; 12-second lifecycle advanced every sample; no Closed proxy | `886x693 -> 1386x693 -> 686x693 -> 1920x1032 -> 686x693`; four pool recreates | Working control; yellow border persisted |
| B. Chrome / `RIGHT x LEVEL_2` Persistent request | First frame succeeds; Error at about 333 ms; `capture/present=21/20`; state Error; `LastResult=-9` | Win32 window completed larger, smaller, maximize, restore, but capture was already stopped; ContentSize remained `886x693`; recreate remained 0 | Motion never reaches STAY; transformed shadow validation returns `E_INVALIDARG`; yellow border disappears; last frame is stale |
| C. Edge / Identity | Healthy; 12-second lifecycle advanced every sample; no Closed proxy | `886x693 -> 1386x693 -> 686x693 -> 1920x1032 -> 686x693`; four pool recreates | Working control; yellow border visible at 1, 6, and 12 seconds |
| D. Edge / `RIGHT x LEVEL_2` Persistent request | First frame succeeds; Error at about 329-333 ms; `capture/present=21/20`; state Error; `LastResult=-9` | Win32 window completed larger, smaller, maximize, restore, but capture was already stopped; ContentSize remained `886x693`; recreate remained 0 | Motion never reaches STAY; same transformed-shadow validation failure; yellow border absent after stop; last frame is stale |

The Return event was never signaled in B or D, so no automatic or explicit
Return path caused the failure. Nevertheless, it would be false to report that
Layer 4 “remained in STAY”: for this geometry the renderer fails at roughly
320-333 ms, before the 360 ms transition endpoint. The stronger and actionable
result is that the requested Persistent STAY state is unreachable.

### 5.1 Capture and presentation timeline

| Fact | Chrome 2.5D | Edge 2.5D |
| --- | --- | --- |
| Initial ContentSize / FramePool | `886x693 / 886x693` | `886x693 / 886x693` |
| Last successful Present | 20 | 20 |
| Last arrived frame | 21 | 21 |
| Final WGC SystemRelativeTime | `85654167572` | `87045904741` |
| Final frame-arrival QPC | `85654033616` | `87045769442` |
| Later sequence/timestamp movement | none | none |
| FramePool recreates | 0 | 0 |
| Final state/result | Error / `NativeFailure (-9)` | Error / `NativeFailure (-9)` |
| Native terminal error | `0x80070057`, D3D11 render/Present failed | `0x80070057`, D3D11 render/Present failed |
| Item Closed proxy (`-20`) | not observed | not observed |

At the last successful frame, FramePool size, source texture size, Stage input,
and renderer input were all `886x693`. This equality follows the same frozen
`RenderFrame(contentWidth, contentHeight, ...)` call: `EnsureSourceTexture`
uses that current ContentSize and `ComposeFlat` receives the same values. No
later sizes can propagate because the worker has already torn down capture.

## 6. Yellow border, session/item lifecycle, and stale frame

Identity control, Edge:

- target HWND `0x20274` remained `IsWindow=true`, root=self, owner=0,
  PID `5188`, class `Chrome_WidgetWin_1` at every second;
- state remained Running and `LastResult=0`;
- capture frames advanced from 17 to 781 and Presents to 780 after the control
  resize;
- `LastSystemRelativeTime100ns` and `LastFrameArrivalQpc` advanced at every
  sample;
- the yellow WGC border was visibly present at 1 s, 6 s, and 12 s.

Identity control, Chrome showed the same pattern: frames 17 to 779, Presents
to 778, all timestamps advanced, no Closed proxy, and the yellow border stayed
visible at 1 s, 6 s, and 12 s.

Persistent 2.5D, both browsers:

1. `StartCapture` succeeds and the yellow border appears.
2. Twenty frames render and present.
3. Frame 21 arrives near the end of Motion A Enter.
4. Stage transform validation returns false; RenderFrame returns
   `E_INVALIDARG`.
5. PreviewEngine records `NativeFailure (-9)`, enters Error, and closes the
   session/frame pool/item during worker teardown.
6. The yellow border disappears.
7. Sequence, timestamps, ContentSize, and Present count no longer change.
8. A picture that remains in the product Preview is necessarily the last
   cached/presented frame, not a new WGC frame.

The product contains no read or write of
`GraphicsCaptureSession.IsBorderRequired`; repository-wide searches found no
`IsBorderRequired`, `BorderRequired`, borderless-capture, or access-request
path. Border-only disablement is therefore excluded at this frozen base.

The `GraphicsCaptureItem.Closed` handler is registered, but it did not fire in
the failing runs: its unique product result is `WindowTargetClosed (-20)`,
whereas both failures ended with `NativeFailure (-9)`. The target HWND also
remained the same root HWND before and after the border loss.

Required lifecycle classification:

- A `CAPTURE-SESSION-STOPS`: yes, as the downstream effect of the renderer
  failure and worker teardown.
- B `CAPTURE-ITEM-CLOSED`: no evidence; `-20` was not observed.
- C `TARGET-HWND-CHANGED`: excluded; HWND/root/owner/PID/class stayed stable.
- D `BORDER-ONLY-DISABLED-BUT-CAPTURE-ALIVE`: excluded; border setting is not
  modified, and frames/timestamps stop with the border.
- E `STALE-LAST-FRAME-AFTER-SESSION-END`: yes for the reported Preview picture.
- F `OTHER`: the initiating defect is the 2.5D presentation validation error,
  not an unexplained WGC shutdown.

`EDGE_LIFECYCLE_CLASSIFICATION = A -> E, initiated by 25D presentation failure`

## 7. Exact 2.5D presentation failure

For source `886x693` and OutputCanvas `1920x1080`, frozen Layer 1 produces:

- flat card bounds: `left=338.649`, `top=54.000`, `right=1581.351`,
  `bottom=1026.000`;
- Layer 2 shadow strength: `0.687545`;
- shadow vertical offset: `11.1879 px`;
- shadow softness: `36.4996 px`.

At exact frozen `RIGHT x LEVEL_2`, Layer 3 produces:

- content transformed bounds: `469.284,58.399 -> 1474.900,1021.920`;
- content corners TL/TR/BL/BR:
  `(470.630,58.399)`, `(1407.276,186.130)`,
  `(469.284,1021.920)`, `(1474.900,870.515)`;
- transformed shadow bounds:
  `426.292,30.228 -> 1499.027,1081.533`.

The content is valid, but the shadow bottom exceeds the 1080 canvas by about
`1.533 px`. During smootherstep Enter, the first invalid integer-millisecond
sample is about 320 ms: shadow bottom is already `1080.071`, beyond the
`1080.01` tolerance. The runtime errors at approximately 329-333 ms, matching
that calculation.

The code chain is:

1. `WindowShowcaseMotionController::Update` interpolates all pose fields.
2. `ComposeWindowStageTransform` transforms content and the Layer 2 shadow
   support rectangle.
3. It requires both complete transformed bounds to be inside OutputCanvas.
4. The shadow check fails and `ComposeWindowStageTransform` returns false.
5. `PreviewRenderer::RenderFrame` returns `E_INVALIDARG`.
6. `PreviewEngine` reports “D3D11 render/Present failed”, sets `-9`, and tears
   down capture.

This is neither `CONTENTSIZE-NOT-UPDATING`, `FRAMEPOOL-SIZE-STALE`,
`STAGE-REFIT-STALE`, nor `RENDERER-SIZE-STALE`. Resize never gets a chance to
propagate in B/D because the 2.5D presentation path stops the worker first.

`EDGE_RESIZE_ROOT_CAUSE = F. OTHER / 25D-TRANSFORMED-SHADOW-BOUNDS-REJECTION`

## 8. Identity resize controls

The earlier Identity runs remain valid controls:

| Phase | Edge Content/Pool | Edge frames / presents / drops | Chrome Content/Pool | VS Code Content/Pool |
| --- | --- | --- | --- | --- |
| normal | `886x693`; recreate 0 | `18 / 18 / 0` | `886x693`; recreate 0 | `886x693`; recreate 0 |
| larger | `1386x693`; recreate 1 | `44 / 43 / 1` | `1386x693`; recreate 1 | `1386x693`; recreate 1 |
| smaller | `686x693`; recreate 2 | `64 / 62 / 2` | `686x693`; recreate 2 | `686x693`; recreate 2 |
| maximized | `1920x1032`; recreate 3 | `83 / 80 / 3` | `1920x1032`; recreate 3 | `1920x1032`; recreate 3 |
| restored | `686x693`; recreate 4 | `102 / 98 / 4` | final `105 / 101 / 4` | final `99 / 95 / 4` |

In Identity mode Edge has no divergence from Chrome or VS Code anywhere in
HWND selection, CreateForWindow, ContentSize, pool recreate, source texture,
Stage input, renderer input, or Present.

## 9. Read-only code and Git history audit

Relevant frozen code path:

1. `CaptureTarget.cs` uses `EnumWindows`, reads PID/title/cloak/root facts, and
   applies the selector predicate above.
2. `MainForm.cs` refreshes the choice list on mode entry/manual refresh and
   later applies the stored handle.
3. `PreviewEngine.cpp` calls `CreateForWindow`, creates the pool from Capture
   Item size, registers `FrameArrived` and `GraphicsCaptureItem.Closed`, and
   starts capture.
4. Each frame reads `ContentSize`; a change recreates the pool and updates
   Stats.
5. The same current content dimensions go to `PreviewRenderer::RenderFrame`.
6. `PreviewRenderer` rebuilds source texture/SRV when dimensions differ,
   recomputes Flat Stage and transformed Stage, then Presents.

Relevant history, without claiming an unproved regression commit:

- `16e9ae06` introduced Window Stage Capture, selector, and the original WGC
  resize path. `CaptureTarget.cs` has no later selector behavior change.
- `ff0b19ec` rebuilt/froze Window Capture and changed PreviewEngine plus Gate
  diagnostics.
- `9102a5fa`, `9ef74f96`, `c015e985`, and `35da4762` froze Layers 1-4.
- `c9cb0a49` restored full Release LTCG configuration only.
- `acd06f23` added only the minimize-to-restore stale-transition-frame fix.

The existing test gap explains why this failure could survive freeze:

- the Layer 3 minimal test composes exact `RIGHT x LEVEL_2` using a
  `1600x900` source fixture;
- the Layer 4 test checks the motion controller's interpolation and STAY state
  independently;
- it does not feed every interpolated pose through Stage composition across
  real window aspect ratios such as `886x693`.

No Edge/MP4 last-known-good commit was supplied or proved. No commit is labeled
as the regression source.

## 10. Pre-fix build and evidence limits

Exactly one Release x64 solution build was attempted. Static project settings
confirm `/GL` and complete `/LTCG`, but the build stopped before Native compile
because the new worktree lacked the pinned GStreamer 1.28.6 SDK. The audit did
not install dependencies or spend a second build.

Short runtime diagnosis reused the clean frozen-base Native DLL at:

`E:\小白录屏器\worktrees\window-restore-stale-size\artifacts\bin\Release\x64\XbPreview.Native.dll`

SHA-256:

`95286691220B2C31C3F9E72F151EF8F63A0E6AC7F20F134BCAB4160ABF1A651E`

Evidence directories:

- Edge Identity lifecycle:
  `%TEMP%\xbpreview-window-target-resize-audit\20260814-024616-EdgeLifecycle-9325dc50dbed45ae8fb4eb0ce7407d9b`
- Chrome Identity lifecycle:
  `%TEMP%\xbpreview-window-target-resize-audit\20260814-024900-ChromeLifecycle-4efe93ff644549cf8513554e90ed0fd1`
- Chrome 2.5D:
  `%TEMP%\xbpreview-window-target-resize-audit\20260814-025649-ChromePersistent25D-4dda892c575b404c9589a7f04052ef21`
- Edge 2.5D:
  `%TEMP%\xbpreview-window-target-resize-audit\20260814-025908-EdgePersistent25D-7adcb0eda41d42489a8c40bfbc4ee300`

The product applies `WDA_EXCLUDEFROMCAPTURE` to Preview, so ordinary screen
copy cannot be used as authoritative Preview-pixel evidence. Target-window
screenshots remain valid for the yellow-border observation. Frame/Present
counts, timestamps, Native error logs, geometry, and the deterministic Stage
calculation are the authoritative presentation evidence.

## 11. Pre-fix repair recommendations

The 2.5D presentation recommendation below was subsequently implemented and
frozen by the addendum in section 13. The MP4 Selection recommendation remains
unimplemented and independent.

MP4 Selection:

1. Refresh generically when the window ComboBox opens.
2. Revalidate the stored HWND against the same eligibility facts immediately
   before applying it.
3. If stale, clear it, refresh, and ask the user to select the live replacement;
   do not map by process name, title, or brand.
4. Add a Gate where an eligible enumerated HWND is destroyed/replaced before
   selection.

2.5D presentation:

1. Preserve the exact frozen `RIGHT x LEVEL_2` pose; do not add an Edge branch.
2. Make partial off-canvas transformed shadow coverage a defined nonfatal
   presentation case (for example, allow normal GPU clipping while retaining
   strict validation of the visible content quad), or otherwise refit only the
   generic Stage/shadow support geometry.
3. Add an integration Gate that runs every Motion A transition sample through
   `ComposeWindowStageTransform` for a small aspect-ratio matrix including
   `886x693`, then holds exact STAY through larger, smaller, maximize, restore.
4. Assert WGC sequence/timestamps, ContentSize, pool size, source texture size,
   Stage inputs, transformed content/shadow bounds, and Render/Present counts.
5. Preserve a renderer-stage-specific diagnostic instead of collapsing this
   failure to the generic “D3D11 render/Present failed” message.

## 12. Pre-fix diff audit and final status

- Production code diff: `0`
- Test code diff: `0`
- Audit-only tool: `tools/window-capture/Invoke-WindowTargetResizeAudit.ps1`
- Audit report: `docs/recovery/WINDOW-TARGET-COMPATIBILITY-MATRIX.md`
- Commit/tag/push: none

`MP4_SELECTION_CLASSIFICATION = C`

`MP4_SELECTION_ROOT_CAUSE = TRANSIENT-HWND-PLUS-STALE-SELECTOR-SNAPSHOT`

`EDGE-25D-SPECIFIC-PRESENTATION-FAILURE`

`EDGE_RESIZE_ROOT_CAUSE = F. OTHER / 25D-TRANSFORMED-SHADOW-BOUNDS-REJECTION`

`EDGE_LIFECYCLE_CLASSIFICATION = A. CAPTURE-SESSION-STOPS -> E. STALE-LAST-FRAME-AFTER-SESSION-END`

`TWO-INDEPENDENT-ISSUES`

`WINDOW-TARGET-COMPATIBILITY-ROOT-CAUSE-FOUND`

## 13. 2.5D shadow-bounds repair and freeze addendum

The bounded repair classifies the presentation defect as:

`A. OVERSTRICT-SHADOW-BOUNDS-VALIDATION`

At `886x693` content in a `1920x1080` OutputCanvas, the logical content card
remains inside the canvas while the finite transformed shadow support reaches
`bottom=1081.533`. The old CPU validation incorrectly treated that soft-effect
overscan as invalid composition. The repair preserves strict
`content.IsInside(OutputCanvas)` validation and changes only transformed
shadow support validation to `IsFiniteNonEmpty`. Projected points must still be
finite, non-degenerate, free of NaN/Inf, and have homogeneous `w >= 0.25`.
The existing full OutputCanvas viewport/rasterizer clips finite shadow
overscan. The card is not moved or scaled, and no union safe-fit is used.

The deterministic Exact Failure Gate covers every integer millisecond of
Motion A from Identity through the 360 ms Transition into exact persistent
`RIGHT x LEVEL_2` STAY. It proves the first finite overscan at 320 ms
(`bottom=1080.071`), the exact STAY bound (`bottom=1081.533`), persistent STAY
with no automatic Return, and larger/smaller/maximized/restored refits. It also
keeps the content-off-canvas negative case rejecting composition and checks
RIGHT/LEFT/FRONT LEVEL_3 for finite clip and pixel coordinates without
NaN/Inf.

Live targeted Gate results:

| Target | Persistent RIGHT L2 sequence | Frame / Present evidence | Session evidence | Result |
| --- | --- | --- | --- | --- |
| Chrome | Identity -> 360 ms Transition -> STAY -> larger -> smaller -> maximize -> restore | `61/61 -> 339/335`; FramePool recreate `0 -> 4` | Running, `lastResult=0`; normal/restored yellow border persisted; post-restore `+41/+41` proves live frames rather than a cached last frame | PASS |
| Edge | Identity -> 360 ms Transition -> STAY -> larger -> smaller -> maximize -> restore | `60/60 -> 337/333`; FramePool recreate `0 -> 4` | Running, `lastResult=0`; no Closed proxy; normal/restored yellow border persisted; post-restore `+41/+41` proves live frames | PASS |

Tiny Identity controls also passed: Chrome `20/20 -> 360/359`; Edge
`20/20 -> 357/356`. Native evidence contains no `E_INVALIDARG`, `0x80070057`,
or `NativeFailure` signature for the repaired Gate.

Xiaobai human acceptance is complete:

- Chrome entered RIGHT LEVEL_2, stayed persistently, followed larger/smaller
  resize and maximize/restore, kept capture alive, and did not retain a stale
  last frame.
- Edge captured normally, retained the 2.5D presentation through the same
  resize/maximize/restore sequence, and no longer lost the yellow border into
  stale-last-frame.
- The shadow remained visibly present in the accepted human presentation.
- The final MP4 was generated and viewed normally in this human Smoke.

`WINDOW-STAGE-25D-SHADOW-BOUNDS-HUMAN-PASS`

The MP4 observation does not close the independent selector defect. The
player window was usable in this Smoke, but
`MP4_SELECTION_CLASSIFICATION = C` and
`TRANSIENT-HWND-PLUS-STALE-SELECTOR-SNAPSHOT` remain recorded; selector Exact
Failure repair/freeze has not been performed in this change.

Frozen-change audit:

- finite shadow support overscan: allowed;
- union safe-fit: no;
- Layer 2 rounded-corner/shadow parameters: diff `0`;
- Layer 3 nine-pose table: diff `0`;
- Layer 4 Motion timing/easing/STAY/Return behavior: diff `0`;
- Window Capture product source: diff `0`;
- Edge AA, MSAA, local supersampling, Manual Zoom Punch-in, and Background
  Preset: not entered;
- Release configuration remains `/GL` plus complete `/LTCG`, without
  `/LTCG:INCREMENTAL` or `/LTCGOUT`.

`WINDOW-STAGE-25D-SHADOW-BOUNDS-FROZEN`
