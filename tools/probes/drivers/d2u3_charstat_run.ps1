# =============================================================================
# d2u3_charstat_run.ps1 -- ONE Play session for the charstat read-out (slice u52play).
#
#   powershell -NoProfile -ExecutionPolicy Bypass -File d2u3_charstat_run.ps1 -Tag u52run1
#
# Shape copied verbatim from the proven tools/probes/drivers/shopgrid_run.ps1:
#   play lock -> editor_stop -> settle any pending recompile -> clear_console ->
#   editor_play -> eval_file <p_runbg.cs> (the game must keep ticking while the editor is
#   unfocused, otherwise the very first driver station never runs) -> run_script the driver
#   -> wait for the DONE marker -> editor_stop -> release lock -> append ONE play-log line
#   (column 4 non-empty) -> freeze the [D2U3C] lines out of `unity command console --tail`.
#
# Output (absolute paths):
#   <root>/.ai-tmp/test/d2u3_charstat_run_<tag>.txt          this run log
#   <root>/.ai-tmp/screenshots/d2u3_charstat_<tag>.png       panel screenshot (driver)
#   <root>/.ai-tmp/screenshots/d2u3_charstat_<tag>.done      done marker (driver)
#   <root>/.ai-tmp/screenshots/u3_charstat_{readings,screen}_<tag>.tsv   (driver)
#   <root>/.ai-tmp/screenshots/d2u3_charstat_evidence_<tag>.txt          frozen [D2U3C] lines
#
# ASCII only (PS 5.1 reads a BOM-less non-ASCII .ps1 as ANSI).
# =============================================================================
param(
    [string]$Tag = 'u52run1',
    [int]$WaitSeconds = 240,
    [int]$LockWaitSeconds = 1500,
    # Rule 2026-09-24 (from `jitter`'s incident): NEVER start the real runner just to read its
    # self-checks -- it takes the lock and writes a ledger line, and a later kill leaves a zombie
    # lock (no `finally`). `-SelfCheckOnly` prints the encoding check + fingerprint and exits
    # WITHOUT touching locks or the editor.
    [switch]$SelfCheckOnly,
    [string]$Why = 'sheet u52play: the character-stat panel must be read on screen (P1 screenshot + every visible text) and the PlayerStatsDto must be pinned next to the on-screen value and the official level-1 value (P2); also the runtime state of the add-point arrow (sprite/alpha/rect) and the D1..D6 layout re-checks. All of that exists only while the game runs: the panel is built by HudPanel and fed by PlayerModule inside Play.'
)
$ErrorActionPreference = 'Continue'

$root = Split-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) -Parent
$proj = Join-Path $root 'client'
$shots = Join-Path $root '.ai-tmp\screenshots'
$test = Join-Path $root '.ai-tmp\test'
$cs = Join-Path $root 'tools\probes\drivers\d2u3_charstat_drive.cs'
$runbg = Join-Path $root 'tools\probes\interact\p_runbg.cs'
$outLog = Join-Path $test ('d2u3_charstat_run_' + $Tag + '.txt')
# Rule 30 (team-lead 2026-09-24, from `zorder`'s accident): a self-check entry must NEVER write a
# path that a normal run also writes -- its `-SelfTest` overwrote a real batch trace. So under
# -SelfCheckOnly the log goes to its OWN file, and nothing else is written at all.
if ($SelfCheckOnly) { $outLog = Join-Path $test ('d2u3_charstat_selfcheck_' + $Tag + '.txt') }
$evFile = Join-Path $shots ('d2u3_charstat_evidence_' + $Tag + '.txt')
$done = Join-Path $shots ('d2u3_charstat_' + $Tag + '.done')
$lock = Join-Path $test 'play-running.lock'
$legacyLock = Join-Path $test 'play.lock'
$playLog = Join-Path $test 'play-log.tsv'
$me = 'u52charstat-play'
$shotDir = ($shots -replace '\\', '/')
$spec = $shotDir + '|' + $Tag

