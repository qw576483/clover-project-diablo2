// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · Module/Combat/DamageFormula.cs
// **官方 D2 伤害 / 命中 / 抗性 / 致命一击公式**的唯一定义处（纯函数，无状态、无 Unity 依赖）。
//
// ⛔ 数值来源：**一律取配表**（`monster_c` / `skill_c` / `missile_c` / `monumod_c`），
//    本文件只放**公式**与**官方常量**（每条都能指到出处），不放任何"某个怪的数值"。
//
// ═════════════════════════════════════════════════════════════════════════════
// 出处表（片 6「战斗伤害 + 怪物数值 1:1」重写；每条都能指到 `文件:行`）
// ═════════════════════════════════════════════════════════════════════════════
// 权威表（LoD 1.10 官方 txt，随参考工程一起落盘）：
//   `<根>/原版资源/参考工程_Diablerie/d2lod1.10txt/data/global/excel/*.txt`
// 参考实现（社区复刻；本片只把它当"别人怎么复刻"的依据，逐行核过）：
//   `<根>/原版资源/参考工程_Diablerie/libd2/packages/game/src/{combat,spell,calc,skills_amazon,montable,monai}.zig`
//
// ① 命中率（Chance to Hit）
//    官方引擎函数 = `DAMAGE_RollAttackHit @0057d9b0`；参考实现逐行移植于
//    `libd2/packages/game/src/combat.zig:72-108`（函数 `chanceToHit`，注释里给了 RVA 与整数口径）：
//      负数交叉：def<0 ⇒ ar-=def, def=0；ar<0 ⇒ def-=ar, ar=0；def<0 ⇒ def=0
//      pct    = (def+ar==0) ? 100 : ar*100/(def+ar)        ← **整数截断**
//      chance = (alvl+dlvl==0) ? pct : pct*2*alvl/(alvl+dlvl)  ← **整数截断**
//      夹取：chance<6 ⇒ 5；chance>94 ⇒ 95（即 5%~95%）
//    ⚠️ 与旧版（本项目把浮点式 `200×ALvl/(ALvl+DLvl)×AR/(AR+DR)` 算完再夹）**不是同一个数**：
//      旧版取整发生在最后（浮点），官方在中间两次整数截断。本片改成官方口径。
//      （实机可复现的例子：ALvl=1 DLvl=10 AR=DR=100 ⇒ 官方 9%（100/11 截断），旧版 9.09%）
//
// ② 物理伤害
//    官方引擎函数 = `DAMAGE_CalculatePhysicalDamage @0057b420`；参考实现
//    `combat.zig:149-180`（`rollPhysicalDamage`）：
//      base   = 武器伤害（无武器则用 mindamage/maxdamage 两个属性）
//      倍率百比 = 技能 param3(ED%) + damagepercent + str*StrBonus/100 + dex*DexBonus/100  ← **整数截断**
//      输出   = base + base*倍率百比/100               ← 官方 `D2ApplyPercent`：**截断，不四舍五入**
//    `StrBonus`/`DexBonus` 是**每把武器各自的值**，来自官方 `Weapons.txt` 的 `StrBonus`/`DexBonus` 列。
//    ⛔【例外 E25】本项目 `item_c` **没有** `StrBonus`/`DexBonus` 列（配表列名契约冻结，本片无权加列）
//      ⇒ 近战一律按官方 `Weapons.txt` 里**绝大多数近战行的值 100** 计（= 每点力量 +1%，与旧注释同义）；
//      弓/弩（官方 `StrBonus=0 / DexBonus=100`）会被算成"按力量加成"⇒ 已登记，消除条件见验收表 E25。
//
// ③ 元素抗性
//    官方引擎函数 = `DAMAGE_ApplyElementalDamageWithResist @0057bf80`；参考实现
//    `spell.zig:252-264`（`applyResist`）：
//      resist >= 100 ⇒ **免疫，返回 0**
//      否则          ⇒ trunc(raw × (100 - resist) / 100)（D2ApplyPercent 截断；负抗性 ⇒ 加伤）
//    ⛔ **怪物抗性没有 75% 上限**：`combat.zig:299-313`（`applyPhysicalFor`）的原文是
//      "players cap at 50 … **MONSTERS have NO cap** (DAMAGE_CalculateResistance applies the 0x32 cap
//      only when !bDefenderIsMonster)"。旧版对**所有**目标都夹 75% ⇒ 怪物免疫（>=100）被抹成 75%
//      ⇒ 本片修掉。玩家侧的元素抗性上限（75%）在 `Module/Player/PlayerStats.MaxResist`（值相同），
//      与"结算函数不夹"这条官方口径并不冲突。
//
// ④ 防御（DR）**不减伤**——官方 D2 中 DR 只出现在①的命中公式里；
//    "防御减伤"的实现形式就是**未命中 ⇒ 本次实际伤害为 0**（`CombatModule` 走这条）。
//
// ⑤ 致命一击（Critical Strike / 双倍物理伤害）
//    官方 `SkillCalc.txt`（LoD 1.10，`d2lod1.10txt/.../excel/SkillCalc.txt`）第 4 行：
//      `dm12   ((110*lvl) * (b-a))/(100 * (lvl+6)) + a`
//    亚马逊「Critical Strike」行的 `passivecalc1 = dm12`，`Param1 = 5`、`Param2 = 80`
//    （`d2lod1.10txt/.../excel/skills.txt` 的 Critical Strike 行；1.09 经典版同一行也是 `5 / 80`，
//      见 `<根>/原版资源/d2raw/data/global/excel/skills.txt`）。
//    整数口径（引擎 `SKILLS_CalcDiminishingReturns @00645b20`）：
//      `libd2/packages/game/src/calc.zig:33-36`：`divTrunc(divTrunc(110*level, level+6)*(b-a),100)+a`，
//      且结果 **上限 = b**（不是 75）。
//    参考实现的用例（`libd2/.../skills_amazon.zig:123-131`）：1 级=16%、5 级=42%、20 级=68%。
//    ⛔ 旧版 `25% + 3%×(slvl-1)`、上限 75% 是**本项目凭空定的近似**（且注释里等的"官方 SkillCalc.txt"
//      其实**一直就在磁盘上**，在参考工程那份 1.10 txt 里）⇒ 本片换成官方 dm12。
//    ⛔ 注意：新版**没有 75% 上限**（官方只夹到 Param2=80）。
//
// ⑥ 技能伤害的等级缩放（`SkillDamageRange`）
//    官方 `SKILLS_GetDamage` 的分段式（参考实现 `spell.zig:88-116` `staged`）：
//      `a = EMin + EMinLev1×(l-1)`（8/16/22/28 级换档），`伤害 = a << HitShift >> 8`。
//    ⛔【例外 E26】本项目 `skill_c` 有 `phys_min/phys_max/elem_min/elem_max/hit_shift`（= 1 级值），
//      **没有** `EMinLev1..5` / `MinLevDam1..5` 这些**每级增量**列 ⇒ 无法复现官方分段式。
//      本函数仍是"1 级值 × 技能等级"的线性式（**本项目新增**），已登记 E26（含消除条件）。
//
// ⑦ 技能法力消耗：官方 `mana + lvlmana × (slvl-1)`（`skills.txt` 的 `mana`/`lvlmana`；`skill_c.mana_cost/mana_per_lvl`）。
//
// ⛔ 随机一律走注入的 `CloverEngine.Rng`（可复现），**禁止 UnityEngine.Random**。
// ─────────────────────────────────────────────────────────────────────────────

