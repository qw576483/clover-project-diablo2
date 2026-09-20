// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · UI/HoverTarget.cs
// 最小的"鼠标悬停"接线件：把 uGUI 的 `IPointerEnter/Exit` 转成两个回调。
//
// 为什么需要它：引擎 `UIFactory` 只提供"点击"（`Button.onClick`），**没有悬停事件**
//（`clover-client-unity-engine/Runtime/Presentation/` 下 grep `IPointerEnterHandler` / `EventTrigger`
//  = 0 命中，实测）；而原版前端**确实有悬停反馈**：
//   参考物 `Diablerie/.../Menu/ClassSelect/ClassSelector.cs:137-155`
//     `OnPointerEnter → ToggleHover(true)`（换 NU2 帧）+ 通知面板换「职业名/职业说明」两行文字；
//     `OnPointerExit  → ToggleHover(false)`（换回 NU1）。
// ⇒ 按 §0「A 有 ⇒ 做」补这一件（不是新玩法，是把原版已有的悬停接线补上）。
//
// 只做转发，不做任何判定：回调为 null ⇒ 什么都不做（`Action` 为 null 是"没接线"而不是异常）。
// ⛔ 一个文件一个 MonoBehaviour（`constraints.md` #1）。
// ─────────────────────────────────────────────────────────────────────────────

using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Diablo2.UI
{
    /// <summary>把 uGUI 的指针进出事件转发给两个回调（见文件头）。</summary>
    public sealed class HoverTarget : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        /// <summary>指针进入时调用（未接线 = null ⇒ 什么都不做）。</summary>
        public Action OnEnter;

        /// <summary>指针离开时调用（未接线 = null ⇒ 什么都不做）。</summary>
        public Action OnExit;

        /// <inheritdoc/>
        public void OnPointerEnter(PointerEventData eventData)
        {
            if (OnEnter != null) OnEnter();
        }

        /// <inheritdoc/>
        public void OnPointerExit(PointerEventData eventData)
        {
            if (OnExit != null) OnExit();
        }
    }
}
