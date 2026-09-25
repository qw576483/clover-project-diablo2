// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · Module/Map/GridMap.cs
// **业务侧格子地图数据**：`TileKind[,]` + 元数据（seed / 出生点 / 出口 / NPC / 刷怪点）
// + 可走查询 + A* 转发 + BFS 连通性自检。
//
//   本文件只保留**本项目语义**（`TileKind` 地形、`MapLog` 日志、地图名/尺寸口径）并通过
//   `WalkableInGraph` 回调接入 —— 引擎件不认识任何一款游戏的地形枚举。
//
// 本项目**不做** CloverMap（引擎二进制地图）的解析与生成，也**不使用** `Game.Map`：
//    原版野外/地牢每局随机生成，与引擎「静态烘焙地图」语义不符
//    （`tools/ai-skill/conventions.md` §与全局 skill 的差异）。
//    本项目地图 = 纯业务格子数据，**未重造任何二进制格式**。
//
// 可走性**唯一判定**在 `Def.TileKindInfo.IsWalkable`（不在本文件重写判等表）。
// 坐标 (gx, gy)：`_tiles[gx, gy]`，x ∈ [0,Width)、y ∈ [0,Height)。
//    等距投影由 `Core/Iso`（项目门面，实现在引擎 `CloverEngine.IsoLayout`）负责，本文件**不涉及**任何渲染。
// ─────────────────────────────────────────────────────────────────────────────

using System;
using System.Collections.Generic;
using CloverEngine;
using Diablo2.Core;
using Diablo2.Def;
using UnityEngine;

namespace Diablo2.Module.Map
{
    /// <summary>格子地图数据与查询（无 MonoBehaviour、无渲染）。</summary>
    public sealed class GridMap
    {
        // ── 格子算法底座（实现 = 引擎件 `CloverEngine.GridGraph`：8 邻接表 / BFS / 孤立口袋填充 /
        //    边界环封 / 可走格缓存 / 批操作）────────────────────────────────────────
        //   这些是**任何**格子图游戏都要的通用算法，而「BFS 与 A* 的对角口径必须逐行一致」
        //   这条一旦各写一份，就会出现「可达性自检通过、寻路走不过去」的静默不一致。
        //   引擎件与 `CloverEngine.AStar` 用**同一个** `Func<Vector2Int,bool>` 契约与同一套越界口径
        //   ⇒ 本文件的 `WalkableInGraph` 可以同时喂给两者。
        // 保留下来的仍是**本项目语义**：`TileKind` / 地形名字 / `MapLog` / 地图尺寸口径 ——
        //   引擎件只吃回调，不认识任何一款游戏的地形枚举。

        private TileKind[,] _tiles;

        // ── 逐格「原版瓦片键」覆盖（罗格营地 / 邪恶洞穴用；野外不用）────────────
        // 为什么要它：`MapView` 默认按 `TileKind` 分类抽一张同类瓦片，那是"能看"的近似；
        // 而原版的营地与洞穴是**每一格都有确定的一张瓦片**（取自原版 ds1），
        // 只有逐格记住才能 1:1 还原（含"原版这格什么都不画"的黑区）。
        // 为空 = 该格按 `TileKind` 走默认瓦片（野外路径），**不是**"画空白"。
        private string[] _groundKeys;
        private string[] _objectKeys;
        private bool _tileOverrides;

        // ── 逐格「deck（可走上方的结构：桥面/平台/甲板）」标记 ───────────────────
        //   等距排序值 = `(gx+gy)*SortOrderStep + SortOrderBase + 层偏移`（`Core/GameConst.cs`）。
        //   桥面格与普通实体格**同档**（`GameConst.LayerOffsetDeckEntity` = 普通实体档）：
        //   遮挡由"格 y 越大越靠前"决定 —— 桥面南行（y=27）的实体腿脚被正南一格（y=28）的
        //   栏杆 `4(D+1)+101 = 4D+105` 盖住（正常遮挡，栏杆不透明像素自本格底边向上 79 px）。
        //   本标记的另一个用处 = 东边界接缝判定（`Module/Map/MapSeam`）。
        // 登记口径（数据驱动，不按坐标硬编码）：该格的**地面瓦片键取自 deck 类包**（`DeckTiles`）
        //   **且该格可走** —— 栏杆行的地面铺的是同一份桥面图集，但那些格不可走，不算桥面。
        private bool[] _deck;

        private bool _countsDirty = true;
        private int _blockedCount;
        private int _walkableCount;

