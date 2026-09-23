# =============================================================================
# finalclose_hover_run.ps1 -- final-close slice (2026-09-24), step 2 take 2.
#
# WHY A SECOND RUN: the first run (finalclose_run.ps1 -Tag fc1) stopped after the
# G1b station, and the runtime log shows WHY that pair cannot be a hover pair:
#     [X] G1b-CLICK ... questIndex=-1 ... ClickDialogOption index=-1 -> skipped
# i.e. the tour never moved the pointer onto an option button in that run (the
# quest was already Done), so BOTH tiles show the buttons in the same state.
#
# THIS RUN: stop after the G1a station (NPC dialog OPEN, options visible, pointer
# still on Akara's ground cell), then drive the pointer with the existing
# X.Api2.ProbeMouse entry and take the frames with the CLI `screenshot` command
# while the editor is STILL IN PLAY:
#     normal  : pointer parked away from every button
#     hoverA  : pointer on the option rect, screen coords as used by the walk
#     hoverB  : same rect with the Y mirrored (the driver flips Y for some paths)
# so the true hover frame is decided by the picture, not by an assumption.
#
# Lock protocol: same Touch-Lock contract as automappanel_run.ps1 (renew before
# every CLI call; our lock gone => abort without touching the editor).
# ASCII only (PS 5.1 parses a BOM-less non-ASCII .ps1 as ANSI).
# =============================================================================
param(
    [string]$Tag = 'fc2',
    [string]$StopAfter = 'G1a-shot',
    [int]$PollSec = 900,
    [int]$HoverSettleMs = 1500,
    [string]$Why = 'presentation-class: the medium NPC-dialog option button must show a CLEAN highlight plate (btn_med_sel re-exported with the fechar palette, speckle 0.424 -> 0.054) while a real injected pointer HOVERS it; only a rendered frame can show that, and the runtime log proves the previous tour run never parked the pointer on a button'
)

$ErrorActionPreference = 'Continue'

$root    = 'c:/Work/Server/f-v2/clover-project-diablo2'
$proj    = Join-Path $root 'client'
$cs      = Join-Path $root 'tools/probes/drivers/x_drive.cs'
$test    = Join-Path $root '.ai-tmp/test'
$shots   = Join-Path $root '.ai-tmp/screenshots'
$raw     = Join-Path $shots 'finalclose_raw2'
$done    = Join-Path $test ('finalclose_done2_' + $Tag + '.txt')
$trace   = Join-Path $test ('finalclose_hover_steps_' + $Tag + '.txt')
$logPath = Join-Path $proj 'Logs/Editor.log'
$playLog = Join-Path $test 'play-log.tsv'
$lock    = Join-Path $test 'play.lock'
$runbg   = Join-Path $root 'tools/probes/interact/p_runbg.cs'
$me      = 'final-close'
$lockTag = '^\s*final-close\b'

# pointer positions to try (screen px, 1920x1080).  The option column sits at
# x 862..1046; the two buttons' centres read off the fc1 frame are y=585 (leave)
# and y=643 (trade) measured from the TOP, i.e. 495 / 437 from the bottom.
$probeNormal = '300,950'
$probeLeaveA = '954,495'
$probeTradeA = '954,437'
$probeTradeB = '954,643'

$script:offset = 0
$script:lines = New-Object System.Collections.ArrayList
$script:cli = 0
$script:touches = 0
$script:mine = $false

