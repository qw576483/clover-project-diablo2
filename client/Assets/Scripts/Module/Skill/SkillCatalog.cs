// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · Module/Skill/SkillCatalog.cs
// 技能**行为目录**（**本项目新增**）：`skill_c` 没有"这个技能怎么打"的列
//   （官方在 `skills.txt` 的 `srvdofunc`/`cltdofunc` + `SkillDesc` 里，本项目未导入）
//   ⇒ 本文件是该映射的**唯一出处**。
//
// ── 为什么必须成表（不能继续按"有伤害就 Enemy"推断）────────────────────────────
//   法师 30 个技能里，"有伤害 + 有 missile"这一组合至少对应 6 种**完全不同**的东西：
//     · 直线飞行体（火弹 / 冰弹 / 闪电）           → 命中单体
//     · 落点炸开（火球 / 冰爆 / 冰川尖刺 / 冰霜宝珠）→ 飞到/命中后**范围**结算
//     · 自身为圆心（新星 / 霜之新星）              → 不是"发一颗弹飞出去"
//     · 地面持续场（火墙 / 火焰之径 / 暴风雪）      → 铺一片**留在原地**的伤害区
//     · 直线扫射（炼狱之火）                       → 不是"飞过去"，是"烧住一条线"
//     · 延迟落点（陨石）                           → 先出落点标记、过一会儿才炸
//   旧的 `TargetOf` 推断只能给出一个粗判（`Missile != "" → Ground`）⇒ 30 个技能里有 12 个
//   会被当成"朝地面扔一颗弹"，施放出来"看着都一样"（回报原话：放技能没效果）。
//
// ── 表的边界（改哪里）─────────────────────────────────────────────────────────
//   · **数值**（法力/伤害/需求等级/投射物速度射程）一律仍来自配表 `skill_c`/`missile_c`；
//     本表**只放行为与表现**（怎么打、放几个、半径几格、持续多久、用哪份素材）。
//   · 表外的技能（亚马逊/死灵/圣骑/野蛮人共 120 个）走 `Infer` 兜底 = **与改动前逐字相同**的
//     推断（`Passive/Missile→Projectile/有伤害→Projectile/Self`）⇒ 其它职业零回归。
//   · 判据：`combatcheck` 的「法师 30 技能逐条」段要求 id 31..60 **每一条**都在表内且行为
//     不是 `Unknown`（少一行 ⇒ 直接红）；同时要求"表内每条 id 都属于当前职业法师"。
// ─────────────────────────────────────────────────────────────────────────────

using Diablo2.Def;

namespace Diablo2.Module.Skill
{
    /// <summary>技能的行为分类（"这一下怎么打"）。</summary>
    internal enum SkillBehavior
    {
        /// <summary>目录里没有这一条（只有 `Infer` 兜底出来的才算；表内行不许用它）。</summary>
        Unknown = 0,

        /// <summary>被动：只能学、不能施放（温暖 / 三种精通）。</summary>
        Passive,

        /// <summary>直线飞行体，命中单体。</summary>
        Projectile,

        /// <summary>直线飞行体，命中（或到达）时按 `radius` **范围**结算。</summary>
        SplashProjectile,

        /// <summary>
        /// **在目标格立即结算**（不飞出东西）：`radius = 0` ⇒ 单体；`radius &gt; 0` ⇒ 该格半径内全部。
        /// <para>表外技能（其它四个职业）走 <see cref="SkillCatalog.Infer"/> 时用的就是这一档 —— 语义与
        /// 改动前的 `TargetOf → Ground/Enemy` 两条即时分支**逐字等价**（零回归）。</para>
        /// </summary>
        InstantArea,

        /// <summary>一次发 `count` 颗（扇形散射）。</summary>
        MultiProjectile,

        /// <summary>以**施法者**为圆心的即时范围（新星 / 霜之新星）。</summary>
        Nova,

        /// <summary>在地面留一片**持续**伤害区（火墙 / 暴风雪）。</summary>
        GroundField,

        /// <summary>跟随施法者、边移动边留火星的持续伤害带（火焰之径）。</summary>
        Trail,

        /// <summary>从施法者朝目标方向的**直线贯穿**（炼狱之火：一条线上的全部命中）。</summary>
        Beam,

