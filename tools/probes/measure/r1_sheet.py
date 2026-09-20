# -*- coding: utf-8 -*-
"""r1_sheet.py -- contact sheet + index for the R1 batch (A1..A16) plus the R1-F cell (A17).

Usage:
    python tools/probes/measure/r1_sheet.py <shots_dir> <out_png> <out_tsv> <frozen_log> <trace>

A1..A16 come from the R1 batch (one Play session); A17 is the R1-F re-capture of the same name-field
measurement after the default-name fix, so the sheet is built from a CONCATENATION of the two frozen
logs -- R1 batch FIRST (the driver script does that concatenation; see r1f_run.ps1).  The two name
lines use DISTINCT keys (`NAME where=after-typing` vs `NAME where=after-typing-fixed`) so that the A8
and A17 cells cannot cross-read each other.

What it does (judgement belongs to a script, not to the model):
  * reads the FROZEN play log (the `[R1] KEY=value` lines the driver printed) and picks, for every
    grid, the raw measured values with a regex;
  * checks that the log lines that carry a grid are present (counts the missing ones -- a fact, so
    the reader can see which grid has no log backing at all);
  * crops zoom windows for the grids whose criterion is a picture detail (char-create portraits,
    the name field, the shop title/hint band) using the `CROP` lines the driver emitted;
  * writes ONE contact sheet (one cell per tile, grid id + measured values burned in) and the
    index tsv (grid / measured values / screenshot / log lines).

It deliberately writes NO verdict words (no pass/fail/consistent): the index carries measured
values only; the adjudication is the main agent's.
"""
import os
import re
import sys

from PIL import Image, ImageDraw

sys.stdout.reconfigure(encoding="utf-8", errors="replace") if hasattr(sys.stdout, "reconfigure") else None

TILE_W, TILE_H, LABEL_H, COLS = 480, 270, 58, 4
HEADER = 74


KEY_EQ = re.compile(r"(\[R1\] )([A-Z][A-Z0-9\-]*)=")


def normalize(line):
    """`[R1] OSIZE=rigValue=3.75` -> `[R1] OSIZE rigValue=3.75`.

    The driver publishes one `[R1] KEY=value` line per probe, while the patterns below are
    written in the `NAME rest=...` form -- the KEY '=' is turned into a space here, once,
    instead of writing 40 near-identical regexes.  Lines that are already space-form
    (`[R1] VERDICT ok=1`, `[R1] CHK name=... ok=1`, `[R1] SHOT-OK n=1 ...`) do not match
    (their KEY is followed by a space + a letter, not by '=') and stay untouched.
    """
    return KEY_EQ.sub(r"\1\2 ", line)


def load(path):
    if not path or not os.path.exists(path):
        return []
    with open(path, "r", encoding="utf-8", errors="replace") as fh:
        return [normalize(ln.rstrip("\r\n")) for ln in fh]


def find_first(lines, pat):
    rx = re.compile(pat, re.S)
    for i, ln in enumerate(lines):
        m = rx.search(ln)
        if m:
            return m, i + 1, ln
    return None, 0, ""


def find_all(lines, pat):
    rx = re.compile(pat, re.S)
    out = []
    for i, ln in enumerate(lines):
        m = rx.search(ln)
        if m:
            out.append((m, i + 1, ln))
    return out


def strip_tag(line):
    """`[2026-..] [Info] [R1] KEY=value` -> `KEY=value` (strip at most 3 leading tags).

    The payload itself may contain `] ` (option lists!), so this must NOT walk to the last bracket.
    """
    if not line:
        return ""
    s = line.strip()
    for _ in range(3):
        m = re.match(r"^\[[^\]]*\]\s*", s)
        if not m:
            break
        s = s[m.end():]
    return s.strip()


