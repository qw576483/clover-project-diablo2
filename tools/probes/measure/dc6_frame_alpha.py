#!/usr/bin/env python3
# dc6_frame_alpha.py -- judge a frame PNG by its BYTES: "does this frame carry the graphic the
# comment/ledger claims?" (opaque-pixel count + alpha bounding box + optional ASCII luminance map).
#
# "the add-point arrow is invisible" as "UiArt.ArrowFrame(0) -- frame 0 IS TRANSPARENT"
# (client/Assets/Scripts/UI/CharacterPanel.cs ... and the same phrase in uicheck/Program.cs L1486).
# Measured against the files on disk that sentence is FALSE:
#   menubutton_0.png = 15x24, opaque = 322 / 360 (only the y=0 row and the x=14 column are
#   transparent), and the luminance map is a real arrow button face (up head rows 1..8,
#   frame 2 has the head at rows 11..19 = the down arrow).
# So the claim "no arrow graphic exists in this batch" is not supported by the asset; whatever is
# wrong on screen is NOT a frame-0-transparency problem. This script is the reusable 量法 for that
# family of question (any exported .DC6 frame), so the next sheet does not have to re-derive it.
#
# Usage (read-only, prints to stdout):
#   python tools/probes/measure/dc6_frame_alpha.py <png> [<png> ...]
#   python tools/probes/measure/dc6_frame_alpha.py --luma <png>
#   python tools/probes/measure/dc6_frame_alpha.py --defaults         # the menubutton 2 copies
# Exit: 0 = all files read; 1 = at least one file missing/unreadable.

import os
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__)))))
DEFAULT_FRAMES = [
    os.path.join(ROOT, "client", "Assets", "Resources", "Clover", "D2", "UI", "Panel", "menubutton_%d.png" % i)
    for i in range(4)
] + [
    os.path.join(ROOT, "client", "Assets", "ThirdParty", "Diablo2", "Images", "ControlPanel",
                 "menubutton__0__%d.png" % i)
    for i in range(4)
]
RAMP = " .:-=+*#%@"


def probe(path, luma):
    from PIL import Image
    im = Image.open(path).convert("RGBA")
    w, h = im.size
    px = im.load()
    opaque = 0
    bbox = None
    hist = {}
    for y in range(h):
        for x in range(w):
            r, g, b, a = px[x, y]
            if a > 0:
                opaque += 1
                key = (r, g, b, a)
                hist[key] = hist.get(key, 0) + 1
                if bbox is None:
                    bbox = [x, y, x, y]
                else:
                    bbox[0] = min(bbox[0], x); bbox[1] = min(bbox[1], y)
                    bbox[2] = max(bbox[2], x); bbox[3] = max(bbox[3], y)
    top = sorted(hist.items(), key=lambda kv: -kv[1])[:4]
    print("%-34s %dx%d opaque=%d/%d bbox=%s top=%s" % (
        os.path.basename(path), w, h, opaque, w * h, bbox, top))
    if luma:
        for y in range(h):
            row = ""
            for x in range(w):
                r, g, b, a = px[x, y]
                if a == 0:
                    row += "x"
                    continue
                l = (r * 299 + g * 587 + b * 114) // 1000
                row += RAMP[min(9, l * 10 // 256)]
            print("%2d|%s|" % (y, row))


def main(argv):
    luma = "--luma" in argv
    args = [a for a in argv[1:] if not a.startswith("--")]
    if "--defaults" in argv or not args:
        args = DEFAULT_FRAMES
    rc = 0
    for p in args:
        if not os.path.exists(p):
            print("MISSING " + p)
            rc = 1
            continue
        try:
            probe(p, luma)
        except Exception as exc:                       # noqa: BLE001 - a broken frame IS the reading
            print("UNREADABLE %s :: %s" % (p, exc))
            rc = 1
    return rc


if __name__ == "__main__":
    sys.exit(main(sys.argv))
