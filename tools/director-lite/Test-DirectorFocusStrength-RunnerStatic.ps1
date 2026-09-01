[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$runner = Join-Path $PSScriptRoot 'Run-DirectorFocusStrength-InteractiveGate.ps1'
$text = Get-Content -Raw -LiteralPath $runner
$null = [scriptblock]::Create($text)

foreach ($required in @(
    "-Label 'SOFT'",
    "-Label 'STRONG'",
    "-Arguments @('--director-lite')",
    "-Arguments @('--director-lite', '--director-focus-strong')",
    '【请点击屏幕左侧】',
    '【请点击屏幕右侧】',
    '【请正常移动鼠标】',
    '【请停止操作，等待自动回全景】',
    'HUMAN CHECK $Label MP4:',
    'worktree 必须 clean'
)) {
    if ($text.IndexOf($required, [StringComparison]::Ordinal) -lt 0) {
        throw "Focus Strength Runner 缺少冻结契约：$required"
    }
}

if (([regex]::Matches($text, 'Run-ShortSession')).Count -ne 3) {
    throw 'Focus Strength Runner 必须定义一次并运行两个独立 Session。'
}

Write-Host 'Director Focus Strength A/B runner static PASS'
