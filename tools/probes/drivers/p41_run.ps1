# =============================================================================
# p41_run.ps1 -- one-off driver for pass 5 (FINAL Play: presentation evidence + numeric probes
#                for the 9 user complaints, in ONE Play session).
#
#   powershell -NoProfile -ExecutionPolicy Bypass -File p41_run.ps1 -Tag r1 [-KeepPlay]
#
# ONE fresh Play session: stop -> clear_console -> play -> cfg -> install the tour -> wait for the
# tour's DONE marker -> console read -> stop -> copy the tiles out of .ai-tmp into
# Assets/Screenshots -> run the sheet script (p41_sheet.py) which decides every grid verdict.
#
# WHY THE TILES ARE CAPTURED BY THE DRIVER ITSELF (and not by `capture_game_view`):
#   `capture_game_view --save_path` resolves relative to the AUTHORING ROOT (Assets/) -- measured:
#   save_path "Temp/x.png" -> savedPath "Assets/Temp/p41_pathcheck.png".  Writing a png under
#   Assets/ while Play runs forces StopAssetImportingV2(ForceSynchronousImport|ForceDomainReload),
#   and after that reload the play session half-initialises (Game.Res null, ticks stop) -- so a
#   per-tile CLI capture destroys the session it is capturing (agent-40a run 1 measured exactly
#   that).  The driver therefore writes into <repo>/.ai-tmp/test/raw/ (never imported) and THIS
#   script copies them into Assets/Screenshots after editor_stop.
#
# NOT shipped: lives in <project>/.ai-tmp/drivers/ and is deleted before delivery.
# ASCII only (PS 5.1 parses a BOM-less non-ASCII .ps1 as ANSI).
# =============================================================================

param(
    [string]$Tag = 'r1',
    # Re-run ONLY the offline half (rebuild the frozen log from the editor log tail + rebuild the
    # contact sheet).  Needed because the tiles/logs of a finished session stay valid: the sheet is a
    # pure function of (frozen log, tile files), so it must never cost another Play session.
    [switch]$SheetOnly,
    [string]$Why = 'pass 5 (final Play): ONE contact sheet + numeric probes for the 9 user complaints -- the judging criteria (byline readable on the boot/menu picture, which menu items are drawn, camera framing, 4-direction facing, original exit tiles instead of a black gap, wilderness size + monster spread) are PRESENTATION-class and a live session is also the only place the runtime values (CameraRig.OrthographicSize, map 80x80, MonsterModule.AliveCount, the exit cells tile keys, walk/run speed + animation frame rate) exist',
    [switch]$KeepPlay
)

$ErrorActionPreference = 'Continue'

$proj     = 'C:\Work\Server\full-dev\clover-project-diablo2\client'
$root     = 'C:\Work\Server\full-dev\clover-project-diablo2'
$cs       = $root + '\.ai-tmp\drivers\p41_drive.cs'
$outDir   = $root + '\.ai-tmp\drivers\out'
$rawDir   = $root + '\.ai-tmp\test\raw'
$sheetDir = $root + '\.ai-tmp\test'
$shotDir  = $proj + '\Assets\Screenshots'
$done     = $outDir + '\p41_done_' + $Tag + '.txt'
$trace    = $outDir + '\p41_steps_' + $Tag + '.txt'
$logCopy  = $outDir + '\p41_log_' + $Tag + '.txt'
$logPath  = $proj + '\Logs\Editor.log'
$playLog  = $root + '\.ai-tmp\test\play-log.tsv'
$sheet    = $sheetDir + '\p41_contact.png'
$index    = $sheetDir + '\p41_index.tsv'

$tileNames = @(
    'p41_01_boot.png', 'p41_02_menu.png', 'p41_03_town.png', 'p41_04_town_wide.png',
    'p41_05_town_exit.png', 'p41_06_wild.png', 'p41_07_wild_wide.png',
    'p41_08_dir_s.png', 'p41_09_dir_n.png', 'p41_10_dir_e.png', 'p41_11_dir_w.png'
)

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
        $keep = @($txt -split "`r?`n" | Where-Object {
            $_ -match '\[P41\]|\[View\]|\[Map\]|\[Flow\]|\[Camera\]|\[Player\]|Error|Exception|Assert' })
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

function Png-Dims([string]$p) {
    try {
        $b = [System.IO.File]::ReadAllBytes($p)
        if ($b.Length -lt 24) { return 'short' }
        $w = ([int]$b[16] * 16777216) + ([int]$b[17] * 65536) + ([int]$b[18] * 256) + [int]$b[19]
        $h = ([int]$b[20] * 16777216) + ([int]$b[21] * 65536) + ([int]$b[22] * 256) + [int]$b[23]
        return ([string]$w + 'x' + [string]$h)
    } catch { return 'err' }
}

