# =============================================================================
# selfcheck_write_surface_check.ps1 -- STANDALONE judge: "the write surface reachable
# from the read-only entry (-SelfCheckOnly) must never be a path a NORMAL run writes".
#
# WHY (team rule 2026-09-24, README section 5.2 item 30):
#   A runner's self-check entry used to reuse the SAME log/batch paths as a real run.
#   Team case (`zorder`): `-SelfTest` did WriteAllText($trace) + appended to the SAME name
#   => a real 11:58 batch trace was overwritten and is unrecoverable. Renaming the entry by
#   convention ("-Tag u32sc") is NOT a fix: the day somebody forgets the tag, the last real
#   run's trace is gone. The only fix that cannot be forgotten is a REDIRECT inside the
#   `if ($SelfCheckOnly) { ... }` branch itself -- then a normal run cannot reach that name.
#
# FIRST INSTANCE (this script generalises it): sheet `u52play` 2026-09-24 14:14:51,
#   tools/probes/drivers/d2u3_charstat_run.ps1 line 45:
#     if ($SelfCheckOnly) { $outLog = Join-Path $test ('d2u3_charstat_selfcheck_' + $Tag + '.txt') }
#   `Say` (the file's only writer, `[System.IO.File]::AppendAllText($outLog, ...)`) is CALLED
#   from the read-only branch => the whole reachable write surface collapses onto that one
#   redirected name.
#
# CRITERION (AST, call form -- never a bare word; rule 5.2 item 45):
#   1. every write call REACHABLE from the read-only branch (writes written directly in the
#      branch PLUS writes inside the functions it calls, transitively) must have a target that
#      is (a) assigned inside a positive `if ($SelfCheckOnly)` branch, or (b) itself carrying
#      the redirect token in its literal;
#   2. the redirect leaf must carry the token => default `-TokenPattern '(?i)(selfcheck|selftest)'`
#      (WIDENING, DELIBERATE + REPORTED: the active runner `d2u32_run.ps1` names its redirected
#      files `u32_selftest_steps_*.txt` -- same intent as `_selfcheck`, and a strictly
#      `_selfcheck`-only matcher would FALSE-RED an active runner; a false red's real cost is
#      that it pushes people to delete the assertion);
#   3. the read-only branch must NOT write/take the real Play lock and MUST contain an `exit`
#      (this is the REACHABILITY half of "selfcheckExit < takeLock": the read-only entry must be
#      able to finish without the lock. The pure LINE-ORDER half lives in
#      tools/probes/measure/c5_order_check.ps1 -- deliberately NOT duplicated here: a line-order
#      classifier was measured UNSOUND in this repo, see runner_gate_check.ps1 head, item C5).
#
# SCOPE: read-only. Takes no lock, writes nothing but stdout. Never edits a runner.
#
# Usage:
#   powershell -NoProfile -File tools/probes/measure/selfcheck_write_surface_check.ps1 -SelfTest
#   powershell -NoProfile -File tools/probes/measure/selfcheck_write_surface_check.ps1
#   powershell -NoProfile -File tools/probes/measure/selfcheck_write_surface_check.ps1 `
#       -Files tools/probes/drivers/d2u3_charstat_run.ps1 -ShowPass
#
# Exit: 0 = no FAIL (SKIP/no-param rows are NOT failures) | 1 = at least one FAIL
#       | 2 = this file is non-ASCII without a BOM (PS 5.1 would read it as ANSI)
#       | 3 = a fixture expectation broke (the judge cannot be trusted)
# ASCII only on purpose (a .ps1 must be pure ASCII or UTF-8 WITH BOM).
# =============================================================================
param(
    [string]$Root = 'c:/Work/Server/f-v2/clover-project-diablo2',
    [string]$Glob = 'tools/probes/drivers/*_run.ps1',
    [string[]]$Files = @(),
    [string]$TokenPattern = '(?i)(selfcheck|selftest)',
    [switch]$SelfTest,
    [switch]$ShowPass
)

$ErrorActionPreference = 'Continue'

# ---- byte-level encoding guard (same family as c5_order_check.ps1) ------------
$selfPath = $PSCommandPath
if ([string]::IsNullOrEmpty($selfPath)) { $selfPath = $MyInvocation.MyCommand.Path }
if (-not [string]::IsNullOrEmpty($selfPath)) {
    $sb = [System.IO.File]::ReadAllBytes($selfPath)
    $na = 0
    foreach ($z in $sb) { if ($z -gt 127) { $na = $na + 1 } }
    $bom = ($sb.Length -ge 3 -and $sb[0] -eq 0xEF -and $sb[1] -eq 0xBB -and $sb[2] -eq 0xBF)
    Write-Host ('SWS-SELF-ENCODING file=' + (Split-Path $selfPath -Leaf) + ' nonAsciiBytes=' + $na + ' hasBom=' + $bom)
    if (($na -gt 0) -and (-not $bom)) {
        Write-Host 'SWS-SELF-ENCODING FAIL: non-ASCII without BOM -> PS 5.1 would read this as ANSI and it would not load'
        exit 2
    }
}

