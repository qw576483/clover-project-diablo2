# =============================================================================
# p36_run.ps1 -- one-off driver for agent-36 (engine sink A5: text-render hook / E19)
#
#   powershell -NoProfile -ExecutionPolicy Bypass -File p36_run.ps1 -Tag r1 [-KeepPlay]
#
# ONE fresh Play session: stop -> clear_console -> play -> short tour (Boot -> MainMenu -> raise the
# engine widgets) -> the tour writes READY -> THIS script captures 1920x1080 from the CLI ->
# writes GO -> the tour runs the in-session equivalence probe -> DONE -> console read -> stop.
# NOT shipped: lives in <project>/.ai-tmp/drivers/ and is deleted before delivery.
# ASCII only (PS 5.1 parses a BOM-less non-ASCII .ps1 as ANSI).
# =============================================================================

param(
    [string]$Tag = 'r1',
    [string]$Why = 'agent-36 engine sink A5 (TextHooks / E19): the engine widget text (CJK Toast + LoadingLayer) is a PRESENTATION-class judgement (which font produced the pixels) and the unregistered-equivalence proof needs a live UIManager; both only exist in Play',
    [switch]$KeepPlay
)

$ErrorActionPreference = 'Continue'

$proj     = 'C:\Work\Server\full-dev\clover-project-diablo2\client'
$root     = 'C:\Work\Server\full-dev\clover-project-diablo2'
$cs       = $root + '\.ai-tmp\drivers\p36_drive.cs'
$outDir   = $root + '\.ai-tmp\drivers\out'
$shotDir  = $proj + '\Assets\Screenshots'
$ready    = $outDir + '\p36_ready_' + $Tag + '.txt'
$go       = $outDir + '\p36_go_' + $Tag + '.txt'
$done     = $outDir + '\p36_done_' + $Tag + '.txt'
$trace    = $outDir + '\p36_steps_' + $Tag + '.txt'
$logCopy  = $outDir + '\p36_log_' + $Tag + '.txt'
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
            $_ -match '\[P36\]|\[Cfg\]|\[Ui\]|\[Resource\]|\[Flow\]|\[App\]|Error|Exception' })
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
    $raw = Unity-Cmd @('run_script', '--file', $cs, '--entry', 'P36.Drive.Step', '--args', $json)
    $result = '?'
    try { $j = $raw | ConvertFrom-Json; $result = [string]$j.data.result.result } catch { $result = 'PARSE-FAIL' }
    Say ('STEP ' + $action + ' arg=' + $arg + ' result=' + $result)
    return $result
}

# =============================================================================
# main
# =============================================================================
if (-not (Test-Path $outDir)) { New-Item -ItemType Directory -Path $outDir | Out-Null }
Set-Content -Path $trace -Value ('# p36 steps tag=' + $Tag) -Encoding UTF8
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

# every tile / marker this round writes is cleared first, so a stale file can never pass for a fresh one
foreach ($f in @('p36_engine_text.png', 'p36_engine_text_native.png')) {
    $p = Join-Path $shotDir $f
    if (Test-Path $p) { Remove-Item $p -Force; Say ('TILE-CLEARED ' + $f) } else { Say ('TILE-ABSENT ' + $f) }
}
foreach ($m in @($ready, $go, $done)) {
    if (Test-Path $m) { Remove-Item $m -Force; Say ('MARKER-CLEARED ' + $m) } else { Say ('MARKER-ABSENT ' + $m) }
}

# play budget ledger: one line per editor_play, written BEFORE entering play
Add-Content -Path $playLog -Value ((Get-Date).ToString('yyyy-MM-dd HH:mm') + "`tclover-impl`tagent-36-engine-sink-A5-text-hook`t" + $Why) -Encoding UTF8
Say ('PLAYLOG-APPENDED ' + $playLog)

Unity-Cmd @('editor_stop') -Quiet | Out-Null
Start-Sleep -Seconds 2
Unity-Cmd @('clear_console') -Quiet | Out-Null
Init-Offset
Say ('LOG-OFFSET ' + $script:offset)
Unity-Cmd @('editor_play') -Quiet | Out-Null
Start-Sleep -Seconds 6

$cfgOk = $false
for ($i = 0; $i -lt 10; $i++) {
    $r = Run-Step 'cfg' ''
    if ($r -match 'gameRunning=1') { $cfgOk = $true; break }
    Start-Sleep -Seconds 3
}
Say ('CFG-OK ' + $cfgOk)

# hand the probe its paths (shot dir + the three handshake files)
$specPaths = ($shotDir -replace '\\', '/') + '|' + ($ready -replace '\\', '/') + '|' + ($go -replace '\\', '/') + '|' + ($done -replace '\\', '/')
Run-Step 'paths' $specPaths | Out-Null

