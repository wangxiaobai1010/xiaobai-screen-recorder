[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$hostExe = Join-Path $repoRoot 'artifacts\bin\Release\x64\XbPreview.Host.exe'
$recordingRoot = Join-Path $repoRoot 'artifacts\p2.5a-recordings'
$diagnosticRoot = Join-Path $repoRoot 'artifacts\bin\Release\x64\diagnostic-logs'

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw '请在“以管理员身份运行”的 PowerShell 中执行本命令。'
}
if (-not (Test-Path -LiteralPath $hostExe -PathType Leaf)) {
    throw "Release x64 主程序不存在：$hostExe"
}

Push-Location $repoRoot
try {
    if ((git branch --show-current) -ne 'main') {
        throw '真人 Gate 只允许从 main 候选运行。'
    }
    if (git status --porcelain) {
        throw '真人 Gate 要求 immutable candidate：worktree 必须 clean。'
    }

    $started = Get-Date
    $process = Start-Process -FilePath $hostExe -ArgumentList '--director-lite' -PassThru
    Start-Sleep -Seconds 8

    Write-Host ''
    Write-Host '【请在小白录屏器中点击“开始录制”，确认开始后回到本终端按 Enter】' -ForegroundColor Yellow -BackgroundColor DarkRed
    Read-Host | Out-Null
    Write-Host '【现在请点击屏幕左侧的一个位置】' -ForegroundColor Black -BackgroundColor Yellow
    Start-Sleep -Seconds 7
    Write-Host '【现在请点击屏幕右侧的一个位置】' -ForegroundColor Black -BackgroundColor Yellow
    Start-Sleep -Seconds 7
    Write-Host '【现在请正常移动鼠标，让镜头跟随】' -ForegroundColor Black -BackgroundColor Cyan
    Start-Sleep -Seconds 10
    Write-Host '【现在请停止操作，观察镜头是否自然回到全景】' -ForegroundColor White -BackgroundColor DarkBlue
    Start-Sleep -Seconds 7
    Write-Host '【请点击“停止录制”，等待 Finalize / Publish 完成，然后回到本终端按 Enter】' -ForegroundColor Yellow -BackgroundColor DarkRed
    Read-Host | Out-Null

    $video = Get-ChildItem -LiteralPath $recordingRoot -Filter *.mp4 -File -Recurse -ErrorAction SilentlyContinue |
        Where-Object LastWriteTime -ge $started |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1
    if ($null -eq $video) {
        throw '未找到本次 Gate 发布的 MP4；请检查录制状态区和错误信息。'
    }

    Write-Host "HUMAN CHECK MP4: $($video.FullName)" -ForegroundColor Green
    Get-ChildItem -LiteralPath $diagnosticRoot -File -ErrorAction SilentlyContinue |
        Where-Object { $_.LastWriteTime -ge $started -and $_.Name -match 'camera|follow' } |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 4 |
        ForEach-Object { Write-Host "CAMERA DIAGNOSTIC: $($_.FullName)" }

    Write-Host '【请正常关闭小白录屏器，以验证 Close 后 observer 无残留；关闭后按 Enter】' -ForegroundColor Black -BackgroundColor Yellow
    Read-Host | Out-Null
    if (-not $process.HasExited) {
        throw '主程序尚未关闭；请正常关闭后重新完成 Gate 收尾。'
    }
}
finally {
    Pop-Location
}
