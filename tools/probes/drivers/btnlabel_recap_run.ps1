# =============================================================================
# btnlabel_recap_run.ps1 -- btn-label-fix slice: the ONE re-capture chain for the rows
# affected by the global button-label colour change (team-lead ruling (1), 2026-09-24).
#
# WHY: the shared constants UiArt.ButtonText / UiLayoutFlow.ButtonText changed
# (0.91,0.82,0.52 -> 0.95,0.87,0.60), so every acceptance row whose evidence picture
# contains text drawn with that constant is an AFFECTED ROW (SKILL 4.6/4.8) -- that is
# the boot hint line, the main-menu / char-select / char-create button labels, the
# inventory cell counts, the NPC dialog options and the shop slots.  Ruling (1) says:
# re-capture them, do NOT register them away as "below the perception threshold".
#
# WHY THIS CHAIN: x_drive.cs already walks exactly those screens and writes FULL-FRAME
# tiles (t0e_drive's tiles are cropped to each Selectable, so they cannot show
# non-interactive text such as the inventory cell counts or the boot hint).
#
# WHY A PRIVATE RAW DIR (spec field 3): x_drive writes its tiles under the dir given in
# the spec.  x_run.ps1 points that at .ai-tmp/screenshots/ itself, so running IT would
# overwrite the 80+ delivered x_* tiles (other rows' frozen evidence).  Here the tiles
# land in .ai-tmp/screenshots/recap_btnlabel/ and I copy out ONLY the rows I am
# re-capturing, as btnlabel_recap_*.png -- zero blast radius on the other rows.
# (Also: x_run.ps1 takes NO play lock and logs clover-impl as the actor.)
#
#   powershell -NoProfile -ExecutionPolicy Bypass -File btnlabel_recap_run.ps1 [-Tag btnlabel-recap]
#
# ASCII only (PS 5.1 parses a BOM-less non-ASCII .ps1 as ANSI).
# =============================================================================
param(
    [string]$Tag = 'btnlabel-recap',
    [string]$StopAfter = '',
    [string]$Why = 'btn-label-fix (ruling 1): the shared button-label colour constant changed, so every row whose evidence picture contains text drawn with UiArt.ButtonText is an affected row (boot hint / main-menu / char-select / char-create labels, inventory cell counts, NPC dialog options, shop slots); the numeric side is already re-judged offline and this ONE session re-captures the presentation side of just those rows'
)

$ErrorActionPreference = 'Continue'

$root    = 'c:/Work/Server/f-v2/clover-project-diablo2'
$proj    = Join-Path $root 'client'
$cs      = Join-Path $root 'tools\probes\drivers\x_drive.cs'
$test    = Join-Path $root '.ai-tmp\test'
$shots   = Join-Path $root '.ai-tmp\screenshots'
$raw     = Join-Path $shots 'recap_btnlabel'
$done    = Join-Path $test ('btnlabel_recap_done_' + $Tag + '.txt')
$trace   = Join-Path $test ('btnlabel_recap_steps_' + $Tag + '.txt')
$frozen  = Join-Path $shots 'btnlabel_recap_evidence.txt'
$logPath = Join-Path $proj 'Logs\Editor.log'
$playLog = Join-Path $test 'play-log.tsv'
$lock    = Join-Path $test 'play.lock'
$runbg   = Join-Path $root 'tools\probes\interact\p_runbg.cs'
$me      = 'btn-label-fix'

# only the tiles of the rows this slice re-captures (mapped to their delivered names)
$recap = [ordered]@{
    'x_b1_boot.png'             = 'btnlabel_recap_b1_boot_hint.png'
    'x_b2_mainmenu.png'         = 'btnlabel_recap_b2_mainmenu.png'
    'x_b3_charselect.png'       = 'btnlabel_recap_b3_charselect.png'
    'x_b4_charcreate.png'       = 'btnlabel_recap_b4_charcreate.png'
    'x_f1_inventory.png'        = 'btnlabel_recap_f1_inventory.png'
    'x_g1_dialog_notstarted.png'= 'btnlabel_recap_g1_dialog_notstarted.png'
    'x_g1_dialog_inprogress.png'= 'btnlabel_recap_g1_dialog_options.png'
    'x_g2_shop_repair.png'      = 'btnlabel_recap_g2_shop.png'
    'x_h2_pause.png'            = 'btnlabel_recap_h2_pause.png'
    'x_h2_options_after.png'    = 'btnlabel_recap_h2_options.png'
}

