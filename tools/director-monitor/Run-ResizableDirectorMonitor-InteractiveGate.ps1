[CmdletBinding()]
param(
    [switch]$PreflightOnly
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0

$productCandidate = '1f2ed4970017b07c8d53874b6dd112f1fd3f8110'
$runnerRelativePath = 'tools/director-monitor/Run-ResizableDirectorMonitor-InteractiveGate.ps1'
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
    Read-Host '完成后回到本终端按 Enter 继续' | Out-Null
}

Push-Location $repoRoot
try {
    $branch = (& git branch --show-current).Trim()
    if ($LASTEXITCODE -ne 0 -or $branch -ne 'main') {
        throw '真人体验 Gate 只允许从 main 候选运行。'
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

    $currentHead = (& git rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0) {
        throw '无法读取当前 HEAD。'
    }

    Write-Host "BRANCH: $branch" -ForegroundColor DarkCyan
    Write-Host "PRODUCT CANDIDATE: $productCandidate" -ForegroundColor DarkCyan
    Write-Host "TOOLING HEAD: $currentHead" -ForegroundColor DarkCyan
    Write-Host "RELEASE EXE: $hostExe" -ForegroundColor DarkCyan

    if ($PreflightOnly) {
        Write-Host 'PREFLIGHT PASS: 未启动产品，未开始真人录制。' -ForegroundColor Green
        return
    }

    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    if (-not $principal.IsInRole(
            [Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw '请在“以管理员身份运行”的 PowerShell 中执行本命令。'
    }

    Write-Host ''
    Write-Host '大画面导演监视器真人体验 Gate' `
        -ForegroundColor White -BackgroundColor DarkBlue
    Write-Host '本轮只判断 resize、最大化、导演感、画面比例、黑屏卡顿、成片排除与 Stop/Close。' `
        -ForegroundColor Cyan
    Write-Host 'Runner 不会自动点击、不会自动开始录制；所有产品操作都由你完成。' `
        -ForegroundColor Green

    $started = Get-Date
    $process = Start-Process -FilePath $hostExe -PassThru
    Start-Sleep -Seconds 3
    if ($process.HasExited) {
        throw '小白录屏器启动后提前退出。'
    }

    Wait-HumanStep '【请先观察当前窗口大小】'
    Wait-HumanStep '【请用鼠标拖动窗口右下角，把小白录屏器放大到约半屏】'
    Wait-HumanStep '【继续拖大到约屏幕的 80%】'
    Wait-HumanStep '【现在点击右上角“最大化”】'
    Wait-HumanStep '【观察 Preview 是否真正变大，并保持画面比例】'
    Wait-HumanStep '【请体验手动 1.6x】'
    Wait-HumanStep '【请体验手动 2.0x】'
    Wait-HumanStep '【请开启自动跟随重点，体验 Soft / Strong Director】'
    Wait-HumanStep '【请选择本次录制要保留的 Director 强度，点击“开始录制”，完成倒计时】'
    Wait-HumanStep '【请点击左右不同区域，并观察 Follow / retarget】'

    Write-Host ''
    Write-Host '【停止操作约 4 秒，观察是否回到 Wide】' `
        -ForegroundColor White -BackgroundColor DarkBlue
    Start-Sleep -Seconds 4
    Read-Host '观察完成后按 Enter 继续' | Out-Null

    Wait-HumanStep '【请点击还原窗口，再自由拖动改变大小】'
    Wait-HumanStep '【现在停止录制并等待保存完成】'

    $video = Get-ChildItem -LiteralPath $recordingRoot -Filter *.mp4 `
        -File -Recurse -ErrorAction SilentlyContinue |
        Where-Object { $_.LastWriteTime -ge $started } |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1
    if ($null -eq $video) {
        throw '未找到本次真人 Gate 发布的 MP4；请检查产品中的失败状态或恢复材料。'
    }

    Write-Host ''
    Write-Host 'HUMAN CHECK MP4:' -ForegroundColor Green
    Write-Host $video.FullName -ForegroundColor Green
    Write-Host '请检查成片中是否出现导演监视器窗口，并回想 resize 时是否拉伸、黑屏或明显卡顿。' `
        -ForegroundColor Cyan

    Wait-HumanStep '【请正常关闭小白录屏器，然后回终端按 Enter】'
    $process.Refresh()
    if (-not $process.HasExited) {
        throw '小白录屏器尚未正常关闭；请关闭后重新完成 Gate 收尾。'
    }

    Write-Host 'HUMAN GATE SESSION COMPLETE：请记录真人体验结论；这不是产品 FINAL PASS。' `
        -ForegroundColor Green
}
finally {
    Pop-Location
}