# Log/ledger encoding (team rule 2026-09-24, item 2): a LOCK file is ASCII, but a LOG file must
# be UTF-8 WITHOUT a BOM -- `-Encoding UTF8` (PS 5.1) prepends a BOM (a shared .tsv then parses
# with a phantom first column) and `-Encoding ASCII` silently turns Chinese CLI output into '?'.
$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
function Say([string]$m) {
    $stamp = (Get-Date).ToString('HH:mm:ss.fff')
    # Write-Host, NOT Write-Output. A function that returns a value AND echoes through
    # Write-Output pushes that string into the caller's pipeline, and `if (Take-Lock)` tests the
    # COLLECTED OUTPUT -- an array (or a string) is always truthy, so the LOCK-RACE-LOST path
    # (Say then `return $false`) would still be read as "I got the lock" = false exclusive.
    # Measured 2026-09-24 (same family as team-lead item 3, last row).
    Write-Host ($stamp + ' ' + $m)
    [System.IO.File]::AppendAllText($outLog, $stamp + ' ' + $m + [Environment]::NewLine, $utf8NoBom)
}
function UC([string[]]$argv) {
    $raw = & unity command @argv --project-path $proj --format json --no-pager 2>&1 | Out-String
    return $raw
}
# ---- lock protocol v2 (team-lead, 2026-09-24): ONE threshold = 12 min + PID liveness --------
# lock content = "<owner> <ISO8601> <PID>"; take => write => re-read verification;
# stale = PID dead OR age >= 12 min;  NEVER touch the editor while the lock is not mine.
# Set when editor_stop could not be confirmed (rule 42): the lock is then deliberately KEPT.
$script:orphanPlay = $false
function Get-LockShot([string]$path) {
    if (-not (Test-Path $path)) { return $null }
    $raw = ''
    try { $raw = (Get-Content $path -Raw) } catch { $raw = '' }
    $flat = ($raw -replace "`r?`n", ' ').Trim()
    # BOM / zero-width markers: PowerShell 5.1's `Set-Content -Encoding UTF8` writes a UTF8 BOM,
    # and a leading U+FEFF would make $parts[0] != <owner> -> My-Lock() would report "not mine"
    # for MY OWN lock => zombie lock + refusal to stop the editor. Measured 2026-09-24 by the
    # sandbox self-test; every peer runner must ignore a BOM when parsing the lock too.
    $flat = ($flat -replace '[\uFEFF\u200B]', '').Trim()
    $flat = (($flat -replace '\s+', ' ')).Trim()
    $parts = @($flat -split ' ')
    $lpid = -1
    if ($parts.Count -ge 3) { [int]::TryParse($parts[2], [ref]$lpid) | Out-Null }
    return @{ path = $path; owner = ($parts[0]); pid = $lpid; raw = $flat }
}
function Lock-OwnerPid([string]$path) { $s = Get-LockShot $path; if ($null -eq $s) { return -1 } else { return $s.pid } }
function Lock-Alive([string]$path) {
    $s = Get-LockShot $path
    if ($null -eq $s) { return $false }
    if ($s.pid -gt 0 -and (Get-Process -Id $s.pid -ErrorAction SilentlyContinue)) { return $true }
    return $false
}
function My-Lock() {
    $s = Get-LockShot $lock
    if ($null -eq $s) { return $false }
    return ($s.owner -eq $me -and $s.pid -eq $PID)
}
function Take-Lock() {
    $body = $me + ' ' + (Get-Date).ToString('o') + ' ' + $PID
    # [!] -Encoding ASCII, NOT UTF8: PowerShell 5.1 writes a BOM for `-Encoding UTF8`, and a BOM
    # makes $parts[0] read back as "\uFEFF<owner>" for any reader that does not strip U+FEFF --
    # exactly the historical runners the mirror exists to block. owner / ISO8601 / PID are all
    # ASCII, so ASCII is lossless. (Reported by `charstat` 2026-09-24; rule: a LOCK FILE must
    # never carry a BOM. Script files may.)
    Set-Content -Path $lock -Value $body -Encoding ASCII          # real lock (three fields)
    # v2.2 transition mirror: 17 historical one-off runners only ever read `play.lock`, so a
    # same-content mirror is written there to keep them out. Same three fields, same owner/PID.
    Set-Content -Path $legacyLock -Value $body -Encoding ASCII
    Start-Sleep -Milliseconds 1100
    # `-eq $true` everywhere a boolean lock helper is tested (reported by `charstat`: this call
    # site was still bare at revision 20778/28cfaee8 -- and the assertion that was supposed to
    # catch it could not, because its regex required a SECOND opening paren).
    if ((My-Lock) -eq $true) {
        $mir = 'no'
        if (Test-Path $legacyLock) { $mir = 'yes' }
        Say ('LOCK-TAKEN ' + (Get-LockShot $lock).raw + ' mirror_play.lock=' + $mir)
        # canonical name (team-wide grep): "read two names" != "write the mirror" -- an audit that
        # only greps for mentions of play.lock cannot tell the two apart, so the WRITE gets its own
        # line. See tools/probes/README.md section 5.2 (rule 8).
        Say ('LEGACY-MIRROR-WRITTEN play.lock owner=' + $me + ' pid=' + $PID + ' present=' + $mir)
        return $true
    }
    Say ('LOCK-RACE-LOST readback=' + (Get-LockShot $lock).raw + ' -> yield (v2 rule 2)')
    return $false
}
function Release-Lock() {
    # Rule 42 item 2: a lock may only be released once the session really exited. If editor_stop
    # could not be confirmed, KEEP the lock (visible to the next slice) rather than hand over an
    # apparently-clean session -- that is how the 13:43 "locks ABSENT but playing" window happened.
    if ($script:orphanPlay -eq $true) {
        Say 'LOCK-RELEASE WITHHELD (ORPHAN-PLAY: playMode was still playing) -> my lock stays so the next slice sees it is NOT clean'
        return
    }
    # v2.2: delete BOTH names, and only the ones whose content is mine.
    $any = $false
    foreach ($f in @($lock, $legacyLock)) {
        if (-not (Test-Path $f)) { continue }
        $any = $true
        $leaf = (Split-Path $f -Leaf)
        $s = Get-LockShot $f
        $mine = ($null -ne $s -and $s.owner -eq $me -and $s.pid -eq $PID)
        if ($mine) {
            Remove-Item $f -Force -ErrorAction SilentlyContinue
            Say ('LOCK-RELEASED (mine) ' + $leaf)
            # canonical tag (team-wide grep) -- only the legacy mirror name carries it, so an
            # audit can tell "the mirror was released" apart from "the real lock was released".
            if ($f -eq $legacyLock) { Say ('LEGACY-MIRROR-RELEASED (mine) ' + $leaf) }
        }
        else {
            $shown = '?'
            if ($null -ne $s) { $shown = $s.raw }
            Say ('LOCK-RELEASE REFUSED (' + $leaf + ' owned by ' + $shown + ') -> left alone (v2 rule 4)')
            if ($f -eq $legacyLock) { Say ('LEGACY-MIRROR-RELEASED REFUSED (owned by ' + $shown + ') left alone') }
        }
    }
    if (-not $any) { Say 'LOCK-RELEASE skip (no lock file)' }
}
# ── 规则 42 item 5（team-lead 统一裁定 2026-09-24）：孤儿 Play 必须**通报主 agent**，
#    ⛔ 不能只写进本片 trace（trace 只有本片自己看；Play 是**共享**资源，必须让主 agent 知情）。
#    机制与全队一致（照 `d2u32_run.ps1` 的落盘报警）：① 追加一行到 `<test>/ORPHAN-PLAY-alert.tsv`
#    （主 agent 可轮询的报警台账）② 打一行 loud `Write-Host`（人眼在控制台就能看到）。
function Notify-MainAgent([string]$what, [string]$playMode, [string]$owner, [string]$pid) {
    $opf = Join-Path $test 'ORPHAN-PLAY-alert.tsv'
    $enc = New-Object System.Text.UTF8Encoding($false)
    $hdr = 'time' + "`t" + 'runner' + "`t" + 'tag' + "`t" + 'what' + "`t" + 'playMode' + "`t" + 'owner' + "`t" + 'pid' + "`t" + 'lockPresent'
    if (-not (Test-Path $opf)) { [System.IO.File]::WriteAllText($opf, $hdr + [Environment]::NewLine, $enc) }
    $row = (Get-Date).ToString('s') + "`t" + 'd2u3_charstat_run' + "`t" + $Tag + "`t" + $what + "`t"
    $row = $row + $playMode + "`t" + $owner + "`t" + $pid + "`t" + ((Test-Path $lock).ToString())
    [System.IO.File]::AppendAllText($opf, $row + [Environment]::NewLine, $enc)
    Say ('NOTIFY-MAIN-AGENT ' + $what + ' (alert file: ' + $opf + ')')
    # ⛔ 拼接必须逐行完整（PS 5.1 会在"跨行括号表达式"上报 Missing closing ')'，
    #   本文件 L231 已记录过同款陷阱）。
    $loud = '*** ORPHAN-PLAY ALERT -- NOTIFY TEAM LEAD: ' + $opf + ' | tag=' + $Tag + ' playMode=' + $playMode
    $loud = $loud + ' owner=' + $owner + ' pid=' + $pid + ' | lock NOT released ***'
    Write-Host $loud
}

