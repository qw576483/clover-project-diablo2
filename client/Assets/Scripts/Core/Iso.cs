// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · Core/Iso.cs
// 等距（isometric）投影的**项目门面**（`tools/ai-skill/conventions.md` §坐标与等距投影）。
//
//   `CloverEngine.IsoLayout`（同源实现，逐行照搬本项目原 `Core/Iso.cs` 的算法与边界处理）。
//   本文件**只留门面**，两类内容：
//     · **保留在项目侧**（引擎刻意不做，属项目语义）：`HalfW` / `HalfH` / `TileWorldWidth` /
//       `TileWorldHeight` 四个常量（值来自 `GameConst`）；带 `layerOffset` 的重载（层偏移口径是
//       本项目的 `GameConst.LayerOffset*`，由调用方传入，引擎不预设层语义）。
//     · **转发给引擎**（一行语义都不加）：正/逆投影、屏幕取格、排序基准、格间距、8 方向
//       ⇒ `IsoLayout` 的对应成员。
//   ⇒ **公开签名一个都没改**（调用点遍布 `Module/**`、`UI/**` 与各离线宿主，全部零改动）。
//
// 坐标口径（权威说明在引擎 `Runtime/Core/IsoLayout.cs` 文件头）：
//   逻辑坐标 = 格子坐标（整数格，`Vector2Int`）；渲染时才做等距投影。
//   格子 (gx, gy) 覆盖逻辑方形 [gx, gx+1] × [gy, gy+1]，**中心** = (gx+0.5, gy+0.5)
//
//   正投影（格 → 世界）：x = (gx - gy) * HalfW ；y = -(gx + gy + 1) * HalfH
//   逆投影（世界 → 格）：反解上面的线性方程组，再 **FloorToInt**
//
// 逆投影必须 `Mathf.FloorToInt`（`constraints.md` #4）：C# 的 `(int)` 强转对**负数向零截断**，
//    会导致「图外可走」「格子错半格」。
// 深度排序必须随格子变化（`constraints.md` #5`），用 <see cref="SortOrder(Vector2Int)"/>。
// ─────────────────────────────────────────────────────────────────────────────

using CloverEngine;
using Diablo2.Def;
using UnityEngine;

// 必须留这个别名：`CloverEngine` 与 `Diablo2.Def` **都有一个 `Dir8`**（引擎自带的那份见
//    `Runtime/Core/Dir8.cs`），同时 `using` 两个命名空间会让裸 `Dir8` 变成 CS0104 二义。
//    用别名把裸 `Dir8` 定为**项目枚举**，
//    引擎枚举一律写成 `CloverEngine.Dir8`（只在下面 `DirectionDelta` 的转发里出现一次）。
using Dir8 = Diablo2.Def.Dir8;

namespace Diablo2.Core
{
    /// <summary>等距投影正/逆变换、深度排序、屏幕取格（**薄门面**，实现在 <see cref="IsoLayout"/>）。</summary>
    public static class Iso
    {
        /// <summary>X 轴半格宽（世界单位）：`IsoTilePxW/2/PPU` = 1.0。</summary>
        public const float HalfW = GameConst.IsoHalfW;

        /// <summary>Y 轴半格高（世界单位）：`IsoTilePxH/2/PPU` = 0.5。</summary>
        public const float HalfH = GameConst.IsoHalfH;

        /// <summary>一张等距菱形瓦片在世界里的宽（= 2*HalfW = 2）。</summary>
        public const float TileWorldWidth = HalfW * 2f;

        /// <summary>一张等距菱形瓦片在世界里的高（= 2*HalfH = 1）。</summary>
        public const float TileWorldHeight = HalfH * 2f;

        /// <summary>
        /// 本项目的等距布局参数（**引擎侧不引入任何项目常量**，四个值全部由这里显式传入）。
        /// 无状态纯函数 ⇒ 单实例即可，所有转发都是无副作用的。
        /// </summary>
        private static readonly IsoLayout Layout = new IsoLayout(
            GameConst.IsoHalfW, GameConst.IsoHalfH, GameConst.SortOrderStep, GameConst.SortOrderBase);

