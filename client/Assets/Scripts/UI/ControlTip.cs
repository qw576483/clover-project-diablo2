// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · UI/ControlTip.cs
// 控件级悬浮提示：指针进控件时，在该控件**顶边中点**显示一行原版字模文本；移出 / 面板关闭即收。
//
// 原版**确有**这件机制（三条出处）：
//   ① 原版 prefab 的关闭钮节点上挂着源码件 `Tooltip`（`CharstatPanel.prefab` / `InventoryPanel.prefab`，
//      字段 `text: Close`）——见 `Core/ResPaths.cs` 的 `BuySellButtonFrameClose`；
//   ② 该件的行为 = `OnPointerEnter` → `Ui.ShowScreenLabel(控件矩形顶边中点, text)`、
//      `OnPointerExit` / `OnDisable` → `Ui.HideScreenLabel()`
//      （出处：参考实现 `Diablerie/Assets/Scripts/Diablerie/Engine/UI/Tooltip.cs`）；
//   ③ 机制在原版里是通用的：官方中文字串表 `原版资源/d2text/chi_string.txt` 的 id
//      **4167「打開迷你面板」** / **4168「關閉迷你面板」** 就是底部小面板开关钮的 hover 文案。
//
// 外观口径（**全部取自既有常量 / 既有实装**，本文件不含自创数值）：
//   · 尺寸 = 文本实测 + padding 左 6 / 右 6 / 上 0 / 下 4 ⇒ 直接复用 `UI/EnemyBarView.NameplateSizeFor`
//     （同一套 `ScreenLabel.cs:24-47` 口径：`ContentSizeFitter` + `VerticalLayoutGroup.padding`）；
//   · 底色 = `EnemyBarView.NameplateBackColor`（`RGBA(0,0,0,0.95)`，`ScreenLabel.cs:45`）；
//   · 字号 = `UiLayoutGame.FontPx16`；文字色 = `Color.white`
//     （参考实现不设文字色 ⇒ uGUI `Text` 默认白；同 `EnemyBarView.CreatePlateLabel` 的实参）。
//
// 显隐接线 = `UI/HoverTarget.cs`（实现 = 引擎 `CloverEngine.PointerHoverRelay`）的两个回调；
// 面板 `OnClose` 再收一次（对应参考实现的 `OnDisable → HideScreenLabel()`）。
// 本件不吃射线（底板 `raycastTarget=false`；`D2Label` 的图形层恒不挡点击）⇒ 提示不会把指针从控件上抢走。
// ─────────────────────────────────────────────────────────────────────────────

using UnityEngine;

namespace Diablo2.UI
{
    /// <summary>
    /// 控件级悬浮提示（节点 + 文本 + 落点；非 MonoBehaviour，由持有面板驱动）。
    /// <para>用法：`ControlTip.Create(面板根, 锚控件的 RectTransform, 文案)` →
    /// `HoverTarget.OnEnter = tip.Show` / `OnExit = tip.Hide` → 面板 `OnClose` 再 `Hide()`。</para>
    /// <para>落点契约：本件 `pivot = (0.5, 0)`（**底边中点**）⇒ `anchoredPosition` 直接写"提示底边中点
    /// 该落在哪"，与参考实现同口径（`ScreenLabel` 也是 `pivot (0.5,0)` + 位置 = 控件顶边中点）。</para>
    /// </summary>
    public sealed class ControlTip
    {
        /// <summary>节点名（层次里一眼能认出；也用于判据的源码检索）。</summary>
        public const string NodeName = "ControlTip";

        private readonly RectTransform _node;
        private readonly RectTransform _anchor;

        private ControlTip(RectTransform node, RectTransform anchor)
        {
            _node = node;
            _anchor = anchor;
        }

        /// <summary>提示节点（面板可继续改它的层次 / 显隐）。</summary>
        public RectTransform Rect { get { return _node; } }

        /// <summary>当前是否显示。</summary>
        public bool IsShown { get { return _node != null && _node.gameObject.activeSelf; } }

