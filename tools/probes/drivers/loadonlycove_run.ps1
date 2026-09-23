# =============================================================================
# loadonlycove_run.ps1 -- 片 loadonly-cove 取证用（**只取证、不改产品代码**）。
#
#   powershell -NoProfile -ExecutionPolicy Bypass -File .ai-tmp/drivers/loadonlycove_run.ps1 -Tag lc1
#
#   两段式（两次 Play，一把锁，跑完一次采齐）：
#     Phase A（mode=walk）    ：创角 -> 进营地 -> 走 9 格(> 半径6) -> 开图截图 -> 保存并退出
#     Phase B（mode=loadonly）：**新一局 Play（内存全空）** -> BootDone -> 读档 -> 开图 -> 读数+截图
#   判据：Phase B 的 exploredCount 明显 > "本局新算的半径 6 兜底圈"，且图上距玩家 >6 格处
#         有已探索格被画出来（画面上只可能来自存档）。
#
#   骨架抄 .ai-tmp/drivers/saveprogress_run2.ps1（同锁协议 / 同 LOG-OFFSET 读法 / 同 run_script 管道）；
#   驱动 = .ai-tmp/drivers/loadonlycove_probe.cs（namespace LOC，一片一个文件）。
#
#   锁：只在"不存在 或 陈旧(>360s) 且 playMode != playing"时才拿；⛔ 绝不删别人的锁；每条退出路径都释放。
# =============================================================================
param(
    [string]$Tag  = 'lc1',
    # both | A | B  —— 只重跑某一相位时用（Phase A 的存档留在盘上，Phase B 可直接复用）
    [string]$Only = 'both',
    # 复用某个已存在的角色名（空 = 自动生成新名）
    [string]$Char = '',
    [string]$Why  = 'loadonly-cove: "读档回灌的已探索集"与"开图兜底半径6圈"必须在画面上可区分 —— 新一局 Play 内存全空只读档, 面板上的探索格只可能来自存档; 判据是画面(automap) => 离线宿主判不了'
)
$ErrorActionPreference = 'Continue'

# ---- self-proof: this script must PARSE (2026-09-24: a UTF-8-no-BOM .ps1 with CJK comments is read as GBK
#      by PowerShell 5.1 => CJK bytes swallow following quotes => syntax explodes). File is UTF-8 **with BOM**.
$__perr = $null
$null = [System.Management.Automation.Language.Parser]::ParseFile($MyInvocation.MyCommand.Path, [ref]$null, [ref]$__perr)
if ($__perr -and $__perr.Count -gt 0) { Write-Host 'PARSE-FAIL'; exit 3 }
Write-Host 'PARSE-OK'

$root    = Split-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) -Parent   # <repo>/clover-project-diablo2
$proj    = $root + '/client'
$cs      = $PSScriptRoot + '/loadonlycove_probe.cs'   # promoted copy lives next to this runner
$test    = $root + '/.ai-tmp/test'
$shots   = $root + '/.ai-tmp/screenshots/loadonlycove-' + $Tag   # per-tag dir: a re-run must NEVER overwrite another run's frames
$heart   = $test + '/heartbeat-loadonlycove.txt'
$done    = $test + '/lc_done_' + $Tag + '.txt'
$trace   = $test + '/lc_steps_' + $Tag + '.txt'
$frozen  = $test + '/lc_readings_' + $Tag + '.txt'
$outPath = $test + '/lc_out_' + $Tag + '.txt'
$logPath = $proj + '/Logs/Editor.log'
$playLog = $test + '/play-log.tsv'
$lock    = $test + '/play.lock'
$mine    = 'loadonly-cove'

# 每轮用**新角色名**（复用同名时 OnCharCreateSubmit 会以"名字已存在"拒绝创角 => 链会走偏）
$charName = if ($Char -ne '') { $Char } else { 'LOC' + (Get-Date -Format 'HHmmss') }

# 过滤正则**别漏关键词**（前片 `[SA]` 漏掉 [SP]/[App]/[Map] 白跑两次）——本片要 [LOC] 自己的行，
# 以及读档回灌链上的 [App](AppProgress) / [Map](MapModule) / [Ui](面板) / [Save] / [Flow]。
$keepRe = '\[LOC\]|\[App\]|\[Map\]|\[Ui\]|\[Save\]|\[Flow\]'

