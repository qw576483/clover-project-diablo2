# 回归全部离线自检宿主：dotnet run 每个 tools/<name>check，输出末行结论
param(
    # 可选：把每个宿主的**完整输出**逐份落盘到该目录（<host>.log，UTF-8）。空 = 只打汇总。
    [string]$FullLogDir = ''
)

# 项目根 = hosts → probes → tools → <项目根>（clover-project-diablo2）。
# `-FullLogDir` 的相对路径以**项目根**为基准解析：⛔ 不按调用者 CWD —— CWD 随调用环境
# 漂移，会让日志落到意料之外的位置。经 .NET IO API 的路径一律先绝对化。
$projRoot = Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $PSScriptRoot))
if ($FullLogDir -ne '') {
    if (-not [IO.Path]::IsPathRooted($FullLogDir)) { $FullLogDir = Join-Path $projRoot $FullLogDir }
    $FullLogDir = [IO.Path]::GetFullPath($FullLogDir)
    New-Item -ItemType Directory -Force -Path $FullLogDir | Out-Null
    Write-Output ("full logs -> " + $FullLogDir)
}

# 读宿主原始输出：先按 UTF-8 严格解码，失败再按 GBK 回落 —— 两条路都**绕开 PowerShell
# 的原生输出解码器**，那正是 乱码事故的源头：PS 5.1 用 OEM/控制台代码页解码
# 外部程序输出，而宿主在「有控制台 / 被重定向」两种上下文里输出的编码还不一样，于是同一天
# 的日志里出现两种乱码（UTF-8 按 GBK 解 / GBK 按 UTF-8 解，后者丢字节、不可完全恢复）。
function Read-NativeLog([string]$path) {
    $bytes = [IO.File]::ReadAllBytes($path)
    try {
        $strict = New-Object System.Text.UTF8Encoding($false, $true)
        return $strict.GetString($bytes)
    } catch {
        return [System.Text.Encoding]::GetEncoding(936).GetString($bytes)
    }
}
# auto-discovered (gate-close-X): the old hard-coded list held 12 entries while
# tools/probes/hosts/ carries 14 .csproj -- movecheck / savecheck were therefore never run
# by the one-click entry at all.  Discovery by csproj kills that class of omission for good.
$hosts = @(Get-ChildItem $PSScriptRoot -Directory -ErrorAction SilentlyContinue |
           Where-Object { Test-Path (Join-Path $_.FullName ($_.Name + '.csproj')) } |
           ForEach-Object { $_.Name } | Sort-Object)
$fail = 0
foreach ($h in $hosts) {
    $dir = Join-Path $PSScriptRoot $h
    if (-not (Test-Path $dir)) { Write-Output ("{0,-12} SKIP (no dir)" -f $h); continue }
    Push-Location $dir
    # cmd 重定向 = 字节级透传（不经 PS 的解码器）；读取走 Read-NativeLog 显式解码。
    $tmpLog = [IO.Path]::GetTempFileName()
    cmd /c "dotnet run -v q --nologo > `"$tmpLog`" 2>&1"
    $code = $LASTEXITCODE
    Pop-Location
    $out = Read-NativeLog $tmpLog
    if ($FullLogDir -ne '') { Move-Item -Force $tmpLog (Join-Path $FullLogDir ($h + '.log')) }
    else { Remove-Item -Force $tmpLog }
    $lines = ($out -split "`r?`n") | Where-Object { $_ -match '自检|通过|失败|FAIL|error' } | Select-Object -Last 3
    $status = if ($code -eq 0) { 'PASS' } else { 'FAIL' }
    if ($code -ne 0) { $fail++ }
    Write-Output ("{0,-12} exit={1} {2}" -f $h, $code, $status)
    foreach ($l in $lines) { Write-Output ("             | " + $l.Trim()) }
}
Write-Output ("TOTAL_HOSTS=" + $hosts.Count + " FAILED=" + $fail)
