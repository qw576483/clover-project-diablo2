# =============================================================================
# u44hover_run.ps1 -- ONE Play session for sheet u44 (hover selection feedback).
#
#   powershell -NoProfile -ExecutionPolicy Bypass -File u44hover_run.ps1
#
# LOCK PROTOCOL v2.2 (team-lead, 2026-09-24 -- the ONLY wording; no local variants)
#   * REAL lock   = <project>/.ai-tmp/test/play-running.lock  "<owner> <ISO8601> <PID>"
#   * MIRROR lock = <project>/.ai-tmp/test/play.lock          same content (transition only:
#                   it exists to stop the 17 historical runners that only read the old name)
#   * take: BOTH files are inspected first; if either is FRESH (age < 12min) => yield.
#           Only when all are stale/absent: write the real lock, then the mirror.
#           A lock with NO PID field is UNKNOWN, not dead => fresh => yield.
#           stale = age >= 12min (the ONE threshold) OR (PID present AND that process is dead).
#   * write -> sleep ~1.1s -> re-read; not mine => LOCK-RACE-LOST -> yield, delete nothing.
#   * lock CONTENT must never carry a BOM (written with -Encoding ASCII); when parsing, strip
#     U+FEFF / U+200B and normalise whitespace (else $parts[0] is BOM+owner and My-Lock()
#     returns false => zombie lock + the editor is never stopped).
#   * the play-log row is appended ONLY AFTER LOCK-TAKEN (never book first).
#   * NEVER call editor_stop / recompile / editor_play while the lock is not mine:
#     a stolen lock => EDITOR-STOP-SKIPPED, release nothing but my own local state.
#   * release: delete only the file(s) whose CONTENT is mine (real + mirror).
#   * self-check (it must be able to FAIL, and its result goes into the log):
#       - encoding: my own bytes -- non-ASCII body count + UTF-8 BOM presence
#       - static:   code lines calling the raw stop command == 1 and it sits inside
#                   Stop-Editor-Safe; the play / recompile call lines sit AFTER the lock
#                   is taken; the guard must not call itself (replace_all pollution).
#   * historical runners that only know the old lock name are registered as DO-NOT-RUN.
#   * sandbox note: this runner owns no temp sandbox (nothing shared to collide on).
# PS 5.1 GOTCHAS already hit in this team (do not reintroduce):
#   - `$pid` is a read-only automatic variable: never use it as a parameter name.
#   - the ternary operator does NOT exist in PS 5.1 (that is a parse error).
#   - a matcher must not match ITSELF (build its patterns from fragments).
# Reference implementations: tools/probes/drivers/d2u3_charstat_run.ps1 (v2.1) and
#                            u52play's lock-protocol-selftest (17/17).
#
# Fixed order: lock wait -> LOCK-TAKEN -> play-log row -> editor_stop -> recompile ->
#   clear_console -> editor_play -> U44Hover.Tour.Install -> wait for done -> editor_stop
#   (only if the lock is still mine) -> release both locks -> freeze the [U44] lines.
#
# Evidence: .ai-tmp/screenshots/u44_0*.png + .ai-tmp/screenshots/u44hover_evidence.txt
#           + .ai-tmp/test/u44hover_done.txt
# ASCII ONLY (PS 5.1 reads a BOM-less non-ASCII .ps1 as ANSI; the sampler gate flags it).
# =============================================================================
param([switch]$SelfCheckOnly)
# -SelfCheckOnly : read-only entry (team rule, from jitter's 13:26 incident): run the byte/encoding
#   self-check, the static shape checks AND the playMode gate, then print SELFCHECK-ONLY-END and exit
#   -- WITHOUT taking the lock and WITHOUT touching the editor beyond the read-only editor_status
#   probe.  Rationale: "start the real runner and then kill it" is a gamble (the runner may legally
#   take the lock within its first seconds; a kill skips `finally` => zombie lock + a bogus ledger
#   row).  Reading a self-check must not require a lock at all.
$ErrorActionPreference = 'Continue'

$root  = $PSScriptRoot
while ($root -and -not (Test-Path (Join-Path $root 'client'))) {
    $up = Split-Path $root -Parent
    if ($up -eq $root -or [string]::IsNullOrEmpty($up)) { break }
    $root = $up
}
$proj  = Join-Path $root 'client'
$test  = Join-Path $root '.ai-tmp\test'
$shots = Join-Path $root '.ai-tmp\screenshots'
$done  = Join-Path $test 'u44hover_done.txt'
$outLog = Join-Path $test 'u44hover_runlog.txt'
$drv   = Join-Path $root 'tools\probes\drivers\u44hover_drive.cs'
$playLog = Join-Path $test 'play-log.tsv'
$lock  = Join-Path $test 'play-running.lock'
$legacyLock = Join-Path $test 'play.lock'
$me    = 'u44impl'
$LockWaitSeconds = 480
$Why = 'sheet u44impl: user complaint "why is there no selection effect when I hover a monster". The offline host pins the wiring and values, but only a live session can tell whether the verbatim reference shader still RENDERS under this project URP pipeline, whether the real mouse -> HoverPicker -> Events.HoverTargetChanged chain drives ViewModule.EntityHighlight.Apply and EnemyBarView on screen, whether the MPB read-back has a sound calibration (step P0 writes 2.5 and reads it back first), whether un-hover restores the ORIGINAL material, and what the top bar / NPC nameplate look like. This run also walks to the wilderness first so the top bar can be seen on a real monster.'
# -SelfCheckOnly must NOT truncate the real run log nor delete the done marker (both are evidence).
if ($SelfCheckOnly) { $outLog = Join-Path $test 'u44hover_selfcheck.txt' }

# ⚠️ Write-HOST (host stream) -- NEVER Write-Output here (team bug #12, found by u52play):
# `if (Take-Lock)` takes the command's WHOLE output as its truth value.  With Write-Output a failed
# take returns @('<LOCK-RACE-LOST line>', $false) -- a NON-EMPTY ARRAY IS ALWAYS TRUE => the runner
# would write the ledger and call editor_play even though it LOST the race (fake exclusive access).
# Belt and braces: the call site is also written as `if ((Take-Lock) -eq $true)`.
# Log files are UTF-8 WITHOUT BOM appends (locks are ASCII; logs must keep Chinese, and a BOM in
# the middle of a shared tsv adds a phantom column for everyone).
function Say([string]$m) {
    $stamp = (Get-Date).ToString('HH:mm:ss.fff')
    Write-Host ($stamp + ' ' + $m)
    [System.IO.File]::AppendAllText($outLog, ($stamp + ' ' + $m) + "`r`n", (New-Object System.Text.UTF8Encoding($false)))
}
function UC([string[]]$argv) {
    $raw = & unity command @argv --project-path $proj --format json --no-pager 2>&1 | Out-String
    return $raw
}

