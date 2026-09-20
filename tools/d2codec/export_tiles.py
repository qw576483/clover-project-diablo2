# -*- coding: utf-8 -*-
"""把原版 `.dt1` 解成 PNG 落进 Unity 工程（`Module/Map` 用）。

产出（**由 `Assets/Editor/AssetImporter.cs` 统一按像素图导入**：Point / Single / 无压缩）：
    client/Assets/Resources/Clover/D2/Tiles/<pack>/<idx>.png      orientation==0 的地砖
    client/Assets/Resources/Clover/D2/Objects/<pack>/<idx>.png    墙 / 帐篷 / 树 / 栅栏 …
    client/Assets/Resources/Clover/D2/Tiles/<pack>/manifest.json  该 pack 的瓦片元数据
    client/Assets/Resources/Clover/D2/Objects/<pack>/manifest.json

`<idx>` = 该 dt1 内 tile 头的**数组下标**（0 起，3 位补零）。
⚠️ **不能用 `main/sub/orientation` 命名**：DT1 里同一组 (m,s,o) 会有多个 **rarity 变体**
（实测 TOWN/floor.dt1 的 144 个 tile 里有 7 个都是 m=5 s=0 o=0）⇒ 会互相覆盖。
DS1 引用瓦片用的是 `main/sub/orientation`，所以 manifest 里同时记这两个键。

调色板 = `data/global/palette/ACT1/Pal.PL2`（依据见 `pl2.py` 文件头）。

用法：
    python export_tiles.py [--raw <d2raw 根>] [--out <Resources/Clover/D2 根>] [--only pack,pack]
"""

import json
import os
import sys

try:
    from . import dt1 as dt1mod
    from . import pl2 as pl2mod
    from . import pngio
except ImportError:
    import dt1 as dt1mod
    import pl2 as pl2mod
    import pngio

# ── 默认路径（项目内相对位置，可移植）────────────────────────────────────────
# 解包产物统一在 `<项目根>/原版资源/d2raw`（skill §1.9：原版素材只放「原版资源」）。
_REPO = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
DEFAULT_RAW = os.path.join(_REPO, '原版资源', 'd2raw')
DEFAULT_OUT = os.path.join(_REPO, 'client', 'Assets', 'Resources', 'Clover', 'D2')