# ---- self version line (team rule README section 67, 2026-09-24) --------------------
# Every reading produced by this judge must be quotable TOGETHER WITH the judge's own
# version, and "stable" must be MECHANICAL: read the file twice and compare bytes
# (2026-09-24 measured: one sibling tool had >=4 observable versions inside 8 minutes).
# A caller must also double-check `RESULT <OK|FAIL>` AND the exit code: a torn/partial
# load can produce NO verdict line while the exit code still reads 0 (= green with nothing judged).
$verPath = $PSCommandPath
if ([string]::IsNullOrEmpty($verPath)) { $verPath = $MyInvocation.MyCommand.Path }
if (-not [string]::IsNullOrEmpty($verPath)) {
    try {
        $vb1 = [System.IO.File]::ReadAllBytes($verPath)
        $vb2 = [System.IO.File]::ReadAllBytes($verPath)
        $vh = [System.Security.Cryptography.SHA256]::Create().ComputeHash($vb1)
        $vhex = (($vh[0..7] | ForEach-Object { $_.ToString('x2') }) -join '')
        $vline = ([System.IO.File]::ReadAllLines($verPath)).Count
        $vmt = (Get-Item -LiteralPath $verPath).LastWriteTime.ToString('yyyy-MM-ddTHH:mm:ss')
        $vsame = ($vb1.Length -eq $vb2.Length)
        if ($vsame) {
            for ($vi = 0; $vi -lt $vb1.Length; $vi++) { if ($vb1[$vi] -ne $vb2[$vi]) { $vsame = $false; break } }
        }
        Write-Host ('SWS-VERSION file=' + (Split-Path $verPath -Leaf) + ' sha256_16=' + $vhex + ' bytes=' + $vb1.Length + ' lines=' + $vline + ' mtime=' + $vmt + ' stable=' + $vsame + ' (stable = two consecutive byte reads identical)')
    } catch {
        Write-Host ('SWS-VERSION-FAIL cannot fingerprint myself: ' + $_.Exception.Message)
    }
}

$script:writeCmdlets = @(
    'set-content', 'add-content', 'clear-content', 'out-file', 'new-item', 'set-item',
    'remove-item', 'move-item', 'copy-item', 'export-csv', 'export-clixml', 'tee-object'
)

function Get-Parse([string]$text) {
    $tok = $null; $err = $null
    $a = [System.Management.Automation.Language.Parser]::ParseInput($text, [ref]$tok, [ref]$err)
    if ($err -and @($err).Count -gt 0) { throw ('parse errors = ' + @($err).Count) }
    return $a
}

function Get-OwnerFunctionName([object]$node) {
    $p = $node.Parent
    while ($null -ne $p) {
        if ($p -is [System.Management.Automation.Language.FunctionDefinitionAst]) { return [string]$p.Name }
        $p = $p.Parent
    }
    return ''
}

function Get-CmdletTargetText([object]$cmd) {
    $els = @($cmd.CommandElements)
    $positional = ''
    $i = 1
    while ($i -lt $els.Count) {
        $e = $els[$i]
        if ($e -is [System.Management.Automation.Language.CommandParameterAst]) {
            $pn = [string]$e.ParameterName
            if ($pn -match '(?i)^(Path|LiteralPath|FilePath|OutFile|Destination)$') {
                if (($i + 1) -lt $els.Count) { return [string]$els[$i + 1].Extent.Text }
            }
            # skip this parameter's VALUE only when the next element is not itself a parameter
            # (otherwise a switch such as -Force would eat the following positional argument).
            if (($i + 1) -lt $els.Count -and $els[$i + 1] -isnot [System.Management.Automation.Language.CommandParameterAst]) { $i++ }
            $i++
            continue
        }
        if ($positional -eq '') { $positional = [string]$e.Extent.Text }
        $i++
    }
    return $positional
}

# a write whose target is a LOOP variable cannot be judged from the write site alone either: the
# value set comes from the collection being iterated. MEASURED (d2u26_run.ps1 L288/L317:
# `foreach ($f in @($mainFile, $legacyFile)) { ... Remove-Item $f ... }`) -- reported as [INFO].
function Test-TargetIsLoopVariable([object]$node, [string]$varName) {
    $p = $node.Parent
    while ($null -ne $p) {
        if ($p -is [System.Management.Automation.Language.ForEachStatementAst]) {
            if ($null -ne $p.Variable -and $p.Variable.VariablePath.UserPath -eq $varName) { return $true }
        }
        $p = $p.Parent
    }
    return $false
}

