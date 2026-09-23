# =============================================================================
# u1_run.ps1 -- ONE Play session for the U-1 runtime verdict (direction-key family
#              / W swap / real ground click) and the freeze of its evidence.
#
#   powershell -NoProfile -ExecutionPolicy Bypass -File u1_run.ps1 [-Tag u1] [-Save g66]
#
# Chain: editor_focus -> editor_stop -> recompile (+ poll recompile_status)
#        -> clear_console -> editor_play -> Api.Cfg (+ DEVICE/MODULES self-check)
#        -> install tools/probes/drivers/u1_drive.cs -> the driver runs the whole
#        chain inside Play and writes a done marker -> wait for it -> echo the [U1]
#        lines -> console_status / console errors -> editor_stop -> freeze
#        .ai-tmp/screenshots/u1_evidence_recapture2.txt
#
# WHY THIS SCRIPT EXISTS (judging asset, clover-engine skill 3.5):
#   .ai-tmp/screenshots/u1_evidence_recapture.txt (2026-09-22 11:49) was produced by
#   the one-off probe .ai-tmp/test/u1_probe.cs, which was deleted afterwards (temp-file
#   rule) => the live chain could not be re-run from the repo.  driver + runner are now
#   committed under tools/probes/drivers/ and reproduce the SAME chain and the SAME
#   three assertions (the criterion was NOT redesigned).
#
# NUMERIC CLASS: the judged rows are runtime logs + assertions => no tiles are captured.
#
# ASCII only (PS 5.1 parses a BOM-less non-ASCII .ps1 as ANSI).
# =============================================================================
param(
    [string]$Tag = 'u1',
    [string]$Save = 'g66',
    [string]$Why = 'U-1 runtime verdict (hold the direction-key family W/A/S/D 1.5s => 0 movement; W => swap weapon group and still 0 movement; a real mouse click on the ground => the character walks): the criterion is the live InputSystem/CloverInput chain plus the runtime grid, i.e. the residual risk (a Unity-level InputActions composite binding or a real-mouse projection miss) is invisible to any offline source scan. This run also promotes the chain from the deleted one-off probe to a committed judging asset.'
)

$ErrorActionPreference = 'Continue'

$root    = Split-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) -Parent
$proj    = Join-Path $root 'client'
$cs      = Join-Path $root 'tools\probes\drivers\u1_drive.cs'
$test    = Join-Path $root '.ai-tmp\test'
$shots   = Join-Path $root '.ai-tmp\screenshots'
$done    = Join-Path $test ('u1_done_' + $Tag + '.txt')
$trace   = Join-Path $test ('u1_steps_' + $Tag + '.txt')
$frozen  = Join-Path $shots 'u1_evidence_recapture2.txt'
$logPath = Join-Path $proj 'Logs\Editor.log'
$playLog = Join-Path $test 'play-log.tsv'

# only the driver's own lines are frozen (the first evidence file has the same shape)
$keepRe = '\[U1\]'

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

function Lines-With([string]$pattern) {
    return @($script:lines | Where-Object { $_ -match $pattern })
}

function Clip([string]$s, [int]$n) {
    if ($null -eq $s) { return '' }
    $one = $s -replace "`r?`n", ' '
    if ($one.Length -le $n) { return $one }
    return $one.Substring(0, $n)
}

function Unity-Cmd([string[]]$argv, [switch]$Quiet) {
    $script:cli = $script:cli + 1
    $raw = & unity command @argv --project-path $proj --format json --no-pager 2>&1 | Out-String
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
            if ($d.Count -gt 0) { $r = 'COMPILE-ERRORS:' + $d.Count + ' ' + (Clip (($d | ForEach-Object { $_.id + ' line ' + $_.line + ': ' + $_.message }) -join ' | ') 300) }
        }
        $result = [string]$r
    } catch { $result = 'PARSE-FAIL' }
    Say ('STEP ' + $entry + ' arg=' + $arg + ' result=' + $result)
    return $result
}

# =============================================================================
# main
# =============================================================================
foreach ($d in @($shots, $test)) { if (-not (Test-Path $d)) { New-Item -ItemType Directory -Path $d | Out-Null } }
Set-Content -Path $trace -Value ('# u1 steps tag=' + $Tag) -Encoding UTF8
$sessionStart = Get-Date
Say ('BEGIN tag=' + $Tag + ' save=' + $Save + ' root=' + $root)

