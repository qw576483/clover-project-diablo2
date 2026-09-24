# =============================================================================
# d2u32_lockproto.ps1 -- pure functions of the TEAM lock protocol (v2.1).
#
#   Single source of truth: `d2u32_run.ps1` dot-sources this file AND so does
#   `d2u32_lock_selftest.ps1` => the decision logic can never drift from its test
#   (the "test mirrors production" anti-pattern is avoided: there is only ONE copy).
#   This file has NO side effects (functions only), so dot-sourcing it is safe.
#
# v2 (team lead 2026-09-24): lock = "<owner> <ISO8601> <PID>";
#   stale = PID dead OR age >= 12 min; 12 min is the ONLY threshold team-wide.
#
# v2.1 (team lead 2026-09-24, after classcols found a near-miss):
#   *** A MISSING PID FIELD MEANS "UNKNOWN", NOT "DEAD" ***
#   => a FRESH lock without a PID field must still be YIELDED to;
#      only age >= 12 min may be taken over (and the takeover must be recorded as
#      "took over <owner>'s stale lock, its lock has no PID field").
#   Rationale: locks written by older runners carry two fields only. Treating
#   "cannot read a PID" as "process is dead" would instantly rob a brand-new lock
#   -- the same shape as the 11:42 incident.
#
# ASCII only (PS 5.1 parses a BOM-less non-ASCII .ps1 as ANSI).
# =============================================================================

# u52play 2026-09-24: PowerShell 5.1's `Set-Content -Encoding UTF8` writes a BOM, and the BOM can
# survive a raw read as the first char of the owner -> the owner no longer matches "mine" ->
# we would refuse to release our OWN lock (zombie) and refuse to stop the editor at shutdown.
# Belt and braces: write locks with -Encoding ASCII in the runner AND strip zero-width chars here.
function Remove-ZeroWidth([string]$s) {
    if ($null -eq $s) { return '' }
    return ($s -replace "[\uFEFF\u200B\u200C\u200D]", '')
}

# Parse "<owner> <ISO8601> <PID>" -> { Owner; Pid; HasPid }
function Parse-LockFields([string]$raw) {
    $o = [pscustomobject]@{ Owner = ''; Pid = ''; HasPid = $false }
    $raw = Remove-ZeroWidth $raw
    if ([string]::IsNullOrWhiteSpace($raw)) { return $o }
    $one = ($raw.Trim() -replace "`r?`n", ' ')
    $parts = @($one -split '\s+')
    if ($parts.Count -ge 1) { $o.Owner = $parts[0] }
    if ($parts.Count -ge 3) {
        $last = $parts[$parts.Count - 1]
        if ($last -match '^\d+$') { $o.Pid = $last; $o.HasPid = $true }
    }
    return $o
}

# Real liveness. u52play 2026-09-24: the parameter must NOT be named `$pid` -- `$pid` is a
# read-only PowerShell automatic variable and `function F([int]$pid)` throws "Cannot overwrite
# variable pid" ONLY at call time (parsing stays silent). Hence `$lockPid`.
function Test-PidAlive([string]$lockPid) {
    if ($lockPid -notmatch '^\d+$') { return $false }
    return [bool](Get-Process -Id ([int]$lockPid) -ErrorAction SilentlyContinue)
}

# Decide stale-ness. $isAlive is an injectable scriptblock (pid string -> bool) so the self-test can
# cover the decision WITHOUT spawning processes; omitted => the REAL one above (so the default path
# is the tested path too -- no second copy of the liveness rule anywhere).
function Test-LockStale([string]$raw, [double]$ageMinutes, [scriptblock]$isAlive) {
    $f = Parse-LockFields $raw
    if ($null -eq $isAlive) { $isAlive = { param($lockPid) Test-PidAlive $lockPid } }
    if ($f.HasPid) {
        $alive = [bool](& $isAlive $f.Pid)
        if (-not $alive) { return $true }            # PID dead => stale, whatever the age
        return ($ageMinutes -ge 12)                  # PID alive => only the 12 min rule
    }
    # no PID field => UNKNOWN (not dead): fresh => never take over; >= 12 min => stale
    return ($ageMinutes -ge 12)
}

# One-line description for the trace / report (records whether the PID field was missing).
function Format-LockFields($f, [double]$ageMinutes, [bool]$stale) {
    $pidNote = ''
    if ($f.HasPid) { $pidNote = 'pid=' + $f.Pid }
    else { $pidNote = 'pid=(none -> unknown, NOT dead)' }
    return ('owner=' + $f.Owner + ' ' + $pidNote + ' age=' + [math]::Round($ageMinutes, 1) + 'm stale=' + $stale)
}
