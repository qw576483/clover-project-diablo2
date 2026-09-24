# =============================================================================
# shopgrid_run.ps1 -- ONE Play session for the shop-grid occupancy probe
#                    (judging asset of 片 impl-shop, same shape as x_run.ps1).
#
#   powershell -NoProfile -ExecutionPolicy Bypass -File shopgrid_run.ps1
#
# Fixed order: editor_stop -> settle any pending recompile -> clear_console -> editor_play
#   -> Byline.Api.Cfg -> ShopGrid.Api.Cfg -> ShopGrid.Tour.Run (zero-arg entry; the CLI
#   requires an argument for Install(string) and a JSON array for --args)
#   -> wait for the done marker -> editor_stop -> append play-log -> freeze the [SG]/[Npc] lines.
#
# Evidence written:
#   .ai-tmp/screenshots/shop_cells_buy.png / shop_cells_sell.png   (screen composite tiles)
#   .ai-tmp/screenshots/shopgrid_evidence.txt                      (frozen log window)
#   .ai-tmp/test/shopgrid_done.txt                                 (done marker)
# ASCII only (PS 5.1 reads a BOM-less non-ASCII .ps1 as ANSI).
# =============================================================================
param([switch]$LockSelfTest, [switch]$SelfCheckOnly)

$ErrorActionPreference = 'Continue'

# Team rule (2026-09-24, after jitter's accident): reading the self-check must NOT take a lock.
# Starting the real runner and killing it mid-flight leaves a ZOMBIE lock + a bogus play-log row
# (killed 9s in while the lock happened to be free => it took the lock, wrote the ledger line, and
# died before `finally`). This script's self-check already exits from inside `if ($LockSelfTest)`
# -- before ANY lock code in the file -- so `-SelfCheckOnly` is just the team-facing name of that
# same lock-free entry point. Never "start the real runner and kill it" to read a self-check.
if ($SelfCheckOnly) { $LockSelfTest = $true }

$root  = Split-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) -Parent
$proj  = Join-Path $root 'client'
$shots = Join-Path $root '.ai-tmp\screenshots'
$test  = Join-Path $root '.ai-tmp\test'
$done  = Join-Path $test 'shopgrid_done.txt'
$runlog = Join-Path $test 'shopgrid_runlog.txt'
$drv   = Join-Path $root 'tools\probes\drivers\shopgrid_drive.cs'
$byl   = Join-Path $root 'tools\probes\drivers\byline_drive.cs'
$playLog = Join-Path $test 'play-log.tsv'

# ---- self-check must not touch any path a NORMAL run writes (team rule 2026-09-24, README 5.2 #30,
# after zorder's `-SelfTest` silently overwrote a real step trace). Self-audit of THIS runner found
# exactly that shape: line "WriteAllText($runlog, ...)" RESETS the shared runlog on every invocation
# and the line after it DELETES the real done marker -- both happened in self-check mode too, which
# silently destroyed the shared copy of the 13:22 trace. In self-check mode everything is redirected
# to dedicated `*_selftest.*` paths; the normal-run paths (runlog / done / ledger / locks) are never
# opened for write. Verified by a before/after hash+mtime snapshot of those exact paths.
# README 5.2 #42-4: a "wait for state X" guard must be paired with an action that PUSHES the
# system into X. d2u27 waited 5x for playMode=stopped, warned, released -- and never CALLED
# editor_stop (its static "editor_stop appears once, inside the guard" assertion was green while
# the behaviour was missing). This counter makes the trigger step observable in the trace:
# a real run must end with `STOP-COUNTER editor_stopCalls>=1`; `0` means the tail skipped the
# trigger (an ORPHAN-PLAY alarm line above says why).
$editorStopCalls = 0
$selfCheckMode = [bool]$LockSelfTest
if ($selfCheckMode) {
    $runlog = Join-Path $test 'shopgrid_runlog_selftest.txt'
    $done   = Join-Path $test 'shopgrid_done_selftest.txt'
}
$why = 'sheet u53-shopart re-collect (lock-protocol-compliant session; R-1 label row + R2-a slot row in ONE session). R-1: the Chinese labels sit OUTSIDE the button rect so the original glyphs (frame 2 hammer+anvil / frame 10 circled slash) are unobstructed -- only the live Label RectTransform shows the realised button-local rect and whether label vs button Overlap. R2-a: the buttons were re-centred into the carved slot INNER RECESS (SlotCenter, original y 399 instead of 381 = the slot top edge, 18 orig px too high) -- only the live Image anchoredPosition shows the realised centre, the distance to SlotCenter and whether the rect is inside SlotInnerRect. Plus the bottom-bar screenshot for both rows.'

function Say([string]$s) {
    $stamp = (Get-Date).ToString('HH:mm:ss.fff')
    $line = $stamp + ' ' + $s
    # Write-HOST, never Write-Output: a printing helper that writes to the success pipeline
    # pollutes the return value of any function that calls it, and PS then reads
    # `if (SomeFunc)` as "the whole output array" => non-empty array is ALWAYS TRUE
    # (that is how u52play lost a lock race and still called editor_play = false exclusivity).
    Write-Host $line
    # logs: UTF-8 WITHOUT BOM append, absolute path (the .NET API resolves relative paths
    # against the process cwd, not $PWD -- README 5.2 #15).
    try { [IO.File]::AppendAllText($runlog, $line + "`r`n", (New-Object Text.UTF8Encoding($false))) } catch { }
}

function UC([string[]]$argv) {
    $raw = & unity command @argv --project-path $proj --format json --no-pager 2>&1 | Out-String
    return $raw
}

foreach ($d in @($shots, $test)) { if (-not (Test-Path $d)) { New-Item -ItemType Directory -Path $d | Out-Null } }
[IO.File]::WriteAllText($runlog, "# shopgrid run log`r`n", (New-Object Text.UTF8Encoding($false)))
if (Test-Path $done) { Remove-Item $done -Force }

