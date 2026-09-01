[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$hostExe = Join-Path $repoRoot 'artifacts\bin\Release\x64\XbPreview.Host.exe'
if (-not (Test-Path -LiteralPath $hostExe -PathType Leaf)) {
    throw 'Release x64 Host is missing. Build the targeted Host project first.'
}

$runRoot = Join-Path $repoRoot (
    'artifacts\formal-home-integration\' +
    (Get-Date -Format 'yyyyMMdd-HHmmss-fff') + '-' +
    [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $runRoot -Force | Out-Null

$fixture = $null
try {
    $fixture = Start-Process -FilePath $hostExe `
        -ArgumentList '--formal-home-window-fixture' `
        -WindowStyle Normal `
        -PassThru
    Start-Sleep -Milliseconds 900
    if ($fixture.HasExited) {
        throw 'The real-window fixture exited before enumeration.'
    }

    $scenarios = @(
        'controls',
        'idle-close',
        'recording-close',
        'paused-close'
    )
    $evidenceFiles = @()
    foreach ($scenario in $scenarios) {
        $scenarioRoot = Join-Path $runRoot $scenario
        $outputRoot = Join-Path $scenarioRoot 'recordings'
        $evidence = Join-Path $scenarioRoot 'evidence.json'
        New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null

        $process = Start-Process -FilePath $hostExe `
            -ArgumentList @(
                '--formal-avalonia-home-gate',
                $scenario,
                $evidence,
                $outputRoot
            ) `
            -WindowStyle Normal `
            -PassThru `
            -Wait
        if ($process.ExitCode -ne 0) {
            throw "Formal Home $scenario process failed: $($process.ExitCode)"
        }
        if (-not (Test-Path -LiteralPath $evidence -PathType Leaf)) {
            throw "Formal Home $scenario produced no evidence."
        }
        $facts = Get-Content -LiteralPath $evidence -Raw -Encoding UTF8 |
            ConvertFrom-Json
        if ($facts.Status -ne 'PASS') {
            throw (
                "Formal Home $scenario failed: " +
                (($facts.Failures | ForEach-Object { [string]$_ }) -join '; '))
        }
        $evidenceFiles += $evidence
        Write-Output "FORMAL-HOME-$($scenario.ToUpperInvariant()) = PASS"
    }

    $controls = Get-Content -LiteralPath $evidenceFiles[0] `
        -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($controls.Recording1.FileExists -ne $true -or
        $controls.Recording2.FileExists -ne $true -or
        $controls.Recording1.FinalizeCount -ne 1 -or
        $controls.Recording2.FinalizeCount -ne 1) {
        throw 'Completed restart evidence did not retain both finalized MP4 files.'
    }

    $summary = [ordered]@{
        Status = 'PASS'
        RunRoot = $runRoot
        Evidence = $evidenceFiles
        CompletedRestartMp4 = @(
            $controls.Recording1.OutputPath,
            $controls.Recording2.OutputPath
        )
        CompletedUtc = [DateTimeOffset]::UtcNow
    }
    $summaryPath = Join-Path $runRoot 'summary.json'
    $summary | ConvertTo-Json -Depth 8 |
        Set-Content -LiteralPath $summaryPath -Encoding UTF8
    Write-Output 'FORMAL-HOME-PRODUCTION-INTEGRATION = PASS'
    Write-Output "EVIDENCE = $summaryPath"
}
finally {
    if ($null -ne $fixture -and -not $fixture.HasExited) {
        Stop-Process -Id $fixture.Id -Force -ErrorAction SilentlyContinue
        $null = $fixture.WaitForExit(5000)
    }
}
