# -*- coding: utf-8 -*-
"""
把**暗黑破坏神 II 1.10f 官方数据表**程序化抽取为本项目源表 `<项目根>/策划/数值文档/<名>_c.txt`。

═══════════════════════════════════════════════════════════════════════════════
一、输入 / 输出
═══════════════════════════════════════════════════════════════════════════════
输入（官方原始表，**tab 分隔、首行字段名、cp1252 编码**；脚本先探测编码再用它解码）：
    <官方 1.10f txt 目录>\\data\\global\\excel\\*.txt
    —— 目录**不写死**，优先取命令行参数 `--src <dir>`，其次取环境变量 `D2SRC_DIR`；
       两者都没给时用项目内默认副本 `<项目根>/原版资源/参考工程_Diablerie/d2lod1.10txt/data/global/excel`
       （⚠️ 旧版本这里写死过 `_assets_tmp\\d2src\\d2lod1.10txt\\...`，那个目录已随工作区清理删除 —— 见文件头 ★ BL-6）
输出（4 行表头，tab 分隔，UTF-8 无 BOM）：
    <项目根>\\策划\\数值文档\\{class,start_item,experience,monster,level,skill,item,affix,monumod,treasureclass,missile}_c.txt

用法：
    python convert.py                                      # 抽取全部 10 张表（用默认输入目录）
    python convert.py --src D:\\d2txt\\data\\global\\excel      # 指定输入目录（等价于设 D2SRC_DIR）
    python convert.py --report                             # 只打印统计（各表行数 / 过滤命中数）不写文件
    python convert.py --names                              # 打印"需要中文名但映射表里没有"的官方标识（用于补 cn_names.py）

**所有数值都取自官方 txt，没有任何手抄/凭印象的数值**；中文显示名由 `cn_names.py` 提供
（官方 1.10f txt 里只有英文名 —— 见该文件头部的"待复核"说明）。

═══════════════════════════════════════════════════════════════════════════════
二、换算公式（每条都能在官方表里指出出处，不得改动）
═══════════════════════════════════════════════════════════════════════════════
1. 职业（charstats.txt）：`LifePerLevel / StaminaPerLevel / ManaPerLevel / LifePerVitality /
   StaminaPerVitality / ManaPerMagic` 这 6 列**官方注释为 "The following are in fourths"**
   （见 charstats.txt 的 `Comment` 列），即单位为 1/4 点 ⇒ 本脚本一律 **÷4** 后落表。
2. 经验（experience.txt）：直接取官方累计经验值；**官方 1.10f 里 8 个职业列完全相同**
   （脚本会断言校验），故只需一列 `exp`。99 级经验 3837739017 > int32 ⇒ 类型用 `int64`。
3. 怪物（MonStats.txt + MonLvl.txt）：官方 `MonStats` 的 hp/ac/ar/dmg/exp 都是**基值**，
   需按**区域等级**到 `MonLvl.txt` 取倍率（百分数）换算：
        hp   = (minHP + maxHP) / 2 × MonLvl.HP  / 100
        ac   = AC                     × MonLvl.AC  / 100
        ar   = A1TH                   × MonLvl.TH  / 100
        dmg  = A1MinD / A1MaxD        × MonLvl.DM  / 100
        exp  = Exp                    × MonLvl.XP  / 100
   倍率取 **普通难度**列（`AC/TH/HP/DM/XP`，非 `L-*` 列）。三处区域的区域等级
   = Levels.txt 的 `MonLvl1`：罗格营地 0（无怪）、血腥荒野 1、邪恶洞穴 1 ⇒ 怪物统一按
   **等级 1** 换算（`MonStats.Level` 另存 `base_level` 列，不做静默丢弃）。
4. 技能（skills.txt）：官方伤害值需按 `HitShift` 移位换算：
        dmg = 原值 × 2^HitShift / 256
   （例：火弹 EMin=6/EMax=12、HitShift=7 ⇒ 3~6，与原版一致）。物理取 `MinDam/MaxDam`，
   元素取 `EMin/EMax`；`dmg_min/dmg_max` 为**物理+元素合计**（按上式换算后相加）。
5. 物品（Weapons/Armor/Misc）：`code == normcode` ⇒ 普通品质（非凡/精英变体是另外的行，
   本表不导入）；品质之外的数值（伤害/防御/价格/网格）全部取官方原值，不做换算。
   双手武器的伤害在 `2handmindam/2handmaxdam` 列（`mindam/maxdam` 为空），脚本自动回退。
6. 词缀（MagicPrefix/MagicSuffix）：只导入 `spawnable=1` 且 `level <= 12`（Act I 起始区域
   可出现的词缀等级）；`mod/min/max` 取 `mod1code/mod1min/mod1max`。
7. 精英词缀（MonUMod.txt）：该表的 `constants` 列 + `*constant desc` 列给出**引擎常数**，
   例如 `none` 行 = "champion chance" 20（%）、`leveladd` 行 = "champion +hp%" 200、
   `curse` 行 = "unique +hp%" 300、`durieldead` 行 = "champion +dmg%" 100、
   `partydead` 行 = "unique +dmg% (strong)" 150。脚本按 `*constant desc` 文本**程序化匹配**
   这些常数，换算成倍率：`hp_mul = 1 + hp% / 100`、`dmg_mul = 1 + dmg% / 100`。
   ⚠️ 官方 MonUMod.txt **没有**定义"护甲倍率"与"抗性加成"常数（这两项在原版引擎里是硬编码），
   故 `ac_mul` 填 1.0、`res_*` 填 0 —— 这是**登记在案的占位**，不是从官方表抽的数值。
8. 掉落表（TreasureClassEx.txt）：从**普通难度**的 "Act 1*" TC 出发做引用闭包，
   `Item1..Item10` 若指向另一个 TC 则一并导入（否则表不可用）。
1b. 起始装备（charstats.txt 的 `item1..item10` / `itemNloc` / `itemNcount`）：
   **不做任何换算**，逐格搬运；官方空位是 `code='0' / count='0'` 的占位 ⇒ 剔除（否则会指向不存在的物品）。
   官方口径：新角色只有「武器 + 盾 + 药水 + 卷轴」，**没有**衣服 / 鞋子 / 头盔（charstats 里就没有这些行）。

9. 投射物（Missiles.txt）：只取"被用到的"——本表 150 个技能引用的 `srvmissile/cltmissile`
   等列 + 8 种怪物的 `Miss*` 列 + 基础 `arrow`；伤害列在官方表里常为空（元素伤害落在**技能**上），
   此时 `dmg_min/dmg_max/e_min/e_max` 为 0（官方原值如此，不做推测）。

取整约定：换算结果一律**四舍五入**（见 `rnd()` 的说明）—— 原版引擎走整数截断，但截断会把
低级怪物按 6% 护甲倍率算出的防御直接抹成 0（血鹰 AC 0.84 → 0），故本项目改为四舍五入并在
`docs/配表说明.md` 登记该实现细节。
"""

import argparse
import os
import random
import sys

# Windows 控制台默认 GBK，中文/特殊符号会抛 UnicodeEncodeError ⇒ 统一改用 UTF-8 输出。
try:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    sys.stderr.reconfigure(encoding="utf-8", errors="replace")
except Exception as exc:                      # 老版本解释器没有 reconfigure
    print(f"[warn] stdout 无法切到 utf-8: {exc}")

HERE = os.path.dirname(os.path.abspath(__file__))
PROJECT_ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
#   随工作区清理**已不存在**（那一整棵 `_assets_tmp` 都没了）⇒ 打表脚本曾经**跑不起来**。
#   现存唯一副本 = 参考工程自带的 1.10 官方 txt（92 张，**含** `SkillCalc.txt` / `MonLvl.txt` /
#   `MonStats2.txt`，比 `原版资源/d2raw/data/global/excel` 那 55 张（经典版）更全）。
#   用相对 PROJECT_ROOT 的路径写，避免再写死盘符。
SRC_DIR = os.path.join(PROJECT_ROOT, "原版资源", "参考工程_Diablerie",
                       "d2lod1.10txt", "data", "global", "excel")
# 上面这份是本项目内的**默认**输入目录（相对 PROJECT_ROOT，故换机器/换盘符都不会失效）；
# 真正生效的值在 main() 里按 `--src` > `D2SRC_DIR` > 本默认值 的优先级解析后覆盖 SRC_DIR。
DEFAULT_SRC_DIR = SRC_DIR
ENV_SRC_VAR = "D2SRC_DIR"
OUT_DIR = os.path.join(PROJECT_ROOT, "策划", "数值文档")

try:
    from cn_names import (CLASS_CN, LEVEL_CN, MONSTER_CN, MONUMOD_CN, MISSILE_CN,
                          SKILL_CN, DAMAGE_TYPE_CN, TREE_CN)
    try:
        from cn_names import ITEM_CN
    except ImportError:                     # 物品中文名还没补时不算致命
        ITEM_CN = {}
except ImportError:                         # cn_names.py 缺失时全部退化为官方英文名
    CLASS_CN = LEVEL_CN = MONSTER_CN = MONUMOD_CN = MISSILE_CN = SKILL_CN = {}
    DAMAGE_TYPE_CN = {}
    TREE_CN = {}
    ITEM_CN = {}

MISSING_NAMES = {"class": set(), "level": set(), "monster": set(), "monumod": set(),
                 "missile": set(), "skill": set(), "item": set()}

# 审计辅助：官方标识 → 官方英文名（--names 模式打印，供人工写中文映射）
OFFICIAL_NAME = {"skill": {}, "item": {}, "missile": {}, "monster": {}, "level": {},
                 "monumod": {}, "class": {}}

# 同类告警只打一次，避免刷屏
_WARNED = set()


def warn_once(key, msg):
    if key in _WARNED:
        return
    _WARNED.add(key)
    print(msg)

# ═══════════════════════════════════════════════════════════════════════════
# 官方表读取
# ═══════════════════════════════════════════════════════════════════════════
_CACHE = {}


def table(name):
    """读官方 txt；先探测编码（cp1252 优先，回退 utf-8），返回 (表头, 行字典列表)。"""
    if name in _CACHE:
        return _CACHE[name]
    path = os.path.join(SRC_DIR, name)
    raw = open(path, "rb").read()
    text = None
    used_enc = None
    for enc in ("cp1252", "utf-8"):
        try:
            text = raw.decode(enc)
            used_enc = enc
            break
        except UnicodeDecodeError:
            continue
    if text is None:
        text = raw.decode("latin-1")
        used_enc = "latin-1"
        print(f"[warn] {name}: cp1252/utf-8 都解不开，退回 {used_enc}")
    rows = [l.split("\t") for l in text.replace("\r\n", "\n").split("\n") if l.strip() != ""]
    hdr = rows[0]
    data = []
    for r in rows[1:]:
        if len(r) > len(hdr):
            print(f"[warn] {name}: 数据行列数({len(r)}) > 表头列数({len(hdr)})，多余列忽略")
        data.append({h: (r[i] if i < len(r) else "") for i, h in enumerate(hdr)})
    _CACHE[name] = (hdr, data)
    print(f"[info] 读入 {name}: enc={used_enc} 列数={len(hdr)} 数据行={len(data)}")
    return _CACHE[name]


def col(row, name, default=""):
    v = row.get(name)
    return default if v is None or v == "" else v


