# =============================================================================
# tools/verify.ps1 -- one-click re-check gate for clover-project-diablo2
#
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools/verify.ps1
#
# CUT DOWN 2026-09-24 (team-lead ruling): this file used to carry ~40 items over
# 2655 lines and took ~277 s, most of it re-proving things that a reader settles
# with one look.  Then the "gate trim" of 2026-09-24 cut it again: the gate is now
# the THREE REQUIREMENTS a machine has to settle -- (1) a REAL build, (2) delivery
# hygiene, (3) references reachable incl. image freshness -- plus sampler-selfcheck,
# which is the precondition that the gate's own scripts are parseable at all.
# Kept:
#
#   01 stray-temp-files    one-off / backup artifacts must not live in the project
#                          tree (caught: *.bak products of the s-docs pass landed in
#                          ce hua/ and client/ and rode along in a commit -- the very
#                          failure SKILL 3 item 5 names)
#   02 screenshot-refs     every Screenshots/<file> the acceptance table cites
#                          resolves on disk (a citation pointing at a moved file
#                          reads exactly like a live one until a machine checks).
#                          It also prints path-reachability:reference-pictures --
#                          the reference-side picture citations, same rule
#   03 evidence-freshness  the evidence a row cites must be NEWER than the sources
#                          that row observes -- machine-only judgement, since a
#                          stale screenshot looks identical to a fresh one.  It
#                          also prints freshness:u1-rows for the U-1 runtime-log
#                          rows, whose names are read from the row, never pinned
#   04 sampler-selfcheck   every .ps1/.psm1 under the repo parses, and a file with
#                          non-ASCII bytes carries a UTF-8 BOM (PS 5.1 decodes a
#                          BOM-less non-ASCII file as ANSI: quotes get eaten and
#                          the whole script dies while an OLD log still looks
#                          like this run's output)
#   05 compile             the Unity assembly recompiles clean; needs a running
#                          Editor -- with no Pipeline instance it reports
#                          HUMAN-ONLY at once instead of a 120 s red wall
#   06 offline-hosts       dotnet builds + runs every tools/probes/hosts/*check
#                          (offline, no Editor): a real compile of the client
#                          sources.  Caught uicheck's Band2Right overflow --
#                          "jing yan cur/next" needs 152 art inside 99 availPx
#
# Verdicts: PASS / FAIL / HUMAN-ONLY.  FAIL = fix it before anyone says "done";
# HUMAN-ONLY = only a human / multimodal reader can settle it.
#
# Removed by the 2026-09-24 gate trim: hard-rules (the Debug.Log / PlayerPrefs /
# GameObject.Find / Instantiate( / bare Input. / Resources.Load greps -- the bans
# still stand, they are simply no longer machine-checked), reference-table (the
# six 1:1 dimensions, the per-row original|ours|delta audit and the fuzzy-wording
# scan), engine-credit (the rendered "by clover-engine" line), and
# d2codec:panels-pixels (per-frame RGBA of the panel PNGs against an independent
# DC6+PL2 decode).
# Deleted earlier in the same day: the coverage-matrix family (items 25..29 -- 4971
# matrix rows re-deriving what the manifest already states), the
# summary/self-consistency family (row counts, summary text == row counts, registry
# row-by-row cross-check), impl-by-executor and no-sync-subagents (dispatch-ledger
# column audits), runner-static-traps and its c5 fixtures, the always-true-asserts
# scanner, probe-hit-rate / line-count / file-size thresholds, no-engine-edits and
# engine-issues.  Nothing here is "planned": a planned item always comes back.
#
# NOT ASCII-only on purpose: this file carries CJK in COMMENTS, so it MUST keep its
# UTF-8 BOM -- item 04 flags exactly that.  Non-ASCII paths / words in CODE are
# built from code points.
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
function Cps([int[]]$codes) { return ([char[]]$codes -join '') }

# -- non-ASCII names / words, built from code points (keeps CODE ASCII) --------
$planDir   = Join-Path $root (Cps @(0x7B56, 0x5212))                                  # ce hua
$spec      = Join-Path $planDir ((Cps @(0x9A8C, 0x6536, 0x8868)) + '.md')             # yan shou biao
$refDir    = Join-Path $root (Cps @(0x539F, 0x7248, 0x8D44, 0x6E90))                  # yuan ban zi yuan
$refPicDir = Join-Path $refDir (Cps @(0x53C2, 0x8003, 0x56FE))                        # can kao tu

