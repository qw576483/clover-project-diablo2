# =============================================================================
# c5_order_check.ps1 -- C5 ORDER judge (team lead 2026-09-24; slice d2u32 / waypoint)
#
# CLAIM UNDER TEST: the -SelfCheckOnly exit must happen BEFORE the real lock is taken.
#   event (A) = the SELFCHECK-ONLY-END emission of the read-only exit
#   event (B) = the FIRST LOCK-TAKEN emission AFTER the real lock write line
#   assert A < B.
# This is a STATIC PROXY and therefore SECONDARY evidence (team lead 2026-09-24). The strong
# evidence is a real run: `-SelfCheckOnly` finishes with the lock state unchanged, while the
# real path does take the lock. See .ai-tmp/test/report-u32.md.
#
# HONEST-JUDGE CONTRACT (team lead 2026-09-24) -- this judge is allowed to say "I don't know":
#   * CANDIDACY: a candidate must contain a REAL lock write
#     (Set-Content -Path $lock <...> -Encoding ASCII). Otherwise [INFO] not-a-runner.
#     That one gate kills two false signals at once:
#       - a JUDGE script (d2u32_lock_selftest.ps1) that merely MENTIONS both tokens as needles
#         (proved: it is scanned by this very run and comes out not-a-runner);
#       - runners whose marker is built by concatenation (d2u26 shape) being silently SKIPPED,
#         which would look like a clean pass (a false GREEN is worse than a red).
#   * EVENTS COME FROM THE AST, never from regex/indentation: a comment mentioning a token, or a
#     token used as a needle inside `-match` / `-like`, must NOT count (any ast parent that is a
#     BinaryExpressionAst with a Like/Match operator => the literal is a needle, not an emission).
#   * BOTH events must be found. If either is missing, or both land on the same line (order is
#     then undefined), the verdict is [INFO] cannot-classify / order-undefined. NEVER a red.
#   * [INFO] on an ACTIVE runner is a CORRECT answer, not a failure. The judge is not here to
#     manufacture 8 greens.
#   * NOT generalised from d2u32's per-file assertion (which compares two constant line numbers and
#     would go FALSE RED on u44hover, where both tokens sit on one line). Do not reuse that one.
#
# Usage (read-only: takes no lock, writes nothing but stdout):
#   powershell -NoProfile -File c5_order_check.ps1
#   powershell -NoProfile -File c5_order_check.ps1 -Active d2u26_run.ps1,d2u32_run.ps1
#   powershell -NoProfile -File c5_order_check.ps1 -Fixtures     # three-way samples + fixtures
# Exit: 0 = no FAIL (INFO-heavy is fine); 1 = at least one FAIL, or a fixture expectation broke.
# ASCII only (PS 5.1 parses a BOM-less non-ASCII .ps1 as ANSI).
# =============================================================================
param(
    [string]$Root = 'c:/Work/Server/f-v2/clover-project-diablo2',
    [string]$Glob = 'tools/probes/drivers/*.ps1',
    [string[]]$Active = @(),
    [switch]$Fixtures,
    [switch]$ShowInfoReasons
)

$ErrorActionPreference = 'Continue'

