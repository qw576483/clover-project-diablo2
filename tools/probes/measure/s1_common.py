# -*- coding: utf-8 -*-
"""S1 数值维 —— 比对器共用件（判据资产，非一次性产物）。

为什么要这个模块（本片的核心设计约束，别改）：
  * `策划/数值文档/*_c.txt`（转写件）与 `client/Assets/StreamingAssets/Table/*.tsv`（运行时表）
    **逐格相同**（实测 row-level-diff=0，10/10 张表）—— 二者是同一份转写的两个落点。
  * ⇒ 拿 `_c.txt` 当「官方值」去比运行时表 = **同义反复**，永远只会报「一致」，判不出任何东西
    （全局 skill §4 第 7 条：判据要判"过程"不判"结果"；一个总能变绿的检查等于没判）。
  * ⇒ **官方值只认官方载体**（`data/global/excel/*.txt` 或由 `*.mpq` 解包出来的同一批 txt）。
    `_c.txt` 在比对器里的角色只有两个：
      ① `s1_plan.tsv` 里说明「我们这边的来源」与「官方表的哪一列」的对照；
      ② 官方载体到位后，可作为**交叉校验对象**（转写 vs 官方，用于发现转写漂移）。
  * 因此：官方载体不在位时，本比对器对 821 行的结论**一律** `缺官方值`，⛔ 一个 `一致` 都不许有。

官方值怎么算出来的：**直接调用** `tools/table-convert/convert.py` 的 build_* 函数
（`convert.py` 就是把官方 txt 抽成本项目源表的那个转换器，列映射/公式/取整口径全在它里面）。
⇒ 比对器不重写任何映射、不重写任何公式，杜绝"两边各写一遍、各错一半"。
"""
import io
import os
import sys

try:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    sys.stderr.reconfigure(encoding="utf-8", errors="replace")
except Exception:
    pass

# ─────────────────────────────────────────────────────────────────────────────
# 路径
# ─────────────────────────────────────────────────────────────────────────────
# 运行时表（"我们这边的值"）
RT_DIR_REL = os.path.join("client", "Assets", "StreamingAssets", "Table")
# 转写件（官方文本的转写，**不是**官方载体）
DOC_DIR_REL = os.path.join("策划", "数值文档")
MATRIX_REL = os.path.join("策划", "状态矩阵.tsv")
CONVERTER_REL = os.path.join("tools", "table-convert", "convert.py")
# 官方 txt 目录的两种常见落点（`--src` 给哪一层都认）
EXCEL_SUBDIRS = (os.path.join("data", "global", "excel"),)

# 逻辑名 → 运行时表文件名（无扩展名）+ convert.py 里对应的 build 函数名
TABLES = [
    ("class", "Class", "build_class"),
    ("experience", "Experience", "build_experience"),
    ("level", "Level", "build_level"),
    ("monster", "Monster", "build_monster"),
    ("skill", "Skill", "build_skill"),
    ("item", "Item", "build_item"),
    ("affix", "Affix", "build_affix"),
    ("monumod", "Monumod", "build_monumod"),
    ("treasureclass", "Treasureclass", "build_treasureclass"),
    ("missile", "Missile", "build_missile"),
]
LOGICAL_BY_ENTITY_TAG = {"Affix": "affix", "Skill": "skill", "Item": "item",
                         "Experience": "experience", "Treasureclass": "treasureclass",
                         "Missile": "missile", "Monumod": "monumod",
                         "Monster": "monster", "Class": "class", "Level": "level"}

# 每张表需要官方 txt 里的哪些文件（少一个 ⇒ 该表整表 `缺官方值`，并写明缺哪个）
TABLE_DEPS = {
    "class": ["charstats.txt"],
    "experience": ["experience.txt"],
    "level": ["Levels.txt"],
    "monster": ["MonStats.txt", "MonStats2.txt", "MonAi.txt", "MonLvl.txt"],
    "skill": ["skills.txt", "SkillDesc.txt"],
    "item": ["Weapons.txt", "Armor.txt", "Misc.txt"],
    "affix": ["MagicPrefix.txt", "MagicSuffix.txt"],
    "monumod": ["MonUMod.txt"],
    "treasureclass": ["TreasureClassEx.txt"],
    "missile": ["Missiles.txt", "skills.txt", "MonStats.txt"],
}
ALL_OFFICIAL_FILES = sorted({f for fs in TABLE_DEPS.values() for f in fs})

