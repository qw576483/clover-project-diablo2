# -*- coding: utf-8 -*-
"""d2u26_sheet.py -- offline judge + contact sheet for the U26/U36 draw-order trace.

Input  : the TSV frozen by tools/probes/drivers/d2u26_sortdrive.cs (one row per frame)
         plus the per-frame crop PNGs the driver wrote next to it.
Output : a contact sheet (frame numbers + keys burned in) and an index TSV with the verdicts.

WHY THE VERDICTS LIVE HERE (and not in a model's head):
  skill 1.12 / 2.3 -- judgement belongs to a script; the sheet is what the AI reads once.

WHAT IS JUDGED
  The driver parks the character on the anti-diagonal through Akara (k = 2 / 1 / 0 cells
  offset) and FREEZES Time.timeScale for >= 30 frames per relation.  Two independent rows:
   (a) key row  -- while the primary sort key (sortingOrder) is equal, the z secondary key
       must discriminate.  A frame whose key tuple is fully equal has NO defined order.
   (b) pixel row -- inside the frozen window nothing can change on its own, so the frame
       fingerprint `sig` must be CONSTANT.  More than one distinct sig = the pixels really
       alternate = the user-visible flicker.
  (a) is the root-cause precondition, (b) is the symptom.  A run can pass (a) and fail (b)
  only if the sort rule does not work the way the fix assumes -- that is exactly what the
  pair of rows is there to separate.

Usage: python d2u26_sheet.py <tsv> <cropdir> <out_png> <out_index_tsv>
Exit code: 0 = all judged rows PASS, 1 = at least one FAIL.
"""
import os
import sys

from PIL import Image, ImageDraw

REL_ORDER = ["sameCell", "antiDiag1", "antiDiag2", "other"]
REL_TITLE = {
    "sameCell": "same cell (player stands ON the NPC)",
    "antiDiag1": "adjacent cell, same world y (1 diagonal step)",
    "antiDiag2": "2 cells apart, same world y (2 diagonal steps)",
    "other": "other geometry (not judged; recorded for context)",
}
MIN_FRAMES = 30
COLS = 10
TILE_W, TILE_H, LABEL_H = 170, 128, 30


# Columns are resolved BY NAME from the driver's own header line -- never by hard-coded index.
# Reason: a silent column drift between d2u26_sortdrive.cs and this judge would burn a whole live
# Play session and produce a sheet that looks plausible.  A missing column is a loud FAIL instead.
REQUIRED = ["frame", "hold", "relation", "pOrder", "nOrder", "pz", "nz", "sig", "cropptr", "frozen"]
COLMAP = {}          # column name -> index (filled from the driver's header line)


def load_rows(path):
    rows = []
    with open(path, "r", encoding="utf-8", errors="replace") as fh:
        for ln in fh:
            ln = ln.rstrip("\r\n")
            if not ln:
                continue
            f = ln.split("\t")
            if not COLMAP:
                COLMAP.update({name: i for i, name in enumerate(f)})
                continue
            def cell(name):
                i = COLMAP.get(name)
                return f[i] if (i is not None and i < len(f)) else ""
            try:
                rows.append({
                    "frame": int(cell("frame")), "hold": int(cell("hold") or 0),
                    "rel": cell("relation"),
                    "pgx": int(cell("pgx") or 0), "pgy": int(cell("pgy") or 0),
                    "agx": int(cell("agx") or 0), "agy": int(cell("agy") or 0),
                    "pOrder": int(cell("pOrder")), "nOrder": int(cell("nOrder")),
                    "pz": float(cell("pz")), "nz": float(cell("nz")),
                    "rect": cell("rect"), "sig": cell("sig"), "primary": cell("primary"),
                    "crop": cell("cropptr"), "frozen": cell("frozen") == "1",
                    "note": cell("note"),
                })
            except ValueError:
                continue
    return rows


def header_problems():
    """Every required column must exist in the driver's header (empty list = OK)."""
    return [c for c in REQUIRED if c not in COLMAP]


def front(r):
    """+1 = player in front, -1 = NPC in front, 0 = key tuple fully equal => UNDEFINED."""
    if r["pOrder"] != r["nOrder"]:
        return 1 if r["pOrder"] > r["nOrder"] else -1
    if r["pz"] < r["nz"]:
        return 1
    if r["nz"] < r["pz"]:
        return -1
    return 0


def group(rows):
    out = {}
    for r in rows:
        out.setdefault(r["rel"], []).append(r)
    return out


