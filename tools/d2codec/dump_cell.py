# -*- coding: utf-8 -*-
"""诊断：打印 `MapGenTownLayout` 里若干格的 kind / ground 瓦片 / object 瓦片，
并回溯到**原版 DS1 的那一格**（哪一块、块本地坐标、来自哪个 dt1）。

典型用途：确认"生成物这一格的键"确实是原版那一格的键（片 4 用来核对出城口）。

⚠️ 坐标口径（片 4 起）：命令行给的是**关卡窗口坐标**（= `MapGenTownLayout` 的 `Rows[y][x]`），
   窗口原点在 `export_town_layout.WIN_X0/WIN_Y0`（不再是参考块的本地 0）。
   格的内容按生成器同一套规则解析：**参考块第一、其余按 `LvlPrest` 顺序逐格补空**
   （所以西侧/北侧那两条带会显示为"由 TownE1/TownS1 提供"）。

用法：
    python dump_cell.py 17,26 17,27 17,28 32,28
    python dump_cell.py --all-void          # 只列出"四块都没瓦片"的格（kind=v）
"""

import os
import re
import sys

try:
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')
except Exception:
    pass

try:
    from . import ds1 as ds1mod
    from . import dt1 as dt1mod
    from . import export_tiles as exp
    from . import export_town_layout as ex
except ImportError:
    import ds1 as ds1mod
    import dt1 as dt1mod
    import export_tiles as exp
    import export_town_layout as ex

MAPGEN = os.path.join(
    os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__)))),
    'client', 'Assets', 'Scripts', 'Module', 'Map', 'MapGenTownLayout.cs')


def _block(src, name):
    m = re.search(r'string\[\]\s+' + name + r'\s*=\s*\{(.*?)\};', src, re.S)
    return re.findall(r'"([^"]*)"', m.group(1)) if m else []


def main(argv):
    with open(MAPGEN, encoding='utf-8') as fh:
        src = fh.read()
    packs = _block(src, 'Packs')
    kinds = _block(src, 'Rows')
    ground = _block(src, 'GroundRows')
    objects = _block(src, 'ObjectRows')

    def decode(row, x):
        off = x * 6
        if row is None or off + 6 > len(row) or row[off] == '-':
            return ''
        pid = int(row[off:off + 3])
        return '%s/%s' % (packs[pid], row[off + 3:off + 6])

    if '--all-void' in argv:
        for y, row in enumerate(kinds):
            for x in range(len(row)):
                if row[x] == 'v':
                    print('  空瓦片格 (%d,%d)  kind=v  ground=%s object=%s'
                          % (x, y, decode(ground[y], x) or '(空)', decode(objects[y], x) or '(空)'))
        return 0

    cells = []
    for a in argv[1:]:
        if ',' in a:
            x, y = a.split(',')
            cells.append((int(x), int(y)))
    if not cells:
        print(__doc__)
        return 2

    # ── 用生成器自己的机制解析"这一格来自哪一块"（同一套对齐与补空顺序）────────────
    pack_of = dict((rel.lower(), pack) for rel, pack, _n in exp.PACKS)
    files = [ex.FileInfo(r, pack_of) for r in ex.preset_files()]
    ref_rel, offs = ex.align_offsets(files)
    ref = [f for f in files if f.rel == ref_rel][0]
    order = [ref] + [f for f in files if f is not ref]

    for (x, y) in cells:
        print('格(%d,%d)  kind=%s  ground=%s  object=%s'
              % (x, y, kinds[y][x] if y < len(kinds) and x < len(kinds[y]) else '?',
                 decode(ground[y], x) or '(空)', decode(objects[y], x) or '(空)'))
        print('   关卡窗口坐标 (%d,%d) ⇒ 合并帧坐标 (%d,%d)'
              % (x, y, x + ex.WIN_X0, y + ex.WIN_Y0))
        hit = False
        for f in order:
            ox, oy = offs[f.rel]
            sx, sy = x + ex.WIN_X0 + ox, y + ex.WIN_Y0 + oy
            if not (0 <= sx < f.ds1.width and 0 <= sy < f.ds1.height):
                continue
            fcell = f.ds1.floor_at(sx, sy)
            wcell = f.ds1.wall_at(0, sx, sy)
            has_f = fcell is not None and not fcell.is_empty
            has_w = wcell is not None and not wcell.is_empty
            if not (has_f or has_w):
                continue
            print('   来自 %s（块本地 %d,%d）：floor=%s  wall0=%s  提供者 dt1=%s'
                  % (os.path.basename(f.rel), sx, sy,
                     ('m=%d s=%d idx=%d' % (fcell.main_index, fcell.sub_index, fcell.tile_index))
                     if has_f else '(空)',
                     ('m=%d s=%d o=%d idx=%d' % (wcell.main_index, wcell.sub_index,
                                                 wcell.orientation, wcell.tile_index))
                     if has_w else '(空)',
                     ','.join(sorted(f.names_of(fcell) | f.names_of(wcell))) or '-'))
            hit = True
            break
        if not hit:
            print('   **四块都没有这一格**（原版这里没有瓦片）⇒ kind 应为 v（Void：不画 + 不可走）')
    return 0


if __name__ == '__main__':
    sys.exit(main(sys.argv))
