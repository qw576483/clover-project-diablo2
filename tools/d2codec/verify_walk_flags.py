# -*- coding: utf-8 -*-
"""**DT1 subtile 可走性口径的自证脚本**（改 `cell_passable` / 块开口签名前必须先跑它）。

为什么需要它：地形拼块能不能接上、洞里能不能走，全押在「一格到底可不可走」这一个口径上。
口径错了不会报错，只会**静默**把整张图变成不能走（或走不该走的地方）——
所以这里用**两条互相独立的判据**把它钉住，任何一条不过就说明口径被改坏了。

判据 ①（**开口方向自洽**）：`CAVES/cave{方向}.ds1` 的**文件名声明了开口方向**
  （`caveNSEW` = 四条边都通、`caveNS` = 南北通、`caveE` = 只通东…）。
  用某个口径算出可走掩码后，看它碰到哪几条边 —— **必须与文件名逐一吻合**。

判据 ②（**水挡路 / 草地可走**）：原版 `river.dt1` 的水面**不可走**、
  `TOWN/floor.dt1` 的地面**可走**。

实测结论（本项目采用的口径）：
  `passable = 中心 subtile 的 flag 里 Walk(1)/PlayerWalk(8) **都没置位**`
  （标志置位 = 不可走）。出处
  `原版资源/参考工程_Diablerie/.../Engine/World/WorldGrid.cs:78-84`
  `ApplyTileCollisions`：`passable = (tile.flags[flagIndex] & (Walk|PlayerWalk)) == 0`。
  ⛔ **不要**改成"任一 subtile 可走"（`any(v & 1)`）：判据 ① 会在
  `caveN / caveS / caveSE / caveNEW / caveSEW` 上失败（多判出边）。

用法：
    python tools/d2codec/verify_walk_flags.py            # 跑两条判据，全过返回 0
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

# 判据 ① 的样本：文件名 → 文件名声明的开口方向（字母集合）
CASES = [
    ('caveE.ds1', 'E'), ('caveW.ds1', 'W'), ('caveN.ds1', 'N'), ('caveS.ds1', 'S'),
    ('caveNS.ds1', 'NS'), ('caveEW.ds1', 'EW'), ('caveSW.ds1', 'SW'), ('caveNE.ds1', 'NE'),
    ('caveNW.ds1', 'NW'), ('caveSE.ds1', 'SE'), ('caveNSE.ds1', 'NSE'), ('caveNSW.ds1', 'NSW'),
    ('caveNEW.ds1', 'NEW'), ('caveSEW.ds1', 'SEW'), ('caveNSEW.ds1', 'NSEW'),
]


def _load(folder, name):
    d = ds1mod.load_ds1(os.path.join(TILES, folder, name))
    tiles = {}
    for rel in d.dt1_files:
        fp = os.path.join(RAW, rel.replace('\\', os.sep))
        if os.path.exists(fp):
            for t in dt1mod.load_dt1(fp).tiles:
                tiles.setdefault(t.composite_index, t)
    return d, tiles


def _edges(mask):
    w, h = len(mask[0]), len(mask)
    bx = range(w // 3, w - w // 3 + 1)
    by = range(h // 3, h - h // 3 + 1)
    out = ''
    if any(mask[0][x] for x in bx):
        out += 'N'
    if any(mask[h - 1][x] for x in bx):
        out += 'S'
    if any(mask[y][0] for y in by):
        out += 'W'
    if any(mask[y][w - 1] for y in by):
        out += 'E'
    return out


def check_openings(mode_name, fn):
    """判据 ①：以 (d, tiles, 某口径) 逐块判；返回失败清单。"""
    bad = []
    for name, want in CASES:
        d, tiles = _load('CAVES', name)
        w, h = d.width, d.height
        mask = [[False] * w for _ in range(h)]
        for y in range(h):
            for x in range(w):
                f = d.floor_at(x, y)
                t = tiles.get(f.tile_index) if not f.is_empty else None
                mask[y][x] = bool(t) and fn(t.flags)
        got = _edges(mask)
        if sorted(got) != sorted(want):
            bad.append((name, want, got))
    return bad


def check_water_and_grass():
    """判据 ②：水（`river.dt1`）**大多数地砖不可走**；地面（`TOWN/floor.dt1`）**大多数可走**。

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

    print('── 判据 ①：`CAVES/cave{方向}.ds1` 的开口方向必须与文件名一致 ──')
    fails = {}
    for mode_name, fn in MODES.items():
        bad = check_openings(mode_name, fn)
        fails[mode_name] = bad
        print('  %-16s %d/%d 命中%s'
              % (mode_name, len(CASES) - len(bad), len(CASES),
                 '' if not bad else '   失败=' + ', '.join('%s(期望%s 实际%s)' % b for b in bad)))

    print()
    print('── 判据 ②：水（river）多数地砖必须**阻挡**、地面（floor）多数必须**可走** ──')
    second_bad = 0
    for rel, n, ok, good in check_water_and_grass():
        if not good:
            second_bad += 1
        print('  %-22s 地砖 %3d 张，中心 subtile 可走 %3d 张（%.0f%%）⇒ %s'
              % (rel, n, ok, 100.0 * ok / n if n else 0.0, 'OK' if good else '❌'))

    print()
    adopted = 'center(采用)'
    if fails.get(adopted):
        print('❌ 采用的口径（%s）没过判据 ①：%s' % (adopted, fails[adopted]))
        return 1
    if second_bad:
        print('❌ 采用的口径（%s）没过判据 ②：%d 张表不合格' % (adopted, second_bad))
        return 1
    print('✅ 采用的口径（center = 中心 subtile 不带 Walk/PlayerWalk 标志）两条判据全过；')
    print('   旧口径 any_walk 在 %d 个样本上失败（这正是本轮修掉的那个 bug）。'
          % len(fails.get('any_walk(旧口径)', [])))
    return 0


if __name__ == '__main__':
    sys.exit(main())
