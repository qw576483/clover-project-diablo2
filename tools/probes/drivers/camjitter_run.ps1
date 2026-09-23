# =============================================================================
# camjitter_run.ps1 -- ONE Play session that freezes the per-frame camera-follow trace.
#
#   powershell -NoProfile -ExecutionPolicy Bypass -File camjitter_run.ps1 -Tag before [-Why "..."]
#
# Chain: editor_focus -> editor_stop -> clear_console -> editor_play -> CamJit.Api.Cfg ->
#        install CamJit.Tour driver -> the driver walks Boot -> MainMenu -> CharSelect -> Stage,
#        drives the character along six zig-zag legs and writes ONE TSV row per frame
#        (frame / dt / player.World / Camera.main.position) -> wait for the done marker ->
#        dump the [CAMJIT] lines out of Editor.log -> editor_stop.
#
# The judging numbers are recomputed OFFLINE from the frozen TSV by camjitter_metrics.py;
# nothing in this script interprets them.
#
# ASCII only (PS 5.1 parses a BOM-less non-ASCII .ps1 as ANSI).
# =============================================================================
param(
    [string]$Tag = 'before',
    [string]$Why = 'cam-jitter before/after baseline: the criterion is the per-frame relative offset between the player and Camera.main at the real frame cadence, which exists only inside a live Play session (Camera.main is written by the in-Update tick chain of App/Bootstrap)',
    # * S1 (U27): the driver's Cfg() used to PIN vSync=0/targetFps=60 (camjitter_drive.cs),
    # so a re-run could never show the client's own cadence. -KeepCadence uses CfgKeepCadence,
    # which leaves Core/FramePacing's pin untouched => the recorded dt IS the shipped cadence.
    [switch]$KeepCadence,
    # * S1 (U27): append two per-frame GC columns (gcc/gcm) to the TSV via the tour spec's 5th
    # field "gc". Default OFF => the TSV keeps exactly its previous column set.
    [switch]$Gc
)

$ErrorActionPreference = 'Continue'

$root    = 'c:/Work/Server/f-v2/clover-project-diablo2'
# NOTE: editor discovery is cwd-relative and --project-path only matches the WINDOWS form of the
# path (measured: '--project-path c:/...' returns STATUS_NO_INSTANCES while the editor is ready).
# So this runner cd's into the project and passes no --project-path at all.
$proj    = 'c:\Work\Server\f-v2\clover-project-diablo2\client'
$cs      = $root + '/tools/probes/drivers/camjitter_drive.cs'
$outDir  = $root + '/.ai-tmp/test'
$tsv     = $outDir + '/cam-jitter-' + $Tag + '.tsv'
$done    = $outDir + '/cam-jitter-done-' + $Tag + '.txt'
$trace   = $outDir + '/cam-jitter-steps-' + $Tag + '.txt'
$logCopy = $outDir + '/cam-jitter-log-' + $Tag + '.txt'
$logPath = $proj + '/Logs/Editor.log'
$playLog = $outDir + '/play-log.tsv'

# * S1 (U27): which Cfg entry runs this session (resolved here so the play-log line below can record it).
$cfgEntry = if ($KeepCadence) { 'CamJit.Api.CfgKeepCadence' } else { 'CamJit.Api.Cfg' }

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
        $keep = @($txt -split "`r?`n" | Where-Object { $_ -match '\[CAMJIT\]' })
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
    $raw = & unity command @argv --format json --no-pager 2>&1 | Out-String
    if (-not $Quiet) { Say ('CLI ' + ($argv -join ' ') + ' -> ' + $raw.Length + ' bytes') }
    return $raw
}

function Run-Step([string]$entry, [string]$arg) {
    if ([string]::IsNullOrEmpty($arg)) {
        $raw = Unity-Cmd @('run_script', '--file', $cs, '--entry', $entry)
    } else {
        # PS 5.1 does not interpret backslash; the literal \" is what the CLI's arg parser needs
        # to see (measured: '["a|b"]' reaches it as '[a|b]' => INVALID_COMMAND_ARGS).
        $raw = Unity-Cmd @('run_script', '--file', $cs, '--entry', $entry, '--args', ('[\"' + $arg + '\"]'))
    }
    $result = '?'
    try { $j = $raw | ConvertFrom-Json; $result = [string]$j.data.result.result } catch { $result = 'PARSE-FAIL' }
    Say ('STEP ' + $entry + ' arg=' + $arg + ' result=' + $result)
    return $result
}

# =============================================================================
Set-Content -Path $trace -Value ('# camjitter steps tag=' + $Tag) -Encoding UTF8
Set-Location -LiteralPath $proj
Say ('BEGIN tag=' + $Tag)
Say ('PROJECT ' + (Get-Location).Path)

