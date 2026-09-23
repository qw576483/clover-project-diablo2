// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · UI/MiniMapPanel.cs
// 自动地图（原版 Tab 切换）**按原版口径重画**：满屏叠加层 + 逐格把
// `ui/AUTOMAP/MaxiMap.dc6` 的 cel（16×32，ACT1 调色板）blit 到**等距位置**。
//
// ★★ 片 `automap-original-verdict`（2026-09-22）改了什么（= 消除 E23/E38 的素材缺失项）：
//   旧版 = 「右上角定尺框 + 逐格程序化点阵 + 本项目自选配色 `#1C1C1C/#484848/#C4C4C4`」
//   （E23 登记：素材不在本机 ⇒ 只能近似）。**本轮原版包到位**，按实测口径重写：
//     ① **满屏叠加层**（原版自动地图没有窗口框 —— `D2/UI/Banner/` 那一族只有标题/开关条
//        `automap* / AutoMapCenter / AutoMapParty / AutoMapOptions`）⇒ 本面板不再有定尺框，
//        贴图按原版比例铺开、**以玩家格为中心**平移（`UpdateView`）。
//     ② **图形 = 原版 cel**：逐格从 `Core/AutoMapCel.generated.cs`（生成物）取 Cel 号，
//        把 `MaxiMap.dc6` 该帧的像素（稀疏索引）用 **ACT1 调色板**上色后 blit。
//        ⛔ 本文件里**没有任何自选颜色常量**（旧版的 `ColBackdrop/ColFloor/ColBlock`、
//        `SuperSample/FloorDotPx/BlockDotPx/ExitDotPx` 全部删除）。
//     ③ **逐格 Cel 号** = 原版 `AutoMap.txt` 的查询键
//        `(LevelName = <act> <LevelType>、Style = DS1 格 prop3 & 0x0F、Sequence = DS1 格 prop2)`
//        → `CelN`（= `MaxiMap.dc6` 帧序号）。逐格值由 `MapModule.BuildMinimap` 走
//        `MinimapArgs.cels / celsOver` 送进来（见 `Contracts.cs` 的 `# contract:` 注释）。
//
//   §几何（实测，可复跑；见 `.ai-tmp/test/automap-plan.md` §1.4）
//     · 世界地砖 = 160×80（`tools/d2codec/dt1.py` 的 `FLOOR_TILE_PX_W/H`）
//       ⇒ automap 比例 **1/10**：一格 = **16×8** 等距菱形。
//     · `MaxiMap` 每帧 16×32，地面花纹落在帧**底部 8 行**（帧 0..3 的 bbox y=24..31）
//       ⇒ **cel 左上角贴到该格的 (posX, posY)**，其中
//         `posX = ((x − y) + (H−1)) × 8`、`posY = (x + y) × 4`（世界同投影，缩到 1/10）。
//     · 画布缩放 = `UiLayoutGame.K`（= 1080/600；与 HUD/其它原版素材同口径）。
//
//   §与原版**仍不同**的（⛔ 不许当"一致"；登记 E23 残余 + 本片回报）
//     ① **多行命中时挑哪一行**（`AutoMap.txt` 的 `TileName` 有 13 个家族名，同一
//        (LevelName, Style, Seq) 常有多行）与 **4 个变体（`Cel1..Cel4`）挑哪一个**：
//        原版规则在本机**没有载体**（没有引擎源码；DT1 头里也没有 type/style 字段）
//        ⇒ 生成器按**可复跑规则**定死（文件里最先出现的那一行、取该行第一个 `CelN ≥ 0`），
//        逐条登记。⇒ 原版逐格"变体/朝向"的细微差别，本项目是**同族 cel 的确定性子集**。
//     ② **揭示粒度**（★ 片 g2-resume 改写，E23 ④ 由"本格 + 8 邻域"改为**记忆式已探索**）：
//        · 旧口径：每换一格只把「本格 + 8 邻域」标记为已探索 ⇒ 走一大圈后地图仍只有一串
//          3×3 小块（实测 `explored=9 / opaquePixels=135`），用户看到的仍是"地图没画出来"。
//        · 新口径（`Reveal` 纯函数，离线可断言）：**访问过即记忆**（集合只增不减），
//          每次揭示 = 从玩家格做**只走可通行格**的 BFS（曼哈顿距离 ≤ `RevealRadius`）+
//          把已揭示地面格**相邻的阻挡格**一并揭示（= 原版的"地板先、墙后"轮廓）。
//        · `RevealRadius` = **6 格**，由**相机视野**推导（不是拍脑袋）：
//          可见半高 = `CameraRig.DefaultOrthographicSize` = 3.75 世界单位、1 格 = 1 世界单位高；
//          等距投影下格偏移 (dx,dy) 的世界位移 = ((dx−dy)·HalfW, −(dx+dy)·HalfH)（`Core/Iso.cs`，
//          HalfW=1.0 / HalfH=0.5）⇒ 同屏条件 = |dx−dy| ≤ 3.75·aspect = 6.67 且 |dx+dy| ≤ 3.75/0.5 = 7.5；
//          而 |dx|+|dy| = max(|dx+dy|, |dx−dy|) ⇒ 取紧的那个下界 floor(6.67) = **6** ⇒
//          「凡是能被玩家看到的格，走过就都记下来」。
//        · ⛔ 仍与原版**不同**：原版按**房间**揭示（引擎 `DRLG` 的房间层，本机无载体），
//          本项目是"视野半径 + 不穿墙"的近似 ⇒ 保留登记 E23 ④（口径已改，条目内容本轮已更新）。
//     ③ **标记图标语义**：`MINIMAP/mapicons.DC6` 8 帧是白色模板且无权威语义映射
//        ⇒ 仍统一用帧 0 + 本项目色调（登记 BLOCKED），只把**位置**口径照原版 blit。
//     ④ 面板底色（半透明黑）沿用旧表现（原版不透明度无载体；登记）。
//
// ★ 数据来源（**零模块耦合**）：`Events.MapGenerated`（`Def.MinimapArgs`，含逐格 Cel）
//   + `Events.PlayerGridChanged`（`Vector2Int`）+ **`Events.MapExplored`**（`IReadOnlyCollection<Vector2Int>`，
//   Map 的"首次探索"增量 ⇒ 已探索的**权威口径**，接管后本面板不再自行累积）。
//
// ★★ U46（用户：「tab 渲染地图不对 / 地图没画出来」）——「画哪些格」与「哪些格已探索」拆开：
//   本面板的**画法只有一处** `RenderExplored(..., bool[] explored, ...)`（纯函数，实例 `Redraw`
//   与离线 `CountDrawn` 都走它），而 `explored` 是**入参**：
//     · **已接线**（S2 的 `Events.MapExplored` 到位后）：Map 每次"某格**首次**被记为已探索"
//       就发**增量**格 ⇒ 本面板 `OnMapExplored → ApplyExplored`（**并入**）⇒ 从那一刻起
//       **口径由外部接管**、本面板不再自行揭示（`ExploredInjected == true`）；
//     · 接管**之前**（进区到玩家开腿之间，Map 还一格都没报）⇒ 走 `Reveal` + `RevealRadius`
//       的**兜底**口径 —— 否则"刚进区还没走路"时地图全空，正是用户报的"地图没画出来"。
//   ⚠️ **仍未 1:1 的部分**（登记 E23 ④）：原版按**房间**揭示，本项目数据里没有房间层 ⇒
//   兜底那段只能是"视野半径 6 格 + 不穿墙 BFS + 墙轮廓"的近似，且同一张图上
//   **兜底段与接管段是并集**（接管只加不减）。画法侧与口径侧已彻底解耦（⛔ 不留第二份画法）。
// ★ Tab 键由 `UI/HudPanel.cs` 轮询并发 `Events.PanelToggleRequest`（本面板自身不读输入）。
// ⛔ 零 `using Diablo2.Module`（分层自检 ③）。
// ─────────────────────────────────────────────────────────────────────────────

