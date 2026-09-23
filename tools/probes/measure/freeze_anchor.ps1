# freeze_anchor.ps1 -- read-only replication of tools/verify.ps1 item 23 (freeze-before-capture),
# so the anchor can be printed BEFORE and AFTER a stale index is removed.
# usage: powershell -NoProfile -ExecutionPolicy Bypass -File tools/probes/measure/freeze_anchor.ps1 [-Exclude <index-name>]
# ASCII-only on purpose (verify.ps1 item 14 flags non-ASCII .ps1 under .ai-tmp without a BOM).
# Judge asset (SKILL 1.8): $PSScriptRoot-relative so it works from any checkout.
param([string[]]$Exclude = @())
$ErrorActionPreference = 'Stop'
$root    = Split-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) -Parent
$shots   = Join-Path $root '.ai-tmp\screenshots'
$scripts = Join-Path $root 'client\Assets\Scripts'

function Read-Lines([string]$p) { return [System.IO.File]::ReadAllLines($p, [System.Text.Encoding]::UTF8) }

Write-Output "===== newest-index anchor (verify.ps1 item 23 wording) ====="
$idxFiles = @(Get-ChildItem $shots -Filter '*.index.tsv' | Sort-Object LastWriteTime -Descending)
foreach ($ix in $idxFiles) { Write-Output ("  index  {0}  {1}" -f $ix.LastWriteTime.ToString('yyyy-MM-dd HH:mm:ss'), $ix.Name) }
$cand = @($idxFiles | Where-Object { $Exclude -notcontains $_.Name })
if ($cand.Count -eq 0) { throw 'no index file left after -Exclude' }
$idx0 = $cand[0]
if ($Exclude.Count -gt 0) { Write-Output ("  EXCLUDED: " + ($Exclude -join ', ')) }
Write-Output ("  -> USED (newest after exclude): {0}" -f $idx0.Name)
$sheetRefs = @()
foreach ($line in @(Read-Lines $idx0.FullName)) {
    foreach ($m in [regex]::Matches($line, '[A-Za-z0-9_\-]+\.png')) { $sheetRefs += $m.Value }
}
$sheetRefs = @($sheetRefs | Sort-Object -Unique)
$batch = @()
foreach ($r in $sheetRefs) { $p = Join-Path $shots $r; if (Test-Path $p) { $batch += (Get-Item $p) } }
Write-Output "  refs:"
foreach ($b in ($batch | Sort-Object LastWriteTime)) {
    Write-Output ("    {0}  {1}" -f $b.LastWriteTime.ToString('yyyy-MM-dd HH:mm:ss'), $b.Name)
}
if ($batch.Count -eq 0) {
    # same fallback as verify.ps1: no resolvable tile in the index -> use the 6h window
    Write-Output "  NOTE: index tiles live in a sub-directory -> verify.ps1 falls back to the 6h png window"
    $win = (Get-Date).AddHours(-6)
    $batch = @(Get-ChildItem $shots -Filter *.png | Where-Object { $_.LastWriteTime -gt $win })
    foreach ($b in ($batch | Sort-Object LastWriteTime | Select-Object -First 5)) {
        Write-Output ("    {0}  {1}" -f $b.LastWriteTime.ToString('yyyy-MM-dd HH:mm:ss'), $b.Name)
    }
}
$t0 = ($batch | Sort-Object LastWriteTime | Select-Object -First 1).LastWriteTime
Write-Output ("  T0 (anchor) = {0}" -f $t0.ToString('yyyy-MM-dd HH:mm:ss'))

$after = @()
$after += @(Get-ChildItem $scripts -Recurse -Filter *.cs | Where-Object { $_.LastWriteTime -gt $t0 })
# same sibling-of-root convention as verify.ps1 item 23
$engRuntime = Join-Path (Split-Path $root -Parent) 'clover-client-unity-engine\Runtime'
if (Test-Path $engRuntime) { $after += @(Get-ChildItem $engRuntime -Recurse -Filter *.cs | Where-Object { $_.LastWriteTime -gt $t0 }) }
$after = @($after | Sort-Object FullName -Unique)
Write-Output ("  impl files after T0 = {0}" -f $after.Count)
foreach ($f in $after) { Write-Output ("    {0}  {1}" -f $f.LastWriteTime.ToString('yyyy-MM-dd HH:mm:ss'), $f.FullName.Substring($root.Length + 1)) }
if ($after.Count -eq 0) { Write-Output '  RESULT = PASS (no impl file touched after capture start)' }
else { Write-Output '  RESULT = the red sentence WOULD fire for the files listed above' }

Write-Output ""
Write-Output "===== every *contact*.index.tsv : mtime / tile count / oldest tile / newest tile ====="
foreach ($ix in @(Get-ChildItem $shots -Filter '*contact*.index.tsv' | Sort-Object LastWriteTime -Descending)) {
    $refs = @()
    foreach ($line in @(Read-Lines $ix.FullName)) {
        foreach ($m in [regex]::Matches($line, '[A-Za-z0-9_\-]+\.png')) { $refs += $m.Value }
    }
    $refs = @($refs | Sort-Object -Unique)
    $files = @()
    foreach ($r in $refs) { $p = Join-Path $shots $r; if (Test-Path $p) { $files += (Get-Item $p) } }
    if ($files.Count -eq 0) {
        Write-Output ("  {0}  mtime={1}  tiles=0 (none resolvable next to the index)" -f $ix.Name, $ix.LastWriteTime.ToString('yyyy-MM-dd HH:mm:ss'))
        continue
    }
    $old = $files | Sort-Object LastWriteTime | Select-Object -First 1
    $new = $files | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    Write-Output ("  {0}  mtime={1}  tiles={2}  oldest={3}({4})  newest={5}({6})" -f `
        $ix.Name, $ix.LastWriteTime.ToString('yyyy-MM-dd HH:mm:ss'), $files.Count,
        $old.LastWriteTime.ToString('MM-dd HH:mm:ss'), $old.Name,
        $new.LastWriteTime.ToString('MM-dd HH:mm:ss'), $new.Name)
}
