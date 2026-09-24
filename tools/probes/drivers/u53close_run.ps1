# =============================================================================
# u53close_run.ps1 -- ONE Play session for slice u53-closefix (close-exit batch).
#   Same shell as shopgrid_run.ps1 (lock -> stop -> recompile -> play -> drive ->
#   wait -> stop -> release -> freeze).  Reads the console with
#   `unity command console --tail` (NOT Logs/Editor.log: that path is known to
#   miss Debug.Log -- see the team note about slice charstat).
#
# Fixed order:
#   play-log.tsv append -> play lock -> editor_stop -> recompile settle ->
#   clear_console -> editor_play -> run_script D2U3P.Dump.Install (args = .ai-tmp)
#   -> poll console (<=180 s) for "[R8] MUTEX-END" / "DONE cells=" / "ABORT no-stage"
#   -> editor_stop -> release lock -> freeze console tail + filtered evidence.
#
# Evidence:
#   .ai-tmp/test/u53close_console_freeze.txt     (raw console tail, frozen)
#   .ai-tmp/test/u53close_evidence.txt           (filtered [D2U3P]/[R8]/CLOSE- lines)
#   .ai-tmp/test/u53close_runlog.txt             (this script's step log)
#   .ai-tmp/test/u3_popups_dump.txt              (per-cell dump written by the driver)
#   .ai-tmp/screenshots/u53_r8_*.png             (R8 shots: Normal layer / close mark)
# ASCII only (PS 5.1 reads a BOM-less non-ASCII .ps1 as ANSI).
# =============================================================================
param([switch]$SelfTest, [switch]$SelfCheckOnly)
# -SelfTest      = lock verdict table + static/two-way assertions; touches NO lock and NO editor.
# -SelfCheckOnly = same, PLUS a READ-ONLY playMode gate probe, and prints SELFCHECK-ONLY-END.
#   team ruling 2026-09-24 (jitter's accident): never "start a real runner and force-kill it" to read
#   the startup self-check -- a 9 s kill left a ZOMBIE lock + an invalid ledger row (its finally
#   never ran).  Wanting a reading must use this no-lock entry, not "remember not to kill".

$ErrorActionPreference = 'Continue'

$root   = Split-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) -Parent
$proj   = Join-Path $root 'client'
$shots  = Join-Path $root '.ai-tmp\screenshots'
$test   = Join-Path $root '.ai-tmp\test'
$drv    = Join-Path $root 'tools\probes\drivers\d2u3_popups_drive.cs'
$spec   = ($root -replace '\\', '/') + '/.ai-tmp'
$runlog = Join-Path $test 'u53close_runlog.txt'
# !! team rule (README 5.2 #30): a self-check must NEVER write a path that a normal run also writes.
# Measured self-hit (2026-09-24): -SelfTest/-SelfCheckOnly truncated u53close_runlog.txt (the very
# file the real 13:11 batch traced into) => redirected here, BEFORE the first Say() call, so the
# normal-run trace is byte-identical across self-checks.  (Proof = full .ai-tmp/test hash snapshot
# before/after, see report section 11.4.)
if ($SelfTest -or $SelfCheckOnly) { $runlog = Join-Path $test 'u53close_selftest.txt' }
$freeze = Join-Path $test 'u53close_console_freeze.txt'
$ev     = Join-Path $test 'u53close_evidence.txt'
$dump   = Join-Path $test 'u3_popups_dump.txt'
$playLog = Join-Path $test 'play-log.tsv'
$lock    = Join-Path $test 'play-running.lock'
$legacyLock = Join-Path $test 'play.lock'
$me      = 'u53-closefix'
$why     = 'u53-closefix: user batch-3 complaint "not even a close on it" -- InventoryPanel close control was an alpha=0 hit area (now a visible bitmap-font X), SkillTree/QuestLog moved Popup->Normal so the engine modal mask cannot lock the mouse out of the HUD, and HudPanel.CloseScreenFamily must be proven at runtime (engine CloseMutexPanels only covers Popup-layer panels).'

function Say([string]$s) {
    $stamp = (Get-Date).ToString('HH:mm:ss.fff')
    Write-Host ($stamp + ' ' + $s)
    Add-Content -Path $runlog -Value ($stamp + ' ' + $s) -Encoding UTF8
}

function UC([string[]]$argv) {
    return (& unity command @argv --project-path $proj --format json --no-pager 2>&1 | Out-String)
}

# ---- SELF-ENCODING CHECK (team rule 2026-09-24) -------------------------------------------
# PS 5.1 parses a BOM-less NON-ASCII .ps1 as ANSI => garbled quotes / syntax errors / false greens.
# Rule for every *.ps1: pure ASCII OR UTF-8 WITH BOM. The check must be able to FAIL and its
# result is logged on every run (never rely on "I remembered to write ASCII").
$selfBytes = [IO.File]::ReadAllBytes($PSCommandPath)
$selfNonAscii = 0
foreach ($by in $selfBytes) { if ($by -gt 127) { $selfNonAscii++ } }
$selfBom = ($selfBytes.Length -ge 3 -and $selfBytes[0] -eq 0xEF -and $selfBytes[1] -eq 0xBB -and $selfBytes[2] -eq 0xBF)
# NOTE: keep this a SINGLE line -- PS 5.1 does not continue an expression that merely STARTS with
# '+' (it terminates the statement) => a wrapped string-concat here is a parse error (measured).
$selfVerdict = if (($selfNonAscii -eq 0) -or $selfBom) { 'OK' } else { 'FAIL' }
Say ('SELF-ENCODING file=' + (Split-Path $PSCommandPath -Leaf) + ' bytes=' + $selfBytes.Length + ' nonAscii=' + $selfNonAscii + ' bom=' + $selfBom + ' verdict=' + $selfVerdict)
if (($selfNonAscii -gt 0) -and (-not $selfBom)) {
    Say 'ABORT self-encoding: non-ASCII .ps1 without BOM (PS 5.1 would parse it as ANSI)'
    Say 'END'
    exit 3
}

