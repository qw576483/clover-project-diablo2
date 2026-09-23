#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""
hud_measure.py —— 底部 HUD 的**像素量法**（原版实机图 vs 本项目实机图 → 同一 art 坐标）

为什么要它（用户 2026-09-23「你 ui 全是问题」）：
  两张图分辨率不同（原版 = 800x600 视频帧；本项目 = 1920x1080 画布）。
  直接比图像像素没有意义 ⇒ 两边都换算到**原版控制面板 `ControlPanel.png`（948x160，
  原版本体素材）的 art 坐标系**：
    · artX = 面板左沿起 0 的原版像素（面板宽 948、中线 474）
    · PY   = 距**画布底边**的高度（= 原版 prefab 的 y 口径）= 138.7 − artY(自面板顶)
             （原版面板底边比画布底边低 21.3 ⇒ 可见高 138.7）

标定（不是"看着像"）：
  · **本项目**：`UiLayoutGame.HudBgPos/HudBgSize` ⇒ artX=(sx−106.8)/1.8、artY=(sy−792)/1.8，
    由常数直接推出、可离线核对（scale=1.8、art_x0=106.8、art_y0=792）。
  · **原版视频帧**：`--calibrate` 把 `ControlPanel.png` 缩放到候选 (scale,x0,y0) 贴在图像的
    **低彩度像素**（石雕/金框）上取平均绝对差最小者。实测最优 (0.776, −66, 361)，
    与理论 (631/800=0.789, −58) 差 1.7%（视频压缩 + 底部裁掉 ~12 art px）⇒ 标定可接受，
    表里单位一律 art px，**两边同口径**，差值可用。

量法（每个元素 = 一个宽松的 art 窗口 + 掩码 + 行/列投影取"显著带"）：
  投影阈值 = 峰值 × 0.45（蝶形/圆盘/横条都能取到主体），窗口只排除明显不相关的东西。
  窗口是**宽松的**，只用于"别把别的元素算进来"，元素自身的几何完全由像素决定。

用法：
    python tools/probes/measure/hud_measure.py --calibrate <image>
    python tools/probes/measure/hud_measure.py --both [--tsv out.tsv]
    python tools/probes/measure/hud_measure.py --image X --scale S --art-x0 X --art-y0 Y --tag T
