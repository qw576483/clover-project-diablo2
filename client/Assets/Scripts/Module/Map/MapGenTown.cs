// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · Module/Map/MapGenTown.cs
// **罗格营地（Rogue Encampment）**：**照原版预设块逐格铺**（不做任何程序化撒点）。
//
// 布局数据**不是本项目自己设计的**，而是从原版地图文件解出来的：
//   来源 = `data/global/tiles/ACT1/TOWN/*.ds1`（Blizzard North, 2000，取自 d2data.mpq）
//          —— 块清单与原版关卡尺寸**都读原版规则表**：
//            · `Levels.txt`「Act 1 - Town」⇒ 关卡 **56×40**（LevelName = Rogue Encampment）
//            · `LvlPrest.txt`「Act 1 - Town 1」⇒ 4 块
//              `TownN1 / TownE1 / TownS1 / TownW1`
//   生成 = `tools/d2codec/export_town_layout.py` → `Module/Map/MapGenTownLayout.cs`
//          （生成物，禁止手改）。
//
// 尺寸：**生成物与契约常量同值 = 原版关卡尺寸 56×40**。
// 取值必须覆盖整关：只取 `TownW1` 的营地本体并裁成 32×32 会把**营地外的河裁掉**（只剩 1 列）。
//
// 木桥：河上那一座桥只有原版 `TownE1.ds1` 有（`OUTDOORS/bridge.dt1`）。生成器给桥
//   单开了一条规则（桥面可走 / 栏杆阻挡），所以本生成器铺出来的图里河是**可以走过去的**
//   —— 见 `MapGenTownLayout.Rows` 第 20/22 行的 `dddddddddd`。
//
//   西边界：营地西围栏**西边还留一段"外面的地"**（该段宽度见生成器
//   `tools/d2codec/export_town_layout.py` 的 `WIN_X0/WIN_Y0` 注释）；行同理：营地南围栏那行
//   就是关卡南边界。**效果**：出城口（现在是内陆格 `(17,26..28)`）西边是"出城口外面那片地"
//   （原版瓦片）⇒ 不再出现"出口外面一片黑"（地图西边界若压在营地西围栏上，出城口就在边界上）。
//
// 布局是**固定**的 ⇒ 不使用 `rng`（保留参数只为与其它生成器**签名一致**，
//    这样 `MapModule` 可以用同一段重试逻辑驱动三个区域）。
// ─────────────────────────────────────────────────────────────────────────────

using System.Collections.Generic;
using System.Text;
using CloverEngine;
using Diablo2.Core;
using Diablo2.Def;
using UnityEngine;

namespace Diablo2.Module.Map
{
    /// <summary>罗格营地固定布局生成器（数据来自原版 DS1，见文件头）。</summary>
    public static class MapGenTown
    {
    /// <summary>
    /// 罗格营地的**传送点交互锚点**（关卡格 = 原版 DS1 的预设单位坐标换算而来的**关卡坐标**）。
    /// <list type="number">
    /// <item>`Levels.txt`「Act 1 - Town」的 `Waypoint` 列 = **0**（= 本关有传送点，编号 0；
    ///   同列 255 = 没有传送点，见同表 `Act 1 - Wilderness 1` / `Act 1 - Cave 1`）。
    ///   这是"本关有传送点"的**表级依据**。</item>
    /// <item>`Objects.txt` Id=**119** = `Name=Waypoint` / `Token=wp` / `SizeX=SizeY=5`
    ///   （5 子格 = 1 格；子格 = 格 × 5，见 `tools/d2codec/export_town_layout.py` 文件头 ③-a）
    ///   ⇒ 传送点物件**占 1 格**、可选中（`Selectable0/2=1`）。
    ///   本体艺术 = `data/global/objects/wp/{TR,S1}/*.dcc`（已解包，见 `ResPaths.D2ObjectsWaypoint`）。</item>
    /// <item>**位置** = 原版城镇块 `TownW1.ds1`（= 本区瓦片的来源块，`MapGenTownLayout` 的参考块）
    ///   objects 层里钉在**五芒星石台**上的那个 **kind=2 预设单位**：子格 (84,69) ⇒
    ///   关卡坐标 (33.8,18.8)（`level = local + (17,5)`，同生成器的块对齐口径）⇒
    ///   最近格 = **(34,19)**（距原版位置 0.2 世界单位；其余三格 ≥ 0.8）。
    ///   `TownE1` 的同名单位折算后 = (34.8,19.8)（块间手调差 1 格，与 NPC 站位同况，
    ///   取参考块那一份）。kind=2 单位的 id 是**该幕 objpreset 的下标**（不是 `Objects.txt`
    ///   行号 —— 按行号直读 id=52 会得到 `Urn`，台子上会多一个原版没有的坛子）；
    ///   该单位就是 objpreset 里的 Waypoint（Id=119），证据 = 它的落点：</item>
    /// <item>五芒星石台 = `town_floor/000..003`（`floor.dt1` m=0 s=52..55）四块**全营唯一**的
    ///   地砖，落 (33,18)/(34,18)/(33,19)/(34,19)（`tools/d2codec/dump_cell.py` 可查）；
    ///   该单位的子格坐标钉在台心公共角（85,70）旁 1 子格内 —— 单位与石台同址 ⇒
    ///   火焰画在石台上（与原版一致）。营火焦土圈（(31,26) 一带，`town_objects/070..079`）
    ///   上的 id=110 单位是**营火**标记（Warriv 站位旁），不是传送点。</item>
    /// </list>
    /// </summary>
    public static readonly Vector2Int Waypoint = new Vector2Int(34, 19);