        /// <summary>
        /// 可走格缓存（`RandomWalkableTile` 用它做 **O(1)** 抽样，避免在洞穴里瞎猜）。
        /// <para>缓存在引擎件 <see cref="GridGraph.WalkableIndex"/>：同一套
        /// 「`x` 外层升序、`y` 内层升序」的填充顺序 ⇒ 同 seed 的抽样序列**逐项可复现**。</para>
        /// </summary>
        private readonly GridGraph.WalkableIndex _walkableIndex = new GridGraph.WalkableIndex();

        // ── 尺寸与元数据 ─────────────────────────────────────────────────────
        /// <summary>宽（格，x 方向）。未生成时 = 0。</summary>
        public int Width { get; private set; }

        /// <summary>高（格，y 方向）。未生成时 = 0。</summary>
        public int Height { get; private set; }

        /// <summary>当前区域。</summary>
        public AreaId Area { get; private set; }

        /// <summary>本张图的 seed（**有效 seed**：重试后可能 ≠ 调用方传入的 seed，日志会打出两者）。</summary>
        public int Seed { get; private set; }

        /// <summary>是否已完成生成（`MapModule` 在自检通过后置位）。</summary>
        public bool Generated { get; private set; }

        /// <summary>上次生成耗时（毫秒，含全部重试）。</summary>
        public double LastGenerateMs { get; set; }

        /// <summary>上次生成实际尝试次数（1 = 一次成功）。</summary>
        public int GenerateAttempts { get; set; } = 1;

        /// <summary>出生点（玩家进图位置）。</summary>
        public Vector2Int SpawnPoint { get; set; }

        /// <summary>洞穴入口格（仅血腥荒野有效）。</summary>
        public Vector2Int? CaveEntrance { get; set; }

        /// <summary>出入口格（`TileKind.Exit`）。顺序：回城口在前、洞穴入口在后（城镇/洞穴各 1 个）。</summary>
        public readonly List<Vector2Int> Exits = new List<Vector2Int>();

        /// <summary>城镇 NPC 站位：**下标 = (int)Def.NpcId**（0=阿卡拉 … 4=瓦瑞夫）。</summary>
        public readonly List<Vector2Int> NpcPoints = new List<Vector2Int>();

        /// <summary>怪物刷新点（洞穴生成时产出；城镇/野外为空）。</summary>
        public readonly List<Vector2Int> MonsterSpawns = new List<Vector2Int>();

        /// <summary>
        /// 目前只有罗格营地有 1 个；其余区域为空列表。
        /// </summary>
        public readonly List<Vector2Int> WaypointPoints = new List<Vector2Int>();

        /// <summary>连通性自检必须可达的格（生成器填：出口 + 洞穴入口 + 所有房间中心 + 刷怪点）。</summary>
        public readonly List<Vector2Int> RequiredReachable = new List<Vector2Int>();

        // ── 障碍 / 可走统计 ──────────────────────────────────────────────────
        /// <summary>障碍格总数（含图外 `Void`）。</summary>
        public int BlockedCount
        {
            get { RecountIfNeeded(); return _blockedCount; }
        }

        /// <summary>可走格总数。</summary>
        public int WalkableCount
        {
            get { RecountIfNeeded(); return _walkableCount; }
        }

        // ═════════════════════════════════════════════════════════════════════
        // 生命周期
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>重建为指定尺寸的空图（全部 <see cref="TileKind.Void"/>），并清空全部元数据。</summary>
        public void Reset(AreaId area, int width, int height, int seed)
        {
            if (width < GameConst.MapMinSize || height < GameConst.MapMinSize)
            {
                MapLog.Warn($"Reset: 尺寸 {width}x{height} 小于硬下限 {GameConst.MapMinSize}，" +
                            "连通性/出生点净空大概率失败（调用方应当用配表 level_c 的 size_min/size_max）");
            }
            if (width > GameConst.MapMaxSize || height > GameConst.MapMaxSize)
            {
                MapLog.Warn($"Reset: 尺寸 {width}x{height} 超过硬上限 {GameConst.MapMaxSize}，瓦片铺装会明显变慢");
            }

            Area = area;
            Width = Mathf.Max(1, width);
            Height = Mathf.Max(1, height);
            Seed = seed;
            _tiles = new TileKind[Width, Height];   // 默认 (TileKind)0 = Void = 图外/未生成（不可走）
            _groundKeys = null;
            _objectKeys = null;
            _tileOverrides = false;
            _deck = new bool[Width * Height];       // 换图必须**重新分配**（否则上一张图的桥面标记会残留）

            Exits.Clear();
            NpcPoints.Clear();
            MonsterSpawns.Clear();
            WaypointPoints.Clear();
            RequiredReachable.Clear();
            CaveEntrance = null;
            SpawnPoint = new Vector2Int(Width / 2, Height / 2);
            Generated = false;
            _countsDirty = true;
        }

