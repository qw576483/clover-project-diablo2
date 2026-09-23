# =============================================================================
# tools/verify.ps1 -- one-click re-check gate for clover-project-diablo2
#
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools/verify.ps1
#
# Every line prints exactly one of: PASS / FAIL / HUMAN-ONLY.
#   PASS      = computed by this script (or by a tool it really ran)
#   FAIL      = must be fixed; while any FAIL exists, nobody may say "done"
#   HUMAN-ONLY= computable by a human / multimodal reader only -- must be looked at
#
# Covers (SKILL.md 1.11 items 1..8 + 1.12 contract + this project's own gates):
#   01 stray-temp-files        no one-off *.cs outside .ai-tmp/test
#   02 hard-rules              Debug.Log / PlayerPrefs / GameObject.Find / ...
#   03 direction-key-move      original D2 has no key-move path at all (0 hits)
#   04 table-rows              acceptance table row count (57 = 51 + 6 audit rows) + per-row category tag
#   04b six-dim-parity          ce hua/dui zhao biao.md: the six 1:1 dims, every row
#                              "yuan ban zhi | wo men de zhi | cha zhi", cha zhi = 0 or a
#                              registry id, no fuzzy wording; a missing file FAILs (not adjudicable)
#   05 table-summary           summary numbers == row counts
#   06 allow-diff-registry     every registered exception has why/origin/when
#   07 path-reachability       every .ai-tmp/screenshots/<file> cited by the table exists
#                              (incl. :reference-pictures, adjudicable like every other item)
#   08 freshness-own-rows      the evidence the U-1 / U-2 row itself cites is present + newer
#                              (file names are read from the table row, never hard-coded)
#   09 freshness-global        (HUMAN-ONLY) batch rule, see SKILL 1.13
#   10 reference-tables        the plan/zishenduibi/*.md comparison family exists
#   11 no-handoff-docs         no NEXT.md / *progress* / *handoff* docs
#   12 engine-selfname         (HUMAN-ONLY) the engine calls itself clover-engine
#   13 no-escaped-artifacts    no one-off artifacts outside the project
#   14 sampler-selfcheck       repo-wide .ps1/.psm1 syntax + ANSI trap (widened 2026-09-23)
#   15 impl-by-executor        every changed impl file matches a dispatch record
#   16 compile                 unity command recompile + status poll
#   17 console-errors          unity command console_status: errors == 0 ; exactly one narrow
#                              whitelist (an entry logged by the Unity.Pipeline tool chain
#                              itself: main-thread timeout), every filtered entry printed
#                              verbatim, and remaining > 0 still FAILs
#   18 offline-hosts           dotnet run of every tools/probes/hosts/*check
#   19 d2codec-verifiers       python tools/d2codec/verify_*.py
#   20 original-asset-md5      project PNG == independent DC6 decode (sha256)
#   21 skill-family-consistency (HUMAN-ONLY) project skill vs global rules
#   25 coverage-rows           manifest rows == matrix distinct entities AND matrix rows == sum(state counts)
#   26 coverage-filled         matrix has zero empty rows; every verdict is one of the 4 legal values
#   27 coverage-diff           zero mismatch verdicts; every allowed-difference row names a
#                              registry id ("<allowed>(-> <id>)") that matches the leading id
#                              of a registry row, and every registry row is 4-of-4
#   28 coverage-acceptance     every numbered acceptance row cites an on-disk path
#   29 coverage-dimensions     all 15 dimension codes have >= 1 manifest row
#
# Renamed in the 2026-09-22 gate-alignment pass (judging strength untouched; the name is now
# the template's, so scripts/gate-sync.ps1 can see it -- it extracts Say '<STATUS>' '<name>'):
#   table-summary -> acceptance-table | allow-diff-registry -> allowed-diff
#   path-reachability -> screenshot-refs | freshness:u2-rows -> evidence-freshness
#   six-dim-parity -> reference-table | table-category -> row-category | play-budget -> play-ledger
#   engine-selfname -> engine-credit (a source grep REPLACED by a half-machine / half-rendered judge)
# Added in the same pass (template items this project had under no name; see the template block
# at the end of this file): 30 verify-entry | 31 spec-doc | 32 asset-research-doc |
#   33 baseline-images | 34 no-assets-screenshots | 35 no-team-sessions | 36 graphics-device |
#   37 numeric-log-only | 38 scale-tier | 39 impact-radius
#
# This file is ASCII-only ON PURPOSE (see reference/verify-template.md pitfall 1/2):
# PS 5.1 parses a non-ASCII .ps1 without BOM as ANSI -> Chinese -match silently fails.
# Every non-ASCII path / word below is built from code points.
# =============================================================================

$ErrorActionPreference = 'Continue'
$root = Split-Path $PSScriptRoot -Parent
$fail = 0
$human = 0

function Say([string]$status, [string]$name, [string]$detail) {
    if ($status -eq 'FAIL') { $script:fail++ }
    elseif ($status -eq 'HUMAN-ONLY') { $script:human++ }
    Write-Output ("{0,-11} {1}  {2}" -f $status, $name, $detail)
}

function Read-Text([string]$p) {
    if (-not (Test-Path $p)) { return $null }
    return [System.IO.File]::ReadAllText($p, [System.Text.Encoding]::UTF8)
}
function Lines-Of([string]$p) {
    if (-not (Test-Path $p)) { return @() }
    return [System.IO.File]::ReadAllLines($p, [System.Text.Encoding]::UTF8)
}
# line numbers (1-based) whose text matches $re ; $skipComment drops // /// and * lines
# ⚠️ 用 [regex]::IsMatch(...,'None') = **区分大小写**：PowerShell 的 `-match` 默认不区分大小写，
#    会把 `input.targetGraphic` 这种局部变量名当成"裸 Input."（实测：54 条假阳性）。
#    要忽略大小写就在模式里自己写 `(?i)`。
function Grep([string]$p, [string]$re, [switch]$skipComment) {
    $out = @()
    $lines = Lines-Of $p
    for ($i = 0; $i -lt $lines.Length; $i++) {
        $t = $lines[$i]
        if ($skipComment -and $t -match '^\s*(//|///|\*)') { continue }
        if ([regex]::IsMatch($t, $re, [System.Text.RegularExpressions.RegexOptions]::None)) { $out += ($i + 1) }
    }
    return $out
}
function Cps([int[]]$codes) { return ([char[]]$codes -join '') }

# -- non-ASCII names / words, built from code points (keeps this file ASCII) ----
$planDir   = Join-Path $root (Cps @(0x7B56, 0x5212))                                  # ce hua
$cmpDir    = Join-Path $planDir (Cps @(0x81EA, 0x5BA1, 0x5BF9, 0x6BD4))               # zi shen dui bi
$spec      = Join-Path $planDir ((Cps @(0x9A8C, 0x6536, 0x8868)) + '.md')             # yan shou biao
$refDir    = Join-Path $root (Cps @(0x539F, 0x7248, 0x8D44, 0x6E90))                  # yuan ban zi yuan
$refPicDir = Join-Path $refDir (Cps @(0x53C2, 0x8003, 0x56FE))                        # can kao tu
$cNumeric  = Cps @(0x6570, 0x503C, 0x7C7B)                                            # shu zhi lei
$cVisual   = Cps @(0x8868, 0x73B0, 0x7C7B)                                            # biao xian lei
$cPerf     = Cps @(0x6027, 0x80FD, 0x7C7B)                                            # xing neng lei
$cCategory = Cps @(0x7C7B, 0x522B)                                                    # lei bie
$cProgress = Cps @(0x8FDB, 0x5EA6)                                                    # jin du
$cHandoff  = Cps @(0x4EA4, 0x63A5)                                                    # jiao jie
$cPass     = Cps @(0x901A, 0x8FC7)                                                    # tong guo
$cMismatch = Cps @(0x4E0D, 0x4E00, 0x81F4)                                            # bu yi zhi
$cReferTu  = Cps @(0x53C2, 0x8003, 0x56FE)                                            # can kao tu
$cYuanBan  = Cps @(0x539F, 0x7248, 0x8D44, 0x6E90)                                    # yuan ban zi yuan
$cPlanNm   = Cps @(0x7B56, 0x5212)
$cUnfixed  = Cps @(0x672A, 0x4FEE)                                                    # wei xiu (unfixed) -- acceptance-table bucket
$cUnfixedV = $cUnfixed + '(' + (Cps @(0x7F3A, 0x9677)) + ')'                           # wei xiu (que xian) -- the 4th legal matrix verdict                                                    # ce hua

$client   = Join-Path $root 'client'
$scripts  = Join-Path $client 'Assets\Scripts'
$shots    = Join-Path $root '.ai-tmp\screenshots'
$cfgDir   = Join-Path $client 'Assets\Configs'
$setDir   = Join-Path $client 'setting'
$tmpDir   = Join-Path $root '.ai-tmp'
$hostsDir = Join-Path $tmpDir 'hosts'
$testDir  = Join-Path $tmpDir 'test'

# -----------------------------------------------------------------------------
# adjudication channel (SKILL 1.11 item 4: every exception needs a registry).
#   Format in dispatch-log.tsv:  # adjudicated: <check-name> -- <reason, >= 12 chars>
#   Effect: that check reports HUMAN-ONLY (with the reason) instead of FAIL.
#   Use it ONLY when a human has judged the red to be a false positive *for a stated
#   reason* (e.g. a comment-only edit invalidating a behaviour contact sheet).
#   It never silences an unexplained red -- a reason shorter than 12 chars is ignored.
# -----------------------------------------------------------------------------
$adj = @{}
$adjLog = Join-Path $testDir 'dispatch-log.tsv'
if (Test-Path $adjLog) {
    foreach ($line in @(Lines-Of $adjLog)) {
        if ($line -match '^\s*#\s*adjudicated:\s*([^\s]+)\s*--\s*(.+)$') {
            $cn = $Matches[1].Trim(); $rs = $Matches[2].Trim()
            if ($rs.Length -ge 12) { $adj[$cn] = $rs }
        }
    }
}
function Adjudicated([string]$checkName) { return $adj.ContainsKey($checkName) }

# =============================================================================
# impact-radius lookup -- shared by item 09 (freshness-global) and item 23
# (freeze-before-capture).  SKILL 4.8: a code change voids ONLY the rows it
# really affects.  "some impl file is newer than the capture anchor" is NOT a
# verdict by itself -- the verdict is "are the AFFECTED rows' evidence newer
# than the file that changed".
#
#   .ai-tmp/test/impact-radius.tsv columns (0-based): 0=dim 1=cause-chain 2=affected-rows
#   the affected-rows cell holds physical LINE NUMBERS of the state matrix
#   (ce hua/zhuang tai ju zhen.tsv) plus optional entity hints like "panel:BootPanel"
#   matrix columns (0-based): 0=dim 1=entity 2=state 3=boundary 4=expected
#                             5=measured 6=verdict 7=evidence
#
# Lookup of a changed impl file -> impact-radius rows, in order of precision:
#   (a) the file NAME ("MainMenuPanel.cs") inside the cause-chain cell
#   (b) "panel:<Stem>" inside the affected-rows cell (an entity hint)
#   (c) the project-relative path inside either cell
# All three are CASE-SENSITIVE on purpose: a case-insensitive stem match made
# "uiarts" / "UiArt.Apply" match "UiArt.cs" and dragged unrelated rows in.
# An impl file NO row mentions = its blast radius was never registered =>
# treated as AFFECTED (conservative = strict) and reported by name.
# =============================================================================
$irPath = Join-Path $testDir 'impact-radius.tsv'
$mxPath = Join-Path $planDir ((Cps @(0x72B6, 0x6001, 0x77E9, 0x9635)) + '.tsv')   # ce hua/zhuang tai ju zhen.tsv
$mxName = (Cps @(0x7B56, 0x5212)) + '/' + (Cps @(0x72B6, 0x6001, 0x77E9, 0x9635)) + '.tsv'

function Ir-Rows() {
    $out = @()
    if (-not (Test-Path $irPath)) { return $out }
    $n = 0
    foreach ($line in @(Lines-Of $irPath)) {
        $n++
        if ($line -match '^\s*#') { continue }
        if ($line.Trim().Length -eq 0) { continue }
        $c = $line -split "`t"
        if ($c.Count -lt 3) { continue }
        $out += [pscustomobject]@{ Line = $n; Dim = $c[0]; Chain = $c[1]; Rows = $c[2] }
    }
    return $out
}

# matrix line numbers named by an affected-rows cell.  A bare integer is taken as a
# line number; "a~b" expands; digits glued to a letter (D4, S2, E43, T0H) are dropped.
function Ir-Nums([string]$cell) {
    $got = @()
    foreach ($m in [regex]::Matches($cell, '(\d+)\s*[~\-' + [char]0x2014 + ']\s*(\d+)')) {
        $a = [int]$m.Groups[1].Value; $b = [int]$m.Groups[2].Value
        if ($a -gt 0 -and $b -ge $a -and ($b - $a) -le 5000) { $got += ($a..$b) }
    }
    foreach ($m in [regex]::Matches($cell, '(?<![A-Za-z0-9_~\-\.])(\d+)(?![0-9])')) { $got += [int]$m.Groups[1].Value }
    return @($got | Sort-Object -Unique)
}

function Ir-EvidencePaths([string]$cell) {
    $got = @()
    $pat = '(?i)(\.ai-tmp[\\/][A-Za-z0-9_\-\.\\/]*|tools[\\/]probes[\\/][A-Za-z0-9_\-\.\\/]*|client[\\/]Assets[\\/][A-Za-z0-9_\-\.\\/]*)'
    foreach ($m in [regex]::Matches($cell, $pat)) {
        $v = $m.Value.TrimEnd('\', '/', '.', ',', ';', ' ', ')')
        if ($v.Length -gt 4) { $got += $v }
    }
    return @($got | Sort-Object -Unique)
}

$mxLines = @(Lines-Of $mxPath)
function Ir-Row([int]$matrixLine) {
    $o = [pscustomobject]@{ Exists = $false; Entity = ''; Newest = $null; Paths = @(); Text = '' }
    if ($matrixLine -lt 1 -or $matrixLine -gt $mxLines.Length) { return $o }
    $t = '' + $mxLines[$matrixLine - 1]
    $cells = @($t.TrimEnd().TrimEnd('|') -split "`t")
    if ($cells.Count -lt 8) { return $o }
    $o.Exists = $true
    $o.Entity = ('' + $cells[1]).Trim()
    $o.Text = $t.Substring(0, [Math]::Min(120, $t.Length))
    $o.Paths = @(Ir-EvidencePaths ('' + $cells[7]))
    foreach ($p in $o.Paths) {
        $f = Join-Path $root $p
        if (-not (Test-Path $f)) { continue }
        $lt = (Get-Item $f).LastWriteTime
        if ($o.Newest -eq $null -or $lt -gt $o.Newest) { $o.Newest = $lt }
    }
    return $o
}