using System.Collections.Generic;
using CloverEngine;
using Diablo2.Core;
using Diablo2.Def;
using UnityEngine;
using UnityEngine.UI;

namespace Diablo2.UI
{
    /// <summary>自动地图面板。层：<see cref="UILayer.Normal"/>。</summary>
    public class MiniMapPanel : UIPanel
    {
        /// <summary>
        /// 叠加层底色不透明度（沿用旧表现；原版该值**无载体** ⇒ 登记 E23 残余）。
        /// 未探索的格是**透明**的 ⇒ 游戏画面透出来（原版 automap 只画已探索部分）。
        /// </summary>
        public const float BackdropAlpha = 0f;      // ★ U4：原版 automap 不压暗（见上）；>0 ⇒ Build() 建满屏黑块

        /// <summary>出入口标记的**图标色调**（本项目选定，见文件头 ③；原版 8 帧是白模板，必须上色）。</summary>
        public static readonly Color MarkerExitTint = new Color(0.95f, 0.85f, 0.35f, 1f);

        /// <summary>可交互（NPC / 洞穴口）标记的**图标色调**（本项目选定，见文件头 ③）。</summary>
        public static readonly Color MarkerInteractTint = new Color(0.42f, 0.62f, 0.95f, 1f);

        private bool _built;
        private bool _subscribed;
        private MinimapArgs _map;

        /// <summary>已探索标记（行优先，长度 = width*height）。</summary>
        private bool[] _explored;

        /// <summary>
        /// 「已探索口径来自**外部**（`Events.MapExplored`）」—— true ⇒ 本面板**不再自行揭示**，
        /// 只把外部并入的格画出来（见 <see cref="ApplyExplored"/> / <see cref="RevealPlayer"/>）。
        /// <para>进区 / 换图（<see cref="ApplyMap"/>）复位为 false ⇒ 先走**兜底**口径
        /// （玩家还没开腿时地图不会是空白），外部第一次报格时**一次性接管**，本区域内不再切回。</para>
        /// </summary>
        private bool _fromSource;

        private RectTransform _overlay;
        private RawImage _raw;
        private Texture2D _tex;
        private Color32[] _pixels;
        private Color32[] _palette;
        private RectTransform _playerDot;
        private readonly List<Image> _markers = new List<Image>();
        private int _texW;
        private int _texH;

