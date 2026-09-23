# -*- coding: utf-8 -*-
"""dump_mapicons_sheet.py -- `MINIMAP/mapicons.DC6` 逐帧解出并排版成可读联络图。

为什么需要它（片 automap-redo2，2026-09-23）：这 8 帧是**纯白模板**（全帧只有调色板索引 32），
把透明通道当白底画出来 == 看不见，所以"这 8 帧到底是边框图块还是图标"必须靠**深色底 + 放大**读图，
而不是靠文件名猜。本脚本把这件事变成可复跑的一条命令，并把每帧的机械量（尺寸 / offset / flip /
非零像素数 / bbox）打到 stdout —— 报告里那张联络图就是它产出的。

产物：`.ai-tmp/screenshots/automapredo_mapicons_sheet.png`（8 帧 × 6 倍最近邻）

用法：`python tools/probes/dump_mapicons_sheet.py [out.png]`
"""
import os
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
sys.path.insert(0, os.path.join(ROOT, "tools"))

from d2codec import dc6 as dc6mod  # noqa: E402
from PIL import Image, ImageDraw  # noqa: E402

DC6 = os.path.join(ROOT, "原版资源/d2dc6/data/global/ui/MINIMAP/mapicons.DC6")
DEFAULT_OUT = os.path.join(ROOT, ".ai-tmp/screenshots/automapredo_mapicons_sheet.png")
SCALE = 6
PAD = 12
LBL = 24
COLS = 4


def main(argv):
    out = argv[1] if len(argv) > 1 else DEFAULT_OUT
    d = dc6mod.parse(open(DC6, "rb").read())
    print("dirs=%d fpd=%d frames=%d" % (d.directions, d.frames_per_dir, len(d.frames)))
    used = set()
    cells = []
    for i, f in enumerate(d.frames):
        nz = [(x, y) for y in range(f.height) for x in range(f.width)
              if f.indices[y * f.width + x]]
        for v in f.indices:
            if v:
                used.add(v)
        bbox = (min(p[0] for p in nz), min(p[1] for p in nz),
                max(p[0] for p in nz), max(p[1] for p in nz)) if nz else None
        cells.append((i, f, bbox, len(nz)))
        print("frame %d: %dx%d off=(%d,%d) flip=%d bbox=%s nonzero=%d"
              % (i, f.width, f.height, f.offset_x, f.offset_y, f.flip, bbox, len(nz)))
    print("palette indices used: %s" % sorted(used))

    rows = (len(cells) + COLS - 1) // COLS
    cw = max(f.width for _, f, _, _ in cells) * SCALE + PAD * 2
    ch = max(f.height for _, f, _, _ in cells) * SCALE + PAD * 2 + LBL
    sheet = Image.new("RGB", (COLS * cw, rows * ch), (16, 16, 20))
    dr = ImageDraw.Draw(sheet)
    for i, f, bbox, nz in cells:
        im = Image.new("RGB", (f.width, f.height), (24, 24, 30))
        px = im.load()
        for y in range(f.height):
            for x in range(f.width):
                if f.indices[y * f.width + x]:
                    px[x, y] = (255, 255, 255)
        im = im.resize((f.width * SCALE, f.height * SCALE), Image.NEAREST)
        cx = (i % COLS) * cw
        cy = (i // COLS) * ch
        sheet.paste(im, (cx + PAD, cy + LBL + PAD))
        dr.text((cx + PAD, cy + 4), "frame %d  %dx%d  nz=%d" % (i, f.width, f.height, nz),
                fill=(255, 240, 170))
    os.makedirs(os.path.dirname(out), exist_ok=True)
    sheet.save(out)
    print("sheet -> %s (%dx%d)" % (out, sheet.width, sheet.height))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
