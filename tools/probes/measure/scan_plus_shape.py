# -*- coding: utf-8 -*-
"""判据资产：在已解包 DC6 全集里按**图形内容**穷举“+（加号 / 十字）形帧”
（片 u52play，2026-09-24；任务 = 给「人物属性面板的加点按钮」找原版帧）。

来历：与 `tools/probes/measure/scan_close_shape.py`（X 形）同族 —— 同一套「按图形内容穷举」
做法，只把“理想形”从两条对角线换成十字。目的有两个：
  ① **独立复核**「属性面板的加点箭头在盘上到底有没有可用的帧」；
  ② 若原版加点按钮真是一个「+」字按钮，本脚本给出它落在哪个 DC6 的哪一帧（排名 + 联络表）。

判据口径（可复现）：
  每帧算**两个**掩码，各自与「理想十字」比 IoU（都按内容外接框归一化，框外不透明像素计入并集 ⇒ 罚分）：
    · A（alpha 掩码）= alpha > 0        —— 适用「不透明前景画在透明底上」的图标（与 X 扫描同口径）；
    · D（暗色掩码）= alpha > 0 且 luma < 100 —— 适用**刻在底座上的暗形**（如 `PANEL/menubutton.DC6`
      的箭头：alpha 是整块不透明石板，箭头本身是**暗色**，用 alpha 扫必然扫不出形状）。
  理想十字：外接框内、以框中心为中轴的横竖两臂，臂厚 = max(1, round(min(bw,bh)/3))。
  plus_iou = |mask ∩ ideal| / |mask ∪ ideal|；cross = 掩码像素里落在理想十字带内的比例。

对照项（每次运行都会打印，证明这套量法真的在判“十字”）：
  · `ideal_plus`   —— 合成十字      期望 ≳ 0.9；
  · `ideal_x`      —— 合成 X         期望显著低于十字（若两者都高 ⇒ 量法失效）；
  · `ideal_block`  —— 合成实心方块   期望 ≈ 掩码面积占比（≈1/3 量级，不能冒充十字）。
  ⛔ 没有对照项读数的排名**不许当判据**。

用法：`python tools/probes/measure/scan_plus_shape.py <项目根>`
产物（写 `<root>/.ai-tmp/test/`）：
  `u52_plus_candidates.tsv`（候选帧排名 + 两个 IoU / cross）
  `u52_plus_sheet.png`（前 120 名的联络表，供人眼读形）
  `u52_plus_sheet_map.tsv`（格号 ↔ 排名）
"""
import os
import sys

ROOT = sys.argv[1] if len(sys.argv) > 1 else r"c:\Work\Server\f-v2\clover-project-diablo2"
# WRITES => a relative ROOT silently writes into whichever tree the process CWD points at (from the
# workspace root that is `c:\Work\Server\f-v2\.ai-tmp\test\`). Refuse instead of guessing.
if not os.path.isabs(ROOT):
    sys.stderr.write("REFUSING: ROOT must be an absolute path (got %r, cwd=%r)\n" % (ROOT, os.getcwd()))
    sys.exit(2)
sys.path.insert(0, os.path.join(ROOT, "tools", "d2codec"))
import dc6  # noqa: E402

SRC = os.path.join(ROOT, "原版资源", "d2dc6")
OUT_TSV = os.path.join(ROOT, ".ai-tmp", "test", "u52_plus_candidates.tsv")
OUT_SHEET = os.path.join(ROOT, ".ai-tmp", "test", "u52_plus_sheet.png")
OUT_MAP = os.path.join(ROOT, ".ai-tmp", "test", "u52_plus_sheet_map.tsv")

W_MIN, H_MIN, W_MAX, H_MAX = 8, 8, 44, 44
OP_MIN = 24
DARK_LUMA = 100
DARK_MIN = 24


def ideal_plus(bw, bh):
    """外接框内的理想十字掩码（row-major，长度 bw*bh）。"""
    t = max(1, int(round(min(bw, bh) / 3.0)))
    cx, cy = (bw - 1) / 2.0, (bh - 1) / 2.0
    out = []
    for y in range(bh):
        for x in range(bw):
            out.append(abs(x - cx) <= t / 2.0 or abs(y - cy) <= t / 2.0)
    return out


