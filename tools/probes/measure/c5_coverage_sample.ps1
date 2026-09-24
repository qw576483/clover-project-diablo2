# c5_coverage_sample.ps1 -- falsifiability sample for tools/verify.ps1 item 14b's zero-coverage guard.
#
# WHY: the 14b row prints "c5-order: files=N PASS=.. FAIL=.. INFO=..".  Without the files= column,
# "PASS=7 FAIL=0" is indistinguishable from "the judge scanned nothing at all" -- a blind judge
# reporting green.  This sample proves the guard turns RED on files=0, i.e. that it can fail.
#
# SAME-SOURCE (team README rule #64 -- a sample must not re-implement the judged logic): Test-C5Coverage
# is NOT copied here.  It is EXTRACTED out of tools/verify.ps1 with the PowerShell AST and executed, so
# the sample follows the real path automatically (change the function in verify.ps1 and this sample
# changes with it).
#
# Judgement assets live under tools/probes/ and are committed (team rule); pure ASCII on purpose.
param(
    [string]$Root = 'c:/Work/Server/f-v2/clover-project-diablo2'
)
$verify = Join-Path $Root 'tools\verify.ps1'
$child = Join-Path $Root 'tools\probes\measure\c5_order_check.ps1'
foreach ($p in @($verify, $child)) {
    if (-not (Test-Path -LiteralPath $p)) { Write-Host ('FAIL missing ' + $p); exit 1 }
}

# ---- extract the guard out of verify.ps1 (same source, no copy) ----
$tk = $null; $er = $null
$vAst = [System.Management.Automation.Language.Parser]::ParseFile($verify, [ref]$tk, [ref]$er)
$fnAst = $vAst.Find({
        param($x)
        $x -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $x.Name -eq 'Test-C5Coverage'
    }, $true)
if (-not $fnAst) { Write-Host 'FAIL Test-C5Coverage not found in tools/verify.ps1 (guard missing => sample inconclusive)'; exit 2 }
. ([ScriptBlock]::Create([string]$fnAst.Extent.Text))
Write-Host ('EXTRACTED from ' + $verify + ' : ' + ([string]$fnAst.Extent.Text -replace "`r?`n", ' '))

# ---- case A: the REAL root.  Expect the guard to pass (files > 0). ----
$real = & powershell -NoProfile -ExecutionPolicy Bypass -Command ("& '" + $child + "' -Root '" + $Root + "'") 2>&1 | Out-String
$realSum = (($real -split "`r?`n") | Where-Object { $_ -match '^SUMMARY' } | Select-Object -First 1)
$realVerdict = Test-C5Coverage $real
Write-Host ('CASE-A real-root        : ' + $realSum + '  => guard=' + $realVerdict + ' (must be True)')

# ---- case B: an EMPTY drivers tree.  The judge scans nothing => files=0 => guard must fail. ----
$emptyRoot = Join-Path $env:TEMP ('u3b-c5-empty-' + $PID)
New-Item -ItemType Directory -Force -Path (Join-Path $emptyRoot 'tools\probes\drivers') | Out-Null
$empty = & powershell -NoProfile -ExecutionPolicy Bypass -Command ("& '" + $child + "' -Root '" + $emptyRoot + "'") 2>&1 | Out-String
$emptySum = (($empty -split "`r?`n") | Where-Object { $_ -match '^SUMMARY' } | Select-Object -First 1)
$emptyVerdict = Test-C5Coverage $empty
Write-Host ('CASE-B empty-root       : ' + $emptySum + '  => guard=' + $emptyVerdict + ' (must be False = RED)')
Remove-Item -LiteralPath $emptyRoot -Recurse -Force -ErrorAction SilentlyContinue

# ---- case C: no denominator printed at all => unverifiable => red ----
$noDenomVerdict = Test-C5Coverage 'PASS  something.ps1  whatever'
Write-Host ('CASE-C no-SUMMARY-output: => guard=' + $noDenomVerdict + ' (must be False = RED)')

# ---- case D: 14b still wires the guard into the fail list (coupling check, text of verify.ps1) ----
$coupled = ((Get-Content -LiteralPath $verify -Raw) -match 'Test-C5Coverage\s+\$c5Out')
Write-Host ('CASE-D 14b-calls-guard  : ' + $coupled + ' (must be True)')

