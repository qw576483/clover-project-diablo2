# =============================================================================
# rows5256_run.ps1 -- ONE Play session for acceptance-row 56
#   ("presentation: ground item nameplate").
#
#   powershell -NoProfile -ExecutionPolicy Bypass -File rows5256_run.ps1 [-Tag r56] [-Why "..."]
#
# Chain: editor_status (wait for non-compiling) -> play.lock -> editor_stop ->
#   clear_console -> editor_play -> Byline.Api.Cfg (team protocol) -> RS.Api.Cfg ->
#   RS.Tour.Install -> wait for the done marker -> read the [RS] lines ->
#   editor_stop -> release the lock -> land the 2 tiles in .ai-tmp/screenshots/.
#
# The deliverable is (a) the two live tiles (hover / Alt) and (b) the runtime node
# metrics written by the driver (glyphH in CANVAS px + rendered row count for the
# nameplate and for the HUD `LifeText`, so "same magnitude as the other HUD text"
# has a number too).
#
# ASCII only (PS 5.1 parses a BOM-less non-ASCII .ps1 as ANSI).
# =============================================================================
param(
    [string]$Tag = 'r56',
    [string]$Why = 'row 56 is presentation-class: the criterion is that a ground item nameplate actually shows on screen on hover and when Alt is held, and that its glyph size matches the rest of the HUD text -- neither exists outside a live Play session'
)

$ErrorActionPreference = 'Continue'

$root    = Split-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) -Parent
$proj    = $root + '\client'
$cs      = $root + '\tools\probes\drivers\rows5256_drive.cs'
$bylineC = $root + '\tools\probes\drivers\byline_drive.cs'
$testDir = $root + '\.ai-tmp\test'
$outDir  = $testDir + '\rows5256'
$rawDir  = $testDir + '\rows5256raw'
$shotDir = $root + '\.ai-tmp\screenshots'
$done    = $outDir + '\rows5256_done_' + $Tag + '.txt'
$trace   = $outDir + '\rows5256_steps_' + $Tag + '.txt'
$logCopy = $outDir + '\rows5256_log_' + $Tag + '.txt'
$logPath = $proj + '\Logs\Editor.log'
$playLog = $testDir + '\play-log.tsv'
$lock    = $testDir + '\play.lock'

$tileNames = @('rows5256_itemlabel_hover.png', 'rows5256_itemlabel_alt.png')

$script:offset = 0
$script:lines = New-Object System.Collections.ArrayList
$script:cli = 0
$lockHeld = $false

function Say([string]$s) {
    $stamp = (Get-Date).ToString('HH:mm:ss.fff')
    Write-Host ($stamp + ' ' + $s)
    if (-not (Test-Path $outDir)) { New-Item -ItemType Directory -Path $outDir | Out-Null }
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
        $keep = @($txt -split "`r?`n" | Where-Object { $_ -match '\[RS\]|\[BYLINE\]|\[RUNBG\]|\[HB\]|\[GroundItemLabel\]|\[Input\]' })
        foreach ($l in $keep) { [void]$script:lines.Add($l) }
    } catch {
        Say ('WARN log-read ' + $_.Exception.Message)
    } finally {
        if ($fs -ne $null) { $fs.Close(); $fs.Dispose() }
    }
}

function Lines-With([string]$pattern) {
    return @($script:lines | Where-Object { $_ -match $pattern })
}

function Unity-Cmd([string[]]$argv, [switch]$Quiet) {
    $script:cli = $script:cli + 1
    $raw = & unity command @argv --project-path $proj --format json --no-pager 2>&1 | Out-String
    if (-not $Quiet) { Say ('CLI ' + ($argv -join ' ') + ' -> ' + $raw.Length + ' bytes') }
    return $raw
}

function Run-Step([string]$entry, [string]$arg) {
    if ([string]::IsNullOrEmpty($arg)) {
        $raw = Unity-Cmd @('run_script', '--file', $cs, '--entry', $entry)
    } else {
        $raw = Unity-Cmd @('run_script', '--file', $cs, '--entry', $entry, '--args', ('[\"' + $arg + '\"]'))
    }
    $result = '?'
    try { $j = $raw | ConvertFrom-Json; $result = [string]$j.data.result.result } catch { $result = 'PARSE-FAIL' }
    Say ('STEP ' + $entry + ' arg=' + $arg + ' result=' + $result)
    return $result
}

# =============================================================================
if (-not (Test-Path $outDir)) { New-Item -ItemType Directory -Path $outDir | Out-Null }
if (-not (Test-Path $rawDir)) { New-Item -ItemType Directory -Path $rawDir | Out-Null }
if (-not (Test-Path $shotDir)) { New-Item -ItemType Directory -Path $shotDir | Out-Null }
Set-Content -Path $trace -Value ('# rows5256 steps tag=' + $Tag) -Encoding UTF8
Say ('BEGIN tag=' + $Tag)
Say ('PROJECT ' + $proj)

$probeBytes = [System.IO.File]::ReadAllBytes($cs)
$nonAscii = 0
foreach ($b in $probeBytes) { if ($b -gt 127) { $nonAscii++ } }
Say ('PROBE-ASCII bytes=' + $probeBytes.Length + ' nonAscii=' + $nonAscii)

# ---- 1. wait for a non-compiling editor -------------------------------------
for ($i = 0; $i -lt 40; $i++) {
    $st = & unity command editor_status --project-path $proj --format json --no-pager 2>&1 | Out-String
    if ($st -match 'compiling\\*":\s*false') { Say ('EDITOR-READY tries=' + $i); break }
    Start-Sleep -Seconds 5
}

