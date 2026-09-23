# -*- coding: utf-8 -*-
"""
t0_d4d5_index.py -- 判据资产：D4UI / D5动画 两维「联络图格号 <-> 状态矩阵行号」索引生成器。

产出 `.ai-tmp/screenshots/t0d4d5_contact.index.tsv`：每一行 = 本片判定的一条矩阵行
（dim / 行号 / 实体 / 状态 / 结论 / 证据里的 x 联络图格号 / 对应 tile / x_contact.index.tsv 行号）。
判据资产（删了就说不清哪条行靠哪张图判的）。
"""
import os
import re
import sys

try:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
except Exception:
    pass

HERE = os.path.abspath(__file__)
ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.dirname(HERE))))
MATRIX = os.path.join(ROOT, "策划", "状态矩阵.tsv")
SHOTS = os.path.join(ROOT, ".ai-tmp", "screenshots")
IDX = os.path.join(SHOTS, "x_contact.index.tsv")
OUT = os.path.join(SHOTS, "t0d4d5_contact.index.tsv")

X_CELL = [
    ("Boot", "B1"), ("MainMenu", "B2/H2-menu"), ("CharSelect", "B3/B3-confirm"),
    ("CharCreate", "B4"), ("Loading", "B5/B5b"), ("HUD", "X6"),
    ("人物属性", "X7"), ("技能树", "E3"), ("任务日志", "G3a/G3b/G3c/G3d"),
    ("小地图", "C4"), ("光标", "X9-default/X9-hover"), ("ItemTooltip", "F1-q0..q4"),
    ("背包", "F1/F2/F3/X5"), ("Settings", "H2-opt"), ("Pause", "H2-pause"),
    ("Death", "H1"), ("选项/暂停底板", "H2-opt/H2-pause"),
]


def xof(ent):
    for k, v in X_CELL:
        if k in ent:
            return v
    return ""


def main():
    # x_contact.index.tsv 里 grid -> (tile, line)
    grid2 = {}
    if os.path.exists(IDX):
        for i, ln in enumerate(open(IDX, "rb").read().decode("utf-8-sig").replace("\r\n", "\n").split("\n"), 1):
            c = ln.split("\t")
            if len(c) >= 5 and c[0] and not c[0].startswith("#") and c[0] != "grid":
                grid2[c[0]] = (c[2], c[4].split(":")[0])
    raw = open(MATRIX, "rb").read().decode("utf-8-sig")
    nl = "\r\n" if "\r\n" in raw else "\n"
    rows = raw.split(nl)
    out = ["dim\tmatrix_line\tentity\tstate\tverdict\tx_grid\ttile\tx_index_line\tevidence"]
    n = 0
    for i, l in enumerate(rows, 1):
        c = l.split("\t")
        if len(c) < 8 or c[0] not in ("D4UI", "D5动画"):
            continue
        if not c[6].strip():
            continue
        # 收本维度全部已判定行（本片 + 前几轮已填的），给出完整的 行号 -> 联络图格号 索引
        g = xof(c[1])
        tile = idxline = ""
        for gg in re.split(r"[/,]", g):
            gg = gg.strip()
            if gg in grid2:
                tile, idxline = grid2[gg]
                break
        out.append("\t".join([c[0], "L%d" % i, c[1], c[2], c[6].split("(")[0], g, tile, idxline,
                              c[7][:120]]))
        n += 1
    open(OUT, "w", encoding="utf-8", newline="").write("\n".join(out) + "\n")
    print("wrote", OUT, "rows=", n, "x_grid_cells_known=", len(grid2))


if __name__ == "__main__":
    main()
