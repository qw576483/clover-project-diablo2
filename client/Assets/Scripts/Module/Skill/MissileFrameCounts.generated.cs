// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · Module/Skill/MissileFrameCounts.generated.cs  【生成文件，勿手改】
// 由 `python tools/d2codec/export_missiles.py` 写出（数据源 = 原版 `data\global\missiles\
// <CelFile>.dcc` 的每方向帧数，与本目录下 `<CelFile>/` 里同名的 `{方向}_{帧号}.png` 一一对应）。
//
// 为什么需要它：`ProjectileView` 要按"飞行距离 ÷ 速度"算帧号并**回绕**，回绕上界就是这里的帧数；
//   猜一个上界 ⇒ 帧键指向不存在的图 ⇒ 静默退成占位方块。
// 与 `manifest.json`（同目录产物）必须一致：改任何一侧都要重跑导出器。
// ─────────────────────────────────────────────────────────────────────────────

using System;
using System.Collections.Generic;

namespace Diablo2.Module.Skill
{
    /// <summary>投射物 CelFile → 每方向帧数（原版 `.dcc` 帧数 = 官方 `Missiles.txt` 的 `AnimLen`）。</summary>
    internal static class MissileFrameCounts
    {
        /// <summary>未登记 CelFile 的兜底帧数（= 1；调用方打一次 Warn）。</summary>
        public const int Fallback = 1;

        // 查表 = 大小写不敏感：素材键历史上有全小写（"firebolt"）与帧表键
        // （"Firebolt"）不一致的坑 ⇒ 查不到会静默退成程序化环/盘（见 `SkillCatalog` 段头注释）。
        //
        // ⚠️ 去重：帧表里同一张图会有两种写法（实测 `IceBolt` 与 `Icebolt`）
        //   ⇒ 直接塞进 `OrdinalIgnoreCase` 字典会抛 `ArgumentException`（已添加了具有相同键的项），
        //   而**静态初始化器抛异常会让整个类型永久不可用**（此后每次访问都抛
        //   `TypeInitializationException`）⇒ `ProjectileView`/`SkillFxView` 全线 TryCreate 失败并吞掉
        //   异常 ⇒ 所有投射物与技能特效"只有逻辑、没有画面"（实测「什么技能都没有效果」）。
        //   ⇒ 这里先落 `Ordinal` 原表、再按大小写不敏感**去重**（重复键保留先出现的一条）。
        //   ⛔ 别再改回"直接 new Dictionary(OrdinalIgnoreCase) { ... }"。
        private static readonly Dictionary<string, int> Counts = BuildCounts();

        private static Dictionary<string, int> BuildCounts()
        {
            var raw = new Dictionary<string, int>(StringComparer.Ordinal)
            {
            { "Arrow", 1 },
            { "BAYellShockWave01", 15 },
            { "Blizzard", 8 },
            { "BloodSmall01", 9 },
            { "BoneCast", 16 },
            { "BoneSpear", 6 },
            { "BoneSpirit", 8 },
            { "ChargedBolt", 10 },
            { "CorpseExplodeGuts", 13 },
            { "CorpseExplodeGutsPoison", 13 },
            { "CurseArea", 20 },
            { "CurseCast", 25 },
            { "ElectricNova", 13 },
            { "ExpArrowExplode", 16 },
            { "FireArrow", 8 },
            { "Fireball", 5 },
            { "Firebolt", 5 },
            { "Flamethrower", 15 },
            { "Flamethrower2", 15 },
            { "FrostNova", 14 },
            { "GlacialSpike", 6 },
            { "Gleam", 8 },
            { "GrimWardLargeStart", 8 },
            { "GrimWardMediumStart", 8 },
            { "GrimWardSmallStart", 5 },
            { "HolyBoltMissile", 16 },
            { "IceArrow", 8 },
            { "IceBlast", 5 },
            { "IceOrb", 16 },
            { "Icebolt", 6 },
            { "Javelin", 1 },
            { "LightningJavelin", 5 },
            { "LightningStrike", 8 },
            { "LightningboltBig", 8 },
            { "MeteorTargetIndicator", 17 },
            { "PoisonSparks", 31 },
            { "RedemptionGhost", 16 },
            { "Revive", 25 },
            { "SafeArrow", 8 },
            { "SpikeFiendMissle", 2 },
            { "WarcryShockwave", 16 },
            { "XBowBolt", 1 },
            { "blessedhammer", 6 },
            { "groundFireBig", 37 },
            { "groundFireMedium", 37 },
            { "groundFireSmall", 37 },
            { "icestormfallvar01", 6 },
            { "icestormimpactvar01", 6 },
            { "poisonNova", 30 },
            { "teethMissile", 30 },
            { "icestormfallvar02", 6 },
            { "icestormfallvar03", 6 },
            { "icestormfallvar04", 6 },
            { "icestormimpactvar02", 6 },
            { "Meteor", 12 },
            { "MeteorTail", 12 },
            { "Teleport", 18 },
            { "FrozenArmor", 24 },
            { "FireEnchant", 17 },
            { "ManaShieldBall", 8 },
            { "ThunderstormCast", 10 },
            { "FireCast_for_Sorceress", 14 },
            { "IceCastNew01", 15 },
            { "LightningCast", 10 },
            { "LightningCastRunesFront", 10 },
            };

            var map = new Dictionary<string, int>(raw.Count, StringComparer.OrdinalIgnoreCase);
            foreach (var kv in raw)
                if (!map.ContainsKey(kv.Key)) map.Add(kv.Key, kv.Value);
            return map;
        }

        /// <summary>该 CelFile 的每方向帧数（未登记 ⇒ <see cref="Fallback"/>）。</summary>
        public static int Of(string celFile)
        {
            if (string.IsNullOrEmpty(celFile)) return Fallback;
            return Counts.TryGetValue(celFile, out var n) ? n : Fallback;
        }

        /// <summary>该 CelFile 是否已登记（调用方据此决定要不要打一次 Warn）。</summary>
        public static bool Has(string celFile)
        {
            return !string.IsNullOrEmpty(celFile) && Counts.ContainsKey(celFile);
        }
    }
}
