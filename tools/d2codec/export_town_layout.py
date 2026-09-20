# -*- coding: utf-8 -*-
"""把**原版罗格营地**的固定布局 + **逐格真实瓦片**从原版预设块抽出来，生成 C# 表
`client/Assets/Scripts/Module/Map/MapGenTownLayout.cs`（生成物，禁止手改）。

── 为什么是「多块 + 原版关卡尺寸」（**规则与尺寸都读原版表，不是本项目定的**）──────
  · 关卡尺寸：`原版资源/d2raw/data/global/excel/Levels.txt` 里 `Name = Act 1 - Town`
    那一行 ⇒ **SizeX=56 SizeY=40**（LevelName = Rogue Encampment）。
  · 预设块清单：`LvlPrest.txt` 里 `Name = Act 1 - Town 1`（`Def=1 Populate=1 FillBlanks=1`）
    的四块 ⇒ **File1=Act1/Town/TownN1.ds1 / File2=TownE1 / File3=TownS1 / File4=TownW1**。
  ⇒ 本项目照这两行做：**关卡 = 56×40**，块 = 这四块，按 `File1..File4` 顺序逐格**补空**
    （`FillBlanks=1` 的语义：本格在前面的块里没有瓦片时，由后面的块补上）。

── 四块的拼接关系（**实测反推，本生成器每次自己复验**）──────────────────────────
  四块都是 **同一座营地**，只是**导出时的原点不同**（原版素材如此）。实测锚点 = 营地**围栏环**
  的左上角；用 `BARRACKS/warp.dt1` 的 3 个标记格做**独立复验**：对齐后四块的 warp 标记格
  **逐格重合**（3/3 命中）⇒ 对齐口径正确（不成立时本脚本直接退出并报错）。
  实测偏移（相对参考块 TownW1）：`TownN1 (+14,-11)`、`TownE1 (+18,-5)`、`TownS1 (+14,+6)`、
  `TownW1 (0,0)`。对齐后取并集 = **原版那一座营地**，且**每块被画布裁掉的部分由其它块补齐**
  （这正是原版 `FillBlanks=1` 想要的效果）。

每格抽两样东西：
    · **kind**（给 `GridMap` 判可走性）：'.' 草 / 'd' 泥 / 'f' 栅栏 / 't' 树 / 's' 石矮墙 /
      'o' 摊位帐篷 / 'r' **水（河/水塘；可走性 = 阻挡）** / 'x' 出城口
    · **逐格瓦片**（给渲染用，**原版铺哪张就哪张**）：
      `ground` = floor 层的瓦片；`object` = wall 层第 0 层的瓦片（没有就留空）。
      编码成 `<packId:3><tileIdx:3>`，packId 是 `Packs` 数组下标，idx 是 dt1 内 tile 数组下标
      （与 `export_tiles.py` 落盘的 `<idx>.png` 一一对应）。

── ③ 两块**不是对齐来的、而是直读 DS1 层**的数据 ─────────────────────────────

  (a) **5 个 NPC 的站位 = 原版坐标**（不再是启发式挑点）。
      DS1 的 objects 层里有两类预设单位（参考实现：
      `libd2/packages/drlg/src/drlg/preset.zig:488-494` 的 `switch (o.kind)` —
      **kind=1 = 怪物/NPC、kind=2 = 物件**；`structs.zig:149-154` 的
      `D2PresetUnitStrc` 注释：`nMode` 1=monster from monpreset.txt、`nPosX/nPosY`
      **是 sub-tile 坐标**）。kind=1 的 `id` 不是 monstats 行号，而是
      **该 act 的 `MonPreset.txt` 块（`Act == act+1` 的那些行，按文件顺序）的下标**
      —— `presettables.zig:11-20`（`MONSTERTBLS_GetMonPresetRecord`）。
      Act 1 的那块：`0=gheed 1=cain1 2=akara 3=chicken 4=rogue1 5=kashya 6=cow
      7=warriv1 8=charsi 9=andariel …`（`MonPreset.txt`，本批 d2data 里没有这张表，
      取自 `原版资源/参考工程_Diablerie/libd2/packages/data/src/excel/MonPreset.txt`）。
      ⇒ 按 `(sub_tile_x // 5, sub_tile_y // 5)` 换算成格（sub-tile = 格 × 5，
      出处 `lib.zig:1136`「SUBTILES (tile*5)」）。
      ⚠️ 四块里都各带一份 NPC；四块是同一座营地按不同原点导出的，实测它们的
      akara/charsi/warriv 换到关卡坐标后**逐格重合**，gheed/kashya 差 ≤2 格
      （作者在各块里手调过）。本项目**取参考块那一份**（= 本表内容所来自的那一块）。

  (b) **跨河木桥**（`OUTDOORS/bridge.dt1`）：只有 `TownE1.ds1`（与 `TownETrans.ds1`）
      有 —— 实测 `TownE1` 的桥 = floor `x∈[47,56] y∈[15,18]`、wall `x∈[47,56] y∈{16,18}`
      （换算成关卡坐标 = `x∈[29,38] y∈[20,23]`，正好横跨河带 `x∈[30,37]`）。
      `TownETrans` 的桥在它自己的第 0 列（`x=0`，y 同 15..18）—— 与 `TownE1` 的第 56 列
      （DS1 的 +1 共享边列）相接 ⇒ **过渡带是这座桥向东的延伸段**（见 `场景对照.md`）。
      本例的合并口径（"参考块第一 + 逐格补空"）会让参考块在那几格赢，桥就落不进来
      ⇒ 这里**给桥单独一条规则**：**任何一块里有桥瓦片的格，一律用桥瓦片**
      （桥面 = 可走 `'d'`、栏杆 = 阻挡 `'s'`），其余格仍按补空规则。

用法：
    python export_town_layout.py [--out <MapGenTownLayout.cs>] [--debug]
"""

import os
import sys

# Windows 控制台默认 GBK，print 里带 '⇒' 这类字符会直接抛 UnicodeEncodeError ⇒ 强制 UTF-8。
try:
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')
except Exception:
    pass

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
EXCEL = os.path.join(RAW, 'data', 'global', 'excel')
DEFAULT_OUT = os.path.join(_REPO, 'client', 'Assets', 'Scripts', 'Module', 'Map',
                           'MapGenTownLayout.cs')

LEVEL_NAME = 'Act 1 - Town'          # Levels.txt 的 Name（LevelName = Rogue Encampment）
PRESET_NAME = 'Act 1 - Town 1'       # LvlPrest.txt 的 Name（Def=1）

