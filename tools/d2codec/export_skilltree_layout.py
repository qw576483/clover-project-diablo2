# -*- coding: utf-8 -*-
"""技能树面板版面生成器 —— 从**原版底图**里解析「节点框 / 系页签 / 说明窗」，落成强类型表。

产物：`client/Assets/Scripts/Def/SkillTreeLayout.cs`（**生成物，禁止手改**）。

═══ 口径（全部可复跑，不是估的） ═══════════════════════════════════════════════

输入三份**权威载体**：

| 来源 | 用途 |
|---|---|
| `client/Assets/Resources/Clover/D2/UI/Panel/skltree_{a,b,n,p,s}_back_{0..3}.png` | **原版像素**：节点框/连线/箭头/页签/说明窗的真身（320×432 ×4 页 ×5 职业，由 `export_d2ui.py::group_panels` 从 `SPELLS/skltree_*.DC6` tile 拼装 + 纵切） |
| `原版资源/参考工程_Diablerie/d2lod1.10txt/data/global/excel/skilldesc.txt` | 原版技能屏布局表：`SkillPage` / `SkillRow` / `SkillColumn` |
| `client/Assets/StreamingAssets/Table/Skill.tsv` | 我们的 `skill_c` 主键 `id` ↔ 原版 `official_id` |

**关键口径（实测，不是推断）**：`skilldesc.txt` 的**行号 = official_id + 1**
（实测：`magic arrow` 的 official_id=6 ↔ skilldesc 第 7 行 = magic arrow / Page 1 / Row 1 / Col 2）。

**页 ↔ 系**：底图第 k 页（k=1,2,3）↔ 原版 `SkillPage = k`，依据是**两条互相独立的原版像素特征**：

1. **节点框位置集合**：底图里每个节点框的「竖直边框段（长 ≈50px）」落在 3 列 × 6 行网格上；
   把检测到的 (列, 行) 集合与 `skilldesc.txt` 该 Page 的 (SkillColumn, SkillRow) 集合对比 ——
   **15/15 页完全一致**（不一致就 abort，不猜）。
2. **树区右边框的缺口**：树区右边框（原版 x 225..229）在底图上**断开一段**，断口位置
   **逐页等于该页对应页签所在的槽**（`_1`→下槽 / `_2`→中槽 / `_3`→上槽，5 职业 15 页全一致）。
   ⇒ 页签槽（自上而下）= 系 3 / 系 2 / 系 1。

页 0 是**共用右列**（顶部木框说明窗 + 3 个系页签，5 职业逐像素相同，md5 一致）。

**节点框几何**：原版把每个技能槽画成「┗ 形管线」（左边框 + 底边框，2px 线宽），
不是闭合方框 ⇒ 取**它的外接矩形**作为「节点框」：宽 45 / 高 50（实测两处完整可见的格
：`row6/col1` = x 12..56 / y 356..405、`row5/col2` = x 81..125 / y 287..336）。
3 列左边框 x = 13 / 82 / 150；6 行顶沿 y = 15 / 82 / 152 / 220 / 287 / 356（全 15 页同网格）。

CLI：
  python export_skilltree_layout.py                      # 只校验 + 打印
  python export_skilltree_layout.py --write              # 校验通过后写产物
  python export_skilltree_layout.py --preview <out.png>  # 另出「原版底图 + 图标按 bbox 合成」对照图
                                                          # （离线证据，非交付物；变体见 SKL_ICON_MODE）
"""

import io
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import dc6           # 同目录：PNG 读取（dc6._read_png_rgba）与最小 PNG 写出
import pngio

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(os.path.dirname(HERE))          # <项目根>
CLIENT = os.path.join(REPO, "client")
PANEL_DIR = os.path.join(CLIENT, "Assets", "Resources", "Clover", "D2", "UI", "Panel")
SKILLICON_DIR = os.path.join(CLIENT, "Assets", "Resources", "Clover", "D2", "UI", "SkillIcon")
SKILL_TSV = os.path.join(CLIENT, "Assets", "StreamingAssets", "Table", "Skill.tsv")
SKILLDESC = os.path.join(REPO, "原版资源", "参考工程_Diablerie", "d2lod1.10txt",
                         "data", "global", "excel", "skilldesc.txt")
