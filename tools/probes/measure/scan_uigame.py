# -*- coding: utf-8 -*-
"""scan_uigame.py —— 「游戏内 UI 1:1」审计的**量法脚本**（w3-uigame 片新增）。

判什么（全部只读原版素材 PNG，不依赖任何工程常量）：
  1. `--sizes`  ：工程引用的 UI 贴图**原生像素尺寸**（IHDR），用来判「控件矩形 == 原版像素 ×1.8」
                  是否拿的是**素材本身的尺寸**还是 prefab 的 sizeDelta（两者不等时就是失真）。
  2. `--minipanel`：`minipanel.png`（173×26）里 **7 个按钮凹槽**的真实 x 区间与中心
                  ⇒ 与 `UiLayoutGame.MiniButtonX`（原版 -63..63 步进 21）对账。
  3. `--belt`   ：`ControlPanel.png` 里**腰带 4 格**与**技能格带 6 格**的暗格实测区间
                  ⇒ 与 `UiLayoutGame.BeltCellX / BeltCellSize / SkillBarSlotX` 对账。
  4. `--inv`    ：`inventory.png` 的格线实测（竖线/横线 x/y）⇒ 与 `InvCellW/H`、`InvGridOrigin` 对账。
  5. `--cursor` ：`Cursor.png` 的内容实测（尺寸 / 连通块 / 是否多帧）
                  ⇒ 判 `Def.CursorKind`（5 态）有没有可用的原版素材。
  6. `--charstat`：`charstat.png` 的空框实测（右上框 / 右下两细框 / 左下空白区）
                  ⇒ 与 `UiLayoutGame.CharTopRightPos / CharBottomRightOrig / CharResistRowOrig` 对账。

用法（仓库根任意 cwd 都可，脚本自己向上找含 `client/Assets` 的那一层）：
  python tools/probes/measure/scan_uigame.py --all
  python tools/probes/measure/scan_uigame.py --belt --minipanel
退出码：0 = 全部量到；1 = 有图取不到（**不是**判"不一致"，不一致由人/断言判）。
只依赖 Pillow。
"""

import argparse
import os
import sys

# 控制台默认是 GBK（Windows）⇒ 强制 stdout 走 UTF-8，否则中文/箭头字符直接抛 UnicodeEncodeError
try:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
except Exception:  # pragma: no cover
    pass

try:
    from PIL import Image
except ImportError:  # pragma: no cover
    print("需要 Pillow：python -m pip install Pillow")
    sys.exit(2)


def find_root():
    """从本文件位置向上找含 `client/Assets` 的那一层（= 仓库根）。"""
    here = os.path.dirname(os.path.abspath(__file__))
    d = here
    for _ in range(8):
        if os.path.isdir(os.path.join(d, "client", "Assets")):
            return d
        nd = os.path.dirname(d)
        if nd == d:
            break
        d = nd
    raise SystemExit("找不到仓库根（向上 8 层都没有 client/Assets）")


ROOT = find_root()
UI = os.path.join(ROOT, "client", "Assets", "Resources", "Clover", "D2", "UI")

# 工程代码/常量引用到的 UI 贴图（`--sizes` 用；路径相对 D2/UI/）
SIZE_LIST = [
    "Panel/ControlPanel.png", "Panel/healthbar.png", "Panel/manabar.png",
    "Panel/ExperienceBar.png", "Panel/ExperienceBarOverlay.png",
    "Panel/inventory.png", "Panel/charstat.png",
    "Panel/minipanel.png", "Panel/overlap.png",
    "Panel/menubutton__0__0.png", "Panel/menubutton__0__2.png",
    "Panel/runbutton_run_NotPressed.png", "Panel/runbutton_walk_NotPressed.png",
    "Panel/quest_back.png", "Panel/dialog_back.png", "Panel/buysell_back.png",
    "Panel/questsocket_0.png", "Panel/questsocket_1.png", "Panel/questtab_0.png",
    "Panel/goldcoinbtn_0.png", "Panel/buysellbtn_0.png", "Panel/tradebtn_0.png",
    "Panel/minipanelbtn__00__00.png", "Panel/skltree_a_back_0.png",
    "Cursor/Cursor.png",
    "EquipSlot/inv_armor.png", "EquipSlot/inv_belt.png", "EquipSlot/inv_boots.png",
    "EquipSlot/inv_helm_glove.png", "EquipSlot/inv_ring_amulet.png", "EquipSlot/inv_weapons.png",
    "MiniMap/mapicon_0.png",
    "Banner/inventory.png", "Banner/quests_0.png", "Banner/charstat.png",
]


def load(rel):
    p = os.path.join(UI, rel)
    if not os.path.exists(p):
        return None
    return Image.open(p).convert("RGBA")


