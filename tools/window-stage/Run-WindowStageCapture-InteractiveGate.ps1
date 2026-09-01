[CmdletBinding()]
param(
    [switch]$PreflightOnly
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$hostExe = Join-Path $repoRoot 'artifacts\bin\Release\x64\XbPreview.Host.exe'
$recordingRoot = Join-Path $repoRoot 'artifacts\p2.5a-recordings'

function Wait-HumanStep {
    param([Parameter(Mandatory = $true)][string]$Message)
    Write-Host ''
    Write-Host $Message -ForegroundColor Black -BackgroundColor Yellow
    Read-Host '完成后回到本终端按 Enter 继续' | Out-Null
}

Push-Location $repoRoot
try {
    $branch = (& git branch --show-current).Trim()
    if ($LASTEXITCODE -ne 0 -or $branch -ne 'main') {
        throw 'Window Stage 真人 Gate 只允许从 main 候选运行。'
    }
    $worktree = @(& git status --porcelain --untracked-files=all)
    if ($LASTEXITCODE -ne 0 -or $worktree.Count -ne 0) {
        throw '真人 Gate 要求 immutable candidate：worktree 必须 clean。'
    }
    if (-not (Test-Path -LiteralPath $hostExe -PathType Leaf)) {
        throw "Release x64 主程序不存在：$hostExe"
    }

    $candidate = (& git rev-parse HEAD).Trim()
    Write-Host "CANDIDATE: $candidate" -ForegroundColor DarkCyan
    Write-Host "RELEASE EXE: $hostExe" -ForegroundColor DarkCyan
    if ($PreflightOnly) {
        Write-Host 'PREFLIGHT PASS：未启动产品，唯一真人 Gate READY。' `
            -ForegroundColor Green
        return
    }

    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    if (-not $principal.IsInRole(
            [Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw '请在“以管理员身份运行”的 PowerShell 中执行本命令。'
    }

    Write-Host ''
    Write-Host 'MVP Window Stage Capture 真人 Gate（约 2–4 分钟）' `
        -ForegroundColor White -BackgroundColor DarkBlue
    Write-Host '本 Gate 不检查最小化，不宣布 FINAL PASS，也不测试 3D。' `
        -ForegroundColor Cyan

    $started = Get-Date
    $process = Start-Process -FilePath $hostExe -PassThru
    Start-Sleep -Seconds 3
    if ($process.HasExited) {
        throw '小白录屏器启动后提前退出。'
    }

    Wait-HumanStep '1. 在“录制范围”选择“窗口”，选择一个普通浏览器/VS Code/Explorer 主窗口。'
    Wait-HumanStep '2. 开始录制；确认 Preview 是干净暖白背景，Window Card 居中，桌面其它软件未出现。'
    Wait-HumanStep '3. 移动真实目标窗口；确认成片舞台中的 Card 仍稳定居中。'
    Wait-HumanStep '4. resize 目标窗口；确认 Card 等比重新 fit，无拉伸、脏边或录制重启。'
    Wait-HumanStep '5. 使用 F9/F10 与 1.6x/2.0x；确认 Manual / Follow 正常。'
    Wait-HumanStep '6. 开启 Director Soft/Strong；点击目标窗口左右区域，确认 focus、retarget 正常。'
    Write-Host '请停止操作约 4 秒，确认 Return Wide。' -ForegroundColor Cyan
    Start-Sleep -Seconds 4
    Read-Host '观察完成后按 Enter 继续' | Out-Null
    Wait-HumanStep '7. 点击目标窗口外（录屏器、其它应用或桌面）；确认 Director 不会无故 retarget。'
    Wait-HumanStep '8. Stop 并等待保存完成，然后在产品内点击“打开视频”。'

    $video = Get-ChildItem -LiteralPath $recordingRoot -Filter *.mp4 `
        -File -Recurse -ErrorAction SilentlyContinue |
        Where-Object { $_.LastWriteTime -ge $started } |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1
    if ($null -eq $video) {
        throw '未找到本次真人 Gate 发布的 MP4；请检查产品失败状态或恢复材料。'
    }

    Write-Host ''
    Write-Host 'HUMAN CHECK MP4:' -ForegroundColor Green
    Write-Host $video.FullName -ForegroundColor Green
    Write-Host ''
    Write-Host '真人只回答：' -ForegroundColor Yellow
    Write-Host '1. “只录一个窗口 + 干净背景”是不是想要的效果？'
    Write-Host '2. 桌面其它软件是否确实没有进入成片？'
    Write-Host '3. Window Card 大小是否舒服？'
    Write-Host '4. 移动真实窗口时，最终舞台是否稳定？'
    Write-Host '5. resize 是否自然？'
    Write-Host '6. F9/F10 / Director 在窗口模式下是否正常？'
    Write-Host '7. 鼠标是否只有一个且位置自然？'
    Write-Host '8. 有没有明显影响下一步 3D Window Motion 的问题？'
    Write-Host ''
    Write-Host '可选：关闭目标窗口做一次极短安全检查，确认产品不 crash。' `
        -ForegroundColor DarkCyan
    Write-Host 'HUMAN GATE SESSION COMPLETE：请记录真人结论；这不是 FINAL PASS。' `
        -ForegroundColor Green
}
finally {
    Pop-Location
}