OUT_CS = os.path.join(CLIENT, "Assets", "Scripts", "Def", "SkillTreeLayout.cs")

# 底图文件名里的职业字母 = `class_c.skill_class` 的首字母（ama/sor/nec/pal/bar）
LETTERS = ("a", "b", "n", "p", "s")
CLS_LETTER = {1: "a", 2: "s", 3: "n", 4: "p", 5: "b"}   # Amazon/Sorceress/Necromancer/Paladin/Barbarian
PAGE_W, PAGE_H = 320, 432
TREE_COUNT = 3
ROWS, COLS = 6, 3
CELL_W, CELL_H = 45, 50                      # 节点框外接矩形（原版 px，实测）
COL_X = (13, 82, 150)                        # 3 列左边框 x（= 网格列，实测）
ROW_Y = (15, 82, 152, 220, 287, 356)         # 6 行顶沿 y（= 网格行，实测）
COVER_MIN = 0.5                              # 左边缘在该格纵向的亮像素覆盖率门限

BRIGHT = 122        # 亮层阈值（石纹最高约 105）
DARK = 40           # 「黑窗内区」阈值


# ─────────────────────────────────────────────────────────────────────────────
#  读图
# ─────────────────────────────────────────────────────────────────────────────
def read_png(path):
    return dc6._read_png_rgba(path)


def lum_at(rgba, w, x, y):
    o = (y * w + x) * 4
    if rgba[o + 3] == 0:
        return None
    return (rgba[o] * 299 + rgba[o + 1] * 587 + rgba[o + 2] * 114) // 1000


def bright(rgba, w, x, y, thr=BRIGHT):
    v = lum_at(rgba, w, x, y)
    return v is not None and v >= thr


def warm(rgba, w, x, y):
    """暖色（金/木饰条）：非中性且偏暖。"""
    o = (y * w + x) * 4
    if rgba[o + 3] == 0:
        return False
    r, g, b = rgba[o], rgba[o + 1], rgba[o + 2]
    return (r - b) >= 16 and (r * 299 + g * 587 + b * 114) // 1000 >= 55


# ─────────────────────────────────────────────────────────────────────────────
#  † 节点框检测：亮层里的「竖直边框段」（长 38..58px）
# ─────────────────────────────────────────────────────────────────────────────
def vertical_runs(rgba, w, h, x, gap=3, minlen=30):
    out = []
    y = 0
    while y < h:
        if bright(rgba, w, x, y):
            y0, ye, miss = y, y, 0
            while y < h:
                if bright(rgba, w, x, y):
                    ye, miss = y, 0
                else:
                    miss += 1
                    if miss > gap:
                        break
                y += 1
            if ye - y0 + 1 >= minlen:
                out.append((y0, ye))
            y = ye + 1
        else:
            y += 1
    return out


def cell_cover(rgba, w, h, col0, row0):
    """网格格 (col0,row0) 的「左边缘纵向亮像素覆盖率」= max over x∈[COL_X-2, COL_X+3]
    的（该格纵向带内亮像素数 / 带长）。
    <para>为什么用覆盖率而不是"找一条长竖线"：原版的横向管线/暗箭头会**横穿**左边缘，
    把一条 50px 的竖线截成短段（实测 a_2 的 (row5,col2) 只剩 42px）⇒ 按长段判定会漏格。
    覆盖率对截断不敏感，且**同时**验证了网格标定（不该有格的格子覆盖率必须低）。</para>
    """
    x0, x1 = COL_X[col0], COL_X[col0]
    y0, y1 = ROW_Y[row0] + 2, ROW_Y[row0] + CELL_H - 3
    best = 0.0
    for x in range(x0 - 2, x1 + 4):
        if x < 0 or x >= w:
            continue
        n = 0
        for y in range(y0, y1 + 1):
            if bright(rgba, w, x, y):
                n += 1
        best = max(best, n / float(y1 - y0 + 1))
    return best


