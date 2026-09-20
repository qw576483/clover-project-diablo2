# =============================================================================
# p38_run.ps1 -- one-off driver for agent-38 (the "yellow placeholder flashes while walking" defect)
#
#   powershell -NoProfile -ExecutionPolicy Bypass -File p38_run.ps1 -Tag r1 [-KeepPlay]
#
# ONE fresh Play session: stop -> clear_console -> play -> short tour (Boot -> MainMenu ->
# SINGLE PLAYER -> roster row 0 ENTER -> Stage -> settle -> baseline A -> walk) ->
# the tour writes READY MID-WALK -> THIS script captures a camera render from the CLI while the
# player is still walking -> writes GO -> the tour finishes the walk, reads B and asserts
# (B - A) == 0 -> DONE -> console read -> stop.
# NOT shipped: lives in <project>/.ai-tmp/drivers/ and is deleted before delivery.
# ASCII only (PS 5.1 parses a BOM-less non-ASCII .ps1 as ANSI).
# =============================================================================

param(
    [string]$Tag = 'r1',
    [string]$Why = 'agent-38 move-flash-yellow-placeholder: the assertion is a RUNTIME counter (ViewModule.PlaceholderTicksOf) read before/after a real 8-direction walk, and the deliverable ALSO needs one camera render showing the Amazon walking with original frames instead of the yellow square -- both only exist in a live Play session',
    [switch]$KeepPlay
)

$ErrorActionPreference = 'Continue'

$proj     = 'C:\Work\Server\full-dev\clover-project-diablo2\client'
$root     = 'C:\Work\Server\full-dev\clover-project-diablo2'
$cs       = $root + '\.ai-tmp\drivers\p38_drive.cs'
$outDir   = $root + '\.ai-tmp\drivers\out'
$shotDir  = $proj + '\Assets\Screenshots'
$shotName = 'p38_move_no_flash.png'
$ready    = $outDir + '\p38_ready_' + $Tag + '.txt'
$go       = $outDir + '\p38_go_' + $Tag + '.txt'
$done     = $outDir + '\p38_done_' + $Tag + '.txt'
$trace    = $outDir + '\p38_steps_' + $Tag + '.txt'
$logCopy  = $outDir + '\p38_log_' + $Tag + '.txt'
$logPath  = $proj + '\Logs\Editor.log'
$playLog  = $root + '\.ai-tmp\test\play-log.tsv'

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
            $_ -match '\[P38\]|\[View\]|\[Flow\]|Error|Exception' })
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

function Run-Step([string]$action, [string]$arg) {
    $json = '[\"' + $action + '\",\"' + $arg + '\"]'
    $raw = Unity-Cmd @('run_script', '--file', $cs, '--entry', 'P38.Drive.Step', '--args', $json)
    $result = '?'
    try { $j = $raw | ConvertFrom-Json; $result = [string]$j.data.result.result } catch { $result = 'PARSE-FAIL' }
    Say ('STEP ' + $action + ' arg=' + $arg + ' result=' + $result)
    return $result
}

# =============================================================================
# main
# =============================================================================
if (-not (Test-Path $outDir)) { New-Item -ItemType Directory -Path $outDir | Out-Null }
Set-Content -Path $trace -Value ('# p38 steps tag=' + $Tag) -Encoding UTF8
Say ('BEGIN tag=' + $Tag)
Say ('PROJECT ' + $proj)

$probeBytes = [System.IO.File]::ReadAllBytes($cs)
$nonAscii = 0
foreach ($b in $probeBytes) { if ($b -gt 127) { $nonAscii++ } }
Say ('PROBE-ASCII bytes=' + $probeBytes.Length + ' nonAscii=' + $nonAscii)

$status = & unity status --format json --no-pager --project-path $proj 2>&1 | Out-String
Say ('UNITY-STATUS ' + ($status -replace "`r?`n", ' '))
$rc = Unity-Cmd @('recompile_status') -Quiet
Say ('RECOMPILE ' + ($rc -replace "`r?`n", ' '))