using CloverEngine;
using Diablo2.Core;
using Diablo2.Def;
using UnityEngine;

namespace Diablo2.Module.Combat
{
    /// <summary>官方 D2 的伤害 / 命中 / 抗性 / 致命一击公式（纯函数，出处见文件头）。</summary>
    public static class DamageFormula
    {
        // ── 命中率上下限（官方 @0057d9b0；参考实现 `combat.zig:102-107`）──────────
        /// <summary>命中率下限（官方 5%）。</summary>
        public const float MinHitChance = GameConst.MinHitChance;

        /// <summary>命中率上限（官方 95%）。</summary>
        public const float MaxHitChance = GameConst.MaxHitChance;

        /// <summary>官方夹取阈值：算出的整数命中率 &lt; 6 ⇒ 用 5（`combat.zig:102-103`）。</summary>
        public const int HitChanceClampLowThreshold = 6;

        /// <summary>官方夹取阈值：算出的整数命中率 &gt; 94 ⇒ 用 95（`combat.zig:104-105`）。</summary>
        public const int HitChanceClampHighThreshold = 94;

        // ── 抗性（官方 @0057bf80 / `spell.zig:252-264`）─────────────────────────
        /// <summary>官方免疫阈值：抗性 ≥ 100 ⇒ 该系伤害为 0。</summary>
        public const int ResistImmuneThreshold = 100;

