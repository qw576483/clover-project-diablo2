#!/usr/bin/env python3
"""U46 (S1): "does the automap overlay actually reach the composited frame?" - measurable.

Why this exists: the automap is a **sparse dark line art** overlay (the original MaxiMap cels
blitted with the ACT1 palette: main colours are (20,20,20)/(56,48,40)/(80,72,60) - see
`.ai-tmp/test/automap-plan.md` 1.4). It is therefore invisible to a casual look at a screenshot
(measured: only ~1.2% of the 1920x1080 pixels change when the panel opens in Town), so a human
reading of the PNG cannot tell "the overlay never rendered" apart from "it rendered but is dark".

This script answers it mechanically: it diffs two tiles captured by `automap_drive.cs`
(panel open vs panel closed, **same area, same reveal state**) and writes an AMPLIFIED
difference map. The difference map shows the isometric cel lattice around the player when the
overlay IS composited.

usage:
  python tools/probes/measure/tab_diff.py <on.png> <off.png> [out_diff.png]
"""
import sys
from PIL import Image


def main(argv):
    if len(argv) < 3:
        print(__doc__)
        return 2
    on_path, off_path = argv[1], argv[2]
    out_path = argv[3] if len(argv) > 3 else "tab_diff.png"

    a = Image.open(on_path).convert("RGB")
    b = Image.open(off_path).convert("RGB")
    if a.size != b.size:
        print("SIZE-MISMATCH %s vs %s" % (a.size, b.size))
        return 1

    pa, pb = a.load(), b.load()
    w, h = a.size
    out = Image.new("RGB", (w, h), (0, 0, 0))
    po = out.load()

    n = 0
    strong = 0
    for y in range(h):
        for x in range(w):
            ra, ga, ba = pa[x, y]
            rb, gb, bb = pb[x, y]
            d = abs(ra - rb) + abs(ga - gb) + abs(ba - bb)
            if not d:
                continue
            n += 1
            if d > 30:
                strong += 1
            v = min(255, d * 4)
            po[x, y] = (v, v, 255 if d > 30 else v // 2)

    out.save(out_path)
    print("on=%s" % on_path)
    print("off=%s" % off_path)
    print("frame=%dx%d differing=%d (%.3f%%) strong(>30)=%d (%.3f%%)"
          % (w, h, n, 100.0 * n / (w * h), strong, 100.0 * strong / (w * h)))
    print("diff map -> %s" % out_path)
    print("verdict rule: a structured isometric cel lattice in the diff map => the overlay IS "
          "composited; a blob only at the character => it is NOT")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