# v2 rule 4: if the lock was taken over by somebody else mid-run, do NOT stop the editor.
function Stop-Editor-Safe([string]$why) {
    # `-ne $true` (not `-not (My-Lock)`): the bare form negates the COLLECTED OUTPUT of the call,
    # so one emitted line inside My-Lock would silently invert this guard into "lock is mine".
    if ((My-Lock) -ne $true) {
        Say ('EDITOR-STOP-SKIPPED (' + $why + ') lock is not mine: ' + (Get-LockShot $lock).raw + ' -> v2 rule 4')
        # Rule 42 item 3 (team-lead 2026-09-24): skipping editor_stop because the lock is not mine
        # must NOT be silent -- if the editor is still playing, the next slice would believe it is
        # taking over a clean session. Alarm with owner/pid/playMode readback.
        $sk = Get-LockShot $lock
        $pmo = 'unreadable'
        $sto = UC @('editor_status')
        if ($sto -match '"playMode":\s*"([A-Za-z]+)"') { $pmo = $Matches[1] }
        if ($pmo -eq 'playing') {
            # 裁定 ①（2026-09-24）：owner/pid **可读则写、不可读则 owner=(unreadable)**
            # （旧值 '?' 语义相同但字面不同一 ⇒ 全队 grep 会漏）。
            $own = '(unreadable)'; $opid = -1
            if ($null -ne $sk) { $own = $sk.owner; $opid = $sk.pid }
            Say ('ORPHAN-PLAY owner=' + $own + ' pid=' + $opid + ' playMode=' + $pmo + ' -> editor_stop skipped (lock is not mine); next slice must treat this as NOT clean')
            # 别人的孤儿 Play 也必须通报主 agent（⛔ 我不能停它，但主 agent 必须知道）——与
            # `u52resist` 同款：报警里写明 cleanupOwner = main agent。
            Notify-MainAgent 'orphan-play-not-mine' $pmo $own "$opid"
        }
        return
    }
    Say ('EDITOR-STOP ' + $why)
    UC @('editor_stop') | Out-Null
    # NEW RULE (team-lead 2026-09-24): do NOT release the lock while Play is still winding down.
    # `editor_stop` returns BEFORE Play really ends (the backup scene has to be restored; measured
    # at 12:29:07), which is exactly how `zorder` saw "playMode=playing but lock ABSENT" at 13:13.
    # Bounded poll (5 x 1 s) -- the verdict is logged either way, so the residual window is documented.
    # 窗口放宽（team-lead 裁定 2026-09-24）：**5×1s → 15×2s**。实测 `editor_stop` 一次就能立刻
    # `Exited play mode`（13:48:52 主 agent 兜底那次即如此）⇒ 真正的病是"没叫"，不是"叫了不停"；
    # 窗口过短只会把"慢退出"误判成孤儿（`jitter` 已放到 15×2s + 重试 8×2s，此处对齐）。
    for ($w = 1; $w -le 15; $w++) {
        Start-Sleep -Seconds 2
        if ($w -eq 8) {
            # 已等 16s 仍未停 ⇒ **再叫一次**（第一次 CLI 调用可能在 bridge 抖动里丢了），
            # 而不是直接判孤儿 —— 先补"触发"，再判"超时"。
            Say 'EDITOR-STOP-RETRY attempt=2'
            UC @('editor_stop') | Out-Null
        }
        $stp = UC @('editor_status')
        $pmp = 'unknown'
        if ($stp -match '"playMode":\s*"([A-Za-z]+)"') { $pmp = $Matches[1] }
        if ($pmp -ne 'playing') { Say ('EDITOR-STOP-CONFIRMED playMode=' + $pmp + ' after=' + $w + 's'); return }
    }
    # Rule 42 item 2 (team-lead 2026-09-24): release the lock ONLY once the session really exited.
    # Before this fix the timeout path released anyway -- which is exactly how today's "both locks
    # ABSENT while playMode=playing" window appeared (another slice's orphan Play). Now: keep the
    # lock, raise ORPHAN-PLAY, and let the next slice see a lock (it may take over after 12 min
    # with a dead PID, which is the designed escape hatch) instead of an apparently clean session.
    $script:orphanPlay = $true
    Say 'EDITOR-STOP-TIMEOUT playMode still playing after 30s (bounded poll 15x2s + 1 retry)'
    Say ('ORPHAN-PLAY owner=' + $me + ' pid=' + $PID + ' playMode=playing after 30s poll -> lock NOT released; next slice must treat this as NOT clean')
    # 裁定 ①：两个名字都打（全队 grep 任一都命中）——语义 = **我没放锁**。
    Say 'LOCK-HELD-ON-PURPOSE lock intentionally kept (unfinished session keeps its owner; the 12min stale rule still frees it)'
    Say 'LOCK-RELEASE WITHHELD (ORPHAN-PLAY: playMode was still playing)'
    # 裁定 ①-2：必须通报主 agent（落盘报警 + loud 行），⛔ 不能只写进本片 trace。
    Notify-MainAgent 'orphan-play-timeout' 'playing' $me "$PID"
}

foreach ($d in @($shots, $test)) { if (-not (Test-Path $d)) { New-Item -ItemType Directory -Path $d | Out-Null } }
[System.IO.File]::WriteAllText($outLog, '# d2u3_charstat_run tag=' + $Tag + ' ' + (Get-Date).ToString('s') + [Environment]::NewLine, (New-Object System.Text.UTF8Encoding($false)))

