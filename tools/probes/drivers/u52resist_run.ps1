# =============================================================================
# u52resist_run.ps1 -- ONE Play session for the charstat resist-row read-out (sheet u52resist).
#
#   powershell -NoProfile -ExecutionPolicy Bypass -File u52resist_run.ps1 -Tag u52resist
#   powershell -NoProfile -ExecutionPolicy Bypass -File u52resist_run.ps1 -SelfTest   # NO lock, NO editor
#
# Shape copied from the proven tools/probes/drivers/d2u3_charstat_run.ps1:
#   play lock -> editor_stop -> settle any pending recompile -> clear_console ->
#   editor_play -> eval_file <p_runbg.cs> (the game must keep ticking while the editor is
#   unfocused, otherwise the very first driver station never runs) -> run_script the driver
#   -> wait for the DONE marker -> editor_stop -> release lock -> append ONE play-log line
#   (column 4 non-empty) -> freeze the [D2U3C] lines out of `unity command console --tail`.
#
# LOCK PROTOCOL v2.3 (team-lead, 2026-09-24) -- implemented here, all four defects closed:
#   1. writes are `-Encoding ASCII` (owner/ISO8601/PID are pure ASCII) -> no BOM in ANY lock file.
#      PS 5.1 `Set-Content -Encoding UTF8` writes a BOM, which makes `$parts[0]` = FEFF+owner for
#      every reader that does not strip it (=> "cannot recognise my own lock" => zombie lock).
#   2. parse strips U+FEFF / U+200B and normalises whitespace (tolerates locks already on disk
#      that were written by the old UTF8 writers).
#   3. `play-running.lock` = the REAL lock (3 fields). We ALSO write the same content to the legacy
#      name `play.lock`: its only purpose is to stop the 17 historical runners that read ONLY the old
#      name (they would otherwise see "no lock" and start a second Play session). Reading both names
#      protects OURSELVES; writing the mirror protects EVERYBODY ELSE.
#   4. missing PID field == "unknown", NOT "dead" => a fresh lock without a PID is NEVER stolen;
#      only `age >= 12 min` (the single threshold) allows a takeover.
#   Plus: 1.1 s readback after writing, ABORT fail-fast BEFORE the editor is touched, only our own
#   lock files are ever deleted, and if the lock was taken over mid-run we do NOT stop the editor.
#
# SELF-CHECKS (`-SelfTest`, and also run at the top of every real session; any FAIL => exit 4
# BEFORE the editor is touched). Every one of them can FAIL -- they are not "print OK" lines:
#   * encoding: pure ASCII byte-wise OR UTF-8 with BOM (counts non-ASCII bytes, reads the header)
#   * lock writes use -Encoding ASCII (no UTF8 lock write anywhere)
#   * `editor_stop` appears exactly once in CODE (comments stripped) and sits inside the guard
#   * `editor_play` / `recompile` code lines are all AFTER the Take-Lock call (comments stripped --
#     a first version reported a false red because the file HEADER mentions editor_play)
#   * lock-decision truth table: fresh+noPID=YIELD, age13+noPID=TAKEOVER, age11.9=YIELD,
#     deadPID=TAKEOVER, livePID+fresh=YIELD
#   * a FOREIGN lock carrying a BOM parses to the right owner, is not "mine", and is NOT deleted
#     (sandbox is per-PID under .ai-tmp/test/, cleaned up in try/finally)
#
# Output (absolute paths):
#   <root>/.ai-tmp/test/u52resist_run_<tag>.txt              this run log
#   <root>/.ai-tmp/screenshots/d2u3_charstat_<tag>.png       panel screenshot (driver)
#   <root>/.ai-tmp/screenshots/d2u3_charstat_<tag>.done      done marker (driver)
#   <root>/.ai-tmp/screenshots/u3_charstat_{readings,screen}_<tag>.tsv   (driver)
#   <root>/.ai-tmp/screenshots/d2u3_charstat_evidence_<tag>.txt          frozen [D2U3C] lines
#
# ASCII only (PS 5.1 reads a BOM-less non-ASCII .ps1 as ANSI) -- asserted by the self-check.
# =============================================================================
param(
    [string]$Tag = 'u52resist',
    [int]$WaitSeconds = 220,
    [int]$LockWaitSeconds = 240,
    [switch]$SelfTest,
    [switch]$SelfCheckOnly,   # alias of -SelfTest: the NO-LOCK entry point (team-lead rule 2026-09-24)
    [string]$Why = 'sheet u52resist: the four resist rows of the character panel must be read on the real screen. Offline the new criterion (needNative less than availPx, recomputed from font16_chi_map.txt) says the 4-char labels need 63 art and the value column needs 42 art, but only a live session proves (a) the labels really draw on ONE line, (b) the line count of the renderer itself agrees, (c) the values stay on one line (the value column is a D2Label and has no uGUI Text at all), and (d) the pixels show no interleaving (u52play read 8 interleaved lines at 11:38:35).'
)
$ErrorActionPreference = 'Continue'

$root = Split-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) -Parent
$proj = Join-Path $root 'client'
$shots = Join-Path $root '.ai-tmp\screenshots'
$test = Join-Path $root '.ai-tmp\test'
$cs = Join-Path $root 'tools\probes\drivers\d2u3_charstat_drive.cs'
$runbg = Join-Path $root 'tools\probes\interact\p_runbg.cs'
$outLog = Join-Path $test ('u52resist_run_' + $Tag + '.txt')

# README 5.2 #30 (zorder accident, team-lead 2026-09-24): the self-check entry must NOT write any path a
 # normal run also writes. Before this line, -SelfTest truncated and re-appended THIS sheet's own run trace
 # (u52resist_run_<tag>.txt) -- the 12:5x session narrative and the 13:10 ABORT lines were lost that way.
 if ($SelfTest -or $SelfCheckOnly) { $outLog = Join-Path $test ('u52resist_selftest_' + $Tag + '.txt') }
$evFile = Join-Path $shots ('d2u3_charstat_evidence_' + $Tag + '.txt')
$done = Join-Path $shots ('d2u3_charstat_' + $Tag + '.done')
$lock = Join-Path $test 'play-running.lock'
$legacyLock = Join-Path $test 'play.lock'
$playLog = Join-Path $test 'play-log.tsv'
$me = 'u52resist'
$shotDir = ($shots -replace '\\', '/')
$spec = $shotDir + '|' + $Tag

function Say([string]$m) {
    $stamp = (Get-Date).ToString('HH:mm:ss.fff')
    # Write-Host (host stream), NOT Write-Output: a print call inside a lock function would otherwise
    # put a line into that function return pipeline => `if (Take-Lock)` sees a NON-EMPTY ARRAY and
    # reads as TRUE even when the race was LOST => fake exclusivity (u52play 2026-09-24, README 5.2 #12).
    Write-Host ($stamp + ' ' + $m)
    Add-Content -Path $outLog -Value ($stamp + ' ' + $m) -Encoding UTF8
}
function UC([string[]]$argv) {
    $raw = & unity command @argv --project-path $proj --format json --no-pager 2>&1 | Out-String
    return $raw
}

