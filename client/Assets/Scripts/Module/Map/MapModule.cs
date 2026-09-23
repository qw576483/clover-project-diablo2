// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · Module/Map/MapModule.cs
// `IMapModule` 的**唯一实现**（门面，internal）：持有 `GridMap`（数据）+ `MapView`（渲染）。
//
// 职责：
//   · 生成（含**可复现随机**、**出生点净空**、**连通性自检**、失败重试、保底布局）
//   · 可走查询 / A* 寻路转发 / 随机可走格
//   · 渲染入口（`ShowArea`）与怪物刷新点 / NPC 点 / 出口 / 洞穴入口的对外暴露
//   · 发 `Events.MapGenerated`（小地图）与 `Events.AreaChanged`
//   · 订阅 `Events.PlayerGridChanged` → 揭迷雾 / 记已探索
//
// ⛔ 契约（`Module/Contracts.cs` 的 `IMapModule`）冻结：本文件**不新增/不改**接口签名。
// ⛔ 不重写 `CloverEngine.{AStar,IsoLayout}` 与 `CloverEngine.Rng`；不碰 `Game.Map`（引擎地图，本项目不使用）。
// ─────────────────────────────────────────────────────────────────────────────

using System.Collections.Generic;
using CloverEngine;
using Diablo2.Core;
using Diablo2.Def;
using UnityEngine;

namespace Diablo2.Module.Map
{
    /// <summary>格子地图门面实现（三处区域：罗格营地 / 血腥荒野 / 邪恶洞穴）。</summary>
    internal sealed class MapModule : IMapModule
    {
        /// <summary>重试换 seed 的步长（质数：让相邻尝试的随机序列充分打散）。</summary>
        private const int RetrySeedStep = 7919;

        private readonly GridMap _grid = new GridMap();
        private MapView _view;
        private Transform _rootOverride;
        private bool _exploreSubscribed;

        // ═════════════════════════════════════════════════════════════════════
        // IMapModule：只读状态
        // ═════════════════════════════════════════════════════════════════════

        /// <inheritdoc />
        public int Width { get { return _grid.Width; } }

        /// <inheritdoc />
        public int Height { get { return _grid.Height; } }

        /// <inheritdoc />
        public AreaId Area { get { return _grid.Area; } }

        /// <inheritdoc />
        public int Seed { get { return _grid.Seed; } }

        /// <inheritdoc />
        public int BlockedCount { get { return _grid.BlockedCount; } }

        /// <inheritdoc />
        public int WalkableCount { get { return _grid.WalkableCount; } }

        /// <inheritdoc />
        public bool IsGenerated { get { return _grid.Generated; } }

        /// <inheritdoc />
        public Vector2Int SpawnPoint { get { return _grid.SpawnPoint; } }

        /// <inheritdoc />
        public IReadOnlyList<Vector2Int> Exits { get { return _grid.Exits; } }

        /// <inheritdoc />
        public Vector2Int? CaveEntrance { get { return _grid.CaveEntrance; } }

        /// <inheritdoc />
        public IReadOnlyList<Vector2Int> NpcPoints { get { return _grid.NpcPoints; } }

        /// <inheritdoc />
        public IReadOnlyList<Vector2Int> MonsterSpawns { get { return _grid.MonsterSpawns; } }

        /// <inheritdoc />
        public IReadOnlyList<Vector2Int> WaypointPoints { get { return _grid.WaypointPoints; } }

        // ═════════════════════════════════════════════════════════════════════
        // IMapModule：查询
        // ═════════════════════════════════════════════════════════════════════

        /// <inheritdoc />
        public bool InBounds(Vector2Int g) { return _grid.InBounds(g); }

        /// <inheritdoc />
        public bool Walkable(Vector2Int g) { return _grid.Walkable(g); }

        /// <inheritdoc />
        public TileKind TileAt(Vector2Int g) { return _grid.TileAt(g); }

