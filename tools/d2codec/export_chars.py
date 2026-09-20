# -*- coding: utf-8 -*-
"""把原版 `.dcc`（角色 / 怪物 / NPC 的单位动画）解成 **逐帧 PNG** 落进 Unity 工程。

产出（由 `Assets/Editor/AssetImporter.cs` 既有的 `Resources/Clover/D2/**` 规则按像素图导入：
Point / Single / PPU=64 / 无压缩 / 无 mipmap / **轴心 = (0.5, 0.5)**）：
    client/Assets/Resources/Clover/D2/Chars/<class>/<动作>_<方向>_<帧号>.png
    client/Assets/Resources/Clover/D2/Monsters/<code>/<动作>_<方向>_<帧号>.png
    client/Assets/Resources/Clover/D2/{Chars,Monsters}/<unit>/manifest.json   该单位的元数据

**帧键约定**（与 `Module/View/SpriteFrames.cs` 的文件头契约逐字一致）：
    `{动作}_{方向}_{帧号}`，动作 = idle/walk/attack/cast/hit/death（+ **run**，见 `ACTIONS`），
    方向 = s/sw/w/nw/n/ne/e/se（`Def.Dir8` 名小写），帧号从 0 起。

**轴心（"不许浮空"的关键）** —— 每个单位一张**定尺寸画布**，其**几何中心 = D2 的单位原点
（脚下）**：
    · D2 的 `.dcc` 方向包围盒以单位原点 (0,0) 为基准（原点 = 脚底中心），
      帧的 `yoffset` 是**底边**（见 `dcc.py` 文件头）；
    · 本导出器把该单位的全部 (动作, 方向) 合成图**统一垫进**同一张画布：
      `画布宽 = 2*max(|左伸|, |右伸|)`、`画布高 = 2*max(|上伸|, |下伸|)`，
      原点落在**画布正中** ⇒ Unity 轴心恒为 **(0.5, 0.5)**（= Unity 默认轴心，
      于是 `AssetImporter.ApplyPixelArt` 一行都不用改）。
    · 因此"角色脚底 = 实体世界坐标"，与 `Iso.GridToWorld`（格中心）对齐；
      帧内自带的左右/上下偏移（`xoffset/yoffset`）**逐帧保留**，动画不会抖。

方向映射（**三层，⛔ 缺一层就整体错位 45°**）：

  ① **逻辑方向序号（32 分度）**：`Diablerie/Engine/Iso.cs:103-109` 的 `Direction(pos, target, 32)`
     以 **map 空间向量 `(1,1)`**（= 世界 `(0,-1)` = **屏幕正下**）为 0 度、顺时针增角 ⇒
     `k=0 南(S) · 4 西南(SW) · 8 西(W) · 12 西北(NW) · 16 北(N) · 20 东北(NE) · 24 东(E) · 28 东南(SE)`。
  ② **8 向逻辑序号 `j = k/4`**：`COFRenderer.cs:106`
     `cofDirection = direction * cof.directionCount / Unit.DirectionCount`，而
     `Unit.cs:13` `Unit.DirectionCount = 32` ⇒ `j` 的顺序 **就是**本项目 `Def.Dir8` 的顺序
     `S, SW, W, NW, N, NE, E, SE`（也就是文件名里 `DIR_NAMES[j]` 的那个下标）。
  ③ **`.dcc` 文件内的方向槽位 ≠ 逻辑序号**：`Engine/IO/D2Formats/DCC.cs:555`
     `internalIndex = DirectionMapping.MapToInternal(header.directionCount, directionIndex)`
     —— 表在 `Engine/IO/D2Formats/DirectionMapping.cs:5-9`（下面的 `DIR_MAP` **逐字照抄**）：
     `_dirs8 = { 4, 0, 5, 1, 6, 2, 7, 3 }` ⇒ **文件槽位 → 屏幕方向 = 0:SW 1:NW 2:NE 3:SE 4:S 5:W 6:N 7:E**。

     ⛔ 索引口径照抄 Diablerie 调用点：**先** `idx = j * dirs // 8`，**再** `slot = DIR_MAP[dirs][idx]`
     （`dirs=8` 时 `idx == j`，**这正是本文件历史缺陷的伪装**：看起来像恒等变换，
      于是漏掉了第 ③ 步，把"文件槽位"直接当成"方向名"）。

     **历史缺陷（本片修复，2026-09-19）**：漏掉第 ③ 步 ⇒ 导出的 1216 张角色 PNG + 5040 张怪物 PNG
     的**方向标签整体错位 45°**（标 `s` 的其实是 SW 姿态、标 `n` 的其实是 S、标 `ne` 的其实是 W…）。
     像素复核（`idle_{dir}_0.png` 的不透明包围盒宽）：错位版 `s=25 sw=24 w=30 nw=31 n=22 ne=44 e=22 se=45`；
     按正确映射重排后 **W=44 / E=45 最宽（正侧身）、S=22 / N=22 最窄（正前/正后）**，与"侧身最宽"吻合。

图层选择（**数据驱动**，不写死）：
    · `.cof` 给出该动作由哪几层组成 + 每帧绘制顺序（`cof.py`）；
    · 每层的 DCC 文件名 = `token + 组件码 + equip + 模式 + 组件武器类别`；
    · `equip` 取 **`"lit"`**（原版"轻甲/裸体"那一套）—— 依据：怪物/NPC 的 DCC **只**提供
      `lit`（实测 `rc` 只有 `littr...`、`zm/tr` 有 `lit/med/hvy`），玩家的 `lit` 就是
      无盔甲时的身体/头发（`am/hd` 的 equip 集合 = `lit`+各头盔码，`lit` = 头发）；
    · 该层没有 `lit` 变体（如玩家右手武器 `rh`、盾 `sh`）⇒ **跳过并登记**（不画错的东西）。

用法：
    python export_chars.py [--out <Resources/Clover/D2 根>] [--only am,fa,rc] [--all-classes]
                           [--emit-cs <SpriteFrameCounts.cs 路径>] [--d2assets <_assets_src 路径>]
"""

import collections
import json
import os
import sys

try:
    from . import dcc as dccmod
    from . import cof as cofmod
except ImportError:
    import dcc as dccmod
    import cof as cofmod