def as_int(row, name, default=0):
    v = col(row, name)
    if v == "":
        return default
    try:
        return int(float(v))
    except ValueError:
        print(f"[warn] 字段 {name}={v!r} 不是数字，按 {default} 处理")
        return default


def as_float(row, name, default=0.0):
    v = col(row, name)
    if v == "":
        return default
    try:
        return float(v)
    except ValueError:
        print(f"[warn] 字段 {name}={v!r} 不是数字，按 {default} 处理")
        return default


def div4(row, name):
    """charstats 的 1/4 单位列 → 实际值。"""
    return as_float(row, name) / 4.0


def rnd(v):
    """四舍五入取整（0.5 进位）。

    原版引擎走整数截断；本项目取四舍五入 —— 否则按 MonLvl 等级 1 的 6% 护甲倍率
    会把低级怪物的防御直接截成 0（例：血鹰 AC=14×6% = 0.84 → 截断 0，四舍五入 1）。
    该取整规则在 `docs/配表说明.md` 里登记，属**已声明的实现细节**，不是官方数值。
    """
    return int(v + 0.5) if v >= 0 else -int(-v + 0.5)


def trunc_pct(v, pct):
    """官方 `D2ApplyPercent` 的口径：`(v × pct) / 100` **向零截断**（不是四舍五入）。

    ★ 片 13（消除 **E30**）：`monster_c` 的 8 个换算列改成用本函数。旧实现走 `rnd()`
    （四舍五入），与官方**差 ±1**（13 项，逐格清单见 `策划/自审对比/数值对照.md` §1.1）。
    例：血鹰 AC = 14 × 6% = 0.84 ⇒ 官方 **0**、旧 `rnd` 给 1。
    依据：引擎 `MONSTER_CalculateLevelScaledStats`；参考实现 `libd2/.../montable.zig:256-286`。
    """
    return int(v * pct / 100) if v >= 0 else -int(-v * pct / 100)


def _name_map(kind):
    return {"class": CLASS_CN, "level": LEVEL_CN, "monster": MONSTER_CN,
            "monumod": MONUMOD_CN, "missile": MISSILE_CN, "skill": SKILL_CN,
            "item": ITEM_CN}[kind]


def cn_names_lookup(kind, key):
    """只查表，不登记缺失（--names 审计用）。"""
    return _name_map(kind).get(key, "")


def cn(kind, key, fallback=None):
    """取中文名；缺映射时登记到 MISSING_NAMES 并退回官方英文标识。"""
    m = _name_map(kind)
    if key in m:
        return m[key]
    MISSING_NAMES[kind].add(key)
    warn_once(f"cn-{kind}-{key}",
              f"[warn] {kind} 缺中文映射 {key!r}，暂用官方英文标识"
              + (f"（官方名：fallback={fallback!r}）" if fallback else ""))
    return fallback if fallback is not None else key


def fmt(v):
    if isinstance(v, float):
        if v == int(v):
            return str(int(v))
        return f"{v:.4f}".rstrip("0").rstrip(".")
    return str(v)


# ═══════════════════════════════════════════════════════════════════════════
# 源表写出（4 行表头：字段名 / 类型 / 端标记 / 中文注释）
# ═══════════════════════════════════════════════════════════════════════════
SHEETS = {}


class Sheet(object):
    def __init__(self, logical, title):
        self.logical = logical
        self.title = title
        self.cols = []          # (字段名, 类型, 中文注释)
        self.rows = []

    def col(self, name, typ, comment):
        for c in self.cols:
            assert c[0] != name, f"{self.logical}: 列名重复 {name}"
        self.cols.append((name, typ, comment))

    def add(self, *values):
        assert len(values) == len(self.cols), \
            f"{self.logical}: 行列数 {len(values)} != 列数 {len(self.cols)}"
        self.rows.append(values)

    def pk(self):
        return self.cols[0][0]

    def dump(self, out_dir):
        pk = self.pk()
        seen = set()
        for r in self.rows:
            key = fmt(r[0])
            assert key != "", f"{self.logical}: 主键为空的行"
            assert key not in seen, f"{self.logical}: 主键重复 {key}"
            seen.add(key)
        path = os.path.join(out_dir, f"{self.logical}_c.txt")
        lines = ["\t".join(c[0] for c in self.cols),
                 "\t".join(c[1] for c in self.cols),
                 "\t".join(["c"] * len(self.cols)),
                 "\t".join(c[2] for c in self.cols)]
        for r in self.rows:
            cells = []
            for v in r:
                s = fmt(v)
                if "\t" in s:
                    print(f"[warn] {self.logical}: 单元格含 tab，已替换为空格")
                    s = s.replace("\t", " ")
                cells.append(s)
            lines.append("\t".join(cells))
        with open(path, "w", encoding="utf-8", newline="\n") as f:
            f.write("\n".join(lines) + "\n")
        return path, len(self.rows), len(self.cols)


def sheet(logical, title):
    s = Sheet(logical, title)
    SHEETS[logical] = s
    return s


# ═══════════════════════════════════════════════════════════════════════════
# 1) class_c —— charstats.txt
# ═══════════════════════════════════════════════════════════════════════════
CLASS_ORDER = ["Amazon", "Sorceress", "Necromancer", "Paladin", "Barbarian"]
SKILL_CLASS = {"Amazon": "ama", "Sorceress": "sor", "Necromancer": "nec",
               "Paladin": "pal", "Barbarian": "bar"}


def build_class():
    _, rows = table("charstats.txt")
    s = sheet("class", "职业（经典 5 职业）")
    s.col("id", "int", "职业 id（1-5）")
    s.col("name", "string", "职业名（中文）")
    s.col("code", "string", "官方 charstats.class")
    s.col("skill_class", "string", "skills.txt 的 charclass 标识（ama/sor/nec/pal/bar）")
    s.col("str", "int", "初始力量")
    s.col("dex", "int", "初始敏捷")
    s.col("vit", "int", "初始体力")
    s.col("eng", "int", "初始精力（官方列名为 int）")
    s.col("stat_per_lvl", "int", "每级可分配属性点")
    s.col("life_per_lvl", "float32", "每级生命成长（官方 LifePerLevel，已 ÷4）")
    s.col("mana_per_lvl", "float32", "每级法力成长（官方 ManaPerLevel，已 ÷4）")
    s.col("stam_per_lvl", "float32", "每级耐力成长（官方 StaminaPerLevel，已 ÷4）")
    s.col("life_per_vit", "float32", "每点体力=生命（官方 LifePerVitality，已 ÷4）")
    s.col("mana_per_mag", "float32", "每点精力=法力（官方 ManaPerMagic，已 ÷4）")
    s.col("stam_per_vit", "float32", "每点体力=耐力（官方 StaminaPerVitality，已 ÷4）")
    #   为什么必须导入：1 级角色的生命/耐力**不是**"起始四维 × 成长系数"，而是
    #     · 生命 = `hpadd` + 起始体力（官方字段说明：hpadd 与 vit 列相加得起始生命）
    #     · 耐力 = `stamina` 列（官方字段说明：Starting amount of Stamina）
    #   `block_factor` 同理：官方格挡式 `(dex-15)*(toblock+BlockFactor)/(2*clvl)` 里的职业项。
    s.col("hp_add", "int", "起始生命加成（官方 charstats.hpadd；与起始体力相加 = 1 级生命）")
    s.col("base_stamina", "int", "起始耐力上限（官方 charstats.stamina）")
    s.col("block_factor", "int", "职业格挡系数（官方 charstats.BlockFactor）")
    s.col("to_hit_factor", "int", "命中修正（官方 ToHitFactor）")
    s.col("walk_velocity", "int", "行走速度（官方 WalkVelocity）")
    s.col("run_velocity", "int", "奔跑速度（官方 RunVelocity）")
    s.col("start_skill", "string", "初始技能（官方 StartSkill，官方表里部分职业为空）")

    by_name = {col(r, "class"): r for r in rows}
    for i, name in enumerate(CLASS_ORDER, start=1):
        r = by_name.get(name)
        if r is None:
            print(f"[error] charstats.txt 缺职业 {name}")
            continue
        s.add(i, cn("class", name), name, SKILL_CLASS[name],
              as_int(r, "str"), as_int(r, "dex"), as_int(r, "vit"), as_int(r, "int"),
              as_int(r, "StatPerLevel"),
              div4(r, "LifePerLevel"), div4(r, "ManaPerLevel"), div4(r, "StaminaPerLevel"),
              div4(r, "LifePerVitality"), div4(r, "ManaPerMagic"), div4(r, "StaminaPerVitality"),
              as_int(r, "hpadd"), as_int(r, "stamina"), as_int(r, "BlockFactor"),
              as_int(r, "ToHitFactor"), as_int(r, "WalkVelocity"), as_int(r, "RunVelocity"),
              col(r, "StartSkill"))
        if col(r, "StartSkill") == "":
            print(f"[info] class_c: {name} 官方 StartSkill 为空 ⇒ start_skill 留空")
    return s


# ═══════════════════════════════════════════════════════════════════════════
# 1b) start_item_c —— charstats.txt 的 item1..item10（新角色起始装备）
# ═══════════════════════════════════════════════════════════════════════════
def source_line_no(name, first_col_value):
    """取官方 txt 里「第 1 列 == first_col_value」那一行的**真实文件行号**（1 基，含表头行）。

    为什么另写一份而不是复用 `table()`：`table()` 会先丢掉空行，行号就不可追溯了；
    本列（`src_line`）是**出处列**，必须等于用户在官方 txt 里能直接跳到的行号。
    """
    path = os.path.join(SRC_DIR, name)
    raw = open(path, "rb").read()
    text = None
    for enc in ("cp1252", "utf-8"):
        try:
            text = raw.decode(enc)
            break
        except UnicodeDecodeError:
            continue
    if text is None:
        text = raw.decode("latin-1")
    for i, line in enumerate(text.replace("\r\n", "\n").split("\n"), start=1):
        if line.strip() == "":
            continue
        if line.split("\t")[0] == first_col_value:
            return i
    return 0


def build_startitem():
    """官方 `charstats.txt` 的 `item1..item10` / `itemNloc` / `itemNcount` 三组列 ⇒ 起始装备表。

    ⚠️ 官方口径（**不要自己加防具**）：charstats 只给「武器 + 盾 + 药水 + 卷轴」，
    原版新角色**没有**衣服 / 鞋子 / 头盔 —— 表里没有的行就是原版没有的行。
    ⚠️ 官方空位是 `code='0' / count='0'` 的**占位**（不是物品 code），必须跳过，
    否则会生成一条指向不存在物品的行（运行期只能打日志跳过）。
    """
    _, rows = table("charstats.txt")
    by_name = {col(r, "class"): r for r in rows}

    s = sheet("start_item", "新角色起始装备（官方 charstats.txt 的 item1..item10）")
    s.col("id", "int", "行 id（本项目编号 1..N）")
    s.col("class", "int", "职业 id（指向 class_c.id）")
    s.col("slot_index", "int", "官方 item 序号 1..10（对应官方列 itemN / itemNloc / itemNcount）")
    s.col("code", "string", "物品 code（指向 item_c.code；官方空位 '0' 已剔除）")
    s.col("loc", "string", "官方 itemNloc 原文（rarm/larm/head/torso/glov/boot/belt/neck/ring；空 = 未装备 ⇒ 进背包）")
    s.col("count", "int", "官方 itemNcount（数量）")
    s.col("src_line", "int", "出处：官方 charstats.txt 的行号（1 基，含表头行）")

    nid = 0
    for i, name in enumerate(CLASS_ORDER, start=1):
        r = by_name.get(name)
        if r is None:
            print(f"[error] start_item_c: charstats.txt 缺职业 {name}")
            continue
        line_no = source_line_no("charstats.txt", name)
        taken = []
        for k in range(1, 11):
            code = col(r, f"item{k}")
            cnt = as_int(r, f"item{k}count", 0)
            if code == "" or code == "0":
                continue                       # 官方空位占位
            if cnt <= 0:
                print(f"[warn] start_item_c: {name} 的 item{k}={code!r} 但 count={cnt} ⇒ 跳过")
                continue
            nid += 1
            taken.append(f"item{k}={code}/{col(r, f'item{k}loc') or '-'}x{cnt}")
            s.add(nid, i, k, code, col(r, f"item{k}loc"), cnt, line_no)
        print(f"[info] start_item_c: {name}(class_c.id={i}, charstats.txt 行号 {line_no}) ⇒ "
              + " ".join(taken))
    return s


