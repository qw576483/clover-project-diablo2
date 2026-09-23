// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · UI/GroundItemLabelView.cs  ★ 本项目新增（impl-I-input，审计 R5）
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
// 样式口径（⛔ 不自创）：
//   · 字体 = `D2Text.D2Font.Font16` —— 与 HUD 技能格热键标签同一号（原版最小号，项目 UI 口径）；
//   · 颜色 = 品质色 `ItemQualityColor.Of(quality)` —— 与 `UI/ItemTooltip` 的名称配色**同一张表**；
//   · 尺寸 = 一格的像素盒（`GameConst.IsoTilePxW` × `GameConst.HalfTilePxH`，契约常量）——
//     名牌不画底框（原版地面物品名也只是一行字），尺寸只决定折行宽度；
//   · 位置 = 该格中心的世界坐标再抬**半格高**（`GameConst.IsoHalfH` = 瓦片半高的世界单位）
//     ⇒ 字落在物品所在格的顶边上（原版名牌就在物品上方）。
//
// ⛔ 一个 `.cs` 一个 MonoBehaviour：本类**不是** MonoBehaviour（由 HudPanel 持有并驱动）。
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
        /// <para>★ 片 font-scale 修（与 V6 给 `EntityTooltip` 的修法同一口径）：原来这里是**世界格 px**
        /// （`GameConst.IsoTilePxW = 128`），而 `D2Label` 的**换行宽度/字号都是画布单位** ⇒ 两套单位混用
        /// 会让换行阈值等于 128/scale（把名牌文字提前折行）。补上显式字号后必须同时换算，
        /// 否则"字变大但框还是世界格 ⇒ 名字被折成两行"。判据：`uicheck` FontScaleCheck 的框单位断言。</para>
        /// </summary>
        private static readonly Vector2 LabelSize =
            new Vector2(GameConst.IsoTilePxW * UiLayoutGame.K, GameConst.HalfTilePxH * UiLayoutGame.K);

        private readonly RectTransform _parent;
        private readonly RectTransform _canvas;

        /// <summary>地面物品 id → 名牌节点（按 id 复用，避免每次悬停都新建/销毁）。</summary>
        private readonly Dictionary<int, D2Label> _nodes = new Dictionary<int, D2Label>();

        /// <summary>本帧没出现在载荷里的 id（收尾时隐藏）。</summary>
        private readonly List<int> _stale = new List<int>();

        private bool _canvasWarned;
        private bool _camWarned;

        /// <summary>当前显示中的名牌数（自证/断言用）。</summary>
        public int VisibleCount { get; private set; }

        /// <summary>最近一次收到的 Alt 态（自证/断言用）。</summary>
        public bool AltHeld { get; private set; }

        public GroundItemLabelView(RectTransform parent)
        {
            _parent = parent;
            _canvas = parent != null ? parent.GetComponentInParent<Canvas>()?.transform as RectTransform : null;
            if (_canvas == null)
            {
                _canvasWarned = true;
                UiLog.Warn("找不到所属 Canvas ⇒ 名牌位置换算退化为格原点（名牌仍会创建，但不跟随物品）");
            }
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
            _stale.Clear();
            foreach (var kv in _nodes) _stale.Add(kv.Key);

            var shown = 0;
            for (var i = 0; i < args.labels.Count; i++)
            {
                var l = args.labels[i];
                if (l == null || l.id < 0) continue;

                _stale.Remove(l.id);

                var label = NodeOf(l.id);
                if (label == null) continue;                 // 建节点失败（只在非预期下发生，已 Warn）

                var text = string.IsNullOrEmpty(l.name) ? "物品 #" + l.id : l.name;
                label.text = text;
                label.color = ItemQualityColor.Of(l.quality);

                var rt = label.rectTransform;
                var pos = ScreenAnchoredPositionOf(l.gridX, l.gridY);
                if (pos.HasValue)
                {
                    label.SetActive(true);
                    rt.anchoredPosition = pos.Value;
                }
                else
                {
                    // 非预期但可解释：相机缺失 / 格在相机背面（切场景那一帧）⇒ 本件藏起来，别画在错误位置
                    label.SetActive(false);
                }

                shown++;
            }

            for (var i = 0; i < _stale.Count; i++)
            {
                if (_nodes.TryGetValue(_stale[i], out var node) && node != null) node.SetActive(false);
            }

            VisibleCount = shown;
            UiLog.Info($"[{Tag}] 名牌应用：altHeld={AltHeld} 载荷 {args.labels.Count} 条 ⇒ 可见 {shown} 条、"
                + $"隐藏 {_stale.Count} 条（节点名 {NodePrefix}<id>）");
        }

        /// <summary>隐藏全部名牌（离场 / 载荷为空）。</summary>
        public void Clear()
        {
            foreach (var kv in _nodes)
                if (kv.Value != null) kv.Value.SetActive(false);
            if (VisibleCount != 0)
                UiLog.Info($"[{Tag}] 名牌清空（原可见 {VisibleCount} 条）");
            VisibleCount = 0;
            AltHeld = false;
        }

        /// <summary>销毁全部名牌节点（HUD 关闭 / 离场；HUD 自己会再 new 一个）。</summary>
        public void Destroy()
        {
            foreach (var kv in _nodes)
            {
                if (kv.Value == null) continue;
                var go = kv.Value.gameObject;
                if (go != null) Object.Destroy(go);
            }
            _nodes.Clear();
            VisibleCount = 0;
            AltHeld = false;
        }

        /// <summary>取（或建）某件地面物品的名牌节点。</summary>
        private D2Label NodeOf(int id)
        {
            if (_nodes.TryGetValue(id, out var exists) && exists != null) return exists;

            // ★ 片 font-scale：补显式字号（默认 0 = 按原版 px 1:1 画 ⇒ 名牌只有应有的 ~55%，
            //   用户报「文字太小」的 6 处之一）。框见 `LabelSize`（已换算到画布 px）。
            var label = D2Label.Create(_parent, NodePrefix + id, string.Empty, D2Text.D2Font.Font16,
                TextAnchor.LowerCenter, Color.white, LabelSize, Vector2.zero, (int)UiLayoutGame.FontPx16);
            if (label == null)
            {
                UiLog.WarnOnce("groundlabel.create." + id, $"名牌节点创建失败（id={id}）⇒ 该物品不显示名牌");
                return null;
            }

            label.SetActive(false);
            _nodes[id] = label;
            UiLog.Info($"[{Tag}] 名牌节点已创建：{NodePrefix}{id}「{label.text}」"
                + $"尺寸={LabelSize.x}×{LabelSize.y}（一格的像素盒）字体=Font16（项目 UI 口径）");
            return label;
        }

        /// <summary>
        /// 格 → 名牌在 Canvas 局部坐标里的落点（**纯换算**：世界坐标 = 格中心抬半格高；屏幕 = 相机投影）。
        /// 相机/画布不可用 ⇒ null（调用方把该件藏起来，绝不画到错误位置）。
        /// </summary>
        private Vector2? ScreenAnchoredPositionOf(int gridX, int gridY)
        {
            if (_canvas == null) return null;

            var cam = UIFactory.UICamera();     // 引擎口径：`Camera.main`，取不到退化成任意一台相机
            if (cam == null)
            {
                if (!_camWarned)
                {
                    _camWarned = true;
                    UiLog.Warn("找不到相机 ⇒ 地面物品名牌无法从格坐标投影到屏幕（名牌暂不显示；只报一次）");
                }
                return null;
            }

            var world = Iso.GridToWorld(gridX, gridY);
            world.y += GameConst.IsoHalfH;      // 抬半格高 = 落在该格顶边上（见文件头「样式口径」）

            var screen = cam.WorldToScreenPoint(world);
            if (screen.z < 0f) return null;     // 相机背面（切场景那一帧）

            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    _canvas, new Vector2(screen.x, screen.y), null, out var local))
            {
                UiLog.WarnOnce("groundlabel.convert.fail", "屏幕点 → Canvas 局部坐标换算失败 ⇒ 名牌位置不更新");
                return null;
            }

            return local;
        }
    }
}