# ---- case E: EXECUTE the real item-14b statements, extracted from verify.ps1 by AST.
# Strongest form of same-source: the c5-order note line printed below is produced by the code that
# really runs in verify.ps1.  Items 1..15 and 16..21 are NOT run (16/17 would call `unity`, which is
# forbidden while the editor is unavailable) -- only the two statements that make up 14b are pulled.
$stmts = $vAst.EndBlock.Statements
$iRgc = -1; $iItem = -1
for ($i = 0; $i -lt $stmts.Count; $i++) {
    $t = [string]$stmts[$i].Extent.Text
    if ($iRgc -lt 0 -and $t -match '\$rgc\s*=\s*Join-Path') { $iRgc = $i }
    if ($iRgc -ge 0 -and $iItem -lt 0 -and $stmts[$i] -is [System.Management.Automation.Language.IfStatementAst] -and $t -match 'runner-static-traps') { $iItem = $i }
}
if ($iRgc -lt 0 -or $iItem -lt 0) { Write-Host 'CASE-E 14b-block-extract: NOT FOUND (inconclusive)'; exit 2 }
$blockText = ((($iRgc..$iItem) | ForEach-Object { [string]$stmts[$_].Extent.Text }) -join "`r`n")
Write-Host ('CASE-E extracted-14b     : statements ' + $iRgc + '..' + $iItem + ' (' + ($blockText -split "`r?`n").Count + ' lines)')
function Say([string]$v, [string]$n, [string]$m) { Write-Output ('SAY ' + $v + ' ' + $n + ' :: ' + $m) }
$eOut = & ([ScriptBlock]::Create($blockText)) 2>&1 | Out-String
$eNote = (($eOut -split "`r?`n") | Where-Object { $_ -match 'c5-order:' } | Select-Object -First 1)
$eSay = (($eOut -split "`r?`n") | Where-Object { $_ -match '^SAY (PASS|FAIL) runner-static-traps' } | Select-Object -First 1)
Write-Host ('CASE-E executed note    : ' + ([string]$eNote).Trim())
Write-Host ('CASE-E executed verdict : ' + ([string]$eSay).Trim())
$eNoteOk = (([string]$eNote) -match 'files=60') -and (([string]$eNote) -notmatch 'zero-coverage')
Write-Host ('CASE-E note-carries-denominator: ' + $eNoteOk + ' (must be True)')

# ---- case F: END-TO-END (the one the ruling asked for).  Run the SAME extracted 14b code against a
# tree whose drivers/ dir is EMPTY, so the child judge genuinely reports files=0.  Expect the note to
# print files=0, the zero-coverage guard to append its red row, and the item verdict to be FAIL.
# Copies of the two measure scripts are used (temp tree only, deleted below); no `unity` is involved.
$tRoot = Join-Path $env:TEMP ('u3b-c5-zerocov-' + $PID)
New-Item -ItemType Directory -Force -Path (Join-Path $tRoot 'tools\probes\drivers') | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $tRoot 'tools\probes\measure') | Out-Null
Copy-Item -LiteralPath (Join-Path $Root 'tools\probes\measure\runner_gate_check.ps1') -Destination (Join-Path $tRoot 'tools\probes\measure\runner_gate_check.ps1') -Force
Copy-Item -LiteralPath $child -Destination (Join-Path $tRoot 'tools\probes\measure\c5_order_check.ps1') -Force
$savedRoot = $root
$root = $tRoot
$fOut = & ([ScriptBlock]::Create($blockText)) 2>&1 | Out-String
$root = $savedRoot
Remove-Item -LiteralPath $tRoot -Recurse -Force -ErrorAction SilentlyContinue
$fNote = (($fOut -split "`r?`n") | Where-Object { $_ -match 'c5-order:' } | Select-Object -First 1)
$fGuard = (($fOut -split "`r?`n") | Where-Object { $_ -match 'c5-zero-coverage' } | Select-Object -First 1)
$fSay = (($fOut -split "`r?`n") | Where-Object { $_ -match '^SAY (PASS|FAIL) runner-static-traps' } | Select-Object -First 1)
Write-Host ('CASE-F zero-cov note    : ' + ([string]$fNote).Trim())
Write-Host ('CASE-F zero-cov red row : ' + ([string]$fGuard).Trim())
Write-Host ('CASE-F zero-cov verdict : ' + ([string]$fSay).Trim())
$fOk = (([string]$fNote) -match 'files=0') -and (([string]$fGuard) -match 'c5-zero-coverage') -and (([string]$fSay) -match '^SAY FAIL')
Write-Host ('CASE-F files=0-makes-14b-RED: ' + $fOk + ' (must be True)')

$bad = 0
if (-not $realVerdict) { $bad++ }
if ($emptyVerdict) { $bad++ }
if ($noDenomVerdict) { $bad++ }
if (-not $coupled) { $bad++ }
if (-not $eNoteOk) { $bad++ }
if (-not $fOk) { $bad++ }
Write-Host ('SAMPLE-C5-COVERAGE failures=' + $bad + ' VERDICT=' + $(if ($bad -eq 0) { 'PASS' } else { 'FAIL' }))
# README §67: green <=> a `RESULT OK` line exists AND exit code = 0; a missing RESULT line is a tool
# fault (red), never a pass.  Invoke this file by path, not with an in-session `& call`.
Write-Host ('RESULT ' + $(if ($bad -eq 0) { 'OK' } else { 'FAIL' }))
if ($bad -eq 0) { exit 0 } else { exit 1 }