        /// <summary>「揭示」日志最多报几条（每换一格都揭示一次 ⇒ 不能每次都打）。</summary>
        private const int RevealLogLimit = 3;
        private int _revealLogCount;

        /// <summary>cel 像素（base64 稀疏编码）解码缓存 —— 每帧只有几十个像素，但别每帧解一遍。</summary>
        private static readonly Dictionary<int, byte[]> CelCache = new Dictionary<int, byte[]>();

        /// <inheritdoc/>
        public override UILayer Layer => UILayer.Normal;

        /// <inheritdoc/>
        public override void OnOpen(object param)
        {
            UiArt.PrepareRoot(Root);
            Build();
            Subscribe();

            var map = UiLog.Require<MinimapArgs>(param, nameof(MiniMapPanel));
            if (map != null) ApplyMap(map);
            else
            {
                UiLog.WarnOnce("minimap.no.map",
                    "自动地图未收到 `MinimapArgs` ⇒ 空白地图；请 Map 模块在生成后 emit `Events.MapGenerated`");
            }

            UiLog.Info($"自动地图已打开（地图={(map != null ? $"{map.width}×{map.height} seed={map.seed}" : "无")}"
                       + $"，画法 = 原版 `AutoMap.txt` 逐格 Cel → `MaxiMap.dc6` 帧（ACT1 调色板）"
                       + $" blit 到 1/{AutoMapCel.ScaleDen} 等距位置；标记用原版 D2/UI/MiniMap/mapicon_"
                       + $"{ResPaths.MiniMapMarkerFrame}）");
        }

        /// <inheritdoc/>
        public override void OnClose()
        {
            Unsubscribe();
            if (_tex != null)
            {
                UnityEngine.Object.Destroy(_tex);
                _tex = null;
            }
            UiLog.Info("自动地图已关闭");
        }

        // ═════════════════════════════════════════════════════════════════════
        // 构件
        // ═════════════════════════════════════════════════════════════════════
        private void Build()
        {
            if (_built) return;
            _built = true;

            // ① 叠加层底：**铺满画布**（原版 = 满屏叠加层，⛔ 不再是右上角定尺框）
            //   ⚠️ `raycastTarget` **必须 = false（不吃射线）**，理由是"原版语义 + 一条实测缺陷"：
            //     · 原版 automap 是**纯显示叠加层**（`D2/UI/Banner/automap*` 那一族只有标题/开关条，
            //       本面板里也没有任何 `IPointer*Handler`）⇒ 它不该吃掉任何鼠标事件；
            //     · 这块 Image **铺满整个画布**（下面 `anchorMin/anchorMax` = 0..1）⇒ 若吃射线，
            //       uGUI 指针命中**恒为真**，`Module/Input/InputReader.IsPointerOverUi()` 随之恒真，
            //       于是 `UiEatsIntent(pressed:true, pointerOverUi:true)` 把**每一次**点击都判成
            //       "点 UI"⇒ `TryGetGroundClick/HoldTarget` 全部返回 false ⇒ **开着地图时人物一步都走不了**
            //       （2026-09-22 用户实测：「tab 看地图时候动不了」）。
            //     · 面板外点地面照走是原版行为（Tab 开着也能点地面移动）⇒ 这一层必须是"看得见、点不到"。
            //   ★ 2026-09-23（U4，用户报「tab 背景不用压暗」）：`BackdropAlpha == 0` 时**不建**这一层
            //     —— 原版 automap 是纯图形叠加层（见常量注释）。连着老表现一起删掉，
            //     否则「alpha=0 的满屏黑块」仍在节点树里（判据要求「不存在压暗层」）。
            if (BackdropAlpha > 0f)
            {
                var bg = UiArt.Panel(transform, "Backdrop",
                    new Vector2(UiArt.RefWidth, UiArt.RefHeight), Vector2.zero,
                    new Color(0f, 0f, 0f, BackdropAlpha), false);
                var bgRt = bg.rectTransform;
                bgRt.anchorMin = Vector2.zero;
                bgRt.anchorMax = Vector2.one;
                bgRt.offsetMin = Vector2.zero;
                bgRt.offsetMax = Vector2.zero;
            }

            // ② automap 贴图（按原版比例铺开；位置由 UpdateView 跟着玩家格平移）
            _overlay = UIFactory.CreateCentered("Overlay", transform, Vector2.zero, Vector2.zero);
            _raw = _overlay.gameObject.AddComponent<RawImage>();
            _raw.raycastTarget = false;
            _raw.color = Color.white;

            // ③ 玩家点（屏幕中心；原版 automap 以玩家为中心 —— 见 UpdateView）
            _playerDot = UiArt.Panel(transform, "PlayerDot", new Vector2(6f, 6f), Vector2.zero,
                new Color(1f, 0.95f, 0.55f, 1f), false).rectTransform;

            // ACT1 调色板（原版 `ACT1/Pal.PL2` 的 256×RGB；生成物给）
            _palette = CreatePalette();
        }