# ---- lock protocol v2.2 ------------------------------------------------------
# NOTE: never name a parameter $pid / $PID -- it is a read-only automatic variable and the
# failure is a RUNTIME one (parsing stays clean).
function Get-LockShot([string]$path) {
    if (-not (Test-Path $path)) { return $null }
    $raw = ''
    try { $raw = (Get-Content $path -Raw) } catch { $raw = '' }
    $flat = ($raw -replace "`r?`n", ' ').Replace([string][char]0xFEFF, '').Replace([string][char]0x200B, '').Trim()
    $parts = @($flat -split '\s+')
    $lockPid = -1
    if ($parts.Count -ge 3) { [int]::TryParse($parts[2], [ref]$lockPid) | Out-Null }
    return @{ path = $path; owner = ($parts[0]); pid = $lockPid; raw = $flat }
}
function Lock-Alive([string]$path) {
    $s = Get-LockShot $path
    if ($null -eq $s) { return $false }
    if ($s.pid -gt 0 -and (Get-Process -Id $s.pid -ErrorAction SilentlyContinue)) { return $true }
    return $false
}
# mine = content says my name AND my PID (a stale copy of my own name is not "mine")
function Lock-IsMine([string]$path) {
    $s = Get-LockShot $path
    if ($null -eq $s) { return $false }
    return ($s.owner -eq $me -and $s.pid -eq $PID)
}
function My-Lock() { return (Lock-IsMine $lock) }
function Write-LockFile([string]$path) {
    Set-Content -Path $path -Value ($me + ' ' + (Get-Date).ToString('o') + ' ' + $PID) -Encoding ASCII
}
# ---- readiness gate: BOTH the lock AND playMode must say "nobody is playing" ------------
# `editor_stop` returns BEFORE the editor really leaves Play (measured: "lock ABSENT while
# playMode=playing"), so an empty lock alone is NOT proof.  `unity status --project-path` silently
# drops the flag and returns an empty table => never use it; `console` groundTruth has no playMode
# (blind source) => at most a fallback, and a fallback must NEVER override a negative verdict.
# FAIL-CLOSED: unreadable playMode = UNKNOWN != stopped => yield (do not rely on a default value
# that merely happens not to be 'playing').
function Get-PlayModeGate() {
    for ($try = 1; $try -le 3; $try++) {
        $st = UC @('editor_status')
        if ($st -match 'COMMAND_FAILED' -or $st -match '"success"\s*:\s*false') {
            # no editor process at all: that is NOT "unknown" (nothing can be playing).  Cold start
            # is out of scope (the user opens the editor with the Hub), so after 3 probes we go on.
            if ($try -lt 3) { Start-Sleep -Seconds 5; continue }
            Say ('LOCK-GATE-BRIDGE-DOWN probed=' + $try + ' (editor process absent; cold start is out of scope)')
            return 'bridge-down'
        }
        if ($st -match '"playMode"\s*:\s*"([A-Za-z]+)"') {
            $pm = $Matches[1]
            Say ('LOCK-GATE playMode=' + $pm + ' probed=' + $try)
            if ($pm -eq 'playing') { return 'playing' }
            return 'stopped'
        }
        if ($try -lt 3) { Start-Sleep -Seconds 5; continue }
    }
    Say 'LOCK-GATE playMode unreadable after 3 probes -> UNKNOWN (unknown != stopped, fail closed)'
    return 'unknown'
}

# Trigger + confirm (team-lead's REVISED ruling, §5.2 #42-1, 2026-09-24 -- it SUPERSEDES the earlier
# "release anyway + alarm" shape): if the editor will not leave Play, DO NOT release the lock names.
#   * an orphan Play with an EMPTY lock is an ownerless session that nobody is entitled to stop
#     (measured cost: 13:42:11 -> 13:48:52, ~6.5 min of blocked team, cleaned up by hand by team-lead);
#   * holding the lock keeps the ownership information and gives the taker a legitimate identity;
#   * it costs NO extra wall time: while playMode=playing every correct gate refuses to start;
#   * cap = the existing 12-minute stale rule (never indefinite); the TAKER's obligation is to
#     `editor_stop` and confirm `stopped` BEFORE starting their own session.
# The window is deliberately wider than 5x1s (measured: one editor_stop exits Play immediately => the
# real disease is "never called", not "called but slow"), and the trigger is RE-ISSUED inside the wait.
function Wait-EditorStopped([int]$tries = 15, [int]$intervalSec = 2) {
    for ($i = 1; $i -le $tries; $i++) {
        $st = UC @('editor_status')
        if ($st -match '"playMode"\s*:\s*"stopped"') { Say ('RELEASE-WAIT playMode=stopped after ' + $i + ' probe(s)'); return $true }
        if ($i % 3 -eq 0) {
            Stop-Editor-Safe 'orphan-retry'
            Say ('ORPHAN-RETRY editor_stop re-issued at probe ' + $i)
        }
        Start-Sleep -Seconds $intervalSec
    }
    # Fresh read-back (NOT the loop's last sample) + owner/pid, so the alarm carries who to ask.
    $st = UC @('editor_status')
    $pm = 'unreadable'
    if ($st -match '"playMode"\s*:\s*"([A-Za-z]+)"') { $pm = $Matches[1] }
    $so = Get-LockShot $lock
    $ow = '(unreadable)'
    if ($null -ne $so -and -not [string]::IsNullOrEmpty($so.owner)) { $ow = $so.owner + ' pid=' + $so.pid }
    $reissues = [math]::Floor($tries / 3)
    Say ('ORPHAN-PLAY playMode=' + $pm + ' lockOwner=' + $ow + ' -- editor_stop re-issued ' + $reissues + 'x over ' + ($tries * $intervalSec) + 's and playMode never reached "stopped"')
    Say ('LOCK-HELD-ON-PURPOSE / LOCK-RELEASE-WITHHELD by ' + $me + ' pid=' + $PID + ' -- the two lock names are deliberately NOT released: an empty lock next to a live Play has no owner, and only the 12-minute stale rule may hand it over. A taker MUST editor_stop and confirm playMode=stopped BEFORE starting their own session')
    $orphanLog = Join-Path $test 'orphan-play-u44impl.txt'
    $om = 'ORPHAN-PLAY (u44impl)' + "`r`n"
    $om = $om + 'when      : ' + (Get-Date).ToString('o') + "`r`n"
    $om = $om + 'playMode  : ' + $pm + "`r`n"
    $om = $om + 'lockOwner : ' + $ow + "`r`n"
    $om = $om + 'lockFile  : ' + $lock + ' -> ' + (Get-LockShot $lock).raw + "`r`n"
    $om = $om + 'mirror    : ' + $legacyLock + ' -> ' + (Get-LockShot $legacyLock).raw + "`r`n"
    $om = $om + 'heldBy    : ' + $me + ' pid=' + $PID + ' (LOCK-RELEASE-WITHHELD)' + "`r`n"
    $om = $om + 'takerMust : editor_stop + confirm playMode=stopped BEFORE your own session (then you may treat the lock as yours)' + "`r`n"
    $om = $om + 'cap       : 12-minute stale rule (never indefinite)' + "`r`n"
    [System.IO.File]::WriteAllText($orphanLog, $om, (New-Object System.Text.UTF8Encoding($false)))
    Say ('ORPHAN-PLAY-NOTIFIED team-lead via ' + $orphanLog + ' (a headless runner cannot send a chat message: this file + the two log lines ARE the notification channel)')
    return $false
}

