// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · UI/SkillSlotHit.cs
// HUD 左/右技能格的**屏幕命中测试**（拖拽落格的查询口）。
//
// 为什么要有它（而不是用 uGUI 的 IDropHandler）：
//   拖拽源 = `UI/SkillTreePanel`（Normal 层窗口式面板），落格 = `UI/HudPanel` 的
//   `LeftSkill` / `RightSkill` 两格。两条路都能走：
//     ① uGUI 标准 Drop：拖拽结束时对 pointer 位置做射线命中，在命中对象及其父链上找 `IDropHandler`。
//        但拖拽期间指针下面压着**技能树面板自己的吃射线底图**（面板不透明区域），
//        射线命中的是面板而不是 HUD 的格 ⇒ Drop 收不到（除非给面板开洞，代价大且脆）。
//     ② **屏幕点 → 矩形包含**的显式查询（本类）：拖拽端在 `OnEndDrag` 里问一句
//        "这个屏幕点落在哪只手的格上"，不依赖射线命中顺序。口径与 `SkillTreePanel.NodeAt`
//        / `InventoryPanel` 的命中测试**同源**（`RectTransformUtility.RectangleContainsScreenPoint`）。
//   ⇒ 取 ②（与项目里"指针命中一律走屏幕点包含"的既有写法一致）。
//
// 登记口 = `HudPanel.BuildSkillSlots`（建格时登记两格的 RectTransform；HUD 重建/销毁时清）。
// 查询方 = `SkillTreePanel.OnEndDrag`（拖拽松手时问一手）。
// ─────────────────────────────────────────────────────────────────────────────

using UnityEngine;

namespace Diablo2.UI
{
    /// <summary>
    /// HUD 左右技能格的屏幕命中测试（见文件头：为什么不用 uGUI 的 `IDropHandler`）。
    /// <para>只登记、只查询；**不持有业务**（绑定请求由 `Events.SkillSlotBindRequest` 走技能模块）。</para>
    /// </summary>
    internal static class SkillSlotHit
    {
        /// <summary>左键技能格（`HudPanel` 建格时登记；HUD 未建/已清 ⇒ null）。</summary>
        public static RectTransform Left;

        /// <summary>右键技能格（同上）。</summary>
        public static RectTransform Right;

        /// <summary>登记两格（由 `HudPanel.BuildSkillSlots` 调用；重复调用 = 覆盖，幂等）。</summary>
        public static void Register(RectTransform left, RectTransform right)
        {
            Left = left;
            Right = right;
        }

        /// <summary>清登记（HUD 侧 `OnClose` 调；之后 `SlotAt` 一律返回 -1）。</summary>
        public static void Clear()
        {
            Left = null;
            Right = null;
        }

        /// <summary>
        /// 屏幕点落在哪只手的技能格上。
        /// </summary>
        /// <param name="screen">屏幕点（`PointerEventData.position`）。</param>
        /// <param name="cam">事件相机（Screen Space - Overlay 传 null，与 `NodeAt` 同口径）。</param>
        /// <returns>0 = 左键格 / 1 = 右键格 / -1 = 都没中（或 HUD 未建）。</returns>
        public static int SlotAt(Vector2 screen, Camera cam)
        {
            if (Left != null && Left.gameObject.activeInHierarchy
                && RectTransformUtility.RectangleContainsScreenPoint(Left, screen, cam))
            {
                return 0;
            }
            if (Right != null && Right.gameObject.activeInHierarchy
                && RectTransformUtility.RectangleContainsScreenPoint(Right, screen, cam))
            {
                return 1;
            }
            return -1;
        }
    }
}
