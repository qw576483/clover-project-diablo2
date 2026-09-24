# =============================================================================
# d2u26_run.ps1 -- ONE Play session that freezes the U26/U36 draw-order trace.
#
#   powershell -NoProfile -ExecutionPolicy Bypass -File d2u26_run.ps1 -Tag u26
#
# Chain: editor_focus -> editor_stop -> clear_console -> editor_play -> D2U26.Api.Cfg ->
#        install D2U26.Tour driver -> the driver reaches Stage(Town), parks the character at
#        three anti-diagonal distances from Akara (2 / 1 / 0 cells) and writes ONE TSV row +
#        ONE crop PNG per frame -> wait for the done marker -> dump the [D2U26] lines out of
#        Editor.log -> editor_stop -> run d2u26_sheet.py.
#
# The verdict ("flicker or not") is recomputed OFFLINE from the frozen TSV by d2u26_sheet.py;
# nothing in this script interprets anything.
#
# ASCII only (PS 5.1 parses a BOM-less non-ASCII .ps1 as ANSI).
# =============================================================================
param(
    [string]$Tag = 'u26',
    [string]$Why = 'U26/U36 need a MULTI-FRAME sequence of the real draw order + real pixels while the character and an NPC share the same gx+gy (same cell / adjacent / 2 cells): the primary sort key is equal there, so the order is decided by the z secondary key alone, and only a live Play session can show whether the pixels alternate',
    # v2.2/v2.3: every runner must ship a self-check that CAN FAIL, in a PER-PID sandbox, with
    # known-good AND known-bad samples.  -SelfTest touches neither the real lock nor the editor.
    [switch]$SelfTest,
    # SelfCheckOnly (team ruling 13:3x): the ONLY entry point for "I just want to read the self-check".
    # Reading it by STARTING A REAL RUN and killing it left a zombie lock + an invalid ledger line
    # (jitter, 13:26) because `finally` never ran.  This mode = the same per-PID sandbox check, plus a
    # read-only editor probe, and it PROVES it never touched the real lock files (item 34).
    [switch]$SelfCheckOnly
)
if ($SelfCheckOnly -eq $true) { $SelfTest = $true; $scOnly = $true } else { $scOnly = $false }

$ErrorActionPreference = 'Continue'

$root    = 'c:/Work/Server/f-v2/clover-project-diablo2'
# NOTE: editor discovery is cwd-relative and --project-path only matches the WINDOWS form of the
# path (measured: '--project-path c:/...' returns STATUS_NO_INSTANCES while the editor is ready).
# So this runner cd's into the project and passes no --project-path at all.
$proj    = 'c:\Work\Server\f-v2\clover-project-diablo2\client'
$cs      = $root + '/tools/probes/drivers/d2u26_sortdrive.cs'
$sheetPy = $root + '/tools/probes/drivers/d2u26_sheet.py'
$outDir  = $root + '/.ai-tmp/test'
$shotDir = $root + '/.ai-tmp/screenshots'
$cropDir = $shotDir + '/u26-' + $Tag
$tsv     = $outDir + '/u26-sort-' + $Tag + '.tsv'
$done    = $outDir + '/u26-sort-done-' + $Tag + '.txt'
$trace   = $outDir + '/u26-sort-steps-' + $Tag + '.txt'
$logCopy = $outDir + '/u26-sort-log-' + $Tag + '.txt'
$sheet   = $shotDir + '/u26-contact-' + $Tag + '.png'
$index   = $shotDir + '/u26-contact-' + $Tag + '.index.tsv'
$logPath = $proj + '/Logs/Editor.log'
$playLog = $outDir + '/play-log.tsv'
# Rule 2 of the orphan-Play policy (team, 13:5x): an orphaned session must be reported to the MAIN AGENT,
# not only written into this runner's own trace (the lead does not tail other people's traces).  One
# shared append-only alert file that anybody can poll.
$alertFile = $outDir + '/orphan-play-alerts.tsv'

# Evidence hygiene (MEASURED, 13:19-13:25): my own -SelfTest runs OVERWROTE u26-sort-steps-u26.txt,
# because the trace header is written with WriteAllText and `Say` appends to the same name.  A sandbox
# run must never touch an evidence file => in -SelfTest mode the trace/log copy go to *-selftest.txt.
if ($SelfTest) {
    $trace   = $outDir + '/u26-sort-steps-' + $Tag + '-selftest.txt'
    $logCopy = $outDir + '/u26-sort-log-' + $Tag + '-selftest.txt'
}

$script:offset = 0
$script:lines = New-Object System.Collections.ArrayList
$script:cli = 0

