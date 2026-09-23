# recapture_scan.ps1 -- read-only scan: which matrix rows cite evidence OLDER than a given fix
# timestamp, grouped by dimension, so the recapture list is derived from disk instead of from memory.
#
# Judge asset (SKILL 1.8): moving this out of .ai-tmp/test keeps it re-runnable for the same
# question. $PSScriptRoot-relative so it works from any checkout (tools/probes/measure).
$ErrorActionPreference = 'Stop'
$root = Split-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) -Parent
$shots = Join-Path $root '.ai-tmp\screenshots'
$outDir = Join-Path $root '.ai-tmp\test'
$matrix = Join-Path $root ($([char]0x7B56) + $([char]0x5212) + '\' + $([char]0x72B6) + $([char]0x6001) + $([char]0x77E9) + $([char]0x9635) + '.tsv')
$FIX = [datetime]'2026-09-21 20:05:17'   # mtime of the newest source file at the last verify run
"root   = $root"
"matrix = $matrix"

$lines = [System.IO.File]::ReadAllLines($matrix, [System.Text.Encoding]::UTF8)
"matrix lines = $($lines.Count)"
$rows = @()
for ($i = 0; $i -lt $lines.Count; $i++) {
    $line = $lines[$i]
    if ($line -match '^\s*#' -or $line.Trim().Length -eq 0) { continue }
    $cells = @($line -split "`t")
    if ($cells.Count -lt 8) { continue }
    $refs = @()
    foreach ($m in [regex]::Matches($line, '(?i)\.ai-tmp/screenshots/([A-Za-z0-9_\-]+\.(?:png|txt))')) { $refs += $m.Groups[1].Value }
    foreach ($m in [regex]::Matches($line, '(?i)\.ai-tmp/screenshots/([A-Za-z0-9_\-]*?)(\d+)\.\.[A-Za-z]?(\d+)(\.(?:png|txt))')) {
        $pre = $m.Groups[1].Value; $from = [int]$m.Groups[2].Value; $to = [int]$m.Groups[3].Value; $ext = $m.Groups[4].Value
        for ($k = $from; $k -le $to; $k++) { $refs += ($pre + $k + $ext) }
    }
    $refs = @($refs | Sort-Object -Unique)
    if ($refs.Count -eq 0) { continue }
    $oldest = $null; $oldestName = ''; $newest = $null; $newestName = ''
    foreach ($r in $refs) {
        $p = Join-Path $shots $r
        if (-not (Test-Path $p)) { continue }
        $t = (Get-Item $p).LastWriteTime
        if ($oldest -eq $null -or $t -lt $oldest) { $oldest = $t; $oldestName = $r }
        if ($newest -eq $null -or $t -gt $newest) { $newest = $t; $newestName = $r }
    }
    if ($oldest -eq $null) { continue }
    $rows += [pscustomobject]@{
        Line = $i + 1; Dim = $cells[0]; Entity = $cells[1]; State = $cells[2]
        Verdict = $cells[6]; Oldest = $oldest; OldestName = $oldestName
        Newest = $newest; NewestName = $newestName; Refs = ($refs -join ',')
    }
}
if (-not (Test-Path $outDir)) { New-Item -ItemType Directory -Path $outDir | Out-Null }
$rows | Export-Csv -NoTypeInformation -Encoding UTF8 (Join-Path $outDir 'recapture_scan.csv')
"rows citing screenshots evidence = $($rows.Count)"
$pre = @($rows | Where-Object { $_.Oldest -lt $FIX })
"rows whose OLDEST cited shot predates the fix ($($FIX.ToString('MM-dd HH:mm:ss'))) = $($pre.Count)"
""
"---- grouped by dimension (pre-fix rows) ----"
foreach ($g in ($pre | Group-Object Dim | Sort-Object Name)) {
    "  {0,-8} {1,5} row(s)" -f $g.Name, $g.Count
}
""
"---- ALL-PRE-FIX rows (newest cited shot also predates the fix) ----"
$allPre = @($pre | Where-Object { $_.Newest -lt $FIX })
foreach ($r in ($allPre | Sort-Object Line)) {
    "{0}`t{1}`t{2}`t{3}`t{4}`t{5}(old {6}) / {7}(new {8})" -f $r.Line, $r.Dim, $r.Entity, $r.State, $r.Verdict, `
        $r.OldestName, $r.Oldest.ToString('MM-dd HH:mm:ss'), $r.NewestName, $r.Newest.ToString('MM-dd HH:mm:ss')
}
""
"---- MIXED rows (has at least one post-fix shot) ----"
$mixed = @($pre | Where-Object { $_.Newest -ge $FIX })
foreach ($r in ($mixed | Sort-Object Line)) {
    "{0}`t{1}`t{2}`t{3}`t{4}`t{5}(old {6}) / {7}(new {8})" -f $r.Line, $r.Dim, $r.Entity, $r.State, $r.Verdict, `
        $r.OldestName, $r.Oldest.ToString('MM-dd HH:mm:ss'), $r.NewestName, $r.Newest.ToString('MM-dd HH:mm:ss')
}
