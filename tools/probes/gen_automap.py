# -*- coding: utf-8 -*-
"""自动地图（automap）生成器：原版 `AutoMap.txt` + `MaxiMap.dc6` + ACT1 调色板 ⇒ 逐格 Cel 表。

**只读**输入（⛔ 不改任何原版资源、不写回 `tools/d2codec`）：
  · `原版资源/d2raw/data/global/excel/AutoMap.txt`     逐格 Cel 权威表（2603 行）
  · `原版资源/d2dc6/data/global/ui/AUTOMAP/MaxiMap.dc6` 1260 帧 × 16×32 图块表
  · `原版资源/d2raw/data/global/palette/ACT1/Pal.PL2`   调色板（原版 ACT1）
  · `原版资源/d2raw/data/global/tiles/ACT1/**/*.ds1`    关卡布局（Style/Sequence 的载体）
  · `client/Assets/Resources/Clover/D2/{Tiles,Objects}/*/manifest.json`
        已解出瓦片的 `compositeIndex (= ((main<<6)+sub)<<5+orientation)` → `<pack>/<idx>`

**口径（逐条有出处）**
  1. `CelN` = `MaxiMap.dc6` 的**帧序号**（Cel 值域 −1..1254 ⊂ 0..1259）。
  2. 查询键 = `(LevelName, Style, Sequence)`；`LevelName` = `<act 号> <LevelType 名>`，
     `Style` = **DS1 格 `prop3 & 0x0F`**、`Sequence` = **DS1 格 `prop2`**（实测：`1 Wilderness`
     全 9 块 floor/wall 命中 100%、`1 Town` wall 100%）。
  3. 命中多行时（`TileName` 的选取规则在原版里没有载体，见 plan §2）取**文件里最先出现的**那一行；
     该行 `Cel1..Cel4` 取**第一个 ≥0 的**。
  4. 无命中行 ⇒ `-1`（该格原版 automap 不画：城镇素底地面就是这一类）。
  5. 同一 `<pack>/<idx>`（= 同一张原版瓦片）在一个关卡里出现多种 (Style,Sequence) 时
     **取出现次数最多的 Cel**（打平取 Cel 号小者），并逐键报告分歧数。

**产物**
  · `client/Assets/Scripts/Module/Map/AutoMapCel.generated.cs`（逐格 Cel 表 + cel 索引数据 + 调色板）
  · `.ai-tmp/test/automap-cel-report.json`（统计与分歧明细）
  · `.ai-tmp/test/automap_town_render.png`（离线预览：城镇 automap 全图，**用于肉眼对账**）

用法：`python tools/probes/gen_automap.py [--render-only]`
"""

import base64
import collections
import glob
import json
import os
import re
import struct
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
sys.path.insert(0, os.path.join(ROOT, 'tools'))

from d2codec import dc6 as dc6mod                      # noqa: E402
from d2codec import ds1 as ds1mod                      # noqa: E402

ORIG = os.path.join(ROOT, u'原版资源')
AUTOMAP_TXT = os.path.join(ORIG, 'd2raw', 'data', 'global', 'excel', 'AutoMap.txt')
MAXIMAP_DC6 = os.path.join(ORIG, 'd2dc6', 'data', 'global', 'ui', 'AUTOMAP', 'MaxiMap.dc6')
ACT1_PL2 = os.path.join(ORIG, 'd2raw', 'data', 'global', 'palette', 'ACT1', 'Pal.PL2')
TILES_ROOT = os.path.join(ORIG, 'd2raw', 'data', 'global', 'tiles', 'ACT1')
RES_D2 = os.path.join(ROOT, 'client', 'Assets', 'Resources', 'Clover', 'D2')
OUT_CS = os.path.join(ROOT, 'client', 'Assets', 'Scripts', 'Core', 'AutoMapCel.generated.cs')
# `--out-cs <path>` 覆盖生成物落点（默认 None = 落 OUT_CS）。见 emit_cs 的 docstring。
OUT_CS_OVERRIDE = None
OUT_JSON = os.path.join(ROOT, '.ai-tmp', 'test', 'automap-cel-report.json')
OUT_PNG = os.path.join(ROOT, '.ai-tmp', 'test', 'automap_town_render.png')
TOWN_LAYOUT_CS = os.path.join(ROOT, 'client', 'Assets', 'Scripts', 'Module', 'Map', 'MapGenTownLayout.cs')