# ─────────────────────────────────────────────────────────────────────────────
# 字段映射表（**来源**：逐条读 tools/table-convert/convert.py 的 build_* 得出；行号见注释）
#   (运行时列名, kind, 官方来源标签, 判据)
#   kind: "num"      = 官方某一列原值（公式 ≈ 恒等）
#         "formula"  = 官方若干列经公式/映射求值
#         "none"     = **官方 txt 里没有对应对手** ⇒ 天然 `缺官方值`（见 NO_OFFICIAL_CARRIER）
#   判据 ∈ {"数值相等", "公式求值相等"}
# ⚠️ 本表必须与 convert.py 的 Sheet.cols **逐列同序**（s1_common.assert_field_map 会机械校验）。
# ─────────────────────────────────────────────────────────────────────────────
FIELD_MAP = {
    # convert.py:315-353 build_class()  —— charstats.txt
    "class": [
        ("id", "none", "（本项目顺序编号 1-5，官方表里没有该列）"),
        ("name", "none", "（中文显示名 cn_names.py；官方 txt 只有英文 class 名）"),
        ("code", "num", "charstats.txt:class", "数值相等"),
        ("skill_class", "none", "（本项目常量映射 SKILL_CLASS，官方无该列）"),
        ("str", "num", "charstats.txt:str", "数值相等"),
        ("dex", "num", "charstats.txt:dex", "数值相等"),
        ("vit", "num", "charstats.txt:vit", "数值相等"),
        ("eng", "num", "charstats.txt:int", "数值相等"),
        ("stat_per_lvl", "num", "charstats.txt:StatPerLevel", "数值相等"),
        ("life_per_lvl", "formula", "charstats.txt:LifePerLevel ÷4（官方注释 'in fourths'）", "公式求值相等"),
        ("mana_per_lvl", "formula", "charstats.txt:ManaPerLevel ÷4", "公式求值相等"),
        ("stam_per_lvl", "formula", "charstats.txt:StaminaPerLevel ÷4", "公式求值相等"),
        ("life_per_vit", "formula", "charstats.txt:LifePerVitality ÷4", "公式求值相等"),
        ("mana_per_mag", "formula", "charstats.txt:ManaPerMagic ÷4", "公式求值相等"),
        ("stam_per_vit", "formula", "charstats.txt:StaminaPerVitality ÷4", "公式求值相等"),
        ("to_hit_factor", "num", "charstats.txt:ToHitFactor", "数值相等"),
        ("walk_velocity", "num", "charstats.txt:WalkVelocity", "数值相等"),
        ("run_velocity", "num", "charstats.txt:RunVelocity", "数值相等"),
        ("start_skill", "num", "charstats.txt:StartSkill", "数值相等"),
    ],
    # convert.py:362-381 build_experience() —— experience.txt
    "experience": [
        ("level", "num", "experience.txt:Level", "数值相等"),
        ("exp", "num", "experience.txt:Amazon（官方 8 职业同值）", "数值相等"),
        ("exp_ratio", "num", "experience.txt:ExpRatio", "数值相等"),
    ],
    # convert.py:402-458 build_level() —— Levels.txt
    "level": [
        ("id", "none", "（本项目顺序编号 1-3）"),
        ("name", "none", "（中文显示名 cn_names.py）"),
        ("code", "num", "Levels.txt:Name", "数值相等"),
        ("level_name", "num", "Levels.txt:LevelName", "数值相等"),
        ("level_id", "num", "Levels.txt:Id", "数值相等"),
        ("act", "num", "Levels.txt:Act", "数值相等"),
        ("area_level", "num", "Levels.txt:MonLvl1", "数值相等"),
        ("mon_density", "num", "Levels.txt:MonDen", "数值相等"),
        ("mon_umin", "num", "Levels.txt:MonUMin", "数值相等"),
        ("mon_umax", "num", "Levels.txt:MonUMax", "数值相等"),
        ("num_mon", "num", "Levels.txt:NumMon", "数值相等"),
        ("monsters", "formula", "Levels.txt:mon1..mon10 → 本项目怪物编号（MONSTER_IDX）", "公式求值相等"),
        ("size_x", "num", "Levels.txt:SizeX", "数值相等"),
        ("size_y", "num", "Levels.txt:SizeY", "数值相等"),
        ("size_min", "formula", "min(Levels.txt:SizeX, Levels.txt:SizeY)", "公式求值相等"),
        ("size_max", "formula", "max(Levels.txt:SizeX, Levels.txt:SizeY)", "公式求值相等"),
        ("random", "formula", "Levels.txt:DrlgType（==2 ⇒ 0，其余 1）", "公式求值相等"),
        ("drlg_type", "num", "Levels.txt:DrlgType", "数值相等"),
        ("is_inside", "num", "Levels.txt:IsInside", "数值相等"),
    ],
    # convert.py:461-539 build_monster() —— MonStats/MonStats2/MonAi/MonLvl
    "monster": [
        ("id", "none", "（本项目顺序编号 1-8）"),
        ("name", "none", "（中文显示名 cn_names.py）"),
        ("code", "num", "MonStats.txt:Id", "数值相等"),
        ("name_str", "num", "MonStats.txt:NameStr", "数值相等"),
        ("base_id", "num", "MonStats.txt:BaseId", "数值相等"),
        ("level", "formula", "Levels.txt:MonLvl1（区域等级常量 1）", "公式求值相等"),
        ("base_level", "num", "MonStats.txt:Level", "数值相等"),
        ("min_grp", "num", "MonStats.txt:MinGrp", "数值相等"),
        ("max_grp", "num", "MonStats.txt:MaxGrp", "数值相等"),
        ("rarity", "num", "MonStats.txt:Rarity", "数值相等"),
        ("hp", "formula", "(trunc(minHP×HP%) + trunc(maxHP×HP%)) ÷ 2（整除）", "公式求值相等"),
        ("hp_min", "formula", "trunc(MonStats.txt:minHP × MonLvl.txt:HP ÷100)", "公式求值相等"),
        ("hp_max", "formula", "trunc(MonStats.txt:maxHP × MonLvl.txt:HP ÷100)", "公式求值相等"),
        ("ac", "formula", "trunc(MonStats.txt:AC × MonLvl.txt:AC ÷100)", "公式求值相等"),
        ("ar", "formula", "trunc(MonStats.txt:A1TH × MonLvl.txt:TH ÷100)", "公式求值相等"),
        ("dmg_min", "formula", "trunc(MonStats.txt:A1MinD × MonLvl.txt:DM ÷100)", "公式求值相等"),
        ("dmg_max", "formula", "trunc(MonStats.txt:A1MaxD × MonLvl.txt:DM ÷100)", "公式求值相等"),
        ("exp", "formula", "trunc(MonStats.txt:Exp × MonLvl.txt:XP ÷100)", "公式求值相等"),
        ("ai", "num", "MonStats.txt:AI", "数值相等"),
        ("speed", "num", "MonStats.txt:Velocity", "数值相等"),
        ("run", "num", "MonStats.txt:Run", "数值相等"),
        ("res_phys", "num", "MonStats.txt:ResDm", "数值相等"),
        ("res_magic", "num", "MonStats.txt:ResMa", "数值相等"),
        ("res_fire", "num", "MonStats.txt:ResFi", "数值相等"),
        ("res_light", "num", "MonStats.txt:ResLi", "数值相等"),
        ("res_cold", "num", "MonStats.txt:ResCo", "数值相等"),
        ("res_poison", "num", "MonStats.txt:ResPo", "数值相等"),
        ("sprite", "num", "MonStats.txt:Code", "数值相等"),
        ("corpse_usable", "num", "MonStats2.txt:corpseSel", "数值相等"),
        ("revive", "num", "MonStats2.txt:revive", "数值相等"),
        ("undead", "formula", "MonStats.txt:lUndead | hUndead", "公式求值相等"),
        ("demon", "num", "MonStats.txt:demon", "数值相等"),
        ("flying", "num", "MonStats.txt:flying", "数值相等"),
        ("treasure_class", "num", "MonStats.txt:TreasureClass1", "数值相等"),
    ],
    # convert.py:545-670 build_skill() —— skills.txt + SkillDesc.txt
    "skill": [
        ("id", "none", "（本项目顺序编号 1-150）"),
        ("class", "formula", "skills.txt:charclass → 本项目职业编号（CLASS_ORDER）", "公式求值相等"),
        ("tree", "formula", "SkillDesc.txt:SkillPage（经 skills.txt:skilldesc 关联）", "公式求值相等"),
        ("name", "none", "（中文显示名 cn_names.py）"),
        ("code", "num", "skills.txt:skill", "数值相等"),
        ("official_id", "num", "skills.txt:Id", "数值相等"),
        ("req_level", "num", "skills.txt:reqlevel", "数值相等"),
        ("max_level", "num", "skills.txt:maxlvl", "数值相等"),
        ("req_skill", "formula", "skills.txt:reqskill1 → 本项目技能编号（0=无）", "公式求值相等"),
        ("skill_points", "num", "skills.txt:skpoints", "数值相等"),
        ("mana_cost", "num", "skills.txt:mana", "数值相等"),
        ("mana_per_lvl", "num", "skills.txt:lvlmana", "数值相等"),
        ("dmg_min", "formula", "rnd((skills.txt:MinDam + EMin) × 2^HitShift ÷ 256)", "公式求值相等"),
        ("dmg_max", "formula", "rnd((skills.txt:MaxDam + EMax) × 2^HitShift ÷ 256)", "公式求值相等"),
        ("phys_min", "num", "skills.txt:MinDam", "数值相等"),
        ("phys_max", "num", "skills.txt:MaxDam", "数值相等"),
        ("elem_min", "num", "skills.txt:EMin", "数值相等"),
        ("elem_max", "num", "skills.txt:EMax", "数值相等"),
        ("phys_min_lev1", "num", "skills.txt:MinLevDam1", "数值相等"),
        ("phys_min_lev2", "num", "skills.txt:MinLevDam2", "数值相等"),
        ("phys_min_lev3", "num", "skills.txt:MinLevDam3", "数值相等"),
        ("phys_min_lev4", "num", "skills.txt:MinLevDam4", "数值相等"),
        ("phys_min_lev5", "num", "skills.txt:MinLevDam5", "数值相等"),
        ("phys_max_lev1", "num", "skills.txt:MaxLevDam1", "数值相等"),
        ("phys_max_lev2", "num", "skills.txt:MaxLevDam2", "数值相等"),
        ("phys_max_lev3", "num", "skills.txt:MaxLevDam3", "数值相等"),
        ("phys_max_lev4", "num", "skills.txt:MaxLevDam4", "数值相等"),
        ("phys_max_lev5", "num", "skills.txt:MaxLevDam5", "数值相等"),
        ("elem_min_lev1", "num", "skills.txt:EMinLev1", "数值相等"),
        ("elem_min_lev2", "num", "skills.txt:EMinLev2", "数值相等"),
        ("elem_min_lev3", "num", "skills.txt:EMinLev3", "数值相等"),
        ("elem_min_lev4", "num", "skills.txt:EMinLev4", "数值相等"),
        ("elem_min_lev5", "num", "skills.txt:EMinLev5", "数值相等"),
        ("elem_max_lev1", "num", "skills.txt:EMaxLev1", "数值相等"),
        ("elem_max_lev2", "num", "skills.txt:EMaxLev2", "数值相等"),
        ("elem_max_lev3", "num", "skills.txt:EMaxLev3", "数值相等"),
        ("elem_max_lev4", "num", "skills.txt:EMaxLev4", "数值相等"),
        ("elem_max_lev5", "num", "skills.txt:EMaxLev5", "数值相等"),
        ("hit_shift", "num", "skills.txt:HitShift", "数值相等"),
        ("dmg_type", "formula", "skills.txt:EType（空且有伤害 ⇒ phys）", "公式求值相等"),
        ("passive", "num", "skills.txt:passive", "数值相等"),
        ("missile", "formula", "skills.txt:srvmissile（空 ⇒ cltmissile）", "公式求值相等"),
        ("desc", "formula", "按官方 EType/mana/lvlmana/reqlevel 拼装", "公式求值相等"),
    ],
    # convert.py:686-795 build_item() —— Weapons/Armor/Misc
    "item": [
        ("id", "none", "（本项目顺序编号）"),
        ("name", "none", "（中文显示名 cn_names.py）"),
        ("code", "num", "Weapons|Armor|Misc.txt:code", "数值相等"),
        ("source", "formula", "行来自哪张官方表（weap/armo/misc）", "公式求值相等"),
        ("type", "num", "Weapons|Armor|Misc.txt:type", "数值相等"),
        ("subtype", "formula", "Weapons|Armor|Misc.txt:type2（空 ⇒ wclass）", "公式求值相等"),
        ("grid_w", "num", "…:invwidth", "数值相等"),
        ("grid_h", "num", "…:invheight", "数值相等"),
        ("lvl_req", "num", "…:levelreq", "数值相等"),
        ("item_level", "num", "…:level", "数值相等"),
        ("str_req", "num", "…:reqstr", "数值相等"),
        ("dex_req", "num", "…:reqdex", "数值相等"),
        ("dmg_min", "formula", "…:mindam（两侧皆 0 ⇒ 回退 2handmindam）", "公式求值相等"),
        ("dmg_max", "formula", "…:maxdam（两侧皆 0 ⇒ 回退 2handmaxdam）", "公式求值相等"),
        ("dmg2_min", "num", "…:2handmindam", "数值相等"),
        ("dmg2_max", "num", "…:2handmaxdam", "数值相等"),
        ("str_bonus", "num", "Weapons.txt:StrBonus（Armor/Misc 无此列 ⇒ 0）", "数值相等"),
        ("dex_bonus", "num", "Weapons.txt:DexBonus（Armor/Misc 无此列 ⇒ 0）", "数值相等"),
        ("mis_min", "num", "…:minmisdam", "数值相等"),
        ("mis_max", "num", "…:maxmisdam", "数值相等"),
        ("def_min", "num", "…:minac", "数值相等"),
        ("def_max", "num", "…:maxac", "数值相等"),
        ("block", "num", "…:block", "数值相等"),
        ("price", "num", "…:cost", "数值相等"),
        ("stack", "num", "…:stackable", "数值相等"),
        ("min_stack", "num", "…:minstack", "数值相等"),
        ("max_stack", "num", "…:maxstack", "数值相等"),
        ("two_handed", "num", "…:2handed", "数值相等"),
        ("one_or_two", "num", "…:1or2handed", "数值相等"),
        ("speed", "num", "…:speed", "数值相等"),
        ("sockets", "num", "…:gemsockets", "数值相等"),
        ("belt_slots", "num", "…:belt", "数值相等"),
        ("quest", "num", "…:quest", "数值相等"),
    ],
    # convert.py:819-859 build_affix() —— MagicPrefix/MagicSuffix
    "affix": [
        ("id", "none", "（本项目顺序编号）"),
        ("name", "num", "MagicPrefix|MagicSuffix.txt:Name", "数值相等"),
        ("name_cn", "formula", "按官方 mod1code + mod1min/max 生成说明", "公式求值相等"),
        ("kind", "formula", "行来自哪张官方表（pre/suf）", "公式求值相等"),
        ("lvl", "num", "…:level", "数值相等"),
        ("lvl_req", "num", "…:levelreq", "数值相等"),
        ("group", "num", "…:group", "数值相等"),
        ("mod", "num", "…:mod1code", "数值相等"),
        ("param", "num", "…:mod1param", "数值相等"),
        ("min", "num", "…:mod1min", "数值相等"),
        ("max", "num", "…:mod1max", "数值相等"),
        ("freq", "num", "…:frequency", "数值相等"),
        ("class_only", "num", "…:classspecific", "数值相等"),
        ("class", "num", "…:class", "数值相等"),
        ("item_types", "formula", "…:itype1..itype7", "公式求值相等"),
        ("etypes", "formula", "…:etype1..etype3", "公式求值相等"),
    ],
    # convert.py:865-954 build_monumod() —— MonUMod.txt
    "monumod": [
        ("id", "none", "（本项目顺序编号）"),
        ("name", "none", "（中文显示名 cn_names.py）"),
        ("code", "num", "MonUMod.txt:uniquemod", "数值相等"),
        ("kind", "formula", "MonUMod.txt:champion（==1 ⇒ 0，否则 1）", "公式求值相等"),
        ("version", "num", "MonUMod.txt:version", "数值相等"),
        ("champion_chance", "formula", "MonUMod.txt:constants（*constant desc = 'champion chance'）", "公式求值相等"),
        ("hp_mul", "formula", "1 + constants('champion +hp%' | 'unique +hp%') ÷100", "公式求值相等"),
        ("dmg_mul", "formula", "1 + constants('champion +dmg%' | 'unique +dmg% (strong)') ÷100", "公式求值相等"),
        ("tohit_add", "formula", "constants('…+tohit%')", "公式求值相等"),
        ("elem_min_add", "formula", "constants('…+elem min dmg%')", "公式求值相等"),
        ("elem_max_add", "formula", "constants('…+elem max dmg%')", "公式求值相等"),
        ("ac_mul", "none", "⛔ 官方 MonUMod.txt 未定义该常数（本项目登记占位 1.0）"),
        ("res_phys", "none", "⛔ 官方 MonUMod.txt 未定义（本项目登记占位 0）"),
        ("res_magic", "none", "⛔ 官方 MonUMod.txt 未定义（本项目登记占位 0）"),
        ("res_fire", "none", "⛔ 官方 MonUMod.txt 未定义（本项目登记占位 0）"),
        ("res_light", "none", "⛔ 官方 MonUMod.txt 未定义（本项目登记占位 0）"),
        ("res_cold", "none", "⛔ 官方 MonUMod.txt 未定义（本项目登记占位 0）"),
        ("res_poison", "none", "⛔ 官方 MonUMod.txt 未定义（本项目登记占位 0）"),
        ("cpick", "formula", "MonUMod.txt:cpick（空 ⇒ cpick (N) ⇒ cpick (H)）", "公式求值相等"),
        ("upick", "formula", "MonUMod.txt:upick（空 ⇒ upick (N) ⇒ upick (H)）", "公式求值相等"),
        ("constants", "num", "MonUMod.txt:constants", "数值相等"),
        ("desc", "num", "MonUMod.txt:*constant desc", "数值相等"),
    ],
    # convert.py:960-1013 build_treasureclass() —— TreasureClassEx.txt
    "treasureclass": [
        ("name", "num", "TreasureClassEx.txt:Treasure Class", "数值相等"),
        ("group", "num", "TreasureClassEx.txt:group", "数值相等"),
        ("level", "num", "TreasureClassEx.txt:level", "数值相等"),
        ("picks", "num", "TreasureClassEx.txt:Picks", "数值相等"),
        ("nodrop", "num", "TreasureClassEx.txt:NoDrop", "数值相等"),
        ("drops", "formula", "TreasureClassEx.txt:Item1..Item10 / Prob1..Prob10", "公式求值相等"),
        ("next_tc", "formula", "Item1..Item10 中指向另一个 TC 的项", "公式求值相等"),
        ("unique", "num", "TreasureClassEx.txt:Unique", "数值相等"),
        ("set", "num", "TreasureClassEx.txt:Set", "数值相等"),
        ("rare", "num", "TreasureClassEx.txt:Rare", "数值相等"),
        ("magic", "num", "TreasureClassEx.txt:Magic", "数值相等"),
        ("is_act1_start", "formula", "是否 Act 1 闭包起点（引用闭包引用而来）", "公式求值相等"),
    ],
    # convert.py:1025-1090 build_missile() —— Missiles.txt
    "missile": [
        ("id", "none", "（本项目顺序编号）"),
        ("name", "none", "（中文显示名 cn_names.py）"),
        ("code", "num", "Missiles.txt:Missile", "数值相等"),
        ("official_id", "num", "Missiles.txt:Id", "数值相等"),
        ("speed", "num", "Missiles.txt:Vel", "数值相等"),
        ("max_vel", "num", "Missiles.txt:MaxVel", "数值相等"),
        ("accel", "num", "Missiles.txt:Accel", "数值相等"),
        ("radius", "num", "Missiles.txt:Size", "数值相等"),
        ("range", "num", "Missiles.txt:Range", "数值相等"),
        ("dmg_min", "num", "Missiles.txt:MinDamage", "数值相等"),
        ("dmg_max", "num", "Missiles.txt:MaxDamage", "数值相等"),
        ("elem_min", "num", "Missiles.txt:EMin", "数值相等"),
        ("elem_max", "num", "Missiles.txt:Emax", "数值相等"),
        ("dmg_type", "num", "Missiles.txt:EType", "数值相等"),
        ("light", "num", "Missiles.txt:Light", "数值相等"),
        ("cel_file", "num", "Missiles.txt:CelFile", "数值相等"),
        ("collide_type", "num", "Missiles.txt:CollideType", "数值相等"),
        ("next_hit", "num", "Missiles.txt:NextHit", "数值相等"),
        ("next_delay", "num", "Missiles.txt:NextDelay", "数值相等"),
        ("sub_missile", "num", "Missiles.txt:SubMissile1", "数值相等"),
        ("used_by", "formula", "引用来源（skills.txt:srvmissile/cltmissile + MonStats.txt:Miss*）", "公式求值相等"),
    ],
}

