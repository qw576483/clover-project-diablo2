<#
  shopart_logshape.ps1 -- a CAN-FAIL shape assertion for append-style logs / heartbeats.
  Provenance: u53-shopart R21 (2026-09-24). My heartbeat was silently broken:
  a PowerShell here-string (@' ... '@) does NOT include the newline before '@ , and
  [IO.File]::AppendAllText does not add one either, so every append glued itself onto
  the previous line. The file still looked normal (9 lines, 18110 bytes) but 22 entries
  sat inside ONE 15421-char line, so "read the last 3 lines" showed old content and the
  reader wrongly concluded the evidence had never been written. The failure is SILENT.

  RULE ENFORCED: writing a log must carry its own line terminator, and after writing you
  must read the SHAPE back (line starts + no glued entries + trailing newline) -- not just
  glance at the last line.

  Modes (exactly one per run):
    -Check   -Path <file>            assert shape of an existing log.  exit 0 = OK, 1 = FAIL
    -Append  -Path <file> -Text <s>  write one line (explicit CRLF, UTF-8 no BOM), then
                                     re-read and assert shape (write-and-read-back).
    -SelfTest                        prove this assertion CAN fail: runs real + synthetic
                                     injections that MUST be red, plus positives that MUST be
                                     green. exit 0 = all controls behaved, 1 = assertion blind.

  Options:
    -MaxLineChars <int>   default 2000 -- glue is also caught by length
    -StrictStart          treat the file as if it had declared SHAPE=ts-name-msg (fatal
                          line-start contract). Default: undeclared file => start-shape INFO.
    -Quiet                suppress the per-check sheet, keep the RESULT line

  VERDICT (team ruling 2026-09-24, README 66). The verdict looks ONLY at the
  FORMAT-INDEPENDENT half -- a rule meant to alarm in every sheet must be calibrated on the
  real corpus, and a line-start white-list is NOT cross-shape:
      E-EOF-NEWLINE   file does not end with 0x0A            => FATAL
      E-LONG          line > MaxLineChars                     => FATAL
      E-GLUE          a date ABUTS a non-separator char       => FATAL
      E-BADSTART-INFO line start differs from the convention  => INFO (never fatal)
      E-MISSING       cannot read the file at all             => FATAL
                      (kept fatal on purpose: "cannot judge" must never read as green)
  History: revision 1 made the line-start rule fatal. Measured on the real corpus it produced
  FALSE REDS on healthy files -- select 16, classcols 69, u44impl 210/222, u3bverify 21,
  charstat 9 lines -- and one sheet added '# ' prefixes to 210 lines just to turn it green.
  Lesson (same family as the glue rule): calibrate on the real corpus before alarming.

  SHAPE DECLARATION (how the start rule becomes enforceable without false reds):
    The judged file MAY declare its own shape on its FIRST line:
        SHAPE=ts-name-msg     date / HH:MM:SS / '#' / ALLCAPS-tag at line start   (my style)
        SHAPE=iso-msg         ISO date, optionally wrapped in '[' , or '#' / tag
        SHAPE=caps-tag        an ALLCAPS tag of >=3 chars (short tags like ASK : / FIX :)
        SHAPE=ts-line         a date or clock time only
    Declared  => line-start violations ARE fatal (E-BADSTART-DECL) -- opt-in strictness,
                 validated against the declaration, so it is strict AND cannot false-red
                 another sheet's format.
    Undeclared => line-start differences are INFO only (E-BADSTART-INFO).

  FORMAT-ONLY REPAIR, AND ITS LIMIT (README 66):
    '#' prefixing is a ZERO-LOSS normalisation for a STATUS-SUMMARY file. It is FORBIDDEN for
    an APPEND-ONLY TIMELINE log (trace / ledger): rewriting its line shape = rewriting history.
    So a red start-shape on a timeline log means FIX THE JUDGE, never the log.

  NOT IN SCOPE (deliberately not added as a shape): lowercase 'key=value' ledgers. Adding it
  would be a WIDENING (it would let real glue through) -- so it stays undeclared+INFO.

  CALL CONTRACT (select, 2026-09-24 -- a crashed judge must never look green):
    GREEN  <=>  a 'RESULT OK' line was printed  AND  the exit code is 0.
    A run that prints NO 'RESULT' line is a TOOL FAILURE => treat as RED, never as pass.
    (Observed: a partially-written revision failed to PARSE, printing no verdict line while an
     in-session '& call' still left $LASTEXITCODE at 0 => callers that treat "no FAIL seen" as
     green would have passed without judging anything.)
    So call it as a FILE, not in-session:
        powershell -NoProfile -File tools\probes\measure\shopart_logshape.ps1 -Check -Path <abs>
    `powershell -File` does return non-zero when the script cannot be parsed; `& script.ps1`
    does not. Runtime failures are additionally trapped and reported as
    'RESULT FAIL(TOOL-ERROR: ...)' with exit 3 (distinct from FAIL=1 / USAGE=2).
    Every run prints a JUDGE-VERSION line (own sha256_16/bytes/lines/mtime) so a reading can be
    cited together with the version that produced it (README 55/66: "read" != "newest").

  AFTER ANY EDIT, RUN A REAL ENTRY (not only -SelfTest) and report the new fingerprint --
  -SelfTest passing on the previous revision says nothing about this one.
