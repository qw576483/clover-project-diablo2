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
//
// ★ R1-B（用户报「为什么有奇怪的蓝条图片占位」）：**平色水墙瓦片不叠**。
//   原版水域里有一层瓦片是**平色**的（唯一色数 = 1），原版靠 `ACT1/Pal.PL2` 的**调色板循环**
//   把它变成水波动画；本引擎**没有运行期调色板循环** ⇒ 它静态渲染出来就是一块硬边平色色块，
//   视觉上等价占位图（用户看到的那条深蓝长条 = `Objects/moor_river/028`，实测 RGBA(0,32,68)、
//   铺在河带 x=47/54 两列共 49 格）。处置 = 当同一格 floor 层已经是**同 dt1 的水瓦片**时，
//   不再把这块平色 wall 瓦片叠上去（河面由 floor 水瓦片呈现）。**只影响渲染**，逐格键/可走性不动。
//   白名单、取证与生效口径见 `PaletteCycledFlatWallTiles` / `IsPaletteCycledFlatWallOverlay`。
// ─────────────────────────────────────────────────────────────────────────────

// ═════════════════════════════════════════════════════════════════════════════
// ★ T0FIX-A（本片）：**单帧尖峰**（`ShowArea`/整图重铺单帧 46.3~73.9 ms ≫ 一帧预算 16.67 ms）
//   的两条处置 —— 「对象池」+「增量新块分帧」。
//
//   ① **对象池**（`TileNodePool`）：节点不再 `Destroy` / 不再每次 `new GameObject`
//      —— 归还进池、取出即复用，**取出的那一路无条件重设全部渲染字段**（见 `ApplyTileState`）。
//      节点创建只有一条路径（`TileNodePool.Take` 的冷分支），所以「新建」与「复用」不可能出现
//      两种渲染结果 ⇒ 画面逐像素不变。
//   ② **增量新块分帧**（`MaxChunksPerFrame`）：相机走进新的块范围时，`RefreshVisibleChunks`
//      **只登记**待建块，真正的建块在 `Update` 里每帧至多建 `MaxChunksPerFrame` 块
//      （旧口径 = 同一帧把新进范围的块**全建完**，一次 3~7 块 ⇒ 单帧尖峰）。
//      「块根先 `SetActive(false)`、块内全部格建完才激活」⇒ ⛔ 不出现"半块地图"可见态。
//   ③ **整图重铺（`RebuildLayers`）保留一帧铺完、不拆帧**：
//      ⛔ **这条取舍已被 T0FIX-H 推翻**（见下面那段）—— 旧画面其实**留得住**（整图双缓冲），
//      所以整图重铺也纳入了分帧预算；本节其余两条（池化 / 增量分帧）逐字仍生效。
//
//   每帧预算的依据（⛔ 不是魔数）：
//     `ComputeVisibleChunkRange` 已把视口**外扩 1 块** = `ChunkSize` 格 = 16 格余量；
//     相机最快 `GameConst.PlayerWalkSpeed` = 3.0 格/s ⇒ 走完这 16 格余量要
//     16 / 3.0 = 5.333 s = 5.333 s × `FramePacing.TargetFrameRate`(60) = **320 帧**。
//     ⇒ 每帧建 1 块（16 格/帧 = 960 格/s）比"刚好跟上相机"快 320 倍，
//       且一帧 1 块 ⇒ 一帧节点数 ≤ `ChunkSize² × 2 + 3` = 515（出货配置不开迷雾），
//       远低于"一帧预算"能承受的量级。断言与逐条数字见 `tools/probes/hosts/mapcheck` §17。
// ═════════════════════════════════════════════════════════════════════════════
//
// ═════════════════════════════════════════════════════════════════════════════
// ★ T0FIX-H（本片）：**整图重铺（`ShowArea` / 贴图到位 / 迷雾开关 → `RebuildLayers`）的单帧尖峰**
//   也降到一帧预算内 —— 手法 = **整图双缓冲 + 每帧节点预算 + 一帧原子切换 + 分帧回收旧集**。
//
//   缺陷（T0E 实测，⛔ 不是推测）：`RebuildLayers` 单帧建 **2824** 个节点
//   （town-rebuild p50 58714 µs / max 77423 µs ⇒ **27.416 µs/GO**；一帧预算 16666.7 µs ÷ 2824
//   = 5.902 µs/GO ⇒ **超 4.6 倍**），且 5 次触发里 **4 次**落在 `fsm=Stage / uiLoading=0`
//   （玩家可操作期，来源 `.ai-tmp/screenshots/t0e_hover/repave_window_{2..5}.txt`）⇒ 掉 3~5 帧。
//
//   为什么上一版"拆帧必露空"的理由站不住：露空**只对"没有旧画面"成立**，
//   而这三条触发路径**都有旧画面** —— 只要把新图建在**隐藏的缓冲集**上，旧图就能一直留到新图就绪：
//     · `ShowArea`（换区）⇒ 屏幕上仍是**旧区**的完整地图，直到切换（不是空白）；
//     · 贴图到位重铺 ⇒ 屏幕上仍是旧（占位/低清）图；
//     · 迷雾开关 ⇒ 屏幕上仍是旧迷雾态。
//
//   四条硬口径（逐条对应任务书的三条约束）：
//     ① **画面结果逐像素不变**：一格的渲染状态仍是 `GroundState/ObjectState/FogState` 三个**纯函数**
//        （T0FIX-A 起就是），`ApplyTileState` 仍无条件写全 6 项、零分支；本片只改
//        "**什么时候算、算到哪个节点上**"：
//          · 块清单顺序仍是 **cx 外层 / cy 内层**（= 改前 `BuildChunkRange` 与"全图逐块"循环的顺序），
//            纯函数 `PlannedChunks` 就是它（供离线复算）；
//          · 块内格序仍是 **x 外层 / y 内层**（游标 `CursorX/CursorY`，跨帧续建也不变）；
//          · 节点仍一律经 `NewTile` ⇒ `SetParent(parent, false)` **追加到末尾**
//            ⇒ 兄弟序（同 `sortingOrder` 时的平局次序）与改前逐项一致。
//     ② **不露空 / 不半张图 / 不闪帧**：新图整幅建在**隐藏**的缓冲层根（`GroundLayerB`…）下，
//        建完才**一帧**切换（6 次**层根** `SetActive`，⛔ 不逐个节点动）⇒ 任何中间帧上，
//        屏幕上要么是**完整的旧图**、要么是**完整的新图**（不存在"半张"）。
//     ③ **单帧预算**：每帧**新建节点** ≤ `MaxTileNodesPerFrame`（= 512，算式见该常量注释）；
//        每帧扫描格 ≤ `MaxTileCellsPerFrame`（= 2×512 —— 一格最多 2 个节点）；
//        旧集**回收**同样按帧预算分摊（⛔ 不在切换帧里逐个 `SetParent` / `Destroy`）。
//     ④ **保底**：缓冲层根若被销毁（场景卸载那类非预期态）⇒ 放弃分帧、**一帧铺完**并打 Warn
//        （⛔ 不许静默、不许留空白）。
//
//   ⚠️ 本片**纯离线**（任务书明令不进 Play）⇒ 帧时间必须由下一批实机重采；
//      本片给的是"预算算式 × 实测基线"的对照与结构性断言，逐条见回报与 `mapcheck` §19。
// ═════════════════════════════════════════════════════════════════════════════
//
// ═════════════════════════════════════════════════════════════════════════════
// ★ T0FIX-I（本片）：修 T0FIX-H 引入的**致命回归 = Stage 黑屏**（双缓冲建出的地图从未被渲染）。
//
//   实机（上一棒 DIAG，同机位全屏，机位/几何/贴图一字未改）：
//     · 进图就绪后：`wholeMapView_totalSR=5134` 而 **`activeSR=0`**、`gRoot_activeChildren=0`、
//       `chunk0_activeSelf=0`、`chunk0Tile0_activeInHierarchy=0`、全屏 **`mean_lum=5.42/255`**（近全黑）；
//     · 把同一子树强制 `SetActive(true)` ⇒ `ground_activeSR=2210 / object_activeSR=357`、
//       **`mean_lum=31.63/255`**（地形出现）。
//
//   根因 = **两处 `activeSelf` 残留**（都靠"激活父节点"救不回来，Unity 语义如此）：
//     ① `EnsureJobChunk` 把缓冲集块根 `SetActive(false)`，而 `SwapToBuilt` 只激活**层根**
//        ⇒ 新图整棵的 `activeInHierarchy` 恒 false（层根一激活也带不活它们）；
//     ② `TileNodePool.Return` 把瓦片 `SetActive(false)`，而 `Take` / `ApplyTileState` 只复位
//        `SpriteRenderer.enabled`，**不复位 `GameObject.activeSelf`** ⇒ 池复用过的瓦片永远不可见。
//
//   处置（**保住双缓冲分帧，⛔ 不回退"一帧铺完"**）：
//     · `TileNodePool.Take`：**取出即 `SetActive(true)`**，与 `Return` 的 `SetActive(false)` 严格配对；
//     · `MarkJobChunkBuilt`：块内格**建满那一帧**把该块三个块根逐块复活（层根仍隐藏 ⇒ 仍不可见，
//       成本 3 次 `SetActive`/块，摊在建图帧里、不落在切换帧）；
//     · `SwapToBuilt`：激活层根**之前**先 `ActivateChunkRoots(...)` 逐块兜底（正常路径空转）。
//     ⛔ 这三处都是**逐块显式**设置 `activeSelf`，**不依赖**"激活父节点会复活子节点"这条语义。
//     离线判据（复现黑屏 + 验证修复 + 不露空）见 `tools/probes/hosts/mapcheck` §20。
// ═════════════════════════════════════════════════════════════════════════════

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

        /// <summary>
        /// ★ revive-chunk（2026-09-24）：玩家格坐标**一步跨多少格**就判为「大跨度位移」。
        /// <para>算式（⛔ 不是魔数）：可见范围的外扩 / 回收的保留带都是 **1 块** = <see cref="ChunkSize"/> 格
        /// ⇒ 跨 ≥ 2 块时，起始位置与落点之间隔着 ≥1 个整块的缓冲带，落点周围的块必然**不在**当前可见集里
        /// ⇒ 必须立刻预建（见 <see cref="PrimeLanding"/>）。</para>
        /// <para>反例（防误触）：走路每步 1 格 ⇒ 永远 &lt; 2 块 ⇒ 一次都不触发
        /// （`tools/probes/hosts/mapcheck` §34 实测：连走 400 步触发 0 次）。</para>
        /// </summary>
        public const int PrimeJumpCells = ChunkSize * 2;

        /// <summary>
        /// ★ revive-chunk：大跨度落位后的**预建窗口**（秒）—— 窗口内落点范围的块不许被回收/丢弃，
        /// 且窗口内若发生整图重铺则按**落点**算范围（与 travel-black 同口径）。
        /// <para>取 1.0 s 的依据：实机缺块窗口 ≈0.5~1 s（`BP-REVIVE-T0.5` 缺 2 块、`T+1.5` 才自愈），
        /// 且落点范围 ≤ 16 块 ÷ `MaxChunksPerFrame`(1 块/帧) ≈ 0.27 s @60fps 就泵完 ⇒ 1 s 足够覆盖。</para>
        /// </summary>
        public const float PrimeLandingWindow = 1.0f;

        /// <summary>
        /// ★ T0FIX-A：**增量路径**每帧最多建几块（> 0）。
        /// <para>依据（⛔ 不是魔数，逐项都是生产常量）：`ComputeVisibleChunkRange` 外扩 1 块
        /// = <see cref="ChunkSize"/> 格余量，相机最快 <see cref="GameConst.PlayerWalkSpeed"/> 格/s
        /// ⇒ 走完余量需 <c>ChunkSize / PlayerWalkSpeed</c> = 5.333 s = 320 帧 @
        /// <see cref="FramePacing.TargetFrameRate"/>；而一次刷新最多新增 7 块（mapcheck §16 ⑤）
        /// ⇒ 需要 ≈ 0.02 块/帧 ⇒ <b>取整数下限 1 块/帧</b>（裕度 ≈ 45 倍，且建图速率
        /// 1 块/帧 = 16 格/帧 = 960 格/s ≫ 相机 3.0 格/s）。</para>
        /// <para>⛔ 整图重铺（<see cref="RebuildLayers"/>）**不受**本常量约束（它走 T0FIX-H 的另一套预算
        /// <see cref="MaxTileNodesPerFrame"/> 节点/帧，见类头的 T0FIX-H 段）：本常量只约束
        /// "相机走进新块范围"这条增量路径。</para>
        /// </summary>
        public const int MaxChunksPerFrame = 1;

        /// <summary>
        /// ★ T0FIX-H：**整图重铺**每帧最多**新建**几个节点（> 0）。
        /// <para>算式（⛔ 不是魔数，逐项都是生产常量或实测基线）：</para>
        /// <para>· 一帧预算 = 1 s ÷ <see cref="FramePacing.TargetFrameRate"/> = **16666.7 µs**；</para>
        /// <para>· T0E 实测整图重铺的每节点成本 = **27.416 µs/GO**（最差）/ 20.79 µs/GO（p50）
        /// （town-rebuild nodes=2824 p50=58714us max=77423us；出处 = `策划/状态矩阵.tsv` 的
        /// `perf:帧时间(ms/frame)` 行 × `.ai-tmp/screenshots/t0e_hover/repave_window_*.txt`）；</para>
        /// <para>· ⇒ 16666.7 ÷ 27.416 = **607.9** ⇒ 取 **512**（2 的幂，且 ≤ 一块满铺的节点上限
        /// `ChunkSize²×2 + 3 = 515` ⇒ 与增量路径 <see cref="MaxChunksPerFrame"/> = 1 块/帧**同量级**）。</para>
        /// <para>⇒ 单帧最差 512 × 27.416 = **14037 µs** ≤ 16666.7 µs（裕度 **1.19×**；按 p50 算 10645 µs ⇒ 1.57×）。
        /// ⛔ 而且本路径每帧**只做"建"或"回收"之一**，单位节点的开销比实测基线（建 + Destroy 挤在同一帧）更低。</para>
        /// </summary>
        public const int MaxTileNodesPerFrame = 512;

        /// <summary>
        /// ★ T0FIX-H：整图重铺每帧最多**扫描**几格 = `MaxTileNodesPerFrame × 2`。
        /// <para>⛔ 不是随手写的：一格最多 2 个节点。它保证"本帧扫过的格数"也有上界 ——
        /// 否则洞穴里成片的"原版那格不画"（`groundKey == ""`）格子会让一帧扫过整张图
        /// （虽不建节点，但同样是白花帧）。</para>
        /// </summary>
        public const int MaxTileCellsPerFrame = MaxTileNodesPerFrame * 2;

        /// <summary>★ T0FIX-H：一个块在三个层上各有一个**块根**节点 ⇒ 建一块 = 3 个结构节点（= `BuildChunk` 的 3 处 `NewChild`）。</summary>
        private const int ChunkRootNodeCount = 3;

        /// <summary>可见区域的检查间隔（秒）——避免每帧算相机视口。</summary>
        private const float ChunkRefreshInterval = 0.25f;

        // ── ★ R1-D：贴图异步到位后的「整图重铺」合并口径（「人物移动抖动」的第三个根因）──────────
        // 旧口径 = 每来一张贴图就 `_repaintRequested = true` ⇒ 下一帧**整图重铺**
        //   （`RebuildLayers()` = 销毁全部块 + 全图重建）：Town 56×40 = 2240 格 ⇒ 单帧
        //   **2000+ 个 GameObject**（ground 每非 Void 格 1 个 + 物件层 + 迷雾层；洞穴最坏 ≈ 9000）。
        //   而贴图是**流式**异步到位的：进图头几秒 / 野外（>4096 格 ⇒ 分块模式，走到哪加载到哪）
        //   会**每秒触发十几次**这种整图重铺 ⇒ 每 0.1s 一次千级节点销毁+重建 ⇒ 移动/走路时**顿挫**。
        // 新口径 = **合并**（不改画面内容，只改发生频率；见 `ShouldRepaintNow`）：
        //   ① 最后一次到位后静默 `RepaintQuietTime` 才重铺（贴图还在陆续来 ⇒ 先攒着）；
        //   ② 两次重铺之间至少隔 `RepaintMinInterval`；
        //   ③ 从第一次请求算起最多等 `RepaintMaxDelay`（**超时无条件重铺** ⇒ 贴图一定会换上）。
        // 画面不变：重铺的**内容**（建哪些节点 / 什么 sprite / 什么 sortingOrder）一字未改。
        // 离线断言：`tools/probes/hosts/playercheck` §15 e。
        // ─────────────────────────────────────────────────────────────────────────────

        /// <summary>★ R1-D：两次整图重铺之间的最小间隔（秒）。</summary>
        public const float RepaintMinInterval = 0.5f;

        /// <summary>★ R1-D：最后一次贴图到位后静默这么久才重铺（避免把重铺无限期推后）。</summary>
        public const float RepaintQuietTime = 0.25f;

        /// <summary>★ R1-D：从第一次请求算起的**最长等待**（超时无条件重铺）。</summary>
        public const float RepaintMaxDelay = 1.0f;

        /// <summary>迷雾不透明度。</summary>
        private const float FogAlpha = 0.80f;

        /// <summary>相机（不设则用 `Camera.main`；两者都没有 ⇒ 关闭可见区域裁剪，铺满全图）。</summary>
        public Camera ViewCamera;

        private GridMap _map;
        private AreaId _area;
        private bool _showing;
        private bool _chunked;
        private bool _repaintRequested;
        private float _repaintFirstAt = -1f;                          // ★ R1-D：本轮首次请求时刻（<0 = 无待办）
        private float _repaintLastAt;                                 // ★ R1-D：最近一次请求时刻
        private float _lastRepaintAt = float.NegativeInfinity;        // ★ R1-D：上一次重铺时刻
        private bool _repaintCoalesceLogged;                          // ★ R1-D：口径日志只报一次
        private float _nextChunkRefresh;

        // ── ★ travel-black：换区时"**按落点**算重铺范围 + 建满才通知落位" ─────────────────────────
        //   缺陷（实机逐帧量到，见 `.ai-tmp/screenshots/travelblack_tb1.log`）：`AppFlow.EnterArea` 的顺序是
        //   `Generate` → `ShowArea` → **然后**才挪玩家/相机 ⇒ `StartRebuild` 那一刻相机还停在**旧区**，
        //   按它算出来的块范围与落地画面无关（实测旧区 (32,27) 算出 16 块，落地后屏上要的是另 9 块）；
        //   建满一帧切换 ⇒ **交换本身**把屏上地砖撤光 = 落地整屏黑（≈1.84 s，直到第二次重铺才补回来）。
        //   修法 = ① 换区那次重铺的可见范围改按**落点**（= 出生点，玩家即将站的地方）算；
        //          ② 交换**之前**再对一次账（不完整就继续建，见 `ExtendJobToPlanRange`）；
        //          ③ 建满交换后发 `Events.MapAreaReady` ⇒ Flow 才挪玩家/相机（那一帧渲染出来就是完整新图）。
        private bool _hasLandingFocus;          // 本次重铺是否按落点算范围（换区置位，发完 AreaReady 清掉）
        private Vector2Int _landingFocus;       // 落点（出生格）
        private bool _areaReadyOwed;            // 欠一次 `Events.MapAreaReady`（换区置位，交换时发掉）
        private bool _hasChunkRange;
        private Vector2Int _chunkMin;
        private Vector2Int _chunkMax;

        // ── ★ revive-chunk（2026-09-24）：**大跨度落位预建**（死亡重生 / TeleportTo / 未来的位移技能）─
        //   缺陷（实机逐帧量到，`.ai-tmp/test/re_readings_re3.txt:345-412`）：**同区域内**的一步大跨度位移
        //   （`PlayerModule.Revive` → `Teleport(SpawnPoint)` / `IPlayerModule.TeleportTo`）**不走**
        //   `Generate` / `ShowArea` / `StartRebuild`，而落点周围的块**早已被 `ReleaseFarChunks` 回收**
        //   ⇒ 只能等 `ChunkRefreshInterval`(0.25 s) 的登记周期 + `MaxChunksPerFrame`(1 块/帧) 逐帧补：
        //   实测 `builtGround 4 → 8 → 10`、`T+0.5s MISSING=2 [(2,3)(2,4)]`、`T+1.5s` 才自愈。
        //   修法 = 落位**当帧**就把落点范围的缺块入队（`PrimeLanding`）⇒ 窗口从 ~1 s 压到 ~1 帧。
        //   ⛔ 不动 `MaxChunksPerFrame` / `ChunkRefreshInterval`（拿性能换视觉）；⛔ 不走 `StartRebuild`。
        private bool _primeActive;              // 预建窗口是否生效（窗口内 `PlanRange` 也按落点算）
        private float _primeUntil;              // 预建窗口截止时刻（unscaledTime）
        private Vector2Int _primeMin;           // 预建范围（落点，闭区间）
        private Vector2Int _primeMax;
        private int _primeQueued;               // 本次预建真正新入队的块数（自证）

        // ── ★ T0FIX-A：节点池 + 增量建块队列 ────────────────────────────────────
        /// <summary>节点池（唯一创建者；`Take` 冷分支才 `new GameObject`）。</summary>
        private TileNodePool _pool;

        /// <summary>池化节点的挂载点（在 `MapRoot` 下 ⇒ 随场景一起销毁，不跨场景泄漏）。</summary>
        private Transform _poolRoot;

        /// <summary>★ T0FIX-A：待建块队列（`RefreshVisibleChunks` 只登记，建块在 `Update` 按帧预算摊平）。</summary>
        private readonly Queue<Vector2Int> _pendingChunks = new Queue<Vector2Int>();

        /// <summary>★ T0FIX-A：本节流口径只报一次。</summary>
        private bool _pacingLogged;

        /// <summary>★ T0FIX-A：本帧已经建了几块（自证用；`Update` 每帧开头清零）。</summary>
        private int _builtThisFrame;

        /// <summary>★ T0FIX-A：历史单帧建块数的最大值（自证用）。</summary>
        private int _builtPeakPerFrame;

        private Transform _groundRoot;
        private Transform _objectRoot;
        private Transform _overlayRoot;

        // ★ T0FIX-H：这三份字典**不再是 readonly** —— 整图重铺完成时它们与"缓冲集"的三份字典
        //   整份互换（见 `SwapToBuilt`）。语义不变：它们永远描述**当前可见集**。
        private Dictionary<Vector2Int, Transform> _groundChunks = new Dictionary<Vector2Int, Transform>();
        private Dictionary<Vector2Int, Transform> _objectChunks = new Dictionary<Vector2Int, Transform>();
        private Dictionary<Vector2Int, Transform> _overlayChunks = new Dictionary<Vector2Int, Transform>();

        // ── ★ T0FIX-H：整图重铺的**双缓冲**（新图先建在隐藏的缓冲集上，建完一帧切换）──────────────
        /// <summary>缓冲集的三层层根（`GroundLayerB` / `ObjectLayerB` / `OverlayLayerB`；**恒隐藏**）。</summary>
        private Transform _bufGroundRoot;
        private Transform _bufObjectRoot;
        private Transform _bufOverlayRoot;

        /// <summary>缓冲集的块字典（铺装期间由 `EnsureJobChunk` 填；切换时整份变成可见集的那份）。</summary>
        private Dictionary<Vector2Int, Transform> _bufGroundChunks = new Dictionary<Vector2Int, Transform>();
        private Dictionary<Vector2Int, Transform> _bufObjectChunks = new Dictionary<Vector2Int, Transform>();
        private Dictionary<Vector2Int, Transform> _bufOverlayChunks = new Dictionary<Vector2Int, Transform>();

        /// <summary>进行中的整图重铺任务（null = 空闲）。</summary>
        private RebuildJob _job;

        /// <summary>已切到隐藏、**待分帧归还池**的旧块根（切换帧里逐个归还 = 又一个尖峰）。</summary>
        private readonly Queue<Transform> _retireChunks = new Queue<Transform>();

        /// <summary>最近一次整图重铺用了多少帧（自证量）。</summary>
        private int _rebuildFramesLast;

        /// <summary>历史「整图重铺帧数」峰值（自证量）。</summary>
        private int _rebuildFramesPeak;

        /// <summary>上一帧回收了几个节点（自证量）。</summary>
        private int _retiredLastFrame;

        /// <summary>历史「单帧回收节点数」峰值（应 ≤ `max(MaxTileNodesPerFrame, 单块节点上限)`）。</summary>
        private int _retirePeakPerFrame;

        /// <summary>历史「整图重铺单帧新建节点数」峰值（应恒 ≤ `MaxTileNodesPerFrame`）。</summary>
        private int _rebuildPeakNodesPerFrame;

        /// <summary>历史「整图重铺单帧扫描格数」峰值（应恒 ≤ `MaxTileCellsPerFrame`）。</summary>
        private int _rebuildPeakCellsPerFrame;

        private bool _fogOn;
        private bool[,] _explored;
        private int _exploredCount;
        private SpriteRenderer[,] _fogTiles;

        /// <summary>R1-B 日志 tag（`Core/Log.cs` 的 KnownTags 白名单内的独立 tag）。</summary>
        private const string FlatOverlayTag = "R1-B";

        /// <summary>本次铺装里，因「平色水墙瓦片不叠」被跳过的格数（`ReportFlatWallOverlaySkips` 用）。</summary>
        private int _flatWallOverlaySkipped;

        /// <summary>R1-B 的生效口径是否已经报过（整个进程只报一次，避免每次重铺都刷屏）。</summary>
        private bool _flatWallOverlayLogged;

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

        // ── ★ T0FIX-A 自证量（供离线/实机断言读，纯读数、无副作用）──────────────

        /// <summary>待建块数（排队中、尚未建）。</summary>
        public int PendingChunkCount { get { return _pendingChunks.Count; } }

        /// <summary>节点池"新建"计数（冷分支次数）。</summary>
        public int PoolCreatedCount { get { return _pool != null ? _pool.CreatedCount : 0; } }

        /// <summary>节点池"复用"计数（热分支次数）。</summary>
        public int PoolReusedCount { get { return _pool != null ? _pool.ReusedCount : 0; } }

        /// <summary>池中当前空闲节点数。</summary>
        public int PoolFreeCount { get { return _pool != null ? _pool.FreeCount : 0; } }

        /// <summary>历史「单帧建块数」峰值（应恒 ≤ <see cref="MaxChunksPerFrame"/>；整图重铺走另一套预算）。</summary>
        public int BuiltPeakPerFrame { get { return _builtPeakPerFrame; } }

        // ── ★ T0FIX-H 自证量（供下一批实机/离线断言读，纯读数、无副作用）──────────────────

        /// <summary>整图重铺是否在进行中（新图正在隐藏的缓冲集上分帧建）。</summary>
        public bool RebuildInProgress { get { return _job != null; } }

        /// <summary>最近一次整图重铺用了多少帧（0 = 还没铺过）。</summary>
        public int RebuildFramesLast { get { return _rebuildFramesLast; } }

        /// <summary>历史「整图重铺帧数」峰值（自证量）。</summary>
        public int RebuildFramesPeak { get { return _rebuildFramesPeak; } }

        /// <summary>待分帧归还池的旧块根个数（0 = 回收已完）。</summary>
        public int PendingRetireChunks { get { return _retireChunks.Count; } }

        /// <summary>历史「单帧回收节点数」峰值（应 ≤ <c>max(MaxTileNodesPerFrame, 单块节点上限 515)</c>）。</summary>
        public int RetirePeakPerFrame { get { return _retirePeakPerFrame; } }

        /// <summary>整图重铺的**单帧新建节点数**峰值（应恒 ≤ <see cref="MaxTileNodesPerFrame"/>）。</summary>
        public int RebuildPeakNodesPerFrame { get { return _rebuildPeakNodesPerFrame; } }

        /// <summary>整图重铺**单帧扫描格数**峰值（应恒 ≤ <see cref="MaxTileCellsPerFrame"/>）。</summary>
        public int RebuildPeakCellsPerFrame { get { return _rebuildPeakCellsPerFrame; } }

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

            if (areaChanged)
            {
                // ★ travel-black：换区这次整图重铺的可见范围**按落点算**（此刻相机还停在旧区 ——
                //   `AppFlow.EnterArea` 是先 `ShowArea` 再挪玩家/相机；按相机算出来的范围与落地画面无关），
                //   并欠一次 `Events.MapAreaReady`（建满交换那一帧才发，Flow 收到才挪玩家/相机）。
                _landingFocus = _map.SpawnPoint;
                _hasLandingFocus = true;
                _areaReadyOwed = true;
                MapLog.Info($"[travel-black] 换区重铺：可见范围改按**落点** {_landingFocus} 算" +
                            "（相机此刻仍在旧区；建满交换后才发 Events.MapAreaReady）");
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

        /// <summary>
        /// 标记某格已探索（迷雾揭开）。由 `MapModule` 订阅 `Events.PlayerGridChanged` 转发。
        /// <para>
        /// ★ 2026-09-23（S2，`Events.MapExplored` 的"首次"判定）：返回 = **本次调用是否真的改变了状态**
        /// （true = 这一格刚刚第一次被探索）。调用方（`MapModule`）用它来保证"只在首次发事件"
        /// —— 重复走过同一格返回 false ⇒ ⛔ 不发（否则边走边刷屏）。
        /// </para>
        /// <para>返回 false 的三种情形：未铺装 / 图外 / 该格此前已探索（都是"没有新增"）。</para>
        /// </summary>
        public bool MarkExplored(Vector2Int g)
        {
            if (_explored == null)
            {
                MapLog.WarnThrottled("view.explore.nomap", "MarkExplored: 地图未铺装（ShowArea 未调用），忽略");
                return false;
            }
            if (_map == null || !_map.InBounds(g)) return false;

            var first = !_explored[g.x, g.y];
            MarkOne(g.x, g.y);                       // 脚下这一格（幂等）

            // ★ automap-panel2（2026-09-24）—— 把 automap 的揭示口径搬到**数据源头**：
            //   首次走到一格时按**已登记口径**一次揭开"玩家可能看到的一片"，而不是只揭开脚下那一格。
            //   **出处（逐条）**：
            //     · 半径 6 = `UI/MiniMapPanel.RevealRadius`（同一条口径，那里写了推导）：
            //       可见半高 = `CameraRig.DefaultOrthographicSize` = 3.75 世界单位（原版 600px/80ppu/2）、
            //       1 格 = 1 世界单位高；等距投影下同屏条件 = |dx−dy| ≤ 3.75·aspect(=6.67) **且**
            //       |dx+dy| ≤ 3.75/0.5(=7.5)，而 |dx|+|dy| = max(|dx+dy|, |dx−dy|)
            //       ⇒ 取紧的下界 floor(6.67) = **6** ⇒ 语义 = "凡是能被玩家看到的格，走过就都记下来"。
            //     · 形状 = **只走可通行格**（`GridMap.Walkable`，唯一判定 `Def.TileKindInfo.IsWalkable`）
            //       ⇒ 不穿墙；再把已揭示地面格的**相邻阻挡格**一并揭示（原版"地板先、墙后"的轮廓；
            //       `TileKind.Void` 不揭示）。
            //     · ⛔ 与**原版按房间揭示**仍不同（本机没有 DRLG 房间层）⇒ 差异仍登记 **E23 ④**；
            //       本段只是把**已经登记并验收**的口径从"面板开图那一次"移到数据源头
            //       ⇒ `_explored`（**存档权威持有者**）与画面口径天然一致，不再有"画面 ⊃ 存档"的口径差。
            //   ⚠️ 返回值语义**一个字未改**（true = **脚下这一格**首次被探索）：`MapModule` 靠它决定是否
            //      发 `Events.MapExplored`（载荷仍 `{g}`）。被本段顺带揭开的格**不**单独发事件，
            //      而由开图/进图时的**全量快照**（`App/AppSnapshots.EmitExploredSnapshot` 读
            //      `IMapModule.ExploredCells`）下发 ⇒ 面板与存档拿到的是**同一份集合**。
            //   ⚠️ 幂等：重复走过同一格只补"还没揭到的"格，不再做一次 BFS 以外的事（开销 = 一次 ≤13×13 邻域）。
            if (first)
            {
                const int R = 6;                     // = UI/MiniMapPanel.RevealRadius（同一口径，见上）
                var w = _map.Width;
                var d4x = new[] { 1, -1, 0, 0 };
                var d4y = new[] { 0, 0, 1, -1 };
                var d8x = new[] { 1, -1, 0, 0, 1, 1, -1, -1 };
                var d8y = new[] { 0, 0, 1, -1, 1, -1, 1, -1 };
                var dist = new int[w * _map.Height];
                for (var i = 0; i < dist.Length; i++) dist[i] = -1;
                var q = new Queue<int>();
                var start = g.y * w + g.x;
                dist[start] = 0;
                q.Enqueue(start);
                while (q.Count > 0)
                {
                    var i = q.Dequeue();
                    var cx = i % w;
                    var cy = i / w;
                    MarkOne(cx, cy);
                    if (dist[i] >= R) continue;
                    for (var k = 0; k < 4; k++)
                    {
                        var nx = cx + d4x[k];
                        var ny = cy + d4y[k];
                        if (!_map.InBounds(nx, ny)) continue;
                        var j = ny * w + nx;
                        if (dist[j] >= 0) continue;
                        if (!_map.Walkable(new Vector2Int(nx, ny))) continue;   // 墙不进 BFS（只由轮廓那步揭示它本身）
                        dist[j] = dist[i] + 1;
                        q.Enqueue(j);
                    }
                }
                for (var i = 0; i < dist.Length; i++)
                {
                    if (dist[i] < 0) continue;                                   // 只从"走到过"的格往外看轮廓
                    var cx = i % w;
                    var cy = i / w;
                    for (var k = 0; k < 8; k++)
                    {
                        var nx = cx + d8x[k];
                        var ny = cy + d8y[k];
                        if (!_map.InBounds(nx, ny) || _explored[nx, ny]) continue;
                        var kind = _map.Get(nx, ny);
                        if (kind == TileKind.Void || TileKindInfo.IsWalkable(kind)) continue;
                        MarkOne(nx, ny);
                    }
                }
            }

            // 揭开一格（幂等）：已探索位 + 迷雾节点（`_fogTiles` 为 null = 本局未开迷雾）
            void MarkOne(int x, int y)
            {
                if (_explored[x, y]) return;
                _explored[x, y] = true;
                _exploredCount++;
                var f = _fogTiles != null ? _fogTiles[x, y] : null;
                if (f != null) f.enabled = false;
            }

            return first;
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
            _job = null;                                 // ★ T0FIX-H：分帧重铺任务作废（下面 DestroyAllChunks 会连缓冲集一起清）
            DestroyAllChunks();
            _bufGroundRoot = null;                       // ★ T0FIX-H：缓冲层根也随 `DestroyAllChunks` 的回收失去内容
            _bufObjectRoot = null;
            _bufOverlayRoot = null;
            _pendingChunks.Clear();                      // ★ T0FIX-A：待办队列一并作废
            if (_pool != null) _pool.Clear();            // ★ T0FIX-A：退场时真销毁池（不跨场景留节点）
            _map = null;
            _showing = false;
            _chunked = false;
            _hasChunkRange = false;
            _hasLandingFocus = false;                    // ★ travel-black：退场时落点口径一并作废
            _areaReadyOwed = false;
            _primeActive = false;                        // ★ revive-chunk：落位预建窗口一并作废
            _primeUntil = float.NegativeInfinity;
            _primeQueued = 0;
            _repaintRequested = false;
            _repaintFirstAt = -1f;                       // ★ R1-D：清掉待重铺时刻（否则旧时刻会立刻触发）
            _lastRepaintAt = float.NegativeInfinity;     // （`_repaintCoalesceLogged` 不复位：口径日志一局只报一次）
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

        /// <summary>
        /// ★ R1-D **纯函数**：现在该不该整图重铺（离线可断言：`tools/probes/hosts/playercheck` §15 e）。
        /// <para>语义 = 「合并口径」三条（见 <see cref="RepaintMinInterval"/> 上方注释）：
        /// ① 有超时兜底 ⇒ 贴图一定会换上；② ③ 是节流 ⇒ 不在贴图潮流里反复整图重铺。</para>
        /// </summary>
        /// <param name="now">当前时刻（`Time.unscaledTime`）。</param>
        /// <param name="firstRequestAt">本轮第一次贴图到位的时刻；**负数 = 没有待办**。</param>
        /// <param name="lastRequestAt">最近一次贴图到位的时刻。</param>
        /// <param name="lastRepaintAt">上一次重铺的时刻（从未 = 负无穷）。</param>
        public static bool ShouldRepaintNow(float now, float firstRequestAt, float lastRequestAt, float lastRepaintAt)
        {
            if (firstRequestAt < 0f) return false;                          // 没有待办的贴图
            if (now - firstRequestAt >= RepaintMaxDelay) return true;       // ① 超时兜底（必换）
            if (now - lastRequestAt < RepaintQuietTime) return false;       // ② 还在陆续到位 ⇒ 先攒着
            if (now - lastRepaintAt < RepaintMinInterval) return false;     // ③ 两次重铺至少隔这么久
            return true;
        }

        /// <summary>
        /// ★ R1-D：登记「贴图到位 ⇒ 需要重铺」（由异步回调调用）。只置标志 + 记时刻，
        /// **不直接重铺** —— 何时真的重铺由 <see cref="ShouldRepaintNow"/> 决定。
        /// </summary>
        private void RequestRepaint()
        {
            var now = Time.unscaledTime;
            if (!_repaintRequested) _repaintFirstAt = now;      // 本轮的第一次
            _repaintRequested = true;
            _repaintLastAt = now;

            if (_repaintCoalesceLogged) return;
            _repaintCoalesceLogged = true;
            MapLog.Info(
                "[R1-D] 地图重铺合并口径：贴图到位**不再逐张整图重铺**。" +
                $"旧口径 = 每来一张贴图 `RebuildLayers()` 一次（Town 56×40 ⇒ 单帧销毁+重建 ≈ 2000+ 个 GameObject，" +
                "洞穴最坏 ≈ 9000；贴图流式到位时每秒可能十几次 ⇒ 移动/进图时顿挫）。" +
                $"新口径 = 静默 {RepaintQuietTime:0.##}s + 两次重铺间隔 ≥ {RepaintMinInterval:0.##}s + 最长等待 {RepaintMaxDelay:0.##}s（超时必换）。" +
                "画面不变：重铺的**内容**一字未改，只改发生频率。断言：tools/probes/hosts/playercheck §15 e");
        }

        /// <summary>
        /// 重建全部三层（`ShowArea` / 迷雾开关 / 贴图异步到位时调用）。
        /// <para>
        /// ★ T0FIX-H（本片）**改口径**：本方法不再"一帧铺完"，而是**开一个分帧重铺任务** ——
        /// 新图建在**隐藏的缓冲集**（`GroundLayerB`…）上，每帧至多 <see cref="MaxTileNodesPerFrame"/> 个节点，
        /// 建满后**一帧原子切换**；旧集再按同一预算分帧归还池。
        /// </para>
        /// <para>为什么上一版"拆帧必露空"的判断要推翻：露空**只对"没有旧画面"成立**。
        /// 这三条触发路径都有旧画面（换区 = 旧区、贴图到位 = 旧图、迷雾开关 = 旧迷雾态），
        /// 只要新图建在隐藏缓冲集上，旧图就能一直留到新图就绪 ⇒ 中间帧**要么完整旧图、要么完整新图**。
        /// 逐条依据见类头的 T0FIX-H 段。</para>
        /// <para>本方法**只做登记与建缓冲集**（⛔ 不在调用点泵帧：`ShowArea`/`SetFogOfWar` 是同步调用，
        /// 当场泵会把它变成"调用方那一帧的尖峰"）；真正的推进在 <see cref="Update"/> 的
        /// <see cref="PumpRebuild"/>（每帧一次，含首次）。</para>
        /// </summary>
        private void RebuildLayers()
        {
            StartRebuild("整图重铺");
        }

        /// <summary>
        /// ★ T0FIX-H：开一次整图重铺任务（缓冲集 + 块清单 + 游标）。
        /// <para>块清单顺序由纯函数 <see cref="PlannedChunks"/> 给出（= 改前 `BuildChunkRange` /
        /// "全图逐块"循环的顺序：cx 外层、cy 内层）⇒ 兄弟序与改前一致。</para>
        /// </summary>
        private void StartRebuild(string why)
        {
            EnsureLayerRoots();
            if (_job != null) CancelRebuildJob("被新的整图重铺请求取代");
            EnsureBufferRoots();

            _pendingChunks.Clear();           // 整图重铺 = 缓冲集从头建 ⇒ 队列里的增量待办作废
            _flatWallOverlaySkipped = 0;      // 本次铺装的计数（R1-B）

            _fogTiles = _fogOn ? new SpriteRenderer[_map.Width, _map.Height] : null;
            _chunked = _map.Width * _map.Height > BuildAllTileThreshold;

            ComputeRebuildRange(out var min, out var max);      // ★ travel-black：换区时按**落点**算
            var buildX0 = Mathf.Max(0, min.x);
            var buildY0 = Mathf.Max(0, min.y);
            _chunkMin = new Vector2Int(buildX0, buildY0);
            _chunkMax = new Vector2Int(max.x, max.y);
            _hasChunkRange = true;

            var chunks = new List<Vector2Int>(_chunked ? 32 : ChunkCountX * ChunkCountY);
            PlannedChunks(_chunked, buildX0, buildY0, _chunkMax.x, _chunkMax.y, ChunkCountX, ChunkCountY, chunks);
            if (chunks.Count == 0)
            {
                // 非预期分支：地图尺寸为 0（不该发生）⇒ 点名，不留"以为铺过了"的假态
                MapLog.Error($"MapView.StartRebuild({why}): 块清单为空（地图 {_map.Width}x{_map.Height}）⇒ 本次不铺装");
                return;
            }

            var job = new RebuildJob
            {
                GroundRoot = _bufGroundRoot,
                ObjectRoot = _bufObjectRoot,
                OverlayRoot = _bufOverlayRoot,
                Chunks = chunks,
                Building = _retireChunks.Count == 0,       // 旧集还没回收完 ⇒ 先回收（缓冲集必须是空的）
            };
            job.CursorX = chunks[0].x * ChunkSize;
            job.CursorY = chunks[0].y * ChunkSize;
            job.CreatedBefore = PoolCreatedCount;
            job.ReusedBefore = PoolReusedCount;
            _job = job;

            MapLog.Info($"[T0FIX-H] 整图重铺改为**分帧双缓冲**（{why}）：块 {chunks.Count} 个" +
                        $"（{(_chunked ? "可见范围" : "全图")}），每帧新建节点 ≤ {MaxTileNodesPerFrame}、扫描格 ≤ {MaxTileCellsPerFrame}；" +
                        $"新图建在隐藏的缓冲层根（GroundLayerB/ObjectLayerB/OverlayLayerB）下，建满后**一帧**切换" +
                        $"（6 次层根 SetActive、⛔ 不逐个节点动），旧集按同一预算分帧归还池" +
                        $"（待回收 {_retireChunks.Count} 块）⇒ 中间帧**要么完整旧图、要么完整新图**");
        }

        /// <summary>
        /// ★ T0FIX-H 的**保底**（非预期态专用）：一帧铺完（= T0FIX-H 之前的口径）。
        /// <para>唯一触发条件 = 分帧重铺期间缓冲层根被销毁（场景卸载那类）⇒ 宁可有尖峰，
        /// 也不许静默留一张空图。⛔ 它与分帧路径**共用**判定与建节点
        /// （`PlanCell` / `ApplyCellPlan` / `BuildChunk`），差别只有"目标集 + 分不分帧"。</para>
        /// </summary>
        private void RebuildImmediate(string why)
        {
            MapLog.Warn($"[T0FIX-H] {why} ⇒ 本次整图重铺**放弃分帧、一帧铺完**（保底：⛔ 不退化成空白、不静默；" +
                        "这条分支实测只在场景卸载那类态上出现）");

            EnsureLayerRoots();
            _job = null;
            _retireChunks.Clear();
            DestroyAllChunks();               // 连缓冲集一起清（它可能已被销毁 ⇒ 判空无害）
            _pendingChunks.Clear();
            _flatWallOverlaySkipped = 0;

            _fogTiles = _fogOn ? new SpriteRenderer[_map.Width, _map.Height] : null;
            _chunked = _map.Width * _map.Height > BuildAllTileThreshold;

            ComputeRebuildRange(out var min, out var max);      // ★ travel-black：换区时按**落点**算
            var buildX0 = Mathf.Max(0, min.x);
            var buildY0 = Mathf.Max(0, min.y);
            _chunkMin = new Vector2Int(buildX0, buildY0);
            _chunkMax = new Vector2Int(max.x, max.y);
            _hasChunkRange = true;

            if (_chunked) BuildChunkRange(buildX0, buildY0, _chunkMax.x, _chunkMax.y);
            else
            {
                for (var cx = 0; cx < ChunkCountX; cx++)
                {
                    for (var cy = 0; cy < ChunkCountY; cy++) BuildChunk(new Vector2Int(cx, cy));
                }
            }

            _rebuildFramesLast = 1;
            LogPacingOnce("整图重铺(保底)");
            ReportFlatWallOverlaySkips();
            NotifyAreaReadyIfOwed("整图重铺(保底，一帧铺完)");
        }

        /// <summary>
        /// ★ T0FIX-H **纯函数**（离线可断言）：整图重铺要建的**块清单**与顺序。
        /// <para>顺序 = 改前两条路径的顺序，逐字保留：分块模式 = `BuildChunkRange`（**cx 外层、cy 内层**）；
        /// 非分块模式 = "全图逐块"循环（同样 cx 外层、cy 内层）。兄弟序（同 `sortingOrder` 的平局次序）
        /// 就靠它不变 ⇒ 画面逐像素不变。</para>
        /// </summary>
        public static void PlannedChunks(bool chunked, int buildX0, int buildY0, int buildX1, int buildY1,
            int chunksX, int chunksY, List<Vector2Int> into)
        {
            if (into == null) return;
            if (chunked)
            {
                for (var cx = buildX0; cx <= buildX1; cx++)
                {
                    for (var cy = buildY0; cy <= buildY1; cy++) into.Add(new Vector2Int(cx, cy));
                }
                return;
            }
            for (var cx = 0; cx < chunksX; cx++)
            {
                for (var cy = 0; cy < chunksY; cy++) into.Add(new Vector2Int(cx, cy));
            }
        }

        /// <summary>
        /// ★ T0FIX-H **纯函数**（离线可断言）：本帧还能不能再处理**一格**（成本 <paramref name="cost"/> 个节点）。
        /// <para>MapView 的分帧循环与离线断言**共用这一条** ⇒ 预算口径不可能漂。两条闸门：
        /// ① 本帧已用节点 + 本格成本 ≤ <paramref name="nodeBudget"/>；
        /// ② 本帧已扫格数 &lt; <paramref name="cellBudget"/>（防"成片不画的格子让一帧扫过整张图"）。</para>
        /// </summary>
        public static bool FrameAccepts(int usedNodes, int usedCells, int cost, int nodeBudget, int cellBudget)
        {
            if (usedCells >= cellBudget) return false;
            return usedNodes + cost <= nodeBudget;
        }

        // ═════════════════════════════════════════════════════════════════════
        // ★ T0FIX-H：整图重铺的**分帧双缓冲**（建缓冲集 → 一帧切换 → 分帧回收旧集）
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>★ T0FIX-H：一次整图重铺任务的进度（同时只有一个；`_job == null` = 空闲）。</summary>
        private sealed class RebuildJob
        {
            /// <summary>目标（**恒隐藏**的）缓冲层根。</summary>
            public Transform GroundRoot, ObjectRoot, OverlayRoot;

            /// <summary>要建的块清单（顺序见 <see cref="PlannedChunks"/>）。</summary>
            public List<Vector2Int> Chunks;

            /// <summary>下一块在 <see cref="Chunks"/> 里的下标。</summary>
            public int ChunkIndex;

            /// <summary>当前块内的续建游标（**x 外层 / y 内层**，与改前块内格序逐字一致）。</summary>
            public int CursorX, CursorY;

            /// <summary>true = 已进入"建"阶段（false = 先等旧集回收完，缓冲集必须是空的）。</summary>
            public bool Building;

            /// <summary>本任务已经用了多少帧。</summary>
            public int Frames;

            /// <summary>本任务累计建的节点数。</summary>
            public int NodesBuilt;

            /// <summary>本任务单帧建节点数的峰值（应 ≤ <see cref="MaxTileNodesPerFrame"/>）。</summary>
            public int PeakNodesInFrame;

            /// <summary>本任务单帧扫描格数的峰值（应 ≤ <see cref="MaxTileCellsPerFrame"/>）。</summary>
            public int PeakCellsInFrame;

            /// <summary>任务开始时的池计数（用来算"这次重铺新建/复用了多少"）。</summary>
            public int CreatedBefore, ReusedBefore;

            /// <summary>★ travel-black：交换前对账追加过几轮（见 <see cref="ExtendJobToPlanRange"/>，上限 4）。</summary>
            public int Extends;
        }

        /// <summary>
        /// ★ T0FIX-H：缓冲集（`GroundLayerB` / `ObjectLayerB` / `OverlayLayerB`）—— 新图先建在这里、
        /// **恒隐藏**，建满才一帧切换。懒创建；场景卸载后会判空重建（与 `EnsureLayerRoots` 同口径）。
        /// </summary>
        private void EnsureBufferRoots()
        {
            if (_bufGroundRoot == null) _bufGroundRoot = NewChild(transform, "GroundLayerB");
            if (_bufObjectRoot == null) _bufObjectRoot = NewChild(transform, "ObjectLayerB");
            if (_bufOverlayRoot == null) _bufOverlayRoot = NewChild(transform, "OverlayLayerB");

            // ⛔ 恒隐藏：任何时刻缓冲集都不可见（可见性只由 SwapToBuilt 的 6 次层根 SetActive 决定）
            _bufGroundRoot.gameObject.SetActive(false);
            _bufObjectRoot.gameObject.SetActive(false);
            _bufOverlayRoot.gameObject.SetActive(false);
        }

        /// <summary>
        /// ★ T0FIX-H：丢弃"建到一半"的重铺任务。缓冲集里的节点**不逐个归还池**（那正是要避免的单帧尖峰，
        /// 实测每节点 20.79~27.416 µs）⇒ 直接销毁缓冲层根（连子树）。**非预期分支** ⇒ 一定打 Warn。
        /// </summary>
        private void CancelRebuildJob(string why)
        {
            var job = _job;
            if (job == null) return;
            _job = null;
            MapLog.Warn($"[T0FIX-H] 上一次分帧重铺被丢弃（{why}）：已建 {job.NodesBuilt} 个节点 / {job.Frames} 帧" +
                        "⇒ 半成品缓冲集**连子树整根销毁**（⛔ 不逐个归还池：那会把单帧尖峰搬到这一帧），下次重铺重建");

            _bufGroundChunks.Clear();
            _bufObjectChunks.Clear();
            _bufOverlayChunks.Clear();
            if (job.GroundRoot != null) Destroy(job.GroundRoot.gameObject);
            if (job.ObjectRoot != null) Destroy(job.ObjectRoot.gameObject);
            if (job.OverlayRoot != null) Destroy(job.OverlayRoot.gameObject);
            _bufGroundRoot = null;
            _bufObjectRoot = null;
            _bufOverlayRoot = null;
        }

        /// <summary>
        /// ★ T0FIX-H：把"已不可见"的旧块**按帧预算**归还池（每帧 ≤ <paramref name="budget"/> 个节点）。
        /// 返回 true = 回收完（队列空）。
        /// <para>为什么回收也要分帧：切换帧里逐个 `SetParent` + `SetActive` 上万次 = 又一个尖峰
        /// （实测每节点 20.79~27.416 µs ⇒ 2824 个节点 ≈ 59~77 ms）。</para>
        /// <para>口径：每帧**至少**回收一块（否则退回"永远回收不完"），故单帧上界 =
        /// `max(budget, 单块节点上限 515)`。</para>
        /// </summary>
        private bool PumpRetire(int budget)
        {
            var nodes = 0;
            while (_retireChunks.Count > 0)
            {
                var chunkRoot = _retireChunks.Peek();
                if (chunkRoot == null) { _retireChunks.Dequeue(); continue; }   // 已被场景卸载销毁
                if (nodes > 0 && nodes + chunkRoot.childCount > budget) break;  // 下一块放不下 ⇒ 留到下一帧
                _retireChunks.Dequeue();
                nodes += RecycleChunk(chunkRoot);
            }

            _retiredLastFrame = nodes;
            if (nodes > _retirePeakPerFrame) _retirePeakPerFrame = nodes;
            return _retireChunks.Count == 0;
        }

        /// <summary>★ T0FIX-H：把一份块字典里的块根全部登记进"待回收"队列（只登记，⛔ 不回收）。</summary>
        private void EnqueueRetire(Dictionary<Vector2Int, Transform> dict)
        {
            foreach (var kv in dict)
            {
                if (kv.Value != null) _retireChunks.Enqueue(kv.Value);
            }
        }

        /// <summary>
        /// ★ T0FIX-H：把整图重铺推进一帧（`Update` 每帧一次；预算 = <see cref="MaxTileNodesPerFrame"/> /
        /// <see cref="MaxTileCellsPerFrame"/>）。
        /// <para>四个出口：① 预算用尽 ⇒ 下帧续建；② 全部建完 ⇒ **同一帧**切换（O(1)）；
        /// ③ 缓冲层根被销毁（非预期）⇒ 转一帧铺完保底；④ 没任务 ⇒ 什么都不做。</para>
        /// </summary>
        private void PumpRebuild()
        {
            var job = _job;
            if (job == null) return;

            if (!job.Building)
            {
                if (!PumpRetire(MaxTileNodesPerFrame)) return;   // 本帧先把旧集回收完（缓冲集必须空）
                job.Building = true;
            }

            if (job.GroundRoot == null || job.ObjectRoot == null || job.OverlayRoot == null)
            {
                // 非预期分支：缓冲层根被销毁（场景卸载那类）⇒ 放弃分帧，退回一帧铺完（⛔ 不静默留空白）
                _bufGroundRoot = null;
                _bufObjectRoot = null;
                _bufOverlayRoot = null;
                _bufGroundChunks.Clear();
                _bufObjectChunks.Clear();
                _bufOverlayChunks.Clear();
                RebuildImmediate("分帧重铺期间缓冲层根被销毁（场景卸载 / 被外部销毁？）");
                return;
            }

            job.Frames++;
            var used = 0;
            var cells = 0;

            while (job.ChunkIndex < job.Chunks.Count)
            {
                var chunk = job.Chunks[job.ChunkIndex];

                if (!_bufGroundChunks.ContainsKey(chunk))
                {
                    // 块根 = 3 个结构节点，**同样计入预算**（预算是"本帧新建节点上限"，不是"瓦片节点上限"）
                    if (!FrameAccepts(used, cells, ChunkRootNodeCount, MaxTileNodesPerFrame, MaxTileCellsPerFrame)) break;
                    EnsureJobChunk(chunk);
                    used += ChunkRootNodeCount;
                }

                var x0 = chunk.x * ChunkSize;
                var y0 = chunk.y * ChunkSize;
                var x1 = Mathf.Min(x0 + ChunkSize, _map.Width);
                var y1 = Mathf.Min(y0 + ChunkSize, _map.Height);
                var deferred = false;

                for (var x = job.CursorX; x < x1 && !deferred; x++)
                {
                    var yFrom = x == job.CursorX ? job.CursorY : y0;
                    for (var y = yFrom; y < y1; y++)
                    {
                        var g = new Vector2Int(x, y);
                        var plan = PlanCell(_map, _area, g);          // ⛔ 先决定要几个节点，再决定要不要建（预算必须精确）
                        if (!FrameAccepts(used, cells, plan.NodeCount, MaxTileNodesPerFrame, MaxTileCellsPerFrame))
                        {
                            job.CursorX = x;
                            job.CursorY = y;
                            deferred = true;
                            break;
                        }
                        used += ApplyCellPlan(plan, g, _bufGroundChunks[chunk], _bufObjectChunks[chunk], _bufOverlayChunks[chunk]);
                        cells++;
                    }
                }
                if (deferred) break;

                job.ChunkIndex++;
                // ★ T0FIX-I：这一块的格**已全部建完** ⇒ 把它的三个块根 `activeSelf` 逐块置回 true。
                //   层根此刻仍隐藏 ⇒ **仍然不可见**（缓冲集的可见性只由 `SwapToBuilt` 的层根 SetActive 决定）；
                //   这么做的意义 = 切换那一帧「层根一激活，整块就出来」，且**不依赖**"激活父节点顺带复活
                //   `activeSelf=false` 的子节点"这种 Unity 语义（那正是本片 Stage 黑屏的根因）。
                MarkJobChunkBuilt(chunk);
                if (job.ChunkIndex < job.Chunks.Count)
                {
                    var next = job.Chunks[job.ChunkIndex];
                    job.CursorX = next.x * ChunkSize;
                    job.CursorY = next.y * ChunkSize;
                }
            }

            job.NodesBuilt += used;
            if (used > job.PeakNodesInFrame) job.PeakNodesInFrame = used;
            if (cells > job.PeakCellsInFrame) job.PeakCellsInFrame = cells;
            if (used > _rebuildPeakNodesPerFrame) _rebuildPeakNodesPerFrame = used;
            if (cells > _rebuildPeakCellsPerFrame) _rebuildPeakCellsPerFrame = cells;

            if (job.ChunkIndex >= job.Chunks.Count)
            {
                // ★ travel-black：交换**之前**再对一次账 —— 交换那一帧屏上必须是"完整的（新）图"。
                //   不完整（相机在重铺期间动过 / 落点范围与登记范围不一致）⇒ 把缺块追加进本次清单、
                //   继续建（下一帧），⛔ 不在这时候交换（否则交换本身就把屏上地砖撤光 = 落地黑）。
                if (!ExtendJobToPlanRange(job)) SwapToBuilt(job);       // 建满 ⇒ 同一帧切换
            }
        }

        /// <summary>★ T0FIX-H：在**缓冲集**里建一块的三层块根（建出来即失活 ⇒ 切换前绝不可见）。</summary>
        private void EnsureJobChunk(Vector2Int chunk)
        {
            var name = $"Chunk_{chunk.x}_{chunk.y}";
            var ground = NewChild(_bufGroundRoot, name);
            var obj = NewChild(_bufObjectRoot, name);
            var overlay = NewChild(_bufOverlayRoot, name);
            ground.gameObject.SetActive(false);
            obj.gameObject.SetActive(false);
            overlay.gameObject.SetActive(false);

            _bufGroundChunks[chunk] = ground;
            _bufObjectChunks[chunk] = obj;
            _bufOverlayChunks[chunk] = overlay;
        }

        /// <summary>
        /// ★ T0FIX-I：把**一个已建满的块**的三个块根逐块复活（`activeSelf = true`）。
        /// <para>为什么要单独一步：`EnsureJobChunk` 建块时把块根 `SetActive(false)`（缓冲集恒隐藏的双保险），
        /// 而 Unity 的语义是「激活父节点**不**复活 `activeSelf=false` 的子节点」⇒ 只激活层根的话，
        /// 双缓冲建出的整张图**一次都不会被渲染**（实机实测：整图 `activeSR=0`、全屏 `mean_lum=5.42/255`）。</para>
        /// <para>时机：块内格**全部建完**那一帧（层根仍隐藏 ⇒ 仍不可见），成本 = 3 次 `SetActive`/块，
        /// 摊在建图帧里 ⇒ 不落在 `SwapToBuilt` 的切换帧上。</para>
        /// </summary>
        private void MarkJobChunkBuilt(Vector2Int chunk)
        {
            Transform ground, obj, overlay;
            if (_bufGroundChunks.TryGetValue(chunk, out ground) && ground != null) ground.gameObject.SetActive(true);
            if (_bufObjectChunks.TryGetValue(chunk, out obj) && obj != null) obj.gameObject.SetActive(true);
            if (_bufOverlayChunks.TryGetValue(chunk, out overlay) && overlay != null) overlay.gameObject.SetActive(true);
        }

        /// <summary>
        /// ★ T0FIX-I：切换前的**不变量兜底** —— 逐块确认新集每个块根 `activeSelf` 已为 true
        /// （正常路径上 `MarkJobChunkBuilt` 已复活过 ⇒ 这里是 O(块数) 的空转，不碰任何瓦片节点）。
        /// <para>⛔ 它的存在意义 = 「不靠层根整体激活」：块根是否可见由它**逐个**负责，而不是赌
        /// Unity 会替我们把 `activeSelf=false` 的子节点复活。</para>
        /// </summary>
        private static void ActivateChunkRoots(Dictionary<Vector2Int, Transform> ground,
            Dictionary<Vector2Int, Transform> obj, Dictionary<Vector2Int, Transform> overlay)
        {
            ActivateChunkRootsIn(ground);
            ActivateChunkRootsIn(obj);
            ActivateChunkRootsIn(overlay);
        }

        /// <summary>★ T0FIX-I：见 <see cref="ActivateChunkRoots"/>（单层）。</summary>
        private static void ActivateChunkRootsIn(Dictionary<Vector2Int, Transform> dict)
        {
            foreach (var kv in dict)
            {
                var t = kv.Value;
                if (t != null && !t.gameObject.activeSelf) t.gameObject.SetActive(true);
            }
        }

        /// <summary>
        /// ★ T0FIX-H：**一帧原子切换**（新图就绪 ⇒ 缓冲集显示、旧集隐藏）。
        /// <para>⛔ 本方法体内**不许**出现任何"逐个节点"的操作（`SetParent` / `Destroy` / `Return` /
        /// `new GameObject` / 写 `sprite`/`color`/`sortingOrder`）—— 那正是尖峰的来源。
        /// 这里只有：6 次**层根** `SetActive` + 三份字典的引用互换 + `O(块数)` 次入队
        /// （旧块进 <see cref="_retireChunks"/>，由 <see cref="PumpRetire"/> 按帧预算归还池）。
        /// 结构性断言见 `tools/probes/hosts/mapcheck` §19。</para>
        /// </summary>
        private void SwapToBuilt(RebuildJob job)
        {
            // ① 旧集：整根失活（内容还在 ⇒ 块根进回收队列，逐帧归还池）
            _groundRoot.gameObject.SetActive(false);
            _objectRoot.gameObject.SetActive(false);
            _overlayRoot.gameObject.SetActive(false);
            EnqueueRetire(_groundChunks);
            EnqueueRetire(_objectChunks);
            EnqueueRetire(_overlayChunks);

            // ② 新集：整根激活（内容在隐藏状态下已经建满 ⇒ 一次切换就是完整新图）
            //   ★ T0FIX-I（**根因修复**）：先**逐块**把新集每个块根的 `activeSelf` 置 true，再激活层根。
            //     `EnsureJobChunk` 建块时置过 false；正常路径上 `MarkJobChunkBuilt` 已在块建满那一帧复活
            //     （⇒ 这里实测是空转），本行是切换帧的**不变量兜底**。
            //     ⛔ 不许删掉它退回"只做下面 3 次层根 SetActive"—— Unity 激活父节点**不**复活
            //     `activeSelf=false` 的子节点，那正是本片 Stage 黑屏到 `mean_lum=5.42/255` 的根因。
            ActivateChunkRoots(_bufGroundChunks, _bufObjectChunks, _bufOverlayChunks);
            job.GroundRoot.gameObject.SetActive(true);
            job.ObjectRoot.gameObject.SetActive(true);
            job.OverlayRoot.gameObject.SetActive(true);

            // ③ 层根与字典互换（旧集变成"下一次的缓冲集"；它的字典已交给回收队列 ⇒ 清空备用）
            var g0 = _groundRoot; _groundRoot = _bufGroundRoot; _bufGroundRoot = g0;
            var o0 = _objectRoot; _objectRoot = _bufObjectRoot; _bufObjectRoot = o0;
            var v0 = _overlayRoot; _overlayRoot = _bufOverlayRoot; _bufOverlayRoot = v0;
            var dg = _groundChunks; _groundChunks = _bufGroundChunks; _bufGroundChunks = dg;
            var dob = _objectChunks; _objectChunks = _bufObjectChunks; _bufObjectChunks = dob;
            var dov = _overlayChunks; _overlayChunks = _bufOverlayChunks; _bufOverlayChunks = dov;
            _bufGroundChunks.Clear();
            _bufObjectChunks.Clear();
            _bufOverlayChunks.Clear();

            _job = null;
            _nextChunkRefresh = 0f;      // ★ chunk-hole：切换完**下一帧**就对照当前相机范围对账一次
                                         //   （新集 = 登记时算的范围；期间相机若动了 ⇒ 立刻补齐/回收，
                                         //   不等下一个 0.25 s 周期，黑窗更短）
            _rebuildFramesLast = job.Frames;
            if (job.Frames > _rebuildFramesPeak) _rebuildFramesPeak = job.Frames;

            LogPacingOnce("整图重铺");
            ReportFlatWallOverlaySkips();     // R1-B：只报一次的数值证据
            NotifyAreaReadyIfOwed("分帧双缓冲交换");   // ★ travel-black：换区那次 ⇒ Flow 此刻才挪玩家/相机
            MapLog.Info($"[T0FIX-H] 整图重铺完成并**一帧切换**：{job.Frames} 帧 / 块 {job.Chunks.Count} / " +
                        $"建节点 {job.NodesBuilt}（单帧峰值 {job.PeakNodesInFrame}/{MaxTileNodesPerFrame}，" +
                        $"扫描峰值 {job.PeakCellsInFrame}/{MaxTileCellsPerFrame}）/ " +
                        $"新建 GO {PoolCreatedCount - job.CreatedBefore} + 复用 {PoolReusedCount - job.ReusedBefore}；" +
                        $"旧集 {_retireChunks.Count} 块转由回收队列按帧（≤ {MaxTileNodesPerFrame} 节点）归还池");
        }

        /// <summary>块网格的列数（`Ceil(Width / ChunkSize)`）。</summary>
        private int ChunkCountX { get { return (int)Mathf.Ceil((float)_map.Width / ChunkSize); } }

        /// <summary>块网格的行数（`Ceil(Height / ChunkSize)`）。</summary>
        private int ChunkCountY { get { return (int)Mathf.Ceil((float)_map.Height / ChunkSize); } }

        /// <summary>把 `[x0,x1] × [y0,y1]` 的块**本帧**建完（`RebuildLayers` 用；不排队）。</summary>
        private void BuildChunkRange(int x0, int y0, int x1, int y1)
        {
            for (var cx = x0; cx <= x1; cx++)
            {
                for (var cy = y0; cy <= y1; cy++) BuildChunk(new Vector2Int(cx, cy));
            }
        }

        /// <summary>
        /// ★ T0FIX-A 的**纯函数**（离线可断言）：本帧该建几块 = `min(待建块数, 预算)`。
        /// 逐条数字与断言见 `tools/probes/hosts/mapcheck` §17。
        /// </summary>
        public static int ChunksThisFrame(int pendingCount, int budgetPerFrame)
        {
            if (pendingCount <= 0 || budgetPerFrame <= 0) return 0;
            return pendingCount < budgetPerFrame ? pendingCount : budgetPerFrame;
        }

        /// <summary>
        /// ★ T0FIX-A：把队列里的待建块**按帧预算**建出来（`Update` 每帧调用一次）。
        /// 一帧至多建 <paramref name="budget"/> 块 ⇒ 单帧新增节点 ≤ 预算 × 单块节点上限。
        /// </summary>
        private void PumpChunkBuild(int budget)
        {
            var n = ChunksThisFrame(_pendingChunks.Count, budget);
            if (n <= 0) return;

            _builtThisFrame += n;
            for (var i = 0; i < n; i++)
            {
                var chunk = _pendingChunks.Dequeue();
                BuildChunk(chunk);
            }

            if (_builtThisFrame > _builtPeakPerFrame) _builtPeakPerFrame = _builtThisFrame;
            LogPacingOnce("增量补块");
        }

        /// <summary>
        /// ★ T0FIX-A：把「本帧建块摊平 + 节点池」的**生效口径**报一次（tag `T0FIX`），
        /// 数字全部由生产常量现算（⛔ 不写裸数字）。
        /// </summary>
        private void LogPacingOnce(string how)
        {
            if (_pacingLogged) return;
            _pacingLogged = true;

            var budgetFrames = ChunkSize / GameConst.PlayerWalkSpeed * FramePacing.TargetFrameRate;
            var perFrameGrid = MaxChunksPerFrame * ChunkSize * FramePacing.TargetFrameRate;
            Log.Info("T0FIX",
                $"[T0FIX] 地图建块摊平 + 节点池生效（{how}）：增量路径每帧至多 {MaxChunksPerFrame} 块" +
                $"（= {MaxChunksPerFrame * ChunkSize} 格/帧 = {perFrameGrid:0} 格/s，" +
                $"相机最快 {GameConst.PlayerWalkSpeed:0.0} 格/s）；" +
                $"依据 = 视口外扩 1 块 {ChunkSize} 格 ÷ {GameConst.PlayerWalkSpeed:0.0} 格/s = " +
                $"{ChunkSize / GameConst.PlayerWalkSpeed:0.###} s = {budgetFrames:0} 帧 @{FramePacing.TargetFrameRate}fps；" +
                $"② **整图重铺**（`RebuildLayers` → 缓冲集）每帧新建节点 ≤ {MaxTileNodesPerFrame}、扫描格 ≤ {MaxTileCellsPerFrame}，" +
                "建满后**一帧**切换（6 次层根 SetActive），旧集按同一预算分帧归还池" +
                $"（口径见类头 T0FIX-H 段与 `MaxTileNodesPerFrame` 的算式）；" +
                $"池计数：新建 {PoolCreatedCount} / 复用 {PoolReusedCount}；块根建出先 SetActive(false)、块内建完才激活");
        }

        /// <summary>
        /// ★ R1-B：把「平色水墙瓦片不叠」的**生效口径 + 实测格数**报一次（tag `R1-B`），
        /// 供下一批进 Play 当**数值证据**（不必截图即可判定这条渲染规则生效）。
        /// 只报一次（整个进程），且只在真的跳过过格子的地图上报（野外/洞穴不会产生这条）。
        /// </summary>
        private void ReportFlatWallOverlaySkips()
        {
            if (_flatWallOverlaySkipped <= 0 || _flatWallOverlayLogged) return;
            _flatWallOverlayLogged = true;
            Log.Info(FlatOverlayTag,
                $"河面 wall 层平色瓦片**不叠**（生效）：本次铺装跳过 {_flatWallOverlaySkipped} 格；" +
                $"白名单键 = {string.Join(",", PaletteCycledFlatWallTiles)}；" +
                "生效条件 = 该 wall 瓦片在白名单内 且 floor 层是同 dt1 的非空水瓦片" +
                "（原版靠 `ACT1/Pal.PL2` 调色板循环把这块平色瓦片变成水波，本引擎无运行期调色板循环 ⇒ " +
                "静态渲染 = 硬边平色色块，视觉上等价占位）⇒ 河面改由同 dt1 的 floor 水瓦片呈现。" +
                "仅渲染层：TileKind / 可走性 / 逐格键一个字未动");
        }

        /// <summary>
        /// 按可见区域**登记**待建块 / 回收远处块（只在大图模式下走）。
        /// <para>★ T0FIX-A：旧口径是"本帧把新进范围的块**全建完**"（一次 3~7 块 ⇒ 单帧尖峰）；
        /// 新口径 = **只登记**，建块交给 `Update` 的 <see cref="PumpChunkBuild"/> 按
        /// <see cref="MaxChunksPerFrame"/> 摊平。登记顺序 = 行主序（块内格序不变）。</para>
        /// </summary>
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
                && buildX1 == _chunkMax.x && buildY1 == _chunkMax.y
                && ChunkRangeCovered(_groundChunks.Keys, _pendingChunks, buildX0, buildY0, buildX1, buildY1))
            {
                return;   // 可见块集合没变**且已完整**：什么都不做（绝不每帧重建）
            }

            _chunkMin = new Vector2Int(buildX0, buildY0);
            _chunkMax = new Vector2Int(buildX1, buildY1);
            _hasChunkRange = true;

            // ★ T0FIX-A：只登记（不建）；已建的不重复排队，队列里已有的不重复入队
            for (var cx = buildX0; cx <= buildX1; cx++)
            {
                for (var cy = buildY0; cy <= buildY1; cy++)
                {
                    var c = new Vector2Int(cx, cy);
                    if (_groundChunks.ContainsKey(c)) continue;
                    if (_pendingChunks.Contains(c)) continue;
                    _pendingChunks.Enqueue(c);
                }
            }

            ReleaseFarChunks(buildX0 - 1, buildY0 - 1, buildX1 + 1, buildY1 + 1);
        }

        /// <summary>
        /// ★ chunk-hole 修复（2026-09-23，血沼泽大片黑）**纯函数**（离线可断言）：
        /// `[x0,x1]×[y0,y1]` 里的**每一块**都已建好、或已在待建队列里吗？
        /// <para>为什么"登记范围没变就早退"不够：`StartRebuild` 会**清空待建队列**（见 :593 的
        /// `_pendingChunks.Clear()`）并把登记范围改成"**那一刻**相机算出来的范围"，而可见集要等
        /// 缓冲集建满、`SwapToBuilt` 切换之后才换成新范围的那份 —— 期间相机一动（进图落位 / 走路 /
        /// 传送），登记范围与实际建块集就**脱钩**；旧口径只比范围 ⇒ 早退 ⇒ 洞里那几块**永远没人补**
        /// （实测：野外 `MISSING=4 [(0,3)(0,4)(1,3)(1,4)]`、`PendingChunks=0`、黑区边界 =
        /// 16×16 块网格的等距投影直线，6/6 采样复现）。加上"集合完整性"这一条后，任何脱钩都会在
        /// 下一次刷新（≤ <see cref="ChunkRefreshInterval"/>）被重新登记、由
        /// <see cref="PumpChunkBuild"/> 补齐 ⇒ 自愈，而不是靠"运气好范围变了"。</para>
        /// </summary>
        internal static bool ChunkRangeCovered(IEnumerable<Vector2Int> built, IEnumerable<Vector2Int> pending,
            int x0, int y0, int x1, int y1)
        {
            for (var cx = x0; cx <= x1; cx++)
            {
                for (var cy = y0; cy <= y1; cy++)
                {
                    var c = new Vector2Int(cx, cy);
                    if (SetHas(built, c) || SetHas(pending, c)) continue;
                    return false;                       // 范围里有一块既没建也没排队 ⇒ 有洞
                }
            }
            return true;
        }

        /// <summary>`Queue&lt;T&gt;` 不实现 `ICollection&lt;T&gt;` ⇒ 用 `IEnumerable` + 手写 Contains（集合都很小）。</summary>
        private static bool SetHas(IEnumerable<Vector2Int> set, Vector2Int c)
        {
            if (set == null) return false;
            foreach (var v in set) { if (v == c) return true; }
            return false;
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

        // ═════════════════════════════════════════════════════════════════════
        // ★ travel-black：换区重铺的**落点范围** + 交换前的完整性对账
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>
        /// ★ travel-black **纯函数**（离线可断言）：以 <paramref name="focusX"/>,<paramref name="focusY"/>（落点）
        /// 为中心、用**当前视口的格半跨**（<paramref name="halfX"/>/<paramref name="halfY"/> — 由视口四角与屏幕
        /// 中心算出的格坐标差，**平移不变**）算块范围，外扩 1 块（口径与 <see cref="ComputeVisibleChunkRange"/> 一致）。
        /// <para>为什么需要"按落点算"：`AppFlow.EnterArea` 是 `Generate` → `ShowArea` → **然后**才挪玩家/相机
        /// ⇒ `StartRebuild` 那一刻相机还在**旧区**，按相机算出的块范围与落地画面**无关**（实机量到：
        /// 旧区 (32,27) 算出 16 块，而落地后屏上要的是另外 9 块）。整图重铺建的是那块错图，建满一帧切换
        /// ⇒ **交换本身**把屏上地砖撤光 = 落地整屏黑（≈1.84 s，逐帧读数 `.ai-tmp/screenshots/travelblack_tb1.log`）。</para>
        /// </summary>
        public static void LandingRange(int focusX, int focusY, int halfX, int halfY, int mapW, int mapH,
            out Vector2Int min, out Vector2Int max)
        {
            var chunksX = (mapW + ChunkSize - 1) / ChunkSize;
            var chunksY = (mapH + ChunkSize - 1) / ChunkSize;
            min = new Vector2Int(Mathf.Clamp(DivFloor(focusX - halfX, ChunkSize) - 1, 0, chunksX - 1),
                                 Mathf.Clamp(DivFloor(focusY - halfY, ChunkSize) - 1, 0, chunksY - 1));
            max = new Vector2Int(Mathf.Clamp(DivFloor(focusX + halfX, ChunkSize) + 1, 0, chunksX - 1),
                                 Mathf.Clamp(DivFloor(focusY + halfY, ChunkSize) + 1, 0, chunksY - 1));
        }

        /// <summary>向下取整除法（C# 的 `/` 对负数是截断 ⇒ 视口在图的左/下边时算出来的块号会偏一格）。</summary>
        private static int DivFloor(int v, int d)
        {
            return v >= 0 ? v / d : -(((-v) + d - 1) / d);
        }

        // ═════════════════════════════════════════════════════════════════════
        // ★ revive-chunk（2026-09-24）：**大跨度落位预建**（死亡重生 / TeleportTo / 位移技能）
        //   口径与 travel-black 的 `LandingRange` 同一份（落点 + 视口格半跨），但**不重铺**：
        //   只把落点范围里缺的块**当帧**入 `_pendingChunks`（跳过 0.25 s 登记等待），
        //   真正的建块仍由 `PumpChunkBuild` 按既有 `MaxChunksPerFrame` 摊平
        //   ⇒ 单帧尖峰口径**一字未改**（⛔ 不是"拿性能换视觉"）。
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>
        /// ★ revive-chunk **纯函数**（离线可断言，`mapcheck` §34）：玩家格坐标一步 `<paramref name="from"/>`
        /// → `<paramref name="to"/>` 是否算「大跨度位移」（Chebyshev 距离 ≥ <see cref="PrimeJumpCells"/>）。
        /// <para>为什么用 Chebyshev：块是**轴对齐**的 ⇒ "斜着跨 30 格"与"横着跨 30 格"在块网格上跨的块数一样，
        /// 用 <c>max(|dx|,|dy|)</c> 判"块网格上跳了几格"最直接。</para>
        /// </summary>
        public static bool IsLargeShift(Vector2Int from, Vector2Int to)
        {
            return Mathf.Max(Mathf.Abs(to.x - from.x), Mathf.Abs(to.y - from.y)) >= PrimeJumpCells;
        }

        /// <summary>
        /// ★ revive-chunk **纯函数**（离线可断言，`mapcheck` §35）：落点 `focus` 处"落地那一刻"要看的块清单
        /// = <see cref="LandingRange"/> + <see cref="PlannedChunks"/>（与整图重铺**同一口径**）。
        /// <para>小图（<c>mapW*mapH ≤ BuildAllTileThreshold</c> ⇒ 不分块、**从不回收块**）⇒ 写空清单返回
        /// （⛔ 不做无谓的全量对账 —— 那是把"重生"变成"重进区域"）。</para>
        /// </summary>
        public static void PrimeChunks(int focusX, int focusY, int halfX, int halfY, int mapW, int mapH,
            List<Vector2Int> into)
        {
            if (into == null) return;
            into.Clear();
            if (mapW <= 0 || mapH <= 0) return;
            if ((long)mapW * mapH <= BuildAllTileThreshold) return;      // 小图不分块 ⇒ 无需预建

            Vector2Int min, max;
            LandingRange(focusX, focusY, halfX, halfY, mapW, mapH, out min, out max);
            var chunksX = (mapW + ChunkSize - 1) / ChunkSize;
            var chunksY = (mapH + ChunkSize - 1) / ChunkSize;
            PlannedChunks(true, min.x, min.y, max.x, max.y, chunksX, chunksY, into);
        }

        /// <summary>
        /// ★ revive-chunk：**大跨度落位预建** —— 落位当帧把落点范围的缺块入队（⛔ 不建节点、⛔ 不重铺）。
        /// <para>调用点 = `MapModule.OnPlayerGridChanged`（<see cref="IsLargeShift"/> 为真时）⇒ 覆盖**所有**
        /// "玩家格坐标一步大跨度变化"的情形（死亡重生 / `IPlayerModule.TeleportTo` / 未来的位移技能），
        /// ⛔ 不是给 `Revive()` 打的单点补丁。</para>
        /// <para>返回 = 真正新入队的块数（0 = 无需预建：小图 / 未铺装 / 换区重铺进行中 / 落点范围的块都已就位）。
        /// 非预期分支（拿不到相机跨度 / 层根被销毁 / 换区重铺进行中 / 小图）都有日志。</para>
        /// </summary>
        public int PrimeLanding(Vector2Int focus, string why)
        {
            if (_map == null || !_showing)
            {
                MapLog.Info($"[revive-chunk] 落位预建跳过：地图未铺装（focus={focus} why={why}）");
                return 0;
            }
            if (!_chunked)
            {
                MapLog.Info($"[revive-chunk] 落位预建跳过：小图不分块（{_map.Width}x{_map.Height} ≤ " +
                            $"{BuildAllTileThreshold}）⇒ 从不回收块、无缺块窗口（focus={focus} why={why}）");
                return 0;
            }
            if (_hasLandingFocus)
            {
                // 换区重铺正在进行：`_landingFocus` 已被 `ShowArea` 占成新区域出生点，那条路径（travel-black）
                // 本来就在按落点铺整图 ⇒ 这里绝不能改写它的口径。
                MapLog.Info($"[revive-chunk] 落位预建跳过：换区重铺进行中（落点口径已被 ShowArea 占用，" +
                            $"focus={focus} why={why}）");
                return 0;
            }
            if (_groundRoot == null || _objectRoot == null || _overlayRoot == null)
            {
                MapLog.WarnThrottled("view.prime.nolayers",
                    "PrimeLanding: 层根节点不存在（已随场景卸载）⇒ 本次不做落位预建");
                return 0;
            }

            int hx, hy;
            if (!ViewHalfExtentCells(out hx, out hy))
            {
                MapLog.Warn("[revive-chunk] 落位预建：拿不到相机视口跨度 ⇒ 按 0 格半跨算" +
                            "（只预建落点所在块及其外扩 1 块，可能补不满落点画面）");
                hx = 0; hy = 0;
            }

            var plan = new List<Vector2Int>();
            PrimeChunks(focus.x, focus.y, hx, hy, _map.Width, _map.Height, plan);
            if (plan.Count == 0) return 0;

            var x0 = int.MaxValue;
            var y0 = int.MaxValue;
            var x1 = int.MinValue;
            var y1 = int.MinValue;
            var queued = 0;
            for (var i = 0; i < plan.Count; i++)
            {
                var c = plan[i];
                if (c.x < x0) x0 = c.x;
                if (c.y < y0) y0 = c.y;
                if (c.x > x1) x1 = c.x;
                if (c.y > y1) y1 = c.y;
                if (_groundChunks.ContainsKey(c)) continue;   // 已建好
                if (_pendingChunks.Contains(c)) continue;     // 已在待建队列
                _pendingChunks.Enqueue(c);
                queued++;
            }

            _primeActive = true;
            _primeUntil = Time.unscaledTime + PrimeLandingWindow;
            _primeMin = new Vector2Int(x0, y0);
            _primeMax = new Vector2Int(x1, y1);
            _primeQueued = queued;
            _landingFocus = focus;      // 窗口内 `PlanRange` 也按落点算（与 travel-black 同口径）

            MapLog.Info($"[revive-chunk] 大跨度落位预建（{why}）：落点 {focus}、视口半跨 {hx}x{hy} 格 ⇒ " +
                        $"范围块 ({x0},{y0})-({x1},{y1}) 共 {plan.Count} 块；**当帧**新入队 {queued} 块" +
                        $"（已建 {_groundChunks.Count} / 待建 {_pendingChunks.Count}）；" +
                        $"建块仍按 MaxChunksPerFrame={MaxChunksPerFrame} 摊平（⛔ 未调性能参数），" +
                        $"预建窗口 {PrimeLandingWindow:0.##}s 内该范围的块不被回收/丢弃");
            return queued;
        }

        /// <summary>
        /// ★ revive-chunk：预建窗口收尾（`Update` 每帧一次）—— 到期即撤下"落点口径"，
        /// 回收/丢弃恢复按相机算（⛔ 窗口不会无限期留着 ⇒ 不会长期多留块）。
        /// </summary>
        private void TickPrimeLanding()
        {
            if (!_primeActive) return;
            if (Time.unscaledTime < _primeUntil) return;
            _primeActive = false;
            MapLog.Info($"[revive-chunk] 落点预建窗口结束（{PrimeLandingWindow:0.##}s）：入队 {_primeQueued} 块、" +
                        $"已建 {_groundChunks.Count} / 待建 {_pendingChunks.Count}（回收口径恢复按相机算）");
        }

        /// <summary>★ revive-chunk：该块是否在**预建窗口**的落点范围内（窗口内不许被回收 / 从队列里撤掉）。</summary>
        private bool InPrimeRange(Vector2Int c)
        {
            return _primeActive
                   && c.x >= _primeMin.x && c.x <= _primeMax.x
                   && c.y >= _primeMin.y && c.y <= _primeMax.y;
        }

        /// <summary>
        /// 当前视口的**格半跨**（四角与屏幕中心的格坐标差的最大值；正交相机 ⇒ 与相机在哪无关，只与视口大小有关）。
        /// 返回 false = 拿不到相机（那时退回按相机算，并 Warn）。
        /// </summary>
        private bool ViewHalfExtentCells(out int halfX, out int halfY)
        {
            halfX = 0;
            halfY = 0;
            var cam = ViewCamera != null ? ViewCamera : Camera.main;
            if (cam == null) return false;
            var c = Iso.ScreenToGrid(cam, new Vector3(Screen.width * 0.5f, Screen.height * 0.5f, 0f));
            var minX = int.MaxValue; var minY = int.MaxValue;
            var maxX = int.MinValue; var maxY = int.MinValue;
            for (var i = 0; i < 4; i++)
            {
                var sx = (i & 1) == 0 ? 0f : Screen.width;
                var sy = (i & 2) == 0 ? 0f : Screen.height;
                var g = Iso.ScreenToGrid(cam, new Vector3(sx, sy, 0f));
                minX = Mathf.Min(minX, g.x); minY = Mathf.Min(minY, g.y);
                maxX = Mathf.Max(maxX, g.x); maxY = Mathf.Max(maxY, g.y);
            }
            halfX = Mathf.Max(Mathf.Abs(minX - c.x), Mathf.Abs(maxX - c.x));
            halfY = Mathf.Max(Mathf.Abs(minY - c.y), Mathf.Abs(maxY - c.y));
            return true;
        }

        /// <summary>
        /// 本次重铺该建哪一片块（**唯一口径**，`StartRebuild` / `RebuildImmediate` / 交换前对账三处共用）：
        /// 换区落位待完成时 = <see cref="LandingRange"/>（落点），否则 = `ComputeVisibleChunkRange`（相机）。
        /// </summary>
        private void PlanRange(out int x0, out int y0, out int x1, out int y1)
        {
            var min = Vector2Int.zero;
            var max = Vector2Int.zero;
            // ★ revive-chunk：预建窗口内（刚发生大跨度落位）也按**落点**算 —— 否则窗口里若来一次贴图驱动的
            //   整图重铺，会按"还在半路上的相机"算范围（与 travel-black 同一形态的黑窗）。
            if (_hasLandingFocus || _primeActive)
            {
                int hx, hy;
                if (ViewHalfExtentCells(out hx, out hy))
                {
                    LandingRange(_landingFocus.x, _landingFocus.y, hx, hy, _map.Width, _map.Height, out min, out max);
                    x0 = min.x; y0 = min.y; x1 = max.x; y1 = max.y;
                    MapLog.Info($"[travel-black] 本次重铺范围按**落点** {_landingFocus}（视口半跨 {hx}×{hy} 格）" +
                                $"⇒ 块 ({x0},{y0})-({x1},{y1})；相机此刻在旧区，不参与算范围");
                    return;
                }
                MapLog.Warn("[travel-black] 落点重铺：拿不到相机视口跨度 ⇒ 本次退回按相机算（可能建错范围）");
            }
            ComputeVisibleChunkRange(out min, out max);
            x0 = Mathf.Max(0, min.x); y0 = Mathf.Max(0, min.y);
            x1 = max.x; y1 = max.y;
        }

        /// <summary>本次整图重铺要建的块范围（<see cref="PlanRange"/> 的薄壳，两处调用点共用）。</summary>
        private void ComputeRebuildRange(out Vector2Int min, out Vector2Int max)
        {
            int x0, y0, x1, y1;
            PlanRange(out x0, out y0, out x1, out y1);
            min = new Vector2Int(x0, y0);
            max = new Vector2Int(x1, y1);
        }

        /// <summary>★ travel-black：交换前的对账最多追加几轮（非预期态 —— 相机每帧都在跑 —— 的兜底，⛔ 不无限拖）。</summary>
        private const int MaxExtendPasses = 4;

        /// <summary>
        /// ★ travel-black：交换**之前**的完整性对账（纯逻辑，⛔ 不建节点、不碰画面）：
        /// 目标范围（<see cref="PlanRange"/>）里"清单里还没有"的块追加进 <paramref name="job"/> 的清单并返回 true
        /// ⇒ 本帧不交换、下一帧继续建。
        /// <para>为什么必须有它：`SwapToBuilt` 是**撤掉整屏旧地砖**的那一刻。只要新集不含"交换那一帧的可见块"，
        /// 交换本身就制造黑屏（chunk-ctl 实测的 `patched T1` 就是这一形态：`MISSING=0 / job=null` 仍黑）。
        /// 追加的块不是"多余的图" —— 它们正是**那一帧屏幕上要看的块**。</para>
        /// </summary>
        private bool ExtendJobToPlanRange(RebuildJob job)
        {
            if (!_chunked) return false;                  // 小图 = 全图清单，恒覆盖（mapcheck §27 已穷举）
            int x0, y0, x1, y1;
            PlanRange(out x0, out y0, out x1, out y1);

            List<Vector2Int> add = null;
            for (var cx = x0; cx <= x1; cx++)
            {
                for (var cy = y0; cy <= y1; cy++)
                {
                    var c = new Vector2Int(cx, cy);
                    if (job.Chunks.Contains(c)) continue;
                    if (add == null) add = new List<Vector2Int>();
                    add.Add(c);
                }
            }
            if (add == null) return false;

            if (job.Extends >= MaxExtendPasses)
            {
                MapLog.Warn($"[travel-black] 交换前对账已达上限 {MaxExtendPasses} 轮，仍有 {add.Count} 块不在清单里" +
                            $"（相机在重铺期间一直在跑？）⇒ 本次**照旧交换**，缺口交由增量补块路径补齐（非预期态）");
                return false;
            }

            job.Extends++;
            job.Chunks.AddRange(add);
            var next = job.Chunks[job.ChunkIndex];
            job.CursorX = next.x * ChunkSize;
            job.CursorY = next.y * ChunkSize;
            MapLog.Info($"[travel-black] 交换前对账：清单缺 {add.Count} 块（第 {job.Extends}/{MaxExtendPasses} 轮）" +
                        $"⇒ 追加进本次清单（块 {job.Chunks.Count} 个），⛔ 本帧不交换（否则交换本身制造黑屏）");
            return true;
        }

        /// <summary>
        /// ★ travel-black：欠着的那次"换区铺装完成"通知 —— 发一次 <see cref="Events.MapAreaReady"/>，并把
        /// 落点范围口径收掉（此后重铺恢复按相机算）。
        /// <para>收方 = `Module/Flow/AppFlow.cs`：它收到才挪玩家/相机/怪 + 关读条屏 ⇒ **落地那一帧**渲染出来
        /// 的就是完整的新区域（旧区画面一直保留到这一刻）。</para>
        /// </summary>
        private void NotifyAreaReadyIfOwed(string why)
        {
            if (!_areaReadyOwed) return;
            _areaReadyOwed = false;
            _hasLandingFocus = false;
            MapLog.Info($"[travel-black] 换区铺装完成并已切换（{why}）⇒ 发 {Events.MapAreaReady}，" +
                        "AppFlow 收到才挪玩家/相机（那一帧渲染出来的就是完整新图，⛔ 中间帧不会出现空屏）");
            try
            {
                if (Game.Event != null) Game.Event.Emit(Events.MapAreaReady);
            }
            catch (System.Exception ex)
            {
                MapLog.Error($"[travel-black] 发 {Events.MapAreaReady} 失败（{ex.GetType().Name}: {ex.Message}）" +
                             "⇒ AppFlow 的落位会超时兜底（见 AppFlow.OnStageTick）");
            }
        }

        /// <summary>
        /// 建一块（3 个块根 + 逐格）。
        /// <para>★ T0FIX-A：块根一建出来先 `SetActive(false)`，**块内全部格建完才激活**
        /// ⇒ 任何时刻都不会出现"半块地图"可见态（分帧也安全）。同帧建完时激活点仍是同一帧
        /// ⇒ 画面与改前逐像素一致。节点全部走 <see cref="EnsurePool"/> 的池（冷池才 `new`）。</para>
        /// </summary>
        private void BuildChunk(Vector2Int chunk)
        {
            if (_groundChunks.ContainsKey(chunk)) return;   // 已铺过

            var name = $"Chunk_{chunk.x}_{chunk.y}";
            var ground = NewChild(_groundRoot, name);
            var obj = NewChild(_objectRoot, name);
            var overlay = NewChild(_overlayRoot, name);
            ground.gameObject.SetActive(false);
            obj.gameObject.SetActive(false);
            overlay.gameObject.SetActive(false);

            _groundChunks[chunk] = ground;
            _objectChunks[chunk] = obj;
            _overlayChunks[chunk] = overlay;

            var x0 = chunk.x * ChunkSize;
            var y0 = chunk.y * ChunkSize;
            var x1 = Mathf.Min(x0 + ChunkSize, _map.Width);
            var y1 = Mathf.Min(y0 + ChunkSize, _map.Height);

            for (var x = x0; x < x1; x++)
            {
                for (var y = y0; y < y1; y++) BuildCell(new Vector2Int(x, y), chunk);
            }

            // 块内全部格建完 ⇒ 三个块根一起激活（同一帧激活点 ⇒ 不出现半块）
            ground.gameObject.SetActive(true);
            obj.gameObject.SetActive(true);
            overlay.gameObject.SetActive(true);
        }

        /// <summary>
        /// 建一格（**增量路径**用：目标是当前可见集的块）。
        /// <para>★ T0FIX-H：判定与落地拆成 `PlanCell`（纯函数、零节点）+ `ApplyCellPlan`（建节点）——
        /// 分帧预算必须**在建节点之前**知道本格要几个节点，否则单帧会超预算。
        /// 本方法 = 两者的薄壳（判定逐条与改前 `BuildCell` 同源）。</para>
        /// </summary>
        private void BuildCell(Vector2Int g, Vector2Int chunk)
        {
            var plan = PlanCell(_map, _area, g);
            if (!plan.Draw) return;
            ApplyCellPlan(plan, g, _groundChunks[chunk], _objectChunks[chunk], _overlayChunks[chunk]);
        }

        /// <summary>★ T0FIX-H：一格的**建/画决定**（纯值；`NodeCount` = 本格要几个节点：0/1/2）。</summary>
        internal readonly struct CellPlan
        {
            /// <summary>这格要不要画（false = `TileKind.Void`：图外/未生成 —— 连迷雾都不画）。</summary>
            public readonly bool Draw;

            /// <summary>地面层瓦片键（null/"" = 这格不画地面：原版洞穴的纯黑岩体就是这样）。</summary>
            public readonly string GroundKey;

            /// <summary>地面层用的 `TileKind`（决定占位色）。</summary>
            public readonly TileKind GroundKind;

            /// <summary>物件层 `TileKind`（决定占位色）。</summary>
            public readonly TileKind ObjectKind;

            /// <summary>物件层瓦片键（仅 <see cref="DrawObject"/> 为真时有效；null = 纯色占位）。</summary>
            public readonly string ObjectKey;

            /// <summary>物件层画不画（已含 `IsHiddenSolidInterior` 排除）。</summary>
            public readonly bool DrawObject;

            /// <summary>R1-B：本格的平色水墙瓦片被"不叠"跳过了（只用于计数）。</summary>
            public readonly bool SkipFlatWall;

            /// <summary>构造（唯一入口；全部字段显式给）。</summary>
            public CellPlan(bool draw, string groundKey, TileKind groundKind, TileKind objectKind,
                bool drawObject, string objectKey, bool skipFlatWall)
            {
                Draw = draw;
                GroundKey = groundKey;
                GroundKind = groundKind;
                ObjectKind = objectKind;
                DrawObject = drawObject;
                ObjectKey = objectKey;
                SkipFlatWall = skipFlatWall;
            }

            /// <summary>本格要建的节点数（0 = 什么都不画；1 = 只有一层；2 = 两层都画）。</summary>
            public int NodeCount
            {
                get
                {
                    var n = 0;
                    if (!string.IsNullOrEmpty(GroundKey)) n++;
                    if (DrawObject) n++;
                    return n;
                }
            }
        }

        /// <summary>
        /// ★ T0FIX-H **纯函数**（离线可断言）：一格要画什么。⛔ 零副作用（不建节点、不动
        /// `_flatWallOverlaySkipped`、不请求贴图）—— 与 <see cref="ApplyCellPlan"/> 的分工见 `BuildCell`。
        /// <para>判定逐条与改前 `BuildCell` 同源：逐格「原版瓦片键」覆盖（罗格营地 / 邪恶洞穴）、
        /// 地面层排序下移、物件层不做 `TileKind` 兜底、R1-B 平色水墙不叠、洞穴实心岩体不画物件。</para>
        /// </summary>
        internal static CellPlan PlanCell(GridMap map, AreaId area, Vector2Int g)
        {
            var kind = map.Get(g);
            if (kind == TileKind.Void) return new CellPlan(false, null, kind, kind, false, null, false);

            // ── 逐格「原版瓦片键」覆盖：**罗格营地**（`MapGenTownLayout`，源 `townW1.ds1`）
            //    与**邪恶洞穴**（`MapGenCaveLayout`，源 `CAVES/*.ds1`）都用它。
            //    ★ 语义（见 `GridMap.TryGetTiles` 注释）：
            //      · 返回 false ⇒ 本图没有逐格覆盖（= 野外），按 `TileKind` 分类取默认瓦片；
            //      · 返回 true 且 groundKey == "" ⇒ **原版这格不画**（洞穴里的纯黑实心岩体就是
            //        这种格），⛔ 不许兜底成占位菱形 —— 兜底会把它变成一堆灰方块。
            string ds1Ground = null, ds1Object = null;
            var fromDs1 = map.TryGetTiles(g.x, g.y, out ds1Ground, out ds1Object);

            // ── 地面层 ──
            // ★ 地面整体下移一个排序步长：见文件头「地面层为什么额外 -SortOrderStep」
            var groundKind = TileKindInfo.IsGroundLayer(kind) ? kind : BaseGroundOf(area);
            var groundKey = fromDs1 ? ds1Ground : GroundKeyOf(groundKind, area, g);

            // ── 物件层 ──
            // **有逐格覆盖的区域（营地 / 洞穴）**：只画原版那一格真的有的瓦片 —— 原版那格没有
            //   wall 层瓦片（水上、纯黑岩体、被连通性修整改成 Wall 的死地…）就**什么都不画**。
            //   ⛔ 这里刻意**不做** `TileKind` 兜底：兜底会画出一堆纯色占位方块（实测 170 个），
            //   比"没有物件"难看得多，而且掩盖了"原版这里本来就没东西"这个事实。
            // **其它区域（野外）**：按 `TileKind` 分类取默认瓦片；取不到才用纯色占位（便于发现问题）。
            var ds1HasObject = fromDs1 && !string.IsNullOrEmpty(ds1Object);
            // ★ R1-B：**平色水墙瓦片不叠**（该格 floor 层已经是同一 dt1 的水瓦片 ⇒ 河面由它呈现）。
            //   口径与出处见 `PaletteCycledFlatWallTiles` / `IsPaletteCycledFlatWallOverlay` 的注释；
            //   ⛔ 只影响本帧画不画这一张物件，**不动** `GridMap` 的键 / `TileKind` / 可走性。
            var skipFlatWallOverlay = ds1HasObject && IsPaletteCycledFlatWallOverlay(ds1Ground, ds1Object);

            var drawObject = fromDs1 ? (ds1HasObject && !skipFlatWallOverlay) : IsObjectKind(kind);
            if (drawObject && IsHiddenSolidInterior(map, g, kind)) drawObject = false;

            var objectKey = drawObject
                ? (ds1HasObject ? ds1Object : (fromDs1 ? null : ObjectKeyOf(kind, area, g)))
                : null;

            return new CellPlan(true, groundKey, groundKind, kind, drawObject, objectKey, skipFlatWallOverlay);
        }

        /// <summary>
        /// ★ T0FIX-H：把一格的计划**落地**（建节点），返回本格**新建的节点数**（恒等于
        /// <see cref="CellPlan.NodeCount"/> —— 帧预算就是按它扣的）。
        /// <para>本方法**不含任何决定**（决定全在 `PlanCell`）；贴图请求（`TrySprite` 的异步侧效）
        /// 与 R1-B 计数都在这里，顺序与改前 `BuildCell` 逐字一致：地面 → 物件 → 迷雾。</para>
        /// </summary>
        private int ApplyCellPlan(CellPlan p, Vector2Int g, Transform ground, Transform obj, Transform overlay)
        {
            if (!p.Draw) return 0;      // Void：连迷雾都不画（与改前 `BuildCell` 的早退同口径）

            var n = 0;

            if (!string.IsNullOrEmpty(p.GroundKey))
            {
                var groundSprite = TrySprite(ResPaths.Tile(p.GroundKey));
                NewTile(ground, GroundState(groundSprite, p.GroundKind, g));
                n++;
            }

            if (p.SkipFlatWall) _flatWallOverlaySkipped++;

            if (p.DrawObject)
            {
                var objectSprite = p.ObjectKey != null ? TrySprite(ResPaths.ObjectSprite(p.ObjectKey)) : null;
                NewTile(obj, ObjectState(objectSprite, p.ObjectKind, g));
                n++;
            }

            // ── 遮蔽层（迷雾）──
            if (_fogOn) CreateFog(g, overlay);
            return n;
        }

        /// <summary>建一格的迷雾（目标 = 那一层的块根；`_fogTiles` 记录节点以便 `MarkExplored` 直接关掉）。</summary>
        private void CreateFog(Vector2Int g, Transform overlay)
        {
            if (_fogTiles == null || _explored == null) return;
            if (_explored[g.x, g.y]) return;
            if (_fogTiles[g.x, g.y] != null) return;

            _fogTiles[g.x, g.y] = NewTile(overlay, FogState(g));
        }

        /// <summary>
        /// 洞穴实心岩体（`CaveWall` 且四周没有一格可走）不画物件层：
        /// 原版洞穴里那些区域是**全黑**的，画出来反而多余，也省下几千个 GameObject。
        /// <para>★ T0FIX-H：改成**静态**（吃 `map` 参数）—— `PlanCell` 是纯函数，它必须能被离线复算。</para>
        /// </summary>
        private static bool IsHiddenSolidInterior(GridMap map, Vector2Int g, TileKind kind)
            => kind == TileKind.CaveWall && !map.HasWalkableNeighbor(g);

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
                    RequestRepaint();              // ★ R1-D：贴图异步到位 → 登记重铺（是否合并见 ShouldRepaintNow）
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
                // ★ 片 L / R12：水**显式登记为"不是物件"**（原版水面是 floor 层）。
                //   显式写出来 = 即便将来 default 改成 true，水也不会被画成石头/崖壁的物件。
                case TileKind.Water:
                    return false;
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

        /// <summary>
        /// 水面（`TileKind.Water`）的 floor 瓦片 —— 原版 `ACT1/OUTDOORS/river.dt1` 解出的
        /// `Tiles/moor_river/*`（44 张水面瓦片，见 `PaletteCycledFlatWallTiles` 的 R1-B 取证）。
        /// <para>
        /// ★ 片 L / R12：本表按**原版数据**取，不靠挑图 —— 键集合 = `MapGenTownLayout` 里
        /// 全部 `'r'` 格实际引用的 floor 键（去重 **41** 个，离线脚本逐格解 6 字符 packId+idx 得到）。
        /// </para>
        /// <para>
        /// 只在**没有逐格原版瓦片键**的路径（`MapGenTownLayout.TryGetTiles` 返回 false 的保底布局）
        /// 才会被 `GroundKeyOf` 用到；城镇 / 野外 / 洞穴三图都走逐格键，所以这是**兜底**，
        /// 但它保证"保底布局下水面也不会退化成灰块"。
        /// </para>
        /// </summary>
        private static readonly string[] WaterTiles =
        {
            "moor_river/000", "moor_river/001", "moor_river/002", "moor_river/003", "moor_river/004",
            "moor_river/005", "moor_river/006", "moor_river/007", "moor_river/008", "moor_river/009",
            "moor_river/010", "moor_river/011", "moor_river/012", "moor_river/013", "moor_river/014",
            "moor_river/015", "moor_river/016", "moor_river/017", "moor_river/018", "moor_river/019",
            "moor_river/020", "moor_river/021", "moor_river/022", "moor_river/023", "moor_river/024",
            "moor_river/025", "moor_river/026", "moor_river/027", "moor_river/029", "moor_river/033",
            "moor_river/034", "moor_river/035", "moor_river/036", "moor_river/037", "moor_river/038",
            "moor_river/039", "moor_river/040", "moor_river/041", "moor_river/042", "moor_river/043",
            "moor_river/044",
        };

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

        // ═════════════════════════════════════════════════════════════════════
        // ★ R1-B：「靠 PL2 调色板循环成动画的**平色** wall 层瓦片」白名单 + 不叠判定
        //   （用户原始投诉：「为什么有奇怪的蓝条图片占位」—— 罗格营地东侧河上的深蓝硬边长条）
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>
        /// ★ R1-B：**平色**且**靠 `PL2` 调色板循环成动画**的 wall 层瓦片白名单。
        /// <para>
        /// 取证（离线判据 `.ai-tmp/test/r1b_probe.py`，扫的是 `MapGenTownLayout` 真正引用到的
        /// **295 个**瓦片键 → 逐张 PNG 采样）：其中**唯一色数 = 1（平色）的只有一个** ——
        /// `Objects/moor_river/028`（160×128，不透明像素 6400 px = 恰好一格的 160×79 菱形，
        /// 全图同色 **RGBA(0,32,68) 深蓝**）。它在原版 `river.dt1` 里是**水面**瓦片，原版靠
        /// `ACT1/Pal.PL2` 的调色板循环把它变成水波动画；本项目**没有运行期调色板循环**
        /// （全库 grep `PaletteCycle|调色板循环` = 0 命中；`MapView` 的真实瓦片一律 `sr.color = Color.white`）
        /// ⇒ 静态渲染出来就是**一块硬边平色色块**，视觉上等价占位图（用户看到的就是它）。
        /// </para>
        /// <para>出处链：`Objects/moor_river/manifest.json`（tileCount=1 / idx 28 / main 5 / sub 0 /
        /// orientation 1 / walk true）× `tools/d2codec/export_tiles.py:66`
        /// （`data/global/tiles/ACT1/OUTDOORS/river.dt1` → pack `moor_river`）×
        /// `Tiles/moor_river/manifest.json`（同一张 dt1 的 44 张 **floor** 水瓦片自带纹理，
        /// 唯一色 11~97，不透明像素同为 6400 px ⇒ 它们本来就铺满整格）。</para>
        /// <para>⛔ 这是**具体键的白名单**，不是「凡平色/透明就丢」的泛化规则：上述扫描证明本项目
        /// 被引用到的平色瓦片只有这一个；将来出现第二个必须**重新取证并登记**，不许把判定放宽。</para>
        /// </summary>
        private static readonly string[] PaletteCycledFlatWallTiles =
        {
            "moor_river/028",
        };

        /// <summary>
        /// 白名单的**只读视图**（离线自检宿主 `tools/probes/hosts/mapcheck` 的 `Step15` 用它钉住
        /// "恰 1 个键且就是取证出来的那一个"）。⛔ 业务代码不要用它做判定 —— 判定走
        /// <see cref="IsPaletteCycledFlatWallOverlay"/>。
        /// </summary>
        internal static string[] FlatWallTileWhitelist { get { return PaletteCycledFlatWallTiles; } }

        /// <summary>
        /// ★ R1-B：该格的 wall 层瓦片是否应该**不叠**（河面改由**同 dt1 的 floor 水瓦片**呈现）。
        /// <para>生效口径（三条**同时**成立）：</para>
        /// <para>① <paramref name="objectKey"/> 在 <see cref="PaletteCycledFlatWallTiles"/> 白名单里
        /// （= 平色 + 靠 PL2 循环）；</para>
        /// <para>② <paramref name="groundKey"/> **非空**（这格地板层已经有东西 —— 不叠不会让它变成空洞）；</para>
        /// <para>③ 两者**同一个 pack**（同一张 `.dt1`）⇒ 是"同一片水的两层"，不是"水面上压了别的东西"。</para>
        /// <para>实测效果：罗格营地河带 `x∈[47,54]` 里 `x=47` / `x=54` 两列的 **49 格**不再出现
        /// 那条硬边深蓝长条（`RebuildLayers` 会把这 49 格作为 `R1-B` 日志报一次）。</para>
        /// <para>⛔ 只影响**渲染**：`GridMap` 的逐格键、`TileKind`、可走性一个字都不动（桥/水的
        /// 可走性仍由 `MapGenTown` 的 kind 决定，`mapcheck` 的桥/水断言不受影响）。</para>
        /// </summary>
        internal static bool IsPaletteCycledFlatWallOverlay(string groundKey, string objectKey)
        {
            if (string.IsNullOrEmpty(groundKey) || string.IsNullOrEmpty(objectKey)) return false;
            if (!IsPaletteCycledFlatWallTile(objectKey)) return false;
            return SameDt1Pack(groundKey, objectKey);
        }

        /// <summary>该物件键是否就是白名单里那块平色水墙瓦片（逐字比较）。</summary>
        internal static bool IsPaletteCycledFlatWallTile(string objectKey)
        {
            if (string.IsNullOrEmpty(objectKey)) return false;
            for (var i = 0; i < PaletteCycledFlatWallTiles.Length; i++)
            {
                if (PaletteCycledFlatWallTiles[i] == objectKey) return true;
            }
            return false;
        }

        /// <summary>两个瓦片键是否来自**同一个 pack 目录**（键形如 `pack/idx`）。</summary>
        private static bool SameDt1Pack(string a, string b)
        {
            var sa = a.LastIndexOf('/');
            var sb = b.LastIndexOf('/');
            if (sa <= 0 || sb <= 0 || sa != sb) return false;
            return string.CompareOrdinal(a, 0, b, 0, sa) == 0;
        }

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
                // ★ 片 L / R12：水**有独立的地面瓦片表**（原版 `river.dt1` 水面）。
                //   ⛔ 不走 default（default = "未登记 ⇒ 纯色占位 + Warn"），否则水面退化成灰块。
                case TileKind.Water: set = WaterTiles; break;
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
                // ★ 片 L / R12：水**没有物件层**（原版水面是 floor 层；`moor_river/028` 那张平色水墙
                //   瓦片已由 R1-B 判为"不叠"）。显式分支 = 不靠 default 兜底，语义明确。
                case TileKind.Water: return null;
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
                // ★ 片 L / R12：水面的占位色 = **深蓝**（区分于岩石的灰）。只在贴图缺失时可见；
                //   取值照原版 R1-B 取证到的那张平色水瓦片 `Objects/moor_river/028` 的实测色
                //   RGBA(0,32,68)（= `PaletteCycledFlatWallTiles` 注释），不是随手挑的蓝。
                case TileKind.Water: return new Color(0f, 32f / 255f, 68f / 255f);
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

        /// <summary>取池（懒创建；池根挂在 `MapRoot` 下 ⇒ 随场景一起销毁，不跨场景泄漏）。</summary>
        private TileNodePool EnsurePool()
        {
            if (_pool != null) return _pool;
            if (_poolRoot == null) _poolRoot = NewChild(transform, "TileNodePool");
            _pool = new TileNodePool(_poolRoot);
            return _pool;
        }

        /// <summary>
        /// 一格瓦片的节点：**从池里取**（池空才新建），然后由
        /// <see cref="ApplyTileState"/> **无条件**重设全部渲染字段。
        /// <para>★ T0FIX-A：全工程**只有这一处**建/复用瓦片节点（`new GameObject` 只出现在
        /// <see cref="TileNodePool.Take"/> 的冷分支）⇒「新建」与「复用」不可能出现两种渲染结果。
        /// 断言见 `tools/probes/hosts/mapcheck` §17。</para>
        /// </summary>
        private SpriteRenderer NewTile(Transform parent, TileRenderState state)
        {
            var sr = EnsurePool().Take(parent);
            ApplyTileState(sr, parent, state);
            return sr;
        }

        /// <summary>
        /// 把一格瓦片的**全部**渲染字段无条件写到节点上（★ T0FIX-A 的"池化前后逐项相等"就靠这里）：
        /// <list type="number">
        ///   <item>`transform.SetParent(parent, false)` —— **追加到末尾** ⇒ 块内格序与新建时一致；</item>
        ///   <item>`sprite`；</item>
        ///   <item>`color`；</item>
        ///   <item>`transform.localScale`；</item>
        ///   <item>`transform.position`（世界坐标，含 x/y/z）；</item>
        ///   <item>`sortingOrder`；</item>
        ///   <item>`enabled` —— ⛔ **池化必须复位它**：迷雾节点会被 `MarkExplored` 置
        ///       `enabled = false`，若不复位，复用到它的一格会**静默不可见**。</item>
        /// </list>
        /// ⛔ 本方法里**不许**出现"是不是复用节点"的分支（一个字都不许）—— 一旦有分支，
        /// 「逐项相等」就不再是结构性保证。
        /// </summary>
        private static void ApplyTileState(SpriteRenderer sr, Transform parent, TileRenderState st)
        {
            sr.name = st.Sprite != null ? "T" : "T_placeholder";
            sr.transform.SetParent(parent, false);
            sr.sprite = st.Sprite;
            sr.color = st.Color;
            sr.transform.localScale = st.LocalScale;
            sr.transform.position = st.Position;
            sr.sortingOrder = st.SortingOrder;
            sr.enabled = true;
        }

        // ═════════════════════════════════════════════════════════════════════
        // ★ T0FIX-A：一格瓦片的**渲染状态**（纯函数；离线可断言、与节点/池无关）
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>地面层一格的状态（`kind` 决定占位色；有贴图则色 = 白、按 80 px/单位缩放）。</summary>
        internal static TileRenderState GroundState(Sprite sprite, TileKind kind, Vector2Int g)
        {
            return new TileRenderState(
                sprite,
                ColorFor(sprite != null, kind, true),
                LocalScaleFor(sprite != null),
                PlaceOf(Iso.GridToWorld(g), sprite, true),
                Iso.SortOrder(g, GameConst.LayerOffsetGround) - GameConst.SortOrderStep);
        }

        /// <summary>物件层一格的状态。</summary>
        internal static TileRenderState ObjectState(Sprite sprite, TileKind kind, Vector2Int g)
        {
            return new TileRenderState(
                sprite,
                ColorFor(sprite != null, kind, false),
                LocalScaleFor(sprite != null),
                PlaceOf(Iso.GridToWorld(g), sprite, false),
                Iso.SortOrder(g, GameConst.LayerOffsetObject));
        }

        /// <summary>迷雾（遮蔽层）一格的状态：无贴图 + 固定暗色（恒压在格中心）。</summary>
        internal static TileRenderState FogState(Vector2Int g)
        {
            return new TileRenderState(
                null,
                new Color(0f, 0f, 0f, FogAlpha),
                LocalScaleFor(false),
                Iso.GridToWorld(g),
                Iso.SortOrder(g, GameConst.LayerOffsetOverlay));
        }

        /// <summary>节点缩放：有原版贴图 ⇒ `契约PPU / 80`（见 <see cref="D2TilePixelsPerUnit"/>）；占位菱形 ⇒ 1。</summary>
        internal static Vector3 LocalScaleFor(bool hasSprite)
        {
            return hasSprite
                ? Vector3.one * (GameConst.PixelsPerUnit / D2TilePixelsPerUnit)
                : Vector3.one;
        }

        /// <summary>节点颜色：有原版贴图 ⇒ 白（原版像素不能被染色）；占位 ⇒ 可辨的占位色。</summary>
        internal static Color ColorFor(bool hasSprite, TileKind kind, bool isGround)
        {
            if (hasSprite) return Color.white;
            return isGround ? GroundColor(kind) : ObjectColor(kind);
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
            => PlaceOfPx(cellCenter, sprite != null ? Mathf.RoundToInt(sprite.rect.height) : 0, isFloor);

        /// <summary>
        /// <see cref="PlaceOf"/> 的**纯内核**（接口只吃"图像高(px)"，⛔ 不碰 `Sprite`）
        /// —— 离线自检宿主因此能逐格复算并断言"画面逐像素不变"（mapcheck §17）。
        /// </summary>
        /// <param name="cellCenter">格中心的世界坐标（`Iso.GridToWorld`）。</param>
        /// <param name="spriteHeightPx">图像高（px）；0 = 占位菱形（与格同心，不做对齐修正）。</param>
        /// <param name="isFloor">true = 地砖（顶边贴格中心上方半格）；false = 墙/物件（底边贴下方半格）。</param>
        internal static Vector3 PlaceOfPx(Vector3 cellCenter, int spriteHeightPx, bool isFloor)
        {
            if (spriteHeightPx <= 0) return cellCenter;         // 占位菱形本来就与格同心

            var h = spriteHeightPx / D2TilePixelsPerUnit;       // 图像在世界单位下的高
            var dy = isFloor
                ? GameConst.IsoHalfH - h * 0.5f             // 顶边在 +halfH ⇒ 中心下移
                : h * 0.5f - GameConst.IsoHalfH;            // 底边在 -halfH ⇒ 中心上移
            return new Vector3(cellCenter.x, cellCenter.y + dy, cellCenter.z);
        }

        // ═════════════════════════════════════════════════════════════════════
        // 回收 / 帧循环
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>
        /// 拆掉全部块。★ T0FIX-A：块内节点**归还池**（不再 `Destroy`），只销毁 3 个块根空节点。
        /// 归还的节点已 `SetParent(池根)` ⇒ 块根被销毁时**不会**连带销毁它们。
        /// </summary>
        private void DestroyAllChunks()
        {
            RecycleChildren(_groundRoot);
            RecycleChildren(_objectRoot);
            RecycleChildren(_overlayRoot);
            // ★ T0FIX-H：**缓冲集也要清**（它可能装着"上一次重铺"的旧内容或"建到一半"的新内容）
            RecycleChildren(_bufGroundRoot);
            RecycleChildren(_bufObjectRoot);
            RecycleChildren(_bufOverlayRoot);
            _retireChunks.Clear();            // 待回收队列里的块根已被上面两轮覆盖 ⇒ 不作废队列 = 悬空引用
            _groundChunks.Clear();
            _objectChunks.Clear();
            _overlayChunks.Clear();
            _bufGroundChunks.Clear();
            _bufObjectChunks.Clear();
            _bufOverlayChunks.Clear();
        }

        /// <summary>回收可见范围之外的块（外层留 1 块缓冲，避免来回走动时反复重建）。</summary>
        private void ReleaseFarChunks(int keepX0, int keepY0, int keepX1, int keepY1)
        {
            ReleaseFarChunksIn(_groundChunks, keepX0, keepY0, keepX1, keepY1);
            ReleaseFarChunksIn(_objectChunks, keepX0, keepY0, keepX1, keepY1);
            ReleaseFarChunksIn(_overlayChunks, keepX0, keepY0, keepX1, keepY1);
            DropFarPendingChunks(keepX0, keepY0, keepX1, keepY1);
        }

        /// <summary>
        /// ★ T0FIX-A：把"待建队列里已经走远"的块撤掉（否则会补一块**永远看不到**的图，
        /// 白花帧预算；也不会有第二次机会被回收）。
        /// </summary>
        private void DropFarPendingChunks(int keepX0, int keepY0, int keepX1, int keepY1)
        {
            if (_pendingChunks.Count == 0) return;

            var keep = new List<Vector2Int>(_pendingChunks.Count);
            var dropped = 0;
            while (_pendingChunks.Count > 0)
            {
                var c = _pendingChunks.Dequeue();
                if (c.x >= keepX0 && c.x <= keepX1 && c.y >= keepY0 && c.y <= keepY1) keep.Add(c);
                else if (InPrimeRange(c)) keep.Add(c);   // ★ revive-chunk：预建窗口内不撤落点范围的待建块
                else dropped++;
            }
            for (var i = 0; i < keep.Count; i++) _pendingChunks.Enqueue(keep[i]);

            if (dropped > 0)
                MapLog.Info($"MapView: 待建队列里撤掉 {dropped} 个已经走远的块（剩余待建 {_pendingChunks.Count}）");
        }

        private void ReleaseFarChunksIn(Dictionary<Vector2Int, Transform> dict, int x0, int y0, int x1, int y1)
        {
            List<Vector2Int> drop = null;
            foreach (var kv in dict)
            {
                var c = kv.Key;
                if (c.x >= x0 && c.x <= x1 && c.y >= y0 && c.y <= y1) continue;
                // ★ revive-chunk：预建窗口内落到"落点范围"的块**保住** —— 此刻相机还停在旧处，
                //   按它算出来的保留带不含落点 ⇒ 不保就会"刚建好又被回收"（预建白做 + 反复建/销毁）。
                if (InPrimeRange(c)) continue;
                if (drop == null) drop = new List<Vector2Int>();
                drop.Add(c);
            }
            if (drop == null) return;

            for (var i = 0; i < drop.Count; i++)
            {
                var node = dict[drop[i]];
                if (node != null) RecycleChunk(node);
                dict.Remove(drop[i]);
            }
        }

        /// <summary>
        /// 把一个层根下的**全部块**回收成池（块内节点归还、块根销毁）。
        /// </summary>
        private void RecycleChildren(Transform root)
        {
            if (root == null) return;
            for (var i = root.childCount - 1; i >= 0; i--) RecycleChunk(root.GetChild(i));
        }

        /// <summary>
        /// 回收一个块：块内所有瓦片节点归还池 ⇒ 再销毁块根空节点。
        /// <para>★ T0FIX-H：**返回归还的节点数**（供 <see cref="PumpRetire"/> 按帧预算摊平；
        /// 已销毁的块根判空返回 0 ⇒ 保底/取消路径上的悬空引用不会抛异常）。</para>
        /// </summary>
        private int RecycleChunk(Transform chunkRoot)
        {
            if (chunkRoot == null) return 0;             // 已随场景卸载销毁：跳过（不静默留脏引用）
            var n = 0;
            for (var i = chunkRoot.childCount - 1; i >= 0; i--)
            {
                var child = chunkRoot.GetChild(i);
                var sr = child.GetComponent<SpriteRenderer>();
                if (sr != null && _pool != null)
                {
                    _pool.Return(sr);
                    n++;
                }
                else Destroy(child.gameObject);          // 非预期形态（不是瓦片节点）：照旧销毁，不静默留孤儿
            }
            Destroy(chunkRoot.gameObject);
            return n;
        }

        private void Update()
        {
            if (!_showing || _map == null) return;

            // ★ agent-16：本组件挂在 `MapRoot` 上，正常会随场景一起销毁 ⇒ `Update` 自然不再被调；
            //   但**层根被单独销毁 / 销毁延时一帧**的窗口里仍可能进来 ⇒ 先过一道安全闸门。
            if (_groundRoot == null && _objectRoot == null && _overlayRoot == null) return;

            if (_repaintRequested)
            {
                // ★ R1-D：贴图流式到位期间**合并**重铺（旧口径 = 每来一张贴图就整图重建一次），
                //   是否到点由纯函数决定（超时兜底保证贴图一定会换上）。见 `ShouldRepaintNow`。
                var now = Time.unscaledTime;
                // ★ T0FIX-H：上一次重铺**还在进行/还在回收**时**不消费**这次请求（否则会把
                //   "建到一半的缓冲集"丢掉重来 ⇒ 白干 + 多一次销毁尖峰）；留在下一帧再判。
                if ((_job == null && _retireChunks.Count == 0)
                    && ShouldRepaintNow(now, _repaintFirstAt, _repaintLastAt, _lastRepaintAt))
                {
                    _repaintRequested = false;
                    _repaintFirstAt = -1f;
                    _lastRepaintAt = now;
                    RebuildLayers();
                }
                // ⛔ 这里**不 return**（T0FIX-A 的旧口径是 return）：重铺已改成分帧，
                //   必须每帧继续泵（下面几步就是它）。新开的任务在同一帧就吃到第一份预算。
            }

            // ★ chunk-hole 修复（2026-09-23）：**登记**（只入队、不建块）不再被重铺/回收挡住。
            //   旧口径把 `RefreshVisibleChunks` 放在两个 early-return 之后 ⇒ 整图重铺进行中的那几秒
            //   （实测 1~4 s：重铺每帧 ≈ 0.3 s）相机走进新区域时**没人登记新块**，`PendingChunks=0`
            //   而 `MISSING=4`，切完之后若登记范围恰好没再变 ⇒ 洞**永久**留着（血沼泽大片黑）。
            //   登记本身零节点、幂等（已建/已在队的块会跳过），建块仍由下面的
            //   `PumpChunkBuild` 在重铺/回收结束之后按帧预算做 —— 两套铺装**依然不会**互相打架。
            _builtThisFrame = 0;
            if (_chunked && Time.unscaledTime >= _nextChunkRefresh)
            {
                _nextChunkRefresh = Time.unscaledTime + ChunkRefreshInterval;
                RefreshVisibleChunks();          // ★ T0FIX-A：只**登记**新进入范围的块（本帧不建）
            }

            // ★ revive-chunk：落位预建窗口收尾（到期即撤下"落点口径"，回收恢复按相机算）
            TickPrimeLanding();

            // ★ T0FIX-H：分帧重铺进行中 ⇒ 本帧只泵它（增量建块等它做完，避免两套铺装互相打架）
            if (_job != null)
            {
                PumpRebuild();
                return;
            }

            // ★ T0FIX-H：旧集还没回收完 ⇒ 先按帧预算回收（它已不可见，不影响画面）
            if (_retireChunks.Count > 0)
            {
                PumpRetire(MaxTileNodesPerFrame);
                return;
            }

            // ★ T0FIX-A：每帧至多建 `MaxChunksPerFrame` 块 —— 单帧尖峰就此摊平。
            //   队列空时是 0 开销（小图 `!_chunked` 从不入队 ⇒ 等价于旧口径的"无逐帧工作"）。
            PumpChunkBuild(MaxChunksPerFrame);
        }
    }
}