# 归一成 4 元组 (运行时列名, kind, 官方来源标签, 判据) —— 简写行按 kind 补默认判据
_CRIT_BY_KIND = {"num": "数值相等", "formula": "公式求值相等", "none": "—"}
FIELD_MAP = {lg: [(t[0], t[1], t[2],
                   (t[3] if len(t) > 3 else _CRIT_BY_KIND[t[1]])) for t in v]
             for lg, v in FIELD_MAP.items()}



def no_official_carrier_fields(logical):
    """官方 txt 里**没有**对应对手的列（比不出来，且不许写成 `一致`）。"""
    return [c for c, k, _, _ in FIELD_MAP[logical] if k == "none"]


# 明确登记：这三个字段**天然**没有官方对手（改这个集合 = 放宽判据，必须同时在
# tools/probes/README.md 与 .ai-tmp/test/s1/UNBLOCK.md 里说明）
EXPECTED_NO_CARRIER = {
    "class": {"id", "name", "skill_class"},
    "level": {"id", "name"},
    "monster": {"id", "name"},
    "skill": {"id", "name"},
    "item": {"id", "name"},
    "affix": {"id"},
    "monumod": {"id", "name", "ac_mul", "res_phys", "res_magic", "res_fire",
                "res_light", "res_cold", "res_poison"},
    "missile": {"id", "name"},
    "experience": set(),
    "treasureclass": set(),
}


