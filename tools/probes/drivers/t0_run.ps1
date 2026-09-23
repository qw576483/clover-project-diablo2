# =============================================================================
# t0_run.ps1 -- one T0 Play session.
#
#   powershell -NoProfile -ExecutionPolicy Bypass -File t0_run.ps1 -Phase s1
#   powershell -NoProfile -ExecutionPolicy Bypass -File t0_run.ps1 -Phase s2
#
# s1 = main session: Boot->MainMenu->CharSelect->CharCreate->CharSelect->Loading->Stage
#      ->Pause->Options->Resume->MainMenu, plus head-60 + steady frame pacing, the MapView
#      ShowArea / RefreshVisibleChunks spikes, the D6 fx entries, and the S3 settings write
#      (real SettingsPanel handlers).  Ends with a console_status read.
# s2 = cold start: editor_stop -> clear_console -> editor_play -> read the persisted setting
#      keys back (S3 persistence) -> console_status (clean read) -> stop.
#
# ASCII only (PS 5.1 parses a BOM-less non-ASCII .ps1 as ANSI).
# =============================================================================
param(
    [string]$Phase = 's1'
)

$ErrorActionPreference = 'Continue'

$root  = Split-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) -Parent
$proj  = Join-Path $root 'client'
$cs    = Join-Path $root 'tools\probes\drivers\t0_drive.cs'
$shots = Join-Path $root '.ai-tmp\screenshots'
$test  = Join-Path $root '.ai-tmp\test'
$done  = Join-Path $test ('t0_done_' + $Phase + '.txt')
$runLog = Join-Path $test ('t0_runlog_' + $Phase + '.txt')
$frozen = Join-Path $shots ('t0_evidence_' + $Phase + '.txt')
$logPath = Join-Path $proj 'Logs\Editor.log'
$playLog = Join-Path $test 'play-log.tsv'
$runbg  = Join-Path $root 'tools\probes\interact\p_runbg.cs'

$why1 = 's1 full-flow Play: the 66 T0 rows need one real chain (Boot/MainMenu/CharSelect/CharCreate/Loading/Stage/Pause/Resume/ToMain) for the D12 per-station side effects (panels/timeScale/timers), plus head60+steady frame pacing with the device name, plus the MapView ShowArea+RefreshVisibleChunks single-frame spikes (us/GO), plus the D6 fx entries (PlayHit/PlayDeath/ShowFloatingText) driven through the real DamagePipeline, plus the S3 settings write via the SettingsPanel handlers'
$why2 = 's2 cold start: the S3 rows are "still effective after restart" and can only be judged by a second cold boot reading the persisted keys back (audio/bgm_volume, audio/sfx_volume, video/quality, video/fullscreen, audio/bgm_mute, audio/sfx_mute); the same minimal session gives the clean console_status read (clear_console then editor_play then one tiny run)'

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

foreach ($d in @($shots, $test)) { if (-not (Test-Path $d)) { New-Item -ItemType Directory -Path $d | Out-Null } }
Set-Content -Path $runLog -Value ('# t0 run log phase=' + $Phase) -Encoding UTF8
Say ('BEGIN phase=' + $Phase + ' root=' + $root)

$probeBytes = [System.IO.File]::ReadAllBytes($cs)
$nonAscii = @($probeBytes | Where-Object { $_ -gt 127 }).Count
Say ('PROBE-ASCII bytes=' + $probeBytes.Length + ' nonAscii=' + $nonAscii)

if (Test-Path $done) { Remove-Item $done -Force; Say 'CLEARED done marker' }
if (Test-Path $frozen) { Remove-Item $frozen -Force }

$why3 = 's3 short Play: the S2 内存占用 rows need a real footprint reading (Profiler.GetTotalAllocatedMemoryLong / MonoUsedSize) together with the render device name -- this cannot be obtained offline'
if ($Phase -eq 's1') { $why = $why1 } elseif ($Phase -eq 's3') { $why = $why3 } else { $why = $why2 }
Add-Content -Path $playLog -Value ((Get-Date).ToString('yyyy-MM-dd HH:mm') + "`tclover-impl`tt0-play-verdicts`t" + $why) -Encoding UTF8
Say ('PLAYLOG-APPENDED ' + $playLog)

