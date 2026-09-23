# =============================================================================
# q_save_run.ps1 -- ONE Play session for slice Q (R7: silent save-load failure).
#
#   powershell -NoProfile -ExecutionPolicy Bypass -File q_save_run.ps1 [-Why "..."]
#
# Fixed order (SKILL 1.13 / verify-template items 16-18):
#   backup saves -> play-log line -> editor_stop -> recompile settle -> clear_console
#   -> editor_play -> run the Q entries -> capture -> editor_stop -> freeze the log
#   -> restore saves.
#
# WHY IT HAS TO BE PLAY (column 4 of play-log.tsv):
#   the fix's judged half is PRESENTATION-class -- whether the reused UI/D2ConfirmPanel
#   (original boxpieces frame + original medium button, title "存档损坏") is really
#   composited on screen over the character-select screen. Only a live session renders it.
#   The same session also has to show the CONTROL group (healthy saves -> no prompt, and a
#   real character still reaches Stage), otherwise "a dialog appeared" would be vacuous.
#
# ASCII only (PS 5.1 parses a BOM-less non-ASCII .ps1 as ANSI).
# =============================================================================
param(
    [string]$Why = 'R7 corrupt-save slice: the judged half is PRESENTATION-class (does the reused UI/D2ConfirmPanel with title "存档损坏" really composited on screen over the character-select screen) and the control group (healthy saves => no prompt; a real character still reaches Stage) only exists in a live session'
)

$ErrorActionPreference = 'Continue'

$root    = Split-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) -Parent
$proj    = Join-Path $root 'client'
$cs      = Join-Path $root 'tools\probes\drivers\q_save_drive.cs'
$saves   = Join-Path $proj 'setting\saves'
$shots   = Join-Path $root '.ai-tmp\screenshots'
$test    = Join-Path $root '.ai-tmp\test'
$fullBak = Join-Path $test 'q-save-backup-full'
$frozen  = Join-Path $test 'q-play-evidence.txt'
$runLog  = Join-Path $test 'q-runlog.txt'
$logPath = Join-Path $proj 'Logs\Editor.log'
$playLog = Join-Path $test 'play-log.tsv'

$keepRe = '^\[\d{4}-\d{2}-\d{2} [\d:.]+\] \[(Info|Warn|Error)\] \[(Q|Save|Flow|Ui|App|FileSlotStore)\]'

$script:offset = 0
$script:lines = New-Object System.Collections.ArrayList
$script:cli = 0

function Say([string]$s) {
    $stamp = (Get-Date).ToString('HH:mm:ss.fff')
    Write-Host ($stamp + ' ' + $s)
    Add-Content -Path $runLog -Value ($stamp + ' ' + $s) -Encoding UTF8
}

function Unity-Cmd([string[]]$argv) {
    $script:cli = $script:cli + 1
    $raw = & unity command @argv --project-path $proj --format json --no-pager 2>&1 | Out-String
    return $raw
}

