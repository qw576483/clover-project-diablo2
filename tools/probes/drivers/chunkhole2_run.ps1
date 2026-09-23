# =============================================================================
# chunkhole2_run.ps1 -- ONE Play session for the chunk-hole2 slice.
#
#   powershell -NoProfile -ExecutionPolicy Bypass -File chunkhole2_run.ps1 -Tag chh2s
#
#   Reuses the S2 navigation chain (tools/probes/drivers/s2_drive.cs, unmodified)
#   to reach the 80x80 wilderness, and at the moment of entry takes:
#     shot 01 (instant of entry)  + deep reading
#     shot 02 (+5 s)              + deep reading
#     shot 03 (+10 s)             + deep reading
#   The deep reading (chunkhole_probe.cs ChH.Api.Chunk) carries the extra fields
#   this slice added: screen-corner grid range, player grid and the literal list
#   of built ground-chunk keys -- so the S2 "MISSING=4" and the chunk-hole
#   "MISSING=0" readings can be compared on the SAME formula.
#
#   Play lock: taken only when absent or stale (>6 min); never delete a lock we
#   do not own; released on every exit path.  ASCII only.
# =============================================================================
param(
    [string]$Tag = 'chh2s',
    [string]$Save = 'g66',
    [string]$Why = 'chunk-hole2: the blood-moor black background is a PRESENTATION fact (large black area yes/no) that only a live frame can settle, and the S2 MISSING=4 vs chunk-hole MISSING=0 contradiction can only be reconciled with the same-formula readings taken at the instant of entry and 5 s later.'
)

$ErrorActionPreference = 'Continue'

$root    = 'c:/Work/Server/f-v2/clover-project-diablo2'
$proj    = $root + '/client'
$cs      = $root + '/tools/probes/drivers/chunkhole_probe.cs'
$tour    = $root + '/tools/probes/drivers/s2_drive.cs'
$test    = $root + '/.ai-tmp/test'
$shots   = $root + '/.ai-tmp/screenshots'
$heart   = $test + '/heartbeat-chunkhole2.txt'
$done    = $test + '/chh2_done_' + $Tag + '.txt'
$trace   = $test + '/chh2_steps_' + $Tag + '.txt'
$frozen  = $test + '/chh2_deep_' + $Tag + '.txt'
$logPath = $proj + '/Logs/Editor.log'
$playLog = $test + '/play-log.tsv'
$lock    = $test + '/play.lock'

$keepRe = '\[S2\]|\[CHH2\]'

$script:offset = 0
$script:lines = New-Object System.Collections.ArrayList
$script:cli = 0