        /// <summary>
        /// 「该格是否是可走上方的结构（桥面/平台）」—— 契约见 `Module/Contracts.cs` 的
        /// `IMapModule.IsDeckGrid`（2026-09-22 为修「营地出门的桥，还是从桥下走」新增）。
        /// <para>实现 = **从 `GridMap` 转发**（登记口径 = 该格地面瓦片键取自 deck 类包 `moor_bridge`
        /// **且该格可走**，见 `DeckTiles` / `GridMap.SetTiles` 的登记点；⛔ 视图层不猜几何）。
        /// 图外 / 未生成 / 未登记 ⇒ false。</para>
        /// </summary>
        public bool IsDeckGrid(Vector2Int g) { return _grid.IsDeck(g); }

        /// <inheritdoc />
        public List<Vector2Int> FindPath(Vector2Int from, Vector2Int to)
        {
            if (!_grid.Generated)
            {
                MapLog.Error($"FindPath: 地图未生成（from={from} to={to}），返回 null");
                return null;
            }
            return _grid.FindPath(from, to);
        }

        /// <inheritdoc />
        public Vector2Int RandomWalkableTile(Rng rng) { return _grid.RandomWalkableTile(rng); }

        // ═════════════════════════════════════════════════════════════════════
        // 生成
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>
        /// 生成地图（**同 seed ⇒ 同地图**）。
        /// 每次尝试都：生成 → 出生点净空校验 → 连通性 BFS 校验；失败则换 seed 重试，
        /// 超过 `GameConst.MapGenMaxRetry` 次改用**保底布局**（并 Warn）。
        /// </summary>
        public void Generate(AreaId area, int seed)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();

            var ok = false;
            var attempts = 0;
            string reason = null;

            for (var attempt = 0; attempt < GameConst.MapGenMaxRetry; attempt++)
            {
                attempts = attempt + 1;
                // 第 1 次用调用方给的 seed；之后派生出确定性的新 seed（**同样可复现**）
                var effectiveSeed = attempt == 0 ? seed : unchecked(seed + attempt * RetrySeedStep);
                var rng = new Rng(effectiveSeed);

                if (TryBuild(area, rng, out reason))
                {
                    ok = true;
                    break;
                }

                MapLog.Warn($"Generate 第 {attempts}/{GameConst.MapGenMaxRetry} 次失败（seed={effectiveSeed}）：" +
                            $"{reason} ⇒ 换 seed 重生成");
            }

            if (!ok)
            {
                MapLog.Warn($"Generate: 连续 {GameConst.MapGenMaxRetry} 次失败（area={area} seed={seed}），" +
                            $"改用**保底布局**（保证可玩：开阔地形 + 1 个出口 + 干净出生点）");
                BuildFallback(area, seed);
            }

            sw.Stop();
            _grid.LastGenerateMs = sw.Elapsed.TotalMilliseconds;
            _grid.GenerateAttempts = attempts;
            _grid.MarkGenerated();

            SubscribeExplore();

            MapLog.Info($"Generate 完成：area={area}({MapLog.AreaLabel(area)}) requestedSeed={seed} " +
                        $"effectiveSeed={_grid.Seed} attempts={attempts} size={_grid.Width}x{_grid.Height} " +
                        $"blocked={_grid.BlockedCount} walkable={_grid.WalkableCount} timeMs={_grid.LastGenerateMs:F1}");

            MapDebug.ReportAfterGenerate(_grid);
            EmitMapGenerated();
        }

