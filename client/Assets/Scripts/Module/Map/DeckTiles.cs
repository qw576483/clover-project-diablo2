// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · Module/Map/DeckTiles.cs
// **「deck（可走上方的结构：桥面 / 平台 / 甲板）」地面瓦片包的唯一判据**。
//
// 为什么单独一个文件：`Contracts.IMapModule.IsDeckGrid` 的数据来源必须只有一处
//   （`Module/Map/GridMap.SetTiles` 登记时判一次、`mapcheck` 断言时判一次），
//   ⛔ 不许在视图层或宿主机里各写一份"哪些包算桥面"。
//
// 出处（⛔ 不是本项目自己造的类）：
//   · 罗格营地出城那座桥的地砖 = 原版 `OUTDOORS/bridge.dt1`，打表包名
//     `MapGenTownLayout.Packs[0] = "moor_bridge"`（生成物：`tools/d2codec/export_town_layout.py`）；
//     `MapGenTownLayout.GroundRows` 里桥面 4 行（y=25..28, x=46..55）的 6 字符编码前 3 位 = `000`。
//   · 野外布局 `MapGenWildLayout.Packs` 里同名包在下标 2（野外的桥用同一份 dt1）。
// ─────────────────────────────────────────────────────────────────────────────

namespace Diablo2.Module.Map
{
    /// <summary>deck（桥面/平台）地面瓦片包的判据（唯一出处；见文件头）。</summary>
    internal static class DeckTiles
    {
        /// <summary>
        /// deck 类**地面瓦片包名**。判据口径 = 「该格的地砖取自这份原版 dt1」，
        /// 与格子坐标无关（⛔ 不按 x/y 区间硬编码）。
        /// </summary>
        private static readonly string[] DeckGroundPacks =
        {
            "moor_bridge",     // 原版 `OUTDOORS/bridge.dt1`（罗格营地出城那座桥）
        };

        /// <summary>
        /// 该**地面瓦片键**是否取自 deck 类包。
        /// <para>键格式 = `&lt;pack&gt;/&lt;idx&gt;`（例：`moor_bridge/006`；出处 = `GridMap.SetTiles` /
        /// `MapGenTownLayout.Decode`）。空串 / null（原版这格不铺地面）⇒ false。</para>
        /// </summary>
        public static bool IsDeckGroundKey(string groundKey)
        {
            if (string.IsNullOrEmpty(groundKey)) return false;
            var slash = groundKey.IndexOf('/');
            var pack = slash > 0 ? groundKey.Substring(0, slash) : groundKey;
            for (var i = 0; i < DeckGroundPacks.Length; i++)
            {
                if (string.Equals(pack, DeckGroundPacks[i], System.StringComparison.Ordinal)) return true;
            }
            return false;
        }
    }
}