$script:offset = 0
$script:lines = New-Object System.Collections.ArrayList
$script:cli = 0
$script:out = New-Object System.Collections.ArrayList

function Say([string]$s) {
    $stamp = (Get-Date).ToString('HH:mm:ss.fff')
    Write-Host ($stamp + ' ' + $s)
    Add-Content -Path $trace -Value ($stamp + ' ' + $s) -Encoding UTF8
    [void]$script:out.Add($stamp + ' ' + $s)
    try { [System.IO.File]::WriteAllText($heart, ($stamp + ' [loadonly-cove] ' + $s)) } catch { }
}

function Init-Offset() {
    if (Test-Path $logPath) { $script:offset = (Get-Item $logPath).Length } else { $script:offset = 0 }
}

function Read-New() {
    if (-not (Test-Path $logPath)) { return }
    $fs = $null
    try {
        $fs = New-Object System.IO.FileStream($logPath, [System.IO.FileMode]::Open,
              [System.IO.FileAccess]::Read, [System.IO.FileShare]::ReadWrite)
        if ($fs.Length -le $script:offset) { return }
        $fs.Seek($script:offset, [System.IO.SeekOrigin]::Begin) | Out-Null
        $len = [int]($fs.Length - $script:offset)
        $buf = New-Object byte[] $len
        $read = $fs.Read($buf, 0, $len)
        $script:offset = $script:offset + $read
        $txt = [System.Text.Encoding]::UTF8.GetString($buf, 0, $read)
        $keep = @($txt -split "`r?`n" | Where-Object { $_ -match $keepRe })
        foreach ($l in $keep) { [void]$script:lines.Add($l) }
    } catch { } finally { if ($fs -ne $null) { $fs.Close(); $fs.Dispose() } }
}

function Lines-With([string]$pattern) { return @($script:lines | Where-Object { $_ -match $pattern }) }

function Release-Lock() {
    if (-not (Test-Path $lock)) { Say 'LOCK-RELEASE skip (no lock file)'; return }
    $body = ''
    try { $body = (Get-Content $lock -Raw) } catch { $body = '' }
    if ($body -match $mine) {
        Remove-Item -LiteralPath $lock -Force -ErrorAction SilentlyContinue
        Say 'LOCK-RELEASED (mine)'
    } else {
        Say ('LOCK-RELEASE REFUSED (owned by ' + $body.Trim() + ') -> left alone')
    }
}

function Clip([string]$s, [int]$n) {
    if ($null -eq $s) { return '' }
    $one = $s -replace "`r?`n", ' '
    if ($one.Length -le $n) { return $one }
    return $one.Substring(0, $n)
}

function Unity-Cmd([string[]]$argv, [switch]$Quiet) {
    $script:cli = $script:cli + 1
    $raw2 = & unity command @argv --project-path $proj --format json --no-pager 2>&1 | Out-String
    if (-not $Quiet) { Say ('CLI ' + ($argv -join ' ') + ' -> ' + $raw2.Length + ' bytes') }
    return $raw2
}

function Run-Step([string]$entry, [string]$arg) {
    if ([string]::IsNullOrEmpty($arg)) {
        $r = Unity-Cmd @('run_script', '--file', $cs, '--entry', $entry)
    } else {
        # ⚠️ 必须写成 `[\"...\"]`：PowerShell 传原生参数时会吃掉裸双引号
        $r = Unity-Cmd @('run_script', '--file', $cs, '--entry', $entry, '--args', ('[\"' + $arg + '\"]'))
    }
    $result = '?'
    try {
        $j = $r | ConvertFrom-Json
        $v = $j.data.result.result
        if ($null -eq $v) { $v = $j.data.result.error }
        if ($null -eq $v) {
            $d = @($j.data.result.diagnostics | Where-Object { $_.severity -eq 'error' })
            if ($d.Count -gt 0) { $v = 'COMPILE-ERRORS:' + $d.Count + ' ' + (Clip (($d | ForEach-Object { $_.id + ' line ' + $_.line + ': ' + $_.message }) -join ' | ') 500) }
        }
        if ([string]::IsNullOrEmpty([string]$v)) {
            try { $e0 = $j.errors[0].message; if ($null -ne $e0) { $v = 'CLI-ERROR: ' + $e0 } } catch { }
        }
        $result = [string]$v
    } catch { $result = 'PARSE-FAIL' }
    if ([string]::IsNullOrEmpty($result)) { $result = '(EMPTY)' }
    Say ('STEP ' + $entry + ' result=' + $result)
    return $result
}