        // ── 正投影：格 → 世界 ───────────────────────────────────────────────
        /// <summary>格子**中心**的世界坐标（z = 0，2D 平面）。</summary>
        public static Vector3 GridToWorld(Vector2Int g) => GridToWorld(g.x, g.y);

        /// <summary>格子**中心**的世界坐标（z = 0，2D 平面）。</summary>
        public static Vector3 GridToWorld(int gx, int gy) => Layout.GridToWorld(gx, gy);

        /// <summary>格子左上角（格坐标原点）的世界坐标 —— 铺装瓦片时对齐用。</summary>
        public static Vector3 GridOriginToWorld(Vector2Int g) => GridOriginToWorld(g.x, g.y);

        /// <summary>格子左上角（格坐标原点）的世界坐标 —— 铺装瓦片时对齐用。</summary>
        public static Vector3 GridOriginToWorld(int gx, int gy) => Layout.GridOriginToWorld(gx, gy);

        // ── 逆投影：世界 → 格 ───────────────────────────────────────────────
        /// <summary>世界坐标 → 格子坐标（**FloorToInt**；世界 z 分量被忽略）。</summary>
        public static Vector2Int WorldToGrid(Vector3 world) => Layout.WorldToGrid(world);

        /// <summary>连续的格坐标（不取整，供插值/插值动画用）。</summary>
        public static Vector2 WorldToGridContinuous(Vector3 world) => Layout.WorldToGridContinuous(world);

        // ── 屏幕 → 世界/格 ──────────────────────────────────────────────────
        /// <summary>
        /// 屏幕坐标 → **地面（z = 0）**的世界坐标。
        /// 必须显式给「相机到地面的距离」，否则点击位置整体偏移（`constraints.md` #6）；
        /// 本项目相机固定等距且为**正交**，距离 = `-camera.transform.position.z`。
        /// </summary>
        /// <param name="cam">主相机（**必须正交**）；为 null 时返回零向量并限频告警。</param>
        /// <param name="screenPos">屏幕坐标（`Game.Input.MousePosition`）。</param>
        public static Vector3 ScreenToWorldOnGround(Camera cam, Vector3 screenPos)
            => Layout.ScreenToWorldOnGround(cam, screenPos);

        /// <summary>屏幕坐标 → 格子坐标（等距逆投影，负数用 Floor）。</summary>
        public static Vector2Int ScreenToGrid(Camera cam, Vector3 screenPos) => Layout.ScreenToGrid(cam, screenPos);

        // ── 深度排序 ────────────────────────────────────────────────────────
        /// <summary>
        /// 该格子的排序基准：`(gx + gy) * GameConst.SortOrderStep + GameConst.SortOrderBase`。
        /// 同一 `gx+gy` 的格按 gy 依次递增，保证「后方物件盖住前方角色」。
        /// </summary>
        public static int SortOrder(Vector2Int g) => SortOrder(g.x, g.y);

        /// <summary>该格子的排序基准。</summary>
        public static int SortOrder(int gx, int gy) => Layout.SortOrder(gx, gy);

        /// <summary>该格子 + 层偏移的排序值（层偏移用 `GameConst.LayerOffset*`，由调用方给）。</summary>
        public static int SortOrder(Vector2Int g, int layerOffset) => SortOrder(g) + layerOffset;

        /// <summary>该格子 + 层偏移的排序值。</summary>
        public static int SortOrder(int gx, int gy, int layerOffset) => SortOrder(gx, gy) + layerOffset;

        /// <summary>
        /// **实体（角色 / 怪物 / 地面物品 / 飞行物）节点的排序值** = 该格基准 + 实体层偏移；
        /// <paramref name="isDeck"/>（该格是桥面/平台，判定 = `IMapModule.IsDeckGrid`，
        /// 登记表 = `Module/Map/DeckTiles`）**不再单独抬档** ⇒ 与普通实体格同值，
        /// 桥面格的遮挡关系由原版口径（格 y 越大越靠前）决定：站桥面南行（y=27）的实体被
        /// 正南一格（y=28）的栏杆 `4(D+1)+101 = 4D+105` 盖住腿脚；站北行（y=26）时栏杆在本格。
        ///
        /// <para>本方法是**纯函数**（不查地图、不碰渲染）⇒ 离线宿主（`mapcheck` §21 /
        /// `movecheck` §11）可逐格断言"桥面实体 == 普通实体档 且 &lt; 正南一格物件层"。</para>
        /// </summary>
        public static int EntitySortOrder(Vector2Int g, bool isDeck)
            => SortOrder(g, isDeck ? GameConst.LayerOffsetDeckEntity : GameConst.LayerOffsetEntity);

