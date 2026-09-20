# -*- coding: utf-8 -*-
# 诊断用：把原版实机截图的底部 HUD 条放大切出来，肉眼看格带顺序与格数。
from PIL import Image

p = r"c:\Work\Server\full-dev\clover-project-diablo2\原版资源\参考图\原版实机_暗黑2_HUD与物品tooltip.png"
out = r"c:\Work\Server\full-dev\clover-project-diablo2\.ai-tmp\test"
im = Image.open(p).convert("RGBA")
W, H = im.size
print(W, H)

bar = im.crop((0, 720, W, 797))
print("bar", bar.size)
half = (W + 1) // 2
for i in range(2):
    c = bar.crop((i * half, 0, min(W, (i + 1) * half), bar.height))
    c = c.resize((c.width * 2, c.height * 2), Image.NEAREST)
    c.save(out + "\\refhud_half%d.png" % i)
    print("saved half", i, c.size)
