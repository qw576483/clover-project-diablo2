# =============================================================================
# automappanel_run.ps1 -- ONE Play session for the "automap panel: wrong centre /
# does it paint at all" question (piece: automap-panel).
#
#   # from .ai-tmp/drivers/ (source of truth during the slice):
#   powershell -NoProfile -ExecutionPolicy Bypass -File .ai-tmp/drivers/automappanel_run.ps1 -Tag am1
#   # promoted canonical copy (in-repo, later slices copy from here):
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools/probes/drivers/automappanel_run.ps1 -Tag am1
#   # offline lock-protocol self test (NO unity CLI, NO Play):
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools/probes/drivers/automappanel_run.ps1 -SelfTest
#
#   Chain (one session): load existing char -> walk away from the spawn -> SHOT before
#     (panel closed) -> emit Events.PanelToggleRequest("MiniMapPanel") -> readings +
#     SHOT after -> A/B pair with ONLY `_raw.enabled` flipped (the diff == exactly what
#     the automap layer contributes; without this control a 0.43%-of-screen overlay is
#     drowned by world animation noise => false "it draws nothing").
#
#   Shape reused from .ai-tmp/drivers/saveprogress_run2.ps1 (same play-lock protocol,
#   same log-offset reading, same run_script plumbing). ASCII ONLY (PS 5.1 reads a
#   UTF-8-no-BOM file as ANSI; a non-ASCII *string literal* would land in play-log.tsv
#   as mojibake => keep every literal ASCII).
#
#   ---------------------------------------------------------------------------
#   DRIVER v2 (2026-09-24): LOCK RENEWAL ONLY. v1 read-outs stay valid under v2 --
#   the change touches the lock-age scheduling value only; it cannot affect any
#   observed quantity (no product code, no probe, no assertion).
#
#   WHY (team rule, decided 2026-09-24): while holding the lock we must renew its
#   mtime before EVERY unity CLI call, because the takeover rule is
#   "age > 360 s AND playMode != playing => may be taken over".
#   Worst case after taking the lock ~= editor_status 48*5s(240) + recompile 60*2s(120)
#   + play poll 200s + post-stop log 12*3s(36) = ~10 min > 360 s
#   => one slow cold boot makes OUR OWN lock look stale and someone else takes it
#   (this is exactly how `automap-panel2` lost its lock at 02:33 on 2026-09-24).
#
#   TOUCH-LOCK CONTRACT (three constraints -- copy these when you port the pattern):
#     (1) renew ONLY the lock whose body carries OUR tag; a foreign lock is never
#         renewed and never deleted (Release-Lock refuses too);
#     (2) if our own lock file is GONE (i.e. it was taken over) => ABORT immediately
#         and DO NOT touch the editor (the new owner may be mid-Play);
#     (3) renewal happens right before each CLI call (inside Unity-Cmd), so the gap
#         between "last touch" and "next CLI" is always ~0.
#   Take over a stale lock only when "age >= 360 s AND playMode != playing".
#   ---------------------------------------------------------------------------
#
#   NOTE on the probe: the C# probe stays at .ai-tmp/drivers/automappanel_probe.cs
#   (it is shared with the concurrent automap-panel2 slice => do not move it).
#   Override with -Probe <path>.
# =============================================================================
param(
    [string]$Tag = 'am',
    [string]$Why = '',
    [string]$Probe = '',
    [switch]$SelfTest
)
$ErrorActionPreference = 'Continue'

$lockTag = 'automap-panel'

if ([string]::IsNullOrEmpty($Why)) {
    $Why = 'automap-panel: the panel opens (IsOpen<MiniMapPanel>()==true, explored snapshot non-empty) but the automap centre/invisibility had to be judged in a real Play session (presentation class), while the numeric half (mapPlayer==livePlayer / anchored / DrawnCells / OpaquePixels) is dumped from the same session so the two halves cannot drift apart.'
}

# ---- project root: resolved from THIS script's location, so the same file works
#      from .ai-tmp/drivers/ and from tools/probes/drivers/ --------------------
$scriptDir = $PSScriptRoot
if ([string]::IsNullOrEmpty($scriptDir)) { $scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path }
$root = $scriptDir
for ($i = 0; $i -lt 8 -and -not [string]::IsNullOrEmpty($root); $i++) {
    if (Test-Path -LiteralPath (Join-Path $root 'client/Assets/Scripts')) { break }
    $root = Split-Path -Parent $root
}

