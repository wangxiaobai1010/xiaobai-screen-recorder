# Xiaobai Recorder packaging

The production installer is defined by `inno/XiaobaiRecorder.iss`. Its stable
Inno Setup identity is `{5C6A42C6-A978-46D3-9F71-A02C6F4CC9EA}` and must be
reused by all future 1.x installers.

The script consumes the audited deployment closure at
`artifacts/packaging/xiaobai-recorder-1.0.0/app` and writes
`artifacts/packaging/installer/XiaobaiRecorder-Setup-1.0.0.exe`.

The only official v1.0.0 app-stage entry point is
`tools/release/New-XiaobaiReleaseAppStage.ps1`. It performs a locked offline
restore, Release x64 native build, .NET 8.0.29 self-contained publish, exact
GStreamer/FFmpeg/VC++ assembly, app-stage bridge, SHA-256 manifest, and
fail-closed compliance gate. Its external dependency paths are explicit
parameters and every selected release input is checked against
`tools/release/release-inputs.v1.0.0.json`.

The source lock records both the verified product source commit and the frozen
release-foundation commit. Current HEAD must descend from that foundation, and
every later tracked path must be either under
`docs/release/compliance/v1.0.0/` or the exact root
`THIRD-PARTY-NOTICES.md`; every other product, runtime, or release-foundation
change fails closed. Normal operation requires a clean tree. One reviewed,
untracked root notice can be admitted with `-ThirdPartyNoticesCandidate` for
pre-freeze validation. While this lock fix itself is under review,
`-ReleaseFoundationCandidateSelfTest` additionally admits only the three exact
authorized foundation-fix paths and requires the notice candidate mode.

The command intentionally stops with exit code 2 after producing a valid
app-stage when required release compliance material is incomplete. Review
`artifacts/packaging/xiaobai-recorder-1.0.0/compliance-report.md`; do not run
Inno Setup unless that report says `Overall: PASS`.

Compile it with the current official Inno Setup 6 compiler:

```powershell
& "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe" `
  "packaging\inno\XiaobaiRecorder.iss"
```

The installer is per-machine, targets 64-bit Windows, supports English and
Simplified Chinese, and owns only the application files, shortcuts, uninstall
identity, and the per-application WER LocalDumps key. User settings, recordings,
custom assets, and existing crash dump files are outside installer ownership.

`inno/Languages/ChineseSimplified.isl` is pinned from the official
`jrsoftware/issrc` repository at commit
`1ae7bf81dc0d2013235dfe4bb0b6f4e4a0b6b25c` (SHA-256
`E0B0B350E2245F3C5E65586DFE43D574F6E7F06F2261149ABA284954B3FC9A8D`).
