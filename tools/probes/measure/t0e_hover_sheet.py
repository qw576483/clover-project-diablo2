# -*- coding: utf-8 -*-
"""
t0e_hover_sheet.py -- judgement asset for the t0-e43-hover-acceptance slice.

Turns the per-widget hover/press tiles captured in Play (.ai-tmp/screenshots/t0e_hover/)
into ONE contact sheet + the cell <-> matrix-row index the acceptance table cites.

Why a pixel diff is the verdict for a PRESENTATION row: uGUI's SpriteSwap writes
Image.overrideSprite (not Image.sprite), so the rendered frame -- not the sprite field --
is the thing the player sees.  The sheet shows normal | hover | pressed side by side and
the index carries the measured per-state pixel difference plus the SpriteState the widget
declares (wantH / wantP) and the ColorTint colours.

    python tools/probes/measure/t0e_hover_sheet.py
"""
import os
import re
import sys
import collections

try:
    from PIL import Image, ImageDraw
except Exception as e:  # pragma: no cover
    print("PIL missing:", e)
    raise

HERE = os.path.abspath(__file__)
ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.dirname(HERE))))
SHOTS = os.path.join(ROOT, ".ai-tmp", "screenshots")
TILES = os.path.join(SHOTS, "t0e_hover")
EVID = os.path.join(SHOTS, "t0e_evidence.txt")

PANEL_LINE = {
    "BootPanel": 2454, "CharCreatePanel": 2457, "CharSelectPanel": 2460,
    "CharacterPanel": 2463, "D2ConfirmPanel": 2466, "DeathPanel": 2469,
    "HudPanel": 2472, "InventoryPanel": 2475, "LoadingPanel": 2478,
    "MainMenuPanel": 2481, "MiniMapPanel": 2484, "NpcDialogPanel": 2487,
    "PausePanel": 2490, "QuestLogPanel": 2493, "SettingsPanel": 2496,
    "ShopPanel": 2499, "SkillTreePanel": 2502,
}
KV = re.compile(r"([A-Za-z][A-Za-z0-9_]*)=([^ ]*)")

# widget -> state:event cell of the state matrix (longest prefix wins)
WIDGET_CELL = [
    ("SpotAmazon", "\u5730\u56fe"), ("SpotBarbarian", ""), ("NameInput", ""),
]


def rows():
    t = open(EVID, "rb").read().decode("utf-8-sig").replace("\r\n", "\n")
    out = []
    for l in t.split("\n"):
        i = l.find("[T0E] HOVER=")
        if i >= 0:
            out.append(dict(KV.findall(l[i + 6 + 6:])))
    return out


def diff(a, b):
    if not (os.path.exists(a) and os.path.exists(b)):
        return -1
    ia = Image.open(a).convert("RGBA")
    ib = Image.open(b).convert("RGBA")
    if ia.size != ib.size:
        return -2
    pa = ia.load()
    pb = ib.load()
    w, h = ia.size
    n = 0
    step = 1
    tot = 0
    for y in range(0, h, step):
        for x in range(0, w, step):
            ca = pa[x, y]
            cb = pb[x, y]
            d = abs(ca[0] - cb[0]) + abs(ca[1] - cb[1]) + abs(ca[2] - cb[2])
            tot += 1
            if d > 24:
                n += 1
    return round(100.0 * n / max(1, tot), 2)


