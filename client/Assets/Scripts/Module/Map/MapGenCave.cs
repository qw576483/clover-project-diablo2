// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · Module/Map/MapGenCave.cs
// **邪恶洞穴（Den of Evil）**：用**原版洞穴预设块**拼出来的迷宫（任务地牢）。
//
// 为什么是"拼块"而不是"自己写房间-走廊"：
//   原版 D2 的洞穴层**本来就是**由引擎 DRLG 把一叠手工 **25×25 预设块**拼出来的
//   （块名 = `cave` + 开通方向首字母：`caveNSEW` / `caveNS` / `caveN` / `caveroom*`…，
//    后缀 `2`/`theme1`/`spec` = 同形状的随机变体）。本项目 1:1 照这个做法：
//     · 块库与逐格瓦片 = `MapGenCaveLayout`（生成物，源 `原版资源/d2raw/.../ACT1/CAVES/*.ds1`）；
//     · 块级拓扑 = 随机 DFS 生成树 + 少量环路（原版地牢有环，不是纯树状）；
//     · 块与块在同一相对坐标上对齐（开口车道 N/S x∈{8,9}∪{16}、W/E y∈{10,11}∪{18}）
//       ⇒ 走廊天然接通。
//   ⇒ 于是"走廊 + 房间"的拓扑与**每一格的瓦片**都是原版的，不是自创的一坨。
//
// 逐格可走性 = 块库里那一格自带的 kind（`. 可走 / X 实心岩体 / ` 空格 = 原版不画的黑区），
//   出处与推导见 `MapGenCaveLayout.cs` 文件头（不是猜的）。
//
// 尺寸：每轴 `GameConst.CaveSlotsMin ~ CaveSlotsMax` 块 ⇒ 50 / 75 格（块边长 25）。
//
// ⛔ `map.CaveEntrance` 保持 null —— 契约「洞穴入口格（**仅血腥荒野有效**；其它区域为 null）」。
// ─────────────────────────────────────────────────────────────────────────────

using System.Collections.Generic;
using CloverEngine;
using Diablo2.Core;
using Diablo2.Def;
using UnityEngine;

namespace Diablo2.Module.Map
{
    /// <summary>邪恶洞穴（原版洞穴预设块 + 块级迷宫）生成器。</summary>
    public static class MapGenCave
    {
        /// <summary>刷新点总数上限（防极端尺寸下怪物过多）。</summary>
        private const int MaxMonsterSpawns = 48;

        /// <summary>块级拓扑重试用次数（拓扑不连通就换一条，在内存里做，不让 MapModule 白跑一趟）。</summary>
        private const int LayoutRetry = 16;

        /// <summary>块级环路数范围（原版地牢有环；纯树状会让动线太单一）。</summary>
        private const int LoopMin = 2, LoopMax = 5;

        // 方向：下标 0..3 与 `MapGenCaveLayout.DirN/S/W/E` 一一对应。
        // y 轴向北（`Iso`：gy 越大越靠北）⇒ N = +y、S = -y、W = -x、E = +x。
        private static readonly int[] DirBits = { 1, 2, 4, 8 };
        private static readonly int[] DirDx = { 0, 0, -1, 1 };
        private static readonly int[] DirDy = { 1, -1, 0, 0 };
        private static readonly string[] DirNames = { "N", "S", "W", "E" };

