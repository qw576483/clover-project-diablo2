// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · Module/Camera/CameraBounds.cs
// 「相机边界夹制」的**项目门面**：只持有**项目取值**（`FocusSafeMarginRatio`）与
// 「半格宽高 = `Iso.HalfW/HalfH`」这两项参数；算法本体在引擎 `CameraBoundsKit`。
//
// 为什么本文件仍然存在（不是多余的一层）：
//   · `FocusSafeMarginRatio` 是**项目口径的常量**（原版 D2 的"主角必须留在视口内"），
//     引擎不替业务定这个值 ⇒ 它留在项目，由本文件传给引擎件；
//   · 半格宽高来自 `GameConst`（经 `Iso.HalfW/HalfH`），同样不进引擎。
//   ⇒ 公开签名与调用点（`CameraRig.CameraPosForFocus/CameraPosForCamera`）保持不变。
//
// 本文件**不含任何算法**：所有夹制只在引擎 `CameraBoundsKit`（同一份 = 离线宿主直接链的那份）；
//   非预期分支"地图角格处让位给主角可见"的日志出口也在引擎
//   `LogThrottle.WarnOnce("Camera","cameraBounds.visibilityWon")`。
//
// 夹制在**格空间**做，不用世界 AABB：地图菱形与其 AABB 之间有**四个三角区**在世界里根本没有
//   对应格子（= 地图外虚空），AABB 夹制会把它们当成"图内"放行 ⇒ 屏幕上出现成片地图外虚空。
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
        /// <para>本常量**留在项目**：引擎不替业务定这个值，由本类的转发传给引擎件。</para>
        /// </summary>
        public const float FocusSafeMarginRatio = 1f;

        /// <summary>
        /// 把机位夹到「可见**格**矩形 ⊆ 地图」，并保证**焦点（玩家）仍在视口安全边距内**。
        /// <para>算法在引擎 <see cref="CameraBoundsKit.ClampCameraGrid"/>；两个项目参数
        /// （<see cref="Iso.HalfW"/> / <see cref="Iso.HalfH"/> / <see cref="FocusSafeMarginRatio"/>）在此处喂进去。</para>
        /// <para>夹的是**机位** `camera`，不是焦点 ——
        /// 焦点只作为「机位最多能挪多远」的参照，本身不被改写。</para>
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
        /// <para>算法在引擎 <see cref="CameraBoundsKit.ClampFocusGrid"/>。</para>
        /// </summary>
        public static Vector2 ClampFocusGrid(Vector2 focus, int mapWidth, int mapHeight, float halfW, float halfH)
        {
            return CameraBoundsKit.ClampFocusGrid(focus, mapWidth, mapHeight,
                halfW, halfH, Iso.HalfW, Iso.HalfH, FocusSafeMarginRatio);
        }
    }
}
