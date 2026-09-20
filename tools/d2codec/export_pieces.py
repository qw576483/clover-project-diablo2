# -*- coding: utf-8 -*-
"""把**原版野外地块 / 洞穴迷宫块**的 ds1 预设抽成 C# 数据表（生成 `MapGenPieceData.cs`）。

为什么要这一层（依据 = **原版自己的生成规则**，不是本项目编的）：

  ① 野外（血腥荒野）：`Levels.txt` 的「Act 1 - Wilderness 1」（id=2）写的是
     `SizeX=80 SizeY=80 DrlgType=3 LevelType=2 SubType=6` ⇒ 原版野外是**用预设块拼出来的**
     （LvlPrest 里 `Outdoors=1` 的那些 8×8 / 16×16 块），再按 `LvlSub.txt` 的 Type=6
     （Stone / Trees / Puddles / Swamp Big / Swamp Small / Wild Objects）随机撒装饰件。
     块清单（逐条来自 `LvlPrest.txt`）：
       · 「Act 1 - Wild Border 1..12」 SizeX=8 SizeY=8   → Bord1..Bord12.ds1（9×9，含崖壁）
       · 「Act 1 - Fence Fill 1..6」   16×16 / 8×16 / 16×8 / 8×8 → Wild1..Wild9.ds1
       · 「Act 1 - Tree Fill」         16×16            → Trees2/Trees3.ds1
  ② 洞穴（邪恶洞穴）：`Levels.txt` 的「Act 1 - Cave 1」（id=8，LevelName=Den of Evil）写的是
     `DrlgType=1 LevelType=3`，并挂 `LvlMaze.txt` 的「Act 1 - Cave 1」（Level=8 SizeX=24 SizeY=24）
     ⇒ 原版洞穴 = **按 24 格为一块的迷宫**，格子由 LvlPrest 的
     「Act 1 - Cave N/S/E/W/NS/EW/NE/…」（24×24 ⇒ ds1 25×25）与
     「Act 1 - Cave Treasure 1..5」（CaveRoomN.ds1，25×25 房间）填。
     迷宫格 24 而 ds1 是 25 ⇒ **相邻块共享 1 格**（这就是拼接 pitch = 24 的由来）。

每格抽三样（与 `export_town_layout.py` 同一口径，逐格 1:1，不缩不放）：
  · `kinds`：'.' = 可走地面 / '#' = 阻挡（崖壁·树·石墙·岩石）
  · `ground`：floor 层瓦片键（`<packId:3><tileIdx:3>`，`------` = 原版这格没有地面层）
  · `objects`：wall 层瓦片键（同上，`------` = 没有）

⛔ 不画 `warp.dt1`（BARRACKS/warp.dt1 的纯色调色板"传送标记"）—— 与罗格营地同一处理，
   理由见 `export_town_layout.py::is_warp_cell` 上方注释（原版引擎按 lvltype 装瓦片库时解析不到它）。

用法：
    python export_pieces.py [--out <MapGenPieceData.cs>] [--debug]
"""

import os
import sys

try:
    from . import ds1 as ds1mod
    from . import dt1 as dt1mod
    from . import export_tiles as exp
except ImportError:
    import ds1 as ds1mod
    import dt1 as dt1mod
    import export_tiles as exp

# 解包产物统一在 `<项目根>/原版资源/d2raw`（skill §1.9）。
_REPO = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
RAW = os.path.join(_REPO, '原版资源', 'd2raw')
DEFAULT_OUT = os.path.join(_REPO, 'client', 'Assets', 'Scripts', 'Module', 'Map',
                           'MapGenPieceData.cs')

OUT_DIR = 'data/global/tiles/ACT1/OUTDOORS'
CAV_DIR = 'data/global/tiles/ACT1/CAVES'
TOWN_DIR = 'data/global/tiles/ACT1/TOWN'
BARRACKS_DIR = 'data/global/tiles/ACT1/BARRACKS'

# ── 分组码（与 C# 侧 `MapGenPieces.Group*` 常量一一对应）──────────────────────
G_BORDER = 1        # 野外边界块（崖壁）
G_FILL16 = 2        # 野外填充块 16×16
G_FILL16x8 = 3      # 野外填充块 16×8
G_FILL8x16 = 4      # 野外填充块 8×16
G_FILL8 = 5         # 野外填充块 8×8
G_SCATTER = 6       # LvlSub Type=6 散落件
G_CAVE_ROOM = 7     # 洞穴房间
G_CAVE_CONN = 8     # 洞穴走廊
G_CAVE_ENT = 9      # 洞穴入口件