# ══════════════════════════════════════════════════════════════════════════════
# ★ 片 4 修正：**关卡窗口原点**
#
# 问题（旧版）：窗口直接取参考块 TownW1 的本地 [0,56)×[0,40)。而参考块的本地 x=0 正是
# **营地西侧围栏那一列** ⇒ 地图的西边界压在营地围栏上、出城口（西侧 3 格缺口）正好落在
# 地图边界 ⇒ 游戏里看过去，出城口外面什么都没有（**黑色的一个口**）。
# 原版不这样：出城口外面还有**营地外那片地**（草地/树/石矮墙），关卡边界在更西边。
#
# 窗口原点怎么定（三条互相独立的依据，都来自原版数据本身）：
#   ① **东边**：`TownE1.ds1` 的本地第 56 列（DS1 的 **+1 共享边列**）上放着木桥的东端；
#      而原版引擎把城镇接缝带「Act 1 - Town 1 Transition E」(8×40) 铺在**野外关卡**的
#      西边界（`libd2/.../drlg/outdoors/OutRoom.zig:271`），该带第 0 列 = 桥的延续
#      （实测 `TownETrans.ds1` 第 0 列 y=15..18 的 wall 层 = `bridge.dt1`）
#      ⇒ **野外的第 0 列 = 城镇关卡的最后 1 列**（共享边列）⇒ 城镇关卡东边界 = 桥的东端那列。
#   ② **南边**：`TownS1.ds1` 的本地第 40 行（+1 共享边行）压着营地南侧围栏；接缝带
#      「Transition S」铺在野外的北边界（`OutRoom.zig:265/268`）⇒ 城镇关卡南边界 = 营地南围栏那行。
#   ③ **宽度对得上**：营地本体（围栏西侧列 → 桥东端列）实测占 **39 列**（合并帧 x 0..38），
#      关卡 56 列 ⇒ 西侧正好差 **17 列**（= 出城口外面那片地）⇒ 窗口 = 合并帧 x∈[-17,38]。
#      行同理：营地南围栏在合并帧第 34 行 ⇒ 窗口 = 合并帧 y∈[-5,34]（40 行）。
#      另一处佐证：只有这样取窗口，西侧那 17 列才**完整落在** `TownE1/TownS1` 的块内容里
#      （`TownE1` 本地 x=1..17、`TownS1` 本地 x=0..13），否则条带底部几行会没有瓦片。
#
# ⇒ 合并帧（= 参考块本地帧）里，**关卡窗口 = x∈[-17,38] × y∈[-5,34]**，即本文件的
#    `WIN_X0/WIN_Y0`；窗口内每一格都按"参考块第一 + 逐格补空"从四块取（含新增的西/北条带）。
# ⚠️ 共享边列的 ±1 约定有 1 格歧义（-17 还是 -18），实测取 -17：此时窗口宽度
#    17+39 = 56 = `Levels.txt` 的 SizeX，且桥东端列落在窗口最后一列。
WIN_X0 = -17
WIN_Y0 = -5

EMPTY_CELL = '------'

# ── NPC 站位（③-a）──────────────────────────────────────────────────────────
# `MonPreset.txt` 的 Act 1 块 → DS1 kind=1 单位的 id。本批 d2data 的 excel 里没有这张表
# （`原版资源/d2raw/data/global/excel/` 只有 43 张），取自参考工程的副本（出处见文件头）。
MONPRESET_SRC = os.path.join(_REPO, '原版资源', '参考工程_Diablerie', 'libd2', 'packages',
                             'data', 'src', 'excel', 'MonPreset.txt')

# 本项目 5 个 NPC（`Def/Enums.cs::NpcId` 的顺序）→ `MonPreset.txt` 的 Place 名。
# ⛔ 顺序 = (int)NpcId：0 阿卡拉 / 1 卡夏 / 2 恰西 / 3 基德 / 4 瓦瑞夫。
NPC_PLACES = ['akara', 'kashya', 'charsi', 'gheed', 'warriv1']

# 桥的提供者 dt1 短名（`fi.names_of()` 返回的是短名，如 'bridge.dt1'）。
BRIDGE_DT1 = 'bridge.dt1'

# 子格 → 格：sub-tile = 格 × 5（出处 `libd2/.../drlg/src/lib.zig:1136`「SUBTILES (tile*5)」）。
SUBTILES_PER_TILE = 5


# ══════════════════════════════════════════════════════════════════════════════
#  ① 原版规则表（Levels / LvlPrest）—— 尺寸与块清单都从表里读
# ══════════════════════════════════════════════════════════════════════════════

def _read_table(name):
    """读一张原版 excel txt（制表符分隔，第一行是列名）。"""
    path = os.path.join(EXCEL, name)
    if not os.path.exists(path):
        raise SystemExit('缺原版规则表 %s（先跑 `python 原版资源/storm.py extract '
                         '原版资源/d2mpq/d2data.mpq 原版资源/d2raw .txt`）' % path)
    with open(path, 'r', encoding='latin-1') as fh:
        rows = [ln.rstrip('\r\n').split('\t') for ln in fh if ln.strip()]
    head = rows[0]
    return head, [dict(zip(head, r)) for r in rows[1:]]


def level_size():
    _head, rows = _read_table('Levels.txt')
    for r in rows:
        if r.get('Name') == LEVEL_NAME:
            return int(r['SizeX']), int(r['SizeY'])
    raise SystemExit('Levels.txt 里找不到 Name = %r（表被换了？）' % LEVEL_NAME)


def preset_files():
    _head, rows = _read_table('LvlPrest.txt')
    for r in rows:
        if r.get('Name') == PRESET_NAME:
            out = []
            for i in range(1, 7):
                v = (r.get('File%d' % i) or '').strip()
                if v and v != '0':
                    out.append(v)
            return out
    raise SystemExit('LvlPrest.txt 里找不到 Name = %r（表被换了？）' % PRESET_NAME)


def monpreset_act1():
    """`MonPreset.txt` 里 `Act == 1` 的那些行（按文件顺序）⇒ DS1 kind=1 单位的 id 字典。

    依据（文件头 ③-a）：DS1 的怪物单位 id 索引的是**该 act 的 MonPreset 块**，
    见参考实现 `libd2/packages/drlg/src/drlg/presettables.zig:11-20`。
    """
    if not os.path.exists(MONPRESET_SRC):
        raise SystemExit('缺 %s（DS1 的 NPC 站位就靠它解释；见本文件文件头 ③-a）' % MONPRESET_SRC)
    with open(MONPRESET_SRC, 'r', encoding='latin-1') as fh:
        rows = [ln.rstrip('\r\n').split('\t') for ln in fh if ln.strip()]
    head = rows[0]
    iact = head.index('Act')
    iplace = head.index('Place')
    return [(r[iplace] or '').strip().lower() for r in rows[1:]
            if len(r) > iplace and (r[iact] or '').strip() == '1']


