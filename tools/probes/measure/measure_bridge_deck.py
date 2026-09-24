# -*- coding: utf-8 -*-
"""量法脚本：判定「营地出门那座桥」的 deck（桥面）格集合 + 桥面/栏杆素材的像素几何。

用法（任意目录，脚本自己向上找仓库根）：
    python tools/probes/measure/measure_bridge_deck.py

为什么留档（判据资产，global skill §1.8）：它是用户症状
「营地出门的桥，还是从桥下走」的**像素级 before 判据** ——
回答"栏杆图形向上溢出北邻格，是素材本身就长这样，还是 MapView 的对齐口径把它推上去的"。
C# 侧的排序断言在 `tools/probes/hosts/mapcheck/Program.cs`（`StepBridgeDeck*`）。

判据（全部是数字，无一处靠"看"）：
  ① 从 `MapGenTownLayout.cs`（生成物）逐格解键，导出 **deck 格集合**（kind='d' 且
     floor 键取自 deck 类包 `moor_bridge`）与 **栏杆格集合**（kind='s' 且 wall 键取自同包）；
  ② 每张 `Tiles/moor_bridge/*.png` / `Objects/moor_bridge/*.png` 的像素尺寸、
     **alpha 内容 bbox**、不透明像素数、唯一色数；
  ③ 把 bbox 换算成"相对它自己那一格"的几何（一格 = 160×80 D2 px；
     1 个 gy 步进 = 40 screen px；地砖顶边贴格顶 / 物件底边贴格底，口径 = `MapView.PlaceOfPx`）；
  ④ 结论行：栏杆内容**向上溢出本格顶边多少行** ⇒ 落在北邻（deck）格上的比例。

口径出处：
  · 一格 = 2×1 世界单位（`GameConst.IsoHalfW/HalfH`）；原版瓦片按 `MapView.D2TilePixelsPerUnit = 80`
    解释 ⇒ 一格 = 160×80 px，`IsoTilePxW/H = 128/64` 是契约值（`GameConst`）不是原版像素。
  · `MapView.PlaceOfPx`（MapView.cs）：地砖顶边贴「格中心上方半格」；物件底边贴「格中心下方半格」。
  · `Iso.SortOrder(g, off) = (gx+gy)*GameConst.SortOrderStep + GameConst.SortOrderBase + off`。
"""

import collections
import os
import re
import sys

try:
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')
except Exception:
    pass

from PIL import Image

# ── 仓库根（向上找含 client/Assets 的那一层）──────────────────────────────────
_here = os.path.dirname(os.path.abspath(__file__))
REPO = _here
while REPO and not os.path.isdir(os.path.join(REPO, 'client', 'Assets')):
    parent = os.path.dirname(REPO)
    if parent == REPO:
        raise SystemExit('找不到含 client/Assets 的仓库根（从 %s 向上）' % _here)
    REPO = parent

CS = os.path.join(REPO, 'client', 'Assets', 'Scripts', 'Module', 'Map', 'MapGenTownLayout.cs')
GC = os.path.join(REPO, 'client', 'Assets', 'Scripts', 'Core', 'GameConst.cs')
MV = os.path.join(REPO, 'client', 'Assets', 'Scripts', 'Module', 'Map', 'MapView.cs')
D2 = os.path.join(REPO, 'client', 'Assets', 'Resources', 'Clover', 'D2')

# deck 类包（= 原版 `OUTDOORS/bridge.dt1` 解出的包名；出处 = `MapGenTownLayout.Packs`）
DECK_PACKS = ('moor_bridge',)

CELL_PX_W = 160          # 一格宽（D2 px） = 2 世界单位 × 80 px/单位
CELL_PX_H = 80           # 一格高（D2 px） = 1 世界单位 × 80 px/单位
ROW_STEP_PX = 40         # gy +1 ⇒ screen y +40（= CELL_PX_H / 2）


def read_array(src, name):
    m = re.search(r'static readonly string\[\]\s+' + name + r'\s*=\s*\{(.*?)\n\s*\};', src, re.S)
    if not m:
        raise SystemExit('解析不到数组 %s' % name)
    return re.findall(r'"([^"]*)"', m.group(1))


def read_int(src, name):
    m = re.search(r'const int\s+' + name + r'\s*=\s*(-?\d+);', src)
    if not m:
        raise SystemExit('解析不到常量 %s' % name)
    return int(m.group(1))


def decode(row, x, packs):
    off = x * 6
    if row is None or off + 6 > len(row):
        return None
    if row[off] == '-':
        return ''
    pid = int(row[off:off + 3])
    if pid < 0 or pid >= len(packs):
        return None
    return packs[pid] + '/' + row[off + 3:off + 6]


def pack_of(key):
    return key.split('/')[0] if key else ''