# ═══════════════════════════════════════════════════════════════════════════
# 2) experience_c —— experience.txt
# ═══════════════════════════════════════════════════════════════════════════
CLASS_COLS = ["Amazon", "Sorceress", "Necromancer", "Paladin", "Barbarian", "Druid", "Assassin"]


def build_experience():
    _, rows = table("experience.txt")
    s = sheet("experience", "等级经验曲线")
    s.col("level", "int", "等级（1-99）")
    s.col("exp", "int64", "升到该级所需累计经验（官方 experience.txt；8 职业同值）")
    s.col("exp_ratio", "int", "官方 ExpRatio（经验获得比率）")
    for r in rows:
        lv = col(r, "Level")
        if not lv.isdigit():
            print(f"[info] experience_c: 跳过非数字 Level 行 {lv!r}（官方 'MaxLvl' 行）")
            continue
        n = int(lv)
        if n < 1:
            print(f"[info] experience_c: 跳过 Level={n}（0 级占位行）")
            continue
        vals = {c: col(r, c) for c in CLASS_COLS}
        if len(set(vals.values())) != 1:
            print(f"[warn] experience_c: Level={n} 各职业经验不同 {vals}，取 Amazon 列")
        s.add(n, int(col(r, "Amazon") or 0), as_int(r, "ExpRatio"))
    return s


# ═══════════════════════════════════════════════════════════════════════════
# ═══════════════════════════════════════════════════════════════════════════
MONSTER_IDS = ["fallen1", "fallenshaman1", "quillrat1", "zombie1",
               "corruptrogue1", "foulcrow2", "brute1", "wraith1"]
MONSTER_IDX = {code: i + 1 for i, code in enumerate(MONSTER_IDS)}
MONSTER_LEVEL = 1          # 三处区域的区域等级（Levels.MonLvl1）：血泊 1 / 邪恶洞穴 1


def monlvl_row(level):
    _, rows = table("MonLvl.txt")
    for r in rows:
        if col(r, "Level") == str(level):
            return r
    print(f"[error] MonLvl.txt 找不到 Level={level}，退回 Level=1")
    return rows[1]


def build_level():
    _, rows = table("Levels.txt")
    s = sheet("level", "区域（罗格营地 / 血腥荒野 / 邪恶洞穴）")
    s.col("id", "int", "区域 id（1-3，本项目编号）")
    s.col("name", "string", "区域名（中文）")
    s.col("code", "string", "官方 Levels.Name（Act 1 - Town / Wilderness 1 / Cave 1）")
    s.col("level_name", "string", "官方 Levels.LevelName（Rogue Encampment / Blood Moor / Den of Evil）")
    s.col("level_id", "int", "官方 Levels.Id")
    s.col("act", "int", "所属章节（官方 Act，0 基 ⇒ 0 = 第一幕）")
    s.col("area_level", "int", "区域等级（官方 MonLvl1）")
    s.col("mon_density", "int", "怪物密度（官方 MonDen，千分之几）")
    s.col("mon_umin", "int", "冠军/精英怪下限（官方 MonUMin）")
    s.col("mon_umax", "int", "冠军/精英怪上限（官方 MonUMax）")
    s.col("num_mon", "int", "同时存在的怪物种类数（官方 NumMon）")
    s.col("monsters", "[]int", "该区域刷出的怪物 id 列表（指向 monster_c.id）")
    s.col("size_x", "int", "官方 SizeX")
    s.col("size_y", "int", "官方 SizeY")
    s.col("size_min", "int", "生成尺寸下限（= min(SizeX, SizeY)）")
    s.col("size_max", "int", "生成尺寸上限（= max(SizeX, SizeY)）")
    s.col("random", "int", "是否随机生成（官方 DrlgType == 2 为固定地形，其余为随机 ⇒ 1/0）")
    s.col("drlg_type", "int", "官方 DrlgType（1 洞穴 / 2 固定 / 3 野外）")
    s.col("is_inside", "int", "是否室内（官方 IsInside）")

    # 三处区域：官方 Id 1 / 2 / 8
    targets = [(1, "Rogue Encampment"), (2, "Blood Moor"), (8, "Den of Evil")]
    for i, (official_id, level_name) in enumerate(targets, start=1):
        r = None
        for cand in rows:
            if col(cand, "Id") == str(official_id):
                r = cand
                break
        if r is None:
            print(f"[error] Levels.txt 找不到 Id={official_id}")
            continue
        if col(r, "LevelName") != level_name:
            print(f"[warn] Levels.txt Id={official_id} LevelName={col(r, 'LevelName')!r} "
                  f"与预期 {level_name!r} 不符")
        mons = []
        for k in range(1, 11):
            mi = col(r, f"mon{k}")
            if mi:
                if mi in MONSTER_IDX:
                    mons.append(MONSTER_IDX[mi])
                else:
                    print(f"[warn] level_c: 区域 {level_name} 的 mon{k}={mi} "
                          f"不在本项目怪物集内（契约只列 8 种），不导入该 id")
        sx, sy = as_int(r, "SizeX"), as_int(r, "SizeY")
        drlg = as_int(r, "DrlgType")
        OFFICIAL_NAME["level"][level_name] = f"{col(r, 'Name')} / {level_name}"
        s.add(i, cn("level", level_name), col(r, "Name"), level_name, official_id,
              as_int(r, "Act"), as_int(r, "MonLvl1"), as_int(r, "MonDen"),
              as_int(r, "MonUMin"), as_int(r, "MonUMax"), as_int(r, "NumMon"),
              ";".join(str(m) for m in mons), sx, sy, min(sx, sy), max(sx, sy),
              0 if drlg == 2 else 1, drlg, as_int(r, "IsInside"))
        print(f"[info] level_c: {level_name} 区域等级={as_int(r, 'MonLvl1')} "
              f"怪物 id={mons} DrlgType={drlg}(random={0 if drlg == 2 else 1})")
    return s


def build_monster():
    _, ms = table("MonStats.txt")
    _, ms2 = table("MonStats2.txt")
    _, ai = table("MonAi.txt")
    lv = monlvl_row(MONSTER_LEVEL)
    hp_pct, ac_pct = as_int(lv, "HP"), as_int(lv, "AC")
    th_pct, dm_pct, xp_pct = as_int(lv, "TH"), as_int(lv, "DM"), as_int(lv, "XP")
    print(f"[info] monster_c: MonLvl 等级 {MONSTER_LEVEL} 倍率 "
          f"HP={hp_pct}% AC={ac_pct}% TH={th_pct}% DM={dm_pct}% XP={xp_pct}%")

    known_ai = {col(r, "AI") for r in ai}
    by_id = {col(r, "Id"): r for r in ms}
    by2 = {col(r, "Id"): r for r in ms2}

    s = sheet("monster", "怪物（Act I 起始三区域）")
    s.col("id", "int", "怪物 id（1-8，本项目编号）")
    s.col("name", "string", "怪物名（中文）")
    s.col("code", "string", "官方 MonStats.Id")
    s.col("name_str", "string", "官方 NameStr（英文名）")
    s.col("base_id", "string", "官方 BaseId（同类基础怪）")
    s.col("level", "int", "换算所用等级（= 区域等级；三处区域均为 1）")
    s.col("base_level", "int", "官方 MonStats.Level（怪物等级基线，仅参考）")
    s.col("min_grp", "int", "刷怪最小成群数（官方 MinGrp）")
    s.col("max_grp", "int", "刷怪最大成群数（官方 MaxGrp）")
    s.col("rarity", "int", "出现权重（官方 Rarity）")
    s.col("hp", "int", "生命（已按 MonLvl 倍率换算；= (hp_min+hp_max)/2）")
    s.col("hp_min", "int", "生命下限（官方 minHP × MonLvl.HP/100）")
    s.col("hp_max", "int", "生命上限（官方 maxHP × MonLvl.HP/100）")
    s.col("ac", "int", "防御（官方 AC × MonLvl.AC/100）")
    s.col("ar", "int", "命中（官方 A1TH × MonLvl.TH/100）")
    s.col("dmg_min", "int", "近战伤害下限（官方 A1MinD × MonLvl.DM/100）")
    s.col("dmg_max", "int", "近战伤害上限（官方 A1MaxD × MonLvl.DM/100）")
    s.col("exp", "int", "击杀经验（官方 Exp × MonLvl.XP/100）")
    s.col("ai", "string", "AI 类型（官方 MonStats.AI）")
    s.col("speed", "float32", "移动速度（官方 Velocity）")
    s.col("run", "int", "奔跑速度（官方 Run）")
    s.col("res_phys", "int", "物理抗性（官方 ResDm）")
    s.col("res_magic", "int", "魔法抗性（官方 ResMa）")
    s.col("res_fire", "int", "火焰抗性（官方 ResFi）")
    s.col("res_light", "int", "闪电抗性（官方 ResLi）")
    s.col("res_cold", "int", "冰冷抗性（官方 ResCo）")
    s.col("res_poison", "int", "毒素抗性（官方 ResPo）")
    s.col("sprite", "string", "动画代码（官方 MonStats.Code，DCC 文件名前缀）")
    s.col("corpse_usable", "int", "尸体可被选中/利用（官方 MonStats2.corpseSel）")
    s.col("revive", "int", "可被复活（官方 MonStats2.revive）")
    s.col("undead", "int", "亡灵（官方 lUndead|hUndead）")
    s.col("demon", "int", "恶魔（官方 demon）")
    s.col("flying", "int", "飞行（官方 flying）")
    s.col("treasure_class", "string", "掉落表（官方 TreasureClass1 = 普通怪槽位，指向 treasureclass_c）")
    #   实测 8 只怪的取值 = `TC1` 普通（`Act 1 H2H A` / `Quill 1` …）、`TC2` **冠军怪**（`Act 1 Champ A|B`）、
    #   `TC3` **唯一（精英）怪**（`Act 1 Unique A|B`）、`TC4` 空（Super 唯一怪本项目没有）。
    #   这里把引擎真正会用的槽位逐列导出，运行期按"是不是精英 / 哪一种精英"取列（禁止在代码里写死名字）。
    s.col("treasure_class_champ", "string", "冠军怪掉落表（官方 TreasureClass2）")
    s.col("treasure_class_unique", "string", "唯一（精英）怪掉落表（官方 TreasureClass3）")

    for mid in MONSTER_IDS:
        r = by_id.get(mid)
        if r is None:
            print(f"[error] monster_c: MonStats.txt 无 Id={mid}")
            continue
        s2 = by2.get(mid, {})
        ai_name = col(r, "AI")
        if ai_name and ai_name not in known_ai:
            print(f"[warn] monster_c: {mid} 的 AI={ai_name!r} 不在 MonAi.txt 里")
        hp_min = trunc_pct(as_int(r, "minHP"), hp_pct)
        hp_max = trunc_pct(as_int(r, "maxHP"), hp_pct)
        OFFICIAL_NAME["monster"][mid] = f"{col(r, 'NameStr')} (AI={col(r, 'AI')})"
        # R6：逐怪登记 3 个 TC 槽位；槽位缺哪个就点名打日志（不静默 —— 缺槽位会让精英怪掉回普通怪 TC）
        tc_n, tc_c, tc_u = col(r, "TreasureClass1"), col(r, "TreasureClass2"), col(r, "TreasureClass3")
        print(f"[info] monster_c: {mid} TC 槽位 普通={tc_n!r} 冠军={tc_c!r} 唯一={tc_u!r}")
        if not tc_c or not tc_u:
            print(f"[warn] monster_c: {mid} 的官方 TreasureClass2/3 为空 ⇒ 该怪精英化后只能沿用普通怪 TC "
                  f"（官方 1.10f 里 8 只怪三列都有值，出现这行说明源表换了）")
        s.add(MONSTER_IDX[mid], cn("monster", mid), mid, col(r, "NameStr"), col(r, "BaseId"),
              MONSTER_LEVEL, as_int(r, "Level"), as_int(r, "MinGrp"), as_int(r, "MaxGrp"),
              as_int(r, "Rarity"),
              (hp_min + hp_max) // 2, hp_min, hp_max,
              trunc_pct(as_int(r, "AC"), ac_pct),
              trunc_pct(as_int(r, "A1TH"), th_pct),
              trunc_pct(as_int(r, "A1MinD"), dm_pct),
              trunc_pct(as_int(r, "A1MaxD"), dm_pct),
              trunc_pct(as_int(r, "Exp"), xp_pct),
              ai_name, as_float(r, "Velocity"), as_int(r, "Run"),
              as_int(r, "ResDm"), as_int(r, "ResMa"), as_int(r, "ResFi"),
              as_int(r, "ResLi"), as_int(r, "ResCo"), as_int(r, "ResPo"),
              col(r, "Code"), as_int(s2, "corpseSel"), as_int(s2, "revive"),
              1 if (as_int(r, "lUndead") or as_int(r, "hUndead")) else 0,
              as_int(r, "demon"), as_int(r, "flying"), tc_n, tc_c, tc_u)
    return s


