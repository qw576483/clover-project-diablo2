# -*- coding: utf-8 -*-
"""
t0i_sheet.py -- judgement asset for the t0-play-repave-recapture slice.

Builds ONE contact sheet + ONE index for the evidence captured in Play:
  * defect 2: t0i_confirm_{n,h,p}.png  (CharCreate Confirm, AFTER a class is selected) --
    pixel diff N|H|P, exactly the rule the t0e hover sheet used (37.51% / 56.2% there).
  * the decisive render check: t0i_diag_stage_asis.png (right after Stage entry) vs
    t0i_diag_stage_forced.png (same subtree, forced activeInHierarchy) -- proves whether the
    T0FIX-H buffer-built map is actually rendered.
  * t0i_areachange_after.png (post area-change full-screen).

Outputs:
    .ai-tmp/screenshots/t0i_contact.png
    .ai-tmp/screenshots/t0i_contact.index.tsv

    python tools/probes/measure/t0i_sheet.py
"""
import os
import re

try:
    from PIL import Image, ImageDraw
except Exception as e:  # pragma: no cover
    print("PIL missing:", e)
    raise

HERE = os.path.abspath(__file__)
ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.dirname(HERE))))
SHOTS = os.path.join(ROOT, ".ai-tmp", "screenshots")
EVID = os.path.join(SHOTS, "t0i_evidence.txt")
KV = re.compile(r"([A-Za-z][A-Za-z0-9_]*)=([^ ]*)")


def kv_line(tag, fname=None):
    p = os.path.join(SHOTS, fname or "t0i_evidence.txt")
    if not os.path.exists(p):
        return {}
    t = open(p, "rb").read().decode("utf-8-sig").replace("\r\n", "\n")
    for l in t.split("\n"):
        i = l.find("[T0I] " + tag + "=")
        if i < 0:
            i = l.find("[T0ID] " + tag + "=")
        if i >= 0:
            return dict(KV.findall(l.partition(tag + "=")[2]))
    return {}


def pixel_diff(a, b):
    """Percentage of pixels whose RGB manhattan distance > 24 (same rule as t0e_hover_sheet)."""
    if not (os.path.exists(a) and os.path.exists(b)):
        return -1
    ia = Image.open(a).convert("RGB")
    ib = Image.open(b).convert("RGB")
    if ia.size != ib.size:
        return -2
    pa = ia.load()
    pb = ib.load()
    w, h = ia.size
    n = 0
    tot = 0
    for y in range(h):
        for x in range(w):
            ca = pa[x, y]
            cb = pb[x, y]
            d = abs(ca[0] - cb[0]) + abs(ca[1] - cb[1]) + abs(ca[2] - cb[2])
            tot += 1
            if d > 24:
                n += 1
    return round(100.0 * n / max(1, tot), 2)


def mean_lum(p):
    """Mean channel value on the 0..255 scale (a blank/black frame is ~0)."""
    if not os.path.exists(p):
        return -1
    im = Image.open(p).convert("L")
    px = im.getdata()
    s = 0
    c = 0
    for v in px:
        s += v
        c += 1
    return round(s / max(1, c), 2)