def detect_cells(rgba, w, h):
    """返回 [(col0, row0, x, y_top, cover)]（col/row 为 0 基；x/y_top = 网格原版 px）。"""
    out = []
    for c in range(COLS):
        for r in range(ROWS):
            cov = cell_cover(rgba, w, h, c, r)
            if cov >= COVER_MIN:
                out.append((c, r, COL_X[c], ROW_Y[r], cov))
    return out


# ─────────────────────────────────────────────────────────────────────────────
#  † 页签槽 / 说明窗
# ─────────────────────────────────────────────────────────────────────────────
def _warm_components(rgba, w, h):
    mask = bytearray(w * h)
    for y in range(h):
        for x in range(230, 320):
            if warm(rgba, w, x, y):
                mask[y * w + x] = 1
    seen = bytearray(w * h)
    boxes = []
    for i in range(w * h):
        if not mask[i] or seen[i]:
            continue
        stack = [i]
        seen[i] = 1
        n = 0
        x0, y0, x1, y1 = w, h, -1, -1
        while stack:
            p = stack.pop()
            n += 1
            x, y = p % w, p // w
            x0, x1 = min(x0, x), max(x1, x)
            y0, y1 = min(y0, y), max(y1, y)
            for dx in (-1, 0, 1):
                for dy in (-1, 0, 1):
                    nx, ny = x + dx, y + dy
                    if 230 <= nx < w and 0 <= ny < h:
                        j = ny * w + nx
                        if mask[j] and not seen[j]:
                            seen[j] = 1
                            stack.append(j)
        boxes.append((n, x0, y0, x1, y1))
    return boxes


def detect_wood_frame(rgba, w, h):
    """页 0 顶部那个**木框**（说明窗的外框）：返回 (框 bbox, 金饰条宽度)。
    饰条宽度 = 从 bbox 左沿起、连续「该列暖色占比 >= 0.5」的列数。"""
    cand = [b for b in _warm_components(rgba, w, h)
            if b[2] < 108 and 70 <= (b[3] - b[1] + 1) <= 95 and 90 <= (b[4] - b[2] + 1) <= 110]
    if not cand:
        return None, None
    n, x0, y0, x1, y1 = max(cand, key=lambda b: b[0])
    trim = 0
    for x in range(x0, x0 + 20):
        cnt = 0
        for y in range(y0, y1 + 1):
            if warm(rgba, w, x, y):
                cnt += 1
        if cnt >= (y1 - y0 + 1) * 0.5:
            trim += 1
        else:
            break
    return (x0, y0, x1, y1), trim


def detect_tab_slots(rgba, w, h):
    """页 0 的 3 个系页签（暖色饰条围出的石牌）：[(x0,y0,x1,y1)] 自上而下。"""
    boxes = [(x0, y0, x1, y1) for (n, x0, y0, x1, y1) in _warm_components(rgba, w, h)
             # 顶部木框（y 2..106）尺寸与页签相近 ⇒ 用 y0 排除（页签从 y≈112 起）
             if n >= 15 and y0 >= 108
             and 70 <= (x1 - x0 + 1) <= 95 and 90 <= (y1 - y0 + 1) <= 110]
    boxes.sort(key=lambda b: b[1])
    return boxes


def detect_desc_window(rgba, w, h):
    """页 0 顶部木框里的**黑窗内区**（说明文字可见区）= 木框内 (lum < DARK) 像素的并集外接矩形。
    <para>木框自身有暗木纹 ⇒ 用「该行/列的暗像素数够多」把它滤掉：
    只保留 x∈[246,306] × y∈[40,100] 这个窗口（内区实测在其中），并要求该行暗像素 ≥ 30。</para>
    """
    rows = []
    for y in range(40, 105):
        n = 0
        for x in range(248, 305):
            v = lum_at(rgba, w, x, y)
            if v is not None and v < DARK:
                n += 1
        if n >= 38:                      # 黑窗内区是一整条暗带；木纹/卷草纹的暗像素远达不到
            rows.append(y)
    if not rows:
        return None
    y0, y1 = min(rows), max(rows)
    cols = []
    for x in range(248, 305):
        n = 0
        for y in range(y0, y1 + 1):
            v = lum_at(rgba, w, x, y)
            if v is not None and v < DARK:
                n += 1
        if n >= (y1 - y0 + 1) * 0.85:
            cols.append(x)
    if not cols:
        return None
    return (min(cols), y0, max(cols), y1)