$keepRe = '\[X\]'

$script:offset = 0
$script:lines = New-Object System.Collections.ArrayList
$script:cli = 0

function Say([string]$s) {
    $stamp = (Get-Date).ToString('HH:mm:ss.fff')
    Write-Host ($stamp + ' ' + $s)
    Add-Content -Path $trace -Value ($stamp + ' ' + $s) -Encoding UTF8
}
function Init-Offset() {
    if (Test-Path $logPath) { $script:offset = (Get-Item $logPath).Length } else { $script:offset = 0 }
}
function Read-New() {
    if (-not (Test-Path $logPath)) { return }
    $fs = $null
    try {
        $fs = New-Object System.IO.FileStream($logPath, [System.IO.FileMode]::Open,
              [System.IO.FileAccess]::Read, [System.IO.FileShare]::ReadWrite)
        if ($fs.Length -le $script:offset) { return }
        $fs.Seek($script:offset, [System.IO.SeekOrigin]::Begin) | Out-Null
        $len = [int]($fs.Length - $script:offset)
        $buf = New-Object byte[] $len
        $read = $fs.Read($buf, 0, $len)
        $script:offset = $script:offset + $read
        $txt = [System.Text.Encoding]::UTF8.GetString($buf, 0, $read)
        $keep = @($txt -split "`r?`n" | Where-Object { $_ -match $keepRe })
        foreach ($l in $keep) { [void]$script:lines.Add($l) }
    } catch {
        Say ('WARN log-read ' + $_.Exception.Message)
    } finally {
        if ($fs -ne $null) { $fs.Close(); $fs.Dispose() }
    }
}
function Lines-With([string]$pattern) { return @($script:lines | Where-Object { $_ -match $pattern }) }
function Release-Lock() {
    if (-not (Test-Path $lock)) { Say 'LOCK-RELEASE skip (no lock file)'; return }
    $body = ''
    try { $body = (Get-Content $lock -Raw) } catch { $body = '' }
    if ($body -match '^\s*btn-label-fix\b') {
        Remove-Item $lock -Force -ErrorAction SilentlyContinue
        Say 'LOCK-RELEASED (mine)'
    } else {
        Say ('LOCK-RELEASE REFUSED (owned by ' + $body.Trim() + ') -> left alone')
    }
}
function Clip([string]$s, [int]$n) {
    if ($null -eq $s) { return '' }
    $one = $s -replace "`r?`n", ' '
    if ($one.Length -le $n) { return $one }
    return $one.Substring(0, $n)
}
function Unity-Cmd([string[]]$argv, [switch]$Quiet) {
    $script:cli = $script:cli + 1
    $raw2 = & unity command @argv --project-path $proj --format json --no-pager --no-banner 2>&1 | Out-String
    if (-not $Quiet) { Say ('CLI ' + ($argv -join ' ') + ' -> ' + $raw2.Length + ' bytes') }
    return $raw2
}
function Run-Step([string]$entry, [string]$arg) {
    if ([string]::IsNullOrEmpty($arg)) {
        $raw2 = Unity-Cmd @('run_script', '--file', $cs, '--entry', $entry)
    } else {
        $raw2 = Unity-Cmd @('run_script', '--file', $cs, '--entry', $entry, '--args', ('[\"' + $arg + '\"]'))
    }
    $result = '?'
    try {
        $j = $raw2 | ConvertFrom-Json
        $r = $j.data.result.result
        if ($null -eq $r) { $r = $j.data.result.error }
        if ($null -eq $r) {
            $d = @($j.data.result.diagnostics | Where-Object { $_.severity -eq 'error' })
            if ($d.Count -gt 0) { $r = 'COMPILE-ERRORS:' + $d.Count + ' ' + (Clip (($d | ForEach-Object { $_.message }) -join ' | ') 400) }
        }
        $result = [string]$r
    } catch { $result = 'PARSE-FAIL' }
    Say ('STEP ' + $entry + ' result=' + $result)
    return $result
}

