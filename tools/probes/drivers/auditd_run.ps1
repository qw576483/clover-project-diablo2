# =============================================================================
# auditd_run.ps1 -- ONE Play session for the audit slice D runtime verdict
#                  (D5 animation / D6 vfx / D7 bgm / D8 sfx).
#
#   powershell -NoProfile -ExecutionPolicy Bypass -File auditd_run.ps1 [-Tag d1] [-Save g66]
#
# WHY ONE SESSION FOR FOUR DIMENSIONS: all four typical failure shapes are the
# same silent one -- "asset on disk, nothing wired, never plays".  Judging them
# needs the live game objects (SpriteAnimator cursor, SpriteRenderer.sprite.name,
# live AudioSource.clip.name, live effect nodes), which exist only inside Play.
# Re-entering Play once per dimension would repeat the whole boot chain four
# times (the skill forbids that).
#
# Chain: editor_stop -> clear_console -> editor_play -> AuditD.Api.Cfg
#        -> install tools/probes/drivers/auditd_drive.cs -> the driver boots to a
#        Stage town, drives D5/D6/D7/D8 probes, writes a done marker
#        -> echo the [AUDITD] lines -> console_status -> editor_stop -> freeze.
#
# ASCII only (PS 5.1 parses a BOM-less non-ASCII .ps1 as ANSI).
# =============================================================================
param(
    [string]$Tag = 'd1',
    [string]$Save = 'g66',
    [string]$Why = 'audit slice D runtime verdict (D5 anim / D6 vfx / D7 bgm / D8 sfx): the four dimensions share one silent failure shape - the asset is on disk but nothing is wired, so it never plays. Only live readings (SpriteAnimator frame cursor + SpriteRenderer.sprite.name, live effect node counts, live AudioSource.clip.name per scene label, per-event clip windows) can judge it; a file scan or sha256 cannot.'
)

$ErrorActionPreference = 'Continue'

$root    = Split-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) -Parent
$proj    = Join-Path $root 'client'
$cs      = Join-Path $root 'tools\probes\drivers\auditd_drive.cs'
$test    = Join-Path $root '.ai-tmp\test'
$shots   = Join-Path $root '.ai-tmp\screenshots'
$done    = Join-Path $test ('auditd_done_' + $Tag + '.txt')
$trace   = Join-Path $test ('auditd_steps_' + $Tag + '.txt')
$frozen  = Join-Path $shots ('auditd_evidence_' + $Tag + '.txt')
$logPath = Join-Path $proj 'Logs\Editor.log'
$playLog = Join-Path $test 'play-log.tsv'

$keepRe = '\[AUDITD\]'

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
            if ($d.Count -gt 0) { $r = 'COMPILE-ERRORS:' + $d.Count + ' ' + (Clip (($d | ForEach-Object { $_.id + ' line ' + $_.line + ': ' + $_.message }) -join ' | ') 500) }
        }
        $result = [string]$r
    } catch { $result = 'PARSE-FAIL ' + (Clip $raw 300) }
    Say ('STEP ' + $entry + ' result=' + $result)
    return $result
}

# =============================================================================
# main
# =============================================================================
foreach ($d in @($shots, $test)) { if (-not (Test-Path $d)) { New-Item -ItemType Directory -Path $d | Out-Null } }
Set-Content -Path $trace -Value ('# auditd steps tag=' + $Tag) -Encoding UTF8
$sessionStart = Get-Date
Say ('BEGIN tag=' + $Tag + ' save=' + $Save + ' root=' + $root)

$probeBytes = [System.IO.File]::ReadAllBytes($cs)
$nonAscii = 0
foreach ($b in $probeBytes) { if ($b -gt 127) { $nonAscii++ } }
Say ('PROBE-ASCII bytes=' + $probeBytes.Length + ' nonAscii=' + $nonAscii)

$status = & unity command editor_status --format json --no-pager --project-path $proj 2>&1 | Out-String
Say ('UNITY-STATUS ' + (Clip ($status -replace "`r?`n", ' ') 300))

foreach ($f in @($done, $frozen)) {
    if (Test-Path $f) { Remove-Item $f -Force; Say ('CLEARED ' + (Split-Path $f -Leaf)) }
}

$shortWhy = $Why
if ($shortWhy.Length -gt 400) { $shortWhy = $shortWhy.Substring(0, 400) }
Add-Content -Path $playLog -Value ((Get-Date).ToString('yyyy-MM-dd HH:mm') + "`taudit-D-play`taudit-D-four-dims`t" + $shortWhy) -Encoding UTF8
Say ('PLAYLOG-APPENDED ' + $playLog)

Unity-Cmd @('editor_focus') -Quiet | Out-Null
Unity-Cmd @('editor_stop') -Quiet | Out-Null
Start-Sleep -Seconds 2

Unity-Cmd @('clear_console') -Quiet | Out-Null
Init-Offset
Say ('LOG-OFFSET ' + $script:offset)
Unity-Cmd @('editor_play') -Quiet | Out-Null
Start-Sleep -Seconds 7