# ---- encoding self-check (team rule 2026-09-24): a PowerShell 5.1 .ps1 that is NON-ASCII and
# BOM-less is read as ANSI by PS 5.1, which silently shreds the syntax ("PARSE-OK" that lies).
# The judgement must be able to FAIL, not just print numbers.
$selfBytes = [System.IO.File]::ReadAllBytes($PSCommandPath)
$hasBom = ($selfBytes.Length -ge 3 -and $selfBytes[0] -eq 0xEF -and $selfBytes[1] -eq 0xBB -and $selfBytes[2] -eq 0xBF)
$nonAscii = @($selfBytes | Where-Object { $_ -gt 127 }).Count
$encOk = ($hasBom -or $nonAscii -eq 0)
$encVerdict = 'OK'
if (-not $encOk) { $encVerdict = 'FAIL(non-ASCII without BOM => PS 5.1 reads it as ANSI)' }
Say ('ENCODING-SELFCHECK bom=' + $hasBom + ' nonAsciiBytes=' + $nonAscii + ' bytes=' + $selfBytes.Length + ' -> ' + $encVerdict)

# Team rule 2026-09-24: every trace carries its OWN fingerprint, so a report can say "per the
# RUNNER-FINGERPRINT line in this trace" instead of pasting a hash that goes stale within minutes
# (`jitter`'s pasted hash mismatched 8 line numbers 2.5 min later => false reds for the reviewer).
#
# HARDENED 2026-09-24 (u3bverify's increment, applied to this file): a `sha16=` read at one moment
# and a `lines=lines-from-another-read` can describe TWO DIFFERENT FILES in ONE row. So `lines` is
# now derived from the SAME $selfBytes snapshot as the hash, with the algorithm printed in the row.
# `lines` = logical lines = ReadAllLines().Count = count(LF), plus 1 when the last byte is not LF
# (an empty file is 0). Verified against [System.IO.File]::ReadAllLines on the drivers in this repo.
$lfCount = 0
foreach ($by in $selfBytes) { if ($by -eq 10) { $lfCount = $lfCount + 1 } }
$selfLines = $lfCount
if ($selfBytes.Length -gt 0 -and $selfBytes[$selfBytes.Length - 1] -ne 10) { $selfLines = $lfCount + 1 }
$selfSha = [System.Security.Cryptography.SHA256]::Create().ComputeHash($selfBytes)
$selfHex = (($selfSha[0..7] | ForEach-Object { $_.ToString('x2') }) -join '')
$selfMtime = (Get-Item -LiteralPath $PSCommandPath).LastWriteTime.ToString('s')
# NOTE: built in a variable and passed on ONE line -- a parenthesised expression split across lines
# is the known PS 5.1 trap (PS reports "Missing closing ')'", and it only bites when a line is added later).
$fpLine = 'RUNNER-FINGERPRINT sha256_16=' + $selfHex + ' bytes=' + $selfBytes.Length + ' lines=' + $selfLines + ' linesAlg=ReadAllLines-equiv(same-snapshot-bytes)' + ' mtime=' + $selfMtime + ' authoritative for THIS trace; verified by re-computing'
Say $fpLine

# Cross-read guard (same class as the hardening above): the row must name ONE version. Re-read the
# file now; if it moved between the snapshot and this point, the row judges the SNAPSHOT only ->
# print that instead of letting a reviewer believe it describes the file as it is now.
$selfBytes2 = [System.IO.File]::ReadAllBytes($PSCommandPath)
$sameAfter = ($selfBytes2.Length -eq $selfBytes.Length)
if ($sameAfter) { for ($i = 0; $i -lt $selfBytes.Length; $i++) { if ($selfBytes2[$i] -ne $selfBytes[$i]) { $sameAfter = $false; break } } }
if ($sameAfter) {
    Say 'RUNNER-FINGERPRINT-CHECK STABLE (re-read right after the row: byte-identical)'
} else {
    $h2 = [System.Security.Cryptography.SHA256]::Create().ComputeHash($selfBytes2)
    $hex2 = (($h2[0..7] | ForEach-Object { $_.ToString('x2') }) -join '')
    Say ('RUNNER-FINGERPRINT-CHECK UNSTABLE-AFTER-PARSE now=' + $hex2 + ' => the row above judges the printed snapshot, NOT the file as it is now')
}

