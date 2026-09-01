[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$runner = Join-Path $PSScriptRoot 'Run-DirectorLite-InteractiveGate.ps1'
$null = [scriptblock]::Create((Get-Content -Raw -LiteralPath $runner))
$text = Get-Content -Raw -LiteralPath $runner
foreach ($required in @(
    '--director-lite',
    '【现在请点击屏幕左侧的一个位置】',
    '【现在请点击屏幕右侧的一个位置】',
    '【现在请正常移动鼠标，让镜头跟随】',
    '【现在请停止操作，观察镜头是否自然回到全景】',
    'HUMAN CHECK MP4:',
    'worktree 必须 clean'
)) {
    if ($text.IndexOf($required, [StringComparison]::Ordinal) -lt 0) {
        throw "Runner 缺少冻结契约：$required"
    }
}
Write-Host 'Director Lite runner static PASS'