# ── 默认路径（本机实际位置）──────────────────────────────────────────────────
# 解包产物统一在 `<项目根>/原版资源`（skill §1.9）。
DEFAULT_D2ASSETS = os.path.join(
    os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__)))),
    '原版资源')
DEFAULT_OUT = os.path.join(os.path.dirname(os.path.dirname(os.path.dirname(
    os.path.abspath(__file__)))), 'client', 'Assets', 'Resources', 'Clover', 'D2')
DEFAULT_CS = os.path.join(os.path.dirname(os.path.dirname(os.path.dirname(
    os.path.abspath(__file__)))), 'client', 'Assets', 'Scripts', 'Module', 'View',
    'SpriteFrameCounts.cs')

#: 单位调色板（D2 单位 DCC 用的调色板）：`data/global/palette/units/Pal.dat`。
#: ⚠️ `.dat` = 768 字节、每项 **B,G,R**（与 `.pl2` 的 R,G,B **反序**）——见 `dcc.read_pl2`
#: 的实测证据；按 R,G,B 读会得到"蓝紫色的亚马逊"。
PALETTE_REL = 'data/global/palette/units/Pal.dat'

#: 每个动作的 equip（"lit" = 无甲身体 / 头发 / 怪物本体）。
EQUIP = 'lit'

#: 找 `.cof` 用的武器类别：玩家用 `hth`（徒手 = 原版新角色无装备的默认外观）；
#: 怪物/NPC 用 **`MonStats.txt` 的 `BaseW` 列**（= 该单位自带的基础武器类别）——
#: ⛔ 不许一律写 `hth`：实测 `cr`（堕落罗格 DarkHunter）**根本没有 hth 的 COF**，
#:   它的 COF 只有 `1HS/2HT/BOW`（原版堕落罗格手持武器）⇒ 写死人 hth 会让 `cr` 一个动作都导不出
#:   （只捞到 `CRDTHTH.COF` 这个恰好存在的死亡动作）。
PLAYER_WEAPON_CLASS = 'hth'

#: 本项目**导出用**的动作表 = `ViewAnim` 的原版代号 + 原版「跑」（`RN`）。
#: 出处：原版 **走/跑两套动画** —— `cof.py:47-50` 的 `PLAYER_MODES` / `MONSTER_MODES` 里
#:   `WL`（walk）与 `RN`（run）是两个独立模式代号（同为 `Diablerie COF.cs:30/33` 的模式清单）。
#: 实测（遍历两个 MPQ 里 `<root>/cof/` 下文件名含 `RN` 的 COF）：
#:   · 5 个玩家都有 `<token>rnhth.cof`（含本项目用的 `hth` 武器类别）⇒ 都能导出 `run_*`；
#:   · 怪物里 `zm`（`zmrnhth.cof`）与 `cr`（`crrn1hs.cof`，其 `BaseW=1hs`）有；
#:     `fa/fs/si/bk/ye/wr` 与 5 个 NPC **没有** ⇒ 该动作被跳过（见 `FALLBACK`，⛔ 不回退到 WL）。
#: ⚠️ 这张表的**键空间 = `ViewAnim`**（`Module/View/ViewAnim.cs`；`Run` 由**片 2b** 加在末尾 = 6）
#:   ⇒ 它同时被 `emit_cs` 用来生成 `SpriteFrameCounts.ByUnit`（那张表的数组下标 = `ViewAnim`）。
#:   ⛔ 加动作必须与 `ViewAnim` 同步：多一列"`ViewAnim` 里没有的动作"就是**没有消费方的死数据**。
ACTIONS = cofmod.VIEW_ANIM_MODES + (('run', 'RN'),)

#: 生成 `SpriteFrameCounts.cs` 用的动作表。
#: **片 2b 起 = `ACTIONS`**（`ViewAnim.Run` 已存在 ⇒ 下标 6 有消费方，`SpriteFrames` 会用它拼
#: `run_*` 帧键）；片 2a 时它只取 `VIEW_ANIM_MODES`（那时 `ViewAnim` 还没有 Run，加列就是死数据）。
CS_ACTIONS = ACTIONS

#: 本项目 `Def.Dir8` 的 8 个方向名（小写，下标 = 枚举值）—— 与 `SpriteFrames.Keys` 的拼法一致。
DIR_NAMES = ('s', 'sw', 'w', 'nw', 'n', 'ne', 'e', 'se')

#: 逻辑方向序号（下标）→ `.dcc` **文件内的方向槽位**（值）。
#: **出处**：Diablerie `Engine/IO/D2Formats/DirectionMapping.cs:5-9` 的 `_dirs1/_dirs4/_dirs8/_dirs16/_dirs32`
#:   （**逐字照抄，⛔ 不许改**）+ 调用点 `Engine/IO/D2Formats/DCC.cs:555`
#:   `internalIndex = DirectionMapping.MapToInternal(header.directionCount, directionIndex)`。
#: 索引口径：`idx = 逻辑序号 j * dirs // 8`（= Diablerie 的 `directionIndex`），`slot = DIR_MAP[dirs][idx]`。
#: 由此得到的 **槽位 → 屏幕方向**：`0=SW 1=NW 2=NE 3=SE 4=S 5=W 6=N 7=E`。
DIR_MAP = {
    1: [0],
    4: [0, 1, 2, 3],
    8: [4, 0, 5, 1, 6, 2, 7, 3],
    16: [4, 8, 0, 9, 5, 10, 1, 11, 6, 12, 2, 13, 7, 14, 3, 15],
    32: [4, 16, 8, 17, 0, 18, 9, 19, 5, 20, 10, 21, 1, 22, 11, 23,
         6, 24, 12, 25, 2, 26, 13, 27, 7, 28, 14, 29, 3, 30, 15, 31],
}

#: 逻辑方向（`Def.Dir8`）的个数 —— 文件名里的 8 个方向名就是这 8 个逻辑序号。
LOGICAL_DIRS = 8


