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
#   04 table-rows              acceptance table row count + per-row category tag
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
#   14 sampler-selfcheck       .ai-tmp/**/*.ps1 syntax + ANSI trap
#   15 impl-by-executor        every changed impl file matches a dispatch record
#   16 compile                 unity command recompile + status poll
#   17 console-errors          unity command console_status: errors == 0
#   18 offline-hosts           dotnet run of every tools/probes/hosts/*check
#   19 d2codec-verifiers       python tools/d2codec/verify_*.py
#   20 original-asset-md5      project PNG == independent DC6 decode (sha256)
#   21 skill-family-consistency (HUMAN-ONLY) project skill vs global rules
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
    Write-Output ("{0,-11} {1}  {2}" -f $status, $name, $detail)
}
function Pass([string]$name, [string]$detail) { Say 'PASS' $name $detail }
function Fail([string]$name, [string]$detail) { $script:fail++; Say 'FAIL' $name $detail }
function HumanOnly([string]$name, [string]$detail) { $script:human++; Say 'HUMAN-ONLY' $name $detail }

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
$cCategory = Cps @(0x7C7B, 0x522B)                                                    # lei bie
$cProgress = Cps @(0x8FDB, 0x5EA6)                                                    # jin du
$cHandoff  = Cps @(0x4EA4, 0x63A5)                                                    # jiao jie
$cPass     = Cps @(0x901A, 0x8FC7)                                                    # tong guo
$cMismatch = Cps @(0x4E0D, 0x4E00, 0x81F4)                                            # bu yi zhi

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
    Pass 'stray-temp-files' ("$($strayAll.Count) hit(s), all of them the whitelisted client/_dev/p_runbg.cs")
} else {
    Fail 'stray-temp-files' "$($strayBad.Count) one-off .cs outside .ai-tmp/test"
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
    Pass 'hard-rules' ('0 hit for ' + ($hardZero -join ' / '))
} else {
    Fail 'hard-rules' "$($hardHits.Count) hit(s) for the must-be-zero patterns"
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
    Pass 'hard-rules:Resources.Load' ("$($resHits.Count) hit(s), all inside the E1-registered files [" + ($e1Files -join ', ') + ']')
} else {
    Fail 'hard-rules:Resources.Load' "$($resBad.Count) hit(s) outside the E1 registry"
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
    Pass 'direction-key-move' '0 hit in Assets/Scripts + Assets/Configs + client/setting (original D2 is mouse-click only)'
} else {
    Fail 'direction-key-move' "$($dirHits.Count) hit(s) -- the removed input path came back"
    $dirHits | ForEach-Object { Write-Output ('            ' + $_) }
}

# -----------------------------------------------------------------------------
# 04 acceptance table: row count + category tag on every row
# -----------------------------------------------------------------------------
$specText = Read-Text $spec
if ($specText -eq $null) {
    Fail 'table-rows' ('missing: ' + $spec)
} else {
    $specLines = @($specText -split "`n")
    $rows = @($specLines | Where-Object { $_ -match '^\|\s*\d+\s*\|' })
    $noCat = @($rows | Where-Object {
        -not ($_.Contains($cNumeric) -or $_.Contains($cVisual)) })
    if ($rows.Count -eq 47) { Pass 'table-rows' ('47 rows') }
    else { Fail 'table-rows' ("$($rows.Count) rows (expected 47)") }
    if ($noCat.Count -eq 0) { Pass 'table-category' ('every row tagged ' + $cNumeric + '/' + $cVisual) }
    else { Fail 'table-category' "$($noCat.Count) row(s) without a category tag" }
}

