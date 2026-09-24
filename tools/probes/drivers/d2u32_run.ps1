# =============================================================================
# d2u32_run.ps1 -- slice u32-close: ONE Play session that walks the whole
# waypoint chain (walk to the pad -> panel opens -> pick a destination ->
# land in the target area) and collects the reads the offline hosts cannot
# reach: areaId (live + the value the next Save() would collect), the
# activated-waypoint list, the chunk miss/black-window reads, and a
# SCREENSHOT-TO-GRID manifest.
#
# REUSE NOTE: this is a minimal edit of tools/probes/drivers/travelblack_run.ps1
# (same editor gate / same Play-lock protocol / same recompile poll / same ping
# gate / same freeze step) -- only the owner, the tag, the driver file, the
# entry points, the artifact names and the keep-regex changed. Do not re-invent.
#
#   powershell -NoProfile -ExecutionPolicy Bypass -File d2u32_run.ps1 [-Tag u32]
#
# Produces (all under <repo>/.ai-tmp/):
#   screenshots/u32_<tag>_{1_panel,2_landed,3_p05,4_p20}.png
#   screenshots/u32_<tag>.log               (per-frame rows)
#   screenshots/u32_shots_<tag>.tsv         (screenshot -> grid manifest)
#   screenshots/u32_evidence_<tag>.txt      (frozen runtime rows)
#   test/u32_steps_<tag>.txt                (runner trace)
#   test/u32_done_<tag>.txt
#
# ASCII only (PS 5.1 parses a BOM-less non-ASCII .ps1 as ANSI).
# =============================================================================
param(
    [string]$Tag = 'u32',
    # lock owner written into play-running.lock AND into play-log.tsv column 2.
    # team lead (2026-09-24) names the re-take session u32re-take; the first session is u32-close.
    [string]$Owner = 'u32-close',
    # READ-ONLY entry (team lead 2026-09-24, from jitter's 13:26 accident): run the byte + static
    # self-checks and ONE editor probe, then exit WITHOUT taking the lock. Never "start the real
    # runner and kill it to read the self-check" -- if the lock happens to be free it will TAKE it and
    # die before `finally` (zombie lock + an invalid ledger row). Usage: -SelfCheckOnly -Tag u32sc
    [switch]$SelfCheckOnly,
    [string]$Why = 'u32-close: the offline hosts now cover the waypoint chain mid-section (activated-list restore/snapshot/new-run reset, click-anchor->walk->panel opens, pick-destination->area switch with 4 rejects + 1 pass). What they CANNOT reach is the real thing: the live areaId after landing, the activated list as the panel really shows it, the per-frame chunk miss/black-window read and the screenshot-to-grid binding. This session walks one real chain (activation via a real ExitEntered area switch, NOT by poking the static set), samples every frame from the landing frame on and takes 4 screenshots with a grid manifest.'
)

$ErrorActionPreference = 'Continue'

$root    = 'c:/Work/Server/f-v2/clover-project-diablo2'
$proj    = Join-Path $root 'client'
$cs      = Join-Path $root 'tools\probes\drivers\d2u32_drive.cs'
$test    = Join-Path $root '.ai-tmp\test'
$shots   = Join-Path $root '.ai-tmp\screenshots'
# the driver writes its done file into ITS OutDir (=$shots), not into $test: keep them in sync
# (first session burned a 420 s wait because of exactly this mismatch).
$done    = Join-Path $shots ('u32_done_' + $Tag + '.txt')
$trace   = Join-Path $test ('u32_steps_' + $Tag + '.txt')
$frozen  = Join-Path $shots ('u32_evidence_' + $Tag + '.txt')
$logPath = Join-Path $proj 'Logs\Editor.log'
$playLog = Join-Path $test 'play-log.tsv'
# play lock: team-wide name is play-running.lock (per team lead, 2026-09-24). The older
# travelblack_run.ps1 uses play.lock, so BOTH are honoured while waiting (taking only mine).
$lock      = Join-Path $test 'play-running.lock'
$legacyLock = Join-Path $test 'play.lock'
$heart   = Join-Path $test 'heartbeat-u32.txt'
$me      = $Owner
# both names are MINE (first session / re-take) -> a stale lock with either owner is reusable,
# and Release-Lock must recognise either one (otherwise we would refuse to release our own lock).
$myOwners = '^\s*(u32-close|u32re-take)\b'

# [U32]     = this driver's rows
# MapView   = the chunk rebuild rows the production code writes
# Waypoint  = production [U32]-adjacent rows by AppWaypoint (Chinese tags kept out: ASCII regex only)
$keepRe = '\[U32\]|MapView|WaypointTravelRequest|AreaChanged|App\]'

