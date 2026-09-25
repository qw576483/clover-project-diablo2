# =============================================================================
# d2p1_run.ps1 -- ONE Play session for the four "needs a window" evidence groups
#   E60 old-save migration / W11 plus-arrow grey state / W12 town waypoint panel
#   (delayed screenshot) / W2+W3 hover dispatch (npc plate vs monster bar, and
#   3 post-kill samples).
#
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools/probes/drivers/d2p1_run.ps1 -Tag p1a
#
#   Play-lock protocol (tools/probes/README.md 5 / 5.1):
#     * lock body = "<owner> <ISO8601> <PID>" written with -Encoding ASCII
#     * real lock .ai-tmp/test/play-running.lock + mirror .ai-tmp/test/play.lock
#     * take only when absent, or when stale (mtime >= 12 min) AND playMode != playing
#     * write the play-log line only AFTER the lock was taken
#     * a compile sentinel must print CSC-EXIT=0 BEFORE editor_play is issued
#     * finish = editor_stop -> bounded poll for playMode=stopped -> release MY locks
#   ASCII only (self-checked below); no obj/ or bin/ is produced here.
# =============================================================================
param(
    [string]$Tag = 'p1a',
    [string]$CharName = 'S2203805',
    [switch]$SelfCheckOnly
)
$ErrorActionPreference = 'Continue'

$root   = Split-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) -Parent
$proj   = $root + '/client'
$cs     = $PSScriptRoot + '/d2p1_evidence.cs'
$test   = $root + '/.ai-tmp/test'
$shots  = $root + '/.ai-tmp/screenshots'
$done   = $test + '/d2p1_done_' + $Tag + '.txt'
$trace  = $test + '/d2p1_steps_' + $Tag + '.txt'
$frozen = $test + '/d2p1_readings_' + $Tag + '.txt'
$outPath= $test + '/d2p1_out_' + $Tag + '.txt'
$logPath= $proj + '/Logs/Editor.log'
$playLog= $test + '/play-log.tsv'
$lockReal = $test + '/play-running.lock'
$lockMirror = $test + '/play.lock'
$saveJson = $proj + '/setting/saves/' + $CharName + '.json'
$backup   = $test + '/d2p1-backup'
$owner  = 'd2p1'

$script:offset = 0
$script:lines = New-Object System.Collections.ArrayList
$script:cli = 0
$script:out = New-Object System.Collections.ArrayList
$script:mine = $false
# lines we freeze: our own probe tag + the production loggers whose own lines are evidence
$keepRe = '\[P1\]|\[EnemyBarView\]|\[Load\]|\[Save\]|\[App\]|\[Ui\]|\[Map\]|\[Player\]'

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
            if ($pmX -eq 'UNKNOWN') { Say ('LOCK-GATE-UNKNOWN playMode unreadable -> yield'); return $false }
            Say ('LOCK-BUSY age=' + [math]::Round($age,0) + 's playMode=' + $pmX + ' -> yield'); return $false
        }
        $pm2 = Get-PlayMode
        if ($pm2 -eq 'playing') { Say ('LOCK-STALE age=' + [math]::Round($age,0) + 's but playMode=playing -> yield'); return $false }
        if ($pm2 -eq 'UNKNOWN') { Say ('LOCK-GATE-UNKNOWN playMode unreadable -> yield'); return $false }
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

foreach ($d in @($shots, $test, $backup)) { if (-not (Test-Path $d)) { New-Item -ItemType Directory -Path $d | Out-Null } }
Set-Content -Path $trace -Value ('# d2p1 steps tag=' + $Tag) -Encoding UTF8
$sessionStart = Get-Date
Say ('BEGIN tag=' + $Tag + ' root=' + $root)

# ---- play lock --------------------------------------------------------------
if (-not (Take-Lock)) { Say ('DONE reason=lock-not-available'); [System.IO.File]::WriteAllLines($outPath, [string[]]$script:out, (New-Object System.Text.UTF8Encoding($false))); exit 2 }
$script:mine = $true