# ---- lock text parsing (v2.3: BOM tolerant) ------------------------------------------------
# Returns a hashtable: owner / iso / pid (-1 = the field is ABSENT = "unknown", never "dead").
function Parse-LockFields([string]$raw) {
    $flat = [string]$raw
    $flat = ($flat -replace "`r?`n", ' ')
    $flat = ($flat -replace '[\uFEFF\u200B]', ' ')     # PS 5.1 Set-Content -Encoding UTF8 wrote a BOM
    $flat = (($flat -replace '\s+', ' ')).Trim()
    $parts = @($flat -split ' ')
    $lockPid = -1
    if ($parts.Count -ge 3) { [int]::TryParse($parts[2], [ref]$lockPid) | Out-Null }
    $iso = ''
    if ($parts.Count -ge 2) { $iso = $parts[1] }
    return @{ owner = ($parts[0]); iso = $iso; pid = $lockPid; raw = $flat }
}
function Get-LockShot([string]$path) {
    if (-not (Test-Path $path)) { return $null }
    $raw = ''
    try { $raw = (Get-Content $path -Raw) } catch { $raw = '' }
    $s = Parse-LockFields $raw
    $s['path'] = $path
    return $s
}
# Is this lock file MINE? (owner AND pid must match -- a foreign file with the same owner but a
# different PID is NOT mine, and a BOM-prefixed foreign file must not be mistaken for mine either.)
function Is-Mine($shot) {
    if ($null -eq $shot) { return $false }
    return ($shot.owner -eq $me -and $shot.pid -eq $PID)
}
function My-Lock() {
    $s = Get-LockShot $lock
    if ($null -eq $s) { return $false }
    return (Is-Mine $s)
}
# v2.1: missing PID == unknown, NOT dead. v2.3 keeps it: a fresh lock is never stolen.
function Lock-Decision([double]$ageMin, [bool]$pidKnown, [bool]$alive) {
    if ($pidKnown -and (-not $alive)) { return 'TAKEOVER' }   # the owner process is gone
    if ($ageMin -ge 12) { return 'TAKEOVER' }                 # the single threshold
    return 'YIELD'                                            # fresh, incl. "no PID field" case
}
# Primary readiness source: `unity command editor_status`. FOUR outcomes:
#   playing / stopped / unknown (bridge answered but no playMode field -- NOT "not playing")
#   / bridge-down (`success:false` / COMMAND_FAILED => no editor process at all).
# Prints nothing: a print call would pollute this function return value (README 5.2 #12).
function Editor-PlayMode() {
    $raw = UC @('editor_status')
    if ($raw -match '"playMode":\s*"playing"') { return 'playing' }
    if ($raw -match '"playMode":\s*"stopped"') { return 'stopped' }
    if ($raw -match 'COMMAND_FAILED|"success":\s*false') { return 'bridge-down' }
    return 'unknown'
}

# Pure readiness-gate decision (unit-testable offline, no editor needed):
#   playing -> ABORT / unknown -> YIELD (fail-closed) / bridge-down -> RETRY-BRIDGE / stopped -> PROCEED
function Gate-Decision([string]$pm) {
    if ($pm -eq 'playing') { return 'ABORT' }
    if ($pm -eq 'unknown') { return 'YIELD' }
    if ($pm -eq 'bridge-down') { return 'RETRY-BRIDGE' }
    return 'PROCEED'
}

function Take-Lock() {
    $body = $me + ' ' + (Get-Date).ToString('o') + ' ' + $PID
    Set-Content -Path $lock -Value $body -Encoding ASCII       # v2.3: ASCII => no BOM
    Start-Sleep -Milliseconds 1100
    if (My-Lock) {
        Set-Content -Path $legacyLock -Value $body -Encoding ASCII   # v2.2 mirror (legacy-name runners)
        Say ('LEGACY-MIRROR-WRITTEN ' + $legacyLock + ' owner=' + $me + ' pid=' + $PID)
        Say ('LOCK-TAKEN ' + (Get-LockShot $lock).raw)
        return $true
    }
    # lost the race: drop our own mirror (content check only -- never touch a foreign file)
    $ms = Get-LockShot $legacyLock
    if (Is-Mine $ms) { Remove-Item $legacyLock -Force -ErrorAction SilentlyContinue }
    Say ('LOCK-RACE-LOST readback=' + (Get-LockShot $lock).raw + ' -> yield (v2 rule 2)')
    return $false
}
function Release-Lock() {
    # v2.2: both names, but only files whose content is OURS (never somebody else's).
    foreach ($f in @($lock, $legacyLock)) {
        if (-not (Test-Path $f)) { Say ('LOCK-RELEASE skip ' + (Split-Path $f -Leaf) + ' (absent)'); continue }
        $s = Get-LockShot $f
        if (Is-Mine $s) {
            Remove-Item $f -Force -ErrorAction SilentlyContinue
            Say ('LOCK-RELEASED ' + (Split-Path $f -Leaf) + ' (mine)')
        } else {
            Say ('LOCK-RELEASE REFUSED ' + (Split-Path $f -Leaf) + ' (owned by ' + $s.raw + ') -> left alone (v2 rule 4)')
        }
    }
}
# v2 rule 4: if the lock was taken over by somebody else mid-run, do NOT stop the editor.
# README 5.2 #42 rule 2.1 (team-lead 2026-09-24): `editor_stop` is IDEMPOTENT, so if the editor has not
# left Play yet we RETRY it (2..3 times with an interval) BEFORE releasing the locks -- one failed stop
# is not a reason to hand the whole problem to the team. Guarded by My-Lock: never touch the editor
# when the lock is not ours.
function Stop-Editor-Retry([int]$tries) {
    for ($i = 1; $i -le $tries; $i++) {
        if (-not (My-Lock)) { Say ('EDITOR-STOP-RETRY skipped try=' + $i + ' (lock not mine)'); return }
        Say ('EDITOR-STOP-RETRY try=' + $i)
        UC @('editor_stop') | Out-Null
        Start-Sleep -Seconds 2
        if ((Editor-PlayMode) -eq 'stopped') { Say ('EDITOR-STOP-RETRY stopped after try=' + $i); return }
    }
    Say 'EDITOR-STOP-RETRY still not stopped -> the CALLER decides; under #42 the lock is HELD on purpose (never released into an unowned orphan play)'
}

function Stop-Editor-Safe([string]$why) {
    if (-not (My-Lock)) {
        # README 5.2 #42 rule 3: skipping the stop because the lock is not mine must NOT be silent --
        # the next owner has to know whether a Play session is still running (read-only probe).
        $os = Get-LockShot $lock
        $opm = Editor-PlayMode
        Say ('EDITOR-STOP-SKIPPED (' + $why + ') lock is not mine: ' + $os.raw + ' -> v2 rule 4')
        if ($opm -eq 'playing') {
            Say ('ORPHAN-PLAY owner=' + $os.owner + ' pid=' + $os.pid + ' cleanupOwner=' + $os.owner + ' (last session owner) | main agent; playMode=playing -> I did NOT stop it and did NOT touch the locks; the next owner must confirm playMode=stopped first (README 5.2 #42)')
        }
        return
    }
    Say ('EDITOR-STOP ' + $why)
    UC @('editor_stop') | Out-Null
}

# README 64 (team-lead 2026-09-24, measured by u3bverify in BOTH directions): a CALL SITE must be
# judged on the AST (command name + arguments), never on text. A string literal or a comment that
# merely MENTIONS a name used to produce a FALSE RED (their own KNOWN-BAD fixture generator was read
# as a call), and an UNQUOTED command-mode argument was INVISIBLE to the old quoted-regex (calls=0 was
# really calls=1) -- i.e. the text route produced a FALSE GREEN. Fail-closed: $null means "the file
# could not be parsed" and every caller must turn that into FAIL. When $ArgValue is given, the command
# must carry a string-constant argument with exactly that value (e.g. the editor_stop invocation).
function Get-CallSiteAsts([string]$Path, [string]$Name, [string]$ArgValue) {
    try {
        $tok = $null; $perr = $null
        $astRoot = [System.Management.Automation.Language.Parser]::ParseFile($Path, [ref]$tok, [ref]$perr)
        $found = @()
        foreach ($c in $astRoot.FindAll({ param($n) $n -is [System.Management.Automation.Language.CommandAst] }, $true)) {
            if ($c.GetCommandName() -ne $Name) { continue }
            if ($ArgValue) {
                # The argument can be BARE (`unity command editor_stop ...` => a StringConstantExpressionAst
                # element) or WRAPPED (`UC @('editor_stop')` => the literal lives inside an ArrayExpressionAst).
                # Searching the command's OWN subtree finds both, yet still ignores every other command:
                # a first version only accepted the bare element and reported calls=0 on this very file.
                $hasArg = @($c.FindAll({ param($n) $n -is [System.Management.Automation.Language.StringConstantExpressionAst] -and $n.Value -eq $ArgValue }, $true)).Count -gt 0
                if (-not $hasArg) { continue }
            }
            $found += $c
        }
        return [pscustomobject]@{ Ok = $true; Calls = @($found) }
    } catch { return [pscustomobject]@{ Ok = $false; Calls = @() } }
}