# ---- runner fingerprint (team rule 2026-09-24): the trace must carry the EXACT revision it
# ran, because hand-pasted fingerprints go stale within minutes and make reviewers read the
# wrong lines (four "stale snapshot" false-reds in one day). Line numbers quoted anywhere must
# be resolved against THIS line's output, not against a hand-copied hash.
try {
    $selfBytes = [IO.File]::ReadAllBytes($PSCommandPath)
    $selfSha = [System.Security.Cryptography.SHA256]::Create().ComputeHash($selfBytes)
    $selfHex = -join ($selfSha[0..7] | ForEach-Object { $_.ToString('x2') })
    # TEAM CONVENTION (2026-09-24): `lines=` means LOGICAL lines = ReadAllLines().Count, and the
    # algorithm is named in the output. Why it matters: count(LF) differs by 1 on a
    # newline-terminated file and count(LF)+1 invents a line, which once made one file look like
    # two different versions ("913 vs 914" at the same hash).
    $selfLines = [System.IO.File]::ReadAllLines($PSCommandPath).Count
    # ⛔ single line on purpose: a multi-line `Say (` continuation broke PS 5.1 parsing THREE
    # times in this runner (this was the third) -- keep it as one expression.
    $fpLine = 'RUNNER-FINGERPRINT sha256_16=' + $selfHex + ' bytes=' + $selfBytes.Length + ' lines=' + $selfLines + '(ReadAllLines) mtime=' + (Get-Item $PSCommandPath).LastWriteTime.ToString('yyyy-MM-dd HH:mm:ss') + ' (authoritative for THIS trace; verified by re-computing)'
    Say $fpLine
} catch {
    Say ('RUNNER-FINGERPRINT unavailable: ' + $_.Exception.Message)
}

$logPath = Join-Path $proj 'Logs\Editor.log'
$off = 0
if (Test-Path $logPath) { $off = (Get-Item $logPath).Length }

# =============================================================================
# PLAY LOCK -- protocol v2 (team-wide, 2026-09-24). Content: "<owner> <ISO8601> <PID>".
#   * the lock is the ONLY right to touch the editor: editor_stop / recompile /
#     editor_play run strictly AFTER LOCK-TAKEN and strictly while the lock is mine;
#   * an existing lock that is NOT mine, whose PID is alive and age < 12 min =>
#     GIVE WAY (retry every 3 min, up to 21 min) then ABORT WITHOUT touching the editor;
#   * stale (PID dead OR age >= 12 min) => takeover, say so in the report;
#   * play-log is appended ONLY AFTER the lock is taken (a line for a session that
#     never started makes the ledger lie -- that is what happened on 2026-09-24 11:25/11:30);
#   * release only MY OWN lock, on every path.
# =============================================================================
$lock = Join-Path $test 'play-running.lock'
$me = 'u53-shopart'
$staleMin = 12

function Get-LockBody([string]$p) {
    if (-not (Test-Path $p)) { return '' }
    $raw = ''
    try { $raw = (Get-Content $p -Raw) } catch { return '' }
    if ($null -eq $raw) { return '' }
    # BOM / zero-width chars MUST be stripped: `Set-Content -Encoding UTF8` writes a BOM on
    # PS 5.1, so the owner field would read as U+FEFF<owner> and we would fail to recognise
    # our OWN lock (zombie lock + refuse to stop the editor). Normalise whitespace too.
    $raw = $raw.Replace([char]0xFEFF, ' ').Replace([char]0x200B, ' ')
    $raw = $raw -replace '[\r\n\t]', ' '
    return $raw.Trim()
}
function Get-LockAge([string]$p) {
    if (-not (Test-Path $p)) { return -1.0 }
    return [math]::Round(((Get-Date) - (Get-Item $p).LastWriteTime).TotalMinutes, 1)
}
function Write-MyLock() {
    # ASCII (no BOM) on purpose: the lock CONTENT must never carry a BOM.
    Set-Content -Path $lock -Value ($me + ' ' + (Get-Date).ToString('o') + ' ' + $PID) -Encoding ASCII
}
function Get-EditorPlayState() {
    # FAIL-CLOSED readiness probe (team rule, 2026-09-24). Returns:
    #   'playing'      -> an editor session is running  => never take the lock / never stop it
    #   'stopped'      -> safe to start Play
    #   'unknown'      -> the playMode field could not be read (missing / empty / unparseable)
    #                     => UNKNOWN != stopped => yield (never rely on a default value)
    #   'bridge-down'  -> editor_status answered success:false / COMMAND_FAILED, i.e. there is
    #                     no editor process at all (an absent editor cannot be playing) =>
    #                     allowed to continue, but only after a bounded 3 x 5s retry.
    $sawBridgeDown = $false
    for ($k = 1; $k -le 3; $k++) {
        $stm = UC @('editor_status')
        $txt = ($stm -join "`n")
        if ($txt -match '"playMode":\s*"([A-Za-z]+)"') {
            $pm = $Matches[1].ToLower()
            if ($pm -eq 'playing') { return 'playing' }
            if ($pm -eq 'stopped') { return 'stopped' }
            return 'unknown'
        }
        if (($txt -match 'COMMAND_FAILED') -or ($txt -match '"success":\s*false')) { $sawBridgeDown = $true }
        Start-Sleep -Seconds 5
    }
    if ($sawBridgeDown) { return 'bridge-down' }
    return 'unknown'
}
function Wait-PlayStopped([int]$polls = 15) {
    # `editor_stop`'s CLI can return BEFORE the editor really left Play mode (it restores the
    # backup scene), which produced "lock free but playMode=playing" windows. Poll, bounded --
    # never hang. WIDENED 2026-09-24 (#42-3): 5x1s was too short; the measured disease was
    # "editor_stop never CALLED", not "called but slow" (one call exits play mode instantly).
    # Returns $true ONLY when playMode was read back as `stopped` (never "assume it worked").
    for ($k = 1; $k -le $polls; $k++) {
        $pm = Get-EditorPlayState
        if ($pm -eq 'stopped') { Say ('PLAY-STOPPED after ' + $k + ' poll(s)'); return $true }
        Start-Sleep -Seconds 2
    }
    Say ('PLAY-WAIT-TIMEOUT playMode not stopped within ' + $polls + ' poll(s) x2s')
    return $false
}
function Stop-Editor-Safe([string]$tag) {
    # The ONLY place in this runner that may call editor_stop. If the lock is no longer
    # mine we must NOT touch the editor -- that is exactly how a session gets interrupted.
    # Returns $true when the lock may be handed over (playMode confirmed `stopped`, or the editor
    # is someone else's), $false when the lock must be KEPT: #42-1 forbids releasing while the
    # session is still in Play (that is what created the 13:42 owner-less session nobody but the
    # lead could legally clean up). `playing` cannot be worked around by ANY correct gate, so
    # holding the lock costs no extra time.
    if ((Test-MyLock) -eq $true) {
        for ($try = 1; $try -le 3; $try++) {
            UC @('editor_stop') | Out-Null
            $script:editorStopCalls++
            Say ('EDITOR-STOPPED tag=' + $tag + ' (trigger fired; call#=' + $editorStopCalls + ' attempt=' + $try + '/3)')
            if ((Wait-PlayStopped 15) -eq $true) { return $true }
            Say ('EDITOR-STOP-RETRY tag=' + $tag + ' attempt=' + $try + '/3 playMode still not stopped -> calling editor_stop again')
        }
        $oOwner = ''; $oPid = -1
        try { if (Test-Path $lock) { $oBody = (Get-Content $lock -Raw); $oOwner = Lock-Owner $oBody; $oPid = Lock-Pid $oBody } } catch { }
        $oPm = Get-EditorPlayState
        Say ('ORPHAN-PLAY tag=' + $tag + ' lockOwner=' + $(if ($oOwner -ne '') { $oOwner } else { '(unreadable)' }) + ' ownerPid=' + $oPid + ' playMode=' + $oPm + ' editor_stopCalls=' + $editorStopCalls + ' -> NOTIFY TEAM-LEAD (this must not stay only in the trace)')
        Say ('LOCK-HELD-ON-PURPOSE lockOwner=' + $me + ' pid=' + $PID + ' tag=' + $tag + ' -- LOCK-RELEASE WITHHELD: the session is still in Play, so releasing would hand over an owner-less session; held under the 12min stale rule, and the TAKER must editor_stop + confirm stopped before its own session')
        return $false
    }
    # Lock is not mine => someone else owns the editor: never touch it, but alarm loudly.
    $xOwner = ''; $xPid = -1
    try { if (Test-Path $lock) { $xBody = (Get-Content $lock -Raw); $xOwner = Lock-Owner $xBody; $xPid = Lock-Pid $xBody } } catch { }
    $xPm = Get-EditorPlayState
    Say ('ORPHAN-PLAY tag=' + $tag + ' lockOwner=' + $(if ($xOwner -ne '') { $xOwner } else { '(unreadable)' }) + ' ownerPid=' + $xPid + ' playMode=' + $xPm + ' -- editor_stop SKIPPED (lock is not mine); a following taker must NOT assume a clean session')
    Say ('SKIP editor_stop tag=' + $tag + ' -> lock no longer mine; readings = suspect')
    return $true
}
function Lock-Owner([string]$body) {
    if ([string]::IsNullOrWhiteSpace($body)) { return '' }
    return ($body.Trim() -split '\s+')[0]
}
function Lock-Pid([string]$body) {
    $f = $body.Trim() -split '\s+'
    if ($f.Length -lt 3) { return -1 }
    $n = 0
    if ([int]::TryParse($f[2], [ref]$n)) { return $n }
    return -1
}
function Lock-Alive([int]$p) {
    if ($p -le 0) { return $false }
    return $null -ne (Get-Process -Id $p -ErrorAction SilentlyContinue)
}
function Release-Lock() {
    # Release BOTH names, but only the entries whose content is MINE (someone else's lock is
    # never deleted). Required by v2.2/v2.3 -- the mirror must go away with the real lock.
    foreach ($p in @($lock, $legacyLock)) {
        $nm = Split-Path $p -Leaf
        $b = Get-LockBody $p
        $o = Lock-Owner $b
        if ($o -eq '') { Say ('LOCK-RELEASE skip name=' + $nm + ' absent'); continue }
        if ($o -eq $me) {
            Remove-Item $p -Force -ErrorAction SilentlyContinue
            Say ('LOCK-RELEASED (mine) name=' + $nm)
        } else {
            Say ('LOCK-RELEASE REFUSED name=' + $nm + ' owner=' + $o + ' -> left alone')
        }
    }
}
function Write-Mirror() {
    # v2.2 (corrected 2026-09-24 by the team lead): the mirror is NOT for the new runners
    # -- they judge the canonical name anyway. Its ONLY purpose is to block the 17 LEGACY
    # runners that read ONLY play.lock: without it they would see "no lock" and could start
    # a second Play session. Reading both names protects ME; writing the mirror protects
    # OTHERS. Same 3 fields, ASCII (no BOM), written right after the real lock is taken.
    Set-Content -Path $legacyLock -Value ($me + ' ' + (Get-Date).ToString('o') + ' ' + $PID) -Encoding ASCII
    Say ('LEGACY-MIRROR-WRITTEN name=' + (Split-Path $legacyLock -Leaf) + ' owner=' + $me + ' pid=' + $PID)
}
function Test-MyLock() { return ((Lock-Owner (Get-LockBody $lock)) -eq $me) }