def ideal_x(bw, bh):
    """同样的外接框内的理想 X（两条对角线，厚 = bw/8）——**对照项**，不该与十字同高。"""
    t = max(1.0, bw / 8.0)
    out = []
    for y in range(bh):
        for x in range(bw):
            dx = x / max(1.0, bw - 1.0)
            d1 = abs(y - dx * (bh - 1))
            d2 = abs(y - ((bh - 1) - dx * (bh - 1)))
            out.append(min(d1, d2) <= t)
    return out


def iou_of(mask, w, h, ideal_fn):
    """返回 (iou, inside_ratio, bbox)。框外的不透明像素计入并集（罚分）。"""
    xs = [i % w for i, v in enumerate(mask) if v]
    ys = [i // w for i, v in enumerate(mask) if v]
    if not xs:
        return 0.0, 0.0, None
    x0, x1, y0, y1 = min(xs), max(xs), min(ys), max(ys)
    bw, bh = x1 - x0 + 1, y1 - y0 + 1
    ideal = ideal_fn(bw, bh)
    inter = union = inside = total = 0
    for y in range(h):
        for x in range(w):
            v = mask[y * w + x]
            if v:
                total += 1
            if not (x0 <= x <= x1 and y0 <= y <= y1):
                if v:
                    union += 1
                continue
            iv = ideal[(y - y0) * bw + (x - x0)]
            if v and iv:
                inter += 1
                inside += 1
            if v or iv:
                union += 1
    return inter / float(max(1, union)), inside / float(max(1, total)), (x0, y0, bw, bh)


def controls():
    """对照项：合成十字 / 合成 X / 合成实心块，走**同一个** iou_of。"""
    out = []
    for name, fn in (("plus", "plus"), ("x", "x")):
        w = h = 24
        t = max(1, int(round(min(w, h) / 3.0)))
        cx, cy = (w - 1) / 2.0, (h - 1) / 2.0
        mask = []
        for y in range(h):
            for x in range(w):
                if fn == "plus":
                    mask.append(abs(x - cx) <= t / 2.0 or abs(y - cy) <= t / 2.0)
                else:
                    mask.append(abs(x - y) <= 1 or abs((w - 1 - x) - y) <= 1)
        i_plus, in_plus, _ = iou_of(mask, w, h, ideal_plus)
        i_x, in_x, _ = iou_of(mask, w, h, ideal_x)
        out.append((name, i_plus, i_x, in_plus, in_x))
    # 实心块：十字 IoU 应≈十字面积/块面积（远小于 1）
    w = h = 24
    mask = [True] * (w * h)
    i_plus, in_plus, _ = iou_of(mask, w, h, ideal_plus)
    i_x, in_x, _ = iou_of(mask, w, h, ideal_x)
    out.append(("block", i_plus, i_x, in_plus, in_x))
    return out


def main():
    pal_path = os.path.join(ROOT, "原版资源", "d2raw", "data", "global", "palette", "ACT1", "Pal.PL2")
    pal = dc6.read_pl2(pal_path)
    cands = []
    files = 0
    for dirpath, _dn, fns in os.walk(SRC):
        for fn in sorted(fns):
            if not fn.lower().endswith(".dc6"):
                continue
            full = os.path.join(dirpath, fn)
            # 与 X 扫描同口径：字体与中文文本位图是「字形」，不是控件图形。
            if "FONT" in full.upper() or os.sep + "chi" + os.sep in full:
                continue
            files += 1
            try:
                d = dc6.parse(open(full, "rb").read())
            except Exception:
                continue
            for fi, f in enumerate(d.frames):
                w, h = f.width, f.height
                if not (W_MIN <= w <= W_MAX and H_MIN <= h <= H_MAX):
                    continue
                rgba = dc6.frame_rgba(f, pal)
                mask_a = [rgba[i * 4 + 3] > 0 for i in range(w * h)]
                op = sum(1 for v in mask_a if v)
                lum = [(rgba[i * 4] * 299 + rgba[i * 4 + 1] * 587 + rgba[i * 4 + 2] * 114) // 1000
                       for i in range(w * h)]
                mask_d = [mask_a[i] and lum[i] < DARK_LUMA for i in range(w * h)]
                dk = sum(1 for v in mask_d if v)
                if op < OP_MIN and dk < DARK_MIN:
                    continue
                ia, ca, bba = iou_of(mask_a, w, h, ideal_plus)
                idark, cdark, bbd = iou_of(mask_d, w, h, ideal_plus) if dk >= DARK_MIN else (0.0, 0.0, None)
                best = max(ia, idark)
                cands.append(dict(path=full.replace(SRC + os.sep, ""), frame=fi, w=w, h=h,
                                  op=op, dk=dk, ia=ia, ca=ca, idark=idark, cdark=cdark,
                                  best=best, f=f, pal=pal))
    cands.sort(key=lambda c: -c["best"])

    print("== controls (same iou_of) ==")
    for name, ip, ix, inp, inx in controls():
        print("  %-6s plus_iou=%.3f x_iou=%.3f  inside(plus)=%.3f inside(x)=%.3f"
              % (name, ip, ix, inp, inx))

    with open(OUT_TSV, "w", encoding="utf-8") as fp:
        fp.write("rank\tfile\tframe\tw\th\topaque\tdark\tplus_iou_alpha\tcross_alpha\tplus_iou_dark\tcross_dark\tbest\n")
        for i, c in enumerate(cands):
            fp.write("%d\t%s\t%d\t%d\t%d\t%d\t%d\t%.3f\t%.3f\t%.3f\t%.3f\t%.3f\n"
                     % (i, c["path"], c["frame"], c["w"], c["h"], c["op"], c["dk"],
                        c["ia"], c["ca"], c["idark"], c["cdark"], c["best"]))

    top = cands[:120]
    cols, cell, scale, pad = 12, 44, 2, 3
    cw = cell * scale + pad * 2
    rows = max(1, (len(top) + cols - 1) // cols)
    W, H = cols * cw + pad, rows * cw + pad
    rgba = bytearray(W * H * 4)
    for i in range(W * H):
        rgba[i * 4:i * 4 + 4] = bytes((30, 30, 34, 255))
    with open(OUT_MAP, "w", encoding="utf-8") as mp:
        mp.write("cell\trow\tcol\trank\tfile\tframe\tw\th\tbest\n")
        for idx, c in enumerate(top):
            r, col = idx // cols, idx % cols
            ox, oy = pad + col * cw + pad, pad + r * cw + pad
            px = dc6.frame_rgba(c["f"], c["pal"])
            for y in range(c["h"]):
                for x in range(c["w"]):
                    if px[(y * c["w"] + x) * 4 + 3] == 0:
                        continue
                    for sy in range(scale):
                        for sx in range(scale):
                            tx, ty = ox + x * scale + sx, oy + y * scale + sy
                            if tx >= W or ty >= H:
                                continue
                            o = (ty * W + tx) * 4
                            rgba[o:o + 3] = px[(y * c["w"] + x) * 4:(y * c["w"] + x) * 4 + 3]
                            rgba[o + 3] = 255
            mp.write("%d\t%d\t%d\t%d\t%s\t%d\t%d\t%d\t%.3f\n"
                     % (idx, r, col, idx, c["path"], c["frame"], c["w"], c["h"], c["best"]))
    dc6.write_png_rgba(OUT_SHEET, bytes(rgba), W, H)
    print("files=%d candidates=%d sheet=%dx%d(rows=%d)" % (files, len(cands), W, H, rows))
    for i, c in enumerate(cands[:15]):
        print("%2d best=%.3f (alpha %.3f / dark %.3f) %dx%d f%d %s"
              % (i, c["best"], c["ia"], c["idark"], c["w"], c["h"], c["frame"], c["path"]))


main()