# 方向掩码位（与 C# 侧一致）：N=1 S=2 E=4 W=8
N, S, E, W = 1, 2, 4, 8

EMPTY = '------'


def _p(name, folder, fname, pitch_x, pitch_y, group, mask=0, note=''):
    return dict(name=name, rel='%s/%s' % (folder, fname), px=pitch_x, py=pitch_y,
                group=group, mask=mask, note=note)


# ── 预设清单（每行逐条都能在 LvlPrest.txt 里找到出处，见文件头）───────────────
PIECES = []
# 野外边界（LvlPrest "Act 1 - Wild Border 1..12"，SizeX=SizeY=8 ⇒ ds1 9×9）
for i in range(1, 13):
    PIECES.append(_p('bord%02d' % i, OUT_DIR, 'bord%d.ds1' % i, 8, 8, G_BORDER,
                     note='Act 1 - Wild Border %d' % i))
# 野外填充（LvlPrest "Act 1 - Fence Fill 1..6" / "Act 1 - Tree Fill"）
PIECES.append(_p('wild1', OUT_DIR, 'wild1.ds1', 16, 16, G_FILL16, note='Fence Fill 1'))
PIECES.append(_p('wild2', OUT_DIR, 'wild2.ds1', 16, 16, G_FILL16, note='Fence Fill 1'))
PIECES.append(_p('wild3', OUT_DIR, 'wild3.ds1', 16, 16, G_FILL16, note='Fence Fill 3'))
PIECES.append(_p('wild4', OUT_DIR, 'wild4.ds1', 16, 16, G_FILL16, note='Fence Fill 3'))
PIECES.append(_p('wild5', OUT_DIR, 'wild5.ds1', 8, 8, G_FILL8, note='Fence Fill 4'))
PIECES.append(_p('wild6', OUT_DIR, 'wild6.ds1', 16, 8, G_FILL16x8, note='Fence Fill 5'))
PIECES.append(_p('wild7', OUT_DIR, 'wild7.ds1', 8, 16, G_FILL8x16, note='Fence Fill 6'))
PIECES.append(_p('wild8', OUT_DIR, 'wild8.ds1', 8, 16, G_FILL8x16, note='Fence Fill 2'))
PIECES.append(_p('wild9', OUT_DIR, 'wild9.ds1', 8, 16, G_FILL8x16, note='Fence Fill 2'))
PIECES.append(_p('trees2', OUT_DIR, 'trees2.ds1', 16, 16, G_FILL16, note='Tree Fill'))
PIECES.append(_p('trees3', OUT_DIR, 'trees3.ds1', 16, 16, G_FILL16, note='Tree Fill'))
# 野外散落件（LvlSub Type=6 的 File 列）
PIECES.append(_p('sc_stone', OUT_DIR, 'stone.ds1', 0, 0, G_SCATTER, note='LvlSub Stone'))
PIECES.append(_p('sc_trees', OUT_DIR, 'trees.ds1', 0, 0, G_SCATTER, note='LvlSub Trees'))
PIECES.append(_p('sc_pud', OUT_DIR, 'pud.ds1', 0, 0, G_SCATTER, note='LvlSub Puddles'))
PIECES.append(_p('sc_swamp', OUT_DIR, 'swamp.ds1', 0, 0, G_SCATTER, note='LvlSub Swamp Small'))
PIECES.append(_p('sc_swamp2', OUT_DIR, 'swamp2.ds1', 0, 0, G_SCATTER, note='LvlSub Swamp Big'))
PIECES.append(_p('sc_obj', OUT_DIR, 'object.ds1', 0, 0, G_SCATTER, note='LvlSub Wild Objects'))
# 洞穴房间（LvlPrest "Act 1 - Cave Treasure 1..5" + CaveRoom6；SizeX=SizeY 空 ⇒ 按 ds1 自身 25×25，pitch 24）
for i in range(1, 7):
    PIECES.append(_p('room%d' % i, CAV_DIR, 'caveroom%d.ds1' % i, 24, 24, G_CAVE_ROOM,
                     note='CaveRoom%d' % i))
