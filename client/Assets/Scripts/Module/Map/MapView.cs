// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · Module/Map/MapView.cs
// 等距地图渲染（**只画，不改逻辑**）。
//
// 分层（`tools/ai-skill/constraints.md` #10：地面/物件/遮蔽必须分三层，
// 否则「人被楼盖住」会随机出现）：
//
//   MapRoot (本组件)
//   ├── GroundLayer    地面   sortingOrder = Iso.SortOrder(g) + LayerOffsetGround - SortOrderStep
//   ├── ObjectLayer    物件   sortingOrder = Iso.SortOrder(g) + GameConst.LayerOffsetObject
//   └── OverlayLayer   遮蔽/迷雾 sortingOrder = Iso.SortOrder(g) + GameConst.LayerOffsetOverlay
//        └── Chunk_x_y  分块节点（大图按可见区域懒加载，避免一次铺几万个 GameObject）
//
// 深度排序**随格子变化**（`constraints.md` #5）：`Iso.SortOrder(gx,gy) = (gx+gy)*4 + 100`，
// 同 `gx+gy` 再按层偏移微调 ⇒ 「后面的树盖住前面的角色」自动成立。
//
// ★ 地面层为什么额外 **-SortOrderStep**（本轮"上真瓦片"时才暴露、必须修的东西）：
//   设格子深度 D = gx+gy。偏移前：地面(D)=4D+100、物件(D)=4D+101、实体(D)=4D+102。
//   而**前面那一格**的地面 = 4(D+1)+100 = **4D+104 > 4D+101** ⇒ 前面那格的地面会画在
//   本格物件**之后**。偏巧原版瓦片是"有高度的图像"（栅栏 128×256、树 160×384…），
//   它的**落脚菱形在图像底部 80 px**，图像必然向下压住自己这一格的南半边（世界 y ∈
//   [格中心-1.0, 格中心]）—— 而那正是前面那格地面菱形的范围 ⇒ **本格瓦片的底部会被切掉半截**
//   （迷雾层同理会被前面那格地面盖住）。把地面整体下移一个步长后：
//       地面(D)=4D+96 < 地面(D+1)=4D+100 < 物件(D)=4D+101 < 实体(D)=4D+102
//       < 物件(D+1)=4D+105 < 实体(D+1)=4D+106
//   ⇒ 物件一定盖得住**它覆盖到的所有地面**，同时仍被更前面那格的物件/实体盖住 ——
//   正是等距渲染要的遮挡关系；实体（脚在格中心、图像只向上长）不受影响。
//
// 素材：**全部来自原版 `.dt1` 解出的 PNG**（`tools/d2codec/export_tiles.py`，
// 调色板 = `data/global/palette/ACT1/Pal.PL2`），路径由 `GroundKeyOf`/`ObjectKeyOf` 给出；
// **取不到才回退纯色菱形占位**（异步加载成功后会自己重铺一次）。
// ─────────────────────────────────────────────────────────────────────────────

using System.Collections.Generic;
using CloverEngine;
using Diablo2.Core;
using Diablo2.Def;
using UnityEngine;

namespace Diablo2.Module.Map
{
    /// <summary>等距地图渲染（三层 + 分块 + 迷雾）。</summary>
    public sealed class MapView : MonoBehaviour
    {
        /// <summary>瓦片总数 ≤ 此值 ⇒ 一次铺满（64×64 = 4096，保证该量级流畅）。</summary>
        public const int BuildAllTileThreshold = 4096;

        /// <summary>分块边长（格）。超过阈值的大图按「可见区域」逐块铺。</summary>
        public const int ChunkSize = 16;

        /// <summary>可见区域的检查间隔（秒）——避免每帧算相机视口。</summary>
        private const float ChunkRefreshInterval = 0.25f;

        /// <summary>迷雾不透明度。</summary>
        private const float FogAlpha = 0.80f;

        /// <summary>相机（不设则用 `Camera.main`；两者都没有 ⇒ 关闭可见区域裁剪，铺满全图）。</summary>
        public Camera ViewCamera;

        private GridMap _map;
        private AreaId _area;
        private bool _showing;
        private bool _chunked;
        private bool _repaintRequested;
        private float _nextChunkRefresh;
        private bool _hasChunkRange;
        private Vector2Int _chunkMin;
        private Vector2Int _chunkMax;

        private Transform _groundRoot;
        private Transform _objectRoot;
        private Transform _overlayRoot;

        private readonly Dictionary<Vector2Int, Transform> _groundChunks = new Dictionary<Vector2Int, Transform>();
        private readonly Dictionary<Vector2Int, Transform> _objectChunks = new Dictionary<Vector2Int, Transform>();
        private readonly Dictionary<Vector2Int, Transform> _overlayChunks = new Dictionary<Vector2Int, Transform>();

        private bool _fogOn;
        private bool[,] _explored;
        private int _exploredCount;
        private SpriteRenderer[,] _fogTiles;

        private readonly Dictionary<string, Sprite> _spriteCache = new Dictionary<string, Sprite>();
        private readonly HashSet<string> _pendingLoads = new HashSet<string>();

        private static Sprite _diamondSprite;

        /// <summary>是否已铺好（`ShowArea` 之后）。</summary>
        public bool Showing { get { return _showing && _map != null; } }

        /// <summary>当前显示的格数据。</summary>
        public GridMap Map { get { return _map; } }

