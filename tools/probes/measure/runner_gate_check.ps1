# runner_gate_check.ps1 -- static, read-only checker for Play-runner traps.
# Owner: sheet u3bverify (moved from .ai-tmp/test/ on team-lead's authorisation, 2026-09-24;
# judgement assets live under tools/probes/ and are committed).
# Pure ASCII on purpose (team rule: a .ps1 must be pure ASCII or UTF-8 WITH BOM).
#
# WHY (every item below was found by hand today; a normal run never shows them):
#   C1  out-of-loop `continue` (classcols) ends the whole script silently with exit 0 --
#       no lock, no ledger row, no error at all.
#   C2  a missing terminal marker makes "it ran" indistinguishable from "it never ran".
#   C3  a hand-pasted fingerprint goes stale within minutes (5 instances today).
#   C4  a multi-line `Say (` continuation (team rule 5.2-29) dies with Missing closing ')'.
#   C5  a self-check must not take the lock => an unlocked entry (-SelfCheckOnly) must EXIST.
#       Its ORDER relative to the lock take is judged at RUNTIME by the owning sheet, not here:
#       a line-order classifier was implemented, measured, and found UNSOUND (it flagged 5 of 8
#       active runners including the reference implementation -- line order != execution order,
#       because the unlocked exit lives in a function/block that is DEFINED after the lock write).
#       That item was handed back to the team and now lives with sheet `waypoint`
#       (input: c:/Work/Server/f-v2/clover-project-diablo2/.ai-tmp/test/u3b-c5-order-diagnosis.txt, 7068 B).
#       ABSOLUTE on purpose -- measured 2026-09-24: this machine's PS location AND process CWD are the
#       WORKSPACE ROOT (c:/Work/Server/f-v2), so a relative `.ai-tmp/...` pointer resolves to MISSING
#       (Get-Item -> MISSING, [IO.File] -> throws).  Do not re-shorten this path.
#   C7  an existence/count assertion judged by a BARE WORD in the raw text (rule 5.2-45).  This file
#       itself carried TWO such OR-fallbacks ($txt -match 'END-MARKER' on C2's terminal marker, and
#       $txt -match '\$SelfCheckOnly\b' on C5's unlocked entry) plus a word-based stop-trigger count.
#       Fixed 2026-09-24 by sheet u3bverify, all three now AST / call form:
#         * the two fallbacks were removed -- measured first with a 58-runner probe: ZERO files had a
#           verdict that depended on them, so removal is verdict-neutral and only closes a false GREEN
#           (a new comment or warning MENTIONING the token satisfied an existence check);
#           RE-VERIFY that "0 files" claim (team rule (3)-2: a 0-hit conclusion needs a positive
#           control) with the committed harness tools/probes/measure/runner_gate_neutrality_control.ps1
#           -- it re-adds the two fallbacks into a $env:TEMP copy, runs BOTH builds -Scope all, diffs
#           all 58 rows, and requires the KNOWN-BAD2 sample to go red in the regressed build;
#         * the stop-trigger count was a real false RED: its loose matcher credited d2u26_run.ps1's own
#           KNOWN-BAD fixture builder (L523 `Set-Content -Value ("Unity-Cmd @('editor_stop')" + ...)`)
#           as an editor_stop CALL, printing calls=2(L367/L523) instead of the true 1(L367).
#       Keyword-only hits are still PRINTED (evidence is never deleted) but under a separate label.
#
# SCOPE -- keep this GATE GREEN.  drivers/ holds ~60 runners, most historical one-shots that
# predate these rules; checking them all yields a red wall that drowns the signal.
#   -Scope active : every item enforced (hard gate) on the 8-runner Play rotation.
#   -Scope all    : C1 structural item enforced repo-wide; CONVENTION items (terminal marker,
#                   fingerprint, unlocked entry, multi-line Say) are [INFO] for historical runners.
# A runner carrying a `# QUARANTINED:` line in its head is skipped (counted, never ignored).
param(
    [string]$ProjectRoot = 'c:/Work/Server/f-v2/clover-project-diablo2',
    [ValidateSet('active', 'all')][string]$Scope = 'active'
)

$ActiveRunners = @(
    'd2u26_run.ps1', 'd2u27_run.ps1', 'd2u3_charstat_run.ps1', 'd2u32_run.ps1',
    'shopgrid_run.ps1', 'u44hover_run.ps1', 'u52resist_run.ps1', 'u53close_run.ps1'
)
$loopTypes = @('WhileStatementAst', 'ForStatementAst', 'ForEachStatementAst', 'DoWhileStatementAst', 'DoUntilStatementAst')
# The Unity-Cmd wrappers that actually carry an `editor_stop` argument in this repo.  Measured
# 2026-09-24 over all 58 `*_run.ps1`: Unity-Cmd=128 / UC=7 / unity=4 hits, and NO other command name
# has one (`unity` is the shape `& unity command editor_stop`, d2u27_run.ps1:222).
$StopCmdNames = @('Unity-Cmd', 'UC', 'unity')

