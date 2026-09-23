# =============================================================================
# bgm_run.ps1 -- ONE Play session for the BGM runtime verdict (the 3 landed
#               ORIGINAL tracks really load, really PLAY, and really SWITCH when
#               the area changes: Town -> BloodMoor -> DenOfEvil), plus the
#               freeze of its evidence.
#
#   powershell -NoProfile -ExecutionPolicy Bypass -File bgm_run.ps1 [-Tag bgm1] [-Save g66]
#
# Chain: editor_focus -> editor_stop -> recompile (+ poll recompile_status)
#        -> clear_console -> editor_play -> Bgm.Api.Cfg -> install
#        tools/probes/drivers/bgm_drive.cs -> the driver boots to a Stage, records
#        the engine readback at each of the three areas and writes a done marker
#        -> wait for it -> echo the [BGM] lines -> console_status / console errors
#        -> editor_stop -> freeze .ai-tmp/screenshots/bgm_evidence_<Tag>.txt
#
# WHY THIS EXISTS: client/资源欠缺清单.md #7 (BGM) was believed to be silent /
# a placeholder; it is wired and the three original .wav files are on disk.  The
# offline host (tools/probes/hosts/audiocheck) proves the BYTES (sha256 vs the
# real D2music.mpq); this driver proves the RUNTIME half: Resources load +
# AudioSource playback + clip.name readback + a real area->track switch.  Only
# these rows are re-captured -- the shared x/w2 tours are NOT re-run.
#
# CRITERION CLASS: numeric (runtime log lines + assertions) => no contact sheet.
# The driver still writes a few bgm_00..bgm_02 context screenshots.
#
# ASCII only (PS 5.1 parses a BOM-less non-ASCII .ps1 as ANSI).
# =============================================================================
param(
    [string]$Tag = 'bgm1',
    [string]$Save = 'g66',
    [string]$Why = 'BGM runtime verdict: the 3 landed original tracks must really load from Resources, really play (engine AudioSource.clip.name readback, looping source) and really switch when the area changes Town -> BloodMoor -> DenOfEvil. A playback failure (broken import, missing clip, silent fallback, or the same track never switching) is invisible to any offline source scan or sha256 check, so only a live Play session can judge it.'
)

$ErrorActionPreference = 'Continue'

$root    = Split-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) -Parent
$proj    = Join-Path $root 'client'
$cs      = Join-Path $root 'tools\probes\drivers\bgm_drive.cs'
$test    = Join-Path $root '.ai-tmp\test'
$shots   = Join-Path $root '.ai-tmp\screenshots'
$done    = Join-Path $test ('bgm_done_' + $Tag + '.txt')
$trace   = Join-Path $test ('bgm_steps_' + $Tag + '.txt')
$frozen  = Join-Path $shots ('bgm_evidence_' + $Tag + '.txt')
$logPath = Join-Path $proj 'Logs\Editor.log'
$playLog = Join-Path $test 'play-log.tsv'

$keepRe = '\[BGM\]'

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
            if ($d.Count -gt 0) { $r = 'COMPILE-ERRORS:' + $d.Count + ' ' + (Clip (($d | ForEach-Object { $_.id + ' line ' + $_.line + ': ' + $_.message }) -join ' | ') 400) }
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
Set-Content -Path $trace -Value ('# bgm steps tag=' + $Tag) -Encoding UTF8
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

# play ledger: one line per editor_play, written BEFORE entering play (column 4 = why)
$shortWhy = $Why
if ($shortWhy.Length -gt 400) { $shortWhy = $shortWhy.Substring(0, 400) }
Add-Content -Path $playLog -Value ((Get-Date).ToString('yyyy-MM-dd HH:mm') + "`tclover-impl`tbgm-original-music`t" + $shortWhy) -Encoding UTF8
Say ('PLAYLOG-APPENDED ' + $playLog)

Unity-Cmd @('editor_focus') -Quiet | Out-Null
Unity-Cmd @('editor_stop') -Quiet | Out-Null
Start-Sleep -Seconds 2

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
for ($i = 0; $i -lt 10; $i++) {
    $r = Run-Step 'BgmDrive.Api.Cfg' ''
    if ($r -match 'CFG-OK') { $cfgOk = $true; break }
    Start-Sleep -Seconds 3
}
Say ('CFG-OK ' + $cfgOk)
Run-Step 'BgmDrive.Api.Ping' '' | Out-Null

$spec = $Tag + '|' + $Save + '|' + ($done -replace '\\', '/') + '|' + ($shots -replace '\\', '/')
$raw = Unity-Cmd @('run_script', '--file', $cs, '--entry', 'BgmDrive.Tour.Install', '--args', ('[\"' + $spec + '\"]'))
Say ('INSTALL ' + ($raw -replace "`r?`n", ' '))