        /// <summary>连锁：命中后跳到最近的未命中目标，最多 `count` 跳（连锁闪电）。</summary>
        Chain,

        /// <summary>延迟落点：先出落点标记，`duration` 秒后在 `radius` 内结算（陨石）。</summary>
        DelayedStrike,

        /// <summary>持续"近身自动打击"：`duration` 秒内每 `tick` 秒劈最近的一只（雷云风暴）。</summary>
        Storm,

        /// <summary>位移：把施法者搬到目标格（传送）。</summary>
        Teleport,

        /// <summary>自身增益（三种护甲 / 能量护盾 / 附魔）：给 `_buffs` 挂一条限时状态。</summary>
        Buff,

        /// <summary>召唤物（多头蛇）：在落点放一个会自己开火的单位。</summary>
        Summon,

        /// <summary>力场（静电力场）：把半径内所有怪物的**当前生命按比例**削掉。</summary>
        ForceField,

        /// <summary>念力（心灵传动）：隔空拾取范围内的地面物品（顺带对目标怪打一下）。</summary>
        Telekinesis,
    }

    /// <summary>一条技能行为（表内一行）。字段含义见 <see cref="SkillCatalog"/> 文件头。</summary>
    internal struct SkillSpec
    {
        /// <summary>行为分类。</summary>
        public SkillBehavior behavior;

        /// <summary>个数：多发数 / 连锁跳数 / 场格数（不用 = 0）。</summary>
        public int count;

        /// <summary>作用半径（格；不用 = 0）。</summary>
        public int radius;

        /// <summary>持续秒数（场 / 增益 / 延迟落点 / 召唤；不用 = 0）。</summary>
        public float duration;

        /// <summary>结算间隔秒数（场 / 雷云 / 召唤开火；不用 = 0）。</summary>
        public float tick;

        /// <summary>表现用素材：投射物 CelFile；`obj:` 前缀 = `D2/Objects/<dir>/NNN` 逐帧。空 = 取配表 missile。</summary>
        public string art;

        /// <summary>命中/爆炸表现素材（空 = 按伤害类型的默认冲击图）。</summary>
        public string impactArt;

        /// <summary>命中冲击的染色（0 = 不染，用素材本色）。</summary>
        public float tint;

        /// <summary>
        /// 施放的**目标要求**（`skill_c` 没有 target 列 ⇒ `SkillModule.TargetOf` 只能按
        /// "有伤害/有 missile"粗判，见那个方法的注释）。
        /// <para>
        /// ⚠️ **必须按行为定，不能按伤害列定**：粗判会把「新星 / 霜之新星」（自身为圆心）、
        /// 「雷云风暴」（自身光环）、「三种护甲 / 能量护盾 / 附魔」（自身增益）、
        /// 「火墙 / 暴风雪 / 陨石 / 多头蛇」（往地上放）全判成 <see cref="SkillTarget.Enemy"/>
        /// ⇒ `TryCast` 会以"需要敌方目标"**拒绝施放**（点空地放不出来 = 回报的"放技能没效果"）。
        /// </para>
        /// <para>表外技能（其它四个职业）由 <see cref="SkillCatalog.Infer"/> 填**旧的粗判值**
        /// ⇒ 与改动前逐字等价（零回归）。</para>
        /// </summary>
        public SkillTarget target;
    }

    /// <summary>技能行为目录（法师 30 条；表外走 <see cref="Infer"/> 兜底）。</summary>
    internal static class SkillCatalog
    {
        /// <summary>目录行数（判据用：法师 30 个技能必须一条不少）。</summary>
        internal const int RowCount = 30;

        // ── 直接映射的素材（官方 `Missiles.txt` 里 `CelFile` 为空的两个技能，
        //    本项目按"同系同观感"补一份**表现**素材，不改任何数值）──────────────────
        /// <summary>
        /// 「闪电」的表现素材（官方 `lightningbolt.cel_file = null`，原版本来就是**程序化折线**、没有精灵图）。
        /// 取官方连锁闪电同款 `LightningStrike`（148×142 亮折线 + 辉光），配合
        /// `SkillFxView` 的"沿世界长度密铺 + 逐份转向对齐弹道" ⇒ 读作一根连续闪链。
        /// </summary>
        internal const string LightningArtFallback = "LightningStrike";

