# =============================================================================
# x_run.ps1 -- ONE Play session of the X full-system tour, for one run tag.
#
#   powershell -NoProfile -ExecutionPolicy Bypass -File x_run.ps1 -Tag run1
#   powershell -NoProfile -ExecutionPolicy Bypass -File x_run.ps1 -Tag run2
#
# Fixed Play order (per the acceptance table's "driver environment" section, unchanged):
#   editor_stop -> clear_console -> editor_play -> eval_file <p_runbg.cs>
#   -> Api2.Cfg -> Tour.Install (the whole station plan runs inside Play)
# NOTE: the old relative '_dev/p_runbg.cs' no longer exists on disk (client/_dev was cleaned);
#       the runbg snippet lives as the judging asset tools/probes/interact/p_runbg.cs, so the
#       run derives its absolute path from $root.  eval_file only runs inside Play mode (it
#       touches Game.Timer, which Game.Launch creates).
#   -> wait for the done marker -> echo the evidence -> stop -> freeze the log
#   -> copy the tiles aside -> build the contact sheets + index
#   -> (run2) compare run1 vs run2 and put run1's tiles back as the delivered set.
#
#   -SheetOnly : rebuild the sheet + index from the last tour of this tag in Editor.log
#                (no Play session needed -- a finished session's tiles and log stay valid).
#
# WHY THE DRIVER WRITES THE TILES ITSELF:
#   `capture_game_view --save_path` resolves under the AUTHORING ROOT (Assets/) -- a png written
#   under Assets/ while Play runs forces StopAssetImportingV2(...|ForceDomainReload) and the play
#   session half-initialises afterwards.  The driver therefore captures into <repo>/.ai-tmp/screenshots/
#   (outside the Unity project => never imported).
#
# ASCII only (PS 5.1 parses a BOM-less non-ASCII .ps1 as ANSI).
# =============================================================================

param(
    [string]$Tag = 'run1',
    [switch]$SheetOnly,
    [string]$Why = 'X batch re-capture of the whole flow with two cold starts: the walk trace (6 frames of one continuous walk), the hit trio, two consecutive normal attacks, cast + missile, inventory/equip before+after, the HUD full screen, the stats/skill/quest/minimap panels, the boxpieces options+pause boards, the new in-game CursorView cursor, and the town/moor/den framing -- all of which are presentation-class and only exist inside a real play session'
)

$ErrorActionPreference = 'Continue'

$root  = Split-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) -Parent
# absolute: the CLI resolves --project-path against the CURRENT directory, and the run script is
# started with the project dir as cwd (a relative 'client' would resolve to <project>/client/client)
$proj  = Join-Path $root 'client'
$cs    = Join-Path $root 'tools\probes\drivers\x_drive.cs'
$sheetPy = Join-Path $root 'tools\probes\measure\x_sheet.py'
$cmpPy   = Join-Path $root 'tools\probes\measure\x_compare.py'
$shots = Join-Path $root '.ai-tmp\screenshots'
$test  = Join-Path $root '.ai-tmp\test'
$aside1 = Join-Path $test 'x_run1'
$aside2 = Join-Path $test 'x_run2'
$done  = Join-Path $test ('x_done_' + $Tag + '.txt')
$runLog = Join-Path $test ('x_runlog_' + $Tag + '.txt')
$frozen = Join-Path $shots ('x_evidence_' + $Tag + '.txt')
$index = Join-Path $shots 'x_contact.index.tsv'
$cmpOut = Join-Path $shots 'x_compare.tsv'
$logPath = Join-Path $proj 'Logs\Editor.log'
$playLog = Join-Path $test 'play-log.tsv'
$runbg  = Join-Path $root 'tools\probes\interact\p_runbg.cs'

