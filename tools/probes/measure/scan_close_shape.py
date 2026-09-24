# -*- coding: utf-8 -*-
"""判据资产：在已解包 DC6 全集里按**图形内容**穷举“X 形关闭按钮”（片 u53-closefix，2026-09-24）。

来历：片 `u53-closefix` 的一次性探针 `.ai-tmp/test/u53_scan_x.py`（当时用于证明“关闭图形在盘取不到”）；
本文件按 skill §3.5“判据资产（探针 / 驱动 / 量法脚本）⇒ 落 `tools/probes/` 并提交”提升入仓（逻辑未改，只换了落点）。

用法：`python tools/probes/measure/scan_close_shape.py <项目根>`
产物（写 `<root>/.ai-tmp/test/`）：`u53_x_candidates.tsv`（候选帧排名 + xiou/diag）、
`u53_x_sheet.png`（前 120 名的联络表，供人眼读形）、`u53_x_sheet_map.tsv`（格号 ↔ 排名）。

已知结果（本工程 2026-09-24）：448 个非字体 DC6 / 647 个 20..54px 帧，**最高 xiou = 0.481**
（物品图标 `items/inv1x1.DC6` 帧 1）⇒ **无 X 形帧**。若以后要找别的小图标，换 `x_iou()` 里的“理想形”即可。
"""

"""u53 片一次性探针：在已解包 DC6 全集里按**图形内容**穷举"X 形关闭按钮"。

判据（可复现）：
  ① 帧尺寸落在关闭按钮族附近（20..54 px 见方）且不透明像素 >= 24；
  ② X 相似度 xiou = |mask ∩ idealX| / |mask ∪ idealX|，idealX = 内容外接框的两条对角线（厚 = 外接框高/8）；
  ③ 另给 diag = 不透明像素里落在对角线带上的比例（0..1）。
输出：
  u53_x_candidates.tsv  —— 排名 / 文件 / 帧号 / w×h / 外接框 / xiou / diag
  u53_x_sheet.png       —— 前 120 名的联络表（12 列，格子外画深灰底 + 白分割线，便于人眼数格）
  u53_x_sheet_map.tsv   —— 格子号(行,列) ↔ 候选排名
用法：python .ai-tmp/test/u53_scan_x.py <项目根>
"""
import os
import sys

ROOT = sys.argv[1] if len(sys.argv) > 1 else r"c:\Work\Server\f-v2\clover-project-diablo2"
sys.path.insert(0, os.path.join(ROOT, "tools", "d2codec"))
import dc6  # noqa: E402

SRC = os.path.join(ROOT, "原版资源", "d2dc6")
OUT_TSV = os.path.join(ROOT, ".ai-tmp", "test", "u53_x_candidates.tsv")
OUT_SHEET = os.path.join(ROOT, ".ai-tmp", "test", "u53_x_sheet.png")
OUT_MAP = os.path.join(ROOT, ".ai-tmp", "test", "u53_x_sheet_map.tsv")