# ---- byte-level encoding guard (team lead 2026-09-24, README 5.2 #47 + u52play's measurement) ----
# `Parser::ParseFile` reads TEXT, so it is BLIND to an encoding fault: a file with non-ASCII bytes and
# no BOM passes every parse assertion and then fails to LOAD under PS 5.1 (which reads a BOM-less
# non-ASCII .ps1 as ANSI) -- exit 1 with ZERO output, which looks like "the script produced nothing".
# Encoding therefore has to be judged on BYTES. Editing tools strip the BOM when they touch a file, so
# this file must either stay pure ASCII or carry EF BB BF. Exit 2 = unloadable-by-encoding.
$selfPath2 = $PSCommandPath
if ([string]::IsNullOrEmpty($selfPath2)) { $selfPath2 = $MyInvocation.MyCommand.Path }
if (-not [string]::IsNullOrEmpty($selfPath2)) {
    $sb2 = [System.IO.File]::ReadAllBytes($selfPath2)
    $sNa = 0
    foreach ($z in $sb2) { if ($z -gt 127) { $sNa = $sNa + 1 } }
    $sBom = ($sb2.Length -ge 3 -and $sb2[0] -eq 0xEF -and $sb2[1] -eq 0xBB -and $sb2[2] -eq 0xBF)
    Write-Host ('C5-SELF-ENCODING file=' + (Split-Path $selfPath2 -Leaf) + ' nonAsciiBytes=' + $sNa + ' hasBom=' + $sBom)
    if (($sNa -gt 0) -and (-not $sBom)) {
        Write-Host 'C5-SELF-ENCODING FAIL: non-ASCII without BOM -> PS 5.1 would read this file as ANSI and it would not load'
        exit 2
    }
}

# ---- effective execution position of an emission ------------------------------------------------
# A literal inside a function BODY is not an emission at that line -- the line is only a definition.
# First version of this judge compared raw line numbers and produced TWO FALSE REDS on ACTIVE
# runners (d2u3_charstat_run.ps1 A=L247 B=L124; u52resist_run.ps1 A=L483 B=L161): in both files the
# LOCK-TAKEN emission lives inside a take-lock helper that is only CALLED later in the wait loop.
# So: script scope -> the line itself; inside a function -> the FIRST call site of that function;
# no direct call site found (called indirectly) -> "uncalled" => [INFO], never a red.
function Get-EventOrder([object]$ast, [int]$line) {
    $out = [pscustomobject]@{ Kind = 'script'; Key = $line; Fn = ''; Calls = ''; Note = '' }
    if ($line -le 0) { $out.Kind = 'missing'; return $out }
    $lit = $null
    foreach ($s in @($ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.StringConstantExpressionAst] }, $true))) {
        if ($s.Extent.StartLineNumber -eq $line) { $lit = $s; break }
    }
    if ($null -eq $lit) { $out.Kind = 'unknown'; $out.Note = 'literal not re-found at L' + $line; return $out }
    $fn = $null
    $p = $lit.Parent
    while ($null -ne $p) {
        if ($p -is [System.Management.Automation.Language.FunctionDefinitionAst]) { $fn = $p; break }
        $p = $p.Parent
    }
    if ($null -eq $fn) { return $out }
    $out.Kind = 'function'; $out.Fn = $fn.Name
    $name = $fn.Name
    $callLines = @()
    foreach ($c in @($ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.CommandAst] }, $true))) {
        if ($c.GetCommandName() -eq $name) { $callLines += $c.Extent.StartLineNumber }
    }
    if ($callLines.Count -eq 0) { $out.Kind = 'uncalled'; $out.Note = 'no direct call site for ' + $name; return $out }
    $out.Calls = ($callLines -join ',')
    $out.Key = ($callLines | Measure-Object -Minimum).Minimum
    return $out
}

