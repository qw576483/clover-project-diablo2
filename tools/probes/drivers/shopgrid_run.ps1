# =============================================================================
# shopgrid_run.ps1 -- ONE Play session for the shop-grid occupancy probe
#                    (judging asset of 片 impl-shop, same shape as x_run.ps1).
#
#   powershell -NoProfile -ExecutionPolicy Bypass -File shopgrid_run.ps1
#
# Fixed order: editor_stop -> settle any pending recompile -> clear_console -> editor_play
#   -> Byline.Api.Cfg -> ShopGrid.Api.Cfg -> ShopGrid.Tour.Run (zero-arg entry; the CLI
#   requires an argument for Install(string) and a JSON array for --args)
#   -> wait for the done marker -> editor_stop -> append play-log -> freeze the [SG]/[Npc] lines.
#
# Evidence written:
#   .ai-tmp/screenshots/shop_cells_buy.png / shop_cells_sell.png   (screen composite tiles)
#   .ai-tmp/screenshots/shopgrid_evidence.txt                      (frozen log window)
#   .ai-tmp/test/shopgrid_done.txt                                 (done marker)
# ASCII only (PS 5.1 reads a BOM-less non-ASCII .ps1 as ANSI).
# =============================================================================
$ErrorActionPreference = 'Continue'

$root  = Split-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) -Parent
$proj  = Join-Path $root 'client'
$shots = Join-Path $root '.ai-tmp\screenshots'
$test  = Join-Path $root '.ai-tmp\test'
$done  = Join-Path $test 'shopgrid_done.txt'
$runlog = Join-Path $test 'shopgrid_runlog.txt'
$drv   = Join-Path $root 'tools\probes\drivers\shopgrid_drive.cs'
$byl   = Join-Path $root 'tools\probes\drivers\byline_drive.cs'
$playLog = Join-Path $test 'play-log.tsv'
$why = 'shop item grid occupancy: the 10x10 buy/sell board must place each item by its own item_c grid size (2x3 shield / 1x4 staff) and a click on a COVERED cell must buy the item that OWNS that cell - the owner map, the icon block rects and the purchase chain (panel -> Events.ShopBuyRequest -> NpcModule.Buy) exist only in a live Play session'

function Say([string]$s) {
    $stamp = (Get-Date).ToString('HH:mm:ss.fff')
    Write-Host ($stamp + ' ' + $s)
    Add-Content -Path $runlog -Value ($stamp + ' ' + $s) -Encoding UTF8
}

function UC([string[]]$argv) {
    $raw = & unity command @argv --project-path $proj --format json --no-pager 2>&1 | Out-String
    return $raw
}

foreach ($d in @($shots, $test)) { if (-not (Test-Path $d)) { New-Item -ItemType Directory -Path $d | Out-Null } }
Set-Content -Path $runlog -Value '# shopgrid run log' -Encoding UTF8
if (Test-Path $done) { Remove-Item $done -Force }

$logPath = Join-Path $proj 'Logs\Editor.log'
$off = 0
if (Test-Path $logPath) { $off = (Get-Item $logPath).Length }

Add-Content -Path $playLog -Value ((Get-Date).ToString('yyyy-MM-dd HH:mm') + "`timpl-shop`tshop-grid-occupancy`t" + $why) -Encoding UTF8
Say ('PLAYLOG-APPENDED ' + $playLog)

Say 'STOP'
UC @('editor_stop') | Out-Null
Start-Sleep -Seconds 2

# settle any pending script compile BEFORE entering Play (a mid-play recompile reloads the domain)
UC @('recompile') | Out-Null
for ($i = 0; $i -lt 40; $i++) {
    Start-Sleep -Seconds 2
    $st = UC @('recompile_status')
    if ($st -match 'up_to_date|completed|idle') { Say ('RECOMPILE ' + ($st -replace "`r?`n", ' ')); break }
}
Start-Sleep -Seconds 2

UC @('clear_console') | Out-Null
Say 'PLAY'
$play = UC @('editor_play')
Say ('PLAY-RESULT ' + ($play -replace "`r?`n", ' '))
Start-Sleep -Seconds 5

$cfg = UC @('run_script', '--file', $byl, '--entry', 'Byline.Api.Cfg')
Say ('BYLINE-CFG ' + ($cfg -replace "`r?`n", ' '))

$cfg2 = UC @('run_script', '--file', $drv, '--entry', 'ShopGrid.Api.Cfg')
Say ('SG-CFG ' + ($cfg2 -replace "`r?`n", ' '))

$inst = UC @('run_script', '--file', $drv, '--entry', 'ShopGrid.Tour.Run')
Say ('INSTALL ' + ($inst -replace "`r?`n", ' '))

if ($inst -notmatch 'INSTALLED') {
    Say 'INSTALL-FAILED (the driver did not install) -> capturing the console and stopping'
    $cons = UC @('console', '--tail', '60')
    Say ('CONSOLE ' + ($cons -replace "`r?`n", ' '))
    Start-Sleep -Seconds 2
    UC @('editor_stop') | Out-Null
    Say 'STOPPED'
    Say 'END'
    exit 1
}

$t0 = Get-Date
while (((Get-Date) - $t0).TotalSeconds -lt 240) {
    if (Test-Path $done) { break }
    Start-Sleep -Milliseconds 500
}
if (Test-Path $done) { Say ('DONE ' + (Get-Content $done -Raw).Trim()) } else { Say 'DONE-MISSING' }

Start-Sleep -Seconds 2
UC @('editor_stop') | Out-Null
Say 'STOPPED'

# freeze the log window the run produced (the driver logs with tag SG via Game.Logger)
$fs = New-Object System.IO.FileStream($logPath, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::ReadWrite)
$fs.Seek($off, [System.IO.SeekOrigin]::Begin) | Out-Null
$len = [int]($fs.Length - $off)
$buf = New-Object byte[] $len
$read = $fs.Read($buf, 0, $len)
$fs.Close(); $fs.Dispose()
$txt = [System.Text.Encoding]::UTF8.GetString($buf, 0, $read)
$keep = @($txt -split "`r?`n" | Where-Object { $_ -match '\[SG\]|\[Npc\]|\[Ui\] 请求买入' })
$ev = Join-Path $shots 'shopgrid_evidence.txt'
[System.IO.File]::WriteAllLines($ev, [string[]]$keep, (New-Object System.Text.UTF8Encoding($false)))
Say ('LOGWRITE ' + $ev + ' lines=' + $keep.Count)
foreach ($l in @($keep | Where-Object { $_ -match '\[SG\]' })) { Say ('EV ' + $l) }
Say 'END'
