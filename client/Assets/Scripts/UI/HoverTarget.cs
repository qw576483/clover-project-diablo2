// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · UI/HoverTarget.cs
// 悬停接线件：实现 = 引擎 `CloverEngine.PointerHoverRelay`
// （`clover-client-unity-engine/Runtime/Presentation/PointerFloatLayer.cs`）。
// 本文件只提供项目侧类型名 + `OnEnter` / `OnExit` 两个 `Action` 字段；
// 调用点 = `CharCreatePanel` 的 `AddComponent<HoverTarget>()`。
//
// 原版前端**确实有悬停反馈**：
//   参考物 `Diablerie/.../Menu/ClassSelect/ClassSelector.cs:137-155`
//     `OnPointerEnter → ToggleHover(true)`（换 NU2 帧）+ 通知面板换「职业名/职业说明」两行文字；
//     `OnPointerExit  → ToggleHover(false)`（换回 NU1）。
// `OnPointerEnter/Exit` 的转发在引擎基类里（改一处 = 所有项目同步）。
// ─────────────────────────────────────────────────────────────────────────────

using CloverEngine;

namespace Diablo2.UI
{
    /// <summary>
    /// 指针进出回调的接线件（实现 = 引擎 <see cref="PointerHoverRelay"/>）。
    /// <para>与 `Diablo2.Def.HoverTarget`（"被悬停的实体"载荷）**同名不同物**，引用时写全限定名
    /// （见 `UI/EnemyBarView.cs` 的注释）。</para>
    /// </summary>
    public sealed class HoverTarget : PointerHoverRelay
    {
    }
}
