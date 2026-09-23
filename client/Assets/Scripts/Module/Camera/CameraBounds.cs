// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · Module/Camera/CameraBounds.cs
// 「相机边界夹制」的**纯数学**：无 Unity 原生调用、不碰 `Game` / `AppContext`
// ⇒ 离线自检宿主（tools/probes/hosts/mapcheck §29）**直接链本文件调生产实现**，
//    ⛔ 不必在探针里再镜像一份"看起来一样"的公式（镜像 = 改了生产也不变红的假闸门）。
//
// ── ★ 缺陷修复（camera-clamp 片，2026-09-23）：夹制用错了坐标系 ────────────────
//   **实测现象**（bw_deep_bwy1.txt）：进血腥荒野后机位 (-19,-11,-10)，chunk 侧 `MISSING=0`
//   却仍有一大片连续黑区，像素量法 blackFrac=0.340（两个独立量法互相印证）。
//   **根因**：`CameraRig.MapWorldBounds` 取的是等距**菱形**四角在世界里的**轴对齐包围盒 AABB**
//   （80×80 地图 ⇒ 158×79 世界单位），`ClampFocus` 只把焦点夹进「AABB − 半屏」。
//   菱形与它的 AABB 之间那**四个三角区**在世界里根本没有对应格子（= 地图外虚空），
//   而 AABB 夹制把它们当成"图内"⇒ 机位落在 AABB 内即被放行（实测该机位就是 no-op）
//   ⇒ 屏幕上 31.3% 是地图外虚空 = 那片黑。
//   **修法**（本文件）：夹制回到**格空间** —— 把「可见格矩形」夹进 [0..W-1]×[0..H-1]。
//
// ── 口径（与 `Core/Iso` 同源；⛔ 不写第二份常量）───────────────────────────────
//   连续格坐标 (gx, gy)：整数 = **格线**（`Iso.WorldToGridContinuous` 的口径；
//   格子 g 覆盖 [g, g+1)，`Iso.GridToWorld(gx,gy)` 返回的是**中心** (gx+0.5, gy+0.5)）。
//   旋转到菱形自己的两条轴（屏幕矩形在这个框里是**轴对齐**的 ⇒ 两向可独立夹制）：
//        u = gx − gy =  X / Iso.HalfW          v = gx + gy = −Y / Iso.HalfH
//        X = u · Iso.HalfW                     Y = −v · Iso.HalfH
//   半屏在 (u,v) 框里的半跨：a = halfW / Iso.HalfW ，b = halfH / Iso.HalfH
//   ⇒ 可见格矩形 = { |Δu| ≤ a, |Δv| ≤ b }（一个菱形）；它全部落在地图内 ⟺
//        s1 = u + v ∈ [a+b, 2(W−1) − (a+b)]      （s1 = 2·gx ∈ [0, 2(W−1)]）
//        s2 = v − u ∈ [a+b, 2(H−1) − (a+b)]      （s2 = 2·gy ∈ [0, 2(H−1)]）
//
// ── ⚠️ 一条**硬约束**：主角必须留在视口内 ──────────────────────────────────────
//   把菱形整个塞进地图需要的位移可能大于"主角还在画面里"允许的位移 —— 在**地图角格**
//   上两者**数学上不可兼得**（证明：两条约束相加得 v ≥ a+b，而主角可见要求 |Δv| ≤ b，
//   在角格处 a+b > 2b 时无解）。此时**让位给主角可见**（地图角落那点虚空由地图边界块
//   的美术去盖，属 `Module/Map` 侧，⛔ 不是相机该解决的事）。
//   判据：`tools/probes/hosts/playercheck` §11「被钳制 ⇒ 焦点仍在视口内」。
// ─────────────────────────────────────────────────────────────────────────────

using Diablo2.Core;
using UnityEngine;

