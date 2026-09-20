# -*- coding: utf-8 -*-
"""把**原版血腥荒野（Act I 野外）的拼块**从 `OUTDOORS/*.ds1` 抽成 C# 表
`client/Assets/Scripts/Module/Map/MapGenWildLayout.cs`（生成物，禁止手改）。

为什么这么做（**这是原版的做法，不是我们自创的**）：
  原版血腥荒野**不是一张整图**，而是引擎 DRLG 按「**8×8 格的预设块网格**」拼出来的：
    · `Levels.txt` → `Blood Moor`：Id=2 / LevelType=**2**(Act 1 - Wilderness) /
      **DrlgType=3**(户外随机) / SizeX=SizeY=**80**（⇒ 10×10 个 8 格块）
    · `LvlPrest.txt`（**块清单的唯一权威来源**，逐行抄在下面 `PIECES` 表里）：
        「Act 1 - Wild Border 1..12」            SizeX=8  SizeY=8 → `Bord1..12.ds1`（+ b/c/o/oe 变体）
        「Act 1 - Wild Cliff Border 2,3,5,6A,6B,6C,7,10」 SizeX=8 SizeY=8 → `StnClf*.ds1`
        「Act 1 - Wild Cliff Cave Left/Right」    SizeX=8  SizeY=8 → `CAVES/clfcave*.ds1`
        「Act 1 - Fence Fill 1..6」              SizeX/SizeY ∈ {16×16, 8×16, 16×8, 8×8} → `Wild1..9.ds1`
        「Act 1 - Tree Fill」                    SizeX=SizeY=16 → `Trees2/Trees3.ds1`
        「Act 1 - Cave Entrance」                SizeX=SizeY=8  → `CAVES/CaveDr1,2.ds1`（洞穴入口）
    · `LvlSub.txt` Type=6（散落件）：`Stone / Trees / Puddles / Swamp Big / Swamp Small / Wild Objects`
      （`LvlSub.dt1mask` 是 **LvlTypes 槽位位掩码**；LvlType 2 的槽 1 = `Town/Floor.dt1`
       ⇒ 只有 `mask & 1` 的行属于 Act I 野外 —— 逐行核对：1 / 3 / 16385 / 262145 / 4194305 / 257 全部含 bit0）
    · LvlType 2 的 dt1 槽表（`LvlTypes.txt` Id=2）同时说明：野外地面 = `Act1/Town/Floor.dt1`，
      崖壁 = `Cliff1/Cliff2/Corner`，树丛 = `TreeGroups`，石墙 = `stonewall`，栅栏 = `Fence`，
      岩石 = `Stones`，水 = `River/pond/puddle`。

⇒ 所以把血腥荒野做成原版那样，唯一正确的做法是**用原版这套块去拼**（块边长 8 格，
   相邻块共享 1 格 = 块 ds1 是 9×9），而不是自己写一个"随机崖壁带 + 随机撒障碍"。

每格抽三样（与 `export_town_layout.py` / `export_cave_layout.py` 同一口径）：
    · kind：'.' 可走 / '#' 阻挡 / ' ' 原版这格没有 floor（图外）
    · ground / object：floor 层与 wall 层的瓦片键（`<packId:3><tileIdx:3>`，`------` = 无）
另外每块带一个**四边开通标志**（`OpenN/S/W/E`，由该块可走掩码的边带算出）——
生成器据此挑「能从这头穿到那头」的边界块来放**出入口**，而不是自己凿洞。

用法：
    python export_wild_layout.py [--out <MapGenWildLayout.cs>] [--debug]
"""

import os
import sys
from collections import OrderedDict

try:
    from . import ds1 as ds1mod
    from . import dt1 as dt1mod
    from . import export_tiles as exp
except ImportError:
    import ds1 as ds1mod
    import dt1 as dt1mod
    import export_tiles as exp

_REPO = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
RAW = os.path.join(_REPO, '原版资源', 'd2raw')
OUT_REL = 'data/global/tiles/ACT1/OUTDOORS'
CAV_REL = 'data/global/tiles/ACT1/CAVES'
TOWN_REL = 'data/global/tiles/ACT1/TOWN'
DEFAULT_OUT = os.path.join(_REPO, 'client', 'Assets', 'Scripts', 'Module', 'Map',
                           'MapGenWildLayout.cs')

EMPTY = '------'

