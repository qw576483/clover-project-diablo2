# =============================================================================
# u1u2_run.ps1 -- ONE Play session that re-collects the runtime evidence two
#   acceptance rows cite (their observed sources were edited this round):
#     U-1: direction keys do not drive the character / W still swaps the weapon
#          group / a real ground click really walks (+ 6 walk frames a09_walk_1..6)
#          -> u1_evidence_recapture2.txt;
#     U-2: two production attacks: the player's own attack frames, the monster's hit
#          chain, the damage float text and the hp -> x_x3_atk*.png / x_e1_hit_*.png
#          + x_evidence_run1.txt (the per-shot [X] GRID lines).
#   Plus two panel shots for human confirmation (shop tabs / character panel labels).
#
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools/probes/drivers/u1u2_run.ps1 -Tag u12a
#
#   Play-lock protocol (tools/probes/README.md 5 / 5.1):
#     * lock body = "<owner> <ISO8601> <PID>" written with -Encoding ASCII
#     * real lock .ai-tmp/test/play-running.lock + mirror .ai-tmp/test/play.lock
#     * take only when absent, or when stale (mtime >= 12 min) AND playMode != playing
#     * write the play-log line only AFTER the lock was taken
#     * a compile sentinel must print PONG before editor_play is issued
#     * finish = editor_stop -> bounded poll for playMode=stopped -> release MY locks
#   ASCII only (self-checked below); no obj/ or bin/ is produced here.
# =============================================================================
param(
    [string]$Tag = 'u12a',
    [string]$CharName = 'S2203805',
    [switch]$ShopOnly,
    [switch]$SelfCheckOnly
)
$ErrorActionPreference = 'Continue'

$root   = Split-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) -Parent
$proj   = $root + '/client'
$cs     = $PSScriptRoot + '/u1u2_evidence.cs'
$test   = $root + '/.ai-tmp/test'
$shots  = $root + '/.ai-tmp/screenshots'
$done   = $test + '/u1u2_done_' + $Tag + '.txt'
$trace  = $test + '/u1u2_steps_' + $Tag + '.txt'
$frozen = $test + '/u1u2_readings_' + $Tag + '.txt'
$outPath= $test + '/u1u2_out_' + $Tag + '.txt'
$logPath= $proj + '/Logs/Editor.log'
$playLog= $test + '/play-log.tsv'
$lockReal = $test + '/play-running.lock'
$lockMirror = $test + '/play.lock'
$owner  = 'u1u2'

$script:offset = 0
$script:lines = New-Object System.Collections.ArrayList
$script:cli = 0
$script:out = New-Object System.Collections.ArrayList
$script:mine = $false
$keepRe = '\[U12\]|\[Monster\]|\[Player\]|\[View\]|\[Combat\]|\[App\]|\[Input\]|\[Ui\]|\[Skill\]'

function Say([string]$s) {
    $stamp = (Get-Date).ToString('HH:mm:ss.fff')
    Write-Host ($stamp + ' ' + $s)
    Add-Content -Path $trace -Value ($stamp + ' ' + $s) -Encoding UTF8
    [void]$script:out.Add($stamp + ' ' + $s)
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
    } catch { } finally { if ($fs -ne $null) { $fs.Close(); $fs.Dispose() } }
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
    $raw2 = & unity command @argv --project-path $proj --format json --no-pager 2>&1 | Out-String
    if (-not $Quiet) { Say ('CLI ' + ($argv -join ' ') + ' -> ' + $raw2.Length + ' bytes') }
    return $raw2
}

function Get-Recompile() {
    $st = Unity-Cmd @('recompile_status') -Quiet
    if ($st -notmatch '"status"') { return 'UNKNOWN' }
    try {
        $j = $st | ConvertFrom-Json
        $v = [string]$j.data.result.status
        if ([string]::IsNullOrEmpty($v)) { return 'UNKNOWN' }
        return $v
    } catch { return 'UNKNOWN' }
}

