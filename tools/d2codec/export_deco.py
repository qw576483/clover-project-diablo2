# -*- coding: utf-8 -*-
"""把原版 DS1 `objects` 层里 **kind=2（物件预设单位）** 的本体动画解成 PNG 落进工程，
并生成物件类表 `client/Assets/Scripts/Module/Map/MapGenDeco.cs`（生成物，禁止手改）。

── ① `id` 是什么（**不是 `Objects.txt` 的行号 / Id**）────────────────────────────
  DS1 的 kind=2 单位：`id` 是**该幕 objpreset 表的下标**（`Index` 列），按 `Act` 取行。
  该表在经典 1.10 的包里**没有 txt**（`D2data.mpq` / `Patch_D2.mpq` / `d2char.mpq` /
  `d2sfx.mpq` 四包按名取 `data\\global\\excel\\ObjPreset.txt` 全部为 0 字节；
  1.10f 的 txt 包 `d2lod1.10txt-1.10f` 里也没有），也没有 `.bin`。
  本文件用的是 **D2R 的官方数据表**（列 = `Index` / `Act` / `ObjectClass`）：

      https://raw.githubusercontent.com/pinkufairy/D2R-Excel/main/base/objpreset.txt

  它能替代经典表的**三条依据**（都可在本文件跑一遍复算）：
    (a) **下标自证**：Act 1 的 `Index 52` = `WaypointOutsideAct1`；而实测
        `ACT1/TOWN/TownW1.ds1` 的 kind=2 单位 `id=52` 的子格坐标 (84,69) 正好落在
        五芒星石台（`floor.dt1` m=0 s=52..55 的四块地砖）上
        ⇒ 与 `MapGenTown.Waypoint` 同一处（同一依据链见该常量注释）。
        若两表不同源，这一条命中的概率 ≈ 1/113。
    (b) **同名同 Id 同 Token**：D2R 的 `objects.txt`（同一仓库）里，
        `TikiTorch1`→`TO`/Id 37、`RogueBonfire`→`RB`/39、`Standard1`→`N1`/35、
        `Standard2`→`N2`/36、`Bank`→`b6`/267、`WaypointOutsideAct1`→`wp`/119
        —— 与经典 1.10 `data/global/excel/objects.txt` 的 `Id`/`Token` **逐行相等**。
        `CLASS_TO_OBJ` 的每条都按此法逐条核对过（D2R 的 `Class`→`*ID`/`Token` 取回经典同名
        那行的 `Id`/`Token`/`Name`/`Draw`，四个字段逐行相等），每条的理由写在它的值里。
    (c) **石头段**：Act 1 的 `Index 6..11` = `StoneAlpha..StoneTheta`，
        与经典 `ObjType.txt` 的 `Stone 1..6`（Token `S1..S6`）**同序同数**。

  ⛔ 想换掉这张表 ⇒ 先证伪 (a)(b)(c)，否则每条 id 判定都会跟着错。

── ② 每个 `id` 判了什么物件（Act 1；本批实际用到的）────────────────────────────
  见本文件 `DECO_IDS` 的每行注释 + 生成物 `MapGenDeco.cs` 的表头。

── ③ 取哪个模式 / 播多快（**都读原版表，不写死**）──────────────────────────────
  · 模式：取**该物件第一个「帧数 > 1 且 `CycleAnim=1`」的模式**（= 它自带的那段
    循环动画），`FrameCnt=0` 的模式按 `objects.txt` 的语义跳过。全部模式都不循环
    （如 `Bank` 只有 NU 1 帧）⇒ 静态，只出第 0 帧。
    出处：`objects.txt` 的 8 组 `FrameCnt#/FrameDelta#/CycleAnim#/Mode#` 列；
    列义（`FrameCnt` = 该模式帧数、`FrameDelta` = 每帧推进量 1/256、`CycleAnim` = 播完是否
    循环、`Mode#` = 该模式是否存在）见 D2R 数据文档 `files/objects.html`。
  · 帧率：`25 × FrameDelta / 256`（换算两条出处：参考实现 `frameDuration = 256/25/FrameDelta`
    + 逐帧播放 ⇒ `fps = 1/frameDuration`；与 `ResPaths.WaypointFrameFps` 同一把尺子，
    见 `export_waypoint.py` 的 `frame_rate_of`）。
  · 尺寸/落位：不缩放、不重采样、不调色；画布 = **本次导出的那几帧**各层包围盒并集
    + 底部透明垫片，使**原点距画布底边 40 px**（= 一格菱形半高 `GameConst.IsoHalfH`
    × 80 px/单位），与 `export_waypoint.py` 同一口径；画布外的层像素裁掉（不缩放）。

── ④ 只搬被引用的那几个 ───────────────────────────────────────────────────────
  导出的 token 清单 = `DECO_IDS` 里 `Draw != 0` 的那些类（`objects.txt` 的 `Draw` 列；
  `Draw=0` 的原版就不画，跳过）。

── ⑤ 两个布局导出器共用本文件的 id 判定 ───────────────────────────────────────
  罗格营地（`export_town_layout.py`）与野外（`export_wild_layout.py`）都从
  DS1 的 `objects` 层取 kind=2 单位，一律走本文件的
  `exportable_kinds()`（可导出 id → 物件描述）与 `units_of()`（单位 → 格 + ds1 id）——
  ⛔ id 判定表只有本文件这一份，别处不复制；判不出的 id 由调用方按 id 汇总点名（口径见
  `DECO_IDS` 上方的约定：留空、不猜）。

用法：
    python tools/d2codec/export_deco.py [--out <Resources/Clover/D2 根>] [--cs-out <MapGenDeco.cs>]
                                        [--mpq-dir <含 D2data.mpq 的目录>] [--only TO,RB]
"""