"""
import argparse
import os
import sys

import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
PROJECT = os.path.abspath(os.path.join(HERE, "..", "..", ".."))

ART_W, ART_H, CENTER = 948.0, 160.0, 474.0
VISIBLE_H = ART_H - 21.3            # 138.7

# 本项目：画布 1920x1080、K=1.8、面板底边贴画布底边（`UiLayoutGame.HudBgPos/HudBgSize`）
OURS = dict(scale=1.8, x0=960.0 + (0.0 - CENTER) * 1.8, y0=792.0)
# 原版基线图：`--calibrate 策划/基线图/原版_实机_UI基准_20260923.png` 的拟合结果
ORIG = dict(scale=0.776, x0=-66.0, y0=361.0)

DEFAULT_ART = os.path.join(PROJECT, "client", "Assets", "Resources", "Clover", "D2", "UI",
                           "Panel", "ControlPanel.png")

# 元素 → (掩码名, art 窗口 (x0,x1,PY0,PY1))；窗口宽松，只排除明显不相干的元素
WINDOWS = [
    ("orb_life",    "red",   (40.0, 280.0, 0.0, 160.0)),
    ("orb_mana",    "blue",  (670.0, 908.0, 0.0, 160.0)),
    ("label_life",  "warm",  (40.0, 320.0, 110.0, 215.0)),    # 球**上方**那行字（可越出面板顶沿）
    ("label_mana",  "warm",  (655.0, 908.0, 110.0, 215.0)),
    ("minipanel",   "white", (330.0, 630.0, 55.0, 138.7)),    # 8 键行（在格带之上）
    ("band",        "dark",  (180.0, 760.0, 10.0, 60.0)),     # 格带暗格内芯
    ("expbar_low",  "gold",  (180.0, 780.0, 0.0, 45.0)),      # 经验条贴面板底（本项目口径）
    ("expbar_mid",  "gold",  (180.0, 780.0, 45.0, 95.0)),     # 经验条在格带之上（原版口径？）
    ("expbar_track","white", (180.0, 780.0, 0.0, 95.0)),      # 经验条白轨
    ("belt",        "sat",   (560.0, 780.0, 5.0, 62.0)),      # 腰带格（药水色）
]


class Cal(object):
    def __init__(self, scale, x0, y0, tag):
        self.s, self.x0, self.y0, self.tag = scale, x0, y0, tag

    def sx(self, art_x):
        return self.x0 + art_x * self.s

    def sy(self, py):
        """PY（距画布底边）→ 屏幕 y。"""
        return self.y0 + (VISIBLE_H - py) * self.s

    def art_x(self, screen_x):
        return (screen_x - self.x0) / self.s

    def py(self, screen_y):
        return VISIBLE_H - (screen_y - self.y0) / self.s


def masks(a):
    R, G, B = a[:, :, 0], a[:, :, 1], a[:, :, 2]
    mx, mn = a.max(2), a.min(2)
    return {
        "red": (R - np.maximum(G, B) > 30) & (R > 45),
        "blue": (B - np.maximum(R, G) > 25) & (B > 45),
        "warm": (R > 110) & (R - B > 45) & (G - B > 12),
        "white": (mn > 100) & (mx - mn < 60),
        "gold": (R > 150) & (R - B > 60) & (G - B > 30),
        "dark": (mx < 70),
        "sat": ((mx - mn) > 60) & (mx > 70),
    }


def _band(profile, frac=0.45, min_run=2):
    """取投影的"显著带"：>= 峰值*frac 的连续段（取最长的一段）。"""
    if profile.max() <= 0:
        return None
    sel = profile >= max(1.0, profile.max() * frac)
    idx = np.nonzero(sel)[0]
    if len(idx) < min_run:
        return None
    groups, s, p = [], idx[0], idx[0]
    for i in idx[1:]:
        if i > p + 3:
            groups.append((s, p))
            s = i
        p = i
    groups.append((s, p))
    return max(groups, key=lambda g: g[1] - g[0])


def measure(img, cal):
    a = np.asarray(img.convert("RGB")).astype(np.float32)
    H, W = a.shape[:2]
    M = masks(a)
    rows = []
    for name, kind, (ax0, ax1, py0, py1) in WINDOWS:
        sx0, sx1 = int(round(cal.sx(ax0))), int(round(cal.sx(ax1)))
        sy0, sy1 = int(round(cal.sy(py1))), int(round(cal.sy(py0)))
        sx0, sx1 = max(0, sx0), min(W, sx1)
        sy0, sy1 = max(0, sy0), min(H, sy1)
        if sx1 - sx0 < 4 or sy1 - sy0 < 4:
            continue
        m = np.zeros((H, W), bool)
        m[sy0:sy1, sx0:sx1] = M[kind][sy0:sy1, sx0:sx1]
        if m.sum() < 20:
            continue
        rr = _band(m.sum(1))
        cc = _band(m.sum(0))
        if not rr or not cc:
            continue
        y0, y1 = rr
        x0, x1 = cc
        rows.append(dict(
            name=name, n=int(m.sum()),
            x_art0=cal.art_x(x0), x_art1=cal.art_x(x1),
            cx_art=cal.art_x((x0 + x1) * 0.5), w_art=(x1 - x0 + 1) / cal.s,
            py_lo=cal.py(y1), py_hi=cal.py(y0),
            cy_py=cal.py((y0 + y1) * 0.5), h_py=(y1 - y0 + 1) / cal.s,
        ))
    return rows


def fmt(rows, tag):
    print("=== %s ===" % tag)
    print("element\tn\tartX0\tartX1\tartXc\tw_art\tPYlo\tPYhi\tPYc\th_py")
    for r in rows:
        print("%s\t%d\t%.1f\t%.1f\t%.1f\t%.1f\t%.1f\t%.1f\t%.1f\t%.1f" % (
            r["name"], r["n"], r["x_art0"], r["x_art1"], r["cx_art"], r["w_art"],
            r["py_lo"], r["py_hi"], r["cy_py"], r["h_py"]))


def table(rows_all):
    print("\n=== 对照表（同 art 坐标；artX = 面板左沿起原版px，PY = 距画布底边原版px）===")
    print("element\toriginal\tours\tdiff")
    keys = []
    for tag in rows_all:
        for r in rows_all[tag]:
            if r["name"] not in keys:
                keys.append(r["name"])
    for name in keys:
        o = next((r for r in rows_all.get("original", []) if r["name"] == name), None)
        u = next((r for r in rows_all.get("ours", []) if r["name"] == name), None)
        if not o or not u:
            print("%s\t%s\t%s\t-缺失-" % (name, _s(o), _s(u)))
            continue
        print("%s\t%s\t%s\tdCX=%+.1f dCY_PY=%+.1f dW=%+.1f dH=%+.1f" % (
            name, _s(o), _s(u), u["cx_art"] - o["cx_art"], u["cy_py"] - o["cy_py"],
            u["w_art"] - o["w_art"], u["h_py"] - o["h_py"]))
    # 本项目内部断言（与本项目的布局常量对，不需要原版图 ⇒ 可进 uicheck）
    print("\n=== 本项目回归断言（量法 vs `UiLayoutGame` 常量，容差 6 原版px）===")
    exp = {"orb_life": (158.0 - 108 / 2, 108.0), "orb_mana": (773.6 - 108 / 2, 108.0),
           "minipanel": (474.0 - 173 / 2, 173.0), "band": (315.0, 228.0)}
    for n, (x0, w) in exp.items():
        r = next((r for r in rows_all.get("ours", []) if r["name"] == n), None)
        if not r:
            print("%s\tFAIL 未量到" % n)
            continue
        d0, dw = r["x_art0"] - x0, r["w_art"] - w
        ok = abs(d0) <= 6 and abs(dw) <= 6
        print("%s\t%s artX0 量%.1f / 期望%.1f (差%+.1f)  w 量%.1f / 期望%.1f (差%+.1f)"
              % (n, "PASS" if ok else "FAIL", r["x_art0"], x0, d0, r["w_art"], w, dw))


def _s(r):
    if not r:
        return "-"
    return "X%.1f..%.1f(c%.1f) PY%.1f..%.1f(c%.1f) w%.1f" % (
        r["x_art0"], r["x_art1"], r["cx_art"], r["py_lo"], r["py_hi"], r["cy_py"], r["w_art"])


def calibrate(img, art_path, s_lo=0.74, s_hi=0.84, s_step=0.004):
    sca = np.asarray(img.convert("RGB")).astype(np.float32)
    H, W = sca.shape[:2]
    low_chroma = (sca.max(2) - sca.min(2)) < 40
    art = Image.open(art_path).convert("RGBA")
    best = None
    s = s_lo
    while s <= s_hi + 1e-9:
        aw, ah = int(ART_W * s), int(ART_H * s)
        ar = np.asarray(art.resize((aw, ah), Image.LANCZOS)).astype(np.float32)
        al = ar[:, :, 3] > 200
        for ay in range(344, 362):
            for ax in range(-80, -40, 2):
                ys0, xs0 = max(0, ay), max(0, ax)
                ys1, xs1 = min(H, ay + ah), min(W, ax + aw)
                if ys1 - ys0 < 60 or xs1 - xs0 < 200:
                    continue
                m = al[ys0 - ay:ys1 - ay, xs0 - ax:xs1 - ax] & low_chroma[ys0:ys1, xs0:xs1]
                if m.sum() < 3000:
                    continue
                d = float(np.abs(sca[ys0:ys1, xs0:xs1][m]
                                 - ar[ys0 - ay:ys1 - ay, xs0 - ax:xs1 - ax][:, :, 0:3][m]).mean())
                if best is None or d < best[3]:
                    best = (round(s, 4), float(ax), float(ay), d, int(m.sum()))
        s += s_step
    return best


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--calibrate")
    ap.add_argument("--art", default=DEFAULT_ART)
    ap.add_argument("--image")
    ap.add_argument("--scale", type=float)
    ap.add_argument("--art-x0", type=float)
    ap.add_argument("--art-y0", type=float)
    ap.add_argument("--tag", default="image")
    ap.add_argument("--both", action="store_true")
    ap.add_argument("--ours", default=os.path.join(PROJECT, ".ai-tmp", "screenshots",
                                                   "hudredo_ours_before.png"))
    ap.add_argument("--tsv")
    args = ap.parse_args()

    if args.calibrate:
        c = calibrate(Image.open(args.calibrate), args.art)
        print("calibrate %s -> scale=%.4f art_x0=%.1f art_y0=%.1f score=%.2f n=%d" %
              (args.calibrate, c[0], c[1], c[2], c[3], c[4]))
        return 0

    if args.both:
        base = os.path.join(PROJECT, "策划", "基线图", "原版_实机_UI基准_20260923.png")
        rows_all = {}
        rows_all["original"] = measure(Image.open(base), Cal(tag="original", **ORIG))
        fmt(rows_all["original"], "original  " + os.path.basename(base))
        if os.path.exists(args.ours):
            rows_all["ours"] = measure(Image.open(args.ours), Cal(tag="ours", **OURS))
            fmt(rows_all["ours"], "ours  " + os.path.basename(args.ours))
        table(rows_all)
        if args.tsv:
            with open(args.tsv, "w", encoding="utf-8") as f:
                f.write("tag\telement\tn\tartX0\tartX1\tartXc\tw_art\tPYlo\tPYhi\tPYc\th_py\n")
                for tag, rows in rows_all.items():
                    for r in rows:
                        f.write("%s\t%s\t%d\t%.1f\t%.1f\t%.1f\t%.1f\t%.1f\t%.1f\t%.1f\t%.1f\n" % (
                            tag, r["name"], r["n"], r["x_art0"], r["x_art1"], r["cx_art"],
                            r["w_art"], r["py_lo"], r["py_hi"], r["cy_py"], r["h_py"]))
        return 0

    cal = Cal(args.scale, args.art_x0, args.art_y0, args.tag)
    fmt(measure(Image.open(args.image), cal), args.tag)
    return 0


if __name__ == "__main__":
    sys.exit(main())