        /// <summary>清空（退出 Stage 时调用）。</summary>
        public void Clear()
        {
            _tiles = null;
            Width = 0;
            Height = 0;
            Seed = 0;
            Generated = false;
            LastGenerateMs = 0d;
            GenerateAttempts = 1;
            Exits.Clear();
            NpcPoints.Clear();
            MonsterSpawns.Clear();
            WaypointPoints.Clear();
            RequiredReachable.Clear();
            _walkableIndex.Clear();
            _blockedCount = 0;
            _walkableCount = 0;
            _countsDirty = true;
            _groundKeys = null;
            _objectKeys = null;
            _tileOverrides = false;
            _deck = null;                           // deck 标记随之作废（`IsDeck` 对 null 表一律 false）
            CaveEntrance = null;
            SpawnPoint = Vector2Int.zero;
        }

        /// <summary>标记生成完成（先重算统计，之后 `BlockedCount/WalkableCount` 就是最终值）。</summary>
        public void MarkGenerated()
        {
            RecountIfNeeded();
            Generated = true;
        }

        // ═════════════════════════════════════════════════════════════════════
        // 格访问
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>是否在图内。</summary>
        public bool InBounds(int x, int y) => _tiles != null && x >= 0 && y >= 0 && x < Width && y < Height;

        /// <summary>是否在图内。</summary>
        public bool InBounds(Vector2Int g) => InBounds(g.x, g.y);

        /// <summary>该格地形（图外 / 未生成 → <see cref="TileKind.Void"/>）。</summary>
        public TileKind Get(int x, int y) => InBounds(x, y) ? _tiles[x, y] : TileKind.Void;

        /// <summary>该格地形（图外 / 未生成 → <see cref="TileKind.Void"/>）。</summary>
        public TileKind Get(Vector2Int g) => Get(g.x, g.y);

        /// <summary>该格地形（`IMapModule.TileAt` 的实现）。</summary>
        public TileKind TileAt(Vector2Int g) => Get(g);

        /// <summary>该格是否可走（图外/未生成 = false；判定走 <see cref="TileKindInfo.IsWalkable"/>）。</summary>
        public bool Walkable(Vector2Int g)
        {
            if (_tiles == null)
            {
                MapLog.WarnThrottled("map.walkable.nogen", "Walkable: 地图尚未生成（Generate 未调用），一律返回 false");
                return false;
            }
            return InBounds(g) && TileKindInfo.IsWalkable(_tiles[g.x, g.y]);
        }

        /// <summary>
        /// <see cref="Walkable"/> 的**静默版**：同一个唯一口径（图外 / 未生成 = false，判定走
        /// <see cref="TileKindInfo.IsWalkable"/>），但**不打**「地图尚未生成」那条 Warn。
        /// <para>为什么要它：引擎件 `CloverEngine.GridGraph` 的 `isWalkable` 委托是**逐格**调的
        /// （BFS / 环封 / 批写 / 抽样索引），它要求**契约①「图外一律返回 false」**；而
        /// <see cref="Walkable"/> 里那条 Warn 属于**业务入口**的痕迹（调用方在未生成时问可走性）——
        /// 算法内部逐格调用不属于该分支，若走 `Walkable`，图未生成时会把一条业务 Warn 变成算法噪声。</para>
        /// </summary>
        private bool WalkableInGraph(Vector2Int g)
            => _tiles != null && InBounds(g) && TileKindInfo.IsWalkable(_tiles[g.x, g.y]);

        /// <summary>写入一格地形（图外忽略并限频告警）。</summary>
        public void Set(int x, int y, TileKind kind)
        {
            if (!InBounds(x, y))
            {
                MapLog.WarnThrottled("map.set.oob", $"Set: ({x},{y}) 在图外（图 {Width}x{Height}），忽略本次写入");
                return;
            }
            _tiles[x, y] = kind;
            _countsDirty = true;
        }

        /// <summary>写入一格地形。</summary>
        public void Set(Vector2Int g, TileKind kind) => Set(g.x, g.y, kind);

        // ── 逐格原版瓦片键覆盖（营地 / 洞穴；见字段注释）────────────────────────

        /// <summary>本图是否启用了「逐格原版瓦片键」（启用后 `MapView` 一律用覆盖值，空串 = 原版不画）。</summary>
        public bool HasTileOverrides => _tileOverrides;

        /// <summary>开启逐格瓦片键覆盖（生成器在铺格之前调用一次）。</summary>
        public void BeginTileOverrides()
        {
            if (_tiles == null) { MapLog.Error("BeginTileOverrides: 地图未 Reset"); return; }
            _groundKeys = new string[Width * Height];
            _objectKeys = new string[Width * Height];
            _tileOverrides = true;
        }

