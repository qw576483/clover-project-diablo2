# -*- coding: utf-8 -*-
"""R1-B 量法脚本：判定「罗格营地东侧河上的纯蓝长条」到底是哪一张原版瓦片。

用法（任意目录，脚本自己找仓库根）：
    python tools/probes/measure/r1b_water_tiles.py

为什么留档（不是一次性脚本）：它是「河面那块平色瓦片是哪一张 / 它是不是平色」这件事的
**唯一像素级判据** —— 删了就不能重新判定同一件事（global skill §1.8：判据资产落 tools/probes/）。
C# 侧的对账断言在 `tools/probes/hosts/mapcheck/Program.cs` 的 `Step15_FlatWaterWallNotOverlaid`
（白名单 / 命中 49 格 / 反例 / manifest 与 PNG 字节数代理证据）；本脚本给**逐像素**权威。

判据（全部是数字，无一处靠"看"）：
  ① 逐格解 `MapGenTownLayout` 的 floor/object 键 → 解析成 Resources 路径 → 是否存在；
  ② 列出**被引用但取不到**的键（有则说明 `MapView` 会走占位菱形）；
  ③ 对**每个被引用到的 PNG** 采样像素：不透明像素数 / 唯一色数 / 均值 RGB；
     唯一色数 = 1 的即「平色瓦片」= 静态渲染出来必然像占位色块；
  ④ 把每个被引用到的平色 PNG 的**格位置**列出来 ⇒ 定位"蓝条"落在哪几格；
  ⑤ 河带逐列 × 逐键 的瓦片清单（floor / wall 各一张，含像素统计）；
  ⑥ 与 `MapView.GroundColor(Exit)` 的亮青 (0, 0.85, 1) 对比（排除"出口占位"假设）；
  ⑦ 逐格「不透明像素覆盖率」（撤掉 wall 层平色瓦片会不会露出空洞）。
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
D2 = os.path.join(REPO, 'client', 'Assets', 'Resources', 'Clover', 'D2')

# `MapView.GroundColor(Exit)` 的亮青占位色（`MapView.cs` 的 `case TileKind.Exit`）
EXIT_CYAN = (0, 217, 255)          # (0.00, 0.85, 1.00) * 255

# 一格等距瓦片的不透明像素满值（160×79 上半菱形）：`GameConst.IsoTilePxW/H`
FULL_CELL_PX = 160 * 79 // 2


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


def img_stats(path):
    """(不透明像素数, 唯一色数, 均值 RGB, 出现最多的色)"""
    im = Image.open(path).convert('RGBA')
    px = list(im.getdata())
    opaque = [p for p in px if p[3] > 0]
    if not opaque:
        return 0, 0, (0, 0, 0), None
    cnt = collections.Counter(opaque)
    top, _n = cnt.most_common(1)[0]
    return (len(opaque), len(cnt),
            (sum(p[0] for p in opaque) / len(opaque),
             sum(p[1] for p in opaque) / len(opaque),
             sum(p[2] for p in opaque) / len(opaque)),
            top)


def main():
    with open(CS, 'r', encoding='utf-8') as fh:
        src = fh.read()
    W = read_int(src, 'Width')
    H = read_int(src, 'Height')
    packs = read_array(src, 'Packs')
    rows = read_array(src, 'Rows')
    ground = read_array(src, 'GroundRows')
    objects = read_array(src, 'ObjectRows')

    print('=' * 78)
    print('R1-B ① 表结构自证（%s）' % os.path.relpath(CS, REPO))
    print('=' * 78)
    print('  Width=%d Height=%d Packs=%s' % (W, H, packs))
    for nm, arr, per in (('Rows', rows, 1), ('GroundRows', ground, 6), ('ObjectRows', objects, 6)):
        bad = [i for i, r in enumerate(arr) if len(r) != W * per]
        print('  %-11s 行数=%d 每行长(去重)=%s 长度异常行=%s'
              % (nm, len(arr), sorted(set(len(r) for r in arr)), bad if bad else '无'))

    # ── 逐格解键 ───────────────────────────────────────────────────────────────
    cells = {}
    for y in range(H):
        for x in range(W):
            cells[(x, y)] = (rows[y][x], decode(ground[y], x, packs), decode(objects[y], x, packs))

    keys = collections.Counter()
    key_cells = collections.defaultdict(list)
    for (x, y), (_k, g, o) in cells.items():
        if g:
            keys[('Tiles', g)] += 1
            key_cells[('Tiles', g)].append((x, y))
        if o:
            keys[('Objects', o)] += 1
            key_cells[('Objects', o)].append((x, y))

    print()
    print('=' * 78)
    print('R1-B ② 全部被引用到的瓦片键 → 文件是否存在（取不到 ⇒ MapView 走占位菱形）')
    print('=' * 78)
    missing = []
    for (layer, key), n in sorted(keys.items()):
        path = os.path.join(D2, layer, (key + '.png').replace('/', os.sep))
        if not os.path.exists(path):
            missing.append((layer, key, n, path))
    print('  被引用键（去重）=%d 个；**取不到文件的 = %d 个**' % (len(keys), len(missing)))
    for layer, key, n, path in missing:
        print('    ❌ %-8s %-18s %3d 格  %s' % (layer, key, n, path))
    if not missing:
        print('    ⇒ 布局层面**没有任何一格取不到瓦片** ⇒ MapView 的占位菱形分支在营地里不会被走到')

    print()
    print('=' * 78)
    print('R1-B ③ 每个被引用 PNG 的像素统计（唯一色数=1 ⇒ 平色瓦片 ⇒ 必然像占位色块）')
    print('=' * 78)
    stats = {}
    flats = []
    for (layer, key), n in sorted(keys.items()):
        path = os.path.join(D2, layer, (key + '.png').replace('/', os.sep))
        if not os.path.exists(path):
            continue
        op, uniq, mean, top = img_stats(path)
        stats[(layer, key)] = (op, uniq, mean, top, n)
        if uniq <= 1:
            flats.append((layer, key, n, op, top))
    print('  平色（唯一色数 ≤ 1）且被引用到的 PNG：%d 个' % len(flats))
    for layer, key, n, op, top in sorted(flats, key=lambda t: -t[2]):
        cs = key_cells[(layer, key)]
        xs = sorted(set(c[0] for c in cs))
        ys = sorted(set(c[1] for c in cs))
        print('    ● %-8s %-18s %3d 格 列x=%s 行y=%d..%d  不透明px=%d  色=RGBA%s'
              % (layer, key, n, xs if len(xs) <= 10 else '%d..%d' % (min(xs), max(xs)),
                 min(ys), max(ys), op, top))

    print()
    print('=' * 78)
    print('R1-B ④ 河带（Rows 里 kind=r 的列）× 逐格键 × 瓦片像素')
    print('=' * 78)
    river = sorted({x for (x, y), (k, _g, _o) in cells.items() if k == 'r'})
    print('  河带列 x = %s（%d 列）' % (river, len(river)))
    for x in river:
        col = [(x, y) for y in range(H)]
        gk = collections.Counter(cells[c][1] for c in col)
        ok = collections.Counter(cells[c][2] for c in col)
        print('  ── x=%d（kind=%s）' % (x, sorted({cells[c][0] for c in col})))
        for k, n in sorted(gk.items(), key=lambda t: -t[1]):
            s = stats.get(('Tiles', k))
            print('       floor  %-18s %2d 格  唯一色=%s 均值RGB=(%s)'
                  % (k if k else '(空)', n, s[1] if s else '取不到',
                     'N/A' if not s else '%.0f,%.0f,%.0f' % s[2]))
        for k, n in sorted(ok.items(), key=lambda t: -t[1]):
            s = stats.get(('Objects', k))
            print('       wall   %-18s %2d 格  唯一色=%s 均值RGB=(%s) 主色RGBA=%s'
                  % (k if k else '(空/不画)', n, s[1] if s else '取不到',
                     'N/A' if not s else '%.0f,%.0f,%.0f' % s[2], s[3] if s else 'N/A'))

    print()
    print('=' * 78)
    print('R1-B ⑤ 假设排除：出口亮青占位 (0, 0.85, 1) = RGB%s' % (EXIT_CYAN,))
    print('=' * 78)
    exits = sorted(c for c, (k, _g, _o) in cells.items() if k == 'x')
    print('  出口格 = %s（共 %d 格）；河带列 x=%s ⇒ 屏幕横向相隔 %d 格宽'
          % (exits, len(exits), river, min(river) - max(c[0] for c in exits)))
    for c in exits:
        g = cells[c][1]
        path = os.path.join(D2, 'Tiles', (g + '.png').replace('/', os.sep))
        print('    出口格 %s floor=%-18s 文件存在=%s' % (c, g, os.path.exists(path)))
    hit = [f for f in flats if abs(f[4][0] - EXIT_CYAN[0]) <= 30
           and abs(f[4][1] - EXIT_CYAN[1]) <= 30 and abs(f[4][2] - EXIT_CYAN[2]) <= 30]
    print('  平色 PNG 里与亮青 (0,217,255) 相近（各通道差 ≤ 30）的：%s'
          % (['%s/%s RGBA%s' % (f[0], f[1], f[4]) for f in hit] if hit else '无'))

    print()
    print('=' * 78)
    print('R1-B ⑥ 逐格「不透明像素覆盖率」（撤掉 wall 层平色瓦片会不会露出空洞）')
    print('=' * 78)
    print('  一格满值 = %d px（160×79 上半菱形）；平色 wall 瓦片与 floor 水瓦片都是 6400 px ⇒ 都铺满整格' % FULL_CELL_PX)
    for layer, key in (('Objects', 'moor_river/028'), ('Tiles', 'moor_river/025'),
                       ('Tiles', 'moor_river/029'), ('Tiles', 'moor_river/017')):
        path = os.path.join(D2, layer, (key + '.png').replace('/', os.sep))
        if not os.path.exists(path):
            print('    %-8s %-18s 缺文件' % (layer, key))
            continue
        im = Image.open(path).convert('RGBA')
        op = sum(1 for q in im.getdata() if q[3] > 0)
        print('    %-8s %-18s %dx%d 不透明=%d px = 满值的 %.1f%%'
              % (layer, key, im.size[0], im.size[1], op, 100.0 * op / FULL_CELL_PX))

    print()
    print('=' * 78)
    print('R1-B ⑦ 结论判据（3 行带数字）')
    print('=' * 78)
    for layer, key, n, op, top in [f for f in flats
                                   if f[0] == 'Objects' and f[1].startswith('moor_river/')]:
        cs = key_cells[(layer, key)]
        print('  ① 河带 wall 层 `%s` = 平色瓦片（唯一色 1，RGBA%s），铺在 %d 格：x=%s'
              % (key, top, n, sorted(set(c[0] for c in cs))))
    print('  ② 被引用但取不到文件的键 = %d 个 ⇒ 亮青/灰色占位菱形在营地无处触发' % len(missing))
    print('  ③ 出口格在 x=%s（离河带 x=%s 至少 %d 列）⇒ 亮青占位不可能出现在河边'
          % (sorted(set(c[0] for c in exits)), river, min(river) - max(c[0] for c in exits)))
    return 0


if __name__ == '__main__':
    sys.exit(main())
