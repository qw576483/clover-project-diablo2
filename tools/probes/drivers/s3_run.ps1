# =============================================================================
# s3_run.ps1 -- ONE Play session that freezes the four S3 runtime traces.
#
#   powershell -NoProfile -ExecutionPolicy Bypass -File s3_run.ps1 [-Why "..."]
#
# Chain: editor_focus -> editor_stop -> clear_console -> editor_play -> S3D.Api.Cfg ->
#        install S3D.Tour driver -> the driver walks Boot -> MainMenu -> CharSelect -> Stage,
#        then runs four phases (same-cell sort / hover probes / monster GetHit / monster
#        move trace) and writes ONE row per frame/sample to <out>/s3_evidence.tsv
#        -> wait for the done marker -> dump the [S3] lines out of Editor.log -> editor_stop.
#
# The judging numbers are recomputed OFFLINE from the frozen TSV; nothing in this script
# interprets them.
#
# ASCII only (PS 5.1 parses a BOM-less non-ASCII .ps1 as ANSI).
# =============================================================================
param(
    [string]$Why = 'four runtime-only quantities in ONE chain: same-cell render-node sort order over 24 frames (U26/U36), real mouse->grid hover record near/far (U33), monster GetHit sprite frame numbers (U40), monster per-frame grid/world trace (U40)'
)

$ErrorActionPreference = 'Continue'

$root    = 'c:/Work/Server/f-v2/clover-project-diablo2'
$proj    = 'c:\Work\Server\f-v2\clover-project-diablo2\client'
$cs      = $root + '/tools/probes/drivers/s3_drive.cs'
$outDir  = $root + '/.ai-tmp/screenshots'
$tsv     = $outDir + '/s3_evidence.tsv'
$done    = $root + '/.ai-tmp/test/s3_done.txt'
$trace   = $root + '/.ai-tmp/test/s3-steps.txt'
$logPath = $proj + '/Logs/Editor.log'
$playLog = $root + '/.ai-tmp/test/play-log.tsv'
$lock    = $root + '/.ai-tmp/test/play.lock'

$script:offset = 0
$script:lines = New-Object System.Collections.ArrayList

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
        $keep = @($txt -split "`r?`n" | Where-Object { $_ -match '\[S3\]' })
        foreach ($l in $keep) { [void]$script:lines.Add($l) }
    } catch {
        Say ('WARN log-read ' + $_.Exception.Message)
    } finally {
        if ($fs -ne $null) { $fs.Close(); $fs.Dispose() }
    }
}