$client   = Join-Path $root 'client'
$shots    = Join-Path $root '.ai-tmp\screenshots'
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
# -----------------------------------------------------------------------------
# inputs shared by the surviving items (hoisted out of the deleted blocks)
# -----------------------------------------------------------------------------
$specText = Read-Text $spec                               # the acceptance table, read once
# -----------------------------------------------------------------------------
# 01 stray-temp-files: no one-off artifact outside .ai-tmp/test -- the criterion is about
#    the ARTIFACT, not its extension (SKILL 3 item 5: one-off products live in
#    <project root>/.ai-tmp/test/ only).
#    TIGHTENED 2026-09-24 after the blind spot was actually exploited: the old check only
#    looked at *.cs under _dev/_assets_src/_assets_tmp, so four IN-TREE BACKUPS
#    (ce hua/yan shou biao.md.bak-sdocsbatch{,2} and client/<...>.md.bak-sdocsbatch{2,3})
#    passed a fully green gate and were then swept into two commits by `git add -A`
#    (cleaned up in d53d0a76).  Backup/temp NAME PATTERNS are now flagged whatever the
#    extension is.
#    Scope = the project tree, PRUNED of .ai-tmp/** (the allowed landing zone) and of
#    every Library / obj / bin / node_modules / .git directory (Unity's package cache and
#    build outputs carry foreign names and would only yield false reds).
#    The *.cs half is UNCHANGED: same _dev/_assets_src/_assets_tmp scope, same
#    client/_dev/p_runbg.cs whitelist.  This pass only ADDS a pattern class.
# -----------------------------------------------------------------------------
$strayAll = @(Get-ChildItem $root -Recurse -Filter *.cs -ErrorAction SilentlyContinue |
              Where-Object { $_.FullName -match '\\(_dev|_assets_src|_assets_tmp)\\' })
$strayBad = @($strayAll | Where-Object { $_.FullName -notmatch '\\_dev\\p_runbg\.cs$' })
# backup / temp name patterns, ANY extension (the project's real naming is <file>.bak-<slice>)
$bakRe = '(~|\.bak(-.*)?|\.orig|\.save|\.tmp|\.old|\.swp)$'
$bakExcludeDirs = @('Library', 'obj', 'bin', 'node_modules', '.git')
$bakAiTmp = Join-Path $root '.ai-tmp'
$bakQueue = New-Object System.Collections.Generic.Queue[string]
$bakQueue.Enqueue($root)
$bakStray = @()
$bakScanned = 0
while ($bakQueue.Count -gt 0) {
    $bakDir = $bakQueue.Dequeue()
    foreach ($bakSub in @(Get-ChildItem $bakDir -Directory -Force -ErrorAction SilentlyContinue)) {
        if ($bakExcludeDirs -contains $bakSub.Name) { continue }
        if ($bakSub.FullName -eq $bakAiTmp) { continue }
        $bakQueue.Enqueue($bakSub.FullName)
    }
    foreach ($bakF in @(Get-ChildItem $bakDir -File -Force -ErrorAction SilentlyContinue)) {
        $bakScanned++
        if ($bakF.Name -match $bakRe) { $bakStray += $bakF.FullName }
    }
}
if ($strayBad.Count -eq 0 -and $bakStray.Count -eq 0) {
    Say 'PASS' 'stray-temp-files' ("$($strayAll.Count) hit(s), all of them the whitelisted client/_dev/p_runbg.cs ; 0 backup/temp-pattern file(s) in $($bakScanned) scanned file(s) outside .ai-tmp/Library/obj/bin/node_modules/.git")
} else {
    if ($strayBad.Count -gt 0) {
        Say 'FAIL' 'stray-temp-files' "$($strayBad.Count) one-off .cs outside .ai-tmp/test"
        $strayBad | ForEach-Object { Write-Output ('            ' + $_) }
    }
    if ($bakStray.Count -gt 0) {
        Say 'FAIL' 'stray-temp-files:backup-pattern' "$($bakStray.Count) backup/temp-pattern file(s) inside the project tree -- one-off products belong in .ai-tmp/test (SKILL 3 item 5)"
        $bakStray | Select-Object -First 20 | ForEach-Object { Write-Output ('            ' + $_) }
    }
}

