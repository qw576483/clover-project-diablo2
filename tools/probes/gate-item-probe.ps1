# gate-item-probe.ps1 -- run ONE item of tools/verify.ps1 in isolation, verbatim.
#
# Why this exists: tools/verify.ps1 is one flat script, so the directional self-test
# every changed check owes (reference/anti-gaming.md section 5: a known-good sample
# must PASS and an injected defect must FAIL) would otherwise mean running the WHOLE
# gate -- and other slices edit the same project concurrently, so a full run is a
# shared resource, not a self-test.  This probe does NOT re-implement any check: it
# slices the real code out of the real tools/verify.ps1 and runs it unchanged.
#
# HOW "cannot drift" IS ENFORCED (not asserted):
#   * this file carries NO copy of any judging logic -- .ai-tmp/test/selfcheck-gatepolarity.ps1
#     section 6 [3a] greps this file for the items' own verdict strings; they must be absent;
#   * every run prints `HARNESS sliceSha:`, the SHA256 of the UNMODIFIED concatenated slice,
#     and `-DumpSlice <path>` writes those exact bytes out, so an independent extraction of
#     the same anchors can be compared byte-for-byte;
#   * anchors are REGEXES resolved against the CURRENT tools/verify.ps1 at run time --
#     never hard-coded line numbers (a line number rots on the next edit).  A missing
#     anchor is a loud HARNESS-FAIL + exit 2, never a silent PASS.
# Re-slice this tool (it is the only writer's asset) after EVERY change to
# tools/verify.ps1 -- comments included -- so its mtime stays newer than the gate.
#
# MULTI-RANGE SEGMENTS: items 04/05/07/27/28/29 depend on the gate's own PRELUDE
# (code-point name tables, $spec/$planDir/$shots/$client, the adjudication channel,
# the coverage tables).  Those preludes are registered as their own segments and are
# concatenated in the GATE'S OWN DEPENDENCY ORDER -- for 27/28/29 that order is
# 04/05 (which sets $specText/$specLines) BEFORE the coverage prelude (which builds
# $accRows from them) BEFORE the 27/28/29 block.  Getting that order wrong makes
# item 28 report "acceptance table has 0 numbered judgement rows" -- a false red.
#
# Usage:
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools/probes/gate-item-probe.ps1 -Item 35
#   ... -Item 15 -Root <sandbox project root>          (inject defects in COPIES only)
#   ... -Item 35 -LogPath <ledger copy with a defect>   (swaps only the ledger path)
#   ... -Item 35 -DumpSlice <file to write the slice to>
#   ... -Item 07 -Gate <a COPY of verify.ps1>           (anchor-degradation self-test)
# Items: 04 05 07 15 17 27 28 29 35
# Exit: 0 = the item reported PASS ; 1 = the item reported FAIL ; 2 = harness problem.
# ASCII-only on purpose (verify-template.md pitfall 1: PS 5.1 reads a BOM-less .ps1
# as ANSI, so CJK in source silently breaks).

param(
    [string]$Item = '35',
    [string]$LogPath = '',
    [string]$DumpSlice = '',
    [string]$Root = '',
    [string]$Gate = ''
)

$ErrorActionPreference = 'Continue'

# Locate the project root by walking up until tools/verify.ps1 shows up.
$projRoot = $PSScriptRoot
while (-not (Test-Path (Join-Path $projRoot 'tools\verify.ps1'))) {
    $up = Split-Path $projRoot -Parent
    if ($up -eq $projRoot -or $up -eq '') { Write-Output 'HARNESS-FAIL project root not found'; exit 2 }
    $projRoot = $up
}
if ($Gate -ne '') {
    $gatePath = (Resolve-Path $Gate).Path
} else {
    $gatePath = Join-Path $projRoot 'tools\verify.ps1'
}

# The same prelude tools/verify.ps1 establishes before any item runs.
# -Root points the item at a SANDBOX project root (used to inject defects into COPIES of
# client/Assets/** -- the real tree is never touched).
if ($Root -ne '') {
    $root = (Resolve-Path $Root).Path
    Write-Output ('HARNESS root override: ' + $root)
} else {
    $root = $projRoot
}
$client = Join-Path $root 'client'
$testDir = Join-Path $root '.ai-tmp\test'
$fail = 0
$human = 0
$unityOk = $null
try { $unityOk = (Get-Command unity -ErrorAction Stop) } catch { $unityOk = $null }
function Say([string]$status, [string]$name, [string]$detail) {
    if ($status -eq 'FAIL') { $script:fail++ }
    elseif ($status -eq 'HUMAN-ONLY') { $script:human++ }
    Write-Output ("{0,-11} {1}  {2}" -f $status, $name, $detail)
}
# The gate's own two file helpers, copied VERBATIM from tools/verify.ps1 (they are part
# of the prelude every item may call, not part of any item's judging logic).
function Read-Text([string]$p) {
    if (-not (Test-Path $p)) { return $null }
    return [System.IO.File]::ReadAllText($p, [System.Text.Encoding]::UTF8)
}
function Lines-Of([string]$p) {
    if (-not (Test-Path $p)) { return @() }
    return [System.IO.File]::ReadAllLines($p, [System.Text.Encoding]::UTF8)
}

