# -*- coding: utf-8 -*-
"""scan_automap_cels.py -- 全量扫 `AutoMap.txt` 的 CelN 值域 + 全 1260 帧 `MaxiMap.dc6` 分块联络图，
并用 `measure/automap_border.py` 里那**三条** stone 判据（luma>=122 / chroma<=45 / 比 9x9 邻域亮>=20）
把"浅灰实心山岩族"找出来。**(仅读取证；不改产品代码；判据不放宽。)**

为什么需要它（片 automap-redo2 第 2 轮，team-lead 批准）：第 1 轮已证 `mapicons.DC6` 是地标图标，
而我们的表（`AutoMapCel.generated.cs` 里 `CelPixels` 的键）用到的 cel 族画出来是"稀疏点阵"，
与原版边界的"浅灰实心山岩块"不同族。缺的是**载体**：哪一批 `CelN` 才是山岩族。本脚本把这件事
变成一条可复跑命令 —— 值域统计（机械）+ 全量联络图（读图）+ 每帧 stone 计数（机械，与
`automap_border.py` 同一套常量）。

产物
  `.ai-tmp/test/automapredo/cel_scan.tsv`      逐帧：cel / 非零像素 / 均值亮度 / 最大亮度 /
                                               stone 像素 / stone 占比 / AutoMap.txt 里出现次数 / 本表是否用到
  `.ai-tmp/test/automapredo/cel_scan.txt`      值域统计（stdout 全文）
  `.ai-tmp/screenshots/automapredo_maximap_sheet_<lo>_<hi>.png`  分块联络图（`*`=本表用到，
                                               黄色标签=该帧 stone 像素 >0）

用法：`python tools/probes/scan_automap_cels.py [--chunk 126] [--no-sheet]`
"""
import collections
import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
sys.path.insert(0, os.path.join(ROOT, "tools"))
sys.path.insert(0, os.path.join(ROOT, "tools", "probes", "measure"))

from d2codec import dc6 as dc6mod  # noqa: E402
from PIL import Image, ImageDraw  # noqa: E402

import automap_border as AB  # noqa: E402  (同一套 stone 常量，绝不复制第二份)

AUTOMAP_TXT = os.path.join(ROOT, "原版资源", "d2raw", "data", "global", "excel", "AutoMap.txt")
MAXIMAP_DC6 = os.path.join(ROOT, "原版资源", "d2dc6", "data", "global", "ui", "AUTOMAP", "MaxiMap.dc6")
ACT1_PL2 = os.path.join(ROOT, "原版资源", "d2raw", "data", "global", "palette", "ACT1", "Pal.PL2")
GEN_CS = os.path.join(ROOT, "client", "Assets", "Scripts", "Core", "AutoMapCel.generated.cs")
OUT_TSV = os.path.join(ROOT, ".ai-tmp", "test", "automapredo", "cel_scan.tsv")
OUT_TXT = os.path.join(ROOT, ".ai-tmp", "test", "automapredo", "cel_scan.txt")
SHOT_DIR = os.path.join(ROOT, ".ai-tmp", "screenshots")

SCALE = 2
PAD = 8
LBL = 12
COLS = 14
ROWS = 9
PER_SHEET = COLS * ROWS


# ─────────────────────────────────────────────────────────────────────────────
#  AutoMap.txt
# ─────────────────────────────────────────────────────────────────────────────
def load_automap():
    """返回 (header, rows)。列序同 `gen_automap.py:load_automap`（f0/1/2/3/4 + Cel 在 f6/8/10/12）。"""
    raw = open(AUTOMAP_TXT, "rb").read().decode("latin-1")
    header, rows = None, []
    for line in raw.split("\n"):
        line = line.rstrip("\r")
        if not line.strip():
            continue
        f = line.split("\t")
        if header is None:
            header = f
            continue
        try:
            rows.append((f[0], f[1], int(f[2]), int(f[3]), int(f[4]),
                         [int(f[6]), int(f[8]), int(f[10]), int(f[12])]))
        except (IndexError, ValueError):
            rows.append((f[0], f[1], -1, -1, -1, [-1, -1, -1, -1]))
    return header, rows