function Get-PlayMode() {
    $st = Unity-Cmd @('editor_status') -Quiet
    if ($st -notmatch '"playMode"') { return 'UNKNOWN' }
    try {
        $j = $st | ConvertFrom-Json
        $pm = [string]$j.data.result.playMode
        if ([string]::IsNullOrEmpty($pm)) { return 'UNKNOWN' }
        return $pm
    } catch { return 'UNKNOWN' }
}

function Read-LockBody([string]$f) {
    if (-not (Test-Path $f)) { return $null }
    try {
        $bytes = [System.IO.File]::ReadAllBytes($f)
        $txt = [System.Text.Encoding]::ASCII.GetString($bytes)
        $txt = $txt.Trim([char]0xFEFF, [char]0x200B, [char]0x200C, [char]0x200D, ' ', "`t", "`r", "`n")
        if ([string]::IsNullOrWhiteSpace($txt)) { return $null }
        return $txt
    } catch { return $null }
}

function Write-LockFile([string]$f, [string]$body) {
    [System.IO.File]::WriteAllText($f, $body, (New-Object System.Text.ASCIIEncoding))
}

function Lock-OwnedBy-Mine([string]$f) {
    $b = Read-LockBody $f
    if ($null -eq $b) { return $false }
    $parts = $b -split '\s+'
    return ($parts.Count -ge 1 -and $parts[0] -eq $owner)
}

function Take-Lock() {
    $now = Get-Date
    if ((Test-Path $lockReal) -or (Test-Path $lockMirror)) {
        $f = $null
        if (Test-Path $lockReal) { $f = $lockReal } else { $f = $lockMirror }
        $age = ($now - (Get-Item $f).LastWriteTime).TotalSeconds
        if ($age -lt 720) {
            $pmX = Get-PlayMode
            if ($pmX -eq 'playing') { Say ('LOCK-BUSY age=' + [math]::Round($age,0) + 's playMode=playing -> ABORT (never touch a running session)'); return $false }
            if ($pmX -eq 'UNKNOWN') { Say 'LOCK-GATE-UNKNOWN playMode unreadable -> yield'; return $false }
            Say ('LOCK-BUSY age=' + [math]::Round($age,0) + 's playMode=' + $pmX + ' -> yield'); return $false
        }
        $pm2 = Get-PlayMode
        if ($pm2 -eq 'playing') { Say ('LOCK-STALE age=' + [math]::Round($age,0) + 's but playMode=playing -> yield'); return $false }
        if ($pm2 -eq 'UNKNOWN') { Say 'LOCK-GATE-UNKNOWN playMode unreadable -> yield'; return $false }
        Say ('LOCK-TAKING stale age=' + [math]::Round($age,0) + 's playMode=' + $pm2 + ' owner=' + $owner + ' pid=' + $PID)
    }
    $body = $owner + ' ' + (Get-Date).ToString('o') + ' ' + $PID
    Write-LockFile $lockReal $body
    Start-Sleep -Milliseconds 900
    if (-not (Lock-OwnedBy-Mine $lockReal)) {
        Say 'LOCK-RACE-LOST (readback is not mine) -> yield'
        return $false
    }
    Copy-Item -Path $lockReal -Destination $lockMirror -Force
    Say ('LOCK-TAKEN ' + $lockReal + ' mirror=' + (Test-Path $lockMirror) + ' body=' + $body)
    Say ('LEGACY-MIRROR-WRITTEN play.lock owner=' + $owner + ' pid=' + $PID + ' present=yes')
    return $true
}