function Unity-Cmd([string[]]$argv, [switch]$Quiet) {
    $raw = & unity command @argv --format json --no-pager 2>&1 | Out-String
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
Set-Content -Path $trace -Value '# s3 steps' -Encoding UTF8
Set-Location -LiteralPath $proj
Say ('BEGIN')
Say ('PROJECT ' + (Get-Location).Path)

$probeBytes = [System.IO.File]::ReadAllBytes($cs)
$nonAscii = 0
foreach ($b in $probeBytes) { if ($b -gt 127) { $nonAscii++ } }
Say ('PROBE-ASCII bytes=' + $probeBytes.Length + ' nonAscii=' + $nonAscii)

$status = & unity status --format json --no-pager 2>&1 | Out-String
Say ('UNITY-STATUS ' + ($status -replace "`r?`n", ' '))

foreach ($p in @($tsv, $done)) { if (Test-Path $p) { Remove-Item $p -Force; Say ('CLEARED ' + $p) } }

# ---- PLAY LOCK (multi-teammate serialisation; read BEFORE touching the editor) --------------
# Only ONE place may own the editor at a time. Rules (fixed by team-lead after S2 was killed twice):
#   * wait until play.lock is ABSENT (or older than 6 min = stale) before taking it;
#   * take it BEFORE editor_stop / editor_play, so editor_stop can only kill our own session;
#   * release it immediately after our Play; NEVER delete somebody else's lock.
$staleSec = 360
$taken = $false
for ($i = 0; $i -lt 40; $i++) {
    if (Test-Path $lock) {
        $age = ((Get-Date) - (Get-Item $lock).LastWriteTime).TotalSeconds
        if ($age -lt $staleSec) {
            $who = (Get-Content $lock -Raw).Trim()
            Say ('LOCK-BUSY holder=' + $who + ' age=' + [int]$age + 's -> Start-Sleep 30 and retry')
            Start-Sleep -Seconds 30
            continue
        }
        Say ('LOCK-STALE age=' + [int]$age + 's content=' + (Get-Content $lock -Raw).Trim())
    }
    Set-Content -Path $lock -Value ('S3 ' + (Get-Date).ToString('s')) -Encoding UTF8
    $after = ((Get-Date) - (Get-Item $lock).LastWriteTime).TotalSeconds
    $mine = (Get-Content $lock -Raw).Trim()
    if ($mine.StartsWith('S3') -and $after -lt 30) { $taken = $true; Say ('LOCK-TAKEN ' + $mine); break }
    Start-Sleep -Seconds 15
}
if (-not $taken) { Say ('LOCK-NOT-TAKEN -> aborting without touching the editor'); Say ('END'); exit 2 }

Add-Content -Path $playLog -Value ((Get-Date).ToString('yyyy-MM-dd HH:mm') + "`tS3`tU26U33U40`t" + $Why) -Encoding UTF8
Say ('PLAYLOG-APPENDED ' + $playLog)

# from here on we own the lock, so editor_stop can only stop OUR session
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
    $r = Run-Step 'S3D.Api.Cfg' ''
    if ($r -match 'gameRunning=1') { $cfgOk = $true; break }
    Start-Sleep -Seconds 3
}
Say ('CFG-OK ' + $cfgOk)

$spec = ($outDir -replace '\\', '/') + '|' + ($done -replace '\\', '/')
# entering Play restarts the Pipeline server (domain reload) -> the first install call can hit
# "No Unity Editor instances found with reachable Pipeline servers"; retry until INSTALLED.
$installed = $false
for ($i = 0; $i -lt 10; $i++) {
    $raw = Unity-Cmd @('run_script', '--file', $cs, '--entry', 'S3D.Tour.Install', '--args', ('[\"' + $spec + '\"]'))
    if ($raw -match 'INSTALLED') { $installed = $true; Say ('INSTALL-OK try=' + $i); break }
    Say ('INSTALL-RETRY try=' + $i + ' ' + (($raw -replace "`r?`n", ' ') -replace '\s+', ' ').Substring(0, [Math]::Min(160, (($raw -replace "`r?`n", ' ') -replace '\s+', ' ').Length)))
    Start-Sleep -Seconds 5
}
Say ('INSTALLED ' + $installed)

$t0 = Get-Date
while (((Get-Date) - $t0).TotalSeconds -lt 240) {
    Read-New
    if (Test-Path $done) { break }
    Start-Sleep -Seconds 3
}
Read-New
Say ('DONE-PRESENT ' + (Test-Path $done))
if (Test-Path $done) { Say ('DONE ' + (Get-Content $done -Raw)) }

$script:lines | ForEach-Object { Say ('LOG ' + $_) }
if (Test-Path $tsv) { Say ('TSV-LINES ' + (Get-Content $tsv | Measure-Object -Line).Lines) }

Unity-Cmd @('editor_stop') -Quiet | Out-Null
# release ONLY our own lock (content must start with "S3"); never anybody else's.
if (Test-Path $lock) {
    $mine = (Get-Content $lock -Raw).Trim()
    if ($mine.StartsWith('S3')) { Remove-Item $lock -Force; Say ('LOCK-RELEASED ' + $mine) }
    else { Say ('LOCK-NOT-OURS-KEPT ' + $mine) }
}
Say ('END')