        /// <summary>迷雾开关状态。</summary>
        public bool FogOfWar { get { return _fogOn; } }

        /// <summary>已探索格数（小地图可用）。</summary>
        public int ExploredCount { get { return _exploredCount; } }

        /// <summary>已分块铺装的块数（自证用）。</summary>
        public int BuiltChunkCount
        {
            get { return _groundChunks.Count + _objectChunks.Count + _overlayChunks.Count; }
        }

        // ═════════════════════════════════════════════════════════════════════
        // 对外接口
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>绑定格数据（`MapModule` 在创建本组件后立即调用）。</summary>
        public void Bind(GridMap map)
        {
            if (map == null)
            {
                MapLog.Error("MapView.Bind: map 为 null，渲染层将无数据可画");
                return;
            }
            _map = map;
        }

        /// <summary>
        /// 切换区域并**一次性**铺好三层（不每帧重建）。
        /// 区域变化时重置「已探索」记录。
        /// </summary>
        public void ShowArea(AreaId area)
        {
            if (_map == null)
            {
                MapLog.Error($"MapView.ShowArea({area}): 未 Bind 地图（先调 Bind）");
                return;
            }
            if (!_map.Generated)
            {
                MapLog.Error($"MapView.ShowArea({area}): 地图尚未生成（先调 IMapModule.Generate）");
                return;
            }

            var areaChanged = !_showing || _area != area;
            _area = area;
            _showing = true;

            if (areaChanged || _explored == null
                || _explored.GetLength(0) != _map.Width || _explored.GetLength(1) != _map.Height)
            {
                _explored = new bool[_map.Width, _map.Height];
                _exploredCount = 0;
            }

            RebuildLayers();
            MapLog.Info($"MapView.ShowArea: area={area}({MapLog.AreaLabel(area)}) size={_map.Width}x{_map.Height} " +
                        $"分块={(_chunked ? "是" : "否")} 迷雾={(_fogOn ? "开" : "关")} 块数={BuiltChunkCount}");
        }

        /// <summary>迷雾开关（战争迷雾：未探索区域盖一层暗色）。已探索记录保留。</summary>
        public void SetFogOfWar(bool on)
        {
            if (_fogOn == on) return;
            _fogOn = on;
            if (_showing) RebuildLayers();
            MapLog.Info($"MapView.SetFogOfWar: {(on ? "开" : "关")}（已探索 {_exploredCount} 格）");
        }

        /// <summary>标记某格已探索（迷雾揭开）。由 `MapModule` 订阅 `Events.PlayerGridChanged` 转发。</summary>
        public void MarkExplored(Vector2Int g)
        {
            if (_explored == null)
            {
                MapLog.WarnThrottled("view.explore.nomap", "MarkExplored: 地图未铺装（ShowArea 未调用），忽略");
                return;
            }
            if (_map == null || !_map.InBounds(g)) return;
            if (_explored[g.x, g.y]) return;

            _explored[g.x, g.y] = true;
            _exploredCount++;

            var fog = _fogTiles != null ? _fogTiles[g.x, g.y] : null;
            if (fog != null) fog.enabled = false;
        }

        /// <summary>该格是否已探索。</summary>
        public bool IsExplored(Vector2Int g)
            => _explored != null && _map != null && _map.InBounds(g) && _explored[g.x, g.y];

        /// <summary>把已探索的格收集到列表（小地图用）。</summary>
        public void CollectExplored(List<Vector2Int> into)
        {
            if (into == null || _explored == null) return;
            for (var x = 0; x < _explored.GetLength(0); x++)
            {
                for (var y = 0; y < _explored.GetLength(1); y++)
                {
                    if (_explored[x, y]) into.Add(new Vector2Int(x, y));
                }
            }
        }

        /// <summary>清空渲染与已探索记录（退出 Stage）。</summary>
        public void Clear()
        {
            DestroyAllChunks();
            _map = null;
            _showing = false;
            _chunked = false;
            _hasChunkRange = false;
            _repaintRequested = false;
            _explored = null;
            _fogTiles = null;
            _exploredCount = 0;
            MapLog.Info("MapView.Clear: 三层渲染与已探索记录已清空");
        }

        // ═════════════════════════════════════════════════════════════════════
        // 铺装
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>某个层根「曾经建过、后来又没了」是否已报过（只报一次，agent-16）。</summary>
        private bool _layerRootsLostLogged;

        /// <summary>
        /// 三个层根（Ground/Object/Overlay）。
        /// ★ agent-16：**逐个判空重建**而不是「三个都在就返回」—— 层根会随 Stage 场景卸载被销毁
        /// （Unity 的 `==` 重载把已销毁对象判成 null），若只判「三个都在」就会在「一个没了、两个还在」
        /// 时整批重建（旧的两个变成孤儿），并在 `SetParent(已销毁父节点)` 上抛 `MissingReferenceException`。
        /// </summary>
        private void EnsureLayerRoots()
        {
            var lost = false;
            if (_groundRoot == null) { _groundRoot = NewChild(transform, "GroundLayer"); lost = true; }
            if (_objectRoot == null) { _objectRoot = NewChild(transform, "ObjectLayer"); lost = true; }
            if (_overlayRoot == null) { _overlayRoot = NewChild(transform, "OverlayLayer"); lost = true; }

            if (lost && _showing && !_layerRootsLostLogged)
            {
                _layerRootsLostLogged = true;
                MapLog.Warn("MapView: 层根节点曾失效（Ground/Object/Overlay 已被销毁，场景已卸载？）⇒ 已重新创建，" +
                            "本次铺装会从头来一遍（本条只报一次）");
            }
        }

