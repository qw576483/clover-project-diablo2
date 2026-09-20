// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · UI/MiniMapPanel.cs
// 自动地图 / 小地图（原版 Tab 切换）：把格子数据画成一张贴图，只显示**已探索**的格子，
// 并在上面叠加玩家点与出入口/可交互标记。
//
// ★★ 片 5（「小地图 + 死亡屏 1:1」轮）改了什么（**只删自加物 + 换原版素材**）：
//   ① **删掉自加的标题行与图例行**（原版自动地图**没有**标题/图例文字 ——
//      `策划/自审对比/README.md` ② 第 12 行早先已登记这条不一致，片 5 收口）。
//      连带删掉 `HeaderH / TitleH / LegendH / HeaderGap / HeaderW / TitlePos / LegendPos`
//      与只服务于标题的 `AreaName()`（避免「定义了但没人用」）。
//   ② **标记改成原版 `mapicons.DC6` 的帧**（`D2/UI/MiniMap/mapicon_{i}.png`，
//      源 `data/global/ui/MINIMAP/mapicons.DC6`，**16×16 ×8**，片 1 解出），不再用纯色方块。
//      ⚠️ **哪一帧 = 什么语义 ⇒ BLOCKED**（8 帧是**白色模板**，实测只用调色板索引 32；
//      原版运行期用色表 shift 上色。参考工程 `Diablerie/**` 与 `libd2/**` **都不引用**
//      这个 DC6 —— 全仓 grep `mapicons|MINIMAP` 只在原版 d2dc6 里命中）⇒
//      按任务书「⛔ 不许自己指定语义」**统一用帧 0**（`ResPaths.MiniMapMarkerFrame`），
//      语义与出处登记为 BLOCKED（拿到语义只需改那一个常量）。
//   ③ 标记**着色**：因为原版图标是白模板，**必须**上色（否则所有标记一模一样）——
//      色值本身是**本项目选的**（登记 E23），沿用旧版已用户可见的两色（出入口=暖黄 / 可交互=冷蓝）。
//
// ★ **面板位置/尺寸**（登记 E23，**没有原版出处**）：原版自动地图是**满屏叠加层**
//   （把已探索格按 `AutoMap.txt` 的 Cel 号从 `ui/AUTOMAP/MaxiMap.dc6`（**1260 帧 16×32**，
//    **全 act 共用**）取小图 blit 到等距位置），原版那一族素材里**根本没有"窗口框"**
//   （只有标题/开关条 `automap` / `AutoMapCenter` / `AutoMapParty` / `automapfade` /
//    `AutoMapOptions`）。本项目是右上角定尺框（沿用 agent-09 的表现）⇒ 见 E23。
// ★ **视野口径**（登记 E23）：现状是「本格 + 8 邻域」**近似**。原版是"逐房间/逐格揭示
//   （`AUTOMAP_RevealRoom`，地板先、墙后）" ⇒ 要做成原版口径需要**逐格的 AutoMap.txt Cel 数据**
//   （LevelType/orientation/index/subindex），本项目的地图数据里没有这一层
//   （`Def.MinimapArgs.tiles` 只有 4 个地形码）⇒ **BLOCKED**（改 `Def`/`Module` = 改契约，须主裁决）。
//
// ★ 数据来源（**零模块耦合**）：
//   `Events.MapGenerated`（参数 `Def.MinimapArgs`：宽高 / 地形码 / seed / 标记点）
//   `Events.PlayerGridChanged`（参数 `Vector2Int` 格坐标）⇒ 本面板**自己累积已探索格**
// ★ 画法：`Texture2D`(宽 = 地图宽, 高 = 地图高) + `RawImage`，`FilterMode.Point` 保持像素风。
//   ⚠️ 没用「一格一个 Image」——40×80 的图会造出几千个 UI 节点（卡顿且吃内存）。
// ★ 层：`UILayer.Normal`（`tools/ai-skill/registry.md` 的面板表）⇒ 不与 HUD 抢 Popup 层。
// ★ Tab 键由 **HUD** 轮询并发 `Events.PanelToggleRequest`（HUD 是 Stage 里常驻的面板，
//   见 `UI/HudPanel.cs`）——本面板自身不读输入（避免两处各读一遍）。
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
        /// <summary>显示区最大边长（画布px；本项目表现，出处见 `UiLayoutGame.MiniMapBoxMax`）。</summary>
        public const float MaxDisplay = UiLayoutGame.MiniMapBoxMax;

        /// <summary>地图贴图四周留的内边距（画布px；本项目表现，见 E23）。</summary>
        public const float InnerPadding = 16f;

        // 地形码 → 颜色（`MinimapArgs.Tile*` 常量；未探索 = 全透明）
        private static readonly Color32 ColVoid = new Color32(0, 0, 0, 0);
        private static readonly Color32 ColWalkable = new Color32(70, 74, 62, 210);
        private static readonly Color32 ColBlocking = new Color32(24, 23, 20, 235);
        private static readonly Color32 ColExit = new Color32(226, 196, 92, 255);
        private static readonly Color32 ColInteractable = new Color32(96, 150, 220, 255);
        private static readonly Color32 ColUnexplored = new Color32(0, 0, 0, 0);

        /// <summary>出入口标记的**图标色调**（本项目选定，见文件头 ③；原版 8 帧是白模板，必须上色）。</summary>
        public static readonly Color MarkerExitTint = new Color(0.95f, 0.85f, 0.35f, 1f);

        /// <summary>可交互（NPC / 洞穴口）标记的**图标色调**（本项目选定，见文件头 ③）。</summary>
        public static readonly Color MarkerInteractTint = new Color(0.42f, 0.62f, 0.95f, 1f);

        private bool _built;
        private bool _subscribed;
        private MinimapArgs _map;

        /// <summary>已探索标记（行优先，长度 = width*height）。</summary>
        private bool[] _explored;

        private RectTransform _box;
        private RawImage _raw;
        private Texture2D _tex;
        private Color32[] _pixels;
        private RectTransform _playerDot;
        private readonly List<Image> _markers = new List<Image>();
        private float _scale = 1f;

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
                // 原版自动地图**没有标题行** ⇒ 这里只留日志（不再往屏幕上写"无数据"字样）
                UiLog.WarnOnce("minimap.no.map",
                    "自动地图未收到 `MinimapArgs` ⇒ 空白地图；请 Map 模块在生成后 emit `Events.MapGenerated`");
            }

            UiLog.Info($"自动地图已打开（地图={(map != null ? $"{map.width}×{map.height} seed={map.seed}" : "无")}"
                       + $"，标记用原版 D2/UI/MiniMap/mapicon_{ResPaths.MiniMapMarkerFrame}）");
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

            // 右上角固定（pivot 放在右上角 ⇒ 地图尺寸变化时不会跑位）。
            // ★ 片 5：删掉自加的表头带后，框顶沿直接贴画布上边留 Margin（不再让出 HeaderH）。
            _box = UIFactory.CreateCentered("MiniMapBox", transform,
                new Vector2(MaxDisplay, MaxDisplay), UiLayoutGame.MiniMapBoxPos);
            _box.pivot = new Vector2(1f, 1f);
            _box.anchorMin = _box.anchorMax = new Vector2(0.5f, 0.5f);

            var bgImg = UiArt.Panel(_box, "Bg", new Vector2(MaxDisplay, MaxDisplay), Vector2.zero,
                new Color(0f, 0f, 0f, 0.78f), true);
            var bgRt = bgImg.rectTransform;
            bgRt.anchorMin = Vector2.zero;
            bgRt.anchorMax = Vector2.one;
            bgRt.offsetMin = Vector2.zero;
            bgRt.offsetMax = Vector2.zero;

            var rawRt = UIFactory.CreateCentered("Map", _box,
                new Vector2(MaxDisplay - InnerPadding, MaxDisplay - InnerPadding), Vector2.zero);
            _raw = rawRt.gameObject.AddComponent<RawImage>();
            _raw.raycastTarget = false;
            _raw.color = Color.white;

            _playerDot = UiArt.Panel(_box, "PlayerDot", new Vector2(6f, 6f), Vector2.zero,
                new Color(1f, 0.95f, 0.55f, 1f), false).rectTransform;
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

            if (map.tiles == null || map.tiles.Count < map.width * map.height)
            {
                UiLog.Warn($"`MinimapArgs.tiles` 长度 {map.tiles?.Count ?? 0} 小于 {map.width}×{map.height}"
                           + " ⇒ 越界格按「图外」处理");
            }

            _map = map;
            _explored = new bool[map.width * map.height];

            if (_tex != null) UnityEngine.Object.Destroy(_tex);
            _tex = new Texture2D(map.width, map.height, TextureFormat.RGBA32, false);
            _tex.filterMode = FilterMode.Point;
            _tex.wrapMode = TextureWrapMode.Clamp;
            _pixels = new Color32[map.width * map.height];

            var inner = MaxDisplay - InnerPadding;
            _scale = Mathf.Min(inner / map.width, inner / map.height);
            var display = new Vector2(map.width * _scale, map.height * _scale);
            _raw.rectTransform.sizeDelta = display;
            _raw.texture = _tex;

            _box.sizeDelta = display + new Vector2(InnerPadding, InnerPadding);

            RevealPlayer(map.playerX, map.playerY);
            BuildMarkers();
            Redraw();
        }

        /// <summary>把玩家当前格（及其邻域）标为已探索，然后提交贴图。</summary>
        private void RevealPlayer(int gx, int gy)
        {
            if (_map == null) return;

            // ⚠️ **近似**（登记 E23）：原版是逐房间/逐格揭示（地板先、墙后），
            //    精确口径需要逐格的 AutoMap.txt Cel 数据（本项目地图数据里没有这一层）。
            //    这里用「本格 + 8 邻域」（半径 1）。
            for (var dy = -1; dy <= 1; dy++)
            {
                for (var dx = -1; dx <= 1; dx++)
                {
                    var x = gx + dx;
                    var y = gy + dy;
                    if (x < 0 || y < 0 || x >= _map.width || y >= _map.height) continue;
                    _explored[y * _map.width + x] = true;
                }
            }
        }

        private void Redraw()
        {
            if (_map == null || _tex == null || _pixels == null) return;

            for (var y = 0; y < _map.height; y++)
            {
                for (var x = 0; x < _map.width; x++)
                {
                    var i = y * _map.width + x;
                    // 贴图 y 轴自底向上 ⇒ 行号要翻（与 `AssetImporter.BuildFontRects` 同一坑）
                    var texIndex = (_map.height - 1 - y) * _map.width + x;
                    _pixels[texIndex] = _explored != null && i < _explored.Length && _explored[i]
                        ? ColorOf(_map.TileAt(x, y))
                        : ColUnexplored;
                }
            }

            _tex.SetPixels32(_pixels);
            _tex.Apply(false);

            UpdatePlayerDot(_map.playerX, _map.playerY);
        }

        private static Color32 ColorOf(byte tile)
        {
            switch (tile)
            {
                case MinimapArgs.TileWalkable: return ColWalkable;
                case MinimapArgs.TileBlocking: return ColBlocking;
                case MinimapArgs.TileExit: return ColExit;
                case MinimapArgs.TileInteractable: return ColInteractable;
                case MinimapArgs.TileVoid: return ColVoid;
                default:
                    UiLog.WarnOnce("minimap.tile." + tile, $"未知地形码 {tile} ⇒ 按图外处理（Map 模块请检查映射）");
                    return ColVoid;
            }
        }

        private void UpdatePlayerDot(int gx, int gy)
        {
            if (_map == null || _playerDot == null) return;

            var size = _raw.rectTransform.sizeDelta;
            var x = (gx + 0.5f) * _scale - size.x * 0.5f;
            var y = size.y * 0.5f - (gy + 0.5f) * _scale;
            _playerDot.anchoredPosition = new Vector2(x, y);
        }

        /// <summary>
        /// 造出入口 / 可交互标记：底图 = 原版 `mapicons.DC6` 的帧（**不是纯色方块**），
        /// 色调由本项目给（原版图标是白模板，必须上色 —— 见文件头 ③）。
        /// </summary>
        private void BuildMarkers()
        {
            for (var i = 0; i < _markers.Count; i++)
                if (_markers[i] != null) UnityEngine.Object.Destroy(_markers[i].gameObject);
            _markers.Clear();

            if (_map?.markerX == null) return;

            var iconPath = ResPaths.MiniMapIcon(ResPaths.MiniMapMarkerFrame);
            var side = new Vector2(UiLayoutGame.MiniMapIconPx, UiLayoutGame.MiniMapIconPx);

            for (var i = 0; i < _map.markerX.Count; i++)
            {
                if (i >= _map.markerY.Count || i >= _map.markerKind.Count)
                {
                    UiLog.Warn($"`MinimapArgs` 标记数组长度不一致（markerX={_map.markerX.Count}）⇒ 第 {i} 个起忽略");
                    break;
                }

                var exit = _map.markerKind[i] == MinimapArgs.TileExit;
                var dot = UiArt.Art(_box, "Marker" + i, iconPath, side, Vector2.zero);
                // 原版图标是"白色模板"（8 帧只用索引 32 = #F4F4F4）⇒ 必须上色，否则所有标记同色
                UiArt.SetArtTint(dot, exit ? MarkerExitTint : MarkerInteractTint);
                dot.rectTransform.anchoredPosition = new Vector2(
                    _map.markerX[i] * _scale - _raw.rectTransform.sizeDelta.x * 0.5f,
                    _raw.rectTransform.sizeDelta.y * 0.5f - _map.markerY[i] * _scale);
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
        }

        private void Unsubscribe()
        {
            if (!_subscribed || Game.Event == null) return;
            _subscribed = false;
            Game.Event.Off<MinimapArgs>(Events.MapGenerated, OnMapGenerated);
            Game.Event.Off<Vector2Int>(Events.PlayerGridChanged, OnPlayerGrid);
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
    }
}
