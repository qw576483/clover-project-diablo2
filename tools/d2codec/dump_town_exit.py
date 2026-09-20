# -*- coding: utf-8 -*-
"""诊断：**罗格营地出城口**那一带的原版数据到底是什么（片 4 的取证工具，常驻）。

为什么要它（三个曾经被写错的事实，都在这里一次性打出来）：
  ① 营地出城口 = 原版参考块 `TownW1.ds1` 里**围栏环西侧那 3 格缺口**（本窗口坐标 (17,26..28)，
     合并帧 (0,21..23)）；那 3 格的 **wall 层是空的**（原版那里什么都不画）——
     ⛔ 不是 `BARRACKS/warp.dt1`。
  ② `warp.dt1` 的 3 个"标记格"在**营地内部**（本地 (12,18)/(14,18)/(16,25)），与出城口无关；
     生成器按"原版 lvltype 不装 warp.dt1 ⇒ 原版不画"的口径把它们**丢掉**，是对的。
  ③ 城镇↔野外的接缝带 = `LvlPrest.txt`「Act 1 - Town 1 Transition E/S」（`TownETrans.ds1` /
     `TownSTrans.ds1`）：**原版把它们铺在野外关卡上**（`参考工程_Diablerie/libd2/.../drlg/
     outdoors/OutRoom.zig:265/268/271`）——本项目野外生成器已按此铺；营地**自己**的出口
     外面那片地用关卡自己的瓦片（见 `export_town_layout.py` 的 `WIN_X0/WIN_Y0`）。

用法：
    python dump_town_exit.py            # 只看出城口 + warp 标记格
    python dump_town_exit.py --band     # 追加打印两条过渡带（8×40 / 56×8）的逐格构成
"""

import os
import sys

try:
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')
except Exception:
    pass

try:
    from . import ds1 as ds1mod
    from . import dt1 as dt1mod
    from . import export_tiles as exp
    from . import export_town_layout as tw
except ImportError:
    import ds1 as ds1mod
    import dt1 as dt1mod
    import export_tiles as exp
    import export_town_layout as tw

RAW = os.path.join(os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__)))),
                   '原版资源', 'd2raw')
TOWN = os.path.join(RAW, 'data', 'global', 'tiles', 'ACT1', 'TOWN')
PACK_OF = dict((rel.lower(), pack) for rel, pack, _note in exp.PACKS)
_INFO = {}

_NAME = {'floor.dt1': 't', 'warp.dt1': 'w', 'bridge.dt1': 'b', 'stonewall.dt1': 's',
         'river.dt1': 'r', 'objects.dt1': 'o', 'fence.dt1': 'f',
         'treegroups.dt1': 'g', 'trees.dt1': 'g'}


def _info(dt1_rel):
    key = dt1_rel.lower()
    if key not in _INFO:
        _INFO[key] = tw._dt1_tile_info(dt1_rel, PACK_OF)
    return _INFO[key]


def _providers(d, cell):
    out = []
    for r in d.dt1_files:
        if cell.tile_index in _info(r):
            out.append(os.path.basename(r.replace('\\', '/')).lower())
    return sorted(set(out))


def _desc(d, x, y, layer):
    cell = d.floor_at(x, y) if layer < 0 else d.wall_at(layer, x, y)
    if cell is None or cell.is_empty:
        return '-'
    best = None
    for r in d.dt1_files:
        v = _info(r).get(cell.tile_index)
        if v is not None and (best is None or best[2] < v[2]):
            best = v
    return '%s/%03d [%s]' % (best[0], best[1], ','.join(_providers(d, cell))) if best \
        else '? [%s]' % ','.join(_providers(d, cell))


def _cellchar(d, x, y):
    cf, cw = d.floor_at(x, y), d.wall_at(0, x, y)
    ch = '.'
    if cf is not None and not cf.is_empty:
        for r in d.dt1_files:
            if cf.tile_index in _info(r):
                ch = _NAME.get(os.path.basename(r.replace('\\', '/')).lower(), '?')
                break
    if cw is not None and not cw.is_empty:
        ch = ch.upper()
    return ch


def main(argv):
    d = ds1mod.load_ds1(os.path.join(TOWN, 'townw1.ds1'))
    print('① 参考块 townw1.ds1   size=%dx%d  dt1=%s'
          % (d.width, d.height, [os.path.basename(r) for r in d.dt1_files]))
    print('  出城口附近（**块本地坐标**；块偏移为 0 ⇒ 合并帧同值；'
          '关卡窗口坐标 = 合并帧 + (%d,%d)）' % (tw.WIN_X0, tw.WIN_Y0))
    for y in range(19, 26):
        cells = ['(%d,%d) F=%s W=%s' % (x, y, _desc(d, x, y, -1), _desc(d, x, y, 0))
                 for x in range(0, 4)]
        print('   y=%2d  %s' % (y, '   '.join(cells)))

    print('  围栏环西侧缺口（= 出城口 3 格）：')
    gap = []
    for y in range(d.height):
        c = d.wall_at(0, 0, y)
        if c.is_empty or 'fence.dt1' not in _providers(d, c):
            gap.append(y)
    print('    列 x=0 上没有围栏的行 = %s' % gap)
    for y in (21, 22, 23):
        print('    (0,%2d)  floor=%s  wall0=%s' % (y, _desc(d, 0, y, -1), _desc(d, 0, y, 0)))

    print('② wall 层里指向 `warp.dt1` 的格（原版编辑器留下的标记格 = **营地内部**，不是出口）：')
    marks = []
    for y in range(d.height):
        for x in range(d.width):
            for layer in range(len(d.walls)):
                c = d.wall_at(layer, x, y)
                if c is None or c.is_empty:
                    continue
                if 'warp.dt1' in _providers(d, c):
                    marks.append((x, y, layer, c.orientation, c.main_index, c.sub_index))
    print('    命中 %d 格：%s（生成器按"原版不画"丢掉，见 export_town_layout.py build()）'
          % (len(marks), marks))

    if '--band' in argv:
        for fname, title in (('townetrans.ds1', '③ 过渡带 E（LvlPrest Def=2，8×40）'),
                             ('townstrans.ds1', '④ 过渡带 S（LvlPrest Def=3，56×8）')):
            t = ds1mod.load_ds1(os.path.join(TOWN, fname))
            print('%s  size=%dx%d  dt1=%s' % (title, t.width, t.height,
                                              [os.path.basename(r) for r in t.dt1_files]))
            print('   图例：小写=只有 floor / 大写=该格 wall 层也有内容；'
                  't=floor.dt1 w=warp b=bridge s=stonewall r=river o=objects f=fence g=treegroups')
            for y in range(t.height):
                print('   y=%2d %s' % (y, ''.join(_cellchar(t, x, y) for x in range(t.width))))
    return 0


if __name__ == '__main__':
    sys.exit(main(sys.argv))