# ---- 2. acquire the play lock (team rule: 6 min stale window) ---------------
for ($i = 0; $i -lt 15; $i++) {
    if (-not (Test-Path $lock)) { break }
    $age = ((Get-Date) - (Get-Item $lock).LastWriteTime).TotalMinutes
    if ($age -ge 6) { Say ('LOCK-STALE age=' + [math]::Round($age, 2) + 'min -> taking it'); Remove-Item $lock -Force; break }
    Say ('LOCK-BUSY age=' + [math]::Round($age, 2) + 'min retry=' + $i)
    Start-Sleep -Seconds 30
}
Set-Content -Path $lock -Value ('rows-52-56 ' + (Get-Date).ToString('o')) -Encoding UTF8
$lockHeld = $true
Say ('LOCK-ACQUIRED ' + $lock)

foreach ($t in $tileNames) {
    $p = Join-Path $rawDir $t
    if (Test-Path $p) { Remove-Item $p -Force }
    $q = Join-Path $shotDir $t
    if (Test-Path $q) { Remove-Item $q -Force }
}
if (Test-Path $done) { Remove-Item $done -Force; Say ('MARKER-CLEARED ' + $done) } else { Say ('MARKER-ABSENT ' + $done) }

Add-Content -Path $playLog -Value ((Get-Date).ToString('yyyy-MM-dd HH:mm') + "`trows-52-56`trows5256-nameplate`t" + $Why) -Encoding UTF8
Say ('PLAYLOG-APPENDED ' + $playLog)

Unity-Cmd @('editor_focus') -Quiet | Out-Null
Unity-Cmd @('editor_stop')  -Quiet | Out-Null
Start-Sleep -Seconds 3
Unity-Cmd @('clear_console') -Quiet | Out-Null
Init-Offset
Say ('LOG-OFFSET ' + $script:offset)
Unity-Cmd @('editor_play') -Quiet | Out-Null
Start-Sleep -Seconds 8

$cfgOk = $false
for ($i = 0; $i -lt 8; $i++) {
    $rawB = & unity command run_script --file $bylineC --entry 'Byline.Api.Cfg' --project-path $proj --format json --no-pager 2>&1 | Out-String
    if ($rawB -match 'gameRunning=1') { $cfgOk = $true; break }
    Start-Sleep -Seconds 3
}
Say ('BYLINE-CFG-OK ' + $cfgOk)

$r2 = Run-Step 'RS.Api.Cfg' ''
Say ('RS-CFG ' + $r2)
$r3 = Run-Step 'RS.Api.Paths' (($rawDir -replace '\\', '/') + '|' + ($done -replace '\\', '/'))
Say ('RS-PATHS ' + $r3)
$r4 = Run-Step 'RS.Api.Ping' ''
Say ('RS-PING ' + $r4)

$spec = ($rawDir -replace '\\', '/') + '|' + ($done -replace '\\', '/')
$raw = Unity-Cmd @('run_script', '--file', $cs, '--entry', 'RS.Tour.Install', '--args', ('[\"' + $spec + '\"]'))
Say ('INSTALL ' + ($raw -replace "`r?`n", ' '))

$t0 = Get-Date
while (((Get-Date) - $t0).TotalSeconds -lt 300) {
    Read-New
    if (Test-Path $done) { break }
    if ((Lines-With 'STATION-FATAL').Count -gt 0) { break }
    Start-Sleep -Milliseconds 300
}
Start-Sleep -Seconds 2
Read-New

if (Test-Path $done) { Say ('DONE ' + (Get-Content $done -Raw).Trim()) }
else { Say 'DONE-MISSING (the driver never signalled completion)' }

foreach ($l in @(Lines-With '\[RS\] (STAGE-READY|DROP|DROP-ID|DROP-VIEW|CAND|HOVER-PROBE|HOVER-SETTLE|T-HOVER|T-ALT|SUMMARY|FINISH|ENTER-STAGE)')) { Say ('EVID ' + $l) }
foreach ($l in @(Lines-With '\[RS\] (T5a|T5b|HOVER)-NODE')) { Say ('NODE ' + $l) }
foreach ($l in @(Lines-With 'STATION-FATAL|SETUP-FAIL|TILE |CAPTURE-FAIL')) { Say ('EVID2 ' + $l) }

$consoleJson = Unity-Cmd @('console_status') -Quiet
Say ('CONSOLE ' + ($consoleJson -replace "`r?`n", ' '))

Unity-Cmd @('editor_stop') -Quiet | Out-Null
Say 'STOPPED'

$landed = 0
foreach ($t in $tileNames) {
    $src = Join-Path $rawDir $t
    $dst = Join-Path $shotDir $t
    if (Test-Path $src) {
        Copy-Item $src $dst -Force
        $fi = Get-Item $dst
        Say ('TILE-LANDED ' + $t + ' bytes=' + $fi.Length + ' ' + $fi.LastWriteTime.ToString('HH:mm:ss') + ' sha256=' + (Get-FileHash $dst -Algorithm SHA256).Hash.Substring(0,16))
        $landed++
    } else {
        Say ('TILE-MISSING ' + $t + ' (the driver never produced it)')
    }
}
Say ('TILES-LANDED ' + $landed + '/' + $tileNames.Count)

[System.IO.File]::WriteAllLines($logCopy, [string[]]$script:lines, (New-Object System.Text.UTF8Encoding($false)))
Say ('LOGWRITE ' + $logCopy + ' lines=' + $script:lines.Count)

if ($lockHeld -and (Test-Path $lock)) { Remove-Item $lock -Force; Say 'LOCK-RELEASED' }
Say ('SUMMARY tag=' + $Tag + ' cfgOk=' + $cfgOk + ' done=' + (Test-Path $done) +
     ' tiles=' + $landed + '/' + $tileNames.Count + ' logLines=' + $script:lines.Count)
Say ('END cli=' + $script:cli)
