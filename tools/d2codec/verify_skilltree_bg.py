# -*- coding: utf-8 -*-
"""技能树底图**未改动自证**：把原版 DC6 重新解一遍，与工程内 PNG **逐像素**比对。

判据（交付用）：`client/Assets/Resources/Clover/D2/UI/Panel/skltree_{a,b,n,p,s}_back_{0..3}.png`
必须与「原版 `SPELLS/skltree_{cls}_back.DC6` 用 ACT1 调色板解出 + tile 拼装（画布宽 320）
+ 纵切成 320×432 页」的结果**逐像素全等**（差异 0）—— 即底图一个字都没被改过。

口径（与生成器一致）：
  · 调色板 `原版资源/d2raw/data/global/palette/ACT1/Pal.PL2`（依据见 `export_d2ui.py` 的 PL2 表）
  · 拼装：`dc6.compose_frame(frames, canvas_w=320)`，再按 432 高切片（= `group_panels` 的口径）

用法：python tools/d2codec/verify_skilltree_bg.py        # 退出码 0 = 全等
"""
import hashlib
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import dc6

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(os.path.dirname(HERE))
CLIENT = os.path.join(REPO, "client")
CLS = ("a", "b", "n", "p", "s")
DC6_DIR = os.path.join(REPO, "原版资源", "d2dc6", "data", "global", "ui", "SPELLS")
PL2 = os.path.join(REPO, "原版资源", "d2raw", "data", "global", "palette", "ACT1", "Pal.PL2")
PNG_DIR = os.path.join(CLIENT, "Assets", "Resources", "Clover", "D2", "UI", "Panel")
PAGE_W, PAGE_H = 320, 432


def main():
    pal = dc6.read_pl2(PL2)
    total = 0
    bad = 0
    print("| 页 | md5（工程内 PNG） | 重新解出的像素差异 |")
    print("|---|---|---|")
    for cls in CLS:
        d = dc6.parse(open(os.path.join(DC6_DIR, "skltree_%s_back.DC6" % cls), "rb").read())
        comp, cw = dc6.compose_frame(d.frames, 320)
        if cw != PAGE_W:
            print("!! %s 拼装画布宽 = %s（期望 %d）" % (cls, cw, PAGE_W))
            bad += 1
            continue
        pages = comp.height // PAGE_H
        for k in range(pages):
            sub = dc6.Frame(PAGE_W, PAGE_H, 0, 0, 0,
                            comp.indices[k * PAGE_H * PAGE_W:(k + 1) * PAGE_H * PAGE_W])
            mine = dc6.frame_rgba(sub, pal)
            path = os.path.join(PNG_DIR, "skltree_%s_back_%d.png" % (cls, k))
            w, h, theirs = dc6._read_png_rgba(path)
            total += 1
            if (w, h) != (PAGE_W, PAGE_H):
                print("!! %s 尺寸 %dx%d" % (path, w, h))
                bad += 1
                continue
            diff = 0
            for i in range(0, len(mine), 4):
                if mine[i:i + 4] != theirs[i:i + 4]:
                    diff += 1
            md5 = hashlib.md5(open(path, "rb").read()).hexdigest()
            if diff:
                bad += 1
            print("| %s_%d | %s | %d / %d |" % (cls, k, md5, diff, PAGE_W * PAGE_H))
    print("\n共 %d 页；逐像素全等 %d；不一致 %d" % (total, total - bad, bad))
    return 0 if bad == 0 else 2


if __name__ == "__main__":
    sys.exit(main())
