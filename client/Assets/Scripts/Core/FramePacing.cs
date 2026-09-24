// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · Core/FramePacing.cs  本项目新增
// **帧节奏的唯一调用点**（`Application.targetFrameRate` / `QualitySettings.vSyncCount`）。
//
//   (`clover-client-unity-engine/Runtime/Presentation/FramePacing.cs`)。
//   本文件只剩下「业务口径 + 日志文案 + 只报一次」这三件业务侧的事：
//     · 口径常量（60 / 0）与"为什么是 60"的论据；
//     · 留痕（成功一条 Info、失败/未生效各一条 Warn，均只报一次）；
//     · `Describe(...)` 的**业务尾注**（动画复位口径 / 移动积分口径）。
//   本文件**不再**出现 `Application.targetFrameRate =` / `QualitySettings.vSyncCount =`
//      这种原生写入 —— 全工程唯一写点 = `CloverEngine.FramePacingPolicy.Pin`。
//
//   全工程**从未**设过 `Application.targetFrameRate`（引擎里唯一写点是
//   `Runtime/Presentation/Quality.cs:179-181`，由 `Game.Quality.SetLevel` 触发；
//   而本项目**从不**调 `Game.Quality` ⇒ grep 0 命中）⇒ 帧率只由 `QualitySettings.vSyncCount`
//   决定，而它在 `client/ProjectSettings/QualitySettings.asset` 里**逐档不同**：
//     Very Low / Low / Ultra = 0（= 不封顶，dt 随机抖动）；Medium / High / Very High = 1（= 垂直同步）。
//   本项目把选项 `LOW/MED/HIGH` 映射到引擎档位 0/1/2（`UI/SettingsPanel.QualityLabels` +
//   `App/Bootstrap.MaxQualityLevel`）⇒ **选 LOW/MED 时帧间隔无上限且随机抖动**，选 HIGH 时又被
//   垂直同步接管 —— 同一份移动代码，帧节奏随"画质档位"变，观感就是"人物移动发抖/一顿一顿"。
//
// **口径（确定性，与画质档位无关）**：U27 起 = **引擎权威推荐**：
//   · 刷新率**可读** ⇒ `vSyncCount = 1`（`FramePacingPolicy.RecommendVSyncCount`）：帧交付锁到
//     刷新率 ⇒ **帧间隔恒定** ⇒ 世界滚动/角色位移的每帧推进量恒定。为什么必须这样：位置是 `f(t)`
//     的光滑函数（按 `dt` 积分）⇒ 每帧推进量 = `v·dt`，**帧间隔不匀就直接变成画面推进不匀**。
//     `dt` 8.5~62.6ms（sd 5.0ms）、相机纵向每帧推进 sd **2.97px**、`corr(纵向偏差, dt−均值)=0.923`
//     ⇒ 不匀就是帧时间造成的（不是相机代码：跟随链路横向 sd 只有 0.19px）；
//     且 60fps 落在 100Hz 上每帧占 1.67 个刷新周期 ⇒ 呈现节拍本身也不整。
//   · 刷新率**读不到**（离线宿主 / 无头 / 平台不提供）⇒ **兜底口径** `targetFrameRate = 60` +
//     `vSyncCount = 0`（= `TargetFrameRate` / `VSyncCount` 两个常量，不许删）。
//   · 为什么兜底是 60 而不是原版 25：原版 25fps 是**逻辑帧**；本项目逻辑早已按 `dt` 积分
//   · 本类**只**写帧节奏：画质内容（阴影 / 分辨率缩放 / LOD / 贴图限制）**一律不碰**。
//
// `QualitySettings.SetQualityLevel` 会**把 vSyncCount 按档位重置**（Unity 语义），所以
//   **每次改画质档位之后**都要重新 `Pin` 一次（`UI/SettingsPanel.ApplyQualityToEngine` 已接）。
// ─────────────────────────────────────────────────────────────────────────────

using System;
using CloverEngine;
using UnityEngine;