$spec = 'tour|' + $Tag + '|' + ($shotDir -replace '\\', '/') + '|' + ($ready -replace '\\', '/') + '|' + ($go -replace '\\', '/') + '|' + ($done -replace '\\', '/')
$raw = Unity-Cmd @('run_script', '--file', $cs, '--entry', 'P36.Tour.Install', '--args', ('[\"' + $spec + '\"]'))
Say ('INSTALL ' + ($raw -replace "`r?`n", ' '))

# ---- phase 1: wait for READY (the tour has raised the widgets and let the UI settle) -----------
$t0 = Get-Date
while (((Get-Date) - $t0).TotalSeconds -lt 120) {
    Read-New
    if (Test-Path $ready) { break }
    if (Test-Path $done) { break }
    if ((Lines-With 'STEP-TIMEOUT').Count -gt 0) { break }
    Start-Sleep -Milliseconds 200
}
Read-New
Say ('READY=' + (Test-Path $ready))

# ---- phase 2: the 1920x1080 capture, issued from the CLI while the probe holds the frame ------
if (Test-Path $ready) {
    Start-Sleep -Seconds 1
    $cap = Unity-Cmd @('capture_game_view', '--source', 'screen', '--width', '1920', '--height', '1080',
                       '--save_path', 'Assets/Screenshots/p36_engine_text.png')
    Say ('CAPTURE ' + ($cap -replace "`r?`n", ' '))
} else {
    Say 'CAPTURE-SKIPPED (no READY marker)'
}
$captured = Join-Path $shotDir 'p36_engine_text.png'
Say ('CAPTURE-FILE ' + $captured + ' exists=' + (Test-Path $captured))
Set-Content -Path $go -Value ('GO tag=' + $Tag + ' ' + (Get-Date).ToString('HH:mm:ss.fff')) -Encoding UTF8
Say ('GO-WRITTEN ' + $go)

# ---- phase 3: wait for DONE (equivalence probe ran) -------------------------------------------
$t1 = Get-Date
while (((Get-Date) - $t1).TotalSeconds -lt 120) {
    Read-New
    if (Test-Path $done) { break }
    if ((Lines-With 'STEP-TIMEOUT').Count -gt 0) { break }
    Start-Sleep -Milliseconds 200
}
Read-New

if (Test-Path $done) { Say ('DONE ' + (Get-Content $done -Raw).Trim()) }
else { Say 'DONE-MISSING (probe never signalled completion)' }

foreach ($l in @(Lines-With '\[P36\] TEXTSCAN'))       { Say ('EVIDENCE ' + $l) }
foreach ($l in @(Lines-With '\[P36\] WIDGET '))       { Say ('EVIDENCE ' + $l) }
foreach ($l in @(Lines-With '\[P36\] WIDGET-DUMP'))   { Say ('EVIDENCE ' + $l) }
foreach ($l in @(Lines-With '\[P36\] RAISE '))        { Say ('EVIDENCE ' + $l) }
foreach ($l in @(Lines-With '\[P36\] EQUIV'))         { Say ('EVIDENCE ' + $l) }
foreach ($l in @(Lines-With '\[P36\] CFG '))          { Say ('EVIDENCE ' + $l) }
foreach ($l in @(Lines-With '\[P36\] SAMPLE state=')) { Say ('EVIDENCE ' + $l) }
foreach ($l in @(Lines-With 'TOUR-DONE'))             { Say ('EVIDENCE ' + $l) }
foreach ($l in @(Lines-With 'STEP-TIMEOUT'))           { Say ('EVIDENCE ' + $l) }
foreach ($l in @(Lines-With 'CHAIN '))                { Say ('EVIDENCE ' + $l) }
foreach ($l in @(Lines-With 'MARKER-'))               { Say ('EVIDENCE ' + $l) }

$consoleJson = Unity-Cmd @('console_status') -Quiet
Say ('CONSOLE ' + ($consoleJson -replace "`r?`n", ' '))

$tiles = @()
foreach ($f in (Get-ChildItem -Path $shotDir -Filter 'p36_*.png' -ErrorAction SilentlyContinue | Sort-Object Name)) {
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

Say ('SUMMARY tag=' + $Tag + ' ready=' + (Test-Path $ready) + ' captured=' + (Test-Path $captured) +
     ' done=' + (Test-Path $done) + ' cfgOk=' + $cfgOk +
     ' scans=' + (@(Lines-With '\[P36\] TEXTSCAN').Count) +
     ' timeouts=' + (@(Lines-With 'STEP-TIMEOUT').Count) +
     ' tiles=' + $tiles.Count + ' logLines=' + $script:lines.Count)
Say ('END cli=' + $script:cli)
