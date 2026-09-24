# =============================================================================
# d2u27_run.ps1 -- ONE Play session: the U27 THREE-LAYER trace + frame-cadence A/B.
#
#   powershell -NoProfile -ExecutionPolicy Bypass -File d2u27_run.ps1 -Tag u27v3
#   powershell -NoProfile -ExecutionPolicy Bypass -File d2u27_run.ps1 -LockSelfTest
#
# LOCK PROTOCOL v2.3 (team-lead, sole wording -- no local variants):
#   * the ONLY real lock is <root>/.ai-tmp/test/play-running.lock
#     content = "<owner> <ISO8601> <PID>"   (owner == the play-log owner column)
#   * DUAL WRITE during the transition: the same 3 fields are MIRRORED into the
#     legacy name `play.lock`. The mirror is NOT for new runners (they read the
#     canonical name anyway) -- its ONLY purpose is to BLOCK the 17 historical
#     runners that read `play.lock` only. "reading both names" protects yourself;
#     "writing the mirror" protects everybody else. Both writes are ASCII.
#   * take  : both names absent      -> write canonical (ASCII) -> sleep ~1s ->
#                                       READ BACK; not mine => LOCK-RACE-LOST -> yield
#                                       then write the mirror (ASCII) + log it
#             canonical mine         -> re-entrant, take
#             other, fresh           -> YIELD; fresh == age < 12min, and a MISSING
#                                       PID segment means LIVENESS UNKNOWN => NOT dead
#             other, stale           -> take over: age >= 12min OR (PID present AND dead);
#                                       a stale LEGACY file is deleted with the tag
#                                       LEGACY-LOCK-STALE-DELETED ... (registered)
#   * yield : retry every 3 minutes; NEVER touch the editor while not holding the lock
#   * abort : ABORT (fail fast) before ANY editor_play / recompile / editor_stop
#   * finish: if the lock is not mine any more => DO NOT touch the editor (rule 4)
#   * release: BOTH names are visited and ONLY a file whose content is mine is deleted;
#              somebody else's file is left alone and logged as REFUSED
#   * ledger: play-log.tsv is appended ONLY after LOCK-TAKEN
#
# SELF-CHECKS THAT CAN FAIL (team rule: "a script that says OK must have a check that
# can fail", each with a KNOWN-GOOD and a KNOWN-BAD sample). All print into the trace:
#   SELFCHECK         non-ASCII byte count + BOM, read from this script's own bytes
#   SELFCHECK-STATIC  editor_stop appears EXACTLY ONCE in code and INSIDE the
#                     Stop-Editor-Safe function; every editor_play / recompile call is
#                     AFTER the canonical lock write; both lock writes are -Encoding ASCII
# Measured traps these guard against (all hit this team today):
#   1. PS 5.1 parses a BOM-less non-ASCII .ps1 as ANSI (a Chinese comment turned into a
#      stray quote) and "parse OK" had been a FALSE GREEN.
#   2. PS 5.1 Set-Content -Encoding UTF8 writes a BOM => the owner field reads back as
#      "\uFEFF<owner>" => you fail to recognize (and release) your own lock: zombie lock.
#      => ASCII for BOTH lock files, and strip U+FEFF/U+200B before parsing anyway.
#   3. A batch replace (replace_all) that swaps the editor_stop call for a guarded
#      wrapper can also rewrite the one INSIDE the wrapper => infinite self-recursion.
#      Only a static assertion can see that, hence SELFCHECK-STATIC.
#   4. $pid is a READ-ONLY automatic variable: a parameter named $pid explodes only at
#      call time. This file never binds $pid (it uses $PID and the hash key pid).
#   5. PS 5.1 cannot call a function with empty parens: write `if (StillMine)`.
#   6. A fixed (non per-PID) self-test sandbox gets deleted by another slice's cleanup
#      => false red, or a real red masked as green. The sandbox here is per-PID.
#
# Judged OFFLINE by tools/probes/drivers/d2u27_judge.py (R1..R6).
# ASCII ONLY -- asserted by SELFCHECK below.
# =============================================================================
param(
    [string]$Tag = 'u27v3',
    [switch]$LockSelfTest,
    [switch]$SelfCheckOnly,   # run the byte-level + static self-checks ONLY, then exit (no lock, no editor)
    [string]$Why = 'U27 residual jitter: a live per-frame three-layer trace (Camera.main / EntityView.Root.position / IPlayerModule.World) separates camera vs render vs logic jitter, and the frame-cadence A/B (vSync=0+60 vs vSync=1+-1) is the evidence needed to decide the engine FramePacing policy'
)

$ErrorActionPreference = 'Continue'

$root   = 'c:/Work/Server/f-v2/clover-project-diablo2'
$proj   = 'c:\Work\Server\f-v2\clover-project-diablo2\client'
$cs     = $root + '/tools/probes/drivers/d2u27_jitter.cs'
$outDir = $root + '/.ai-tmp/test'
$shot   = $root + '/.ai-tmp/screenshots/u27'
$tsv    = $outDir + '/d2u27-' + $Tag + '.tsv'
$done   = $outDir + '/d2u27-done-' + $Tag + '.txt'
# README 5.2-30: a self-check entry must NEVER write a path a normal run also writes.
# This line used to be `d2u27-steps-<Tag>.txt` for BOTH modes => `-SelfCheckOnly -Tag u27v5`
# would have truncated/appended to a REAL batch's evidence trace (exactly zorder's incident,
# silently). Self-check modes now go to a per-PID name that no normal run ever uses.
$isSelfCheck = ($LockSelfTest -or $SelfCheckOnly)
if ($isSelfCheck) { $trace = $outDir + '/d2u27-selftest-' + $Tag + '-' + $PID + '.txt' }
else { $trace = $outDir + '/d2u27-steps-' + $Tag + '.txt' }
$logCopy = $outDir + '/d2u27-log-' + $Tag + '.txt'
$logPath = $proj + '/Logs/Editor.log'
$playLog = $outDir + '/play-log.tsv'

$MyOwner    = 'd2u27-u27'                         # == the play-log owner column
$StaleMin   = 12                                  # the team's ONLY threshold
$lockMain   = $outDir + '/play-running.lock'      # canonical lock (written)
$lockLegacy = $outDir + '/play.lock'              # legacy name (mirrored during transition)

$script:offset = 0
$script:lines = New-Object System.Collections.ArrayList
$script:cli = 0
$script:lockHeld = $false
$script:stopped = $false

function Append-Utf8([string]$path, [string]$text) {
    # log/ledger files: always UTF-8 WITHOUT BOM. Why not -Encoding ASCII: the CLI and
    # Unity emit Chinese (e.g. STATUS_NO_INSTANCES) and ASCII would silently turn every
    # such character into '?'. Why not PS -Encoding UTF8: that writes a BOM when the file
    # is created, and a BOM becomes a stray first column for TSV parsers.
    [IO.File]::AppendAllText($path, $text, (New-Object System.Text.UTF8Encoding($false)))
}

function Say([string]$s) {
    $stamp = (Get-Date).ToString('HH:mm:ss.fff')
    Write-Host ($stamp + ' ' + $s)
    Append-Utf8 $trace ($stamp + ' ' + $s + "`r`n")
}

function Strip-Invisible([string]$s) {
    if ($null -eq $s) { return '' }
    return ($s -replace "[\uFEFF\u200B]", '').Trim()
}

function Parse-Lock([string]$path) {
    # returns: exists, owner, iso, pid (-1 = segment missing), ageMin, alive
    $r = @{ exists = $false; owner = ''; iso = ''; pid = -1; ageMin = 0.0; alive = $false }
    if (-not (Test-Path -LiteralPath $path)) { return $r }
    $r.exists = $true
    $fi = Get-Item -LiteralPath $path
    # AGE COMES FROM mtime ONLY -- never from the payload ISO8601 (u3bverify's finding):
    # Touch-Lock refreshes LastWriteTime while the payload keeps the TAKE time, so
    # payload and mtime legitimately differ by minutes on a healthy live lock. Judging
    # stale from the payload would steal a lock that is actively heart-beating.
    $r.ageMin = ((Get-Date) - $fi.LastWriteTime).TotalMinutes
    $body = ''
    try { $body = Strip-Invisible ([IO.File]::ReadAllText($path)) } catch { $body = '' }
    $parts = @($body -split '\s+' | Where-Object { $_ -ne '' })
    if ($parts.Count -ge 1) { $r.owner = $parts[0] }
    if ($parts.Count -ge 2) { $r.iso = $parts[1] }
    if ($parts.Count -ge 3) {
        $tmp = 0
        if ([int]::TryParse($parts[2], [ref]$tmp)) { $r.pid = $tmp }
    }
    if ($r.pid -gt 0) {
        try { $null = Get-Process -Id $r.pid -ErrorAction Stop; $r.alive = $true } catch { $r.alive = $false }
    }
    return $r
}

