[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$hostExe = Join-Path $repoRoot 'artifacts\bin\Release\x64\XbPreview.Host.exe'
$recordingRoot = Join-Path $repoRoot 'artifacts\p2.5a-recordings'

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw '请在“以管理员身份运行”的 PowerShell 中执行本命令。'
}
if (-not (Test-Path -LiteralPath $hostExe -PathType Leaf)) {
    throw "Release x64 主程序不存在：$hostExe"
}

function Run-ShortSession {
    param(
        [Parameter(Mandatory)]
        [string]$Label,

        [Parameter(Mandatory)]
        [string]$Description,

        [Parameter(Mandatory)]
        [string[]]$Arguments
    )

    Write-Host ''
    Write-Host "准备 $Label：$Description" -ForegroundColor Black -BackgroundColor Cyan
    $started = Get-Date
    $process = Start-Process -FilePath $hostExe -ArgumentList $Arguments -PassThru
    Start-Sleep -Seconds 6

    Write-Host '【请点击“开始录制”，确认后回到本终端按 Enter】' -ForegroundColor Yellow -BackgroundColor DarkRed
    Read-Host | Out-Null
    Write-Host '【请点击屏幕左侧】' -ForegroundColor Black -BackgroundColor Yellow
    Start-Sleep -Seconds 4
    Write-Host '【请点击屏幕右侧】' -ForegroundColor Black -BackgroundColor Yellow
    Start-Sleep -Seconds 4
    Write-Host '【请正常移动鼠标】' -ForegroundColor Black -BackgroundColor Cyan
    Start-Sleep -Seconds 5
    Write-Host '【请停止操作，等待自动回全景】' -ForegroundColor White -BackgroundColor DarkBlue
    Start-Sleep -Seconds 5
    Write-Host '【请点击“停止录制”，等待 Finalize / Publish 完成，然后按 Enter】' -ForegroundColor Yellow -BackgroundColor DarkRed
    Read-Host | Out-Null

    $video = Get-ChildItem -LiteralPath $recordingRoot -Filter *.mp4 -File -Recurse -ErrorAction SilentlyContinue |
        Where-Object LastWriteTime -ge $started |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1
    if ($null -eq $video) {
        throw "$Label 未找到本次发布的 MP4；请检查录制状态区和错误信息。"
    }

    Write-Host "HUMAN CHECK $Label MP4: $($video.FullName)" -ForegroundColor Green
    Write-Host '【请正常关闭小白录屏器；关闭后按 Enter】' -ForegroundColor Black -BackgroundColor Yellow
    Read-Host | Out-Null
    if (-not $process.HasExited) {
        throw "$Label 主程序尚未关闭；请正常关闭后重新完成本组收尾。"
    }
}

Push-Location $repoRoot
try {
    if ((git branch --show-current) -ne 'main') {
        throw '真人 A/B Gate 只允许从 main 候选运行。'
    }
    if (git status --porcelain) {
        throw '真人 A/B Gate 要求 immutable candidate：worktree 必须 clean。'
    }

    Run-ShortSession `
        -Label 'SOFT' `
        -Description '柔和 1.6x（默认）' `
        -Arguments @('--director-lite')
    Run-ShortSession `
        -Label 'STRONG' `
        -Description '强调 2.0x' `
        -Arguments @('--director-lite', '--director-focus-strong')
}
finally {
    Pop-Location
}
