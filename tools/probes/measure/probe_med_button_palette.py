# dialog-options2 (2026-09-24) 判据资产（**可复跑**，落 tools/probes/measure/ 入仓）：
# 判 btn_med_sel.png（中等按钮·悬停底图）到底是"导出写坏了"还是"原版就这样"。
#
# 跑法：cd <项目根>/tools/d2codec && python ../probes/measure/probe_med_button_palette.py
#   （依赖 `原版资源/d2dc6` + `原版资源/d2raw/data/global/palette`，与既有 CheckOriginalRes 同口径；
#     素材未下载的机器上跑不了）
#
# 输出的三个量：
#   ① 与磁盘 PNG 的逐像素差（`0` ⇒ 磁盘那张**就是**这套调色板解出的；非 0 ⇒ 是别的调色板那一版）；
#   ② "麻点度量" = 相邻不透明像素对的 max(|dR|,|dG|,|dB|) 均值（越小越平滑；用错调色板会长麻点）；
#   ③ 孤立高饱和像素占比（另一片用过的口径，复现用）。
#
# 结论（2026-09-24，两片共同定死；**现状与"只按目录分调色板"的直觉相反**）：
#   · `FrontEnd/MediumButtonBlank.dc6`（常态/按下）—— 正确调色板 = **ACT1**：
#       麻点 26.03 / 24.08，与既有干净条带 `button_medium.png`（25.96 / 23.55）一致；
#       换成 `fechar` 会烂到 105.16 / 99.46、`menu1` 90.67 / 86.23 ⇒ ⛔ 这两张**不许**用 fechar 重出。
#   · `FrontEnd/MediumSelButtonBlank.dc6`（悬停/高亮）—— 正确调色板 = **`fechar`**：
#       ACT1 解出 53.40（麻点，实机悬停糊住文案）→ fechar **26.56**（落进同族干净带）；
#       而 `menu1` 61.71 ⇒ **menu1 不是答案**（虽同名"菜单"，实测最差之一）。
#       ⛔ 两套调色板是**按文件**分的（同一屏里的"共享按钮"与"Sel 变体"不同源），
#          所以**不要**拿"整组统一用某套"去重出这批图。
#   · 为什么会这样（否证性证据）：`find_frontend_button_palette.py` 把包里 **15 套**全扫一遍，
#     **没有任何一套**能同时让常态帧与高亮帧干净 ⇒ 只能按文件分；`probe_sel_button_structure.py`
#     另证两个 DC6 连边框环/雕槽的**索引都不同**（相同率 23.7% / 21.1%）⇒ 不是同一张画换了色表。
#   · 色相锚点（肉眼可判，`probe_sel_palette_gem_anchor.py`）：fechar 解出的**金边框 + 深蓝宝石**
#     与"已知干净的常态帧（ACT1）"同形同色；ACT1 解出的那张连边框都撒满彩点。
#
# 现状核对（2026-09-24 03:0x 之后）：磁盘 `btn_med_sel.png` = fechar 版（麻点 **26.56**），
#   `btn_med_sel_pressed.png` = **23.20** ⇒ 判据「回到同族量级」**已达标**。
import os, sys
import numpy as np
from PIL import Image

ROOT = r"c:/Work/Server/f-v2/clover-project-diablo2"
sys.path.insert(0, os.path.join(ROOT, "tools", "d2codec"))
import dc6

SRC = os.path.join(ROOT, "原版资源", "d2dc6")
PL2 = os.path.join(ROOT, "原版资源", "d2raw", "data", "global", "palette")


def load_pal(name):
    return dc6.read_pl2(os.path.join(PL2, name, "Pal.PL2"))


def rgba(frame, pal):
    flat = np.frombuffer(bytes(dc6.frame_rgba(frame, pal)), dtype=np.uint8).astype(int)
    return flat.reshape(frame.height, frame.width, 4)