        /// <summary>写一格的**原版瓦片键**（`ResPaths.Tile` / `ObjectSprite` 的相对路径；空串 = 原版这格不画）。</summary>
        public void SetTiles(int x, int y, string groundKey, string objectKey)
        {
            if (!InBounds(x, y))
            {
                MapLog.WarnThrottled("map.settiles.oob", $"SetTiles: ({x},{y}) 在图外，忽略");
                return;
            }
            if (!_tileOverrides)
            {
                MapLog.WarnThrottled("map.settiles.noover", "SetTiles: 未先调用 BeginTileOverrides，忽略本次写入");
                return;
            }
            var i = y * Width + x;
            _groundKeys[i] = groundKey;
            _objectKeys[i] = objectKey;

            // deck 登记（**唯一判据见 `DeckTiles`**）：地面键取自 deck 类包 **且本格可走** ⇒ 桥面。
            // 为什么挂在 SetTiles 上：这是三个生成器（城镇/野外/洞穴）写地面键的**唯一入口**
            // ⇒ 一处判据覆盖全部铺图路径，不必在每个生成器里各排一遍（那种写法必然漏）。
            // `else` 分支：同一格被改铺成非 deck 地砖时必须**撤销**标记（幂等，不残留）。
            if (_deck != null)
            {
                _deck[i] = DeckTiles.IsDeckGroundKey(groundKey) && TileKindInfo.IsWalkable(_tiles[x, y]);
            }
        }

        /// <summary>
        /// 取某格的**原版瓦片键**。
        /// 返回 <c>false</c> = 本图没开启逐格覆盖（调用方按 `TileKind` 分类取默认瓦片）。
        /// 返回 <c>true</c> 时 <paramref name="groundKey"/> 可能是空串 = 原版这格**什么都不画**。
        /// </summary>
        public bool TryGetTiles(int x, int y, out string groundKey, out string objectKey)
        {
            groundKey = null;
            objectKey = null;
            if (!_tileOverrides || !InBounds(x, y)) return false;
            var i = y * Width + x;
            groundKey = _groundKeys[i] ?? "";
            objectKey = _objectKeys[i] ?? "";
            return true;
        }

        // ── deck（可走上方的结构：桥面/平台/甲板；见 `_deck` 字段注释）────────────────

        /// <summary>
        /// 标记该格为 **deck**（站在上面的实体在视图层会抬一档排序）。
        /// <para>正常路径**不需要**显式调它：`SetTiles` 已按「地面键取自 deck 类包 + 本格可走」
        /// 自动登记；本方法供自证/特例使用。</para>
        /// </summary>
        public void MarkDeck(int x, int y)
        {
            if (_deck == null)
            {
                MapLog.WarnThrottled("map.deck.noarr", "MarkDeck: deck 表未分配（地图未 Reset？），忽略本次标记");
                return;
            }
            if (!InBounds(x, y))
            {
                MapLog.WarnThrottled("map.deck.oob", $"MarkDeck: ({x},{y}) 在图外（图 {Width}x{Height}），忽略");
                return;
            }
            _deck[y * Width + x] = true;
        }

        /// <summary>标记该格为 deck（见 <see cref="MarkDeck(int,int)"/>）。</summary>
        public void MarkDeck(Vector2Int g) => MarkDeck(g.x, g.y);

        /// <summary>
        /// 该格是否是 deck（可走上方的结构）。图外 / 未生成 / 未启用 ⇒ false
        /// —— 与 <see cref="Walkable"/> 的越界口径一致（`IMapModule.IsDeckGrid` 的实现来源）。
        /// </summary>
        public bool IsDeck(Vector2Int g)
        {
            if (_deck == null) return false;
            if (!InBounds(g)) return false;
            return _deck[g.y * Width + g.x];
        }

        /// <summary>
        /// 整图填充（遍历顺序由引擎件 <see cref="GridGraph.Fill"/> 给，本项目只给"写什么"）。
        /// </summary>
        public void Fill(TileKind kind)
        {
            if (_tiles == null) { MapLog.Error("Fill: 地图未 Reset"); return; }
            GridGraph.Fill(Width, Height, (x, y) => _tiles[x, y] = kind);
            _countsDirty = true;
        }

        /// <summary>
        /// 矩形填充（左下角 = (x0,y0)，尺寸 w×h；超出部分自动裁剪）。
        /// <para>遍历 + 裁剪在引擎件 <see cref="GridGraph.FillRect"/>；`false` = 尺寸非法、
        /// **一格都没写**（由本方法留痕 —— 引擎不替调用方打业务日志）。</para>
        /// </summary>
        public void FillRect(int x0, int y0, int w, int h, TileKind kind)
        {
            if (!GridGraph.FillRect(x0, y0, w, h, Width, Height, (x, y) => _tiles[x, y] = kind))
            {
                MapLog.WarnThrottled("map.rect.bad", $"FillRect: 非法尺寸 {w}x{h}（x0={x0} y0={y0}），忽略");
                return;
            }
            _countsDirty = true;
        }

