# =============================================================================
# pv_audio_run.ps1 -- play-verify group 4: monster SFX, ONE Play session.
#
#   powershell -NoProfile -ExecutionPolicy Bypass -File pv_audio_run.ps1
#
# Chain: take the team play lock -> wait compiling:false -> clear_console -> editor_play
#        -> S2.Api.Cfg (from s2_drive.cs -- NOT rewritten, reused)
#        -> S2.Tour.Install (s2_drive.cs reaches Stage and travels to Blood Moor)
#        -> wait for its VERDICT (=> we are standing in Blood Moor, monsters spawned)
#        -> PVA.Api.Install (pv_audio_probe.cs) inside the SAME Play session
#        -> three observation windows: HIT / AGGRO(chase+attack) / KILL
#        -> PVA.Api.Finish -> dump [PVA] lines -> editor_stop -> release MY lock
#        -> publish raw tiles as .ai-tmp/screenshots/pv_audio_*.png
#
# Nothing here re-implements the boot/menu/stage chain: that is s2_drive.cs's job.
# Nothing here touches product code: the probe only calls the PUBLIC contract
# IMonsterModule.ApplyDamage / NotifyAttacked and reads AudioSource transitions.
#
# ASCII only (PS 5.1 parses a BOM-less non-ASCII .ps1 as ANSI).
# =============================================================================
param(
    [string]$Why = 'play-verify group 4: the per-monster SFX wiring is green offline (audiocheck 98 / animcheck 87) but has never been heard ONCE in a live session; whether the real monster_hit_*/atk_*/die_*/step_* clips actually come off the pool can only be judged inside Play'
)

$ErrorActionPreference = 'Continue'

$root   = 'c:/Work/Server/f-v2/clover-project-diablo2'
$proj   = Join-Path $root 'client'
$test   = Join-Path $root '.ai-tmp/test'
$shots  = Join-Path $root '.ai-tmp/screenshots'
$raw    = Join-Path $test 'raw_pvaudio'
$s2cs   = Join-Path $root 'tools\probes\drivers\s2_drive.cs'
$pvcs   = Join-Path $root 'tools\probes\drivers\pv_audio_probe.cs'
$done   = Join-Path $test 'pv_audio_done.txt'
$trace  = Join-Path $test 'pv_audio_steps.txt'
$doneS2 = Join-Path $test 'pv_audio_s2_done.txt'
$frozen = Join-Path $shots 'pv1_audio_evidence.txt'
$logPath = Join-Path $proj 'Logs\Editor.log'
$playLog = Join-Path $test 'play-log.tsv'
$lock   = Join-Path $test 'play.lock'

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
        $keep = @($txt -split "`r?`n" | Where-Object { $_ -match '\[PVA\]|\[S2\]|\[Audio\]|pool full|SFX pool|AudioPool' })
        foreach ($l in $keep) { [void]$script:lines.Add($l) }
    } catch { Say ('WARN log-read ' + $_.Exception.Message) }
    finally { if ($fs -ne $null) { $fs.Close(); $fs.Dispose() } }
}

function Lines-With([string]$pattern) { return @($script:lines | Where-Object { $_ -match $pattern }) }
function Clip([string]$s, [int]$n) {
    if ($null -eq $s) { return '' }
    $one = $s -replace "`r?`n", ' '
    if ($one.Length -le $n) { return $one }
    return $one.Substring(0, $n)
}

function Unity-Cmd([string[]]$argv, [switch]$Quiet) {
    $script:cli = $script:cli + 1
    $r = & unity command @argv --format json --no-pager 2>&1 | Out-String
    if (-not $Quiet) { Say ('CLI ' + ($argv -join ' ') + ' -> ' + $r.Length + ' bytes') }
    return $r
}