# ---- the judge ---------------------------------------------------------------------------------
function Test-C5Order([string]$path) {
    $res = [pscustomobject]@{
        Name = (Split-Path $path -Leaf); Verdict = 'INFO'; Reason = ''; A = 0; B = 0; Anchor = 0
    }
    $errs = $null
    $ast = [System.Management.Automation.Language.Parser]::ParseFile($path, [ref]$null, [ref]$errs)
    if (@($errs).Count -gt 0) { $res.Reason = 'parse error(s) -> cannot-classify'; return $res }

    # candidacy: a real lock write (variable named `lock` + an ASCII literal on a Set-Content)
    $lw = $null
    $sets = @($ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.CommandAst] -and $n.GetCommandName() -eq 'Set-Content' }, $true))
    foreach ($c in $sets) {
        # Two ways to recognise a lock write, both AST-based:
        #  (1) the path argument looks like a lock name ($lock / $lockFile / $lockPath / $legacyLock).
        #      A first draft demanded the exact name `lock` and misclassified the ACTIVE runner
        #      u44hover_run.ps1 -- a false NEGATIVE, i.e. the very silent-skip shape this gate exists
        #      to prevent (a skipped runner looks like a clean pass).
        #  (2) STRUCTURAL fallback: u44hover writes its lock through a variable called `$path`, so the
        #      name says nothing -- but a lock write always records its OWNER PID. So: an ASCII write
        #      whose subtree mentions $PID. (Note: $PID is nested inside a paren expression, hence
        #      FindAll over the command subtree, not CommandElements.)
        $hasLockName = @($c.FindAll({ param($n) $n -is [System.Management.Automation.Language.VariableExpressionAst] -and $n.VariablePath.UserPath -match 'lock' }, $true)).Count -ge 1
        $hasPid = @($c.FindAll({ param($n) $n -is [System.Management.Automation.Language.VariableExpressionAst] -and $n.VariablePath.UserPath -eq 'PID' }, $true)).Count -ge 1
        $hasLock = ($hasLockName -or $hasPid)
        $hasAscii = @($c.CommandElements | Where-Object { $_ -is [System.Management.Automation.Language.StringConstantExpressionAst] -and $_.Value -eq 'ASCII' }).Count -ge 1
        if ($hasLock -and $hasAscii) { $lw = $c; break }
    }
    if ($null -eq $lw) { $res.Reason = 'no real lock write -> not a runner'; return $res }
    $res.Anchor = $lw.Extent.StartLineNumber

    # the two events, needle-aware
    $aLine = 0; $bLine = 0
    $strs = @($ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.StringConstantExpressionAst] }, $true))
    foreach ($s in $strs) {
        $isNeedle = $false
        $p = $s.Parent
        while ($null -ne $p) {
            if ($p -is [System.Management.Automation.Language.BinaryExpressionAst]) {
                $op = [string]$p.Operator
                if ($op -match 'Like|Match') { $isNeedle = $true; break }
            }
            $p = $p.Parent
        }
        if ($isNeedle) { continue }
        if (($aLine -eq 0) -and $s.Value.StartsWith('SELFCHECK-ONLY-END')) { $aLine = $s.Extent.StartLineNumber }
        if (($bLine -eq 0) -and $s.Value.StartsWith('LOCK-TAKEN') -and ($s.Extent.StartLineNumber -gt $lw.Extent.StartLineNumber)) {
            $bLine = $s.Extent.StartLineNumber
        }
    }
    $res.A = $aLine; $res.B = $bLine
    if (($aLine -eq 0) -and ($bLine -eq 0)) { $res.Reason = 'no emission found for either event -> cannot-classify'; return $res }
    if ($aLine -eq 0) { $res.Reason = 'no SELFCHECK-ONLY-END emission (concatenated marker?) -> cannot-classify'; return $res }
    if ($bLine -eq 0) { $res.Reason = 'no LOCK-TAKEN emission after the lock write -> cannot-classify'; return $res }
    if ($aLine -eq $bLine) { $res.Reason = 'both emissions on ONE line -> order undefined'; return $res }
    $ea = Get-EventOrder $ast $aLine
    $eb = Get-EventOrder $ast $bLine
    $why = 'A@L' + $aLine + '(' + $ea.Kind + ') B@L' + $bLine + '(' + $eb.Kind + ')'
    if ($ea.Kind -eq 'function') { $why = $why + ' A-fn=' + $ea.Fn + ' A-calls=' + $ea.Calls }
    if ($eb.Kind -eq 'function') { $why = $why + ' B-fn=' + $eb.Fn + ' B-calls=' + $eb.Calls }
    if (($ea.Kind -eq 'unknown') -or ($eb.Kind -eq 'unknown') -or ($ea.Kind -eq 'uncalled') -or ($eb.Kind -eq 'uncalled')) {
        $res.Reason = 'an emission line sits in a function with no direct call site -> cannot-classify (' + $why + ')'
        return $res
    }
    if ($ea.Key -eq $eb.Key) { $res.Reason = 'both effective positions are L' + $ea.Key + ' -> order undefined (' + $why + ')'; return $res }
    if ($ea.Key -lt $eb.Key) { $res.Verdict = 'PASS'; $res.Reason = 'self-check exit precedes the lock take (' + $why + ')' }
    else { $res.Verdict = 'FAIL'; $res.Reason = 'self-check exit comes AFTER the lock take (' + $why + ')' }
    return $res
}

