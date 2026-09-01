# ENV-WGC-1060 Diagnosis

## WINDOWS BUILD

- Windows Pro 23H2, build `22631.2861` (`10.0.22631`).
- Active interactive user: `WIN-E2826GDME7R\Administrator`, console session `1`.
- The Codex sandbox process runs in the same session under a different token: `WIN-E2826GDME7R\CodexSandboxOffline` (SID ending `-1001`); Administrator's SID ends `-500`.

## WGC ERROR

- The existing P2.8B Gate 1 evidence records `GraphicsCaptureSession::IsSupported` failing before Preview/Recording initialization with HRESULT `0x80070424`, Win32 `1060`.
- Microsoft defines Win32 1060 (`ERROR_SERVICE_DOES_NOT_EXIST`) as “the specified service does not exist as an installed service.”
- Gate 1 and Gate 2 were not rerun in this diagnostic.

## CAPTURESERVICE TEMPLATE

- SCM template `CaptureService` exists and is queryable.
- Configuration: `USER_SHARE_PROCESS TEMPLATE`, Manual/demand start (`Start=3`), account `NT AUTHORITY\LocalService`, dependency `RpcSs`, image `%SystemRoot%\system32\svchost.exe -k LocalService -p`.
- Registry template exists at `HKLM\SYSTEM\CurrentControlSet\Services\CaptureService`.
- `Parameters\ServiceDll` is `C:\Windows\System32\CaptureService.dll`; the file exists, is Microsoft-signed, and reports version `10.0.22621.1`.
- The stopped template reports Win32 exit `1077` (not started since boot), not 1060. No missing or malformed template evidence was found.

## PER-USER INSTANCE

- SCM and the registry contain one CaptureService instance: `CaptureService_c24c5`, Manual, currently stopped, with instance type `USER_SHARE_PROCESS INSTANCE`.
- The suffix `_c24c5` is shared by the other per-user services on this machine; several of those instances are running. This is the per-user instance set associated with the normal interactive login context.
- No CaptureService instance exists for the separate `CodexSandboxOffline` logon token used by the sandboxed diagnostic/Gate process.
- Microsoft documents that per-user service instances are created at sign-in, use names of the form `<service>_<LUID>`, and share the same LUID suffix for one user context. `CaptureService` is documented as the OneCore Capture Service used by Windows.Graphics.Capture.

## MINIMAL WGC REPRO

The independent probe called only:

```powershell
[Windows.Graphics.Capture.GraphicsCaptureSession,Windows.Graphics.Capture,ContentType=WindowsRuntime]::IsSupported()
```

- Under `CodexSandboxOffline`: reproducibly throws inner HRESULT `0x80070424` with the message that the specified service is not installed.
- Under the active interactive `Administrator` token, on the same machine and session: returns `True`.
- `CaptureService_c24c5` remained stopped when the Administrator probe succeeded; no service was manually started and no configuration was changed.
- Therefore the failure is independently reproducible without P2.8B, WASAPI, miniaudio, AAC, MP4, or the product recording pipeline, and is isolated to the sandbox/offline user-service context.

## EVENT / SERVICE FACTS

- A narrow System/Application event-log query around the recorded Gate failure found no directly related CaptureService or Windows.Graphics.Capture event.
- No dedicated enabled CaptureService/WGC event channel containing a matching error was found.
- Template and instance security descriptors are present. The system DLL and service registration are observable and internally consistent.
- No registry, service, Windows setting, login session, or service state was changed during this investigation.

## ROOT CAUSE

**A — `CAPTURESERVICE-INSTANCE-MISSING`.**

The missing object is the per-user `CaptureService` instance for the artificial `CodexSandboxOffline` logon token. The machine-level template and the interactive Administrator instance are healthy. The decisive counterexample is that the identical minimal API call returns `True` under Administrator while it returns 1060 under the sandbox token on the same machine/session.

This is not `CAPTURESERVICE-TEMPLATE-MISSING-OR-BROKEN`, and current evidence does not support `SESSION-REFRESH-REQUIRED`: logout/reboot is unnecessary to explain the observed split and would not correct launching the validation under the wrong user token.

Official references:

- Microsoft Learn: [Per-user services in Windows](https://learn.microsoft.com/en-us/windows/application-management/per-user-services-in-windows)
- Microsoft Learn: [Screen capture / GraphicsCaptureSession.IsSupported](https://learn.microsoft.com/en-us/windows/apps/develop/media-authoring-processing/screen-capture)
- Microsoft Learn: [System error codes 1000–1299](https://learn.microsoft.com/en-us/windows/win32/debug/system-error-codes--1000-1299-)

## MINIMAL SAFE NEXT ACTION

In a separately authorized validation run, launch Gate 1 from the normal interactive `Administrator` context so WGC resolves the existing `CaptureService_c24c5` instance. Do not create/delete/start services, edit the registry or StartType, run DISM/SFC, log out, or reboot for the current evidence.

If a future minimal probe run from the normal Administrator context fails, reopen environment diagnosis before considering a session refresh or system repair. That condition was not observed here.

## PRODUCT CODE IMPACT

- None identified. The current P2.8B product diff contains no references to CaptureService, SCM service creation/control, or Windows service registry configuration.
- The failure occurs at the WGC capability check before the P2.8B audio path starts.
- No P2.8B, P2.7, P2.6, test, or Windows configuration code was modified to obtain this conclusion.

## FINAL VERDICT

`ENV-WGC-ROOT-CAUSE-FOUND`

Root-cause classification: **A — `CAPTURESERVICE-INSTANCE-MISSING`**.