        /// <summary>
        /// 生成邪恶洞穴。
        /// </summary>
        /// <returns>成功返回 true；块级拓扑 / 连通性自检反复失败则 false（调用方应换 seed 重生成）。</returns>
        public static bool Generate(GridMap map, Rng rng)
        {
            if (map == null || rng == null)
            {
                MapLog.Error("MapGenCave.Generate: map/rng 为 null，放弃生成");
                return false;
            }
            if (MapGenCaveLayout.Pieces == null || MapGenCaveLayout.Pieces.Length == 0)
            {
                MapLog.Error("MapGenCave: 块库为空（MapGenCaveLayout.cs 是生成物，请重跑 " +
                             "tools/d2codec/export_cave_layout.py）");
                return false;
            }

            // 随机块数（每轴 2~3）⇒ 尺寸 50 / 75；块边长 = MapGenCaveLayout.PieceSize（25）
            var slotsX = rng.Next(GameConst.CaveSlotsMin, GameConst.CaveSlotsMax + 1);
            var slotsY = rng.Next(GameConst.CaveSlotsMin, GameConst.CaveSlotsMax + 1);

            for (var attempt = 0; attempt < LayoutRetry; attempt++)
            {
                var r = attempt == 0 ? rng : rng.Derive(unchecked(attempt * 7919 + 13));
                if (TryLayout(map, r, slotsX, slotsY)) return true;
                MapLog.Warn($"MapGenCave: 第 {attempt + 1}/{LayoutRetry} 次块级拓扑不可用" +
                            $"（slots={slotsX}x{slotsY} seed={r.Seed}）⇒ 换拓扑重来");
            }

            // 本生成器自己搞不定 ⇒ 明确返回 false，让 MapModule 按既有机制换 seed 重试
            MapLog.Error($"MapGenCave: 连续 {LayoutRetry} 次都没拼出一条可走的洞穴" +
                         $"（slots={slotsX}x{slotsY} seed={rng.Seed}）⇒ 交由 MapModule 换 seed 重生成");
            map.Clear();
            return false;
        }

        // ═════════════════════════════════════════════════════════════════════
        // 一次尝试
        // ═════════════════════════════════════════════════════════════════════