# ---- RUNNER FINGERPRINT (team ruling 2026-09-24, from u3bverify's finding) --------------------
# A hand-copied hash/line-number goes stale within minutes (measured: 8 line numbers mismatched
# 2.5 min later => reviewers re-check a line that no longer exists => false reds).  So the trace
# carries its OWN fingerprint: reports cite "as per the RUNNER-FINGERPRINT line in the trace".
$fpHash = (Get-FileHash $PSCommandPath -Algorithm SHA256).Hash.Substring(0, 16).ToLowerInvariant()
Say ('RUNNER-FINGERPRINT sha256_16=' + $fpHash + ' bytes=' + $selfBytes.Length + ' lines=' + ((Get-Content $PSCommandPath).Count) + ' mtime=' + (Get-Item $PSCommandPath).LastWriteTime.ToString('s') + ' (authoritative for THIS trace; verified by re-computing)')

foreach ($d in @($shots, $test)) { if (-not (Test-Path $d)) { New-Item -ItemType Directory -Path $d | Out-Null } }
# (self-check mode already redirected $runlog at its assignment -- see the note there)
Set-Content -Path $runlog -Value '# u53-closefix run log' -Encoding UTF8

# =============================================================================
# LOCK PROTOCOL v2 (team-wide, 2026-09-24) -- owner <ISO8601> <PID>
#   1) no file           => write (my PID) -> wait ~1 s -> READ BACK; not mine => yield
#   2) file exists        => 3 fields: PID alive (Get-Process -Id) AND age < 12 min => YIELD
#                            PID dead OR age >= 12 min => stale, take it over (and say so)
#   3) 12 min = the ONLY threshold (no local 5/8/10 min variants)
#   4) NEVER touch the editor (editor_play / recompile / editor_stop) unless the lock is MINE
#   5) play-log.tsv is appended ONLY AFTER the lock is confirmed mine (a row must never exist
#      for a session that never had the lock -- that is exactly how the ledger got polluted:
#      this script used to append first and discover the busy lock afterwards)
#   6) release only MY lock; if the lock was taken over by someone else => do not delete it,
#      do not send editor_stop, and record "suspect (lock stolen / intervened)" for the batch
# =============================================================================
# Normalise lock content: strip U+FEFF / U+200B + trim.  LOCK CONTENT must never carry a BOM
# (the SCRIPT file may be UTF-8-with-BOM, the lock body may not) -- v2.2 item (2).
function Norm([string]$raw) {
    if ($null -eq $raw) { return '' }
    return ((($raw -replace ([string][char]0xFEFF), '') -replace ([string][char]0x200B), '')).Trim()
}
function LockBody() {
    if (-not (Test-Path $lock)) { return '' }
    try { return (Norm (Get-Content $lock -Raw)) } catch { return '' }
}
function LegacyBody() {
    if (-not (Test-Path $legacyLock)) { return '' }
    try { return (Norm (Get-Content $legacyLock -Raw)) } catch { return '' }
}
function IsMine([string]$body) {
    return ($body -match ('^\s*' + [regex]::Escape($me) + '\s'))
}
function LockIsMine() { return (IsMine (LockBody)) }

# v2.2 (1): transitional MIRROR -- the real lock is play-running.lock (3 fields), and while the
# 17 historical runners (which only read play.lock) are still on disk we ALSO write an identical
# mirror to play.lock so they see a busy lock.  Written as ASCII: no BOM in lock CONTENT.
function Write-Locks() {
    $body = ($me + ' ' + (Get-Date).ToString('o') + ' ' + $PID)
    Set-Content -Path $lock -Value $body -Encoding ASCII
    Set-Content -Path $legacyLock -Value $body -Encoding ASCII
    # log the mirror explicitly: "read both names" != "write the mirror" (team method note 5.2 #8).
    # The mirror exists ONLY to block the 17 historical runners that read the old name: reading
    # protects me, writing protects them.
    Say ('LEGACY-MIRROR-WRITTEN ' + $legacyLock + ' [' + $body + ']')
    return $body
}

# The ONLY place in this script that sends editor_stop (static assertion enforces "exactly once",
# u52play's team-wide self-recursion / unguarded-tail bug).  Never stops somebody else's session.
function Stop-Editor-Safe([string]$tag) {
    if (-not (LockIsMine)) {
        Say ('SKIP editor_stop [' + $tag + ']: lock is not mine (' + (LockBody) + ')')
        Add-Content -Path (Join-Path $test 'u53close_suspect.txt') -Value ((Get-Date).ToString('s') + ' editor_stop skipped [' + $tag + ']: lock not mine ' + (LockBody) + ' => batch suspect (lock stolen / intervened)') -Encoding UTF8
        return
    }
    UC @('editor_stop') | Out-Null
    Say ('EDITOR-STOPPED [' + $tag + '] (lock was mine)')
}

