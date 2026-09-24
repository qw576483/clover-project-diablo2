// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · Module/Map/MapGenWilderness.cs
// **血腥荒野（Blood Moor）**：用**原版 ACT1 野外预设块**拼出来的随机野外
// （**每局不同，同 seed 可复现**）。
//
// **为什么改成"拼原版块"**（上一版是自画的"随机崖壁带 + 随机撒尖刺栅栏"）：
//    原版血腥荒野**不是一张整图**，而是引擎 DRLG 按**8 格一块的网格**拼出来的 ——
//    依据是原版自己的生成规则表（不是本项目猜的）：
//      · `Levels.txt`「Blood Moor」：LevelType=2(Act 1 - Wilderness) **DrlgType=3**(户外随机)
//        Size 80×80 ⇒ 10×10 个 8 格块
//      · `LvlPrest.txt`：`Act 1 - Wild Border 1..12`（SizeX=SizeY=8）、`Act 1 - Wild Cliff Border *`、
//        `Act 1 - Wild Cliff Cave *`、`Act 1 - Fence Fill 1..6`、`Act 1 - Tree Fill`、
//        `Act 1 - Cave Entrance` —— 逐块出处见生成物 `MapGenWildLayout.Source`
//      · `LvlSub.txt` Type=6：`Stone / Trees / Puddles / Swamp Big / Swamp Small / Wild Objects`
//      · `LvlTypes.txt` Id=2：野外地面 = `Act1/Town/Floor.dt1`，崖壁 = `Cliff1/Cliff2/Corner`，
//        树丛 = `TreeGroups`，石墙 = `stonewall`，栅栏 = `Fence`，碎石 = `Stones`，水 = `River/pond/puddle`
//
//      ① **基底草地**：每格一张原版 `TOWN/floor.dt1` 草地瓦片（原版引擎也是先把关卡铺满草）；
//      ② **边界块**：周圈每格一块原版边界块（`Bord*` / `StnClf*` / `clfcave*`），
//         按所在边**镜像**（原版对每条边用不同朝向的块）⇒ 崖壁永远贴在地图外圈；
//      ③ **填充块 + 散落件**：`Wild*` / `Trees*` / `LvlSub Type=6` 的树丛、栅栏、石堆、水洼、沼泽、杂物，
//         以**叠加**方式盖章（只写这些块真的有内容的格）—— 这就是原版"草地上长出一片树林"的做法。
//      块里"纯可走"的格**不覆盖基底草地**：原版那几格本来就是引擎铺的草。
//
//    每一格的**地面与物件瓦片键都来自原版 ds1**（逐格覆盖，见 `GridMap.SetTiles`）。
//    洞穴入口 = 原版 `Act 1 - Cave Entrance` 块（`CAVES/CaveDr*.ds1`）+ 原版洞穴口瓦片。
//
// 随机与可复现：块选择、镜像、散落件位置、土路拐点全部走注入的 `CloverEngine.Rng`
//   ⇒ **同 seed ⇒ 同图；不同 seed ⇒ 不同图**（`MapCheck` 有断言）。
//
// 怪物刷新点：契约写明「洞穴生成时产出；野外为空列表」⇒ 本生成器**不填**
//    `MonsterSpawns`，`MonsterModule` 用 `IMapModule.RandomWalkableTile(rng)` 撒点。
// ─────────────────────────────────────────────────────────────────────────────

using CloverEngine;
using Diablo2.Core;
using Diablo2.Def;
using UnityEngine;

namespace Diablo2.Module.Map
{
    /// <summary>血腥荒野（用原版野外预设块拼出来的随机野外）。</summary>
    public static class MapGenWilderness
    {
        /// <summary>
        /// 每轴的**原版块数** = `GameConst.WildernessMaxSize / MapGenWildLayout.BorderPitch`
        /// = 80 / 8 = **10 块/轴** ⇒ 血腥荒野恒为 **80×80 格**。
        /// <para>
        /// **出处（原版自己的规则表，不是本项目定的）**：`原版资源/d2raw/data/global/excel/Levels.txt`
        /// 「Act 1 - Wilderness 1」（LevelName = Blood Moor）的 `SizeX = 80` / `SizeY = 80`、
        /// `DrlgType = 3`（户外随机）；块边长 8 = `LvlPrest.txt`「Act 1 - Wild Border *」的 `SizeX`。
        /// **复核入口 = `tools/probes/hosts/mapcheck`**：上面两条出处（`原版资源/d2raw/data/global/excel/Levels.txt`
        /// 「Act 1 - Wilderness 1」的 `SizeX/SizeY = 80/80`、`LvlPrest.txt`「Act 1 - Wild Border *」块边长 8）
        /// 由它逐条复核；尺寸断言见该宿主输出的 80×80 段（末行「MapCheck 结束：全部通过」）。
        /// </para>
        /// <para>
        /// 原版**没有**尺寸随机：Wilderness 1 恒 80×80（`Levels.txt` 只给一组 SizeX/SizeY，
        /// 没有尺寸范围列）。
        /// </para>
        /// </summary>
        private const int CellsPerAxis = GameConst.WildernessMaxSize / MapGenWildLayout.BorderPitch;