def speckle(a):
    """麻点度量：相邻且都不透明的像素对，max(|dR|,|dG|,|dB|) 的均值（越小越平滑）。"""
    op = a[:, :, 3] > 0
    tot, n = 0, 0
    for dy, dx in ((0, 1), (1, 0)):
        h = a.shape[0] - dy
        w = a.shape[1] - dx
        m = op[:h, :w] & op[dy:, dx:]
        if m.sum() == 0:
            continue
        d = np.abs(a[:h, :w, :3] - a[dy:, dx:, :3]).max(2)
        tot += d[m].sum()
        n += int(m.sum())
    return tot / n if n else 0.0


def onemag(a):
    """孤立高饱和像素占比：高饱和像素里 8 邻域内没有第二个高饱和像素的比例。"""
    mx, mn = a[:, :, :3].max(2), a[:, :, :3].min(2)
    hi = ((mx - mn) > 80) & (mx > 90)
    if hi.sum() == 0:
        return 0.0
    iso = 0
    ys, xs = np.nonzero(hi)
    for y, x in zip(ys, xs):
        y0, y1 = max(0, y - 1), min(hi.shape[0], y + 2)
        x0, x1 = max(0, x - 1), min(hi.shape[1], x + 2)
        if hi[y0:y1, x0:x1].sum() == 1:
            iso += 1
    return iso / hi.sum()


# 调色板集合 = `原版资源/d2raw/data/global/palette/**` 下**实际在盘**的每一套（自动带出
# 2026-09-24 新取回的 `menu1`；⛔ 不再写死那 4 套 —— 写死会让"取到新调色板后脚本看不见它"）。
PAL_ROOT = os.path.join(ROOT, "原版资源", "d2raw", "data", "global", "palette")
names = sorted(d for d in os.listdir(PAL_ROOT)
               if os.path.exists(os.path.join(PAL_ROOT, d, "Pal.PL2")))
pals = {n: load_pal(n) for n in names}
print("在盘调色板 %s" % names)
print("（原版 FrontEnd 按钮按惯例用 `menu1`；`menu1` 是 2026-09-24 dialog-options2 片从 "
      "D2data.mpq 取回的，见 tools/probes/measure/extract_palette_from_mpq.py）")

for name, out in (("MediumButtonBlank.dc6", "btn_med_normal.png"),
                  ("MediumSelButtonBlank.dc6", "btn_med_sel.png")):
    path = os.path.join(SRC, "data", "global", "ui", "FrontEnd", name)
    d = dc6.parse(open(path, "rb").read())
    print("%s: %d frames, sizes=%s" % (name, len(d.frames), [("frame")]))
    f = d.frames[0]
    print("   frame0 w=%d h=%d" % (f.width, f.height))
    for pn, pal in pals.items():
        a = rgba(f, pal)
        print("   %-8s speckle=%6.2f  isolated_hi=%.3f" % (pn, speckle(a), onemag(a)))
    a_act1 = rgba(f, pals["ACT1"])
    disk = np.asarray(Image.open(os.path.join(
        ROOT, "client/Assets/Resources/Clover/D2/UI/Menu", out)).convert("RGBA")).astype(int)
    print("   vs on-disk %s : maxdiff=%d ndiff=%d" % (
        out, np.abs(a_act1 - disk).max(), int((np.abs(a_act1 - disk).sum(2) > 0).sum())))

# 条带各帧
strip = np.asarray(Image.open(os.path.join(
    ROOT, "client/Assets/Resources/Clover/D2/UI/Menu/button_medium.png")).convert("RGBA")).astype(int)
for i in range(3):
    fr = strip[:, i * 128:(i + 1) * 128]
    print("strip f%d speckle=%6.2f isolated_hi=%.3f" % (i, speckle(fr), onemag(fr)))
for nm in ("btn_med_normal.png", "btn_med_pressed.png", "btn_med_sel.png", "btn_med_sel_pressed.png"):
    a = np.asarray(Image.open(os.path.join(
        ROOT, "client/Assets/Resources/Clover/D2/UI/Menu", nm)).convert("RGBA")).astype(int)
    print("%-24s speckle=%6.2f isolated_hi=%.3f" % (nm, speckle(a), onemag(a)))
