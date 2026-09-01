# MVP Resizable Director Monitor

Status: `MVP-RESIZABLE-DIRECTOR-MONITOR-GATE-READY`

This slice is ready for the single human experience gate. It is not a FINAL PASS.

## 1. EXISTING PREVIEW REUSE

- `MainForm._previewSurface` remains the only managed preview HWND.
- `NativePreviewSession.Create` still passes that HWND into the existing native `PreviewEngine` and `PreviewRenderer`.
- The displayed image is the existing final `OutputCanvas` composition. `PreviewRenderer::RenderFrame` builds the OutputCanvas first, sends that same texture to the recording frame tap, and then presents it to the preview swap chain.
- No second renderer, capture path, camera path, or UI framework was added.

## 2. RESIZE / MAXIMIZE DESIGN

- The product window explicitly uses the standard Windows `Sizable` border and enabled maximize box. No maximum size or forced aspect ratio is applied to the Form.
- Initial client size is `900 x 760`; minimum window size is `640 x 640`.
- The Minimal Shell now uses a fill-docked table layout. Its Preview row receives all remaining client area instead of fixing the preview to 245 px high.
- `_previewSurface` is `DockStyle.Fill`, so edge resize, corner resize, maximize, and restore all change the real preview viewport.
- During Recording, only nonessential setup rows collapse. The Preview remains visible and the current Normal/Maximized window size is not rewritten.
- If the safety fallback must minimize the Form, the prior Normal/Maximized state is restored after Stop.

## 3. ASPECT RATIO

- Form aspect ratio is intentionally free.
- Native presentation continues to call `CalculateLetterbox(OutputCanvas, PreviewViewport)` on every presented frame.
- The preview render target is cleared to neutral black before the fitted OutputCanvas is drawn, producing letterbox or pillarbox without stretching.
- OutputCanvas dimensions remain owned by `SessionGeometry`; preview viewport dimensions remain owned by presentation resize.

## 4. CAPTURE EXCLUSION

- The existing mature path is reused: native `PreviewEngine::ApplyWindowDisplayAffinity` applies `WDA_EXCLUDEFROMCAPTURE` to the MainForm exclusion HWND.
- Managed UI reads the native WDA result from Preview stats. On success, the monitor stays visible while recording.
- On failure, the existing safe fallback minimizes the monitor for Recording rather than silently recording it. Stop restores the prior Normal/Maximized state.
- No capture rewrite or speculative fallback was added.

## 5. RECORDING ISOLATION

- `RequestResizeAsync` only submits the latest viewport width/height to `XbPreview_Resize`.
- Native `PreviewEngine::Resize` only changes requested preview width/height and the preview swap-chain resize generation.
- OutputCanvas creation, frame tap, encoder configuration, H.264, audio, recording timeline, camera state, Follow, Director ownership, and Safe Publish are unchanged.
- A targeted fake-native lifecycle test starts Recording, resizes the monitor, and proves: no Recording Stop/Finalize, active Recording remains active, `SessionGeometry` and `OutputCanvas` are unchanged, and the only native call caused by resize is `native:resize:1440x810`.

## 6. TESTS

Passed:

- Visual Studio MSBuild `Release|x64` full solution build.
- `XbPreview.Managed.Tests.exe --resizable-director-monitor`.
  - standard resize/maximize/restore contract;
  - useful minimum size and no maximum;
  - preview viewport grows in both dimensions;
  - recording presentation keeps Preview visible and preserves window size/state;
  - capture exclusion success keeps the monitor visible;
  - exclusion failure uses safe minimize/restore fallback;
  - OutputCanvas aspect fit/letterbox calculation;
  - Manual presets, Director Lite, Follow, and recording-resize isolation.
- `XbPreview.Managed.Tests.exe --minimal-shell`.
- `XbPreview.Managed.Tests.exe --director-lite`.
- `XbPreview.Managed.Tests.exe --camera-motion`.

Environment-limited checks, not treated as product regressions:

- The native default suite stopped in its pre-existing GPU tap priming test (`PrimeTapGeneration`) before reaching this slice's paths; the first frame was observed but no tap generation was created in this execution session.
- `--p2.7b-background-recording` and the real-MainForm portion of `--p2.5b-recording-controller` could not start a WGC Preview in the current desktop execution session.
- The managed default aggregate also expects the external sibling `p1c2-hotkey-toggle-prototype/P1C2-FROZEN-HASHES.txt`, which is not present in this workspace.
- No audio human gate, crash matrix, 60-minute run, or full history replay was run, as required by this slice.

## 7. CANDIDATE

- Baseline branch: `main`.
- Baseline HEAD: `0c9b62a448231e5172766ded00ec140f4ed327b2`.
- Candidate subject: `feat(mvp): add resizable director monitor`.
- Candidate is created only after the build and targeted tests above pass. The final handoff reports its immutable SHA and clean-worktree proof.
- No push, tag, or merge is part of this slice.

## 8. HUMAN EXPERIENCE GATE

Use the candidate Release x64 build in an interactive Windows desktop session:

1. Open the recorder and start Recording.
2. Drag an edge and a corner from the initial size to roughly half-screen, then roughly 80% of the screen.
3. Maximize, then restore.
4. Exercise Manual 1.6x, Manual 2.0x, and Return Wide.
5. Exercise Director Soft/Strong, Follow, and retarget.
6. Confirm the image is materially easier to direct, remains sharp, never stretches, and does not repeatedly black out or stall while resizing.
7. Stop normally and confirm the monitor is absent from the MP4 when WDA succeeds. If WDA is unavailable, confirm the explicit safe minimize fallback occurred instead of silently contaminating the MP4.

Ask only:

- Does the larger picture finally feel like a director monitor?
- Are camera moves now clear enough to judge?
- Does resizing feel like a normal browser window?
- Do maximize and restore behave normally?
- Is there obvious resize stutter or black-screen behavior?
- Is the experience beginning to feel fun?

## 9. VERDICT

`MVP-RESIZABLE-DIRECTOR-MONITOR-GATE-READY`

The implementation boundary is complete and the shortest human experience gate is prepared. Do not expand this slice into capture controls, button beautification, transparent click-through behavior, or a visual redesign before the human verdict.