# ---- fixtures: three-way samples (real-file-green / known-bad-red / good-fragment-green) --------
# plus one fixture for each shape that CANNOT be decided (the counter-example set).
function Get-Fixtures {
    $f = [ordered]@{}
    $f['good_min'] = @(
        'param([switch]$SelfCheckOnly)'
        '$lock = ''x'''
        'if ($SelfCheckOnly) { Say (''SELFCHECK-ONLY-END locks-after=[False]''); exit 0 }'
        'Set-Content -Path $lock -Value ''me 2026-01-01T00:00:00+08:00 1'' -Encoding ASCII'
        'Say (''LOCK-TAKEN '' + $lock)'
    )
    $f['bad_order'] = @(
        '$lock = ''x'''
        'Set-Content -Path $lock -Value ''me 2026-01-01T00:00:00+08:00 1'' -Encoding ASCII'
        'Say (''LOCK-TAKEN '' + $lock)'
        'if ($SelfCheckOnly) { Say (''SELFCHECK-ONLY-END locks-after=[False]''); exit 0 }'
    )
    $f['needle_only'] = @(
        '$lock = ''x'''
        'Set-Content -Path $lock -Value ''me'' -Encoding ASCII'
        '$t = [IO.File]::ReadAllText(''r.ps1'')'
        'if ($t -match ''LOCK-TAKEN'') { Write-Host ''has lock marker'' }'
        'if ($t -like ''*SELFCHECK-ONLY-END*'') { Write-Host ''has selfcheck marker'' }'
    )
    $f['concat_no_say'] = @(
        '$lock = ''x'''
        'Set-Content -Path $lock -Value ''me'' -Encoding ASCII'
        '$a = ''SELFCHECK'' + ''-ONLY-END'''
        '$b = ''LOCK'' + ''-TAKEN '' + $lock'
        'Write-Host $a'
        'Write-Host $b'
    )
    $f['same_line'] = @(
        '$lock = ''x'''
        'Set-Content -Path $lock -Value ''me'' -Encoding ASCII'
        'Say (''SELFCHECK-ONLY-END''); Say (''LOCK-TAKEN '' + $lock)'
    )
    $f['not_a_runner'] = @(
        '$t = [IO.File]::ReadAllText(''r.ps1'')'
        'if ($t -like ''*LOCK-TAKEN*'') { Write-Host ''x'' }'
    )
    # the two shapes that produced FALSE REDS in the first version of this judge: the marker lives
    # inside a take-lock helper, so only the CALL SITE decides the real order.
    $f['fn_called_after'] = @(
        'param([switch]$SelfCheckOnly)'
        '$lock = ''x'''
        'function Take-Lock {'
        '    Set-Content -Path $lock -Value ''me'' -Encoding ASCII'
        '    Say (''LOCK-TAKEN '' + $lock)'
        '    return $true'
        '}'
        'if ($SelfCheckOnly) { Say (''SELFCHECK-ONLY-END locks-after=[]''); exit 0 }'
        '$t = Take-Lock'
    )
    $f['fn_called_before'] = @(
        'param([switch]$SelfCheckOnly)'
        '$lock = ''x'''
        'function Take-Lock {'
        '    Set-Content -Path $lock -Value ''me'' -Encoding ASCII'
        '    Say (''LOCK-TAKEN '' + $lock)'
        '    return $true'
        '}'
        '$t = Take-Lock'
        'if ($SelfCheckOnly) { Say (''SELFCHECK-ONLY-END locks-after=[]''); exit 0 }'
    )
    return $f
}

