# =============================================================================
# w2_run.ps1 -- ONE Play session of the W2 full-system tour, for one run tag.
#
#   powershell -NoProfile -ExecutionPolicy Bypass -File w2_run.ps1 -Tag run1
#   powershell -NoProfile -ExecutionPolicy Bypass -File w2_run.ps1 -Tag run2
#
# Fixed Play order (per the acceptance table's "driver environment" section, unchanged):
#   editor_stop -> clear_console -> editor_play -> eval_file _dev/p_runbg.cs
#   -> Api2.Cfg -> Tour.Install (the whole station plan runs inside Play)
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
    [string]$Why = 'W2 full-system tour (B1..H3): the rows the acceptance table lists as must-be-judged-in-Play -- panel layouts, world framing, cursor/NoWalk, camera follow, 8-direction walk frames, the hit trio (damage float + sfx + hp bar), level up, skill tree, missile cast, inventory/tooltip colours, equip/belt/potion, gold, the four quest states with dialog+quest log+shop settlement, the death screen, pause/options persistence and save-and-exit re-entry -- none of which exist outside a real play session'
)

$ErrorActionPreference = 'Continue'

$root  = Split-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) -Parent
# absolute: the CLI resolves --project-path against the CURRENT directory, and the run script is
# started with the project dir as cwd (a relative 'client' would resolve to <project>/client/client)
$proj  = Join-Path $root 'client'
$cs    = Join-Path $root 'tools\probes\drivers\w2_drive.cs'
$sheetPy = Join-Path $root 'tools\probes\measure\w2_sheet.py'
$cmpPy   = Join-Path $root 'tools\probes\measure\w2_compare.py'
$shots = Join-Path $root '.ai-tmp\screenshots'
$test  = Join-Path $root '.ai-tmp\test'
$aside1 = Join-Path $test 'w2_run1'
$aside2 = Join-Path $test 'w2_run2'
$done  = Join-Path $test ('w2_done_' + $Tag + '.txt')
$runLog = Join-Path $test ('w2_runlog_' + $Tag + '.txt')
$frozen = Join-Path $shots ('w2_evidence_' + $Tag + '.txt')
$index = Join-Path $shots 'w2_contact.index.tsv'
$cmpOut = Join-Path $shots 'w2_compare.tsv'
$logPath = Join-Path $proj 'Logs\Editor.log'
$playLog = Join-Path $test 'play-log.tsv'

$tileNames = @(
    'w2_b1_boot.png', 'w2_b2_mainmenu.png',
    'w2_b3_charselect.png', 'w2_b3_delete_confirm.png', 'w2_b4_charcreate.png',
    'w2_b5_loading_a.png', 'w2_b5_loading_b.png',
    'w2_c5_title_town.png', 'w2_c5_title_moor.png', 'w2_c5_title_den.png',
    'w2_c1_town_wide.png', 'w2_c1_town_default.png', 'w2_c4_minimap.png',
    'w2_d1_nowalk_cursor.png', 'w2_d1_detour_start.png', 'w2_d1_detour_stop.png',
    'w2_d2_follow_a.png', 'w2_d2_follow_b.png',
    'w2_d3_dir0_S.png', 'w2_d3_dir1_SW.png', 'w2_d3_dir2_W.png', 'w2_d3_dir3_NW.png',
    'w2_d3_dir4_N.png', 'w2_d3_dir5_NE.png', 'w2_d3_dir6_E.png', 'w2_d3_dir7_SE.png',
    'w2_e1_hit_a.png', 'w2_e1_hit_b.png', 'w2_e2_levelup.png',
    'w2_e3_skilltree.png', 'w2_e4_cast_a.png', 'w2_e4_cast_b.png',
    'w2_f1_inventory.png', 'w2_f1_tooltip_q0.png', 'w2_f1_tooltip_q1.png',
    'w2_f1_tooltip_q2.png', 'w2_f1_tooltip_q3.png', 'w2_f1_tooltip_q4.png',
    'w2_f1_inventory_after_pickup.png',
    'w2_f2_equip.png', 'w2_f2_belt_before.png', 'w2_f2_belt_after.png', 'w2_f3_gold.png',
    'w2_g1_dialog_notstarted.png', 'w2_g1_dialog_inprogress.png',
    'w2_g1_dialog_ready.png', 'w2_g1_dialog_done.png',
    'w2_g2_shop_buy.png', 'w2_g2_shop_sell.png', 'w2_g2_shop_repair.png',
    'w2_g3_questlog_notstarted.png', 'w2_g3_questlog_inprogress.png',
    'w2_g3_questlog_ready.png', 'w2_g3_questlog_done.png', 'w2_g4_den_cleared.png',
    'w2_h1_death.png', 'w2_h2_pause.png', 'w2_h2_options_before.png',
    'w2_h2_options_after.png', 'w2_h2_mainmenu.png', 'w2_h3_reentry.png'
)

