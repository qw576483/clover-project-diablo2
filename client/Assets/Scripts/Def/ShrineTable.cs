// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · Def/ShrineTable.cs
// **官方神殿行为表**（逐行搬运，⛔ 不自造一行、不自造一个数）。
//
// 唯一出处：`原版资源/d2lod1.10txt-1.10f/data/global/excel/Shrines.txt`
//   （Blizzard North, 2000；列 = `Shrine Type / Shrine name / Effect / Code / Arg0 / Arg1 /
//    Duration in frames / reset time in minutes / rarity / view name / niftyphrase / effectclass / LevelMin`）
//   本文件的行序与表**逐行同序**（跳过表头后的 `None` 行为 row 0，与 `SourceRow` 字段一致）。
//
// 命名：`Shrine name` 列（英文官网名）保留为 `EnglishName`；`Name` = 该列的简体中文译名
//   （本项目口径：`chi_string.txt` 没有神殿名的对应条目 —— 该表 5393 条里 id 2225 = 「神殿」、
//    id 3313 = 「魔法神殿」只有类别名，没有逐条神殿名 ⇒ 逐条中文名是**本项目新增译名**，
//    只用于日志与文案，不参与任何判定）。
//
// 「本工程能落地哪几行」的判据（见 `Supported`）：
//   · 只依赖**角色自身**状态（生命 / 法力 / 耐力 / 防御 / 命中 / 抗性 / 经验）的行 ⇒ 可落地；
//   · 需要本工程**没有的系统**的行 ⇒ 不落地，逐条理由写在 `UnsupportedWhy`。
// ─────────────────────────────────────────────────────────────────────────────
using System.Collections.Generic;

namespace Diablo2.Def
{
    /// <summary>官方 `Shrines.txt` 的一行（见文件头）。</summary>
    public struct ShrineRow
    {
        /// <summary>该行在 `Shrines.txt` 里的**数据行号**（表头后第 0 行 = `None`）。</summary>
        public readonly int SourceRow;

        /// <summary>`Shrine Type` 列（`Recharge` / `Booster` / `Magic`）。</summary>
        public readonly string Type;

        /// <summary>`Shrine name` 列（官方英文名）。</summary>
        public readonly string EnglishName;

        /// <summary>本项目中文译名（仅日志/文案用，见文件头）。</summary>
        public readonly string Name;

        /// <summary>`Effect` 列（官方效果号）。</summary>
        public readonly int Effect;

        /// <summary>`Arg0` 列。</summary>
        public readonly int Arg0;

        /// <summary>`Arg1` 列。</summary>
        public readonly int Arg1;

        /// <summary>`Duration in frames` 列（0 = 无时限）。</summary>
        public readonly int DurationFrames;

        /// <summary>`reset time in minutes` 列（用掉之后多久可再激活，单位 = 分钟）。</summary>
        public readonly int ResetMinutes;

        /// <summary>`rarity` 列（掷选权重）。</summary>
        public readonly int Rarity;

        /// <summary>`effectclass` 列。</summary>
        public readonly int EffectClass;

        /// <summary>`LevelMin` 列。</summary>
        public readonly int LevelMin;

        /// <summary>本工程能否落地该行（判据见文件头；理由见 <see cref="UnsupportedWhy"/>）。</summary>
        public readonly bool Supported;

        public ShrineRow(int sourceRow, string type, string englishName, string name, int effect,
            int arg0, int arg1, int durationFrames, int resetMinutes, int rarity, int effectClass,
            int levelMin, bool supported)
        {
            SourceRow = sourceRow;
            Type = type;
            EnglishName = englishName;
            Name = name;
            Effect = effect;
            Arg0 = arg0;
            Arg1 = arg1;
            DurationFrames = durationFrames;
            ResetMinutes = resetMinutes;
            Rarity = rarity;
            EffectClass = effectClass;
            LevelMin = levelMin;
            Supported = supported;
        }
    }