# Release: delete MY lock and MY mirror only (never somebody else's, never silently).
function Release-Lock() {
    if (IsMine (LockBody)) {
        Remove-Item $lock -Force -ErrorAction SilentlyContinue
        Say 'LOCK-RELEASED (mine)'
    } elseif ((LockBody) -ne '') {
        Say ('LOCK-RELEASE REFUSED (owned by ' + (LockBody) + ') -> left alone')
    } else { Say 'LOCK-RELEASE skip (no lock file)' }
    if (IsMine (LegacyBody)) {
        Remove-Item $legacyLock -Force -ErrorAction SilentlyContinue
        Say 'LEGACY-MIRROR-RELEASED (mine)'
    } elseif ((LegacyBody) -ne '') {
        Say ('LEGACY-MIRROR-RELEASE REFUSED (owned by ' + (LegacyBody) + ') -> left alone')
    }
}

# v2.1 verdict (the ONLY place the stale/busy decision is made -- so it can be self-tested):
#   ''                            => 'free'
#   age >= 12min                  => 'stale'   (12 min is the only threshold)
#   MISSING PID field, fresh      => 'busy'    (unknown != dead -- v2.1: never grab it)
#   PID field present but dead    => 'stale'
#   PID alive and fresh           => 'busy'
function LockVerdict([string]$body, [double]$ageMin) {
    if ([string]::IsNullOrWhiteSpace($body)) { return 'free' }
    if ($ageMin -ge 12) { return 'stale' }
    $parts = $body -split '\s+'
    $hasPid = ($parts.Count -ge 3)
    $ownerPid = 0
    if ($hasPid) {
        [void][int]::TryParse($parts[2], [ref]$ownerPid)
        if ($ownerPid -le 0) { $hasPid = $false }     # 3 fields but the 3rd is not a PID => unknown
    }
    if (-not $hasPid) { return 'busy' }               # v2.1
    if ($null -eq (Get-Process -Id $ownerPid -ErrorAction SilentlyContinue)) { return 'stale' }
    return 'busy'
}

# Static source assertions, extracted into a PURE function so they can be fed known-good AND
# known-bad samples (a check that only ever fails on fake samples is as harmful as a false green).
# !! Comment lines are blanked BEFORE line numbers are computed: `charstat` hit a false RED on
#    d2u3_charstat_run.ps1 because a file-header comment merely MENTIONED `editor_play`.
function StaticChecks([string[]]$srcLines) {
    $code = New-Object System.Collections.ArrayList
    foreach ($ln in $srcLines) {
        if ($ln -match '^\s*#') { [void]$code.Add('') } else { [void]$code.Add($ln) }
    }
    $lockLine = 0; $firstEditor = 0; $stopLine = 0; $stopCount = 0; $guardFn = 0; $guardEnd = 0
    for ($k = 0; $k -lt $code.Count; $k++) {
        $ln = $code[$k]
        if ($ln -match "Say \('LOCK-TAKEN") { if ($lockLine -eq 0) { $lockLine = $k + 1 } }
        if ($ln -match "^\s*UC @\('editor_stop'\)") { $stopLine = $k + 1; $stopCount++ }
        if (($ln -match "UC @\('editor_play'\)") -or ($ln -match "UC @\('recompile'\)")) { if ($firstEditor -eq 0) { $firstEditor = $k + 1 } }
        if ($ln -match '^function Stop-Editor-Safe') { $guardFn = $k + 1 }
        if ($guardFn -gt 0 -and $guardEnd -eq 0 -and ($k + 1) -gt $guardFn -and $ln -match '^\}') { $guardEnd = $k + 1 }
    }
    $text = ($code -join "`n")
    $out = New-Object System.Collections.ArrayList
    [void]$out.Add(@{ n = 'static: LOCK-TAKEN line exists'; ok = ($lockLine -gt 0) })
    [void]$out.Add(@{ n = 'static: every editor_play/recompile is AFTER the lock line'; ok = ($firstEditor -gt $lockLine) })
    [void]$out.Add(@{ n = 'static: editor_stop appears exactly once in code'; ok = ($stopCount -eq 1) })
    [void]$out.Add(@{ n = 'static: that editor_stop is INSIDE Stop-Editor-Safe'; ok = ($guardFn -gt 0 -and $stopLine -gt $guardFn -and ($guardEnd -eq 0 -or $stopLine -le $guardEnd)) })
    [void]$out.Add(@{ n = 'static: lock writes only inside Write-Locks (no stray Set-Content)'; ok = (([regex]::Matches($text, 'Set-Content -Path \$lock ')).Count -eq 1 -and ([regex]::Matches($text, 'Set-Content -Path \$legacyLock ')).Count -eq 1) })
    [void]$out.Add(@{ n = 'static: legacy mirror write present (v2.2 transitional)'; ok = ($text -match 'Set-Content -Path \$legacyLock -Value \$body -Encoding ASCII') })

    # --- u52play team bug #4: `Say` must NOT write to the SUCCESS stream (Write-Output) and no
    #     condition may call a Say-bearing function bare -- PowerShell takes the command's WHOLE
    #     output as the condition, so `@('<log line>', $false)` is a non-empty array => TRUE =>
    #     "lost the lock race" reads as "won it" (false exclusivity).  Static, stub-independent.
    $sayFn = ''
    foreach ($ln in $code) { if ($ln -match '^\s*function\s+Say\b') { $sayFn = ($ln -replace '.*function\s+Say[^(]*\([^)]*\)\s*\{?', '') } }
    $sayStart = -1; $sayEnd = -1
    for ($k = 0; $k -lt $code.Count; $k++) {
        if ($sayStart -lt 0 -and $code[$k] -match '^\s*function\s+Say\b') { $sayStart = $k }
        elseif ($sayStart -ge 0 -and $sayEnd -lt 0 -and $code[$k] -match '^\}') { $sayEnd = $k; break }
    }
    $sayBody = ''
    if ($sayStart -ge 0 -and $sayEnd -gt $sayStart) { $sayBody = (($code[$sayStart..$sayEnd]) -join "`n") }
    [void]$out.Add(@{ n = 'static: Say uses Write-Host and NEVER Write-Output'; ok = ($sayBody -match 'Write-Host' -and -not ($sayBody -match 'Write-Output')) })
    $bareCall = $false
    foreach ($ln in $code) {
        if ($ln -match '^\s*if\s*\(\s*(-?not\s+)?(Say|Write-Locks|Stop-Editor-Safe)\b') { $bareCall = $true }
    }
    [void]$out.Add(@{ n = 'static: no bare if(Say-bearing-fn) (conditions compare explicitly)'; ok = (-not $bareCall) })

    # --- team bug #5 (classcols, 2026-09-24): a `continue` OUTSIDE a loop silently ends the script
    #     with exit 0 -- no LOCK-TAKEN, no ledger row, no Play, NO ERROR.  AST-based (not regex):
    #     every ContinueStatementAst must have a LoopStatementAst somewhere up its parent chain.
    $astT = $null; $astE = $null
    $ast = [System.Management.Automation.Language.Parser]::ParseFile($PSCommandPath, [ref]$astT, [ref]$astE)
    $loopOut = @()
    foreach ($c in $ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.ContinueStatementAst] }, $true)) {
        $pa = $c.Parent
        $inL = $false
        while ($pa -ne $null) {
            if ($pa -is [System.Management.Automation.Language.LoopStatementAst]) { $inL = $true; break }
            $pa = $pa.Parent
        }
        if (-not $inL) { $loopOut += $c.Extent.StartLineNumber }
    }
    [void]$out.Add(@{ n = 'static: every continue sits inside a loop (no silent stop, exit-0 trap)'; ok = ($loopOut.Count -eq 0) })
    [void]$out.Add(@{ n = 'static: terminal markers present (LOCK-TAKEN + END + STOPPED)'; ok = ($text -match 'LOCK-TAKEN' -and $text -match "Say 'END'" -and $text -match "STOPPED") })
    return $out
}