        /// <summary>生成罗格营地（**固定布局**，同 seed 与不同 seed 结果都一样）。</summary>
        public static void Generate(GridMap map, Rng rng)
        {
            if (map == null)
            {
                MapLog.Error("MapGenTown.Generate: map 为 null，放弃生成");
                return;
            }

            // rng 仅用于统一签名（固定布局不需要随机）；显式说明，避免读代码的人以为漏了随机
            var seed = rng != null ? rng.Seed : 0;

            var w = MapGenTownLayout.Width;
            var h = MapGenTownLayout.Height;
            // 契约常量与生成物**必须相等**（两侧都按原版关卡尺寸 56×40）。
            // 不一致 = 生成物没跟上 / 有人改了常量 ⇒ 报错拒绝生成，别静默铺一张错尺寸的图。
            if (w != GameConst.TownWidth || h != GameConst.TownHeight)
            {
                MapLog.Error($"MapGenTown: 生成物尺寸 {w}x{h} ≠ GameConst.TownWidth/TownHeight = " +
                             $"{GameConst.TownWidth}x{GameConst.TownHeight} ⇒ 拒绝生成（两处都必须 = " +
                             "原版 `Levels.txt`「Act 1 - Town」的 SizeX/SizeY；改完重跑 " +
                             "`tools/d2codec/export_town_layout.py`）");
                return;
            }
            map.Reset(AreaId.Town, w, h, seed);

            if (MapGenTownLayout.Rows == null || MapGenTownLayout.Rows.Length != h)
            {
                // 非预期：生成物与网格尺寸不一致 ⇒ 直接报出来，别静默铺一张空图
                MapLog.Error($"MapGenTown: 布局表行数 {MapGenTownLayout.Rows?.Length ?? -1} ≠ 网格高 {h}" +
                             "（MapGenTownLayout.cs 是生成物，请重跑 tools/d2codec/export_town_layout.py）");
                map.Fill(TileKind.Grass);
                return;
            }

            // ① 营地外先铺草地（原版野外底色），再逐格盖原版布局
            map.Fill(TileKind.Grass);

            // ①.5 逐格登记**原版瓦片键**（floor 层 + wall 层各一张，取自原版块）：
            //     `MapView` 见到本图启用了覆盖就一律用它 ⇒ 营地画面 = 原版营地画面（含
            //     "原版这格没铺/没物件" 的格）。取不到键的格留空串 = 原版那格不画。
            map.BeginTileOverrides();
            var noGround = 0;
            // 桥面登记对账（数值证据，见 `DeckTiles` / `GridMap.SetTiles`）：
            //   deckTiles = 布局里地面键取自 deck 类包（`moor_bridge`）的格数（**含栏杆行的地面**）；
            //   deckMarked = 其中真正登记成 deck 的格数（= 可走的桥面格）。
            // 判据：桥面行存在（deckTiles > 0）却一格都没登记上 ⇒ **非预期**，必须 Warn（不静默）。
            var deckTiles = 0;
            var deckMarked = 0;

            var counts = new int[128];
            for (var y = 0; y < h; y++)
            {
                var row = MapGenTownLayout.Rows[y];
                if (row == null || row.Length != w)
                {
                    MapLog.Error($"MapGenTown: 布局表第 {y} 行长度 {(row == null ? -1 : row.Length)} ≠ 网格宽 {w}" +
                                 "（生成物与网格不一致，整行按草地处理）");
                    continue;
                }

                for (var x = 0; x < w; x++)
                {
                    var c = row[x];
                    counts[c < 128 ? c : 0]++;
                    var kind = KindOf(c);
                    map.Set(x, y, kind);
                    if (kind == TileKind.Exit) map.Exits.Add(new Vector2Int(x, y));

                    string gk = null, ok = null;
                    if (!MapGenTownLayout.TryGetTiles(x, y, out gk, out ok)) { noGround++; gk = ""; ok = ""; }
                    map.SetTiles(x, y, gk ?? "", ok ?? "");

                    // 桥面（deck）登记：判据 = 地面键取自 deck 类包 **且本格可走**（口径唯一出处 = DeckTiles）。
                    // 这座桥的 4 行里只有两行可走（`Rows` 的 'd'）⇒ 桥面 = 2 行 × 10 列；
                    // 另两行（'s' 栏杆行）地面同图集但不可走，**不算**桥面。
                    if (DeckTiles.IsDeckGroundKey(gk))
                    {
                        deckTiles++;
                        if (map.IsDeck(new Vector2Int(x, y))) deckMarked++;
                    }
                }
            }

            if (deckTiles > 0 && deckMarked == 0)
            {
                MapLog.Warn($"MapGenTown: 布局里有 {deckTiles} 格「deck 类包（{MapGenTownLayout.Packs[0]}）地砖」" +
                            "（桥面 + 栏杆行的地面），但**一格都没登记成 deck**（可走性判据变了？）" +
                            "⇒ 站在桥上的实体不会抬排序档，「人从桥下走」会复现（IsDeckGrid 全 false）");
            }
            else if (deckTiles > 0)
            {
                MapLog.Info($"MapGenTown: deck（桥面）登记 {deckMarked} 格；布局里 deck 类包地砖共 {deckTiles} 格" +
                            $"（差额 {deckTiles - deckMarked} = 栏杆行的地面：同图集但不可走 ⇒ 不算桥面）");
            }
            if (noGround > 0)
            {
                MapLog.Warn($"MapGenTown: 有 {noGround} 格不在原版布局表范围内（无原版瓦片键）⇒ 这" +
                            "几格在原版里没有瓦片，渲染层不会画任何东西");
            }

            // ①.6 装饰物件（原版 ds1 `objects` 层的 kind=2 预设单位：火炬 / 营火 / 旗 / 箱子）：
            //     位置与 ds1 id 都在布局表里（`DecoCells` / `DecoDs1Ids`），`id → 物件类` 查
            //     `MapGenDeco`（帧数 / 帧率 / 贴图目录都在那张表里，出处见生成物文件头）。
            //     **只登记"画什么"**：这些单位的可走性由 `Rows` 的 kind 决定，装饰物件不改它。
            var decoRegistered = 0;
            var decoUnmapped = 0;
            for (var i = 0; i < MapGenTownLayout.DecoCells.Length; i++)
            {
                var cell = MapGenTownLayout.DecoCells[i];
                if (!map.InBounds(cell))
                {
                    MapLog.Warn($"MapGenTown: 装饰物件 #{i} 的格 {cell} 在图外（布局表被改过？）⇒ 跳过");
                    continue;
                }
                var kindIndex = MapGenDeco.IndexOf(MapGenTownLayout.DecoDs1Ids[i]);
                if (kindIndex < 0)
                {
                    decoUnmapped++;
                    if (decoUnmapped == 1)
                    {
                        MapLog.Warn($"MapGenTown: 装饰物件的 ds1 id={MapGenTownLayout.DecoDs1Ids[i]} " +
                                    "在 MapGenDeco 里没有对应物件类 ⇒ 该单位不画" +
                                    "（传送点走 MapGenTown.Waypoint 锚点路径；原版 Draw=0 的类本来就不画）");
                    }
                    continue;
                }
                map.SetDeco(cell.x, cell.y, kindIndex);
                decoRegistered++;
            }
            MapLog.Info($"MapGenTown: 装饰物件登记 {decoRegistered} 个（原版 ds1 kind=2 预设单位）；" +
                        $"未映射 {decoUnmapped} 个（传送点 / Draw=0）");

            //   封环口径（出处同 `GridMap.BorderRingCells`：原版 `LvlPrest` 边界块边长 8 >
            //   相机可见格半跨 7.083）：把**营地外**的边界带（距任一地图边 < `BorderRingCells`
            //   **且**位于营地内框之外的可走格）封成**原版树线**（物件键取自布局表自身的 't' 格
            //   = 原版 `town_trees` 瓦片；不新增素材、不用纯色块）。营地外圈的树线/围栏在原版
            //   就是不可走的边界 ⇒ 封掉它既不缩水也不露虚空。
            //   · **必须跳过** `map.Exits`（围栏西侧 3 格出城口）与 `RequiredReachable`
            //     （NPC / 传送点 / 出生点）—— 否则把它们埋成树会让必需可达目标不可达 ⇒
            //     连通性自检失败 ⇒ `MapModule` 换 seed 重试（本图是**固定布局**，8 次全败）
            //     ⇒ 落保底布局。
            //   · **营地内框那些格不封**：它们是 `TileKind.Grass` + `town_floor/028` 的可行走
            //     草地，封掉就得裁原版营地本体；且 `CameraBounds` 的角格回退（"让位给主角可见"）
            //     要靠它们遮挡。
            //   （实际封环调用放在下面 `RequiredReachable` 填好之后 —— 封环要按「必需可达目标」
            //    做保护，早调会看不到那份清单。）

            //   原版城镇关卡的**东边界列与野外第 0 列是同一条「共享边列」**（本仓库
            //   `tools/d2codec/export_town_layout.py` 文件头已逐条复核）⇒ 原版「过桥向东 = 进入野外」。
            //   `map.Exits`（= 围栏西侧 `warp.dt1` 的 3 格缺口）是**已冻结的判据**，
            //   不许为了新功能放宽它。

            // ② 出生点 / NPC 站位：由生成器在原版布局上算好（出生点 8 邻全可走；NPC 全在可达区）
            map.SpawnPoint = MapGenTownLayout.Spawn;
            for (var i = 0; i < MapGenTownLayout.Npcs.Length; i++) map.NpcPoints.Add(MapGenTownLayout.Npcs[i]);

            // ③ 传送点锚点：这里**只落锚点**，本体贴图由视图层按锚点取（`MapView.PlanCell` 的
            //   `IsWaypointAnchor` 分支，取 `ResPaths.WaypointFrame`）。硬要求：锚点必须
            //   **可走且在围栏内**（否则玩家走不到 / 点不到）—— 不合格就点名报错，
            //   而不是静默地登记一个点不到的位置。
            map.WaypointPoints.Add(Waypoint);
            if (!map.Walkable(Waypoint))
            {
                MapLog.Error($"MapGenTown: 传送点锚点 {Waypoint} 不可走（原版布局表被改过？）" +
                             "⇒ 玩家走不到，传送面板点不开。请复核 MapGenTown.Waypoint 的出处");
            }

            // 必须可达：出城口 + 全部 NPC + 传送点
            map.RequiredReachable.AddRange(map.Exits);
            map.RequiredReachable.AddRange(map.NpcPoints);
            map.RequiredReachable.AddRange(map.WaypointPoints);

            //   A′：把**营地外**的边界带（距任一地图边 < `BorderRingCells` **且**位于营地内框之外
            //       的可走格）封成**原版树线**（物件键取自布局表自身的 't' 格 = 原版 `town_trees` 瓦片；
            //       不新增素材、不用纯色块、不缩地图）。为什么只封营地外：
            //         是普通可行走草地（`TileKind.Grass` + `town_floor/028`）—— 原版 Rogue Encampment
            //         封掉之后 `CameraBounds` 的角格回退（"让位给主角可见"，`CameraBounds.cs:31`）
            //         才有东西可遮。
            //       · **营地内框那 153 格**（贴围栏内沿）**不封**：封它就得裁原版营地本体
            var sealedCells = SealOutOfCampBorderBand(map);
            MapLog.Info($"MapGenTown: 边界环封闭 {sealedCells} 格（**仅营地外**；n={GridMap.BorderRingCells}，" +
                        $"用原版 `town_trees` 瓦片 ⇒ 不新增素材）⇒ 营地外可走区离四边界恒 ≥ " +
                        $"{GridMap.BorderRingCells} 格；营地内框（贴围栏内沿）不封 = 已登记差异 E57");

            // 不变量：可达 == 可走（营地是围栏围起来的，正常不会填到任何格；填到了说明有死地）
            map.FillUnreachablePockets(map.SpawnPoint, TileKind.Wall);

            //   B：连通性修整只改 `TileKind`（→ `Wall`），**不给物件瓦片** ⇒ 这些格在画面上
            //      什么都不画却挡路（"撞得上却看不见"）。边界带这一圈在画面上必须是**看得见的
            //      边界**（原版是树线/崖壁/河岸）⇒ 用本区域自身的 't' 物件键补上。
            //      只在边界带上做（营地内框不碰）；不改可走性、不动水面、已有原版物件瓦片的不动。
            var rekeyed = RekeyInvisibleBlockers(map);
            MapLog.Info($"MapGenTown: 补键 {rekeyed} 格（**营地内框以外、本就不可走但缺物件瓦片** ⇒ 否则是" +
                        "「看得见却走不过去」的空气墙；补本区域原版 't' 物件键，不改可走性、不动水面）");

            MapLog.Info($"MapGenTown: 原版罗格营地布局已铺（源 {Sources()} 关卡尺寸 {w}x{h}" +
                        $"（出处 {MapGenTownLayout.SizeSource}））：" +
                        $"栅栏 {Count(counts, 'f')} / 摊位·帐篷 {Count(counts, 'o')} / 树 {Count(counts, 't')} / " +
                        $"石矮墙 {Count(counts, 's')} / 水·河 {Count(counts, 'r')} / 泥土 {Count(counts, 'd')} / " +
                        $"草地 {Count(counts, '.')} / " +
                        $"出城口 {map.Exits.Count} / NPC {map.NpcPoints.Count} / 出生点 {map.SpawnPoint}");

            // 出生点净空是硬要求（生成器只在"8 邻全可走"的格里挑，这里再验一次，防生成物被改坏）
            if (!map.IsSpawnClear(map.SpawnPoint, 1))
            {
                MapLog.Warn($"MapGenTown: 出生点 {map.SpawnPoint} 的 3×3 邻域不是全可走" +
                            "（生成物被手改过？）—— MapModule 会换 seed 重试并最终落保底布局");
            }
        }

