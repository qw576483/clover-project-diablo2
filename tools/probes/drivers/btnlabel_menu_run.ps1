# =============================================================================
# btnlabel_menu_run.ps1 -- btn-label-fix slice, ONE Play session for the CONTACT SHEET
# of the global button-label colour change (ruling 1 follow-up, approved 2026-09-24).
#
# WHY (and why ONE session): the shared constant UiArt.ButtonText / UiLayoutFlow.ButtonText
# changed, so the button-label rows of NINE screens are affected.  The numeric side is
# already covered per-screen by uicheck; the PRESENTATION side ("can a human read it")
# needs one contact sheet, not one Play session per screen (SKILL 4 / unity-cli rule).
#
# REUSE NOTE (do not re-invent the chain): this file = the proven gates of my own
# tools/probes/drivers/btnlabel_run.ps1 (idle gate -> my lock -> recompile(+poll) ->
# clear_console -> play -> Game.Res gate -> tour -> console_status -> editor_stop ->
# release MY lock) + the tour driver that already walks all nine screens,
# tools/probes/drivers/t0e_drive.cs (Boot -> MainMenu -> CharSelect -> CharCreate -> Stage
# -> HUD/Inventory/Character/SkillTree/QuestLog/MiniMap -> NpcDialog/Shop -> D2Confirm ->
# Pause -> Settings -> Death).  t0e's own runner (t0e_run.ps1) takes NO play lock and uses
# the actor name "clover-impl", so it must not be run as-is while other slices are in Play.
#
#   powershell -NoProfile -ExecutionPolicy Bypass -File btnlabel_menu_run.ps1 [-Tag btnlabel-menu]
#
# Tiles land in .ai-tmp/screenshots/t0e_hover/ (outside the Unity project => never imported).
# ASCII only (PS 5.1 parses a BOM-less non-ASCII .ps1 as ANSI).
# =============================================================================
param(
    [string]$Tag = 'btnlabel-menu',
    [string]$Why = 'btn-label-fix (ruling 1 follow-up): the shared button-label colour constant changed (UiArt.ButtonText / UiLayoutFlow.ButtonText = 0.95,0.87,0.60, 4.70:1 vs the old #191919 2.79:1), so the button-label rows of NINE screens are affected; the numeric side is covered per-screen offline (uicheck), but "can a human read the label" is a presentation row and needs ONE live contact sheet across the whole menu chain -- not one Play session per screen'
)

$ErrorActionPreference = 'Continue'

$root    = 'c:/Work/Server/f-v2/clover-project-diablo2'
$proj    = Join-Path $root 'client'
$cs      = Join-Path $root 'tools\probes\drivers\t0e_drive.cs'
$test    = Join-Path $root '.ai-tmp\test'
$shots   = Join-Path $root '.ai-tmp\screenshots'
$hovert  = Join-Path $shots 't0e_hover'
$done    = Join-Path $test ('btnlabel_menu_done_' + $Tag + '.txt')
$trace   = Join-Path $test ('btnlabel_menu_steps_' + $Tag + '.txt')
$frozen  = Join-Path $shots 'btnlabel_menu_evidence.txt'
$logPath = Join-Path $proj 'Logs\Editor.log'
$playLog = Join-Path $test 'play-log.tsv'
$lock    = Join-Path $test 'play.lock'
$runbg   = Join-Path $root 'tools\probes\interact\p_runbg.cs'
$me      = 'btn-label-fix'

$keepRe = '\[T0E\]'

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
    if ($body -match '^\s*btn-label-fix\b') {
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
    $raw = & unity command @argv --project-path $proj --format json --no-pager --no-banner 2>&1 | Out-String
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
            $d = @($j.data.result.diagnostics | Where-Object { $_.severity -eq 'error' })
            if ($d.Count -gt 0) { $r = 'COMPILE-ERRORS:' + $d.Count + ' ' + (Clip (($d | ForEach-Object { $_.message }) -join ' | ') 400) }
        }
        $result = [string]$r
    } catch { $result = 'PARSE-FAIL' }
    Say ('STEP ' + $entry + ' result=' + $result)
    return $result
}

# =============================================================================
# main
# =============================================================================
foreach ($d in @($shots, $test, $hovert)) { if (-not (Test-Path $d)) { New-Item -ItemType Directory -Path $d | Out-Null } }
Set-Content -Path $trace -Value ('# btnlabel menu steps tag=' + $Tag) -Encoding UTF8
$sessionStart = Get-Date
Set-Location -LiteralPath $proj
Say ('BEGIN tag=' + $Tag + ' cwd=' + (Get-Location).Path)
foreach ($f in @($done, $frozen)) { if (Test-Path $f) { Remove-Item $f -Force; Say ('CLEARED ' + (Split-Path $f -Leaf)) } }
if (Test-Path $hovert) { Remove-Item -Recurse -Force $hovert; New-Item -ItemType Directory -Path $hovert | Out-Null; Say 'CLEARED hover tiles' }

# ---- editor idle gate BEFORE taking the lock (same rule as btnlabel_run.ps1) ----
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

# ---- play lock (NEVER delete a lock we do not own) ------------------------------
for ($i = 0; $i -lt 20; $i++) {
    if (-not (Test-Path $lock)) { break }
    $age = ((Get-Date) - (Get-Item $lock).LastWriteTime).TotalMinutes
    if ($age -ge 6) { Say ('LOCK-STALE age=' + [math]::Round($age, 1) + 'm -> taking it'); break }
    Say ('LOCK-BUSY age=' + [math]::Round($age, 1) + 'm -> Start-Sleep 30 (try ' + ($i + 1) + ')')
    Start-Sleep -Seconds 30
}
Set-Content -Path $lock -Value ($me + ' ' + (Get-Date).ToString('o')) -Encoding UTF8
Say ('LOCK-TAKEN ' + $lock)