        /// <summary>「暴风雪」的表现素材 = 原版 `Blizzard.dcc` 冰柱（已从 MPQ 解包导入；不再用 `IceBlast` 近似）。</summary>
        internal const string BlizzardArtFallback = "Blizzard";

        /// <summary>
        /// 「多头蛇」**蛇口本体**的表现素材：官方 `groundFireBig`（66×312 原版高火柱）。
        /// <para>原版 Hydra 是**召唤怪物**（本工程无召唤怪物精灵管线），蛇身画不出来；
        /// 但"原地不动的喷火口"才是它最认得出的特征 ⇒ 用一根持续燃烧的高火柱顶替，
        /// 加上每 0.8s 真的朝最近敌人吐 `Firebolt`。旧值 `Firebolt`（圆火球）读作"一簇火"，不是"喷火口"。</para>
        /// </summary>
        internal const string HydraArt = "groundFireBig";

        /// <summary>多头蛇**出生那一下**的落地闪光素材（官方 missiles.txt：`hydra` → cel_file `Firebolt`）。</summary>
        internal const string HydraSpawnFlashArt = "Firebolt";

        /// <summary>`D2/Objects/fx` 的帧数（与 `export_deco.py` 产物一致；判据钉住）。</summary>
        internal const int HydraArtFrames = 26;

        // ── 官方 code（`skill_c.code` 列的原文；**一律按 code 查 id，不写死数字**）─────
        //   理由：id 31..60 是本项目的**自定排序**，而 code 来自官方 `skills.txt` 的行名
        //   ⇒ 将来若配表重排 id，写死数字的被动/增益会静默挂到别的技能上（且没有任何报错）。
        //   取值方式：`SkillModule.IdOfCode(...)` → `-1` = 本职业没有该技能（表外职业自然为 -1）。
        /// <summary>温暖（`Warmth`）——唯一的法力回复来源。</summary>
        internal const string CodeWarmth = "Warmth";

        /// <summary>能量护盾（`Energy Shield`）——受伤时按比例由法力承担。</summary>
        internal const string CodeEnergyShield = "Energy Shield";

        /// <summary>附魔（`Enchant`）——近战附加火焰伤害（跨模块只读口见 `SkillBuffs`）。</summary>
        internal const string CodeEnchant = "Enchant";

        /// <summary>雷云风暴（`Thunder Storm`）——限时自动劈最近的敌人。</summary>
        internal const string CodeThunderStorm = "Thunder Storm";

        /// <summary>三种护甲（冻结 / 寒颤 / 冰冷）的 code 原文 —— 顺序即 `SkillModule.ArmorIds` 的顺序。</summary>
        internal static readonly string[] CodeArmors = { "Frozen Armor", "Shiver Armor", "Chilling Armor" };

        /// <summary>火焰精通（`Fire Mastery`）。</summary>
        internal const string CodeFireMastery = "Fire Mastery";

        /// <summary>闪电精通（`Lightning Mastery`）。</summary>
        internal const string CodeLightningMastery = "Lightning Mastery";

        /// <summary>冰冷精通（`Cold Mastery`）。</summary>
        internal const string CodeColdMastery = "Cold Mastery";

        // ── 左键可用性（原版 `skills.txt` 的 `leftskill` 列）──────────────────────
        //   口径（官方数据，**不是推断**）：官方 `data/global/excel/skills.txt`（本仓
        //   `原版资源/d2lod1.10txt-1.10f/data/global/excel/skills.txt`）有一列 `leftskill`：
        //     值 `1`  ⇒ 这个技能**可以**装到鼠标**左键**；
        //     值为空  ⇒ **不能**装左键（原版帮助文本的原话见凯恩之角
        //     `wiki.d.163.com/In_Game_control_(Diablo_II)`：「注意：某些技能（通常是非攻击技能）不能用
        //     鼠标左键施放，而右键可以设置所有的技能。」）。
        //   ⇒ 右键**所有**已学技能都能装（本表不拦）；左键只有下面 `CodeLeftCastable` 里的能装。
        //   ⚠️ 这张表**必须与官方列逐条一致**：判据 = `skillcheck` 的「左键可用性」段
        //   （它拿本表两组 + 法师 30 行做**划分**校验：两组无交集、并集恰好 30 行 ⇒ 打错一个 code 直接红）。