if ($SelfTest -or $SelfCheckOnly) {
    # no-lock, no-editor entry: prove what the state was on both sides of this run
    Say ('LOCK-SELFCHECK-ONLY locks before=[' + (Test-Path $lock) + ',' + (Test-Path $legacyLock) + ']')
    if ($SelfCheckOnly) {
        $stc = UC @('editor_status')
        $pmc = if ($stc -match '"playMode"\s*:\s*"([A-Za-z]+)"') { $Matches[1] } else { 'UNREADABLE' }
        $brc = if ($stc -match '"success"\s*:\s*true') { 'ok' } else { 'bridge-down' }
        Say ('SELFCHECK-ONLY-GATE playMode=' + $pmc + ' bridge=' + $brc + ' verdict=' + $(if ($pmc -eq 'stopped') { 'would-take-lock' } elseif ($pmc -eq 'playing') { 'would-ABORT-editor-is-playing' } else { 'would-yield-fail-closed' }))
    }
    $now = (Get-Date).ToString('o')
    $cases = @(
        @{ n = 'empty-is-free';                 body = '';                          age = 0.0;  want = 'free' },
        @{ n = 'parse-3field-live-pid-fresh';   body = ($me + ' ' + $now + ' ' + $PID); age = 1.0;  want = 'busy' },
        @{ n = 'parse-2field(no-pid)-fresh';    body = ('other ' + $now);            age = 0.5;  want = 'busy' },
        @{ n = 'fresh-no-pid-is-NOT-stale';     body = ('u53-closefix 2026-09-24T11:50:07.0000000+08:00'); age = 1.0; want = 'busy' },
        @{ n = 'old-no-pid-IS-stale(13m)';      body = ('u53-closefix 2026-09-24T11:00:00.0000000+08:00'); age = 13.0; want = 'stale' },
        @{ n = 'dead-pid-IS-stale';             body = ('other ' + $now + ' 999999'); age = 1.0; want = 'stale' },
        @{ n = 'live-pid-fresh-NOT-stale';      body = ('other ' + $now + ' ' + $PID); age = 1.0; want = 'busy' },
        @{ n = '3field-non-numeric-pid-fresh';  body = ('other ' + $now + ' abc');    age = 1.0;  want = 'busy' },
        @{ n = '3field-non-numeric-pid-old';    body = ('other ' + $now + ' abc');    age = 13.0; want = 'stale' }
    )
    $pass = 0
    foreach ($c in $cases) {
        $got = LockVerdict $c.body $c.age
        $ok = ($got -eq $c.want)
        if ($ok) { $pass++ }
        Say (('LOCK-SELFTEST {0} {1} want={2} got={3}' -f $(if ($ok) { 'PASS' } else { 'FAIL' }), $c.n, $c.want, $got))
    }
    # --- extra checks (u52play v2.1 broadcast): BOM recognition + static placement guards -------
    # pitfall 2: PS 5.1 `Set-Content -Encoding UTF8` writes a BOM => parts[0] = "\uFEFF<owner>" and
    #            a slice cannot recognise/release its OWN lock.  Prove the strip path.
    # pitfall 3: a batch edit can leave an UNGUARDED editor_stop or make it recurse => assert
    #            placement statically (cheap, fails loudly) instead of trusting the edit.
    $extra = @()
    $bomBody = ([string][char]0xFEFF) + ($me + ' ' + $now + ' ' + $PID)
    $norm = ((($bomBody -replace ([string][char]0xFEFF), '') -replace ([string][char]0x200B), '')).Trim()
    $extra += @{ n = 'bom-prefixed-own-lock-is-recognised'; ok = ($norm -match ('^\s*' + [regex]::Escape($me) + '\s')) }
    $extra += @{ n = 'no-UTF8-BOM-write-on-the-lock'; ok = (-not ([IO.File]::ReadAllText($PSCommandPath) -match 'Set-Content -Path \$lock -Value.*-Encoding UTF8')) }

    $srcLines = [IO.File]::ReadAllLines($PSCommandPath)
    foreach ($c in (StaticChecks $srcLines)) { $extra += $c }

    # ---- TWO-WAY self-check (team rule): a check must be GREEN on a known-good sample and RED on
    #      a known-bad one.  Known-good = a comment that carries the PATTERNS (charstat's false red);
    #      known-bad = a command placed BEFORE the lock line, and a duplicated editor_stop.
    $commentSample = @(
        '# UC @(''editor_play'') | Out-Null',
        '# UC @(''editor_stop'') | Out-Null',
        '# function Stop-Editor-Safe { }',
        '# }'
    )
    $fakeOk = $true
    foreach ($c in (StaticChecks ($commentSample + $srcLines))) { if (-not $c.ok) { $fakeOk = $false } }
    $extra += @{ n = 'two-way: pattern-carrying COMMENTS stay GREEN (known-good)'; ok = $fakeOk }

    $pre = New-Object System.Collections.ArrayList
    foreach ($ln in $srcLines) { [void]$pre.Add($ln) }
    $lockIx = 0
    for ($k = 0; $k -lt $pre.Count; $k++) { if ($pre[$k] -match "Say \('LOCK-TAKEN") { $lockIx = $k; break } }
    # Build the known-bad fixture from PIECES on purpose: a literal copy in the source would itself
    # be a code line matching the very pattern under test => false red on this very check (measured
    # 12:23 on this file: "every editor_play/recompile is AFTER the lock line" went red because of
    # the fixture literal).  The check's own test data must not be indistinguishable from the thing
    # it looks for.
    $badCmd = ("UC @(" + "'editor_play'" + ") | Out-Null")
    [void]$pre.Insert([Math]::Max(0, $lockIx - 1), $badCmd)
    $preDetected = $false
    foreach ($c in (StaticChecks $pre.ToArray())) { if (($c.n -match 'AFTER the lock line') -and (-not $c.ok)) { $preDetected = $true } }
    $extra += @{ n = 'two-way: editor_play BEFORE the lock line goes RED (known-bad)'; ok = $preDetected }

    # known-bad for the Write-Output class: a Say that writes to the SUCCESS stream must go RED.
    $badSay = @(
        'function Say([string]$s) {',
        '    Write-Output $s',
        '}'
    ) + $srcLines
    $badSayDetected = $false
    foreach ($c in (StaticChecks $badSay)) { if (($c.n -match 'Say uses Write-Host') -and (-not $c.ok)) { $badSayDetected = $true } }
    $extra += @{ n = 'two-way: Write-Output inside Say goes RED (known-bad)'; ok = $badSayDetected }

    $dup = New-Object System.Collections.ArrayList
    foreach ($ln in $srcLines) { [void]$dup.Add($ln) }
    [void]$dup.Add("    UC @('editor_stop') | Out-Null")
    $dupDetected = $false
    foreach ($c in (StaticChecks $dup.ToArray())) { if (($c.n -match 'exactly once') -and (-not $c.ok)) { $dupDetected = $true } }
    $extra += @{ n = 'two-way: a duplicated editor_stop goes RED (known-bad)'; ok = $dupDetected }
    foreach ($c in $extra) {
        $ok = $c.ok
        if ($ok) { $pass++ }
        Say (('LOCK-STATIC {0} {1}' -f $(if ($ok) { 'PASS' } else { 'FAIL' }), $c.n))
    }

    $total = $cases.Count + $extra.Count
    Say ("LOCK-SELFTEST SUMMARY {0}/{1}" -f $pass, $total)
    Say ('LOCK-SELFCHECK-ONLY locks after=[' + (Test-Path $lock) + ',' + (Test-Path $legacyLock) + ']')
    if ($SelfCheckOnly) { Say 'SELFCHECK-ONLY-END' }
    Say 'END'
    if ($pass -eq $total) { exit 0 } else { exit 1 }
}