        /// <summary>一次尝试：生成 + 净空 + 连通性。任一步失败即返回 false（附原因，供重试日志）。</summary>
        private bool TryBuild(AreaId area, Rng rng, out string reason)
        {
            switch (area)
            {
                case AreaId.Town:
                    MapGenTown.Generate(_grid, rng);
                    break;

                case AreaId.BloodMoor:
                    MapGenWilderness.Generate(_grid, rng);
                    break;

                case AreaId.DenOfEvil:
                    if (!MapGenCave.Generate(_grid, rng))
                    {
                        reason = "房间-走廊算法放不下足够的房间";
                        return false;
                    }
                    break;

                default:
                    MapLog.Error($"TryBuild: 未登记的区域 {(int)area}（契约只定义 Town/BloodMoor/DenOfEvil）");
                    reason = $"未登记的区域 {(int)area}";
                    return false;
            }

            // ② 出生点净空：出生格 + 8 邻必须可走（否则角色一出生就被卡死）
            if (!_grid.IsSpawnClear(_grid.SpawnPoint, 1))
            {
                reason = $"出生点 {_grid.SpawnPoint} 的 3×3 邻域不可走";
                return false;
            }

            // ③ 连通性：出生点必须可达 出口 / 洞穴入口 / 所有房间 / 所有刷怪点
            if (!_grid.VerifyConnectivity(out var unreachable, out var first))
            {
                reason = $"{unreachable} 个目标不可达（第一个 {first}）";
                return false;
            }

            reason = null;
            return true;
        }

        /// <summary>
        /// 保底布局（连通性重试全失败时的最后手段）：一块开阔地形 + 周围不可走边界 +
        /// 1~2 个出口 + 干净的出生点。**永远能通过自检**，保证游戏不会卡在「进不去图」。
        /// </summary>
        private void BuildFallback(AreaId area, int seed)
        {
            var rng = new Rng(unchecked(seed ^ 0x5F5F5F5F));
            var w = Mathf.Clamp(32, GameConst.MapMinSize, GameConst.MapMaxSize);
            var h = w;
            _grid.Reset(area, w, h, rng.Seed);

            var floor = area == AreaId.DenOfEvil ? TileKind.CaveFloor
                      : area == AreaId.Town ? TileKind.TownFloor
                      : TileKind.Dirt;
            var wall = area == AreaId.DenOfEvil ? TileKind.CaveWall : TileKind.Rock;
            _grid.Fill(floor);

            for (var x = 0; x < w; x++)
            {
                _grid.Set(x, 0, wall);
                _grid.Set(x, h - 1, wall);
            }
            for (var y = 0; y < h; y++)
            {
                _grid.Set(0, y, wall);
                _grid.Set(w - 1, y, wall);
            }

            _grid.SpawnPoint = new Vector2Int(w / 2, h / 2);
            _grid.ClearAround(_grid.SpawnPoint, floor, 2);

            var northDoor = new Vector2Int(w / 2, 0);
            _grid.Set(northDoor, TileKind.Exit);
            _grid.Exits.Add(northDoor);
            _grid.Set(w / 2 - 1, 1, floor);
            _grid.Set(w / 2, 1, floor);
            _grid.Set(w / 2 + 1, 1, floor);

            if (area == AreaId.BloodMoor)
            {
                var southDoor = new Vector2Int(w / 2, h - 1);
                _grid.Set(southDoor, TileKind.Exit);
                _grid.Exits.Add(southDoor);
                _grid.CaveEntrance = southDoor;
                _grid.Set(w / 2 - 1, h - 2, floor);
                _grid.Set(w / 2, h - 2, floor);
                _grid.Set(w / 2 + 1, h - 2, floor);
            }

            _grid.RequiredReachable.AddRange(_grid.Exits);
            _grid.CaveEntrance = area == AreaId.BloodMoor ? _grid.CaveEntrance : null;

            MapLog.Warn($"BuildFallback: 已铺设保底布局（area={area} size={w}x{h} seed={_grid.Seed} " +
                        $"出口={_grid.Exits.Count} 出生点={_grid.SpawnPoint}）");
        }

        /// <inheritdoc />
        public void Clear()
        {
            UnsubscribeExplore();
            if (_view != null) _view.Clear();
            _grid.Clear();
            MapLog.Info("Clear: 地图数据 / 渲染 / 已探索记录全部清空（退出 Stage）");
        }

