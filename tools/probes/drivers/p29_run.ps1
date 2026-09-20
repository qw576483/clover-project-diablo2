# =============================================================================
# p29_run.ps1 -- one-off driver for agent-29 (verify the Timer id-0 tombstone fix end to end,
#               the char-create turn-over animation, and every UI button, in ONE Play session).
# NOT shipped: lives in <project>/.ai-tmp/drivers/ and is deleted before delivery.
#
#   powershell -NoProfile -ExecutionPolicy Bypass -File p29_run.ps1 -Tag r1 [-NameSuffix P29A] [-KeepPlay]
#
# Design notes (SKILL change-loop item 5 -- sampler self-check):
#   * ASCII only (PS 5.1 parses a BOM-less non-ASCII .ps1 as ANSI).
#   * Completion signal = the marker file the PROBE ITSELF writes
#     (.ai-tmp/drivers/out/p29_done_<tag>.txt); it is deleted before the run so a stale marker
#     can never be mistaken for a fresh one. Timeouts are only a backstop, never a pass.
#   * Editor.log is read through FileShare.ReadWrite (the Editor holds it open exclusively).
#   * NO probe timer is created anywhere: this round must observe a session whose FIRST
#     CloverEngine.Timer is the engine's scene-progress poller (the shipped-build path).
#   * Screenshots are taken by the game itself (ScreenCapture at an exact frame): a 2.16 s turn-over
#     and a ~1.3 s read-bar screen are far too short for a CLI round trip, and one batch matters.
# =============================================================================

param(
    [string]$Tag = 'r1',
    [string]$NameSuffix = 'P29A',
    [string]$Why = 'agent-29: fresh session (no p_runbg => no probe timer) to verify the Timer id-0 tombstone fix (first Game.Scene.Load of the session) + the char-create fw/bw turn-over + every UI button by real clicks',
    [switch]$KeepPlay
)

$ErrorActionPreference = 'Continue'

$proj     = 'C:\Work\Server\full-dev\clover-project-diablo2\client'
$root     = 'C:\Work\Server\full-dev\clover-project-diablo2'
$cs       = $root + '\.ai-tmp\drivers\p29_drive.cs'
$outDir   = $root + '\.ai-tmp\drivers\out'
$shotDir  = $proj + '\Assets\Screenshots'
$marker   = $outDir + '\p29_done_' + $Tag + '.txt'
$trace    = $outDir + '\p29_steps_' + $Tag + '.txt'
$logCopy  = $outDir + '\p29_log_' + $Tag + '.txt'
$logPath  = $proj + '\Logs\Editor.log'
$playLog  = $root + '\.ai-tmp\test\play-log.tsv'

$script:offset = 0
$script:lines = New-Object System.Collections.ArrayList
$script:cli = 0

function Say([string]$s) {
    # Write-Host, NOT Write-Output: Write-Output would land in the pipeline result of Unity-Cmd,
    # turning its return value into a string ARRAY and making ConvertFrom-Json fail (found live:
    # the first Play run reported "PARSE-FAIL" for every step while the CLI JSON was perfectly fine).
    $stamp = (Get-Date).ToString('HH:mm:ss.fff')
    Write-Host ($stamp + ' ' + $s)
    Add-Content -Path $trace -Value ($stamp + ' ' + $s) -Encoding UTF8
}

# ---------------------------------------------------------------- log reader
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
            $_ -match '\[P29\]|\[P28\]|\[Scene\]|\[Flow\]|\[Stage\]|\[App\]|\[Ui\]|\[Timer\]|\[Resource\]|\[Input\]|Error|Exception' })
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

# ---------------------------------------------------------------- unity CLI
function Unity-Cmd([string[]]$argv, [switch]$Quiet) {
    $script:cli = $script:cli + 1
    $raw = & unity command @argv --project-path $proj --format json --no-pager 2>&1 | Out-String
    if (-not $Quiet) { Say ('CLI ' + ($argv -join ' ') + ' -> ' + $raw.Length + ' bytes') }
    return $raw
}

function Run-Step([string]$action, [string]$arg) {
    # PowerShell 5.1 mangles inner double quotes when handing argv to a native exe; the proven
    # form is a literal backslash-quote inside the argument.
    $json = '[\"' + $action + '\",\"' + $arg + '\"]'
    $raw = Unity-Cmd @('run_script', '--file', $cs, '--entry', 'P29.Drive.Step', '--args', $json)
    $result = '?'
    try { $j = $raw | ConvertFrom-Json; $result = [string]$j.data.result.result } catch { $result = 'PARSE-FAIL' }
    Say ('STEP ' + $action + ' arg=' + $arg + ' result=' + $result)
    return $result
}

function Install-Tour([string]$spec) {
    $json = '[\"' + $spec + '\"]'
    $raw = Unity-Cmd @('run_script', '--file', $cs, '--entry', 'P29.Tour.Install', '--args', $json)
    $result = '?'
    try { $j = $raw | ConvertFrom-Json; $result = [string]$j.data.result.result } catch { $result = 'PARSE-FAIL' }
    Say ('INSTALL result=' + $result + ' spec=' + $spec)
    return $result
}

# =============================================================================
# main
# =============================================================================
if (-not (Test-Path $outDir)) { New-Item -ItemType Directory -Path $outDir | Out-Null }
Set-Content -Path $trace -Value ('# p29 steps tag=' + $Tag + ' nameSuffix=' + $NameSuffix) -Encoding UTF8
Say ('BEGIN tag=' + $Tag + ' nameSuffix=' + $NameSuffix)
Say ('PROJECT ' + $proj)

