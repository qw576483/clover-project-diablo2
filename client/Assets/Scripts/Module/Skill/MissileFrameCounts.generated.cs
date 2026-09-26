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

        private static readonly Dictionary<string, int> Counts =
            new Dictionary<string, int>(StringComparer.Ordinal)
        {
            { "Arrow", 1 },
            { "groundFireBig", 37 },
            { "BloodSmall01", 9 },
            { "BoneCast", 16 },
            { "ChargedBolt", 10 },
            { "IceArrow", 8 },
            { "CorpseExplodeGuts", 13 },
            { "CurseArea", 20 },
            { "CurseCast", 25 },
            { "FireArrow", 8 },
            { "ExpArrowExplode", 16 },
            { "Fireball", 5 },
            { "Firebolt", 5 },
            { "groundFireMedium", 37 },
            { "groundFireSmall", 37 },
            { "FrostNova", 14 },
            { "HolyBoltMissile", 16 },
            { "BAYellShockWave01", 15 },
            { "IceBlast", 5 },
            { "Icebolt", 6 },
            { "Flamethrower", 15 },
            { "Flamethrower2", 15 },
            { "LightningJavelin", 5 },
            { "SafeArrow", 8 },
            { "XBowBolt", 1 },
            { "ElectricNova", 13 },
            { "Javelin", 1 },
            { "Gleam", 8 },
            { "SpikeFiendMissle", 2 },
            { "teethMissile", 30 },
        };

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
