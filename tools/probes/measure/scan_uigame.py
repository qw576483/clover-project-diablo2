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
    "Panel/menubutton_0.png", "Panel/menubutton_2.png",
    "Panel/runbutton_run_NotPressed.png", "Panel/runbutton_walk_NotPressed.png",
    "Panel/quest_back.png", "Panel/dialog_back.png", "Panel/buysell_back.png",
    "Panel/questsocket_0.png", "Panel/questsocket_1.png", "Panel/questtab_0.png",
    "Panel/goldcoinbtn_0.png", "Panel/buysellbtn_0.png", "Panel/tradebtn_0.png",
    "Panel/minipanelbtn_0.png", "Panel/skltree_a_back_0.png",
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


# ── 8. 通用：一批图的"内容外接矩形"（判画布是否留透明边距 ⇒ 影响 preserveAspect）──
def cmd_bbox(patterns):
    import glob as _glob
    for pat in patterns:
        full = pat if os.path.isabs(pat) else os.path.join(UI, pat)
        files = sorted(_glob.glob(full))
        if not files:
            print("  %s -> 0 个文件" % pat)
            continue
        print("== bbox %s（%d 个文件）==" % (pat, len(files)))
        for p in files:
            im = Image.open(p).convert("RGBA")
            w, h = im.size
            px = im.load()
            rows = [y for y in range(h) if any(px[x, y][3] > 32 for x in range(w))]
            cols = [x for x in range(w) if any(px[x, y][3] > 32 for y in range(h))]
            if not rows:
                print("  %-34s %dx%d 全透明" % (os.path.basename(p), w, h))
                continue
            print("  %-34s %dx%d  content x %d..%d / y %d..%d  (透明边距 L%d R%d T%d B%d)"
                  % (os.path.basename(p), w, h, min(cols), max(cols), min(rows), max(rows),
                     min(cols), w - 1 - max(cols), min(rows), h - 1 - max(rows)))
    return 0


# ── 9. 中文字形覆盖：运行时文本里每个 CJK 字能否落到原版 chi 字模？ ──────────
def _load_chi_map(size):
    """读 `font{N}_chi_map.txt` → set(码位)。格式：COLS/CELL/COUNT 表头 + `code frame advance col row`。"""
    p = os.path.join(ROOT, "client", "Assets", "Resources", "Clover", "D2", "Fonts",
                     "font%d_chi_map.txt" % size)
    if not os.path.exists(p):
        return None
    codes = set()
    with open(p, "r", encoding="utf-8", errors="replace") as f:
        for line in f:
            line = line.strip()
            if not line or line[0] == "#":
                continue
            if line.startswith("COLS") or line.startswith("CELL") or line.startswith("COUNT"):
                continue
            parts = line.split(" ")
            if len(parts) < 5:
                continue
            try:
                codes.add(int(parts[0]))
            except ValueError:
                pass
    return codes


def _load_s2t():
    p = os.path.join(ROOT, "client", "Assets", "Resources", "Clover", "D2", "Fonts",
                     "font_chi_s2t.txt")
    if not os.path.exists(p):
        return None
    m = {}
    with open(p, "r", encoding="utf-8", errors="replace") as f:
        for line in f:
            line = line.strip()
            if not line or line[0] == "#" or line.startswith("COUNT"):
                continue
            parts = line.split(" ")
            if len(parts) < 2:
                continue
            try:
                m[int(parts[0])] = int(parts[1])
            except ValueError:
                pass
    return m