import argparse
import hashlib
import json
import os
import sys

try:
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')
except Exception:
    pass

try:
    from . import cof as cofmod
    from . import dcc as dccmod
    from . import pngio
    from . import storm
except ImportError:
    import cof as cofmod
    import dcc as dccmod
    import pngio
    import storm

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

#: 单位调色板（D2 物件 DCC 用的那一份；与 `export_waypoint.py` 同源）。
PALETTE_REL = 'data/global/palette/units/Pal.dat'
#: 官方物件表（帧数 / Token / Draw 都从它读，不写死）。
OBJECTS_REL = 'data/global/excel/objects.txt'
#: 经典 ObjType.txt（Name/Token/Beta；用来把 objpreset 的 `ObjectClass` 名对回经典行）。
OBJTYPE_REL = 'data/global/excel/ObjType.txt'
#: 武器类别段（物件一律 hth）。
WEAPON_CLASS = 'HTH'
#: 装备段（物件只有 `lit` 这一种变体；出处 = 包内文件名本身，见 `export_waypoint.py`）。
EQUIP = 'lit'
#: 一格菱形的半高（px）：`GameConst.IsoHalfH` = 0.5 世界单位 × 80 px/单位。
HALF_CELL_PX = 40
#: 模式代号（下标 ↔ 代号：0=NU / 1=OP / 2=ON；出处 `ObjMode.txt` 的行序）。
MODE_CODES = ('NU', 'OP', 'ON')
#: `objects.txt` 的 Act 列是**位掩码**（1=act1 / 2=act2 / 4=act3 / 8=act4 / 16=act5 / 15=act1..4）。
ACT_BIT = {1: 1, 2: 2, 3: 4, 4: 8, 5: 16}

# ── objpreset（Act 1 段）────────────────────────────────────────────────────────
# 逐行照录 `ObjPreset.txt` 的 Act=1 段（列 = Index / Act / ObjectClass），来源见文件头 ①。
OBJPRESET_ACT1 = {
    0: 'Fountain', 1: 'TikiTorch1', 2: 'RogueBonfire', 3: 'Standard1', 4: 'Standard2',
    5: 'LargeChestR', 6: 'StoneAlpha', 7: 'StoneBeta', 8: 'StoneGamma', 9: 'StoneDelta',
    10: 'StoneLambda', 11: 'StoneTheta', 12: 'InifussTree', 13: 'Ripple4', 14: 'Ripple4',
    15: 'Ripple3', 16: 'Ripple3', 17: 'Brazier', 18: 'BloodFountain', 19: 'Candles1',
    20: 'Candles2', 21: 'TikiTorch1', 22: 'StoneSoundOnly', 23: 'RiverSoundOnly1',
    24: 'RiverSoundOnly2', 25: 'TowerTome', 26: 'Gibbet', 27: 'HoleInGround',
    28: 'BubblingBloodPool', 29: 'DeerShrine', 30: 'ForestAltar', 31: 'HealingWell',
    32: 'HornShrine', 33: 'Town1Ambient', 34: 'StoneSoundOnly', 35: 'Flies',
    36: 'Malus', 37: 'WaypointOutsideAct1', 38: 'PlaceUniqueChest', 39: 'Fountain5',
    40: 'HiddenStash', 41: 'HidingSpot', 42: 'Log', 43: 'FireSmall', 44: 'FireMedium',
    45: 'FireLarge', 46: 'ArmorStand1', 47: 'ArmorStand2', 48: 'WeaponRack1',
    49: 'WeaponRack2', 50: 'Bookshelf1', 51: 'Bookshelf2', 52: 'WaypointOutsideAct1',
    53: 'WaypointInsideAct1', 54: 'Bed1Act1', 55: 'Bed2Act1', 56: 'Rock', 57: 'RockC',
    58: 'RockD', 59: 'ChestOutdoor1', 60: 'ChestOutdoor2', 61: 'ChestOutdoor3',
    62: 'ChestOutdoor4', 63: 'LargeChestL', 64: 'Chest5', 65: 'Chest6', 66: 'Chest7',
    67: 'RogueCorpse1', 68: 'RogueCorpse2', 69: 'RogueRollingCorpse1',
    70: 'RogueStakedCorpse1', 71: 'RogueStakedCorpse2', 72: 'CorpseSkeleton',
    73: 'GuarDonaStick', 74: 'BurnedBody1Act1', 75: 'BurningBody2Act1',
    76: 'ExplodingCow', 77: 'Fountain1', 78: 'Fountain8', 79: 'Fountain6',
    80: 'ManaWell1', 81: 'ManaWell2', 82: 'ShrineAltar', 83: 'BullShrine',
    84: 'STeleShrine', 85: 'Act1CathedralShrine', 86: 'Act1JailShrine',
    87: 'Act1JailHealthShrine', 88: 'Act1JailManaShrine', 89: 'Casket1', 90: 'Casket2',
    91: 'Casket3', 92: 'Casket4', 93: 'Casket5', 94: 'Casket6', 95: 'Barrel',
    96: 'Crate', 97: 'WallTorch1', 98: 'StoolAct1', 99: 'Woodpile1Act1',
    100: 'Woodpile2Act1', 101: 'HiddenDoor2', 102: 'Bank', 103: 'WIRT', 104: 'GoldProxy',
    105: 'PlaceRandomTreasureChest', 106: 'HellLight1', 107: 'HellLight2',
    108: 'HellLight3', 109: 'Fog', 110: 'CainAct1Start', 111: 'ConsolationChest',
    112: 'PermanentPortal2',
}