# ══════════════════════════════════════════════════════════════════════════════
#  ② 素材解析
# ══════════════════════════════════════════════════════════════════════════════

def _palette():
    if _palette.pal is None:
        try:
            from . import pl2 as pl2mod
        except ImportError:
            import pl2 as pl2mod
        _palette.pal = pl2mod.load_pl2(
            os.path.join(RAW, 'data', 'global', 'palette', 'ACT1', 'Pal.PL2'))
    return _palette.pal


_palette.pal = None

_DT1_CACHE = {}


def _is_grass_floor(w, h, rgba):
    sr = sg = sb = n = 0
    for i in range(w * h):
        if rgba[i * 4 + 3] == 0:
            continue
        sr += rgba[i * 4]
        sg += rgba[i * 4 + 1]
        sb += rgba[i * 4 + 2]
        n += 1
    if n == 0:
        return False
    r, g, b = sr / n, sg / n, sb / n
    return g - b >= 14 and g >= r - 2


def _load_dt1(rel):
    """dt1 相对路径（小写）→ 已解析的 DT1（四块共用同一批 dt1，只读一次）。"""
    key = rel.replace('\\', '/').lower()
    if key in _DT1_CACHE:
        return _DT1_CACHE[key]
    fp = os.path.join(RAW, key.replace('/', os.sep))
    val = None
    if os.path.exists(fp):
        try:
            val = dt1mod.load_dt1(fp)
        except ValueError:
            val = None
    _DT1_CACHE[key] = val
    return val


_TILE_INFO_CACHE = {}


def _dt1_tile_info(rel, pack_of):
    """**每张 dt1 只解析/渲染一次**（四块共用同一批 dt1）：→ {composite: (pack, idx, area, grass)}。

    同一个 compositeIndex 有多个 rarity 变体（含 0×0 占位）⇒ 取「不透明像素最多」的那个。
    """
    key = rel.replace('\\', '/').lower()
    if key in _TILE_INFO_CACHE:
        return _TILE_INFO_CACHE[key]

    out = {}
    d = _load_dt1(rel)
    if d is None:
        print('  [WARN] 依赖 dt1 读不到：%s' % rel)
        _TILE_INFO_CACHE[key] = out
        return out
    pack = pack_of.get(key)
    if pack is None:
        print('  [WARN] dt1 不在 export_tiles.PACKS（其瓦片没导出）：%s' % rel)
    fp = os.path.join(RAW, key.replace('/', os.sep))
    with open(fp, 'rb') as fh:
        data = fh.read()
    pal = _palette()
    for tile in d.tiles:
        if pack is None or tile.width <= 0 or tile.pixel_height <= 0:
            continue
        img = dt1mod.render_tile(tile, data=data)
        area = sum(img.mask)
        if area == 0:
            continue
        prev = out.get(tile.composite_index)
        if prev is not None and prev[2] >= area:
            continue
        grass = (_is_grass_floor(img.w, img.h, img.to_rgba(pal))
                 if tile.orientation == 0 else False)
        out[tile.composite_index] = (pack, tile.array_index, area, grass)
    _TILE_INFO_CACHE[key] = out
    return out


def _tiles_path(rel):
    """`LvlPrest.txt` 的 File 列是相对 `data/global/tiles/` 的路径（如 `Act1/Town/TownW1.ds1`）。"""
    rel = rel.replace('\\', '/')
    if not rel.lower().startswith('data/'):
        rel = 'data/global/tiles/' + rel
    return os.path.join(RAW, rel.replace('/', os.sep))


class FileInfo(object):
    """一个预设块：DS1 + 每格瓦片的解析结果（pack / 下标 / 是否草地 / 提供者）。"""

    def __init__(self, rel, pack_of):
        self.rel = rel
        path = _tiles_path(rel)
        if not os.path.exists(path):
            raise SystemExit('缺预设块 %s（先解包 tiles）' % path)
        self.ds1 = ds1mod.load_ds1(path)
        self.providers = {}      # composite_index → set(dt1 短名)
        self.best = {}           # composite_index → (pack, array_index, area, is_grass)

        for r in self.ds1.dt1_files:
            short = os.path.basename(r.replace('\\', '/')).lower()
            info = _dt1_tile_info(r, pack_of)
            for ci, v in info.items():
                self.providers.setdefault(ci, set()).add(short)
                prev = self.best.get(ci)
                if prev is None or prev[2] < v[2]:
                    self.best[ci] = v
            if _load_dt1(r) is not None:
                # 该 dt1 里"有瓦片但没导出"的 composite 也要能判种类（providers）
                for tile in _load_dt1(r).tiles:
                    self.providers.setdefault(tile.composite_index, set()).add(short)

    def names_of(self, cell):
        """该格的 dt1 提供者短名集合。"""
        if cell is None or cell.is_empty:
            return set()
        return self.providers.get(cell.tile_index, set())

    def grass_of(self, cell):
        hit = self.best.get(cell.tile_index)
        return bool(hit is not None and hit[3])


def _pack_table(files):
    used = set()
    for f in files:
        for _ci, (_pack, _idx, _area, _g) in f.best.items():
            used.add(_pack)
    packs = sorted(used)
    return packs, dict((p, i) for i, p in enumerate(packs))


def _key_of(fi, cell, pack_id):
    hit = fi.best.get(cell.tile_index)
    if hit is None:
        return ''
    return '%03d%03d' % (pack_id[hit[0]], hit[1])


# ══════════════════════════════════════════════════════════════════════════════
#  ③ 对齐（锚点 = 围栏环左上角；独立复验 = warp 标记格）
# ══════════════════════════════════════════════════════════════════════════════

def _ring_anchor(fi):
    """营地围栏环（`fence.dt1`）的左上角 ⇒ (x, y, w, h, count)。"""
    d = fi.ds1
    xs, ys, n = [], [], 0
    for y in range(d.height):
        for x in range(d.width):
            c = d.wall_at(0, x, y)
            if c.is_empty:
                continue
            if 'fence.dt1' in fi.names_of(c):
                xs.append(x)
                ys.append(y)
                n += 1
    if not xs:
        raise SystemExit('块 %s 里找不到围栏环（fence.dt1）⇒ 对齐锚点失效' % fi.rel)
    return min(xs), min(ys), max(xs) - min(xs) + 1, max(ys) - min(ys) + 1, n


def _warp_cells(fi):
    """该块里依赖 `BARRACKS/warp.dt1` 的格（实测每块 3 格，作独立复验）。"""
    d = fi.ds1
    out = []
    for y in range(d.height):
        for x in range(d.width):
            c = d.wall_at(0, x, y)
            if not c.is_empty and 'warp.dt1' in fi.names_of(c):
                out.append((x, y))
    return out