# Guard-window probe shared by the two call-site checks: look at the raw lines above the call, but
# SKIP comment-only lines (so a comment that mentions a guard cannot grandfather a real call).
function Test-GuardWindowAbove([string[]]$AllLines, [int]$LineNo, [string[]]$Tokens) {
    for ($k = [Math]::Max(1, $LineNo - 12); $k -lt $LineNo; $k++) {
        $l = $AllLines[$k - 1]
        if ($l -match '^\s*#') { continue }
        foreach ($tk in $Tokens) { if ($l.Contains($tk)) { return $true } }
    }
    return $false
}

# =============================================================================
# self-checks -- every item below CAN fail; they run BEFORE any lock/editor action
# =============================================================================
function Self-Check([switch]$Sandbox) {
    $fails = 0
    $src = $PSCommandPath
    $bytes = [IO.File]::ReadAllBytes($src)
    $bom = ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF)
    $nonAscii = 0
    foreach ($b in $bytes) { if ($b -gt 127) { $nonAscii++ } }
    $encOk = (($nonAscii -eq 0) -or $bom)
    Say ('SELF-CHK encoding nonAsciiBytes=' + $nonAscii + ' bom=' + $bom + ' rule="pure ASCII OR UTF8+BOM" verdict=' + $(if ($encOk) { 'PASS' } else { 'FAIL' }))
    if (-not $encOk) { $fails++ }

    $text = [IO.File]::ReadAllText($src)
    $lines = @($text -split "`r?`n")
    # comment-stripped view -- a first version of this check reported a FALSE RED because the file
    # HEADER mentions editor_play (team-lead, 2026-09-24: comments are not code).
    $code = @()
    foreach ($ln in $lines) { if ($ln -notmatch '^\s*#') { $code += $ln } }

    $utf8LockWrites = 0
    $asciiLockWrites = 0
    foreach ($ln in $code) {
        if ($ln -match 'Set-Content' -and $ln -match '\$(lock|legacyLock)\b') {
            if ($ln -match '-Encoding\s+UTF8') { $utf8LockWrites++ }
            if ($ln -match '-Encoding\s+ASCII') { $asciiLockWrites++ }
        }
    }
    Say ('SELF-CHK lock-writes ascii=' + $asciiLockWrites + ' utf8=' + $utf8LockWrites + ' verdict=' + $(if ($utf8LockWrites -eq 0 -and $asciiLockWrites -ge 2) { 'PASS' } else { 'FAIL' }))
    if (-not ($utf8LockWrites -eq 0 -and $asciiLockWrites -ge 2)) { $fails++ }

    # Count only the real INVOCATION, and build the token by concatenation so this very check does
    # not match its own source text (the first version counted 3: the two lines of this function
    # plus the guard -- an assertion must not judge the wrong sample).
    # #42 rule 2.1 changed the count: there are now TWO legitimate stop calls (one in Stop-Editor-Safe,
    # one in Stop-Editor-Retry) => "exactly once" is the wrong criterion. The real invariant is: EVERY
    # editor_stop invocation must sit inside a My-Lock guard (a stray unguarded stop is the accident
    # class this check exists for). Still fail-able: an unguarded call anywhere turns it red.
    $stopAgg = @()
    $stopParseOk = $true
    foreach ($cn in @('UC', 'unity', 'Unity-Cmd')) {
        $r = Get-CallSiteAsts $PSCommandPath $cn ('editor' + '_stop')
        if (-not $r.Ok) { $stopParseOk = $false; break }
        $stopAgg += $r.Calls
    }
    $stopCount = 0
    if ($stopParseOk) { $stopCount = @($stopAgg).Count }
    $unguarded = 0
    if ($stopParseOk) {
        foreach ($c in $stopAgg) {
            if (-not (Test-GuardWindowAbove $lines $c.Extent.StartLineNumber @('My-Lock'))) { $unguarded++ }
        }
    }
    $stopOk = $stopParseOk -and ($stopCount -ge 1) -and ($unguarded -eq 0)
    Say ('SELF-CHK editor_stop-guarded calls=' + $stopCount + ' unguarded=' + $unguarded + ' verdict=' + $(if ($stopOk) { 'PASS' } else { 'FAIL' }))
    if (-not $stopOk) { $fails++ }

    $takeIdx = -1
    for ($i = 0; $i -lt $code.Count; $i++) { if ($code[$i] -match 'Take-Lock') { $takeIdx = $i } }   # LAST match = the call site, not the definition
    # Same trap as above, second instance: the pattern string must NOT appear literally in this
    # file, or the check matches its own source line (team-lead 2026-09-24: assertions must not
    # judge the wrong sample). Build both tokens by concatenation.
    $before = @()
    $edPlay = "editor" + "_play"
    $recTok = [char]39 + "recompile" + [char]39
    for ($i = 0; $i -lt $code.Count; $i++) {
        if ($i -ge $takeIdx -and $takeIdx -ge 0) { continue }
        if ($code[$i].Contains($edPlay) -or $code[$i].Contains($recTok)) { $before += $i }
    }
    $orderOk = ($takeIdx -ge 0) -and ($before.Count -eq 0)
    Say ('SELF-CHK lock-taken-before-editor takeLockLine=' + $takeIdx + ' editorCallsBefore=' + ($before -join ',') + ' verdict=' + $(if ($orderOk) { 'PASS' } else { 'FAIL' }))
    if (-not $orderOk) { $fails++ }

    # lock-decision truth table (v2.1/v2.3): missing PID == unknown, never dead
    $cases = @(
        @{ n = 'fresh(2m)+noPID';      a = 2.0;   p = $false; v = $true;  want = 'YIELD' },
        @{ n = 'age11.9+noPID';        a = 11.9;  p = $false; v = $false; want = 'YIELD' },
        @{ n = 'age13+noPID';          a = 13.0;  p = $false; v = $false; want = 'TAKEOVER' },
        @{ n = 'fresh(2m)+deadPID';    a = 2.0;   p = $true;  v = $false; want = 'TAKEOVER' },
        @{ n = 'fresh(2m)+livePID';    a = 2.0;   p = $true;  v = $true;  want = 'YIELD' },
        @{ n = 'age13+livePID';        a = 13.0;  p = $true;  v = $true;  want = 'TAKEOVER' }
    )
    $decFails = @()
    foreach ($c in $cases) {
        $got = Lock-Decision $c.a $c.p $c.v
        if ($got -ne $c.want) { $decFails += ($c.n + '(got ' + $got + ', want ' + $c.want + ')') }
    }
    Say ('SELF-CHK lock-decision cases=' + $cases.Count + ' bad=' + $decFails.Count + ' ' + ($decFails -join '; ') + ' verdict=' + $(if ($decFails.Count -eq 0) { 'PASS' } else { 'FAIL' }))
    if ($decFails.Count -ne 0) { $fails++ }

    # BOM-headed FOREIGN lock: must parse to the right owner, must NOT be "mine", must NOT be deleted
    $bomSample = ([char]0xFEFF) + 'bomowner 2026-09-24T12:00:00+08:00 999998'
    $s = Parse-LockFields $bomSample
    $parseOk = ($s.owner -eq 'bomowner') -and ($s.pid -eq 999998)
    Say ('SELF-CHK bom-lock-parsed owner=' + $s.owner + ' pid=' + $s.pid + ' verdict=' + $(if ($parseOk) { 'PASS' } else { 'FAIL' }))
    if (-not $parseOk) { $fails++ }
    $mineOk = -not (Is-Mine $s)
    Say ('SELF-CHK bom-lock-not-mine (foreign owner must never be treated as ours) verdict=' + $(if ($mineOk) { 'PASS' } else { 'FAIL' }))
    if (-not $mineOk) { $fails++ }

    $twoField = Parse-LockFields 'oldrunner 2026-09-24T11:50:07+08:00'
    $twoOk = ($twoField.pid -eq -1)
    Say ('SELF-CHK parse-2field-noPID pid=' + $twoField.pid + ' (=unknown, NOT dead) verdict=' + $(if ($twoOk) { 'PASS' } else { 'FAIL' }))
    if (-not $twoOk) { $fails++ }

    # per-PID sandbox (never a fixed path: two sheets running at once would delete each other's dir)
    if ($Sandbox) {
        $sand = Join-Path $test ('u52resist-self-' + $PID)
        try {
            if (-not (Test-Path $sand)) { New-Item -ItemType Directory -Path $sand | Out-Null }
            $f = Join-Path $sand 'play.lock'
            Set-Content -Path $f -Value $bomSample -Encoding UTF8      # deliberately the OLD buggy writer
            $shot = Get-LockShot $f
            $kept = Test-Path $f
            Say ('SELF-CHK bom-lock-not-deleted file=' + (Split-Path $f -Leaf) + ' stillThere=' + $kept + ' verdict=' + $(if ($kept) { 'PASS' } else { 'FAIL' }))
            if (-not $kept) { $fails++ }
        } finally {
            Remove-Item $sand -Recurse -Force -ErrorAction SilentlyContinue
            Say ('SELF-CHK sandbox-cleaned ' + $sand)
        }
    }
    # u52play bug #4 (README 5.2 #12): the print helper must not use Write-Output (it would pollute the
    # lock function return value and make `if (Take-Lock)` TRUE on a LOST race = fake exclusivity).
    # Both tokens are built by concatenation so these assertions cannot match their own source line.
    $woTok = "Write-" + "Output"
    $whTok = "Write-" + "Host"
    $sayIdx = -1
    for ($i = 0; $i -lt $code.Count; $i++) { if ($code[$i] -match 'function Say\(') { if ($sayIdx -lt 0) { $sayIdx = $i } } }
    $sayBody = @()
    if ($sayIdx -ge 0) { for ($k = $sayIdx; $k -lt [Math]::Min($sayIdx + 8, $code.Count); $k++) { $sayBody += $code[$k] } }
    $sayBad = 0
    foreach ($l in $sayBody) { if ($l.Contains($woTok)) { $sayBad++ } }
    $sayOk = ($sayIdx -ge 0) -and ($sayBad -eq 0) -and (($sayBody -join ' ').Contains($whTok))
    Say ('SELF-CHK say-uses-WriteHost writeOutputHits=' + $sayBad + ' verdict=' + $(if ($sayOk) { 'PASS' } else { 'FAIL' }))
    if (-not $sayOk) { $fails++ }
    $tcTok = '(Take-Lock) -eq ' + '$true'
    $tcHits = 0
    foreach ($l in $code) { if ($l.Contains($tcTok)) { $tcHits++ } }
    $tcOk = ($tcHits -ge 1)
    Say ('SELF-CHK take-lock-compared-explicitly hits=' + $tcHits + ' verdict=' + $(if ($tcOk) { 'PASS' } else { 'FAIL' }))
    if (-not $tcOk) { $fails++ }

    # v2.4 (zorder/team-lead): readiness = lock AND playMode; `playing` => ABORT before the lock is
    # touched; and the lock must not be released before the editor left Play (bounded poll).
    # Tokens built by concatenation so these assertions cannot match their own source lines.
    $rgTok = 'RELEASE' + '-GATE'
    $abTok = 'editor' + '-is-playing'
    $rgHits = 0
    $abHits = 0
    foreach ($l in $code) { if ($l.Contains($rgTok)) { $rgHits++ }; if ($l.Contains($abTok)) { $abHits++ } }
    $gateOk = ($rgHits -ge 2) -and ($abHits -ge 1)

    # v2.4 rule 4 (classcols): the playMode gate must be FAIL-CLOSED, and the old console fallback
    # (which could override the primary verdict) must be GONE. Tokens by concatenation.
    $unTok = 'LOCK-GATE' + '-UNKNOWN'
    $bdTok = 'LOCK-GATE' + '-BRIDGE-DOWN'
    $fbTok = 'EDITOR-STATUS' + '-FALLBACK'
    $unHits = 0
    $bdHits = 0
    $fbHits = 0
    foreach ($l in $code) { if ($l.Contains($unTok)) { $unHits++ }; if ($l.Contains($bdTok)) { $bdHits++ }; if ($l.Contains($fbTok)) { $fbHits++ } }
    $fcOk = ($unHits -ge 1) -and ($bdHits -ge 1) -and ($fbHits -eq 0)
    Say ('SELF-CHK lock-gate-fail-closed unknownHits=' + $unHits + ' bridgeDownHits=' + $bdHits + ' legacyFallbackHits=' + $fbHits + ' verdict=' + $(if ($fcOk) { 'PASS' } else { 'FAIL' }))
    if (-not $fcOk) { $fails++ }

    # team-lead rule 2 (2026-09-24): every exit path must print a terminal marker, the gate must sit
    # INSIDE the lock retry loop, and no top-level `continue` may exist (it would silently end the
    # script with exit 0). Tokens by concatenation so these assertions cannot match their own lines.
    $endTok = 'Say ' + [char]39 + 'END' + [char]39
    $exitHits = 0
    $endHits = 0
    foreach ($l in $code) { if ($l.TrimStart().StartsWith('exit ')) { $exitHits++ }; if ($l.Contains($endTok)) { $endHits++ } }
    $markOk = ($exitHits -eq $endHits)
    Say ('SELF-CHK terminal-marker-per-exit exits=' + $exitHits + ' endMarkers=' + $endHits + ' verdict=' + $(if ($markOk) { 'PASS' } else { 'FAIL' }))
    if (-not $markOk) { $fails++ }
    # team-lead rule (charstat 2026-09-24): loop membership MUST be judged on the AST Parent chain.
    # Indentation/regex/line-ranges are PROVEN wrong here -- the reference runner was judged the
    # OPPOSITE of the truth by an indentation walk (a closed `}` is not a loop keyword, so the walk
    # keeps climbing into an inner loop). Fail-closed: any parse error or missing evidence => FAIL.
    $astOutside = 0
    $astGateInLoop = $false
    $astErr = ''
    try {
        $astTok = $null
        $astPerr = $null
        $ast = [System.Management.Automation.Language.Parser]::ParseFile($PSCommandPath, [ref]$astTok, [ref]$astPerr)
        $contAst = $ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.ContinueStatementAst] }, $true)
        foreach ($c in $contAst) {
            $p = $c.Parent
            $inLoop = $false
            while ($null -ne $p) { if ($p -is [System.Management.Automation.Language.LoopStatementAst]) { $inLoop = $true; break }; $p = $p.Parent }
            if (-not $inLoop) { $astOutside++ }
        }
        $gateAst = $ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.CommandAst] -and $n.Extent.Text.Contains('LOCK-GATE' + '-UNKNOWN') }, $true)
        foreach ($g in $gateAst) {
            $p = $g.Parent
            while ($null -ne $p) { if ($p -is [System.Management.Automation.Language.LoopStatementAst]) { $astGateInLoop = $true; break }; $p = $p.Parent }
        }
    } catch { $astErr = $_.Exception.Message }
    $astOk = ($astOutside -eq 0) -and $astGateInLoop -and ($astErr.Length -eq 0)
    Say ('SELF-CHK gate-inside-lock-loop(ast) continueOutsideLoop=' + $astOutside + ' gateInsideLoop=' + $(if ($astGateInLoop) { 1 } else { 0 }) + ' err="' + $astErr + '" verdict=' + $(if ($astOk) { 'PASS' } else { 'FAIL' }))
    if (-not $astOk) { $fails++ }
    # README 5.2 #29 (team-lead 2026-09-24): the REAL parser is the only thing that catches a broken
    # multi-line Say-continuation (PS 5.1 raises Missing closing paren only at CALL time, never at
    # parse-of-file time in a way we notice). Reuses the parse done above; any syntax error => FAIL.
    $parseErrCount = 0
    if ($null -ne $astPerr) { $parseErrCount = @($astPerr).Count }
    $prOk = ($parseErrCount -eq 0)
    Say ('SELF-CHK parse-errors-zero parserErrors=' + $parseErrCount + ' verdict=' + $(if ($prOk) { 'PASS' } else { 'FAIL' }))
    if (-not $prOk) { $fails++ }
    # README 5.2 #34 (team-lead 2026-09-24, three-way rule): a negative sample only proves the check
    # CAN fail -- not that it fails FOR THE RIGHT REASON. Direction 2 = inject a known-bad sample and
    # assert THIS check turns red; direction 3 = inject the repaired fragment and assert it turns green.
    $injBad = -1
    $injGood = -1
    $sand2 = Join-Path $test ('u52resist-inj-' + $PID)
    try {
        if (-not (Test-Path $sand2)) { New-Item -ItemType Directory -Path $sand2 | Out-Null }
        $badFile = Join-Path $sand2 'bad.ps1'
        $goodFile = Join-Path $sand2 'good.ps1'
        [IO.File]::WriteAllText($badFile, "Say ('unclosed" + [char]10, (New-Object Text.UTF8Encoding($false)))
        [IO.File]::WriteAllText($goodFile, "Say ('closed')" + [char]10, (New-Object Text.UTF8Encoding($false)))
        $bjT = $null
        $bjE = $null
        [System.Management.Automation.Language.Parser]::ParseFile($badFile, [ref]$bjT, [ref]$bjE) | Out-Null
        $injBad = @($bjE).Count
        $gdT = $null
        $gdE = $null
        [System.Management.Automation.Language.Parser]::ParseFile($goodFile, [ref]$gdT, [ref]$gdE) | Out-Null
        $injGood = @($gdE).Count
    } finally {
        Remove-Item $sand2 -Recurse -Force -ErrorAction SilentlyContinue
    }
    $injOk = ($injBad -ge 1) -and ($injGood -eq 0)
    Say ('SELF-CHK parse-check-three-way knownBadErrors=' + $injBad + ' positiveControlErrors=' + $injGood + ' verdict=' + $(if ($injOk) { 'PASS' } else { 'FAIL' }))
    if (-not $injOk) { $fails++ }

    # u3bverify self-correction (team-lead 2026-09-24): a fingerprint printed BEFORE the parse can
    # describe a different revision than the AST we just walked -- "a row that prints a version it did
    # not judge". Re-hash AFTER the parse; if the bytes moved, say so in the row AND fail (fail-closed).
    $stabTok = 'UNSTABLE-AFTER' + '-PARSE'
    $stabNote = ''
    try {
        $hashNow = (($selfSha.ComputeHash([IO.File]::ReadAllBytes($PSCommandPath)) | ForEach-Object { $_.ToString('x2') }) -join '')
        if ($hashNow -ne $selfHash) { $stabNote = ' ' + $stabTok + ' now=' + $hashNow.Substring(0,16) + ' => this row judged the bytes of ' + $selfHash.Substring(0,16) }
    } catch { $stabNote = ' ' + $stabTok + ' rehash-failed' }
    $stabOk = ($stabNote.Length -eq 0)
    Say ('SELF-CHK fingerprint-stable-after-parse judged=' + $selfHash.Substring(0,16) + $stabNote + ' verdict=' + $(if ($stabOk) { 'PASS' } else { 'FAIL' }))
    if (-not $stabOk) { $fails++ }

    # #42 rules 2.1 / 2.2: releasing must be preceded by an editor_stop RETRY, and the ORPHAN-PLAY alarm
    # must name who cleans up. Tokens by concatenation (never match this line itself).
    $rtTok = 'EDITOR-STOP' + '-RETRY'
    $coTok = 'cleanup' + 'Owner='
    $rtHits = 0
    $coHits = 0
    foreach ($l in $code) { if ($l.Contains($rtTok)) { $rtHits++ }; if ($l.Contains($coTok)) { $coHits++ } }
    $r42Ok = ($rtHits -ge 1) -and ($coHits -ge 1)
    Say ('SELF-CHK release-retry-and-orphan-owner retryHits=' + $rtHits + ' cleanupOwnerHits=' + $coHits + ' verdict=' + $(if ($r42Ok) { 'PASS' } else { 'FAIL' }))
    if (-not $r42Ok) { $fails++ }

    # #42 item 4 (team-lead 2026-09-24, d2u27 root cause): an assertion that the stop code EXISTS is not
    # enough -- the d2u27 reference had `editor_stop` exactly once inside its guard function and STILL
    # never stopped the editor, because the normal teardown never CALLED the guard. So check REACHABILITY
    # on the AST: at least one call to the stop guard must sit on an UNCONDITIONAL path (no
    # IfStatementAst on its Parent chain). Fail-closed: parse failure => -1 => FAIL. Reuses the parse above.
    $trigUncond = 0
    try {
        $trigAst = $ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.CommandAst] -and $n.Extent.Text.StartsWith('Stop-Editor-Safe') }, $true)
        foreach ($tg in $trigAst) {
            $p = $tg.Parent
            $cond = $false
            while ($null -ne $p) { if ($p -is [System.Management.Automation.Language.IfStatementAst]) { $cond = $true; break }; $p = $p.Parent }
            if (-not $cond) { $trigUncond++ }
        }
    } catch { $trigUncond = -1 }
    $trigOk = ($trigUncond -ge 1)
    Say ('SELF-CHK stop-trigger-reachable unconditionalStopGuardCalls=' + $trigUncond + ' verdict=' + $(if ($trigOk) { 'PASS' } else { 'FAIL' }))
    if (-not $trigOk) { $fails++ }

    # #42 2026-09-24 13:5x (team-lead reversed its own approval): an orphan play must be HELD, not handed
    # over. First token by concatenation; the second is a regression guard (the withdrawn wording must
    # not come back). Fail-able in both directions.
    $heldTok = 'LOCK-HELD' + '-ON-PURPOSE'
    $oldTok = 'releasing' + ' anyway'
    $heldHits = 0
    $oldHits = 0
    foreach ($l in $code) { if ($l.Contains($heldTok)) { $heldHits++ }; if ($l.Contains($oldTok)) { $oldHits++ } }
    $holdOk = ($heldHits -ge 2) -and ($oldHits -eq 0)
    Say ('SELF-CHK hold-lock-on-orphan heldMarkerHits=' + $heldHits + ' withdrawnWordingHits=' + $oldHits + ' verdict=' + $(if ($holdOk) { 'PASS' } else { 'FAIL' }))
    if (-not $holdOk) { $fails++ }
    $topCont = 0
    foreach ($l in $code) { if ($l.StartsWith('continue') -and $l.Trim().StartsWith('continue')) { $topCont++ } }
    $contOk = ($topCont -eq 0)
    Say ('SELF-CHK no-top-level-continue hits=' + $topCont + ' verdict=' + $(if ($contOk) { 'PASS' } else { 'FAIL' }))
    if (-not $contOk) { $fails++ }
    Say ('SELF-CHK release-gate-and-playing-abort releaseGateHits=' + $rgHits + ' playingAbortHits=' + $abHits + ' verdict=' + $(if ($gateOk) { 'PASS' } else { 'FAIL' }))
    if (-not $gateOk) { $fails++ }

    # ---- README 57(b) (team-lead 2026-09-24; promoted from this sheet) ---------------------------
    # The comment-stripped view ($code) is what every assertion above judges, so a decided change that
    # only lands in a COMMENT is invisible to all of them. Measured here: the tail comment still claimed
    # the older window and a release-anyway rule while the behaviour had already been reversed (bounded
    # poll + editor_stop retries + HOLD the lock on an orphan). Fix = machine-readable expression:

    # (1) stale wording, WHOLE FILE (comments included), with a POSITIVE CONTROL BY INJECTION: a matcher
    #     that can never match is not a gate, so "0 hits on the real file" must be shown alongside "the
    #     same matcher hits a synthetic line that carries the withdrawn wording".
    $staleToks = @()
    $staleToks += ('release' + ' regardless')
    $staleToks += ('releasing' + ' anyway')
    $staleToks += ('bounded ' + '5 x 1s')
    $staleHits = 0
    foreach ($ln in $lines) { foreach ($st in $staleToks) { if ($ln.IndexOf($st, [StringComparison]::OrdinalIgnoreCase) -ge 0) { $staleHits++ } } }
    $injLine = '# Poll, bounded ' + '5 x 1s, for playMode=stopped; release' + ' regardless afterwards.'
    $injHits = 0
    foreach ($st in $staleToks) { if ($injLine.IndexOf($st, [StringComparison]::OrdinalIgnoreCase) -ge 0) { $injHits++ } }
    # Broadcast 2026-09-24 item 2 (PS `-match` is case-INsensitive, but String.Contains is case-SENSITIVE):
    # a capitalised variant would slip past a Contains()-based gate. So the matcher is OrdinalIgnoreCase and
    # BOTH controls must fire: a CAPS-variant hit sample, and a plain-prose MISS sample (a matcher that
    # hits everything is as useless as one that hits nothing).
    $injCaps = '# Poll, Bounded ' + '5 x 1s, ...; Release' + ' Regardless afterwards.'
    $capsHits = 0
    foreach ($st in $staleToks) { if ($injCaps.IndexOf($st, [StringComparison]::OrdinalIgnoreCase) -ge 0) { $capsHits++ } }
    $missLine = '# a plain prose line that carries none of the withdrawn wordings'
    $missHits = 0
    foreach ($st in $staleToks) { if ($missLine.IndexOf($st, [StringComparison]::OrdinalIgnoreCase) -ge 0) { $missHits++ } }
    $staleOk = ($staleHits -eq 0) -and ($injHits -ge 2) -and ($capsHits -ge 2) -and ($missHits -eq 0)
    Say ('SELF-CHK stale-wording wholeFileHits=' + $staleHits + ' injectedHits=' + $injHits + ' capsVariantHits=' + $capsHits + ' plainProseHits=' + $missHits + ' verdict=' + $(if ($staleOk) { 'PASS' } else { 'FAIL' }))
    if (-not $staleOk) { $fails++ }

    # (2) every threshold pair quoted in a COMMENT ("N x Ms") must still exist in CODE as `-le/-lt N`
    #     plus `-Seconds M`. Edit the loop and forget the comment (or the reverse) => red. That is
    #     precisely the drift class of 57(b). The `>= 2 pairs` floor is itself fail-able: deleting the
    #     comments would otherwise make this check vacuously green.
    $thPairs = @()
    foreach ($ln in $lines) {
        if ($ln -notmatch '^\s*#') { continue }
        foreach ($tm in [regex]::Matches($ln, '(?i)\d+\s*x\s*\d+\s*s')) { $thPairs += $tm.Value }
    }
    $thBad = 0
    $thSeen = ''
    foreach ($pv in $thPairs) {
        $tm2 = [regex]::Match($pv, '(?i)(\d+)\s*x\s*(\d+)s')
        $n = $tm2.Groups[1].Value; $s = $tm2.Groups[2].Value
        $thSeen += ($n + 'x' + $s + ' ')
        $hasN = $false; $hasS = $false
        foreach ($cl in $code) {
            if ($cl -match ('-le\s+' + $n + '\b') -or $cl -match ('-lt\s+' + $n + '\b')) { $hasN = $true }
            if ($cl -match ('-Seconds\s+' + $s + '\b')) { $hasS = $true }
        }
        if (-not ($hasN -and $hasS)) { $thBad++ }
    }
    $thOk = ($thPairs.Count -ge 2) -and ($thBad -eq 0)
    Say ('SELF-CHK comment-thresholds-match-code pairs=' + $thPairs.Count + ' seen=[' + $thSeen.Trim() + '] bad=' + $thBad + ' verdict=' + $(if ($thOk) { 'PASS' } else { 'FAIL' }))
    if (-not $thOk) { $fails++ }

    # (3) the ONE takeover threshold: documented in the header as a lower bound in minutes AND used as a
    #     bare literal in code. Assert both exist and are equal -- changing either side alone goes red.
    $docMin = -1
    foreach ($ln in $lines) {
        $dm = [regex]::Match($ln, 'age\s*>=\s*(\d+)\s*min')
        if ($dm.Success) { $docMin = [int]$dm.Groups[1].Value; break }
    }
    $codeMin = -1
    foreach ($cl in $code) {
        if ($cl -match ('return\s+' + [char]39 + 'TAKEOVER' + [char]39)) {
            $cm = [regex]::Match($cl, '-ge\s+(\d+)')
            if ($cm.Success) { $codeMin = [int]$cm.Groups[1].Value }
        }
    }
    $tkOk = ($docMin -gt 0) -and ($docMin -eq $codeMin)
    Say ('SELF-CHK takeover-threshold commentMin=' + $docMin + ' codeMin=' + $codeMin + ' verdict=' + $(if ($tkOk) { 'PASS' } else { 'FAIL' }))
    if (-not $tkOk) { $fails++ }

    # (4) the RELEASE condition: Release-Lock may only be called (a) after the editor really left Play,
    #     or (b) when no session was ever started (play never came up). An unguarded third call site is
    #     the accident class this check exists for. Same 12-code-line window shape as editor_stop-guarded.
    $rlCall = 'Release' + '-Lock'
    $rlProbe = Get-CallSiteAsts $PSCommandPath $rlCall $null
    $rlAsts = @($rlProbe.Calls)
    $rlCount = 0
    if ($rlProbe.Ok) { $rlCount = $rlAsts.Count }
    $rlUnguarded = 0
    if ($rlProbe.Ok) {
        foreach ($c in $rlAsts) {
            if (-not (Test-GuardWindowAbove $lines $c.Extent.StartLineNumber @('$leftPlay', 'PLAY-FAILED'))) { $rlUnguarded++ }
        }
    }
    $rlOk = $rlProbe.Ok -and ($rlCount -ge 2) -and ($rlUnguarded -eq 0)
    Say ('SELF-CHK release-call-sites calls=' + $rlCount + ' unguarded=' + $rlUnguarded + ' verdict=' + $(if ($rlOk) { 'PASS' } else { 'FAIL' }))
    if (-not $rlOk) { $fails++ }

    return $fails
}