$probeBytes = [System.IO.File]::ReadAllBytes($cs)
$nonAscii = 0
foreach ($b in $probeBytes) { if ($b -gt 127) { $nonAscii++ } }
Say ('PROBE-ASCII bytes=' + $probeBytes.Length + ' nonAscii=' + $nonAscii)

$status = & unity status --format json --no-pager --project-path $proj 2>&1 | Out-String
Say ('UNITY-STATUS ' + ($status -replace "`r?`n", ' '))

foreach ($f in @($done, $frozen)) {
    if (Test-Path $f) { Remove-Item $f -Force; Say ('CLEARED ' + (Split-Path $f -Leaf)) }
}

# play ledger: one line per editor_play, written BEFORE entering play
Add-Content -Path $playLog -Value ((Get-Date).ToString('yyyy-MM-dd HH:mm') + "`tclover-impl`tu1-driver-promotion`t" + $Why) -Encoding UTF8
Say ('PLAYLOG-APPENDED ' + $playLog)

Unity-Cmd @('editor_focus') -Quiet | Out-Null
Unity-Cmd @('editor_stop') -Quiet | Out-Null
Start-Sleep -Seconds 2

# settle any pending script compile BEFORE entering Play (a mid-Play domain reload would
# half-initialise the session and the chain would freeze with Game.Res null)
Unity-Cmd @('recompile') -Quiet | Out-Null
$recompile = '(unknown)'
for ($i = 0; $i -lt 40; $i++) {
    Start-Sleep -Seconds 2
    $st = Unity-Cmd @('recompile_status') -Quiet
    try { $j = $st | ConvertFrom-Json; $recompile = (($j.data.result | ConvertFrom-Json).status) } catch { }
    if ($recompile -match 'up_to_date|completed|idle') { break }
}
Say ('RECOMPILE status=' + $recompile)
Start-Sleep -Seconds 3

Unity-Cmd @('clear_console') -Quiet | Out-Null
Init-Offset
Say ('LOG-OFFSET ' + $script:offset)
Unity-Cmd @('editor_play') -Quiet | Out-Null
Start-Sleep -Seconds 7

$cfgOk = $false
for ($i = 0; $i -lt 8; $i++) {
    $r = Run-Step 'U1.Api.Cfg' ''
    if ($r -match 'gameRunning=1' -or $r -match 'CFG-OK') { $cfgOk = $true; break }
    Start-Sleep -Seconds 3
}
Say ('CFG-OK ' + $cfgOk)
Run-Step 'U1.Api.Ping' '' | Out-Null

$spec = $Tag + '|' + $Save + '|' + ($done -replace '\\', '/')
$raw = Unity-Cmd @('run_script', '--file', $cs, '--entry', 'U1.Tour.Install', '--args', ('[\"' + $spec + '\"]'))
Say ('INSTALL ' + ($raw -replace "`r?`n", ' '))

$t0 = Get-Date
while (((Get-Date) - $t0).TotalSeconds -lt 240) {
    Read-New
    if (Test-Path $done) { break }
    if ((Lines-With 'STEP-TIMEOUT').Count -gt 6) { Say 'TOO MANY STEP-TIMEOUTs -> leaving the poll early'; break }
    Start-Sleep -Milliseconds 300
}
Read-New

if (Test-Path $done) { Say ('DONE ' + (Get-Content $done -Raw).Trim()) }
else { Say 'DONE-MISSING (the driver never signalled completion)' }

# The done marker is a small file written immediately, while the Editor flushes Editor.log
# with a delay -- reading the log once right after the marker can lose the LAST lines (that is
# exactly what happened on the first run: the marker existed but VERDICT/SUMMARY were not on
# disk yet).  So keep re-reading until the driver's last line (VERDICT) is in the frozen set.
$t1 = Get-Date
while (((Get-Date) - $t1).TotalSeconds -lt 30) {
    Read-New
    if ((Lines-With '\[U1\] VERDICT').Count -gt 0) { break }
    Start-Sleep -Milliseconds 500
}
Read-New
Say ('TAIL-SETTLED verdictLines=' + (@(Lines-With '\[U1\] VERDICT').Count) + ' logLines=' + $script:lines.Count)