# Logs/ledger are UTF-8 WITHOUT a BOM (team rule): `-Encoding ASCII` would silently turn Chinese
# into '?', and creating a file with `-Encoding UTF8` in PS 5.1 writes a BOM that adds a phantom
# first column to a .tsv.  AppendAllText + UTF8Encoding($false) never writes a BOM.
function Say([string]$s) {
    $stamp = (Get-Date).ToString('HH:mm:ss.fff')
    $line = $stamp + ' ' + $s
    Write-Host $line
    [System.IO.File]::AppendAllText($trace, $line + "`r`n", (New-Object System.Text.UTF8Encoding($false)))
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
        $keep = @($txt -split "`r?`n" | Where-Object { $_ -match '\[D2U26\]' })
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
[System.IO.File]::WriteAllText($trace, ('# d2u26 steps tag=' + $Tag + "`r`n"), (New-Object System.Text.UTF8Encoding($false)))
Set-Location -LiteralPath $proj
Say ('BEGIN tag=' + $Tag)
# Product self-fingerprint (team 13:2x, adopted from jitter; README 5.2 #24).  A hand-pasted hash goes
# stale within minutes -- measured: a pasted fingerprint mismatched after 2.5 min and 8 quoted line
# numbers no longer resolved (L155/L471 were bare `}`), which turned into a FALSE RED for a reviewer.
# So every artefact points at THIS line of THIS trace instead of a pasted value.
$selfPath = $PSCommandPath
$selfSha = (Get-FileHash $selfPath -Algorithm SHA256).Hash
$selfMtime0 = (Get-Item $selfPath).LastWriteTime
$selfBytes = (Get-Item $selfPath).Length
$selfLines = ([System.IO.File]::ReadAllLines($selfPath)).Count
Say ('RUNNER-FINGERPRINT sha256_16=' + $selfSha.Substring(0, 16) + ' bytes=' + $selfBytes + ' lines=' + $selfLines + ' mtime=' + (Get-Item $selfPath).LastWriteTime.ToString('yyyy-MM-ddTHH:mm:ss') + ' (authoritative for THIS trace; verified by re-computing)')
Say ('PROJECT ' + (Get-Location).Path)

$probeBytes = [System.IO.File]::ReadAllBytes($cs)
$nonAscii = 0
foreach ($b in $probeBytes) { if ($b -gt 127) { $nonAscii++ } }
Say ('PROBE-ASCII bytes=' + $probeBytes.Length + ' nonAscii=' + $nonAscii)

# =============================================================================
# READINESS GATE -- FAIL-CLOSED (team ruling, 13:2x; the hole was found by `classcols`).
# Accurate root cause of the status oddity (team, 12:5x, A/B/C measured): `--project-path` is
# SILENTLY DROPPED by the `status` subcommand, so a status call WITH that flag returns an empty
# table -- the bare call from <root>/client is fine.  This runner never passes --project-path to
# `status`, and the gate itself is `editor_status` (it carries playMode; `console` does not).
#
# EXACTLY ONE of three outcomes may start a Play session:
#   A) reachable + playMode READABLE -> judge on it: playing => ABORT; else gate on status/compiling
#   B) reachable but playMode MISSING/empty -> UNKNOWN == "not stopped" => YIELD (exit 4).
#      Proof a CLI can fail silently (both measured today): `status --project-path` returns an EMPTY
#      table; `run_script` answers success:true for a driver that does not even compile.  So
#      "unreadable" is NOT "not playing" -- the same polarity we already use on the lock side
#      (a missing PID field is UNKNOWN, not dead => yield).  Do NOT rely on a default value that
#      merely happens not to equal 'playing' -- that is a judgement that holds by coincidence.
#   C) bridge down (`success:false` / `COMMAND_FAILED`, i.e. no editor process) -> NOT "unknown":
#      bounded 3 x 5s retry, and ANY reachable reading inside that window decides (playing => ABORT).
#      Still down => LOCK-GATE-BRIDGE-DOWN + continue on the historical path: with no editor process
#      nobody can be in Play, and `editor_play` is what starts a cold editor.
# The whole gate is skipped in a sandbox: -SelfTest must never touch the CLI or the editor.
# =============================================================================
$readyOk = $false
$edParsed = $false
$bridgeDown = $false
$probed = 0
if (-not $SelfTest) {
    $status = & unity status --format json --no-pager 2>&1 | Out-String
    Say ('UNITY-STATUS (bare, cwd=<root>/client; with --project-path this table is silently empty) ' + ($status -replace "`r?`n", ' '))

    for ($attempt = 1; $attempt -le 3; $attempt++) {
        $probed = $attempt
        $edJson = & unity command editor_status --format json --no-pager 2>&1 | Out-String
        $ed = $null
        try { $ed = ($edJson | ConvertFrom-Json).data.result } catch { $ed = $null }
        if ($null -ne $ed -and (@($ed.PSObject.Properties.Name) -contains 'playMode') -and ("$($ed.playMode)".Trim() -ne '')) {
            $edParsed = $true
            break
        }
        if ($edJson -match '"success"\s*:\s*false' -or $edJson -match 'COMMAND_FAILED') {
            $bridgeDown = $true
            Say ('LOCK-GATE-BRIDGE-DOWN probed=' + $attempt + '/3 (success:false or COMMAND_FAILED) -> bounded retry')
            Start-Sleep -Seconds 5
            continue
        }
        # reachable output, but the playMode field is absent/empty: UNKNOWN is NOT "stopped"
        Say ('LOCK-GATE-UNKNOWN playMode unreadable (bytes=' + $edJson.Length + ') -> yield (unknown != stopped)')
        Say 'END'
        exit 4
    }

    if ($edParsed) {
        $edLine = 'status=' + $ed.status + ' playMode=' + $ed.playMode + ' compiling=' + $ed.compiling + ' domainReload=' + $ed.domainReloadInProgress
        $readyOk = ($ed.status -eq 'ready') -and ($ed.compiling -eq $false) -and ($ed.playMode -eq 'stopped')
        Say ('EDITOR-READY source=editor_status probed=' + $probed + ' ' + $edLine + ' => startable=' + $readyOk)
        # POLITE STOP: a live Play session must never be preempted, even if the lock looks free (a
        # session started by an old-name runner holds no lock we can see).  11:43 incident's lesson.
        if ($ed.playMode -eq 'playing') {
            Say 'ABORT editor-is-playing playMode=playing -- the editor was never touched and the play-log was NOT written'
            Say 'END'
            exit 3
        }
    } else {
        Say ('LOCK-GATE-BRIDGE-DOWN probed=' + $probed + ' -> continue (no editor process; nobody can be in Play)')
        # The console fallback is used ONLY when the primary source was UNREADABLE (bridge down) --
        # never to override a parsed "not startable" verdict (it cannot see playMode).  Measured:
        # with someone in Play it reported startable=True while editor_status correctly said playing.
        $readyJson = & unity command console --tail 1 --format json --no-pager 2>&1 | Out-String
        try {
            $gt = ($readyJson | ConvertFrom-Json).data.result.groundTruth
            $readyOk = ($gt.compiling -eq $false) -and ($gt.compilationFailed -eq $false)
            Say ('EDITOR-READY source=console compiling=' + $gt.compiling + ' compilationFailed=' + $gt.compilationFailed + ' => startable=' + $readyOk)
        } catch { Say ('EDITOR-READY console parse-fail (' + $readyJson.Length + ' bytes) -> not used as a gate') }
    }
} else {
    Say 'EDITOR-GATE-SKIPPED (selftest: a sandbox never touches the CLI or the editor)'
}

if (-not $SelfTest) { foreach ($p in @($tsv, $done)) { if (Test-Path $p) { Remove-Item $p -Force; Say ('CLEARED ' + $p) } } }

# =============================================================================
# PLAY LOCK -- team protocol v2.3 (single source of truth, no local variants)
#   * two names: play-running.lock (real) + play.lock (legacy MIRROR, written so that the
#     historical runners that only read the old name cannot see "an empty lock" and start a
#     second Play session)
#   * content = "<owner> <ISO8601> <PID>", written with -Encoding ASCII (a BOM once made an
#     owner unable to recognise its own lock -> zombie lock)
#   * stale = age >= 12 min  OR  (a PID field exists AND that process is dead);
#     MISSING PID field = UNKNOWN (NOT dead) => while fresh it MUST be respected (v2.1 hole)
#   * write -> read back after ~1 s -> not mine => yield (never delete the winner's file)
#   * release = both names, only the one whose content is mine
#   * nothing touches the editor before the lock is ours (ABORT fail-fast below)
# =============================================================================
$me       = 'u26-zorder'
$testDir  = $outDir
$lock     = Join-Path $testDir 'play-running.lock'
$legacy   = Join-Path $testDir 'play.lock'
$staleMin = 12                     # the ONE team threshold (12 minutes, no local variants)

function Read-LockOwner([string]$file) {
    # -> @{ owner; iso; pid; raw }; BOM / zero-width stripped, whitespace normalised
    $res = @{ owner = ''; iso = ''; pid = -1; raw = '' }
    if (-not (Test-Path $file)) { return $res }
    try { $res.raw = [System.IO.File]::ReadAllText($file) } catch { return $res }
    $t = $res.raw
    foreach ($ch in @([char]0xFEFF, [char]0x200B, [char]0x200C, [char]0x200D)) { $t = $t.Replace($ch, ' ') }
    $parts = @($t -split '\s+' | Where-Object { $_ -ne '' })
    if ($parts.Count -ge 1) { $res.owner = $parts[0] }
    if ($parts.Count -ge 2) { $res.iso = $parts[1] }
    if ($parts.Count -ge 3) { $n = 0; if ([int]::TryParse($parts[2], [ref]$n)) { $res.pid = $n } }
    return $res
}

function Test-LockStale([string]$file) {
    $o = Read-LockOwner $file
    if ($o.raw -eq '') { return $true }                        # empty / unreadable == ours to take
    $age = ((Get-Date) - (Get-Item $file).LastWriteTime).TotalMinutes
    if ($age -ge $staleMin) { return $true }
    if ($o.pid -gt 0) {
        $alive = $false
        try { Get-Process -Id $o.pid -ErrorAction Stop | Out-Null; $alive = $true } catch { $alive = $false }
        if (-not $alive) { return $true }                      # PID known and dead
    }
    return $false        # no PID field => unknown => fresh => NOT stale
}

function Write-LockFile([string]$file, [string]$owner, [int]$lockPid) {
    $body = $owner + ' ' + (Get-Date).ToString('o') + ' ' + $lockPid
    Set-Content -Path $file -Value $body -Encoding ASCII       # ASCII on purpose: no BOM
    $bytes = [System.IO.File]::ReadAllBytes($file)
    $bom = ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF)
    Say ('LOCK-WRITE ' + (Split-Path $file -Leaf) + ' bytes=' + $bytes.Length + ' bom=' + $bom + ' body=[' + $body + ']')
    if ($bom) { Say 'LOCK-BOM-CHECK FAIL' } else { Say 'LOCK-BOM-CHECK OK' }
    return (-not $bom)
}

function Test-MyLock([string]$file = $lock) { return ((Read-LockOwner $file).owner -eq $me) }

function Test-TakeLock([string]$mainFile, [string]$legacyFile, [string]$owner, [int]$lockPid) {
    foreach ($f in @($mainFile, $legacyFile)) {
        if (-not (Test-Path $f)) { Say ('LOCK-PRE-TAKE ' + (Split-Path $f -Leaf) + '=absent'); continue }
        $o = Read-LockOwner $f
        $age = ((Get-Date) - (Get-Item $f).LastWriteTime).TotalMinutes
        Say ('LOCK-PRE-TAKE ' + (Split-Path $f -Leaf) + ' owner=' + $o.owner + ' pid=' + $o.pid + ' age=' + [math]::Round($age, 1) + 'm')
        if ($o.owner -eq $owner) { continue }
        if (Test-LockStale $f) {
            $why = if ($o.pid -gt 0) { 'pid-dead-or-age-ge-12' } else { 'no-pid-but-age-ge-12' }
            Say ('LOCK-STALE-TAKEOVER ' + (Split-Path $f -Leaf) + ' owner=' + $o.owner + ' pid=' + $o.pid + ' age=' + [math]::Round($age, 1) + 'm reason=' + $why + ' (registered)')
            Remove-Item $f -Force -ErrorAction SilentlyContinue
            continue
        }
        $script:busyOwner = $o.owner
        Say ('LOCK-BUSY ' + (Split-Path $f -Leaf) + ' owner=' + $o.owner + ' pid=' + $o.pid + ' age=' + [math]::Round($age, 1) + 'm => YIELD')
        return $false
    }
    if ((Write-LockFile $mainFile $owner $lockPid) -ne $true) { return $false }
    Start-Sleep -Milliseconds 900
    if (-not ((Read-LockOwner $mainFile).owner -eq $owner)) {
        Say ('LOCK-RACE-LOST readback owner=[' + (Read-LockOwner $mainFile).owner + '] -> yielding (we delete nothing)')
        return $false
    }
    Say ('LOCK-VERIFIED ' + (Read-LockOwner $mainFile).raw.Trim())
    Write-LockFile $legacyFile $owner $lockPid | Out-Null
    Say ('LEGACY-MIRROR-WRITTEN ' + (Split-Path $legacyFile -Leaf))
    return $true
}

