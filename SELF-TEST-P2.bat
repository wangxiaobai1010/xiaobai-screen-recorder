@echo off
setlocal
cd /d "%~dp0"

if /I "%~1"=="Baseline" goto :baseline
if /I "%~1"=="SpikeA1" goto :spike_a1
if /I "%~1"=="SpikeA2" goto :spike_a2
if /I "%~1"=="P2_3A" goto :p2_3a
if /I "%~1"=="P2_3B" goto :p2_3b
if /I "%~1"=="P2_4" goto :p2_4
goto :usage

:common_start
for %%I in ("%CD%") do set "CURRENT_DIRECTORY_NAME=%%~nxI"
if /I not "%CURRENT_DIRECTORY_NAME%"=="p2-video-recording-closed-loop-prototype" (
  echo ERROR: SELF-TEST-P2 must run from the canonical P2 directory.
  exit /b 2
)
set "MSBUILD=C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe"
if not exist "%MSBUILD%" (
  echo ERROR: VS MSBuild not found.
  exit /b 1
)
exit /b 0

:residual
powershell -NoProfile -ExecutionPolicy Bypass -Command "$p=@(Get-Process -ErrorAction SilentlyContinue | Where-Object {$_.ProcessName -like 'XbPreview*' -or $_.ProcessName -like 'P2.SpikeA*' -or $_.ProcessName -like 'P2.QualityAB*'}); if($p.Count -ne 0){$p | Format-Table ProcessName,Id; exit 1}"
exit /b %ERRORLEVEL%

:regression
"%MSBUILD%" "XbPreview.P1D-A1.sln" /m /restore /t:Build /p:Configuration=Release /p:Platform=x64 /v:minimal
if errorlevel 1 exit /b 1
dotnet "artifacts\bin\Release\x64\XbPreview.Managed.Tests.dll"
if errorlevel 1 exit /b 1
"artifacts\bin\Release\x64\XbPreview.Native.Tests.exe"
if errorlevel 1 exit /b 1
if not exist "artifacts\bin\Release\x64\XbPreview.Host.exe" exit /b 1
if not exist "artifacts\bin\Release\x64\XbPreview.Native.dll" exit /b 1
exit /b 0

:baseline
if not "%~2"=="" goto :usage
call :common_start
if errorlevel 1 exit /b 1
echo [1/6] Residual process check
call :residual
if errorlevel 1 exit /b 1
echo [2/6] P2 starting baseline and current scope verification
powershell -NoProfile -ExecutionPolicy Bypass -File "VERIFY-P2-STATIC.ps1" Baseline
if errorlevel 1 exit /b 1
echo [3/6] Main solution and inherited tests
call :regression
if errorlevel 1 exit /b 1
echo [4/6] P2 Baseline recheck
powershell -NoProfile -ExecutionPolicy Bypass -File "VERIFY-P2-STATIC.ps1" Baseline
if errorlevel 1 exit /b 1
echo [5/6] Residual process recheck
call :residual
if errorlevel 1 exit /b 1
echo [6/6] Unique Baseline conclusion
echo SELF-TEST-P2 Baseline: PASS
exit /b 0