CEL_W, CEL_H = 16, 32          # MaxiMap.dc6 实测：每帧 16×32（1260 帧）
SCALE_NUM, SCALE_DEN = 1, 10   # 世界地砖 160×80（dt1.py:FLOOR_TILE_PX_*）⇒ automap 1/10

FLOOR_NAMES = frozenset(('fl', 'co'))

# 关卡 → (AutoMap LevelName, 该关卡的 DS1 清单)
LEVELS = collections.OrderedDict()
LEVELS['Town'] = dict(
    act=1, level_type='Town', area_id=0,
    ds1=[os.path.join(TILES_ROOT, 'TOWN', n) for n in
         ('TownN1.ds1', 'TownE1.ds1', 'TownS1.ds1', 'TownW1.ds1')])
LEVELS['BloodMoor'] = dict(
    act=1, level_type='Wilderness', area_id=1,
    ds1=sorted(glob.glob(os.path.join(TILES_ROOT, 'OUTDOORS', '*.ds1'))) +
        [os.path.join(TILES_ROOT, 'TOWN', 'TownETrans.ds1')])
LEVELS['DenOfEvil'] = dict(
    act=1, level_type='Cave', area_id=2,
    ds1=sorted(glob.glob(os.path.join(TILES_ROOT, 'CAVES', '*.ds1'))))


# ─────────────────────────────────────────────────────────────────────────────
#  AutoMap.txt
# ─────────────────────────────────────────────────────────────────────────────
class Row(object):
    __slots__ = ('level', 'tile', 'style', 's0', 's1', 'cels')

    def __init__(self, level, tile, style, s0, s1, cels):
        self.level, self.tile, self.style, self.s0, self.s1, self.cels = \
            level, tile, style, s0, s1, cels

    def matches(self, style, seq):
        if self.style != style:
            return False
        if self.s0 < 0 or self.s1 < 0:
            return True
        return self.s0 <= seq <= self.s1

    def first_cel(self):
        for c in self.cels:
            if c >= 0:
                return c
        return -1


def load_automap():
    raw = open(AUTOMAP_TXT, 'rb').read().decode('latin-1')
    rows, header = [], None
    for line in raw.split('\n'):
        line = line.rstrip('\r')
        if not line.strip():
            continue
        f = line.split('\t')
        if header is None:
            header = f
            continue
        rows.append(Row(f[0], f[1], int(f[2]), int(f[3]), int(f[4]),
                        [int(f[6]), int(f[8]), int(f[10]), int(f[12])]))
    return header, rows


# ─────────────────────────────────────────────────────────────────────────────
#  已解出瓦片：compositeIndex → (<pack>/<idx>, 层)
# ─────────────────────────────────────────────────────────────────────────────
def load_tile_index():
    """返回 (地面层表, 物件层表, dt1 基名 → {(层, pack)})。

    ⚠️ `compositeIndex (= ((main<<6)+sub)<<5+orientation)` **只在同一个 dt1 文件内唯一**
    ⇒ 跨 dt1 会撞（实测：`floor.dt1` 的 (main0,sub0,orient0) 与 `cave.dt1` 的同名三元组同值）。
    按 DS1 自己的 `dt1_files` 清单把候选 pack 收窄，撞车即消失。
    """
    floor_map, wall_map = collections.defaultdict(list), collections.defaultdict(list)
    by_source = collections.defaultdict(set)
    for layer_dir, dst in (('Tiles', floor_map), ('Objects', wall_map)):
        for man in sorted(glob.glob(os.path.join(RES_D2, layer_dir, '*', 'manifest.json'))):
            pack = os.path.basename(os.path.dirname(man))
            j = json.load(open(man, encoding='utf-8'))
            src = os.path.basename(str(j.get('sourceDt1', '')).replace('\\', '/')).lower()
            by_source[src].add((layer_dir, pack))
            for t in j.get('tiles', []):
                key = '%s/%03d' % (pack, int(t['idx']))
                dst[int(t['compositeIndex'])].append(key)
    return floor_map, wall_map, by_source