function Release-Lock([string]$mainFile = $lock, [string]$legacyFile = $legacy) {
    foreach ($f in @($mainFile, $legacyFile)) {
        if (-not (Test-Path $f)) { Say ('LOCK-RELEASE ' + (Split-Path $f -Leaf) + ' skip (absent)'); continue }
        $o = Read-LockOwner $f
        if ($o.owner -eq $me) {
            Remove-Item $f -Force -ErrorAction SilentlyContinue
            Say ('LOCK-RELEASED (mine: ' + (Split-Path $f -Leaf) + ')')
        } else {
            Say ('LOCK-RELEASE REFUSED (owned by ' + $o.owner + ') -> ' + (Split-Path $f -Leaf) + ' left alone')
        }
    }
}

# The ONLY place allowed to stop the editor.  If the lock is not ours the editor is left alone --
# that is exactly how the 11:43:25 incident interrupted somebody else's Play session.
# Read-only playMode probe -> a STATE STRING ('stopped' / 'playing' / whatever the CLI reports /
# 'unknown' on an unreadable field or a parse failure).  A sandbox never touches the CLI.
function Read-PlayModeOnce {
    if ($SelfTest) { return 'unknown' }
    $raw = & unity command editor_status --format json --no-pager 2>&1 | Out-String
    try { return [string](($raw | ConvertFrom-Json).data.result).playMode } catch { return 'unknown' }
}

# Rule 2 (team, 13:5x): report the orphan to the MAIN AGENT.  Owner/pid are written when readable and
# '(unreadable)' when not -- never a fabricated id.  The alert file is UTF-8 without BOM (no phantom
# first column) and append-only, so concurrent runners cannot corrupt each other's lines.
function Send-OrphanAlert([string]$why, [string]$owner, [int]$lockPid, [string]$pm) {
    $ownerTxt = if ([string]::IsNullOrEmpty($owner)) { '(unreadable)' } else { $owner }
    $pidTxt = if ($lockPid -gt 0) { [string]$lockPid } else { '(unreadable)' }
    $line = (Get-Date).ToString('o') + "`t" + $why + "`t" + $ownerTxt + "`t" + $pidTxt + "`t" + $pm + "`t" + $me + "`t" + $PID
    try {
        [System.IO.File]::AppendAllText($alertFile, $line + "`r`n", (New-Object System.Text.UTF8Encoding($false)))
        Say ('ORPHAN-PLAY-NOTIFY ' + $alertFile + ' line=[' + $line + ']')
    } catch {
        Say ('ORPHAN-ALERT-WRITE-FAIL ' + $_.Exception.Message + ' -- the alarm is still in this trace')
    }
}

function Stop-Editor-Safe([string]$why) {
    if ((Test-MyLock) -ne $true) {
        # Rule 3 (team, 13:4x): skipping the stop because the lock is not ours MUST NOT be silent -- the
        # next taker has to know they may be inheriting a LIVE session (measured 13:43: both lock files
        # ABSENT while `playMode=playing`, i.e. "lock empty" != "editor idle").  Alarm with owner/pid plus
        # a READ-ONLY playMode readback, and notify the lead (rule 2).  Nothing is stopped, nothing touched.
        $oAlarm = Read-LockOwner $lock
        $pmAlarm = Read-PlayModeOnce
        Say ('ORPHAN-PLAY (' + $why + '): lock is not mine (owner=' + $oAlarm.owner + ' pid=' + $oAlarm.pid + ') and playMode=' + $pmAlarm + ' -> no stop issued, their lock left alone (rule 3)')
        if ($pmAlarm -eq 'playing') { Send-OrphanAlert -why ('not-mine-' + $why) -owner $oAlarm.owner -lockPid $oAlarm.pid -pm $pmAlarm }
        Say ('SKIP-EDITOR-STOP (lock not mine) why=' + $why + ' -- the editor was left alone')
        return $false
    }
    Unity-Cmd @('editor_stop') -Quiet | Out-Null
    Say ('EDITOR-STOPPED why=' + $why)
    return $true
}

# RULE 1 (team, 13:15, root-caused from zorder's 13:13 reading): the stop CLI is ASYNCHRONOUS -- it
#   returns while the editor is still leaving Play (backup-scene restore; visible in the 12:29:07 logs).
#   So after triggering a stop we CONFIRM `playMode=stopped` back, bounded.
# RULE 42 (team, 13:5x): the window was widened from 5 x 1s to 15 x 2s (a single trigger makes Unity
#   print `Exited play mode` immediately, so the real disease is "never asked", not "asked but stuck"),
#   and the guard now PUSHES toward the wanted state instead of only waiting.
function Wait-PlayStopped([int]$tries = 15, [int]$sleepSec = 2, [switch]$StopIfPlaying) {
    if ($SelfTest) { return $false }                # a sandbox must never touch the CLI/editor
    if ((Test-MyLock) -ne $true) {
        Say 'PLAY-WAIT-SKIP (lock not mine) -- the editor is not ours to wait on'
        return $false
    }
    $triggered = $false
    for ($i = 1; $i -le $tries; $i++) {
        $pm = Read-PlayModeOnce
        if ($pm -eq 'stopped') {
            Say ('PLAY-STOPPED after ' + $i + ' poll(s) playMode=stopped => safe to hand over')
            return $true
        }
        if (($pm -eq 'playing') -and ($StopIfPlaying -eq $true) -and (-not $triggered)) {
            # rule 42 (d): a guard that only waits merely postpones the failure (measured: d2u27/u27v5
            # waited 5 x 1s, never wired the stop, released both locks, Play stayed up 6.5 minutes).
            Say ('PLAY-STOP-TRIGGER playMode=' + $pm + ' -> Stop-Editor-Safe (guarded: only while the lock is ours)')
            Stop-Editor-Safe 'wait-play-stopped' | Out-Null
            $triggered = $true
        }
        Start-Sleep -Seconds $sleepSec
    }
    Say ('PLAY-WAIT-TIMEOUT tries=' + $tries + ' x ' + $sleepSec + 's playMode=' + (Read-PlayModeOnce))
    return $false
}

# Rule 42 (team, 13:5x -- this REPLACES the earlier "release + alarm" ruling): if the session is still
# alive after a bounded trigger+confirm, HOLD the lock instead of releasing it.  Measured why: the 13:42
# orphan produced "both locks ABSENT + playMode=playing" = an OWNERLESS session that no runner is allowed
# to stop, and the team waited 6.5 minutes for a human.  Holding preserves the ownership fact and costs
# no extra time (every correct gate refuses to start while playing); it then falls under the existing
# 12-minute stale rule, and rule 4 makes the taker responsible for stopping it first.
# Returns a STATUS STRING: 'stopped' (safe to hand over) / 'withheld' (still alive; alarm + alert sent) /
# 'not-mine' (somebody else owns it now; we touch nothing).
function Confirm-StoppedOrWithhold([string]$why, [int]$tries = 15, [int]$sleepSec = 2) {
    $o0 = Read-LockOwner $lock
    if ($o0.owner -ne $me) {
        $pm0 = Read-PlayModeOnce
        Say ('ORPHAN-PLAY (' + $why + '): the lock is NOT mine anymore (owner=' + $o0.owner + ' pid=' + $o0.pid + ') and playMode=' + $pm0 + ' -> nothing to confirm, nothing of theirs is touched (rule 3)')
        if ($pm0 -eq 'playing') { Send-OrphanAlert -why ('not-mine-' + $why) -owner $o0.owner -lockPid $o0.pid -pm $pm0 }
        return 'not-mine'
    }
    if ((Wait-PlayStopped -tries $tries -sleepSec $sleepSec -StopIfPlaying) -eq $true) { return 'stopped' }
    Stop-Editor-Safe ($why + '-retry') | Out-Null      # one explicit re-trigger round, then a shorter window
    if ((Wait-PlayStopped -tries 8 -sleepSec $sleepSec) -eq $true) { return 'stopped' }
    $o = Read-LockOwner $lock
    $pm = Read-PlayModeOnce
    Say ('ORPHAN-PLAY (' + $why + '): owner=' + $o.owner + ' pid=' + $o.pid + ' playMode=' + $pm + ' after ' + $tries + ' + 8 bounded polls')
    Say ('LOCK-HELD-ON-PURPOSE / LOCK-RELEASE WITHHELD (' + (Split-Path $lock -Leaf) + ' + ' + (Split-Path $legacy -Leaf) + ') -- NOT releasing: a released lock with a live session is ownerless (13:42 cost the team 6.5 min); the 12-min stale rule plus the taker stop-and-confirm duty (rule 4) are the designed path')
    Send-OrphanAlert -why $why -owner $o.owner -lockPid $o.pid -pm $pm
    return 'withheld'
}