:spike_a1
if not "%~2"=="" goto :usage
call :common_start
if errorlevel 1 exit /b 1
echo [1/14] Residual XbPreview and Spike process check
call :residual
if errorlevel 1 exit /b 1
echo [2/14] P2 starting 175-file Baseline verification
powershell -NoProfile -ExecutionPolicy Bypass -File "VERIFY-P2-STATIC.ps1" Baseline
if errorlevel 1 exit /b 1
echo [3/14] P2.2A exact scope and static verification
powershell -NoProfile -ExecutionPolicy Bypass -File "VERIFY-P2-STATIC.ps1" SpikeA1
if errorlevel 1 exit /b 1
echo [4/14] Main solution Release x64 build and inherited tests
call :regression
if errorlevel 1 exit /b 1
echo [5/14] Independent Spike Release x64 build
"%MSBUILD%" "spikes\P2.2A-MfSinkWriterGpuFrame\P2.SpikeA.MfSinkWriterGpuFrame.vcxproj" /m /t:Build /p:Configuration=Release /p:Platform=x64 /v:minimal
if errorlevel 1 exit /b 1
echo [6/14] Three independent Spike lifecycles
powershell -NoProfile -ExecutionPolicy Bypass -File "spikes\P2.2A-MfSinkWriterGpuFrame\RUN-P2.2A-SPIKE.ps1" -PoolSize 6 -RunCount 3
set "SPIKE_RESULT=%ERRORLEVEL%"
if "%SPIKE_RESULT%"=="20" exit /b 1
if not "%SPIKE_RESULT%"=="0" if not "%SPIKE_RESULT%"=="10" exit /b 1
echo [7/14] Three-result artifact validation
powershell -NoProfile -ExecutionPolicy Bypass -Command "$s=Get-ChildItem 'artifacts\spikes\P2.2A\spike-a1-*-summary.json' | Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1; if($null -eq $s){exit 1}; $j=Get-Content -Raw $s.FullName|ConvertFrom-Json; if($j.RunCount -ne 3 -or @($j.Runs).Count -ne 3 -or $j.Classification -eq 'INVALID_EXPERIMENT'){exit 1}; if($j.Classification -eq 'SUPPORTED'){foreach($r in $j.Runs){if(-not(Test-Path -LiteralPath $r.FinalFilePath)-or $r.SubmittedFrames-ne 150-or $r.DroppedFrames-ne 0-or $r.TrackedReturned-ne 150-or $r.OutstandingTrackedSamples-ne 0){exit 1}}}"
if errorlevel 1 exit /b 1
echo [8/14] Product Runtime and P1d-a2 frozen source recheck
powershell -NoProfile -ExecutionPolicy Bypass -File "VERIFY-P2-STATIC.ps1" SpikeA1
if errorlevel 1 exit /b 1
echo [9/14] P2 Baseline remains reproducible
powershell -NoProfile -ExecutionPolicy Bypass -File "VERIFY-P2-STATIC.ps1" Baseline
if errorlevel 1 exit /b 1
echo [10/14] Main solution and inherited tests remain green
call :regression
if errorlevel 1 exit /b 1
echo [11/14] Final Spike static verification
powershell -NoProfile -ExecutionPolicy Bypass -File "VERIFY-P2-STATIC.ps1" SpikeA1
if errorlevel 1 exit /b 1
echo [12/14] Residual process check
call :residual
if errorlevel 1 exit /b 1
echo [13/14] Product runtime fingerprint is unchanged
powershell -NoProfile -ExecutionPolicy Bypass -File "VERIFY-P2-STATIC.ps1" SpikeA1
if errorlevel 1 exit /b 1
echo [14/14] Unique Spike A1 conclusion
if "%SPIKE_RESULT%"=="0" (
  echo SELF-TEST-P2 SpikeA1: PASS-SUPPORTED
) else (
  echo SELF-TEST-P2 SpikeA1: PASS-UNSUPPORTED
)
exit /b 0

:usage
echo ERROR: Usage: SELF-TEST-P2.bat Baseline ^| SpikeA1 ^| SpikeA2 ^| P2_3A ^| P2_3B ^| P2_4
echo PreAcceptance and PostAcceptance remain protected until their P2 stages exist.
exit /b 2

:spike_a2
if not "%~2"=="" goto :usage
call :common_start
if errorlevel 1 exit /b 1
echo [1/12] Residual process check
call :residual
if errorlevel 1 exit /b 1
echo [2/12] Baseline, A1, and A2 gates
powershell -NoProfile -ExecutionPolicy Bypass -File "VERIFY-P2-STATIC.ps1" Baseline
if errorlevel 1 exit /b 1
powershell -NoProfile -ExecutionPolicy Bypass -File "VERIFY-P2-STATIC.ps1" SpikeA1
if errorlevel 1 exit /b 1
powershell -NoProfile -ExecutionPolicy Bypass -File "VERIFY-P2-STATIC.ps1" SpikeA2
if errorlevel 1 exit /b 1
echo [3/12] Main solution and inherited tests
call :regression
if errorlevel 1 exit /b 1
echo [4/12] A1 project build
"%MSBUILD%" "spikes\P2.2A-MfSinkWriterGpuFrame\P2.SpikeA.MfSinkWriterGpuFrame.vcxproj" /m /t:Build /p:Configuration=Release /p:Platform=x64 /v:minimal
if errorlevel 1 exit /b 1
echo [5/12] A2 project build
"%MSBUILD%" "spikes\P2.2B-D3D11VideoProcessorNv12\P2.SpikeA2.D3D11VideoProcessorNv12.vcxproj" /m /t:Build /p:Configuration=Release /p:Platform=x64 /v:minimal
if errorlevel 1 exit /b 1
echo [6/12] Three A2 runs
powershell -NoProfile -ExecutionPolicy Bypass -File "spikes\P2.2B-D3D11VideoProcessorNv12\RUN-P2.2B-SPIKE.ps1" -RunCount 3
set "A2_RESULT=%ERRORLEVEL%"
if "%A2_RESULT%"=="20" exit /b 1
echo [7/12] One independent A1 control run
powershell -NoProfile -ExecutionPolicy Bypass -File "spikes\P2.2A-MfSinkWriterGpuFrame\RUN-P2.2A-SPIKE.ps1" -PoolSize 6 -RunCount 1
if errorlevel 1 exit /b 1
echo [8/12] Final A2 gate
powershell -NoProfile -ExecutionPolicy Bypass -File "VERIFY-P2-STATIC.ps1" SpikeA2
if errorlevel 1 exit /b 1
echo [9/12] Product runtime and upstream recheck
powershell -NoProfile -ExecutionPolicy Bypass -File "VERIFY-P2-STATIC.ps1" Baseline
if errorlevel 1 exit /b 1
echo [10/12] Residual process check
call :residual
if errorlevel 1 exit /b 1
echo [11/12] Output evidence check
powershell -NoProfile -ExecutionPolicy Bypass -Command "$s=Get-ChildItem 'artifacts\spikes\P2.2B\spike-a2-*-summary.json'|Sort-Object LastWriteTimeUtc -Descending|Select-Object -First 1;if($null-eq$s){exit 1};$j=Get-Content -Raw -Encoding UTF8 $s.FullName|ConvertFrom-Json;if($j.RunCount-ne3-or$j.Classification-eq'INVALID_EXPERIMENT'){exit 1}"
if errorlevel 1 exit /b 1
echo [12/12] Unique A2 conclusion
if "%A2_RESULT%"=="0" (echo SELF-TEST-P2 SpikeA2: PASS-SUPPORTED) else (echo SELF-TEST-P2 SpikeA2: PASS-UNSUPPORTED)
exit /b 0