# ─────────────────────────────────────────────────────────────────────────────
#  逐格 Cel 解析
# ─────────────────────────────────────────────────────────────────────────────
def resolve_cel(rows_by_level, level_name, is_floor, style, seq):
    """返回 (cel, 命中行数, 该行可用变体数, 是否走了地面 seq 兜底)。命中 0 行 ⇒ (-1, 0, 0, False)。

    ★ 片 automap-redo2 第 3 轮（2026-09-23）新增**地面层 Sequence 兜底**，依据是机械实测
    （`[shift test]` / `[shift detail]`）：

      把 key 里的 Sequence 平移 −1 / 0 / +1 后重算"无命中格数占比"：
        · Town(1 Town)      floor 73.1% → **0.8%**（+1）    wall 0%（三个平移都是 0%）
        · BloodMoor(1 Wild) floor 76.2% → **0.2%**（+1）    wall 0%
        · DenOfEvil(1 Cave) floor **12.9%（现状已很低）** → 15.0%（+1，反而变差）
      逐键看根因：Town/Wilderness 的地面行是 `fl seq=[1,46]`/`[1,47]`，而 DS1 的 `prop2` 域含 0
      且 0 是最大宗（Town 6781/9348 格、Wilderness 8044 格）；Cave 的地面行 `fl seq=[0,0]` 从 0 起、
      0 命中 22393 格 ⇒ **两种约定在同一个字段上并存**。

    ⇒ 因此**只做兜底、不做全局平移**（全局 +1 会改掉 Cave 已正确的 4 个键：seq=12 变无命中、
      seq=16/10/11 选中不同 cel）。兜底是现状的**严格超集**：已命中的键仍命中同一行、cel 不变，
      爆炸半径只有"把无命中的地面格变有"；wall 命中率在三个平移下都是 0% ⇒ 兜底不会触发。
    ⚠️ 本条**可证伪**：它假定原版城镇/野外 automap 的**地面是有纹理的**。若一张**已知关卡名**的
      Act1 原版 automap 基线图显示城镇地面确实空白 ⇒ 本条作废，恢复"无命中 ⇒ -1"。
    """
    hits = [r for r in rows_by_level.get(level_name, ())
            if r.matches(style, seq) and ((r.tile in FLOOR_NAMES) == is_floor)]
    fallback = False
    if not hits and is_floor:
        # 仅地面层、仅在 0 命中时回退一次（详见 docstring 的实测依据）
        hits = [r for r in rows_by_level.get(level_name, ())
                if r.matches(style, seq + 1) and r.tile in FLOOR_NAMES]
        fallback = bool(hits)
    if not hits:
        return -1, 0, 0, False
    return hits[0].first_cel(), len(hits), sum(1 for c in hits[0].cels if c >= 0), fallback


def walk_level(cfg, rows_by_level, floor_map, wall_map, by_source):
    """走该关卡的全部 DS1，收集 (层, 瓦片键) → Cel 投票。"""
    votes = {'ground': collections.defaultdict(collections.Counter),
             'object': collections.defaultdict(collections.Counter)}
    stats = dict(ds1=0, cells_floor=0, cells_wall=0, hit_floor=0, hit_wall=0,
                 no_texture=0, key_not_shipped=0, multi_row=0, multi_variant=0,
                 ci_ambiguous=0, ci_outside_level=0, files=[],
                 floor_seq_fallback=0)      # ★ 地面 Sequence 兜底命中数（见 resolve_cel）
    level_name = '%d %s' % (cfg['act'], cfg['level_type'])
    for path in cfg['ds1']:
        if not os.path.exists(path):
            print('  [WARN] DS1 不存在：%s' % path)
            continue
        d = ds1mod.load_ds1(path)
        stats['ds1'] += 1
        stats['files'].append(os.path.basename(path))
        # 本 DS1 自己的 dt1 清单 ⇒ 允许的 pack 集合（跨 dt1 的 compositeIndex 撞车由此排除）
        allowed = set()
        for rel in d.dt1_files:
            base = os.path.basename(str(rel).replace('\\', '/')).lower()
            allowed |= by_source.get(base, set())
        if not allowed:
            stats['no_texture'] += 1
        for layer, cells, is_floor in (('ground', d.floors[0] if d.floors else [], True),
                                       ('object', d.walls[0] if d.walls else [], False)):
            tmap = floor_map if is_floor else wall_map
            for c in cells:
                if c.is_empty:
                    continue
                stats['cells_floor' if is_floor else 'cells_wall'] += 1
                cel, nhit, nvar, fellback = resolve_cel(rows_by_level, level_name, is_floor,
                                                        c.prop3 & 0x0F, c.prop2)
                if nhit == 0:
                    continue
                if fellback:
                    stats['floor_seq_fallback'] += 1
                if nhit > 1:
                    stats['multi_row'] += 1
                if nvar > 1:
                    stats['multi_variant'] += 1
                stats['hit_floor' if is_floor else 'hit_wall'] += 1
                keys = tmap.get(c.tile_index)
                if not keys:
                    stats['key_not_shipped'] += 1
                    continue
                # 只保留属于本 DS1 的 dt1 集的 pack（层由 Tiles/Objects 目录决定）
                keys = [k for k in keys if (('Tiles' if is_floor else 'Objects'),
                                            k.split('/')[0]) in allowed]
                if not keys:
                    stats['ci_outside_level'] += 1
                    continue
                if len(keys) > 1:
                    stats['ci_ambiguous'] += 1
                for k in keys:
                    votes[layer][k][cel] += 1
    return votes, stats