        // ═════════════════════════════════════════════════════════════════════
        // 渲染
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>
        /// 二次调用：让渲染节点挂到场景里已有的「地图根」上（`Stage` 场景自带，
        /// 见 `docs/步骤文档.md` §3.6）。**本项目新增的非契约方法**；不调也能跑
        /// （那时 `ShowArea` 会自己 `new GameObject("MapRoot")`）。
        /// </summary>
        public void AttachRoot(Transform root)
        {
            _rootOverride = root;
            if (_view != null && root != null) _view.transform.SetParent(root, false);
        }

        private void EnsureView()
        {
            if (_view != null) return;    // Unity 的 == 会把「已销毁的对象」当 null，正好用于场景重载后的重建

            var go = new GameObject("MapRoot");
            if (_rootOverride != null) go.transform.SetParent(_rootOverride, false);
            _view = go.AddComponent<MapView>();
            _view.Bind(_grid);
            MapLog.Info($"EnsureView: 创建 MapRoot + MapView（三层等距渲染）" +
                        $"{(_rootOverride != null ? "，挂在场景地图根下" : "，根节点由本模块创建")}");
        }

        /// <inheritdoc />
        public void ShowArea(AreaId area)
        {
            if (!_grid.Generated)
            {
                MapLog.Error($"ShowArea({area}): 地图尚未生成（先调 Generate），本次不渲染");
                return;
            }
            EnsureView();
            _view.ShowArea(area);
        }

        /// <summary>战争迷雾开关（转 `MapView`；**非契约方法**，`IMapModule` 上没有）。</summary>
        public void SetFogOfWar(bool on)
        {
            if (_view == null)
            {
                MapLog.Warn($"SetFogOfWar({on}): 渲染层尚未创建（先 ShowArea），本次忽略");
                return;
            }
            _view.SetFogOfWar(on);
        }

        /// <summary>标记某格已探索（**非契约方法**；正常情况下由 `Events.PlayerGridChanged` 自动驱动）。</summary>
        public void MarkExplored(Vector2Int g)
        {
            if (_view != null) _view.MarkExplored(g);
        }

        /// <summary>该格是否已探索（**非契约方法**）。</summary>
        public bool IsExplored(Vector2Int g)
        {
            return _view != null && _view.IsExplored(g);
        }

        /// <summary>上次生成实际尝试次数（1 = 一次成功；&gt;1 = 触发过重试；**非契约方法**）。</summary>
        public int GenerateAttempts { get { return _grid.GenerateAttempts; } }

        /// <summary>上次生成耗时（毫秒，含全部重试；**非契约方法**）。</summary>
        public double LastGenerateMs { get { return _grid.LastGenerateMs; } }

        /// <summary>自证：多行统计块（`MapDebug.DumpStats`）。</summary>
        public string DumpStats() { return MapDebug.DumpStats(_grid); }

        /// <summary>自证：可走性字符画（`maxRows &lt;= 0` = 全量）。</summary>
        public string DumpAscii(int maxRows = 0) { return MapDebug.DumpAscii(_grid, maxRows); }

        /// <summary>自证：地形哈希（同 seed 两次生成必须一致）。</summary>
        public string Hash() { return MapDebug.Hash(_grid); }

        /// <summary>
        /// 自证：本图是否启用了「逐格原版瓦片键」（罗格营地 / 邪恶洞穴 = true，野外 = false）。
        /// **非契约方法**（`IMapModule` 上没有）。
        /// </summary>
        public bool HasTileOverrides => _grid.HasTileOverrides;

        /// <summary>自证：取某格的**原版瓦片键**（空串 = 原版这格不画）。**非契约方法**。</summary>
        public bool TryGetTileKeys(int x, int y, out string groundKey, out string objectKey)
            => _grid.TryGetTiles(x, y, out groundKey, out objectKey);

        /// <summary>
        /// 自证：本模块持有的 `GridMap`（**非契约方法**，`IMapModule` 上没有）。
        /// <para>用途 = 离线自检宿主能拿**生产同一份**格数据去复算 `MapView.PlanCell`（T0FIX-H 的
        /// "逐格判定同源 / 不露空"断言必须跑在真实地图上，而不是宿主自己再生成一张）；⛔ 业务代码不要用它。</para>
        /// </summary>
        public GridMap Grid { get { return _grid; } }