        /// <summary>
        /// 法师技能里**可以**装左键的 code（官方 `skills.txt` 的 `leftskill=1` 那 14 条，逐条抄自官方列）。
        /// </summary>
        internal static readonly string[] CodeLeftCastable =
        {
            "Fire Bolt", "Warmth", "Charged Bolt", "Ice Bolt", "Inferno", "Ice Blast", "Fire Ball",
            "Lightning", "Chain Lightning", "Glacial Spike", "Fire Mastery", "Lightning Mastery",
            "Frozen Orb", "Cold Mastery",
        };

        /// <summary>
        /// 法师技能里**不能**装左键的 code（官方 `skills.txt` 的 `leftskill` 列为空那 16 条）。
        /// <para>与 <see cref="CodeLeftCastable"/> 一起构成法师 30 行的**划分**（判据会校验）。</para>
        /// </summary>
        internal static readonly string[] CodeNotLeftCastable =
        {
            "Frozen Armor", "Static Field", "Telekinesis", "Frost Nova", "Blaze", "Nova", "Shiver Armor",
            "Fire Wall", "Enchant", "Teleport", "Meteor", "Thunder Storm", "Energy Shield", "Blizzard",
            "Chilling Armor", "Hydra",
        };

        /// <summary>普通攻击（`-1`）的左键可用性：官方 `skills.txt` 里 `Attack`（Id 0）的 `leftskill=1` ⇒ 可以。</summary>
        internal const bool AttackLeftCastable = true;

        private static readonly System.Collections.Generic.Dictionary<int, SkillSpec> Table = Build();

        /// <summary>
        /// 这个技能能不能装到**鼠标左键**（官方的左键限制）。
        /// <para>判据顺序：`-1`（普通攻击）→ 固定 <see cref="AttackLeftCastable"/>；本表登记过的技能
        /// → 看 code 落在 <see cref="CodeLeftCastable"/> 还是 <see cref="CodeNotLeftCastable"/>；
        /// **本表没登记的技能**（其它四个职业的 120 个）→ 返回 `true`（**不改它们的行为**，与改动前一致；
        /// 本项目只把官方列落到了法师这 30 条上，见文件头「表的边界」）。</para>
        /// </summary>
        internal static bool CanLeftCast(int skillId, Table.BaseSkillRow row)
        {
            if (skillId == -1) return AttackLeftCastable;
            if (row == null) return true;                  // 取不到行 ⇒ 不拦（宁可漏拦也不误拦）
            var code = row.Code;
            if (string.IsNullOrEmpty(code)) return true;
            if (!Table.ContainsKey(skillId)) return true;  // 表外职业：零回归
            for (var i = 0; i < CodeLeftCastable.Length; i++)
            {
                if (string.Equals(CodeLeftCastable[i], code, System.StringComparison.OrdinalIgnoreCase)) return true;
            }
            for (var i = 0; i < CodeNotLeftCastable.Length; i++)
            {
                if (string.Equals(CodeNotLeftCastable[i], code, System.StringComparison.OrdinalIgnoreCase)) return false;
            }
            // 表内技能却没落进任何一组 ⇒ 说明上面两组抄漏了/抄错了（判据会红）⇒ 这里**拦**（保守）
            return false;
        }

        /// <summary>取一条技能行为（表内优先；表外走与改动前逐字相同的推断）。</summary>
        internal static SkillSpec Of(int skillId, Table.BaseSkillRow row, SkillDef def)
        {
            if (Table.TryGetValue(skillId, out var spec)) return spec;
            return Infer(row, def);
        }

        /// <summary>本表是否登记了这个技能 id（判据用）。</summary>
        internal static bool Has(int skillId) => Table.ContainsKey(skillId);