# 洞穴走廊（LvlPrest "Act 1 - Cave <方向组合>"；每个组合有 1/2 两个变体，随机取）
_CONNS = [
    ('conn_n', 'caveN', N), ('conn_s', 'caveS', S), ('conn_e', 'caveE', E), ('conn_w', 'caveW', W),
    ('conn_ns', 'caveNS', N | S), ('conn_ew', 'caveEW', E | W),
    ('conn_ne', 'caveNE', N | E), ('conn_nw', 'caveNW', N | W),
    ('conn_se', 'caveSE', S | E), ('conn_sw', 'caveSW', S | W),
    ('conn_nse', 'caveNSE', N | S | E), ('conn_nsw', 'caveNSW', N | S | W),
    ('conn_new', 'caveNEW', N | E | W), ('conn_sew', 'caveSEW', S | E | W),
    ('conn_nsew', 'caveNSEW', N | S | E | W),
]
for base, fname, mask in _CONNS:
    PIECES.append(_p(base, CAV_DIR, fname + '.ds1', 24, 24, G_CAVE_CONN, mask,
                     note='Cave %s（变体 1）' % fname))
    if os.path.exists(os.path.join(RAW, CAV_DIR.replace('/', os.sep), fname + '2.ds1')):
        PIECES.append(_p(base + 'b', CAV_DIR, fname + '2.ds1', 24, 24, G_CAVE_CONN, mask,
                         note='Cave %s（变体 2）' % fname))
# 洞穴入口（LvlPrest "Act 1 - DOE Entrance"，SizeX=SizeY=8 ⇒ ds1 9×9）
PIECES.append(_p('denent', CAV_DIR, 'denent.ds1', 8, 8, G_CAVE_ENT, 0, note='Act 1 - DOE Entrance'))
PIECES.append(_p('denent2', CAV_DIR, 'denent2.ds1', 8, 8, G_CAVE_ENT, 0, note='Act 1 - DOE Entrance'))


def _palette():
    if _palette.pal is None:
        try:
            from . import pl2 as pl2mod
        except ImportError:
            import pl2 as pl2mod
        _palette.pal = pl2mod.load_pl2(os.path.join(RAW, 'data', 'global', 'palette', 'ACT1', 'Pal.PL2'))
    return _palette.pal


_palette.pal = None


def _tile_cache(raw_root):
    """建「compositeIndex → 最好的那张瓦片」表（口径同 `export_town_layout.py`）。

    同一个 compositeIndex 有多个 rarity 变体，其中夹着 0×0 占位 ⇒ 取不透明像素最多的那张。
    """
    if _tile_cache.cache is not None:
        return _tile_cache.cache
    pack_of = {}
    for rel_dt1, pack, _note in exp.PACKS:
        pack_of[rel_dt1.lower()] = pack
    packs = sorted(set(pack_of.values()))
    pack_id = dict((p, i) for i, p in enumerate(packs))

    best = {}
    providers = {}
    scanned = set()
    for rel_dt1, pack, _note in exp.PACKS:
        fp = os.path.join(raw_root, rel_dt1.replace('/', os.sep))
        if not os.path.exists(fp):
            print('  [WARN] dt1 不存在：%s' % fp)
            continue
        try:
            d = dt1mod.load_dt1(fp)
        except ValueError as exc:
            print('  [WARN] 跳过 %s' % exc)
            continue
        short = os.path.basename(fp).lower()
        scanned.add(short)
        with open(fp, 'rb') as fh:
            data = fh.read()
        for tile in d.tiles:
            providers.setdefault(tile.composite_index, set()).add(short)
            if tile.width <= 0 or tile.pixel_height <= 0:
                continue
            img = dt1mod.render_tile(tile, data=data)
            area = sum(img.mask)
            if area == 0:
                continue
            prev = best.get(tile.composite_index)
            if prev is not None and prev[2] >= area:
                continue
            best[tile.composite_index] = (pack, tile.array_index, area, tile)
    _tile_cache.cache = (best, providers, packs, pack_id)
    print('  瓦片库：扫了 %d 个 dt1，compositeIndex %d 个' % (len(scanned), len(best)))
    return _tile_cache.cache


_tile_cache.cache = None