        /// <summary>自证：全图「可走格」的连通片数（>1 = 存在走不到的孤立区）。**非契约方法**。</summary>
        public int CountWalkableComponents()
        {
            if (!_grid.Generated) return 0;
            var w = _grid.Width;
            var h = _grid.Height;
            var seen = new bool[w, h];
            var comps = 0;
            for (var y = 0; y < h; y++)
            {
                for (var x = 0; x < w; x++)
                {
                    if (seen[x, y] || !_grid.Walkable(new Vector2Int(x, y))) continue;
                    comps++;
                    var stack = new Stack<Vector2Int>();
                    stack.Push(new Vector2Int(x, y));
                    seen[x, y] = true;
                    while (stack.Count > 0)
                    {
                        var c = stack.Pop();
                        for (var d = 0; d < 4; d++)
                        {
                            var n = new Vector2Int(c.x + (d == 0 ? 1 : d == 1 ? -1 : 0),
                                                   c.y + (d == 2 ? 1 : d == 3 ? -1 : 0));
                            if (!_grid.InBounds(n) || seen[n.x, n.y]) continue;
                            if (!_grid.Walkable(n)) continue;
                            seen[n.x, n.y] = true;
                            stack.Push(n);
                        }
                    }
                }
            }
            return comps;
        }

        // ═════════════════════════════════════════════════════════════════════
        // 小地图 / 事件
        // ═════════════════════════════════════════════════════════════════════

        /// <inheritdoc />
        public MinimapArgs BuildMinimap()
        {
            var args = new MinimapArgs
            {
                areaId = (int)_grid.Area,
                width = _grid.Width,
                height = _grid.Height,
                seed = _grid.Seed,
                playerX = _grid.SpawnPoint.x,
                playerY = _grid.SpawnPoint.y,
            };

            for (var y = 0; y < _grid.Height; y++)
            {
                for (var x = 0; x < _grid.Width; x++) args.tiles.Add(CodeOf(_grid.Get(x, y)));
            }

            // ★ 逐格 **原版 automap Cel**（`AutoMapCel.generated.cs` = 原版 `AutoMap.txt` + `MaxiMap.dc6`
            //   + ACT1 调色板 + 原版 DS1 的解析产物；口径见 `MinimapArgs.cels` 的 `# contract:` 注释）：
            //   按该格的**原版瓦片键**（`GridMap.TryGetTiles`，罗格营地/洞穴/野外三个生成器都逐格登记）
            //   查表。查不到键 ⇒ `-1`（原版这一格不画 automap）。
            //   ⚠️ 非预期分支：本图**没有**逐格瓦片覆盖（生成失败的保底布局 `BuildFallback`）⇒ 整幅
            //      查不到 Cel，自动地图会是空的 —— 留一次 Warn（不静默），并在回报里点名。
            var celOk = 0;
            for (var y = 0; y < _grid.Height; y++)
            {
                for (var x = 0; x < _grid.Width; x++)
                {
                    short g = -1, o = -1;
                    string gk, ok;
                    if (_grid.TryGetTiles(x, y, out gk, out ok))
                    {
                        g = AutoMapCel.Cel((int)_grid.Area, false, gk);
                        // ★ 片 L / R12：`Objects/moor_river/028` 是**原版平色水墙瓦片**，它在
                        //   `AutoMapCel` 的物件表里和石墙（`moor_stonewall/*`）映射到**同一个 Cel 60**
                        //   ⇒ 水格在小地图上看着就是石头，这正是 R12 报的"小地图分不出水与石头"。
                        //   判据与主视图 R1-B **完全同一条**（`MapView.IsPaletteCycledFlatWallOverlay`）：
                        //   它不是墙、是水面，水面已由 floor 层的水 Cel 呈现 ⇒ 物件层在这个 Cel 上**不叠**。
                        //   ⛔ 只影响小地图画不画这一张物件；`TileKind` / 可走性 / 逐格键一个字不动。
                        o = MapView.IsPaletteCycledFlatWallOverlay(gk, ok)
                            ? AutoMapCel.None
                            : AutoMapCel.Cel((int)_grid.Area, true, ok);
                        if (g >= 0) celOk++;
                    }
                    args.cels.Add(g);
                    args.celsOver.Add(o);
                }
            }
            if (celOk == 0)
            {
                MapLog.WarnOnce("minimap.no.cel",
                    $"BuildMinimap: area={_grid.Area} 整幅一格 automap Cel 都没有（逐格原版瓦片键缺失？" +
                    "保底布局不走逐格覆盖）⇒ 自动地图只有玩家点与标记，没有地图图形");
            }

            for (var i = 0; i < _grid.Exits.Count; i++) AddMarker(args, _grid.Exits[i], MinimapArgs.TileExit);
            for (var i = 0; i < _grid.NpcPoints.Count; i++) AddMarker(args, _grid.NpcPoints[i], MinimapArgs.TileInteractable);

            MapLog.Info($"BuildMinimap: area={_grid.Area} {args.width}x{args.height} " +
                        $"tiles={args.tiles.Count} markers={args.markerX.Count}");
            return args;
        }

