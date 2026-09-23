# =============================================================================
# msc2_run.ps1 -- melee-samecell, run 2: the DETERMINISTIC same-cell left click.
#
# Same chain as msc_run.ps1 (REUSED s2_drive.cs boot chain), but the probe is msc_probe.cs
# (MSC.Api.SameCell): it puts the player ON the monster's cell with the production
# IPlayerModule.TeleportTo, then fires the REAL gesture HandlePrimaryClick(grid) 12 times.
#
# Why run 2 is needed: run 1 (av2_probe.cs) already proved the production chain fires
# `clip=hit` x5 and `[普攻]` x12 on REAL left clicks -- but it only ever reached distance 1.41
# (a diagonal neighbour), so it did NOT exercise the same-cell posture the defect is about.
# The proximity in run 1 was luck (the monster has to step onto the player); this run removes it.
#
# Evidence freeze: run 1's incremental capture missed the `[普攻]` lines (harness filter defect),
# so this run freezes the session by re-reading client/Logs/Editor.log and keeping the
# interesting line families for the whole session day.
#
# ASCII only (PS 5.1 parses a BOM-less non-ASCII .ps1 as ANSI).
# =============================================================================
param(
    [string]$Why = 'melee-samecell run 2: the deterministic same-cell posture (player ON the monster cell + real HandlePrimaryClick) must resolve damage and must not print the shape rejection - this exists only in a live Play session; run 1 proved the adjacent-neighbour case only',
    [string]$Tag = 'msc2'
)

$ErrorActionPreference = 'Continue'

$root    = 'c:/Work/Server/f-v2/clover-project-diablo2'
$proj    = Join-Path $root 'client'
$test    = Join-Path $root '.ai-tmp/test'
$shots   = Join-Path $root '.ai-tmp/screenshots'
$raw     = Join-Path $test ('raw_' + $Tag)
$s2cs    = Join-Path $root 'tools\probes\drivers\s2_drive.cs'
$probcs  = Join-Path $root 'tools\probes\drivers\msc_probe.cs'
$entry   = 'MSC.Api.SameCell'
$pfx     = 'melee2_'
$donePr  = Join-Path $test ($Tag + '_done.txt')
$doneS2  = Join-Path $test ($Tag + '_s2_done.txt')
$summary = Join-Path $test ($Tag + '_summary.txt')
$trace   = Join-Path $test ($Tag + '_steps.txt')
$frozen  = Join-Path $shots ($Tag + '_evidence.txt')
$logPath = Join-Path $proj 'Logs\Editor.log'
$playLog = Join-Path $test 'play-log.tsv'
$lock    = Join-Path $test 'play.lock'
$owner   = 'melee-samecell'

$script:offset = 0
$script:lines = New-Object System.Collections.ArrayList
$script:cli = 0

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
        $keep = @($txt -split "`r?`n" | Where-Object { $_ -match '\[MSC\]|\[S2\]|\[Combat\]|\[Monster\]|SFX-START|判定形状' })
        foreach ($l in $keep) { [void]$script:lines.Add($l) }
    } catch { Say ('WARN log-read ' + $_.Exception.Message) }
    finally { if ($fs -ne $null) { $fs.Close(); $fs.Dispose() } }
}

function Read-All-Log() {
    if (-not (Test-Path $logPath)) { return @() }
    $fs = $null
    try {
        $fs = New-Object System.IO.FileStream($logPath, [System.IO.FileMode]::Open,
              [System.IO.FileAccess]::Read, [System.IO.FileShare]::ReadWrite)
        $len = [int]$fs.Length
        $buf = New-Object byte[] $len
        [void]$fs.Read($buf, 0, $len)
        return ([System.Text.Encoding]::UTF8.GetString($buf, 0, $len) -split "`r?`n")
    } catch { Say ('WARN log-read-all ' + $_.Exception.Message); return @() }
    finally { if ($fs -ne $null) { $fs.Close(); $fs.Dispose() } }
}

function Lines-With([string]$pattern) { return @($script:lines | Where-Object { $_ -match $pattern }) }
function Clip([string]$s, [int]$n) {
    if ($null -eq $s) { return '' }
    $one = $s -replace "`r?`n", ' '
    if ($one.Length -le $n) { return $one }
    return $one.Substring(0, $n)
}

