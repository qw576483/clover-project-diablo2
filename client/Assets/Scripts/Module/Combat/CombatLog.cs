// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · Module/Combat/CombatLog.cs
// 战斗子系统（Combat + 与之共用伤害管线的 Monster/Skill/View）的**统一日志入口**，
// tag 固定 = `Combat`（`Core/Log.cs` 的 `KnownTags` 白名单内）。
//
//    全部交给 `CloverEngine.LogThrottle`，本文件**不自持任何 `HashSet` / `Dictionary`**：
//      · `WarnOnce`      ⇒ `LogThrottle.ShouldLog(key, float.PositiveInfinity)`（时间口径的"只报一次"）；
//      · `WarnThrottled` ⇒ `LogThrottle.ShouldLogEvery(key, ThrottleEveryN)`（**计数口径**的无时钟降频）。
//    `LogThrottle` 的三级时钟**永不抛异常**（`Runtime/Core/LogThrottle.cs` 语义约束 ①）：
//    非 Unity 进程（`tools/probes/hosts/*` 离线自检宿主）首次探测失败即自动降级到进程单调时钟，
//    故本文件可以直接委托。需要**确定性**计时（离线断言逐值可复现）时注入 `Log.Clock`
//    （它转发到 `LogThrottle.Clock`）；本层**不**自行改全局时钟（那会劫持宿主/其他模块的注入）。
//
// 本层口径：
//    ① 计数口径的间隔 = 第 1 次 + 之后每 100 次一条；`ShouldLogEvery` 只有单一 `everyN`
//       ⇒ 不含"第 10 次"那条（短促突发的证据少 1 行；要更密把该常量改成 10 即可，代价是每百次多 8 行）。
//    ② 不追加行尾后缀：计数口径**不向调用方暴露 N**
//       （只有 `*Counted` 那组会补 `（同类第 N 次）`，但那组直发 `Game.Logger`、绕过本项目 `Log`
//       门面的 tag 规范化）；全项目无判据依赖该后缀。
//    ③ key 加模块前缀 `Combat/`：引擎的限频表是**全局一张**
//       ⇒ 加前缀防止跨模块同名 key 串味（对调用方不可见，key 从不进日志）。
//    输出**一律走本层 `Log.Warn` / `Log.Error`**：tag 规范化 + 项目静默开关在那一层。
//    空 key 的处理：不输出，且不与其他调用方合并计数（引擎计数口径会把空 key 归并成 `"default"`）。
// ─────────────────────────────────────────────────────────────────────────────

using System;
using CloverEngine;
using Diablo2.Core;

namespace Diablo2.Module.Combat
{
    /// <summary>战斗日志门面（tag = <see cref="Tag"/>）。</summary>
    public static class CombatLog
    {
        /// <summary>模块日志 tag。</summary>
        public const string Tag = "Combat";

        /// <summary>
        /// 计数闸门（<see cref="LogThrottle.ShouldLogEvery"/>）的间隔：第 1 次 + 之后每 100 次一条
        /// （引擎只支持单一 `everyN` ⇒ 取 100）。
        /// </summary>
        private const int ThrottleEveryN = 100;

        /// <summary>本模块在引擎全局限频表里的 key 前缀（引擎是一张全局表 ⇒ 防跨模块撞 key）。</summary>
        private const string KeyPrefix = "Combat/";

        /// <summary>信息级（正常流程节点）。</summary>
        public static void Info(string msg) => Log.Info(Tag, msg);

        /// <summary>警告级（非预期但可恢复）。</summary>
        public static void Warn(string msg) => Log.Warn(Tag, msg);

        /// <summary>错误级（不该发生）。</summary>
        public static void Error(string msg, Exception ex = null) => Log.Error(Tag, msg, ex);

        /// <summary>
        /// 只报一次（同一 key 整个进程一条）。闸门 = 引擎 <see cref="LogThrottle.ShouldLog"/>（`+∞`）。
        /// </summary>
        /// <returns>本次是否真的输出。</returns>
        public static bool WarnOnce(string key, string msg)
        {
            if (string.IsNullOrEmpty(key))
            {
                Log.Warn(Tag, "CombatLog.WarnOnce 收到空 key ⇒ 不输出（避免自刷屏）");
                return false;
            }
            if (!LogThrottle.ShouldLog(KeyPrefix + key, float.PositiveInfinity)) return false;
            Log.Warn(Tag, msg);
            return true;
        }

        /// <summary>
        /// 无时钟降频：第 1 次 + 之后每 <see cref="ThrottleEveryN"/> 次一条。
        /// 闸门 = 引擎计数口径 <see cref="LogThrottle.ShouldLogEvery"/>（判定只用计数，不用计时值）。
        /// </summary>
        /// <returns>本次是否真的输出。</returns>
        public static bool WarnThrottled(string key, string msg)
        {
            if (string.IsNullOrEmpty(key))
            {
                Log.Warn(Tag, "CombatLog.WarnThrottled 收到空 key ⇒ 不输出（避免自刷屏）");
                return false;
            }
            if (!LogThrottle.ShouldLogEvery(KeyPrefix + key, ThrottleEveryN)) return false;
            Log.Warn(Tag, msg);
            return true;
        }

        /// <summary>
        /// 清空降频 / 只报一次记录（换区域 / 重进游戏时调用）。
        /// <para>引擎只提供**整体**清空（<see cref="LogThrottle.Reset"/>：时间口径 + 计数口径一起清），
        /// 没有"按模块清"的入口 ⇒ 本调用会把**其他模块**（含 `Core/Log`）的限频记录一并清掉，
        /// 它们各自的"只报一次"会重新生效一次。
        /// **无玩家可见影响**：唯一调用点 `DeathFlow.Reset()`（回主菜单 / 换区域）本来就要求"重开闸门"，
        /// 被多清的只是别人的**日志密度**，不触碰任何游戏状态 / 数值 / 存档。</para>
        /// </summary>
        public static void ResetThrottle() => LogThrottle.Reset();
    }
}