function Take-Lock() {
    Write-LockFile $lock
    # v2.2/v2.3 MIRROR: "reading two names protects YOURSELF, writing the mirror protects OTHERS"
    # -- the 17 historical runners only read play.lock, so without the mirror they would see an
    # empty lock inside my window and could start a second Play session.  ASCII => no BOM in the
    # lock CONTENT (a BOM makes even MY OWN read-back miss my name: zombie lock).
    Write-LockFile $legacyLock
    Say ('LEGACY-MIRROR-WRITTEN ' + $legacyLock + ' = ' + (Get-LockShot $legacyLock).raw)
    Start-Sleep -Milliseconds 1100
    if (My-Lock) {
        Say ('LOCK-TAKEN ' + (Get-LockShot $lock).raw + ' | mirror=' + (Get-LockShot $legacyLock).raw)
        return $true
    }
    Say ('LOCK-RACE-LOST readback=' + (Get-LockShot $lock).raw + ' -> yield (v2.2 rule 2)')
    return $false
}
function Release-Lock() {
    foreach ($f in @($lock, $legacyLock)) {
        $leaf = Split-Path $f -Leaf
        if (-not (Test-Path $f)) { Say ('LOCK-RELEASE skip ' + $leaf + ' (absent)'); continue }
        if (Lock-IsMine $f) { Remove-Item $f -Force -ErrorAction SilentlyContinue; Say ('LOCK-RELEASED (mine) ' + $leaf) }
        else { Say ('LOCK-RELEASE REFUSED ' + $leaf + ' (owned by ' + (Get-LockShot $f).raw + ') -> left alone (v2.2 rule 6)') }
    }
}
# v2 rule 4: if the lock was taken over by somebody else mid-run, do NOT stop the editor.
function Stop-Editor-Safe([string]$why) {
    if (-not (My-Lock)) {
        Say ('EDITOR-STOP-SKIPPED (' + $why + ') lock is not mine: ' + (Get-LockShot $lock).raw + ' -> v2.2 rule 4')
        return
    }
    Say ('EDITOR-STOP ' + $why)
    UC @('editor_stop') | Out-Null
}

# sha16 helper (first 8 bytes of SHA256, hex) -- byte level, so the fingerprint can be bound to the very
# bytes a parser judged (see the parse block inside Invoke-SelfCheck).
function Get-Sha16([string]$path) {
    $bb = [System.IO.File]::ReadAllBytes($path)
    $hh = [System.Security.Cryptography.SHA256]::Create().ComputeHash($bb)
    $ss = ''
    for ($i = 0; $i -lt 8; $i++) { $ss = $ss + $hh[$i].ToString('x2') }
    return $ss
}