# ── `ObjectClass` → 经典 1.10 `ObjType.txt` 的行（本批用到的 8 条）──────────────
# 键 = objpreset 的 `ObjectClass`，值 = (经典 ObjType.txt 的行号即 objects.txt 的 Id,
# 经典名, 经典 Token, 判定依据)。
# 依据写法：`D2R objects.txt` 的 `* Class`→`ID/Token` 与经典 `objects.txt` 逐行相等
# （见文件头 ①(b)）；同名无法直译的那两条另给依据。
CLASS_TO_OBJ = {
    'TikiTorch1': (37, 'Dummy', 'TO', 'D2R objects.txt: TikiTorch1/ID37/TO = 经典 Id37'),
    'RogueBonfire': (39, 'fire', 'RB', 'D2R objects.txt: RogueBonfire/ID39/RB = 经典 Id39'),
    'Standard1': (35, 'Dummy', 'N1', 'D2R objects.txt: Standard1/ID35/N1 = 经典 Id35'),
    'Standard2': (36, 'Dummy', 'N2', 'D2R objects.txt: Standard2/ID36/N2 = 经典 Id36'),
    'Bank': (267, 'bank', 'b6', 'D2R objects.txt: Bank/ID267/b6 = 经典 Id267'),
    'WaypointOutsideAct1': (119, 'Waypoint', 'wp',
                            'D2R objects.txt: WaypointOutsideAct1/ID119/wp = 经典 Id119'),
    'Town1Ambient': (78, 'Town Ambient', 'SS',
                     'D2R objects.txt: Town1Ambient/ID78/SS = 经典 Id78（Draw=0）'),
    'CainAct1Start': (385, 'Dummy', 'ss',
                      '经典 ObjType.txt 行386「cain start」/ss（唯一同名行；objects.txt '
                      'Id385 Draw=0 的原版就不画）；D2R 的 objects.txt 无该类行'),
    # ── 野外 / 洞穴用到的类（依据同上：D2R objects.txt 的 `Class`→`*ID`/`Token` 与经典
    #    `objects.txt` 的 `Id`/`Token`/`Name` 逐行相等；objpreset 的 `Index` 见
    #    `OBJPRESET_ACT1`）──────────────────────────────────────────────────────
    'Barrel': (7, 'Barrel', 'B1', 'D2R objects.txt: Barrel/ID7/B1 = 经典 Id7'),
    'RogueCorpse1': (54, 'RogueCorpse', 'Z1',
                     'D2R objects.txt: RogueCorpse1/ID54/Z1 = 经典 Id54'),
    'RogueCorpse2': (55, 'RogueCorpse', 'Z2',
                     'D2R objects.txt: RogueCorpse2/ID55/Z2 = 经典 Id55'),
    'RogueRollingCorpse1': (56, 'RogueCorpse', 'Z5',
                            'D2R objects.txt: RogueRollingCorpse1/ID56/Z5 = 经典 Id56'),
    'RogueStakedCorpse1': (57, 'CorpseOnStick', 'Z3',
                           'D2R objects.txt: RogueStakedCorpse1/ID57/Z3 = 经典 Id57'),
    'RogueStakedCorpse2': (58, 'CorpseOnStick', 'Z4',
                           'D2R objects.txt: RogueStakedCorpse2/ID58/Z4 = 经典 Id58'),
    'ForestAltar': (81, 'Shrine', 'AF',
                    'D2R objects.txt: ForestAltar/ID81/AF = 经典 Id81'),
    'HornShrine': (83, 'Shrine', 'HS',
                   'D2R objects.txt: HornShrine/ID83/HS = 经典 Id83'),
    'Flies': (103, 'Dummy', 'FL', 'D2R objects.txt: Flies/ID103/FL = 经典 Id103'),
    'Fountain8': (138, 'Well', 'zy', 'D2R objects.txt: Fountain8/ID138/zy = 经典 Id138'),
    'FireSmall': (160, 'fire', 'FX',
                  'D2R objects.txt: FireSmall/ID160/FX = 经典 Id160'),
    'FireMedium': (161, 'fire', 'FY',
                   'D2R objects.txt: FireMedium/ID161/FY = 经典 Id161'),
    'CorpseSkeleton': (171, 'skeleton', 'sx',
                       'D2R objects.txt: CorpseSkeleton/ID171/sx = 经典 Id171'),
    'Chest5': (240, 'chest', 'cy', 'D2R objects.txt: Chest5/ID240/cy = 经典 Id240'),
    'Chest6': (241, 'chest', 'cx', 'D2R objects.txt: Chest6/ID241/cx = 经典 Id241'),
    'Chest7': (242, 'chest', 'cu', 'D2R objects.txt: Chest7/ID242/cu = 经典 Id242'),
    'GoldProxy': (269, 'dummy', '1g',
                  'D2R objects.txt: GoldProxy/ID269/1g = 经典 Id269（Draw=0）'),
    'ConsolationChest': (397, 'chest', 'yf',
                         'D2R objects.txt: ConsolationChest/ID397/yf = 经典 Id397'),
}