# 分组（与 C# 侧 `MapGenWildLayout.Group*` 一一对应）
G_BORDER = 0        # 边界/崖壁块（8×8）
G_FILL16 = 1        # 16×16 填充
G_FILL16X8 = 2      # 16×8
G_FILL8X16 = 3      # 8×16
G_FILL8 = 4         # 8×8 填充
G_ENTRANCE = 5      # 洞穴入口块（8×8）
G_SCATTER = 6       # LvlSub Type=6 散落件
G_BAND = 7          # ★ 罗格营地的**过渡带**（8×40，`LvlPrest`「Act 1 - Town 1 Transition E」）

# ── 块清单：**(名字, 子目录, ds1 文件, 块宽, 块高, 组, 原版出处)**
#    块宽/块高 = `LvlPrest.txt` 的 SizeX/SizeY（**格距 pitch**，不是 ds1 尺寸：
#    原版块 ds1 一律比 pitch 大 1 格 ⇒ 相邻块共享那一格，这就是 pitch = SizeX 的由来）。
PIECES = []


def _p(name, folder, fname, pw, ph, group, src):
    PIECES.append(dict(name=name, rel='%s/%s' % (folder, fname), pw=pw, ph=ph,
                       group=group, src=src))


# ── ① 野外边界（LvlPrest "Act 1 - Wild Border 1..12"，SizeX=SizeY=8）────────────
for _i in range(1, 13):
    _p('bord%d' % _i, OUT_REL, 'bord%d.ds1' % _i, 8, 8, G_BORDER,
       'LvlPrest Act 1 - Wild Border %d' % _i)
    for _v in ('b', 'c', 'o', 'oe'):        # b/c = 同形状变体；o/oe = 带开口的那两套
        _p('bord%d%s' % (_i, _v), OUT_REL, 'bord%d%s.ds1' % (_i, _v), 8, 8, G_BORDER,
           'LvlPrest Act 1 - Wild Border %d (variant %s)' % (_i, _v))
# ── ② 崖壁边界（LvlPrest "Act 1 - Wild Cliff Border *"，SizeX=SizeY=8）──────────
for _n, _f in (('2', 'stnclf2'), ('3', 'stnclf3'), ('5', 'stnclf5'), ('6a', 'stnclf6a'),
               ('6b', 'stnclf6b'), ('6c', 'stnclf6c'), ('7', 'stnclf7'),
               ('10', 'stnclf10'), ('L1', 'stnclfL1'), ('L2', 'stnclfL2'),
               ('R1', 'stnclfR1'), ('R2', 'stnclfR2')):
    _p('stnclf' + _n, OUT_REL, _f + '.ds1', 8, 8, G_BORDER,
       'LvlPrest Act 1 - Wild Cliff Border %s' % _n)