function Run-Step([string]$file, [string]$entry, [string]$arg) {
    if ([string]::IsNullOrEmpty($arg)) {
        $r = Unity-Cmd @('run_script', '--file', $file, '--entry', $entry) -Quiet
    } else {
        $r = Unity-Cmd @('run_script', '--file', $file, '--entry', $entry, '--args', ('[\"' + $arg + '\"]')) -Quiet
    }
    $result = '?'
    try {
        $j = $r | ConvertFrom-Json
        $v = $j.data.result.result
        if ($null -eq $v) { $v = $j.data.result.error }
        if ($null -eq $v) {
            $d = @($j.data.result.diagnostics | Where-Object { $_.severity -eq 'error' })
            if ($d.Count -gt 0) { $v = 'COMPILE-ERRORS:' + $d.Count + ' ' + (Clip (($d | ForEach-Object { $_.id + ' line ' + $_.line + ': ' + $_.message }) -join ' | ') 500) }
        }
        $result = [string]$v
    } catch { $result = 'PARSE-FAIL' }
    Say ('STEP ' + $entry + ' arg=' + $arg + ' result=' + $result)
    return $result
}

foreach ($d in @($test, $shots, $raw)) { if (-not (Test-Path $d)) { New-Item -ItemType Directory -Path $d | Out-Null } }
Set-Content -Path $trace -Value '# pv audio steps' -Encoding UTF8
Set-Location -LiteralPath $proj
$sessionStart = Get-Date
Say ('BEGIN cwd=' + (Get-Location).Path)

# ---- play lock -------------------------------------------------------------
for ($i = 0; $i -lt 20; $i++) {
    if (-not (Test-Path $lock)) { break }
    $age = ((Get-Date) - (Get-Item $lock).LastWriteTime).TotalMinutes
    if ($age -ge 6) { Say ('LOCK-STALE age=' + [math]::Round($age, 1) + 'm -> taking it'); break }
    Say ('LOCK-BUSY age=' + [math]::Round($age, 1) + 'm -> Start-Sleep 30 (try ' + ($i + 1) + ')')
    Start-Sleep -Seconds 30
}
Set-Content -Path $lock -Value ('play-verify ' + (Get-Date).ToString('o')) -Encoding UTF8
Say 'LOCK-TAKEN (play-verify)'

Add-Content -Path $playLog -Value ((Get-Date).ToString('yyyy-MM-dd HH:mm') + "`tplay-verify`tpv4-monster-audio`t" + $Why) -Encoding UTF8
Say 'PLAYLOG-APPENDED'

# ---- editor idle -----------------------------------------------------------
$idle = $false
for ($i = 0; $i -lt 40; $i++) {
    $st = Unity-Cmd @('editor_status') -Quiet
    $compiling = '?'
    try { $j = $st | ConvertFrom-Json; $compiling = [string]$j.data.result.compiling } catch { }
    if ($compiling -eq 'False' -or $compiling -eq 'false') { $idle = $true; Say ('EDITOR-IDLE try=' + $i); break }
    Say ('EDITOR-COMPILING -> sleep 5 (' + $i + ')')
    Start-Sleep -Seconds 5
}
Say ('EDITOR-IDLE ' + $idle)

foreach ($f in @($done, $doneS2)) { if (Test-Path $f) { Remove-Item $f -Force; Say ('CLEARED ' + (Split-Path $f -Leaf)) } }

Unity-Cmd @('editor_focus') -Quiet | Out-Null
Unity-Cmd @('editor_stop')  -Quiet | Out-Null
Start-Sleep -Seconds 2
Unity-Cmd @('recompile') -Quiet | Out-Null
$recompile = '(unknown)'
for ($i = 0; $i -lt 40; $i++) {
    Start-Sleep -Seconds 2
    $rj = Unity-Cmd @('recompile_status') -Quiet
    try { $jj = $rj | ConvertFrom-Json; $recompile = (($jj.data.result | ConvertFrom-Json).status) } catch { }
    if ($recompile -match 'up_to_date|completed|idle') { break }
}
Say ('RECOMPILE status=' + $recompile)
if ($recompile -notmatch 'up_to_date|completed|idle') {
    Say 'ABORT recompile never settled -> release MY lock and stop'
    Remove-Item $lock -Force -ErrorAction SilentlyContinue
    exit 1
}

