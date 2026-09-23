# -*- coding: utf-8 -*-
"""片 M3 判据资产：罗格营地「出城那座桥」的**逐格可走性 vs 桥面格数据**对照 + 连通性断言。

为什么单独一份（而不是改 measure_bridge_deck.py）：那份量的是**素材像素几何**（栏杆向上
溢出几行），本份量的是**通行数据**（哪几格可走、桥面两列/两行是否连通）——
用户症状「桥的上面过不去」本质是"数据层可走格集合 ≠ 桥面贴图铺到的格集合"。

用法（任意目录）：
    python tools/probes/measure/m3_bridge_probe.py

解析对象（全部是生成物，⛔ 不复制逻辑、不猜）：
  · `Module/Map/MapGenTownLayout.cs` 的 Rows / GroundRows / ObjectRows / Packs
  · 可走性判据**照抄** `Def/Enums.cs` 的 `TileKindInfo.IsWalkable`（同源，不重写判等表）
判据（全部是数字）：
  ① 桥面格集合 = floor 键包 == moor_bridge 的格；其中 kind 可走的 = 「可走桥面」；
  ② 8 邻接 BFS（对角要求两侧可走，与 `CloverEngine.AStar` 同规则）取连通分量；
  ③ 断言「所有可走桥面格属于**同一个**连通分量」——不过 ⇒ 打印被隔离的那些格。
"""

import os
import re
import sys

try:
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')
except Exception:
    pass

_here = os.path.dirname(os.path.abspath(__file__))
REPO = _here
while REPO and not os.path.isdir(os.path.join(REPO, 'client', 'Assets')):
    parent = os.path.dirname(REPO)
    if parent == REPO:
        raise SystemExit('找不到含 client/Assets 的仓库根')
    REPO = parent

CS = os.path.join(REPO, 'client', 'Assets', 'Scripts', 'Module', 'Map', 'MapGenTownLayout.cs')
EN = os.path.join(REPO, 'client', 'Assets', 'Scripts', 'Def', 'Enums.cs')

DECK_PACKS = ('moor_bridge',)


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


# ── 可走性判据：从 Def/Enums.cs 的 TileKindInfo.IsWalkable 解析（同源，不重写）────
def parse_walkable(enums_src):
    """返回 dict[TileKind 名字] = bool。

    做法 = 逐行扫 `IsWalkable` 的方法体，**累积 case 标签**直到遇到 `return true/false;`
    （C# 的 switch 里多个 case 共用一个 return）—— 这正是本项目那份实现的写法。
    ⛔ 不在这里重写"哪些 kind 可走"的判等表（那是 `TileKindInfo` 的唯一职责）。
    """
    m = re.search(r'public static bool IsWalkable\(TileKind kind\)\s*\{', enums_src)
    if not m:
        raise SystemExit('解析不到 TileKindInfo.IsWalkable 定义')
    lines = enums_src[m.end():].splitlines()
    out = {}
    pending = []
    for ln in lines:
        s = ln.strip()
        cm = re.match(r'case\s+TileKind\.(\w+)\s*:', s)
        if cm:
            pending.append(cm.group(1))
            continue
        rm = re.match(r'return\s+(true|false)\s*;', s)
        if rm:
            for name in pending:
                out[name] = (rm.group(1) == 'true')
            pending = []
            continue
        if s.startswith('public ') or s.startswith('/// <summary>是否为地面层'):
            break
    if not out:
        raise SystemExit('IsWalkable 主体里没解析到任何 case')
    return out


# 布局字符 → TileKind 名字（照抄 `MapGenTown.KindOf`，仅作映射，不改判据）
CH2KIND = {'.': 'Grass', 'd': 'Dirt', 'f': 'Fence', 'o': 'Wall', 'w': 'Wall',
           't': 'Tree', 's': 'Rock', 'r': 'Water', 'x': 'Exit', 'v': 'Void'}


