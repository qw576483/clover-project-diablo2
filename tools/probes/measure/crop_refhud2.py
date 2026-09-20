# -*- coding: utf-8 -*-
# 诊断用：原版实机截图 → 底部条 3 段 3x 放大（看清顺序与格数）。
from PIL import Image

p = r"c:\Work\Server\full-dev\clover-project-diablo2\原版资源\参考图\原版实机_暗黑2_HUD与物品tooltip.png"
out = r"c:\Work\Server\full-dev\clover-project-diablo2\.ai-tmp\test"
im = Image.open(p).convert("RGBA")
W, H = im.size

bounds = [(0, 440), (420, 880), (850, W)]
for i, (x0, x1) in enumerate(bounds):
    c = im.crop((x0, 735, x1, H))
    c = c.resize((c.width * 3, c.height * 3), Image.NEAREST)
    c.save(out + "\\refbar_%d.png" % i)
    print(i, (x0, x1), "->", c.size)