def alpha_bbox(path):
    """(w, h, col0, col1, row0, row1, opaque) —— row0/row1 相对图像顶边。"""
    im = Image.open(path).convert('RGBA')
    w, h = im.size
    px = im.load()
    col0, col1, row0, row1 = w, -1, h, -1
    opaque = 0
    uniq = set()
    for y in range(h):
        for x in range(w):
            p = px[x, y]
            if p[3] > 0:
                opaque += 1
                uniq.add(p)
                if x < col0:
                    col0 = x
                if x > col1:
                    col1 = x
                if y < row0:
                    row0 = y
                if y > row1:
                    row1 = y
    return w, h, col0, col1, row0, row1, opaque, len(uniq)


def main():
    with open(CS, 'r', encoding='utf-8') as fh:
        src = fh.read()
    W = read_int(src, 'Width')
    H = read_int(src, 'Height')
    packs = read_array(src, 'Packs')
    rows = read_array(src, 'Rows')
    ground = read_array(src, 'GroundRows')
    objects = read_array(src, 'ObjectRows')

    with open(GC, 'r', encoding='utf-8') as fh:
        gcsrc = fh.read()
    step = read_int(gcsrc, 'SortOrderStep')
    base = read_int(gcsrc, 'SortOrderBase')
    off_ground = read_int(gcsrc, 'LayerOffsetGround')
    off_object = read_int(gcsrc, 'LayerOffsetObject')
    off_entity = read_int(gcsrc, 'LayerOffsetEntity')
    off_overlay = read_int(gcsrc, 'LayerOffsetOverlay')
    town_w = read_int(gcsrc, 'TownWidth')
    town_h = read_int(gcsrc, 'TownHeight')

    print('=' * 84)
    print('① 表结构 + 排序常量自证')
    print('=' * 84)
    print('  Rows %dx%d（GameConst.TownWidth/Height = %dx%d，一致=%s）  Packs=%s'
          % (W, H, town_w, town_h, (W, H) == (town_w, town_h), packs))
    for nm, arr, per in (('Rows', rows, 1), ('GroundRows', ground, 6), ('ObjectRows', objects, 6)):
        bad = [i for i, r in enumerate(arr) if len(r) != W * per]
        print('  %-11s 行数=%d 长度异常行=%s' % (nm, len(arr), bad if bad else '无'))
    print('  SortOrder(g,off) = (gx+gy)*%d + %d + off（off: ground=%d object=%d entity=%d overlay=%d）'
          % (step, base, off_ground, off_object, off_entity, off_overlay))

    cells = {}
    for y in range(H):
        for x in range(W):
            cells[(x, y)] = (rows[y][x], decode(ground[y], x, packs), decode(objects[y], x, packs))

    # ── deck / 栏杆 格集合（判据 = 该层瓦片键的包名，不按坐标硬编码）──────────
    deck_all = sorted(c for c, (k, g, o) in cells.items() if pack_of(g or '') in DECK_PACKS)
    deck_walk = sorted(c for c in deck_all if cells[c][0] == 'd')
    deck_blocked = sorted(set(deck_all) - set(deck_walk))
    rail = sorted(c for c, (k, g, o) in cells.items() if pack_of(o or '') in DECK_PACKS)

    print()
    print('=' * 84)
    print('② 桥结构：deck 格集合（floor 键包 = %s）与栏杆格集合（wall 键包 = 同）' % (DECK_PACKS,))
    print('=' * 84)
    print('  deck 格（floor 包 ∈ deck 类包）= %d 格；其中 kind=\'d\'（可走）= %d 格，kind≠\'d\' = %d 格'
          % (len(deck_all), len(deck_walk), len(deck_blocked)))
    for tag, cs in (('可走 deck', deck_walk), ('非可走（栏杆行地面）', deck_blocked), ('栏杆（wall 层）', rail)):
        if not cs:
            continue
        xs = sorted(set(c[0] for c in cs))
        ys = sorted(set(c[1] for c in cs))
        print('  %-16s %2d 格  x=%s  y=%s' % (tag, len(cs), xs, ys))
    print('  ⚠ 注意：栏杆行的**地面**也是 deck 类包地砖（原版桥面同一张图集）'
          '⇒ 只看 floor 键会把 4 行都算 deck；本项目的 deck 判据因此再要求该格 TileKind 可走。')

    print()
    print('=' * 84)
    print('③ 素材像素几何（每张：尺寸 / alpha bbox / 不透明 px / 唯一色 / 相对自己那一格）')
    print('=' * 84)
    print('  口径：一格 = %dx%d D2 px；1 个 gy 步进 = %d screen px；'
          '地砖「顶边贴格顶」/ 物件「底边贴格底」（MapView.PlaceOfPx）'
          % (CELL_PX_W, CELL_PX_H, ROW_STEP_PX))

    geo = {}
    for layer in ('Tiles', 'Objects'):
        d = os.path.join(D2, layer, 'moor_bridge')
        files = sorted(f for f in os.listdir(d) if f.endswith('.png'))
        print()
        print('  ── %s/moor_bridge（实际 %d 张：%s..%s）'
              % (layer, len(files), files[0], files[-1]))
        print('     %-9s %-9s %-19s %-9s %-8s %s'
              % ('file', 'size', 'alpha bbox(c0..c1,r0..r1)', 'opaque', 'uniq', '几何（相对自己那一格）'))
        for fname in files:
            i = int(fname[:3])
            path = os.path.join(d, fname)
            w, h, c0, c1, r0, r1, op, uniq = alpha_bbox(path)
            geo[(layer, i)] = (w, h, c0, c1, r0, r1, op, uniq)
            if op == 0:
                note = '整张全透明（原版不画）'
            elif layer == 'Tiles':
                top = r0                       # 图像顶边 = 格顶 ⇒ 内容顶边相对格顶
                bot = r1
                note = ('顶边贴格顶 ⇒ 内容占屏幕行 %d..%d（本格 0..%d）；越界 %+.1f..%+.1f 行'
                        % (top, bot, CELL_PX_H - 1,
                           top / float(ROW_STEP_PX), (bot - (CELL_PX_H - 1)) / float(ROW_STEP_PX)))
            else:
                top = CELL_PX_H - h + r0       # 图像底边 = 格底 ⇒ 内容顶边相对格顶
                bot = CELL_PX_H - h + r1
                note = ('底边贴格底 ⇒ 内容占屏幕行 %d..%d（本格 0..%d）；**向上溢出格顶 %+.1f 行**'
                        % (top, bot, CELL_PX_H - 1, -top / float(ROW_STEP_PX)))
            print('     %-9s %-9s %-19s %-9d %-8d %s'
                  % ('%03d.png' % i, '%dx%d' % (w, h), '%d..%d r%d..%d' % (c0, c1, r0, r1),
                     op, uniq, note))

    print()
    print('=' * 84)
    print('④ 结论（逐条给数字）')
    print('=' * 84)
    rail_geo = [geo[('Objects', i)] for i in range(20) if ('Objects', i) in geo]
    rail_h = [g[1] for g in rail_geo]
    rail_up = [(CELL_PX_H - g[4]) / float(ROW_STEP_PX) for g in rail_geo if g[6] > 0]
    print('  ① 栏杆 PNG 尺寸 = %s；内容向上溢出本格顶边 %s 行（均值 %.2f，最大 %.2f）'
          % (sorted(set('%dx%d' % (g[0], g[1]) for g in rail_geo)),
             sorted(set('%.1f' % u for u in rail_up)),
             sum(rail_up) / len(rail_up) if rail_up else 0.0,
             max(rail_up) if rail_up else 0.0))
    print('     ⇒ 溢出 ≥ 2 行 = 素材**本身就长这样**（图形向上盖住北邻格），'
          '不是 MapView 把图推上去的（对齐只改 ±半格 = ±1 行）。')
    floor_geo = [geo[('Tiles', i)] for i in range(40) if ('Tiles', i) in geo]
    nonflat = [g for g in floor_geo if g[7] > 1]
    print('  ② 桥面地砖 %d 张：尺寸 = %s；唯一色 >1 的 = %d 张（其余为平色/全透明）'
          % (len(floor_geo), sorted(set('%dx%d' % (g[0], g[1]) for g in floor_geo)),
             len(nonflat)))

    if deck_walk and rail:
        c = deck_walk[0]
        print('  ③ 排序（改前）：deck 格 %s 上的实体 = (%d+%d)*%d + %d + %d = %d'
              % (c, c[0], c[1], step, base, off_entity,
                 (c[0] + c[1]) * step + base + off_entity))
        print('     正南一格 %s 的物件层 = (%d+%d)*%d + %d + %d = %d'
              % ((c[0], c[1] + 1), c[0], c[1] + 1, step, base, off_object,
                 (c[0] + c[1] + 1) * step + base + off_object))
        print('     ⇒ 南侧栏杆排序值更大 ⇒ **必然**盖住桥面实体；栏杆内容又向上溢出 ≥2 行'
              '（见 ①）⇒ 画面上"从桥下走"。')

    # 逐 deck 格：南邻是否真有栏杆物件 + 栏杆是否盖住本格
    both = 0
    for c in deck_walk:
        south = (c[0], c[1] + 1)
        if cells.get(south) is not None and pack_of(cells[south][2] or '') in DECK_PACKS:
            both += 1
    print('  ④ deck 可走格中，**正南一格就是栏杆(S wall 层)** 的 = %d / %d 格 ⇒ %s'
          % (both, len(deck_walk),
             '每一格都会被盖' if both == len(deck_walk) else '部分格会被盖'))
    return 0


if __name__ == '__main__':
    sys.exit(main())
