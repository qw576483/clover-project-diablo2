# =============================================================================
# u44_pixel_diff.ps1 -- pixel-fingerprint differ for the u44 hover-brighten claim
#
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools/probes/measure/u44_pixel_diff.ps1
#
# WHAT IT MEASURES (sheet u44impl, closeout item 3: "un-hovered must stay pixel-identical"):
#   The runtime read-back proves the MaterialPropertyBlock goes 3.0/1.01 (hover) and
#   1.0/1.0 (off), and that the ORIGINAL material object is restored -- but a property
#   value alone does not prove anything reached the screen.  This tool compares the two
#   Play screenshots (u44_01_shader_on.png vs u44_02_shader_off.png, same camera, only the
#   highlight toggled) and writes a machine-readable TSV:
#
#       # u44 pixel diff ...
#       on   <file> <bytes>
#       off  <file> <bytes> <offBrightness>
#       diff <changedPixelCount> <changedRatio> <maxChannelDelta> <meanAbsDelta> <w> <h>
#
#   The uicheck host (HoverSelectCheck.CheckU44Closeout) reads this TSV and asserts
#   changedPixelCount > 0 AND maxChannelDelta > 0 -- i.e. "the screen really brightened",
#   a NUMBER, not a claim.  (The host cannot decode PNGs: System.Drawing is not part of
#   its reference set, so the measurement lives here and the assertion lives in the host.)
#
# ASCII only (PS 5.1 reads a BOM-less non-ASCII .ps1 as ANSI).
# =============================================================================
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

# Project root = the first ancestor that contains client/Assets (self-correcting: no fixed
# number of Split-Path hops -- a wrong hop count silently points the script at a sibling dir).
$root = $PSScriptRoot
while ($root -and -not (Test-Path (Join-Path $root 'client\Assets'))) {
    $up = Split-Path $root -Parent
    if ($up -eq $root -or [string]::IsNullOrEmpty($up)) { break }
    $root = $up
}
if (-not (Test-Path (Join-Path $root 'client\Assets'))) { Write-Output 'ROOT-NOT-FOUND'; exit 1 }
Write-Output ('ROOT ' + $root)
$shots = Join-Path $root '.ai-tmp\screenshots'
$refs  = Join-Path $root 'tools\probes\refs'
$on    = Join-Path $shots 'u44_01_shader_on.png'
$off   = Join-Path $shots 'u44_02_shader_off.png'
$out   = Join-Path $refs  'u44_pixel_diff.tsv'

foreach ($p in @($on, $off)) { if (-not (Test-Path $p)) { Write-Output ("MISSING " + $p); exit 1 } }

$a = [System.Drawing.Bitmap]::FromFile($on)
$b = [System.Drawing.Bitmap]::FromFile($off)
if ($a.Width -ne $b.Width -or $a.Height -ne $b.Height) {
    Write-Output ("SIZE-MISMATCH " + $a.Width + "x" + $a.Height + " vs " + $b.Width + "x" + $b.Height)
    exit 1
}

# LockBits for speed (1920x1080 x 2 images); both are 32bpp ARGB PNGs written by Unity.
$rect = New-Object System.Drawing.Rectangle(0, 0, $a.Width, $a.Height)
$fmt  = [System.Drawing.Imaging.ImageFormat]::Png
$da = $a.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$db = $b.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$stride = $da.Stride
$bytes = $stride * $a.Height
$bufA = New-Object byte[] $bytes
$bufB = New-Object byte[] $bytes
[System.Runtime.InteropServices.Marshal]::Copy($da.Scan0, $bufA, 0, $bytes)
[System.Runtime.InteropServices.Marshal]::Copy($db.Scan0, $bufB, 0, $bytes)
$a.UnlockBits($da); $b.UnlockBits($db)

$changed = 0
$maxD = 0
$sumD = [long]0
for ($i = 0; $i -lt $bytes; $i += 4) {
    $d0 = [math]::Abs($bufA[$i]   - $bufB[$i])
    $d1 = [math]::Abs($bufA[$i+1] - $bufB[$i+1])
    $d2 = [math]::Abs($bufA[$i+2] - $bufB[$i+2])
    $d = $d0; if ($d1 -gt $d) { $d = $d1 }; if ($d2 -gt $d) { $d = $d2 }
    if ($d -gt 0) { $changed++ }
    if ($d -gt $maxD) { $maxD = $d }
    $sumD += $d
}
$px = $a.Width * $a.Height
$ratio = [math]::Round(100.0 * $changed / $px, 4)
$mean = [math]::Round(1.0 * $sumD / $px, 6)
$w = $a.Width; $h = $a.Height
$a.Dispose(); $b.Dispose()

$lines = @(
    '# u44 pixel diff -- u44_01_shader_on.png (hover, _Brightness 3.0) vs u44_02_shader_off.png (restored original)',
    '# measured by tools/probes/measure/u44_pixel_diff.ps1 ; asserted by uicheck HoverSelectCheck.CheckU44Closeout',
    '# CONFOUND: the two shots are 11 frames apart (frame 116 vs 127) and the player sprite runs an idle',
    '#   animation, so part of the changed pixels come from the animation, NOT only from _Brightness.',
    '#   => this number proves "the two frames differ"; the clean proof of "it brightened" is the',
    '#   property-block read-back (shader=Sprite, _Brightness=3 _Contrast=1.01, visible=1) plus the',
    '#   contact sheet read by eye; a confound-free pair needs a frozen animation (not taken yet).',
    ('on' + "`t" + $on + "`t" + (Get-Item $on).Length + "`t_Brightness=3.0"),
    ('off' + "`t" + $off + "`t" + (Get-Item $off).Length + "`t_Brightness=1.0"),
    ('diff' + "`t" + $changed + "`t" + $ratio + "`t" + $maxD + "`t" + $mean + "`t" + $w + "`t" + $h)
)
[System.IO.File]::WriteAllLines($out, [string[]]$lines, (New-Object System.Text.UTF8Encoding($false)))
Write-Output ("TSV " + $out)
$lines | ForEach-Object { Write-Output ("  " + $_) }
Write-Output ("RESULT changedPixels=" + $changed + " (" + $ratio + "%) maxChannelDelta=" + $maxD + " meanAbsDelta=" + $mean)
