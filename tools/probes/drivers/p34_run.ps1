# =============================================================================
# p34_run.ps1 -- one-off driver for agent-34 (engine sink A3: Exists / LoadAll)
#
#   powershell -NoProfile -ExecutionPolicy Bypass -File p34_run.ps1 -Tag r1 [-KeepPlay]
#
# ONE fresh Play session: stop -> clear_console -> play -> short tour (main menu / stage HUD /
# inventory) + the resource probe -> marker -> console read -> stop.
# NOT shipped: lives in <project>/.ai-tmp/drivers/ and is deleted before delivery.
# ASCII only (PS 5.1 parses a BOM-less non-ASCII .ps1 as ANSI).
# =============================================================================

param(
    [string]$Tag = 'r1',
    [string]$Why = 'agent-34: one fresh Play session for the UI presentation evidence (original panel art + inv* icons + CJK bitmap font) and the new synchronous engine entries (Exists / LoadAll) probed live',
    [switch]$KeepPlay
)

$ErrorActionPreference = 'Continue'

$proj     = 'client'
$root     = 'clover-project-diablo2'
$cs       = $root + '\.ai-tmp\drivers\p34_drive.cs'
$outDir   = $root + '\.ai-tmp\drivers\out'
$shotDir  = $proj + '\Assets\Screenshots'
$marker   = $outDir + '\p34_done_' + $Tag + '.txt'
$trace    = $outDir + '\p34_steps_' + $Tag + '.txt'
$logCopy  = $outDir + '\p34_log_' + $Tag + '.txt'
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
            $_ -match '\[P34\]|\[Cfg\]|\[Ui\]|\[Resource\]|\[Flow\]|\[App\]|\[Item\]|Error|Exception' })
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
    $raw = Unity-Cmd @('run_script', '--file', $cs, '--entry', 'P34.Drive.Step', '--args', $json)
    $result = '?'
    try { $j = $raw | ConvertFrom-Json; $result = [string]$j.data.result.result } catch { $result = 'PARSE-FAIL' }
    Say ('STEP ' + $action + ' arg=' + $arg + ' result=' + $result)
    return $result
}

# =============================================================================
# main
# =============================================================================
if (-not (Test-Path $outDir)) { New-Item -ItemType Directory -Path $outDir | Out-Null }
Set-Content -Path $trace -Value ('# p34 steps tag=' + $Tag) -Encoding UTF8
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

# every tile this round writes is deleted first so a stale file can never be mistaken for a fresh one
foreach ($f in @('p34_mm.png', 'p34_hud.png', 'p34_inv.png', 'p34_opt.png')) {
    $p = Join-Path $shotDir $f
    if (Test-Path $p) { Remove-Item $p -Force; Say ('TILE-CLEARED ' + $f) }
}
if (Test-Path $marker) { Remove-Item $marker -Force; Say ('MARKER-CLEARED ' + $marker) } else { Say ('MARKER-ABSENT ' + $marker) }

# play budget ledger: one line per editor_play, written BEFORE entering play
Add-Content -Path $playLog -Value ((Get-Date).ToString('yyyy-MM-dd HH:mm') + "`tclover-impl`tagent-34-engine-sink-A3-resource-exists-loadall`t" + $Why) -Encoding UTF8
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

$spec = 'tour|' + $Tag + '|' + ($shotDir -replace '\\', '/') + '|' + ($marker -replace '\\', '/')
$raw = Unity-Cmd @('run_script', '--file', $cs, '--entry', 'P34.Tour.Install', '--args', ('[\"' + $spec + '\"]'))
Say ('INSTALL ' + ($raw -replace "`r?`n", ' '))

$t0 = Get-Date
$deadline = 180
while (((Get-Date) - $t0).TotalSeconds -lt $deadline) {
    Read-New
    if (Test-Path $marker) { break }
    if ((Lines-With 'STEP-TIMEOUT').Count -gt 0) { break }
    Start-Sleep -Milliseconds 200
}
Read-New

if (Test-Path $marker) { Say ('MARKER ' + (Get-Content $marker -Raw).Trim()) }
else { Say 'MARKER-MISSING (probe never signalled completion)' }

foreach ($l in @(Lines-With '\[P34\] RES '))        { Say ('EVIDENCE ' + $l) }
foreach ($l in @(Lines-With '\[P34\] INV-OPEN='))   { Say ('EVIDENCE ' + $l) }
foreach ($l in @(Lines-With '\[P34\] CLICK-DONE'))  { Say ('EVIDENCE ' + $l) }
foreach ($l in @(Lines-With '\[P34\] SAMPLE state=')) { Say ('EVIDENCE ' + $l) }
foreach ($l in @(Lines-With 'TOUR-DONE'))           { Say ('EVIDENCE ' + $l) }
foreach ($l in @(Lines-With 'STEP-TIMEOUT'))        { Say ('EVIDENCE ' + $l) }
foreach ($l in @(Lines-With 'CHAIN '))              { Say ('EVIDENCE ' + $l) }
foreach ($l in @(Lines-With 'SHOT-GRID'))           { Say ('EVIDENCE ' + $l) }

$consoleJson = Unity-Cmd @('console_status') -Quiet
Say ('CONSOLE ' + ($consoleJson -replace "`r?`n", ' '))

$tiles = @()
foreach ($f in (Get-ChildItem -Path $shotDir -Filter 'p34_*.png' -ErrorAction SilentlyContinue | Sort-Object Name)) {
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

Say ('SUMMARY tag=' + $Tag + ' marked=' + (Test-Path $marker) + ' cfgOk=' + $cfgOk +
     ' resLines=' + (@(Lines-With '\[P34\] RES ').Count) +
     ' timeouts=' + (@(Lines-With 'STEP-TIMEOUT').Count) +
     ' tiles=' + $tiles.Count + ' logLines=' + $script:lines.Count)
Say ('END cli=' + $script:cli)
