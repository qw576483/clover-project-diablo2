// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · Module/Skill/SkillBuffs.cs
// 技能增益对**其它模块**开放的**只读查询口**（**本项目新增**）。
//
// ── 为什么必须是"静态只读口"而不是加契约方法 ────────────────────────────────
//   `Module/Contracts.cs` 的 `ICombatModule` 与 `ISkillModule` 都是**冻结契约**
//   （注释原文：「怪物 AI 与玩家/怪物模块之间只经本门面结算伤害」/契约不允许加方法）。
//   「附魔」这类增益的真实效果必须发生在**普攻结算里**（`Module/Combat/CombatModule`），
//   而那是另一个模块 ⇒ 需要一个不触碰任何接口的通道。
//
//   本类就是那条通道：**写方只有 `SkillModule` 一个**（增益生效/到期/清档时 `Set…`），
//   读方可以有任何模块（当前 = `CombatModule` 的普攻附加火焰伤害）。
//
// ── 边界（不许越界）─────────────────────────────────────────────────────────
//   · 本类**只有两个静态状态**（近战附加火焰伤害的 min/max、护甲防御加成%），**不掷随机**
//     —— 随机一律由调用方自己的 `CloverEngine.Rng` 掷（见 `DamagePipeline` 文件头的同一口径），
//     否则"同 seed 可复现"会被打破。
//   · 本类**不做结算**：伤害走 `DamagePipeline.ApplyToMonster`（唯一结算函数），
//     抗性只在那里减一次。
//   · 本类的生命周期**完全由 `SkillModule` 驱动**：增益到期 / 换角色 / `ClearProjectiles`
//     都会 `SetMeleeFire(0, 0)` ⇒ 静止态恒为零，不会"漏一条挂到下一局"。
//
// ── 判据（`skillcheck` 宿主逐条钉）─────────────────────────────────────────
//   · 附魔生效中：`HasMeleeFire == true` 且 `MeleeFireMin/Max` > 0；
//   · 附魔**未**学 / 已到期 / 清档后：恒为 `false` 且 min/max = 0；
//   · `SetMeleeFire(0, 0)` 与 `(3, 5)` 之间来回切 ⇒ 读出值与写入值逐字相等（无误存）。
// ─────────────────────────────────────────────────────────────────────────────

namespace Diablo2.Module.Skill
{
    /// <summary>
    /// 技能增益的跨模块**只读**查询口（当前只承载「附魔」的近战附加火焰伤害）。
    /// <para>写方 = `SkillModule`（唯一）；读方 = 任何模块（当前 `CombatModule` 普攻）。</para>
    /// </summary>
    internal static class SkillBuffs
    {
        private static int _meleeFireMin;
        private static int _meleeFireMax;

        /// <summary>是否有近战附加火焰伤害在生效（= 附魔挂上了）。</summary>
        public static bool HasMeleeFire { get { return _meleeFireMax > 0; } }

        /// <summary>附加火焰伤害下限（0 = 没有）。</summary>
        public static int MeleeFireMin { get { return _meleeFireMin; } }

        /// <summary>附加火焰伤害上限（0 = 没有）。</summary>
        public static int MeleeFireMax { get { return _meleeFireMax; } }

        /// <summary>
        /// 设置近战附加火焰伤害（`min/max` ≤ 0 ⇒ 等价于"取消"；负数一律归零）。
        /// <para>唯一调用点 = `SkillModule`（附魔生效 / 到期 / 清档）。</para>
        /// </summary>
        public static void SetMeleeFire(int min, int max)
        {
            _meleeFireMin = min > 0 ? min : 0;
            _meleeFireMax = max > 0 ? max : 0;
            // 上下限写反时按上限定（避免 RollWeaponDamage 收到 min>max 的非法区间）
            if (_meleeFireMax < _meleeFireMin) _meleeFireMax = _meleeFireMin;
        }

        // ═════════════════════════════════════════════════════════════════════
        // 护甲防御加成（冻结 / 寒颤 / 冰冷护甲）
        // ═════════════════════════════════════════════════════════════════════

        private static int _armorDefensePercent;

        /// <summary>
        /// 三种护甲（冻结 / 寒颤 / 冰冷）当前提供的**防御加成**（%，0 = 没有）。
        /// <para>数值出处 = `SkillTuning` 的「三种护甲的防御加成」段（官方 `skills.txt` 的
        /// `aurastat1 = skill_armor_percent`，Param 注释给基准值与每级增量）。</para>
        /// <para>读方 = `Module/Player/PlayerModule.Defense`（命中判定的 `player.Defense` 与
        /// 角色面板同走该属性）⇒ 无需触发 `PlayerStats.Recalc`：加成是读时叠加的。</para>
        /// </summary>
        public static int ArmorDefensePercent { get { return _armorDefensePercent; } }

        /// <summary>
        /// 设置护甲防御加成%（≤ 0 ⇒ 清零）。唯一调用点 = `SkillModule`（护甲生效 / 到期 / 清档）。
        /// <para>互斥口径与护甲反伤一致（`SkillModule.OnPlayerDamaged` 的 ②）：多件护甲同时挂时
        /// 取**等级最高**的那件的加成，只算一份。</para>
        /// </summary>
        public static void SetArmorDefensePercent(int percent)
        {
            _armorDefensePercent = percent > 0 ? percent : 0;
        }
    }
}