# Protocol v2.1: a MISSING PID field means UNKNOWN, never "dead" -- a fresh 2-field lock
# (written by an older runner) must still be respected, otherwise we instantly steal a
# perfectly fresh lock (that is the 11:42 accident, same shape). Takeover ONLY when
# age >= 12 min, or when the PID is present AND that process is gone.
function Lock-Verdict([string]$body, [double]$ageMin) {
    if ($ageMin -ge $staleMin) { return 'takeover' }
    $p = Lock-Pid $body
    if ($p -le 0) { return 'busy' }          # v2.1: no PID field + fresh => give way
    if (Lock-Alive $p) { return 'busy' }
    return 'takeover'
}

# Static judge over CODE lines (comments must be dropped by the caller FIRST, otherwise a
# comment mentioning editor_play produces a FALSE RED -- the exact trap charstat hit).
# Returns a string starting with True/False. Two-way self-tested below (known-good + known-bad).
function Test-StaticLayout([string[]]$code) {
    $fnStart = -1; $fnEnd = -1
    for ($k = 0; $k -lt $code.Count; $k++) {
        if ($fnStart -lt 0 -and $code[$k] -match 'function Stop-Editor-Safe') { $fnStart = $k + 1; continue }
        if ($fnStart -gt 0 -and $fnEnd -lt 0 -and $code[$k] -match '^\}') { $fnEnd = $k + 1; break }
    }
    # Patterns are the REAL call shapes (`UC @('editor_stop')`), NOT the bare word: a bare
    # word also matches prose (comments -- dropped by the caller) and the self-test sample
    # strings (dropped by the caller too). Both drop-steps are what make this judge honest.
    $in = 0; $out = 0; $take = 0; $play = 0; $rec = 0
    for ($k = 0; $k -lt $code.Count; $k++) {
        $ln = $k + 1
        if ($code[$k] -match "@\('editor_stop'\)") {
            if ($fnStart -gt 0 -and $ln -ge $fnStart -and $ln -le $fnEnd) { $in++ } else { $out++ }
        }
        if ($take -eq 0 -and $code[$k] -match '^\s+Write-MyLock\s*$') { $take = $ln }
        if ($play -eq 0 -and $code[$k] -match "@\('editor_play'\)") { $play = $ln }
        if ($rec -eq 0 -and $code[$k] -match "@\('recompile'\)") { $rec = $ln }
    }
    # `-ge 1` (not `-eq 1`): since #42-3 the shutdown CALLS editor_stop in a bounded retry loop, so
    # more than one call form is legitimate. What must stay true is the SAFETY property: every call
    # is inside the guarded function (`$out -eq 0`) and the editor is stopped before play/recompile.
    # (#45: count the CALL FORM, never the bare word -- prose containing "editor_stop" must not move
    #  these numbers at all, which is why the pattern above is `@\('editor_stop'\)`.)
    $ok = ($in -ge 1 -and $out -eq 0 -and $take -gt 0 -and $play -gt $take -and $rec -gt $take)
    return ($ok.ToString() + ' editor_stopInFn=' + $in + ' editor_stopOutside=' + $out + ' writeLockLine=' + $take + ' editor_playLine=' + $play + ' recompileLine=' + $rec)
}