        /// <summary>矩形填充（向量版）。</summary>
        public void FillRect(Vector2Int origin, int w, int h, TileKind kind) => FillRect(origin.x, origin.y, w, h, kind);

        /// <summary>
        /// <para>出处（两条，互相印证）：</para>
        /// <para>① **原版地形**：`原版资源/d2lod1.10txt-1.10f/data/global/excel/Levels.txt`
        /// 「Act 1 - Wilderness 1」(= Blood Moor) `SizeX = SizeY = 80`；
        /// `LvlPrest.txt`「Act 1 - Wild Border 1..12」`SizeX = SizeY = 8`
        /// ⇒ 80 / 8 = **10×10 块**，最外一圈块（每边 8 格）= 原版的**边界环**
        /// （Wild Border / Wild Cliff Border = 崖壁 + 树线，原版即不可走）。</para>
        /// <para>② **相机几何**：`ortho = 3.75`、`1920×1080` ⇒ 半屏 halfW = 6.667 / halfH = 3.75，
        /// `Iso.HalfW = 1` / `Iso.HalfH = 0.5` ⇒ 可见**格**包围盒半跨 =
        /// `halfW/(2·HalfW) + halfH/(2·HalfH)` = 3.333 + 3.75 = **7.083 格**
        /// ⇒ 机位离边界 ≥ 7.083 才能零虚空；取 **8** 刚好覆盖（8 &gt; 7.083）。</para>
        /// </summary>
        public const int BorderRingCells = 8;

        /// <summary>
        /// 现在还可走的格封成**不可走地形**（`blockKind`：树 / 崖壁 / 石墙，不是 `Void`）。
        /// <para>为什么必须在地图侧封而不是在相机侧夹：相机侧夹制要求「机位离边界 ≥ 7.083 格」，
        /// 而可走区一直铺到最外圈时玩家自己就能走到离边界 1 格处 ⇒ 两侧数学互斥
        /// （`camera-follow` 片结论）。封住边界环后，可走区天然满足「机位 = 玩家 ⇒ 零虚空」。</para>
        /// <para>**不缩小地图**：外圈格仍然铺着原版地面/物件瓦片（只是不可走），
        /// 画面上是一圈树线/崖壁，不是黑虚空。</para>
        /// </summary>
        /// <returns>被封掉的格数（= 0 属非预期分支，会留 Warn）。</returns>
        public int SealBorderRing(int n, TileKind blockKind)
        {
            if (_tiles == null) { MapLog.Error("SealBorderRing: 地图未 Reset"); return 0; }
            if (n <= 0) { MapLog.Warn($"SealBorderRing: n={n} 非法（必须 > 0），忽略"); return 0; }

            // 「距任一图边 < n 格」的判定 + 遍历顺序在引擎件 `GridGraph.SealBorderRing`；
            //   "封成什么地形"由本方法给（blockKind，不是 Void —— 外圈仍铺着原版地面/物件瓦片）。
            var sealedCount = GridGraph.SealBorderRing(Width, Height, n, WalkableInGraph,
                (x, y) => _tiles[x, y] = blockKind);
            _countsDirty = true;

            if (sealedCount == 0)
            {
                MapLog.Warn($"SealBorderRing: 地图 {Width}x{Height} 的 {n} 格边界环里**一格可走的都没有**" +
                            "（原版块已全部封死？还是被前面的步骤清过？）⇒ 本条属非预期分支，请复核生成顺序");
            }
            return sealedCount;
        }

        /// <summary>
        /// 画一条水平线（含两端；端点顺序无所谓）。
        /// 归一化 + 遍历在引擎件 <see cref="GridGraph.LineH"/>；越界的 `x` 照旧交给 <see cref="Set"/> 留痕。
        /// </summary>
        public void LineH(int y, int xFrom, int xTo, TileKind kind)
            => GridGraph.LineH(y, xFrom, xTo, (px, py) => Set(px, py, kind));

        /// <summary>
        /// 画一条垂直线（含两端；端点顺序无所谓）。
        /// 归一化 + 遍历在引擎件 <see cref="GridGraph.LineV"/>；越界的 `y` 照旧交给 <see cref="Set"/> 留痕。
        /// </summary>
        public void LineV(int x, int yFrom, int yTo, TileKind kind)
            => GridGraph.LineV(x, yFrom, yTo, (px, py) => Set(px, py, kind));