function Unity-Cmd([string[]]$argv, [switch]$Quiet) {
    $script:cli = $script:cli + 1
    $r = & unity command @argv --format json --no-pager 2>&1 | Out-String
    if (-not $Quiet) { Say ('CLI ' + ($argv -join ' ') + ' -> ' + $r.Length + ' bytes') }
    return $r
}

function Run-Step([string]$file, [string]$entryName, [string]$arg) {
    if ([string]::IsNullOrEmpty($arg)) {
        $r = Unity-Cmd @('run_script', '--file', $file, '--entry', $entryName) -Quiet
    } else {
        $r = Unity-Cmd @('run_script', '--file', $file, '--entry', $entryName, '--args', ('[\"' + $arg + '\"]')) -Quiet
    }
    $result = '?'
    try {
        $j = $r | ConvertFrom-Json
        $v = $j.data.result.result
        if ($null -eq $v) { $v = $j.data.result.error }
        if ($null -eq $v) {
            $d = @($j.data.result.diagnostics | Where-Object { $_.severity -eq 'error' })
            if ($d.Count -gt 0) { $v = 'COMPILE-ERRORS:' + $d.Count + ' ' + (Clip (($d | ForEach-Object { $_.id + ' line ' + $_.line + ': ' + $_.message }) -join ' | ') 700) }
        }
        $result = [string]$v
    } catch { $result = 'PARSE-FAIL' }
    Say ('STEP ' + $entryName + ' arg=' + $arg + ' result=' + $result)
    return $result
}

foreach ($d in @($test, $shots, $raw)) { if (-not (Test-Path $d)) { New-Item -ItemType Directory -Path $d | Out-Null } }
Set-Content -Path $trace -Value '# msc2 steps' -Encoding UTF8
Set-Location -LiteralPath $proj
$sessionStart = Get-Date
Say ('BEGIN cwd=' + (Get-Location).Path)

# ---- play lock -------------------------------------------------------------
for ($i = 0; $i -lt 30; $i++) {
    if (-not (Test-Path $lock)) { break }
    $age = ((Get-Date) - (Get-Item $lock).LastWriteTime).TotalMinutes
    if ($age -ge 6) {
        $stL = Unity-Cmd @('editor_status') -Quiet
        $playL = '?'
        try { $jL = $stL | ConvertFrom-Json; $playL = [string]$jL.data.result.playMode } catch { }
        if ($playL -notmatch 'playing') { Say ('LOCK-STALE age=' + [math]::Round($age, 1) + 'm playMode=' + $playL + ' -> taking it'); break }
        Say ('LOCK-STALE-BUT-EDITOR-PLAYING age=' + [math]::Round($age, 1) + 'm -> refuse to steal, sleep 30 (try ' + ($i + 1) + ')')
    } else {
        Say ('LOCK-BUSY age=' + [math]::Round($age, 1) + 'm -> Start-Sleep 30 (try ' + ($i + 1) + ')')
    }
    Start-Sleep -Seconds 30
}
if (Test-Path $lock) {
    $age2 = ((Get-Date) - (Get-Item $lock).LastWriteTime).TotalMinutes
    $stL2 = Unity-Cmd @('editor_status') -Quiet
    $playL2 = '?'
    try { $jL2 = $stL2 | ConvertFrom-Json; $playL2 = [string]$jL2.data.result.playMode } catch { }
    if ($age2 -lt 6 -or $playL2 -match 'playing') {
        Say ('ABORT lock held by ' + ((Get-Content $lock -Raw -Encoding UTF8).Trim()) + ' age=' + [math]::Round($age2, 1) + 'm playMode=' + $playL2 + ' -> nothing written, nothing run')
        exit 2
    }
}
Set-Content -Path $lock -Value ($owner + ' ' + (Get-Date).ToString('o')) -Encoding UTF8
Say ('LOCK-TAKEN (' + $owner + ')')

Add-Content -Path $playLog -Value ((Get-Date).ToString('yyyy-MM-dd HH:mm') + "`t" + $owner + "`tmsc2-same-cell-deterministic`t" + $Why) -Encoding UTF8
Say 'PLAYLOG-APPENDED'