        /// <summary>土路半宽（格）—— 路占 `2*RoadHalfWidth+1 = 3` 格宽（原版路 2~3 格宽）。</summary>
        private const int RoadHalfWidth = 1;

        /// <summary>
        /// **进区落点距地图边界的最小格数 = 8**。
        /// <para>出处（**实测值**，不是拍脑袋定的）：玩家格 = (1,20)、相机 `ortho=3.75`、
        /// `screen=1920x1080` 下**实测的可见格 AABB** = `corners=[-6,13..8,27]`
        /// ⇒ 可见范围是玩家**左右各 7 格**（x∈[p−6, p+7]）、**上下各 7 格**。
        /// 落点距边界 ≥ 8 格 ⇒ 进区那一刻整屏（含等距投影后的屏幕四角）都落在地图内，
        /// **不会露出图外 Void**。</para>
        /// <para>原版语义（**不是**"把落点钳到地图正中心"）：从罗格营地东侧过桥进荒野，
        /// 落点仍在**荒野西侧那条土路上**（`PaintRoad` 铺的「回城口 → 洞穴口」土路，`gate.y` 那一行），
        /// 只是从"贴着西边界那一列"改成"沿土路往东走进荒野几步"。
        /// 依据：`MapSeam.cs` 文件头引的 `libd2/.../drlg/outdoors/OutRoom.zig:271`
        ///（野外第 0 列 = 城镇关卡最后 1 列，共享边列 ⇒ 西门就是入口方向）。</para>
        /// </summary>
        public const int EntryMarginCells = 8;

        /// <summary>散落件个数范围（原版 `LvlSub` Type=6 也是"少量随机撒"）。</summary>
        private const int ScatterMin = 3, ScatterMax = 8;

        //   （`MapGenWildLayout.Piece.OpenN/S/W/E`，由导出器从该块可走掩码的边带算出）。
        //   "拼接对不对"是数据层的事，宿主必须能直接断言，而不是靠读日志。
        /// <summary>周圈槽位数（= 4·cells − 4）。</summary>
        public static int AuditBorderSlots;

        /// <summary>其中"朝外的边是闭的（崖壁朝外）+ 沿环两邻边是开的（环上连通）"的槽位数。</summary>
        public static int AuditBorderStitched;

        /// <summary>其中"一次就按四个开口条件精确匹配到块"的槽位数（没匹配到的会放宽 + 留日志）。</summary>
        public static int AuditBorderExact;

        /// <summary>
        /// 周圈里**改铺原版城镇过渡带**的槽位数（`GroupBand` 那一块，8×40 = 1 块宽 × 5 槽）。
        /// <para>为什么单列：那几槽**不是**"按四边开口拼的边界块"（它们是 `LvlPrest`
        /// 「Act 1 - Town 1 Transition E」的城镇接缝块）⇒ 断言要减去它们。
        /// 依据 = `libd2/.../drlg/outdoors/OutRoom.zig:251-275`（原版把这条带铺在野外关卡
        /// 靠城那条边上）+ `ActInit.zig:75-83`（只对 Act1 的 2..7 号户外关卡调用）。</para>
        /// </summary>
        public static int AuditBandSlots;

        /// <summary>内部填充槽位数（放下的块数）。</summary>
        public static int AuditInteriorSlots;

        /// <summary>其中"四边全开"的块数（内部块不许把场地中间的通道掐断）。</summary>
        public static int AuditInteriorAllOpen;

        /// <summary>洞穴口物件瓦片键（`Resources/Clover/D2/Objects/cave_door/000.png`）。</summary>
        private const string CaveDoorKey = "cave_door/000";

