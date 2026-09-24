// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · Module/Map/MapModule.cs
// `IMapModule` 的**唯一实现**（门面，internal）：持有 `GridMap`（数据）+ `MapView`（渲染）。
//
// 职责：
//   · 生成（含**可复现随机**、**出生点净空**、**连通性自检**、失败重试、保底布局）
//   · 可走查询 / A* 寻路转发 / 随机可走格
//   · 渲染入口（`ShowArea`）与怪物刷新点 / NPC 点 / 出口 / 洞穴入口的对外暴露
//   · 发 `Events.MapGenerated`（小地图）与 `Events.AreaChanged`
//   · 订阅 `Events.PlayerGridChanged` → 揭迷雾 / 记已探索 / 发 `Events.MapExplored`
//
// 契约（`Module/Contracts.cs` 的 `IMapModule`）冻结：本文件**不新增/不改**接口签名。
//     （实现 = 从 `MapView._explored` 投影，见该属性的注释）。
// 不重写 `CloverEngine.{AStar,IsoLayout}` 与 `CloverEngine.Rng`；不碰 `Game.Map`（引擎地图，本项目不使用）。
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

        /// <summary>
        /// <para>**唯一权威仍是 `MapView._explored`**：本表只是"上次被问到时从渲染层抄下来的一份快照"，
        /// 任何可能改变已探索集合的操作（新格 / 换区 / 清场）都把它置脏
        /// ⇒ 不存在第二份可与渲染层漂移的状态（两份状态必然漂移）。</para>
        /// </summary>
        private readonly List<Vector2Int> _exploredCache = new List<Vector2Int>();

        /// <summary>投影缓存是否已过期（true = 下次读 `ExploredCells` 时从 `MapView` 重抄一遍）。</summary>
        private bool _exploredDirty = true;

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

        /// <summary>
        /// <para>实现口径（逐条）：</para>
        /// <list type="number">
        /// <item>**数据源唯一** = 渲染层 `MapView` 的 `_explored` 位图（`CollectExplored` 抄出）
        ///   —— 本模块**不新造**第二份已探索状态；</item>
        /// <item>**何时增长** = `MapView.MarkExplored` 首次标记某格（由 `Events.PlayerGridChanged`
        ///   驱动，见 <see cref="OnPlayerGridChanged"/>）；</item>
        /// <item>**何时清空** = `ShowArea`（换区/重铺 ⇒ `MapView` 按新图尺寸重建位图）与 `Clear`（退出 Stage）
        ///   —— 两处都把投影置脏，下次读取时自然为空/新的集合；</item>
        /// <item>未铺装（`_view == null`）⇒ **空集合**（不抛）。</item>
        /// </list>
        /// </summary>
        public IReadOnlyCollection<Vector2Int> ExploredCells
        {
            get
            {
                if (_exploredDirty)
                {
                    _exploredCache.Clear();
                    if (_view != null) _view.CollectExplored(_exploredCache);
                    _exploredDirty = false;
                }
                return _exploredCache;
            }
        }

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
        /// <para>实现 = **从 `GridMap` 转发**（登记口径 = 该格地面瓦片键取自 deck 类包 `moor_bridge`
        /// **且该格可走**，见 `DeckTiles` / `GridMap.SetTiles` 的登记点；视图层不猜几何）。
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
            _exploredDirty = true;        // ★ S2：渲染层的已探索位图已清 ⇒ 投影必须跟着作废
            _hasLastPlayerGrid = false;   // ★ revive-chunk：退场 ⇒ "上一格"作废（下次进图第一格不算跳变）
            _grid.Clear();
            MapLog.Info("Clear: 地图数据 / 渲染 / 已探索记录全部清空（退出 Stage）");
        }

        // ═════════════════════════════════════════════════════════════════════
        // 渲染
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>
        /// 二次调用：让渲染节点挂到场景里已有的「地图根」上（`Stage` 场景自带，
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
            // S2：`MapView.ShowArea` 在换区（或尺寸不符）时会按新图重建已探索位图
            //   ⇒ 投影缓存必须作废，否则 `ExploredCells` 会把上一张图的格报给自动地图。
            _exploredDirty = true;
            // revive-chunk：换区后**第一格**不算"大跨度跳变"（那次整图重铺由 travel-black 的落点口径负责）
            _hasLastPlayerGrid = false;
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

        /// <summary>
        /// 标记某格已探索（**非契约方法**；正常情况下由 `Events.PlayerGridChanged` 自动驱动）。
        /// <para>S2：与自动订阅那条路径**同一口径** —— 只有"首次"才发 `Events.MapExplored`
        /// 并作废投影缓存（两条入口都不许绕开这个判定，否则"每帧发事件"会从后门回来）。</para>
        /// </summary>
        public void MarkExplored(Vector2Int g)
        {
            if (_view == null) return;
            if (_view.MarkExplored(g)) OnFirstExplored(g);
        }

        /// <summary>
        /// 某格**首次**被记为已探索：作废投影缓存 + 发 `Events.MapExplored`（载荷 = 本次新增的格集合）。
        /// <para>只在这一处发（`OnPlayerGridChanged` 与公开的 `MarkExplored` 都走它），
        /// 且只在 `MapView.MarkExplored` 回 true（真·首次）时被调 ⇒ 走过同一格不会重复发。</para>
        /// <para>载荷给的是**新数组**（不是复用的可变集合）⇒ 收方可以安全持有引用。</para>
        /// </summary>
        private void OnFirstExplored(Vector2Int g)
        {
            _exploredDirty = true;
            if (Game.Event == null) return;   // 离线宿主 / Game.Launch 未调用：只记状态，不发事件
            Game.Event.Emit<IReadOnlyCollection<Vector2Int>>(Events.MapExplored, new[] { g });
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
        /// "逐格判定同源 / 不露空"断言必须跑在真实地图上，而不是宿主自己再生成一张）；业务代码不要用它。</para>
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
                //   填 `SpawnPoint` 时，玩家走到 (22,26) 按 Tab 打开 automap ⇒ 面板读数
                //   `livePlayerGrid=(22,26) mapPlayer==livePlayer=0`，且叠加层位移 `anchored=(57.60,115.20)`
                //   与"玩家站在出生点旁"那一局**逐字节相同** ⇒ 自动地图**完全以出生点为中心**：
                //   按半径揭示的是出生点周围那圈（玩家身边是空的）、画面中心也不在玩家身上
                //   —— 用户看到的就是"地图没画出来 / 画得不对"。
                playerX = _hasLastPlayerGrid ? _lastPlayerGrid.x : _grid.SpawnPoint.x,
                playerY = _hasLastPlayerGrid ? _lastPlayerGrid.y : _grid.SpawnPoint.y,
            };

            for (var y = 0; y < _grid.Height; y++)
            {
                for (var x = 0; x < _grid.Width; x++) args.tiles.Add(CodeOf(_grid.Get(x, y)));
            }

            // 逐格 **原版 automap Cel**（`AutoMapCel.generated.cs` = 原版 `AutoMap.txt` + `MaxiMap.dc6`
            //   + ACT1 调色板 + 原版 DS1 的解析产物；口径见 `MinimapArgs.cels` 的 `# contract:` 注释）：
            //   按该格的**原版瓦片键**（`GridMap.TryGetTiles`，罗格营地/洞穴/野外三个生成器都逐格登记）
            //   查表。查不到键 ⇒ `-1`（原版这一格不画 automap）。
            //   非预期分支：本图**没有**逐格瓦片覆盖（生成失败的保底布局 `BuildFallback`）⇒ 整幅
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
                        //   `AutoMapCel` 的物件表里和石墙（`moor_stonewall/*`）映射到**同一个 Cel 60**
                        //   ⇒ 水格在小地图上看着就是石头，这正是 R12 报的"小地图分不出水与石头"。
                        //   判据与主视图 R1-B **完全同一条**（`MapView.IsPaletteCycledFlatWallOverlay`）：
                        //   它不是墙、是水面，水面已由 floor 层的水 Cel 呈现 ⇒ 物件层在这个 Cel 上**不叠**。
                        //   只影响小地图画不画这一张物件；`TileKind` / 可走性 / 逐格键一个字不动。
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
            //   是**判定出来的**，不是"忘了登记掉到 default"。
            //   真正的"水 / 石头可区分"发生在 `BuildMinimap` 的物件 Cel 那一层（见那里的 注释）：
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
            Game.Event.On<IReadOnlyCollection<Vector2Int>>(Events.MapExploredRestore, OnExploredRestore);
            _exploreSubscribed = true;
        }

        private void UnsubscribeExplore()
        {
            if (!_exploreSubscribed) return;
            if (Game.Event != null)
            {
                Game.Event.Off<Vector2Int>(Events.PlayerGridChanged, OnPlayerGridChanged);
                Game.Event.Off<IReadOnlyCollection<Vector2Int>>(Events.MapExploredRestore, OnExploredRestore);
            }
            _exploreSubscribed = false;
        }

        /// <summary>
        /// （收 `Events.MapExploredRestore`，见 `Core/Events.cs` 的常量注释）。
        /// <para>
        /// 为什么落在这里（而不是 App 层直接改渲染层）：已探索的**唯一权威**是 `MapView._explored`
        /// （`IMapModule.ExploredCells` 只是它的投影）⇒ 回灌必须走这条同源路径，否则会多出第二份状态。
        /// </para>
        /// <para>
        /// 语义 = **并入（幂等）**：只有真的新增了格才发**一条** `Events.MapExplored`（载荷 = 本次新增格），
        /// 让 automap 侧按既有增量口径并入；不每格发一条（几千格会刷屏）。
        /// </para>
        /// </summary>
        private void OnExploredRestore(IReadOnlyCollection<Vector2Int> cells)
        {
            if (_view == null)
            {
                // 非预期分支：回灌早于 ShowArea（正常由 App 层保证在装配完成后发）⇒ 点名留痕，不静默
                MapLog.Warn($"回灌已探索：渲染层未铺装（ShowArea 未调用）⇒ 本次忽略，{cells?.Count ?? 0} 格未记住");
                return;
            }
            if (cells == null || cells.Count == 0)
            {
                MapLog.Warn("回灌已探索：载荷为空 ⇒ 忽略（正常不该发空载荷）");
                return;
            }

            var fresh = new List<Vector2Int>();
            foreach (var c in cells)
            {
                if (_view.MarkExplored(c)) fresh.Add(c);
            }

            if (fresh.Count == 0)
            {
                MapLog.Info($"回灌已探索 {cells.Count} 格：全部已是已探索状态（幂等，无新增，不发 {Events.MapExplored}）");
                return;
            }

            _exploredDirty = true;      // ★ S2：投影缓存必须跟着作废（否则 `ExploredCells` 少报这批格）
            MapLog.Info($"回灌已探索 {cells.Count} 格 ⇒ 新增 {fresh.Count} 格（读档带回的地图记忆，"
                + $"区域={_grid.Area}，共发 1 条 {Events.MapExplored}）");

            if (Game.Event == null) return;   // 离线宿主：只记状态，不发事件（同 OnFirstExplored 口径）
            Game.Event.Emit<IReadOnlyCollection<Vector2Int>>(Events.MapExplored, fresh);
        }

        /// <summary>
        /// `Events.PlayerGridChanged` 回调（**同一个方法引用**才能注销，见 `Core/Events.cs` 注释）。
        /// <para>S2：**这里就是 `Events.MapExplored` 的发方**（`Events.cs` 的常量注释与本行一一对应）：
        /// 玩家每换一格 ⇒ 若该格是**第一次**被探索，`MapView.MarkExplored` 回 true ⇒ 发一次事件；
        /// 重复走过同一格回 false ⇒ **0 次**发出（不是每帧 / 不是每格无脑发）。</para>
        /// </summary>
        private void OnPlayerGridChanged(Vector2Int g)
        {
            if (_view == null) return;

            //   为什么需要：同区域内一步大跨度位移（死亡重生 `Revive` → `Teleport(SpawnPoint)`、
            //   `IPlayerModule.TeleportTo`）**不走** `Generate`/`ShowArea`/`StartRebuild`，而落点周围的块
            //   **早已被 `ReleaseFarChunks` 回收** ⇒ 实测 `T+0.5s MISSING=2`、`T+1.5s` 才自愈。
            // 放在这里（而不是 `Revive()` 里）= **公共路径**：任何"玩家格坐标一步大跨度变化"都受益。
            if (_hasLastPlayerGrid && MapView.IsLargeShift(_lastPlayerGrid, g))
                _view.PrimeLanding(g, $"格跳变 {_lastPlayerGrid} -> {g}");
            _lastPlayerGrid = g;
            _hasLastPlayerGrid = true;

            if (_view.MarkExplored(g)) OnFirstExplored(g);
        }

        /// <summary>
        /// revive-chunk：上一位玩家格 + 是否有效 —— 判"这一步是不是**大跨度位移**"用。
        /// <para>`ShowArea`（换区：那次重铺由 travel-black 的落点口径负责）与 `Clear`（退场）都复位
        /// ⇒ 换区后的**第一格**不算跳变（否则会在换区重铺期间又插一次预建）。</para>
        /// </summary>
        private Vector2Int _lastPlayerGrid;
        private bool _hasLastPlayerGrid;
    }
}
