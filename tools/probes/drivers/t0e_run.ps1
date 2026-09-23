# =============================================================================
# t0e_run.ps1 -- ONE Play session for the t0-e43-hover-acceptance slice.
#
#   powershell -NoProfile -ExecutionPolicy Bypass -File t0e_run.ps1
#
# Chain: Boot -> MainMenu (pointer-convention CALIB + sweep) -> CharSelect -> CharCreate
#        -> CharSelect -> Loading -> Stage (repave watch across the whole session) ->
#        HUD sweep -> Inventory/Character/SkillTree/QuestLog/MiniMap sweeps ->
#        NpcDialog/Shop sweeps -> D2Confirm sweep -> Pause sweep -> Settings sweep ->
#        Resume -> BloodMoor (area change) -> gold setup -> REAL death (gold penalty) ->
#        DeathPanel sweep -> revive -> finish.
#
# Why this chain MUST run in Play (SKILL 2 / unity-cli "a batch action, not a per-item loop"):
#   the D4UI interaction rows need a REAL pointer hover + a REAL pointer press on every
#   interactive control (offline SpriteSwap wiring is not a presentation proof); the S2
#   MapView full-repave trigger times + the FSM/loading state at that instant exist only at
#   runtime; and the death gold penalty needs a REAL death through the production chain.
#   Splitting them would re-run the same boot/load chain several times.
#
# ASCII only (PS 5.1 parses a BOM-less non-ASCII .ps1 as ANSI).
# =============================================================================
$ErrorActionPreference = 'Continue'

$root  = Split-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) -Parent
$proj  = Join-Path $root 'client'
$cs    = Join-Path $root 'tools\probes\drivers\t0e_drive.cs'
$shots = Join-Path $root '.ai-tmp\screenshots'
$hovert = Join-Path $shots 't0e_hover'
$test  = Join-Path $root '.ai-tmp\test'
$done  = Join-Path $test 't0e_done.txt'
$runLog = Join-Path $test 't0e_runlog.txt'
$frozen = Join-Path $shots 't0e_evidence.txt'
$logPath = Join-Path $proj 'Logs\Editor.log'
$playLog = Join-Path $test 'play-log.tsv'
$runbg  = Join-Path $root 'tools\probes\interact\p_runbg.cs'

$why = 'D4UI: a REAL pointer hover + a REAL pointer press on every interactive control (offline SpriteSwap wiring is not a presentation proof); S2: the MapView full-repave trigger times + the FSM/loading state AT that instant; rows 33/47: the gold lost on a REAL death -- none of these exist outside one live session'

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
                foreach ($dd in @($d | Select-Object -First 6)) { Say ('DIAG ' + (Clip ([string]$dd.message) 400)) }
            }
        }
        $result = [string]$r
    } catch { $result = 'PARSE-FAIL' }
    Say ('STEP ' + $entry + ' result=' + $result)
    return $result
}

foreach ($d in @($shots, $test, $hovert)) { if (-not (Test-Path $d)) { New-Item -ItemType Directory -Path $d | Out-Null } }
Set-Content -Path $runLog -Value '# t0e run log' -Encoding UTF8
Say ('BEGIN root=' + $root)

$probeBytes = [System.IO.File]::ReadAllBytes($cs)
$nonAscii = @($probeBytes | Where-Object { $_ -gt 127 }).Count
Say ('PROBE-ASCII bytes=' + $probeBytes.Length + ' nonAscii=' + $nonAscii)

if (Test-Path $done) { Remove-Item $done -Force; Say 'CLEARED done marker' }
if (Test-Path $frozen) { Remove-Item $frozen -Force }
if (Test-Path $hovert) { Remove-Item -Recurse -Force $hovert; Say 'CLEARED hover tiles' }

Add-Content -Path $playLog -Value ((Get-Date).ToString('yyyy-MM-dd HH:mm') + "`tclover-impl`tt0-e43-hover-acceptance`t" + $why) -Encoding UTF8
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
    $r = Run-Step 'T0E.Api2.Cfg' ''
    if ($r -match 'gameRunning=1') { $cfgOk = $true; break }
    Start-Sleep -Seconds 3
}
Say ('CFG-OK ' + $cfgOk)

if ($cfgOk) {
    $spec = ($hovert -replace '\\', '/') + '|' + ($done -replace '\\', '/')
    $raw = Unity-Cmd @('run_script', '--file', $cs, '--entry', 'T0E.Tour.Install', '--args', ('[\"' + $spec + '\"]'))
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
foreach ($l in @($script:lines | Where-Object { $_ -match '\[T0E\]' })) { Say ('EV ' + (Clip $l 2000)) }

$consoleJson = Unity-Cmd @('console_status') -Quiet
Say ('CONSOLE-RAW ' + ($consoleJson -replace "`r?`n", ' '))

Unity-Cmd @('editor_stop') -Quiet | Out-Null
Say 'STOPPED'

[System.IO.File]::WriteAllLines($frozen, [string[]]$script:lines, (New-Object System.Text.UTF8Encoding($false)))
Say ('LOGWRITE ' + $frozen + ' lines=' + $script:lines.Count)

$tiles = @(Get-ChildItem $hovert -Filter *.png -ErrorAction SilentlyContinue)
Say ('TILES ' + $tiles.Count + ' in ' + $hovert)
Say ('SUMMARY cfgOk=' + $cfgOk + ' done=' + (Test-Path $done) + ' logLines=' + $script:lines.Count + ' tiles=' + $tiles.Count + ' cli=' + $script:cli)
Say 'END'