# ── DS1 kind=2 的 id → 物件类（Act 1）──────────────────────────────────────────
# 本文件里**只登记、不导出**的 ds1 id（成因写在 `NOT_EXPORTED` 里，供生成器点名）：
#   52 = 传送点本体：城镇的传送台由既有路径画（`MapGenTown.Waypoint` 锚点 +
#        `MapGenDeco` 之外的 `ResPaths.WaypointFrame`），本条导出会重复一份贴图、
#        并让那张 ds1 单位所在格多画一个传送台。
NOT_EXPORTED = {
    52: '传送点本体由既有锚点路径负责（MapGenTown.Waypoint / ResPaths.WaypointFrame）',
}

# 本批三个区域（营地 / 野外 / 洞穴）的 ds1 里实测出现过的 id 全在这里；
# 每条给「判定依据」。⛔ 判不出的 id 一律**留空**（生成器照 id 分组报错点名，不许猜）。
DECO_IDS = {
    # id  freq(实测)              物件类            依据
    1:  ('TikiTorch1', '营地里 19 处（TownW1/N1/E1/S1 各 18~19 处）、野外 trees2.ds1 '
                       '1 处、洞穴 caveE/caveS 各 3~4 处；objpreset Act1 Index1'),
    2:  ('RogueBonfire', 'TownW1 子格(69,108)/(13,21) 一带，铺在 objects.dt1 的焦土圈地砖上'),
    3:  ('Standard1', 'TownW1 子格(113,117)/(22,23) 一处'),
    4:  ('Standard2', 'TownW1 子格(52,138)/(10,27) 一处'),
    33: ('Town1Ambient', 'TownW1 子格(82,127)/(16,25) 一处；Draw=0（原版不画）'),
    52: ('WaypointOutsideAct1', 'TownW1 子格(84,69) 落在五芒星石台上 ⇒ 传送点（见文件头 ①(a)）'),
    102: ('Bank', 'TownW1 子格(56,94)/(11,18) 一处'),
    110: ('CainAct1Start', 'TownW1 子格(74,106)/(14,21) 一处（Warriv 站位旁）；Draw=0'),
    # ── 野外 / 洞穴用到的（依据同第 ① 节：objpreset Act1 的 `Index` → `ObjectClass` 见
    #    `OBJPRESET_ACT1`；该类 → 经典 `Id`/`Token` 的逐行相等见 `CLASS_TO_OBJ`）─────────
    30: ('ForestAltar', '野外 wild3.ds1 1 处；objpreset Act1 Index30'),
    32: ('HornShrine', '野外 wild2.ds1 1 处；objpreset Act1 Index32'),
    35: ('Flies', '野外 sc_swamp2.ds1 2 处 / sc_obj.ds1 1 处；objpreset Act1 Index35'),
    43: ('FireSmall', '洞穴 caveNtheme1.ds1 2 处 / caveWtheme1.ds1 1 处；objpreset Act1 Index43'),
    44: ('FireMedium', '洞穴 caveEWtheme1.ds1 / caveWtheme1.ds1 各 1 处；objpreset Act1 Index44'),
    64: ('Chest5', '洞穴 caveSPre2.ds1 / caveNEWtheme1.ds1 / caveNtheme1.ds1 各 1 处；'
                   'objpreset Act1 Index64'),
    65: ('Chest6', '洞穴 caveNSEWtheme1.ds1 / caveNSW2.ds1 / caveWtheme1.ds1 各 1 处；'
                   'objpreset Act1 Index65'),
    66: ('Chest7', '洞穴 caveEtheme1.ds1 / caveNWtheme1.ds1 / caveSWtheme1.ds1 / '
                   'caveNSWtheme1.ds1 各 1 处；objpreset Act1 Index66'),
    67: ('RogueCorpse1', '洞穴 caveNEtheme1.ds1 / caveNtheme1.ds1 / caveSEWtheme1.ds1 / '
                         'caveWtheme1.ds1 各 1 处；objpreset Act1 Index67'),
    68: ('RogueCorpse2', '洞穴 caveNEWtheme1.ds1 / caveNSEWtheme1.ds1 / caveNStheme1.ds1 / '
                         'caveSEtheme1.ds1 / caveStheme1.ds1 / caveSWtheme1.ds1 各 1 处；'
                         'objpreset Act1 Index68'),
    69: ('RogueRollingCorpse1', '洞穴 caveNEtheme1.ds1 / caveNStheme1.ds1 / caveNtheme1.ds1 / '
                                'caveNSWtheme1.ds1 / caveSEWtheme1.ds1 / caveWtheme1.ds1 各 1 处；'
                                'objpreset Act1 Index69'),
    70: ('RogueStakedCorpse1', '洞穴 caveEWtheme1.ds1 / caveNEWtheme1.ds1 / caveNSWtheme1.ds1 / '
                               'caveNtheme1.ds1 / caveSEtheme1.ds1 / caveWtheme1.ds1 各 1 处；'
                               'objpreset Act1 Index70'),
    71: ('RogueStakedCorpse2', '洞穴 caveNSEtheme1.ds1 1 处；objpreset Act1 Index71'),
    72: ('CorpseSkeleton', '洞穴 caveNStheme1.ds1 / caveNSWtheme1.ds1 / caveNtheme1.ds1 / '
                           'caveSEtheme1.ds1 / caveStheme1.ds1 各 1 处；objpreset Act1 Index72'),
    78: ('Fountain8', '洞穴 caveEtheme1.ds1 / caveNStheme1.ds1 / caveSEtheme1.ds1 / '
                      'caveStheme1.ds1 各 1 处；objpreset Act1 Index78'),
    95: ('Barrel', '洞穴 caveNEWtheme1.ds1 3 处 / caveNWtheme1.ds1 4 处 / caveSEWtheme1.ds1 4 处 / '
                   'caveWtheme1.ds1 3 处；objpreset Act1 Index95'),
    104: ('GoldProxy', '洞穴各块共 25 处；Draw=0（原版的金币占位，本来就不画）；'
                       'objpreset Act1 Index104'),
    111: ('ConsolationChest', '洞穴各块共 4 处；objpreset Act1 Index111'),
}