#>
[CmdletBinding()]
param(
    [string]$Path,
    [string]$Text,
    [switch]$Check,
    [switch]$Append,
    [switch]$SelfTest,
    [int]$MaxLineChars = 2000,
    [switch]$StrictStart,
    [switch]$Quiet
)

$ErrorActionPreference = 'Stop'
$Utf8NoBom = New-Object System.Text.UTF8Encoding($false)

# Line-start conventions. NOTE (?-i) + -cnotmatch are REQUIRED: PowerShell's -match is
# case-INSENSITIVE by default, so a bare [A-Z] also accepts lowercase prose -- which silently
# defeats the whole check (measured: 'prose continuation ...' matched [A-Z] under -match).
# All four shapes below are NARROWINGS of the same date/tag family; none of them is a widening.
$RxTimestamp = '\d{4}-\d{2}-\d{2}[ T]\d{2}:\d{2}(?::\d{2})?'
$RxCapsTag   = '^[A-Z][A-Z0-9_\-]{2,}\b'   # >=3 chars: short tags like ASK : / FIX : pass
$ShapeStart = @{
    'ts-name-msg' = '^(?-i)\[?(' + $RxTimestamp + '|\d{2}:\d{2}:\d{2}(?:\.\d+)?|#|[A-Z][A-Z0-9_\-]{4,}\b)'
    'iso-msg'     = '^(?-i)\[?(' + $RxTimestamp + '|#|[A-Z][A-Z0-9_\-]{4,}\b)'
    'caps-tag'    = '^(?-i)(' + $RxCapsTag + ')'
    'ts-line'     = '^(?-i)(' + $RxTimestamp + '|\d{2}:\d{2}:\d{2}(?:\.\d+)?)'
}
$RxShapeDecl = '^\s*SHAPE\s*=\s*([A-Za-z0-9\-_]+)\s*$'

# Characters that may LEGALLY sit immediately before a date in the middle of a line
# (an event time quoted inside an entry: "lock=[u52resist <date>]", "mtime=<date>", ...).
# Calibrated 2026-09-24 against real files, see the glue detector below.
$RxSepBefore = '^[\s=:\[\{\<\(\|\;,"''/\\+\-]$'

# [IO.File] resolves a RELATIVE path against the process CWD, which is NOT PowerShell's
# location (measured: PS location = <project>, [Environment]::CurrentDirectory = workspace
# root -- so a relative read silently landed in a DIFFERENT .ai-tmp tree). Always root it.
function Resolve-LocalPath {
    param([string]$P)
    if ([System.IO.Path]::IsPathRooted($P)) { return $P }
    return (Join-Path (Get-Location).ProviderPath $P)
}

