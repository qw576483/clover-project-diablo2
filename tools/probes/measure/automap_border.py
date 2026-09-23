#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""automap_border.py -- is the Tab automap BORDER drawn the way the original draws it?

WHY THIS FILE EXISTS (slice automap-redo2, 2026-09-23):
  The user: "\u4ed6\u7684\u8fb9\u754c\u90fd\u662f\u7528\u4e00\u4e2a\u5c71\u7684\u56fe\u6807\u53bb\u63a7\u5236\u7684"
  ("his borders are all controlled by a mountain icon").  A human reading two PNGs cannot
  decide "the border is a light-grey stone/rock population" vs "the border is thin dark line
  art"; that is a histogram question, so it belongs to a script.

WHAT IT MEASURES (both numbers come from the REFERENCE, not from taste):
  * `rock_ratio`   -- fraction of pixels that are BRIGHT + NEUTRAL (luma >= LUMA_MIN,
                      chroma = max(r,g,b)-min(r,g,b) <= CHROMA_MAX).  On the original
                      automap baseline this is exactly the stone/rock border population
                      (the original's walkable interior is near-black; the border is light
                      grey).  Annotated arrows / green captions / HUD orbs are excluded by
                      the chroma test, the dark interior by the luma test.
  * `rock_colors`  -- top-K quantised RGB (bucket 16) inside that population: the original's
                      sampled border colour values ("\u4e3b\u8272\u76f4\u65b9\u56fe").
  * `match_ratio`  -- fraction of a frame's pixels within MATCH_TOL (per-channel) of any of the
                      reference's top-K rock colours.

ASSERTIONS (relative => self-calibrating; absolute thresholds are printed, never assumed):
  A1 reference self-proof : rock_ratio(REF) >= REF_MIN            [the ref really has stone]
  A2 coverage            : rock_ratio(OUR) >= COVER_K * rock_ratio(REF)
  A3 colour              : match_ratio(OUR) >= MATCH_K * match_ratio(REF)
  A4 our border is not line art: the rock population of OUR frame must not collapse
                           (rock_ratio(OUR) >= LINERT_MIN)

PROVENANCE of the constants (re-derivable: run with --report):
  REF  = 策划/基线图/原版_实机_HUD+automap_20260923.png   <-- CORRECTED 2026-09-23 (round 2)
         The capture used in round 1 (原版_automap_实机截图_20260923.png) was WRONG as a
         "border" source: locating its densest stone clusters with these very criteria and
         magnifying them 4x shows THE GAME WORLD ITSELF (cave rock + a lit torch + floor
         perspective), not automap ink.  Round 1's `rock_ratio REF=0.0263` therefore measured
         world rock, and its A2/A4 verdict must not be quoted.
         The HUD baseline magnified the same way shows the real automap: a LADDER OF SMALL
         GREY BRICKS on near-black, plus the world showing through in lit areas.
         measured here: luma p50=3 p90=53 p95=63 p99=98 (a dark capture, hence PCT-based).
  PCT = 95  -- the original automap's stone border IS its brightest band, so the stone
               threshold is taken as the reference's own 95th luma percentile (printed every
               run) instead of a hand-picked constant.  A1 then proves the band really is
               neutral stone (and not the yellow/green guide annotations) via CHROMA_MAX.
  `--lattice` additionally prints the STRUCTURAL definition of that ladder (connected-blob
  sizes, x/y pitch by projection autocorrelation, stone luma) for REF and OUR, so "像不像"
  becomes 间距/块尺寸/亮度三个数而不是一句观感。
  CHROMA_MAX = 45  -- the stone population is neutral; the yellow/red/green
                      annotations and the HUD orbs all sit above this.
  TOPK = 3, MATCH_TOL = 24, COVER_K = 0.5, MATCH_K = 0.6, REF_MIN = 0.025, LINERT_MIN = 0.02.
  Both frames are measured over the same rule (drop the bottom HUD band, DROP_BOTTOM = 0.25).

usage:
  python tools/probes/measure/automap_border.py --ref <ref.png> --our <our.png> \\
         [--sheet .ai-tmp/screenshots/automapredo_compare.png] [--report]

