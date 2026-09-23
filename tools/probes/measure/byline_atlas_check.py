# -*- coding: utf-8 -*-
"""Why the brand byline needs the chi tier -- ground-truth check of the ORIGINAL bitmap fonts.

For every candidate tier shipped in the project (latin `font{16,24,30,42}` and chi
`font{N}_chi`) this prints, per character of "by clover-engine":

  * the ink bounding box of the cell the tier actually holds for that character
  * its ink height, and whether it reaches BELOW the baseline of that tier's capital 'A'

A tier that can draw the byline literally needs real lowercase: `b`/`l`/`k`/`h` must have an
ascender, and `g`/`j`/`p`/`q`/`y` must descend below the baseline.  The latin tiers fail both
(their 97..122 cells are reduced-size capitals -- the classic single-case D2 font), the chi tiers
pass.  That is the whole reason `UiArt.Label(..., forceChi: true)` exists for the byline.

Usage: python byline_atlas_check.py
"""
import io
import os
import re
import sys

from PIL import Image

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8')

FONT_DIR = os.path.join(os.path.dirname(os.path.abspath(__file__)),
                        "..", "..", "..", "client", "Assets", "Resources", "Clover", "D2", "Fonts")
FONT_DIR = os.path.normpath(FONT_DIR)
BYLINE = "by clover-engine"
CHI_CELL = 19          # font24_chi grid cell (from font24_chi_map.txt header); per-tier below
CHI_COLS = 117


def load_latin(tier):
    """Return (image, {code: rect}) for a latin tier (sprite per code, name font<N>_<code>)."""
    png = os.path.join(FONT_DIR, tier + ".png")
    meta = os.path.join(FONT_DIR, tier + ".png.meta")
    if not (os.path.exists(png) and os.path.exists(meta)):
        return None, None
    img = Image.open(png).convert("RGBA")
    txt = io.open(meta, encoding="utf-8").read()
    rects = {}
    for m in re.finditer(r"name: %s_(\d+)\s*\n\s*rect:\s*\n\s*serializedVersion: \d+\s*\n"
                         r"\s*x: ([\-\d.]+)\s*\n\s*y: ([\-\d.]+)\s*\n"
                         r"\s*width: ([\-\d.]+)\s*\n\s*height: ([\-\d.]+)" % tier, txt):
        rects[int(m.group(1))] = tuple(int(float(m.group(i))) for i in (2, 3, 4, 5))
    return img, rects


def latin_cell(img, rects, code):
    x, y, w, h = rects[code]
    return img.crop((x, img.height - y - h, x + w, img.height - y))


def load_chi(tier):
    """Return (image, {code: (col,row)}, cell) from font<N>_chi_map.txt."""
    png = os.path.join(FONT_DIR, tier + "_chi.png")
    mp = os.path.join(FONT_DIR, tier + "_chi_map.txt")
    if not (os.path.exists(png) and os.path.exists(mp)):
        return None, None, None
    cell = CHI_CELL
    for line in io.open(mp, encoding="utf-8"):
        m = re.search(r"格子[^0-9]*(\d+)\s*[×x]\s*(\d+)", line)
        if m:
            cell = int(m.group(1))
            break
    img = Image.open(png).convert("RGBA")
    g = {}
    for line in io.open(mp, encoding="utf-8"):
        if line.startswith("#"):
            continue
        f = line.split()
        if len(f) >= 5:
            g[int(f[0])] = (int(f[3]), int(f[4]))
    return img, g, cell


def chi_cell(img, g, cell, code):
    col, row = g[code]
    return img.crop((col * cell, row * cell, col * cell + cell, row * cell + cell))


def report_latin(tier):
    img, rects = load_latin(tier)
    if img is None:
        print("%-10s MISSING" % tier)
        return
    cap = latin_cell(img, rects, ord("A")).getbbox()
    base = cap[3]
    print("--- %s  atlas=%s  sprites=%d  capital A ink bbox=%s (baseline row=%d)"
          % (tier, img.size, len(rects), cap, base))
    bad = []
    for ch in BYLINE:
        if ch == " ":
            continue
        code = ord(ch)
        ink = latin_cell(img, rects, code).getbbox()
        drop = ink[3] - base
        print("    %s(%d) ink_bbox=%-18s ink_h=%2d  below_baseline=%+d"
              % (ch, code, ink, ink[3] - ink[1], drop))
        if drop >= 2:
            bad.append(ch)
    print("    => characters with a real descender: %s" % (bad if bad else "NONE"
          + "  == single-case font: 'by clover-engine' renders as capitals"))
    # the decisive pair check: is the 97..122 cell the same letterform as 65..90?
    same = 0
    for ch in "abcdefghijklmnopqrstuvwxyz":
        u = latin_cell(img, rects, ord(ch.upper())).tobytes()
        l = latin_cell(img, rects, ord(ch)).tobytes()
        if u == l:
            same += 1
    print("    => lowercase cells pixel-identical to the uppercase ones: %d/26" % same)


def report_chi(tier):
    img, g, cell = load_chi(tier)
    if img is None:
        print("%-10s MISSING" % tier)
        return
    cap = chi_cell(img, g, cell, ord("A")).getbbox()
    base = cap[3]
    missing = [c for c in BYLINE if ord(c) not in g]
    print("--- %s_chi  atlas=%s  glyphs=%d  cell=%d  capital A ink bbox=%s (baseline row=%d)"
          % (tier, img.size, len(g), cell, cap, base))
    if missing:
        print("    MISSING GLYPHS for the byline: %s" % missing)
    for ch in BYLINE:
        if ch == " ":
            continue
        code = ord(ch)
        if code not in g:
            print("    %s(%d) NOT IN MAP" % (ch, code))
            continue
        ink = chi_cell(img, g, cell, code).getbbox()
        print("    %s(%d) ink_bbox=%-18s ink_h=%2d  below_baseline=%+d"
              % (ch, code, ink, ink[3] - ink[1], ink[3] - base))


if __name__ == "__main__":
    print("font dir: %s" % FONT_DIR)
    print('byline   : "%s"' % BYLINE)
    print()
    for t in ("font16", "font24", "font30", "font42"):
        report_latin(t)
    print()
    for t in ("font16", "font24", "font30", "font42"):
        report_chi(t)