        // ═════════════════════════════════════════════════════════════════════
        // 地图数据
        // ═════════════════════════════════════════════════════════════════════
        private void ApplyMap(MinimapArgs map)
        {
            if (!_built || map == null) return;

            if (map.width <= 0 || map.height <= 0)
            {
                UiLog.Error($"`MinimapArgs` 尺寸非法：{map.width}×{map.height} ⇒ 不绘制（Map 模块请检查生成结果）");
                return;
            }

            if (map.cels == null || map.cels.Count < map.width * map.height)
            {
                UiLog.Warn($"`MinimapArgs.cels` 长度 {map.cels?.Count ?? 0} 小于 {map.width}×{map.height}"
                           + " ⇒ 缺的格按「原版不画」处理（Map 模块请检查 BuildMinimap）");
            }

            _map = map;
            _explored = new bool[map.width * map.height];
            _fromSource = false;   // 新地图 ⇒ 上一张图的已探索集合作废（尺寸都换了）⇒ 先兜底，等外部接管

            // automap 贴图尺寸（纹理px = **原版px**）：一格 16 宽 / 4 高步进，cel 高 32
            var stepX = AutoMapCel.W / 2;
            var stepY = AutoMapCel.W / 4;      // 世界地砖 80 ÷ 10 ÷ 2 = 4（= 16/4）
            _texW = (map.width + map.height - 2) * stepX + AutoMapCel.W;
            _texH = (map.width + map.height - 2) * stepY + AutoMapCel.H;

            if (_tex != null) UnityEngine.Object.Destroy(_tex);
            _tex = new Texture2D(_texW, _texH, TextureFormat.RGBA32, false);
            _tex.filterMode = FilterMode.Point;
            _tex.wrapMode = TextureWrapMode.Clamp;
            _pixels = new Color32[_texW * _texH];
            for (var i = 0; i < _pixels.Length; i++) _pixels[i] = new Color32(0, 0, 0, 0);
            _raw.texture = _tex;
            _raw.rectTransform.sizeDelta = new Vector2(_texW * UiLayoutGame.K, _texH * UiLayoutGame.K);

            RevealPlayer(map.playerX, map.playerY);
            BuildMarkers();
            Redraw();

            UiLog.Info($"自动地图画法：原版 `MaxiMap.dc6` 逐格 cel（{AutoMapCel.W}×{AutoMapCel.H}，"
                       + $"帧数 {AutoMapCel.FrameCount}）+ ACT1 调色板（`ACT1/Pal.PL2` 256 色）"
                       + $"，比例 1/{AutoMapCel.ScaleDen}（格 = 16×8 菱形），贴图 {_texW}×{_texH} 原版px"
                       + $" × {UiLayoutGame.K} 画布px；已探索 = **记忆式**（访问过即记忆，半径 {RevealRadius} 格的"
                       + "可通行 BFS + 相邻墙轮廓；与「原版按房间揭示」的差异仍登记 E23 ④）");
        }

        /// <summary>
        /// 揭示半径（**曼哈顿距离**，格）—— 由相机视野推导，见文件头 ②：取
        /// `floor(DefaultOrthographicSize × 16/9 / HalfW)` = `floor(6.67)` = 6 ⇒ 同屏可见的格全在内。
        /// </summary>
        public const int RevealRadius = 6;

        /// <summary>4 邻域步长（BFS 用；曼哈顿距离）。</summary>
        private static readonly int[] Dx4 = { 1, -1, 0, 0 };
        private static readonly int[] Dy4 = { 0, 0, 1, -1 };

        /// <summary>8 邻域步长（"墙轮廓"用）。</summary>
        private static readonly int[] Dx8 = { 1, -1, 0, 0, 1, 1, -1, -1 };
        private static readonly int[] Dy8 = { 0, 0, 1, -1, 1, -1, 1, -1 };

        /// <summary>该地形码能否走过去（BFS 只走这些格 ⇒ 不穿墙）。</summary>
        public static bool Passable(byte tile)
            => tile == MinimapArgs.TileWalkable || tile == MinimapArgs.TileExit;

