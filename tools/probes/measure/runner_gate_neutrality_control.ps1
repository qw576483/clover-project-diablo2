# runner_gate_neutrality_control.ps1 -- POSITIVE CONTROL for a "0 hits" claim (team rule (3)-2:
# before believing any "found nothing" result, prove the matcher can match at all).
#
# CLAIM UNDER TEST (sheet u3bverify, 2026-09-24, recorded in runner_gate_check.ps1's C7 header):
#   "removing the two raw-text bare-word fallbacks from runner_gate_check.ps1 is VERDICT-NEUTRAL on
#    the real corpus -- 58 runners, 0 of which depended on them."
# A "0 files depended on it" result is a 0-hit conclusion, so it must carry a positive control.
#
# METHOD -- take the REAL gate, RE-ADD exactly the two fallbacks (a syntactic regression), run BOTH
# builds with -Scope all on the same project, then DIFF their output row by row:
#   * every per-file row identical AND per-row FAIL still 0 in both  => the fallbacks are inert on
#     this tree (the neutrality claim holds);
#   * the REGRESSED build must nevertheless fail MORE, and every extra failure must come from the
#     synthetic KNOWN-BAD2 sample => the matcher demonstrably CAN match (that is the control).
# If a real runner row changed under the regression, the neutrality claim would be FALSE.
#
# Judgement asset: lives under tools/probes/ and is committed; pure ASCII on purpose.
# Read-only w.r.t. the project: the mutated copy is written to $env:TEMP and deleted in `finally`.
param([string]$ProjectRoot = 'c:/Work/Server/f-v2/clover-project-diablo2')
$src = Join-Path $ProjectRoot 'tools\probes\measure\runner_gate_check.ps1'
if (-not (Test-Path -LiteralPath $src)) { Write-Host ('FAIL missing ' + $src); exit 1 }
$copy = Join-Path $env:TEMP ('rungate-pc-' + $PID + '.ps1')
$t = [System.IO.File]::ReadAllText($src)

# the two fallbacks that were removed in the real file (exact tool bodies as of 2026-09-24)
$a = '    return ((Get-EmissionInfo $ast).endm.Count -gt 0)'
$b = '    return (((Get-EmissionInfo $ast).endm.Count -gt 0) -or ($ast.Extent.Text -match ''END-MARKER''))'
$c = '    return (Get-HasParam $ast ''SelfCheckOnly'')'
$d = '    return ((Get-HasParam $ast ''SelfCheckOnly'') -or ($ast.Extent.Text -match ''\$SelfCheckOnly\b''))'

$n1 = ([regex]::Matches($t, [regex]::Escape($a))).Count
$n2 = ([regex]::Matches($t, [regex]::Escape($c))).Count
Write-Host "REGRESSION anchors: terminalMarker=$n1 unlocked=$n2 (each must be 1)"
if ($n1 -ne 1 -or $n2 -ne 1) { Write-Host 'ANCHOR-MISS (the gate was refactored) -> control inconclusive, do not claim either way'; exit 2 }
[System.IO.File]::WriteAllText($copy, $t.Replace($a, $b).Replace($c, $d), (New-Object System.Text.ASCIIEncoding))

try {
    $real = & powershell -NoProfile -ExecutionPolicy Bypass -File $src -ProjectRoot $ProjectRoot -Scope all 2>&1
    $realRc = $LASTEXITCODE
    $reg = & powershell -NoProfile -ExecutionPolicy Bypass -File $copy -ProjectRoot $ProjectRoot -Scope all 2>&1
    $regRc = $LASTEXITCODE
    Write-Host "rc real=$realRc regressed=$regRc (real must be 0)"

    $r1 = @($real | Where-Object { $_ -match '_run\.ps1\s+(PASS|FAIL)' })
    $r2 = @($reg | Where-Object { $_ -match '_run\.ps1\s+(PASS|FAIL)' })
    Write-Host "per-file rows real=$($r1.Count) regressed=$($r2.Count) (must be equal)"
    $diff = 0
    for ($i = 0; $i -lt [Math]::Max($r1.Count, $r2.Count); $i++) {
        if ([string]$r1[$i] -ne [string]$r2[$i]) { $diff++; Write-Host ("ROW-DIFF #" + $i); Write-Host ("  real: " + $r1[$i]); Write-Host ("  regr: " + $r2[$i]) }
    }
    # a flip on a historical runner shows up as an [INFO] label, not as a FAIL -> compare those too
    $i1 = @($real | Where-Object { $_ -match 'no-terminal-marker|no-unlocked-SelfCheckOnly-entry' })
    $i2 = @($reg | Where-Object { $_ -match 'no-terminal-marker|no-unlocked-SelfCheckOnly-entry' })
    Write-Host "no-marker/no-unlock INFO lines real=$($i1.Count) regressed=$($i2.Count) (must be equal)"

    $k1 = (($real | Where-Object { $_ -match 'KNOWN-BAD2-SAMPLE' }) -join ' ')
    $k2 = (($reg | Where-Object { $_ -match 'KNOWN-BAD2-SAMPLE' }) -join ' ')
    Write-Host "KNOWN-BAD2 real     : $k1"
    Write-Host "KNOWN-BAD2 regressed: $k2"
    Write-Host "SCOPE real     : " + (($real | Where-Object { $_ -match '^SCOPE=' }) -join ' ')
    Write-Host "SCOPE regressed: " + (($reg | Where-Object { $_ -match '^SCOPE=' }) -join ' ')

    $realFail = @($real | Where-Object { $_ -match '_run\.ps1\s+FAIL' }).Count
    $regFail = @($reg | Where-Object { $_ -match '_run\.ps1\s+FAIL' }).Count
    Write-Host "per-row FAIL real=$realFail regressed=$regFail (both must be 0)"
    $canMatch = (($k2 -match 'barewordSatisfiesEnd=True') -and ($k2 -match 'barewordSatisfiesUnlocked=True') -and ($k1 -match 'barewordSatisfiesEnd=False'))
    $neutral = ($diff -eq 0) -and ($i1.Count -eq $i2.Count) -and ($realFail -eq 0) -and ($regFail -eq 0) -and ($realRc -eq 0)
    Write-Host ("POSITIVE-CONTROL rowDiffs=" + $diff + " matcher-can-match=" + $canMatch + " neutrality-holds-on-real-rows=" + $neutral)
    if ($canMatch -and $neutral) { Write-Host 'VERDICT=PASS (claim holds, and the control proves the check is not blind)'; exit 0 }
    Write-Host 'VERDICT=FAIL (either the control is blind or the neutrality claim is false -- report it)'
    exit 1
} finally { Remove-Item -LiteralPath $copy -Force -ErrorAction SilentlyContinue }
