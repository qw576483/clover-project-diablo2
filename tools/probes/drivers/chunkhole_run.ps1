# =============================================================================
# chunkhole_run.ps1 -- one Play session for the chunk-hole slice.
#
#   powershell -NoProfile -ExecutionPolicy Bypass -File chunkhole_run.ps1 [-Tag chh]
#
#   Reuses the S2 navigation chain (unchanged file tools/probes/drivers/s2_drive.cs)
#   to reach the 80x80 wilderness, and adds the ONE reading S2 left unmeasured:
#   the deep state of MapView's rebuild job, sampled several times in the same
#   area (identity / Frames / NodesBuilt / ChunkIndex / Building / pending /
#   retire / built / expected-range MISSING count).
#
#   Play-lock protocol: take the lock only when it is absent or stale (>6min),
#   never delete a lock we do not own, release ours on every exit path.
#
#   ASCII only (PS 5.1 parses a BOM-less non-ASCII .ps1 as ANSI).
# =============================================================================
param(
    [string]$Tag = 'chh',
    [string]$Save = 'g66',
    [int]$Samples = 8,
    [double]$SampleGapSec = 1.2,
    [string]$Why = 'chunk-hole: the blood-moor black background is bisected down to (a) chunks that should exist were never built; the unmeasured link is why MapView._job stays non-null. Only a live frame loop can show whether Update reaches PumpRebuild, whether StartRebuild is restarted every time, and whether the missing-chunk count stays > 0 after the fix.'
)

$ErrorActionPreference = 'Continue'

#   !! 2026-09-23: promoted from .ai-tmp/drivers/ to tools/probes/drivers/ (judgement asset).
#      $root must now walk up THREE levels from .../tools/probes/drivers to reach the project
#      root (two levels were enough at the old location).  Run artefacts (readings / shots /
#      heartbeat) still land ONLY under <project-root>/.ai-tmp/** -- never in the Unity project.
#      Encoding: this file is ASCII-only on purpose (PS 5.1 mis-parses a BOM-less non-ASCII .ps1).
$root    = Split-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) -Parent
$proj    = Join-Path $root 'client'
$cs      = Join-Path $root 'tools\probes\drivers\chunkhole_probe.cs'
$tour    = Join-Path $root 'tools\probes\drivers\s2_drive.cs'
$test    = Join-Path $root '.ai-tmp\test'
$shots   = Join-Path $root '.ai-tmp\screenshots'
$heart   = Join-Path $test 'heartbeat-chunkhole.txt'
$done    = Join-Path $test ('chh_done_' + $Tag + '.txt')
$trace   = Join-Path $test ('chh_steps_' + $Tag + '.txt')
$frozen  = Join-Path $test ('chh_deep_' + $Tag + '.txt')
$logPath = Join-Path $proj 'Logs\Editor.log'
$playLog = Join-Path $test 'play-log.tsv'
$lock    = Join-Path $test 'play.lock'