// ⚠️ 命名空间与 `CameraRig.cs` 一致（`Diablo2.Module`，⛔ **不是** `Diablo2.Module.Camera`）：
//   后者会让 `Module/Map/MapView.cs` 里的裸 `Camera` 解析成命名空间（CS0118，本机真报过）。
namespace Diablo2.Module
{
    /// <summary>
    /// 相机边界夹制的纯函数（**格空间**，⛔ 不是世界 AABB）。
    /// <para>是 `CameraRig.CameraPosForFocus` 与 `tools/probes/hosts/mapcheck` §29 的**同一份实现**。</para>
    /// </summary>
    public static class CameraBounds
    {
        /// <summary>"主角可见优先"生效过（只报一次，避免每帧刷屏）。</summary>
        private static bool _visibilityWonLogged;

        /// <summary>
        /// 把机位（= 焦点世界坐标）夹到「可见**格**矩形 ⊆ 地图」，并保证焦点仍在视口内。
        /// </summary>
        /// <param name="focus">焦点（= 未夹制时的机位）世界坐标。</param>
        /// <param name="mapWidth">地图宽（格）。</param>
        /// <param name="mapHeight">地图高（格）。</param>
        /// <param name="halfW">半屏宽（世界单位 = `orthoSize * aspect`）。</param>
        /// <param name="halfH">半屏高（世界单位 = `orthoSize`）。</param>
        /// <returns>夹制后的机位世界坐标（xy）。</returns>
        public static Vector2 ClampFocusGrid(Vector2 focus, int mapWidth, int mapHeight, float halfW, float halfH)
        {
            var iw = Iso.HalfW;                       // 等距半格（世界单位）：1.0 / 0.5
            var ih = Iso.HalfH;
            if (mapWidth <= 0 || mapHeight <= 0 || halfW <= 0f || halfH <= 0f || iw <= 0f || ih <= 0f)
                return focus;

            var a = halfW / iw;                       // 半屏在 u 方向的半跨（格）
            var b = halfH / ih;                       // 半屏在 v 方向的半跨（格）
            var margin = a + b;                       // 菱形顶点到中心在两个方向上的合计跨度

            var u = focus.x / iw;                     // 焦点（= 机位）在菱形轴系里的坐标
            var v = -focus.y / ih;

            var s1 = ClampSpan(u + v, margin, 2f * (mapWidth - 1) - margin);    // = 2·gx
            var s2 = ClampSpan(v - u, margin, 2f * (mapHeight - 1) - margin);   // = 2·gy

            var u2 = (s1 - s2) * 0.5f;
            var v2 = (s1 + s2) * 0.5f;

            // 主角可见预算：焦点必须还在视口内 ⇒ |Δu| ≤ a、|Δv| ≤ b（见文件头「硬约束」）。
            var uc = Mathf.Clamp(u2, u - a, u + a);
            var vc = Mathf.Clamp(v2, v - b, v + b);
            if (Mathf.Abs(uc - u2) > 1e-4f || Mathf.Abs(vc - v2) > 1e-4f)
            {
                if (!_visibilityWonLogged)
                {
                    _visibilityWonLogged = true;
                    Log.Warn("Camera", $"地图角格处「零虚空」与「主角可见」不可兼得 ⇒ 本次让位给主角可见" +
                                       $"（机位 ({uc * iw:0.##},{-vc * ih:0.##})，" +
                                       $"零虚空解为 ({u2 * iw:0.##},{-v2 * ih:0.##})，只报一次）");
                }
                u2 = uc;
                v2 = vc;
            }

            return new Vector2(u2 * iw, -v2 * ih);
        }

        /// <summary>纯函数：把 [lo,hi] 这段可行区间夹出来；区间为空（视野比地图还大）⇒ 居中（原版语义）。</summary>
        private static float ClampSpan(float s, float lo, float hi)
        {
            if (hi <= lo) return (lo + hi) * 0.5f;
            return Mathf.Clamp(s, lo, hi);
        }
    }
}