        /// <summary>
        /// **揭示（纯函数，离线可断言）**：把"玩家这次能看到/走过"的格并进 <paramref name="explored"/>（**记忆式**：
        /// 只增不减）。① 从玩家格做只走可通行格的 BFS（曼哈顿 ≤ <see cref="RevealRadius"/>）；
        /// ② 把已揭示地面格的**相邻阻挡格**一并揭示（原版"地板先、墙后"的轮廓；`TileVoid` 不揭示）。
        /// </summary>
        /// <returns>本次**新**标记的格数（用于日志/断言）。</returns>
        public static int Reveal(MinimapArgs map, bool[] explored, int gx, int gy)
        {
            if (map == null || explored == null) return 0;
            if (map.width <= 0 || map.height <= 0 || explored.Length < map.width * map.height) return 0;
            if (gx < 0 || gy < 0 || gx >= map.width || gy >= map.height) return 0;

            var n = map.width * map.height;
            var dist = new int[n];                       // 曼哈顿距离（-1 = 没走到）
            for (var i = 0; i < n; i++) dist[i] = -1;
            var queue = new Queue<int>();

            var start = gy * map.width + gx;
            dist[start] = 0;
            queue.Enqueue(start);

            var fresh = 0;
            while (queue.Count > 0)
            {
                var i = queue.Dequeue();
                if (!explored[i])
                {
                    explored[i] = true;
                    fresh++;
                }

                var d = dist[i];
                if (d >= RevealRadius) continue;

                var x = i % map.width;
                var y = i / map.width;
                for (var k = 0; k < 4; k++)
                {
                    var nx = x + Dx4[k];
                    var ny = y + Dy4[k];
                    if (nx < 0 || ny < 0 || nx >= map.width || ny >= map.height) continue;
                    var j = ny * map.width + nx;
                    if (dist[j] >= 0) continue;
                    if (!Passable(map.TileAt(nx, ny))) continue;    // 墙不进 BFS（下一步只揭示它本身）
                    dist[j] = d + 1;
                    queue.Enqueue(j);
                }
            }

            // ② 墙轮廓：已揭示地面格的相邻阻挡格（非 Void）也揭示 —— 这是"轮廓感"的来源
            for (var i = 0; i < n; i++)
            {
                if (dist[i] < 0) continue;
                var x = i % map.width;
                var y = i / map.width;
                for (var k = 0; k < 8; k++)
                {
                    var nx = x + Dx8[k];
                    var ny = y + Dy8[k];
                    if (nx < 0 || ny < 0 || nx >= map.width || ny >= map.height) continue;
                    var j = ny * map.width + nx;
                    if (explored[j]) continue;
                    var t = map.TileAt(nx, ny);
                    if (t == MinimapArgs.TileVoid || Passable(t)) continue;
                    explored[j] = true;
                    fresh++;
                }
            }

            return fresh;
        }

        /// <summary>
        /// 渲染侧的已探索**口径是否已被外部接管**（= Map 模块在发 `Events.MapExplored` ⇒ 本面板只画它）。
        /// </summary>
        public bool ExploredInjected => _fromSource;

        /// <summary>
        /// **渲染侧注入入口（U46）**：把外部报来的已探索格**并入**本面板的位图（只增不减），
        /// 并把口径来源标记为外部 ⇒ 此后本面板**不再自行揭示**。
        ///
        /// <para>⛔ 本方法**只操作渲染状态**（`_explored` 位图 + 重画），⛔ 不碰任何模块、不读输入、
        /// 不决定"什么算已探索" —— 那是数据源的事（原版按**房间**揭示，本项目无该载体，
        /// 见文件头 ② 与登记 E23 ④）。</para>
        ///
        /// <para><b>载荷口径</b>：收 `Events.MapExplored`（`MapModule.OnFirstExplored` 发，
        /// **增量**：当前实现每次恰 1 格）⇒ 因此本方法是**并入**语义。`cells == null` 只忽略 + 留痕，
        /// ⛔ 不用它"清空"：清空只发生在 <see cref="ApplyMap"/>（换图 / 换区）。</para>
        ///
        /// <para><b>为什么要有这个入口</b>：用户报的"地图没画出来 / 画得不对"里，"画哪些格"
        /// 与"哪些格已探索"是两件事。把后者做成入参 ⇒ ① 判据可以**自己造集合**判渲染
        /// （离线 uicheck 的 <see cref="CountDrawn"/> / <see cref="RenderExplored"/>）；
        /// ② 换揭示口径不必改渲染代码（⛔ 不留第二份画法）。</para>
        /// </summary>
        /// <param name="cells">外部报来的已探索格（格坐标，增量）；null ⇒ 忽略并留痕。</param>
        /// <returns>本次真正**新**并入的格数（越界 / 重复不算）。</returns>
        public int ApplyExplored(IReadOnlyCollection<Vector2Int> cells)
        {
            if (_map == null || _explored == null)
            {
                UiLog.WarnOnce("minimap.inject.before.map",
                    "注入已探索集合时还没有地图数据 ⇒ 忽略（请确认 Map 模块先发 `Events.MapGenerated`）");
                return 0;
            }
            if (cells == null)
            {
                UiLog.WarnOnce("minimap.inject.null",
                    "收到的已探索集合为 null ⇒ 忽略（本面板不据此清空；清空只发生在换图时）");
                return 0;
            }

            var handover = !_fromSource;           // ★ 首次接管：打一条口径说明（此后不再自行揭示）
            _fromSource = true;

            var n = 0;
            foreach (var c in cells)
            {
                if (c.x < 0 || c.y < 0 || c.x >= _map.width || c.y >= _map.height)
                {
                    UiLog.WarnOnce("minimap.inject.oob",
                        $"已探索集合里有越界格 ({c.x},{c.y})（地图 {_map.width}×{_map.height}）⇒ 跳过"
                        + "（请检查 Map 模块的格坐标口径；只报一次）");
                    continue;
                }
                var i = c.y * _map.width + c.x;
                if (_explored[i]) continue;        // 并入语义：走过同一格不重复置位（只增不减）
                _explored[i] = true;
                n++;
            }

            if (n > 0) Redraw();

            if (handover)
            {
                UiLog.Info("自动地图：已探索口径**由外部接管**（`Events.MapExplored` = Map 的\"走过即记忆\"，"
                           + $"本次并入 {n} 格）⇒ 本面板此后不再自行揭示；接管前那段是**兜底**口径"
                           + $"（半径 {RevealRadius} 格 BFS + 墙轮廓）—— 同一张图上两段口径的并集已登记 E23 ④");
            }
            else if (n > 0 && _revealLogCount < RevealLogLimit)
            {
                _revealLogCount++;
                UiLog.Info($"自动地图并入已探索 {n} 格（外部 / Map 报来）⇒ 累计 {CountExplored(_explored)} 格，"
                           + $"画出 {DrawnCells} 格 / {OpaquePixels} 图元");
            }
            return n;
        }