def to_file_slot(dirs, logical_dir):
    """逻辑方向序号（0..7，顺序同 `Def.Dir8`）→ `.dcc` 文件内的方向槽位。

    ⛔ 三处**不许**简化：
      · `dirs` 不在 `DIR_MAP` 里 ⇒ 抛 `ValueError`（**不许静默退回 0**：那会让 8 个方向的图全一样）；
      · 索引必须是 `logical_dir * dirs // 8`（= Diablerie 的 `directionIndex`），不是 `logical_dir`；
      · 结果必须经 `DIR_MAP` 换算（`dirs=8` 时 `{4,0,5,1,6,2,7,3}` **不是恒等**，这正是历史缺陷所在）。
    """
    table = DIR_MAP.get(dirs)
    if table is None:
        raise ValueError('DCC directionCount=%d 不在 DirectionMapping 表里（只有 %s）⇒ 拒绝导出'
                         % (dirs, sorted(DIR_MAP)))
    idx = logical_dir * dirs // LOGICAL_DIRS
    if not 0 <= idx < len(table):
        raise ValueError('逻辑方向 %d 在 directionCount=%d 下算出索引 %d，越界（表长 %d）'
                         % (logical_dir, dirs, idx, len(table)))
    return table[idx]


#: 缺动作时的回退链（NPC 实测只有 NU/WL；**回退到的也是原版动画，绝不用占位色块**）。
FALLBACK = {
    'attack': ('A1', 'WL', 'NU'),
    'cast': ('SC', 'A1', 'NU'),
    'hit': ('GH', 'NU'),
    'death': ('DT', 'GH', 'NU'),
    'walk': ('WL', 'NU'),
    'idle': ('NU',),
    #: `run` **刻意空回退**：原版"跑"只有一部分单位有（玩家全是 `RN`；怪物里实测只有 `zm`/`cr`）。
    #: ⛔ 不许回退到 `WL` —— 那会造出"跑 = 走"的**假动画**（`run_*.png` 与 `walk_*.png` 逐像素相同），
    #:    与"回退到的也是原版动画、语义不变"的初衷相反。缺 `RN` ⇒ 该 (单位, run) 直接跳过并打 WARN。
    'run': (),
}


class Unit(object):
    """一个要导出的单位。"""

    __slots__ = ('kind', 'token', 'out_dir', 'mpq', 'class_name', 'note', 'weapon_class',
                 'component_flags')

    def __init__(self, kind, token, out_dir, mpq, class_name=None, note=''):
        self.kind = kind          # player / monster / npc
        self.token = token        # D2 的 2 字母代码（am / fa / rc …）
        self.out_dir = out_dir    # 输出子目录（如 Chars/amazon、Monsters/fa）
        self.mpq = mpq
        self.class_name = class_name
        self.note = note
        # 由 `monstats.txt` 填（玩家固定 hth / 无 component_flags），见 `load_monstats`
        self.weapon_class = PLAYER_WEAPON_CLASS
        self.component_flags = {}

    def __repr__(self):
        return 'Unit(%s/%s wc=%s)' % (self.out_dir, self.token, self.weapon_class)


def load_monstats(arch):
    """读 `data/global/excel/monstats.txt`，返回 `Code → {'basew':…, 'flags':{组件:0/1}}`。

    出处：`d2data.mpq` 的 `data/global/excel/monstats.txt`（本项目 `策划/` 的配表就是从它打的）。
      · `BaseW` = 该单位自带的**基础武器类别** —— 实测 `DarkHunter(CR).BaseW = 1hs`，
        这正是 `cr` 必须用 `1hs` 而不是 `hth` 找 COF 的原始依据（`cr` 没有 hth 的 COF）。
      · `HD/TR/LG/RA/LA/RH/LH/SH/S1..S8` = **该组件画不画**（1 = 画）。
        这是"哪些层属于这个怪"的唯一权威来源：实测 `DarkHunter` 的 `S3=1`、而
        `Fallen/Zombie/…` 各有不同组合；不看这张表就会把 COF 里列出的层全画出来
        （实测 `cr` 的 S3 是一团 58×189 的青色块，画出来完全不像原版）。
    """
    data = arch.read('data/global/excel/monstats.txt')
    if not data:
        print('[WARN] 读不到 monstats.txt ⇒ 退化为"全部按 hth + 只画身体"')
        return {}
    lines = data.decode('latin1').splitlines()
    hdr = lines[0].split('\t')
    need = ['Code', 'BaseW'] + list(COMPONENTS)
    if any(c not in hdr for c in need):
        print('[WARN] monstats.txt 列不全（缺 %s）⇒ 退化处理'
              % [c for c in need if c not in hdr])
        return {}
    ci, wi = hdr.index('Code'), hdr.index('BaseW')
    cidx = dict((c, hdr.index(c)) for c in COMPONENTS)
    out = {}
    for l in lines[1:]:
        f = l.split('\t')
        if len(f) <= max(cidx.values()):
            continue
        code = f[ci].strip().lower()
        if not code or code in out:
            continue
        out[code] = {
            'basew': f[wi].strip().lower(),
            'flags': dict((c, f[i].strip() not in ('', '0')) for c, i in cidx.items()),
        }
    return out


# ── 单位清单 ─────────────────────────────────────────────────────────────────
#   玩家：`ResPaths.CharDir(PlayerClass)` = `D2/Chars/{枚举名小写}/`
#   怪物：`SpriteFrames.SpriteCodeOf` → `monster_c.sprite` 列（= `Monster.tsv` 的 sprite 列，大写）
#         ⇒ `D2/Monsters/{小写}/`
#   NPC：`Code` 列取自原版 `monstarts.txt`（Class/Code）—— 阿卡拉 PS / 卡夏 RC / 恰西 CI /
#        基德 GH / 瓦瑞夫 WA（**不是**名字前两字母，实测 ak/ka/ch 在 MPQ 里不存在！
#        对照 `MonStats.txt`：Akara→PS、Kashya→RC、Charsi→CI、Gheed→GH、Warriv→WA、
#        DeckardCain→DC；**出处**：`d2data.mpq` 的 `data/global/excel/monstats.txt` 的 `Code` 列）
PLAYERS = (
    Unit('player', 'am', 'Chars/amazon', 'd2char.mpq', 'Amazon'),
    Unit('player', 'so', 'Chars/sorceress', 'd2char.mpq', 'Sorceress'),
    Unit('player', 'ne', 'Chars/necromancer', 'd2char.mpq', 'Necromancer'),
    Unit('player', 'pa', 'Chars/paladin', 'd2char.mpq', 'Paladin'),
    Unit('player', 'ba', 'Chars/barbarian', 'd2char.mpq', 'Barbarian'),
)

