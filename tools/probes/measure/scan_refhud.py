# -*- coding: utf-8 -*-
# 诊断用：量「原版实机」截图的底部控制面板 —— 从左到右到底有哪些格、各多少格。
from PIL import Image

p = r"c:\Work\Server\full-dev\clover-project-diablo2\原版资源\参考图\原版实机_暗黑2_HUD与物品tooltip.png"
im = Image.open(p).convert("RGBA")
w, h = im.size
print("size", w, h)
px = im.load()

# HUD 条大致在底部 ~55px；先找"格带"所在的行：亮金线多的行
def gold_count(y):
    n = 0
    for x in range(w):
        r, g, b, a = px[x, y]
        if a > 40 and r > 110 and g > 90 and b < 120 and r - b > 30:
            n += 1
    return n

rows = sorted(((y, gold_count(y)) for y in range(h - 90, h)), key=lambda t: -t[1])[:10]
print("gold-richest rows:", rows)

y = rows[0][0]
print("scan row y =", y)

# 在该行上打"不是近黑背景"的段（格子的内芯通常偏暗但被金框分隔）
def runs(y, pred, min_w=3):
    out = []
    x = 0
    while x < w:
        if pred(x, y):
            x0 = x
            while x < w and pred(x, y):
                x += 1
            if x - x0 >= min_w:
                out.append((x0, x - 1))
        else:
            x += 1
    return out


def is_gold(x, y):
    r, g, b, a = px[x, y]
    return a > 40 and r > 100 and g > 80 and b < 130 and r - b > 25


for yy in (y - 6, y - 3, y, y + 3):
    rs = runs(yy, is_gold, 2)
    print("y=%3d goldRuns(%d)=%s" % (yy, len(rs), rs))
