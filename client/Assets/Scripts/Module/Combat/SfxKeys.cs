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

        // ═════════════════════════════════════════════════════════════════════
        // 逐类怪物音效键（★ 片 monster-audio）
        //
        // 为什么要有这 30 个键：上面那 3 个通用键（`MonsterAttack`/`MonsterDie`/`MonsterRevive`）
        //   的素材源**全是堕落者 `fallen`** ⇒ 打僵尸、打血鹰听到的都是沉沦魔的叫声/死声，
        //   而**怪物自己的受击音（`MonSounds.HitSound`）一个键都没有** —— 这正是用户说的
        //   「怪物没音效、打击没声音」。原版是**逐类一套音**。
        //
        // 键名规则 = `monster_{hit|atk|die|step}_{原版 MonStats.Code 小写}`（8 类）：
        //   fa=Fallen / fs=FallenShaman / si=QuillRat(尖刺鼠) / zm=Zombie /
        //   ye=Brute(野兽) / cr=CorruptRogue / bk=BloodHawk(血鹰) / wr=Wraith。
        //   ⚠ 代码 → 类别的**唯一出处** = `MonStats.txt` 的 `Code` 列 × `MonSound` 列
        //     （`原版资源/d2lod1.10txt-1.10f/data/global/excel/MonStats.txt`），
        //     ⛔ 不是凭名字猜的 —— 例：`bk` 是 **foulcrow（血鹰）**，`ye` 才是 brute。
        //   音效条目名出处 = `MonSounds.txt`；目标文件出处 = `Sounds.txt` 的 `FileName` 列
        //     （受击音的真名是 `gethit1.wav`，⛔ **不是** `hit1.wav`）。
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>沉沦魔受击（`fallen_hit_1`）。</summary>
        public const string MonsterHitFa = "monster_hit_fa";

        /// <summary>沉沦魔攻击（`fallen_attack_1`）。</summary>
        public const string MonsterAtkFa = "monster_atk_fa";

        /// <summary>沉沦魔死亡（`fallen_death_1`）。</summary>
        public const string MonsterDieFa = "monster_die_fa";

        /// <summary>沉沦魔脚步（`light_walk_dirt_1`）。</summary>
        public const string MonsterStepFa = "monster_step_fa";

        /// <summary>沉沦魔萨满受击（`fallenshaman_hit_1`）。</summary>
        public const string MonsterHitFs = "monster_hit_fs";

        /// <summary>沉沦魔萨满攻击（`fallenshaman_attack_1`）。</summary>
        public const string MonsterAtkFs = "monster_atk_fs";

        /// <summary>沉沦魔萨满死亡（`fallenshaman_death_1`）。</summary>
        public const string MonsterDieFs = "monster_die_fs";

        /// <summary>沉沦魔萨满脚步（`light_walk_dirt_1`）。</summary>
        public const string MonsterStepFs = "monster_step_fs";

        /// <summary>尖刺鼠受击（`spikefiend_hit_1`）。</summary>
        public const string MonsterHitSi = "monster_hit_si";

        /// <summary>尖刺鼠攻击（`spikefiend_attack_1`；原版 `MonSounds` 无脚步）。</summary>
        public const string MonsterAtkSi = "monster_atk_si";

        /// <summary>尖刺鼠死亡（`spikefiend_death_1`）。</summary>
        public const string MonsterDieSi = "monster_die_si";

        /// <summary>僵尸受击（`zombie_hit_1`）。</summary>
        public const string MonsterHitZm = "monster_hit_zm";

        /// <summary>僵尸攻击（`zombie_attack_1`）。</summary>
        public const string MonsterAtkZm = "monster_atk_zm";

        /// <summary>僵尸死亡（`zombie_death_1`）。</summary>
        public const string MonsterDieZm = "monster_die_zm";

        /// <summary>僵尸脚步（`light_walk_dirt_1`）。</summary>
        public const string MonsterStepZm = "monster_step_zm";

        /// <summary>野兽受击（`yeti_hit_1`；`MonStats.Code=YE` 的 Brute 系）。</summary>
        public const string MonsterHitYe = "monster_hit_ye";

        /// <summary>野兽攻击（`yeti_attack_1`）。</summary>
        public const string MonsterAtkYe = "monster_atk_ye";

        /// <summary>野兽死亡（`yeti_death_1`）。</summary>
        public const string MonsterDieYe = "monster_die_ye";

        /// <summary>野兽脚步（`heavy_walk_dirt_1`）。</summary>
        public const string MonsterStepYe = "monster_step_ye";

        /// <summary>腐化罗格受击（`corrupt_hit_1`）。</summary>
        public const string MonsterHitCr = "monster_hit_cr";

        /// <summary>腐化罗格攻击（`corrupt_attack_1`）。</summary>
        public const string MonsterAtkCr = "monster_atk_cr";

        /// <summary>腐化罗格死亡（`corrupt_death_1`，文件是 `die1.wav`）。</summary>
        public const string MonsterDieCr = "monster_die_cr";

        /// <summary>腐化罗格脚步（`medium_walk_dirt_1`）。</summary>
        public const string MonsterStepCr = "monster_step_cr";

        /// <summary>血鹰受击（`hawk_hit_1`；`MonStats.Code=BK` 的 FoulCrow 系）。</summary>
        public const string MonsterHitBk = "monster_hit_bk";

        /// <summary>血鹰攻击（`hawk_attack_1`）。</summary>
        public const string MonsterAtkBk = "monster_atk_bk";

        /// <summary>血鹰死亡（`hawk_death_1`）。</summary>
        public const string MonsterDieBk = "monster_die_bk";

        /// <summary>血鹰振翅（`hawk_wing_1` = `MonSounds.FootstepLayer`，飞行怪没有脚步）。</summary>
        public const string MonsterStepBk = "monster_step_bk";

        /// <summary>幽灵受击（`wraith_hit_1`；原版 `MonSounds` 无脚步）。</summary>
        public const string MonsterHitWr = "monster_hit_wr";

        /// <summary>幽灵攻击（`wraith_attack_1`）。</summary>
        public const string MonsterAtkWr = "monster_atk_wr";

        /// <summary>幽灵死亡（`wraith_death_1`）。</summary>
        public const string MonsterDieWr = "monster_die_wr";

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