# ---- editor idle -----------------------------------------------------------
$idle = $false
for ($i = 0; $i -lt 60; $i++) {
    $st = Unity-Cmd @('editor_status') -Quiet
    $compiling = '?'
    $play = '?'
    try { $j = $st | ConvertFrom-Json; $compiling = [string]$j.data.result.compiling; $play = [string]$j.data.result.playMode } catch { }
    if ($play -match 'play' -and $play -notmatch 'stopped') { Say ('EDITOR-IN-PLAY mode=' + $play + ' -> editor_stop'); Unity-Cmd @('editor_stop') -Quiet | Out-Null; Start-Sleep -Seconds 3 }
    if ($compiling -eq 'False' -or $compiling -eq 'false') { $idle = $true; Say ('EDITOR-IDLE try=' + $i + ' playMode=' + $play); break }
    Say ('EDITOR-COMPILING -> sleep 5 (' + $i + ')')
    Start-Sleep -Seconds 5
}
Say ('EDITOR-IDLE ' + $idle)

foreach ($f in @($donePr, $doneS2)) { if (Test-Path $f) { Remove-Item $f -Force; Say ('CLEARED ' + (Split-Path $f -Leaf)) } }

Unity-Cmd @('editor_focus') -Quiet | Out-Null
Unity-Cmd @('editor_stop')  -Quiet | Out-Null
Start-Sleep -Seconds 2
Unity-Cmd @('recompile') -Quiet | Out-Null
$recompile = '(unknown)'
for ($i = 0; $i -lt 60; $i++) {
    Start-Sleep -Seconds 2
    $rj = Unity-Cmd @('recompile_status') -Quiet
    try { $jj = $rj | ConvertFrom-Json; $recompile = (($jj.data.result | ConvertFrom-Json).status) } catch { }
    if ($recompile -match 'up_to_date|completed|idle') { break }
}
Say ('RECOMPILE status=' + $recompile)
if ($recompile -notmatch 'up_to_date|completed|idle') {
    Say 'ABORT recompile never settled -> release MY lock and stop'
    Remove-Item $lock -Force -ErrorAction SilentlyContinue
    exit 1
}

Unity-Cmd @('clear_console') -Quiet | Out-Null
Init-Offset
Unity-Cmd @('editor_play') -Quiet | Out-Null
Start-Sleep -Seconds 8

# ---- reuse s2_drive.cs to reach Stage + Blood Moor --------------------------
$cfgOk = $false
for ($i = 0; $i -lt 10; $i++) {
    $r = Run-Step $s2cs 'S2.Api.Cfg' ''
    if ($r -match 'CFG-OK') { $cfgOk = $true; break }
    Start-Sleep -Seconds 3
}
Say ('CFG-OK ' + $cfgOk)
if (-not $cfgOk) { Say 'ABORT no Cfg -> stop + release MY lock'; Unity-Cmd @('editor_stop') -Quiet | Out-Null; Remove-Item $lock -Force -ErrorAction SilentlyContinue; exit 1 }

$spec = $Tag + '|g66|' + (($doneS2 -replace '\\', '/')) + '|' + (($raw -replace '\\', '/'))
Run-Step $s2cs 'S2.Tour.Install' $spec | Out-Null

$t0 = Get-Date
while (((Get-Date) - $t0).TotalSeconds -lt 300) {
    Read-New
    if ((Lines-With '\[S2\] VERDICT').Count -gt 0) { break }
    Start-Sleep -Milliseconds 500
}
Read-New
Say ('S2-CHAIN verdictLines=' + (Lines-With '\[S2\] VERDICT').Count)

# ---- OUR same-cell probe in the SAME Play session ---------------------------
$spec2 = (($shots -replace '\\', '/')) + '|' + (($donePr -replace '\\', '/')) + '|' + (($summary -replace '\\', '/')) + '|' + $pfx
$inst = Run-Step $probcs $entry $spec2
Say ('PROBE-START ' + $inst)
if ($inst -match 'ERR|PARSE-FAIL|COMPILE-ERRORS') {
    Say 'ABORT the probe did not start -> stop + release MY lock'
    foreach ($l in @(Lines-With '\[MSC\]')) { Say ('EVIDENCE ' + $l) }
    Unity-Cmd @('editor_stop') -Quiet | Out-Null
    Remove-Item $lock -Force -ErrorAction SilentlyContinue
    exit 1
}

