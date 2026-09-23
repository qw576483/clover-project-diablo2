# -*- coding: utf-8 -*-
"""HUD 控制面板**落位**判据（hud-redo2 片，2026-09-23）。

判据（一条，机械可判）：**底图的不透明内容必须收口在画布底边**。

依据 / 为什么需要它
------------------
`ControlPanel.png`（948×160，原版 `ctrlpnl7.DC6` 解出）的**不透明内容只到第 138 行**
（第 139..159 行 alpha 全 0）。原版 `ControlPanel.prefab` 的 `Background` 是
`pivot(0.5,0) + pos.y = -21.3` ⇒ 图框底边比屏幕底边低 21.3px ⇒ **那 21.3 行透明像素落到屏幕外**，
画面内容正好在屏幕底边收口。

若给整组 HUD 加一个 `HudBaseLift = 21.3` 的"贴底抬升"（让图框底边贴住画布底边），
透明尾巴会被抬进画面 ⇒ **屏幕最底 38.34 画布px（21.3×1.8）露出游戏世界**，且 HUD 每个元素
都比原版高 38.34 画布px。这就是本片修掉的那个缺陷（`UiLayoutGame.HudBaseLift` 21.3 → 0）。

用法
----
    python tools/probes/measure/hud_panel_placement.py            # 只解底图（离线，秒级）
    python tools/probes/measure/hud_panel_placement.py <截图.png>  # 额外定位截图里面板的真实落点

退出码：0 = 全部通过；1 = 有断言失败（可直接进 `tools/probes/hosts/run_all_hosts.ps1` 那条链的口径）。
"""
import os
import sys

import numpy as np
from PIL import Image

# 本机控制台默认 GBK ⇒ 含 '⇒' 等字符的断言名会 UnicodeEncodeError（实测踩过），强制 UTF-8 输出
try:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
except Exception:
    pass

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", ".."))
ART = os.path.join(ROOT, "client", "Assets", "Resources", "Clover", "D2", "UI", "Panel", "ControlPanel.png")

# ── 工程侧口径（与 UiLayoutGame 同步：K / 画布高 / 底图尺寸）────────────────────
K = 1.8
REF_H = 1080.0
ART_W, ART_H = 948, 160
CONTENT_LAST_ROW = 138          # 实测量出的"最后一个不透明行"（下面会复核）
# 原版 prefab：Background pos.y = -21.3（图框底边比画布底边低这么多）、高 160
BG_POS_Y = -21.3

fails = []


def check(name, ok, detail):
    print(("[ OK ] " if ok else "[FAIL] ") + name + "   (" + str(detail) + ")")
    if not ok:
        fails.append(name)