# no-lock entry point: print the local self-checks and STOP (no lock, no editor, no ledger line).
if ($SelfCheckOnly) {
    # Rule 35 (team-lead 2026-09-24, `zorder`'s correction): a "locks are free/held" statement MUST
    # carry the PATH -- `client/play-running.lock` and `<root>/.ai-tmp/test/play-running.lock` are
    # two different files, and reading the wrong one produced "conclusive" but invalid evidence.
    $l1 = Test-Path $lock
    $l2 = Test-Path $legacyLock
    Say ('SELFCHECK-ONLY-LOCKS before: ' + $lock + '=' + $l1 + ' | ' + $legacyLock + '=' + $l2)
    $l1b = Test-Path $lock
    $l2b = Test-Path $legacyLock
    Say ('SELFCHECK-ONLY-LOCKS after:  ' + $lock + '=' + $l1b + ' | ' + $legacyLock + '=' + $l2b)
    # ═════════════════════════════════════════════════════════════════════════
    # ★★ 规则 42 item 6 / 规则 45（team-lead 2026-09-24）：**"触发真的会被调到"必须可机械判**
    #
    # 现场代价（本日唯一造成全队 6.5 分钟阻塞的根因）：`d2u27` 的 `editor_stop` 确实只出现 1 处、
    #   也确实在守卫函数内（**存在性**通过），但**那条路径在正常收尾里走不到**（**可达性**不通过）
    #   ⇒ 两把锁被释放、编辑器在 Play 里挂了 6.5 分钟，直到主 agent 兜底。
    #   ⇒ 存在性 ≠ 可达性；本段用 **AST** 判可达性，并配**退化样本**（⛔ 判据必须能红）。
    # 口径（规则 45）：判"次数/存在"一律匹配 **AST 节点 / 调用形式**，⛔ 不匹配裸词 ——
    #   四片都曾因为"新加了一条含 `editor_stop` 一词的报警文案"把 only-once 断言判成**假红**，
    #   而假红的真正损失是**逼人把断言删掉**。本段所有计数都走 `CommandAst` 节点。
    # ═════════════════════════════════════════════════════════════════════════
    $astFail = $false
    $uncond = -1
    $stopCallCount = -1
    $notifyCount = -1
    $withholdGuards = -1
    $ucCalls = -1
    $trigVerdict = 'FAIL(not evaluated)'
    $degVerdict = 'FAIL(not evaluated)'
    try {
        $src = [System.IO.File]::ReadAllText($PSCommandPath)
        $tk = $null; $pe = $null
        $ast = [System.Management.Automation.Language.Parser]::ParseInput($src, [ref]$tk, [ref]$pe)
        if ($pe -and @($pe).Count -gt 0) { throw ('parse errors = ' + @($pe).Count) }
        $fns = @($ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq 'Stop-Editor-Safe' }, $true))
        if ($fns.Count -ne 1) { throw ('Stop-Editor-Safe definitions = ' + $fns.Count + ' (require exactly 1)') }
        $body = $fns[0]
        # ⚠️ 判据形状（规则 45：匹配**调用形式**，⛔ 不匹配裸词）：本 runner 调 CLI 是
        #   `UC @('editor_stop')` —— **动词是实参**，`GetCommandName()` 返回的是 `UC`。
        #   ⇒ 必须判「命令名 ∈ {UC, Ucmd} 且实参里有字符串常量 'editor_stop'」。
        #   （第一版按命令名判 ⇒ editorStopCalls=0 ⇒ **假红**；假红会逼人删断言。）
        #   实参可能是**直接常量**（`UC 'editor_stop'`）也可能是**数组字面量**
        #   （`UC @('editor_stop')` ← 本 runner 的形式；它是一层 ArrayLiteralAst，直接看
        #   `CommandElements` 会找不到 ⇒ 第一版就是在这里给出"神秘的 0"）。
        $isStop = { param($n)
            if ($n -isnot [System.Management.Automation.Language.CommandAst]) { return $false }
            $nm = $n.GetCommandName()
            if ($nm -ne 'UC' -and $nm -ne 'Ucmd') { return $false }
            foreach ($s in $n.FindAll({ param($x) $x -is [System.Management.Automation.Language.StringConstantExpressionAst] }, $false)) {
                if ($s.Value -eq 'editor_stop') { return $true }
            }
            return $false
        }
        # 诊断位：函数内 UC/Ucmd 调用总数 —— 判据形状再错时，0 不再"神秘"（能区分
        # "调用形式没匹配上"与"函数里根本没有 CLI 调用"）。
        $ucCalls = @($body.FindAll({ param($n) $n -is [System.Management.Automation.Language.CommandAst] -and ($n.GetCommandName() -eq 'UC' -or $n.GetCommandName() -eq 'Ucmd') }, $true)).Count
        $stops = @($body.FindAll($isStop, $true))
        $stopCallCount = $stops.Count
        $uncond = 0
        foreach ($c in $stops) {
            $p = $c.Parent
            $guard = $false
            while ($null -ne $p) {
                if ($p -is [System.Management.Automation.Language.IfStatementAst]) { $guard = $true; break }
                $p = $p.Parent
            }
            if (-not $guard) { $uncond++ }
        }
        $notifyCount = @($body.FindAll({ param($n) $n -is [System.Management.Automation.Language.CommandAst] -and $n.GetCommandName() -eq 'Notify-MainAgent' }, $true)).Count
        $rns = @($ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq 'Release-Lock' }, $true))
        $withholdGuards = 0
        if ($rns.Count -ge 1) {
            foreach ($ia in @($rns[0].FindAll({ param($n) $n -is [System.Management.Automation.Language.IfStatementAst] }, $true))) {
                if ($ia.Clauses[0].Item1.Extent.Text -notmatch 'orphanPlay') { continue }
                if (@($ia.FindAll({ param($n) $n -is [System.Management.Automation.Language.ReturnStatementAst] }, $true)).Count -ge 1) { $withholdGuards++ }
            }
        }
        if ($uncond -ge 1) { $trigVerdict = 'PASS' }
        else { $trigVerdict = 'FAIL(all editor_stop calls sit inside an if => unreachable on the normal path)'; $astFail = $true }

        # 退化样本：把那条无条件调用包进 `if ($true)` 后再判 ⇒ 必须变成 0 个无条件调用。
        # ⛔ 不许恒真：若包起来还是 PASS，说明判据根本判不到"可达性"。
        $deg = $src.Replace("    UC @('editor_stop') | Out-Null", "    if (`$true) { UC @('editor_stop') | Out-Null }")
        if ($deg -eq $src) { $degVerdict = 'FAIL(degraded transform did not apply => sample invalid)'; $astFail = $true }
        else {
            $dtk = $null; $dpe = $null
            $dast = [System.Management.Automation.Language.Parser]::ParseInput($deg, [ref]$dtk, [ref]$dpe)
            $dfns = @($dast.FindAll({ param($n) $n -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq 'Stop-Editor-Safe' }, $true))
            $duncond = -1
            if ($dfns.Count -eq 1) {
                $duncond = 0
                foreach ($c in @($dfns[0].FindAll($isStop, $true))) {
                    $p = $c.Parent
                    $guard = $false
                    while ($null -ne $p) {
                        if ($p -is [System.Management.Automation.Language.IfStatementAst]) { $guard = $true; break }
                        $p = $p.Parent
                    }
                    if (-not $guard) { $duncond++ }
                }
            }
            if ($duncond -eq 0) { $degVerdict = 'PASS(0 unconditional after wrapping the call in if ($true))' }
            else { $degVerdict = 'FAIL(degraded sample still had ' + $duncond + ' unconditional => criterion is vacuous)'; $astFail = $true }
        }
    } catch {
        $trigVerdict = 'FAIL(exception: ' + $_.Exception.Message + ')'
        $astFail = $true
    }
    Say ('SELF-CHK stop-trigger-reachable editorStopCalls=' + $stopCallCount + ' unconditionalStopGuardCalls=' + $uncond + ' ucCallsInGuard=' + $ucCalls + ' verdict=' + $trigVerdict)
    Say ('SELF-CHK stop-trigger-degraded-sample ' + $degVerdict)
    Say ('SELF-CHK orphan-notify-and-withhold notifyCalls=' + $notifyCount + ' (require >=2: timeout + not-mine) withholdGuards=' + $withholdGuards + ' (require >=1: Rule 42 item 1/2/5)')
    if ($astFail) { Say 'SELFCHECK-FAIL see the SELF-CHK lines above'; exit 3 }
    if (($notifyCount -lt 2) -or ($withholdGuards -lt 1)) { Say 'SELFCHECK-FAIL orphan notify/withhold missing (Rule 42 item 1/2/5)'; exit 3 }

    # ---------------------------------------------------------------------------------------
    # Rule (team-lead 2026-09-24, slice `charstat`, item 2): report -> DRIVER version -> evidence
    # batch must line up, and the alignment must be re-runnable -- "report cites reading R, R was
    # produced by driver version D" must be checkable by a command, not by prose.
    # Delegates to tools/probes/measure/charstat_batch_align.py, which re-reads the ledger
    # (.ai-tmp/test/d2u3_charstat_batches.tsv), the evidence files on disk, the report's machine
    # readable `BATCH <tag> ...` line and the driver on disk. Exit codes:
    #   0 = PASS | 3 = SUSPECT-DECLARED (a producer moved after the batch AND the report declares
    #   it -> LOUD and non-zero, never a silent pass) | 1 = SUSPECT-UNDECLARED / DRIFT /
    #   REPORT-MISMATCH / ledger unusable.
    # Rule 45: the checker carries its own known-good + known-bad samples (`--selftest`); a checker
    # that cannot go red is not a checker, so its selftest is a hard gate here too.
    # ---------------------------------------------------------------------------------------
    $alignFail = $false
    $alignScript = Join-Path $root 'tools\probes\measure\charstat_batch_align.py'
    if (-not (Test-Path $alignScript)) {
        Say ('SELF-CHK report-driver-batch-align verdict=FAIL(script missing: ' + $alignScript + ')')
        $alignFail = $true
    } else {
        $alignOut = (& python $alignScript 2>&1 | Out-String)
        $alignCode = $LASTEXITCODE
        foreach ($ln in ($alignOut -split "`r?`n")) { if ($ln.Trim().Length -gt 0) { Say ('SELF-CHK batch-align| ' + $ln.Trim()) } }
        $alignVerdict = 'PASS'
        if ($alignCode -eq 3) { $alignVerdict = 'SUSPECT-DECLARED' }
        elseif ($alignCode -ne 0) { $alignVerdict = 'FAIL'; $alignFail = $true }
        Say ('SELF-CHK report-driver-batch-align exit=' + $alignCode + ' verdict=' + $alignVerdict + ' (0=PASS 3=SUSPECT-DECLARED 1=FAIL)')
        $degOut = (& python $alignScript --selftest 2>&1 | Out-String)
        $degCode = $LASTEXITCODE
        foreach ($ln in ($degOut -split "`r?`n")) { if ($ln.Trim().Length -gt 0) { Say ('SELF-CHK batch-align-selftest| ' + $ln.Trim()) } }
        if ($degCode -ne 0) { Say 'SELF-CHK report-driver-batch-align-degraded-sample FAIL'; $alignFail = $true }
        else { Say 'SELF-CHK report-driver-batch-align-degraded-sample PASS(known-good+known-bad cases all as expected)' }
    }
    if ($alignFail) { Say 'SELFCHECK-FAIL report-driver-batch-align see the SELF-CHK lines above'; exit 3 }

    Say ('SELFCHECK-ONLY-END (no lock taken, no editor touched, nothing accounted; locksUnchanged=' + (($l1 -eq $l1b) -and ($l2 -eq $l2b)) + ')')
    exit 0
}
if (Test-Path $done) { Remove-Item $done -Force }