function Run-Step([string]$entry, [string]$arg) {
    if ([string]::IsNullOrEmpty($arg)) {
        $raw = Unity-Cmd @('run_script', '--file', $cs, '--entry', $entry)
    } else {
        $raw = Unity-Cmd @('run_script', '--file', $cs, '--entry', $entry, '--args', ('[\"' + $arg + '\"]'))
    }
    $result = '?'
    try {
        $j = $raw | ConvertFrom-Json
        $r = $j.data.result.result
        if ($null -eq $r) { $r = $j.data.result.error }
        if ($null -eq $r) {
            $d = $j.data.result.diagnostics | Where-Object { $_.severity -eq 'error' }
            if ($d) { $r = 'COMPILE-ERRORS:' + (@($d).Count) + ' first=' + (@($d)[0].message) }
        }
        $result = [string]$r
    } catch { $result = 'PARSE-FAIL ' + ($raw -replace "`r?`n", ' ').Substring(0, [Math]::Min(300, $raw.Length)) }
    Say ('STEP ' + $entry + ' result=' + $result)
    return $result
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

# =============================================================================
# main
# =============================================================================
foreach ($d in @($shots, $test)) { if (-not (Test-Path $d)) { New-Item -ItemType Directory -Path $d | Out-Null } }
Set-Content -Path $runLog -Value ('# q run log ' + (Get-Date).ToString('s')) -Encoding UTF8
Say ('BEGIN root=' + $root)

# ---- 0. FULL backup of every save before anything runs (second line of defence) ----
if (Test-Path $fullBak) { Remove-Item $fullBak -Recurse -Force }
New-Item -ItemType Directory -Path $fullBak | Out-Null
Copy-Item (Join-Path $saves '*.json') $fullBak -Force
$bakCount = @(Get-ChildItem $fullBak -Filter *.json).Count
Say ('BACKUP-FULL dir=' + $fullBak + ' files=' + $bakCount)
if ($bakCount -lt 1) { Say 'ABORT no saves to back up'; exit 1 }

$probeBytes = [System.IO.File]::ReadAllBytes($cs)
$nonAscii = @($probeBytes | Where-Object { $_ -gt 127 }).Count
Say ('PROBE-ASCII bytes=' + $probeBytes.Length + ' nonAscii=' + $nonAscii)

Add-Content -Path $playLog -Value ((Get-Date).ToString('yyyy-MM-dd HH:mm') + "`timpl-Q-save`tq-save-r7`t" + $Why) -Encoding UTF8
Say ('PLAYLOG-APPENDED ' + $playLog)

# ---- 1. settle compile BEFORE entering Play (a mid-Play recompile domain-reloads the session)
Unity-Cmd @('editor_stop') | Out-Null
Start-Sleep -Seconds 2
Unity-Cmd @('recompile') | Out-Null
$upToDate = $false
for ($i = 0; $i -lt 40; $i++) {
    Start-Sleep -Seconds 2
    $st = Unity-Cmd @('recompile_status')
    if ($st -match 'up_to_date|completed|idle') { $upToDate = $true; break }
}
Say ('RECOMPILE-SETTLED upToDate=' + $upToDate)
Start-Sleep -Seconds 3

Unity-Cmd @('clear_console') | Out-Null
Init-Offset
Say ('LOG-OFFSET ' + $script:offset)
Unity-Cmd @('editor_play') | Out-Null
Start-Sleep -Seconds 5

# ---- 2. baseline + CONTROL group (healthy saves) ----
Run-Step 'Q.Api.Cfg' '' | Out-Null
Run-Step 'Q.Api.Probe' '' | Out-Null
Run-Step 'Q.Api.ControlContinue' '' | Out-Null
Start-Sleep -Seconds 4
Run-Step 'Q.Api.Probe' '' | Out-Null
Say 'SHOT-BEFORE-CONTROL'
Run-Step 'Q.Api.Shot' 'q1_charselect_control.png' | Out-Null
Start-Sleep -Seconds 3

# ---- 3. CONTROL: a real character still reaches Stage ----
Run-Step 'Q.Api.EnterGood' '' | Out-Null
Start-Sleep -Seconds 10
Run-Step 'Q.Api.Shot' 'q2_stage_control.png' | Out-Null
Start-Sleep -Seconds 4
Run-Step 'Q.Api.Probe' '' | Out-Null

# ---- 4. back to the menu, then corrupt EXACTLY one slot ----
Run-Step 'Q.Api.BackToMenu' '' | Out-Null
Start-Sleep -Seconds 5
Run-Step 'Q.Api.Probe' '' | Out-Null
Run-Step 'Q.Api.Corrupt' '' | Out-Null
Start-Sleep -Seconds 1

# ---- 5. THE DEFECT: open character select with the corrupt slot -> the prompt MUST appear
Run-Step 'Q.Api.CorruptContinue' '' | Out-Null
Start-Sleep -Seconds 5
Run-Step 'Q.Api.Probe' '' | Out-Null
Run-Step 'Q.Api.Shot' 'q3_corrupt_dialog.png' | Out-Null
Start-Sleep -Seconds 4

# ---- 6. second open of the same corrupt slot: no second dialog (per-session dedup) ----
Run-Step 'Q.Api.ReopenCharSelect' '' | Out-Null
Start-Sleep -Seconds 5
Run-Step 'Q.Api.Shot' 'q4_corrupt_second_open.png' | Out-Null
Start-Sleep -Seconds 3
Run-Step 'Q.Api.Probe' '' | Out-Null

# ---- 7. put the slot back, leave Play ----
Run-Step 'Q.Api.Restore' '' | Out-Null
Unity-Cmd @('editor_stop') | Out-Null
Say 'STOPPED'

Read-New
[System.IO.File]::WriteAllLines($frozen, [string[]]$script:lines, (New-Object System.Text.UTF8Encoding($false)))
Say ('LOGWRITE ' + $frozen + ' lines=' + $script:lines.Count)

foreach ($l in @($script:lines | Where-Object { $_ -match '\[Q\]' })) { Say ('EV ' + $l) }
foreach ($l in @($script:lines | Where-Object { $_ -match 'R7|损坏' })) { Say ('EV ' + $l) }

# ---- 8. restore every save from the full backup (belt and braces) ----
Copy-Item (Join-Path $fullBak '*.json') $saves -Force
$nowCount = @(Get-ChildItem $saves -Filter *.json).Count
Say ('RESTORE-FULL dir=' + $saves + ' files=' + $nowCount + ' (expected ' + $bakCount + ')')

foreach ($t in @('q1_charselect_control.png', 'q2_stage_control.png', 'q3_corrupt_dialog.png', 'q4_corrupt_second_open.png')) {
    $p = Join-Path $shots $t
    if (Test-Path $p) { $fi = Get-Item $p; Say ('TILE ' + $t + ' bytes=' + $fi.Length + ' ' + $fi.LastWriteTime.ToString('HH:mm:ss')) }
    else { Say ('TILE-MISSING ' + $t) }
}

$consoleJson = Unity-Cmd @('console_status')
Say ('CONSOLE ' + (($consoleJson -replace "`r?`n", ' ').Substring(0, [Math]::Min(600, $consoleJson.Length))))
Say ('END cli=' + $script:cli)