# ─────────────────────────────────────────────────────────────────────────────
# 读取工具
# ─────────────────────────────────────────────────────────────────────────────
def find_repo_root(start=None):
    """向上找同时含 `client/Assets` 与 `策划/状态矩阵.tsv` 的那一层。"""
    p = os.path.abspath(start or os.path.dirname(os.path.abspath(__file__)))
    while True:
        if (os.path.isdir(os.path.join(p, "client", "Assets"))
                and os.path.isfile(os.path.join(p, MATRIX_REL))):
            return p
        nxt = os.path.dirname(p)
        if nxt == p:
            raise RuntimeError("找不到仓库根（需含 client/Assets 与 策划/状态矩阵.tsv）")
        p = nxt


def read_tsv_rows(path):
    """返回 (header, [cells...], first_data_lineno)。空行跳过；不做去引号等加工。"""
    with io.open(path, "r", encoding="utf-8", errors="replace") as f:
        raw = f.read()
    lines = raw.replace("\r\n", "\n").replace("\r", "\n").split("\n")
    rows = []
    first = None
    hdr = None
    for i, l in enumerate(lines, start=1):
        if l == "":
            continue
        if hdr is None:
            hdr = l.split("\t")
            continue
        if first is None:
            first = i
        rows.append(l.split("\t"))
    return hdr, rows, (first or 0)