# ---- self-check: encoding + static lock discipline (both MUST be able to fail) ----
function Invoke-SelfCheck() {
    $myPath = $PSCommandPath
    $bytes = [System.IO.File]::ReadAllBytes($myPath)
    $nonAscii = 0
    foreach ($b in $bytes) { if ($b -gt 127) { $nonAscii++ } }
    $hasBom = $false
    if ($bytes.Length -ge 3) {
        if ($bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF) { $hasBom = $true }
    }
    $body = $nonAscii
    if ($hasBom) { $body = $nonAscii - 3 }
    $encOk = $false
    if ($body -eq 0) { $encOk = $true }
    if ($hasBom) { $encOk = $true }

    # patterns assembled from fragments so this function never counts ITSELF
    $q = [string][char]39
    $patStop = 'UC @(' + $q + 'editor_stop' + $q + ')'
    $patPlay = 'UC @(' + $q + 'editor_play' + $q + ')'
    $patRec = 'UC @(' + $q + 'recompile' + $q + ')'
    $patAnchor = 'PLAYLOG-APPENDED'
    $patFn = 'function Stop-Editor-Safe'
    $patCall = 'Stop-Editor-Safe '

    $lines = [System.IO.File]::ReadAllLines($myPath)
    $codeStop = @()
    $codePlay = @()
    $codeRec = @()
    $fnLine = 0
    $anchor = 0
    $selfRec = 0
    for ($i = 0; $i -lt $lines.Length; $i++) {
        $t = $lines[$i].TrimStart()
        if ($t.StartsWith('#')) { continue }
        # skip the pattern-definition lines themselves (they hold the very literals we look for,
        # so without this the matcher counts ITSELF: stopCalls=2 and a fake self-recursion hit)
        if ($t.StartsWith('$pat')) { continue }
        if ($t.Contains($patStop)) { $codeStop += ($i + 1) }
        if ($t.Contains($patPlay)) { $codePlay += ($i + 1) }
        if ($t.Contains($patRec)) { $codeRec += ($i + 1) }
        if ($t.Contains($patAnchor)) { $anchor = ($i + 1) }
        if ($t.Contains($patFn)) { $fnLine = ($i + 1) }
        if ($fnLine -gt 0 -and ($i + 1) -gt $fnLine -and ($i + 1) -lt ($fnLine + 8)) {
            if ($t.Contains($patCall)) { $selfRec = 1 }
        }
    }
    # ⛔ the old LINE-based "exactly one mention of editor_stop / editor_play / recompile" criteria were
    # REPLACED by AST call-form criteria (see the AST block below) -- rule 5.2 #45: a new alarm text that
    # merely QUOTES the command name made them a false red (4 sheets hit this; a false red's real cost is
    # that it pressures the author into deleting the assertion).  The invariant that matters is not
    # "mentioned once" but "every editor_stop call site lives inside the guarded helper, and the session
    # starts only after the ledger anchor".  ($codeStop/$codePlay/$codeRec/$anchor stay as informational
    # line data in the SELFCHECK-STATIC line below.)

    # ---- shape checks: they read the FILE (not a stub), so a stubbed Say cannot mask them -----
    # (u52play's lesson: a stubbed print function hid the `if (Take-Lock)` truthiness bug from 32
    #  green assertions -- the text below is scanned, so the stub is irrelevant.)
    $inSay = 0
    $sayHost = 0
    $sayOut = 0
    $gateLine = 0
    $whileLine = 0
    $afterLoopLine = 0
    $compared = 0
    for ($i = 0; $i -lt $lines.Length; $i++) {
        $s = $lines[$i].TrimStart()
        if ($s.StartsWith('#')) { continue }
        if ($s.StartsWith('$pat')) { continue }
        if ($s.StartsWith('function Say(')) { $inSay = $i + 1 }
        if ($inSay -gt 0 -and ($i + 1) -gt $inSay -and ($i + 1) -lt ($inSay + 12)) {
            if ($s.Contains('Write-Host')) { $sayHost = 1 }
            if ($s.Contains('Write-Output')) { $sayOut = 1 }
        }
        if ($s.Contains('while (-not $got')) { $whileLine = $i + 1 }
        if ($s.Contains('if (-not $got) {')) { $afterLoopLine = $i + 1 }
        if ($s.Contains('$gate = Get-PlayModeGate')) { $gateLine = $i + 1 }
        if ($s.Contains('(Take-Lock) -eq $true')) { $compared = $i + 1 }
    }
    $sayOk = $false
    if ($sayHost -eq 1 -and $sayOut -eq 0) { $sayOk = $true }
    $cmpOk = $false
    if ($compared -gt 0) { $cmpOk = $true }
    # ★ "is that statement inside a loop" MUST be answered with the AST, not with indentation or line
    #   ranges (team rule, from charstat's measured case where indentation gave the OPPOSITE answer:
    #   a closed `}` is not a loop keyword, so backtracking walks into a deeper loop).  Also counts
    #   `continue` statements and requires every one of them to sit inside a loop (the "continue
    #   outside a loop silently ends the script with exit 0" trap) + the terminal markers.
    $astErrCount = -1
    $gateAst = 0
    $contTotal = 0
    $contInLoop = 0
    $termOk = 0
    $fnCount = 0
    $aliasHits = 0
    $nodesScanned = 0
    $mtStable = 0
    $orphanCount = 0
    $orphanDegCount = -1
    $orphanEmitters = 0
    $finOrderOk = 0
    $finScanned = 0
    $stopSites = 0
    $stopInHelper = 0
    $playSites = 0
    $recSites = 0
    $relGuarded = 0
    $withheldEmitters = 0
    $triggerTotal = 0
    $triggerUnconditional = 0
    $shaBound = 0
    $shaPre = ''
    $shaPost = ''
    try {
        $astErrs = $null
        # §5.2 #32 -- a parsing criterion must first PROVE the file is static (u3bverify measured a
        # mid-edit read that produced 2 phantom syntax errors, gone 45 s later).  mtime before/after.
        $mtBefore = (Get-Item $myPath).LastWriteTimeUtc
        # ⚠️ ② (u3bverify's SELF-CORRECTION, which applies to my file too): taking the hash AFTER the
        # parse labels "a version the AST never judged" -- and the row stays self-consistent, so nothing
        # flags it.  So: hash BEFORE the parse, hash AFTER, and also require equality with the fingerprint
        # this run printed at startup ($myHex) => the printed version IS the judged version.
        $shaPre = Get-Sha16 $myPath
        $ast = [System.Management.Automation.Language.Parser]::ParseFile($myPath, [ref]$null, [ref]$astErrs)
        $mtAfter = (Get-Item $myPath).LastWriteTimeUtc
        $shaPost = Get-Sha16 $myPath
        if ($mtBefore -eq $mtAfter) { $mtStable = 1 }
        if ($shaPre -eq $shaPost -and $shaPre -eq $myHex) { $shaBound = 1 }
        $astErrCount = $astErrs.Count
        $loopNames = @('WhileStatementAst', 'ForStatementAst', 'ForEachStatementAst', 'DoWhileStatementAst', 'DoUntilStatementAst')
        $gateCalls = $ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.CommandAst] -and $n.GetCommandName() -eq 'Get-PlayModeGate' }, $true)
        foreach ($gc in $gateCalls) {
            $p = $gc.Parent
            while ($null -ne $p) {
                if ($loopNames -contains $p.GetType().Name) { $gateAst = 1; break }
                $p = $p.Parent
            }
        }
        $conts = $ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.ContinueStatementAst] }, $true)
        foreach ($cc in $conts) {
            $contTotal++
            $p = $cc.Parent
            while ($null -ne $p) {
                if ($loopNames -contains $p.GetType().Name) { $contInLoop++; break }
                $p = $p.Parent
            }
        }
        $astText = [System.IO.File]::ReadAllText($myPath)
        if ($astText.Contains('LOCK-TAKEN') -and $astText.Contains("Say 'END'") -and $astText.Contains('RUNNER-FINGERPRINT') -and $astText.Contains('SELFCHECK-ONLY-END')) { $termOk = 1 }
        # §5.2 #40-① (my own reported trap, now a CRITERION): single-letter helper names collide with the
        # GLOBAL PowerShell alias table -- my snapshot harness named a function `H`, so every Get-FileHash
        # comparison silently never ran and the errors came from Get-History instead of from the logic.
        # Scan FUNCTION NAMES and require zero alias collisions (verb-noun only).
        $fnDefs = $ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.FunctionDefinitionAst] }, $true)
        foreach ($fd in $fnDefs) {
            $fnCount++
            if (Get-Alias -Name $fd.Name -ErrorAction SilentlyContinue) { $aliasHits++ }
        }
        # §5.2 #40-② : a scan must report how much it scanned.  nodesScanned=0 means the predicate matched
        # nothing -- that is a "vacuous green" waiting to happen, so 0 is a FAIL, never a pass.
        $nodesScanned = $ast.FindAll({ param($n) $true }, $true).Count
        # §5.2 #42-3 : the "I am about to leave the editor possibly still in Play" alarm must exist on BOTH
        # exit paths (bounded release-wait timeout, and the lock-wait timeout).  Counted on the real text,
        # with a DEGRADED sample (the tag scrubbed out) that must read 0 -- so the count provably reads it.
        $orphanCount = ([regex]::Matches($astText, 'ORPHAN-PLAY')).Count
        $orphanDegCount = ([regex]::Matches($astText.Replace('ORPHAN-PLAY', 'ORPHAN-XXXXXX'), 'ORPHAN-PLAY')).Count
        # ⚠️ the plain-text count above is NOT the criterion: 5 of its ~7 hits are THIS harness's own
        # lines (the criterion, its log, its FAIL text) -- that is exactly the "a scanner counting its own
        # fixture" trap (rule 5.2 #31): delete BOTH real alarms and it would still read >= 2 => vacuous
        # green.  So count the ALARM EMITTERS: `Say` commands whose payload starts with the alarm format.
        $sayCmds = $ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.CommandAst] -and $n.GetCommandName() -eq 'Say' }, $true)
        foreach ($sc in $sayCmds) { if ($sc.Extent.Text.Contains('ORPHAN-PLAY playMode=')) { $orphanEmitters++ } }
        # §5.2 #42-4 (team-lead, from the d2u27 root cause: an assertion proved editor_stop EXISTS in the
        # file, yet the normal cleanup path never reached it => "green assertion, missing behaviour").
        # So prove REACHABILITY + ORDER on the cleanup path itself, via AST: inside a `finally` block, the
        # TRIGGER (Stop-Editor-Safe) must come first, then the CONFIRM (Wait-EditorStopped), then the
        # HANDOVER (Release-Lock).  A wait with no trigger only delays a certain failure.
        $tryStmts = $ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.TryStatementAst] }, $true)
        foreach ($ts in $tryStmts) {
            if ($null -eq $ts.Finally) { continue }
            $finScanned++
            $posStop = -1; $posWait = -1; $posRel = -1
            $finCmds = $ts.Finally.FindAll({ param($n) $n -is [System.Management.Automation.Language.CommandAst] }, $true)
            foreach ($fc in $finCmds) {
                $nm = $fc.GetCommandName()
                if ($nm -eq 'Stop-Editor-Safe' -and $posStop -lt 0) { $posStop = $fc.Extent.StartOffset }
                if ($nm -eq 'Wait-EditorStopped' -and $posWait -lt 0) { $posWait = $fc.Extent.StartOffset }
                if ($nm -eq 'Release-Lock' -and $posRel -lt 0) { $posRel = $fc.Extent.StartOffset }
            }
            if ($posStop -ge 0 -and $posWait -ge 0 -and $posRel -ge 0 -and $posStop -lt $posWait -and $posWait -lt $posRel) { $finOrderOk = 1 }
        }
        # §5.2 #45 -- count CALL FORMS / AST nodes, never bare words.  The invariant: EVERY editor_stop
        # call site lives inside the guarded helper (true however often the helper re-issues it, and
        # however many alarm texts quote the name in prose).
        $helperLo = -1; $helperHi = -1
        $helperDefs = $ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq 'Stop-Editor-Safe' }, $true)
        foreach ($hd in $helperDefs) { $helperLo = $hd.Extent.StartOffset; $helperHi = $hd.Extent.EndOffset }
        $ucCmds = $ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.CommandAst] -and $n.GetCommandName() -eq 'UC' }, $true)
        $playSiteOff = -1
        $recSiteOff = -1
        foreach ($ucc in $ucCmds) {
            $ct = $ucc.Extent.Text
            if ($ct.StartsWith("UC @('editor_stop')")) {
                $stopSites++
                if ($helperLo -ge 0 -and $ucc.Extent.StartOffset -gt $helperLo -and $ucc.Extent.EndOffset -lt $helperHi) { $stopInHelper++ }
            }
            if ($ct.StartsWith("UC @('editor_play')")) { $playSites++; $playSiteOff = $ucc.Extent.StartOffset }
            if ($ct.StartsWith("UC @('recompile')")) { $recSites++; $recSiteOff = $ucc.Extent.StartOffset }
        }
        # offset based (not line based): the shared-ledger append must precede the session start
        # ⚠️ SINGLE quotes: with double quotes PowerShell expands $playLog into the real path, the literal
        # is then never found, anchorIdx stays -1 and the play/rec criteria fail closed (self-inflicted
        # false red #2 of this sheet -- caught by its own criterion, which is the point of fail-closed).
        $anchorIdx = $astText.IndexOf('AppendAllText($playLog')
        # §5.2 #42-1 (revised): the release must be GUARDED by the stop-confirmation -- otherwise a stuck
        # Play would hand over an ownerless session.  AST: Release-Lock's Parent chain reaches an IfStatement.
        $relCmds = $ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.CommandAst] -and $n.GetCommandName() -eq 'Release-Lock' }, $true)
        foreach ($rc in $relCmds) {
            $p = $rc.Parent
            while ($null -ne $p) {
                if ($p -is [System.Management.Automation.Language.IfStatementAst]) { $relGuarded = 1; break }
                if ($p -is [System.Management.Automation.Language.FunctionDefinitionAst]) { break }
                $p = $p.Parent
            }
        }
        # Withhold emitters (§42-1): must be a `Say` carrying the marker AND sitting INSIDE the orphan
        # path itself (Wait-EditorStopped).  A looser "the file mentions the marker" version was measured
        # to be too weak: after scrubbing the real alarm the `finally`'s "(see LOCK-RELEASE-WITHHELD)"
        # prose still satisfied it => the injection stayed GREEN (a criterion that cannot fail).
        $waitLo = -1; $waitHi = -1
        $waitDefs = $ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq 'Wait-EditorStopped' }, $true)
        foreach ($wd in $waitDefs) { $waitLo = $wd.Extent.StartOffset; $waitHi = $wd.Extent.EndOffset }
        foreach ($sc2 in $sayCmds) {
            if ($sc2.Extent.Text.Contains('LOCK-RELEASE-WITHHELD') -and $waitLo -ge 0 -and $sc2.Extent.StartOffset -gt $waitLo -and $sc2.Extent.EndOffset -lt $waitHi) { $withheldEmitters++ }
        }
        # §42-1 ① TRIGGER REACHABILITY (team-lead; shape copied from u52resist): at least ONE call to the
        # stop-guard must sit on an UNCONDITIONAL path -- no IfStatementAst on its Parent chain.  Existence
        # != reachability: d2u27 had the call exactly once, inside the guard function, and STILL never
        # reached it on the normal cleanup path => locks released while the editor played for 6.5 minutes.
        $guardCalls = $ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.CommandAst] -and $n.GetCommandName() -eq 'Stop-Editor-Safe' }, $true)
        foreach ($gcc in $guardCalls) {
            $triggerTotal++
            $cond = 0
            $gp = $gcc.Parent
            while ($null -ne $gp) {
                if ($gp -is [System.Management.Automation.Language.IfStatementAst]) { $cond = 1; break }
                if ($gp -is [System.Management.Automation.Language.FunctionDefinitionAst]) { break }
                $gp = $gp.Parent
            }
            if ($cond -eq 0) { $triggerUnconditional++ }
        }
    }
    catch { $astErrCount = -1 }
    $gateOk = $false
    if ($gateAst -eq 1 -and $astErrCount -eq 0 -and $mtStable -eq 1) { $gateOk = $true }
    $contOk = $false
    if ($contInLoop -eq $contTotal -and $contTotal -gt 0) { $contOk = $true }
    $aliasOk = $false
    if ($aliasHits -eq 0 -and $fnCount -gt 0) { $aliasOk = $true }
    $scanOk = $false
    if ($nodesScanned -gt 0) { $scanOk = $true }
    $orphanOk = $false
    if ($orphanEmitters -ge 2 -and $orphanDegCount -eq 0) { $orphanOk = $true }
    $finOk = $false
    if ($finOrderOk -eq 1 -and $finScanned -gt 0) { $finOk = $true }
    # §5.2 #45 : call-form criteria (they cannot be moved by prose that quotes a command name)
    $stopOk = $false
    if ($stopSites -ge 1 -and $stopInHelper -eq $stopSites) { $stopOk = $true }
    $playOk = $false
    if ($playSites -eq 1 -and $anchorIdx -gt 0 -and $playSiteOff -gt $anchorIdx) { $playOk = $true }
    $recOk = $false
    if ($recSites -eq 1 -and $anchorIdx -gt 0 -and $recSiteOff -gt $anchorIdx) { $recOk = $true }
    # §5.2 #42-1 (revised): the handover must be GUARDED by the stop-confirmation, and the marker must exist
    $relOk = $false
    if ($relGuarded -eq 1 -and $finOrderOk -eq 1) { $relOk = $true }
    $withheldOk = $false
    if ($withheldEmitters -ge 1) { $withheldOk = $true }
    # §42-1 ① : a parse failure counts as FAIL (fail-closed -- an unparsable file can prove nothing)
    $trigOk = $false
    if ($astErrCount -eq 0 -and $triggerTotal -ge 1 -and $triggerUnconditional -ge 1) { $trigOk = $true }
    # ② the printed fingerprint must label the bytes the AST judged (and the file must be static across it)
    $shaSegOk = $false
    if ($shaBound -eq 1 -and $astErrCount -eq 0) { $shaSegOk = $true }
    $sayN = 0; if ($sayOk) { $sayN = 1 }
    $cmpN = 0; if ($cmpOk) { $cmpN = 1 }
    $gateN = 0; if ($gateOk) { $gateN = 1 }
    $contN = 0; if ($contOk) { $contN = 1 }
    $termN = 0; if ($termOk -eq 1) { $termN = 1 }
    $aliasN = 0; if ($aliasOk) { $aliasN = 1 }
    $scanN = 0; if ($scanOk) { $scanN = 1 }
    $orphanN = 0; if ($orphanOk) { $orphanN = 1 }
    $finN = 0; if ($finOk) { $finN = 1 }
    $relN = 0; if ($relOk) { $relN = 1 }
    $withheldN = 0; if ($withheldOk) { $withheldN = 1 }
    $trigN = 0; if ($trigOk) { $trigN = 1 }
    $shaN = 0; if ($shaSegOk) { $shaN = 1 }
    $shapeA = 'SELFCHECK-SHAPE say_uses_writehost=' + $sayHost + ' say_uses_writeoutput=' + $sayOut + ' (want 1/0) ok=' + $sayN
    $shapeB = ' | take_lock_compared_line=' + $compared + ' ok=' + $cmpN
    $shapeC = ' | AST parseErr=' + $astErrCount + ' mtime_stable=' + $mtStable + ' gate_call_inside_loop=' + $gateAst + ' ok=' + $gateN
    $shapeD = ' | continue total=' + $contTotal + ' inLoop=' + $contInLoop + ' ok=' + $contN + ' (total=0 => FAIL, else vacuous)'
    $shapeE = ' | terminal_markers(LOCK-TAKEN/END/RUNNER-FINGERPRINT/SELFCHECK-ONLY-END) ok=' + $termN
    $shapeF = ' | scannedNodes=' + $nodesScanned + ' ok=' + $scanN + ' (rule 5.2#40-2: a scan must report n>0)'
    $shapeG = ' | functions=' + $fnCount + ' aliasCollisions=' + $aliasHits + ' ok=' + $aliasN + ' (rule 5.2#40-1: verb-noun only)'
    $shapeH = ' | ORPHAN-PLAY alarmEmitters=' + $orphanEmitters + '/2 ok=' + $orphanN + ' degradedSample=' + $orphanDegCount + ' (textHits=' + $orphanCount + ' incl. this harness, not the criterion) (rule 5.2#42-3: never leave a live Play silently)'
    $shapeI = ' | finallyOrder trigger(Stop-Editor-Safe)->confirm(Wait-EditorStopped)->handover(Release-Lock) ok=' + $finN + ' (rule 5.2#42-4; finally blocks scanned=' + $finScanned + ', 0 => FAIL)'
    $shapeJ = ' | releaseGuardedByConfirm ok=' + $relN + ' / withheldMarkerEmittersInsideWait-EditorStopped=' + $withheldEmitters + ' ok=' + $withheldN + ' (rule 5.2#42-1 revised: never hand over an ownerless live Play; the marker must sit on the orphan path, not in prose elsewhere)'
    $shapeK = ' | callForm stopSites=' + $stopSites + ' inHelper=' + $stopInHelper + ' playSites=' + $playSites + ' recSites=' + $recSites + ' anchorOffset=' + $anchorIdx + ' (rule 5.2#45: count call forms, NOT bare words -- this line replacing the old line-based only-once counters is what makes that fix visible)'
    $verdict = 'FAIL'; if ($trigN -eq 1) { $verdict = 'PASS' }
    $shapeL = ' | SELF-CHK stop-trigger-reachable totalGuardCalls=' + $triggerTotal + ' unconditional=' + $triggerUnconditional + ' verdict=' + $verdict + ' (rule 5.2#42-1: existence != reachability; shape from u52resist)'
    $shapeM = ' | fingerprintLabelsParsedBytes sha16Pre=' + $shaPre + ' sha16Post=' + $shaPost + ' printed=' + $myHex + ' ok=' + $shaN + ' (rule: hash BEFORE and AFTER the parse, and both must equal the startup fingerprint; hashing after the parse alone prints a version the AST never judged)'
    Say ($shapeA + $shapeB + $shapeC + $shapeD + $shapeE + $shapeF + $shapeG + $shapeH + $shapeI + $shapeJ + $shapeK + $shapeL + $shapeM)
    if (-not $sayOk) { Say 'SELFCHECK-FAIL the print function must use Write-Host and never Write-Output (else if (fn) is always true)'; exit 3 }
    if (-not $cmpOk) { Say 'SELFCHECK-FAIL Take-Lock must be compared explicitly: if ((Take-Lock) -eq $true)'; exit 3 }
    if (-not $gateOk) { Say 'SELFCHECK-FAIL the playMode gate must sit INSIDE the lock retry loop (AST: Parent chain), the file must parse with 0 errors, and mtime must be stable before/after the parse'; exit 3 }
    if (-not $contOk) { Say 'SELFCHECK-FAIL continue statements: either one landed OUTSIDE a loop (silently ends the script with exit 0) or there were ZERO of them (vacuous green)'; exit 3 }
    if ($termOk -ne 1) { Say 'SELFCHECK-FAIL missing terminal marker (LOCK-TAKEN / END / RUNNER-FINGERPRINT / SELFCHECK-ONLY-END)'; exit 3 }
    if (-not $aliasOk) { Say 'SELFCHECK-FAIL function names: an alias collision is present (or none was scanned). Use verb-noun names only -- the alias table is global (rule 5.2 #40-1)'; exit 3 }
    if (-not $scanOk) { Say 'SELFCHECK-FAIL scannedNodes=0: the AST scan matched nothing, so any green from it would be vacuous (rule 5.2 #40-2)'; exit 3 }
    if (-not $orphanOk) { Say 'SELFCHECK-FAIL ORPHAN-PLAY alarm: fewer than 2 alarms, or the degraded sample still matched (rule 5.2 #42-3: a live Play must never be left silently)'; exit 3 }
    if (-not $finOk) { Say 'SELFCHECK-FAIL cleanup path: no finally block contains the required order trigger->confirm->handover (rule 5.2 #42-4: a wait without a trigger merely delays a certain failure -- d2u27 had the call in the file but NOT on the path)'; exit 3 }
    if (-not $relOk) { Say 'SELFCHECK-FAIL the handover (Release-Lock) is not guarded by the stop-confirmation inside the cleanup finally (rule 5.2 #42-1 revised: releasing a live Play leaves an ownerless session)'; exit 3 }
    if (-not $withheldOk) { Say 'SELFCHECK-FAIL no LOCK-RELEASE-WITHHELD marker: the orphan-Play path must SAY that it keeps the lock'; exit 3 }
    if (-not $trigOk) { Say 'SELFCHECK-FAIL the stop-guard (Stop-Editor-Safe) has no UNCONDITIONAL call site, or the file does not parse: existence != reachability (rule 5.2 #42-1; d2u27 released the locks while the editor stayed in Play for 6.5 min)'; exit 3 }
    if (-not $shaSegOk) { Say 'SELFCHECK-FAIL the printed fingerprint does not label the bytes the AST judged (sha16Pre must equal sha16Post must equal the startup fingerprint; a hash taken only AFTER the parse prints a version the AST never saw -- u3bverify self-corrected exactly this)'; exit 3 }

    $encN = 0; if ($encOk) { $encN = 1 }
    $stopN = 0; if ($stopOk) { $stopN = 1 }
    $playN = 0; if ($playOk) { $playN = 1 }
    $recN = 0; if ($recOk) { $recN = 1 }
    $bomN = 0; if ($hasBom) { $bomN = 1 }
    Say ('SELFCHECK-ENCODING nonAsciiBytes=' + $nonAscii + ' body=' + $body + ' bom=' + $bomN + ' ok=' + $encN + ' (rule: pure ASCII OR utf8-with-BOM)')
    # ⚠️ the *Calls counters below are LINE mentions = INFORMATIONAL ONLY (they move when an alarm text
    # quotes a command name -- rule 5.2 #45).  The criteria are the AST call-form numbers in shapeK.
    $lineA = 'SELFCHECK-STATIC (line mentions = informational, NOT the criterion, rule 5.2#45) lineStopMentions=' + $codeStop.Count + ' at=' + ($codeStop -join ',') + ' fnLine=' + $fnLine
    $lineB = ' linePlayMentions=' + $codePlay.Count + ' at=' + ($codePlay -join ',') + ' lineRecMentions=' + $codeRec.Count + ' at=' + ($codeRec -join ',')
    $lineC = ' lockAnchorLine=' + $anchor + ' stop_all_inside_helper=' + $stopN + ' play_after_ledger=' + $playN + ' recompile_after_ledger=' + $recN + ' guard_self_recursion=' + $selfRec
    Say ($lineA + $lineB + $lineC)
    if (-not $encOk) { Say 'SELFCHECK-FAIL encoding (fix: pure ASCII or a UTF-8 BOM)'; exit 3 }
    if (-not $stopOk) { Say 'SELFCHECK-FAIL every editor_stop CALL SITE must live inside Stop-Editor-Safe (AST call-form count, rule 5.2#45), and at least one must exist'; exit 3 }
    if (-not $playOk) { Say 'SELFCHECK-FAIL editor_play must be issued exactly once and only AFTER the shared-ledger append'; exit 3 }
    if (-not $recOk) { Say 'SELFCHECK-FAIL recompile must be issued exactly once and only AFTER the shared-ledger append'; exit 3 }
    if ($selfRec -eq 1) { Say 'SELFCHECK-FAIL Stop-Editor-Safe calls itself (replace_all pollution)'; exit 3 }
}

