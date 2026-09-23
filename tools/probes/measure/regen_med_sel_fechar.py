# -*- coding: utf-8 -*-
"""重出 `btn_med_sel*.png`（用正确的调色板 `fechar`）—— dialog-options 片，2026-09-24。

为什么：`D2/UI/Menu/btn_med_sel.png`（= 原版 `FrontEnd/MediumSelButtonBlank.dc6` 帧 0）
原先是按 **ACT1** 解出来的，是麻点图（孤立高饱和像素占比 **0.424**；同族干净图 0.017~0.075）
⇒ 中等按钮一悬停底板就花斑、把文案糊掉。

怎么定的调色板（可复跑）：把 MPQ 里能解的 **15 套** `data/global/palette/*/Pal.PL2` 全量扫一遍
（`tools/probes/measure/probe_med_sel_palette.py` + `.ai-tmp/drivers/do_pal_sweep.py`），
同一麻点判据下唯一落进"干净带"的是 **`fechar`**：
    sel 帧0 = 0.054（7/130）、帧1 = 0.055（7/127）
    对照：menu1=0.271 / menu4=0.103 / sky=0.106 / ACT1=0.424（其余更差）
横向佐证：同目录的 **共享** 按钮（WideButtonBlank / MediumButtonBlank / CancelButtonBlank）
用 **ACT1** 才干净（0.017/0.040/0.045），用 fechar 反而 0.155~0.177 起麻点；
只有这个 **FrontEnd 专属的 "Sel" 变体** 要 fechar —— 与本项目既有惯例一致
（`export_d2ui.py` 的 `frontend` 组本来就整组用 `PL2_FECHAR`）。

⚠️ 本脚本**只写两张 PNG**（`btn_med_sel.png` / `btn_med_sel_pressed.png`），
   不碰 `UiArt` 接线（是否改回 `ResPaths.BtnMedSel` 由主 agent 裁决）。
   旧的两张会被复制到 `<repo>/.ai-tmp/test/do-pal/` 留档（审计用）。

用法：python tools/probes/measure/regen_med_sel_fechar.py
"""
import io
import os
import shutil
import sys

import numpy as np

try:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
except Exception:                                       # noqa: BLE001
    pass

ROOT = r"c:/Work/Server/f-v2/clover-project-diablo2"
sys.path.insert(0, os.path.join(ROOT, "tools", "d2codec"))
import dc6                                              # noqa: E402

SRC = os.path.join(ROOT, "原版资源", "d2dc6", "data", "global", "ui", "FrontEnd",
                   "MediumSelButtonBlank.dc6")
PL2 = os.path.join(ROOT, "原版资源", "d2raw", "data", "global", "palette", "fechar", "Pal.PL2")
DST = os.path.join(ROOT, "client", "Assets", "Resources", "Clover", "D2", "UI", "Menu")
KEEP = os.path.join(ROOT, ".ai-tmp", "test", "do-pal")

TARGETS = [("btn_med_sel.png", 0), ("btn_med_sel_pressed.png", 1)]


def speckle(arr):
    a = arr.astype(int)
    ch = np.where(a[:, :, 3] > 0, a[:, :, :3].max(axis=2) - a[:, :, :3].min(axis=2), 0)
    hot = ch > 60
    if hot.sum() == 0:
        return 0.0, 0, 0
    nb = np.zeros_like(ch, dtype=float)
    cnt = np.zeros_like(ch, dtype=float)
    for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1)):
        nb += np.roll(np.roll(ch, dy, 0), dx, 1)
        cnt += 1
    iso = hot & ((nb / cnt) < 30)
    return float(iso.sum()) / hot.sum(), int(iso.sum()), int(hot.sum())


def main():
    os.makedirs(KEEP, exist_ok=True)
    pal = dc6.read_pl2(PL2)
    print("调色板：%s（%d 项）" % (PL2, len(pal)))
    d = dc6.parse(open(SRC, "rb").read())
    print("源：%s  帧数=%d  尺寸=%dx%d" % (os.path.basename(SRC), len(d.frames),
                                          d.frames[0].width, d.frames[0].height))
    for fname, idx in TARGETS:
        dst = os.path.join(DST, fname)
        if os.path.exists(dst):
            shutil.copyfile(dst, os.path.join(KEEP, "old_" + fname))     # 留档（审计）
        f = d.frames[idx]
        argb = dc6.frame_rgba(f, pal)
        dc6.write_png_rgba(dst, argb, f.width, f.height)
        arr = np.frombuffer(bytes(argb), dtype=np.uint8).reshape(f.height, f.width, 4)
        s = speckle(arr)
        print("  %-24s <- 帧%d  %dx%d  麻点占比 %.3f（孤立 %d / 高饱和 %d）"
              % (fname, idx, f.width, f.height, s[0], s[1], s[2]))
    print("完成；旧图留档在 %s（old_*.png）" % KEEP)


if __name__ == "__main__":
    main()