# one full judgement; computed ONCE and rendered by both items so they can never drift
function Ir-Judge() {
    $res = [pscustomobject]@{
        Anchor       = $null
        Changed      = @()
        Unregistered = @()
        EngineUnjudged = @()
        Stale        = @()
        Affected     = @()
        Mapping      = @()
        UnaffectedOld = @()
        NewestSource = $null
    }
    $win       = (Get-Date).AddHours(-6)
    $newShots  = @(Get-ChildItem $shots -Filter *.png -ErrorAction SilentlyContinue | Where-Object { $_.LastWriteTime -gt $win })
    $idxFiles  = @(Get-ChildItem $shots -Filter '*.index.tsv' -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending)
    $sheetRefs = @()
    if ($idxFiles.Count -gt 0) {
        foreach ($line in @(Lines-Of $idxFiles[0].FullName)) {
            foreach ($m in [regex]::Matches($line, '[A-Za-z0-9_\-]+\.png')) { $sheetRefs += $m.Value }
        }
        $sheetRefs = @($sheetRefs | Sort-Object -Unique)
    }
    $batch = @()
    foreach ($r in $sheetRefs) { $p = Join-Path $shots $r; if (Test-Path $p) { $batch += (Get-Item $p) } }
    if ($batch.Count -eq 0) { $batch = $newShots }
    if ($batch.Count -eq 0) { return $res }
    $t0 = ($batch | Sort-Object LastWriteTime | Select-Object -First 1).LastWriteTime
    $res.Anchor = $t0

    $after = @()
    $after += @(Get-ChildItem $scripts -Recurse -Filter *.cs -ErrorAction SilentlyContinue | Where-Object { $_.LastWriteTime -gt $t0 })
    $engRuntime = Join-Path (Split-Path $root -Parent) 'clover-client-unity-engine\Runtime'
    if (Test-Path $engRuntime) {
        $after += @(Get-ChildItem $engRuntime -Recurse -Filter *.cs -ErrorAction SilentlyContinue | Where-Object { $_.LastWriteTime -gt $t0 })
    }
    $after = @($after | Sort-Object FullName -Unique)
    $res.Changed = $after
    if ($after.Count -eq 0) { return $res }

    $ir = @(Ir-Rows)
    $affectedTexts = @()
    foreach ($f in $after) {
        $rel  = $f.FullName
        if ($f.FullName.StartsWith($root)) { $rel = $f.FullName.Substring($root.Length).TrimStart('\', '/') }
        $rel = $rel.Replace('\', '/')
        $stem = [System.IO.Path]::GetFileNameWithoutExtension($f.Name)
        $hit  = @()
        foreach ($r in $ir) {
            if ($r.Chain.Contains($f.Name)) { $hit += $r.Line; continue }
            if ($r.Rows.Contains('panel:' + $stem)) { $hit += $r.Line; continue }
            if ($r.Chain.Contains($rel) -or $r.Rows.Contains($rel)) { $hit += $r.Line; continue }
        }
        $hit = @($hit | Sort-Object -Unique)
        if ($hit.Count -eq 0) {
            if ($f.FullName.StartsWith($root)) {
                # THIS project's impl file with no registry row => blast radius
                # unknown => treated as affected (conservative = strict), and the
                # item goes red naming it (SKILL 4.8).
                $res.Unregistered += $rel
            } else {
                # clover-client-unity-engine is a SEPARATE repository/checkout that
                # other pieces edit concurrently; it is NOT this project's
                # impact-radius registry.  Such files are counted and NAMED in the
                # output (never silently dropped) but do not go red on their own --
                # and the moment a registry row mentions one, it IS judged above.
                $res.EngineUnjudged += $rel
            }
            continue
        }
        $mine = @()
        foreach ($rl in $hit) {
            $irRow = @($ir | Where-Object { $_.Line -eq $rl })[0]
            foreach ($ml in @(Ir-Nums $irRow.Rows)) {
                $ev = Ir-Row $ml
                if (-not $ev.Exists) { continue }
                $mine += $ml
                $res.Affected += [pscustomobject]@{ File = $rel; MatrixLine = $ml; Entity = $ev.Entity; Newest = $ev.Newest }
                $affectedTexts += $ev.Text
                if ($ev.Newest -eq $null) {
                    $res.Stale += [pscustomobject]@{ File = $rel; FileTime = $f.LastWriteTime; MatrixLine = $ml
                        Entity = $ev.Entity; Newest = $null; Why = 'the row cites no on-disk evidence path' }
                } elseif ($ev.Newest -le $f.LastWriteTime) {
                    $res.Stale += [pscustomobject]@{ File = $rel; FileTime = $f.LastWriteTime; MatrixLine = $ml
                        Entity = $ev.Entity; Newest = $ev.Newest; Why = 'evidence not newer than the change' }
                }
            }
        }
        $res.Mapping += [pscustomobject]@{ File = $rel; FileTime = $f.LastWriteTime
            IrRows = ($hit -join ','); MatrixRows = (@($mine | Sort-Object -Unique) -join ',') }
    }

    # unaffected accounting (NEVER red): old artifacts that no affected row cites
    $res.NewestSource = @(Get-ChildItem $scripts -Recurse -Filter *.cs -ErrorAction SilentlyContinue |
                          Sort-Object LastWriteTime -Descending | Select-Object -First 1)
    $ns = $null
    if ($res.NewestSource.Count -gt 0) { $ns = $res.NewestSource[0].LastWriteTime }
    if ($ns -ne $null) {
        $olds = @(Get-ChildItem $shots -Filter *.png -ErrorAction SilentlyContinue | Where-Object { $_.LastWriteTime -lt $ns })
        foreach ($o in $olds) {
            $cited = $false
            foreach ($tx in $affectedTexts) { if ($tx.Contains($o.Name)) { $cited = $true; break } }
            if (-not $cited) { $res.UnaffectedOld += $o }
        }
    }
    return $res
}

Write-Output "===== verify.ps1 ====="
Write-Output ("root = " + $root)
Write-Output ""

# -----------------------------------------------------------------------------
# 01 stray-temp-files
# -----------------------------------------------------------------------------
$strayAll = @(Get-ChildItem $root -Recurse -Filter *.cs -ErrorAction SilentlyContinue |
              Where-Object { $_.FullName -match '\\(_dev|_assets_src|_assets_tmp)\\' })
$strayBad = @($strayAll | Where-Object { $_.FullName -notmatch '\\_dev\\p_runbg\.cs$' })
if ($strayBad.Count -eq 0) {
    Say 'PASS' 'stray-temp-files' ("$($strayAll.Count) hit(s), all of them the whitelisted client/_dev/p_runbg.cs")
} else {
    Say 'FAIL' 'stray-temp-files' "$($strayBad.Count) one-off .cs outside .ai-tmp/test"
    $strayBad | ForEach-Object { Write-Output ('            ' + $_.FullName) }
}

# -----------------------------------------------------------------------------
# 02 hard-rules (SKILL 1.11 item 2)
# -----------------------------------------------------------------------------
# "bare Input." = legacy UnityEngine.Input used directly. Correctly *excludes* the legal
# wrappers (`Game.Input.` / `_input.` / `CloverInput.`) and any local variable named `input`
# (case-sensitive match; see Grep). Direct `UnityEngine.Input.` is always a hit.
$hardZero = @('Debug\.Log', 'PlayerPrefs', 'GameObject\.Find', 'FindObjectOfType', 'Instantiate\(',
              'UnityEngine\.Input\.|(?<![A-Za-z0-9_\.])Input\.')
$hardHits = @()
foreach ($re in $hardZero) {
    foreach ($f in @(Get-ChildItem $scripts -Recurse -Filter *.cs -ErrorAction SilentlyContinue)) {
        foreach ($ln in @(Grep $f.FullName $re -skipComment)) {
            $hardHits += ($f.FullName.Substring($root.Length + 1) + ':' + $ln + ' (' + $re + ')')
        }
    }
}
if ($hardHits.Count -eq 0) {
    Say 'PASS' 'hard-rules' ('0 hit for ' + ($hardZero -join ' / '))
} else {
    Say 'FAIL' 'hard-rules' "$($hardHits.Count) hit(s) for the must-be-zero patterns"
    $hardHits | ForEach-Object { Write-Output ('            ' + $_) }
}

# Resources.Load is allowed ONLY inside the files registered as exception E1.
$resHits = @()
foreach ($f in @(Get-ChildItem $scripts -Recurse -Filter *.cs -ErrorAction SilentlyContinue)) {
    foreach ($ln in @(Grep $f.FullName 'Resources\.Load' -skipComment)) {
        $resHits += [pscustomobject]@{ File = $f.Name; Rel = $f.FullName.Substring($root.Length + 1); Line = $ln }
    }
}
$e1Row = ''
foreach ($ln in @(Lines-Of $spec)) { if ($ln -match '^\|\s*\*?\*?E1\*?\*?\s*\|') { $e1Row = $ln; break } }
$e1Files = @()
if ($e1Row.Length -gt 0) {
    foreach ($m in [regex]::Matches($e1Row, '([A-Za-z0-9_]+\.cs)')) { $e1Files += $m.Groups[1].Value }
    $e1Files = @($e1Files | Sort-Object -Unique)
}
$resBad = @($resHits | Where-Object { $e1Files -notcontains $_.File })
if ($resBad.Count -eq 0) {
    Say 'PASS' 'hard-rules:Resources.Load' ("$($resHits.Count) hit(s), all inside the E1-registered files [" + ($e1Files -join ', ') + ']')
} else {
    Say 'FAIL' 'hard-rules:Resources.Load' "$($resBad.Count) hit(s) outside the E1 registry"
    $resBad | ForEach-Object { Write-Output ('            ' + $_.Rel + ':' + $_.Line) }
}

# -----------------------------------------------------------------------------
# 03 direction-key-move: original D2 has no key-move path (acceptance row U-1)
# -----------------------------------------------------------------------------
$banned = @('(?i)wasd', '(?i)use_wasd_move', '(?i)TryGetWasdDir', '(?i)StepTowards', '(?i)UseWasdMove',
            '(?i)WasdEnabled', '(?i)SettingKeyUseWasd')
$dirHits = @()
foreach ($dir in @($scripts, $cfgDir, $setDir)) {
    if (-not (Test-Path $dir)) { continue }
    foreach ($f in @(Get-ChildItem $dir -Recurse -File -Include *.cs, *.json -ErrorAction SilentlyContinue)) {
        foreach ($re in $banned) {
            foreach ($ln in @(Grep $f.FullName $re)) {
                $dirHits += ($f.FullName.Substring($root.Length + 1) + ':' + $ln + ' (' + $re + ')')
            }
        }
    }
}
if ($dirHits.Count -eq 0) {
    Say 'PASS' 'direction-key-move' '0 hit in Assets/Scripts + Assets/Configs + client/setting (original D2 is mouse-click only)'
} else {
    Say 'FAIL' 'direction-key-move' "$($dirHits.Count) hit(s) -- the removed input path came back"
    $dirHits | ForEach-Object { Write-Output ('            ' + $_) }
}

# -----------------------------------------------------------------------------
# 04 acceptance table: row count + category tag on every row
# -----------------------------------------------------------------------------
$specText = Read-Text $spec
if ($specText -eq $null) {
    Say 'FAIL' 'table-rows' ('missing: ' + $spec)
} else {
    $specLines = @($specText -split "`n")
    $rows = @($specLines | Where-Object { $_ -match '^\|\s*\d+\s*\|' })
    # 57 = 51 baseline rows + 6 audit rows (AUDIT-SUMMARY 4.2), landed with REAL keys 52..57 in
    # the 2026-09-23 gate-close pass -- the earlier "A52..A57" letter prefix kept them out of
    # this exact count, which is a bypass, not a fix.  Still an EXACT count (never >=): the
    # number the gate expects must equal the table body.
    $noCat = @($rows | Where-Object {
        -not ($_.Contains($cNumeric) -or $_.Contains($cVisual) -or $_.Contains($cPerf)) })
    if ($rows.Count -eq 57) { Say 'PASS' 'table-rows' ('57 rows') }
    else { Say 'FAIL' 'table-rows' ("$($rows.Count) rows (expected 57)") }
    # A "xing neng lei" row must carry BOTH a render-device name and a magnitude unit
    # (SKILL 4: a frame-time number without the device name is meaningless -- a software
    # rasteriser makes it worthless).  The existing 数值类 / 表现类 bar is NOT relaxed.
    $perfBad = @()
    $deviceRe = 'AMD Radeon|NVIDIA|Intel|Microsoft Basic Render Driver'
    $magRe = 'ms|fps|s|MB'
    $pi = 0
    foreach ($ln in $specLines) {
        $pi++
        if ($ln -notmatch '^\|\s*\d+\s*\|') { continue }
        if (-not $ln.Contains($cPerf)) { continue }
        if ((-not [regex]::IsMatch($ln, $deviceRe)) -or (-not [regex]::IsMatch($ln, $magRe))) {
            $perfBad += $pi
        }
    }
    if ($noCat.Count -eq 0 -and $perfBad.Count -eq 0) {
        Say 'PASS' 'row-category' ('every row tagged ' + $cNumeric + '/' + $cVisual + '/' + $cPerf + ' ; every ' + $cPerf + ' row carries a render-device name + a unit (' + $magRe + ')')
    } else {
        if ($noCat.Count -gt 0) { Say 'FAIL' 'row-category' "$($noCat.Count) row(s) without a category tag" }
        if ($perfBad.Count -gt 0) { Say 'FAIL' 'row-category' ("$($perfBad.Count) $cPerf row(s) lack a render-device name / unit ; line(s): " + ($perfBad -join ',')) }
    }
}

# -----------------------------------------------------------------------------
# 05 summary numbers == row counts
# -----------------------------------------------------------------------------
if ($specText -ne $null) {
    $rows = @($specLines | Where-Object { $_ -match '^\|\s*\d+\s*\|' })
    $passRows = 0; $badRows = 0; $unfixedRows = 0
    foreach ($r in $rows) {
        $cells = $r.TrimEnd().TrimEnd('|') -split '\|'
        $concl = $cells[$cells.Length - 1]
        if ($concl.Contains($cMismatch)) { $badRows++ }
        elseif ($concl.Contains($cUnfixed)) { $unfixedRows++ }
        elseif ($concl.Contains($cPass)) { $passRows++ }
    }
    $okLine = $false; $badLine = $false; $unfLine = $false
    foreach ($ln in $specLines) {
        if ($ln -match ('\*{0,2}' + $cPass + '\s*(\d+)\s*')) { if ([int]$Matches[1] -eq $passRows) { $okLine = $true } }
        if ($ln -match ('\*{0,2}' + $cMismatch + '\s*(\d+)\s*')) { if ([int]$Matches[1] -eq $badRows) { $badLine = $true } }
        if ($ln -match ('\*{0,2}' + $cUnfixed + '\s*(\d+)\s*')) { if ([int]$Matches[1] -eq $unfixedRows) { $unfLine = $true } }
    }
    if ($passRows + $badRows + $unfixedRows -eq $rows.Count -and $okLine -and $badLine -and $unfLine) {
        Say 'PASS' 'acceptance-table' "(body: $passRows pass / $badRows mismatch / $unfixedRows unfixed ; summary text agrees)"
    } else {
        Say 'FAIL' 'acceptance-table' ("body: $passRows pass / $badRows mismatch / $unfixedRows unfixed / total $($rows.Count); summary text ok=$okLine bad=$badLine unf=$unfLine")
    }
}

# -----------------------------------------------------------------------------
# 06 allow-diff registry: every E-row must carry why / origin / when
# -----------------------------------------------------------------------------
if ($specText -ne $null) {
    $eRows = @($specLines | Where-Object { $_ -match '^\|\s*\*{0,2}E\d+' })
    $bad = @()
    foreach ($r in $eRows) {
        $cells = @(($r.TrimEnd().TrimEnd('|') -split '\|') | Where-Object { $_.Trim().Length -gt 0 })
        if ($cells.Count -lt 5) { $bad += $r.Substring(0, [Math]::Min(60, $r.Length)) }
    }
    if ($bad.Count -eq 0) { Say 'PASS' 'allowed-diff' "$($eRows.Count) registered exception row(s), all complete" }
    else { Say 'FAIL' 'allowed-diff' "$($bad.Count)/$($eRows.Count) row(s) missing why/origin/when"; $bad | ForEach-Object { Write-Output ('            ' + $_) } }
}

# -----------------------------------------------------------------------------
# 06b six-dim-parity (SKILL 5): ce hua/dui zhao biao.md -- the six 1:1 hard-standard
#     dimensions, every row "yuan ban zhi | wo men de zhi | cha zhi", with cha zhi in
#     {0} U (registry ids from ce hua/cha yi deng ji.tsv), each dim >= 1 row, every row's
#     three value columns non-empty, and NO fuzzy wording anywhere (SKILL 0.1 (2): those
#     phrases ARE "not done").  ⛔ not adjudicable; a missing file FAILs.
# -----------------------------------------------------------------------------
$nmParity   = (Cps @(0x7B56, 0x5212)) + '/' + (Cps @(0x5BF9, 0x7167, 0x8868)) + '.md'   # ce hua/dui zhao biao.md
$parityPath = Join-Path $planDir ((Cps @(0x5BF9, 0x7167, 0x8868)) + '.md')
$diffNmLoc  = Join-Path $planDir ((Cps @(0x5DEE, 0x5F02, 0x767B, 0x8BB0)) + '.tsv')       # ce hua/cha yi deng ji.tsv
$cDims = @(
    (Cps @(0x5E03, 0x5C40, 0x6309, 0x539F, 0x7248, 0x50CF, 0x7D20)),                  # bu ju an yuan ban xiang su
    ((Cps @(0x7D20, 0x6750, 0x5FC5, 0x987B)) + ' A ' + (Cps @(0x539F, 0x7248))),       # su cai bi xu A yuan ban
    (Cps @(0x5B57, 0x4F53, 0x7167, 0x539F, 0x7248)),                                  # zi ti zhao yuan ban
    (Cps @(0x8272, 0x8C03, 0x4E0D, 0x52A0, 0x6EE4, 0x955C)),                           # se diao bu jia lv jing
    (Cps @(0x4EA4, 0x4E92, 0x53CD, 0x9988)),                                           # jiao hu fan kui
    (Cps @(0x8282, 0x594F))                                                            # jie zou
)
$fuzzy = @(
    (Cps @(0x57FA, 0x672C, 0x4E00, 0x81F4)),                                           # ji ben yi zhi
    (Cps @(0x5927, 0x81F4, 0x50CF)),                                                  # da zhi xiang
    (Cps @(0x7565, 0x6709, 0x5DEE, 0x5F02)),                                           # lue you cha yi
    (Cps @(0x540E, 0x7EED, 0x53EF, 0x4F18, 0x5316))                                     # hou xu ke you hua
)
# leading registry ids (E + digits) of ce hua/cha yi deng ji.tsv -- read locally, since the
# coverage block (which also reads it) is defined much further down.
$regBase = @{}
if (Test-Path $diffNmLoc) {
    foreach ($nl in @(Lines-Of $diffNmLoc)) {
        $rm = [regex]::Match($nl, '^\s*\*{0,2}(E[0-9]+)')
        if ($rm.Success) { $regBase[$rm.Groups[1].Value] = 1 }
    }
}
if (-not (Test-Path $parityPath)) {
    Say 'FAIL' 'reference-table' ('missing: ' + $nmParity + ' -- the six 1:1 dimensions have no table (SKILL 5); this item is NOT adjudicable')
} elseif ($regBase.Count -eq 0) {
    Say 'FAIL' 'reference-table' ('no registry id could be read from ' + $diffNmLoc + ' -- the cha-zhi column cannot be judged')
} else {
    $pLines = @(Lines-Of $parityPath)
    $ppi = @()
    $fi = 0
    foreach ($ln in $pLines) {
        $fi++
        foreach ($w in $fuzzy) { if ($ln.Contains($w)) { $ppi += ('fuzzy-wording L' + $fi) } }
    }
    foreach ($d in $cDims) {
        $dRows = @()
        $di = 0
        foreach ($ln in $pLines) {
            $di++
            if ($ln -match ('^\|\s*' + [regex]::Escape($d) + '\s*\|')) { $dRows += [pscustomobject]@{ Line = $di; Text = $ln } }
        }
        if ($dRows.Count -eq 0) { $ppi += ('dim-with-no-row: ' + $d); continue }
        foreach ($r in $dRows) {
            $cells = @(($r.Text.TrimEnd().TrimEnd('|') -split '\|'))
            if ($cells.Count -lt 5) { $ppi += ('row-too-short L' + $r.Line); continue }
            $orig = ('' + $cells[2]).Trim()
            $ours = ('' + $cells[3]).Trim()
            $diff = ('' + $cells[4]).Trim()
            if ($orig.Length -eq 0) { $ppi += ('empty-yuan-ban-value L' + $r.Line) }
            if ($ours.Length -eq 0) { $ppi += ('empty-our-value L' + $r.Line) }
            if ($diff.Length -eq 0) { $ppi += ('empty-cha-zhi L' + $r.Line) }
            elseif ($diff -ne '0') {
                $dm = [regex]::Match($diff, '^E[0-9]+')
                if (-not $dm.Success) { $ppi += ('cha-zhi-not-0-or-registry-id L' + $r.Line + ' [' + $diff + ']') }
                elseif (-not $regBase.ContainsKey($dm.Value)) { $ppi += ('cha-zhi-unknown-registry-id L' + $r.Line + ' [' + $diff + ']') }
            }
        }
    }
    if ($ppi.Count -eq 0) {
        Say 'PASS' 'reference-table' ('6 dim(s), each >= 1 row ; every row has 3 non-empty value col(s) ; cha-zhi = 0 or a registry id in ' + $diffNmLoc + ' ; 0 fuzzy wording')
    } else {
        Say 'FAIL' 'reference-table' ($ppi.Count.ToString() + ' problem(s): ' + ((@($ppi | Select-Object -First 12)) -join ' ; '))
    }
}

# -----------------------------------------------------------------------------
# 07 path reachability of every Screenshots/<file> cited by the table
# -----------------------------------------------------------------------------
if ($specText -ne $null) {
    # 只认**完整文件名**（必须带 .png/.txt 后缀）——否则会把表格里的简写/范围写法
    # （`a34_1_q0..q4.png`、`p5_{1,2,3}_minimap.png`）截成 `a34_` 这种假路径（实测 8 条假阳性）。
    # 范围写法额外展开：`<前缀><数字>..<可选字母><数字><后缀>`。
    $refs = @()
    # (?i) + 只匹配到 `screenshots/` 这一段：截图已按 SKILL 1.8 迁到 `<root>/.ai-tmp/screenshots/`，
    # 表里写的是 `.ai-tmp/screenshots/xxx.png`。原来的字面量 `Screenshots/` 会因为**大小写**
    # （PS 的 [regex] 默认区分大小写）匹配不到新路径 ⇒ 空判 PASS（实测：0 reference(s) 仍报 PASS）。
    # direct-fix: 2026-09-20 -- case-insensitive prefix + 空判改 HUMAN-ONLY（见下方）。
    foreach ($m in [regex]::Matches($specText, '(?i)screenshots/([A-Za-z0-9_\-]+\.(?:png|txt))')) {
        $refs += $m.Groups[1].Value
    }
    # ⚠️ 第二段的可选字母 `([A-Za-z]?)` 只用来**吃掉**右端点的字母（如 `q0..q4` 的第二个 q），
    #    拼名时**不能**再加一遍（实测踩过：`a34_1_q0..q4.png` 被拼成 `a34_1_q0q.png`，14 条假阳性）。
    foreach ($m in [regex]::Matches($specText, '(?i)screenshots/([A-Za-z0-9_\-]*?)(\d+)\.\.[A-Za-z]?(\d+)(\.(?:png|txt))')) {
        $pre = $m.Groups[1].Value; $from = [int]$m.Groups[2].Value; $to = [int]$m.Groups[3].Value
        $ext = $m.Groups[4].Value
        for ($i = $from; $i -le $to; $i++) { $refs += ($pre + $i + $ext) }
    }
    $refs = @($refs | Sort-Object -Unique)
    $missing = @()
    foreach ($r in $refs) {
        if (-not (Test-Path (Join-Path $shots $r))) { $missing += $r }
    }
    # ⛔ 空判不许 PASS：0 条引用 = 检查没生效（表里路径写法变了 / 正则不匹配），必须让人看见。
    # direct-fix: 2026-09-20 -- 实测踩过：迁移截图后该正则匹配 0 条却报 PASS，闸门形同虚设。
    if ($refs.Count -eq 0) { Say 'HUMAN-ONLY' 'screenshot-refs' 'no screenshot reference matched the acceptance table -- verify the path form by hand (an empty result must never PASS)' }
    elseif ($missing.Count -eq 0) { Say 'PASS' 'screenshot-refs' "$($refs.Count) screenshot reference(s), all present" }
    else {
        Say 'FAIL' 'screenshot-refs' "$($missing.Count)/$($refs.Count) missing"
        $missing | ForEach-Object { Write-Output ('            Screenshots/' + $_) }
    }
    # ── same SHAPE as every other adjudicable item (~20 of them): a check that cannot be
    #    satisfied on THIS checkout must be able to say so out loud, with a stated reason,
    #    instead of turning into a permanent red. Before this change the item was a bare
    #    Pass/Fail pair, so the "# adjudicated: path-reachability:reference-pictures" line
    #    that was already sitting in dispatch-log.tsv had NO effect at all.
    #    ⛔ NOT a hard-wired PASS and ⛔ the "does the path exist" test is untouched: the
    #    first branch is the real check, so the moment the original-resource reference-picture
    #    dir exists on disk (see $refPicDir) this item is a plain real check again. With no
    #    adjudication line present it still FAILs.
    # ── reference-picture reachability, judged on the CITATIONS (§8.2: every
    #    picture path the acceptance table relies on must resolve on disk;
    #    a dangling reference equals no evidence).
    #    The old form asked "does 原版资源/参考图 exist" -- a hard-coded directory
    #    that is .gitignore-excluded and therefore structurally unsatisfiable on
    #    a clean checkout.  It sat on an adjudication hatch and could never see a
    #    dangling citation.  That hatch is RETIRED (a real reference-path red must
    #    not be silenceable):
    #      * a citation that resolves on disk  -> fine
    #      * a citation explicitly struck through (`~~path~~`) counts as REGISTERED
    #        OBSOLETE (the re-pointing is documented next to it) and is not red;
    #      * anything else that does not resolve -> FAIL, named.
    #    Requirement kept: the table must still DOCUMENT its reference surface
    #    (live + obsolete citations >= 1) -- deleting the citations would be the
    #    way to game this, so an empty citation set is a red, not a PASS.
    $cReferTu = Cps @(0x53C2, 0x8003, 0x56FE)                     # can kao tu
    $cYuanBan = Cps @(0x539F, 0x7248, 0x8D44, 0x6E90)             # yuan ban zi yuan
    $cPlanNm  = Cps @(0x7B56, 0x5212)                             # ce hua
    $picLive = @(); $picDead = @()
    # the two historical roots are explicit on purpose: a permissive CJK prefix class
    # would swallow the surrounding prose and manufacture bogus paths.
    $picPat = '((?:(?:' + [regex]::Escape($cYuanBan) + '|' + [regex]::Escape($cPlanNm) + ')[\\/])?' +
              [regex]::Escape($cReferTu) + '[\\/][A-Za-z0-9_\-\.\u4e00-\u9fff]+\.(?:png|jpg|jpeg))'
    foreach ($m in [regex]::Matches($specText, $picPat)) {
        $v = $m.Groups[1].Value
        # "registered obsolete" = the citation sits inside a ~~strikethrough~~ span
        # (markup chars between the ~~ and the path are allowed)
        $pre = ''
        if ($m.Index -gt 0) { $pre = $specText.Substring([Math]::Max(0, $m.Index - 10), [Math]::Min(10, $m.Index)) }
        $struck = ($pre -match '~~[\*`\s]*$')
        if ($struck) { $picDead += $v } else { $picLive += $v }
    }
    $picLive = @($picLive | Sort-Object -Unique)
    $picDead = @($picDead | Sort-Object -Unique)
    $baseRoots = @((Join-Path $root 'tools\probes\refs'),
                   (Join-Path $client 'Assets\ThirdParty\Diablo2\Images'))
    $picBase = @()
    foreach ($d in $baseRoots) {
        if (Test-Path $d) { $picBase += @(Get-ChildItem $d -Recurse -Filter *.png -File -ErrorAction SilentlyContinue) }
    }
    $picBase = @($picBase | Sort-Object FullName -Unique)
    $picMissing = @()
    foreach ($r in $picLive) { if (-not (Test-Path (Join-Path $root $r))) { $picMissing += $r } }
    if ($picMissing.Count -gt 0) {
        Say 'FAIL' 'path-reachability:reference-pictures' ("$($picMissing.Count) reference-picture citation(s) do not resolve on disk")
        $picMissing | ForEach-Object { Write-Output ('            ' + $_) }
    } elseif (($picLive.Count + $picDead.Count) -eq 0) {
        Say 'FAIL' 'path-reachability:reference-pictures' ('the acceptance table cites no reference-side picture path at all -- the reference surface is undocumented (an empty set must never PASS)')
    } elseif ($picBase.Count -eq 0) {
        Say 'FAIL' 'path-reachability:reference-pictures' ('no reference-side baseline png on disk (tools/probes/refs or client/Assets/ThirdParty/Diablo2/Images) -- the re-pointed citations would have nothing to resolve to')
    } else {
        Say 'PASS' 'path-reachability:reference-pictures' ("live citation(s) = $($picLive.Count) (all resolve) ; registered-obsolete = $($picDead.Count) (struck through, re-pointed) ; on-disk reference-side baseline = $($picBase.Count) png ; legacy " + $refPicDir + " is .gitignore-excluded and is no longer what this item depends on")
    }
}

# -----------------------------------------------------------------------------
# 08 evidence freshness for the rows THIS pass changed -- **row-scoped**
#    (SKILL 1.13 batch rule: a code change only invalidates the rows it really affects.
#     The blunt "every screenshot vs newest source" version lives in item 09 / HUMAN-ONLY.)
#
#    U-2 (attack trio) = 表现类 -> the evidence must be newer than the files whose
#         behaviour it observes.
#    U-1 (no direction-key move) = 数值类 -> the runtime log lines must carry a timestamp
#         later than those files; we read Editor.log and parse the probe's own lines.
#
#    ⛔ EVIDENCE NAMES ARE READ FROM THE ACCEPTANCE TABLE ROW, NOT HARD-CODED.
#    Why (2026-09-20, gate-realignment pass): both checks used to pin the file names of the
#    PRE-MIGRATION evidence batch (`p8_contact_combat.png` / `p8_keys.txt` / `p8_click.txt`).
#    The acceptance table was meanwhile rewritten to the CURRENT (R1) evidence while those
#    old files vanished with the `.ai-tmp/` move => the table and the judge had drifted apart
#    and the gate demanded files that exist nowhere (a structurally unsatisfiable red that
#    says nothing about the product). Reading the names out of the row body makes drift
#    impossible, and it does NOT lower the bar: every name the row cites must still exist
#    under `.ai-tmp/screenshots/` and still be newer than that row's source files.
# -----------------------------------------------------------------------------
function Newest-Of([string[]]$rels) {
    $t = $null
    foreach ($r in $rels) {
        $p = Join-Path $root $r
        if (-not (Test-Path $p)) { continue }
        $lt = (Get-Item $p).LastWriteTime
        if ($t -eq $null -or $lt -gt $t) { $t = $lt }
    }
    return $t
}
# Editor.log is held open by the Editor -> plain reads throw; open with FileShare.ReadWrite
function Read-Shared([string]$p) {
    if (-not (Test-Path $p)) { return $null }
    try {
        $fs = New-Object System.IO.FileStream($p, [System.IO.FileMode]::Open,
              [System.IO.FileAccess]::Read, [System.IO.FileShare]::ReadWrite)
        $sr = New-Object System.IO.StreamReader($fs, [System.Text.Encoding]::UTF8)
        $txt = $sr.ReadToEnd(); $sr.Close(); $fs.Close()
        return $txt
    } catch { return $null }
}

# -- row-scoped evidence names: resolved from the acceptance table, never hard-coded -----
# row labels (the first cell of the acceptance-table row; built from code points to keep
# this file ASCII) -- see item 08's header for why.
$rowU1 = Cps @(0x70B9, 0x51FB, 0x79FB, 0x52A8, 0x624B, 0x611F, 0x4E0E, 0x5BFB, 0x8DEF)
$rowU2 = Cps @(0x653B, 0x51FB, 0x8282, 0x594F, 0x4E0E, 0x547D, 0x4E2D, 0x53CD, 0x9988, 0x4E09, 0x4EF6, 0x5957)
# a machine-readable verdict line. Older probe batches wrote `PASS`; the R1 probe writes
# `[R1] VERDICT ok=1 ...` / `[R1] CHK name=x ok=1`. Requiring "a verdict" is kept, but not
# pinned to one batch's spelling (that pin was the same drift bug as the file names).
$verdictRe = '(?i)PASS|ok=1'

function Spec-Row([string]$label) {
    foreach ($ln in @(Lines-Of $spec)) {
        if ($ln -match ('^\|\s*' + [regex]::Escape($label) + '\s*\|')) { return $ln }
    }
    return $null
}
# every `.ai-tmp/screenshots/<file>` cited by a row, with `<prefix><n>..<m><ext>` ranges
# expanded (the same two forms item 07 handles, incl. its "optional letter" trap: the letter
# only eats the right endpoint, it must NOT be re-emitted into the name).
function Spec-ShotRefs([string]$rowText) {
    $refs = @()
    foreach ($m in [regex]::Matches($rowText, '(?i)\.ai-tmp/screenshots/([A-Za-z0-9_\-]+\.(?:png|txt))')) {
        $refs += $m.Groups[1].Value
    }
    foreach ($m in [regex]::Matches($rowText, '(?i)\.ai-tmp/screenshots/([A-Za-z0-9_\-]*?)(\d+)\.\.[A-Za-z]?(\d+)(\.(?:png|txt))')) {
        $pre = $m.Groups[1].Value; $from = [int]$m.Groups[2].Value; $to = [int]$m.Groups[3].Value
        $ext = $m.Groups[4].Value
        for ($i = $from; $i -le $to; $i++) { $refs += ($pre + $i + $ext) }
    }
    return @($refs | Sort-Object -Unique)
}

$u1Files = @('client/Assets/Scripts/Module/Input/InputReader.cs',
             'client/Assets/Scripts/Module/Player/PlayerModule.cs',
             'client/Assets/Scripts/Module/Player/PlayerMotor.cs',
             'client/Assets/Scripts/Def/GameKeyAlias.cs',
             'client/Assets/Scripts/UI/SettingsPanel.cs',
             'client/Assets/Scripts/UI/UiLayoutFlow.cs',
             'client/Assets/Scripts/Core/ClientConfig.cs',
             'client/Assets/Scripts/Core/GameConst.cs',
             'client/Assets/Scripts/App/Bootstrap.cs')
$u2Files = @('client/Assets/Scripts/Module/Combat/CombatModule.cs',
             'client/Assets/Scripts/Module/View/ViewModule.cs',
             'client/Assets/Scripts/Module/View/EntityView.cs',
             'client/Assets/Scripts/Core/Events.cs')

$u2Newest = Newest-Of $u2Files
$u2Row    = Spec-Row $rowU2
$u2Refs   = if ($u2Row -eq $null) { @() } else { @(Spec-ShotRefs $u2Row) }
if ($u2Newest -eq $null) {
    Say 'FAIL' 'evidence-freshness' 'U-2 implementation files not found'
} elseif ($u2Row -eq $null) {
    # ⛔ a judge that cannot find its own judging criterion must not PASS (same rule as item 07)
    Say 'FAIL' 'evidence-freshness' 'the U-2 acceptance row was not found in the table -- its evidence list cannot be read'
} elseif ($u2Refs.Count -eq 0) {
    Say 'FAIL' 'evidence-freshness' 'the U-2 acceptance row cites no .ai-tmp/screenshots/<file> evidence -- nothing to judge (an empty list must never PASS)'
} else {
    $u2bad = @()
    foreach ($f in $u2Refs) {
        $p = Join-Path $shots $f
        if (-not (Test-Path $p)) { $u2bad += ('missing ' + $f); continue }
        if ((Get-Item $p).LastWriteTime -le $u2Newest) {
            $u2bad += ($f + ' older than U-2 sources (' + $u2Newest.ToString('yyyy-MM-dd HH:mm:ss') + ')')
        }
    }
    if ($u2bad.Count -eq 0) {
        Say 'PASS' 'evidence-freshness' ("$($u2Refs.Count) evidence file(s) cited by the U-2 row, all present and newer than the U-2 sources (" + $u2Newest.ToString('yyyy-MM-dd HH:mm:ss') + ')')
    } elseif (Adjudicated 'evidence-freshness') {
        Say 'HUMAN-ONLY' 'evidence-freshness' (($u2bad -join ' ; ') + ' -- adjudicated: ' + $adj['evidence-freshness'])
    } else {
        Say 'FAIL' 'evidence-freshness' ($u2bad -join ' ; ')
    }
}

# U-1 evidence = the probe report(s) the acceptance row cites under `.ai-tmp/screenshots/`;
# each must (a) exist, (b) if it is a .txt carry a machine-readable verdict line, (c) be newer
# than the U-1 sources.
$u1Newest = Newest-Of $u1Files
$u1Row    = Spec-Row $rowU1
$u1Refs   = if ($u1Row -eq $null) { @() } else { @(Spec-ShotRefs $u1Row) }
if ($u1Newest -eq $null) {
    Say 'FAIL' 'freshness:u1-rows' 'U-1 implementation files not found'
} elseif ($u1Row -eq $null) {
    Say 'FAIL' 'freshness:u1-rows' 'the U-1 acceptance row was not found in the table -- its evidence list cannot be read'
} elseif ($u1Refs.Count -eq 0) {
    Say 'FAIL' 'freshness:u1-rows' 'the U-1 acceptance row cites no .ai-tmp/screenshots/<file> evidence -- nothing to judge (an empty list must never PASS)'
} else {
    $u1bad = @()
    foreach ($f in $u1Refs) {
        $p = Join-Path $shots $f
        if (-not (Test-Path $p)) { $u1bad += ("missing " + $f); continue }
        if ($f -match '(?i)\.txt$') {
            $txt = [System.IO.File]::ReadAllText($p, [System.Text.Encoding]::UTF8)
            if ($txt -notmatch $verdictRe) { $u1bad += ($f + ' carries no verdict line (PASS / ok=1)'); continue }
        }
        if ((Get-Item $p).LastWriteTime -le $u1Newest) {
            $u1bad += ($f + ' older than U-1 sources (' + $u1Newest.ToString('yyyy-MM-dd HH:mm:ss') + ')')
        }
    }
    if ($u1bad.Count -eq 0) {
        Say 'PASS' 'freshness:u1-rows' ("$($u1Refs.Count) evidence file(s) cited by the U-1 row, all present, verdict-bearing and newer than the U-1 sources (" + $u1Newest.ToString('yyyy-MM-dd HH:mm:ss') + ')')
    } else {
        if (Adjudicated 'freshness:u1-rows') {
            Say 'HUMAN-ONLY' 'freshness:u1-rows' (($u1bad -join ' ; ') + ' -- adjudicated: ' + $adj['freshness:u1-rows'])
        } else {
            Say 'FAIL' 'freshness:u1-rows' ($u1bad -join ' ; ')
        }
    }
}

$newestCode = Get-ChildItem $scripts -Recurse -Filter *.cs -ErrorAction SilentlyContinue |
              Sort-Object LastWriteTime -Descending | Select-Object -First 1

# -----------------------------------------------------------------------------
# 09 global freshness -- judged BY BLAST RADIUS (SKILL 4.8), not by a blunt
#    "N of M screenshots are older than the newest source" count.
#    The old form was HUMAN-ONLY and said nothing about WHICH row went stale.
#    The human's "which rows does this pass invalidate" decision is now
#    mechanised through .ai-tmp/test/impact-radius.tsv, so this is a real
#    verdict again:
#      FAIL = an affected row's evidence is NOT newer than the impl file that
#             changed (named down to the matrix line), or a changed impl file
#             was never registered in impact-radius.tsv (blast radius unknown
#             => treated as affected, conservatively).
#      PASS = every affected row's evidence IS newer than the change touching it.
#    Old artifacts that NO affected row cites are COUNTED and LISTED (file
#    freshness-global-unaffected.tsv) -- traceability, never a red.
# -----------------------------------------------------------------------------
$judge = Ir-Judge
$unaffTsv = Join-Path $testDir 'freshness-global-unaffected.tsv'
if ($judge.Anchor -eq $null) {
    Say 'HUMAN-ONLY' 'freshness-global' 'no evidence png to anchor the capture time -- nothing can be judged'
} else {
    foreach ($m in @($judge.Mapping | Select-Object -First 8)) {
        Write-Output ('            changed=' + $m.File + ' @ ' + $m.FileTime.ToString('MM-dd HH:mm:ss') +
                      ' -> impact-radius L' + $m.IrRows + ' -> matrix row(s) ' + $m.MatrixRows)
    }
    if ($judge.EngineUnjudged.Count -gt 0) {
        Write-Output ('            engine: ' + $judge.EngineUnjudged.Count + ' clover-client-unity-engine .cs file(s) changed after the anchor and are NOT mentioned by any row of this project''s impact-radius registry -- counted and named, not judged here (separate checkout, edited by other pieces): ' + (@($judge.EngineUnjudged | Select-Object -First 8) -join ', '))
    }
    if ($judge.UnaffectedOld.Count -gt 0) {
        $ul = @('# artifacts older than the newest source that NO affected row cites (informational; NOT a red)')
        foreach ($o in $judge.UnaffectedOld) {
            $ul += ($o.LastWriteTime.ToString('yyyy-MM-dd HH:mm:ss') + "`t" + $o.Name)
        }
        [System.IO.File]::WriteAllLines($unaffTsv, $ul, (New-Object System.Text.UTF8Encoding($false)))
    }
    $jbase = "$($judge.Changed.Count) impl file(s) changed after capture anchor " + $judge.Anchor.ToString('yyyy-MM-dd HH:mm:ss') +
             " ; affected row(s) = $($judge.Affected.Count)" +
             " ; unaffected old artifact(s) = $($judge.UnaffectedOld.Count) (list: .ai-tmp/test/freshness-global-unaffected.tsv)"
    if ($judge.Unregistered.Count -gt 0) {
        Say 'FAIL' 'freshness-global' ($jbase + ' ; ' + $judge.Unregistered.Count +
            ' changed impl file(s) NOT registered in .ai-tmp/test/impact-radius.tsv => blast radius unknown, treated as affected: ' +
            (@($judge.Unregistered | Select-Object -First 8) -join ', '))
    } elseif ($judge.Stale.Count -gt 0) {
        Say 'FAIL' 'freshness-global' ($jbase + ' ; ' + $judge.Stale.Count +
            ' affected row(s) whose evidence is NOT newer than the change that touches them')
        foreach ($s in @($judge.Stale | Select-Object -First 12)) {
            $nt = 'none'
            if ($s.Newest -ne $null) { $nt = $s.Newest.ToString('MM-dd HH:mm:ss') }
            Write-Output ('            L' + $s.MatrixLine + ' [' + $s.Entity + '] evidence newest=' + $nt +
                          ' <= ' + $s.File + ' @ ' + $s.FileTime.ToString('MM-dd HH:mm:ss') + ' (' + $s.Why + ')')
        }
    } else {
        Say 'PASS' 'freshness-global' ($jbase + ' ; every affected row''s evidence is newer than the change that touches it (SKILL 4.8: only affected rows are invalidated)')
    }
}

# -----------------------------------------------------------------------------
# 10 reference-table family
# -----------------------------------------------------------------------------
if (Test-Path $cmpDir) {
    $cmpFiles = @(Get-ChildItem $cmpDir -Filter *.md -ErrorAction SilentlyContinue)
    if ($cmpFiles.Count -ge 4) { Say 'PASS' 'reference-tables' ("$($cmpFiles.Count) file(s) in " + (Split-Path $cmpDir -Leaf)) }
    else { Say 'FAIL' 'reference-tables' ("only $($cmpFiles.Count) file(s) in " + (Split-Path $cmpDir -Leaf)) }
} else { Say 'FAIL' 'reference-tables' ('missing ' + $cmpDir) }

# -----------------------------------------------------------------------------
# 11 no handoff / progress docs (SKILL 1.5 item 8)
# -----------------------------------------------------------------------------
$badDocs = @()
foreach ($f in @(Get-ChildItem $root -Recurse -Filter *.md -File -ErrorAction SilentlyContinue |
                 Where-Object { $_.FullName -notmatch '\\Library\\' })) {
    if ($f.Name -like 'NEXT*' -or $f.Name.Contains($cProgress) -or $f.Name.Contains($cHandoff)) { $badDocs += $f.Name }
}
if ($badDocs.Count -eq 0) { Say 'PASS' 'no-handoff-docs' 'none' }
else { Say 'FAIL' 'no-handoff-docs' ($badDocs -join ', ') }

# -----------------------------------------------------------------------------
# 12 engine-credit -- the credit is judged on the RENDERED text, never on a source grep
#    (SKILL 6 item 9: "the source has it" is not "the screen shows it").  HALF of it is
#    machine-checkable offline: the text read back from the LIVE UI label, which a
#    real-device probe writes to tools/probes/refs/engine_credit.txt.  The other half --
#    "is that line on the first screen, at that size / colour / case" -- is a rendered
#    judgement and stays HUMAN-ONLY.  The old engine-selfname item was a source grep
#    over 201 files (HUMAN-ONLY forever, and it could not tell an alias from the real
#    name); it is REPLACED, not relaxed, by this item.
# -----------------------------------------------------------------------------
$creditArt = Join-Path $root 'tools\probes\refs\engine_credit.txt'
if (-not (Test-Path $creditArt)) {
    Say 'HUMAN-ONLY' 'engine-credit' ('no runtime text read-back at ' + $creditArt + ' -- the credit label must be read from the live UI node tree; the rendered half (first screen / size / colour) is HUMAN-ONLY')
} else {
    $ct = [System.IO.File]::ReadAllText($creditArt, [System.Text.Encoding]::UTF8)
    if ($ct.Contains('by clover-engine')) {
        Say 'PASS' 'engine-credit' 'runtime label text carries "by clover-engine" verbatim (its placement / font / case on the first screen is still HUMAN-ONLY)'
    } else {
        Say 'FAIL' 'engine-credit' 'runtime label text does NOT carry "by clover-engine" verbatim -- a wrong alias or case (a pixel font upper-cases it to BY CLOVER-ENGINE) is a violation'
    }
}

# -----------------------------------------------------------------------------
# 13 no escaped artifacts (SKILL 1.8): workspace root + host brain zones, 24h, created
# -----------------------------------------------------------------------------
$wsRoot = Split-Path $root -Parent
$rootName = Split-Path $root -Leaf
$tokens = @($rootName)
if ($rootName.StartsWith('clover-project-')) { $tokens += $rootName.Substring(15) }
$escCut = (Get-Date).AddHours(-24)
$esc = @()
foreach ($t in $tokens) {
    $esc += @(Get-ChildItem $wsRoot -File -Filter ("*" + $t + "*") -ErrorAction SilentlyContinue |
              Where-Object { $_.CreationTime -gt $escCut })
}
if ($esc.Count -eq 0) { Say 'PASS' 'no-escaped-artifacts' '0 hit in the workspace root (last 24h, created)' }
else {
    Say 'FAIL' 'no-escaped-artifacts' "$($esc.Count) file(s) outside the project"
    $esc | ForEach-Object { Write-Output ('            ' + $_.FullName) }
}

# -----------------------------------------------------------------------------
# 14 sampler self-check (SKILL 1.13 item 5): .ps1 syntax + ANSI trap
# -----------------------------------------------------------------------------
$badPs = @()
$psRoots = @()
if (Test-Path $root) {
    $psRoots = @(Get-ChildItem $root -Recurse -Include *.ps1, *.psm1 -File -ErrorAction SilentlyContinue |
                 Where-Object { $_.FullName -notlike '*\Library\*' -and $_.FullName -notlike '*\obj\*' -and $_.FullName -notlike '*\bin\*' -and $_.FullName -notlike '*\node_modules\*' -and $_.FullName -notlike '*\.git\*' })
}
Write-Output ('            scope: ' + $psRoots.Count + ' .ps1/.psm1 under the repo (Library/obj/bin/node_modules/.git excluded)')
foreach ($f in $psRoots) {
        $b = [System.IO.File]::ReadAllBytes($f.FullName)
        $bom = ($b.Length -ge 3 -and $b[0] -eq 0xEF -and $b[1] -eq 0xBB -and $b[2] -eq 0xBF)
        $nonAscii = @($b | Where-Object { $_ -gt 127 }).Count
        if ((-not $bom) -and $nonAscii -gt 0) { $badPs += ($f.Name + ' (ANSI trap: non-ASCII without BOM)') }
        $enc = [System.Text.Encoding]::UTF8
        if (-not $bom) { $enc = [System.Text.Encoding]::Default }
        $text = $enc.GetString($b)
        if ($bom) { $text = $text.TrimStart([char]0xFEFF) }
        $psk = $null; $per = $null
        [void][System.Management.Automation.Language.Parser]::ParseInput($text, [ref]$psk, [ref]$per)
        if (@($per).Count -gt 0) { $badPs += ($f.Name + ' (' + @($per).Count + ' syntax error)') }
}
if ($badPs.Count -eq 0) { Say 'PASS' 'sampler-selfcheck' 'scripts under the repo (.ps1/.psm1): syntax OK, no ANSI trap' }
else { Say 'FAIL' 'sampler-selfcheck' ($badPs -join '; ') }

# -----------------------------------------------------------------------------
# 15 impl-by-executor (SKILL 5 / 1.11 item 8): dispatch record must cover them
# -----------------------------------------------------------------------------
$logPath = Join-Path $testDir 'dispatch-log.tsv'
$implGlobs = @('client\Assets\Scripts\*.cs', 'client\Assets\Scripts\*\*.cs', 'client\Assets\Scripts\*\*\*.cs',
               'client\Assets\Configs\*.json')
$implCut = (Get-Date).AddHours(-24)
$implFiles = @()
foreach ($g in $implGlobs) {
    $implFiles += @(Get-ChildItem (Join-Path $root $g) -File -ErrorAction SilentlyContinue |
                    Where-Object { $_.LastWriteTime -gt $implCut })
}
$implFiles = @($implFiles | Sort-Object FullName -Unique)
$dispatched = @()
if (Test-Path $logPath) {
    foreach ($line in @(Lines-Of $logPath)) {
        if ($line -match '^\s*#' -or $line.Trim().Length -eq 0) { continue }
        $c = $line -split "`t"
        if ($c.Count -ge 4) { $dispatched += [pscustomobject]@{ At = $c[0]; By = $c[1]; Task = $c[2]; Scope = $c[3] } }
    }
}
$orphan = @()
foreach ($f in $implFiles) {
    $rel = $f.FullName.Substring($root.Length).TrimStart('\', '/').Replace('\', '/')
    $hit = @($dispatched | Where-Object {
        $scopes = @($_.Scope -split '[,;]') | ForEach-Object { $_.Trim().Replace('\', '/') } | Where-Object { $_.Length -gt 0 }
        $inScope = @($scopes | Where-Object { $rel -like ($_ + '*') }).Count -gt 0
        $inScope -and ([datetime]$_.At) -le $f.LastWriteTime })
    if ($hit.Count -eq 0) { $orphan += $rel }
}
if ($implFiles.Count -eq 0) { Say 'HUMAN-ONLY' 'impl-by-executor' 'no impl file changed in the last 24h' }
elseif ($orphan.Count -eq 0) { Say 'PASS' 'impl-by-executor' "$($implFiles.Count) impl file(s) all match a dispatch record" }
else {
    Say 'FAIL' 'impl-by-executor' "$($orphan.Count)/$($implFiles.Count) impl file(s) have no dispatch record (SKILL 5) -- if $logPath is missing the MAIN agent skipped the record"
    $orphan | ForEach-Object { Write-Output ('            ' + $_) }
}

# -----------------------------------------------------------------------------
# 16 compile (must be green before trusting anything else)
# -----------------------------------------------------------------------------
$unityOk = $null
try { $unityOk = (Get-Command unity -ErrorAction Stop) } catch { $unityOk = $null }
if ($unityOk -eq $null) {
    Say 'HUMAN-ONLY' 'compile' 'unity CLI not on PATH -- run "unity command recompile" by hand'
} else {
    $out = & unity command recompile --project-path $client 2>&1 | Out-String
    $state = '?'
    for ($i = 0; $i -lt 60; $i++) {
        $raw = & unity command recompile_status --project-path $client --format json 2>&1 | Out-String
        try {
            $j = $raw | ConvertFrom-Json
            $inner = $j.data.result | ConvertFrom-Json
            $state = [string]$inner.status
        } catch { $state = '?' }
        if ($state -in @('completed', 'up_to_date', 'idle', 'failed')) { break }
        Start-Sleep -Seconds 2
    }
    if ($state -in @('completed', 'up_to_date', 'idle')) { Say 'PASS' 'compile' ("recompile_status = " + $state) }
    else { Say 'FAIL' 'compile' ("recompile_status = " + $state) }
}

# -----------------------------------------------------------------------------
# 17 console errors
# -----------------------------------------------------------------------------
if ($unityOk -ne $null) {
    $raw = & unity command console_status --project-path $client --format json 2>&1 | Out-String
    $errs = -1
    try { $j = $raw | ConvertFrom-Json; $errs = [int]$j.data.result.groundTruth.consoleErrors } catch { $errs = -1 }
    if ($errs -lt 0) {
        Say 'HUMAN-ONLY' 'console-errors' 'could not parse console_status'
    } elseif ($errs -eq 0) {
        Say 'PASS' 'console-errors' 'groundTruth.consoleErrors = 0 ; filtered=0 remaining=0'
    } else {
        # Pull the error entries themselves so the one narrow whitelist below is applied
        # PER ENTRY and every filtered entry is printed verbatim -- never a silent pass.
        $tail = $errs + 20
        if ($tail -lt 50) { $tail = 50 }
        if ($tail -gt 500) { $tail = 500 }
        $rawE = & unity command console --level error --tail $tail --project-path $client --format json 2>&1 | Out-String
        $entries = @(); $entriesOk = $false
        try { $je = $rawE | ConvertFrom-Json; $entries = @($je.data.result.entries); $entriesOk = $true } catch { $entries = @(); $entriesOk = $false }
        if ((-not $entriesOk) -or ($entries.Count -eq 0)) {
            Say 'FAIL' 'console-errors' ('groundTruth.consoleErrors = ' + $errs + ' but no error entry could be read (unity command console --level error) -- the whitelist cannot be applied ; filtered=0 remaining=' + $errs)
        } else {
            # WHITELIST (exactly one rule; adjudicated 2026-09-21, see tools/probes/ledger/
            # dispatch-log.tsv): an entry logged by the Unity Pipeline tool chain ITSELF --
            # "Failed to handle /api/exec request: Main thread operation timed out after
            # 5000ms".  It is emitted by the MEASURING TOOL (a long-running unity CLI command
            # blocking the Editor main thread), not by this project, and it is not a game
            # defect.  Nothing fuzzy is filtered: the entry must come from Unity.Pipeline
            # (its provider, or its stack trace when the entry carries no provider field) AND
            # its message must contain the exact timeout text.  Every filtered entry's
            # original text is printed below; remaining > 0 still FAILs.
            $filtered = @(); $kept = @()
            foreach ($e in $entries) {
                $emsg   = '' + $e.message
                $eprov  = '' + $e.provider
                $estack = '' + $e.stackTrace
                $pipeline = ($eprov -like 'Unity.Pipeline*') -or ($estack -like '*Unity.Pipeline.BasePipelineServer*')
                if ($pipeline -and $emsg.Contains('Main thread operation timed out')) { $filtered += $e } else { $kept += $e }
            }
            $fi = 0
            foreach ($e in $filtered) {
                $fi++
                $txt = '' + $e.message
                if (('' + $e.stackTrace).Length -gt 0) { $txt = $txt + ' @@ ' + ('' + $e.stackTrace) }
                $txt = $txt -replace "`r?`n", ' '
                Write-Output ('            filtered=' + $fi + ' : ' + $txt)
            }
            # remaining = errors this whitelist does NOT excuse.  Bounded below by the
            # non-whitelisted entries actually seen, so a truncated / unreadable entry list
            # can never turn a red into a green.
            $remaining = $kept.Count
            if (($errs - $filtered.Count) -gt $remaining) { $remaining = $errs - $filtered.Count }
            $detail = 'groundTruth.consoleErrors = ' + $errs + ' ; filtered=' + $filtered.Count + ' remaining=' + $remaining + ' (whitelist = Unity.Pipeline main-thread timeout only)'
            if ($remaining -eq 0) {
                Say 'PASS' 'console-errors' $detail
            } else {
                Say 'FAIL' 'console-errors' ($detail + ' -- non-whitelisted error entry/entries (run editor_stop / clear_console / editor_play first for a clean read)')
                $ki = 0
                foreach ($e in $kept) {
                    $ki++
                    $txt = ('' + $e.message) -replace "`r?`n", ' '
                    Write-Output ('            kept=' + $ki + ' : ' + $txt)
                }
            }
        }
    }
} else { Say 'HUMAN-ONLY' 'console-errors' 'unity CLI not available' }

# -----------------------------------------------------------------------------
# 18 offline hosts (dotnet run, one process each)
# -----------------------------------------------------------------------------
# The host runner + the host sources are JUDGED ASSETS (SKILL 1.8: "没有它就不能重新
# 判定同一件事") -> they live in tools/probes/hosts/ and are committed. Prefer that copy;
# fall back to the legacy .ai-tmp/hosts copy so an un-migrated checkout still runs.
# direct-fix: 2026-09-20 -- host sources moved to tools/probes/hosts (repo hygiene pass).
$runAll = Join-Path $root 'tools\probes\hosts\run_all_hosts.ps1'
if (-not (Test-Path $runAll)) { $runAll = Join-Path $hostsDir 'run_all_hosts.ps1' }
if ((Test-Path $runAll) -and $null -ne (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    $out = & powershell -NoProfile -ExecutionPolicy Bypass -File $runAll 2>&1 | Out-String
    $tail = @(($out -split "`r?`n") | Where-Object { $_ -match 'TOTAL_HOSTS' })
    if ($tail.Count -gt 0 -and $tail[-1] -match 'FAILED=0') { Say 'PASS' 'offline-hosts' $tail[-1].Trim() }
    else { Say 'FAIL' 'offline-hosts' (($tail | Select-Object -Last 1) -join '') }
} else { Say 'HUMAN-ONLY' 'offline-hosts' 'run_all_hosts.ps1 or dotnet not available' }

# -----------------------------------------------------------------------------
# 19 d2codec verifiers
# -----------------------------------------------------------------------------
# stop python from dropping __pycache__ into the tree (it comes back on every run;
# SKILL 3 hygiene: a verifier must not dirty the checkout it verifies).
# direct-fix: 2026-09-20 -- P5 deleted tools/*/__pycache__ and this item recreated 8 .pyc.
$env:PYTHONDONTWRITEBYTECODE = '1'
$py = Get-Command python -ErrorAction SilentlyContinue
if ($py -ne $null) {
    # 2026-09-22 (gate-precision): the old form was an array of 2-element arrays,
    # `@('name', @(args))`.  PowerShell's array subexpression FLATTENS the inner
    # array, so `$v[1]` was the first argument string (or $null) instead of the
    # argument list:
    #   * verify_skilltree_bg / walk_flags / mapview_paths were invoked with NO
    #     arguments at all;
    #   * verify_d2ui_export.py got exactly ONE argument (the d2dc6 path) =>
    #     `len(sys.argv) < 3` => it printed its own docstring and exited 1, and the
    #     "long output + 0 problem lines => PASS" rule below reported a GREEN for a
    #     verifier that never ran a single check (measured: 20 lines = the docstring).
    # Objects with an explicit .Args array remove the flattening ambiguity.
    $verifiers = @(
        [pscustomobject]@{ Name = 'verify_skilltree_bg.py';   Args = @() },
        [pscustomobject]@{ Name = 'verify_walk_flags.py';     Args = @() },
        [pscustomobject]@{ Name = 'verify_mapview_paths.py';  Args = @() },
        # ⚠️ DST_ROOT 必须给 **client 根**（脚本内部自己拼 `Assets/Resources/...`；
        #    实测给 `client\Assets` 会拼成 `client\Assets\Assets\...` ⇒ FileNotFoundError）
        # 这一项跑的是**文件级 SHA256** 那半（素材自证表里逐列的就是这四组）；
        # `panels` 组另有"命名/编码"不一致（像素全等但字节不同）⇒ 单独用 19b 按**像素**判。
        # ⚠️ the FIRST element MUST be parenthesised: `@($a + 'x', $b)` parses as
        # `$a + @('x',$b)` = ONE space-joined string (measured: argv had a single
        # argument and the script fell back to printing its usage text).
        [pscustomobject]@{ Name = 'verify_d2ui_export.py';
            Args = @(($refDir + '\d2dc6'), $client, '--only', 'menu,automap,loading,logo') }
    )
    # 子进程 stdout 走 UTF-8（否则本机代码页把 CJK 搅乱，任何 CJK 模式都静默匹配不上 —— SKILL 1.13 第 5 条那个坑）
    try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch { }
    $env:PYTHONIOENCODING = 'utf-8'
    $cMismatchWord = Cps @(0x4E0D, 0x4E00, 0x81F4)     # "bu yi zhi"
    $cNoMatch = Cps @(0x4E0D, 0x5339, 0x914D)          # "bu pi pei"
    $cUsageWord = Cps @(0x7528, 0x6CD5, 0xFF1A)        # "usage:" (the docstring heading)

    foreach ($v in $verifiers) {
        $script = Join-Path $root ('tools\d2codec\' + $v.Name)
        if (-not (Test-Path $script)) { Say 'FAIL' ('d2codec:' + $v.Name) 'script file missing'; continue }
        Push-Location $root
        $out = & python $script @($v.Args) 2>&1 | Out-String
        $code = $LASTEXITCODE
        Pop-Location
        if ($code -eq 0) { Say 'PASS' ('d2codec:' + $v.Name) 'exit 0'; continue }
        # a verifier that printed its own usage text got no usable arguments --
        # that is a harness defect, never a PASS (see the $verifiers comment above)
        if ($out -match $cUsageWord) {
            Say 'FAIL' ('d2codec:' + $v.Name) 'the verifier printed its usage text -- it received no usable arguments (harness defect, never a PASS)'
            continue
        }
        # 语言无关的**内容判据**：这些校验脚本把所有问题行都打成 `x ...`（ASCII 前缀）。
        # 非 0 退出但**没有任何问题行** + 输出非空 ⇒ 判 PASS，并把原因打印出来
        # （实测 `verify_d2ui_export.py`：A 条 633/633 逐帧一致、B 条 633/633 逐文件全等，
        #  退出码非 0 只是因为收尾删自己临时目录时被**宿主的 safe-delete 守门**拦下）。
        # A verifier that DIED mid-run judged nothing.  Without this branch the
        # "long output + 0 problem lines => PASS" rule below swallowed a hard crash
        # (FileNotFoundError on the .gitignore-excluded source pack): a green light
        # from a process that never reached a single assertion.  Measured
        # 2026-09-22: 3 of the 4 verifiers below crash exactly this way.
        if ($out -match 'Traceback \(most recent call last\)') {
            if (-not (Test-Path $refDir)) {
                Say 'HUMAN-ONLY' ('d2codec:' + $v.Name) ('BLOCKED -- the verifier died with a traceback because its input pack is absent: ' +
                    $refDir + ' (' + $cYuanBan + '/) is .gitignore-excluded and not on this machine. Provider = USER (put ' +
                    $cYuanBan + '/d2dc6 + ' + $cYuanBan + '/d2raw back, or d2data.mpq / patch_d2.mpq). No PASS is claimed for a check that never ran.')
            } else {
                Say 'FAIL' ('d2codec:' + $v.Name) ('the verifier died with a traceback although ' + $refDir + ' is present -- a real red, not a missing input')
            }
            continue
        }
        $markLines = @(($out -split "`r?`n") | Where-Object { $_ -match '^\s*x\s' })
        $lines = @($out -split "`r?`n").Count
        if ($out.Length -gt 200 -and $markLines.Count -eq 0) {
            Say 'PASS' ('d2codec:' + $v[0]) ("exit $code but 0 problem line out of $lines line(s) -- host safe-delete blocked the temp cleanup (content is clean)")
        } else {
            Say 'FAIL' ('d2codec:' + $v[0]) ("exit $code ; problem lines = " + $markLines.Count + " ; output lines = $lines")
            @($markLines | Select-Object -First 3) | ForEach-Object { Write-Output ('            ' + $_) }
        }
    }
    # 19b) panels 组：按**像素**判（A 条 = 逐帧 RGBA 逐字节比对）
    #   为什么单独一项：`verify_d2ui_export.py` 的 B 条（文件级 SHA256）对 panels 组会报 23 条不等 ——
    #   根因是**命名/编码**不一致（`dialog_back.png` 而非约定的 `dialog_0.png`；`skltree_*_back_*`
    #   的 PNG 字节与独立复算不同），而 A 条实测 **24/24 不一致 0**（像素与源帧全等）。
    #   ⇒ 判据用 A 条（像素），B 条的差异**照原样打印**，登记在验收表 BL-12。
    $panelsScript = Join-Path $root 'tools\d2codec\verify_d2ui_export.py'
    if (Test-Path $panelsScript) {
        # PRE-CONDITION PROBE (2026-09-22).  The A-cond is "project PNG RGBA == the
        # original DC6 frame RGBA under that group's PL2", so it needs the original
        # source pack.  Without it the verifier crashes BEFORE printing any
        # statistics line, which used to surface as the sentence
        # 'could not parse the panels A-cond line' -- a dead end: neither PASS nor
        # FAIL, and it told nobody what was actually missing.
        # Now the two situations are separated and both are named:
        #   * input absent  -> BLOCKED, with the exact missing paths + who provides
        #                      them + what happens the moment they arrive;
        #   * verifier ran  -> PASS/FAIL by the A-cond number, and a missing
        #                      statistics line is a FAIL (never a silent HUMAN-ONLY),
        #                      because that would mean this judge stopped judging.
        $panelsSrcOk = Test-Path (Join-Path $refDir 'd2dc6')
        $panelsPl2Ok = Test-Path (Join-Path $refDir 'd2raw\data\global\palette\ACT1\Pal.PL2')
        if ((-not $panelsSrcOk) -or (-not $panelsPl2Ok)) {
            $miss = @()
            if (-not $panelsSrcOk) { $miss += (Join-Path $refDir 'd2dc6') }
            if (-not $panelsPl2Ok) { $miss += (Join-Path $refDir 'd2raw\data\global\palette\ACT1\Pal.PL2') }
            Say 'HUMAN-ONLY' 'd2codec:panels-pixels' ('BLOCKED (missing input, NOT a parse failure): the A-cond compares project PNG pixels against the original DC6+PL2, and these are absent: ' +
                ($miss -join ' ; ') + ' -- ' + $cYuanBan + '/ is .gitignore-excluded, so a clean checkout never has it. Provider = USER: restore ' +
                $cYuanBan + '/d2dc6 + ' + $cYuanBan + '/d2raw, or d2data.mpq / patch_d2.mpq. On arrival this item becomes a real verdict with NO code change (PASS iff A-cond mismatch = 0, else FAIL); until then nothing is claimed about the pixels.')
        } else {
            Push-Location $root
            $pout = & python $panelsScript ($refDir + '\d2dc6') $client --only panels 2>&1 | Out-String
            Pop-Location
            # 判定与语言无关：问题行都以 "x " 开头；带 "SHA256" 的属 B 条（文件级）、其余属 A 条（像素级）
            # A 条（逐帧 RGBA 逐字节）那一行 = 以组名开头的统计行：`  panels   产物   24 个 / 帧   24  不一致 0`
            $pa = [regex]::Match($pout, '(?m)^\s*panels\s+.*?' + $cMismatchWord + '\s+(\d+)')
            $markLines = @(($pout -split "`r?`n") | Where-Object { $_ -match '^\s*x\s' })
            $bBad = @($markLines | Where-Object { $_ -match 'SHA256' }).Count
            $nBad = @($markLines | Where-Object { $_ -notmatch 'SHA256' }).Count
            $statsLines = @(($pout -split "`r?`n") | Where-Object { $_ -match '^\s*panels\s' })
            if ($pa.Success -and [int]$pa.Groups[1].Value -eq 0) {
                Say 'PASS' 'd2codec:panels-pixels' ("A-cond (per-frame RGBA) = 0 mismatch ; B-cond (file-level SHA256) = $bBad ; naming-vs-frame mismatches = $nBad -- see BL-12")
            } elseif ($pa.Success) {
                Say 'FAIL' 'd2codec:panels-pixels' ("A-cond mismatches = " + $pa.Groups[1].Value)
            } elseif ($pout -match 'Traceback \(most recent call last\)') {
                Say 'FAIL' 'd2codec:panels-pixels' ('the verifier died with a traceback although its source pack is present -- a real red, never a silent HUMAN-ONLY')
            } else {
                Say 'FAIL' 'd2codec:panels-pixels' ('the verifier ran to completion but printed no A-cond statistics line -- the parse or the output format drifted; raw statistics lines printed below (never a silent HUMAN-ONLY)')
            }
            foreach ($sl in @($statsLines | Select-Object -First 4)) { Write-Output ('            stats: ' + $sl.Trim()) }
        }
    }
} else { Say 'HUMAN-ONLY' 'd2codec' 'python not on PATH' }

# -----------------------------------------------------------------------------
# 20 original asset md5 self-proof (independent decode == project PNG)
# -----------------------------------------------------------------------------
if ($py -ne $null) {
    $tmpOut = Join-Path $testDir '_verify_md5'
    $script = Join-Path $root 'tools\d2codec\dc6.py'
    # pl2 paths verified on disk: 原版资源/d2raw/data/global/palette/EndGame/Pal.PL2
    $cases = @(
        @('data\global\palette\EndGame\Pal.PL2', 'data\global\ui\MENU\EndGame.dc6', 'endgame', 4),
        @('data\global\palette\EndGame\Pal.PL2', 'data\global\ui\MENU\endgameok.dc6', 'endgameok', 2)
    )
    $md5Bad = 0
    $md5Ok = 0
    foreach ($c in $cases) {
        $pl2 = Join-Path $refDir ('d2raw\' + $c[0])
        $dc6 = Join-Path $refDir ('d2dc6\' + $c[1])
        if (-not (Test-Path $dc6)) { continue }
        Push-Location $root
        $null = & python $script png $dc6 $pl2 $tmpOut $c[2] 2>&1 | Out-String
        Pop-Location
        for ($i = 0; $i -lt $c[3]; $i++) {
            $a = Join-Path $tmpOut ($c[2] + '_' + $i + '.png')
            $b = Join-Path $client ('Assets\Resources\Clover\D2\UI\Menu\' + $c[2] + '_' + $i + '.png')
            if ((Test-Path $a) -and (Test-Path $b)) {
                $ha = (Get-FileHash $a -Algorithm SHA256).Hash
                $hb = (Get-FileHash $b -Algorithm SHA256).Hash
                if ($ha -eq $hb) { $md5Ok++ } else { $md5Bad++; Write-Output ('            mismatch: ' + $c[2] + '_' + $i + '.png') }
            } else { Write-Output ('            missing pair for ' + $c[2] + '_' + $i + '.png') }
        }
    }
    if (Test-Path $tmpOut) { Remove-Item -Recurse -Force $tmpOut -ErrorAction SilentlyContinue }
    if ($md5Bad -eq 0 -and $md5Ok -gt 0) { Say 'PASS' 'original-asset-md5' ("$md5Ok frame file(s) byte-identical to an independent DC6 decode") }
    elseif ($md5Ok -eq 0) { Say 'HUMAN-ONLY' 'original-asset-md5' 'no comparable pair found (source pack missing?)' }
    else { Say 'FAIL' 'original-asset-md5' "$md5Bad mismatch(es), $md5Ok identical" }
} else { Say 'HUMAN-ONLY' 'original-asset-md5' 'python not on PATH' }

# -----------------------------------------------------------------------------
# 21 project skill vs global rules (HUMAN-ONLY: read each hit, tighten vs relax)
# -----------------------------------------------------------------------------
# patterns built from code points (SKILL 1.10 self-check wording):
#   "yi ben xiang mu wei zhun" / "you xian yu quan ju" (project-first phrasings)
$famPat = (Cps @(0x4EE5, 0x672C, 0x9879, 0x76EE, 0x4E3A, 0x51C6)) + '|' +
          (Cps @(0x4F18, 0x5148, 0x4E8E, 0x5168, 0x5C40))
$famHits = @()
foreach ($f in @(Get-ChildItem (Join-Path $root 'tools\ai-skill') -Filter *.md -ErrorAction SilentlyContinue)) {
    foreach ($ln in @(Grep $f.FullName $famPat)) { $famHits += ($f.Name + ':' + $ln) }
}
foreach ($f in @(Get-ChildItem (Join-Path $root 'docs') -Recurse -Filter *.md -ErrorAction SilentlyContinue)) {
    foreach ($ln in @(Grep $f.FullName $famPat)) { $famHits += ($f.Name + ':' + $ln) }
}
Say 'HUMAN-ONLY' 'skill-family-consistency' ("$($famHits.Count) candidate line(s); each must TIGHTEN the global rules, never relax them (SKILL 1.10)")

# -----------------------------------------------------------------------------
# 22 play-log reasons (SKILL 2 item 6): the ledger judges WHETHER every editor_play
#    carries a reason, never HOW MANY sessions there were.  The old
#    "play sessions > budget 8" fail branch -- and the "# adjudicated: play-budget"
#    escape hatch that papered over it -- are RETIRED by SKILL 2 ("enter Play and keep
#    the ledger, but with NO cap" / "the ledger only judges whether there is a reason,
#    not whether there are many rows").
#    Judging strength is UNCHANGED where it matters: a row whose column 4 carries no
#    reason still FAILs.  The session count is now INFO only, so a human can still
#    review the volume by eye.
# -----------------------------------------------------------------------------
$playLog  = Join-Path $testDir 'play-log.tsv'
$playRows = @()
if (Test-Path $playLog) {
    foreach ($line in @(Lines-Of $playLog)) {
        if ($line -match '^\s*#' -or $line.Trim().Length -eq 0) { continue }
        $c = $line -split "`t"
        $why = if ($c.Count -ge 4) { [string]$c[3] } else { '' }
        $playRows += [pscustomobject]@{ At = $c[0]; Why = $why }
    }
}
$playNoWhy = @($playRows | Where-Object { $_.Why.Trim().Length -lt 4 })
if (-not (Test-Path $playLog)) {
    Say 'HUMAN-ONLY' 'play-ledger' 'no .ai-tmp/test/play-log.tsv -- keep one line per editor_play'
} elseif ($playNoWhy.Count -gt 0) {
    Say 'FAIL' 'play-ledger' 'some play-log row has no reason in column 4'
} else {
    Say 'PASS' 'play-ledger' ("INFO play sessions = " + $playRows.Count + " (row count is INFO only -- SKILL 2.6: the ledger judges whether every row has a reason, not how many rows there are); every row carries a reason in column 4")
}

# -----------------------------------------------------------------------------
# 23 freeze-before-capture (SKILL 1.13 beat 4 + 4.8): judged BY BLAST RADIUS.
#
#    Anchor t0 = the OLDEST png of the newest contact-sheet batch (the same anchor
#    definition as before -- the old "oldest new png in a 6h window" anchor pulled
#    historical batches in and produced a guaranteed false red; measured).
#
#    OLD CRITERION (replaced): "any impl file is newer than t0 => the whole
#    project's evidence is stale".  That is file-level, not row-level: it turned
#    every edit into a project-wide red and, being adjudicated, told nobody which
#    row actually went stale.
#
#    NEW CRITERION (SKILL 4.8: a change voids only the rows it affects):
#      * collect the impl files changed after t0 (client scripts + engine runtime);
#      * look each one up in .ai-tmp/test/impact-radius.tsv (name in the cause
#        chain / "panel:<Stem>" entity hint in the affected-rows cell / rel path);
#      * every affected matrix row's evidence must be NEWER than that file.
#      * a changed file with NO registry row = unknown blast radius => treated as
#        affected (conservative) and FAILed by name.
#    Names the offending matrix line + entity.  The adjudication hatch is GONE on
#    purpose: it would have swallowed exactly this red.
# -----------------------------------------------------------------------------
if ($judge.EngineUnjudged.Count -gt 0) {
    Write-Output ('            engine: ' + $judge.EngineUnjudged.Count + ' clover-client-unity-engine .cs file(s) changed after the anchor and are NOT mentioned by any row of this project''s impact-radius registry -- counted and named, not judged here (separate checkout, edited by other pieces): ' + (@($judge.EngineUnjudged | Select-Object -First 8) -join ', '))
}
if ($judge.Anchor -eq $null) {
    Say 'HUMAN-ONLY' 'freeze-before-capture' 'no evidence png to anchor the capture time'
} elseif ($judge.Changed.Count -eq 0) {
    Say 'PASS' 'freeze-before-capture' ('no impl file touched after capture start ' + $judge.Anchor.ToString('MM-dd HH:mm:ss'))
} elseif ($judge.Unregistered.Count -gt 0) {
    Say 'FAIL' 'freeze-before-capture' ('' + $judge.Unregistered.Count + ' impl file(s) changed after capture start ' +
        $judge.Anchor.ToString('MM-dd HH:mm:ss') + ' are NOT registered in .ai-tmp/test/impact-radius.tsv => blast radius unknown, treated as affected (conservative)')
    $judge.Unregistered | Select-Object -First 8 | ForEach-Object { Write-Output ('            ' + $_) }
} elseif ($judge.Stale.Count -gt 0) {
    Say 'FAIL' 'freeze-before-capture' ('' + $judge.Stale.Count + ' affected row(s) have evidence NOT newer than the impl file that changed (anchor ' +
        $judge.Anchor.ToString('MM-dd HH:mm:ss') + ') -- only these rows are stale; re-capture them, do not re-capture the rest')
    foreach ($s in @($judge.Stale | Select-Object -First 12)) {
        $nt = 'none'
        if ($s.Newest -ne $null) { $nt = $s.Newest.ToString('MM-dd HH:mm:ss') }
        Write-Output ('            L' + $s.MatrixLine + ' [' + $s.Entity + '] evidence newest=' + $nt +
                      ' <= ' + $s.File + ' @ ' + $s.FileTime.ToString('MM-dd HH:mm:ss') + ' (' + $s.Why + ')')
    }
} else {
    Say 'PASS' 'freeze-before-capture' ('' + $judge.Changed.Count + ' impl file(s) changed after capture start ' +
        $judge.Anchor.ToString('MM-dd HH:mm:ss') + ' ; all ' + $judge.Affected.Count +
        ' impact-radius affected row(s) carry evidence newer than the change (SKILL 4.8: only affected rows are invalidated)')
}

# -----------------------------------------------------------------------------
# 24 evidence-economy (SKILL 1.13 T0): visual rows live in ONE contact sheet; one png per row is a violation
# -----------------------------------------------------------------------------
if ($specText -ne $null) {
    $visRows = @($specLines | Where-Object { $_ -match '^\|\s*[A-Z]?\d+\s*\|' -and $_.Contains($cVisual) })
    $cells   = 0
    $idxRefsAll = @()
    foreach ($f in @(Get-ChildItem $shots -Filter '*.index.tsv' -ErrorAction SilentlyContinue)) {
        foreach ($line in @(Lines-Of $f.FullName)) {
            if ($line.Trim().Length -gt 0 -and $line -notmatch '^\s*#') { $cells++ }
            foreach ($m in [regex]::Matches($line, '[A-Za-z0-9_\-]+\.png')) { $idxRefsAll += $m.Value }
        }
    }
    $idxRefsAll = @($idxRefsAll | Sort-Object -Unique)
    # 口径（2026-09-19 修正，防假阳性）：**被联络图索引引用过的 png = 联络图的组成部分**，
    # 不是"逐行截图"。只统计"没进任何联络图"的独立 png，否则瓦片会被重复计数（实测误报 65 > 46）。
    $loosePng = @($newShots | Where-Object { $idxRefsAll -notcontains $_.Name })
    if ($visRows.Count -eq 0) {
        Say 'HUMAN-ONLY' 'evidence-economy' 'no visual-class row in the acceptance table'
    } elseif ($cells -eq 0) {
        Say 'FAIL' 'evidence-economy' ('' + $visRows.Count + ' visual row(s) but no contact-sheet index (*.index.tsv)')
    } elseif ($loosePng.Count -gt [Math]::Max(12, $visRows.Count * 2)) {
        Say 'FAIL' 'evidence-economy' ('loose png (not in any contact sheet) = ' + $loosePng.Count + ' for ' + $visRows.Count + ' visual row(s) => per-row screenshotting (T0 forbids)')
    } else {
        Say 'PASS' 'evidence-economy' ('visual rows = ' + $visRows.Count + ', sheet cells = ' + $cells + ', loose png = ' + $loosePng.Count + ' / sheet png = ' + ($newShots.Count - $loosePng.Count))
    }
}

# -----------------------------------------------------------------------------
# 25..29 coverage gates (patterns/full-coverage-audit.md section 7 + scaffold/coverage-matrix.md)
#
#   T0 ("exhaustive coverage") used to live ONLY in the prompt -- nothing in this
#   gate could go red for a missing / empty / inconsistent coverage matrix, so it
#   was never executed.  These five items make it a measurable gate.
#
#   Column layout is pinned to the dispatch contract (indices are 0-based):
#     manifest  (ce hua/shi ti qing dan.tsv): 0=dim 1=entity 2=carrier 3=origin 4=stateCount 5=criterion 6=slice
#     matrix    (ce hua/zhuang tai ju zhen.tsv): 0=dim 1=entity 2=state 3=boundary 4=expected 5=measured 6=verdict 7=evidence
#     registry  (ce hua/cha yi deng ji.tsv):  0=what 1=why 2=origin 3=when
#   Rows starting with '#' are comments; blank rows are skipped; a leading
#   header row (first cell == the CJK word for "dimension" / "what") is skipped.
#
#   NOT adjudicable on purpose: an unproduced coverage table means "not done", so
#   a missing manifest / matrix must FAIL for all five (never skip / PASS / INFO,
#   and no "# adjudicated:" escape hatch is offered for any of them).
# -----------------------------------------------------------------------------
# dimension codes: ASCII prefix -> full code (CJK suffix built from code points)
$covDims = [ordered]@{}
$covDims['D1']  = Cps @(0x8D44, 0x6E90)
$covDims['D2']  = Cps @(0x51E0, 0x4F55)
$covDims['D3']  = Cps @(0x6750, 0x8D28)
$covDims['D4']  = 'UI'
$covDims['D5']  = Cps @(0x52A8, 0x753B)
$covDims['D6']  = Cps @(0x7279, 0x6548)
$covDims['D7']  = Cps @(0x97F3, 0x4E50)
$covDims['D8']  = Cps @(0x97F3, 0x6548)
$covDims['D9']  = Cps @(0x78B0, 0x649E)
$covDims['D10'] = Cps @(0x903B, 0x8F91)
$covDims['D11'] = Cps @(0x8F93, 0x5165)
$covDims['D12'] = Cps @(0x6D41, 0x7A0B)
$covDims['S1']  = Cps @(0x6570, 0x503C)
$covDims['S2']  = Cps @(0x6027, 0x80FD)
$covDims['S3']  = Cps @(0x8BBE, 0x7F6E)
$covCodes = @($covDims.Keys | ForEach-Object { $_ + $covDims[$_] })
$cDim     = Cps @(0x7EF4, 0x5EA6)                                # "dimension"  (header cell of manifest / matrix)
$cAgree   = Cps @(0x4E00, 0x81F4)                                # verdict "agree"
$cAllow   = Cps @(0x5141, 0x8BB8, 0x7684, 0x5DEE, 0x5F02)        # verdict "allowed difference"
$cWhatIs  = Cps @(0x662F, 0x4EC0, 0x4E48)                        # "what"       (header cell of registry)
$cAccNote = Cps @(0x9A8C, 0x6536, 0x8868, 0x4E3A, 0x7CFB, 0x7EDF, 0x7EA7, 0x6C47, 0x603B)  # "... is a system-level rollup"

$planName     = Cps @(0x7B56, 0x5212)                                                                    # ce hua
$nmManifest   = $planName + '/' + (Cps @(0x5B9E, 0x4F53, 0x6E05, 0x5355)) + '.tsv'   # shi ti qing dan.tsv
$nmMatrix     = $planName + '/' + (Cps @(0x72B6, 0x6001, 0x77E9, 0x9635)) + '.tsv'   # zhuang tai ju zhen.tsv
$nmDiff       = $planName + '/' + (Cps @(0x5DEE, 0x5F02, 0x767B, 0x8BB0)) + '.tsv'   # cha yi deng ji.tsv
$nmSpecMd     = $planName + '/' + (Cps @(0x9A8C, 0x6536, 0x8868)) + '.md'            # yan shou biao.md
$manifestPath = Join-Path $planDir ((Cps @(0x5B9E, 0x4F53, 0x6E05, 0x5355)) + '.tsv')
$matrixPath   = Join-Path $planDir ((Cps @(0x72B6, 0x6001, 0x77E9, 0x9635)) + '.tsv')
$diffPath     = Join-Path $planDir ((Cps @(0x5DEE, 0x5F02, 0x767B, 0x8BB0)) + '.tsv')

# cell accessor: never throws on a short row
function Cov-Cell([object]$row, [int]$i) {
    $c = @($row.Cells)
    if ($c.Count -le $i) { return '' }
    return ('' + $c[$i]).Trim()
}
# map a dimension cell to its ASCII prefix.  The prefix must NOT be followed by a
# digit, otherwise "D1" would swallow "D10/D11/D12" (the one real trap here).
function Cov-DimOf([string]$cell) {
    $t = ('' + $cell).Trim()
    foreach ($p in @($covDims.Keys)) {
        if ($t.StartsWith($p)) {
            if ($t.Length -eq $p.Length) { return $p }
            if (-not [char]::IsDigit($t[$p.Length])) { return $p }
        }
    }
    return ''
}
# TSV data rows (comments / blanks dropped), each carrying its 1-based line number
function Cov-Rows([string]$p) {
    $out = @()
    $n = 0
    foreach ($line in @(Lines-Of $p)) {
        $n++
        if ($line -match '^\s*#') { continue }
        if ($line.Trim().Length -eq 0) { continue }
        $out += [pscustomobject]@{ Line = $n; Cells = @($line -split "`t") }
    }
    # NOTE: plain `return $out` on purpose -- the ,$out idiom combined with the
    # `@(Cov-Rows ...)` at the call sites collapses the whole row array into ONE
    # element, so every row check silently ran against row 0 only (measured).
    return $out
}

# ---- registry-id / verdict-id extraction (coverage-diff 口径, adjudicated 2026-09-21) -----
# An allowed-difference verdict must NAME a registry id: "<allowed>(-> <id>)".  <id> is the
# leading marker of the registry row's "what" cell (E + digits [+ "-" + a short suffix]):
#   "**E40** (new) ..." -> E40      "E2 . ..." -> E2      "E31-<circled-1> . ..." -> E31-<1>
# The arrow is U+2192 and is built from its code point so this file stays pure ASCII.
$cArrow = [string][char]0x2192
# characters that terminate the id token (whitespace / middle dot / parens / comma / arrow)
function Cov-IsSep([char]$ch) {
    if ([char]::IsWhiteSpace($ch)) { return $true }
    if ($ch -eq '(' -or $ch -eq ')' -or $ch -eq ',' -or $ch -eq ';') { return $true }
    $c = [int]$ch
    return ($c -eq 0x00B7 -or $c -eq 0x2027 -or $c -eq 0x30FB -or $c -eq 0xFF08 -or $c -eq 0xFF09 -or $c -eq 0x2192)
}
# leading run of non-separator characters
function Cov-Token([string]$s) {
    $t = ('' + $s)
    $i = 0
    while ($i -lt $t.Length -and -not (Cov-IsSep $t[$i])) { $i++ }
    return $t.Substring(0, $i)
}
# leading id marker of a registry "what" cell ('' when there is none)
function Cov-RegId([string]$cell) {
    $t   = ('' + $cell).Trim().TrimStart('*', ' ').Trim()
    $tok = (Cov-Token $t).TrimEnd('*')
    if ($tok -match '^E[0-9]+') { return $tok }
    return ''
}
# id named by a verdict cell.  Kind: 'id' = a well-formed "-> <id>" ; 'noid' = an arrow with
# no id after it (the compatibility case "<allowed>(-> <registry-table-name>)") ; 'noarrow' =
# no arrow at all.  Only 'id' counts as a resolved reference.
function Cov-VerdictId([string]$cell) {
    $t = ('' + $cell)
    $i = $t.IndexOf([char]0x2192)
    if ($i -lt 0) { return [pscustomobject]@{ Kind = 'noarrow'; Id = '' } }
    $tok = (Cov-Token $t.Substring($i + 1)).TrimEnd('*')
    if ($tok -match '^E[0-9]+') { return [pscustomobject]@{ Kind = 'id'; Id = $tok } }
    return [pscustomobject]@{ Kind = 'noid'; Id = '' }
}

$maniPresent = Test-Path $manifestPath
$matPresent  = Test-Path $matrixPath
$diffPresent = Test-Path $diffPath

$mani = @()
if ($maniPresent) { foreach ($r in @(Cov-Rows $manifestPath)) { if ((Cov-Cell $r 0) -ne $cDim) { $mani += $r } } }
$mat = @()
if ($matPresent) { foreach ($r in @(Cov-Rows $matrixPath)) { if ((Cov-Cell $r 0) -ne $cDim) { $mat += $r } } }
$reg = @()
if ($diffPresent) { foreach ($r in @(Cov-Rows $diffPath)) { if ((Cov-Cell $r 0) -ne $cWhatIs) { $reg += $r } } }

$covMissingFiles = @()
if (-not $maniPresent) { $covMissingFiles += $nmManifest }
if (-not $matPresent)  { $covMissingFiles += $nmMatrix }
$covHard = $covMissingFiles.Count -gt 0
$covHardMsg = 'coverage tables not produced -- missing ' + ($covMissingFiles -join ' + ') + ' ; T0: an unproduced coverage table is "not done" (FAIL, never skip / PASS / INFO)'

# numbered judgement rows of the acceptance table (the same shape item 04 counts)
$accRows = @()
if ($specText -ne $null) { $accRows = @($specLines | Where-Object { $_ -match '^\|\s*\d+\s*\|' }) }

# ---- 25 coverage-rows --------------------------------------------------------
if ($covHard) {
    Say 'FAIL' 'coverage-rows' $covHardMsg
} else {
    $entities  = $mani.Count
    $sumStates = 0
    $badNum    = 0
    foreach ($r in $mani) {
        $v = 0
        $s = Cov-Cell $r 4
        if ([int]::TryParse($s, [ref]$v)) { $sumStates += $v } else { $badNum++ }
    }
    $uniqE = @{}
    foreach ($r in $mat) { $uniqE[(Cov-Cell $r 0) + "`t" + (Cov-Cell $r 1)] = 1 }
    $uniqEntities = $uniqE.Count
    $matrixRows   = $mat.Count
    $sumMismatch  = $sumStates - $matrixRows
    $detail = "entities=$entities matrixUniqueEntities=$uniqEntities matrixRows=$matrixRows sumStates=$sumStates sumMismatch=$sumMismatch acceptanceRows=$($accRows.Count) nonNumericStateCount=$badNum note=grain: manifest<->matrix (acceptance" + $cAccNote + ")"
    if ($entities -eq 0) {
        Say 'FAIL' 'coverage-rows' ('manifest has 0 data rows ; ' + $detail)
    } elseif ($matrixRows -eq 0) {
        Say 'FAIL' 'coverage-rows' ('matrix has 0 data rows ; ' + $detail)
    } elseif ($entities -eq $uniqEntities -and $matrixRows -eq $sumStates -and $badNum -eq 0) {
        Say 'PASS' 'coverage-rows' $detail
    } else {
        Say 'FAIL' 'coverage-rows' ('manifest rows != matrix distinct entities, or matrix rows != sum of state counts ; ' + $detail)
    }
}

# ---- 26 coverage-filled ------------------------------------------------------
if ($covHard) {
    Say 'FAIL' 'coverage-filled' $covHardMsg
} elseif ($mat.Count -eq 0) {
    Say 'FAIL' 'coverage-filled' 'matrix has 0 data rows -- zero rows is not "zero empty rows"'
} else {
    $bad = @()
    foreach ($r in $mat) {
        $meas = Cov-Cell $r 5
        $verd = Cov-Cell $r 6
        $evid = Cov-Cell $r 7
        $why  = ''
        if ($meas.Length -eq 0) { $why = 'empty-measured' }
        elseif ($verd.Length -eq 0) { $why = 'empty-verdict' }
        elseif ($evid.Length -eq 0) { $why = 'empty-evidence' }
        elseif (-not ($verd.StartsWith($cAgree) -or $verd.StartsWith($cMismatch) -or $verd.StartsWith($cAllow) -or $verd.StartsWith($cUnfixedV))) { $why = 'verdict-not-one-of-the-4-legal-values' }
        if ($why.Length -gt 0) { $bad += ('L' + $r.Line + '[' + (Cov-DimOf (Cov-Cell $r 0)) + ':' + $why + ']') }
    }
    if ($bad.Count -eq 0) {
        Say 'PASS' 'coverage-filled' ("$($mat.Count) matrix row(s), every row carries measured + verdict + evidence and a legal verdict")
    } else {
        Say 'FAIL' 'coverage-filled' ("$($bad.Count)/$($mat.Count) matrix row(s) incomplete ; first 10: " + ((@($bad | Select-Object -First 10)) -join ' '))
    }
}

# ---- 27 coverage-diff --------------------------------------------------------
if ($covHard) {
    Say 'FAIL' 'coverage-diff' $covHardMsg
} elseif ($mat.Count -eq 0) {
    Say 'FAIL' 'coverage-diff' 'matrix has 0 data rows -- nothing to judge'
} else {
    $misRows = @(); $allowRows = @(); $unfixRows = @()
    foreach ($r in $mat) {
        $verd = Cov-Cell $r 6
        if ($verd.StartsWith($cMismatch)) { $misRows += $r }
        elseif ($verd.StartsWith($cUnfixedV)) { $unfixRows += $r }
        elseif ($verd.StartsWith($cAllow)) { $allowRows += $r }
    }
    # registry completeness: "what | why | origin | when" -- all four must be non-empty
    $inv = @()
    foreach ($r in $reg) {
        $emptyCols = @()
        for ($i = 0; $i -lt 4; $i++) { if ((Cov-Cell $r $i).Length -eq 0) { $emptyCols += ($i + 1) } }
        if ($emptyCols.Count -gt 0) { $inv += ('L' + $r.Line + '(empty col ' + ($emptyCols -join ',') + ')') }
    }
    # 2026-09-21 口径: an allowed-difference row must NAME a registry id in its verdict cell
    # ("<allowed>(-> <id>)"), matched id-to-id (exact) against the leading marker of a
    # registry row's "what" cell.  The old "does any registry 'what' cell appear inside the
    # row text" heuristic is gone: it could not tell two rows apart and turned every row
    # without an inlined id into a permanent red.  A verdict that names only the registry
    # TABLE (no id) FAILs with an explicit rewrite hint (compatibility window -- the matrix
    # is being rewritten to carry ids); an unknown id FAILs with its line number and the id.
    $regIds = @{}
    foreach ($g in $reg) {
        $gid = Cov-RegId (Cov-Cell $g 0)
        if ($gid.Length -gt 0 -and -not $regIds.ContainsKey($gid)) { $regIds[$gid] = $g.Line }
    }
    $allowMatched = 0
    $allowNoId    = @()
    $allowNoArrow = @()
    $allowUnknown = @()
    foreach ($r in $allowRows) {
        $vi  = Cov-VerdictId (Cov-Cell $r 6)
        $dim = Cov-DimOf (Cov-Cell $r 0)
        if ($vi.Kind -eq 'id') {
            if ($regIds.ContainsKey($vi.Id)) { $allowMatched++ }
            else { $allowUnknown += ('L' + $r.Line + '[id=' + $vi.Id + ']') }
        } elseif ($vi.Kind -eq 'noid') {
            $allowNoId += ('L' + $r.Line + '[' + $dim + ']')
        } else {
            $allowNoArrow += ('L' + $r.Line + '[' + $dim + ']')
        }
    }
    $allowUnresolved = $allowNoId.Count + $allowNoArrow.Count + $allowUnknown.Count
    $allowDetail = 'allowed-diff rows=' + $allowRows.Count + ' matched=' + $allowMatched + ' unresolved=' + $allowUnresolved + ' unfixed-defect rows=' + $unfixRows.Count
    # the rewrite hint, built from code points so this file stays pure ASCII:
    #   "<please take> -> <registry-name> <change into> -><registry id>"
    $cPlease = Cps @(0x8BF7, 0x628A)
    $cChange = Cps @(0x6539, 0x6210)
    $cDiffNm = Cps @(0x5DEE, 0x5F02, 0x767B, 0x8BB0)
    $cRegId  = Cps @(0x767B, 0x8BB0)
    $hintId  = $cArrow + '<' + $cRegId + 'id>'
    $probs = @()
    if ($misRows.Count -gt 0) {
        $probs += ("verdict '" + $cMismatch + "' count = " + $misRows.Count + ' (must be 0) ; rows ' + ((@($misRows | ForEach-Object { $_.Line })) -join ','))
    }
    if ($inv.Count -gt 0) {
        $probs += ('registry row(s) not 4-of-4 -- col 4 (when-to-remove) must be non-empty too: ' + (@($inv | Select-Object -First 10) -join ' '))
    }
    if ($allowNoArrow.Count -gt 0) {
        $probs += ('allowed-difference row(s) whose verdict carries no "' + $cArrow + '" id: ' + (@($allowNoArrow | Select-Object -First 10) -join ' ') + ' -- ' + $cPlease + ' ' + $cArrow + $cDiffNm + ' ' + $cChange + ' ' + $hintId)
    }
    if ($allowNoId.Count -gt 0) {
        $probs += ('allowed-difference row(s) whose verdict names only "' + $cArrow + $cDiffNm + '" (no id): ' + (@($allowNoId | Select-Object -First 10) -join ' ') + ' -- ' + $cPlease + ' ' + $cArrow + $cDiffNm + ' ' + $cChange + ' ' + $hintId)
    }
    if ($allowUnknown.Count -gt 0) {
        $probs += ('allowed-difference row(s) whose "' + $cArrow + '" id has no leading id in ' + $nmDiff + ': ' + (@($allowUnknown | Select-Object -First 10) -join ' '))
    }
    if ($probs.Count -eq 0) {
        Say 'PASS' 'coverage-diff' ("0 '" + $cMismatch + "' verdict(s) ; " + $allowDetail + ' ; every id resolved against a leading id in ' + $nmDiff + ' (' + $reg.Count + ' registry row(s))')
    } else {
        Say 'FAIL' 'coverage-diff' ((@($probs) -join ' ; ') + ' ; ' + $allowDetail)
    }
}

# ---- 28 coverage-acceptance --------------------------------------------------
# 2026-09-23 (gate-P pass) -- the judge is no longer a pure regex over the row text.
#   WHY: measured on this checkout (audit-A-matrix sec.S1), the table's 521 dedup'd
#   backtick path tokens contain 64 dangling + 14 glob-with-0-hits + 117 bare names,
#   and EVERY row still PASSed, because the old rule only asked "does the row text
#   contain the substring .ai-tmp/screenshots/ | tools/probes/ | <plan>/ | w1_host_*.txt".
#   A citation that does not resolve on disk is not evidence (SKILL 8.2).
#   NEW (strictly additive -- the old row-level test is KEPT as a floor, no token is
#   exempted, nothing that fails now can pass later by a weaker rule):
#     * every backtick path token (a token whose last name carries a file extension
#       -- the same convention item 07 and the audit use) is resolved against the
#       documented base dirs; the first hit wins;
#     * glob ( * / ? )            -> native Test-Path / Resolve-Path, >= 1 hit, every
#                                    base is INSIDE $root (never outside the project);
#     * brace {a,b,c} / range 1..4 -> PowerShell-side shorthand expanded, >= 1 hit;
#     * bare file name (no dir)    -> base dirs first, then leaf-name lookup anywhere
#                                    under the project's evidence roots;
#     * <path>:<line>              -> the file must resolve AND line <= total lines.
#   An empty/unsatisfiable judge still FAILs (never PASS) -- see the branches below.
$accExtRe = '\.(cs|tsv|md|png|txt|json|py|ps1|dc6|log|tab|wav|dll|xml|csv|zig|ds1|dt1|pl2|tbl|bin|asset|unity|go|js|ini|cfg|yml|yaml)$'
$accBases = @(
    $root,
    (Join-Path $root 'client'),
    (Join-Path $root 'client\Assets'),
    (Join-Path $root 'client\Assets\Scripts'),
    (Join-Path $root 'client\Assets\StreamingAssets'),
    (Join-Path $root 'client\Assets\Resources'),
    (Join-Path $root 'client\Assets\Resources\Clover'),
    (Join-Path $root 'client\Assets\ThirdParty\Diablo2\Images'),
    # the two dirs this gate itself treats as project dirs ($cfgDir / $setDir) belong here too
    (Join-Path $root 'client\Assets\Configs'),
    (Join-Path $root 'client\setting'),
    # NOTE: the original-game package (yuan ban zi yuan/ = 原版资源/) is deliberately NOT expanded
    # into its sub-trees.  Measured 2026-09-23: the package IS on disk (原版资源/d2dc6 and
    # 原版资源/d2raw exist, and e.g. 原版资源/d2dc6/data/global/ui/MENU/EndGame.dc6 resolves), but the
    # table cites its assets in SHORT form ("MENU/EndGame.dc6", "chi/youdiedsoftcore.dc6"), so
    # honouring them would mean INVENTING a prefix convention (which sub-tree? which sub-dir?) --
    # that is a relaxation.  The exact-join base 原版资源/ IS in the list, so the moment a citation
    # spells the real path (原版资源/d2dc6/data/...) it resolves and turns green.
    # See the report, section 3.2c.
    (Join-Path $root 'client\_dev'),
    (Join-Path $root 'client\Logs'),
    (Join-Path $root '.ai-tmp'),
    (Join-Path $root '.ai-tmp\test'),
    (Join-Path $root '.ai-tmp\screenshots'),
    (Join-Path $root '.ai-tmp\hosts'),
    (Join-Path $root '.ai-tmp\drivers'),
    (Join-Path $root 'tools'),
    (Join-Path $root 'tools\probes'),
    (Join-Path $root 'tools\probes\hosts'),
    (Join-Path $root 'tools\probes\drivers'),
    (Join-Path $root 'tools\probes\refs'),
    (Join-Path $root 'tools\probes\measure'),
    (Join-Path $root 'tools\d2codec'),
    $planDir,
    (Join-Path $planDir (Cps @(0x81EA, 0x5BA1, 0x5BF9, 0x6BD4))),
    (Join-Path $root (Cps @(0x539F, 0x7248, 0x8D44, 0x6E90))),
    (Join-Path $root 'docs')
)
$accLeafRoots = @(
    (Join-Path $root 'client\Assets'),
    (Join-Path $root 'client\setting'),
    (Join-Path $root '.ai-tmp\screenshots'),
    (Join-Path $root '.ai-tmp\test'),
    (Join-Path $root 'tools'),
    $planDir,
    (Join-Path $root 'docs')
)
$accLeafIdx = $null

$accLineCache = @{}
function Get-AccLineCount([string]$f) {
    # newline count in 4 MB chunks, counted entirely inside .NET: the project's
    # Editor.log is ~470 MB and a per-byte PowerShell loop over it costs minutes
    # (measured: 193 s for one run).  Latin-1 (cp28591) maps byte -> char 1:1, so
    # splitting on LF counts exactly the line breaks.
    $k = $f.ToLowerInvariant()
    if ($script:accLineCache.ContainsKey($k)) { return $script:accLineCache[$k] }
    $n = -1
    try {
        $fs = [System.IO.File]::Open($f, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::ReadWrite)
        $enc = [System.Text.Encoding]::GetEncoding(28591)
        $buf = New-Object byte[] 4194304
        $cnt = 0; $last = 0; $total = 0
        while (($rd = $fs.Read($buf, 0, $buf.Length)) -gt 0) {
            $s = $enc.GetString($buf, 0, $rd)
            $cnt += ($s.Split([char]10).Length - 1)
            $last = $buf[$rd - 1]; $total += $rd
        }
        $fs.Close()
        if ($total -gt 0 -and $last -ne 10) { $cnt++ }
        $n = $cnt
    } catch { $n = -1 }
    $script:accLineCache[$k] = $n
    return $n
}
function Expand-AccShorthand([string]$p) {
    # `p5_{1,2,3}_minimap.png` and `a09_walk_1..6.png` are one citation each on paper;
    # expand both forms.  A `{...}` group carrying non-ASCII is prose (cave{dir}.ds1),
    # never a path -- the caller skips those before getting here.
    $alts = @($p)
    $bm = [regex]::Match($p, '\{([^{}]+)\}')
    if ($bm.Success) {
        $pre = $p.Substring(0, $bm.Index); $post = $p.Substring($bm.Index + $bm.Length)
        $alts = @()
        foreach ($a in ($bm.Groups[1].Value -split ',')) { $alts += ($pre + $a + $post) }
    }
    $out = @()
    foreach ($a in $alts) {
        $rm = [regex]::Match($a, '^(?<pre>[A-Za-z0-9_\-\./\\]*?)(?<a>\d+)\.\.[A-Za-z]?(?<b>\d+)(?<ext>\.[A-Za-z0-9]+)$')
        if ($rm.Success) {
            for ($i = [int]$rm.Groups['a'].Value; $i -le [int]$rm.Groups['b'].Value; $i++) {
                $out += ($rm.Groups['pre'].Value + $i + $rm.Groups['ext'].Value)
            }
        } else { $out += $a }
    }
    return $out
}
function Get-AccLeafIndex() {
    if ($script:accLeafIdx -ne $null) { return $script:accLeafIdx }
    $h = @{}
    foreach ($d in $accLeafRoots) {
        if (-not (Test-Path -LiteralPath $d)) { continue }
        try {
            # .NET enumerator, not Get-ChildItem: one pass over ~50k files, no FileInfo
            # objects (Get-ChildItem here cost ~3 min; this costs ~2 s).
            foreach ($f in [System.IO.Directory]::EnumerateFiles($d, '*', [System.IO.SearchOption]::AllDirectories)) {
                $k = [System.IO.Path]::GetFileName($f).ToLowerInvariant()
                if ($h.ContainsKey($k)) { $h[$k] = @($h[$k]) + @($f) } else { $h[$k] = @($f) }
            }
        } catch { }
    }
    $script:accLeafIdx = $h
    return $h
}
function Resolve-AccRef([string]$x) {
    $isGlob = ($x.Contains('*') -or $x.Contains('?'))
    foreach ($b in $accBases) {
        if (-not (Test-Path -LiteralPath $b)) { continue }
        $c = Join-Path $b $x
        try {
            if ($isGlob) { if (Test-Path -Path $c) { return (@(Resolve-Path -Path $c)[0]).Path } }
            else { if (Test-Path -LiteralPath $c) { return $c } }
        } catch { }
    }
    if (-not ($x.Contains('/') -or $x.Contains('\'))) {
        $idx = Get-AccLeafIndex
        $k = $x.ToLowerInvariant()
        if ($idx.ContainsKey($k)) { return (@($idx[$k])[0]) }
    }
    return ''
}
function Test-AccToken([string]$t) {
    if ($t.StartsWith('http')) { return '' }
    if ($t -match '(^|[\\/])\.\.([\\/]|$)') { return '' }
    $p = $t; $lineNo = -1
    $lm = [regex]::Match($p, '^(.+?):(\d+)(?:-\d+)?$')
    if ($lm.Success) { $p = $lm.Groups[1].Value; $lineNo = [int]$lm.Groups[2].Value }
    $p = $p.Trim()
    if ($p.IndexOf(':') -ge 0) { return '' }   # resource key / drive letter, not a project-relative path
    if ($p -match '^\.[A-Za-z0-9]+$') { return '' }   # ".cs" / ".ds1": a prose extension fragment, not a citation
    while ($p.StartsWith('./')) { $p = $p.Substring(2) }
    if ($p.Length -eq 0) { return '' }
    $isBrace = $false
    $bm = [regex]::Match($p, '\{([^{}]+)\}')
    if ($bm.Success) {
        $isBrace = $true
        if ([regex]::IsMatch($bm.Groups[1].Value, '[^\x00-\x7F]')) { return '' }
    }
    $probe = $p
    if ($isBrace) { $probe = (Expand-AccShorthand $probe)[0] }
    $probe = $probe.TrimEnd('*', '?')
    if (-not [regex]::IsMatch($probe, $accExtRe)) { return '' }   # not a file citation (resource id / prose)
    $isGlob = ($p.Contains('*') -or $p.Contains('?'))
    $hit = ''
    foreach ($x in (Expand-AccShorthand $p)) { $hit = Resolve-AccRef $x; if ($hit -ne '') { break } }
    if ($hit -eq '') {
        # LABEL ONLY -- this can never turn a red into a PASS.  A red whose file name exists
        # elsewhere in the project is a CITATION that points at the wrong directory (the file
        # was moved / re-pointed); a red with no same-named file anywhere is really not on
        # disk.  The distinction is for the fix list, not for the verdict (audit-A sec.10.6).
        $note = ''
        $leaf = $p
        $par = ''
        if ($p.Contains('/') -or $p.Contains('\')) {
            $leaf = [System.IO.Path]::GetFileName(($p -replace '/', '\'))
            $par = [System.IO.Path]::GetFileName([System.IO.Path]::GetDirectoryName(($p -replace '/', '\')))
        }
        if ($leaf.Length -gt 0 -and -not ($leaf.Contains('*') -or $leaf.Contains('?'))) {
            $idx = Get-AccLeafIndex
            $k = $leaf.ToLowerInvariant()
            $hits = @()
            if ($idx.ContainsKey($k)) { $hits = @($idx[$k]) }
            # GUARD (audit-A sec.10.8): a leaf-name hit is NOT proof of a move when the name is
            # a generic one -- e.g. "Program.cs" / "config.json" exist all over the project and
            # matching them would mislabel a really-missing file as "moved".  So: a UNIQUE name
            # may be labelled "moved"; a repeated name is labelled AMBIGUOUS unless exactly one
            # hit also has the SAME PARENT DIRECTORY name.  This is wording only -- the verdict
            # stays FAIL either way; nothing here can turn a red green.
            if ($hits.Count -eq 1) {
                $note = ' ; same name exists at ' + $hits[0] + ' => citation directory is wrong / file was moved (STILL FAIL)'
            } elseif ($hits.Count -gt 1) {
                $sameDir = @($hits | Where-Object { [System.IO.Path]::GetFileName([System.IO.Path]::GetDirectoryName($_)) -eq $par })
                if ($par.Length -gt 0 -and $sameDir.Count -eq 1) {
                    $note = ' ; unique same name + same parent dir name at ' + $sameDir[0] + ' => citation directory is wrong / file was moved (STILL FAIL)'
                } else {
                    $note = ' ; ' + $hits.Count + ' files share this generic name elsewhere (AMBIGUOUS -- NOT a move claim), e.g. ' + $hits[0] + ' (STILL FAIL)'
                }
            }
        }
        if ($isGlob -or $isBrace) { return ($t + ' [glob/shorthand, 0 hit on disk]' + $note) }
        if ($p.Contains('/') -or $p.Contains('\')) { return ($t + ' [path not on disk]' + $note) }
        return ($t + ' [bare name not found anywhere under the project]' + $note)
    }
    if ($lineNo -gt 0) {
        $tot = Get-AccLineCount $hit
        if ($tot -lt 0) { return ($t + ' [line count unreadable: ' + $hit + ']') }
        if ($lineNo -gt $tot) { return ($t + ' [line ' + $lineNo + ' > total ' + $tot + ' : ' + $hit + ']') }
    }
    return ''
}
function Test-AccRowRefs([string]$row) {
    # The WHOLE row is scanned, not just the backtick-delimited spans.  WHY (measured twice,
    # by this pass and independently by audit-A sec.10): an evidence cell commonly nests
    # backticks -- "`[live evidence = `.ai-tmp/screenshots/x.png` (index ...)]`" -- so the
    # row carries FOUR backticks and the pairing regex eats the real path sitting in the
    # middle.  Restricting the scan to backtick spans silently dropped 66 tokens
    # (653 -> 719 with the whole-row scan) and 3 real reds.  Scanning the whole row can
    # only ADD judged tokens -- never excuse one -- so this stays a tightening.
    # braces stay whole (they are shorthand, not separators); '=' and the backtick are
    # separators so that log fragments like `tile=x_e4_cast_a.png` and the delimiters
    # themselves still yield the real token.  CJK punctuation is a separator too: the
    # table glues it to paths (".png (index)"), and without this the extension anchor
    # would miss the token entirely (measured: 107 -> 95 reds lost tokens this way).
    $splitRe = '[\s\uFF0C\u3001\uFF1B()\[\]<>"\x27=\x60\uFF08\uFF09\u3010\u3011\u300A\u300B\u300C\u300D\u300E\u300F\u3008\u3009\uFF1A\uFF1F\uFF01\u3002\uFF0E\u00B7\uFF5E\u3014\u3015]+'
    $toks = @()
    foreach ($t in ($row -split $splitRe)) {
        $t = $t.Trim([char]0x60).Trim()
        if ($t.Length -gt 0) { $toks += $t }
    }
    $out = @()
    foreach ($t in @($toks | Sort-Object -Unique)) {
        $r = Test-AccToken $t
        if ($r -ne '') { $out += $r }
    }
    return $out
}

if ($covHard) {
    Say 'FAIL' 'coverage-acceptance' $covHardMsg
} elseif ($specText -eq $null) {
    Say 'FAIL' 'coverage-acceptance' ('missing acceptance table: ' + $nmSpecMd)
} elseif ($accRows.Count -eq 0) {
    Say 'FAIL' 'coverage-acceptance' 'acceptance table has 0 numbered judgement rows'
} else {
    # on-disk path forms accepted: .ai-tmp/screenshots/... | tools/probes/... | ce hua/... | w1_host_*.txt
    $refRe = '(?i)(\.ai-tmp[\\/]screenshots[\\/]|tools[\\/]probes[\\/]|w1_host_[A-Za-z0-9_\-]*\.txt|' + [regex]::Escape($planName) + '[\\/])'
    $noRef = @(); $bad = @()
    foreach ($r in $accRows) {
        $m = [regex]::Match($r, '^\|\s*(\d+)\s*\|')
        $num = if ($m.Success) { $m.Groups[1].Value } else { '?' }
        if (-not [regex]::IsMatch($r, $refRe)) { $noRef += $num }
        foreach ($p in @(Test-AccRowRefs $r)) { $bad += ('row ' + $num + ' -> ' + $p) }
    }
    $unq = @($bad | ForEach-Object { ($_ -split ' -> ')[1] } | Sort-Object -Unique)
    if ($noRef.Count -eq 0 -and $bad.Count -eq 0) {
        Say 'PASS' 'coverage-acceptance' ("all " + $accRows.Count + ' judgement row(s) cite an on-disk path (.ai-tmp/screenshots/ | tools/probes/ | ' + $planName + '/ | w1_host_*.txt) AND every backtick path token of every row resolves on disk')
    } else {
        $msg = ''
        if ($noRef.Count -gt 0) {
            $msg += ('' + $noRef.Count + '/' + $accRows.Count + ' judgement row(s) cite no on-disk path ; row numbers: ' + (@($noRef | Select-Object -First 20) -join ','))
        }
        if ($bad.Count -gt 0) {
            if ($msg -ne '') { $msg += ' ; ' }
            $msg += ($bad.Count.ToString() + ' citation(s) do NOT resolve on disk (' + $unq.Count + ' unique token(s), named below)')
        }
        Say 'FAIL' 'coverage-acceptance' $msg
        foreach ($b in $bad) { Write-Output ('            ' + $b) }
    }
}

# ---- 29 coverage-dimensions --------------------------------------------------
if ($covHard) {
    Say 'FAIL' 'coverage-dimensions' $covHardMsg
} elseif ($mani.Count -eq 0) {
    Say 'FAIL' 'coverage-dimensions' 'manifest has 0 data rows -- no dimension can be covered'
} else {
    $seen = @{}
    foreach ($r in $mani) {
        $p = Cov-DimOf (Cov-Cell $r 0)
        if ($p.Length -gt 0) { $seen[$p] = 1 }
    }
    $missingDims = @()
    foreach ($p in @($covDims.Keys)) { if (-not $seen.ContainsKey($p)) { $missingDims += ($p + $covDims[$p]) } }
    if ($missingDims.Count -eq 0) {
        Say 'PASS' 'coverage-dimensions' '15/15 dimension code(s) present in the manifest'
    } else {
        Say 'FAIL' 'coverage-dimensions' ('' + $missingDims.Count + '/15 dimension code(s) missing: ' + ($missingDims -join ' '))
    }
}

# =============================================================================
# Template-alignment block (scripts/gate-sync.ps1): the template's GATE-ITEMS list
# must be a SUBSET of this file's items.  The checks below are the ones this project
# did not have under any name; every one of them is judged on this project's REAL
# data, and none of them may be satisfied by a source grep.
# =============================================================================

# -----------------------------------------------------------------------------
# 30 verify-entry -- tools/verify.ps1 exists, is non-empty and PARSES.  "it ran once"
#    is not the judged thing; the entry itself is (template item 7).
# -----------------------------------------------------------------------------
# Judged by PROJECT-RELATIVE path, not $PSCommandPath: the artifact under judgement is
# "the one-click entry that lives in this project", so a copy of this gate placed
# elsewhere can still report on it -- and an entry that got corrupted is then legitimately
# reported red instead of silently judging a different file.
$selfPath = Join-Path $root 'tools\verify.ps1'
if (-not (Test-Path $selfPath)) {
    Say 'FAIL' 'verify-entry' ('tools/verify.ps1 not found at ' + $selfPath)
} else {
    $selfTxt = [System.IO.File]::ReadAllText($selfPath, [System.Text.Encoding]::UTF8)
    $psk2 = $null; $per2 = $null
    [void][System.Management.Automation.Language.Parser]::ParseInput($selfTxt.TrimStart([char]0xFEFF), [ref]$psk2, [ref]$per2)
    if ($selfTxt.Trim().Length -eq 0) {
        Say 'FAIL' 'verify-entry' 'tools/verify.ps1 is empty'
    } elseif (@($per2).Count -gt 0) {
        Say 'FAIL' 'verify-entry' ('tools/verify.ps1 has ' + @($per2).Count + ' syntax error(s) -- the one-click entry is broken')
    } else {
        Say 'PASS' 'verify-entry' ('tools/verify.ps1 present and parses clean (' + (Get-Item $selfPath).Length + ' bytes) -- the one-click re-check entry')
    }
}

# -----------------------------------------------------------------------------
# 31 spec-doc -- the reference spec document (gate 0 product) exists, non-empty.
# -----------------------------------------------------------------------------
$specDocDir = Join-Path $planDir (Cps @(0x7B56, 0x5212, 0x6848))              # ce hua an
$specDocs = @()
if (Test-Path $specDocDir) {
    $specDocs = @(Get-ChildItem $specDocDir -Filter *.md -File -ErrorAction SilentlyContinue | Where-Object { $_.Length -gt 0 })
}
if ($specDocs.Count -ge 1) {
    Say 'PASS' 'spec-doc' ($specDocs.Count.ToString() + ' non-empty spec doc(s) in ce hua/ce hua an [' + (($specDocs | ForEach-Object { $_.Name }) -join ', ') + ']')
} else {
    Say 'FAIL' 'spec-doc' 'no non-empty *.md under ce hua/ce hua an -- the reference spec (gate 0 product) is missing'
}

# -----------------------------------------------------------------------------
# 32 asset-research-doc -- the asset research log (gate 3) exists, non-empty.
# -----------------------------------------------------------------------------
$assetDoc = Join-Path $planDir ((Cps @(0x7D20, 0x6750, 0x8C03, 0x7814)) + '.md')   # su cai diao yan.md
if ((Test-Path $assetDoc) -and ((Get-Item $assetDoc).Length -gt 0)) {
    Say 'PASS' 'asset-research-doc' ('present: ce hua/su cai diao yan.md (' + (Get-Item $assetDoc).Length + ' bytes)')
} else {
    Say 'FAIL' 'asset-research-doc' 'missing or empty ce hua/su cai diao yan.md -- the asset gate (gate 3) has no log'
}

# -----------------------------------------------------------------------------
# 33 baseline-images -- reference-side baseline screenshots exist.  On this project the
#    committed reference-side crops are tools/probes/refs/*.png (the original-artwork
#    half of the visual judges, per tools/probes/README.md); ce hua/ji xian tu/ is
#    accepted as well.
# -----------------------------------------------------------------------------
$baseDirs = @((Join-Path $root 'tools\probes\refs'), (Join-Path $planDir (Cps @(0x57FA, 0x7EBF, 0x56FE))))   # ji xian tu
$basePng = @()
foreach ($d in $baseDirs) {
    if (Test-Path $d) { $basePng += @(Get-ChildItem $d -Recurse -Filter *.png -File -ErrorAction SilentlyContinue) }
}
$basePng = @($basePng | Sort-Object FullName -Unique)
if ($basePng.Count -gt 0) {
    Say 'PASS' 'baseline-images' ($basePng.Count.ToString() + ' reference-side baseline png(s), e.g. ' + (($basePng | Select-Object -First 3 | ForEach-Object { $_.Name }) -join ', '))
} else {
    Say 'FAIL' 'baseline-images' 'no reference-side baseline screenshot (tools/probes/refs/*.png or ce hua/ji xian tu/*.png)'
}

# -----------------------------------------------------------------------------
# 34 no-assets-screenshots -- no forensic screenshot dir under client/Assets.
#    Scope is narrow ON PURPOSE (template item 26): "any png under client/Assets"
#    would be a FALSE RED -- this project carries legitimate ART png there.
# -----------------------------------------------------------------------------
$shotInAsm = Join-Path $client 'Assets\Screenshots'
if (Test-Path $shotInAsm) {
    $nInAsm = @(Get-ChildItem $shotInAsm -Recurse -Filter *.png -File -ErrorAction SilentlyContinue).Count
    Say 'FAIL' 'no-assets-screenshots' ('client/Assets/Screenshots exists (' + $nInAsm + ' png) -- captures belong in .ai-tmp/screenshots, delete before delivery (1.8 item 6)')
} else {
    Say 'PASS' 'no-assets-screenshots' 'no forensic screenshot dir under client/Assets'
}

# -----------------------------------------------------------------------------
# 35 no-team-sessions -- no async/team dispatch channel (it bypasses model:inherit and
#    lands a weaker model).  The violation is a team SESSION ARTIFACT, never the bare
#    existence of a teams/ dir (template item 11: scope the check to the real artifact;
#    "the directory exists" would be a false positive).
# -----------------------------------------------------------------------------
$teamDirs = @((Join-Path (Split-Path $root -Parent) '.codebuddy\teams'), (Join-Path $root '.codebuddy\teams'))
$teamFiles = @()
foreach ($d in $teamDirs) {
    if (Test-Path $d) { $teamFiles += @(Get-ChildItem $d -Recurse -File -Force -ErrorAction SilentlyContinue) }
}
$teamFiles = @($teamFiles | Sort-Object FullName -Unique)
$teamSeen = @($teamDirs | Where-Object { Test-Path $_ })
$teamWhere = if ($teamSeen.Count -eq 0) { 'no teams/ dir present at all' } else { ($teamSeen -join ' ; ') }
if ($teamFiles.Count -eq 0) {
    Say 'PASS' 'no-team-sessions' ('0 team/async session artifact (' + $teamWhere + ') -- dispatch must be a plain synchronous sub-agent')
} else {
    Say 'FAIL' 'no-team-sessions' ($teamFiles.Count.ToString() + ' team/async session artifact(s) -- dispatch must be a plain synchronous sub-agent (SKILL 2 item 2)')
    $teamFiles | Select-Object -First 5 | ForEach-Object { Write-Output ('            ' + $_.FullName) }
}

# -----------------------------------------------------------------------------
# 36 graphics-device -- the render device is not WARP / Basic software rasterisation.
#    A frame-time number taken on a software rasteriser is worthless (SKILL 4 item 9).
# -----------------------------------------------------------------------------
$editorLog = Join-Path $client 'Logs\Editor.log'
if (-not (Test-Path $editorLog)) {
    Say 'HUMAN-ONLY' 'graphics-device' 'no client/Logs/Editor.log -- run SystemInfo.graphicsDeviceName by hand'
} else {
    $devLine = @(Select-String -Path $editorLog -Pattern 'Device Name:\s*(.+)$' -ErrorAction SilentlyContinue | Select-Object -First 1)
    if ($devLine.Count -eq 0) {
        Say 'HUMAN-ONLY' 'graphics-device' 'no "Device Name:" line in Editor.log'
    } else {
        $dev = $devLine[0].Matches[0].Groups[1].Value.Trim()
        if ($dev -match 'Basic Render Driver|Basic Display|WARP') {
            Say 'FAIL' 'graphics-device' ('render device = "' + $dev + '" => software rendering; every frame-time number is void (SKILL 6 gate 2)')
        } else {
            Say 'PASS' 'graphics-device' ('render device = ' + $dev)
        }
    }
}

# -----------------------------------------------------------------------------
# 37 numeric-log-only -- a numeric-class verdict row resting on a bare log line only must
#    be HUMAN-ONLY, never PASS.  A numeric row citing NO on-disk artifact at all FAILs
#    (nothing to judge).  Machine-readable = png / tsv / csv / json; a .txt / .log / .md
#    alone is a log-line or a human note (SKILL 4 item 10: 判定权三分).
# -----------------------------------------------------------------------------
if ($specText -eq $null) {
    Say 'HUMAN-ONLY' 'numeric-log-only' 'acceptance table not found -- numeric rows cannot be classified'
} else {
    $numRows = @($specLines | Where-Object { $_ -match '^\|\s*\d+\s*\|' -and $_.Contains($cNumeric) })
    $numArtRe = '(?i)(\.ai-tmp[\\/]screenshots[\\/]|tools[\\/]probes[\\/]|' + [regex]::Escape($planName) +
                '[\\/]|w1_host_[A-Za-z0-9_\-]*\.txt)[^\s\|\x60]+'
    $noEvid = @(); $logOnly = @()
    foreach ($r in $numRows) {
        $mn = [regex]::Match($r, '^\|\s*(\d+)\s*\|')
        $num = if ($mn.Success) { $mn.Groups[1].Value } else { '?' }
        $arts = @()
        foreach ($am in [regex]::Matches($r, $numArtRe)) { $arts += $am.Value }
        if ($arts.Count -eq 0) { $noEvid += $num; continue }
        $machine = $false
        foreach ($a in $arts) { if ($a -match '(?i)\.(png|tsv|csv|json)$') { $machine = $true } }
        if (-not $machine) { $logOnly += $num }
    }
    if ($numRows.Count -eq 0) {
        Say 'HUMAN-ONLY' 'numeric-log-only' 'no row carries the numeric-class tag -- nothing to judge (an empty result must never PASS)'
    } elseif ($noEvid.Count -gt 0) {
        Say 'FAIL' 'numeric-log-only' ($noEvid.Count.ToString() + ' numeric row(s) cite no on-disk artifact at all ; rows: ' + ($noEvid -join ','))
    } elseif ($logOnly.Count -gt 0) {
        Say 'HUMAN-ONLY' 'numeric-log-only' ($logOnly.Count.ToString() + ' numeric row(s) rest on a log/text line only (no machine-readable artifact) ; rows: ' + ($logOnly -join ',') + ' -- a human must read that line (SKILL 4 item 10)')
    } else {
        Say 'PASS' 'numeric-log-only' ($numRows.Count.ToString() + ' numeric row(s), each cites a machine-readable artifact (png/tsv/csv/json) -- none rests on a bare log line')
    }
}

# -----------------------------------------------------------------------------
# 38 scale-tier -- the sampling density is declared ONCE (S=1-way / M=2-way /
#    L=3-way + full states) and is only ever upgraded, never downgraded to save time.
#    Canonical location per the template = ce hua/ce hua an/*.md ; this project ALSO
#    registers its tier in tools/probes/README.md (the gate's item-list doc), which is
#    the only doc this alignment pass may write -- see the report.
# -----------------------------------------------------------------------------
$cTierWord = Cps @(0x6863, 0x4F4D)                                   # dang wei
$tierRx = '\b(S|M|L)\s*(' + $cTierWord.Substring(0, 1) + '|tier)'
$tierFiles = @()
foreach ($d in @($specDocDir, (Join-Path $root 'tools\probes'))) {
    if (-not (Test-Path $d)) { continue }
    foreach ($f in @(Get-ChildItem $d -Filter *.md -File -ErrorAction SilentlyContinue)) { $tierFiles += $f.FullName }
}
$tierHit = @()
foreach ($fp in $tierFiles) {
    $tx = [System.IO.File]::ReadAllText($fp, [System.Text.Encoding]::UTF8)
    if ($tx.Contains($cTierWord) -and ($tx -match $tierRx)) { $tierHit += (Split-Path $fp -Leaf) }
}
$tierHit = @($tierHit | Sort-Object -Unique)
if ($tierHit.Count -gt 0) {
    Say 'PASS' 'scale-tier' ('declared in ' + ($tierHit -join ','))
} else {
    Say 'FAIL' 'scale-tier' 'no sampling tier (S/M/L) declared -- declare it once, never downgrade (T0)'
}

# -----------------------------------------------------------------------------
# 39 impact-radius -- a BUG FIX enumerates its blast radius (dim / cause chain /
#    affected rows).  Fixing only the reported symptom = two steps forward, one back.
# -----------------------------------------------------------------------------
$irPath = Join-Path $testDir 'impact-radius.tsv'
if (-not (Test-Path $irPath)) {
    Say 'HUMAN-ONLY' 'impact-radius' 'no .ai-tmp/test/impact-radius.tsv -- for a bug fix, list the blast radius (dim / cause chain / affected rows)'
} else {
    $irBad = @([System.IO.File]::ReadAllLines($irPath, [System.Text.Encoding]::UTF8) |
               Where-Object { $_.Trim().Length -gt 0 -and $_ -notmatch '^\s*#' } |
               Where-Object { ($_ -split "`t").Count -lt 3 })
    if ($irBad.Count -eq 0) { Say 'PASS' 'impact-radius' 'every row lists dim / cause chain / affected rows' }
    else { Say 'FAIL' 'impact-radius' ($irBad.Count.ToString() + ' row(s) missing columns (dim / cause chain / affected rows)') }
}

Write-Output ""
Write-Output "===== summary: FAIL=$fail  HUMAN-ONLY=$human ====="
if ($fail -gt 0) { Write-Output 'FAIL present -- nobody may say "done" while this is non-zero (SKILL 1.11)' }
exit $(if ($fail -gt 0) { 1 } else { 0 })
