# =============================================================================
# tools/hooks/install.ps1 -- install the clover-project-diablo2 pre-commit gate
#
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools/hooks/install.ps1
#
# What it does (and ONLY this):
#   * points core.hooksPath at .githooks for THIS repository (local config)
#   * if .githooks/pre-commit is already tracked, marks it executable in the
#     index (git update-index --chmod=+x)
#
# It never touches any other git config key, and never commits / pushes.
# WHY this exists: SKILL.md 0.5 meta-rule -- a rule that "must always hold"
# is only guaranteed once it is a real gate (a hook, not a reminder).
# Doc: tools/hooks/README.md
# =============================================================================
$ErrorActionPreference = 'Continue'   # git writes progress to stderr; do not turn that into a stop

$root = (& git rev-parse --show-toplevel 2>$null)
if (-not $root) {
    Write-Error "Not inside a git work tree -- run this from inside the repository."
    exit 1
}
Set-Location $root
Write-Host "repo root: $root"

$hook = Join-Path $root '.githooks\pre-commit'
if (-not (Test-Path $hook)) {
    Write-Error "missing $hook -- the hook file must exist before installing."
    exit 1
}

# 1) point core.hooksPath at .githooks (relative -> the repo stays portable)
& git config core.hooksPath .githooks
if ($LASTEXITCODE -ne 0) { Write-Error "git config core.hooksPath failed."; exit 1 }
$set = (& git config --get core.hooksPath)
Write-Host "core.hooksPath = $set"

# 2) record the executable bit if (and only if) the hook is already tracked.
#    Untracked -> do NOT stage anything; the maintainer's first commit should
#    use `git add --chmod=+x .githooks/pre-commit` (see README).
& git ls-files --error-unmatch .githooks/pre-commit 2>$null | Out-Null
$tracked = ($LASTEXITCODE -eq 0)
if ($tracked) {
    & git update-index --chmod=+x .githooks/pre-commit 2>$null
    Write-Host "marked .githooks/pre-commit executable in the index (mode 100755)."
} else {
    Write-Host "note: .githooks/pre-commit is not tracked yet -> index left untouched."
    Write-Host "      first commit should run: git add --chmod=+x .githooks/pre-commit"
}

Write-Host ""
Write-Host "installed. every commit in this repo now runs tools/verify.ps1."
Write-Host "emergency bypass (and log it): SKIP_VERIFY_GATE=1 git commit ..."
exit 0