$t1 = Get-Date
while (((Get-Date) - $t1).TotalSeconds -lt 120) {
    Read-New
    if (Test-Path $donePr) { break }
    Start-Sleep -Milliseconds 1000
}
Read-New
Say ('PROBE-DONE ' + (Test-Path $donePr))
if (Test-Path $donePr) { Say ('DONE-FILE ' + (Clip (Get-Content $donePr -Raw) 200)) }

foreach ($l in @(Lines-With '\[MSC\]')) { Say ('EVIDENCE ' + $l) }
foreach ($l in @(Lines-With '\[普攻\]|判定形状')) { Say ('COMBAT ' + $l) }

$consoleJson = Unity-Cmd @('console_status') -Quiet
$consoleErrors = -1
try { $cj = $consoleJson | ConvertFrom-Json; $consoleErrors = $cj.data.result.counts.error } catch { }
Say ('CONSOLE-STATUS errors=' + $consoleErrors)

foreach ($f in @($summary)) {
    if (Test-Path $f) { Say ('TXT ' + (Split-Path $f -Leaf) + ' lines=' + (@(Get-Content $f).Count)) }
    else { Say ('TXT MISSING ' + $f) }
}

Unity-Cmd @('editor_stop') -Quiet | Out-Null
Say 'STOPPED'
if (Test-Path $lock) {
    $body = ''
    try { $body = (Get-Content $lock -Raw) } catch { $body = '' }
    if ($body -match ('^\s*' + $owner + '\b')) { Remove-Item $lock -Force -ErrorAction SilentlyContinue; Say 'LOCK-RELEASED (mine)' }
    else { Say ('LOCK-RELEASE REFUSED (owned by ' + $body.Trim() + ')') }
}

# ---- freeze (from the WHOLE log, day-filtered: the incremental filter dropped [普攻]) -----
$hdr = New-Object System.Collections.ArrayList
[void]$hdr.Add('# melee-samecell run 2 -- frozen from client/Logs/Editor.log (whole-day read)')
[void]$hdr.Add('# session: ' + $sessionStart.ToString('yyyy-MM-dd HH:mm:ss') + '..' + (Get-Date).ToString('HH:mm:ss'))
[void]$hdr.Add('# run: powershell -NoProfile -ExecutionPolicy Bypass -File tools/probes/drivers/msc2_run.ps1')
[void]$hdr.Add('# chain: s2_drive.cs S2.Api.Cfg + S2.Tour.Install (reused) -> msc_probe.cs MSC.Api.SameCell')
[void]$hdr.Add('# env: recompile=' + $recompile + ' console_errors=' + $consoleErrors)
[void]$hdr.Add('# NOTE: the day filter keeps every line of ' + $sessionStart.ToString('yyyy-MM-dd') + '; this is the only session of the day in this log.')
[void]$hdr.Add('# ---------------------------------------------------------------------------')
$dayPrefix = '^\[[0-9]{4}-[0-9]{2}-[0-9]{2} '
$day = '^\[(' + $sessionStart.ToString('yyyy-MM-dd') + ') '
$all = Read-All-Log
foreach ($l in $all) {
    if ($l -notmatch $day) { continue }
    if ($l -match '\[MSC\]|\[S2\] VERD|\[S2\] FINISH|SFX-START|判定形状|\[普攻\]|\[Combat\] \[|\[Monster\]|pool full|AudioPool|\[Audio\]') { [void]$hdr.Add($l) }
}
[void]$hdr.Add('# ---------------------------------------------------------------------------')
[System.IO.File]::WriteAllLines($frozen, [string[]]$hdr, (New-Object System.Text.UTF8Encoding($false)))
Say ('FROZEN ' + $frozen + ' lines=' + $hdr.Count)
Say ('SUMMARY msc=' + (Lines-With '\[MSC\]').Count + ' cli=' + $script:cli)
Say 'END'