def detect_tree_border_gap(rgba, w, h):
    """树区右边框（x 225..229）的缺口 y 区间（= 该页被选中的页签槽）。"""
    on = []
    for y in range(h):
        hit = False
        for x in range(225, 230):
            v = lum_at(rgba, w, x, y)
            if v is not None and v >= 100:
                hit = True
                break
        on.append(hit)
    segs = []
    y = 0
    while y < h:
        if on[y]:
            y0, ye, miss = y, y, 0
            while y < h:
                if on[y]:
                    ye, miss = y, 0
                else:
                    miss += 1
                    if miss > 6:
                        break
                y += 1
            if ye - y0 + 1 >= 15:
                segs.append((y0, ye))
            y = ye + 1
        else:
            y += 1
    if len(segs) >= 2:
        return segs[0][1] + 1, segs[1][0] - 1
    if len(segs) == 1:
        return (segs[0][1] + 1, h - 1) if segs[0][0] <= 3 else (0, segs[0][0] - 1)
    return None


# ─────────────────────────────────────────────────────────────────────────────
#  † 原版布局表（skilldesc）+ 我们的 skill_c
# ─────────────────────────────────────────────────────────────────────────────
def read_layout_table():
    rows = [l.split("\t") for l in io.open(SKILLDESC, encoding="latin-1")]
    out = {}
    for i, r in enumerate(rows):
        if i == 0 or len(r) < 4:
            continue
        try:
            page, rw, col = int(r[1] or 0), int(r[2] or 0), int(r[3] or 0)
        except ValueError:
            continue
        if page:
            out[i] = (page, rw, col)     # 键 = skilldesc 第 i 行 = official_id + 1
    return out


def read_our_skills():
    rows = [l.rstrip("\n").split("\t") for l in io.open(SKILL_TSV, encoding="utf-8")]
    hdr = rows[0]
    i_id, i_cls, i_tree, i_code, i_off = (hdr.index(x) for x in
                                          ("id", "class", "tree", "code", "official_id"))
    out = []
    for r in rows[1:]:
        if len(r) <= i_off:
            continue
        out.append({"id": int(r[i_id]), "cls": int(r[i_cls]), "tree": int(r[i_tree]),
                    "code": r[i_code], "official_id": int(r[i_off])})
    return out