#   Act I 8 种怪物 —— sprite 代码逐条来自本项目配表 `Table/Monster.tsv` 的 `sprite` 列。
MONSTERS = (
    Unit('monster', 'fa', 'Monsters/fa', 'd2data.mpq', note='堕落者 Fallen'),
    Unit('monster', 'fs', 'Monsters/fs', 'd2data.mpq', note='堕落萨满 FallenShaman'),
    Unit('monster', 'si', 'Monsters/si', 'd2data.mpq', note='尖刺鼠 QuillRat'),
    Unit('monster', 'zm', 'Monsters/zm', 'd2data.mpq', note='僵尸 Zombie'),
    Unit('monster', 'cr', 'Monsters/cr', 'd2data.mpq', note='堕落罗格 DarkHunter'),
    Unit('monster', 'bk', 'Monsters/bk', 'd2data.mpq', note='血鹰 BloodHawk'),
    Unit('monster', 'ye', 'Monsters/ye', 'd2data.mpq', note='巨兽 GargantuanBeast'),
    Unit('monster', 'wr', 'Monsters/wr', 'd2data.mpq', note='幽灵 Ghost'),
)

NPCS = (
    Unit('npc', 'ps', 'Monsters/ps', 'd2data.mpq', note='阿卡拉 Akara'),
    Unit('npc', 'rc', 'Monsters/rc', 'd2data.mpq', note='卡夏 Kashya'),
    Unit('npc', 'ci', 'Monsters/ci', 'd2data.mpq', note='恰西 Charsi'),
    Unit('npc', 'gh', 'Monsters/gh', 'd2data.mpq', note='基德 Gheed'),
    Unit('npc', 'wa', 'Monsters/wa', 'd2data.mpq', note='瓦瑞夫 Warriv'),
)


# ═════════════════════════════════════════════════════════════════════════════
#  MPQ 读取（ctypes 包 StormLib；`_assets_src/storm.py` 已被验证可解加密 mpq）
# ═════════════════════════════════════════════════════════════════════════════
class Archive(object):
    """一个已打开的 MPQ + 名字索引（**大小写不敏感**）。"""

    def __init__(self, storm, path):
        self.storm = storm
        self.path = path
        self.handle = storm.open_archive(path)
        # ⚠️ MPQ 里存的是**反斜杠**路径；`SFileOpenFileEx` 用原件名最稳（大小写不敏感，
        #    但分隔符不要自己换）⇒ 索引键用正斜杠小写，读盘时用原件名。
        self.names = [n.replace('\\', '/') for n in storm.list_files(self.handle)]
        raw = list(storm.list_files(self.handle))
        self.by_lower = dict((n.replace('\\', '/').lower(), n) for n in raw)
        self._cache = {}

    def read(self, rel):
        """按相对路径读（大小写不敏感、分隔符正斜杠）；不存在返回 None。"""
        key = rel.lower()
        real = self.by_lower.get(key)
        if real is None:
            return None
        if key not in self._cache:
            self._cache[key] = self.storm.read_file(self.handle, real)
        return self._cache[key]

    def has_prefix(self, rel_prefix):
        p = rel_prefix.lower()
        return any(n.lower().startswith(p) for n in self.names)

    def list_prefix(self, rel_prefix):
        p = rel_prefix.lower()
        return sorted(n for n in self.names if n.lower().startswith(p))


# ═════════════════════════════════════════════════════════════════════════════
#  单位导出
# ═════════════════════════════════════════════════════════════════════════════
class LayerSource(object):
    """一个"要画的层"：组件码 + 它的 DCC 文件名 + 解码出的方向数据。"""

    __slots__ = ('component', 'dcc_rel', 'dcc', 'key')

    def __init__(self, component, dcc_rel):
        self.component = component
        self.dcc_rel = dcc_rel
        self.dcc = None          # dccmod.Dcc
        self.key = None


def unit_root(unit):
    """该单位在 MPQ 里的根目录（玩家在 `chars/`，怪物/NPC 在 `monsters/`）。"""
    if unit.kind == 'player':
        return 'data/global/chars/%s/' % unit.token
    return 'data/global/monsters/%s/' % unit.token


def cof_rel(unit, mode):
    """`.cof` 相对路径：`<root>/cof/<token><mode><weaponClass>.cof`（出处 `cof.py` 文件头）。"""
    return '%scof/%s%s%s.cof' % (unit_root(unit), unit.token, mode, unit.weapon_class)


def component_dcc_rel(unit, component, mode, weapon_class):
    """组件 DCC 相对路径：`<root>/<comp>/<token><comp><equip><mode><wc>.dcc`。

    出处 Diablerie `COF.cs:105-108` 的 `GetSpritesheetFilename(layer, equip)`：
    文件名 = `token + layer.name + equip + mode + **layer.weaponClass**`。
    ⚠️ 这里用的是**该层自己的 weaponClass**（来自 COF，逐层不同：亚马逊 hth COF 里
    `HD/LA/LG/S1/S2/SH/TR` 是 `1ht`、`RA` 是 `hth`），**不是**找 COF 时用的那个武器类别；
    写错会**静默取不到图**（只剩个别层能出图，其余被当"无变体"跳过）。
    """
    stem = '%s%s%s%s%s' % (unit.token, component, EQUIP, mode, weapon_class)
    return '%s%s/%s.dcc' % (unit_root(unit), component.lower(), stem)


def resolve_mode(arch, unit, mode):
    """找一个可用的模式代号：`mode` 本身 → `FALLBACK` 链。返回 (mode, cof_bytes) 或 (None, None)。"""
    for cand in (mode,) + FALLBACK.get(mode, ()):
        if cand == mode and mode in FALLBACK.get(mode, ()):
            continue
        data = arch.read(cof_rel(unit, cand))
        if data:
            return cand, data
    return None, None


#: 全部 16 个组件码（顺序 = 原版 `CompCode`，见 `cof.py::COMPONENT_CODES`）。
COMPONENTS = ('HD', 'TR', 'LG', 'RA', 'LA', 'RH', 'LH', 'SH',
              'S1', 'S2', 'S3', 'S4', 'S5', 'S6', 'S7', 'S8')