        /// <summary>把玩家当前格（按 <see cref="Reveal"/> 口径）标为已探索，然后提交贴图。</summary>
        private void RevealPlayer(int gx, int gy)
        {
            if (_map == null || _explored == null) return;

            // ★ U46：口径已被外部接管（Map 发 `Events.MapExplored`）⇒ 面板**不自行揭示**
            //   （口径只有一处权威来源；"接管"那条日志在 ApplyExplored 里打，这里不重复播报）
            if (_fromSource) return;

            var fresh = Reveal(_map, _explored, gx, gy);
            if (fresh > 0 && _revealLogCount < RevealLogLimit)
            {
                _revealLogCount++;
                UiLog.Info($"自动地图揭示：玩家格 ({gx},{gy}) ⇒ 本次新揭示 {fresh} 格，" +
                           $"累计 {CountExplored(_explored)} 格（记忆式已探索，半径 {RevealRadius} 格 + 墙轮廓；" +
                           "口径见 MiniMapPanel.Reveal 与文件头 ②）");
            }
        }

        /// <summary>已探索格数（断言/日志用；纯函数）。</summary>
        public static int CountExplored(bool[] explored)
        {
            if (explored == null) return 0;
            var n = 0;
            for (var i = 0; i < explored.Length; i++) if (explored[i]) n++;
            return n;
        }

        /// <summary>本帧真正画出 ≥1 图元的格数（日志 / 断言用）。</summary>
        public int DrawnCells { get; private set; }

        /// <summary>本帧「有 cel 的已探索格」数（= <see cref="DrawnCells"/> 当且仅当不静默丢格）。</summary>
        public int CellsWithCel { get; private set; }

        /// <summary>本帧写出的不透明像素数（"图元稀疏 / 没画出来"的直接数字）。</summary>
        public int OpaquePixels { get; private set; }

        /// <summary>
        /// **纯函数渲染核心（⛔ 全项目唯一一份 automap 画法）**：把 <paramref name="explored"/> 里
        /// 每个已探索格的 cel 按等距几何 blit 进 <paramref name="pixels"/>（进入时先清空整块）。
        ///
        /// <para>★ U46（用户：「tab 渲染地图不对 / 地图没画出来」）：**已探索集合是入参**（= 注入集合），
        /// 本函数**不决定揭示口径** —— 口径属数据源（Map 模块 / 注入方）。实例 <see cref="Redraw"/>
        /// 与离线 <see cref="CountDrawn"/> 都走这一条：判据与产品**同源**，⛔ 不留第二份画法
        /// （这是"只允许一处权威实现"在渲染侧的执行面）。</para>
        /// </summary>
        /// <param name="pixels">目标像素缓冲（长度 ≥ texW×texH）。</param>
        /// <param name="explored">**注入**的已探索集合（行优先，长度 ≥ width×height；本函数只读它）。</param>
        /// <param name="cellsWithCel">出参：已探索格中至少有一层 cel ≥ 0 的格数（原版这一格本来不画的不算）。</param>
        /// <param name="opaquePixels">出参：写出的不透明像素总数。</param>
        /// <returns>真正写出 ≥1 图元的格数。</returns>
        /// <param name="map">地图数据（只读）。</param>
        public static int RenderExplored(Color32[] pixels, int texW, int texH, MinimapArgs map,
            bool[] explored, Color32[] palette, out int cellsWithCel, out int opaquePixels)
        {
            cellsWithCel = 0;
            opaquePixels = 0;
            if (pixels == null || map == null || explored == null || palette == null) return 0;
            if (map.width <= 0 || map.height <= 0 || texW <= 0 || texH <= 0) return 0;
            if (texW * texH > pixels.Length) return 0;

            for (var i = 0; i < pixels.Length; i++) pixels[i] = new Color32(0, 0, 0, 0);

            var drawn = 0;
            for (var y = 0; y < map.height; y++)
            {
                for (var x = 0; x < map.width; x++)
                {
                    var i = y * map.width + x;
                    if (i >= explored.Length || !explored[i]) continue;

                    // 原版逐层 blit：先地面层的 cel，再物件（墙）层的 cel（后者盖在上面）
                    var floor = map.CelAt(x, y, false);
                    var over = map.CelAt(x, y, true);
                    if (floor < 0 && over < 0) continue;      // 原版这一格本来就不画
                    cellsWithCel++;

                    var before = opaquePixels;
                    opaquePixels += BlitInto(pixels, texW, texH, map.height, floor, x, y, palette);
                    opaquePixels += BlitInto(pixels, texW, texH, map.height, over, x, y, palette);
                    if (opaquePixels > before) drawn++;
                }
            }
            return drawn;
        }

