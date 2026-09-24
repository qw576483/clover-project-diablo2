// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · UI/GroundItemLabelView.cs  本项目新增（impl-I-input，审计 R5）
//
// 地面物品**名牌层**：原版 D2 的两条表现 ——
//   ① 鼠标悬停地面物品 ⇒ 该物品显示名牌（名字按品质配色）；
//   ② 按住 `Alt` ⇒ **常显**当前区域全部地面物品名。
//
// 数据来源 = **事件**（本层零 `using Diablo2.Module`，分层自检 ③）：
//   `Events.GroundItemLabelsChanged`（载荷 `Def.GroundItemLabelsArgs`，命名空间 `Diablo2.Def`）
//   由 `Module/Input/InputReader.PublishGroundItemLabels` 发出 —— 它消费两件事：
//     · `InputReader.ShowGroundItems`（= 原版 `Alt`；改动前该属性**全仓 0 消费**）
//     · `Events.HoverTargetChanged` 解析出的当前悬停格
//
// 为什么挂在 HUD（`UI/HudPanel` 持有）：HUD 是进图后常驻的画布层，名牌必须画在
//   角色/物品之上、又不该被面板遮住；节点与 HUD 同生命周期（`StageEntered` 建 / `StageLeft` 拆）。
//
//   「按 id 池化复用 + 世界点投影到画布 + 显隐 + 收尾隐藏」这一套机制移入引擎件
//   `CloverEngine.WorldProjectedLabelLayer`（`clover-client-unity-engine/Runtime/Presentation/
//   WorldOverlayWidgets.cs`）；本文件只剩三件事：
//     ① D2 取值：文案（名字 / "物品 #id" 兜底）、品质配色、世界落点（格中心 + 半格高）；
//     ② 渲染注入：把 `D2Label` 包成引擎认得的 `IOverlayLabelView`（引擎不许引用项目类）；
//     ③ 事件订阅与日志（调用点 / 公开 API **零改动**）。
//   屏幕点 ⇄ 画布局部点换算由引擎件统一走 `ScreenPointUtil`（本文件不再自己调 `RectTransformUtility`）。
//
// 样式口径（不自创）：
//   · 字体 = `D2Text.D2Font.Font16` —— 与 HUD 技能格热键标签同一号（原版最小号，项目 UI 口径）；
//   · 颜色 = 品质色 `ItemQualityColor.Of(quality)` —— 与 `UI/ItemTooltip` 的名称配色**同一张表**；
//   · 尺寸 = 一格的像素盒（`GameConst.IsoTilePxW` × `GameConst.HalfTilePxH`，契约常量）——
//     名牌不画底框（原版地面物品名也只是一行字），尺寸只决定折行宽度；
//   · 位置 = 该格中心的世界坐标再抬**半格高**（`GameConst.IsoHalfH` = 瓦片半高的世界单位）
//     ⇒ 字落在物品所在格的顶边上（原版名牌就在物品上方）。
//
// 一个 `.cs` 一个 MonoBehaviour：本类**不是** MonoBehaviour（由 HudPanel 持有并驱动）。
// ─────────────────────────────────────────────────────────────────────────────

using System.Collections.Generic;
using CloverEngine;
using Diablo2.Core;
using Diablo2.Def;
using UnityEngine;

namespace Diablo2.UI
{
    /// <summary>地面物品名牌层（见文件头）。</summary>
    internal sealed class GroundItemLabelView
    {
        private const string Tag = "GroundItemLabel";

        /// <summary>名牌节点名前缀（离线/实机都靠它判"节点建出来了"，见验收断言）。</summary>
        public const string NodePrefix = "GroundItemLabel_";

        /// <summary>
        /// 名牌矩形尺寸 = 一格的像素盒（出处见文件头「样式口径」），**换算到画布 px**（× `UiLayoutGame.K`）。
        /// <para> 修（与 给 `EntityTooltip` 的修法同一口径）：原来这里是**世界格 px**
        /// （`GameConst.IsoTilePxW = 128`），而 `D2Label` 的**换行宽度/字号都是画布单位** ⇒ 两套单位混用
        /// 会让换行阈值等于 128/scale（把名牌文字提前折行）。补上显式字号后必须同时换算，
        /// 否则"字变大但框还是世界格 ⇒ 名字被折成两行"。判据：`uicheck` FontScaleCheck 的框单位断言。</para>
        /// </summary>
        private static readonly Vector2 LabelSize =
            new Vector2(GameConst.IsoTilePxW * UiLayoutGame.K, GameConst.HalfTilePxH * UiLayoutGame.K);

        /// <summary>引擎通用件：世界投影标签层（**池化复用 / 投影 / 显隐**全在它里面）。</summary>
        private readonly WorldProjectedLabelLayer _layer;

        /// <summary>本次要应用的标签（复用一个列表：`Apply` 是悬停驱动的热路径，不每次分配）。</summary>
        private readonly List<WorldLabelItem> _items = new List<WorldLabelItem>();

        /// <summary>当前显示中的名牌数（自证/断言用；转发引擎件）。</summary>
        public int VisibleCount => _layer != null ? _layer.VisibleCount : 0;

        /// <summary>最近一次收到的 Alt 态（自证/断言用）。</summary>
        public bool AltHeld { get; private set; }