def crop_of(lines, tile, extra=None):
    """Look up the driver's CROP rect for a tile (screen px, bottom-left origin)."""
    pat = r"CROP n=(\d+) tile=" + re.escape(tile) + \
          r" (?:\S+ )?sx0=(-?[\d.]+) sy0=(-?[\d.]+) sx1=(-?[\d.]+) sy1=(-?[\d.]+)"
    if extra is None:
        m, _n, _l = find_first(lines, pat)
        if not m:
            return None
        return (float(m.group(2)), float(m.group(3)), float(m.group(4)), float(m.group(5)))
    boxes = []
    for want in extra:
        m, _n, _l = find_first(lines, r"CROP n=" + str(want) + r" [^\n]*?sx0=(-?[\d.]+) sy0=(-?[\d.]+) sx1=(-?[\d.]+) sy1=(-?[\d.]+)")
        if m:
            boxes.append((float(m.group(1)), float(m.group(2)), float(m.group(3)), float(m.group(4))))
    if not boxes:
        return None
    return (min(b[0] for b in boxes), min(b[1] for b in boxes),
            max(b[2] for b in boxes), max(b[3] for b in boxes))


def screen_box_to_image(box, w, h, margin=8):
    """screen px (bottom-left) -> PIL box (top-left)."""
    if box is None:
        return None
    x0 = max(0, int(box[0] - margin))
    x1 = min(w, int(box[2] + margin))
    y0 = max(0, int(h - box[3] - margin))
    y1 = min(h, int(h - box[1] + margin))
    if x1 - x0 < 8 or y1 - y0 < 8:
        return None
    return (x0, y0, x1, y1)