def pick_cel(counter):
    """取票数最多的 Cel；打平取 Cel 号小者。返回 (cel, 票数, 分歧票数)。"""
    best = sorted(counter.items(), key=lambda kv: (-kv[1], kv[0]))
    return best[0][0], best[0][1], sum(v for _, v in best[1:])


# ─────────────────────────────────────────────────────────────────────────────
#  产出：C# 表
# ─────────────────────────────────────────────────────────────────────────────
def encode_cel_pixels(frame):
    """稀疏编码：每个非透明像素 3 字节 (x, y, 调色板索引) → base64。"""
    buf = bytearray()
    idx = frame.indices
    for y in range(frame.height):
        row = y * frame.width
        for x in range(frame.width):
            v = idx[row + x]
            if v:
                buf += bytes((x, y, v))
    return base64.b64encode(bytes(buf)).decode('ascii')


def emit_cs(table, cels_used, palette_rgb, cel_pixels, report, out_cs=None):
    """写生成物。`out_cs` 可覆盖输出路径 —— 供 `--out-cs` 在**不能写 Assets/ 的时段**
    （例如别的片正占着 play.lock 在 Play：写 Assets/Scripts/** 会触发域重载杀掉对方的会话）
    先把生成物算出来核对。默认仍是 `OUT_CS`。"""
    out_cs = out_cs or OUT_CS
    lines = []
    A = lines.append
    A('// ─────────────────────────────────────────────────────────────────────────────')
    A('// Diablo2 · Core/AutoMapCel.generated.cs')
    A('// ⚠️ **本文件是生成物，禁止手改**（生成器：`tools/probes/gen_automap.py`）。')
    A('//')
    A('// 数据来源 = **原版**（Blizzard North, 2000，取自 d2data.mpq；本项目非商用）：')
    A('//   · `data/global/excel/AutoMap.txt`      逐格 Cel 权威表（2603 行；列 = LevelName/TileName/')
    A('//                                          Style/StartSequence/EndSequence/Type1..4/Cel1..4）')
    A('//   · `data/global/ui/AUTOMAP/MaxiMap.dc6` 1260 帧 × **16×32** 图块表（`CelN` = 帧序号）')
    A('//   · `data/global/palette/ACT1/Pal.PL2`   调色板（原版 ACT1）')
    A('//   · `data/global/tiles/ACT1/**/*.ds1`    Style/Sequence 的载体（`prop3 & 0x0F` / `prop2`）')
    A('//')
    A('// 查询键 = (LevelName = `<act> <LevelType>`、Style = DS1 `prop3 & 0x0F`、Sequence = DS1 `prop2`)；')
    A('// 命中多行 ⇒ 取表里最先出现的一行、取该行第一个 `CelN >= 0`；无命中 ⇒ -1（该格不画）。')
    A('// ★ 地面层 Sequence 兜底（片 automap-redo2 第 3 轮，2026-09-23）：**仅当 `hits` 为空 且 该格属地面层**')
    A('//   时，用 `Sequence + 1` 再查一次；仍无命中 ⇒ -1。依据是机械实测：')
    A('//   Town 地面无命中格 73.1% → 0.8%、Wilderness 76.2% → 0.2%（+1），而 Cave 地面现状已只 12.9%')
    A('//   且它的 `fl` 行本就从 seq=0 起 ⇒ **全局平移会改坏 Cave**，所以只做兜底。墙层三个平移都是 0% 无命中 ⇒ 不受影响。')
    A('// ⛔ 原版「多行命中时挑哪一行 / 4 个变体挑哪一个」的规则**本机没有载体**（见')
    A('//   （定案登记 §2）⇒ 上一条是本项目**定死并登记**的可复跑规则。')
    A('//')
    A('// 几何（实测）：世界地砖 160×80 ⇒ automap 比例 **1/10**；一格 = 16×8 等距菱形、')
    A('//   cel 帧的**底部 8 行**（y=24..31）就是这格 ⇒ cel 左上角贴到 (posX, posY)。')
    A('// ─────────────────────────────────────────────────────────────────────────────')
    A('')
    A('using System.Collections.Generic;')
    A('')
    A('namespace Diablo2.Core')
    A('{')
    A('    /// <summary>原版自动地图逐格 Cel 表（生成物，见文件头）。</summary>')
    A('    public static class AutoMapCel')
    A('    {')
    A('        /// <summary>图块帧宽（纹理px；原版 `MaxiMap.dc6` 每帧 16×32）。</summary>')
    A('        public const int W = %d;' % CEL_W)
    A('        /// <summary>图块帧高（纹理px）。</summary>')
    A('        public const int H = %d;' % CEL_H)
    A('        /// <summary>世界 → automap 的缩放分母（地砖 160×80 ⇒ 1/10）。</summary>')
    A('        public const int ScaleDen = %d;' % SCALE_DEN)
    A('        /// <summary>`MaxiMap.dc6` 帧数（实测 1260 = `CelN` 的值域上界 + 1）。</summary>')
    A('        public const int FrameCount = %d;' % report['frames'])
    A('        /// <summary>无 Cel（该格原版 automap 不画）。</summary>')
    A('        public const short None = -1;')
    A('')
    A('        /// <summary>ACT1 调色板（原版 `ACT1/Pal.PL2` 的 256×RGB；索引 0 无意义）。</summary>')
    A('        public static readonly byte[] PaletteRgb = Decode(')
    A('            "%s");' % base64.b64encode(bytes(palette_rgb)).decode('ascii'))
    A('')
    A('        /// <summary>逐 cel 的稀疏像素（base64：每 3 字节 `x, y, 调色板索引`）。</summary>')
    A('        public static readonly Dictionary<int, byte[]> CelPixels = new Dictionary<int, byte[]>')
    A('        {')
    for cel in sorted(cel_pixels):
        A('            { %d, Decode("%s") },' % (cel, cel_pixels[cel]))
    A('        };')
    A('')
    A('        /// <summary>逐关卡的逐格 Cel 表（键 = `<pack>/<idx>`，与 `GridMap` 的瓦片键同口径）。</summary>')
    A('        private static readonly Dictionary<string, short>[] _ground =')
    A('        {')
    for area in sorted(table):
        A('            /* %s */ Build(%s),' % (report['areas'][area]['name'],
                                             ToKeys(table[area]['ground'])))
    A('        };')
    A('        private static readonly Dictionary<string, short>[] _object =')
    A('        {')
    for area in sorted(table):
        A('            /* %s */ Build(%s),' % (report['areas'][area]['name'],
                                             ToKeys(table[area]['object'])))
    A('        };')
    A('')
    A('        /// <summary>取某格（按原版瓦片键）的 Cel；<see cref="None"/> = 原版不画这格。</summary>')
    A('        public static short Cel(int areaId, bool objectLayer, string tileKey)')
    A('        {')
    A('            var tbl = objectLayer ? _object : _ground;')
    A('            if (tileKey == null || areaId < 0 || areaId >= tbl.Length) return None;')
    A('            short cel;')
    A('            return tbl[areaId].TryGetValue(tileKey, out cel) ? cel : None;')
    A('        }')
    A('')
    A('        private static Dictionary<string, short> Build(string[] flat)')
    A('        {')
    A('            var d = new Dictionary<string, short>(flat.Length / 2);')
    A('            for (var i = 0; i + 1 < flat.Length; i += 2) d[flat[i]] = short.Parse(flat[i + 1]);')
    A('            return d;')
    A('        }')
    A('')
    A('        private static byte[] Decode(string b64) => System.Convert.FromBase64String(b64);')
    A('    }')
    A('}')
    os.makedirs(os.path.dirname(out_cs), exist_ok=True)
    open(out_cs, 'w', encoding='utf-8', newline='\n').write('\n'.join(lines) + '\n')
    return out_cs


