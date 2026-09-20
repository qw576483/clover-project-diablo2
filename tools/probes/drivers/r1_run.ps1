# =============================================================================
# r1_run.ps1 -- ONE Play session for the R1 batch (A1..A16 evidence).
#
#   powershell -NoProfile -ExecutionPolicy Bypass -File r1_run.ps1 -Tag r1 [-KeepPlay]
#
# Stop -> clear_console -> play -> cfg (+MoveCommand counters) -> install the tour ->
# wait for the tour's DONE marker -> echo the evidence -> stop -> freeze the log ->
# build the contact sheet + index (tools/probes/measure/r1_sheet.py).
#
#   -SheetOnly : rebuild the frozen log + the sheet from the LAST tour of this tag in
#                Editor.log (the tiles and the log of a finished session stay valid, so
#                a sheet rebuild must never cost another Play session).
#
# WHY THE DRIVER WRITES THE TILES ITSELF:
#   `capture_game_view --save_path` resolves under the AUTHORING ROOT (Assets/) -- a png
#   written under Assets/ while Play runs forces StopAssetImportingV2(ForceSynchronousImport
#   |ForceDomainReload) and the play session half-initialises afterwards (Game.Res null,
#   ticks stop).  The driver therefore captures into <repo>/.ai-tmp/screenshots/ (outside
#   the Unity project => never imported).
#
# ASCII only (PS 5.1 parses a BOM-less non-ASCII .ps1 as ANSI).
# =============================================================================

param(
    [string]$Tag = 'r1',
    [switch]$SheetOnly,
    [string]$Why = 'R1 five-fix batch (R1-B click fallback / R1-C char-create transition+name / R1-D frame pacing / R1-E dialog+shop): the judging criteria are the live runtime values plus the pixels -- per-frame transition rectangles vs native sizes, the on-screen bridge/water click and its nearest-walkable fallback, the shop title/hint lines, and whether a dialog-panel button click produces a MoveCommand -- none of which exist outside a real play session',
    [switch]$KeepPlay
)

$ErrorActionPreference = 'Continue'

$proj  = 'client'
$root  = Split-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) -Parent
$cs    = Join-Path $root 'tools\probes\drivers\r1_drive.cs'
$sheetPy = Join-Path $root 'tools\probes\measure\r1_sheet.py'
$shots = Join-Path $root '.ai-tmp\screenshots'
$test  = Join-Path $root '.ai-tmp\test'
$done  = Join-Path $test ('r1_done_' + $Tag + '.txt')
$trace = Join-Path $test ('r1_steps_' + $Tag + '.txt')
$runLog = Join-Path $test ('r1_runlog_' + $Tag + '.txt')
# the sheet reads the SUMMARY / DEVICE lines for its header out of this trace
$trace = $runLog
$frozen = Join-Path $shots ('r1_evidence_' + $Tag + '.txt')
$sheet = Join-Path $shots 'r1_contact.png'
$index = Join-Path $shots 'r1_contact.index.tsv'
$logPath = Join-Path $proj 'Logs\Editor.log'
$playLog = Join-Path $test 'play-log.tsv'

$tileNames = @(
    'a01_town_wide.png', 'a02_town_bridge.png',
    'a03a_bridge_click_050.png', 'a03b_bridge_click_150.png', 'a04_water_fallback.png',
    'a05a_amazon_idle.png', 'a05b_barbarian_idle.png',
    'a06a_amazon_transition.png', 'a06b_barbarian_transition.png',
    'a07a_amazon_front.png', 'a07b_barbarian_front.png', 'a08_name_digits.png',
    'a09_walk_1.png', 'a09_walk_2.png', 'a09_walk_3.png', 'a09_walk_4.png', 'a09_walk_5.png', 'a09_walk_6.png',
    'a10_dialog_not_started.png', 'a11_dialog_in_progress.png',
    'a12_shop_and_dialog.png', 'a13_shop_closed.png',
    'a14_dialog_button_pre.png', 'a15_shop_title_hint.png', 'a16_questlog.png'
)