# ---------------------------------------------------------------------------
# grids: id / title / tile(s) / span / measured fields (label, regex) / log lines that must exist
# ---------------------------------------------------------------------------
def rows():
    r = []

    def add(grid, title, tiles, span, fields, pats, crop_extra=None, crop_tile=None, per_tile_pat=None,
            all_pat=None, crop_from_name=False, crop_name_where=None):
        r.append(dict(grid=grid, title=title, tiles=tiles, span=span, fields=fields,
                      pats=pats, crop_extra=crop_extra, crop_tile=crop_tile, per_tile_pat=per_tile_pat,
                      all_pat=all_pat, crop_from_name=crop_from_name,
                      crop_name_where=crop_name_where))

    # ---- A1 whole level, probe ortho ~28 --------------------------------------
    add("A1", "rogue encampment: whole level (probe ortho 28)", ["a01_town_wide.png"], 2,
        [("rigOrtho", r"WIDE-READY rigOrtho=([\d.]+)"),
         ("camOrtho", r"WIDE-READY .*camOrtho=([\d.]+)"),
         ("camPos", r"WIDE-READY .*camPos=(\([-\d.,]+\))"),
         ("visibleH", r"WIDE-READY .*visibleWorldH=([\d.]+)"),
         ("map", r"TOWN tag=a1 .*map=(\d+x\d+)"),
         ("restoredRigOrtho", r"CAM-RESTORED rigOrtho=([\d.]+)")],
        [r"WIDE requested ortho=28 .*rigSetOk=1",
         r"SHOT-OK n=10 state=a1-town-wide name=a01_town_wide\.png kind=camera"])

    # ---- A2 default framing at the bridge -------------------------------------
    add("A2", "town: default framing at bridge+river", ["a02_town_bridge.png"], 2,
        [("rigOrtho", r"OSIZE tag=a2 rigValue=([\d.]+)"),
         ("camOrtho", r"OSIZE tag=a2 .*camValue=([\d.]+)"),
         ("pxPerWorldUnit", r"OSIZE tag=a2 .*pxPerWorldUnit=([\d.]+)"),
         ("standCell", r"HOP why=a2-west-bank to=(\(\d+,\d+\))"),
         ("bridgeCell", r"CELLSCREEN tag=a2.*?bridge\((\d+,\d+)\)=(-?[\d.]+@-?[\d.]+)"),
         ("waterCell", r"CELLSCREEN tag=a2.*?water\(47,29\)=(-?[\d.]+@-?[\d.]+)"),
         ("bridgeCell2", r"CELLSCREEN tag=a2.*?bridge\(\d+,\d+\)=\S+ bridge\((\d+,\d+)\)=(-?[\d.]+@-?[\d.]+)")],
        [r"HOP why=a2-west-bank to=\(\d+,\d+\) walkable=1",
         r"SHOT-OK n=11 state=a2-town-bridge name=a02_town_bridge\.png kind=camera"])

    # ---- A3 click the bridge deck --------------------------------------------
    add("A3", "click the bridge deck -> walks on", ["a03a_bridge_click_050.png", "a03b_bridge_click_150.png"], 1,
        [("setup", r"A3-SETUP stand=(\(\d+,\d+\)) bridgeTarget=(\(\d+,\d+\)) pathCells=(\d+)"),
         ("clickWant", r"CK-BEGIN want=(\(\d+,\d+\)) tileKind=(\w+) walkable=(\d)"),
         ("moveCmdDelta", r"CK-DONE want=\(\d+,\d+\).*?moveCmdDelta=(-?\d+)"),
         ("t0500", r"A3-T0500 playerGrid=(\(\d+,\d+\)) .*?moving=(\d)"),
         ("t1500", r"A3-T1500 playerGrid=(\(\d+,\d+\)) .*?moving=(\d)"),
         ("arrived", r"A3-ARRIVED playerGrid=(\(\d+,\d+\)) .*?isBridgeDeck=(\d)"),
         ("moveLine", r"\[Move\] steps=(\d+) path=\S+ from=(\(\d+,\d+\)) to=(\(\d+,\d+\))")],
        [r"CK-PROJ want=(\(\d+,\d+\)) got=\1 ok=1",
         r"CK-DONE want=\(\d+,\d+\) walkable=1 tileKind=Dirt",
         r"SHOT-OK n=12 state=a3-t0\.5 name=a03a_bridge_click_050\.png kind=camera",
         r"SHOT-OK n=13 state=a3-t1\.5 name=a03b_bridge_click_150\.png kind=camera",
         r"A3-ARRIVED .*isBridgeDeck=1"])

    # ---- A4 click the water -> nearest walkable ------------------------------
    add("A4", "click water -> nearest walkable (R1-B)", ["a04_water_fallback.png"], 1,
        [("clickWant", r"A4-PICK waterCell=(\(\d+,\d+\)) tileKind=(\w+) walkable=(\d)"),
         ("moveCmdDelta", r"CK-DONE want=\(\d+,\d+\).*?moveCmdDelta=(-?\d+)"),
         ("fallbackLog", r"\[R1-B\] .*?点击 \((\d+),(\d+)\) 地形=(\w+) ⇒ 落到 \((\d+),(\d+)\) 地形=(\w+)，路径 (\d+) 格"),
         ("arrived", r"A4-ARRIVED playerGrid=(\(\d+,\d+\)) tileKindHere=(\w+) landedOnWalkable=(\d)")],
        [r"A4-PICK waterCell=\(\d+,\d+\) tileKind=\w+ walkable=0",
         r"A4-PICK .*expectedLanding=\(\d+,\d+\)",
         r"\[R1-B\] 点到不可走格",
         r"SHOT-OK n=14 state=a4-water-fallback name=a04_water_fallback\.png kind=camera"])

    # ---- A5/A6/A7 char create (crop = the live portrait rectangle) -----------
    add("A5", "char-create: both portraits untouched (NU1 baseline)",
        ["a05a_amazon_idle.png", "a05b_barbarian_idle.png"], 1,
        [("amazon", r"PORTRAIT n=1 which=amazon-idle-baseline .*?sprite=(\S+) native=(\d+x\d+) rect=([\d.]+x[\d.]+)"),
         ("amazonRatios", r"PORTRAIT n=1 .*?nativeAspect=([\d.]+) rectAspect=([\d.]+) ratioW=([\d.]+)"),
         ("barbarian", r"PORTRAIT n=2 which=barbarian-idle-baseline .*?sprite=(\S+) native=(\d+x\d+) rect=([\d.]+x[\d.]+)"),
         ("barbarianRatios", r"PORTRAIT n=2 .*?nativeAspect=([\d.]+) rectAspect=([\d.]+) ratioW=([\d.]+)")],
        [r"CC-READY classIndex=-1", r"SHOT-OK n=1 state=a5a", r"SHOT-OK n=2 state=a5b"],
        crop_tile=True)

    add("A6", "char-create: transition mid-frame (per-frame rectangle)",
        ["a06a_amazon_transition.png", "a06b_barbarian_transition.png"], 1,
        [("amazonFrame", r"PORTRAIT n=3 which=amazon-transition-mid .*?frame=(\d+/\d+) .*?sprite=(\S+) native=(\d+x\d+) rect=([\d.]+x[\d.]+)"),
         ("amazonRatios", r"PORTRAIT n=3 .*?nativeAspect=([\d.]+) rectAspect=([\d.]+) ratioW=([\d.]+) ratioH=([\d.]+)"),
         ("barbFrame", r"PORTRAIT n=5 which=barbarian-transition-mid .*?frame=(\d+/\d+) .*?sprite=(\S+) native=(\d+x\d+) rect=([\d.]+x[\d.]+)"),
         ("barbRatios", r"PORTRAIT n=5 .*?nativeAspect=([\d.]+) rectAspect=([\d.]+) ratioW=([\d.]+) ratioH=([\d.]+)"),
         ("pframes", r"PFRAME slot=(\d+) frame=(\d+) sprite=(\S+) native=(\d+x\d+)"),
         ("pframeRatios", r"PFRAME slot=0 frame=\d+ .*?ratioW=([\d.]+) ratioH=([\d.]+) rectAspect=([\d.]+) nativeAspect=([\d.]+)")],
        [r"CC-CLICK target=SpotAmazon \(real mouse\)",
         r"CC-CLICK target=SpotBarbarian \(real mouse\)",
         r"SHOT-OK n=3 state=a6a", r"SHOT-OK n=5 state=a6b"],
        crop_tile=True)

    add("A7", "char-create: transition finished (NU3 front idle)",
        ["a07a_amazon_front.png", "a07b_barbarian_front.png"], 1,
        [("amazon", r"PORTRAIT n=4 which=amazon-front-end .*?shownState=(\d+) .*?sprite=(\S+) native=(\d+x\d+) rect=([\d.]+x[\d.]+)"),
         ("amazonRatios", r"PORTRAIT n=4 .*?nativeAspect=([\d.]+) rectAspect=([\d.]+) ratioW=([\d.]+)"),
         ("barbarian", r"PORTRAIT n=6 which=barbarian-front-end .*?shownState=(\d+) .*?sprite=(\S+) native=(\d+x\d+) rect=([\d.]+x[\d.]+)"),
         ("barbRatios", r"PORTRAIT n=6 .*?nativeAspect=([\d.]+) rectAspect=([\d.]+) ratioW=([\d.]+)"),
         ("transEnd", r"CC-TRANS-END (amazon|barbarian) after=(.*)$")],
        [r"CC-TRANS-END amazon after=.*transSlot=-1",
         r"CC-TRANS-END barbarian after=.*transSlot=-1",
         r"SHOT-OK n=4 state=a7a", r"SHOT-OK n=6 state=a7b"],
        crop_tile=True)

    # ---- A8 name field with digits ------------------------------------------
    #   NOTE: A8 is the R1-batch cell and shows the state BEFORE the R1-F default-name fix
    #   (visible text "HeroAma65x|").  Its tile file and its values both come from the R1 batch, so
    #   the title says so explicitly -- the AFTER state is A17 (below).
    add("A8", "char-create: name field typed a digit-bearing string (R1 batch, BEFORE the R1-F default-name fix)",
        ["a08_name_digits.png"], 1,
        [("typed", r"TYPE-STR typed=\"([^\"]*)\" .*?digitsPresent=(\d)"),
         ("buffer", r"NAME where=after-typing .*?buffer=\"([^\"]*)\""),
         ("digitsInBuffer", r"NAME where=after-typing .*?digitsInBuffer=(\d)"),
         ("visibleInput", r"NAME where=after-typing .*?visible_input=\"([^\"]*)\""),
         ("visibleLabel", r"NAME where=after-typing .*?visible_label=\"([^\"]*)\""),
         ("display", r"NAME where=after-typing .*?display=\"([^\"]*)\""),
         ("caret", r"NAME where=after-typing .*?caret=(\d+)"),
         ("typeQueued", r"TYPE char\[(\d)\]='(\w)' queued=(\d)")],
        [r"TYPE-PICK candidate=", r"TYPE char\[0\]='\w' queued=1", r"TYPE char\[4\]='\w' queued=1",
         r"NAME where=after-typing .*?digitsInBuffer=1",
         r"SHOT-OK n=7 state=a8"],
        crop_tile=True, crop_from_name=True)

    # ---- A9 walk trace -------------------------------------------------------
    add("A9", "walk trace: 6 frames of one continuous walk",
        ["a09_walk_%d.png" % i for i in range(1, 7)], 1,
        [("spot", r"A9-SPOT from=(\(\d+,\d+\)) to=(\(\d+,\d+\)) cells=(\d+) dir=(\w+)"),
         ("speed", r"A9-SPOT .*moveSpeed=(\S+) expectedCellsPerSec=(\S+)")],
        [r"A9-SPOT from=\(\d+,\d+\) to=\(\d+,\d+\) cells=\d+",
         r"A9-DONE shots=6",
         r"SHOT-OK n=2[1-6] state=a9-frame"],
        crop_tile=False,
        per_tile_pat=r"A9 n=%d frame=\d+ .*?cell=(\([-\d.,]+\)) dWorldFromPrev=([\d.]+) dCellFromPrev=([-\d.]+) moving=(\d)",
        all_pat=r"A9 n=\d+ frame=\d+ .*?dWorldFromPrev=[\d.]+ dCellFromPrev=[-\d.]+ moving=\d")

    # ---- A10 dialog (not started) -------------------------------------------
    add("A10", "Akara dialog, quest not started", ["a10_dialog_not_started.png"], 2,
        [("spot", r"A10-SPOT akara=(\(\d+,\d+\)) standAt=(\(\d+,\d+\)) dist=([\d.]+)"),
         ("click", r"A10-CLICKRESULT result=(\S+) akara=(\(\d+,\d+\)) moveCmdDelta=(-?\d+)"),
         ("dialog", r"DIALOG tag=a10 open=1 .*?options=(\[[^\]]*\])"),
         ("dialogFlags", r"DIALOG tag=a10 .*?canAcceptQuest=(\w+) canTurnInQuest=(\w+)"),
         ("dialogNpc", r"DIALOG tag=a10 .*?npcName=\"([^\"]*)\" canAcceptQuest=(\w+)"),
         ("npcs", r"NPCS count=(\d+) currentNpcId=(-?\d+)")],
        [r"A10-SPOT akara=\(41,19\)", r"DIALOG tag=a10 open=1",
         r"SHOT-OK n=15 state=a10"])
    # ---- A11 accept the quest -----------------------------------------------
    add("A11", "accept quest via the dialog button -> dialog refreshes", ["a11_dialog_in_progress.png"], 2,
        [("clickIndex", r"UI-OPTION tag=a11 index=(\d+) node=(\S+)"),
         ("optionsBefore", r"UI-OPTION tag=a11 .*?(options=\[[^\]]*\])"),
         ("quest", r"QUEST tag=a11-after-accept denState=(\w+) denRemaining=(-?\d+)"),
         ("dialog", r"DIALOG tag=a11 open=1 .*?options=(\[[^\]]*\])"),
         ("state", r"A11-STATE moveCmds=(\d+) playerGrid=(\(\d+,\d+\))")],
        [r"UI-OPTION tag=a11 index=1", r"CLICKTOP tag=a11 index=1",
         r"QUEST tag=a11-after-accept denState=InProgress", r"SHOT-OK n=16 state=a11"])

    # ---- A12 shop + dialog bar ----------------------------------------------
    add("A12", "click trade -> shop open AND dialog bar still there", ["a12_shop_and_dialog.png"], 2,
        [("clickIndex", r"UI-OPTION tag=a12 index=(\d+) node=(\S+)"),
         ("state", r"A12-STATE uiRetried=(\d) shopOpen=(\d) dialogOpen=(\d) shopLayer=(\w+) dialogLayer=(\w+)"),
         ("shop", r"SHOP tag=a12 open=1 layer=(\w+) title=\"([^\"]*)\" hint=\"([^\"]*)\"")],
        [r"UI-OPTION tag=a12 index=\d", r"A12-STATE .*?shopOpen=1 .*?dialogOpen=1",
         r"SHOT-OK n=17 state=a12"])

    # ---- A13 shop closed, dialog survives ------------------------------------
    add("A13", "close the shop -> dialog bar survives", ["a13_shop_closed.png"], 2,
        [("state", r"A13-STATE uiRetried=(\d) shopOpen=(\d) dialogOpen=(\d) currentNpcId=(-?\d+)"),
         ("dialog", r"DIALOG tag=a13 open=1 .*?options=(\[[^\]]*\])"),
         ("quest", r"DIALOG tag=a13 .*?denState=(\w+)")],
        [r"A13-CLICK clicking the shop Close button", r"A13-STATE .*?shopOpen=0 .*?dialogOpen=1",
         r"SHOT-OK n=19 state=a13"])

    # ---- A14 dialog button click moves nobody -------------------------------
    add("A14", "dialog-panel button click -> 0 MoveCommand", ["a14_dialog_button_pre.png"], 2,
        [("clickTop", r"CLICKTOP tag=a14 index=(\d+) node=(\S+) screen=(\([-\d.,]+\)) .*?raycastTop=\"([^\"]*)\""),
         ("result", r"A14-RESULT uiRetried=(\d) moveCmdsBefore=(\d+) moveCmdsAfter=(\d+) moveCmdDelta=(-?\d+) .*?gridChanged=(\d)"),
         ("dialog", r"A14-PRECLICK .*?dialogOpen=(\d)")],
        # `dialogOpen=0` in the RESULT line is what makes the A14 claim self-checking: the dialog
        # only closes if the click was really delivered to the panel button, so
        # (moveCmdDelta=0 AND dialogOpen=0) together mean "delivered AND produced no MoveCommand".
        [r"CLICKTOP tag=a14 index=0", r"A14-RESULT .*?moveCmdDelta=0 .*?gridChanged=0 .*?dialogOpen=0",
         r"SHOT-OK n=20 state=a14"])

    # ---- A15 shop title + hint ----------------------------------------------
    add("A15", "shop title line + hint line (zoom of both)", ["a15_shop_title_hint.png"], 2,
        [("title", r"SHOP tag=a15 open=1 layer=\S+ title=\"([^\"]*)\" hint=\"([^\"]*)\""),
         ("labelRects", r"SHOP tag=a15 .*?(titleScreen=[^ ]+ titleRect=\S+) .*?(hintScreen=[^ ]+ hintRect=\S+)"),
         ("table", r"SHOPYTABLE tag=a15 shopTitlePosY=(\S+ \S+) shopHintPosY=(\S+ \S+)")],
        [r"SHOP tag=a15 open=1", r"CROP n=90 tile=a15", r"CROP n=91 tile=a15",
         r"SHOT-OK n=18 state=a15"],
        crop_extra=[90, 91], crop_tile=True)

    # ---- A16 quest log (Q) --------------------------------------------------
    add("A16", "quest log panel (Q)", ["a16_questlog.png"], 2,
        [("state", r"A16-STATE questLogOpen=(\d)"),
         ("quest", r"QUEST tag=a16-questlog denState=(\w+) denRemaining=(-?\d+) canTurnInDen=(\d)")],
        [r"A16-STATE questLogOpen=1", r"SHOT-OK n=21 state=a16"])

    # ---- A17 ★ R1-F: the same name field AFTER the default-name fix ----------
    #   Measured values only (no verdict).  `glued` = 0 means the typed string IS the whole buffer,
    #   i.e. the prefilled default name is gone; `defaultInBuffer` = 0 says the default does not occur
    #   anywhere inside it.  A8 (above) carries the PRE-fix value of the same measurement, so the two
    #   cells can be read side by side.
    add("A17", "char-create: name field AFTER the R1-F default-name fix (default name no longer glued)",
        ["a17_name_fixed.png"], 2,
        [("default", r"NAME-COMPARE defaultName=\"([^\"]*)\""),
         ("typed", r"NAME-COMPARE .*?typed=\"([^\"]*)\""),
         ("buffer", r"NAME-COMPARE .*?buffer=\"([^\"]*)\""),
         ("visible", r"NAME-COMPARE .*?visible=\"([^\"]*)\""),
         ("defaultInBuffer", r"NAME-COMPARE .*?defaultInBuffer=(\d)"),
         ("glued", r"NAME-COMPARE .*?glued=(\d)"),
         ("visibleInput", r"NAME where=after-typing-fixed .*?visible_input=\"([^\"]*)\""),
         ("caret", r"NAME where=after-typing-fixed .*?caret=(\d+)")],
        [r"NAME-COMPARE ", r"NAME where=after-typing-fixed", r"SHOT-OK n=\d+ state=a17"],
        crop_from_name=True, crop_name_where="after-typing-fixed")

    return r


