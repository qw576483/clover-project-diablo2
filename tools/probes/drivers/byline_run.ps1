# =============================================================================
# byline_run.ps1 -- ONE Play session for the SKILL 6.9 brand-byline verdict.
#
#   powershell -NoProfile -ExecutionPolicy Bypass -File byline_run.ps1 [-Tag b1] [-Why "..."]
#
# Chain: editor_focus -> editor_stop -> clear_console -> editor_play -> Api.Cfg -> install the
# byline driver -> the driver measures the ByLine node on the boot screen and on the main menu and
# captures both composited frames itself -> wait for the done marker -> read the [BYLINE] lines ->
# editor_stop -> land the tiles in <repo>/.ai-tmp/screenshots/.
#
# The rendered pixels (not the node's text property) are the criterion, so the tiles are the
# deliverable of this run; the crop/zoom step is offline (byline_crop.py).
#
# ASCII only (PS 5.1 parses a BOM-less non-ASCII .ps1 as ANSI).
# =============================================================================
param(
    [string]$Tag = 'b1',
    [string]$Why = 'byline-render-verdict: the SKILL 6.9 criterion is the RENDERED glyph shapes of the bottom brand line, which exist only as composited pixels inside a live Play session (the node text property is explicitly not accepted)'
)

$ErrorActionPreference = 'Continue'

$proj    = 'client'
$root    = Split-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) -Parent
$cs      = $root + '\tools\probes\drivers\byline_drive.cs'
$outDir  = $root + '\.ai-tmp\test\byline'
$rawDir  = $root + '\.ai-tmp\test\raw'
$shotDir = $root + '\.ai-tmp\screenshots'
$done    = $outDir + '\byline_done_' + $Tag + '.txt'
$trace   = $outDir + '\byline_steps_' + $Tag + '.txt'
$logCopy = $outDir + '\byline_log_' + $Tag + '.txt'
$logPath = $proj + '\Logs\Editor.log'
$playLog = $root + '\.ai-tmp\test\play-log.tsv'

$tileNames = @('byline_01_boot.png', 'byline_02_menu.png')

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
        $keep = @($txt -split "`r?`n" | Where-Object { $_ -match '\[BYLINE\]|\[RUNBG\]|\[HB\]' })
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

function Run-Step([string]$entry, [string]$arg) {
    if ([string]::IsNullOrEmpty($arg)) {
        $raw = Unity-Cmd @('run_script', '--file', $cs, '--entry', $entry)
    } else {
        $raw = Unity-Cmd @('run_script', '--file', $cs, '--entry', $entry, '--args', ('[\"' + $arg + '\"]'))
    }
    $result = '?'
    try { $j = $raw | ConvertFrom-Json; $result = [string]$j.data.result.result } catch { $result = 'PARSE-FAIL' }
    Say ('STEP ' + $entry + ' arg=' + $arg + ' result=' + $result)
    return $result
}

# =============================================================================
if (-not (Test-Path $outDir)) { New-Item -ItemType Directory -Path $outDir | Out-Null }
if (-not (Test-Path $rawDir)) { New-Item -ItemType Directory -Path $rawDir | Out-Null }
if (-not (Test-Path $shotDir)) { New-Item -ItemType Directory -Path $shotDir | Out-Null }
Set-Content -Path $trace -Value ('# byline steps tag=' + $Tag) -Encoding UTF8
Say ('BEGIN tag=' + $Tag)
Say ('PROJECT ' + $proj)

$probeBytes = [System.IO.File]::ReadAllBytes($cs)
$nonAscii = 0
foreach ($b in $probeBytes) { if ($b -gt 127) { $nonAscii++ } }
Say ('PROBE-ASCII bytes=' + $probeBytes.Length + ' nonAscii=' + $nonAscii)

$status = & unity status --format json --no-pager --project-path $proj 2>&1 | Out-String
Say ('UNITY-STATUS ' + ($status -replace "`r?`n", ' '))