        /// <summary>重建全部三层（`ShowArea` / 迷雾开关 / 贴图异步到位时调用）。</summary>
        private void RebuildLayers()
        {
            EnsureLayerRoots();
            DestroyAllChunks();

            _fogTiles = _fogOn ? new SpriteRenderer[_map.Width, _map.Height] : null;
            _chunked = _map.Width * _map.Height > BuildAllTileThreshold;

            if (_chunked)
            {
                _hasChunkRange = false;
                RefreshVisibleChunks();
            }
            else
            {
                var chunksX = (int)Mathf.Ceil((float)_map.Width / ChunkSize);
                var chunksY = (int)Mathf.Ceil((float)_map.Height / ChunkSize);
                for (var cx = 0; cx < chunksX; cx++)
                {
                    for (var cy = 0; cy < chunksY; cy++) BuildChunk(new Vector2Int(cx, cy));
                }
            }
        }

        /// <summary>按可见区域补块 / 回收远处块（只在大图模式下走）。</summary>
        private void RefreshVisibleChunks()
        {
            // ★ agent-16：层根被销毁（场景卸载）时**绝不**继续铺（否则在已销毁父节点上建子节点）。
            //   外层 `MapModule`/`Update` 也会判，但这里是唯一真正 new 节点的路径，必须自己再判一次。
            if (_groundRoot == null || _objectRoot == null || _overlayRoot == null)
            {
                MapLog.WarnThrottled("view.chunk.nolayers",
                    "MapView.RefreshVisibleChunks: 层根节点不存在（已随场景卸载）⇒ 本次不做可见区域铺装");
                return;
            }

            ComputeVisibleChunkRange(out var min, out var max);

            var buildX0 = Mathf.Max(0, min.x);
            var buildY0 = Mathf.Max(0, min.y);
            var buildX1 = max.x;
            var buildY1 = max.y;

            if (_hasChunkRange && buildX0 == _chunkMin.x && buildY0 == _chunkMin.y
                && buildX1 == _chunkMax.x && buildY1 == _chunkMax.y)
            {
                return;   // 可见块集合没变：什么都不做（绝不每帧重建）
            }

            _chunkMin = new Vector2Int(buildX0, buildY0);
            _chunkMax = new Vector2Int(buildX1, buildY1);
            _hasChunkRange = true;

            for (var cx = buildX0; cx <= buildX1; cx++)
            {
                for (var cy = buildY0; cy <= buildY1; cy++) BuildChunk(new Vector2Int(cx, cy));
            }

            ReleaseFarChunks(buildX0 - 1, buildY0 - 1, buildX1 + 1, buildY1 + 1);
        }

        /// <summary>相机视口四角 → 格范围 → 块范围（含 1 块外扩，避免边缘留白）。</summary>
        private void ComputeVisibleChunkRange(out Vector2Int min, out Vector2Int max)
        {
            var chunksX = (int)Mathf.Ceil((float)_map.Width / ChunkSize);
            var chunksY = (int)Mathf.Ceil((float)_map.Height / ChunkSize);
            min = new Vector2Int(0, 0);
            max = new Vector2Int(chunksX - 1, chunksY - 1);

            var cam = ViewCamera != null ? ViewCamera : Camera.main;
            if (cam == null)
            {
                MapLog.WarnThrottled("view.nocam", "MapView: 找不到相机（ViewCamera 未设且 Camera.main 为空）⇒ 关闭可见区域裁剪，铺满全图");
                return;
            }

            var minX = int.MaxValue;
            var minY = int.MaxValue;
            var maxX = int.MinValue;
            var maxY = int.MinValue;
            for (var i = 0; i < 4; i++)
            {
                var sx = (i & 1) == 0 ? 0f : Screen.width;
                var sy = (i & 2) == 0 ? 0f : Screen.height;
                var g = Iso.ScreenToGrid(cam, new Vector3(sx, sy, 0f));
                minX = Mathf.Min(minX, g.x);
                minY = Mathf.Min(minY, g.y);
                maxX = Mathf.Max(maxX, g.x);
                maxY = Mathf.Max(maxY, g.y);
            }

            // 外扩 1 块（视口边缘的菱形可能只露出一角）
            min = new Vector2Int(Mathf.Clamp(minX / ChunkSize - 1, 0, chunksX - 1),
                                 Mathf.Clamp(minY / ChunkSize - 1, 0, chunksY - 1));
            max = new Vector2Int(Mathf.Clamp(maxX / ChunkSize + 1, 0, chunksX - 1),
                                 Mathf.Clamp(maxY / ChunkSize + 1, 0, chunksY - 1));
        }

        private void BuildChunk(Vector2Int chunk)
        {
            if (_groundChunks.ContainsKey(chunk)) return;   // 已铺过

            _groundChunks[chunk] = NewChild(_groundRoot, $"Chunk_{chunk.x}_{chunk.y}");
            _objectChunks[chunk] = NewChild(_objectRoot, $"Chunk_{chunk.x}_{chunk.y}");
            _overlayChunks[chunk] = NewChild(_overlayRoot, $"Chunk_{chunk.x}_{chunk.y}");

            var x0 = chunk.x * ChunkSize;
            var y0 = chunk.y * ChunkSize;
            var x1 = Mathf.Min(x0 + ChunkSize, _map.Width);
            var y1 = Mathf.Min(y0 + ChunkSize, _map.Height);

            for (var x = x0; x < x1; x++)
            {
                for (var y = y0; y < y1; y++) BuildCell(new Vector2Int(x, y), chunk);
            }
        }

