# -*- coding: utf-8 -*-
"""定位 `MediumSelButtonBlank.dc6` 该用哪套调色板（dialog-options 片，2026-09-24）。

背景（可复跑）：`D2/UI/Menu/btn_med_sel.png` 是按 **ACT1** 解 `FrontEnd/MediumSelButtonBlank.dc6`
帧 0 的产物，实测是麻点图（尺 A：**落盘 PNG** 0.437、**DC6 帧** 0.424；同族常态/按下只有 0.049/0.075）
⇒ 判断为**调色板错**（不是写坏：与 `tools/d2codec` 解码器输出逐像素 maxdiff=0）。
本机 `d2raw/data/global/palette` 只有 ACT1/EndGame/fechar/loading 四套 ⇒ 用**恢复回来的**
`storm.dll` 从 MPQ 里把缺的那套解出来，再用**同一判据**量一遍。

用法：
  python tools/probes/measure/probe_med_sel_palette.py [--list-only]

输出（stdout）：
  ① `Patch_D2.mpq` 的 `(listfile)` 里所有含 palette 的名字（真值表 ⇒ 到底有没有 menu1）；
  ② 逐个候选名 `SFileExtractFile` 能否解出非空字节（**这是"存在"的唯一判据**）；
  ③ 对解出来的每套 palette 跑一遍麻点判据（与 ACT1 基线对照）。

─────────────────────────────────────────────────────────────────────────────
★ 本项目有两把**不同定义**的"麻点尺"（⚠️ 数值**不可直接互比**，别拿 0.054 去比 26.56）：
─────────────────────────────────────────────────────────────────────────────
【尺 A = 本文件 / uicheck Ⓐ-4·Ⓐ-5d 用的那把：**孤立高饱和像素占比**（无量纲，0~1）】
  定义（与 `speckle()` 逐行一致，改这里必须同步改代码）：
    · 记 `ch = max(R,G,B) − min(R,G,B)`；**alpha == 0 的像素 `ch` 记 0**（透明不算）；
    · "高饱和像素" = `ch > 60`；
    · 若某高饱和像素的**4 邻域**（上下左右）平均 `ch < 30` ⇒ 它是**孤立点**（= 一颗麻点）；
    · 比值 = 孤立点数 / 高饱和像素数。
  本文件量的是 **DC6 帧**（未落盘的原始帧）：
    · 干净：`fechar` sel 帧0 = **0.054**（7/130）、帧1 **0.055**（7/127）；
      同族 DC6 帧：WideButtonBlank f0/f2 = 0.017/0.024、MediumButtonBlank f0/f1 = 0.040/0.066、Cancel f0 0.045；
    · 坏：ACT1 解 sel 帧0/帧1 = **0.424/0.410**；`menu1` 0.271；menu4 0.103、sky 0.106（其余 0.24~0.55）。
  ⚠️ **落盘 PNG**（`Resources/.../Menu/btn_*.png`）上量出来的 A 值略有不同（有拼接/编码差异）：
     uicheck Ⓐ-4 实测 Wide 0.035/0.050、Medium 0.049/0.075、**sel 0.054** ⇒ 两者都属"干净带"，
     判据用的是**落盘 PNG** 那一版（`uicheck` 的 Ⓐ-4/Ⓐ-5d）。

【尺 B = 片 dialog-options2 的 `tools/probes/measure/probe_med_button_palette.py`：**麻点度量（相邻像素色差均值）**】
  定义：相邻且都不透明的像素对的 `max(|ΔR|,|ΔG|,|ΔB|)` 的**均值**（有量纲，0~255）。
  实测：干净 26.03 / 24.08（常态 / 按下）、26.56 / 23.20（fechar 版 sel / sel_pressed）；
        坏图 53.40（ACT1 sel）、`menu1` 61.71、**把 fechar 误用到常态图上 105.16**。
  两把尺的**结论一致**（`fechar` 是 `MediumSelButtonBlank.dc6` 唯一干净解；共享按钮必须留在 ACT1），
  但**刻度不同** ⇒ 报告/注释里引用数值时**必须带上尺名**。
  ⚠️ 还会遇到第三种数：`0.486（132/302）`/`90.67`/`61.71` 这类是片 dialog-options2 脚本里
     `onemag()` 的 **8 邻域近似**（**不是**尺 A 的 4 邻域）—— 同一张坏图在尺 A 下是 **0.424**，
     两者**不可互比**（出处：`tools/probes/measure/probe_med_button_palette.py` 的文件头）。
─────────────────────────────────────────────────────────────────────────────
"""
import io
import os
import sys

import numpy as np

try:                                                        # 控制台是 GBK，日志里带 Unicode 会炸
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
except Exception:                                           # noqa: BLE001
    pass

ROOT = r"c:/Work/Server/f-v2/clover-project-diablo2"
sys.path.insert(0, os.path.join(ROOT, "tools", "d2codec"))
import dc6          # noqa: E402
import storm        # noqa: E402

MPQ_DIR = os.path.join(ROOT, "原版资源", "_mpq_incoming")
TMP = os.path.join(ROOT, ".ai-tmp", "test", "do-pal")
DC6_SEL = os.path.join(ROOT, "原版资源", "d2dc6", "data", "global", "ui",
                       "FrontEnd", "MediumSelButtonBlank.dc6")