# ---- README 5.2 #30 (team lead 2026-09-24): a self-check entry must NEVER write a path that a
# NORMAL run also writes. Team case (zorder): its -SelfTest WriteAllText($trace)'d + appended to the
# SAME name => the 11:58 batch's step trace for u26 was overwritten and is unrecoverable.
# OUR shape: Say() writes BOTH $trace and $heart, and the startup line below RESETS $trace. So with
# the switch set we REDIRECT all three BEFORE anything writes. (Convention "-Tag u32sc" is NOT a
# fix: once somebody forgets it, the last real run's trace is gone.) Verified by .ai-tmp hash diff.
if ($SelfCheckOnly) {
    $trace  = Join-Path $test  ('u32_selftest_steps_' + $Tag + '.txt')
    $heart  = Join-Path $test  ('u32_selftest_heartbeat.txt')
    $frozen = Join-Path $shots ('u32_selftest_evidence_' + $Tag + '.txt')
}

# lock protocol (v2.1) -- single source of truth, shared with d2u32_lock_selftest.ps1
$proto = Join-Path $PSScriptRoot 'd2u32_lockproto.ps1'
if (-not (Test-Path $proto)) { Write-Host ('FATAL missing ' + $proto); exit 1 }
. $proto

$script:offset = 0
$script:lines = New-Object System.Collections.ArrayList
$script:cli = 0

