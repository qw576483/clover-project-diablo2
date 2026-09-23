# dialog-options2 一次性探针：`MediumSelButtonBlank.dc6` 与同族 `MediumButtonBlank.dc6` 的**结构对照**
# 目的：判定"高亮帧解出麻点"到底是(a)调色板选错 还是(b)这张 DC6 的版面本身与常态帧不同源。
# 已知：(a) 已被否证 —— 包里 15 套调色板**没有一套**能同时让两个 DC6 干净
#      （见同目录 `find_frontend_button_palette.py`）⇒ 两套调色板是**按文件**分的。
# 跑法：cd <项目根>/tools/d2codec && python ../probes/measure/probe_sel_button_structure.py
import os
import sys
import numpy as np

ROOT = r"c:/Work/Server/f-v2/clover-project-diablo2"
sys.path.insert(0, os.path.join(ROOT, "tools", "d2codec"))
import dc6

SRC = os.path.join(ROOT, "原版资源", "d2dc6", "data", "global", "ui", "FrontEnd")
PAL = os.path.join(ROOT, "原版资源", "d2raw", "data", "global", "palette")


def idxs(frame):
    return np.frombuffer(bytes(frame.indices), dtype=np.uint8).reshape(frame.height, frame.width)


def speckle(a):
    op = a[:, :, 3] > 0
    tot, n = 0, 0
    for dy, dx in ((0, 1), (1, 0)):
        h, w = a.shape[0] - dy, a.shape[1] - dx
        m = op[:h, :w] & op[dy:, dx:]
        if m.sum() == 0:
            continue
        d = np.abs(a[:h, :w, :3] - a[dy:, dx:, :3]).max(2)
        tot += d[m].sum()
        n += int(m.sum())
    return tot / n if n else 0.0


def rgba(frame, pal):
    flat = np.frombuffer(bytes(dc6.frame_rgba(frame, pal)), dtype=np.uint8).astype(int)
    return flat.reshape(frame.height, frame.width, 4)


nor = dc6.parse(open(os.path.join(SRC, "MediumButtonBlank.dc6"), "rb").read())
sel = dc6.parse(open(os.path.join(SRC, "MediumSelButtonBlank.dc6"), "rb").read())
wide = dc6.parse(open(os.path.join(SRC, "WideButtonBlank.dc6"), "rb").read())

print("DC6 版本：MediumButton=%r  MediumSel=%r  WideButton=%r" % (nor.version, sel.version, wide.version))
print("帧数：MediumButton=%d  MediumSel=%d  每向帧数=%s/%s"
      % (len(nor.frames), len(sel.frames), nor.frames_per_dir, sel.frames_per_dir))

a, b = idxs(nor.frames[0]), idxs(sel.frames[0])
same = int((a == b).sum())
print("\n帧0 逐像素索引：完全相同的像素 %d / %d (%.1f%%)"
      % (same, a.size, 100.0 * same / a.size))

# 边框环（外 6 px）+ 两侧雕槽（x<22 / x>105）里，索引是否一致
ring = np.zeros(a.shape, bool)
ring[:6, :] = ring[-6:, :] = True
ring[:, :6] = ring[:, -6:] = True
gems = np.zeros(a.shape, bool)
gems[:, :24] = gems[:, -24:] = True
inner = ~(ring | gems)
for nm, m in (("外框环(6px)", ring), ("两侧雕槽 x<24 / x>104", gems), ("中段版面", inner)):
    eq = (a == b)[m]
    print("   %-24s 索引相同率 %.3f   (常态索引均值 %.1f / 高亮 %.1f)"
          % (nm, eq.mean(), a[m].mean(), b[m].mean()))

pal_act1 = dc6.read_pl2(os.path.join(PAL, "ACT1", "Pal.PL2"))
pal_menu1 = dc6.read_pl2(os.path.join(PAL, "menu1", "Pal.PL2"))
print("\n中段版面里高亮帧出现最多的 6 个索引及其两套色：")
vals, counts = np.unique(b[inner], return_counts=True)
order = np.argsort(-counts)[:6]
for i in order:
    v = int(vals[i])
    print("   idx %3d x%-5d ACT1=%s menu1=%s" % (v, counts[i], pal_act1[v][:3], pal_menu1[v][:3]))

print("\n同族第三张（WideButtonBlank，256x35）在两套色下的麻点：")
for pn, p in (("ACT1", pal_act1), ("menu1", pal_menu1)):
    print("   %-6s wide f0=%.2f  f2=%.2f" % (pn, speckle(rgba(wide.frames[0], p)),
                                             speckle(rgba(wide.frames[2], p))))
