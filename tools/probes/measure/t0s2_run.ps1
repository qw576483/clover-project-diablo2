# =============================================================================
# t0s2_run.ps1 -- ONE Play session for slice t0-s2-recapture.
#
#   powershell -NoProfile -ExecutionPolicy Bypass -File .ai-tmp/test/t0s2_run.ps1
#
# This is an ADAPTED COPY of the committed runner tools/probes/drivers/t0i_run.ps1
# (that dir is frozen by the task's hard constraints, so it is reused, not edited).
# Differences vs the original:
#   * $root is derived from .ai-tmp/test (2 levels up), not tools/probes/drivers (3)
#   * the frozen evidence file is t0-s2recap_evidence.txt (the original overwrites
#     t0i_evidence.txt, which the S2 block's L4945/L4947 rows still cite)
#   * done marker / run log renamed t0s2_*
# The chain reuses the COMMITTED driver tools/probes/drivers/t0i_drive.cs unchanged.
#
# Chain: Boot -> MainMenu -> CharSelect -> CharCreate (real-click a class, sweep Confirm)
#        -> CharSelect -> Loading -> Stage -> LOAD/MEM/steady pacing -> T0FIX-H rebuild
#        measured with a manual PumpRebuild stopwatch -> real BloodMoor area change with
#        per-frame readings + a full-screen shot (mean_lum) -> finish.
#
# Why this MUST run in Play (SKILL 2 / unity-cli "a batch action, not a per-item loop"):
#   the enter-stage load wall-clock, the footprint (Profiler.GetTotalAllocatedMemoryLong /
#   MonoUsedSize), the 180-frame pacing + cpuMainThreadFrameTime and the render device name
#   only exist in a live session; the T0FIX-H rebuild split (per-frame PumpRebuild / one-frame
#   SwapToBuilt) cost exists only at runtime; and the post-area-change VISIBILITY (is the
#   buffer-built map actually on screen, or black again) is a per-frame runtime observation.
#
# ASCII only (PS 5.1 parses a BOM-less non-ASCII .ps1 as ANSI -> verify item 14).
# =============================================================================
$ErrorActionPreference = 'Continue'

$root  = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$proj  = Join-Path $root 'client'
$cs    = Join-Path $root 'tools\probes\drivers\t0i_drive.cs'
$shots = Join-Path $root '.ai-tmp\screenshots'
$test  = Join-Path $root '.ai-tmp\test'
$done  = Join-Path $test 't0s2_done.txt'
$runLog = Join-Path $test 't0s2_runlog.txt'
$frozen = Join-Path $shots 't0-s2recap_evidence.txt'
$logPath = Join-Path $proj 'Logs\Editor.log'
$playLog = Join-Path $test 'play-log.tsv'
$runbg  = Join-Path $root 'tools\probes\interact\p_runbg.cs'

$why = 'S2 recapture: the enter-stage load wall-clock, the footprint (Profiler totals), the 180-frame pacing + cpuMainThreadFrameTime + the render device name exist only in a live session, the per-frame PumpRebuild/SwapToBuilt cost is runtime-only, and whether the buffer-built map is actually visible after the area change (mean_lum, not black again) is a per-frame runtime observation'

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
        $keep = @($txt -split "`r?`n" | Where-Object { $_ -match '^\[\d{4}-\d{2}-\d{2} ' })
        foreach ($l in $keep) { [void]$script:lines.Add($l) }
    } catch {
        Say ('WARN log-read ' + $_.Exception.Message)
    } finally {
        if ($fs -ne $null) { $fs.Close(); $fs.Dispose() }
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
            if ($d) {
                $r = 'COMPILE-ERRORS:' + $d.Count
                foreach ($dd in @($d | Select-Object -First 8)) { Say ('DIAG ' + (Clip ([string]$dd.message) 400)) }
            }
        }
        $result = [string]$r
    } catch { $result = 'PARSE-FAIL' }
    Say ('STEP ' + $entry + ' result=' + $result)
    return $result
}

