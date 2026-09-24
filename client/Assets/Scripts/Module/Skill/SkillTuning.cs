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
    }
}