        /// <summary>
        /// <para>封哪些格：可走 **且** 距任一地图边 &lt; <see cref="GridMap.BorderRingCells"/> **且**
        /// **不在营地内框**（`x∈(17,47) × y∈(16,39)`，口径同 `mapcheck §10/§32` 的 campInterior）。</para>
        /// <para>跳过（否则连锁变红，见调用点注释）：`map.Exits` / `map.IsDeck`（§24）/
        /// `map.RequiredReachable`（NPC / 传送点 / 出生点）。</para>
        /// <para>地形 = <see cref="TileKind.Tree"/>；**物件键**取布局表自身 't' 格的**原版**物件键
        /// （轮换），**地面键保持原样**（草地）⇒ 与原版树线格一个样子；不新增素材、不用纯色块。</para>
        /// </summary>
        /// <returns>实际封掉的格数。</returns>
        private static int SealOutOfCampBorderBand(GridMap map)
        {
            var keys = ObjectKeysOf('t');
            if (keys.Length == 0)
            {
                MapLog.Error("MapGenTown: 布局表里找不到任何 't'（树）格的原版物件键 ⇒ **边界带不封**" +
                             "（用纯色块封就是用户报过的『占位图』前景 ⇒ 属非预期分支；请复核 " +
                             "MapGenTownLayout / export_town_layout.py）");
                return 0;
            }

            var n = GridMap.BorderRingCells;
            var sealedCount = 0;
            var keptProtected = 0;
            for (var y = 0; y < map.Height; y++)
            {
                for (var x = 0; x < map.Width; x++)
                {
                    var d = Mathf.Min(Mathf.Min(x, map.Width - 1 - x), Mathf.Min(y, map.Height - 1 - y));
                    if (d >= n) continue;
                    var g = new Vector2Int(x, y);
                    if (!map.Walkable(g)) continue;
                    if (IsCampInterior(g)) continue;                  // A″：营地内框不封
                    if (IsSealProtected(map, g)) { keptProtected++; continue; }

                    map.TryGetTiles(x, y, out var ground, out _);     // 地面键原样（草地）
                    map.Set(g, TileKind.Tree);
                    map.SetTiles(x, y, ground ?? "", keys[(x * 7 + y) % keys.Length]);
                    sealedCount++;
                }
            }

            if (keptProtected > 0)
            {
                MapLog.Info($"MapGenTown: 边界带里有 {keptProtected} 格**受保护未封**（出城口 / 桥面 deck" +
                            "（§24 要求 x=Width-1 有可走桥面格）/ NPC / 传送点 / 出生点）");
            }
            return sealedCount;
        }