        /// <summary>
        /// 表外技能（其它四个职业）的兜底推断 —— **与改动前 `TryCast` 的分支逐字等价**：
        /// <list type="bullet">
        /// <item>被动 → <see cref="SkillBehavior.Passive"/>（上游 `IsPassive` 已拒施放）；</item>
        /// <item>有投射物 → <see cref="SkillBehavior.Projectile"/>（等价旧的
        /// <c>def.target == Ground &amp;&amp; Missile != ""</c> 分支）；</item>
        /// <item>有伤害（含武器伤害源）→ <see cref="SkillBehavior.InstantArea"/>，其中
        /// **旧的 `Ground` ⇒ `radius = InstantAoeRadius`**（旧 `cast.ground.noaoe` 分支）、
        /// 否则 `radius = 0`（旧单目标即时分支）；</item>
        /// <item>其余 → <see cref="SkillBehavior.Buff"/>（旧的 `cast.nobuff` 分支：只扣蓝进冷却）。</item>
        /// </list>
        /// </summary>
        internal static SkillSpec Infer(Table.BaseSkillRow row, SkillDef def)
        {
            var s = new SkillSpec { behavior = SkillBehavior.Unknown };
            // 目标要求 = **配表现成的粗判值**（`SkillModule.TargetOf` 已写进 `def.target`）
            //   —— 表外职业这一格必须与改动前一字不差（零回归的唯一保证）。
            s.target = def != null ? def.target : SkillTarget.Self;
            if (row == null) return s;

            if (row.Passive != 0)
            {
                s.behavior = SkillBehavior.Passive;
                return s;
            }
            if (!string.IsNullOrEmpty(row.Missile))
            {
                s.behavior = SkillBehavior.Projectile;
                return s;
            }
            if (row.SrcDam > 0 || row.DmgMin > 0 || row.DmgMax > 0)
            {
                s.behavior = SkillBehavior.InstantArea;
                s.radius = s.target == SkillTarget.Ground ? Tuning.InstantAoeRadius : 0;
                return s;
            }
            s.behavior = SkillBehavior.Buff;
            s.duration = 0f;
            return s;
        }

        /// <summary>只读 `SkillTuning` 的两个常量（表内/表外共用一份值，不抄第二份）。</summary>
        private static class Tuning
        {
            public const int InstantAoeRadius = SkillTuning.InstantAoeRadius;
        }

