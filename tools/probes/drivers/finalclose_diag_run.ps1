# =============================================================================
# finalclose_diag_run.ps1 -- final-close (2026-09-24) follow-up diagnosis:
# does the NPC-dialog option button really ENTER the hover state?
#
# WHY: a pixel diff alone cannot separate "the highlight plate swapped" from "the
# mouse cursor is drawn on top of the button".  So this run stops the X tour right
# after the dialog is open (station G1a-shot), then parks the injected pointer on
# each option button through FDiag.Hover.Move (which reads the button's OWN
# RectTransform, so no guessed coordinates) and reads back, via FDiag.Hover.Read:
#   EventSystem + currentInputModule + the game's mouse position + raycast top hit
#   + any PopupMask + per-option Image.sprite.name / Selectable.currentSelectionState.
# currentSelectionState == Highlighted AND sprite == btn_med_sel is the proof that
# hover really engaged; sprite == btn_med_normal with state == Normal is the proof
# that it did not.
#
# Lock protocol: same Touch-Lock contract as automappanel_run.ps1.
# ASCII only (PS 5.1 parses a BOM-less non-ASCII .ps1 as ANSI).
# =============================================================================
param(
    [string]$Tag = 'fcd1',
    [string]$StopAfter = 'G1a-shot',
    [int]$PollSec = 900,
    [int]$SettleMs = 1600,
    [string]$Why = 'presentation-class/diagnostic: the hover state of the NPC dialog option button must be proven from runtime state (Selectable.currentSelectionState + Image.sprite.name) because a pixel diff cannot separate "the highlight plate swapped" from "the mouse cursor is drawn on the button"; the same session re-reads EventSystem/raycast/PopupMask so a blocked pointer ray cannot be mistaken for "hover not implemented"'
)

$ErrorActionPreference = 'Continue'

$root    = 'c:/Work/Server/f-v2/clover-project-diablo2'
$proj    = Join-Path $root 'client'
$cs      = Join-Path $root 'tools/probes/drivers/x_drive.cs'
$diagcs  = Join-Path $root 'tools/probes/drivers/finalclose_hoverdiag.cs'
$test    = Join-Path $root '.ai-tmp/test'
$shots   = Join-Path $root '.ai-tmp/screenshots'
$raw     = Join-Path $shots 'finalclose_raw3'
$done    = Join-Path $test ('finalclose_done3_' + $Tag + '.txt')
$trace   = Join-Path $test ('finalclose_diag_steps_' + $Tag + '.txt')
$playLog = Join-Path $test 'play-log.tsv'
$lock    = Join-Path $test 'play.lock'
$runbg   = Join-Path $root 'tools/probes/interact/p_runbg.cs'
$me      = 'final-close'
$lockTag = '^\s*final-close\b'

$script:cli = 0
$script:mine = $false
$script:touches = 0

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
    try { (Get-Item -LiteralPath $lock).LastWriteTime = Get-Date; $script:touches = $script:touches + 1; return $true }
    catch { Say ('TOUCH-LOCK-FAIL ' + $_.Exception.Message); return $false }
}
function Release-Lock() {
    if (-not (Test-Path -LiteralPath $lock)) { Say 'LOCK-RELEASE skip (no lock file)'; $script:mine = $false; return }
    $body = ''
    try { $body = (Get-Content -LiteralPath $lock -Raw) } catch { $body = '' }
    if ($body -match $lockTag) {
        Remove-Item -LiteralPath $lock -Force -ErrorAction SilentlyContinue
        $script:mine = $false
        Say ('LOCK-RELEASED (mine; touches=' + $script:touches + ')')
    } else { Say ('LOCK-RELEASE REFUSED (owned by ' + $body.Trim() + ') -> left alone') }
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
        if (-not (Touch-Lock)) { Say 'LOCK-LOST -> ABORT and DO NOT touch the editor'; exit 2 }
    }
    $raw2 = & unity command @argv --project-path $proj --format json --no-pager --no-banner 2>&1 | Out-String
    if (-not $Quiet) { Say ('CLI ' + ($argv -join ' ') + ' -> ' + $raw2.Length + ' bytes') }
    return $raw2
}
function Parse-Result([string]$raw2) {
    try {
        $j = $raw2 | ConvertFrom-Json
        $r = $j.data.result.result
        if ($null -eq $r) { $r = $j.data.result.error }
        if ($null -ne $r) { return [string]$r }
    } catch { }
    return 'PARSE-FAIL'
}
function Run-Step([string]$entry, [string]$arg) {
    if ([string]::IsNullOrEmpty($arg)) { $o = Unity-Cmd @('run_script', '--file', $cs, '--entry', $entry) }
    else { $o = Unity-Cmd @('run_script', '--file', $cs, '--entry', $entry, '--args', ('[\"' + $arg + '\"]')) }
    $r = Parse-Result $o
    Say ('STEP ' + $entry + ' result=' + (Clip $r 400))
    return $r
}
function Diag([string]$entry, [string]$arg) {
    if ([string]::IsNullOrEmpty($arg)) { $o = Unity-Cmd @('run_script', '--file', $diagcs, '--entry', $entry) }
    else { $o = Unity-Cmd @('run_script', '--file', $diagcs, '--entry', $entry, '--args', ('[\"' + $arg + '\"]')) }
    $r = Parse-Result $o
    Say ('DIAG ' + $entry + ' -> ' + (Clip $r 700))
    return $r
}
function CaptureDiag([string]$name) {
    # capture_game_view writes relative to the AUTHORING root => it lands in
    # client/Assets/Temp/ ... ; copy it out and delete the artefacts (incl. .meta)
    $rel = 'Temp/fcd_' + $name + '.png'
    $src = Join-Path $proj ($rel -replace '/', '\')
    $dst = Join-Path $shots ('finalclose_hover3_' + $name + '.png')
    Remove-Item -LiteralPath $src -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath ($src + '.meta') -Force -ErrorAction SilentlyContinue
    Unity-Cmd @('capture_game_view', '--source', 'screen', '--width', '1920', '--height', '1080',
                '--save_path', $rel) -Quiet | Out-Null
    if (Test-Path -LiteralPath $src) {
        Copy-Item -LiteralPath $src -Destination $dst -Force
        Remove-Item -LiteralPath $src -Force -ErrorAction SilentlyContinue
        Remove-Item -LiteralPath ($src + '.meta') -Force -ErrorAction SilentlyContinue
        Say ('CAPTURED ' + $name + ' -> ' + $dst + ' bytes=' + (Get-Item -LiteralPath $dst).Length)
        return $true
    }
    Say ('CAPTURE-FAILED ' + $name)
    return $false
}

# =============================================================================
foreach ($d in @($shots, $test, $raw)) { if (-not (Test-Path -LiteralPath $d)) { New-Item -ItemType Directory -Path $d | Out-Null } }
Set-Content -Path $trace -Value ('# final-close diag steps tag=' + $Tag) -Encoding UTF8
Set-Location -LiteralPath $proj
Say ('BEGIN tag=' + $Tag + ' stopAfter=' + $StopAfter)

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
    Say ('LOCK-BUSY age=' + [math]::Round($age, 1) + 's playing=' + $playing + ' owner=' + (Clip $body 40) + ' -> Start-Sleep 30')
    Start-Sleep -Seconds 30
}
Set-Content -Path $lock -Value ($me + ' ' + (Get-Date).ToString('o')) -Encoding UTF8
$script:mine = $true
Say ('LOCK-TAKEN ' + $lock)
Add-Content -Path $playLog -Value ((Get-Date).ToString('yyyy-MM-dd HH:mm') + "`t" + $me + "`t" + $Tag + "`t" + $Why) -Encoding UTF8
Say ('PLAYLOG-APPENDED')

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
if (($recompile -notmatch 'up_to_date|completed|idle') -or ($failed -eq 'True')) { Say 'ABORT recompile not clean'; Release-Lock; exit 1 }
Start-Sleep -Seconds 3