def judge(g):
    """Per-relation verdict from the frozen rows."""
    frozen = [r for r in g if r["frozen"]]
    primary_equal = [r for r in g if r["pOrder"] == r["nOrder"]]
    undef = [r for r in g if front(r) == 0]
    seq = [front(r) for r in frozen if front(r) != 0]
    flips = sum(1 for i in range(1, len(seq)) if seq[i] != seq[i - 1])
    sigs = []
    for r in frozen:
        if r["sig"] not in sigs:
            sigs.append(r["sig"])
    # Run-length encode the fingerprint sequence: "2 distinct sigs" is NOT the flicker criterion,
    # because the first frozen frame is allowed to differ once (the frame the freeze took effect on
    # still carried the last step of the unfrozen frame).  The user-visible flicker is an
    # ALTERNATION (A-B-A) and/or a change that keeps happening.  Measured in the 12:32 run:
    # sameCell/antiDiag1 = 1 transition on the first frame then 34 identical frames => not flicker.
    runs = []
    for r in frozen:
        if runs and runs[-1][0] == r["sig"]:
            runs[-1][1] += 1
        else:
            runs.append([r["sig"], 1])
    transitions = max(0, len(runs) - 1)
    alternations = 0
    for i in range(2, len(runs)):
        if runs[i][0] == runs[i - 2][0] and runs[i][0] != runs[i - 1][0]:
            alternations += 1
    missing = [r for r in frozen if not os.path.exists(r["crop"])]
    reasons = []
    if len(g) < MIN_FRAMES:
        reasons.append("frames=%d<%d" % (len(g), MIN_FRAMES))
    if len(frozen) < MIN_FRAMES:
        reasons.append("frozenFrames=%d<%d" % (len(frozen), MIN_FRAMES))
    if not primary_equal:
        reasons.append("no frame with an equal primary key (the relation was never actually reached)")
    if undef:
        reasons.append("UNDEFINED-ORDER frames=%d (key tuple fully equal)" % len(undef))
    if flips:
        reasons.append("front flips=%d inside the frozen window" % flips)
    if alternations:
        reasons.append("flicker shape: %d A-B-A alternation(s) inside the frozen window" % alternations)
    if transitions > 1:
        reasons.append("fingerprint changed %d times inside the frozen window (only the settle frame may differ)"
                       % transitions)
    if missing:
        reasons.append("crop PNG missing=%d" % len(missing))
    return {
        "frames": len(g), "frozen": len(frozen), "primary_equal": len(primary_equal),
        "undef": len(undef), "flips": flips, "sigs": sigs, "missing": len(missing),
        "transitions": transitions, "alternations": alternations,
        "rle": " -> ".join("%s x%d" % (s[:6], n) for s, n in runs),
        "front_seq": "".join("+" if v > 0 else "-" for v in seq),
        "verdict": "PASS" if not reasons else "FAIL",
        "reasons": reasons,
    }


def build_sheet(rows, by_rel, out_png, tag):
    judged = [r for r in REL_ORDER if r in by_rel]
    width = COLS * TILE_W
    height = 44 + len(judged) * (TILE_H + LABEL_H) + 30
    sheet = Image.new("RGB", (width, height), (18, 18, 22))
    draw = ImageDraw.Draw(sheet)
    draw.text((8, 6), "d2u26 contact sheet  tag=%s  (frozen windows: pixel differences can only come from the draw order)" % tag,
              fill=(255, 245, 180))
    draw.text((8, 24), "one row per geometry relation; frames are labelled #frame / relation / front(+player,-npc) / order,z",
              fill=(170, 170, 180))
    y = 44
    for rel in judged:
        g = by_rel[rel]
        j = judge(g)
        draw.rectangle([0, y, width, y + LABEL_H - 1], fill=(30, 30, 36))
        draw.text((4, y + 3), "#%s  %s  %s  frames=%d frozen=%d primaryEqual=%d undef=%d flips=%d sigs=%d"
                  % (rel, REL_TITLE.get(rel, rel), j["verdict"], j["frames"], j["frozen"],
                     j["primary_equal"], j["undef"], j["flips"], len(j["sigs"])),
                  fill=(120, 255, 140) if j["verdict"] == "PASS" else (255, 110, 110))
        shot = [r for r in g if r["frozen"]][:COLS] or g[:COLS]
        for i, r in enumerate(shot):
            cx = i * TILE_W
            cy = y + LABEL_H
            if os.path.exists(r["crop"]):
                try:
                    im = Image.open(r["crop"]).convert("RGB")
                    scale = min(float(TILE_W - 4) / im.width, float(TILE_H - 4) / im.height)
                    im = im.resize((max(1, int(im.width * scale)), max(1, int(im.height * scale))),
                                   Image.NEAREST)
                    sheet.paste(im, (cx + 2, cy + 2))
                except Exception as exc:                                   # pragma: no cover
                    draw.text((cx + 4, cy + 8), "tile error %s" % exc, fill=(255, 120, 120))
            else:
                draw.text((cx + 4, cy + 8), "crop MISSING", fill=(255, 120, 120))
            draw.text((cx + 2, cy + TILE_H - 22), "#%d %s front=%s" % (r["frame"], r["rel"], front(r)),
                      fill=(210, 210, 140))
            draw.text((cx + 2, cy + TILE_H - 10), "o=%d/%d z=%s/%s %s" % (
                r["pOrder"], r["nOrder"], r["pz"], r["nz"], r["sig"][:8]), fill=(190, 220, 255))
        y += TILE_H + LABEL_H
    draw.text((8, height - 22), "verdicts are computed by d2u26_sheet.py from the frozen TSV; "
                                "the model only reads this sheet", fill=(170, 170, 180))
    sheet.save(out_png)
    return judged


