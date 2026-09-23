# =============================================================================
# travelblack_run.ps1 -- slice travel-black: ONE Play session that drives a REAL
# waypoint travel and samples the MapView rebuild state EVERY FRAME from the
# landing frame on (plus 3 screenshots: landed / +0.5s / +2.0s).
#
# REUSE NOTE: this file is a minimal edit of the sibling driver tools/probes/drivers/
# groundicon_run.ps1 (same editor gate / same Play-lock protocol / same recompile poll /
# same freeze step) -- only the owner, the tag, the artifact names and the keep-regex
# changed. Do not re-invent the chain.
#
#   powershell -NoProfile -ExecutionPolicy Bypass -File travelblack_run.ps1 [-Tag travelblack] [-SaveSave g66]
#
# Produces:
#   .ai-tmp/screenshots/travelblack_<tag>_{1_landed,2_p05,3_p20}.png
#   .ai-tmp/screenshots/travelblack_<tag>.log                   (per-frame rows)
#   .ai-tmp/screenshots/travelblack_evidence_<tag>.txt          (frozen runtime rows)
#   .ai-tmp/test/travelblack_steps_<tag>.txt                    (runner trace)
#   .ai-tmp/test/travelblack_done_<tag>.txt
#
# ASCII only (PS 5.1 parses a BOM-less non-ASCII .ps1 as ANSI).
# =============================================================================
param(
    [string]$Tag = 'travelblack',
    [string]$Why = 'travel-black: play-verify proved the waypoint travel LOGIC is green (CHK area 0->1 ok=1) but the arrived frame pv_wp_4_arrived.png is a fully black screen. chunk-ctl/chunk-hole2 pinned the "area entry black window" to MapView''s T0FIX-H framed double-buffered rebuild, but the LANDING INSTANT was never measured per frame. This session samples job/ground/bufGround/pending + coverage of the camera-visible chunk range EVERY FRAME from the landing frame on, and takes 3 screenshots (landed / +0.5s / +2.0s).'
)

$ErrorActionPreference = 'Continue'

$root    = 'c:/Work/Server/f-v2/clover-project-diablo2'
$proj    = Join-Path $root 'client'
$cs      = Join-Path $root 'tools\probes\drivers\travelblack_drive.cs'
$test    = Join-Path $root '.ai-tmp\test'
$shots   = Join-Path $root '.ai-tmp\screenshots'
$done    = Join-Path $test ('travelblack_done_' + $Tag + '.txt')
$trace   = Join-Path $test ('travelblack_steps_' + $Tag + '.txt')
$frozen  = Join-Path $shots ('travelblack_evidence_' + $Tag + '.txt')
$logPath = Join-Path $proj 'Logs\Editor.log'
$playLog = Join-Path $test 'play-log.tsv'
$lock    = Join-Path $test 'play.lock'
$heart   = Join-Path $test 'heartbeat-travelblack.txt'
$me      = 'travel-black'

$keepRe = '\[TBLACK\]|\[T0FIX-H\]|\[T0FIX-I\]|MapView'

$script:offset = 0
$script:lines = New-Object System.Collections.ArrayList
$script:cli = 0