# code freeze check: the probe file itself must be pure ASCII (PS 5.1 / Roslyn mangling)
$probeBytes = [System.IO.File]::ReadAllBytes($cs)
$nonAscii = 0
foreach ($b in $probeBytes) { if ($b -gt 127) { $nonAscii++ } }
Say ('PROBE-ASCII bytes=' + $probeBytes.Length + ' nonAscii=' + $nonAscii)

# status + compile gate (evidence: the session we are about to drive runs a compiled build)
$status = & unity status --format json --no-pager --project-path $proj 2>&1 | Out-String
Say ('UNITY-STATUS ' + ($status -replace "`r?`n", ' '))
$rc = Unity-Cmd @('recompile_status') -Quiet
Say ('RECOMPILE ' + ($rc -replace "`r?`n", ' '))

if (Test-Path $marker) { Remove-Item $marker -Force; Say ('MARKER-CLEARED ' + $marker) } else { Say ('MARKER-ABSENT ' + $marker) }

# play budget ledger (SKILL 1.13 T0): one line per editor_play, written BEFORE entering play
Add-Content -Path $playLog -Value ((Get-Date).ToString('yyyy-MM-dd HH:mm') + "`tclover-impl`tagent-29-tombstone-verify-and-ui-buttons`t" + $Why) -Encoding UTF8
Say ('PLAYLOG-APPENDED ' + $playLog)

# fresh session: stop -> clear console -> play
Unity-Cmd @('editor_stop') -Quiet | Out-Null
Start-Sleep -Seconds 2
Unity-Cmd @('clear_console') -Quiet | Out-Null
Init-Offset
Say ('LOG-OFFSET ' + $script:offset)
Unity-Cmd @('editor_play') -Quiet | Out-Null
Start-Sleep -Seconds 6

# play-mode + input setup (creates no timer); retry until the engine is really running
$cfgOk = $false
for ($i = 0; $i -lt 10; $i++) {
    $r = Run-Step 'cfg' ''
    if ($r -match 'gameRunning=1') { $cfgOk = $true; break }
    Start-Sleep -Seconds 3
}
Say ('CFG-OK ' + $cfgOk)

# install the tour driver (spec = tour|tag|shotDir|nameSuffix|markerPath)
$spec = 'tour|' + $Tag + '|' + ($shotDir -replace '\\', '/') + '|' + $NameSuffix + '|' + ($marker -replace '\\', '/')
Install-Tour $spec | Out-Null

$t0 = Get-Date
$deadline = 330
$finished = $false
while (((Get-Date) - $t0).TotalSeconds -lt $deadline) {
    Read-New
    if (Test-Path $marker) { $finished = $true; break }
    if ((Lines-With 'STEP-TIMEOUT').Count -gt 0) { break }
    Start-Sleep -Milliseconds 200
}
Read-New

if (Test-Path $marker) {
    Say ('MARKER ' + (Get-Content $marker -Raw).Trim())
} else {
    Say 'MARKER-MISSING (probe never signalled completion)'
}

foreach ($l in @(Lines-With 'TOUR-DONE'))     { Say ('EVIDENCE ' + $l) }
foreach ($l in @(Lines-With 'STEP-TIMEOUT'))   { Say ('EVIDENCE ' + $l) }
foreach ($l in @(Lines-With 'INFLIGHT'))       { Say ('EVIDENCE ' + $l) }
foreach ($l in @(Lines-With 'SAMPLE state='))  { Say ('EVIDENCE ' + $l) }
foreach ($l in @(Lines-With 'DUMP_BUTTONS'))   { Say ('EVIDENCE ' + $l) }
foreach ($l in @(Lines-With 'SHOT-SELF'))      { Say ('EVIDENCE ' + $l) }
foreach ($l in @(Lines-With 'BOOT-ENTRY'))     { Say ('EVIDENCE ' + $l) }

# console errors of THIS session (console was cleared right before play)
$consoleJson = Unity-Cmd @('console_status') -Quiet
Say ('CONSOLE ' + ($consoleJson -replace "`r?`n", ' '))

# tile inventory: the game wrote these itself; sizes prove they are not empty files
$tiles = @()
foreach ($f in (Get-ChildItem -Path $shotDir -Filter ('p29_' + $Tag + '_*.png') -ErrorAction SilentlyContinue | Sort-Object Name)) {
    $tiles += ($f.Name + ':' + $f.Length + ':' + $f.LastWriteTime.ToString('HH:mm:ss'))
}
Say ('TILES count=' + $tiles.Count + ' ' + ($tiles -join ' '))

# persist the filtered log region for the sheet step (Chinese-aware)
[System.IO.File]::WriteAllLines($logCopy, [string[]]$script:lines, (New-Object System.Text.UTF8Encoding($false)))
Say ('LOGWRITE ' + $logCopy + ' lines=' + $script:lines.Count)

if (-not $KeepPlay) {
    Unity-Cmd @('editor_stop') -Quiet | Out-Null
    Say 'STOPPED'
} else {
    Say 'KEEP-PLAY (editor left running for inspection)'
}

Say ('SUMMARY tag=' + $Tag + ' marked=' + (Test-Path $marker) + ' cfgOk=' + $cfgOk +
     ' tourDone=' + (@(Lines-With 'TOUR-DONE').Count) + ' timeouts=' + (@(Lines-With 'STEP-TIMEOUT').Count) +
     ' tiles=' + $tiles.Count + ' logLines=' + $script:lines.Count)
Say ('END cli=' + $script:cli)