        /// <summary>生成血腥荒野。**同 seed ⇒ 同地图**。</summary>
        public static void Generate(GridMap map, Rng rng)
        {
            if (map == null || rng == null)
            {
                MapLog.Error("MapGenWilderness.Generate: map/rng 为 null，放弃生成");
                return;
            }
            if (MapGenWildLayout.Pieces == null || MapGenWildLayout.Pieces.Length == 0)
            {
                MapLog.Error("MapGenWilderness: 野外拼块库为空（MapGenWildLayout.cs 是生成物，请重跑 " +
                             "tools/d2codec/export_wild_layout.py）");
                return;
            }

            var pitch = MapGenWildLayout.BorderPitch;
            //   每轴块数 = `CellsPerAxis`（固定值，不再随机 6~10 块 —— 那会生成 48~80 的小图）。
            var cells = CellsPerAxis;
            var w = cells * pitch;
            var h = cells * pitch;

            AuditBorderSlots = AuditBorderStitched = AuditBorderExact = 0;
            AuditBandSlots = 0;
            AuditInteriorSlots = AuditInteriorAllOpen = 0;

            map.Reset(AreaId.BloodMoor, w, h, rng.Seed);
            map.Fill(TileKind.Grass);
            map.BeginTileOverrides();

            // ① 基底草地：每格一张**原版** `TOWN/floor.dt1` 草地瓦片
            FillBaseGround(map, rng);

            // ② 两个出入口落在哪一"行"块上（回城口在西、洞穴口在东；相隔 ≥2 块避免门口对穿）
            var gateRow = rng.Next(1, cells - 1);
            var caveRow = gateRow;
            for (var guard = 0; guard < 8 && Mathf.Abs(caveRow - gateRow) < 2; guard++)
            {
                caveRow = rng.Next(1, cells - 1);
            }

            // ③ 周圈：原版边界块（崖壁 / 石墙 / 树线），按所在边镜像 ⇒ 崖壁贴外圈
            //    西边界上正对回城口那一段改铺**原版城镇过渡带**（`GroupBand`，8×40 = 5 槽）：
            //      原版引擎就是这么铺的（`OutRoom.zig:251-275` 把 `Act 1 - Town 1 Transition E`
            //      放在野外关卡的西边界；`ActInit.zig:75-83` 只对 Act1 的 2..7 号户外关卡调用）。
            var covered = new bool[cells, cells];
            var bandSlots = 5;
            var bandFrom = Mathf.Clamp(gateRow - bandSlots / 2, 0,
                                       Mathf.Max(0, cells - bandSlots));
            StampBorderRing(map, rng, cells, covered, bandFrom, bandFrom + bandSlots - 1);

            // ④ 内部：原版填充块（Fence Fill / Tree Fill）叠加成片树丛与木栅栏
            ScatterFill(map, rng, cells, covered);

            // ⑤ LvlSub Type=6 散落件（石堆 / 树 / 水洼 / 沼泽 / 野外杂物）
            ScatterProps(map, rng, cells);

            // ⑥ 洞穴入口 = 原版 `Act 1 - Cave Entrance` 块（`CAVES/CaveDr*.ds1`）
            StampEntrance(map, rng, cells, caveRow);

            // ⑦ 土路：回城口 → （拐一次）→ 洞穴口；顺带把两侧出入口打通
            var gateY = gateRow * pitch + pitch / 2;
            var caveY = caveRow * pitch + pitch / 2;
            PaintRoad(map, rng, w, gateY, caveY);

            //   原版语义：最外一圈 8 格块 = `LvlPrest`「Act 1 - Wild Border *」= 崖壁 + 树线，
            //   相机怎么夹都会露虚空（`camera-follow` 片已证：零虚空要求机位离边界 ≥ 7.083 格）。
            //   ⇒ 封住外圈（不缩地图：外圈瓦片照旧铺，画面 = 一圈树线/崖壁，不是黑虚空）。
            var sealedCells = map.SealBorderRing(GridMap.BorderRingCells, TileKind.Tree);
            MapLog.Info($"MapGenWilderness: 边界环封闭 {sealedCells} 格" +
                        $"（n={GridMap.BorderRingCells} = 原版 `LvlPrest`「Act 1 - Wild Border *」块边长 8，" +
                        $"> 相机实测可见格半跨 7.083）⇒ 可走区离四边界恒 ≥ {GridMap.BorderRingCells} 格");

            // ⑧ 出入口落在**边界环的内沿**（环本身不可走 ⇒ 门口不能再压在地图第 0 列 / 最后一列）
            var gate = new Vector2Int(GridMap.BorderRingCells, gateY);
            var cave = new Vector2Int(w - 1 - GridMap.BorderRingCells, caveY);
            map.Set(gate, TileKind.Exit);
            map.Set(cave, TileKind.Exit);
            map.Exits.Add(gate);
            map.Exits.Add(cave);
            map.CaveEntrance = cave;
            ApplyCaveDoor(map, rng, cave);

            // ⑧ 出生点：土路上第一个 3×3 全可走的格（**不挖洞**，只挑现成的）
            var spawn = PickSpawn(map, gate);
            if (!spawn.HasValue)
            {
                MapLog.Warn("MapGenWilderness: 土路上找不到 3×3 全可走的格（原版块把路掐断了？）" +
                            "⇒ 就地净空 3×3（本条属非预期分支，正常不该出现）");
                spawn = new Vector2Int(
                    Mathf.Clamp(gate.x + EntryMarginCells, EntryMarginCells, w - 1 - EntryMarginCells),
                    Mathf.Clamp(gateY, EntryMarginCells, h - 1 - EntryMarginCells));
                map.ClearAround(spawn.Value, TileKind.Road, 1);
            }
            map.SpawnPoint = spawn.Value;

            // ⑨ 连通性目标（必须在「填孤立口袋」之前登记，否则会被当成孤立区误填）
            map.RequiredReachable.AddRange(map.Exits);
            map.FillUnreachablePockets(map.SpawnPoint, TileKind.Rock);

            MapLog.Info($"MapGenWilderness: 原版块拼接的血腥荒野生成完成（size={w}x{h}" +
                        $"（= 原版 `Levels.txt`「Act 1 - Wilderness 1」的 SizeX/SizeY=80/80，**固定不随机**）" +
                        $" 块网格={cells}x{cells} 每块 {pitch} 格 seed={rng.Seed} 出生点={map.SpawnPoint} " +
                        $"回城口={gate} 洞穴入口={cave} 可走={map.WalkableCount} 障碍={map.BlockedCount}）");
        }

        // ── 基底草地 ─────────────────────────────────────────────────────────