        /// <summary>
        /// 锚控件的**顶边中点**（**纯函数**，离线可断言）：x 取控件中心、y 取控件顶边。
        /// <para>口径 = 参考实现 `Tooltip.cs` 的 `new Vector2(rect.center.x, rect.yMax)`；
        /// 高度非正 ⇒ 不加偏移（不把异常高度算成"往下")。</para>
        /// </summary>
        /// <param name="anchorCenter">锚控件中心（与提示节点同一父空间）。</param>
        /// <param name="anchorHeight">锚控件高（同一父空间口径，取 `RectTransform.rect.height`）。</param>
        public static Vector2 TopCenterOf(Vector2 anchorCenter, float anchorHeight)
            => new Vector2(anchorCenter.x, anchorCenter.y + (anchorHeight > 0f ? anchorHeight : 0f) * 0.5f);

        /// <summary>
        /// 在 <paramref name="parent"/> 下建一件提示（**初始隐藏**），锚到 <paramref name="anchor"/>。
        /// </summary>
        /// <param name="parent">宿主（须与 <paramref name="anchor"/> 同一父节点 ⇒ 两者局部坐标同空间）。</param>
        /// <param name="anchor">锚控件（本工程面板节点都是"居中定尺"，见 <see cref="IsCentered"/>）。</param>
        /// <param name="text">文案（由调用方给，本件不自造）。</param>
        /// <returns>提示件；<paramref name="parent"/> / <paramref name="anchor"/> 为 null ⇒ 返回 null 并点名。</returns>
        public static ControlTip Create(Transform parent, RectTransform anchor, string text)
        {
            if (parent == null || anchor == null)
            {
                // 非预期分支：宿主 / 锚缺失 ⇒ 不建、点名（不静默返回一件摆不到位置的提示）。
                UiLog.Warn($"控件提示未建：parent 或 anchor 为 null（文案「{text}」）");
                return null;
            }

            var size = EnemyBarView.NameplateSizeFor(text);
            var bg = UiArt.Panel(parent, NodeName, size, Vector2.zero, EnemyBarView.NameplateBackColor, false);
            bg.rectTransform.pivot = new Vector2(0.5f, 0f);         // 底边中点 ⇒ 见类注释的落点契约
            D2Label.Create(bg.transform, "Label", text, EnemyBarView.NameplateFont, TextAnchor.MiddleCenter,
                Color.white, size, Vector2.zero, (int)UiLayoutGame.FontPx16);

            var tip = new ControlTip(bg.rectTransform, anchor);
            tip.Hide();
            return tip;
        }

        /// <summary>按锚控件**当前**位置摆好并显示。</summary>
        public void Show()
        {
            if (_node == null || _anchor == null) return;

            if (!IsCentered(_anchor))
            {
                // 非预期分支：锚不是"居中定尺"节点 ⇒ 本件不猜它的位置（点名，不静默摆错地方）。
                UiLog.WarnOnce("tip.anchor.mode",
                    $"控件提示的锚 {_anchor.name} 不是居中定尺节点（anchorMin={_anchor.anchorMin} / "
                    + $"anchorMax={_anchor.anchorMax}）⇒ 提示不显示");
                return;
            }

            _node.anchoredPosition = TopCenterOf(_anchor.anchoredPosition, _anchor.rect.height);
            _node.gameObject.SetActive(true);
        }

        /// <summary>收起（节点保留，供下一次 <see cref="Show"/> 复用）。</summary>
        public void Hide()
        {
            if (_node != null) _node.gameObject.SetActive(false);
        }

        /// <summary>锚控件是否"居中定尺"（`anchorMin = anchorMax = (0.5,0.5)`）。</summary>
        private static bool IsCentered(RectTransform rt)
            => rt.anchorMin == new Vector2(0.5f, 0.5f) && rt.anchorMax == new Vector2(0.5f, 0.5f);
    }
}
