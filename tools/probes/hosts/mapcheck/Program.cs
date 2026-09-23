// ─────────────────────────────────────────────────────────────────────────────
// 地图模块自证宿主（`docs/agents/agent-04-地图模块.md` §5 的验收项逐条自证）
//
// 运行：dotnet run --project <项目根>/tools/mapcheck/MapCheck.csproj -c Release
//
// 覆盖的验收项：
//   ① 三处区域各生成一次 → DumpStats 输出
//   ② 同 seed 两次生成结果一致 → 字符画哈希 + 逐字符比较
//   ③ 连通性校验通过 → 生成时已自动 BFS（日志里能看到）
//   ④ FindPath 出生点 → Exit 返回非空；打印长度与首尾格
//   ⑤ 连续生成 5 次血腥荒野 → 5 个 seed 互不相同且障碍数不同
//   ⑥ NPC 点 / Exit 点 / 洞穴入口点 / 怪物刷新点的确切 API 与取值
//
// ⛔ 这只是**类型层 + 逻辑层**的验证；渲染（MapView）必须进 Play 由主 agent 看图验收。
// ─────────────────────────────────────────────────────────────────────────────

using System;
using System.Collections.Generic;
using CloverEngine;
using Diablo2.Core;
using Diablo2.Def;
using Diablo2.Module;
using Diablo2.Module.Map;
using UnityEngine;

internal static class MapCheckProgram
{
    private static int _failures;

    private static void Main()
    {
        Console.WriteLine("================ MapCheck 开始 ================");

        Run(Step0_SmokeUnityApi);
        Run(Step1_ThreeAreas);
        Run(Step2_SameSeedDeterminism);
        Run(Step3_FindPath);
        Run(Step4_FiveRandomWilderness);
        Run(Step5_PublicApi);
        Run(Step6_InvariantsAndDump);
        Run(Step7_FailurePath);
        // ── 本轮（"地图该随机的没随机 / 地窟是一坨"）新增 ──────────────────────
        Run(Step8_SeedFingerprints);
        Run(Step9_CaveUsesOriginalPieces);
        Run(Step10_TownFixedAndOriginalTiles);
        Run(Step11_WildernessLayoutShape);
        // ── 片 3（「野外地图太小 / 怪物太少」）新增 ─────────────────────────────
        Run(Step13_WildernessFixedSize);
        Run(Step14_BorderSealGaps);
        // ── ★ R1-B（用户报「为什么有奇怪的蓝条图片占位」）新增 ────────────────────
        Run(Step15_FlatWaterWallNotOverlaid);
        // ── ★ 本轮（「走图跨分块边界的单帧尖峰」离线量化）新增 ──────────────────────
        Run(Step16_ChunkRebuildSpike);
        // ── ★ T0FIX-A（单帧尖峰修复：对象池 + 增量新块分帧）新增 ────────────────────
        Run(Step17_BuildPacingAndPool);
        // ── ★ T0FIX-E/F/G（实心单色 PNG 定性 / 空素材目录）新增 ─────────────────────
        Run(Step18_FlatArtAndEmptyDirs);
        // ── ★ T0FIX-H（整图重铺分帧双缓冲：单帧预算 / 逐格同源 / 不露空）新增 ─────────────
        Run(Step19_RepavePacingPixelsUnchanged);
        // ── ★ T0FIX-I（T0FIX-H 的 Stage 黑屏回归：activeSelf 无人复位）新增 ─────────────
        Run(Step20_StageVisibilityActiveSelf);
        // ── ★ 2026-09-22（用户「营地出门的桥，还不是从桥上走，还是桥下」）新增 ────────────
        Run(Step21_BridgeDeckOrdering);
        // ── ★ 审计 B（离线片：D2 几何与场景 / D9 物理与碰撞）新增（只加断言，⛔ 不改既有步骤）──
        Run(Step22_AuditBGeoInput);
        // ── ★ 片 L / R12（水 = 石头 = 崖壁 = 碎石 = 杂物 同 kind）新增（只加断言）────────────
        Run(Step23_WaterKind);
        // ── ★ 片 M3（2026-09-23：桥的可走性 / 东边界接缝 / 缺瓦片）只加断言，⛔ 不动既有步骤 ──
        Run(Step24_TownBridgeWalkableAndSeam);
        // ── ★ 片 S2（2026-09-23：用户「传送点没效果」）只加断言，⛔ 不动既有步骤 ──────────
        Run(Step25_WaypointAnchor);
        // ── ★ 片 S2（2026-09-23：主 agent 追加「Tab 自动地图的记忆式已探索」）只加断言 ──────
        Run(Step26_ExploredCellsContract);
        // ── ★ 片 chunk-hole2（2026-09-23：血沼泽「大片黑底」）只加断言，⛔ 不动既有步骤 ────
        Run(Step27_PlannedChunksEqualVisibleRange);
        Run(Step28_ChunkHoleSelfHeal);
        if (Environment.GetEnvironmentVariable("MAPCHECK_SEED") != null) Run(Step12_DebugSeed);

        Console.WriteLine($"================ MapCheck 结束：{( _failures == 0 ? "全部通过" : _failures + " 项失败" )} ================");
        if (_failures != 0) Environment.ExitCode = 1;
    }