$proj    = $root + '/client'
$cs      = $root + '/.ai-tmp/drivers/automappanel_probe.cs'
if (-not [string]::IsNullOrEmpty($Probe)) { $cs = $Probe }
$test    = $root + '/.ai-tmp/test'
$shots   = $root + '/.ai-tmp/screenshots'
$heart   = $test + '/heartbeat-automappanel.txt'
$done    = $test + '/amp_done_' + $Tag + '.txt'
$trace   = $test + '/amp_steps_' + $Tag + '.txt'
$frozen  = $test + '/amp_readings_' + $Tag + '.txt'
$outPath = $test + '/amp_out_' + $Tag + '.txt'
$logPath = $proj + '/Logs/Editor.log'
$playLog = $test + '/play-log.tsv'
$lock    = $test + '/play.lock'

$charName = 'SPProgsp2'
$keepRe = '\[AMP\]'

$script:offset = 0
$script:lines = New-Object System.Collections.ArrayList
$script:cli = 0
$script:out = New-Object System.Collections.ArrayList
$script:mine = $false          # do WE own play.lock right now?
$script:touches = 0            # how many times we renewed it (reported at the end)

function Say([string]$s) {
    $stamp = (Get-Date).ToString('HH:mm:ss.fff')
    Write-Host ($stamp + ' ' + $s)
    Add-Content -Path $trace -Value ($stamp + ' ' + $s) -Encoding UTF8
    [void]$script:out.Add($stamp + ' ' + $s)
    try { [System.IO.File]::WriteAllText($heart, ($stamp + ' [' + $lockTag + '] ' + $s)) } catch { }
}

function Init-Offset() {
    if (Test-Path -LiteralPath $logPath) { $script:offset = (Get-Item -LiteralPath $logPath).Length } else { $script:offset = 0 }
}

function Read-New() {
    if (-not (Test-Path -LiteralPath $logPath)) { return }
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
    } catch { } finally { if ($fs -ne $null) { $fs.Close(); $fs.Dispose() } }
}

function Lines-With([string]$pattern) { return @($script:lines | Where-Object { $_ -match $pattern }) }

# ---- TOUCH-LOCK (driver v2, see header contract) ----------------------------
function Touch-Lock() {
    if (-not (Test-Path -LiteralPath $lock)) { return $false }          # (2) taken over
    $body = ''
    try { $body = (Get-Content -LiteralPath $lock -Raw) } catch { $body = '' }
    if ($body -notmatch $lockTag) { return $false }                     # (1) foreign lock: never touch
    try {
        (Get-Item -LiteralPath $lock).LastWriteTime = Get-Date
        $script:touches = $script:touches + 1
        return $true
    } catch {
        Say ('TOUCH-LOCK-FAIL ' + $_.Exception.Message)
        return $false
    }
}

function Release-Lock() {
    if (-not (Test-Path -LiteralPath $lock)) { Say 'LOCK-RELEASE skip (no lock file)'; $script:mine = $false; return }
    $body = ''
    try { $body = (Get-Content -LiteralPath $lock -Raw) } catch { $body = '' }
    if ($body -match $lockTag) {
        Remove-Item -LiteralPath $lock -Force -ErrorAction SilentlyContinue
        $script:mine = $false
        Say ('LOCK-RELEASED (mine; touches=' + $script:touches + ')')
    } else {
        Say ('LOCK-RELEASE REFUSED (owned by ' + $body.Trim() + ') -> left alone')
    }
}

function Clip([string]$s, [int]$n) {
    if ($null -eq $s) { return '' }
    $one = $s -replace "`r?`n", ' '
    if ($one.Length -le $n) { return $one }
    return $one.Substring(0, $n)
}

