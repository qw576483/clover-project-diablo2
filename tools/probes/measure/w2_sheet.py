# -*- coding: utf-8 -*-
"""w2_sheet.py -- contact sheet(s) + index for the W2 full-system tour.

Usage:
    python tools/probes/measure/w2_sheet.py <shots_dir> <frozen_log> <trace> <out_dir> [tag]

What it does (judgement belongs to a script, not to the model):
  * reads the FROZEN play log (the `[W2] ...` lines the driver printed);
  * takes every `GRID=<id> tile=<file> vals="<raw values>"` line as ONE cell, in log order --
    the values it shows are the driver's raw measured values, copied verbatim;
  * attaches the byte count of that tile from the matching `SHOT-OK ... name=<file> bytes=<n>` line;
  * lays the cells out over two sheets by grid-id prefix (flow / game) and writes the tiles;
  * writes ONE index tsv: grid / measured values / tile / bytes / log line number + verbatim line.

It deliberately writes NO verdict words (no 通过 / 一致 / PASS / FAIL): the index carries measured
values only; the adjudication belongs to the reader.  `w2_sheet_selftest.py` re-checks that.
"""
import os
import re
import sys

from PIL import Image, ImageDraw

try:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
except Exception:
    pass

TILE_W, TILE_H, LABEL_H, COLS = 480, 270, 62, 4
HEADER = 78

GRID_RX = re.compile(r"\[W2\]\s+GRID=(\S+)\s+tile=(\S+)\s+vals=\"(.*)\"\s*$")
SHOT_RX = re.compile(r"\[W2\]\s+SHOT-OK\s+n=(\d+)\s+state=\S+\s+name=(\S+)\s+kind=(\S+)\s+bytes=(\d+)")
CROP_RX = re.compile(r"\[W2\]\s+CROP\s+n=(\d+)\s+tile=(\S+)\s+node=(\S+)\s+sx0=(-?[\d.]+)\s+sy0=(-?[\d.]+)\s+sx1=(-?[\d.]+)\s+sy1=(-?[\d.]+)")

# ---- sheet split: prefix -> sheet name -------------------------------------
FLOW_PREFIXES = ("B", "C5", "G1", "G2", "G3", "H1", "H2", "H3")
GAME_PREFIXES = ("C1", "C2", "C3", "C4", "D1", "D2", "D3", "E1", "E2", "E3", "E4",
                 "F1", "F2", "F3", "G4")

# ---- cells that deserve two columns (bigger pictures) ----------------------
WIDE_IDS = ("C1", "C2", "C3", "G4", "D1-detour-start", "D1-detour-stop", "D2", "D2b")


def load(path):
    if not path or not os.path.exists(path):
        return []
    with open(path, "r", encoding="utf-8", errors="replace") as fh:
        return [ln.rstrip("\r\n") for ln in fh]


def strip_tag(line):
    """`[ts] [Info] [W2] KEY=v` -> `[W2] KEY=v` (strip at most 2 leading tags)."""
    if not line:
        return ""
    s = line.strip()
    for _ in range(2):
        m = re.match(r"^\[[^\]]*\]\s*", s)
        if not m:
            break
        s = s[m.end():]
    return s.strip()


def collect(lines):
    cells = []
    shots = {}
    crops = []
    for i, ln in enumerate(lines, start=1):
        m = SHOT_RX.search(ln)
        if m:
            shots[m.group(2)] = (int(m.group(1)), m.group(3), int(m.group(4)))
            continue
        m = CROP_RX.search(ln)
        if m:
            crops.append(dict(n=int(m.group(1)), tile=m.group(2), node=m.group(3),
                              sx0=float(m.group(4)), sy0=float(m.group(5)),
                              sx1=float(m.group(6)), sy1=float(m.group(7)), line=i))
            continue
        m = GRID_RX.search(ln)
        if m:
            cells.append(dict(grid=m.group(1), tile=m.group(2), vals=m.group(3), line=i,
                              verbatim=strip_tag(ln)))
    for c in cells:
        s = shots.get(c["tile"])
        c["bytes"] = s[2] if s else -1
        c["kind"] = s[1] if s else "-"
    return cells, shots, crops


def crop_for(crops, tile):
    for c in crops:
        if c["tile"] == tile:
            return (c["sx0"], c["sy0"], c["sx1"], c["sy1"])
    return None


def screen_box_to_image(box, w, h, margin=10):
    if box is None:
        return None
    x0 = max(0, int(box[0] - margin))
    x1 = min(w, int(box[2] + margin))
    y0 = max(0, int(h - box[3] - margin))
    y1 = min(h, int(h - box[1] + margin))
    if x1 - x0 < 8 or y1 - y0 < 8:
        return None
    return (x0, y0, x1, y1)


def span_of(cell):
    gid = cell["grid"]
    for w in WIDE_IDS:
        if gid == w or gid.startswith(w + "-"):
            return 2
    return 1