$tileNames = @(
    'x_b1_boot.png', 'x_b2_mainmenu.png',
    'x_b3_charselect.png', 'x_b3_delete_confirm.png', 'x_b4_charcreate.png',
    'x_b5_loading_a.png', 'x_b5_loading_b.png',
    'x_c5_title_town.png', 'x_c5_title_moor.png', 'x_c5_title_den.png',
    'x_c1_town_wide.png', 'x_c1_town_default.png', 'x_c4_minimap.png',
    'x_x6_hud.png', 'x_x7_charstats.png',
    'a09_walk_1.png', 'a09_walk_2.png', 'a09_walk_3.png',
    'a09_walk_4.png', 'a09_walk_5.png', 'a09_walk_6.png',
    'x_x9_cursor_default.png',
    'x_d1_nowalk_cursor.png', 'x_d1_detour_start.png', 'x_d1_detour_stop.png',
    'x_d2_follow_a.png', 'x_d2_follow_b.png',
    'x_d3_dir0_S.png', 'x_d3_dir1_SW.png', 'x_d3_dir2_W.png', 'x_d3_dir3_NW.png',
    'x_d3_dir4_N.png', 'x_d3_dir5_NE.png', 'x_d3_dir6_E.png', 'x_d3_dir7_SE.png',
    'x_e1_hit_a.png', 'x_e1_hit_b.png', 'x_e1_hit_c.png', 'x_e1_hit_after.png',
    'x_x3_atk1_a.png', 'x_x3_atk1_b.png', 'x_x3_atk2_a.png', 'x_x3_atk2_b.png',
    'x_x9_cursor_hover.png',
    'x_e2_levelup.png',
    'x_e3_skilltree.png', 'x_e4_cast_a.png', 'x_e4_cast_b.png',
    'x_f1_inventory.png', 'x_f1_tooltip_q0.png', 'x_f1_tooltip_q1.png',
    'x_f1_tooltip_q2.png', 'x_f1_tooltip_q3.png', 'x_f1_tooltip_q4.png',
    'x_f1_inventory_after_pickup.png',
    'x_f2_equip_before.png', 'x_f2_equip.png', 'x_f2_belt_before.png', 'x_f2_belt_after.png',
    'x_f3_gold.png',
    'x_g1_dialog_notstarted.png', 'x_g1_dialog_inprogress.png',
    'x_g1_dialog_ready.png', 'x_g1_dialog_done.png',
    'x_g2_shop_buy.png', 'x_g2_shop_sell.png', 'x_g2_shop_repair.png',
    'x_g3_questlog_notstarted.png', 'x_g3_questlog_inprogress.png',
    'x_g3_questlog_ready.png', 'x_g3_questlog_done.png', 'x_g4_den_cleared.png',
    'x_h1_death.png', 'x_h2_pause.png', 'x_h2_options_before.png',
    'x_h2_options_after.png', 'x_h2_mainmenu.png', 'x_h3_reentry.png'
)

# Project logger writes `[yyyy-MM-dd HH:mm:ss.fff] [Level] [Tag] message`; the leading timestamp
# anchor keeps .NET stack frames ("CloverEngine.Logger:Log (...)") out of the frozen evidence.
$keepRe = '^\[\d{4}-\d{2}-\d{2} [\d:.]+\] \[(Info|Warn|Error)\] \[(X|Flow|Map|Move|Player|Combat|Monster|Skill|Item|Quest|Npc|Ui|Audio|App|Input|Camera|View|Save|Table|Cfg)\]'

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
    try {
        $j = $raw | ConvertFrom-Json
        $r = $j.data.result.result
        if ($null -eq $r) { $r = $j.data.result.error }
        if ($null -eq $r) {
            $d = $j.data.result.diagnostics | Where-Object { $_.severity -eq 'error' }
            if ($d) { $r = 'COMPILE-ERRORS:' + (@($d).Count) }
        }
        $result = [string]$r
    } catch { $result = 'PARSE-FAIL' }
    Say ('STEP ' + $entry + ' result=' + $result)
    return $result
}

function Clip([string]$s, [int]$n) {
    if ($null -eq $s) { return '' }
    $one = $s -replace "`r?`n", ' '
    if ($one.Length -le $n) { return $one }
    return $one.Substring(0, $n)
}

function Build-Sheet() {
    $py = & python $sheetPy $shots $frozen $runLog $shots $Tag 2>&1 | Out-String
    foreach ($ln in ($py -split "`r?`n")) { if ($ln.Trim().Length -gt 0) { Say ('SHEET ' + $ln) } }
}

function Copy-Tiles([string]$to) {
    if (-not (Test-Path $to)) { New-Item -ItemType Directory -Path $to | Out-Null } else {
        Get-ChildItem -Path $to -Filter *.png -ErrorAction SilentlyContinue | Remove-Item -Force
    }
    $n = 0
    foreach ($t in $tileNames) {
        $p = Join-Path $shots $t
        if (Test-Path $p) { Copy-Item $p (Join-Path $to $t) -Force; $n++ }
    }
    Say ('ASIDE ' + $to + ' tiles=' + $n)
    return $n
}