# ─────────────────────────────────────────────────────────────────────────────
#  MaxiMap.dc6 逐帧 stone 计数（判据 = automap_border 的三条，透明像素当黑底）
# ─────────────────────────────────────────────────────────────────────────────
def frame_stone(frame, lum_by_index):
    """返回 (nonzero, mean_luma, max_luma, stone_px)。

    stone 判据逐字同 `automap_border.classify`：`luma >= AB.percentile(REF,p95)` 用外层算好的
    `stone_luma`；`chroma <= AB.CHROMA_MAX`；`luma - 9x9 邻域均值 >= AB.CONTRAST_MIN`。
    透明像素（索引 0）按 luma 0 / chroma 0 处理 —— 这正是 automap 把它们 blit 到黑底上的语义。
    """
    w, h = frame.width, frame.height
    px = frame.indices
    lum = [[0] * w for _ in range(h)]
    nz = 0
    ssum = 0
    smax = 0
    for y in range(h):
        for x in range(w):
            v = px[y * w + x]
            if v:
                nz += 1
            l = lum_by_index[v]
            lum[y][x] = l
            if v:
                ssum += l
                if l > smax:
                    smax = l
    R = AB.BOX_R
    ii = [[0] * (w + 1) for _ in range(h + 1)]
    for y in range(h):
        run = 0
        for x in range(w):
            run += lum[y][x]
            ii[y + 1][x + 1] = ii[y][x + 1] + run

    stone = 0
    for y in range(h):
        for x in range(w):
            v = px[y * w + x]
            if not v:
                continue
            l = lum[y][x]
            if l < STONE_LUMA:
                continue
            r, g, b = palette[v][:3]
            if max(r, g, b) - min(r, g, b) > AB.CHROMA_MAX:
                continue
            y0, y1 = max(0, y - R), min(h - 1, y + R)
            x0, x1 = max(0, x - R), min(w - 1, x + R)
            n = (y1 - y0 + 1) * (x1 - x0 + 1)
            s = ii[y1 + 1][x1 + 1] - ii[y0][x1 + 1] - ii[y1 + 1][x0] + ii[y0][x0]
            if l - s / float(n) >= AB.CONTRAST_MIN:
                stone += 1
    return nz, (ssum / float(nz) if nz else 0.0), smax, stone


def used_cels():
    txt = open(GEN_CS, "r", encoding="utf-8").read()
    return sorted(set(int(m) for m in re.findall(r"^\s*\{\s*(\d+),\s*Decode\(", txt, re.M)))


palette = None
STONE_LUMA = 122          # 外层从原版基线 p95 复算后覆盖（见 main）