foreach ($d in @($shots, $test)) { if (-not (Test-Path $d)) { New-Item -ItemType Directory -Path $d | Out-Null } }
Set-Content -Path $outLog -Value ('# u52resist_run tag=' + $Tag + ' ' + (Get-Date).ToString('s')) -Encoding ASCII

# Team-wide rule (team-lead 2026-09-24, README 5.2 #24): the artefact carries its own fingerprint.
# Never paste a fingerprint by hand -- it is a workspace snapshot and goes stale in minutes (u3bverify
# measured 8/8 line numbers mismatching 2.5 min after a hand-pasted fingerprint, which produced FALSE
# REDs for the reviewer). Reports that cite line numbers must say: authoritative = the RUNNER-FINGERPRINT
# line inside THIS trace, and any line number must be re-derived from that exact revision.
$selfBytes = [IO.File]::ReadAllBytes($PSCommandPath)
$selfSha = [Security.Cryptography.SHA256]::Create()
$selfHash = (($selfSha.ComputeHash($selfBytes) | ForEach-Object { $_.ToString('x2') }) -join '')
$selfLines = @([IO.File]::ReadAllLines($PSCommandPath)).Count
$selfMtime = (Get-Item $PSCommandPath).LastWriteTime.ToString('s')
Say ('RUNNER-FINGERPRINT sha256_16=' + $selfHash.Substring(0,16) + ' bytes=' + $selfBytes.Length + ' lines=' + $selfLines + ' mtime=' + $selfMtime + ' (authoritative for THIS trace; verified by re-computing)')