# ═════════════════════════════════════════════════════════════════════════════
def main():
    argv = sys.argv[1:]
    do_write = "--write" in argv
    preview = argv[argv.index("--preview") + 1] if "--preview" in argv else None

    layout = read_layout_table()
    ours = read_our_skills()
    print("skilldesc 布局行 = %d；Skill.tsv 技能 = %d" % (len(layout), len(ours)))

    pages = {}
    for letter in LETTERS:
        for k in range(4):
            p = os.path.join(PANEL_DIR, "skltree_%s_back_%d.png" % (letter, k))
            if not os.path.exists(p):
                raise SystemExit("缺底图：%s" % p)
            w, h, rgba = read_png(p)
            if (w, h) != (PAGE_W, PAGE_H):
                raise SystemExit("%s 尺寸 %dx%d != %dx%d" % (p, w, h, PAGE_W, PAGE_H))
            pages[(letter, k)] = (w, h, rgba)

    ref = pages[("a", 0)][2]
    for letter in LETTERS[1:]:
        if pages[(letter, 0)][2] != ref:
            raise SystemExit("页 0 在 %s 与 a 之间不一致（预期：共用右列，逐像素相同）" % letter)
    print("页 0（共用右列）：5 职业逐像素相同 = OK")

    w0, h0 = PAGE_W, PAGE_H
    r0 = pages[("a", 0)][2]
    tabs = detect_tab_slots(r0, w0, h0)
    if len(tabs) != TREE_COUNT:
        raise SystemExit("页 0 检出页签 %d 个（应为 %d）" % (len(tabs), TREE_COUNT))
    print("系页签槽（自上而下）：%s"
          % ["x %d..%d / y %d..%d" % (t[0], t[2], t[1], t[3]) for t in tabs])
    desc = detect_desc_window(r0, w0, h0)
    if desc is None:
        raise SystemExit("页 0 未检出说明窗（黑窗内区）")
    print("说明窗（黑窗内区）：x %d..%d / y %d..%d = %dx%d"
          % (desc[0], desc[2], desc[1], desc[3], desc[2] - desc[0] + 1, desc[3] - desc[1] + 1))
    wood, trim = detect_wood_frame(r0, w0, h0)
    if wood is None or not trim:
        raise SystemExit("页 0 未检出顶部木框（说明区的可见区）")
    print("说明区木框：x %d..%d / y %d..%d = %dx%d，金饰条宽 %dpx"
          % (wood[0], wood[2], wood[1], wood[3],
             wood[2] - wood[0] + 1, wood[3] - wood[1] + 1, trim))

    expected = {}
    for s in ours:
        page, rw, col = layout[s["official_id"] + 1]
        if page != s["tree"]:
            raise SystemExit("skill_c 的 tree=%d 与原版 SkillPage=%d 不符（技能 %s #%d）"
                             % (s["tree"], page, s["code"], s["id"]))
        expected.setdefault((s["cls"], page), set()).add((rw, col))

    detected = {}
    for letter in LETTERS:
        for k in (1, 2, 3):
            cells = detect_cells(pages[(letter, k)][2], w0, h0)
            detected[(letter, k)] = cells
            got = sorted(set((r + 1, c + 1) for (c, r, _, _, _) in cells))
            print("  %s_%d：检出节点框 %d 个  (row,col) = %s" % (letter, k, len(cells), got))

    bad = 0
    for cls in range(1, 6):
        letter = CLS_LETTER[cls]
        for page in (1, 2, 3):
            exp = expected.get((cls, page), set())
            got = set((r + 1, c + 1) for (c, r, _, _, _) in detected[(letter, page)])
            if exp != got:
                bad += 1
                print("  x cls=%d(%s) page=%d：原版布局 %s != 底图检出 %s"
                      % (cls, letter, page, sorted(exp), sorted(got)))
    print("页 <-> 系 校验（底图节点框 (列,行) 集合 vs skilldesc SkillColumn/SkillRow）："
          + ("15/15 一致" if bad == 0 else "%d/15 不一致 => ABORT" % bad))
    if bad:
        raise SystemExit("节点框集合与原版布局表不一致 => 不生成（不许猜）")

    tab_of_page = {}
    for k in (1, 2, 3):
        gaps = set()
        for letter in LETTERS:
            gaps.add(detect_tree_border_gap(pages[(letter, k)][2], w0, h0))
        if len(gaps) != 1:
            raise SystemExit("页 %d 的树区右边框缺口在 5 职业间不一致：%s" % (k, gaps))
        g = gaps.pop()
        hit = None
        for i, t in enumerate(tabs):
            if abs(g[0] - t[1]) <= 12 and abs(g[1] - t[3]) <= 12:
                hit = i
        if hit is None:
            raise SystemExit("页 %d 的缺口 %s 不对应任何页签槽" % (k, g))
        tab_of_page[k] = hit
        print("  页 %d：右边框缺口 y %d..%d => 命中页签槽 #%d（自上而下 1/2/3）" % (k, g[0], g[1], hit + 1))
    if sorted(tab_of_page.values()) != [0, 1, 2]:
        raise SystemExit("页 <-> 页签槽 不是一一对应：%s" % tab_of_page)

    cellmap = {}
    for (letter, k), cells in detected.items():
        cellmap[(letter, k)] = {(r + 1, c + 1): (x, y) for (c, r, x, y, _) in cells}

    recs = []
    for s in sorted(ours, key=lambda s: s["id"]):
        page, rw, col = layout[s["official_id"] + 1]
        letter = CLS_LETTER[s["cls"]]
        x, y = cellmap[(letter, page)][(rw, col)]
        recs.append((s["id"], s["cls"], s["tree"], rw, col, x, y, CELL_W, CELL_H, s["official_id"]))
    print("生成记录：%d 条（每技能一条，位置 = 原版底图画出的节点框）" % len(recs))

    if preview:
        write_preview(preview, pages, recs)
    if not do_write:
        print("（未加 --write => 只校验，不落盘）")
        return 0
    write_cs(recs, tabs, desc, tab_of_page, wood, trim)
    return 0