# =============================================================================
# -SelfTest : lock protocol + encoding + static assertions, in a PER-PID sandbox.
#   Touches neither the real lock nor the editor.  Every check ships with a KNOWN-BAD sample
#   (a check that cannot fail is not a check -- the team found three kinds of false green today).
# =============================================================================
if ($SelfTest) {
    # Real-lock state is read before AND after the sandbox run (item 34), on the paths this runner
    # actually uses: $testDir = <root>/.ai-tmp/test -- NOT client/play-running.lock, which is a
    # DIFFERENT file.  Measured today (13:26/13:30): reading client/ made a "no lock is held" claim
    # look evidenced while the real drop points were held by u44impl.  Read the right path.
    $lkBefore = @((Test-Path $lock), (Test-Path $legacy))
    $sb = Join-Path $root ('.ai-tmp/test/u26-lockselftest-' + $PID)
    if (Test-Path $sb) { Remove-Item $sb -Recurse -Force -ErrorAction SilentlyContinue }
    New-Item -ItemType Directory -Force -Path $sb | Out-Null
    $script:st = 0; $script:sf = 0
    function T([string]$name, [bool]$ok, [string]$detail) {
        if ($ok) { $script:st++ } else { $script:sf++ }
        Write-Host ('[' + $(if ($ok) { ' OK ' } else { 'FAIL' }) + '] ' + $name + '   (' + $detail + ')')
    }
    $m = Join-Path $sb 'play-running.lock'
    $l = Join-Path $sb 'play.lock'
    try {
        $ok1 = Test-TakeLock $m $l 'u26-zorder' 4242
        $o1 = Read-LockOwner $m; $o2 = Read-LockOwner $l
        $b1 = [System.IO.File]::ReadAllBytes($m)
        T '1 take-on-empty' ($ok1 -and $o1.owner -eq 'u26-zorder' -and $o1.pid -eq 4242) ('owner=' + $o1.owner + ' pid=' + $o1.pid)
        T '2 write-3-fields' ((@([System.IO.File]::ReadAllText($m) -split '\s+' | Where-Object { $_ -ne '' })).Count -eq 3) 'fields=3'
        T '3 lock-no-BOM' (-not ($b1[0] -eq 0xEF -and $b1[1] -eq 0xBB -and $b1[2] -eq 0xBF)) ('first3=' + $b1[0] + ',' + $b1[1] + ',' + $b1[2])
        T '4 mirror-written' ($o2.owner -eq 'u26-zorder') ('play.lock owner=' + $o2.owner)
        T '5 my-lock-recognised' (Test-MyLock $m) 'Test-MyLock on our own sandbox lock'
        Set-Content -Path $m -Value 'other-slice 2026-09-24T12:00:00+08:00' -Encoding ASCII
        Remove-Item $l -Force -ErrorAction SilentlyContinue
        T '6 fresh-no-pid-YIELDS' (-not (Test-TakeLock $m $l 'u26-zorder' 4242)) '2-field fresh lock must NOT be taken (11:42 accident)'
        (Get-Item $m).LastWriteTime = (Get-Date).AddMinutes(-13)
        T '7 old-no-pid-TAKEOVER' (Test-TakeLock $m $l 'u26-zorder' 4242) 'no PID field but age 13m => stale'
        Set-Content -Path $m -Value 'other-slice 2026-09-24T12:00:00+08:00 999999' -Encoding ASCII
        Remove-Item $l -Force -ErrorAction SilentlyContinue
        T '8 dead-pid-TAKEOVER' (Test-TakeLock $m $l 'u26-zorder' 4242) 'pid 999999 not running => stale'
        Set-Content -Path $m -Value ('other-slice 2026-09-24T12:00:00+08:00 ' + $PID) -Encoding ASCII
        Remove-Item $l -Force -ErrorAction SilentlyContinue
        T '9 live-pid-fresh-YIELDS' (-not (Test-TakeLock $m $l 'u26-zorder' 4242)) 'live pid + fresh => yield'
        Release-Lock $m $l
        T '10 release-refuses-others' ((Test-Path $m) -and ((Read-LockOwner $m).owner -eq 'other-slice')) 'somebody else file must stay'
        $bytes = [byte[]](0xEF, 0xBB, 0xBF) + [System.Text.Encoding]::ASCII.GetBytes('other-slice 2026-09-24T12:00:00+08:00')
        [System.IO.File]::WriteAllBytes($m, $bytes)
        T '11 bom-lock-fresh-YIELDS' (-not (Test-TakeLock $m $l 'u26-zorder' 4242)) 'BOM stripped => owner=other-slice => yield'
        $selfPath = $PSCommandPath
        $raw = [System.IO.File]::ReadAllLines($selfPath)
        $code = @($raw | ForEach-Object { if ($_ -match '^\s*#') { '' } else { $_ } })
        # The static checks run on the PRODUCTION region only.  The self-test block itself mentions
        # these names (in regexes and in the fake samples) and would otherwise pollute the counts --
        # measured: the first version reported 7 editor_stop hits and "play@293 < take@331".
        # That is the same "false red" family the team hit twice today.
        # Measured AGAIN (13:29): this file now has TWO `if ($SelfTest)` blocks (a small early one
        #   for the evidence-hygiene trace redirect + the big assertion block).  Blanking only the
        #   FIRST match left the big block inside `$prod`, so the scanner counted its own FIXTURES:
        #   7 editor_stop / 6 Write-Output / 3 bare take-lock / play@420 < take@581.  Region trimming
        #   must therefore brace-match EVERY block, not "the first one" -- a scanner that silently
        #   measures its own test fixtures is exactly the false-green family we are fighting.
        $prod = @($code)
        for ($i = 0; $i -lt $code.Count; $i++) {
            if ($code[$i] -notmatch '^if \(\$SelfTest\)') { continue }
            $depth = 0; $started = $false
            for ($j = $i; $j -lt $code.Count; $j++) {
                $depth += ([regex]::Matches($code[$j], '\{')).Count - ([regex]::Matches($code[$j], '\}')).Count
                if ($code[$j] -match '\{') { $started = $true }
                $prod[$j] = ''
                if ($started -and $depth -le 0) { break }
            }
        }
        $stop = -1; $play = -1; $take = -1; $asciiLock = -1; $stopCount = 0
        for ($i = 0; $i -lt $prod.Count; $i++) {
            $ln = $prod[$i]
            # Count the actual CLI CALL, not every mention of the verb: the real rule is "the editor is
            # stopped exactly once", and a log/alarm string that NAMES the verb must not inflate it
            # (measured need: rule 3 added an ORPHAN-PLAY message containing the word).  The KNOWN-BAD
            # sample below writes the same call form, so sample and scan share one predicate.
            if ($ln -match "Unity-Cmd\s*@\(\s*'editor_stop'") { $stopCount++; if ($stop -lt 0) { $stop = $i + 1 } }
            if ($ln -match "Unity-Cmd\s*@\(\s*'editor_play'") { if ($play -lt 0) { $play = $i + 1 } }
            if ($ln -match 'Test-TakeLock \$lock') { if ($take -lt 0) { $take = $i + 1 } }
            if ($ln -match 'Set-Content' -and $ln -match '\$file' -and $ln -match '-Encoding ASCII') { if ($asciiLock -lt 0) { $asciiLock = $i + 1 } }
        }
        $gs = 0; $ge = 0
        for ($i = 0; $i -lt $prod.Count; $i++) {
            if ($gs -eq 0 -and $prod[$i] -match '^function Stop-Editor-Safe') { $gs = $i + 1; continue }
            if ($gs -gt 0 -and $ge -eq 0 -and ($i + 1) -gt $gs -and $prod[$i] -match '^\}') { $ge = $i + 1 }
        }
        T '12 editor_stop-only-once' ($stopCount -eq 1) ('code lines with editor_stop = ' + $stopCount)
        T '13 editor_stop-inside-guard' (($ge -gt 0) -and ($stop -ge $gs) -and ($stop -le $ge)) ('stop@' + $stop + ' guard=' + $gs + '..' + $ge)
        T '14 editor_play-after-take' (($take -gt 0) -and ($play -gt $take)) ('take@' + $take + ' play@' + $play)
        T '15 lock-write-is-ASCII' ($asciiLock -gt 0) ('Set-Content -Encoding ASCII @' + $asciiLock)
        $fake = Join-Path $sb 'fake-two-stops.ps1'
        Set-Content -Path $fake -Value ("Unity-Cmd @('editor_stop')" + "`n" + "Say 'x'" + "`n" + "Unity-Cmd @('editor_stop')") -Encoding ASCII
        $c2 = 0; foreach ($ln in @([System.IO.File]::ReadAllLines($fake) | ForEach-Object { if ($_ -match '^\s*#') { '' } else { $_ } })) { if ($ln -match "Unity-Cmd\s*@\(\s*'editor_stop'") { $c2++ } }
        T '16 KNOWN-BAD two-stops-FLAGGED' ($c2 -eq 2) ('fake file editor_stop lines = ' + $c2 + ' (the checker must not be blind)')
        # Static guards for the "false exclusive" family (u52play): a print helper that emits into the
        # SUCCESS pipeline turns a lock function's return value into an array, and `if (<array>)` is
        # always TRUE => a LOST lock race is read as a win.  Root cause absent here (Say uses
        # Write-Host; 0 Write-Output in the file), but these two guards make it unable to come back.
        $wo = 0; $hostSeen = $false; $bareTake = 0
        for ($i = 0; $i -lt $prod.Count; $i++) {
            $ln = $prod[$i]
            if ($ln -match 'Write-Output') { $wo++ }
            if ($ln -match 'Write-Host') { $hostSeen = $true }
            if ($ln -match 'if \(Test-TakeLock ' -and $ln -notmatch '-eq \$true') { $bareTake++ }
        }
        T '20 no-Write-Output-in-runner' ($wo -eq 0) ('Write-Output lines = ' + $wo)
        T '21 print-helper-uses-WriteHost' $hostSeen 'Write-Host present in the printing helper'
        T '22 take-lock-compared-explicitly' ($bareTake -eq 0) ('bare if (Test-TakeLock ...) call sites = ' + $bareTake)
        $fakeWo = Join-Path $sb 'fake-write-output.ps1'
        Set-Content -Path $fakeWo -Value ("function Say(`$m) { Write-Output `$m }" + "`n" + "if (Take-Lock) { Say 'x' }") -Encoding ASCII
        $c3 = 0; $c4 = 0
        foreach ($ln in [System.IO.File]::ReadAllLines($fakeWo)) {
            if ($ln -match 'Write-Output') { $c3++ }
            if ($ln -match 'if \(Take-Lock\)' -and $ln -notmatch '-eq \$true') { $c4++ }
        }
        T '23 KNOWN-BAD write-output-in-helper-FLAGGED' ($c3 -eq 1) ('fake Write-Output lines = ' + $c3)
        T '24 KNOWN-BAD bare-take-if-FLAGGED' ($c4 -eq 1) ('fake bare if (Take-Lock) = ' + $c4)
        # RULE 1 static guard: inside `finally`, the bounded `Wait-PlayStopped` poll must run BEFORE
        #   `Release-Lock` -- otherwise our own tail is a window with the lock free while Play still runs.
        # RULE 42 static guard: inside `finally`, the CONFIRMED hand-over must precede the release, and
        # the release must be CONDITIONAL on that confirmation (releasing while a session is alive is
        # exactly what produced the ownerless session at 13:42).  Scanned only AFTER `finally`, because
        # the take-over duty (rule 4) uses the same helper earlier in the file.
        $fin = 0
        for ($i = 0; $i -lt $prod.Count; $i++) { if ($prod[$i] -match '^\}\s*finally\s*\{') { $fin = $i + 1 } }
        $conf = -1; $rel = -1; $condRel = -1
        for ($i = $fin; $i -lt $prod.Count; $i++) {
            # allow a leading assignment (`$handover = Confirm-StoppedOrWithhold 'finally'`) -- a
            # line-anchored pattern silently matched NOTHING here (measured: Confirm@-1), which is the
            # "looks right, matches nothing" family; the KNOWN-BAD sample uses the same pattern.
            if ($conf -lt 0 -and $prod[$i] -match "Confirm-StoppedOrWithhold\s+'") { $conf = $i + 1 }
            if ($condRel -lt 0 -and $prod[$i] -match '^\s*if \(\$handover') { $condRel = $i + 1 }
            if ($rel -lt 0 -and $prod[$i] -match '^\s*Release-Lock\s*$') { $rel = $i + 1 }
        }
        T '25 handover-confirm-before-conditional-release' (($fin -gt 0) -and ($conf -gt $fin) -and ($rel -gt $conf) -and ($condRel -gt 0) -and ($condRel -lt $rel)) ('finally@' + $fin + ' Confirm@' + $conf + ' conditional-release-if@' + $condRel + ' Release-Lock@' + $rel + ' (release must be conditional on the confirmation)')
        $fakeW = Join-Path $sb 'fake-release-no-confirm.ps1'
        Set-Content -Path $fakeW -Value ('Release-Lock' + "`n" + "Confirm-StoppedOrWithhold 'x'") -Encoding ASCII
        $fw = [System.IO.File]::ReadAllLines($fakeW); $w2 = -1; $r2 = -1
        for ($i = 0; $i -lt $fw.Count; $i++) {
            if ($fw[$i] -match "Confirm-StoppedOrWithhold\s+'") { if ($w2 -lt 0) { $w2 = $i + 1 } }
            if ($fw[$i] -match '^\s*Release-Lock\s*$') { if ($r2 -lt 0) { $r2 = $i + 1 } }
        }
        T '26 KNOWN-BAD release-before-confirm-FLAGGED' (($r2 -gt 0) -and ($w2 -gt 0) -and ($r2 -lt $w2)) ('fake rel@' + $r2 + ' confirm@' + $w2 + ' => the ordering rule must flag this (the check is not blind)')
        # RULE (team 13:2x): the readiness gate is FAIL-CLOSED.  Only a REACHABLE playMode reading may
        # start Play; an unreadable one must YIELD (exit) BEFORE the lock is taken; and a parsed
        # `playing` must be followed by an ABORT.  Process assertions -- "we edited it once" is not a
        # guarantee, and a default value that merely differs from 'playing' is a coincidence, not a rule.
        $takeLn = -1; $unkLn = -1; $unkExit = -1; $playLn = -1; $playAbort = -1
        for ($i = 0; $i -lt $prod.Count; $i++) {
            $ln = $prod[$i]
            if ($takeLn -lt 0 -and $ln -match 'Test-TakeLock \$lock') { $takeLn = $i + 1 }
            if ($unkLn -lt 0 -and $ln -match 'LOCK-GATE-UNKNOWN') { $unkLn = $i + 1 }
            if ($unkLn -gt 0 -and $unkExit -lt 0 -and ($i + 1) -gt $unkLn -and $ln -match '^\s*exit \d') { $unkExit = $i + 1 }
            if ($playLn -lt 0 -and $ln -match "playMode\s*-eq\s*'playing'") { $playLn = $i + 1 }
            if ($playLn -gt 0 -and $playAbort -lt 0 -and ($i + 1) -gt $playLn -and $ln -match 'ABORT') { $playAbort = $i + 1 }
        }
        T '27 gate-unknown-yields-before-lock' (($unkLn -gt 0) -and ($unkExit -gt $unkLn) -and ($unkExit -lt $takeLn)) ('take@' + $takeLn + ' UNKNOWN@' + $unkLn + ' exit@' + $unkExit + ' (an unreadable playMode must exit BEFORE taking the lock)')
        T '28 gate-playing-aborts-before-lock' (($playLn -gt 0) -and ($playAbort -gt $playLn) -and ($playAbort -lt $takeLn)) ('playing-if@' + $playLn + ' ABORT@' + $playAbort + ' take@' + $takeLn)
        $fakeF = Join-Path $sb 'fake-fail-open-gate.ps1'
        Set-Content -Path $fakeF -Value ("`$pmNow = 'unknown'" + "`n" + "if (`$pmNow -eq 'playing') { Say 'ABORT' }" + "`n" + "if (Test-TakeLock `$lock `$legacy `$me `$PID) { }") -Encoding ASCII
        $ff = [System.IO.File]::ReadAllLines($fakeF); $fu = -1; $fuExit = -1; $ft = -1
        for ($i = 0; $i -lt $ff.Count; $i++) {
            $ln = $ff[$i]
            if ($ft -lt 0 -and $ln -match 'Test-TakeLock \$lock') { $ft = $i + 1 }
            if ($fu -lt 0 -and $ln -match 'LOCK-GATE-UNKNOWN') { $fu = $i + 1 }
            if ($fu -gt 0 -and $fuExit -lt 0 -and ($i + 1) -gt $fu -and $ln -match '^\s*exit \d') { $fuExit = $i + 1 }
        }
        $fakeFlagged = -not (($fu -gt 0) -and ($fuExit -gt $fu) -and ($fuExit -lt $ft))
        T '29 KNOWN-BAD fail-open-gate-FLAGGED' $fakeFlagged ('fake: UNKNOWN@' + $fu + ' exit@' + $fuExit + ' take@' + $ft + ' => the SAME predicate rejects it (fail-open cannot pass)')
        # RULE (team 13:2x, found by classcols): a `continue` OUTSIDE a loop SILENTLY ends the script --
        # exit 0, no terminal marker, no lock, no Play, no error.  The 2-line patch about to be pasted
        # into six runners is exactly that shape, so it must be caught mechanically.  This is a
        # CONTROL-FLOW property => checked with the AST (a regex cannot see nesting).
        # Measured fixture (top level): 'BEFORE'; if ($true){ '...continue'; continue }; 'AFTER'
        #   => BEFORE + the message are printed, AFTER/LOCK-TAKEN never are, exit code 0.
        function Get-ContinueOutsideLoop([string]$path) {
            $perr = $null
            $past = [System.Management.Automation.Language.Parser]::ParseFile($path, [ref]$null, [ref]$perr)
            $out = @()
            foreach ($c in $past.FindAll({ param($n) $n -is [System.Management.Automation.Language.ContinueStatementAst] }, $true)) {
                $p = $c.Parent; $inLoop = $false
                while ($null -ne $p) {
                    if (($p -is [System.Management.Automation.Language.ForStatementAst]) -or ($p -is [System.Management.Automation.Language.ForEachStatementAst]) -or ($p -is [System.Management.Automation.Language.WhileStatementAst]) -or ($p -is [System.Management.Automation.Language.DoWhileStatementAst])) { $inLoop = $true; break }
                    $p = $p.Parent
                }
                if (-not $inLoop) { $out += $c.Extent.StartLineNumber }
            }
            return $out
        }
        $badMine = @(Get-ContinueOutsideLoop $selfPath)
        T '30 no-continue-outside-loop' ($badMine.Count -eq 0) ('continue statements outside any loop = ' + $(if ($badMine.Count -eq 0) { 'none' } else { ($badMine -join ',') }) + ' (that trap ends the run silently with exit 0)')
        $fakeC = Join-Path $sb 'fake-continue-outside-loop.ps1'
        Set-Content -Path $fakeC -Value ("Say 'BEFORE'" + "`n" + "if (`$true) { Say 'GATE-UNKNOWN -> continue'; continue }" + "`n" + "Say 'AFTER'") -Encoding ASCII
        $badFake = @(Get-ContinueOutsideLoop $fakeC)
        T '31 KNOWN-BAD continue-outside-loop-FLAGGED' ($badFake.Count -eq 1) ('fixture: count=' + $badFake.Count + ' at line(s) [' + ($badFake -join ',') + '] (must be exactly 1 => the AST check has teeth)')
        # Terminal-marker rule: `exit 0` and "the chain never ran" are the SAME shape, so a reader must
        # be able to tell them apart from the log alone => every production exit is preceded by the
        # END marker (and only a real run prints LOCK-ACQUIRED before it).
        # u52play's taxonomy: this is an ABSENCE-type check ("0 hits") and an absence-type check can
        # be green simply because the regex never matches anything.  Such checks MUST have a KNOWN-BAD
        # sample, and the sample must run through the SAME helper as the real check (item 35).
        function Get-ExitsWithoutEndMarker([string[]]$lines) {
            $n = 0
            for ($i = 0; $i -lt $lines.Count; $i++) {
                if ($lines[$i] -notmatch '^\s*exit \d') { continue }
                $has = $false
                for ($k = [Math]::Max(0, $i - 3); $k -lt $i; $k++) { if ($lines[$k] -match "Say 'END'") { $has = $true } }
                if (-not $has) { $n++ }
            }
            return $n
        }
        $exitNoMarker = Get-ExitsWithoutEndMarker $prod
        T '32 terminal-marker-before-every-exit' ($exitNoMarker -eq 0) ('production exits without a preceding END marker = ' + $exitNoMarker + ' (exit 0 vs never-ran must stay distinguishable)')
        $fakeTM = Join-Path $sb 'fake-exit-no-marker.ps1'
        Set-Content -Path $fakeTM -Value ("Say 'X'" + "`n" + "exit 3") -Encoding ASCII
        $tmFake = Get-ExitsWithoutEndMarker ([System.IO.File]::ReadAllLines($fakeTM))
        T '35 KNOWN-BAD terminal-marker-missing-FLAGGED' ($tmFake -eq 1) ('fixture exits without a preceding END = ' + $tmFake + ' (must be 1 => this absence-type check is not blind)')
        # Real-lock evidence (works for -SelfTest and -SelfCheckOnly): the drop points this runner
        # really uses must be untouched by a sandbox run.  Cheap, and it turns "trust me" into a number.
        $lkAfter = @((Test-Path $lock), (Test-Path $legacy))
        T '34 real-locks-untouched-by-sandbox' (($lkAfter[0] -eq $lkBefore[0]) -and ($lkAfter[1] -eq $lkBefore[1])) ('dir=' + $testDir + ' before=[' + ($lkBefore -join ',') + '] after=[' + ($lkAfter -join ',') + '] (client/ is a DIFFERENT location, not read here)')
        # Product self-fingerprint (team 13:2x): the trace must carry the runner's OWN hash AND it must
        # match a fresh recomputation -- "paste the hash into the report" goes stale within minutes
        # (measured: 2.5 min, 8 quoted line numbers dead), so the fingerprint has to be self-verifying.
        $fpInTrace = ''
        if (Test-Path $trace) {
            foreach ($l in [System.IO.File]::ReadAllLines($trace)) {
                if ($l -match 'RUNNER-FINGERPRINT sha256_16=([0-9A-Fa-f]{16})') { $fpInTrace = $Matches[1]; break }
            }
        }
        $fpNow = (Get-FileHash $selfPath -Algorithm SHA256).Hash.Substring(0, 16)
        T '33 fingerprint-line-accurate' (($fpInTrace -ne '') -and ($fpInTrace -eq $fpNow)) ('trace states ' + $(if ($fpInTrace -eq '') { '(missing)' } else { $fpInTrace }) + ' / recomputed ' + $fpNow + ' (a stale pasted hash must be impossible)')
        # READ-WHILE-WRITING (team ruling 13:4x): every conclusion above is drawn from the file's TEXT,
        # so if the file changed DURING this run the conclusions are about a file that no longer
        # exists (measured elsewhere: a file parsed at 13:30:11 gave 2 syntax errors, and the same file
        # at 13:30:56 parsed clean => an editorial transient, not a code defect).  Compare the hash to
        # the one taken at startup; the trace then carries the stability proof for its own findings.
        $selfMtimeNow = (Get-Item $selfPath).LastWriteTime
        $selfShaNow = (Get-FileHash $selfPath -Algorithm SHA256).Hash
        T '36 file-stable-during-selfcheck' (($selfShaNow -eq $selfSha) -and ($selfMtimeNow -eq $selfMtime0)) ('sha256_16 ' + $selfSha.Substring(0, 16) + ' -> ' + $selfShaNow.Substring(0, 16) + ' ; mtime ' + $selfMtime0.ToString('HH:mm:ss.fff') + ' -> ' + $selfMtimeNow.ToString('HH:mm:ss.fff') + ' (any change invalidates this trace findings)')
        # INJECTION proof (team ruling 13:4x: a check meant to hold in the FUTURE must be proven on the
        # REAL file text, not only on a hand-written fixture): take the real production text, append one
        # top-level `continue` (the exact silent-exit-0 shape), and run the SAME AST helper => it must
        # flag exactly that one line.  This is stronger than a separate fake file: it proves the helper
        # works against THIS file's real structure (inner loops, comments, strings included).
        $inj = Join-Path $sb 'injected-real-text.ps1'
        [System.IO.File]::WriteAllLines($inj, [string[]](@([System.IO.File]::ReadAllLines($selfPath)) + @('continue')), (New-Object System.Text.UTF8Encoding($false)))
        $injHits = @(Get-ContinueOutsideLoop $inj)
        T '37 INJECTED continue-outside-loop into REAL text FLAGGED' ($injHits.Count -eq 1) ('injected copy: count=' + $injHits.Count + ' at line(s) [' + ($injHits -join ',') + '] of ' + ([System.IO.File]::ReadAllLines($inj)).Count + ' lines (green->red on the real text)')
        # Rule 34 (three directions): a negative sample proves the check CAN fail -- not that it fails
        # for the RIGHT reason, and nothing at all about a check that is ALWAYS red (a regex matching
        # everything is "always failing" and would look like a strict check).  So each helper also gets
        # a POSITIVE control: a compliant snippet must PASS.  Result per helper:
        #   (1) real file => green   (2) known-bad => red   (3) positive snippet => green
        $posC = Join-Path $sb 'pos-continue-in-loop.ps1'
        Set-Content -Path $posC -Value ("for (`$i = 0; `$i -lt 3; `$i++) { if (`$i -eq 1) { continue } }" + "`n" + "foreach (`$x in 1..2) { continue }") -Encoding ASCII
        $posHits = @(Get-ContinueOutsideLoop $posC)
        T '38 POSITIVE-CONTROL continue-in-loop-PASSES' ($posHits.Count -eq 0) ('fixture has 2 COMPLIANT continues; out-of-loop hits = ' + $posHits.Count + ' (must be 0 => the check is not always-red)')
        $posT = Join-Path $sb 'pos-exit-with-marker.ps1'
        Set-Content -Path $posT -Value ("Say 'END'" + "`n" + "exit 3") -Encoding ASCII
        $posTM = Get-ExitsWithoutEndMarker ([System.IO.File]::ReadAllLines($posT))
        T '39 POSITIVE-CONTROL exit-with-END-marker-PASSES' ($posTM -eq 0) ('fixture has END before exit; missing-marker count = ' + $posTM + ' (must be 0)')
        # Rule 3 (team 13:4x, measured 13:43: BOTH lock files ABSENT while playMode=playing): a skipped
        # stop must raise the alarm before anything else can happen -- existence + position, so a silent
        # hand-over cannot come back (the next taker must know they may inherit a live session).
        $alarmLn = -1; $stopCallLn = -1
        for ($i = 0; $i -lt $prod.Count; $i++) {
            if ($alarmLn -lt 0 -and $prod[$i] -match 'ORPHAN-PLAY') { $alarmLn = $i + 1 }
            if ($stopCallLn -lt 0 -and $prod[$i] -match "Unity-Cmd\s*@\(\s*'editor_stop'") { $stopCallLn = $i + 1 }
        }
        T '40 orphan-play-alarm-present' (($alarmLn -gt 0) -and ($stopCallLn -gt 0) -and ($alarmLn -lt $stopCallLn)) ('ORPHAN-PLAY@' + $alarmLn + ' editor_stop-call@' + $stopCallLn + ' (a skipped stop must warn, never hand over silently)')
        # Hand-over must be trigger -> confirm -> release ON EVERY PATH (rule 42): a wait guard with no
        # pusher just postpones the failure (d2u27/u27v5 waited 5 x 1s, never stopped, Play stayed up
        # 6.5 min).  Checked INSIDE the finally block, in order.
        $finLn = -1; $trigLn = -1; $confLn = -1; $relLn = -1
        for ($i = 0; $i -lt $prod.Count; $i++) { if ($prod[$i] -match '^\}\s*finally\s*\{') { $finLn = $i + 1 } }
        for ($i = $finLn; $i -gt 0 -and $i -lt $prod.Count; $i++) {
            $ln = $prod[$i]
            if ($trigLn -lt 0 -and $ln -match "^\s*Stop-Editor-Safe\s+'") { $trigLn = $i + 1 }
            if ($confLn -lt 0 -and $ln -match "Confirm-StoppedOrWithhold\s+'") { $confLn = $i + 1 }
            if ($relLn -lt 0 -and $ln -match '^\s*Release-Lock\s*$') { $relLn = $i + 1 }
            if (($trigLn -gt 0) -and ($confLn -gt $trigLn) -and ($relLn -gt $confLn)) { break }
        }
        T '41 handover-trigger-confirm-release' (($finLn -gt 0) -and ($trigLn -gt $finLn) -and ($confLn -gt $trigLn) -and ($relLn -gt $confLn)) ('finally@' + $finLn + ' TRIGGER@' + $trigLn + ' CONFIRM@' + $confLn + ' RELEASE@' + $relLn + ' (waiting without pushing is not hand-over)')
        # Closing self-check (team, 13:5x): "the trigger will actually be CALLED".  EXISTENCE IS NOT
        # REACHABILITY -- d2u27 had exactly one `editor_stop`, inside its guard, every static assertion
        # green, and still blocked the team for 6.5 minutes because that path is unreachable in a normal
        # shutdown.  Judged by AST: at least one guard call must sit on an UNCONDITIONAL path (no
        # IfStatementAst in its Parent chain).  A parse failure is FAIL, not PASS (fail-closed).
        function Get-UnconditionalGuardCalls([string]$path, [string]$guard) {
            $perr2 = $null
            $past2 = [System.Management.Automation.Language.Parser]::ParseFile($path, [ref]$null, [ref]$perr2)
            if ($null -ne $perr2 -and @($perr2).Count -gt 0) { return $null }
            $cnt = 0
            foreach ($c in $past2.FindAll({ param($node) $node -is [System.Management.Automation.Language.CommandAst] }, $true)) {
                if ($c.GetCommandName() -ne $guard) { continue }
                $p2 = $c.Parent; $conditional = $false
                while ($null -ne $p2) {
                    if ($p2 -is [System.Management.Automation.Language.IfStatementAst]) { $conditional = $true; break }
                    $p2 = $p2.Parent
                }
                if (-not $conditional) { $cnt++ }
            }
            return $cnt
        }
        $unc = Get-UnconditionalGuardCalls $selfPath 'Stop-Editor-Safe'
        T '42 stop-trigger-reachable' (($null -ne $unc) -and ($unc -ge 1)) ('unconditional Stop-Editor-Safe calls = ' + $(if ($null -eq $unc) { '(parse failed => FAIL)' } else { $unc }) + ' (existence is not reachability)')
        $fakeU = Join-Path $sb 'fake-guard-only-in-if.ps1'
        Set-Content -Path $fakeU -Value ("if (`$x) { Stop-Editor-Safe 'only-here' }") -Encoding ASCII
        $uncFake = Get-UnconditionalGuardCalls $fakeU 'Stop-Editor-Safe'
        T '43 KNOWN-BAD guard-only-inside-if-FLAGGED' (($null -ne $uncFake) -and ($uncFake -eq 0)) ('fake unconditional calls = ' + $(if ($null -eq $uncFake) { '(parse failed)' } else { $uncFake }) + ' (must be 0 => the reachability check has teeth)')
        # Rule 2 needs a MECHANISM, not a habit: the alert writer must really land a line.  It writes the
        # SHARED channel, so the probe points it at the sandbox for one call and restores it -- the real
        # channel is only ever written with real alarms.
        $alertReal = $alertFile
        $alertFile = Join-Path $sb 'alert-write-probe.tsv'
        Send-OrphanAlert -why 'selftest-probe' -owner 'u26-selftest' -lockPid 4242 -pm 'playing'
        $probeOk = (Test-Path $alertFile) -and (([System.IO.File]::ReadAllText($alertFile)) -match 'selftest-probe')
        $alertFile = $alertReal
        T '44 orphan-alert-write-lands' $probeOk ('sandbox probe line landed = ' + $probeOk + '; the shared channel unaffected (probe wrote to the sandbox copy)')
        # ...and it must be WIRED at every alarm site, not merely defined (a defined-but-never-called
        # notifier is the "assertion green, behaviour missing" family).
        $alertCalls = 0
        foreach ($l in $prod) { if ($l -match 'Send-OrphanAlert\s+-why') { $alertCalls++ } }
        T '45 orphan-alert-wired' ($alertCalls -ge 3) ('Send-OrphanAlert call sites = ' + $alertCalls + ' (not-mine stop + not-mine confirm + withheld hand-over)')
        # Rule (team, 13:5x): three line-counting algorithms differ, and `Measure-Object -Line` is the
        # worst because it SILENTLY SKIPS BLANK LINES -- it looks like a line count and gives a stable but
        # wrong green/red.  The unified `lines=` label therefore belongs to ReadAllLines().Count only; a
        # blank-skipping count must be labelled `rows=` (or similar) and say its algorithm.
        # The label regex uses a lookbehind so `evidenceLines=` / `.Lines` do NOT count as a `lines=` label
        # (measured: the first version both missed the real field and would have flagged `evidenceLines=`).
        $moBad = 0; $rowsField = 0
        foreach ($l in $prod) {
            if (($l -match 'Measure-Object\s+-Line') -and ($l -match '(?<![A-Za-z])lines\s*=')) { $moBad++ }
            if ($l -match '(?<![A-Za-z])rows\s*=') { $rowsField++ }
        }
        T '46 no-blank-skipping-lines-field' (($moBad -eq 0) -and ($rowsField -ge 1)) ('Measure-Object-with-lines= lines = ' + $moBad + ' ; rows= fields = ' + $rowsField + ' (a blank-skipping count must never be labelled lines=)')
        $rb = [System.IO.File]::ReadAllBytes($selfPath); $na = 0
        foreach ($b in $rb) { if ($b -gt 127) { $na++ } }
        $hb = ($rb.Length -ge 3 -and $rb[0] -eq 0xEF -and $rb[1] -eq 0xBB -and $rb[2] -eq 0xBF)
        T '17 runner-encoding-ok (ascii or utf8-bom)' (($na -eq 0) -or $hb) ('nonAscii=' + $na + ' bom=' + $hb)
        $fake2 = Join-Path $sb 'fake-play-before-take.ps1'
        Set-Content -Path $fake2 -Value ("Unity-Cmd @('editor_play')" + "`n" + "if (Test-TakeLock `$lock `$legacy `$me `$PID) { }") -Encoding ASCII
        $f3 = [System.IO.File]::ReadAllLines($fake2); $p2 = 0; $t2 = 0
        for ($i = 0; $i -lt $f3.Count; $i++) {
            if ($f3[$i] -match "Unity-Cmd\s*@\(\s*'editor_play'") { if ($p2 -eq 0) { $p2 = $i + 1 } }
            if ($f3[$i] -match 'Test-TakeLock \$lock') { if ($t2 -eq 0) { $t2 = $i + 1 } }
        }
        T '19 KNOWN-BAD play-before-take-FLAGGED' (($p2 -gt 0) -and ($t2 -gt 0) -and ($p2 -lt $t2)) ('play@' + $p2 + ' take@' + $t2 + ' => the ordering rule must flag this')
        $fakeNa = Join-Path $sb 'fake-nonascii.ps1'
        [System.IO.File]::WriteAllBytes($fakeNa, [System.Text.Encoding]::UTF8.GetBytes("# " + [char]0x26D4))
        $nb = [System.IO.File]::ReadAllBytes($fakeNa); $na2 = 0
        foreach ($b in $nb) { if ($b -gt 127) { $na2++ } }
        $hb2 = ($nb.Length -ge 3 -and $nb[0] -eq 0xEF -and $nb[1] -eq 0xBB -and $nb[2] -eq 0xBF)
        T '18 KNOWN-BAD nonascii-no-bom-FLAGGED' (($na2 -gt 0) -and (-not $hb2)) ('nonAscii=' + $na2 + ' bom=' + $hb2)
    } finally {
        Remove-Item $sb -Recurse -Force -ErrorAction SilentlyContinue
    }
    Write-Host ('SELFTEST d2u26 lock-protocol/encoding: pass=' + $script:st + ' fail=' + $script:sf + ' sandbox=' + $sb)
    if ($scOnly -eq $true) {
        # Read-only editor probe: reported, never acted on (no lock, no editor_focus/play/stop).
        $pj = & unity command editor_status --format json --no-pager 2>&1 | Out-String
        $pmNow = 'unknown'
        try { $pmNow = [string](($pj | ConvertFrom-Json).data.result).playMode } catch { $pmNow = 'unknown' }
        Write-Host ('SELFCHECK-EDITOR-PROBE readOnly=1 playMode=' + $pmNow + ' (diagnostic only: no lock taken, no editor action)')
        Write-Host ('SELFCHECK-ONLY-END locks before=[' + ($lkBefore -join ',') + '] after=[' + (@((Test-Path $lock), (Test-Path $legacy)) -join ',') + '] dir=' + $testDir + ' -- nothing was acquired or released')
    } else {
        Write-Host ('SELFTEST NOTE: real locks in ' + $testDir + ' before=[' + ($lkBefore -join ',') + '] after=[' + (@((Test-Path $lock), (Test-Path $legacy)) -join ',') + '] (client/ is NOT where this runner locks)')
    }
    exit $(if ($script:sf -eq 0) { 0 } else { 1 })
}