function Test-LogShape {
    param([string]$Path, [int]$MaxLineChars = 2000, [switch]$StrictStart)

    $Path = Resolve-LocalPath $Path
    $r = [pscustomobject]@{
        Path       = $Path
        Exists     = $false
        Bytes      = 0
        Lines      = 0
        TailNl     = $false
        MaxLine    = 0
        Shape      = 'undeclared'
        StartFatal = $false
        StartMiss  = 0
        Infos      = @()
        Fails      = @()
    }
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        $r.Fails = @('E-MISSING file does not exist')
        return $r
    }
    $r.Exists = $true
    $bytes = [System.IO.File]::ReadAllBytes($Path)
    $r.Bytes = $bytes.Length
    # 1) trailing newline present (an appender that forgets it is the root defect)
    $r.TailNl = ($bytes.Length -gt 0 -and $bytes[$bytes.Length - 1] -eq 0x0A)

    $lines = [System.IO.File]::ReadAllLines($Path)
    $r.Lines = $lines.Count
    $fails = New-Object System.Collections.Generic.List[string]
    $infos = New-Object System.Collections.Generic.List[string]

    # SHAPE declaration on the FIRST line (team ruling, README 66). Declared => the start
    # contract is validated strictly (E-BADSTART-DECL, fatal); undeclared => INFO only.
    # -StrictStart is equivalent to declaring ts-name-msg without editing the file.
    $declLine = 0
    if ($lines.Count -gt 0 -and $lines[0] -match $RxShapeDecl) {
        $decl = $Matches[1].ToLower()
        if ($ShapeStart.ContainsKey($decl)) {
            $r.Shape = $decl
            $r.StartFatal = $true
            $startRx = $ShapeStart[$decl]
        } else {
            $r.Shape = $decl + '(unsupported)'
            $infos.Add("SHAPE=$decl is not a supported shape => line-start check stays INFO (supported: " + (($ShapeStart.Keys | Sort-Object) -join ' / ') + ')')
        }
        $declLine = 1
    } elseif ($StrictStart) {
        $r.Shape = 'ts-name-msg(via -StrictStart)'
        $r.StartFatal = $true
    }
    if ($r.StartFatal -and -not $startRx) { $startRx = $ShapeStart['ts-name-msg'] }
    if (-not $startRx) { $startRx = $ShapeStart['ts-name-msg'] }   # undeclared: shape used for INFO probes

    for ($i = 0; $i -lt $lines.Count; $i++) {
        $ln = $lines[$i]
        $no = $i + 1
        if ($ln.Length -gt $r.MaxLine) { $r.MaxLine = $ln.Length }
        if ($ln.Trim().Length -eq 0) { continue }   # blank line is not a violation by itself
        if ($i -lt $declLine) { continue }          # the declaration line itself is exempt

        # 2) glue detector. CALIBRATION (2026-09-24, measured, not assumed):
        #    a naive "line holds >=2 dates" rule is a FALSE-POSITIVE GENERATOR -- honest entries
        #    quote other event times (lock payloads, 'mtime=', 'at='), e.g. u27 line[17] has 2,
        #    u27 line[40] has 3. What actually marks glue is a date that ABUTS a non-separator
        #    char: real defect file (u53shop-heartbeat.raw-pre-split.bak) has 21/21 mid-line
        #    dates preceded by '.', while every legit embedded date in u27/u32/u3b/u44 is
        #    preceded by space or '='. So: flag abutting dates only.
        foreach ($m in [regex]::Matches($ln, $RxTimestamp)) {
            if ($m.Index -eq 0) { continue }
            $pc = [string]$ln[$m.Index - 1]
            if (-not ([regex]::IsMatch($pc, $RxSepBefore))) {
                $from = [Math]::Max(0, $m.Index - 24)
                $ctx = $ln.Substring($from, $m.Index - $from) + '|' + $ln.Substring($m.Index, [Math]::Min(26, $ln.Length - $m.Index))
                $fails.Add("E-GLUE line[$no] char-before='$pc' ctx='$ctx'")
            }
        }
        # 3) line-start shape (catches a newline inserted inside one entry).
        #    -cnotmatch: case-sensitive on purpose, see the note next to $RxAllowedStart.
        #    FATAL only with -StrictStart: the line-start convention is FORMAT-SCOPED, and
        #    enforcing it by default produced 16/69/9 false reds on healthy foreign heartbeats
        #    (select/charstat/u3bverify, 2026-09-24). Default = INFO.
        if ($ln -cnotmatch $startRx) {
            $r.StartMiss++
            $head = $ln.Substring(0, [Math]::Min(70, $ln.Length))
            if ($r.StartFatal) {
                $fails.Add("E-BADSTART-DECL line[$no] head='$head' (violates SHAPE=$($r.Shape))")
            } else {
                $infos.Add("E-BADSTART-INFO line[$no] head='$head'")
            }
        }
        # 4) length (glue without distinguishable timestamps still blows up here)
        if ($ln.Length -gt $MaxLineChars) {
            $fails.Add("E-LONG line[$no] len=$($ln.Length) > max=$MaxLineChars")
        }
    }
    if (-not $r.TailNl) {
        $fails.Add('E-EOF-NEWLINE file does not end with 0x0A (appender forgot its terminator)')
    }
    $r.Fails = $fails.ToArray()
    $r.Infos = $infos.ToArray()
    return $r
}

function Write-Report {
    param($Result, [switch]$Quiet)
    $verdict = if ($Result.Fails.Count -eq 0) { 'OK' } else { 'FAIL' }
    $startShape = if ($Result.StartMiss -eq 0) { 'OK(0)' } elseif ($Result.StartFatal) { 'MISMATCH(' + $Result.StartMiss + ',FATAL)' } else { 'MISMATCH(' + $Result.StartMiss + ',INFO)' }
    $line = 'LINESHAPE path=' + $Result.Path +
            ' bytes=' + $Result.Bytes +
            ' lines=' + $Result.Lines + '(ReadAllLines)' +
            ' trailing_newline=' + $(if ($Result.TailNl) { 'OK' } else { 'NO' }) +
            ' shape=' + $Result.Shape +
            ' start_shape=' + $startShape +
            ' maxline=' + $Result.MaxLine + '/' + $MaxLineChars +
            ' fails=' + $Result.Fails.Count +
            ' infos=' + $Result.Infos.Count +
            ' verdict=' + $verdict
    if (-not $Quiet) { Write-Host ('  ' + $line) } else { Write-Host $line }
    foreach ($f in $Result.Fails) { Write-Host ('    ' + $f) }
    foreach ($f in $Result.Infos) { Write-Host ('    ' + $f + '   [INFO: format-scoped, never affects verdict]') }
    return $verdict
}