# ---- play lock (team-wide protocol) ---------------------------------------------
$t0 = Get-Date
$got = $false
$tookOver = ''
$legacyStale = ''
while (-not $got -and ((Get-Date) - $t0).TotalSeconds -lt $LockWaitSeconds) {
    $blocked = $null
    foreach ($f in @($lock, $legacyLock)) {
        if (-not (Test-Path $f)) { continue }
        $s = Get-LockShot $f
        # A file whose content is MINE is not an obstacle -- and this must be a PER-FILE test,
        # because under v2.2 my own content sits in BOTH names while My-Lock() only inspects $lock.
        # NOTE the explicit `-eq $true`: a bare `if (My-Lock)` would test the call's COLLECTED
        # OUTPUT, so the day My-Lock (or Get-LockShot) emits a log line the condition silently
        # inverts into truthy-array territory (reported by `charstat` 2026-09-24, same family as
        # the Take-Lock bug).
        if ($null -ne $s -and $s.owner -eq $me -and $s.pid -eq $PID) { continue }
        $age = ((Get-Date) - (Get-Item $f).LastWriteTime).TotalMinutes
        $pidKnown = ($s.pid -gt 0)
        $alive = Lock-Alive $f
        # v2 rule 2/3: ONE threshold (12 min) + PID liveness.
        # stale = age >= 12min  OR  (PID present AND that process is gone).
        # A lock with NO PID field (an older runner that has not migrated yet) must NOT be
        # treated as stale while it is fresh -- an absent PID is "unknown", not "dead".
        $stale = ($age -ge 12) -or ($pidKnown -and -not $alive)
        $pidFlag = 0; if ($pidKnown) { $pidFlag = 1 }
        $aliveFlag = 0; if ($alive) { $aliveFlag = 1 }
        $who = (Split-Path $f -Leaf) + ' owner=' + $s.owner + ' pid=' + $s.pid + ' age=' + [math]::Round($age, 1) + 'm pidKnown=' + $pidFlag + ' alive=' + $aliveFlag
        if (-not $stale) {
            $blocked = $f
            $whyYield = 'fresh'
            if (-not $pidKnown) { $whyYield = 'NO-PID-FIELD but fresh -> v2.1 unknown != dead, must yield' }
            Say ('LOCK-BUSY ' + $who + ' -> yield (' + $whyYield + '), sleep 30')
            break
        }
        # v2.1: a lock with NO PID field may only be taken over once it is age-stale.
        $why = 'reason=age ' + [math]::Round($age, 1) + 'm >= 12m'
        if (-not $pidKnown) { $why = $why + ' (and the lock has NO-PID-FIELD; v2.1: unknown != dead)'; if ($age -lt 12) { $why = 'reason=?? BUG: no-pid + fresh must yield' } }
        elseif (-not $alive) { $why = 'reason=pid ' + $s.pid + ' is DEAD (age ' + [math]::Round($age, 1) + 'm)' }
        Say ('LOCK-STALE ' + $who + ' -> takeover allowed, ' + $why)
        $tookOver = $s.raw + ' [' + $why + ']'
        # team rule 2026-09-24: the only real lock is play-running.lock; play.lock is a legacy
        # file that must never be written again. A STALE legacy file may be removed -- but only
        # with an account line, never silently.
        if ($f -ne $lock) { $legacyStale = $s.raw + ' [' + $why + ']' }
    }
    if ($null -eq $blocked) {
        # NEW RULE (team-lead 2026-09-24, from `zorder`'s 13:13 reading): a free lock does NOT
        # prove the editor is free -- `editor_stop` returns BEFORE Play really ends, so a session
        # can be running with NO lock at all. Readiness = lock + playMode, and
        # playMode=playing => ABORT (no lock taken beyond ours, editor untouched, nothing logged
        # into the play ledger). Checked on EVERY attempt: it is one CLI call per 30 s wait step.
        # fail-CLOSED (team-lead ruling 2026-09-24): an UNREADABLE playMode is "unknown", never
        # "stopped" -- the same rule as "missing PID = unknown != dead" on the lock side, and two
        # silent-CLI failures were measured today (`status --project-path` -> empty table;
        # `run_script` -> success:true for a driver that does not compile).
        # NB: the bridge being unreachable is a DIFFERENT case (no editor cannot be in Play), but a
        # RUNNING editor can also answer that way (measured 13:0x) -> bounded retry first, and any
        # reachable reading decides.
        $gate = 'unknown'
        $probed = 0
        for ($g = 1; $g -le 3; $g++) {
            $probed = $g
            $stm = UC @('editor_status')
            if ($stm -match '"playMode":\s*"([A-Za-z]+)"') { $gate = $Matches[1]; break }
            if ($stm -match 'COMMAND_FAILED|"success":\s*false') { $gate = 'bridge-down'; Start-Sleep -Seconds 5; continue }
            $gate = 'unknown'
            break
        }
        Say ('LOCK-GATE playMode=' + $gate + ' probed=' + $probed)
        if ($gate -eq 'playing') {
            Say 'ABORT editor-is-playing -> someone is in Play without a lock; editor untouched, nothing accounted'
            exit 2
        }
        if ($gate -eq 'unknown') {
            Say 'LOCK-GATE-UNKNOWN playMode unreadable -> yield (unknown != stopped)'
            Start-Sleep -Seconds 30
            continue
        }
        if ($gate -eq 'bridge-down') {
            Say ('LOCK-GATE-BRIDGE-DOWN probed=' + $probed + ' -> continue (no editor cannot be in Play)')
        }
        # explicit `-eq $true`: a bare `if (Take-Lock)` tests the COLLECTED output of the call,
        # so ANY echoed line would make it truthy and a lost lock race would be read as a win.
        if ((Take-Lock) -eq $true) {
            if ($tookOver.Length -gt 0) { Say ('LOCK-TOOK-OVER took over a stale lock: ' + $tookOver) }
            if ($legacyStale.Length -gt 0) {
                # canonical name (team-wide grep), printed BEFORE the delete so the account line
                # exists even if the delete itself fails.
                Say ('LEGACY-LOCK-STALE-DELETED ' + $legacyStale + ' (registered)')
                Remove-Item $legacyLock -Force -ErrorAction SilentlyContinue
                Say ('LEGACY-LOCK-CLEANUP removed the stale legacy lock play.lock (accounted): ' + $legacyStale)
            }
            $got = $true
            break
        }
    }
    Start-Sleep -Seconds 30
}
if (-not $got) {
    $so = Get-LockShot $lock
    $oname = '?'
    if ($null -ne $so) { $oname = $so.owner }
    Say ('ABORT lock held by ' + $oname + ' - alive/fresh after the lock wait -> editor untouched (v2 rule 3, fail fast)')
    exit 2
}