def build(out_path, debug):
    best, providers, packs, pack_id = _tile_cache(RAW)
    warp_pack = 'warp'

    def key_of(composite):
        hit = best.get(composite)
        if hit is None:
            return ''
        return '%03d%03d' % (pack_id[hit[0]], hit[1])

    def is_warp(composite):
        hit = best.get(composite)
        return hit is not None and hit[0] == warp_pack

    def walk_flag_blocks(tile):
        """DT1 的**阻挡标志**：`flags[12] & (Walk|PlayerWalk)` 置位 ⇒ 该格阻挡。

        依据三条（互相独立，见 `MapGenPieces.cs` 文件头）：
          ① 参考实现 `Diablerie/.../Engine/World/WorldGrid.cs:78-84`：
             `passable = (flags[flagIndex] & (Walk|PlayerWalk)) == 0`（置位 = 阻挡）；
          ② 自洽：`CAVES/cave{方向}.ds1` 的"连通体碰哪几条边"在该口径下与**文件名声明的方向**
             逐一吻合（`caveE`→E、`caveEW`→E/W、`caveNS`→N/S、`caveNSEW`→四条边…）；
             反过来的口径则每块都碰四条边（无信息）；
          ③ 该"阻挡集"正是**画了岩壁立面的那圈格**（洞壁脚下），而平坦洞底瓦片不带标志。
        """
        if tile is None or len(tile.flags) <= 12:
            return False
        return bool(tile.flags[12] & (1 | 8))

    def kind_of_wall(composite):
        names = providers.get(composite, set())
        # 崖壁 / 树 / 栅栏 / 石墙 / 物件 / 水 / 洞壁 —— 全部阻挡
        for key in ('cliff1.dt1', 'cliff2.dt1', 'border.dt1', 'corner.dt1', 'treegroups.dt1',
                    'trees.dt1', 'fence.dt1', 'stonewall.dt1', 'objects.dt1', 'stones.dt1',
                    'river.dt1', 'ruin.dt1', 'tower.dt1', 'towerb.dt1', 'cairn.dt1',
                    'cottages.dt1', 'fallen.dt1', 'bridge.dt1', 'cave.dt1', 'cavedr.dt1'):
            if key in names:
                return '#'
        if 'warp.dt1' in names:
            return '.'          # 原版传送标记：可走，但不画（见文件头）
        return '#'

    def is_cave_rel(rel):
        return '/CAVES/' in rel

    records = []
    total_cells = 0
    for spec in PIECES:
        src = os.path.join(RAW, spec['rel'].replace('/', os.sep))
        if not os.path.exists(src):
            print('  [WARN] 跳过（源文件不存在）：%s' % src)
            continue
        try:
            d = ds1mod.load_ds1(src)
        except Exception as exc:                      # noqa: BLE001 —— 非预期，必须留痕
            print('  [WARN] 跳过 %s：%s' % (spec['rel'], exc))
            continue

        w, h = d.width, d.height
        floor = d.floors[0] if d.floors else None
        wall = d.walls[0] if d.walls else None

        kinds = []
        ground = []
        objects = []
        n_block = n_floor = n_obj = n_warp = 0
        cave_mode = is_cave_rel(spec['rel'])
        for y in range(h):
            krow = []
            grow = []
            orow = []
            for x in range(w):
                fc = floor[y * w + x] if floor is not None else None
                wc = wall[y * w + x] if wall is not None else None

                has_floor = fc is not None and not fc.is_empty
                has_wall = wc is not None and not wc.is_empty

                gk = key_of(fc.tile_index) if has_floor else ''
                ok = ''
                if has_wall:
                    if is_warp(wc.tile_index):
                        n_warp += 1
                    else:
                        ok = key_of(wc.tile_index)

                # 可走性：**野外/城镇**按「有地面即可走，wall 层物件阻挡」；
                #          **洞穴**额外按 DT1 阻挡标志（无地面的格 = 实心岩体）。
                if not has_floor:
                    kind = '#'                       # 没地面 = 实心（洞穴岩体 / 图外）
                elif has_wall:
                    kind = kind_of_wall(wc.tile_index)
                elif cave_mode and _blocks(best.get(fc.tile_index)):
                    kind = '#'
                else:
                    kind = '.'
                if kind == '#':
                    n_block += 1
                else:
                    n_floor += 1
                if ok:
                    n_obj += 1

                krow.append(kind)
                grow.append(gk if gk else EMPTY)
                orow.append(ok if ok else EMPTY)
            kinds.append(''.join(krow))
            ground.append(''.join(grow))
            objects.append(''.join(orow))

        total_cells += w * h
        records.append(dict(spec, w=w, h=h, kinds=kinds, ground=ground, objects=objects))
        print('  %-9s %-26s %2dx%-3d pitch=%dx%-3d 阻挡 %4d / 地面 %4d / 物件 %4d / warp 标记 %d'
              % (spec['name'], os.path.basename(spec['rel']), w, h, spec['px'], spec['py'],
                 n_block, n_floor, n_obj, n_warp))

    _write_cs(out_path, records, packs)
    print('  合计 %d 块，%d 格 → %s' % (len(records), total_cells, out_path))
    return 0