def read_runtime_table(root, rt_name):
    return read_tsv_rows(os.path.join(root, RT_DIR_REL, rt_name + ".tsv"))


def read_source_table(root, logical):
    """读转写件 `策划/数值文档/<logical>_c.txt` —— 4 行表头（名/型/端/注释）后才是数据。"""
    path = os.path.join(root, DOC_DIR_REL, logical + "_c.txt")
    hdr, rows, first = read_tsv_rows(path)
    return hdr, rows[3:], first + 3


def read_matrix(root=None):
    """返回全部数据行的 dict 列表（含 行号 / 8 列）。"""
    root = root or find_repo_root()
    with io.open(os.path.join(root, MATRIX_REL), "r", encoding="utf-8", errors="replace") as f:
        lines = f.read().replace("\r\n", "\n").replace("\r", "\n").split("\n")
    out = []
    for i, l in enumerate(lines, start=1):
        if l == "" or l.startswith("#"):
            continue
        c = l.split("\t")
        if len(c) < 8 or c[0] == "维度":
            continue
        out.append({"line": i, "dim": c[0], "entity": c[1], "state": c[2],
                    "boundary": c[3], "expect": c[4], "measured": c[5],
                    "verdict": c[6], "evidence": c[7]})
    return out


def s1_rows(root=None):
    """状态矩阵里 S1数值 维的全部数据行。"""
    return [r for r in read_matrix(root) if r["dim"] == "S1数值"]


