# =============================================================================
# tools/check-assets.ps1 -- skill 8.1 (original-asset rule) -- STOCK mode
#
#   Rule: assets that entered the project must be REACHABLE from code/table text.
#   This script is the STOCK variant (inventory of what is already on disk):
#   it only REPORTS, it never deletes.
#
#   Reference source text = the concatenated text of everything the client can read:
#     client/Assets/Scripts/**/*.cs      (code)
#     client/Assets/Editor/**/*.cs       (editor code)
#     client/Assets/Configs/**           (json config)
#     client/Assets/Resources/Levels/*.txt
#   An asset counts as REFERENCED when its extension-less FILE NAME occurs
#   literally in that text.
#
#   Whitelist (never counted as unreferenced):
#     W1  file name starts with "_"
#     W2  files under Resources/UI/  -- still counted in the unreferenced total,
#         but also reported as its own number (so a human can judge the panel
#         prefabs that are loaded through ResPaths constants).
#
#   Usage:
#     powershell -NoProfile -ExecutionPolicy Bypass -File tools/check-assets.ps1 -Warn
#       -> report only, exit code 0 (use this during a cleanup pass)
#     powershell -NoProfile -ExecutionPolicy Bypass -File tools/check-assets.ps1
#       -> gate mode: unreferenced > 0 => exit code 1
#
#   Output is ASCII-only ON PURPOSE (PS 5.1 parses a non-ASCII .ps1 without BOM
#   as ANSI -> silent mismatch; same pitfall as tools/verify.ps1).
# =============================================================================
param([switch]$Warn)

$ErrorActionPreference = 'Continue'

$root   = Split-Path $PSScriptRoot -Parent
$client = Join-Path $root 'client'
$resDir = Join-Path $client 'Assets\Resources'

function Read-Text2([string]$p) {
    if (-not (Test-Path $p)) { return '' }
    try { return [System.IO.File]::ReadAllText($p, [System.Text.Encoding]::UTF8) } catch { return '' }
}

Write-Output "===== check-assets.ps1 (skill 8.1 stock mode) ====="
Write-Output ("root = " + $root)
Write-Output ("mode = " + $(if ($Warn) { 'warn (report only, exit 0)' } else { 'gate (unreferenced > 0 => exit 1)' }))

# ---------------------------------------------------------------- reference text
$srcGroups = @(
    @('scripts', (Join-Path $client 'Assets\Scripts'),   '*.cs',  $true),
    @('editor',  (Join-Path $client 'Assets\Editor'),    '*.cs',  $true),
    @('configs', (Join-Path $client 'Assets\Configs'),   '*.json', $true),
    @('levels',  (Join-Path $client 'Assets\Resources\Levels'), '*.txt', $false)
)
$sb = New-Object System.Text.StringBuilder
$srcCount = @{}
foreach ($g in $srcGroups) {
    $n = 0
    if (Test-Path $g[1]) {
        $files = @(if ($g[3]) { Get-ChildItem $g[1] -Recurse -File -Filter $g[2] -ErrorAction SilentlyContinue }
                   else      { Get-ChildItem $g[1] -File -Filter $g[2] -ErrorAction SilentlyContinue })
        foreach ($f in $files) {
            [void]$sb.AppendLine((Read-Text2 $f.FullName))
            $n++
        }
    }
    $srcCount[$g[0]] = $n
}
$blob = $sb.ToString()
Write-Output ("ref-source files: scripts=" + $srcCount['scripts'] + " editor=" + $srcCount['editor'] + " configs=" + $srcCount['configs'] + " levels=" + $srcCount['levels'] + " ; text chars = " + $blob.Length)

# ---------------------------------------------------------------- asset scan
if (-not (Test-Path $resDir)) {
    Write-Output ("resource dir missing: " + $resDir)
    Write-Output "RESULT: resource-files=0 unreferenced=0 unreferenced-MB=0"
    if ($Warn) { exit 0 } else { exit 0 }
}

$assets = @(Get-ChildItem $resDir -Recurse -File -ErrorAction SilentlyContinue |
            Where-Object { $_.Extension -ne '.meta' })
$totalFiles = $assets.Count
$totalBytes = 0
foreach ($a in $assets) { $totalBytes += $a.Length }

$unref      = @()   # one entry per unreferenced FILE
$unrefBytes = 0
$w1         = 0     # whitelist W1: name starts with "_"
$uiUnref    = 0     # whitelist W2: under Resources/UI/ -- counted, reported apart
$nameCache  = @{}   # literal-name -> referenced?  (cache: the same name repeats)

foreach ($a in $assets) {
    $base = [System.IO.Path]::GetFileNameWithoutExtension($a.Name)
    if ($base.StartsWith('_')) { $w1++; continue }                        # W1
    $hit = $nameCache[$base]
    if ($null -eq $hit) {
        # literal containment of the file name in the reference text
        $hit = ($blob -match [regex]::Escape($base))
        $nameCache[$base] = $hit
    }
    if (-not $hit) {
        $rel = $a.FullName.Substring($resDir.Length).TrimStart('\', '/')
        $top = ($rel -split '\\')[0]
        if (@($rel -split '\\').Count -eq 1) { $top = '(Resources root)' }
        $unref += [pscustomobject]@{ Rel = $rel; Base = $base; Top = $top; Len = $a.Length }
        $unrefBytes += $a.Length
        if ($top -eq 'UI') { $uiUnref++ }                                 # W2
    }
}

Write-Output ("resource files = " + $totalFiles + " (size = " + [math]::Round($totalBytes / 1MB, 1) + " MB)")
Write-Output ("whitelist W1 (name starts with '_')                        = " + $w1)
Write-Output ("whitelist W2 (under Resources/UI/, also counted below)    = " + $uiUnref)
Write-Output ("unreferenced = " + $unref.Count + " / " + $totalFiles)
Write-Output ("unreferenced volume = " + [math]::Round($unrefBytes / 1MB, 1) + " MB")
Write-Output ("distinct unreferenced names = " + (@($unref | ForEach-Object { $_.Base } | Sort-Object -Unique).Count))

Write-Output "unreferenced by top-level subdir (top 10):"
$grp = @($unref | Group-Object Top | Sort-Object Count -Descending | Select-Object -First 10)
if ($grp.Count -eq 0) { Write-Output "  (none)" }
foreach ($g in $grp) {
    $mb = [math]::Round((($g.Group | Measure-Object Len -Sum).Sum) / 1MB, 1)
    Write-Output ("  " + $g.Name.PadRight(24) + " " + $g.Count + " file(s)  " + $mb + " MB")
}

Write-Output "unreferenced list (one line per file):"
foreach ($u in $unref) { Write-Output ("  UNREF  " + $u.Rel) }

if ($Warn) {
    Write-Output ("RESULT: resource-files=" + $totalFiles + " unreferenced=" + $unref.Count + " unreferenced-MB=" + [math]::Round($unrefBytes / 1MB, 1) + " -> WARN (exit 0)")
    exit 0
} else {
    if ($unref.Count -gt 0) {
        Write-Output ("RESULT: resource-files=" + $totalFiles + " unreferenced=" + $unref.Count + " unreferenced-MB=" + [math]::Round($unrefBytes / 1MB, 1) + " -> FAIL (exit 1)")
        exit 1
    } else {
        Write-Output ("RESULT: resource-files=" + $totalFiles + " unreferenced=0 -> PASS (exit 0)")
        exit 0
    }
}