Say 'SELF-CHECK'
    # Self-Check's Say lines also flow into its return pipeline (PowerShell returns ALL output), so the
    # fail count must be picked out explicitly -- otherwise fails= prints an array and exit is always 4.
    $selfOut = @(Self-Check -Sandbox)
    $selfFails = 0
    foreach ($o in $selfOut) { if ($o -is [int]) { $selfFails = $o } }
if ($SelfTest -or $SelfCheckOnly) {
    Say ('SELF-TEST-RESULT fails=' + $selfFails + ' (no lock taken, editor untouched)')
    Say 'SELFCHECK-ONLY-END'   # team-standard marker: locks before/after untouched
    Say 'END'
    exit $(if ($selfFails -eq 0) { 0 } else { 4 })
}
if ($selfFails -ne 0) {
    Say ('SELF-CHECK-FAILED fails=' + $selfFails + ' -> ABORT before touching any lock/editor')
    Say 'END'
    exit 4
}


if (Test-Path $done) { Remove-Item $done -Force }

# ---- play lock (team-wide protocol v2.3) ----------------------------------------
$t0 = Get-Date
$got = $false
while (-not $got -and ((Get-Date) - $t0).TotalSeconds -lt $LockWaitSeconds) {
    # v2.4 rule 4 + team-lead placement rule 2 (2026-09-24): this readiness gate lives INSIDE the retry
    # loop on purpose -- a `continue` outside a loop silently ends the script with exit 0 (measured by
    # classcols: no LOCK-TAKEN, no ledger, no Play, no error). YIELD needs a loop to continue in.
    Say 'EDITOR-STATUS-FIRST'
    $pm = Editor-PlayMode
    $gate = Gate-Decision $pm
    if ($gate -eq 'RETRY-BRIDGE') {
        # A dead bridge is NOT "unknown": with no editor there cannot be anybody in Play. But the editor
        # may still be running while the bridge is unreachable (measured 13:0x) => bounded retry 3x5s,
        # and take ANY readable verdict that appears (playing => ABORT below).
        for ($k = 1; $k -le 3; $k++) {
            Start-Sleep -Seconds 5
            $pmr = Editor-PlayMode
            Say ('LOCK-GATE-BRIDGE-DOWN probe=' + $k + ' result=' + $pmr)
            if ($pmr -ne 'bridge-down') { $pm = $pmr; $gate = Gate-Decision $pm; break }
        }
        if ($gate -eq 'RETRY-BRIDGE') { Say 'LOCK-GATE-BRIDGE-DOWN probed=3 (no editor process; cold start is out of scope) -> proceeding' }
    }
    if ($gate -eq 'ABORT') {
        Say 'ABORT editor-is-playing (lock not taken, editor_stop not sent, ledger not written) -> v2.4 rule 1'
        Say 'END'
        exit 5
    }
    if ($gate -eq 'YIELD') {
        # FAIL-CLOSED: unreadable != stopped. Wait, then retry INSIDE this bounded loop (never the editor).
        Say 'LOCK-GATE-UNKNOWN playMode unreadable -> yield (unknown != stopped)'
        Start-Sleep -Seconds 30
        continue
    }
    Say ('EDITOR-STATUS-FIRST playMode=' + $pm + ' gate=' + $gate)
    $blocked = $null
    foreach ($f in @($lock, $legacyLock)) {
        if (-not (Test-Path $f)) { continue }
        if (My-Lock) { continue }
        $s = Get-LockShot $f
        $age = ((Get-Date) - (Get-Item $f).LastWriteTime).TotalMinutes
        $pidKnown = ($s.pid -gt 0)
        $alive = $false
        if ($pidKnown -and (Get-Process -Id $s.pid -ErrorAction SilentlyContinue)) { $alive = $true }
        $dec = Lock-Decision $age $pidKnown $alive
        $who = (Split-Path $f -Leaf) + ' owner=' + $s.owner + ' pid=' + $s.pid + ' age=' + [math]::Round($age, 1) + 'm pidKnown=' + $(if ($pidKnown) { 1 } else { 0 }) + ' alive=' + $(if ($alive) { 1 } else { 0 })
        if ($dec -eq 'YIELD') {
            $blocked = $f
            Say ('LOCK-BUSY ' + $who + ' -> yield, sleep 30')
            break
        }
        Say ('LOCK-STALE ' + $who + ' -> takeover allowed (age>=12min or owner process gone)')
        if ($f -eq $legacyLock) {
            Remove-Item $f -Force -ErrorAction SilentlyContinue
            Say ('LEGACY-LOCK-STALE-DELETED ' + (Split-Path $f -Leaf) + ' owner=' + $s.owner + ' (registered)')
        }
    }
    if ($null -eq $blocked) {
        if ((Take-Lock) -eq $true) { $got = $true; break }
    }
    Start-Sleep -Seconds 30
}
if (-not $got) {
    $so = Get-LockShot $lock
    $oname = '?'
    if ($null -ne $so) { $oname = $so.owner }
    Say ('ABORT lock held by ' + $oname + ' - alive/fresh after the lock wait -> editor untouched (fail fast)')
    exit 2
}