        private void BuildCell(Vector2Int g, Vector2Int chunk)
        {
            var kind = _map.Get(g);
            if (kind == TileKind.Void) return;      // 图外/未生成：什么都不画

            var world = Iso.GridToWorld(g);

            // ── 逐格「原版瓦片键」覆盖：**罗格营地**（`MapGenTownLayout`，源 `townW1.ds1`）
            //    与**邪恶洞穴**（`MapGenCaveLayout`，源 `CAVES/*.ds1`）都用它。
            //    ★ 语义（见 `GridMap.TryGetTiles` 注释）：
            //      · 返回 false ⇒ 本图没有逐格覆盖（= 野外），按 `TileKind` 分类取默认瓦片；
            //      · 返回 true 且 groundKey == "" ⇒ **原版这格不画**（洞穴里的纯黑实心岩体就是
            //        这种格），⛔ 不许兜底成占位菱形 —— 兜底会把它变成一堆灰方块。
            string ds1Ground = null, ds1Object = null;
            var fromDs1 = _map.TryGetTiles(g.x, g.y, out ds1Ground, out ds1Object);

            // ── 地面层 ──
            // ★ 地面整体下移一个排序步长：见文件头「地面层为什么额外 -SortOrderStep」
            var groundKind = TileKindInfo.IsGroundLayer(kind) ? kind : BaseGroundOf(_area);
            var groundKey = fromDs1 ? ds1Ground : GroundKeyOf(groundKind, _area, g);
            if (!string.IsNullOrEmpty(groundKey))
            {
                var groundSprite = TrySprite(ResPaths.Tile(groundKey));
                NewTile(_groundChunks[chunk], PlaceOf(world, groundSprite, isFloor: true),
                    groundSprite, GroundColor(groundKind),
                    Iso.SortOrder(g, GameConst.LayerOffsetGround) - GameConst.SortOrderStep);
            }

            // ── 物件层 ──
            // **有逐格覆盖的区域（营地 / 洞穴）**：只画原版那一格真的有的瓦片 —— 原版那格没有
            //   wall 层瓦片（水上、纯黑岩体、被连通性修整改成 Wall 的死地…）就**什么都不画**。
            //   ⛔ 这里刻意**不做** `TileKind` 兜底：兜底会画出一堆纯色占位方块（实测 170 个），
            //   比"没有物件"难看得多，而且掩盖了"原版这里本来就没东西"这个事实。
            // **其它区域（野外）**：按 `TileKind` 分类取默认瓦片；取不到才用纯色占位（便于发现问题）。
            var ds1HasObject = fromDs1 && !string.IsNullOrEmpty(ds1Object);
            if ((fromDs1 ? ds1HasObject : IsObjectKind(kind)) && !IsHiddenSolidInterior(g, kind))
            {
                var objectKey = ds1HasObject ? ds1Object
                              : (fromDs1 ? null : ObjectKeyOf(kind, _area, g));
                var objectSprite = objectKey != null ? TrySprite(ResPaths.ObjectSprite(objectKey)) : null;
                NewTile(_objectChunks[chunk], PlaceOf(world, objectSprite, isFloor: false),
                    objectSprite, ObjectColor(kind),
                    Iso.SortOrder(g, GameConst.LayerOffsetObject));
            }

            // ── 遮蔽层（迷雾）──
            if (_fogOn) CreateFog(g, chunk);
        }

        private void CreateFog(Vector2Int g, Vector2Int chunk)
        {
            if (_fogTiles == null || _explored == null) return;
            if (_explored[g.x, g.y]) return;
            if (_fogTiles[g.x, g.y] != null) return;

            _fogTiles[g.x, g.y] = NewTile(_overlayChunks[chunk], Iso.GridToWorld(g), null,
                new Color(0f, 0f, 0f, FogAlpha), Iso.SortOrder(g, GameConst.LayerOffsetOverlay));
        }

        /// <summary>
        /// 洞穴实心岩体（`CaveWall` 且四周没有一格可走）不画物件层：
        /// 原版洞穴里那些区域是**全黑**的，画出来反而多余，也省下几千个 GameObject。
        /// </summary>
        private bool IsHiddenSolidInterior(Vector2Int g, TileKind kind)
            => kind == TileKind.CaveWall && !_map.HasWalkableNeighbor(g);

        // ═════════════════════════════════════════════════════════════════════
        // 素材 / 颜色 / 节点
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>取地形/物件贴图（先查缓存 → 再查已驻留 → 都没有就异步加载并先返回 null = 用占位）。</summary>
        private Sprite TrySprite(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            if (_spriteCache.TryGetValue(path, out var cached)) return cached;

            if (Game.Res == null)
            {
                MapLog.WarnThrottled("view.res.null", "MapView.TrySprite: Game.Res 为 null（Game.Launch/CloverRes.Init 未调用？），用纯色占位");
                return null;
            }

            var resident = Game.Res.TryGet<Sprite>(path);
            if (resident != null)
            {
                _spriteCache[path] = resident;
                return resident;
            }

            if (_pendingLoads.Add(path))
            {
                Game.Res.LoadAsset<Sprite>(path, sprite =>
                {
                    if (sprite == null)
                    {
                        MapLog.WarnOnce("view.sprite.miss." + path,
                            $"地图贴图缺失：{path}（用纯色占位；已登记 client/资源欠缺清单.md #3=瓦片 / #4=物件）");
                        return;
                    }
                    _spriteCache[path] = sprite;
                    _repaintRequested = true;      // 贴图异步到位 → 下一帧重铺（只重铺一次）
                });
            }
            return null;
        }

