# =============================================================================
# camverify_run.ps1 -- ONE Play session for the cam-verify piece (2026-09-24).
#
#   powershell -NoProfile -ExecutionPolicy Bypass -File camverify_run.ps1 -Tag cv1
#
# Chain: play.lock -> editor_status(compiling:false / playMode:stopped) ->
#        recompile -> clear_console -> editor_play -> S2.Api.Cfg -> S2.Tour.Install
#        (the menu chain) -> as soon as a map is live: CV.Api.Freeze (stop the tour
#        driver from steering player+camera) -> CV.Api.Sweep -> for each of the 8
#        outermost WALKABLE cells: CV.Api.Place + screenshot + reading ->
#        CV.Api.Travel 1 (Blood Moor) -> same sweep on the 80x80 chunked map ->
#        editor_stop + release the lock.
#
# WHY a live session: "is the void inside the view at the edge" and "is the player
# still on screen" are live-state / visual judgements; the offline hosts only
# assert the pure math of the same definitions.
#
# Reuses (unmodified): tools/probes/drivers/s2_drive.cs (menu chain),
#                       tools/probes/drivers/blackwhy_probe.cs (BWy.Api.Shot).
# New: tools/probes/drivers/camverify_probe.cs (CV.Api.*).
# ASCII only (PS 5.1 parses a BOM-less non-ASCII .ps1 as ANSI).
# =============================================================================
param(
    [string]$Tag = 'cv1',
    [string]$Save = 'g66',
    [string]$Why = 'cam-verify: at the outermost WALKABLE cell the two criteria (visible grid rect inside the map / the player still inside the viewport) can only be judged on a real frame; the same session also reads the follow screen distance (player vs camera) which no offline host can produce',
    [int]$TravelArea = 1
)

$ErrorActionPreference = 'Continue'

$root    = 'c:/Work/Server/f-v2/clover-project-diablo2'
$proj    = $root + '/client'
$csCV    = $root + '/tools/probes/drivers/camverify_probe.cs'
$csShot  = $root + '/tools/probes/drivers/blackwhy_probe.cs'
$csTour  = $root + '/tools/probes/drivers/s2_drive.cs'
$test    = $root + '/.ai-tmp/test'
# NOTE (measured 2026-09-24 02:20 / 02:23): a concurrent piece's cleanup removed files out
# of the SHARED .ai-tmp/screenshots + .ai-tmp/test while this runner was live (6 of my PNGs
# and the whole per-point log of the earlier tag vanished). So: own subdirectory + a non
# "cv_*" log name, and re-verify every artifact at the end of the run.
$shots   = $root + '/.ai-tmp/screenshots/camverify'
$heart   = $test + '/heartbeat-camverify.txt'
$trace   = $test + '/camverify_steps_' + $Tag + '.txt'
$readOut = $test + '/camverify_readings_' + $Tag + '.txt'
$playLog = $test + '/play-log.tsv'
$lock    = $test + '/play.lock'

$script:cli = 0

function Say([string]$s) {
    $stamp = (Get-Date).ToString('HH:mm:ss.fff')
    Write-Host ($stamp + ' ' + $s)
    Add-Content -Path $trace -Value ($stamp + ' ' + $s) -Encoding UTF8
    Add-Content -Path $readOut -Value ($stamp + ' ' + $s) -Encoding UTF8
    try { [System.IO.File]::WriteAllText($heart, ($stamp + ' [cam-verify] ' + $s)) } catch { }
}

# keep the lock's mtime fresh: a concurrent runner is allowed to steal a lock older than
# 6 min (documented rule), and that is exactly what happened at 02:24 during the first run.
function Touch-Lock() {
    try { (Get-Item $lock).LastWriteTime = Get-Date } catch { }
}

function Clip([string]$s, [int]$n) {
    if ($null -eq $s) { return '' }
    $one = $s -replace "`r?`n", ' '
    if ($one.Length -le $n) { return $one }
    return $one.Substring(0, $n)
}