# put back every tile of the last complete session that THIS session did not re-shoot.  Only files
# that are MISSING right now are restored, so a fresh tile is never overwritten by an older one.
function Restore-FromBackup([string]$why) {
    if ([string]::IsNullOrEmpty($script:backup) -or -not (Test-Path $script:backup)) {
        Say ('TILE-RESTORE skipped (no usable backup) why=' + $why)
        return 0
    }
    $n = 0
    foreach ($t in $tileNames) {
        $p = Join-Path $shots $t
        $b = Join-Path $script:backup $t
        if (-not (Test-Path $p) -and (Test-Path $b)) { Copy-Item $b $p -Force; $n++ }
    }
    Say ('TILE-RESTORED n=' + $n + ' why=' + $why + ' from=' + $script:backup)
    return $n
}

# =============================================================================
# main
# =============================================================================
foreach ($d in @($shots, $test)) { if (-not (Test-Path $d)) { New-Item -ItemType Directory -Path $d | Out-Null } }
Set-Content -Path $runLog -Value ('# x run log tag=' + $Tag) -Encoding UTF8
Say ('BEGIN tag=' + $Tag + ' sheetOnly=' + $SheetOnly + ' root=' + $root)

if ($SheetOnly) {
    $fs = New-Object System.IO.FileStream($logPath, [System.IO.FileMode]::Open,
          [System.IO.FileAccess]::Read, [System.IO.FileShare]::ReadWrite)
    $sr = New-Object System.IO.StreamReader($fs, [System.Text.Encoding]::UTF8)
    $all = $sr.ReadToEnd() -split "`r?`n"
    $sr.Close(); $fs.Close()
    $anchor = -1
    for ($i = 0; $i -lt $all.Length; $i++) {
        if ($all[$i] -match '\[X\] TOUR-INSTALL spec=tour\|' + [regex]::Escape($Tag) + '\|') { $anchor = $i }
    }
    Say ('SHEETONLY-ANCHOR ' + $anchor + ' of ' + $all.Length + ' editor log lines')
    if ($anchor -lt 0) { Say 'SHEETONLY-FAIL no tour anchor in the editor log'; exit 1 }
    $keep = @($all[$anchor..($all.Length - 1)] | Where-Object { $_ -match $keepRe })
    [System.IO.File]::WriteAllLines($frozen, [string[]]$keep, (New-Object System.Text.UTF8Encoding($false)))
    Say ('LOGWRITE ' + $frozen + ' lines=' + $keep.Count)
    Build-Sheet
    exit
}

$probeBytes = [System.IO.File]::ReadAllBytes($cs)
$nonAscii = @($probeBytes | Where-Object { $_ -gt 127 }).Count
Say ('PROBE-ASCII bytes=' + $probeBytes.Length + ' nonAscii=' + $nonAscii)

$status = & unity status --format json --no-pager --project-path $proj 2>&1 | Out-String
Say ('UNITY-STATUS ' + ($status -replace "`r?`n", ' '))

# stale artefacts must never pass for fresh ones -- BUT "clear first, heal only if the tour really
# finished" destroyed already-delivered tiles whenever a session was interrupted.  Measured
# 2026-09-21 18:15:51: the tour DID write x_f1_tooltip_q2.png (SHOT-OK n=41 bytes=338823), and a
# later interrupted run took it away => the tile went "missing" in tools/verify.ps1 while the
# reference was perfectly correct.  So: BACK UP FIRST, delete only after the backup is complete,
# and at the end put back every tile this session did not re-shoot.
$stamp  = (Get-Date).ToString('yyyyMMdd_HHmmss')
$backup = Join-Path $test ('x_run_backup_' + $stamp)
if (-not (Test-Path $backup)) { New-Item -ItemType Directory -Path $backup | Out-Null }
$present = 0
$copied  = 0
foreach ($t in $tileNames) {
    $p = Join-Path $shots $t
    if (Test-Path $p) {
        $present++
        Copy-Item $p (Join-Path $backup $t) -Force
        if (Test-Path (Join-Path $backup $t)) { $copied++ }
    }
}
Say ('TILE-BACKUP dir=' + $backup + ' present=' + $present + ' copied=' + $copied)
if ($copied -lt $present) {
    Say 'TILE-BACKUP-FAILED backup is incomplete -> the delivered tiles are NOT cleared'
    $backup = ''
} else {
    foreach ($t in $tileNames) {
        $p = Join-Path $shots $t
        if (Test-Path $p) { Remove-Item $p -Force }
    }
    Say 'TILE-CLEARED all'
}
foreach ($f in @($done, $index, $frozen)) {
    if (Test-Path $f) { Remove-Item $f -Force; Say ('CLEARED ' + (Split-Path $f -Leaf)) }
}

Add-Content -Path $playLog -Value ((Get-Date).ToString('yyyy-MM-dd HH:mm') + "`tclover-impl`tx-tour-" + $Tag + "`t" + $Why) -Encoding UTF8
Say ('PLAYLOG-APPENDED ' + $playLog)