Add-Content -Path $playLog -Value ((Get-Date).ToString('yyyy-MM-dd HH:mm') + "`tu52resist`tu52-resist-wrap`t" + $Why) -Encoding UTF8
Say ('PLAYLOG-APPENDED ' + $playLog)

try {
    Say 'STOP'
    Stop-Editor-Safe 'v2-gated'

    # team-lead 2026-09-24 (taker obligation, #42): whoever holds the lock must see the editor really
    # stopped before starting its own session -- a takeover of a stale lock may inherit an orphan play.
    # Bounded; if it stays playing we HOLD the lock and abort (never start on top of somebodys play).
    $stoppedNow = $false
    for ($i = 1; $i -le 15; $i++) {
        Start-Sleep -Seconds 2
        if ((Editor-PlayMode) -eq 'stopped') { $stoppedNow = $true; break }
    }
    if (-not $stoppedNow) {
        Stop-Editor-Retry 8
        if ((Editor-PlayMode) -eq 'stopped') { $stoppedNow = $true }
    }
    if (-not $stoppedNow) {
        Say ('ORPHAN-PLAY owner=(from lock) playMode=' + (Editor-PlayMode) + ' -> I will NOT start a session on top of it; cleanupOwner=main agent')
        Say 'LOCK-HELD-ON-PURPOSE not releasing; taker must editor_stop and confirm playMode=stopped (README 5.2 #42)'
        Say 'NOTIFY-MAIN-AGENT ORPHAN-PLAY + LOCK-HELD-ON-PURPOSE'
        Say 'END'
        exit 6
    }
    Say 'PRETAKEN-EDITOR-STATE playMode=stopped (confirmed before RECOMPILE)'
    Start-Sleep -Seconds 2

    Say 'RECOMPILE'
    UC @('recompile') | Out-Null
    for ($i = 0; $i -lt 40; $i++) {
        Start-Sleep -Seconds 2
        $st = UC @('recompile_status')
        if ($st -match 'up_to_date|completed|idle') { Say ('RECOMPILE ' + ($st -replace "`r?`n", ' ')); break }
    }
    Start-Sleep -Seconds 2

    UC @('clear_console') | Out-Null

    # editor_play: the recompile above re-creates the pipeline server (new port), so the first
    # call can answer 401 "Missing or invalid authentication token" against the stale target.
    Say 'PLAY'
    $playing = $false
    for ($i = 1; $i -le 6; $i++) {
        $play = UC @('editor_play')
        $pl = ($play -replace "`r?`n", ' ')
        Say ('PLAY-RESULT try=' + $i + ' ' + $pl.Substring(0, [Math]::Min(260, $pl.Length)))
        Start-Sleep -Seconds 4
        $stt = UC @('editor_status')
        if ($stt -match '"playMode":\s*"playing"') { $playing = $true; Say ('PLAYMODE playing try=' + $i); break }
        Say ('PLAYMODE not-playing try=' + $i)
    }
    if (-not $playing) {
        Say 'PLAY-FAILED the editor never reported playMode=playing -> releasing the lock, no session'
        Stop-Editor-Safe 'v2-gated'
        Release-Lock
        Say 'END'
        exit 3
    }
    Start-Sleep -Seconds 6

    Say 'RUNBG'
    for ($i = 1; $i -le 4; $i++) {
        $rb = UC @('eval_file', '--file', $runbg)
        Say ('RUNBG-RESULT try=' + $i + ' ' + ($rb -replace "`r?`n", ' ').Substring(0, [Math]::Min(200, ($rb -replace "`r?`n", ' ').Length)))
        if ($rb -like '*"success": true*') { break }
        Start-Sleep -Seconds 6
    }
    Start-Sleep -Seconds 2

    # WARM-UP: compiling this file for the first time makes Unity reload the domain, which restarts
    # the game and destroys any driver object created before the reload. Call the no-op entry first,
    # let the reload settle, then install the real driver.
    Say 'WARMUP'
    $w = UC @('run_script', '--file', $cs, '--entry', 'Diablo2.Probes.D2U3C.Charstat.Ping')
    $wl = ($w -replace "`r?`n", ' ')
    Say ('WARMUP-RESULT ' + $wl.Substring(0, [Math]::Min(1500, $wl.Length)))
    Start-Sleep -Seconds 18
    $stt2 = UC @('editor_status')
    # NOTE (team-lead 2026-09-24, adopted from jitter): `compiling=true` in the status dump below is
    # TRANSIENT-NORMAL and is NOT a gate -- shopart 13:22 ran a fully normal session with
    # compiling=True / compilationFailed=False. Never promote it to a stop condition (would mis-abort).
    Say ('POST-WARMUP ' + (($stt2 -replace "`r?`n", ' ')))
    for ($i = 1; $i -le 3; $i++) {
        $rb2 = UC @('eval_file', '--file', $runbg)
        if ($rb2 -like '*"success": true*') { Say ('RUNBG2-OK try=' + $i); break }
        $rl2 = ($rb2 -replace "`r?`n", ' ')
        Say ('RUNBG2 try=' + $i + ' ' + $rl2.Substring(0, [Math]::Min(160, $rl2.Length)))
        Start-Sleep -Seconds 6
    }
    Start-Sleep -Seconds 2

    Say ('RUN_SCRIPT spec=' + $spec)
    $json = '[\"' + $spec + '\"]'
    $r = UC @('run_script', '--file', $cs, '--entry', 'Diablo2.Probes.D2U3C.Charstat.Install', '--args', $json)
    $rl = ($r -replace "`r?`n", ' ')
    Say ('RUN_SCRIPT-RESULT ' + $rl.Substring(0, [Math]::Min(4000, $rl.Length)))
} catch {
    Say ('ERROR ' + $_.Exception.Message)
}