# every file this round writes is cleared first so a stale file can never pass for a fresh one
$shot = Join-Path $shotDir $shotName
if (Test-Path $shot) { Remove-Item $shot -Force; Say ('TILE-CLEARED ' + $shotName) } else { Say ('TILE-ABSENT ' + $shotName) }
foreach ($m in @($ready, $go, $done)) {
    if (Test-Path $m) { Remove-Item $m -Force; Say ('MARKER-CLEARED ' + $m) } else { Say ('MARKER-ABSENT ' + $m) }
}

# play budget ledger: one line per editor_play, written BEFORE entering play
Add-Content -Path $playLog -Value ((Get-Date).ToString('yyyy-MM-dd HH:mm') + "`tclover-impl`tagent-38-move-flash-yellow-placeholder`t" + $Why) -Encoding UTF8
Say ('PLAYLOG-APPENDED ' + $playLog)

Unity-Cmd @('editor_stop') -Quiet | Out-Null
Start-Sleep -Seconds 2
Unity-Cmd @('clear_console') -Quiet | Out-Null
Init-Offset
Say ('LOG-OFFSET ' + $script:offset)
Unity-Cmd @('editor_play') -Quiet | Out-Null
Start-Sleep -Seconds 7

$cfgOk = $false
for ($i = 0; $i -lt 10; $i++) {
    $r = Run-Step 'cfg' ''
    if ($r -match 'gameRunning=1') { $cfgOk = $true; break }
    Start-Sleep -Seconds 3
}
Say ('CFG-OK ' + $cfgOk)
$tickProbe = Run-Step 'ticks' ''
Say ('TICKS-PROBE ' + $tickProbe)

# hand the probe its paths (shot dir + the three handshake files)
$specPaths = ($shotDir -replace '\\', '/') + '|' + ($ready -replace '\\', '/') + '|' + ($go -replace '\\', '/') + '|' + ($done -replace '\\', '/')
Run-Step 'paths' $specPaths | Out-Null

$spec = 'tour|' + $Tag + '|' + ($shotDir -replace '\\', '/') + '|' + ($ready -replace '\\', '/') + '|' + ($go -replace '\\', '/') + '|' + ($done -replace '\\', '/')
$raw = Unity-Cmd @('run_script', '--file', $cs, '--entry', 'P38.Tour.Install', '--args', ('[\"' + $spec + '\"]'))
Say ('INSTALL ' + ($raw -replace "`r?`n", ' '))

# ---- phase 1: wait for READY (the tour is MID-WALK and holds that state for us) ----------------
$t0 = Get-Date
while (((Get-Date) - $t0).TotalSeconds -lt 180) {
    Read-New
    if (Test-Path $ready) { break }
    if (Test-Path $done) { break }
    if ((Lines-With 'STEP-TIMEOUT').Count -gt 0) { break }
    Start-Sleep -Milliseconds 200
}
Read-New
Say ('READY=' + (Test-Path $ready))

# ---- phase 2: the camera render, issued from the CLI while the player is still walking ---------
# source=camera on purpose: source=screen returns a cached frame when the editor is unfocused.
if (Test-Path $ready) {
    Start-Sleep -Milliseconds 400
    $cap = Unity-Cmd @('capture_game_view', '--source', 'camera',
                       '--save_path', ('Assets/Screenshots/' + $shotName))
    Say ('CAPTURE ' + ($cap -replace "`r?`n", ' '))
} else {
    Say 'CAPTURE-SKIPPED (no READY marker)'
}
Say ('CAPTURE-FILE exists=' + (Test-Path $shot))
if (Test-Path $shot) {
    $fi = Get-Item $shot
    Say ('CAPTURE-STAT bytes=' + $fi.Length + ' written=' + $fi.LastWriteTime.ToString('yyyy-MM-dd HH:mm:ss.fff'))
}
Set-Content -Path $go -Value ('GO tag=' + $Tag + ' ' + (Get-Date).ToString('HH:mm:ss.fff')) -Encoding UTF8
Say ('GO-WRITTEN ' + $go)