# ── ③ 崖壁上的洞口件（LvlPrest "Act 1 - Wild Cliff Cave Left/Right"）────────────
_p('clfcave', CAV_REL, 'clfcave.ds1', 8, 8, G_BORDER, 'LvlPrest Act 1 - Wild Cliff Cave Left')
_p('clfcave2', CAV_REL, 'clfcave2.ds1', 8, 8, G_BORDER, 'LvlPrest Act 1 - Wild Cliff Cave Right')
# ── ④ 内部填充（LvlPrest "Act 1 - Fence Fill 1..6" / "Tree Fill"）──────────────
_p('wild1', OUT_REL, 'wild1.ds1', 16, 16, G_FILL16, 'LvlPrest Act 1 - Fence Fill 1')
_p('wild2', OUT_REL, 'wild2.ds1', 16, 16, G_FILL16, 'LvlPrest Act 1 - Fence Fill 1')
_p('wild3', OUT_REL, 'wild3.ds1', 16, 16, G_FILL16, 'LvlPrest Act 1 - Fence Fill 3')
_p('wild4', OUT_REL, 'wild4.ds1', 16, 16, G_FILL16, 'LvlPrest Act 1 - Fence Fill 3')
_p('wild5', OUT_REL, 'wild5.ds1', 8, 8, G_FILL8, 'LvlPrest Act 1 - Fence Fill 4')
_p('wild6', OUT_REL, 'wild6.ds1', 16, 8, G_FILL16X8, 'LvlPrest Act 1 - Fence Fill 5')
_p('wild7', OUT_REL, 'wild7.ds1', 8, 16, G_FILL8X16, 'LvlPrest Act 1 - Fence Fill 6')
_p('wild8', OUT_REL, 'wild8.ds1', 8, 16, G_FILL8X16, 'LvlPrest Act 1 - Fence Fill 2')
_p('wild9', OUT_REL, 'wild9.ds1', 8, 16, G_FILL8X16, 'LvlPrest Act 1 - Fence Fill 2')
_p('trees2', OUT_REL, 'trees2.ds1', 16, 16, G_FILL16, 'LvlPrest Act 1 - Tree Fill')
_p('trees3', OUT_REL, 'trees3.ds1', 16, 16, G_FILL16, 'LvlPrest Act 1 - Tree Fill')
# ── ⑤ 洞穴入口（LvlPrest "Act 1 - Cave Entrance"，SizeX=SizeY=8）───────────────
_p('cdr1', CAV_REL, 'cavedr1.ds1', 8, 8, G_ENTRANCE, 'LvlPrest Act 1 - Cave Entrance')
_p('cdr2', CAV_REL, 'cavedr2.ds1', 8, 8, G_ENTRANCE, 'LvlPrest Act 1 - Cave Entrance')
# ── ⑥ 散落件（LvlSub.txt Type=6，适用于 LvlType 2）────────────────────────────
_p('sc_stone', OUT_REL, 'stone.ds1', 8, 8, G_SCATTER, 'LvlSub Type=6 Stone')
_p('sc_trees', OUT_REL, 'trees.ds1', 8, 8, G_SCATTER, 'LvlSub Type=6 Trees')
_p('sc_pud', OUT_REL, 'pud.ds1', 8, 8, G_SCATTER, 'LvlSub Type=6 Puddles')
_p('sc_swamp', OUT_REL, 'swamp.ds1', 8, 8, G_SCATTER, 'LvlSub Type=6 Swamp Small')
_p('sc_swamp2', OUT_REL, 'swamp2.ds1', 8, 8, G_SCATTER, 'LvlSub Type=6 Swamp Big')
_p('sc_obj', OUT_REL, 'object.ds1', 8, 8, G_SCATTER, 'LvlSub Type=6 Wild Objects')
# ── ⑦ ★ 罗格营地的**过渡带**（`LvlPrest`「Act 1 - Town 1 Transition E」，Def=2）─────
#   它不是野外块，而是**城镇接缝**那一块：原版引擎把它铺在**野外关卡**靠城的那条边上
#   （依据：参考实现 `libd2/packages/drlg/src/drlg/outdoors/OutRoom.zig:251-275`
#     `SpawnTownTransitionsAndCaves` 用 `SpawnOutdoorLevelPresetEx(pLevel, 0, 1, 2, …)`
#     放在野外关卡的西边界；调用点 = `drlg/outdoors/ActInit.zig:75-83`，只对 Act1 的
#     2..7 号户外关卡调用）。
#   尺寸 8×40 = 1 块宽 × 5 块高（`LvlPrest` 的 SizeX/SizeY = 8/40）⇒ 正好沿一条边铺 5 槽。
#   ⚠️ **S 那条（`TownSTrans`/`TownSTrans2`，56×8）没有进来**：它铺在野外关卡的**北边界**
#     （`SpawnOutdoorLevelPresetEx(pLevel, 0, 0, 3, …)`），本项目野外只有**一条**与城镇
#     相连的接缝（西边界的回城口）⇒ 见 `策划/自审对比/场景对照.md` 的登记。
_p('towne', TOWN_REL, 'TownETrans.ds1', 8, 40, G_BAND,
   'LvlPrest Act 1 - Town 1 Transition E')

# 供 `MapGenWildLayout` 用的 alphabet 符号表（下标 → 字符）：全部可打印 ASCII 去掉 `"` 与 `\`。
SYMBOLS = ''.join(chr(c) for c in range(33, 127) if chr(c) not in '"\\')


