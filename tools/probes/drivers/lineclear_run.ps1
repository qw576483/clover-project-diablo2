# =============================================================================
# lineclear_run.ps1 -- lineclear-fix: ONE Play session for the "monster swings at
# a wall forever" defect (registered by slice melee-ai-why, fixed in MonsterAi +
# CombatModule).
#
# Chain (nothing re-implemented, no product code touched):
#   take MY play lock -> wait compiling:false -> clear_console -> editor_play
#     -> S2.Api.Cfg + S2.Tour.Install (s2_drive.cs, REUSED -- reaches Stage/Blood Moor)
#     -> wait for [S2] VERDICT (=> standing in the field with monsters)
#     -> LCL.Api.Start (lineclear_probe.cs: place the player behind a wall, then
#        sample "blocked?" + the monster's own `attacking` rising edge per frame)
#     -> wait for .ai-tmp/test/lcl_done.txt
#     -> dump [LCL] / [Combat] lines -> console_status -> editor_stop -> release MY lock
#
# ASCII only (PS 5.1 parses a BOM-less non-ASCII .ps1 as ANSI).
# =============================================================================
param(
    [string]$Why = 'lineclear-fix: the settlement layer rejects "attack line blocked by terrain" with a log only, so the monster kept swinging at the wall (the AI never learned). Rejected swings leave NO DamageDealt event, so only a live per-frame reading of the monster attack flag + the firing-band placement behind a real wall can show that the swings stopped.',
    [string]$Tag = 'lcl'
)

$ErrorActionPreference = 'Continue'

$root    = 'c:/Work/Server/f-v2/clover-project-diablo2'
$proj    = Join-Path $root 'client'
$test    = Join-Path $root '.ai-tmp/test'
$shots   = Join-Path $root '.ai-tmp/screenshots'
$raw     = Join-Path $test ('raw_' + $Tag)
$s2cs    = Join-Path $root 'tools\probes\drivers\s2_drive.cs'
$lclcs   = Join-Path $root 'tools\probes\drivers\lineclear_probe.cs'
$summary = Join-Path $test ($Tag + '_summary.txt')
$doneLcl = Join-Path $test ($Tag + '_done.txt')
$doneS2  = Join-Path $test ($Tag + '_s2_done.txt')
$trace   = Join-Path $test ($Tag + '_steps.txt')
$frozen  = Join-Path $shots ($Tag + '_evidence.txt')
$logPath = Join-Path $proj 'Logs\Editor.log'
$playLog = Join-Path $test 'play-log.tsv'
$lock    = Join-Path $test 'play.lock'

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
        $keep = @($txt -split "`r?`n" | Where-Object { $_ -match '\[LCL\]|\[S2\]|\[Combat\]|\[Monster\]' })
        foreach ($l in $keep) { [void]$script:lines.Add($l) }
    } catch { Say ('WARN log-read ' + $_.Exception.Message) }
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

function Run-Step([string]$file, [string]$entry, [string]$arg) {
    if ([string]::IsNullOrEmpty($arg)) {
        $r = Unity-Cmd @('run_script', '--file', $file, '--entry', $entry) -Quiet
    } else {
        $r = Unity-Cmd @('run_script', '--file', $file, '--entry', $entry, '--args', ('[\"' + $arg + '\"]')) -Quiet
    }
    $result = '?'
    try {
        $j = $r | ConvertFrom-Json
        $v = $j.data.result.result
        if ($null -eq $v) { $v = $j.data.result.error }
        if ($null -eq $v) {
            $d = @($j.data.result.diagnostics | Where-Object { $_.severity -eq 'error' })
            if ($d.Count -gt 0) { $v = 'COMPILE-ERRORS:' + $d.Count + ' ' + (Clip (($d | ForEach-Object { $_.id + ' line ' + $_.line + ': ' + $_.message }) -join ' | ') 900) }
        }
        $result = [string]$v
    } catch { $result = 'PARSE-FAIL' }
    Say ('STEP ' + $entry + ' arg=' + $arg + ' result=' + $result)
    return $result
}

foreach ($d in @($test, $shots, $raw)) { if (-not (Test-Path $d)) { New-Item -ItemType Directory -Path $d | Out-Null } }
Set-Content -Path $trace -Value '# lcl steps' -Encoding UTF8
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
Set-Content -Path $lock -Value ('lineclear-fix ' + (Get-Date).ToString('o')) -Encoding UTF8
Say 'LOCK-TAKEN (lineclear-fix)'

Add-Content -Path $playLog -Value ((Get-Date).ToString('yyyy-MM-dd HH:mm') + "`tlineclear-fix`tlcl-wall-blocked`t" + $Why) -Encoding UTF8
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

foreach ($f in @($doneLcl, $doneS2)) { if (Test-Path $f) { Remove-Item $f -Force; Say ('CLEARED ' + (Split-Path $f -Leaf)) } }