def x_iou(mask, w, h):
    """mask: list[bool] row-major. 返回 (xiou, diag, bbox) 。"""
    xs = [i % w for i, v in enumerate(mask) if v]
    ys = [i // w for i, v in enumerate(mask) if v]
    if not xs:
        return 0.0, 0.0, None
    x0, x1, y0, y1 = min(xs), max(xs), min(ys), max(ys)
    bw, bh = x1 - x0 + 1, y1 - y0 + 1
    thick = max(1.0, bh / 8.0)
    inter = union = diag_hit = 0
    span = max(1.0, float(bw - 1))
    for y in range(h):
        for x in range(w):
            v = mask[y * w + x]
            if not (x0 <= x <= x1 and y0 <= y <= y1):
                if v:
                    union += 1          # 框外的不透明像素算进并集（惩罚）
                continue
            dx = (x - x0) / span
            fy = y0 + dx * (bh - 1)
            d1 = abs(y - fy)
            d2 = abs(y - (y1 - dx * (bh - 1)))
            ideal = min(d1, d2) <= thick
            if v and ideal:
                inter += 1
                diag_hit += 1
            if v or ideal:
                union += 1
    return inter / float(union), diag_hit / float(max(1, sum(mask))), (x0, y0, bw, bh)


def main():
    pal = dc6.read_pl2(os.path.join(ROOT, "原版资源", "d2raw", "data", "global", "palette", "ACT1", "Pal.PL2"))
    cands = []
    files = 0
    for dirpath, _dn, fns in os.walk(SRC):
        for fn in sorted(fns):
            if not fn.lower().endswith(".dc6"):
                continue
            # 排除字体与中文文本位图（它们是"字形"，不是 UI 控件图形；实测 font30/font42 的
            # 汉字笔画会以 xiou=1.000 霸榜 ⇒ 必须先剔除，否则真控件候选被挤掉）。
            rel = os.path.join(dirpath, fn)
            if "FONT" in rel.upper() or os.sep + "chi" + os.sep in rel:
                continue
            full = rel
            files += 1
            try:
                d = dc6.parse(open(full, "rb").read())
            except Exception:
                continue
            for fi, f in enumerate(d.frames):
                w, h = f.width, f.height
                if not (20 <= w <= 54 and 16 <= h <= 54):
                    continue
                mask = [f.indices[i] != 0 for i in range(w * h)]
                op = sum(1 for v in mask if v)
                if op < 24:
                    continue
                iou, diag, bbox = x_iou(mask, w, h)
                cands.append(dict(path=full.replace(SRC + os.sep, ""), frame=fi, w=w, h=h,
                                  op=op, iou=iou, diag=diag, bbox=bbox, f=f, pal=pal))
    cands.sort(key=lambda c: -c["iou"])

    with open(OUT_TSV, "w", encoding="utf-8") as fp:
        fp.write("rank\tfile\tframe\tw\th\topaque\txiou\tdiag\tbbox_x\ty\tw\th\n")
        for i, c in enumerate(cands):
            b = c["bbox"] or (0, 0, 0, 0)
            fp.write("%d\t%s\t%d\t%d\t%d\t%d\t%.3f\t%.3f\t%d\t%d\t%d\t%d\n"
                     % (i, c["path"], c["frame"], c["w"], c["h"], c["op"], c["iou"], c["diag"],
                        b[0], b[1], b[2], b[3]))

    top = cands[:120]
    cols, cell, scale, pad = 12, 54, 2, 3
    cw = cell * scale + pad * 2
    rows = (len(top) + cols - 1) // cols
    W, H = cols * cw + pad, rows * cw + pad
    rgba = bytearray(W * H * 4)
    for i in range(W * H):
        rgba[i * 4 + 0] = 30
        rgba[i * 4 + 1] = 30
        rgba[i * 4 + 2] = 34
        rgba[i * 4 + 3] = 255
    with open(OUT_MAP, "w", encoding="utf-8") as mp:
        mp.write("cell\trow\tcol\trank\tfile\tframe\tw\th\txiou\n")
        for idx, c in enumerate(top):
            r, col = idx // cols, idx % cols
            ox, oy = pad + col * cw + pad, pad + r * cw + pad
            px = dc6.frame_rgba(c["f"], c["pal"])
            for y in range(c["h"]):
                for x in range(c["w"]):
                    a = px[(y * c["w"] + x) * 4 + 3]
                    if a == 0:
                        continue
                    for sy in range(scale):
                        for sx in range(scale):
                            tx, ty = ox + x * scale + sx, oy + y * scale + sy
                            if tx >= W or ty >= H:
                                continue
                            o = (ty * W + tx) * 4
                            rgba[o] = px[(y * c["w"] + x) * 4 + 0]
                            rgba[o + 1] = px[(y * c["w"] + x) * 4 + 1]
                            rgba[o + 2] = px[(y * c["w"] + x) * 4 + 2]
                            rgba[o + 3] = 255
            mp.write("%d\t%d\t%d\t%d\t%s\t%d\t%d\t%d\t%.3f\n"
                     % (idx, r, col, idx, c["path"], c["frame"], c["w"], c["h"], c["iou"]))
    dc6.write_png_rgba(OUT_SHEET, bytes(rgba), W, H)
    print("files=%d candidates=%d sheet=%dx%d(rows=%d)" % (files, len(cands), W, H, rows))
    for i, c in enumerate(cands[:15]):
        print("%2d xiou=%.3f diag=%.3f %dx%d f%d %s"
              % (i, c["iou"], c["diag"], c["w"], c["h"], c["frame"], c["path"]))


main()