        private static bool IsCampInterior(Vector2Int g) => g.x > 17 && g.x < 47 && g.y > 16 && g.y < 39;

        /// <summary>封边界带时必须放过的格（放过原因见 <see cref="SealOutOfCampBorderBand"/>）。</summary>
        private static bool IsSealProtected(GridMap map, Vector2Int g)
        {
            if (map.IsDeck(g)) return true;                 // 桥面：§24 要求 x=Width-1 上有可走桥面格
            for (var i = 0; i < map.Exits.Count; i++)
            {
                if (map.Exits[i] == g) return true;         // 出城口 3 格（玩家从那里出门）
            }
            return map.IsRequiredTarget(g);                 // NPC / 传送点 / 出生点
        }

        /// <summary>
        /// 给**营地内框以外、本就不可走、却没有物件瓦片**的格补上本区域原版 't' 物件键。
        /// <para>成因：连通性修整（`FillUnreachablePockets` → `Wall`）与布局里少数阻挡格只定
        /// `TileKind`、没有 wall 层瓦片；渲染层对"没有物件键的阻挡格"什么都不画 ⇒ 撞得上却看不见
        /// （玩家站在营地围栏边看这一圈草地，就是"看着能走却走不过去"）。</para>
        /// <para>这一圈本身就是原版的**围栏之外的边界地**（树线 / 河岸）⇒ 补上 't' 物件键即可
        /// 与原版同貌，不新增素材。</para>
        /// <para>不动：`TileKind.Void`、水面（水面本身就是「不可走」的画面）、**地面键也为空**的格
        /// （原版那格本来就不画 = 黑虚空，补了就是凭空长树）、已有物件键的格、营地内框、受保护格
        /// （出城口 / 桥面 / NPC / 传送点 / 出生点）。**不改可走性**。</para>
        /// </summary>
        /// <returns>补键的格数。</returns>
        private static int RekeyInvisibleBlockers(GridMap map)
        {
            var keys = ObjectKeysOf('t');
            if (keys.Length == 0)
            {
                MapLog.Error("MapGenTown: 布局表里找不到任何 't'（树）格的原版物件键 ⇒ 不改键" +
                             "（用纯色块补就是『占位图』⇒ 属非预期分支；请复核 MapGenTownLayout）");
                return 0;
            }

            var rekeyed = 0;
            for (var y = 0; y < map.Height; y++)
            {
                for (var x = 0; x < map.Width; x++)
                {
                    var g = new Vector2Int(x, y);
                    if (map.Walkable(g)) continue;
                    if (map.Get(g) == TileKind.Void) continue;        // 原版本来就不画的格（黑虚空）：一律不补
                    if (map.Get(g) == TileKind.Water) continue;
                    if (IsCampInterior(g)) continue;
                    if (IsSealProtected(map, g)) continue;
                    if (!map.TryGetTiles(x, y, out var ground, out var objectKey)) continue;
                    //   判据用**地面键**：有地面、没物件 = 看得见的空气墙（要补）；
                    //   地面键也空 = 原版那格什么都不画（黑虚空），补了就是凭空长出一棵树。
                    if (string.IsNullOrEmpty(ground)) continue;
                    if (!string.IsNullOrEmpty(objectKey)) continue;

                    map.SetTiles(x, y, ground, keys[(x * 7 + y) % keys.Length]);
                    rekeyed++;
                }
            }
            return rekeyed;
        }