def s1_empty_rows(root=None):
    """S1数值 维里 `实测` 为空的那些行（= 闸门 coverage-filled 在报红的行）。"""
    return [r for r in s1_rows(root) if r["measured"].strip() == ""]


def entity_table(logical):
    for lg, rt, _ in TABLES:
        if lg == logical:
            return rt
    raise KeyError(logical)


def logical_of_matrix_entity(entity):
    """`tbl:Affix(301 id)` → `affix`（不认识的实体返回 None）。"""
    if entity.startswith("tbl:"):
        name = entity[4:].split("(")[0].strip()
        return LOGICAL_BY_ENTITY_TAG.get(name)
    return None


# ─────────────────────────────────────────────────────────────────────────────
# 官方载体
# ─────────────────────────────────────────────────────────────────────────────
# 官方 txt 的**项目内默认落点**（与 convert.py:89-90 的 DEFAULT_SRC_DIR 逐字一致；
# ⛔ 该目录在 .gitignore 里 ⇒ 干净检出必然没有，只有用户放进来才有）
DEFAULT_SRC_REL = os.path.join("原版资源", "参考工程_Diablerie", "d2lod1.10txt",
                               "data", "global", "excel")


def resolve_official_src(src, root=None):
    """把 `--src` 归一成官方 txt 目录。

    返回 dict(kind, excel_dir, raw, msg)：
      kind = "txt"     官方 txt 目录已定位（excel_dir 指向含 *.txt 的那一层）
           = "missing" 没给 / 给了但不存在的路径
           = "mpq"     给的是 `.mpq`（或目录里只有 *.mpq）—— 本仓**没有** mpq 解包器，
                        绝不假装能读；调用方负责把解包指引打给用户。
    """
    if not src:
        # 没给 --src / 环境变量 ⇒ 试项目内默认落点（convert.py 的 DEFAULT_SRC_DIR）。
        # 命中 ⇒ 用户把官方 txt 放到默认位置就够了，一条命令都不用带参数。
        if root:
            d = os.path.join(root, DEFAULT_SRC_REL)
            if os.path.isfile(os.path.join(d, ALL_OFFICIAL_FILES[0])) or (
                    os.path.isdir(d) and any(f.lower().endswith(".txt") for f in os.listdir(d))):
                return {"kind": "txt", "excel_dir": d, "raw": d, "msg": d}
            return {"kind": "missing", "excel_dir": None, "raw": d,
                    "msg": ("未给 --src（也无环境变量 D2SRC_DIR），项目内默认落点也不存在：%s" % d)}
        return {"kind": "missing", "excel_dir": None, "raw": None,
                "msg": "未给 --src（也没有环境变量 D2SRC_DIR）—— 没有官方载体可比"}
    raw = os.path.abspath(os.path.expanduser(src))
    if raw.lower().endswith(".mpq"):
        return {"kind": "mpq", "excel_dir": None, "raw": raw, "msg": raw}
    if not os.path.exists(raw):
        return {"kind": "missing", "excel_dir": None, "raw": raw,
                "msg": "路径不存在：%s" % raw}
    if os.path.isdir(raw):
        for sub in EXCEL_SUBDIRS:                      # …/data/global/excel
            cand = os.path.join(raw, sub)
            if os.path.isdir(cand):
                return {"kind": "txt", "excel_dir": cand, "raw": raw, "msg": cand}
        if any(f.lower().endswith(".txt") for f in os.listdir(raw)):
            return {"kind": "txt", "excel_dir": raw, "raw": raw, "msg": raw}
        mpqs = [f for f in os.listdir(raw) if f.lower().endswith(".mpq")]
        if mpqs:
            return {"kind": "mpq", "excel_dir": None, "raw": raw,
                    "msg": "目录 %s 里只有 mpq：%s" % (raw, ", ".join(sorted(mpqs)))}
    return {"kind": "missing", "excel_dir": None, "raw": raw,
            "msg": "目录里既没有 data/global/excel/*.txt，也没有 *.txt：%s" % raw}