def main(argv):
    if len(argv) < 5:
        print("usage: d2u26_sheet.py <tsv> <cropdir> <out_png> <out_index_tsv>")
        return 2
    tsv, cropdir, out_png, out_index = argv[1], argv[2], argv[3], argv[4]
    tag = os.path.basename(tsv)
    if not os.path.exists(tsv):
        print("FAIL tsv missing: %s" % tsv)
        return 1
    rows = load_rows(tsv)
    missing_cols = header_problems()
    if missing_cols:
        print("FAIL the driver's header is missing columns this judge needs: %s" % ",".join(missing_cols))
        print("     header=%s" % ",".join(COLMAP.keys()))
        return 1
    if not rows:
        print("FAIL tsv has no parsable rows: %s" % tsv)
        return 1
    by_rel = group(rows)
    judged = build_sheet(rows, by_rel, out_png, tag)

    bad = 0
    print("=== per-relation verdicts (frozen windows only) ===")
    with open(out_index, "w", encoding="utf-8", newline="") as fh:
        fh.write("relation\tframes\tfrozen\tprimary_equal\tundefined\tfront_flips\tdistinct_sigs\t"
                 "missing_crops\ttransitions\talternations\tfingerprint_rle\tfront_sequence\t"
                 "verdict\treasons\n")
        for rel in REL_ORDER:
            if rel not in by_rel:
                if rel != "other":
                    print("%-12s MISSING (relation never recorded)" % rel)
                    fh.write("%s\t0\t0\t0\t0\t0\t0\t0\t\tFAIL\trelation-never-recorded\n" % rel)
                    bad += 1
                continue
            j = judge(by_rel[rel])
            print("%-12s %-4s frames=%-4d frozen=%-4d primaryEqual=%-4d undef=%-3d flips=%-3d "
                  "sigs=%-3d transitions=%-3d alternations=%-3d %s"
                  % (rel, j["verdict"], j["frames"], j["frozen"], j["primary_equal"], j["undef"],
                     j["flips"], len(j["sigs"]), j["transitions"], j["alternations"],
                     ("; ".join(j["reasons"]))[:110]))
            print("             front(+player/-npc) sequence: %s" % j["front_seq"][:120])
            print("             frozen fingerprint rle: %s" % (j["rle"][:150] if j["rle"] else "(none)"))
            fh.write("%s\t%d\t%d\t%d\t%d\t%d\t%d\t%d\t%d\t%d\t%s\t%s\t%s\t%s\n" % (
                rel, j["frames"], j["frozen"], j["primary_equal"], j["undef"], j["flips"],
                len(j["sigs"]), j["missing"], j["transitions"], j["alternations"],
                j["rle"], j["front_seq"], j["verdict"], " ; ".join(j["reasons"])))
            if rel != "other" and j["verdict"] != "PASS":
                bad += 1

    total_frozen = sum(1 for r in rows if r["frozen"])
    print("\nfrozenFrames=%d  rows=%d  sheet=%s  index=%s" % (total_frozen, len(rows), out_png, out_index))
    print("FAIL count = %d" % bad)
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