foreach ($d in @($shots, $test)) { if (-not (Test-Path $d)) { New-Item -ItemType Directory -Path $d | Out-Null } }
[System.IO.File]::WriteAllText($outLog, '# u44hover run log pid=' + $PID + ' ' + (Get-Date).ToString('s') + "`r`n",
    (New-Object System.Text.UTF8Encoding($false)))
# RUNNER-FINGERPRINT (team rule): a trace must carry the fingerprint of the runner that produced it,
# because a quoted fingerprint is a mid-workspace snapshot -- after the next edit every quoted line
# number is stale (verified: 8/8 line numbers mismatched 2.5 min later).  Quote THIS line, not a
# number pasted by hand.
$myBytes = [System.IO.File]::ReadAllBytes($PSCommandPath)
$mySha = [System.Security.Cryptography.SHA256]::Create().ComputeHash($myBytes)
$myHex = ''
for ($i = 0; $i -lt 8; $i++) { $myHex = $myHex + $mySha[$i].ToString('x2') }
$fpA = 'RUNNER-FINGERPRINT sha256_16=' + $myHex + ' bytes=' + $myBytes.Length
$fpB = ' lines=' + ([System.IO.File]::ReadAllLines($PSCommandPath)).Length
$fpC = ' mtime=' + (Get-Item $PSCommandPath).LastWriteTime.ToString('o')
Say ($fpA + $fpB + $fpC + ' (authoritative for THIS trace; verified by re-computing)')
Invoke-SelfCheck
if ($SelfCheckOnly) {
    # read-only proof that this entry takes nothing: lock state before/after must be IDENTICAL.
    $lkA = Test-Path $lock
    $lkB = Test-Path $legacyLock
    $pm = Get-PlayModeGate
    $afterA = Test-Path $lock
    $afterB = Test-Path $legacyLock
    $locks = ' LOCKS before=[' + $lkA + ',' + $lkB + '] after=[' + $afterA + ',' + $afterB + ']'
    Say ('SELFCHECK-ONLY gate=' + $pm + $locks + ' (read-only: no lock taken, no ledger row, editor untouched)')
    Say 'SELFCHECK-ONLY-END'
    exit 0
}
if (Test-Path $done) { Remove-Item $done -Force }