function Say([string]$s) {
    $stamp = (Get-Date).ToString('HH:mm:ss.fff')
    Write-Host ($stamp + ' ' + $s)
    Add-Content -Path $trace -Value ($stamp + ' ' + $s) -Encoding UTF8
}
function Touch-Lock() {
    if (-not (Test-Path -LiteralPath $lock)) { return $false }
    $body = ''
    try { $body = (Get-Content -LiteralPath $lock -Raw) } catch { $body = '' }
    if ($body -notmatch $lockTag) { return $false }
    try {
        (Get-Item -LiteralPath $lock).LastWriteTime = Get-Date
        $script:touches = $script:touches + 1
        return $true
    } catch {
        Say ('TOUCH-LOCK-FAIL ' + $_.Exception.Message)
        return $false
    }
}
function Release-Lock() {
    if (-not (Test-Path -LiteralPath $lock)) { Say 'LOCK-RELEASE skip (no lock file)'; $script:mine = $false; return }
    $body = ''
    try { $body = (Get-Content -LiteralPath $lock -Raw) } catch { $body = '' }
    if ($body -match $lockTag) {
        Remove-Item -LiteralPath $lock -Force -ErrorAction SilentlyContinue
        $script:mine = $false
        Say ('LOCK-RELEASED (mine; touches=' + $script:touches + ')')
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
    if ($script:mine) {
        if (-not (Touch-Lock)) {
            Say 'LOCK-LOST -> our play.lock is gone (taken over?) => ABORT and DO NOT touch the editor'
            exit 2
        }
    }
    $raw2 = & unity command @argv --project-path $proj --format json --no-pager --no-banner 2>&1 | Out-String
    if (-not $Quiet) { Say ('CLI ' + ($argv -join ' ') + ' -> ' + $raw2.Length + ' bytes') }
    return $raw2
}
function Run-Step([string]$entry, [string]$arg) {
    if ([string]::IsNullOrEmpty($arg)) {
        $raw2 = Unity-Cmd @('run_script', '--file', $cs, '--entry', $entry)
    } else {
        $raw2 = Unity-Cmd @('run_script', '--file', $cs, '--entry', $entry, '--args', ('[\"' + $arg + '\"]'))
    }
    $result = '?'
    try {
        $j = $raw2 | ConvertFrom-Json
        $r = $j.data.result.result
        if ($null -eq $r) { $r = $j.data.result.error }
        if ($null -eq $r) {
            $d = @($j.data.result.diagnostics | Where-Object { $_.severity -eq 'error' })
            if ($d.Count -gt 0) { $result = 'COMPILE-ERRORS:' + $d.Count + ' ' + (Clip (($d | ForEach-Object { $_.message }) -join ' | ') 400) }
        } else { $result = [string]$r }
    } catch { $result = 'PARSE-FAIL' }
    Say ('STEP ' + $entry + ' result=' + $result)
    return $result
}
function Shot([string]$file) {
    # Primary: capture_game_view --source screen (the only documented way to include
    # Screen Space - Overlay canvases, which is where the dialog panels live).
    # save_path is project-relative => write under client/Temp/ and copy out.
    $leaf = Split-Path -Leaf $file
    $rel = 'Temp/fc3_' + $leaf
    $dest = Join-Path $proj ($rel -replace '/', '\')
    Remove-Item -LiteralPath $dest -Force -ErrorAction SilentlyContinue
    $resp = Unity-Cmd @('capture_game_view', '--source', 'screen', '--width', '1920',
                        '--height', '1080', '--save_path', $rel)
    Say ('CAPTURE-RESP ' + (Clip $resp 320))
    if (Test-Path -LiteralPath $dest) {
        Copy-Item -LiteralPath $dest -Destination $file -Force
        Say ('SHOT(screen) ' + $leaf + ' bytes=' + (Get-Item -LiteralPath $file).Length)
        return $true
    }
    # Fallback: the plain screenshot command (game view).
    Unity-Cmd @('screenshot', '--view', 'game', '--output', $file) -Quiet | Out-Null
    if (Test-Path -LiteralPath $file) {
        Say ('SHOT(screenshot-fallback) ' + $leaf + ' bytes=' + (Get-Item -LiteralPath $file).Length)
        return $true
    }
    Say ('SHOT-FAILED ' + $leaf)
    return $false
}
function Hover([string]$xy, [string]$label) {
    Run-Step 'X.Api2.ProbeMouse' $xy | Out-Null
    Start-Sleep -Milliseconds $HoverSettleMs
    Say ('PROBED ' + $label + ' xy=' + $xy)
}

# =============================================================================
foreach ($d in @($shots, $test, $raw)) { if (-not (Test-Path -LiteralPath $d)) { New-Item -ItemType Directory -Path $d | Out-Null } }
Set-Content -Path $trace -Value ('# final-close hover steps tag=' + $Tag) -Encoding UTF8
$pwd0 = (Get-Location).Path
Set-Location -LiteralPath $proj
Say ('BEGIN tag=' + $Tag + ' stopAfter=' + $StopAfter + ' cwd0=' + $pwd0)

$idle = $false
for ($i = 0; $i -lt 30; $i++) {
    $st = Unity-Cmd @('editor_status') -Quiet
    $compiling = $true; $play = '?'
    try {
        $sj = $st | ConvertFrom-Json
        $inner = $sj.data.result
        if ($inner -is [string]) { $inner = $inner | ConvertFrom-Json }
        $compiling = [bool]$inner.compiling
        $play = [string]$inner.playMode
    } catch { }
    if ((-not $compiling) -and ($play -eq 'stopped')) { $idle = $true; Say ('EDITOR-IDLE (try ' + ($i + 1) + ')'); break }
    Say ('EDITOR-BUSY compiling=' + $compiling + ' playMode=' + $play + ' -> Start-Sleep 10')
    Start-Sleep -Seconds 10
}
if (-not $idle) { Say 'ABORT editor never went idle'; exit 1 }

for ($i = 0; $i -lt 20; $i++) {
    if (-not (Test-Path -LiteralPath $lock)) { break }
    $age = ((Get-Date) - (Get-Item -LiteralPath $lock).LastWriteTime).TotalSeconds
    $body = ''
    try { $body = (Get-Content -LiteralPath $lock -Raw) } catch { $body = '' }
    $st2 = Unity-Cmd @('editor_status') -Quiet
    $playing = $false
    try {
        $sj2 = $st2 | ConvertFrom-Json
        $inner2 = $sj2.data.result
        if ($inner2 -is [string]) { $inner2 = $inner2 | ConvertFrom-Json }
        $playing = ([string]$inner2.playMode -eq 'playing')
    } catch { }
    if (($age -ge 360) -and (-not $playing)) { Say ('LOCK-STALE age=' + [math]::Round($age, 1) + 's -> taking it'); break }
    Say ('LOCK-BUSY age=' + [math]::Round($age, 1) + 's playing=' + $playing + ' owner=' + (Clip $body 40) + ' -> Start-Sleep 30 (try ' + ($i + 1) + ')')
    Start-Sleep -Seconds 30
}
Set-Content -Path $lock -Value ($me + ' ' + (Get-Date).ToString('o')) -Encoding UTF8
$script:mine = $true
Say ('LOCK-TAKEN ' + $lock)
Add-Content -Path $playLog -Value ((Get-Date).ToString('yyyy-MM-dd HH:mm') + "`t" + $me + "`t" + $Tag + "`t" + $Why) -Encoding UTF8
Say ('PLAYLOG-APPENDED ' + $playLog)

Unity-Cmd @('editor_focus') -Quiet | Out-Null
Unity-Cmd @('editor_stop') -Quiet | Out-Null
Start-Sleep -Seconds 2

Unity-Cmd @('recompile') -Quiet | Out-Null
$recompile = '(unknown)'; $failed = '?'
for ($i = 0; $i -lt 90; $i++) {
    Start-Sleep -Seconds 2
    $st = Unity-Cmd @('recompile_status') -Quiet
    try {
        $j = $st | ConvertFrom-Json
        $inner = $j.data.result
        if ($inner -is [string]) { $inner = $inner | ConvertFrom-Json }
        $recompile = [string]$inner.status
        $failed = [string]$inner.failed
    } catch { }
    if ($recompile -match 'up_to_date|completed|idle') { break }
}
Say ('RECOMPILE status=' + $recompile + ' failed=' + $failed)
if (($recompile -notmatch 'up_to_date|completed|idle') -or ($failed -eq 'True')) {
    Say 'ABORT recompile not clean'
    Release-Lock
    exit 1
}
Start-Sleep -Seconds 3

Unity-Cmd @('clear_console') -Quiet | Out-Null
Unity-Cmd @('editor_focus') -Quiet | Out-Null
Unity-Cmd @('editor_play') -Quiet | Out-Null
Start-Sleep -Seconds 4
if (Test-Path $runbg) { $bg = Unity-Cmd @('eval_file', '--file', $runbg) -Quiet; Say ('RUNBG ' + (Clip $bg 160)) }

$cfgOk = $false
for ($i = 0; $i -lt 10; $i++) {
    $r = Run-Step 'X.Api2.Cfg' ''
    if ($r -match 'gameRunning=1') { $cfgOk = $true; break }
    Start-Sleep -Seconds 3
}
Say ('CFG-OK ' + $cfgOk)
if (-not $cfgOk) {
    Unity-Cmd @('editor_stop') -Quiet | Out-Null
    Release-Lock
    exit 1
}
Run-Step 'X.Api2.Paths' (($raw -replace '\\', '/') + '|' + ($done -replace '\\', '/')) | Out-Null
$spec = 'tour|' + $Tag + '|' + ($raw -replace '\\', '/') + '|' + ($done -replace '\\', '/') + '|' + $StopAfter
$r2 = Unity-Cmd @('run_script', '--file', $cs, '--entry', 'X.Tour.Install', '--args', ('[\"' + $spec + '\"]'))
Say ('INSTALL ' + (Clip $r2 200))

$t0 = Get-Date
while (((Get-Date) - $t0).TotalSeconds -lt $PollSec) {
    if (Test-Path $done) { break }
    Start-Sleep -Milliseconds 400
}
if (Test-Path $done) { Say ('DONE ' + (Get-Content $done -Raw).Trim()) } else { Say 'DONE-MISSING' }

# ---- the pointer/hover captures (editor is STILL IN PLAY) -------------------
$nShot = Join-Path $shots 'finalclose_hover2_normal.png'
$hShotA = Join-Path $shots 'finalclose_hover2_tradeA.png'
$hShotB = Join-Path $shots 'finalclose_hover2_tradeB.png'

Hover $probeNormal 'normal(away)'
Shot $nShot | Out-Null
Hover $probeTradeA 'tradeA(y-from-bottom 437)'
Shot $hShotA | Out-Null
Hover $probeTradeB 'tradeB(y-from-top 643)'
Shot $hShotB | Out-Null
Hover $probeLeaveA 'leaveA(495)'
Shot (Join-Path $shots 'finalclose_hover2_leaveA.png') | Out-Null

$consoleJson = Unity-Cmd @('console_status') -Quiet
$consoleErrors = -1
try { $cj = $consoleJson | ConvertFrom-Json; $consoleErrors = $cj.data.result.counts.error } catch { }
Say ('CONSOLE-STATUS errors=' + $consoleErrors)

Unity-Cmd @('editor_stop') -Quiet | Out-Null
Say 'STOPPED'
Release-Lock
Say ('SUMMARY tag=' + $Tag + ' cfgOk=' + $cfgOk + ' recompile=' + $recompile + ' failed=' + $failed + ' consoleErrors=' + $consoleErrors)
Say ('END cli=' + $script:cli)