# a write whose target is a FUNCTION PARAMETER cannot be judged from the write site alone:
# the value arrives from the call site. Reported as [INFO], never as a red (fail-loud would be a
# false red; mapping call sites is a different, deeper judge).
function Test-TargetIsFunctionParameter([object]$node, [string]$varName) {
    $p = $node.Parent
    while ($null -ne $p) {
        if ($p -is [System.Management.Automation.Language.FunctionDefinitionAst]) {
            foreach ($pp in @($p.Parameters)) {
                if ($pp.Name.VariablePath.UserPath -eq $varName) { return [string]$p.Name }
            }
            return ''
        }
        $p = $p.Parent
    }
    return ''
}

# ---- the judge -----------------------------------------------------------------
# Returns a plain object: Verdict / Reasons / Direct / Indirect / Redirects / Violations
function Test-JudgeSource([string]$text, [string]$tokenPattern) {
    $ast = Get-Parse $text
    $out = [pscustomobject]@{
        Verdict = 'PASS'; Reasons = @(); Redirects = @(); SafeRoots = @(); ParamTargets = @()
        Direct = @(); Indirect = @(); Violations = @()
    }

    $hasParam = $false
    if ($null -ne $ast.ParamBlock) {
        foreach ($p in @($ast.ParamBlock.Parameters)) {
            if ($p.Name.VariablePath.UserPath -eq 'SelfCheckOnly') { $hasParam = $true }
        }
    }
    if (-not $hasParam) {
        $out.Verdict = 'SKIP'
        $out.Reasons += 'no -SelfCheckOnly parameter => not a read-only-entry runner (not a failure)'
        return $out
    }

    # ---- read-only branch blocks: FIXPOINT over the real shapes seen in this repo --------
    # MEASURED 2026-09-24 (this is the "narrow matcher -> false RED" family, README 5.2 #45):
    # a first version accepted ONLY `if ($SelfCheckOnly) { ... }` and reported 4 of the 8 ACTIVE
    # runners as FAIL. All four were FALSE REDS; the real shapes are
    #   (a) a COMBINED condition  -- u52resist_run.ps1 L71/L533 `if ($SelfTest -or $SelfCheckOnly)`,
    #       u53close_run.ps1 L43/L254 same                     (2 runners)
    #   (b) an ALIAS flag         -- d2u26_run.ps1 L29 `if ($SelfCheckOnly -eq $true) { $SelfTest =
    #       $true; $scOnly = $true }` (the real branch is `if ($scOnly)`);
    #       shopgrid_run.ps1 L28 `if ($SelfCheckOnly) { $LockSelfTest = $true }`         (2 runners)
    # So: seed the flag set with SelfCheckOnly, then close over
    #   * a clause whose condition POSITIVELY references a known flag (`-or`/`-and`/`-eq $true`),
    #     skipping any negation (`!$X`, `-not $X`, `-eq $false`, `-ne $true`), and
    #   * an assignment INSIDE such a block that turns ANOTHER variable into a flag
    #     (`$X = $true` or `$X = <expr mentioning a flag>`).
    # Cost of the narrow version (why widening instead of "fix the runners"): a false red's real
    # cost is that it pushes people to DELETE the assertion -- the repo has measured this 4x today.
    $ifs = @($ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.IfStatementAst] }, $true))
    $flags = New-Object System.Collections.Generic.HashSet[string]
    [void]$flags.Add('SelfCheckOnly')
    $blocks = @()
    $blockKeys = @{}
    $changed = $true
    $guard = 0
    while ($changed -and $guard -lt 16) {
        $guard++
        $changed = $false
        foreach ($ifa in $ifs) {
            foreach ($cl in @($ifa.Clauses)) {
                $cond = ([string]$cl.Item1.Extent.Text).Trim()
                $hit = ''
                foreach ($f in @($flags)) {
                    $fe = [regex]::Escape($f)
                    if ($cond -match ('(?i)(^|[^\w$])!\s*\$' + $fe + '\b')) { continue }
                    if ($cond -match ('(?i)-not\s+\$' + $fe + '\b')) { continue }
                    if ($cond -match ('(?i)\$' + $fe + '\s+-(eq|ne)\s+\$(false|true)')) {
                        $op = $Matches[1]; $lit = $Matches[2]
                        if (($op -eq 'eq' -and $lit -eq 'false') -or ($op -eq 'ne' -and $lit -eq 'true')) { continue }
                    }
                    if ($cond -match ('(?i)\$' + $fe + '\b')) { $hit = $f; break }
                }
                if ($hit -eq '') { continue }
                $b = $cl.Item2
                $key = ('' + $b.Extent.StartLineNumber + ':' + $b.Extent.EndLineNumber + ':' + $b.Extent.StartColumnNumber)
                if (-not $blockKeys.ContainsKey($key)) { $blockKeys[$key] = $true; $blocks += $b }
                # alias flags declared inside the branch
                foreach ($a in @($b.FindAll({ param($n) $n -is [System.Management.Automation.Language.AssignmentStatementAst] }, $true))) {
                    if ($a.Left -isnot [System.Management.Automation.Language.VariableExpressionAst]) { continue }
                    $vn = [string]$a.Left.VariablePath.UserPath
                    if ($flags.Contains($vn)) { continue }
                    $rhs = ([string]$a.Right.Extent.Text).Trim()
                    $alias = ($rhs -match '^\$true$')
                    if (-not $alias) {
                        foreach ($f2 in @($flags)) {
                            if ($rhs -match ('(?i)\$' + [regex]::Escape($f2) + '\b')) { $alias = $true; break }
                        }
                    }
                    if ($alias) { [void]$flags.Add($vn); $changed = $true }
                }
            }
        }
    }
    if ($blocks.Count -eq 0) {
        $out.Verdict = 'FAIL'
        $out.Reasons += 'the parameter is declared but no positive read-only branch (`if ($SelfCheckOnly)`, combined `-or`, or alias flag) exists'
        $out.Violations += 'no-readonly-branch'
        return $out
    }
    $out.Reasons += ('flags=' + ((@($flags) | Sort-Object) -join ','))

    # redirect map: variables assigned INSIDE a read-only branch to a token-bearing value
    $redir = @{}
    foreach ($b in $blocks) {
        foreach ($a in @($b.FindAll({ param($n) $n -is [System.Management.Automation.Language.AssignmentStatementAst] }, $true))) {
            if ($a.Left -is [System.Management.Automation.Language.VariableExpressionAst]) {
                $vn = [string]$a.Left.VariablePath.UserPath
                if (([string]$a.Right.Extent.Text) -match $tokenPattern) { $redir[$vn] = [string]$a.Right.Extent.Text }
            }
        }
    }
    $keys = @($redir.Keys) | Sort-Object
    foreach ($k in $keys) { $out.Redirects += ('$' + $k + ' = ' + $redir[$k]) }

    # SAFE ROOTS, transitive: a write target may be a VARIABLE that is only *derived* from a
    # token-bearing root -- the sandbox idiom, measured on the active runner d2u26_run.ps1:
    #   $sb   = Join-Path $root ('.ai-tmp/test/u26-lockselftest-' + $PID)     <- token
    #   $fake = Join-Path $sb 'fake.txt'                                      <- derived, also safe
    # Judging `$fake` as "the normal-run path" was a FALSE RED once already (24 reported violations
    # on d2u26, all of them its per-PID self-check fixtures). Closure is over assignments INSIDE a
    # read-only branch only -- a script-scope normal-run root never becomes safe this way.
    $safe = @{}
    $branchAssigns = @()
    foreach ($b in $blocks) {
        foreach ($a in @($b.FindAll({ param($n) $n -is [System.Management.Automation.Language.AssignmentStatementAst] }, $true))) {
            if ($a.Left -isnot [System.Management.Automation.Language.VariableExpressionAst]) { continue }
            $branchAssigns += $a
            $vn = [string]$a.Left.VariablePath.UserPath
            if (([string]$a.Right.Extent.Text) -match $tokenPattern) { $safe[$vn] = 'token-in-rhs' }
        }
    }
    $ch2 = $true
    $g2 = 0
    while ($ch2 -and $g2 -lt 16) {
        $g2++
        $ch2 = $false
        foreach ($a in $branchAssigns) {
            $vn = [string]$a.Left.VariablePath.UserPath
            if ($safe.ContainsKey($vn)) { continue }
            $rhs = [string]$a.Right.Extent.Text
            foreach ($sv in @($safe.Keys)) {
                if ($rhs -match ('\$' + [regex]::Escape($sv) + '\b')) { $safe[$vn] = ('via $' + $sv); $ch2 = $true; break }
            }
        }
    }
    $out.SafeRoots = @($safe.Keys) | Sort-Object | ForEach-Object { ('$' + $_ + ' (' + $safe[$_] + ')') }

    # call closure: functions reachable from the read-only branch
    $fnMap = @{}
    foreach ($fd in @($ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.FunctionDefinitionAst] }, $true))) {
        if (-not $fnMap.ContainsKey([string]$fd.Name)) { $fnMap[[string]$fd.Name] = $fd }
    }
    $reach = New-Object System.Collections.Generic.HashSet[string]
    $queue = New-Object System.Collections.Queue
    foreach ($b in $blocks) {
        foreach ($c in @($b.FindAll({ param($n) $n -is [System.Management.Automation.Language.CommandAst] }, $true))) {
            $nm = [string]$c.GetCommandName()
            if ($nm -and $fnMap.ContainsKey($nm)) { [void]$queue.Enqueue($nm) }
        }
    }
    while ($queue.Count -gt 0) {
        $nm = [string]$queue.Dequeue()
        if (-not $reach.Add($nm)) { continue }
        foreach ($c in @($fnMap[$nm].FindAll({ param($n) $n -is [System.Management.Automation.Language.CommandAst] }, $true))) {
            $n2 = [string]$c.GetCommandName()
            if ($n2 -and $fnMap.ContainsKey($n2)) { [void]$queue.Enqueue($n2) }
        }
    }

    # write census (whole file; each row keeps its owner function + line)
    $writes = @()
    foreach ($c in @($ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.CommandAst] }, $true))) {
        $nm = [string]$c.GetCommandName()
        if (-not $nm) { continue }
        if ($script:writeCmdlets -contains $nm.ToLower()) {
            $writes += [pscustomobject]@{
                Kind = 'cmdlet'; Name = $nm; Line = $c.Extent.StartLineNumber
                Owner = (Get-OwnerFunctionName $c); Target = (Get-CmdletTargetText $c); Node = $c
            }
        }
    }
    foreach ($m in @($ast.FindAll({
                    param($n)
                    $n -is [System.Management.Automation.Language.InvokeMemberExpressionAst] -and $n.Static -eq $true
                }, $true))) {
        # `[System.IO.File]::AppendAllText(...)` -- the type NAME comes with its brackets, so strip
        # them before matching (measured: keeping them made this matcher blind to the ONLY writer of
        # the first-instance runner, and FX-PASS then passed VACUOUSLY -- caught by FX-FAIL-no-redirect).
        $typeTxt = ([string]$m.Expression.Extent.Text).Trim()
        $typeTxt = $typeTxt -replace '^\[', '' -replace '\]$', ''
        $mem = ''
        if ($m.Member -is [System.Management.Automation.Language.StringConstantExpressionAst]) { $mem = [string]$m.Member.Value }
        if (($typeTxt -match '^System\.IO\.(File|Directory|FileInfo|DirectoryInfo)$') -and ($mem -match '(?i)^(Write|Append|Create|Copy|Move|Replace|Delete)')) {
            $tgt = ''
            if (@($m.Arguments).Count -ge 1) { $tgt = [string]$m.Arguments[0].Extent.Text }
            $writes += [pscustomobject]@{
                Kind = 'dotnet'; Name = ($typeTxt + '::' + $mem); Line = $m.Extent.StartLineNumber
                Owner = (Get-OwnerFunctionName $m); Target = $tgt; Node = $m
            }
        }
    }
    foreach ($c in @($ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.CommandAst] }, $true))) {
        if ([string]$c.GetCommandName() -ne 'New-Object') { continue }
        $joined = (([string]$c.Extent.Text) -replace '\s+', ' ')
        if ($joined -notmatch '(?i)StreamWriter') { continue }
        $tgt = ''
        $els = @($c.CommandElements)
        # `New-Object System.IO.StreamWriter <path>`: element 1 IS the type name => the path is 2.
        if ($els.Count -ge 3) { $tgt = [string]$els[2].Extent.Text }
        elseif ($els.Count -ge 2) { $tgt = [string]$els[1].Extent.Text }
        $writes += [pscustomobject]@{
            Kind = 'dotnet'; Name = 'System.IO.StreamWriter (ctor)'; Line = $c.Extent.StartLineNumber
            Owner = (Get-OwnerFunctionName $c); Target = $tgt; Node = $c
        }
    }

    # reachable = written directly in the read-only branch, or inside a function it calls
    $blockRanges = @()
    foreach ($b in $blocks) { $blockRanges += @($b.Extent.StartLineNumber, $b.Extent.EndLineNumber) }
    $reachable = @()
    foreach ($w in $writes) {
        $isDirect = $false
        if ($w.Owner -eq '') {
            for ($i = 0; $i -lt $blockRanges.Count; $i += 2) {
                if ($w.Line -ge $blockRanges[$i] -and $w.Line -le $blockRanges[$i + 1]) { $isDirect = $true; break }
            }
        }
        if ($isDirect) { $out.Direct += $w; $reachable += $w; continue }
        if ($w.Owner -ne '' -and $reach.Contains($w.Owner)) { $out.Indirect += $w; $reachable += $w }
    }

    # rule 1+2 -- every reachable write must land on a redirected / token-bearing name
    foreach ($w in $reachable) {
        $t = [string]$w.Target
        $ok = $false
        $why = ''
        if ($t -match '^\$([A-Za-z_][A-Za-z0-9_]*)$') {
            $vn = $Matches[1]
            if ($safe.ContainsKey($vn)) { $ok = $true; $why = ('safe-root-in-readonly-branch ($' + $vn + ' ' + $safe[$vn] + ')') }
        }
        if (-not $ok -and ($t -match $tokenPattern)) { $ok = $true; $why = 'token-in-target-literal' }
        if (-not $ok -and ($t -match '^\$([A-Za-z_][A-Za-z0-9_]*)$')) {
            # unclassifiable from the write site: the value arrives from the call site / collection
            $fn = Test-TargetIsFunctionParameter $w.Node $Matches[1]
            if ($fn -ne '') {
                $ok = $true
                $out.ParamTargets += ('line ' + $w.Line + ' ' + $w.Name + ' -> $' + $Matches[1] + ' = parameter of ' + $fn + '() -> call-site check needed')
            } elseif (Test-TargetIsLoopVariable $w.Node $Matches[1]) {
                $ok = $true
                $out.ParamTargets += ('line ' + $w.Line + ' ' + $w.Name + ' -> $' + $Matches[1] + ' = foreach loop variable -> collection check needed')
            }
        }
        if (-not $ok) {
            if ([string]::IsNullOrEmpty($t)) { $why = 'target-unreadable (no -Path/1st arg) -> needs human' }
            elseif ($t -match '^\$') { $why = ('NOT redirected: ' + $t + ' is the normal-run path') }
            else { $why = ('NOT redirected: literal target ' + $t) }
            $out.Violations += ('line ' + $w.Line + ' ' + $w.Kind + ' ' + $w.Name + ' -> ' + $t + ' | ' + $why)
        }
    }

    # rule 3a -- the read-only branch must not write or take the real Play lock
    foreach ($w in ($out.Direct + $out.Indirect)) {
        if ([string]$w.Target -match '\$(lock|legacyLock)\b') {
            $out.Violations += ('line ' + $w.Line + ' writes the PLAY LOCK from the read-only branch (' + $w.Target + ')')
        }
    }
    foreach ($b in $blocks) {
        foreach ($c in @($b.FindAll({ param($n) $n -is [System.Management.Automation.Language.CommandAst] }, $true))) {
            if ([string]$c.GetCommandName() -match '(?i)^Take-?Lock$') {
                $out.Violations += ('line ' + $c.Extent.StartLineNumber + ' calls the lock take from the read-only branch')
            }
        }
    }

    # rule 3b -- the read-only branch must contain an exit (reachability half of selfcheckExit < takeLock)
    $exits = 0
    foreach ($b in $blocks) { $exits += @($b.FindAll({ param($n) $n -is [System.Management.Automation.Language.ExitStatementAst] }, $true)).Count }
    if ($exits -eq 0) { $out.Violations += 'the read-only branch contains no `exit` => it cannot finish without the lock' }

    if (@($out.Violations).Count -gt 0) { $out.Verdict = 'FAIL' } else { $out.Verdict = 'PASS' }
    return $out
}