# ─────────────────────────────────────────────────────────────────────────────
def write_cs(recs, tabs, desc, tab_of_page, wood, trim):
    L = []
    A = L.append
    A("// -----------------------------------------------------------------------------")
    A("// Diablo2 · Def/SkillTreeLayout.cs")
    A("// [!] **生成物，禁止手改** —— 由 `tools/d2codec/export_skilltree_layout.py` 生成。")
    A("//")
    A("// 口径（复跑：`python tools/d2codec/export_skilltree_layout.py --write`）：")
    A("//   · 节点框位置：从原版 `SPELLS/skltree_{a,b,n,p,s}_back.DC6` tile 拼装出的 320x432 页")
    A("//     （`Resources/Clover/D2/UI/Panel/skltree_{cls}_back_{0..3}.png`）**逐像素解析**得到；")
    A("//     坐标 = **原版 px**，原点 = 该页左上角（x 右 / y 下）。")
    A("//   · 页 <-> 系：底图第 k 页 <-> 原版 `SkillPage = k`（k=1,2,3），")
    A("//     依据 ① 底图节点框 (列,行) 集合 == `skilldesc.txt` 该 Page 的 (SkillColumn,SkillRow)")
    A("//            （15/15 页一致）")
    A("//     依据 ② 树区右边框的缺口逐页落在对应页签槽（15/15 页一致）")
    A("//     => **两条独立特征互证**；任一不成立时生成器 abort（不猜）。")
    A("//   · `skilldesc.txt` 行号 = `official_id + 1`（实测）。")
    A("//   · 页签槽：页 0（共用右列，5 职业逐像素相同）解析；**自上而下 = 系 3 / 系 2 / 系 1**。")
    A("//   · 节点框 = 原版画出的「L 形管线」的外接矩形（实测量处完整可见的格 = 45x50）。")
    A("//")
    A("// 为什么这份表在 `Def/` 而不在 `Module/Skill/`：")
    A("//   `tools/ai-skill/conventions.md` 硬性规定「**UI 不许 using Diablo2.Module.**」；")
    A("//   面板（`UI/SkillTreePanel`）要读这份表 => 它只能是**无逻辑的纯数据**（`Def/` 的定位）。")
    A("// -----------------------------------------------------------------------------")
    A("")
    A("namespace Diablo2.Def")
    A("{")
    A("    /// <summary>原版 px 矩形（原点 = 320x432 底图页左上角）。</summary>")
    A("    public struct SkillArtRect")
    A("    {")
    A("        public float x;")
    A("        public float y;")
    A("        public float w;")
    A("        public float h;")
    A("")
    A("        public SkillArtRect(float x, float y, float w, float h)")
    A("        {")
    A("            this.x = x;")
    A("            this.y = y;")
    A("            this.w = w;")
    A("            this.h = h;")
    A("        }")
    A("    }")
    A("")
    A("    /// <summary>一个技能槽的**原版像素**位置（`skillId` = 我们 `skill_c` 的主键）。</summary>")
    A("    public struct SkillTreeCell")
    A("    {")
    A("        /// <summary>`skill_c` 主键（= `SkillDef.id`）。</summary>")
    A("        public int skillId;")
    A("")
    A("        /// <summary>职业 1..5（= `PlayerClass`）。</summary>")
    A("        public int cls;")
    A("")
    A("        /// <summary>系 1..3（= 原版 `SkillPage`；面板页号也用 1..3）。</summary>")
    A("        public int tree;")
    A("")
    A("        /// <summary>原版 `SkillRow`（1..6）。</summary>")
    A("        public int row;")
    A("")
    A("        /// <summary>原版 `SkillColumn`（1..3）。</summary>")
    A("        public int col;")
    A("")
    A("        /// <summary>原版底图画出的节点框（外接矩形）。</summary>")
    A("        public SkillArtRect box;")
    A("    }")
    A("")
    A("    /// <summary>技能树面板版面表（**生成物**，见文件头）。</summary>")
    A("    public static class SkillTreeLayout")
    A("    {")
    A("        /// <summary>底图页尺寸（原版 px）。</summary>")
    A("        public const int PageW = %d;" % PAGE_W)
    A("")
    A("        public const int PageH = %d;" % PAGE_H)
    A("")
    A("        /// <summary>系个数（= 底图第 1/2/3 页）。</summary>")
    A("        public const int TreeCount = %d;" % TREE_COUNT)
    A("")
    A("        /// <summary>节点框尺寸（原版 px；实测两处完整可见的格 = 45x50）。</summary>")
    A("        public const float CellW = %df;" % CELL_W)
    A("")
    A("        public const float CellH = %df;" % CELL_H)
    A("")
    A("        /// <summary>说明区**木框**的外沿 bbox（原版 px；页 0 顶部那个木框）。</summary>")
    A("        public static readonly SkillArtRect WoodFrame =")
    A("            new SkillArtRect(%df, %df, %df, %df);"
      % (wood[0], wood[1], wood[2] - wood[0] + 1, wood[3] - wood[1] + 1))
    A("")
    A("        /// <summary>木框金饰条宽度（原版 px）⇒ 可见区 = <see cref=\"WoodFrame\"/> 内缩这么多。</summary>")
    A("        public const float WoodTrim = %df;" % trim)
    A("")
    A("        /// <summary>说明文字可见区 = 页 0 顶部木框里的**黑窗内区**（原版 px）。</summary>")
    A("        public static readonly SkillArtRect DescWindow =")
    A("            new SkillArtRect(%df, %df, %df, %df);"
      % (desc[0], desc[1], desc[2] - desc[0] + 1, desc[3] - desc[1] + 1))
    A("")
    A("        /// <summary>")
    A("        /// 3 个**系页签**的点击区（原版 px，索引 = **系 - 1**）。")
    A("        /// <para>实测：页签槽自上而下 = 系 3 / 系 2 / 系 1（依据 = 树区右边框缺口 +")
    A("        /// 底图上被点亮的那个页签，两条独立特征互证，见文件头）=> 这里按「系」排。</para>")
    A("        /// </summary>")
    A("        public static readonly SkillArtRect[] TabSlots =")
    A("        {")
    for k in (1, 2, 3):
        t = tabs[tab_of_page[k]]
        A("            // 系 %d <= 页签槽 #%d（自上而下）" % (k, tab_of_page[k] + 1))
        A("            new SkillArtRect(%df, %df, %df, %df),"
          % (t[0], t[1], t[2] - t[0] + 1, t[3] - t[1] + 1))
    A("        };")
    A("")
    A("        /// <summary>全部技能槽（%d 条 = 5 职业 x 3 系 x 每系技能数）。</summary>" % len(recs))
    A("        public static readonly SkillTreeCell[] Cells =")
    A("        {")
    for sid, cls, tree, row, col, x, y, cw, ch, _off in recs:
        A("            new SkillTreeCell { skillId = %d, cls = %d, tree = %d, row = %d, col = %d,"
          % (sid, cls, tree, row, col))
        A("                                box = new SkillArtRect(%df, %df, %df, %df) },"
          % (x, y, cw, ch))
    A("        };")
    A("")
    A("        /// <summary>按 `skillId` 取槽位；表里没有（配表被改过）=> 返回 false。</summary>")
    A("        public static bool TryGet(int skillId, out SkillTreeCell cell)")
    A("        {")
    A("            for (var i = 0; i < Cells.Length; i++)")
    A("            {")
    A("                if (Cells[i].skillId == skillId)")
    A("                {")
    A("                    cell = Cells[i];")
    A("                    return true;")
    A("                }")
    A("            }")
    A("            cell = default(SkillTreeCell);")
    A("            return false;")
    A("        }")
    A("")
    A("        /// <summary>某职业某个系（1..3）的槽位数。</summary>")
    A("        public static int CountOf(int cls, int tree)")
    A("        {")
    A("            var n = 0;")
    A("            for (var i = 0; i < Cells.Length; i++)")
    A("            {")
    A("                if (Cells[i].cls == cls && Cells[i].tree == tree) n++;")
    A("            }")
    A("            return n;")
    A("        }")
    A("    }")
    A("}")
    A("")
    with io.open(OUT_CS, "w", encoding="utf-8", newline="\n") as f:
        f.write("\n".join(L))
    print("WROTE %s（%d 行）" % (OUT_CS, len(L)))


