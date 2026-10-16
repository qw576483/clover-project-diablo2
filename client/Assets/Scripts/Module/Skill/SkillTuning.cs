// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · Module/Skill/SkillTuning.cs
// 技能/投射物的**节奏与单位换算常量**（**本项目新增**）。
//
// 边界：技能的法力/伤害/需求等级/前置/投射物速度与射程**全部来自配表**
// （`skill_c` / `missile_c`），本文件只放"配表单位 → 本项目世界单位"的换算系数
// 与"没有配表列"的节奏参数，每一条都标了理由，将来校准只改这里。
//
// 单位换算为什么要系数：官方 `Missiles.txt` 的 `Vel`/`Range` 用的是引擎内部单位
//    （子格/帧级量纲），本项目 `Core/GameConst.TileSize=1` 世界单位 = 1 格、速度以"格/秒"计。
//    官方未公开精确换算 ⇒ 用**可校准系数**近似，并保证"火弹约 1~2 秒飞越一屏"的手感。
// ─────────────────────────────────────────────────────────────────────────────

namespace Diablo2.Module.Skill
{
    /// <summary>技能与投射物的节奏/换算常量（**本项目新增**）。</summary>
    internal static class SkillTuning
    {
        // ── 投射物（配表 `missile_c` 单位 → 本项目单位）─────────────────────────
        /// <summary>【本项目新增】`missile_c.speed`（官方 `Vel`）→ 格/秒 的换算系数。</summary>
        public const float SpeedToTilesPerSecond = 0.50f;

        /// <summary>【本项目新增】`missile_c.range`（官方 `Range`）→ 格 的换算系数。</summary>
        public const float RangeToTiles = 0.40f;

        /// <summary>【本项目新增】`missile_c.radius`（官方 `Size`）→ 格 的换算系数。</summary>
        public const float RadiusToTiles = 0.80f;

        /// <summary>【本项目新增】配表缺失时的投射物速度兜底（格/秒）。</summary>
        public const float FallbackSpeed = 10f;

        /// <summary>【本项目新增】配表缺失时的射程兜底（格）。</summary>
        public const float FallbackRange = 20f;

        /// <summary>【本项目新增】怪物"身体半径"（格）——用来把投射物命中判定放宽到"擦到就算"。</summary>
        public const float MonsterBodyRadius = 0.35f;

        /// <summary>【本项目新增】投射物出生点离施法者的位移（格）——别一出生就命中自己脚下的怪。</summary>
        public const float SpawnOffset = 0.50f;

        // ── 冷却/施法节奏（官方由 `AnimData` 的攻击帧驱动，本项目无该列）────────
        /// <summary>【本项目新增】`skill_c` 无 delay 列（官方在 `skills.txt` 的 `delay` 字段）⇒
        /// 面向地面的施法（投射物/AoE）用该默认冷却（秒）。</summary>
        public const float DefaultGroundCooldownSeconds = 0.40f;

        /// <summary>【本项目新增】面向敌方/自身的即时技能默认冷却（秒）。</summary>
        public const float DefaultInstantCooldownSeconds = 0.25f;

        /// <summary>【本项目新增】`skill_c` 无 delay 列时的兜底"出手间隔"上界（秒）——
        /// 防止把某个技能写成 0 冷却导致每帧刷法术。</summary>
        public const float MinCooldownSeconds = 0.10f;

        // ── 目标判定 ─────────────────────────────────────────────────────────
        /// <summary>【本项目新增】`SkillTarget.Enemy` 的搜索半径（格）——点歪一格也能命中。</summary>
        public const int EnemySearchRadius = 1;

        /// <summary>【本项目新增】`SkillTarget.Ground` 且**无投射物**时的范围伤害半径（格）。</summary>
        public const int InstantAoeRadius = 1;

        // ── 技能树面板布局（`skill_c` 没有 SkillDesc 的行列列）──────────────────
        /// <summary>【本项目新增】技能树面板的等级档位（行）——与原版"1/6/12/18/24/30 逐档解锁"一致。</summary>
        public static readonly int[] TreeLevelTiers = { 1, 6, 12, 18, 24, 30 };

        // ═════════════════════════════════════════════════════════════════════
        // 法师技能的"怎么打"节奏（配表里没有这些列；官方在 `skills.txt` 的服务端函数里）
        //   改这里 = 改手感，不改数值口径；每条的**出处**都写在注释里。
        // ═════════════════════════════════════════════════════════════════════

        // ── 多发 / 直线 ──────────────────────────────────────────────────────
        /// <summary>【本项目新增】多发技能（充能弹）的**相邻两颗夹角**（度）。
        /// 官方靠 `ChargedBolt` 的散布参数（`skills.txt` 的 `Param1..8`，本项目未导入）
        /// ⇒ 取 15° ⇒ 3 发总张角 30°，"看得见是散的、又不会散到打不中"。</summary>
        public const float MultiProjectileSpreadDeg = 15f;

