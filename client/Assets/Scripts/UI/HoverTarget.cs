// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · UI/HoverTarget.cs
// 悬停接线件：**已下沉到引擎**（`CloverEngine.PointerHoverRelay`，
// `clover-client-unity-engine/Runtime/Presentation/PointerFloatLayer.cs`）——
// 本文件退化为**空子类**，公开 API（类名 + `OnEnter` / `OnExit` 两个 `Action` 字段）
// 与全部调用点（`CharCreatePanel` 的 `AddComponent<HoverTarget>()`）一字不改
// （`结构规则.md` §4.4：引擎已有同类能力 ⇒ 项目侧只留薄转发）。
//
// 为什么引擎需要它：引擎 `UIFactory` 只提供"点击"（`Button.onClick`），原先**没有悬停事件**
//（下沉前全引擎 grep `IPointerEnterHandler` / `EventTrigger` = 0 命中）；而原版前端**确实有悬停反馈**：
//   参考物 `Diablerie/.../Menu/ClassSelect/ClassSelector.cs:137-155`
//     `OnPointerEnter → ToggleHover(true)`（换 NU2 帧）+ 通知面板换「职业名/职业说明」两行文字；
//     `OnPointerExit  → ToggleHover(false)`（换回 NU1）。
// ⛔ 本文件不再自带实现：`OnPointerEnter/Exit` 的转发在引擎基类里（改一处 = 所有项目同步）。
// ─────────────────────────────────────────────────────────────────────────────

using CloverEngine;

namespace Diablo2.UI
{
    /// <summary>
    /// 指针进出回调的接线件（实现 = 引擎 <see cref="PointerHoverRelay"/>）。
    /// <para>⛔ 与 `Diablo2.Def.HoverTarget`（"被悬停的实体"载荷）**同名不同物**，引用时写全限定名
    /// （见 `UI/EnemyBarView.cs` 的注释）。</para>
    /// </summary>
    public sealed class HoverTarget : PointerHoverRelay
    {
    }
}