        /// <summary>本区域「无专用贴图的地形」的底：城镇=石地 / 野外=草地 / 洞穴=岩壁。</summary>
        private static TileKind BaseGroundOf(AreaId area)
        {
            switch (area)
            {
                case AreaId.Town: return TileKind.TownFloor;
                case AreaId.BloodMoor: return TileKind.Grass;
                case AreaId.DenOfEvil: return TileKind.CaveWall;
                default:
                    MapLog.WarnThrottled("view.areabase", $"BaseGroundOf: 未登记的区域 {(int)area}，按草地处理");
                    return TileKind.Grass;
            }
        }

        /// <summary>地面层是否画物件（阻挡物）。</summary>
        private static bool IsObjectKind(TileKind kind)
        {
            switch (kind)
            {
                case TileKind.Rock:
                case TileKind.Tree:
                case TileKind.Fence:
                case TileKind.Wall:
                case TileKind.CaveWall:
                case TileKind.Exit:      // 出入口要看得见（营地出口 = 围栏缺口；野外洞穴口 = `CAVES/cavedr.dt1`）
                    return true;
                default:
                    return false;
            }
        }

        // ═════════════════════════════════════════════════════════════════════
        // 原版瓦片表（**换素材只改这一段**）
        //
        // 键 = 相对 `ResPaths.D2Tiles` / `ResPaths.D2Objects` 的路径（`ResPaths.Tile` /
        //      `ObjectSprite` 会补前缀），指到 `Resources/Clover/D2/{Tiles,Objects}/<pack>/<idx>.png`。
        // 来源 = `tools/d2codec/export_tiles.py` 从原版 `.dt1` 解出（调色板 ACT1/Pal.PL2），
        //      每张图都是**原版像素**（未缩放、未调色）。
        // 挑片依据 = `tools/d2codec/pick_tiles.py`（按真实像素均值色分类）+ `contact_sheet.py`
        //      生成的联络表人工核对（`_assets_src/_preview/s_*.png`），**不是靠文件名猜**。
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>草地（Act I 野外/营地外的草）—— `town/floor.dt1` 里偏绿的 10 张。</summary>
        private static readonly string[] GrassTiles =
        {
            "town_floor/029", "town_floor/030", "town_floor/031", "town_floor/033", "town_floor/034",
            "town_floor/036", "town_floor/038", "town_floor/040", "town_floor/042", "town_floor/043",
        };

        /// <summary>泥土 / 土路（营地内的路、野外踩出来的土）。</summary>
        private static readonly string[] DirtTiles =
        {
            "town_floor/000", "town_floor/001", "town_floor/002", "town_floor/012", "town_floor/013",
            "town_floor/014", "town_floor/016", "town_floor/017", "town_floor/018", "town_floor/060",
        };

        /// <summary>城镇地面（营地内部：土 + 碎石混合）。</summary>
        private static readonly string[] TownFloorTiles =
        {
            "town_floor/019", "town_floor/020", "town_floor/021", "town_floor/022", "town_floor/062",
            "town_floor/063", "town_floor/064", "town_floor/065", "town_floor/066", "town_floor/067",
        };

        /// <summary>洞穴地面 —— `CAVES/cave.dt1` 里 orientation==0 且 `Walk` 标志为真的瓦片。</summary>
        private static readonly string[] CaveFloorTiles =
        {
            "cave/144", "cave/081", "cave/139", "cave/107", "cave/111",
            "cave/125", "cave/087", "cave/137",
        };

        /// <summary>洞穴实心岩体（`CaveWall`）的地面：同洞穴地面，岩壁另有物件层盖上去。</summary>
        private static readonly string[] CaveWallGroundTiles = CaveFloorTiles;

        /// <summary>岩石 / 水边（城镇东侧的河岸、野外碎石）。</summary>
        private static readonly string[] RockTownTiles =
        {
            "moor_stonewall/002", "moor_stonewall/003", "moor_stonewall/004", "moor_stonewall/005",
        };

        /// <summary>岩石（野外，`OUTDOORS/stones.dt1` 的巨石）。</summary>
        private static readonly string[] RockMoorTiles =
        {
            "moor_stones/026", "moor_stones/027", "moor_stones/028",
        };

        /// <summary>树（城镇周边，`TOWN/trees.dt1`）。</summary>
        private static readonly string[] TreeTownTiles =
        {
            "town_trees/000", "town_trees/001", "town_trees/009", "town_trees/010",
        };

        /// <summary>树（野外，`OUTDOORS/treegroups.dt1`）。</summary>
        private static readonly string[] TreeMoorTiles =
        {
            "moor_trees/001", "moor_trees/002", "moor_trees/004", "moor_trees/005",
        };

        /// <summary>栅栏（罗格营地木栅 + 石基，`TOWN/fence.dt1`）。</summary>
        private static readonly string[] FenceTownTiles =
        {
            "town_fence/000", "town_fence/001", "town_fence/010", "town_fence/011",
        };

