# =============================================================================
# dialogoptions_run.ps1 -- dialog-options slice: ONE Play chain that re-captures
# the NPC dialog tiles after the hover-plate fix (UiArt medium-button highlight).
#
# Chain: wait for the team play.lock (absent/stale + editor not playing/compiling)
#        -> take the lock -> `unity command recompile` (+ wait until not compiling)
#        -> append one play-log.tsv row -> x_run.ps1 -Tag <tag> (the existing X tour:
#           it walks boot/menu/charselect/town and opens Akara's dialog twice)
#        -> editor_stop -> release the lock (only if it is ours).
#
# ASCII only (PS 5.1 parses a BOM-less non-ASCII .ps1 as ANSI).
# =============================================================================
param(
    [string]$Tag = 'do1',
    [int]$LockWaitSec = 240,
    [int]$CompileWaitSec = 240,
    [switch]$SkipTour
)

$ErrorActionPreference = 'Continue'

$root    = 'c:/Work/Server/f-v2/clover-project-diablo2'
$proj    = Join-Path $root 'client'
$test    = Join-Path $root '.ai-tmp/test'
$lock    = Join-Path $test 'play.lock'
$playLog = Join-Path $test 'play-log.tsv'
$trace   = Join-Path $test ('dialogoptions_steps_' + $Tag + '.txt')
$me      = 'dialog-options'

function Say([string]$s) {
    $line = (Get-Date).ToString('HH:mm:ss.fff') + ' ' + $s
    Write-Host $line
    Add-Content -Path $trace -Value $line -Encoding UTF8
}

function EditorStatus() {
    $raw = (unity command editor_status --format json 2>$null) -join ''
    try { return ($raw | ConvertFrom-Json).data.result } catch { return $null }
}

Set-Location $proj
Say ('START tag=' + $Tag)

# ---- 1. take the team lock ---------------------------------------------------
$deadline = (Get-Date).AddSeconds($LockWaitSec)
$mine = $false
while ((Get-Date) -lt $deadline) {
    $st = EditorStatus
    $has = Test-Path $lock
    $age = -1
    if ($has) { $age = [int]((Get-Date) - (Get-Item $lock).LastWriteTime).TotalSeconds }
    $idle = ($st -ne $null) -and ($st.playMode -eq 'stopped') -and ($st.compiling -eq $false)
    if ($idle -and ((-not $has) -or $age -gt 360)) {
        Set-Content -Path $lock -Value ($me + ' ' + (Get-Date).ToString('o')) -Encoding UTF8
        $mine = $true
        Say ('LOCK-TAKEN playMode=' + $st.playMode + ' compiling=' + $st.compiling)
        break
    }
    $pm = '?'
    $cp = '?'
    if ($st -ne $null) { $pm = $st.playMode; $cp = $st.compiling }
    Say ('LOCK-WAIT has=' + $has + ' age=' + $age + ' playMode=' + $pm + ' compiling=' + $cp)
    Start-Sleep 15
}
if (-not $mine) { Say 'LOCK-NOT-ACQUIRED'; exit 2 }

try {
    # ---- 2. recompile (this slice changed .cs files) -------------------------
    $out = (unity command recompile 2>&1) -join ' '
    Say ('RECOMPILE-ISSUED ' + $out)
    $cdl = (Get-Date).AddSeconds($CompileWaitSec)
    $ok = $false
    while ((Get-Date) -lt $cdl) {
        Start-Sleep 10
        $st = EditorStatus
        if ($st -and $st.compiling -eq $false) { $ok = $true; break }
    }
    Say ('RECOMPILE-DONE ok=' + $ok)

    # ---- 3. account for the Play session (SKILL 4.4) -------------------------
    $why = 'presentation-class: only a real frame shows whether the NPC dialog option buttons are legible when the pointer HOVERS one of them (the hover state swaps the button plate; that plate was the corrupt btn_med_sel.png). The same session re-reads the DIALOG= lines (option labels) so the numeric and the visual half cannot drift apart.'
    Add-Content -Path $playLog -Value ((Get-Date).ToString('yyyy-MM-dd HH:mm') + "`t" + $me + "`t" + $Tag + "`t" + $why) -Encoding UTF8
    Say 'PLAY-LOG-APPENDED'

    if (-not $SkipTour) {
        # ---- 4. the existing X tour (opens Akara's dialog twice) -------------
        Say 'TOUR-START x_run.ps1'
        $x = Join-Path $root 'tools/probes/drivers/x_run.ps1'
        & powershell -NoProfile -ExecutionPolicy Bypass -File $x -Tag $Tag 2>&1 | ForEach-Object { Say ('x| ' + $_) }
        Say 'TOUR-DONE'
    }
}
finally {
    # ---- 5. stop + release our own lock only --------------------------------
    (unity command editor_stop 2>&1) | Out-Null
    Say 'EDITOR-STOPPED'
    if ((Test-Path $lock) -and ((Get-Content $lock -Raw) -match $me)) {
        Remove-Item $lock -Force
        Say 'LOCK-RELEASED (ours)'
    } else {
        Say 'LOCK-NOT-OURS (left alone)'
    }
}
Say 'END'