function Get-LockVerdict([string]$path) {
    # returns: absent | mine | busy | stale   (+ reason)
    $l = Parse-Lock $path
    if (-not $l.exists) { return @{ verdict = 'absent'; why = 'no file' } }
    if ($l.owner -eq $MyOwner) { return @{ verdict = 'mine'; why = 'owner is me' } }
    if ($l.ageMin -ge $StaleMin) {
        return @{ verdict = 'stale'; why = ('age=' + [math]::Round($l.ageMin, 1) + 'm >= ' + $StaleMin + 'm') }
    }
    if ($l.pid -gt 0 -and -not $l.alive) {
        return @{ verdict = 'stale'; why = ('pid=' + $l.pid + ' is dead, age=' + [math]::Round($l.ageMin, 1) + 'm') }
    }
    if ($l.pid -le 0) {
        return @{ verdict = 'busy'; why = ('no PID segment => LIVENESS UNKNOWN, not dead; age=' + [math]::Round($l.ageMin, 1) + 'm < ' + $StaleMin + 'm => yield') }
    }
    return @{ verdict = 'busy'; why = ('pid=' + $l.pid + ' alive, age=' + [math]::Round($l.ageMin, 1) + 'm') }
}

function Classify-Gate([string]$raw) {
    # PURE classifier (fixture-tested in -LockSelfTest): playing | stopped | unknown | bridge-down.
    # fail-closed by construction: anything reachable that does not carry a playMode value is
    # 'unknown' -- never silently 'stopped'.
    if ($raw -notmatch '"success":\s*true') { return 'bridge-down' }   # editor PROCESS absent
    if ($raw -match '"playMode":\s*"([a-zA-Z_]+)"') { return $Matches[1] }
    return 'unknown'                                                    # unreadable != stopped
}

function Find-LoopKind($node) {
    # README 5.2-28 (charstat's counter-example): "is this statement inside a loop" MUST be
    # answered from the AST Parent chain. Indentation back-tracking got the OPPOSITE answer
    # (a closed `}` is not a loop keyword, so the walk kept finding a deeper loop). Returns
    # the nearest enclosing loop kind, or '' when the walk reaches a function boundary.
    $p = $node.Parent
    while ($p -ne $null) {
        if ($p -is [System.Management.Automation.Language.ForStatementAst]) { return 'for' }
        if ($p -is [System.Management.Automation.Language.ForEachStatementAst]) { return 'foreach' }
        if ($p -is [System.Management.Automation.Language.WhileStatementAst]) { return 'while' }
        if ($p -is [System.Management.Automation.Language.DoWhileStatementAst]) { return 'dowhile' }
        if ($p -is [System.Management.Automation.Language.DoUntilStatementAst]) { return 'dountil' }
        if ($p -is [System.Management.Automation.Language.FunctionDefinitionAst]) { return '' }
        $p = $p.Parent
    }
    return ''
}

function Find-EnclosingFunction($node) {
    $p = $node.Parent
    while ($p -ne $null) {
        if ($p -is [System.Management.Automation.Language.FunctionDefinitionAst]) { return $p.Name }
        $p = $p.Parent
    }
    return ''
}

function StillMine {
    # canonical name ONLY (the legacy mirror may be somebody else's stale file)
    $l = Parse-Lock $lockMain
    if (-not $l.exists) { return $false }
    return ($l.owner -eq $MyOwner -and $l.pid -eq $PID)
}

function Stop-Editor-Safe([string]$tag, [switch]$Again) {
    # the ONLY place in this file allowed to call the editor_stop verb (static-asserted:
    # SELFCHECK-STATIC / -STATIC-4 count exactly one `editor_stop` command element and
    # require it inside this function). -Again re-issues it for the release-retry path
    # WITHOUT adding a second call site (an earlier raw retry call was caught by the
    # assertions and made the runner ABORT -- the gate proved it can fail).
    # rule 4: if the lock was taken over we must NOT stop somebody else's editor.
    if ($script:stopped -and -not $Again) { return }
    # explicit comparison ON PURPOSE (README 5.2-12): the decision must not depend on the
    # success pipeline being clean -- `if ((f) -eq $true)` is immune to a helper emitting
    # output (an array holding $false is still a NON-EMPTY array => truthy).
    if (-not ((StillMine) -eq $true)) {
        # README 5.2-42-3: skipping editor_stop because the lock is not mine MUST raise an
        # alarm with owner/pid/playMode -- a silent hand-over makes the next session believe
        # it inherited a clean one.
        $orph = Parse-Lock $lockMain
        $pmNow = 'unknown'
        try { $pmNow = Classify-Gate ((& unity command editor_status --format json --no-pager 2>&1 | Out-String)) } catch { $pmNow = 'unknown' }
        $orphMsg = 'ORPHAN-PLAY(' + $tag + ') stop skipped: lock not mine (owner=' + $orph.owner + ' pid=' + $orph.pid + ') playMode=' + $pmNow + ' => NOT touching the editor; the next session MUST check playMode before entering'
        Say $orphMsg
        return
    }
    $script:stopped = $true
    try {
        & unity command editor_stop --format json --no-pager 2>&1 | Out-Null
        Say ('STOPPED(' + $tag + ')')
    } catch {
        Say ('STOP-FAILED(' + $tag + ') ' + $_.Exception.Message)
    }
}