# ─────────────────────────────────────────────────────────────────────────────
#  离线对照图（**不是交付物**）：把技能图标按解析出的 bbox 画到原版底图上。
#  变体（环境变量 SKL_ICON_MODE）：
#    inner   = 图标按「框的内径」等比内缩（默认口径）
#    native  = 图标按原版 48x48 原尺寸贴（中心对齐）
#    stretch = 图标拉伸填满内径
# ─────────────────────────────────────────────────────────────────────────────
def write_preview(path, pages, recs):
    mode = os.environ.get("SKL_ICON_MODE", "inner")
    _, _, base = pages[("a", 1)]
    canvas = bytearray(base)
    icons = {}
    for name in sorted(os.listdir(SKILLICON_DIR)):
        if name.startswith("amaSkillicon_") and name.endswith(".png"):
            icons[int(name[len("amaSkillicon_"):-4])] = os.path.join(SKILLICON_DIR, name)

    def blit(src, sw, sh, dx, dy, dw, dh):
        """最近邻缩放贴图（保留 alpha）。"""
        for y in range(dh):
            sy = min(sh - 1, int(y * sh / dh))
            ty = dy + y
            if ty < 0 or ty >= PAGE_H:
                continue
            for x in range(dw):
                sx = min(sw - 1, int(x * sw / dw))
                tx = dx + x
                if tx < 0 or tx >= PAGE_W:
                    continue
                so = (sy * sw + sx) * 4
                if src[so + 3] == 0:
                    continue
                do = (ty * PAGE_W + tx) * 4
                canvas[do:do + 4] = src[so:so + 4]

    for sid, cls, tree, row, col, x, y, cw, ch, off in recs:
        if cls != 1 or tree != 1:
            continue
        # 图标帧号 = (official_id − (6 + 30×(class−1))) × 2（出处：`ResPaths.SkillIcon` 注释
        # 与 `export_d2ui.py::group_skillicons`）；Amazon class=1 ⇒ (off − 6) × 2。
        frame = (off - (6 + 30 * (cls - 1))) * 2
        if frame not in icons:
            print("  预览：缺口图 amaSkillicon_%d ⇒ 跳过" % frame)
            continue
        sw, sh, src = read_png(icons[frame])
        if mode == "native":
            blit(src, sw, sh, int(round(x + cw * 0.5 - sw * 0.5)),
                 int(round(y + ch * 0.5 - sh * 0.5)), sw, sh)
        elif mode == "stretch":
            blit(src, sw, sh, int(x + 2), int(y + 2), int(cw - 4), int(ch - 4))
        else:
            side = int(min(cw - 4, ch - 4))
            blit(src, sw, sh, int(round(x + cw * 0.5 - side * 0.5)),
                 int(round(y + ch * 0.5 - side * 0.5)), side, side)
    pngio.write_rgba(path, PAGE_W, PAGE_H, canvas)
    print("WROTE 预览 %s（mode=%s）" % (path, mode))


if __name__ == "__main__":
    sys.exit(main())