        /// <summary>每格铺一张原版草地瓦片（原版 `TOWN/floor.dt1`，LvlType 2 的槽 1）。</summary>
        private static void FillBaseGround(GridMap map, Rng rng)
        {
            var tiles = MapGenWildLayout.GrassTiles;
            if (tiles == null || tiles.Length == 0)
            {
                MapLog.Error("MapGenWilderness: 原版草地瓦片表为空（生成物被改坏？）⇒ 本图地面会全空");
                return;
            }
            for (var x = 0; x < map.Width; x++)
            {
                for (var y = 0; y < map.Height; y++)
                {
                    map.SetTiles(x, y, tiles[rng.Next(tiles.Length)], "");
                }
            }
        }

        // ── 周圈（原版边界块）────────────────────────────────────────────────

        /// <summary>
        /// 周圈每格 ← 一块**原版边界块**，**按该块的"四边开口"标志拼接**（原版语义，
        /// `MapGenWildLayout.Piece.OpenN/S/W/E` 由导出器从块的可走掩码边带算出）：
        /// <para>· **朝外的那条边必须是"闭"的**（`Open* == false`）= 崖壁朝地图外圈；</para>
        /// <para>· **沿环上相邻的两条边必须是"开"的** = 崖壁带连续、周圈能走通；</para>
        /// <para>· **朝内那条边必须是"开"的** = 内部草地接得上周圈。</para>
        /// 四条条件在「块 × 是否左右翻 × 是否上下翻」里精确匹配（翻 = 原版对每条边用不同
        /// 朝向的块，本项目用镜像表达同一件事）；一个都匹配不到才放宽并留日志。
        /// </summary>
        private static void StampBorderRing(GridMap map, Rng rng, int cells, bool[,] covered,
            int bandFrom, int bandTo)
        {
            var placed = 0;
            var blocked = 0;
            var exact = 0;
            var bandSlots = 0;
            var band = BandPiece();
            if (band == null)
            {
                MapLog.Warn("MapGenWilderness: 拼块库里没有城镇过渡带（Group=Band）⇒ 西边界全按边界块铺" +
                            "（生成物没跟上？重跑 tools/d2codec/export_wild_layout.py）");
                bandFrom = bandTo = -1;                 // 关掉这条分支
            }
            for (var i = 0; i < cells; i++)
            {
                for (var j = 0; j < cells; j++)
                {
                    if (i != 0 && j != 0 && i != cells - 1 && j != cells - 1) continue;

                    // 西边界正对回城口那一段 = **原版城镇过渡带**（不是崖壁边界块）。
                    //   整条带只在它的第一槽盖一次章（带本身 8×40 = 5 槽，一次铺满）。
                    if (i == 0 && j >= bandFrom && j <= bandTo)
                    {
                        if (j == bandFrom) blocked += Stamp(map, band, 0, bandFrom, false, false);
                        covered[i, j] = true;
                        bandSlots++;
                        continue;
                    }

                    // 该槽位要求的"开口"（N/S/W/E 各自：mustOpen / mustClosed / don't care）
                    var n = Req.Free;
                    var s = Req.Free;
                    var w = Req.Free;
                    var e = Req.Free;

                    // 朝外 = 闭
                    if (j == 0) n = Req.Closed;                 // 北边一列：朝外是北
                    if (j == cells - 1) s = Req.Closed;         // 南边一列：朝外是南
                    if (i == 0) w = Req.Closed;
                    if (i == cells - 1) e = Req.Closed;

                    // 沿环相邻 = 开（崖壁带连续）；朝内 = 开（内部接得上）
                    if (j == 0)
                    {
                        if (i > 0) w = Req.Open;
                        if (i < cells - 1) e = Req.Open;
                        s = Req.Open;
                    }
                    else if (j == cells - 1)
                    {
                        if (i > 0) w = Req.Open;
                        if (i < cells - 1) e = Req.Open;
                        n = Req.Open;
                    }
                    else if (i == 0)
                    {
                        n = Req.Open;
                        s = Req.Open;
                        e = Req.Open;
                    }
                    else if (i == cells - 1)
                    {
                        n = Req.Open;
                        s = Req.Open;
                        w = Req.Open;
                    }

                    var piece = PickBorderByOpen(rng, n, s, w, e, out var flipX, out var flipY,
                                                 out var isExact);
                    if (piece == null)
                    {
                        MapLog.Error("MapGenWilderness: 边界块库里没有 Group=Border 的块 ⇒ 周圈无法铺装");
                        return;
                    }
                    if (isExact) exact++;
                    blocked += Stamp(map, piece, i, j, flipX, flipY);
                    covered[i, j] = true;
                    placed++;
                }
            }

            var ring = 4 * cells - 4;
            AuditBorderSlots = ring;
            AuditBorderStitched = placed;
            AuditBorderExact = exact;
            AuditBandSlots = bandSlots;
            MapLog.Info($"MapGenWilderness: 周圈 {placed}/{ring - bandSlots} 块原版边界块，**按四边开口拼接**" +
                        $"（朝外闭 + 沿环开 + 朝内开）：精确匹配 {exact}/{placed} 块；" +
                        $"另有 {bandSlots} 槽（西边界 x 带 0、槽 {bandFrom}..{bandTo}）改铺**原版罗格营地" +
                        $"过渡带**「Act 1 - Town 1 Transition E」(8×40，出处 LvlPrest Def=2) ⇒ 接缝不再是" +
                        $"崖壁而是营地外那片开阔地；写入阻挡格 {blocked}");
        }