# ── 通用：按行/列找"暗块"区间 ────────────────────────────────────────────────
def dark_runs(px, w, fixed, lo, hi, fixed_is_y, thresh=60, min_len=3):
    """沿 fixed 行/列扫 lo..hi，返回连续暗像素区间 [(a,b), ...]。"""
    out = []
    v = lo
    while v < hi:
        def d(i):
            x, y = (i, fixed) if not fixed_is_y else (fixed, i)
            r, g, b, a = px[x, y]
            return a > 40 and r < thresh and g < thresh and b < thresh
        if d(v):
            v0 = v
            while v < hi and d(v):
                v += 1
            if v - v0 >= min_len:
                out.append((v0, v - 1))
        else:
            v += 1
    return out


def show(label, runs):
    info = " ".join("%d..%d(%.1f)" % (a, b, (a + b) / 2.0) for a, b in runs)
    print("  %-16s n=%d  %s" % (label, len(runs), info))


# ── 1. 原生尺寸 ─────────────────────────────────────────────────────────────
def cmd_sizes():
    print("== 工程引用的 UI 贴图原生尺寸（IHDR） ==")
    miss = 0
    for rel in SIZE_LIST:
        im = load(rel)
        if im is None:
            print("  %-42s MISSING" % rel)
            miss += 1
            continue
        print("  %-42s %dx%d" % (rel, im.size[0], im.size[1]))
    return 1 if miss else 0


# ── 2. minipanel 的 7 个按钮凹槽 ────────────────────────────────────────────
def cmd_minipanel():
    im = load("Panel/minipanel.png")
    if im is None:
        print("[minipanel] 取不到 minipanel.png")
        return 1
    w, h = im.size
    px = im.load()
    print("== minipanel.png %dx%d ==" % (w, h))
    mid = h // 2
    runs = dark_runs(px, w, mid, 0, w, False, thresh=70, min_len=5)
    show("y=%d 行暗块" % mid, runs)
    print("  ⇒ 凹槽中心相对**贴图左沿**: %s" % ["%.1f" % ((a + b) / 2.0) for a, b in runs])
    print("  ⇒ 凹槽中心相对**贴图中心**: %s" % ["%+.1f" % ((a + b) / 2.0 - w / 2.0) for a, b in runs])
    return 0


# ── 3. ControlPanel 的腰带格 / 技能格带 ─────────────────────────────────────
def cmd_belt():
    im = load("Panel/ControlPanel.png")
    if im is None:
        print("[belt] 取不到 ControlPanel.png")
        return 1
    w, h = im.size
    px = im.load()
    print("== ControlPanel.png %dx%d ==" % (w, h))
    # 腰带带（原版描述 art y 86..116）与技能格带（art y 47..73）
    for y in (101, 60, 55):
        runs = dark_runs(px, w, y, 0, w, False, thresh=70, min_len=5)
        show("y=%d 行暗块" % y, runs)
        if runs:
            print("     中心 art x = %s" % ["%.1f" % ((a + b) / 2.0) for a, b in runs])
    # 技能格带：逐行找 6 格
    for y in (55, 60, 65):
        runs = dark_runs(px, w, y, 300, 560, False, thresh=70, min_len=5)
        show("y=%d 技能带(300..560)" % y, runs)
    return 0


# ── 4. inventory 格线 ───────────────────────────────────────────────────────
def cmd_inv():
    im = load("Panel/inventory.png")
    if im is None:
        print("[inv] 取不到 inventory.png")
        return 1
    w, h = im.size
    px = im.load()
    print("== inventory.png %dx%d ==" % (w, h))

    def bright(x, y):
        r, g, b, a = px[x, y]
        return a > 40 and r > 120 and g > 110 and b > 90

    # 竖线：在背包格区（y 252..369）里逐列统计亮像素数
    cols = []
    for x in range(w):
        n = 0
        for y in range(252, 370):
            if bright(x, y):
                n += 1
        if n >= 100:
            cols.append(x)
    print("  竖格线 x = %s" % cols)
    rows = []
    for y in range(h):
        n = 0
        for x in range(17, 310):
            if bright(x, y):
                n += 1
        if n >= 250:
            rows.append(y)
    print("  横格线 y = %s" % rows)
    return 0