$t0 = Get-Date
while (((Get-Date) - $t0).TotalSeconds -lt 300) {
    Read-New
    if (Test-Path $done) { break }
    Start-Sleep -Milliseconds 300
}
Read-New

if (Test-Path $done) { Say ('DONE ' + (Get-Content $done -Raw).Trim()) }
else { Say 'DONE-MISSING (the driver never signalled completion)' }

$t1 = Get-Date
while (((Get-Date) - $t1).TotalSeconds -lt 30) {
    Read-New
    if ((Lines-With '\[BGM\] VERDICT').Count -gt 0) { break }
    Start-Sleep -Milliseconds 500
}
Read-New
Say ('TAIL-SETTLED verdictLines=' + (@(Lines-With '\[BGM\] VERDICT').Count) + ' logLines=' + $script:lines.Count)

foreach ($l in @(Lines-With '\[BGM\] ')) { Say ('EVIDENCE ' + $l) }

$consoleJson = Unity-Cmd @('console_status') -Quiet
$consoleErrors = -1
try { $cj = $consoleJson | ConvertFrom-Json; $consoleErrors = $cj.data.result.counts.error } catch { }
Say ('CONSOLE-STATUS errors=' + $consoleErrors + ' json=' + (Clip $consoleJson 400))
$errJson = Unity-Cmd @('console', '--level', 'error', '--tail', '5') -Quiet
$errCount = -1
try { $ej = $errJson | ConvertFrom-Json; $errCount = $ej.data.result.returned } catch { }
Say ('CONSOLE-ERRORS tail=' + $errCount + ' json=' + (Clip $errJson 400))

$deviceLine = (@(Lines-With '\[BGM\] STEP env device=') | Select-Object -First 1)
if (-not $deviceLine) { $deviceLine = '(no [BGM] env line found)' }

Unity-Cmd @('editor_stop') -Quiet | Out-Null
Say 'STOPPED'

$sessionEnd = Get-Date
$hdr = New-Object System.Collections.ArrayList
[void]$hdr.Add('# BGM runtime evidence -- frozen from client/Logs/Editor.log (numeric class: runtime log + assertions)')
[void]$hdr.Add('# batch: bgm-original-music   session: ' + $sessionStart.ToString('yyyy-MM-dd HH:mm:ss') + '..' + $sessionEnd.ToString('HH:mm:ss') + '   driver: tools/probes/drivers/bgm_drive.cs')
[void]$hdr.Add('# run: powershell -NoProfile -ExecutionPolicy Bypass -File tools/probes/drivers/bgm_run.ps1 -Tag ' + $Tag)
[void]$hdr.Add('# why: judge whether the 3 landed original BGM tracks really LOAD, really PLAY and really SWITCH')
[void]$hdr.Add('#      (engine AudioSource.clip.name readback on the looping BGM source) -- invisible to any offline scan.')
[void]$hdr.Add('# ENVIRONMENT SELF-CHECK (skill 4.5):')
[void]$hdr.Add('#   device    = ' + $deviceLine)
[void]$hdr.Add('#   recompile = ' + $recompile)
[void]$hdr.Add('#   console   = console_status counts.error=' + $consoleErrors + ' (console --level error --tail 5 returned=' + $errCount + ')')
[void]$hdr.Add('# ---------------------------------------------------------------------------')
foreach ($l in @(Lines-With '\[BGM\]')) { [void]$hdr.Add($l) }
[void]$hdr.Add('# ---------------------------------------------------------------------------')
[void]$hdr.Add('# RE-RUN: powershell -NoProfile -ExecutionPolicy Bypass -File tools/probes/drivers/bgm_run.ps1 -Tag ' + $Tag)
[System.IO.File]::WriteAllLines($frozen, [string[]]$hdr, (New-Object System.Text.UTF8Encoding($false)))
Say ('FROZEN ' + $frozen + ' lines=' + $hdr.Count)

$verdictLine = (@(Lines-With '\[BGM\] VERDICT') | Select-Object -First 1)
Say ('SUMMARY tag=' + $Tag + ' cfgOk=' + $cfgOk + ' done=' + (Test-Path $done) +
     ' recompile=' + $recompile + ' consoleErrors=' + $consoleErrors +
     ' chk=' + (@(Lines-With '\[BGM\] CHK ').Count) +
     ' logLines=' + $script:lines.Count)
Say ('VERDICT-LINE ' + $verdictLine)
Say ('END cli=' + $script:cli)