def main(argv):
    global palette, STONE_LUMA
    chunk = PER_SHEET
    do_sheet = True
    i = 1
    while i < len(argv):
        if argv[i] == "--chunk":
            chunk = int(argv[i + 1]); i += 2
        elif argv[i] == "--no-sheet":
            do_sheet = False; i += 1
        else:
            print("unknown arg %s" % argv[i]); return 2

    log = []

    def say(s):
        print(s)
        log.append(s)

    # ---- 判据常量：与 automap_border 同源，从原版基线复算 ----
    ref = os.path.join(ROOT, "策划", "基线图", "原版_automap_实机截图_20260923.png")
    ref_im, rw, rh = AB.rows(ref)
    STONE_LUMA = AB.percentile(AB.luma_hist(ref_im), AB.PCT)
    say("STONE criteria (identical to measure/automap_border.py): "
        "luma>=%d (REF p%d) AND chroma<=%d AND luma - mean(9x9) >= %d"
        % (STONE_LUMA, AB.PCT, AB.CHROMA_MAX, AB.CONTRAST_MIN))

    # ---- AutoMap.txt 值域 ----
    header, rows = load_automap()
    say("AutoMap.txt header: %s" % header)
    say("AutoMap.txt data rows: %d" % len(rows))
    cnt = collections.Counter()
    fam = collections.defaultdict(collections.Counter)
    neg = 0
    for lv, tile, st, s0, s1, cels in rows:
        for c in cels:
            if c < 0:
                neg += 1
            else:
                cnt[c] += 1
                fam[tile][c] += 1
    say("CelN slots: total=%d  (-1 slots=%d)  distinct CelN=%d  min=%s  max=%s"
        % (len(rows) * 4, neg, len(cnt), min(cnt) if cnt else None, max(cnt) if cnt else None))
    say("CelN top20 by occurrences: %s" % ", ".join(
        "%d:%d" % (c, n) for c, n in cnt.most_common(20)))
    say("TileName families (13 expected): %s" % ", ".join(
        "%s(%d cels)" % (t, len(v)) for t, v in sorted(fam.items())))
    for t in sorted(fam):
        say("  family %-6s cels=%s" % (t, ",".join(str(c) for c in sorted(fam[t]))))

    # ---- MaxiMap 全量 ----
    global palette
    palette = dc6mod.read_pl2(ACT1_PL2)
    lum_by_index = []
    for idx in range(256):
        r, g, b = palette[idx][:3]
        lum_by_index.append((r * 299 + g * 587 + b * 114) // 1000)
    d = dc6mod.parse(open(MAXIMAP_DC6, "rb").read())
    used = set(used_cels())
    say("MaxiMap.dc6 frames=%d  (16x32)  used by our table: %s" % (len(d.frames), sorted(used)))

    stats = []
    for n, f in enumerate(d.frames):
        nz, mean_l, max_l, stone = frame_stone(f, lum_by_index)
        stats.append(dict(cel=n, nz=nz, mean=mean_l, mx=max_l, stone=stone,
                          occ=cnt.get(n, 0), used=(n in used)))
    stone_frames = [s for s in stats if s["stone"] > 0]
    stone_frames.sort(key=lambda s: -s["stone"])
    say("frames with stone pixels (>0): %d / %d" % (len(stone_frames), len(stats)))
    say("stone-richest 40: %s" % ", ".join(
        "%d(s=%d,occ=%d%s)" % (s["cel"], s["stone"], s["occ"], ",USED" if s["used"] else "")
        for s in stone_frames[:40]))
    used_stone = [s for s in stats if s["used"]]
    say("our table's own cels, stone px each: %s" % ", ".join(
        "%d:%d" % (s["cel"], s["stone"]) for s in used_stone))
    # 值域覆盖：AutoMap.txt 里点到的 CelN 有多少落在 1260 帧内
    oob = sorted(c for c in cnt if c >= len(d.frames))
    say("AutoMap.txt CelN outside 0..%d: %s" % (len(d.frames) - 1, oob if oob else "(none)"))

    # ---- 落盘 ----
    os.makedirs(os.path.dirname(OUT_TSV), exist_ok=True)
    with open(OUT_TSV, "w", encoding="utf-8", newline="") as fh:
        fh.write("cel\tnonzero\tmean_luma\tmax_luma\tstone_px\tstone_ratio_of_frame\t"
                 "occ_in_automap_txt\tused_by_our_table\n")
        for s in stats:
            fh.write("%d\t%d\t%.1f\t%d\t%d\t%.4f\t%d\t%d\n" % (
                s["cel"], s["nz"], s["mean"], s["mx"], s["stone"],
                s["stone"] / float(16 * 32), s["occ"], 1 if s["used"] else 0))
    say("tsv -> %s" % OUT_TSV)

    # ---- 分块联络图（全 1260 帧） ----
    if do_sheet:
        stone_set = set(s["cel"] for s in stone_frames)
        for lo in range(0, len(d.frames), chunk):
            hi = min(lo + chunk, len(d.frames))
            idxs = list(range(lo, hi))
            rows_n = (len(idxs) + COLS - 1) // COLS
            cw = 16 * SCALE + PAD * 2
            ch = 32 * SCALE + PAD * 2 + LBL
            # 每行顶部再加一条 14px 的"行号尺"（读图时定位用）
            sheet = Image.new("RGB", (COLS * cw, rows_n * ch + 16), (16, 16, 20))
            dr = ImageDraw.Draw(sheet)
            dr.text((2, 2), "MaxiMap.dc6 cels %d..%d   SCALE=%dx  * = used by our table  "
                            "YELLOW label = stone_px>0 (criteria = measure/automap_border.py)"
                    % (lo, hi - 1, SCALE), fill=(255, 240, 170))
            for k, n in enumerate(idxs):
                f = d.frames[n]
                im = Image.new("RGB", (f.width, f.height), (10, 10, 12))
                px = im.load()
                for y in range(f.height):
                    for x in range(f.width):
                        v = f.indices[y * f.width + x]
                        if v:
                            px[x, y] = palette[v][:3]
                im = im.resize((f.width * SCALE, f.height * SCALE), Image.NEAREST)
                cx = (k % COLS) * cw
                cy = 16 + (k // COLS) * ch
                sheet.paste(im, (cx + PAD, cy + LBL + PAD // 2))
                tag = "%d%s%s" % (n, "*" if (n in used) else "", "!" if (n in stone_set) else "")
                dr.text((cx + PAD, cy + 1), tag,
                        fill=(255, 255, 120) if (n in stone_set) else
                             ((140, 220, 255) if (n in used) else (150, 150, 160)))
            out = os.path.join(SHOT_DIR, "automapredo_maximap_sheet_%d_%d.png" % (lo, hi - 1))
            sheet.save(out)
            say("sheet -> %s (%dx%d)" % (out, sheet.width, sheet.height))

    with open(OUT_TXT, "w", encoding="utf-8", newline="") as fh:
        fh.write("\n".join(log) + "\n")
    say("log -> %s" % OUT_TXT)
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