if ($LockSelfTest) {
    # Self-check output goes to a PER-PID file: a fixed path would be shared state, and two
    # sheets self-testing at the same time would delete/overwrite each other's log (false red
    # / false green). Removed in the finally below.
    $runlog = Join-Path $test ('u53shop-lockselftest-' + $PID + '.txt')
    try {
    Say 'LOCK-SELFTEST (pure decision table, no editor access)'
    $cases = @(
        @{ n = 'fresh-3field-live-pid'; b = ('other 2026-09-24T11:00:00 ' + $PID); a = 2.0;  want = 'busy' },
        @{ n = 'fresh-3field-dead-pid'; b = 'other 2026-09-24T11:00:00 999999';    a = 2.0;  want = 'takeover' },
        @{ n = 'fresh-NO-pid(v2.1)';    b = 'other 2026-09-24T11:00:00';           a = 2.0;  want = 'busy' },
        @{ n = 'fresh-garbage-no-pid';  b = 'whatever';                            a = 0.5;  want = 'busy' },
        @{ n = 'stale-NO-pid-13m';      b = 'other 2026-09-24T11:00:00';           a = 13.0; want = 'takeover' },
        # boundary: 11.9 min must still YIELD -- this is the case that breaks if the age
        # parameter is [int] (PS casts 11.9 -> 12 and a fresh lock gets taken over).
        @{ n = 'no-pid-age11.9-YIELD';  b = 'other 2026-09-24T11:00:00';           a = 11.9; want = 'busy' },
        @{ n = 'stale-3field-live-pid'; b = ('other 2026-09-24T11:00:00 ' + $PID); a = 13.0; want = 'takeover' },
        @{ n = 'stale-3field-dead-pid'; b = 'other 2026-09-24T11:00:00 999999';    a = 13.0; want = 'takeover' },
        @{ n = 'body-empty => free';    b = '';                                    a = 1.0;  want = 'free' },
        @{ n = 'owner-mine => mine';    b = ('u53-shopart 2026-09-24T11:00:00 ' + $PID); a = 1.0; want = 'mine' }
    )
    $fail = 0
    foreach ($c in $cases) {
        $o = Lock-Owner $c.b
        $got = 'free'
        if ($o -eq $me) { $got = 'mine' } elseif ($o -ne '') { $got = Lock-Verdict $c.b $c.a }
        $ok = ($got -eq $c.want)
        if (-not $ok) { $fail++ }
        Say (($(if ($ok) { '[ OK ]' } else { '[FAIL]' })) + ' selftest ' + $c.n + ' => ' + $got + ' (want ' + $c.want + ')')
    }
    # --- encoding self-check (must be able to FAIL, not "I remember writing ASCII") ------
    $bytes = [System.IO.File]::ReadAllBytes($PSCommandPath)
    $nonAscii = 0
    foreach ($b in $bytes) { if ($b -gt 127) { $nonAscii++ } }
    $hasBom = ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF)
    $encOk = (($nonAscii -eq 0) -or $hasBom)
    if (-not $encOk) { $fail++ }
    Say ('[ ' + $(if ($encOk) { 'OK' } else { 'FAIL' }) + ' ] selftest encoding: nonAsciiBytes=' + $nonAscii + ' utf8Bom=' + $hasBom + ' (rule: pure ASCII OR UTF-8 with BOM)')

    # --- static layout judge + two-way self-test (known-good sample AND known-bad) ------
    # Drop comments FIRST (a comment mentioning editor_play => FALSE RED, the trap charstat
    # hit) and then drop this very self-test block (its sample strings would otherwise be
    # counted as real calls => another false reading).
    $allLines = @(Get-Content $PSCommandPath | Where-Object { $_ -notmatch '^\s*#' })
    $codeReal = @()
    $inSelf = $false
    foreach ($l in $allLines) {
        if (-not $inSelf -and $l -match '^if \(\$LockSelfTest\)') { $inSelf = $true; continue }
        if ($inSelf) { if ($l -match '^\}') { $inSelf = $false }; continue }
        $codeReal += $l
    }
    $rReal = Test-StaticLayout $codeReal
    $good = @('function Stop-Editor-Safe([string]$t) {', '    if (Test-MyLock) {', "        UC @('editor_stop') | Out-Null", '    }', '}', '    Write-MyLock', "    UC @('editor_play')", "    UC @('recompile')")
    $rGood = Test-StaticLayout $good
    $bad = @('function Stop-Editor-Safe([string]$t) {', "    UC @('editor_stop') | Out-Null", '}', '    Write-MyLock', "    UC @('editor_play')", "    UC @('recompile')", "    UC @('editor_stop') | Out-Null  tail-unguarded")
    $rBad = Test-StaticLayout $bad
    $okReal = $rReal.StartsWith('True')
    $okGood = $rGood.StartsWith('True')
    $okBad = $rBad.StartsWith('False')
    if (-not $okReal) { $fail++ }
    if (-not $okGood) { $fail++ }
    if (-not $okBad) { $fail++ }
    Say ('[ ' + $(if ($okReal) { 'OK' } else { 'FAIL' }) + ' ] selftest static-layout(real runner): ' + $rReal)
    Say ('[ ' + $(if ($okGood) { 'OK' } else { 'FAIL' }) + ' ] selftest static-layout(known-GOOD sample): ' + $rGood)
    Say ('[ ' + $(if ($okBad) { 'OK' } else { 'FAIL' }) + ' ] selftest static-layout(known-BAD sample): ' + $rBad)

    # --- transition rule: the legacy name must be READ in code -------------------------
    $readsLegacy = (($codeReal -join "`n") -match 'play\.lock')
    if (-not $readsLegacy) { $fail++ }
    Say ('[ ' + $(if ($readsLegacy) { 'OK' } else { 'FAIL' }) + ' ] selftest legacy-lock-name-is-read: play.lock mentioned in code=' + $readsLegacy)

    # --- v2.2 corrected: READING both names is not WRITING the mirror (audit them separately)
    $codeTxt = ($codeReal -join "`n")
    # NOTE `(?m)`: $codeTxt is ONE string joined by newlines, and .NET anchors ^/$ only match
    # the whole string without the Multiline option (that mistake made this check false-negative
    # once -- a "gate that cannot go green" is as useless as one that cannot go red).
    $writesMirror = ($codeTxt -match 'LEGACY-MIRROR-WRITTEN') -and
                    ($codeTxt -match 'Set-Content -Path \$legacyLock[^\r\n]*-Encoding ASCII') -and
                    ($codeTxt -match '(?m)^\s+Write-Mirror\s*$')
    if (-not $writesMirror) { $fail++ }
    Say ('[ ' + $(if ($writesMirror) { 'OK' } else { 'FAIL' }) + ' ] selftest mirror-is-WRITTEN (not just read): LEGACY-MIRROR-WRITTEN + ASCII write of $legacyLock + call site = ' + $writesMirror)

    $relStart = -1; $relEnd = -1
    for ($k = 0; $k -lt $codeReal.Count; $k++) {
        if ($relStart -lt 0 -and $codeReal[$k] -match 'function Release-Lock') { $relStart = $k + 1; continue }
        if ($relStart -gt 0 -and $relEnd -lt 0 -and $codeReal[$k] -match '^\}') { $relEnd = $k + 1; break }
    }
    $relBody = ''
    if ($relStart -gt 0) { $relBody = ($codeReal[($relStart - 1)..($relEnd - 1)] -join "`n") }
    $relBoth = $relBody.Contains('$lock') -and $relBody.Contains('$legacyLock')
    if (-not $relBoth) { $fail++ }
    Say ('[ ' + $(if ($relBoth) { 'OK' } else { 'FAIL' }) + ' ] selftest Release-Lock covers BOTH names: ' + $relBoth + ' lines=' + $relStart + '..' + $relEnd)

    # --- v2.4: the readiness gate must NOT be `unity status` (empty table on this machine) --
    $usesBrokenStatus = ($codeTxt -match 'unity status')
    $usesGroundTruth = ($codeTxt -match 'EDITOR-GROUNDTRUTH') -and ($codeTxt -match "console', '--tail'")
    $gateOk = ((-not $usesBrokenStatus) -and $usesGroundTruth)
    if (-not $gateOk) { $fail++ }
    Say ('[ ' + $(if ($gateOk) { 'OK' } else { 'FAIL' }) + ' ] selftest readiness-gate: uses console ground truth=' + $usesGroundTruth + ' , uses broken `unity status`=' + $usesBrokenStatus)

    # --- u52play bug class: a printing helper must NOT write to the success pipeline, and a
    #     lock call must never be used as a bare command-condition (non-empty array is truthy).
    $sayStart = -1; $sayEnd = -1
    for ($k = 0; $k -lt $codeReal.Count; $k++) {
        if ($sayStart -lt 0 -and $codeReal[$k] -match 'function Say\(') { $sayStart = $k + 1; continue }
        if ($sayStart -gt 0 -and $sayEnd -lt 0 -and $codeReal[$k] -match '^\}') { $sayEnd = $k + 1; break }
    }
    $sayBody = ''
    if ($sayStart -gt 0) { $sayBody = ($codeReal[($sayStart - 1)..($sayEnd - 1)] -join "`n") }
    $sayOk = ($sayBody -match 'Write-Host') -and (-not ($sayBody -match 'Write-Output'))
    if (-not $sayOk) { $fail++ }
    Say ('[ ' + $(if ($sayOk) { 'OK' } else { 'FAIL' }) + ' ] selftest say-uses-WriteHost (no Write-Output in Say, lines=' + $sayStart + '..' + $sayEnd + '): ' + $sayOk)

    $bareCond = ($codeTxt -match 'if \(Test-MyLock\)') -or ($codeTxt -match 'if \(-not \(Test-MyLock\)\)')
    $explicit = ($codeTxt -match '\(Test-MyLock\) -eq \$true')
    $condOk = ((-not $bareCond) -and $explicit)
    if (-not $condOk) { $fail++ }
    Say ('[ ' + $(if ($condOk) { 'OK' } else { 'FAIL' }) + ' ] selftest lock-calls-compared-explicitly: bareForm=' + $bareCond + ' explicitForm=' + $explicit)

    # --- team rules: fail-closed playMode gate + bounded release poll (both must exist) ----
    $gateFailClosed = ($codeTxt -match 'LOCK-GATE-UNKNOWN') -and ($codeTxt -match 'LOCK-GATE-BRIDGE-DOWN') -and
                      ($codeTxt -match 'LOCK-GATE-PLAYMODE') -and ($codeTxt -match 'editor_status') -and
                      ($codeTxt -match 'registered TAKEOVER')
    if (-not $gateFailClosed) { $fail++ }
    Say ('[ ' + $(if ($gateFailClosed) { 'OK' } else { 'FAIL' }) + ' ] selftest playmode-gate-FAIL-CLOSED (playing|unknown => yield, bridge-down distinct): ' + $gateFailClosed)

    $releasePoll = ($codeTxt -match 'PLAY-STOPPED after') -and ($codeTxt -match 'PLAY-WAIT-TIMEOUT') -and
                   ($codeTxt -match 'function Wait-PlayStopped')
    if (-not $releasePoll) { $fail++ }
    Say ('[ ' + $(if ($releasePoll) { 'OK' } else { 'FAIL' }) + ' ] selftest release-polls-playMode-stopped (bounded, never hangs): ' + $releasePoll)

    # --- team rule 2026-09-24: `-SelfCheckOnly` must be an ALIAS of this lock-free entry, and the
    #     mapping must be a real assignment (not a comment) => assert the exact code shape.
    $aliasMapped = ($codeTxt -match '\$SelfCheckOnly') -and ($codeTxt -match 'if \(\$SelfCheckOnly\) \{ \$LockSelfTest = \$true \}')
    if (-not $aliasMapped) { $fail++ }
    Say ('[ ' + $(if ($aliasMapped) { 'OK' } else { 'FAIL' }) + ' ] selftest selfcheckonly-alias-maps-to-lock-free-entry: ' + $aliasMapped)

    # --- team rule 2026-09-24 (README 5.2 #30): the self-check must not write/delete any path the
    #     normal run writes => it must be redirected to dedicated *_selftest.* paths.
    #    Live assertion on the REAL variables (same paragraph/instant as the values it judges --
    #    README 5.2 #28), NOT on the source text: this very needle's own literal sits inside the
    #    block the scanner drops, so a text scan here would be a self-referential false red.
    #    Property under test: neither live path may BE the shared/normal one. The block is free to
    #    divert the runlog further (it uses a per-PID file); any non-normal target is fine.
    $selfRedir = ($selfCheckMode -eq $true) -and ($runlog -notmatch '[\\/]shopgrid_runlog\.txt$') -and ($done -notmatch '[\\/]shopgrid_done\.txt$') -and ($done -match '_selftest\.txt$') -and ($runlog -notmatch 'runlog\.txt$')
    if (-not $selfRedir) { $fail++ }
    Say ('[ ' + $(if ($selfRedir) { 'OK' } else { 'FAIL' }) + ' ] selftest selfcheck-writes-no-normal-run-path: live runlog=' + (Split-Path $runlog -Leaf) + ' done=' + (Split-Path $done -Leaf))

    # --- README 5.2 #42: skipping editor_stop (lock not mine) must carry an ORPHAN-PLAY alarm with
    #     owner / pid / playMode readback, otherwise the next taker inherits an unknown session.
    $orphanAlarm = ($codeTxt -match 'ORPHAN-PLAY') -and ($codeTxt -match 'ownerPid=') -and ($codeTxt -match 'playMode=') -and ($codeTxt -match 'Get-EditorPlayState')
    if (-not $orphanAlarm) { $fail++ }
    Say ('[ ' + $(if ($orphanAlarm) { 'OK' } else { 'FAIL' }) + ' ] selftest skip-editor_stop-carries-ORPHAN-PLAY-alarm (owner/pid/playMode): ' + $orphanAlarm)

    # --- README 5.2 #42-4: the shutdown must CALL the trigger (editor_stop), not merely "wait" --
    #     d2u27 waited 5x, warned and released without ever calling editor_stop. The trace must
    #     therefore carry an observable call counter, emitted on the same path as the release.
    #    Plus the ORDER on the shutdown path: trigger -> waited-for readback -> handover.
    $iFinal   = $codeTxt.LastIndexOf("Stop-Editor-Safe 'final'", [StringComparison]::Ordinal)
    $iCounter = $codeTxt.LastIndexOf('STOP-COUNTER editor_stopCalls=', [StringComparison]::Ordinal)
    $iRelease = $codeTxt.LastIndexOf('Release-Lock', [StringComparison]::Ordinal)
    $orderOk = ($iFinal -ge 0) -and ($iCounter -gt $iFinal) -and ($iRelease -gt $iCounter)
    #    NOTE the increment is written `$script:editorStopCalls++`, so the regex must NOT anchor a
    #    literal `$` right before the name (that mistake made this needle red once).
    $triggerCounter = ($codeTxt -match 'editorStopCalls\+\+') -and ($codeTxt -match 'STOP-COUNTER editor_stopCalls=') -and ($codeTxt -match "editor_stop'\)") -and $orderOk
    if (-not $triggerCounter) { $fail++ }
    Say ('[ ' + $(if ($triggerCounter) { 'OK' } else { 'FAIL' }) + ' ] selftest shutdown-CALLS-the-trigger, in order trigger->readback->release: ' + $triggerCounter + ' idx final=' + $iFinal + ' counter=' + $iCounter + ' release=' + $iRelease)

    # --- README 5.2 #42-1 (UNIFIED RULING, supersedes "release + alarm"): when editor_stop was
    #     retried and playMode is STILL not stopped, the lock must be KEPT (an owner-less play
    #     session is what forced the lead to clean up at 13:42). Assert the withhold shape.
    $withhold = ($codeTxt.IndexOf('ORPHAN-PLAY', [StringComparison]::Ordinal) -ge 0) -and ($codeTxt.IndexOf('LOCK-HELD-ON-PURPOSE', [StringComparison]::Ordinal) -ge 0) -and ($codeTxt.IndexOf('LOCK-RELEASE WITHHELD', [StringComparison]::Ordinal) -ge 0) -and ($codeTxt.IndexOf('if ($finalStopOk -eq $true) { Release-Lock', [StringComparison]::Ordinal) -ge 0) -and ($codeTxt.IndexOf('exit 3', [StringComparison]::Ordinal) -ge 0)
    if (-not $withhold) { $fail++ }
    Say ('[ ' + $(if ($withhold) { 'OK' } else { 'FAIL' }) + ' ] selftest orphan-play-WITHHOLDS-the-lock (ORPHAN-PLAY + LOCK-HELD-ON-PURPOSE + release guarded by $finalStopOk + exit 3): ' + $withhold)

    # --- README 5.2 #42-2 (TAKER DUTY): whoever takes over a stale/absent lock while the editor
    #     is still in Play must editor_stop and CONFIRM stopped before its own session.
    $takerDuty = ($codeTxt.IndexOf('registered TAKEOVER', [StringComparison]::Ordinal) -ge 0) -and ($codeTxt.IndexOf('editor_stop + confirm stopped before my own session', [StringComparison]::Ordinal) -ge 0) -and ($codeTxt.IndexOf('$initStopOk -ne $true', [StringComparison]::Ordinal) -ge 0)
    if (-not $takerDuty) { $fail++ }
    Say ('[ ' + $(if ($takerDuty) { 'OK' } else { 'FAIL' }) + ' ] selftest taker-duty (takeover registers + post-lock stop must confirm stopped, else abort+withhold): ' + $takerDuty)

    # --- TEAM CHECK (u52resist shape, README 5.2 #42): EXISTENCE != REACHABILITY. d2u27's
    #     `editor_stop` existed exactly once inside its guard (existence PASS) yet the normal
    #     shutdown never walked that path => both locks were released while the editor stayed in
    #     Play for 6.5 min. So: AST-find every call to the shutdown GUARD and require >= 1 on an
    #     UNCONDITIONAL path (no IfStatementAst ancestor). Fail-closed: parse failure = FAIL.
    $astErrs = $null
    $astTree = [System.Management.Automation.Language.Parser]::ParseFile($PSCommandPath, [ref]$null, [ref]$astErrs)
    $uncondGuardCalls = 0
    if ($astErrs.Count -eq 0) {
        foreach ($cm in $astTree.FindAll({ param($n) $n -is [System.Management.Automation.Language.CommandAst] }, $true)) {
            if ($cm.GetCommandName() -ne 'Stop-Editor-Safe') { continue }
            $anc = $cm.Parent; $cond = $false
            while ($anc) { if ($anc -is [System.Management.Automation.Language.IfStatementAst]) { $cond = $true; break }; $anc = $anc.Parent }
            if (-not $cond) { $uncondGuardCalls++ }
        }
    } else { $uncondGuardCalls = -1 }
    $reachOk = ($astErrs.Count -eq 0) -and ($uncondGuardCalls -ge 1)
    if (-not $reachOk) { $fail++ }
    Say ('[ ' + $(if ($reachOk) { 'OK' } else { 'FAIL' }) + ' ] SELF-CHK stop-trigger-reachable unconditionalStopGuardCalls=' + $uncondGuardCalls + ' astErrors=' + $astErrs.Count + ' verdict=' + $(if ($reachOk) { 'PASS' } else { 'FAIL' }))

    Say ('LOCK-SELFTEST RESULT ' + ($cases.Count + 19 - $fail) + ' checks, fail=' + $fail)
    if ($SelfCheckOnly) { Say 'SELFCHECK-ONLY-END' }
    Say 'END'
    } finally {
        try { if (Test-Path $runlog) { Remove-Item $runlog -Force -ErrorAction SilentlyContinue } } catch { }
    }
    if ($fail -eq 0) { exit 0 } else { exit 1 }
}