        public GroundItemLabelView(RectTransform parent)
        {
            var canvas = parent != null ? parent.GetComponentInParent<Canvas>()?.transform as RectTransform : null;
            if (canvas == null)
            {
                UiLog.Warn("找不到所属 Canvas ⇒ 名牌位置换算退化为格原点（名牌仍会创建，但不跟随物品）");
            }

            // 引擎件只做机制；**画什么字**由 `CreateLabelView` 注入（项目侧 `D2Label` 位图字模）。
            _layer = new WorldProjectedLabelLayer(canvas, parent, CreateLabelView, Tag);
        }

        /// <summary>
        /// 应用一次名牌载荷（`Events.GroundItemLabelsChanged` 的收方）。
        /// 空/无载荷 ⇒ 隐藏全部名牌（原版：指针离开物品且没按 Alt = 不显示）。
        /// </summary>
        public void Apply(GroundItemLabelsArgs args)
        {
            if (args == null || args.labels == null || args.labels.Count == 0)
            {
                Clear();
                return;
            }

            AltHeld = args.altHeld;
            _items.Clear();
            for (var i = 0; i < args.labels.Count; i++)
            {
                var l = args.labels[i];
                if (l == null || l.id < 0) continue;      // 非法 id：不喂给引擎（引擎会跳过并留痕）

                // D2 取值全部在这三行：世界落点 / 文案兜底 / 品质配色
                _items.Add(new WorldLabelItem
                {
                    Id = l.id,
                    World = WorldOf(l.gridX, l.gridY),
                    Text = string.IsNullOrEmpty(l.name) ? "物品 #" + l.id : l.name,
                    Color = ItemQualityColor.Of(l.quality),
                });
            }

            var shown = _layer.Apply(_items);
            UiLog.Info($"[{Tag}] 名牌应用：altHeld={AltHeld} 载荷 {args.labels.Count} 条 ⇒ 可见 {shown} 条、"
                + $"隐藏 {_items.Count - shown} 条（节点名 {NodePrefix}<id>；池化/投影机制 = 引擎 WorldProjectedLabelLayer）");
        }

        /// <summary>隐藏全部名牌（离场 / 载荷为空）。</summary>
        public void Clear()
        {
            var before = VisibleCount;
            if (_layer != null) _layer.Clear();
            if (before != 0)
                UiLog.Info($"[{Tag}] 名牌清空（原可见 {before} 条）");
            AltHeld = false;
        }

        /// <summary>销毁全部名牌节点（HUD 关闭 / 离场；HUD 自己会再 new 一个）。</summary>
        public void Destroy()
        {
            if (_layer != null) _layer.Dispose();
            AltHeld = false;
        }

        /// <summary>
        /// 格 → 世界落点（**D2 语义**：格中心再抬半格高 = 落在该格顶边上，见文件头「样式口径」）。
        /// </summary>
        private static Vector3 WorldOf(int gridX, int gridY)
        {
            var world = Iso.GridToWorld(gridX, gridY);
            world.y += GameConst.IsoHalfH;
            return world;
        }

        /// <summary>
        /// 引擎标签工厂（**项目侧渲染注入点**）：造一条 `D2Label`（原版字模、项目字号 / 对齐），
        /// 包成引擎认得的 `IOverlayLabelView`。返回 `null` ⇒ 该物品不显示名牌（引擎会留痕）。
        /// </summary>
        private static IOverlayLabelView CreateLabelView(int id, Transform parent)
        {
            var label = D2Label.Create(parent, NodePrefix + id, string.Empty, D2Text.D2Font.Font16,
                TextAnchor.LowerCenter, Color.white, LabelSize, Vector2.zero, (int)UiLayoutGame.FontPx16);
            if (label == null)
            {
                UiLog.WarnOnce("groundlabel.create." + id, $"名牌节点创建失败（id={id}）⇒ 该物品不显示名牌");
                return null;
            }

            UiLog.Info($"[{Tag}] 名牌节点已创建：{NodePrefix}{id} 尺寸={LabelSize.x}×{LabelSize.y}"
                + $"（一格的像素盒）字体=Font16（项目 UI 口径）");
            return new LabelView(label);
        }

        /// <summary>
        /// `D2Label` → 引擎 <see cref="IOverlayLabelView"/> 的适配器。
        /// <para>为什么要有它：引擎件**不许引用项目类**（`D2Label` 属 `Diablo2.UI`）⇒ 适配只能落在项目侧。
        /// 引擎只通过这 5 个动作驱动标签，不认识位图字模、也不知道字号。</para>
        /// </summary>
        private sealed class LabelView : IOverlayLabelView
        {
            private readonly D2Label _label;

            public LabelView(D2Label label) { _label = label; }

            /// <summary>节点被销毁 ⇒ `gameObject` 为 null（Unity 的"已销毁 == null"语义）。</summary>
            public bool IsAlive => _label != null && _label.gameObject != null;

            public RectTransform Rect => _label != null ? _label.rectTransform : null;

            public void SetText(string text) { if (_label != null) _label.SetText(text); }

            public void SetColor(Color color) { if (_label != null) _label.SetColor(color); }

            public void SetActive(bool active) { if (_label != null) _label.SetActive(active); }
        }
    }
}
