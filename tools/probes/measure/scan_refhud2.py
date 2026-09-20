# -*- coding: utf-8 -*-
# 诊断用：在原版实机截图上定位「血球 / 蓝球 / 药水(腰带) 格」，算它们相对两球的横向比例。
from PIL import Image

p = r"c:\Work\Server\full-dev\clover-project-diablo2\原版资源\参考图\原版实机_暗黑2_HUD与物品tooltip.png"
im = Image.open(p).convert("RGB")
W, H = im.size
px = im.load()
print("size", W, H)

# 底部 HUD 带（工具提示之外的最低一条）——先用 y 745..795
Y0, Y1 = 745, 795


def col_stats(x):
    red = blue = 0
    for y in range(Y0, Y1):
        r, g, b = px[x, y]
        if r > 90 and r - g > 45 and r - b > 45:
            red += 1
        if b > 90 and b - r > 45 and b - g > 30:
            blue += 1
    return red, blue


cols = [(x, col_stats(x)) for x in range(W)]
red_cols = [x for x, (r, b) in cols if r >= 4]
blue_cols = [x for x, (r, b) in cols if b >= 4]


def groups(xs):
    out = []
    for x in xs:
        if out and x - out[-1][1] <= 6:
            out[-1][1] = x
        else:
            out.append([x, x])
    return [g for g in out if g[1] - g[0] >= 3]


print("RED groups (health orb / potions):")
for g in groups(red_cols):
    print("   %4d..%4d  w=%3d  center=%.1f" % (g[0], g[1], g[1] - g[0] + 1, (g[0] + g[1]) / 2))
print("BLUE groups (mana orb):")
for g in groups(blue_cols):
    print("   %4d..%4d  w=%3d  center=%.1f" % (g[0], g[1], g[1] - g[0] + 1, (g[0] + g[1]) / 2))
