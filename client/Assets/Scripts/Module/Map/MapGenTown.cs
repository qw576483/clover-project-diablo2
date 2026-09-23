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
//   上一版只取 `TownW1` 的营地本体并裁成 32×32 ⇒ **营地外的河被裁掉了**（只剩 1 列）；
//   agent-41 改成"整关"，agent-42 按主 agent 裁决把 `GameConst.TownWidth/TownHeight`
//   一并同步为 56/40 ⇒ 这里**不再有尺寸漂移**，两者不一致时直接报错拒绝生成（不静默）。
//
// ★ 木桥：河上那一座桥只有原版 `TownE1.ds1` 有（`OUTDOORS/bridge.dt1`）。生成器给桥
//   单开了一条规则（桥面可走 / 栏杆阻挡），所以本生成器铺出来的图里河是**可以走过去的**
//   —— 见 `MapGenTownLayout.Rows` 第 20/22 行的 `dddddddddd`。
//
// ★ 片 4（窗口原点）：关卡窗口**不再**取参考块的本地 [0,0)，而是「营地本体 39 列 + 出城口
//   外面 17 列」= 56 列（行同理：营地南围栏那行就是关卡南边界）。依据见生成器
//   `tools/d2codec/export_town_layout.py` 的 `WIN_X0/WIN_Y0` 注释。**效果**：出城口
//   （现在是内陆格 `(17,26..28)`）西边 17 列是"出城口外面那片地"（原版瓦片）⇒ 不再出现
//   "出口外面一片黑"（旧口径下地图西边界正好压在营地西围栏上，出城口就在地图边界上）。
//
// ⛔ 布局是**固定**的 ⇒ 不使用 `rng`（保留参数只为与其它生成器**签名一致**，
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
    /// <para>
    /// ★ 片 g1-resume 新增（修用户报的「传送点没效果」；验收表 S-40）。出处逐条：
    /// </para>
    /// <list type="number">
    /// <item>`Levels.txt`「Act 1 - Town」的 `Waypoint` 列 = **0**（= 本关有传送点，编号 0；
    ///   同列 255 = 没有传送点，见同表 `Act 1 - Wilderness 1` / `Act 1 - Cave 1`）。
    ///   这是"本关有传送点"的**表级依据**。</item>
    /// <item>`Objects.txt` Id=**119** = `Name=Waypoint` / `Token=wp` / `SizeX=SizeY=5`
    ///   （5 子格 = 1 格；子格 = 格 × 5，见 `tools/d2codec/export_town_layout.py` 文件头 ③-a）
    ///   ⇒ 传送点物件**占 1 格**、可选中（`Selectable0/2=1`）。</item>
    /// <item>**位置** = 原版四块城镇块（`LpPrest.txt`「Act 1 - Town 1」的
    ///   `TownN1/E1/S1/W1.ds1`）里那个 **kind=2 预设单位**。四块是同一座营地按不同原点导出的
    ///   （对齐口径见 `MapGenTownLayout` 文件头），把该单位按生成器同一套变换
    ///   （`level = local + WIN - offs`）折算成关卡坐标后**四块完全重合** = (31,26)：
    ///   <c>TownW1 本地(14,21)+（17,5) / TownN1(28,10)+(3,16) / TownE1(32,16)+(-1,10) / TownS1(28,27)+(3,-1)</c>
    ///   —— 四块给出同一个关卡格 ⇒ 它是**关卡级的标记单位**（不是某一块的装饰）。</item>
    /// </list>
    /// <para>
    /// ⚠️ **已登记的差异（未决项，见本轮报告）**：该预设单位在 `Objects.txt` 里的行名是
    /// `not used`（Id=110 / Token=n5）—— 这是原版美术在 DS1 里留的**占位单位**，引擎在运行时
    /// 用 Id=119 的 Waypoint 替换它；而**世界里的传送点本体艺术**（`data/global/objects/wp/*.dc6`）
    /// 本工程没有解包（`原版资源/d2dc6` 里只有物品图标 `invwpl/invwps.DC6`）。
    /// 因此本片**只用它做交互锚点**，不新增任何自创贴图；视觉差异已登记到报告里等主 agent 裁决。
    /// </para>
    /// </summary>
    public static readonly Vector2Int Waypoint = new Vector2Int(31, 26);

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
            // 契约常量与生成物**必须相等**（主 agent 裁决：两侧都按原版关卡尺寸 56×40）。
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

            // ★ 片 map-border：**边界环封闭**（可走区离四边界 ≥ `GridMap.BorderRingCells` 格）。
            //   出处同 `GridMap.BorderRingCells`：原版 `LvlPrest` 边界块边长 8 > 相机实测可见格半跨 7.083。
            //   营地外圈的树线/围栏在原版就是不可走的边界 ⇒ 封掉它既不缩水也不露虚空。
            // ⛔ 片 map-border2（接力片）结论：**城镇不能走 `SealBorderRing`** —— 实测证据（见
            //   `.ai-tmp/test/report-mapborder2.md`）：
            //   ① 城镇关卡 56×40 是**原版 `Levels.txt`「Act 1 - Town」的整关**（= 营地本体 39 列
            //      + 出城口外 17 列，见本文件头片 4 注释），营地**围栏那一行就是关卡的最后一行**
            //      ⇒ 营地内部格（如 NPC 基德 (22,33)）离边界只有 6 格、紧贴围栏的格只有 1 格；
            //   ② 封 8 格边界环 ⇒ (22,33) 被埋成树 ⇒ 必需可达目标不可达 ⇒ 连通性自检失败 ⇒
            //      `MapModule` 换 seed 重试（本图是**固定布局**，8 次全败）⇒ 落保底布局
            //      ⇒ mapcheck §10/§14/§15/§24 等 **16 项既有判据连带变红**（实测：47 项失败）；
            //   ③ 而 §31 要求的"所有可走格距边界 ≥ 8"与 §24 的**冻结判据**「关卡东边界列
            //      （x=Width-1）上存在可走桥面格」（原版木桥东端 = 与野外的共享边列，玩家从那里
            //      过桥进野外）在数学上互斥：x=Width-1 的可走格距边界恒 = 0。
            //   ⇒ 本片**不改动城镇封环**，把冲突交主 agent 裁决（选项：① 城镇豁免 §31；
            //     ② 城镇关卡扩为 56+2×8 × 40+2×8 并整体内移，同时 §24 / NPC 坐标 / 传送点
            //     (31,26) / 河东岸 (54,27) / 西北角 30 格 Void 等**绝对坐标判据**一并改为
            //     "布局坐标系"，那是契约级改动）。野外与洞穴两侧已封环（§31 全绿）。
            // ★ 片 map-border2 第二轮（主 agent **二次裁决 2026-09-23：A′ 做 / A″ 保留差异**）：
            //   A′：把**营地外**的边界带（距任一地图边 < `BorderRingCells` **且**位于营地内框之外
            //       的可走格）封成**原版树线**（物件键取自布局表自身的 't' 格 = 原版 `town_trees` 瓦片；
            //       ⛔ 不新增素材、⛔ 不用纯色块）。为什么只封营地外：
            //       · 实测（`mapcheck §32`）"过了生产相机夹制仍露地图外"的 310 格里，**营地外 157 格**
            //         是普通可行走草地（`TileKind.Grass` + `town_floor/028`）—— 原版 Rogue Encampment
            //         外围不长这样 ⇒ 这 157 格属**生成缺陷**（玩家能从出生点一路走出去）；
            //         封掉之后 `CameraBounds` 的角格回退（"让位给主角可见"，`CameraBounds.cs:31`）
            //         才有东西可遮。
            //       · **营地内框那 153 格**（贴围栏内沿）**不封**：封它就得裁原版营地本体
            //         （会埋掉 NPC 基德 (22,33)），且 §24 的东边界接缝桥面格恒在 `x=Width-1`
            //         ⇒ 保留为**已登记差异**（`策划/差异登记.tsv` E57）。
            //   ⛔ 封环**必须跳过**这些格（否则连锁变红）：`map.Exits`（围栏西侧 3 格出城口）、
            //      桥面/deck（§24 冻结判据：`x=Width-1` 上必须有可走桥面格）、
            //      `RequiredReachable`（NPC / 传送点 / 出生点）。
            // （A′ 的实际封环调用放在下面 `RequiredReachable` 填好之后 —— 封环要按
            //   「必需可达目标」做保护，早调会看不到那份清单。）

            // ★ 片 M3 说明（为什么这里**不**动 `map.Exits`）：
            //   原版城镇关卡的**东边界列与野外第 0 列是同一条「共享边列」**（出处
            //   `libd2/.../drlg/outdoors/OutRoom.zig:271`；本仓库 `tools/d2codec/export_town_layout.py`
            //   文件头 line 98-106 已逐条复核）⇒ 原版「过桥向东 = 进入野外」。
            //   但这条接缝**不写进 `map.Exits`**：`mapcheck` §10 的既有判据「城镇出口恰 3 格
            //   （= 围栏西侧 `warp.dt1` 的 3 格缺口）」是**已冻结的判据**，⛔ 不许为了新功能放宽它
            //   （片 M3 约束 = 只加断言、别改判据）。⇒ 接缝改在**触发侧**判定，判据唯一出处 =
            //   `Module/Map/MapSeam.IsTownEastSeam`（纯函数；`mapcheck §24` 与 `movecheck §9` 都已断言）。

            // ② 出生点 / NPC 站位：由生成器在原版布局上算好（出生点 8 邻全可走；NPC 全在可达区）
            map.SpawnPoint = MapGenTownLayout.Spawn;
            for (var i = 0; i < MapGenTownLayout.Npcs.Length; i++) map.NpcPoints.Add(MapGenTownLayout.Npcs[i]);

            // ★ 片 g1-resume：传送点交互锚点（出处见 `MapGenTown.Waypoint` 的注释）。
            //   ⛔ 不新增任何贴图：原版世界里那座传送台的本体艺术本工程未解包（见该常量的注释）。
            //   硬要求：锚点必须**可走且在围栏内**（否则玩家走不到 / 点不到）——不合格就点名报错，
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

            // ★ 片 map-border2 第二轮（主 agent **二次裁决 2026-09-23：A′ 做 / A″ 保留差异**）：
            //   A′：把**营地外**的边界带（距任一地图边 < `BorderRingCells` **且**位于营地内框之外
            //       的可走格）封成**原版树线**（物件键取自布局表自身的 't' 格 = 原版 `town_trees` 瓦片；
            //       ⛔ 不新增素材、⛔ 不用纯色块、⛔ 不缩地图）。为什么只封营地外：
            //       · 实测（`mapcheck §32`）"过了生产相机夹制仍露地图外"的 310 格里，**营地外 157 格**
            //         是普通可行走草地（`TileKind.Grass` + `town_floor/028`）—— 原版 Rogue Encampment
            //         外围不长这样 ⇒ 这 157 格属**生成缺陷**（玩家能从出生点一路走出去）；
            //         封掉之后 `CameraBounds` 的角格回退（"让位给主角可见"，`CameraBounds.cs:31`）
            //         才有东西可遮。
            //       · **营地内框那 153 格**（贴围栏内沿）**不封**：封它就得裁原版营地本体
            //         （会埋掉 NPC 基德 (22,33)），且 §24 的东边界接缝桥面格恒在 `x=Width-1`
            //         ⇒ 属**已登记差异**（`策划/差异登记.tsv` E57）。
            //   ⛔ 封环必须放过：`map.Exits`（围栏西侧 3 格出城口）、`map.IsDeck`（§24 要求
            //      `x=Width-1` 上有可走桥面格）、`RequiredReachable`（NPC / 传送点 / 出生点）。
            var sealedCells = SealOutOfCampBorderBand(map);
            MapLog.Info($"MapGenTown: 边界环封闭 {sealedCells} 格（**仅营地外**；n={GridMap.BorderRingCells}，" +
                        $"用原版 `town_trees` 瓦片 ⇒ 不新增素材）⇒ 营地外可走区离四边界恒 ≥ " +
                        $"{GridMap.BorderRingCells} 格；营地内框（贴围栏内沿）不封 = 已登记差异 E57");

            // 不变量：可达 == 可走（营地是围栏围起来的，正常不会填到任何格；填到了说明有死地）
            map.FillUnreachablePockets(map.SpawnPoint, TileKind.Wall);

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
        /// ★ 片 map-border2 第二轮（裁决 A′）：把**营地外**的边界带封成原版树线。
        /// <para>封哪些格：可走 **且** 距任一地图边 &lt; <see cref="GridMap.BorderRingCells"/> **且**
        /// **不在营地内框**（`x∈(17,47) × y∈(16,39)`，口径同 `mapcheck §10/§32` 的 campInterior）。</para>
        /// <para>⛔ 跳过（否则连锁变红，见调用点注释）：`map.Exits` / `map.IsDeck`（§24）/
        /// `map.RequiredReachable`（NPC / 传送点 / 出生点）。</para>
        /// <para>地形 = <see cref="TileKind.Tree"/>；**物件键**取布局表自身 't' 格的**原版**物件键
        /// （轮换），**地面键保持原样**（草地）⇒ 与原版树线格一个样子；⛔ 不新增素材、⛔ 不用纯色块。</para>
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

        /// <summary>营地**内框**（围栏环以内；口径同 `mapcheck §10/§32` 的 campInterior）。</summary>
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
                // ★ 片 L / R12：水 = **独立 `TileKind.Water`**（原版 river.dt1 的水面）。
                //   此前与水边的石头同归 `Rock` ⇒ 光标 / 小地图 / tooltip 无法把"水"与"石头"分开。
                //   ⛔ 不许把水改成可走（原版不可涉水）：可走性仍是 `false`（`TileKindInfo.IsWalkable`）。
                case 'r': return TileKind.Water;   // 水（河/水塘，原版 river.dt1）—— 不可涉水
                case 'x': return TileKind.Exit;
                // ★ 片 4：'v' = 原版**四块都没有瓦片**的格（实测只有西北角 3×10 一块）。
                //    原版那几格什么都不画、也没有 walk 标志 ⇒ 不画 + 不可走（= TileKind.Void）。
                //    ⛔ 不许当草地（当草地会变成"可走的隐形格"，玩家能走到纯黑背景上去）。
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