namespace Diablo2.Core
{
    /// <summary>帧节奏（帧率上限 / 垂直同步）的业务口径 + 留痕 —— 机制走 <see cref="CloverEngine.FramePacingPolicy"/>。见文件头。</summary>
    public static class FramePacing
    {
        /// <summary>日志 tag（`Core/Log.KnownTags` 白名单内的模块名）。</summary>
        public const string Tag = "App";

        /// <summary>
        /// **兜底**帧率上限（与画质档位无关）= 引擎兜底值，见文件头"为什么是 60"。
        /// <para>**实际档位由 <see cref="Pin"/> 现取**
        /// （<see cref="CloverEngine.FramePacingPolicy.Recommend"/>）；只有刷新率**读不到**
        /// （离线宿主 / 无头 / 平台不提供）时才落到本值。</para>
        /// </summary>
        public const int TargetFrameRate = CloverEngine.FramePacingPolicy.DefaultTargetFrameRate;

        /// <summary>
        /// **兜底**垂直同步档位（0 = 关）。刷新率读不到时用它退回"帧率上限"口径。
        /// <para>刷新率**可读**时 <see cref="Pin"/> 用的是
        /// <see cref="CloverEngine.FramePacingPolicy.RecommendVSyncCount(float)"/>（= 1）而不是本值
        /// —— 本值只在读不到刷新率时生效。两个常量因此仍是"**兜底口径**"，不是"实际口径"，
        /// 判据（`tools/probes/hosts/playercheck` §15 e1/e3）判的正是这条兜底链。</para>
        /// </summary>
        public const int VSyncCount = CloverEngine.FramePacingPolicy.DefaultVSyncCount;

        /// <summary>「已生效」那条只报一次（`Bootstrap.ResetStaticsForNewPlaySession` 会复位它）。</summary>
        private static bool _logged;

        /// <summary>「未生效 / 设置失败」那条只报一次（同上）。</summary>
        private static bool _warned;

        /// <summary>每局 Play 复位"只报一次"记录（工程若开了「不重载域」，`static` 会跨局残留）。</summary>
        public static void ResetStaticsForNewPlaySession()
        {
            _logged = false;
            _warned = false;
        }

        /// <summary>
        /// 把帧节奏钉死（幂等）。`reason` 只进日志（"启动" / "选项面板应用画质档位 2" / 自检宿主）。
        /// <para>**机制全部在引擎**（`CloverEngine.FramePacingPolicy.Pin` / `TryRead`）：本方法只做
        /// 原生 API 抛异常（离线自检宿主属正常现象）⇒ Warn；读回值不等于目标值 ⇒ Warn；
        /// 成功 ⇒ 首次（或"确实被改回去过"时）报一条 Info。各只报一次。</para>
        /// </summary>
        public static void Pin(string reason)
        {
            int beforeFps, beforeVsync;
            string readError;
            if (!CloverEngine.FramePacingPolicy.TryRead(out beforeFps, out beforeVsync, out readError))
            {
                beforeFps = 0;
                beforeVsync = 0;
            }

            // U27：**档位由引擎权威给** —— `Recommend` = 刷新率可读 ⇒ vSyncCount=1（帧交付锁到
            //   刷新率，帧间隔恒定）；刷新率读不到（离线宿主 / 无头 / 平台不提供）⇒ **兜底口径**
            //   `TargetFrameRate`(60) + `VSyncCount`(0)。两条都在，不许把兜底删掉。
            int wantFps, wantVsync;
            float refreshHz;
            var refreshReadable = CloverEngine.FramePacingPolicy.Recommend(out wantFps, out wantVsync, out refreshHz);

            int fps, vsync;
            string pinError;
            var ok = CloverEngine.FramePacingPolicy.Pin(wantFps, wantVsync,
                out fps, out vsync, out pinError);

            if (!ok && pinError != null)
            {
                if (_warned) return;
                _warned = true;
                Game.Logger?.Warn(Tag,
                    $"[R1-D] 帧节奏设置失败（{pinError}）⇒ 保持引擎默认帧节奏" +
                    "（离线自检宿主走这一条属正常现象；Unity 里出现请看这条日志；只报一次）");
                return;
            }

            if (fps != wantFps || vsync != wantVsync)
            {
                if (_warned) return;
                _warned = true;
                Game.Logger?.Warn(Tag,
                    $"[R1-D] 帧节奏未生效：期望 targetFrameRate={wantFps} vSyncCount={wantVsync}，" +
                    $"读回 targetFrameRate={fps} vSyncCount={vsync}（原生 API 不可用 / 被平台改写？只报一次）");
                return;
            }

            // 只报一次；之后只在"确实被改回去过"（画质档位变更把 vSync 重置了）时再报一条
            if (_logged && beforeFps == wantFps && beforeVsync == wantVsync) return;
            _logged = true;

            Game.Logger?.Info(Tag, Describe(reason, wantFps, wantVsync, beforeFps, beforeVsync,
                refreshHz, refreshReadable));
        }