function Wait-Recompile([string]$why) {
    Unity-Cmd @('recompile') -Quiet | Out-Null
    $st2 = '(unknown)'
    for ($i = 0; $i -lt 60; $i++) {
        Start-Sleep -Seconds 2
        $raw2 = Unity-Cmd @('recompile_status') -Quiet
        try { $j = $raw2 | ConvertFrom-Json; $st2 = (($j.data.result | ConvertFrom-Json).status) } catch { }
        if ($st2 -match 'up_to_date|completed|idle') { break }
    }
    Say ('RECOMPILE[' + $why + '] status=' + $st2)
    return $st2
}

# 一轮 Play：clear_console -> LOG-OFFSET -> editor_play -> ping -> Install(spec) -> 等 done -> console -> stop
function Play-Round([string]$label, [string]$mode, [string]$extra) {
    $d = $done + '.' + $mode
    if (Test-Path $d) { Remove-Item -LiteralPath $d -Force -ErrorAction SilentlyContinue }
    if (Test-Path ($d + '.side.txt')) { Remove-Item -LiteralPath ($d + '.side.txt') -Force -ErrorAction SilentlyContinue }

    Add-Content -Path $playLog -Value ((Get-Date).ToString('yyyy-MM-dd HH:mm') + "`tloadonly-cove`t" + $Tag + '-' + $mode + "`t" + $extra) -Encoding UTF8
    Say ('PLAYLOG-APPENDED ' + $mode)

    Unity-Cmd @('clear_console') -Quiet | Out-Null
    Init-Offset
    Say ('LOG-OFFSET[' + $label + '] ' + $script:offset)
    Unity-Cmd @('editor_play') -Quiet | Out-Null
    Start-Sleep -Seconds 8

    $ok = $false
    for ($i = 0; $i -lt 10; $i++) {
        $r = Run-Step 'LOC.Api.Ping' ''
        if ($r -match 'PONG') { $ok = $true; break }
        Start-Sleep -Seconds 3
    }
    Say ('GAME-RES-OK[' + $label + '] ' + $ok)
    if (-not $ok) { return $false }

    $spec = $charName + '|' + ($d -replace '\\', '/') + '|' + ($shots -replace '\\', '/') + '|' + $mode
    Say ('SPEC[' + $label + '] ' + $spec)
    $r2 = Run-Step 'LOC.Tour.Install' $spec
    Say ('INSTALL[' + $label + '] ' + (Clip $r2 300))

    # Phase A 最长 ~60s，Phase B ~40s；给足 240s
    # ⚠️ 必须只看**本轮**的完成信号：`$script:lines` 会跨轮累积 ⇒ 用 `Lines-With 'LOC-DONE'`
    #    在第二轮会立刻命中第一轮的 LOC-DONE（lc1 实测：Phase B 在 INSTALL 后 0.08s 就被判"完成"，
    #    Play 被立刻停掉，整轮白跑）。⇒ 本轮信号 = **本轮专属**的 side 文件 + marker 文件。
    $side = $d + '.side.txt'
    $t0 = Get-Date
    while (((Get-Date) - $t0).TotalSeconds -lt 240) {
        Read-New
        if (Test-Path $side) {
            $sc = ''
            try { $sc = [System.IO.File]::ReadAllText($side) } catch { }
            if ($sc -match 'LOC-DONE') { Say ('DRIVER-DONE-SEEN[' + $label + '] (side)'); break }
        }
        if (Test-Path $d) { Say ('DONE-MARKER-SEEN[' + $label + ']'); break }
        Start-Sleep -Milliseconds 500
    }
    Read-New
    for ($i = 0; $i -lt 12; $i++) {
        if ((Test-Path $d) -or ($script:lines.Count -gt 0)) { break }
        Start-Sleep -Seconds 3
        Read-New
    }
    Say ('LINES[' + $label + '] n=' + $script:lines.Count)

    $cj = Unity-Cmd @('console_status') -Quiet
    $errs = -1
    try { $j = $cj | ConvertFrom-Json; $errs = $j.data.result.counts.error } catch { }
    Say ('CONSOLE-STATUS[' + $label + '] errors=' + $errs)

    Unity-Cmd @('editor_stop') -Quiet | Out-Null
    Say ('STOPPED[' + $label + ']')
    # editor 停掉之后才能可靠读 Editor.log（Play 期间该文件被编辑器独占，前片两次读回 0 行）
    for ($i = 0; $i -lt 12; $i++) { Start-Sleep -Seconds 3; Read-New; if ($script:lines.Count -gt 0) { break } }
    Say ('POST-STOP-LINES[' + $label + '] n=' + $script:lines.Count)
    return $true
}