        /// <summary>取那块**城镇过渡带**（`Group=Band`，8×40）。没有 ⇒ null（调用方留日志）。</summary>
        private static MapGenWildLayout.Piece BandPiece()
        {
            var all = MapGenWildLayout.Pieces;
            if (all == null) return null;
            for (var i = 0; i < all.Length; i++)
            {
                if (all[i].Group == MapGenWildLayout.GroupBand) return all[i];
            }
            return null;
        }

        /// <summary>周圈槽位对某条边的要求。</summary>
        private enum Req
        {
            /// <summary>这条边必须是"开"的（该块这边能走出去）。</summary>
            Open,

            /// <summary>这条边必须是"闭"的（崖壁 / 实心）。</summary>
            Closed,

            /// <summary>不要求。</summary>
            Free,
        }

        /// <summary>
        /// 在「Group=Border 的块 × 左右翻 × 上下翻」里挑一块满足四边要求的（蓄水池抽样，均匀随机）。
        /// <para>翻会交换开口标志：左右翻换 W/E，上下翻换 N/S（`EffectiveOpen`）。</para>
        /// <para>精确匹配不到时**放宽为只要求"朝外的边是闭的"**（并留 Warn）；再不行就任意边界块。</para>
        /// </summary>
        private static MapGenWildLayout.Piece PickBorderByOpen(Rng rng, Req n, Req s, Req w, Req e,
            out bool flipX, out bool flipY, out bool exact)
        {
            var all = MapGenWildLayout.Pieces;
            var pick = -1;
            var pickFx = false;
            var pickFy = false;
            var count = 0;

            // 两轮：第一轮要求四条边**全部**命中；第二轮只要求"朝外的边是闭的"（放宽）
            for (var pass = 0; pass < 2 && pick < 0; pass++)
            {
                var strict = pass == 0;
                for (var k = 0; k < all.Length; k++)
                {
                    var p = all[k];
                    if (p.Group != MapGenWildLayout.GroupBorder) continue;
                    for (var m = 0; m < 4; m++)
                    {
                        var fx = (m & 1) != 0;
                        var fy = (m & 2) != 0;
                        EffectiveOpen(p, fx, fy, out var pn, out var ps, out var pw, out var pe);
                        var ok = strict
                            ? Check(n, pn) && Check(s, ps) && Check(w, pw) && Check(e, pe)
                            : CheckClosed(n, pn) && CheckClosed(s, ps)
                              && CheckClosed(w, pw) && CheckClosed(e, pe);
                        if (!ok) continue;
                        count++;
                        if (rng.Next(count) == 0) { pick = k; pickFx = fx; pickFy = fy; }
                    }
                }
                if (strict && pick < 0)
                {
                    MapLog.WarnThrottled("wild.ring.relax", "MapGenWilderness: 周圈有槽位找不到" +
                        "「四条开口条件全中」的边界块 ⇒ 放宽为只要求" +
                        "「朝外那条边是闭的（崖壁朝外）」（本条只报一次）");
                }
            }

            flipX = pickFx;
            flipY = pickFy;
            if (pick < 0)
            {
                exact = false;
                return PickOne(rng, MapGenWildLayout.GroupBorder);   // 兜底：任意边界块
            }
            EffectiveOpen(all[pick], pickFx, pickFy, out var fn, out var fs, out var fw, out var fe);
            exact = Check(n, fn) && Check(s, fs) && Check(w, fw) && Check(e, fe);
            return all[pick];
        }

        /// <summary>某个槽位要求 `req` 与该块的实际开口 `have` 是否相容（`Free` = 不要求）。</summary>
        private static bool Check(Req req, bool have)
            => req == Req.Free || (req == Req.Open) == have;

        /// <summary>放宽口径：只校验"要求闭的边确实是闭的"，开/不要求都不管。</summary>
        private static bool CheckClosed(Req req, bool have) => req != Req.Closed || !have;

        /// <summary>
        /// 块的**四边开口**（翻过之后的）：左右翻交换 W/E，上下翻交换 N/S。
        /// 依据 = `MapGenWildLayout.Piece.OpenN/S/W/E`（导出器从块的可走掩码边带算出，
        /// 见 `tools/d2codec/export_wild_layout.py::edge_open`）。
        /// </summary>
        private static void EffectiveOpen(MapGenWildLayout.Piece p, bool flipX, bool flipY,
            out bool n, out bool s, out bool w, out bool e)
        {
            n = flipY ? p.OpenS : p.OpenN;
            s = flipY ? p.OpenN : p.OpenS;
            w = flipX ? p.OpenE : p.OpenW;
            e = flipX ? p.OpenW : p.OpenE;
        }

        /// <summary>挑一块原版边界块（蓄水池抽样：一次遍历、均匀随机）。</summary>
        private static MapGenWildLayout.Piece PickBorder(Rng rng) => PickOne(rng, MapGenWildLayout.GroupBorder);

        private static MapGenWildLayout.Piece PickOne(Rng rng, int group)
        {
            var all = MapGenWildLayout.Pieces;
            var pick = -1;
            var n = 0;
            for (var i = 0; i < all.Length; i++)
            {
                if (all[i].Group != group) continue;
                n++;
                if (rng.Next(n) == 0) pick = i;
            }
            return pick < 0 ? null : all[pick];
        }