    /// <summary>官方神殿行为表 + 按 `rarity` 的掷选（纯函数，离线可判）。</summary>
    public static class ShrineTable
    {
        /// <summary>
        /// 官方 `Shrines.txt` 的全部数据行（行序与表一致）。`Supported == false` 的行不进掷选池。
        /// </summary>
        public static readonly ShrineRow[] Rows =
        {
            new ShrineRow(0,  "None",     "None",                  "无",           0,  100, 100,    0, 2, 1, 0,  0, false),
            new ShrineRow(1,  "Recharge", "Refill",                "补充",         1,  100, 100,    0, 2, 1, 4,  1, true),
            new ShrineRow(2,  "Recharge", "Health Boost",          "生命强化",     2,  200, 400,    0, 5, 2, 2,  1, true),
            new ShrineRow(3,  "Recharge", "Mana Boost",            "法力强化",     3,  200, 400,    0, 5, 2, 3,  1, true),
            new ShrineRow(4,  "Recharge", "Health Exchange",       "生命转换",     4,  50,  500,    0, 0, 3, 2,  2, true),
            new ShrineRow(5,  "Recharge", "Mana Exchange",         "法力转换",     5,  50,  500,    0, 0, 3, 3,  2, true),
            new ShrineRow(6,  "Booster",  "Armor Boost",           "护甲强化",     6,  100,   1, 2400, 5, 2, 4,  5, true),
            new ShrineRow(7,  "Booster",  "Combat Boost",          "战斗强化",     7,  200, 200, 2400, 5, 2, 4,  8, true),
            new ShrineRow(8,  "Booster",  "Resist Fire Boost",     "抗火强化",     8,   75,   0, 3600, 5, 2, 4,  5, true),
            new ShrineRow(9,  "Booster",  "Resist Cold Boost",     "抗寒强化",     9,   75,   0, 3600, 5, 2, 4, 26, true),
            new ShrineRow(10, "Booster",  "Resist Lightning Boost","抗电强化",    10,   75,   0, 3600, 5, 2, 4, 32, true),
            new ShrineRow(11, "Booster",  "Resist Poison Boost",   "抗毒强化",    11,   75,   0, 3600, 5, 2, 4, 21, true),
            new ShrineRow(12, "Booster",  "Skill Boost",           "技能强化",    12,    2,   0, 2400, 5, 2, 4,  1, false),
            new ShrineRow(13, "Booster",  "Recharge Boost",        "法力回复强化",13,  400,   0, 2400, 5, 2, 4,  1, false),
            new ShrineRow(14, "Booster",  "Stamina Boost",         "耐力强化",    14,  200,   0, 4800, 5, 3, 4,  1, true),
            new ShrineRow(15, "Booster",  "Experience Boost",      "经验强化",    15,   50,  50, 3600, 0, 3, 4,  1, false),
            new ShrineRow(16, "Magic",    "Shrine of Enirhs",      "恩尼尔神殿",  16,    0,   0,    0, 0, 3, 1,  1, false),
            new ShrineRow(17, "Magic",    "Portal to unknown",     "未知传送门",  17,    0,   0,    0, 0, 2, 1,  3, true),
            new ShrineRow(18, "Magic",    "Gem Upgrade",           "宝石升级",    18,    0,   0,    0, 0, 3, 1,  4, false),
            new ShrineRow(19, "Magic",    "Storm Shrine",          "风暴神殿",    19,   50, 2000,   0, 0, 2, 1,  1, false),
            new ShrineRow(20, "Magic",    "Warping Shrine",        "扭曲神殿",    20,    0,   0,    0, 0, 3, 1,  3, false),
            new ShrineRow(21, "Magic",    "Exploding Shrine",      "爆炸神殿",    21,    5,  10,    0, 0, 3, 1,  1, false),
            new ShrineRow(22, "Magic",    "Poison Shrine",         "毒气神殿",    22,    5,  10,    0, 0, 3, 1,  1, false),
        };

