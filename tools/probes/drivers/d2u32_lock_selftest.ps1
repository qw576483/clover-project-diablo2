# =============================================================================
# d2u32_lock_selftest.ps1 -- OFFLINE self-test: team lock protocol v2.2 + script hygiene
#                            + STATIC assertions about d2u32_run.ps1 (doubly checked).
#
#   Run:  powershell -NoProfile -ExecutionPolicy Bypass -File d2u32_lock_selftest.ps1
#   Exit: 0 = all PASS, 1 = at least one FAIL.  Seconds, offline, NEVER touches the real lock.
#
#   It dot-sources d2u32_lockproto.ps1 -- the SAME file d2u32_run.ps1 dot-sources -- so the
#   decision assertions cannot drift from production (nothing is mirrored).
#
#   Two families of false verdicts are both defended here (team lead 2026-09-24):
#     FALSE GREEN = the judge cannot fail        -> every static check gets a known-WRONG sample
#     FALSE RED   = the judge judges the wrong thing -> line numbers come from the AST, never from
#                   regexing raw text: charstat hit exactly that when a HEADER COMMENT mentioning
#                   `editor_play` made a correct runner look wrong.
#
#   Team cases carried in (each from a real near-miss):
#     v2.1    missing PID field => UNKNOWN, not dead               (classcols)
#     BOM     a BOM must not make our OWN lock look foreign        (u52play #2)
#     $pid    a function parameter named $pid throws at CALL time  (u52play #1)
#     static  editor_stop ONLY inside Stop-IfMine; editor_play/recompile after LOCK-TAKEN (u52play #3)
#     v2.2    legacy mirror written; release deletes BOTH names    (team lead)
#
# ASCII only (PS 5.1 parses a BOM-less non-ASCII .ps1 as ANSI). An earlier version of THIS file
# violated that with U+2500 separators and the sibling encoding gate caught it -- keep it ASCII.
# =============================================================================

$ErrorActionPreference = 'Stop'

$proto = Join-Path $PSScriptRoot 'd2u32_lockproto.ps1'
$runner = Join-Path $PSScriptRoot 'd2u32_run.ps1'
foreach ($f in @($proto, $runner)) {
    if (-not (Test-Path $f)) { Write-Host ('FATAL missing ' + $f); exit 1 }
}
. $proto

$fail = 0
$n = 0

function Assert([string]$what, [bool]$ok, [string]$detail) {
    $script:n = $script:n + 1
    if (-not $ok) { $script:fail = $script:fail + 1 }
    Write-Host (($(if ($ok) { '[ OK ]' } else { '[FAIL]' })) + ' ' + $what + '   (' + $detail + ')')
}

# decision case with INJECTED liveness (pid 4242 counts as "alive")
function CaseD([string]$what, [string]$raw, [double]$age, [bool]$expectStale) {
    $alive = { param($lockPid) ($lockPid -eq '4242') }
    $got = [bool](Test-LockStale -raw $raw -ageMinutes $age -isAlive $alive)
    Assert $what ($got -eq $expectStale) (Format-LockFields (Parse-LockFields $raw) $age $got)
}