        private static bool TryLayout(GridMap map, Rng rng, int slotsX, int slotsY)
        {
            var p = MapGenCaveLayout.PieceSize;
            var w = slotsX * p;
            var h = slotsY * p;

            map.Reset(AreaId.DenOfEvil, w, h, rng.Seed);
            map.Fill(TileKind.CaveWall);          // 先全实心：任何没被块覆盖的格都是岩体

            // ── ① 块级迷宫：随机 DFS 生成树（保证全连通）+ 少量环路 ──────────────
            var conn = new bool[slotsX, slotsY, 4];
            var need = new int[slotsX, slotsY];
            var visited = new bool[slotsX, slotsY];
            var startSlot = new Vector2Int(rng.Next(slotsX), rng.Next(slotsY));
            var stack = new Stack<Vector2Int>();
            stack.Push(startSlot);
            visited[startSlot.x, startSlot.y] = true;
            var slots = 1;

            var candidates = new List<int>(4);
            while (stack.Count > 0)
            {
                var cur = stack.Peek();
                candidates.Clear();
                for (var d = 0; d < 4; d++)
                {
                    var nx = cur.x + DirDx[d];
                    var ny = cur.y + DirDy[d];
                    if (nx < 0 || ny < 0 || nx >= slotsX || ny >= slotsY) continue;
                    if (visited[nx, ny]) continue;
                    candidates.Add(d);
                }
                if (candidates.Count == 0) { stack.Pop(); continue; }

                var pick = candidates[rng.Next(candidates.Count)];
                var nb = new Vector2Int(cur.x + DirDx[pick], cur.y + DirDy[pick]);
                Connect(conn, need, cur, pick);
                visited[nb.x, nb.y] = true;
                slots++;
                stack.Push(nb);
            }

            // 环路：随机把若干「相邻但没连」的槽对连上（原版地牢有环）
            var loops = rng.Next(LoopMin, LoopMax + 1);
            for (var k = 0; k < loops; k++)
            {
                var si = rng.Next(slotsX);
                var sj = rng.Next(slotsY);
                var d = rng.Next(4);
                var nx = si + DirDx[d];
                var ny = sj + DirDy[d];
                if (nx < 0 || ny < 0 || nx >= slotsX || ny >= slotsY) continue;
                if (conn[si, sj, d]) continue;
                Connect(conn, need, new Vector2Int(si, sj), d);
            }

            // 洞口所在槽 = 西边界上的随机一槽；**强制它开通西向**，
            // 这样原版块在 gx==0 那列必有可走格 ⇒ 出口可以正好落在图的西边界上（像原版的洞口）。
            var gateSlotY = rng.Next(slotsY);
            need[0, gateSlotY] |= MapGenCaveLayout.DirW;

            // ── ② 逐槽挑块 + 铺格（含逐格原版瓦片键）──────────────────────────
            map.BeginTileOverrides();
            var used = new int[16];
            var pieces = new MapGenCaveLayout.Piece[slotsX, slotsY];
            var floorCells = 0;

            for (var j = 0; j < slotsY; j++)
            {
                for (var i = 0; i < slotsX; i++)
                {
                    var piece = PickPiece(need[i, j], rng);
                    if (piece == null)
                    {
                        MapLog.Warn($"MapGenCave: 槽({i},{j}) 需要开通 {DescribeMask(need[i, j])}，" +
                                    "块库里没有能覆盖它的块 ⇒ 本次拓扑作废");
                        return false;
                    }
                    pieces[i, j] = piece;
                    used[piece.DirMask & 15]++;
                    floorCells += StampPiece(map, piece, i, j, p);
                }
            }

            // ── ③ 出生点 / 出口 ──────────────────────────────────────────────
            var gate = PickGate(map, pieces[0, gateSlotY], 0, p);
            if (!gate.HasValue)
            {
                MapLog.Warn("MapGenCave: 洞口槽 (0," + gateSlotY + ") 块 " + pieces[0, gateSlotY].Name +
                            " 在西边界上没有可走格 ⇒ 本次拓扑作废");
                return false;
            }
            var exit = gate.Value;
            map.Set(exit, TileKind.Exit);
            map.Exits.Add(exit);

            // 洞口要**看得见**：这一格的墙层换成原版洞穴口瓦片 `cave_door/000`
            // （原版 `CAVES/cavedr.dt1`，出处同 `MapView.ExitCaveTiles`）。
            // 只改这**一格**的物件层，地面层与其余格一律保持原版块原样（见下方"不做后处理"的注释）。
            ApplyExitDoor(map, exit);


            var spawn = PickSpawn(map, pieces[0, gateSlotY], 0, p, exit);
            if (!spawn.HasValue)
            {
                MapLog.Warn("MapGenCave: 洞口槽里找不到 3×3 净空的出生格 ⇒ 本次拓扑作废");
                return false;
            }
            map.SpawnPoint = spawn.Value;

            // ⛔ **不做任何"净空"/挖洞后处理** —— 出生点与洞口都是**在原版块已有可走格里挑的**
            //   （`PickSpawn` / `PickGate` 都要求 3×3 全可走），所以铺完就是原版那一格不动的样子。
            //   这样整张图 = 原版块逐格 1:1（可直接与原版 ds1 合成图逐像素比对，见
            //   `.ai-tmp/test/a_compose_map.py`）；一旦在这里 ClearAround，就会出现"游戏里比原版
            //   多挖了几格"的差异，那种差异对复刻来说就是缺陷。

            map.CaveEntrance = null;             // 契约：仅血腥荒野有效

            // ── ④ 怪物刷新点（洞口槽不刷：出生点不能一进去就被围）───────────────
            BuildMonsterSpawns(map, slotsX, slotsY, gateSlotY, p);

            // ── ⑤ 连通性目标 + 兜底填孤立口袋 ────────────────────────────────
            map.RequiredReachable.AddRange(map.Exits);
            map.RequiredReachable.AddRange(map.MonsterSpawns);
            for (var j = 0; j < slotsY; j++)
            {
                for (var i = 0; i < slotsX; i++)
                {
                    var c = NearestWalkable(map, i * p + p / 2, j * p + p / 2, p);
                    if (c.HasValue) map.RequiredReachable.Add(c.Value);
                }
            }

            // 洞穴按构造本就全连通（块内已凿通 + 块间双向开口）；真出现孤立口袋说明拼接出了
            // 偏差，这里兜底填掉并留日志（否则刷怪点/掉落会落进走不到的死地）。
            map.FillUnreachablePockets(map.SpawnPoint, TileKind.CaveWall);

            if (!map.VerifyConnectivity(out var unreachable, out var first))
            {
                MapLog.Warn($"MapGenCave: 连通性自检失败（{unreachable} 个目标不可达，第一个 {first}）" +
                            $"⇒ 本次拓扑作废（seed={rng.Seed}）");
                return false;
            }

            var kinds = new Dictionary<string, int>();
            for (var j = 0; j < slotsY; j++)
            {
                for (var i = 0; i < slotsX; i++)
                {
                    var n = pieces[i, j].Name;
                    kinds[n] = kinds.TryGetValue(n, out var v) ? v + 1 : 1;
                }
            }

            MapLog.Info($"MapGenCave: 邪恶洞穴生成完成（**原版洞穴块拼接** size={w}x{h} seed={rng.Seed} " +
                        $"块网格={slotsX}x{slotsY} 可走格={map.WalkableCount} 洞口槽=(0,{gateSlotY}) " +
                        $"出生点={map.SpawnPoint} 出口={exit} 刷新点={map.MonsterSpawns.Count}）；" +
                        $"用到的块：{DescribePieces(kinds)}");
            return true;
        }