function Invoke-Append {
    param([string]$Path, [string]$Text)
    $Path = Resolve-LocalPath $Path
    $payload = $Text
    if (-not $payload.EndsWith("`n")) { $payload = $payload + "`r`n" }   # the whole point
    $dir = Split-Path -Parent $Path
    if ($dir -and -not (Test-Path -LiteralPath $dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
    [System.IO.File]::AppendAllText($Path, $payload, $Utf8NoBom)
    # write-and-read-back: the assertion runs on what is actually on disk
    $res = Test-LogShape -Path $Path -MaxLineChars $MaxLineChars -StrictStart:$StrictStart
    $term = if ($payload.EndsWith("`r`n")) { 'explicit-CRLF' } else { 'NONE' }
    Write-Host ('APPEND terminator=' + $term + ' payload_chars=' + $payload.Length)
    return (Write-Report -Result $res)
}

# ---------------------------------------------------------------- SelfTest
function Invoke-SelfTest {
    $scriptRoot = $PSScriptRoot
    $projectRoot = (Resolve-Path (Join-Path $scriptRoot '..\..\..')).Path
    $testDir = Join-Path $projectRoot '.ai-tmp\test'
    $sandbox = Join-Path $testDir 'shopart-logshape-selftest'
    $realFixed = Join-Path $testDir 'u53shop-heartbeat.txt'
    $realGlued = Join-Path $testDir 'u53shop-heartbeat.raw-pre-split.bak.txt'

    Write-Host 'SHOPART-LOGSHAPE SELFTEST (no editor access, pure offline)'
    $checks = New-Object System.Collections.Generic.List[object]
    function Add-Check([string]$Name, [string]$Got, [string]$Want, [string]$Note) {
        $checks.Add([pscustomobject]@{ Name = $Name; Got = $Got; Want = $Want; Note = $Note })
    }

    if (Test-Path -LiteralPath $sandbox) { Remove-Item -LiteralPath $sandbox -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $sandbox | Out-Null
    try {
        $t1 = '2026-09-24 10:00:00 self one'
        $t2 = '2026-09-24 10:01:00 self two'
        $t3 = '2026-09-24 10:02:00 self three'

        # ---- POSITIVE: the sanctioned writer (explicit CRLF + write-and-read-back) stays green
        $pos = Join-Path $sandbox 'pos_append_api.txt'
        foreach ($t in @($t1, $t2, $t3)) { [void](Invoke-Append -Path $pos -Text $t) }
        $rp = Test-LogShape -Path $pos -MaxLineChars $MaxLineChars
        Add-Check 'POS-synthetic-append-api' $(if ($rp.Fails.Count -eq 0) { 'OK' } else { 'FAIL' }) 'OK' ('fails=' + $rp.Fails.Count)

        # ---- NEGATIVE 0 (REAL): the actual pre-fix heartbeat, kept as the historical injection
        if (Test-Path -LiteralPath $realGlued) {
            $rg = Test-LogShape -Path $realGlued -MaxLineChars $MaxLineChars
            $glueHit = @($rg.Fails | Where-Object { $_ -like 'E-GLUE*' }).Count
            Add-Check 'NEG-real-pre-fix-backup' $(if ($rg.Fails.Count -gt 0 -and $glueHit -gt 0) { 'FAIL' } else { 'OK' }) 'FAIL' ('fails=' + $rg.Fails.Count + ' glue=' + $glueHit)
        } else {
            Add-Check 'NEG-real-pre-fix-backup' 'SKIP' 'FAIL' 'injection file absent (temp dir cleaned)'
        }

        # ---- NEGATIVE 1 (REAL, must be green): the repaired heartbeat
        if (Test-Path -LiteralPath $realFixed) {
            $rf = Test-LogShape -Path $realFixed -MaxLineChars $MaxLineChars
            Add-Check 'POS-real-repaired-heartbeat' $(if ($rf.Fails.Count -eq 0) { 'OK' } else { 'FAIL' }) 'OK' ('fails=' + $rf.Fails.Count)
        } else {
            Add-Check 'POS-real-repaired-heartbeat' 'SKIP' 'OK' 'heartbeat absent'
        }

        # ---- NEGATIVE 2 (SYNTHETIC: exact mechanism) entry2 unterminated, then entry3 appended
        $glue = Join-Path $sandbox 'neg_glue.txt'
        [System.IO.File]::WriteAllText($glue, ($t1 + "`r`n" + $t2), $Utf8NoBom)          # no trailing NL
        [System.IO.File]::AppendAllText($glue, ($t3 + "`r`n"), $Utf8NoBom)               # glues onto t2
        $rg2 = Test-LogShape -Path $glue -MaxLineChars $MaxLineChars
        $glueHit2 = @($rg2.Fails | Where-Object { $_ -like 'E-GLUE*' }).Count
        Add-Check 'NEG-synthetic-glue' $(if ($rg2.Fails.Count -gt 0) { 'FAIL' } else { 'OK' }) 'FAIL' ('fails=' + $rg2.Fails.Count + ' glue=' + $glueHit2)

        # ---- NEGATIVE 3 (SYNTHETIC: appender forgot the terminator only)
        $eof = Join-Path $sandbox 'neg_eof.txt'
        [System.IO.File]::WriteAllText($eof, ($t1 + "`r`n" + $t2), $Utf8NoBom)
        $re = Test-LogShape -Path $eof -MaxLineChars $MaxLineChars
        $eofHit = @($re.Fails | Where-Object { $_ -like 'E-EOF-NEWLINE*' }).Count
        Add-Check 'NEG-synthetic-no-trailing-newline' $(if ($eofHit -gt 0) { 'FAIL' } else { 'OK' }) 'FAIL' ('fails=' + $re.Fails.Count)

        # ---- NEGATIVE 4 (SYNTHETIC: newline inserted INSIDE one entry -> continuation line).
        #      BOTH halves are checked: default must report it as INFO (not fatal), and the very
        #      same bytes under -StrictStart MUST be fatal. A rule that can never fail is
        #      worthless; a rule that fails on foreign formats is a false-red generator
        #      (this file's first revision did exactly that: 16/69/9 false reds on healthy
        #      select / classcols / u3bverify heartbeats).
        $bad = Join-Path $sandbox 'neg_badstart.txt'
        [System.IO.File]::WriteAllText($bad, ($t1 + "`r`nprose continuation of the previous entry`r`n"), $Utf8NoBom)
        $rb = Test-LogShape -Path $bad -MaxLineChars $MaxLineChars
        $badInfo = @($rb.Infos | Where-Object { $_ -like 'E-BADSTART*' }).Count
        Add-Check 'NEG-bad-line-start(default=INFO)' $(if ($rb.Fails.Count -eq 0 -and $badInfo -gt 0) { 'INFO' } else { 'WRONG' }) 'INFO' ('fails=' + $rb.Fails.Count + ' info=' + $badInfo)
        $rbs = Test-LogShape -Path $bad -MaxLineChars $MaxLineChars -StrictStart
        $badFatal = @($rbs.Fails | Where-Object { $_ -like 'E-BADSTART*' }).Count
        Add-Check 'NEG-bad-line-start(-StrictStart)' $(if ($badFatal -gt 0) { 'FAIL' } else { 'OK' }) 'FAIL' ('fatal=' + $badFatal + ' fails=' + $rbs.Fails.Count)

        # ---- NEGATIVE 5 (SYNTHETIC: one entry that never got split, timestamps not distinguishable)
        $long = Join-Path $sandbox 'neg_long.txt'
        [System.IO.File]::WriteAllText($long, ($t1 + (' x' * ($MaxLineChars + 50)) + "`r`n"), $Utf8NoBom)
        $rl = Test-LogShape -Path $long -MaxLineChars $MaxLineChars
        $longHit = @($rl.Fails | Where-Object { $_ -like 'E-LONG*' }).Count
        Add-Check 'NEG-synthetic-overlong-line' $(if ($longHit -gt 0) { 'FAIL' } else { 'OK' }) 'FAIL' ('fails=' + $rl.Fails.Count)

        # ---- FALSE-POSITIVE CONTROL (SYNTHETIC): honest entries that QUOTE other event times.
        #      This is the shape that made the naive ">=2 dates per line" rule cry wolf
        #      (u27 line[17]/[40]/[97]/[103], u32 line[31], u3b line[25], u44 line[17]).
        $emb = Join-Path $sandbox 'pos_embedded_dates.txt'
        $l1 = '2026-09-24T12:38:26.3439178+08:00 jitter U27: lock=[u52resist 2026-09-24T12:38:23.1976806+08:00 39792] play.lock=ABSENT'
        $l2 = '2026-09-24 14:19:51  REPORT-FINGERPRINT report=report-u32.md sha256_16=a40003cd15926833 mtime=2026-09-24T14:19:44'
        [System.IO.File]::WriteAllText($emb, ($l1 + "`r`n" + $l2 + "`r`n"), $Utf8NoBom)
        $re2 = Test-LogShape -Path $emb -MaxLineChars $MaxLineChars
        Add-Check 'POS-embedded-dates-not-glue' $(if ($re2.Fails.Count -eq 0) { 'OK' } else { 'FAIL' }) 'OK' ('fails=' + $re2.Fails.Count)

        # ---- FALSE-POSITIVE CONTROL (REAL): another sheet's heartbeat, must stay green.
        #      Skipped if that file is gone (it is not mine to keep).
        $u27 = Join-Path $testDir 'u27-heartbeat.txt'
        if (Test-Path -LiteralPath $u27) {
            $ru = Test-LogShape -Path $u27 -MaxLineChars $MaxLineChars
            Add-Check 'POS-real-foreign-heartbeat(u27)' $(if ($ru.Fails.Count -eq 0) { 'OK' } else { 'FAIL' }) 'OK' ('fails=' + $ru.Fails.Count)
        } else {
            Add-Check 'POS-real-foreign-heartbeat(u27)' 'SKIP' 'OK' 'file absent'
        }

        # ---- FALSE-RED REGRESSION (REAL foreign formats): the four shapes the first revision
        #      judged FAIL while healthy (select field-style / classcols bracket-ISO /
        #      u3bverify prose-head / charstat mixed). Assert precisely the thing that was fixed:
        #      NO format-scoped E-BADSTART may be FATAL. Any other hard defect those files may
        #      carry is reported in the note, deliberately NOT as this control failing.
        foreach ($pair in @(
                @{ n = 'u44-heartbeat.txt'; tag = 'select/field-style' },
                @{ n = 'classcols-heartbeat.txt'; tag = 'classcols/bracket-ISO' },
                @{ n = 'u3b-heartbeat.txt'; tag = 'u3bverify/prose-head' },
                @{ n = 'u3charstat-heartbeat.txt'; tag = 'charstat/mixed' })) {
            $fp = Join-Path $testDir $pair.n
            $nm = 'POS-no-false-red(' + $pair.n + ')'
            if (Test-Path -LiteralPath $fp) {
                $rr = Test-LogShape -Path $fp -MaxLineChars $MaxLineChars
                $bsFatal = @($rr.Fails | Where-Object { $_ -like 'E-BADSTART*' }).Count
                Add-Check $nm $(if ($bsFatal -eq 0) { 'OK' } else { 'FAIL' }) 'OK' ('badstart_fatal=' + $bsFatal + ' other_fails=' + ($rr.Fails.Count - $bsFatal) + ' info=' + $rr.Infos.Count + ' ' + $pair.tag)
            } else {
                Add-Check $nm 'SKIP' 'OK' ('absent ' + $pair.tag)
            }
        }

        # ---- FALSE-RED REGRESSION (SYNTHETIC, deterministic): the three foreign line shapes
        #      reproduce the false reds even if the real files get re-formatted by their owners
        #      (measured: select re-wrote u44-heartbeat.txt at 15:55 => that real control went
        #      vacuous, so the instrument must not depend on it).
        $foreign = Join-Path $sandbox 'pos_foreign_shapes.txt'
        # ASCII-only on purpose: an asset with non-ASCII would depend on the reader's codepage
        $f1 = '[2026-09-24T11:11:03.3453871+08:00] classcols: table-column reading ...'   # bracket-ISO
        $f2 = 'select: u44 hover-tier _Contrast=1.01 vs normal-tier 1.0'                  # field-style
        $f3 = '  prose continuation written by a here-string (no entry header of its own)' # continuation
        [System.IO.File]::WriteAllText($foreign, ($f1 + "`r`n" + $f2 + "`r`n" + $f3 + "`r`n"), $Utf8NoBom)
        $rfo = Test-LogShape -Path $foreign -MaxLineChars $MaxLineChars
        $bsFatalF = @($rfo.Fails | Where-Object { $_ -like 'E-BADSTART*' }).Count
        Add-Check 'POS-foreign-shapes(default)' $(if ($rfo.Fails.Count -eq 0 -and $bsFatalF -eq 0 -and $rfo.Infos.Count -ge 2) { 'OK' } else { 'WRONG' }) 'OK' ('fails=' + $rfo.Fails.Count + ' info=' + $rfo.Infos.Count)
        $rfoS = Test-LogShape -Path $foreign -MaxLineChars $MaxLineChars -StrictStart
        Add-Check 'POS-foreign-shapes(-StrictStart)' $(if ($rfoS.Fails.Count -ge 2) { 'FAIL' } else { 'WRONG' }) 'FAIL' ('fails=' + $rfoS.Fails.Count + ' (bracket-ISO must stay legal => exactly the 2 non-date starts)')

        # ---- SHAPE DECLARATION (team ruling, README 66): declared => strict AND no false red;
        #      undeclared => INFO; an UNSUPPORTED declaration must NOT silently widen the rule.
        $decIso = Join-Path $sandbox 'shape_iso.txt'
        $isoA = '[2026-09-24T11:11:03.3453871+08:00] classcols: table-column reading'
        $isoB = '2026-09-24 11:12:00 classcols: second line'
        $isoC = 'REPORT-FINGERPRINT sha256_16=deadbeef bytes=1 lines=1'
        [System.IO.File]::WriteAllText($decIso, ('SHAPE=iso-msg' + "`r`n" + $isoA + "`r`n" + $isoB + "`r`n" + $isoC + "`r`n"), $Utf8NoBom)
        $rdi = Test-LogShape -Path $decIso -MaxLineChars $MaxLineChars
        Add-Check 'POS-shape-declared(iso-msg)' $(if ($rdi.Fails.Count -eq 0 -and $rdi.StartMiss -eq 0 -and $rdi.StartFatal) { 'OK' } else { 'WRONG' }) 'OK' ('fails=' + $rdi.Fails.Count + ' start_miss=' + $rdi.StartMiss + ' fatal_mode=' + $rdi.StartFatal)

        $decBad = Join-Path $sandbox 'shape_iso_violation.txt'
        [System.IO.File]::WriteAllText($decBad, ('SHAPE=iso-msg' + "`r`n" + $isoA + "`r`n" + 'lowercase prose that violates the declared shape' + "`r`n"), $Utf8NoBom)
        $rdb = Test-LogShape -Path $decBad -MaxLineChars $MaxLineChars
        $decFatal = @($rdb.Fails | Where-Object { $_ -like 'E-BADSTART-DECL*' }).Count
        Add-Check 'NEG-shape-declared-violation' $(if ($decFatal -gt 0) { 'FAIL' } else { 'OK' }) 'FAIL' ('decl_fatal=' + $decFatal + ' fails=' + $rdb.Fails.Count)

        $decCaps = Join-Path $sandbox 'shape_caps.txt'
        [System.IO.File]::WriteAllText($decCaps, ('SHAPE=caps-tag' + "`r`n" + 'ASK : team-lead asked a question' + "`r`n" + 'FIX : landed' + "`r`n" + 'RAW : 41/41 rows' + "`r`n"), $Utf8NoBom)
        $rdc = Test-LogShape -Path $decCaps -MaxLineChars $MaxLineChars
        Add-Check 'POS-shape-declared(caps-tag,3-char)' $(if ($rdc.Fails.Count -eq 0 -and $rdc.StartMiss -eq 0) { 'OK' } else { 'WRONG' }) 'OK' ('fails=' + $rdc.Fails.Count + ' start_miss=' + $rdc.StartMiss)

        $decUnk = Join-Path $sandbox 'shape_unknown.txt'
        [System.IO.File]::WriteAllText($decUnk, ('SHAPE=kv-ledger' + "`r`n" + 'slice=shopart status=green' + "`r`n" + 'scope=no client/** changed' + "`r`n"), $Utf8NoBom)
        $rdu = Test-LogShape -Path $decUnk -MaxLineChars $MaxLineChars
        Add-Check 'POS-shape-unsupported-stays-INFO' $(if ($rdu.Fails.Count -eq 0 -and $rdu.Infos.Count -ge 2) { 'OK' } else { 'WRONG' }) 'OK' ('fails=' + $rdu.Fails.Count + ' info=' + $rdu.Infos.Count + ' (no widening added for kv ledgers)')

        # ---- E-GLUE width, BOTH directions (select measured: a SINGLE date abutting a
        #      non-separator, e.g. '(2026-09-24 ...', also reds; one space turns it OK).
        $gAb = Join-Path $sandbox 'glue_single_abutting.txt'
        [System.IO.File]::WriteAllText($gAb, ('2026-09-24 11:00:00 u44impl: see' + $t1 + ')' + "`r`n"), $Utf8NoBom)
        $rga = Test-LogShape -Path $gAb -MaxLineChars $MaxLineChars
        $gaHit = @($rga.Fails | Where-Object { $_ -like 'E-GLUE*' }).Count
        Add-Check 'NEG-glue-single-abutting-date' $(if ($gaHit -gt 0) { 'FAIL' } else { 'OK' }) 'FAIL' ('glue=' + $gaHit + ' (documented width: add a separator char to clean it)')

        $gSp = Join-Path $sandbox 'glue_same_line_with_space.txt'
        [System.IO.File]::WriteAllText($gSp, ('2026-09-24 11:00:00 u44impl: see (' + $t1 + ')' + "`r`n"), $Utf8NoBom)
        $rgs = Test-LogShape -Path $gSp -MaxLineChars $MaxLineChars
        Add-Check 'POS-glue-same-line-with-space' $(if ($rgs.Fails.Count -eq 0) { 'OK' } else { 'WRONG' }) 'OK' ('fails=' + $rgs.Fails.Count)

        $gJoin = Join-Path $sandbox 'glue_two_timestamps_joined.txt'
        [System.IO.File]::WriteAllText($gJoin, ($t1 + $t2 + "`r`n"), $Utf8NoBom)   # R21 exact signature
        $rgj = Test-LogShape -Path $gJoin -MaxLineChars $MaxLineChars
        $gjHit = @($rgj.Fails | Where-Object { $_ -like 'E-GLUE*' }).Count
        Add-Check 'NEG-glue-joined-timestamps-still-red' $(if ($gjHit -gt 0) { 'FAIL' } else { 'WRONG' }) 'FAIL' ('glue=' + $gjHit + ' (the demotion must not weaken this)');

        # ---- my own heartbeat must ALSO satisfy the STRICT line-start contract
        if (Test-Path -LiteralPath $realFixed) {
            $rs = Test-LogShape -Path $realFixed -MaxLineChars $MaxLineChars -StrictStart
            Add-Check 'POS-own-heartbeat(-StrictStart)' $(if ($rs.Fails.Count -eq 0) { 'OK' } else { 'FAIL' }) 'OK' ('fails=' + $rs.Fails.Count + ' start_miss=' + $rs.StartMiss)
        } else {
            Add-Check 'POS-own-heartbeat(-StrictStart)' 'SKIP' 'OK' 'absent'
        }

        # ---- NEGATIVE 6 (SYNTHETIC: missing file -> must not be a silent pass)
        $rm = Test-LogShape -Path (Join-Path $sandbox 'does_not_exist.txt') -MaxLineChars $MaxLineChars
        Add-Check 'NEG-missing-file' $(if ($rm.Fails.Count -gt 0) { 'FAIL' } else { 'OK' }) 'FAIL' ('fails=' + $rm.Fails.Count)

        # sheet
        Write-Host ''
        Write-Host ('{0,-34} {1,-5} {2,-5} {3}' -f 'CASE', 'GOT', 'WANT', 'NOTE')
        foreach ($c in $checks) {
            Write-Host ('{0,-34} {1,-5} {2,-5} {3}' -f $c.Name, $c.Got, $c.Want, $c.Note)
        }
        $bad = @($checks | Where-Object { $_.Got -ne $_.Want })
        Write-Host ''
        Write-Host ('SELFTEST RESULT ' + $checks.Count + ' checks, fail=' + $bad.Count + ', skipped=' + @($checks | Where-Object { $_.Got -eq 'SKIP' }).Count)
        return $bad.Count
    }
    finally {
        if (Test-Path -LiteralPath $sandbox) { Remove-Item -LiteralPath $sandbox -Recurse -Force }
    }
}

# ---------------------------------------------------------------- dispatch
# Every run self-identifies, so a reading can be cited WITH the version that produced it.
function Write-JudgeVersion {
    $p = $PSCommandPath
    if (-not $p) { return }
    try {
        $b = [System.IO.File]::ReadAllBytes($p)
        Write-Host ('JUDGE-VERSION ' + [System.IO.Path]::GetFileName($p) +
            ' sha256_16=' + (Get-FileHash -LiteralPath $p -Algorithm SHA256).Hash.Substring(0, 16).ToLower() +
            ' bytes=' + $b.Length +
            ' lines=' + ([System.IO.File]::ReadAllLines($p).Count) + '(ReadAllLines)' +
            ' mtime=' + (Get-Item -LiteralPath $p).LastWriteTime.ToString('yyyy-MM-dd HH:mm:ss'))
    } catch {
        Write-Host ('JUDGE-VERSION unknown (' + $_.Exception.Message + ')')
    }
}

$modeCount = @($Check, $Append, $SelfTest) | Where-Object { $_ } | Measure-Object | Select-Object -ExpandProperty Count
if ($modeCount -ne 1) {
    Write-Host 'usage: shopart_logshape.ps1 -Check -Path <file> | -Append -Path <file> -Text <s> | -SelfTest'
    exit 2
}
Write-JudgeVersion

try {
    if ($SelfTest) {
        $n = Invoke-SelfTest
        exit ([int]($n -gt 0))
    }

    if (-not $Path) { Write-Host 'ERR -Path is required'; exit 2 }
    if ($Append) {
        if (-not $Text) { Write-Host 'ERR -Text is required with -Append'; exit 2 }
        $v = Invoke-Append -Path $Path -Text $Text
    } else {
        Write-Host ('SHOPART-LOGSHAPE CHECK strict_start=' + [bool]$StrictStart)
        $res = Test-LogShape -Path $Path -MaxLineChars $MaxLineChars -StrictStart:$StrictStart
        $v = Write-Report -Result $res
    }
    Write-Host ('RESULT ' + $v)
    exit ([int]($v -ne 'OK'))
} catch {
    # A crashed judge must NOT look green: no verdict line would be emitted otherwise.
    Write-Host ('RESULT FAIL(TOOL-ERROR: ' + $_.Exception.Message + ')')
    exit 3
}
