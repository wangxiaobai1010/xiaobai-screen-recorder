# MVP Minimal Recording Shell v1

Date: 2026-08-09

Baseline: `86560419ea19d88c1c8b2631a6b1afdb7f857952`

Candidate commit message: `feat(mvp): add minimal recording shell`

## 1. REUSED PRODUCT CORE

The shell remains the existing WinForms `XbPreview.Host/MainForm`. It reuses the
existing Preview lifecycle, full-screen WGC capture, `RecordingController`,
`ManagedRecordingSnapshot`, OutputCanvas/MP4 encoder, Safe Publish and recovery,
background recording, audio controls, Manual 1.6x/2.0x camera, Follow, Director
Lite, and Soft/Strong focus strength. No capture, encoder, audio, publish, camera
motion, or ownership pipeline was duplicated.

## 2. UI STRUCTURE

One medium vertical window answers the MVP questions in order:

1. capture range: full screen, the only available target;
2. computer sound and microphone;
3. Manual camera or “自动跟随重点”;
4. Manual 1.6x/2.0x or Director Soft/Strong;
5. the visually dominant “开始录制” action.

The existing preview remains embedded. The old Preview start/stop, hotkey,
custom-region, cursor, WGC/QPC/ABI and diagnostic-log controls are not in the
product visual tree. They remain internal implementation assets rather than a
second application or a new framework.

## 3. RECORDING STATES

The media state and elapsed time come from `ManagedRecordingSnapshot`. The only
shell-local transient state is the three-second pre-start countdown.

| Product state | Source fact | User presentation |
| --- | --- | --- |
| Idle | Snapshot Idle | 准备就绪 |
| Countdown | single-flight shell countdown | 3, 2, 1 |
| Starting / Recording | Snapshot Starting or Recording | REC, Snapshot elapsed time, Stop |
| Stopping | Snapshot Stopping | 正在安全保存 |
| Completed | Completed plus OutputSuccess, ReadyToPublish and Published | 录制完成 and output actions |
| Failed | Snapshot Failed, or Completed without publish truth | visible short failure; never fake Completed |

The 500 ms UI refresh only rereads facts; it is not a media clock. A delayed UI
refresh therefore catches up to `Snapshot.Elapsed`.

## 4. AUDIO CONTROLS

“电脑声音” and “麦克风” map directly to the existing system/microphone mute
controls with the mature unity microphone gain. No audio graph is stopped or
restarted. First launch reflects the established default: both sources enabled.
Failures are reread from the native control facts and shown as a short product
message.

## 5. CAMERA CONTROLS

The default is Manual, Wide 1.0x. With “自动跟随重点” off, the existing Manual
1.6x and 2.0x commands remain available during recording. With it on, Manual
buttons leave the user path and the existing DirectorLite owner is enabled.
Soft 1.6x remains the default and Strong 2.0x is optional. Director strength and
owner selection lock once countdown/recording starts. Camera ownership remains
exactly Manual or DirectorLite; omega, Follow, clamp, retarget and inactivity are
unchanged.

## 6. COUNTDOWN

Start enters one non-blocking three-second countdown. An interlocked action gate
rejects repeat Start requests. Only after 3, 2, 1 does the shell call the existing
`RecordingController.StartAsync`; there is no second Start or recording state
machine. Close cancels and disposes a pending countdown.

## 7. RECORDING BAR

Once the Snapshot reports Starting/Recording, the window collapses to a quiet
recording strip with semantic red used only for REC/Stop. It shows real elapsed
time, Stop, and either “手动重点放大” with 1.6x/2.0x or “自动跟随重点 · 柔和/强调”.

The primary capture-safety path reuses the exclusion window already supplied to
the WGC session and its `WDA_EXCLUDEFROMCAPTURE` fact. If native stats report that
exclusion failed, the sole fallback minimizes the product window immediately
before real Start; the taskbar restores the same Stop entry. Capture architecture
is unchanged.

## 8. COMPLETION / FAILURE

Stop remains the existing single-flight `RecordingController.StopAsync` path.
The shell does not show completion until Finalize, validation and Safe Publish
facts are successful. “打开视频” and “打开文件夹” continue to use the real
`PublishedPath` and existing `RecordingOutputActions`. Failure stays visible and
recovery material is not deleted by the shell.

## 9. VISUAL DIRECTION

The v1 direction is “Quiet Retro-Future Instrument”: warm gray paper, charcoal
text, thin neutral borders, restrained instrument-like controls, generous
spacing, and signal red reserved for REC, Stop and failure. It intentionally does
not introduce a design system, gradients, glass, decorative textures, or a card
wall.

## 10. TESTS

Verified on the Release x64 output:

- solution Release x64 Build: PASS;
- `--minimal-shell`: PASS (single-flight Start/countdown/Stop, Snapshot states and
  elapsed time, publish-truth completion, visible failure, independent audio mute
  mapping, Manual/Director controls, Soft/Strong mapping and recording lock,
  default product-shell visual tree);
- `--p2.6a3-publish-mapping`: PASS (real published-path action policy, restart,
  failure, Stop/Close and MainForm presentation seams);
- `--director-lite`: PASS;
- `--camera-motion`: PASS;
- `--p2.8e-audio-controls`: PASS.

An additional real-media `--p2.5b-recording-controller` attempt was made. The
current automation desktop returned Win32 1060 from
`GraphicsCaptureSession::IsSupported` before Preview entered Recording, so this
environment-only attempt produced no product assertion or media result. The
required deterministic shell, publish, Director, camera-motion and audio suites
above are the candidate evidence; the unique product Gate is the actual desktop
media check.

## 11. CANDIDATE

All implementation, targeted tests, this report and the unique Runner are frozen
in the single commit named `feat(mvp): add minimal recording shell`. Its exact
content-addressed SHA is reported by Git after the commit is created; embedding a
commit's own SHA in its contents is not possible. The Gate requires `main` and a
clean worktree before launch.

## 12. HUMAN GATE

The one product Gate is:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "E:\小白录屏器\xiaobai-screen-recorder\tools\minimal-shell\Run-MinimalShell-ProductGate.ps1"
```

The Runner only preflights and launches the immutable candidate, presents the
single ordinary-user checklist, waits for normal app close, and prints:

```text
HUMAN CHECK MP4: <published path>
```

The human judges first-glance Start clarity, sound labels, “自动跟随重点”,
Soft/Strong meaning, Manual lockout, countdown comfort, REC/Stop visibility,
output discovery, and whether the shell feels like a real product.

## 13. DEFERRED FEATURES

Window capture plus clean background remains the next capture-scope MVP work,
after this shell Gate. Custom region is outside MVP. Pause/Resume, tray, camera,
device/resolution/FPS/bitrate selection, settings and persistence, installer,
update, login/cloud, editor, annotation, privacy mode and the full visual system
remain deferred.

## 14. VERDICT

`MVP-MINIMAL-SHELL-GATE-READY`

This is a human product-experience Gate candidate, not a FINAL PASS. No window
capture work starts in this change.