def cmd_cjk(patterns):
    import glob as _glob
    maps = {}
    for size in (16, 24, 30, 42):
        codes = _load_chi_map(size)
        maps[size] = codes
        print("font%d_chi_map.txt : %s" % (size, "取不到" if codes is None else "%d 个码位" % len(codes)))
    s2t = _load_s2t()
    print("font_chi_s2t.txt  : %s" % ("取不到" if s2t is None else "%d 条" % len(s2t)))

    if maps.get(16) is None or s2t is None:
        print("[cjk] 缺字模映射表 ⇒ 无法判（这本身就是缺陷：中文会整条不显示）")
        return 1

    def renderable(cp, size):
        m = maps.get(size)
        if m is None:
            return True
        if cp in m:
            return True
        alt = s2t.get(cp)
        return alt is not None and alt in m

    files = []
    for pat in patterns:
        full = pat if os.path.isabs(pat) else os.path.join(ROOT, pat)
        files += _glob.glob(full, recursive=True)
    files = sorted(set(files))
    if not files:
        print("[cjk] 没有文件匹配 %s" % patterns)
        return 1

    import re
    # 只取**字符串字面量**里的字（注释里的字不上屏：本轮实测 76 个"缺字形"全是注释里的
    # ``(U+26D4 U+FE0F) 与日文「対」(U+5BFE)，属噪声 —— 判据必须只盯上屏文本）
    lit_re = re.compile(r'"((?:[^"\\]|\\.)*)"', re.S)

    total_bad = 0
    for p in files:
        try:
            txt = open(p, "r", encoding="utf-8", errors="replace").read()
        except Exception as e:
            print("  [skip] %s (%s)" % (p, e))
            continue
        chars = set()
        for m in lit_re.finditer(txt):
            for ch in m.group(1):
                if ord(ch) >= 0x2E80:      # CJK 区起（含汉字/全角标点/假名）
                    chars.add(ch)
        bad = sorted(c for c in chars if not renderable(ord(c), 16))
        if bad:
            total_bad += len(bad)
            print("  %-64s CJK %5d  缺字形 %d : %s"
                  % (os.path.relpath(p, ROOT), len(chars), len(bad),
                     " ".join("%s(U+%04X)" % (c, ord(c)) for c in bad)))
    print("[cjk] 结论：扫描 %d 个文件，缺字形合计 %d 个" % (len(files), total_bad))
    return 0


# ── 10. 图标命中率：配表 code/技能 → 磁盘上真的有那张原版图？ ─────────────────
def _read_tsv(rel, key_col, want_cols):
    p = os.path.join(ROOT, rel)
    if not os.path.exists(p):
        return None
    with open(p, "r", encoding="utf-8", errors="replace") as f:
        lines = [l.rstrip("\r\n") for l in f if l.strip()]
    if not lines:
        return None
    head = lines[0].split("\t")
    idx = {}
    for c in want_cols + [key_col]:
        idx[c] = head.index(c) if c in head else -1
    rows = []
    for l in lines[1:]:
        f2 = l.split("\t")
        if len(f2) < len(head):
            continue
        rows.append({c: (f2[idx[c]] if idx[c] >= 0 else "") for c in idx})
    return rows


def _cpp_alias_table():
    """从 `UI/D2Icon.cs` 的 `IconFileAlias` 初始化器里抽出别名表（判据 = 工程真正装载的那张表）。"""
    p = os.path.join(ROOT, "client", "Assets", "Scripts", "UI", "D2Icon.cs")
    if not os.path.exists(p):
        return None
    import re
    txt = open(p, "r", encoding="utf-8", errors="replace").read()
    body = re.search(r"IconFileAlias\s*=\s*new Dictionary<string, string>\s*\{(.*?)\};", txt, re.S)
    if not body:
        return None
    return dict(re.findall(r'\{\s*"([^"]+)"\s*,\s*"([^"]+)"\s*\}', body.group(1)))