def ToKeys(d):
    parts = []
    for k in sorted(d):
        parts.append('"%s", "%d"' % (k, d[k]))
    return 'new[] { ' + ', '.join(parts) + ' }' if parts else 'new string[0]'


# ─────────────────────────────────────────────────────────────────────────────
#  离线预览（城镇）：读生成物布局表 → 按与面板同一几何贴 cel
# ─────────────────────────────────────────────────────────────────────────────
def parse_town_layout():
    src = open(TOWN_LAYOUT_CS, encoding='utf-8').read()
    def arr(name):
        m = re.search(r'%s\s*=\s*\{(.*?)\n\s*\};' % name, src, re.S)
        return re.findall(r'"([^"]*)"', m.group(1))
    packs = arr('Packs')
    ground = arr('GroundRows')
    obj = arr('ObjectRows')
    return packs, ground, obj


def decode6(packs, row, x):
    off = x * 6
    if row is None or off + 6 > len(row) or row[off] == '-':
        return ''
    pid = (ord(row[off]) - 48) * 100 + (ord(row[off + 1]) - 48) * 10 + (ord(row[off + 2]) - 48)
    if pid < 0 or pid >= len(packs):
        return ''
    return '%s/%s' % (packs[pid], row[off + 3:off + 6])


def render(area_id, table, cel_pixels, palette_rgb, w, h, get_keys, out_png):
    step_x, step_y = CEL_W // 2, CEL_H // 8          # 16×8 等距菱形 ⇒ 步长 (±8, +4)
    texw = (w + h - 2) * step_x + CEL_W
    texh = (w + h - 2) * step_y + CEL_H
    buf = bytearray(texw * texh * 4)
    pal = [tuple(palette_rgb[i * 3:i * 3 + 3]) for i in range(256)]
    drawn = {False: 0, True: 0}
    for y in range(h):
        for x in range(w):
            px = ((x - y) + (h - 1)) * step_x
            py = (x + y) * step_y
            for obj_layer in (False, True):
                key = get_keys(x, y, obj_layer)
                if not key:
                    continue
                tbl = table['object'] if obj_layer else table['ground']
                cel = tbl.get(key, -1)
                if cel < 0 or cel not in cel_pixels:
                    continue
                raw = base64.b64decode(cel_pixels[cel])
                for i in range(0, len(raw), 3):
                    u, v, pi = raw[i], raw[i + 1], raw[i + 2]
                    tx, ty = px + u, py + v
                    if 0 <= tx < texw and 0 <= ty < texh:
                        o = (ty * texw + tx) * 4
                        r, g, b = pal[pi]
                        buf[o:o + 4] = bytes((r, g, b, 255))
                drawn[obj_layer] += 1
    # 透明底 → 深灰（仅预览用，便于肉眼；面板里未探索 = 透明）
    for i in range(0, len(buf), 4):
        if buf[i + 3] == 0:
            buf[i:i + 4] = bytes((0x10, 0x10, 0x10, 0xFF))
    write_png(out_png, texw, texh, bytes(buf))
    return texw, texh, drawn