        /// <summary>布局表里字符 <paramref name="c"/> 的格引用到的**原版物件键**（去重，轮换用）。</summary>
        private static string[] ObjectKeysOf(char c)
        {
            var set = new List<string>(4);
            var rows = MapGenTownLayout.Rows;
            for (var y = 0; y < rows.Length; y++)
            {
                var row = rows[y];
                if (row == null) continue;
                for (var x = 0; x < row.Length; x++)
                {
                    if (row[x] != c) continue;
                    if (!MapGenTownLayout.TryGetTiles(x, y, out _, out var ok)) continue;
                    if (string.IsNullOrEmpty(ok)) continue;
                    if (!set.Contains(ok)) set.Add(ok);
                }
            }
            return set.ToArray();
        }

        /// <summary>块清单拼成一个字符串（日志用）。</summary>
        private static string Sources()
        {
            var names = MapGenTownLayout.SourceDs1;
            if (names == null || names.Length == 0) return "（无）";
            var sb = new StringBuilder();
            for (var i = 0; i < names.Length; i++)
            {
                if (i > 0) sb.Append(", ");
                var n = names[i];
                var slash = n.LastIndexOf('/');
                sb.Append(slash >= 0 ? n.Substring(slash + 1) : n);
            }
            return sb.ToString();
        }