# ---- "am I alone in here?" evidence (v2.2 rule 6) ---------------------------
$st = UC @('editor_status')
$stFlat = ($st -replace "`r?`n", ' ')
$stNote = ' [note: compiling=true is transient-normal, NOT a gate (measured: shopart 13:22 ran a normal session with compiling=True)]'
Say ('EDITOR-STATUS-BEFORE-LOCK ' + $stFlat.Substring(0, [Math]::Min(220, $stFlat.Length)) + $stNote)

$t0 = Get-Date
$got = $false
$tookOver = ''
while (-not $got -and ((Get-Date) - $t0).TotalSeconds -lt $LockWaitSeconds) {
    $blocked = $null
    foreach ($f in @($lock, $legacyLock)) {
        if (-not (Test-Path $f)) { continue }
        if (Lock-IsMine $f) { continue }
        $s = Get-LockShot $f
        $age = ((Get-Date) - (Get-Item $f).LastWriteTime).TotalMinutes
        $pidKnown = ($s.pid -gt 0)
        $alive = Lock-Alive $f
        $stale = ($age -ge 12) -or ($pidKnown -and -not $alive)
        $pidFlag = 0; if ($pidKnown) { $pidFlag = 1 }
        $aliveFlag = 0; if ($alive) { $aliveFlag = 1 }
        $who = (Split-Path $f -Leaf) + ' owner=' + $s.owner + ' pid=' + $s.pid + ' age=' + [math]::Round($age, 1) + 'm pidKnown=' + $pidFlag + ' alive=' + $aliveFlag
        if (-not $stale) {
            $blocked = $f
            $whyYield = 'fresh'
            if (-not $pidKnown) { $whyYield = 'NO-PID-FIELD but fresh -> v2.1 unknown != dead, must yield' }
            Say ('LOCK-BUSY ' + $who + ' -> yield (' + $whyYield + '), sleep 30')
            break
        }
        $why = 'reason=age ' + [math]::Round($age, 1) + 'm >= 12m'
        if (-not $pidKnown) { $why = $why + ' (lock has NO-PID-FIELD; v2.1: unknown != dead)' }
        elseif (-not $alive) { $why = 'reason=pid ' + $s.pid + ' is DEAD (age ' + [math]::Round($age, 1) + 'm)' }
        Say ('LOCK-STALE ' + $who + ' -> takeover allowed, ' + $why)
        $tookOver = $s.raw + ' [' + $why + ']'
        if ((Split-Path $f -Leaf) -eq 'play.lock') {
            Say ('LEGACY-LOCK-STALE-DELETED play.lock owner=' + $s.owner + ' age=' + [math]::Round($age, 1) + 'm (registered)')
            Remove-Item $f -Force -ErrorAction SilentlyContinue
        }
    }
    if ($null -eq $blocked) {
        # ★ the readiness gate lives INSIDE this retry loop on purpose: a `continue` outside a loop
        #   silently ends the script with exit 0 and prints nothing (team trap #25, classcols).
        $gate = Get-PlayModeGate
        if ($gate -eq 'playing') {
            Say 'ABORT editor-is-playing (lock is empty but the editor is still in Play) -> editor untouched (v2.4)'
            Say 'END'
            exit 4
        }
        if ($gate -eq 'unknown') {
            Say 'LOCK-GATE-UNKNOWN playMode unreadable -> yield (unknown != stopped, fail closed), sleep 30'
            Start-Sleep -Seconds 30
            continue
        }
        if ((Take-Lock) -eq $true) {
            if ($tookOver.Length -gt 0) { Say ('LOCK-TOOK-OVER took over a stale lock: ' + $tookOver) }
            $got = $true
            break
        }
    }
    Start-Sleep -Seconds 30
}
if (-not $got) {
    $so = Get-LockShot $lock
    $oname = '?'
    if ($null -ne $so) { $oname = $so.owner }
    Say ('ABORT lock held by ' + $oname + ' - alive/fresh after the lock wait -> editor untouched (v2.2 rule 3, fail fast)')
    # §5.2 #42-3: leaving WITHOUT stopping the editor is allowed only if it is not playing -- or it must
    # be an ALARM.  Read-only probe (this path never held a lock), and we never touch the editor here.
    $pmOrphan = Get-PlayModeGate
    if ($pmOrphan -eq 'playing') {
        $so2 = Get-LockShot $lock
        $ow2 = 'none'
        if ($null -ne $so2) { $ow2 = $so2.owner + ' pid=' + $so2.pid }
        Say ('ORPHAN-PLAY playMode=playing lockOwner=' + $ow2 + ' -- I never took a lock and never touched the editor, but the editor IS in Play; if that lock is stale/absent the NEXT taker must editor_stop first (do not assume a clean session)')
    }
    Say 'END'
    exit 2
}