function Say([string]$s) {
    $stamp = (Get-Date).ToString('HH:mm:ss.fff')
    Write-Host ($stamp + ' ' + $s)
    Add-Content -Path $trace -Value ($stamp + ' ' + $s) -Encoding UTF8
    Set-Content -Path $heart -Value ($stamp + ' ' + $s) -Encoding UTF8
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

function Release-Lock() {
    if (-not (Test-Path $lock)) { Say 'LOCK-RELEASE skip (no lock file)'; return }
    $body = ''
    try { $body = (Get-Content $lock -Raw) } catch { $body = '' }
    if ($body -match '^\s*travel-black\b') {
        Remove-Item $lock -Force -ErrorAction SilentlyContinue
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
    $raw2 = & unity command @argv --format json --no-pager 2>&1 | Out-String
    if (-not $Quiet) { Say ('CLI ' + ($argv -join ' ') + ' -> ' + $raw2.Length + ' bytes') }
    return $raw2
}

# =============================================================================
# main
# =============================================================================
foreach ($d in @($shots, $test)) { if (-not (Test-Path $d)) { New-Item -ItemType Directory -Path $d | Out-Null } }
Set-Content -Path $trace -Value ('# travel-black steps tag=' + $Tag) -Encoding UTF8
$sessionStart = Get-Date
Set-Location -LiteralPath $proj
Say ('BEGIN tag=' + $Tag + ' cwd=' + (Get-Location).Path)

foreach ($f in @($done, $frozen)) { if (Test-Path $f) { Remove-Item $f -Force; Say ('CLEARED ' + (Split-Path $f -Leaf)) } }

# ---- editor gate + play lock ---------------------------------------------------
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

for ($i = 0; $i -lt 24; $i++) {
    if (-not (Test-Path $lock)) { break }
    $age = ((Get-Date) - (Get-Item $lock).LastWriteTime).TotalMinutes
    if ($age -ge 6) { Say ('LOCK-STALE age=' + [math]::Round($age, 1) + 'm -> taking it'); break }
    Say ('LOCK-BUSY age=' + [math]::Round($age, 1) + 'm -> Start-Sleep 30 (try ' + ($i + 1) + ')')
    Start-Sleep -Seconds 30
}
Set-Content -Path $lock -Value ($me + ' ' + (Get-Date).ToString('o')) -Encoding UTF8
Say ('LOCK-TAKEN ' + $lock)

$status = & unity status --format json --no-pager 2>&1 | Out-String
Say ('UNITY-STATUS ' + (Clip $status 300))

Add-Content -Path $playLog -Value ((Get-Date).ToString('yyyy-MM-dd HH:mm') + "`t" + $me + "`t" + $Tag + "`t" + $Why) -Encoding UTF8
Say ('PLAYLOG-APPENDED ' + $playLog)

Unity-Cmd @('editor_focus') -Quiet | Out-Null
Unity-Cmd @('editor_stop') -Quiet | Out-Null
Start-Sleep -Seconds 2

Unity-Cmd @('recompile') -Quiet | Out-Null
$recompile = '(unknown)'
$failed = '?'
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
if ($recompile -notmatch 'up_to_date|completed|idle') {
    Say 'ABORT recompile never settled -> releasing MY lock and stopping'
    Release-Lock
    exit 1
}
if ($failed -eq 'True') {
    Say 'ABORT recompile failed=true -> releasing MY lock and stopping'
    Release-Lock
    exit 1
}
Start-Sleep -Seconds 3

# ---- pre-play compile gate: run_script compiles the whole file, so a CS error surfaces here
$gate = Unity-Cmd @('run_script', '--file', $cs, '--entry', 'TBlackDrv.Api.Ping')
$gateOk = $false
$gateText = [string]$gate
if ($gateText -match 'PONG') { $gateOk = $true }
Say ('PING-GATE ok=' + $gateOk + ' ' + (Clip $gateText 300))
if (-not $gateOk) {
    Say 'ABORT the driver did not compile / answer -> releasing MY lock and stopping'
    Unity-Cmd @('editor_stop') -Quiet | Out-Null
    Release-Lock
    exit 1
}

Unity-Cmd @('clear_console') -Quiet | Out-Null
Init-Offset
Say ('LOG-OFFSET ' + $script:offset)
Unity-Cmd @('editor_play') -Quiet | Out-Null
Start-Sleep -Seconds 8

$spec = (($shots -replace '\\', '/') + '|' + $Tag)
$r2 = Unity-Cmd @('run_script', '--file', $cs, '--entry', 'TBlackDrv.Api.Go', '--args', ('[\"' + $spec + '\"]'))
Say ('INSTALL ' + (Clip $r2 400))

$t0 = Get-Date
while (((Get-Date) - $t0).TotalSeconds -lt 240) {
    Read-New
    if (Test-Path $done) { break }
    Start-Sleep -Milliseconds 400
}
Read-New
Start-Sleep -Seconds 3
Read-New

if (Test-Path $done) { Say ('DONE ' + (Get-Content $done -Raw).Trim()) }
else { Say 'DONE-MISSING (the driver never signalled completion)' }

$consoleJson = Unity-Cmd @('console_status') -Quiet
$consoleErrors = -1
try { $cj = $consoleJson | ConvertFrom-Json; $consoleErrors = $cj.data.result.counts.error } catch { }
Say ('CONSOLE-STATUS errors=' + $consoleErrors)

Unity-Cmd @('editor_stop') -Quiet | Out-Null
Say 'STOPPED'
Release-Lock

# ---- freeze --------------------------------------------------------------------
$sessionEnd = Get-Date
$hdr = New-Object System.Collections.ArrayList
[void]$hdr.Add('# travel-black evidence -- fresh per-frame runtime rows ([TBLACK]) frozen from client/Logs/Editor.log')
[void]$hdr.Add('# session: ' + $sessionStart.ToString('yyyy-MM-dd HH:mm:ss') + '..' + $sessionEnd.ToString('HH:mm:ss') + '   driver: tools/probes/drivers/travelblack_drive.cs')
[void]$hdr.Add('# run: powershell -NoProfile -ExecutionPolicy Bypass -File tools/probes/drivers/travelblack_run.ps1 -Tag ' + $Tag)
[void]$hdr.Add('#   recompile = ' + $recompile + ' failed=' + $failed + '   console = console_status counts.error=' + $consoleErrors)
[void]$hdr.Add('# ---------------------------------------------------------------------------')
foreach ($l in @(Lines-With '\[TBLACK\]')) { [void]$hdr.Add($l) }
foreach ($l in @(Lines-With '\[T0FIX')) { [void]$hdr.Add($l) }
[System.IO.File]::WriteAllLines($frozen, [string[]]$hdr, (New-Object System.Text.UTF8Encoding($false)))
Say ('FROZEN ' + $frozen + ' lines=' + $hdr.Count)
Say ('SUMMARY tag=' + $Tag + ' done=' + (Test-Path $done) + ' recompile=' + $recompile +
     ' failed=' + $failed + ' consoleErrors=' + $consoleErrors +
     ' rows=' + (@(Lines-With '\[TBLACK\]').Count))
Say ('END cli=' + $script:cli)
