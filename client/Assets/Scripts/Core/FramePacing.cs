// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · Core/FramePacing.cs  ★★ 本项目新增 ★★
// **帧节奏的唯一调用点**（`Application.targetFrameRate` / `QualitySettings.vSyncCount`）。
//
// ★ 本片（镜头/帧节奏下沉）：**机制已下沉到引擎** `CloverEngine.FramePacingPolicy`
//   (`clover-client-unity-engine/Runtime/Presentation/FramePacing.cs`)。
//   本文件只剩下「业务口径 + 日志文案 + 只报一次」这三件业务侧的事：
//     · 口径常量（60 / 0）与"为什么是 60"的论据；
//     · 留痕（成功一条 Info、失败/未生效各一条 Warn，均只报一次）；
//     · `Describe(...)` 的**业务尾注**（动画复位口径 / 移动积分口径）。
//   ⛔ 本文件**不再**出现 `Application.targetFrameRate =` / `QualitySettings.vSyncCount =`
//      这种原生写入 —— 全工程唯一写点 = `CloverEngine.FramePacingPolicy.Pin`。
//
// ★ R1-D（用户投诉「人物移动抖动」的候选①，已定根因）：
//   全工程**从未**设过 `Application.targetFrameRate`（引擎里唯一写点是
//   `Runtime/Presentation/Quality.cs:179-181`，由 `Game.Quality.SetLevel` 触发；
//   而本项目**从不**调 `Game.Quality` ⇒ grep 0 命中）⇒ 帧率只由 `QualitySettings.vSyncCount`
//   决定，而它在 `client/ProjectSettings/QualitySettings.asset` 里**逐档不同**：
//     Very Low / Low / Ultra = 0（= 不封顶，dt 随机抖动）；Medium / High / Very High = 1（= 垂直同步）。
//   本项目把选项 `LOW/MED/HIGH` 映射到引擎档位 0/1/2（`UI/SettingsPanel.QualityLabels` +
//   `App/Bootstrap.MaxQualityLevel`）⇒ **选 LOW/MED 时帧间隔无上限且随机抖动**，选 HIGH 时又被
//   垂直同步接管 —— 同一份移动代码，帧节奏随"画质档位"变，观感就是"人物移动发抖/一顿一顿"。
//
// **口径（确定性，与画质档位无关）**：`targetFrameRate = 60` + `vSyncCount = 0`。
//   · 为什么是 60 而不是原版 25：原版 25fps 是**逻辑帧**；本项目逻辑早已按 `dt` 积分
//     （帧率无关 ⇒ 移动速度/动画相位与帧率解耦，断言见 `tools/probes/hosts/playercheck` §15 b
//     "变 dt 走同一条路径 ⇒ 终点逐格一致"）⇒ 帧率只影响采样密度与平滑度，取 60 抖动最小、
//     且与显示刷新率解耦（vSync=0）。
//   · ⛔ 本类**只**写帧节奏：画质内容（阴影 / 分辨率缩放 / LOD / 贴图限制）**一律不碰**。
//
// ⛔ `QualitySettings.SetQualityLevel` 会**把 vSyncCount 按档位重置**（Unity 语义），所以
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

        /// <summary>帧率上限（恒定，与画质档位无关）。= 引擎建议值，见文件头"为什么是 60"。</summary>
        public const int TargetFrameRate = CloverEngine.FramePacingPolicy.DefaultTargetFrameRate;

        /// <summary>垂直同步档位（0 = 关；关掉才能让 <see cref="TargetFrameRate"/> 真的生效）。</summary>
        public const int VSyncCount = CloverEngine.FramePacingPolicy.DefaultVSyncCount;

        /// <summary>「已生效」那条只报一次（`Bootstrap.ResetStaticsForNewPlaySession` 会复位它）。</summary>
        private static bool _logged;

        /// <summary>「未生效 / 设置失败」那条只报一次（同上）。</summary>
        private static bool _warned;

        /// <summary>★ 每局 Play 复位"只报一次"记录（工程若开了「不重载域」，`static` 会跨局残留）。</summary>
        public static void ResetStaticsForNewPlaySession()
        {
            _logged = false;
            _warned = false;
        }

        /// <summary>
        /// 把帧节奏钉死（幂等）。`reason` 只进日志（"启动" / "选项面板应用画质档位 2" / 自检宿主）。
        /// <para>**机制全部在引擎**（`CloverEngine.FramePacingPolicy.Pin` / `TryRead`）：本方法只做
        /// 「读改前值 → 让引擎写 → 按引擎的返回值分流日志」。**任何非预期分支都留痕**：
        /// 原生 API 抛异常（离线自检宿主属正常现象）⇒ Warn；读回值不等于目标值 ⇒ Warn；
        /// 成功 ⇒ 首次（或"确实被改回去过"时）报一条 Info。各只报一次。</para>
        /// </summary>
        public static void Pin(string reason)
        {
            int beforeFps, beforeVsync;
            string readError;
            if (!CloverEngine.FramePacingPolicy.TryRead(out beforeFps, out beforeVsync, out readError))
            {
                // 非预期分支（离线宿主走这一条）：读不成 ⇒ 改前值无从得知，用占位值并如实写进日志
                beforeFps = 0;
                beforeVsync = 0;
            }

            int fps, vsync;
            string pinError;
            var ok = CloverEngine.FramePacingPolicy.Pin(TargetFrameRate, VSyncCount,
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

            if (fps != TargetFrameRate || vsync != VSyncCount)
            {
                if (_warned) return;
                _warned = true;
                Game.Logger?.Warn(Tag,
                    $"[R1-D] 帧节奏未生效：期望 targetFrameRate={TargetFrameRate} vSyncCount={VSyncCount}，" +
                    $"读回 targetFrameRate={fps} vSyncCount={vsync}（原生 API 不可用 / 被平台改写？只报一次）");
                return;
            }

            // 只报一次；之后只在"确实被改回去过"（画质档位变更把 vSync 重置了）时再报一条
            if (_logged && beforeFps == TargetFrameRate && beforeVsync == VSyncCount) return;
            _logged = true;

            Game.Logger?.Info(Tag, Describe(reason, beforeFps, beforeVsync));
        }

        /// <summary>
        /// 「生效口径」的**单行文本**（被 <see cref="Pin"/> 打进日志；离线自检宿主
        /// `tools/probes/hosts/playercheck` §15 e3 直接断言它的用词 —— 保证口径不会被改日志时丢掉）。
        /// <para>前半段 = 引擎件 <see cref="CloverEngine.FramePacingPolicy.Describe"/>（机制口径）；
        /// 后半段 = 本项目的**业务尾注**（旧口径的代价 + 动画复位口径 + 移动积分口径）。</para>
        /// </summary>
        /// <param name="reason">为什么重钉（"启动" / "选项面板应用画质档位 2" / 宿主自检…）。</param>
        /// <param name="beforeFps">改前的 `targetFrameRate`。</param>
        /// <param name="beforeVsync">改前的 `vSyncCount`。</param>
        public static string Describe(string reason, int beforeFps, int beforeVsync)
        {
            return "[R1-D] " + CloverEngine.FramePacingPolicy.Describe(TargetFrameRate, VSyncCount, reason,
                       beforeFps, beforeVsync) +
                   "（旧口径：LOW/MED ⇒ vSync=0 不封顶 ⇒ dt 抖动、HIGH ⇒ vSync=1）。" +
                   "动画复位口径 = `Module/View/SpriteAnimator.Play` 早退判据（同动作 + 同帧键 + 有帧 + 未播完 ⇒ 原样返回、" +
                   "**不重置帧号进度**）；换朝向/换动作时帧键整组换掉 ⇒ 从第 0 帧起（原版 8 方向各一套 .cof，必须保留）。" +
                   "移动积分口径见 `Module/Player/PlayerMotor.LogBudgetOnce` 那条 [R1-D]（每帧位移 ≤ speed×dt）。";
        }
    }
}