def main():
    with open(CS, 'r', encoding='utf-8') as fh:
        src = fh.read()
    with open(EN, 'r', encoding='utf-8') as fh:
        enums = fh.read()

    W = read_int(src, 'Width')
    H = read_int(src, 'Height')
    packs = read_array(src, 'Packs')
    rows = read_array(src, 'Rows')
    ground = read_array(src, 'GroundRows')
    objects = read_array(src, 'ObjectRows')
    walk_tbl = parse_walkable(enums)

    print('=' * 92)
    print('① 表结构自证')
    print('=' * 92)
    print('  Width/Height = %dx%d  Packs=%s' % (W, H, packs))
    print('  Rows=%d  GroundRows=%d  ObjectRows=%d' % (len(rows), len(ground), len(objects)))
    for nm, arr, per in (('Rows', rows, 1), ('GroundRows', ground, 6), ('ObjectRows', objects, 6)):
        bad = [i for i, r in enumerate(arr) if len(r) != W * per]
        print('  %-11s 长度异常行 = %s' % (nm, bad if bad else '无'))
    print('  IsWalkable 解析到的 TileKind = %s' % sorted(walk_tbl.keys()))
    print('  未知字符（不在 CH2KIND 里） = %s'
          % sorted(set(''.join(rows)) - set(CH2KIND.keys())))

    cells = {}
    for y in range(H):
        for x in range(W):
            cells[(x, y)] = (rows[y][x], decode(ground[y], x, packs), decode(objects[y], x, packs))

    def is_walk(c):
        ch, g, o = cells[c]
        return bool(walk_tbl.get(CH2KIND.get(ch, '?'), False))

    deck = sorted(c for c, (k, g, o) in cells.items() if (g or '').split('/')[0] in DECK_PACKS)
    deck_walk = sorted(c for c in deck if is_walk(c))
    deck_block = sorted(set(deck) - set(deck_walk))
    rail = sorted(c for c, (k, g, o) in cells.items() if (o or '').split('/')[0] in DECK_PACKS)

    print()
    print('=' * 92)
    print('② 桥面格数据 vs 可走性（判据 = floor 键包 ∈ %s）' % (DECK_PACKS,))
    print('=' * 92)
    print('  桥面地砖格 = %d 格；其中**可走** = %d 格，**不可走** = %d 格'
          % (len(deck), len(deck_walk), len(deck_block)))
    for tag, cs in (('可走桥面', deck_walk), ('不可走（栏杆行地面）', deck_block), ('栏杆（wall 层）', rail)):
        if not cs:
            continue
        xs = sorted(set(c[0] for c in cs))
        ys = sorted(set(c[1] for c in cs))
        print('  %-22s %2d 格  x=%s  y=%s' % (tag, len(cs), xs, ys))

    if deck:
        x0 = max(0, min(c[0] for c in deck) - 1)
        x1 = min(W - 1, max(c[0] for c in deck) + 1)
        y0 = max(0, min(c[1] for c in deck) - 2)
        y1 = min(H - 1, max(c[1] for c in deck) + 2)
        xs = list(range(x0, x1 + 1))

        def row_of(fn):
            return ' '.join('%6s' % fn(x) for x in xs)

        print()
        print('  逐格对照（x=%d..%d，y=%d..%d；图 %dx%d）：每行一格，4 条 = kind / 可走 / floor 键 / wall 键'
              % (x0, x1, y0, y1, W, H))
        print('    x 标尺       = ' + ' '.join('%6d' % x for x in xs))
        for y in range(y0, y1 + 1):
            def g(x, _y=y, _i=0):
                c = (x, _y)
                if c not in cells:
                    return '--'
                ch, gf, ob = cells[c]
                return [ch, 'W' if is_walk(c) else '.',
                        (gf or '-').split('/')[-1] if gf else '-',
                        (ob or '-').split('/')[-1] if ob else '-'][_i]
            print('    y=%-3d kind  = %s' % (y, row_of(lambda x, _y=y: g(x, _y, 0))))
            print('          walk  = %s' % row_of(lambda x, _y=y: g(x, _y, 1)))
            print('          floor = %s' % row_of(lambda x, _y=y: g(x, _y, 2)))
            print('          wall  = %s' % row_of(lambda x, _y=y: g(x, _y, 3)))

    # ── ③ BFS 连通性（对角要求两侧可走，与 CloverEngine.AStar 同规则）──────────
    NEI = [(1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (1, -1), (-1, 1), (-1, -1)]

    def flood(start):
        seen = {start}
        q = [start]
        while q:
            cx, cy = q.pop()
            for dx, dy in NEI:
                n = (cx + dx, cy + dy)
                if n in seen or not (0 <= n[0] < W and 0 <= n[1] < H):
                    continue
                if not is_walk(n):
                    continue
                if dx and dy:
                    if not is_walk((cx + dx, cy)) or not is_walk((cx, cy + dy)):
                        continue
                seen.add(n)
                q.append(n)
        return seen

    print()
    print('=' * 92)
    print('③ 连通性断言：所有**可走桥面格**必须属于同一个连通分量')
    print('=' * 92)
    rc = 0
    if not deck_walk:
        print('  ⛔ 没有任何可走桥面格（数据缺口）')
        rc = 1
    else:
        comp = flood(deck_walk[0])
        iso = [c for c in deck_walk if c not in comp]
        print('  从 %s 起 BFS 可达 %d 格（其中可走桥面格命中 %d / %d）'
              % (deck_walk[0], len(comp), len(deck_walk) - len(iso), len(deck_walk)))
        if iso:
            print('  ⛔ **被隔离的可走桥面格 %d 个** = %s' % (len(iso), iso))
            rc = 1
        else:
            print('  ✅ 全部可走桥面格连通')

        # 桥轴两端：取 x 最小 / 最大 的可走桥面格，互求路径长度
        xs = sorted(c[0] for c in deck_walk)
        a = min(deck_walk, key=lambda c: (c[0], c[1]))
        b = max(deck_walk, key=lambda c: (c[0], c[1]))
        # BFS 求最短路径（逐格）
        prev = {a: None}
        q = [a]
        while q:
            cur = q.pop(0)
            if cur == b:
                break
            for dx, dy in NEI:
                n = (cur[0] + dx, cur[1] + dy)
                if n in prev or not (0 <= n[0] < W and 0 <= n[1] < H) or not is_walk(n):
                    continue
                if dx and dy and (not is_walk((cur[0] + dx, cur[1])) or not is_walk((cur[0], cur[1] + dy))):
                    continue
                prev[n] = cur
                q.append(n)
        if b in prev:
            path = []
            cur = b
            while cur is not None:
                path.append(cur)
                cur = prev[cur]
            path.reverse()
            print('  沿桥轴：%s → %s 最短路径 %d 步（绕行 %d 步）'
                  % (a, b, len(path) - 1, len(path) - 1 - abs(b[0] - a[0])))
            print('  路径 = %s' % path)
        else:
            print('  ⛔ %s → %s **不可达**（桥轴断开）' % (a, b))
            rc = 1

    # ── ④ 全图瓦片存在性（用户报「地图边界缺瓦片 / 贴图问题」的离线判据）────────────
    print()
    print('=' * 92)
    print('④ bbox 内逐格瓦片存在性：布局里的每个键都必须有对应 PNG')
    print('=' * 92)
    D2 = os.path.join(REPO, 'client', 'Assets', 'Resources', 'Clover', 'D2')
    missing = {}
    empty_ground = []
    for y in range(H):
        for x in range(W):
            ch, g, o = cells[(x, y)]
            for layer, key in (('Tiles', g), ('Objects', o)):
                if not key:
                    continue
                sub, idx = key.split('/')
                p = os.path.join(D2, layer, sub, idx + '.png')
                if not os.path.exists(p):
                    missing.setdefault((layer, sub), []).append(((x, y), key))
            if not g and ch != 'v':
                empty_ground.append(((x, y), ch))
    if missing:
        rc = 1
        for (layer, sub), lst in sorted(missing.items()):
            print('  ⛔ 缺图：%s/%s —— %d 格引用不存在的 PNG，例：%s'
                  % (layer, sub, len(lst), lst[:6]))
    else:
        print('  ✅ 布局引用的**全部**瓦片键都能在 Resources 里找到 PNG（无缺图）')
    print('  ground 键为空（原版该格不铺地面）的格 = %d 个（kind≠Void，逐格登记）'
          % len(empty_ground))
    if empty_ground:
        print('     前 20 个 = %s' % empty_ground[:20])
        print('     （口径：`GroundRows` 的 `------` = 该块那格没瓦片；四块合并后仍为空 = 原版真不画，'
              '**不是**本项目缺图）')

    # ── ⑤ 边界：东南西北四条边界列/行的可走格（看「墙缺一段 / 黑三角」是否由边界格引起）──
    print()
    print('=' * 92)
    print('⑤ 四条边界的可走格分布（边界黑区 = 原版无邻图；这里只看"边界上哪些格能站人"）')
    print('=' * 92)
    for tag, cs in (('x=0（西）', [(0, y) for y in range(H)]),
                    ('x=%d（东）' % (W - 1), [(W - 1, y) for y in range(H)]),
                    ('y=0（北）', [(x, 0) for x in range(W)]),
                    ('y=%d（南）' % (H - 1), [(x, H - 1) for x in range(W)])):
        w = [c for c in cs if is_walk(c)]
        print('  %-12s 可走 %2d / %-2d 格%s' % (tag, len(w), len(cs),
              ('；可走格 = %s' % w) if len(w) <= 12 else ''))

    return rc


if __name__ == '__main__':
    sys.exit(main())