def align_offsets(files):
    """四块相对参考块（围栏环 min-x 最小者）的偏移，并用 warp 标记格复验。"""
    anchors = [(f, _ring_anchor(f)) for f in files]
    ref = min(anchors, key=lambda t: (t[1][0], t[1][1]))[0]
    rx, ry = _ring_anchor(ref)[:2]
    offs = {}
    for f, (ax, ay, w, h, n) in anchors:
        offs[f.rel] = (ax - rx, ay - ry)
        print('  锚点 %-12s 围栏环 x[%d..%d] y[%d..%d]（%d 格）⇒ 偏移 (%+d,%+d)'
              % (os.path.basename(f.rel), ax, ax + w - 1, ay, ay + h - 1, n,
                 ax - rx, ay - ry))

    ref_warp = sorted((x - offs[ref.rel][0], y - offs[ref.rel][1]) for x, y in _warp_cells(ref))
    checked = 0
    for f in files:
        ws = sorted((x - offs[f.rel][0], y - offs[f.rel][1]) for x, y in _warp_cells(f))
        if not ws:
            print('  [WARN] 块 %s 没有 warp 标记格 ⇒ 跳过该项复验' % f.rel)
            continue
        ok = (ws == ref_warp)
        checked += 1
        print('  复验 %-12s 对齐后 warp 标记格 = %s  %s'
              % (os.path.basename(f.rel), ws, 'OK' if ok else '★不一致（参考 %s）' % ref_warp))
        if not ok:
            raise SystemExit('四块对齐失败：warp 标记格不重合 ⇒ 锚点口径不成立'
                             '（请重查本文件的对齐段）')
    print('  参考块 = %s（围栏环左上角 (%d,%d)）  对齐复验通过 %d 块'
          % (os.path.basename(ref.rel), rx, ry, checked))
    return ref.rel, offs


# ══════════════════════════════════════════════════════════════════════════════
#  ④ 主流程
# ══════════════════════════════════════════════════════════════════════════════

def _kind_of_wall(names, cell):
    if cell.orientation == 14 or 'treegroups.dt1' in names:
        return 't'
    if 'fence.dt1' in names:
        return 'f'
    if 'stonewall.dt1' in names:
        return 's'
    if 'objects.dt1' in names:
        return 'o'
    if 'river.dt1' in names or 'stones.dt1' in names:
        return 'r'
    return 's'