        /// <summary>
        /// 近战武器的官方 `Weapons.txt::StrBonus`（每点力量 +1% 武器伤害）。
        /// <para>出处：官方 `Weapons.txt` 的 `StrBonus` 列（近战行绝大多数为 100；弓/弩为 0 且 `DexBonus=100`）。
        /// 本项目 `item_c` 无该列（配表列名契约冻结）⇒ 见文件头【例外 E25】。调用方要算弓/弩时应传**敏捷**。</para>
        /// </summary>
        public const int MeleeStrBonus = 100;

        // ── 空手伤害（官方：未装备武器时 1~2 点）───────────────────────────────
        /// <summary>空手伤害下限（官方 fists）。</summary>
        public const int UnarmedMinDamage = 1;

        /// <summary>空手伤害上限（官方 fists）。</summary>
        public const int UnarmedMaxDamage = 2;

        // ── 致命一击（官方 SkillCalc `dm12` + skills.txt Param1/Param2）──────────
        /// <summary>官方 `skills.txt`「Critical Strike」行的 `Param1`（dm12 的 a = 最小增益，%）。</summary>
        public const int CritParam1 = 5;

        /// <summary>官方 `skills.txt`「Critical Strike」行的 `Param2`（dm12 的 b = 渐近上限，%）。</summary>
        public const int CritParam2 = 80;

        // ═════════════════════════════════════════════════════════════════════
        // ① 命中率
        // ═════════════════════════════════════════════════════════════════════
        /// <summary>
        /// 官方命中率（返回 0~1 的比例，**已按官方整数口径算完并夹在 5%~95%**）。
        /// <para>出处：`DAMAGE_RollAttackHit @0057d9b0`；参考实现 `combat.zig:83-108`（含负数交叉与两次整数截断）。</para>
        /// </summary>
        /// <param name="attackerLevel">攻击者等级 ALvl（≥1，非法时告警但**不**改值——官方对 0 有专门分支）。</param>
        /// <param name="defenderLevel">防御者等级 DLvl（≥1）。</param>
        /// <param name="attackRating">攻击者命中 AR（可为负：官方会把它折到防御上）。</param>
        /// <param name="defense">防御者防御 DR（可为负：同上）。</param>
        public static float HitChance(int attackerLevel, int defenderLevel, int attackRating, int defense)
        {
            if (attackerLevel < 1)
            {
                CombatLog.WarnThrottled("hit.alvl", $"HitChance: 攻击者等级 {attackerLevel} < 1（数据异常）⇒ 按官方口径继续算（等级 0 只影响等级项）");
            }
            if (defenderLevel < 1)
            {
                CombatLog.WarnThrottled("hit.dlvl", $"HitChance: 防御者等级 {defenderLevel} < 1（数据异常）⇒ 按官方口径继续算");
            }

            // 官方：负值交叉（`combat.zig:86-94`）——不是"按 0 处理"，是把负值折到对面再置 0
            var ar = attackRating;
            var dr = defense;
            if (dr < 0)
            {
                ar -= dr;
                dr = 0;
                CombatLog.WarnThrottled("hit.dr.neg", $"HitChance: DR={defense} < 0 ⇒ 官方口径把它折到 AR 上（AR {attackRating} → {ar}）");
            }
            if (ar < 0)
            {
                dr -= ar;
                ar = 0;
                CombatLog.WarnThrottled("hit.ar.neg", $"HitChance: AR={attackRating} < 0 ⇒ 官方口径把它折到 DR 上（DR {defense} → {dr}）");
            }
            if (dr < 0) dr = 0;

            var denomAr = dr + ar;
            var pct = denomAr == 0 ? 100 : ar * 100 / denomAr;        // 整数截断（官方 D2ApplyPercent）

            var denomLvl = attackerLevel + defenderLevel;
            var chance = denomLvl == 0 ? pct : pct * 2 * attackerLevel / denomLvl;

            if (chance < HitChanceClampLowThreshold) chance = (int)(MinHitChance * 100f);
            else if (chance > HitChanceClampHighThreshold) chance = (int)(MaxHitChance * 100f);

            return chance / 100f;
        }

