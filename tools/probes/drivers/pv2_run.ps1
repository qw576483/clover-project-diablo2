# =============================================================================
# pv2_run.ps1 -- the play-verify camera-jitter AFTER capture, DUAL CADENCE.
#
#   powershell -NoProfile -ExecutionPolicy Bypass -File pv2_run.ps1
#
# WHY: camjitter_run.ps1 does NOT take the team's play.lock (it was written before
# the lock rule), so this wrapper owns the lock around BOTH of its Play sessions:
#   (1) tag pvpin  -> CamJit.Api.Cfg          (vSync=0 / targetFps=60, the pinned
#                    cadence the "-before" baseline used => apples to apples)
#   (2) tag pvkeep -> CamJit.Api.CfgKeepCadence (the engine's own FramePacingPolicy
#                    left untouched => the cadence the player actually gets)
# The two runs are deliberately kept side by side so that an improvement caused by
# merely switching the measurement cadence cannot be mistaken for a real fix.
#
# ASCII only (PS 5.1 parses a BOM-less non-ASCII .ps1 as ANSI).
# =============================================================================
$ErrorActionPreference = 'Continue'

$root   = 'c:/Work/Server/f-v2/clover-project-diablo2'
$proj   = Join-Path $root 'client'
$test   = Join-Path $root '.ai-tmp/test'
$lock   = Join-Path $test 'play.lock'
$camjit = Join-Path $root 'tools/probes/drivers/camjitter_run.ps1'
$trace  = Join-Path $test 'pv2_steps.txt'

function Say([string]$s) {
    $stamp = (Get-Date).ToString('HH:mm:ss.fff')
    Write-Host ($stamp + ' ' + $s)
    Add-Content -Path $trace -Value ($stamp + ' ' + $s) -Encoding UTF8
}

Set-Content -Path $trace -Value '# pv2 (play-verify jitter dual cadence)' -Encoding UTF8
Set-Location -LiteralPath $proj
Say ('BEGIN cwd=' + (Get-Location).Path)

# ---- wait for a free / stale lock, then take it (we are the play-verify piece) ----
for ($i = 0; $i -lt 20; $i++) {
    if (-not (Test-Path $lock)) { break }
    $age = ((Get-Date) - (Get-Item $lock).LastWriteTime).TotalMinutes
    if ($age -ge 6) { Say ('LOCK-STALE age=' + [math]::Round($age, 1) + 'm -> taking it'); break }
    Say ('LOCK-BUSY age=' + [math]::Round($age, 1) + 'm -> Start-Sleep 30 (try ' + ($i + 1) + ')')
    Start-Sleep -Seconds 30
}
Set-Content -Path $lock -Value ('play-verify ' + (Get-Date).ToString('o')) -Encoding UTF8
Say 'LOCK-TAKEN (play-verify)'

# ---- wait for the editor to be idle (compiling:false) before entering Play ----
$idle = $false
for ($i = 0; $i -lt 40; $i++) {
    $st = & unity command editor_status --format json --no-pager 2>&1 | Out-String
    $compiling = '?'
    try { $j = $st | ConvertFrom-Json; $compiling = [string]$j.data.result.compiling } catch { }
    if ($compiling -eq 'False' -or $compiling -eq 'false') { $idle = $true; Say ('EDITOR-IDLE try=' + $i); break }
    Say ('EDITOR-COMPILING -> sleep 5 (try ' + $i + ')')
    Start-Sleep -Seconds 5
}
Say ('EDITOR-IDLE ' + $idle)

# ---- session 1: pinned cadence (same measuring stick as the -before baseline) ----
Say 'RUN-1 pvpin (CamJit.Api.Cfg: vSync=0 targetFps=60)'
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File $camjit -Tag 'pvpin' 2>&1 |
    ForEach-Object { Add-Content -Path (Join-Path $test 'pv2_pin_out.txt') -Value $_ -Encoding UTF8 }
Say 'RUN-1-END pvpin'

Start-Sleep -Seconds 5

# ---- session 2: real shipped cadence (FramePacingPolicy left untouched) ----
Say 'RUN-2 pvkeep (CamJit.Api.CfgKeepCadence)'
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File $camjit -Tag 'pvkeep' -KeepCadence 2>&1 |
    ForEach-Object { Add-Content -Path (Join-Path $test 'pv2_keep_out.txt') -Value $_ -Encoding UTF8 }
Say 'RUN-2-END pvkeep'

# ---- stop + release our own lock ----
$st2 = & unity command editor_stop --format json --no-pager 2>&1 | Out-String
Say ('EDITOR-STOP ' + ($st2 -replace "`r?`n", ' ').Substring(0, [Math]::Min(160, ($st2 -replace "`r?`n", ' ').Length)))
if (Test-Path $lock) {
    $body = ''
    try { $body = (Get-Content $lock -Raw) } catch { $body = '' }
    if ($body -match '^\s*(play-verify|S2)\b') { Remove-Item $lock -Force -ErrorAction SilentlyContinue; Say 'LOCK-RELEASED (mine)' }
    else { Say ('LOCK-RELEASE REFUSED (owned by ' + $body.Trim() + ')') }
}
Say 'END'