Unity-Cmd @('clear_console') -Quiet | Out-Null
Init-Offset
Unity-Cmd @('editor_play') -Quiet | Out-Null
Start-Sleep -Seconds 8

# ---- reuse s2_drive.cs to reach Stage + Blood Moor --------------------------
$cfgOk = $false
for ($i = 0; $i -lt 10; $i++) {
    $r = Run-Step $s2cs 'S2.Api.Cfg' ''
    if ($r -match 'CFG-OK') { $cfgOk = $true; break }
    Start-Sleep -Seconds 3
}
Say ('CFG-OK ' + $cfgOk)
if (-not $cfgOk) { Say 'ABORT no Cfg -> stop + release MY lock'; Unity-Cmd @('editor_stop') -Quiet | Out-Null; Remove-Item $lock -Force -ErrorAction SilentlyContinue; exit 1 }

$spec = 'pvaudio|g66|' + (($doneS2 -replace '\\', '/')) + '|' + (($raw -replace '\\', '/'))
Run-Step $s2cs 'S2.Tour.Install' $spec | Out-Null

$t0 = Get-Date
while (((Get-Date) - $t0).TotalSeconds -lt 240) {
    Read-New
    if ((Lines-With '\[S2\] VERDICT').Count -gt 0) { break }
    Start-Sleep -Milliseconds 500
}
Read-New
Say ('S2-CHAIN verdictLines=' + (Lines-With '\[S2\] VERDICT').Count)

# ---- install OUR probe in the same Play session -----------------------------
$spec2 = ($done -replace '\\', '/') + '|' + ($raw -replace '\\', '/')
$inst = Run-Step $pvcs 'PVA.Api.Install' $spec2
Say ('PROBE-INSTALL ' + $inst)
if ($inst -match 'ERR|PARSE-FAIL|COMPILE-ERRORS') {
    Say 'ABORT the probe did not install -> stop + release MY lock'
    Unity-Cmd @('editor_stop') -Quiet | Out-Null
    Remove-Item $lock -Force -ErrorAction SilentlyContinue
    exit 1
}

Start-Sleep -Seconds 2
$snap = Run-Step $pvcs 'PVA.Api.Snap' ''
Say ('SNAP ' + $snap)

# pull the live monster ids out of the SNAP log line: monsters=[ID:kindId:name:...]
$ids = @()
$snapLine = @(Lines-With '\[PVA\] SNAP') | Select-Object -Last 1
if ($snapLine) {
    foreach ($mm in ([regex]::Matches($snapLine, '(\d+):\d+:[^:,\[\]]+:\d+/\d+'))) { $ids += $mm.Groups[1].Value }
}
$ids = $ids | Select-Object -Unique
Say ('MONSTER-IDS ' + ($ids -join ',') + ' count=' + $ids.Count)
if ($ids.Count -eq 0) { Say 'ABORT no live monsters in view -> finish with what we have' }
$allIds = ($ids -join '|')

# ---- phase 1: HIT (expect the generic impact sound + the monster own gethit sound) ----
if ($ids.Count -gt 0) {
    Run-Step $pvcs 'PVA.Api.Mark' 'hit' | Out-Null
    Run-Step $pvcs 'PVA.Api.Hit' ($ids[0] + '|3') | Out-Null
    Start-Sleep -Seconds 2
    Run-Step $pvcs 'PVA.Api.Since' '' | Out-Null
    Run-Step $pvcs 'PVA.Api.Shot' 'pv_audio_1_hit.png' | Out-Null
    Start-Sleep -Seconds 2

    # ---- phase 2: AGGRO -> the monsters chase (step sound) and swing (attack sound) ----
    Run-Step $pvcs 'PVA.Api.Mark' 'chase' | Out-Null
    Run-Step $pvcs 'PVA.Api.Aggro' $allIds | Out-Null
    Start-Sleep -Seconds 6
    Run-Step $pvcs 'PVA.Api.Since' '' | Out-Null
    Run-Step $pvcs 'PVA.Api.Shot' 'pv_audio_2_chase.png' | Out-Null
    Start-Sleep -Seconds 2

    # ---- phase 3: KILL (expect the per-class death sound) ----
    Run-Step $pvcs 'PVA.Api.Mark' 'kill' | Out-Null
    Run-Step $pvcs 'PVA.Api.Hit' ($ids[0] + '|99999') | Out-Null
    Start-Sleep -Seconds 3
    Run-Step $pvcs 'PVA.Api.Since' '' | Out-Null
    Run-Step $pvcs 'PVA.Api.Shot' 'pv_audio_3_kill.png' | Out-Null
    Start-Sleep -Seconds 2
    Run-Step $pvcs 'PVA.Api.Snap' '' | Out-Null
}