Unity-Cmd @('editor_focus') -Quiet | Out-Null
Unity-Cmd @('editor_stop')  -Quiet | Out-Null
Start-Sleep -Seconds 2
Unity-Cmd @('recompile') -Quiet | Out-Null
$recompile = '(unknown)'
for ($i = 0; $i -lt 90; $i++) {
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

# ---- OUR probe in the SAME Play session ------------------------------------
$spec2 = (($summary -replace '\\', '/')) + '|' + (($doneLcl -replace '\\', '/')) + '|' + (($shots -replace '\\', '/')) + '|' + ($Tag + '_')
$inst = Run-Step $lclcs 'LCL.Api.Start' $spec2
Say ('PROBE-START ' + $inst)
if ($inst -match 'ERR|PARSE-FAIL|COMPILE-ERRORS') {
    Say 'ABORT the probe did not start -> stop + release MY lock'
    foreach ($l in @(Lines-With '\[LCL\]')) { Say ('EVIDENCE ' + $l) }
    Unity-Cmd @('editor_stop') -Quiet | Out-Null
    Remove-Item $lock -Force -ErrorAction SilentlyContinue
    exit 1
}

$t1 = Get-Date
while (((Get-Date) - $t1).TotalSeconds -lt 180) {
    Read-New
    if (Test-Path $doneLcl) { break }
    Start-Sleep -Milliseconds 1000
}
Read-New
Say ('LCL-DONE file=' + (Test-Path $doneLcl))
if (Test-Path $doneLcl) { Say ('DONE-FILE ' + (Clip (Get-Content $doneLcl -Raw) 400)) }

foreach ($l in @(Lines-With '\[LCL\]')) { Say ('EVIDENCE ' + $l) }

$consoleJson = Unity-Cmd @('console_status') -Quiet
$consoleErrors = -1
try { $cj = $consoleJson | ConvertFrom-Json; $consoleErrors = $cj.data.result.counts.error } catch { }
Say ('CONSOLE-STATUS errors=' + $consoleErrors)

# every settlement-layer monster-attack line in this session (the report counts the
# "blocked" subset offline -- the rejection text is non-ASCII, so it is NOT matched here)
$blk = @(Lines-With 'RequestMonsterAttack:')
Say ('COMBAT-REQUESTMONSTERATTACK-LINES ' + $blk.Count)

Unity-Cmd @('editor_stop') -Quiet | Out-Null
Say 'STOPPED'
if (Test-Path $lock) {
    $body = ''
    try { $body = (Get-Content $lock -Raw) } catch { $body = '' }
    if ($body -match '^\s*lineclear-fix\b') { Remove-Item $lock -Force -ErrorAction SilentlyContinue; Say 'LOCK-RELEASED (mine)' }
    else { Say ('LOCK-RELEASE REFUSED (owned by ' + $body.Trim() + ')') }
}

# ---- publish the raw s2 tiles (context only) --------------------------------
if (Test-Path $raw) {
    foreach ($f in Get-ChildItem $raw -Filter '*.png') {
        $nm = $Tag + '_s2_' + $f.Name
        Copy-Item $f.FullName (Join-Path $shots $nm) -Force
        Say ('TILE-COPIED ' + $f.Name + ' -> ' + $nm + ' bytes=' + $f.Length)
    }
}

# ---- freeze -----------------------------------------------------------------
$hdr = New-Object System.Collections.ArrayList
[void]$hdr.Add('# lineclear-fix -- fresh runtime log, frozen from client/Logs/Editor.log')
[void]$hdr.Add('# session: ' + $sessionStart.ToString('yyyy-MM-dd HH:mm:ss') + '..' + (Get-Date).ToString('HH:mm:ss'))
[void]$hdr.Add('# run: powershell -NoProfile -ExecutionPolicy Bypass -File tools/probes/drivers/lineclear_run.ps1')
[void]$hdr.Add('# chain: s2_drive.cs S2.Api.Cfg + S2.Tour.Install (reused) -> lineclear_probe.cs (LCL.Api.Start)')
[void]$hdr.Add('# env: recompile=' + $recompile + ' console_errors=' + $consoleErrors)
[void]$hdr.Add('# ---------------------------------------------------------------------------')
foreach ($l in @(Lines-With '\[LCL\]|\[S2\] VERD|\[S2\] FINISH')) { [void]$hdr.Add($l) }
[void]$hdr.Add('# ---------------------------------------------------------------------------')
[System.IO.File]::WriteAllLines($frozen, [string[]]$hdr, (New-Object System.Text.UTF8Encoding($false)))
Say ('FROZEN ' + $frozen + ' lines=' + $hdr.Count)
Say ('SUMMARY beats=' + (Lines-With '\[LCL\] BEAT').Count + ' blockedLogs=' + $blk.Count + ' cli=' + $script:cli)
Say 'END'
