# -*- coding: utf-8 -*-
"""x_compare.py -- two-run tile comparison for the X tour (run1 vs run2).

Usage:
    python tools/probes/measure/x_compare.py <run1_dir> <run2_dir> <frozen_log_run1> <out_tsv> [frozen_log_run2]

For every tile it reports, as NUMBERS only (no verdict words):
  * bytes in each run and whether the byte count matches;
  * sha256 in each run and whether it matches (this is the "byte-for-byte identical" claim);
  * for tiles whose panel rectangle was logged by the driver (`[X] CROP ...`), it crops BOTH runs to
    that same rectangle and reports the differing pixel count inside the crop.  This is the
    apples-to-apples number for UI panels that sit on top of a randomly generated world: the world
    behind them differs by design, so a whole-image compare cannot be equal there.

It also copies the `[X] WORLD tag=... seed=...` lines of both frozen logs into extra rows so the
world-seed difference between the two runs is visible in the same table.

Why it is a judgement asset: deleting it makes "the two runs agree" un-recheckable.
"""
import hashlib
import os
import re
import sys

try:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
except Exception:
    pass

from PIL import Image, ImageChops

CROP_RX = re.compile(r"\[X\]\s+CROP\s+n=(\d+)\s+tile=(\S+)\s+node=(\S+)\s+sx0=(-?[\d.]+)\s+sy0=(-?[\d.]+)\s+sx1=(-?[\d.]+)\s+sy1=(-?[\d.]+)")
WORLD_RX = re.compile(r"\[X\]\s+WORLD\s+tag=(\S+)[^\n]*?seed=(-?\d+)[^\n]*?map=(\d+x\d+)")

# panels/interface tiles whose two runs must agree after cropping out the world behind them
KEY_PREFIXES = ("B", "C1", "C2", "C3", "C4", "G1", "G2", "G3", "H1")


def load(path):
    if not path or not os.path.exists(path):
        return []
    with open(path, "r", encoding="utf-8", errors="replace") as fh:
        return [ln.rstrip("\r\n") for ln in fh]


def sha_of(path):
    h = hashlib.sha256()
    with open(path, "rb") as fh:
        for chunk in iter(lambda: fh.read(65536), b""):
            h.update(chunk)
    return h.hexdigest()


def crops(lines):
    out = {}
    for ln in lines:
        m = CROP_RX.search(ln)
        if not m:
            continue
        out[m.group(2)] = (m.group(3), float(m.group(4)), float(m.group(5)), float(m.group(6)), float(m.group(7)))
    return out


def box_to_pil(box, w, h, margin=6):
    x0 = max(0, int(box[0] - margin))
    x1 = min(w, int(box[2] + margin))
    y0 = max(0, int(h - box[3] - margin))
    y1 = min(h, int(h - box[1] + margin))
    if x1 - x0 < 4 or y1 - y0 < 4:
        return None
    return (x0, y0, x1, y1)


def crop_diff(path1, path2, box):
    """(differing pixel count, width x height) of the same crop in both images, or (None, None)."""
    try:
        a = Image.open(path1).convert("RGB")
        b = Image.open(path2).convert("RGB")
    except Exception:
        return None, None
    if a.size != b.size:
        return None, "size %s vs %s" % (a.size, b.size)
    pb = box_to_pil(box, a.width, a.height)
    if pb is None:
        return None, None
    ca = a.crop(pb)
    cb = b.crop(pb)
    diff = ImageChops.difference(ca, cb)
    bb = diff.getbbox()
    n = 0
    if bb is not None:
        hist = diff.convert("L").histogram()
        n = sum(hist[1:])
    return n, "%dx%d" % (ca.width, ca.height)


def main(argv):
    if len(argv) < 5:
        print(__doc__)
        return 2
    d1, d2, log1 = argv[1], argv[2], argv[3]
    out = argv[4]
    log2 = argv[5] if len(argv) > 5 else ""
    lines1 = load(log1)
    cl = crops(lines1)

    tiles = sorted(f for f in os.listdir(d1) if f.lower().endswith(".png"))
    rows = []
    key_unmatched = []
    for t in tiles:
        p1 = os.path.join(d1, t)
        p2 = os.path.join(d2, t)
        b1 = os.path.getsize(p1)
        s1 = sha_of(p1)
        if os.path.exists(p2):
            b2 = os.path.getsize(p2)
            s2 = sha_of(p2)
        else:
            b2, s2 = -1, "-"
        same_bytes = 1 if b1 == b2 else 0
        same_sha = 1 if (s2 == s1) else 0
        node = ""
        cdiff = ""
        cwh = ""
        if t in cl:
            node = cl[t][0]
            n, wh = crop_diff(p1, p2, cl[t][1:]) if os.path.exists(p2) else (None, None)
            cdiff = "" if n is None else str(n)
            cwh = "" if wh is None else wh
        rows.append((t, b1, b2, same_bytes, same_sha, node, cwh, cdiff))
        if same_sha == 0 and t.startswith(KEY_PREFIXES):
            key_unmatched.append((t, node, cdiff))

    world = []
    if log2:
        w1 = {}
        x = {}
        for ln in lines1:
            m = WORLD_RX.search(ln)
            if m:
                w1[m.group(1)] = (m.group(2), m.group(3))
        for ln in load(log2):
            m = WORLD_RX.search(ln)
            if m:
                x[m.group(1)] = (m.group(2), m.group(3))
        for tag in sorted(set(list(w1.keys()) + list(x.keys()))):
            world.append((tag, w1.get(tag), x.get(tag)))

    with open(out, "w", encoding="utf-8", newline="") as fh:
        fh.write("tile\tbytes_run1\tbytes_run2\tsame_bytes\tsame_sha256\tcrop_node\tcrop_wh\tcrop_diff_pixels\n")
        for r in rows:
            fh.write("\t".join(str(x) for x in r) + "\n")
        fh.write("# crop_diff_pixels compares ONLY the rectangle the driver logged for that tile "
                 "(the world behind a UI panel is randomly generated per run, so a whole-image compare "
                 "cannot be equal there)\n")
        if world:
            fh.write("# world seeds per area (from the two frozen logs)\n")
            fh.write("area\tseed_run1_map\tseed_run2_map\n")
            for tag, a, b in world:
                fh.write("%s\t%s\t%s\n" % (tag, a if a else "-", b if b else "-"))

    n_same = sum(1 for r in rows if r[4] == 1)
    print("compared %d tiles: same_sha256=%d differing=%d" % (len(rows), n_same, len(rows) - n_same))
    print("key-prefix tiles whose sha differs: %d" % len(key_unmatched))
    for t, node, cdiff in key_unmatched:
        print("  %-34s crop=%s diff_pixels=%s" % (t, node or "-", cdiff if cdiff != "" else "-"))
    print("compare -> %s" % out)
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