        private void Redraw()
        {
            if (_map == null || _tex == null || _pixels == null) return;

            int withCel, opaque;
            DrawnCells = RenderExplored(_pixels, _texW, _texH, _map, _explored, _palette,
                out withCel, out opaque);
            CellsWithCel = withCel;
            OpaquePixels = opaque;

            _tex.SetPixels32(_pixels);
            _tex.Apply(false);

            UpdateView();
        }

        /// <summary>
        /// **纯函数版 blit**（实例 `Blit` 的实现体；离线宿主可直接调它做"图元数"断言，
        /// ⛔ 不复制第二份画法）：把一个 cel 的稀疏像素按等距几何写进像素缓冲。
        /// </summary>
        /// <returns>本格实际写入的**图元数**（不透明像素个数）。</returns>
        public static int BlitInto(Color32[] pixels, int texW, int texH, int mapHeight,
            int cel, int gx, int gy, Color32[] palette)
        {
            if (pixels == null || palette == null) return 0;
            if (cel < 0 || texW <= 0 || texH <= 0) return 0;                 // -1 = 原版这一格不画
            if (mapHeight <= 0 || texW * texH > pixels.Length + texW) return 0;

            byte[] px;
            if (!CelCache.TryGetValue(cel, out px))
            {
                if (!AutoMapCel.CelPixels.TryGetValue(cel, out px) || px == null || px.Length == 0)
                {
                    // 非预期：表里有 Cel 号但没导出该帧的像素 ⇒ 留痕一次，不静默（生成器请重跑）
                    UiLog.WarnOnce("minimap.cel.pixels." + cel,
                        $"自动地图：Cel {cel} 没有像素数据（`AutoMapCel.CelPixels`）⇒ 该格不画；"
                        + "请重跑 `python tools/probes/gen_automap.py`");
                    return 0;
                }
                CelCache[cel] = px;
            }

            var stepX = AutoMapCel.W / 2;
            var stepY = AutoMapCel.W / 4;      // 世界地砖 80 ÷ 10 ÷ 2 = 4（= 16/4）
            var ox = ((gx - gy) + (mapHeight - 1)) * stepX;
            var oy = (gx + gy) * stepY;
            var written = 0;
            for (var i = 0; i + 2 < px.Length; i += 3)
            {
                var tx = ox + px[i];
                var ty = oy + px[i + 1];
                if (tx < 0 || ty < 0 || tx >= texW || ty >= texH) continue;
                pixels[(texH - 1 - ty) * texW + tx] = palette[px[i + 2]];
                written++;
            }
            return written;
        }

        /// <summary>ACT1 调色板（原版 `ACT1/Pal.PL2` 的 256×RGB；索引 0 = 透明）。实例与离线宿主共用。</summary>
        public static Color32[] CreatePalette()
        {
            var p = new Color32[256];
            for (var i = 0; i < 256; i++)
            {
                p[i] = new Color32(AutoMapCel.PaletteRgb[i * 3],
                    AutoMapCel.PaletteRgb[i * 3 + 1], AutoMapCel.PaletteRgb[i * 3 + 2], 255);
            }
            p[0] = new Color32(0, 0, 0, 0);
            return p;
        }

        /// <summary>
        /// **纯函数：给定已探索集合 ⇒ 数出图元**（离线断言的判据入口；⛔ 与实例 `Redraw` 走**同一条**
        /// <see cref="RenderExplored"/> —— 判据与产品同源，不是第二份实现）。
        /// <para>口径（判"过程"不判"结果"）：`cellsWithCel` = 已探索格中**至少有一层 cel ≥ 0** 的格数；
        /// `cellsDrawn` = 其中**真的写出 ≥1 图元**的格数 ⇒ **两者必须相等**（不相等 = 静默丢格）。</para>
        /// <para>★ U46：<paramref name="explored"/> 是**注入集合**（判据自己造、产品只画）——
        /// 这样"揭示口径"改了不会把渲染判据一起改掉（反之亦然）。</para>
        /// </summary>
        public static void CountDrawn(MinimapArgs map, bool[] explored, int texW, int texH, Color32[] palette,
            out int cellsWithCel, out int cellsDrawn, out int opaquePixels)
        {
            cellsWithCel = 0;
            cellsDrawn = 0;
            opaquePixels = 0;
            if (map == null || explored == null || palette == null) return;
            if (map.width <= 0 || map.height <= 0 || texW <= 0 || texH <= 0) return;

            var pixels = new Color32[texW * texH];
            cellsDrawn = RenderExplored(pixels, texW, texH, map, explored, palette,
                out cellsWithCel, out opaquePixels);
        }

        /// <summary>
        /// 把叠加层平移，使**玩家格**落在屏幕中心（原版 automap 以玩家为中心，见文件头 ①）。
        /// </summary>
        private void UpdateView()
        {
            if (_map == null || _overlay == null) return;

            var stepX = AutoMapCel.W / 2;
            var stepY = AutoMapCel.W / 4;      // 世界地砖 80 ÷ 10 ÷ 2 = 4（= 16/4）
            var k = UiLayoutGame.K;

            // 玩家格的"格心"纹理坐标（该格 cel 的地面菱形中心 = 左上角 + (8, 28)）
            var px = ((_map.playerX - _map.playerY) + (_map.height - 1)) * stepX + AutoMapCel.W / 2;
            var py = (_map.playerX + _map.playerY) * stepY + AutoMapCel.H - AutoMapCel.W / 4;

            _overlay.sizeDelta = new Vector2(_texW * k, _texH * k);
            _overlay.anchoredPosition = new Vector2((_texW * 0.5f - px) * k, (py - _texH * 0.5f) * k);
        }

