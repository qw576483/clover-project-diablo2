# =============================================================================
# uifix4_run.ps1 -- ui-fix4 slice (relay after ui-fix3): ONE Play session that
# re-runs the proven S2 tour (drag chain + waypoint chain, s2_drive.cs) with the
# extended node monitor (uifix4_dump.cs: positions for every node + screen-point
# ownership grid + every-frame drag poll), producing:
#   .ai-tmp/screenshots/uifix4_wp_panel.png   (waypoint panel, LIVE, + dump)
#   .ai-tmp/screenshots/uifix4_drag.png       (mid-drag, ghost visible)
#   .ai-tmp/screenshots/uifix4_drag_after.png (right after the drop)
#   .ai-tmp/test/uifix4_wp_dump.txt / uifix4_inv_dump.txt (node dumps + hit grid)
#   .ai-tmp/screenshots/uifix4_evidence.txt   (frozen [S2]+[UF4] log rows)
#
#   powershell -NoProfile -ExecutionPolicy Bypass -File uifix4_run.ps1 [-Tag uifix4a] [-Save g66]
#
# ASCII only.
# =============================================================================
param(
    [string]$Tag = 'uifix4a',
    [string]$Save = 'g66',
    [string]$Why = 'ui-fix4 relay: ui-fix3 changed InventoryPanel/WaypointPanel but its run never proved the three visible defects (white panel box / black block over the first destination label / ghost+residue after drag-out); this session re-verifies the waypoint panel and captures the drag MID-gesture (ui-fix3 monitor polled too slowly and missed the whole drag)'
)

$ErrorActionPreference = 'Continue'

$root    = 'c:/Work/Server/f-v2/clover-project-diablo2'
$proj    = Join-Path $root 'client'
$cs      = Join-Path $root 'tools\probes\drivers\s2_drive.cs'
$dumpCs  = Join-Path $root 'tools\probes\drivers\uifix4_dump.cs'
$test    = Join-Path $root '.ai-tmp\test'
$shots   = Join-Path $root '.ai-tmp\screenshots'
$raw     = Join-Path $test 'raw_uifix4'
$done    = Join-Path $test ('uifix4_done_' + $Tag + '.txt')
$trace   = Join-Path $test ('uifix4_steps_' + $Tag + '.txt')
$frozen  = Join-Path $shots 'uifix4_evidence.txt'
$logPath = Join-Path $proj 'Logs\Editor.log'
$playLog = Join-Path $test 'play-log.tsv'
$lock    = Join-Path $test 'play.lock'
$me      = 'ui-fix4'

$keepRe = '\[S2\]|\[UF4\]'

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
    if ($body -match '^\s*ui-fix4\b') {
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
    $raw2 = & unity command @argv --format json --no-pager 2>&1 | Out-String
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
            if ($d.Count -gt 0) { $v = 'COMPILE-ERRORS:' + $d.Count + ' ' + (Clip (($d | ForEach-Object { $_.id + ' line ' + $_.line + ': ' + $_.message }) -join ' | ') 400) }
        }
        $result = [string]$v
    } catch { $result = 'PARSE-FAIL' }
    Say ('STEP ' + $entry + ' arg=' + $arg + ' result=' + $result)
    return $result
}

# =============================================================================
# main
# =============================================================================
foreach ($d in @($shots, $test, $raw)) { if (-not (Test-Path $d)) { New-Item -ItemType Directory -Path $d | Out-Null } }
Set-Content -Path $trace -Value ('# uifix4 steps tag=' + $Tag) -Encoding UTF8
$sessionStart = Get-Date
Set-Location -LiteralPath $proj
Say ('BEGIN tag=' + $Tag + ' save=' + $Save + ' cwd=' + (Get-Location).Path)

foreach ($f in @($done, $frozen)) { if (Test-Path $f) { Remove-Item $f -Force; Say ('CLEARED ' + (Split-Path $f -Leaf)) } }

# ---- editor gate + play lock ---------------------------------------------------
# the editor must be idle BEFORE we claim the lock (skill 4.5 / team play-lock rule)
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

for ($i = 0; $i -lt 20; $i++) {
    if (-not (Test-Path $lock)) { break }
    $age = ((Get-Date) - (Get-Item $lock).LastWriteTime).TotalMinutes
    if ($age -ge 6) { Say ('LOCK-STALE age=' + [math]::Round($age, 1) + 'm -> taking it'); break }
    Say ('LOCK-BUSY age=' + [math]::Round($age, 1) + 'm -> Start-Sleep 30 (try ' + ($i + 1) + ')')
    Start-Sleep -Seconds 30
}
Set-Content -Path $lock -Value ($me + ' ' + (Get-Date).ToString('o')) -Encoding UTF8
Say ('LOCK-TAKEN ' + $lock)

$status = & unity status --format json --no-pager 2>&1 | Out-String
Say ('UNITY-STATUS ' + (Clip $status 300))

Add-Content -Path $playLog -Value ((Get-Date).ToString('yyyy-MM-dd HH:mm') + "`t" + $me + "`t" + $Tag + "`t" + $Why) -Encoding UTF8
Say ('PLAYLOG-APPENDED ' + $playLog)

Unity-Cmd @('editor_focus') -Quiet | Out-Null
Unity-Cmd @('editor_stop') -Quiet | Out-Null
Start-Sleep -Seconds 2