        /// <summary>木栏（野外，`OUTDOORS/fence.dt1`）。</summary>
        private static readonly string[] FenceMoorTiles =
        {
            "moor_fence/003", "moor_fence/015", "moor_fence/016", "moor_fence/017",
        };

        /// <summary>帐篷 / 木棚（罗格营地的"房子"，`TOWN/objects.dt1`）。</summary>
        private static readonly string[] TentTiles =
        {
            "town_objects/000", "town_objects/001", "town_objects/002", "town_objects/004",
            "town_objects/005",
        };

        /// <summary>断墙 / 石堆（野外用到的"墙"）。</summary>
        private static readonly string[] WallMoorTiles =
        {
            "moor_stonewall/000", "moor_stonewall/001", "moor_stonewall/006", "moor_stonewall/007",
        };

        /// <summary>洞穴岩壁（`CAVES/cave.dt1` 的 orientation==12 岩体）。</summary>
        private static readonly string[] CaveWallTiles =
        {
            "cave/091", "cave/093", "cave/095", "cave/096",
        };

        // ⛔ **片 4 删除**：这里原先有一张 `ExitWarpTiles = { warp/000 … warp/003 }`，注释写
        //    "城镇/野外的传送点，`BARRACKS/warp.dt1` orientation==10"。**两条都是错的**（实测）：
        //    ① 出处不对：`BARRACKS/warp.dt1` 的瓦片在**营地内部** 3 格上（参考块 `TownW1.ds1`
        //       本地 (12,18)/(14,18)/(16,25)，合并后同坐标）——**不是出城口**，也不是任何一个
        //       `TileKind.Exit` 格；营地出城口（西侧围栏 3 格缺口，合并帧 (0,21..23)）的
        //       wall 层是**空的**（原版那里本来就不画东西）。
        //       实测命令：`python tools/d2codec/dump_town_exit.py`（出城口 + warp 标记格）
        //       / `python tools/d2codec/dump_cell.py 17,26`（新窗口下出城口格）。
        //    ② 常量本身是**死代码**：三个区域生成器都调了 `GridMap.BeginTileOverrides()`
        //       ⇒ `TryGetTiles` 一律返回 true ⇒ `ObjectKeyOf` 的 Exit 分支只在
        //       "没有逐格覆盖的图"上才会走到，而本项目不存在这种图。
        //    ③ `D2/Tiles/` 下没有 `warp` 目录（只有 `D2/Objects/warp/`，从 `warp.dt1` 解出的
        //       81 张**纯色填充菱形**）—— 真按它取图会得到一块纯色方块。
        //    ⇒ 出入口的**真实口径**：营地出口 = 关卡自己的地面瓦片（`MapGenTownLayout` 逐格键，
        //      片 4 起外围 17 列也是原版瓦片）；野外洞穴口 = `CAVES/cavedr.dt1`（下面这张表）。

        /// <summary>
        /// 洞穴口物件（`CAVES/cavedr.dt1`，出处：`LvlPrest.txt`「Act 1 - Cave Entrance」→
        /// `Act1/Caves/CaveDr1.ds1`；本项目野外生成器 `MapGenWilderness.ApplyCaveDoor` 也用它）。
        /// </summary>
        private static readonly string[] ExitCaveTiles =
        {
            "cave_door/000", "cave_door/001",
        };

        /// <summary>地面贴图键（`kind` + 区域 ⇒ 具体瓦片；**取不到路径返回 null = 纯色占位**）。</summary>
        private static string GroundKeyOf(TileKind kind, AreaId area, Vector2Int g)
        {
            string[] set;
            switch (kind)
            {
                case TileKind.Grass: set = GrassTiles; break;
                case TileKind.Dirt: set = DirtTiles; break;
                case TileKind.Road: set = DirtTiles; break;
                case TileKind.TownFloor: set = TownFloorTiles; break;
                case TileKind.CaveFloor: set = CaveFloorTiles; break;
                case TileKind.CaveWall: set = CaveWallGroundTiles; break;
                case TileKind.Exit:
                    // 出入口本身是块地：城镇/野外的门走土路，洞穴口走洞内地面
                    set = area == AreaId.DenOfEvil ? CaveFloorTiles : DirtTiles;
                    break;
                case TileKind.Rock:
                case TileKind.Tree:
                case TileKind.Fence:
                case TileKind.Wall:
                    // 阻挡物也要有地面（否则它们脚下是黑的）
                    set = area == AreaId.Town ? TownFloorTiles : GrassTiles;
                    break;
                default:
                    MapLog.WarnThrottled("view.groundkey." + (int)kind,
                        $"GroundKeyOf: TileKind={(int)kind} 未登记地面瓦片 ⇒ 本类格子走纯色占位");
                    return null;
            }
            return set[PickVariant(g, set.Length)];
        }