# ---------------------------------------------------------------------------
# Rule 5.2-45: judge "count/existence" by CALL FORM / AST NODE, never by a bare word.
# A literal that only appears as a needle (operand of -match/-like/-eq/-ne/...) or inside a
# comparison is NOT an emission: a new warning message that merely MENTIONS a token must not
# satisfy (or break) these checks.  Measured upstream: four sheets got a FALSE RED on an
# "appears exactly once" assertion the moment they added a message containing the word.
#
# THE TERMINAL-MARKER "OR" IS LOAD-BEARING -- do NOT tighten it to require LOCK-TAKEN.
# Verified on 2026-09-24 by reading the files: d2u26_run.ps1 emits NO "LOCK-TAKEN" literal at all
# (file-wide occurrence = 1 and it is a COMMENT at L606; Say-lines containing it = 0), yet it is an
# ACTIVE runner.  It satisfies this convention check solely through "END" (5 emissions).  A future
# "tightening" would turn d2u26 into a FALSE RED on the spot.
# Why a literal-based scan is already immune to the three upstream false-red variants:
#   comments      -> a comment produces no StringConstantExpressionAst at all (d2u26 L606 proves it);
#   needles       -> excluded by the ancestor walk below;
#   concatenation -> 'LOCK-TAKEN ' is its own literal, so "-match 'LOCK-TAKEN'" still sees it.
#                    (u52play cross-checked all 58 runners: probes based on EXACT equality would
#                     false-red ~56 of them, because most concatenate owner/pid into the marker.)
# BOTH SIDES OF THE OR ARE LOAD-BEARING -- a corrected census (u52play, 2026-09-24, algorithm stated):
#   LT+END = 33 | LOCK-TAKEN-only = 0 | END-only = 25 | both missing = 0
#     (recount by the file's owner, same algorithm, 2026-09-24 14:1x: both=33 / LT-only=0 / END-only=25
#      / neither=0 over tools/probes/drivers/*run*.ps1 = 58 files -- two independent implementations
#      agreed cell by cell, so the census does not depend on one person's matcher).
#   END-only includes d2u26_run.ps1, and d2u26 IS in this gate's hard set ($ActiveRunners, 8 runners)
#   => dropping the END branch makes THIS gate fail d2u26 on the spot.
#   Dropping the END branch       -> 1 HARD false red here (d2u26) + 24 [INFO] ones (e.g.
#                                    camjitter_run.ps1, a genuine END-only Play runner that is NOT in
#                                    this gate's rotation).  Quoting it as "25 hard" or "2 active"
#                                    overstates what THIS gate would report: only the hard set fails.
#   Tightening the LT form        -> d2u32_run.ps1 false-reds immediately (it emits 'END ...' with a
#                                    prefix, not a bare 'END').
#   NOTE the first census said "18 of 58 lack both" -- that was a MATCHER-ARTICLE artifact (a too
#   narrow 'END' quotation), i.e. the same family as 5.2-45: a narrow matcher flips "0 missing" into
#   "18 missing" without anything changing on disk.
# ---------------------------------------------------------------------------
function Get-EmissionInfo([object]$ast) {
    $res = @{ lock = @(); endm = @(); fp = @() }
    $lits = $ast.FindAll({
            param($x)
            ($x -is [System.Management.Automation.Language.StringConstantExpressionAst] -or
             $x -is [System.Management.Automation.Language.ExpandableStringExpressionAst])
        }, $true)
    foreach ($s in $lits) {
        $cur = $s.Parent; $needle = $false
        while ($cur) {
            if ($cur.GetType().Name -eq 'BinaryExpressionAst' -and ($cur.Operator.ToString() -match 'Match|Like|Eq|Ne|Contains')) { $needle = $true; break }
            $cur = $cur.Parent
        }
        if ($needle) { continue }
        $v = [string]$s.Value
        if ($v -match 'LOCK-TAKEN') { $res.lock += $s.Extent.StartLineNumber }
        # terminal marker must START with END (so "... SELFCHECK-ONLY-END" does NOT count -- that
        # would be a false GREEN in this very check: measured before the fix, it made END=True on
        # many historical runners whose only END occurrence was inside SELFCHECK-ONLY-END).
        $t = $v.Trim()
        if ($v -match 'END-MARKER' -or $t -eq 'END' -or $t.StartsWith('END ') -or $t.StartsWith('END-')) { $res.endm += $s.Extent.StartLineNumber }
        if ($v -match 'RUNNER-FINGERPRINT') { $res.fp += $s.Extent.StartLineNumber }
    }
    return $res
}
function Get-HasParam([object]$ast, [string]$name) {
    $ps = $ast.FindAll({ param($x) $x -is [System.Management.Automation.Language.ParameterAst] }, $true)
    foreach ($p in $ps) { if ($p.Name.VariablePath.UserPath -eq $name) { return $true } }
    return $false
}