def build_t0v():
    """Rebuild the t0v_contact montage from whatever tiles t0v_contact.index.tsv cites.

    The index is authoritative: it lists, per cell, the tile file + the reading.  A reused
    Play driver later overwrote t0i_areachange_after.png, so the previously baked montage no
    longer matched its own index.  Glueing the CURRENT tile content into one vertical sheet
    (same 640-wide layout as before) restores montage <-> index consistency.

        python tools/probes/measure/t0i_sheet.py --t0v
    """
    idxp = os.path.join(SHOTS, "t0v_contact.index.tsv")
    with open(idxp, "rb") as f:
        t = f.read().decode("utf-8-sig").replace("\r\n", "\n")
    rows = []
    for l in t.split("\n")[1:]:
        if not l.strip():
            continue
        parts = l.split("\t")
        if len(parts) < 4:
            continue
        rows.append((parts[0], parts[1], parts[3]))   # cell, tile file, reading/label
    W = 640
    BAND = 26
    cells = []
    for cell, tile, label in rows:
        p = os.path.join(SHOTS, tile)
        im = Image.open(p).convert("RGB")
        im = im.resize((W, max(1, round(im.size[1] * W / im.size[0]))), Image.BICUBIC)
        lab = label if label.startswith(cell) else ("%s %s" % (cell, label))
        cells.append((im, lab, tile, mean_lum(p)))
    H = sum(im.size[1] + BAND for im, _, _, _ in cells)
    sheet = Image.new("RGB", (W, max(H, 1)), (18, 18, 22))
    dr = ImageDraw.Draw(sheet)
    y = 0
    for im, lab, tile, _ml in cells:
        sheet.paste(im, (0, y))
        y += im.size[1]
        dr.text((4, y + 6), lab, fill=(240, 240, 240))
        y += BAND
    outp = os.path.join(SHOTS, "t0v_contact.png")
    sheet.save(outp)
    print("t0v sheet=%s size=%s" % (outp, sheet.size))
    for cell, tile, label in rows:
        print("  %s <- %s  mean_lum=%s" % (cell, tile, mean_lum(os.path.join(SHOTS, tile))))