Unity-Cmd @('editor_stop') -Quiet | Out-Null
Start-Sleep -Seconds 2

# SETTLE ANY PENDING SCRIPT COMPILE BEFORE ENTERING PLAY.
# Measured: a full-tour run died at station "F2-prepare" because the editor picked up a pending
# asset change mid-Play, recompiled Diablo2.dll ("Reloading assemblies after forced synchronous
# recompile") and domain-reloaded the running Play session -> Game.Res null and the tour froze.
# Get the compile out of the way first and only then enter Play.
# HARD GATE (measured 2026-09-23): a concurrent table generator left Assets/Scripts/Table/Base/
# BaseTable.cs half written; recompile_status then said compilationFailed=true and the editor
# answered editor_play with "All compiler errors have to be fixed before you can enter playmode!" --
# i.e. the Play session never started and the whole run was wasted.  Assert the flag, do not just
# match a status word (a "completed" status carries the failure flag too).
Unity-Cmd @('recompile') -Quiet | Out-Null
$upToDate = $false
$compileBad = $false
$compileRaw = ''
for ($i = 0; $i -lt 40; $i++) {
    Start-Sleep -Seconds 2
    $st = Unity-Cmd @('recompile_status') -Quiet
    $compileRaw = $st
    if ($st -match 'compilationFailed"\s*:\s*true') { $compileBad = $true; break }
    if ($st -match 'up_to_date|completed|idle') { $upToDate = $true; break }
}
Say ('RECOMPILE-SETTLED upToDate=' + $upToDate + ' compilationFailed=' + $compileBad)
if ($compileBad) {
    Say ('ABORT-COMPILE ' + (Clip ($compileRaw -replace "`r?`n", ' ') 900))
    Say 'ABORT-COMPILE the editor refuses to enter Play; a half-written file under Assets/Scripts is the usual cause (do NOT edit it from here -- code is frozen for this slice)'
    Restore-FromBackup 'aborted-before-play:compile-errors' | Out-Null
    exit 1
}
Start-Sleep -Seconds 3

Unity-Cmd @('clear_console') -Quiet | Out-Null
Init-Offset
Say ('LOG-OFFSET ' + $script:offset)
Unity-Cmd @('editor_play') -Quiet | Out-Null
Start-Sleep -Seconds 4

# the acceptance table's fixed order: runInBackground first, then the tour
$bg = Unity-Cmd @('eval_file', '--file', $runbg) -Quiet
Say ('RUNBG ' + (Clip $bg 240))

$cfgOk = $false
for ($i = 0; $i -lt 6; $i++) {
    $r = Run-Step 'X.Api2.Cfg' ''
    if ($r -match 'gameRunning=1') { $cfgOk = $true; break }
    Start-Sleep -Seconds 4
}
Say ('CFG-OK ' + $cfgOk)
Run-Step 'X.Api2.Ping' '' | Out-Null

$spec = 'tour|' + $Tag + '|' + ($shots -replace '\\', '/') + '|' + ($done -replace '\\', '/')
$raw = Unity-Cmd @('run_script', '--file', $cs, '--entry', 'X.Tour.Install', '--args', ('[\"' + $spec + '\"]'))
Say ('INSTALL ' + ($raw -replace "`r?`n", ' '))

$t0 = Get-Date
while (((Get-Date) - $t0).TotalSeconds -lt 1800) {
    Read-New
    if (Test-Path $done) { break }
    Start-Sleep -Milliseconds 400
}
Read-New

if (Test-Path $done) { Say ('DONE ' + (Get-Content $done -Raw).Trim()) }
else { Say 'DONE-MISSING (the tour never signalled completion)' }

# INTERRUPTED PATH (no done marker, or no FINISH= line in the log): put every tile this session did
# not re-shoot back from the backup, so a killed tour can never again take the delivered set with it.
$finished = (Test-Path $done) -or (@(Lines-With '\[X\] FINISH=').Count -gt 0)
Say ('SESSION-FINISHED ' + $finished)
if (-not $finished) { Restore-FromBackup 'session-interrupted' | Out-Null }

foreach ($l in @(Lines-With '\[X\] (VERDICT|TOUR-DONE|FINISH|SUMMARY|NOTES)')) { Say ('EVIDENCE ' + $l) }
foreach ($l in @(Lines-With '\[X\] (STATION-FATAL|STATION-TIMEOUT|SHOT-TIMEOUT|CK-FALLBACK|CHK )')) { Say ('EVIDENCE ' + $l) }
foreach ($l in @(Lines-With '\[X\] (X1-|X3-|X6|X7-|X9-|E1-HIT|E4-RESULT|CALIB|STARTUP-SETTINGS)')) { Say ('EVIDENCE ' + $l) }
foreach ($l in @(Lines-With '\] \[Error\]')) { Say ('EVIDENCE ' + $l) }