# =============================================================================
# main
# =============================================================================
if (-not (Test-Path $outDir)) { New-Item -ItemType Directory -Path $outDir | Out-Null }
if (-not (Test-Path $rawDir)) { New-Item -ItemType Directory -Path $rawDir | Out-Null }
Set-Content -Path $trace -Value ('# p41 steps tag=' + $Tag) -Encoding UTF8
Say ('BEGIN tag=' + $Tag + ' sheetOnly=' + $SheetOnly)
Say ('PROJECT ' + $proj)

if ($SheetOnly) {
    # ---- rebuild the frozen log from the editor log tail of the LAST tour of this tag -------------
    # anchor = the last `[P41] TOUR-INSTALL spec=tour|` line (written when the tour was installed)
    # the editor holds the log open -> read it through a SHARED FileStream (plain ReadAllLines throws)
    $fs = New-Object System.IO.FileStream($logPath, [System.IO.FileMode]::Open,
          [System.IO.FileAccess]::Read, [System.IO.FileShare]::ReadWrite)
    $sr = New-Object System.IO.StreamReader($fs, [System.Text.Encoding]::UTF8)
    $all = $sr.ReadToEnd() -split "`r?`n"
    $sr.Close(); $fs.Close(); $sr = $null; $fs = $null
    $anchor = -1
    for ($i = 0; $i -lt $all.Length; $i++) {
        if ($all[$i] -match '\[P41\] TOUR-INSTALL spec=tour\|' + [regex]::Escape($Tag) + '\|') { $anchor = $i }
    }
    Say ('SHEETONLY-ANCHOR ' + $anchor + ' of ' + $all.Length + ' editor log lines')
    if ($anchor -lt 0) { Say 'SHEETONLY-FAIL no tour anchor in the editor log; cannot rebuild the frozen log'; exit 1 }

    $keep = @($all[$anchor..($all.Length - 1)] | Where-Object {
        $_ -match '\[P41\]|\[View\]|\[Map\]|\[Flow\]|\[Camera\]|\[Player\]|Error|Exception|Assert' })
    [System.IO.File]::WriteAllLines($logCopy, [string[]]$keep, (New-Object System.Text.UTF8Encoding($false)))
    Say ('LOGWRITE ' + $logCopy + ' lines=' + $keep.Count)

    $consoleJson = Unity-Cmd @('console_status') -Quiet
    Add-Content -Path $trace -Value ('SHEETONLY CONSOLE ' + ($consoleJson -replace "`r?`n", ' ')) -Encoding UTF8
    Add-Content -Path $trace -Value ('SUMMARY tag=' + $Tag + ' sheetOnly=True logLines=' + $keep.Count) -Encoding UTF8

    $py = & python ($sheetDir + '\p41_sheet.py') $shotDir $sheet $index $logCopy $trace 2>&1 | Out-String
    foreach ($ln in ($py -split "`r?`n")) { if ($ln.Trim().Length -gt 0) { Say ('SHEET ' + $ln) } }
    Say ('SHEET-FILES contact=' + $sheet + ' exists=' + (Test-Path $sheet) + ' index=' + $index + ' exists=' + (Test-Path $index))
    Say ('END cli=' + $script:cli)
    exit
}

$probeBytes = [System.IO.File]::ReadAllBytes($cs)
$nonAscii = 0
foreach ($b in $probeBytes) { if ($b -gt 127) { $nonAscii++ } }
Say ('PROBE-ASCII bytes=' + $probeBytes.Length + ' nonAscii=' + $nonAscii)

$status = & unity status --format json --no-pager --project-path $proj 2>&1 | Out-String
Say ('UNITY-STATUS ' + ($status -replace "`r?`n", ' '))
$rc = Unity-Cmd @('recompile_status') -Quiet
Say ('RECOMPILE ' + ($rc -replace "`r?`n", ' '))

# every artefact this round writes is cleared first so a stale file can never pass for a fresh one
foreach ($t in $tileNames) {
    $p = Join-Path $rawDir $t
    if (Test-Path $p) { Remove-Item $p -Force; Say ('RAWTILE-CLEARED ' + $t) }
    $q = Join-Path $shotDir $t
    if (Test-Path $q) { Remove-Item $q -Force; Say ('SHOTTILE-CLEARED ' + $t) }
}
if (Test-Path $done) { Remove-Item $done -Force; Say ('MARKER-CLEARED ' + $done) } else { Say ('MARKER-ABSENT ' + $done) }
foreach ($f in @($sheet, $index)) { if (Test-Path $f) { Remove-Item $f -Force; Say ('SHEET-CLEARED ' + $f) } }

# play budget ledger: one line per editor_play, written BEFORE entering play
Add-Content -Path $playLog -Value ((Get-Date).ToString('yyyy-MM-dd HH:mm') + "`tclover-impl`tpass5-final-play-evidence`t" + $Why) -Encoding UTF8
Say ('PLAYLOG-APPENDED ' + $playLog)