        /// <summary>
        /// 造出入口 / 可交互标记：底图 = 原版 `mapicons.DC6` 的帧（**不是纯色方块**），
        /// 色调由本项目给（原版图标是白模板，必须上色 —— 见文件头 ③）；位置按 automap 几何落。
        /// </summary>
        private void BuildMarkers()
        {
            for (var i = 0; i < _markers.Count; i++)
                if (_markers[i] != null) UnityEngine.Object.Destroy(_markers[i].gameObject);
            _markers.Clear();

            if (_map?.markerX == null) return;

            var iconPath = ResPaths.MiniMapIcon(ResPaths.MiniMapMarkerFrame);
            var side = new Vector2(UiLayoutGame.MiniMapIconPx, UiLayoutGame.MiniMapIconPx);
            var stepX = AutoMapCel.W / 2;
            var stepY = AutoMapCel.W / 4;      // 世界地砖 80 ÷ 10 ÷ 2 = 4（= 16/4）
            var k = UiLayoutGame.K;

            for (var i = 0; i < _map.markerX.Count; i++)
            {
                if (i >= _map.markerY.Count || i >= _map.markerKind.Count)
                {
                    UiLog.Warn($"`MinimapArgs` 标记数组长度不一致（markerX={_map.markerX.Count}）⇒ 第 {i} 个起忽略");
                    break;
                }

                var exit = _map.markerKind[i] == MinimapArgs.TileExit;
                var dot = UiArt.Art(_overlay, "Marker" + i, iconPath, side, Vector2.zero);
                // 原版图标是"白色模板"（8 帧只用索引 32 = #F4F4F4）⇒ 必须上色，否则所有标记同色
                UiArt.SetArtTint(dot, exit ? MarkerExitTint : MarkerInteractTint);

                var tx = ((_map.markerX[i] - _map.markerY[i]) + (_map.height - 1)) * stepX
                         + AutoMapCel.W / 2;
                var ty = (_map.markerX[i] + _map.markerY[i]) * stepY
                         + AutoMapCel.H - AutoMapCel.W / 4;
                dot.rectTransform.anchoredPosition = new Vector2(
                    (tx - _texW * 0.5f) * k, (_texH * 0.5f - ty) * k);
                _markers.Add(dot);
            }

            UiLog.Info($"自动地图标记 {_markers.Count} 个，底图 = 原版 {iconPath}"
                       + $"（{UiLayoutGame.MiniMapIconPx:0.#} 画布px = 原版 16×{UiLayoutGame.K}）");
        }

        // ═════════════════════════════════════════════════════════════════════
        // 事件
        // ═════════════════════════════════════════════════════════════════════
        private void Subscribe()
        {
            if (_subscribed || Game.Event == null) return;
            _subscribed = true;
            Game.Event.On<MinimapArgs>(Events.MapGenerated, OnMapGenerated);
            Game.Event.On<Vector2Int>(Events.PlayerGridChanged, OnPlayerGrid);
            // ★ U46：已探索的**权威来源** = Map 模块（`MapModule.OnFirstExplored` 发增量格）——
            //   收到即接管（此后本面板不再自行揭示，见 ApplyExplored / RevealPlayer）。
            Game.Event.On<IReadOnlyCollection<Vector2Int>>(Events.MapExplored, OnMapExplored);
        }

        private void Unsubscribe()
        {
            if (!_subscribed || Game.Event == null) return;
            _subscribed = false;
            Game.Event.Off<MinimapArgs>(Events.MapGenerated, OnMapGenerated);
            Game.Event.Off<Vector2Int>(Events.PlayerGridChanged, OnPlayerGrid);
            Game.Event.Off<IReadOnlyCollection<Vector2Int>>(Events.MapExplored, OnMapExplored);
        }

        private void OnMapGenerated(MinimapArgs map)
        {
            if (map == null)
            {
                UiLog.Warn("收到地图事件但参数为 null ⇒ 忽略");
                return;
            }
            ApplyMap(map);
        }

        private void OnPlayerGrid(Vector2Int grid)
        {
            if (_map == null)
            {
                UiLog.WarnOnce("minimap.grid.before.map",
                    $"收到 `{Events.PlayerGridChanged}` 时还没有地图数据 ⇒ 只更新玩家点（请确认 Map 模块先发 MapGenerated）");
                return;
            }

            _map.playerX = grid.x;
            _map.playerY = grid.y;
            RevealPlayer(grid.x, grid.y);
            Redraw();
        }

        /// <summary>
        /// ★ U46：Map 模块报来**新**被记为已探索的格（增量）⇒ 并入本面板位图（口径接管见
        /// <see cref="ApplyExplored"/>）。⛔ 本面板不自己决定"哪些格已探索"。
        /// </summary>
        private void OnMapExplored(IReadOnlyCollection<Vector2Int> cells)
        {
            ApplyExplored(cells);
        }
    }
}