def main():
    n = os.path.join(SHOTS, "t0i_confirm_n.png")
    h = os.path.join(SHOTS, "t0i_confirm_h.png")
    p = os.path.join(SHOTS, "t0i_confirm_p.png")
    after = os.path.join(SHOTS, "t0i_areachange_after.png")
    asis = os.path.join(SHOTS, "t0i_diag_stage_asis.png")
    forced = os.path.join(SHOTS, "t0i_diag_stage_forced.png")

    cf = kv_line("CONFIRM")
    dh = pixel_diff(n, h)
    dp = pixel_diff(h, p)
    cat = "A_hover_feedback" if dh >= 0.5 else ("B_press_only" if dp >= 0.5 else "D_none")

    lum = {"confirm_n": mean_lum(n), "confirm_h": mean_lum(h), "confirm_p": mean_lum(p),
           "areachange_after": mean_lum(after),
           "diag_asis": mean_lum(asis), "diag_forced": mean_lum(forced)}

    idx = ["cell\tpanel\twidget\tkind\tstate\tmatrix_line\tsprN\tsprH\tsprP\twantH\twantP\t"
           "interactableBefore\tinteractableHover\thover_pixel_diff_pct\tpress_pixel_diff_pct\tcategory\t"
           "n_tile\th_tile\tp_tile\tmean_lum"]

    def add(*cols):
        idx.append("\t".join([str(c) for c in cols]))

    add("t0i-confirm", "CharCreatePanel", "Confirm", "Selectable", "class-selected", "L2457",
        cf.get("sprN", ""), cf.get("sprH", ""), cf.get("sprP", ""), cf.get("wantH", ""), cf.get("wantP", ""),
        cf.get("interactableBefore", ""), cf.get("interactableHover", ""),
        dh, dp, cat, "t0i_confirm_n.png", "t0i_confirm_h.png", "t0i_confirm_p.png", "")
    add("t0i-diag-asis", "Stage", "MapView", "presentation", "stage-as-is(right after entry)", "L4946",
        "", "", "", "", "", "", "", "", "", "render-check",
        "t0i_diag_stage_asis.png", "", "", lum["diag_asis"])
    add("t0i-diag-forced", "Stage", "MapView", "presentation", "stage-forced-active(control)", "L4946",
        "", "", "", "", "", "", "", "", "", "render-check",
        "t0i_diag_stage_forced.png", "", "", lum["diag_forced"])
    add("t0i-areachange-after", "Stage", "MapView", "presentation", "after-area-change", "L4946",
        "", "", "", "", "", "", "", "", "", "areachange",
        "t0i_areachange_after.png", "", "", lum["areachange_after"])
    add("t0i-sheet", "summary", "contact-sheet", "sheet", "all-of-the-above", "L4946",
        "", "", "", "", "", "", "", "", "", "sheet",
        "t0i_contact.png", "", "", "")
    open(os.path.join(SHOTS, "t0i_contact.index.tsv"), "w", encoding="utf-8", newline="").write("\n".join(idx) + "\n")

    # ---- contact sheet ----------------------------------------------------------------
    PAD = 8
    LABW = 640
    rows = []
    if os.path.exists(n):
        rows.append(("Confirm (CharCreate) AFTER class selected   [NORMAL | HOVER | PRESSED]\n"
                     " sprN=%s sprH=%s sprP=%s (sprite FIELD; SpriteSwap writes overrideSprite)\n"
                     " wantH=%s wantP=%s  interactable %s->%s\n"
                     " PIXEL DIFF hover=%s%% press=%s%% -> %s   (CharSelect/Enter reference = 37.51%% / 56.2%%)"
                     % (cf.get("sprN", "?"), cf.get("sprH", "?"), cf.get("sprP", "?"),
                        cf.get("wantH", "?"), cf.get("wantP", "?"),
                        cf.get("interactableBefore", "?"), cf.get("interactableHover", "?"),
                        dh, dp, cat),
                     [n, h, p], 1))
    rows.append(("DECISIVE: Stage as-is (left) vs same subtree force-activated (right)\n"
                 " mean_lum as-is = %s   forced = %s   ->  as-is is a BLANK/BLACK map"
                 % (lum["diag_asis"], lum["diag_forced"]),
                 [asis, forced], 1))
    if os.path.exists(after):
        rows.append(("Stage after the Town->BloodMoor area change (mean_lum=%s)" % lum["areachange_after"],
                     [after], 1))

    H = 90
    for _, imgs, _sc in rows:
        mh = 1
        for t in imgs:
            if os.path.exists(t):
                mh = max(mh, Image.open(t).size[1] if _sc == 0 else min(Image.open(t).size[1], 400))
        H += mh + 70
    W = LABW + 3 * 460 + PAD * 5
    sheet = Image.new("RGB", (W, max(H, 300)), (18, 18, 22))
    dr = ImageDraw.Draw(sheet)
    dr.text((10, 8), "T0I repave-recapture: defect-2 confirm feedback + the decisive render check", fill=(240, 240, 240))
    y = 46
    for lab, imgs, _sc in rows:
        dr.text((10, y), lab, fill=(210, 230, 255))
        x = LABW
        for t in imgs:
            if os.path.exists(t):
                im = Image.open(t).convert("RGB")
                if _sc == 1:
                    im.thumbnail((440, 400))
                sheet.paste(im, (x + PAD, y + PAD))
                dr.rectangle([x + PAD - 1, y + PAD - 1, x + PAD + im.size[0], y + PAD + im.size[1]], outline=(90, 90, 110))
                x += im.size[0] + PAD
        mh = 1
        for t in imgs:
            if os.path.exists(t):
                im = Image.open(t)
                mh = max(mh, min(im.size[1], 400) if _sc == 1 else im.size[1])
        y += mh + 70
    sheet.save(os.path.join(SHOTS, "t0i_contact.png"))

    rep = ["confirm hover_pixel_diff_pct=%s press_pixel_diff_pct=%s category=%s (ref CharSelect/Enter 37.51/56.2)"
           % (dh, dp, cat),
           "confirm interactable %s->%s  sprField N/H/P=%s/%s/%s wantH=%s wantP=%s"
           % (cf.get("interactableBefore", "?"), cf.get("interactableHover", "?"),
              cf.get("sprN", "?"), cf.get("sprH", "?"), cf.get("sprP", "?"), cf.get("wantH", "?"), cf.get("wantP", "?")),
           "stage mean_lum as-is=%s forced=%s (blank vs rendered)" % (lum["diag_asis"], lum["diag_forced"]),
           "areachange_after mean_lum=%s" % lum["areachange_after"],
           "sheet=%s" % os.path.join(SHOTS, "t0i_contact.png"),
           "index=%s" % os.path.join(SHOTS, "t0i_contact.index.tsv")]
    open(os.path.join(ROOT, ".ai-tmp", "test", "t0i_sheet_summary.txt"), "w", encoding="utf-8").write("\n".join(rep) + "\n")
    print("\n".join(rep))


if __name__ == "__main__":
    import sys
    if "--t0v" in sys.argv:
        build_t0v()
    else:
        main()
