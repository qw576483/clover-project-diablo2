# -*- coding: utf-8 -*-
# 诊断用：在原版实机截图上扫"金线竖分隔"，定位底部面板里的每个格子（含序号标签）。
from PIL import Image

p = r"原版资源\参考图\原版实机_暗黑2_HUD与物品tooltip.png"
im = Image.open(p).convert("RGB")
W, H = im.size
px = im.load()


def is_gold(x, y):
    r, g, b = px[x, y]
    return r > 95 and g > 75 and b < 140 and (r - b) > 22 and (g - b) > 8


def runs(y, min_w=1, gap=1):
    out = []
    x = 0
    while x < W:
        if is_gold(x, y):
            x0 = x
            while x < W and is_gold(x, y):
                x += 1
            out.append((x0, x - 1))
        else:
            x += 1
    # 合并间距 <= gap 的
    merged = []
    for a, b in out:
        if merged and a - merged[-1][1] <= gap:
            merged[-1][1] = b
        else:
            merged.append([a, b])
    return [m for m in merged if m[1] - m[0] + 1 >= min_w]


for y in range(740, 795, 5):
    rs = runs(y, 1, 1)
    if len(rs) <= 40:
        print("y=%3d n=%2d %s" % (y, len(rs), rs))
    else:
        print("y=%3d n=%2d (太多, 只报首 30) %s" % (y, len(rs), rs[:30]))