def probe_official_dir(excel_dir):
    """逐个官方 txt 点名；返回 (present, missing)。"""
    present, missing = [], []
    for f in ALL_OFFICIAL_FILES:
        (present if os.path.isfile(os.path.join(excel_dir, f)) else missing).append(f)
    return present, missing


MPQ_HELP_LINES = [
    "检测到 MPQ —— ⛔ 本仓没有 mpq 解包器（tools/d2codec/ 只覆盖 dc6/dcc/dt1/ds1/tbl/pl2/cof，",
    "没有任何读 MPQ 容器的代码），所以**不会**假装能读。必须先解包成官方 txt，比对器才认。",
    "",
    "解包目标（务必落成这个形状，比对器两处都认）：",
    "    <解包输出根>/data/global/excel/<17 个官方 txt>",
    "",
    "用你手上任一款 MPQ 解包器（本仓不提供、也无法在离线环境验证其命令行参数，故不在此处编造参数，",
    "请以该工具的 -h/--help 为准）把 MPQ 里的 data/global/excel/ 整目录解出来；",
    "常见可用的有：Ladik 的 MPQ Editor、StormLib 系工具、Diablo II 模组工具链自带的 mpq 解包器。",
    "需要的 17 个文件：",
    "    " + "  ".join(ALL_OFFICIAL_FILES[:6]),
    "    " + "  ".join(ALL_OFFICIAL_FILES[6:12]),
    "    " + "  ".join(ALL_OFFICIAL_FILES[12:]),
    "",
    "解包完成后，先点一次名（不比对，只报还缺哪些文件）：",
    '    python tools/probes/measure/s1_value_diff.py --src "<解包输出根>" --probe',
    "17 个文件齐了就直接跑全量：",
    '    python tools/probes/measure/s1_value_diff.py --src "<解包输出根>" --gate-artifacts',
    "",
    "另一种等价走法（不解包，直接拿别人给的同版本 txt）：把 17 个 txt 放进",
    "    <项目根>/原版资源/参考工程_Diablerie/d2lod1.10txt/data/global/excel/",
    "放好后**不带参数**跑同一条命令即可（比对器会认这个项目内默认落点）：",
    "    python tools/probes/measure/s1_value_diff.py --gate-artifacts",
]


