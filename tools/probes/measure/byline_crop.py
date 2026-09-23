# -*- coding: utf-8 -*-
# Crop the brand-byline band out of a composited 1920x1080 game tile and zoom it with NEAREST.
# The band is taken from the RUNTIME numbers the driver logged (screenCorners), not from a guess:
#   corner y is bottom-up (WorldToScreenPoint) -> image row = H - y.
# Usage: byline_crop.py <tile.png> <out_prefix> [threshold]
import sys, io
from PIL import Image
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8')

tile, prefix = sys.argv[1], sys.argv[2]
TH = int(sys.argv[3]) if len(sys.argv) > 3 else 80
im = Image.open(tile).convert("RGB")
W, H = im.size
print("tile %s  %dx%d  threshold=%d" % (tile, W, H, TH))

x0, x1 = 500, 1420                      # runtime screenCorners x 510..1410 (+margin)
y_top, y_bot = H - 110, H - 44          # runtime screenCorners y(bottom-up) 51..105 (+margin)
band = im.crop((x0, y_top, x1, y_bot))
band.save(prefix + "_band.png")

px = band.load()
def isink(x, y):
    r, g, b = px[x, y]
    return max(r, g, b) > TH

cols = [x for x in range(band.width) if sum(1 for y in range(band.height) if isink(x, y)) >= 2]
rows = [y for y in range(band.height) if sum(1 for x in range(band.width) if isink(x, y)) >= 2]
if not cols or not rows:
    print("NO INK at threshold", TH); sys.exit(1)
bx0, bx1, by0, by1 = min(cols), max(cols), min(rows), max(rows)
print("ink bbox in band: x %d..%d (w=%d) y %d..%d (h=%d)" % (bx0, bx1, bx1 - bx0 + 1, by0, by1, by1 - by0 + 1))
print("ink bbox in FULL tile: x %d..%d  y %d..%d" % (x0 + bx0, x0 + bx1, y_top + by0, y_top + by1))

pad = 4
ink = band.crop((max(0, bx0 - pad), max(0, by0 - pad), min(band.width, bx1 + 1 + pad), min(band.height, by1 + 1 + pad)))
ink.save(prefix + "_ink.png")
print("ink crop %s  %dx%d" % (prefix + "_ink.png", ink.width, ink.height))
for z in (4, 8):
    big = ink.resize((ink.width * z, ink.height * z), Image.NEAREST)
    big.save("%s_ink_%dx.png" % (prefix, z))
    print("zoom %s_ink_%dx.png  %dx%d" % (prefix, z, big.width, big.height))

# per-column ink -> glyph clusters (gaps > 1px separate glyphs)
dark = [1 if any(isink(x, y) for y in range(band.height)) else 0 for x in range(band.width)]
runs, i = [], 0
while i < len(dark):
    if dark[i]:
        j = i
        while j + 1 < len(dark) and dark[j + 1]:
            j += 1
        runs.append((i, j)); i = j + 1
    else:
        i += 1
print("clusters(>=2px) in FULL tile x: %s" % [(x0 + a, x0 + b) for a, b in runs if b - a >= 1])
print("cluster count = %d  (expected 16 glyph nodes incl. 1 space)" % len([1 for a, b in runs if b - a >= 1]))
