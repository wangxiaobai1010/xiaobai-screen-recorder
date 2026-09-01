[CmdletBinding()]
param(
    [switch]$PreflightOnly
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$hostExe = Join-Path $repoRoot 'artifacts\bin\Release\x64\XbPreview.Host.exe'
$recordingRoot = Join-Path $repoRoot 'artifacts\p2.5a-recordings'

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
if (-not $PreflightOnly -and -not $principal.IsInRole(
        [Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw '请在“以管理员身份运行”的 PowerShell 中执行本命令。'
}
if (-not (Test-Path -LiteralPath $hostExe -PathType Leaf)) {
    throw "Release x64 主程序不存在：$hostExe"
}

Push-Location $repoRoot
try {
    if ((git branch --show-current) -ne 'main') {
        throw '真人产品 Gate 只允许从 main 候选运行。'
    }
    if (git status --porcelain) {
        throw '真人产品 Gate 要求 immutable candidate：worktree 必须 clean。'
    }

    $candidate = (git rev-parse HEAD).Trim()
    if ($PreflightOnly) {
        Write-Host "PREFLIGHT PASS: $candidate" -ForegroundColor Green
        return
    }
    $started = Get-Date
    Write-Host "MVP 极简录屏产品壳候选：$candidate" -ForegroundColor Cyan
    Write-Host ''
    Write-Host '请只在应用内完成下面这条普通用户路径：' -ForegroundColor Yellow
    Write-Host '1. 看首页，确认第一眼知道从哪里开始。'
    Write-Host '2. 分别切换电脑声音与麦克风。'
    Write-Host '3. 开关“自动跟随重点”，查看柔和/强调与手动倍率互斥。'
    Write-Host '4. 点击“开始录制”，观察 3、2、1 倒计时。'
    Write-Host '5. 录制一小段，确认 REC、真实时长、镜头状态与 Stop 清楚。'
    Write-Host '6. 点击“停止并保存”，等待真正完成。'
    Write-Host '7. 依次使用“打开视频”“打开文件夹”。'
    Write-Host '8. 正常关闭小白录屏器。'
    Write-Host ''
    Write-Host '请判断：声音与自动跟随文案是否易懂、锁定是否自然、倒计时与 Stop 是否好找、完成后是否知道视频在哪里，以及整体是否像真正的软件。' -ForegroundColor DarkCyan

    $process = Start-Process -FilePath $hostExe -PassThru
    $process.WaitForExit()

    $video = Get-ChildItem -LiteralPath $recordingRoot -Filter *.mp4 `
        -File -Recurse -ErrorAction SilentlyContinue |
        Where-Object LastWriteTime -ge $started |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1
    if ($null -eq $video) {
        throw '未找到本次产品 Gate 发布的 MP4；请检查应用显示的失败状态或恢复材料。'
    }
    Write-Host "HUMAN CHECK MP4: $($video.FullName)" -ForegroundColor Green
}
finally {
    Pop-Location
}