Unity-Cmd @('editor_stop') -Quiet | Out-Null
Start-Sleep -Seconds 2
Unity-Cmd @('clear_console') -Quiet | Out-Null
Init-Offset
Say ('LOG-OFFSET ' + $script:offset)
Unity-Cmd @('editor_play') -Quiet | Out-Null
Start-Sleep -Seconds 7

$cfgOk = $false
for ($i = 0; $i -lt 8; $i++) {
    $r = Run-Step 'P41.Api.Cfg' ''
    if ($r -match 'gameRunning=1') { $cfgOk = $true; break }
    Start-Sleep -Seconds 3
}
Say ('CFG-OK ' + $cfgOk)
Run-Step 'P41.Api.Ping' '' | Out-Null

# hand the probe its raw dir + the done marker
$specPaths = ($rawDir -replace '\\', '/') + '|' + ($done -replace '\\', '/')
Run-Step 'P41.Api.Paths' $specPaths | Out-Null

$spec = 'tour|' + $Tag + '|' + ($rawDir -replace '\\', '/') + '|' + ($done -replace '\\', '/')
$raw = Unity-Cmd @('run_script', '--file', $cs, '--entry', 'P41.Tour.Install', '--args', ('[\"' + $spec + '\"]'))
Say ('INSTALL ' + ($raw -replace "`r?`n", ' '))

$t0 = Get-Date
while (((Get-Date) - $t0).TotalSeconds -lt 300) {
    Read-New
    if (Test-Path $done) { break }
    if ((Lines-With 'STEP-TIMEOUT').Count -gt 0) { break }
    Start-Sleep -Milliseconds 300
}
Read-New

if (Test-Path $done) { Say ('DONE ' + (Get-Content $done -Raw).Trim()) }
else { Say 'DONE-MISSING (the tour never signalled completion)' }

foreach ($l in @(Lines-With '\[P41\] ')) { Say ('EVIDENCE ' + $l) }
foreach ($l in @(Lines-With 'STEP-TIMEOUT')) { Say ('EVIDENCE ' + $l) }
foreach ($l in @(Lines-With 'CHAIN ')) { Say ('EVIDENCE ' + $l) }
foreach ($l in @(Lines-With 'ERR ')) { Say ('EVIDENCE ' + $l) }
foreach ($l in @(Lines-With 'Exception')) { Say ('EVIDENCE ' + $l) }

$consoleJson = Unity-Cmd @('console_status') -Quiet
Say ('CONSOLE ' + ($consoleJson -replace "`r?`n", ' '))

if (-not $KeepPlay) {
    Unity-Cmd @('editor_stop') -Quiet | Out-Null
    Say 'STOPPED'
} else {
    Say 'KEEP-PLAY (editor left running for inspection)'
}

# ---- land the tiles where the project keeps screenshots (AFTER play, so no import/reload) ------
$landed = 0
foreach ($t in $tileNames) {
    $src = Join-Path $rawDir $t
    $dst = Join-Path $shotDir $t
    if (Test-Path $src) {
        Copy-Item $src $dst -Force
        $fi = Get-Item $dst
        $dims = Png-Dims $dst
        Say ('TILE-LANDED ' + $t + ' bytes=' + $fi.Length + ' dims=' + $dims + ' ' + $fi.LastWriteTime.ToString('HH:mm:ss'))
        $landed++
    } else {
        Say ('TILE-MISSING ' + $t + ' (the driver never produced it)')
    }
}
Say ('TILES-LANDED ' + $landed + '/' + $tileNames.Count)

[System.IO.File]::WriteAllLines($logCopy, [string[]]$script:lines, (New-Object System.Text.UTF8Encoding($false)))
Say ('LOGWRITE ' + $logCopy + ' lines=' + $script:lines.Count)

# ---- the sheet (the only place grid verdicts are decided) --------------------------------------
$py = & python ($sheetDir + '\p41_sheet.py') $shotDir $sheet $index $logCopy $trace 2>&1 | Out-String
foreach ($ln in ($py -split "`r?`n")) { if ($ln.Trim().Length -gt 0) { Say ('SHEET ' + $ln) } }
Say ('SHEET-FILES contact=' + $sheet + ' exists=' + (Test-Path $sheet) + ' index=' + $index + ' exists=' + (Test-Path $index))

Say ('SUMMARY tag=' + $Tag + ' cfgOk=' + $cfgOk + ' done=' + (Test-Path $done) +
     ' tiles=' + $landed + '/' + $tileNames.Count +
     ' verdicts=' + (@(Lines-With '\[P41\] VERDICT').Count) +
     ' chk=' + (@(Lines-With '\[P41\] CHK ').Count) +
     ' timeouts=' + (@(Lines-With 'STEP-TIMEOUT').Count) +
     ' logLines=' + $script:lines.Count)
Say ('END cli=' + $script:cli)
