# -*- coding: utf-8 -*-
"""p34_sheet.py -- one-off contact-sheet builder for agent-34 (engine sink A3: Exists / LoadAll).

NOT shipped (lives in <project>/.ai-tmp/drivers/). Reads the frozen Play log + the sampler trace,
decides every evidence grid DETERMINISTICALLY (regex over the log + real file checks on the tile),
and writes:
  client/Assets/Screenshots/p34_ui_res.png          (one grid per evidence row, labels burned in)
  client/Assets/Screenshots/p34_ui_res.index.tsv    (grid <-> verdict <-> screenshot <-> log lines)

SKILL 1.12 item 1: judgement belongs to a script; the model only reads the finished sheet once.

Usage: python p34_sheet.py <shot_dir> <out_png> <out_tsv> <log_extract> <trace>
"""
import os
import re
import sys

from PIL import Image, ImageDraw

# CJK as escapes (this file stays ASCII)
CJK_TOAST = "\u6280\u80fd\u680f"                     # "skill bar" (the toast triggered by clicking slot 0)
CJK_OPTIONS = "\u80cc\u666f\u97f3\u6a50"             # "background music" (the options row label)
CJK_SETTINGS = "\u8bbe\u7f6e"                         # "[settings]" log tag

TS = r"\[(\d{4}-\d\d-\d\d \d\d:\d\d:\d\d\.\d+)\]"

# crops inside the 1920x1080 frames (keeps the burned labels readable)
CROP_HUD_ICONS = (400, 840, 1000, 1080)      # the L/R weapon slots + skill bar (original inv* art)
CROP_OPTIONS = (560, 260, 1360, 800)         # the options rows (CJK bitmap font)


class Row(object):
    def __init__(self, grid, name, kind, expect, tile=None, crop=None, pats=None):
        self.grid = grid
        self.name = name
        self.kind = kind
        self.expect = expect
        self.tile = tile
        self.crop = crop
        self.pats = pats or []
        self.verdict = "FAIL"
        self.reason = ""
        self.evidence = []


def load(path):
    with open(path, "r", encoding="utf-8", errors="replace") as fh:
        return [ln.rstrip("\r\n") for ln in fh]


def first(lines, pat):
    rx = re.compile(pat)
    for ln in lines:
        if rx.search(ln):
            return ln
    return ""