# ---- phase 3: wait for DONE (walk finished + the B - A assertion ran) --------------------------
$t1 = Get-Date
while (((Get-Date) - $t1).TotalSeconds -lt 180) {
    Read-New
    if (Test-Path $done) { break }
    if ((Lines-With 'STEP-TIMEOUT').Count -gt 0) { break }
    Start-Sleep -Milliseconds 200
}
Read-New

if (Test-Path $done) { Say ('DONE ' + (Get-Content $done -Raw).Trim()) }
else { Say 'DONE-MISSING (probe never signalled completion)' }

foreach ($l in @(Lines-With '\[P38\] CFG '))              { Say ('EVIDENCE ' + $l) }
foreach ($l in @(Lines-With '\[P38\] WALK-BEGIN'))        { Say ('EVIDENCE ' + $l) }
foreach ($l in @(Lines-With '\[P38\] BASELINE'))          { Say ('EVIDENCE ' + $l) }
foreach ($l in @(Lines-With '\[P38\] MIDWALK'))           { Say ('EVIDENCE ' + $l) }
foreach ($l in @(Lines-With '\[P38\] WALK-END'))          { Say ('EVIDENCE ' + $l) }
foreach ($l in @(Lines-With '\[P38\] VERDICT'))           { Say ('EVIDENCE ' + $l) }
foreach ($l in @(Lines-With '\[P38\] WALK-MOVE #'))       { Say ('EVIDENCE ' + $l) }
foreach ($l in @(Lines-With '\[P38\] WALK-SAMPLE #'))     { Say ('EVIDENCE ' + $l) }
foreach ($l in @(Lines-With '\[P38\] ANIMROW'))           { Say ('EVIDENCE ' + $l) }
foreach ($l in @(Lines-With '\[P38\] ANIMDUMP'))          { Say ('EVIDENCE ' + $l) }
foreach ($l in @(Lines-With 'TOUR-DONE'))                 { Say ('EVIDENCE ' + $l) }
foreach ($l in @(Lines-With 'STEP-TIMEOUT'))              { Say ('EVIDENCE ' + $l) }
foreach ($l in @(Lines-With 'CHAIN '))                    { Say ('EVIDENCE ' + $l) }
foreach ($l in @(Lines-With 'MARKER-'))                   { Say ('EVIDENCE ' + $l) }

$consoleJson = Unity-Cmd @('console_status') -Quiet
Say ('CONSOLE ' + ($consoleJson -replace "`r?`n", ' '))

$tiles = @()
foreach ($f in (Get-ChildItem -Path $shotDir -Filter 'p38_*.png' -ErrorAction SilentlyContinue | Sort-Object Name)) {
    $tiles += ($f.Name + ':' + $f.Length + ':' + $f.LastWriteTime.ToString('HH:mm:ss'))
}
Say ('TILES count=' + $tiles.Count + ' ' + ($tiles -join ' '))

[System.IO.File]::WriteAllLines($logCopy, [string[]]$script:lines, (New-Object System.Text.UTF8Encoding($false)))
Say ('LOGWRITE ' + $logCopy + ' lines=' + $script:lines.Count)

if (-not $KeepPlay) {
    Unity-Cmd @('editor_stop') -Quiet | Out-Null
    Say 'STOPPED'
} else {
    Say 'KEEP-PLAY (editor left running for inspection)'
}

Say ('SUMMARY tag=' + $Tag + ' ready=' + (Test-Path $ready) + ' captured=' + (Test-Path $shot) +
     ' done=' + (Test-Path $done) + ' cfgOk=' + $cfgOk +
     ' moves=' + (@(Lines-With '\[P38\] WALK-MOVE #').Count) +
     ' samples=' + (@(Lines-With '\[P38\] WALK-SAMPLE #').Count) +
     ' verdicts=' + (@(Lines-With '\[P38\] VERDICT').Count) +
     ' timeouts=' + (@(Lines-With 'STEP-TIMEOUT').Count) +
     ' tiles=' + $tiles.Count + ' logLines=' + $script:lines.Count)
Say ('END cli=' + $script:cli)