        /// <summary>【本项目新增】直线技能（炼狱之火 / 闪电）的**半宽**（格）——
        /// 判定口径 = 怪物中心到该线段的距离 ≤ 本值。0.75 格 ≈ 一格宽再多一点（擦着也算烧到）。</summary>
        public const float BeamHalfWidth = 0.75f;

        // ── 位移 ────────────────────────────────────────────────────────────
        /// <summary>【本项目新增】传送落点被地形挡住时，向外螺旋找可走格的**最大半径**（格）。</summary>
        public const int TeleportSnapRadius = 3;

        /// <summary>【本项目新增】传送的**最大距离**（格）——超出则拒绝并**退还法力**
        /// （原版传送无距离上限，但有施法距离惩罚；本项目用硬上限保证"不跳出地图/不穿墙"）。</summary>
        public const int TeleportMaxTiles = 20;

        // ── 持续场 / 火星带 ─────────────────────────────────────────────────
        /// <summary>【本项目新增】火焰之径**每一簇火星**的存活秒数（驱动器每 `tick` 秒生一簇）。</summary>
        public const float TrailPuddleSeconds = 2.0f;

        /// <summary>【本项目新增】火焰之径**每一簇火星**的结算间隔（秒）。</summary>
        public const float TrailPuddleTick = 0.4f;

        /// <summary>【本项目新增】持续场的**逐格命中半径**（格）——怪物中心到该格中心的距离 ≤ 本值算被烧到。
        /// 0.75 ⇒ 相邻格（距离 1.0）不算重复命中，同格（0.0）与半格偏移（~0.5）算。</summary>
        public const float FieldPointRadius = 0.75f;

        // ── 冲击 / 雷云 / 召唤 ──────────────────────────────────────────────
        /// <summary>【本项目新增】命中冲击闪光（`SkillFxKind.Impact`）的存活秒数。</summary>
        public const float ImpactSeconds = 0.22f;

        /// <summary>【本项目新增】陨石坠落物的**起飞高度**（格）：火球从落点正上方本高度开始掉，
        /// 按 t² 重力曲线加速落地，与落点标记的爆炸同步（官方 missiles.txt：
        /// `meteor` = CelFile `Meteor`（12 帧火球）+ CltSubMissile2 `meteortail` = `MeteorTail`（12 帧尾迹））。</summary>
        public const float MeteorFallHeight = 14f;

        /// <summary>【本项目新增】雷云风暴的**每次劈击间隔**（秒；配表 `skill_c` 无该列，
        /// 官方在 `skills.txt` 的 `delay`）。</summary>
        public const float StormTickSeconds = 1.0f;

        /// <summary>【本项目新增】多头蛇吐出的投射物 code（官方 `Hydra` 用 `firebolt`）。</summary>
        public const string SummonMissile = "firebolt";

        /// <summary>【本项目新增】多头蛇的**蛇口份数**（纯表现：`SkillFxView.SyncSummon` 摆
        /// 主口 + 两侧小一号共 3 根原版高火柱；开火仍只驱动一次 ⇒ 不放大伤害）。</summary>
        public const int HydraHeads = 3;

        // ── 被动：温暖（唯一的法力回复来源）──────────────────────────────────
        /// <summary>【本项目新增】温暖 1 级时**每秒**回复 = `maxMana × 本值 / 1000`（千分比）。
        /// 出处：官方 `skills.txt` 的 `Warmth` 行 `Param1 = 30`、`Param2 = 12`
        /// ⇒ 1 级 3.0%/s、每级 +1.2%/s（原版口径一致：1 级 30/1000·Δmana，逐级递增）。</summary>
        public const int WarmthBasePercent = 30;

        /// <summary>【本项目新增】温暖每级递增（千分比；见 <see cref="WarmthBasePercent"/>）。</summary>
        public const int WarmthPercentPerLevel = 12;

        // ── 元素精通（fire / lightning：进"掷点之前"的乘区；cold：**降怪物抗性**，不是加伤）──
        // 出处（官方 `skills.txt` 实列，逐行核对；**旧口径"三系共用 30/12"是错的**，
        //   那条注释声称"三行 Param1=30 Param2=12"与官方数据不符 —— 官方三行各不相同）：
        //     Id=61  Fire Mastery       Param1=**30**  Param2=**7**
        //     Id=63  Lightning Mastery  Param1=**50**  Param2=**12**
        //     Id=65  Cold Mastery       Param1=**20**  Param2=**5**
        //   ⇒ 满级 20 级：火 30+7×19 = **163%**（旧口径 258%，超 95 个百分点）、
        //     闪电 50+12×19 = **278%**（旧口径 258%，少 20 个百分点）。
        //   ⚠️ **冷精通的机制与另两系不同**：官方是"按等级降低目标的冰冷抗性"
        //     （社区 1.10 文档一致：reduces the cold resistances of monsters，抗性下限 −100%），
        //     **不是**加技能伤害 ⇒ 冷精通不加进任何乘区，走抗性通道（见
        //     `SkillModule.ColdMasteryResistReduction` → `DamagePipeline` → `MonsterModule.ApplyDamage`）。
        /// <summary>火焰精通 1 级加成（%）；官方 `Fire Mastery` Param1。</summary>
        public const int FireMasteryBasePercent = 30;

