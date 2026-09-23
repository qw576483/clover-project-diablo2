# dialog-options2 一次性搜索：原版 `data/global/palette/**` 里**哪一套**能让
# `MediumButtonBlank.dc6`（常态/按下，已知 ACT1 解出干净 26.03/24.08）**与**
# `MediumSelButtonBlank.dc6`（高亮，ACT1 解出 53.40 麻点）**同时**回到同族量级。
#
# 判据：交叉验证 —— 正确的那一套必须让**两个** DC6 都干净；只让其中一个干净的一律排除
#       （例：fechar 让 Sel 干净 26.56，却让常态烂到 105.16 ⇒ 排除）。
import os
import sys
import numpy as np

ROOT = r"c:/Work/Server/f-v2/clover-project-diablo2"
sys.path.insert(0, os.path.join(ROOT, "tools", "d2codec"))
import dc6
import storm

TMP = os.path.join(ROOT, ".ai-tmp", "test", "do2_pals")
SRC = os.path.join(ROOT, "原版资源", "d2dc6", "data", "global", "ui", "FrontEnd")
STRIP = os.path.join(ROOT, "client", "Assets", "Resources", "Clover", "D2", "UI", "Menu", "button_medium.png")

CANDIDATES = ["ACT1", "ACT2", "ACT3", "ACT4", "ACT5", "EndGame", "fechar", "loading",
              "menu0", "menu1", "menu2", "menu3", "menu4", "sky", "static", "units",
              "trademark", "codex", "expansion"]


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


def rgba(frame, p):
    flat = np.frombuffer(bytes(dc6.frame_rgba(frame, p)), dtype=np.uint8).astype(int)
    return flat.reshape(frame.height, frame.width, 4)


os.makedirs(TMP, exist_ok=True)
storm.set_work_dir(TMP)
h = storm.open_archive(os.path.join(ROOT, "原版资源", "_mpq_incoming", "D2data.mpq"), patch="Patch_D2.mpq")
landed = {}
try:
    for name in CANDIDATES:
        for cand in ("Pal.PL2", "Pal.pl2"):
            raw = storm.read_file(h, "data/global/palette/%s/%s" % (name, cand))
            if raw:
                p = os.path.join(TMP, name + ".PL2")
                with open(p, "wb") as fh:
                    fh.write(raw)
                landed[name] = (p, len(raw))
                break
finally:
    storm.close_archive(h)

print("包里探到 %d 套：%s" % (len(landed), sorted(landed)))

nor = dc6.parse(open(os.path.join(SRC, "MediumButtonBlank.dc6"), "rb").read()).frames
sel = dc6.parse(open(os.path.join(SRC, "MediumSelButtonBlank.dc6"), "rb").read()).frames

from PIL import Image
strip = np.asarray(Image.open(STRIP).convert("RGBA")).astype(int)
ref_n = speckle(strip[:, 0:128])
ref_p = speckle(strip[:, 128:256])
print("参考（干净族，来自既有条带导出）: 常态 %.2f / 按下 %.2f" % (ref_n, ref_p))
print("%-10s %8s %8s %8s %8s   %s" % ("palette", "nor.f0", "nor.f1", "sel.f0", "sel.f1", "结论"))
rows = []
for name in sorted(landed):
    pal = dc6.read_pl2(landed[name][0])
    a = [speckle(rgba(f, pal)) for f in (nor[0], nor[1], sel[0], sel[1])]
    # 交叉验证：两个 DC6 的四帧都要落在"干净族"量级（<= 该族上界 30，含 15% 余量）
    ok = all(x <= 30.0 for x in a)
    rows.append((max(a), name, a, ok))
for mx, name, a, ok in sorted(rows):
    print("%-10s %8.2f %8.2f %8.2f %8.2f   %s" % (name, a[0], a[1], a[2], a[3],
                                                  "候选（四帧全干净）" if ok else "排除"))
print("\n结论：%s" % ("有候选见上" if any(r[3] for r in rows) else "五套+扩展里**没有**任何一套能同时让两个 DC6 干净"))