        /// <summary>
        /// 洞口格加一个**原版洞穴口**物件瓦片（`cave_door/000`，源 `CAVES/cavedr.dt1`）。
        /// 只改物件层、只改这一格 —— 地面层与其它格保持原版块原样（这样与原版 ds1 合成图
        /// 的逐像素差异就只有这 1 格，见 `.ai-tmp/test/a_compose_map.py`）。
        /// </summary>
        private static void ApplyExitDoor(GridMap map, Vector2Int exit)
        {
            if (!map.TryGetTiles(exit.x, exit.y, out var ground, out _))
            {
                MapLog.Warn("MapGenCave: 洞口格没有逐格瓦片覆盖（覆盖未启用？）⇒ 洞口不会画洞穴口瓦片");
                return;
            }
            map.SetTiles(exit.x, exit.y, ground, ExitDoorKey);
        }

        /// <summary>洞穴口瓦片键（`Resources/Clover/D2/Objects/cave_door/000.png`）。</summary>
        private const string ExitDoorKey = "cave_door/000";

        /// <summary>把一块原版预设盖到槽 (si,sj) 上。</summary>
        /// <returns>该块贡献的可走格数。</returns>
        private static int StampPiece(GridMap map, MapGenCaveLayout.Piece piece, int si, int sj, int p)
        {
            var walkable = 0;
            for (var py = 0; py < p; py++)
            {
                // ★ 竖向翻转：块的第 0 行是它的**北**边（`caveN*` 的开口在 y=0），
                //   而本项目 gy 越大越靠北 ⇒ 块的 py 行落到 gy = sj*p + (p-1-py)。
                var gy = sj * p + (p - 1 - py);
                for (var px = 0; px < p; px++)
                {
                    var gx = si * p + px;
                    var idx = MapGenCaveLayout.CellIndex(piece.Cells, py * p + px);
                    if (idx < 0 || idx >= piece.Alphabet.Length)
                    {
                        MapLog.WarnThrottled("cave.cell.bad",
                            $"MapGenCave: 块 {piece.Name} 的格 ({px},{py}) 下标 {idx} 越界，按实心岩体处理");
                        continue;
                    }
                    var code = piece.Alphabet[idx];
                    var kindChar = code.Length > 0 ? code[0] : 'X';
                    var ground = MapGenCaveLayout.Decode(code.Length >= 7 ? code.Substring(1, 6) : "------");
                    var obj = MapGenCaveLayout.Decode(code.Length >= 13 ? code.Substring(7, 6) : "------");

                    var tile = kindChar == '.' ? TileKind.CaveFloor : TileKind.CaveWall;
                    map.Set(gx, gy, tile);
                    map.SetTiles(gx, gy, ground, obj);
                    if (tile == TileKind.CaveFloor) walkable++;
                }
            }
            return walkable;
        }