        // ── 距离与方向 ──────────────────────────────────────────────────────
        /// <summary>格间**八向步数**（Chebyshev 距离）—— 8 邻接寻路/射程判定用它。</summary>
        public static int GridDistance(Vector2Int a, Vector2Int b) => Layout.GridDistance(a, b);

        /// <summary>格间欧氏距离（世界单位，格边长 = 1）—— 手感类距离（拾取/对话）用它。</summary>
        public static float GridDistanceEuclidean(Vector2Int a, Vector2Int b) => Layout.GridDistanceEuclidean(a, b);

        /// <summary>格间欧氏距离（含小数坐标，移动插值中判定用）。</summary>
        public static float GridDistanceEuclidean(Vector2 a, Vector2 b) => Layout.GridDistanceEuclidean(a, b);

        /// <summary>是否为 8 邻接（含同格）。</summary>
        public static bool IsAdjacent(Vector2Int a, Vector2Int b) => GridDistance(a, b) <= 1;

        /// <summary>
        /// 格增量 → 8 方向朝向（**格空间增量 → 屏幕上的朝向**）。
        /// 权威表与推导在引擎 <see cref="IsoLayout.DirectionTo(Vector2Int)"/>（本方法只转发），此处照抄：
        /// <code>
        ///   (0, +1) → SW   世界位移 (−HalfW, −HalfH) ⇒ 屏幕左下
        ///   (0, -1) → NE   屏幕右上
        ///   (+1,  0) → SE   屏幕右下
        ///   (-1,  0) → NW   屏幕左上
        ///   (+1, +1) → S    屏幕正下      (-1, -1) → N    屏幕正上
        ///   (+1, -1) → E    屏幕正右      (-1, +1) → W    屏幕正左
        /// </code>
        /// 推导：`GridToWorld` = `x=(gx−gy)·HalfW`、`y=−(gx+gy+1)·HalfH`
        /// ⇒ `Δworld = ((Δgx−Δgy)·HalfW, −(Δgx+Δgy)·HalfH)`，故 `(0,+1)` 是左下(SW)、`(+1,+1)` 才是正下(S)。
        /// <para>增量取符号后比较，故 (3, 7) 与 (1, 1) 结果相同。</para>
        /// <para>自洽判据：<c>DirectionDelta(DirectionTo(delta))</c> 与 <c>delta</c> 的**符号方向一致**。</para>
        /// <para>**枚举映射**：算法在引擎 <see cref="IsoLayout.DirectionTo(Vector2Int)"/>，
        /// 返回引擎 <see cref="CloverEngine.Dir8"/>。这里做的是**逐值直转**
        /// （`CloverEngine.Dir8` 与 `Diablo2.Def.Dir8` **必须同序同值**：
        /// `S=0 SW=1 W=2 NW=3 N=4 NE=5 E=6 SE=7`）——
        /// 两枚举只要有一项错位，朝向就会整体错位**且不会报错**（表现为「人物朝向看着别扭」）。
        /// 新增方向时**两个枚举都要改**，改完到本行确认顺序。</para>
        /// </summary>
        public static Dir8 DirectionTo(Vector2Int delta) => (Dir8)(int)Layout.DirectionTo(delta);

        /// <summary>从 <paramref name="from"/> 指向 <paramref name="to"/> 的 8 方向（同格按 S）。</summary>
        public static Dir8 DirectionTo(Vector2Int from, Vector2Int to) => (Dir8)(int)Layout.DirectionTo(from, to);

        /// <summary>朝向 → 格增量（<see cref="DirectionTo(Vector2Int)"/> 的逆，用于「朝前移动一格」）。</summary>
        public static Vector2Int DirectionDelta(Dir8 dir) => Layout.DirectionDelta((CloverEngine.Dir8)(int)dir);
    }
}