$locked = $false
for ($i = 0; $i -lt 40; $i++) {
    # ---- PRE-TAKE gate: fail-CLOSED (team ruling 2026-09-24, classcols' hole) ------------------
    # Only "playMode=stopped" may take the lock.  fail-OPEN is forbidden: "cannot read playMode"
    # != "not playing" (two live proofs of SILENT cli failure: `status --project-path` returns an
    # empty table; `run_script` answers success:true for a driver that does not even compile).
    # Bridge-DOWN is a different case: with no editor process nobody can be in Play => allowed,
    # but only after a bounded 3 x 5 s retry, and any reachable reading during that wins.
    $gateState = 'unknown'
    for ($g = 1; $g -le 3; $g++) {
        $stg = UC @('editor_status')
        if ($stg -match '"playMode"\s*:\s*"([A-Za-z]+)"') { $gateState = $Matches[1]; break }
        if ($stg -match '"success"\s*:\s*false|COMMAND_FAILED') {
            $gateState = 'bridge-down'
            Say ('LOCK-GATE-BRIDGE-DOWN probed=' + $g + ' (bridge unreachable; retry 5s)')
            Start-Sleep -Seconds 5
            continue
        }
        $gateState = 'unreadable'
        Say ('LOCK-GATE-UNKNOWN playMode unreadable probe=' + $g + ' => yield (unknown != stopped)')
        break
    }
    if ($gateState -eq 'playing') {
        Say 'LOCK-GATE playMode=playing => ABORT (not taking the lock, not touching the editor, not accounting)'
        Say 'END'
        exit 4
    }
    if ($gateState -eq 'unreadable') {
        Say 'LOCK-GATE-UNKNOWN => NOT taking the lock (fail-closed); sleep 30 and re-probe'
        Start-Sleep -Seconds 30
        continue
    }
    if ($gateState -eq 'bridge-down') { Say 'LOCK-GATE-BRIDGE-DOWN probed=3 => proceeding (no editor cannot be someone playing)' }
    else { Say ('LOCK-GATE playMode=' + $gateState + ' => may take the lock') }

    # TRANSITION RULE (team 2026-09-24): exactly ONE real lock = play-running.lock.
    # .ai-tmp/test/play.lock is LEGACY -- never write it; while runners are still being migrated
    # consult BOTH: legacy exists AND fresh => yield; legacy exists AND stale => register + delete
    # (never delete silently). This slice takes/gives up ONLY play-running.lock.
    $legacyBody = (LegacyBody)     # BOM/ZWSP-stripped -- same normaliser as the real lock
    if ($legacyBody -ne '') {
        $legacyAge = ((Get-Date) - (Get-Item $legacyLock).LastWriteTime).TotalMinutes
        if ((LockVerdict $legacyBody $legacyAge) -ne 'stale') {
            Say ('LOCK-BUSY legacy play.lock owner=' + $legacyBody + ' age=' + [math]::Round($legacyAge, 1) + 'm -> sleep 30 (transition rule: both files checked)')
            Start-Sleep -Seconds 30
            continue
        }
        Say ('LEGACY-LOCK-STALE-DELETED play.lock owner=' + $legacyBody + ' age=' + [math]::Round($legacyAge, 1) + 'm (registered; stale => removable)')
        Remove-Item $legacyLock -Force -ErrorAction SilentlyContinue
    }

    $b = (LockBody)
    if ($b -eq '') {
        # exclusivity evidence for THIS batch (v2 rule 6): both names were empty just before taking.
        Say ('LOCK-PRE-TAKE-FREE real=' + (LockBody) + ' legacy=' + (LegacyBody) + ' (both empty => nobody held it)')
        [void](Write-Locks)
        Start-Sleep -Seconds 1
        if ((LockIsMine) -eq $true) { $locked = $true; break }   # explicit compare: a bare `if (Fn)` treats the command's WHOLE output as the condition, so any stray stdout line makes it TRUE (team bug #4, u52play)
        Say ('LOCK-RACE lost (read back: ' + (LockBody) + ') -> yield')
        Start-Sleep -Seconds 30
        continue
    }
    $age = ((Get-Date) - (Get-Item $lock).LastWriteTime).TotalMinutes
    $verdict = LockVerdict $b $age
    if ($verdict -eq 'stale') {
        Say ('LOCK-STALE-TAKEOVER owner=' + $b + ' age=' + [math]::Round($age, 1) + 'm')
        [void](Write-Locks)
        Start-Sleep -Seconds 1
        if ((LockIsMine) -eq $true) { $locked = $true; break }   # explicit compare: a bare `if (Fn)` treats the command's WHOLE output as the condition, so any stray stdout line makes it TRUE (team bug #4, u52play)
        Say ('LOCK-RACE lost on takeover (read back: ' + (LockBody) + ') -> yield')
    } else {
        Say ('LOCK-BUSY owner=' + $b + ' age=' + [math]::Round($age, 1) + 'm -> sleep 30 (try ' + ($i + 1) + ')')
    }
    Start-Sleep -Seconds 30
}
if (-not $locked) {
    # fail-fast: never touch the editor when the lock is not mine
    Say ('ABORT lock held by ' + (LockBody))
    Say 'END'
    exit 2
}
Say ('LOCK-TAKEN ' + $lock + ' [' + (LockBody) + ']')