Unity-Cmd @('recompile') -Quiet | Out-Null
$recompile = '(unknown)'
$failed = '?'
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
if ($recompile -notmatch 'up_to_date|completed|idle') {
    Say 'ABORT recompile never settled -> releasing MY lock and stopping'
    Release-Lock
    exit 1
}
if ($failed -eq 'True') {
    Say 'ABORT recompile failed=true -> releasing MY lock and stopping'
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
    $r = Run-Step $cs 'S2.Api.Cfg' ''
    if ($r -match 'CFG-OK') { $cfgOk = $true; break }
    Start-Sleep -Seconds 3
}
Say ('CFG-OK ' + $cfgOk)
if (-not $cfgOk) {
    Say 'ABORT Api.Cfg never answered -> editor_stop + release MY lock'
    Unity-Cmd @('editor_stop') -Quiet | Out-Null
    Release-Lock
    exit 1
}
Run-Step $cs 'S2.Api.Ping' '' | Out-Null

# passive monitor first: it must be in place BEFORE the tour opens any panel
Run-Step $dumpCs 'UF4.Dump.Install' ((Join-Path $root '.ai-tmp') -replace '\\', '/')

$spec = $Tag + '|' + $Save + '|' + ($done -replace '\\', '/') + '|' + ($raw -replace '\\', '/')
$r2 = Unity-Cmd @('run_script', '--file', $cs, '--entry', 'S2.Tour.Install', '--args', ('[\"' + $spec + '\"]'))
Say ('INSTALL ' + (Clip $r2 300))

$t0 = Get-Date
while (((Get-Date) - $t0).TotalSeconds -lt 420) {
    Read-New
    if (Test-Path $done) { break }
    if ((Lines-With 'STEP-TIMEOUT').Count -gt 8) { Say 'TOO MANY STEP-TIMEOUTs -> leaving the poll early'; break }
    Start-Sleep -Milliseconds 400
}
Read-New
Start-Sleep -Seconds 3
Read-New

if (Test-Path $done) { Say ('DONE ' + (Get-Content $done -Raw).Trim()) }
else { Say 'DONE-MISSING (the driver never signalled completion)' }

$consoleJson = Unity-Cmd @('console_status') -Quiet
$consoleErrors = -1
try { $cj = $consoleJson | ConvertFrom-Json; $consoleErrors = $cj.data.result.counts.error } catch { }
Say ('CONSOLE-STATUS errors=' + $consoleErrors)

$deviceLine = (@(Lines-With '\[S2\] DEVICE device=') | Select-Object -First 1)
if (-not $deviceLine) { $deviceLine = '(no [S2] DEVICE line found)' }

Unity-Cmd @('editor_stop') -Quiet | Out-Null
Say 'STOPPED'
Release-Lock

# ---- publish the tiles ---------------------------------------------------------
if (Test-Path $raw) {
    $map = @{
        's2_01_inventory_open.png'         = 'uifix4_tour_inv_open.png'
        's2_02_drag_to_empty.png'          = 'uifix4_tour_drag_1.png'
        's2_03_drag_swap.png'              = 'uifix4_tour_drag_2.png'
        's2_04_drag_to_ground.png'         = 'uifix4_tour_drag_3.png'
        's2_05_waypoint_panel.png'         = 'uifix4_tour_wp_panel.png'
        's2_06_waypoint_travel.png'        = 'uifix4_tour_wp_travel.png'
        's2_07_bloodmoor_on_entry.png'     = 'uifix4_tour_wp_entry.png'
        's2_08_bloodmoor_after_travel.png' = 'uifix4_tour_wp_arrived.png'
    }
    foreach ($f in Get-ChildItem $raw -Filter '*.png') {
        $nm = $f.Name
        if ($map.ContainsKey($nm)) { $nm = $map[$nm] } else { $nm = 'uifix4_' + $f.Name }
        $dest = Join-Path $shots $nm
        Copy-Item $f.FullName $dest -Force
        Say ('TILE-COPIED ' + $f.Name + ' -> ' + $nm)
    }
}

# ---- freeze --------------------------------------------------------------------
$sessionEnd = Get-Date
$hdr = New-Object System.Collections.ArrayList
[void]$hdr.Add('# ui-fix4 evidence -- fresh runtime log rows ([S2] tour + [UF4] monitor), frozen from client/Logs/Editor.log')
[void]$hdr.Add('# session: ' + $sessionStart.ToString('yyyy-MM-dd HH:mm:ss') + '..' + $sessionEnd.ToString('HH:mm:ss') + '   drivers: tools/probes/drivers/{s2_drive,uifix4_dump}.cs')
[void]$hdr.Add('# run: powershell -NoProfile -ExecutionPolicy Bypass -File tools/probes/drivers/uifix4_run.ps1 -Tag ' + $Tag)
[void]$hdr.Add('# ENVIRONMENT SELF-CHECK:')
[void]$hdr.Add('#   device    = ' + $deviceLine)
[void]$hdr.Add('#   recompile = ' + $recompile + ' failed=' + $failed)
[void]$hdr.Add('#   console   = console_status counts.error=' + $consoleErrors)
[void]$hdr.Add('# ---------------------------------------------------------------------------')
foreach ($l in @(Lines-With '\[UF4\]')) { [void]$hdr.Add($l) }
foreach ($l in @(Lines-With '\[S2\]')) { [void]$hdr.Add($l) }
[System.IO.File]::WriteAllLines($frozen, [string[]]$hdr, (New-Object System.Text.UTF8Encoding($false)))
Say ('FROZEN ' + $frozen + ' lines=' + $hdr.Count)

Say ('SUMMARY tag=' + $Tag + ' cfgOk=' + $cfgOk + ' done=' + (Test-Path $done) +
     ' recompile=' + $recompile + ' failed=' + $failed + ' consoleErrors=' + $consoleErrors +
     ' uf4=' + (@(Lines-With '\[UF4\]').Count))
Say ('END cli=' + $script:cli)
