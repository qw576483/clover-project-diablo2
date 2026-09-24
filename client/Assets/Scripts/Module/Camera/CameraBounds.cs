// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · Module/Camera/CameraBounds.cs
// 「相机边界夹制」的**项目门面**：只持有**项目取值**（`FocusSafeMarginRatio`）与
// 「半格宽高 = `Iso.HalfW/HalfH`」这两项参数，算法本体**已下沉引擎**
//
// 为什么本文件仍然存在（不是多余的一层）：
//   · `FocusSafeMarginRatio` 是**项目口径的常量**（原版 D2 的"主角必须留在视口内"），
//     引擎不替业务定这个值 ⇒ 它留在项目，由本文件传给引擎件；
//   · 半格宽高来自 `GameConst`（经 `Iso.HalfW/HalfH`），同样不进引擎。
//   ⇒ 公开签名与调用点（`CameraRig.CameraPosForFocus/CameraPosForCamera`、
//
// 本文件**不含任何算法**：所有夹制只在引擎 `CameraBoundsKit`（同一份 = 离线宿主直接链的那份）。
//    （非预期分支"地图角格处让位给主角可见"的日志出口一并移到引擎
//     `LogThrottle.WarnOnce("Camera","cameraBounds.visibilityWon")`）。
//
//   **实测现象**（bw_deep_bwy1.txt）：进血腥荒野后机位 (-19,-11,-10)，chunk 侧 `MISSING=0`
//   却仍有一大片连续黑区，像素量法 blackFrac=0.340（两个独立量法互相印证）。
//   （80×80 地图 ⇒ 158×79 世界单位），`ClampFocus` 只把焦点夹进「AABB − 半屏」。
//   菱形与它的 AABB 之间那**四个三角区**在世界里根本没有对应格子（= 地图外虚空），
//   而 AABB 夹制把它们当成"图内"⇒ 机位落在 AABB 内即被放行（实测该机位就是 no-op）
//   ⇒ 屏幕上 31.3% 是地图外虚空 = 那片黑。
//
// ── 一条**硬约束**：主角必须留在视口内 ──────────────────────────────────────
//   在地图**角格**上「零虚空」与「主角可见」数学上不可兼得（证明见引擎件文件头）⇒
//   此时**让位给主角可见**（地图角落那点虚空由地图边界块的美术去盖，属 `Module/Map` 侧）。
// ─────────────────────────────────────────────────────────────────────────────

using CloverEngine;
using Diablo2.Core;
using UnityEngine;

// 命名空间与 `CameraRig.cs` 一致（`Diablo2.Module`，**不是** `Diablo2.Module.Camera`）：
//   后者会让 `Module/Map/MapView.cs` 里的裸 `Camera` 解析成命名空间（CS0118，本机真报过）。
namespace Diablo2.Module
{
    /// <summary>
    /// 相机边界夹制的**项目门面**（格空间，不是世界 AABB）：实现 = 引擎
    /// <see cref="CameraBoundsKit"/>，本类只把两个项目参数喂进去
    /// （<see cref="FocusSafeMarginRatio"/> 与 <see cref="Iso.HalfW"/> / <see cref="Iso.HalfH"/>）。
    /// </summary>
    public static class CameraBounds
    {
        /// <summary>
        /// 机位可以为了藏住虚空而离开焦点，但**不得**超过 `半屏 × 本值` ⇒ 焦点（玩家）永远落在
        /// 视口 [inset, 1−inset] 内（inset = (1 − 本值)/2），不许被顶到画面边框上。
        /// <para>取值被两侧夹死：① 下界 —— 血腥荒野落点格 (1,20) 需要 `6.083 / 6.667 = 0.9125`
        /// 时恰好取等，&gt; 1 没有意义（会允许焦点落到画面外）。⇒ **只能取 1**。</para>
        /// <para>eng-camera-math 片：本常量**留在项目**（引擎不替业务定这个值），
        /// 由本类的转发传给引擎件。</para>
        /// </summary>
        public const float FocusSafeMarginRatio = 1f;

        /// <summary>
        /// 把机位夹到「可见**格**矩形 ⊆ 地图」，并保证**焦点（玩家）仍在视口安全边距内**。
        /// <para>eng-camera-math 片：**实现已下沉引擎** <see cref="CameraBoundsKit.ClampCameraGrid"/>
        /// （逐行同源），本方法是薄转发 —— 签名与调用点一字未改；两个项目参数在此处喂进去。</para>
        /// <para>口径（camera-follow 片）：夹的是**机位** `camera`，不是焦点 ——
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
            return CameraBoundsKit.ClampCameraGrid(camera, focus, mapWidth, mapHeight,
                halfW, halfH, Iso.HalfW, Iso.HalfH, FocusSafeMarginRatio);
        }

        /// <summary>
        /// 兼容口径：**机位与焦点同一处**（= 相机想停在玩家身上的理想情形）。
        /// <para>eng-camera-math 片：**实现已下沉引擎**（薄转发）。</para>
        /// </summary>
        public static Vector2 ClampFocusGrid(Vector2 focus, int mapWidth, int mapHeight, float halfW, float halfH)
        {
            return CameraBoundsKit.ClampFocusGrid(focus, mapWidth, mapHeight,
                halfW, halfH, Iso.HalfW, Iso.HalfH, FocusSafeMarginRatio);
        }
    }
}