def main():
    if not os.path.exists(ART):
        print("找不到底图：" + ART)
        return 1
    im = Image.open(ART).convert("RGBA")
    a = np.array(im).astype(np.uint8)
    if (im.size != (ART_W, ART_H)):
        check("底图尺寸 == 948×160", False, im.size)
    else:
        check("底图尺寸 == 948×160", True, im.size)

    alpha = a[..., 3] > 128
    rows = [int(alpha[y].sum()) for y in range(ART_H)]
    last = max(y for y in range(ART_H) if rows[y] > 0)
    first = min(y for y in range(ART_H) if rows[y] > 0)
    print("  不透明内容行 %d..%d（第 %d..%d 行全透明）；逐行不透明列数见 --rows" % (first, last, last + 1, ART_H - 1))
    check("底图不透明内容最后一行 == %d（常量 CONTENT_LAST_ROW）" % CONTENT_LAST_ROW, last == CONTENT_LAST_ROW,
          "实测 %d" % last)
    check("底图底部存在透明尾巴（最后一行 < 159 ⇒ 原版那 21.3px 落到屏幕外是对的）",
          last < ART_H - 1, "尾巴 %d 行" % (ART_H - 1 - last))

    # ── 工程侧：panel 矩形（HudBaseLift = 0 口径）与内容底边 ────────────────────
    # 底图中心 y（画布）= (原版 pos.y -21.3 + 高 80) ×1.8 − 540   —— lift = 0
    center_y = (BG_POS_Y + ART_H * 0.5) * K - REF_H * 0.5
    rect_bottom = center_y - ART_H * 0.5 * K
    content_bottom = rect_bottom + (ART_H - CONTENT_LAST_ROW) * K
    print("  panel 中心 y = %.2f, rect 底边 = %.2f, 内容底边 = %.2f（画布底边 = %.1f）"
          % (center_y, rect_bottom, content_bottom, -REF_H * 0.5))
    check("rect 底边比画布底边低 21.3×1.8 = 38.34（那是透明尾巴，本来就该在屏外）",
          abs((-REF_H * 0.5) - rect_bottom - 21.3 * K) <= 0.5, "越出 %.2f" % ((-REF_H * 0.5) - rect_bottom))
    check("**内容底边收口在画布底边**（|内容底 −(−540)| ≤ 2 画布px）",
          abs(content_bottom - (-REF_H * 0.5)) <= 2.0, "差 %.2f" % (content_bottom - (-REF_H * 0.5)))

    # ── 可选：在实机截图里定位面板（模板匹配，掩码只算不透明像素）──────────────
    if len(sys.argv) > 1:
        shot_path = sys.argv[1]
        S = np.array(Image.open(shot_path).convert("RGB")).astype(np.float32)
        H, W, _ = S.shape
        t = np.array(im.resize((int(ART_W * K), int(ART_H * K)), Image.NEAREST)).astype(np.float32)
        th, tw = t.shape[0], t.shape[1]
        best = None
        # ★ 关键：`HudBaseLift == 0` 时图框**底边在画布之下 38.34px**（底图自己那 21.3 行是透明的）
        #   ⇒ 图框**放不进** 1080 高的画面，模板匹配必须允许"只比较画面内那一段"，
        #   否则真实落点会被 `y0 + th > H` 这条守卫直接跳掉、匹配只能落在垃圾位置
        #   （本片实测踩过：判据对**修好的帧**报 32.96/40.51 而真位置在 y0=830）。
        for y0 in range(400, H + 1, 2):
            hh = min(th, H - y0)
            if hh < th * 0.6:                 # 画面内至少要有 60% 的图框才判
                break
            tt = t[:hh]
            mm = tt[..., 3] > 200
            if mm.sum() < 1000:
                continue
            for dx in range(-40, 41, 4):
                x0 = W // 2 + dx - tw // 2
                if x0 < 0 or x0 + tw > W:
                    continue
                d = np.abs(S[y0:y0 + hh, x0:x0 + tw] - tt[..., :3]).mean(axis=2)[mm].mean()
                if best is None or d < best[0]:
                    best = (d, dx, y0, x0, hh)
        if best is None:
            check("在截图里定位到面板底图", False, "搜索范围内没找到")
        else:
            d, dx, y0, x0, hh = best
            # 图框边长已知 ⇒ 由 y0（图框顶部）直接算"内容底边在屏幕上的 y"
            content_y = y0 + CONTENT_LAST_ROW * K
            print("  面板落点 x0=%d y0=%d（水平偏移 %d，画面内可见 %d/%d 行）meanAbsDiff=%.2f"
                  % (x0, y0, dx, hh, th, d))
            # 这条**只是"匹配没落在垃圾位置"的守门**，真正判 HUD 的是下一条（内容底边）。
            # 容差 = 20 是**实测定的**：我们自己往底图上画的东西（两颗球的高亮圆盘、技能格图标、
            # 小面板那一排）会抬高残差 —— 已修好的两帧实测 12.7 / 15.5，而"球是空的时候"的旧帧是 8.8。
            # ⛔ 别把这条当成"像不像原版"的判据（那是并排图 + 人眼的活）。
            check("定位可信（meanAbsDiff ≤ 20）", d <= 20.0, "%.2f" % d)
            check("截图里**内容底边贴近画面底边**（屏幕高 %.0f，容差 3px）" % H,
                  abs(content_y - H) <= 3.0, "内容底 y = %.1f ⇒ 差 %.1f" % (content_y, content_y - H))

    print("\n=== %s ===" % ("全部通过" if not fails else ("失败 %d 项：%s" % (len(fails), "; ".join(fails)))))
    return 0 if not fails else 1


if __name__ == "__main__":
    sys.exit(main())
