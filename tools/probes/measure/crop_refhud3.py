# -*- coding: utf-8 -*-
# 诊断用：原版实机截图 → 整条底部面板（含上半部）分 3 段 2x。
from PIL import Image

p = r"c:\Work\Server\full-dev\clover-project-diablo2\原版资源\参考图\原版实机_暗黑2_HUD与物品tooltip.png"
out = r"c:\Work\Server\full-dev\clover-project-diablo2\.ai-tmp\test"
im = Image.open(p).convert("RGBA")
W, H = im.size

bounds = [(0, 460), (440, 900), (880, W)]
for i, (x0, x1) in enumerate(bounds):
    c = im.crop((x0, 570, x1, H))
    c = c.resize((c.width * 2, c.height * 2), Image.NEAREST)
    c.save(out + "\\refpanel_%d.png" % i)
    print(i, (x0, x1), "->", c.size)