HEADER = '''// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · Module/Map/MapGenPieceData.cs
// ⚠️ **本文件是生成物，禁止手改**（生成器：`tools/d2codec/export_pieces.py`）。
//
// 数据来源 = **原版** `data/global/tiles/ACT1/{OUTDOORS,CAVES}/*.ds1`
//   （Blizzard North, 2000，取自 d2data.mpq；本项目非商用）。
//   挑哪些块 / 拼接格距 = **原版自己的生成规则表**（`d2lod1.10txt/data/global/excel/`）：
//     · 野外   ：Levels.txt「Act 1 - Wilderness 1」DrlgType=3 Size 80x80 SubType=6
//                + LvlPrest「Act 1 - Wild Border 1..12」/「Fence Fill 1..6」/「Tree Fill」
//                + LvlSub Type=6（散落件）
//     · 邪恶洞穴：Levels.txt「Act 1 - Cave 1」DrlgType=1
//                + LvlMaze「Act 1 - Cave 1」SizeX=SizeY=24（⇒ 相邻块共享 1 格，pitch=24）
//                + LvlPrest「Act 1 - Cave <方向组合>」/「Cave Treasure 1..5」
//
// 每块三张表（逐格 1:1，不缩不放）：
//   Kinds[y][x]    '.' = 可走地面 / '#' = 阻挡（崖壁·树·石墙·岩石·洞壁）
//   Ground[y][x]   每格 floor 层瓦片键（6 字符：3 位 packId + 3 位瓦片序号；`------` = 无）
//   Objects[y][x]  每格 wall 层瓦片键（同上；`------` = 无）
//   packId → `Packs[packId]`，瓦片文件 = `Resources/Clover/D2/{Tiles,Objects}/<pack>/<idx>.png`
// ─────────────────────────────────────────────────────────────────────────────
'''


def _rows(table):
    return ['            "%s",' % row for row in table]


def _write_cs(out_path, records, packs):
    lines = [HEADER, 'namespace Diablo2.Module.Map', '{',
             '    /// <summary>原版野外 / 洞穴预设块数据（生成物，见文件头）。</summary>',
             '    internal static class MapGenPieceData', '    {',
             '        /// <summary>pack 目录名（下标 = 各表里的 3 位 packId）。</summary>',
             '        public static readonly string[] Packs =', '        {']
    for p in packs:
        lines.append('            "%s",' % p)
    lines += ['        };', '',
              '        /// <summary>全部预设块（顺序 = 生成器里的数组下标）。</summary>',
              '        internal static readonly Piece[] Pieces =', '        {']

    for r in records:
        lines.append('            new Piece(')
        lines.append('                "%s", "%s", %d, %d, %d, %d,' % (
            r['name'], r['rel'].replace('data/global/tiles/ACT1/', 'Act1/'),
            r['px'], r['py'], r['group'], r['mask']))
        for label, table in (('kinds', r['kinds']), ('ground', r['ground']), ('objects', r['objects'])):
            lines.append('                // %s' % label)
            lines.append('                new string[]')
            lines.append('                {')
            lines += ['    ' + s for s in _rows(table)]
            lines.append('                },')
        lines.append('                "%s"),' % r['note'].replace('"', "'"))
    lines += ['        };', '    }', '}', '']

    with open(out_path, 'w', encoding='utf-8', newline='\r\n') as fh:
        fh.write('\n'.join(lines))


def main(argv):
    out = argv[argv.index('--out') + 1] if '--out' in argv else DEFAULT_OUT
    print('从原版 ds1 预设抽地块表：%d 块（野外 %d / 洞穴 %d）' % (
        len(PIECES),
        sum(1 for p in PIECES if p['group'] <= G_SCATTER),
        sum(1 for p in PIECES if p['group'] >= G_CAVE_ROOM)))
    return build(out, '--debug' in argv)


if __name__ == '__main__':
    sys.exit(main(sys.argv))