function Release-Lock() {
    foreach ($f in @($lockReal, $lockMirror)) {
        if (-not (Test-Path $f)) { continue }
        $b = Read-LockBody $f
        if ($null -ne $b) {
            $parts = $b -split '\s+'
            if ($parts.Count -ge 1 -and $parts[0] -eq $owner) {
                Remove-Item $f -Force -ErrorAction SilentlyContinue
                Say ('LOCK-RELEASED (mine) ' + (Split-Path $f -Leaf))
            } else {
                Say ('LOCK-RELEASE REFUSED (owned by ' + (Clip $b 80) + ') -> ' + (Split-Path $f -Leaf) + ' left alone')
                if ($f -eq $lockMirror) { Say ('LEGACY-MIRROR-RELEASE REFUSED (owned by ' + (Clip $b 80) + ') left alone') }
            }
        } else {
            Say ('LOCK-RELEASE REFUSED (unreadable body) -> ' + (Split-Path $f -Leaf) + ' left alone')
        }
    }
}

function Wait-PlayMode([string]$want, [int]$n) {
    for ($i = 0; $i -lt $n; $i++) {
        Start-Sleep -Seconds 1
        $pm = Get-PlayMode
        if ($pm -eq $want) { Say ('RELEASE-GATE playMode=' + $pm + ' try=' + ($i + 1)); return $true }
    }
    Say ('RELEASE-GATE playMode=' + $want + ' not observed within ' + $n + 's')
    return $false
}

# ---- static self-check (runs before anything touches the editor) -------------
$self = [System.IO.File]::ReadAllBytes($PSCommandPath)
$na = 0
foreach ($b in $self) { if ($b -gt 127) { $na++ } }
$bom = ($self.Length -ge 3 -and $self[0] -eq 0xEF -and $self[1] -eq 0xBB -and $self[2] -eq 0xBF)
$encOk = ($na -eq 0) -or $bom
Say ('ENCODING-SELFCHECK runner nonAscii=' + $na + ' bom=' + $bom + ' verdict=' + $(if ($encOk) { 'OK' } else { 'FAIL' }))
if (-not $encOk) { Say 'SELFCHECK-FAIL encoding (fix: pure ASCII or a UTF-8 BOM)'; exit 3 }

$parseErrs = $null
try {
    $parseErrs = $null
    [void][System.Management.Automation.Language.Parser]::ParseFile($PSCommandPath, [ref]$null, [ref]$parseErrs)
} catch { }
$pe = 0
if ($null -ne $parseErrs) { $pe = $parseErrs.Count }
Say ('PARSE-SELFCHECK errors=' + $pe)
if ($pe -ne 0) { Say 'SELFCHECK-FAIL parse'; exit 3 }

if (-not (Test-Path $cs)) { Say ('ABORT missing ' + $cs); exit 1 }
$cb = [System.IO.File]::ReadAllBytes($cs)
$cna = 0
foreach ($b in $cb) { if ($b -gt 127) { $cna++ } }
Say ('CS ' + (Split-Path $cs -Leaf) + ' bytes=' + $cb.Length + ' nonAscii=' + $cna)

$pmNow = Get-PlayMode
Say ('PRECHECK playMode=' + $pmNow + ' locks real=' + (Test-Path $lockReal) + ' mirror=' + (Test-Path $lockMirror))

if ($SelfCheckOnly) {
    Say 'SELFCHECK-ONLY-END'
    [System.IO.File]::WriteAllLines($outPath, [string[]]$script:out, (New-Object System.Text.UTF8Encoding($false)))
    exit 0
}

foreach ($d in @($shots, $test)) { if (-not (Test-Path $d)) { New-Item -ItemType Directory -Path $d | Out-Null } }
Set-Content -Path $trace -Value ('# u1u2 steps tag=' + $Tag) -Encoding UTF8
$sessionStart = Get-Date
Say ('BEGIN tag=' + $Tag + ' root=' + $root)

# ---- play lock --------------------------------------------------------------
if ((Take-Lock) -eq $true) { $script:mine = $true } else { Say 'DONE reason=lock-not-available'; [System.IO.File]::WriteAllLines($outPath, [string[]]$script:out, (New-Object System.Text.UTF8Encoding($false))); exit 2 }