# ---- static checker: returns the booleans instead of asserting, so it can be run on BOTH a known
# ---- CORRECT file (real runner) and known WRONG files (negative samples). AST-based on purpose.
function Get-StaticChecks([string]$path) {
    $res = [pscustomobject]@{
        ParseOk = $false; EditorStopOnceInsideGuard = $false; EditorPlayAfterLock = $false
        RecompileAfterLock = $false; LockWriteAscii = $false; NoPidParam = $false
        LegacyMirrorWritten = $false; ReleaseDeletesBoth = $false; ReadsBothLockNames = $false
        SayUsesWriteHostNoWriteOutput = $false; NoBareIfOnReturningFunc = $false
        RunnerFingerprintEmitted = $false
        ContinuesInsideLoops = $false; ContinueCount = 0
        SelfCheckOnlyBeforeLock = $false
        StopTriggerUncond = 0
    }
    $errs = $null
    $ast = [System.Management.Automation.Language.Parser]::ParseFile($path, [ref]$null, [ref]$errs)
    $res.ParseOk = ((@($errs).Count) -eq 0)
    if (-not $res.ParseOk) { return $res }
    $text = [System.IO.File]::ReadAllText($path)
    $strings = @($ast.FindAll({ param($node) $node -is [System.Management.Automation.Language.StringConstantExpressionAst] }, $true))

    $stops = @($strings | Where-Object { $_.Value -eq 'editor_stop' })
    $fn = $ast.FindAll({ param($node) $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Stop-IfMine' }, $true) | Select-Object -First 1
    $lo = -1; $hi = -1
    if ($null -ne $fn) { $lo = $fn.Extent.StartLineNumber; $hi = $fn.Extent.EndLineNumber }
    $inside = @($stops | Where-Object { $_.Extent.StartLineNumber -ge $lo -and $_.Extent.StartLineNumber -le $hi })
    $res.EditorStopOnceInsideGuard = (($stops.Count -eq 1) -and ($inside.Count -eq 1))

    # the LOCK-TAKEN marker comes from the AST as well (a comment mentioning it must NOT count)
    $mk = @($strings | Where-Object { $_.Value -like 'LOCK-TAKEN*' } | Select-Object -First 1)
    $mkLine = 0
    if ($mk.Count -ge 1) { $mkLine = $mk[0].Extent.StartLineNumber }
    $pl = @($strings | Where-Object { $_.Value -eq 'editor_play' } | Select-Object -First 1)
    $rc = @($strings | Where-Object { $_.Value -eq 'recompile' } | Select-Object -First 1)
    $pLine = 0; if ($pl.Count -ge 1) { $pLine = $pl[0].Extent.StartLineNumber }
    $rLine = 0; if ($rc.Count -ge 1) { $rLine = $rc[0].Extent.StartLineNumber }
    $res.EditorPlayAfterLock = (($mkLine -gt 0) -and ($pLine -gt $mkLine))
    $res.RecompileAfterLock = (($mkLine -gt 0) -and ($rLine -gt $mkLine))

    $res.LockWriteAscii = ($text -match '(?s)Set-Content -Path \$lock -Value [^\r\n]*-Encoding ASCII')
    # the trace must carry its OWN identity (team lead 2026-09-24): hand-pasted fingerprints go stale
    # within minutes and then produce false-red line lookups. Measured inside this function on purpose:
    # the first version of this assertion read a function-local from the caller's scope and was a
    # FALSE RED (2nd self-inflicted false red today -- the harness needs double-checking too).
    $res.RunnerFingerprintEmitted = ($text -match 'RUNNER-FINGERPRINT sha256_16=')
    # The TWO columns must be counted SEPARATELY (team lead 2026-09-24): reading both names protects
    # only US; WRITING the mirror protects OTHERS (the 17 historical runners that only read play.lock).
    # A single "mentionsLegacy" flag would have reported a false all-green for 4 real holes.
    $res.LegacyMirrorWritten = ($text -match '(?s)Set-Content -Path \$legacyLock -Value [^\r\n]*-Encoding ASCII')
    $relFn = $ast.FindAll({ param($node) $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Release-Lock' }, $true) | Select-Object -First 1
    $relLo = -1; $relHi = -1
    if ($null -ne $relFn) { $relLo = $relFn.Extent.StartLineNumber; $relHi = $relFn.Extent.EndLineNumber }
    $duoInside = 0; $duoOutside = 0
    $linesTxt = [System.IO.File]::ReadAllLines($path)
    for ($i = 0; $i -lt $linesTxt.Length; $i++) {
        if ($linesTxt[$i].IndexOf('@($lock, $legacyLock)') -ge 0) {
            $ln = $i + 1
            if ($ln -ge $relLo -and $ln -le $relHi) { $duoInside++ } else { $duoOutside++ }
        }
    }
    $res.ReleaseDeletesBoth = ($duoInside -ge 1)      # release column (inside the Release-Lock function)
    $res.ReadsBothLockNames = ($duoOutside -ge 1)     # wait-loop column (read = protect ourselves)
    $pidParam = @($ast.FindAll({ param($node) $node -is [System.Management.Automation.Language.ParameterAst] -and $node.Name.VariablePath.UserPath -eq 'pid' }, $true))
    $res.NoPidParam = ($pidParam.Count -eq 0)

    # read-only entry (team lead 2026-09-24): a runner must BE ABLE to report its self-check without
    # taking the lock, and that exit must sit BEFORE the LOCK-TAKEN marker -- otherwise "let me just
    # read the self-check" inevitably means "start it and kill it", which is exactly how jitter got a
    # zombie lock. AST/line-based, never regex-on-raw-text (comments mentioning the marker must not count).
    $scParam = @($ast.FindAll({ param($node) $node -is [System.Management.Automation.Language.ParameterAst] -and $node.Name.VariablePath.UserPath -eq 'SelfCheckOnly' }, $true))
    # NOTE: `-like 'SELFCHECK-ONLY-END*'` (4th self-inflicted FALSE RED of the day: the runner emits
    # 'SELFCHECK-ONLY-END locks-after=[...' -- one CONCATENATED literal, so `-eq 'SELFCHECK-ONLY-END'`
    # found nothing and the correct runner looked wrong). Same shape as the LOCK-TAKEN lookup above.
    $scStr = @($strings | Where-Object { $_.Value -like 'SELFCHECK-ONLY-END*' } | Select-Object -First 1)
    $scLine = 0
    if ($scStr.Count -ge 1) { $scLine = $scStr[0].Extent.StartLineNumber }
    $res.SelfCheckOnlyBeforeLock = (($scParam.Count -ge 1) -and ($scLine -gt 0) -and ($mkLine -gt 0) -and ($scLine -lt $mkLine))

    # classcols found the trap: a fail-closed gate pasted OUTSIDE the take-lock retry loop silently
    # runs NOTHING (exit 0). 6 runners were about to paste `...; continue` two-liners, so the team
    # lead made this check RESIDENT (2026-09-24) -- and it scans the WHOLE file, so a continue glued
    # in by a LATER round is covered too (not just today's known sites).
    $cCount = 0
    $res.ContinuesInsideLoops = Test-ContinuesInsideLoops -src $text -count ([ref]$cCount)
    $res.ContinueCount = $cCount

    # existence != reachability (see the helper above): >=1 unconditional stop-guard call, or -1 on a
    # parse error (fail-closed) -- a guard that is never reached must NOT look healthy.
    $res.StopTriggerUncond = Get-UncondStopGuardCalls $text

    # u52play bug #4 (false-exclusive): a printing function using Write-Output pollutes the caller's
    # return value -- PS makes `if (F)` true for a NON-EMPTY ARRAY, so @('log line', $false) is truthy
    # => "lost the lock race but proceeded anyway". Static + comment-free checks (a stub Say in a
    # sandbox would hide exactly this -- hence a STATIC assertion, not a runtime stub).
    $sayFn = $ast.FindAll({ param($node) $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Say' }, $true) | Select-Object -First 1
    if ($null -ne $sayFn) {
        $sayBody = $sayFn.Extent.Text
        $res.SayUsesWriteHostNoWriteOutput = (($sayBody -match 'Write-Host') -and ($sayBody -notmatch 'Write-Output'))
    }
    $codeLines = @()
    foreach ($ln in $linesTxt) { if ($ln -notmatch '^\s*#') { $codeLines += $ln } }
    $code = ($codeLines -join "`n")
    $bareIf = ($code -match 'if\s*\([^)]*Stop-IfMine')
    $allGuarded = $true
    foreach ($l in @($codeLines | Where-Object { $_ -match 'Stop-IfMine' })) {
        # the DEFINITION line is not a call site (first run of this check produced a FALSE RED on
        # `function Stop-IfMine([string]$why) {` -- the "judge stricter than the requirement" family)
        if ($l -match '^\s*function\s+Stop-IfMine') { continue }
        if (($l -notmatch '=\s*Stop-IfMine') -and ($l -notmatch '\(Stop-IfMine')) { $allGuarded = $false }
    }
    $res.NoBareIfOnReturningFunc = ((-not $bareIf) -and $allGuarded)
    return $res
}

# ---- continue-in-loop guard (team lead 2026-09-24: RESIDENT, not a one-off) -------------------
# A fail-closed gate must live INSIDE the take-lock retry loop. Pasted outside it, the runner exits
# 0 having done NOTHING -- a silent hole that looks like a normal run (classcols; 6 runners were
# about to glue exactly those two lines in). It takes an arbitrary SOURCE STRING so the very same
# logic can be pointed at fragments in the negative-sample section (no mirroring => no drift).
#   allowed ancestors = the 5 loop statements; the walk STOPS at a function boundary (a continue
#   inside a nested function is NOT in that loop at runtime) and `switch` is NOT accepted as a loop
#   (deliberately stricter: if a future runner needs it, widen it here in ONE place).
function Test-ContinuesInsideLoops([string]$src, [ref]$count) {
    $errs = $null
    $ast = [System.Management.Automation.Language.Parser]::ParseInput($src, [ref]$null, [ref]$errs)
    $all = @($ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.ContinueStatementAst] }, $true))
    $count.Value = $all.Count
    foreach ($c in $all) {
        $inLoop = $false
        $p = $c.Parent
        while ($null -ne $p) {
            if ($p -is [System.Management.Automation.Language.FunctionDefinitionAst]) { break }
            if (($p -is [System.Management.Automation.Language.ForStatementAst]) -or
                ($p -is [System.Management.Automation.Language.ForEachStatementAst]) -or
                ($p -is [System.Management.Automation.Language.WhileStatementAst]) -or
                ($p -is [System.Management.Automation.Language.DoWhileStatementAst]) -or
                ($p -is [System.Management.Automation.Language.DoUntilStatementAst])) { $inLoop = $true; break }
            $p = $p.Parent
        }
        if (-not $inLoop) { return $false }
    }
    return $true
}

# ---- REACHABILITY of the stop trigger (team lead 2026-09-24, from d2u27's incident) --------------
# EXISTENCE != REACHABILITY. d2u27's "editor_stop exactly once, inside the guard" was GREEN while the
# normal shutdown path never called it: the locks were released and the editor stayed in Play for 6.5
# minutes until the team lead cleaned up by hand. So: count the stop guard's CALL SITES that sit on an
# UNCONDITIONAL path (no IfStatementAst anywhere up the Parent chain). Parse failure => -1 => FAIL
# (fail-closed). Reference reading from u52resist: unconditionalStopGuardCalls=2 verdict=PASS.
function Get-UncondStopGuardCalls([string]$src) {
    $errs = $null
    $ast = [System.Management.Automation.Language.Parser]::ParseInput($src, [ref]$null, [ref]$errs)
    if (@($errs).Count -gt 0) { return -1 }
    $n = 0
    foreach ($c in @($ast.FindAll({ param($x) $x -is [System.Management.Automation.Language.CommandAst] -and $x.GetCommandName() -eq 'Stop-IfMine' }, $true))) {
        $cond = $false
        $p = $c.Parent
        while ($null -ne $p) {
            if ($p -is [System.Management.Automation.Language.IfStatementAst]) { $cond = $true; break }
            $p = $p.Parent
        }
        if (-not $cond) { $n = $n + 1 }
    }
    return $n
}

Write-Host '=== d2u32 lock protocol v2.2 + hygiene self-test (shared logic, no mirror) ==='

# --- 0) SELF-ENCODING (the header rule of this very file, violated twice today) ----------------
# PS 5.1 parses a BOM-less non-ASCII .ps1 as ANSI -> garbled literals. Both times the culprit was a
# pretty glyph inside a COMMENT. A self-check turns "I remember to keep it ASCII" into a machine check.
$selfBytes = [System.IO.File]::ReadAllBytes($PSCommandPath)
$selfNa = 0
foreach ($z in $selfBytes) { if ($z -gt 127) { $selfNa = $selfNa + 1 } }
Assert 'static[self]: this self-test file is pure ASCII (a BOM-less non-ASCII .ps1 parses as ANSI)' `
    ($selfNa -eq 0) ('nonAsciiBytes=' + $selfNa + ' (a glyph in a comment counts)')

# --- 1) parsing ------------------------------------------------------------------------------
$a = Parse-LockFields 'u53-closefix 2026-09-24T11:50:07.3360183+08:00'
$b = Parse-LockFields 'u32re-take 2026-09-24T11:47:34.5458051+08:00 1432'
Assert 'parse: 2-field => HasPid=false / 3-field => HasPid=true' `
    ((-not $a.HasPid) -and $b.HasPid -and ($b.Pid -eq '1432')) `
    ('owner=' + $a.Owner + '/' + $b.Owner + ' pid=' + $b.Pid)

# --- 2) v2.1: missing PID is UNKNOWN, not dead -----------------------------------------------
CaseD 'fresh-no-pid-is-NOT-stale (age=1m, no PID)  <-- classcols' 'u53-closefix 2026-09-24T11:50:07+08:00' 1.0 $false
CaseD 'old-no-pid-IS-stale       (age=13m, no PID)'               'u53-closefix 2026-09-24T11:50:07+08:00' 13.0 $true

# --- 3) 3-field cases + the 12 min boundary ---------------------------------------------------
CaseD 'dead-pid-IS-stale          (age=1m, pid dead)'   'shopart 2026-09-24T11:42:16+08:00 999999' 1.0 $true
CaseD 'live-pid-fresh-NOT-stale   (age=1m, pid alive)'  'u52play 2026-09-24T11:37:04+08:00 4242'   1.0 $false
CaseD 'live-pid-old-IS-stale      (age=13m, pid alive)' 'u52play 2026-09-24T11:37:04+08:00 4242'  13.0 $true
CaseD 'threshold-11.98m-NOT-stale (pid alive)'          'u52play 2026-09-24T11:37:04+08:00 4242'  11.98 $false
CaseD 'threshold-12.00m-IS-stale  (pid alive)'          'u52play 2026-09-24T11:37:04+08:00 4242'  12.0 $true

# --- 4) malformed content --------------------------------------------------------------------
CaseD 'garbage-fresh-NOT-stale    (age=2m)'  'not-a-lock-line' 2.0 $false
CaseD 'garbage-old-IS-stale       (age=13m)' 'not-a-lock-line' 13.0 $true

# --- 5) BOM must not make our OWN lock look foreign (u52play #2) ------------------------------
$bom = [char]0xFEFF
$zb = [char]0x200B
$c = Parse-LockFields ($bom + 'u32re-take 2026-09-24T11:47:34+08:00 1432')
Assert 'BOM-prefixed lock parses to the same owner/pid' `
    ($c.Owner -eq 'u32re-take' -and $c.HasPid -and ($c.Pid -eq '1432')) `
    ('owner=' + $c.Owner + ' pid=' + $c.Pid + ' hasPid=' + $c.HasPid)
Assert 'BOM/ZWSP-prefixed lock still matches the mine-pattern' `
    (((Remove-ZeroWidth ($bom + 'u32re-take x 1')) -match '^\s*(u32-close|u32re-take)\b') -and
     ((Remove-ZeroWidth ($zb + $bom + 'u32-close x 1')) -match '^\s*(u32-close|u32re-take)\b')) `
    'Remove-ZeroWidth + ^\s*(u32-close|u32re-take)\b'

# --- 6) REAL liveness path (no injected scriptblock => the default, tested here) --------------
$liveRaw = 'probe 2026-01-01T00:00:00+08:00 ' + $PID
Assert 'real Test-PidAlive: our own PID is alive => fresh lock NOT stale' `
    (-not [bool](Test-LockStale -raw $liveRaw -ageMinutes 1.0)) ('pid=' + $PID + ' -> stale=False')
Assert 'real Test-PidAlive: pid 999999 is dead => stale' `
    ([bool](Test-LockStale -raw 'probe 2026-01-01T00:00:00+08:00 999999' -ageMinutes 1.0)) `
    'pid=999999 -> stale=True'
Assert 'Test-PidAlive(abc) is False (non-numeric pid value)' ((Test-PidAlive 'abc') -eq $false) 'Test-PidAlive(abc) = False'

# --- 7) static checks on the REAL runner (known CORRECT sample => all must be green) ----------
$r = Get-StaticChecks $runner
Assert 'static[real]: runner parses with the REAL parser' $r.ParseOk 'no fake PARSE-OK'
Assert 'static[real]: editor_stop exactly once and ONLY inside Stop-IfMine' $r.EditorStopOnceInsideGuard 'see check'
Assert 'static[real]: editor_play sits AFTER the LOCK-TAKEN marker' $r.EditorPlayAfterLock 'AST-based'
Assert 'static[real]: recompile sits AFTER the LOCK-TAKEN marker' $r.RecompileAfterLock 'AST-based'
Assert 'static[real]: the real lock is written with -Encoding ASCII' $r.LockWriteAscii 'no BOM'
Assert 'static[real]: no function parameter is named $pid' $r.NoPidParam 'read-only automatic var'
Assert 'static[real]: runner prints its OWN fingerprint (RUNNER-FINGERPRINT sha256_16=) at startup' `
    $r.RunnerFingerprintEmitted 'team lead 2026-09-24: traces carry their own identity'
Assert 'static[real]: EVERY continue in the runner sits inside a loop (fail-closed gate must not land outside it)' `
    $r.ContinuesInsideLoops ('whole file, AST ancestors; continues=' + $r.ContinueCount + ' (classcols)')
Assert 'static[real]: the runner really HAS >=1 continue (else the check above is vacuous)' `
    ($r.ContinueCount -ge 1) ('continues=' + $r.ContinueCount + ' -- anti-vacuous guard')
Assert 'static[real]: -SelfCheckOnly exists AND its END marker sits BEFORE LOCK-TAKEN (read-only entry)' `
    $r.SelfCheckOnlyBeforeLock 'read the self-check WITHOUT taking the lock (jitter zombie-lock)'

# README 5.2 #42 (team lead 2026-09-24): release ONLY after the play session really ended, and never
# hand over a playing editor silently. AST string constants (a COMMENT mentioning these tokens must
# not satisfy the check -- that is the very reason regex is banned for markers).
$ra = [System.Management.Automation.Language.Parser]::ParseFile($runner, [ref]$null, [ref]$null)
$rs = @($ra.FindAll({ param($n) $n -is [System.Management.Automation.Language.StringConstantExpressionAst] }, $true))
$stopL = 0; $rbL = 0; $orL = 0
$x1 = @($rs | Where-Object { $_.Value -eq 'editor_stop' } | Select-Object -First 1); if ($x1.Count -ge 1) { $stopL = $x1[0].Extent.StartLineNumber }
$x2 = @($rs | Where-Object { $_.Value -like 'STOP-READBACK*' } | Select-Object -First 1); if ($x2.Count -ge 1) { $rbL = $x2[0].Extent.StartLineNumber }
$x3 = @($rs | Where-Object { $_.Value -like 'ORPHAN-PLAY*' } | Select-Object -First 1); if ($x3.Count -ge 1) { $orL = $x3[0].Extent.StartLineNumber }
Assert 'static[real]: playMode=stopped readback exists AFTER editor_stop (lock released only after the session ended)' `
    (($stopL -gt 0) -and ($rbL -gt $stopL)) ('editor_stop@L' + $stopL + ' STOP-READBACK@L' + $rbL)
Assert 'static[real]: ORPHAN-PLAY alarm present (rule 3: no silent hand-over of a playing editor)' `
    ($orL -gt 0) ('ORPHAN-PLAY@L' + $orL)
# ADJUDICATION 2026-09-24 (team lead revoked "release + alert"): an orphan play must WITHHOLD the lock
# and alert. EXISTENCE is the right strength here -- team lead's own ruling is that "which comes first"
# is not statically decidable (definition order != execution order), so a real run is the evidence.
$rt = [System.IO.File]::ReadAllText($runner)
Assert 'static[real]: orphan-play path withholds the lock + alerts elsewhere than the trace' `
    (($rt -match 'LOCK-RELEASE WITHHELD') -and ($rt -match 'withholdRelease') -and ($rt -match 'ORPHAN-PLAY-alert')) `
    'existence only (order needs a real run)'
# REACHABILITY (team lead 2026-09-24, README 5.2 #42 item 4): the stop trigger must be CALLED on an
# unconditional path -- otherwise releasing the lock looks fine while the editor stays in Play.
Assert 'static[real]: the stop trigger is REACHABLE (>=1 unconditional Stop-IfMine call)' `
    ($r.StopTriggerUncond -ge 1) ('unconditionalStopGuardCalls=' + $r.StopTriggerUncond + ' (d2u27 shape = 0)')
# NOTE -- SINGLE quotes on purpose: in a double-quoted string PowerShell EXPANDS $x (=> empty => parse
# error => the helper's fail-closed -1). That was the 5th self-inflicted false red today, and this time
# the NEGATIVE sample caught it -- i.e. the three-way rule also protects itself.
$uCond = Get-UncondStopGuardCalls 'if ($x) { Stop-IfMine ''why'' }'
$uUncond = Get-UncondStopGuardCalls "[void](Stop-IfMine 'why')"
Assert 'negative sample: a stop guard that lives ONLY inside an if => 0 unconditional (check can fail)' `
    ($uCond -eq 0) ('conditional-only fragment = ' + $uCond)
Assert 'positive fragment: an unconditional stop guard => 1 (no blanket rejection)' `
    ($uUncond -eq 1) ('unconditional fragment = ' + $uUncond)
# three-way rule (README 5.2 #34): a fragment that stops and releases WITHOUT a readback must be RED
$frag = "Say ('editor_stop'); Say ('LOCK-RELEASED (mine)')"
$fa = [System.Management.Automation.Language.Parser]::ParseInput($frag, [ref]$null, [ref]$null)
$fs = @($fa.FindAll({ param($n) $n -is [System.Management.Automation.Language.StringConstantExpressionAst] }, $true))
$fragRb = @($fs | Where-Object { $_.Value -like 'STOP-READBACK*' }).Count
Assert 'negative sample: stop+release WITHOUT any readback yields 0 STOP-READBACK markers (check can fail)' `
    ($fragRb -eq 0) ('fragment STOP-READBACK count=' + $fragRb)

Assert 'static[real]: Say uses Write-Host and never Write-Output (no return-value pollution)' $r.SayUsesWriteHostNoWriteOutput 'u52play bug #4'
Assert 'static[real]: no bare if(Stop-IfMine); every call site takes the value explicitly' $r.NoBareIfOnReturningFunc '= or [void]( (comment-free)'
Assert 'static[real]: COLUMN-WRITE  legacy mirror is written with -Encoding ASCII' $r.LegacyMirrorWritten 'play.lock mirror (protects others)'
Assert 'static[real]: COLUMN-READ   wait loop inspects BOTH lock names' $r.ReadsBothLockNames 'protects ourselves'
Assert 'static[real]: COLUMN-RELEASE Release-Lock iterates BOTH lock names' $r.ReleaseDeletesBoth 'lock + legacyLock'

# --- 8) negative + false-red samples, in a PER-PID sandbox with try/finally (team lead 2026-09-24)
$root = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$tmp = Join-Path $root ('.ai-tmp/test/d2u32-lockcheck-' + $PID)
try {
    if (-not (Test-Path $tmp)) { New-Item -ItemType Directory -Path $tmp -Force | Out-Null }

    # --- continue-in-loop: BOTH directions on FRAGMENTS, so the whole-file check above can never be
    #     vacuously green (same helper, two texts: loop-less must be RED, looped must be GREEN).
    $cBad = 0
    $okBad = Test-ContinuesInsideLoops -src 'function F() { if ($x) { continue } }' -count ([ref]$cBad)
    $cGood = 0
    $okGood = Test-ContinuesInsideLoops -src 'while ($t) { if ($x) { continue } }' -count ([ref]$cGood)
    Assert 'negative sample: a continue OUTSIDE any loop is RED (gate pasted outside the retry loop)' `
        (($okBad -eq $false) -and ($cBad -eq 1)) ('ok=' + $okBad + ' n=' + $cBad)
    Assert 'negative sample: a continue INSIDE a while loop is GREEN (no blanket rejection)' `
        (($okGood -eq $true) -and ($cGood -eq 1)) ('ok=' + $okGood + ' n=' + $cGood)

    # The team lead's requirement is "it must ALSO catch a continue glued in by a FUTURE round", so
    # mutate the REAL runner text (append one top-level continue) and re-run the SAME whole-file
    # check on it. This is the direct proof that the resident check covers unknown future sites.
    $mutPath = Join-Path $tmp 'mutated_future_runner.ps1'
    $mutText = [System.IO.File]::ReadAllText($runner) + "`ncontinue`n"
    [System.IO.File]::WriteAllText($mutPath, $mutText, (New-Object System.Text.ASCIIEncoding))
    $mut = Get-StaticChecks $mutPath
    Assert 'future-proof: one EXTRA continue appended to the real runner turns the check RED' `
        (($mut.ParseOk -eq $true) -and ($mut.ContinuesInsideLoops -eq $false)) `
        ('parseOk=' + $mut.ParseOk + ' inLoops=' + $mut.ContinuesInsideLoops + ' n=' + $mut.ContinueCount)

    $badPath = Join-Path $tmp 'bad_runner.ps1'
    $badText = @'
param([int]$pid)
Unity-Cmd @('editor_play')
Unity-Cmd @('recompile')
Unity-Cmd @('editor_stop')
Set-Content -Path $lock -Value 'x 2026-01-01T00:00:00+08:00 1' -Encoding UTF8
Say ('LOCK-TAKEN ')
'@
    [System.IO.File]::WriteAllText($badPath, $badText, (New-Object System.Text.ASCIIEncoding))
    $rb = Get-StaticChecks $badPath
    $allRed = (-not $rb.EditorStopOnceInsideGuard) -and (-not $rb.EditorPlayAfterLock) -and
              (-not $rb.RecompileAfterLock) -and (-not $rb.LockWriteAscii) -and (-not $rb.NoPidParam) -and
              (-not $rb.LegacyMirrorWritten) -and (-not $rb.ReleaseDeletesBoth) -and (-not $rb.ReadsBothLockNames)
    Assert 'negative sample: a runner WITHOUT the fingerprint line is RED (fingerprint check can fail)' `
        ($rb.RunnerFingerprintEmitted -eq $false) ('rb.fingerprint=' + $rb.RunnerFingerprintEmitted)
    Assert 'negative sample: a runner WITHOUT -SelfCheckOnly is RED (read-only entry check can fail)' `
        ($rb.SelfCheckOnlyBeforeLock -eq $false) ('rb.selfCheckOnly=' + $rb.SelfCheckOnlyBeforeLock)
    Assert 'negative sample: known-WRONG runner makes ALL 8 static checks red (judge can fail)' `
        $allRed ('stop=' + $rb.EditorStopOnceInsideGuard + ' play=' + $rb.EditorPlayAfterLock +
                 ' rec=' + $rb.RecompileAfterLock + ' ascii=' + $rb.LockWriteAscii +
                 ' noPid=' + $rb.NoPidParam + ' mirror=' + $rb.LegacyMirrorWritten +
                 ' relBoth=' + $rb.ReleaseDeletesBoth + ' readsBoth=' + $rb.ReadsBothLockNames)

    # --- the exact hole this round's audit found (4 runners): "reads both, NEVER writes the mirror".
    #     The two columns MUST be able to disagree -- otherwise one flag hides the other's hole.
    $onlyReadPath = Join-Path $tmp 'readonly_runner.ps1'
    $onlyReadText = @'
param()
$mk = 'LOCK-TAKEN '
function Release-Lock() { foreach ($f in @($lock, $legacyLock)) { } }
$busy = $null
foreach ($f in @($lock, $legacyLock)) { }
'@
    [System.IO.File]::WriteAllText($onlyReadPath, $onlyReadText, (New-Object System.Text.ASCIIEncoding))
    $ro = Get-StaticChecks $onlyReadPath
    Assert 'column independence: reads-both=TRUE while mirror-written=FALSE (the 4-runner hole is RED, not hidden)' `
        (($ro.ReadsBothLockNames -eq $true) -and ($ro.LegacyMirrorWritten -eq $false)) `
        ('readsBoth=' + $ro.ReadsBothLockNames + ' mirror=' + $ro.LegacyMirrorWritten + ' relBoth=' + $ro.ReleaseDeletesBoth)

    # negative sample for u52play bug #4: a Say that writes with Write-Output => the static check must
    # go RED (this is the case a runtime stub CANNOT catch, which is why it is a static assertion).
    $woPath = Join-Path $tmp 'writeoutput_runner.ps1'
    $woText = @'
function Say([string]$s) { Write-Output $s }
function Stop-IfMine() { Say 'x'; return $false }
if (Stop-IfMine) { Say 'proceeded although it failed' }
'@
    [System.IO.File]::WriteAllText($woPath, $woText, (New-Object System.Text.ASCIIEncoding))
    $wo = Get-StaticChecks $woPath
    Assert 'negative sample: Write-Output in Say -> RED, and bare if(func) -> RED (false-exclusive found)' `
        (($wo.SayUsesWriteHostNoWriteOutput -eq $false) -and ($wo.NoBareIfOnReturningFunc -eq $false)) `
        ('sayOk=' + $wo.SayUsesWriteHostNoWriteOutput + ' noBareIf=' + $wo.NoBareIfOnReturningFunc)

    # false-red guard: a COMMENT mentioning editor_play, with the real call AFTER the marker, must stay OK
    $cmtPath = Join-Path $tmp 'comment_runner.ps1'
    $cmtText = @'
# this header comment mentions editor_play and LOCK-TAKEN on purpose
param()
Say ('LOCK-TAKEN ')
Unity-Cmd @('editor_play')
'@
    [System.IO.File]::WriteAllText($cmtPath, $cmtText, (New-Object System.Text.ASCIIEncoding))
    $rc2 = Get-StaticChecks $cmtPath
    Assert 'false-red guard: a comment mentioning editor_play does NOT flip the check (AST line numbers)' `
        ($rc2.EditorPlayAfterLock -eq $true) ('EditorPlayAfterLock=' + $rc2.EditorPlayAfterLock)
}
finally {
    if (Test-Path $tmp) { Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue }
}
Assert 'sandbox cleaned up (per-PID, try/finally)' (-not (Test-Path $tmp)) $tmp

Write-Host ('=== ' + ($n - $fail) + '/' + $n + ' PASS, FAIL=' + $fail + ' ===')
if ($fail -gt 0) { exit 1 }
exit 0