        /// <summary>表头后第一行（`None`）在 <see cref="Rows"/> 里的下标。</summary>
        public const int NoneRow = 0;

        /// <summary>
        /// 未落地的行**逐条理由**（下标 = `Effect` 号）。
        /// <para>⛔ 不是"没做"，是"本工程没有那个系统" —— 每条都点名缺什么。</para>
        /// </summary>
        public static string UnsupportedWhy(int effect)
        {
            switch (effect)
            {
                case 12: return "需要把加成灌进 `ISkillModule` 的技能等级计算（技能模块自持 `_levels`，"
                              + "`IPlayerModule` 上没有任何技能等级入口）";
                case 13: return "需要改法力回复速率的结算点（本工程的法力回复不在 `Module/Player` 内，"
                              + "它只有 `RestoreMana(int)` 这个一次性入口 ⇒ 拿不到「每秒回多少」这一项）";
                case 15: return "「击杀经验 +50%」需要乘在击杀结算上，本工程的经验入口 `AddExp(int)` "
                              + "同时承担任务 / 其它经验发放 ⇒ 乘在上面会连带放大";
                case 16: return "「反转角色名」需要改角色名 / 名字渲染，且原版语义不可逆，本工程不实现";
                case 18: return "需要宝石系统（本工程物品表里没有宝石类别与升级配方）";
                case 19: return "「所有玩家与怪物掉 1/2 血并射火球」需要 AoE 伤害 + 投射物轮询，"
                              + "且会波及玩家 ⇒ 本工程不实现";
                case 20: return "需要「最近的怪物升格为精英」这条 AI 改造（怪物模块没有该入口）";
                case 21: return "需要「抛撒爆炸药水」的物品实例化 + 抛物线飞行，本工程没有";
                case 22: return "需要「抛撒毒气药水 + 神殿周围一圈毒气」，本工程没有";
                default: return null;
            }
        }

        /// <summary>按 `Effect` 号取行（找不到 ⇒ 返回 <see cref="NoneRow"/> 那一行并给出 false）。</summary>
        public static bool TryGetByEffect(int effect, out ShrineRow row)
        {
            for (var i = 0; i < Rows.Length; i++)
            {
                if (Rows[i].Effect == effect) { row = Rows[i]; return true; }
            }
            row = Rows[NoneRow];
            return false;
        }

        /// <summary>掷选池 = 全部 <see cref="ShrineRow.Supported"/> 且 `Rarity &gt; 0` 的行。</summary>
        public static List<int> BuildPool()
        {
            var pool = new List<int>(Rows.Length);
            for (var i = 0; i < Rows.Length; i++)
            {
                if (!Rows[i].Supported) continue;
                if (Rows[i].Rarity <= 0) continue;
                pool.Add(i);
            }
            return pool;
        }

        /// <summary>池内权重和（= 各行 `rarity` 之和）。</summary>
        public static int TotalWeight(List<int> pool)
        {
            var sum = 0;
            if (pool == null) return 0;
            for (var i = 0; i < pool.Count; i++) sum += Rows[pool[i]].Rarity;
            return sum;
        }

        /// <summary>
        /// 按官方 `rarity` 权重在池内掷一行（**纯函数**，`roll ∈ [0, totalWeight)`）。
        /// <para>越界 / 权重和 ≤ 0 ⇒ 返回 <see cref="NoneRow"/> 那一行（调用方据此跳过，不静默取值）。</para>
        /// </summary>
        public static bool TryRoll(List<int> pool, int roll, out ShrineRow row)
        {
            var total = TotalWeight(pool);
            if (total <= 0 || pool == null || pool.Count == 0 || roll < 0 || roll >= total)
            {
                row = Rows[NoneRow];
                return false;
            }

            var acc = 0;
            for (var i = 0; i < pool.Count; i++)
            {
                acc += Rows[pool[i]].Rarity;
                if (roll < acc) { row = Rows[pool[i]]; return true; }
            }
            row = Rows[NoneRow];
            return false;
        }
    }
}