def build(out_path, debug):
    gw, gh = level_size()
    rels = preset_files()
    pack_of = {}
    for rel_dt1, pack, _note in exp.PACKS:
        pack_of[rel_dt1.lower()] = pack

    print('原版规则：Levels.txt「%s」⇒ 关卡 %dx%d；LvlPrest.txt「%s」⇒ %d 块：%s'
          % (LEVEL_NAME, gw, gh, PRESET_NAME, len(rels), ', '.join(rels)))

    files = [FileInfo(r, pack_of) for r in rels]
    ref_rel, offs = align_offsets(files)
    packs, pack_id = _pack_table(files)

    # ── 逐格合并 ─────────────────────────────────────────────────────────────
    # **优先级 = 参考块第一，其余按 LvlPrest 的 File 顺序补空**（原版 `FillBlanks=1` 的语义：
    # 只补"还没有瓦片"的格）。为什么参考块第一：四块是同一座营地（见文件头，warp 标记
    # 复验），参考块是**最完整**的那一份（含完整围栏环 + 出城口）；若按 File1 顺序把
    # TownN1 放第一，它的**内部物件**会盖到参考块的围栏上（实测围栏环被盖掉 ⇒ 自检不过）。
    ref = [f for f in files if f.rel == ref_rel][0]
    order = [ref] + [f for f in files if f is not ref]
    wall_src = [[None] * gw for _ in range(gh)]     # (FileInfo, cell) —— wall 层（0 优先，1 叠层）
    floor_src = [[None] * gw for _ in range(gh)]
    wall1_cells = 0
    filled_by_others = 0
    uncovered = 0                    # 窗口内"四块都没有内容"的格（原版这几格本来就没瓦片）

    def cell_of(f, x, y, layer):
        """取块 f 在**关卡格** (x,y) 上的第 layer 层（越界 ⇒ None）。

        ⚠️ (x,y) 是**关卡窗口坐标**（本文件 `WIN_X0/WIN_Y0` 定义的帧），不是块本地坐标：
           块本地 = 窗口 + 窗口原点(合并帧) + 该块偏移。
        """
        ox, oy = offs[f.rel]
        sx, sy = x + WIN_X0 + ox, y + WIN_Y0 + oy
        if not (0 <= sx < f.ds1.width and 0 <= sy < f.ds1.height):
            return None
        if layer == 0:
            return f.ds1.wall_at(0, sx, sy)
        return f.ds1.floor_at(sx, sy)

    for y in range(gh):
        for x in range(gw):
            # ── 原版 `FillBlanks` 是**逐格**补空：该格"什么都没有"（wall 与 floor 都空）
            #    才轮到后面的块。⛔ 不是逐层补：逐层补会让别的块的**错位墙**填进参考块的
            #    空地（实测会把营地内变成一堆杂墙、并把出城口堵死）。
            src = None                                   # 命中的块（该格的整格来源）
            for i, f in enumerate(order):
                c0 = cell_of(f, x, y, 0)
                c1 = cell_of(f, x, y, 1)
                if ((c0 is not None and not c0.is_empty)
                        or (c1 is not None and not c1.is_empty)):
                    src = f
                    if i > 0:
                        filled_by_others += 1
                    break
            if src is None:
                uncovered += 1
                continue

            w0 = cell_of(src, x, y, 0)
            fl = cell_of(src, x, y, 1)
            if w0 is not None and not w0.is_empty:
                wall_src[y][x] = (src, w0)
            elif src is ref and len(ref.ds1.walls) > 1:
                # wall 层第 1 层（原版帐篷顶棚等叠层）：参考块有就直接用它
                ox, oy = offs[ref.rel]
                w1c = ref.ds1.wall_at(1, x + WIN_X0 + ox, y + WIN_Y0 + oy)
                if not w1c.is_empty:
                    wall_src[y][x] = (ref, w1c)
                    wall1_cells += 1
            if fl is not None and not fl.is_empty:
                floor_src[y][x] = (src, fl)

    # ── ★ 木桥（文件头 ③-b）：任何一块里有桥瓦片的格 → **一律用桥** ────────────
    #   为什么单开一条规则：本例的合并口径是"参考块第一 + 逐格补空"，而参考块（TownW1）
    #   在河那几格**有** floor 瓦片 ⇒ 桥（只有 TownE1 有）本来落不进来。
    #   桥的语义：有 floor 瓦片的行 = 桥面（可走）、有 wall 瓦片的行 = 栏杆（阻挡）。
    bridge_floor = [[None] * gw for _ in range(gh)]
    bridge_wall = [[None] * gw for _ in range(gh)]
    for f in files:
        fox, foy = offs[f.rel]
        for y in range(gh):
            for x in range(gw):
                sx, sy = x + WIN_X0 + fox, y + WIN_Y0 + foy
                if not (0 <= sx < f.ds1.width and 0 <= sy < f.ds1.height):
                    continue
                cw = f.ds1.wall_at(0, sx, sy)
                if cw is not None and not cw.is_empty and BRIDGE_DT1 in f.names_of(cw):
                    bridge_wall[y][x] = (f, cw)
                cf = f.ds1.floor_at(sx, sy)
                if cf is not None and not cf.is_empty and BRIDGE_DT1 in f.names_of(cf):
                    bridge_floor[y][x] = (f, cf)
    bridge_cells = [(x, y) for y in range(gh) for x in range(gw)
                    if bridge_floor[y][x] is not None or bridge_wall[y][x] is not None]
    bridge_deck = [(x, y) for y in range(gh) for x in range(gw)
                   if bridge_floor[y][x] is not None and bridge_wall[y][x] is None]
    if not bridge_cells:
        raise SystemExit('合并结果里没有木桥（bridge.dt1）⇒ 桥丢了，检查 bridge 的 pick 规则')

    kinds = [['.' for _ in range(gw)] for _ in range(gh)]
    ground = [[EMPTY_CELL for _ in range(gw)] for _ in range(gh)]
    objects = [[EMPTY_CELL for _ in range(gw)] for _ in range(gh)]
    warp_dropped = 0
    river_cells = []

    for y in range(gh):
        for x in range(gw):
            fw = wall_src[y][x]
            ff = floor_src[y][x]
            wall_is_warp = False

            # ★ 木桥优先（只有它这几格会走到这个分支，见上面的注释）
            bw = bridge_wall[y][x]
            bf = bridge_floor[y][x]
            if bw is not None or bf is not None:
                if bw is not None:
                    fi, cell = bw
                    objects[y][x] = _key_of(fi, cell, pack_id) or EMPTY_CELL
                    kinds[y][x] = 's'            # 栏杆（石） = 阻挡
                else:
                    kinds[y][x] = 'd'            # 桥面 = 可走
                if bf is not None:
                    fi, cell = bf
                    ground[y][x] = _key_of(fi, cell, pack_id) or EMPTY_CELL
                continue

            if fw is not None:
                fi, cell = fw
                names = fi.names_of(cell)
                if 'warp.dt1' in names:
                    # 原版 `BARRACKS/warp.dt1` 的瓦片**不画**（编辑器留下的孤儿传送标记：
                    # 整张 dt1 只有唯一调色板值 = 纯色填充菱形；`LvlTypes.txt` 的
                    # `Act 1 - Town` 那 10 个 dt1 槽位里没有 warp.dt1 ⇒ 原版引擎按 lvltype
                    # 装载瓦片库时解析不到它 ⇒ 原版这几格本来就不画东西）。
                    wall_is_warp = True
                    warp_dropped += 1
                else:
                    objects[y][x] = _key_of(fi, cell, pack_id) or EMPTY_CELL
                    kinds[y][x] = _kind_of_wall(names, cell)

            if ff is not None:
                fi, cell = ff
                ground[y][x] = _key_of(fi, cell, pack_id) or EMPTY_CELL
                names = fi.names_of(cell)
                if wall_is_warp or fw is None:
                    if 'river.dt1' in names:
                        kinds[y][x] = 'r'                     # 水（河/水塘）= 阻挡
                    elif fw is None:
                        kinds[y][x] = 'd' if not fi.grass_of(cell) else '.'
                    if kinds[y][x] == 'r':
                        river_cells.append((x, y))

    # ── 四块都没覆盖到的格 = **原版那几格没有瓦片**（实测只有西北角 3×10 的一小块）────
    #    ⛔ 不许给它编地面：原版不画；也不许当可走（原版没有瓦片 ⇒ 没有 walk 标志 ⇒ 不可走）。
    #    ⇒ 记成 kind='v'（`MapGenTown.KindOf` 映到 `TileKind.Void`：不画、不可走）。
    void_cells = []
    for y in range(gh):
        for x in range(gw):
            if ground[y][x] == EMPTY_CELL and objects[y][x] == EMPTY_CELL:
                kinds[y][x] = 'v'
                void_cells.append((x, y))
    if len(void_cells) != uncovered:
        raise SystemExit('内部不一致：uncovered=%d 与逐格扫描出的空瓦片格 %d 不等'
                         % (uncovered, len(void_cells)))

    # ── 围栏环 / 出城口：**以参考块自己那几格为准**（它的缺口就是原版的出城口）────
    #    ⛔ 不按合并结果反推：其它块是同一营地按别的原点导出的，它们的栅栏会**错位盖到**
    #       出城口上（实测能把缺口堵死 ⇒ 城里没有出口）。所以缺口口径固定取参考块。
    ref_fence = set()
    ox, oy = offs[ref.rel]
    for ry in range(ref.ds1.height):
        for rx in range(ref.ds1.width):
            c = ref.ds1.wall_at(0, rx, ry)
            if not c.is_empty and 'fence.dt1' in ref.names_of(c):
                # 块本地 → **关卡窗口坐标**（= 合并帧 − 窗口原点）
                ref_fence.add((rx - ox - WIN_X0, ry - oy - WIN_Y0))
    ring_rows = sorted(set(p[1] for p in ref_fence))
    ring_cols = sorted(set(p[0] for p in ref_fence))
    ring_top, ring_bottom = min(ring_rows), max(ring_rows)
    ring_left, ring_right = min(ring_cols), max(ring_cols)
    span_x = [x for x in range(ring_left, ring_right + 1)]
    span_y = [y for y in range(ring_top, ring_bottom + 1)]
    miss_n = [x for x in span_x if (x, ring_top) not in ref_fence]
    miss_s = [x for x in span_x if (x, ring_bottom) not in ref_fence]
    miss_w = [y for y in span_y if (ring_left, y) not in ref_fence]
    miss_e = [y for y in span_y if (ring_right, y) not in ref_fence]
    if miss_n or miss_s:
        raise SystemExit('参考块围栏环的南北边不连续（缺 N=%s S=%s）⇒ 它不是"围栏围出的营地"'
                         % (miss_n, miss_s))
    if not (1 <= len(miss_w) <= 5):
        raise SystemExit('参考块西侧栅栏缺口异常（缺 %s）⇒ 出城口找不到' % (miss_w,))

    gate_cells = [(ring_left, y) for y in miss_w]
    for gx, gy in gate_cells:
        kinds[gy][gx] = 'x'
        objects[gy][gx] = EMPTY_CELL     # 出城口不画东西（别的块的错位栅栏也不许画上去）
    gate_x = ring_left
    gate_y = sorted(p[1] for p in gate_cells)[len(gate_cells) // 2]

    spawn, reach = _pick_points(kinds, gate_x, gate_y,
                                (ring_left, ring_top, ring_right, ring_bottom))
    npcs = _original_npcs(ref, offs[ref.rel], kinds)

    rx0 = min(c[0] for c in river_cells) if river_cells else -1
    rx1 = max(c[0] for c in river_cells) if river_cells else -1
    ry0 = min(c[1] for c in river_cells) if river_cells else -1
    ry1 = max(c[1] for c in river_cells) if river_cells else -1
    fence_n = sum(1 for y in range(gh) for x in range(gw) if kinds[y][x] == 'f')
    print('  参考块围栏环：行 y[%d..%d] 列 x[%d..%d]（%d 格）'
          % (ring_top, ring_bottom, ring_left, ring_right, len(ref_fence)))
    print('    北边缺 %s / 南边缺 %s（都应为空）' % (miss_n, miss_s))
    print('    西边缺 %s ⇒ 出城口（%d 格，x=%d）' % (miss_w, len(gate_cells), gate_x))
    print('    东边缺 %s（朝河一侧原版就没有栅栏，只有两端的短桩）' % miss_e)
    print('  合并后 kind==f 的格：%d' % fence_n)
    print('  由其它块补空的格：%d（★ 片 4 起**正常 > 0**：窗口西侧/北侧那两条带只在'
          '非参考块里有内容，正是出城口外面那片地）' % filled_by_others)
    print('  窗口内"四块都没有瓦片"的格：%d（原版那几格本来就不画；已记成 kind=v = Void：'
          '不画且不可走。实测只在西北角 %s）'
          % (uncovered, ('x[%d..%d] y[%d..%d]' % (min(c[0] for c in void_cells),
                                                   max(c[0] for c in void_cells),
                                                   min(c[1] for c in void_cells),
                                                   max(c[1] for c in void_cells)))
             if void_cells else '（无）'))
    print('  wall 层第 1 层（原版叠层，如帐篷顶棚）：%d 格' % wall1_cells)
    print('  水（river.dt1，阻挡）：%d 格 x[%d..%d] y[%d..%d]'
          % (len(river_cells), rx0, rx1, ry0, ry1))
    print('  warp 标记格：%d 格（wall 层不画，见 build() 注释）' % warp_dropped)
    if bridge_cells:
        bxs = [c[0] for c in bridge_cells]
        bys = [c[1] for c in bridge_cells]
        dw = [c[0] for c in bridge_deck]
        print('  ★ 木桥（bridge.dt1，来自 %s）：%d 格 x[%d..%d] y[%d..%d]'
              % (', '.join(sorted(set(os.path.basename(bridge_floor[c[1]][c[0]][0].rel)
                                      for c in bridge_cells if bridge_floor[c[1]][c[0]]))),
                 len(bridge_cells), min(bxs), max(bxs), min(bys), max(bys)))
        print('     桥面（可走）= %d 格 x[%d..%d]、栏杆（阻挡）= %d 格'
              % (len(bridge_deck), min(dw), max(dw), len(bridge_cells) - len(bridge_deck)))
    print('  出生点 %s（8 邻全可走、在围栏环内）  NPC %s  可达格 %d' % (spawn, npcs, len(reach)))

    if not river_cells:
        raise SystemExit('合并结果里没有水（river.dt1）⇒ 营地外的河丢了，检查对齐偏移')
    reachable_gates = [g for g in gate_cells if g in reach]
    if not reachable_gates:
        raise SystemExit('出城口不可达（出生点走不到城门）⇒ 合并结果不对')

    if debug:
        print('  ── kind（. 草 / d 泥 / f 栅栏 / t 树 / s 石矮墙 / o 摊位 / r 水 / x 出口）──')
        for row in kinds:
            print('    ' + ''.join(row))
        n_ground = sum(1 for r in ground for c in r if c != EMPTY_CELL)
        n_obj = sum(1 for r in objects for c in r if c != EMPTY_CELL)
        print('  逐格瓦片：ground %d 格 / object %d 格（总 %d 格）'
              % (n_ground, n_obj, gw * gh))
        # 有 kind（该格被判为栅栏/树/石墙/摊位/水）但**取不到可渲染瓦片**的格必为 0
        no_tile = []
        for y in range(gh):
            for x in range(gw):
                if kinds[y][x] == 'r' and ground[y][x] == EMPTY_CELL:
                    no_tile.append((x, y, 'r', 'ground'))
                if kinds[y][x] in ('f', 't', 's', 'o') and objects[y][x] == EMPTY_CELL:
                    no_tile.append((x, y, kinds[y][x], 'object'))
        print('  !! 有 kind 但取不到原版瓦片的格：%d 个 %s'
              % (len(no_tile), no_tile[:20]))

    _write_cs(out_path, kinds, ground, objects, packs, spawn, npcs, gw, gh, rels, ref_rel, offs)
    print('  → %s' % out_path)
    return 0


def _original_npcs(ref, off, kinds):
    """参考块里 `kind=1` 的预设单位 → 本项目 5 个 NPC 的**关卡坐标**（文件头 ③-a）。

    返回顺序 = `(int)Def.NpcId`（0 阿卡拉 / 1 卡夏 / 2 恰西 / 3 基德 / 4 瓦瑞夫）。
    取不到 / 落点不可走 ⇒ 直接报错退出（这是"依据"链断了，不能静默退回启发式）。
    """
    names = monpreset_act1()
    ox, oy = off
    found = {}
    for o in ref.ds1.objects:
        if o.obj_type != 1:                       # 1 = 怪物/NPC，2 = 物件（preset.zig:488-494）
            continue
        if o.obj_id < 0 or o.obj_id >= len(names):
            print('  [WARN] %s 的怪物预设单位 id=%d 越出 Act 1 MonPreset 块（%d 行）⇒ 忽略'
                  % (os.path.basename(ref.rel), o.obj_id, len(names)))
            continue
        place = names[o.obj_id]
        if place not in NPC_PLACES:
            continue
        tx = o.x // SUBTILES_PER_TILE - ox - WIN_X0
        ty = o.y // SUBTILES_PER_TILE - oy - WIN_Y0
        if place in found:
            continue                              # 同名的第二份：忽略（留一行日志即可）
        found[place] = (tx, ty)

    out = []
    for place in NPC_PLACES:
        if place not in found:
            raise SystemExit('参考块 %s 里找不到 NPC %r 的预设单位（DS1 被换了？）'
                             % (os.path.basename(ref.rel), place))
        tx, ty = found[place]
        gx, gy = tx, ty
        if not (0 <= gy < len(kinds) and 0 <= gx < len(kinds[0])):
            raise SystemExit('NPC %r 的关卡坐标 (%d,%d) 越界' % (place, gx, gy))
        if kinds[gy][gx] not in ('.', 'd', 'x'):
            raise SystemExit('NPC %r 的关卡坐标 (%d,%d) 落在阻挡格 %r 上（换算口径不对？）'
                             % (place, gx, gy, kinds[gy][gx]))
        out.append((gx, gy))
    if len(set(out)) != len(out):
        raise SystemExit('5 个 NPC 的关卡坐标有重合：%s' % (out,))
    return out


def _pick_points(kinds, gate_x, gate_y, ring):
    """从出城口内侧 BFS 求可达区；出生点只从**围栏环内**的可达格里挑（原版出生点在营地里）。

    返回 (spawn, reach)；NPC 站位不在这里挑（走 `_original_npcs` 的原版坐标）。
    """
    from collections import deque
    gw = len(kinds[0])
    gh = len(kinds)
    walkable = ('.', 'd', 'x')
    ring_left, ring_top, ring_right, ring_bottom = ring

    start = None
    for dx in (1, 2, 3):
        if gate_x + dx < gw and kinds[gate_y][gate_x + dx] in walkable:
            start = (gate_x + dx, gate_y)
            break
    if start is None:
        raise SystemExit('找不到出城口内侧的可走格（DS1 布局变了？）')

    seen = {start}
    q = deque([start])
    while q:
        x, y = q.popleft()
        for nx, ny in ((x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1)):
            if nx < 0 or ny < 0 or nx >= gw or ny >= gh:
                continue
            if (nx, ny) in seen or kinds[ny][nx] not in walkable:
                continue
            seen.add((nx, ny))
            q.append((nx, ny))

    def clear8(p):
        x, y = p
        for dx in (-1, 0, 1):
            for dy in (-1, 0, 1):
                nx, ny = x + dx, y + dy
                if nx < 0 or ny < 0 or nx >= gw or ny >= gh or kinds[ny][nx] not in walkable:
                    return False
        return True

    safe = [p for p in seen if clear8(p)]
    if not safe:
        raise SystemExit('没有 8 邻全可走的可达格（营地被切碎了？）')

    # 出生点只在**围栏环内**的可达格里挑（原版出生点在营地中央，不在营地外的河对岸）
    inside = [p for p in safe
              if ring_left < p[0] < ring_right and ring_top < p[1] < ring_bottom]
    if not inside:
        print('  [WARN] 围栏环内没有 8 邻全可走的可达格 ⇒ 出生点退回"全部可达格"里挑')
        inside = safe

    cx = sum(p[0] for p in inside) / float(len(inside))
    cy = sum(p[1] for p in inside) / float(len(inside))
    spawn = min(inside, key=lambda p: (p[0] - cx) ** 2 + (p[1] - cy) ** 2)
    return spawn, seen


HEADER = '''// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · Module/Map/MapGenTownLayout.cs
// ⚠️ **本文件是生成物，禁止手改**（生成器：`tools/d2codec/export_town_layout.py`）。
//
// 数据来源 = **原版** `data/global/tiles/ACT1/TOWN/*.ds1`（Blizzard North, 2000，
//   取自 d2data.mpq；本项目非商用）。块清单与关卡尺寸**都读原版规则表**：
//     · `Levels.txt`「Act 1 - Town」⇒ 关卡 %d×%d（LevelName = Rogue Encampment）
//     · `LvlPrest.txt`「Act 1 - Town 1」⇒ %d 块：%s
//   四块是**同一座营地**按不同原点导出的（实测锚点 = 围栏环左上角；用 `warp.dt1`
//   的 3 个标记格复验：对齐后逐格重合）⇒ 本项目按实测偏移对齐后**逐格补空**合并
//   （对应原版 `FillBlanks=1` 的语义），得到的就是原版那一座营地。
//   实测偏移（相对参考块 %s）：%s
//
// ★ 片 4（**关卡窗口原点修正**）：本表的格子坐标 = **关卡窗口**，不是参考块本地帧。
//   窗口 = 合并帧 x∈[%d..%d] × y∈[%d..%d]（56×40，= `Levels.txt` 的 SizeX/SizeY）。
//   为什么修：窗口取参考块本地 0 时，地图西边界正好压在**营地西侧围栏**那一列上
//   ⇒ 出城口（西侧 3 格缺口）落在**地图边界**上，游戏里看过去出口外面什么都没有
//   （用户报的"营地出口是黑色的一个口"）。
//   原版关卡 56 列 = 营地本体 **39 列**（围栏西侧列 → 木桥东端列）+ **出城口外面 17 列**；
//   行同理：营地南围栏那行就是关卡南边界（原版 `OutRoom.zig:265/268` 把
//   `LvlPrest`「Act 1 - Town 1 Transition S」铺在**野外关卡**的北边界 ⇒ 城镇南边界即接缝）。
//   ⇒ 出城口现在是**内陆格**，它西边那 17 列铺的是出城口外面那片地（逐格取自
//     `TownE1/TownS1` 的块内容，全是原版瓦片）。三条依据写在生成器
//     `tools/d2codec/export_town_layout.py` 的 `WIN_X0/WIN_Y0` 注释里。
//
// 三张表：
//   Rows[y][x]       每格 kind：'.' 草 / 'd' 泥 / 'f' 栅栏 / 't' 树 / 's' 石矮墙 /
//                                'o' 摊位帐篷 / 'r' **水（阻挡）** / 'x' 出城口 /
//                                'v' 四块都没瓦片的空角（⇒ `TileKind.Void`：不画 + 不可走）
//   GroundRows[y]    每格 **原版 floor 层**的瓦片键（6 字符一组：3 位 packId + 3 位瓦片序号）
//   ObjectRows[y]    每格 **原版 wall 层**的瓦片键（同上；`------` = 该格没有墙层瓦片）
//   packId → `Packs[packId]`，瓦片文件 = `Resources/Clover/D2/{Tiles,Objects}/<pack>/<idx>.png`
//   Spawn / Npcs     出生点（**围栏环内**、8 邻全可走）；5 个 NPC = **原版坐标**
//                    （`TownW1.ds1` 的 kind=1 预设单位 + `MonPreset.txt` Act 1 块）
//   **桥**：`bridge.dt1` 的格一律用桥（只有 `TownE1.ds1` 有）—— 桥面 = `'d'`（可走）、
//          栏杆 = `'s'`（阻挡）。见生成器 `export_town_layout.py` 文件头 ③-b。
//
// ⚠️ **尺寸**：本表宽高 = 原版关卡尺寸（%d×%d）；`GameConst.TownWidth/Height`
//   **已同步为同值**（agent-42 按主 agent 裁决改），两者不一致时 `MapGenTown` 直接报错。
//   32×32 是更早一版"只取 TownW1 的营地本体"时的裁切值，会把营地外的河裁掉。
// ─────────────────────────────────────────────────────────────────────────────
'''

PACK_HELP = '''        /// <summary>pack 目录名（下标 = `GroundRows` / `ObjectRows` 里的 3 位 packId）。</summary>
        public static readonly string[] Packs =
        {
%s
        };
'''


def _encode_rows(table):
    return ['            "%s",' % ''.join(cell for cell in row) for row in table]


def _write_cs(out_path, kinds, ground, objects, packs, spawn, npcs, gw, gh, rels, ref_rel, offs):
    offset_txt = ', '.join('%s(%+d,%+d)' % (os.path.basename(r), offs[r][0], offs[r][1])
                           for r in rels)
    lines = [HEADER % (gw, gh, len(rels), ', '.join(os.path.basename(r) for r in rels),
                       os.path.basename(ref_rel), offset_txt,
                       WIN_X0, WIN_X0 + gw - 1, WIN_Y0, WIN_Y0 + gh - 1, gw, gh),
             'using UnityEngine;', '',
             'namespace Diablo2.Module.Map', '{',
             '    /// <summary>原版罗格营地固定布局 + 逐格原版瓦片（生成物，见文件头）。</summary>',
             '    internal static class MapGenTownLayout', '    {',
             '        /// <summary>网格宽（= 原版 `Levels.txt` 的 SizeX）。</summary>',
             '        public const int Width = %d;' % gw, '',
             '        /// <summary>网格高（= 原版 `Levels.txt` 的 SizeY）。</summary>',
             '        public const int Height = %d;' % gh, '',
             '        /// <summary>原版块清单（`LvlPrest.txt`「Act 1 - Town 1」的 File1..FileN）。</summary>',
             '        public static readonly string[] SourceDs1 =', '        {']
    for r in rels:
        lines.append('            "%s",' % r)
    lines += ['        };', '',
              '        /// <summary>尺寸出处（日志/自检用）。</summary>',
              '        public const string SizeSource = "Levels.txt Name=Act 1 - Town 的 SizeX/SizeY";', '',
              '        /// <summary>%d 行 kind（`Rows[y][x]`）。</summary>' % gh,
              '        public static readonly string[] Rows =', '        {']
    lines += _encode_rows(kinds)
    lines += ['        };', '']
    lines += (PACK_HELP % '\n'.join('            "%s",' % p for p in packs)).rstrip('\n').split('\n')
    lines += ['',
              '        /// <summary>%d 行「floor 层原版瓦片」（每格 6 字符；见文件头）。</summary>' % gh,
              '        public static readonly string[] GroundRows =', '        {']
    lines += _encode_rows(ground)
    lines += ['        };', '',
              '        /// <summary>%d 行「wall 层原版瓦片」（每格 6 字符；`------` = 无）。</summary>' % gh,
              '        public static readonly string[] ObjectRows =', '        {']
    lines += _encode_rows(objects)
    lines += ['        };', '',
              '        /// <summary>出生点（原版营地中央的净空格；生成器保证 8 邻全可走）。</summary>',
              '        public static readonly Vector2Int Spawn = new Vector2Int(%d, %d);'
              % (spawn[0], spawn[1]), '',
              '        /// <summary>5 个 NPC 站位 = **原版坐标**，顺序 = (int)Def.NpcId',
              '        /// （0 阿卡拉 / 1 卡夏 / 2 恰西 / 3 基德 / 4 瓦瑞夫）。',
              '        /// <para>出处：原版 `TownW1.ds1` 的 objects 层里 kind=1（怪物/NPC）预设单位，',
              '        /// `id` 索引 `MonPreset.txt` 的 Act 1 块（0=gheed / 2=akara / 5=kashya /',
              '        /// 7=warriv1 / 8=charsi）；子格坐标 ÷5 换算成格。逐条依据见',
              '        /// `tools/d2codec/export_town_layout.py` 文件头 ③-a。</para></summary>',
              '        public static readonly Vector2Int[] Npcs =', '        {']
    for p in npcs:
        lines.append('            new Vector2Int(%d, %d),' % (p[0], p[1]))
    lines += ['        };', '',
              '        /// <summary>',
              '        /// 取某格的**原版瓦片键**（`ResPaths.Tile` / `ObjectSprite` 的相对路径）。',
              '        /// 返回 false = 该格不在原版布局范围内（调用方回退到按 `TileKind` 分类的默认瓦片）。',
              '        /// </summary>',
              '        /// <param name="x">格 x（0..Width-1）。</param>',
              '        /// <param name="y">格 y（0..Height-1）。</param>',
              '        /// <param name="groundKey">floor 层瓦片键（可能为空串 = 原版这格没铺地面）。</param>',
              '        /// <param name="objectKey">wall 层瓦片键（空串 = 这格没有墙/物件）。</param>',
              '        public static bool TryGetTiles(int x, int y, out string groundKey, out string objectKey)',
              '        {',
              '            groundKey = null;',
              '            objectKey = null;',
              '            if (x < 0 || y < 0 || x >= Width || y >= Height) return false;',
              '',
              '            groundKey = Decode(GroundRows[y], x);',
              '            objectKey = Decode(ObjectRows[y], x);',
              '            return true;',
              '        }',
              '',
              '        /// <summary>解一行里第 `x` 格的 6 字符编码（`------` → 空串）。</summary>',
              '        private static string Decode(string row, int x)',
              '        {',
              '            var off = x * 6;',
              '            if (row == null || off + 6 > row.Length) return "";',
              '            if (row[off] == \'-\') return "";',
              '',
              '            var packId = (row[off] - \'0\') * 100 + (row[off + 1] - \'0\') * 10',
              '                + (row[off + 2] - \'0\');',
              '            if (packId < 0 || packId >= Packs.Length) return "";',
              '            return Packs[packId] + "/" + row.Substring(off + 3, 3);',
              '        }',
              '    }', '}', '']

    with open(out_path, 'w', encoding='utf-8', newline='\r\n') as fh:
        fh.write('\n'.join(lines))


def main(argv):
    out = argv[argv.index('--out') + 1] if '--out' in argv else DEFAULT_OUT
    print('从原版预设块抽取罗格营地布局 + 逐格瓦片（块清单/尺寸都读原版规则表）')
    return build(out, '--debug' in argv)


if __name__ == '__main__':
    sys.exit(main(sys.argv))
