// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · Module/View/SpriteFrameCounts.cs
// ⚠️ **本文件是生成物，禁止手改**（生成器：`tools/d2codec/export_chars.py --emit-cs`）。
//
// 数据来源 = 原版 `.cof` 的 `framesPerDirection`（逐动作一份，见各单位目录的 manifest.json）；
//   原版素材取自 Diablo II (Blizzard North, 2000) 的 d2char.mpq / d2data.mpq，本项目非商用。
//
// 为什么必须有这张表：`SpriteFrames.FrameCounts` 是按**动作**给一个数组的，
//   而原版**每个单位的每个动作帧数都不同**（例：亚马逊 NU=8 帧、DT=15 帧；
//   堕落者 TR 层只有 6 帧的走路）⇒ 用一份全局常量会让"帧键"指向不存在的图（静默缺图）。
// ⛔ 改这张表 = 重跑导出器；手改必然与磁盘上的 PNG 数量对不上。
// ─────────────────────────────────────────────────────────────────────────────
// >>> 片2b NOTE（**非生成物**：本块由 agent 追加，只说明 `run` 这一列的来路，可整块删）
//   · 本列的值 = 各单位目录 `manifest.json` 的 `actions.run.frames`（= 片 2a 重导素材时的
//     **实测**：5 个职业各 `8`、`zm` = `8`、`cr` = `10`；其余单位原版没有 `RN` ⇒ `0`）。
//   · 本次是**用生成器自己的 `emit_cs()`** 对着磁盘上那 18 份 manifest.json 重出的
//     （`export_chars.py --emit-cs` 整条命令要先读 MPQ，而本机 `原版资源/storm.py:11` 硬编码的
//      StormLib DLL 路径已失效 ⇒ 该命令跑不动，只能直接驱动它的 `emit_cs()`；口径同一份函数）。
//   · 重跑生成器（`python tools/d2codec/export_chars.py --emit-cs <本文件>`）应得到**同样结果**：
//     片 2b 已把 `CS_ACTIONS` 打开成 7 列（= `ViewAnim` 含 `Run`），本文件的 7 列口径与之一致。
// <<< 片2b NOTE

using System.Collections.Generic;

namespace Diablo2.Module.View
{
    /// <summary>逐单位 / 逐动作的真实帧数（生成物，见文件头）。</summary>
    internal static class SpriteFrameCounts
    {
        /// <summary>动作名（与 <see cref="SpriteFrames.Keys"/> 的拼法一致）的下标顺序。</summary>
        public static readonly string[] ActionNames =
        {
            "idle", "walk", "attack", "cast", "hit", "death", "run",
        };

        /// <summary>
        /// `单位目录键 → 7 个动作的帧数`（下标同 <see cref="ActionNames"/>）。
        /// 单位目录键 = `ResPaths.CharDir` / `ResPaths.MonsterDir` 的实参（小写）。
        /// </summary>
        public static readonly Dictionary<string, int[]> ByUnit =
            new Dictionary<string, int[]>
            {
                { "amazon", new[] { 8, 8, 13, 20, 6, 23, 8 } },   // am
                { "sorceress", new[] { 8, 8, 16, 14, 8, 24, 8 } },   // so
                { "necromancer", new[] { 8, 8, 15, 16, 7, 27, 8 } },   // ne
                { "paladin", new[] { 8, 10, 14, 16, 5, 28, 8 } },   // pa
                { "barbarian", new[] { 8, 8, 12, 14, 5, 27, 8 } },   // ba
                { "fa", new[] { 20, 10, 10, 0, 7, 20, 0 } },   // fa
                { "fs", new[] { 12, 14, 17, 0, 5, 21, 0 } },   // fs
                { "si", new[] { 8, 9, 16, 0, 6, 14, 0 } },   // si
                { "zm", new[] { 8, 12, 16, 0, 5, 19, 8 } },   // zm
                { "cr", new[] { 13, 12, 16, 0, 9, 24, 10 } },   // cr
                { "bk", new[] { 11, 8, 11, 0, 5, 23, 0 } },   // bk
                { "ye", new[] { 8, 12, 12, 0, 6, 18, 0 } },   // ye
                { "wr", new[] { 12, 14, 18, 16, 8, 19, 0 } },   // wr
                { "ps", new[] { 13, 8, 0, 0, 0, 0, 0 } },   // ps
                { "rc", new[] { 12, 8, 0, 0, 0, 0, 0 } },   // rc
                { "ci", new[] { 13, 8, 0, 0, 0, 0, 0 } },   // ci
                { "gh", new[] { 8, 8, 0, 0, 0, 0, 0 } },   // gh
                { "wa", new[] { 16, 12, 0, 0, 0, 0, 0 } },   // wa
            };

        /// <summary>取某单位某动作的帧数；未登记返回 0（调用方按"缺图"处理）。</summary>
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
