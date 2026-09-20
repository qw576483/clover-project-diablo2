# -*- coding: utf-8 -*-
"""p41_sheet.py -- contact-sheet builder + verdict engine for pass 5 (the FINAL Play of the round).

NOT shipped (lives in <project>/.ai-tmp/test/, deleted before delivery).
Reads the frozen Play log (the [P41] lines) + the sampler trace, decides every evidence grid
DETERMINISTICALLY (regex over the log + real checks on the tile files) and writes:
  .ai-tmp/test/p41_contact.png     one grid per evidence row, labels + numbers burned in
  .ai-tmp/test/p41_index.tsv       grid <-> verdict <-> screenshot <-> the log lines that carry it

SKILL 1.12 item 1: the judgement belongs to a script; the model reads the finished sheet once.

Usage: python p41_sheet.py <shot_dir> <out_png> <out_tsv> <frozen_log> <trace>
"""
import os
import re
import sys

from PIL import Image, ImageDraw

TS = r"\[(\d{4}-\d\d-\d\d \d\d:\d\d:\d\d\.\d+)\]"

# crop boxes inside the 1920x1080 frames (the byline row sits at canvas y=-462 -> screen y 1002)
CROP_BYLINE = (680, 950, 1240, 1050)


class Row(object):
    def __init__(self, grid, name, kind, expect, tile=None, crop=None, pats=None, note=""):
        self.grid = grid
        self.name = name
        self.kind = kind
        self.expect = expect
        self.tile = tile
        self.crop = crop
        self.pats = pats or []
        self.note = note
        self.verdict = "FAIL"
        self.reason = ""
        self.evidence = []


KEY_EQ = re.compile(r"(\[P41\] )([A-Z][A-Z0-9\-]*)=")


def normalize(line):
    """`[P41] OSIZE=rigValue=3.75` -> `[P41] OSIZE rigValue=3.75`.

    The driver publishes one `[P41] KEY=value` line per probe (as the round's task asks).  The grid
    patterns below are written against the `NAME rest=...` form, so the KEY '=' is turned into a space
    here -- one place, instead of 20 near-identical regexes.  Lines that already use a space stay
    untouched (`[P41] VERDICT ok=1`, `[P41] SHOT-OK n=1 ...`, `[P41] CHK name=...`).
    """
    return KEY_EQ.sub(r"\1\2 ", line)


def load(path):
    with open(path, "r", encoding="utf-8", errors="replace") as fh:
        return [normalize(ln.rstrip("\r\n")) for ln in fh]


def first(lines, pat):
    rx = re.compile(pat)
    for ln in lines:
        if rx.search(ln):
            return ln
    return ""


def num(line, pat, default=None):
    if not line:
        return default
    m = re.search(pat, line)
    if not m:
        return default
    try:
        return float(m.group(1))
    except ValueError:
        return default