$consoleJson = Unity-Cmd @('console_status') -Quiet
Say ('CONSOLE ' + (Clip $consoleJson 600))

Unity-Cmd @('editor_stop') -Quiet | Out-Null
Say 'STOPPED'

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

# ---- TILE AUDIT: every tile the session DECLARED (one GRID= line each) must really be on disk ----
Say '==== TILE AUDIT (Test-Path per declared tile) ===='
$gridTiles = @($script:lines |
    Where-Object { $_ -match '\[X\] GRID=(\S+) tile=(\S+)' } |
    ForEach-Object { [regex]::Match($_, '\[X\] GRID=(\S+) tile=(\S+)').Groups[2].Value } |
    Select-Object -Unique)
$auditFail = 0
$auditMissing = ''
foreach ($t in $gridTiles) {
    $p = Join-Path $shots $t
    if (Test-Path $p) {
        $fi = Get-Item $p
        Say ('AUDIT PASS  ' + $t + ' bytes=' + $fi.Length + ' ' + $fi.LastWriteTime.ToString('HH:mm:ss'))
    } else {
        $auditFail++
        if ($auditMissing.Length -gt 0) { $auditMissing += ',' }
        $auditMissing += $t
        Say ('AUDIT FAIL  ' + $t + ' declared by a GRID= line but NOT on disk')
    }
}
Say ('AUDIT declared=' + $gridTiles.Count + ' missing=' + $auditFail)
if ($auditFail -gt 0) { Say ('AUDIT missingNames=' + $auditMissing) }

[System.IO.File]::WriteAllLines($frozen, [string[]]$script:lines, (New-Object System.Text.UTF8Encoding($false)))
Say ('LOGWRITE ' + $frozen + ' lines=' + $script:lines.Count)

if ($Tag -eq 'run1') {
    Copy-Tiles $aside1 | Out-Null
} else {
    Copy-Tiles $aside2 | Out-Null
    if ((Test-Path $aside1) -and (Test-Path $aside2)) {
        $py = & python $cmpPy $aside1 $aside2 (Join-Path $shots 'x_evidence_run1.txt') $cmpOut (Join-Path $shots 'x_evidence_run2.txt') 2>&1 | Out-String
        foreach ($ln in ($py -split "`r?`n")) { if ($ln.Trim().Length -gt 0) { Say ('COMPARE ' + $ln) } }
    } else {
        Say 'COMPARE-SKIPPED (run1 aside dir missing)'
    }
    # the delivered tile set + the frozen log + the index must all come from the SAME run
    $n = 0
    foreach ($t in $tileNames) {
        $p1 = Join-Path $aside1 $t
        if (Test-Path $p1) { Copy-Item $p1 (Join-Path $shots $t) -Force; $n++ }
    }
    Say ('RESTORED run1 tiles into the delivery dir: ' + $n)
    [System.IO.File]::WriteAllLines($frozen, [string[]]$script:lines, (New-Object System.Text.UTF8Encoding($false)))
    Build-Sheet
    Say ('SHEET-FILES index=' + (Test-Path $index) + ' flow=' + (Test-Path (Join-Path $shots 'x_contact_flow.png')) + ' game=' + (Test-Path (Join-Path $shots 'x_contact_game.png')))
    exit
}

Build-Sheet
Say ('SHEET-FILES index=' + (Test-Path $index) + ' flow=' + (Test-Path (Join-Path $shots 'x_contact_flow.png')) + ' game=' + (Test-Path (Join-Path $shots 'x_contact_game.png')))

Say ('SUMMARY tag=' + $Tag + ' cfgOk=' + $cfgOk + ' done=' + (Test-Path $done) +
     ' tiles=' + $landed + '/' + $tileNames.Count +
     ' verdicts=' + (@(Lines-With '\[X\] VERDICT').Count) +
     ' grids=' + (@(Lines-With '\[X\] GRID=').Count) +
     ' timeouts=' + (@(Lines-With 'STATION-TIMEOUT|SHOT-TIMEOUT').Count) +
     ' fallbacks=' + (@(Lines-With 'CK-FALLBACK|FALLBACK').Count) +
     ' logLines=' + $script:lines.Count)
Say ('END cli=' + $script:cli)