# =============================================================================
# main
# =============================================================================
foreach ($d in @($shots, $test, $raw)) { if (-not (Test-Path $d)) { New-Item -ItemType Directory -Path $d | Out-Null } }
Set-Content -Path $trace -Value ('# btnlabel recap steps tag=' + $Tag) -Encoding UTF8
$sessionStart = Get-Date
Set-Location -LiteralPath $proj
$stopLabel = '(none)'
if ($StopAfter -ne '') { $stopLabel = $StopAfter }
Say ('BEGIN tag=' + $Tag + ' stopAfter=' + $stopLabel + ' cwd=' + (Get-Location).Path)

$idle = $false
for ($i = 0; $i -lt 30; $i++) {
    $st = Unity-Cmd @('editor_status') -Quiet
    $compiling = $true; $play = '?'
    try {
        $sj = $st | ConvertFrom-Json
        $inner = $sj.data.result
        if ($inner -is [string]) { $inner = $inner | ConvertFrom-Json }
        $compiling = [bool]$inner.compiling
        $play = [string]$inner.playMode
    } catch { }
    if ((-not $compiling) -and ($play -eq 'stopped')) { $idle = $true; Say ('EDITOR-IDLE compiling=false playMode=stopped (try ' + ($i + 1) + ')'); break }
    Say ('EDITOR-BUSY compiling=' + $compiling + ' playMode=' + $play + ' -> Start-Sleep 10')
    Start-Sleep -Seconds 10
}
if (-not $idle) { Say 'ABORT editor never went idle -> NOT taking the lock'; exit 1 }

for ($i = 0; $i -lt 20; $i++) {
    if (-not (Test-Path $lock)) { break }
    $age = ((Get-Date) - (Get-Item $lock).LastWriteTime).TotalMinutes
    if ($age -ge 6) { Say ('LOCK-STALE age=' + [math]::Round($age, 1) + 'm -> taking it'); break }
    Say ('LOCK-BUSY age=' + [math]::Round($age, 1) + 'm -> Start-Sleep 30 (try ' + ($i + 1) + ')')
    Start-Sleep -Seconds 30
}
Set-Content -Path $lock -Value ($me + ' ' + (Get-Date).ToString('o')) -Encoding UTF8
Say ('LOCK-TAKEN ' + $lock)

Add-Content -Path $playLog -Value ((Get-Date).ToString('yyyy-MM-dd HH:mm') + "`t" + $me + "`t" + $Tag + "`t" + $Why) -Encoding UTF8
Say ('PLAYLOG-APPENDED ' + $playLog)

Unity-Cmd @('editor_focus') -Quiet | Out-Null
Unity-Cmd @('editor_stop') -Quiet | Out-Null
Start-Sleep -Seconds 2

Unity-Cmd @('recompile') -Quiet | Out-Null
$recompile = '(unknown)'; $failed = '?'
for ($i = 0; $i -lt 60; $i++) {
    Start-Sleep -Seconds 2
    $st = Unity-Cmd @('recompile_status') -Quiet
    try {
        $j = $st | ConvertFrom-Json
        $inner = $j.data.result
        if ($inner -is [string]) { $inner = $inner | ConvertFrom-Json }
        $recompile = [string]$inner.status
        $failed = [string]$inner.failed
    } catch { }
    if ($recompile -match 'up_to_date|completed|idle') { break }
}
Say ('RECOMPILE status=' + $recompile + ' failed=' + $failed)
if (($recompile -notmatch 'up_to_date|completed|idle') -or ($failed -eq 'True')) {
    Say 'ABORT recompile not clean -> releasing MY lock and stopping'
    Release-Lock
    exit 1
}
Start-Sleep -Seconds 3

Unity-Cmd @('clear_console') -Quiet | Out-Null
Init-Offset
Say ('LOG-OFFSET ' + $script:offset)
Unity-Cmd @('editor_focus') -Quiet | Out-Null
Unity-Cmd @('editor_play') -Quiet | Out-Null
Start-Sleep -Seconds 4
if (Test-Path $runbg) {
    $bg = Unity-Cmd @('eval_file', '--file', $runbg) -Quiet
    Say ('RUNBG ' + (Clip $bg 200))
}

