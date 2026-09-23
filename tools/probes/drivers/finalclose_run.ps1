# =============================================================================
# finalclose_run.ps1 -- final-close slice (2026-09-24): ONE Play chain that
# re-captures the NPC-dialog option pair AFTER the hover-plate fix landed.
#
# WHY: the hover plate (btn_med_sel.png) was re-exported with the fechar palette
# (speckle 0.424 -> 0.054) and UiArt.ButtonSpritesFor now wires highlight =
# BtnMedSel.  Only a real frame can show whether the hovered medium button is
# now a CLEAN highlight plate (carved gold frame + grey stone face + blue
# sapphires) with the label still readable.  The twin tiles give the pair:
#   x_g1_dialog_notstarted.png -> finalclose_hover_normal.png  (no pointer on a button)
#   x_g1_dialog_inprogress.png -> finalclose_hover_hover.png   (pointer rests on the option it clicked)
#
# REUSES x_drive.cs (the X tour) instead of writing a new driver: the same tour
# already performs the real-mouse option clicks, so the pointer genuinely rests
# on a medium option button when the tile is taken.  The tour writes its tiles
# into a PRIVATE raw dir (spec field 3) so no other slice's frozen tile is
# overwritten; only the two rows above are copied out.
#
# Play lock: taken only when absent or (age >= 360 s AND playMode != playing);
# renewed before EVERY unity CLI call (Touch-Lock contract in automappanel_run.ps1).
# ASCII only (PS 5.1 parses a BOM-less non-ASCII .ps1 as ANSI).
# =============================================================================
param(
    [string]$Tag = 'fc1',
    [string]$StopAfter = 'G1b-shot',
    [int]$PollSec = 900,
    [string]$Why = 'presentation-class: only a real frame shows whether the medium option button of the NPC dialog shows a CLEAN highlight plate while the pointer HOVERS it (the plate btn_med_sel.png was re-exported with the fechar palette, speckle 0.424 -> 0.054, and UiArt now wires highlight = BtnMedSel); the same session pairs it with the non-hover state so the two tiles can be read side by side'
)

$ErrorActionPreference = 'Continue'

$root    = 'c:/Work/Server/f-v2/clover-project-diablo2'
$proj    = Join-Path $root 'client'
$cs      = Join-Path $root 'tools/probes/drivers/x_drive.cs'
$test    = Join-Path $root '.ai-tmp/test'
$shots   = Join-Path $root '.ai-tmp/screenshots'
$raw     = Join-Path $shots 'finalclose_raw'
$done    = Join-Path $test ('finalclose_done_' + $Tag + '.txt')
$trace   = Join-Path $test ('finalclose_steps_' + $Tag + '.txt')
$frozen  = Join-Path $test ('finalclose_play_evidence_' + $Tag + '.txt')
$logPath = Join-Path $proj 'Logs/Editor.log'
$playLog = Join-Path $test 'play-log.tsv'
$lock    = Join-Path $test 'play.lock'
$runbg   = Join-Path $root 'tools/probes/interact/p_runbg.cs'
$me      = 'final-close'
$lockTag = '^\s*final-close\b'

# only the two rows this slice re-captures (x_drive tile name -> delivered name)
$recap = [ordered]@{
    'x_g1_dialog_notstarted.png' = 'finalclose_hover_normal.png'
    'x_g1_dialog_inprogress.png' = 'finalclose_hover_hover.png'
}

# do NOT filter to a single tag: the previous slice lost two runs because
# $keepRe='\[SA\]' dropped every [SP]/[App]/[Map] line (see x_run.ps1 history).
$keepRe = '\[X\]|\[DIALOG\]|\[SA\]|\[SP\]|\[App\]|\[Map\]'

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
    } catch {
        Say ('WARN log-read ' + $_.Exception.Message)
    } finally {
        if ($fs -ne $null) { $fs.Close(); $fs.Dispose() }
    }
}
function Lines-With([string]$pattern) { return @($script:lines | Where-Object { $_ -match $pattern }) }

# ---- TOUCH-LOCK (contract: (1) renew only our own lock, (2) gone => abort,
#      (3) renew right before every CLI call) ---------------------------------
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

# =============================================================================
# main
# =============================================================================
foreach ($d in @($shots, $test, $raw)) { if (-not (Test-Path -LiteralPath $d)) { New-Item -ItemType Directory -Path $d | Out-Null } }
Set-Content -Path $trace -Value ('# final-close steps tag=' + $Tag) -Encoding UTF8
$sessionStart = Get-Date
$pwd0 = (Get-Location).Path
Set-Location -LiteralPath $proj
Say ('BEGIN tag=' + $Tag + ' stopAfter=' + $StopAfter + ' cwd0=' + $pwd0 + ' cwd=' + (Get-Location).Path)

# ---- editor idle -------------------------------------------------------------
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
    if ((-not $compiling) -and ($play -eq 'stopped')) { $idle = $true; Say ('EDITOR-IDLE compiling=false playMode=stopped (try ' + ($i + 1) + ')'); break }
    Say ('EDITOR-BUSY compiling=' + $compiling + ' playMode=' + $play + ' -> Start-Sleep 10')
    Start-Sleep -Seconds 10
}
if (-not $idle) { Say 'ABORT editor never went idle -> NOT taking the lock'; exit 1 }