        /// <summary>
        /// 只在地面（可走）格上写物件，避免把土路/出入口覆盖掉。
        /// <para>可走判定（含"图外一律不可走"）在引擎件 <see cref="GridGraph.SetOnWalkable"/>，
        /// 喂进去的是本项目的唯一可走口径 <see cref="WalkableInGraph"/>。</para>
        /// </summary>
        /// <returns>是否真的写入了。</returns>
        public bool SetOnWalkable(int x, int y, TileKind kind)
            => GridGraph.SetOnWalkable(WalkableInGraph, x, y, (wx, wy) =>
            {
                _tiles[wx, wy] = kind;
                _countsDirty = true;
            });

        /// <summary>只在地面（可走）格上写物件。</summary>
        public bool SetOnWalkable(Vector2Int g, TileKind kind) => SetOnWalkable(g.x, g.y, kind);

        /// <summary>
        /// 把以 center 为中心、半径 r 的圆盘内**已是可走**的格写成 kind。
        /// 圆盘遍历在引擎件 <see cref="GridGraph.PaintDiskWalkable"/>（它内部逐格走可走判定 ⇒ 图外格不会被写）。
        /// </summary>
        public void PaintDiskWalkable(Vector2Int center, int r, TileKind kind)
            => GridGraph.PaintDiskWalkable(WalkableInGraph, center, r, (x, y) => SetOnWalkable(x, y, kind));

        /// <summary>
        /// 把以 center 为中心、半径 r 的圆盘**无条件**写成 kind（挖洞/开地）。
        /// 圆盘遍历在引擎件 <see cref="GridGraph.FillDisk"/>；越界格照旧交给 <see cref="Set"/>（由其留痕）。
        /// </summary>
        public void FillDisk(Vector2Int center, int r, TileKind kind)
            => GridGraph.FillDisk(center, r, (x, y) => Set(x, y, kind));

        /// <summary>
        /// 把该格周围 (2·radius+1)² 的方块写成 kind（**超出图的部分自动裁剪**）—— 出生点净空/门口开地用它。
        /// 遍历 + 裁剪在引擎件 <see cref="GridGraph.FillRect"/>（写格仍经 <see cref="Set"/>）。
        /// </summary>
        public void ClearAround(Vector2Int center, TileKind kind, int radius = 1)
            => GridGraph.FillRect(center.x - radius, center.y - radius, 2 * radius + 1, 2 * radius + 1,
                Width, Height, (x, y) => Set(x, y, kind));

        /// <summary>
        /// 该格是否已有可走的 8 邻（用于判断「这面岩壁是否露出到视野里」）。
        /// 邻接表用引擎件 <see cref="GridGraph.Neighbors8"/>（与 BFS / `AStar` 同一份，不再自留第二份）。
        /// </summary>
        public bool HasWalkableNeighbor(Vector2Int g)
        {
            var steps = GridGraph.Neighbors8;
            for (var i = 0; i < steps.Length; i++)
            {
                if (Walkable(new Vector2Int(g.x + steps[i].x, g.y + steps[i].y))) return true;
            }
            return false;
        }

        // ═════════════════════════════════════════════════════════════════════
        // 查询 / 寻路 / 抽样
        // ═════════════════════════════════════════════════════════════════════

        public bool IsSpawnClear(Vector2Int spawn, int radius = 1)
        {
            for (var x = spawn.x - radius; x <= spawn.x + radius; x++)
            {
                for (var y = spawn.y - radius; y <= spawn.y + radius; y++)
                {
                    if (!Walkable(new Vector2Int(x, y))) return false;
                }
            }
            return true;
        }

        /// <summary>
        /// 求路径（逐格，含起终点）。**不可达 / 入参非法返回 null**（`CloverEngine.AStar` 已打日志）。
        /// 全项目唯一寻路实现：只转发，不另写一份。
        /// </summary>
        public List<Vector2Int> FindPath(Vector2Int from, Vector2Int to)
            => AStar.Find(Walkable, from, to);

        /// <summary>
        /// 随机取一个可走格（掉落 / 刷怪用）。
        /// 用可走格缓存做 **O(1)** 抽样 —— 在洞穴这种「大部分是墙」的图上随机撒点几乎全落空，
        /// 缓存法既快又天然不空手而归。无可用格时返回出生点并告警。
        /// </summary>
        public Vector2Int RandomWalkableTile(Rng rng)
        {
            if (rng == null)
            {
                MapLog.Error("RandomWalkableTile: rng 为 null（随机必须走注入式 CloverEngine.Rng），返回出生点");
                return SpawnPoint;
            }

            RecountIfNeeded();
            if (_walkableIndex.Count == 0)
            {
                MapLog.Warn("RandomWalkableTile: 本图没有任何可走格（生成失败？），返回出生点");
                return SpawnPoint;
            }
            // O(1) 取值走引擎件 `GridGraph.WalkableIndex.Pick`（这里传的 `rng.Next(Count)` 恒合法；
            //   真越界时引擎会收敛 + 降频 Warn —— 那是本方法不可能产生的状态，留痕即可）
            return _walkableIndex.Pick(rng.Next(_walkableIndex.Count));
        }

