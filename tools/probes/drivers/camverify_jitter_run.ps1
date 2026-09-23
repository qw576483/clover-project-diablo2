# =============================================================================
# camverify_jitter_run.ps1 -- AFTER capture of the camera-follow trace, DUAL cadence.
#
#   powershell -NoProfile -ExecutionPolicy Bypass -File camverify_jitter_run.ps1
#
# WHY: camjitter_run.ps1 does NOT take the team's play.lock, so this wrapper owns
# the lock around BOTH of its Play sessions:
#   (1) tag cvpin  -> CamJit.Api.Cfg            (vSync=0 / targetFps=60: the same
#                    measuring stick as the "before" baseline in report-playverify.md)
#   (2) tag cvkeep -> CamJit.Api.CfgKeepCadence (the engine's own FramePacing left
#                    untouched => the cadence a player actually gets)
# Both are reported side by side so that an improvement produced only by switching
# the measurement cadence cannot be mistaken for a fix.
#
# The TSVs are judged OFFLINE by tools/probes/drivers/camjitter_who.py (unmodified)
# plus the px conversion in tools/probes/drivers/camverify_lag.py.
# ASCII only (PS 5.1 parses a BOM-less non-ASCII .ps1 as ANSI).
# =============================================================================
$ErrorActionPreference = 'Continue'

$root   = 'c:/Work/Server/f-v2/clover-project-diablo2'
$proj   = $root + '/client'
$test   = $root + '/.ai-tmp/test'
$lock   = $test + '/play.lock'
$camjit = $root + '/tools/probes/drivers/camjitter_run.ps1'
$trace  = $test + '/cv_jitter_steps.txt'
$heart  = $test + '/heartbeat-camverify.txt'

function Say([string]$s) {
    $stamp = (Get-Date).ToString('HH:mm:ss.fff')
    Write-Host ($stamp + ' ' + $s)
    Add-Content -Path $trace -Value ($stamp + ' ' + $s) -Encoding UTF8
    try { [System.IO.File]::WriteAllText($heart, ($stamp + ' [cam-verify] ' + $s)) } catch { }
}

Set-Content -Path $trace -Value '# cam-verify jitter dual cadence' -Encoding UTF8
Set-Location -LiteralPath $proj
Say ('BEGIN cwd=' + (Get-Location).Path)

for ($i = 0; $i -lt 15; $i++) {
    if (-not (Test-Path $lock)) { break }
    $age = ((Get-Date) - (Get-Item $lock).LastWriteTime).TotalMinutes
    if ($age -ge 6) { Say ('LOCK-STALE age=' + [math]::Round($age, 1) + 'm -> taking it'); break }
    Say ('LOCK-BUSY age=' + [math]::Round($age, 1) + 'm -> wait 30s (try ' + ($i + 1) + ')')
    Start-Sleep -Seconds 30
}
Set-Content -Path $lock -Value ('cam-verify ' + (Get-Date).ToString('o')) -Encoding UTF8
Say 'LOCK-TAKEN (cam-verify)'

try {
    Say 'RUN-1 cvpin (CamJit.Api.Cfg: vSync=0 targetFps=60)'
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $camjit -Tag 'cvpin' 2>&1 |
        ForEach-Object { Add-Content -Path ($test + '/cv_jitter_pin_out.txt') -Value $_ -Encoding UTF8 }
    Say 'RUN-1-END cvpin'

    Start-Sleep -Seconds 5

    Say 'RUN-2 cvkeep (CamJit.Api.CfgKeepCadence)'
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $camjit -Tag 'cvkeep' -KeepCadence 2>&1 |
        ForEach-Object { Add-Content -Path ($test + '/cv_jitter_keep_out.txt') -Value $_ -Encoding UTF8 }
    Say 'RUN-2-END cvkeep'
} finally {
    try { & unity command editor_stop --format json --no-pager 2>&1 | Out-Null } catch { }
    if (Test-Path $lock) {
        $body = ''
        try { $body = (Get-Content $lock -Raw) } catch { $body = '' }
        if ($body -match '^\s*cam-verify\b') { Remove-Item $lock -Force -ErrorAction SilentlyContinue; Say 'LOCK-RELEASED (mine)' }
        else { Say ('LOCK-RELEASE REFUSED (owned by ' + $body.Trim() + ')') }
    }
    Say 'END'
}