function Unity-Cmd([string[]]$argv, [switch]$Quiet) {
    $script:cli = $script:cli + 1
    # (3) renew right before every CLI call; abort (2) if our lock is gone.
    if ($script:mine) {
        if (-not (Touch-Lock)) {
            Say 'LOCK-LOST -> our play.lock is gone (taken over?) => ABORT and DO NOT touch the editor'
            [System.IO.File]::WriteAllLines($outPath, [string[]]$script:out, (New-Object System.Text.UTF8Encoding($false)))
            exit 2
        }
    }
    $raw2 = & unity command @argv --project-path $proj --format json --no-pager 2>&1 | Out-String
    if (-not $Quiet) { Say ('CLI ' + ($argv -join ' ') + ' -> ' + $raw2.Length + ' bytes') }
    return $raw2
}

function Run-Step([string]$entry, [string]$arg) {
    if ([string]::IsNullOrEmpty($arg)) {
        $r = Unity-Cmd @('run_script', '--file', $cs, '--entry', $entry)
    } else {
        $r = Unity-Cmd @('run_script', '--file', $cs, '--entry', $entry, '--args', ('[\"' + $arg + '\"]'))
    }
    $result = '?'
    try {
        $j = $r | ConvertFrom-Json
        $v = $j.data.result.result
        if ($null -eq $v) { $v = $j.data.result.error }
        if ($null -eq $v) {
            $d = @($j.data.result.diagnostics | Where-Object { $_.severity -eq 'error' })
            if ($d.Count -gt 0) { $v = 'COMPILE-ERRORS:' + $d.Count + ' ' + (Clip (($d | ForEach-Object { $_.id + ' line ' + $_.line + ': ' + $_.message }) -join ' | ') 500) }
        }
        if ([string]::IsNullOrEmpty([string]$v)) {
            try { $e0 = $j.errors[0].message; if ($null -ne $e0) { $v = 'CLI-ERROR: ' + $e0 } } catch { }
        }
        $result = [string]$v
    } catch { $result = 'PARSE-FAIL' }
    if ([string]::IsNullOrEmpty($result)) { $result = '(EMPTY)' }
    Say ('STEP ' + $entry + ' result=' + $result)
    return $result
}

function Wait-Recompile([string]$why) {
    Unity-Cmd @('recompile') -Quiet | Out-Null
    $st2 = '(unknown)'
    for ($i = 0; $i -lt 60; $i++) {
        Start-Sleep -Seconds 2
        $raw2 = Unity-Cmd @('recompile_status') -Quiet
        try { $j = $raw2 | ConvertFrom-Json; $st2 = (($j.data.result | ConvertFrom-Json).status) } catch { }
        if ($st2 -match 'up_to_date|completed|idle') { break }
    }
    Say ('RECOMPILE[' + $why + '] status=' + $st2)
    return $st2
}

# =============================================================================
# main
# =============================================================================
foreach ($d in @($shots, $test)) { if (-not (Test-Path -LiteralPath $d)) { New-Item -ItemType Directory -Path $d | Out-Null } }
Set-Content -Path $trace -Value ('# automap-panel steps tag=' + $Tag) -Encoding UTF8
$sessionStart = Get-Date
Say ('BEGIN tag=' + $Tag + ' root=' + $root)
Say ('ROOT-RESOLVED scriptDir=' + $scriptDir)

# ---- offline self test of the lock protocol (no unity CLI, no Play) ---------
if ($SelfTest) {
    $tmpLock = $test + '/amp_lock_selftest.lock'
    $script:lock = $tmpLock
    Remove-Item -LiteralPath $tmpLock -Force -ErrorAction SilentlyContinue

    $a = Touch-Lock                                                        # missing file
    Set-Content -Path $tmpLock -Value ($lockTag + ' ' + (Get-Date).ToString('o')) -Encoding UTF8
    (Get-Item -LiteralPath $tmpLock).LastWriteTime = (Get-Date).AddMinutes(-10)
    $b = Touch-Lock                                                        # our own, 10 min old
    $age = ((Get-Date) - (Get-Item -LiteralPath $tmpLock).LastWriteTime).TotalSeconds
    Set-Content -Path $tmpLock -Value ('cam-verify ' + (Get-Date).ToString('o')) -Encoding UTF8
    (Get-Item -LiteralPath $tmpLock).LastWriteTime = (Get-Date).AddMinutes(-10)
    $c = Touch-Lock                                                        # foreign lock
    $age2 = ((Get-Date) - (Get-Item -LiteralPath $tmpLock).LastWriteTime).TotalSeconds
    Remove-Item -LiteralPath $tmpLock -Force -ErrorAction SilentlyContinue

    $ok = ((-not $a) -and $b -and ($age -lt 5) -and (-not $c) -and ($age2 -gt 500))
    Say ('SELFTEST missingRefused=' + (-not $a) + ' renewOwn=' + $b + ' ageAfterRenew=' + [math]::Round($age, 1) + 's foreignRefused=' + (-not $c) + ' foreignAgeUnchanged=' + [math]::Round($age2, 0) + 's => ' + $(if ($ok) { 'PASS' } else { 'FAIL' }))
    Remove-Item -LiteralPath $trace -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $heart -Force -ErrorAction SilentlyContinue
    if ($ok) { exit 0 } else { exit 1 }
}