        // ═════════════════════════════════════════════════════════════════════
        // 目录表（法师 id 31..60；数值仍在配表，这里只有行为/表现）
        //
        //   ⚠️ `art` / `impactArt` 的值 = **`MissileFrameCounts.generated.cs` 的键原文**
        //     （如 `Firebolt` / `Icebolt` / `GlacialSpike`）。原查表大小写敏感
        //     （`StringComparer.Ordinal`）⇒ 全小写的 `"firebolt"` 查不到 ⇒ `FramesOf` 返回 0
        //     ⇒ 技能特效**静默**退成程序化环/盘。
        // ═════════════════════════════════════════════════════════════════════
        private static System.Collections.Generic.Dictionary<int, SkillSpec> Build()
        {
            var t = new System.Collections.Generic.Dictionary<int, SkillSpec>();

            // ── 目标要求的判据（每行的 `target` 为什么是这个值）──────────────────
            //   · `Ground`  = 往**鼠标那一格**放（投射物 / 地面场 / 落点标记 / 召唤位 / 传送落点 /
            //                 念力目标）⇒ 哪怕那里没有怪也放得出来（"点空地也能放"）。
            //   · `Enemy`   = 必须点到怪物（连锁闪电 / 纯伤害即时技 / 直线喷吐）。
            //   · `Self`    = 以**自己**为圆心或挂在自己身上（新星 / 雷云 / 所有增益 / 火焰之径）
            //                 ⇒ 原版也是"按一下就在脚下生效"，不需要任何目标。
            //   · `None`    = 被动（只能学，不能施放）。
            //   ⛔ 不许照抄 `def.target`：那是"有伤害 ⇒ Enemy"的粗判，会把上表的 Self/Ground 全变成
            //     Enemy（点空地放不出来）。判据 = `skillcheck` 的「目标要求逐条」段。

            // ── 火系（tree 1）────────────────────────────────────────────────
            t[31] = new SkillSpec { behavior = SkillBehavior.Projectile, target = SkillTarget.Ground, art = "Firebolt", impactArt = "ExpArrowExplode" };       // 火弹
            t[32] = new SkillSpec { behavior = SkillBehavior.Passive, target = SkillTarget.None };                                                          // 温暖
            t[36] = new SkillSpec { behavior = SkillBehavior.Beam, target = SkillTarget.Enemy, radius = 4, duration = 0.5f, art = "Flamethrower", impactArt = "Flamethrower2" };  // 炼狱之火
            t[41] = new SkillSpec { behavior = SkillBehavior.Trail, target = SkillTarget.Self, duration = 3.0f, tick = 0.35f, count = 4, art = "groundFireSmall" };  // 火焰之径
            t[42] = new SkillSpec { behavior = SkillBehavior.SplashProjectile, target = SkillTarget.Ground, radius = 2, art = "Fireball", impactArt = "ExpArrowExplode" };  // 火球
            t[46] = new SkillSpec { behavior = SkillBehavior.GroundField, target = SkillTarget.Ground, count = 5, duration = 3.5f, tick = 0.4f, art = "groundFireBig" };   // 火墙
            t[47] = new SkillSpec { behavior = SkillBehavior.Buff, target = SkillTarget.Self, duration = 60f, radius = 1, art = "FireEnchant", impactArt = "FireCast_for_Sorceress" };  // 附魔（官方 Overlay.txt：`enchant` → Filename `FireEnchant` 17帧；cast 光 = `fire_cast_1` → `FireCast_for_Sorceress`）
            t[51] = new SkillSpec { behavior = SkillBehavior.DelayedStrike, target = SkillTarget.Ground, radius = 3, duration = 1.2f, art = "MeteorTargetIndicator", impactArt = "ExpArrowExplode" };  // 陨石
            t[56] = new SkillSpec { behavior = SkillBehavior.Passive, target = SkillTarget.None };                                                          // 火焰精通
            t[57] = new SkillSpec { behavior = SkillBehavior.Summon, target = SkillTarget.Ground, duration = 12f, tick = 0.8f, radius = 6, count = 1, art = HydraArt, impactArt = HydraSpawnFlashArt };  // 多头蛇

            // ── 电系（tree 2）────────────────────────────────────────────────
            t[33] = new SkillSpec { behavior = SkillBehavior.MultiProjectile, target = SkillTarget.Ground, count = 3, art = "ChargedBolt", impactArt = "ChargedBolt" };  // 充能弹
            t[37] = new SkillSpec { behavior = SkillBehavior.ForceField, target = SkillTarget.Self, radius = 4, art = "ElectricNova" };                      // 静电力场（官方 skills.txt：无发射导弹、castoverlay=light_cast_1 ⇒ 无飞行体；用电系新星环作 AoE 视觉，主题正确——旧值 obj:fx 是火，和火系技能撞脸）
            t[38] = new SkillSpec { behavior = SkillBehavior.Telekinesis, target = SkillTarget.Ground, radius = 5, art = "LightningCastRunesFront" };       // 心灵传动（官方 skills.txt：cast=none、castoverlay=`light_cast_2` → Overlay.txt Filename `LightningCastRunesFront` 10帧）
            t[43] = new SkillSpec { behavior = SkillBehavior.Nova, target = SkillTarget.Self, radius = 6, duration = 0.45f, art = "ElectricNova" };          // 新星
            t[44] = new SkillSpec { behavior = SkillBehavior.Beam, target = SkillTarget.Ground, radius = 8, duration = 0.35f, art = LightningArtFallback, impactArt = LightningArtFallback };  // 闪电（官方 missiles.txt：`lightningbolt` 的 cel_file=**null** ⇒ 原版本来就是程序化折线、没有精灵图；用官方连锁同款 `LightningStrike` 亮折线密铺 + 逐份转向对齐弹道 = 最接近"一根连续闪链"的最优近似；旧值 `LightningboltBig` 是竖直雷柱，不转就读作"一排竖条"）
            t[48] = new SkillSpec { behavior = SkillBehavior.Chain, target = SkillTarget.Enemy, count = 4, radius = 6, duration = 0.35f, art = "LightningStrike", impactArt = "LightningStrike" };   // 连锁闪电（官方 missiles.txt：`chainlightning` → cel_file `LightningStrike`。⚠️ `Chain` 的每份走的是 **impactArt**（见 `SkillFxView.ArtOf`），不配就落到 `DefaultImpactArt`，所以这里必须显式配）
            t[49] = new SkillSpec { behavior = SkillBehavior.Teleport, target = SkillTarget.Ground, art = "Teleport" };                                      // 传送（官方 missiles.txt：`teleport` → CelFile `Teleport`，18 帧真·原版；旧值 Gleam 是 sparkle 微光，张冠李戴）
            t[52] = new SkillSpec { behavior = SkillBehavior.Storm, target = SkillTarget.Self, duration = 10f, tick = 1.0f, radius = 7, art = "ElectricNova", impactArt = "LightningboltBig" };  // 雷云风暴（跟随视觉 = 雷云区 ElectricNova 环；落雷 = 原版 thunderstorm1~4 → LightningboltBig）
            t[53] = new SkillSpec { behavior = SkillBehavior.Buff, target = SkillTarget.Self, duration = 60f, radius = 1, art = "ManaShieldBall", impactArt = "LightningCast" };  // 能量护盾（官方 Overlay.txt：`energyshield` → Filename `ManaShieldBall` 8帧；cast 光 = `light_cast_1` → `LightningCast`）
            t[58] = new SkillSpec { behavior = SkillBehavior.Passive, target = SkillTarget.None };                                                          // 闪电精通

            // ── 冰系（tree 3）────────────────────────────────────────────────
            t[34] = new SkillSpec { behavior = SkillBehavior.Projectile, target = SkillTarget.Ground, art = "Icebolt", impactArt = "IceBlast" };               // 冰弹
            t[35] = new SkillSpec { behavior = SkillBehavior.Buff, target = SkillTarget.Self, duration = 60f, radius = 1, art = "FrozenArmor", impactArt = "IceCastNew01" };  // 冻结护甲（官方 Overlay.txt：`frozenarmor` → Filename `FrozenArmor` 24帧；cast 光 = `ice_cast_1` → `IceCastNew01`）
            t[39] = new SkillSpec { behavior = SkillBehavior.Nova, target = SkillTarget.Self, radius = 4, duration = 0.45f, art = "FrostNova" };            // 霜之新星
            t[40] = new SkillSpec { behavior = SkillBehavior.SplashProjectile, target = SkillTarget.Ground, radius = 2, art = "IceBlast", impactArt = "IceBlast" };     // 冰爆
            t[45] = new SkillSpec { behavior = SkillBehavior.Buff, target = SkillTarget.Self, duration = 60f, radius = 1, art = "FrozenArmor", impactArt = "IceCastNew01" };  // 寒颤护甲（官方 Overlay.txt：`shiverarmor` → Filename `FrozenArmor` 24帧——三护甲光环同一张图是官方事实；差异在受击反弹导弹 sparkle/chillingarmorbolt）
            t[50] = new SkillSpec { behavior = SkillBehavior.SplashProjectile, target = SkillTarget.Ground, radius = 1, art = "GlacialSpike", impactArt = "IceBlast" }; // 冰川尖刺
            t[54] = new SkillSpec { behavior = SkillBehavior.GroundField, target = SkillTarget.Ground, count = 9, duration = 4.0f, tick = 0.5f, radius = 4, art = BlizzardArtFallback };  // 暴风雪
            t[55] = new SkillSpec { behavior = SkillBehavior.Buff, target = SkillTarget.Self, duration = 60f, radius = 1, art = "FrozenArmor", impactArt = "IceCastNew01" };  // 冰冷护甲（官方 Overlay.txt：`chillarmor` → Filename `FrozenArmor` 24帧；受击反弹另有导弹 `chillingarmorbolt`→`Icebolt`，非光环本体）
            t[59] = new SkillSpec { behavior = SkillBehavior.SplashProjectile, target = SkillTarget.Ground, radius = 3, art = "IceOrb", impactArt = "IceBlast" };   // 冰霜宝珠（素材键 = `IceOrb`：原 `"frozenorb"` 在帧表/导出目录都不存在；弹体 celFile 也是 `IceOrb`）
            t[60] = new SkillSpec { behavior = SkillBehavior.Passive, target = SkillTarget.None };                                                          // 冰冷精通

            return t;
        }
    }
}