        /// <summary>布局字符 → 本项目 `TileKind`（字符含义见 `MapGenTownLayout.Rows` 注释）。</summary>
        private static TileKind KindOf(char c)
        {
            switch (c)
            {
                case '.': return TileKind.Grass;
                case 'd': return TileKind.Dirt;
                case 'f': return TileKind.Fence;
                case 'o': return TileKind.Wall;    // 摊位 / 帐篷 / 货车（原版 wall 层的 objects.dt1）
                case 'w': return TileKind.Wall;    // 兼容别名（旧生成物用过）
                case 't': return TileKind.Tree;
                case 's': return TileKind.Rock;    // 营地内的石矮墙（原版 wall 层的 stonewall.dt1）
                //   不许把水改成可走（原版不可涉水）：可走性仍是 `false`（`TileKindInfo.IsWalkable`）。
                case 'r': return TileKind.Water;   // 水（河/水塘，原版 river.dt1）—— 不可涉水
                case 'x': return TileKind.Exit;
                //    原版那几格什么都不画、也没有 walk 标志 ⇒ 不画 + 不可走（= TileKind.Void）。
                //    不许当草地（当草地会变成"可走的隐形格"，玩家能走到纯黑背景上去）。
                case 'v': return TileKind.Void;
                default:
                    // 非预期：生成物里出现了没登记的分类字符 ⇒ 留痕，并按草地处理（不静默）
                    MapLog.WarnThrottled("towngen.ch." + (int)c,
                        $"MapGenTown: 布局表出现未登记字符 '{c}'（(int)={(int)c}）⇒ 按草地处理");
                    return TileKind.Grass;
            }
        }

        private static int Count(int[] counts, char c) { return counts[c]; }
    }
}
