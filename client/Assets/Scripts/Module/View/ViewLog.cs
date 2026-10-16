// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · Module/View/ViewLog.cs
// 精灵视图模块的**统一日志入口**（tag 固定 = `View`，在 `Core/Log.cs` 的白名单内）。
//
//   闸门 = `CloverEngine.LogThrottle`，本文件**不自持任何 `HashSet` / `Dictionary`**：
//      · `WarnOnce`      ⇒ `LogThrottle.ShouldLog(key, float.PositiveInfinity)`（时间口径的"只报一次"）；
//      · `WarnThrottled` ⇒ `LogThrottle.ShouldLogEvery(key, ThrottleEveryN)`（**计数口径**的无时钟降频）。
//    `LogThrottle` 的三级时钟**永不抛异常**：非 Unity 进程（`tools/probes/hosts/*` 离线自检宿主）
//    首次探测失败即自动降级到进程单调时钟，故本层可以直接委托。
//    需要**确定性**计时（离线断言逐值可复现）时注入 `Log.Clock`（它转发到 `LogThrottle.Clock`）；
//    本层**不**自行改全局时钟（那会劫持宿主/其他模块的注入）。
//
// 本层口径（与 Combat/Skill/Monster/Audio 那 4 份一致）：
//    ① 计数间隔 = 第 1 次 + 之后每 100 次一条；`ShouldLogEvery` 只有单一 `everyN`，
//       故不含"第 10 次"那条（要更密把该常量改成 10，代价是每百次多 8 行）。
//    ② 不追加行尾后缀：计数闸门**不向调用方暴露 N**。
//       （`*Counted` 那组会补 `（同类第 N 次）`，但它直发 `Game.Logger`、绕过本项目 `Log` 门面的
//        tag 规范化与静默开关 ⇒ **不用**；全项目无判据依赖该后缀。）
//    ③ key 加模块前缀 `View/`：引擎的限频表是**全局一张** ⇒ 加前缀防止跨模块同名 key 串味
//       （对调用方不可见，key 从不进日志）。
//    ④ `ResetThrottle()` 只提供**整体**清空（时间口径 + 计数口径一起清），没有"按模块清"。
//       **无玩家可见影响**：唯一的调用点 `ViewModule.Clear()`（离场/复位）本来就是"让只报一次的日志重新
//       生效"，被一并清掉的只是其他模块的**日志密度**（它们各自的"只报一次"会重新报一次），
//       不触碰任何游戏状态 / 数值 / 存档；且 Clear() 只在场景切换时走到。
//    输出**一律走本层 `Log.Warn` / `Log.Error`**：tag 规范化 + 项目静默开关在那一层。
//    空 key 的处理：不输出，且不与其他调用方合并计数（引擎计数口径会把空 key 归并成 `"default"`）。
// 禁止裸 `Debug.Log`。
// ─────────────────────────────────────────────────────────────────────────────

using System;
using CloverEngine;
using Diablo2.Core;

namespace Diablo2.Module.View
{
    /// <summary>视图模块日志门面（tag = <see cref="Tag"/>）。</summary>
    public static class ViewLog
    {
        /// <summary>模块日志 tag。</summary>
        public const string Tag = "View";

        /// <summary>
        /// 计数闸门（<see cref="LogThrottle.ShouldLogEvery"/>）的间隔：第 1 次 + 之后每 100 次一条
        /// （引擎只支持单一 `everyN` ⇒ 取 100）。
        /// </summary>
        private const int ThrottleEveryN = 100;

        /// <summary>本模块在引擎全局限频表里的 key 前缀（引擎是一张全局表 ⇒ 防跨模块撞 key）。</summary>
        private const string KeyPrefix = "View/";

        /// <summary>信息级。</summary>
        public static void Info(string msg) => Log.Info(Tag, msg);

        /// <summary>警告级。</summary>
        public static void Warn(string msg) => Log.Warn(Tag, msg);

        /// <summary>错误级。</summary>
        public static void Error(string msg, Exception ex = null) => Log.Error(Tag, msg, ex);

        /// <summary>只报一次（同一 key 整个进程一条）。闸门 = 引擎 <see cref="LogThrottle.ShouldLog"/>。</summary>
        public static bool WarnOnce(string key, string msg)
        {
            if (string.IsNullOrEmpty(key))
            {
                Log.Warn(Tag, "ViewLog.WarnOnce 收到空 key ⇒ 不输出（避免自刷屏）");
                return false;
            }
            if (!LogThrottle.ShouldLog(KeyPrefix + key, float.PositiveInfinity)) return false;
            Log.Warn(Tag, msg);
            return true;
        }

        /// <summary>
        /// 无时钟降频：第 1 次 + 之后每 <see cref="ThrottleEveryN"/> 次一条
        /// （闸门 = <see cref="LogThrottle.ShouldLogEvery"/>，判定只用计数、不用计时值）。
        /// </summary>
        public static bool WarnThrottled(string key, string msg)
        {
            if (string.IsNullOrEmpty(key))
            {
                Log.Warn(Tag, "ViewLog.WarnThrottled 收到空 key ⇒ 不输出（避免自刷屏）");
                return false;
            }
            if (!LogThrottle.ShouldLogEvery(KeyPrefix + key, ThrottleEveryN)) return false;
            Log.Warn(Tag, msg);
            return true;
        }

        /// <summary>
        /// 清空降频 / 只报一次记录（离场 / 复位时调用，见 <c>ViewModule.Clear</c>）。
        /// <para>引擎只提供**整体**清空（<see cref="LogThrottle.Reset"/>），没有"按模块清"的入口
        /// ⇒ 其他模块（含 `Core/Log`）的限频记录会被一并清掉，它们各自的"只报一次"重新生效一次。        /// **无玩家可见影响**：只影响日志密度，不触碰游戏状态 / 数值 / 存档。</para>
        /// </summary>
        public static void ResetThrottle() => LogThrottle.Reset();
    }
}