def cmd_icons():
    items = _read_tsv("client/Assets/StreamingAssets/Table/Item.tsv", "id", ["code", "name"])
    skills = _read_tsv("client/Assets/StreamingAssets/Table/Skill.tsv", "id",
                       ["class", "official_id", "name", "code"])
    classes = _read_tsv("client/Assets/StreamingAssets/Table/Class.tsv", "id", ["code", "skill_class"])
    alias = _cpp_alias_table()
    if items is None or skills is None or classes is None or alias is None:
        print("[icons] 缺输入（Item.tsv / Skill.tsv / Class.tsv / D2Icon.cs）")
        return 1

    items_dir = os.path.join(ROOT, "client", "Assets", "Resources", "Clover", "D2", "Items")
    icon_dir = os.path.join(ROOT, "client", "Assets", "Resources", "Clover", "D2", "UI", "SkillIcon")

    print("== 物品图标（`D2/Items/inv{code 经别名表}.png`）==")
    hit = miss = 0
    miss_list = []
    for r in items:
        code = r.get("code", "")
        if not code or code == "-":
            continue
        f = alias.get(code, code)
        if os.path.exists(os.path.join(items_dir, "inv%s.png" % f)):
            hit += 1
        else:
            miss += 1
            miss_list.append("%s/%s(%s)" % (r.get("id"), code, r.get("name")))
    print("  命中 %d / 缺 %d（共 %d）" % (hit, miss, hit + miss))
    if miss_list:
        print("  缺：%s" % " ".join(miss_list))

    print("== 技能图标（`D2/UI/SkillIcon/{skill_class}Skillicon_{(official_id-start)*2(+1)}.png`）==")
    cls_by_id = {r.get("id"): r for r in classes}
    s_hit = s_miss = 0
    s_miss_list = []
    frames_seen = set()
    for r in skills:
        try:
            cid = int(r.get("class", "0"))
            oid = int(r.get("official_id", "-1"))
        except ValueError:
            continue
        row = cls_by_id.get(str(cid))
        if row is None:
            continue
        sc = row.get("skill_class") or ""
        if not sc:
            continue
        idx = (oid - (6 + 30 * (cid - 1))) * 2
        if idx < 0:
            s_miss += 1
            s_miss_list.append("%s/oid=%d(idx<0)" % (r.get("id"), oid))
            continue
        for d in (0, 1):
            for ext in (".png",):
                fn = "%sSkillicon_%d%s" % (sc, idx + d, ext)
                frames_seen.add(fn)
                if os.path.exists(os.path.join(icon_dir, fn)):
                    if d == 0:
                        s_hit += 1
                elif d == 0:
                    s_miss += 1
                    s_miss_list.append("%s/%s(oid=%d idx=%d)" % (r.get("id"), sc, oid, idx))
    print("  命中 %d / 缺 %d（共 %d）" % (s_hit, s_miss, s_hit + s_miss))
    if s_miss_list:
        print("  缺：%s" % " ".join(s_miss_list[:20]))
    return 0


# ── 11. 素材双套对比：`X__00__NN.png`（参考工程副本） vs `X_NN.png`（本项目 DC6 导出）──
#  判什么：两套是否**同一画面**、差异是否**只在 alpha**（`(0,0,0,0)` vs `(0,0,0,255)`）。
#  为什么要判：D2 的 DC6 口径是「调色板索引 0 = 透明」（`tools/d2codec/dc6.py:frame_rgba`），
#  参考工程副本把索引 0 写成了**不透明黑** ⇒ 用它做底图会在画面上留下黑边/黑角。
PAIR_STEMS = [
    ("minipanelbtn_%d.png", "minipanelbtn__00__%02d.png", 16),
    ("menubutton_%d.png", "menubutton__0__%d.png", 4),
]


def _rgb_equal(a, b):
    """两图尺寸相同且**逐像素 RGB 相同**（忽略 alpha）？"""
    if a.size != b.size:
        return False
    pa, pb = a.load(), b.load()
    w, h = a.size
    for y in range(h):
        for x in range(w):
            if pa[x, y][:3] != pb[x, y][:3]:
                return False
    return True


def _alpha_black_pad(a, b):
    """差异是否全部是「DC6 导出 = (0,0,0,0) / 参考副本 = (0,0,0,255)」这一对？返回差异像素数或 -1。"""
    pa, pb = a.load(), b.load()
    w, h = a.size
    n = 0
    for y in range(h):
        for x in range(w):
            va, vb = pa[x, y], pb[x, y]
            if va == vb:
                continue
            if va == (0, 0, 0, 0) and vb == (0, 0, 0, 255):
                n += 1
            else:
                return -1
    return n


