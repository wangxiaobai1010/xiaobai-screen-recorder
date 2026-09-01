[CmdletBinding()]
param(
    [switch]$PreflightOnly
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0

$productCandidate = 'b9d3dbe656f62dbfdc2105aaa49ea3959759e5ce'
$runnerRelativePath = 'tools/director-monitor/Run-DirectorMonitorExperience-InteractiveGate.ps1'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$hostExe = Join-Path $repoRoot 'artifacts\bin\Release\x64\XbPreview.Host.exe'
$recordingRoot = Join-Path $repoRoot 'artifacts\p2.5a-recordings'

function Wait-HumanStep {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Message
    )

    Write-Host ''
    Write-Host $Message -ForegroundColor Black -BackgroundColor Yellow
    Read-Host '完成后按 Enter 继续' | Out-Null
}

Push-Location $repoRoot
try {
    $branch = (& git branch --show-current).Trim()
    if ($LASTEXITCODE -ne 0 -or $branch -ne 'main') {
        throw '真人体验 Gate 只允许从 main 候选运行。'
    }

    $currentHead = (& git rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0) {
        throw '无法读取当前 HEAD。'
    }

    $worktree = @(& git status --porcelain --untracked-files=all)
    if ($LASTEXITCODE -ne 0 -or $worktree.Count -ne 0) {
        throw '真人体验 Gate 要求 immutable candidate：worktree 必须 clean。'
    }

    & git cat-file -e "${productCandidate}^{commit}" 2>$null
    if ($LASTEXITCODE -ne 0) {
        throw "产品 candidate 不存在：$productCandidate"
    }

    & git merge-base --is-ancestor $productCandidate HEAD
    if ($LASTEXITCODE -ne 0) {
        throw "当前 HEAD 无法追溯到产品 candidate：$productCandidate"
    }

    $changesAfterCandidate = @(
        & git diff --name-only "${productCandidate}..HEAD"
    )
    if ($LASTEXITCODE -ne 0) {
        throw '无法核对产品 candidate 之后的 tooling 变化。'
    }
    $unexpectedChanges = @(
        $changesAfterCandidate |
            Where-Object { $_ -ne $runnerRelativePath }
    )
    if ($unexpectedChanges.Count -ne 0) {
        throw (
            '产品 candidate 之后存在非 Runner 变化：' +
            ($unexpectedChanges -join ', ')
        )
    }

    if (-not (Test-Path -LiteralPath $hostExe -PathType Leaf)) {
        throw "Release x64 主程序不存在：$hostExe"
    }

    Write-Host "BRANCH: $branch" -ForegroundColor DarkCyan
    Write-Host "PRODUCT CANDIDATE: $productCandidate" -ForegroundColor DarkCyan
    Write-Host "TOOLING HEAD: $currentHead" -ForegroundColor DarkCyan
    Write-Host "RELEASE EXE: $hostExe" -ForegroundColor DarkCyan

    if ($PreflightOnly) {
        Write-Host 'PREFLIGHT PASS: 未启动产品，未开始真人 Gate。' `
            -ForegroundColor Green
        return
    }

    Write-Host ''
    Write-Host '大屏导演体验唯一真人 Gate' `
        -ForegroundColor White -BackgroundColor DarkBlue
    Write-Host '只判断 Preview 大小与稳定性、录制尺寸连续性、镜头快捷键和 Director 互斥。' `
        -ForegroundColor Cyan
    Write-Host 'Runner 不会自动点击或开始录制；所有产品操作均由你完成。' `
        -ForegroundColor Green

    $started = Get-Date
    $process = Start-Process -FilePath $hostExe -PassThru
    Start-Sleep -Seconds 3
    if ($process.HasExited) {
        throw '小白录屏器启动后提前退出。'
    }

    Wait-HumanStep '【录制前先把窗口拖大，观察 Preview 是否已经足够大】'
    Wait-HumanStep '【点击镜头相关区域，观察 Preview 是否仍稳定、不抖动】'
    Wait-HumanStep '【开始录制，确认窗口尺寸没有突然变化】'
    Wait-HumanStep '【开启镜头快捷键，按真实 F9 / F10，体验手动镜头】'
    Wait-HumanStep '【观察当前镜头状态文字是否同步】'

    Write-Host ''
    Write-Host '当前产品保留冻结规则：录制中不切换 Manual / Director。' `
        -ForegroundColor Cyan
    Wait-HumanStep '【请先停止并保存 Manual 体验段，再开启自动跟随重点；随后开始一个极短 Director 体验段】'
    Wait-HumanStep '【开启自动跟随重点，确认 F9/F10 暂时不能接管】'
    Wait-HumanStep '【请停止并保存 Director 体验段】'
    Wait-HumanStep '【关闭自动跟随重点，确认快捷键恢复】'
    Wait-HumanStep '【把窗口拖到半屏、约 80%、最大化，再还原】'
    Wait-HumanStep '【停止并保存，然后正常关闭小白录屏器】'

    $process.Refresh()
    if (-not $process.HasExited) {
        throw '小白录屏器尚未正常关闭；请关闭后重新完成 Gate 收尾。'
    }

    $video = Get-ChildItem -LiteralPath $recordingRoot -Filter *.mp4 `
        -File -Recurse -ErrorAction SilentlyContinue |
        Where-Object { $_.LastWriteTime -ge $started } |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1
    if ($null -eq $video) {
        throw '未找到本次真人 Gate 发布的 MP4；请检查录制状态或恢复材料。'
    }

    Write-Host ''
    Write-Host 'HUMAN CHECK MP4:' -ForegroundColor Green
    Write-Host $video.FullName -ForegroundColor Green
    Write-Host ''
    Write-Host '【请回到终端按 Enter】' `
        -ForegroundColor Black -BackgroundColor Yellow
    Read-Host | Out-Null

    Write-Host 'HUMAN GATE SESSION COMPLETE：请记录真人体验结论；这不是产品 FINAL PASS。' `
        -ForegroundColor Green
}
finally {
    Pop-Location
}
