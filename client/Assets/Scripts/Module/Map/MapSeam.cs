// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · Module/Map/MapSeam.cs
// **区域衔接接缝（seam）的唯一判据**（纯函数，不碰地图数据、不碰渲染）。
//
// 为什么单独一个文件（与 `DeckTiles` 同一口径）：同一条判据会被**三个地方**用到
//
// 出处（不是本项目自己编的语义）：
//   · `原版资源/d2lod1.10txt-1.10f/.../Levels.txt`：`Act 1 - Town` = SizeX 56 / SizeY 40
//     （DrlgType 2 = 预设块；`Act 1 - Wilderness 1` = 80×80 / DrlgType 3），与
//     `MapGenTownLayout` 的 56×40 一致。
//   · `tools/d2codec/export_town_layout.py` 文件头 line 98-106（逐条复核过原版引擎行为）：
//     「野外的第 0 列 = 城镇关卡的最后 1 列（共享边列）⇒ 城镇关卡东边界 = 桥的东端那列」，
//     出处 `libd2/.../drlg/outdoors/OutRoom.zig:271`。
//   ⇒ 原版「**过桥向东 = 进入野外（Blood Moor）**」；这条接缝落在**关卡最后一列上、桥东端
//     那两个可走桥面格**（实测：东边界列共 38 格可走，但除桥面这 2 格外，其余是河对岸一条
//     从出生点走不到的孤立窄条 ⇒ 若把整列都当出口，连通性自检必然失败）。
// ─────────────────────────────────────────────────────────────────────────────

using Diablo2.Def;
using UnityEngine;

namespace Diablo2.Module.Map
{
    /// <summary>区域衔接接缝的判据（见文件头出处）。</summary>
    internal static class MapSeam
    {
        /// <summary>
        /// <para>判据 = ①区域是罗格营地；②在关卡**东边界列**（x = Width-1，= 原版共享边列）；
        /// ③该格是 **deck 可走格**（桥面，`IMapModule.IsDeckGrid`，登记见 `Module/Map/DeckTiles`）
        /// —— 三个条件都是数据，不按坐标硬编码。</para>
        /// <para>只读、无副作用 ⇒ 离线宿主可逐格断言（不依赖 Unity 运行时）。</para>
        /// </summary>
        public static bool IsTownEastSeam(AreaId area, int width, Vector2Int g, bool isDeckGrid)
            => area == AreaId.Town && g.x == width - 1 && isDeckGrid;
    }
}