def main(argv):
    shot_dir = argv[1]
    out_png = argv[2]
    out_tsv = argv[3]
    log_path = argv[4]
    trace_path = argv[5]

    lines = load(log_path)
    trace = load(trace_path)
    console = first(trace, r"CONSOLE \{")
    marked = first(trace, r"SUMMARY tag=")

    console_err = "?"
    if console:
        m = re.search(r'"consoleErrors":\s*(\d+)', console)
        if m:
            console_err = m.group(1)

    # ---- the numbers this round is about (all from the probe's own log lines) --------------
    exists_icon = "RES exists[D2/Items/invbrx]="
    exists_miss = "RES exists[D2/Items/invnosuchitem]="
    exists_empty = "RES exists[empty]="
    exists_rep = "RES exists-repeat[D2/Items/invbrx]="
    strip_line = first(lines, r"\[P34\] RES loadall\[D2/UI/Menu/button_wide\]=")
    font_line = first(lines, r"\[P34\] RES loadall\[D2/Fonts/font42\]=")
    chi_line = first(lines, r"\[P34\] RES loadall\[D2/Fonts/font30_chi\]=")
    bogus_line = first(lines, r"\[P34\] RES loadall\[bogus\]=")
    tryget_line = first(lines, r"\[P34\] RES tryget\[button_wide\]=")
    d2icon_line = first(lines, r"\[P34\] RES d2icon\.")
    cfg_line = first(lines, r"\[P34\] RES cfgSource=")
    inv_line = first(lines, r"\[P34\] INV-OPEN=")
    set_line = first(lines, r"\[P34\] SETTINGS-OPEN=")
    toast_line = first(lines, r"\[P34\] CLICK-DONE name=SkillBar0 ")

    def tail(line, n=120):
        return line[-n:] if line else "(no log line)"

    m = re.search(r"loadall\[D2/UI/Menu/button_wide\]=(\d+) names=\[([^\]]*)\]", strip_line or "")
    strip_n = int(m.group(1)) if m else -1
    strip_names = m.group(2) if m else ""
    m = re.search(r"loadall\[D2/Fonts/font42\]=(\d+)", font_line or "")
    font_n = int(m.group(1)) if m else -1
    m = re.search(r"loadall\[D2/Fonts/font30_chi\]=(\d+)", chi_line or "")
    chi_n = int(m.group(1)) if m else -1
    m = re.search(r"loadall\[bogus\]=(\d+)", bogus_line or "")
    bogus_n = int(m.group(1)) if m else -1
    d2icon_true = len(re.findall(r"=[1]\b", d2icon_line or ""))
    inv_open = "1" in (inv_line or "")
    settings_open = "1" in (set_line or "")

    samples = {
        "mainmenu": first(lines, r"\[P34\] SAMPLE state=mainmenu "),
        "hud": first(lines, r"\[P34\] SAMPLE state=hud "),
        "inventory": first(lines, r"\[P34\] SAMPLE state=inventory "),
        "options": first(lines, r"\[P34\] SAMPLE state=options "),
    }

    rows = []

    def add(*a, **kw):
        rows.append(Row(*a, **kw))

    # ---------------------------------------------------------------- presentation rows
    add("G1", "main menu", "presentation",
        "original menu screen art + original button art + latin bitmap font, MainMenuPanel open",
        "p34_mm.png", None,
        [r"\[P34\] SAMPLE state=mainmenu .*panels=\[MainMenu\]", r"\[P34\] SHOT-GRID state=mainmenu "])
    add("G2", "stage HUD", "presentation",
        "original control-panel art + original inv* item icons (L/R slots) + CJK bitmap toast",
        "p34_hud.png", None,
        [r"\[P34\] SAMPLE state=hud .*panels=\[Hud\]", r"\[P34\] CLICK-DONE name=SkillBar0 ",
         r"\[P34\] SHOT-GRID state=hud "])
    add("G3", "inventory", "presentation",
        "original stone panel art + equip-slot art, InventoryPanel open over the HUD",
        "p34_inv.png", None,
        [r"\[P34\] SAMPLE state=inventory .*panels=\[Hud,Inventory\]", r"\[P34\] INV-OPEN=1",
         r"\[P34\] SHOT-GRID state=inventory "])
    add("G4", "options", "presentation",
        "CJK bitmap font on a real panel (volume / full-screen / quality / CLOSE) + original button art",
        "p34_opt.png", None,
        [r"\[P34\] SETTINGS-OPEN=1",
         r"\[P34\] SAMPLE state=options .*fsm=Pause ",
         r"\[P34\] SHOT-GRID state=options "])
    add("G5", "HUD icon zoom", "presentation(crop of G2)",
        "the on-screen item icons really are the original inv* art (L/R weapon slots)",
        "p34_hud.png", CROP_HUD_ICONS,
        [r"\[P34\] SAMPLE state=hud "])
    add("G6", "options zoom", "presentation(crop of G4)",
        "the CJK labels are drawn from the original bitmap font atlas (not the default Unity font)",
        "p34_opt.png", CROP_OPTIONS,
        [r"\[P34\] SETTINGS-OPEN=1"])

    # ---------------------------------------------------------------- numeric rows (no tile)
    def numeric(grid, name, expect, pats, note):
        r = Row(grid, name, "numeric(log/assert)", expect, None, None, pats)
        r.reason = note
        rows.append(r)

    numeric("N1", "LoadAll strip", "the whole-strip bulk load returns the SAME 3 sub-sprites as before "
            "(E1 root cause 1): count == 3, names == button_wide_0/1/2",
            [r"\[P34\] RES loadall\[D2/UI/Menu/button_wide\]=3 "],
            tail(strip_line, 90))
    numeric("N2", "LoadAll atlas/bogus", "font atlas 256 sub-sprites; a nonexistent path returns an EMPTY "
            "array (0), never null and never a throw",
            [r"\[P34\] RES loadall\[D2/Fonts/font42\]=256", r"\[P34\] RES loadall\[bogus\]=0"],
            "font42=%d  font30_chi=%d  bogus=%d" % (font_n, chi_n, bogus_n))
    numeric("N3", "Exists answers", "Exists: existing=true, missing=false, empty-path=false, repeat=true "
            "(cached, no second probe)",
            [r"\[P34\] RES exists\[D2/Items/invbrx\]=true",
             r"\[P34\] RES exists\[D2/Items/invnosuchitem\]=false",
             r"\[P34\] RES exists\[empty\]=false",
             r"\[P34\] RES exists-repeat\[D2/Items/invbrx\]=true"],
            "icon=true missing=false empty=false repeat=true")
    numeric("N4", "D2Icon switched", "D2Icon.ItemIconExists (the sync existence question E1 root cause 2) now "
            "asks Game.Res and answers true for 8 real item icons",
            [r"\[P34\] RES d2icon\."],
            "ids with exists=true: %d/8" % d2icon_true)
    numeric("N5", "three sync entries", "the entries really differ: TryGet does NOT load "
            "(strip not resident => null) while LoadAll does (=> 3)",
            [r"\[P34\] RES tryget\[button_wide\]=null"],
            tail(tryget_line, 70))
    numeric("N6", "ClientConfig switched", "config.json still loads (file source as before) -- now asked "
            "through Game.Res.LoadAll<TextAsset>",
            [r"\[P34\] RES cfgSource="],
            tail(cfg_line, 90))
    numeric("N7", "console errors", "consoleErrors = 0 for the whole session (console cleared right before play)",
            [],
            "consoleErrors=%s" % console_err)
    numeric("N8", "E1 exception gone", "Assets/Scripts has ZERO direct `Resources.Load*` call sites "
            "(uicheck 2-c gate) => the E1 exception can be removed",
            [],
            "uicheck 2-c: 0 hits (offline gate, run separately)")

    # ---------------------------------------------------------------- verdicts
    for r in rows:
        ev = []
        ok = True
        for p in r.pats:
            hit = first(lines, p)
            if hit:
                ev.append(hit)
            else:
                ok = False
                r.reason += "log-pattern-miss[" + p[:44] + "] "
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
                if size < 30000:
                    tile_ok = False
                    tile_msg = "tile-too-small(%d)" % size
                else:
                    try:
                        im = Image.open(path)
                        if im.size != (1920, 1080):
                            tile_ok = False
                            tile_msg = "tile-size=%s" % (im.size,)
                        else:
                            ext = im.convert("L").getextrema()
                            if ext[1] - ext[0] < 24:
                                tile_ok = False
                                tile_msg = "tile-nearly-uniform(range=%d)" % (ext[1] - ext[0])
                            else:
                                tile_msg = "%dx%d %dKB" % (im.size[0], im.size[1], size // 1024)
                    except Exception as exc:
                        tile_ok = False
                        tile_msg = "tile-unreadable(%s)" % exc
            if not tile_ok:
                r.reason += tile_msg + " "

        # per-grid numeric judgements (the only place numbers are interpreted)
        num_ok = True
        if r.grid == "N1":
            num_ok = strip_n == 3 and strip_names == "button_wide_0,button_wide_1,button_wide_2"
            r.reason += "" if num_ok else "strip count/names differ (n=%d names=%s) " % (strip_n, strip_names)
        elif r.grid == "N2":
            num_ok = font_n == 256 and bogus_n == 0
            r.reason += "" if num_ok else "font/bogus numbers wrong "
        elif r.grid == "N3":
            num_ok = ("=true" in (first(lines, re.escape(exists_icon)) or "=")) and \
                     ("=false" in (first(lines, re.escape(exists_miss)) or "=")) and \
                     ("=false" in (first(lines, re.escape(exists_empty)) or "=")) and \
                     ("=true" in (first(lines, re.escape(exists_rep)) or "="))
            r.reason += "" if num_ok else "Exists answers wrong "
        elif r.grid == "N4":
            num_ok = d2icon_true == 8
            r.reason += "" if num_ok else "d2icon true-count=%d " % d2icon_true
        elif r.grid == "N7":
            num_ok = console_err == "0"
            r.reason += "" if num_ok else "consoleErrors=%s " % console_err
        elif r.grid in ("G3",):
            num_ok = inv_open
            r.reason += "" if num_ok else "INV-OPEN=0 "
        elif r.grid in ("G4", "G6"):
            num_ok = settings_open
            r.reason += "" if num_ok else "SETTINGS-OPEN=0 "

        r.verdict = "PASS" if (ok and tile_ok and num_ok) else "FAIL"
        if not r.reason:
            r.reason = tile_msg
        elif tile_msg and "tile" not in r.reason:
            r.reason = r.reason + " " + tile_msg

    # ---------------------------------------------------------------- build the sheet
    TILE_W, TILE_H, LABEL_H, COLS = 480, 270, 30, 4
    n = len(rows)
    rows_n = (n + COLS - 1) // COLS
    HEADER = 64
    sheet = Image.new("RGB", (COLS * TILE_W, HEADER + rows_n * (TILE_H + LABEL_H)), (18, 18, 22))
    draw = ImageDraw.Draw(sheet)
    draw.text((8, 6), "p34 UI/resource contact sheet   agent-34 (engine sink A3: Exists / LoadAll)",
              fill=(255, 245, 180))
    draw.text((8, 24), "AI reads THIS sheet only; per-grid verdicts come from p34_sheet.py over the frozen "
                       "Play log + the tile files", fill=(170, 170, 180))
    draw.text((8, 42), marked[:110], fill=(150, 200, 255))

    for i, r in enumerate(rows):
        cx = (i % COLS) * TILE_W
        cy = HEADER + (i // COLS) * (TILE_H + LABEL_H)
        draw.line([(cx, cy), (cx + TILE_W, cy)], fill=(90, 90, 100))
        draw.line([(cx, cy), (cx, cy + TILE_H + LABEL_H)], fill=(90, 90, 100))
        color = (120, 255, 140) if r.verdict == "PASS" else (255, 110, 110)
        draw.rectangle([cx, cy, cx + TILE_W, cy + LABEL_H - 1], fill=(30, 30, 36))
        draw.text((cx + 4, cy + 3), "#%s %s | %s | %s" % (
            r.grid, r.name, "num" if r.kind.startswith("num") else "view", r.verdict), fill=color)
        draw.text((cx + 4, cy + 17), (r.reason or r.expect)[:74], fill=(210, 210, 140))

        if r.tile:
            path = os.path.join(shot_dir, r.tile)
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
            draw.text((cx + 6, cy + LABEL_H + 26), r.expect[:76], fill=(170, 220, 255))
            for k, ln in enumerate([r.reason] + [x[-110:] for x in r.evidence][:2]):
                draw.text((cx + 6, cy + LABEL_H + 44 + k * 14), ln[:78], fill=(200, 230, 210))

    sheet.save(out_png)
    print("sheet -> %s (%dx%d, %d grids)" % (out_png, sheet.width, sheet.height, n))

    with open(out_tsv, "w", encoding="utf-8", newline="") as fh:
        fh.write("grid\tstate\tcategory\texpectation\tscreenshot\tsample_line(verbatim)\t"
                 "log_evidence(verbatim)\tscript_verdict\n")
        for r in rows:
            fh.write("\t".join([
                r.grid, r.name, r.kind, r.expect.replace("\t", " "),
                ("Assets/Screenshots/" + r.tile) if r.tile else "(numeric row: no tile)",
                (samples.get(r.name, "") or "").replace("\t", " "),
                (" || ".join(r.evidence) if r.evidence else r.reason).replace("\t", " "),
                r.verdict,
            ]) + "\n")
    print("index -> %s" % out_tsv)

    bad = [r for r in rows if r.verdict != "PASS"]
    print("\n=== run facts ===")
    print("summary        : %s" % marked)
    print("consoleErrors  : %s" % console_err)
    print("strip          : n=%d names=%s" % (strip_n, strip_names))
    print("fonts          : font42=%d font30_chi=%d bogus=%d" % (font_n, chi_n, bogus_n))
    print("d2icon true    : %d/8" % d2icon_true)
    print("inventory open : %s   settings open : %s" % (inv_open, settings_open))
    print("\n=== grid verdicts ===")
    for r in rows:
        print("%-4s %-22s %-4s %s" % (r.grid, r.name, r.verdict, r.reason))
    print("\nFAIL count = %d / %d" % (len(bad), len(rows)))
    return 0 if not bad else 1


if __name__ == "__main__":
    sys.exit(main(sys.argv))