:p2_3a
if not "%~2"=="" goto :usage
call :common_start
if errorlevel 1 exit /b 1
echo [1/10] Residual process check
call :residual
if errorlevel 1 exit /b 1
echo [2/10] P2.3A scope, historical A2 baseline, and upstream verification
powershell -NoProfile -ExecutionPolicy Bypass -File "VERIFY-P2-STATIC.ps1" P2_3A
if errorlevel 1 exit /b 1
echo [3/10] Main solution Release x64 build and inherited tests
call :regression
if errorlevel 1 exit /b 1
echo [4/10] Independent A1 control build and run
powershell -NoProfile -ExecutionPolicy Bypass -File "spikes\P2.2A-MfSinkWriterGpuFrame\RUN-P2.2A-SPIKE.ps1" -PoolSize 6 -RunCount 1
if errorlevel 1 exit /b 1
echo [5/10] Independent A2 control build and run
powershell -NoProfile -ExecutionPolicy Bypass -File "spikes\P2.2B-D3D11VideoProcessorNv12\RUN-P2.2B-SPIKE.ps1" -RunCount 1
if errorlevel 1 exit /b 1
echo [6/10] OutputCanvas static and formal-set recheck
powershell -NoProfile -ExecutionPolicy Bypass -File "VERIFY-P2-STATIC.ps1" P2_3A
if errorlevel 1 exit /b 1
echo [7/10] Main solution regression recheck
call :regression
if errorlevel 1 exit /b 1
echo [8/10] Frozen upstream and ProductRuntime scope recheck
powershell -NoProfile -ExecutionPolicy Bypass -File "VERIFY-P2-STATIC.ps1" P2_3A
if errorlevel 1 exit /b 1
echo [9/10] Residual process check
call :residual
if errorlevel 1 exit /b 1
echo [10/10] Unique P2.3A conclusion
echo SELF-TEST-P2 P2_3A: PASS
exit /b 0

:p2_3b
if not "%~2"=="" goto :usage
call :common_start
if errorlevel 1 exit /b 1
echo [1/11] Residual process check
call :residual
if errorlevel 1 exit /b 1
echo [2/11] P2.3B scope, P2.3A historical baseline, and frozen upstream verification
powershell -NoProfile -ExecutionPolicy Bypass -File "VERIFY-P2-STATIC.ps1" P2_3B
if errorlevel 1 exit /b 1
echo [3/11] Main solution Debug x64 build
"%MSBUILD%" "XbPreview.P1D-A1.sln" /m /restore /t:Build /p:Configuration=Debug /p:Platform=x64 /v:minimal
if errorlevel 1 exit /b 1
echo [4/11] Main solution Release x64 build and inherited tests
call :regression
if errorlevel 1 exit /b 1
echo [5/11] Thirty-second GPU Tap diagnostic
"artifacts\bin\Release\x64\XbPreview.Native.Tests.exe" --p2.3b-diagnostics
if errorlevel 1 exit /b 1
echo [6/11] Independent A1 control build and run
powershell -NoProfile -ExecutionPolicy Bypass -File "spikes\P2.2A-MfSinkWriterGpuFrame\RUN-P2.2A-SPIKE.ps1" -PoolSize 6 -RunCount 1
if errorlevel 1 exit /b 1
echo [7/11] Independent A2 control build and run
powershell -NoProfile -ExecutionPolicy Bypass -File "spikes\P2.2B-D3D11VideoProcessorNv12\RUN-P2.2B-SPIKE.ps1" -RunCount 1
if errorlevel 1 exit /b 1
echo [8/11] P2.3B and P2.3A OutputCanvas static recheck
powershell -NoProfile -ExecutionPolicy Bypass -File "VERIFY-P2-STATIC.ps1" P2_3B
if errorlevel 1 exit /b 1
echo [9/11] Release regression recheck
call :regression
if errorlevel 1 exit /b 1
echo [10/11] Frozen upstream, bounded resource, and residual process recheck
powershell -NoProfile -ExecutionPolicy Bypass -File "VERIFY-P2-STATIC.ps1" P2_3B
if errorlevel 1 exit /b 1
call :residual
if errorlevel 1 exit /b 1
echo [11/11] Unique P2.3B conclusion
echo SELF-TEST-P2 P2_3B: PASS
exit /b 0

