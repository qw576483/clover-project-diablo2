# -*- coding: utf-8 -*-
"""charstat_sheet.py -- ONE contact sheet for the character-stat panel (U3 complaint #1).

Usage:
    python tools/probes/measure/charstat_sheet.py --before <png> --after <png> --out <png>
        [--readings <u3_charstat_readings_<tag>.tsv>] [--screen <u3_charstat_screen_<tag>.tsv>]
        [--crop x0,y0,x1,y1] [--scale 2]

Why this exists: complaint #1 ("the character panel: the numbers are wrong, the UI display is wrong")
is a DISPLAY complaint, so the verdict needs one picture a reviewer can rule on -- before/after at the
same crop, plus the driver's own field table next to it.  The numbers on this sheet are copied
VERBATIM from the driver's TSVs (no recomputation here), so the picture cannot disagree with the log.

The sheet ALSO overlays the expected value-column centres (from `UI/UiLayoutGame` as measured by the
driver's screen TSV header: `panelBgC=cx,cy ... scale=<canvas px per art px>`), because "the number
sits in the right recess" is the part a reader cannot judge by eye from the art alone.

Everything is echoed into <out>.check.txt so the sheet can be re-derived without guessing.
"""
import argparse
import os
import sys

from PIL import Image, ImageDraw

try:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
except Exception:
    pass

# Expected value-column centre, in original art px relative to the panel centre (art 160), from
# `client/Assets/Scripts/UI/UiLayoutGame.cs` (U3 measured the recesses pixel by pixel):
#   CharStatValueX = 96 - 160 = -64   (four attributes)
#   CharDefValueX  = 290 - 160 = 130  (defense / the two thin bottom rows)
#   CharCurMaxX    = (231+309)/2 - 160 = 110  (stamina/life/mana "cur/max")
COLUMNS = [
    ("attr-values (art 96)", -64.0, (255, 210, 90)),
    ("defense/thin (art 290)", 130.0, (120, 235, 140)),
    ("cur/max (art 270)", 110.0, (120, 190, 255)),
]


def load_tsv(path):
    rows = []
    if not path or not os.path.exists(path):
        return rows
    with open(path, "r", encoding="utf-8", errors="replace") as fh:
        for line in fh:
            line = line.rstrip("\r\n")
            if not line or line.startswith("#"):
                continue
            rows.append(line.split("\t"))
    return rows


def parse_screen_header(path):
    """`# panelBgC=..,.. panelW=.. scale=..` -> (cx, cy, scale) or None."""
    if not path or not os.path.exists(path):
        return None
    with open(path, "r", encoding="utf-8", errors="replace") as fh:
        for line in fh:
            if not line.startswith("#") or "panelBgC=" not in line:
                continue
            cx = cy = sc = None
            for tok in line.replace("#", " ").split():
                if tok.startswith("panelBgC="):
                    a, b = tok.split("=", 1)[1].split(",")[:2]
                    cx, cy = float(a), float(b)
                elif tok.startswith("scale="):
                    sc = float(tok.split("=", 1)[1])
            if cx is not None and sc:
                return cx, cy, sc
    return None


def parse_crop(text):
    if not text:
        return None
    parts = [int(float(x)) for x in text.replace(" ", "").split(",")]
    if len(parts) != 4:
        raise SystemExit("--crop needs 4 comma-separated numbers: x0,y0,x1,y1")
    return tuple(parts)


def crop_img(path, box):
    im = Image.open(path).convert("RGB")
    w, h = im.size
    if box is None:
        return im, (0, 0)
    x0, y0, x1, y1 = box
    x0, y0 = max(0, x0), max(0, y0)
    x1, y1 = min(w, x1), min(h, y1)
    if x1 - x0 < 8 or y1 - y0 < 8:
        raise SystemExit("crop %s is empty inside %s (%dx%d)" % (box, path, w, h))
    return im.crop((x0, y0, x1, y1)), (x0, y0)