$status = & unity status --format json --no-pager --no-banner --project-path $proj 2>&1 | Out-String
Say ('UNITY-STATUS ' + (Clip $status 300))
Add-Content -Path $playLog -Value ((Get-Date).ToString('yyyy-MM-dd HH:mm') + "`t" + $me + "`t" + $Tag + "`t" + $Why) -Encoding UTF8
Say ('PLAYLOG-APPENDED ' + $playLog)

Unity-Cmd @('editor_focus') -Quiet | Out-Null
Unity-Cmd @('editor_stop') -Quiet | Out-Null
Start-Sleep -Seconds 2

Unity-Cmd @('recompile') -Quiet | Out-Null
$recompile = '(unknown)'; $failed = '?'
for ($i = 0; $i -lt 60; $i++) {
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
if (($recompile -notmatch 'up_to_date|completed|idle') -or ($failed -eq 'True')) {
    Say 'ABORT recompile not clean -> releasing MY lock and stopping'
    Release-Lock
    exit 1
}
Start-Sleep -Seconds 3

Unity-Cmd @('clear_console') -Quiet | Out-Null
Init-Offset
Say ('LOG-OFFSET ' + $script:offset)
Unity-Cmd @('editor_focus') -Quiet | Out-Null
Unity-Cmd @('editor_play') -Quiet | Out-Null
Start-Sleep -Seconds 4
if (Test-Path $runbg) {
    $bg = Unity-Cmd @('eval_file', '--file', $runbg) -Quiet
    Say ('RUNBG ' + (Clip $bg 200))
}

$cfgOk = $false
for ($i = 0; $i -lt 10; $i++) {
    $r = Run-Step 'T0E.Api2.Cfg' ''
    if ($r -match 'gameRunning=1') { $cfgOk = $true; break }
    Start-Sleep -Seconds 3
}
Say ('CFG-OK ' + $cfgOk)
if (-not $cfgOk) {
    Say 'ABORT Api2.Cfg never answered -> editor_stop + release MY lock'
    Unity-Cmd @('editor_stop') -Quiet | Out-Null
    Release-Lock
    exit 1
}

$spec = ($hovert -replace '\\', '/') + '|' + ($done -replace '\\', '/')
$r2 = Unity-Cmd @('run_script', '--file', $cs, '--entry', 'T0E.Tour.Install', '--args', ('[\"' + $spec + '\"]'))
Say ('INSTALL ' + (Clip $r2 300))

$t0 = Get-Date
while (((Get-Date) - $t0).TotalSeconds -lt 600) {
    Read-New
    if (Test-Path $done) { break }
    Start-Sleep -Milliseconds 400
}
Read-New
Start-Sleep -Seconds 3
Read-New
if (Test-Path $done) { Say ('DONE ' + (Get-Content $done -Raw).Trim()) }
else { Say 'DONE-MISSING (the plan never signalled completion within 600s)' }

$consoleJson = Unity-Cmd @('console_status') -Quiet
$consoleErrors = -1
try { $cj = $consoleJson | ConvertFrom-Json; $consoleErrors = $cj.data.result.counts.error } catch { }
Say ('CONSOLE-STATUS errors=' + $consoleErrors)

Unity-Cmd @('editor_stop') -Quiet | Out-Null
Say 'STOPPED'
Release-Lock

$tiles = @(Get-ChildItem $hovert -Filter *.png -ErrorAction SilentlyContinue)
Say ('TILES ' + $tiles.Count + ' in ' + $hovert)

$sessionEnd = Get-Date
$hdr = New-Object System.Collections.ArrayList
[void]$hdr.Add('# btn-label-fix (ruling 1 follow-up) evidence -- menu-chain contact sheet rows, frozen from client/Logs/Editor.log')
[void]$hdr.Add('# session: ' + $sessionStart.ToString('yyyy-MM-dd HH:mm:ss') + '..' + $sessionEnd.ToString('HH:mm:ss') + '   driver: tools/probes/drivers/t0e_drive.cs')
[void]$hdr.Add('# run: powershell -NoProfile -ExecutionPolicy Bypass -File tools/probes/drivers/btnlabel_menu_run.ps1 -Tag ' + $Tag)
[void]$hdr.Add('#   recompile = ' + $recompile + ' failed=' + $failed)
[void]$hdr.Add('#   console   = console_status counts.error=' + $consoleErrors)
[void]$hdr.Add('#   tiles     = ' + $tiles.Count + ' in ' + $hovert)
[void]$hdr.Add('# ---------------------------------------------------------------------------')
foreach ($l in @(Lines-With '\[T0E\]')) { [void]$hdr.Add($l) }
[System.IO.File]::WriteAllLines($frozen, [string[]]$hdr, (New-Object System.Text.UTF8Encoding($false)))
Say ('FROZEN ' + $frozen + ' lines=' + $hdr.Count)
Say ('SUMMARY tag=' + $Tag + ' cfgOk=' + $cfgOk + ' done=' + (Test-Path $done) + ' recompile=' + $recompile +
     ' failed=' + $failed + ' consoleErrors=' + $consoleErrors + ' tiles=' + $tiles.Count + ' t0e=' + (@(Lines-With '\[T0E\]').Count))
Say ('END cli=' + $script:cli)