#: 无装备时**一定画**的身体组件（头/躯干/腿/左臂/右臂）。
BODY_COMPONENTS = ('HD', 'TR', 'LG', 'LA', 'RA')

#: 需要装备才画的组件（右手武器 / 左手武器 / 盾 / S1..S8 覆盖层）。
GEAR_COMPONENTS = ('RH', 'LH', 'SH', 'S1', 'S2', 'S3', 'S4', 'S5', 'S6', 'S7', 'S8')

#: 怪物/NPC 的"可画组件"白名单（还要与 `monstats.txt` 的逐组件标志取交集）。
#: 为什么把 `S2..S8` / `RH` / `LH` / `SH` 排除在外：
#:   · `RH/LH/SH` 的 DCC 命名里嵌的是**具体武器/盾代码**（axe/clb/ssd/buc…），
#:     没有 `lit` 这种"默认"变体，而 `monstats.txt` 也没给"用哪一把" ⇒ 选谁都只能靠猜；
#:   · `S2..S8` 是**逐怪自定义的附加覆盖层**（实测 `DarkHunter` 的 S3 是一团 58×189 的
#:     青色块，`Fallen` 的 S2/S8 只有 DT/S1 的变体），触发条件不在数据里 ⇒ 画出来是噪声；
#:   · `S1` 保留：它是**手里的武器覆盖层**（实测 `fa/cr/fs` 的 S1 都有 `lit` 且覆盖全部
#:     动作模式，画出来正好是"堕落者拎着棍子 / 堕落罗格握着剑"），是身体的一部分。
MONSTER_COMPONENTS = ('HD', 'TR', 'LG', 'LA', 'RA', 'S1')

#: 层选择策略（`--layers`）：
#:   `auto`（默认）= 玩家 `BODY_COMPONENTS`、怪物/NPC `MONSTER_COMPONENTS ∩ monstats 标志`；
#:   `body` / `all` = 强制所有单位都用该策略（人工比对用；`all` 会把 COF 里所有层都画出来，
#:   实测会带出 `cr` 的 S3 之类噪声，仅用于排查）。
LAYER_POLICY = 'auto'


def plan_layers(arch, unit, cof, mode):
    """按 COF 的层清单决定"要画哪几层"。

    规则（数据驱动，**不写死文件名**）：
      · 每层按 `token+组件码+equip+模式+**该层自己的 weaponClass**` 找 DCC；
      · `equip = "lit"`（原版"无甲身体/头发/怪物本体"那一套，见文件头）；
      · 组件白名单：玩家 = `BODY_COMPONENTS`（徒手，不出现幻影武器）；
        怪物/NPC = `MONSTER_COMPONENTS` ∩ `monstats.txt` 逐组件标志（见两个常量的注释）；
      · 文件存在但被白名单挡掉、或白名单允许但文件不存在 ⇒ **都记进 `skipped`**（可核对）。
    """
    # 每个单位"允许画"的组件集合：
    #   · 玩家：身体 `HD/TR/LG/LA/RA`（无装备 ⇒ 不画武器/盾，否则会出现幻影武器）；
    #   · 怪物/NPC：`MONSTER_COMPONENTS` ∩ `monstats.txt` 的逐组件标志（1 = 该怪有这一层）。
    if unit.kind == 'player':
        allowed = set(BODY_COMPONENTS)
    else:
        allowed = set(c for c in MONSTER_COMPONENTS
                      if unit.component_flags.get(c, False))
    if LAYER_POLICY == 'body':
        allowed = set(BODY_COMPONENTS)
    elif LAYER_POLICY == 'all':
        allowed = set(COMPONENTS)

    drawn = []
    skipped = []
    for layer in cof.layers:
        comp = layer.component_code
        rel = component_dcc_rel(unit, comp, mode, layer.weapon_class)
        exists = bool(arch.read(rel))
        if comp not in allowed:
            if unit.component_flags.get(comp) is False:
                why = 'monstats 标志=0（该怪没有这一层）'
            elif unit.kind == 'player':
                why = '玩家徒手/无装备 ⇒ 不画武器/盾（原版行为）'
            elif comp in GEAR_COMPONENTS and comp not in MONSTER_COMPONENTS:
                why = '不在本项目可画清单（RH/LH/SH 需要具体武器/盾代码；S2..S8 触发条件不明）'
            else:
                why = '策略（--layers）'
            skipped.append('layer#%d(%s,wc=%s)：不画（%s；文件%s）'
                           % (layer.index, comp, layer.weapon_class, why,
                              '存在' if exists else '不存在'))
            continue
        if comp in GEAR_COMPONENTS and unit.kind == 'player':
            skipped.append('layer#%d(%s,wc=%s)：装备层（%s）⇒ 徒手/无装备时不画'
                           '（文件%s）' % (layer.index, comp, layer.weapon_class,
                                        '武器/盾/覆盖层', '存在' if exists else '不存在'))
            continue
        if exists:
            drawn.append(LayerSource(comp, rel))
        else:
            skipped.append('layer#%d(%s,wc=%s) 无 %s 变体 ⇒ 跳过（%s）'
                           % (layer.index, comp, layer.weapon_class, EQUIP,
                              'DCC 不存在' if not exists else '策略'))
    return drawn, skipped


def measure(arch, unit, mode, cof, sources):
    """量出该 (单位, 动作) 下所有**逻辑方向**的合成包围盒（只读帧头，不解像素）。

    `d` = **逻辑方向序号**（0..7，顺序 = `Def.Dir8` = `DIR_NAMES` 的下标）；
    文件内的方向槽位由 `to_file_slot(dirs, d)` 换算（**不许**再拿 `d` 当槽位用 —— 那是历史缺陷）。
    """
    info = {}
    for d in range(LOGICAL_DIRS):
        union = None
        for src in sources:
            data = arch.read(src.dcc_rel)
            if data is None:
                continue
            try:
                hdr = _direction_boxes(data)
            except ValueError as exc:
                print('    [WARN] %s: %s' % (src.dcc_rel, exc))
                continue
            # ⚠️ 放在 `try` 之外：方向表不认识 ⇒ 直接抛（⛔ 不许被 WARN 吞掉后静默继续）
            di = to_file_slot(hdr['dirs'], d)
            box = hdr['dir_boxes'][di]
            union = _union(union, box)
        info[d] = union
    return info