if (-not (Test-Path -LiteralPath $cs)) { Say ('ABORT missing ' + $cs); exit 1 }
if (-not (Test-Path -LiteralPath $proj)) { Say ('ABORT missing project ' + $proj); exit 1 }

# ---- play lock: take only when absent or stale (age >= 360 s AND not playing)
for ($i = 0; $i -lt 15; $i++) {
    if (-not (Test-Path -LiteralPath $lock)) { break }
    $age = ((Get-Date) - (Get-Item -LiteralPath $lock).LastWriteTime).TotalSeconds
    if ($age -ge 360) {
        $st0 = Unity-Cmd @('editor_status') -Quiet
        $pm0 = '?'
        try { $j0 = $st0 | ConvertFrom-Json; $pm0 = $j0.data.result.playMode } catch { }
        if ($pm0 -ne 'playing') { Say ('LOCK-STALE age=' + [math]::Round($age, 0) + 's playMode=' + $pm0 + ' -> taking it'); break }
        Say ('LOCK-STALE but playMode=playing -> wait (try ' + ($i + 1) + ')')
    } else {
        Say ('LOCK-BUSY age=' + [math]::Round($age, 0) + 's -> wait 30s (try ' + ($i + 1) + ')')
    }
    Start-Sleep -Seconds 30
}
Set-Content -Path $lock -Value ($lockTag + ' ' + (Get-Date).ToString('o')) -Encoding UTF8
$script:mine = $true
Say ('LOCK-TAKEN ' + $lock + ' (renew-before-every-CLI is ON)')

for ($i = 0; $i -lt 48; $i++) {
    $st = Unity-Cmd @('editor_status') -Quiet
    try {
        $js = $st | ConvertFrom-Json
        $c = $js.data.result.compiling
        $pm = $js.data.result.playMode
        if ($c -eq $false -and $pm -eq 'stopped') { Say ('EDITOR-STATUS compiling=' + $c + ' playMode=' + $pm); break }
    } catch { }
    if ($i % 6 -eq 0) { Say ('EDITOR-WAIT compiling/playMode not ready yet (' + $i + ')') }
    Start-Sleep -Seconds 5
}
Unity-Cmd @('editor_stop') -Quiet | Out-Null
Start-Sleep -Seconds 2

$rc0 = Wait-Recompile 'preplay'
if ($rc0 -notmatch 'up_to_date|completed|idle') {
    Say 'ABORT recompile never settled -> editor_stop + release MY lock'
    Unity-Cmd @('editor_stop') -Quiet | Out-Null
    Release-Lock
    [System.IO.File]::WriteAllLines($outPath, [string[]]$script:out, (New-Object System.Text.UTF8Encoding($false)))
    exit 1
}
Start-Sleep -Seconds 3

foreach ($f in @($done, ($done + '.side.txt'), ($done + '.readings.txt'))) {
    if (Test-Path -LiteralPath $f) { Remove-Item -LiteralPath $f -Force; Say ('CLEARED ' + (Split-Path -Leaf $f)) }
}

Add-Content -Path $playLog -Value ((Get-Date).ToString('yyyy-MM-dd HH:mm') + "`t" + $lockTag + "`tam-" + $Tag + "`t" + $Why) -Encoding UTF8
Say ('PLAYLOG-APPENDED ' + $playLog)