        // ═════════════════════════════════════════════════════════════════════
        // ② 物理伤害
        // ═════════════════════════════════════════════════════════════════════
        /// <summary>
        /// 掷一次武器伤害：`[min, max]`（含两端）。区间非法（max &lt; min 或两者 ≤ 0）返回 0 并限频告警。
        /// <para>数值来源：`item_c.dmg_min/dmg_max` → `ItemStack.dmgMin/dmgMax`（官方 `Weapons.txt`）。</para>
        /// </summary>
        public static int RollWeaponDamage(int min, int max, Rng rng)
        {
            if (min < 0) min = 0;
            if (max < min)
            {
                CombatLog.WarnThrottled("dmg.weapon.range", $"RollWeaponDamage: 区间非法 min={min} max={max} ⇒ 返回 0");
                return 0;
            }
            if (max == min) return min;
            if (rng == null)
            {
                CombatLog.WarnThrottled("dmg.weapon.norng", "RollWeaponDamage: rng 为 null（不可复现）⇒ 取下限");
                return min;
            }
            return rng.Next(min, max + 1);      // Next 的上界是开区间 ⇒ +1 含 max
        }

        /// <summary>
        /// 官方物理伤害：`武器伤害 + 武器伤害 × 倍率百比 / 100`，
        /// 倍率百比 = `力量 × StrBonus / 100 + 敏捷 × DexBonus / 100 + 技能 ED%`
        /// （出处：`DAMAGE_CalculatePhysicalDamage @0057b420`；参考实现 `combat.zig:149-180`）。
        /// <para>**整数截断**（官方 `D2ApplyPercent`），不是四舍五入。</para>
        /// <para>
        /// ★ **片 14 消除【例外 E25】**：旧签名只有 `attribute` 一个属性、内部写死 `MeleeStrBonus = 100`
        /// ⇒ 弓/弩（官方 `StrBonus=0 / DexBonus=100`）会被错误地按**力量**加成算。现在
        /// **逐武器**取配表 `item_c.str_bonus` / `dex_bonus`（打表来源 = 官方 `Weapons.txt` 的
        /// `StrBonus`/`DexBonus` 两列），两个属性都参与。
        /// </para>
        /// </summary>
        /// <param name="weaponRoll">已掷出的武器伤害（`RollWeaponDamage` 的结果）。</param>
        /// <param name="str">力量。</param>
        /// <param name="dex">敏捷。</param>
        /// <param name="strBonus">该武器的 `str_bonus`（近战多为 100，弓/弩为 0）。</param>
        /// <param name="dexBonus">该武器的 `dex_bonus`（弓/弩为 100，近战多为 0）。</param>
        /// <param name="skillMultiplier">技能倍率（普通攻击 = 1）；按官方口径折算成 `ED%` 并入倍率百比。</param>
        public static int PhysicalDamage(int weaponRoll, int str, int dex, int strBonus, int dexBonus,
                                         float skillMultiplier)
        {
            if (weaponRoll <= 0) return 0;
            if (skillMultiplier <= 0f)
            {
                CombatLog.WarnThrottled("dmg.skillmul", $"PhysicalDamage: 技能倍率 {skillMultiplier} ≤ 0（异常）⇒ 按 1 计算");
                skillMultiplier = 1f;
            }

            // 官方：dmg_pct = str*StrBonus/100 + dex*DexBonus/100 + 技能 ED%（**每项各自整数截断**）
            var dmgPct = str * strBonus / 100 + dex * dexBonus / 100;
            var skillPct = Mathf.RoundToInt((skillMultiplier - 1f) * 100f);
            dmgPct += skillPct;

            // 官方：out = base + base*dmg_pct/100（D2ApplyPercent ⇒ 截断）
            var result = weaponRoll + weaponRoll * dmgPct / 100;
            return result < 0 ? 0 : result;
        }

        // ═════════════════════════════════════════════════════════════════════
        // ③ 抗性 / 元素
        // ═════════════════════════════════════════════════════════════════════
        /// <summary>
        /// 通用抗性结算（官方口径）：`抗性 ≥ 100 ⇒ 0`（免疫）；否则 `trunc(伤害 × (100 - 抗性) / 100)`。
        /// <para>
        /// 出处：`DAMAGE_ApplyElementalDamageWithResist @0057bf80`；参考实现 `spell.zig:252-264`（`applyResist`）。
        /// ⛔ **不夹 75% 上限**：官方对**怪物**不设上限（`combat.zig:299-313`）；
        /// 玩家侧的元素抗性上限在 `PlayerStats.MaxResist`（= 75）里**先夹好再传进来**。
        /// </para>
        /// <para>抗性为负 ⇒ 伤害更高（官方如此）；结果不小于 0。</para>
        /// </summary>
        public static int ApplyResist(int raw, int resistPercent)
        {
            if (raw <= 0) return 0;
            if (resistPercent >= ResistImmuneThreshold) return 0;      // 官方：免疫

            var v = raw * (100 - resistPercent) / 100;                 // 整数截断（D2ApplyPercent）
            return Mathf.Max(0, v);
        }

