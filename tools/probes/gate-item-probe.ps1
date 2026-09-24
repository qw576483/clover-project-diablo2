# gate-item-probe.ps1 -- run ONE item of tools/verify.ps1 in isolation, verbatim.
#
# Why this exists: tools/verify.ps1 is one flat script, so the directional self-test
# every changed check owes (reference/anti-gaming.md section 5: a known-good sample
# must PASS and an injected defect must FAIL) would otherwise mean running the WHOLE
# gate -- and other slices are editing the same project concurrently, so a full run
# is a shared resource, not a self-test.  This probe does NOT re-implement any check:
# it slices the real block out of the real tools/verify.ps1 (anchored on the item's
# own comment header) and runs it unchanged, so the probe cannot drift from the gate.
#
# "Cannot drift" is enforced, not asserted:
#   * this file carries NO copy of any judging logic (asserted by
#     .ai-tmp/test/selfcheck-gatepolarity.ps1 section 6, which greps this file for the
#     item's own verdict strings -- they must be absent);
#   * every run prints `HARNESS sliceSha:`, the SHA256 of the UNDMODIFIED slice, and
#     `-DumpSlice <path>` writes those bytes out, so an independent extraction of the
#     same anchor range can be compared byte-for-byte.
# Re-slice (regenerate/extend this tool) after EVERY change to tools/verify.ps1 -- comments
# included -- so its mtime stays newer than the gate it claims to describe.
# Registered items: 15 (impl-by-executor), 17 (console-errors), 35 (no-sync-subagents).
#
# Usage:
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools/probes/gate-item-probe.ps1 -Item 35
#   powershell ... -Item 35 -LogPath <a ledger copy carrying an injected defect>
#   powershell ... -Item 35 -DumpSlice <path to write the exact slice to>
#   powershell ... -Item 17          (console-errors; talks to the live Editor)
#
# Exit: 0 = the item reported PASS ; 1 = the item reported FAIL ; 2 = harness problem.
# ASCII-only on purpose (verify-template.md pitfall 1: PS 5.1 reads a BOM-less .ps1
# as ANSI, so CJK in source silently breaks).

param(
    [string]$Item = '35',
    [string]$LogPath = '',
    [string]$DumpSlice = '',
    [string]$Root = ''
)

$ErrorActionPreference = 'Continue'

# Locate the project root by walking up until tools/verify.ps1 shows up.
$projRoot = $PSScriptRoot
while (-not (Test-Path (Join-Path $projRoot 'tools\verify.ps1'))) {
    $up = Split-Path $projRoot -Parent
    if ($up -eq $projRoot -or $up -eq '') { Write-Output 'HARNESS-FAIL project root not found'; exit 2 }
    $projRoot = $up
}
$gatePath = Join-Path $projRoot 'tools\verify.ps1'

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

# Anchor table: item -> the header line that STARTS the block, and the next item's
# header line (the block ends on the line before that item's leading rule).
$anchors = @{
    '35' = @('^# 35 no-sync-subagents', '^# 36 graphics-device')
    '17' = @('^# 17 console errors', '^# 18 offline hosts')
    '15' = @('^# 15 impl-by-executor', '^# 16 compile')
}
if (-not $anchors.ContainsKey($Item)) {
    Write-Output ('HARNESS-FAIL no anchor registered for item ' + $Item)
    exit 2
}
$startRe = $anchors[$Item][0]
$endRe = $anchors[$Item][1]

$lines = [System.IO.File]::ReadAllLines($gatePath, [System.Text.Encoding]::UTF8)
$start = -1
$end = -1
for ($i = 0; $i -lt $lines.Count; $i++) {
    if ($start -lt 0) {
        if ($lines[$i] -match $startRe) { $start = $i }
    } elseif ($lines[$i] -match $endRe) {
        $end = $i
        break
    }
}
if ($start -lt 0 -or $end -lt 0) { Write-Output ('HARNESS-FAIL could not slice item ' + $Item); exit 2 }

$sliceText = ($lines[($start - 1)..($end - 2)]) -join "`r`n"
$sha256 = [System.Security.Cryptography.SHA256]::Create()
$sliceSha = ([BitConverter]::ToString($sha256.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($sliceText)))).Replace('-', '')
Write-Output ('HARNESS gate    : ' + $gatePath)
Write-Output ('HARNESS block   : item ' + $Item + ' lines ' + ($start + 1) + '..' + ($end - 1))
Write-Output ('HARNESS sliceSha: ' + $sliceSha + ' (sha256 of the UNMODIFIED slice)')
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