def cmd_pairs():
    print("== 双套素材：DC6 导出（`X_N.png`） vs 参考工程副本（`X__00__N.png` / 描述名） ==")
    print("   ⚠️ w4 已把 20 个副本（`minipanelbtn__00__*` / `menubutton__0__*`）与 4 个 `ResPaths."
          "PanelArrow*` 一起收口 ⇒ 副本那一侧**应当全部 MISSING**（MISSING = 期望，不是缺素材）。")
    tot = same_alpha = different = 0
    for fa, fb, n in PAIR_STEMS:
        for i in range(n):
            na, nb = fa % i, fb % i
            pa = os.path.join(UI, "Panel", na)
            pb = os.path.join(UI, "Panel", nb)
            if not os.path.exists(pa):
                print("  %-30s vs %-30s **DC6 导出那一侧也缺**（真缺素材！）" % (na, nb))
                continue
            if not os.path.exists(pb):
                print("  %-30s vs %-30s 副本已删除（w4 期望）" % (na, nb))
                continue
            a = Image.open(pa).convert("RGBA")
            b = Image.open(pb).convert("RGBA")
            tot += 1
            if not _rgb_equal(a, b):
                different += 1
                print("  %-30s vs %-30s **画面不同**（不能互换）" % (na, nb))
                continue
            nbad = _alpha_black_pad(a, b)
            if nbad >= 0:
                same_alpha += 1
                print("  %-30s vs %-30s 同画面；alpha 差 %d 像素（DC6=透明 / 副本=不透明黑）"
                      % (na, nb, nbad))
            else:
                different += 1
                print("  %-30s vs %-30s 同 RGB 但 alpha 差**不是**纯黑填充" % (na, nb))
    print("[pairs] 共 %d 对：同画面且差异=黑填充 %d，其它 %d" % (tot, same_alpha, different))

    # 描述名 ↔ `_N` 的映射（按 RGB 相同自动配对；不许靠猜名字）
    print("\n== 描述名 → DC6 `_N` 的**自动配对**（判据 = 逐像素 RGB 相同） ==")
    named = ["runbutton_run_NotPressed.png", "runbutton_run_Pressed.png",
             "runbutton_walk_NotPressed.png", "runbutton_walk_Pressed.png"]
    # w4：`menubutton__0__*` / `minipanelbtn__00__*` 已从磁盘删除 ⇒ 不再列入；
    #    它们的「描述名 → DC6 帧号」配对结果已固化在 `UI/UiArt.cs` 的注释里（walk 0/1、run 2/3）。

    cands = sorted([f for f in os.listdir(os.path.join(UI, "Panel"))
                    if f.endswith(".png") and not f.endswith("_.png") and "__" not in f
                    and f.startswith(("menubutton_", "minipanelbtn_", "runbutton_"))])
    for nm in named:
        pa = os.path.join(UI, "Panel", nm)
        if not os.path.exists(pa):
            print("  %-34s MISSING" % nm)
            continue
        a = Image.open(pa).convert("RGBA")
        match = None
        for c in cands:
            b = Image.open(os.path.join(UI, "Panel", c)).convert("RGBA")
            if _rgb_equal(a, b):
                match = c
                break
        print("  %-34s -> %s" % (nm, match if match else "（磁盘上没有同画面的 `_N` 文件）"))
    return 0


CMDS = {
    "sizes": cmd_sizes,
    "minipanel": cmd_minipanel,
    "belt": cmd_belt,
    "inv": cmd_inv,
    "cursor": cmd_cursor,
    "charstat": cmd_charstat,
    "icons": cmd_icons,
    "pairs": cmd_pairs,
}


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--all", action="store_true")
    ap.add_argument("--art", metavar="REL", action="append", default=[],
                    help="打印该图（相对 D2/UI/ 的路径，可重复）的 ASCII 灰度图")
    ap.add_argument("--bbox", metavar="GLOB", action="append", default=[],
                    help="打印一批图的内容外接矩形（相对 D2/UI/ 的通配符，可重复）")
    ap.add_argument("--cjk", metavar="GLOB", action="append", default=[],
                    help="扫一批文本文件里的 CJK 字是否能在原版 chi 字模里找到（相对仓库根的通配符）")
    for k in CMDS:
        ap.add_argument("--" + k, action="store_true")
    a = ap.parse_args()

    picked = [k for k in CMDS if getattr(a, k.replace("-", "_"))]
    if not a.art and not a.bbox and not a.cjk and (a.all or not picked):
        picked = list(CMDS)

    rc = 0
    for rel in a.art:
        rc |= cmd_art(rel)
        print("")
    if a.bbox:
        rc |= cmd_bbox(a.bbox)
        print("")
    if a.cjk:
        rc |= cmd_cjk(a.cjk)
        print("")
    for k in picked:
        rc |= CMDS[k]()
        print("")
    print("ROOT=%s" % ROOT)
    return rc


if __name__ == "__main__":
    sys.exit(main())