def build_sheet(name, title, cells, shots_dir, crops, trace, out_png):
    placed = []
    x = y = 0
    for c in cells:
        sp = span_of(c)
        if x + sp > COLS:
            x = 0
            y += 1
        placed.append((c, x, y, sp))
        x += sp
    nrows = (y + 1) if placed else 1
    sheet = Image.new("RGB", (COLS * TILE_W, HEADER + nrows * (TILE_H + LABEL_H)), (16, 16, 20))
    draw = ImageDraw.Draw(sheet)
    draw.text((8, 6), "W2 contact sheet [%s] -- %s" % (name, title), fill=(255, 245, 180))
    draw.text((8, 24), "each cell = one GRID line: grid id + tile + the RAW measured values copied "
                       "verbatim out of the frozen log", fill=(170, 170, 180))
    draw.text((8, 42), "index tsv carries grid / values / tile / bytes / log line; no verdict words",
              fill=(170, 170, 180))
    summ = dev = ""
    for ln in trace:
        if "SUMMARY" in ln:
            summ = ln.strip()
        if "DEVICE" in ln and not dev:
            dev = strip_tag(ln)
    draw.text((8, 58), (summ or "(no SUMMARY line in the trace)")[:215], fill=(150, 200, 255))

    for c, cx, cy, sp in placed:
        x0 = cx * TILE_W
        y0 = HEADER + cy * (TILE_H + LABEL_H)
        w = sp * TILE_W
        draw.line([(x0, y0), (x0 + w, y0)], fill=(90, 90, 100))
        draw.line([(x0, y0), (x0, y0 + TILE_H + LABEL_H)], fill=(90, 90, 100))
        draw.rectangle([x0, y0, x0 + w, y0 + LABEL_H - 1], fill=(28, 28, 34))
        draw.text((x0 + 4, y0 + 3), "#%s  %s" % (c["grid"], c["tile"])[:86], fill=(255, 235, 150))
        draw.text((x0 + 4, y0 + 16), ("kind=%s bytes=%s line=%d" % (c["kind"], c["bytes"], c["line"]))[:86],
                  fill=(180, 210, 255))
        draw.text((x0 + 4, y0 + 29), c["vals"][:86], fill=(200, 235, 205))
        draw.text((x0 + 4, y0 + 43), c["vals"][86:172], fill=(200, 235, 205))

        path = os.path.join(shots_dir, c["tile"])
        if not os.path.exists(path):
            draw.text((x0 + 6, y0 + LABEL_H + 10), "tile MISSING: " + c["tile"], fill=(255, 120, 120))
            continue
        try:
            im = Image.open(path).convert("RGB")
            box = screen_box_to_image(crop_for(crops, c["tile"]), im.width, im.height)
            if box is not None:
                im = im.crop(box)
            scale = min(float(w - 4) / im.width, float(TILE_H - 4) / im.height)
            im = im.resize((max(1, int(im.width * scale)), max(1, int(im.height * scale))), Image.NEAREST)
            ox = x0 + (w - im.width) // 2
            oy = y0 + LABEL_H + (TILE_H - im.height) // 2
            sheet.paste(im, (ox, oy))
        except Exception as exc:
            draw.text((x0 + 6, y0 + LABEL_H + 10), "tile error: %s" % exc, fill=(255, 120, 120))

    sheet.save(out_png)
    print("sheet[%s] -> %s (%dx%d, %d cells)" % (name, out_png, sheet.width, sheet.height, len(cells)))


def main(argv):
    if len(argv) < 5:
        print(__doc__)
        return 2
    shots_dir, frozen, trace_path, out_dir = argv[1], argv[2], argv[3], argv[4]
    tag = argv[5] if len(argv) > 5 else "w2"
    lines = load(frozen)
    trace = load(trace_path)
    cells, shots, crops = collect(lines)
    os.makedirs(out_dir, exist_ok=True)

    flow = [c for c in cells if c["grid"].startswith(FLOW_PREFIXES)]
    game = [c for c in cells if c["grid"].startswith(GAME_PREFIXES)]
    other = [c for c in cells if c not in flow and c not in game]
    game = game + other

    build_sheet("flow", "front end + quest/npc/death screens", flow, shots_dir, crops, trace,
                os.path.join(out_dir, "w2_contact_flow.png"))
    build_sheet("game", "world framing + movement + combat + items", game, shots_dir, crops, trace,
                os.path.join(out_dir, "w2_contact_game.png"))

    out_tsv = os.path.join(out_dir, "w2_contact.index.tsv")
    with open(out_tsv, "w", encoding="utf-8", newline="") as fh:
        fh.write("grid\tmeasuring_field_values\tscreenshot\tbytes\tlog_line_no:verbatim\n")
        for c in cells:
            fh.write("\t".join([
                c["grid"],
                c["vals"].replace("\t", " "),
                c["tile"],
                str(c["bytes"]),
                "%d:%s" % (c["line"], c["verbatim"].replace("\t", " ")),
            ]) + "\n")
        fh.write("# cells = one per GRID line (the driver's own measured values); "
                 "no verdict words on purpose -- values only\n")
    print("index -> %s (%d cells, %d tiles with a logged byte count)"
          % (out_tsv, len(cells), sum(1 for c in cells if c["bytes"] > 0)))
    missing = [c["tile"] for c in cells if c["bytes"] < 0]
    if missing:
        print("tiles without a SHOT-OK line: %s" % ",".join(missing))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