# =============================================================================
# main
# =============================================================================
foreach ($dir in @($shots, $test)) { if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir | Out-Null } }
Set-Content -Path $trace -Value ('# loadonly-cove steps tag=' + $Tag + ' char=' + $charName) -Encoding UTF8
$sessionStart = Get-Date
Say ('BEGIN tag=' + $Tag + ' char=' + $charName)

if (-not (Test-Path $cs)) { Say ('ABORT missing ' + $cs); exit 1 }
$pb = [System.IO.File]::ReadAllBytes($cs)
$na = 0
foreach ($b in $pb) { if ($b -gt 127) { $na++ } }
Say ('PROBE ' + (Split-Path $cs -Leaf) + ' bytes=' + $pb.Length + ' nonAscii=' + $na)

# ---- play lock -------------------------------------------------------------
for ($i = 0; $i -lt 15; $i++) {
    if (-not (Test-Path $lock)) { break }
    $age = ((Get-Date) - (Get-Item $lock).LastWriteTime).TotalSeconds
    if ($age -ge 360) {
        $st0 = Unity-Cmd @('editor_status') -Quiet
        $pm0 = '?'
        try { $j0 = $st0 | ConvertFrom-Json; $pm0 = $j0.data.result.playMode } catch { }
        if ($pm0 -ne 'playing') { Say ('LOCK-STALE age=' + [math]::Round($age, 0) + 's playMode=' + $pm0 + ' -> taking it'); break }
        Say ('LOCK-STALE but playMode=playing -> wait (try ' + ($i + 1) + ')')
    } else {
        Say ('LOCK-BUSY age=' + [math]::Round($age, 0) + 's owner=' + ((Get-Content $lock -Raw).Trim()) + ' -> wait 30s (try ' + ($i + 1) + ')')
    }
    Start-Sleep -Seconds 30
}
# ⛔ 绝不允许覆盖别人的锁（前片脚本在这里是无条件写的 => 会踩到持有者）
if (Test-Path $lock) {
    $ageN = ((Get-Date) - (Get-Item $lock).LastWriteTime).TotalSeconds
    if ($ageN -lt 360) {
        $bodyN = ''
        try { $bodyN = (Get-Content $lock -Raw).Trim() } catch { }
        if ($bodyN -notmatch $mine) {
            Say ('ABORT lock still held by ' + $bodyN + ' age=' + [math]::Round($ageN, 0) + 's -> 实机未取到（锁被占用）')
            [System.IO.File]::WriteAllLines($outPath, [string[]]$script:out, (New-Object System.Text.UTF8Encoding($false)))
            exit 2
        }
    }
}
Set-Content -Path $lock -Value ($mine + ' ' + (Get-Date).ToString('o')) -Encoding UTF8
Say ('LOCK-TAKEN ' + $lock)

for ($i = 0; $i -lt 48; $i++) {
    $st = Unity-Cmd @('editor_status') -Quiet
    try {
        $js = $st | ConvertFrom-Json
        $c = $js.data.result.compiling
        $pm = $js.data.result.playMode
        if ($c -eq $false -and $pm -eq 'stopped') { Say ('EDITOR-STATUS compiling=' + $c + ' playMode=' + $pm); break }
    } catch { }
    if ($i % 6 -eq 0) { Say ('EDITOR-WAIT compiling/playMode not ready yet (' + $i + ')') }
    Start-Sleep -Seconds 5
}
Unity-Cmd @('editor_stop') -Quiet | Out-Null
Start-Sleep -Seconds 2