# Shared ledger: UTF-8 without BOM (a BOM here would add a phantom first column for any TSV
# reader, and ASCII would eat Chinese in $Why).
[System.IO.File]::AppendAllText($playLog, (Get-Date).ToString('yyyy-MM-dd HH:mm') + "`tu52play`tu52-charstat-arrow-play`t" + $Why + [Environment]::NewLine, $utf8NoBom)
Say ('PLAYLOG-APPENDED ' + $playLog)

try {
    Say 'STOP'
    Stop-Editor-Safe 'v2-gated'
    Start-Sleep -Seconds 2

    # ── 规则 42 item 4（team-lead 统一裁定 2026-09-24）：**接管者义务** ——
    #    接管 stale 锁的人，必须在开**自己**的会话前先把前一个会话停到 `stopped`。
    #    若停不掉（`Stop-Editor-Safe` 已判孤儿、`$script:orphanPlay` 置真）⇒ ⛔ **不许把自己的
    #    Play 叠在别人的 Play 上**：直接退出，且**不放锁**（`Release-Lock` 会因 orphanPlay 拒绝
    #    释放 ⇒ 会话保留归属，12 分钟 stale 规则仍能放行下一个接管者）。
    #    出处：`d2u32_run.ps1` 的 `TAKEOVER-ORPHAN-PLAY` 同款处置；本片此前缺这一道闸门。
    if ($script:orphanPlay -eq $true) {
        Say 'TAKEOVER-ORPHAN-PLAY the inherited session would not stop -> NOT starting our own session; lock intentionally KEPT'
        Notify-MainAgent 'takeover-orphan-play' 'playing' $me "$PID"
        Say 'END'
        exit 4
    }

    Say 'RECOMPILE'
    UC @('recompile') | Out-Null
    for ($i = 0; $i -lt 40; $i++) {
        Start-Sleep -Seconds 2
        $st = UC @('recompile_status')
        if ($st -match 'up_to_date|completed|idle') { Say ('RECOMPILE ' + ($st -replace "`r?`n", ' ')); break }
    }
    Start-Sleep -Seconds 2

    UC @('clear_console') | Out-Null

    # editor_play: the recompile above re-creates the pipeline server (new port), so the first
    # call can answer 401 "Missing or invalid authentication token" against the stale target
    # (measured 2026-09-24 11:25:07 -> port moved 7801 -> 7802). Retry until the editor really plays.
    Say 'PLAY'
    $playing = $false
    for ($i = 1; $i -le 6; $i++) {
        $play = UC @('editor_play')
        $pl = ($play -replace "`r?`n", ' ')
        Say ('PLAY-RESULT try=' + $i + ' ' + $pl.Substring(0, [Math]::Min(260, $pl.Length)))
        Start-Sleep -Seconds 4
        $stt = UC @('editor_status')
        if ($stt -match '"playMode":\s*"playing"') { $playing = $true; Say ('PLAYMODE playing try=' + $i); break }
        Say ('PLAYMODE not-playing try=' + $i)
    }
    if (-not $playing) {
        Say 'PLAY-FAILED the editor never reported playMode=playing -> releasing the lock, no session'
        Stop-Editor-Safe 'v2-gated'
        Release-Lock
        Say 'END'
        exit 3
    }
    Start-Sleep -Seconds 6

    Say 'RUNBG'
    for ($i = 1; $i -le 4; $i++) {
        $rb = UC @('eval_file', '--file', $runbg)
        Say ('RUNBG-RESULT try=' + $i + ' ' + ($rb -replace "`r?`n", ' ').Substring(0, [Math]::Min(200, ($rb -replace "`r?`n", ' ').Length)))
        if ($rb -like '*"success": true*') { break }
        Start-Sleep -Seconds 6
    }
    Start-Sleep -Seconds 2

    # WARM-UP: compiling this file for the first time makes Unity reload the domain, which restarts
    # the game and destroys any driver object created before the reload (measured: run u52run1 got
    # the driver installed and then wiped by that reload). Call the no-op entry first, let the
    # reload settle, then install the real driver.
    Say 'WARMUP'
    $w = UC @('run_script', '--file', $cs, '--entry', 'Diablo2.Probes.D2U3C.Charstat.Ping')
    $wl = ($w -replace "`r?`n", ' ')
    Say ('WARMUP-RESULT ' + $wl.Substring(0, [Math]::Min(220, $wl.Length)))
    Start-Sleep -Seconds 18
    $stt2 = UC @('editor_status')
    Say ('POST-WARMUP ' + (($stt2 -replace "`r?`n", ' ')))
    for ($i = 1; $i -le 3; $i++) {
        $rb2 = UC @('eval_file', '--file', $runbg)
        if ($rb2 -like '*"success": true*') { Say ('RUNBG2-OK try=' + $i); break }
        $rl2 = ($rb2 -replace "`r?`n", ' ')
        Say ('RUNBG2 try=' + $i + ' ' + $rl2.Substring(0, [Math]::Min(160, $rl2.Length)))
        Start-Sleep -Seconds 6
    }
    Start-Sleep -Seconds 2

    # ---------------------------------------------------------------------------------------
    # Rule (team-lead 2026-09-24, slice `charstat`, item 2): a batch must NAME the DRIVER version
    # that produced it. Until this line existed only RUNNER-FINGERPRINT was recorded, so the driver
    # version could only be RECONSTRUCTED from other reports' prose -- and a report could then cite
    # a driver revision that never produced its evidence (measured: the driver was patched at
    # 14:18:24 while that batch's evidence came from the 14:12:36 revision).
    # Emitted immediately before the driver is handed to the editor, then re-read right after, so
    # this row names ONE version (same hardening as RUNNER-FINGERPRINT above).
    # ---------------------------------------------------------------------------------------
    $drvBytes = [System.IO.File]::ReadAllBytes($cs)
    $drvLf = 0
    for ($i = 0; $i -lt $drvBytes.Length; $i++) { if ($drvBytes[$i] -eq 10) { $drvLf++ } }
    $drvLines = $drvLf
    if ($drvBytes.Length -gt 0 -and $drvBytes[$drvBytes.Length - 1] -ne 10) { $drvLines = $drvLf + 1 }
    $drvHex = (([System.Security.Cryptography.SHA256]::Create().ComputeHash($drvBytes))[0..7] | ForEach-Object { $_.ToString('x2') }) -join ''
    $drvMtime = (Get-Item -LiteralPath $cs).LastWriteTime.ToString('s')
    Say ('DRIVER-FINGERPRINT sha256_16=' + $drvHex + ' bytes=' + $drvBytes.Length + ' lines=' + $drvLines + ' linesAlg=ReadAllLines-equiv(same-snapshot-bytes)' + ' mtime=' + $drvMtime + ' tag=' + $Tag + ' authoritative for THIS trace; verified by re-computing')
    $drvBytes2 = [System.IO.File]::ReadAllBytes($cs)
    $drvSame = ($drvBytes2.Length -eq $drvBytes.Length)
    if ($drvSame) { for ($i = 0; $i -lt $drvBytes.Length; $i++) { if ($drvBytes2[$i] -ne $drvBytes[$i]) { $drvSame = $false; break } } }
    if ($drvSame) {
        Say 'DRIVER-FINGERPRINT-CHECK STABLE (re-read right after the row: byte-identical)'
    } else {
        Say 'DRIVER-FINGERPRINT-CHECK UNSTABLE-MID-RUN (the driver moved between the row and the run) => THIS batch cannot be attributed to one driver version; treat its evidence as suspect'
    }

    Say ('RUN_SCRIPT spec=' + $spec)
    $json = '[\"' + $spec + '\"]'
    $r = UC @('run_script', '--file', $cs, '--entry', 'Diablo2.Probes.D2U3C.Charstat.Install', '--args', $json)
    $rl = ($r -replace "`r?`n", ' ')
    Say ('RUN_SCRIPT-RESULT ' + $rl.Substring(0, [Math]::Min(500, $rl.Length)))
} catch {
    Say ('ERROR ' + $_.Exception.Message)
}