        /// <summary>
        /// 「生效口径」的**单行文本**（被 <see cref="Pin"/> 打进日志）。
        /// <para>前半段 = 引擎件 <see cref="CloverEngine.FramePacingPolicy.Describe"/>（机制口径）；
        /// 后半段 = 本项目的**业务尾注**（档位来源 + 动画复位口径 + 移动积分口径）。</para>
        /// </summary>
        /// <param name="reason">为什么重钉（"启动" / "选项面板应用画质档位 2" / 宿主自检…）。</param>
        public static string Describe(string reason, int beforeFps, int beforeVsync)
            => Describe(reason, TargetFrameRate, VSyncCount, beforeFps, beforeVsync, 0f, false);

        /// <summary>
        /// **实际生效档位**的口径文本（<see cref="Pin"/> 打的就是这一条）。
        /// <para><paramref name="refreshReadable"/> = false ⇒ 本行说明走的是**兜底口径**
        /// （刷新率读不到）；true ⇒ 说明档位取自 <see cref="CloverEngine.FramePacingPolicy.Recommend"/>
        /// 且把读到的刷新率一起写出来（"为什么 vSync=1"当场可查）。</para>
        /// </summary>
        /// <param name="reason">为什么重钉（"启动" / "选项面板应用画质档位 2" / 宿主自检…）。</param>
        /// <param name="fps">实际写下去的帧率上限。</param>
        /// <param name="vSync">实际写下去的垂直同步档位。</param>
        /// <param name="refreshHz">读到的显示器刷新率（读不到为 0）。</param>
        /// <param name="refreshReadable">刷新率是否可读（决定本行的"档位来源"措辞）。</param>
        public static string Describe(string reason, int fps, int vSync, int beforeFps, int beforeVsync,
            float refreshHz, bool refreshReadable)
        {
            var source = refreshReadable
                ? $"（档位来源 = 引擎 `FramePacingPolicy.Recommend()`：显示器 {refreshHz:0.##}Hz 可读 ⇒ vSyncCount={vSync}，" +
                  "帧交付锁到刷新率 ⇒ 帧间隔恒定；此时平台忽略 targetFrameRate 属预期）"
                : $"（档位来源 = **兜底口径**：刷新率读不到 ⇒ targetFrameRate={fps} vSyncCount={vSync}）";

            return "[R1-D] " + CloverEngine.FramePacingPolicy.Describe(fps, vSync, reason,
                       beforeFps, beforeVsync) + source +
                   "（旧口径：LOW/MED ⇒ vSync=0 不封顶 ⇒ dt 抖动、HIGH ⇒ vSync=1）。" +
                   "动画复位口径 = `Module/View/SpriteAnimator.Play` 早退判据（同动作 + 同帧键 + 有帧 + 未播完 ⇒ 原样返回、" +
                   "**不重置帧号进度**）；换朝向/换动作时帧键整组换掉 ⇒ 从第 0 帧起（原版 8 方向各一套 .cof，必须保留）。" +
                   "移动积分口径见 `Module/Player/PlayerMotor.LogBudgetOnce` 那条 [R1-D]（每帧位移 ≤ speed×dt）。";
        }
    }
}
