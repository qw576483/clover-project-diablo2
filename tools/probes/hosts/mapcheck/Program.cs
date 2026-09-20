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
                // 只影响渲染：这 49 格**依然是水 = 阻挡**（TileKind 与可走性一个字没动）
                if (town.TileAt(new Vector2Int(x, y)) == TileKind.Rock
                    && !town.Walkable(new Vector2Int(x, y))) blockedKept++;
            }
        }
        cols.Sort();
        Console.WriteLine($"  命中「平色水墙瓦片不叠」的格 = {hit} 格，列 x = {FmtInts(cols)}");
        Check(hit == 49, $"城镇图命中 **49 格**（河带 x=47 有 13 格 + x=54 有 36 格；实测 {hit}）");
        Check(wrongColumn == 0 && wrongObject == 0,
            $"命中格全在河带 x=47/54（越界列 = {wrongColumn}）且 object 键全是 moor_river/028（异常 = {wrongObject}）");
        Check(blockedKept == hit,
            $"这 {hit} 格**仍然是水 = 阻挡**（TileKind == Rock 且不可走 = {blockedKept}/{hit} ⇒ 只改渲染、不改逻辑）");

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
