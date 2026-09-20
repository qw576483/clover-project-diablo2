# 回归全部离线自检宿主（agent-05 用）：dotnet run 每个 tools/<name>check，输出末行结论
$root = Split-Path -Parent $PSScriptRoot
$hosts = @('corecheck','mapcheck','dircheck','playercheck','combatcheck','itemcheck','audiocheck','uicheck','flowcheck','fullcheck','buildcheck')
$fail = 0
foreach ($h in $hosts) {
    $dir = Join-Path $PSScriptRoot $h
    if (-not (Test-Path $dir)) { Write-Output ("{0,-12} SKIP (no dir)" -f $h); continue }
    Push-Location $dir
    $out = & dotnet run -v q --nologo 2>&1 | Out-String
    $code = $LASTEXITCODE
    Pop-Location
    $lines = ($out -split "`r?`n") | Where-Object { $_ -match '自检|通过|失败|FAIL|error' } | Select-Object -Last 3
    $status = if ($code -eq 0) { 'PASS' } else { 'FAIL' }
    if ($code -ne 0) { $fail++ }
    Write-Output ("{0,-12} exit={1} {2}" -f $h, $code, $status)
    foreach ($l in $lines) { Write-Output ("             | " + $l.Trim()) }
}
Write-Output ("TOTAL_HOSTS=" + $hosts.Count + " FAILED=" + $fail)