foreach ($t in $tileNames) {
    $p = Join-Path $rawDir $t
    if (Test-Path $p) { Remove-Item $p -Force; Say ('RAWTILE-CLEARED ' + $t) }
    $q = Join-Path $shotDir $t
    if (Test-Path $q) { Remove-Item $q -Force; Say ('SHOTTILE-CLEARED ' + $t) }
}
if (Test-Path $done) { Remove-Item $done -Force; Say ('MARKER-CLEARED ' + $done) } else { Say ('MARKER-ABSENT ' + $done) }

# play budget ledger: one line per editor_play, written BEFORE entering play
Add-Content -Path $playLog -Value ((Get-Date).ToString('yyyy-MM-dd HH:mm') + "`tclover-impl`tbyline-render-verdict`t" + $Why) -Encoding UTF8
Say ('PLAYLOG-APPENDED ' + $playLog)

Unity-Cmd @('editor_focus') -Quiet | Out-Null
Unity-Cmd @('editor_stop')  -Quiet | Out-Null
Start-Sleep -Seconds 2
Unity-Cmd @('clear_console') -Quiet | Out-Null
Init-Offset
Say ('LOG-OFFSET ' + $script:offset)
Unity-Cmd @('editor_play') -Quiet | Out-Null
Start-Sleep -Seconds 7

$cfgOk = $false
for ($i = 0; $i -lt 8; $i++) {
    $r = Run-Step 'Byline.Api.Cfg' ''
    if ($r -match 'gameRunning=1') { $cfgOk = $true; break }
    Start-Sleep -Seconds 3
}
Say ('CFG-OK ' + $cfgOk)

$spec = $Tag + '|' + ($rawDir -replace '\\', '/') + '|' + ($done -replace '\\', '/')
$raw = Unity-Cmd @('run_script', '--file', $cs, '--entry', 'Byline.Tour.Install', '--args', ('[\"' + $spec + '\"]'))
Say ('INSTALL ' + ($raw -replace "`r?`n", ' '))

$t0 = Get-Date
while (((Get-Date) - $t0).TotalSeconds -lt 200) {
    Read-New
    if (Test-Path $done) { break }
    if ((Lines-With 'STEP-TIMEOUT').Count -gt 0) { break }
    Start-Sleep -Milliseconds 300
}
Read-New

if (Test-Path $done) { Say ('DONE ' + (Get-Content $done -Raw).Trim()) }
else { Say 'DONE-MISSING (the driver never signalled completion)' }

foreach ($l in @(Lines-With '\[BYLINE\] ')) { Say ('EVIDENCE ' + $l) }

$consoleJson = Unity-Cmd @('console_status') -Quiet
Say ('CONSOLE ' + ($consoleJson -replace "`r?`n", ' '))

Unity-Cmd @('editor_stop') -Quiet | Out-Null
Say 'STOPPED'

$landed = 0
foreach ($t in $tileNames) {
    $src = Join-Path $rawDir $t
    $dst = Join-Path $shotDir $t
    if (Test-Path $src) {
        Copy-Item $src $dst -Force
        $fi = Get-Item $dst
        Say ('TILE-LANDED ' + $t + ' bytes=' + $fi.Length + ' ' + $fi.LastWriteTime.ToString('HH:mm:ss'))
        $landed++
    } else {
        Say ('TILE-MISSING ' + $t + ' (the driver never produced it)')
    }
}
Say ('TILES-LANDED ' + $landed + '/' + $tileNames.Count)

[System.IO.File]::WriteAllLines($logCopy, [string[]]$script:lines, (New-Object System.Text.UTF8Encoding($false)))
Say ('LOGWRITE ' + $logCopy + ' lines=' + $script:lines.Count)
Say ('SUMMARY tag=' + $Tag + ' cfgOk=' + $cfgOk + ' done=' + (Test-Path $done) +
     ' tiles=' + $landed + '/' + $tileNames.Count + ' logLines=' + $script:lines.Count)
Say ('END cli=' + $script:cli)