# The SHARED ledger must never gain a BOM in the middle (a BOM adds a phantom column to the TSV
# parser for everyone) and must never be written ASCII either (the CLI output contains Chinese =>
# it would be silently turned into '?').  team-lead's rule (2026-09-24): UTF-8 WITHOUT BOM append.
$playLine = (Get-Date).ToString('yyyy-MM-dd HH:mm') + "`t" + $me + "`tu44-hover-select`t" + $Why + "`r`n"
[System.IO.File]::AppendAllText($playLog, $playLine, (New-Object System.Text.UTF8Encoding($false)))
Say ('PLAYLOG-APPENDED ' + $playLog)

$logPath = Join-Path $proj 'Logs\Editor.log'
$off = 0
if (Test-Path $logPath) { $off = (Get-Item $logPath).Length }

$rc = 0
try {
    Say 'STOP'
    Stop-Editor-Safe 'before-play'
    Start-Sleep -Seconds 2

    UC @('recompile') | Out-Null
    for ($i = 0; $i -lt 60; $i++) {
        Start-Sleep -Seconds 2
        $rst = UC @('recompile_status')
        if ($rst -match 'up_to_date|completed|idle') { Say ('RECOMPILE ' + ($rst -replace "`r?`n", ' ')); break }
    }
    Start-Sleep -Seconds 2

    UC @('clear_console') | Out-Null
    Say 'PLAY'
    $play = UC @('editor_play')
    Say ('PLAY-RESULT ' + ($play -replace "`r?`n", ' '))
    Start-Sleep -Seconds 6

    $inst = ''
    for ($try = 1; $try -le 8; $try++) {
        $inst = UC @('run_script', '--file', $drv, '--entry', 'U44Hover.Tour.Install')
        Say ('INSTALL try=' + $try + ' ' + ($inst -replace "`r?`n", ' '))
        if ($inst -match 'INSTALLED' -and $inst -notmatch 'Runtime Error|Compilation Failed|error CS') { break }
        Start-Sleep -Seconds 5
    }

    if ($inst -notmatch 'INSTALLED' -or $inst -match 'error CS') {
        Say 'INSTALL-FAILED -> capturing the console'
        $cons = UC @('console', '--tail', '80')
        Say ('CONSOLE ' + ($cons -replace "`r?`n", ' '))
        $rc = 1
    }
    else {
        $t1 = Get-Date
        while (((Get-Date) - $t1).TotalSeconds -lt 300) {
            if (Test-Path $done) { break }
            Start-Sleep -Milliseconds 500
        }
        if (Test-Path $done) { Say ('DONE ' + (Get-Content $done -Raw).Trim()) } else { Say 'DONE-MISSING'; $rc = 1 }
    }
}
finally {
    Start-Sleep -Seconds 2
    Stop-Editor-Safe 'after-run'
    # v2.5 (team-lead's REVISED #42-1): trigger -> confirm -> handover, and hand over ONLY if the confirm
    # passed.  If the editor is stuck in Play we KEEP the lock (see Wait-EditorStopped): releasing it while
    # playMode=playing creates an ownerless session -- correct gates refuse to start, and nobody is
    # entitled to stop it, so the whole team waits (measured 6.5 min on 2026-09-24).
    $stoppedOk = Wait-EditorStopped 15 2
    if ($stoppedOk) {
        Release-Lock
        Say 'STOPPED'
    }
    else {
        Say 'LOCK-KEPT playMode never reached "stopped" -> the two lock names are deliberately NOT released (see LOCK-RELEASE-WITHHELD); a taker must editor_stop and confirm stopped first'
    }

    $fs = New-Object System.IO.FileStream($logPath, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::ReadWrite)
    $fs.Seek($off, [System.IO.SeekOrigin]::Begin) | Out-Null
    $len = [int]($fs.Length - $off)
    $buf = New-Object byte[] $len
    $read = $fs.Read($buf, 0, $len)
    $fs.Close(); $fs.Dispose()
    $txt = [System.Text.Encoding]::UTF8.GetString($buf, 0, $read)
    $keep = @($txt -split "`r?`n" | Where-Object { $_ -match '\[U44\]' })
    $ev = Join-Path $shots 'u44hover_evidence.txt'
    [System.IO.File]::WriteAllLines($ev, [string[]]$keep, (New-Object System.Text.UTF8Encoding($false)))
    Say ('LOGWRITE ' + $ev + ' lines=' + $keep.Count)
    foreach ($l in $keep) { Say ('EV ' + $l) }
    Say 'END'
}
exit $rc
