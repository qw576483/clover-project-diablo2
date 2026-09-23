#!/usr/bin/env python3
"""U46 (S1): "is the automap drawing the RIGHT palette?" - measurable, offline, read-only.

Question (team-lead): the automap is barely visible on screen; is that because we resolve
MaxiMap's cel indices against the WRONG palette (e.g. a terrain slice), or because the
original automap art really is dark line art?

Two ORIGINAL carriers answer it without any product change:

  1. ANCHOR - `MINIMAP/mapicons.DC6` is a *white template* (`.ai-tmp/test/automap-plan.md`
     1.4 (3)). Decoding it with the slice we ship must therefore produce near-white, and its
     frames must use exactly the white index. If that holds, the slice/offset is right.
  2. `AUTOMAP/MaxiMap.dc6` (the automap cel table) - the palette indices it actually uses and
     the luminance of the resulting RGB (weighted by pixel count). That is the colour the
     automap REALLY paints with.

Verdict rule (fixed before running):
  anchor near-white + cels dark  => NOT a palette-offset mistake; the cel art is dark line art
                                    => the original's visibility must come from something else
                                    (needs an original reference we do not have -> report BLOCKED)
  anchor not near-white          => the slice/offset is wrong => wrong palette (generator bug)

usage: python tools/probes/measure/pal_probe.py

DIVISION OF LABOUR (同类量法只留一处权威, 2026-09-23 / 片 S1 归一时写入):
  * `pal_probe.py`  (this file) = the AUTHORITY for "automap 配色出处": it reads the ORIGINAL
    carriers (ACT1/Pal.PL2 slice + AUTOMAP/MaxiMap.dc6 used indices/luminance + the
    MINIMAP/mapicons.DC6 white-template anchor).
  * `w6_automap_palette.py` = DEPRECATED (it asserts the deleted ColBackdrop/ColFloor/ColWallLine
    constants, so it is vacuously green and is NOT a gate). It is kept only because its PNG
    histogram of the 8 mapicon frames is an INDEPENDENT second path that corroborates this file's
    anchor: w6 measured the exported mapicon PNGs' only colour as `#F4F4F4`, and this file gets
    `pal[32] = (244,244,244)` from the PL2 slice => the slice is right.
"""
import collections
import os
import sys

sys.path.insert(0, "tools")
from d2codec import dc6 as dc6mod  # noqa: E402

PAL = "原版资源/d2raw/data/global/palette/ACT1/Pal.PL2"
MAXI = "原版资源/d2dc6/data/global/ui/AUTOMAP/MaxiMap.dc6"
ICONS = "原版资源/d2dc6/data/global/ui/MINIMAP/mapicons.DC6"

PALETTE = None


def lum(c):
    return 0.299 * c[0] + 0.587 * c[1] + 0.114 * c[2]


def hist(path, label, limit=10 ** 9):
    d = dc6mod.parse(open(path, "rb").read())
    frames = getattr(d, "frames", None)
    if frames is None:
        print("%s: unexpected Dc6 shape: %r" % (label, [a for a in dir(d) if not a.startswith("_")]))
        return
    h = collections.Counter()
    for i, f in enumerate(frames):
        if i >= limit:
            break
        for v in f.indices:
            if v:
                h[v] += 1
    tot = sum(h.values())
    print("%s frames=%d used_indices=%d used_pixels=%d" % (label, len(frames), len(h), tot))
    print("   index list = %s" % sorted(h.keys())[:24])
    for idx, n in h.most_common(10):
        c = PALETTE[idx]
        print("   idx=%4d n=%7d rgb=(%3d,%3d,%3d) lum=%5.1f" % (idx, n, c[0], c[1], c[2], lum(c)))
    if tot:
        wm = sum(lum(PALETTE[i]) * n for i, n in h.items()) / tot
        print("   weighted-mean lum = %5.1f / 255   min=%5.1f  max=%5.1f"
              % (wm, min(lum(PALETTE[i]) for i in h), max(lum(PALETTE[i]) for i in h)))


def main():
    global PALETTE
    PALETTE = dc6mod.read_pl2(PAL)
    print("PL2 file = %s (%d bytes)" % (PAL, os.path.getsize(PAL)))
    print("slice we use: pal[0]=%s pal[1]=%s pal[32]=%s pal[33]=%s"
          % (PALETTE[0], PALETTE[1], PALETTE[32], PALETTE[33]))
    print("ANCHOR: pal[32] lum=%.1f (mapicons.DC6 white template must land on it)"
          % lum(PALETTE[32]))
    hist(ICONS, "mapicons(marker)")
    hist(MAXI, "MaxiMap(automap cels)", limit=1260)
    return 0


if __name__ == "__main__":
    sys.exit(main())