# ═══════════════════════════════════════════════════════════════════════════
# 5) skill_c —— skills.txt + SkillDesc.txt
# ═══════════════════════════════════════════════════════════════════════════

#   实测（`skills.txt` 5 职业全部 `calc1` 非空行的 `*calc1 desc` 去重）= 33 种，其中只有下面 6 种
#   是"伤害倍率"语义；**不能按词根 "damage" 判**：官方还有 `damage radius`（半径）、
#   `damage absorption`（吸收）、`% damage to return`（反伤）这类含 damage 却**不是**倍率的 desc。
_DAMAGE_PCT_DESCS = frozenset({
    "damage%", "damage %", "+ dmg%", "dmg%", "dm%", "fire damage%",
})


def _damage_pct_of(calc, desc, row):
    """官方 `calc1` → `(1 级伤害倍率%, 每级增量%, 是否已确定)`。

    **规则（唯一出处 = 官方列值本身，⛔ 不做"列名看起来像"的推断）**：

    1. `calc1` 为空 **且** `desc` 为空 ⇒ 官方这行根本没有 calc ⇒ `(0, 0, 1)`（倍率确实是 0）。
    2. `desc` 为空但 `calc1` 非空 ⇒ 官方**没标语义** ⇒ **不猜**，返回 `(0, 0, 0)`
       （运行时按 0% 结算并留日志；例：`Leap Attack` / `Stun`）。
    3. `desc` 不在 `_DAMAGE_PCT_DESCS` 白名单里（= 官方明说它是别的东西：弹数 / 目标数 / 半径 /
       转化率 / 反伤 / 吸收…）⇒ 返回 `(0, 0, 1)`：**伤害倍率确实是 0**，且已确定（运行时不该告警）。
    4. `desc` 在白名单里 ⇒ 过公式门：`calc1` 以 `ln12` 开头 ⇒ `Param1 + Param2×(lvl-1)`；
       以 `ln34` 开头 ⇒ `Param3 + Param4×(lvl-1)`（官方线性 calc 的标准形式，本工程既有实现见
       `DamageFormula.SkillDamageRange` 的 `staged` 注释）。
       其它形态（`dm34`、字面数 `12`、`skill('X'.blvl)*parN`、`min(...)`）= **无法静态解析**
       ⇒ `(0, 0, 0)`，⛔ 不编数。
    5. ⚠️ 形如 `ln12+skill('Stun'.blvl)*par8` 的**协同项**只取前导 `ln12`；协同项要"别的技能的等级"，
       本项目未建模 ⇒ **数值上不加**，但原文完整保留在 `dmg_pct_calc` 列里可追溯。
    """
    d = (desc or "").strip().lower()
    c = (calc or "").strip()
    if c == "" and d == "":
        return 0, 0, 1
    if d == "":
        return 0, 0, 0
    if d not in _DAMAGE_PCT_DESCS:
        return 0, 0, 1
    if c.startswith("ln12"):
        return as_int(row, "Param1"), as_int(row, "Param2"), 1
    if c.startswith("ln34"):
        return as_int(row, "Param3"), as_int(row, "Param4"), 1
    return 0, 0, 0


