# -*- coding: utf-8 -*-
"""Measure the NPC nameplate's black bar width in a screenshot (W9, pixel half).

    python d2tour_barwidth.py <shot.png> [--maxv 20] [--minrun 40]

The bar is drawn as an Image of RGBA(0,0,0,0.95) over the grass, so its rows are
almost black. Counted per row: the longest horizontal run of pixels whose R, G and
B are all <= --maxv. The longest run over the whole image is reported together with
its row and column range; the caller compares it with the canvas-space width that
`EnemyBarView.NameplateSizeFor(text)` returned in the same session.

RESULT OK when a run of at least --minrun pixels exists (a measurement happened);
this tool never decides whether the width is *correct* -- that comparison is the
caller's, and both numbers are printed.
"""

import sys

from PIL import Image


def main(argv):
    if len(argv) < 2:
        print(__doc__)
        return 2
    path = argv[1]
    maxv = int(argv[argv.index('--maxv') + 1]) if '--maxv' in argv else 20
    minrun = int(argv[argv.index('--minrun') + 1]) if '--minrun' in argv else 40
    im = Image.open(path).convert('RGB')
    px = im.load()
    W, H = im.size
    best = (0, -1, -1, -1)
    for y in range(H):
        run = 0
        start = 0
        for x in range(W):
            r, g, b = px[x, y]
            if r <= maxv and g <= maxv and b <= maxv:
                if run == 0:
                    start = x
                run += 1
                if run > best[0]:
                    best = (run, y, start, x)
            else:
                run = 0
    print('IMAGE %s %dx%d maxv=%d' % (path, W, H, maxv))
    print('LONGEST-BLACK-RUN %d px  row=%d  cols=%d..%d' % (best[0], best[1], best[2], best[3]))
    ok = best[0] >= minrun
    print('RESULT %s' % ('OK' if ok else 'FAIL no run >= %d' % minrun))
    return 0 if ok else 1


if __name__ == '__main__':
    sys.exit(main(sys.argv))