# Lines worth keeping.  NOTE the leading timestamp anchor: the editor log also holds .NET
# stack frames ("CloverEngine.Logger:Log (...System.Exception)") which match a bare
# /Error|Exception/ filter and bury the real evidence under hundreds of noise lines.
# The project logger writes `[yyyy-MM-dd HH:mm:ss.fff] [Level] [Tag] message`.
$keepRe = '^\[\d{4}-\d{2}-\d{2} [\d:.]+\] \[(Info|Warn|Error)\] \[(R1|R1-B|R1-C|R1-D|R1-E|Move|Player|Npc|Ui|Map|Camera|Flow|Input|App|Event|Table|Cfg|Item|Quest)\]'

$script:offset = 0
$script:lines = New-Object System.Collections.ArrayList
$script:cli = 0

function Say([string]$s) {
    $stamp = (Get-Date).ToString('HH:mm:ss.fff')
    Write-Host ($stamp + ' ' + $s)
    Add-Content -Path $runLog -Value ($stamp + ' ' + $s) -Encoding UTF8
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
    Say ('STEP ' + $entry + ' result=' + $result)
    return $result
}

# =============================================================================
# main
# =============================================================================
foreach ($d in @($shots, $test)) { if (-not (Test-Path $d)) { New-Item -ItemType Directory -Path $d | Out-Null } }
Set-Content -Path $runLog -Value ('# r1 run log tag=' + $Tag) -Encoding UTF8
Say ('BEGIN tag=' + $Tag + ' sheetOnly=' + $SheetOnly + ' root=' + $root)

if ($SheetOnly) {
    $fs = New-Object System.IO.FileStream($logPath, [System.IO.FileMode]::Open,
          [System.IO.FileAccess]::Read, [System.IO.FileShare]::ReadWrite)
    $sr = New-Object System.IO.StreamReader($fs, [System.Text.Encoding]::UTF8)
    $all = $sr.ReadToEnd() -split "`r?`n"
    $sr.Close(); $fs.Close(); $sr = $null; $fs = $null
    $anchor = -1
    for ($i = 0; $i -lt $all.Length; $i++) {
        if ($all[$i] -match '\[R1\] TOUR-INSTALL spec=tour\|' + [regex]::Escape($Tag) + '\|') { $anchor = $i }
    }
    Say ('SHEETONLY-ANCHOR ' + $anchor + ' of ' + $all.Length + ' editor log lines')
    if ($anchor -lt 0) { Say 'SHEETONLY-FAIL no tour anchor in the editor log'; exit 1 }
    $keep = @($all[$anchor..($all.Length - 1)] | Where-Object { $_ -match $keepRe })
    [System.IO.File]::WriteAllLines($frozen, [string[]]$keep, (New-Object System.Text.UTF8Encoding($false)))
    Say ('LOGWRITE ' + $frozen + ' lines=' + $keep.Count)
    $py = & python $sheetPy $shots $sheet $index $frozen $trace 2>&1 | Out-String
    foreach ($ln in ($py -split "`r?`n")) { if ($ln.Trim().Length -gt 0) { Say ('SHEET ' + $ln) } }
    exit
}

$probeBytes = [System.IO.File]::ReadAllBytes($cs)
$nonAscii = @($probeBytes | Where-Object { $_ -gt 127 }).Count
Say ('PROBE-ASCII bytes=' + $probeBytes.Length + ' nonAscii=' + $nonAscii)

$status = & unity status --format json --no-pager --project-path $proj 2>&1 | Out-String
Say ('UNITY-STATUS ' + ($status -replace "`r?`n", ' '))

# stale artefacts must never pass for fresh ones
foreach ($t in $tileNames) {
    $p = Join-Path $shots $t
    if (Test-Path $p) { Remove-Item $p -Force; Say ('TILE-CLEARED ' + $t) }
}
foreach ($f in @($done, $sheet, $index, $frozen)) {
    if (Test-Path $f) { Remove-Item $f -Force; Say ('CLEARED ' + (Split-Path $f -Leaf)) }
}

# play ledger: one line per editor_play, written BEFORE entering play
Add-Content -Path $playLog -Value ((Get-Date).ToString('yyyy-MM-dd HH:mm') + "`tclover-impl`tr1-batch-evidence`t" + $Why) -Encoding UTF8
Say ('PLAYLOG-APPENDED ' + $playLog)

Unity-Cmd @('editor_stop') -Quiet | Out-Null
Start-Sleep -Seconds 2
Unity-Cmd @('clear_console') -Quiet | Out-Null
Init-Offset
Say ('LOG-OFFSET ' + $script:offset)
Unity-Cmd @('editor_play') -Quiet | Out-Null
Start-Sleep -Seconds 4

