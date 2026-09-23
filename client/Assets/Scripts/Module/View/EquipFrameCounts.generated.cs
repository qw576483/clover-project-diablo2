// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · Module/View/EquipFrameCounts.generated.cs
// ⚠️ **本文件是生成物，禁止手改**（生成器：`tools/probes/measure/gen_equip_frame_counts.py`）。
//
// 数据来源 = 各装备外观套目录 `Chars/{class}/equip/{key}/manifest.json` 的
//   `actions.{action}.frames`（= 原版 `.cof` 的 `framesPerDirection`；导出器
//   `tools/d2codec/export_chars.py --equip-sets` 写盘）。原版素材取自
//   Diablo II (Blizzard North, 2000) 的 d2char.mpq / d2data.mpq，本项目非商用。
//
// 为什么必须生成（= 为什么不能沿用 `SpriteFrameCounts` 的徒手帧数）：
//   原版**逐单位 × 逐动作**帧数都不同，而同一个职业**装上武器后帧数还会变**
//   （实测：amazon 徒手 attack=13，`amazon/equip/jav` attack=15；barbarian 徒手 attack=12，
//    `barbarian/equip/hax` attack=16）。沿用徒手帧数 ⇒ 帧键指向不存在的图 ⇒ 静默退成纯色占位。
//
// 键（unitKey）= `"{class}/equip/{key}"`（小写；与 `SpriteFrames.UnitKeyOfEquip` 同值，
//   `key` = 装备外观套目录名，见 `Module/View/EquipVisual.KeyOf` 的拼法）。
// 值 = 7 个动作的帧数，**下标 = `ViewAnim`**（idle=0 / walk=1 / attack=2 / cast=3 /
//   hit=4 / death=5 / run=6）——顺序与 `ActionNames` 逐字一致。
//
// ⛔ 重跑口径：素材变了（补导同框套 `jav_buc` / `hax_buc`、或重导某套）⇒ 重跑生成器；
//    手改必然与磁盘上的 PNG 数量对不上。
// ─────────────────────────────────────────────────────────────────────────────

using System.Collections.Generic;

namespace Diablo2.Module.View
{
    /// <summary>装备外观套的逐动作真实帧数（生成物，见文件头）。</summary>
    internal static class EquipFrameCounts
    {
        /// <summary>动作名（下标顺序 = <see cref="ViewAnim"/>，与 <see cref="SpriteFrames.Keys"/> 拼法一致）。</summary>
        public static readonly string[] ActionNames =
        {
            "idle", "walk", "attack", "cast", "hit", "death", "run",
        };

        /// <summary>
        /// `装备外观套 unitKey（"{class}/equip/{key}"） → 7 个动作的帧数`。
        /// <para>**只登记磁盘上真的存在的套**：某 key 不在表里 ⇒ `SpriteFrames` 按
        /// `EquipVisual.FallbackChain` 逐级回退（`{w}_{s}` → `{w}` → `{s}` → 徒手）。</para>
        /// </summary>
        public static readonly Dictionary<string, int[]> ByUnit =
            new Dictionary<string, int[]>
            {
                { "amazon/equip/buc", new[] { 8, 8, 15, 20, 6, 23, 8 } },
                { "amazon/equip/jav", new[] { 8, 8, 15, 20, 6, 23, 8 } },
                { "amazon/equip/jav_buc", new[] { 8, 8, 15, 20, 6, 23, 8 } },
                { "barbarian/equip/buc", new[] { 8, 8, 16, 14, 5, 27, 8 } },
                { "barbarian/equip/hax", new[] { 8, 8, 16, 14, 5, 27, 8 } },
                { "barbarian/equip/hax_buc", new[] { 8, 8, 16, 14, 5, 27, 8 } },
                { "necromancer/equip/wnd", new[] { 8, 8, 19, 16, 7, 27, 8 } },
                { "paladin/equip/buc", new[] { 8, 10, 15, 16, 5, 28, 8 } },
                { "paladin/equip/ssd", new[] { 8, 10, 15, 16, 5, 28, 8 } },
                { "sorceress/equip/sst", new[] { 8, 8, 18, 14, 8, 24, 8 } },
            };

        /// <summary>该装备外观套是否已导出（有 manifest ⇒ 生成物里就有一行）。</summary>
        public static bool Has(string unitKey)
        {
            return !string.IsNullOrEmpty(unitKey) && ByUnit.ContainsKey(unitKey.ToLowerInvariant());
        }

        /// <summary>取某套某动作的帧数；未登记 / 越界返回 0（调用方按"缺图"处理）。</summary>
        public static int Of(string unitKey, ViewAnim anim)
        {
            if (string.IsNullOrEmpty(unitKey)) return 0;
            int[] counts;
            if (!ByUnit.TryGetValue(unitKey.ToLowerInvariant(), out counts)) return 0;
            var i = (int)anim;
            if (counts == null || i < 0 || i >= counts.Length) return 0;
            return counts[i];
        }
    }
}
