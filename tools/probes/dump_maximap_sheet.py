# -*- coding: utf-8 -*-
"""dump_maximap_sheet.py -- `AUTOMAP/MaxiMap.dc6`（1260 帧 × 16×32）任意区间排版成联络图。

为什么需要它（片 automap-redo2，2026-09-23）："我们的 automap 边界没有原版那种**浅灰山岩块**"
是选 cel 的问题还是配色的问题，只有把**我们表真正用到的 cel**（`AutoMapCel.generated.cs` 里
`CelPixels` 的键）与相邻帧一起画出来才判得了。带 `*` 的格子 = 我们当前用到的 cel。

用法：
  python tools/probes/dump_maximap_sheet.py <lo> <hi> [out.png]
  默认 out = `.ai-tmp/screenshots/automapredo_maximap_<lo>_<hi>.png`
"""
import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
sys.path.insert(0, os.path.join(ROOT, "tools"))

from d2codec import dc6 as dc6mod  # noqa: E402
from PIL import Image, ImageDraw  # noqa: E402

MAXIMAP = os.path.join(ROOT, "原版资源/d2dc6/data/global/ui/AUTOMAP/MaxiMap.dc6")
ACT1 = os.path.join(ROOT, "原版资源/d2raw/data/global/palette/ACT1/Pal.PL2")
GEN = os.path.join(ROOT, "client/Assets/Scripts/Core/AutoMapCel.generated.cs")
SCALE = 3
PAD = 6
LBL = 14
COLS = 8


def used_cels():
    txt = open(GEN, "r", encoding="utf-8").read()
    return sorted(set(int(m) for m in re.findall(r"^\s*\{\s*(\d+),\s*Decode\(", txt, re.M)))


def main(argv):
    if len(argv) < 3:
        print(__doc__)
        return 2
    lo, hi = int(argv[1]), int(argv[2])
    out = argv[3] if len(argv) > 3 else os.path.join(
        ROOT, ".ai-tmp/screenshots/automapredo_maximap_%d_%d.png" % (lo, hi))
    used = set(used_cels())
    print("cels present in AutoMapCel.CelPixels: %s" % sorted(used))
    d = dc6mod.parse(open(MAXIMAP, "rb").read())
    pal = dc6mod.read_pl2(ACT1)
    print("MaxiMap frames=%d (16x32)" % len(d.frames))

    idxs = list(range(lo, min(hi, len(d.frames))))
    rows = (len(idxs) + COLS - 1) // COLS
    cw = 16 * SCALE + PAD * 2
    ch = 32 * SCALE + PAD * 2 + LBL
    sheet = Image.new("RGB", (COLS * cw, rows * ch), (16, 16, 20))
    dr = ImageDraw.Draw(sheet)
    for k, i in enumerate(idxs):
        f = d.frames[i]
        im = Image.new("RGB", (f.width, f.height), (10, 10, 12))
        px = im.load()
        for y in range(f.height):
            for x in range(f.width):
                v = f.indices[y * f.width + x]
                if v:
                    px[x, y] = pal[v][:3]
        im = im.resize((f.width * SCALE, f.height * SCALE), Image.NEAREST)
        cx = (k % COLS) * cw
        cy = (k // COLS) * ch
        sheet.paste(im, (cx + PAD, cy + LBL + PAD))
        dr.text((cx + PAD, cy + 2), "cel %d%s" % (i, " *" if i in used else ""),
                fill=(255, 255, 120) if i in used else (150, 150, 160))
    os.makedirs(os.path.dirname(out), exist_ok=True)
    sheet.save(out)
    print("sheet -> %s (%dx%d)  *=used by our table" % (out, sheet.width, sheet.height))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