$cfgOk = $false
for ($i = 0; $i -lt 10; $i++) {
    $r = Run-Step 'R1.Api.Cfg' ''
    if ($r -match 'gameRunning=1') { $cfgOk = $true; break }
    Start-Sleep -Seconds 3
}
Say ('CFG-OK ' + $cfgOk)
Run-Step 'R1.Api.Ping' '' | Out-Null

$specPaths = ($shots -replace '\\', '/') + '|' + ($done -replace '\\', '/')
Run-Step 'R1.Api.Paths' $specPaths | Out-Null

$spec = 'tour|' + $Tag + '|' + ($shots -replace '\\', '/') + '|' + ($done -replace '\\', '/')
$raw = Unity-Cmd @('run_script', '--file', $cs, '--entry', 'R1.Tour.Install', '--args', ('[\"' + $spec + '\"]'))
Say ('INSTALL ' + ($raw -replace "`r?`n", ' '))

$t0 = Get-Date
while (((Get-Date) - $t0).TotalSeconds -lt 600) {
    Read-New
    if (Test-Path $done) { break }
    if ((Lines-With 'STEP-TIMEOUT').Count -gt 0) { Say 'STEP-TIMEOUT seen -> leaving the poll early'; break }
    Start-Sleep -Milliseconds 300
}
Read-New

if (Test-Path $done) { Say ('DONE ' + (Get-Content $done -Raw).Trim()) }
else { Say 'DONE-MISSING (the tour never signalled completion)' }

foreach ($l in @(Lines-With '\[R1\] ')) { Say ('EVIDENCE ' + $l) }
foreach ($l in @(Lines-With 'STEP-TIMEOUT|STEP-FATAL')) { Say ('EVIDENCE ' + $l) }
foreach ($l in @(Lines-With '\] \[R1-B\]|CK-FALLBACK|CC-CLICK-RETRY|CONFIRM-RETRY')) { Say ('EVIDENCE ' + $l) }
foreach ($l in @(Lines-With '\] \[Error\]')) { Say ('EVIDENCE ' + $l) }

$consoleJson = Unity-Cmd @('console_status') -Quiet
Say ('CONSOLE ' + ($consoleJson -replace "`r?`n", ' '))

if (-not $KeepPlay) {
    Unity-Cmd @('editor_stop') -Quiet | Out-Null
    Say 'STOPPED'
} else {
    Say 'KEEP-PLAY (editor left running for inspection)'
}

$landed = 0
foreach ($t in $tileNames) {
    $p = Join-Path $shots $t
    if (Test-Path $p) {
        $fi = Get-Item $p
        Say ('TILE ' + $t + ' bytes=' + $fi.Length + ' ' + $fi.LastWriteTime.ToString('HH:mm:ss'))
        $landed++
    } else {
        Say ('TILE-MISSING ' + $t)
    }
}
Say ('TILES ' + $landed + '/' + $tileNames.Count)

[System.IO.File]::WriteAllLines($frozen, [string[]]$script:lines, (New-Object System.Text.UTF8Encoding($false)))
Say ('LOGWRITE ' + $frozen + ' lines=' + $script:lines.Count)

$py = & python $sheetPy $shots $sheet $index $frozen $trace 2>&1 | Out-String
foreach ($ln in ($py -split "`r?`n")) { if ($ln.Trim().Length -gt 0) { Say ('SHEET ' + $ln) } }
Say ('SHEET-FILES contact=' + (Test-Path $sheet) + ' index=' + (Test-Path $index))

Say ('SUMMARY tag=' + $Tag + ' cfgOk=' + $cfgOk + ' done=' + (Test-Path $done) +
     ' tiles=' + $landed + '/' + $tileNames.Count +
     ' verdicts=' + (@(Lines-With '\[R1\] VERDICT').Count) +
     ' chk=' + (@(Lines-With '\[R1\] CHK ').Count) +
     ' timeouts=' + (@(Lines-With 'STEP-TIMEOUT').Count) +
     ' fallbacks=' + (@(Lines-With 'CK-FALLBACK').Count) +
     ' logLines=' + $script:lines.Count)
Say ('END cli=' + $script:cli)