foreach ($d in @($shots, $test)) { if (-not (Test-Path $d)) { New-Item -ItemType Directory -Path $d | Out-Null } }
Set-Content -Path $runLog -Value '# t0s2 run log' -Encoding UTF8
Say ('BEGIN root=' + $root)

$probeBytes = [System.IO.File]::ReadAllBytes($cs)
$nonAscii = @($probeBytes | Where-Object { $_ -gt 127 }).Count
Say ('PROBE-ASCII bytes=' + $probeBytes.Length + ' nonAscii=' + $nonAscii)

if (Test-Path $done) { Remove-Item $done -Force; Say 'CLEARED done marker' }
if (Test-Path $frozen) { Remove-Item $frozen -Force }
foreach ($p in @('t0i_confirm_n.png','t0i_confirm_h.png','t0i_confirm_p.png','t0i_areachange_mid.png','t0i_areachange_after.png')) {
    $f = Join-Path $shots $p
    if (Test-Path $f) { Remove-Item $f -Force }
}

Add-Content -Path $playLog -Value ((Get-Date).ToString('yyyy-MM-dd HH:mm') + "`tclover-impl`tt0-s2-recapture`t" + $why) -Encoding UTF8
Say ('PLAYLOG-APPENDED ' + $playLog)

$status = & unity status --format json --no-pager --no-banner --project-path $proj 2>&1 | Out-String
Say ('UNITY-STATUS ' + ($status -replace "`r?`n", ' '))

Unity-Cmd @('editor_focus') -Quiet | Out-Null
Start-Sleep -Seconds 1
Unity-Cmd @('editor_stop') -Quiet | Out-Null
Start-Sleep -Seconds 2

$recomp = Unity-Cmd @('recompile') -Quiet
$upToDate = $false
for ($i = 0; $i -lt 60; $i++) {
    Start-Sleep -Seconds 2
    $st = Unity-Cmd @('recompile_status') -Quiet
    if ($st -match 'up_to_date|completed|idle') { $upToDate = $true; break }
    if ($st -match 'failed') { break }
}
Say ('RECOMPILE-SETTLED upToDate=' + $upToDate + ' raw=' + (Clip $recomp 200))
if (-not $upToDate) {
    Say 'RECOMPILE-FAILED -- aborting before Play'
    Say 'END'
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
    $r = Run-Step 'T0I.Api2.Cfg' ''
    if ($r -match 'gameRunning=1') { $cfgOk = $true; break }
    Start-Sleep -Seconds 3
}
Say ('CFG-OK ' + $cfgOk)

if ($cfgOk) {
    $spec = ($shots -replace '\\', '/') + '|' + ($done -replace '\\', '/')
    $raw = Unity-Cmd @('run_script', '--file', $cs, '--entry', 'T0I.Tour.Install', '--args', ('[\"' + $spec + '\"]'))
    Say ('INSTALL ' + (Clip $raw 300))

    $t0 = Get-Date
    while (((Get-Date) - $t0).TotalSeconds -lt 900) {
        Read-New
        if (Test-Path $done) { break }
        Start-Sleep -Milliseconds 400
    }
    Read-New
    if (Test-Path $done) { Say ('DONE ' + (Get-Content $done -Raw).Trim()) }
    else { Say 'DONE-MISSING (the plan never signalled completion)' }
}

Read-New
foreach ($l in @($script:lines | Where-Object { $_ -match '\[T0I\]' })) { Say ('EV ' + (Clip $l 3000)) }

$consoleJson = Unity-Cmd @('console_status') -Quiet
Say ('CONSOLE-RAW ' + ($consoleJson -replace "`r?`n", ' '))

Unity-Cmd @('editor_stop') -Quiet | Out-Null
Say 'STOPPED'

[System.IO.File]::WriteAllLines($frozen, [string[]]$script:lines, (New-Object System.Text.UTF8Encoding($false)))
Say ('LOGWRITE ' + $frozen + ' lines=' + $script:lines.Count)

Say ('SUMMARY cfgOk=' + $cfgOk + ' done=' + (Test-Path $done) + ' logLines=' + $script:lines.Count + ' cli=' + $script:cli)
Say 'END'