def build_skill():
    _, sk = table("skills.txt")
    _, sd = table("SkillDesc.txt")
    sd_by_key = {col(r, "skilldesc"): r for r in sd}
    desc_by_key = {}
    for r in sk:
        if col(r, "charclass"):
            desc_by_key[col(r, "skill")] = sd_by_key.get(col(r, "skilldesc"))

    picked = [r for r in sk if col(r, "charclass") in SKILL_CLASS.values()]
    print(f"[info] skill_c: skills.txt 共 {len(sk)} 行，5 经典职业(ama/sor/nec/pal/bar) {len(picked)} 行")
    code2id = {}
    for i, r in enumerate(picked, start=1):
        code2id[col(r, "skill")] = i

    s = sheet("skill", "技能（5 职业 × 3 系）")
    s.col("id", "int", "技能 id（1-150，本项目编号）")
    s.col("class", "int", "所属职业（指向 class_c.id）")
    s.col("tree", "int", "技能树（1-3，官方 SkillDesc.SkillPage）")
    s.col("name", "string", "技能名（中文）")
    s.col("code", "string", "官方 skills.skill")
    s.col("official_id", "int", "官方 skills.Id")
    s.col("req_level", "int", "需求等级（官方 reqlevel）")
    s.col("max_level", "int", "最高等级（官方 maxlvl）")
    s.col("req_skill", "int", "前置技能（指向 skill_c.id，0 = 无）")
    s.col("skill_points", "int", "每级消耗技能点（官方 skpoints）")
    s.col("mana_cost", "int", "基础法力消耗（官方 mana）")
    s.col("mana_per_lvl", "int", "每级法力增量（官方 lvlmana）")
    #    官方 `staged(level=1)` ⇒ `base + lev[0]*(1-1)` = `base` ⇒ 1 级值就是 `MinDam`/`EMin`。
    #    等级缩放走下面新增的 `*_lev1..5` 列（见 `DamageFormula.SkillDamageRange` 的分段式）。
    s.col("dmg_min", "int", "**1 级**伤害下限（(MinDam+EMin) × 2^HitShift / 256，等级缩放见 *_lev* 列）")
    s.col("dmg_max", "int", "**1 级**伤害上限（(MaxDam+EMax) × 2^HitShift / 256，等级缩放见 *_lev* 列）")
    s.col("phys_min", "int", "物理伤害下限（官方 MinDam，未换算）")
    s.col("phys_max", "int", "物理伤害上限（官方 MaxDam，未换算）")
    s.col("elem_min", "int", "元素伤害下限（官方 EMin，未换算）")
    s.col("elem_max", "int", "元素伤害上限（官方 EMax，未换算）")
    # ── 每级增量（官方原值，**不换算**；用法见 libd2 `spell.zig:88-116` 的 `staged`）──
    #    段位语义（官方口径）：lev1 = 1~8 级每级 +N；lev2 = 9~16；lev3 = 17~22；lev4 = 23~28；lev5 = 29+。
    s.col("phys_min_lev1", "int", "物理下限每级增量 1~8 级（官方 MinLevDam1，未换算）")
    s.col("phys_min_lev2", "int", "物理下限每级增量 9~16 级（官方 MinLevDam2）")
    s.col("phys_min_lev3", "int", "物理下限每级增量 17~22 级（官方 MinLevDam3）")
    s.col("phys_min_lev4", "int", "物理下限每级增量 23~28 级（官方 MinLevDam4）")
    s.col("phys_min_lev5", "int", "物理下限每级增量 29+ 级（官方 MinLevDam5）")
    s.col("phys_max_lev1", "int", "物理上限每级增量 1~8 级（官方 MaxLevDam1）")
    s.col("phys_max_lev2", "int", "物理上限每级增量 9~16 级（官方 MaxLevDam2）")
    s.col("phys_max_lev3", "int", "物理上限每级增量 17~22 级（官方 MaxLevDam3）")
    s.col("phys_max_lev4", "int", "物理上限每级增量 23~28 级（官方 MaxLevDam4）")
    s.col("phys_max_lev5", "int", "物理上限每级增量 29+ 级（官方 MaxLevDam5）")
    s.col("elem_min_lev1", "int", "元素下限每级增量 1~8 级（官方 EMinLev1）")
    s.col("elem_min_lev2", "int", "元素下限每级增量 9~16 级（官方 EMinLev2）")
    s.col("elem_min_lev3", "int", "元素下限每级增量 17~22 级（官方 EMinLev3）")
    s.col("elem_min_lev4", "int", "元素下限每级增量 23~28 级（官方 EMinLev4）")
    s.col("elem_min_lev5", "int", "元素下限每级增量 29+ 级（官方 EMinLev5）")
    s.col("elem_max_lev1", "int", "元素上限每级增量 1~8 级（官方 EMaxLev1）")
    s.col("elem_max_lev2", "int", "元素上限每级增量 9~16 级（官方 EMaxLev2）")
    s.col("elem_max_lev3", "int", "元素上限每级增量 17~22 级（官方 EMaxLev3）")
    s.col("elem_max_lev4", "int", "元素上限每级增量 23~28 级（官方 EMaxLev4）")
    s.col("elem_max_lev5", "int", "元素上限每级增量 29+ 级（官方 EMaxLev5）")
    s.col("hit_shift", "int", "伤害移位（官方 HitShift）")
    s.col("dmg_type", "string", "伤害类型（官方 EType：fire/cold/ltng/pois/mag，空 = 物理）")
    s.col("passive", "int", "是否被动技能（官方 passive）")
    s.col("missile", "string", "投射物（官方 srvmissile，空则取 cltmissile；指向 missile_c.code）")
    s.col("desc", "string", "技能说明（本项目按官方字段拼装）")
    #   三组**新增列**，语义与出处逐列写在这里（不是"列名看起来像"）：
    #   ① `src_dam` ← 官方 `skills.txt` 列 219 `SrcDam`。
    #      "Controls the percentage modifier for how much weapon damage is transferred to the skill's
    #       damage (Out of 128)."
    #      ⇒ 它是 **分母 128 的"武器伤害转移百分比"**，**不是位掩码**。
    #        官方 1.10f 全表取值只有 {32,48,96,128} ⇒ 25% / 37.5% / 75% / 100%。
    #        实证：`Bash=128`、`Multiple Shot=96`、`Strafe=96`、`Blade Shield=32`；
    #        而纯法术技能（`Fire Bolt`/`Warmth`）该列为空 ⇒ 0 = **不用武器伤害**。
    #   ② `dmg_pct_*` ← 官方 `calc1`（列 186）+ `*calc1 desc`（列 187）。
    #      `*calc1 desc` 是官方自带的语义标注：`Bash='damage%'`、`Impale='dm%'`、`Guided Arrow='+ dmg%'`、
    #      `Fend='max targets'`、`Multiple Shot='# missiles'`、`Conversion='chance to convert'`。
    #      ⇒ **只有语义是"伤害倍率"的行**才取 `dmg_pct_base/per_lvl`（见 `_damage_pct_of` 的规则）；
    #        其它语义（弹数 / 目标数 / 转化率）一律 0，不把"目标数 12"当"伤害 12%"。
    #   ③ `missile_a/b/c` ← 官方 `srvmissilea/b/c`（次要服务器投射物槽）。
    #      D2 数据指南：`srvmissile#` = "the secondary missiles used by the server functions"（同上出处）。
    #      它们**不是** `srvmissile` 的备选写法，而是多投射物/多箭的第 2/3/4 个槽 ——
    #      实证：`Multiple Shot` 的 `srvmissile` 本来就是空、三箭全在 `srvmissilea`（`multipleshotarrow`）。
    s.col("src_dam", "int", "武器伤害转移比（官方 SrcDam，分母 128 ⇒ 128=100%；0=不用武器伤害）")
    s.col("dmg_pct_calc", "string", "官方 calc1 原文（伤害倍率公式；语义见 dmg_pct_desc）")
    s.col("dmg_pct_desc", "string", "官方 *calc1 desc 原文（damage% / max targets / # missiles ...）")
    s.col("dmg_pct_base", "int", "1 级伤害倍率%（calc1 以 ln12⇒Param1 / ln34⇒Param3 开头时；否则 0）")
    s.col("dmg_pct_per_lvl", "int", "每级伤害倍率增量%（ln12⇒Param2 / ln34⇒Param4；否则 0）")
    s.col("dmg_pct_parsed", "int", "1 = dmg_pct_base/per_lvl 可信；0 = calc1 无法静态解析（运行时按 0% + 留日志）")
    s.col("missile_a", "string", "第 2 投射物槽（官方 srvmissilea，指向 missile_c.code）")
    s.col("missile_b", "string", "第 3 投射物槽（官方 srvmissileb）")
    s.col("missile_c", "string", "第 4 投射物槽（官方 srvmissilec）")

    no_shift = 0
    no_tree = 0
    n_src = 0        # ★ 片 N：官方 SrcDam 非空（武器伤害类攻击技能）
    n_pct_ok = 0     # ★ 片 N：伤害倍率已确定（含"语义明确非伤害倍率"）
    n_pct_raw = 0    # ★ 片 N：calc1 非空但无法静态解析（dmg_pct_parsed=0）
    n_slot = 0       # ★ 片 N：次要投射物槽 srvmissilea/b/c 任一非空
    n_slot_only = 0  # ★ 片 N：有次要槽但主槽为空（多投射物技能）
    n_etype_stun = 0  # ★ 片 T（S-32）：官方 EType='stun' 的行数（归一到 phys）
    for i, r in enumerate(picked, start=1):
        code = col(r, "skill")
        OFFICIAL_NAME["skill"][code] = f"{col(r, 'skilldesc')} / 系={col(r, 'charclass')}"
        d = desc_by_key.get(code)
        tree = as_int(d, "SkillPage") if d else 0
        if tree == 0:
            no_tree += 1
            print(f"[warn] skill_c: {code} 的 SkillDesc.SkillPage=0，tree 记 0")
        shift = as_int(r, "HitShift")
        if col(r, "HitShift") == "":
            no_shift += 1
            warn_once("shift-empty-" + code,
                      f"[warn] skill_c: {code} 官方 HitShift 为空（按移位 0 处理，即伤害 ÷256）")
        mul = (2 ** shift) / 256.0
        phys_min, phys_max = as_int(r, "MinDam"), as_int(r, "MaxDam")
        elem_min, elem_max = as_int(r, "EMin"), as_int(r, "EMax")
        dmg_min = rnd((phys_min + elem_min) * mul)
        dmg_max = rnd((phys_max + elem_max) * mul)
        etype = col(r, "EType")
        #   官方 `skills.txt`（1.10f）自证三件事（可逐列回查）：
        #     ① 全表 `EType` 取值只有 {'', fire, cold, ltng, pois, mag, **stun**}（stun 仅 2 行）；
        #     ② 这 2 行的 **`EMin`/`EMax` 都为空**（没有任何元素伤害），伤害来自 `SrcDam=128`
        #        （武器伤害转移 128/128 ⇒ **物理**）；
        #     ③ 与 `EType` 同一族的 `ELen`/`ELevLen1..3`/`ELenSymPerCalc` 才是**状态持续帧数**：
        #        `Stun`(Id=139) 的 `Param1=30` 且官方自带标注 `*Param1 Description = Frames the target is stunned`，
        #        对应 `ELen=30`、`ELevLen1..3=5,5,2`、`ELenSymPerCalc=skill('War Cry'.blvl)*par6`。
        #   ⇒ `stun` 的语义 = "附加眩晕状态"，其伤害通道是物理。
        #   不这么做时它会以"未登记 EType"落到 `DamageFormula.DamageTypeOf` 的 default
        #     （Warn + 按物理结算，09-23 日志 138 条）—— 结果虽同，但把"已登记"混进了"未知值"。
        if etype == "stun":
            n_etype_stun += 1
            print(f"[info] skill_c: {code} 官方 EType='stun'（眩晕状态，非伤害系；EMin/EMax 空、"
                  f"SrcDam={as_int(r, 'SrcDam')}、ELen={as_int(r, 'ELen')}）⇒ 伤害通道登记为 phys")
            etype = "phys"
        if etype == "" and (dmg_min or dmg_max):
            etype = "phys"
        elif etype == "":
            etype = ""
        req = col(r, "reqskill1")
        req_id = code2id.get(req, 0)
        if req and req_id == 0:
            print(f"[warn] skill_c: {code} 的前置技能 {req!r} 不在 5 职业技能集内，req_skill=0")
        missile = col(r, "srvmissile") or col(r, "cltmissile")
        is_passive = as_int(r, "passive")

        src_dam = as_int(r, "SrcDam")
        pct_calc = col(r, "calc1").strip().strip('"')
        pct_desc = col(r, "*calc1 desc").strip()
        pct_base, pct_per, pct_ok = _damage_pct_of(pct_calc, pct_desc, r)
        mslot_a, mslot_b, mslot_c = (col(r, "srvmissilea"), col(r, "srvmissileb"),
                                     col(r, "srvmissilec"))
        if src_dam > 0:
            n_src += 1
            if pct_ok:
                n_pct_ok += 1
            elif pct_calc:
                n_pct_raw += 1
        if mslot_a or mslot_b or mslot_c:
            n_slot += 1
            if not missile:
                n_slot_only += 1
        if src_dam > 0 and not missile and (dmg_min or dmg_max) == 0:
            pct_txt = (f"伤害倍率 {pct_base}%（每级 {pct_per:+d}%）" if pct_ok
                       else f"伤害倍率不可静态解析（官方 calc1={pct_calc!r}）")
            desc = (f"武器伤害类攻击：武器伤害 ×{src_dam}/128（官方 SrcDam）+ {pct_txt}，"
                    f"法力 {as_int(r, 'mana')}（每级 {as_int(r, 'lvlmana'):+d}），"
                    f"需求等级 {as_int(r, 'reqlevel')}")
        elif dmg_max > 0:
            desc = (f"{DAMAGE_TYPE_CN.get(etype, etype)}伤害 {dmg_min}-{dmg_max}，"
                    f"法力 {as_int(r, 'mana')}（每级 {as_int(r, 'lvlmana'):+d}），"
                    f"需求等级 {as_int(r, 'reqlevel')}")
        elif is_passive:
            desc = f"被动技能，需求等级 {as_int(r, 'reqlevel')}"
        else:
            desc = (f"辅助技能，法力 {as_int(r, 'mana')}"
                    f"（每级 {as_int(r, 'lvlmana'):+d}），需求等级 {as_int(r, 'reqlevel')}")
        s.add(i, CLASS_ORDER.index(_class_of(col(r, "charclass"))) + 1, tree,
              cn("skill", code), code, as_int(r, "Id"),
              as_int(r, "reqlevel"), as_int(r, "maxlvl"), req_id,
              as_int(r, "skpoints"), as_int(r, "mana"), as_int(r, "lvlmana"),
              dmg_min, dmg_max, phys_min, phys_max, elem_min, elem_max,
              as_int(r, "MinLevDam1"), as_int(r, "MinLevDam2"), as_int(r, "MinLevDam3"),
              as_int(r, "MinLevDam4"), as_int(r, "MinLevDam5"),
              as_int(r, "MaxLevDam1"), as_int(r, "MaxLevDam2"), as_int(r, "MaxLevDam3"),
              as_int(r, "MaxLevDam4"), as_int(r, "MaxLevDam5"),
              as_int(r, "EMinLev1"), as_int(r, "EMinLev2"), as_int(r, "EMinLev3"),
              as_int(r, "EMinLev4"), as_int(r, "EMinLev5"),
              as_int(r, "EMaxLev1"), as_int(r, "EMaxLev2"), as_int(r, "EMaxLev3"),
              as_int(r, "EMaxLev4"), as_int(r, "EMaxLev5"),
              shift, etype, is_passive, missile, desc,
              src_dam, pct_calc, pct_desc, pct_base, pct_per, pct_ok,
              mslot_a, mslot_b, mslot_c)
    if n_etype_stun:
        print(f"[info] skill_c: {n_etype_stun} 行官方 EType='stun' ⇒ 已登记为 phys"
              f"（眩晕是控制状态；出处 = 官方 skills.txt 该行 EMin/EMax 空 + SrcDam 非空 + ELen/*Param1 描述，S-32）")
    if no_shift:
        print(f"[warn] skill_c: {no_shift} 个技能 HitShift 为空（按 0 处理）")
    if no_tree:
        print(f"[warn] skill_c: {no_tree} 个技能树栏位为 0")
    print(f"[info] skill_c 武器伤害类（官方 SrcDam≠空）= {n_src} 行；"
          f"伤害倍率已确定 {n_pct_ok} 行 / 无法静态解析 {n_pct_raw} 行（dmg_pct_parsed=0，运行时按 0%+日志）")
    print(f"[info] skill_c 次要投射物槽（srvmissilea/b/c 任一非空）= {n_slot} 行；"
          f"其中主槽 srvmissile/cltmissile 为空 = {n_slot_only} 行（多投射物技能）")
    print(f"[info] skill_c 技能树分布: " +
          str({t: sum(1 for x in s.rows if x[2] == t) for t in (0, 1, 2, 3)}))
    return s


