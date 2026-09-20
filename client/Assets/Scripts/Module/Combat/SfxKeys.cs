// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · Module/Combat/SfxKeys.cs
// **本项目新增**：战斗/技能/受击类**音效键常量**的唯一来源。
//
// 为什么需要它：`IAudioModule.Sfx/SfxAt(string key, …)` 的注释明确「**音效键就是资源名**」
// （引擎按 `Sound/SFX/{name}` 约定取资源，见 `Core/ResPaths.D2Sfx`），
// 但项目里**没有**音效键的登记处（`Core/ResPaths.cs` 不登记具体音效名，`Core/Events.cs` 只登记事件名）。
// 战斗管线要在多处触发音效（命中 / 未命中 / 受击 / 死亡 / 复活 / 施法），
// 若把 `"hit"` 这类字面量散落各处，改名必漏、检索不到 ⇒ 收敛到本文件一处。
//
// ⚠️ 这些键**必须与 agent-11（`Module/Audio`）的解包产物名一致**；
//    素材未到位前 `IAudioModule` 是空实现，调用只留日志。
//    真实文件名到位后：**只改本文件的常量值**，不动任何调用点。
//    （已追加登记 `client/资源欠缺清单.md` → 音效一节的期望键名）
// ─────────────────────────────────────────────────────────────────────────────

namespace Diablo2.Module.Combat
{
    /// <summary>战斗音效键（= 资源名；引擎按 `Sound/SFX/{键}` 取）。</summary>
    public static class SfxKeys
    {
        /// <summary>命中（挥砍打到目标的"噗"）。</summary>
        public const string Hit = "hit";

        /// <summary>未命中（挥空）。</summary>
        public const string Miss = "miss";

        /// <summary>玩家受伤。</summary>
        public const string PlayerHurt = "player_hurt";

        /// <summary>玩家死亡。</summary>
        public const string PlayerDie = "player_die";

        /// <summary>玩家复活。</summary>
        public const string PlayerRevive = "player_revive";

        /// <summary>怪物死亡。</summary>
        public const string MonsterDie = "monster_die";

        /// <summary>怪物攻击（挥击/啄击）。</summary>
        public const string MonsterAttack = "monster_attack";

        /// <summary>萨满复活同伴（原版是吟唱）。</summary>
        public const string MonsterRevive = "monster_revive";

        /// <summary>技能施放（通用）。</summary>
        public const string Cast = "cast";

        /// <summary>火系技能（火弹/火焰箭…）。</summary>
        public const string CastFire = "cast_fire";

        /// <summary>冰系技能（冰弹/寒冰箭…）。</summary>
        public const string CastCold = "cast_cold";

        /// <summary>电系技能。</summary>
        public const string CastLightning = "cast_lightning";

        /// <summary>毒系技能。</summary>
        public const string CastPoison = "cast_poison";

        /// <summary>升级（`Events.LevelUp` 时）。</summary>
        public const string LevelUp = "level_up";

        /// <summary>按伤害类型取施法音效键（未登记的类型回落到 <see cref="Cast"/>）。</summary>
        public static string CastOf(Diablo2.Def.DamageType type)
        {
            switch (type)
            {
                case Diablo2.Def.DamageType.Fire: return CastFire;
                case Diablo2.Def.DamageType.Cold: return CastCold;
                case Diablo2.Def.DamageType.Lightning: return CastLightning;
                case Diablo2.Def.DamageType.Poison: return CastPoison;
                default: return Cast;
            }
        }
    }
}