# ── 用到的 dt1（原版路径 → 本项目的 pack 名）─────────────────────────────────
#   pack 名 = 本项目自定的短名，同时是 `Resources/Clover/D2/{Tiles,Objects}/<pack>/` 目录名。
#
#   这张表**不是随手挑的**：它 = 三个区域的原版 DS1 声明的 dt1 依赖去重后的结果。
#   实测命令（`tools/d2codec/ds1.py <ds1>`）：
#     · 罗格营地 townN1/E1/S1/W1.ds1 →
#         town/treegroups·stonewall·floor·objects·fence + outdoors/{river,treegroups,stonewall,objects}
#     · 血腥荒野 wild1..wild4/trees2.ds1 →
#         outdoors/{stones,treegroups,stonewall,fence} + **TOWN/floor.dt1**（野外地面也用城镇那张地砖表）
#   故 `town_floor` 同时是"城镇地面"和"野外地面"的来源 —— **原版就是这么分的**。
#   ⚠️ 刻意**不导出** `OUTDOORS/Outdoor1.dt1`：它是 **DT1 v4.1** 旧格式（`version=(4,1)`，
#      参考实现 `DT1.cs:280` 也只接受 7.6），且**没有任何 DS1 依赖它**（实测）⇒ 不是本项目需要的东西。
PACKS = [
    # (相对 d2raw 的 dt1 路径, pack 名, 用途说明)
    ('data/global/tiles/ACT1/TOWN/floor.dt1',          'town_floor',      '罗格营地 + 血腥荒野的**地面**（原版共用）'),
    ('data/global/tiles/ACT1/TOWN/fence.dt1',          'town_fence',      '罗格营地栅栏（木桩 + 石基）'),
    ('data/global/tiles/ACT1/TOWN/objects.dt1',        'town_objects',    '帐篷 / 篝火 / 木桶 / 货物'),
    ('data/global/tiles/ACT1/TOWN/trees.dt1',          'town_trees',      '营地周边树木'),
    ('data/global/tiles/ACT1/OUTDOORS/treegroups.dt1', 'moor_trees',      '血腥荒野树丛'),
    ('data/global/tiles/ACT1/OUTDOORS/stones.dt1',     'moor_stones',     '血腥荒野碎石/岩石'),
    ('data/global/tiles/ACT1/OUTDOORS/stonewall.dt1',  'moor_stonewall',  '血腥荒野石墙/断墙'),
    ('data/global/tiles/ACT1/OUTDOORS/fence.dt1',      'moor_fence',      '血腥荒野木栏'),
    ('data/global/tiles/ACT1/OUTDOORS/objects.dt1',    'moor_objects',    '帐篷 / 营地杂物（原版城镇的帐篷就在这张表里）'),
    ('data/global/tiles/ACT1/CAVES/cave.dt1',          'cave',            '邪恶洞穴地面 / 岩壁'),
    ('data/global/tiles/ACT1/CAVES/cavedr.dt1',        'cave_door',       '洞穴岩柱/门框'),
    ('data/global/tiles/ACT1/BARRACKS/warp.dt1',       'warp',            '出入口传送点（原版城镇出口就靠它的 orientation 10）'),
    ('data/global/tiles/ACT1/OUTDOORS/river.dt1',      'moor_river',      '河/水边（原版野外与城镇边界都用）'),
    # ── ★ 野外拼块轮新增（`export_wild_layout.py` 的块依赖到它们）─────────────────
    #   依据 = `LvlTypes.txt` 的 `Id=2 Act 1 - Wilderness` 的 dt1 槽表：
    #     槽 11 = Outdoors/Cliff1.dt1、槽 10 = Cliff2.dt1、槽 13 = Corner.dt1
    #     （`LvlPrest` 的「Act 1 - Wild Cliff Border *」dt1mask 恰好只含这些位 ⇒ 崖壁边界块用它）
    #     槽 19 = Outdoors/puddle.dt1、槽 23 = Outdoors/Swamp.dt1
    #     （`LvlSub` Type=6 的 Puddles / Swamp Small / Swamp Big dt1mask 含这些位）
    #   ⚠️ 实测：`OUTDOORS/trees.dt1` **不存在**；`Trees2/Trees3.ds1`（LvlPrest "Act 1 - Tree Fill"）
    #     与 `LvlSub Type=6 Trees`（`trees.ds1`）依赖的都是 **`TOWN/trees.dt1`**
    #     （`ds1.py` 打印出的完整路径：`data\global\tiles\act1\town\trees.dt1`）
    #     ⇒ 复用上面的 `town_trees` pack，**不再另开一个**。
    ('data/global/tiles/ACT1/OUTDOORS/cliff1.dt1',     'moor_cliff1',     '野外崖壁（LvlType 2 槽 11，StnClf* 边界块用）'),
    ('data/global/tiles/ACT1/OUTDOORS/cliff2.dt1',     'moor_cliff2',     '野外崖壁（槽 10）'),
    ('data/global/tiles/ACT1/OUTDOORS/corner.dt1',     'moor_corner',     '崖壁转角（槽 13）'),
    ('data/global/tiles/ACT1/OUTDOORS/puddle.dt1',     'moor_puddle',     '野外水洼（槽 19，LvlSub Puddles）'),
    ('data/global/tiles/ACT1/OUTDOORS/swamp.dt1',      'moor_swamp',      '野外沼泽水（槽 23，LvlSub Swamp）'),
    # ── ★ 木桥（罗格营地跨河那一座）────────────────────────────────────────────
    #   依据 = `LvlPrest.txt`「Act 1 - Town 1」的 File2 `Act1/Town/TownE1.ds1` 与
    #     「Act 1 - Town 1 Transition E」的 `Act1/Town/TownETrans.ds1`：
    #     `python tools/d2codec/ds1.py <ds1>` 打印的依赖表里**只有这两块**含
    #     `data/global/tiles/act1/outdoors/bridge.dt1`（其它三块 TownN1/S1/W1 都没有）。
    #   桥的格位（本地坐标）：`TownE1` floor x∈[47,56] y∈[15,18]、wall x∈[47,56] y∈{16,18}；
    #     `TownETrans` 的桥恰好在它自己的第 0 列（x=0, y∈[15,18]）—— 与 TownE1 的
    #     第 56 列（DS1 的 +1 共享边列）相接，即**过渡带是这座桥向东的延伸段**。
    ('data/global/tiles/ACT1/OUTDOORS/bridge.dt1',     'moor_bridge',     '罗格营地跨河木桥（TownE1 + TownETrans 独有）'),
]

PALETTE_REL = 'data/global/palette/ACT1/Pal.PL2'