Unity-Cmd @('clear_console') -Quiet | Out-Null
Unity-Cmd @('editor_focus') -Quiet | Out-Null
Unity-Cmd @('editor_play') -Quiet | Out-Null
Start-Sleep -Seconds 4
if (Test-Path $runbg) { Unity-Cmd @('eval_file', '--file', $runbg) -Quiet | Out-Null; Say 'RUNBG-OK' }

$cfgOk = $false
for ($i = 0; $i -lt 10; $i++) {
    $r = Run-Step 'X.Api2.Cfg' ''
    if ($r -match 'gameRunning=1') { $cfgOk = $true; break }
    Start-Sleep -Seconds 3
}
Say ('CFG-OK ' + $cfgOk)
if (-not $cfgOk) { Unity-Cmd @('editor_stop') -Quiet | Out-Null; Release-Lock; exit 1 }
Run-Step 'X.Api2.Paths' (($raw -replace '\\', '/') + '|' + ($done -replace '\\', '/')) | Out-Null
$spec = 'tour|' + $Tag + '|' + ($raw -replace '\\', '/') + '|' + ($done -replace '\\', '/') + '|' + $StopAfter
Say ('INSTALL ' + (Clip (Unity-Cmd @('run_script', '--file', $cs, '--entry', 'X.Tour.Install', '--args', ('[\"' + $spec + '\"]'))) 200))

$t0 = Get-Date
while (((Get-Date) - $t0).TotalSeconds -lt $PollSec) {
    if (Test-Path $done) { break }
    Start-Sleep -Milliseconds 400
}
if (Test-Path $done) { Say ('DONE ' + (Get-Content $done -Raw).Trim()) } else { Say 'DONE-MISSING' }

# ---- the diagnosis: park the pointer, then read the runtime state -------------
$base = Diag 'FDiag.Hover.Read' 'before-any-move'
$cases = @(
    @('1', 'raw',  'opt1-raw'),
    @('1', 'flip', 'opt1-flip'),
    @('0', 'raw',  'opt0-raw'),
    @('0', 'flip', 'opt0-flip')
)
foreach ($c in $cases) {
    Diag 'FDiag.Hover.Move' ($c[0] + '|' + $c[1]) | Out-Null
    Start-Sleep -Milliseconds $SettleMs
    $r = Diag 'FDiag.Hover.Read' ('after-' + $c[2])
    if ($r -match 'state=Highlighted') { CaptureDiag $c[2] | Out-Null }
}
# park the pointer far away again and read the resting state
Run-Step 'X.Api2.ProbeMouse' '300,950' | Out-Null
Start-Sleep -Milliseconds 900
Diag 'FDiag.Hover.Read' 'after-park-away' | Out-Null

$consoleJson = Unity-Cmd @('console_status') -Quiet
$consoleErrors = -1
try { $cj = $consoleJson | ConvertFrom-Json; $consoleErrors = $cj.data.result.counts.error } catch { }
Say ('CONSOLE-STATUS errors=' + $consoleErrors)

Unity-Cmd @('editor_stop') -Quiet | Out-Null
Say 'STOPPED'
Release-Lock
Say ('SUMMARY tag=' + $Tag + ' cfgOk=' + $cfgOk + ' recompile=' + $recompile + ' failed=' + $failed + ' consoleErrors=' + $consoleErrors + ' cli=' + $script:cli)
Say 'END'