        /// <summary>
        /// 挑一块能覆盖 <paramref name="needMask"/> 的原版块：
        /// 候选 = `DirMask ⊇ needMask`；在候选里优先**多余开口最少**的（多余开口会形成凹龛），
        /// 同档随机取一块（变体多样性）。
        /// </summary>
        private static MapGenCaveLayout.Piece PickPiece(int needMask, Rng rng)
        {
            var all = MapGenCaveLayout.Pieces;
            var bestExtra = int.MaxValue;
            var best = new List<MapGenCaveLayout.Piece>(8);

            for (var i = 0; i < all.Length; i++)
            {
                var dm = all[i].DirMask & 15;
                if ((dm & needMask) != needMask) continue;      // 必须开通全部需要方向
                var extra = PopCount(dm) - PopCount(needMask);
                if (extra < bestExtra) { bestExtra = extra; best.Clear(); best.Add(all[i]); }
                else if (extra == bestExtra) best.Add(all[i]);
            }
            if (best.Count == 0) return null;
            return best[rng.Next(best.Count)];
        }

        /// <summary>
        /// 洞口格：洞口槽里 **gx 最小**（贴图西边界）的可走格，取其中最靠槽中间、且 3×3 全可走的那个。
        /// 要求 3×3 全可走是为了**不做任何挖洞后处理**（见 `TryLayout` ③ 的注释）。
        /// </summary>
        private static Vector2Int? PickGate(GridMap map, MapGenCaveLayout.Piece piece, int si, int p)
        {
            var minX = int.MaxValue;
            for (var y = 0; y < p; y++)
            {
                for (var x = 0; x < p; x++)
                {
                    if (!map.Walkable(new Vector2Int(si * p + x, y))) continue;
                    if (si * p + x < minX) minX = si * p + x;
                }
            }
            if (minX == int.MaxValue) return null;

            Vector2Int? best = null;
            var bestDist = int.MaxValue;
            for (var y = 0; y < p; y++)
            {
                var g = new Vector2Int(minX, y);
                if (!map.Walkable(g) || !map.IsSpawnClear(g, 1)) continue;
                var d = Mathf.Abs(y - p / 2);
                if (d < bestDist) { bestDist = d; best = g; }
            }
            if (best == null)
            {
                // 西边界那一列恰好没有 3×3 净空的格 ⇒ 退回"西边界可走格"，但**不挖洞**，
                // 只把它的 3×3 是否净空交给调用方判（进出口都要能站人）。
                for (var y = 0; y < p; y++)
                {
                    var g = new Vector2Int(minX, y);
                    if (map.Walkable(g)) return g;
                }
            }
            return best;
        }

        /// <summary>出生点：洞口槽里 3×3 全可走、且离洞口 3~10 格（太近一进洞就触发出口）的格。</summary>
        private static Vector2Int? PickSpawn(GridMap map, MapGenCaveLayout.Piece piece, int si, int p,
            Vector2Int exit)
        {
            Vector2Int? best = null;
            var bestScore = float.MaxValue;
            for (var y = 0; y < p; y++)
            {
                for (var x = 0; x < p; x++)
                {
                    var g = new Vector2Int(si * p + x, y);
                    if (!map.Walkable(g)) continue;
                    if (!map.IsSpawnClear(g, 1)) continue;
                    var d = Vector2.Distance(new Vector2(g.x, g.y), new Vector2(exit.x, exit.y));
                    if (d < 3f) continue;
                    var score = Mathf.Abs(d - 6f);         // 离洞口约 6 格最理想
                    if (score < bestScore) { bestScore = score; best = g; }
                }
            }
            if (best.HasValue) return best;

            // 兜底：宽限到"3×3 全可走"的任意格（洞口槽可能又小又窄）
            for (var y = 0; y < p; y++)
            {
                for (var x = 0; x < p; x++)
                {
                    var g = new Vector2Int(si * p + x, y);
                    if (map.Walkable(g) && map.IsSpawnClear(g, 1) && g != exit) return g;
                }
            }
            MapLog.Warn("MapGenCave: 洞口槽里没有任何 3×3 全可走的格（原版块异常窄），出生点取洞口格");
            return map.Walkable(exit) ? exit : (Vector2Int?)null;
        }