        /// <summary>某系抗性的配表取值（`monster_c.res_*`；<see cref="DamageType"/> 没有魔法系）。</summary>
        public static int MonsterResist(Table.BaseMonsterRow row, DamageType type)
        {
            if (row == null) return 0;
            switch (type)
            {
                case DamageType.Physical: return row.ResPhys;
                case DamageType.Fire: return row.ResFire;
                case DamageType.Cold: return row.ResCold;
                case DamageType.Lightning: return row.ResLight;
                case DamageType.Poison: return row.ResPoison;
                default:
                    CombatLog.WarnThrottled("res.unmapped", $"MonsterResist: 未登记的伤害类型 {type} ⇒ 按 0 抗性");
                    return 0;
            }
        }

        /// <summary>把 `skill_c.dmg_type`（官方 `EType`）映射到本项目枚举。</summary>
        public static DamageType DamageTypeOf(string etype)
        {
            if (string.IsNullOrEmpty(etype)) return DamageType.Physical;
            switch (etype.Trim().ToLowerInvariant())
            {
                case "fire": return DamageType.Fire;
                case "cold": return DamageType.Cold;
                case "ltng": return DamageType.Lightning;
                case "pois": return DamageType.Poison;
                case "phys": return DamageType.Physical;
                case "mag":
                    // ★ 片 16（**消除【例外 E27】**）：官方 `EType=mag` 是「魔法」伤害，走**独立抗性**
                    //   `ResMa`（配表 `monster_c.res_magic`，早已导出）。现在 `Def.DamageType` 有第 6 系
                    //   `Magic` ⇒ 不再降级成物理通道（旧实现的限频 Warn + 按物理结算已删）。
                    return DamageType.Magic;
                default:
                    CombatLog.WarnThrottled("dmgtype.unknown", $"DamageTypeOf: 未登记的 EType「{etype}」⇒ 按物理结算");
                    return DamageType.Physical;
            }
        }

        // ═════════════════════════════════════════════════════════════════════
        // ⑤ 致命一击
        // ═════════════════════════════════════════════════════════════════════
        /// <summary>
        /// 官方 dm 曲线（渐减，`SKILLS_CalcDiminishingReturns @00645b20`；参考实现 `calc.zig:33-36`）：
        /// `r = trunc(trunc(110×lvl, lvl+6) × (b-a) / 100) + a`，且 **上限 = b**。
        /// <para>出处链：`SkillCalc.txt` 的 `dm12` 行 + `skills.txt` 的 Param1/Param2 + `calc.zig:33-36`；用例见 `skills_amazon.zig:123-131`。</para>
        /// </summary>
        public static int Diminishing(int level, int a, int b)
        {
            if (level <= 0) return a;
            var r = (110 * level / (level + 6)) * (b - a) / 100 + a;
            return b >= r ? r : b;
        }

        /// <summary>
        /// 致命一击成功率（0~1）。官方 = 亚马逊「Critical Strike」的 `passivecalc1 = dm12`，`Param1=5`、`Param2=80`
        /// ⇒ **整数 `dm12`**（1 级 16% / 5 级 42% / 20 级 68%，参考实现用例实测值）。
        /// <para>未学（slvl ≤ 0）⇒ 0。**没有 75% 上限**（官方只夹到 Param2 = 80%）。</para>
        /// </summary>
        public static float CriticalStrikeChance(int skillLevel)
        {
            if (skillLevel <= 0) return 0f;
            return Diminishing(skillLevel, CritParam1, CritParam2) / 100f;
        }

