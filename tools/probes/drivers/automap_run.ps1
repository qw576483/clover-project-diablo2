# =============================================================================
# automap_run.ps1 -- ONE Play session for the original-口径 automap evidence.
#
#   powershell -NoProfile -ExecutionPolicy Bypass -File automap_run.ps1 [-Tag a1] [-Why "..."]
#
# Chain: editor_focus -> editor_stop -> clear_console -> editor_play -> install the
# driver -> it enters the stage and captures the Tab overlay in Town / BloodMoor /
# DenOfEvil (open + closed) itself -> wait for the done marker -> read [AUTOMAP] lines
# -> editor_stop -> land the tiles in <repo>/.ai-tmp/screenshots/.
#
# ASCII only (PS 5.1 parses a BOM-less non-ASCII .ps1 as ANSI).
# =============================================================================
param(
    [string]$Tag = 'a1',
    [string]$Piece = 'automap-render-fix',
    [string]$Why = 'automap-render-fix: the criterion is the RENDERED automap overlay (original MaxiMap cels + ACT1 palette blitted at isometric positions, explored vs unexplored) which exists only as composited pixels inside a live Play session'
)

$ErrorActionPreference = 'Continue'

$proj    = 'client'
$root    = Split-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) -Parent
$cs      = $root + '\tools\probes\drivers\automap_drive.cs'
$outDir  = $root + '\.ai-tmp\test\automap'
$rawDir  = $root + '\.ai-tmp\test\raw'
$shotDir = $root + '\.ai-tmp\screenshots'
$done    = $outDir + '\automap_done_' + $Tag + '.txt'
$trace   = $outDir + '\automap_steps_' + $Tag + '.txt'
$logCopy = $outDir + '\automap_log_' + $Tag + '.txt'
$logPath = $proj + '\Logs\Editor.log'
$playLog = $root + '\.ai-tmp\test\play-log.tsv'

$tileNames = @('am_Town_on_unexpl.png', 'am_Town_on_expl.png', 'am_Town_off.png',
               'am_BloodMoor_on_unexpl.png', 'am_BloodMoor_on_expl.png', 'am_BloodMoor_off.png',
               'am_DenOfEvil_on_unexpl.png', 'am_DenOfEvil_on_expl.png', 'am_DenOfEvil_off.png')

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
        $keep = @($txt -split "`r?`n" | Where-Object { $_ -match '\[AUTOMAP\]|\[Map\]' })
        foreach ($l in $keep) { [void]$script:lines.Add($l) }
    } catch {
        Say ('WARN log-read ' + $_.Exception.Message)
    } finally {
        if ($fs -ne $null) { $fs.Close(); $fs.Dispose() }
    }
}

function Unity-Cmd([string[]]$argv, [switch]$Quiet) {
    $script:cli = $script:cli + 1
    $raw = & unity command @argv --project-path $proj --format json --no-pager 2>&1 | Out-String
    if (-not $Quiet) { Say ('CLI ' + ($argv -join ' ') + ' -> ' + $raw.Length + ' bytes') }
    return $raw
}

if (-not (Test-Path $outDir)) { New-Item -ItemType Directory -Path $outDir | Out-Null }
if (-not (Test-Path $rawDir)) { New-Item -ItemType Directory -Path $rawDir | Out-Null }
if (-not (Test-Path $shotDir)) { New-Item -ItemType Directory -Path $shotDir | Out-Null }
Set-Content -Path $trace -Value ('# automap steps tag=' + $Tag) -Encoding UTF8
Say ('BEGIN tag=' + $Tag)

$probeBytes = [System.IO.File]::ReadAllBytes($cs)
$nonAscii = 0
foreach ($b in $probeBytes) { if ($b -gt 127) { $nonAscii++ } }
Say ('PROBE-ASCII bytes=' + $probeBytes.Length + ' nonAscii=' + $nonAscii)

foreach ($t in $tileNames) {
    foreach ($d in @($rawDir, $shotDir)) {
        $p = Join-Path $d $t
        if (Test-Path $p) { Remove-Item $p -Force; Say ('TILE-CLEARED ' + $p) }
    }
}
if (Test-Path $done) { Remove-Item $done -Force } else { Say 'MARKER-ABSENT' }

Add-Content -Path $playLog -Value ((Get-Date).ToString('yyyy-MM-dd HH:mm') + "`tclover-impl`t" + $Piece + "`t" + $Why) -Encoding UTF8
Say ('PLAYLOG-APPENDED ' + $playLog)

Unity-Cmd @('editor_focus') -Quiet | Out-Null
Unity-Cmd @('editor_stop')  -Quiet | Out-Null
Start-Sleep -Seconds 2
Unity-Cmd @('clear_console') -Quiet | Out-Null
Init-Offset
Say ('LOG-OFFSET ' + $script:offset)
Unity-Cmd @('editor_play') -Quiet | Out-Null
Start-Sleep -Seconds 8

$spec = ($rawDir -replace '\\', '/') + '|' + ($done -replace '\\', '/')
$raw = Unity-Cmd @('run_script', '--file', $cs, '--entry', 'AutoMapDrv.Tour.Install', '--args', ('[\"' + $spec + '\"]'))
Say ('INSTALL ' + ($raw -replace "`r?`n", ' '))

$t0 = Get-Date
while (((Get-Date) - $t0).TotalSeconds -lt 300) {
    Read-New
    if (Test-Path $done) { break }
    Start-Sleep -Milliseconds 400
}
Read-New

if (Test-Path $done) { Say ('DONE ' + (Get-Content $done -Raw).Trim()) }
else { Say 'DONE-MISSING (the driver never signalled completion)' }

foreach ($l in @($script:lines | Where-Object { $_ -match '\[AUTOMAP\] ' })) { Say ('EVIDENCE ' + $l) }

Unity-Cmd @('editor_stop') -Quiet | Out-Null
Say 'STOPPED'

$landed = 0
foreach ($t in $tileNames) {
    $src = Join-Path $rawDir $t
    $dst = Join-Path $shotDir $t
    if (Test-Path $src) {
        Copy-Item $src $dst -Force
        $fi = Get-Item $dst
        Say ('TILE-LANDED ' + $t + ' bytes=' + $fi.Length)
        $landed++
    } else {
        Say ('TILE-MISSING ' + $t)
    }
}
Say ('TILES-LANDED ' + $landed + '/' + $tileNames.Count)

[System.IO.File]::WriteAllLines($logCopy, [string[]]$script:lines, (New-Object System.Text.UTF8Encoding($false)))
Say ('LOGWRITE ' + $logCopy + ' lines=' + $script:lines.Count)
Say ('SUMMARY tag=' + $Tag + ' done=' + (Test-Path $done) + ' tiles=' + $landed + '/' + $tileNames.Count + ' logLines=' + $script:lines.Count)
Say ('END cli=' + $script:cli)