# ---------------------------------------------------------------------------
# C7 -- the three bare-word judgments, each implemented ONCE (rule 5.2-45).
# The per-file loop and the KNOWN-BAD #2 sample at the bottom of this file MUST call these same
# functions: if the sample re-implemented them, a regression in the loop would leave the sample
# green -- a sample that cannot fail (rule 5.2-34).  Verify by editing one helper: the sample goes red.
# ---------------------------------------------------------------------------
function Test-HasTerminalMarker([object]$ast) {
    # AST only: needle-excluded string literals actually present in the file.  A comment produces NO
    # AST node at all, so a comment saying "END-MARKER" can never satisfy this (false GREEN closed).
    return ((Get-EmissionInfo $ast).endm.Count -gt 0)
}
function Test-HasUnlockedEntry([object]$ast) {
    # AST only: a REAL ParameterAst named SelfCheckOnly.  Measured 2026-09-24: on every one of the 8
    # active runners that ParameterAst is nested inside a function, NOT at script scope, so demanding
    # script scope would false-red all 8 -- existence is judged wherever it is declared.
    return (Get-HasParam $ast 'SelfCheckOnly')
}
function Get-StopCallInfo([object]$ast) {
    # A CALL requires (a) a Unity-Cmd wrapper name and (b) an exact 'editor_stop' string literal
    # argument.  Anything else that merely mentions the word lands in keywordOnly (evidence kept, but
    # it can never be counted as a call).  See the long note at the per-file call site.
    $r = @{ calls = @(); uncond = 0; keywordOnly = @() }
    foreach ($c in $ast.FindAll({ param($x) $x -is [System.Management.Automation.Language.CommandAst] }, $true)) {
        $lit = @($c.FindAll({ param($x) $x -is [System.Management.Automation.Language.StringConstantExpressionAst] -and $x.Value -eq 'editor_stop' }, $true))
        if ($lit.Count -eq 0) {
            if ($c.Extent.Text -match 'editor_stop') { $r.keywordOnly += ('L' + $c.Extent.StartLineNumber) }
            continue
        }
        if ($StopCmdNames -notcontains $c.GetCommandName()) { $r.keywordOnly += ('L' + $c.Extent.StartLineNumber + '@' + $c.GetCommandName()); continue }
        $r.calls += $c.Extent.StartLineNumber
        $cur = $c.Parent; $cond = $false
        while ($cur) { if ($cur.GetType().Name -eq 'IfStatementAst') { $cond = $true; break }; $cur = $cur.Parent }
        if (-not $cond) { $r.uncond++ }
    }
    return $r
}

$dir = Join-Path $ProjectRoot 'tools/probes/drivers'
if (-not (Test-Path -LiteralPath $dir)) { Write-Host "FAIL drivers dir not found: $dir"; exit 1 }

$files = Get-ChildItem -LiteralPath $dir -File -Filter '*_run.ps1' | Sort-Object Name
if ($Scope -eq 'active') { $files = $files | Where-Object { $ActiveRunners -contains $_.Name } }