def main(argv):
    ap = argparse.ArgumentParser()
    ap.add_argument("--before", required=True)
    ap.add_argument("--after", required=True)
    ap.add_argument("--out", required=True)
    ap.add_argument("--readings", default="")
    ap.add_argument("--screen", default="")
    ap.add_argument("--crop", default="")
    ap.add_argument("--scale", type=int, default=2)
    a = ap.parse_args(argv[1:])

    # Path discipline (team-lead 2026-09-24, trap #1 / rule "every read and write uses an ABSOLUTE
    # path"): Python's `open()`/`Image.open()` follow the PROCESS cwd, which on this box is the
    # WORKSPACE ROOT (`c:\Work\Server\f-v2`) while this script lives under the project -- so a
    # relative `--out .ai-tmp/test/x.png` writes into ANOTHER project's tree **silently** (the
    # tree exists, nothing errors).  Every path is absolutised here and echoed below, so both a
    # wrong-tree run and a wrong-tree write are visible; missing inputs stay fail-loud.
    a.before = os.path.abspath(a.before)
    a.after = os.path.abspath(a.after)
    a.out = os.path.abspath(a.out)
    a.readings = os.path.abspath(a.readings) if a.readings else ""
    a.screen = os.path.abspath(a.screen) if a.screen else ""
    print("resolved cwd=%s" % os.getcwd())
    for _k in ("before", "after", "out", "readings", "screen"):
        print("resolved %s=%s" % (_k, getattr(a, _k)))
    if not a.out.lower().endswith(".png"):
        raise SystemExit("USAGE --out must be a .png path (got %s)" % a.out)

    box = parse_crop(a.crop)
    for p in (a.before, a.after):
        if not os.path.exists(p):
            raise SystemExit("missing input: " + p + "  (cwd=" + os.getcwd() + ")")

    bef, boff = crop_img(a.before, box)
    aft, aoff = crop_img(a.after, box)
    z = max(1, a.scale)
    bef = bef.resize((bef.width * z, bef.height * z), Image.NEAREST)
    aft = aft.resize((aft.width * z, aft.height * z), Image.NEAREST)

    hdr = parse_screen_header(a.screen)
    rows = load_tsv(a.readings)

    LAB_W = 470
    TITLE_H = 74
    head_rows = 3 + (len(COLUMNS) if hdr else 0) + 2 + len(rows)
    h = max(TITLE_H + bef.height + 30, TITLE_H + head_rows * 15 + 30)

    sheet = Image.new("RGB", (bef.width + aft.width + LAB_W + 24, h), (14, 14, 18))
    d = ImageDraw.Draw(sheet)
    d.text((8, 6), "charstat panel -- complaint #1 (%s)" % "on-screen numbers / UI display", fill=(255, 240, 170))
    d.text((8, 22), "BEFORE %s (%d B)   vs   AFTER %s (%d B)"
           % (os.path.basename(a.before), os.path.getsize(a.before),
              os.path.basename(a.after), os.path.getsize(a.after)), fill=(190, 190, 200))
    d.text((8, 38), "crop=%s scale=x%d   (numbers below are copied verbatim from the driver TSVs)"
           % (box, z), fill=(170, 170, 180))
    d.text((8, 54), "expected value columns: " + " | ".join("%s @ art %+g" % (n, x) for n, x, _ in COLUMNS),
           fill=(150, 200, 255))

    y = TITLE_H
    xb = 8
    xa = 8 + bef.width + 12
    d.text((xb, y - 14), "BEFORE", fill=(255, 160, 160))
    d.text((xa, y - 14), "AFTER", fill=(160, 255, 160))
    sheet.paste(bef, (xb, y))
    sheet.paste(aft, (xa, y))

    # overlay the expected columns on BOTH pictures (same crop => same offset)
    if hdr:
        cx, cy, sc = hdr
        for name, rel, col in COLUMNS:
            for base, off in ((xb, boff), (xa, aoff)):
                sx = cx + rel * sc - off[0]
                if 0 <= sx < (bef.width if base == xb else aft.width):
                    px = base + int(sx * z)
                    d.line([(px, y), (px, y + bef.height)], fill=col)
    # legend
    lx = xa + aft.width + 22
    d.text((lx, y - 14), "field table (driver TSV)", fill=(255, 235, 150))
    ty = y
    if rows:
        d.text((lx, ty), "field  | dto | ON SCREEN | official | verdict", fill=(180, 180, 190))
        ty += 14
        for r in rows:
            while len(r) < 6:
                r.append("")
            line = "%s | %s | %s | %s | %s" % (r[0], r[1], r[2], r[3], r[5])
            col = (255, 120, 120) if "MISMATCH" in r[5] else (200, 235, 205)
            d.text((lx, ty), line[:96], fill=col)
            ty += 15
    else:
        d.text((lx, ty), "(no readings tsv given -> picture only)", fill=(200, 200, 200))

    sheet.save(a.out)
    chk = a.out + ".check.txt"
    with open(chk, "w", encoding="utf-8", newline="") as fh:
        fh.write("before=%s bytes=%d\n" % (a.before, os.path.getsize(a.before)))
        fh.write("after=%s bytes=%d\n" % (a.after, os.path.getsize(a.after)))
        fh.write("crop=%s scale=%d\n" % (box, z))
        fh.write("columns=%s\n" % ";".join("%s=%g" % (n, x) for n, x, _ in COLUMNS))
        fh.write("screen_header=%s\n" % (hdr,))
        fh.write("rows=%d\n" % len(rows))
        for r in rows:
            fh.write("row\t" + "\t".join(r) + "\n")
    print("sheet -> %s (%dx%d; before %dx%d, after %dx%d; %d rows)"
          % (a.out, sheet.width, sheet.height, bef.width, bef.height, aft.width, aft.height, len(rows)))
    print("check -> %s" % chk)
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