DC6_MED = os.path.join(ROOT, "原版资源", "d2dc6", "data", "global", "ui",
                       "FrontEnd", "MediumButtonBlank.dc6")

# 候选：D2 的 global palette 目录（大小写两种拼法都试；包内名一律反斜杠，storm._A 会归一化）
CAND_DIRS = ["ACT1", "ACT2", "ACT3", "ACT4", "ACT5", "EndGame", "loading", "fechar",
             "menu0", "menu1", "menu2", "menu3", "menu4",
             "units", "sky", "static", "town", "trademark", "expansion", "infinity"]
CAND_FILES = ["Pal.PL2", "pal.pl2", "pal.PL2", "Pal.pl2"]


def speckle(arr):
    """孤立高饱和像素 / 高饱和像素（与 uicheck Ⓐ-4/Ⓐ-5 同一口径）。"""
    a = arr.astype(int)
    rgb = a[:, :, :3]
    al = a[:, :, 3]
    ch = rgb.max(axis=2) - rgb.min(axis=2)
    ch = np.where(al > 0, ch, 0)
    hot = ch > 60
    if hot.sum() == 0:
        return 0.0, 0, 0
    nb = np.zeros_like(ch, dtype=float)
    cnt = np.zeros_like(ch, dtype=float)
    for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1)):
        nb += np.roll(np.roll(ch, dy, 0), dx, 1)
        cnt += 1
    iso = hot & ((nb / cnt) < 30)
    return float(iso.sum()) / float(hot.sum()), int(iso.sum()), int(hot.sum())


def frame_arr(path, idx, pal):
    d = dc6.parse(open(path, "rb").read())
    f = d.frames[idx]
    raw = bytes(dc6.frame_rgba(f, pal))
    return np.frombuffer(raw, dtype=np.uint8).reshape(f.height, f.width, 4)


def main():
    list_only = "--list-only" in sys.argv
    os.makedirs(TMP, exist_ok=True)
    storm.set_work_dir(TMP)

    print("=== ① Patch_D2.mpq 的 (listfile) 里含 'palette' 的名字 ===")
    names = []
    for mpq, patch in [("Patch_D2.mpq", None), ("D2data.mpq", "Patch_D2.mpq")]:
        p = os.path.join(MPQ_DIR, mpq)
        try:
            h = storm.open_archive(p, patch)
        except OSError as ex:
            print("  OPEN FAIL %s: %s" % (mpq, ex))
            continue
        try:
            try:
                all_names = storm.list_files(h)
                hits = [n for n in all_names if "palette" in n.lower()]
                print("  %s: listfile=%d 条，含 palette 的 %d 条" % (mpq, len(all_names), len(hits)))
                for n in hits[:40]:
                    print("     " + n)
                names += hits
            except OSError as ex:
                print("  %s: 无 (listfile)（%s）" % (mpq, ex))
        finally:
            storm.close_archive(h)

    if list_only:
        return

    print()
    print("=== ② 候选 palette 的提取结果（非空字节才算存在）===")
    found = {}
    h = storm.open_archive(os.path.join(MPQ_DIR, "D2data.mpq"), "Patch_D2.mpq")
    try:
        cands = []
        for d in CAND_DIRS:
            for f in CAND_FILES:
                cands.append("data/global/palette/%s/%s" % (d, f))
        for n in sorted(set(names)):
            if n.lower().endswith(".pl2"):
                cands.append(n)
        for c in cands:
            b = storm.read_file(h, c)
            if b:
                found[c] = len(b)
                print("  OK   %-52s %d B" % (c, len(b)))
    finally:
        storm.close_archive(h)
    if not found:
        print("  ⛔ 一个都没解出来")
        return

    print()
    print("=== ③ 每套找到的 palette 上跑同一麻点判据 ===")
    print("  %-52s %-22s %-22s" % ("palette(源名)", "sel 帧0 占比(iso/hot)", "med 帧0 占比(iso/hot)"))
    for c, size in sorted(found.items()):
        raw = None
        h = storm.open_archive(os.path.join(MPQ_DIR, "D2data.mpq"), "Patch_D2.mpq")
        try:
            raw = storm.read_file(h, c)
        finally:
            storm.close_archive(h)
        if not raw:
            continue
        pl2 = os.path.join(TMP, "Pal_" + c.replace("/", "_"))
        with io.open(pl2, "wb") as fh:
            fh.write(raw)
        try:
            pal = dc6.read_pl2(pl2)
        except Exception as ex:                                  # noqa: BLE001
            print("  %-52s PL2 解析失败：%s" % (c, ex))
            continue
        r_sel = speckle(frame_arr(DC6_SEL, 0, pal))
        r_med = speckle(frame_arr(DC6_MED, 0, pal))
        print("  %-52s %.3f (%3d/%4d)      %.3f (%3d/%4d)"
              % (c, r_sel[0], r_sel[1], r_sel[2], r_med[0], r_med[1], r_med[2]))


if __name__ == "__main__":
    main()