def _class_of(charclass):
    for k, v in SKILL_CLASS.items():
        if v == charclass:
            return k
    return ""


# ═══════════════════════════════════════════════════════════════════════════
# 6) item_c —— Weapons.txt + Armor.txt + Misc.txt
# ═══════════════════════════════════════════════════════════════════════════
ITEM_MAX_LEVEL = 12        # Act I 全区（Catacombs 12）可掉落的物品等级上限


def build_item():
    _, wp = table("Weapons.txt")
    _, ar = table("Armor.txt")
    _, mi = table("Misc.txt")

    s = sheet("item", "物品（武器 / 防具 / 杂物）")
    s.col("id", "int", "物品 id（本项目编号）")
    s.col("name", "string", "物品名（中文）")
    s.col("code", "string", "官方 code")
    s.col("source", "string", "来源官方表（weap/armo/misc）")
    s.col("type", "string", "物品大类（官方 type）")
    s.col("subtype", "string", "子类（官方 type2，空则取 wclass）")
    s.col("grid_w", "int", "背包占格宽（官方 invwidth）")
    s.col("grid_h", "int", "背包占格高（官方 invheight）")
    s.col("lvl_req", "int", "使用等级需求（官方 levelreq）")
    s.col("item_level", "int", "物品等级 qlvl（官方 level）")
    s.col("str_req", "int", "力量需求（官方 reqstr）")
    s.col("dex_req", "int", "敏捷需求（官方 reqdex）")
    s.col("dmg_min", "int", "单手伤害下限（官方 mindam）")
    s.col("dmg_max", "int", "单手伤害上限（官方 maxdam）")
    s.col("dmg2_min", "int", "双手伤害下限（官方 2handmindam）")
    s.col("dmg2_max", "int", "双手伤害上限（官方 2handmaxdam）")
    #   官方物理伤害 = `武器 + 武器 × (str×StrBonus + dex×DexBonus + 技能ED) / 100 / 100`
    #   （`DAMAGE_CalculatePhysicalDamage @0057b420`；参考实现 `combat.zig:149-180`）。
    #   近战绝大多数 `StrBonus=100 / DexBonus=0`；**弓与弩是 `StrBonus=0 / DexBonus=100`**。
    #   Armor.txt / Misc.txt **没有**这两列 ⇒ 非武器行为 0（`col()` 对缺列返回空串 ⇒ `as_int` 给 0）。
    s.col("str_bonus", "int", "力量伤害加成%（官方 Weapons.StrBonus；弓/弩为 0；非武器为 0）")
    s.col("dex_bonus", "int", "敏捷伤害加成%（官方 Weapons.DexBonus；弓/弩为 100；非武器为 0）")
    s.col("mis_min", "int", "投掷伤害下限（官方 minmisdam）")
    s.col("mis_max", "int", "投掷伤害上限（官方 maxmisdam）")
    s.col("def_min", "int", "防御下限（官方 minac）")
    s.col("def_max", "int", "防御上限（官方 maxac）")
    s.col("block", "int", "格挡率（官方 block）")
    s.col("price", "int", "基础价格（官方 cost）")
    s.col("stack", "int", "可堆叠（官方 stackable）")
    s.col("min_stack", "int", "最小堆叠数（官方 minstack）")
    s.col("max_stack", "int", "最大堆叠数（官方 maxstack）")
    s.col("two_handed", "int", "仅双手（官方 2handed）")
    s.col("one_or_two", "int", "可单手可双手（官方 1or2handed）")
    s.col("speed", "int", "武器速度修正（官方 speed）")
    s.col("sockets", "int", "最大孔数（官方 gemsockets）")
    s.col("belt_slots", "int", "腰带可放药水数（官方 belt）")
    s.col("quest", "int", "是否任务物品（官方 quest）")

    nid = 0

    def take(row, source):
        nonlocal nid
        code = col(row, "code")
        if code == "":
            return
        if code in code_seen:
            print(f"[info] item_c: code={code} 在 {source} 重复（另一来源已收录），跳过")
            return
        code_seen.add(code)
        nid += 1
        OFFICIAL_NAME["item"][code] = (f"{col(row, 'name')}"
                                       f" ({source}/{col(row, 'type')})")
        typ = col(row, "type")
        #   删掉第三项（而不是换一个"看起来像"的键）：候选语义 = 官方给出的"武器类别"，只有
        #   `type2` / `wclass` 两个真实来源；官方 `Armor.txt` 二者都**没有**该列 ⇒ `armo` 的 `subtype`
        #   本就应留空（防具部位看 `type` 列，`LootRoller.MatchesFamily("armo")` 也只用 `Source`）。
        sub = col(row, "type2") or col(row, "wclass")
        #   （**实机表现：拿弓打不出伤害**；`item_c` 现有 4 弓 + 1 弩 + 若干双手斧全中招）。
        #   官方列语义：双手武器的"伤害"就是 `2hand*` 那两列 ⇒ 这里**回退**到它们
        #   `dmg2_min/dmg2_max` 仍照原样另存双手值（供"可单可双"的武器查询）。
        #   官方取值证据（1.10f `Weapons.txt`，本项目 42 行武器逐行核）：
        #     · `lax/bax/spr/tri/bar/…/lxb` 14 行：`mindam`/`maxdam` **两列都空**、`2hand*` 有值 ⇒ 必须回退（原行为不变）；
        #     · `gpl`/`opl`（投掷药水）2 行：官方 `mindam='0'`、`maxdam='1'` **两列都有值** ⇒ 现值 `(0,1)` 就是官方值，
        #       **不得**回退（旧判据按值判 0 会漏掉"单侧为 0"，把它们错回退成 `2hand*` 的空值 `(0,0)`）；
        #     · 其余 26 行：两列都有值 ⇒ 不回退。
        #   另加 `2hand*` 非空这道闸：官方没给双手值时不许把已有的单侧值抹成 0（对既有产物逐字节无影响）。
        one_min, one_max = col(row, "mindam").strip(), col(row, "maxdam").strip()
        two_min, two_max = col(row, "2handmindam").strip(), col(row, "2handmaxdam").strip()
        dmg_min, dmg_max = as_int(row, "mindam"), as_int(row, "maxdam")
        if (one_min == "" or one_max == "") and (two_min or two_max):
            dmg_min, dmg_max = as_int(row, "2handmindam"), as_int(row, "2handmaxdam")
        s.add(nid, cn("item", code), code, source, typ, sub,
              as_int(row, "invwidth"), as_int(row, "invheight"),
              as_int(row, "levelreq"), as_int(row, "level"), as_int(row, "reqstr"),
              as_int(row, "reqdex"),
              dmg_min, dmg_max, as_int(row, "2handmindam"), as_int(row, "2handmaxdam"),
              as_int(row, "StrBonus"), as_int(row, "DexBonus"),
              as_int(row, "minmisdam"), as_int(row, "maxmisdam"),
              as_int(row, "minac"), as_int(row, "maxac"), as_int(row, "block"),
              as_int(row, "cost"), as_int(row, "stackable"),
              as_int(row, "minstack"), as_int(row, "maxstack"),
              as_int(row, "2handed"), as_int(row, "1or2handed"), as_int(row, "speed"),
              as_int(row, "gemsockets"), as_int(row, "belt"), as_int(row, "quest"))

    code_seen = set()
    w_norm = [r for r in wp if col(r, "code") == col(r, "normcode")
              and as_int(r, "spawnable") == 1 and as_int(r, "level") <= ITEM_MAX_LEVEL]
    print(f"[info] item_c: Weapons.txt {len(wp)} 行 ⇒ 普通品质(code==normcode)且spawnable 且 "
          f"level<={ITEM_MAX_LEVEL} 命中 {len(w_norm)}")
    for r in w_norm:
        take(r, "weap")
    a_norm = [r for r in ar if col(r, "code") == col(r, "normcode")
              and as_int(r, "spawnable") == 1 and as_int(r, "level") <= ITEM_MAX_LEVEL]
    print(f"[info] item_c: Armor.txt {len(ar)} 行 ⇒ 命中 {len(a_norm)}")
    for r in a_norm:
        take(r, "armo")
    #   第 6 行"杂物：`version<=0` 且非任务且 `level<=12`"；官方 `Misc.txt` 的 `version` 列
    #   0 = 经典 / 100 = 资料片专属）。被过滤掉的 code 逐条打印，**不再是一条隐式规则** ——
    #   因为 `treasureclass_c` 是照官方整行搬的，若某个 TC 引用了这些 code，该项在运行期**永不掉落**
    m_exp = [r for r in mi if as_int(r, "version") > 0 and as_int(r, "quest") == 0
             and as_int(r, "level") <= ITEM_MAX_LEVEL]
    if m_exp:
        exp_codes = sorted(col(r, "code") for r in m_exp)
        print(f"[info] item_c: 按**经典版**口径过滤掉官方 version>0（资料片专属）{len(m_exp)} 行："
              f"{exp_codes}（武器/防具侧同理：`code != normcode` 的资料片品质行不导入）")
    m_classic = [r for r in mi if as_int(r, "version") <= 0 and as_int(r, "quest") == 0
                 and as_int(r, "level") <= ITEM_MAX_LEVEL]
    m_norm = []
    for r in m_classic:
        nm = col(r, "name")
        if nm.startswith("Not used"):
            print(f"[info] item_c: 官方 Misc.txt 未启用行 code={col(r, 'code')!r} "
                  f"name={nm!r} 不导入（官方表里就是 'Not used' 占位）")
            continue
        m_norm.append(r)
    print(f"[info] item_c: Misc.txt {len(mi)} 行 ⇒ 经典且非任务且 level<={ITEM_MAX_LEVEL} 命中 "
          f"{len(m_classic)}，去掉官方 'Not used' 占位后 {len(m_norm)}")
    for r in m_norm:
        take(r, "misc")
    return s


# ═══════════════════════════════════════════════════════════════════════════
# 7) affix_c —— MagicPrefix.txt + MagicSuffix.txt
# ═══════════════════════════════════════════════════════════════════════════
AFFIX_MAX_LEVEL = 12
MOD_CN = {
    "ac%": "防御强化", "red-dmg": "物理伤害减免", "red-dmg%": "物理伤害减免%",
    "att": "命中", "att%": "命中%", "dmg%": "伤害强化", "dmg-min": "最小伤害",
    "dmg-max": "最大伤害", "str": "力量", "dex": "敏捷", "enr": "精力", "vit": "体力",
    "life": "生命", "mana": "法力", "hp%": "生命%", "mana%": "法力%", "regen": "生命回复",
    "manaregen": "法力回复", "res-fire": "火焰抗性", "res-cold": "冰冷抗性",
    "res-ltng": "闪电抗性", "res-pois": "毒素抗性", "res-all": "全抗性",
    "light": "光照范围", "balance1": "快速打击恢复", "balance2": "快速格挡",
    "balance3": "快速施法", "swing1": "攻击速度", "ac": "防御", "block": "格挡率",
    "thorns": "荆棘反弹", "crush": "压碎性打击", "deadly": "致命一击", "slow": "减速",
    "howl": "恐惧", "knock": "击退", "ease": "需求降低", "dur": "耐久", "rep-dur": "耐久回复",
    "gold%": "额外金币", "mag%": "提升魔法物品几率", "lightrad": "光照范围",
    "half-freeze": "减半冰冻时间", "nofreeze": "无法被冰冻", "sock": "孔数",
    "pois-min": "毒素下限", "pois-max": "毒素上限", "pois-len": "毒素持续",
}