# ---- segments -----------------------------------------------------------------
# mode 'rule'   : slice = (startLine - 1) .. (stopLine - 2)   -- keeps the `# ---` rule
#                 above the item header and drops the next item's rule + header
# mode 'incl'   : slice = startLine .. stopLine               -- both anchors included
# mode 'before' : slice = startLine .. (stopLine - 1)
$segments = [ordered]@{
    'preludeA'   = @('incl',   '^function Cps\(',                      '^\$testDir\s*=')
    'preludeB'   = @('incl',   '^\$adj\s*=\s*@\{\}',                   '^function Adjudicated\(')
    'item0405'   = @('rule',   '^# 04 acceptance table',               '^# 06 allow-diff registry')
    'covPrelude' = @('incl',   '^# dimension codes:',                  '^if \(\$specText -ne \$null\) \{ \$accRows')
    'item07'     = @('rule',   '^# 07 path reachability',              '^# 08 evidence freshness')
    'item272829' = @('before', '^# ---- 27 coverage-diff',             '^# ={20,}')
    'item15'     = @('rule',   '^# 15 impl-by-executor',               '^# 16 compile')
    'item17'     = @('rule',   '^# 17 console errors',                 '^# 18 offline hosts')
    'item35'     = @('rule',   '^# 35 no-sync-subagents',              '^# 36 graphics-device')
}
# item -> the segments to concatenate, in the gate's own dependency order
$items = [ordered]@{
    '04' = @('preludeA', 'preludeB', 'item0405')
    '05' = @('preludeA', 'preludeB', 'item0405')
    '07' = @('preludeA', 'preludeB', 'item0405', 'item07')
    '15' = @('item15')
    '17' = @('item17')
    '27' = @('preludeA', 'preludeB', 'item0405', 'covPrelude', 'item272829')
    '28' = @('preludeA', 'preludeB', 'item0405', 'covPrelude', 'item272829')
    '29' = @('preludeA', 'preludeB', 'item0405', 'covPrelude', 'item272829')
    '35' = @('item35')
}

if (-not $items.Contains($Item)) {
    Write-Output ('HARNESS-FAIL no anchors registered for item ' + $Item + ' (registered: ' + (($items.Keys) -join ' ') + ')')
    exit 2
}

$lines = [System.IO.File]::ReadAllLines($gatePath, [System.Text.Encoding]::UTF8)

function Resolve-Segment([string]$name) {
    $s = $segments[$name]
    $mode = $s[0]; $startRe = $s[1]; $stopRe = $s[2]
    $startLine = -1
    for ($i = 0; $i -lt $lines.Count; $i++) { if ($lines[$i] -match $startRe) { $startLine = $i; break } }
    if ($startLine -lt 0) { throw ('segment ' + $name + ': START anchor not found -> ' + $startRe) }
    $stopLine = -1
    for ($i = $startLine + 1; $i -lt $lines.Count; $i++) { if ($lines[$i] -match $stopRe) { $stopLine = $i; break } }
    if ($stopLine -lt 0) { throw ('segment ' + $name + ': STOP anchor not found below line ' + ($startLine + 1) + ' -> ' + $stopRe) }
    $from = $startLine
    $to = $stopLine
    if ($mode -eq 'rule') { $from = $startLine - 1; $to = $stopLine - 2 }
    elseif ($mode -eq 'before') { $to = $stopLine - 1 }
    if ($from -lt 0 -or $to -lt $from) { throw ('segment ' + $name + ': degenerate range ' + ($from + 1) + '..' + ($to + 1)) }
    return @($from, $to)
}

Write-Output ('HARNESS gate    : ' + $gatePath)
$parts = @()
$spans = @()
foreach ($nm in $items[$Item]) {
    try { $r = Resolve-Segment $nm }
    catch {
        Write-Output ('HARNESS-FAIL ' + $_.Exception.Message)
        Write-Output ('HARNESS-FAIL item ' + $Item + ' was NOT run -- a missing/ambiguous anchor must never be a silent PASS')
        exit 2
    }
    $spans += ($nm + '=' + ($r[0] + 1) + '..' + ($r[1] + 1))
    $parts += (($lines[$r[0]..$r[1]]) -join "`r`n")
}
$sliceText = ($parts -join "`r`n")
Write-Output ('HARNESS block   : item ' + $Item + ' = ' + (($items[$Item]) -join ' + ') + '  [' + ($spans -join ' ') + ']')
$sha256 = [System.Security.Cryptography.SHA256]::Create()
$sliceSha = ([BitConverter]::ToString($sha256.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($sliceText)))).Replace('-', '')
Write-Output ('HARNESS sliceSha: ' + $sliceSha + ' (sha256 of the UNMODIFIED concatenated slice)')
if ($DumpSlice -ne '') {
    [System.IO.File]::WriteAllText($DumpSlice, $sliceText, (New-Object System.Text.UTF8Encoding($false)))
    Write-Output ('HARNESS sliceDump: ' + $DumpSlice)
}

$code = $sliceText
if ($LogPath -ne '') {
    # Swap ONLY the ledger path, so an injected-defect copy is judged by the real logic.
    $before = $code
    $code = $code.Replace("Join-Path `$root '.ai-tmp\test\dispatch-log.tsv'", "'" + $LogPath + "'")
    if ($code -eq $before) { Write-Output 'HARNESS-FAIL -LogPath override matched nothing'; exit 2 }
    Write-Output ('HARNESS override: dispatch ledger -> ' + $LogPath)
}
Write-Output 'HARNESS ---------------- verbatim output below ----------------'

$blockOut = @(Invoke-Expression $code)
$blockOut | ForEach-Object { Write-Output $_ }

Write-Output ('HARNESS fail=' + $fail + ' human=' + $human)
if ($fail -gt 0) { exit 1 } else { exit 0 }