# ─────────────────────────────────────────────────────────────────────────────
# convert.py 桥（官方 txt → 期望值）
# ─────────────────────────────────────────────────────────────────────────────
class ConverterError(RuntimeError):
    pass


def load_converter(root=None):
    """import tools/table-convert/convert.py 成模块（不调用它的 main，不会写任何文件）。"""
    root = root or find_repo_root()
    conv_path = os.path.join(root, CONVERTER_REL)
    if not os.path.isfile(conv_path):
        raise ConverterError("找不到转换器：%s" % conv_path)
    conv_dir = os.path.dirname(conv_path)
    if conv_dir not in sys.path:
        sys.path.insert(0, conv_dir)
    import importlib.util
    spec = importlib.util.spec_from_file_location("d2_convert_bridge", conv_path)
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    return mod


def expected_for_table(mod, logical, excel_dir):
    """跑 convert.py 对应 build_*，返回 (cols, rows_of_pystr, error_or_None)。

    ⛔ 不写盘：只调 build_*（它们只往内存 SHEETS 里塞），绝不调 main()/dump()。
    """
    fn_name = dict((lg, fn) for lg, _, fn in TABLES)[logical]
    missing = [f for f in TABLE_DEPS[logical]
               if not os.path.isfile(os.path.join(excel_dir, f))]
    if missing:
        return None, None, "缺官方 txt：" + ", ".join(missing)
    mod.SRC_DIR = excel_dir
    mod._CACHE.clear()
    mod.SHEETS.clear()
    fn = getattr(mod, fn_name)
    import contextlib
    buf = io.StringIO()
    try:
        with contextlib.redirect_stdout(buf):
            sh = fn()
    except FileNotFoundError as e:                     # 官方 txt 半到位
        return None, None, "读官方 txt 失败：%s" % e
    except Exception as e:                             # noqa: BLE001
        return None, None, "convert.py %s 抛异常：%s: %s" % (fn_name, type(e).__name__, e)
    cols = [c[0] for c in sh.cols]
    rows = [tuple(mod.fmt(v) for v in r) for r in sh.rows]
    return cols, rows, None


# ─────────────────────────────────────────────────────────────────────────────
def fmt_diff(our, exp):
    """差值：相等 ⇒ '0'；两边都是数字 ⇒ 带符号差；否则 ⇒ '≠'。"""
    if our == exp:
        return "0"
    try:
        a, b = float(our), float(exp)
    except (TypeError, ValueError):
        return "≠"
    d = a - b
    if d == int(d):
        return "%+d" % int(d)
    return "%+g" % d