# ---------------------------------------------------------------------------
# v2.3 sandbox selftest (no editor, and the REAL lock is never taken or released):
#   -LockSelfTest       sandbox path is per-PID and removed in finally
#
# INTENTIONAL-THRESHOLD-FIXTURE: the AddMinutes(-13) / AddMinutes(-11.9) calls below
#   backdate a lock on purpose to straddle the ONLY threshold (12min). They are test
#   fixtures, NOT a violation -- registered here so a future byte-level scan does not
#   report them (same treatment as INTENTIONAL-BOM-SAMPLE further down).
# ---------------------------------------------------------------------------
if ($LockSelfTest) {
    $script:ok = 0
    $script:bad = 0
    $script:src = Get-Content -Encoding UTF8 $PSCommandPath
    $sandbox = Join-Path $root ('.ai-tmp/test/d2u27-lockselftest-' + $PID)
    $f = $sandbox + '/play-running.lock'
    $fm = $sandbox + '/play.lock'
    $script:rc = 0

    function Chk([string]$name, [bool]$cond, [string]$detail) {
        if ($cond) { Write-Host ('  [ OK ] ' + $name + '   (' + $detail + ')'); $script:ok = $script:ok + 1 }
        else { Write-Host ('  [FAIL] ' + $name + '   (' + $detail + ')'); $script:bad = $script:bad + 1 }
    }

    try {
        if (Test-Path $sandbox) { Remove-Item $sandbox -Recurse -Force }
        New-Item -ItemType Directory -Path $sandbox -Force | Out-Null

        Chk 'sandbox-is-per-pid' ($sandbox -match ([string]$PID)) $sandbox

        $v = Get-LockVerdict $f
        Chk 'absent-when-no-file' ($v.verdict -eq 'absent') $v.why

        # KNOWN-BAD #1: fresh lock, live PID -> must YIELD
        Set-Content -LiteralPath $f -Value ('other 2026-09-24T11:00:00+08:00 ' + $PID) -Encoding ASCII
        $v = Get-LockVerdict $f
        Chk 'live-pid-fresh-YIELD' ($v.verdict -eq 'busy') $v.why

        # KNOWN-BAD #2: fresh lock, NO PID segment -> liveness UNKNOWN -> must YIELD
        #              (this is the exact case that must NOT be stolen)
        Set-Content -LiteralPath $f -Value 'other 2026-09-24T11:00:00+08:00' -Encoding ASCII
        $v = Get-LockVerdict $f
        Chk 'no-pid-fresh-YIELD' ($v.verdict -eq 'busy') $v.why

        # KNOWN-BAD #3: live PID but age 13m -> stale, take over
        Set-Content -LiteralPath $f -Value ('other ' + (Get-Date).ToString('o') + ' 999999') -Encoding ASCII
        (Get-Item -LiteralPath $f).LastWriteTime = (Get-Date).AddMinutes(-13)
        $v = Get-LockVerdict $f
        Chk 'live-pid-age13-TAKEOVER' ($v.verdict -eq 'stale') $v.why

        Set-Content -LiteralPath $f -Value ('other ' + (Get-Date).ToString('o')) -Encoding ASCII
        (Get-Item -LiteralPath $f).LastWriteTime = (Get-Date).AddMinutes(-13)
        $v = Get-LockVerdict $f
        Chk 'no-pid-age13-TAKEOVER' ($v.verdict -eq 'stale') $v.why

        # KNOWN-GOOD boundary: 11.9m must still YIELD (threshold is strictly 12m)
        Set-Content -LiteralPath $f -Value ('other ' + (Get-Date).ToString('o')) -Encoding ASCII
        (Get-Item -LiteralPath $f).LastWriteTime = (Get-Date).AddMinutes(-11.9)
        $v = Get-LockVerdict $f
        Chk 'no-pid-age11.9-YIELD' ($v.verdict -eq 'busy') $v.why

        # KNOWN-BAD #4: dead PID, fresh -> stale, take over
        Set-Content -LiteralPath $f -Value ('other ' + (Get-Date).ToString('o') + ' 999999') -Encoding ASCII
        $v = Get-LockVerdict $f
        Chk 'dead-pid-TAKEOVER' ($v.verdict -eq 'stale') $v.why

        Set-Content -LiteralPath $f -Value ('d2u27-u27 ' + (Get-Date).ToString('o') + ' ' + $PID) -Encoding ASCII
        $v = Get-LockVerdict $f
        Chk 'mine-is-reentrant' ($v.verdict -eq 'mine') $v.why

        $l = Parse-Lock $f
        Chk 'take-lock-3-fields' ($l.owner -eq 'd2u27-u27' -and $l.pid -eq $PID -and $l.alive) ('owner=' + $l.owner + ' pid=' + $l.pid + ' alive=' + $l.alive)

        # the mirror must be ASCII too, and must be parseable back as mine
        Set-Content -LiteralPath $fm -Value ('d2u27-u27 ' + (Get-Date).ToString('o') + ' ' + $PID) -Encoding ASCII
        $lm = Parse-Lock $fm
        Chk 'mirror-3-fields-parseable' ($lm.owner -eq 'd2u27-u27' -and $lm.pid -eq $PID) ('owner=' + $lm.owner + ' pid=' + $lm.pid)

        # byte-level: neither lock file may start with a BOM
        $b1 = [IO.File]::ReadAllBytes($f)
        $b2 = [IO.File]::ReadAllBytes($fm)
        $bom1 = ($b1.Length -ge 3 -and $b1[0] -eq 0xEF -and $b1[1] -eq 0xBB -and $b1[2] -eq 0xBF)
        $bom2 = ($b2.Length -ge 3 -and $b2[0] -eq 0xEF -and $b2[1] -eq 0xBB -and $b2[2] -eq 0xBF)
        Chk 'lock-files-have-no-BOM' ((-not $bom1) -and (-not $bom2)) ('canonicalBom=' + $bom1 + ' mirrorBom=' + $bom2)

        # KNOWN-BAD #5: a BOM-prefixed owner must STILL be recognized as mine (Strip-Invisible)
        #   INTENTIONAL-BOM-SAMPLE -- this writes a BOM on purpose (test fixture, NOT a
        #   violation of v2.3). Registered here so byte-level scans do not flag it.
        [IO.File]::WriteAllText($f, ([char]0xFEFF + 'd2u27-u27 ' + (Get-Date).ToString('o') + ' ' + $PID))
        $v = Get-LockVerdict $f
        Chk 'bom-prefixed-owner-still-mine' ($v.verdict -eq 'mine') $v.why

        # static text assertion: BOTH lock writes must be -Encoding ASCII, none UTF8
        $asciiWrites = 0
        $utf8LockWrites = 0
        foreach ($ln in $script:src) {
            $t = $ln.Trim()
            if ($t.StartsWith('#')) { continue }
            if ($t.Contains('Set-Content') -and ($t.Contains('lockMain') -or $t.Contains('lockLegacy')) -and ($t.Contains('Value'))) {
                if ($t -match '-Encoding\s+ASCII') { $asciiWrites = $asciiWrites + 1 }
                elseif ($t -match '-Encoding\s+UTF8') { $utf8LockWrites = $utf8LockWrites + 1 }
            }
        }
        Chk 'lock-writes-use-ASCII' ($asciiWrites -ge 2 -and $utf8LockWrites -eq 0) ('asciiWrites=' + $asciiWrites + ' utf8LockWrites=' + $utf8LockWrites)

        # fail-closed gate classifier: KNOWN-GOOD (playing/stopped) and KNOWN-BAD
        # (reachable-but-field-missing => unknown; unreachable => bridge-down) fixtures.
        Chk 'gate-classify-playing' ((Classify-Gate '{"success": true, "data": {"result": {"playMode": "playing"}}}') -eq 'playing') 'fixture: playMode=playing'
        Chk 'gate-classify-stopped' ((Classify-Gate '{"success": true, "data": {"result": {"status": "ready", "playMode": "stopped"}}}') -eq 'stopped') 'fixture: playMode=stopped'
        Chk 'gate-classify-unknown-fail-closed' ((Classify-Gate '{"success": true, "data": {"result": {"status": "ready"}}}') -eq 'unknown') 'reachable but no playMode field => unknown (never "stopped")'
        Chk 'gate-classify-bridge-down' ((Classify-Gate '{"success": false, "errors": [{"code": "COMMAND_FAILED"}]}') -eq 'bridge-down') 'editor process absent'
    } finally {
        Remove-Item $sandbox -Recurse -Force -ErrorAction SilentlyContinue
    }

    Chk 'sandbox-cleaned' (-not (Test-Path $sandbox)) 'removed'
    if ($script:bad -gt 0) { $script:rc = 1 }
    # NOTE: no StillMine case here ON PURPOSE -- StillMine reads the REAL canonical lock,
    # and this selftest must never touch it. "my own lock is recognizable" is covered by
    # mine-is-reentrant / take-lock-3-fields / mirror-3-fields-parseable / bom-...
    Write-Host ('LOCK-SELFTEST summary: OK=' + $script:ok + ' FAIL=' + $script:bad)
    exit $script:rc
}

# ---------------------------------------------------------------------------
# startup self-checks (can fail; results go into the trace)
# ---------------------------------------------------------------------------
Set-Content -Path $trace -Value ('# d2u27 steps tag=' + $Tag) -Encoding ASCII
Set-Location -LiteralPath $proj
Say ('BEGIN tag=' + $Tag)
Say ('RUNNER-PID ' + $PID)

$selfBytes = [IO.File]::ReadAllBytes($PSCommandPath)
$nonAscii = 0
foreach ($x in $selfBytes) { if ($x -gt 127) { $nonAscii = $nonAscii + 1 } }
$hasBom = ($selfBytes.Length -ge 3 -and $selfBytes[0] -eq 0xEF -and $selfBytes[1] -eq 0xBB -and $selfBytes[2] -eq 0xBF)
$asciiOk = ($nonAscii -eq 0 -or $hasBom)
Say ('SELFCHECK nonAscii=' + $nonAscii + ' bom=' + $hasBom + ' bytes=' + $selfBytes.Length + ' => ' + $(if ($asciiOk) { 'PASS' } else { 'FAIL' }))

