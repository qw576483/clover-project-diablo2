# **雕槽宝石 / 金边框**上 —— 这是"只有眼睛能判"那一半，且**肉眼可复核**。
#
# 为什么需要它：中段版面在错误调色板下是麻点（噪声），数值度量只能给"平滑度"，判不出**色相对不对**；
# 而"同一颗宝石 / 同一圈金边框"的色相是硬锚点 —— 某套色若让宝石变灰或变别的色，它就不是这套图的色表。
#
# 跑法：cd <项目根>/tools/d2codec && python ../probes/measure/probe_sel_palette_gem_anchor.py
# 产物（供 AI 读图，不许只看数值）：
#   .ai-tmp/screenshots/dialogoptions2_selpal_full.png   四行整条按钮 5×（A 参考 / B sel@ACT1 / C sel@fechar / D 磁盘现状）
#   .ai-tmp/screenshots/dialogoptions2_selpal_gems.png   同上四行的**左端宝石区 10×**
#   金边框 + 深蓝宝石；B（sel@ACT1）连边框都撒满白/粉/蓝彩点 ⇒ **fechar 才是这一个 DC6 的调色板**。
#   别把这条推广到同族的常态/按下图（那两张要 ACT1，fechar 会让它们烂到 105 —— 见 probe_med_button_palette.py）。
import os
import sys
import numpy as np
from PIL import Image

ROOT = r"c:/Work/Server/f-v2/clover-project-diablo2"
sys.path.insert(0, os.path.join(ROOT, "tools", "d2codec"))
import dc6

SRC = os.path.join(ROOT, "原版资源", "d2dc6", "data", "global", "ui", "FrontEnd")
PAL = os.path.join(ROOT, "原版资源", "d2raw", "data", "global", "palette")
SEL_NOW = os.path.join(ROOT, "client/Assets/Resources/Clover/D2/UI/Menu/btn_med_sel.png")


def p(name):
    return dc6.read_pl2(os.path.join(PAL, name, "Pal.PL2"))


def rgba(frame, pal):
    flat = np.frombuffer(bytes(dc6.frame_rgba(frame, pal)), dtype=np.uint8).astype(int)
    return flat.reshape(frame.height, frame.width, 4).astype(np.uint8)


nor = dc6.parse(open(os.path.join(SRC, "MediumButtonBlank.dc6"), "rb").read()).frames[0]
sel = dc6.parse(open(os.path.join(SRC, "MediumSelButtonBlank.dc6"), "rb").read()).frames[0]

rows = [
    ("A normal@ACT1 (参考，已知干净)", rgba(nor, p("ACT1")), 0),
    ("B sel@ACT1", rgba(sel, p("ACT1")), 1),
    ("C sel@fechar (对方重出的那版)", rgba(sel, p("fechar")), 2),
    ("D 磁盘现在的 btn_med_sel.png", np.asarray(Image.open(SEL_NOW).convert("RGBA")).astype(np.uint8), 3),
]

# ① 整条按钮放大 5×（看整体观感）  ② 左端宝石区放大 10×（看色相硬锚点）
S1, S2 = 5, 10
full_h, gem_w = 35 * S1, 40
canvas = Image.new("RGB", (128 * S1, (35 * S1 + 8) * len(rows)), (25, 25, 35))
gems = Image.new("RGB", (gem_w * S2, (35 * S2 + 8) * len(rows)), (25, 25, 35))
for i, (_lab, a, _k) in enumerate(rows):
    im = Image.fromarray(a, "RGBA")
    canvas.paste(im.resize((128 * S1, 35 * S1), Image.NEAREST), (0, i * (35 * S1 + 8)))
    g = im.crop((0, 0, gem_w, 35))
    gems.paste(g.resize((gem_w * S2, 35 * S2), Image.NEAREST), (0, i * (35 * S2 + 8)))
    # 宝石区平均色（只统计不透明且高饱和的像素）——"硬锚点"的数值版
    arr = a[6:29, 2:22].astype(int)
    mx, mn = arr[:, :, :3].max(2), arr[:, :, :3].min(2)
    hi = ((mx - mn) > 40) & (arr[:, :, 3] > 0)
    if hi.sum():
        print("%-34s 宝石区高饱和像素 %3d 个，平均 RGB = %s" %
              (_lab, int(hi.sum()), tuple(arr[:, :, :3][hi].mean(0).round(1))))
    else:
        print("%-34s 宝石区**没有**高饱和像素（宝石发灰）" % _lab)

canvas.save(os.path.join(ROOT, ".ai-tmp", "screenshots", "dialogoptions2_selpal_full.png"))
gems.save(os.path.join(ROOT, ".ai-tmp", "screenshots", "dialogoptions2_selpal_gems.png"))
print("rows top->bottom:", [r[0] for r in rows])