# accounting AFTER the lock is confirmed mine (v2 rule 5)
# Ledger row = appends into a SHARED, machine-parsed TSV => UTF-8 **without BOM** (team rule):
# `-Encoding UTF8` on a file this script CREATES would inject a BOM (extra column for TSV parsers),
# and `-Encoding ASCII` would silently turn CJK into '?'.  $playLog is absolute (built from
# $PSScriptRoot) so the .NET call cannot hit the "relative path resolves to the wrong cwd" trap.
$row = ((Get-Date).ToString('yyyy-MM-dd HH:mm') + "`t" + $me + "`tu53-closefix-close-exit`t" + $why)
[IO.File]::AppendAllText($playLog, $row + "`r`n", (New-Object System.Text.UTF8Encoding($false)))
Say ('PLAYLOG-APPENDED ' + $playLog)

# Exclusivity evidence (v2 rule 6) + readiness gate.
# NOTE 2026-09-24: `unity status` (the bare subcommand) returns an EMPTY table on this host -- it is
# a broken reading, NOT "editor offline".  `unity command editor_status` is a DIFFERENT subcommand
# and works (measured 12:32 -> status/playMode/compiling/lastHeartbeat all real), and
# `unity command console --tail 1` carries groundTruth(compiling/compilationFailed/consoleErrors).
# So the gate reads BOTH and its verdict can FAIL (success!=true => the bridge is dead => abort;
# compiling=true is NOT fatal -- the recompile-settle loop below waits it out).
$st0 = UC @('editor_status')
$bridgeOk = ($st0 -match '"success"\s*:\s*true')
# team rule (2026-09-24, from zorder's finding): the readiness gate MUST also look at playMode --
# `editor_stop` returns before the editor really leaves Play, so a "lock free but playMode=playing"
# window exists; playing the editor while another session is still in Play must ABORT, not proceed.
$playing = ($st0 -match '"playMode"\s*:\s*"playing"')
# post-take gate is fail-closed too: an unreadable playMode is NOT "not playing".
if (-not ($st0 -match '"playMode"\s*:\s*"([A-Za-z]+)"')) {
    Say 'ABORT post-take gate: playMode unreadable => fail-closed (releasing my lock, NOT touching the editor)'
    Release-Lock
    Say 'END'
    exit 4
}
$gt = UC @('console', '--tail', '1')
$compiling = ($gt -match '"compiling"\s*:\s*true')
$compFail = ($gt -match '"compilationFailed"\s*:\s*true')
$ready = ($bridgeOk -and (-not $compFail) -and (-not $playing))
Say ('EDITOR-STATUS-FIRST bridge_ok=' + $bridgeOk + ' playMode=' + $(if ($playing) { 'playing' } else { 'stopped/other' }) + ' compiling=' + $compiling + ' compilationFailed=' + $compFail + ' lastHeartbeat=' + $(if ($st0 -match '"lastHeartbeat"\s*:\s*"([^"]+)"') { $Matches[1] } else { 'n/a' }) + ' verdict=' + $(if ($playing) { 'ABORT-editor-is-playing' } elseif ($ready) { 'READY' } else { 'NOT-READY' }))
if ((-not $ready) -or $playing) {
    Say ('ABORT editor not startable (playing=' + $playing + ' bridge_ok=' + $bridgeOk + ' compilationFailed=' + $compFail + ') -- releasing my lock, NOT touching the editor')
    Release-Lock
    Say 'END'
    exit 4
}