# The artifact carries its OWN fingerprint (u3bverify's suggestion): any reading in this
# trace can be traced back to the exact script revision without anyone pasting line numbers
# by hand -- pasted fingerprints go stale the moment the file is edited again.
$selfSha = (Get-FileHash -Algorithm SHA256 -LiteralPath $PSCommandPath).Hash.ToLower().Substring(0, 16)
$selfLines = [IO.File]::ReadAllLines($PSCommandPath).Count
# README 5.2-29 (hard taboo): ONE line -- a multi-line `Say (` continuation is what makes
# PS 5.1 suddenly report "Missing closing ')'" when someone adds a line nearby.
$fpMsg = 'RUNNER-FINGERPRINT sha256_16=' + $selfSha + ' bytes=' + $selfBytes.Length + ' lines=' + $selfLines + ' mtime=' + (Get-Item -LiteralPath $PSCommandPath).LastWriteTime.ToString('yyyy-MM-dd HH:mm:ss') + ' (authoritative for THIS trace; verified by re-computing, not by trusting a pasted number)'
Say $fpMsg
if (-not $asciiOk) {
    Say 'ABORT non-ASCII .ps1 without BOM (PS 5.1 would parse it as ANSI) => never touched the editor'
    exit 4
}

# ---- static lock-discipline assertions ------------------------------------
# Patterns are assembled at run time ON PURPOSE: a literal pattern written here would
# match THIS block's own source line (self-referential false red). Comment lines are
# skipped (the header documents the protocol and names the verbs), and only lines that
# actually INVOKE the editor CLI count (a Say()/ABORT message is not a call).
$patStop = 'ed' + 'itor_stop'
$patPlay = "'ed" + "itor_play'|'rec" + "ompile'"
$patLock = 'Set-Content -LiteralPath ' + '$lockMain'
$patMirror = 'Set-Content -LiteralPath ' + '$lockLegacy'
$src = Get-Content -Encoding UTF8 $PSCommandPath
$stopAt = @()
$playAt = @()
$lockAt = 0
$mirrorAt = 0
for ($i = 0; $i -lt $src.Count; $i++) {
    $t = $src[$i].Trim()
    if ($t.StartsWith('#')) { continue }
    $isCall = ($src[$i] -match 'Unity-Cmd|unity command')
    if ($isCall -and $src[$i] -match $patStop) { $stopAt = $stopAt + ($i + 1) }
    if ($isCall -and $src[$i] -match $patPlay) { $playAt = $playAt + ($i + 1) }
    if ($src[$i].Contains($patLock)) { $lockAt = $i + 1 }
    if ($src[$i].Contains($patMirror)) { $mirrorAt = $i + 1 }
}

# the guard function's own line range (so we can prove the single stop sits inside it)
$fnStart = 0
$fnEnd = 0
for ($i = 0; $i -lt $src.Count; $i++) {
    if ($src[$i] -match '^\s*function\s+Stop-Editor-Safe\b') { $fnStart = $i + 1 }
    elseif ($fnStart -gt 0 -and $fnEnd -eq 0 -and $i -ge $fnStart -and $src[$i] -match '^\}') { $fnEnd = $i + 1; break }
}
$stopOnce = ($stopAt.Count -eq 1)
$stopGuarded = ($stopOnce -and $fnStart -gt 0 -and $fnEnd -gt 0 -and $stopAt[0] -gt $fnStart -and $stopAt[0] -lt $fnEnd)
# matches the CALL shape only ("Stop-Editor-Safe '<tag>'"), so the regex literal used by
# the function-range scan above is not counted as a call site (it was, in the first pass)
$patSafeCall = 'Stop-' + "Editor-Safe '"
$stopCalls = 0
foreach ($ln in $src) {
    if ($ln.Trim().StartsWith('#')) { continue }
    if ($ln.Contains($patSafeCall)) { $stopCalls = $stopCalls + 1 }
}
$afterOk = ($lockAt -gt 0 -and $playAt.Count -gt 0)
foreach ($ln in $playAt) { if ($ln -le $lockAt) { $afterOk = $false } }
$mirrorOk = ($mirrorAt -gt $lockAt -and ($playAt.Count -eq 0 -or $mirrorAt -lt $playAt[0]))

# README 5.2-12/13 (u52play's false-exclusivity bug): a helper that writes to the SUCCESS
# PIPELINE poisons `if (f)` -- @('a line', $false) is a NON-EMPTY array => truthy => a lost
# lock race reads as "won". Assert WITHOUT relying on any stub / sandbox double:
#   (a) the printing helper Say uses Write-Host and never a success-pipeline write;
#   (b) no bare `if (StillMine)` call is left anywhere in code (must be an explicit -eq $true).
$patWO = 'Write-' + 'Output'                 # assembled so this line cannot match itself
$sayStart = 0
$sayEnd = 0
for ($i = 0; $i -lt $src.Count; $i++) {
    if ($src[$i] -match '^\s*function\s+Say\b') { $sayStart = $i + 1 }
    elseif ($sayStart -gt 0 -and $sayEnd -eq 0 -and $i -ge $sayStart -and $src[$i] -match '^\}') { $sayEnd = $i + 1; break }
}
$sayCode = @()
for ($i = $sayStart - 1; $i -lt $sayEnd; $i++) {
    if (-not $src[$i].Trim().StartsWith('#')) { $sayCode = $sayCode + $src[$i] }
}
$sayHost = (@($sayCode | Where-Object { $_ -match 'Write-Host' }).Count -gt 0)
$sayOut = (@($sayCode | Where-Object { $_ -match $patWO }).Count -eq 0)
$woHits = 0
$bareIf = 0
$patBare = 'if (' + '(StillMine)' + ')'          # assembled for the same reason
$patBareNot = 'if (' + '-not (StillMine)' + ')'
foreach ($ln in $src) {
    if ($ln.Trim().StartsWith('#')) { continue }
    if ($ln -match $patWO) { $woHits = $woHits + 1 }
    if ($ln.Contains($patBare) -or $ln.Contains($patBareNot)) { $bareIf = $bareIf + 1 }
}
$pipeOk = ($sayHost -and $sayOut -and $woHits -eq 0 -and $bareIf -eq 0 -and $sayEnd -gt $sayStart)

# README 5.2-25: never end silently. Every terminating `exit <n>` must be preceded (within
# 4 code lines) by an ABORT marker, and the normal path must print its terminal marker.
$patEnd = 'Say ' + "'END'"
$exitAt = @()
for ($i = 0; $i -lt $src.Count; $i++) {
    $t = $src[$i].Trim()
    if ($t.StartsWith('#')) { continue }
    if ($t -match '^exit\s+\d+$') { $exitAt = $exitAt + ($i + 1) }
}
$exitMarked = ($exitAt.Count -gt 0)
foreach ($ln in $exitAt) {
    $lo = [Math]::Max(0, $ln - 4)
    if ((($src[$lo..($ln - 1)] -join ' ')) -notmatch 'ABORT|SELFCHECK-ONLY-END') { $exitMarked = $false }
}
$endMarker = $false
foreach ($ln in $src) {
    if ($ln.Trim().StartsWith('#')) { continue }
    if ($ln.Contains($patEnd)) { $endMarker = $true }
}
$noSilentOk = ($exitMarked -and $endMarker)
Say ('SELFCHECK-STATIC-3 exits@[' + ($exitAt -join ',') + '] all-marked=' + $exitMarked + ' endMarker=' + $endMarker + ' => ' + $(if ($noSilentOk) { 'PASS' } else { 'FAIL' }))