def main():
    rs = rows()
    idx = ["cell\tpanel\twidget\tkind\tmatrix_line\tsprN\twantH\twantP\ttrans\tctint_n_h_changed\trayTopBL\trayTopTL\thover_pixel_diff_pct\tpress_pixel_diff_pct\tcategory\tn_tile\th_tile\tp_tile"]
    parsed = []
    for d in rs:
        g = d.get("cell", "g?").lstrip("g")
        panel = d.get("panel", "?")
        n = os.path.join(TILES, "g%s_n.png" % g)
        h = os.path.join(TILES, "g%s_h.png" % g)
        p = os.path.join(TILES, "g%s_p.png" % g)
        dh = diff(n, h)
        dp = diff(h, p)
        tint = "Y" if d.get("ctintH") != d.get("ctintN") or d.get("ctintP") != d.get("ctintH") else "N"
        if dh >= 0.5:
            c = "A_hover_feedback"
        elif dp >= 0.5:
            c = "B_press_only"
        elif tint == "Y":
            c = "C_tint_only"
        else:
            c = "D_none"
        parsed.append((d, g, panel, dh, dp, tint, c, n, h, p))
        idx.append("\t".join([
            d.get("cell", "?"), panel, d.get("widget", "?"), d.get("kind", "?"),
            "L%d" % PANEL_LINE.get(panel, -1), d.get("sprN", ""), d.get("wantH", ""), d.get("wantP", ""),
            d.get("trans", ""), tint, d.get("rayTopBL", ""), d.get("rayTopTL", ""),
            str(dh), str(dp), c, os.path.basename(n), os.path.basename(h), os.path.basename(p)]))
    open(os.path.join(SHOTS, "t0e_hover_contact.index.tsv"), "w", encoding="utf-8", newline="").write("\n".join(idx) + "\n")

    # ---- contact sheet: rows = widgets, 3 columns (normal | hover | pressed) ----
    COLS = 3
    LABW = 560
    PAD = 6
    TITLE = 46
    cells = []
    for d, g, panel, dh, dp, tint, c, n, h, p in parsed:
        cells.append((d, g, panel, dh, dp, c, [n, h, p]))
    # all tiles are already cropped to the widget (+6 px), so cell height varies: use a
    # fixed per-row height = max tile height in that row.
    rowH = []
    for d, g, panel, dh, dp, c, tiles in cells:
        m = 1
        for t in tiles:
            if os.path.exists(t):
                m = max(m, Image.open(t).size[1])
        rowH.append(m)
    colW = []
    for ci in range(COLS):
        m = 1
        for (d, g, panel, dh, dp, c, tiles) in cells:
            t = tiles[ci]
            if os.path.exists(t):
                m = max(m, Image.open(t).size[0])
        colW.append(min(m, 420))
    W = LABW + sum(colW) + PAD * (COLS + 1)
    H = TITLE + sum(rh + PAD for rh in rowH) + PAD
    H = min(H, 30000)
    sheet = Image.new("RGBA", (W, H), (18, 18, 22, 255))
    dr = ImageDraw.Draw(sheet)
    dr.text((10, 8), "T0E hover/press contact sheet -- per interactive control: NORMAL | HOVER | PRESSED  (real pointer injection)", fill=(240, 240, 240))
    y = TITLE
    for ri, (d, g, panel, dh, dp, c, tiles) in enumerate(cells):
        lab = "%s %s/%s\n %s\n wantH=%s wantP=%s\n pixDiff h=%s%% p=%s%% -> %s" % (
            d.get("cell"), panel, d.get("widget", "").split("/")[-1], d.get("kind", ""),
            d.get("wantH"), d.get("wantP"), dh, dp, c)
        dr.text((10, y + 4), lab, fill=(210, 230, 255))
        x = LABW
        for ci in range(COLS):
            t = tiles[ci]
            if os.path.exists(t):
                im = Image.open(t).convert("RGBA")
                sheet.alpha_composite(im, (x + PAD, y + PAD))
            dr.rectangle([x + PAD - 1, y + PAD - 1, x + PAD + colW[ci], y + PAD + rowH[ri]], outline=(70, 70, 80, 255))
            x += colW[ci] + PAD
        y += rowH[ri] + PAD
    sheet.convert("RGB").save(os.path.join(SHOTS, "t0e_hover_contact.png"))

    sm = collections.Counter(x[6] for x in parsed)
    per = collections.Counter((x[2], x[6]) for x in parsed)
    rep = ["widgets=%d" % len(parsed), "sheet=%s" % os.path.join(SHOTS, "t0e_hover_contact.png"),
           "index=%s" % os.path.join(SHOTS, "t0e_hover_contact.index.tsv"), "--- categories ---"]
    for k, v in sm.most_common():
        rep.append("%s\t%d" % (k, v))
    rep.append("--- per panel ---")
    for p in sorted(set(x[0] for x in per)):
        rep.append("%s\t%s" % (p, " ".join(sorted("%s=%d" % (cc, vv) for ((pp, cc), vv) in per.items() if pp == p))))
    rep.append("--- A rows (hover feedback measurable in the rendered frame) ---")
    for x in parsed:
        if x[6] == "A_hover_feedback":
            rep.append("%s %s/%s pixDiff h=%s%% p=%s%%" % (x[0].get("cell"), x[2], x[0].get("widget", "").split("/")[-1], x[3], x[4]))
    rep.append("--- rows with NO rendered change in any state ---")
    for x in parsed:
        if x[6] == "D_none":
            rep.append("%s %s/%s trans=%s wantH=%s wantP=%s pixDiff h=%s p=%s" % (
                x[0].get("cell"), x[2], x[0].get("widget", "").split("/")[-1], x[0].get("trans"), x[0].get("wantH"), x[0].get("wantP"), x[3], x[4]))
    open(os.path.join(ROOT, ".ai-tmp", "test", "t0e_sheet_summary.txt"), "w", encoding="utf-8").write("\n".join(rep) + "\n")
    print("sheet rows=%d" % len(parsed))


if __name__ == "__main__":
    main()
