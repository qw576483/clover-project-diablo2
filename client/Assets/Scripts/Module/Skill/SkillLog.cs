// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · Module/Skill/SkillLog.cs
// 技能模块的**统一日志入口**（tag 固定 = `Skill`，在 `Core/Log.cs` 的白名单内）。
//
//    全部交给 `CloverEngine.LogThrottle`，本文件**不再自持任何 `HashSet` / `Dictionary`**：
//      · `WarnOnce` / `ErrorOnce` ⇒ `LogThrottle.ShouldLog(key, float.PositiveInfinity)`（只报一次）；
//      · `WarnThrottled`          ⇒ `LogThrottle.ShouldLogEvery(key, ThrottleEveryN)`（计数口径降频）。
//    `WarnOnce` 与 `ErrorOnce` 在引擎表里**共用同一个 key**（`Skill/` + key）—— 与原实现共用同一个
//    `OnceDone` 集合的行为一致（同一个 key 先 Warn 过，之后的 Error 也不再出）。
//    离线宿主安全性由引擎保证（`Runtime/Core/LogThrottle.cs`：三级时钟、永不抛异常、自动降级），
//    故本文件可以直接委托；要确定性计时请注入 `Log.Clock`（本层不自行改全局时钟）。
//    逐条理由 / 两处语义差异（计数间隔改为"第 1 次 + 之后每 100 次"、行尾不再补 `（第 N 次）`）见
//    `Module/Combat/CombatLog.cs` 文件头，不在此重复。
// 禁止裸 `Debug.Log`。
// ─────────────────────────────────────────────────────────────────────────────

using System;
using CloverEngine;
using Diablo2.Core;

namespace Diablo2.Module.Skill
{
    /// <summary>技能模块日志门面（tag = <see cref="Tag"/>）。</summary>
    public static class SkillLog
    {
        /// <summary>模块日志 tag。</summary>
        public const string Tag = "Skill";

        /// <summary>
        /// 计数闸门（<see cref="LogThrottle.ShouldLogEvery"/>）的间隔：第 1 次 + 之后每 100 次一条
        /// （原自实现 = 第 1 次、第 10 次、以及 100 的倍数；引擎只支持单一 `everyN` ⇒ 取 100，
        /// 100 的倍数段逐点一致）。
        /// </summary>
        private const int ThrottleEveryN = 100;

        /// <summary>本模块在引擎全局限频表里的 key 前缀（引擎是一张全局表 ⇒ 防跨模块撞 key）。</summary>
        private const string KeyPrefix = "Skill/";

        /// <summary>信息级。</summary>
        public static void Info(string msg) => Log.Info(Tag, msg);

        /// <summary>警告级。</summary>
        public static void Warn(string msg) => Log.Warn(Tag, msg);

        /// <summary>错误级。</summary>
        public static void Error(string msg, Exception ex = null) => Log.Error(Tag, msg, ex);

        /// <summary>只报一次（与 <see cref="ErrorOnce"/> 共用同一个 key ⇒ 同 key 二者合计只出一条）。</summary>
        public static bool WarnOnce(string key, string msg)
        {
            if (string.IsNullOrEmpty(key))
            {
                Log.Warn(Tag, "SkillLog.WarnOnce 收到空 key ⇒ 不输出（避免自刷屏）");
                return false;
            }
            if (!LogThrottle.ShouldLog(KeyPrefix + key, float.PositiveInfinity)) return false;
            Log.Warn(Tag, msg);
            return true;
        }

        /// <summary>只报一次（Error 级），与 <see cref="WarnOnce"/> 共用同一个 key。</summary>
        public static bool ErrorOnce(string key, string msg)
        {
            if (string.IsNullOrEmpty(key))
            {
                Log.Warn(Tag, "SkillLog.ErrorOnce 收到空 key ⇒ 不输出");
                return false;
            }
            if (!LogThrottle.ShouldLog(KeyPrefix + key, float.PositiveInfinity)) return false;
            Log.Error(Tag, msg);
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
                Log.Warn(Tag, "SkillLog.WarnThrottled 收到空 key ⇒ 不输出（避免自刷屏）");
                return false;
            }
            if (!LogThrottle.ShouldLogEvery(KeyPrefix + key, ThrottleEveryN)) return false;
            Log.Warn(Tag, msg);
            return true;
        }

        /// <summary>
        /// 清空降频记录。引擎只提供**整体**清空（<see cref="LogThrottle.Reset"/>）⇒ 其他模块的限频
        /// 记录会一并被清（各自的"只报一次"重新生效一次）。**无玩家可见影响**：只影响日志密度，
        /// 不触碰游戏状态 / 数值 / 存档。
        /// </summary>
        public static void ResetThrottle() => LogThrottle.Reset();
    }
}