function Say([string]$s) {
    $stamp = (Get-Date).ToString('HH:mm:ss.fff')
    Write-Host ($stamp + ' ' + $s)
    Add-Content -Path $trace -Value ($stamp + ' ' + $s) -Encoding UTF8
    Set-Content -Path $heart -Value ($stamp + ' ' + $s) -Encoding UTF8
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

# ---- play lock helpers (protocol v2, team lead 2026-09-24 -- the ONLY wording allowed) ---------
# lock content is exactly three fields: "<owner> <ISO8601> <PID>".
# stale  = PID dead OR age >= 12 min  (12 min is the ONLY threshold team-wide; no local variants).
# Forbidden: editor_play / recompile / editor_stop while the lock is not ours.

function Lock-Text() {
    if (-not (Test-Path $lock)) { return '' }
    $t = ''
    try { $t = (Get-Content $lock -Raw -Encoding UTF8) } catch { $t = '' }
    # BOM strip (u52play 2026-09-24): a surviving BOM would make our OWN lock look foreign.
    return ((Remove-ZeroWidth $t).Trim() -replace "`r?`n", ' ')
}

# v2.2: during the transition we hold the real lock AND a same-content mirror in the legacy name
# (play.lock), so historical runners that only read play.lock are blocked. Release deletes BOTH,
# and only the ones whose content is ours.
function Release-Lock() {
    $released = @()
    foreach ($f in @($lock, $legacyLock)) {
        if (-not (Test-Path $f)) { continue }
        $body = ''
        try { $body = ((Remove-ZeroWidth (Get-Content $f -Raw -Encoding UTF8)).Trim() -replace "`r?`n", ' ') } catch { $body = '' }
        if ($body -match $myOwners) {
            Remove-Item $f -Force -ErrorAction SilentlyContinue
            $released += (Split-Path $f -Leaf)
        } else {
            Say ('LOCK-RELEASE REFUSED (' + (Split-Path $f -Leaf) + ' owned by ' + $body + ') -> left alone')
        }
    }
    if ($released.Count -eq 0) { Say 'LOCK-RELEASE skip (nothing of mine)' }
    else { Say ('LOCK-RELEASED (mine): ' + ($released -join ',')) }
}

# protocol v2 rule 4: never stop the editor when the lock is not ours; if it was taken over,
# clear only our local state and mark the readings suspect.
# README 5.2 #42 (team lead 2026-09-24): "locks free" != "editor free". closefix's u27v5 session:
# the driver ended and BOTH locks were released at 13:42:11, yet the editor was STILL in Play at
# 13:43 (locks ABSENT + playMode=playing) -> whoever takes the lock next walks into someone's Play.
# So: read playMode back (bounded) BEFORE releasing, and never hand over a playing editor silently.
function Get-PlayMode() {
    $st = Unity-Cmd @('editor_status') -Quiet
    $compiling = $true; $play = '?'; $readable = $false
    try {
        $sj = $st | ConvertFrom-Json
        $inner = $sj.data.result
        if ($inner -is [string]) { $inner = $inner | ConvertFrom-Json }
        if ($null -ne $inner) {
            $compiling = [bool]$inner.compiling
            $play = [string]$inner.playMode
            $readable = ($play -ne '' -and $null -ne $play)
        }
    } catch { }
    return [pscustomobject]@{ Compiling = $compiling; Play = $play; Readable = $readable }
}

function Stop-IfMine([string]$why) {
    $body = Lock-Text
    if ($body -match $myOwners) {
        Unity-Cmd @('editor_stop') -Quiet | Out-Null
        Say ('STOPPED (' + $why + ') -> reading playMode back before releasing the lock')
        $pm = Get-PlayMode
        $ended = $false
        for ($k = 0; $k -lt 12; $k++) {
            if ($pm.Readable -and (-not $pm.Compiling) -and ($pm.Play -eq 'stopped')) { $ended = $true; break }
            Say ('STOP-READBACK try=' + ($k + 1) + ' playMode=' + $pm.Play + ' compiling=' + $pm.Compiling + ' readable=' + $pm.Readable + ' -> Start-Sleep 5')
            Start-Sleep -Seconds 5
            $pm = Get-PlayMode
        }
        if ($ended) { Say 'STOP-READBACK playMode=stopped CONFIRMED -> safe to release the lock' }
        else {
            # ADJUDICATION 2026-09-24 (team lead, which REVOKED its own earlier "release + alert"):
            # when a play session will not stop, DO NOT release the lock. Holding it keeps the
            # ownership of an unfinished session, which is what makes the next taker legitimate; and
            # since every correct gate refuses to start while playMode=playing, HOLDING COSTS NO
            # EXTRA WALL-CLOCK TIME. Bounded by the existing 12-minute stale rule (a dead PID frees it).
            # The old shape left "two free locks + playMode=playing" (an ownerless session) and it took
            # the team lead 6.5 minutes to clean up by hand -- that is why this was revoked.
            Say ('ORPHAN-PLAY (normal shutdown): editor_stop was issued but playMode never read back as stopped (last=' + $pm.Play + ' owner="' + $body + '")')
            Say ('LOCK-RELEASE WITHHELD / LOCK-HELD-ON-PURPOSE: keeping ' + $lock + ' and ' + $legacyLock + ' -- the unfinished session keeps an owner (12min stale rule still frees it)')
            $opf = Join-Path $test 'ORPHAN-PLAY-alert.tsv'
            $opLine = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss') + "`t" + $me + "`t" + $Tag + "`tplayMode=" + $pm.Play + "`towner=" + $body + "`t" + $why
            Add-Content -Path $opf -Encoding UTF8 -Value $opLine -ErrorAction SilentlyContinue
            Write-Host ('*** ORPHAN-PLAY ALERT -- NOTIFY TEAM LEAD: ' + $opf + ' | tag=' + $Tag + ' playMode=' + $pm.Play + ' | lock NOT released ***')
            $script:withholdRelease = $true      # caller must skip Release-Lock
        }
        return $true
    }
    $pm2 = Get-PlayMode
    if ($pm2.Readable -and ($pm2.Play -ne 'stopped')) {
        Say ('ORPHAN-PLAY (' + $why + '): lock is not mine (now "' + $body + '") and playMode=' + $pm2.Play + ' -> NOT issuing editor_stop, NOT touching their lock (rule 3)')
    }
    Say ('SKIP editor_stop (' + $why + '): lock is not mine (now "' + $body + '") -> local state only; readings = suspect (lock stolen / intervened)')
    return $false
}

function Clip([string]$s, [int]$n) {
    if ($null -eq $s) { return '' }
    $one = $s -replace "`r?`n", ' '
    if ($one.Length -le $n) { return $one }
    return $one.Substring(0, $n)
}

function Unity-Cmd([string[]]$argv, [switch]$Quiet) {
    $script:cli = $script:cli + 1
    $raw2 = & unity command @argv --format json --no-pager 2>&1 | Out-String
    if (-not $Quiet) { Say ('CLI ' + ($argv -join ' ') + ' -> ' + $raw2.Length + ' bytes') }
    return $raw2
}

# =============================================================================
# main
# =============================================================================
foreach ($d in @($shots, $test)) { if (-not (Test-Path $d)) { New-Item -ItemType Directory -Path $d | Out-Null } }
# ---- PS 5.1 encoding self-check (team lead 2026-09-24) -------------------------------
# PS 5.1 parses a BOM-less non-ASCII .ps1 as ANSI -> garbled quotes / fake "PARSE-OK".
# Rule: every *.ps1 must be pure ASCII or UTF-8 **with BOM**. The verdict is LOGGED, and a
# violation aborts BEFORE the editor is touched (never rely on "I remember writing ASCII").
$selfPath = $PSCommandPath
if ([string]::IsNullOrEmpty($selfPath)) { $selfPath = $MyInvocation.MyCommand.Path }
$selfBytes = [System.IO.File]::ReadAllBytes($selfPath)
$nonAscii = 0
foreach ($b in $selfBytes) { if ($b -gt 127) { $nonAscii = $nonAscii + 1 } }
$hasBom = ($selfBytes.Length -ge 3 -and $selfBytes[0] -eq 0xEF -and $selfBytes[1] -eq 0xBB -and $selfBytes[2] -eq 0xBF)
$encOk = ($nonAscii -eq 0) -or $hasBom
# README 5.2 #29 (hard taboo, team lead 2026-09-24): NEVER split a `Say (` over several lines --
# PS 5.1 suddenly reports `Missing closing ')'` (shopart 3x / zorder 12 errors / u44impl 4 sites /
# u52play 2x). Precompute into a variable, then hand the variable to Say. (This runner had 2 sites.)
$encLine = 'ENCODING-SELFCHECK file=' + (Split-Path $selfPath -Leaf) + ' bytes=' + $selfBytes.Length + ' nonAsciiBytes=' + $nonAscii + ' hasBom=' + $hasBom + ' verdict=' + $(if ($encOk) { 'OK (pure ASCII or UTF-8+BOM)' } else { 'FAIL' })
Say $encLine
if (-not $encOk) {
    Say 'ABORT ENCODING-SELFCHECK: this .ps1 is non-ASCII without BOM -> refusing to touch the editor'
    exit 1
}

Set-Content -Path $trace -Value ('# u32-close steps tag=' + $Tag) -Encoding UTF8
$sessionStart = Get-Date
Set-Location -LiteralPath $proj
Say ('BEGIN tag=' + $Tag + ' cwd=' + (Get-Location).Path)

# ---- RUNNER-FINGERPRINT (team lead 2026-09-24, adopted from jitter) --------------------------
# The trace carries its OWN identity so that anyone quoting line numbers later can write
# "authoritative for THIS trace" and re-compute it. Reason (u3bverify measured it): a HAND-PASTED
# fingerprint goes stale within minutes (its 2.5-min-old copy mismatched by 8 line numbers, and
# `L155`/`L471` were both `}`) -> a reviewer checking current code against old line numbers gets a
# FALSE RED like "`:438` has no lock write". So: print it, never paste it.
$fpBytes = [System.IO.File]::ReadAllBytes($selfPath)
$fpSha = [System.Security.Cryptography.SHA256]::Create()
$fpHash = ([BitConverter]::ToString($fpSha.ComputeHash($fpBytes)) -replace '-', '').ToLower()
$fpLines = ([System.IO.File]::ReadAllLines($selfPath)).Length
# same rule as above (README 5.2 #29): precompute, never a multi-line `Say (`
$fpLine = 'RUNNER-FINGERPRINT sha256_16=' + $fpHash.Substring(0, 16) + ' bytes=' + $fpBytes.Length + ' lines=' + $fpLines + ' mtime=' + (Get-Item $selfPath).LastWriteTime.ToString('yyyy-MM-ddTHH:mm:ss') + ' (authoritative for THIS trace; verified by re-computing)'
Say $fpLine

# ---- -SelfCheckOnly: the read-only entry (team lead 2026-09-24) --------------------------------
# WHY: jitter's real runner was started and Stop-Process'd after 9 s; the lock happened to be FREE,
# so it took the lock (+ mirror) and wrote a play-log row, then died before `finally` => a zombie
# lock + one invalid ledger row (would have blocked anyone waiting). "Remember not to kill it" is
# NOT a fix -- it bets on the lock happening to be busy. So the read-only path is an explicit switch,
# and it prints the lock state BEFORE and AFTER, so "we touched nothing" is evidence, not a promise.
# It sits BEFORE the clear/cold-start block on purpose: no file is cleared, no ledger row is written.
if ($SelfCheckOnly) {
    $lockBefore = @((Test-Path $lock), (Test-Path $legacyLock))
    Say ('SELFCHECK-ONLY-BEGIN locks-before=[' + ($lockBefore -join ',') + '] (tag=' + $Tag + ')')
    $child = Join-Path $PSScriptRoot 'd2u32_lock_selftest.ps1'
    $staticRc = 0
    if (Test-Path $child) {
        $out = & powershell -NoProfile -ExecutionPolicy Bypass -File $child 2>&1
        $rc = $LASTEXITCODE
        $staticRc = $rc
        $tail = @($out | Where-Object { $_ -match 'PASS, FAIL' })
        $sum = '(no summary line)'
        if ($tail.Count -ge 1) { $sum = ([string]$tail[-1]).Trim() }
        Say ('SELFCHECK-ONLY-STATIC ' + $sum + ' exit=' + $rc)
    } else {
        Say ('SELFCHECK-ONLY-STATIC SKIPPED missing ' + $child)
    }
    # ONE probe, deliberately NOT the 30x retry loop: this path must stay cheap and side-effect free.
    $st = Unity-Cmd @('editor_status') -Quiet
    $compiling = $true; $play = '?'; $readable = $false
    try {
        $sj = $st | ConvertFrom-Json
        $inner = $sj.data.result
        if ($inner -is [string]) { $inner = $inner | ConvertFrom-Json }
        if ($null -ne $inner) {
            $compiling = [bool]$inner.compiling
            $play = [string]$inner.playMode
            $readable = ($play -ne '' -and $null -ne $play)
        }
    } catch { }
    Say ('SELFCHECK-ONLY-PROBE playMode=' + $play + ' compiling=' + $compiling + ' readable=' + $readable + ' (read-only single probe)')
    $lockAfter = @((Test-Path $lock), (Test-Path $legacyLock))
    $same = (($lockBefore -join ',') -eq ($lockAfter -join ','))
    # README 5.2 #42 rule 3: if we merely OBSERVE a playing editor we still say so out loud -- and
    # explicitly record that we did NOT issue editor_stop (the lock may belong to someone else).
    if ($readable -and ($play -ne 'stopped')) {
        Say ('ORPHAN-PLAY selfcheck-probe playMode=' + $play + ' locks=[' + ($lockBefore -join ',') + '] -> observation only, NO editor_stop was issued (rule 3)')
    }
    Say ('SELFCHECK-ONLY-END locks-after=[' + ($lockAfter -join ',') + '] unchanged=' + $same + ' no-lock-taken=1 no-ledger-row=1 no-play=1')
    if (-not $same) { exit 1 }
    # team lead's wiring rule (2026-09-24): a FAIL must reach the exit code, INFO is only a log line.
    # Without this the read-only entry printed "46/47 PASS" and still exited 0 (a green-looking signal).
    if ($staticRc -ne 0) {
        Write-Host ('*** SELFCHECK-ONLY-STATIC FAILED (exit=' + $staticRc + ') -> read-only entry reports FAILURE ***')
        exit 1
    }
    exit 0
}

foreach ($f in @($done, $frozen)) { if (Test-Path $f) { Remove-Item $f -Force; Say ('CLEARED ' + (Split-Path $f -Leaf)) } }

# ---- editor gate + play lock ---------------------------------------------------
$idle = $false
$bridgeDown = 0                  # probes that returned NO usable reading (bridge unreachable)
for ($i = 0; $i -lt 30; $i++) {
    $st = Unity-Cmd @('editor_status') -Quiet
    # fail-closed defaults (team lead 2026-09-24): a missing playMode must NEVER look like "stopped".
    $compiling = $true; $play = '?'; $readable = $false
    try {
        $sj = $st | ConvertFrom-Json
        $inner = $sj.data.result
        if ($inner -is [string]) { $inner = $inner | ConvertFrom-Json }
        if ($null -ne $inner) {
            $compiling = [bool]$inner.compiling
            $play = [string]$inner.playMode
            $readable = ($play -ne '' -and $play -ne $null)
        }
    } catch { }
    if (-not $readable) { $bridgeDown = $bridgeDown + 1 }
    if ((-not $compiling) -and ($play -eq 'stopped')) { $idle = $true; Say ('EDITOR-IDLE compiling=false playMode=stopped (try ' + ($i + 1) + ')'); break }
    # team lead 2026-09-24 (from jitter): label compiling=true so nobody later mistakes it for a gate.
    # shopart 13:22 proved compiling=True + compilationFailed=False is a TRANSIENT NORMAL state (the
    # session ran to completion) -> treating it as a gate would abort a perfectly good session.
    Say ('EDITOR-BUSY compiling=' + $compiling + ' (compiling=true is transient-normal, NOT a gate) playMode=' + $play + ' readable=' + $readable + ' -> Start-Sleep 10')
    Start-Sleep -Seconds 10
}
if (-not $idle) {
    # team lead adjudication A (2026-09-24): form two (ABORT / fail-closed, no cold start) is APPROVED
    # for this runner, on one condition -- say WHICH of the two situations it was, so whoever reads the
    # log knows whether an editor exists at all. `editor process absent` != `someone is in Play`.
    if ($bridgeDown -ge 30) {
        Say ('LOCK-GATE-BRIDGE-DOWN probed=' + $bridgeDown + ' editor-bridge-down (editor process absent; cold start is out of scope) -> ABORT')
    } else {
        Say ('ABORT editor never went idle (readable probes=' + (30 - $bridgeDown) + '/30) -> editor is in Play or compiling -> NOT taking the lock')
    }
    # README 5.2 #42 rule 3: walking away while the editor PLAYS and neither lock is ours is an
    # ORPHAN PLAY -- say it out loud (lock bodies + playMode) instead of a silent exit, or the next
    # taker believes it inherits a clean session. (This exact state was live at 13:43 today.)
    if ($readable -and ($play -ne 'stopped')) {
        $o1 = 'ABSENT'
        if (Test-Path $lock) { try { $o1 = ((Remove-ZeroWidth (Get-Content $lock -Raw -Encoding UTF8)).Trim() -replace "`r?`n", ' ') } catch { $o1 = 'UNREADABLE' } }
        $o2 = 'ABSENT'
        if (Test-Path $legacyLock) { try { $o2 = ((Remove-ZeroWidth (Get-Content $legacyLock -Raw -Encoding UTF8)).Trim() -replace "`r?`n", ' ') } catch { $o2 = 'UNREADABLE' } }
        if ((-not ($o1 -match $myOwners)) -and (-not ($o2 -match $myOwners))) {
            Say ('ORPHAN-PLAY gate-abort playMode=' + $play + ' compiling=' + $compiling + ' play-running.lock="' + $o1 + '" play.lock="' + $o2 + '" -> NOT issuing editor_stop (rule 3), NOT taking the lock')
        }
    }
    exit 1
}

$script:takeoverNote = ''
$script:staleLegacy = ''
for ($i = 0; $i -lt 24; $i++) {
    $fresh = $null          # first FRESH foreign lock -> somebody is inside -> yield
    $staleList = @()        # foreign locks that are stale -> may be taken over
    foreach ($f in @($lock, $legacyLock)) {
        if (-not (Test-Path $f)) { continue }
        $raw = ''
        try { $raw = (Get-Content $f -Raw -Encoding UTF8) } catch { $raw = '' }
        $raw = ($raw.Trim() -replace "`r?`n", ' ')
        if ($raw -match $myOwners) { continue }     # mine (left over from an aborted run) -> reuse
        # protocol v2.1 (single source of truth = d2u32_lockproto.ps1, dot-sourced above):
        # "<owner> <ISO8601> <PID>"; stale = PID dead OR age >= 12m;
        # *** missing PID field => UNKNOWN, NOT dead => a fresh 2-field lock MUST be yielded to ***
        $lf = Parse-LockFields $raw
        $age = ((Get-Date) - (Get-Item $f).LastWriteTime).TotalMinutes
        # real liveness lives in the proto (Test-PidAlive) -> the default path IS the tested path
        $stale = Test-LockStale -raw $raw -ageMinutes $age
        $rec = [pscustomobject]@{ File = $f; Text = (Format-LockFields $lf $age $stale) }
        if ($stale) { $staleList += $rec }
        elseif ($null -eq $fresh) { $fresh = $rec }
    }
    # BOTH files are inspected (team lead 2026-09-24): two lock names once let two sessions each
    # believe they were exclusive. Any FRESH lock in EITHER file => yield; only when none is fresh
    # may the stale ones be taken over. (The old loop broke on the first existing file -> if the
    # canonical lock was stale while play.lock was fresh, it would have entered anyway = false-exclusive.)
    if ($null -ne $fresh) {
        $busy = $fresh.File
        Say ('LOCK-BUSY ' + (Split-Path $fresh.File -Leaf) + ' ' + $fresh.Text + ' -> Start-Sleep 30 (try ' + ($i + 1) + ')')
    } else {
        $busy = $null
        foreach ($s in $staleList) {
            $script:takeoverNote = 'TAKEOVER stale lock (' + (Split-Path $s.File -Leaf) + '): ' + $s.Text
            Say $script:takeoverNote
            if ((Split-Path $s.File -Leaf) -eq (Split-Path $legacyLock -Leaf)) { $script:staleLegacy = $s.File }
        }
    }
    if ($null -eq $busy) { break }
    Start-Sleep -Seconds 30
}
# FAIL-FAST (team lead, 2026-09-24): if somebody else still owns a fresh lock after the wait,
# ABORT here -- NEVER overwrite their lock and NEVER touch the editor (no editor_stop / recompile /
# editor_play). The editor part of this script is entirely after LOCK-TAKEN, so exiting here is safe.
if ($null -ne $busy) {
    Say ('ABORT lock held by ' + (Split-Path $busy -Leaf) + ' -> NOT touching the editor')
    exit 1
}
# -Encoding ASCII on purpose (u52play 2026-09-24): PS 5.1's UTF8 writes a BOM, which then makes our
# own lock unreadable as "mine". owner / ISO8601 / PID are pure ASCII by construction.
Set-Content -Path $lock -Value ($me + ' ' + (Get-Date).ToString('o') + ' ' + $PID) -Encoding ASCII
# WRITE-THEN-READ-BACK (team lead, 2026-09-24 amended rule): two sessions can see an empty lock
# in the same instant, so wait ~1 s and read it again -- if it is no longer mine, back off.
# We never delete the file here (it belongs to the winner); we just exit before touching the editor.
Start-Sleep -Seconds 1
$verify = Lock-Text          # BOM-stripped (see Lock-Text): a raw read could hide our own name
if ($verify -notmatch $myOwners) {
    Say ('LOCK-LOST after write (now held by ' + $verify + ') -> backing off, NOT touching the editor')
    exit 1
}
Say ('LOCK-TAKEN ' + $lock + ' (content="' + (Lock-Text) + '")')

# ADJUDICATION 2026-09-24 (team lead): taking over a stale lock carries a NEW obligation -- stop
# whatever the previous owner left running BEFORE we start our own session, and confirm it. The 13:42
# orphan play was inherited exactly this way (two free locks + playMode=playing), so the takeover path
# is now a clean-up path, not just a bookkeeping step. Window widened to 15x2s (team lead: the real
# bug was "nobody called editor_stop", not "it would not stop"; jitter measured a single call exits
# play mode immediately). If it still will not stop: keep the lock, alert, and do NOT play.
if ($script:takeoverNote.Length -gt 0) {
    # REUSE Stop-IfMine instead of a second editor_stop call site (u52play's layout rule: editor_stop
    # must exist exactly ONCE and only inside that guard -- a second literal turned that assertion red,
    # which is the correct behaviour of an AST-based count). Reusing also inherits the readback, the
    # orphan-play withhold and the alert for free.
    Say ('TAKEOVER-CLEANUP taking over (' + $script:takeoverNote + ') -> stopping the inherited session before ours')
    $tookOver = Stop-IfMine 'takeover cleanup'
    if ($script:withholdRelease) {
        Say 'TAKEOVER-ORPHAN-PLAY inherited session would not stop -> NOT starting our own session; lock intentionally KEPT (unfinished session keeps an owner)'
        exit 1
    }
    if ($tookOver) { Say 'TAKEOVER-ORPHAN-CLEARED inherited session stopped and confirmed before our own' }
}
if ($script:takeoverNote -ne '') { Say ('NOTE ' + $script:takeoverNote + ' -- recorded for the report') }

# v2.2 transition mirror: same content as the real lock, in the legacy name. Purpose = block the
# 17 historical one-off runners that only read play.lock (registered "do not run again").
$mirror = Lock-Text
Set-Content -Path $legacyLock -Value $mirror -Encoding ASCII
Say ('LEGACY-MIRROR-WRITTEN ' + $legacyLock + ' (content="' + $mirror + '"; released together with the real lock)')

# a STALE foreign play.lock is deleted (logged first, and only now that we own the real lock)
if ($script:staleLegacy -ne '' -and (Test-Path $script:staleLegacy)) {
    Remove-Item $script:staleLegacy -Force -ErrorAction SilentlyContinue
    Say ('LEGACY-LOCK-STALE-DELETED ' + $script:staleLegacy + ' (registered)')
}

$status = & unity status --format json --no-pager 2>&1 | Out-String
Say ('UNITY-STATUS ' + (Clip $status 300))

Add-Content -Path $playLog -Value ((Get-Date).ToString('yyyy-MM-dd HH:mm') + "`t" + $me + "`t" + $Tag + "`t" + $Why) -Encoding UTF8
Say ('PLAYLOG-APPENDED ' + $playLog)

Unity-Cmd @('editor_focus') -Quiet | Out-Null
# routed through the guard on purpose (u52play 2026-09-24 static assertion): `editor_stop` must
# appear ONLY inside Stop-IfMine, so "every stop is ownership-checked" becomes mechanically checkable.
[void](Stop-IfMine 'pre-play reset')
Start-Sleep -Seconds 2

Unity-Cmd @('recompile') -Quiet | Out-Null
$recompile = '(unknown)'
$failed = '?'
for ($i = 0; $i -lt 90; $i++) {
    Start-Sleep -Seconds 2
    $st = Unity-Cmd @('recompile_status') -Quiet
    try {
        $j = $st | ConvertFrom-Json
        $inner = $j.data.result
        if ($inner -is [string]) { $inner = $inner | ConvertFrom-Json }
        $recompile = [string]$inner.status
        $failed = [string]$inner.failed
    } catch { }
    if ($recompile -match 'up_to_date|completed|idle') { break }
}
Say ('RECOMPILE status=' + $recompile + ' failed=' + $failed)
if ($recompile -notmatch 'up_to_date|completed|idle') {
    # ADJUDICATION 2026-09-24 (team lead, README 5.2 #42 item 4): "waiting for a state" is useless
    # without an action that PUSHES the system into it -- and the old text here claimed "and stopping"
    # while only releasing. Now the guard ("the action") runs first, and the release honours a
    # withheld lock. (Same shape as d2u27's green assertion with a missing behaviour.)
    Say 'ABORT recompile never settled -> stop through the ownership guard, then release MY lock'
    [void](Stop-IfMine 'recompile never settled')
    if ($script:withholdRelease) { Say 'LOCK-RELEASE WITHHELD (recompile never settled)' } else { Release-Lock }
    exit 1
}
if ($failed -eq 'True') {
    Say 'ABORT recompile failed=true -> stop through the ownership guard, then release MY lock'
    [void](Stop-IfMine 'recompile failed')
    if ($script:withholdRelease) { Say 'LOCK-RELEASE WITHHELD (recompile failed)' } else { Release-Lock }
    exit 1
}
Start-Sleep -Seconds 3

# ---- pre-play compile gate: run_script compiles the whole file, so a CS error surfaces here
$gate = Unity-Cmd @('run_script', '--file', $cs, '--entry', 'U32Drv.Api.Ping')
$gateOk = $false
$gateText = [string]$gate
Set-Content -Path (Join-Path $test ('u32_ping_' + $Tag + '.json')) -Value $gateText -Encoding UTF8
if ($gateText -match 'PONG') { $gateOk = $true }
Say ('PING-GATE ok=' + $gateOk + ' ' + (Clip $gateText 300))
if (-not $gateOk) {
    Say 'ABORT the driver did not compile / answer -> stop through the ownership guard, then release MY lock'
    [void](Stop-IfMine 'ping-gate fail')
    # post-play path: an orphan play session must KEEP the lock (adjudication 2026-09-24)
    if ($script:withholdRelease) { Say 'LOCK-RELEASE WITHHELD (ping-gate fail / orphan play)' } else { Release-Lock }
    exit 1
}

Unity-Cmd @('clear_console') -Quiet | Out-Null
Init-Offset
Say ('LOG-OFFSET ' + $script:offset)
Unity-Cmd @('editor_play') -Quiet | Out-Null
Start-Sleep -Seconds 8

$spec = (($shots -replace '\\', '/') + '|' + $Tag)
$r2 = Unity-Cmd @('run_script', '--file', $cs, '--entry', 'U32Drv.Api.Go', '--args', ('[\"' + $spec + '\"]'))
Say ('INSTALL ' + (Clip $r2 400))

$t0 = Get-Date
while (((Get-Date) - $t0).TotalSeconds -lt 420) {
    Read-New
    if (Test-Path $done) { break }
    Start-Sleep -Milliseconds 500
}
Read-New
Start-Sleep -Seconds 3
Read-New

if (Test-Path $done) { Say ('DONE ' + (Get-Content $done -Raw).Trim()) }
else { Say 'DONE-MISSING (the driver never signalled completion)' }

$consoleJson = Unity-Cmd @('console_status') -Quiet
$consoleErrors = -1
try { $cj = $consoleJson | ConvertFrom-Json; $consoleErrors = $cj.data.result.counts.error } catch { }
Say ('CONSOLE-STATUS errors=' + $consoleErrors)

# protocol v2 rule 4: stop the editor ONLY while the lock is still ours; if it was taken over,
# touch nothing of theirs and flag the batch so it gets marked suspect (lock stolen / intervened).
$stoppedMine = Stop-IfMine 'normal shutdown'
# ADJUDICATION 2026-09-24: an orphan play session => KEEP the lock (see Stop-IfMine). Withholding is
# signalled through $script:withholdRelease, which defaults to $null (= falsy) when never set, so the
# normal path still releases. The lock we keep has OUR pid, so once this process exits it reads as
# stale immediately and the next taker may take it over -- but only after stopping the editor first.
if ($stoppedMine -and (-not $script:withholdRelease)) { Release-Lock }
elseif ($stoppedMine) {
    Say ('LOCK-RELEASE WITHHELD (orphan play): ' + $lock + ' stays with us so the unfinished session keeps an owner; a taker must editor_stop + confirm stopped first')
}
$intervened = -not $stoppedMine
Say ('INTERVENED=' + $intervened + ' (true => this batch MUST be marked suspect (lock stolen / intervened))')

# ---- freeze --------------------------------------------------------------------
$sessionEnd = Get-Date
$hdr = New-Object System.Collections.ArrayList
[void]$hdr.Add('# u32-close evidence -- fresh per-frame runtime rows ([U32]) frozen from client/Logs/Editor.log')
[void]$hdr.Add('# session: ' + $sessionStart.ToString('yyyy-MM-dd HH:mm:ss') + '..' + $sessionEnd.ToString('HH:mm:ss') + '   driver: tools/probes/drivers/d2u32_drive.cs')
[void]$hdr.Add('# run: powershell -NoProfile -ExecutionPolicy Bypass -File tools/probes/drivers/d2u32_run.ps1 -Tag ' + $Tag)
[void]$hdr.Add('#   recompile = ' + $recompile + ' failed=' + $failed + '   console = console_status counts.error=' + $consoleErrors)
[void]$hdr.Add('#   lock protocol v2: takeover="' + $script:takeoverNote + '"   intervened=' + $intervened + ' (true => editor_stop was refused because the lock was no longer ours)')
[void]$hdr.Add('# ---------------------------------------------------------------------------')
foreach ($l in @(Lines-With '\[U32\]')) { [void]$hdr.Add($l) }
[System.IO.File]::WriteAllLines($frozen, [string[]]$hdr, (New-Object System.Text.UTF8Encoding($false)))
Say ('FROZEN ' + $frozen + ' lines=' + $hdr.Count)
# README 5.2 #29 again: 3rd multi-line `Say (` in this file -- precompute into $sumLine.
$sumLine = 'SUMMARY tag=' + $Tag + ' done=' + (Test-Path $done) + ' recompile=' + $recompile + ' failed=' + $failed + ' consoleErrors=' + $consoleErrors + ' rows=' + (@(Lines-With '\[U32\]').Count) + ' takeover="' + $script:takeoverNote + '" intervened=' + $intervened
Say $sumLine
Say ('END cli=' + $script:cli)