Unity-Cmd @('clear_console') -Quiet | Out-Null
Init-Offset
Say ('LOG-OFFSET ' + $script:offset)
Unity-Cmd @('editor_play') -Quiet | Out-Null
Start-Sleep -Seconds 8

$cfgOk = $false
for ($i = 0; $i -lt 10; $i++) {
    $r = Run-Step 'AMP.Api.Ping' ''
    if ($r -match 'PONG') { $cfgOk = $true; break }
    Start-Sleep -Seconds 3
}
Say ('GAME-RES-OK ' + $cfgOk)
if (-not $cfgOk) {
    Say 'ABORT Api.Ping never answered -> editor_stop + release MY lock'
    Unity-Cmd @('editor_stop') -Quiet | Out-Null
    Release-Lock
    [System.IO.File]::WriteAllLines($outPath, [string[]]$script:out, (New-Object System.Text.UTF8Encoding($false)))
    exit 1
}

$spec = $charName + '|' + ($done -replace '\\', '/') + '|' + ($shots -replace '\\', '/')
Say ('SPEC ' + $spec)
$r2 = Run-Step 'AMP.Tour.Install' $spec
Say ('INSTALL ' + (Clip $r2 300))

$t0 = Get-Date
while (((Get-Date) - $t0).TotalSeconds -lt 200) {
    Read-New
    if ((Lines-With 'AMP-DONE').Count -gt 0) { Say 'DRIVER-DONE-SEEN'; break }
    if (Test-Path -LiteralPath $done) { Say 'DONE-MARKER-SEEN'; break }
    Start-Sleep -Milliseconds 500
}
Read-New
if (Test-Path -LiteralPath ($done + '.side.txt')) { Say ('SIDE-CHANNEL bytes=' + (Get-Item -LiteralPath ($done + '.side.txt')).Length) } else { Say 'SIDE-CHANNEL missing' }
for ($i = 0; $i -lt 12; $i++) {
    if ($script:lines.Count -gt 0) { break }
    Start-Sleep -Seconds 3
    Read-New
}
Say ('AMP-LINES n=' + $script:lines.Count)

$consoleJson = Unity-Cmd @('console_status') -Quiet
$consoleErrors = -1
try { $cj = $consoleJson | ConvertFrom-Json; $consoleErrors = $cj.data.result.counts.error } catch { }
Say ('CONSOLE-STATUS errors=' + $consoleErrors)

Unity-Cmd @('editor_stop') -Quiet | Out-Null
Say 'STOPPED'
for ($i = 0; $i -lt 12; $i++) { Start-Sleep -Seconds 3; Read-New; if ($script:lines.Count -gt 0) { break } }
Say ('POST-STOP-LINES n=' + $script:lines.Count)
Release-Lock

$hdr = New-Object System.Collections.ArrayList
[void]$hdr.Add('# automap-panel readings tag=' + $Tag)
[void]$hdr.Add('# session: ' + $sessionStart.ToString('yyyy-MM-dd HH:mm:ss') + '..' + (Get-Date).ToString('HH:mm:ss'))
[void]$hdr.Add('# run: powershell -NoProfile -ExecutionPolicy Bypass -File tools/probes/drivers/automappanel_run.ps1 -Tag ' + $Tag)
[void]$hdr.Add('# driver: v2 (lock renewal only; v1 read-outs unaffected)')
[void]$hdr.Add('')
foreach ($l in $script:lines) { [void]$hdr.Add($l) }
[void]$hdr.Add('')
[void]$hdr.Add('# ---- side channel (straight-to-disk; survives the game logger thread being aborted) ----')
if (Test-Path -LiteralPath ($done + '.side.txt')) {
    foreach ($l in (Get-Content -LiteralPath ($done + '.side.txt') -Encoding UTF8)) { [void]$hdr.Add($l) }
} else { [void]$hdr.Add('(no side channel file)') }
[System.IO.File]::WriteAllLines($frozen, [string[]]$hdr, (New-Object System.Text.UTF8Encoding($false)))
Say ('FROZEN ' + $frozen)
[System.IO.File]::WriteAllLines($outPath, [string[]]$script:out, (New-Object System.Text.UTF8Encoding($false)))
Say ('END (lock touches=' + $script:touches + ')')