def write_png(path, w, h, rgba):
    import zlib
    raw = bytearray()
    for y in range(h):
        raw.append(0)
        raw += rgba[y * w * 4:(y + 1) * w * 4]
    def chunk(tag, payload):
        c = struct.pack('>I', len(payload)) + tag + payload
        return c + struct.pack('>I', zlib.crc32(tag + payload) & 0xFFFFFFFF)
    png = b'\x89PNG\r\n\x1a\n'
    png += chunk(b'IHDR', struct.pack('>IIBBBBB', w, h, 8, 6, 0, 0, 0))
    png += chunk(b'IDAT', zlib.compress(bytes(raw), 9))
    png += chunk(b'IEND', b'')
    d = os.path.dirname(path)
    if d:
        os.makedirs(d, exist_ok=True)
    open(path, 'wb').write(png)


# ─────────────────────────────────────────────────────────────────────────────
def main():
    global OUT_CS_OVERRIDE
    argv = sys.argv[1:]
    if '--out-cs' in argv:
        OUT_CS_OVERRIDE = argv[argv.index('--out-cs') + 1]
        print('OUT-CS-OVERRIDE %s（不改 Assets/；用于 play.lock 被别人占着时先核对生成物）'
              % OUT_CS_OVERRIDE)
    elif '--render-only' in argv:
        print('RENDER-ONLY（本轮保持既有语义：照常重写生成物）')
    header, rows = load_automap()
    rows_by_level = collections.defaultdict(list)
    for r in rows:
        rows_by_level[r.level].append(r)
    print('AutoMap.txt: 表头 %d 列，数据行 %d，LevelName %d 个' % (len(header), len(rows), len(rows_by_level)))

    floor_map, wall_map, by_source = load_tile_index()
    print('已解出瓦片：地面层 compositeIndex %d 个、物件层 %d 个；来源 dt1 基名 %d 个'
          % (len(floor_map), len(wall_map), len(by_source)))

    d = dc6mod.parse(open(MAXIMAP_DC6, 'rb').read())
    pal = dc6mod.read_pl2(ACT1_PL2)
    palette_rgb = bytearray()
    for i in range(256):
        palette_rgb += bytes(pal[i][:3])
    print('MaxiMap.dc6：%d 帧，尺寸集合 %s' % (len(d.frames),
                                          sorted(set('%dx%d' % (f.width, f.height) for f in d.frames))))

    table, report = {}, dict(header=header, rows=len(rows), levels={}, areas={},
                             frames=len(d.frames))
    for name, cfg in LEVELS.items():
        votes, stats = walk_level(cfg, rows_by_level, floor_map, wall_map, by_source)
        t = {}
        for layer in ('ground', 'object'):
            t[layer] = {}
            for k, ctr in votes[layer].items():
                cel, top, diss = pick_cel(ctr)
                t[layer][k] = cel
        table[cfg['area_id']] = t
        report['levels'][name] = stats
        report['areas'][cfg['area_id']] = dict(name=name, levelName='%d %s' % (cfg['act'], cfg['level_type']),
                                               ground=len(t['ground']), object=len(t['object']))
        print('[%s] %s：DS1 %d 块  floor 格 %d（命中 %d，其中 seq 兜底 %d）  wall 格 %d（命中 %d）  '
              '键未随包 %d  非本关 dt1 集 %d  同 ci 多 pack %d  多行命中 %d  多变体行 %d  '
              '地面键 %d / 物件键 %d'
              % (name, report['areas'][cfg['area_id']]['levelName'], stats['ds1'],
                 stats['cells_floor'], stats['hit_floor'], stats['floor_seq_fallback'],
                 stats['cells_wall'], stats['hit_wall'],
                 stats['key_not_shipped'], stats['ci_outside_level'], stats['ci_ambiguous'],
                 stats['multi_row'], stats['multi_variant'],
                 len(t['ground']), len(t['object'])))

    cels = set()
    for area in table:
        for layer in ('ground', 'object'):
            cels |= set(v for v in table[area][layer].values() if v >= 0)
    report['cels_used'] = sorted(cels)
    print('被引用的 Cel %d 个：%s%s' % (len(cels), sorted(cels)[:24], ' ...' if len(cels) > 24 else ''))

    cel_pixels = {}
    for cel in sorted(cels):
        if cel >= len(d.frames):
            print('  [WARN] Cel %d 超出帧数 %d ⇒ 跳过' % (cel, len(d.frames)))
            continue
        cel_pixels[cel] = encode_cel_pixels(d.frames[cel])
    wrote = emit_cs(table, cels, palette_rgb, cel_pixels, report, out_cs=OUT_CS_OVERRIDE)
    print('WROTE %s%s' % (wrote, '   (--out-cs override; the game依然读 %s)' % OUT_CS
                          if OUT_CS_OVERRIDE else ''))

    # 预览：城镇（读生成物布局表 → 与面板同一几何）
    packs, ground, obj = parse_town_layout()
    get_keys = lambda x, y, ol: decode6(packs, obj[y] if ol else ground[y], x)
    texw, texh, drawn = render(0, table[0], cel_pixels, palette_rgb,
                               len(ground[0]) // 6, len(ground), get_keys, OUT_PNG)
    report['preview'] = dict(png=OUT_PNG, w=texw, h=texh, ground_cells=drawn[False],
                             object_cells=drawn[True])
    print('WROTE %s（%dx%d，贴了 地面 %d / 物件 %d 个 cel）'
          % (OUT_PNG, texw, texh, drawn[False], drawn[True]))

    os.makedirs(os.path.dirname(OUT_JSON), exist_ok=True)
    json.dump(report, open(OUT_JSON, 'w', encoding='utf-8'), ensure_ascii=False, indent=1)
    print('WROTE %s' % OUT_JSON)
    return 0


if __name__ == '__main__':
    sys.exit(main())
