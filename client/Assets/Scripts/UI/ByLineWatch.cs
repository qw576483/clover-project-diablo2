// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · UI/ByLineWatch.cs
//
//  品牌署名行（`UiLayoutFlow.Brand` 挂出来的 `ByLine` 节点）上的**生命周期通知件**：
//  `OnEnable` / `OnDisable` / `OnDestroy` 各把 `Brand.Refresh()` 叫一次，
//  「同一屏恰好一条可见」的裁决因此能跟上**面板被销毁 / 被隐藏**这两件事 ——
//  引擎 `UIManager.Close` 走 `Object.Destroy`（**帧末**才真的消失，`Runtime/Presentation/UI.cs:210`），
//  而面板打开 / 关闭事件在那一刻看到的是"还没死掉"的旧节点 ⇒ 只靠事件会把最上层判错。
//
//  本件**不含任何裁决逻辑**（裁决只有 `UiLayoutFlow.Brand.Refresh` 一处）—— 它只负责"叫一声"。
// ─────────────────────────────────────────────────────────────────────────────

using UnityEngine;

namespace Diablo2.UI
{
    /// <summary>署名行的生命周期通知：状态一变就请 `UiLayoutFlow.Brand` 重算可见性。</summary>
    internal sealed class ByLineWatch : MonoBehaviour
    {
        private void OnEnable() { UiLayoutFlow.Brand.Refresh(); }

        private void OnDisable() { UiLayoutFlow.Brand.Refresh(); }

        private void OnDestroy() { UiLayoutFlow.Brand.Refresh(); }
    }
}