        // ── 内部（原版填充块）────────────────────────────────────────────────

        /// <summary>内部被原版填充块盖住的**目标占比**（原版血腥荒野是"开阔草地 + 几片树林"）。</summary>
        private const float FillCoverage = 0.35f;

        /// <summary>
        /// 在**内部随机撒若干块原版填充块**（16×16 / 16×8 / 8×16 / 8×8 混搭，随机竖放或横放），
        /// 块从 `MapGenWildLayout` 的填充组里随机取 —— 这正是原版 `LvlPrest` 里
        /// 「Fence Fill 1..6 / Tree Fill」那几行的尺寸组合。
        /// <para>**不是把整个内部铺满**：这些块是装饰性的（树丛 / 木栅栏），原版野外是
        /// 「引擎铺满草地 + 零星几片树林」；逐格铺满会让整张图变成一片树林（实测可走率掉到 16%）。
        /// 也不覆盖"纯可走"格 ⇒ 不会把草地切成碎块。</para>
        /// </summary>
        private static void ScatterFill(GridMap map, Rng rng, int cells, bool[,] covered)
        {
            var sizes = new[]
            {
                MapGenWildLayout.GroupFill16,
                MapGenWildLayout.GroupFill16x8,
                MapGenWildLayout.GroupFill8x16,
                MapGenWildLayout.GroupFill8,
            };
            var interior = (cells - 2) * (cells - 2);
            var want = Mathf.Max(2, Mathf.RoundToInt(interior * FillCoverage / 4f));

            var filled = 0;
            var blocked = 0;
            var tries = 0;
            AuditInteriorSlots = 0;
            AuditInteriorAllOpen = 0;
            while (filled < want && tries < want * 8)
            {
                tries++;
                var i = rng.Next(1, cells - 1);
                var j = rng.Next(1, cells - 1);
                if (covered[i, j]) continue;

                var piece = PickFill(rng, sizes[rng.Next(sizes.Length)], cells, i, j, covered);
                if (piece == null) continue;

                blocked += Stamp(map, piece, i, j, false, true);
                AuditInteriorSlots++;
                if (piece.OpenN && piece.OpenS && piece.OpenW && piece.OpenE) AuditInteriorAllOpen++;
                for (var dj = 0; dj < piece.SizeH / MapGenWildLayout.BorderPitch; dj++)
                {
                    for (var di = 0; di < piece.SizeW / MapGenWildLayout.BorderPitch; di++)
                    {
                        covered[i + di, j + dj] = true;
                    }
                }
                filled++;
            }

            var ratio = 100f * filled * 4f / interior;
            MapLog.Info($"MapGenWilderness: 内部放原版填充块 {filled}/{want} 块（约占内部 {ratio:F0}%，" +
                        $"目标 {FillCoverage * 100f:F0}%，写入阻挡格 {blocked}）；" +
                        $"其中**四边全开**的块 {AuditInteriorAllOpen}/{AuditInteriorSlots}" +
                        $"（内部块不许把场地中间的通道掐断，见 PickFill）；" +
                        $"其余内部 = 基底草地（原版也是引擎先铺满草）");
        }

        /// <summary>在 (i,j) 处挑一块该尺寸、落在内部且不与已覆盖格重叠的原版填充块。</summary>
        private static MapGenWildLayout.Piece PickFill(Rng rng, int group, int cells, int i, int j,
            bool[,] covered)
        {
            var all = MapGenWildLayout.Pieces;
            var pitch = MapGenWildLayout.BorderPitch;
            var pick = -1;
            var n = 0;
            for (var k = 0; k < all.Length; k++)
            {
                var p = all[k];
                if (p.Group != group) continue;

                // 内部填充块必须**四边全开**（`OpenN/S/W/E` 全 true）：这类块是"草地 +
                //   树丛/木栅栏"的装饰块，四边全开才不会在场地中间形成一段"四面不通"的墙
                //   —— 这正是"块与块按出口拼接"在内部落地的形式（原版野外内部就是开阔草地）。
                if (!p.OpenN || !p.OpenS || !p.OpenW || !p.OpenE) continue;

                var cw = p.SizeW / pitch;
                var ch = p.SizeH / pitch;
                if (i + cw > cells - 1 || j + ch > cells - 1) continue;   // 必须留在内部

                var free = true;
                for (var dj = 0; dj < ch && free; dj++)
                {
                    for (var di = 0; di < cw; di++)
                    {
                        if (covered[i + di, j + dj]) { free = false; break; }
                    }
                }
                if (!free) continue;

                n++;
                if (rng.Next(n) == 0) pick = k;
            }
            return pick < 0 ? null : all[pick];
        }

        // ── 散落件（LvlSub Type=6）──────────────────────────────────────────