def rgba_to_file(out_path, img, palette):
    pngio.write_rgba(out_path, img.w, img.h, img.to_rgba(palette))


def export_pack(raw_root, out_root, rel_dt1, pack, note, palette):
    src = os.path.join(raw_root, rel_dt1.replace('/', os.sep))
    if not os.path.exists(src):
        print('  [SKIP] 源文件不存在：%s' % src)
        return None

    try:
        dt1 = dt1mod.load_dt1(src)
    except ValueError as exc:
        # 非预期分支：格式版本不对（实测 OUTDOORS/Outdoor1.dt1 是旧格式 4.1）⇒ 跳过并留痕
        print('  [SKIP] %-14s %s' % (pack, exc))
        return None
    with open(src, 'rb') as fh:
        data = fh.read()

    tiles_dir = os.path.join(out_root, 'Tiles', pack)
    objs_dir = os.path.join(out_root, 'Objects', pack)

    floors = []
    walls = []
    skipped = 0
    for tile in dt1.tiles:
        if tile.width <= 0 or tile.pixel_height <= 0:
            # 非预期但**原版确实存在**的占位瓦片（实测 town_trees.dt1 有 7 个 0×0 的；
            # 参考实现 `DT1.cs:184-188` 也只是打个 "Zero size" 就 continue）——
            # 跳过并计数，绝不写出 0×0 的坏 PNG（Unity 会报 "File could not be read"）。
            skipped += 1
            continue

        img = dt1mod.render_tile(tile, data=data)
        idx = '%03d' % tile.array_index
        rec = {
            'idx': tile.array_index,
            'file': idx + '.png',
            'w': tile.width,
            'h': tile.pixel_height,
            'main': tile.main_index,
            'sub': tile.sub_index,
            'orientation': tile.orientation,
            'compositeIndex': tile.composite_index,
            'rarity': tile.rarity,
            'walk': tile.is_walkable_flag,
            'bbox': list(img.opaque_bbox) if img.opaque_bbox else None,
        }

        if tile.is_floor:
            os.makedirs(tiles_dir, exist_ok=True)
            rgba_to_file(os.path.join(tiles_dir, idx + '.png'), img, palette)
            floors.append(rec)
        else:
            os.makedirs(objs_dir, exist_ok=True)
            rgba_to_file(os.path.join(objs_dir, idx + '.png'), img, palette)
            walls.append(rec)

    _write_manifest(tiles_dir, pack, rel_dt1, note, 'Tiles', floors)
    _write_manifest(objs_dir, pack, rel_dt1, note, 'Objects', walls)

    print('  %-16s %-22s 地砖 %3d / 物件 %3d / 跳过 0×0 %d'
          % (pack, rel_dt1.split('/')[-1], len(floors), len(walls), skipped))
    return {'pack': pack, 'dt1': rel_dt1, 'floors': len(floors), 'walls': len(walls),
            'skipped': skipped}


def _write_manifest(directory, pack, rel_dt1, note, kind, records):
    if not records:
        return
    payload = {
        'pack': pack,
        'sourceDt1': rel_dt1,
        'kind': kind,
        'note': note,
        'palette': PALETTE_REL,
        'tileCount': len(records),
        'tiles': records,
    }
    with open(os.path.join(directory, 'manifest.json'), 'w', encoding='utf-8') as fh:
        json.dump(payload, fh, ensure_ascii=False, indent=1)


def main(argv):
    raw_root = DEFAULT_RAW
    out_root = DEFAULT_OUT
    only = None
    if '--raw' in argv:
        raw_root = argv[argv.index('--raw') + 1]
    if '--out' in argv:
        out_root = argv[argv.index('--out') + 1]
    if '--only' in argv:
        only = set(argv[argv.index('--only') + 1].split(','))

    palette = pl2mod.load_pl2(os.path.join(raw_root, PALETTE_REL.replace('/', os.sep)))
    print('调色板 %s（256 条，取前 1024 字节）' % PALETTE_REL)
    print('输出根 %s' % out_root)

    stats = []
    for rel_dt1, pack, note in PACKS:
        if only and pack not in only:
            continue
        r = export_pack(raw_root, out_root, rel_dt1, pack, note, palette)
        if r:
            stats.append(r)

    tf = sum(s['floors'] for s in stats)
    tw = sum(s['walls'] for s in stats)
    print('合计：%d 个 pack，地砖 PNG=%d，物件 PNG=%d，共 %d' % (len(stats), tf, tw, tf + tw))
    return 0


if __name__ == '__main__':
    sys.exit(main(sys.argv))