        /// <summary>可走格列表（只读，供模块内部/自证用）。</summary>
        public IReadOnlyList<Vector2Int> WalkableCells
        {
            get { RecountIfNeeded(); return _walkableIndex.Cells; }
        }

        // ═════════════════════════════════════════════════════════════════════
        // 连通性自检（BFS）
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>
        /// 从 <paramref name="from"/> 做一次 8 邻接 BFS，把可达格在 <paramref name="visited"/> 里标 `true`。
        /// <para>
        /// 邻接规则与 `CloverEngine.AStar` **完全一致**（8 邻接、对角要求两侧格都可走）——
        /// 否则会出现「BFS 说通、A* 走不过去」的假通过。
        /// </para>
        /// </summary>
        /// <param name="visited">由调用方分配的 `Width × Height` 布尔数组（本方法按「初始全 false」使用）。</param>
        /// <returns>可达格数（起点不可走 / 图未生成 → 0，并已打日志）。</returns>
        public int FloodFillFrom(Vector2Int from, bool[,] visited)
        {
            if (_tiles == null)
            {
                MapLog.Error("FloodFillFrom: 地图未生成");
                return 0;
            }
            if (visited == null || visited.GetLength(0) != Width || visited.GetLength(1) != Height)
            {
                MapLog.Error($"FloodFillFrom: visited 尺寸不符（应为 {Width}x{Height}）");
                return 0;
            }
            if (!Walkable(from))
            {
                MapLog.Warn($"FloodFillFrom: 起点 {from} 不可走，返回 0（地图没生成完 / 起点被障碍压住）");
                return 0;
            }

            // 8 邻接 BFS 在引擎件 `GridGraph.FloodFill` —— **同一份**邻接表 / 对角口径
            //   （对角要求两侧格都可走，否则 BFS 会「贴着墙角穿过去」而 `AStar` 不会）。
            //   上面三条前置判定**留在本项目**：它们要打业务日志（引擎只认识"调用方违约"）。
            return GridGraph.FloodFill(WalkableInGraph, Width, Height, from, visited);
        }

        /// <summary>
        /// 把「从 <paramref name="from"/> 走不到的孤立可走口袋」填成 <paramref name="fill"/>。
        /// <para>
        /// 为什么必须做：`RandomWalkableTile` 是**掉落与刷怪的落点来源**，
        /// 它同时让「可达格数 == 可走格数」成为可自检的硬不变量。
        /// </para>
        /// </summary>
        /// <param name="protectRequired">true ⇒ <see cref="RequiredReachable"/> 里的格不填（留给连通性自检判失败）。</param>
        /// <returns>实际填充的格数。</returns>
        public int FillUnreachablePockets(Vector2Int from, TileKind fill, bool protectRequired = true)
        {
            var visited = new bool[Width, Height];
            if (FloodFillFrom(from, visited) == 0)
            {
                MapLog.Warn("FillUnreachablePockets: 起点不可达/图未生成，跳过（不做任何填充）");
                return 0;
            }

            // 孤立口袋扫描在引擎件 `GridGraph.FillUnreachablePockets`（遍历顺序 x 外层升序、
            //   "填成什么地形"与"哪些格受保护"由本方法给（引擎不认识 `TileKind` / `RequiredReachable`）。
            Func<Vector2Int, bool> protect = protectRequired ? (Func<Vector2Int, bool>)IsRequiredTarget : null;
            var filled = GridGraph.FillUnreachablePockets(WalkableInGraph, Width, Height, visited, protect,
                (x, y) => { _tiles[x, y] = fill; }, out var keptRequired);
            _countsDirty = true;

            if (filled > 0)
            {
                MapLog.Info($"连通性修整：填掉 {filled} 格「从 {from} 不可达的孤立口袋」" +
                            $"（否则掉落/刷怪会落进死地）→ 地形改为 {fill}");
            }
            if (keptRequired > 0)
            {
                MapLog.Warn($"连通性修整：有 {keptRequired} 格孤立可走格属于**必需可达目标**，未填充；" +
                            "连通性自检会把它们报为失败并触发重生成");
            }
            return filled;
        }