:p2_4
if not "%~2"=="" goto :usage
for %%I in ("%CD%") do set "CURRENT_DIRECTORY_NAME=%%~nxI"
if /I not "%CURRENT_DIRECTORY_NAME%"=="p2-4-outputcanvas-encoding-prototype" (
  echo ERROR: P2_4 self-test must run from the canonical P2.4 directory.
  exit /b 2
)
set "MSBUILD=C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe"
if not exist "%MSBUILD%" (
  echo ERROR: VS MSBuild not found.
  exit /b 1
)
echo [1/14] Residual process check
call :residual
if errorlevel 1 exit /b 1
echo [2/14] P2.4 exact scope, architecture, and frozen-source verification
powershell -NoProfile -ExecutionPolicy Bypass -File "VERIFY-P2-STATIC.ps1" P2_4
if errorlevel 1 exit /b 1
echo [3/14] Main solution Debug x64 build
"%MSBUILD%" "XbPreview.P1D-A1.sln" /m /restore /t:Build /p:Configuration=Debug /p:Platform=x64 /v:minimal
if errorlevel 1 exit /b 1
echo [4/14] Main solution Release x64 build
"%MSBUILD%" "XbPreview.P1D-A1.sln" /m /restore /t:Build /p:Configuration=Release /p:Platform=x64 /v:minimal
if errorlevel 1 exit /b 1
echo [5/14] Native and Managed regression tests
"artifacts\bin\Release\x64\XbPreview.Native.Tests.exe"
if errorlevel 1 exit /b 1
dotnet "artifacts\bin\Release\x64\XbPreview.Managed.Tests.dll"
if errorlevel 1 exit /b 1
echo [6/14] Product CropTransform pixel-exact and encoding-quality regression
powershell -NoProfile -ExecutionPolicy Bypass -File "spikes\P2.4Q-CropUvQualityAB\RUN-P2.4Q-CROP-UV-AB.ps1"
if errorlevel 1 exit /b 1
echo [7/14] Five-second-equivalent product-module GPU encoding integration
"artifacts\bin\Release\x64\XbPreview.Native.Tests.exe" --p2.4-consumer-lifecycle
if errorlevel 1 exit /b 1
"artifacts\bin\Release\x64\XbPreview.Native.Tests.exe" --p2.4-integration
if errorlevel 1 exit /b 1
echo [8/14] Thirty-second-equivalent product-module GPU encoding integration
"artifacts\bin\Release\x64\XbPreview.Native.Tests.exe" --p2.4-thirty-second
if errorlevel 1 exit /b 1
echo [9/14] P2.3B bounded Tap diagnostic regression
"artifacts\bin\Release\x64\XbPreview.Native.Tests.exe" --p2.3b-diagnostics
if errorlevel 1 exit /b 1
echo [10/14] Independent P2.2A control run
powershell -NoProfile -ExecutionPolicy Bypass -File "spikes\P2.2A-MfSinkWriterGpuFrame\RUN-P2.2A-SPIKE.ps1" -PoolSize 6 -RunCount 1
if errorlevel 1 exit /b 1
echo [11/14] Independent P2.2B control run
powershell -NoProfile -ExecutionPolicy Bypass -File "spikes\P2.2B-D3D11VideoProcessorNv12\RUN-P2.2B-SPIKE.ps1" -RunCount 1
if errorlevel 1 exit /b 1
echo [12/14] Final P2.4 governance and frozen-source recheck
powershell -NoProfile -ExecutionPolicy Bypass -File "VERIFY-P2-STATIC.ps1" P2_4
if errorlevel 1 exit /b 1
echo [13/14] Residual process check
call :residual
if errorlevel 1 exit /b 1
echo [14/14] Unique P2.4 conclusion
echo SELF-TEST-P2 P2_4: PASS
exit /b 0