function Unity-Cmd([string[]]$argv, [switch]$Quiet) {
    Touch-Lock
    $script:cli = $script:cli + 1
    $raw = & unity command @argv --format json --no-pager 2>&1 | Out-String
    if (-not $Quiet) { Say ('CLI ' + ($argv -join ' ') + ' -> ' + $raw.Length + ' bytes') }
    return $raw
}

function Run-Step([string]$file, [string]$entry, [string]$arg) {
    if ([string]::IsNullOrEmpty($arg)) {
        $r = Unity-Cmd @('run_script', '--file', $file, '--entry', $entry)
    } else {
        $r = Unity-Cmd @('run_script', '--file', $file, '--entry', $entry, '--args', ('[\"' + $arg + '\"]'))
    }
    $result = '?'
    try {
        $j = $r | ConvertFrom-Json
        $v = $j.data.result.result
        if ($null -eq $v) { $v = $j.data.result.error }
        if ($null -eq $v) {
            $d = @($j.data.result.diagnostics | Where-Object { $_.severity -eq 'error' })
            if ($d.Count -gt 0) { $v = 'COMPILE-ERRORS:' + $d.Count + ' ' + (Clip (($d | ForEach-Object { $_.id + ' line ' + $_.line + ': ' + $_.message }) -join ' | ') 400) }
        }
        $result = [string]$v
    } catch { $result = 'PARSE-FAIL' }
    Say ('STEP ' + $entry + ' ' + (Clip $result 400))
    return $result
}

function Shot([string]$path) {
    $r = Run-Step $csShot 'BWy.Api.Shot' $path
    Start-Sleep -Seconds 2
    $sz = -1
    if (Test-Path $path) { $sz = (Get-Item $path).Length }
    Say ('SHOT ' + $path + ' bytes=' + $sz)
    return $sz
}

function Release-Lock() {
    if (-not (Test-Path $lock)) { Say 'LOCK-RELEASE skip (no lock file)'; return }
    $body = ''
    try { $body = (Get-Content $lock -Raw) } catch { $body = '' }
    if ($body -match '^\s*cam-verify\b') {
        Remove-Item $lock -Force -ErrorAction SilentlyContinue
        Say 'LOCK-RELEASED (mine)'
    } else {
        Say ('LOCK-RELEASE REFUSED (owned by ' + $body.Trim() + ') -> left alone')
    }
}

function SweepArea([string]$label, [string]$what) {
    $sw = Run-Step $csCV 'CV.Api.Sweep' ''
    Say ('SWEEP[' + $label + '] ' + $sw)
    if ($sw -notmatch 'SWEEP map=') { Say ('SWEEP-ABORT ' + $label); return 0 }

    $names = @('mm', 'mM', 'Mm', 'MM', 'Wmid', 'Emid', 'Nmid', 'Smid')
    $n = 0
    foreach ($nm in $names) {
        $m = [regex]::Match($sw, '\|\s*' + $nm + '=(-?\d+),(-?\d+)')
        if (-not $m.Success) { Say ('POINT-MISSING ' + $nm); continue }
        $spec = $m.Groups[1].Value + ',' + $m.Groups[2].Value
        $r = Run-Step $csCV 'CV.Api.Place' $spec
        Start-Sleep -Seconds 2
        $r2 = Run-Step $csCV 'CV.Api.Ping' ''
        Say ('EDGE[' + $label + '/' + $nm + '] ' + (Clip $r2 460))
        Shot ($shots + '/camverify_' + $what + '_' + $nm + '.png') | Out-Null
        $n = $n + 1
    }
    return $n
}

# =============================================================================
# main
# =============================================================================
foreach ($d in @($shots, $test)) { if (-not (Test-Path $d)) { New-Item -ItemType Directory -Path $d | Out-Null } }
Set-Content -Path $trace -Value ('# cam-verify steps tag=' + $Tag) -Encoding UTF8
Set-Content -Path $readOut -Value ('# cam-verify readings tag=' + $Tag) -Encoding UTF8
Set-Location -LiteralPath $proj
Say ('BEGIN tag=' + $Tag + ' cwd=' + (Get-Location).Path)

foreach ($f in @($csCV, $csShot, $csTour)) { if (-not (Test-Path $f)) { Say ('ABORT missing ' + $f); exit 1 } }