        /// <summary>物件贴图键（`kind` + 区域 ⇒ 具体瓦片；**取不到路径返回 null = 纯色占位**）。</summary>
        private static string ObjectKeyOf(TileKind kind, AreaId area, Vector2Int g)
        {
            string[] set;
            switch (kind)
            {
                case TileKind.Rock:
                    set = area == AreaId.Town ? RockTownTiles : RockMoorTiles;
                    break;
                case TileKind.Tree:
                    set = area == AreaId.Town ? TreeTownTiles : TreeMoorTiles;
                    break;
                case TileKind.Fence:
                    set = area == AreaId.Town ? FenceTownTiles : FenceMoorTiles;
                    break;
                case TileKind.Wall:
                    set = area == AreaId.Town ? TentTiles : WallMoorTiles;
                    break;
                case TileKind.CaveWall: set = CaveWallTiles; break;
                case TileKind.Exit:
                    // ⛔ 只有**洞穴口**有物件瓦片；营地/野外的出口原版**不画物件**
                    //    （营地出口 = 围栏缺口，wall 层本来就是空的）。别再给营地出口编一张物件。
                    if (area != AreaId.DenOfEvil) return null;
                    set = ExitCaveTiles;
                    break;
                default: return null;
            }
            return set[PickVariant(g, set.Length)];
        }

        /// <summary>
        /// 同一类地形在若干张原版瓦片里**确定性地**挑一张（让地面不呆板）。
        /// **不用 `UnityEngine.Random`**（项目禁用它，见 `_common.md` §3.5）：用格坐标做整数散列
        /// ⇒ 同一格每次铺出来都一样，地图看起来一致又可复现。
        /// </summary>
        private static int PickVariant(Vector2Int g, int count)
        {
            if (count <= 1) return 0;
            var h = unchecked((uint)(g.x * 73856093) ^ (uint)(g.y * 19349663) ^ 0x9E3779B9u);
            h ^= h >> 13;
            h = unchecked(h * 0x85EBCA6Bu);
            h ^= h >> 16;
            return (int)(h % (uint)count);
        }

        /// <summary>占位色（验收要求「哪可走哪不可走一眼可辨」）。</summary>
        private static Color GroundColor(TileKind kind)
        {
            switch (kind)
            {
                case TileKind.Grass: return new Color(0.30f, 0.55f, 0.24f);
                case TileKind.Dirt: return new Color(0.45f, 0.35f, 0.22f);
                case TileKind.Road: return new Color(0.62f, 0.50f, 0.33f);
                case TileKind.TownFloor: return new Color(0.68f, 0.62f, 0.48f);
                case TileKind.CaveFloor: return new Color(0.55f, 0.50f, 0.45f);
                case TileKind.CaveWall: return new Color(0.07f, 0.07f, 0.09f);   // 洞穴实心岩体：近黑
                case TileKind.Exit: return new Color(0.00f, 0.85f, 1.00f);       // 出入口：亮青（显眼）
                default: return new Color(0.5f, 0.5f, 0.5f);
            }
        }

        /// <summary>物件层占位色。</summary>
        private static Color ObjectColor(TileKind kind)
        {
            switch (kind)
            {
                case TileKind.Rock: return new Color(0.50f, 0.50f, 0.52f);       // 岩石/篝火/木桩：灰
                case TileKind.Tree: return new Color(0.13f, 0.32f, 0.16f);       // 树：深绿
                case TileKind.Fence: return new Color(0.55f, 0.42f, 0.25f);      // 栅栏：木色
                case TileKind.Wall: return new Color(0.35f, 0.35f, 0.38f);       // 帐篷/石墙：深灰
                case TileKind.CaveWall: return new Color(0.18f, 0.17f, 0.20f);
                default: return new Color(0.4f, 0.4f, 0.4f);
            }
        }

        /// <summary>占位菱形 sprite（懒创建，全图共用一张 + 各自 tint）。</summary>
        private static Sprite DiamondSprite
        {
            get
            {
                if (_diamondSprite == null) _diamondSprite = CreateDiamondSprite();
                return _diamondSprite;
            }
        }

        /// <summary>
        /// 生成 128×64 的**菱形**白图（PPU=64 ⇒ 2×1 世界单位，正好一格等距瓦片），
        /// pivot = 中心 ⇒ 与 `Iso.GridToWorld`（格中心）对齐。
        /// </summary>
        private static Sprite CreateDiamondSprite()
        {
            var w = GameConst.IsoTilePxW;
            var h = GameConst.IsoTilePxH;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                name = "TileDiamondPlaceholder",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };

            var pixels = new Color32[w * h];
            var opaque = new Color32(255, 255, 255, 255);
            var clear = new Color32(0, 0, 0, 0);
            var halfX = (w - 1) * 0.5f;
            var halfY = (h - 1) * 0.5f;