# NOTE: AuditD.Api.Cfg() answers CFG-OK as soon as the DEVICE name is readable, which is
# true while the editor is still sitting in edit mode (gameRunning=0).  Waiting only for
# "CFG-OK" once installed the driver into a dead session (measured: driver never ran, the
# done marker never appeared, the evidence file stayed at 1 line).  Gate on gameRunning=1.
$cfgOk = $false
for ($i = 0; $i -lt 20; $i++) {
    $r = Run-Step 'AuditD.Api.Cfg' ''
    if ($r -match 'CFG-OK gameRunning=1') { $cfgOk = $true; break }
    Start-Sleep -Seconds 3
}
Say ('CFG-OK ' + $cfgOk)

$spec = $Tag + '|' + $Save + '|' + ($done -replace '\\', '/') + '|' + ($shots -replace '\\', '/')
$raw = Unity-Cmd @('run_script', '--file', $cs, '--entry', 'AuditD.Tour.Install', '--args', ('[\"' + $spec + '\"]'))
Say ('INSTALL ' + (Clip ($raw -replace "`r?`n", ' ') 400))

$t0 = Get-Date
while (((Get-Date) - $t0).TotalSeconds -lt 600) {
    Read-New
    if (Test-Path $done) { break }
    Start-Sleep -Milliseconds 400
}
Read-New

if (Test-Path $done) { Say ('DONE ' + (Get-Content $done -Raw).Trim()) }
else { Say 'DONE-MISSING (the driver never signalled completion)' }

$t1 = Get-Date
while (((Get-Date) - $t1).TotalSeconds -lt 40) {
    Read-New
    if ((Lines-With '\[AUDITD\] VERDICT').Count -gt 0) { break }
    Start-Sleep -Milliseconds 500
}
Read-New
Say ('TAIL-SETTLED verdictLines=' + (@(Lines-With '\[AUDITD\] VERDICT').Count) + ' logLines=' + $script:lines.Count)

foreach ($l in @(Lines-With '\[AUDITD\] ')) { Say ('EVIDENCE ' + $l) }

$consoleJson = Unity-Cmd @('console_status') -Quiet
$consoleErrors = -1
try { $cj = $consoleJson | ConvertFrom-Json; $consoleErrors = $cj.data.result.counts.error } catch { }
Say ('CONSOLE-STATUS errors=' + $consoleErrors + ' json=' + (Clip $consoleJson 300))

$deviceLine = (@(Lines-With '\[AUDITD\] STEP env device=') | Select-Object -First 1)
if (-not $deviceLine) { $deviceLine = '(no [AUDITD] env line found)' }

Unity-Cmd @('editor_stop') -Quiet | Out-Null
Say 'STOPPED'

# the last lines can land while console_status is running (it is slow); read once more
# before freezing so the VERDICT / CHK block can never be missed.
Read-New
Say ('POST-STOP-READ logLines=' + $script:lines.Count + ' verdictLines=' + (@(Lines-With '\[AUDITD\] VERDICT').Count))
foreach ($l in @(Lines-With '\[AUDITD\] (VERDICT|CHK |D5-|D6-|D7-|D8-)')) { Say ('EVIDENCE2 ' + $l) }

$sessionEnd = Get-Date
$hdr = New-Object System.Collections.ArrayList
[void]$hdr.Add('# audit D runtime evidence -- frozen from client/Logs/Editor.log (numeric class: runtime readings + assertions)')
[void]$hdr.Add('# batch: audit-D-four-dims   session: ' + $sessionStart.ToString('yyyy-MM-dd HH:mm:ss') + '..' + $sessionEnd.ToString('HH:mm:ss') + '   driver: tools/probes/drivers/auditd_drive.cs')
[void]$hdr.Add('# run: powershell -NoProfile -ExecutionPolicy Bypass -File tools/probes/drivers/auditd_run.ps1 -Tag ' + $Tag)
[void]$hdr.Add('# why: judge D5 anim / D6 vfx / D7 bgm / D8 sfx by live readings only')
[void]$hdr.Add('# ENVIRONMENT SELF-CHECK (skill 4.5):')
[void]$hdr.Add('#   device = ' + $deviceLine)
[void]$hdr.Add('#   console = console_status counts.error=' + $consoleErrors)
[void]$hdr.Add('# ---------------------------------------------------------------------------')
foreach ($l in @(Lines-With '\[AUDITD\]')) { [void]$hdr.Add($l) }
[void]$hdr.Add('# ---------------------------------------------------------------------------')
[void]$hdr.Add('# RE-RUN: powershell -NoProfile -ExecutionPolicy Bypass -File tools/probes/drivers/auditd_run.ps1 -Tag ' + $Tag)
[System.IO.File]::WriteAllLines($frozen, [string[]]$hdr, (New-Object System.Text.UTF8Encoding($false)))
Say ('FROZEN ' + $frozen + ' lines=' + $hdr.Count)

$verdictLine = (@(Lines-With '\[AUDITD\] VERDICT') | Select-Object -First 1)
Say ('SUMMARY tag=' + $Tag + ' cfgOk=' + $cfgOk + ' done=' + (Test-Path $done) +
     ' consoleErrors=' + $consoleErrors +
     ' chk=' + (@(Lines-With '\[AUDITD\] CHK ').Count) +
     ' logLines=' + $script:lines.Count)
Say ('VERDICT-LINE ' + $verdictLine)
Say ('END cli=' + $script:cli)