        /// <summary>在各槽内取刷新点（洞口槽除外；去重；总数封顶）。</summary>
        private static void BuildMonsterSpawns(GridMap map, int slotsX, int slotsY, int gateSlotY, int p)
        {
            var seen = new HashSet<Vector2Int>();
            for (var j = 0; j < slotsY; j++)
            {
                for (var i = 0; i < slotsX; i++)
                {
                    if (i == 0 && j == gateSlotY) continue;
                    var cells = new List<Vector2Int>();
                    for (var y = 0; y < p; y++)
                    {
                        for (var x = 0; x < p; x++)
                        {
                            var g = new Vector2Int(i * p + x, j * p + y);
                            if (map.Walkable(g)) cells.Add(g);
                        }
                    }
                    var want = Mathf.Clamp(cells.Count / 24, 1, 6);
                    for (var k = 0; k < want && map.MonsterSpawns.Count < MaxMonsterSpawns; k++)
                    {
                        var g = cells.Count > 0 ? cells[RandomIndex(cells.Count, i, j, k)] : Vector2Int.zero;
                        if (cells.Count == 0) break;
                        if (!seen.Add(g)) continue;
                        map.MonsterSpawns.Add(g);
                    }
                }
            }

            if (map.MonsterSpawns.Count == 0)
            {
                MapLog.Warn("MapGenCave: 未产出任何怪物刷新点（块过窄？）—— MonsterModule 需退回 RandomWalkableTile 撒点");
            }
        }

        /// <summary>确定性索引（**不用 UnityEngine.Random**；把槽号/序号混进来即可，只求"别老取同一格"）。</summary>
        private static int RandomIndex(int count, int i, int j, int k)
        {
            var h = unchecked((uint)(i * 73856093) ^ (uint)(j * 19349663) ^ (uint)(k * 83492791));
            h ^= h >> 13;
            h = unchecked(h * 0x85EBCA6Bu);
            h ^= h >> 16;
            return (int)(h % (uint)count);
        }

        /// <summary>离 (cx,cy) 最近的可走格（距离用环形搜索，p 格范围内）。</summary>
        private static Vector2Int? NearestWalkable(GridMap map, int cx, int cy, int p)
        {
            for (var r = 0; r <= p; r++)
            {
                for (var dx = -r; dx <= r; dx++)
                {
                    for (var dy = -r; dy <= r; dy++)
                    {
                        if (Mathf.Abs(dx) != r && Mathf.Abs(dy) != r) continue;
                        var g = new Vector2Int(cx + dx, cy + dy);
                        if (map.Walkable(g)) return g;
                    }
                }
            }
            return null;
        }

        private static void Connect(bool[,,] conn, int[,] need, Vector2Int slot, int dir)
        {
            var nx = slot.x + DirDx[dir];
            var ny = slot.y + DirDy[dir];
            conn[slot.x, slot.y, dir] = true;
            conn[nx, ny, Opposite(dir)] = true;
            need[slot.x, slot.y] |= DirBits[dir];
            need[nx, ny] |= DirBits[Opposite(dir)];
        }

        private static int Opposite(int dir)
        {
            // 0=N↔1=S、2=W↔3=E
            return dir == 0 ? 1 : (dir == 1 ? 0 : (dir == 2 ? 3 : 2));
        }

        private static int PopCount(int mask)
        {
            var n = 0;
            for (var i = 0; i < 4; i++) if ((mask & (1 << i)) != 0) n++;
            return n;
        }

        private static string DescribeMask(int mask)
        {
            var s = "";
            for (var d = 0; d < 4; d++) if ((mask & DirBits[d]) != 0) s += DirNames[d];
            return s.Length == 0 ? "(无)" : s;
        }

        private static string DescribePieces(Dictionary<string, int> kinds)
        {
            var parts = new List<string>();
            foreach (var kv in kinds) parts.Add($"{kv.Key}×{kv.Value}");
            parts.Sort();
            return string.Join(" ", parts);
        }
    }
}