# ── 5. Cursor 内容 ─────────────────────────────────────────────────────────
def cmd_cursor():
    im = load("Cursor/Cursor.png")
    if im is None:
        print("[cursor] 取不到 Cursor.png")
        return 1
    w, h = im.size
    px = im.load()
    print("== Cursor.png %dx%d ==" % (w, h))

    # 不透明像素的外接矩形 + 用 alpha 打一张 ASCII 小图（人眼在终端里就能判"几个图形"）
    xs, ys = [], []
    for y in range(h):
        for x in range(w):
            if px[x, y][3] > 32:
                xs.append(x)
                ys.append(y)
    if not xs:
        print("  全透明（不是可用的光标素材）")
        return 0
    print("  不透明外接矩形: x %d..%d  y %d..%d（%dx%d）"
          % (min(xs), max(xs), min(ys), max(ys), max(xs) - min(xs) + 1, max(ys) - min(ys) + 1))
    for y in range(h):
        line = "".join("#" if px[x, y][3] > 128 else ("." if px[x, y][3] > 32 else " ")
                       for x in range(w))
        print("  |" + line + "|")

    # 连通块数（4 邻域）—— 判"是否多帧并排"
    seen = [[False] * w for _ in range(h)]
    blocks = 0
    for y0 in range(h):
        for x0 in range(w):
            if seen[y0][x0] or px[x0, y0][3] <= 32:
                continue
            blocks += 1
            stack = [(x0, y0)]
            seen[y0][x0] = True
            while stack:
                x, y = stack.pop()
                for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                    nx, ny = x + dx, y + dy
                    if 0 <= nx < w and 0 <= ny < h and not seen[ny][nx] and px[nx, ny][3] > 32:
                        seen[ny][nx] = True
                        stack.append((nx, ny))
    print("  不透明连通块数 = %d" % blocks)
    return 0


# ── 6. charstat 空框 ───────────────────────────────────────────────────────
def cmd_charstat():
    im = load("Panel/charstat.png")
    if im is None:
        print("[charstat] 取不到 charstat.png")
        return 1
    w, h = im.size
    px = im.load()
    print("== charstat.png %dx%d ==" % (w, h))

    def gold(x, y):
        r, g, b, a = px[x, y]
        return a > 40 and r > 90 and r - b > 25

    rows = []
    for y in range(h):
        n = 0
        for x in range(w):
            if gold(x, y):
                n += 1
        if n >= 200:
            rows.append(y)
    print("  满宽金线 y = %s" % rows)
    cols = []
    for x in range(w):
        n = 0
        for y in range(h):
            if gold(x, y):
                n += 1
        if n >= 200:
            cols.append(x)
    print("  满高金线 x = %s" % cols)
    return 0


# ── 7. 通用：把一张图打成 ASCII（人眼在终端里判"图上到底画了什么"）──────────
ART_RAMP_X = " .:-=+*#%@"


def cmd_art(rel):
    # 支持 "REL@x0,y0,x1,y1" 只打一个窗口（大图看局部）
    win = None
    if "@" in rel:
        rel, spec = rel.split("@", 1)
        try:
            win = [int(t) for t in spec.split(",")]
            if len(win) != 4:
                raise ValueError
        except ValueError:
            print("[art] 窗口写法应为 REL@x0,y0,x1,y1，收到 %r" % spec)
            return 1

    im = load(rel)
    if im is None:
        print("[art] 取不到 %s" % rel)
        return 1
    if win is not None:
        im = im.crop((win[0], win[1], win[2], win[3]))
    w, h = im.size
    px = im.load()
    print("== art %s %dx%d（左=暗 右=亮；'.' = 半透明，' ' = 全透明）==" % (rel, w, h))
    for y in range(h):
        line = []
        for x in range(w):
            r, g, b, a = px[x, y]
            if a <= 32:
                line.append(" ")
                continue
            if a <= 128:
                line.append(".")
                continue
            v = (r * 299 + g * 587 + b * 114) // 1000
            line.append(ART_RAMP_X[min(len(ART_RAMP_X) - 1, v * len(ART_RAMP_X) // 256)])
        print("  |" + "".join(line) + "|")
    return 0


CMDS = {
    "sizes": cmd_sizes,
    "minipanel": cmd_minipanel,
    "belt": cmd_belt,
    "inv": cmd_inv,
    "cursor": cmd_cursor,
    "charstat": cmd_charstat,
}


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--all", action="store_true")
    ap.add_argument("--art", metavar="REL", action="append", default=[],
                    help="打印该图（相对 D2/UI/ 的路径，可重复）的 ASCII 灰度图")
    for k in CMDS:
        ap.add_argument("--" + k, action="store_true")
    a = ap.parse_args()

    picked = [k for k in CMDS if getattr(a, k.replace("-", "_"))]
    if not a.art and (a.all or not picked):
        picked = list(CMDS)

    rc = 0
    for rel in a.art:
        rc |= cmd_art(rel)
        print("")
    for k in picked:
        rc |= CMDS[k]()
        print("")
    print("ROOT=%s" % ROOT)
    return rc


if __name__ == "__main__":
    sys.exit(main())