def build_affix():
    s = sheet("affix", "魔法词缀（前缀 + 后缀）")
    s.col("id", "int", "词缀 id（本项目编号）")
    s.col("name", "string", "官方词缀名（英文原名；中文待官方 localized .tbl 到位后复核）")
    s.col("name_cn", "string", "中文说明（本项目按官方 mod 码 + 数值生成）")
    s.col("kind", "string", "类型：pre = 前缀 / suf = 后缀")
    s.col("lvl", "int", "词缀等级 alvl（官方 level）")
    s.col("lvl_req", "int", "需求等级（官方 levelreq）")
    s.col("group", "int", "同组互斥（官方 group）")
    s.col("mod", "string", "属性码（官方 mod1code）")
    s.col("param", "int", "属性参数（官方 mod1param）")
    s.col("min", "int", "属性最小值（官方 mod1min）")
    s.col("max", "int", "属性最大值（官方 mod1max）")
    s.col("freq", "int", "出现频率（官方 frequency）")
    s.col("class_only", "int", "是否职业专属（官方 classspecific）")
    s.col("class", "string", "专属职业（官方 class）")
    s.col("item_types", "[]string", "可出现的物品类型（官方 itype1..itype7）")
    s.col("etypes", "[]string", "排除的物品类型（官方 etype1..etype3）")

    nid = 0
    for fname, kind in (("MagicPrefix.txt", "pre"), ("MagicSuffix.txt", "suf")):
        _, rows = table(fname)
        kept = [r for r in rows if as_int(r, "spawnable") == 1 and as_int(r, "level") <= AFFIX_MAX_LEVEL]
        print(f"[info] affix_c: {fname} {len(rows)} 行 ⇒ spawnable=1 且 level<={AFFIX_MAX_LEVEL} 命中 {len(kept)}")
        for r in kept:
            nid += 1
            name = col(r, "Name")
            mod = col(r, "mod1code")
            mn, mx = as_int(r, "mod1min"), as_int(r, "mod1max")
            span = f"{mn}" if mn == mx else f"{mn}~{mx}"
            name_cn = f"{MOD_CN.get(mod, mod or '未知')} {span}" if mod else "（无属性）"
            if mod and mod not in MOD_CN:
                warn_once("affix-mod-" + mod,
                          f"[info] affix_c: mod 码 {mod!r} 无中文映射，name_cn 直接用码名")
            itypes = [col(r, f"itype{i}") for i in range(1, 8) if col(r, f"itype{i}")]
            etypes = [col(r, f"etype{i}") for i in range(1, 4) if col(r, f"etype{i}")]
            s.add(nid, name, name_cn, kind, as_int(r, "level"), as_int(r, "levelreq"),
                  as_int(r, "group"), mod, as_int(r, "mod1param"), mn, mx,
                  as_int(r, "frequency"), as_int(r, "classspecific"), col(r, "class"),
                  ";".join(itypes), ";".join(etypes))
    return s


# ═══════════════════════════════════════════════════════════════════════════
# 8) monumod_c —— MonUMod.txt
# ═══════════════════════════════════════════════════════════════════════════
def build_monumod():
    _, rows = table("MonUMod.txt")
    # 官方 MonUMod.txt 的 `constants` + `*constant desc` 两列构成"引擎常数表"：
    # 按 `*constant desc` 的文本把常数抽出来（不含任何手抄数值）。
    const = {}
    for r in rows:
        d = col(r, "*constant desc").strip()
        v = col(r, "constants")
        if d and v != "":
            if d in const and const[d] != v:
                print(f"[warn] monumod_c: 常数 {d!r} 出现多个值 {const[d]} / {v}，取后者")
            const[d] = v
    need = ["champion chance", "champion +hp%", "champion +dmg%", "champion +tohit%",
            "unique +hp%", "unique +dmg% (strong)", "unique +tohit%",
            "minion +hp%", "minion +dmg% (strong)", "minion +tohit%",
            "champion +elem min dmg%", "champion +elem max dmg%",
            "unique +elem min dmg%", "unique +elem max dmg%",
            "minion +elem min dmg%", "minion +elem max dmg%"]
    for k in need:
        if k not in const:
            print(f"[error] monumod_c: 官方 MonUMod.txt 缺常数 {k!r}")

    def c_int(k):
        return int(float(const.get(k, 0) or 0))

    def c_float(k):
        return float(const.get(k, 0) or 0)

    champ_chance = c_int("champion chance")
    mul = {
        0: dict(hp=1 + c_float("champion +hp%") / 100, dmg=1 + c_float("champion +dmg%") / 100,
                tohit=c_int("champion +tohit%"), emin=c_int("champion +elem min dmg%"),
                emax=c_int("champion +elem max dmg%")),
        1: dict(hp=1 + c_float("unique +hp%") / 100, dmg=1 + c_float("unique +dmg% (strong)") / 100,
                tohit=c_int("unique +tohit%"), emin=c_int("unique +elem min dmg%"),
                emax=c_int("unique +elem max dmg%")),
    }
    print(f"[info] monumod_c: 官方常数 champion chance={champ_chance}% "
          f"champion(hp={mul[0]['hp']}×,dmg={mul[0]['dmg']}×,tohit=+{mul[0]['tohit']}%) "
          f"unique(hp={mul[1]['hp']}×,dmg={mul[1]['dmg']}×,tohit=+{mul[1]['tohit']}%)")

    s = sheet("monumod", "精英/冠军怪词缀")
    s.col("id", "int", "词缀 id（1-…，本项目编号）")
    s.col("name", "string", "词缀名（中文）")
    s.col("code", "string", "官方 MonUMod.uniquemod")
    s.col("kind", "int", "类型：0 = 冠军怪 / 1 = 精英怪（唯一怪）")
    s.col("version", "int", "官方 version（0 = 经典，100 = 资料片专属）")
    s.col("champion_chance", "int", "冠军怪出现几率%（官方 MonUMod.txt 常数 'champion chance'，全局）")
    s.col("hp_mul", "float32", "生命倍率（1 + 官方常数 hp% / 100）")
    s.col("dmg_mul", "float32", "伤害倍率（1 + 官方常数 dmg% / 100）")
    s.col("tohit_add", "int", "命中加成%（官方常数 +tohit%）")
    s.col("elem_min_add", "int", "元素伤害下限加成%（官方常数 +elem min dmg%）")
    s.col("elem_max_add", "int", "元素伤害上限加成%（官方常数 +elem max dmg%）")
    s.col("ac_mul", "float32", "护甲倍率（⚠️ 官方 MonUMod.txt 未定义该常数，占位 1.0）")
    s.col("res_phys", "int", "物理抗性加成%（⚠️ 官方未定义，占位 0）")
    s.col("res_magic", "int", "魔法抗性加成%（⚠️ 官方未定义，占位 0）")
    s.col("res_fire", "int", "火焰抗性加成%（⚠️ 官方未定义，占位 0）")
    s.col("res_light", "int", "闪电抗性加成%（⚠️ 官方未定义，占位 0）")
    s.col("res_cold", "int", "冰冷抗性加成%（⚠️ 官方未定义，占位 0）")
    s.col("res_poison", "int", "毒素抗性加成%（⚠️ 官方未定义，占位 0）")
    s.col("cpick", "int", "冠军怪抽取权重（官方 cpick）")
    s.col("upick", "int", "精英怪抽取权重（官方 upick）")
    s.col("constants", "string", "官方 constants 原文（审计用）")
    s.col("desc", "string", "官方 *constant desc 原文（审计用）")

    picked = []
    for r in rows:
        picks = any(col(r, f) for f in ("cpick", "cpick (N)", "cpick (H)",
                                        "upick", "upick (N)", "upick (H)"))
        if as_int(r, "enabled") != 1:
            if picks or as_int(r, "champion") == 1:
                print(f"[info] monumod_c: 官方 enabled=0 的 {col(r, 'uniquemod')!r} 不导入")
            continue
        if not picks and as_int(r, "champion") != 1:
            continue
        picked.append(r)
    print(f"[info] monumod_c: MonUMod.txt {len(rows)} 行 ⇒ 可抽取的精英/冠军词缀 {len(picked)} 行")
    for i, r in enumerate(picked, start=1):
        code = col(r, "uniquemod")
        kind = 0 if as_int(r, "champion") == 1 else 1
        m = mul[kind]
        cpick = as_int(r, "cpick") or as_int(r, "cpick (N)") or as_int(r, "cpick (H)")
        upick = as_int(r, "upick") or as_int(r, "upick (N)") or as_int(r, "upick (H)")
        OFFICIAL_NAME["monumod"][code] = (f"{col(r, '*constant desc')} / "
                                          f"{'champion' if kind == 0 else 'unique'}")
        s.add(i, cn("monumod", code), code, kind, as_int(r, "version"), champ_chance,
              m["hp"], m["dmg"], m["tohit"], m["emin"], m["emax"],
              1.0, 0, 0, 0, 0, 0, 0, cpick, upick,
              col(r, "constants"), col(r, "*constant desc"))
    return s