        /// <summary>随机撒几件原版散落件（叠加式）。</summary>
        private static void ScatterProps(GridMap map, Rng rng, int cells)
        {
            var want = rng.Next(ScatterMin, ScatterMax + 1);
            var list = new System.Collections.Generic.List<MapGenWildLayout.Piece>();
            var all = MapGenWildLayout.Pieces;
            for (var i = 0; i < all.Length; i++)
            {
                if (all[i].Group == MapGenWildLayout.GroupScatter) list.Add(all[i]);
            }
            if (list.Count == 0)
            {
                MapLog.Warn("MapGenWilderness: 拼块库里没有散落件（Group=Scatter）⇒ 本图不撒野外杂物");
                return;
            }

            var blocked = 0;
            for (var k = 0; k < want; k++)
            {
                var piece = list[rng.Next(list.Count)];
                var i = rng.Next(1, cells - 1);
                var j = rng.Next(1, cells - 1);
                blocked += Stamp(map, piece, i, j, false, true);
            }
            MapLog.Info($"MapGenWilderness: 原版散落件 {want} 件（LvlSub Type=6：石堆/树/水洼/沼泽/杂物，" +
                        $"写入阻挡格 {blocked}）");
        }

        // ── 洞穴入口 ─────────────────────────────────────────────────────────

        /// <summary>
        /// 洞穴口所在的那一"行"块 ← 原版 `Act 1 - Cave Entrance`（`CAVES/CaveDr1,2.ds1`）。
        /// 该块在原版里就是把野外与洞穴口接上的那一块。
        /// </summary>
        private static void StampEntrance(GridMap map, Rng rng, int cells, int caveRow)
        {
            var piece = PickOne(rng, MapGenWildLayout.GroupEntrance);
            if (piece == null)
            {
                MapLog.Warn("MapGenWilderness: 拼块库里没有洞穴入口块（Group=Entrance）" +
                            "⇒ 洞口只有土路、没有洞口贴图");
                return;
            }
            var blocked = Stamp(map, piece, cells - 1, caveRow, false, false);
            MapLog.Info($"MapGenWilderness: 洞穴口块 = {piece.Name}（原版出处 {piece.Source}，" +
                        $"写入阻挡格 {blocked}）");
        }

        /// <summary>把洞穴口的物件层换成原版洞穴口瓦片（`CAVES/cavedr.dt1`）。</summary>
        private static void ApplyCaveDoor(GridMap map, Rng rng, Vector2Int cave)
        {
            if (!map.TryGetTiles(cave.x, cave.y, out var ground, out _))
            {
                MapLog.Warn("MapGenWilderness: 洞穴口格没有逐格瓦片覆盖（覆盖未启用？）⇒ 洞口不画洞穴口瓦片");
                return;
            }
            if (string.IsNullOrEmpty(ground))
            {
                ground = MapGenWildLayout.GrassTiles[rng.Next(MapGenWildLayout.GrassTiles.Length)];
            }
            map.Set(cave, TileKind.Exit);
            map.SetTiles(cave.x, cave.y, ground, CaveDoorKey);
        }

        // ── 土路 ─────────────────────────────────────────────────────────────

        /// <summary>
        /// 从回城口铺到洞穴口：2 条横向段 + 1 条竖直段（拐一次，像原版的路）；
        /// 路宽 `2*RoadHalfWidth+1 = 3` 格 —— **出生点 3×3 净空因此天然成立**（不额外挖洞）。
        /// 地面用**原版**泥土/土路瓦片（`TOWN/floor.dt1` 里非草地的那批）。
        /// </summary>
        private static void PaintRoad(GridMap map, Rng rng, int w, int gateY, int caveY)
        {
            var tiles = MapGenWildLayout.DirtTiles;
            if (tiles == null || tiles.Length == 0)
            {
                MapLog.Error("MapGenWilderness: 原版泥土瓦片表为空 ⇒ 土路只能用占位色（生成物被改坏？）");
            }
            var mid = Mathf.Clamp(w / 2, 1, w - 2);
            var painted = 0;

            for (var x = 0; x < mid; x++) painted += PaintRoadCell(map, rng, tiles, x, gateY);
            var step = caveY >= gateY ? 1 : -1;
            for (var y = gateY; y != caveY + step; y += step)
            {
                painted += PaintRoadCell(map, rng, tiles, mid, y);
            }
            for (var x = mid; x < w; x++) painted += PaintRoadCell(map, rng, tiles, x, caveY);

            MapLog.Info($"MapGenWilderness: 土路已铺 {painted} 格（宽 {2 * RoadHalfWidth + 1} 格，" +
                        $"回城口 y={gateY} → 拐点 x={mid} → 洞穴口 y={caveY}）");
        }

        /// <summary>在 (x,y) 及其上下各 1 格铺土路（`TileKind.Road` + 原版泥土瓦片）。</summary>
        private static int PaintRoadCell(GridMap map, Rng rng, string[] tiles, int x, int y)
        {
            var n = 0;
            for (var d = -RoadHalfWidth; d <= RoadHalfWidth; d++)
            {
                var gy = y + d;
                if (!map.InBounds(x, gy)) continue;
                map.Set(x, gy, TileKind.Road);
                if (tiles != null && tiles.Length > 0) map.SetTiles(x, gy, tiles[rng.Next(tiles.Length)], "");
                n++;
            }
            return n;
        }

        // ── 出生点 ───────────────────────────────────────────────────────────

