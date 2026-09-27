# -*- coding: utf-8 -*-
"""**DT1 subtile 可走性口径的自证脚本**（改 `cell_passable` 前必须先跑它）。

为什么需要它：地形拼块能不能接上，全押在「一格到底可不可走」这一个口径上。
口径错了不会报错，只会**静默**把整张图变成不能走（或走不该走的地方）——
所以这里用一条可机械复算的判据把它钉住，不过就说明口径被改坏了。

判据（**水挡路 / 地面可走**）：原版 `river.dt1` 的水面**不可走**、
  `TOWN/floor.dt1` 的地面**可走**。

实测结论（本项目采用的口径）：
  `passable = 中心 subtile 的 flag 里 Walk(1)/PlayerWalk(8) **都没置位**`
  （标志置位 = 不可走）。出处
  `原版资源/参考工程_Diablerie/.../Engine/World/WorldGrid.cs:78-84`
  `ApplyTileCollisions`：`passable = (tile.flags[flagIndex] & (Walk|PlayerWalk)) == 0`。
  ⛔ **不要**改成"任一 subtile 可走"（`any(v & 1)`）：那会多判出可走格。

用法：
    python tools/d2codec/verify_walk_flags.py            # 跑判据，全过返回 0
"""

import os
import sys

try:
    from . import ds1 as ds1mod
    from . import dt1 as dt1mod
    from . import export_wild_layout as wild
except ImportError:
    import ds1 as ds1mod
    import dt1 as dt1mod
    import export_wild_layout as wild

_REPO = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
RAW = os.path.join(_REPO, '原版资源', 'd2raw')
TILES = os.path.join(RAW, 'data', 'global', 'tiles', 'ACT1')

# 候选口径：名字 → 判定函数（都接收一个 tile 的 25 个 subtile flag）
MODES = {
    'center(采用)': lambda fl: not (fl[12] & 9),
    'any_walk(旧口径)': lambda fl: any(v & 1 for v in fl),
    'all_clear': lambda fl: not any(v & 9 for v in fl),
}


def check_water_and_grass():
    """判据：水（`river.dt1`）**大多数地砖不可走**；地面（`TOWN/floor.dt1`）**大多数可走**。

    ⚠️ 判据写成"多数"而不是"全部"，因为实测这两张表里都夹着**边界/过渡**瓦片：
    `river.dt1` 44 张地砖里有 7 张是**岸边**（可走），`floor.dt1` 144 张里有 22 张是
    墙脚/物件脚下的地砖（不可走）。写成"全部"会得到假失败。
    """
    out = []
    for rel, want_walk_ratio in (('OUTDOORS/river.dt1', 0.5), ('TOWN/floor.dt1', 0.5)):
        fp = os.path.join(TILES, rel.replace('/', os.sep))
        d = dt1mod.load_dt1(fp)
        n = ok = 0
        for t in d.tiles:
            if t.orientation != 0 or t.width <= 0 or t.pixel_height <= 0:
                continue
            n += 1
            if wild.cell_passable(t):
                ok += 1
        blocked_ratio = 1.0 - (float(ok) / n if n else 0.0)
        if rel.endswith('river.dt1'):
            good = blocked_ratio >= want_walk_ratio        # 水：多数阻挡
        else:
            good = (float(ok) / n if n else 0.0) >= want_walk_ratio   # 地面：多数可走
        out.append((rel, n, ok, good))
    return out


def main():
    # 本脚本会打印 `⇒` 等非 GBK 字符；中文 Windows 控制台默认 GBK ⇒ 直接换成 UTF-8 输出，
    # 否则会在**最后一行**抛 UnicodeEncodeError（判据已经跑完、结论却打不出来）。
    try:
        sys.stdout.reconfigure(encoding='utf-8', errors='replace')
    except Exception:                                  # noqa: BLE001 —— 老版本 Python 没有 reconfigure
        pass

    print('── 判据：水（river）多数地砖必须**阻挡**、地面（floor）多数必须**可走** ──')
    bad = 0
    for rel, n, ok, good in check_water_and_grass():
        if not good:
            bad += 1
        print('  %-22s 地砖 %3d 张，中心 subtile 可走 %3d 张（%.0f%%）⇒ %s'
              % (rel, n, ok, 100.0 * ok / n if n else 0.0, 'OK' if good else '❌'))

    print()
    if bad:
        print('❌ 采用的口径（center）没过判据：%d 张表不合格' % bad)
        return 1
    print('✅ 采用的口径（center = 中心 subtile 不带 Walk/PlayerWalk 标志）判据通过。')
    return 0


if __name__ == '__main__':
    sys.exit(main())