$st0 = Unity-Cmd @('editor_status') -Quiet
Say ('EDITOR-STATUS ' + (Clip $st0 300))

# ---- play lock -------------------------------------------------------------
for ($i = 0; $i -lt 15; $i++) {
    if (-not (Test-Path $lock)) { break }
    $age = ((Get-Date) - (Get-Item $lock).LastWriteTime).TotalMinutes
    if ($age -ge 6) { Say ('LOCK-STALE age=' + [math]::Round($age, 1) + 'm -> taking it'); break }
    Say ('LOCK-BUSY age=' + [math]::Round($age, 1) + 'm -> wait 30s (try ' + ($i + 1) + ')')
    Start-Sleep -Seconds 30
}
Set-Content -Path $lock -Value ('cam-verify ' + (Get-Date).ToString('o')) -Encoding UTF8
Say ('LOCK-TAKEN ' + $lock)

try {
    for ($i = 0; $i -lt 40; $i++) {
        $st = Unity-Cmd @('editor_status') -Quiet
        try {
            $js = $st | ConvertFrom-Json
            $c = $js.data.result.compiling
            $pm = $js.data.result.playMode
            if ($c -eq $false -and $pm -eq 'stopped') { Say ('EDITOR-STATUS compiling=' + $c + ' playMode=' + $pm); break }
        } catch { }
        if ($i % 6 -eq 0) { Say ('EDITOR-WAIT (' + $i + ')') }
        Start-Sleep -Seconds 5
    }
    $stF = Unity-Cmd @('editor_status') -Quiet
    $pmF = '(unknown)'
    try { $jsF = $stF | ConvertFrom-Json; $pmF = [string]$jsF.data.result.playMode } catch { }
    if ($pmF -ne 'stopped') {
        Say ('ABORT editor playMode=' + $pmF + ' -> not mine, leaving it alone')
        Release-Lock
        exit 1
    }
    Unity-Cmd @('editor_stop') -Quiet | Out-Null
    Start-Sleep -Seconds 2

    Unity-Cmd @('recompile') -Quiet | Out-Null
    $rc = '(unknown)'
    for ($i = 0; $i -lt 45; $i++) {
        Start-Sleep -Seconds 2
        $rj = Unity-Cmd @('recompile_status') -Quiet
        try { $rc = ([string]($rj | ConvertFrom-Json).data.result | ConvertFrom-Json).status } catch { }
        if ($rc -match 'up_to_date|completed|idle') { break }
    }
    Say ('RECOMPILE status=' + $rc)

    Add-Content -Path $playLog -Value ((Get-Date).ToString('yyyy-MM-dd HH:mm') + "`tcam-verify`tcv-" + $Tag + "`t" + $Why) -Encoding UTF8
    Say ('PLAYLOG-APPENDED ' + $playLog)

    Unity-Cmd @('clear_console') -Quiet | Out-Null
    Unity-Cmd @('editor_play') -Quiet | Out-Null
    Start-Sleep -Seconds 8

    $cfgOk = $false
    for ($i = 0; $i -lt 10; $i++) {
        $r = Run-Step $csTour 'S2.Api.Cfg' ''
        if ($r -match 'CFG-OK') { $cfgOk = $true; break }
        Start-Sleep -Seconds 3
    }
    Say ('CFG-OK ' + $cfgOk)
    if (-not $cfgOk) { Say 'ABORT S2.Api.Cfg never answered' }

    $spec = $Tag + '|' + $Save + '|' + ($test + '/cv_done_' + $Tag + '.txt') + '|' + ($test + '/cvraw_' + $Tag)
    $raw = Unity-Cmd @('run_script', '--file', $csTour, '--entry', 'S2.Tour.Install', '--args', ('[\"' + $spec + '\"]'))
    Say ('INSTALL ' + (Clip $raw 300))

    # ---- wait for the FIRST live map (the tour hits Town before it travels) ----
    $live = ''
    for ($i = 0; $i -lt 60; $i++) {
        $p = Run-Step $csCV 'CV.Api.Ping' ''
        if ($p -match 'map=1 ') { $live = $p; break }
        Start-Sleep -Seconds 2
    }
    Say ('LIVE ' + (Clip $live 400))
    if ($live -eq '') { Say 'ABORT never reached a live map' }
    else {
        # FREEZE first: the tour keeps steering player+camera every frame (and it would
        # walk to the waypoint and travel away on its own).
        Say ('FREEZE ' + (Run-Step $csCV 'CV.Api.Freeze' ''))
        Start-Sleep -Seconds 2

        $p1 = Run-Step $csCV 'CV.Api.Ping' ''
        Say ('PHASE1 ' + (Clip $p1 400))
        $size1 = '?'
        $m1 = [regex]::Match($p1, 'map=(\d+x\d+)')
        if ($m1.Success) { $size1 = $m1.Groups[1].Value }
        Say ('PHASE1-SIZE ' + $size1)
        [void](SweepArea ('p1_' + $size1) ('a1_' + $size1))

        # ---- second area over the one legitimate chain ------------------------
        $other = if ($size1 -eq '80x80') { 0 } else { $TravelArea }
        $want  = if ($size1 -eq '80x80') { '56x40' } else { '80x80' }
        $tv = Run-Step $csCV 'CV.Api.Travel' ([string]$other)
        Say ('TRAVEL ' + (Clip $tv 200))
        $ok2 = $false
        for ($i = 0; $i -lt 40; $i++) {
            Start-Sleep -Seconds 3
            $p2 = Run-Step $csCV 'CV.Api.Ping' ''
            if ($p2 -match ('map=' + $want)) { $ok2 = $true; Say ('PHASE2 ' + (Clip $p2 400)); break }
        }
        if ($ok2) {
            Start-Sleep -Seconds 4            # let the (possibly chunked) map build
            [void](SweepArea ('p2_' + $want) ('a2_' + $want))
        } else { Say ('PHASE2-MISS want=' + $want) }

        # ---- production clamp vs "完全跟随" (cap=0) at the same cells ----------
        foreach ($sp in @('0,0', '40,40')) {
            $r0 = Run-Step $csCV 'CV.Api.Place' $sp
            Say ('CONTRAST-PROD ' + $sp + ' ' + (Clip $r0 300))
            Start-Sleep -Seconds 2
            Shot ($shots + '/camverify_contrast_prod_' + ($sp -replace ',', '_') + '.png') | Out-Null
            $rp = Run-Step $csCV 'CV.Api.PinOn' ''
            Say ('CONTRAST-FOLLOW ' + $sp + ' ' + (Clip $rp 300))
            Start-Sleep -Seconds 2
            Shot ($shots + '/camverify_contrast_follow_' + ($sp -replace ',', '_') + '.png') | Out-Null
            Run-Step $csCV 'CV.Api.PinOff' '' | Out-Null
        }
    }

    $cj = Unity-Cmd @('console_status') -Quiet
    Say ('CONSOLE ' + (Clip $cj 300))

    Unity-Cmd @('editor_stop') -Quiet | Out-Null
    Say 'STOPPED'

    # ---- artifact re-verification (a concurrent cleanup ate files mid-run before) ----
    $pngs = @(Get-ChildItem -Path $shots -Filter '*.png' -ErrorAction SilentlyContinue)
    Say ('ARTIFACT png=' + $pngs.Count + ' [' + (($pngs | ForEach-Object { $_.Name }) -join ',') + ']')
    $missing = 0
    foreach ($n in @('mm', 'mM', 'Mm', 'MM', 'Wmid', 'Emid', 'Nmid', 'Smid')) {
        foreach ($a in @('a1', 'a2')) {
            $p = Get-ChildItem -Path $shots -Filter ('camverify_' + $a + '_*_' + $n + '.png') -ErrorAction SilentlyContinue
            if (-not $p) { $missing = $missing + 1 }
        }
    }
    Say ('ARTIFACT missingEdgeShots=' + $missing + ' (0 = every edge point has its picture)')
} finally {
    try { Unity-Cmd @('editor_stop') -Quiet | Out-Null } catch { }
    Release-Lock
    Say ('END cli=' + $script:cli)
}