# SELFCHECK-STATIC-4: the same three questions answered from the REAL AST (README 5.2-28),
# because line ranges / indentation cannot tell "the loop closed" from "still inside it".
#   (a) every `continue` must have an enclosing loop        <- verify.ps1's planned item 1
#   (b) `editor_stop` occurs exactly once and inside Stop-Editor-Safe
#   (c) every numeric `exit` is preceded (same block) by an ABORT / SELFCHECK-ONLY-END marker
$astTokens = $null
$astErrors = $null
$ast = [System.Management.Automation.Language.Parser]::ParseFile($PSCommandPath, [ref]$astTokens, [ref]$astErrors)
$astCnt = 0
$astCntInLoop = 0
if ($ast -ne $null) {
    foreach ($n in $ast.FindAll({ param($x) $x -is [System.Management.Automation.Language.ContinueStatementAst] }, $true)) {
        $astCnt = $astCnt + 1
        if ((Find-LoopKind $n) -ne '') { $astCntInLoop = $astCntInLoop + 1 }
    }
}
$astStopCnt = 0
$astStopFn = ''
if ($ast -ne $null) {
    foreach ($c in $ast.FindAll({ param($x) $x -is [System.Management.Automation.Language.CommandAst] }, $true)) {
        $hit = $false
        foreach ($e in $c.CommandElements) { if ($e.Extent.Text -eq 'editor_stop') { $hit = $true } }
        if ($hit) { $astStopCnt = $astStopCnt + 1; if ($astStopFn -eq '') { $astStopFn = Find-EnclosingFunction $c } }
    }
}
# (c) AST form is fiddly for "preceded by"; the line-window version in STATIC-3 already
#     answers it and is not a nesting question, so it stays the authority for (c).
# README 5.2-42-4 (team-lead): "waiting for a state" must be paired with "pushing it there",
# so assert the SHUTDOWN path (the finally block) really CALLS the guarded stopper. This is an
# existence check on the AST -- deliberately NOT a line-number comparison, because line order
# does not equal execution order (5.2-43). The static "editor_stop appears once" assertion was
# green while the shutdown path never reached that one call site.
$finStop = $false
if ($ast -ne $null) {
    foreach ($t in $ast.FindAll({ param($x) $x -is [System.Management.Automation.Language.TryStatementAst] }, $true)) {
        if ($t.Finally -ne $null) {
            foreach ($c in $t.Finally.FindAll({ param($x) $x -is [System.Management.Automation.Language.CommandAst] }, $true)) {
                if ($c.CommandElements.Count -gt 0 -and $c.CommandElements[0].Extent.Text -eq 'Stop-Editor-Safe') { $finStop = $true }
            }
        }
    }
}
$astOk = ($astCnt -eq $astCntInLoop) -and ($astStopCnt -eq 1) -and ($astStopFn -eq 'Stop-Editor-Safe') -and ($astErrors.Count -eq 0) -and $finStop
# README 5.2 (u3bverify's self-correction): a row must print the revision it ACTUALLY judged.
# The startup RUNNER-FINGERPRINT is taken earlier than this parse, so if the file changed in
# between, this row would otherwise carry a version it never judged. Print post-parse sha and
# say so explicitly when it differs (the end-of-run RUNNER-CHANGED-MID-RUN is the second net).
$selfShaPost = (Get-FileHash -Algorithm SHA256 -LiteralPath $PSCommandPath).Hash.ToLower().Substring(0, 16)
if ($selfShaPost -eq $selfSha) {
    $revNote = 'sha16=' + $selfShaPost
} else {
    $revNote = 'sha16=' + $selfShaPost + ' [PARSED != STARTUP(' + $selfSha + ') => this row judges ' + $selfShaPost + ']'
}
Say ('SELFCHECK-STATIC-4(AST) ' + $revNote + ' continue=' + $astCnt + ' in-loop=' + $astCntInLoop + ' editor_stop=' + $astStopCnt + ' in-fn=' + $astStopFn + ' finallyCallsStopper=' + $finStop + ' parseErrors=' + $astErrors.Count + ' => ' + $(if ($astOk) { 'PASS' } else { 'FAIL' }))
$s2Msg = 'SELFCHECK-STATIC-2 say@[' + $sayStart + '..' + $sayEnd + '] writeHost=' + $sayHost + ' noPipeWrite=' + $sayOut + ' pipelineWriteHits=' + $woHits + ' bareIfStillMine=' + $bareIf + ' => ' + $(if ($pipeOk) { 'PASS' } else { 'FAIL' })
Say $s2Msg

$s1Msg = 'SELFCHECK-STATIC stop@[' + ($stopAt -join ',') + '] once=' + $stopOnce + ' in-fn(' + $fnStart + '..' + $fnEnd + ')=' + $stopGuarded + ' calls=' + $stopCalls + ' ; play/recompile@[' + ($playAt -join ',') + '] after-lock-line(' + $lockAt + ')=' + $afterOk + ' ; mirror-line(' + $mirrorAt + ') after-lock-before-play=' + $mirrorOk
Say $s1Msg
if (-not ($stopGuarded -and $stopCalls -ge 2 -and $afterOk -and $mirrorOk -and $pipeOk -and $noSilentOk -and $astOk)) {
    Say 'ABORT static lock-discipline self-check failed => never touched the editor'
    exit 5
}

# ---- PRE-TAKE: playMode gate (team-lead rule 1) ---------------------------
# "no lock file but still in Play" is NOT a lock bug: `editor_stop` returns from the CLI
# before the editor has actually left Play mode (restoring the backup scene takes time).
# => look at the LOCK **and** playMode, and `playMode=playing` => ABORT: take nothing,
# stop nothing, write no ledger. Parsed only from editor_status (it is the only source
# that can see playMode; a playMode-blind source must never override it -- rule 2).
# (called inline on purpose: Unity-Cmd is defined further down, after the lock is held.)
# FAIL-CLOSED (team-lead ruling, classcols' finding): an UNREADABLE playMode must NOT be
# read as "stopped" -- that is the same door rule 1 was meant to close (empty lock + editor
# in Play + state unreadable => grab the lock and call editor_play). The lock side already
# uses this polarity for a missing PID (unknown != dead => yield); mirror it here.
# Three distinct states, probed with a BOUNDED retry (3 x 5s) so a transient blip cannot
# stall us, and any reachable reading during that window wins:
#   reachable + "playing"  -> ABORT (take nothing, stop nothing, write no ledger)
#   reachable + "stopped"  -> proceed
#   UNREADABLE (reachable but no playMode field) -> YIELD (unknown != stopped)
#   unreachable (success:false / COMMAND_FAILED = the editor PROCESS is absent)
#                          -> proceed, logged as LOCK-GATE-BRIDGE-DOWN probed=N (no editor
#                             => nobody can be in Play; some runners start it that way)
# README 5.2-25: this gate lives INSIDE the lock retry loop ON PURPOSE. A `continue`
#    written OUTSIDE any loop makes PowerShell end the script SILENTLY with exit 0 -- no
#    LOCK-TAKEN, no ledger, no Play, no error (classcols' fixture; the 4th "silent success"
#    case today). And every terminating `exit` below is preceded by a Say('ABORT ...').
# -SelfCheckOnly: verification must never be able to grab the lock. Before this switch
# existed, the only way to read the startup self-checks was to run the real runner and kill
# it -- which is safe ONLY while somebody else happens to hold the lock. On 13:26 the lock
# was free, the runner legitimately took it, and Stop-Process -Force left a zombie lock
# (owner d2u27-u27, pid dead) plus a junk ledger row. This switch removes that whole risk.
if ($SelfCheckOnly) {
    Say 'SELFCHECK-ONLY-END (byte-level + static self-checks above; NO lock taken, editor untouched)'
    exit 0
}

$tookOver = ''
$held = $false
$gateUnknown = $false
for ($i = 0; $i -lt 4; $i++) {
    $gate = 'unknown'
    $gateStatus = ''
    for ($g = 0; $g -lt 3; $g++) {
        $rawPre = & unity command editor_status --format json --no-pager 2>&1 | Out-String
        $gate = Classify-Gate $rawPre
        if ($gate -eq 'bridge-down') { $gateStatus = '(unreachable)' }
        elseif ($rawPre -match '"status":\s*"([a-zA-Z_]+)"') { $gateStatus = $Matches[1] }
        else { $gateStatus = '(no status)' }
        $gMsg = 'LOCK-GATE probe=' + ($g + 1) + ' reachable=' + $(if ($gate -eq 'bridge-down') { 'false' } else { 'true' }) + ' status=' + $gateStatus + ' playMode=' + $gate + ' bytes=' + $rawPre.Length
        Say $gMsg
        if ($gate -eq 'playing' -or $gate -eq 'stopped') { break }
        Start-Sleep -Seconds 5
    }
    if ($gate -eq 'playing') {
        Say 'ABORT editor-is-playing (pre-take gate) => not taking the lock, not sending editor_stop, not writing play-log'
        exit 6
    }
    if ($gate -eq 'unknown') {
        $gateUnknown = $true
        Say ('LOCK-GATE-UNKNOWN playMode unreadable (try ' + ($i + 1) + ') -> yield (unknown != stopped); not taking the lock this round')
        if ($i -lt 3) { Start-Sleep -Seconds 180 }
        continue
    }
    $gateUnknown = $false
    if ($gate -eq 'bridge-down') { Say 'LOCK-GATE-BRIDGE-DOWN probed=3 (editor process absent => nobody can be in Play; proceeding)' }
    Say ('PRETAKEN-EDITOR-STATE playMode=' + $gate + ' status=' + $gateStatus + ' (fail-closed gate passed, attempt ' + ($i + 1) + ')')

    $busy = $false
    foreach ($c in @($lockMain, $lockLegacy)) {
        $v = Get-LockVerdict $c
        if ($v.verdict -eq 'busy') {
            Say ('LOCK-BUSY path=' + $c + ' ' + $v.why + ' (try ' + ($i + 1) + ')')
            $busy = $true
        } elseif ($v.verdict -eq 'stale') {
            $l = Parse-Lock $c
            $tookOver = $l.owner + ' (' + $v.why + ')'
            Say ('LOCK-STALE-TAKEOVER path=' + $c + ' owner=' + $l.owner + ' reason=' + $v.why)
        }
    }
    if (-not $busy) { $held = $true; break }
    Start-Sleep -Seconds 180
}
if (-not $held) {
    if ($gateUnknown) {
        Say 'ABORT lock-gate-unknown after 4 attempts (playMode unreadable; unknown != stopped) => never touched the editor, never wrote play-log'
        exit 7
    }
    $who = foreach ($c in @($lockMain, $lockLegacy)) { $l = Parse-Lock $c; if ($l.exists) { $l.owner } }
    Say ('ABORT lock held by ' + ($who -join ' | '))
    Say 'ABORT (never touched the editor, never wrote play-log)'
    exit 2
}