# Project logger writes `[yyyy-MM-dd HH:mm:ss.fff] [Level] [Tag] message`; the leading timestamp
# anchor keeps .NET stack frames ("CloverEngine.Logger:Log (...)") out of the frozen evidence.
$keepRe = '^\[\d{4}-\d{2}-\d{2} [\d:.]+\] \[(Info|Warn|Error)\] \[(W2|Flow|Map|Move|Player|Combat|Monster|Skill|Item|Quest|Npc|Ui|Audio|App|Input|Camera|View|Save|Table|Cfg|R1)\]'

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

# =============================================================================
# main
# =============================================================================
foreach ($d in @($shots, $test)) { if (-not (Test-Path $d)) { New-Item -ItemType Directory -Path $d | Out-Null } }
Set-Content -Path $runLog -Value ('# w2 run log tag=' + $Tag) -Encoding UTF8
Say ('BEGIN tag=' + $Tag + ' sheetOnly=' + $SheetOnly + ' root=' + $root)

if ($SheetOnly) {
    $fs = New-Object System.IO.FileStream($logPath, [System.IO.FileMode]::Open,
          [System.IO.FileAccess]::Read, [System.IO.FileShare]::ReadWrite)
    $sr = New-Object System.IO.StreamReader($fs, [System.Text.Encoding]::UTF8)
    $all = $sr.ReadToEnd() -split "`r?`n"
    $sr.Close(); $fs.Close()
    $anchor = -1
    for ($i = 0; $i -lt $all.Length; $i++) {
        if ($all[$i] -match '\[W2\] TOUR-INSTALL spec=tour\|' + [regex]::Escape($Tag) + '\|') { $anchor = $i }
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

# stale artefacts must never pass for fresh ones
foreach ($t in $tileNames) {
    $p = Join-Path $shots $t
    if (Test-Path $p) { Remove-Item $p -Force }
}
foreach ($f in @($done, $index, $frozen)) {
    if (Test-Path $f) { Remove-Item $f -Force; Say ('CLEARED ' + (Split-Path $f -Leaf)) }
}
Say 'TILE-CLEARED all'

Add-Content -Path $playLog -Value ((Get-Date).ToString('yyyy-MM-dd HH:mm') + "`tclover-impl`tw2-tour-" + $Tag + "`t" + $Why) -Encoding UTF8
Say ('PLAYLOG-APPENDED ' + $playLog)

Unity-Cmd @('editor_stop') -Quiet | Out-Null
Start-Sleep -Seconds 2
Unity-Cmd @('clear_console') -Quiet | Out-Null
Init-Offset
Say ('LOG-OFFSET ' + $script:offset)
Unity-Cmd @('editor_play') -Quiet | Out-Null
Start-Sleep -Seconds 4

# the acceptance table's fixed order: runInBackground first, then the tour
$bg = Unity-Cmd @('eval_file', '--file', '_dev/p_runbg.cs') -Quiet
Say ('RUNBG ' + (Clip $bg 200))

$cfgOk = $false
for ($i = 0; $i -lt 6; $i++) {
    $r = Run-Step 'W2.Api2.Cfg' ''
    if ($r -match 'gameRunning=1') { $cfgOk = $true; break }
    Start-Sleep -Seconds 4
}
Say ('CFG-OK ' + $cfgOk)
Run-Step 'W2.Api2.Ping' '' | Out-Null

$spec = 'tour|' + $Tag + '|' + ($shots -replace '\\', '/') + '|' + ($done -replace '\\', '/')
$raw = Unity-Cmd @('run_script', '--file', $cs, '--entry', 'W2.Tour.Install', '--args', ('[\"' + $spec + '\"]'))
Say ('INSTALL ' + ($raw -replace "`r?`n", ' '))

$t0 = Get-Date
while (((Get-Date) - $t0).TotalSeconds -lt 1200) {
    Read-New
    if (Test-Path $done) { break }
    Start-Sleep -Milliseconds 400
}
Read-New

if (Test-Path $done) { Say ('DONE ' + (Get-Content $done -Raw).Trim()) }
else { Say 'DONE-MISSING (the tour never signalled completion)' }

foreach ($l in @(Lines-With '\[W2\] (VERDICT|TOUR-DONE|FINISH|SUMMARY|NOTES)')) { Say ('EVIDENCE ' + $l) }
foreach ($l in @(Lines-With '\[W2\] (STATION-FATAL|STATION-TIMEOUT|SHOT-TIMEOUT|CK-FALLBACK|CHK )')) { Say ('EVIDENCE ' + $l) }
foreach ($l in @(Lines-With '\[W2\] (H1-|H2-PERSIST|H3-VERIFY|G1d-RESULT|E2-|E4-RESULT|G4-CLEARED|CALIB|STARTUP-SETTINGS)')) { Say ('EVIDENCE ' + $l) }
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

[System.IO.File]::WriteAllLines($frozen, [string[]]$script:lines, (New-Object System.Text.UTF8Encoding($false)))
Say ('LOGWRITE ' + $frozen + ' lines=' + $script:lines.Count)

if ($Tag -eq 'run1') {
    Copy-Tiles $aside1 | Out-Null
} else {
    Copy-Tiles $aside2 | Out-Null
    if ((Test-Path $aside1) -and (Test-Path $aside2)) {
        $py = & python $cmpPy $aside1 $aside2 (Join-Path $shots 'w2_evidence_run1.txt') $cmpOut (Join-Path $shots 'w2_evidence_run2.txt') 2>&1 | Out-String
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
    Say ('SHEET-FILES index=' + (Test-Path $index) + ' flow=' + (Test-Path (Join-Path $shots 'w2_contact_flow.png')) + ' game=' + (Test-Path (Join-Path $shots 'w2_contact_game.png')))
    exit
}

Build-Sheet
Say ('SHEET-FILES index=' + (Test-Path $index) + ' flow=' + (Test-Path (Join-Path $shots 'w2_contact_flow.png')) + ' game=' + (Test-Path (Join-Path $shots 'w2_contact_game.png')))

Say ('SUMMARY tag=' + $Tag + ' cfgOk=' + $cfgOk + ' done=' + (Test-Path $done) +
     ' tiles=' + $landed + '/' + $tileNames.Count +
     ' verdicts=' + (@(Lines-With '\[W2\] VERDICT').Count) +
     ' grids=' + (@(Lines-With '\[W2\] GRID=').Count) +
     ' timeouts=' + (@(Lines-With 'STATION-TIMEOUT|SHOT-TIMEOUT').Count) +
     ' fallbacks=' + (@(Lines-With 'CK-FALLBACK|FALLBACK').Count) +
     ' logLines=' + $script:lines.Count)
Say ('END cli=' + $script:cli)