# ---- fixtures: the judge must be able to go RED ---------------------------------
if ($SelfTest) {
    $fx = @()
    $fx += @{ Name = 'FX-PASS-redirected-via-function'; Expect = 'PASS'; Text = @'
param([switch]$SelfCheckOnly, [string]$Tag = 'x')
$test = 'c:/tmp'
$outLog = Join-Path $test ('x_run_' + $Tag + '.txt')
if ($SelfCheckOnly) { $outLog = Join-Path $test ('x_selfcheck_' + $Tag + '.txt') }
function Say([string]$m) { [System.IO.File]::AppendAllText($outLog, $m + "`n") }
if ($SelfCheckOnly) {
    Say 'SELFCHECK-ONLY-BEGIN'
    Say 'SELFCHECK-ONLY-END'
    exit 0
}
'@ }
    $fx += @{ Name = 'FX-FAIL-no-redirect'; Expect = 'FAIL'; Text = @'
param([switch]$SelfCheckOnly, [string]$Tag = 'x')
$test = 'c:/tmp'
$outLog = Join-Path $test ('x_run_' + $Tag + '.txt')
function Say([string]$m) { [System.IO.File]::AppendAllText($outLog, $m + "`n") }
if ($SelfCheckOnly) {
    Say 'SELFCHECK-ONLY-BEGIN'
    exit 0
}
'@ }
    $fx += @{ Name = 'FX-FAIL-direct-write-in-branch'; Expect = 'FAIL'; Text = @'
param([switch]$SelfCheckOnly)
$test = 'c:/tmp'
$evidence = Join-Path $test 'x_evidence.txt'
if ($SelfCheckOnly) {
    Set-Content -Path $evidence -Value 'self check' -Encoding UTF8
    exit 0
}
'@ }
    $fx += @{ Name = 'FX-FAIL-lock-written-from-branch'; Expect = 'FAIL'; Text = @'
param([switch]$SelfCheckOnly)
$lock = 'c:/tmp/play-running.lock'
if ($SelfCheckOnly) {
    Set-Content -Path ('c:/tmp/x_selfcheck_steps.txt') -Value 'ok'
    Set-Content -Path $lock -Value 'me' -Encoding ASCII
    exit 0
}
'@ }
    $fx += @{ Name = 'FX-FAIL-no-exit-in-branch'; Expect = 'FAIL'; Text = @'
param([switch]$SelfCheckOnly)
$log = 'c:/tmp/x_run.txt'
function Say([string]$m) { [System.IO.File]::AppendAllText($log, $m) }
if ($SelfCheckOnly) {
    $log = 'c:/tmp/x_selfcheck_run.txt'
    Say 'SELFCHECK-ONLY-BEGIN'
}
'@ }
    $fx += @{ Name = 'FX-SKIP-not-a-readonly-runner'; Expect = 'SKIP'; Text = @'
param([string]$Tag = 'x')
$log = 'c:/tmp/x_run.txt'
function Say([string]$m) { [System.IO.File]::AppendAllText($log, $m) }
Say 'hi'
'@ }
    # the two REAL shapes (measured false reds of the first, narrow matcher)
    $fx += @{ Name = 'FX-PASS-combined-or-condition'; Expect = 'PASS'; Text = @'
param([switch]$SelfTest, [switch]$SelfCheckOnly, [string]$Tag = 'x')
$test = 'c:/tmp'
$outLog = Join-Path $test ('x_run_' + $Tag + '.txt')
if ($SelfTest -or $SelfCheckOnly) { $outLog = Join-Path $test ('u52resist_selftest_' + $Tag + '.txt') }
function Say([string]$m) { [System.IO.File]::AppendAllText($outLog, $m) }
if ($SelfTest -or $SelfCheckOnly) {
    Say 'SELFCHECK-ONLY-END'
    exit 0
}
'@ }
    $fx += @{ Name = 'FX-FAIL-combined-or-without-redirect'; Expect = 'FAIL'; Text = @'
param([switch]$SelfTest, [switch]$SelfCheckOnly, [string]$Tag = 'x')
$test = 'c:/tmp'
$outLog = Join-Path $test ('x_run_' + $Tag + '.txt')
function Say([string]$m) { [System.IO.File]::AppendAllText($outLog, $m) }
if ($SelfTest -or $SelfCheckOnly) {
    Say 'SELFCHECK-ONLY-END'
    exit 0
}
'@ }
    $fx += @{ Name = 'FX-PASS-loop-variable-target-is-info'; Expect = 'PASS'; Text = @'
param([switch]$SelfCheckOnly)
$test = 'c:/tmp'
$outLog = Join-Path $test 'x_run.txt'
if ($SelfCheckOnly) { $outLog = Join-Path $test 'x_selfcheck.txt' }
function Say([string]$m) { [System.IO.File]::AppendAllText($outLog, $m) }
function Sweep([string[]]$paths) { foreach ($f in $paths) { Remove-Item $f -Force -ErrorAction SilentlyContinue } }
if ($SelfCheckOnly) {
    Sweep @('c:/tmp/a','c:/tmp/b')
    Say 'SELFCHECK-ONLY-END'
    exit 0
}
'@ }
    $fx += @{ Name = 'FX-PASS-alias-flag'; Expect = 'PASS'; Text = @'
param([switch]$SelfCheckOnly, [string]$Tag = 'x')
$test = 'c:/tmp'
$runlog = Join-Path $test 'x_runlog.txt'
if ($SelfCheckOnly) { $LockSelfTest = $true }
if ($LockSelfTest) { $runlog = Join-Path $test 'x_selftest.txt' }
function Say([string]$m) { [System.IO.File]::AppendAllText($runlog, $m) }
if ($LockSelfTest) {
    Say 'SELFCHECK-ONLY-END'
    exit 0
}
'@ }
    $fx += @{ Name = 'FX-FAIL-alias-flag-without-redirect'; Expect = 'FAIL'; Text = @'
param([switch]$SelfCheckOnly, [string]$Tag = 'x')
$test = 'c:/tmp'
$runlog = Join-Path $test 'x_runlog.txt'
if ($SelfCheckOnly) { $LockSelfTest = $true }
function Say([string]$m) { [System.IO.File]::AppendAllText($runlog, $m) }
if ($LockSelfTest) {
    Say 'SELFCHECK-ONLY-END'
    exit 0
}
'@ }

    $bad = 0
    foreach ($f in $fx) {
        $r = Test-JudgeSource $f.Text $TokenPattern
        $good = ($r.Verdict -eq $f.Expect)
        if (-not $good) { $bad++ }
        $vs = @($r.Violations) -join ' ;; '
        Write-Host ('SWS-FIXTURE ' + $f.Name + ' expect=' + $f.Expect + ' got=' + $r.Verdict + ' => ' + $(if ($good) { 'OK' } else { 'BROKEN' }) + $(if ($r.Violations.Count -gt 0) { ' | ' + $vs } else { '' }))
    }
    Write-Host ('SWS-FIXTURE-SUMMARY fixtures=' + $fx.Count + ' broken=' + $bad)
    # terminal verdict line (section 67): a caller must check THIS line AND the exit code.
    Write-Host ('RESULT ' + $(if ($bad -gt 0) { 'FAIL' } else { 'OK' }))
    if ($bad -gt 0) { exit 3 }
    exit 0
}

# ---- the real run ---------------------------------------------------------------
# team rule 2026-09-24 (shopart's trap): .NET IO resolves against the PROCESS CWD, not the PS
# location => a relative -Root/-Files would silently judge a same-named file in another tree and
# still print a green. Refuse instead of guessing (fail-loud).
if (-not [System.IO.Path]::IsPathRooted($Root)) {
    Write-Host ('SWS FAIL -Root must be an ABSOLUTE path (got "' + $Root + '", cwd=' + (Get-Location).ProviderPath + ')')
    exit 2
}

$targets = @()
if (@($Files).Count -gt 0) {
    foreach ($one in @($Files)) {
        if (-not [System.IO.Path]::IsPathRooted($one)) {
            Write-Host ('SWS FAIL -Files must contain ABSOLUTE paths (got "' + $one + '", cwd=' + (Get-Location).ProviderPath + ')')
            exit 2
        }
        $targets += $one
    }
} else {
    $targets = @(Get-ChildItem -LiteralPath (Join-Path $Root ($Glob -replace '/[^/]*$', '')) -File -Filter ($Glob -replace '^.*/', '') -ErrorAction SilentlyContinue |
            Sort-Object Name | ForEach-Object { $_.FullName })
}
if (@($targets).Count -eq 0) { Write-Host ('SWS FAIL no target files (glob=' + $Glob + ')'); exit 1 }

$fails = 0
foreach ($t in $targets) {
    $txt = ''
    try { $txt = [System.IO.File]::ReadAllText($t) } catch { Write-Host ('SWS FAIL unreadable ' + $t + ' :: ' + $_.Exception.Message); $fails++; continue }
    $r = $null
    try { $r = Test-JudgeSource $txt $TokenPattern } catch {
        Write-Host ('SWS FAIL judge-exception ' + (Split-Path $t -Leaf) + ' :: ' + $_.Exception.Message)
        $fails++; continue
    }
    $leaf = Split-Path $t -Leaf
    # team rule 2026-09-24 (shopart's trap): a judge that reads files and then says PASS must print
    # the ABSOLUTE path it actually read + the byte count, otherwise the green cannot be re-checked.
    # (.NET IO does NOT follow Set-Location => a relative path silently reads another tree's file.)
    $bytes = -1
    try { $bytes = (New-Object System.IO.FileInfo($t)).Length } catch { }
    $tag = 'SWS ' + $r.Verdict + ' ' + $leaf + ' file=' + $t + ' bytes=' + $bytes
    if ($r.Verdict -ne 'PASS' -and $r.Verdict -ne 'SKIP') { $fails++ }
    if ($ShowPass -or ($r.Verdict -ne 'PASS' -and $r.Verdict -ne 'SKIP')) {
        Write-Host ($tag + ' direct=' + @($r.Direct).Count + ' indirect=' + @($r.Indirect).Count + ' redirects=' + @($r.Redirects).Count + ' safeRoots=' + @($r.SafeRoots).Count + ' paramTargets=' + @($r.ParamTargets).Count)
        foreach ($rd in @($r.Redirects)) { Write-Host ('      redirect: ' + $rd) }
        foreach ($v in @($r.Violations)) { Write-Host ('      VIOLATION: ' + $v) }
        foreach ($v in @($r.ParamTargets)) { Write-Host ('      INFO-unclassifiable: ' + $v) }
        foreach ($rs in @($r.Reasons)) { Write-Host ('      reason: ' + $rs) }
    } else {
        Write-Host ($tag + ' (direct=' + @($r.Direct).Count + ' indirect=' + @($r.Indirect).Count + ')')
    }
}
Write-Host ('SWS-SUMMARY checked=' + @($targets).Count + ' FAIL=' + $fails)
# terminal verdict line (section 67): a caller must check THIS line AND the exit code.
Write-Host ('RESULT ' + $(if ($fails -gt 0) { 'FAIL' } else { 'OK' }))
if ($fails -gt 0) { exit 1 }
exit 0