$fail = 0; $skipped = 0; $checked = 0
foreach ($f in $files) {
    # QUIESCENCE GUARD -- parsing a file that is being edited right now reports a TRANSIENT
    # parse error (measured: u44hover_run.ps1 at 13:30:11 gave 2 errors, at 13:30:56 gave 0).
    # BYTE-EXACT variant (2026-09-24 15:5x, found by auditing this checker's own fingerprint
    # order): the fingerprint must cover the SAME bytes the AST was built from, so it is taken
    # BEFORE and AFTER the parse.  The previous code hashed the file AFTER the parse, so a file
    # moved during the parse printed a self-consistent row (snap == now) whose sha16 labelled a
    # version the AST had NOT been built from -- i.e. the row claimed a fingerprint it had not
    # judged.  Now: hash moves during parse => the AST is ambiguous => SKIP (same conservative
    # treatment as the mtime rule), and the row can never carry a post-parse hash.
    $m1 = (Get-Item -LiteralPath $f.FullName).LastWriteTimeUtc
    $sha16Pre = (Get-FileHash -LiteralPath $f.FullName -Algorithm SHA256).Hash.Substring(0, 16)
    $tk = $null; $er = $null
    $ast = [System.Management.Automation.Language.Parser]::ParseFile($f.FullName, [ref]$tk, [ref]$er)
    $parseErr = @($er).Count
    $m2 = (Get-Item -LiteralPath $f.FullName).LastWriteTimeUtc
    $sha16Post = (Get-FileHash -LiteralPath $f.FullName -Algorithm SHA256).Hash.Substring(0, 16)
    if (($m1 -ne $m2) -or ($parseErr -ne 0) -or ($sha16Pre -ne $sha16Post)) {
        Start-Sleep -Milliseconds 1200
        $m3 = (Get-Item -LiteralPath $f.FullName).LastWriteTimeUtc
        if ($m3 -ne $m2) { Write-Host ("{0,-28} SKIP file is being edited (mtime moved during parse) -> re-run later" -f $f.Name); $skipped++; continue }
        $sha16Pre = (Get-FileHash -LiteralPath $f.FullName -Algorithm SHA256).Hash.Substring(0, 16)
        $tk = $null; $er = $null
        $ast = [System.Management.Automation.Language.Parser]::ParseFile($f.FullName, [ref]$tk, [ref]$er)
        $parseErr = @($er).Count
        $sha16Post = (Get-FileHash -LiteralPath $f.FullName -Algorithm SHA256).Hash.Substring(0, 16)
        if ($sha16Pre -ne $sha16Post) { Write-Host ("{0,-28} SKIP bytes moved during re-parse -> re-run later" -f $f.Name); $skipped++; continue }
    }

    # INPUT FINGERPRINT = the bytes the AST was built from (see the quiescence guard above).
    # At print time we re-hash and append UNSTABLE-AFTER-PARSE if the file moved under us, so a
    # reader can always tell WHICH version a row judged.
    $sha16Snap = $sha16Post
    $txt = Get-Content -LiteralPath $f.FullName -Raw
    $head = ($txt -split "`r?`n" | Select-Object -First 30) -join "`n"
    if ($head -match '#\s*QUARANTINED:') {
        Write-Host ("{0,-28} SKIP QUARANTINED (registered, not a gate target)" -f $f.Name); $skipped++; continue
    }

    $checked++
    $bad = @(); $contIn = 0; $contOut = 0; $info = @()
    foreach ($c in $ast.FindAll({ param($x) $x -is [System.Management.Automation.Language.ContinueStatementAst] }, $true)) {
        $cur = $c.Parent; $found = $false
        while ($cur) { if ($loopTypes -contains $cur.GetType().Name) { $found = $true; break }; $cur = $cur.Parent }
        if ($found) { $contIn++ } else { $contOut++; $bad += ("continue-outside-loop@L" + $c.Extent.StartLineNumber) }
    }
    $mlSay = @($ast.FindAll({ param($x) $x -is [System.Management.Automation.Language.CommandAst] -and $x.GetCommandName() -eq 'Say' -and $x.Extent.StartLineNumber -ne $x.Extent.EndLineNumber }, $true))
    # rule 5.2-45: existence is judged from the AST (needle-excluded literals / a real ParameterAst),
    # NOT from a bare word search in the raw text.
    # 2026-09-24 (sheet u3bverify): the two raw-text OR-fallbacks that used to hang off $hasEnd and
    # $hasUnlocked (`-or ($txt -match 'END-MARKER')` / `-or ($txt -match '\$SelfCheckOnly\b')`) are
    # GONE.  They matched the token anywhere in the file -- a comment or a new warning MESSAGE would
    # have satisfied these EXISTENCE checks (a false GREEN, the mirror image of the four-sheet false
    # RED rule 5.2-45 was written for).  Removed only after measuring: a probe over all 58 `*_run.ps1`
    # found ZERO files whose verdict depended on either fallback (END bare-word-only = 0,
    # unlocked bare-word-only = 0), so the removal is verdict-neutral and closes the hole.
    # Note: a comment can never contribute here -- the PowerShell parser emits no AST node for it.
    # Both go through the C7 helpers above so the KNOWN-BAD #2 sample judges by the SAME code.
    $em = Get-EmissionInfo $ast
    $hasLockTaken = ($em.lock.Count -gt 0)
    $hasEnd = Test-HasTerminalMarker $ast
    $hasFp = ($em.fp.Count -gt 0)
    $hasUnlocked = Test-HasUnlockedEntry $ast
    $isActive = ($ActiveRunners -contains $f.Name)

    if ($parseErr -ne 0) { $bad += "parseErr=$parseErr" }
    if ($contOut -ne 0) { $bad += "out-of-loop-continue=$contOut" }

    # Convention items: enforced for the active rotation, [INFO] for historical one-shots.
    # multi-line Say belongs here, not with the structural items: 26 of ~60 historical runners
    # carry one (almost always on their SUMMARY line) and they parse fine, so a hard rule over
    # the whole set is a 26-row red wall (team-lead ruling, 2026-09-24).
    $mlLabel = 'multiline-Say@' + (($mlSay | ForEach-Object { 'L' + $_.Extent.StartLineNumber }) -join '/')

    # C6 REACHABILITY (team-lead 2026-09-24, from u52resist's rule): EXISTENCE != REACHABILITY.
    # d2u27 had exactly one `editor_stop`, correctly inside its guard function -- the existence
    # assertion was green -- yet that path was unreachable on the normal shutdown, so both locks
    # were released while the editor stayed in Play for ~6.5 minutes (the only team-wide block
    # today).  Rule: at least ONE editor_stop CALL must sit on an unconditional path, i.e. no
    # IfStatementAst among its AST ancestors.  Call form only (a bare word / message text does not
    # count -- rule 5.2-45); a parse failure is NOT a red (fail-open here would fake a blocker).
    #
    # CALL FORM (2026-09-24, sheet u3bverify -- this was the ONE real false red of the three word-based
    # spots in this file).  A hit now requires BOTH:
    #   (a) the CommandAst's command name is a Unity-Cmd wrapper -- measured over all 58 `*_run.ps1`:
    #       only Unity-Cmd (128 editor_stop arg literals), UC (7) and unity (4) ever carry one;
    #   (b) that command contains a StringConstantExpressionAst whose VALUE IS EXACTLY 'editor_stop'.
    # Measured effect: d2u26_run.ps1 used to print calls=2(L367/L523); L523 is
    # `Set-Content -Path $fake -Value ("Unity-Cmd @('editor_stop')" + ...)`, i.e. that runner's OWN
    # KNOWN-BAD FIXTURE BUILDER -- a word matcher read a fixture as a call.  Call form gives 1(L367),
    # which matches the runner's own self-check ('12 editor_stop-only-once').
    # Nothing is hidden by the tightening: word-only hits are still listed, under keywordOnly=, so a
    # future runner that reaches the stop through a NEW wrapper shows up instead of silently vanishing.
    # The classifier lives in Get-StopCallInfo() above, so the KNOWN-BAD #2 sample calls the same code.
    # Side finding (u3bverify, measured): the NEW rule also fixes a false NEGATIVE -- the old matcher
    # required the token to be QUOTED, so `& unity command editor_stop` (d2u27_run.ps1:222, an
    # unquoted command-mode argument) was invisible and that runner printed calls=0().  See below.
    $sc = Get-StopCallInfo $ast
    $stopCalls = $sc.calls; $stopUncond = $sc.uncond; $stopKeywordOnly = $sc.keywordOnly
    $info += ('[INFO] stop-trigger-reachable calls=' + $stopCalls.Count + '(' + (($stopCalls | ForEach-Object { 'L' + $_ }) -join '/') + ') unconditional=' + $stopUncond + ' keywordOnly=' + $stopKeywordOnly.Count + '(' + ($stopKeywordOnly -join '/') + ')')

    foreach ($pair in @(
            @('no-terminal-marker', (-not ($hasLockTaken -or $hasEnd))),
            @('no-RUNNER-FINGERPRINT', (-not $hasFp)),
            @('no-unlocked-SelfCheckOnly-entry', (-not $hasUnlocked)),
            @($mlLabel, ($mlSay.Count -ne 0)))) {
        if ($pair[1]) { if ($isActive) { $bad += $pair[0] } else { $info += ('[INFO] ' + $pair[0]) } }
    }
    # C6 REACHABILITY -- DIAGNOSTIC ONLY, deliberately NOT a gate item (measured 2026-09-24):
    # the "no IfStatementAst ancestor" rule flags 3 of the active 8, and two of those are FALSE
    # POSITIVES: d2u32_run.ps1:182 (`Unity-Cmd @('editor_stop')` inside `if ($body -match $myOwners)`)
    # and shopgrid_run.ps1:188 (`UC @('editor_stop')` inside `if ((Test-MyLock) -eq $true)`) are
    # ownership guards, i.e. exactly the CONDITION that is TRUE on the normal path.  A generic rule
    # cannot tell "guard that is true when I own the lock" from "guard that is never true" -- only the
    # runner's author can, which is why this belongs in a PER-RUNNER self-check (u52resist's shape:
    # `SELF-CHK stop-trigger-reachable unconditionalStopGuardCalls=2 verdict=PASS`), not here.
    # It is still useful as a diagnostic: keywordOnly= surfaces runners where the word exists but no
    # CALL does.  CORRECTION 2026-09-24 (sheet u3bverify): the earlier claim here -- "d2u27_run.ps1's
    # only `editor_stop` hit is a needle inside its own self-check scan" -- is FALSE and was a matcher
    # artifact of exactly the 5.2-45 family.  d2u27_run.ps1:222 is a REAL call on an unconditional
    # path: `& unity command editor_stop --format json --no-pager`.  The old word matcher required the
    # token to be QUOTED ([`'"]editor_stop[`'"]`) and therefore could not see an unquoted command
    # argument, so it printed calls=0() for a runner that does call the stop.  Call form gives
    # calls=1(L222) unconditional=1.  (d2u27's real defect was reachability of the CALLER, which this
    # file deliberately does not judge -- see the note above about ownership guards.)
    if ($stopUncond -lt 1) { $info += '[INFO] stop-trigger-unreachable-by-this-rule (DIAGNOSTIC, not a gate: ownership guards count as conditional)' }

    # RAW EVIDENCE -- first occurrence of each token in the file text, needles included.  Kept
    # on purpose (team-lead: never delete evidence); it is NOT what the gate judges.
    $lines = Get-Content -LiteralPath $f.FullName -Encoding UTF8
    $i1 = (0..([Math]::Max(0, $lines.Count - 1)) | Where-Object { $lines[$_] -match 'SELFCHECK-ONLY-END' } | Select-Object -First 1)
    $i2 = (0..([Math]::Max(0, $lines.Count - 1)) | Where-Object { $lines[$_] -match 'LOCK-TAKEN' } | Select-Object -First 1)
    $endLine = '-'; if ($null -ne $i1) { $endLine = $i1 + 1 }
    $ltLine = '-'; if ($null -ne $i2) { $ltLine = $i2 + 1 }
    $info += ("[INFO] firstOccurrence SELFCHECK-ONLY-END=L$endLine LOCK-TAKEN=L$ltLine (needles included, not enforced)")

    $status = if ($bad.Count -eq 0) { 'PASS' } else { 'FAIL' }
    if ($bad.Count -ne 0) { $fail++ }
    $sha16Now = (Get-FileHash -LiteralPath $f.FullName -Algorithm SHA256).Hash.Substring(0, 16)
    $srcNote = ''
    if ($sha16Now -ne $sha16Snap) { $srcNote = (' [UNSTABLE-AFTER-PARSE now=' + $sha16Now + ' => this row judges the snapshot ' + $sha16Snap + ', not the current file]') }
    $line = "{0,-28} {1} parseErr={2} continue(in={3},out={4}) multiLineSay={5} LOCK-TAKEN={6} END={7} FP={8} unlockedEntry={9} sha16={10}" -f `
        $f.Name, $status, $parseErr, $contIn, $contOut, $mlSay.Count, $hasLockTaken, $hasEnd, $hasFp, $hasUnlocked, $sha16Snap
    if ($bad.Count -ne 0) { $line += ' :: ' + ($bad -join ',') }
    $line += $srcNote
    Write-Host $line
    if ($info.Count -ne 0) { Write-Host ('                             ' + ($info -join ' ; ')) }
}

# ---------------------------------------------------------------------------
# C5 ORDER -- ONE call site, and it is NOT here.  TEAM-LEAD RULING 2026-09-24 (decision 2):
#   ORDER is judged AT RUNTIME by the owning sheet (`-SelfCheckOnly`: prints SELFCHECK-ONLY-END,
#   the lock's before/after fingerprint is unchanged, and the ledger has 0 rows).  The STATIC side
#   keeps ONLY the unlocked-entry EXISTENCE item -- that is this file's `no-unlocked-SelfCheckOnly-entry`
#   (Test-HasUnlockedEntry).  No line-order / order classifer may be re-added here.
#   => CORRECTION (team-lead ruling 2026-09-24, after this sheet raised it as an open item):
#      c5_order_check.ps1 in tools/verify.ps1 item 14b is NOT the revoked thing and KEEPS FAIL => red.
#      What was revoked was only the bare-token LINE-ORDER classifier that used to live in THIS file --
#      it compared line numbers where execution order is meant, and flagged 5 of 8 active runners
#      including the reference implementation.  c5_order_check is a different animal: two events,
#      needles excluded, a function body resolved to its call site, INFO when either event cannot be
#      found -- it can only red when BOTH events exist AND A >= B, so it does not fake reds.
#      Sub-check 2 was also hardened in the same round: it now prints the child's denominator
#      files=/PASS=/FAIL=/INFO= and turns RED on files=0 (zero rows judged is never green); guard =
#      Test-C5Coverage() in verify.ps1, same-source sample = tools/probes/measure/c5_coverage_sample.ps1
#      (the sample extracts that function out of verify.ps1 via the AST -- it never re-implements it).
#   Offline reading taken 2026-09-24 by this sheet, using verify.ps1's exact invocation:
#   SUMMARY files=60 PASS=7 FAIL=0 INFO=53, `-Fixtures` expectations broken=0.
# It runs in tools/verify.ps1 item 14b (sub-check 2, sheet u3bverify's invocation, which quotes
# the args via -Command so the -Active list arrives as a real array).
# History: a line-order classifier used to live in THIS file; it was implemented, measured and
# found UNSOUND (5 of 8 active runners flagged, including the reference implementation: line
# order != execution order).  Took over by sheet waypoint, which resolves a function-body event
# to the FUNCTION'S FIRST CALL SITE and is allowed to answer [INFO] cannot-classify.
# A duplicate wrapper block was briefly added HERE on 2026-09-24 and then removed the same hour:
# two wrappers of one judge = two places to keep the "FAIL red / INFO log" contract, and this
# tool is itself invoked twice by 14b.  Judge: tools/probes/measure/c5_order_check.ps1
# (README 5.2 #43 / #46 explain why the wrapper must also check the child's own denominator).
# ---------------------------------------------------------------------------

# ---------------------------------------------------------------------------
# KNOWN-BAD / KNOWN-GOOD self-test -- keeps this checker falsifiable.  Without the RED row a
# green result proves nothing (team rule 5.2-34: real-file-green AND known-bad-red, same batch).
# Fixtures live in $env:TEMP (approved intentional deviation, 2026-09-24: zero litter in the
# repo tree, and it keeps "this script writes nothing under .ai-tmp" literally true) and are
# deleted in `finally`.
# ---------------------------------------------------------------------------
$tmpBad = Join-Path $env:TEMP ('rungate-knownbad-' + $PID + '.ps1')
Set-Content -LiteralPath $tmpBad -Encoding ASCII -Value @(
    'Write-Host "A"',
    'if ($true) { Write-Host "B"; continue }',
    'Say ("multi" +',
    '     "line")',
    'Write-Host "LOCK-TAKEN pid=1"'
)
$kBCont = $false; $kBSay = $false
try {
    $tk2 = $null; $er2 = $null
    $ast2 = [System.Management.Automation.Language.Parser]::ParseFile($tmpBad, [ref]$tk2, [ref]$er2)
    foreach ($c in $ast2.FindAll({ param($x) $x -is [System.Management.Automation.Language.ContinueStatementAst] }, $true)) {
        $cur = $c.Parent; $found = $false
        while ($cur) { if ($loopTypes -contains $cur.GetType().Name) { $found = $true; break }; $cur = $cur.Parent }
        if (-not $found) { $kBCont = $true }
    }
    $kBSay = (@($ast2.FindAll({ param($x) $x -is [System.Management.Automation.Language.CommandAst] -and $x.GetCommandName() -eq 'Say' -and $x.Extent.StartLineNumber -ne $x.Extent.EndLineNumber }, $true)).Count -gt 0)
} finally { Remove-Item -LiteralPath $tmpBad -Force -ErrorAction SilentlyContinue }
Write-Host ("KNOWN-BAD-SAMPLE out-of-loop-continue-detected=" + $kBCont + " multiline-Say-detected=" + $kBSay + " (both must be True)")
if (-not $kBCont) { $fail++ }
if (-not $kBSay) { $fail++ }

# ---------------------------------------------------------------------------
# KNOWN-BAD #2 -- the falsifiability guard for the CALL-FORM / bare-word fixes (rule 5.2-45, C7).
# One fixture carries BOTH the prose mentions and ONE real call, so a single row proves all four
# directions at once:
#   * '# END-MARKER ...' in a COMMENT            -> endm must stay 0  (comment satisfies nothing)
#   * '# $SelfCheckOnly ...' in a COMMENT        -> must stay 0       (same, for the unlocked entry)
#   * Write-Host "editor_stop" / a fixture string that EMBEDS the call text
#                                                -> must NOT be counted as a call (keywordOnly only)
#   * exactly one Unity-Cmd @('editor_stop')      -> MUST be counted  (the rule must not go blind)
# If someone reverts any of the three fixes, this row turns red (stopCalls would read 3 and both
# booleans would read True) -- which is the only thing that makes the green rows above meaningful.
# ---------------------------------------------------------------------------
$tmpBad2 = Join-Path $env:TEMP ('rungate-bareword-' + $PID + '.ps1')
Set-Content -LiteralPath $tmpBad2 -Encoding ASCII -Value @(
    '# END-MARKER appears in this comment only -> must not satisfy the terminal-marker check',
    '# $SelfCheckOnly is only mentioned here   -> must not satisfy the unlocked-entry check',
    'param($Whatever)',
    'Write-Host "editor_stop"',
    'Set-Content -Path "$env:TEMP\fx.ps1" -Value "Unity-Cmd @(''editor_stop'')" -Encoding ASCII',
    "Unity-Cmd @('editor_stop') -Quiet | Out-Null"
)
$kB2EndSatisfied = $true; $kB2UnlockSatisfied = $true; $kB2Calls = -1; $kB2KeywordOnly = -1
try {
    $tk3 = $null; $er3 = $null
    $ast3 = [System.Management.Automation.Language.Parser]::ParseFile($tmpBad2, [ref]$tk3, [ref]$er3)
    # The SAME three helpers the per-file loop uses -- never a re-implementation, otherwise this
    # sample would stay green while the loop regressed (a sample that cannot fail).
    $kB2EndSatisfied = Test-HasTerminalMarker $ast3
    $kB2UnlockSatisfied = Test-HasUnlockedEntry $ast3
    $sc2 = Get-StopCallInfo $ast3
    $kB2Calls = $sc2.calls.Count; $kB2KeywordOnly = $sc2.keywordOnly.Count
} finally { Remove-Item -LiteralPath $tmpBad2 -Force -ErrorAction SilentlyContinue }
Write-Host ("KNOWN-BAD2-SAMPLE barewordSatisfiesEnd=" + $kB2EndSatisfied + " barewordSatisfiesUnlocked=" + $kB2UnlockSatisfied + " stopCalls=" + $kB2Calls + " keywordOnly=" + $kB2KeywordOnly + " (end/unlock must be False; stopCalls must be 1; keywordOnly must be 2)")
if ($kB2EndSatisfied) { $fail++ }
if ($kB2UnlockSatisfied) { $fail++ }
if ($kB2Calls -ne 1) { $fail++ }
if ($kB2KeywordOnly -ne 2) { $fail++ }

# ---------------------------------------------------------------------------
# ZERO-COVERAGE GUARD (same principle as verify.ps1 14b's Test-C5Coverage; team-lead 2026-09-24):
# "CHECKED=0 FAILED=0 VERDICT=PASS" is a BLIND judge reporting green -- reachable in two real ways:
# `-Scope active` against a tree whose drivers/ holds none of the 8 rotation names, and a run where
# every candidate was SKIPped (file being edited).  Zero rows judged is not evidence.
# Reproduce the RED on purpose (empty tree = nothing to judge):
#   $e = Join-Path $env:TEMP 'rg-empty'; New-Item -ItemType Directory -Force (Join-Path $e 'tools\probes\drivers') | Out-Null
#   & <this script> -ProjectRoot $e -Scope active    => "ZERO-COVERAGE: ..." + CHECKED=0 FAILED=1 VERDICT=FAIL, exit 1
# ---------------------------------------------------------------------------
if ($checked -eq 0) {
    Write-Host 'ZERO-COVERAGE: nothing was judged (CHECKED=0) -> FAIL; a blind judge is never green'
    $fail++
}

Write-Host ("SCOPE=" + $Scope + " CHECKED=" + $checked + " SKIPPED=" + $skipped + " FAILED=" + $fail + " VERDICT=" + $(if ($fail -eq 0) { 'PASS' } else { 'FAIL' }))
# ---------------------------------------------------------------------------
# README §67 terminal verdict line (team-lead 2026-09-24).  Consumer contract, both required:
#   green  <=>  a `RESULT OK` line EXISTS  AND  the process exit code is 0.
# A MISSING RESULT line is a TOOL FAULT and must be read as RED -- never as a pass.  Measured
# upstream: a judge loaded mid-write threw a non-terminating error, printed no verdict line at all,
# and still left $LASTEXITCODE = 0, so "no FAIL in the output => green" was green while nothing had
# been judged.  Consumers must therefore invoke this file by PATH (`powershell -NoProfile -File ...`,
# which does return a real exit code), not with an in-session `& call`.
# ---------------------------------------------------------------------------
Write-Host ("RESULT " + $(if ($fail -eq 0) { 'OK' } else { 'FAIL' }))
if ($fail -eq 0) { exit 0 } else { exit 1 }