        /// <summary>
        /// 土路上第一个 3×3 全可走的格（**距四边界 ≥ <see cref="EntryMarginCells"/> 格**，仍尽量靠回城口那一侧）。
        /// </summary>
        private static Vector2Int? PickSpawn(GridMap map, Vector2Int gate)
        {
            var minX = Mathf.Max(gate.x + 1, EntryMarginCells);
            var maxX = map.Width - 1 - EntryMarginCells;
            if (maxX < minX) maxX = minX;
            for (var x = minX; x <= maxX; x++)
            {
                var g = new Vector2Int(x, gate.y);
                if (map.IsSpawnClear(g, 1)) return g;
            }
            return null;
        }

        // ── 盖章 ─────────────────────────────────────────────────────────────

        /// <summary>
        /// 把一块原版预设**叠加**到槽 (ci,cj) 上：
        /// <para>· 该块里"纯可走"的格**不写**（原版那几格本来就是引擎铺的草地，见文件头 ①）；
        /// · 该块里"原版什么都没有"的格也**不写**（`trees.ds1` 这类只有物件层的子块就是这样）；
        /// · 只有"真的画了东西"的格才落下来：地形改成对应 `TileKind`、地面/物件瓦片键照抄原版
        ///   （地面为空的格保留已有地面，避免把基底草地抹掉）。</para>
        /// <param name="flipX">true ⇒ 块的列序左右翻转（东边界用）。</param>
        /// <param name="flipY">true ⇒ 块的"南半"落到本格低 gy（南边界用）。</param>
        /// <returns>本次写入的阻挡格数。</returns>
        private static int Stamp(GridMap map, MapGenWildLayout.Piece piece, int ci, int cj,
            bool flipX, bool flipY)
        {
            var pitch = MapGenWildLayout.BorderPitch;
            var written = 0;
            for (var py = 0; py < piece.SizeH; py++)
            {
                var dy = flipY ? (piece.SizeH - 1 - py) : py;
                var gy = cj * pitch + dy;
                for (var px = 0; px < piece.SizeW; px++)
                {
                    var dx = flipX ? (piece.SizeW - 1 - px) : px;
                    var gx = ci * pitch + dx;
                    if (!map.InBounds(gx, gy)) continue;
                    if (!Decode(piece, py, px, out var kindChar, out var classChar,
                                out var ground, out var obj)) continue;

                    if (kindChar == '.') continue;                       // 纯可走：保持基底草地
                    if (kindChar == ' ' && string.IsNullOrEmpty(ground)
                        && string.IsNullOrEmpty(obj)) continue;          // 原版这格什么都没有

                    map.Set(gx, gy, KindOf(classChar));
                    if (string.IsNullOrEmpty(ground))
                    {
                        // 只有物件层（如原版 `trees.ds1`）⇒ 保留已有地面瓦片键
                        map.TryGetTiles(gx, gy, out var keepGround, out _);
                        map.SetTiles(gx, gy, keepGround, obj);
                    }
                    else
                    {
                        map.SetTiles(gx, gy, ground, obj);
                    }
                    written++;
                }
            }
            return written;
        }

        /// <summary>阻挡物种类码 → 本项目 `TileKind`（码的含义见 `MapGenWildLayout` 文件头）。</summary>
        private static TileKind KindOf(char classChar)
        {
            switch (classChar)
            {
                case 'T': return TileKind.Tree;      // 树丛 / 单棵树
                case 'F': return TileKind.Fence;     // 木栅栏
                case 'W': return TileKind.Wall;      // 石矮墙 / 废墟 / 村舍
                case 'C': return TileKind.Rock;      // 崖壁
                case 'S': return TileKind.Rock;      // 碎石
                case 'O': return TileKind.Rock;      // 野外杂物
                case 'X': return TileKind.Rock;      // 水（原版水是阻挡）
                case 'R': return TileKind.Rock;      // 其它阻挡
                default:
                    MapLog.WarnThrottled("wild.kind." + (int)classChar,
                        $"MapGenWilderness: 未知的阻挡物种类码 '{classChar}'（(int)={(int)classChar}）" +
                        "⇒ 按 Rock 处理（生成物被改坏？）");
                    return TileKind.Rock;
            }
        }

        /// <summary>解一块里 (px,py)（0,0 = 块左上）的 `<kind><class><ground6><object6>`。</summary>
        private static bool Decode(MapGenWildLayout.Piece piece, int py, int px, out char kindChar,
            out char classChar, out string ground, out string obj)
        {
            kindChar = ' ';
            classChar = 'R';
            ground = "";
            obj = "";

            var idx = MapGenWildLayout.CellIndex(piece.Cells, py * piece.SizeW + px);
            if (idx < 0 || idx >= piece.Alphabet.Length)
            {
                MapLog.WarnThrottled("wild.cell.bad",
                    $"MapGenWilderness: 块 {piece.Name} 的格 ({px},{py}) 下标 {idx} 越界，跳过该格");
                return false;
            }

            var code = piece.Alphabet[idx];
            kindChar = code.Length > 0 ? code[0] : 'X';
            classChar = code.Length > 1 ? code[1] : 'R';
            ground = MapGenWildLayout.Decode(code.Length >= 8 ? code.Substring(2, 6) : "------");
            obj = MapGenWildLayout.Decode(code.Length >= 14 ? code.Substring(8, 6) : "------");
            return true;
        }
    }
}