def _sym(i):
    return SYMBOLS[i // len(SYMBOLS)] + SYMBOLS[i % len(SYMBOLS)]


def _palette():
    if _palette.cache is None:
        try:
            from . import pl2 as pl2mod
        except ImportError:
            import pl2 as pl2mod
        _palette.cache = pl2mod.load_pl2(os.path.join(
            RAW, 'data', 'global', 'palette', 'ACT1', 'Pal.PL2'))
    return _palette.cache


_palette.cache = None


# 草地 / 泥土的**绿度**门限（均值色的 `G-B`）。
# 为什么是 14 而不是"能区分出绿色就算"：原版 `TOWN/floor.dt1` 的 144 张地砖**全都是**
# 暗绿+土褐混色的碎斑图（实测均值色 R 27~37 / G 32~37 / B 14~25），
# 用宽松门限（如 G-B ≥ 8）会把 77 张**偏土褐**的也算作草地，逐格随机铺出来就是
# 「一格草一格土」的棋盘格 —— 实测实机截图（`a30_01_moor_spawn.png`）里像一块花被子，
# 与原版血腥荒野"一大片绿"的观感不符。收紧到 G-B ≥ 14（取绿度最高的那批）就对了。
GRASS_GREENNESS = 14.0


def _greenness(rgba, w, h):
    """瓦片**不透明像素**的均值色 → `(G-B, G-R)`；没有不透明像素返回 None。"""
    sr = sg = sb = n = 0
    for i in range(w * h):
        if rgba[i * 4 + 3] == 0:
            continue
        sr += rgba[i * 4]
        sg += rgba[i * 4 + 1]
        sb += rgba[i * 4 + 2]
        n += 1
    if n == 0:
        return None
    r, g, b = sr / n, sg / n, sb / n
    return (g - b, g - r)


def _is_grass(rgba, w, h):
    """该地砖是不是**草地**（绿度门限见 `GRASS_GREENNESS`）。"""
    gg = _greenness(rgba, w, h)
    if gg is None:
        return False
    gb, gr = gg
    return gb >= GRASS_GREENNESS and gr >= -2.0


def ground_tiles(pack_of, debug):
    """原版野外**地面**用的瓦片（`TOWN/floor.dt1`，LvlType 2 的槽 1）分草地/泥土两档。

    出处：`LvlTypes.txt` 的 `Id=2 Act 1 - Wilderness` 第一个 dt1 槽 = `Act1/Town/Floor.dt1`
    （原版野外地面与城镇地面共用同一张表）；分类用**像素均值色**，不靠文件名猜。
    """
    fp = os.path.join(RAW, TOWN_REL.replace('/', os.sep), 'floor.dt1')
    d = dt1mod.load_dt1(fp)
    with open(fp, 'rb') as fh:
        data = fh.read()
    pal = _palette()
    pack = pack_of[(TOWN_REL + '/floor.dt1').lower()]
    grass, dirt = [], []
    for tile in d.tiles:
        if tile.width <= 0 or tile.pixel_height <= 0 or not tile.is_floor:
            continue
        img = dt1mod.render_tile(tile, data=data)
        if not img.mask or sum(img.mask) == 0:
            continue
        key = '%s/%03d' % (pack, tile.array_index)
        (grass if _is_grass(img.to_rgba(pal), img.w, img.h) else dirt).append(key)
    if debug:
        print('  地面瓦片（town/floor.dt1）：草地 %d 张 / 泥土 %d 张' % (len(grass), len(dirt)))
    return grass, dirt


# 阻挡物的**种类码**（给 C# 侧决定 `TileKind`：树/栅栏/石墙/崖壁…）。
# 依据 = 该格物件瓦片落在哪张 dt1 上（`providers`），不是靠文件名猜块名。
#   `.` 只出现在"不阻挡"的格上（C# 侧只在 kind=='#' 时读它）。
TILE_CLASS = (
    ('treegroups.dt1', 'T'),      # 树丛（野外边界/树线）
    ('trees.dt1', 'T'),           # 单棵树（TOWN/trees.dt1，野外 Tree Fill 也用）
    ('fence.dt1', 'F'),           # 木栅栏
    ('stonewall.dt1', 'W'),       # 石矮墙
    ('cliff1.dt1', 'C'),          # 崖壁
    ('cliff2.dt1', 'C'),
    ('corner.dt1', 'C'),
    ('border.dt1', 'C'),
    ('stones.dt1', 'S'),          # 碎石 / 岩石
    ('objects.dt1', 'O'),         # 野外杂物（木桶/木桩/车…）
    ('cairn.dt1', 'S'),
    ('ruin.dt1', 'W'),
    ('cottages.dt1', 'W'),
    ('tower.dt1', 'W'),
    ('towerb.dt1', 'W'),
    ('pond.dt1', 'X'),            # 水（pond）
    ('puddle.dt1', 'X'),          # 水洼
    ('swamp.dt1', 'X'),           # 沼泽水
    ('river.dt1', 'X'),           # 河
    ('cavedr.dt1', 'O'),          # 洞穴口
)


def class_of(providers):
    """该格（按它用到的 dt1 名字集合）→ 种类码。"""
    for key, code in TILE_CLASS:
        if key in providers:
            return code
    return 'R'


def _load_tiles(ds1, pack_of, pack_id):
    """该 ds1 依赖的 dt1 → `best[composite] = (pack, idx, tile)` + `providers[composite] = {dt1 名}`。

    `providers` 用来判**阻挡物的种类**（树/栅栏/崖壁…，见 `class_of`），
    因为同一个 compositeIndex 可能由多张 dt1 提供（实测 `stonewall.dt1` 与 `cliff1.dt1` 有重叠）。
    """
    best = {}
    providers = {}
    for rel in ds1.dt1_files:
        rel_l = rel.replace('\\', '/').lower()
        fp = os.path.join(RAW, rel.replace('\\', os.sep))
        if not os.path.exists(fp):
            print('  [WARN] 依赖 dt1 不存在：%s' % fp)
            continue
        pack = pack_of.get(rel_l)
        if pack is None:
            print('  [WARN] dt1 不在 export_tiles.PACKS 里（其瓦片不会导出）：%s' % rel)
            continue
        try:
            d = dt1mod.load_dt1(fp)
        except ValueError as exc:
            print('  [WARN] 跳过 %s' % exc)
            continue
        with open(fp, 'rb') as fh:
            data = fh.read()
        for tile in d.tiles:
            if tile.width <= 0 or tile.pixel_height <= 0:
                continue
            img = dt1mod.render_tile(tile, data=data)
            area = sum(img.mask)
            if area == 0:
                continue
            providers.setdefault(tile.composite_index, set()).add(os.path.basename(fp).lower())
            prev = best.get(tile.composite_index)
            if prev is not None and prev[2] >= area:
                continue
            best[tile.composite_index] = (pack, tile.array_index, area, tile)
    return best, providers


def cell_passable(tile):
    """**格级可走** = 该瓦片 5×5 subtile 的**中心**那一个不带阻挡标志。

    口径出处（**不是猜的**）：
      · `原版资源/参考工程_Diablerie/.../Engine/World/WorldGrid.cs:78-84`
        `ApplyTileCollisions`：`flagIndex` 从 0 递增遍历 25 个 subtile，
        `passable = (flags[flagIndex] & (Walk|PlayerWalk)) == 0` ⇒ **标志置位 = 不可走**。
      · 自洽检验（`.ai-tmp/test/flagtest.py` 可复跑）：对 `CAVES/cave{方向}.ds1` 15 个块，
        用本口径算出的可走掩码碰到的边与**文件名声明的开口方向**逐一吻合；
        换成「任一 subtile 可走」则 `caveN` 会多出 W 边（错）。
      · 语义：本项目一格 = 一个寻路节点 = 角色站在格子中心 ⇒ 中心 subtile 决定该节点能不能站。
    """
    return not (tile.flags[12] & 9)


def edge_open(walk, w, h):
    """四边「中段是否有可走格」标志（出入口靠它挑块，不自己凿洞）。"""
    band_x = range(max(0, w // 3), min(w, w - w // 3 + 1))
    band_y = range(max(0, h // 3), min(h, h - h // 3 + 1))
    return (any(walk[0][x] for x in band_x),
            any(walk[h - 1][x] for x in band_x),
            any(walk[y][0] for y in band_y),
            any(walk[y][w - 1] for y in band_y))


def build(out_path, debug):
    pack_of = {}
    for rel_dt1, pack, _note in exp.PACKS:
        pack_of[rel_dt1.lower()] = pack
    packs = sorted(set(pack_of.values()))
    pack_id = dict((p, i) for i, p in enumerate(packs))

    grass, dirt = ground_tiles(pack_of, debug)

    records = []
    skipped = []
    for spec in PIECES:
        src = os.path.join(RAW, spec['rel'].replace('/', os.sep))
        if not os.path.exists(src):
            skipped.append((spec['name'], '源文件不存在 %s' % spec['rel']))
            continue
        try:
            d = ds1mod.load_ds1(src)
        except Exception as exc:                       # noqa: BLE001 —— 非预期，必须留痕
            skipped.append((spec['name'], '%s: %s' % (type(exc).__name__, exc)))
            continue

        w, h = d.width, d.height
        tiles, providers = _load_tiles(d, pack_of, pack_id)
        if not tiles:
            skipped.append((spec['name'], '依赖 dt1 一张都没读到'))
            continue

        def key_of(cell):
            hit = None
            if cell is not None and not cell.is_empty:
                hit = tiles.get(cell.tile_index)
            if hit is None:
                return ''
            if hit[0] == 'warp':
                # 非预期但原版确实存在：`CAVES/cavedr1,2.ds1` 依赖 `BARRACKS/warp.dt1`
                # （编辑器留下的孤儿传送标记，纯色调色板）⇒ **不画**，与
                # `export_town_layout.py::is_warp_cell` / `export_pieces.py` 同一处理。
                return ''
            return '%03d%03d' % (pack_id[hit[0]], hit[1])

        kinds = [[' '] * w for _ in range(h)]
        cls = [['.'] * w for _ in range(h)]
        ground = [[''] * w for _ in range(h)]
        obj = [[''] * w for _ in range(h)]
        walk = [[False] * w for _ in range(h)]
        for y in range(h):
            for x in range(w):
                f = d.floor_at(x, y)
                wc = d.wall_at(0, x, y)
                has_floor = f is not None and not f.is_empty
                ft = tiles.get(f.tile_index) if has_floor else None
                ok = key_of(wc)
                if not has_floor:
                    kinds[y][x] = ' '                  # 原版这格没有 floor = 图外
                else:
                    walk[y][x] = cell_passable(ft[3]) if ft else False
                    # 有 wall 层物件（树 / 石墙 / 栅栏 / 崖壁 / 岩石）即阻挡；依据
                    # `Diablerie/.../Engine/World/WorldGrid.cs:78-84`
                    # `passable = (flags[12] & (Walk|PlayerWalk)) == 0`（置位 = 阻挡）。
                    kinds[y][x] = '.' if (walk[y][x] and not ok) else '#'
                ground[y][x] = key_of(f) if has_floor else ''
                obj[y][x] = ok
                # 种类码：有物件层瓦片就按物件判，否则按地砖判（水在 floor 层）
                if ok:
                    cls[y][x] = class_of(providers.get(wc.tile_index, set()))
                elif has_floor:
                    cls[y][x] = class_of(providers.get(f.tile_index, set()))

        o_n, o_s, o_w, o_e = edge_open(walk, w, h)
        n_walk = sum(1 for r in walk for v in r if v)
        rec = dict(spec)
        rec.update(w=w, h=h, kinds=kinds, cls=cls, ground=ground, objects=obj,
                   open_n=o_n, open_s=o_s, open_w=o_w, open_e=o_e, walkable=n_walk)
        records.append(rec)
        print('  %-10s %-18s %2dx%-3d pitch=%2dx%-3d open=%-4s 可走 %4d/%4d'
              % (spec['name'], os.path.basename(spec['rel']), w, h, spec['pw'], spec['ph'],
                 ''.join(c for c, f in (('N', o_n), ('S', o_s), ('W', o_w), ('E', o_e)) if f) or '-',
                 n_walk, w * h))

    for n, r in skipped:
        print('  [剔除] %-12s %s' % (n, r))

    by_group = {}
    for r in records:
        by_group.setdefault(r['group'], []).append(r['name'])
    print('  合计 %d 块；分组：%s' % (len(records),
                                 ' '.join('G%d=%d' % (g, len(v)) for g, v in sorted(by_group.items()))))
    print('  出入口候选（东西向可穿）：%s'
          % ', '.join(r['name'] for r in records if r['open_w'] and r['open_e']) or '-')

    _write_cs(out_path, records, packs, grass, dirt, skipped)
    print('  → %s' % out_path)
    return 0


HEADER = '''// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · Module/Map/MapGenWildLayout.cs
// ⚠️ **本文件是生成物，禁止手改**（生成器：`tools/d2codec/export_wild_layout.py`）。
//
// 数据来源 = **原版** `data/global/tiles/ACT1/{OUTDOORS,CAVES}/*.ds1`
//   （Blizzard North, 2000，取自 d2data.mpq；本项目非商用）。
// **挑哪些块**不是本项目决定的，而是**原版自己的生成规则表**：
//   · `Levels.txt`「Blood Moor」：LevelType=2(Act 1 - Wilderness) DrlgType=3 Size 80x80
//     ⇒ 10×10 个 8 格块（块边长 = `LvlPrest.txt` 的 SizeX）
//   · `LvlPrest.txt`「Act 1 - Wild Border 1..12」/「Wild Cliff Border *」/「Fence Fill 1..6」
//     /「Tree Fill」/「Cave Entrance」/「Wild Cliff Cave *」→ 下面每块的 `Source` 列逐块标注
//   · `LvlSub.txt` Type=6（Stone / Trees / Puddles / Swamp Big / Swamp Small / Wild Objects）
//     → `GroupScatter` 那几块
//   · `LvlPrest.txt`「Act 1 - Town 1 Transition E」（Def=2，8×40）→ `GroupBand` 那一块：
//     **罗格营地的过渡带**。原版引擎把它铺在**野外关卡靠城的那条边**上
//     （`libd2/.../drlg/outdoors/OutRoom.zig:251-275` + `ActInit.zig:75-83`）。
// 每块的**四边开通标志**由该块的可走掩码算出，生成器据此挑能"穿过去"的边界块当出入口。
//
// 三张表（逐格 1:1，不缩不放）：
//   `Pieces[i].Cells` 每格 2 个字符 = `Alphabet` 下标（行 0 = 块**最北**一行）
//   `Alphabet` 每条 = `<kind><class><ground6><object6>`（14 字符）
//     kind ：' ' = 原版这格没有 floor（图外）/ '.' = 可走地面 / '#' = 阻挡
//     class：阻挡物的种类（给 C# 侧定 `TileKind`，按**该格用到的 dt1 名**判定）
//            T=树(Tree) / F=栅栏(Fence) / W=石墙·废墟·村舍(Wall) / C=崖壁(Rock)
//            S=碎石(Rock) / O=杂物(Rock) / X=水(Rock) / R=其它阻挡(Rock) / .=非阻挡
//     ground6 / object6 = `<packId:3><tileIdx:3>`；`------` = 该层没有瓦片
//   `GrassTiles` / `DirtTiles` = 原版野外地面瓦片（`TOWN/floor.dt1`，按像素均值色分档）
//
// 剔除记录（源文件缺失 / 依赖 dt1 读不到）：
%s
// ─────────────────────────────────────────────────────────────────────────────
'''

CLASS_SHELL = '''using UnityEngine;

namespace Diablo2.Module.Map
{
    /// <summary>原版 Act I 野外（血腥荒野）拼块库（生成物，见文件头）。</summary>
    internal static class MapGenWildLayout
    {
        /// <summary>边界块的格距（原版 `LvlPrest.SizeX`）：相邻块共享 1 格，块 ds1 比 pitch 大 1。</summary>
        public const int BorderPitch = 8;

        /// <summary>分组码：边界块（8×8）。</summary>
        public const int GroupBorder = %d;

        /// <summary>分组码：16×16 填充块。</summary>
        public const int GroupFill16 = %d;

        /// <summary>分组码：16×8 填充块。</summary>
        public const int GroupFill16x8 = %d;

        /// <summary>分组码：8×16 填充块。</summary>
        public const int GroupFill8x16 = %d;

        /// <summary>分组码：8×8 填充块。</summary>
        public const int GroupFill8 = %d;

        /// <summary>分组码：洞穴入口块。</summary>
        public const int GroupEntrance = %d;

        /// <summary>分组码：`LvlSub` Type=6 散落件。</summary>
        public const int GroupScatter = %d;

        /// <summary>分组码：罗格营地的**过渡带**（8×40，铺在野外关卡靠城那条边上）。</summary>
        public const int GroupBand = %d;

        /// <summary>符号表（`Pieces[].Cells` 每 2 个字符 = 一个下标，base = 本表长度）。</summary>
        public const string SymTable = "%s";

        /// <summary>pack 目录名（下标 = 各编码里的 3 位 packId）。</summary>
        public static readonly string[] Packs =
        {
%s
        };

        /// <summary>原版野外**草地**地面瓦片（`TOWN/floor.dt1`，像素均值色判定）。</summary>
        public static readonly string[] GrassTiles =
        {
%s
        };

        /// <summary>原版野外**泥土 / 土路**地面瓦片（同上）。</summary>
        public static readonly string[] DirtTiles =
        {
%s
        };

        /// <summary>一块原版野外预设。</summary>
        internal sealed class Piece
        {
            /// <summary>块名（= 原版 ds1 文件名，去掉扩展名）。</summary>
            public readonly string Name;

            /// <summary>原版出处（`LvlPrest.txt` / `LvlSub.txt` 的 Name 列）。</summary>
            public readonly string Source;

            /// <summary>分组码（`Group*`）。</summary>
            public readonly int Group;

            /// <summary>格距宽（原版 `LvlPrest.SizeX`）。</summary>
            public readonly int PitchW;

            /// <summary>格距高（原版 `LvlPrest.SizeY`）。</summary>
            public readonly int PitchH;

            /// <summary>ds1 宽（= PitchW + 1）。</summary>
            public readonly int SizeW;

            /// <summary>ds1 高（= PitchH + 1）。</summary>
            public readonly int SizeH;

            /// <summary>北边中段是否有可走格（出入口挑块用）。</summary>
            public readonly bool OpenN;

            /// <summary>南边中段是否有可走格。</summary>
            public readonly bool OpenS;

            /// <summary>西边中段是否有可走格。</summary>
            public readonly bool OpenW;

            /// <summary>东边中段是否有可走格。</summary>
            public readonly bool OpenE;

            /// <summary>`SizeW × SizeH` 格，每格 **2 个字符** = <see cref="Alphabet"/> 下标（行 0 = 最北）。</summary>
            public readonly string Cells;

            /// <summary>`<kind><class><ground6><object6>`（14 字符）组合表；class 见文件头。</summary>
            public readonly string[] Alphabet;

            public Piece(string name, string source, int group, int pitchW, int pitchH,
                int sizeW, int sizeH, bool openN, bool openS, bool openW, bool openE,
                string cells, string[] alphabet)
            {
                Name = name;
                Source = source;
                Group = group;
                PitchW = pitchW;
                PitchH = pitchH;
                SizeW = sizeW;
                SizeH = sizeH;
                OpenN = openN;
                OpenS = openS;
                OpenW = openW;
                OpenE = openE;
                Cells = cells;
                Alphabet = alphabet;
            }

            /// <summary>东西向可穿（出入口放在西 / 东边界时用它）。</summary>
            public bool ThroughEW { get { return OpenW && OpenE; } }

            /// <summary>南北向可穿。</summary>
            public bool ThroughNS { get { return OpenN && OpenS; } }
        }

        /// <summary>全部预设块。</summary>
        public static readonly Piece[] Pieces =
        {
%s
        };

        /// <summary>`Cells` 里第 <paramref name="cell"/> 格（0 起，行优先）的 `Alphabet` 下标。</summary>
        public static int CellIndex(string cells, int cell)
        {
            var p = cell * 2;
            var hi = SymTable.IndexOf(cells[p]);
            var lo = SymTable.IndexOf(cells[p + 1]);
            return hi * SymTable.Length + lo;
        }

        /// <summary>解一个 `<packId:3><tileIdx:3>` 编码（`------` → 空串）。</summary>
        public static string Decode(string code)
        {
            if (string.IsNullOrEmpty(code) || code[0] == '-') return "";
            var packId = (code[0] - '0') * 100 + (code[1] - '0') * 10 + (code[2] - '0');
            if (packId < 0 || packId >= Packs.Length) return "";
            return Packs[packId] + "/" + code.Substring(3, 3);
        }
    }
}
'''


def _b(v):
    return 'true' if v else 'false'


def _write_cs(out_path, records, packs, grass, dirt, skipped):
    skip_lines = ['//   · %-12s %s' % (n, r) for n, r in skipped] or ['//   （无）']
    header = HEADER % '\n'.join(skip_lines)

    body = []
    for r in records:
        alphabet = OrderedDict()
        cells = []
        for y in range(r['h']):
            for x in range(r['w']):
                key = (r['kinds'][y][x], r['cls'][y][x], r['ground'][y][x] or EMPTY,
                       r['objects'][y][x] or EMPTY)
                if key not in alphabet:
                    alphabet[key] = len(alphabet)
                cells.append(_sym(alphabet[key]))
        body.append('            new Piece(')
        body.append('                "%s", "%s", %d, %d, %d, %d, %d, %s, %s, %s, %s,'
                    % (r['name'], r['src'], r['group'], r['pw'], r['ph'], r['w'], r['h'],
                       _b(r['open_n']), _b(r['open_s']), _b(r['open_w']), _b(r['open_e'])))
        body.append('                // %d×%d 格，行 0 = 最北' % (r['w'], r['h']))
        body.append('                "%s",' % ''.join(cells))
        body.append('                new[]')
        body.append('                {')
        for (k, c, g, o) in alphabet.keys():
            body.append('                    "%s%s%s%s",' % (k, c, g, o))
        body.append('                }),')

    text = CLASS_SHELL % (G_BORDER, G_FILL16, G_FILL16X8, G_FILL8X16, G_FILL8,
                          G_ENTRANCE, G_SCATTER, G_BAND, SYMBOLS,
                          '\n'.join('            "%s",' % p for p in packs),
                          '\n'.join('            "%s",' % t for t in grass),
                          '\n'.join('            "%s",' % t for t in dirt),
                          '\n'.join(body))

    with open(out_path, 'w', encoding='utf-8', newline='\r\n') as fh:
        fh.write(header + text)


def main(argv):
    out = argv[argv.index('--out') + 1] if '--out' in argv else DEFAULT_OUT
    print('从原版 ACT1/OUTDOORS + CAVES 抽野外拼块（%d 块）：' % len(PIECES))
    return build(out, '--debug' in argv)


if __name__ == '__main__':
    sys.exit(main(sys.argv))