# ---- play lock ---------------------------------------------------------------
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
    if (($age -ge 360) -and (-not $playing)) { Say ('LOCK-STALE age=' + [math]::Round($age, 1) + 's playing=' + $playing + ' -> taking it'); break }
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
    Say 'ABORT recompile not clean -> releasing MY lock and stopping'
    Release-Lock
    exit 1
}
Start-Sleep -Seconds 3

Unity-Cmd @('clear_console') -Quiet | Out-Null
Init-Offset
Say ('LOG-OFFSET ' + $script:offset)
Unity-Cmd @('editor_focus') -Quiet | Out-Null
Unity-Cmd @('editor_play') -Quiet | Out-Null
Start-Sleep -Seconds 4
if (Test-Path $runbg) {
    $bg = Unity-Cmd @('eval_file', '--file', $runbg) -Quiet
    Say ('RUNBG ' + (Clip $bg 200))
}

$cfgOk = $false
for ($i = 0; $i -lt 10; $i++) {
    $r = Run-Step 'X.Api2.Cfg' ''
    if ($r -match 'gameRunning=1') { $cfgOk = $true; break }
    Start-Sleep -Seconds 3
}
Say ('CFG-OK ' + $cfgOk)
if (-not $cfgOk) {
    Say 'ABORT X.Api2.Cfg never answered -> editor_stop + release MY lock'
    Unity-Cmd @('editor_stop') -Quiet | Out-Null
    Release-Lock
    exit 1
}
Run-Step 'X.Api2.Paths' (($raw -replace '\\', '/') + '|' + ($done -replace '\\', '/')) | Out-Null

$spec = 'tour|' + $Tag + '|' + ($raw -replace '\\', '/') + '|' + ($done -replace '\\', '/') + '|' + $StopAfter
$r2 = Unity-Cmd @('run_script', '--file', $cs, '--entry', 'X.Tour.Install', '--args', ('[\"' + $spec + '\"]'))
Say ('INSTALL ' + (Clip $r2 300))

$t0 = Get-Date
while (((Get-Date) - $t0).TotalSeconds -lt $PollSec) {
    Read-New
    if (Test-Path $done) { break }
    Start-Sleep -Milliseconds 400
}
Read-New
Start-Sleep -Seconds 3
Read-New
if (Test-Path $done) { Say ('DONE ' + (Get-Content $done -Raw).Trim()) }
else { Say ('DONE-MISSING (the plan never signalled completion within ' + $PollSec + 's)') }

$consoleJson = Unity-Cmd @('console_status') -Quiet
$consoleErrors = -1
try { $cj = $consoleJson | ConvertFrom-Json; $consoleErrors = $cj.data.result.counts.error } catch { }
Say ('CONSOLE-STATUS errors=' + $consoleErrors)

Unity-Cmd @('editor_stop') -Quiet | Out-Null
Say 'STOPPED'
Release-Lock

$copied = 0
foreach ($k in $recap.Keys) {
    $src = Join-Path $raw $k
    if (Test-Path $src) {
        Copy-Item $src (Join-Path $shots $recap[$k]) -Force
        $copied++
        Say ('RECAP-COPIED ' + $k + ' -> ' + $recap[$k])
    } else {
        Say ('RECAP-MISSING ' + $k)
    }
}
$tiles = @(Get-ChildItem $raw -Filter *.png -ErrorAction SilentlyContinue)
Say ('RAW-TILES ' + $tiles.Count + ' in ' + $raw)

$sessionEnd = Get-Date
$hdr = New-Object System.Collections.ArrayList
[void]$hdr.Add('# final-close: NPC dialog option pair (normal vs hover) after the hover-plate fix')
[void]$hdr.Add('# session: ' + $sessionStart.ToString('yyyy-MM-dd HH:mm:ss') + '..' + $sessionEnd.ToString('HH:mm:ss') + '   driver: tools/probes/drivers/x_drive.cs (reused)')
[void]$hdr.Add('# run: powershell -NoProfile -ExecutionPolicy Bypass -File tools/probes/drivers/finalclose_run.ps1 -Tag ' + $Tag + ' -StopAfter ' + $StopAfter)
[void]$hdr.Add('#   recompile = ' + $recompile + ' failed=' + $failed)
[void]$hdr.Add('#   console   = console_status counts.error=' + $consoleErrors)
[void]$hdr.Add('#   raw tiles = ' + $tiles.Count + ' in ' + $raw + ' ; recap copied = ' + $copied)
[void]$hdr.Add('# ---------------------------------------------------------------------------')
foreach ($l in @(Lines-With '\[X\]|\[DIALOG\]')) { [void]$hdr.Add($l) }
[System.IO.File]::WriteAllLines($frozen, [string[]]$hdr, (New-Object System.Text.UTF8Encoding($false)))
Say ('FROZEN ' + $frozen + ' lines=' + $hdr.Count)
Say ('SUMMARY tag=' + $Tag + ' cfgOk=' + $cfgOk + ' done=' + (Test-Path $done) + ' recompile=' + $recompile +
     ' failed=' + $failed + ' consoleErrors=' + $consoleErrors + ' rawTiles=' + $tiles.Count + ' recapCopied=' + $copied +
     ' x=' + (@(Lines-With '\[X\]').Count))
Say ('END cli=' + $script:cli)