$probeBytes = [System.IO.File]::ReadAllBytes($cs)
$nonAscii = 0
foreach ($b in $probeBytes) { if ($b -gt 127) { $nonAscii++ } }
Say ('PROBE-ASCII bytes=' + $probeBytes.Length + ' nonAscii=' + $nonAscii)

$status = & unity status --format json --no-pager 2>&1 | Out-String
Say ('UNITY-STATUS ' + ($status -replace "`r?`n", ' '))

foreach ($p in @($tsv, $done)) { if (Test-Path $p) { Remove-Item $p -Force; Say ('CLEARED ' + $p) } }

# Bookkeeping line. The piece column is 'S1' (the old literal 'g2-resume' was a stale hard-coded
# name); the cadence suffix is appended ONLY when -KeepCadence is used, so the default line keeps
# the exact shape of the previous script. NOTE: this line is accounting, not measurement - the
# measured path (tour legs / TSV columns / dt maths) is untouched by this piece.
$logLine = (Get-Date).ToString('yyyy-MM-dd HH:mm') + "`tS1`tcam-jitter-" + $Tag + "`t" + $Why
if ($KeepCadence) { $logLine += " [keepCadence=1 entry=" + $cfgEntry + "]" }
if ($Gc) { $logLine += " [gcColumns=1]" }
Add-Content -Path $playLog -Value $logLine -Encoding UTF8
Say ('PLAYLOG-APPENDED ' + $playLog)

Unity-Cmd @('editor_focus') -Quiet | Out-Null
Unity-Cmd @('editor_stop')  -Quiet | Out-Null
Start-Sleep -Seconds 2
Unity-Cmd @('clear_console') -Quiet | Out-Null
Init-Offset
Say ('LOG-OFFSET ' + $script:offset)
Unity-Cmd @('editor_play') -Quiet | Out-Null
Start-Sleep -Seconds 7

Say ('CADENCE-MODE entry=' + $cfgEntry + ' keepCadence=' + $(if ($KeepCadence) { 1 } else { 0 }))
$cfgOk = $false
for ($i = 0; $i -lt 8; $i++) {
    $r = Run-Step $cfgEntry ''
    if ($r -match 'CFG-OK|gameRunning=1') { $cfgOk = $true; break }
    Start-Sleep -Seconds 3
}
Say ('CFG-OK ' + $cfgOk)

$spec = $Tag + '|g66|' + ($tsv -replace '\\', '/') + '|' + ($done -replace '\\', '/')
if ($Gc) { $spec += '|gc' }        # * S1 (U27): 5th spec field => driver appends gcc/gcm columns
$raw = Unity-Cmd @('run_script', '--file', $cs, '--entry', 'CamJit.Tour.Install', '--args', ('[\"' + $spec + '\"]'))
Say ('INSTALL ' + ($raw -replace "`r?`n", ' '))

$t0 = Get-Date
while (((Get-Date) - $t0).TotalSeconds -lt 260) {
    Read-New
    if (Test-Path $done) { break }
    if ((Lines-With 'STEP-TIMEOUT').Count -gt 0) {
        if ((Lines-With 'FINISH').Count -gt 0) { break }
    }
    Start-Sleep -Milliseconds 400
}
Read-New

if (Test-Path $done) { Say ('DONE ' + (Get-Content $done -Raw).Trim()) }
else { Say 'DONE-MISSING (the driver never signalled completion)' }

foreach ($l in $script:lines) { Say ('EVIDENCE ' + $l) }

$consoleJson = Unity-Cmd @('console_status') -Quiet
Say ('CONSOLE ' + ($consoleJson -replace "`r?`n", ' '))

Unity-Cmd @('editor_stop') -Quiet | Out-Null
Say 'STOPPED'

if (Test-Path $tsv) {
    $fi = Get-Item $tsv
    $rows = (Get-Content $tsv | Measure-Object -Line).Lines
    Say ('TSV-LANDED ' + $tsv + ' bytes=' + $fi.Length + ' lines=' + $rows + ' ' + $fi.LastWriteTime.ToString('HH:mm:ss'))
} else {
    Say ('TSV-MISSING ' + $tsv + ' (the driver never wrote it)')
}

[System.IO.File]::WriteAllLines($logCopy, [string[]]$script:lines, (New-Object System.Text.UTF8Encoding($false)))
Say ('LOGWRITE ' + $logCopy + ' lines=' + $script:lines.Count)
Say ('SUMMARY tag=' + $Tag + ' cfgOk=' + $cfgOk + ' done=' + (Test-Path $done) + ' tsv=' + (Test-Path $tsv) + ' cli=' + $script:cli)
Say 'END'