try {
    # ---- settle the editor --------------------------------------------------
    $stm = 'UNKNOWN'
    for ($i = 0; $i -lt 48; $i++) {
        $stm = Get-PlayMode
        if ($stm -eq 'stopped') { break }
        if ($stm -eq 'playing') { Say 'ABORT editor is playing'; Say 'DONE reason=editor-playing'; Release-Lock; exit 2 }
        Say ('EDITOR-WAIT playMode=' + $stm + ' try=' + ($i + 1))
        Start-Sleep -Seconds 5
    }
    Say ('EDITOR-STATUS playMode=' + $stm)
    if ($stm -eq 'playing') { Say 'DONE reason=editor-playing'; Release-Lock; exit 2 }
    Unity-Cmd @('editor_stop') -Quiet | Out-Null
    Start-Sleep -Seconds 2

    # ---- force the editor to pick up the sources on disk --------------------
    #   `run_script` compiles only the DRIVER; the game assemblies come from the editor's own
    #   script compile.  Without this step Play can run the PREVIOUS revision (measured 2026-09-26:
    #   the monster attack interval still read the old constant while the new one was on disk).
    Unity-Cmd @('recompile', '--focus') -Quiet | Out-Null
    $rc = 'unknown'
    $rcTries = 0
    for ($i = 0; $i -lt 40; $i++) {
        Start-Sleep -Seconds 2
        $rcTries = $i + 1
        $rc = Get-Recompile
        if ($rc -match 'completed|up_to_date') { break }
    }
    Say ('RECOMPILE status=' + $rc + ' tries=' + $rcTries)

    # ---- compile sentinel: the driver must compile AND run before we play ---
    $sentOk = $false
    for ($i = 0; $i -lt 6; $i++) {
        $r = Unity-Cmd @('run_script', '--file', $cs, '--entry', 'U1U2.Probe.Ping')
        if ($r -match 'PONG frame=') { $sentOk = $true; Say ('CSC-EXIT=0 SENTINEL ' + (Clip $r 200)); break }
        if ($r -match 'diagnostics') { Say ('SENTINEL-DIAG ' + (Clip $r 1200)) }
        else { Say ('SENTINEL-RETRY ' + (Clip $r 200)) }
        Start-Sleep -Seconds 4
    }
    if (-not $sentOk) { Say 'ABORT compile sentinel never reached PONG -> no window burned'; Say 'DONE reason=sentinel-failed'; Release-Lock; exit 4 }

    foreach ($f in @($done)) { if (Test-Path $f) { Remove-Item $f -Force; Say ('CLEARED ' + (Split-Path $f -Leaf)) } }

    Add-Content -Path $playLog -Value ((Get-Date).ToString('yyyy-MM-dd HH:mm') + "`tu1u2`t" + $Tag + "`tU-1/U-2 row evidence recapture: whether the held direction keys move the character, whether W still swaps the weapon group, whether a real ground click walks, and which attack / hit / float-text frame is on screen during two production attacks are live readings: only a running session runs the real InputReader + PlayerMotor + SpriteAnimator + async load and shows the frame that reaches the screen") -Encoding UTF8
    Say ('PLAYLOG-APPENDED ' + $playLog + ' tag=' + $Tag)

    Unity-Cmd @('clear_console') -Quiet | Out-Null
    Init-Offset
    Say ('LOG-OFFSET ' + $script:offset)
    Unity-Cmd @('editor_play') -Quiet | Out-Null
    Start-Sleep -Seconds 8

    $mode = if ($ShopOnly) { 'shoponly' } else { '' }
    $spec = $CharName + '|' + ($done -replace '\\', '/') + '|' + ($shots -replace '\\', '/') + '|' + ($test -replace '\\', '/') + '|' + $mode
    Say ('SPEC ' + $spec)
    $inst = Unity-Cmd @('run_script', '--file', $cs, '--entry', 'U1U2.U1U2Run.Install', '--args', ('[\"' + $spec + '\"]'))
    Say ('INSTALL ' + (Clip $inst 300))

    $t0 = Get-Date
    while (((Get-Date) - $t0).TotalSeconds -lt 660) {
        Read-New
        if ((Lines-With 'TOUR-DONE').Count -gt 0) { Say 'DRIVER-DONE-SEEN'; break }
        if (Test-Path $done) { Say 'DONE-MARKER-SEEN'; break }
        Start-Sleep -Milliseconds 500
    }
    Read-New
    for ($i = 0; $i -lt 12; $i++) {
        if ($script:lines.Count -gt 0) { break }
        Start-Sleep -Seconds 3
        Read-New
    }
    Say ('TOUR-LINES n=' + $script:lines.Count)
    Say ('READINGS-SUMMARY n=' + (Lines-With 'MF-LEGA-RUN-SUMMARY|MF-LEGB-WALK-SUMMARY').Count)
    Say ('READINGS-FRAMES n=' + (Lines-With 'MF-FRAME').Count)
    Say ('READINGS-DEATH frames=' + (Lines-With 'MF-DEATH-FRAME').Count + ' summary=' + (Lines-With 'DEATH-SUMMARY').Count + ' died=' + (Lines-With '\[Player\].*died ').Count + ' revived=' + (Lines-With 'DEATH-REVIVED-IDLE').Count)

    $consoleJson = Unity-Cmd @('console_status') -Quiet
    $consoleErrors = -1
    try { $cj = $consoleJson | ConvertFrom-Json; $consoleErrors = $cj.data.result.counts.error } catch { }
    Say ('CONSOLE-STATUS errors=' + $consoleErrors)

    # ---- stop -> confirm stopped -> release (order is mandatory) ------------
    Unity-Cmd @('editor_stop') -Quiet | Out-Null
    Say 'STOPPED'
    $ok = Wait-PlayMode 'stopped' 15
    for ($i = 0; $i -lt 12; $i++) { Start-Sleep -Seconds 3; Read-New; if ($script:lines.Count -gt 0) { break } }
    Say ('POST-STOP-LINES n=' + $script:lines.Count)
    Release-Lock
    $script:mine = $false

    $hdr = New-Object System.Collections.ArrayList
    [void]$hdr.Add('# u1u2 readings tag=' + $Tag)
    [void]$hdr.Add('# session: ' + $sessionStart.ToString('yyyy-MM-dd HH:mm:ss') + '..' + (Get-Date).ToString('HH:mm:ss'))
    [void]$hdr.Add('# run: powershell -NoProfile -ExecutionPolicy Bypass -File tools/probes/drivers/u1u2_run.ps1 -Tag ' + $Tag)
    [void]$hdr.Add('# console errors = ' + $consoleErrors + '   playMode-stopped-confirmed = ' + $ok)
    [void]$hdr.Add('# ---------------------------------------------------------------------------')
    foreach ($l in $script:lines) { [void]$hdr.Add($l) }
    [void]$hdr.Add('# ---------------------------------------------------------------------------')
    [System.IO.File]::WriteAllLines($frozen, [string[]]$hdr, (New-Object System.Text.UTF8Encoding($false)))
    Say ('FROZEN ' + $frozen + ' lines=' + $script:lines.Count)
    Say ('END cli=' + $script:cli)
    [System.IO.File]::WriteAllLines($outPath, [string[]]$script:out, (New-Object System.Text.UTF8Encoding($false)))
}
finally {
    if ($script:mine) { Say 'FINALLY release MY lock'; Release-Lock }
    if (-not (Test-Path $outPath)) { [System.IO.File]::WriteAllLines($outPath, [string[]]$script:out, (New-Object System.Text.UTF8Encoding($false))) }
}