$rc0 = Wait-Recompile 'preplay'
if ($rc0 -notmatch 'up_to_date|completed|idle') {
    Say 'ABORT recompile never settled -> editor_stop + release MY lock'
    Unity-Cmd @('editor_stop') -Quiet | Out-Null
    Release-Lock
    [System.IO.File]::WriteAllLines($outPath, [string[]]$script:out, (New-Object System.Text.UTF8Encoding($false)))
    exit 1
}
Start-Sleep -Seconds 3

# ---- Phase A ---------------------------------------------------------------
$okA = 'skipped'
if ($Only -ne 'B') {
Say '=== PHASE A (walk 9 cells > radius 6, then save & exit) ==='
$okA = Play-Round 'A' 'walk' 'phase A: create char -> town -> walk 9 cells away -> open automap (screenshot) -> SaveAndExit; needed in Play because the judgement is a picture (automap) and because the save file must really receive the explored mask'
Say ('PHASE-A-OK ' + $okA)
if ($okA) {
    # 直接读盘上的存档原文（不在 Play 里读，避免再进一次编辑器；写侧外证）
    $sf = $proj + '/setting/saves/' + $charName + '.json'
    if (Test-Path $sf) {
        $sj = [System.IO.File]::ReadAllText($sf)
        $keys = @('"areaId":', '"gridX":', '"gridY":', '"mapSeed":', '"visitedWaypoints":', '"exploredByArea":')
        $parts = @()
        foreach ($k in $keys) { if ($sj.Contains($k)) { $parts += ($k + 'FOUND') } else { $parts += ($k + 'MISSING') } }
        Say ('A-SAVE-FILE ' + $sf + ' len=' + $sj.Length + ' ' + ($parts -join ' '))
        $ni = $sj.IndexOf('"exploredByArea":')
        if ($ni -ge 0) { Say ('A-SAVE-EXPLORED ' + (Clip $sj.Substring($ni, [Math]::Min(400, $sj.Length - $ni)) 400)) }
    } else {
        Say ('A-SAVE-FILE MISSING ' + $sf)
    }
}
}   # if ($Only -ne 'B')

# ---- Phase B（新一局 Play：内存全空）----------------------------------------
$okB = 'skipped'
if ($Only -ne 'A') {
Say '=== PHASE B (FRESH Play, memory empty, load-only) ==='
$okB = Play-Round 'B' 'loadonly' 'phase B (decisive): brand-new Play session, memory has no progress memory at all -> load from the save file only -> open automap -> readings + screenshot; cells on screen can therefore only come from the save'
Say ('PHASE-B-OK ' + $okB)
}

Release-Lock

$hdr = New-Object System.Collections.ArrayList
[void]$hdr.Add('# loadonly-cove readings tag=' + $Tag + ' char=' + $charName)
[void]$hdr.Add('# session: ' + $sessionStart.ToString('yyyy-MM-dd HH:mm:ss') + '..' + (Get-Date).ToString('HH:mm:ss'))
[void]$hdr.Add('# run: powershell -NoProfile -ExecutionPolicy Bypass -File .ai-tmp/drivers/loadonlycove_run.ps1 -Tag ' + $Tag)
[void]$hdr.Add('# recompile = ' + $rc0 + '   phaseA=' + $okA + ' phaseB=' + $okB)
[void]$hdr.Add('# ---------------------------------------------------------------------------')
foreach ($l in $script:lines) { [void]$hdr.Add($l) }
[void]$hdr.Add('# ---------------------------------------------------------------------------')
[System.IO.File]::WriteAllLines($frozen, [string[]]$hdr, (New-Object System.Text.UTF8Encoding($false)))
Say ('FROZEN ' + $frozen + ' lines=' + $script:lines.Count)
Say ('END cli=' + $script:cli)
[System.IO.File]::WriteAllLines($outPath, [string[]]$script:out, (New-Object System.Text.UTF8Encoding($false)))