Run-Step $pvcs 'PVA.Api.Finish' 'done' | Out-Null
$t1 = Get-Date
while (((Get-Date) - $t1).TotalSeconds -lt 25) {
    Read-New
    if (Test-Path $done) { break }
    Start-Sleep -Milliseconds 500
}
Read-New
Say ('DONE ' + (Test-Path $done))

foreach ($l in @(Lines-With '\[PVA\]')) { Say ('EVIDENCE ' + $l) }

$consoleJson = Unity-Cmd @('console_status') -Quiet
$consoleErrors = -1
try { $cj = $consoleJson | ConvertFrom-Json; $consoleErrors = $cj.data.result.counts.error } catch { }
Say ('CONSOLE-STATUS errors=' + $consoleErrors)

Unity-Cmd @('editor_stop') -Quiet | Out-Null
Say 'STOPPED'
if (Test-Path $lock) {
    $body = ''
    try { $body = (Get-Content $lock -Raw) } catch { $body = '' }
    if ($body -match '^\s*(play-verify|S2)\b') { Remove-Item $lock -Force -ErrorAction SilentlyContinue; Say 'LOCK-RELEASED (mine)' }
    else { Say ('LOCK-RELEASE REFUSED (owned by ' + $body.Trim() + ')') }
}

# ---- publish the tiles ------------------------------------------------------
if (Test-Path $raw) {
    foreach ($f in Get-ChildItem $raw -Filter '*.png') {
        $nm = 'pv_audio_' + $f.Name
        Copy-Item $f.FullName (Join-Path $shots $nm) -Force
        Say ('TILE-COPIED ' + $f.Name + ' -> ' + $nm + ' bytes=' + $f.Length)
    }
}

# ---- freeze ----------------------------------------------------------------
$hdr = New-Object System.Collections.ArrayList
[void]$hdr.Add('# pv group 4 (monster SFX) -- fresh runtime log, frozen from client/Logs/Editor.log')
[void]$hdr.Add('# session: ' + $sessionStart.ToString('yyyy-MM-dd HH:mm:ss') + '..' + (Get-Date).ToString('HH:mm:ss'))
[void]$hdr.Add('# run: powershell -NoProfile -ExecutionPolicy Bypass -File tools/probes/drivers/pv_audio_run.ps1')
[void]$hdr.Add('# chain: s2_drive.cs S2.Api.Cfg + S2.Tour.Install (reused, not rewritten) -> pv_audio_probe.cs (PVA.Api.*)')
[void]$hdr.Add('# env: recompile=' + $recompile + ' console_errors=' + $consoleErrors + ' device=AMD Radeon RX 5700 XT')
[void]$hdr.Add('# ---------------------------------------------------------------------------')
foreach ($l in @(Lines-With '\[PVA\]|\[S2\] DEV|\[S2\] VERD|\[Audio\]|pool|AudioPool')) { [void]$hdr.Add($l) }
[void]$hdr.Add('# ---------------------------------------------------------------------------')
[System.IO.File]::WriteAllLines($frozen, [string[]]$hdr, (New-Object System.Text.UTF8Encoding($false)))
Say ('FROZEN ' + $frozen + ' lines=' + $hdr.Count)
Say ('SUMMARY ids=' + ($ids -join ',') + ' plays=' + (Lines-With '\[PVA\] SFX-START').Count + ' cli=' + $script:cli)
Say 'END'