foreach ($l in @(Lines-With '\[U1\] ')) { Say ('EVIDENCE ' + $l) }

# ---- environment self-check for the frozen header (skill 4.5) -----------------
$consoleJson = Unity-Cmd @('console_status') -Quiet
$consoleErrors = -1
try { $cj = $consoleJson | ConvertFrom-Json; $consoleErrors = $cj.data.result.counts.error } catch { }
Say ('CONSOLE-STATUS errors=' + $consoleErrors + ' json=' + (Clip $consoleJson 400))
$errJson = Unity-Cmd @('console', '--level', 'error', '--tail', '5') -Quiet
$errCount = -1
try { $ej = $errJson | ConvertFrom-Json; $errCount = $ej.data.result.returned } catch { }
Say ('CONSOLE-ERRORS tail=' + $errCount + ' json=' + (Clip $errJson 400))

$deviceLine = (@(Lines-With '\[U1\] DEVICE device=') | Select-Object -First 1)
if (-not $deviceLine) { $deviceLine = '(no [U1] DEVICE line found)' }

Unity-Cmd @('editor_stop') -Quiet | Out-Null
Say 'STOPPED'

# ---- freeze --------------------------------------------------------------------
$sessionEnd = Get-Date
$hdr = New-Object System.Collections.ArrayList
[void]$hdr.Add('# U-1 evidence re-capture #2 -- PROMOTED JUDGING ASSET (fresh runtime log, frozen from client/Logs/Editor.log)')
[void]$hdr.Add('# batch: u1-driver-promotion   session: ' + $sessionStart.ToString('yyyy-MM-dd HH:mm:ss') + '..' + $sessionEnd.ToString('HH:mm:ss') + '   driver: tools/probes/drivers/u1_drive.cs')
[void]$hdr.Add('# run: powershell -NoProfile -ExecutionPolicy Bypass -File tools/probes/drivers/u1_run.ps1 -Tag ' + $Tag)
[void]$hdr.Add('# why: u1_evidence_recapture.txt (2026-09-22 11:49) was produced by the one-off probe .ai-tmp/test/u1_probe.cs,')
[void]$hdr.Add('#      which was deleted afterwards (temp-file rule) => the chain could not be re-run from the repo. The driver is')
[void]$hdr.Add('#      now a committed judging asset; the chain, the markers and the three assertions are UNCHANGED.')
[void]$hdr.Add('# ENVIRONMENT SELF-CHECK (skill 4.5):')
[void]$hdr.Add('#   device    = ' + $deviceLine)
[void]$hdr.Add('#   recompile = ' + $recompile)
[void]$hdr.Add('#   console   = console_status counts.error=' + $consoleErrors + ' (console --level error --tail 5 returned=' + $errCount + ')')
[void]$hdr.Add('# ---------------------------------------------------------------------------')
foreach ($l in @(Lines-With '\[U1\]')) { [void]$hdr.Add($l) }
[void]$hdr.Add('# ---------------------------------------------------------------------------')
[void]$hdr.Add('# RE-RUN: powershell -NoProfile -ExecutionPolicy Bypass -File tools/probes/drivers/u1_run.ps1 -Tag ' + $Tag)
[void]$hdr.Add('#   (ONE Play session; numeric class => runtime logs + assertions, no tiles. The offline judges of the same')
[void]$hdr.Add('#    criterion are re-run by tools/verify.ps1 item 03 direction-key-move and item 18 offline-hosts')
[void]$hdr.Add('#    (uicheck / playercheck); they are the source-scan half, this file is the live half.)')
[System.IO.File]::WriteAllLines($frozen, [string[]]$hdr, (New-Object System.Text.UTF8Encoding($false)))
Say ('FROZEN ' + $frozen + ' lines=' + $hdr.Count)

$verdictLine = (@(Lines-With '\[U1\] VERDICT') | Select-Object -First 1)
Say ('SUMMARY tag=' + $Tag + ' cfgOk=' + $cfgOk + ' done=' + (Test-Path $done) +
     ' recompile=' + $recompile + ' consoleErrors=' + $consoleErrors +
     ' chk=' + (@(Lines-With '\[U1\] CHK ').Count) +
     ' logLines=' + $script:lines.Count)
Say ('VERDICT-LINE ' + $verdictLine)
Say ('END cli=' + $script:cli)
