# -*- coding: utf-8 -*-
"""
属性面板底图「凹槽几何」量法（判据资产，随仓提交）。

用途 / 为什么必须有它
--------------------
`UI/UiLayoutGame.cs` 里属性面板的**行矩形**来自原版 prefab（`CharstatPanel.prefab` 的
RectTransform，逐节点实测），那是 `uicheck` 已断言的真值；但原版 prefab **只有标签节点**，
`charstat.png` 底图上每行的「标签隔间 / 数值隔间」并没有 prefab 节点可查 ——
它们只能从**底图像素**量出来。本脚本就是那条「量的口径」：

  · 底图 `client/Assets/Resources/Clover/D2/UI/Panel/charstat.png`（320×432）
  · 暗色阈值 42/255 → 4 邻域连通域 → 取「宽 ≥ 30 且高 ≥ 8 且面积 ≥ 200」的暗块 = 凹槽
  · 输出每块的中心（面板中心坐标系：node = art − (160, 216)，y 向上为正，与
    `UiLayoutGame.CharStatRowOrig` 同口径）

用它跑出来的读数直接决定了 `UiLayoutGame` 的 `CharStatValueX/W`、`CharDefValueX/W`、
`CharCurMaxX/W`、`CharRowSlotH`、`CharRowTextDy`（`uicheck` §③-b 逐条断言这几个值）。
改底图 ⇒ 必须重跑本脚本并核对那几个常量，⛔ 不许手改常量去凑读数。

跑法
----
    python tools/probes/measure/charstat_slots.py [输出 tsv 路径]

输出路径省略时 = `<仓库根>/.ai-tmp/test/charstat_slots.tsv`（**绝对**，由本文件位置推出）；
给了相对路径也会先按调用者 cwd 转绝对再打印 `resolved out=`，⛔ 不会静默落到别的工作区。
底图路径同样是**绝对**的 —— 本脚本不认 cwd，只认自己所在的仓库。
"""

import os
import sys
from PIL import Image

# Path discipline (team-lead 2026-09-24, trap #1 / rule "every read and write uses an ABSOLUTE
# path"): `open()` and `Image.open()` follow the PROCESS cwd, which on this box is the WORKSPACE
# ROOT (`c:\Work\Server\f-v2`), while this script lives under the project -- so a relative source
# or output path can silently address ANOTHER project's tree.  Both are therefore resolved from
# THIS FILE's location, never from the cwd.
_HERE = os.path.dirname(os.path.abspath(__file__))
REPO_ROOT = os.path.abspath(os.path.join(_HERE, "..", "..", ".."))
SRC = os.path.join(REPO_ROOT, "client", "Assets", "Resources", "Clover", "D2", "UI", "Panel",
                   "charstat.png")
OUT_DEFAULT = os.path.join(REPO_ROOT, ".ai-tmp", "test", "charstat_slots.tsv")
TH = 42          # 暗色阈值（0..255 平均亮度）
MIN_W = 30       # 凹槽最小宽（原版px）
MIN_H = 8        # 凹槽最小高
MIN_AREA = 200   # 最小像素数（滤掉文字/纹理噪点）


def main(argv):
    # absolutised against the CWD the caller meant, then echoed below -> a wrong-tree run is visible
    path = os.path.abspath(argv[1]) if len(argv) > 1 else OUT_DEFAULT
    if not os.path.exists(SRC):
        print("USAGE source art missing: %s  cwd=%s" % (SRC, os.getcwd()))
        return 2
    print("resolved src=%s (%d B)" % (SRC, os.path.getsize(SRC)))
    print("resolved out=%s  cwd=%s" % (path, os.getcwd()))
    im = Image.open(SRC).convert("RGB")
    w, h = im.size
    px = im.load()
    dark = [[(sum(px[x, y]) / 3.0) < TH for x in range(w)] for y in range(h)]

    lab = [[0] * w for _ in range(h)]
    boxes, cur = [], 0
    for y in range(h):
        for x in range(w):
            if not dark[y][x] or lab[y][x]:
                continue
            cur += 1
            stack = [(x, y)]
            lab[y][x] = cur
            x0 = x1 = x
            y0 = y1 = y
            n = 0
            while stack:
                cx, cy = stack.pop()
                n += 1
                x0, x1 = min(x0, cx), max(x1, cx)
                y0, y1 = min(y0, cy), max(y1, cy)
                for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                    nx, ny = cx + dx, cy + dy
                    if 0 <= nx < w and 0 <= ny < h and dark[ny][nx] and not lab[ny][nx]:
                        lab[ny][nx] = cur
                        stack.append((nx, ny))
            boxes.append((n, x0, y0, x1, y1))

    rows = [b for b in boxes
            if (b[3] - b[1] + 1) >= MIN_W and (b[4] - b[2] + 1) >= MIN_H and b[0] >= MIN_AREA]
    rows.sort(key=lambda b: (b[2], b[1]))

    lines = ["artpx\tx0\tx1\ty0\ty1\tw\th\tcx_art\tcy_art\tnode_x\tnode_y"]
    print("IHDR %d %d  (thr=%d)" % (w, h, TH))
    for n, x0, y0, x1, y1 in rows:
        cx = (x0 + x1) / 2.0
        cy = (y0 + y1) / 2.0
        node_x, node_y = cx - w / 2.0, h / 2.0 - cy
        line = "%d\t%d\t%d\t%d\t%d\t%d\t%d\t%.1f\t%.1f\t%.1f\t%.1f" % (
            n, x0, x1, y0, y1, x1 - x0 + 1, y1 - y0 + 1, cx, cy, node_x, node_y)
        lines.append(line)
        print("  x %3d..%3d  y %3d..%3d  w=%3d h=%3d   node=(%7.1f,%7.1f)" % (
            x0, x1, y0, y1, x1 - x0 + 1, y1 - y0 + 1, node_x, node_y))

    if path:
        # NOTE: text mode on purpose -- the shipped `charstat_slots.tsv` was written this way
        # (CRLF), and switching to newline="\n" would silently change the cited artifact's bytes.
        with open(path, "w", encoding="utf-8") as f:
            f.write("\n".join(lines) + "\n")
        print("wrote %s (%d B, %d data rows)" % (path, os.path.getsize(path), len(rows)))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