        // ═════════════════════════════════════════════════════════════════════
        // 技能伤害（配表驱动）
        // ═════════════════════════════════════════════════════════════════════
        /// <summary>
        /// 官方的**每级伤害分段式**（引擎 `SKILLS_GetDamage`；参考实现逐行移植见
        /// `libd2/packages/game/src/spell.zig:88-116` 的 `staged`）：
        /// <code>
        /// a = base
        /// if (l &gt; 28) { a += lev5*(l-28); l = 28; }
        /// if (l &gt; 22) { a += lev4*(l-22); l = 22; }
        /// if (l &gt; 16) { a += lev3*(l-16); l = 16; }
        /// if (l &gt;  8) { a += lev2*(l- 8); l =  8; }
        /// a += lev1*(max(0,l)-1)
        /// return a &lt;&lt; hitShift;          // 1/256 定点；调用方再 &gt;&gt; 8
        /// </code>
        /// <para>
        /// 段位语义：`lev1` = **1~8 级**每级增量、`lev2` = 9~16、`lev3` = 17~22、`lev4` = 23~28、
        /// `lev5` = 29+；`base` = 官方 `MinDam`/`EMin`（= **1 级**值，因为 l=1 时 `lev1×(l-1) = 0`）。
        /// **负数按 1 级**处理（官方 `@max(clvl,1)`：主动技能至少 1 级）。
        /// </para>
        /// </summary>
        private static long Staged(int skillLevel, int baseValue, int lev1, int lev2, int lev3,
                                   int lev4, int lev5, int hitShift)
        {
            var l = skillLevel < 1 ? 1 : skillLevel;
            long a = baseValue;
            if (l > 28) { a += (long)lev5 * (l - 28); l = 28; }
            if (l > 22) { a += (long)lev4 * (l - 22); l = 22; }
            if (l > 16) { a += (long)lev3 * (l - 16); l = 16; }
            if (l > 8) { a += (long)lev2 * (l - 8); l = 8; }
            a += (long)lev1 * (l - 1);
            return a << hitShift;
        }

        /// <summary>
        /// 技能在**当前等级**的伤害区间 —— 官方口径（`SKILLS_GetDamage`）：**物理与元素各自**走
        /// <see cref="Staged"/> 分段、各自 `&gt;&gt; 8`（**整数截断**），最后相加。
        /// <para>
        /// ★ 本轮（片 12）**消除了【例外 E26】**：旧实现是"本项目新增的线性式" `dmg_min × slvl`
        /// （把 1 级值当每级增量用），现已换成官方分段式 —— 配表 `skill_c` 也补齐了
        /// `phys_min_lev1..5` / `phys_max_lev1..5` / `elem_min_lev1..5` / `elem_max_lev1..5` 共 **20 列**
        /// （官方 `MinLevDam1..5` / `MaxLevDam1..5` / `EMinLev1..5` / `EMaxLev1..5`，**未换算的原值**）。
        /// </para>
        /// <para>
        /// 取值示例（火弹 `EMin=6 EMax=12 EMinLev1=3 EMaxLev1=3 HitShift=7`，输出 = `a × 128 / 256` 截断）：
        /// 1 级 `3~6`、2 级 `4~7`、3 级 `6~9`（旧线性式会给 1 级 3~6、2 级 6~12、3 级 9~18 ⇒ **偏高一倍**）。
        /// </para>
        /// </summary>
        public static void SkillDamageRange(Table.BaseSkillRow row, int skillLevel, out int min, out int max)
        {
            min = 0;
            max = 0;
            if (row == null || skillLevel <= 0) return;

            var physMin = Staged(skillLevel, row.PhysMin, row.PhysMinLev1, row.PhysMinLev2,
                                 row.PhysMinLev3, row.PhysMinLev4, row.PhysMinLev5, row.HitShift) >> 8;
            var physMax = Staged(skillLevel, row.PhysMax, row.PhysMaxLev1, row.PhysMaxLev2,
                                 row.PhysMaxLev3, row.PhysMaxLev4, row.PhysMaxLev5, row.HitShift) >> 8;
            var elemMin = Staged(skillLevel, row.ElemMin, row.ElemMinLev1, row.ElemMinLev2,
                                 row.ElemMinLev3, row.ElemMinLev4, row.ElemMinLev5, row.HitShift) >> 8;
            var elemMax = Staged(skillLevel, row.ElemMax, row.ElemMaxLev1, row.ElemMaxLev2,
                                 row.ElemMaxLev3, row.ElemMaxLev4, row.ElemMaxLev5, row.HitShift) >> 8;

            min = (int)(physMin + elemMin);
            max = (int)(physMax + elemMax);
            if (min < 0) min = 0;
            if (max < min) max = min;
        }

        /// <summary>
        /// 技能在**当前等级**的法力消耗：官方 `mana + lvlmana × (slvl-1)`（`skill_c.mana_cost/mana_per_lvl`）。
        /// 结果不小于 0（官方 `lvlmana` 可为负，如「魔法箭」每级 -1）。
        /// </summary>
        public static int SkillManaCost(Table.BaseSkillRow row, int skillLevel)
        {
            if (row == null || skillLevel <= 0) return 0;
            var cost = row.ManaCost + row.ManaPerLvl * (skillLevel - 1);
            return cost < 0 ? 0 : cost;
        }
    }
}