def _direction_boxes(data):
    """**只读头**：返回 {'dirs': N, 'fpd': F, 'dir_boxes': [Rect,...]}（不解像素）。"""
    import struct as _struct
    if len(data) < 12:
        raise ValueError('DCC 太短')
    if data[0] != dccmod.DCC_SIGNATURE:
        raise ValueError('不是 DCC（签名 0x%02X）' % data[0])
    dirs = data[2]
    fpd = _struct.unpack_from('<i', data, 3)[0]
    tag = _struct.unpack_from('<i', data, 7)[0]
    if tag != 1:
        raise ValueError('header.tag=%d ≠ 1' % tag)
    offs = [_struct.unpack_from('<i', data, 15 + 4 * i)[0] for i in range(dirs)]
    boxes = []
    for off in offs:
        bm = dccmod.BitReader(data, off * dccmod.DIR_OFFSET_MULT)
        bm.bits(32)
        bm.bits(2)
        f = [dccmod.CRAZY_BIT_TABLE[bm.bits(4)] for _ in range(7)]
        v0b, wb, hb, xb, yb, ob, cb = f
        minx = miny = 1 << 30
        maxx = maxy = -(1 << 30)
        for _i in range(fpd):
            bm.bits(v0b)
            w = bm.bits(wb)
            h = bm.bits(hb)
            xo = bm.signed(xb)
            yo = bm.signed(yb)
            bm.bits(ob)
            bm.bits(cb)
            bm.bit()
            left = xo
            top = yo - h + 1
            minx = min(minx, left)
            miny = min(miny, top)
            maxx = max(maxx, left + w)
            maxy = max(maxy, top + h)
        boxes.append(dccmod.Rect(minx, miny, maxx - minx, maxy - miny))
    return {'dirs': dirs, 'fpd': fpd, 'dir_boxes': boxes}


def _union(a, b):
    if a is None:
        return dccmod.Rect(b.left, b.top, b.width, b.height)
    left = min(a.left, b.left)
    top = min(a.top, b.top)
    right = max(a.left + a.width, b.left + b.width)
    bottom = max(a.top + a.height, b.top + b.height)
    return dccmod.Rect(left, top, right - left, bottom - top)