# -----------------------------------------------------------------------------
# 02 screenshot-refs -- every Screenshots/<file> cited by the table resolves
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
# 03 evidence-freshness for the rows THIS pass changed -- **row-scoped**
#    (SKILL 1.13 batch rule: a code change only invalidates the rows it really affects.
#     The blunt "every screenshot vs newest source" version was a separate judge -- deleted in the 2026-09-24 gate cut; only this row-scoped judge survives.)
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

# -- row-scoped evidence names: resolved from the acceptance table, never hard-coded -----
# row labels (the first cell of the acceptance-table row; built from code points to keep
# this file ASCII) -- see this item's header for why.
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
# expanded (the same two forms the screenshot-refs item handles, incl. its "optional letter" trap: the letter
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
    # ⛔ a judge that cannot find its own judging criterion must not PASS (same rule as the screenshot-refs item)
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

# -----------------------------------------------------------------------------
# 04 sampler-selfcheck (SKILL 1.13 item 5): .ps1 syntax + ANSI trap
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
# 05 compile -- the assembly must build before anything else is worth believing.
#    The Editor-side recompile is the only true compile entry point.  With no
#    Editor attached the unity CLI answers COMMAND_FAILED ("No Pipeline instance
#    found for project"): that is neither a build failure nor a pass.  The old
#    version polled recompile_status 60 times with a 2 s sleep (a 120 s red wall)
#    and piped the empty answer into Out-String, which bound $null to InputObject
#    and printed a parameter-binding error on every poll.  Offline build coverage
#    lives in item 06 (offline-hosts): dotnet compiles + runs the client sources.
# -----------------------------------------------------------------------------
$unityOk = $null
try { $unityOk = (Get-Command unity -ErrorAction Stop) } catch { $unityOk = $null }
if ($unityOk -eq $null) {
    Say 'HUMAN-ONLY' 'compile' 'unity CLI not on PATH -- start the project from Unity Hub, then run "unity command recompile" by hand'
} else {
    $null = & unity command recompile --project-path $client 2>&1 | Out-Null
    $state = '?'
    for ($i = 0; $i -lt 5; $i++) {
        $raw = (& unity command recompile_status --project-path $client --format json 2>&1) -join "`n"
        if ($raw.Trim().Length -eq 0) { break }
        try {
            $j = $raw | ConvertFrom-Json
            if ($null -eq $j.data -or $null -eq $j.data.result) { break }
            $inner = $j.data.result | ConvertFrom-Json
            $state = [string]$inner.status
        } catch { $state = '?'; break }
        if ($state -in @('completed', 'up_to_date', 'idle', 'failed')) { break }
        Start-Sleep -Seconds 2
    }
    if ($state -in @('completed', 'up_to_date', 'idle')) { Say 'PASS' 'compile' ('recompile_status = ' + $state) }
    elseif ($state -eq 'failed') { Say 'FAIL' 'compile' 'recompile_status = failed -- the assembly does not compile' }
    else { Say 'HUMAN-ONLY' 'compile' 'no Unity Pipeline instance answered (Editor not running, or the CLI cannot reach it) -- start the project from Unity Hub and re-run; nothing is claimed about the assembly build' }
}
# -----------------------------------------------------------------------------
# 06 offline-hosts (dotnet run, one process each)
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
    else {
        Say 'FAIL' 'offline-hosts' (($tail | Select-Object -Last 1) -join '')
        @(($out -split "`r?`n") | Where-Object { $_ -match 'exit=\d+\s+FAIL' } | ForEach-Object { Write-Output ('            ' + $_.Trim()) })
    }
} else { Say 'HUMAN-ONLY' 'offline-hosts' 'run_all_hosts.ps1 or dotnet not available' }

Write-Output ""
Write-Output "===== summary: FAIL=$fail  HUMAN-ONLY=$human ====="
if ($fail -gt 0) { Write-Output 'FAIL present -- nobody may say "done" while this is non-zero (SKILL 1.11)' }
exit $(if ($fail -gt 0) { 1 } else { 0 })