Say 'STOP'
Stop-Editor-Safe 'pre-run'
Start-Sleep -Seconds 2

UC @('recompile') | Out-Null
for ($i = 0; $i -lt 40; $i++) {
    Start-Sleep -Seconds 2
    $st = UC @('recompile_status')
    if ($st -match 'up_to_date|completed|idle') { Say ('RECOMPILE ' + ($st -replace "`r?`n", ' ')); break }
}
Start-Sleep -Seconds 2

UC @('clear_console') | Out-Null
Say 'PLAY'
$play = UC @('editor_play')
Say ('PLAY-RESULT ' + ($play -replace "`r?`n", ' '))
Start-Sleep -Seconds 5

# STAGE PRECONDITION: the popups driver only dumps panels that are OPEN, and its own guard
# waits 120 s for Game.IsRunning && HudPanel open.  A cold Play session sits on the main menu
# (measured 2026-09-24: "ABORT no-stage gameRunning=1 hudOpen=0"), so the X tour must create the
# stage first (entry X.Tour.Install, spec = tour|<tag>|<shotsDir>|<doneFile>, see x_run.ps1:337).
# NOTE: the CLI wants a real JArray; PS 5.1 native-arg quoting eats bare quotes (first attempt
# failed with INVALID_COMMAND_ARGS "...received [C:/.../.ai-tmp]") => escaped-quote idiom.
$xdrv  = Join-Path $root 'tools\probes\drivers\x_drive.cs'
$xdone = Join-Path $test 'u53close_xtour_done.txt'
$xspec = 'tour|u53close|' + ($shots -replace '\\', '/') + '|' + ($xdone -replace '\\', '/')
if (Test-Path $xdone) { Remove-Item $xdone -Force }
$xr = UC @('run_script', '--file', $xdrv, '--entry', 'X.Tour.Install', '--args', ('[\"' + $xspec + '\"]'))
Say ('XTOUR-INSTALL ' + ($xr -replace "`r?`n", ' '))