        /// <summary>该格是否为必需可达目标。</summary>
        public bool IsRequiredTarget(Vector2Int g)
        {
            for (var i = 0; i < RequiredReachable.Count; i++)
            {
                if (RequiredReachable[i] == g) return true;
            }
            return false;
        }

        /// <summary>
        /// 从<b>出生点</b>做一次 BFS，校验 <see cref="RequiredReachable"/>（出口 / 洞穴入口 / 房间 / 刷怪点）
        /// 全部可达。
        /// </summary>
        /// <param name="unreachableCount">不可达的目标数。</param>
        /// <param name="firstUnreachable">第一个不可达的目标格（`unreachableCount == 0` 时无意义）。</param>
        /// <returns>全部可达返回 true。</returns>
        public bool VerifyConnectivity(out int unreachableCount, out Vector2Int firstUnreachable)
        {
            unreachableCount = 0;
            firstUnreachable = Vector2Int.zero;

            if (_tiles == null)
            {
                MapLog.Error("VerifyConnectivity: 地图未生成，判定失败");
                return false;
            }

            var visited = new bool[Width, Height];
            var reached = FloodFillFrom(SpawnPoint, visited);
            if (reached == 0)
            {
                MapLog.Warn($"VerifyConnectivity: 出生点 {SpawnPoint} 不可走（或图未生成），判定失败");
                return false;
            }

            // 目标可达性计数在引擎件 `GridGraph.CountUnreachableTargets`（它内部做一次
            //   **同口径** BFS ⇒ 与上面 `FloodFillFrom` 的可达数必然相等；多一次扫描只发生在地图生成期）。
            unreachableCount = GridGraph.CountUnreachableTargets(WalkableInGraph, Width, Height, SpawnPoint,
                RequiredReachable, out _, out firstUnreachable);

            MapLog.Info($"连通性自检：从出生点 {SpawnPoint} 可达 {reached} 格（可走共 {WalkableCount}），" +
                        $"需可达目标 {RequiredReachable.Count} 个，不可达 {unreachableCount} 个");
            if (unreachableCount > 0) return false;
            if (reached != WalkableCount)
            {
                // 不判失败（原版地图允许孤立小岛），但必须留痕：可能是分割的障碍块
                MapLog.Warn($"连通性自检：可达 {reached} ≠ 可走总数 {WalkableCount}，" +
                            $"存在 {WalkableCount - reached} 格「孤立可走区」（未列入必须可达目标，不判失败）");
            }
            return true;
        }

        // ═════════════════════════════════════════════════════════════════════
        // 内部
        // ═════════════════════════════════════════════════════════════════════

        private void RecountIfNeeded()
        {
            if (!_countsDirty) return;
            _countsDirty = false;
            _blockedCount = 0;
            _walkableCount = 0;
            _walkableIndex.Clear();

            if (_tiles == null) return;

            // 逐格扫一遍只为收集"本图出现过哪些 TileKind"（下面的 default 分支必须留痕）
            var seen = new HashSet<TileKind>();
            for (var x = 0; x < Width; x++)
            {
                for (var y = 0; y < Height; y++) seen.Add(_tiles[x, y]);
            }

            // 可走格列表 + 可走计数在引擎件 `GridGraph.WalkableIndex.Rebuild`
            _walkableCount = _walkableIndex.Rebuild(WalkableInGraph, Width, Height);
            _blockedCount = Width * Height - _walkableCount;

            // switch 的 default 分支必须留痕：新增了 TileKind 却忘了登记可走性 ⇒ 会被静默当障碍
            foreach (var kind in seen)
            {
                if (!IsKnownKind(kind))
                {
                    MapLog.WarnOnce("map.unknownkind." + (int)kind,
                        $"发现未登记的 TileKind={(int)kind}；TileKindInfo.IsWalkable 走 default 分支" +
                        "（按不可走处理）。请在 Def/Enums.cs 登记可走性。");
                }
            }
        }

        /// <summary>是否为本项目登记过的 <see cref="TileKind"/>（`default` 分支即未登记）。</summary>
        private static bool IsKnownKind(TileKind kind)
        {
            switch (kind)
            {
                case TileKind.Void:
                case TileKind.Grass:
                case TileKind.Dirt:
                case TileKind.Road:
                case TileKind.Rock:
                case TileKind.Tree:
                case TileKind.Fence:
                case TileKind.Wall:
                case TileKind.CaveFloor:
                case TileKind.CaveWall:
                case TileKind.Exit:
                case TileKind.TownFloor:
                //   否则逐格扫描会把它当"未登记的 TileKind"打 Warn（假告警）。
                case TileKind.Water:
                    return true;
                default:
                    return false;
            }
        }
    }
}