def export_unit(arch, unit, out_root, palette):
    """导出一个单位。返回统计 dict。"""
    print('== %s（%s，%s，武器类别=%s）%s'
          % (unit.out_dir, unit.token, unit.mpq, unit.weapon_class, unit.note))

    # ① 逐动作解析 COF + 定层；记下每个动作的帧数/方向数/画布
    actions = []          # (action_name, mode_used, cof, sources, skipped, per_dir_box)
    min_left = min_right = min_top = min_bottom = 0
    for action, mode in ACTIONS:
        mode_used, cof_bytes = resolve_mode(arch, unit, mode)
        if mode_used is None:
            print('   [WARN] %s：%s（%s）的 COF 不存在，且回退链 %s 也没有 ⇒ 该动作跳过'
                  % (action, mode, unit.token, FALLBACK.get(action)))
            continue
        cof = cofmod.parse(cof_bytes, cof_rel(unit, mode_used))
        sources, skipped = plan_layers(arch, unit, cof, mode_used)
        if not sources:
            print('   [WARN] %s/%s：没有可画的层 ⇒ 跳过' % (action, mode_used))
            continue
        per_dir = measure(arch, unit, mode_used, cof, sources)
        # 画布范围 = 各方向包围盒相对原点的最大伸展
        for d, box in per_dir.items():
            if box is None:
                continue
            min_left = min(min_left, box.left)
            min_right = max(min_right, box.left + box.width)
            min_top = min(min_top, box.top)
            min_bottom = max(min_bottom, box.top + box.height)
        actions.append([action, mode_used, cof, sources, skipped, per_dir])
        note = '' if mode_used == mode else '（回退：%s→%s）' % (mode, mode_used)
        print('   %-6s %s%s：层=%s 跳过=%d 帧/向=%d' % (
            action, mode_used, note,
            ','.join(s.component.upper() for s in sources), len(skipped), cof.frames_per_dir))
        for s in skipped:
            print('        · ' + s)

    if not actions:
        print('   [FAIL] %s 没有任何可导出的动作' % unit.out_dir)
        return None

    # ② 画布：原点落在正中 ⇒ Unity 轴心恒为 (0.5, 0.5)
    half_w = max(abs(min_left), abs(min_right))
    half_h = max(abs(min_top), abs(min_bottom))
    canvas_w = max(2, 2 * half_w)
    canvas_h = max(2, 2 * half_h)
    origin_x = canvas_w // 2
    origin_y = canvas_h // 2
    print('   画布 %dx%d（原点在图内 (%d,%d) = 正中 ⇒ pivot=(0.5,0.5)）'
          % (canvas_w, canvas_h, origin_x, origin_y))

    out_dir = os.path.join(out_root, unit.out_dir.replace('/', os.sep))
    os.makedirs(out_dir, exist_ok=True)

    # ③ 逐动作 × 方向 × 帧：合成 + 垫进画布 + 落盘
    stats = collections.OrderedDict()
    total = 0
    for action, mode_used, cof, sources, skipped, per_dir in actions:
        # 解出各层该动作的 DCC（每个 (层, 方向) 解一次，按需）
        fpd = cof.frames_per_dir
        frames_written = None
        for d in range(LOGICAL_DIRS):
            # ⚠️ `d` = **逻辑方向序号**（0..7，顺序 = `Def.Dir8` = `DIR_NAMES`）：
            #   文件名用它、COF 的 priority 用它；**DCC 的文件槽位**由 `to_file_slot` 单独换算。
            union = per_dir.get(d)
            if union is None:
                print('        [WARN] %s dir=%s 无包围盒 ⇒ 跳过' % (action, DIR_NAMES[d]))
                continue
            # 各层解方向
            layers = []
            for src in sources:
                data = arch.read(src.dcc_rel)
                if data is None:
                    continue
                try:
                    dec = dccmod.parse(data, src.dcc_rel)
                except ValueError as exc:
                    print('        [WARN] 解 %s 失败：%s' % (src.dcc_rel, exc))
                    continue
                di = to_file_slot(dec.direction_count(), d)
                layers.append((src, dec.directions[di]))
            if not layers:
                continue

            # 绘制顺序 = COF.priority（后 → 前）—— COF 内部方向就是**逻辑方向**（无 DirectionMapping 那一层）
            order = cof.draw_order(d * cof.num_directions // LOGICAL_DIRS, 0)

            for fidx in range(fpd):
                idx = bytearray(canvas_w * canvas_h)      # 0 = 透明
                for li in order:
                    if li >= len(cof.layers):
                        continue
                    comp = cof.layers[li].component_code
                    hit = None
                    for src, direction in layers:
                        if src.component == comp:
                            hit = direction
                            break
                    if hit is None:
                        continue
                    if fidx >= len(hit.frames):
                        continue
                    fr = hit.frames[fidx]
                    bw = hit.box.width
                    # 该层该帧在画布里的左上角
                    dst_x = origin_x + hit.box.left
                    dst_y = origin_y + hit.box.top
                    for y in range(hit.box.height):
                        dy = dst_y + y
                        if dy < 0 or dy >= canvas_h:
                            continue
                        srow = y * bw
                        drow = dy * canvas_w + dst_x
                        for x in range(bw):
                            v = fr[srow + x]
                            if v:
                                dx = dst_x + x
                                if 0 <= dx < canvas_w:
                                    idx[drow + x] = v
                rgba = dccmod.frame_rgba(idx, palette)
                name = '%s_%s_%d.png' % (action, DIR_NAMES[d], fidx)
                dccmod.write_png_rgba(os.path.join(out_dir, name), rgba, canvas_w, canvas_h)
                total += 1
                if frames_written != fpd:
                    frames_written = fpd
        stats[action] = {'mode': mode_used, 'frames': fpd,
                         'dirs': len([1 for d in range(LOGICAL_DIRS) if per_dir.get(d) is not None]),
                         'layers': [s.component.upper() for s in sources],
                         'skipped': skipped}

    # ④ manifest（审计用；与 export_tiles.py 的做法一致）
    manifest = {
        'unit': unit.out_dir,
        'kind': unit.kind,
        'token': unit.token,
        'sourceMpq': unit.mpq,
        'sourcePath': unit_root(unit),
        'palette': PALETTE_REL,
        'equip': EQUIP,
        'weaponClass': unit.weapon_class,
        'canvas': {'w': canvas_w, 'h': canvas_h, 'originX': origin_x, 'originY': origin_y,
                   'pivot': [0.5, 0.5],
                   'extent': {'left': min_left, 'right': min_right,
                              'up': min_top, 'down': min_bottom}},
        'directions': list(DIR_NAMES),
        'actions': stats,
        'pngCount': total,
    }
    with open(os.path.join(out_dir, 'manifest.json'), 'w', encoding='utf-8') as fh:
        json.dump(manifest, fh, ensure_ascii=False, indent=1)
    print('   → %d 张 PNG + manifest.json' % total)
    return manifest


def emit_cs(manifests, path):
    """生成 `Module/View/SpriteFrameCounts.cs`（帧数唯一来源；`SpriteFrames` 引用它）。

    表里的动作 = `CS_ACTIONS`（**片 2b 起 = `ACTIONS` = `ViewAnim` 的 7 个动作，含 `run`**）。
    为什么必须与 `ViewAnim` 一一对应： `SpriteFrameCounts.ByUnit` 的 int[] **下标 = `ViewAnim`**
    （`Of(unitKey, anim)` 取 `(int)anim`）⇒ 列的顺序/个数必须与该枚举完全一致。
    片 2a 时 `ViewAnim` 还没有 `Run`（下标 6 不存在）⇒ 那时只写 6 列（多写就是没有消费方的死数据）；
    **片 2b 把 `Run = 6` 加进 `ViewAnim` 之后**，`CS_ACTIONS` 才能打开到 7 列（本函数无需再改）。
    ⛔ 本函数刻意**不动**生成文本里的任何字面量（「N 个动作」由 `len(CS_ACTIONS)` 算出）。
    """
    lines = []
    lines.append('// ─────────────────────────────────────────────────────────────────────────────')
    lines.append('// Diablo2 · Module/View/SpriteFrameCounts.cs')
    lines.append('// ⚠️ **本文件是生成物，禁止手改**（生成器：`tools/d2codec/export_chars.py --emit-cs`）。')
    lines.append('//')
    lines.append('// 数据来源 = 原版 `.cof` 的 `framesPerDirection`（逐动作一份，见各单位目录的 manifest.json）；')
    lines.append('//   原版素材取自 Diablo II (Blizzard North, 2000) 的 d2char.mpq / d2data.mpq，本项目非商用。')
    lines.append('//')
    lines.append('// 为什么必须有这张表：`SpriteFrames.FrameCounts` 是按**动作**给一个数组的，')
    lines.append('//   而原版**每个单位的每个动作帧数都不同**（例：亚马逊 NU=8 帧、DT=15 帧；')
    lines.append('//   堕落者 TR 层只有 6 帧的走路）⇒ 用一份全局常量会让"帧键"指向不存在的图（静默缺图）。')
    lines.append('// ⛔ 改这张表 = 重跑导出器；手改必然与磁盘上的 PNG 数量对不上。')
    lines.append('// ─────────────────────────────────────────────────────────────────────────────')
    lines.append('')
    lines.append('using System.Collections.Generic;')
    lines.append('')
    lines.append('namespace Diablo2.Module.View')
    lines.append('{')
    lines.append('    /// <summary>逐单位 / 逐动作的真实帧数（生成物，见文件头）。</summary>')
    lines.append('    internal static class SpriteFrameCounts')
    lines.append('    {')
    lines.append('        /// <summary>动作名（与 <see cref="SpriteFrames.Keys"/> 的拼法一致）的下标顺序。</summary>')
    lines.append('        public static readonly string[] ActionNames =')
    lines.append('        {')
    lines.append('            ' + ', '.join('"%s"' % a for a, _mode in CS_ACTIONS) + ',')
    lines.append('        };')
    lines.append('')
    lines.append('        /// <summary>')
    lines.append('        /// `单位目录键 → %d 个动作的帧数`（下标同 <see cref="ActionNames"/>）。'
                 % len(CS_ACTIONS))
    lines.append('        /// 单位目录键 = `ResPaths.CharDir` / `ResPaths.MonsterDir` 的实参（小写）。')
    lines.append('        /// </summary>')
    lines.append('        public static readonly Dictionary<string, int[]> ByUnit =')
    lines.append('            new Dictionary<string, int[]>')
    lines.append('            {')
    for m in manifests:
        key = m['unit'].split('/')[-1].lower()
        counts = []
        for a, _mode in CS_ACTIONS:      # ⚠️ 列的顺序/个数必须 = `ViewAnim`（片 2b 起含 run = 7 列）
            st = m['actions'].get(a)
            counts.append(st['frames'] if st else 0)
        lines.append('                { "%s", new[] { %s } },   // %s'
                     % (key, ', '.join(str(c) for c in counts), m.get('token', '')))
    lines.append('            };')
    lines.append('')
    lines.append('        /// <summary>取某单位某动作的帧数；未登记返回 0（调用方按"缺图"处理）。</summary>')
    lines.append('        public static int Of(string unitKey, ViewAnim anim)')
    lines.append('        {')
    lines.append('            if (string.IsNullOrEmpty(unitKey)) return 0;')
    lines.append('            int[] counts;')
    lines.append('            if (!ByUnit.TryGetValue(unitKey.ToLowerInvariant(), out counts)) return 0;')
    lines.append('            var i = (int)anim;')
    lines.append('            if (counts == null || i < 0 || i >= counts.Length) return 0;')
    lines.append('            return counts[i];')
    lines.append('        }')
    lines.append('    }')
    lines.append('}')
    lines.append('')
    with open(path, 'w', encoding='utf-8', newline='\n') as fh:
        fh.write('\n'.join(lines))
    print('→ %s' % path)


def main(argv):
    d2assets = DEFAULT_D2ASSETS
    out_root = DEFAULT_OUT
    cs_path = DEFAULT_CS
    only = None
    want_classes = None
    if '--d2assets' in argv:
        d2assets = argv[argv.index('--d2assets') + 1]
    if '--out' in argv:
        out_root = argv[argv.index('--out') + 1]
    if '--emit-cs' in argv:
        cs_path = argv[argv.index('--emit-cs') + 1]
    if '--no-cs' in argv:
        cs_path = None
    if '--only' in argv:
        only = set(x.lower() for x in argv[argv.index('--only') + 1].split(','))
    if '--all-classes' in argv:
        want_classes = True
    if '--layers' in argv:
        global LAYER_POLICY
        LAYER_POLICY = argv[argv.index('--layers') + 1]
        if LAYER_POLICY not in ('auto', 'body', 'all'):
            print('--layers 只接受 auto / body / all')
            return 2

    # Windows 控制台默认 GBK，输出里的 ⇒/· 会抛 UnicodeEncodeError ⇒ 强制 UTF-8（替换非法字符）
    try:
        sys.stdout.reconfigure(encoding='utf-8', errors='replace')
    except Exception:
        pass

    sys.path.insert(0, d2assets)
    try:
        import storm
    except ImportError:
        print('找不到 storm.py（%s）—— 需要 `_assets_src/storm.py` 才能读 MPQ' % d2assets)
        return 2

    mpqs = os.path.join(d2assets, 'd2mpq')
    arcs = {}
    for name in ('d2char.mpq', 'd2data.mpq'):
        p = os.path.join(mpqs, name)
        if not os.path.exists(p):
            print('缺少数据包：%s' % p)
            return 2
        arcs[name] = Archive(storm, p)

    pal_rel = PALETTE_REL
    pal_bytes = None
    for a in arcs.values():
        pal_bytes = a.read(pal_rel)
        if pal_bytes:
            break
    if not pal_bytes:
        print('找不到单位调色板 %s' % pal_rel)
        return 2
    # ⚠️ 调色板临时文件写到**系统临时目录**：绝不能落进 `Assets/`（会被 Unity 当资源导入）
    import tempfile
    tmp_pal = os.path.join(tempfile.gettempdir(), 'd2_units_pal.dat')
    with open(tmp_pal, 'wb') as fh:
        fh.write(pal_bytes)
    palette = dccmod.read_pl2(tmp_pal)
    print('调色板 %s（%d 字节）' % (pal_rel, len(pal_bytes)))

    units = list(PLAYERS if want_classes else PLAYERS[:1]) + list(MONSTERS) + list(NPCS)
    if only:
        units = [u for u in units if u.token in only or u.out_dir.split('/')[-1] in only]

    # 每个单位的 COF 武器类别 + 可画组件：玩家固定 hth / 只画身体；
    # 怪物/NPC 取 monstats.txt 的 BaseW + 逐组件标志（见 `load_monstats` 的注释）
    stats = load_monstats(arcs['d2data.mpq'])
    for u in units:
        if u.kind == 'player':
            u.weapon_class = PLAYER_WEAPON_CLASS
            continue
        rec = stats.get(u.token)
        if rec:
            u.weapon_class = rec['basew'] or PLAYER_WEAPON_CLASS
            u.component_flags = rec['flags']
        else:
            u.weapon_class = PLAYER_WEAPON_CLASS
            print('[WARN] %s（%s）在 monstats.txt 里没有记录 ⇒ 退回 wc=%s、组件按 COF 全画'
                  % (u.out_dir, u.token, PLAYER_WEAPON_CLASS))

    manifests = []
    for u in units:
        arch = arcs[u.mpq]
        if not arch.has_prefix(unit_root(u)):
            print('== SKIP %s：%s 里没有 %s' % (u.out_dir, u.mpq, unit_root(u)))
            continue
        m = export_unit(arch, u, out_root, palette)
        if m:
            manifests.append(m)

    print()
    print('合计：%d 个单位，%d 张 PNG' % (len(manifests), sum(m['pngCount'] for m in manifests)))
    if cs_path:
        emit_cs(manifests, cs_path)
    return 0


if __name__ == '__main__':
    sys.exit(main(sys.argv))