function Get-Expectations {
    $e = [ordered]@{}
    $e['good_min'] = 'PASS'
    $e['bad_order'] = 'FAIL'
    # it DOES have a lock write, so it is a runner -- but its tokens are needles only, so there is no
    # emission to classify. (The first draft of this table wrongly expected not-a-runner; the table
    # itself caught the mistake, which is what the table is for.)
    $e['needle_only'] = 'INFO:cannot-classify'
    $e['fn_called_after'] = 'PASS'
    $e['fn_called_before'] = 'FAIL'
    $e['concat_no_say'] = 'INFO:cannot-classify'
    $e['same_line'] = 'INFO:order undefined'
    $e['not_a_runner'] = 'INFO:not a runner'
    return $e
}

function Test-Expectation([string]$verdict, [string]$reason, [string]$expected) {
    if ($expected -eq 'PASS') { return ($verdict -eq 'PASS') }
    if ($expected -eq 'FAIL') { return ($verdict -eq 'FAIL') }
    if ($expected.StartsWith('INFO:')) {
        $kw = $expected.Substring(5)
        return (($verdict -eq 'INFO') -and ($reason -match [regex]::Escape($kw)))
    }
    return $false
}

# ---- main --------------------------------------------------------------------------------------
$fail = 0
Write-Host '=== C5 order judge (static proxy; INFO is a legal answer) ==='

if ($Fixtures) {
    $root2 = Join-Path $Root '.ai-tmp/test'
    $dir = Join-Path $root2 ('c5-fixtures-' + $PID)
    try {
        if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
        $fx = Get-Fixtures
        $ex = Get-Expectations
        foreach ($name in $fx.Keys) {
            $p = Join-Path $dir ($name + '.ps1')
            [System.IO.File]::WriteAllText($p, (($fx[$name] -join "`n") + "`n"), (New-Object System.Text.ASCIIEncoding))
            $r = Test-C5Order $p
            $ok = Test-Expectation $r.Verdict $r.Reason $ex[$name]
            if (-not $ok) { $fail = $fail + 1 }
            $tag = '[ OK ]'
            if (-not $ok) { $tag = '[FAIL]' }
            $line = $tag + ' ' + $name + ' expect=' + $ex[$name] + ' got=' + $r.Verdict + ' A=' + $r.A + ' B=' + $r.B + ' anchor=' + $r.Anchor + ' :: ' + $r.Reason
            Write-Host $line
        }
    } finally {
        if (Test-Path $dir) { Remove-Item -LiteralPath $dir -Recurse -Force -ErrorAction SilentlyContinue }
    }
    Write-Host ('FIXTURES expectations broken=' + $fail)
    if ($fail -gt 0) { exit 1 }
    exit 0
}

$files = @(Get-ChildItem -Path (Join-Path $Root $Glob) -File -ErrorAction SilentlyContinue | Sort-Object Name)
$nPass = 0; $nFail = 0; $nInfo = 0
foreach ($fo in $files) {
    $r = Test-C5Order $fo.FullName
    if ($r.Verdict -eq 'PASS') { $nPass = $nPass + 1 }
    elseif ($r.Verdict -eq 'FAIL') { $nFail = $nFail + 1 }
    else { $nInfo = $nInfo + 1 }
    $mark = ''
    if ($Active -contains $r.Name) { $mark = ' [ACTIVE]' }
    if (($r.Verdict -ne 'INFO') -or $ShowInfoReasons -or ($Active -contains $r.Name)) {
        Write-Host ($r.Verdict + '  ' + $r.Name + $mark + '  A=L' + $r.A + ' B=L' + $r.B + ' anchor=L' + $r.Anchor + '  ' + $r.Reason)
    } else {
        Write-Host ($r.Verdict + '  ' + $r.Name + $mark + '  ' + $r.Reason)
    }
}
Write-Host ('SUMMARY files=' + $files.Count + ' PASS=' + $nPass + ' FAIL=' + $nFail + ' INFO=' + $nInfo)
if ($nFail -gt 0) { exit 1 }
exit 0
