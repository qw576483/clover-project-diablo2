# -*- coding: utf-8 -*-
"""Pixel diff for the W1 "same camera / same sprite, two frames" reading.

    python d2tour_diff.py <a.png> <b.png> [--out <diff.png>]

Prints the two frame sizes, the number of pixels differing in any channel, the
largest absolute channel delta and the bounding box of the differences, then a
verdict line. Contract: RESULT OK only when the differing pixel count is 0 and
the sizes match; exit 0 / 1 / 2 (2 = the tool itself could not run).

A non-zero diff is still a *reading* (it is reported with its bbox and an
optional amplified diff image) -- it is never silently rounded to "ok".
"""

import sys

from PIL import Image, ImageChops


def main(argv):
    if len(argv) < 3:
        print(__doc__)
        return 2
    a_path, b_path = argv[1], argv[2]
    out_path = None
    if '--out' in argv:
        out_path = argv[argv.index('--out') + 1]

    a = Image.open(a_path).convert('RGB')
    b = Image.open(b_path).convert('RGB')
    print('A %s %dx%d' % (a_path, a.width, a.height))
    print('B %s %dx%d' % (b_path, b.width, b.height))
    if a.size != b.size:
        print('SIZE-MISMATCH')
        print('RESULT FAIL size-mismatch')
        return 1

    diff = ImageChops.difference(a, b)
    bbox = diff.getbbox()
    grey = diff.convert('L')
    hist = grey.histogram()
    diffpix = sum(hist[1:])
    maxdelta = 0
    for i in range(len(hist) - 1, 0, -1):
        if hist[i]:
            maxdelta = i
            break
    print('DIFF-PIXELS %d of %d (%.6f%%)' % (diffpix, a.width * a.height,
                                             100.0 * diffpix / (a.width * a.height)))
    print('MAX-CHANNEL-DELTA %d' % maxdelta)
    print('DIFF-BBOX %s' % (str(bbox) if bbox else '(none)'))
    if out_path and bbox:
        amp = diff.point(lambda v: min(255, v * 8)).convert('RGB')
        amp.save(out_path)
        print('DIFF-IMAGE %s (8x amplified)' % out_path)
    ok = diffpix == 0
    print('RESULT %s' % ('OK' if ok else 'FAIL %d' % diffpix))
    return 0 if ok else 1


if __name__ == '__main__':
    sys.exit(main(sys.argv))
