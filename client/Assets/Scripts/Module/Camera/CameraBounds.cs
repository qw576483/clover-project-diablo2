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
        /// <summary>
        /// ★ camera-follow 片（2026-09-23）：「主角可见」的**安全边距**（占半屏的比例）。
        /// 机位可以为了藏住虚空而离开焦点，但**不得**超过 `半屏 × 本值` ⇒ 焦点（玩家）永远落在
        /// 视口 [inset, 1−inset] 内（inset = (1 − 本值)/2），⛔ 不许被顶到画面边框上。
        /// <para>取值被两侧夹死：① 下界 —— 血腥荒野落点格 (1,20) 需要 `6.083 / 6.667 = 0.9125`
        /// 个半屏位移才能把虚空压到 0（`mapcheck` §29 `fracProd == 0` + 生产==规格），本值 &lt; 0.9125
        /// 会让 §29 变红；② 上界 —— `playercheck` §11.10 四角用例的「图外格量不差于修前」在本值 = 1
        /// 时恰好取等，&gt; 1 没有意义（会允许焦点落到画面外）。⇒ **只能取 1**。</para>
        /// </summary>
        public const float FocusSafeMarginRatio = 1f;

        /// <summary>"主角可见优先"生效过（只报一次，避免每帧刷屏）。</summary>
        private static bool _visibilityWonLogged;

        /// <summary>
        /// 把机位夹到「可见**格**矩形 ⊆ 地图」，并保证**焦点（玩家）仍在视口安全边距内**。
        /// <para>★ 口径（camera-follow 片）：夹的是**机位** `camera`，⛔ 不是焦点 ——
        /// 焦点只作为「机位最多能挪多远」的参照。前一片把两者当同一个量（`camera == focus`），
        /// 于是「藏虚空」的位移被当成「焦点被夹」⇒ 玩家被顶到画面角落（实机 p50 598px）。</para>
        /// </summary>
        /// <param name="camera">**机位**世界坐标（被夹的那个量）。</param>
        /// <param name="focus">**焦点**（玩家）世界坐标 —— 只用于限定位移上限，本身不被改写。</param>
        /// <param name="mapWidth">地图宽（格）。</param>
        /// <param name="mapHeight">地图高（格）。</param>
        /// <param name="halfW">半屏宽（世界单位 = `orthoSize * aspect`）。</param>
        /// <param name="halfH">半屏高（世界单位 = `orthoSize`）。</param>
        /// <returns>夹制后的机位世界坐标（xy）。</returns>
        public static Vector2 ClampCameraGrid(Vector2 camera, Vector2 focus, int mapWidth, int mapHeight,
            float halfW, float halfH)
        {
            var iw = Iso.HalfW;                       // 等距半格（世界单位）：1.0 / 0.5
            var ih = Iso.HalfH;
            if (mapWidth <= 0 || mapHeight <= 0 || halfW <= 0f || halfH <= 0f || iw <= 0f || ih <= 0f)
                return camera;

            var a = halfW / iw;                       // 半屏在 u 方向的半跨（格）
            var b = halfH / ih;                       // 半屏在 v 方向的半跨（格）
            var margin = a + b;                       // 菱形顶点到中心在两个方向上的合计跨度

            var u = camera.x / iw;                    // **机位**在菱形轴系里的坐标
            var v = -camera.y / ih;

            // ★ camera-follow 片：上界用 **2·W / 2·H**（格的连续口径），⛔ 不是 2·(W−1)。
            //   `Iso` 的连续格坐标里「格 g 覆盖 [g, g+1)」⇒ 地图的真实连续范围是 [0, W]×[0, H]；
            //   取 (W−1) 等于把最外一圈格当成图外，**白白吃掉一整格**可跟随范围
            //   （56×40 城镇：机位 gy 上限 31.917 → 32.917 ⇒ 玩家↔机位偏移少 1 格 ≈ 144px@1080p）。
            var s1 = ClampSpan(u + v, margin, 2f * mapWidth - margin);          // = 2·gx
            var s2 = ClampSpan(v - u, margin, 2f * mapHeight - margin);         // = 2·gy

            var u2 = (s1 - s2) * 0.5f;
            var v2 = (s1 + s2) * 0.5f;

            // ★ 主角可见预算：位移是相对**焦点**（玩家）量的，⛔ 不是相对机位自己
            //   （相对机位自己量的话，机位已经在边缘上 ⇒ 预算被自己吃掉，玩家一路被顶到画面角上）。
            //   上界 = 半屏 × FocusSafeMarginRatio ⇒ 焦点永远落在视口 [inset, 1−inset] 内。
            var uf = focus.x / iw;
            var vf = -focus.y / ih;
            var au = a * FocusSafeMarginRatio;
            var bv = b * FocusSafeMarginRatio;
            var uc = Mathf.Clamp(u2, uf - au, uf + au);
            var vc = Mathf.Clamp(v2, vf - bv, vf + bv);
            if (Mathf.Abs(uc - u2) > 1e-4f || Mathf.Abs(vc - v2) > 1e-4f)
            {
                if (!_visibilityWonLogged)
                {
                    _visibilityWonLogged = true;
                    Log.Warn("Camera", $"地图角格处「零虚空」与「主角可见（安全边距 {FocusSafeMarginRatio:0.##}）」" +
                                       $"不可兼得 ⇒ 本次让位给主角可见（机位 ({uc * iw:0.##},{-vc * ih:0.##})，" +
                                       $"零虚空解为 ({u2 * iw:0.##},{-v2 * ih:0.##})，焦点 ({focus.x:0.##},{focus.y:0.##})，只报一次）");
                }
                u2 = uc;
                v2 = vc;
            }

            return new Vector2(u2 * iw, -v2 * ih);
        }

        /// <summary>
        /// 兼容口径：**机位与焦点同一处**（= 相机想停在玩家身上的理想情形）。
        /// `mapcheck` §29 / `playercheck` §11 的用例走这个入口（它们只给一个点）。
        /// </summary>
        public static Vector2 ClampFocusGrid(Vector2 focus, int mapWidth, int mapHeight, float halfW, float halfH)
        {
            return ClampCameraGrid(focus, focus, mapWidth, mapHeight, halfW, halfH);
        }

        /// <summary>纯函数：把 [lo,hi] 这段可行区间夹出来；区间为空（视野比地图还大）⇒ 居中（原版语义）。</summary>
        private static float ClampSpan(float s, float lo, float hi)
        {
            if (hi <= lo) return (lo + hi) * 0.5f;
            return Mathf.Clamp(s, lo, hi);
        }
    }
}