# (Test-StaticLayout is defined ABOVE the self-test block: PowerShell resolves a function
#  only after its definition line has been executed.)

$got = $false
for ($i = 0; $i -lt 7; $i++) {
    # v2.2 transition rule: judge BOTH names -- a legacy runner (17 of them read only
    # play.lock) may be holding the editor through the old file name. Either name being
    # fresh means GIVE WAY; only when all of them are stale/absent do we take the lock.
    $canonBody = Get-LockBody $lock
    $canonOwner = Lock-Owner $canonBody
    $legacyBody = Get-LockBody $legacyLock
    $legacyOwner = Lock-Owner $legacyBody
    if ($canonOwner -eq $me) { $got = $true; Say 'LOCK-ALREADY-MINE'; break }

    $busy = $false
    if ($canonOwner -ne '') {
        $canonAge = Get-LockAge $lock
        $canonVerdict = Lock-Verdict $canonBody $canonAge
        Say ('LOCK-NAME-CHECK name=play-running.lock owner=' + $canonOwner + ' age=' + $canonAge + 'm verdict=' + $canonVerdict)
        if ($canonVerdict -eq 'busy') { $busy = $true }
    } else {
        Say 'LOCK-NAME-CHECK name=play-running.lock absent'
    }
    if ($legacyOwner -ne '') {
        $legacyAge = Get-LockAge $legacyLock
        $legacyVerdict = Lock-Verdict $legacyBody $legacyAge
        Say ('LOCK-NAME-CHECK name=play.lock(LEGACY) owner=' + $legacyOwner + ' age=' + $legacyAge + 'm verdict=' + $legacyVerdict)
        if ($legacyVerdict -eq 'busy') { $busy = $true }
    } else {
        Say 'LOCK-NAME-CHECK name=play.lock(LEGACY) absent'
    }
    if ($busy) {
        Say ('LOCK-BUSY (a fresh lock exists) -> retry 180s try=' + ($i + 1) + '/7')
        Start-Sleep -Seconds 180
        continue
    }

    # FAIL-CLOSED readiness gate (team rule 2026-09-24): a free lock is NOT enough -- no
    # editor session may be running either. `playing` or an unreadable playMode => yield
    # (never rely on "the default value happens not to equal playing"). Read the values
    # BEFORE writing any lock, so an unknown state never turns into a stolen session.
    $pmState = Get-EditorPlayState
    Say ('LOCK-GATE-PLAYMODE state=' + $pmState)
    if ($pmState -eq 'playing') {
        # #42-2 TAKER DUTY: reaching this line means no FRESH live lock exists (the `$busy` branch
        # above retries while a live owner holds one) => this Play session is an ORPHAN (13:42 live
        # sample: both names ABSENT + playMode=playing, 6.5 min owner-less). Register the takeover
        # here; the post-lock stop (`Stop-Editor-Safe 'post-lock-initial'`) stops it and CONFIRMS
        # `stopped` before my own session starts -- and withholds the lock if it cannot.
        Say 'LOCK-GATE-PLAYMODE state=playing owner=(none/stale) -> registered TAKEOVER: taking the lock, then editor_stop + confirm stopped before my own session'
    }
    if ($pmState -eq 'unknown') {
        Say 'LOCK-GATE-UNKNOWN playMode unreadable -> yield (unknown != stopped)'
        Start-Sleep -Seconds 30
        continue
    }
    if ($pmState -eq 'bridge-down') {
        Say 'LOCK-GATE-BRIDGE-DOWN probed=3 editor process absent -> continuing (an absent editor cannot be playing)'
    }

    # all stale or absent -> registered takeover of a stale LEGACY lock, then take mine
    if ($legacyOwner -ne '') {
        Say ('LEGACY-LOCK-STALE-DELETED name=play.lock owner=' + $legacyOwner + ' age=' + (Get-LockAge $legacyLock) + 'm (registered)')
        Remove-Item $legacyLock -Force -ErrorAction SilentlyContinue
    }
    if ($canonOwner -ne '') {
        Say ('LOCK-STALE-TAKEOVER name=play-running.lock owner=' + $canonOwner + ' age=' + (Get-LockAge $lock) + 'm')
    }
    Write-MyLock
    Start-Sleep -Seconds 1
    $back = Get-LockBody $lock
    if ((Lock-Owner $back) -eq $me) { $got = $true; Say ('LOCK-TAKEN ' + $lock + ' pid=' + $PID); break }
    Say ('LOCK-RACE-LOST (winner=' + (Lock-Owner $back) + ') -> give way 180s')
    Start-Sleep -Seconds 180
}
if (-not $got) {
    Say ('ABORT lock held by ' + (Lock-Owner (Get-LockBody $lock)) + ' -> no editor command issued')
    Say 'END'
    exit 1
}