def build_rows(lines, trace):
    console = first(trace, r"CONSOLE \{")
    console_err = None
    if console:
        m = re.search(r'"consoleErrors":\s*(\d+)', console)
        if m:
            console_err = int(m.group(1))

    rows = []

    def add(*a, **kw):
        rows.append(Row(*a, **kw))

    # ---------------------------------------------------------------- presentation grids
    add("G1", "boot screen", "presentation",
        "boot/start screen: original DIABLO II logo + byline row at the bottom (readable)",
        "p41_01_boot.png", None,
        [r"\[P41\] BOOT scene=\S* *.*focused=\d",
         r"\[P41\] BYLINE where=boot present=1 .*tier=Font24 .*ok=1",
         r"\[P41\] SHOT-OK n=1 state=boot name=p41_01_boot\.png kind=screen"])
    add("G2", "boot byline zoom", "presentation(crop of G1)",
        "'by clover-engine' really is drawn at the bottom of the FIRST screen (font24 tier)",
        "p41_01_boot.png", CROP_BYLINE,
        [r"\[P41\] BYLINE where=boot present=1 .*ok=1"])
    add("G3", "main menu", "presentation",
        "main menu draws ONLY SINGLE PLAYER / EXIT (the two unshipped items are gone; the original "
        "slots are NOT re-filled) + the byline row at the bottom",
        "p41_02_menu.png", None,
        [r"\[P41\] MENU scene=Menu",
         r"\[P41\] MENU-FORBIDDEN multiplayerOrCinematics=0 expected=0",
         r"\[P41\] MENU-TEXTS labelTexts=\[EXIT,SINGLE PLAYER\] hits=2 forbiddenTexts=0 ok=1",
         r"\[P41\] SHOT-OK n=2 state=menu name=p41_02_menu\.png kind=screen"])
    add("G4", "menu byline zoom", "presentation(crop of G3)",
        "'by clover-engine' on the main menu (this row did not exist before this round)",
        "p41_02_menu.png", CROP_BYLINE,
        [r"\[P41\] BYLINE where=menu present=1 .*ok=1"])
    add("G5", "Rogue Encampment", "presentation",
        "town at the SHIPPED framing: one screen is about 7.5 grid rows (ortho 3.75), new "
        "56x40 window layout",
        "p41_03_town.png", None,
        [r"\[P41\] TOWN area=Town map=56x40 ",
         r"\[P41\] OSIZE rigValue=3\.75 camValue=3\.75 visibleWorldH=7\.5",
         r"\[P41\] SHOT-OK n=3 state=town name=p41_03_town\.png kind=camera"])
    add("G6", "town wide (probe ortho)", "presentation",
        "whole 56x40 level in one frame (probe-only ortho 12 = CameraRig.MaxOrthographicSize, so the "
        "new window origin and the inland exit can be seen)",
        "p41_04_town_wide.png", None,
        [r"\[P41\] WIDE-SHOT which=town orthoOverride=12 overrideOk=1",
         r"\[P41\] SHOT-OK n=4 state=town-wide name=p41_04_town_wide\.png kind=camera"])
    add("G7", "exit gap close-up", "presentation",
        "the town exit (17,26..28) is original town_floor dirt road with ground OUTSIDE it - not a "
        "black gap and not a cyan block",
        "p41_05_town_exit.png", None,
        [r"\[P41\] WALKTO target=\(19,27\) arrived=1",
         r"\[P41\] SHOT-OK n=5 state=town-exit name=p41_05_town_exit\.png kind=camera"])
    add("G8", "Blood Moor", "presentation",
        "wilderness at the shipped framing (player just stepped through the exit): original tiles, "
        "monsters around",
        "p41_06_wild.png", None,
        [r"\[P41\] WILD area=BloodMoor map=80x80 ",
         r"\[P41\] MONSTERS aliveCount=\d+",
         r"\[P41\] SHOT-OK n=6 state=wild name=p41_06_wild\.png kind=camera"])
    add("G9", "wilderness wide (probe ortho)", "presentation",
        "a whole slice of the 80x80 map in one frame: the map is many screens big and the monsters "
        "are spread over it (probe-only ortho 12)",
        "p41_07_wild_wide.png", None,
        [r"\[P41\] WIDE-SHOT which=wild orthoOverride=12 overrideOk=1",
         r"\[P41\] SHOT-OK n=7 state=wild-wide name=p41_07_wild_wide\.png kind=camera"])
    # (grid, screen direction, tile, DIR-SHOT n = index+1, SHOT-OK n = 20+index)
    dirs = [("G10", "S", "p41_08_dir_s.png", 1, 20),
            ("G11", "N", "p41_09_dir_n.png", 2, 21),
            ("G12", "E", "p41_10_dir_e.png", 3, 22),
            ("G13", "W", "p41_11_dir_w.png", 4, 23)]
    for grid, want, tile, dirn, shotn in dirs:
        low = want.lower()
        add(grid, "walk %s (%s)" % (want, {"S": "south", "N": "north",
                                           "E": "east", "W": "west"}[want]), "presentation",
            "the sprite really faces the walked direction: logical Dir == %s AND the frame key is "
            "walk_%s_* (the direction mapping that used to be off by one step)" % (want, low),
            tile, None,
            [r"\[P41\] DIR-SHOT n=%d tile=%s want=%s dir=%s dirOk=1 key=\S*(?:walk|run)_%s_\d+ keyOk=1 "
             r"sprite=(?:walk|run)_%s_\d+ spriteOk=1" % (dirn, re.escape(tile), want, want, low, low),
             r"\[P41\] SHOT-OK n=%d state=dir-%s name=%s kind=camera" % (shotn, want, re.escape(tile))])

    # ---------------------------------------------------------------- numeric grids
    def numeric(grid, name, expect, pats, note):
        r = Row(grid, name, "numeric(log/assert)", expect, None, None, pats, note)
        rows.append(r)

    numeric("N1", "camera framing", "CameraRig.OrthographicSize == 3.75 and the camera really carries "
            "it; visible height == 2x3.75 == 7.5 world units == 7.5 grid rows (1 grid = 1 unit)",
            [r"\[P41\] OSIZE rigValue=3\.75 camValue=3\.75 visibleWorldH=7\.5 .*visibleGridRows=7\.5"],
            "watch the rig value AND the value read back off Camera.main")
    numeric("N2", "walk/run constants", "GameConst.PlayerWalkSpeed == 3.0 and PlayerWalkSpeedFactor "
            "== 7/15 (walk 1.4 cells/s)",
            [r"\[P41\] SPEEDCONST PlayerWalkSpeed=3 walkFactor=0\.46667 walkSpeedDerived=1\.4"],
            "origin: Diablerie Player.cs walkSpeed 7 / runSpeed 15 over Iso.SubTileCount 5")
    numeric("N3", "measured walk/run speed", "3 s straight-line displacement == run 3.0 cells/s, "
            "walk 1.4 cells/s (movingFrames == totalFrames, measured in cell space)",
            [r"\[P41\] SPEED mode=run .*speed=[\d.]+ .*expect=3\.000 .*cellSpace=1",
             r"\[P41\] SPEED mode=walk .*speed=[\d.]+ .*expect=1\.400 .*cellSpace=1"],
            "integrated in the motor's cell-center space (world units are logged too)")
    numeric("N4", "animation frame rate", "effective sprite frame rate == frames x cells/s: run 8x3.0 "
            "= 24 fps, walk 8x1.4 = 11.2 fps (one animation cycle per grid cell)",
            [r"\[P41\] ANIM-RUN anim=Run frames=8 .*effFps=24\.00",
             r"\[P41\] ANIM-WALK anim=Walk frames=8 .*effFps=11\.20"],
            "baseFps x speedScale, cross-checked against the measured FrameIndex advance per second")
    numeric("N5", "map sizes", "town == 56x40 (Levels.txt 'Act 1 - Town'), wilderness == FIXED 80x80 "
            "(Levels.txt 'Act 1 - Wilderness 1'; it used to be a random 48..80)",
            [r"\[P41\] TOWN area=Town map=56x40 ",
             r"\[P41\] WILD area=BloodMoor map=80x80 "],
            "read off IMapModule.Width/Height in both areas")
    numeric("N6", "wilderness monster density", "AliveCount is around the per-cell sampling "
            "expectation walkable x 520/100000 x 1.83 (~41.9) - the old 'packs' reading gave ~17",
            [r"\[P41\] MONSTERS aliveCount=\d+ .*expectedFromMonDen=[\d.]+"],
            "MonDen = level_c mon_density per Levels.txt; sampling denominator 100000")
    numeric("N7", "exit cells + tiles", "the exit cells (17,26)/(17,27)/(17,28) are TileKind.Exit and "
            "carry the ORIGINAL ground tiles town_floor/035|054|052 with an empty object layer",
            [r"\[P41\] EXITTILE g=\(17,26\) kind=Exit keysAvailable=1 ground=town_floor/035 object=\(none\)",
             r"\[P41\] EXITTILE g=\(17,27\) kind=Exit keysAvailable=1 ground=town_floor/054 object=\(none\)",
             r"\[P41\] EXITTILE g=\(17,28\) kind=Exit keysAvailable=1 ground=town_floor/052 object=\(none\)"],
            "ground key read through the non-contract MapModule.TryGetTileKeys")
    numeric("N8", "session hygiene", "whole session: consoleErrors == 0, no step timeouts, player "
            "alive, camera ortho restored to 3.75, driver verdict ok=1",
            [r"\[P41\] FINISH .*timeoutAt=\(none\) playerDead=0 .*rigOrthoNow=3\.75",
             r"\[P41\] VERDICT ok=1 "],
            "consoleErrors read from console_status after the run")

    # ---------------------------------------------------------------- tile checks
    shot_dir = SHOT_DIR
    for r in rows:
        ev = []
        ok = True
        for p in r.pats:
            hit = first(lines, p)
            if hit:
                ev.append(hit)
            else:
                ok = False
                r.reason += "log-pattern-miss[" + p[:46] + "] "
        r.evidence = ev

        tile_ok = True
        tile_msg = ""
        if r.tile:
            path = os.path.join(shot_dir, r.tile)
            if not os.path.exists(path):
                tile_ok = False
                tile_msg = "tile-missing"
            else:
                size = os.path.getsize(path)
                if size < 20000:
                    tile_ok = False
                    tile_msg = "tile-too-small(%d)" % size
                else:
                    try:
                        im = Image.open(path)
                        if im.size != (1920, 1080):
                            tile_ok = False
                            tile_msg = "tile-size=%s" % (im.size,)
                        else:
                            cim = im.convert("L")
                            if r.crop:
                                cim = cim.crop(r.crop)
                            ext = cim.getextrema()
                            if ext[1] - ext[0] < 24:
                                tile_ok = False
                                tile_msg = "nearly-uniform(range=%d)" % (ext[1] - ext[0])
                            else:
                                tile_msg = "%dx%d %dKB" % (im.size[0], im.size[1], size // 1024)
                    except Exception as exc:
                        tile_ok = False
                        tile_msg = "tile-unreadable(%s)" % exc
            if not tile_ok:
                r.reason += tile_msg + " "

        num_ok = True
        detail = ""
        g = r.grid
        osize = first(lines, r"\[P41\] OSIZE ")
        monsters = first(lines, r"\[P41\] MONSTERS ")
        if g == "G1":
            num_ok = True
        elif g in ("G2", "G4"):
            pass
        elif g == "G3":
            mb = first(lines, r"\[P41\] MENU-BUTTONS ")
            m = re.search(r"names=\[([^\]]*)\]", mb or "")
            names = m.group(1) if m else ""
            num_ok = ("active=2" in (mb or "")) and names == "Quit,Single"
            detail = "active buttons names=[%s] (expected Quit,Single)" % names
        elif g == "G5":
            o = num(osize, r"rigValue=([\d.]+)")
            c = num(osize, r"camValue=([\d.]+)")
            vh = num(osize, r"visibleWorldH=([\d.]+)")
            num_ok = o == 3.75 and c == 3.75 and vh == 7.5
            detail = "ortho rig=%s cam=%s visibleWorldH=%s" % (o, c, vh)
        elif g == "G6":
            num_ok = True
        elif g == "G7":
            n = 0
            for x, y, k in ((17, 26, "035"), (17, 27, "054"), (17, 28, "052")):
                if first(lines, r"\[P41\] EXITTILE g=\(%d,%d\) kind=Exit .*ground=town_floor/%s " % (x, y, k)):
                    n += 1
            num_ok = n == 3
            detail = "exit cell tile keys matched %d/3 (expect town_floor/035,054,052)" % n
        elif g == "G8":
            alive = num(monsters, r"aliveCount=(\d+)")
            num_ok = alive is not None and 20 <= alive <= 90
            detail = "AliveCount=%s (expected band 20..90, mean 41.9)" % alive
        elif g == "G9":
            inview = num(monsters, r"inOneScreen=(\d+)")
            num_ok = inview is not None
            detail = "monsters inside this very frame=%s (map-wide alive=%s)" % (
                inview, num(monsters, r"aliveCount=(\d+)"))
        elif g in ("G10", "G11", "G12", "G13"):
            want = {"G10": "S", "G11": "N", "G12": "E", "G13": "W"}[g]
            line = first(lines, r"\[P41\] DIR-SHOT .*want=%s " % want)
            num_ok = line is not None and ("dir=%s " % want) in (line or "") and "dirOk=1" in (line or "")
            detail = (line or "(no DIR-SHOT line)").split("[P41] ")[-1][:150]
        elif g == "N1":
            o = num(osize, r"rigValue=([\d.]+)")
            c = num(osize, r"camValue=([\d.]+)")
            vh = num(osize, r"visibleWorldH=([\d.]+)")
            num_ok = o == 3.75 and c == 3.75 and vh == 7.5
            detail = "ortho rig=%s cam=%s visibleWorldH=%s aspect=%s" % (
                o, c, vh, num(osize, r"aspect=([\d.]+)"))
        elif g == "N2":
            sc = first(lines, r"\[P41\] SPEEDCONST ")
            ws = num(sc, r"PlayerWalkSpeed=([\d.]+)")
            wf = num(sc, r"walkFactor=([\d.]+)")
            wd = num(sc, r"walkSpeedDerived=([\d.]+)")
            num_ok = ws == 3.0 and abs((wf or 0) - 7.0 / 15.0) < 0.0001 and wd == 1.4
            detail = "walkSpeed=%s factor=%s => walk=%s cells/s" % (ws, wf, wd)
        elif g == "N3":
            sr = first(lines, r"\[P41\] SPEED mode=run ")
            sw = first(lines, r"\[P41\] SPEED mode=walk ")
            run = num(sr, r"speed=([\d.]+)")
            walk = num(sw, r"speed=([\d.]+)")
            mr = re.search(r"movingFrames=(\d+)/(\d+)", sr or "")
            mw = re.search(r"movingFrames=(\d+)/(\d+)", sw or "")
            full = bool(mr and mr.group(1) == mr.group(2)) and bool(mw and mw.group(1) == mw.group(2))
            num_ok = (run is not None and abs(run - 3.0) <= 0.30
                      and walk is not None and abs(walk - 1.4) <= 0.25 and full)
            detail = "run=%s (exp 3.000) walk=%s (exp 1.400) movingFramesFull=%s" % (run, walk, full)
        elif g == "N4":
            ar = first(lines, r"\[P41\] ANIM-RUN ")
            aw = first(lines, r"\[P41\] ANIM-WALK ")
            er = num(ar, r"effFps=([\d.]+)")
            mrun = num(ar, r"measuredFrameAdvancePerSec=([\d.]+)")
            ew = num(aw, r"effFps=([\d.]+)")
            mwalk = num(aw, r"measuredFrameAdvancePerSec=([\d.]+)")
            num_ok = (er == 24.0 and ew == 11.2
                      and mrun is not None and 19 <= mrun <= 29
                      and mwalk is not None and 8.5 <= mwalk <= 14)
            detail = "run eff=%s meas=%s | walk eff=%s meas=%s" % (er, mrun, ew, mwalk)
        elif g == "N5":
            tw = num(first(lines, r"\[P41\] TOWN "), r"map=(\d+)x")
            th = num(first(lines, r"\[P41\] TOWN "), r"map=\d+x(\d+)")
            ww = num(first(lines, r"\[P41\] WILD "), r"map=(\d+)x")
            wh = num(first(lines, r"\[P41\] WILD "), r"map=\d+x(\d+)")
            num_ok = (tw, th) == (56.0, 40.0) and (ww, wh) == (80.0, 80.0)
            detail = "town=%sx%s wild=%sx%s" % (tw, th, ww, wh)
        elif g == "N6":
            alive = num(monsters, r"aliveCount=(\d+)")
            exp = num(monsters, r"expectedFromMonDen=([\d.]+)")
            inview = num(monsters, r"inOneScreen=(\d+)")
            near = num(monsters, r"within20Cells=(\d+)")
            walk = num(monsters, r"walkable=(\d+)")
            num_ok = alive is not None and 20 <= alive <= 90
            detail = ("alive=%s expected=%s walkable=%s | in one screen=%s within20=%s"
                      % (alive, exp, walk, inview, near))
        elif g == "N7":
            got = []
            for x, y in ((17, 26), (17, 27), (17, 28)):
                got.append(first(lines, r"\[P41\] EXITTILE g=\(%d,%d\)" % (x, y)).split("[P41] ")[-1])
            num_ok = all(("ground=town_floor/" + k) in g for k, g in zip(("035", "054", "052"), got))
            detail = " || ".join(got)
        elif g == "N8":
            ver = first(lines, r"\[P41\] VERDICT ")
            chk = re.findall(r"([a-z\-]+)=(\d)", ver or "")
            bad = [n for n, v in chk if v == "0"]
            num_ok = console_err == 0 and not bad
            detail = "consoleErrors=%s verdictOk=%s failed-checks=%s" % (
                console_err, "ok=1" in (ver or ""), ",".join(bad) if bad else "(none)")

        if detail:
            r.reason = (r.reason + " " + detail).strip()

        r.verdict = "PASS" if (ok and tile_ok and num_ok) else "FAIL"
        if not r.reason:
            r.reason = tile_msg
        elif tile_msg and "tile" not in r.reason:
            r.reason = r.reason + " " + tile_msg

    return rows, console_err


def build_sheet(rows, out_png, out_tsv, trace):
    TILE_W, TILE_H, LABEL_H, COLS = 480, 270, 42, 4
    n = len(rows)
    rowsn = (n + COLS - 1) // COLS
    HEADER = 66
    sheet = Image.new("RGB", (COLS * TILE_W, HEADER + rowsn * (TILE_H + LABEL_H)), (18, 18, 22))
    draw = ImageDraw.Draw(sheet)
    draw.text((8, 6), "p41 contact sheet -- pass 5 final Play: 9 user complaints, ONE session",
              fill=(255, 245, 180))
    draw.text((8, 24), "grids G*=pictures (numbered, with the numbers burned in), N*=runtime numbers; "
                       "verdicts come from p41_sheet.py over the frozen log + the tile files",
              fill=(170, 170, 180))
    draw.text((8, 44), first(trace, r"SUMMARY tag=")[:140] or "(no summary line)",
              fill=(150, 200, 255))

    for i, r in enumerate(rows):
        cx = (i % COLS) * TILE_W
        cy = HEADER + (i // COLS) * (TILE_H + LABEL_H)
        draw.line([(cx, cy), (cx + TILE_W, cy)], fill=(90, 90, 100))
        draw.line([(cx, cy), (cx, cy + TILE_H + LABEL_H)], fill=(90, 90, 100))
        color = (120, 255, 140) if r.verdict == "PASS" else (255, 110, 110)
        draw.rectangle([cx, cy, cx + TILE_W, cy + LABEL_H - 1], fill=(30, 30, 36))
        draw.text((cx + 4, cy + 3), "#%s %s | %s" % (r.grid, r.name, r.verdict), fill=color)
        draw.text((cx + 4, cy + 17), (r.expect or "")[:78], fill=(210, 210, 140))
        draw.text((cx + 4, cy + 30), (r.reason or "")[:78], fill=(170, 220, 255))

        if r.tile:
            path = os.path.join(SHOT_DIR, r.tile)
            if os.path.exists(path):
                try:
                    im = Image.open(path).convert("RGB")
                    if r.crop:
                        im = im.crop(r.crop)
                    scale = min(float(TILE_W) / im.width, float(TILE_H) / im.height)
                    im = im.resize((max(1, int(im.width * scale)), max(1, int(im.height * scale))),
                                   Image.NEAREST)
                    ox = cx + (TILE_W - im.width) // 2
                    oy = cy + LABEL_H + (TILE_H - im.height) // 2
                    sheet.paste(im, (ox, oy))
                except Exception as exc:
                    draw.text((cx + 4, cy + LABEL_H + 8), "tile error: %s" % exc, fill=(255, 120, 120))
            else:
                draw.text((cx + 4, cy + LABEL_H + 8), "tile MISSING", fill=(255, 120, 120))
        else:
            draw.text((cx + 6, cy + LABEL_H + 8), "NUMERIC  " + r.name, fill=(255, 245, 180))
            for k, ln in enumerate([r.reason] + [x[-108:] for x in r.evidence][:2]):
                draw.text((cx + 6, cy + LABEL_H + 28 + k * 16), ln[:80], fill=(200, 230, 210))

    sheet.save(out_png)
    print("sheet -> %s (%dx%d, %d grids)" % (out_png, sheet.width, sheet.height, n))

    with open(out_tsv, "w", encoding="utf-8", newline="") as fh:
        fh.write("grid\tstate\tcategory\texpectation\tscreenshot\tshow_this_number\t"
                 "log_evidence(verbatim)\tscript_verdict\n")
        for r in rows:
            fh.write("\t".join([
                r.grid, r.name, r.kind, r.expect.replace("\t", " "),
                ("Assets/Screenshots/" + r.tile) if r.tile else "(numeric row: no tile)",
                (r.reason or "").replace("\t", " "),
                (" || ".join(r.evidence) if r.evidence else "(see script_verdict)").replace("\t", " "),
                r.verdict,
            ]) + "\n")
    print("index -> %s" % out_tsv)


def main(argv):
    global SHOT_DIR
    SHOT_DIR = argv[1]
    out_png = argv[2]
    out_tsv = argv[3]
    log_path = argv[4]
    trace_path = argv[5]

    lines = load(log_path)
    trace = load(trace_path)

    rows, console_err = build_rows(lines, trace)
    build_sheet(rows, out_png, out_tsv, trace)

    bad = [r for r in rows if r.verdict != "PASS"]
    print("\n=== run facts ===")
    print("frozen log     : %s (%d lines)" % (log_path, len(lines)))
    for key in ("CFG ", "BOOT ", "MENU-BUTTONS", "TOWN ", "OSIZE", "WILD ", "MONSTERS",
                "SPEEDCONST", "FINISH", "VERDICT"):
        ln = first(lines, r"\[P41\] " + key.replace(" ", ""))
        if ln:
            print("%-14s : %s" % (key.strip(), ln.split("[P41] ")[-1][:200]))
    print("consoleErrors  : %s" % console_err)
    print("\n=== grid verdicts ===")
    for r in rows:
        print("%-4s %-24s %-4s %s" % (r.grid, r.name, r.verdict, r.reason[:130]))
    print("\nFAIL count = %d / %d" % (len(bad), len(rows)))
    return 0 if not bad else 1


if __name__ == "__main__":
    SHOT_DIR = ""
    sys.exit(main(sys.argv))