$cfgOk = $false
for ($i = 0; $i -lt 10; $i++) {
    $r = Run-Step 'X.Api2.Cfg' ''
    if ($r -match 'gameRunning=1') { $cfgOk = $true; break }
    Start-Sleep -Seconds 3
}
Say ('CFG-OK ' + $cfgOk)
if (-not $cfgOk) {
    Say 'ABORT X.Api2.Cfg never answered -> editor_stop + release MY lock'
    Unity-Cmd @('editor_stop') -Quiet | Out-Null
    Release-Lock
    exit 1
}
Run-Step 'X.Api2.Paths' (($raw -replace '\\', '/') + '|' + ($done -replace '\\', '/')) | Out-Null

$spec = 'tour|' + $Tag + '|' + ($raw -replace '\\', '/') + '|' + ($done -replace '\\', '/') + '|' + $StopAfter
$r2 = Unity-Cmd @('run_script', '--file', $cs, '--entry', 'X.Tour.Install', '--args', ('[\"' + $spec + '\"]'))
Say ('INSTALL ' + (Clip $r2 300))

$t0 = Get-Date
while (((Get-Date) - $t0).TotalSeconds -lt 900) {
    Read-New
    if (Test-Path $done) { break }
    Start-Sleep -Milliseconds 400
}
Read-New
Start-Sleep -Seconds 3
Read-New
if (Test-Path $done) { Say ('DONE ' + (Get-Content $done -Raw).Trim()) }
else { Say 'DONE-MISSING (the plan never signalled completion within 900s)' }

$consoleJson = Unity-Cmd @('console_status') -Quiet
$consoleErrors = -1
try { $cj = $consoleJson | ConvertFrom-Json; $consoleErrors = $cj.data.result.counts.error } catch { }
Say ('CONSOLE-STATUS errors=' + $consoleErrors)

Unity-Cmd @('editor_stop') -Quiet | Out-Null
Say 'STOPPED'
Release-Lock

$copied = 0
foreach ($k in $recap.Keys) {
    $src = Join-Path $raw $k
    if (Test-Path $src) {
        Copy-Item $src (Join-Path $shots $recap[$k]) -Force
        $copied++
        Say ('RECAP-COPIED ' + $k + ' -> ' + $recap[$k])
    } else {
        Say ('RECAP-MISSING ' + $k)
    }
}
$tiles = @(Get-ChildItem $raw -Filter *.png -ErrorAction SilentlyContinue)
Say ('RAW-TILES ' + $tiles.Count + ' in ' + $raw)

$sessionEnd = Get-Date
$hdr = New-Object System.Collections.ArrayList
[void]$hdr.Add('# btn-label-fix re-capture (ruling 1) -- fresh full-frame tiles for the rows affected by the shared button-label colour change')
[void]$hdr.Add('# session: ' + $sessionStart.ToString('yyyy-MM-dd HH:mm:ss') + '..' + $sessionEnd.ToString('HH:mm:ss') + '   driver: tools/probes/drivers/x_drive.cs')
[void]$hdr.Add('# run: powershell -NoProfile -ExecutionPolicy Bypass -File tools/probes/drivers/btnlabel_recap_run.ps1 -Tag ' + $Tag)
[void]$hdr.Add('#   recompile = ' + $recompile + ' failed=' + $failed)
[void]$hdr.Add('#   console   = console_status counts.error=' + $consoleErrors)
[void]$hdr.Add('#   raw tiles = ' + $tiles.Count + ' in ' + $raw + ' ; recap copied = ' + $copied)
[void]$hdr.Add('# ---------------------------------------------------------------------------')
foreach ($l in @(Lines-With '\[X\]')) { [void]$hdr.Add($l) }
[System.IO.File]::WriteAllLines($frozen, [string[]]$hdr, (New-Object System.Text.UTF8Encoding($false)))
Say ('FROZEN ' + $frozen + ' lines=' + $hdr.Count)
Say ('SUMMARY tag=' + $Tag + ' cfgOk=' + $cfgOk + ' done=' + (Test-Path $done) + ' recompile=' + $recompile +
     ' failed=' + $failed + ' consoleErrors=' + $consoleErrors + ' rawTiles=' + $tiles.Count + ' recapCopied=' + $copied +
     ' x=' + (@(Lines-With '\[X\]').Count))
Say ('END cli=' + $script:cli)