            for (var y = 0; y < h; y++)
            {
                for (var x = 0; x < w; x++)
                {
                    var nx = Mathf.Abs(x - halfX) / halfX;
                    var ny = Mathf.Abs(y - halfY) / halfY;
                    pixels[y * w + x] = nx + ny <= 1.0f ? opaque : clear;
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply(false, false);
            var sprite = Sprite.Create(tex, new Rect(0f, 0f, w, h), new Vector2(0.5f, 0.5f), GameConst.PixelsPerUnit);
            sprite.name = "TileDiamondPlaceholder";
            return sprite;
        }

        private static Transform NewChild(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = Vector3.zero;
            return go.transform;
        }

        private static SpriteRenderer NewTile(Transform parent, Vector3 worldOrPlace, Sprite sprite,
            Color placeholderColor, int order)
        {
            var go = new GameObject(sprite != null ? "T" : "T_placeholder");
            go.transform.SetParent(parent, false);
            go.transform.position = worldOrPlace;          // 用世界坐标：与父节点是否偏移无关

            var sr = go.AddComponent<SpriteRenderer>();
            if (sprite != null)
            {
                sr.sprite = sprite;
                sr.color = Color.white;
                // 原版瓦片的 PPU 与本项目契约 PPU（64）不同 ⇒ 用缩放把"一格 = 160 px"对上
                // （见 PlaceOf / D2TilePixelsPerUnit）。像素本身不动，只是显示尺寸换算。
                go.transform.localScale = Vector3.one * (GameConst.PixelsPerUnit / D2TilePixelsPerUnit);
            }
            else
            {
                sr.sprite = DiamondSprite;                 // 占位：菱形 128×64 @ PPU 64 = 正好一格
                sr.color = placeholderColor;
            }
            sr.sortingOrder = order;
            return sr;
        }

        /// <summary>
        /// 原版等距格是 **160×80 px**（DT1 实测，见 `tools/d2codec/dt1.py` 文件头：
        /// 32×32 等距子块按 5×5 拼成），而本项目一格 = 2×1 世界单位（`IsoHalfW/H`）
        /// ⇒ 原版瓦片要按 **80 px/单位** 解释（`GameConst.PixelsPerUnit = 64` 是契约，不能改，
        /// 所以改用节点缩放 `64/80` 把差额补回来）。
        /// </summary>
        private const float D2TilePixelsPerUnit = 80f;

        /// <summary>
        /// 算出瓦片节点的**世界坐标**（含对齐修正）。空 sprite（占位）时就是格中心。
        ///
        /// 对齐规则（出处：格式参考实现 `Diablerie/.../World/WorldRenderer.cs:164-180`
        /// 的 `topLeft` 计算）：
        ///   · **地砖**（orientation==0）：图像**顶边**贴在格中心上方半格 ⇒ 图像的"顶部 80 px 菱形"
        ///     正好铺满本格（实测 `town_floor/000.png` 的不透明像素就在 row 0..79）；
        ///   · **墙/物件**：图像**底边**贴在格中心下方半格（= 本格菱形的前角）⇒ 图像底部 80 px
        ///     就是它的落脚菱形，更高的部分向上长（栅栏/树/帐篷都是这样）。
        /// </summary>
        private static Vector3 PlaceOf(Vector3 cellCenter, Sprite sprite, bool isFloor)
        {
            if (sprite == null) return cellCenter;         // 占位菱形本来就与格同心

            var h = sprite.rect.height / D2TilePixelsPerUnit;   // 图像在世界单位下的高
            var dy = isFloor
                ? GameConst.IsoHalfH - h * 0.5f             // 顶边在 +halfH ⇒ 中心下移
                : h * 0.5f - GameConst.IsoHalfH;            // 底边在 -halfH ⇒ 中心上移
            return new Vector3(cellCenter.x, cellCenter.y + dy, cellCenter.z);
        }

        // ═════════════════════════════════════════════════════════════════════
        // 回收 / 帧循环
        // ═════════════════════════════════════════════════════════════════════

        private void DestroyAllChunks()
        {
            DestroyChildren(_groundRoot);
            DestroyChildren(_objectRoot);
            DestroyChildren(_overlayRoot);
            _groundChunks.Clear();
            _objectChunks.Clear();
            _overlayChunks.Clear();
        }

        /// <summary>回收可见范围之外的块（外层留 1 块缓冲，避免来回走动时反复重建）。</summary>
        private void ReleaseFarChunks(int keepX0, int keepY0, int keepX1, int keepY1)
        {
            ReleaseFarChunksIn(_groundChunks, keepX0, keepY0, keepX1, keepY1);
            ReleaseFarChunksIn(_objectChunks, keepX0, keepY0, keepX1, keepY1);
            ReleaseFarChunksIn(_overlayChunks, keepX0, keepY0, keepX1, keepY1);
        }

        private void ReleaseFarChunksIn(Dictionary<Vector2Int, Transform> dict, int x0, int y0, int x1, int y1)
        {
            List<Vector2Int> drop = null;
            foreach (var kv in dict)
            {
                var c = kv.Key;
                if (c.x >= x0 && c.x <= x1 && c.y >= y0 && c.y <= y1) continue;
                if (drop == null) drop = new List<Vector2Int>();
                drop.Add(c);
            }
            if (drop == null) return;

            for (var i = 0; i < drop.Count; i++)
            {
                var node = dict[drop[i]];
                if (node != null) Destroy(node.gameObject);
                dict.Remove(drop[i]);
            }
        }

        private static void DestroyChildren(Transform root)
        {
            if (root == null) return;
            for (var i = root.childCount - 1; i >= 0; i--) Destroy(root.GetChild(i).gameObject);
        }

        private void Update()
        {
            if (!_showing || _map == null) return;

            // ★ agent-16：本组件挂在 `MapRoot` 上，正常会随场景一起销毁 ⇒ `Update` 自然不再被调；
            //   但**层根被单独销毁 / 销毁延时一帧**的窗口里仍可能进来 ⇒ 先过一道安全闸门。
            if (_groundRoot == null && _objectRoot == null && _overlayRoot == null) return;

            if (_repaintRequested)
            {
                _repaintRequested = false;
                RebuildLayers();     // 贴图异步到位：只重铺一次
                return;
            }

            if (!_chunked) return;    // 小图一次铺满，不存在逐帧工作
            if (Time.unscaledTime < _nextChunkRefresh) return;
            _nextChunkRefresh = Time.unscaledTime + ChunkRefreshInterval;
            RefreshVisibleChunks();
        }
    }
}