        /// <summary>火焰精通每级递增（%）；官方 `Fire Mastery` Param2。</summary>
        public const int FireMasteryPercentPerLevel = 7;

        /// <summary>闪电精通 1 级加成（%）；官方 `Lightning Mastery` Param1。</summary>
        public const int LightningMasteryBasePercent = 50;

        /// <summary>闪电精通每级递增（%）；官方 `Lightning Mastery` Param2。</summary>
        public const int LightningMasteryPercentPerLevel = 12;

        /// <summary>冰冷精通 1 级降低的怪物冰冷抗性（%）；官方 `Cold Mastery` Param1。</summary>
        public const int ColdMasteryResistBase = 20;

        /// <summary>冰冷精通每级多降的抗性（%）；官方 `Cold Mastery` Param2。</summary>
        public const int ColdMasteryResistPerLevel = 5;

        /// <summary>冰冷精通能把怪物抗性压到的**下限**（官方：不能破 −100）。</summary>
        public const int ColdMasteryResistFloor = -100;

        // ── 三种护甲（冻结/寒颤/冰冷）的防御加成 ─────────────────────────────
        //   出处（三个技能同一个官方机制）：官方 `skills.txt` 的三种护甲行都带
        //     `aurastat1 = skill_armor_percent`、`aurastatcalc1 = ln12`
        //   （`ln12` = Param1 + (等级-1) × Param2），且列注释逐字给出基准值与每级增量：
        //     Frozen Armor ：「30 % AC base / 5 % AC per level」→ Param1=30, Param2=5
        //     Shiver Armor ：「45 % AC base / 6 % AC per level」→ Param1=45, Param2=6
        //     Chilling Armor：「45 % AC Bonus / 5 % AC bonus per level」→ Param1=45, Param2=5
        //   ⇒ 防御加成（%）= 基准 + (技能等级 - 1) × 每级增量，加到玩家 AC 上（原版是加法叠加：
        //     装备 `ac%` 与技能加成同属"百分比 AC 加成"一档，见 `PlayerStats.Recalc`）。
        /// <summary>【本项目新增】冻结护甲 1 级的防御加成（%）；见本段出处注释。</summary>
        public const int FrozenArmorDefenseBasePercent = 30;

        /// <summary>【本项目新增】冻结护甲每级递增（%）；见本段出处注释。</summary>
        public const int FrozenArmorDefensePerLevel = 5;

        /// <summary>【本项目新增】寒颤护甲 1 级的防御加成（%）；见本段出处注释。</summary>
        public const int ShiverArmorDefenseBasePercent = 45;

        /// <summary>【本项目新增】寒颤护甲每级递增（%）；见本段出处注释。</summary>
        public const int ShiverArmorDefensePerLevel = 6;

        /// <summary>【本项目新增】冰冷护甲 1 级的防御加成（%）；见本段出处注释。</summary>
        public const int ChillingArmorDefenseBasePercent = 45;

        /// <summary>【本项目新增】冰冷护甲每级递增（%）；见本段出处注释。</summary>
        public const int ChillingArmorDefensePerLevel = 5;

        // ── 能量护盾（受伤时按比例由法力承担）──────────────────────────────
        /// <summary>【本项目新增】能量护盾 1 级吸收比例（百分比）。
        /// 出处：官方 `skills.txt` 的 `Energy Shield` 行 `Param1 = 20`、`Param2 = 6`
        /// ⇒ 1 级 20%、每级 +6%（**只作用在"受伤之后"的补偿上**，不改抗性结算）。</summary>
        public const int EnergyShieldBasePercent = 20;

        /// <summary>【本项目新增】能量护盾每级递增（百分比）。</summary>
        public const int EnergyShieldPercentPerLevel = 6;

        // ── 静电力场 ────────────────────────────────────────────────────────
        /// <summary>【本项目新增】静电力场把目标削到**当前生命的**这个比例（普通难度 25%）。
        /// 出处：官方 `skills.txt` 的 `Static Field`：普通难度下限 25%、恶梦 33%、地狱 50%
        /// （本项目只有普通难度 ⇒ 只实现 25% 这一档；difficulty 表未接入）。</summary>
        public const float StaticFieldKeepFraction = 0.25f;
    }
}