# mirror FIRST (right after the real lock is mine, before any editor command): the legacy
# runners read only this name, so it has to exist for as long as the real one does.
Write-Mirror

$ledgerLine = (Get-Date).ToString('yyyy-MM-dd HH:mm') + "`tu53-shopart`tshop-slot-center`t" + $why + "`r`n"
[IO.File]::AppendAllText($playLog, $ledgerLine, (New-Object Text.UTF8Encoding($false)))
Say ('PLAYLOG-APPENDED(after-lock) ' + $playLog)

if ((Test-MyLock) -ne $true) { Say 'ABORT lock lost right after LOCK-TAKEN'; exit 1 }

Say 'STOP'
$initStopOk = Stop-Editor-Safe 'post-lock-initial'
if ($initStopOk -ne $true) {
    # #42-1/#42-2: I took over (possibly an orphan Play) but could not get it back to `stopped`
    # => never start my own session on top of it, and never turn it owner-less: KEEP the lock.
    Say 'ABORT post-lock editor_stop did not reach playMode=stopped -> LOCK-RELEASE WITHHELD, NOT entering Play'
    Say ('FOR-TEAM-LEAD tag=post-lock-initial lockOwner=' + $me + ' pid=' + $PID + ' -> orphan Play needs a human/lead decision')
    Say 'END'
    exit 3
}
Start-Sleep -Seconds 2