$waited = 0
while (-not (Test-Path $done) -and $waited -lt $WaitSeconds) {
    Start-Sleep -Seconds 3
    $waited += 3
}
if (Test-Path $done) { Say ('DONE ' + (Get-Content $done -Raw).Trim()) } else { Say ('DONE-MISSING after ' + $waited + 's') }

# ---- the Unity console window (Debug.Log): read it with `unity command console` ----------
$cons = UC @('console', '--tail', '500')
$keep = @($cons -split "`r?`n" | Where-Object { $_ -match '\[D2U3C\]' })
if ($keep.Count -gt 0) {
    [System.IO.File]::WriteAllLines($evFile, [string[]]$keep, (New-Object System.Text.UTF8Encoding($false)))
    Say ('EVIDENCE ' + $evFile + ' lines=' + $keep.Count)
    foreach ($l in $keep) { Say ('EV ' + $l.Trim()) }
} else {
    Say 'EVIDENCE-EMPTY no [D2U3C] line in the console tail'
    $errs = @($cons -split "`r?`n" | Where-Object { $_ -match 'error CS|Exception|NullReference' } | Select-Object -First 12)
    foreach ($e in $errs) { Say ('CONSOLE-ERR ' + $e.Trim()) }
}

Say 'STOP-AGAIN'
Stop-Editor-Safe 'v2-gated'
# v2.4 rule 1: `editor_stop` returns before the editor really leaves Play (the backup scene restore
# takes a moment) => releasing the lock immediately opens a "no lock but still playing" window.
# Poll, bounded 15 x 2s, for playMode=stopped; if it stays playing the lock is HELD on purpose
# (LOCK-HELD-ON-PURPOSE -- #42 reversal: never hand over an unowned orphan play); bounded, never hangs.
$leftPlay = $false
for ($i = 1; $i -le 15; $i++) {
    Start-Sleep -Seconds 2
    $pm2 = Editor-PlayMode
    if ($pm2 -eq 'stopped') { $leftPlay = $true; Say ('RELEASE-GATE playMode=stopped after ' + ($i * 2) + 's'); break }
    Say ('RELEASE-GATE still playMode=' + $pm2 + ' try=' + $i)
}
if (-not $leftPlay) {
    # #42 rule 2.1: retry the (idempotent) editor_stop before deciding anything.
    Stop-Editor-Retry 8
    if ((Editor-PlayMode) -eq 'stopped') { $leftPlay = $true; Say 'RELEASE-GATE stopped after editor_stop retries' }
}
if ($leftPlay) {
    Release-Lock
} else {
    # team-lead 2026-09-24 13:5x REVERSAL of the earlier "release anyway" ruling: with a live orphan
    # play, releasing makes the session UNOWNED -- nobody may stop somebody elses session, and the team
    # lead had to bail it out 6.5 minutes later. Holding costs nothing (any correct gate yields while
    # playMode=playing) and keeps the ownership fact. Bounded by the existing 12-minute stale rule.
    $os2 = Get-LockShot $lock
    $opid = -1
    $oown = '(unreadable)'
    if ($null -ne $os2) { $opid = $os2.pid; if ($os2.pid -gt 0) { $oown = $os2.owner } }
    $cleanup = 'main agent'
    if ($oown -ne '(unreadable)') { $cleanup = $oown + ' (last session owner) | main agent' }
    Say ('ORPHAN-PLAY owner=' + $oown + ' pid=' + $opid + ' playMode=' + (Editor-PlayMode) + ' cleanupOwner=' + $cleanup)
    Say 'LOCK-HELD-ON-PURPOSE I am NOT releasing the locks: the orphan play needs an owner; the taker must editor_stop and confirm playMode=stopped BEFORE its own session (README 5.2 #42)'
    Say 'NOTIFY-MAIN-AGENT ORPHAN-PLAY + LOCK-HELD-ON-PURPOSE -- report this line to the team lead, do not stay silent'
}
Say ('SUMMARY tag=' + $Tag + ' done=' + (Test-Path $done) + ' outLog=' + $outLog)
Say 'END'