        private static void AddMarker(MinimapArgs args, Vector2Int g, byte kind)
        {
            args.markerX.Add(g.x);
            args.markerY.Add(g.y);
            args.markerKind.Add(kind);
        }

        private static byte CodeOf(TileKind kind)
        {
            if (kind == TileKind.Void) return MinimapArgs.TileVoid;
            if (kind == TileKind.Exit) return MinimapArgs.TileExit;
            // 注意：NPC 点会在 BuildMinimap 里被 marker 单独标出（面板画在交互层）
            // ★ 片 L / R12：`TileKind.Water` **显式登记**为阻挡（水不可涉水）—— 它落到 `TileBlocking`
            //   是**判定出来的**，不是"忘了登记掉到 default"。
            //   ⚠️ 真正的"水 / 石头可区分"发生在 `BuildMinimap` 的物件 Cel 那一层（见那里的 ★ 注释）：
            //      面板画的是**原版逐格 automap Cel**，本数组只用于日志计数，不参与画面。
            if (kind == TileKind.Water) return MinimapArgs.TileBlocking;
            return TileKindInfo.IsWalkable(kind) ? MinimapArgs.TileWalkable : MinimapArgs.TileBlocking;
        }

        private void EmitMapGenerated()
        {
            if (Game.Event == null)
            {
                MapLog.Error("EmitMapGenerated: Game.Event 为 null（Game.Launch 未调用？），跳过事件派发");
                return;
            }
            Game.Event.Emit(Events.MapGenerated, BuildMinimap());
            Game.Event.Emit(Events.AreaChanged, _grid.Area);
        }

        private void SubscribeExplore()
        {
            if (_exploreSubscribed) return;
            if (Game.Event == null)
            {
                MapLog.Error("SubscribeExplore: Game.Event 为 null（Game.Launch 未调用？），迷雾不会自动揭开");
                return;
            }
            Game.Event.On<Vector2Int>(Events.PlayerGridChanged, OnPlayerGridChanged);
            _exploreSubscribed = true;
        }

        private void UnsubscribeExplore()
        {
            if (!_exploreSubscribed) return;
            if (Game.Event != null) Game.Event.Off<Vector2Int>(Events.PlayerGridChanged, OnPlayerGridChanged);
            _exploreSubscribed = false;
        }

        /// <summary>`Events.PlayerGridChanged` 回调（**同一个方法引用**才能注销，见 `Core/Events.cs` 注释）。</summary>
        private void OnPlayerGridChanged(Vector2Int g)
        {
            if (_view != null) _view.MarkExplored(g);
        }
    }
}
