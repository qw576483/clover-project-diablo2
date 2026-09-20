# -*- coding: utf-8 -*-
# 诊断用：把 ControlPanel.png 的左带 / 右带放大切出来，肉眼看清到底是什么格。
from PIL import Image

src = r"c:\Work\Server\full-dev\clover-project-diablo2\client\Assets\Resources\Clover\D2\UI\Panel\ControlPanel.png"
out = r"c:\Work\Server\full-dev\clover-project-diablo2\.ai-tmp\test"

im = Image.open(src).convert("RGBA")

for name, box in (("left_band", (210, 60, 560, 140)), ("right_band", (555, 60, 760, 140))):
    c = im.crop(box)
    c = c.resize((c.width * 3, c.height * 3), Image.NEAREST)
    c.save(out + "\\" + name + ".png")
    print(name, box, "->", c.size)
