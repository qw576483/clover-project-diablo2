#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
btn_plate_luma.py —— 量「原版按钮底图」的石牌亮度，产出 btn_plate_luma.tsv。

为什么需要它（★ btn-label-fix，2026-09-23）：
    实机缺陷「传送点面板目的地按钮上的字读不出来」的**量化判据**需要两个数：
      ① 按钮底图石牌有多亮（＝文字的"背景"）；
      ② 文字色相对它的对比度够不够。
    ① 只能从素材像素量（uicheck 是无图像库的控制台宿主，不引 PNG 解码）⇒
    由本脚本把测量结果写成 `btn_plate_luma.tsv`，uicheck 读该表 + 工程里的配色常量算 ②。

量法（口径，改了就重新跑本脚本）：
    · 取样区 = PNG 的**内区** x ∈ [22%, 78%]、y ∈ [25%, 75%]
      —— 避开四周的金色雕花外框与左右两颗蓝宝石（它们不是"文字背后的石牌"）；
    · 只统计 alpha > 200 的像素（透明/半透明边不算）；
    · 亮度 = WCAG 2.1 相对亮度 0.2126·R + 0.7152·G + 0.0722·B（sRGB 分量 0..1），
      ⛔ 不做 gamma 线性化（本表给的是 sRGB 域的亮度，uicheck 侧再按 WCAG 公式线性化）。

复跑：
    cd c:/Work/Server/f-v2/clover-project-diablo2
    python tools/probes/measure/btn_plate_luma.py
"""

import os
import statistics
import sys

try:
    from PIL import Image
except ImportError:  # pragma: no cover
    sys.stderr.write("需要 Pillow：python -m pip install pillow\n")
    raise SystemExit(2)

# 工程根 = 本文件上溯三级（<root>/tools/probes/measure/x.py）
ROOT = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", ".."))
ART_ROOT = os.path.join(ROOT, "client", "Assets", "Resources", "Clover", "D2", "UI", "Menu")

# 本屏（传送点面板 = 原版中等按钮 128×35）用的那张 + 宽按钮（272×35，菜单屏用）作对照
ARTS = [
    ("D2/UI/Menu/btn_med_normal.png", "btn_med_normal.png"),
    ("D2/UI/Menu/btn_wide_normal.png", "btn_wide_normal.png"),
]

HEADER = [
    "# 原版按钮底图「石牌亮度」实测（**生成物，不要手改**）",
    "# 生成命令：python tools/probes/measure/btn_plate_luma.py",
    "# 量法：内区 x 22%..78% / y 25%..75%（避开金色雕花外框与两侧宝石），只统计 alpha>200 的像素；",
    "#       亮度 = sRGB 相对亮度 0.2126R+0.7152G+0.0722B（分量 0..1，未做 gamma 线性化）",
    "# 列：art \t mean_srgb \t median_srgb \t p90_srgb \t pixels",
]


def measure(path):
    im = Image.open(path).convert("RGBA")
    w, h = im.size
    x0, x1 = int(w * 0.22), int(w * 0.78)
    y0, y1 = int(h * 0.25), int(h * 0.75)
    vals = []
    for y in range(y0, y1):
        for x in range(x0, x1):
            r, g, b, a = im.getpixel((x, y))
            if a > 200:
                vals.append((0.2126 * r + 0.7152 * g + 0.0722 * b) / 255.0)
    vals.sort()
    return {
        "size": (w, h),
        "n": len(vals),
        "mean": sum(vals) / len(vals),
        "median": statistics.median(vals),
        "p90": vals[int(len(vals) * 0.9)],
    }


def main():
    out = os.path.join(os.path.dirname(os.path.abspath(__file__)), "btn_plate_luma.tsv")
    lines = list(HEADER)
    lines.append("art\tmean_srgb\tmedian_srgb\tp90_srgb\tpixels")
    for rel, name in ARTS:
        p = os.path.join(ART_ROOT, name)
        if not os.path.exists(p):
            sys.stderr.write("缺素材：" + p + "\n")
            return 1
        m = measure(p)
        lines.append("%s\t%.3f\t%.3f\t%.3f\t%d" % (rel, m["mean"], m["median"], m["p90"], m["n"]))
        print("%-32s %dx%d  mean=%.3f median=%.3f p90=%.3f (n=%d)"
              % (name, m["size"][0], m["size"][1], m["mean"], m["median"], m["p90"], m["n"]))
    with open(out, "w", encoding="utf-8", newline="\n") as f:
        f.write("\n".join(lines) + "\n")
    print("写出 " + out)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