$status = & unity status --format json --no-pager --no-banner --project-path $proj 2>&1 | Out-String
Say ('UNITY-STATUS ' + ($status -replace "`r?`n", ' '))

Unity-Cmd @('editor_stop') -Quiet | Out-Null
Start-Sleep -Seconds 2

Unity-Cmd @('recompile') -Quiet | Out-Null
$upToDate = $false
for ($i = 0; $i -lt 40; $i++) {
    Start-Sleep -Seconds 2
    $st = Unity-Cmd @('recompile_status') -Quiet
    if ($st -match 'up_to_date|completed|idle') { $upToDate = $true; break }
}
Say ('RECOMPILE-SETTLED upToDate=' + $upToDate)
Start-Sleep -Seconds 3

Unity-Cmd @('clear_console') -Quiet | Out-Null
Init-Offset
Say ('LOG-OFFSET ' + $script:offset)
Unity-Cmd @('editor_play') -Quiet | Out-Null
Start-Sleep -Seconds 4

$bg = Unity-Cmd @('eval_file', '--file', $runbg) -Quiet
Say ('RUNBG ' + (Clip $bg 200))

$cfgOk = $false
for ($i = 0; $i -lt 8; $i++) {
    $r = Run-Step 'T0.Api2.Cfg' ''
    if ($r -match 'gameRunning=1') { $cfgOk = $true; break }
    Start-Sleep -Seconds 3
}
Say ('CFG-OK ' + $cfgOk)

if ($cfgOk) {
    if ($Phase -eq 's2') {
        Run-Step 'T0.Api2.StartupRead' 'coldstart-s2' | Out-Null
        Start-Sleep -Seconds 2
    } elseif ($Phase -eq 's3') {
        Start-Sleep -Seconds 3
        Run-Step 'T0.Api2.Mem' '' | Out-Null
        Start-Sleep -Seconds 2
    } else {
        $spec = ($shots -replace '\\', '/') + '|' + ($done -replace '\\', '/')
        $raw = Unity-Cmd @('run_script', '--file', $cs, '--entry', 'T0.Tour.Install', '--args', ('[\"' + $spec + '\"]'))
        Say ('INSTALL ' + (Clip $raw 300))

        $t0 = Get-Date
        while (((Get-Date) - $t0).TotalSeconds -lt 600) {
            Read-New
            if (Test-Path $done) { break }
            Start-Sleep -Milliseconds 400
        }
        Read-New
        if (Test-Path $done) { Say ('DONE ' + (Get-Content $done -Raw).Trim()) }
        else { Say 'DONE-MISSING (the plan never signalled completion)' }
    }
}

Read-New
foreach ($l in @($script:lines | Where-Object { $_ -match '\[T0\]' })) { Say ('EV ' + (Clip $l 900)) }

$consoleJson = Unity-Cmd @('console_status') -Quiet
Say ('CONSOLE-RAW ' + ($consoleJson -replace "`r?`n", ' '))

Unity-Cmd @('editor_stop') -Quiet | Out-Null
Say 'STOPPED'

[System.IO.File]::WriteAllLines($frozen, [string[]]$script:lines, (New-Object System.Text.UTF8Encoding($false)))
Say ('LOGWRITE ' + $frozen + ' lines=' + $script:lines.Count)

$tiles = @('t0_stage_town.png','t0_d6_monster.png','t0_d6_hit.png','t0_d6_float.png','t0_d6_death.png','t0_d12_pause.png','t0_d12_options.png')
foreach ($t in $tiles) {
    $p = Join-Path $shots $t
    if (Test-Path $p) { Say ('TILE ' + $t + ' bytes=' + (Get-Item $p).Length) } else { Say ('TILE-MISSING ' + $t) }
}

Say ('SUMMARY phase=' + $Phase + ' cfgOk=' + $cfgOk + ' done=' + (Test-Path $done) + ' logLines=' + $script:lines.Count + ' cli=' + $script:cli)
Say 'END'