$lockOk = $false
for ($i = 0; $i -lt 30; $i++) {
    $script:busyOwner = $null
    if ((Test-TakeLock $lock $legacy $me $PID) -eq $true) { $lockOk = $true; break }
    if ($null -eq $script:busyOwner) { continue }        # lost a write race: re-judge immediately
    Say ('LOCK-WAIT try=' + ($i + 1) + '/30 -> offline work, retry in 3 min')
    Start-Sleep -Seconds 180
}
if (-not $lockOk) {
    Say ('ABORT lock held by ' + $script:busyOwner + ' -- the editor was never touched and the play-log was NOT written')
    Say 'END'
    exit 3
}
Say ('LOCK-ACQUIRED ' + $me + ' pid=' + $PID)

try {
# Accounting line (skill 4.4): column 4 = WHY this chain must be a live Play session.
# Written only AFTER the lock is ours (a play-log line without a Play session makes the ledger lie).
$logLine = (Get-Date).ToString('yyyy-MM-dd HH:mm') + "`t" + $me + "`tU26-U36-draw-order`t" + $Why
# ledger = UTF-8 WITHOUT BOM append (a BOM on the FIRST line of a tsv adds a phantom column)
[System.IO.File]::AppendAllText($playLog, $logLine + "`r`n", (New-Object System.Text.UTF8Encoding($false)))
Say ('PLAYLOG-APPENDED ' + $playLog)

Unity-Cmd @('editor_focus') -Quiet | Out-Null
Stop-Editor-Safe 'pre-play-reset' | Out-Null   # guarded: we hold the lock here, so it cannot interrupt others
# RULE 4 -- the TAKER's duty (team, 13:5x): a lock we just took may be a STALE one whose owner left Play
# running.  Before opening OUR session we must stop the editor and CONFIRM `stopped`, otherwise the
# orphan's session and ours fight over the same editor.  If it cannot be confirmed we do NOT start: the
# lock stays with us (rule 42), the alarm + lead notification are already emitted above.
$preCheck = Confirm-StoppedOrWithhold 'pre-play'
if ($preCheck -ne 'stopped') {
    Say ('ABORT-TAKEOVER-NOT-CLEAN pre-play status=' + $preCheck + ' -- our session was NOT started; the lock is left HELD on purpose (see ORPHAN-PLAY / ' + (Split-Path $alertFile -Leaf) + ')')
    Say 'END'
    exit 5
}
Start-Sleep -Seconds 2
Unity-Cmd @('clear_console') -Quiet | Out-Null
Init-Offset
Say ('LOG-OFFSET ' + $script:offset)
Unity-Cmd @('editor_play') -Quiet | Out-Null
Start-Sleep -Seconds 7

$cfgOk = $false
for ($i = 0; $i -lt 8; $i++) {
    $r = Run-Step 'D2U26.Api.Cfg' ''
    if ($r -match 'CFG-OK') { $cfgOk = $true; break }
    Start-Sleep -Seconds 3
}
Say ('CFG-OK ' + $cfgOk)
$ping = Run-Step 'D2U26.Api.Ping' ''
Say ('PING ' + $ping)

$spec = $Tag + '|g66|' + ($tsv -replace '\\', '/') + '|' + ($done -replace '\\', '/') + '|' + ($cropDir -replace '\\', '/')
$raw = Unity-Cmd @('run_script', '--file', $cs, '--entry', 'D2U26.Tour.Install', '--args', ('[\"' + $spec + '\"]'))
Say ('INSTALL ' + ($raw -replace "`r?`n", ' '))

$t0 = Get-Date
while (((Get-Date) - $t0).TotalSeconds -lt 420) {
    Read-New
    if (Test-Path $done) { break }
    Start-Sleep -Milliseconds 400
}
Read-New

if (Test-Path $done) { Say ('DONE ' + (Get-Content $done -Raw).Trim()) }
else { Say 'DONE-MISSING (the driver never signalled completion)' }

foreach ($l in $script:lines) { Say ('EVIDENCE ' + $l) }

$consoleJson = Unity-Cmd @('console_status') -Quiet
Say ('CONSOLE-STATUS ' + ($consoleJson -replace "`r?`n", ' '))
# team protocol: read the captured console with `console --tail <n>` (console_status only gives counters)
$consoleTail = Unity-Cmd @('console', '--tail', '200') -Quiet
Say ('CONSOLE-TAIL ' + ($consoleTail -replace "`r?`n", ' '))

Stop-Editor-Safe 'post-run'            # if the lock was taken over meanwhile, the editor is left alone
Say 'STOPPED'

if (Test-Path $tsv) {
    $fi = Get-Item $tsv
    # NOTE (team, 13:5x): `Measure-Object -Line` SWITCHES OFF blank lines, so its count is a DIFFERENT
    # quantity -- the most dangerous of the three line-counting algorithms (it looks like a line count and
    # yields a stable but wrong green/red).  Therefore this field is named `rows=`, its algorithm is stated
    # inline, and the unified `lines=` label is reserved for ReadAllLines().Count (the RUNNER-FINGERPRINT line).
    $rows = (Get-Content $tsv | Measure-Object -Line).Lines
    # The algorithm note stays HERE (a comment) and not inside the log string: meta text in a log line
# breaks naive parsers and made assertion 46 flag its own explanation (measured).
Say ('TSV-LANDED ' + $tsv + ' bytes=' + $fi.Length + ' rows=' + $rows)
} else {
    Say ('TSV-MISSING ' + $tsv + ' (the driver never wrote it)')
}

[System.IO.File]::WriteAllLines($logCopy, [string[]]$script:lines, (New-Object System.Text.UTF8Encoding($false)))
Say ('LOGWRITE ' + $logCopy + ' evidenceLines=' + $script:lines.Count + ' (count of collected [D2U26] lines -- NOT a file line count)')

# ---- offline verdict: the sheet script owns every number ---------------------
if ((Test-Path $tsv) -and (Test-Path $sheetPy)) {
    $py = & python $sheetPy $tsv $cropDir $sheet $index 2>&1 | Out-String
    Say ('SHEET ' + ($py -replace "`r?`n", ' | '))
    Say ('SHEET-OUT ' + $sheet + ' / ' + $index)
} else {
    Say ('SHEET-SKIPPED tsv=' + (Test-Path $tsv) + ' py=' + (Test-Path $sheetPy))
}

Say ('SUMMARY tag=' + $Tag + ' cfgOk=' + $cfgOk + ' done=' + (Test-Path $done) + ' tsv=' + (Test-Path $tsv) + ' cli=' + $script:cli)
Say 'END'
} finally {
    # Hand-over = THREE steps, and they must hold on EVERY exit path (team rule 42, 13:4x):
    #   (1) TRIGGER: push the system toward the wanted state -- `editor_stop` (guarded: only while the
    #       lock is ours; otherwise it raises ORPHAN-PLAY and leaves their session alone).
    #   (2) CONFIRM: read `playMode=stopped` back, bounded (rule 1: the stop CLI returns before Play
    #       has really exited -- measured 13:13: "lock ABSENT while playMode=playing").
    #   (3) HAND OVER: release the two lock names.
    # Why (1) belongs HERE and not only on the happy path: measured failure mode (d2u27/u27v5) -- a
    # shutdown path that only WAITED (5 x 1s) and never wired the stop released its lock while Play
    # stayed up for 6.5 minutes.  A wait-guard with no pusher merely postpones a failure, while the log
    # still reads like "I tried".  Our happy path did call the stop, but a mid-chain crash did not.
    Stop-Editor-Safe 'finally' | Out-Null
    # (2) CONFIRM, and (3) release ONLY if the session really ended (rule 42): releasing while Play is
    #     still alive is what created the ownerless session at 13:42.  On 'withheld' the lock is kept on
    #     purpose and the lead is notified through the shared alert file.
    $handover = Confirm-StoppedOrWithhold 'finally'
    if ($handover -eq 'stopped') {
        Release-Lock
    } else {
        Say ('LOCK-KEPT ' + (Split-Path $lock -Leaf) + ' status=' + $handover + ' -- hand-over withheld on purpose (see ORPHAN-PLAY); a stale taker must stop the editor and confirm stopped first (rule 4)')
    }
}
