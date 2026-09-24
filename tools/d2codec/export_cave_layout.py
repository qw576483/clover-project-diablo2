# -*- coding: utf-8 -*-
"""把**原版 ACT1 洞穴预设**（`CAVES/*.ds1`，每块 25×25）抽成 C# 表
`client/Assets/Scripts/Module/Map/MapGenCaveLayout.cs`（生成物，禁止手改）。

为什么这么做（**这是原版的做法，不是我们自创的**）：
    原版 D2 的洞穴层（邪恶洞穴 / 洞穴 1-2 层 / 洞窟 …）**不是**由一张整图 ds1 铺出来的，
    而是由引擎的 DRLG 把一叠手工做的 **25×25 预设块**拼出来的：
      · 块名 = `caveNSEW` / `caveNS` / `caveNE` / `caveN` / `caveE` / `caveroom1..6` …
        （`cave` + 该块**开通的方向**首字母；`caveroom*` = 尽头房间），
      · `caveNSW2` / `caveNSEtheme1` 这种后缀 = **同连接形状的随机变体**（原版按 variant 抽），
      · 每块 25×25 格，块与块之间在**同一相对坐标**上对齐 ⇒ 走廊天然接通。
    ⇒ 本项目要把"地窟"做成原版那样，唯一正确的做法就是**用原版这套块去拼**，
      而不是自己写一个"矩形房间 + L 型走廊"的生成器（那是自创，不像原版）。

逐格可走性怎么来的（**不是猜的**）：
    `CAVES/cave.dt1` 每个瓦片的 tile 头里有 **25 个 subtile flag**（5×5 子块，`DT1.cs:20 Walk=1`）。
    实测三档分得很干净：
      · 纯走廊地砖 → 25/25 全可走（如 `cave.dt1` #70）
      · 纯岩体地砖 → 0/25 全不可走（如 `cave.dt1` #153）
      · 过渡砖（走廊与岩体交界）→ 15/25（如 `cave.dt1` #192）
    ⇒ 格级判定取 **"该瓦片至少 1 个 subtile 可走"**，与原版"敌我能不能踩过去"一致。
    另有 226 格的 floor 引用 **解析不到任何 dt1 的 compositeIndex（61440）** —— 那正是原版
    洞穴里**什么都不画的实心岩体**（黑色区域），本项目按"实心岩体 + 不铺地砖"处理。

开口签名：每块在四边各有 2 条"通道车道"：
    北/南 = x ∈ {8,9}（lane0）与 x = {16}（lane1）；西/东 = y ∈ {10,11}（lane0）与 y = {18}（lane1）。
    本生成器只收「每条边要么不通、要么两条车道都通」的块（`dirMask` 4 位：N=1 S=2 W=4 E=8）
    ⇒ 拼接时不会出现"半条走廊撞墙"的破口；形状不规整的块（如 `caveroom1/3/4`）只作候选剔除，
    在产物里留痕（`Skipped` 常量注释）。

用法：
    python export_cave_layout.py [--out <MapGenCaveLayout.cs>] [--debug]
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
CAVES_REL = 'data/global/tiles/ACT1/CAVES'
DEFAULT_OUT = os.path.join(_REPO, 'client', 'Assets', 'Scripts', 'Module', 'Map',
                           'MapGenCaveLayout.cs')

PIECE_SIZE = 25
EMPTY = '------'

# ── 开口车道（相对块左上角）────────────────────────────────────────────────
# 本轮**重新实测**（口径改了：可走 = **中心 subtile 不带阻挡标志**，见 `cell_passable`）：
#   逐块统计四边「边界可走格集合」，95 块在每条边上**只有两种取值**：
#     · 关闭：只有角格（如 N 边 = `(0,)`）
#     · 打开：角格 + 一段**连续 7 格**（N/S = x∈[9,15]；W/E = y∈[11,17]）
LANE = {
    'N': [(x, 0) for x in range(9, 16)],
    'S': [(x, 24) for x in range(9, 16)],
    'W': [(0, y) for y in range(11, 18)],
    'E': [(24, y) for y in range(11, 18)],
}

DIR_BIT = {'N': 1, 'S': 2, 'W': 4, 'E': 8}


def _sym(i):
    """文档符号：`Alphabet` 下标 → **两个字符**（base = 符号表长度）。

    为什么两个字符：实测单个洞穴块的 tile 组合最多 **93 种**（`caveE.ds1`），
    超过可打印 ASCII 去掉 `"` / `\\` 后的 92 个 ⇒ 一位不够，固定用两位，
    C# 侧按下标 2*i / 2*i+1 取。
    """
    return SYMBOLS[i // len(SYMBOLS)] + SYMBOLS[i % len(SYMBOLS)]

# 供 `MapGenCaveLayout` 用的 alphabet 字符表（下标 → 字符）。
# 取全部可打印 ASCII，去掉 `"` 与 `\`（C# 字符串字面量里要转义），共 92 个符号。
SYMBOLS = ''.join(chr(c) for c in range(33, 127) if chr(c) not in '"\\')


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


def _load_tiles(ds1, pack_of, debug):
    """把该 ds1 依赖的 dt1 读进来：compositeIndex → (pack, arrayIndex, tile)。

    同一 compositeIndex 会有多个 rarity 变体（含 0×0 占位）⇒ 取「不透明像素最多」的那个。
    """
    best = {}
    for rel in ds1.dt1_files:
        rel_l = rel.replace('\\', '/').lower()
        fp = os.path.join(RAW, rel.replace('\\', os.sep))
        if not os.path.exists(fp):
            print('  [WARN] 依赖 dt1 不存在：%s' % fp)
            continue
        pack = pack_of.get(rel_l)
        if pack is None:
            print('  [WARN] dt1 不在 export_tiles.PACKS（不导出瓦片）：%s' % rel)
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
            prev = best.get(tile.composite_index)
            if prev is not None and prev[3] >= area:
                continue
            best[tile.composite_index] = (pack, tile.array_index, tile, area)
    return best


N4 = ((1, 0), (-1, 0), (0, 1), (0, -1))


def cell_passable(tile):
    """**格级可走** = 该瓦片 **5×5 subtile 的中心那一个** 不带阻挡标志。

    口径出处（**不是猜的**）：
      · `原版资源/参考工程_Diablerie/.../Engine/World/WorldGrid.cs:78-84`
        `ApplyTileCollisions`：`flagIndex` 从 0 递增遍历 **25 个 subtile**，
        `passable = (tile.flags[flagIndex] & (Walk|PlayerWalk)) == 0`
        ⇒ **标志置位 = 该 subtile 不可走**（`Walk=1` / `PlayerWalk=8`）。
      · 自洽检验（⛔ 原一次性脚本 `.ai-tmp/test/flagtest.py` **已删**、无在盘同物；**在盘替代** = `tools/d2codec/verify_walk_flags.py` 判据①「开口方向自洽」，
        **现状 = 需原版包，本机 `BLOCKED（缺 原版资源/d2dc6 + 原版资源/d2raw）`** ⇒ 本机跑不了）：
        对 `CAVES/cave{方向}.ds1` 15 个块，用**中心 subtile** 判定算出的可走掩码碰到的边与**文件名声明
        的开口方向**逐一吻合（`caveNS`→N/S、`caveNSEW`→四条边…）；若改成「任一 subtile 可走」（旧口径）则 `caveN` 会多出 W 边、`caveSE` 多出 N 边（错）。
      · 为什么取"中心"而不是"全部 25 个"：本项目一格 = 一个 ds1 格 = 一个寻路节点，
        节点代表的是**角色站在格子中心**那个位置 ⇒ 中心 subtile 决定该节点能不能站。
    """
    return not (tile.flags[12] & 9)


def _components(walk):
    """4 邻连通片列表，每片是一组 (x,y)（按大小降序）。"""
    n = PIECE_SIZE
    seen = [[False] * n for _ in range(n)]
    out = []
    for y in range(n):
        for x in range(n):
            if not walk[y][x] or seen[y][x]:
                continue
            comp = set()
            stack = [(x, y)]
            seen[y][x] = True
            while stack:
                cx, cy = stack.pop()
                comp.add((cx, cy))
                for dx, dy in N4:
                    nx, ny = cx + dx, cy + dy
                    if 0 <= nx < n and 0 <= ny < n and walk[ny][nx] and not seen[ny][nx]:
                        seen[ny][nx] = True
                        stack.append((nx, ny))
            out.append(comp)
    out.sort(key=len, reverse=True)
    return out


def build(out_path, debug):
    src_dir = os.path.join(RAW, CAVES_REL.replace('/', os.sep))
    pack_of = {}
    for rel_dt1, pack, _note in exp.PACKS:
        pack_of[rel_dt1.lower()] = pack
    # 洞穴块只允许用洞穴自己的两张 dt1（cave / cavedr）——
    # `warp.dt1` 是编辑器留下的孤儿传送标记（与罗格营地同一坑，见 export_town_layout.py），
    # 出现在依赖里就整块剔除，避免把纯色菱形当岩壁画出来。
    allowed_packs = ('cave', 'cave_door')
    packs = [p for p in sorted(set(pack_of.values())) if p in allowed_packs]
    pack_id = dict((p, i) for i, p in enumerate(packs))

    names = sorted(f for f in os.listdir(src_dir) if f.endswith('.ds1'))
    pieces = []
    skipped = []

    for name in names:
        ds1 = ds1mod.load_ds1(os.path.join(src_dir, name))
        if ds1.width != PIECE_SIZE or ds1.height != PIECE_SIZE:
            skipped.append((name, '尺寸 %dx%d ≠ %dx%d' % (ds1.width, ds1.height,
                                                        PIECE_SIZE, PIECE_SIZE)))
            continue
        deps = [r.replace('\\', '/').lower() for r in ds1.dt1_files]
        if any('warp.dt1' in d for d in deps):
            skipped.append((name, '依赖 warp.dt1（编辑器孤儿标记，不画）'))
            continue

        tiles = _load_tiles(ds1, pack_of, debug)

        def key_of(cell):
            hit = tiles.get(cell.tile_index)
            if hit is None:
                return ''
            return '%03d%03d' % (pack_id[hit[0]], hit[1])

        # ── ① 逐格原始信息（kind / ground / object / 可走）────────────────
        walk = [[False] * PIECE_SIZE for _ in range(PIECE_SIZE)]
        kind = [[' '] * PIECE_SIZE for _ in range(PIECE_SIZE)]
        ground = [[''] * PIECE_SIZE for _ in range(PIECE_SIZE)]
        obj = [[''] * PIECE_SIZE for _ in range(PIECE_SIZE)]
        for y in range(PIECE_SIZE):
            for x in range(PIECE_SIZE):
                f = ds1.floor_at(x, y)
                w = ds1.wall_at(0, x, y)
                ft = tiles.get(f.tile_index) if not f.is_empty else None
                # 格级可走 = 该 floor 瓦片**中心 subtile** 不带阻挡标志（口径见 `cell_passable`）
                walk[y][x] = bool(ft and cell_passable(ft[2]))
                kind[y][x] = ' ' if f.is_empty else ('.' if walk[y][x] else 'X')
                ground[y][x] = key_of(f) if not f.is_empty else ''
                obj[y][x] = key_of(w) if (w is not None and not w.is_empty) else ''

        # ── ② 开口签名（在**原始**可走掩码上算）：某边"通" ⇔ 该边那 7 格车道全可走 ──
        dir_mask = 0
        for d in ('N', 'S', 'W', 'E'):
            if all(walk[y][x] for (x, y) in LANE[d]):
                dir_mask |= DIR_BIT[d]

        # ── ③ 块内连通性：只保留「含全部开口的那一片」——
        #      **不凿洞、不改任何瓦片**（下一版之前这里会用 `_repair` 沿 L 形凿出新地砖，
        #      现在的做法是把不含开口的可走碎块降级为 `X`（= 实心岩体）：**渲染一个像素都不动**
        #      （`Ground`/`Object` 瓦片键照原样保留，只有可走性变），原版那几格本来就走不过去。
        comps = _components(walk)
        open_lane_cells = []
        for d in ('N', 'S', 'W', 'E'):
            if dir_mask & DIR_BIT[d]:
                open_lane_cells += LANE[d]

        keep = None
        for comp in comps:
            if all(p in comp for p in open_lane_cells):
                keep = comp
                break
        if keep is None:
            skipped.append((name, '开口格落在 %d 个不同连通片里 ⇒ 块内接不通，剔除' % len(comps)))
            continue

        for y in range(PIECE_SIZE):
            for x in range(PIECE_SIZE):
                if walk[y][x] and (x, y) not in keep:
                    walk[y][x] = False
                    if kind[y][x] == '.':
                        kind[y][x] = 'X'
        total = len(keep)
        if total == 0:
            skipped.append((name, '没有任何可走格'))
            continue

        # ── ④ 编码 ────────────────────────────────────────────────────────
        alphabet = OrderedDict()
        cells = []
        for y in range(PIECE_SIZE):
            for x in range(PIECE_SIZE):
                key = (kind[y][x], ground[y][x] or EMPTY, obj[y][x] or EMPTY)
                if key not in alphabet:
                    alphabet[key] = len(alphabet)
                cells.append(_sym(alphabet[key]))

        pieces.append({
            'name': os.path.splitext(name)[0],
            'dirMask': dir_mask,
            'cells': ''.join(cells),
            'alphabet': [('%s%s%s' % (k, g, o)) for (k, g, o) in alphabet.keys()],
            'walkable': total,
        })

    # ── 按 (dirMask, 变体序号) 排序输出 ─────────────────────────────────────
    pieces.sort(key=lambda p: (p['dirMask'], p['name']))
    by_mask = {}
    for p in pieces:
        by_mask.setdefault(p['dirMask'], []).append(p['name'])

    print('洞穴预设：%d 个 → 收 %d 个可拼接块，剔除 %d 个' %
          (len(names), len(pieces), len(skipped)))
    for mask, mem in sorted(by_mask.items()):
        dirs = ''.join(d for d in 'NSWE' if mask & DIR_BIT[d]) or '(全封闭)'
        print('  dirMask=%3d(%-4s) %2d 个: %s' % (mask, dirs, len(mem), ', '.join(mem)))
    print('  每块可走格数：%s' %
          ', '.join('%s=%d' % (p['name'], p['walkable']) for p in pieces))
    for n, r in skipped:
        print('  [剔除] %-22s %s' % (n, r))

    _write_cs(out_path, pieces, packs, skipped, by_mask)
    print('  → %s' % out_path)
    return 0


HEADER = '''// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · Module/Map/MapGenCaveLayout.cs
// ⚠️ **本文件是生成物，禁止手改**（生成器：`tools/d2codec/export_cave_layout.py`）。
//
// 数据来源 = **原版** `data/global/tiles/ACT1/CAVES/*.ds1`（每块 25×25 格）
//   （Blizzard North, 2000，取自 d2data.mpq；本项目非商用）。
//
// 为什么用它们（原版就是这么做的，不是自创）：
//   原版 D2 的洞穴层由引擎 DRLG 把一叠手工 **25×25 预设块**拼出来：
//     块名 = `cave` + 该块**开通方向**的首字母（`caveNSEW` = 四通；`caveNS` = 南北通；
//     `caveN` = 北向尽头房间…），后缀 `2` / `theme1` / `spec` … = **同连接形状的随机变体**。
//   块与块在**同一相对坐标**上对齐 ⇒ 走廊天然接通。本项目的地窟 = 用这套块拼出来的迷宫，
//   所以拓扑与瓦片都是原版的。
//
// 逐格可走性（不是猜的）：`cave.dt1` 每个 tile 头有 25 个 subtile flag（5×5 子块）。
//   口径 = **中心那个 subtile**（index 12）不带阻挡标志（`Walk=1` / `PlayerWalk=8`）；
//   依据 `原版资源/参考工程_Diablerie/.../Engine/World/WorldGrid.cs:78-84`
//   `ApplyTileCollisions`：`passable = (flags[flagIndex] & (Walk|PlayerWalk)) == 0`
//   （**标志置位 = 不可走**），并用 `cave{方向}.ds1` 15 个块的**文件名声明的开口方向**做自洽检验。
//   另有约 226 格的 floor 引用**解析不到任何 dt1 的 compositeIndex** —— 那正是原版洞穴里
//   **什么都不画的实心岩体**（黑区），本项目按"实心岩体 + 不铺地砖"处理（见 cells 里的 'X'/' '）。
//
// 三张数据：
//   Pieces[i].DirMask   四边"通/不通"位（N=1 S=2 W=4 E=8；只收"要么不通、要么两条车道都通"的块）
//   Pieces[i].Alphabet  该块用到的 `<kind><ground6><object6>` 组合表
//                       （kind：' ' 无 floor 格 / '.' 可走地面 / 'X' 实心岩体；
//                        ground6/object6 = `<packId:3><tileIdx:3>`，`------` = 该层没瓦片）
//   Pieces[i].Cells     25×25 格，**每格 2 个字符** = `SymTable` 里的下标（行优先，行 0 = 块最北一行）
//   `Packs[packId]` → `Resources/Clover/D2/{Tiles,Objects}/<pack>/<idx>.png`
//   `SymTable`          下标 → 字符的符号表（base = 表长，92）
//
// ⛔ **本产物与原版 ds1 逐格逐瓦片相同**：生成器**不凿洞、不改任何地面/物件瓦片**。
//   块内可走格若因"逐格抽象"断成几片，只保留**含全部开口**的那一片，
//   其余可走格降级为 `X`（渲染键照旧 ⇒ 画面上一个像素都不动，只是走不过去）。
//   每块可走格数：%s
//
// 剔除记录（形状不规整 / 开口跨片 / 依赖 `warp.dt1` 的块，共 %d 个）：
%s
// ─────────────────────────────────────────────────────────────────────────────
'''


def _write_cs(out_path, pieces, packs, skipped, by_mask):
    skip_lines = []
    for n, r in skipped:
        skip_lines.append('//   · %-22s %s' % (n, r))
    walk_lines = []
    for p in pieces:
        walk_lines.append('//   · %-22s %d 格' % (p['name'], p['walkable']))
    header = HEADER % ('\n'.join(walk_lines) if walk_lines else '//   （无）',
                       len(skipped), '\n'.join(skip_lines) if skip_lines else '//   （无）')

    lines = [header, 'using UnityEngine;', '', 'namespace Diablo2.Module.Map', '{',
             '    /// <summary>原版 ACT1 洞穴预设块库（生成物，见文件头）。</summary>',
             '    internal static class MapGenCaveLayout', '    {',
             '        /// <summary>每块的边长（格）。</summary>',
             '        public const int PieceSize = %d;' % PIECE_SIZE, '',
             '        /// <summary>pieces 目录名（下标 = `Alphabet` 里的 3 位 packId）。</summary>',
             '        public static readonly string[] Packs =', '        {']
    for p in packs:
        lines.append('            "%s",' % p)
    lines += ['        };', '',
              '        /// <summary>文档符号表（`Cells` 里每 2 个字符 = 一个下标，base = 本表长度）。</summary>',
              '        public const string SymTable = "%s";' % SYMBOLS, '',
              '        /// <summary>一块原版洞穴预设。</summary>',
              '        internal sealed class Piece',
              '        {',
              '            /// <summary>块名（= 原版 ds1 文件名，去掉扩展名）。</summary>',
              '            public readonly string Name;',
              '',
              '            /// <summary>四边通不通（N=1 S=2 W=4 E=8）。</summary>',
              '            public readonly int DirMask;',
              '',
              '            /// <summary>25×25 格，每格 **2 个字符** = <see cref="Alphabet"/> 下标。</summary>',
              '            public readonly string Cells;',
              '',
              '            /// <summary>`<kind><ground6><object6>` 组合表。</summary>',
              '            public readonly string[] Alphabet;',
              '',
              '            public Piece(string name, int dirMask, string cells, string[] alphabet)',
              '            {',
              '                Name = name;',
              '                DirMask = dirMask;',
              '                Cells = cells;',
              '                Alphabet = alphabet;',
              '            }',
              '',
              '            /// <summary>该方向是否开通。</summary>',
              '            public bool Opens(int dirBit) { return (DirMask & dirBit) != 0; }',
              '        }', '',
              '        /// <summary>方向位。</summary>',
              '        public const int DirN = 1, DirS = 2, DirW = 4, DirE = 8;', '',
              '        /// <summary>全部可拼接块（按 DirectedMask 升序）。</summary>',
              '        public static readonly Piece[] Pieces =', '        {']
    for p in pieces:
        lines.append('            new Piece("%s", %d,' % (p['name'], p['dirMask']))
        lines.append('                "%s",' % p['cells'])
        lines.append('                new[]')
        lines.append('                {')
        for a in p['alphabet']:
            lines.append('                    "%s",' % a)
        lines += ['                }),']
    lines += ['        };', '',
              '        /// <summary>`Cells` 里第 <paramref name="cell"/> 格（0 起，行优先）的 `Alphabet` 下标。</summary>',
              '        public static int CellIndex(string cells, int cell)',
              '        {',
              '            var p = cell * 2;',
              '            var hi = SymTable.IndexOf(cells[p]);',
              '            var lo = SymTable.IndexOf(cells[p + 1]);',
              '            return hi * SymTable.Length + lo;',
              '        }', '',
              '        /// <summary>解一个 `<packId:3><tileIdx:3>` 编码（`------` → 空串）。</summary>',
              '        public static string Decode(string code)',
              '        {',
              '            if (string.IsNullOrEmpty(code) || code[0] == \'-\') return "";',
              '            var packId = (code[0] - \'0\') * 100 + (code[1] - \'0\') * 10 + (code[2] - \'0\');',
              '            if (packId < 0 || packId >= Packs.Length) return "";',
              '            return Packs[packId] + "/" + code.Substring(3, 3);',
              '        }',
              '    }', '}', '']

    with open(out_path, 'w', encoding='utf-8', newline='\r\n') as fh:
        fh.write('\n'.join(lines))


def main(argv):
    out = argv[argv.index('--out') + 1] if '--out' in argv else DEFAULT_OUT
    print('从原版 CAVES/*.ds1 抽取可拼接洞穴块：%s' % CAVES_REL)
    return build(out, '--debug' in argv)


if __name__ == '__main__':
    sys.exit(main(sys.argv))