#: 子格 → 格：`sub-tile = 格 × 5`。出处 = 上游参考实现 `libd2` 的
#: `packages/drlg/src/lib.zig:1136`，原文「SUBTILES (tile*5). We composite them onto a
#: single subtile-space bitmap」；
#: https://raw.githubusercontent.com/jaenster/libd2/main/packages/drlg/src/lib.zig
SUBTILES_PER_TILE = 5


def rel(*parts):
    return '\\'.join(parts)


def summary(raw, path):
    return {'path': path, 'bytes': len(raw), 'sha256': hashlib.sha256(raw).hexdigest()}


def frame_rate_of(frame_delta):
    """`FrameDelta` → 每秒播放帧数（换算出处见文件头 ③）。"""
    if frame_delta <= 0:
        raise ValueError('FrameDelta = %d（<=0）-> 算不出帧率（原版 0 = 该模式没启用）' % frame_delta)
    return 25.0 * frame_delta / 256.0


# ══════════════════════════════════════════════════════════════════════════════
#  原版表读取（objects.txt / ObjType.txt）
# ══════════════════════════════════════════════════════════════════════════════

def _read_txt(path):
    if not os.path.exists(path):
        raise SystemExit('缺原版表 %s（先按 `storm.py` 的方式从 mpq 解，或放 1.10f txt 包）' % path)
    with open(path, 'r', encoding='latin-1') as fh:
        rows = [ln.rstrip('\r\n').split('\t') for ln in fh if ln.strip()]
    head = rows[0]
    data = {}
    for r in rows[1:]:
        if len(r) <= head.index('Id'):
            continue
        v = r[head.index('Id')].strip()
        if v == '':
            continue
        data[int(v)] = dict(zip(head, r))
    return head, data


def objects_path(mpq_dir):
    p = os.path.join(REPO_ROOT, '原版资源', 'd2lod1.10txt-1.10f',
                     'data', 'global', 'excel', 'objects.txt')
    if os.path.exists(p):
        return p
    raise SystemExit('缺 objects.txt（找过 %s）' % p)


def pick_mode(row):
    """该物件采用的模式下标 + 依据（文件头 ③）。

    返回 `(modeIndex, frames, frameDelta, cycle, why)`。
    """
    for i in range(8):
        cnt = int((row.get('FrameCnt%d' % i) or '0').strip() or 0)
        cyc = int((row.get('CycleAnim%d' % i) or '0').strip() or 0)
        if cnt > 1 and cyc == 1:
            return (i, cnt, int((row.get('FrameDelta%d' % i) or '0').strip() or 0), cyc,
                    '第一个「帧数>1 且 CycleAnim=1」的模式')
    cnt = int((row.get('FrameCnt0') or '0').strip() or 0)
    delta = int((row.get('FrameDelta0') or '0').strip() or 0)
    return (0, max(cnt, 1), delta, int((row.get('CycleAnim0') or '0').strip() or 0),
            '没有任何循环模式 ⇒ 静态，取模式 0 的第 0 帧')


def deco_kinds(objects_rows):
    """`DECO_IDS` × 经典 objects.txt → 逐个 ds1 id 的物件描述（判不出的跳过并报错点名）。"""
    out = []
    unknown = []
    for ds1_id in sorted(DECO_IDS):
        cls_name = DECO_IDS[ds1_id][0]
        hit = CLASS_TO_OBJ.get(cls_name)
        if hit is None:
            unknown.append((ds1_id, cls_name))
            continue
        obj_id, cl_name, token, why = hit
        row = objects_rows.get(obj_id)
        if row is None:
            unknown.append((ds1_id, cls_name))
            continue
        mode, frames, delta, cycle, why_mode = pick_mode(row)
        draw = int((row.get('Draw') or '1').strip() or 0)
        out.append(dict(ds1Id=ds1_id, cls=cls_name, className=cl_name, token=token,
                        objId=obj_id, modeIndex=mode, modeCode=MODE_CODES[mode],
                        frames=frames, frameDelta=delta, cycle=cycle,
                        fps=(frame_rate_of(delta) if frames > 1 else 0.0),
                        draw=draw, why=DECO_IDS[ds1_id][1], whyMode=why_mode,
                        whyClass=why))
    return out, unknown


def skip_reason(ds1_id):
    """某个 ds1 id **不在** `exportable_kinds()` 里的成因（供调用方点名时区分）。"""
    if ds1_id not in DECO_IDS:
        return '未登记'
    if ds1_id in NOT_EXPORTED:
        return '由既有路径负责'
    return '原版 Draw=0'


def exportable_kinds(objects_rows=None):
    """可导出的 ds1 kind=2 id → 物件描述（`Draw != 0` 且不在 `NOT_EXPORTED`）。

    三个布局导出器（营地 / 野外 / 洞穴）都从这里取表，口径只有这一份（见文件头 ⑤）。
    """
    if objects_rows is None:
        _head, objects_rows = _read_txt(objects_path(None))
    items, _unknown = deco_kinds(objects_rows)
    return dict((it['ds1Id'], it) for it in items
                if it['draw'] != 0 and it['ds1Id'] not in NOT_EXPORTED)


