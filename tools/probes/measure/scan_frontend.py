# -*- coding: utf-8 -*-
# 诊断用：量 FrontEnd 半身像各帧 PNG 的实际尺寸 —— 判定导出时是否把每帧的 offset 烘进画布。
import os
import struct

root = r"c:\Work\Server\full-dev\clover-project-diablo2\client\Assets\Resources\Clover\D2\UI\FrontEnd"


def png_size(p):
    with open(p, "rb") as f:
        head = f.read(24)
    w, h = struct.unpack(">II", head[16:24])
    return w, h


for cls in ("amazon", "barbarian"):
    d = os.path.join(root, cls)
    if not os.path.isdir(d):
        print(cls, "MISSING", d)
        continue
    groups = {}
    for fn in sorted(os.listdir(d)):
        if not fn.endswith(".png"):
            continue
        base = fn[:-4]
        code = base.split("_")[0]
        groups.setdefault(code, []).append(base)
    for code, names in sorted(groups.items()):
        sizes = {}
        for n in names:
            sz = png_size(os.path.join(d, n + ".png"))
            sizes[sz] = sizes.get(sz, 0) + 1
        print("%-6s %-6s frames=%3d  sizes=%s" % (cls, code, len(names), sizes))