# Do NOT wait for the tour to finish: the X tour EXITS PLAY when it completes (measured
# 2026-09-24 11:33: "DontDestroyOnLoad can only be used in play mode" on our Install afterwards).
# So the driver goes in WHILE the tour runs; the tour drives the same panels, which is why the
# driver now re-emits once when a toggle closed a station (A-RETRY) and pre-closes in R8.
Start-Sleep -Seconds 75

# WARNING -- MEASURED FLAW (2026-09-24 13:13): the console is CUMULATIVE across sessions, so the wait loop
# below matched a STALE "[R8] MUTEX-END" line from the 11:23 session and stopped the editor 5 s
# after the install (mode B got cut short; the readings I needed were already in).  Clearing the
# console right before installing the driver removes the stale tokens => the done-marker match can
# only fire on THIS run's lines.
UC @('clear_console') | Out-Null
$inst = UC @('run_script', '--file', $drv, '--entry', 'D2U3P.Dump.Install', '--args', ('[\"' + $spec + '\"]'))
Say ('INSTALL ' + ($inst -replace "`r?`n", ' '))

$t0 = Get-Date
$verdict = 'TIMEOUT'
while (((Get-Date) - $t0).TotalSeconds -lt 180) {
    Start-Sleep -Seconds 5
    $tail = UC @('console', '--tail', '12')
    if ($tail -match 'MUTEX-END') { $verdict = 'R8-DONE'; break }
    if ($tail -match 'DONE cells=') { $verdict = 'DRIVER-DONE'; break }
    if ($tail -match 'ABORT no-stage') { $verdict = 'ABORT-NO-STAGE'; break }
}
Say ('WAIT-VERDICT ' + $verdict + ' elapsed=' + [math]::Round(((Get-Date) - $t0).TotalSeconds, 0) + 's')

Start-Sleep -Seconds 2
# v2 rule 4: only stop the editor while the lock is still MINE (if someone took it over,
# stopping their session would be the exact accident we are fixing) -- and mark the batch.
Stop-Editor-Safe 'final'
# team rule (2026-09-24): `editor_stop` returns BEFORE Play actually exits (backup-scene restore),
# so releasing the lock immediately opens a "no lock but playMode=playing" window for the next slice.
# Poll playMode=stopped (bounded 5 x 1s) BEFORE releasing; log how many polls it took.
$stopPolls = -1
for ($w = 0; $w -lt 5; $w++) {
    $ps = UC @('editor_status')
    if ($ps -match '"playMode"\s*:\s*"stopped"') { $stopPolls = $w; break }
    Start-Sleep -Seconds 1
}
if ($stopPolls -ge 0) { Say ('EDITOR-PLAYSTOPPED polls=' + $stopPolls) }
else { Say 'EDITOR-PLAYSTOPPED-TIMEOUT polls=5 (playMode still not stopped within 5s => a "lock free but playing" window may open; registered honestly)' }
Release-Lock
Say 'STOPPED'

$cons = UC @('console', '--tail', '800')
[System.IO.File]::WriteAllText($freeze, $cons, (New-Object System.Text.UTF8Encoding($false)))
$keep = @($cons -split "`r?`n" | Where-Object { $_ -match '\[D2U3P\]|\[R8\]|CLOSE-|POPUPMASK|MUTEX' })
[System.IO.File]::WriteAllLines($ev, [string[]]$keep, (New-Object System.Text.UTF8Encoding($false)))
Say ('LOGWRITE ' + $freeze + ' / ' + $ev + ' lines=' + $keep.Count)
foreach ($l in $keep) { Say ('EV ' + $l) }
if (Test-Path $dump) { Say ('DUMP-BYTES ' + (Get-Item $dump).Length) }

# ARTIFACT OWNERSHIP (gates/audits attribute evidence BY FILE NAME -- a shared driver writing a
# fixed name made one slice's readings look like another's).  The shared driver writes
# u3_popups_dump.txt / u3_popups_shots.tsv; this slice PUBLISHES byte-identical copies under its own
# tag.  The old names are NOT deleted (same bytes, they stay as the shared-driver artefacts).
$shotsIdx  = Join-Path $test 'u3_popups_shots.tsv'
$mineDump  = Join-Path $test 'u53_closefix_dump.txt'
$mineShots = Join-Path $test 'u53_closefix_shots.tsv'
foreach ($pair in @(,@($dump, $mineDump)) + @(,@($shotsIdx, $mineShots))) {
    $src = $pair[0]; $dst = $pair[1]
    if (-not (Test-Path $src)) { Say ('ARTIFACT-COPY skip (missing ' + $src + ')'); continue }
    Copy-Item $src $dst -Force
    $h1 = (Get-FileHash $src -Algorithm SHA256).Hash
    $h2 = (Get-FileHash $dst -Algorithm SHA256).Hash
    Say ('ARTIFACT-COPY ' + (Split-Path $dst -Leaf) + ' sha256Match=' + ($h1 -eq $h2) + ' bytes=' + (Get-Item $dst).Length)
}
Say 'END'