# ═══════════════════════════════════════════════════════════════════════════
# 9) treasureclass_c —— TreasureClassEx.txt
# ═══════════════════════════════════════════════════════════════════════════
def build_treasureclass():
    _, rows = table("TreasureClassEx.txt")
    by_name = {}
    for r in rows:
        n = col(r, "Treasure Class")
        if n:
            by_name[n] = r
    start = [n for n in by_name if "Act 1" in n and "(N)" not in n and "(H)" not in n]

    #   永远进不了闭包 ⇒ `DeathFlow.TreasureClassIdOf` 返回 0 ⇒ 尖刺鼠永久零掉落。
    #   根 = 本项目 8 只怪的 `MonStats.TreasureClass1/2/3`（普通/冠军/唯一槽位），
    #   与 `Module/Combat/DeathFlow.cs` 的取表口径**逐列一致**（不靠名字前缀猜）。
    _, ms = table("MonStats.txt")
    runtime_roots = set()
    for r in ms:
        if col(r, "Id") not in MONSTER_IDS:
            continue
        for c in ("TreasureClass1", "TreasureClass2", "TreasureClass3"):
            v = col(r, c)
            if v:
                runtime_roots.add(v)
    absent = sorted(t for t in runtime_roots if t not in by_name)
    if absent:                                  # 载体不可得 = 转换器无能为力 ⇒ 必须显式点名（⛔ 不静默）
        print(f"[warn] treasureclass_c: monster_c 引用的 TC 在官方 TreasureClassEx.txt 里**不存在**：{absent}"
              f" ⇒ 这些怪/精英落回 LootRoller 的降级链（运行期会 WarnOnce）")
    start = sorted(set(start) | {t for t in runtime_roots if t in by_name})
    print(f"[info] treasureclass_c: 普通难度 Act 1 TC + 8 只怪的 TC 槽位根 = {len(start)} 个，开始做引用闭包")
    closure = set(start)
    queue = list(start)
    while queue:
        n = queue.pop()
        r = by_name[n]
        for k in range(1, 11):
            item = col(r, f"Item{k}")
            if item and item in by_name and item not in closure:
                closure.add(item)
                queue.append(item)
    print(f"[info] treasureclass_c: 闭包后共 {len(closure)} 个 TC（含被引用的子 TC）")

    s = sheet("treasureclass", "掉落表（Act I 普通难度，含引用闭包）")
    s.col("name", "string", "TC 名（官方 Treasure Class，主键）")
    s.col("group", "int", "TC 组（官方 group）")
    s.col("level", "int", "TC 等级（官方 level）")
    s.col("picks", "int", "抽取次数（官方 Picks）")
    s.col("nodrop", "int", "不掉落权重（官方 NoDrop）")
    s.col("drops", "string", "掉落项与权重，格式 item;prob|item;prob（官方 Item1..Item10 / Prob1..Prob10）")
    s.col("next_tc", "string", "被引用的子 TC（官方 Item 里指向 TC 的项，; 分隔；指向物品的项不在此列）")
    s.col("unique", "int", "成色·唯一（官方 Unique）")
    s.col("set", "int", "成色·套装（官方 Set）")
    s.col("rare", "int", "成色·稀有（官方 Rare）")
    s.col("magic", "int", "成色·魔法（官方 Magic）")
    s.col("is_act1_start", "int", "是否属于 Act 1 起点 TC（1 = 闭包起点，0 = 被引用进来的子 TC）")

    for n in sorted(closure):
        r = by_name[n]
        pairs = []
        subs = []
        for k in range(1, 11):
            item, prob = col(r, f"Item{k}"), col(r, f"Prob{k}")
            if not item:
                continue
            if prob == "":
                print(f"[info] treasureclass_c: TC {n!r} 的 Item{k}={item!r} 无 Prob，按权重 1 记录")
                prob = "1"
            pairs.append(f"{item};{prob}")
            if item in by_name:
                subs.append(item)
        s.add(n, as_int(r, "group"), as_int(r, "level"), as_int(r, "Picks"),
              as_int(r, "NoDrop"), "|".join(pairs), ";".join(subs),
              as_int(r, "Unique"), as_int(r, "Set"), as_int(r, "Rare"), as_int(r, "Magic"),
              1 if n in start else 0)

    # R3 闸门：`monster_c` 引用的 TC（普通/冠军/唯一三个槽位）必须**全部**在本表里 ⇒ 差集必须为空。
    diff = sorted(t for t in runtime_roots if t not in closure)
    print(f"[info] treasureclass_c: 闸门 monster_c 引用 TC ⊆ treasureclass_c ⇒ 差集 {diff}（应为空）")
    if diff:
        print(f"[error] treasureclass_c: 差集非空 {diff} ⇒ 对应怪/精英永久不掉落（先修闭包根再打表）")
    return s


# ═══════════════════════════════════════════════════════════════════════════
# 10) missile_c —— Missiles.txt
# ═══════════════════════════════════════════════════════════════════════════
SKILL_MISSILE_COLS = ["srvmissile", "srvmissilea", "srvmissileb", "srvmissilec",
                      "cltmissile", "cltmissilea", "cltmissileb", "cltmissilec", "cltmissiled"]
MON_MISSILE_COLS = ["MissA1", "MissA2", "MissS1", "MissS2", "MissS3", "MissS4", "MissC", "MissSQ"]
MISSILE_SKILL_MAX_LEVEL = 12


def build_missile():
    _, msx = table("Missiles.txt")
    _, sk = table("skills.txt")
    _, ms = table("MonStats.txt")
    by_code = {col(r, "Missile"): r for r in msx}

    wanted = {"arrow": "基础箭矢（普攻）"}
    for r in sk:
        if col(r, "charclass") not in SKILL_CLASS.values():
            continue
        if as_int(r, "reqlevel") > MISSILE_SKILL_MAX_LEVEL:
            continue
        for c in SKILL_MISSILE_COLS:
            v = col(r, c)
            if v:
                wanted.setdefault(v, f"技能 {col(r, 'skill')}.{c}")
    for r in ms:
        if col(r, "Id") in MONSTER_IDS:
            for c in MON_MISSILE_COLS:
                v = col(r, c)
                if v:
                    wanted.setdefault(v, f"怪物 {col(r, 'Id')}.{c}")
    print(f"[info] missile_c: 被引用的投射物 {len(wanted)} 个")
    missing = [k for k in wanted if k not in by_code]
    for k in missing:
        print(f"[warn] missile_c: 投射物 {k!r}（{wanted[k]}）在 Missiles.txt 里找不到，跳过")

    s = sheet("missile", "投射物")
    s.col("id", "int", "投射物 id（本项目编号）")
    s.col("name", "string", "投射物名（中文）")
    s.col("code", "string", "官方 Missiles.Missile")
    s.col("official_id", "int", "官方 Missiles.Id")
    s.col("speed", "int", "飞行速度（官方 Vel）")
    s.col("max_vel", "int", "最大速度（官方 MaxVel）")
    s.col("accel", "int", "加速度（官方 Accel）")
    s.col("radius", "int", "碰撞半径（官方 Size）")
    s.col("range", "int", "射程（官方 Range）")
    s.col("dmg_min", "int", "物理伤害下限（官方 MinDamage，常为空 ⇒ 0）")
    s.col("dmg_max", "int", "物理伤害上限（官方 MaxDamage，常为空 ⇒ 0）")
    s.col("elem_min", "int", "元素伤害下限（官方 EMin，常为空 ⇒ 0）")
    s.col("elem_max", "int", "元素伤害上限（官方 Emax，常为空 ⇒ 0）")
    s.col("dmg_type", "string", "元素类型（官方 EType）")
    s.col("light", "int", "光照半径（官方 Light）")
    s.col("cel_file", "string", "动画文件（官方 CelFile）")
    s.col("collide_type", "int", "碰撞类型（官方 CollideType）")
    s.col("next_hit", "int", "穿透后继续命中（官方 NextHit）")
    s.col("next_delay", "int", "重复命中间隔（官方 NextDelay）")
    s.col("sub_missile", "string", "子投射物（官方 SubMissile1）")
    s.col("used_by", "string", "引用来源（审计用）")

    i = 0
    for code in sorted(wanted):
        r = by_code.get(code)
        if r is None:
            continue
        i += 1
        OFFICIAL_NAME["missile"][code] = (f"{col(r, 'CelFile')} / {wanted[code]}")
        s.add(i, cn("missile", code), code, as_int(r, "Id"),
              as_int(r, "Vel"), as_int(r, "MaxVel"), as_int(r, "Accel"),
              as_int(r, "Size"), as_int(r, "Range"),
              as_int(r, "MinDamage"), as_int(r, "MaxDamage"),
              as_int(r, "EMin"), as_int(r, "Emax"), col(r, "EType"),
              as_int(r, "Light"), col(r, "CelFile"), as_int(r, "CollideType"),
              as_int(r, "NextHit"), as_int(r, "NextDelay"), col(r, "SubMissile1"),
              wanted[code])
    return s


# ═══════════════════════════════════════════════════════════════════════════
# main
# ═══════════════════════════════════════════════════════════════════════════
BUILDERS = [build_class, build_startitem, build_experience, build_level, build_monster, build_skill,
            build_item, build_affix, build_monumod, build_treasureclass, build_missile]


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--report", action="store_true", help="只打印统计，不写文件")
    ap.add_argument("--names", action="store_true", help="打印缺中文映射的官方标识")
    ap.add_argument("--src", default="", metavar="DIR",
                    help="官方 1.10f txt 所在目录（其下应有 data/global/excel/*.txt）；"
                         "默认取环境变量 " + ENV_SRC_VAR + "，再默认 <项目根>/原版资源/参考工程_Diablerie/…")
    ap.add_argument("--seed", type=int, default=20240101, help="预留（本脚本不使用随机数）")
    args = ap.parse_args()
    random.seed(args.seed)

    # 输入目录：命令行 > 环境变量 > 项目内默认副本（表读取函数读的是模块级 SRC_DIR，故这里覆盖它）
    global SRC_DIR
    SRC_DIR = os.path.abspath(os.path.expanduser(
        args.src or os.environ.get(ENV_SRC_VAR) or DEFAULT_SRC_DIR))
    if args.src:                                   # 显式给了就报一行，便于对照复跑
        print(f"[info] 输入目录来自 --src：{SRC_DIR}")
    elif os.environ.get(ENV_SRC_VAR):
        print(f"[info] 输入目录来自 ${ENV_SRC_VAR}：{SRC_DIR}")
    else:
        print(f"[info] 输入目录取项目内默认副本：{SRC_DIR}")

    if not os.path.isdir(SRC_DIR):
        print(f"[fatal] 官方数据表目录不存在：{SRC_DIR}")
        print(f"        用 --src <dir> 或环境变量 {ENV_SRC_VAR} 指定官方 1.10f txt 所在目录"
              f"（其下应有 data/global/excel/*.txt）。")
        return 2
    if not os.path.isdir(OUT_DIR):
        os.makedirs(OUT_DIR)

    for b in BUILDERS:
        b()

    print("\n" + "=" * 70)
    total = 0
    for lg, t in SHEETS.items():
        print(f"{t.title:<34} {lg}_c  {len(t.rows):>4} 行 × {len(t.cols):>3} 列")
        total += len(t.rows)
    print(f"{'合计':<34} {total:>4} 行")

    if MISSING_NAMES["item"]:
        print(f"[warn] item 中文映射缺 {len(MISSING_NAMES['item'])} 条："
              f"{sorted(MISSING_NAMES['item'])[:20]}{' …' if len(MISSING_NAMES['item']) > 20 else ''}")
    for kind in ("class", "level", "monster", "monumod", "missile", "skill"):
        if MISSING_NAMES[kind]:
            print(f"[warn] {kind} 中文映射缺：{sorted(MISSING_NAMES[kind])}")

    if args.names:
        print("\n---- 中文映射审计清单（key = 官方标识 / 官方原文）----")
        for kind in ("class", "level", "monster", "monumod", "missile", "skill", "item"):
            got = OFFICIAL_NAME[kind]
            if kind == "item":
                print(f"# {kind}（共 {len(got)}，★ = 缺中文映射）")
                for k in sorted(got):
                    star = "★" if k in MISSING_NAMES[kind] else " "
                    cn_v = cn_names_lookup(kind, k)
                    print(f'    "{k}": "{cn_v}",  # {star} {got[k]}')
            elif got:
                print(f"# {kind}（共 {len(got)}）")
                for k in sorted(got):
                    star = "★" if k in MISSING_NAMES[kind] else " "
                    print(f'    "{k}": "{cn_names_lookup(kind, k)}",  # {star} {got[k]}')
        return 0

    if args.report:
        print("\n[report] 未写文件（--report）")
        return 0

    print("\n---- 写出源表 ----")
    for lg, t in SHEETS.items():
        path, n, c = t.dump(OUT_DIR)
        print(f"  写出 {path}  ({n} 行 × {c} 列)")
    print("完成。")
    return 0


if __name__ == "__main__":
    sys.exit(main())