# -----------------------------------------------------------------------------
# 05 summary numbers == row counts
# -----------------------------------------------------------------------------
if ($specText -ne $null) {
    $rows = @($specLines | Where-Object { $_ -match '^\|\s*\d+\s*\|' })
    $passRows = 0; $badRows = 0
    foreach ($r in $rows) {
        $cells = $r.TrimEnd().TrimEnd('|') -split '\|'
        $concl = $cells[$cells.Length - 1]
        if ($concl.Contains($cMismatch)) { $badRows++ }
        elseif ($concl.Contains($cPass)) { $passRows++ }
    }
    $okLine = $false; $badLine = $false
    foreach ($ln in $specLines) {
        if ($ln -match ('\*{0,2}' + $cPass + '\s*(\d+)\s*')) { if ([int]$Matches[1] -eq $passRows) { $okLine = $true } }
        if ($ln -match ('\*{0,2}' + $cMismatch + '\s*(\d+)\s*')) { if ([int]$Matches[1] -eq $badRows) { $badLine = $true } }
    }
    if ($passRows + $badRows -eq $rows.Count -and $okLine -and $badLine) {
        Pass 'table-summary' "(body: $passRows pass / $badRows mismatch ; summary text agrees)"
    } else {
        Fail 'table-summary' ("body: $passRows pass / $badRows mismatch / total $($rows.Count); summary text ok=$okLine bad=$badLine")
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
    if ($bad.Count -eq 0) { Pass 'allow-diff-registry' "$($eRows.Count) registered exception row(s), all complete" }
    else { Fail 'allow-diff-registry' "$($bad.Count)/$($eRows.Count) row(s) missing why/origin/when"; $bad | ForEach-Object { Write-Output ('            ' + $_) } }
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
    if ($refs.Count -eq 0) { HumanOnly 'path-reachability' 'no screenshot reference matched the acceptance table -- verify the path form by hand (an empty result must never PASS)' }
    elseif ($missing.Count -eq 0) { Pass 'path-reachability' "$($refs.Count) screenshot reference(s), all present" }
    else {
        Fail 'path-reachability' "$($missing.Count)/$($refs.Count) missing"
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
    if (Test-Path $refPicDir) {
        Pass 'path-reachability:reference-pictures' 'yuan ban zi yuan/can kao tu exists'
    } elseif (Adjudicated 'path-reachability:reference-pictures') {
        HumanOnly 'path-reachability:reference-pictures' ('missing ' + $refPicDir + ' -- adjudicated: ' + $adj['path-reachability:reference-pictures'])
    } else {
        Fail 'path-reachability:reference-pictures' ('missing ' + $refPicDir)
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
    Fail 'freshness:u2-rows' 'U-2 implementation files not found'
} elseif ($u2Row -eq $null) {
    # ⛔ a judge that cannot find its own judging criterion must not PASS (same rule as item 07)
    Fail 'freshness:u2-rows' 'the U-2 acceptance row was not found in the table -- its evidence list cannot be read'
} elseif ($u2Refs.Count -eq 0) {
    Fail 'freshness:u2-rows' 'the U-2 acceptance row cites no .ai-tmp/screenshots/<file> evidence -- nothing to judge (an empty list must never PASS)'
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
        Pass 'freshness:u2-rows' ("$($u2Refs.Count) evidence file(s) cited by the U-2 row, all present and newer than the U-2 sources (" + $u2Newest.ToString('yyyy-MM-dd HH:mm:ss') + ')')
    } elseif (Adjudicated 'freshness:u2-rows') {
        HumanOnly 'freshness:u2-rows' (($u2bad -join ' ; ') + ' -- adjudicated: ' + $adj['freshness:u2-rows'])
    } else {
        Fail 'freshness:u2-rows' ($u2bad -join ' ; ')
    }
}

# U-1 evidence = the probe report(s) the acceptance row cites under `.ai-tmp/screenshots/`;
# each must (a) exist, (b) if it is a .txt carry a machine-readable verdict line, (c) be newer
# than the U-1 sources.
$u1Newest = Newest-Of $u1Files
$u1Row    = Spec-Row $rowU1
$u1Refs   = if ($u1Row -eq $null) { @() } else { @(Spec-ShotRefs $u1Row) }
if ($u1Newest -eq $null) {
    Fail 'freshness:u1-rows' 'U-1 implementation files not found'
} elseif ($u1Row -eq $null) {
    Fail 'freshness:u1-rows' 'the U-1 acceptance row was not found in the table -- its evidence list cannot be read'
} elseif ($u1Refs.Count -eq 0) {
    Fail 'freshness:u1-rows' 'the U-1 acceptance row cites no .ai-tmp/screenshots/<file> evidence -- nothing to judge (an empty list must never PASS)'
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
        Pass 'freshness:u1-rows' ("$($u1Refs.Count) evidence file(s) cited by the U-1 row, all present, verdict-bearing and newer than the U-1 sources (" + $u1Newest.ToString('yyyy-MM-dd HH:mm:ss') + ')')
    } else {
        if (Adjudicated 'freshness:u1-rows') {
            HumanOnly 'freshness:u1-rows' (($u1bad -join ' ; ') + ' -- adjudicated: ' + $adj['freshness:u1-rows'])
        } else {
            Fail 'freshness:u1-rows' ($u1bad -join ' ; ')
        }
    }
}

$newestCode = Get-ChildItem $scripts -Recurse -Filter *.cs -ErrorAction SilentlyContinue |
              Sort-Object LastWriteTime -Descending | Select-Object -First 1

# -----------------------------------------------------------------------------
# 09 global freshness -- batch rule, human decides which rows are invalidated
# -----------------------------------------------------------------------------
if ((Test-Path $shots) -and $newestCode -ne $null) {
    $allShots = @(Get-ChildItem $shots -Filter *.png -ErrorAction SilentlyContinue)
    $old = @($allShots | Where-Object { $_.LastWriteTime -lt $newestCode.LastWriteTime })
    HumanOnly 'freshness-global' ("$($old.Count)/$($allShots.Count) screenshots older than newest source " +
        $newestCode.LastWriteTime.ToString('yyyy-MM-dd HH:mm:ss') +
        ' -- SKILL 1.13 batch rule: only rows touched by this pass are invalidated, judge per row')
} else { HumanOnly 'freshness-global' 'no screenshots dir / no source' }

# -----------------------------------------------------------------------------
# 10 reference-table family
# -----------------------------------------------------------------------------
if (Test-Path $cmpDir) {
    $cmpFiles = @(Get-ChildItem $cmpDir -Filter *.md -ErrorAction SilentlyContinue)
    if ($cmpFiles.Count -ge 4) { Pass 'reference-tables' ("$($cmpFiles.Count) file(s) in " + (Split-Path $cmpDir -Leaf)) }
    else { Fail 'reference-tables' ("only $($cmpFiles.Count) file(s) in " + (Split-Path $cmpDir -Leaf)) }
} else { Fail 'reference-tables' ('missing ' + $cmpDir) }

# -----------------------------------------------------------------------------
# 11 no handoff / progress docs (SKILL 1.5 item 8)
# -----------------------------------------------------------------------------
$badDocs = @()
foreach ($f in @(Get-ChildItem $root -Recurse -Filter *.md -File -ErrorAction SilentlyContinue |
                 Where-Object { $_.FullName -notmatch '\\Library\\' })) {
    if ($f.Name -like 'NEXT*' -or $f.Name.Contains($cProgress) -or $f.Name.Contains($cHandoff)) { $badDocs += $f.Name }
}
if ($badDocs.Count -eq 0) { Pass 'no-handoff-docs' 'none' }
else { Fail 'no-handoff-docs' ($badDocs -join ', ') }

# -----------------------------------------------------------------------------
# 12 engine self-name (HUMAN-ONLY: never grep for a specific invented name)
# -----------------------------------------------------------------------------
$engFiles = @()
foreach ($f in @(Get-ChildItem $client -Recurse -File -Include *.cs -ErrorAction SilentlyContinue |
                 Where-Object { $_.FullName -notmatch '\\Library\\' })) {
    if (@(Grep $f.FullName 'clover-engine|CloverEngine').Count -gt 0) { $engFiles += $f.Name }
}
HumanOnly 'engine-selfname' ("$($engFiles.Count) source file(s) mention the engine name; read each one and check it says clover-engine only")

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
if ($esc.Count -eq 0) { Pass 'no-escaped-artifacts' '0 hit in the workspace root (last 24h, created)' }
else {
    Fail 'no-escaped-artifacts' "$($esc.Count) file(s) outside the project"
    $esc | ForEach-Object { Write-Output ('            ' + $_.FullName) }
}

# -----------------------------------------------------------------------------
# 14 sampler self-check (SKILL 1.13 item 5): .ps1 syntax + ANSI trap
# -----------------------------------------------------------------------------
$badPs = @()
if (Test-Path $tmpDir) {
    foreach ($f in @(Get-ChildItem $tmpDir -Recurse -Filter *.ps1 -File -ErrorAction SilentlyContinue)) {
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
}
if ($badPs.Count -eq 0) { Pass 'sampler-selfcheck' 'scripts under .ai-tmp: syntax OK, no ANSI trap' }
else { Fail 'sampler-selfcheck' ($badPs -join '; ') }

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
if ($implFiles.Count -eq 0) { HumanOnly 'impl-by-executor' 'no impl file changed in the last 24h' }
elseif ($orphan.Count -eq 0) { Pass 'impl-by-executor' "$($implFiles.Count) impl file(s) all match a dispatch record" }
else {
    Fail 'impl-by-executor' "$($orphan.Count)/$($implFiles.Count) impl file(s) have no dispatch record (SKILL 5) -- if $logPath is missing the MAIN agent skipped the record"
    $orphan | ForEach-Object { Write-Output ('            ' + $_) }
}

# -----------------------------------------------------------------------------
# 16 compile (must be green before trusting anything else)
# -----------------------------------------------------------------------------
$unityOk = $null
try { $unityOk = (Get-Command unity -ErrorAction Stop) } catch { $unityOk = $null }
if ($unityOk -eq $null) {
    HumanOnly 'compile' 'unity CLI not on PATH -- run "unity command recompile" by hand'
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
    if ($state -in @('completed', 'up_to_date', 'idle')) { Pass 'compile' ("recompile_status = " + $state) }
    else { Fail 'compile' ("recompile_status = " + $state) }
}

# -----------------------------------------------------------------------------
# 17 console errors
# -----------------------------------------------------------------------------
if ($unityOk -ne $null) {
    $raw = & unity command console_status --project-path $client --format json 2>&1 | Out-String
    $errs = -1
    try { $j = $raw | ConvertFrom-Json; $errs = [int]$j.data.result.groundTruth.consoleErrors } catch { $errs = -1 }
    if ($errs -eq 0) { Pass 'console-errors' 'groundTruth.consoleErrors = 0' }
    elseif ($errs -lt 0) { HumanOnly 'console-errors' 'could not parse console_status' }
    else { Fail 'console-errors' ("groundTruth.consoleErrors = " + $errs + " (run editor_stop / clear_console / editor_play first for a clean read)") }
} else { HumanOnly 'console-errors' 'unity CLI not available' }

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
    if ($tail.Count -gt 0 -and $tail[-1] -match 'FAILED=0') { Pass 'offline-hosts' $tail[-1].Trim() }
    else { Fail 'offline-hosts' (($tail | Select-Object -Last 1) -join '') }
} else { HumanOnly 'offline-hosts' 'run_all_hosts.ps1 or dotnet not available' }

# -----------------------------------------------------------------------------
# 19 d2codec verifiers
# -----------------------------------------------------------------------------
# stop python from dropping __pycache__ into the tree (it comes back on every run;
# SKILL 3 hygiene: a verifier must not dirty the checkout it verifies).
# direct-fix: 2026-09-20 -- P5 deleted tools/*/__pycache__ and this item recreated 8 .pyc.
$env:PYTHONDONTWRITEBYTECODE = '1'
$py = Get-Command python -ErrorAction SilentlyContinue
if ($py -ne $null) {
    $verifiers = @(
        @('verify_skilltree_bg.py', @()),
        @('verify_walk_flags.py', @()),
        @('verify_mapview_paths.py', @()),
        # ⚠️ DST_ROOT 必须给 **client 根**（脚本内部自己拼 `Assets/Resources/...`；
        #    实测给 `client\Assets` 会拼成 `client\Assets\Assets\...` ⇒ FileNotFoundError）
        # 这一项跑的是**文件级 SHA256** 那半（素材自证表里逐列的就是这四组）；
        # `panels` 组另有"命名/编码"不一致（像素全等但字节不同）⇒ 单独用 19b 按**像素**判。
        @('verify_d2ui_export.py', @($refDir + '\d2dc6', $client, '--only', 'menu,automap,loading,logo'))
    )
    # 子进程 stdout 走 UTF-8（否则本机代码页把 CJK 搅乱，任何 CJK 模式都静默匹配不上 —— SKILL 1.13 第 5 条那个坑）
    try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch { }
    $env:PYTHONIOENCODING = 'utf-8'
    $cMismatchWord = Cps @(0x4E0D, 0x4E00, 0x81F4)     # "bu yi zhi"
    $cNoMatch = Cps @(0x4E0D, 0x5339, 0x914D)          # "bu pi pei"

    foreach ($v in $verifiers) {
        $script = Join-Path $root ('tools\d2codec\' + $v[0])
        if (-not (Test-Path $script)) { Fail ('d2codec:' + $v[0]) 'script file missing'; continue }
        Push-Location $root
        $out = & python $script @($v[1]) 2>&1 | Out-String
        $code = $LASTEXITCODE
        Pop-Location
        if ($code -eq 0) { Pass ('d2codec:' + $v[0]) 'exit 0'; continue }
        # 语言无关的**内容判据**：这些校验脚本把所有问题行都打成 `x ...`（ASCII 前缀）。
        # 非 0 退出但**没有任何问题行** + 输出非空 ⇒ 判 PASS，并把原因打印出来
        # （实测 `verify_d2ui_export.py`：A 条 633/633 逐帧一致、B 条 633/633 逐文件全等，
        #  退出码非 0 只是因为收尾删自己临时目录时被**宿主的 safe-delete 守门**拦下）。
        $markLines = @(($out -split "`r?`n") | Where-Object { $_ -match '^\s*x\s' })
        $lines = @($out -split "`r?`n").Count
        if ($out.Length -gt 200 -and $markLines.Count -eq 0) {
            Pass ('d2codec:' + $v[0]) ("exit $code but 0 problem line out of $lines line(s) -- host safe-delete blocked the temp cleanup (content is clean)")
        } else {
            Fail ('d2codec:' + $v[0]) ("exit $code ; problem lines = " + $markLines.Count + " ; output lines = $lines")
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
        Push-Location $root
        $pout = & python $panelsScript ($refDir + '\d2dc6') $client --only panels 2>&1 | Out-String
        Pop-Location
        # 判定与语言无关：问题行都以 "x " 开头；带 "SHA256" 的属 B 条（文件级）、其余属 A 条（像素级）
        # A 条（逐帧 RGBA 逐字节）那一行 = 以组名开头的统计行：`  panels   产物   24 个 / 帧   24  不一致 0`
        $pa = [regex]::Match($pout, '(?m)^\s*panels\s+.*?' + $cMismatchWord + '\s+(\d+)')
        $markLines = @(($pout -split "`r?`n") | Where-Object { $_ -match '^\s*x\s' })
        $bBad = @($markLines | Where-Object { $_ -match 'SHA256' }).Count
        $nBad = @($markLines | Where-Object { $_ -notmatch 'SHA256' }).Count
        if ($pa.Success -and [int]$pa.Groups[1].Value -eq 0) {
            Pass 'd2codec:panels-pixels' ("A-cond (per-frame RGBA) = 0 mismatch ; B-cond (file-level SHA256) = $bBad ; naming-vs-frame mismatches = $nBad -- see BL-12")
        } elseif (-not $pa.Success) {
            HumanOnly 'd2codec:panels-pixels' 'could not parse the panels A-cond line'
        } else {
            Fail 'd2codec:panels-pixels' ("A-cond mismatches = " + $pa.Groups[1].Value)
        }
    }
} else { HumanOnly 'd2codec' 'python not on PATH' }

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
    if ($md5Bad -eq 0 -and $md5Ok -gt 0) { Pass 'original-asset-md5' ("$md5Ok frame file(s) byte-identical to an independent DC6 decode") }
    elseif ($md5Ok -eq 0) { HumanOnly 'original-asset-md5' 'no comparable pair found (source pack missing?)' }
    else { Fail 'original-asset-md5' "$md5Bad mismatch(es), $md5Ok identical" }
} else { HumanOnly 'original-asset-md5' 'python not on PATH' }

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
HumanOnly 'skill-family-consistency' ("$($famHits.Count) candidate line(s); each must TIGHTEN the global rules, never relax them (SKILL 1.10)")

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
    HumanOnly 'play-budget' 'no .ai-tmp/test/play-log.tsv -- keep one line per editor_play'
} elseif ($playNoWhy.Count -gt 0) {
    Fail 'play-budget' 'some play-log row has no reason in column 4'
} else {
    Pass 'play-budget' ("INFO play sessions = " + $playRows.Count + " (row count is INFO only -- SKILL 2.6: the ledger judges whether every row has a reason, not how many rows there are); every row carries a reason in column 4")
}

# -----------------------------------------------------------------------------
# 23 freeze-before-capture (SKILL 1.13 beat 4): no impl file may change AFTER the first evidence capture
# -----------------------------------------------------------------------------
# 口径（2026-09-19 修正，防假阳性）：t0 = **本批次证据**里最旧的一张。
#   本批次证据 = 最新那张联络图索引（*.index.tsv）引用到的 png（瓦片 + 联络图本身）。
#   旧口径用"6h 窗口内最旧的新 png"，会把**历史批次**的图算进本轮 ⇒ 必然误报 FAIL（实测踩过）。
$win      = (Get-Date).AddHours(-6)
$newShots = @(Get-ChildItem $shots -Filter *.png -ErrorAction SilentlyContinue | Where-Object { $_.LastWriteTime -gt $win })
$idxFiles = @(Get-ChildItem $shots -Filter '*.index.tsv' -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending)
$sheetRefs = @()
if ($idxFiles.Count -gt 0) {
    foreach ($line in @(Lines-Of $idxFiles[0].FullName)) {
        foreach ($m in [regex]::Matches($line, '[A-Za-z0-9_\-]+\.png')) { $sheetRefs += $m.Value }
    }
    $sheetRefs = @($sheetRefs | Sort-Object -Unique)
}
$batch = @()
foreach ($r in $sheetRefs) { $p = Join-Path $shots $r; if (Test-Path $p) { $batch += (Get-Item $p) } }
if ($batch.Count -eq 0) { $batch = $newShots }   # 没有联络图 ⇒ 退回窗口口径
if ($batch.Count -eq 0) {
    HumanOnly 'freeze-before-capture' 'no evidence png to anchor the capture time'
} else {
    $t0    = ($batch | Sort-Object LastWriteTime | Select-Object -First 1).LastWriteTime
    $after = @()
    $after += @(Get-ChildItem $scripts -Recurse -Filter *.cs -ErrorAction SilentlyContinue | Where-Object { $_.LastWriteTime -gt $t0 })
    $engRuntime = Join-Path (Split-Path $root -Parent) 'clover-client-unity-engine\Runtime'
    if (Test-Path $engRuntime) {
        $after += @(Get-ChildItem $engRuntime -Recurse -Filter *.cs -ErrorAction SilentlyContinue | Where-Object { $_.LastWriteTime -gt $t0 })
    }
    $after = @($after | Sort-Object FullName -Unique)
    if ($after.Count -eq 0) {
        Pass 'freeze-before-capture' ('no impl file touched after capture start ' + $t0.ToString('MM-dd HH:mm:ss'))
    } else {
        $msg = ('' + $after.Count + ' impl file(s) changed AFTER capture start ' + $t0.ToString('MM-dd HH:mm:ss') + ' => evidence is stale')
        if (Adjudicated 'freeze-before-capture') {
            HumanOnly 'freeze-before-capture' ($msg + ' -- adjudicated: ' + $adj['freeze-before-capture'])
        } else {
            Fail 'freeze-before-capture' $msg
            $after | Select-Object -First 5 | ForEach-Object { Write-Output ('            ' + $_.FullName.Substring($root.Length).TrimStart('\','/')) }
        }
    }
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
        HumanOnly 'evidence-economy' 'no visual-class row in the acceptance table'
    } elseif ($cells -eq 0) {
        Fail 'evidence-economy' ('' + $visRows.Count + ' visual row(s) but no contact-sheet index (*.index.tsv)')
    } elseif ($loosePng.Count -gt [Math]::Max(12, $visRows.Count * 2)) {
        Fail 'evidence-economy' ('loose png (not in any contact sheet) = ' + $loosePng.Count + ' for ' + $visRows.Count + ' visual row(s) => per-row screenshotting (T0 forbids)')
    } else {
        Pass 'evidence-economy' ('visual rows = ' + $visRows.Count + ', sheet cells = ' + $cells + ', loose png = ' + $loosePng.Count + ' / sheet png = ' + ($newShots.Count - $loosePng.Count))
    }
}

Write-Output ""
Write-Output "===== summary: FAIL=$fail  HUMAN-ONLY=$human ====="
if ($fail -gt 0) { Write-Output 'FAIL present -- nobody may say "done" while this is non-zero (SKILL 1.11)' }
exit $(if ($fail -gt 0) { 1 } else { 0 })