function Say([string]$s) {
    $stamp = (Get-Date).ToString('HH:mm:ss.fff')
    Write-Host ($stamp + ' ' + $s)
    Add-Content -Path $trace -Value ($stamp + ' ' + $s) -Encoding UTF8
    try { [System.IO.File]::WriteAllText($heart, ($stamp + ' ' + $s)) } catch { }
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
    if ($body -match '^\s*CHH2\b') {
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
    $raw2 = & unity command @argv --project-path $proj --format json --no-pager 2>&1 | Out-String
    if (-not $Quiet) { Say ('CLI ' + ($argv -join ' ') + ' -> ' + $raw2.Length + ' bytes') }
    return $raw2
}

function Run-Step([string]$file, [string]$entry, [string]$arg) {
    if ([string]::IsNullOrEmpty($arg)) {
        $r = Unity-Cmd @('run_script', '--file', $file, '--entry', $entry)
    } else {
        $r = Unity-Cmd @('run_script', '--file', $file, '--entry', $entry, '--args', ('[\"' + $arg + '\"]'))
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
        $result = [string]$v
    } catch { $result = 'PARSE-FAIL' }
    Say ('STEP ' + $entry + ' result=' + $result)
    return $result
}

# =============================================================================
# main
# =============================================================================
foreach ($d in @($shots, $test)) { if (-not (Test-Path $d)) { New-Item -ItemType Directory -Path $d | Out-Null } }
Set-Content -Path $trace -Value ('# chunk-hole2 steps tag=' + $Tag) -Encoding UTF8
$sessionStart = Get-Date
Say ('BEGIN tag=' + $Tag + ' root=' + $root)

$probeBytes = [System.IO.File]::ReadAllBytes($cs)
$nonAscii = 0
foreach ($b in $probeBytes) { if ($b -gt 127) { $nonAscii++ } }
Say ('PROBE-ASCII bytes=' + $probeBytes.Length + ' nonAscii=' + $nonAscii)

# ---- play lock -------------------------------------------------------------
for ($i = 0; $i -lt 20; $i++) {
    if (-not (Test-Path $lock)) { break }
    $age = ((Get-Date) - (Get-Item $lock).LastWriteTime).TotalMinutes
    if ($age -ge 6) { Say ('LOCK-STALE age=' + [math]::Round($age, 1) + 'm -> taking it'); break }
    Say ('LOCK-BUSY age=' + [math]::Round($age, 1) + 'm -> Start-Sleep 45 (try ' + ($i + 1) + ')')
    Start-Sleep -Seconds 45
}
Set-Content -Path $lock -Value ('CHH2 ' + (Get-Date).ToString('o')) -Encoding UTF8
Say ('LOCK-TAKEN ' + $lock)

for ($i = 0; $i -lt 60; $i++) {
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

foreach ($f in @($done, $frozen)) { if (Test-Path $f) { Remove-Item $f -Force; Say ('CLEARED ' + (Split-Path $f -Leaf)) } }

Add-Content -Path $playLog -Value ((Get-Date).ToString('yyyy-MM-dd HH:mm') + "`tchunk-hole2`tchunk-hole2-entry-screenshots`t" + $Why) -Encoding UTF8
Say ('PLAYLOG-APPENDED ' + $playLog)

Unity-Cmd @('recompile') -Quiet | Out-Null
$recompile = '(unknown)'
for ($i = 0; $i -lt 40; $i++) {
    Start-Sleep -Seconds 2
    $st = Unity-Cmd @('recompile_status') -Quiet
    try { $j = $st | ConvertFrom-Json; $recompile = (($j.data.result | ConvertFrom-Json).status) } catch { }
    if ($recompile -match 'up_to_date|completed|idle') { break }
}
Say ('RECOMPILE status=' + $recompile)
if ($recompile -notmatch 'up_to_date|completed|idle') {
    Say 'ABORT recompile never settled -> releasing MY lock and stopping'
    Release-Lock
    exit 1
}
Start-Sleep -Seconds 3

Unity-Cmd @('clear_console') -Quiet | Out-Null
Init-Offset
Say ('LOG-OFFSET ' + $script:offset)
Unity-Cmd @('editor_play') -Quiet | Out-Null
Start-Sleep -Seconds 7

$cfgOk = $false
for ($i = 0; $i -lt 10; $i++) {
    $r = Run-Step $tour 'S2.Api.Cfg' ''
    if ($r -match 'CFG-OK') { $cfgOk = $true; break }
    Start-Sleep -Seconds 3
}
Say ('CFG-OK ' + $cfgOk)
if (-not $cfgOk) {
    Say 'ABORT Api.Cfg never answered -> editor_stop (we hold the lock) + release MY lock'
    Unity-Cmd @('editor_stop') -Quiet | Out-Null
    Release-Lock
    exit 1
}

$raw = Join-Path $test ('raw_' + $Tag)
if (-not (Test-Path $raw)) { New-Item -ItemType Directory -Path $raw | Out-Null }
$spec = $Tag + '|' + $Save + '|' + ($done -replace '\\', '/') + '|' + ($raw -replace '\\', '/')
Say ('SPEC ' + $spec)
$specArgs = '[\"' + $spec + '\"]'
$r2 = Unity-Cmd @('run_script', '--file', $tour, '--entry', 'S2.Tour.Install', '--args', $specArgs)
Say ('INSTALL ' + (Clip $r2 300))

# ---- poll: entry instant -> 3 shots + 3 readings ---------------------------
$deep = New-Object System.Collections.ArrayList
$entered = $false
$t0 = Get-Date
$shotN = 0
$nextShotAt = Get-Date
while (((Get-Date) - $t0).TotalSeconds -lt 420) {
    Read-New
    if (-not $entered -and ((Lines-With '\[S2\] CHUNK-ENTER').Count -gt 0)) {
        $entered = $true
        Say 'AREA-ENTERED -> entry shot + reading now'
        $nextShotAt = Get-Date
    }
    if ($entered -and $shotN -lt 3 -and ((Get-Date) - $nextShotAt).TotalSeconds -ge 0) {
        $shotN = $shotN + 1
        $p = $shots + '/chunkhole2_0' + $shotN + '_' + $(if ($shotN -eq 1) { 'entry' } elseif ($shotN -eq 2) { 'plus5s' } else { 'plus10s' }) + '.png'
        $sr = Run-Step $cs 'ChH.Api.Shot' $p
        Say ('SHOT ' + $p + ' -> ' + (Clip $sr 160))
        Start-Sleep -Seconds 2
        $line = Run-Step $cs 'ChH.Api.Chunk' ''
        [void]$deep.Add(('sample' + $shotN + ' ' + $line))
        if ($shotN -ge 3) { break }
        $nextShotAt = (Get-Date).AddSeconds(5)
        continue
    }
    if (Test-Path $done) { Say 'TOUR-DONE-SEEN'; break }
    Start-Sleep -Milliseconds 500
}
Read-New
Say ('DEEP-SAMPLES n=' + $deep.Count)

foreach ($l in @(Lines-With '\[S2\] CHUNK-ENTER')) { Say ('TOUR-EVIDENCE ' + (Clip $l 400)) }

$consoleJson = Unity-Cmd @('console_status') -Quiet
$consoleErrors = -1
try { $cj = $consoleJson | ConvertFrom-Json; $consoleErrors = $cj.data.result.counts.error } catch { }
Say ('CONSOLE-STATUS errors=' + $consoleErrors)

Unity-Cmd @('editor_stop') -Quiet | Out-Null
Say 'STOPPED'
Release-Lock

if (Test-Path $raw) {
    foreach ($f in Get-ChildItem $raw -Filter '*.png') {
        Copy-Item $f.FullName (Join-Path $shots ('chunkhole2_tour_' + $f.Name)) -Force
        Say ('TILE-COPIED chunkhole2_tour_' + $f.Name + ' bytes=' + $f.Length)
    }
}

$hdr = New-Object System.Collections.ArrayList
[void]$hdr.Add('# chunk-hole2 entry-instant vs +5s readings (tag=' + $Tag + ')')
[void]$hdr.Add('# session: ' + $sessionStart.ToString('yyyy-MM-dd HH:mm:ss') + '..' + (Get-Date).ToString('HH:mm:ss'))
[void]$hdr.Add('# run: powershell -NoProfile -ExecutionPolicy Bypass -File .ai-tmp/drivers/chunkhole2_run.ps1 -Tag ' + $Tag)
[void]$hdr.Add('# recompile = ' + $recompile + '   console errors = ' + $consoleErrors + '   probe bytes = ' + $probeBytes.Length + ' nonAscii = ' + $nonAscii)
[void]$hdr.Add('# ---------------------------------------------------------------------------')
foreach ($l in $deep) { [void]$hdr.Add($l) }
[void]$hdr.Add('# ---------------------------------------------------------------------------')
[System.IO.File]::WriteAllLines($frozen, [string[]]$hdr, (New-Object System.Text.UTF8Encoding($false)))
Say ('FROZEN ' + $frozen + ' deepLines=' + $deep.Count)
Say ('END cli=' + $script:cli)