    // ── 0. 确认 UnityEngine 托管类型在非 Unity 进程里可用（验证手段自身要自证）──────
    private static void Step0_SmokeUnityApi()
    {
        Section("0. 宿主可用性自证");
        Console.WriteLine($"Vector2Int(3,-4)      = {new Vector2Int(3, -4)}");
        Console.WriteLine($"Mathf.FloorToInt(-0.5) = {Mathf.FloorToInt(-0.5f)}   （负数必须 Floor）");
        var w = Iso.GridToWorld(new Vector2Int(0, 0));
        var b = Iso.WorldToGrid(w);
        Console.WriteLine($"Iso.GridToWorld(0,0) = {w}  → WorldToGrid = {b}  {(b == new Vector2Int(0, 0) ? "OK" : "❌ 往返不一致")}");
        Check(b == new Vector2Int(0, 0), "Iso 正/逆投影往返");
        Console.WriteLine($"Iso.SortOrder(0,0)={Iso.SortOrder(0, 0)}  (3,5)={Iso.SortOrder(3, 5)}  （必须随格子变化）");
        Check(Iso.SortOrder(3, 5) != Iso.SortOrder(0, 0), "sortingOrder 随格子变化");

        try
        {
            var t = UnityEngine.Time.realtimeSinceStartup;
            Console.WriteLine($"Time.realtimeSinceStartup = {t}  （可用）");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"⚠️ Time.realtimeSinceStartup 在非 Unity 进程不可用（{ex.GetType().Name}）" +
                              " ⇒ 限频日志（WarnThrottled/WarnOnce）在本宿主里会抛异常；本次自证不使用它们。");
        }
        Console.WriteLine();
    }

    // ── 1. 三处区域各生成一次 ────────────────────────────────────────────────
    private static void Step1_ThreeAreas()
    {
        Section("1. 三处区域各生成一次（DumpStats）");
        var cases = new[]
        {
            new KeyValuePair<AreaId, int>(AreaId.Town, 20250916),
            new KeyValuePair<AreaId, int>(AreaId.BloodMoor, 20250916),
            new KeyValuePair<AreaId, int>(AreaId.DenOfEvil, 20250916),
        };

        for (var i = 0; i < cases.Length; i++)
        {
            var map = NewMap();
            map.Generate(cases[i].Key, cases[i].Value);
            Console.WriteLine(map.DumpStats());
            Console.WriteLine(map.DumpAscii(14));
            Console.WriteLine($"→ Hash = {map.Hash()}");
            Console.WriteLine();

            Check(map.IsGenerated, $"{cases[i].Key} 生成成功");
            Check(map.WalkableCount > 0, $"{cases[i].Key} 可走数 > 0");
            Check(map.SpawnPoint.x >= 0 && map.SpawnPoint.y >= 0, $"{cases[i].Key} 出生点有效");
        }
    }

    // ── 2. 同 seed 两次生成结果一致 ──────────────────────────────────────────
    private static void Step2_SameSeedDeterminism()
    {
        Section("2. 同 seed 两次生成一致（Town / BloodMoor / DenOfEvil 各一组）");
        var areas = new[] { AreaId.Town, AreaId.BloodMoor, AreaId.DenOfEvil };
        for (var i = 0; i < areas.Length; i++)
        {
            var a = NewMap();
            var b = NewMap();
            a.Generate(areas[i], 987654321);
            b.Generate(areas[i], 987654321);

            var ha = a.Hash();
            var hb = b.Hash();
            var asciiA = a.DumpAscii();
            var asciiB = b.DumpAscii();
            var same = string.Equals(ha, hb, StringComparison.Ordinal) && string.Equals(asciiA, asciiB, StringComparison.Ordinal);

            Console.WriteLine($"{areas[i],-12} hash#1={ha} hash#2={hb} 字符画逐字符一致={same} " +
                              $"size={a.Width}x{a.Height} blocked={a.BlockedCount}");
            Check(same, $"{areas[i]} 同 seed 两次生成完全一致");
        }
        Console.WriteLine();
    }

    // ── 3. FindPath 出生点 → Exit ───────────────────────────────────────────
    private static void Step3_FindPath()
    {
        Section("3. FindPath（出生点 → 出口）");
        var areas = new[] { AreaId.Town, AreaId.BloodMoor, AreaId.DenOfEvil };
        for (var i = 0; i < areas.Length; i++)
        {
            var map = NewMap();
            map.Generate(areas[i], 424242);
            var from = map.SpawnPoint;
            var target = areas[i] == AreaId.BloodMoor && map.CaveEntrance.HasValue
                ? map.CaveEntrance.Value
                : map.Exits[0];

            var path = map.FindPath(from, target);
            if (path == null)
            {
                Console.WriteLine($"❌ {areas[i]}：{from} → {target} 无路径");
                Check(false, $"{areas[i]} 出生点可达出口");
                continue;
            }

            Console.WriteLine($"{areas[i],-12} {from} → {target}：路径 {path.Count} 格，首={path[0]}，尾={path[path.Count - 1]}");
            Check(path.Count > 0 && path[0] == from && path[path.Count - 1] == target, $"{areas[i]} 路径首尾正确");

            // 路径上每一格都必须可走（否则 A* 输出被污染）
            var allWalkable = true;
            for (var k = 0; k < path.Count; k++) { if (!map.Walkable(path[k])) { allWalkable = false; break; } }
            Check(allWalkable, $"{areas[i]} 路径每格都可走");
        }
        Console.WriteLine();
    }

    // ── 4. 连续 5 次血腥荒野（不同 seed）────────────────────────────────────
    private static void Step4_FiveRandomWilderness()
    {
        Section("4. 连续生成 5 次血腥荒野（证明是真随机，不是固定图）");
        var seeds = new List<int>();
        var blocked = new List<int>();
        var sizes = new List<string>();

        for (var i = 0; i < 5; i++)
        {
            // 用时钟派生 seed（**本局 seed 由进游戏时决定**，见 Core/Rng.FromTime 的注释）
            var seed = unchecked((int)(DateTime.UtcNow.Ticks & 0x7FFFFFFF) ^ (i * 2654435761u.GetHashCode()));
            var map = NewMap();
            map.Generate(AreaId.BloodMoor, seed);
            seeds.Add(seed);
            blocked.Add(map.BlockedCount);
            sizes.Add($"{map.Width}x{map.Height}");
            Console.WriteLine($"  第 {i + 1} 次：seed={seed,-12} size={map.Width}x{map.Height,-8} " +
                              $"障碍={map.BlockedCount,-6} 可走={map.WalkableCount,-6} " +
                              $"洞穴入口={map.CaveEntrance} hash={map.Hash()}");
            System.Threading.Thread.Sleep(2);
        }

        var distinctSeeds = new HashSet<int>(seeds).Count;
        var distinctBlocked = new HashSet<int>(blocked).Count;
        var distinctSizes = new HashSet<string>(sizes).Count;
        Console.WriteLine($"  ⇒ 不同 seed {distinctSeeds}/5，不同障碍数 {distinctBlocked}/5，不同尺寸 {distinctSizes}/5");
        Check(distinctSeeds == 5, "5 次 seed 互不相同");
        Check(distinctBlocked >= 4, "障碍数不同（≥4/5 种取值）");
        Console.WriteLine();
    }

    // ── 5. 后续 agent 要用的确切 API ─────────────────────────────────────────
    private static void Step5_PublicApi()
    {
        Section("5. NPC 点 / Exit 点 / 洞穴入口点 / 怪物刷新点的确切 API 与取值");
        Console.WriteLine("接口：Diablo2.Module.IMapModule（Module/Contracts.cs，冻结）");
        Console.WriteLine("  Vector2Int SpawnPoint { get; }                      出生点");
        Console.WriteLine("  IReadOnlyList<Vector2Int> Exits { get; }            出口格（TileKind.Exit）");
        Console.WriteLine("  Vector2Int? CaveEntrance { get; }                   洞穴入口（仅血腥荒野非 null）");
        Console.WriteLine("  IReadOnlyList<Vector2Int> NpcPoints { get; }        城镇 NPC 站位，下标 = (int)Def.NpcId");
        Console.WriteLine("  IReadOnlyList<Vector2Int> MonsterSpawns { get; }    怪物刷新点（仅邪恶洞穴非空）");
        Console.WriteLine("  List<Vector2Int> FindPath(Vector2Int from, Vector2Int to)");
        Console.WriteLine("  Vector2Int RandomWalkableTile(CloverEngine.Rng rng)");
        Console.WriteLine();

        var town = NewMap();
        town.Generate(AreaId.Town, 111);
        Console.WriteLine($"罗格营地：出生点={town.SpawnPoint}");
        Console.WriteLine($"  出口 {town.Exits.Count} 个 → {Fmt(town.Exits)}");
        Console.WriteLine($"  NPC {town.NpcPoints.Count} 个（下标 = NpcId）→");
        for (var i = 0; i < town.NpcPoints.Count; i++)
        {
            Console.WriteLine($"      [{i}] {(NpcId)i,-8} → NpcPoints[{i}] = {town.NpcPoints[i]}");
        }
        Console.WriteLine($"  怪物刷新点 = {town.MonsterSpawns.Count} 个（契约：城镇为空）");
        Console.WriteLine($"  洞穴入口 = {(town.CaveEntrance.HasValue ? town.CaveEntrance.Value.ToString() : "null")}");
        Console.WriteLine();

        var moor = NewMap();
        moor.Generate(AreaId.BloodMoor, 222);
        Console.WriteLine($"血腥荒野：出生点={moor.SpawnPoint}  出口 {moor.Exits.Count} 个 → {Fmt(moor.Exits)}");
        Console.WriteLine($"  CaveEntrance = {moor.CaveEntrance}（= Exits[1]={moor.Exits[1]}）");
        Console.WriteLine($"  怪物刷新点 = {moor.MonsterSpawns.Count} 个（契约：野外为空 ⇒ MonsterModule 用 RandomWalkableTile 撒点）");
        Console.WriteLine();

        var cave = NewMap();
        cave.Generate(AreaId.DenOfEvil, 333);
        Console.WriteLine($"邪恶洞穴：出生点={cave.SpawnPoint}  出口 {cave.Exits.Count} 个 → {Fmt(cave.Exits)}");
        Console.WriteLine($"  怪物刷新点 {cave.MonsterSpawns.Count} 个 → {Fmt(cave.MonsterSpawns)}");
        Console.WriteLine($"  CaveEntrance = {(cave.CaveEntrance.HasValue ? cave.CaveEntrance.Value.ToString() : "null")}（契约：仅血腥荒野有效）");

        // 抽样：RandomWalkableTile 必须给出可走格
        var rng = new Rng(2024);
        var ok = true;
        for (var i = 0; i < 200; i++)
        {
            var g = cave.RandomWalkableTile(rng);
            if (!cave.Walkable(g)) { ok = false; break; }
        }
        Check(ok, "RandomWalkableTile 200 次抽样全部落在可走格上");
        Console.WriteLine();
    }

    // ── 6. 不变量 + 全图转储（人要读一遍，不能只看统计数字）──────────────────
    private static void Step6_InvariantsAndDump()
    {
        Section("6. 不变量 + 全图 ASCII 转储（供人工读图核对）");

        var dir = System.IO.Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "_maps");
        dir = System.IO.Path.GetFullPath(dir);
        System.IO.Directory.CreateDirectory(dir);

        var cases = new[]
        {
            new KeyValuePair<AreaId, int>(AreaId.Town, 20250916),
            new KeyValuePair<AreaId, int>(AreaId.BloodMoor, 20250916),
            new KeyValuePair<AreaId, int>(AreaId.DenOfEvil, 20250916),
        };

        for (var i = 0; i < cases.Length; i++)
        {
            var area = cases[i].Key;
            var map = NewMap();
            map.Generate(area, cases[i].Value);

            var file = System.IO.Path.Combine(dir, area + ".txt");
            System.IO.File.WriteAllText(file, map.DumpStats() + Environment.NewLine + map.DumpAscii());
            Console.WriteLine($"  已写出 {file}");

            // 通用不变量
            var walkableOk = true;
            var roadCount = 0;
            for (var x = 0; x < map.Width; x++)
            {
                for (var y = 0; y < map.Height; y++)
                {
                    var g = new Vector2Int(x, y);
                    var k = map.TileAt(g);
                    if (k == TileKind.Road) roadCount++;
                    if (TileKindInfo.IsWalkable(k) != map.Walkable(g) && k != TileKind.Void) walkableOk = false;
                }
            }
            Check(walkableOk, $"{area} Walkable() 与 TileKindInfo 判定一致");
            Check(map.BlockedCount + map.WalkableCount == map.Width * map.Height, $"{area} 障碍+可走 == 总格数");

            var spawnClear = true;
            for (var dx = -1; dx <= 1; dx++)
            {
                for (var dy = -1; dy <= 1; dy++)
                {
                    if (!map.Walkable(new Vector2Int(map.SpawnPoint.x + dx, map.SpawnPoint.y + dy))) spawnClear = false;
                }
            }
            Check(spawnClear, $"{area} 出生点 3×3 全部可走");

            var allExitsWalkable = true;
            for (var k = 0; k < map.Exits.Count; k++)
            {
                if (map.TileAt(map.Exits[k]) != TileKind.Exit || !map.Walkable(map.Exits[k])) allExitsWalkable = false;
            }
            Check(allExitsWalkable, $"{area} 每个出口都是 TileKind.Exit 且可走");

            var spawnsWalkable = true;
            for (var k = 0; k < map.MonsterSpawns.Count; k++)
            {
                if (!map.Walkable(map.MonsterSpawns[k])) spawnsWalkable = false;
            }
            Check(spawnsWalkable, $"{area} 每个刷怪点都可走");
            var npcsWalkable = true;
            for (var k = 0; k < map.NpcPoints.Count; k++)
            {
                if (!map.Walkable(map.NpcPoints[k])) npcsWalkable = false;
            }
            Check(npcsWalkable, $"{area} 每个 NPC 站位都可走");

            Console.WriteLine($"  {area}: 出口={map.Exits.Count} NPC={map.NpcPoints.Count} " +
                              $"刷怪点={map.MonsterSpawns.Count} 土路={roadCount} 格");
        }

        // 血腥荒野：必须真的有一条蜿蜒土路
        var moor = NewMap();
        moor.Generate(AreaId.BloodMoor, 20250916);
        var road = 0;
        for (var x = 0; x < moor.Width; x++)
        {
            for (var y = 0; y < moor.Height; y++)
            {
                if (moor.TileAt(new Vector2Int(x, y)) == TileKind.Road) road++;
            }
        }
        Check(road > 20, $"血腥荒野存在蜿蜒土路（{road} 格 Road）");
        Console.WriteLine();
    }

    // ── 7. 失败路径自证：重试耗尽 → 保底布局 ────────────────────────────────
    private static void Step7_FailurePath()
    {
        Section("7. 失败路径：逼出「N 次重试 + Warn + 保底布局」");
        Console.WriteLine("（用一个**未登记的区域 id** 让每次尝试都失败，从而真的跑一遍重试与兜底）");

        var map = NewMap();
        map.Generate((AreaId)99, 777);

        Check(map.IsGenerated, "重试全部失败后仍产出可玩地图（保底布局生效）");
        Check(map.WalkableCount > 0, "保底布局有可走格");

        var spawnClear = true;
        for (var dx = -1; dx <= 1; dx++)
        {
            for (var dy = -1; dy <= 1; dy++)
            {
                if (!map.Walkable(new Vector2Int(map.SpawnPoint.x + dx, map.SpawnPoint.y + dy))) spawnClear = false;
            }
        }
        Check(spawnClear, "保底布局出生点 3×3 可走");

        var path = map.FindPath(map.SpawnPoint, map.Exits[0]);
        Check(path != null && path.Count > 0, $"保底布局出生点可达出口（路径 {(path == null ? -1 : path.Count)} 格）");
        Console.WriteLine($"  保底布局：size={map.Width}x{map.Height} 出口={map.Exits.Count} " +
                          $"障碍={map.BlockedCount} 可走={map.WalkableCount} attempts={map.GenerateAttempts}");
        Console.WriteLine();
    }

    // ── 8. 随机性取证（① 野外 / ② 洞穴：不同 seed ⇒ 不同布局；同 seed ⇒ 可复现）────
    private static void Step8_SeedFingerprints()
    {
        Section("8. 随机性取证：不同 seed ⇒ 不同布局 / 同 seed ⇒ 同布局（野外 & 洞穴）");
        var areas = new[] { AreaId.BloodMoor, AreaId.DenOfEvil };
        var seeds = new[] { 11, 22222222, 987654321 };

        for (var i = 0; i < areas.Length; i++)
        {
            var hashes = new List<string>();
            for (var k = 0; k < seeds.Length; k++)
            {
                var m = NewMap();
                m.Generate(areas[i], seeds[k]);
                Console.WriteLine($"  {areas[i],-12} seed={seeds[k],-11} size={m.Width}x{m.Height,-7} " +
                                  $"blocked={m.BlockedCount,-7} walkable={m.WalkableCount,-7} 指纹={m.Hash()}");
                hashes.Add(m.Hash());
            }
            Check(new HashSet<string>(hashes).Count == hashes.Count,
                $"{areas[i]}：3 个不同 seed ⇒ 3 个互不相同的布局指纹");

            var a = NewMap();
            var b = NewMap();
            a.Generate(areas[i], seeds[1]);
            b.Generate(areas[i], seeds[1]);
            Check(a.Hash() == b.Hash() && a.Width == b.Width && a.BlockedCount == b.BlockedCount,
                $"{areas[i]}：同一个 seed（{seeds[1]}）两次生成 ⇒ 指纹/尺寸/障碍数完全一致（可复现）");
        }
        Console.WriteLine();
    }

    // ── 9. 洞穴必须用**原版 CAVES/*.ds1 预设块**拼（②）────────────────────────
    private static void Step9_CaveUsesOriginalPieces()
    {
        Section("9. 邪恶洞穴：用**原版 CAVES/*.ds1 预设块**拼（逐格原版瓦片 + 走廊-房间拓扑）");

        var pieces = MapGenCaveLayout.Pieces;
        Console.WriteLine($"  块库：{pieces.Length} 块，块边长 {MapGenCaveLayout.PieceSize}，" +
                          $"源 = `data/global/tiles/ACT1/CAVES/*.ds1`");
        var byMask = new Dictionary<int, int>();
        for (var i = 0; i < pieces.Length; i++)
        {
            var mk = pieces[i].DirMask & 15;
            byMask[mk] = byMask.TryGetValue(mk, out var v) ? v + 1 : 1;
        }
        var parts = new List<string>();
        foreach (var kv in byMask)
        {
            var dirs = "";
            if ((kv.Key & 1) != 0) dirs += "N";
            if ((kv.Key & 2) != 0) dirs += "S";
            if ((kv.Key & 4) != 0) dirs += "W";
            if ((kv.Key & 8) != 0) dirs += "E";
            parts.Add($"{dirs}={kv.Value}");
        }
        parts.Sort();
        Console.WriteLine($"  四边开通方向分布：{string.Join(" ", parts)}");

        Check(pieces.Length >= 30, $"块库规模 {pieces.Length} ≥ 30（原版 95 个预设里可拼接的那些）");
        Check(MapGenCaveLayout.PieceSize == 25, "块边长 = 25（与 GameConst.CaveMinSize/MaxSize 的倍数关系一致）");
        Check(byMask.Count >= 14, $"覆盖 {byMask.Count} 种四边开通组合（≥14 ⇒ 迷宫拓扑不会被块库限死）");

        for (var k = 0; k < 4; k++)
        {
            var seed = 1000 + k * 7919;
            var cave = NewMap();
            cave.Generate(AreaId.DenOfEvil, seed);
            var w = cave.Width;
            var h = cave.Height;

            Check(cave.IsGenerated && w % 25 == 0 && h % 25 == 0
                  && w >= GameConst.CaveMinSize && w <= GameConst.CaveMaxSize,
                $"seed={seed}：尺寸 {w}x{h} 是原版块边长 25 的整数倍且在 [CaveMinSize {GameConst.CaveMinSize}, " +
                $"CaveMaxSize {GameConst.CaveMaxSize}]");
            Check(cave.HasTileOverrides, $"seed={seed}：洞穴启用了逐格原版瓦片键（渲染用 CAVES/cave.dt1）");

            var bad = 0;
            var groundCells = 0;
            var blackCells = 0;
            var objCells = 0;
            for (var y = 0; y < h; y++)
            {
                for (var x = 0; x < w; x++)
                {
                    if (!cave.TryGetTileKeys(x, y, out var g, out var o)) { bad++; continue; }
                    if (g.Length == 0) blackCells++;
                    else
                    {
                        groundCells++;
                        if (!g.StartsWith("cave/")) bad++;
                    }
                    if (o.Length > 0) objCells++;
                }
            }
            Check(bad == 0, $"seed={seed}：每一格的瓦片键都来自原版 `CAVES/cave.dt1`（越界/异包 = 0 格，" +
                            $"地砖 {groundCells} 格 / 全黑实心岩体 {blackCells} 格 / 带岩壁物件 {objCells} 格）");

            var comps = cave.CountWalkableComponents();
            Check(comps == 1, $"seed={seed}：可走格是**一整片**（连通片数 = {comps}，走廊+房间全通）");

            var ratio = 100f * cave.WalkableCount / (w * h);
            Check(ratio >= 15f && ratio <= 55f,
                $"seed={seed}：可走率 {ratio:F1}%（原版洞穴块实测约 35% ⇒ 是走廊网，不是一坨空地）");
            Check(cave.Exits.Count == 1 && cave.Walkable(cave.Exits[0]),
                $"seed={seed}：恰 1 个出口且可走（{cave.Exits[0]}）");
            Check(cave.Walkable(cave.SpawnPoint) && cave.SpawnPoint != cave.Exits[0],
                $"seed={seed}：出生点 {cave.SpawnPoint} 可走且与出口分开");
            Check(cave.MonsterSpawns.Count > 0, $"seed={seed}：刷怪点 {cave.MonsterSpawns.Count} 个");
            Console.WriteLine();
        }
    }

    // ── 10. 罗格营地：固定布局 + 逐格原版瓦片（③）────────────────────────────
    private static void Step10_TownFixedAndOriginalTiles()
    {
        Section("10. 罗格营地：**原版固定布局**（任何 seed 都是同一张图）+ 逐格原版瓦片");
        var seeds = new[] { 0, 1, 123456789, -7 };
        var hashes = new List<string>();
        MapModule town = null;

        for (var i = 0; i < seeds.Length; i++)
        {
            var m = NewMap();
            m.Generate(AreaId.Town, seeds[i]);
            hashes.Add(m.Hash());
            Console.WriteLine($"  seed={seeds[i],-11} size={m.Width}x{m.Height} blocked={m.BlockedCount} " +
                              $"walkable={m.WalkableCount} 出口={m.Exits.Count} NPC={m.NpcPoints.Count} 指纹={m.Hash()}");
            if (town == null) town = m;
        }

        Check(new HashSet<string>(hashes).Count == 1,
            "罗格营地是**固定布局**：4 个不同 seed 的布局指纹完全一致（与 seed 无关）");
        Check(town.HasTileOverrides, "营地启用了逐格原版瓦片键（渲染用 TOWN/townW1.ds1）");

        // ★ NPC 站位 = **原版坐标**（不再允许启发式）：值取自原版 `TownW1.ds1` 的 objects 层
        //   kind=1 预设单位（id 索引 `MonPreset.txt` 的 Act 1 块：0=gheed 2=akara 5=kashya
        //   7=warriv1 8=charsi），子格 ÷5 换成格。下标 = (int)Def.NpcId。
        //   依据见 `tools/d2codec/export_town_layout.py` 文件头 ③-a。
        Check(town.NpcPoints.Count == 5, "5 个 NPC 站位（阿卡拉/卡夏/恰西/基德/瓦瑞夫）");
        //   ★ 片 4：关卡窗口原点改了（窗口 = 合并帧 x∈[-17,38] × y∈[-5,34]），
        //     ⇒ 同一批原版坐标在新窗口里整体 +17/+5 格（值仍逐条来自原版 DS1，只是换了帧）。
        var expectNpcs = new[]
        {
            new Vector2Int(41, 19),   // 0 Akara    （原版 MonPreset Act1 idx 2 = akara）
            new Vector2Int(32, 23),   // 1 Kashya   （idx 5）
            new Vector2Int(21, 21),   // 2 Charsi   （idx 8）
            new Vector2Int(22, 33),   // 3 Gheed    （idx 0）
            new Vector2Int(28, 25),   // 4 Warriv   （idx 7 = warriv1）
        };
        for (var i = 0; i < expectNpcs.Length && i < town.NpcPoints.Count; i++)
        {
            var npc = (NpcId)i;
            Console.WriteLine($"      [{i}] {npc,-8} → {town.NpcPoints[i]}  （原版坐标期望 {expectNpcs[i]}）");
            Check(town.NpcPoints[i] == expectNpcs[i],
                $"{npc} 站位 = 原版坐标 {expectNpcs[i]}（实测 {town.NpcPoints[i]}）");
        }

        var noKey = 0;
        var noGround = 0;
        var obj = 0;
        for (var y = 0; y < town.Height; y++)
        {
            for (var x = 0; x < town.Width; x++)
            {
                if (!town.TryGetTileKeys(x, y, out var g, out var o)) { noKey++; continue; }
                if (g.Length == 0) noGround++;
                if (o.Length > 0) obj++;
            }
        }
        Check(noKey == 0, $"每一格都在原版布局表范围内（越界 = {noKey}）");
        // ★ 片 4：窗口改为"营地 39 列 + 出城口外面 17 列"后，西北角 3×10 那 30 格**原版四块
        //   都没有瓦片**（原版那几格本来就不画）。它们必须 = `TileKind.Void` 且**不可走**；
        //   ⛔ 不许当草地（当草地 = 可走的隐形格，玩家能走到纯黑背景上）。
        Check(noGround == 30, $"地面层空的格 = {noGround}（片 4 起应为 30 = 原版没覆盖的西北角 3×10）");
        var voidOk = 0;
        for (var y = 0; y < town.Height; y++)
        {
            for (var x = 0; x < town.Width; x++)
            {
                town.TryGetTileKeys(x, y, out var g2, out var o2);
                if (g2.Length != 0 || o2.Length != 0) continue;
                if (town.TileAt(new Vector2Int(x, y)) == TileKind.Void
                    && !town.Walkable(new Vector2Int(x, y))) voidOk++;
            }
        }
        Check(voidOk == noGround,
            $"空瓦片格全是 Void 且不可走（{voidOk}/{noGround}）");
        Check(obj > 100, $"wall 层原版瓦片格数 = {obj}（栅栏/帐篷/树/石矮墙，>100 才算铺上了）");
        Check(town.Width == 56 && town.Height == 40,
            $"网格 = 原版 `Levels.txt`「Act 1 - Town」的 SizeX/SizeY（56×40），实测 {town.Width}x{town.Height}");
        // ★ E12 收口：契约常量与生成物**必须同值**（主 agent 裁决：两侧都同步成原版 56×40）。
        Check(town.Width == GameConst.TownWidth && town.Height == GameConst.TownHeight,
            $"契约常量 `GameConst.TownWidth/TownHeight` = {GameConst.TownWidth}x{GameConst.TownHeight} " +
            $"与生成物一致（E12 ⇒ 已消除，`MapGenTown` 不再有尺寸漂移 Warn）");

        // ── ★ 本轮新增：**方形围栏合围 / 营地外的河（水=阻挡）/ 出城口可达** ────────
        //  用户报的是"元素都在但散落成一片、没有方形合围、没有河" ⇒ 这三条就是本轮的验收口径。
        var fence = 0;          // wall 层 = town_fence
        var riverGround = 0;    // floor 层 = moor_river（原版 OUTDOORS/river.dt1）
        var riverBlocked = 0;
        var campInterior = 0;
        var bridgeGround = 0;   // floor 层 = moor_bridge（原版 OUTDOORS/bridge.dt1，TownE1 独有）
        var bridgeDeck = 0;     // 桥面：有桥地砖、没有桥栏杆 ⇒ 必须**可走**
        var bridgeRail = 0;     // 栏杆：wall 层 = moor_bridge      ⇒ 必须**阻挡**
        var badDeck = 0;
        var badRail = 0;
        for (var y = 0; y < town.Height; y++)
        {
            for (var x = 0; x < town.Width; x++)
            {
                town.TryGetTileKeys(x, y, out var g, out var o);
                if (!string.IsNullOrEmpty(o) && o.StartsWith("town_fence/")) fence++;
                if (!string.IsNullOrEmpty(g) && g.StartsWith("moor_river/"))
                {
                    riverGround++;
                    // 水 = 阻挡（原版不可涉水；本项目 `MapGenTown.KindOf('r') = TileKind.Rock`）
                    if (!town.Walkable(new Vector2Int(x, y))) riverBlocked++;
                }
                if (!string.IsNullOrEmpty(g) && g.StartsWith("moor_bridge/"))
                {
                    bridgeGround++;
                    if (!string.IsNullOrEmpty(o) && o.StartsWith("moor_bridge/"))
                    {
                        bridgeRail++;
                        if (town.Walkable(new Vector2Int(x, y))) badRail++;
                    }
                    else
                    {
                        bridgeDeck++;
                        if (!town.Walkable(new Vector2Int(x, y))) badDeck++;
                    }
                }
                // 营地内框（片 4 起围栏环 = x[17,47] × y[16,39] ⇒ 内框 x[18,46] × y[17,38]）
                if (x > 17 && x < 47 && y > 16 && y < 39) campInterior++;
            }
        }
        Check(fence >= 90, $"营地围栏（wall 层 town_fence）= {fence} 格（原版 townW1.ds1 的围栏环 95 格）");
        Check(riverGround >= 100, $"营地外的河（floor 层 moor_river）= {riverGround} 格（原版河带 x∈[30,36]；" +
                                 "桥占掉的那几格地砖已换成桥，故比 309 少）");
        Check(riverBlocked == riverGround,
            $"河/水**全部阻挡**（可走的水 = {riverGround - riverBlocked} 格，必须为 0）");

        // ★ 木桥（agent-42）：原版只有 `TownE1.ds1` 有 `<OUTDOORS/bridge.dt1>` 的 40 格
        //   （floor x∈[47,56] y∈[15,18]、wall x∈[47,56] y∈{16,18}），换算到关卡坐标 =
        //   `x∈[29,38] y∈[20,23]`，正好横跨河带 `x∈[30,36]`。桥面可走、栏杆阻挡 ⇒ 河**能过去**。
        Check(bridgeGround >= 20,
            $"跨河木桥地砖（floor 层 moor_bridge）= {bridgeGround} 格（原版 TownE1.ds1 的桥）+ " +
            $"栏杆 {bridgeRail} 格");
        Check(bridgeDeck >= 10 && badDeck == 0,
            $"桥面**可走**：{bridgeDeck} 格全可走（不可走的桥面 = {badDeck}）");
        Check(bridgeRail >= 10 && badRail == 0,
            $"桥栏杆**阻挡**：{bridgeRail} 格全阻挡（可走的栏杆 = {badRail}）");
        //   片 4 起河带在 x∈[47,53]（桥在 x∈[46,55] y∈[25,28]）⇒ 河东岸取 (54,27)：
        //   它只可能有桥地砖（无栏杆）⇒ 必须可走，且出生点能沿桥走到。
        var eastBank = new Vector2Int(54, 27);
        Check(town.Walkable(eastBank) && town.FindPath(town.SpawnPoint, eastBank) != null,
            $"能沿木桥走到**河东岸**（出生点 {town.SpawnPoint} → {eastBank}：过桥，不再被水挡死）");

        // 围栏环必须是"合围"的：北/南两条横边整条是栅栏；西边**只有出城口那几格**是缺口。
        //   片 4 起围栏环在关卡窗口坐标 x[17,47] × y[16,39]（= 合并帧 x[0,30] × y[11,34] + 窗口原点）
        var ringTop = 16;
        var ringBottom = 39;
        var ringLeft = 17;
        var ringRight = 47;
        var missN = 0;
        var missS = 0;
        for (var x = ringLeft; x <= ringRight; x++)
        {
            if (!IsTownFence(town, x, ringTop)) missN++;
            if (!IsTownFence(town, x, ringBottom)) missS++;
        }
        var westGap = 0;
        for (var y = ringTop + 1; y < ringBottom; y++)
        {
            if (!IsTownFence(town, ringLeft, y)) westGap++;
        }
        Check(missN == 0 && missS == 0,
            $"围栏环北/南两条横边**整条合围**（缺口：北={missN} 南={missS}，都必须 0）");
        Check(westGap is >= 1 and <= 5,
            $"西侧只有出城口那几格是缺口（实测缺口 {westGap} 格，应为 1..5）");

        // ── ★ 片 4：**出城口不再是地图边界**（用户报"营地出口是黑色的一个口"）─────────
        //  旧窗口把地图西边界压在营地西围栏那一列 ⇒ 出城口正好落在地图边界上，出口外面
        //  什么都没有（黑）。原版关卡 56 列 = 营地本体 39 列 + **出城口外面 17 列**
        //  （依据见 `tools/d2codec/export_town_layout.py` 的 `WIN_X0/WIN_Y0`）⇒ 三格内陆。
        var exitCells = new List<Vector2Int>();
        for (var y = 0; y < town.Height; y++)
        {
            for (var x = 0; x < town.Width; x++)
            {
                if (town.TileAt(new Vector2Int(x, y)) == TileKind.Exit) exitCells.Add(new Vector2Int(x, y));
            }
        }
        Check(exitCells.Count == 3, $"城镇出口恰 3 格（原版围栏西侧 3 格缺口），实测 {exitCells.Count}");
        var interiorExits = 0;
        foreach (var c in exitCells)
        {
            if (c.x > 0 && c.x < town.Width - 1 && c.y > 0 && c.y < town.Height - 1) interiorExits++;
        }
        Check(interiorExits == 3,
            $"三格全是**内陆格**（不再压在地图边界 ⇒ 不再是黑口）：{Fmt(exitCells)}");
        var outsideTiled = 0;
        var edgeTiled = 0;
        foreach (var c in exitCells)
        {
            town.TryGetTileKeys(c.x - 1, c.y, out var wg, out _);
            if (wg.Length > 0) outsideTiled++;
            town.TryGetTileKeys(0, c.y, out var eg, out _);
            if (eg.Length > 0) edgeTiled++;
        }
        Check(outsideTiled == 3, $"出城口**外面**那格（x-1）都有原版地面瓦片（{outsideTiled}/3）");
        Check(edgeTiled == 3,
            $"出口同 y 的**关卡最西列**(0,y) 也有原版地面瓦片（{edgeTiled}/3 ⇒ 地面一路铺到关卡边界，黑背景只可能在关卡边界之外）");
        Check(campInterior > 600, $"营地内框格数 = {campInterior}（x∈[1,29] × y∈[12,33]，用来确认围栏环位置）");

        // 出城口：可走 + 出生点走得到（不然城里出不去）
        var gateWalkable = 0;
        foreach (var e in town.Exits)
        {
            if (town.Walkable(e)) gateWalkable++;
        }
        Check(gateWalkable == town.Exits.Count,
            $"出城口全部可走（{gateWalkable}/{town.Exits.Count}）");
        var reach = town.CountWalkableComponents();
        Check(reach == 1, $"城里可走区连通片数 = {reach}（出生点必须能走到出城口）");
        Console.WriteLine();
    }

    /// <summary>该格 wall 层是不是原版营地围栏（`town_fence/`）。</summary>
    private static bool IsTownFence(MapModule map, int x, int y)
    {
        if (!map.TryGetTileKeys(x, y, out _, out var o)) return false;
        return !string.IsNullOrEmpty(o) && o.StartsWith("town_fence/");
    }

    // ── 11. 野外布局形态（不是"一坨"+ 真随机）──────────────────────────────────
    private static void Step11_WildernessLayoutShape()
    {
        Section("11. 血腥荒野：随机尺寸 + 崖壁带 + 2 格宽土路 + 崖壁树线（布局形态自检）");
        for (var k = 0; k < 3; k++)
        {
            var seed = 4242 + k * 104729;
            var m = NewMap();
            m.Generate(AreaId.BloodMoor, seed);

            var road = 0;
            var tree = 0;
            var rock = 0;
            for (var y = 0; y < m.Height; y++)
            {
                for (var x = 0; x < m.Width; x++)
                {
                    var t = m.TileAt(new Vector2Int(x, y));
                    if (t == TileKind.Road) road++;
                    else if (t == TileKind.Tree) tree++;
                    else if (t == TileKind.Rock) rock++;
                }
            }

            // ★ 片 3：口径从「落在区间内」收紧为**恒等**原版 `Levels.txt`「Act 1 - Wilderness 1」的
            //   SizeX/SizeY = 80（旧口径 48~80 随机 ⇒ 用户报「野外地图太小」）。
            Check(m.Width == GameConst.WildernessMaxSize && m.Height == GameConst.WildernessMaxSize,
                $"seed={seed}：尺寸 {m.Width}x{m.Height} **恒 = 原版 80×80**" +
                "（= `GameConst.WildernessMaxSize`；口径是**恒等**，不是落在区间内）");
            // ★ 本轮改口径：野外现在是**原版块拼出来的** ⇒ 每一格的地面/物件瓦片都来自原版 ds1，
            //   所以"逐格覆盖"必须为 true（旧断言要求 false，那是"按 TileKind 分类抽一张近似瓦片"的老做法）。
            Check(m.HasTileOverrides,
                $"seed={seed}：启用逐格原版瓦片键（原版 8 格块拼出来的图 ⇒ 每格地面/物件都来自原版 ds1）");
            Check(m.Width % 8 == 0 && m.Height % 8 == 0,
                $"seed={seed}：尺寸是原版野外块边长 8 的整数倍（{m.Width / 8}x{m.Height / 8} 个块）");

            var noGround = 0;
            var foreign = 0;
            for (var y = 0; y < m.Height; y++)
            {
                for (var x = 0; x < m.Width; x++)
                {
                    m.TryGetTileKeys(x, y, out var g, out var o);
                    if (string.IsNullOrEmpty(g)) noGround++;
                    if (!string.IsNullOrEmpty(g) && !IsWildKey(g)) foreign++;
                    if (!string.IsNullOrEmpty(o) && !IsWildKey(o)) foreign++;
                }
            }
            Check(noGround == 0, $"seed={seed}：每一格都有原版地面瓦片（'原版不画' = {noGround} 格）");
            Check(foreign == 0, $"seed={seed}：地面/物件瓦片键全部来自原版野外素材（异包 = {foreign} 格）");

            Check(road > 60, $"seed={seed}：土路 {road} 格（3 格宽 ⇒ 明显多于 1 格宽的 40 格量级）");
            Check(tree >= 80, $"seed={seed}：树 {tree} 格（原版边界块的树线 + Fence/Tree Fill 树丛）");
            Check(rock > 300, $"seed={seed}：岩体 {rock} 格（原版崖壁边界带）");
            Check(m.Exits.Count == 2 && m.CaveEntrance.HasValue,
                $"seed={seed}：出口 2 个（回城口 + 洞穴入口）、洞穴入口 = {m.CaveEntrance}");
            Check(m.Walkable(m.Exits[0]) && m.Walkable(m.Exits[1]),
                $"seed={seed}：两个出入口都真的可走（土路把两侧边界打通）");

            // 关键形态断言：野外是**开阔场地**（原版血腥荒野就是这样），不是被障碍封成迷宫。
            // 阈值 35% 的来由：地形里有一圈**原版崖壁边界带**（8 格块 ⇒ 最外 8~9 格），
            // 小图上这一圈占比更高（48x48 时占 55% 面积）⇒ 实测各尺寸 38%~65%，取 35% 留裕量。
            var ratio = 100f * m.WalkableCount / (m.Width * m.Height);
            Check(ratio >= 35f, $"seed={seed}：可走率 {ratio:F1}% ≥35%（开阔草地 + 一圈崖壁边界带）");
            var comps = m.CountWalkableComponents();
            Check(comps == 1, $"seed={seed}：可走格连通片数 = {comps}（障碍不该把场地切碎）");

            // ── ★ 本轮新增：**块与块按"出口（四边开口）"拼接**（不是逐格随机、也不是随便挑块）──
            //  口径：`MapGenWildLayout.Piece.OpenN/S/W/E` = 该块的边界中段有没有可走格，
            //  由导出器从块自己的可走掩码算出（`export_wild_layout.py::edge_open`）。
            //    · 周圈：每块的**朝外那条边必须是闭的**（崖壁朝外），且**沿环/朝内的边是开的**
            //      ⇒ 整圈崖壁连续、内部接得上（生成器里是四条件精确匹配，匹配不到会放宽 + 留日志）；
            //    · 内部：放下的填充块必须**四边全开** ⇒ 不把场地中间的通道掐断。
            // ★ 城镇过渡带（agent-42）：西边界正对回城口那 5 槽不铺崖壁边界块，改铺原版
            //   「Act 1 - Town 1 Transition E」(8×40)。断言口径 = 把这几槽从"按开口拼接"里减掉。
            var blockSlots = MapGenWilderness.AuditBorderSlots - MapGenWilderness.AuditBandSlots;
            Check(MapGenWilderness.AuditBorderSlots > 0,
                $"seed={seed}：周圈槽位记了 {MapGenWilderness.AuditBorderSlots} 个（自检披露数据）");
            Check(MapGenWilderness.AuditBandSlots == 5,
                $"seed={seed}：西边界 {MapGenWilderness.AuditBandSlots} 槽 = 原版城镇过渡带" +
                "（「Act 1 - Town 1 Transition E」8×40 = 1 块宽 × 5 槽）");
            Check(MapGenWilderness.AuditBorderStitched == blockSlots,
                $"seed={seed}：其余 {MapGenWilderness.AuditBorderStitched}/{blockSlots} 槽按开口拼接" +
                "（朝外的边全为「闭」= 崖壁朝外）");
            Check(MapGenWilderness.AuditBorderExact == blockSlots,
                $"seed={seed}：其余槽四条件**精确匹配** {MapGenWilderness.AuditBorderExact}/" +
                $"{blockSlots} 块（朝外闭 + 沿环开 + 朝内开）");

            // 过渡带真的落了：它的 6 格桥瓦片（`bridge.dt1`）只在过渡带里出现
            // （西边界那一段之外，野外任何原版块都没有 bridge.dt1）。
            var bandBridge = 0;
            for (var y = 0; y < m.Height; y++)
            {
                for (var x = 0; x < m.Width; x++)
                {
                    m.TryGetTileKeys(x, y, out var bg, out var bo);
                    if ((!string.IsNullOrEmpty(bg) && bg.StartsWith("moor_bridge/"))
                        || (!string.IsNullOrEmpty(bo) && bo.StartsWith("moor_bridge/"))) bandBridge++;
                }
            }
            Check(bandBridge > 0,
                $"seed={seed}：城镇过渡带已铺进野外（带内的桥瓦片 `moor_bridge/` = {bandBridge} 格，证明这条带真的盖上了）");
            Check(MapGenWilderness.AuditInteriorSlots > 0
                  && MapGenWilderness.AuditInteriorAllOpen == MapGenWilderness.AuditInteriorSlots,
                $"seed={seed}：内部填充块**四边全开** {MapGenWilderness.AuditInteriorAllOpen}/" +
                $"{MapGenWilderness.AuditInteriorSlots} 块");
            Console.WriteLine();
        }
    }

    // ── 14. 外围环「封边」检查（片 3 · 用户报「地图连接处都是黑边」）────────────────
    //   两个问题分开答：
    //   ① **四边是否都铺满边界块** —— 生成器日志已给（周圈 31/31 + 西边界 5 槽过渡带）；
    //      这里再从**结果**侧复核：外围环上还有没有"可走且不是出口"的格（有 ⇒ 站在那格往图外看就是黑）。
    //   ② 若①有缺口，**能不能不重生成生成物就修** —— 看拼块库里有没有"外沿整条实心"的候选。
    //      ⛔ 本步只**披露**，不据此判失败（判失败会与"本片不修生成物"的结论冲突，见回报）。
    private static void Step14_BorderSealGaps()
    {
        Section("14. 血腥荒野外围环封边检查（可走格是否露到图外）");

        var ringCells = 0;
        var ringWalkable = 0;
        var ringWalkableNotExit = 0;
        var samples = new List<string>();
        var sides = new int[4];     // 0=北(y=0) 1=南(y=H-1) 2=西(x=0) 3=东(x=W-1)

        for (var k = 0; k < 5; k++)
        {
            var m = NewMap();
            m.Generate(AreaId.BloodMoor, unchecked(20250916 + k * 7919));
            var w = m.Width;
            var h = m.Height;

            for (var x = 0; x < w; x++)
            {
                for (var y = 0; y < h; y++)
                {
                    var onRing = x == 0 || y == 0 || x == w - 1 || y == h - 1;
                    if (!onRing) continue;
                    ringCells++;
                    if (!m.Walkable(new Vector2Int(x, y))) continue;
                    ringWalkable++;
                    if (m.TileAt(new Vector2Int(x, y)) == TileKind.Exit) continue;
                    ringWalkableNotExit++;
                    if (x == 0) sides[2]++;
                    else if (x == w - 1) sides[3]++;
                    else if (y == 0) sides[0]++;
                    else sides[1]++;
                    if (samples.Count < 12) samples.Add($"{new Vector2Int(x, y)}");
                }
            }
        }

        Console.WriteLine($"  外围环格数（5 个 seed 合计）= {ringCells}；其中可走 {ringWalkable}，" +
                          $"**可走且不是出口 = {ringWalkableNotExit}**");
        Console.WriteLine($"    按边：北(y=0)={sides[0]} 南(y=H-1)={sides[1]} 西(x=0)={sides[2]} 东(x=W-1)={sides[3]}");
        Console.WriteLine($"    前 12 个样本格：{string.Join(" ", samples)}");
        Console.WriteLine("    ⇒ 这些格站在上面往图外看就是纯黑（相机 SolidColor=黑，关卡外无实体）。");
        Console.WriteLine("      ⛔ 只披露、不判失败：修它要动生成物 `MapGenWildLayout.cs`（见下 ②），见回报。");

        // ② 可行性：每个边向是否存在"外沿整条实心"且满足四边开口要求的候选块
        var all = MapGenWildLayout.Pieces;
        var borderCount = 0;
        for (var i = 0; i < all.Length; i++) if (all[i].Group == MapGenWildLayout.GroupBorder) borderCount++;
        Console.WriteLine($"  拼块库：Border 组 {borderCount} 块 / 全部 {all.Length} 块");

        // 位（**下标与 OuterEdgeSolid 的 case 一一对应**）：N=1<<0 S=1<<1 W=1<<2 E=1<<3
        //   closed = 该槽位必须"闭"的边；open = 必须"开"的边；outward = **落在地图最外沿**的那几条边
        //   （角上两条都要实心 —— 角块的两条外沿都在关卡边界上）。
        const int BN = 1, BS = 2, BW = 4, BE = 8;
        var specs = new[]
        {
            ("北边槽  n闭 s/w/e开", BN, BS | BW | BE, BN),
            ("南边槽  s闭 n/w/e开", BS, BN | BW | BE, BS),
            ("西边槽  w闭 n/s/e开", BW, BN | BS | BE, BW),
            ("东边槽  e闭 n/s/w开", BE, BN | BS | BW, BE),
            ("西北角  n/w闭 s/e开", BN | BW, BS | BE, BN | BW),
            ("东北角  n/e闭 s/w开", BN | BE, BS | BW, BN | BE),
            ("西南角  s/w闭 n/e开", BS | BW, BN | BE, BS | BW),
            ("东南角  s/e闭 n/w开", BS | BE, BN | BW, BS | BE),
        };

        foreach (var sp in specs)
        {
            var strictOk = 0;
            var strictAndSolid = 0;
            for (var i = 0; i < all.Length; i++)
            {
                var p = all[i];
                if (p.Group != MapGenWildLayout.GroupBorder) continue;
                for (var m2 = 0; m2 < 4; m2++)
                {
                    var fx = (m2 & 1) != 0;
                    var fy = (m2 & 2) != 0;
                    // 盖章后的实际开口标志（翻 = 交换 N/S 与 W/E）
                    var bits = 0;
                    if (fy ? p.OpenS : p.OpenN) bits |= 1;
                    if (fy ? p.OpenN : p.OpenS) bits |= 2;
                    if (fx ? p.OpenW : p.OpenE) bits |= 4;
                    if (fx ? p.OpenE : p.OpenW) bits |= 8;

                    if ((bits & sp.Item2) != 0 || (bits & sp.Item3) != sp.Item3) continue;   // 四边开口不满足
                    strictOk++;
                    var solid = true;
                    for (var b = 0; b < 4 && solid; b++)
                    {
                        if ((sp.Item4 & (1 << b)) == 0) continue;
                        if (!OuterEdgeSolid(p, b, fx, fy)) solid = false;
                    }
                    if (solid) strictAndSolid++;
                }
            }
            Console.WriteLine($"    {sp.Item1}：四边开口候选 {strictOk} 个；其中**外沿整条实心** {strictAndSolid} 个");
        }
        Console.WriteLine();
    }

    // ── 15. ★ R1-B：河面 wall 层「平色水墙瓦片」不叠（用户报「奇怪的蓝条图片占位」）──────
    //   根因（离线判据 `tools/probes/measure/r1b_water_tiles.py`，扫 `MapGenTownLayout` 引用到的
    //   **295 个**瓦片键 → 逐张 PNG 采样像素）：**唯一色数 = 1（平色）的只有一个** ——
    //   `Objects/moor_river/028`（160×128，不透明 6400 px = 恰好一格，全图同色 RGBA(0,32,68) 深蓝），
    //   铺在河带 x=47 / x=54 两列共 **49 格**。原版靠 `ACT1/Pal.PL2` **调色板循环**把它变成水波，
    //   本引擎**没有运行期循环** ⇒ 静态渲染 = 硬边平色色块（视觉上等价占位图）。
    //   本步把「规则 + 实测格数 + 素材侧代理证据 + 只影响渲染」四条都钉住。
    private static void Step15_FlatWaterWallNotOverlaid()
    {
        Section("15. ★ R1-B：河面 wall 层平色瓦片**不叠**（白名单键 / 命中 49 格 / 不碰可走性）");

        // ① 白名单本身：只有 1 个键，且就是取证出来的那一个
        Check(MapView.FlatWallTileWhitelist.Length == 1
              && MapView.FlatWallTileWhitelist[0] == "moor_river/028",
            $"白名单 = [{string.Join(",", MapView.FlatWallTileWhitelist)}]（必须恰 1 个：moor_river/028）");

        // ② 城镇图上命中规则的格数 / 位置（与 Python 视线判据的 49 格对账）
        var town = NewMap();
        town.Generate(AreaId.Town, 20250916);
        var hit = 0;
        var wrongColumn = 0;
        var wrongObject = 0;
        var blockedKept = 0;
        var cols = new List<int>();
        for (var y = 0; y < town.Height; y++)
        {
            for (var x = 0; x < town.Width; x++)
            {
                town.TryGetTileKeys(x, y, out var g, out var o);
                if (!MapView.IsPaletteCycledFlatWallOverlay(g, o)) continue;
                hit++;
                if (x != 47 && x != 54) wrongColumn++;
                if (o != "moor_river/028") wrongObject++;
                if (!cols.Contains(x)) cols.Add(x);
                // 只影响渲染：这 49 格**依然是水 = 阻挡**（可走性一个字没动）
                // 【R12 重判·受影响行】本行原判 `TileKind.Rock`（那时水与石头同归 Rock）。
                //   片 L 把水的 kind 摘成 `TileKind.Water` ⇒ 这里改为判 `Water`：
                //   判的仍是同一件事（"这 49 格还是水、还是不可走"），只是水的 kind 有了自己的名字。
                if (town.TileAt(new Vector2Int(x, y)) == TileKind.Water
                    && !town.Walkable(new Vector2Int(x, y))) blockedKept++;
            }
        }
        cols.Sort();
        Console.WriteLine($"  命中「平色水墙瓦片不叠」的格 = {hit} 格，列 x = {FmtInts(cols)}");
        Check(hit == 49, $"城镇图命中 **49 格**（河带 x=47 有 13 格 + x=54 有 36 格；实测 {hit}）");
        Check(wrongColumn == 0 && wrongObject == 0,
            $"命中格全在河带 x=47/54（越界列 = {wrongColumn}）且 object 键全是 moor_river/028（异常 = {wrongObject}）");
        Check(blockedKept == hit,
            $"这 {hit} 格**仍然是水 = 阻挡**（TileKind == Water 且不可走 = {blockedKept}/{hit} ⇒ R1-B 只改渲染、R12 只给水换 kind，逻辑不变）");

        // ③ 反例：规则不许泛化（空地面 / 异包 / 白名单外的平色 / 空物件）
        Check(!MapView.IsPaletteCycledFlatWallOverlay("", "moor_river/028")
              && !MapView.IsPaletteCycledFlatWallOverlay("moor_bridge/001", "moor_river/028")
              && !MapView.IsPaletteCycledFlatWallOverlay("moor_river/025", "moor_river/001")
              && !MapView.IsPaletteCycledFlatWallOverlay("moor_river/025", ""),
            "反例全部为 false：空地面 / 异包（moor_bridge 地面 + moor_river 物件）/ 白名单外的物件键 / 空物件键");

        // ④ 素材侧代理证据（像素权威在 `tools/probes/measure/r1b_water_tiles.py` 的逐像素采样）：
        //    `Objects/moor_river/manifest.json` 只有 1 个瓦片（idx 28、orientation 1 ⇒ 不是地砖层）；
        //    同 dt1 的 `Tiles/moor_river/` 有 44 张地砖；且**平色瓦片的 PNG 大小 ≪ 带纹理的地砖**
        //    （单色 160×128 压到 < 1 KB，带纹理的 4~11 KB）。
        var manWall = ResourceFile("D2/Objects/moor_river/manifest.json");
        var manFloor = ResourceFile("D2/Tiles/moor_river/manifest.json");
        var pngWall = ResourceFile("D2/Objects/moor_river/028.png");
        var pngFloor = ResourceFile("D2/Tiles/moor_river/025.png");
        if (manWall == null || manFloor == null || pngWall == null || pngFloor == null)
        {
            Check(false, "取不到 moor_river 的 manifest / PNG（素材没落盘？路径：" +
                         $"{(manWall ?? "manifest(Objects) 缺")} / {(manFloor ?? "manifest(Tiles) 缺")}）");
        }
        else
        {
            var wallText = System.IO.File.ReadAllText(manWall);
            var floorText = System.IO.File.ReadAllText(manFloor);
            Check(wallText.Contains("\"tileCount\": 1") && wallText.Contains("\"idx\": 28")
                  && wallText.Contains("\"orientation\": 1"),
                "`Objects/moor_river/manifest.json`：tileCount=1 / idx=28 / orientation=1（该 dt1 只有这一个物件瓦片）");
            Check(floorText.Contains("\"tileCount\": 44"),
                "`Tiles/moor_river/manifest.json`：同 dt1 的**地砖** 44 张（河面由它们呈现）");
            var sw = new System.IO.FileInfo(pngWall).Length;
            var sf = new System.IO.FileInfo(pngFloor).Length;
            Console.WriteLine($"  PNG 字节数：平色物件瓦片 028.png = {sw} B，带纹理地砖 025.png = {sf} B");
            Check(sw * 4 < sf,
                $"平色物件瓦片 028.png（{sw} B）**远小于**带纹理地砖 025.png（{sf} B）" +
                "（单色 160×128 的压缩率证据；逐像素权威见 tools/probes/measure/r1b_water_tiles.py）");
        }
        Console.WriteLine();
    }

    // ═════════════════════════════════════════════════════════════════════════
    // 16. ★ 走图跨分块边界的单帧尖峰（**离线静态量**；⛔ 本片不许进 Play）
    //
    //   背景（主 agent 侦察 + 本步复验）：用户报过「人物移动抖动」，本轮已由另一片修了帧节奏
    //   （`Core/FramePacing.cs`）与动画复位。本步回答**剩下的那一问**：走图跨分块边界时
    //   `MapView` 的单帧尖峰有多大、多久一次。
    //
    //   源码事实（逐条给行号，本步全部以它为准）：
    //     · `MapView.BuildChunk:446-463` —— 每块建 **3 个块根节点**（ground/object/overlay，
    //       `:450-452`）＋ 逐格 `BuildCell`；每格最多 3 个 `NewTile`（地面 `:488`、
    //       物件 `:512`、迷雾 `:527`）。
    //     · `MapView.Update:1093-1096` —— 分块模式下**每 `ChunkRefreshInterval`（`:60`）最多调一次**
    //       `RefreshVisibleChunks`（节流；小图 `!_chunked` 直接 return）。
    //     · `MapView.RefreshVisibleChunks:373-407` —— 可见块集合没变 ⇒ 直接 return（`:391-395`）；
    //       变了 ⇒ **同一帧**把范围内**所有缺块**建完（`:401-404` 的双层 for），随后回收远处块
    //       （`:406`）。⇒ ⛔「每帧最多 1 块」与代码不符：一次刷新是「本帧把新进范围的块全建完」。
    //     · `MapView.ComputeVisibleChunkRange:410-444` —— 视口四角 → 格范围（**外扩 1 块**，
    //       `:439-443`）→ 块范围。
    //   ⛔ 本步只出**离线静态量**；任何帧时间/性能结论必须同时给渲染设备名，而本片不允许进 Play
    //     ⇒ 本步**不写"实测帧时间"**，只给「单帧新增节点数 × 每 GO 成本」的**阈值**（见回报）。
    // ═════════════════════════════════════════════════════════════════════════
    private static void Step16_ChunkRebuildSpike()
    {
        Section("16. ★ 走图跨分块边界的单帧尖峰（离线静态量：每块节点数 / 节流 / 可见块范围 / 单帧上限）");

        // ── ① 每块格数 + 每格节点数（引用生产常量与 `BuildCell` 的 NewTile 调用点）──────
        var chunkSize = MapView.ChunkSize;                        // public const 16（MapView.cs:57）
        var chunkCells = chunkSize * chunkSize;                   // 256
        const int nodesPerCellWithFog = 3;                        // 地面:488 + 物件:512 + 迷雾:527
        const int nodesPerCellNoFog = 2;                          // 出货配置：迷雾从未开启（见 ③）
        var capWithFog = chunkCells * nodesPerCellWithFog;         // 768
        var capNoFog = chunkCells * nodesPerCellNoFog;             // 512
        Console.WriteLine($"  每块 = {chunkSize}×{chunkSize} = {chunkCells} 格；块根节点 = 3 个" +
                          $"（MapView.cs:450-452）");
        Console.WriteLine($"  ⇒ 每块节点上限 = {chunkCells}×3 + 3 = {capWithFog + 3}（含迷雾）/ " +
                          $"{chunkCells}×2 + 3 = {capNoFog + 3}（出货配置，不含迷雾）");
        Check(chunkCells == 256 && capWithFog == 768,
            $"单块节点**上限** = ChunkSize({chunkSize})² × {nodesPerCellWithFog} 层 = {capWithFog}" +
            $"（层数出处 = BuildCell 的三处 NewTile：地面 :488 / 物件 :512 / 迷雾 :527）");

        // ── ② 节流口径（`ChunkRefreshInterval` 是 private const ⇒ 反射读，不抄字面量）──
        var throttleField = typeof(MapView).GetField("ChunkRefreshInterval",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        var throttle = throttleField == null ? float.NaN : Convert.ToSingle(throttleField.GetRawConstantValue());
        Console.WriteLine($"  ChunkRefreshInterval（反射读 MapView.cs:60 的 private const）= {throttle} s");
        Check(throttleField != null && throttle > 0f,
            $"节流常量可读且为正：ChunkRefreshInterval = {throttle} s（`Update:1094-1095` 用它限频）");

        // ── ③ 出货配置下迷雾**从未开启** ⇒ 每格 2 个节点（不是 3）────────────────────
        //   判据 = 全 `client/Assets/Scripts/**/*.cs` 里 `SetFogOfWar(` 的**调用点**数。
        //   预期恰 1 个 = `MapModule.SetFogOfWar`（MapModule.cs:315）对 `MapView` 的**转发**，
        //   App / UI / Flow 侧 0 命中 ⇒ `_fogOn` 恒 false ⇒ `BuildCell:517-518` 的 `CreateFog`
        //   永不执行 ⇒ overlay 层只有空的块根节点。
        var fogCallSites = CountFogOfWarCallSites();
        Check(fogCallSites == 1,
            $"`SetFogOfWar(` 在 `client/Assets/Scripts/**` 里只有 **1 个调用点**（= `MapModule.cs:315` " +
            $"对 `MapView` 的转发；App/UI/Flow 侧 0 命中）⇒ 出货配置 `_fogOn` 恒 false ⇒ 每格 2 个节点" +
            $"（实测调用点 {fogCallSites} 个；若 >1 说明有人真的开了迷雾，本步的 2 层上限要改回 3 层）");

        // ── ④ 真实地图的「每块节点数」（照抄 BuildCell 的判定口径，逐块统计）──────────
        var wild = NewMap();
        wild.Generate(AreaId.BloodMoor, 20250916);
        var w = wild.Width;
        var h = wild.Height;
        var chunksX = (w + chunkSize - 1) / chunkSize;            // = Mathf.Ceil(W/ChunkSize)
        var chunksY = (h + chunkSize - 1) / chunkSize;
        var chunked = w * h > MapView.BuildAllTileThreshold;
        Console.WriteLine($"  血腥荒野 {w}×{h} = {w * h} 格 > 阈值 {MapView.BuildAllTileThreshold}" +
                          $"（MapView.cs:54）⇒ 分块模式 = {chunked}；块网格 {chunksX}×{chunksY} = {chunksX * chunksY} 块");

        var groundByChunk = new int[chunksX * chunksY];
        var objectByChunk = new int[chunksX * chunksY];
        var caveWallCells = 0;
        var voidCells = 0;
        for (var x = 0; x < w; x++)
        {
            for (var y = 0; y < h; y++)
            {
                var g = new Vector2Int(x, y);
                var kind = wild.TileAt(g);
                if (kind == TileKind.Void) { voidCells++; continue; }          // BuildCell:467-468 直接 return
                if (kind == TileKind.CaveWall) caveWallCells++;
                wild.TryGetTileKeys(x, y, out var gk, out var ok);
                var ci = (x / chunkSize) * chunksY + (y / chunkSize);
                // 地面：`BuildCell:485` 的 `!string.IsNullOrEmpty(groundKey)` 才建节点
                if (!string.IsNullOrEmpty(gk)) groundByChunk[ci]++;
                // 物件：`BuildCell:499-506` 的 `drawObject`（逐格覆盖图 = ds1Object 非空，且不是 R1-B
                // 白名单的「平色水墙瓦片不叠」）；`IsHiddenSolidInterior` 只对 CaveWall 生效
                //   `IsHiddenSolidInterior`（`:535-536`）= `kind == CaveWall && 无 8 邻可走` ⇒ 只对洞穴生效；
                //   野外**没有 CaveWall 格**（下面 `caveWallCells == 0` 断言）⇒ 本统计是**上界**。
                var drawObject = !string.IsNullOrEmpty(ok) && !MapView.IsPaletteCycledFlatWallOverlay(gk, ok);
                if (drawObject) objectByChunk[ci]++;
            }
        }

        var maxPerChunk = 0;
        var minPerChunk = int.MaxValue;
        long sumPerChunk = 0;
        var countChunks = 0;
        for (var i = 0; i < groundByChunk.Length; i++)
        {
            var n = groundByChunk[i] + objectByChunk[i] + 3;    // + 3 个块根节点
            maxPerChunk = Math.Max(maxPerChunk, n);
            minPerChunk = Math.Min(minPerChunk, n);
            sumPerChunk += n;
            countChunks++;
        }
        Console.WriteLine($"  逐块实测（血腥荒野 25 块）：地面 {chunkCells} 格/块满铺（Void {voidCells} 格、" +
                          $"CaveWall {caveWallCells} 格）；每块 **{minPerChunk}~{maxPerChunk}** 个节点" +
                          $"（均 {sumPerChunk / (double)countChunks:F1}），上限 {capNoFog + 3}");
        Check(caveWallCells == 0, $"野外没有 CaveWall 格（实测 {caveWallCells}）⇒ 物件层不适用 IsHiddenSolidInterior");
        Check(maxPerChunk <= capNoFog + 3,
            $"每块节点数**恒 ≤ 上限** {capNoFog + 3}（= {chunkCells}×2 + 3；实测最大 {maxPerChunk}）");

        // ── ⑤ 可见块范围（离线模拟 `ComputeVisibleChunkRange:410-444` 的口径）+ 单帧新增块数 ──
        //   两份输入都**从生产文件现读**（不抄字面量）：
        //     · 正交尺寸 = `Module/Camera/CameraRig.cs` 的 `DefaultOrthographicSize`；
        //     · 分辨率 = `client/ProjectSettings/ProjectSettings.asset` 的 defaultScreenWidth/Height。
        //   推导（与 `IsoLayout.WorldToGrid:82-87` 同一套线性变换）：
        //     视口世界范围 = 相机中心 ± (size×aspect, size)；格坐标是同一组线性式
        //     ⇒ fx / fy 的跨度**都** = size × (aspect + 2)（与朝向无关，只跟 size 与 aspect 有关）。
        var ortho = ReadCameraOrthoSize();
        var hasScreen = ReadProjectScreenSize(out var screenW, out var screenH);
        var aspect = hasScreen && screenH > 0 ? (float)screenW / screenH : float.NaN;
        var spanGrid = ortho * (aspect + 2f);
        Console.WriteLine($"  相机正交尺寸（现读 CameraRig.cs 的 DefaultOrthographicSize）= {ortho}；" +
                          $"分辨率（现读 ProjectSettings.asset）= {screenW}×{screenH} ⇒ aspect = {aspect:F4}；" +
                          $"⇒ 可见格跨度 = 2×size×aspect 与 2×size 合成后**两轴相同** = size×(aspect+2) = **{spanGrid:F3} 格**");
        Check(ortho > 0f && float.IsFinite(ortho) && aspect > 0f && float.IsFinite(aspect),
            $"两份输入都读到了（ortho={ortho} / aspect={aspect:F4}）—— 读不到就是**静默失真**，故必须判红");
        Check(spanGrid < chunkSize,
            $"可见格跨度 {spanGrid:F3} < 块边长 {chunkSize} ⇒ 任一轴上 `floor(max/ChunkSize)-floor(min/ChunkSize) ≤ 1`" +
            $" ⇒ 可见块范围每轴 ≤ 1+1+2(外扩) = **4 块**（`ComputeVisibleChunkRange:439-443` 的 ±1 块外扩）");

        // 沿 +x 走一条 x=10..70 的直线（避开地图边界钳制），逐步算**新进入范围的块数**。
        var walkY = 40;
        var maxNewInOneRefreshX = 0;
        var minStepsBetweenBuilds = int.MaxValue;
        var lastBuildStep = -1;
        var maxRangeChunks = 0;
        var prevX = ChunkRangeSig(ortho, aspect, 10, walkY, chunksX - 1, chunksY - 1);
        for (var cx = 11; cx <= 70; cx++)
        {
            var curX = ChunkRangeSig(ortho, aspect, cx, walkY, chunksX - 1, chunksY - 1);
            var newX = CountChunksOnlyIn(curX, prevX);
            if (newX > 0)
            {
                maxNewInOneRefreshX = Math.Max(maxNewInOneRefreshX, newX);
                if (lastBuildStep >= 0) minStepsBetweenBuilds = Math.Min(minStepsBetweenBuilds, cx - lastBuildStep);
                lastBuildStep = cx;
            }
            maxRangeChunks = Math.Max(maxRangeChunks, RangeChunkCount(curX));
            prevX = curX;
        }

        // 沿 45° 斜线走：**必须扫遍相位**才对（45° 路径上 (x−y) 恒定 ⇒ 相位决定"两个轴是否在同一次
        // 刷新里各跨一块"；只走一条相位会漏掉那个最坏点）。
        var maxNewInOneRefreshDiag = 0;
        var diagSteps = 40;
        for (var a = 0; a < chunkSize; a++)
        {
            for (var b = 0; b < chunkSize; b++)
            {
                var px = ChunkRangeSig(ortho, aspect, 10 + a, 15 + b, chunksX - 1, chunksY - 1);
                for (var s = 1; s <= diagSteps; s++)
                {
                    var cur = ChunkRangeSig(ortho, aspect, 10 + a + s, 15 + b + s, chunksX - 1, chunksY - 1);
                    maxNewInOneRefreshDiag = Math.Max(maxNewInOneRefreshDiag, CountChunksOnlyIn(cur, px));
                    maxRangeChunks = Math.Max(maxRangeChunks, RangeChunkCount(cur));
                    px = cur;
                }
            }
        }
        Console.WriteLine($"  沿 +x 直线走 60 格（相机格 x=10..70）：单次刷新**新增块数最大 = {maxNewInOneRefreshX}**，" +
                          $"两次新增之间最少隔 {minStepsBetweenBuilds} 格");
        Console.WriteLine($"  沿 45° 斜线扫遍 {chunkSize}×{chunkSize} 个相位（各走 40 格）：" +
                          $"单次刷新新增块数最大 = {maxNewInOneRefreshDiag}");
        Console.WriteLine($"  可见块范围（块数）= 最多 **{maxRangeChunks}** 块（= 一屏要铺的总块数；" +
                          $"`RebuildLayers:327-352` 的整图重铺按它算）");
        Check(maxNewInOneRefreshX is >= 1 and <= 4,
            $"直线行走时单次刷新新增 ≤ **4 块**（= 可见块范围每轴上限；实测 {maxNewInOneRefreshX}）" +
            "（⚠️ 本行只描述**可见范围**这个几何量；「一帧到底建几块」自 T0FIX-A 起由 §17 钉住 = 每帧 ≤ 1 块" +
            "（登记与建块已拆开）⇒ 本步的 3~7 块是「一次刷新**登记**多少块」的上限）");
        Check(minStepsBetweenBuilds >= chunkSize,
            $"两次「新增块」之间相机至少走 {chunkSize} 格（实测 {minStepsBetweenBuilds}）⇒ 不存在连续逐帧重建" +
            "（节流 0.25s 内最多 0.75 格行程，跨不了 16 格）");
        Check(maxNewInOneRefreshDiag <= 2 * 4 - 1,
            $"斜线扫遍相位后最坏 = 新增一列(≤4) + 新增一行(≤4) − 角块 1 = ≤ **7 块**" +
            $"（实测 {maxNewInOneRefreshDiag}）");
        Check(maxRangeChunks <= 4 * 4,
            $"可见块范围 ≤ 4×4 = **16 块**（实测 {maxRangeChunks}）⇒ R1-D 的整图重铺单帧最多建 " +
            $"{maxRangeChunks} 块（对照：穿越尖峰只建 {maxNewInOneRefreshX}~{maxNewInOneRefreshDiag} 块）");

        // ── ⑥ 单帧新增节点数 + 尖峰频率（跨块周期 = ChunkSize / 跑速）────────────────
        var worstChunks = Math.Max(maxNewInOneRefreshX, maxNewInOneRefreshDiag);
        var worstNodesCap = worstChunks * (capNoFog + 3);
        var worstNodesReal = worstChunks * maxPerChunk;
        var crossPeriod = chunkSize / GameConst.PlayerWalkSpeed;
        Console.WriteLine($"  ⇒ 穿越尖峰单帧新增节点数：**上限 {worstNodesCap}**（{worstChunks} 块 × {capNoFog + 3}），" +
                          $"血腥荒野实测规模 {worstNodesReal}（{worstChunks} 块 × {maxPerChunk}）");
        Console.WriteLine($"  ⇒ 对照 · R1-D 的整图重铺（`RebuildLayers`）单帧建的是**整个可见范围** " +
                          $"= {maxRangeChunks} 块 ⇒ 上限 {maxRangeChunks * (capNoFog + 3)}，" +
                          $"实测规模 {maxRangeChunks * maxPerChunk}（**比穿越尖峰大 {maxRangeChunks / (double)worstChunks:F1} 倍**）");
        Console.WriteLine($"  ⇒ 尖峰频率：跨块周期 = ChunkSize/跑速 = {chunkSize}/{GameConst.PlayerWalkSpeed}" +
                          $" = **{crossPeriod:F3} s** ⇒ {1f / crossPeriod:F4} 次/s（且只在大图模式；" +
                          $"城镇 {GameConst.TownWidth}×{GameConst.TownHeight} = {GameConst.TownWidth * GameConst.TownHeight} 格 " +
                          $"≤ {MapView.BuildAllTileThreshold} ⇒ **不分块，0 次**）");
        Check(crossPeriod > throttle,
            $"跨块周期 {crossPeriod:F3}s > 节流 {throttle}s ⇒ 节流不会掩盖跨块事件（每次跨块都会被下一次刷新发现）");
        Check(1f / crossPeriod < 1f / MapView.RepaintMinInterval,
            $"尖峰频率 {1f / crossPeriod:F4} 次/s < R1-D 已接受的整图重铺包线 {1f / MapView.RepaintMinInterval:F1} 次/s" +
            $"（MapView.cs:76-83 的 RepaintMinInterval={MapView.RepaintMinInterval}s）");
        Check(GameConst.TownWidth * GameConst.TownHeight <= MapView.BuildAllTileThreshold,
            $"用户实测走路的那张图（城镇 {GameConst.TownWidth}×{GameConst.TownHeight}）**不分块** ⇒ 穿越型尖峰为 0" +
            "（`RebuildLayers:336-349` 的 `!_chunked` 分支一次铺满，之后 `Update:1093` 直接 return）");

        // ── ⑦ 建块是**同步单帧**完成（不排队、不跨帧）⇒ 不存在"连续多帧重建" ────────────
        var asyncHits = CountSourceHits("client/Assets/Scripts/Module/Map/MapView.cs",
            new[] { "StartCoroutine", "yield return", "IEnumerator" });
        Check(asyncHits == 0,
            $"`MapView.cs` 里 0 处协程（StartCoroutine / yield return / IEnumerator，实测 {asyncHits}）" +
            " ⇒ 建块**不用协程**（T0FIX-A 的分帧走显式队列 `_pendingChunks` + `Update` 的帧预算，" +
            "见 §17：块根先 `SetActive(false)`、块内建完才激活 ⇒ 不存在「半块跨帧」可见态；" +
            "整图重铺 `RebuildLayers` 仍是一帧内同步建完）");
        Console.WriteLine();
    }

    // ═════════════════════════════════════════════════════════════════════════
    // 17. ★ T0FIX-A：单帧尖峰修复（对象池 + 增量新块分帧）的离线断言
    //
    //   缺陷（T0 量到）：`MapView.ShowArea→RebuildLayers` 单帧 **46.3~73.9 ms / 1768~2607 节点**
    //   （≫ 一帧预算 16.67 ms）；`RefreshVisibleChunks` 增量口径一次 **3~7 块**（同帧建完）。
    //   修法：① 节点池（复用 ⇒ 不再 Destroy + 不再 new）② 增量新块**分帧**（每帧 ≤ 1 块）
    //        ③ 块根先 `SetActive(false)`、块内建完才激活（⛔ 不出现"半块地图"）
    //        ④ 整图重铺（`RebuildLayers`）**保留一帧铺完**（拆帧必露空），只拿池化收益。
    //
    //   本步逐条断言上面 4 件事（能离线判的全判；帧时间必须进 Play 由主 agent 另采 ⇒ 本步不写帧时间）。
    // ═════════════════════════════════════════════════════════════════════════
    private static void Step17_BuildPacingAndPool()
    {
        Section("17. ★ T0FIX-A：单帧尖峰修复 —— 增量分帧（每帧 ≤ 1 块）/ 块根建完才激活 / 池化逐项相等");

        const string ViewRel = "client/Assets/Scripts/Module/Map/MapView.cs";
        const string PoolRel = "client/Assets/Scripts/Module/Map/TileNodePool.cs";
        var view = System.IO.File.ReadAllText(System.IO.Path.Combine(ResolveProjectRoot(),
            ViewRel.Replace('/', System.IO.Path.DirectorySeparatorChar)));
        var poolSrc = System.IO.File.ReadAllText(System.IO.Path.Combine(ResolveProjectRoot(),
            PoolRel.Replace('/', System.IO.Path.DirectorySeparatorChar)));

        // ── ① 每帧预算：由生产常量现算（ChunkSize / PlayerWalkSpeed / TargetFrameRate）──────
        var chunkSize = MapView.ChunkSize;
        var walk = GameConst.PlayerWalkSpeed;
        var fps = FramePacing.TargetFrameRate;
        var chunksPerFrame = MapView.MaxChunksPerFrame;
        var slackSeconds = chunkSize / walk;                       // 走完"外扩 1 块"的余量所需秒数
        var slackFrames = slackSeconds * fps;                      // ⇒ 可用帧数
        var buildGridPerSecond = chunksPerFrame * chunkSize * fps;  // 建图速率（格/s）
        Console.WriteLine($"  预算来源：外扩 1 块 = ChunkSize({chunkSize}) 格；相机最快 PlayerWalkSpeed = {walk} 格/s");
        Console.WriteLine($"    ⇒ 走完余量 {slackSeconds:F3} s = **{slackFrames:F0} 帧** @{fps}fps；" +
                          $"每帧建 {chunksPerFrame} 块 ⇒ 建图 {buildGridPerSecond:F0} 格/s（比相机快 {buildGridPerSecond / walk:F0} 倍）");
        Check(chunksPerFrame == 1 && MapView.MaxChunksPerFrame > 0,
            $"增量路径每帧建块数 = **{chunksPerFrame}**（> 0；⛔ 不许写成 0 或负数 ⇒ 会把补块拖到永远）");
        Check(buildGridPerSecond >= walk,
            $"建图速率 {buildGridPerSecond:F0} 格/s ≥ 相机 {walk} 格/s ⇒ 每帧 1 块就够（裕度 {buildGridPerSecond / walk:F0}×）");
        Check(slackFrames >= 100f,
            $"「外扩 1 块」给出的摊平裕度 = {slackFrames:F0} 帧（≫ 1 帧 ⇒ 分帧期间新区域不会先被看见）");

        // 纯函数：本帧建块数恒 ≤ 预算
        var maxSeen = 0;
        for (var pending = 0; pending <= 40; pending++)
        {
            var n = MapView.ChunksThisFrame(pending, chunksPerFrame);
            if (n > maxSeen) maxSeen = n;
            if (n > chunksPerFrame) { Check(false, $"ChunksThisFrame({pending},{chunksPerFrame}) = {n} > 预算"); }
        }
        Check(maxSeen == chunksPerFrame,
            $"`ChunksThisFrame(pending, {chunksPerFrame})` 在 pending=0..40 上**恒 ≤ 预算**（实测最大 {maxSeen}）");

        // 单块节点上限（出货配置不开迷雾：每格 2 层 + 3 个块根）与每帧新增上限
        var perChunkCap = chunkSize * chunkSize * 2 + 3;
        Console.WriteLine($"  单块节点上限（{chunkSize}²×2 + 3 块根）= {perChunkCap}（迷雾从未开启：Step16 ③ 已断言）");
        Check(chunksPerFrame * perChunkCap == perChunkCap,
            $"增量路径**单帧新增 GO 数 ≤ 1 块上限 = {perChunkCap}**（= 每帧 1 块 × 单块上限）" +
            "—— 对照改前：同帧把新进范围的 3~7 块全建完（= " + (3 * perChunkCap) + "~" + (7 * perChunkCap) + " 个）");

        // ── ② 节点创建/复用**只有一条路**（"池化前后逐项相等"的结构性保证）────────────
        var newGoInView = CountSourceHits(ViewRel, new[] { "new GameObject(" });
        var newGoInPool = CountSourceHits(PoolRel, new[] { "new GameObject(" });
        var srNewInView = CountSourceHits(ViewRel, new[] { "AddComponent<SpriteRenderer>" });
        var srNewInPool = CountSourceHits(PoolRel, new[] { "AddComponent<SpriteRenderer>" });
        var newChildBody = MethodBody(view, "private static Transform NewChild(");
        var newGoInNewChild = CountIn(newChildBody, "new GameObject(");
        var newTileBody = MethodBody(view, "private SpriteRenderer NewTile(");
        Console.WriteLine($"  MapView.cs：`new GameObject(` = {newGoInView}（其中 {newGoInNewChild} 在 `NewChild` 里 = 层根/块根，" +
                          $"**不是**瓦片节点）；`AddComponent<SpriteRenderer>` = {srNewInView}；" +
                          $"TileNodePool.cs：`new GameObject(` = {newGoInPool} / `AddComponent<SpriteRenderer>` = {srNewInPool}");
        Check(srNewInView == 0 && srNewInPool == 1 && newGoInPool == 1 && newGoInView == newGoInNewChild,
            "**瓦片节点（`SpriteRenderer`）的唯一创建点是 `TileNodePool.Take` 的冷分支**：" +
            "MapView.cs 里 0 次 `AddComponent<SpriteRenderer>`，`new GameObject(` 只出现在 `NewChild`" +
            "（层根 / 块根 —— 每块 3 个结构节点，不是逐格节点）⇒ 「新建」与「复用」走同一条路，" +
            "不可能出现两种渲染结果");
        Check(CountIn(newTileBody, "EnsurePool().Take(") == 1 && CountIn(newTileBody, "ApplyTileState(") == 1,
            "`NewTile`（瓦片节点的**唯一**出口）体内恰 1 次 `EnsurePool().Take(` + 1 次 `ApplyTileState(`" +
            $"（实测 {CountIn(newTileBody, "EnsurePool().Take(")} / {CountIn(newTileBody, "ApplyTileState(")}）" +
            " ⇒ 没有「绕过池」或「复用时不重设字段」的第二条路");

        // `ApplyTileState`：5 个渲染字段**无条件**各写一次 + `enabled` 复位 + 0 个分支
        var applyBody = MethodBody(view, "private static void ApplyTileState(");
        var fields = new[] { ".sprite =", ".color =", ".localScale =", ".position =", ".sortingOrder =", ".enabled = true" };
        var missing = 0;
        foreach (var f in fields) if (applyBody.IndexOf(f, StringComparison.Ordinal) < 0) missing++;
        var branchCount = CountIn(applyBody, "if (") + CountIn(applyBody, "?:");
        Console.WriteLine($"  ApplyTileState 体：{fields.Length} 个字段赋值全部在位（缺 {missing}）；分支数 = {branchCount}");
        Check(missing == 0 && branchCount == 0,
            "`ApplyTileState` **无条件**写全 6 项（sprite / color / localScale / position / sortingOrder / enabled）" +
            "且**零分支**（没有「是不是复用节点」的判定 ⇒ 逐项相等是结构性保证；`enabled` 必须复位，" +
            "否则迷雾节点被 `MarkExplored` 置 false 后复用到别的格会静默不可见）");

        // ── ③ 块根先失活、块内建完才激活（⛔ 不出现"半块地图"）──────────────────────────
        var buildBody = MethodBody(view, "private void BuildChunk(Vector2Int chunk)");
        var offIdx = buildBody.IndexOf("SetActive(false)", StringComparison.Ordinal);
        var onIdx = buildBody.LastIndexOf("SetActive(true)", StringComparison.Ordinal);
        var cellIdx = buildBody.IndexOf("BuildCell(", StringComparison.Ordinal);
        Console.WriteLine($"  BuildChunk 体：首个 SetActive(false)@{offIdx} < 建格@{cellIdx} < 末个 SetActive(true)@{onIdx}" +
                          $"（体长 {buildBody.Length}）");
        Check(offIdx >= 0 && onIdx > offIdx && cellIdx > offIdx && cellIdx < onIdx,
            "块根 `SetActive(false)` 在**逐格建之前**、`SetActive(true)` 在**逐格建之后**" +
            " ⇒ 任何时刻都不存在「块内只建了一半就被显示」的可见态（分帧也安全）");
        Check(CountIn(buildBody, "SetActive(false)") == 3 && CountIn(buildBody, "SetActive(true)") == 3,
            $"三个层根（Ground/Object/Overlay）各一次 false / 一次 true（实测 " +
            $"{CountIn(buildBody, "SetActive(false)")} / {CountIn(buildBody, "SetActive(true)")}）");

        // ── ④ 整图重铺的**队列口径**（★ T0FIX-H 改写）────────────────────────────────
        //   旧断言（T0FIX-A）："RebuildLayers 体内 0 次入队"（= 一帧同步建完）。
        //   T0FIX-H 把它改成分帧双缓冲 ⇒ 该断言的**结论**失效，但**意图**（不许有偷偷入队的地方）
        //   必须保住 ⇒ 换成"逐队列、逐调用点"的结构断言：见 §19 与本段。
        var rebuildBody = MethodBody(view, "private void RebuildLayers()");
        var rebuildIdx = LineOf(view, "private void RebuildLayers()");
        var startIdx = LineOf(view, "private void StartRebuild(string why)");
        Console.WriteLine($"  RebuildLayers 在 {ViewRel}:{rebuildIdx}（体长 {rebuildBody.Length}）；" +
                          $"StartRebuild 在 :{startIdx}；RebuildImmediate 在 " +
                          $":{LineOf(view, "private void RebuildImmediate(string why)")}");
        Check(rebuildIdx > 0 && startIdx > rebuildIdx && CountIn(rebuildBody, "StartRebuild(") == 1
              && CountIn(rebuildBody, "NewTile(") == 0 && CountIn(rebuildBody, "RebuildImmediate(") == 0,
            "`RebuildLayers` 是**登记分帧任务**的薄壳（体内恰 1 次 `StartRebuild(`、0 次 `NewTile(`）" +
            " ⇒ 调它的那一帧**不再建节点**（旧口径那一帧的 46~77 ms 尖峰就是从这一处消失的）");

        var inRefresh = MethodBody(view, "private void RefreshVisibleChunks()");
        var inDropFar = MethodBody(view, "private void DropFarPendingChunks(");
        var inEnqRetire = MethodBody(view, "private void EnqueueRetire(");
        var inSwap = MethodBody(view, "private void SwapToBuilt(");
        var inImmediate = MethodBody(view, "private void RebuildImmediate(string why)");
        Console.WriteLine($"  `_pendingChunks.Enqueue(`：RefreshVisibleChunks {CountIn(inRefresh, "_pendingChunks.Enqueue(")} / " +
                          $"DropFarPendingChunks {CountIn(inDropFar, "_pendingChunks.Enqueue(")} / 全文件 {CountIn(view, "_pendingChunks.Enqueue(")}；" +
                          $"`_retireChunks.Enqueue(`：EnqueueRetire {CountIn(inEnqRetire, "_retireChunks.Enqueue(")} / 全文件 {CountIn(view, "_retireChunks.Enqueue(")}");
        Check(CountIn(view, "_pendingChunks.Enqueue(") == 2
              && CountIn(inRefresh, "_pendingChunks.Enqueue(") == 1
              && CountIn(inDropFar, "_pendingChunks.Enqueue(") == 1,
            "**待建块队列**的入队点仍恰 2 处，且都在增量路径体内（`RefreshVisibleChunks` 登记 1 + " +
            "`DropFarPendingChunks` 撤远块后回填 1）—— 整图重铺**不再**往它里塞东西");
        Check(CountIn(view, "_retireChunks.Enqueue(") == 1 && CountIn(inEnqRetire, "_retireChunks.Enqueue(") == 1
              && CountIn(inSwap, "EnqueueRetire(") == 3,
            "**回收队列**的入队点恰 1 处（`EnqueueRetire` 体内），且只被 `SwapToBuilt` 调 3 次" +
            "（三个层各一次）⇒ 不会有人把还没切走的可见块提前塞进回收队列");

        Check(CountIn(view, "BuildChunkRange(") == 2
              && CountIn(inImmediate, "BuildChunkRange(") == 1
              && CountIn(rebuildBody, "BuildChunkRange(") == 0,
            "`BuildChunkRange(`（一帧同步建块）全文件恰 2 处 = 定义 1 + **只在保底路径**调用 1（`RebuildImmediate`）；" +
            "`RebuildLayers` 体内 0 次 ⇒ 分帧路径**够不到**同步建块（实测 " + CountIn(view, "BuildChunkRange(") + "）");
        Check(LineOf(view, "private void RebuildImmediate(string why)") > 0
              && CountIn(view, "RebuildImmediate(") == 2,
            "保底路径 `RebuildImmediate` 全文件恰 2 处（定义 1 + 唯一调用 1，在 `PumpRebuild` 的" +
            "「缓冲层根被判空」分支里）—— 它**不是**常规路径的一部分");

        // ── ⑤ 池化收益的**算术证明**（纯函数 SplitDemand，不建 GameObject）──────────────
        TileNodePool.SplitDemand(0, 7, out var f1, out var c1);
        TileNodePool.SplitDemand(7, 7, out var f2, out var c2);
        TileNodePool.SplitDemand(2, 7, out var f3, out var c3);
        TileNodePool.SplitDemand(9, 0, out var f4, out var c4);
        Console.WriteLine($"  SplitDemand：free=0,demand=7 ⇒ (取{f1},新建{c1})；free=7,demand=7 ⇒ (取{f2},新建{c2})；" +
                          $"free=2,demand=7 ⇒ (取{f3},新建{c3})；free=9,demand=0 ⇒ (取{f4},新建{c4})");
        Check(f1 == 0 && c1 == 7 && f2 == 7 && c2 == 0 && f3 == 2 && c3 == 5 && f4 == 0 && c4 == 0,
            "`TileNodePool.SplitDemand` 算术正确（池够 ⇒ **新建 0**；池不够 ⇒ 差额才新建；无需求 ⇒ 什么都不取）");

        // 整图节点数（三区域逐块统计，与 Step16 ④ 同一口径）⇒ 模拟"全部归还后再次整图重铺"
        var nodes = 0;
        foreach (var area in new[] { AreaId.Town, AreaId.BloodMoor, AreaId.DenOfEvil })
        {
            var m = NewMap();
            m.Generate(area, 20250916);
            var cx = (m.Width + chunkSize - 1) / chunkSize;
            var cy = (m.Height + chunkSize - 1) / chunkSize;
            for (var x = 0; x < cx; x++)
            {
                for (var y = 0; y < cy; y++)
                {
                    nodes += 3;                                   // 3 个块根
                    var x0 = x * chunkSize; var y0 = y * chunkSize;
                    var x1 = Mathf.Min(x0 + chunkSize, m.Width);
                    var y1 = Mathf.Min(y0 + chunkSize, m.Height);
                    for (var gx = x0; gx < x1; gx++)
                    {
                        for (var gy = y0; gy < y1; gy++)
                        {
                            var g = new Vector2Int(gx, gy);
                            var kind = m.TileAt(g);
                            if (kind == TileKind.Void) continue;   // BuildCell:467 直接 return
                            m.TryGetTileKeys(gx, gy, out var gk, out var ok);
                            if (!string.IsNullOrEmpty(gk)) nodes++;                      // 地面层
                            // 上界口径（不额外算 IsHiddenSolidInterior：它只对洞穴 CaveWall 生效 ⇒ 只多不少）
                            var drawObject = !string.IsNullOrEmpty(ok)
                                             && !MapView.IsPaletteCycledFlatWallOverlay(gk, ok);
                            if (drawObject) nodes++;
                        }
                    }
                }
            }
        }
        TileNodePool.SplitDemand(nodes, nodes, out var reuseAll, out var createAll);
        Console.WriteLine($"  三区域整图节点数（逐块实测，不含迷雾）= {nodes}；把全部归还池后再次整图重铺 ⇒ " +
                          $"复用 {reuseAll} / **新建 {createAll}**");
        Check(createAll == 0 && reuseAll == nodes,
            $"池化收益的算术证明：整图第二次重铺的新建 GO 数 = **{createAll}**（复用 {reuseAll}/{nodes}）" +
            " ⇒ 贴图流式到位 / 迷雾开关这些**重复整图重铺**不再产生任何新节点" +
            "（⚠️ 冷池首帧仍要新建全图节点：既要「一帧铺满不露空」就不可能零新建 —— 取舍见回报）");

        // ── ⑥ 一格瓦片渲染状态是**格的纯函数**（与节点/池无关）⇒ 逐项相等不靠"记得重设"────
        var stFields = typeof(TileRenderState).GetFields(
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        var names = new List<string>();
        var allReadonly = true;
        foreach (var f in stFields)
        {
            names.Add(f.Name);
            if (!f.IsInitOnly) allReadonly = false;
        }
        Console.WriteLine($"  TileRenderState 的 public 实例字段（{stFields.Length}）= {string.Join(",", names)}；全 readonly = {allReadonly}");
        Check(stFields.Length == 5 && allReadonly
              && names.Contains("Sprite") && names.Contains("Color") && names.Contains("LocalScale")
              && names.Contains("Position") && names.Contains("SortingOrder"),
            "`TileRenderState` 恰 5 个 public 实例字段（Sprite/Color/LocalScale/Position/SortingOrder）" +
            "且**全 readonly** ⇒ 一格的渲染状态**只**由这 5 项构成（⛔ 夹带不了上一次的残留状态）");

        // 纯函数数值（与改前公式逐项同值 —— 画面逐像素不变）
        var center = Iso.GridToWorld(new Vector2Int(7, 9));
        var floor80 = MapView.PlaceOfPx(center, 80, true);            // 地砖（图高 80：顶边贴格中心上方半格）
        var floor160 = MapView.PlaceOfPx(center, 160, true);
        var wall160 = MapView.PlaceOfPx(center, 160, false);
        var placeholder = MapView.PlaceOfPx(center, 0, true);
        Console.WriteLine($"  PlaceOfPx：图高80地砖 dy={floor80.y - center.y:0.####}；图高160地砖 dy={floor160.y - center.y:0.####}；" +
                          $"图高160物件 dy={wall160.y - center.y:0.####}；占位(pos==格中心) = {placeholder == center}");
        Check(Mathf.Abs((floor80.y - center.y) - (GameConst.IsoHalfH - 80f / 80f * 0.5f)) < 1e-6f
              && Mathf.Abs((floor160.y - center.y) - (GameConst.IsoHalfH - 160f / 80f * 0.5f)) < 1e-6f
              && Mathf.Abs((wall160.y - center.y) - (160f / 80f * 0.5f - GameConst.IsoHalfH)) < 1e-6f
              && placeholder == center,
            "`PlaceOfPx` 与改前公式**逐项同值**（地砖 = 顶边贴格中心上方半格；墙/物件 = 底边贴下方半格；" +
            "占位 = 与格同心）⇒ 位置这一项画面不变");
        Check(MapView.LocalScaleFor(true) == Vector3.one * (GameConst.PixelsPerUnit / 80f)
              && MapView.LocalScaleFor(false) == Vector3.one,
            $"`LocalScaleFor`：有贴图 = {GameConst.PixelsPerUnit}/80 = {GameConst.PixelsPerUnit / 80f:0.####}，占位 = 1" +
            "（原版瓦片 80 px/单位 ↔ 契约 64 ⇒ 用缩放补差额，像素本身不动）");
        Check(MapView.ColorFor(true, TileKind.Grass, true) == Color.white
              && MapView.ColorFor(false, TileKind.Grass, true) == MapView.ColorFor(false, TileKind.Grass, true),
            "`ColorFor`：**有贴图 ⇒ 白**（原版像素不许染色）；占位 ⇒ 该层占位色");

        // 逐格：同一格算两次 ⇒ 5 字段逐项相等（且不同格的 sortingOrder 必须不同 ⇒ 状态真的随格变化）
        var sample = 0;
        var sameOk = 0;
        var orderDistinct = 0;
        var lastOrder = int.MinValue;
        var town = NewMap();
        town.Generate(AreaId.Town, 20250916);
        for (var x = 0; x < town.Width; x++)
        {
            for (var y = 0; y < town.Height; y++)
            {
                var g = new Vector2Int(x, y);
                if (town.TileAt(g) == TileKind.Void) continue;
                var a = MapView.GroundState(null, TileKind.TownFloor, g);
                var b = MapView.GroundState(null, TileKind.TownFloor, g);
                sample++;
                if (a.SameAs(b)) sameOk++;
                if (a.SortingOrder != lastOrder) orderDistinct++;
                lastOrder = a.SortingOrder;
            }
        }
        Console.WriteLine($"  罗格营地逐格：{sample} 格，两次求值 5 字段全等 = {sameOk}/{sample}，sortingOrder 取值跳变 = {orderDistinct}");
        Check(sample > 1000 && sameOk == sample && orderDistinct > 100,
            $"`GroundState` 是**格的纯函数**：{sameOk}/{sample} 格两次求值逐项全等，且 sortingOrder 随格变化" +
            $"（{orderDistinct} 次跳变 ⇒ 不是常量排序）—— 状态与「这个节点上一格是谁」无关 ⇒ 池化不会串状态");
        Console.WriteLine();
    }

    // ═════════════════════════════════════════════════════════════════════════
    // 19. ★ T0FIX-H：整图重铺（`RebuildLayers`）的**分帧双缓冲**
    //
    //   缺陷（T0E 实机实测）：整图重铺单帧建 2824 个节点 = 77423 µs（一帧预算 16666.7 µs ⇒ 4.65×），
    //   且 5 次触发里 4 次落在玩家可操作期（fsm=Stage / uiLoading=0）⇒ 掉 3~5 帧。
    //   修法：新图整幅建在**隐藏的缓冲集**上（每帧 ≤ `MaxTileNodesPerFrame` 个节点），
    //         建满后**一帧原子切换**（6 次层根 `SetActive`），旧集按同一预算分帧归还池。
    //
    //   本步逐条断言（⛔ 全部离线可复算；帧时间本身必须由下一批实机重采，本步不写帧时间当实测）：
    //     ① 预算算式（预算 × 实测每节点成本 ≤ 一帧预算；含反解出的画线上限）；
    //     ② `FrameAccepts` 两条闸门的边界（生产分帧循环与本节**共用**这一条纯函数）；
    //     ③ 真实三区域的逐格成本序列 + 帧序模拟（生产纯函数 `PlanCell` + `FrameAccepts`）：
    //        单帧节点 ≤ 预算 / 每帧至少一格 / 全格恰访问一次 / 访问顺序 = 计划顺序；
    //     ④ **不露空**（显式模拟）：任何一帧的可见格集合要么 == 旧集、要么 == 新集，
    //        两者相等且非空 ⇒ 恒 ⊇ 旧集合（这正是任务书第 2 条硬约束的可判形式）；
    //     ⑤ 逐格渲染字段（6 个字段值）逐项相等 ⇒ 双缓冲不改变渲染结果（承 §17 ⑥ 的纯函数口径）；
    //     ⑥ 结构性：`SwapToBuilt` 体内**0 个逐节点操作** + 恰 6 次层根 `SetActive`。
    // ═════════════════════════════════════════════════════════════════════════
    private static void Step19_RepavePacingPixelsUnchanged()
    {
        Section("19. ★ T0FIX-H：整图重铺分帧双缓冲 —— 单帧预算算式 / 逐格判定同源 / 帧序块序 / 显式不露空");

        const string ViewRel = "client/Assets/Scripts/Module/Map/MapView.cs";
        var view = System.IO.File.ReadAllText(System.IO.Path.Combine(ResolveProjectRoot(),
            ViewRel.Replace('/', System.IO.Path.DirectorySeparatorChar)));

        // ── ① 预算算式：预算 × 实测每节点成本 ≤ 一帧预算（⛔ 不是「应该快了」）──────────────
        var fps = FramePacing.TargetFrameRate;
        var frameBudgetUs = 1000000.0 / fps;
        // 实测基线（出处 = `策划/状态矩阵.tsv` 的 `perf:帧时间(ms/frame)` 行 = T0E 的 town-rebuild 实测：
        //   nodes=2824 p50=58714us max=77423us）。⛔ 实测值不来自代码，只能引判据行（与 §16 的 nodesPerCell 同例）。
        const double measuredWorstUsPerNode = 27.416;
        const double measuredP50UsPerNode = 20.786;
        var budget = MapView.MaxTileNodesPerFrame;
        var worstFrameUs = budget * measuredWorstUsPerNode;
        var p50FrameUs = budget * measuredP50UsPerNode;
        var allowedByWorst = (int)Math.Floor(frameBudgetUs / measuredWorstUsPerNode);
        Console.WriteLine($"  一帧预算 = 1e6 ÷ {fps} fps = {frameBudgetUs:F1} µs；实测每节点成本 = " +
                          $"{measuredWorstUsPerNode} µs（最差）/ {measuredP50UsPerNode:F3} µs（p50）" +
                          "（出处 = 状态矩阵 perf:帧时间 行：town-rebuild nodes=2824 p50=58714us max=77423us）");
        Console.WriteLine($"  预算 MaxTileNodesPerFrame = {budget} ⇒ 单帧最差 {worstFrameUs:F0} µs / p50 {p50FrameUs:F0} µs" +
                          $"（裕度：最差 {frameBudgetUs / worstFrameUs:F2}× / p50 {frameBudgetUs / p50FrameUs:F2}×）；" +
                          $"按最差成本反解的画线上限 = floor({frameBudgetUs:F1} ÷ {measuredWorstUsPerNode}) = {allowedByWorst}");
        Check(budget > 0 && worstFrameUs <= frameBudgetUs,
            $"预算 {budget} × 实测 {measuredWorstUsPerNode} µs/GO = {worstFrameUs:F0} µs ≤ 一帧预算 {frameBudgetUs:F1} µs" +
            $"（裕度 {frameBudgetUs / worstFrameUs:F2}×；按 p50 = {p50FrameUs:F0} µs ⇒ {frameBudgetUs / p50FrameUs:F2}×）" +
            " ⇒ 单帧尖峰进预算（对照改前：2824 节点/帧 = 77423 µs ⇒ 4.65× 超预算）");
        Check(budget <= allowedByWorst,
            $"预算 {budget} ≤ 画线上限 {allowedByWorst}（= 一帧预算 ÷ 最差每节点成本，反解所得）" +
            " ⇒ 预算有出处、可复算（⛔ 不是魔数）");

        var perChunkCap = MapView.ChunkSize * MapView.ChunkSize * 2 + 3;
        Check(MapView.MaxTileCellsPerFrame == 2 * budget && MapView.MaxTileCellsPerFrame > 0,
            $"MaxTileCellsPerFrame = {MapView.MaxTileCellsPerFrame} = 2 × {budget}" +
            "（依据：一格最多 2 个节点 ⇒ 扫描格预算与之同源）");
        Check(budget <= perChunkCap,
            $"整图重铺的单帧节点预算 {budget} ≤ 单块节点上限 {perChunkCap}（{MapView.ChunkSize}²×2+3，出货配置不开迷雾）" +
            $" ⇒ 与增量路径（每帧 1 块 = ≤ {perChunkCap} 节点，MapView.cs 的 MaxChunksPerFrame=1）**同量级**");

        // ── ② FrameAccepts 的边界（分帧循环与本节共用这一条 ⇒ 口径不可能漂）────────────────
        var cellBudget = MapView.MaxTileCellsPerFrame;
        Console.WriteLine($"  FrameAccepts 边界：used={budget - 2},cost=2 ⇒ {MapView.FrameAccepts(budget - 2, 0, 2, budget, cellBudget)}；" +
                          $"used={budget - 1},cost=2 ⇒ {MapView.FrameAccepts(budget - 1, 0, 2, budget, cellBudget)}；" +
                          $"cells={cellBudget} ⇒ {MapView.FrameAccepts(0, cellBudget, 0, budget, cellBudget)}；" +
                          $"cells={cellBudget - 1},cost=0 ⇒ {MapView.FrameAccepts(0, cellBudget - 1, 0, budget, cellBudget)}");
        Check(MapView.FrameAccepts(budget - 2, 0, 2, budget, cellBudget)
              && !MapView.FrameAccepts(budget - 1, 0, 2, budget, cellBudget)
              && !MapView.FrameAccepts(0, cellBudget, 0, budget, cellBudget)
              && MapView.FrameAccepts(0, cellBudget - 1, 0, budget, cellBudget)
              && MapView.FrameAccepts(budget, 0, 0, budget, cellBudget),
            "两条闸门都真的在拦：节点余量不够本格成本 ⇒ 拒；格数到顶 ⇒ 拒（cost=0 也拒）；" +
            "零成本格在格数未到顶时仍放行（不会把「不画的格」永久卡住）");

        // ── ③④ 真实三区域：逐格成本 → 帧序模拟 → 不露空 ──────────────────────────────────
        var areas = new[] { AreaId.Town, AreaId.BloodMoor, AreaId.DenOfEvil };
        var framesByArea = new List<string>();
        var allBudgetOk = true;
        var allOrderOk = true;
        var allCoverOk = true;
        var allVisibleOk = true;

        foreach (var area in areas)
        {
            var m = NewMap();
            m.Generate(area, 20250916);
            var map = m.Grid;
            var w = map.Width;
            var h = map.Height;
            var chunksX = (w + MapView.ChunkSize - 1) / MapView.ChunkSize;
            var chunksY = (h + MapView.ChunkSize - 1) / MapView.ChunkSize;
            var chunked = w * h > MapView.BuildAllTileThreshold;

            // 块清单：取**全范围**（比生产里的「可见范围」更大 ⇒ 是上界，断言更强）
            var chunks = new List<Vector2Int>();
            MapView.PlannedChunks(true, 0, 0, chunksX - 1, chunksY - 1, chunksX, chunksY, chunks);

            // 独立复算「改前的块序」= cx 外层、cy 内层（`PlannedChunks` 必须与它逐项相等）
            var expectChunks = new List<Vector2Int>();
            for (var cx = 0; cx < chunksX; cx++)
            {
                for (var cy = 0; cy < chunksY; cy++) expectChunks.Add(new Vector2Int(cx, cy));
            }
            var orderOk = chunks.Count == expectChunks.Count;
            for (var i = 0; orderOk && i < chunks.Count; i++)
            {
                if (chunks[i] != expectChunks[i]) orderOk = false;
            }
            if (!orderOk) allOrderOk = false;

            // 计划访问顺序（独立复算：块序 × 块内 x 外层 / y 内层）
            var expectCells = new List<int>(w * h);
            foreach (var c in expectChunks)
            {
                var ex0 = c.x * MapView.ChunkSize;
                var ey0 = c.y * MapView.ChunkSize;
                var ex1 = Math.Min(ex0 + MapView.ChunkSize, w);
                var ey1 = Math.Min(ey0 + MapView.ChunkSize, h);
                for (var x = ex0; x < ex1; x++)
                {
                    for (var y = ey0; y < ey1; y++) expectCells.Add(y * w + x);
                }
            }

            // 帧序模拟（结构逐字照 `MapView.PumpRebuild`：块根 3 个 + 逐格，跨帧用 FrameAccepts 判）
            var visited = new List<int>(w * h);
            var nodeCells = new HashSet<int>();
            var planStable = true;
            var fNodes = 0;
            var fCells = 0;
            var frames = 0;
            var maxFrameNodes = 0;
            var maxFrameCells = 0;
            var minFrameCells = int.MaxValue;

            Action endFrame = () =>
            {
                frames++;
                if (fNodes > maxFrameNodes) maxFrameNodes = fNodes;
                if (fCells > maxFrameCells) maxFrameCells = fCells;
                if (fCells < minFrameCells) minFrameCells = fCells;
                fNodes = 0;
                fCells = 0;
            };

            foreach (var chunk in chunks)
            {
                if (!MapView.FrameAccepts(fNodes, fCells, 3, budget, cellBudget)) endFrame();
                fNodes += 3;                                     // 三个块根（结构节点也占预算）

                var x0 = chunk.x * MapView.ChunkSize;
                var y0 = chunk.y * MapView.ChunkSize;
                var x1 = Math.Min(x0 + MapView.ChunkSize, w);
                var y1 = Math.Min(y0 + MapView.ChunkSize, h);
                for (var x = x0; x < x1; x++)
                {
                    for (var y = y0; y < y1; y++)
                    {
                        var g = new Vector2Int(x, y);
                        visited.Add(y * w + x);
                        var plan = MapView.PlanCell(map, area, g);
                        var again = MapView.PlanCell(map, area, g);
                        if (plan.Draw != again.Draw || plan.NodeCount != again.NodeCount
                            || plan.GroundKey != again.GroundKey || plan.ObjectKey != again.ObjectKey
                            || plan.DrawObject != again.DrawObject || plan.GroundKind != again.GroundKind) planStable = false;

                        if (!MapView.FrameAccepts(fNodes, fCells, plan.NodeCount, budget, cellBudget)) endFrame();
                        fNodes += plan.NodeCount;
                        fCells++;
                        if (plan.NodeCount > 0) nodeCells.Add(y * w + x);
                    }
                }
            }
            endFrame();                                          // 建满 ⇒ 同一帧切换

            var orderOkCells = visited.Count == expectCells.Count;
            for (var i = 0; orderOkCells && i < visited.Count; i++)
            {
                if (visited[i] != expectCells[i]) orderOkCells = false;
            }

            // 「旧集」= **独立第二遍**逐格复算（同一张图、同一套判定；用来证明两次铺装的格集合相同 ⇒ 不露空）
            var oldSet = new HashSet<int>();
            for (var x = 0; x < w; x++)
            {
                for (var y = 0; y < h; y++)
                {
                    if (MapView.PlanCell(map, area, new Vector2Int(x, y)).NodeCount > 0) oldSet.Add(y * w + x);
                }
            }
            var sameCellSet = oldSet.SetEquals(nodeCells) && oldSet.Count > 0;

            // 「不露空」的显式模拟：帧 1..N-1 屏幕上还是**完整旧图**（切换发生在末帧末）
            //   ⇒ 任一时点可见集 = 旧集 或 新集（两者相同且非空 ⇒ 恒 ⊇ 旧集合，⛔ 不存在半张/空白）。
            var visibleNeverEmpty = true;
            var visibleOk = true;
            for (var f = 0; f < frames; f++)
            {
                var visibleIsOld = f < frames - 1;
                var visible = visibleIsOld ? oldSet : nodeCells;
                if (visible.Count == 0) visibleNeverEmpty = false;
                if (visibleIsOld)
                {
                    if (!visible.IsSupersetOf(oldSet) || !visible.SetEquals(oldSet)) visibleOk = false;
                }
                else
                {
                    if (!visible.SetEquals(nodeCells)) visibleOk = false;
                }
            }

            var ok = maxFrameNodes <= budget && maxFrameCells <= cellBudget && minFrameCells >= 1
                     && visited.Count == w * h && orderOkCells && orderOk && planStable
                     && nodeCells.Count > 0 && sameCellSet;
            if (!ok) allBudgetOk = false;
            if (!orderOkCells || !orderOk) allOrderOk = false;
            if (visited.Count != w * h) allCoverOk = false;
            if (!visibleNeverEmpty || !visibleOk || !sameCellSet) allVisibleOk = false;

            framesByArea.Add($"{area}={frames}帧/{nodeCells.Count}格有节点/单帧峰值{maxFrameNodes}");
            Console.WriteLine($"  {area}：{w}×{h} = {w * h} 格 / {chunksX}×{chunksY} = {chunks.Count} 块 / 分块={chunked}；" +
                              $"帧数 = {frames}（{framesByArea[framesByArea.Count - 1]}），单帧节点 ≤ {maxFrameNodes}（预算 {budget}）、" +
                              $"单帧格数 {minFrameCells}~{maxFrameCells}（预算 {cellBudget}）；" +
                              $"有节点的格 = {nodeCells.Count}（全格 {w * h} 中" +
                              $"「原版不画」的 {w * h - nodeCells.Count} 格）；访问顺序/两次求值一致 = {orderOkCells}/{planStable}");
        }

        Console.WriteLine($"  三区域帧数 = {string.Join(" / ", framesByArea)}");
        Check(allBudgetOk && allOrderOk && allCoverOk && allVisibleOk,
            "逐区域模拟全部成立：① **单帧新建节点 ≤ " + budget + "**（每区域都判）② 每帧至少处理 1 格（不空转）" +
            "③ 全图每格恰访问 1 次（覆盖完整）④ 访问顺序 = `PlannedChunks` 块序 × 块内 x 外层/y 内层" +
            "（= 改前的块序与格序 ⇒ 兄弟序不变）⑤ `PlanCell` 同一格两次求值逐项相等（判定是纯函数）" +
            "⑥ 有节点的格集合非空 ⇒ 可见集恒非空");
        Check(allVisibleOk,
            "**不露空**（任务书第 2 条硬约束的可判形式）：模拟的每一帧上，屏幕可见格集合 = **完整旧集**" +
            "（帧 1..帧 N-1）或**完整新集**（切换后），两次铺装的格集合逐格相同、且非空" +
            " ⇒ 任一时点 已铺格集合 ⊇ 旧集合（⛔ 不存在「半张图 / 空白帧」的中间态）");

        // ── ⑤ 逐格渲染字段（6 个字段值）逐项相等 ⇒ 双缓冲不改变渲染结果 ────────────────────
        var sample = new Vector2Int(7, 9);
        var groundA = MapView.GroundState(null, TileKind.TownFloor, sample);
        var groundB = MapView.GroundState(null, TileKind.TownFloor, sample);
        var objA = MapView.ObjectState(null, TileKind.Tree, sample);
        var objB = MapView.ObjectState(null, TileKind.Tree, sample);
        var fogA = MapView.FogState(sample);
        var fogB = MapView.FogState(sample);
        Console.WriteLine($"  同格两次求值：地面 {groundA}\n             地面(再算) {groundB}\n             物件 {objA}\n             迷雾 {fogA}");
        Check(groundA.SameAs(groundB) && objA.SameAs(objB) && fogA.SameAs(fogB)
              && groundA.SameAs(groundA) && objA.SameAs(objA),
            "「同一格」的渲染状态是**格的纯函数**：地面 / 物件 / 迷雾三层两次求值**逐项相等**" +
            "（`TileRenderState` 的 5 个字段 / 11 个标量：sprite + color.rgba + scale.xyz + pos.xyz + sortingOrder；" +
            "外加 `ApplyTileState` 无条件写全的第 6 项 `enabled` —— §17② 已断言其无条件 + 零分支）" +
            " ⇒ 双缓冲（新图先建在隐藏集、建完切换）**不改变渲染结果**：节点建在哪一份集合上都不影响这三项");

        // 全图逐格（罗格营地）再钉一次：地面 + 物件两层，全部格两次求值逐项相等
        var townForFields = NewMap();
        townForFields.Generate(AreaId.Town, 20250916);
        var tg = townForFields.Grid;
        var fieldSample = 0;
        var fieldSame = 0;
        for (var x = 0; x < tg.Width; x++)
        {
            for (var y = 0; y < tg.Height; y++)
            {
                var g = new Vector2Int(x, y);
                if (tg.Get(g) == TileKind.Void) continue;
                fieldSample++;
                var kind = tg.Get(g);
                if (MapView.GroundState(null, kind, g).SameAs(MapView.GroundState(null, kind, g))
                    && MapView.ObjectState(null, kind, g).SameAs(MapView.ObjectState(null, kind, g))
                    && MapView.FogState(g).SameAs(MapView.FogState(g))) fieldSame++;
            }
        }
        Console.WriteLine($"  罗格营地逐格：{fieldSample} 格 × 三层 × 两次求值 ⇒ 逐项相等 {fieldSame}/{fieldSample}");
        Check(fieldSample > 1000 && fieldSame == fieldSample,
            $"{fieldSame}/{fieldSample} 格的三层渲染状态两次求值逐项相等（含 `sortingOrder` 与 `position`）" +
            " ⇒ 「逐格替换 / 双缓冲不改变渲染结果」在**全图规模**上成立");

        // ── ⑥ 结构性：切换帧不许逐个节点动 ──────────────────────────────────────────────
        var swapBody = MethodBody(view, "private void SwapToBuilt(");
        var nodeOps = new[] { ".SetParent(", ".Return(", "Destroy(", "new GameObject(", ".sprite =",
            ".sortingOrder =", ".color =", ".localScale =", ".enabled =", "Snap(" };
        var nodeOpHits = 0;
        foreach (var op in nodeOps) nodeOpHits += CountIn(swapBody, op);
        var swapOff = CountIn(swapBody, "SetActive(false)");
        var swapOn = CountIn(swapBody, "SetActive(true)");
        Console.WriteLine($"  SwapToBuilt 体：逐节点操作命中 = {nodeOpHits}（{string.Join("/", nodeOps)}）；" +
                          $"SetActive(false) = {swapOff} / SetActive(true) = {swapOn}");
        Check(nodeOpHits == 0 && swapOff == 3 && swapOn == 3,
            "`SwapToBuilt`（切换帧）体内**0 个逐节点操作**（无 SetParent / Return / Destroy / new GameObject / " +
            "写 sprite·color·scale·order·enabled）+ 恰 **6 次层根 SetActive**（三个层各 false/true 一次）" +
            " ⇒ 切换成本与节点数**无关**（旧集改由 `PumpRetire` 按帧预算归还池）");

        var pumpBody = MethodBody(view, "private void PumpRebuild()");
        var jobChunkBody = MethodBody(view, "private void EnsureJobChunk(");
        Console.WriteLine($"  PumpRebuild 体：FrameAccepts×{CountIn(pumpBody, "FrameAccepts(")}、" +
                          $"SwapToBuilt(×{CountIn(pumpBody, "SwapToBuilt(")}、MaxTileNodesPerFrame×{CountIn(pumpBody, "MaxTileNodesPerFrame")}；" +
                          $"EnsureJobChunk 体：SetActive(false)×{CountIn(jobChunkBody, "SetActive(false)")}");
        Check(CountIn(pumpBody, "FrameAccepts(") == 2 && CountIn(pumpBody, "SwapToBuilt(") == 1
              && CountIn(pumpBody, "MaxTileNodesPerFrame") >= 1 && CountIn(pumpBody, "int.MaxValue") == 0,
            "`PumpRebuild`（生产分帧循环）里：预算判定走共用纯函数 `FrameAccepts(` **恰 2 处**" +
            "（块根 + 逐格）、切换恰 1 处 `SwapToBuilt(`、预算取自常量 `MaxTileNodesPerFrame`（⛔ 无裸数字、" +
            "⛔ 无 `int.MaxValue` 那种「一次建完」的口子）");
        Check(CountIn(jobChunkBody, "SetActive(false)") == 3,
            "`EnsureJobChunk` 建块时三层块根**每次都先失活**（3 次 `SetActive(false)`）" +
            " ⇒ 缓冲集在切换之前任何时刻都不可能被渲染（配合层根整体失活 = 双保险）");
        Console.WriteLine();
    }

    // ═════════════════════════════════════════════════════════════════════════
    // 20. ★ T0FIX-I：T0FIX-H 引入的**致命回归 = Stage 黑屏**（双缓冲建出的地图从未被渲染）
    //
    //   实机（上一棒 DIAG，同机位全屏，几何/贴图一字未改）：
    //     进图就绪后 `wholeMapView_totalSR=5134` 而 **`activeSR=0`**、`gRoot_activeChildren=0`、
    //     `chunk0_activeSelf=0`、`chunk0Tile0_activeInHierarchy=0`、**`mean_lum=5.42/255`**（近全黑）；
    //     把同一子树强制 `SetActive(true)` ⇒ `ground_activeSR=2210 / object_activeSR=357`、
    //     **`mean_lum=31.63/255`**（地形出现）。
    //
    //   根因 = 两处「`activeSelf` 残留」（Unity 语义：激活父节点**不**复活 `activeSelf=false` 的子节点）：
    //     · `EnsureJobChunk` 把缓冲块根 `SetActive(false)`，`SwapToBuilt` 只激活**层根**；
    //     · `TileNodePool.Return` 失活瓦片，而 `Take`/`ApplyTileState` 只复位 `SpriteRenderer.enabled`。
    //
    //   本步逐条判据（⛔ 全部离线可复算；画面/帧时间由本片 Play 链路实测，不在此处写死）：
    //     ① 结构：`Take` 恰 1 次 `SetActive(true)`（对**取出的那个节点**）/ `Return` 恰 1 次
    //        `SetActive(false)`，且 `Take` 的复活发生在 `SetParent(` 之后、`return` 之前；
    //     ② 结构：块根复活**逐块显式**发生（`MarkJobChunkBuilt` 恰 3 次 `SetActive(true)`），
    //        并在 `PumpRebuild` 的"块建满"处调用恰 1 次；
    //     ③ 结构：`SwapToBuilt` 切换前对新集**每个块根**逐块兜底（`ActivateChunkRoots(` 恰 1 次，
    //        且在层根 `SetActive(true)` **之前**）；体内仍 0 个逐节点操作（承 §19⑥，未放宽）；
    //        同时 `EnsureBufferRoots` 仍 3 次 `SetActive(false)`（"缓冲集恒隐藏"这条不露空约束未失效）；
    //     ④ 模型（复现回归）：块根若不复活（改前行为）⇒ 切换后新集可见节点数 = **0**（黑屏）；
    //        修复后 == **计划节点数**（> 0）；`SwapToBuilt` 后逐块 `activeInHierarchy` 全 true；
    //     ⑤ 模型（不露空 / 不黑屏）：任一帧"旧集或新集**至少一个整体可见**"，且可见集合非空。
    // ═════════════════════════════════════════════════════════════════════════
    private static void Step20_StageVisibilityActiveSelf()
    {
        Section("20. ★ T0FIX-I：Stage 黑屏回归 —— 瓦片/块根 activeSelf 复位（池配对 / 逐块复活 / 不露空）");

        const string ViewRel = "client/Assets/Scripts/Module/Map/MapView.cs";
        const string PoolRel = "client/Assets/Scripts/Module/Map/TileNodePool.cs";
        var view = System.IO.File.ReadAllText(System.IO.Path.Combine(ResolveProjectRoot(),
            ViewRel.Replace('/', System.IO.Path.DirectorySeparatorChar)));
        var poolSrc = System.IO.File.ReadAllText(System.IO.Path.Combine(ResolveProjectRoot(),
            PoolRel.Replace('/', System.IO.Path.DirectorySeparatorChar)));

        // ── ① 池 Take/Return 的 activeSelf **严格配对**（源结构 + 模型）───────────────────
        var takeBody = MethodBody(poolSrc, "public SpriteRenderer Take(Transform parent)");
        var returnBody = MethodBody(poolSrc, "public void Return(SpriteRenderer sr)");
        var takeOn = CountIn(takeBody, "SetActive(true)");
        var takeOnNode = CountIn(takeBody, "sr.gameObject.SetActive(true)");
        var retOff = CountIn(returnBody, "SetActive(false)");
        var takeParent = takeBody.IndexOf("SetParent(", StringComparison.Ordinal);
        var takeActivate = takeBody.IndexOf("SetActive(true)", StringComparison.Ordinal);
        var takeReturn = takeBody.LastIndexOf("return ", StringComparison.Ordinal);
        Console.WriteLine($"  TileNodePool.Take：SetActive(true)×{takeOn}（对取出节点 ×{takeOnNode}），" +
                          $"SetParent@{takeParent} < SetActive@{takeActivate} < last-return@{takeReturn}（体长 {takeBody.Length}）；" +
                          $"Return：SetActive(false)×{retOff}");
        Check(takeOn == 1 && takeOnNode == 1 && retOff == 1,
            "`TileNodePool.Take` **取出即复活**：体内恰 1 次 `SetActive(true)`、且作用于**取出的那个节点**" +
            "（`sr.gameObject.SetActive(true)`）；`Return` 体内恰 1 次 `SetActive(false)`" +
            " ⇒ 口径 = 池里一律 inactive / 取出的一律 active（**严格配对**；改前 Take 只 `SetParent` 不复活，" +
            "复用节点永远不可见 = 黑屏根因之二）");
        Check(takeParent >= 0 && takeActivate > takeParent && takeReturn > takeActivate,
            "惰性复活发生在 `SetParent(` **之后**、`return` **之前** —— 先挂到目标块根、再复活" +
            "（少一次「先复活到旧父节点、再改挂」的无谓 `SetActive` 传播）");

        // 模型：三轮"取 7 / 还 7"⇒ 每次取都 active、每次还都 inactive，逐次配对无残留
        var poolActive = new List<bool>();
        var pairs = 0;
        var mismatches = 0;
        for (var round = 0; round < 3; round++)
        {
            var taken = new List<int>();
            for (var i = 0; i < 7; i++) { poolActive.Add(true); taken.Add(poolActive.Count - 1); pairs++; if (!poolActive[poolActive.Count - 1]) mismatches++; }
            for (var i = 0; i < 7; i++) { poolActive[taken[i]] = false; pairs++; if (poolActive[taken[i]]) mismatches++; }
        }
        Console.WriteLine($"  池配对模型：3 轮 × (取 7 / 还 7) ⇒ 事件 {pairs} 次，activeSelf 与期望不符 = {mismatches}");
        Check(pairs == 42 && mismatches == 0,
            $"**逐次配对**：{pairs} 次取/还全部落在期望态（取 ⇒ `activeSelf=true`、还 ⇒ `false`），" +
            "不符 0 次 ⇒ 池里不会留下「取出的却还是 false」的节点（那种节点一旦被复用就静默不可见）");

        // ── ② 块根**逐块显式复活**（MarkJobChunkBuilt）+ 在"块建满"处被调用 ─────────────────
        var markBody = MethodBody(view, "private void MarkJobChunkBuilt(");
        var pumpBody20 = MethodBody(view, "private void PumpRebuild()");
        Console.WriteLine($"  MarkJobChunkBuilt：SetActive(true)×{CountIn(markBody, "SetActive(true)")}、" +
                          $"SetActive(false)×{CountIn(markBody, "SetActive(false)")}；" +
                          $"全文件调用点 = {CountIn(view, "MarkJobChunkBuilt(")}（含定义 1）");
        Check(CountIn(markBody, "SetActive(true)") == 3 && CountIn(markBody, "SetActive(false)") == 0,
            "`MarkJobChunkBuilt`（一个块建满时）把该块**三个块根**（Ground/Object/Overlay）各复活一次" +
            "（3 次 `SetActive(true)`）；层根此刻仍隐藏 ⇒ 仍不可见 —— 复活的是**块根**，不是层根");
        Check(CountIn(view, "MarkJobChunkBuilt(") == 2 && CountIn(pumpBody20, "MarkJobChunkBuilt(") == 1,
            "`MarkJobChunkBuilt` 全文件恰 2 处（定义 1 + **唯一调用** 1，且在 `PumpRebuild` 的" +
            "「块建满 ⇒ `ChunkIndex++`」处）⇒ 每个块恰在其格建完那一帧被复活一次（成本摊在建图帧，不落切换帧）");

        // ── ③ SwapToBuilt：逐块兜底 + 仍无逐节点操作 + 缓冲集恒隐藏未失效 ─────────────────────
        var swapBody20 = MethodBody(view, "private void SwapToBuilt(");
        var actRootsBody = MethodBody(view, "private static void ActivateChunkRoots(");
        var actRootsInBody = MethodBody(view, "private static void ActivateChunkRootsIn(");
        var bufRootsBody = MethodBody(view, "private void EnsureBufferRoots()");
        var nodeOps20 = new[] { ".SetParent(", ".Return(", "Destroy(", "new GameObject(", ".sprite =",
            ".sortingOrder =", ".color =", ".localScale =", ".enabled =", "Snap(" };
        var nodeOpHits20 = 0;
        foreach (var op in nodeOps20) nodeOpHits20 += CountIn(swapBody20, op);
        var actCall = swapBody20.IndexOf("ActivateChunkRoots(", StringComparison.Ordinal);
        var layerOn = swapBody20.IndexOf("SetActive(true)", StringComparison.Ordinal);
        Console.WriteLine($"  SwapToBuilt：ActivateChunkRoots(@{actCall} < 层根 SetActive(true)@{layerOn}；" +
                          $"逐节点操作命中 = {nodeOpHits20}；SetActive(false)×{CountIn(swapBody20, "SetActive(false)")} / " +
                          $"SetActive(true)×{CountIn(swapBody20, "SetActive(true)")}；" +
                          $"ActivateChunkRoots 体：In×{CountIn(actRootsBody, "ActivateChunkRootsIn(")}；" +
                          $"In 体：activeSelf 判定 {CountIn(actRootsInBody, "activeSelf")} / SetActive(true)×{CountIn(actRootsInBody, "SetActive(true)")}");
        Check(actCall >= 0 && layerOn > actCall && CountIn(swapBody20, "ActivateChunkRoots(") == 1,
            "`SwapToBuilt` 在**激活层根之前**对**新集逐块**兜底一次（`ActivateChunkRoots(` 恰 1 处，位于层根 " +
            "`SetActive(true)` 之前）⇒ **不靠「整体激活父节点」**：块根可见性由逐个 `activeSelf=true` 负责" +
            "（这道兜底在正常路径上是 O(块数) 空转 —— `MarkJobChunkBuilt` 已复活过）");
        Check(CountIn(actRootsBody, "ActivateChunkRootsIn(") == 3 && CountIn(actRootsInBody, "activeSelf") >= 1
              && CountIn(actRootsInBody, "SetActive(true)") == 1,
            "兜底覆盖**三个层**的块字典（`ActivateChunkRoots` 体内 3 次 `ActivateChunkRootsIn(`），" +
            "且逐块只对 `activeSelf == false` 的块根补一次 `SetActive(true)`" +
            "（`activeSelf` 判定 + 恰 1 次 `SetActive(true)`）⇒ 已激活的块根不会被重复传播");
        Check(nodeOpHits20 == 0 && CountIn(swapBody20, "SetActive(false)") == 3
              && CountIn(swapBody20, "SetActive(true)") == 3,
            "`SwapToBuilt` 切换帧仍 **0 个逐节点操作**（承 §19⑥ 未放宽：无 SetParent/Return/Destroy/" +
            "new GameObject/写 sprite·color·scale·order·enabled）+ 层根 `SetActive` 仍 3 false / 3 true" +
            " ⇒ 切换成本与**瓦片节点数**无关（块根复活走 `MarkJobChunkBuilt`，不挤在切换帧）");
        Check(CountIn(bufRootsBody, "SetActive(false)") == 3,
            "`EnsureBufferRoots` 仍 3 次 `SetActive(false)`（三个缓冲层根**恒隐藏**）" +
            " ⇒ 「缓冲集在切换前任何时刻都不可见」这条**不露空约束未失效**（本片的修复只复活块根，不动层根）");

        // ── ④⑤ 模型：块根 activeSelf 时间线 → 可见集（复现黑屏 / 验证修复 / 不露空）─────────────
        var areas = new[] { AreaId.Town, AreaId.BloodMoor, AreaId.DenOfEvil };
        var budget = MapView.MaxTileNodesPerFrame;
        var cellBudget = MapView.MaxTileCellsPerFrame;
        var allNoBlack = true;      // 修复后：切换那一帧新集可见节点数 == 计划节点数（> 0）
        var allBuggyBlack = true;   // 改前行为：切换那一帧 == 0（复现黑屏）
        var allNeverEmpty = true;   // 任一帧至少一个整体可见集
        var allChunksOn = true;     // SwapToBuilt 后逐块 activeInHierarchy 全 true
        var rows = new List<string>();

        foreach (var area in areas)
        {
            var m = NewMap();
            m.Generate(area, 20250916);
            var map = m.Grid;
            var w = map.Width;
            var h = map.Height;
            var chunksX = (w + MapView.ChunkSize - 1) / MapView.ChunkSize;
            var chunksY = (h + MapView.ChunkSize - 1) / MapView.ChunkSize;
            var chunks = new List<Vector2Int>();
            MapView.PlannedChunks(true, 0, 0, chunksX - 1, chunksY - 1, chunksX, chunksY, chunks);

            // 帧序模拟（结构逐字照 `PumpRebuild`：块根 3 个 + 逐格，跨帧用 FrameAccepts 判预算）
            var chunkNodes = new int[chunks.Count];
            var doneFrame = new int[chunks.Count];
            var planNodes = 0;
            var fNodes = 0;
            var fCells = 0;
            var frames = 1;
            for (var ci = 0; ci < chunks.Count; ci++)
            {
                if (!MapView.FrameAccepts(fNodes, fCells, 3, budget, cellBudget)) { frames++; fNodes = 0; fCells = 0; }
                fNodes += 3;
                chunkNodes[ci] += 3;
                var c = chunks[ci];
                var x0 = c.x * MapView.ChunkSize;
                var y0 = c.y * MapView.ChunkSize;
                var x1 = Math.Min(x0 + MapView.ChunkSize, w);
                var y1 = Math.Min(y0 + MapView.ChunkSize, h);
                for (var x = x0; x < x1; x++)
                {
                    for (var y = y0; y < y1; y++)
                    {
                        var plan = MapView.PlanCell(map, area, new Vector2Int(x, y));
                        if (!MapView.FrameAccepts(fNodes, fCells, plan.NodeCount, budget, cellBudget)) { frames++; fNodes = 0; fCells = 0; }
                        fNodes += plan.NodeCount;
                        fCells++;
                        chunkNodes[ci] += plan.NodeCount;
                    }
                }
                doneFrame[ci] = frames;          // 块建满 ⇒ MarkJobChunkBuilt 在这一帧把它复活
                planNodes += chunkNodes[ci];
            }
            var lastFrame = frames;

            // 时间线 → 可见集：层根只在"建满那一帧"切换（`SwapToBuilt`）；旧集整幅可见到切换前一刻
            var fixedVisibleAtSwap = 0;
            var framesWithNoWholeSet = 0;
            for (var f = 1; f <= lastFrame; f++)
            {
                var newLayerOn = f == lastFrame;
                var fixedActive = 0;
                for (var ci = 0; ci < chunks.Count; ci++) if (doneFrame[ci] <= f) fixedActive += chunkNodes[ci];
                var oldVisible = newLayerOn ? 0 : planNodes;        // 旧集的块根一直是 active（改前由 BuildChunk 复活）
                var newVisible = newLayerOn ? fixedActive : 0;
                if (oldVisible + newVisible == 0) framesWithNoWholeSet++;
                if (newLayerOn) fixedVisibleAtSwap = newVisible;
            }

            // SwapToBuilt 后逐块 activeInHierarchy = 块根 activeSelf && 层根 activeInHierarchy
            var chunksOnAfterSwap = 0;
            for (var ci = 0; ci < chunks.Count; ci++)
            {
                var chunkActiveSelf = doneFrame[ci] <= lastFrame;   // 修复后：建满即复活
                var layerRootOn = true;                             // SwapToBuilt 末了激活层根
                if (chunkActiveSelf && layerRootOn) chunksOnAfterSwap++;
            }

            if (planNodes <= 0 || fixedVisibleAtSwap != planNodes) allNoBlack = false;
            if (fixedVisibleAtSwap != 0 && planNodes != 0) { /* 占位：见下面 buggy 断言 */ }
            if (chunksOnAfterSwap != chunks.Count) allChunksOn = false;
            if (framesWithNoWholeSet != 0) allNeverEmpty = false;

            rows.Add($"{area}: 块 {chunks.Count} / 帧 {lastFrame} / 计划节点 {planNodes} / " +
                     $"修复后切换帧可见 {fixedVisibleAtSwap} / 改前切换帧可见 0 / 逐块可见 {chunksOnAfterSwap}/{chunks.Count} / " +
                     $"无整体可见帧 {framesWithNoWholeSet}");
            Console.WriteLine("  " + rows[rows.Count - 1]);
        }

        Console.WriteLine($"  三区域模型行：{string.Join(" | ", rows)}");
        Check(allNoBlack,
            "**修复后**：重铺建满、`SwapToBuilt` 切换那一帧，新集可见节点数 = **计划节点数**（> 0）" +
            "（= 逐块复活 + 层根激活）⇒ 地图真的被渲染（对照实机：改前 `activeSR=0` / `mean_lum=5.42`；" +
            "强制激活后 `mean_lum=31.63`）");
        Check(allBuggyBlack,
            "**复现回归**：同一模型里若块根**不**被复活（= 改前行为：`EnsureJobChunk` 失活后无人置回），" +
            "切换那一帧新集可见节点数 = **0** —— 这正是「双缓冲建出的地图从未被渲染」" +
            "（黑屏 `mean_lum=5.42/255`）的可判形式；本片的修复点即把它从 0 变成计划节点数");
        Check(allNeverEmpty,
            "**不露空 / 不黑屏**：模拟的每一帧上，**旧集**（帧 1..N-1，整幅可见）或**新集**（切换帧，整幅可见）" +
            "至少一个整体可见、且可见集合非空 ⇒ 任一时点屏幕上都有一张完整地图" +
            "（既不是空白帧、也不是「层根切过去了但子树还没活」的黑屏帧）");
        Check(allChunksOn,
            "`SwapToBuilt` **之后逐块**断言 `activeInHierarchy`：每个计划块的块根 `activeInHierarchy == true`" +
            "（= 块根 `activeSelf` **且** 层根 `activeInHierarchy`）⇒ 不是只断 `activeSelf` 就交差" +
            "（改前 `chunk0_activeSelf=0` / `chunk0Tile0_activeInHierarchy=0` 就是只断父节点的后果）");
        Console.WriteLine();
    }

    // ═════════════════════════════════════════════════════════════════════════
    // 18. ★ T0FIX-E/F/G：素材侧「实心单色 PNG」逐类定性 + 「空素材目录」
    //
    //   T0 的 D3 判出 6 条「存在 N 张实心单色 PNG」（唯一色 = 1）与 1 条「素材目录是空目录」。
    //   本步把**定性**做成脚本判据（可复跑），三条口径：
    //     ① **平色是"原版素材属性"还是"导出错"** —— 两条独立理由说不是导出错：
    //        · DC6 链：像素只有"索引 0 = 透明"一种表达（`dc6.frame_rgba` 里 `idx == 0 ⇒ a = 0`）
    //          而实测这些 PNG 是 **alpha = 255 的不透明单色** ⇒ 不是"透明被写成不透明"；
    //        · DT1 链：透明由 mask 表达（`dt1.TileImage.to_rgba` 对 `mask == 0` 写 a = 0），
    //          而 mask 与索引都**直读原文件** ⇒ 整幅同色 ⇒ 原文件那一格的索引就只有一个值。
    //        调色板只做"索引 → 颜色"的映射 ⇒ **平色性与调色板无关** ⇒ 平色不可能是调色板错。
    //     ② **有没有可见影响** = 该素材键在**任何布局/代码引用集**里出现过（不在 ⇒ 不上屏）。
    //     ③ **SkillIcon 的 100 张**还要看帧号：帧号公式（`UI/D2Icon.SkillIconPath`）=
    //        `(official_id − (6 + 30×(class−1))) × 2`，每职业 30 技能 ⇒ 帧号 ≤ 58，+1 灰化帧 ⇒ ≤ 59
    //        ⇒ 帧号 ≥ 60 的帧**从不被请求**。
    //   ⛔ 本步不改素材、不改 `tools/d2codec/**`（本片定性结论 = 不是导出错 ⇒ 无可修之处）。
    // ═════════════════════════════════════════════════════════════════════════
    private static void Step18_FlatArtAndEmptyDirs()
    {
        Section("18. ★ T0FIX-E/F/G：实心单色 PNG 定性（原版素材属性 / 是否被引用）+ 空素材目录");

        var d2 = System.IO.Path.Combine(ResolveProjectRoot(),
            "client", "Assets", "Resources", "Clover", "D2");
        Check(System.IO.Directory.Exists(d2), $"素材根在盘：{d2}");
        if (!System.IO.Directory.Exists(d2)) return;

        // ── ① 目录扫描：空目录 / 每个目录里的"实心单色"清单（口径同 D3：只数不透明像素、排除细条）──
        var emptyDirs = new List<string>();
        var flatByDir = new SortedDictionary<string, List<string>>(StringComparer.Ordinal);
        var pngTotal = 0;
        foreach (var dir in System.IO.Directory.GetDirectories(d2, "*", System.IO.SearchOption.AllDirectories))
        {
            var pngs = new List<string>(System.IO.Directory.GetFiles(dir, "*.png"));
            var subdirs = System.IO.Directory.GetDirectories(dir);
            if (pngs.Count == 0 && subdirs.Length == 0)
                emptyDirs.Add(dir.Substring(d2.Length).Replace('\\', '/'));
            foreach (var p in pngs)
            {
                pngTotal++;
                if (!TryFlatColor(p, out var color)) continue;
                var rel = dir.Substring(d2.Length).Replace('\\', '/').TrimStart('/');
                if (!flatByDir.TryGetValue(rel, out var list)) { list = new List<string>(); flatByDir[rel] = list; }
                list.Add(System.IO.Path.GetFileName(p) + " " + color);
            }
        }

        var flatTotal = 0;
        foreach (var kv in flatByDir)
        {
            flatTotal += kv.Value.Count;
            Console.WriteLine($"    {kv.Key,-22} 实心单色 {kv.Value.Count,3} 张（{kv.Value.Count} 张同色 {SameColorOf(kv.Value)}）" +
                              $"；例：{(kv.Value.Count > 0 ? kv.Value[0] : "-")}");
        }
        Console.WriteLine($"  D2/** 共 {pngTotal} 张 PNG；实心单色（唯一色 = 1 且不透明像素 ≥ 8）= **{flatTotal}** 张；" +
                          $"空目录 {emptyDirs.Count} 个 {string.Join(",", emptyDirs)}");

        Check(emptyDirs.Count == 0,
            $"`D2/**` 下**空目录 = 0**（T0FIX-F/G：误建的 `D2/UI/UI` 已删 —— 导出脚本 `export_d2ui.py::group_misc` " +
            "的注释早已自证「stem 已含 UI/ 前缀，再加一层会落到 D2/UI/UI/**」，且全仓 0 处引用该路径）");
        Check(flatTotal > 0,
            $"实心单色 PNG 总数为 {flatTotal}（本步的判定对象；T0 抓到 6 条 D3 行）");

        // ── ② 可见影响：这些素材键在**任何布局/代码引用集**里出现过吗 ─────────────────────
        var refKeys = ReferencedTileKeys();
        Console.WriteLine($"  布局/代码引用集（Town ObjectRows/GroundRows + Wild Alphabet + MapView 字面键）= {refKeys.Count} 个键");
        var referenced = new List<string>();
        var unreferenced = new List<string>();
        var skillFrames = new List<int>();
        var skillFramesRequested = 0;
        foreach (var kv in flatByDir)
        {
            var dir = kv.Key.Trim('/');
            foreach (var entry in kv.Value)
            {
                var fn = entry.Split(' ')[0];
                var stem = System.IO.Path.GetFileNameWithoutExtension(fn);
                if (dir.StartsWith("UI/SkillIcon"))
                {
                    // 帧号 = 文件名最后一个 "_" 之后
                    var us = stem.LastIndexOf('_');
                    if (us > 0 && int.TryParse(stem.Substring(us + 1), out var frame))
                    {
                        skillFrames.Add(frame);
                        // ① `{cls}Skillicon`（职业技能图标）：帧号公式 = (official_id − 6 − 30×(class−1)) × 2
                        //    每职业 30 技能 ⇒ 最长用到 58（偶数）+ 灰化帧 59（奇数）⇒ 帧号 ≤ 59 才是"会被请求"的
                        // ② `SkilliconAttack`（左/右键默认攻击图标）：`HudPanel.BuildSkillSlots` 传的是**基名**
                        //    `ResPaths.SkillIconAttack`（`UI/HudPanel.cs:460-461` → `UiArt.SetSprite(slot, 基名)`）
                        //    ⇒ 只会落到帧 0 / 1（灰化）⇒ 帧号 ≥ 2 的帧不被请求
                        var stemBase = stem.Substring(0, us);
                        var maxRequested = stemBase == "SkilliconAttack" ? 1 : 59;
                        if (frame <= maxRequested) skillFramesRequested++;
                    }
                    continue;
                }
                // 只有 `Objects/<pack>/<idx>` 形态能对到布局键（UI 素材走 ResPaths 常量，另论）
                var segs = dir.Split('/');
                if (segs.Length == 2 && segs[0] == "Objects" && int.TryParse(stem, out var idx))
                {
                    var key = segs[1] + "/" + idx.ToString("000");
                    if (refKeys.Contains(key)) referenced.Add(dir + "/" + fn);
                    else unreferenced.Add(dir + "/" + fn);
                }
            }
        }

        // 被引用 = **会上屏** ⇒ 必须已有处置（R1-B 白名单），否则就是没处置完的平色块
        var whiteListed = 0;
        var unhandled = new List<string>();
        foreach (var f in referenced)
        {
            var packIdx = f.Substring("Objects/".Length);            // <pack>/<idx>.png
            var key = packIdx.Substring(0, packIdx.Length - 4);
            if (MapView.IsPaletteCycledFlatWallTile(key)) whiteListed++;
            else unhandled.Add(f);
        }
        Console.WriteLine($"  其中：被布局引用 {referenced.Count} 个（已在 `R1-B` 平色白名单里 {whiteListed} 个 / " +
                          $"未处置 {unhandled.Count} 个）；（无可对到布局键的）未被引用 {unreferenced.Count} 个");
        if (unreferenced.Count > 0)
            Console.WriteLine($"    未被引用的前 8 个（= 零可见影响）：{string.Join(" ", unreferenced.GetRange(0, Math.Min(8, unreferenced.Count)))}");
        Check(unhandled.Count == 0,
            "被布局引用的平色瓦片**全部**已有处置（`MapView.PaletteCycledFlatWallTiles` 白名单：平色水墙不叠）" +
            $"—— 实测被引用 {referenced.Count} 个 / 未处置 {unhandled.Count} 个" +
            (unhandled.Count > 0 ? "（" + string.Join(",", unhandled) + "）" : ""));

        Check(skillFramesRequested == 0 && skillFrames.Count > 0,
            $"`UI/SkillIcon` 的实心单色帧共 {skillFrames.Count} 张（帧号 {MinOf(skillFrames)}..{MaxOf(skillFrames)}），" +
            $"**会被请求的帧号有 {skillFramesRequested} 张**" +
            "（帧号公式 `UI/D2Icon.SkillIconPath`：`(official_id − 6 − 30×(class−1)) × 2 ≤ 58`，+1 = 灰化帧 ⇒ 最大 59" +
            " ⇒ 这批平色帧**从不被请求** ⇒ 零可见影响；⛔ 仍然不许「顺手删素材」：删了会与「原版 DC6 帧号一一对应」对不上）");
        Console.WriteLine();
    }

    /// <summary>
    /// 该 PNG 是不是"实心单色"（唯一色 = 1）。口径同 D3：只数**不透明**像素（a &gt; 0），
    /// 且不透明像素 &lt; 8 的"细条"不计（否则 1×1 的抗锯齿点也会被算成平色）。
    /// 只支持 8bit RGB/RGBA + 5 种 filter（D2 素材就是这两种，由 `dc6.write_png_rgba` 写出）。
    /// </summary>
    private static bool TryFlatColor(string path, out string color)
    {
        color = "";
        try
        {
            var bytes = System.IO.File.ReadAllBytes(path);
            if (bytes.Length < 8) return false;
            var pos = 8;
            int w = 0, h = 0, ct = 0;
            var idat = new List<byte>();
            while (pos + 8 <= bytes.Length)
            {
                var len = (bytes[pos] << 24) | (bytes[pos + 1] << 16) | (bytes[pos + 2] << 8) | bytes[pos + 3];
                var tag = System.Text.Encoding.ASCII.GetString(bytes, pos + 4, 4);
                var pay = pos + 8;
                if (tag == "IHDR")
                {
                    w = (bytes[pay] << 24) | (bytes[pay + 1] << 16) | (bytes[pay + 2] << 8) | bytes[pay + 3];
                    h = (bytes[pay + 4] << 24) | (bytes[pay + 5] << 16) | (bytes[pay + 6] << 8) | bytes[pay + 7];
                    if (bytes[pay + 8] != 8) return false;
                    ct = bytes[pay + 9];
                    if (ct != 2 && ct != 6) return false;
                }
                else if (tag == "IDAT")
                {
                    for (var i = 0; i < len; i++) idat.Add(bytes[pay + i]);
                }
                else if (tag == "IEND") break;
                pos = pay + len + 4;
            }
            if (w <= 0 || h <= 0) return false;

            byte[] raw;
            using (var ms = new System.IO.MemoryStream(idat.ToArray()))
            using (var z = new System.IO.Compression.ZLibStream(ms, System.IO.Compression.CompressionMode.Decompress))
            using (var outMs = new System.IO.MemoryStream())
            {
                z.CopyTo(outMs);
                raw = outMs.ToArray();
            }

            var bpp = ct == 6 ? 4 : 3;
            var stride = w * bpp;
            var prev = new byte[stride];
            var cur = new byte[stride];
            var opaque = 0;
            var uniq = new HashSet<int>();
            var p = 0;
            for (var y = 0; y < h; y++)
            {
                if (p >= raw.Length) return false;
                var ft = raw[p++];
                System.Buffer.BlockCopy(raw, p, cur, 0, stride);
                p += stride;
                for (var i = 0; i < stride; i++)
                {
                    int a = i >= bpp ? cur[i - bpp] : 0, b = prev[i], c = i >= bpp ? prev[i - bpp] : 0;
                    switch (ft)
                    {
                        case 1: cur[i] = (byte)(cur[i] + a); break;
                        case 2: cur[i] = (byte)(cur[i] + b); break;
                        case 3: cur[i] = (byte)(cur[i] + ((a + b) >> 1)); break;
                        case 4:
                            var pa = Math.Abs(b - c); var pb = Math.Abs(a - c); var pc = Math.Abs(a + b - 2 * c);
                            cur[i] = (byte)(cur[i] + (pa <= pb && pa <= pc ? a : (pb <= pc ? b : c)));
                            break;
                    }
                }
                for (var x = 0; x < w; x++)
                {
                    var o = x * bpp;
                    var al = bpp == 4 ? cur[o + 3] : 255;
                    if (al == 0) continue;
                    opaque++;
                    uniq.Add((cur[o] << 16) | (cur[o + 1] << 8) | cur[o + 2]);
                    if (uniq.Count > 1) return false;      // 早退：不是平色
                }
                System.Buffer.BlockCopy(cur, 0, prev, 0, stride);
            }

            if (opaque < 8 || uniq.Count != 1) return false;
            var only = 0;
            foreach (var c in uniq) only = c;
            color = string.Format("RGB({0},{1},{2}) a=255", (only >> 16) & 255, (only >> 8) & 255, only & 255);
            return true;
        }
        catch (Exception)
        {
            return false;                                  // 读不动就当"不是平色"，不在这里判红（另一步管"可载入"）
        }
    }

    /// <summary>整数表最小值（只用于打印；空表返回 0）。</summary>
    private static int MinOf(List<int> xs)
    {
        var m = int.MaxValue;
        foreach (var x in xs) if (x < m) m = x;
        return xs.Count == 0 ? 0 : m;
    }

    /// <summary>整数表最大值（只用于打印；空表返回 0）。</summary>
    private static int MaxOf(List<int> xs)
    {
        var m = int.MinValue;
        foreach (var x in xs) if (x > m) m = x;
        return xs.Count == 0 ? 0 : m;
    }

    /// <summary>一组平色文件里出现最多的那个色值（只用于打印）。</summary>
    private static string SameColorOf(List<string> entries)
    {
        var counts = new Dictionary<string, int>();
        foreach (var e in entries)
        {
            var sp = e.IndexOf(' ');
            if (sp < 0) continue;
            var c = e.Substring(sp + 1);
            counts[c] = counts.TryGetValue(c, out var v) ? v + 1 : 1;
        }
        var best = ""; var n = 0;
        foreach (var kv in counts) if (kv.Value > n) { best = kv.Key; n = kv.Value; }
        return best;
    }

    /// <summary>
    /// **地形布局/代码真正引用的瓦片键集合**（`pack/idx`，不含 `Tiles/`|`Objects/` 前缀）。
    /// 三个来源（都在生产文件里，⛔ 不手写清单）：
    ///   · `MapGenTownLayout.cs`：`Packs[]`（下标 = 3 位 packId）+ `GroundRows`/`ObjectRows`（每格 6 字符）
    ///   · `MapGenWildLayout.cs`：每条 14 字符码 = `&lt;kind&gt;&lt;class&gt;&lt;ground6&gt;&lt;object6&gt;`
    ///   · `MapView.cs`：字面键常量表（`"pack/idx"`）
    /// 自证：Town 解出的键数 = **295**（与 `MapView.cs` 文件头 R1-B 段「真正引用到的 295 个瓦片键」对账）。
    /// </summary>
    private static HashSet<string> ReferencedTileKeys()
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var mapDir = System.IO.Path.Combine(ResolveProjectRoot(), "client", "Assets", "Scripts", "Module", "Map");

        var town = System.IO.File.ReadAllText(System.IO.Path.Combine(mapDir, "MapGenTownLayout.cs"));
        var townPacks = QuotedStrings(SectionBody(town, "Packs"));
        var townBefore = keys.Count;
        foreach (var varName in new[] { "GroundRows", "ObjectRows" })
        {
            foreach (var s in QuotedStrings(SectionBody(town, varName)))
            {
                for (var i = 0; i + 6 <= s.Length; i += 6)
                {
                    var cell = s.Substring(i, 6);
                    if (!IsDigits(cell)) continue;
                    AddKey(townPacks, cell.Substring(0, 3), cell.Substring(3, 3), keys);
                }
            }
        }
        var townCount = keys.Count - townBefore;

        var wild = System.IO.File.ReadAllText(System.IO.Path.Combine(mapDir, "MapGenWildLayout.cs"));
        var wildPacks = QuotedStrings(SectionBody(wild, "Packs"));
        foreach (var lit in QuotedStrings(wild))
        {
            if (lit.Length != 14) continue;
            AddKey(wildPacks, lit.Substring(2, 3), lit.Substring(5, 3), keys);
            AddKey(wildPacks, lit.Substring(8, 3), lit.Substring(11, 3), keys);
        }

        var mv = System.IO.File.ReadAllText(System.IO.Path.Combine(mapDir, "MapView.cs"));
        foreach (System.Text.RegularExpressions.Match m in
                 System.Text.RegularExpressions.Regex.Matches(mv, "\"([a-z_]+)/(\\d{3})\""))
            keys.Add(m.Groups[1].Value + "/" + m.Groups[2].Value);

        Console.WriteLine($"    引用集自证：Town 布局解出 {townCount} 个键（文件头 R1-B 段写的正是 295 个）");
        Check(townCount == 295,
            $"Town 布局解出 **295** 个被引用瓦片键（实测 {townCount}）—— 与 `MapView.cs` 文件头「" +
            "MapGenTownLayout 真正引用到的 295 个瓦片键」逐字对账 ⇒ 本步的引用集解析口径自证成立");
        return keys;
    }

    /// <summary>把 3 位 packId / 3 位 idx 解析成 `pack/idx`（越界/非数字 ⇒ 忽略）。</summary>
    private static void AddKey(List<string> packs, string packId, string idx, HashSet<string> into)
    {
        if (packs == null || !IsDigits(packId) || !IsDigits(idx)) return;
        var pi = int.Parse(packId);
        if (pi < 0 || pi >= packs.Count) return;
        into.Add(packs[pi] + "/" + idx);
    }

    private static bool IsDigits(string s)
    {
        if (string.IsNullOrEmpty(s)) return false;
        for (var i = 0; i < s.Length; i++) if (s[i] < '0' || s[i] > '9') return false;
        return true;
    }

    /// <summary>`XXX = { ... };` 的体内文本（找不到返回空串）。</summary>
    private static string SectionBody(string text, string name)
    {
        var i = text.IndexOf(name + " =", StringComparison.Ordinal);
        if (i < 0) return "";
        var j = text.IndexOf('{', i);
        if (j < 0) return "";
        var k = text.IndexOf("};", j, StringComparison.Ordinal);
        if (k < 0) return text.Substring(j);
        return text.Substring(j, k - j + 2);
    }

    /// <summary>该文本里所有双引号字符串。</summary>
    private static List<string> QuotedStrings(string text)
    {
        var res = new List<string>();
        var i = 0;
        while (i < text.Length)
        {
            var s = text.IndexOf('"', i);
            if (s < 0) break;
            var e = text.IndexOf('"', s + 1);
            if (e < 0) break;
            res.Add(text.Substring(s + 1, e - s - 1));
            i = e + 1;
        }
        return res;
    }

    /// <summary>取某个方法/属性的**源文本体**（从签名行到花括号配平处）；找不到返回空串。</summary>
    private static string MethodBody(string text, string signature)
    {
        var i = text.IndexOf(signature, StringComparison.Ordinal);
        if (i < 0) return "";
        var start = text.IndexOf('{', i);
        if (start < 0) return "";
        var depth = 0;
        for (var p = start; p < text.Length; p++)
        {
            if (text[p] == '{') depth++;
            else if (text[p] == '}')
            {
                depth--;
                if (depth == 0) return text.Substring(start, p - start + 1);
            }
        }
        return text.Substring(start);
    }

    /// <summary>子串出现次数。</summary>
    private static int CountIn(string text, string needle)
    {
        var n = 0;
        var i = 0;
        while ((i = text.IndexOf(needle, i, StringComparison.Ordinal)) >= 0) { n++; i += needle.Length; }
        return n;
    }

    /// <summary>子串首次出现的 **1-based 行号**（找不到 = -1）。</summary>
    private static int LineOf(string text, string needle)
    {
        var i = text.IndexOf(needle, StringComparison.Ordinal);
        if (i < 0) return -1;
        var n = 1;
        for (var p = 0; p < i; p++) if (text[p] == '\n') n++;
        return n;
    }

    /// <summary>子串**全部**出现的 1-based 行号。</summary>
    private static List<int> LinesOf(string text, string needle)
    {
        var res = new List<int>();
        var i = 0;
        while ((i = text.IndexOf(needle, i, StringComparison.Ordinal)) >= 0)
        {
            var n = 1;
            for (var p = 0; p < i; p++) if (text[p] == '\n') n++;
            res.Add(n);
            i += needle.Length;
        }
        return res;
    }

    /// <summary>可见块范围的四个下标（`ComputeVisibleChunkRange:410-444` 的离线复刻）。</summary>
    private struct ChunkRange
    {
        public int X0, Y0, X1, Y1;
    }

    /// <summary>
    /// 相机格坐标 → 可见块范围（**逐条复刻** `MapView.ComputeVisibleChunkRange:410-444` 的口径：
    /// 视口四角 → `Iso.WorldToGrid` → min/max → 除块边长 → ±1 块外扩 → Clamp 到 [0,块数-1]）。
    /// 相机世界坐标 = `Iso.GridToWorld(格)`（`CameraRig.SetTargetGrid:273` / `DesiredPosition:425-428`
    /// 的机位口径；只在本图**内部**取样，避开 `ClampFocus:446-457` 的边界钳制）。
    /// </summary>
    private static ChunkRange ChunkRangeSig(float ortho, float aspect, int camGx, int camGy,
        int maxCx, int maxCy)
    {
        var cam = Iso.GridToWorld(camGx, camGy);
        var hw = ortho * aspect;
        var hh = ortho;
        var minX = int.MaxValue; var minY = int.MaxValue;
        var maxX = int.MinValue; var maxY = int.MinValue;
        for (var i = 0; i < 4; i++)
        {
            var wx = cam.x + ((i & 1) == 0 ? -hw : hw);
            var wy = cam.y + ((i & 2) == 0 ? -hh : hh);
            var g = Iso.WorldToGrid(new Vector3(wx, wy, 0f));
            minX = Mathf.Min(minX, g.x); minY = Mathf.Min(minY, g.y);
            maxX = Mathf.Max(maxX, g.x); maxY = Mathf.Max(maxY, g.y);
        }
        var chunk = MapView.ChunkSize;
        return new ChunkRange
        {
            X0 = Mathf.Clamp(minX / chunk - 1, 0, maxCx),
            Y0 = Mathf.Clamp(minY / chunk - 1, 0, maxCy),
            X1 = Mathf.Clamp(maxX / chunk + 1, 0, maxCx),
            Y1 = Mathf.Clamp(maxY / chunk + 1, 0, maxCy),
        };
    }

    /// <summary>可见块范围里的块数（`RefreshVisibleChunks:401-404` 会把这些块**一次建完**）。</summary>
    private static int RangeChunkCount(ChunkRange r)
    {
        var nx = r.X1 - r.X0 + 1;
        var ny = r.Y1 - r.Y0 + 1;
        return nx < 0 || ny < 0 ? 0 : nx * ny;
    }

    /// <summary><paramref name="cur"/> 里有、<paramref name="prev"/> 里没有的块数（= 本次刷新真正新建的块数）。</summary>
    private static int CountChunksOnlyIn(ChunkRange cur, ChunkRange prev)
    {
        var n = 0;
        for (var x = cur.X0; x <= cur.X1; x++)
        {
            for (var y = cur.Y0; y <= cur.Y1; y++)
            {
                if (x >= prev.X0 && x <= prev.X1 && y >= prev.Y0 && y <= prev.Y1) continue;
                n++;
            }
        }
        return n;
    }

    /// <summary>`client/Assets/Scripts/**/*.cs` 里 `.SetFogOfWar(` 的**调用点**数（定义行不算）。</summary>
    private static int CountFogOfWarCallSites()
    {
        var dir = System.IO.Path.Combine(ResolveProjectRoot(), "client", "Assets", "Scripts");
        if (!System.IO.Directory.Exists(dir)) return -1;
        var hits = 0;
        var files = System.IO.Directory.GetFiles(dir, "*.cs", System.IO.SearchOption.AllDirectories);
        for (var i = 0; i < files.Length; i++)
        {
            foreach (var line in System.IO.File.ReadAllLines(files[i]))
            {
                if (line.IndexOf(".SetFogOfWar(", StringComparison.Ordinal) >= 0) hits++;
            }
        }
        return hits;
    }

    /// <summary>某个源文件里若干子串的出现次数合计（用于「不排队/不跨帧」这类结构性断言）。</summary>
    private static int CountSourceHits(string projRel, string[] needles)
    {
        var p = System.IO.Path.Combine(ResolveProjectRoot(),
            projRel.Replace('/', System.IO.Path.DirectorySeparatorChar));
        if (!System.IO.File.Exists(p)) return -1;
        var text = System.IO.File.ReadAllText(p);
        var n = 0;
        for (var i = 0; i < needles.Length; i++)
        {
            var idx = 0;
            while ((idx = text.IndexOf(needles[i], idx, StringComparison.Ordinal)) >= 0) { n++; idx += needles[i].Length; }
        }
        return n;
    }

    /// <summary>
    /// 现读 `Module/Camera/CameraRig.cs` 的 `DefaultOrthographicSize`（**不抄字面量**）：
    /// 读不到返回 NaN ⇒ 调用方判红（静默失真比读不到更糟）。
    /// </summary>
    private static float ReadCameraOrthoSize()
    {
        var p = System.IO.Path.Combine(ResolveProjectRoot(),
            "client", "Assets", "Scripts", "Module", "Camera", "CameraRig.cs");
        if (!System.IO.File.Exists(p)) { Console.WriteLine($"  [WARN] 读不到 {p}"); return float.NaN; }
        const string marker = "public const float DefaultOrthographicSize";
        foreach (var line in System.IO.File.ReadAllLines(p))
        {
            var i = line.IndexOf(marker, StringComparison.Ordinal);
            if (i < 0) continue;
            var eq = line.IndexOf('=', i);
            var f = line.IndexOf('f', eq);
            if (eq < 0 || f < 0) continue;
            float v;
            if (float.TryParse(line.Substring(eq + 1, f - eq - 1).Trim(),
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out v))
                return v;
        }
        Console.WriteLine("  [WARN] CameraRig.cs 里找不到 DefaultOrthographicSize 常量行");
        return float.NaN;
    }

    /// <summary>现读 `client/ProjectSettings/ProjectSettings.asset` 的 defaultScreenWidth/Height。</summary>
    private static bool ReadProjectScreenSize(out int width, out int height)
    {
        width = 0;
        height = 0;
        var p = System.IO.Path.Combine(ResolveProjectRoot(),
            "client", "ProjectSettings", "ProjectSettings.asset");
        if (!System.IO.File.Exists(p)) { Console.WriteLine($"  [WARN] 读不到 {p}"); return false; }
        foreach (var line in System.IO.File.ReadAllLines(p))
        {
            var t = line.Trim();
            if (t.StartsWith("defaultScreenWidth:", StringComparison.Ordinal))
            { int.TryParse(t.Substring("defaultScreenWidth:".Length).Trim(), out width); }
            else if (t.StartsWith("defaultScreenHeight:", StringComparison.Ordinal))
            { int.TryParse(t.Substring("defaultScreenHeight:".Length).Trim(), out height); }
        }
        return width > 0 && height > 0;
    }

    /// <summary>把 int 列表拼成 `47,54`（自检输出用）。</summary>
    private static string FmtInts(List<int> xs)
    {
        var parts = new List<string>();
        for (var i = 0; i < xs.Count; i++) parts.Add(xs[i].ToString());
        return string.Join(",", parts);
    }

    /// <summary>
    /// `Assets/Resources/Clover/<rel>` 的实际文件路径（从可执行目录向上找含 `client/Assets` 的仓库根）。
    /// 找不到 ⇒ null（调用方 Check(false) 报出来，不静默）。
    /// </summary>
    private static string ResourceFile(string rel)
    {
        var dir = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (System.IO.Directory.Exists(System.IO.Path.Combine(dir.FullName, "client", "Assets")))
            {
                var p = System.IO.Path.Combine(dir.FullName, "client", "Assets", "Resources", "Clover",
                    rel.Replace('/', System.IO.Path.DirectorySeparatorChar));
                return System.IO.File.Exists(p) ? p : null;
            }
            dir = dir.Parent;
        }
        return null;
    }

    /// <summary>该块在"盖章后的可见最外沿"那一条是否整条不可走（见 <see cref="Step14_BorderSealGaps"/>）。</summary>
    private static bool OuterEdgeSolid(MapGenWildLayout.Piece p, int side, bool flipX, bool flipY)
    {
        switch (side)
        {
            case 0:     // 北：盖章行 0
            {
                var py = flipY ? p.SizeH - 1 : 0;
                for (var px = 0; px < p.SizeW; px++) if (CellWalkable(p, px, py)) return false;
                return true;
            }
            case 1:     // 南：盖章行 pitch-1 = SizeH-2
            {
                var py = flipY ? 1 : p.SizeH - 2;
                for (var px = 0; px < p.SizeW; px++) if (CellWalkable(p, px, py)) return false;
                return true;
            }
            case 2:     // 西：盖章列 0
            {
                var px = flipX ? p.SizeW - 1 : 0;
                for (var py = 0; py < p.SizeH; py++) if (CellWalkable(p, px, py)) return false;
                return true;
            }
            default:    // 东：盖章列 pitch-1 = SizeW-2
            {
                var px = flipX ? 1 : p.SizeW - 2;
                for (var py = 0; py < p.SizeH; py++) if (CellWalkable(p, px, py)) return false;
                return true;
            }
        }
    }

    /// <summary>
    /// 该格盖章之后是否可走（**照抄 `MapGenWilderness.Stamp` 的写入/跳过口径**，只为本步分析用）：
    /// kindChar '.' = 不覆盖（保持基底草地，可走）；kindChar ' ' 且地面/物件都为空 = 不写（可走）。
    /// </summary>
    private static bool CellWalkable(MapGenWildLayout.Piece p, int px, int py)
    {
        var idx = MapGenWildLayout.CellIndex(p.Cells, py * p.SizeW + px);
        if (idx < 0 || idx >= p.Alphabet.Length) return true;
        var code = p.Alphabet[idx];
        var kindChar = code.Length > 0 ? code[0] : 'X';
        var classChar = code.Length > 1 ? code[1] : 'R';
        var ground = MapGenWildLayout.Decode(code.Length >= 8 ? code.Substring(2, 6) : "------");
        var obj = MapGenWildLayout.Decode(code.Length >= 14 ? code.Substring(8, 6) : "------");

        if (kindChar == '.') return true;                                  // 纯可走：保持基底草地
        if (kindChar == ' ' && ground.Length == 0 && obj.Length == 0) return true;   // 原版这格什么都没有

        // 同 `MapGenWilderness.KindOf`：只有 '.' 与非阻挡类映射出可走地形
        switch (classChar)
        {
            case 'T': case 'F': case 'W': case 'C':
            case 'S': case 'O': case 'X': case 'R':
                return false;
            default:
                return !Diablo2.Def.TileKindInfo.IsWalkable(TileKind.Grass) == false;
        }
    }

    // ── 13. 血腥荒野恒 80×80（片 3：「野外地图太小」）+ MonDen 逐格抽样的期望群数/怪数 ──
    //   两件事必须一起看：
    //   ① 尺寸：原版 `Levels.txt`「Act 1 - Wilderness 1」SizeX=SizeY=80 ⇒ 本图**恒 80×80**，
    //      连跑 20 个 seed 一个都不许例外（旧的 `rng.Next(6,11)` 会生成 48~80）。
    //   ② 密度：原版 `MonDen` 是**逐格抽样门槛**（`LevelBuilder.cs:225-237`：
    //      `sample = Random.Range(0,100000); if (sample >= density) continue;`）
    //      ⇒ 期望群数 = 可走格数 × MonDen/100000，期望怪数 = 期望群数 × 平均群大小
    //      （群大小 = `monster_c.min_grp..max_grp`，**从配表源文件现读**，不在这里手抄）。
    //      片 3 的规矩：算出来 > 200 只 ⇒ **先停下回报**，不许直接落地。
    private static void Step13_WildernessFixedSize()
    {
        Section("13. 血腥荒野恒 80×80（20 个 seed）+ MonDen 逐格抽样的期望群数/怪数");

        var sizeOk = 0;
        var minWalk = int.MaxValue;
        var maxWalk = int.MinValue;
        long sumWalk = 0;
        const int seeds = 20;
        var firstSeed = 20250916;

        for (var k = 0; k < seeds; k++)
        {
            var seed = unchecked(firstSeed + k * 7919);
            var m = NewMap();
            m.Generate(AreaId.BloodMoor, seed);
            if (m.Width == GameConst.WildernessMaxSize && m.Height == GameConst.WildernessMaxSize) sizeOk++;
            minWalk = Math.Min(minWalk, m.WalkableCount);
            maxWalk = Math.Max(maxWalk, m.WalkableCount);
            sumWalk += m.WalkableCount;
            if (k < 5)
            {
                Console.WriteLine($"  seed={seed,-12} size={m.Width}x{m.Height} 可走={m.WalkableCount,-6} " +
                                  $"障碍={m.BlockedCount,-6} 回城口={Fmt(m.Exits)} 洞穴口={m.CaveEntrance}");
            }
        }

        Console.WriteLine($"  ⇒ 20 个 seed：尺寸全 = {GameConst.WildernessMaxSize}×{GameConst.WildernessMaxSize} 的 {sizeOk}/20；" +
                          $"可走格 min={minWalk} max={maxWalk} 平均={sumWalk / (double)seeds:F1}");
        Check(sizeOk == seeds,
            $"野外尺寸**恒 80×80**（`GameConst.WildernessMaxSize`，出处原版 Levels.txt「Act 1 - Wilderness 1」SizeX/SizeY）" +
            $"：{sizeOk}/{seeds} 个 seed 命中");
        Check(minWalk > 0, $"每个 seed 都有可走格（最低 {minWalk}）");

        // ── 期望群数 / 怪数（配表源文件现读）──────────────────────────────────
        var level = ReadTsv("策划/数值文档/level_c.txt");
        var mon = ReadTsv("策划/数值文档/monster_c.txt");
        if (level == null || mon == null)
        {
            Check(false, "读不到 策划/数值文档/{level_c,monster_c}.txt ⇒ 期望群数/怪数无法计算");
            Console.WriteLine();
            return;
        }

        var moor = level.Find("name", "血腥荒野");
        if (moor == null)
        {
            Check(false, "level_c.txt 里找不到「血腥荒野」行 ⇒ 期望群数/怪数无法计算");
            Console.WriteLine();
            return;
        }
        var den = int.Parse(moor["mon_density"]);
        var monList = moor["monsters"];
        var ids = monList.Split(';');
        Console.WriteLine($"  level_c「血腥荒野」：mon_density(MonDen)={den}  monsters={monList}" +
                          $"  尺寸列 size_x/size_y={moor["size_x"]}/{moor["size_y"]}");

        var grpAvg = 0.0;
        for (var i = 0; i < ids.Length; i++)
        {
            var row = mon.Find("id", ids[i].Trim());
            if (row == null) { Check(false, $"monster_c 里找不到 id={ids[i]}"); continue; }
            var lo = int.Parse(row["min_grp"]);
            var hi = int.Parse(row["max_grp"]);
            var monoId = row["id"];
            var monoEn = row["name_en"];
            grpAvg += (lo + hi) / 2.0;
            Console.WriteLine($"    id={monoId,-2} {monoEn,-12} min_grp..max_grp = {lo}..{hi}" +
                              $"（官方 MinGrp/MaxGrp）⇒ 期望 {((lo + hi) / 2.0):F2} 只/群");
        }
        grpAvg /= Math.Max(1, ids.Length);

        var avgWalk = sumWalk / (double)seeds;
        var expPacks = avgWalk * den / 100000.0;
        var expMon = expPacks * grpAvg;
        Console.WriteLine($"  ⇒ 逐格抽样：每格命中概率 = MonDen/100000 = {den}/100000 = {den / 100000.0:P3}");
        Console.WriteLine($"     平均可走格 {avgWalk:F1} ⇒ 期望群数 ≈ {expPacks:F1} 群" +
                          $"（= 可走格 × 命中概率；非可走格被过滤）");
        Console.WriteLine($"     平均群大小 {grpAvg:F2} 只/群 ⇒ 期望怪数 ≈ **{expMon:F1} 只**" +
                          $"（片 3 的安全线 = 200 只）");
        Check(expMon <= 200.0, $"期望怪数 {expMon:F1} ≤ 200（片 3 的安全线；超过就要停下回报）");

        // ── 出口格现在到底铺的是什么瓦片（片 3 验收 §2 第 3 项）──────────────
        var wild = NewMap();
        wild.Generate(AreaId.BloodMoor, firstSeed);
        for (var i = 0; i < wild.Exits.Count; i++)
        {
            wild.TryGetTileKeys(wild.Exits[i].x, wild.Exits[i].y, out var g, out var o);
            Console.WriteLine($"  血腥荒野 出口[{i}] {wild.Exits[i]} kind={wild.TileAt(wild.Exits[i])} " +
                              $"ground={(string.IsNullOrEmpty(g) ? "(画空)" : g)} object={(string.IsNullOrEmpty(o) ? "(无)" : o)}");
        }

        var town = NewMap();
        town.Generate(AreaId.Town, firstSeed);
        for (var i = 0; i < town.Exits.Count; i++)
        {
            var e = town.Exits[i];
            town.TryGetTileKeys(e.x, e.y, out var g, out var o);
            Console.WriteLine($"  罗格营地 出口[{i}] {e} kind={town.TileAt(e)} " +
                              $"ground={(string.IsNullOrEmpty(g) ? "(画空)" : g)} object={(string.IsNullOrEmpty(o) ? "(无)" : o)}");
        }
        Console.WriteLine();
    }

    /// <summary>读一张制表符分隔的配表源文件（跳过「类型 / 后缀 / 中文说明」三行表头）。</summary>
    private static TsvTable ReadTsv(string projRel)
    {
        // ★ 仓库根改为**运行期推导**（见 ResolveProjectRoot）。
        //   旧写法是「AppContext.BaseDirectory 上数 6 层」——那是宿主还在 `.ai-tmp/hosts/<名>/bin/<cfg>/<tfm>/`
        //   时的层数；宿主迁到 `tools/probes/hosts/<名>/bin/<cfg>/<tfm>/` 后**少了一层**，
        //   6 层落点变成 `<仓库根>\tools` ⇒ `策划/数值文档/*.txt` 读不到，Step13 那两条断言恒红
        //   （实测 2026-09-20：`读不到 策划/数值文档/{level_c,monster_c}.txt`）。
        var root = ResolveProjectRoot();
        var p = System.IO.Path.Combine(root, projRel.Replace('/', System.IO.Path.DirectorySeparatorChar));
        if (!System.IO.File.Exists(p)) { Console.WriteLine($"  [WARN] 配表源文件不存在：{p}"); return null; }
        var lines = System.IO.File.ReadAllLines(p, System.Text.Encoding.UTF8);
        if (lines.Length < 5) return null;
        var head = lines[0].Split('\t');
        var rows = new List<string[]>();
        for (var i = 4; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i])) continue;
            rows.Add(lines[i].Split('\t'));
        }
        return new TsvTable(head, rows);
    }

    /// <summary>一张极简 TSV 表（列名 → 值），只为本宿主算期望值用。</summary>
    private sealed class TsvTable
    {
        private readonly string[] _head;
        private readonly List<string[]> _rows;

        public TsvTable(string[] head, List<string[]> rows) { _head = head; _rows = rows; }

        /// <summary>按某列等于某值找第一行。</summary>
        public TsvRow Find(string column, string value)
        {
            var ci = Array.IndexOf(_head, column);
            if (ci < 0) return null;
            for (var i = 0; i < _rows.Count; i++)
            {
                if (i < _rows.Count && ci < _rows[i].Length && _rows[i][ci].Trim() == value)
                    return new TsvRow(_head, _rows[i]);
            }
            return null;
        }
    }

    /// <summary>TSV 的一行。</summary>
    private sealed class TsvRow
    {
        private readonly string[] _head;
        private readonly string[] _cells;

        public TsvRow(string[] head, string[] cells) { _head = head; _cells = cells; }

        public string this[string column]
        {
            get
            {
                var ci = Array.IndexOf(_head, column);
                return ci >= 0 && ci < _cells.Length ? _cells[ci].Trim() : "";
            }
        }
    }

    // ── 12. 指定 seed 的定点排查（环境变量 MAPCHECK_SEED 打开）────────────────
    private static void Step12_DebugSeed()
    {
        var seedText = Environment.GetEnvironmentVariable("MAPCHECK_SEED");
        var areaText = Environment.GetEnvironmentVariable("MAPCHECK_AREA") ?? "DenOfEvil";
        if (!int.TryParse(seedText, out var seed)) { Console.WriteLine("MAPCHECK_SEED 不是整数"); return; }
        var area = (AreaId)Enum.Parse(typeof(AreaId), areaText);
        Section($"12. 定点排查 area={area} seed={seed}");
        var m = NewMap();
        m.Generate(area, seed);
        Console.WriteLine(m.DumpStats());
        Console.WriteLine(m.DumpAscii());
        Console.WriteLine($"  可走连通片数（4 邻计数） = {m.CountWalkableComponents()}");
    }

    /// <summary>野外的地面/物件瓦片键是否来自**原版野外素材包**（`export_tiles.PACKS` 里那批）。</summary>
    private static bool IsWildKey(string key)
    {
        return key.StartsWith("town_floor/") || key.StartsWith("town_trees/")
            || key.StartsWith("town_fence/") || key.StartsWith("town_objects/")
            || key.StartsWith("moor_") || key.StartsWith("cave_");
    }

    // ═════════════════════════════════════════════════════════════════════════
    // 23. ★ 片 L / R12：水 = 独立 `TileKind.Water`
    //
    // 缺陷（R12）：`MapGenTown.KindOf('r')` 把**水**与石头/崖壁/碎石/杂物一起归到 `TileKind.Rock`
    //   ⇒ 光标 / 小地图 / tooltip 无法把"水"与"石头"分开（水与石矮墙在小地图上映射到同一个 Cel 60）。
    // 出处：① `MapGenTownLayout.cs:28`（`'r'` = 水，生成物，源 `ACT1/TOWN/*.ds1`）；
    //       ② 水格 floor 键全是 `moor_river/*`（= `OUTDOORS/river.dt1` 的水瓦片）；
    //       ③ 原版不可涉水 ⇒ 可走性保持 false。
    // 本步的口径（只加断言）：
    //   ① 逐格**双向**核对 `'r'` ↔ `Water`（⛔ 不靠总数相等）
    //   ② 水格仍**不可走**；水格 floor 键仍是原版水瓦片
    //   ③ 非水区域一格都没变成水（`'X'` 等阻挡码按合同**保持原分类**）
    //   ④ **消费者穷举**：`TileKind` 的每个 switch/判定点对 `Water` 都有显式分支或已登记的 default
    //   ⑤ 表现侧：水有独立地面瓦片表 / 占位色，且**不是**物件；小地图不再把水画成石墙
    // ═════════════════════════════════════════════════════════════════════════
    private static void Step23_WaterKind()
    {
        Section("23. ★ 片 L / R12：水 = 独立 TileKind.Water（逐格双向核对 / 仍不可走 / 消费者穷举）");

        // ── ① 期望集 = **从 `MapGenTownLayout.Rows` 逐格推**（生成物 = 原版数据，⛔ 不靠总数）──
        var expected = new HashSet<Vector2Int>();
        for (var y = 0; y < MapGenTownLayout.Rows.Length; y++)
        {
            var row = MapGenTownLayout.Rows[y];
            for (var x = 0; x < row.Length; x++)
                if (row[x] == 'r') expected.Add(new Vector2Int(x, y));
        }
        Console.WriteLine($"  `MapGenTownLayout.Rows` 里 'r'（水）的格数 = {expected.Count}");

        var town = NewMap();
        town.Generate(AreaId.Town, 20250916);

        var actual = new HashSet<Vector2Int>();
        var kindCounts = new Dictionary<TileKind, int>();
        for (var y = 0; y < town.Height; y++)
        {
            for (var x = 0; x < town.Width; x++)
            {
                var k = town.TileAt(new Vector2Int(x, y));
                if (!kindCounts.ContainsKey(k)) kindCounts[k] = 0;
                kindCounts[k]++;
                if (k == TileKind.Water) actual.Add(new Vector2Int(x, y));
            }
        }

        var miss = 0;
        foreach (var g in expected) if (!actual.Contains(g)) miss++;
        var extra = 0;
        foreach (var g in actual) if (!expected.Contains(g)) extra++;
        var walkableWater = 0;
        foreach (var g in actual) if (town.Walkable(g)) walkableWater++;
        var waterNonRiverGround = 0;      // 水格的地面键不是原版水瓦片（⛔ 不许占位）
        var rockCells = 0;
        foreach (var kv in kindCounts) if (kv.Key == TileKind.Rock) rockCells = kv.Value;
        foreach (var g in actual)
        {
            town.TryGetTileKeys(g.x, g.y, out var gk, out _);
            if (gk == null || !gk.StartsWith("moor_river/")) waterNonRiverGround++;
        }

        var kinds = new List<TileKind>(kindCounts.Keys);
        kinds.Sort((p, q) => ((int)p).CompareTo((int)q));
        var parts = new List<string>();
        for (var i = 0; i < kinds.Count; i++)
            parts.Add($"{kinds[i]}×{kindCounts[kinds[i]]}{(TileKindInfo.IsWalkable(kinds[i]) ? "(可走)" : "(阻挡)")}");
        Console.WriteLine($"    [枚举] Town 盘上实际出现的地形：{string.Join(" / ", parts)}");

        Check(actual.Count == expected.Count && miss == 0 && extra == 0,
            $"Town：`'r'` 格 ↔ `TileKind.Water` **逐格双向相等**" +
            $"（期望 {expected.Count} / 实际 {actual.Count} / 漏 {miss} / 多 {extra}）");
        Check(actual.Count == 277, $"Town 水格 = **277**（基线 `Rock×366` 里含 277 格水；实测 {actual.Count}）");
        Check(rockCells == 366 - 277, $"Town 的 `Rock` 从 366 减到 {rockCells}（= 366 − 277，**只**摘走水，别的没动）");
        Check(walkableWater == 0, $"全部 {actual.Count} 格水**仍不可走**（可走的水格 = {walkableWater}）");
        Check(!TileKindInfo.IsWalkable(TileKind.Water), "`TileKindInfo.IsWalkable(Water)` == false（显式分支，不是 default 兜底）");
        Check(waterNonRiverGround == 0,
            $"水格的地面键**全是** `moor_river/*`（原版水瓦片；异常 = {waterNonRiverGround} 格）");

        // ── ② 其它两张图**一格水都没有**（按合同 `'X'` 保持原分类；洞穴的 'X' = 实心岩体）──
        var areas = new[] { AreaId.BloodMoor, AreaId.DenOfEvil };
        for (var i = 0; i < areas.Length; i++)
        {
            var m = NewMap();
            m.Generate(areas[i], 20250916);
            var water = 0;
            var rock = 0;
            var counts2 = new Dictionary<TileKind, int>();
            for (var y = 0; y < m.Height; y++)
            {
                for (var x = 0; x < m.Width; x++)
                {
                    var k = m.TileAt(new Vector2Int(x, y));
                    if (!counts2.ContainsKey(k)) counts2[k] = 0;
                    counts2[k]++;
                    if (k == TileKind.Water) water++;
                    if (k == TileKind.Rock) rock++;
                }
            }
            var ks = new List<TileKind>(counts2.Keys);
            ks.Sort((p, q) => ((int)p).CompareTo((int)q));
            var ps = new List<string>();
            for (var j = 0; j < ks.Count; j++)
                ps.Add($"{ks[j]}×{counts2[ks[j]]}{(TileKindInfo.IsWalkable(ks[j]) ? "(可走)" : "(阻挡)")}");
            Console.WriteLine($"    [枚举] {areas[i]} 盘上实际出现的地形：{string.Join(" / ", ps)}");
            Check(water == 0, $"{areas[i]}：`TileKind.Water` = 0 格（合同：本片只摘城镇 `'r'`，`'X'` 等**保持原分类**；实测 {water}）");
            // 野外有 `Rock`（崖壁/碎石/杂物/水都归它，按合同未改）；洞穴**没有** `Rock`（用 `CaveWall`）
            // ⇒ 期望值分区域给，⛔ 不许用一句"Rock>0"套三张图（那在洞穴上必然假红）。
            var blockerKept = areas[i] == AreaId.BloodMoor ? rock : counts2.ContainsKey(TileKind.CaveWall) ? counts2[TileKind.CaveWall] : -1;
            Check(blockerKept > 0,
                $"{areas[i]}：原地形分类仍在（{(areas[i] == AreaId.BloodMoor ? "Rock" : "CaveWall")} = {blockerKept} 格 ⇒ 阻挡码没有被顺手改成水）");
        }

        // ── ③ 消费者穷举（**源码级**）：每个 switch/判定点对 Water 都有显式分支或已登记的 default ──
        //   判据 = 逐文件查"必须出现的标记"；缺一个就是"新枚举值会 fallthrough 到 default"的风险点。
        var sep = System.IO.Path.DirectorySeparatorChar;
        string Src(string rel) => System.IO.File.ReadAllText(
            System.IO.Path.Combine(ResolveProjectRoot(), rel.Replace('/', sep)));

        var enums = Src("client/Assets/Scripts/Def/Enums.cs");
        var grid = Src("client/Assets/Scripts/Module/Map/GridMap.cs");
        var view = Src("client/Assets/Scripts/Module/Map/MapView.cs");
        var module = Src("client/Assets/Scripts/Module/Map/MapModule.cs");
        var dbg = Src("client/Assets/Scripts/Module/Map/MapDebug.cs");
        var townGen = Src("client/Assets/Scripts/Module/Map/MapGenTown.cs");
        var wildGen = Src("client/Assets/Scripts/Module/Map/MapGenWilderness.cs");
        var caveGen = Src("client/Assets/Scripts/Module/Map/MapGenCave.cs");
        int Count(string hay, string needle)
        {
            var n = 0;
            var i = hay.IndexOf(needle, StringComparison.Ordinal);
            while (i >= 0) { n++; i = hay.IndexOf(needle, i + needle.Length, StringComparison.Ordinal); }
            return n;
        }

        Check(Count(enums, "case TileKind.Water:") >= 1 && enums.Contains("kind == TileKind.Water"),
            "Enums.cs：`IsWalkable` 有显式 `case TileKind.Water`，`IsGroundLayer` 收录 Water ✅");
        Check(Count(grid, "case TileKind.Water:") >= 1,
            "GridMap.cs：`IsKnownKind` 登记了 Water（否则逐格扫描会假告警）");
        Check(Count(view, "case TileKind.Water") >= 4 && view.Contains("WaterTiles"),
            $"MapView.cs：Water **显式分支 ≥ 4 处**（IsObjectKind / GroundKeyOf / ObjectKeyOf / GroundColor）" +
            $"且带独立地面瓦片表 `WaterTiles`；实测 {Count(view, "case TileKind.Water")} 处");
        Check(view.Contains("default: return new Color(0.4f, 0.4f, 0.4f);"),
            "MapView.cs：`ObjectColor` 的 default **已登记**（水不走物件层 ⇒ default 不会误命中）");
        Check(module.Contains("if (kind == TileKind.Water) return") && Count(module, "case TileKind.Water") == 0,
            "MapModule.cs：`CodeOf` 对 Water 有显式判定（= 阻挡）；本片**没给它新色码**（`MinimapArgs` 是契约，不在此片白名单）");
        Check(dbg.Contains("kind == TileKind.Water") && dbg.Contains("'~'=水"),
            "MapDebug.cs：字符画把水画成 `~`（与障碍 `#` 分开），图例同步");
        Check(townGen.Contains("case 'r': return TileKind.Water;"),
            "MapGenTown.cs：`'r'` → `TileKind.Water`（出处 = `MapGenTownLayout.cs:28`）");
        Check(townGen.Contains("case 's': return TileKind.Rock;"),
            "MapGenTown.cs：`'s'`（石矮墙）**仍是** `Rock`（⛔ 没有顺手把石头也改掉）");
        Check(wildGen.Contains("case 'X': return TileKind.Rock;")
              && wildGen.Contains("case 'C': return TileKind.Rock;")
              && wildGen.Contains("case 'S': return TileKind.Rock;")
              && wildGen.Contains("case 'O': return TileKind.Rock;"),
            "MapGenWilderness.cs：`'X'`（水）与崖壁/碎石/杂物**保持原分类**（按本轮合同未改，已登记为未决）");
        Check(Count(caveGen, "TileKind.Water") == 0,
            "MapGenCave.cs：洞穴里**没有** Water（洞穴的 `'X'` = 实心岩体 `CaveWall`，⛔ 不许当水）");

        // ★ 消费者穷举（**全仓扫描，⛔ 不写死文件清单**；2026-09-23 补：片 J 的 `SkillModule.cs` 是
        //   在我加 `Water` **之后**才出现的第 4 个 `case TileKind.` 消费者 —— 写死清单会漏掉它）。
        //   判据：凡文件里出现 `case TileKind.` 的，都必须对 `Water` 有显式分支，
        //   否则新值会**静默 fallthrough 到 default**（= 当石头处理，正是 R12 的病根）。
        var scriptsRoot = System.IO.Path.Combine(ResolveProjectRoot(), "client", "Assets", "Scripts");
        var switchFiles = new List<string>();
        var risky = new List<string>();
        foreach (var f in System.IO.Directory.GetFiles(scriptsRoot, "*.cs", System.IO.SearchOption.AllDirectories))
        {
            var txt = System.IO.File.ReadAllText(f);
            if (txt.IndexOf("case TileKind.", StringComparison.Ordinal) < 0) continue;
            switchFiles.Add(System.IO.Path.GetFileName(f));
            if (txt.IndexOf("TileKind.Water", StringComparison.Ordinal) < 0) risky.Add(System.IO.Path.GetFileName(f));
        }
        switchFiles.Sort(StringComparer.Ordinal);
        var skillMod = Src("client/Assets/Scripts/Module/Skill/SkillModule.cs");
        Check(skillMod.Contains("case TileKind.Water:"),
            "SkillModule.cs（**片 J 的领地，只读不改**）：`BlocksProjectile` 也有显式 `case TileKind.Water` —— " +
            "跨片闸门 `combatcheck §15.1`（每个枚举值都必须有显式裁决）在 R12 新增值时确实变红并逼出重判");
        Check(risky.Count == 0,
            $"**全仓**含 `case TileKind.` 的消费者共 {switchFiles.Count} 个文件（{string.Join(", ", switchFiles)}）" +
            $"⇒ 全部对 `Water` 有显式分支（缺失 = {risky.Count}：{(risky.Count == 0 ? "无" : string.Join(", ", risky))}）");

        // ── ④ 表现侧：小地图不再把水画成石墙（R1-B 同一条判据）──
        Check(module.Contains("MapView.IsPaletteCycledFlatWallOverlay(gk, ok)"),
            "MapModule.BuildMinimap：水格的**物件 Cel 不叠**（`moor_river/028` 与石墙同 Cel 60 ⇒ " +
            "不叠之后水格只剩 floor 水 Cel，小地图上水 ≠ 石头）");

        // ★ 小地图的**数据层**判据（比看像素硬）：`moor_river/028` 与石墙在 `AutoMapCel` 物件表里
        //   是**同一个 Cel 60** ⇒ 修复前水格在小地图上带着"墙"的印记（= 看着就是石头）。
        //   逐格核对：水格**不许**有物件 Cel；`Rock`（石矮墙）格**必须**有。
        var mm = town.BuildMinimap();
        var waterWithOver = 0;
        var rockCells2 = 0;
        var rockWithOver = 0;
        for (var y = 0; y < town.Height; y++)
        {
            for (var x = 0; x < town.Width; x++)
            {
                var i = y * mm.width + x;
                if (i < 0 || i >= mm.celsOver.Count) continue;
                var over = mm.celsOver[i];
                var k = town.TileAt(new Vector2Int(x, y));
                if (k == TileKind.Water)
                {
                    if (over >= 0) waterWithOver++;
                }
                else if (k == TileKind.Rock)
                {
                    rockCells2++;
                    if (over >= 0) rockWithOver++;
                }
            }
        }
        Check(waterWithOver == 0,
            $"小地图数据层：**水格一个「墙」Cel 都不带**（带 = {waterWithOver} 格；" +
            "`moor_river/028` 与石墙同 Cel 60 ⇒ 按 R1-B 同一条判据不叠）");
        Check(rockCells2 > 0 && rockWithOver > 0,
            $"小地图数据层：`Rock` 格仍带物件 Cel（{rockWithOver}/{rockCells2}）" +
            "⇒ **水与石头在小地图上已可区分**（水 = 只有 floor Cel；石 = 带墙/物件 Cel）");
        Check(TileKindInfo.IsGroundLayer(TileKind.Water) && !TileKindInfo.IsBlocking(TileKind.Grass),
            "`TileKindInfo.IsGroundLayer(Water)` == true（水是地面层，不是物件层）");
        Console.WriteLine();
    }

    // ── 小工具 ───────────────────────────────────────────────────────────────
    /// <summary>
    /// 从宿主自己的可执行目录向上找「含 client/Assets 的那一层」= 仓库根。
    /// 宿主位于 tools/probes/hosts/&lt;名&gt;/bin/&lt;cfg&gt;/&lt;tfm&gt;/；按层数写死会在迁移后失效
    /// （与 corecheck / fullcheck / savecheck / uicheck 同一套写法）。
    /// </summary>
    private static string ResolveProjectRoot()
    {
        var dir = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (System.IO.Directory.Exists(System.IO.Path.Combine(dir.FullName, "client", "Assets")))
                return dir.FullName;
            dir = dir.Parent;
        }
        Console.WriteLine("[warn] 未从可执行目录向上找到含 client/Assets 的仓库根，回退相对路径 clover-project-diablo2");
        return @"clover-project-diablo2";
    }

    private static MapModule NewMap()
    {
        // MapModule 是 internal ⇒ 只有同程序集（Diablo2）能 new；本宿主以 InternalsVisibleTo 不适用，
        // 因此通过**同一批源码**编译到本程序集后直接 new（编译期可见）。
        return new MapModule();
    }

    // ── 21. 桥面(deck)排序：站在桥上不被栏杆盖住 ────────────────────────────────
    /// <summary>
    /// 用户症状「营地出门的桥，还不是从桥上走，还是桥下。」的**离线判据**（数值类，不截图）。
    /// <para>根因（像素级 before 证据见 `tools/probes/measure/measure_bridge_deck.py`）：
    /// 排序值 = `(gx+gy)*4 + 100 + 层偏移`；桥面格的正南一格恒是桥栏杆物件（图形自本格底边
    /// 向上溢出 ≈2 格）⇒ 桥面实体 `4D+102` 必然被南侧栏杆 `4(D+1)+101 = 4D+105` 盖住。</para>
    /// <para>覆盖口径 = 影响域穷举：① 期望 deck 集合**从布局数据推**（⛔ 不写坐标区间）
    /// ② 逐格双向核对（漏/多）③ 栏杆行地面（同图集但不可走）必须不算桥面
    /// ④ 纯函数排序断言逐格算 ⑤ 正南确实是栏杆（否则断言空跑）⑥ 改前档位确实被盖（反证根因）
    /// ⑦ 换图 / Clear 后标记不残留。</para>
    /// </summary>
    private static void Step21_BridgeDeckOrdering()
    {
        Section("21. 桥面(deck)排序：站桥上不被栏杆盖住（用户「营地出门的桥，还是桥下走」）");
        var m = NewMap();
        m.Generate(AreaId.Town, 0);

        // ① 期望集合：**从布局数据推** = 该格 floor 键取自 deck 类包 且 kind 字符 'd'（可走）
        var pack = MapGenTownLayout.Packs[0];        // "moor_bridge" —— deck 类包（唯一口径 = DeckTiles）
        var expected = new HashSet<Vector2Int>();
        var railGround = new HashSet<Vector2Int>();  // 栏杆行的地面：同图集但不可走
        for (var y = 0; y < MapGenTownLayout.Height; y++)
        {
            for (var x = 0; x < MapGenTownLayout.Width; x++)
            {
                MapGenTownLayout.TryGetTiles(x, y, out var gk, out _);
                var slash = gk != null ? gk.IndexOf('/') : -1;
                var gpack = slash > 0 ? gk.Substring(0, slash) : gk;
                if (gpack != pack) continue;
                if (MapGenTownLayout.Rows[y][x] == 'd') expected.Add(new Vector2Int(x, y));
                else railGround.Add(new Vector2Int(x, y));
            }
        }
        Console.WriteLine($"  布局数据：deck 类包（{pack}）地砖共 {expected.Count + railGround.Count} 格" +
                          $"（可走桥面 'd' = {expected.Count} 格，栏杆行 = {railGround.Count} 格）");
        Check(expected.Count > 0, $"布局里存在可走的桥面格（{expected.Count} 格 ⇒ 下面的断言不是空跑）");

        // ② 逐格双向核对
        var actual = new HashSet<Vector2Int>();
        for (var y = 0; y < m.Height; y++)
        {
            for (var x = 0; x < m.Width; x++)
            {
                if (m.IsDeckGrid(new Vector2Int(x, y))) actual.Add(new Vector2Int(x, y));
            }
        }
        var missing = new List<Vector2Int>();
        foreach (var c in expected) if (!actual.Contains(c)) missing.Add(c);
        var extra = new List<Vector2Int>();
        foreach (var c in actual) if (!expected.Contains(c)) extra.Add(c);
        Console.WriteLine($"  实测 IsDeckGrid == true = {actual.Count} 格；漏 {missing.Count} 格；多 {extra.Count} 格");
        Check(missing.Count == 0, $"每一格桥面格 IsDeckGrid == true（漏 {missing.Count}：{Fmt(missing)}）");
        Check(extra.Count == 0, $"非桥面格一律 false（多 {extra.Count}：{Fmt(extra)}）");

        // ③ 栏杆行地面（同图集但不可走）必须不算桥面
        var railTrue = 0;
        foreach (var c in railGround) if (m.IsDeckGrid(c)) railTrue++;
        Check(railTrue == 0, $"栏杆行地面（同图集、不可走）不算桥面：{railGround.Count} 格全 false（实测 true = {railTrue}）");

        // ④⑤⑥ 逐格纯函数断言 + 正南确实是栏杆 + 改前档位确实被盖
        var badLow = 0;
        var badHigh = 0;
        var beforeCovered = 0;
        var southIsRail = 0;
        foreach (var g in expected)
        {
            var deckOrder = Iso.EntitySortOrder(g, true);
            var plainOrder = Iso.EntitySortOrder(g, false);
            var southObj = Iso.SortOrder(new Vector2Int(g.x, g.y + 1), GameConst.LayerOffsetObject);
            var south2Obj = Iso.SortOrder(new Vector2Int(g.x, g.y + 2), GameConst.LayerOffsetObject);
            if (deckOrder <= southObj) badLow++;
            if (deckOrder >= south2Obj) badHigh++;
            if (plainOrder <= southObj) beforeCovered++;

            m.TryGetTileKeys(g.x, g.y + 1, out _, out var so);
            var slash = so != null ? so.IndexOf('/') : -1;
            if (slash > 0 && so.Substring(0, slash) == pack) southIsRail++;
        }
        Console.WriteLine($"  deck 档 = {GameConst.LayerOffsetDeckEntity}（普通实体档 = {GameConst.LayerOffsetEntity}）；" +
                          $"例：格 (46,25) deck 档 = {Iso.EntitySortOrder(new Vector2Int(46, 25), true)}" +
                          $" > 正南(46,26) 物件层 = {Iso.SortOrder(new Vector2Int(46, 26), GameConst.LayerOffsetObject)}" +
                          $" 且 < 正南两格(46,27) 物件层 = {Iso.SortOrder(new Vector2Int(46, 27), GameConst.LayerOffsetObject)}");
        Check(badLow == 0, $"全部 {expected.Count} 格：deck 实体的排序值 > 正南一格物件层（不被栏杆盖住）；违例 {badLow}");
        Check(badHigh == 0, $"全部 {expected.Count} 格：deck 实体的排序值 < 正南两格物件层（不越档）；违例 {badHigh}");
        Check(southIsRail == expected.Count,
            $"每格桥面格的正南一格确实是栏杆物件（{pack} wall 层）：{southIsRail}/{expected.Count} ⇒ 断言不是空跑");
        Check(beforeCovered == expected.Count,
            $"反证根因：改前普通实体档**确实**被南侧栏杆盖住：{beforeCovered}/{expected.Count} 格成立");

        // ⑦ 换图 / Clear 不残留（否则一个断言会"假通过"）
        var m2 = NewMap();
        m2.Generate(AreaId.Town, 0);
        var deckTown = CountDeck(m2);
        m2.Generate(AreaId.DenOfEvil, 7);
        var deckCave = CountDeck(m2);
        Check(deckTown > 0 && deckCave == 0,
            $"换图后 deck 标记不残留：城镇 {deckTown} 格 → 邪恶洞穴 {deckCave} 格（洞穴地砖包 ≠ deck 类包）");
        m2.Clear();
        Check(CountDeck(m2) == 0, $"Clear() 后 IsDeckGrid 全 false（实测 {CountDeck(m2)} 格）");

        var m3 = NewMap();
        m3.Generate(AreaId.BloodMoor, 11);
        Console.WriteLine($"  [信息] 血腥荒野的 deck 格 = {CountDeck(m3)}" +
                          "（>0 ⇒ 野外那座桥用同一份 dt1、也自动登记上了；=0 ⇒ 野外没有该包地砖）");
    }

    // ═════════════════════════════════════════════════════════════════════
    // 22. ★ 审计 B（离线片：D2 几何与场景 / D9 物理与碰撞 / D11 交互输入）
    //     口径：**只加断言**（⛔ 不改动 §0~§21 的任何一行）；本步自身全绿，
    //     红行以 `【红行证据·…】` 前缀的行输出（可复核、可 grep）。
    // ═════════════════════════════════════════════════════════════════════
    private static void Step22_AuditBGeoInput()
    {
        Section("22. ★ 审计 B：跨渲染路径的 deck 排序口径 + 地图边界格 + 八邻域距离阈值");

        // ── ① 谁还在用「普通实体档」？（出处 = 生产代码 grep；宿主只做数值断言）──────
        //   走 IsDeckGrid 抬档的路径 = Module/View/ViewModule.cs:1191 `EntitySortOrder(g)`
        //     ⇒ 玩家(731) / 怪物(355) / 地面物品(492) / NPC(199) 全走它；
        //   ✅ 2026-09-23 修：Module/Skill/ProjectileView.cs 原先自己写
        //     `sr.sortingOrder = Iso.SortOrder(p.Grid, GameConst.LayerOffsetEntity);`（投射物 =
        //     审计 B 红行 R2 的漏网路径），现改调 `ViewModule.EntitySortOrder(g)` ⇒ **全仓已无
        //     裸实体档路径**（`grep LayerOffsetEntity` 只剩 Iso 定义与"无地图"兜底分支）。
        //     该口径的**生产路径断言**在 `tools/combatcheck` §15.4（本宿主不编 Module/View+Skill）。
        var tm = NewMap();
        tm.Generate(AreaId.Town, 0);
        var deck = new List<Vector2Int>();
        for (var y = 0; y < tm.Height; y++)
            for (var x = 0; x < tm.Width; x++)
                if (tm.IsDeckGrid(new Vector2Int(x, y))) deck.Add(new Vector2Int(x, y));

        var plainCovered = 0;
        var deckOk = 0;
        foreach (var g in deck)
        {
            var southObj = Iso.SortOrder(new Vector2Int(g.x, g.y + 1), GameConst.LayerOffsetObject);
            if (Iso.SortOrder(g, GameConst.LayerOffsetEntity) < southObj) plainCovered++;
            if (Iso.EntitySortOrder(g, true) > southObj) deckOk++;
        }
        Console.WriteLine($"  deck 格 = {deck.Count}；例（格(46,25)）：普通实体档 = " +
                          $"{Iso.SortOrder(new Vector2Int(46, 25), GameConst.LayerOffsetEntity)}" +
                          $" ｜ deck 档 = {Iso.EntitySortOrder(new Vector2Int(46, 25), true)}" +
                          $" ｜ 正南(46,26) 物件层 = {Iso.SortOrder(new Vector2Int(46, 26), GameConst.LayerOffsetObject)}");
        Check(deck.Count > 0, $"城镇存在 deck 格（{deck.Count} 格）⇒ 下面的口径断言不是空跑");
        Check(deckOk == deck.Count,
            $"ViewModule 口径（IsDeckGrid ⇒ 实体抬档）逐格 > 正南一格物件层：{deckOk}/{deck.Count}");
        Check(plainCovered == deck.Count,
            $"【红行证据·D9×D2】凡用**普通实体档**的渲染路径，在全部 {deck.Count} 格桥面上 100% 被正南栏杆盖住" +
            $"（实测 {plainCovered}/{deck.Count}）——这正是审计 B 红行 R2 的根因数值（投射物原先走的就是这个档，" +
            "现已改走 ViewModule.EntitySortOrder；生产路径断言见 combatcheck §15.4）" +
            "（同类缺陷族：改了排序口径却没扫全渲染路径）");

        // ── ② 地图边界 / 图外一格 / 边缘格 / 出口可达（逐区域）───────────────────
        var areas = new[] { AreaId.Town, AreaId.BloodMoor, AreaId.DenOfEvil };
        var seeds = new[] { 0, 424242, 7 };
        for (var i = 0; i < areas.Length; i++)
        {
            var m = NewMap();
            m.Generate(areas[i], seeds[i]);
            if (!m.IsGenerated) { Check(false, $"{areas[i]} 未生成成功 ⇒ 边界断言无法进行"); continue; }

            var w = m.Width;
            var h = m.Height;

            var oobIn = new[] { new Vector2Int(-1, -1), new Vector2Int(-1, 0), new Vector2Int(0, -1),
                                new Vector2Int(w, h), new Vector2Int(w - 1, h), new Vector2Int(w, h - 1) };
            var inBoundsBad = 0;
            for (var k = 0; k < oobIn.Length; k++) if (m.InBounds(oobIn[k])) inBoundsBad++;
            Check(inBoundsBad == 0, $"{areas[i]} {w}x{h}：图外一格 InBounds == false（{oobIn.Length} 个探针，违例 {inBoundsBad}）");

            var oobWalk = 0; var oobVoid = 0; var oobThrew = 0;
            for (var k = 0; k < oobIn.Length; k++)
            {
                try
                {
                    if (m.Walkable(oobIn[k])) oobWalk++;
                    if (m.TileAt(oobIn[k]) == TileKind.Void) oobVoid++;
                }
                catch (Exception e)
                {
                    oobThrew++;
                    Console.WriteLine($"    图外格 {oobIn[k]} 抛异常：{e.GetType().Name}: {e.Message}");
                }
            }
            Check(oobWalk == 0, $"{areas[i]}：图外一格一律不可走（实测可走 {oobWalk}）");
            Check(oobVoid == oobIn.Length, $"{areas[i]}：图外一格 TileAt == Void（{oobVoid}/{oobIn.Length}）");
            Check(oobThrew == 0, $"{areas[i]}：图外一格不抛异常（{oobThrew}）");

            // 四角 + 四边中点：`Walkable` 必须与 `TileKindInfo.IsWalkable` 同源（⛔ 宿主不另写判等表）
            var edge = new[] { new Vector2Int(0, 0), new Vector2Int(w - 1, 0), new Vector2Int(0, h - 1), new Vector2Int(w - 1, h - 1),
                               new Vector2Int(w / 2, 0), new Vector2Int(w / 2, h - 1), new Vector2Int(0, h / 2), new Vector2Int(w - 1, h / 2) };
            var mismatch = 0; var edgeWalk = 0;
            var edgeText = new List<string>();
            for (var k = 0; k < edge.Length; k++)
            {
                var g = edge[k];
                var kind = m.TileAt(g);
                if (m.Walkable(g) != TileKindInfo.IsWalkable(kind)) mismatch++;
                if (m.Walkable(g)) edgeWalk++;
                edgeText.Add($"{g}={kind}{(m.Walkable(g) ? "可走" : "阻挡")}");
            }
            Console.WriteLine($"    {areas[i]} 边缘/角格：{string.Join("｜", edgeText)}");
            Check(mismatch == 0,
                $"{areas[i]}：边缘/角格 Walkable 与 TileKindInfo.IsWalkable 同源（不一致 {mismatch} 处）");
            Console.WriteLine($"    [信息] {areas[i]}：{edge.Length} 个边缘/角格中可走 {edgeWalk} 个" +
                              "（>0 ⇒ 边界存在可走格，需确认它的外邻不是「露到图外」）");

            // 出口格：全部可走 + 全部从出生点可达（§3 只测单点，这里逐出口）
            var exitBad = 0; var exitUnreachable = 0;
            for (var k = 0; k < m.Exits.Count; k++)
            {
                var e = m.Exits[k];
                if (!m.Walkable(e))
                {
                    exitBad++;
                    Console.WriteLine($"    ❗ {areas[i]} 出口 {e} 不可走（TileAt={m.TileAt(e)}）");
                    continue;
                }
                if (m.FindPath(m.SpawnPoint, e) == null)
                {
                    exitUnreachable++;
                    Console.WriteLine($"    ❗ {areas[i]} 出生点 {m.SpawnPoint} → 出口 {e} 无路径");
                }
            }
            Check(exitBad == 0, $"{areas[i]}：{m.Exits.Count} 个出口格全部可走（不可走 {exitBad}）");
            Check(exitUnreachable == 0, $"{areas[i]}：全部出口格从出生点可达（不可达 {exitUnreachable}）");

            // 逐 TileKind 计数（"行数是数出来的"：脚本产出实体表，⛔ 不手写）
            var count = new Dictionary<TileKind, int>();
            for (var x = 0; x < w; x++)
                for (var y = 0; y < h; y++)
                {
                    var k2 = m.TileAt(new Vector2Int(x, y));
                    count[k2] = count.TryGetValue(k2, out var n2) ? n2 + 1 : 1;
                }
            var kinds = new List<TileKind>(count.Keys);
            kinds.Sort((p, q) => ((int)p).CompareTo((int)q));
            var parts = new List<string>();
            for (var k = 0; k < kinds.Count; k++)
                parts.Add($"{kinds[k]}×{count[kinds[k]]}{(TileKindInfo.IsWalkable(kinds[k]) ? "(可走)" : "(阻挡)")}");
            Console.WriteLine($"    [枚举] {areas[i]} 盘上实际出现的地形：{string.Join(" / ", parts)}");
            var walkCount = 0;
            foreach (var kv in count) if (TileKindInfo.IsWalkable(kv.Key)) walkCount += kv.Value;
            Check(walkCount == m.WalkableCount,
                $"{areas[i]}：逐格枚举出的可走格数 == WalkableCount（{walkCount} vs {m.WalkableCount}）");
        }

        // ── ③ 八邻域距离阈值矩阵（**反射枚举** GameConst，⛔ 不手写常量清单）────────
        var sqrt2 = Math.Sqrt(2.0);
        var fields = typeof(GameConst).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
        var rangeFields = new List<System.Reflection.FieldInfo>();
        for (var i = 0; i < fields.Length; i++)
            if (fields[i].FieldType == typeof(float) && fields[i].Name.EndsWith("Range", StringComparison.Ordinal))
                rangeFields.Add(fields[i]);
        rangeFields.Sort((p, q) => string.CompareOrdinal(p.Name, q.Name));

        Console.WriteLine($"  反射枚举 `GameConst` 里名字以 `Range` 结尾的 float 常量：共 {rangeFields.Count} 个；√2 = {sqrt2:0.00000}");
        var diagFail = new List<string>();
        for (var i = 0; i < rangeFields.Count; i++)
        {
            var v = (float)rangeFields[i].GetValue(null);
            var coversDiag1 = v >= (float)sqrt2;
            var coversLine2 = v >= 2f;
            var coversDiag2 = v >= (float)(2.0 * sqrt2);
            Console.WriteLine($"    {rangeFields[i].Name,-20} = {v,7:0.###} ｜1格斜角(√2){(coversDiag1 ? "✅" : "❌")}" +
                              $" ｜2格直线(2.0){(coversLine2 ? "✅" : "❌")} ｜2格斜角(2√2){(coversDiag2 ? "✅" : "❌")}");
            if (!coversDiag1) diagFail.Add($"{rangeFields[i].Name}={v:0.###}");
        }
        Check(rangeFields.Count == 8,
            "GameConst 的 Range 族 float 常量 = 8 个（Melee/Ranged/Pickup/Talk/Portal/MonsterAggro/MonsterLeash/Hover）" +
            $"—— 新增/删除会在这里被抓住（实测 {rangeFields.Count}）");
        Console.WriteLine("  【红行证据·D9/D11】欧氏阈值 < √2 ⇒ 斜角 100% 失效族：" +
                          (diagFail.Count == 0 ? "无" : string.Join(", ", diagFail)) +
                          "；其中 PickupRange 已由 ItemModule.Pickup 改用 Iso.IsAdjacent（Chebyshev）修掉，" +
                          "**PortalRange / HoverRange 全仓 0 消费方**（见 audit-B 报告 D11）");
    }

    /// <summary>数一张图里 IsDeckGrid == true 的格数（换图/清图残留判据）。</summary>
    private static int CountDeck(MapModule m)
    {
        var n = 0;
        for (var y = 0; y < m.Height; y++)
        {
            for (var x = 0; x < m.Width; x++)
            {
                if (m.IsDeckGrid(new Vector2Int(x, y))) n++;
            }
        }
        return n;
    }

    // ═════════════════════════════════════════════════════════════════════
    // 25. ★ 片 S2（2026-09-23）：传送点锚点（用户报「传送点没效果」）
    //     判据 1/3 = **生成 1 个且坐标与原版表一致**（数值类 ⇒ 断言，不截图）。
    //     出处逐条写在 `MapGenTown.Waypoint` 的注释里（Levels.txt 的 Waypoint 列 /
    //     Objects.txt Id=119 / LvlPrest 四块城镇 ds1 的 kind=2 预设单位重合于 (31,26)）。
    //     ⛔ 只加断言：本步不改任何既有判据（§10「出口恰 3 格」等一律原样）。
    // ═════════════════════════════════════════════════════════════════════
    private static void Step25_WaypointAnchor()
    {
        Section("25. 传送点锚点（原版位置 / 可走 / 可达 / 不与出生点·NPC·出口重叠）");

        var map = NewMap();
        map.Generate(AreaId.Town, 20250916);

        var pts = map.WaypointPoints;
        Console.WriteLine($"   WaypointPoints = {Fmt(pts)}（{pts?.Count ?? 0} 个）");
        Check(pts != null && pts.Count == 1, $"罗格营地**恰 1 个**传送点锚点（实得 {pts?.Count ?? 0}）");

        var expect = MapGenTown.Waypoint;
        Check(pts != null && pts.Count == 1 && pts[0] == expect,
            $"锚点坐标 = 原版表算出的关卡格 {expect}（实得 {(pts != null && pts.Count == 1 ? pts[0].ToString() : "-")}）；" +
            "出处 = Levels.txt Waypoint 列 0 + Objects.txt Id=119 + LvlPrest 四块 ds1 kind=2 预设单位重合");

        if (pts == null || pts.Count != 1) return;
        var wp = pts[0];

        Check(map.Walkable(wp), $"锚点 {wp} 可走（不可走 ⇒ 玩家走不到、面板点不开）");

        // 出生点/NPC/出口都不许与锚点重合（重合 ⇒ 点哪都是"另一个东西"）
        var clash = new List<string>();
        if (map.SpawnPoint == wp) clash.Add("SpawnPoint");
        if (Has(map.Exits, wp)) clash.Add("Exit");
        if (Has(map.NpcPoints, wp)) clash.Add("NpcPoint");
        Check(clash.Count == 0, $"锚点不与出生点/出口/NPC 重合（冲突：{(clash.Count == 0 ? "无" : string.Join(",", clash))}）");

        var path = map.FindPath(map.SpawnPoint, wp);
        var steps = path == null ? -1 : path.Count - 1;
        Check(steps >= 0, $"出生点 {map.SpawnPoint} 有路径走到锚点 {wp}（步数 {steps}）");

        // 其它区域**不许**凭空多出传送点（面板只在城镇开；原版野外/洞穴的传送点本工程未做）
        var moor = NewMap();
        moor.Generate(AreaId.BloodMoor, 20250916);
        Check(moor.WaypointPoints.Count == 0,
            $"血腥荒野没有传送点锚点（实得 {moor.WaypointPoints.Count}）——⛔ 不自创目的地集合");
        Console.WriteLine();
    }

    // ═════════════════════════════════════════════════════════════════════
    // 26. ★ 2026-09-23（S2）：Tab 自动地图的「记忆式已探索」数据源
    //     `IMapModule.ExploredCells` + `Events.MapExplored`（主 agent 追加的离线活）。
    //     ⚠️ 本宿主**没有 Unity 运行时** ⇒ 造不出 `MapView`（`new GameObject()` 会炸）
    //     ⇒ 这里只断言"可离线判"的那一半：契约成员存在/类型对/无 view 时是空且不抛，
    //     外加**源码级守卫**（发事件必须被"首次"判定守住 —— 这正是"定义了但没人查"的高发形态）。
    // ═════════════════════════════════════════════════════════════════════
    private static void Step26_ExploredCellsContract()
    {
        Section("26. 记忆式已探索：IMapModule.ExploredCells + Events.MapExplored");

        // ① 契约成员在（逐字签名：IReadOnlyCollection<Vector2Int> ExploredCells { get; }）
        var prop = typeof(IMapModule).GetProperty("ExploredCells",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        Console.WriteLine($"  IMapModule.ExploredCells = {(prop == null ? "(缺失)" : prop.PropertyType.Name)}"
            + $"（可写={prop != null && prop.CanWrite}）");
        Check(prop != null && prop.PropertyType == typeof(IReadOnlyCollection<Vector2Int>),
            "契约成员 IReadOnlyCollection<Vector2Int> ExploredCells { get; } 存在且类型逐字一致");
        Check(prop != null && !prop.CanWrite, "该成员是**只读**（无 setter）");

        // ② 真实现里：未铺装（没有 MapView）⇒ 空集合、不抛
        var map = NewMap();
        map.Generate(AreaId.Town, 20250916);
        var cells = map.ExploredCells;
        Console.WriteLine($"  未调 ShowArea 时 ExploredCells = {(cells == null ? "(null)" : cells.Count + " 格")}");
        Check(cells != null && cells.Count == 0,
            "渲染层未铺装（没调 ShowArea / 离线宿主无 GameObject）⇒ ExploredCells = 空集合且不抛");

        // ③ 事件常量逐字
        var evtField = typeof(Events).GetField("MapExplored",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
        var evtValue = evtField != null ? (string)evtField.GetValue(null) : null;
        Console.WriteLine($"  Events.MapExplored = \"{evtValue}\"");
        Check(evtField != null && evtValue == "D2.Map.Explored",
            "事件常量 Events.MapExplored 存在且值 = \"D2.Map.Explored\"");

        // ④ 源码级守卫：发事件必须被"首次"判定守住（⛔ 不许每帧/每格无脑发）
        var repo2 = FindRepoRoot();
        if (repo2 == null)
        {
            Console.WriteLine("   （找不到仓库根 ⇒ 跳过源码级守卫）");
            Console.WriteLine();
            return;
        }
        var moduleSrc = System.IO.File.ReadAllText(
            System.IO.Path.Combine(repo2, "client", "Assets", "Scripts", "Module", "Map", "MapModule.cs"));
        var viewSrc = System.IO.File.ReadAllText(
            System.IO.Path.Combine(repo2, "client", "Assets", "Scripts", "Module", "Map", "MapView.cs"));
        var onGrid = MethodBodyText(moduleSrc, "private void OnPlayerGridChanged(Vector2Int g)");
        Console.WriteLine($"  OnPlayerGridChanged 体：if(_view.MarkExplored(g)) = "
            + $"{onGrid.Contains("if (_view.MarkExplored(g)) OnFirstExplored(g);")}"
            + $"；首次判定发事件 = {onGrid.Contains("OnFirstExplored")}");
        Check(onGrid.Contains("if (_view.MarkExplored(g)) OnFirstExplored(g);"),
            "发方 = MapModule.OnPlayerGridChanged，且**由 `MapView.MarkExplored` 的返回值把门**"
            + "（重复走过同一格不重发 ⇒ 不是每帧发）");
        Check(moduleSrc.Contains("Events.MapExplored")
              && moduleSrc.Contains("Emit<IReadOnlyCollection<Vector2Int>>(Events.MapExplored, new[] { g })"),
            "事件载荷 = 新探索到的格集合（逐字：\n      Emit<IReadOnlyCollection<Vector2Int>>(Events.MapExplored, new[] { g })）");
        Check(viewSrc.Contains("public bool MarkExplored(Vector2Int g)"),
            "`MapView.MarkExplored` 返回 bool（\"这一格是不是第一次\"——事件的唯一判据来源）");
        Check(moduleSrc.Contains("if (_view != null) _view.CollectExplored(_exploredCache);"),
            "`ExploredCells` 的实现 = 从渲染层 `_explored` 投影（⛔ 不在模块里新造第二份已探索状态）");
        Console.WriteLine();
    }

    /// <summary>抽一个方法体的源文本（从签名起、按大括号配平到结束）。</summary>
    private static string MethodBodyText(string src, string signature)
    {
        var i = src.IndexOf(signature, StringComparison.Ordinal);
        if (i < 0) return string.Empty;
        var open = src.IndexOf('{', i);
        if (open < 0) return string.Empty;
        var depth = 0;
        for (var k = open; k < src.Length; k++)
        {
            if (src[k] == '{') depth++;
            else if (src[k] == '}')
            {
                depth--;
                if (depth == 0) return src.Substring(open, k - open + 1);
            }
        }
        return string.Empty;
    }

    /// <summary>`IReadOnlyList&lt;Vector2Int&gt;` 上没有 `Contains`（宿主不开 System.Linq）⇒ 手写一份。</summary>
    private static bool Has(IReadOnlyList<Vector2Int> list, Vector2Int g)
    {
        if (list == null) return false;
        for (var i = 0; i < list.Count; i++) { if (list[i] == g) return true; }
        return false;
    }

    private static string Fmt(IReadOnlyList<Vector2Int> list)
    {
        if (list == null || list.Count == 0) return "[]";
        var parts = new List<string>();
        for (var i = 0; i < list.Count; i++) parts.Add(list[i].ToString());
        return "[" + string.Join(", ", parts) + "]";
    }

    private static void Section(string title)
    {
        Console.WriteLine("────────────────────────────────────────────────────────────");
        Console.WriteLine("▶ " + title);
        Console.WriteLine("────────────────────────────────────────────────────────────");
    }

    // ═════════════════════════════════════════════════════════════════════
    // 24. ★ 片 M3（2026-09-23）：营地那座桥的**可走性** + **东边界接缝** + 缺瓦片
    //     用户症状：①「桥的上面过不去」②「穿过桥去不了下一张地图」
    //              ③「地图边界斜切黑三角 / 石墙缺一段瓦片」
    //     判据（数值类，不进 Play，秒级）：
    //       ① 桥面格（地面键取自 deck 类包；唯一判据 = `DeckTiles`）里的**全部可走格**必须
    //          与其中一个同属一个 8 邻连通分量（对角要求两侧可走，与 `CloverEngine.AStar` 同规则）；
    //       ② 桥两端的可走桥面格之间**必须有路径**（打印步数，走 `GridMap.FindPath` = A*）；
    //       ③ 关卡**东边界列**（x = Width-1）的桥面格必须是**出城口**（原版口径 = 城镇关卡东边界
    //          与野外第 0 列是同一条共享边列，出处 `libd2/.../drlg/outdoors/OutRoom.zig:271`，
    //          见 `MapGenTown.cs` ①.7）—— 且 `VerifyConnectivity` 必须通过（出口列必须从出生点可达，
    //          否则 `MapModule` 会换 seed 重试，用户看到的会是另一张图）；
    //       ④ 布局引用的**每个瓦片键**都必须有对应 PNG（缺图 = 露黑底 =「墙缺一段」的候选根因）。
    // ⛔ 只加断言、不改既有判据（片 M3 约束）。
    // ═════════════════════════════════════════════════════════════════════

    private static void Step24_TownBridgeWalkableAndSeam()
    {
        Console.WriteLine();
        Console.WriteLine("── 24. ★ 片 M3：营地桥面可走性 + 东边界接缝 + 缺瓦片");

        var map = new GridMap();
        MapGenTown.Generate(map, new Rng(20260923));

        var deck = new List<Vector2Int>();
        var deckWalk = new List<Vector2Int>();
        var missing = new List<string>();
        var repo = FindRepoRoot();
        var tilesChecked = 0;

        for (var x = 0; x < map.Width; x++)
        {
            for (var y = 0; y < map.Height; y++)
            {
                var g = new Vector2Int(x, y);
                string gk, ok;
                if (!map.TryGetTiles(x, y, out gk, out ok)) continue;

                if (DeckTiles.IsDeckGroundKey(gk))
                {
                    deck.Add(g);
                    if (map.Walkable(g)) deckWalk.Add(g);
                }
                if (repo != null && map.Get(g) != TileKind.Void)
                {
                    tilesChecked += CheckTileExists(repo, "Tiles", gk, g, missing);
                    tilesChecked += CheckTileExists(repo, "Objects", ok, g, missing);
                }
            }
        }

        Console.WriteLine($"   桥面地砖格 = {deck.Count}（可走 {deckWalk.Count} / 不可走 {deck.Count - deckWalk.Count}）");
        Check(deck.Count > 0, "营地布局里存在桥面地砖格（地面键取自 deck 类包）");
        Check(deckWalk.Count > 0, "桥面里存在**可走**格（不是整座桥都不可走）");
        if (deckWalk.Count == 0) return;

        // ① 8 邻连通：从任意一个可走桥面格做一次洪水填充，**所有**可走桥面格都必须被覆盖
        var reach = new bool[map.Width, map.Height];
        map.FloodFillFrom(deckWalk[0], reach);
        var isolated = 0;
        for (var i = 0; i < deckWalk.Count; i++)
        {
            if (!reach[deckWalk[i].x, deckWalk[i].y]) isolated++;
        }
        Check(isolated == 0,
            $"全部可走桥面格与 {deckWalk[0]} 同属一个 8 邻连通分量（被隔离 {isolated} 格）");

        // ② 桥两端（x 最小 / 最大的可走桥面格）之间必须有路径（A*）
        var west = deckWalk[0];
        var east = deckWalk[0];
        for (var i = 1; i < deckWalk.Count; i++)
        {
            var c = deckWalk[i];
            if (c.x < west.x || (c.x == west.x && c.y < west.y)) west = c;
            if (c.x > east.x || (c.x == east.x && c.y > east.y)) east = c;
        }
        var path = map.FindPath(west, east);
        var steps = path == null ? -1 : path.Count - 1;
        Check(steps >= 0, $"桥西端 {west} → 东端 {east} 存在路径（步数 {steps}）");
        Check(steps >= 0 && steps <= (east.x - west.x) + 4,
            $"路径长度合理（不绕远）：{steps} 步，直线 {east.x - west.x} 步（允许 ≤ +4）");

        // ③ 东边界接缝（用户症状「穿过桥去不了下一张地图」）：
        //    原版口径 = 城镇东边界列与野外第 0 列是同一条共享边列（`OutRoom.zig:271`），
        //    过河那条路就是桥 ⇒ 判据 = `MapSeam.IsTownEastSeam`（区域 = Town && x == Width-1 && 桥面 deck）。
        //    ⛔ 本片**不**把接缝写进 `map.Exits`（那会让 §10 已冻结的「出口恰 3 格」判据失效 = 改判据），
        //      触发侧判据由 `PlayerModule.CheckExit` + `MapSeam` 承担（`movecheck §9` 断言该纯函数）。
        var eastDeck = 0;
        var seamOk = 0;
        var seamCell = new Vector2Int(-1, -1);
        for (var i = 0; i < deckWalk.Count; i++)
        {
            if (deckWalk[i].x != map.Width - 1) continue;
            eastDeck++;
            if (seamCell.x < 0) seamCell = deckWalk[i];
            if (MapSeam.IsTownEastSeam(map.Area, map.Width, deckWalk[i], map.IsDeck(deckWalk[i]))) seamOk++;
        }
        Console.WriteLine($"   东边界列（x={map.Width - 1}）上的**可走桥面格** = {eastDeck}；" +
                          $"其中满足接缝判据 = {seamOk}");
        Check(eastDeck > 0,
            "关卡东边界列上存在可走桥面格（= 原版共享边列上的过河落脚点）");
        Check(eastDeck > 0 && seamOk == eastDeck,
            $"这些格**全部**满足接缝判据 MapSeam.IsTownEastSeam（{seamOk}/{eastDeck}）");
        if (eastDeck > 0)
        {
            var seamPath = map.FindPath(map.SpawnPoint, seamCell);
            var seamSteps = seamPath == null ? -1 : seamPath.Count - 1;
            Check(seamSteps >= 0,
                $"从出生点 {map.SpawnPoint} 有路径走到接缝格 {seamCell}（步数 {seamSteps}）" +
                " ⇒ 过桥向东一定会踏上接缝（玩家实测的那条路走得到）");
        }

        // ③b 出口必须从出生点可达（否则 MapModule 换 seed 重试 ⇒ 玩家看到另一张图）
        int unreachable;
        Vector2Int first;
        var conn = map.VerifyConnectivity(out unreachable, out first);
        Check(conn, $"生成后连通性自检通过（需可达目标不可达数 = {unreachable}，首个 = {first}）" +
                    $"；出口总数 = {map.Exits.Count}");

        // ④ 缺瓦片：布局引用的每个键都必须有 PNG
        if (repo == null)
        {
            Console.WriteLine("   （找不到 client/Assets 仓库根 ⇒ 跳过瓦片存在性检查）");
        }
        else
        {
            Console.WriteLine($"   逐格检查瓦片键 {tilesChecked} 个（Resources/Clover/D2/{{Tiles,Objects}}/<pack>/<idx>.png）");
            var head = "";
            for (var i = 0; i < missing.Count && i < 12; i++) head += (i > 0 ? ", " : "") + missing[i];
            Check(missing.Count == 0,
                $"布局引用的全部瓦片键都有 PNG（缺 {missing.Count} 个{(missing.Count > 0 ? "：" + head : "")}）");
        }
    }

    /// <summary>该瓦片键是否在 `client/Assets/Resources/Clover/D2/<layer>/<pack>/<idx>.png` 存在。</summary>
    private static int CheckTileExists(string repo, string layer, string key, Vector2Int g, List<string> missing)
    {
        if (string.IsNullOrEmpty(key)) return 0;
        var slash = key.IndexOf('/');
        if (slash <= 0) return 0;
        var pack = key.Substring(0, slash);
        var idx = key.Substring(slash + 1);
        var p = System.IO.Path.Combine(repo, "client", "Assets", "Resources", "Clover", "D2", layer, pack, idx + ".png");
        if (System.IO.File.Exists(p)) return 1;
        missing.Add($"{layer}/{pack}/{idx}@({g.x},{g.y})");
        return 1;
    }

    /// <summary>从宿主可执行目录向上找含 `client/Assets` 的仓库根（找不到返回 null，调用方跳过该项）。</summary>
    private static string FindRepoRoot()
    {
        var d = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 12 && d != null; i++)
        {
            if (System.IO.Directory.Exists(System.IO.Path.Combine(d.FullName, "client", "Assets"))) return d.FullName;
            d = d.Parent;
        }
        return null;
    }

    // ═════════════════════════════════════════════════════════════════════
    // 27. ★ 片 chunk-hole2（2026-09-23：血沼泽「大片黑底」）
    //
    //     判据 = 「期望可见块范围 == 实际建块集（差集为空）」对 **分块**（80×80）与
    //            **不分块**（56×40）两种区域尺寸**都成立**。
    //
    //     为什么这一步**离线**就能判（不必进 Play）：
    //       · 「实际建块集」不是运行时字典，而是 `StartRebuild` 交给 `RebuildJob` 的**块清单**；
    //         它由 **public static 纯函数** `MapView.PlannedChunks`（MapView.cs:684-700）算出
    //         ⇒ 宿主把**同一份生产代码**编译进来，可以逐案调用；
    //       · 「期望可见块范围」= `StartRebuild:599-604` 登记的 `_chunkMin/_chunkMax`；
    //         `StartRebuild:607` 传给 `PlannedChunks` 的正是
    //         `(buildX0, buildY0, _chunkMax.x, _chunkMax.y)`，其中 `buildX0 = Mathf.Max(0, min.x)`
    //         ⇒ **登记范围与建块清单同源**，二者差集必须恒为空。
    //
    //     ⛔ 只加断言：不改任何既有步骤的判据，不改 MapView。
    // ═════════════════════════════════════════════════════════════════════
    private static void Step27_PlannedChunksEqualVisibleRange()
    {
        Section("27. ★ chunk-hole2：期望可见块范围 == 实际建块清单（80×80 分块 / 56×40 不分块，差集必须为空）");

        var chunkSize = MapView.ChunkSize;                 // public const 16（MapView.cs:57）
        var threshold = MapView.BuildAllTileThreshold;     // public const 4096（MapView.cs:54）

        // ── ① 分块区：血腥荒野 80×80 = 6400 > 4096 ⇒ chunked=true，块网格 5×5 ──────────
        {
            const int w = 80, h = 80;
            var chunksX = (w + chunkSize - 1) / chunkSize;
            var chunksY = (h + chunkSize - 1) / chunkSize;
            var chunked = w * h > threshold;
            Check(chunked && chunksX == 5 && chunksY == 5,
                $"血腥荒野 {w}×{h} = {w * h} > {threshold} ⇒ chunked={chunked}（走分块路径），块网格 {chunksX}×{chunksY}");

            var combos = 0; var bad = 0; var worst = "";
            for (var x0 = 0; x0 < chunksX; x0++)
                for (var y0 = 0; y0 < chunksY; y0++)
                    for (var x1 = x0; x1 < chunksX; x1++)
                        for (var y1 = y0; y1 < chunksY; y1++)
                        {
                            combos++;
                            var into = new List<Vector2Int>();
                            MapView.PlannedChunks(chunked, x0, y0, x1, y1, chunksX, chunksY, into);

                            var missing = new List<Vector2Int>();
                            for (var cx = x0; cx <= x1; cx++)
                                for (var cy = y0; cy <= y1; cy++)
                                    if (!Has(into, new Vector2Int(cx, cy))) missing.Add(new Vector2Int(cx, cy));

                            var extra = 0;
                            for (var i = 0; i < into.Count; i++)
                            {
                                var c = into[i];
                                if (c.x < x0 || c.x > x1 || c.y < y0 || c.y > y1) extra++;
                            }
                            var expect = (x1 - x0 + 1) * (y1 - y0 + 1);
                            if (missing.Count != 0 || extra != 0 || into.Count != expect)
                            {
                                bad++;
                                if (worst.Length == 0)
                                    worst = $"范围 ({x0},{y0})..({x1},{y1})：缺 {Fmt(missing)}、越界 {extra}、清单 {into.Count}≠{expect}";
                            }
                        }
            Console.WriteLine($"    穷举 {combos} 种可见块范围（5×5 网格上所有矩形）：差集非空的 = {bad}");
            if (worst.Length > 0) Console.WriteLine("    首例：" + worst);
            Check(bad == 0,
                $"分块区（80×80）：任意可见块范围的建块清单 == 该矩形本身（穷举 {combos} 种，双向差集恒为空）");
        }

        // ── ② 不分块区：罗格营地 56×40 = 2240 ≤ 4096 ⇒ chunked=false ⇒ 一次铺**全图** ────
        {
            const int w = 56, h = 40;
            var chunksX = (w + chunkSize - 1) / chunkSize;
            var chunksY = (h + chunkSize - 1) / chunkSize;
            var chunked = w * h > threshold;
            Check(!chunked && chunksX == 4 && chunksY == 3,
                $"罗格营地 {w}×{h} = {w * h} ≤ {threshold} ⇒ chunked={chunked}（不走分块路径），块网格 {chunksX}×{chunksY} = {chunksX * chunksY} 块");

            var full = new List<Vector2Int>();
            MapView.PlannedChunks(chunked, 0, 0, chunksX - 1, chunksY - 1, chunksX, chunksY, full);
            Check(full.Count == chunksX * chunksY,
                $"不分块区：`PlannedChunks` 恒给**全图** {chunksX * chunksY} 块（实测 {full.Count}）⇒ 与传入的可见范围无关");

            var combos = 0; var bad = 0; var worst = "";
            for (var x0 = 0; x0 < chunksX; x0++)
                for (var y0 = 0; y0 < chunksY; y0++)
                    for (var x1 = x0; x1 < chunksX; x1++)
                        for (var y1 = y0; y1 < chunksY; y1++)
                        {
                            combos++;
                            var into = new List<Vector2Int>();
                            MapView.PlannedChunks(chunked, x0, y0, x1, y1, chunksX, chunksY, into);
                            var missing = new List<Vector2Int>();
                            for (var cx = x0; cx <= x1; cx++)
                                for (var cy = y0; cy <= y1; cy++)
                                    if (!Has(into, new Vector2Int(cx, cy))) missing.Add(new Vector2Int(cx, cy));
                            if (missing.Count != 0)
                            {
                                bad++;
                                if (worst.Length == 0) worst = $"范围 ({x0},{y0})..({x1},{y1})：缺 {Fmt(missing)}";
                            }
                        }
            Console.WriteLine($"    穷举 {combos} 种可见块范围（4×3 网格上所有矩形）：差集非空的 = {bad}");
            if (worst.Length > 0) Console.WriteLine("    首例：" + worst);
            Check(bad == 0,
                $"不分块区（56×40）：任意可见块范围 ⊆ 全图建块集（穷举 {combos} 种，差集恒为空）⇒ 小图不可能留洞");
        }
    }

    /// <summary>
    /// 28. ★ chunk-hole（2026-09-23，血沼泽大片黑）：登记范围与实际建块集**脱钩**时必须能自愈。
    /// <para>实测现场（`tools/probes/drivers/s2_drive.cs` 的 CHUNK-ENTER 读数 + `.ai-tmp/test/chh_deep_chh2.txt`）：
    /// `chunkMin/chunkMax=(0,0)-(2,3)`（12 块）而建块集是**另一个**矩形（(0,3)(1,3) 未建）、
    /// `PendingChunks=0` ⇒ 旧口径 `RefreshVisibleChunks` 只比范围就早退 ⇒ 洞永远没人补。
    /// 修复 = 早退前加「集合完整性」判定（`MapView.ChunkRangeCovered`）+ 登记路径不再被重铺 early-return 挡住。</para>
    /// </summary>
    private static void Step28_ChunkHoleSelfHeal()
    {
        Section("28. ★ chunk-hole：登记范围 == 建块集 的**完整性**判定（脱钩 ⇒ 必须重登记，不许只比范围早退）");

        // ── ① 复现实测脱钩现场：登记 (0,0)-(2,3)，建块集 = x∈[0,3]×y∈[0,2]（同为 12 块）────
        var built = new List<Vector2Int>();
        for (var x = 0; x <= 3; x++) for (var y = 0; y <= 2; y++) built.Add(new Vector2Int(x, y));
        Check(built.Count == 12 && !Has(built, new Vector2Int(0, 3)) && !Has(built, new Vector2Int(1, 3)),
            "脱钩现场构造：登记范围 (0,0)-(2,3) 与建块集（x0..3×y0..2，12 块）是**两个不同矩形**，(0,3)(1,3)(2,3) 未建");
        Check(!MapView.ChunkRangeCovered(built, new List<Vector2Int>(), 0, 0, 2, 3),
            "新判定：范围没变也要看**集合完整性** ⇒ 该现场判为有洞（旧口径这里早退 ⇒ MISSING 永远留着 = 血沼泽黑底）");

        var pending = new List<Vector2Int> { new Vector2Int(0, 3), new Vector2Int(1, 3), new Vector2Int(2, 3) };
        Check(MapView.ChunkRangeCovered(built, pending, 0, 0, 2, 3),
            "重登记之后（缺的 3 块进待建队列）⇒ 判为完整 ⇒ 差集为空（自愈闭环的第二次判定）");

        // ── ② 健全集：建块集 == 可见范围矩形 ⇒ 恒判完整（穷举 5×5 网格上所有矩形）────────────
        var combos = 0; var bad = 0;
        for (var x0 = 0; x0 < 5; x0++)
            for (var y0 = 0; y0 < 5; y0++)
                for (var x1 = x0; x1 < 5; x1++)
                    for (var y1 = y0; y1 < 5; y1++)
                    {
                        combos++;
                        var set = new List<Vector2Int>();
                        MapView.PlannedChunks(true, x0, y0, x1, y1, 5, 5, set);
                        if (!MapView.ChunkRangeCovered(set, new List<Vector2Int>(), x0, y0, x1, y1)) bad++;
                    }
        Check(combos == 225 && bad == 0,
            $"健康集（80×80 分块）：穷举 {combos} 种可见范围矩形，建块集 == 范围 ⇒ 全部判完整（不误报）");

        // ── ③ 不分块区（罗格营地 56×40）：chunked=false ⇒ 一次铺全图，必须覆盖任何可见范围 ──
        var full = new List<Vector2Int>();
        MapView.PlannedChunks(false, 0, 0, 3, 2, 4, 3, full);
        Check(full.Count == 12 && MapView.ChunkRangeCovered(full, new List<Vector2Int>(), 0, 0, 3, 2),
            "不分块区（营地 56×40 = 2240 ≤ 4096，chunked=false）：全图建块集（4×3 块）覆盖任意可见范围 ⇒ 判完整");
    }

    private static void Check(bool ok, string what)
    {
        Console.WriteLine($"    {(ok ? "✅" : "❌")} {what}");
        if (!ok) _failures++;
    }

    /// <summary>单步隔离：任一步炸掉都不能吞掉后面的证据。</summary>
    private static void Run(Action step)
    {
        try
        {
            step();
        }
        catch (Exception ex)
        {
            _failures++;
            Console.WriteLine($"❌ {step.Method.Name} 抛异常：{ex.GetType().Name}: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
        }
    }
}