$keepRe = '\[S2\]|\[CHH\]'

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
    if ($body -match '^\s*CHH\b') {
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
Set-Content -Path $trace -Value ('# chunk-hole steps tag=' + $Tag) -Encoding UTF8
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
Set-Content -Path $lock -Value ('CHH ' + (Get-Date).ToString('o')) -Encoding UTF8
Say ('LOCK-TAKEN ' + $lock)

# editor status: compiling:false AND playMode stopped before touching anything
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

Add-Content -Path $playLog -Value ((Get-Date).ToString('yyyy-MM-dd HH:mm') + "`tchunk-hole`tchunk-hole-deep-reading`t" + $Why) -Encoding UTF8
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

$rawDir = Join-Path $test ('raw_' + $Tag)
$spec = $Tag + '|' + $Save + '|' + (($done -replace '\\', '/')) + '|' + (($rawDir -replace '\\', '/'))
Say ('SPEC ' + $spec)
if (-not (Test-Path (Join-Path $test ('raw_' + $Tag)))) { New-Item -ItemType Directory -Path (Join-Path $test ('raw_' + $Tag)) | Out-Null }
$specArgs = '[\"' + $spec + '\"]'   # PS5.1 native-pass escaping (same recipe as s2_run.ps1)
$r2 = Unity-Cmd @('run_script', '--file', $tour, '--entry', 'S2.Tour.Install', '--args', $specArgs)
Say ('INSTALL ' + (Clip $r2 300))

# ---- poll: wait until the S2 tour steps into the 80x80 area, then sample ----
$deep = New-Object System.Collections.ArrayList
$entered = $false
$shotEntry = $false
$t0 = Get-Date
while (((Get-Date) - $t0).TotalSeconds -lt 480) {
    Read-New
    if (-not $entered -and ((Lines-With '\[S2\] CHUNK-ENTER').Count -gt 0)) {
        $entered = $true
        Say 'AREA-ENTERED -> starting the deep-job sampling'
        $null = Run-Step $cs 'ChH.Api.Shot' (Join-Path $shots 'chunkhole_after_entry.png')
        $shotEntry = $true
    }
    if ($entered -and $deep.Count -lt $Samples) {
        $line = Run-Step $cs 'ChH.Api.Chunk' ''
        [void]$deep.Add($line)
        if ($deep.Count -ge $Samples) { break }
        Start-Sleep -Seconds $SampleGapSec
        continue
    }
    if (Test-Path $done) { Say 'TOUR-DONE-SEEN'; break }
    if (-not $entered -and ((Lines-With 'STEP-FATAL').Count -gt 3)) { Say 'TOO MANY STEP-FATALs -> leaving the poll early'; break }
    Start-Sleep -Milliseconds 500
}
Read-New
Say ('DEEP-SAMPLES n=' + $deep.Count)
if ($entered) {
    Start-Sleep -Seconds 1
    $null = Run-Step $cs 'ChH.Api.Shot' (Join-Path $shots 'chunkhole_after_settled.png')
    Start-Sleep -Seconds 2
    foreach ($s in @('chunkhole_after_entry.png', 'chunkhole_after_settled.png')) {
        $p = Join-Path $shots $s
        if (Test-Path $p) { Say ('SHOT-OK ' + $s + ' bytes=' + (Get-Item $p).Length) } else { Say ('SHOT-MISSING ' + $s) }
    }
}

foreach ($l in @(Lines-With '\[S2\] CHUNK')) { Say ('TOUR-EVIDENCE ' + (Clip $l 300)) }

$consoleJson = Unity-Cmd @('console_status') -Quiet
$consoleErrors = -1
try { $cj = $consoleJson | ConvertFrom-Json; $consoleErrors = $cj.data.result.counts.error } catch { }
Say ('CONSOLE-STATUS errors=' + $consoleErrors)

Unity-Cmd @('editor_stop') -Quiet | Out-Null
Say 'STOPPED'
Release-Lock

# ---- publish the raw tiles produced by this run -----------------------------
$raw = Join-Path $test ('raw_' + $Tag)
if (Test-Path $raw) {
    foreach ($f in Get-ChildItem $raw -Filter '*.png') {
        $destName = 'chunkhole_' + $f.Name          # never overwrite the S2 evidence images
        Copy-Item $f.FullName (Join-Path $shots $destName) -Force
        Say ('TILE-COPIED ' + $destName + ' bytes=' + $f.Length)
    }
}

# ---- freeze -----------------------------------------------------------------
$hdr = New-Object System.Collections.ArrayList
[void]$hdr.Add('# chunk-hole deep rebuild-job readings (tag=' + $Tag + ')')
$sessLine = '# session: ' + $sessionStart.ToString('yyyy-MM-dd HH:mm:ss') + '..' + (Get-Date).ToString('HH:mm:ss') + '   probe: .ai-tmp/drivers/chunkhole_probe.cs   navigation: tools/probes/drivers/s2_drive.cs'
[void]$hdr.Add($sessLine)
[void]$hdr.Add('# run: powershell -NoProfile -ExecutionPolicy Bypass -File .ai-tmp/drivers/chunkhole_run.ps1 -Tag ' + $Tag)
$envLine = '# recompile = ' + $recompile + '   console_status counts.error = ' + $consoleErrors + '   probe bytes = ' + $probeBytes.Length + ' nonAscii = ' + $nonAscii
[void]$hdr.Add($envLine)
[void]$hdr.Add('# ---------------------------------------------------------------------------')
foreach ($l in $deep) { [void]$hdr.Add($l) }
[void]$hdr.Add('# ---------------------------------------------------------------------------')
[System.IO.File]::WriteAllLines($frozen, [string[]]$hdr, (New-Object System.Text.UTF8Encoding($false)))
Say ('FROZEN ' + $frozen + ' deepLines=' + $deep.Count)
Say ('END cli=' + $script:cli)