try {
    # ---- backup the on-disk old save (migration rewrites it in place) --------
    if (Test-Path $saveJson) {
        Copy-Item -Path $saveJson -Destination ($backup + '/' + $CharName + '.json') -Force
        $jm = [System.IO.File]::ReadAllText($saveJson)
        $jb = (Get-Item $saveJson).Length
        $jv = [regex]::Match($jm, '"version":(\d+)').Groups[1].Value
        Say ('SAVE-BACKUP ' + $backup + '/' + $CharName + '.json bytes=' + $jb + ' version=' + $jv)
    } else { Say ('ABORT save file missing: ' + $saveJson); Say 'DONE reason=no-old-save'; Release-Lock; exit 1 }

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

    # ---- compile sentinel: the driver must compile AND run before we play ---
    $sentOk = $false
    for ($i = 0; $i -lt 6; $i++) {
        $r = Unity-Cmd @('run_script', '--file', $cs, '--entry', 'P1.Api.Ping')
        if ($r -match 'PONG frame=') { $sentOk = $true; Say ('CSC-EXIT=0 SENTINEL ' + (Clip $r 200)); break }
        if ($r -match 'diagnostics') { Say ('SENTINEL-DIAG ' + (Clip $r 600)) }
        else { Say ('SENTINEL-RETRY ' + (Clip $r 200)) }
        Start-Sleep -Seconds 4
    }
    if (-not $sentOk) { Say 'ABORT compile sentinel never reached PONG -> no window burned'; Say 'DONE reason=sentinel-failed'; Release-Lock; exit 4 }

    foreach ($f in @($done)) { if (Test-Path $f) { Remove-Item $f -Force; Say ('CLEARED ' + (Split-Path $f -Leaf)) } }

    Add-Content -Path $playLog -Value ((Get-Date).ToString('yyyy-MM-dd HH:mm') + "`td2p1`t" + $Tag + "`tE60 old-save 3 resources + W11 plus arrows + W12 delayed waypoint panel + W2/W3 hover dispatch are all runtime-only state (real load of the on-disk old save, uGUI node state, asynchronous panel art, event dispatch into EnemyBarView) => offline hosts cannot produce them") -Encoding UTF8
    Say ('PLAYLOG-APPENDED ' + $playLog + ' tag=' + $Tag)

    Unity-Cmd @('clear_console') -Quiet | Out-Null
    Init-Offset
    Say ('LOG-OFFSET ' + $script:offset)
    Unity-Cmd @('editor_play') -Quiet | Out-Null
    Start-Sleep -Seconds 8

    $spec = $CharName + '|' + ($done -replace '\\', '/') + '|' + ($shots -replace '\\', '/') + '|full'
    Say ('SPEC ' + $spec)
    $inst = Unity-Cmd @('run_script', '--file', $cs, '--entry', 'P1.Tour.Install', '--args', ('[\"' + $spec + '\"]'))
    Say ('INSTALL ' + (Clip $inst 300))

    $t0 = Get-Date
    while (((Get-Date) - $t0).TotalSeconds -lt 420) {
        Read-New
        if ((Lines-With 'P1-DONE').Count -gt 0) { Say 'DRIVER-DONE-SEEN'; break }
        if (Test-Path $done) { Say 'DONE-MARKER-SEEN'; break }
        Start-Sleep -Milliseconds 500
    }
    Read-New
    for ($i = 0; $i -lt 12; $i++) {
        if ($script:lines.Count -gt 0) { break }
        Start-Sleep -Seconds 3
        Read-New
    }
    Say ('P1-LINES n=' + $script:lines.Count)
    Say ('READINGS-PL1 n=' + (Lines-With '\[P1\]').Count)

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
    [void]$hdr.Add('# d2p1 readings tag=' + $Tag)
    [void]$hdr.Add('# session: ' + $sessionStart.ToString('yyyy-MM-dd HH:mm:ss') + '..' + (Get-Date).ToString('HH:mm:ss'))
    [void]$hdr.Add('# run: powershell -NoProfile -ExecutionPolicy Bypass -File tools/probes/drivers/d2p1_run.ps1 -Tag ' + $Tag)
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