exit 0 = all assertions PASS, 1 = at least one FAIL, 2 = bad usage.
"""
import os
import sys

try:
    from PIL import Image, ImageDraw
except ImportError:
    print("PIL missing")
    sys.exit(2)

CHROMA_MAX = 45
BOX_R = 4                   # local-contrast window radius (9x9)
CONTRAST_MIN = 20           # ink-on-black: this pixel is >=20 luma above its own 9x9 surround
PCT = 95                    # the original automap's stone border IS its brightest band
TOPK = 3
MATCH_TOL = 24
COVER_K = 0.5
MATCH_K = 0.6
REF_MIN = 0.010          # 原版那张 capture 自己量到 0.0134；这条只是"REF 真的带墨"的体检
LINERT_MIN = 0.010       # 绝对下限：>=1% 的帧像素必须是"黑底上的浅灰墨"（原版 0.0134）
CORR_MIN = 0.50          # A5 只在自相关峰 >=0.50 时判（峰太弱说明没找到梯格）
LATTICE_ASPECT_K = 2.0   # A5 容差：pitch_y/pitch_x 的比值允许差 2 倍
DROP_BOTTOM = 0.25          # both baselines carry the D2 HUD at the bottom
BUCKET = 16


def rows(path):
    im = Image.open(path).convert("RGB")
    w, h = im.size
    keep = int(h * (1.0 - DROP_BOTTOM))
    return im.crop((0, 0, w, keep)), w, keep


def luma_hist(im):
    px = im.load()
    w, h = im.size
    hist = [0] * 256
    for y in range(h):
        for x in range(w):
            r, g, b = px[x, y]
            hist[(r * 299 + g * 587 + b * 114) // 1000] += 1
    return hist


def percentile(hist, p):
    total = float(sum(hist)) or 1.0
    want = total * p / 100.0
    acc = 0
    for i in range(256):
        acc += hist[i]
        if acc >= want:
            return i
    return 255


def stone_mask(im, luma_min):
    """-> (mask[2D int], lum[2D int])，mask = 三条 stone 判据（唯一定义处；`classify` 用它）。"""
    px = im.load()
    w, h = im.size
    lum = [[0] * w for _ in range(h)]
    for y in range(h):
        for x in range(w):
            r, g, b = px[x, y]
            lum[y][x] = (r * 299 + g * 587 + b * 114) // 1000
    R = BOX_R
    ii = [[0] * (w + 1) for _ in range(h + 1)]
    for y in range(h):
        run = 0
        for x in range(w):
            run += lum[y][x]
            ii[y + 1][x + 1] = ii[y][x + 1] + run
    mask = [[0] * w for _ in range(h)]
    for y in range(h):
        for x in range(w):
            if lum[y][x] < luma_min:
                continue
            r, g, b = px[x, y]
            if max(r, g, b) - min(r, g, b) > CHROMA_MAX:
                continue
            y0, y1 = max(0, y - R), min(h - 1, y + R)
            x0, x1 = max(0, x - R), min(w - 1, x + R)
            n = (y1 - y0 + 1) * (x1 - x0 + 1)
            s = ii[y1 + 1][x1 + 1] - ii[y0][x1 + 1] - ii[y1 + 1][x0] + ii[y0][x0]
            if lum[y][x] - s / float(n) >= CONTRAST_MIN:
                mask[y][x] = 1
    return mask, lum


def classify(im, luma_min):
    """-> the automap's STONE population: bright AND neutral AND drawn ON A DARK BACKGROUND.

    WHY THE THIRD TEST IS NOT OPTIONAL (measured this run): the automap is an overlay on top of
    the running game, so "bright + neutral" alone also matches the game world under it (brown
    dirt / grey path have chroma <= 45).  With that rule only, our present frame scores 3.2% and
    the original 3.7% -- i.e. the check would PASS while the two frames look nothing alike.
    The automap's stone is INK ON BLACK: its immediate 8-neighbourhood is mostly far darker.
    The world's grass/dirt is a LARGE LOW-CONTRAST REGION and has almost no such pixels.
    """
    px = im.load()
    w, h = im.size
    lum = [[0] * w for _ in range(h)]
    for y in range(h):
        for x in range(w):
            r, g, b = px[x, y]
            lum[y][x] = (r * 299 + g * 587 + b * 114) // 1000
    # integral image of luma -> mean over a (2R+1)^2 window in O(1) per pixel
    R = BOX_R
    ii = [[0] * (w + 1) for _ in range(h + 1)]
    for y in range(h):
        run = 0
        for x in range(w):
            run += lum[y][x]
            ii[y + 1][x + 1] = ii[y][x + 1] + run
    stone = []
    for y in range(h):
        for x in range(w):
            if lum[y][x] < luma_min:
                continue
            r, g, b = px[x, y]
            if max(r, g, b) - min(r, g, b) > CHROMA_MAX:
                continue
            y0, y1 = max(0, y - R), min(h - 1, y + R)
            x0, x1 = max(0, x - R), min(w - 1, x + R)
            n = (y1 - y0 + 1) * (x1 - x0 + 1)
            s = ii[y1 + 1][x1 + 1] - ii[y0][x1 + 1] - ii[y1 + 1][x0] + ii[y0][x0]
            if lum[y][x] - s / float(n) >= CONTRAST_MIN:
                stone.append((r, g, b))
    return stone


def top_colors(rock, k=TOPK):
    tally = {}
    for r, g, b in rock:
        key = (r // BUCKET * BUCKET, g // BUCKET * BUCKET, b // BUCKET * BUCKET)
        tally[key] = tally.get(key, 0) + 1
    return sorted(tally.items(), key=lambda kv: -kv[1])[:k]


def match_ratio(rock, ref_colors, tol=MATCH_TOL):
    if not rock:
        return 0.0
    n = 0
    for r, g, b in rock:
        for cr, cg, cb in ref_colors:
            if abs(r - cr) <= tol and abs(g - cg) <= tol and abs(b - cb) <= tol:
                n += 1
                break
    return n / float(len(rock))


def modes(hist):
    total = float(sum(hist)) or 1.0
    dark = [i for i in range(256) if i < 60]
    light = [i for i in range(256) if i >= 100]
    d = sum(hist[i] for i in dark) / total
    l = sum(hist[i] for i in light) / total
    return d, l


def lattice(mask, lum):
    """量化"砖块梯格"：连通块统计 + 梯格间距（自相关）+ 亮度。

    `mask` 是 stone 掩码（与判据同一套常量）。间距用掩码的**列和/行和自相关**取第一个
    显著峰的位置（像素，源帧尺度）；砖块用 8 连通块的点数分布。两个量都是"判过程"的量：
    间距不会因为整体提亮/压暗而变，块数也不会因为改一个颜色常量而变。
    """
    h = len(mask)
    w = len(mask[0]) if h else 0
    seen = [[0] * w for _ in range(h)]
    blobs = []
    for y0 in range(h):
        for x0 in range(w):
            if not mask[y0][x0] or seen[y0][x0]:
                continue
            stack = [(x0, y0)]
            seen[y0][x0] = 1
            n = 0
            minx = maxx = x0
            miny = maxy = y0
            while stack:
                x, y = stack.pop()
                n += 1
                if x < minx: minx = x
                if x > maxx: maxx = x
                if y < miny: miny = y
                if y > maxy: maxy = y
                for dy in (-1, 0, 1):
                    for dx in (-1, 0, 1):
                        nx, ny = x + dx, y + dy
                        if 0 <= nx < w and 0 <= ny < h and mask[ny][nx] and not seen[ny][nx]:
                            seen[ny][nx] = 1
                            stack.append((nx, ny))
            blobs.append((n, maxx - minx + 1, maxy - miny + 1))
    blobs.sort(reverse=True)

    # 梯格间距 = 掩码"列和/行和"的自相关第一个显著峰（k = 3..40 px）。
    # WHY 用自相关而不是"人工量一块砖"：砖块本身连成一串（见 blobs），量单块不可靠；
    # 而"每隔多少像素重复一次"是这套梯格的**结构不变量** —— 整体提亮/压暗不会改变它。
    def autocorr(vals, m):
        base = sum(v * v for v in vals) or 1
        best_k, best_v = 0, 0.0
        for k in range(3, 41):
            s = 0
            for i in range(m - k):
                s += vals[i] * vals[i + k]
            v = s / float(base) * (m / float(m - k))
            if v > best_v:
                best_v, best_k = v, k
        return best_k, best_v

    cols = [sum(mask[y][x] for y in range(h)) for x in range(w)]
    rows = [sum(mask[y]) for y in range(h)]
    px, vx = autocorr(cols, w)
    py, vy = autocorr(rows, h)

    lit = [lum[y][x] for y in range(h) for x in range(w) if mask[y][x]]
    lit.sort()
    n = len(lit)
    return dict(blobs=blobs, blobs_total=len(blobs), pitch_x=px, corr_x=vx,
                pitch_y=py, corr_y=vy,
                luma_p50=lit[n // 2] if n else 0,
                luma_p90=lit[int(n * 0.9)] if n else 0, luma_max=lit[-1] if n else 0)


def main(argv):
    ref = our = sheet = None
    report = False
    lattice_on = False
    i = 1
    while i < len(argv):
        if argv[i] == "--ref":
            ref = argv[i + 1]; i += 2
        elif argv[i] == "--our":
            our = argv[i + 1]; i += 2
        elif argv[i] == "--sheet":
            sheet = argv[i + 1]; i += 2
        elif argv[i] == "--report":
            report = True; i += 1
        elif argv[i] == "--lattice":
            lattice_on = True; i += 1
        else:
            print("unknown arg %s" % argv[i]); return 2
    if not ref or not our:
        print(__doc__); return 2

    ref_im, rw, rh = rows(ref)
    our_im, ow, oh = rows(our)
    ref_hist = luma_hist(ref_im)
    our_hist = luma_hist(our_im)
    luma_min = percentile(ref_hist, PCT)
    ref_rock = classify(ref_im, luma_min)
    our_rock = classify(our_im, luma_min)
    rn = float(rw * rh)
    on = float(ow * oh)
    rr = len(ref_rock) / rn
    orr = len(our_rock) / on
    ref_top = top_colors(ref_rock)
    ref_colors = [c for c, _ in ref_top]
    mr_ref = match_ratio(ref_rock, ref_colors)
    mr_our = match_ratio(our_rock, ref_colors)

    print("REF %s  crop=%dx%d" % (ref, rw, rh))
    print("OUR %s  crop=%dx%d" % (our, ow, oh))
    if report:
        d, l = modes(ref_hist)
        print("REPORT REF luma: dark(<60)=%.3f  light(>=100)=%.3f  p50=%d p90=%d p95=%d p99=%d"
              % (d, l, percentile(ref_hist, 50), percentile(ref_hist, 90),
                 percentile(ref_hist, 95), percentile(ref_hist, 99)))
        d, l = modes(our_hist)
        print("REPORT OUR luma: dark(<60)=%.3f  light(>=100)=%.3f  p95=%d"
              % (d, l, percentile(our_hist, 95)))
        for c, n in ref_top:
            print("REPORT REF rock colour %s  %d px (%.3f%% of crop)"
                  % (c, n, 100.0 * n / rn))
    print("luma_min(REF p%d)=%d  chroma_max=%d  contrast>=%d over %dx%d"
          "  (stone test = luma>=%d AND chroma<=%d AND brighter-than-local-surround)"
          % (PCT, luma_min, CHROMA_MAX, CONTRAST_MIN, 2 * BOX_R + 1, 2 * BOX_R + 1,
             luma_min, CHROMA_MAX))
    print("rock_ratio  REF=%.4f  OUR=%.4f" % (rr, orr))
    print("ref_top%d=%s" % (TOPK, ref_colors))
    print("match_ratio REF=%.4f  OUR=%.4f" % (mr_ref, mr_our))

    lat = {}
    if lattice_on:
        rf, rl = stone_mask(ref_im, luma_min)
        of, ol = stone_mask(our_im, luma_min)
        for tag, mk, lm in (("REF", rf, rl), ("OUR", of, ol)):
            L = lattice(mk, lm)
            lat[tag] = L
            blobs = L["blobs"]
            print("LATTICE %s  blobs=%d  blob_px p50=%d p90=%d max=%d | blob_bbox p50=(%dx%d)"
                  "  pitch_x=%dpx(corr=%.2f) pitch_y=%dpx(corr=%.2f) | stone_luma p50=%d p90=%d max=%d"
                  % (tag, L["blobs_total"],
                     blobs[len(blobs) // 2][0] if blobs else 0,
                     blobs[int(len(blobs) * 0.1)][0] if blobs else 0,
                     blobs[0][0] if blobs else 0,
                     blobs[len(blobs) // 2][1] if blobs else 0,
                     blobs[len(blobs) // 2][2] if blobs else 0,
                     L["pitch_x"], L["corr_x"], L["pitch_y"], L["corr_y"],
                     L["luma_p50"], L["luma_p90"], L["luma_max"]))

    checks = [
        ("A1 ref-has-stone", rr >= REF_MIN,
         "rock_ratio(REF)=%.4f >= %.3f" % (rr, REF_MIN)),
        ("A2 coverage", orr >= COVER_K * rr,
         "rock_ratio(OUR)=%.4f >= %.4f (=%.2f*%.4f)" % (orr, COVER_K * rr, COVER_K, rr)),
        ("A3 colour", mr_our >= MATCH_K * mr_ref,
         "match_ratio(OUR)=%.4f >= %.4f (=%.2f*%.4f)" % (mr_our, MATCH_K * mr_ref, MATCH_K, mr_ref)),
        ("A4 not-line-art", orr >= LINERT_MIN,
         "rock_ratio(OUR)=%.4f >= %.3f (absolute floor; the REF itself is %.4f)" %
         (orr, LINERT_MIN, rr)),
    ]
    # A5 = 结构判据：梯格"行距/列距"的**比值**。比值是尺度无关的（截图缩放过也不变），
    # 而"间距绝对值"会随捕获分辨率变 ⇒ 只判比值，且两轴的自相关峰都要够显著才判。
    if lat.get("REF") and lat.get("OUR"):
        rx, ry = lat["REF"]["pitch_x"], lat["REF"]["pitch_y"]
        ox, oy = lat["OUR"]["pitch_x"], lat["OUR"]["pitch_y"]
        if (rx and ry and ox and oy
                and lat["REF"]["corr_x"] >= CORR_MIN and lat["REF"]["corr_y"] >= CORR_MIN
                and lat["OUR"]["corr_x"] >= CORR_MIN and lat["OUR"]["corr_y"] >= CORR_MIN):
            ar_ref = ry / float(rx)
            ar_our = oy / float(ox)
            good = (max(ar_ref, ar_our) / min(ar_ref, ar_our)) <= LATTICE_ASPECT_K
            checks.append((
                "A5 lattice-aspect",
                good,
                "lattice pitch_y/pitch_x: REF=%d/%d=%.2f  OUR=%d/%d=%.2f (within x%.1f)"
                % (ry, rx, ar_ref, oy, ox, ar_our, LATTICE_ASPECT_K)))
        else:
            print("A5 lattice-aspect SKIPPED (autocorrelation peak too weak to trust)")
    bad = 0
    for name, ok, why in checks:
        print("CHK %-18s %s   %s" % (name, "PASS" if ok else "FAIL", why))
        if not ok:
            bad += 1
    print("FAIL count = %d / %d" % (bad, len(checks)))

    if sheet:
        S = max(1, min(3, 1600 // max(rw, ow)))
        a = ref_im.resize((rw * S, rh * S), Image.NEAREST)
        b = our_im.resize((ow * S, oh * S), Image.NEAREST)
        W = max(a.width, b.width)
        H = a.height + b.height + 84
        out = Image.new("RGB", (W, H), (16, 16, 20))
        out.paste(a, (0, 26))
        out.paste(b, (0, a.height + 26 + 40))
        dr = ImageDraw.Draw(out)
        dr.text((6, 6), "REF (top) = 原版 baseline   vs   OUR (bottom) = our Tab frame",
                fill=(255, 240, 170))
        dr.text((6, a.height + 32),
                "rock_ratio ref=%.4f our=%.4f | match ref=%.4f our=%.4f | top colors %s"
                % (rr, orr, mr_ref, mr_our, ref_colors), fill=(160, 220, 255))
        dr.text((6, a.height + 48), "FAIL count = %d (see stdout for the per-assertion lines)"
                % bad, fill=(255, 120, 120) if bad else (120, 255, 140))
        os.makedirs(os.path.dirname(sheet), exist_ok=True)
        out.save(sheet)
        print("sheet -> %s (%dx%d)" % (sheet, out.width, out.height))
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