def measure(lines, row):
    """(values_text, log_lines, missing) for one grid."""
    vals = []
    logs = []
    for label, pat in row["fields"]:
        m, ln, raw = find_first(lines, pat)
        if not m:
            vals.append(label + "=(miss)")
            continue
        got = " ".join(g for g in m.groups() if g is not None)
        vals.append(label + "=" + got)
        if ln and (ln, strip_tag(raw)) not in logs:
            logs.append((ln, strip_tag(raw)))
    if row.get("all_pat"):
        for m, ln, raw in find_all(lines, row["all_pat"]):
            if (ln, strip_tag(raw)) not in logs:
                logs.append((ln, strip_tag(raw)))
    missing = []
    for p in row["pats"]:
        m, _ln, _raw = find_first(lines, p)
        if not m:
            missing.append(p)
    return vals, logs, missing


def build(lines, shots, out_png, out_tsv, trace):
    rows_ = rows()
    cells = []
    for row in rows_:
        vals, logs, missing = measure(lines, row)
        crop_box = None
        if row.get("crop_from_name"):
            # Which `NAME where=...` line to crop from.  A8 crops from the R1-batch line
            # (`after-typing`), A17 from the R1-F line (`after-typing-fixed`) -- two DISTINCT keys, so
            # find_first() cannot cross them even though both logs live in the concatenated file.
            where = row.get("crop_name_where") or "after-typing"
            m, _ln, _raw = find_first(lines, r"NAME where=" + re.escape(where)
                                      + r" .*?screen=(-?[\d.]+),(-?[\d.]+) rect=([\d.]+)x([\d.]+)")
            if m:
                cx, cy, w, h = (float(m.group(1)), float(m.group(2)),
                                float(m.group(3)), float(m.group(4)))
                crop_box = (cx - w / 2, cy - h / 2, cx + w / 2, cy + h / 2)
        elif row.get("crop_tile"):
            key_tile = row["tiles"][0]
            crop_box = crop_of(lines, key_tile, row.get("crop_extra"))
        for k, tile in enumerate(row["tiles"]):
            box = crop_box
            if row.get("crop_tile") and k > 0 and not row.get("crop_extra"):
                box = crop_of(lines, tile)
            v = vals
            if row.get("per_tile_pat"):
                m, _ln, _raw = find_first(lines, row["per_tile_pat"] % (k + 1))
                got = " ".join(g for g in m.groups() if g is not None) if m else "(miss)"
                v = ["sample%d=%s" % (k + 1, got)] + vals[:1]
            cells.append(dict(row=row, tile=tile, vals=v, logs=logs, missing=missing, box=box))

    # ---- packing: spans in a 4-unit-wide grid ---------------------------------
    placed = []
    x = 0
    y = 0
    for c in cells:
        span = 1
        if len(c["row"]["tiles"]) == 1:
            span = c["row"]["span"]
        if x + span > COLS:
            x = 0
            y += 1
        placed.append((c, x, y, span))
        x += span
    nrows = y + 1
    sheet = Image.new("RGB", (COLS * TILE_W, HEADER + nrows * (TILE_H + LABEL_H)), (16, 16, 20))
    draw = ImageDraw.Draw(sheet)
    draw.text((8, 6), "R1 contact sheet -- A1..A17 (R1 five fixes + R1-F default-name fix)",
              fill=(255, 245, 180))
    draw.text((8, 24), "cells carry the grid id, the tile and the RAW measured values read out of the "
                       "frozen log; A1..A16 = R1 batch log, A17 = R1-F log; index tsv = r1_contact.index.tsv",
              fill=(170, 170, 180))
    summ = ""
    for ln in trace:
        if "SUMMARY" in ln:
            summ = ln.strip()
    draw.text((8, 44), summ[:190], fill=(150, 200, 255))
    if len(trace) > 1:
        dev = [l for l in trace if "DEVICE" in l or "graphicsDeviceName" in l]
        if dev:
            draw.text((8, 60), strip_tag(dev[-1])[:190], fill=(150, 220, 180))

    for c, cx, cy, span in placed:
        row = c["row"]
        x0 = cx * TILE_W
        y0 = HEADER + cy * (TILE_H + LABEL_H)
        w = span * TILE_W
        draw.line([(x0, y0), (x0 + w, y0)], fill=(90, 90, 100))
        draw.line([(x0, y0), (x0, y0 + TILE_H + LABEL_H)], fill=(90, 90, 100))
        draw.rectangle([x0, y0, x0 + w, y0 + LABEL_H - 1], fill=(28, 28, 34))
        draw.text((x0 + 4, y0 + 3), "#%s %s" % (row["grid"], row["title"])[:80], fill=(255, 235, 150))
        draw.text((x0 + 4, y0 + 16), (c["tile"] + ("  zsp=%s" % (span,)))[:80], fill=(180, 210, 255))
        for i, line in enumerate(c["vals"][:2]):
            draw.text((x0 + 4, y0 + 29 + i * 13), line[:110], fill=(200, 235, 205))
        if c["missing"]:
            draw.text((x0 + 4, y0 + 55 - 13), "missingLogPatterns=%d" % len(c["missing"]), fill=(255, 150, 150))

        path = os.path.join(shots, c["tile"])
        if not os.path.exists(path):
            draw.text((x0 + 6, y0 + LABEL_H + 10), "tile MISSING: " + c["tile"], fill=(255, 120, 120))
            continue
        try:
            im = Image.open(path).convert("RGB")
            box = screen_box_to_image(c["box"], im.width, im.height)
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
    print("sheet -> %s (%dx%d, %d cells, %d grids)" % (out_png, sheet.width, sheet.height, len(cells), len(rows_)))

    with open(out_tsv, "w", encoding="utf-8", newline="") as fh:
        fh.write("grid\tmeasuring_field_values\tscreenshot\tlog_line_no:verbatim\n")
        for row in rows_:
            vals, logs, missing = measure(lines, row)
            logs_txt = " || ".join("%d:%s" % (ln, txt) for ln, txt in logs[:4])
            if missing:
                logs_txt += (" || missingLogPatterns=%d" % len(missing))
            fh.write("\t".join([
                row["grid"],
                (row["title"] + " :: " + " ; ".join(vals)).replace("\t", " "),
                ",".join(row["tiles"]),
                logs_txt.replace("\t", " "),
            ]) + "\n")
        fh.write("# cells: one per tile (A5/A6/A7 have 2 slots, A9 has 6 walk frames); "
                 "no verdict words on purpose -- values only\n")
    print("index -> %s" % out_tsv)
    bad = sum(1 for row in rows_ if measure(lines, row)[2])
    print("grids with a missing log pattern = %d / %d" % (bad, len(rows_)))
    return 0


def main(argv):
    if len(argv) < 6:
        print(__doc__)
        return 2
    shots, out_png, out_tsv, frozen, trace_path = argv[1], argv[2], argv[3], argv[4], argv[5]
    lines = load(frozen)
    trace = load(trace_path)
    return build(lines, shots, out_png, out_tsv, trace)


if __name__ == "__main__":
    sys.exit(main(sys.argv))