# settle any pending script compile BEFORE entering Play (a mid-play recompile reloads the domain)
UC @('recompile') | Out-Null
for ($i = 0; $i -lt 40; $i++) {
    Start-Sleep -Seconds 2
    $st = UC @('recompile_status')
    if ($st -match 'up_to_date|completed|idle') { Say ('RECOMPILE ' + ($st -replace "`r?`n", ' ')); break }
}
# RUNNER DISCIPLINE (2026-09-24, learned the hard way twice): a green-looking poll is NOT
# enough -- `recompile_status` reports failed=true while another sheet's file is broken,
# and Unity then refuses to enter Play ("All compiler errors have to be fixed"), so the
# driver can never install (gameRunning=0) and 8 install retries all die. Detect it HERE,
# release my own lock and exit WITHOUT entering Play (observed 12:14 with
# Module/View/EntityHighlight.cs CS1061 Shader.HasProperty).
if ($st -match 'failed.{0,3}true') {
    Say 'COMPILE-FAILED (another sheet''s source is broken) -> MY lock released, NOT entering Play'
    Say ('RECOMPILE-RAW ' + ($st -replace "`r?`n", ' '))
    Release-Lock
    Say 'STOPPED'
    Say 'END'
    exit 1
}
# v2.4 team note (2026-09-24 12:30, lead): `unity status` returns an EMPTY table on this
# machine even while an editor is alive and ticking -> it must NEVER be used as a readiness
# gate (it would abort sessions that would have run). Read the REAL values from the console
# ground truth instead (compiling / compilationFailed / consoleErrors); two sheets already
# ran Play successfully while `status` was empty.
$gt = UC @('console', '--tail', '1')
$gtCompiling = ($gt -match 'compiling.{0,3}true')
$gtFailed = ($gt -match 'compilationFailed.{0,3}true')
$gtRaw = ($gt -replace "`r?`n", ' ')
# `compiling=True` here is TRANSIENT-NORMAL, NOT a gate: the 13:22 session ran to completion under
# compiling=True / compilationFailed=False (try=1, diagnostics:[], DONE why=end) -- gating on it
# would abort healthy sessions. Only `compilationFailed=True` (and the RECOMPILE `failed=true`
# check above) may abort.
Say ('EDITOR-GROUNDTRUTH compiling=' + $gtCompiling + ' compilationFailed=' + $gtFailed + ' status-empty-table-ignored=1 (transient-normal, not a gate) raw=' + $gtRaw)
if ($gtFailed) {
    Say 'ABORT compilationFailed=true (console ground truth) -> MY lock released, NOT entering Play'
    Release-Lock
    Say 'STOPPED'
    Say 'END'
    exit 1
}