# canonical lock first; ASCII (no BOM => the owner field reads back clean)
Set-Content -LiteralPath $lockMain -Value ($MyOwner + ' ' + (Get-Date).ToString('o') + ' ' + $PID) -Encoding ASCII
Say ('LOCK-TAKEN ' + $MyOwner + ' pid=' + $PID + ' (canonical, 3 fields, ASCII)')

Start-Sleep -Seconds 1
$echo = Strip-Invisible ([IO.File]::ReadAllText($lockMain))
$ep = @($echo -split '\s+')
if ($ep.Count -lt 3 -or $ep[0] -ne $MyOwner -or $ep[2] -ne [string]$PID) {
    Say ('LOCK-RACE-LOST readback="' + $echo + '" => yield')
    Say 'ABORT (never touched the editor, never wrote play-log)'
    if ((StillMine) -eq $true) { Remove-Item -LiteralPath $lockMain -Force -ErrorAction SilentlyContinue }
    exit 3
}
Say ('LOCK-VERIFIED readback="' + $echo + '"')
$script:lockHeld = $true

# stale files lying around under either name are accounted for and removed, then the
# mirror is written so that the legacy-only historical runners keep seeing "taken".
foreach ($c in @($lockMain, $lockLegacy)) {
    $l = Parse-Lock $c
    if (-not $l.exists) { continue }
    if ($l.owner -eq $MyOwner) { continue }
    if ($l.ageMin -ge $StaleMin -or ($l.pid -gt 0 -and -not $l.alive)) {
        Say ('LEGACY-LOCK-STALE-DELETED path=' + $c + ' owner=' + $l.owner + ' age=' + [math]::Round($l.ageMin, 1) + 'm pid=' + $l.pid + ' (registered)')
        Remove-Item -LiteralPath $c -Force -ErrorAction SilentlyContinue
    } else {
        Say ('LOCK-FRESH-OTHER-KEPT path=' + $c + ' owner=' + $l.owner + ' age=' + [math]::Round($l.ageMin, 1) + 'm => not mine, not stale, LEFT ALONE')
    }
}

# v2.3 dual write: the MIRROR is what blocks the 17 legacy-only runners
# README 5.1 (team-lead): the mirror carries the CANONICAL READBACK TEXT => owner + PID + ISO
# are all identical (byte-identical). The older shape (a fresh ISO ~1s later) is legitimate
# too and stays as the degenerate sample; nothing asserts byte INequality either way.
Set-Content -LiteralPath $lockLegacy -Value $echo -Encoding ASCII
$mEcho = Strip-Invisible ([IO.File]::ReadAllText($lockLegacy))
Say ('LEGACY-MIRROR-WRITTEN path=' + $lockLegacy + ' readback="' + $mEcho + '" byteEqualCanonical=' + [string]($mEcho -eq $echo) + ' (ASCII, 3 fields)')

# README 5.2-42-4 (the TAKER's obligation): I may have just taken over a STALE lock, and the
# previous owner may have left an orphan Play (that is exactly what its ORPHAN-PLAY would
# have recorded). Now that the lock IS mine I am the legitimate owner, so clear it BEFORE my
# own session: editor_stop (the single guarded entry) -> read back playMode=stopped.
# NOTE: Get-EditorState/Unity-Cmd are defined further down, so this uses an inline probe.
$rawChk = & unity command editor_status --format json --no-pager 2>&1 | Out-String
if ((Classify-Gate $rawChk) -eq 'playing') {
    Say 'STALE-TAKEOVER-ORPHAN-PLAY playMode=playing => editor_stop + confirm stopped before my session (5.2-42-4)'
    Stop-Editor-Safe 'stale-cleanup' -Again
    for ($k3 = 0; $k3 -lt 5; $k3++) {
        Start-Sleep -Seconds 2
        $pm3 = Classify-Gate ((& unity command editor_status --format json --no-pager 2>&1 | Out-String))
        Say ('STALE-TAKEOVER-WAIT playMode=' + $pm3 + ' try=' + ($k3 + 1))
        if ($pm3 -eq 'stopped') { Say ('STALE-TAKEOVER-CLEAN playMode=stopped try=' + ($k3 + 1)); break }
    }
} else {
    Say ('STALE-TAKEOVER-CHECK playMode=' + (Classify-Gate $rawChk) + ' (no orphan Play to clear)')
}

function Touch-Lock() {
    if (-not $script:lockHeld) { return }
    if ((StillMine) -eq $true) {
        try {
            $now = Get-Date
            (Get-Item -LiteralPath $lockMain).LastWriteTime = $now
            if (Test-Path -LiteralPath $lockLegacy) { (Get-Item -LiteralPath $lockLegacy).LastWriteTime = $now }
        } catch { }
    }
}

function Init-Offset() {
    if (Test-Path $logPath) { $script:offset = (Get-Item $logPath).Length } else { $script:offset = 0 }
}

function Read-New() {
    if (-not (Test-Path $logPath)) { return }
    $fs = $null
    try {
        Touch-Lock
        $fs = New-Object System.IO.FileStream($logPath, [System.IO.FileMode]::Open,
              [System.IO.FileAccess]::Read, [System.IO.FileShare]::ReadWrite)
        if ($fs.Length -le $script:offset) { return }
        $fs.Seek($script:offset, [System.IO.SeekOrigin]::Begin) | Out-Null
        $len = [int]($fs.Length - $script:offset)
        $buf = New-Object byte[] $len
        $read = $fs.Read($buf, 0, $len)
        $script:offset = $script:offset + $read
        $txt = [System.Text.Encoding]::UTF8.GetString($buf, 0, $read)
        $keep = @($txt -split "`r?`n" | Where-Object { $_ -match '\[U27\]' })
        foreach ($l in $keep) { [void]$script:lines.Add($l) }
    } catch {
        Say ('WARN log-read ' + $_.Exception.Message)
    } finally {
        if ($fs -ne $null) { $fs.Close(); $fs.Dispose() }
    }
}

function Unity-Cmd([string[]]$argv, [switch]$Quiet) {
    Touch-Lock
    $script:cli = $script:cli + 1
    $raw = & unity command @argv --format json --no-pager 2>&1 | Out-String
    if (-not $Quiet) { Say ('CLI ' + ($argv -join ' ') + ' -> ' + $raw.Length + ' bytes') }
    return $raw
}

