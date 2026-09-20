# -*- coding: utf-8 -*-
# 诊断用：量 ControlPanel.png 底图上"暗格"的实际 x 区间（判定原版到底有几格、在哪）。
from PIL import Image

p = r"client\Assets\Resources\Clover\D2\UI\Panel\ControlPanel.png"
im = Image.open(p).convert("RGBA")
w, h = im.size
print("size", w, h)
px = im.load()


def is_dark(x, y):
    r, g, b, a = px[x, y]
    return a > 40 and r < 60 and g < 60 and b < 60


def runs_on(y, min_w=6):
    out = []
    x = 0
    while x < w:
        if is_dark(x, y):
            x0 = x
            while x < w and is_dark(x, y):
                x += 1
            if x - x0 >= min_w:
                out.append((x0, x - 1))
        else:
            x += 1
    return out


for y in (50, 60, 70, 80, 90, 100, 110, 120):
    rs = runs_on(y)
    tot = sum(b - a + 1 for a, b in rs)
    print("y=%3d  dark=%4d  n=%2d  runs=%s" % (y, tot, len(rs), rs))