$waited = 0
while (-not (Test-Path $done) -and $waited -lt $WaitSeconds) {
    Start-Sleep -Seconds 3
    $waited += 3
}
if (Test-Path $done) { Say ('DONE ' + (Get-Content $done -Raw).Trim()) } else { Say ('DONE-MISSING after ' + $waited + 's') }

# ---- the Unity console window (Debug.Log): read it with `unity command console` ----------
$cons = UC @('console', '--tail', '500')
$keep = @($cons -split "`r?`n" | Where-Object { $_ -match '\[D2U3C\]' })
if ($keep.Count -gt 0) {
    [System.IO.File]::WriteAllLines($evFile, [string[]]$keep, (New-Object System.Text.UTF8Encoding($false)))
    Say ('EVIDENCE ' + $evFile + ' lines=' + $keep.Count)
    foreach ($l in $keep) { Say ('EV ' + $l.Trim()) }
} else {
    Say 'EVIDENCE-EMPTY no [D2U3C] line in the console tail'
    $errs = @($cons -split "`r?`n" | Where-Object { $_ -match 'error CS|Exception|NullReference' } | Select-Object -First 12)
    foreach ($e in $errs) { Say ('CONSOLE-ERR ' + $e.Trim()) }
}

Say 'STOP-AGAIN'
Stop-Editor-Safe 'tail'
Release-Lock
Say ('SUMMARY tag=' + $Tag + ' done=' + (Test-Path $done) + ' outLog=' + $outLog)
Say 'END'