function Get-EditorState() {
    # PRIMARY readiness source: the ONLY one that reports playMode. Rule 2: it must never
    # be OR-ed with a playMode-blind source (`console`), because that lets a blind
    # "startable" override a `playMode=playing` verdict. console is a FALLBACK, used only
    # when this one cannot be parsed.
    $r = @{ parsed = $false; status = ''; playMode = ''; compiling = ''; compilationFailed = ''; bytes = 0 }
    $raw = Unity-Cmd @('editor_status') -Quiet
    $r.bytes = $raw.Length
    if ($raw -match '"success":\s*true') {
        if ($raw -match '"status":\s*"([a-zA-Z_]+)"') { $r.status = $Matches[1] }
        if ($raw -match '"playMode":\s*"([a-zA-Z_]+)"') { $r.playMode = $Matches[1] }
        if ($raw -match '"compiling":\s*(true|false)') { $r.compiling = $Matches[1] }
        if ($raw -match '"compilationFailed":\s*(true|false)') { $r.compilationFailed = $Matches[1] }
        $r.parsed = ($r.status -ne '' -and $r.playMode -ne '')
    }
    return $r
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

if ($tookOver -ne '') { Say ('REPORT-NOTE took over a stale lock: ' + $tookOver) }

try {
    foreach ($p in @($tsv, $done)) { if (Test-Path $p) { Remove-Item $p -Force; Say ('CLEARED ' + $p) } }
    if (Test-Path $shot) { Remove-Item $shot -Recurse -Force -ErrorAction SilentlyContinue }

    # ledger: ONLY after the lock is held (rule 5); owner column == lock owner
    $logLine = (Get-Date).ToString('yyyy-MM-dd HH:mm') + "`t" + $MyOwner + "`td2u27-" + $Tag + "`t" + $Why
    Append-Utf8 $playLog ($logLine + "`r`n")
    Say ('PLAYLOG-APPENDED (after LOCK-TAKEN) ' + $playLog)

    Unity-Cmd @('editor_focus') -Quiet | Out-Null
    Stop-Editor-Safe 'pre'
    Start-Sleep -Seconds 2
    Unity-Cmd @('clear_console') -Quiet | Out-Null
    Init-Offset
    Say ('LOG-OFFSET ' + $script:offset)

    # NOTE (corrected): `unity status` returns an empty table because its `--project-path`
    # `--project-path` is SILENTLY DROPPED by that subcommand (team-lead A/B/C measured it),
    # not because the subcommand is broken. `editor_status` (used here) is unaffected and
    # carries `playMode`, which is exactly why it is the PRIMARY gate.
    $esInfo = Get-EditorState
    # `compiling=true` is a TRANSIENT NORMAL state (team-lead ruling: measured during a
    # fully successful session) -- it is logged, never used as a blocking gate.
    $eiMsg = 'EDITOR-STATUS-INFO parsed=' + $esInfo.parsed + ' status=' + $esInfo.status + ' playMode=' + $esInfo.playMode + ' compiling=' + $esInfo.compiling + ' (transient-normal, not a gate)' + ' compilationFailed=' + $esInfo.compilationFailed + ' bytes=' + $esInfo.bytes
    Say $eiMsg

    $rc = Unity-Cmd @('recompile') -Quiet
    Say ('RECOMPILE ' + ($rc -replace "`r?`n", ' '))
    for ($i = 0; $i -lt 45; $i++) {
        Start-Sleep -Seconds 2
        $rs = Unity-Cmd @('recompile_status') -Quiet
        if ($rs -match '"compiling":\s*false' -or $rs -match 'up_to_date') { Say ('RECOMPILE-DONE try=' + ($i + 1)); break }
        if ($i -eq 44) { Say 'RECOMPILE-TIMEOUT (90s)' }
    }

    # READINESS GATE (rules 1+2): PRIMARY = `editor_status` (the only source that can see
    # `playMode`). `console --tail 1` is a FALLBACK used **only when the primary cannot be
    # parsed** -- never "OR" of the two (an OR lets console's playMode-blind view override a
    # `playMode=playing` verdict). `playMode=playing` here means somebody else's session has
    # not torn down: log it loudly and keep waiting; the driver reports its own state.
    $ready = $false
    for ($i = 0; $i -lt 10; $i++) {
        $esR = Get-EditorState
        if ($esR.parsed) {
            $reMsg = 'READINESS-EDITOR try=' + ($i + 1) + ' source=editor_status status=' + $esR.status + ' playMode=' + $esR.playMode + ' compiling=' + $esR.compiling + ' (transient-normal, not a gate)' + ' compilationFailed=' + $esR.compilationFailed
            Say $reMsg
            if ($esR.playMode -eq 'playing') { Say 'READINESS-PLAYING (editor is in Play -- not mine / not torn down; waiting, NOT sending editor_stop)' }
            if ($esR.playMode -eq 'stopped' -and $esR.compiling -eq 'false') { $ready = $true; break }
        } else {
            Say ('READINESS-FALLBACK reason=editor_status-unparsed bytes=' + $esR.bytes + ' => console --tail 1 (playMode-blind)')
            $cc = Unity-Cmd @('console', '--tail', '1') -Quiet
            $compiling = '?'
            $failed = '?'
            $bridgeOk = ($cc -match '"success":\s*true')
            $errCode = ''
            if ($cc -match '"code":\s*"([A-Z_]+)"') { $errCode = $Matches[1] }
            if ($cc -match '"compiling":\s*(true|false)') { $compiling = $Matches[1] }
            if ($cc -match '"compilationFailed":\s*(true|false)') { $failed = $Matches[1] }
            Say ('READINESS-CONSOLE try=' + ($i + 1) + ' bridge=' + $(if ($bridgeOk) { 'ok' } else { 'UNREACHABLE' }) + ' err=' + $errCode + ' compiling=' + $compiling + ' compilationFailed=' + $failed + ' bytes=' + $cc.Length)
            if ($bridgeOk -and $compiling -eq 'false' -and $failed -eq 'false') { $ready = $true; break }
        }
        Start-Sleep -Seconds 3
    }
    if (-not $ready) { Say 'READINESS-NOT-CONFIRMED (primary gate never reported stopped+not-compiling) => proceeding anyway (log-only gate); the driver reports its own state' }

    Unity-Cmd @('editor_play') -Quiet | Out-Null

    $playing = $false
    for ($i = 0; $i -lt 40; $i++) {
        Start-Sleep -Seconds 3
        $st = Unity-Cmd @('editor_status') -Quiet
        if ($st -match '"playMode":\s*"playing"') { $playing = $true; Say ('PLAY-MODE playing try=' + ($i + 1)); break }
    }
    if (-not $playing) { Say 'PLAY-MODE-NOT-REACHED (120s)' }
    Start-Sleep -Seconds 3

    $cfgOk = $false
    for ($i = 0; $i -lt 8; $i++) {
        $r = Run-Step 'U27.Api.Cfg' ''
        if ($r -match 'CFG ok|gameRunning=1') { $cfgOk = $true; break }
        Start-Sleep -Seconds 3
    }
    Say ('CFG-OK ' + $cfgOk)

    $spec = $Tag + '|g66|' + ($tsv -replace '\\', '/') + '|' + ($done -replace '\\', '/') + '|' + ($shot -replace '\\', '/')
    $raw = Unity-Cmd @('run_script', '--file', $cs, '--entry', 'U27.Tour.Install', '--args', ('[\"' + $spec + '\"]'))
    Say ('INSTALL ' + ($raw -replace "`r?`n", ' '))
    # README 5.2-36: `"success": true` + `"diagnostics": []` is NOT acceptance -- measured
    # case: a relative --file returns exactly that shape while the same response carries
    # `error="File Not Found"`. Acceptance = the ENTRY's return value shows up. ($cs is
    # absolute on purpose, and this line makes a silent non-install visible.)
    $instRes = '(none)'
    try { $jInst = $raw | ConvertFrom-Json; $instRes = [string]$jInst.data.result.result } catch { $instRes = '(PARSE-FAIL)' }
    $instErr = ''
    if ($raw -match '"error":\s*"([^"]+)"') { $instErr = $Matches[1] }
    if ($instRes -eq '' -or $instRes -eq '(none)' -or $instRes -eq '(PARSE-FAIL)') { Say 'WARN INSTALL-ACCEPTANCE-FAILED (no entry return value => the tour is NOT installed; TSV will be missing)' }
    $instMsg = 'INSTALL-RESULT result="' + $instRes + '" err="' + $instErr + '" file=' + $cs
    Say $instMsg

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

    Stop-Editor-Safe 'main'

    if (Test-Path $tsv) {
        $fi = Get-Item $tsv
        # README 5.2 (team-lead): `lines = ReadAllLines().Count` is the ONLY sanctioned
        # algorithm. This used to be a `Get-Content | Measure-Object` **-Line** count, which SILENTLY
        # SKIPS EMPTY LINES (select measured 327 vs 254 on the same file) -- it "looks like"
        # a line count and is a different quantity => stable but wrong pass/fail.
        $rows = [IO.File]::ReadAllLines($tsv).Count
        Say ('TSV-LANDED ' + $tsv + ' bytes=' + $fi.Length + ' lines(ReadAllLines)=' + $rows)
    } else { Say ('TSV-MISSING ' + $tsv) }

    if (Test-Path $shot) { Say ('SHOTS-LANDED png=' + @(Get-ChildItem $shot -Filter *.png -ErrorAction SilentlyContinue).Count) }
    else { Say 'SHOTS-MISSING' }

    [System.IO.File]::WriteAllLines($logCopy, [string[]]$script:lines, (New-Object System.Text.UTF8Encoding($false)))
    Say ('SUMMARY tag=' + $Tag + ' cfgOk=' + $cfgOk + ' playing=' + $playing + ' done=' + (Test-Path $done) + ' tsv=' + (Test-Path $tsv) + ' cli=' + $script:cli + ' tookOverStale=' + ($tookOver -ne ''))
    Say ('JUDGE-NEXT python tools/probes/drivers/d2u27_judge.py ' + $tsv)
} finally {
    # ROOT CAUSE of the 13:42-13:48 orphan Play (team-lead's evidence, d2u27-steps-u27v5.txt:
    # `RELEASE-WAIT playMode=playing try=1..5` -> `RELEASE-WARN ... releasing anyway` -> two
    # RELEASED, with NO editor_stop anywhere): `Stop-Editor-Safe 'pre'` at session START had
    # already set the one-shot `$script:stopped` flag, so 'main'/'finally' returned silently.
    # The flag only exists to prevent a DOUBLE stop inside the SHUTDOWN => reset it here, so
    # the shutdown sequence really performs trigger -> confirm -> hand over.
    $script:stopped = $false

    # README 5.2 (14b's UNSTABLE-AFTER-PARSE): if THIS script changed while it ran, every
    # reading emitted above judges a DIFFERENT revision than the one now on disk -- say so
    # explicitly instead of leaving the reader to guess which version each row belongs to.
    $selfShaEnd = (Get-FileHash -Algorithm SHA256 -LiteralPath $PSCommandPath).Hash.ToLower().Substring(0, 16)
    if ($selfShaEnd -ne $selfSha) {
        Say ('RUNNER-CHANGED-MID-RUN startup=' + $selfSha + ' now=' + $selfShaEnd + ' => every reading above judges the STARTUP revision')
    } else {
        Say ('RUNNER-UNCHANGED-MID-RUN sha256_16=' + $selfShaEnd)
    }

    # rule 4: only stop the editor while I still own the lock
    Stop-Editor-Safe 'finally'

    # rule 1 (team-lead): `editor_stop` returns BEFORE the editor has actually left Play
    # mode, so releasing the lock immediately opens a "no lock but still playing" window.
    # Poll (bounded 5 x 1s) for playMode=stopped BEFORE deleting the lock files.
    # README 5.2-42-2: `editor_stop` -> READ BACK playMode=stopped -> only THEN release the
    # two lock names. The 13:43 site exactly traced to this runner: the old fallback was
    # "5 x 1s then RELEASE-WARN and release anyway", and the editor was still playing
    # 13:43 while both locks were ABSENT (closefix's left-over Play). A silent hand-over is
    # what lets the next session walk into a live Play, so now: poll longer, retry
    # editor_stop once, and if it STILL will not leave Play -> HOLD the lock on purpose.
    $releaseOk = $true
    if ((StillMine) -eq $true) {
        $leftPlay = $false
        for ($k = 0; $k -lt 15; $k++) {
            $esF = Get-EditorState
            if (($k % 5) -eq 0) { Say ('RELEASE-WAIT playMode=' + $esF.playMode + ' parsed=' + $esF.parsed + ' try=' + ($k + 1)) }
            if ($esF.parsed -and ($esF.playMode -eq 'stopped')) { $leftPlay = $true; break }
            if (-not $esF.parsed) { Say ('RELEASE-UNKNOWN playMode unreadable (try ' + ($k + 1) + ') => NOT treated as stopped') }
            Start-Sleep -Seconds 2
        }
        if (-not $leftPlay) {
            Say 'RELEASE-RETRY playMode not stopped after 15x2s => re-issuing editor_stop via the single guarded entry'
            Stop-Editor-Safe 'retry' -Again
            for ($k2 = 0; $k2 -lt 8; $k2++) {
                Start-Sleep -Seconds 2
                $esG = Get-EditorState
                if ($esG.parsed -and ($esG.playMode -eq 'stopped')) { $leftPlay = $true; Say ('RELEASE-WAIT-2 playMode=stopped try=' + ($k2 + 1)); break }
            }
        }
        if (-not $leftPlay) {
            # README 5.2-42-1 (team-lead's REVERSED ruling, with the 13:42 evidence): do NOT
            # release. Releasing produced "two empty locks + playMode=playing" = an OWNERLESS
            # session that nobody was allowed to stop (the main agent had to do it 6.5 min
            # later). Holding keeps the ownership information, and it costs no time because a
            # correct gate refuses to start while playMode=playing anyway. The 12min stale
            # rule is the ONLY escape hatch.
            $pmF = Classify-Gate ((& unity command editor_status --format json --no-pager 2>&1 | Out-String))
            $o1 = Parse-Lock $lockMain
            $o2 = Parse-Lock $lockLegacy
            $oc1 = if ($o1.exists) { $o1.owner + '/' + $o1.pid } else { '(unreadable)' }
            $oc2 = if ($o2.exists) { $o2.owner + '/' + $o2.pid } else { '(unreadable)' }
            $releaseOk = $false
            Say ('ORPHAN-PLAY owner=' + $MyOwner + ' pid=' + $PID + ' playMode=' + $pmF + ' canonical=' + $oc1 + ' legacy=' + $oc2 + ' => editor could not be stopped after the bounded retries')
            Say ('LOCK-HELD-ON-PURPOSE / LOCK-RELEASE WITHHELD ' + $MyOwner + ' pid=' + $PID + ' (5.2-42-1: the session keeps a legal owner; the 12min stale rule is the only escape hatch; whoever takes this stale lock MUST editor_stop + confirm playMode=stopped before starting)')
            try {
                $alarm = 'ORPHAN-PLAY owner=' + $MyOwner + ' pid=' + $PID + ' playMode=' + $pmF + ' canonical=' + $oc1 + ' legacy=' + $oc2 + ' at=' + (Get-Date).ToString('o') + " - runner cannot message, so THIS FILE + the trace lines are the notification to the main agent" + "`r`n"
                [IO.File]::AppendAllText(($outDir + '/ORPHAN-PLAY-' + $PID + '.txt'), $alarm, (New-Object System.Text.UTF8Encoding($false)))
            } catch { Say ('ORPHAN-PLAY-ALARM-WRITE-FAILED ' + $_.Exception.Message) }
        }
    } else {
        Say 'RELEASE-WAIT skipped (lock is not mine => nothing to tear down)'
    }

    if ($releaseOk -eq $true) {
        # release BOTH names, and only a file whose content is mine
        foreach ($c in @($lockMain, $lockLegacy)) {
            $l = Parse-Lock $c
            if (-not $l.exists) { continue }
            if ($l.owner -eq $MyOwner -and $l.pid -eq $PID) {
                Remove-Item -LiteralPath $c -Force -ErrorAction SilentlyContinue
                if ($c -eq $lockMain) { Say ('LOCK-RELEASED (mine) ' + $c) } else { Say ('LEGACY-MIRROR-RELEASED (mine) ' + $c) }
            } else {
                if ($c -eq $lockMain) { Say ('LOCK-RELEASE REFUSED (owner=' + $l.owner + ' pid=' + $l.pid + ') left alone ' + $c) }
                else { Say ('LEGACY-MIRROR-RELEASE REFUSED (owner=' + $l.owner + ' pid=' + $l.pid + ') left alone ' + $c) }
            }
        }
    } else {
        Say ('LOCK-HELD-ON-PURPOSE owner=' + $MyOwner + ' pid=' + $PID + ' (playMode never reached stopped; see ORPHAN-PLAY above)')
    }
    Say 'END'
}