Start-Sleep -Seconds 2

UC @('clear_console') | Out-Null
Say 'PLAY'
$play = UC @('editor_play')
Say ('PLAY-RESULT ' + ($play -replace "`r?`n", ' '))
Start-Sleep -Seconds 5

# A domain reload on entering Play mode can outlast the fixed 5 s settle above: the first
# ShopGrid.Tour.Run then dies with "DontDestroyOnLoad can only be used in play mode"
# (observed 2026-09-24 11:16). Retry the cfg+install triple instead of aborting the session.
$inst = ''
for ($try = 1; $try -le 8; $try++) {
    $cfg = UC @('run_script', '--file', $byl, '--entry', 'Byline.Api.Cfg')
    Say ('BYLINE-CFG try=' + $try + ' ' + ($cfg -replace "`r?`n", ' '))

    $cfg2 = UC @('run_script', '--file', $drv, '--entry', 'ShopGrid.Api.Cfg')
    Say ('SG-CFG try=' + $try + ' ' + ($cfg2 -replace "`r?`n", ' '))

    $inst = UC @('run_script', '--file', $drv, '--entry', 'ShopGrid.Tour.Run')
    Say ('INSTALL try=' + $try + ' ' + ($inst -replace "`r?`n", ' '))
    if ($inst -match 'INSTALLED' -and $inst -notmatch 'Runtime Error|Compilation Failed') { break }
    Start-Sleep -Seconds 5
}

if ($inst -notmatch 'INSTALLED') {
    Say 'INSTALL-FAILED (the driver did not install) -> capturing the console and stopping'
    $cons = UC @('console', '--tail', '60')
    Say ('CONSOLE ' + ($cons -replace "`r?`n", ' '))
    Start-Sleep -Seconds 2
    $ifStopOk = Stop-Editor-Safe 'install-failed'
    if ($ifStopOk -eq $true) { Release-Lock } else { Say 'LOCK-RELEASE WITHHELD (install-failed path): the session is still in Play -> lock kept for the taker' }
    Say 'STOPPED'
    Say 'END'
    exit 1
}

$t0 = Get-Date
while (((Get-Date) - $t0).TotalSeconds -lt 240) {
    if (Test-Path $done) { break }
    Start-Sleep -Milliseconds 500
}
if (Test-Path $done) { Say ('DONE ' + (Get-Content $done -Raw).Trim()) } else { Say 'DONE-MISSING' }

Start-Sleep -Seconds 2
# protocol v2 rule 4: if the lock is no longer mine, DO NOT touch the editor and DO NOT
# delete someone else's lock -- my session may have been interrupted (readings = suspect).
$finalStopOk = Stop-Editor-Safe 'final'
Say ('STOP-COUNTER editor_stopCalls=' + $editorStopCalls + ' (release follows only after the trigger + a waited-for readback; 0 = trigger skipped -> see ORPHAN-PLAY above)')
if ($finalStopOk -eq $true) { Release-Lock; Say 'STOPPED' } else { Say 'STOPPED-withheld (LOCK-HELD-ON-PURPOSE: lock intentionally NOT released; see ORPHAN-PLAY + LOCK-RELEASE WITHHELD above; exit 3)' }

# freeze the log window the run produced (the driver logs with tag SG via Game.Logger)
$fs = New-Object System.IO.FileStream($logPath, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::ReadWrite)
$fs.Seek($off, [System.IO.SeekOrigin]::Begin) | Out-Null
$len = [int]($fs.Length - $off)
$buf = New-Object byte[] $len
$read = $fs.Read($buf, 0, $len)
$fs.Close(); $fs.Dispose()
$txt = [System.Text.Encoding]::UTF8.GetString($buf, 0, $read)
$keep = @($txt -split "`r?`n" | Where-Object { $_ -match '\[SG\]|\[Npc\]|\[Ui\] 请求买入' })
$ev = Join-Path $shots 'shopgrid_evidence.txt'
[System.IO.File]::WriteAllLines($ev, [string[]]$keep, (New-Object System.Text.UTF8Encoding($false)))
Say ('LOGWRITE ' + $ev + ' lines=' + $keep.Count)
foreach ($l in @($keep | Where-Object { $_ -match '\[SG\]' })) { Say ('EV ' + $l) }
Say 'END'
# distinct exit code so a wrapper can never mistake "lock withheld on purpose" for a clean finish
# (README 5.2: an exit code alone is not a verdict, but it must not be the WRONG one either).
if ($finalStopOk -ne $true) { exit 3 }