def units_of(ds1, exportable, origin_cell=(0, 0)):
    """ds1 `objects` 层的 kind=2（物件预设单位）→ `[(格 x, 格 y, ds1 id)]`。

    `origin_cell` 是**该 ds1 左上角在目标坐标系里的格坐标**（营地布局用它对齐四块；
    野外 / 洞穴的预设块用它定位块内格）。返回 `(units, skipped)`：`skipped` = 不在
    `exportable` 里的 id 的出现次数（调用方按 id 点名，⛔ 不猜也不静默丢）。
    """
    ox, oy = origin_cell
    out = []
    skipped = {}
    for o in ds1.objects:
        if o.obj_type != 2:
            continue
        if o.obj_id not in exportable:
            skipped[o.obj_id] = skipped.get(o.obj_id, 0) + 1
            continue
        out.append((o.x // SUBTILES_PER_TILE - ox, o.y // SUBTILES_PER_TILE - oy, o.obj_id))
    return out, skipped


# ══════════════════════════════════════════════════════════════════════════════
#  动画解包
# ══════════════════════════════════════════════════════════════════════════════

def load_layers(handle, token, mode_code):
    """读 COF 并解出该模式的各层 DCC（按 COF 的绘制顺序返回）。"""
    cof_rel = rel('data', 'global', 'objects', token, 'COF',
                  '%s%s%s.COF' % (token, mode_code, WEAPON_CLASS))
    cof_bytes = storm.read_file(handle, cof_rel)
    if not cof_bytes:
        return None
    c = cofmod.parse(cof_bytes, cof_rel)
    layers = []
    for idx in c.draw_order(0, 0):
        comp = c.layers[idx].component_code
        dcc_rel = rel('data', 'global', 'objects', token, comp,
                      '%s%s%s%s%s.dcc' % (token, comp, EQUIP, mode_code, WEAPON_CLASS))
        raw = storm.read_file(handle, dcc_rel)
        if not raw:
            return None
        d = dccmod.parse(raw, dcc_rel)
        if len(d.directions) != 1:
            raise ValueError('%s：方向数 = %d（物件实测为 1）' % (dcc_rel, len(d.directions)))
        layers.append({'component': comp, 'raw': raw, 'rel': dcc_rel, 'dir': d.directions[0]})
    return cof_rel, cof_bytes, c, layers


def export_token(handle, palette, item, out_root):
    """解一个 ds1 id 的物件本体 → `<out>/<token 小写>/<帧号三位>.png` + 登记。"""
    token = item['token']
    mode = item['modeCode']
    got = load_layers(handle, token, mode)
    if got is None:
        return None
    cof_rel, cof_bytes, c, layers = got
    frames = item['frames']
    if c.frames_per_dir < frames:
        raise ValueError('%s: COF 帧数 %d < 采用的 FrameCnt %d' % (cof_rel, c.frames_per_dir, frames))

    # 画布 = **本次实际导出的那 `frames` 帧**的包围盒并集（不是 DCC 的方向包围盒）：
    #   `frames` 取自 `objects.txt` 的 `FrameCnt`，可以小于 DCC 的 `framesPerDir`
    #   （实测 `B1` 的 NU 有 10 帧、表只用第 0 帧，其余帧的偏移会把画布撑到给不出垫片）。
    used = []
    for l in layers:
        boxes = l['dir'].frame_boxes
        if len(boxes) < frames:
            raise ValueError('%s：DCC 帧数 %d < 采用的 FrameCnt %d'
                             % (l['rel'], len(boxes), frames))
        used += boxes[:frames]
    left = min(b.left for b in used)
    top = min(b.top for b in used)
    right = max(b.left + b.width for b in used)
    bottom = max(b.top + b.height for b in used)
    box_w, box_h = right - left, bottom - top
    pad = HALF_CELL_PX - (box_h + top)          # 原点距画布底边 = box_h + top
    if pad < 0:
        raise ValueError('%s：并集框算出的垫片为负（%d）' % (token, pad))
    canvas_h = box_h + pad

    dir_name = token.lower()
    out_dir = os.path.join(out_root, 'Objects', dir_name)
    os.makedirs(out_dir, exist_ok=True)
    files = []
    for fi in range(frames):
        buf = bytearray(box_w * canvas_h * 4)
        for li in c.draw_order(0, fi):
            comp = c.layers[li].component_code
            lay = next(l for l in layers if l['component'] == comp)
            dr = lay['dir']
            rgba = dccmod.frame_rgba(dr.frames[fi], palette)
            # 层图是**方向包围盒**尺寸 ⇒ 画布小于它时要裁掉画布外的部分（否则负下标会绕回）
            ox, oy = dr.box.left - left, dr.box.top - top
            x0, x1 = max(0, ox), min(box_w, ox + dr.box.width)
            y0, y1 = max(0, oy), min(canvas_h, oy + dr.box.height)
            for y in range(y0, y1):
                src = (y - oy) * dr.box.width * 4
                dst_row = y * box_w
                for x in range(x0, x1):
                    s = src + (x - ox) * 4
                    if rgba[s + 3] == 0:
                        continue
                    d = (dst_row + x) * 4
                    buf[d:d + 4] = rgba[s:s + 4]
        name = '%03d.png' % fi
        path = os.path.join(out_dir, name)
        n = pngio.write_rgba(path, box_w, canvas_h, bytes(buf))
        files.append({'file': '%s/%s' % (dir_name, name), 'w': box_w, 'h': canvas_h,
                      'bytes': n,
                      'sha256': hashlib.sha256(open(path, 'rb').read()).hexdigest()})
    return {'ds1Id': item['ds1Id'], 'token': token, 'dir': dir_name,
            'class': item['cls'], 'className': item['className'], 'objId': item['objId'],
            'mode': item['modeCode'], 'modeIndex': item['modeIndex'],
            'frames': frames, 'cycle': item['cycle'], 'frameDelta': item['frameDelta'],
            'fps': item['fps'], 'draw': item['draw'],
            'why': item['why'], 'whyMode': item['whyMode'], 'whyClass': item['whyClass'],
            'canvas': {'w': box_w, 'h': canvas_h, 'unionH': box_h, 'padBottom': pad,
                       'originPxFromTop': [-left, -top]},
            'sources': {'cof': summary(cof_bytes, cof_rel),
                        'dcc': [summary(l['raw'], l['rel']) for l in layers],
                        'objects': OBJECTS_REL, 'palette': PALETTE_REL},
            'files': files}


CS_HEADER = '''// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · Module/Map/MapGenDeco.cs
// **本文件是生成物，禁止手改**（生成器：`tools/d2codec/export_deco.py`）。
//
// 原版 DS1 `objects` 层里 **kind=2（物件预设单位）** 的物件类表。
// `id` = 该幕 objpreset 表的下标（**不是 `Objects.txt` 的 Id**）；经典 1.10 的包里
// 没有 `ObjPreset.txt`，本表用 D2R 的官方数据表（`Index`/`Act`/`ObjectClass`）：
//   https://raw.githubusercontent.com/pinkufairy/D2R-Excel/main/base/objpreset.txt
// 替换依据（三条，详见生成器文件头 ①）：Index 52 = Waypoint 落在五芒星台；
//   D2R objects.txt 的 Class→Id/Token 与经典逐行相等；Index 6..11 = Stone 1..6 同序。
//
// 每个物件类的**帧数与帧率都读原版 `Objects.txt`**（不是本项目的数字）：
//   · 模式 = 该物件第一个「FrameCnt>1 且 CycleAnim=1」的模式（自带的那段循环动画）；
//     没有任何循环模式 ⇒ 静态，取模式 0 第 0 帧。
//   · 帧率 = `25 × FrameDelta / 256`（换算出处同 `ResPaths.WaypointFrameFps`）。
//   逐条的 Id/Token/模式/帧数/帧率/依据 = `Resources/Clover/D2/Objects/deco-manifest.json`。
//
// 帧贴图：`Resources/Clover/D2/Objects/<Dir>/<帧号三位>.png`
//   （键由 `ResPaths.DecoFrame(Dir, i)` 拼，⛔ 不许在别处手拼路径）。
// ─────────────────────────────────────────────────────────────────────────────
using UnityEngine;

namespace Diablo2.Module.Map
{
    /// <summary>原版 kind=2 物件的类表（生成物，见文件头）。</summary>
    internal static class MapGenDeco
    {
        /// <summary>一个物件类（= 一个 ds1 id 在原版 objpreset 里的那一类）。</summary>
        public struct Kind
        {
            /// <summary>`Resources/Clover/D2/Objects/` 下的目录名（= 原版 Token 的小写）。</summary>
            public readonly string Dir;

            /// <summary>播放帧数（= 原版 `Objects.txt` 该模式列的 `FrameCnt`）。</summary>
            public readonly int Frames;

            /// <summary>帧率（= `25 × FrameDelta / 256`；`Frames <= 1` 时无意义）。</summary>
            public readonly float Fps;

            /// <summary>是否循环（= 原版 `CycleAnim`）。</summary>
            public readonly bool Loop;

            /// <summary>原版 `Objects.txt` 的 `Id`。</summary>
            public readonly int SrcId;

            /// <summary>原版 `Objects.txt` 的 `Token`。</summary>
            public readonly string SrcToken;

            /// <summary>采用的模式下标（0=NU / 1=OP / 2=ON）。</summary>
            public readonly int SrcModeIndex;

            /// <summary>ds1 `objects` 层的 kind=2 id（= objpreset 的 `Index`）。</summary>
            public readonly int Ds1Id;

            /// <summary>objpreset 的 `ObjectClass`。</summary>
            public readonly string ClassName;

            /// <summary>构造（生成物内部用；字段顺序 = 上面声明的顺序）。</summary>
            public Kind(string dir, int frames, float fps, bool loop, int srcId, string srcToken,
                int srcModeIndex, int ds1Id, string className)
            {
                Dir = dir;
                Frames = frames;
                Fps = fps;
                Loop = loop;
                SrcId = srcId;
                SrcToken = srcToken;
                SrcModeIndex = srcModeIndex;
                Ds1Id = ds1Id;
                ClassName = className;
            }
        }

        /// <summary>下标 = ds1 `objects` 层 kind=2 单位要画的物件类（`-1` = 不画）。</summary>
        public static readonly Kind[] Kinds =
        {
%s
        };

        /// <summary>`ds1 id` → <see cref="Kinds"/> 下标（`-1` = 这类物件原版不画 / 未登记）。</summary>
        public static int IndexOf(int ds1Id)
        {
            for (var i = 0; i < Kinds.Length; i++)
            {
                if (Kinds[i].Ds1Id == ds1Id) return i;
            }
            return -1;
        }
    }
}
'''


def write_cs(path, exported):
    rows = []
    for e in exported:
        rows.append('            new Kind("%s", %d, %sf, %s, %d, "%s", %d, %d, "%s"),'
                    % (e['dir'], e['frames'],
                       ('%.5f' % e['fps']) if e['frames'] > 1 else '0',
                       'true' if e['cycle'] == 1 else 'false',
                       e['objId'], e['token'], e['modeIndex'], e['ds1Id'], e['class']))
    with open(path, 'w', encoding='utf-8', newline='\r\n') as fh:
        fh.write(CS_HEADER % '\n'.join(rows))


def main(argv=None):
    ap = argparse.ArgumentParser()
    ap.add_argument('--out', default=os.path.join(REPO_ROOT, 'client', 'Assets', 'Resources',
                                                  'Clover', 'D2'))
    ap.add_argument('--cs-out', default=os.path.join(REPO_ROOT, 'client', 'Assets', 'Scripts',
                                                     'Module', 'Map', 'MapGenDeco.cs'))
    ap.add_argument('--mpq-dir', default=os.path.join(REPO_ROOT, '原版资源', '_mpq_incoming'))
    ap.add_argument('--only', default='')
    args = ap.parse_args(argv)
    # ⛔ 先落成**绝对路径**：`storm.open_archive` 会 `os.chdir` 到 mpq 所在目录（ANSI 接口
    #   只吃 ASCII 路径），此后相对路径会落到那个目录下（实测：产物被写到 `原版资源/_mpq_incoming/`）。
    args.out = os.path.abspath(args.out)
    args.cs_out = os.path.abspath(args.cs_out)

    _head, objects_rows = _read_txt(objects_path(args.mpq_dir))
    items, unknown = deco_kinds(objects_rows)
    for ds1_id, cls in unknown:
        print('  [WARN] ds1 id=%d 的物件类 %r 在经典表里判不出 ⇒ 跳过（点名，不猜）'
              % (ds1_id, cls))
    if args.only:
        keep = set(t.strip().upper() for t in args.only.split(',') if t.strip())
        items = [it for it in items if it['token'].upper() in keep]

    printable = [it for it in items if it['draw'] != 0 and it['ds1Id'] not in NOT_EXPORTED]
    skipped = [it for it in items if it['draw'] == 0]
    for it in items:
        if it['ds1Id'] in NOT_EXPORTED:
            print('  id=%-4d %s：%s' % (it['ds1Id'], it['cls'], NOT_EXPORTED[it['ds1Id']]))

    print('ds1 kind=2 id → 物件类（Act 1 objpreset）：')
    for it in items:
        print('  id=%-4d %-22s → 经典 Id=%-4d Token=%-3s 「%s」 模式%d(%s) 帧数=%d '
              'FrameDelta=%d fps=%.5f Draw=%d'
              % (it['ds1Id'], it['cls'], it['objId'], it['token'], it['className'],
                 it['modeIndex'], it['modeCode'], it['frames'], it['frameDelta'],
                 it['fps'], it['draw']))
    for it in skipped:
        print('  id=%-4d %s：原版 `Draw=0` ⇒ 不导出、不画' % (it['ds1Id'], it['cls']))

    mpq = os.path.join(args.mpq_dir, 'D2data.mpq')
    if not os.path.exists(mpq):
        raise OSError('找不到 %s' % mpq)
    tmp = os.path.join(REPO_ROOT, '.ai-tmp', 'test', 'export-deco-tmp')
    os.makedirs(tmp, exist_ok=True)
    storm.set_work_dir(tmp)

    handle = storm.open_archive(mpq, patch=os.path.join(args.mpq_dir, 'Patch_D2.mpq'))
    exported = []
    try:
        pal_raw = storm.read_file(handle, PALETTE_REL)
        if not pal_raw:
            raise OSError('包内没有 %s' % PALETTE_REL)
        pal_tmp = os.path.join(tmp, 'units-Pal.dat')
        with open(pal_tmp, 'wb') as fh:
            fh.write(pal_raw)
        palette = dccmod.read_pl2(pal_tmp)

        for it in printable:
            got = export_token(handle, palette, it, args.out)
            if got is None:
                print('  [WARN] %s（Token=%s 模式=%s）包内没有 COF/DCC ⇒ 跳过（点名）'
                      % (it['cls'], it['token'], it['modeCode']))
                continue
            exported.append(got)
            print('  %-4s %-8s 模式%s %d 帧 %dx%d  fps=%.5f → %s/'
                  % (it['token'], it['cls'], it['modeCode'], got['frames'],
                     got['canvas']['w'], got['canvas']['h'], got['fps'], got['dir']))
    finally:
        storm.close_archive(handle)

    manifest = {
        'source': {
            'objpreset': 'D2R objpreset.txt (Index/Act/ObjectClass) — 见生成器文件头 ①',
            'objects': objects_path(args.mpq_dir),
            'objtype': OBJTYPE_REL,
            'palette': PALETTE_REL,
        },
        'anchors': {
            'index52': 'Act1 Index52 = WaypointOutsideAct1 ↔ TownW1 kind=2 id=52 落在五芒星台',
            'classToObj': {k: {'objId': v[0], 'name': v[1], 'token': v[2], 'why': v[3]}
                           for k, v in CLASS_TO_OBJ.items()},
        },
        'kinds': exported,
    }
    mpath = os.path.join(args.out, 'Objects', 'deco-manifest.json')
    os.makedirs(os.path.dirname(mpath), exist_ok=True)
    with open(mpath, 'w', encoding='utf-8') as fh:
        json.dump(manifest, fh, indent=2, ensure_ascii=False)
        fh.write('\n')
    print('  → %s' % mpath)

    write_cs(args.cs_out, exported)
    print('  → %s（%d 类）' % (args.cs_out, len(exported)))
    return 0


if __name__ == '__main__':
    sys.exit(main())
