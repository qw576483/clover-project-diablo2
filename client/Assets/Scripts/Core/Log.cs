// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · Core/Log.cs
// 轻量日志门面：**全项目唯一的打日志入口**。
//
// 为什么还要一层：本项目**禁止裸 `Debug.Log`**，日志一律经本层 ——
//   它把「转发 + tag 规范化 + 防刷屏」收敛到一处：
//   · 高频回调（每帧碰撞/寻路失败/素材缺失）里裸打日志会**刷屏把日志文件打爆**，
//     本层提供 <see cref="WarnThrottled"/> / <see cref="ErrorOnce"/> 两个降频入口。
//
// tag 约定：**用模块名**（`Map` / `Flow` / `Player` / `Combat` / …），
//   与验收要抄的日志格式一致：`[时间] [级别] [Flow] → MainMenu`。
//   **不要**自己加 `D2.` 前缀（会破坏按 `[Flow]` 检索的验收脚本）。
//
//   降频入口（`WarnThrottled` / `ErrorThrottled` / `WarnOnce` / `ErrorOnce` / `ShouldLog`）需要一个
//   「单调秒」。默认取 Unity 的 `Time.realtimeSinceStartup`；但那是**原生 ECall**，在非 Unity 进程
//   （`tools/*check` 这类离线自检宿主）里调用会抛 `SecurityException`
//   （`ECall methods must be packaged into a system module`）。
//   ⇒ 本文件：**① 可注入时钟** `Log.Clock`（`Func<float>`，返回单调秒）；
//              **② 首次发现 Unity 时钟不可用就自动降级到进程单调时钟（`Stopwatch`）并报一次**。
//   **离线宿主请注入时钟**（`Log.Clock = () => mySeconds;`）—— 注入后限频行为完全可复现（确定性），
//   不注入也不会抛异常（走自动降级，只是时间原点不可控）。排障看 `Log.ClockSource`。
//
//   `CloverEngine.LogThrottle`（同源实现，自带离线断言 71/71 PASS）。本文件**只留门面**，分两类：
//     · **保留在项目侧**（引擎刻意不做，属项目专属）：`KnownTags` 白名单 / `IsKnownTag` /
//       `Normalize(tag)` / `Suppress` / 基础转发 `Info/Warn/Error/Debug`。
//       tag 规范化必须**先于**真正输出 —— 验收脚本按 `[tag]` 检索日志，而引擎版 `LogThrottle`
//       不做 tag 校验（它不认识本项目的模块名）。所以降频入口一律走本层 `Warn`/`Error` 输出。
//     · **转发给引擎**（一行语义都不加）：限频窗口 /「只报一次」/ 三级时钟 / 注入时钟 / 清表
//       ⇒ `LogThrottle.ShouldLog` / `Clock` / `ClockSource` / `Reset`。
//   `Log.Clock` / `Log.ClockSource` 是注入与排障入口（读写都转发到 `LogThrottle`）。
// ─────────────────────────────────────────────────────────────────────────────

using System;
using System.Collections.Generic;
using CloverEngine;

namespace Diablo2.Core
{
    /// <summary>项目日志门面（转发引擎 <see cref="ILogger"/> + 防刷屏）。</summary>
    public static class Log
    {
        /// <summary>缺 tag 时使用的兜底 tag（正常代码不该走到这里）。</summary>
        public const string FallbackTag = "D2";

        /// <summary>本项目合法的模块 tag 白名单（写错 tag 会让验收脚本检索不到日志）。</summary>
        private static readonly HashSet<string> KnownTags = new HashSet<string>(StringComparer.Ordinal)
        {
            "App", "Flow", "Map", "Player", "Monster", "Combat", "Skill", "Item", "Quest",
            "Npc", "Input", "Camera", "View", "Audio", "Save", "Ui", "Table", "Cfg",
            "AStar", "Iso", "Rng", "D2",
            // 把"生效口径"类 Info 单独挂一个 tag 便于按 tag 检索（混进 Map/Player
            //   会与模块常规日志无法区分）。不加这一行不会报错，但首条该 tag 的日志会附带
            //   一条「tag 不在白名单」的 Warn。
            "R1-B",
            // 同一理由（按 tag 检索"只报一次"的生效口径行）；三条日志分别在
            //   `UI/CharCreatePanel.OnOpen`（过渡逐帧矩形 / 名字输入由面板驱动）与 `UI/UiArt.SetSprite`（请求守卫）。
            "R1-C",
            // `T0FIX`：`Module/Audio/AudioModule`（静音开关落盘 / 冷启动读回）、
            //   `Module/Flow/AppFlow.OnSaveDone`（`D2.Save.Done` 的消费账目）。
            //   不加这一行不会报错，但首条 T0FIX 日志会附带一条「tag 不在白名单」的 Warn。
            "T0FIX",
            // 不加这一行不会报错，但首条该 tag 的日志会附带一条「tag 不在白名单」的 Warn。
            "T0GAP",
        };

        /// <summary>全局静默开关（只给压测/自动化用；正常流程不要打开）。</summary>
        public static bool Suppress { get; set; }

        /// <summary>该 tag 是否在模块白名单内（供模块自检/日志取证用；白名单本身不外露）。</summary>
        public static bool IsKnownTag(string tag) => !string.IsNullOrEmpty(tag) && KnownTags.Contains(tag);

        /// <summary>tag 相关告警（缺 tag / tag 不在白名单）是否已说过一次 —— 避免自刷屏。</summary>
        private static bool _tagWarned;

        // ── 时钟（单调秒；实现 = 引擎 `LogThrottle`，这里只转发）──────────────
        /// <summary>
        /// 可注入时钟：返回**单调递增的秒**，语义与 `UnityEngine.Time.realtimeSinceStartup` 一致
        /// （与帧率/`timeScale` 无关，用于限频计时）。
        /// <para>默认 `null` = 用 Unity 的 `Time.realtimeSinceStartup`。
        /// **离线宿主（非 Unity 进程）请注入自己的时钟**，例如：
        /// <c>Diablo2.Core.Log.Clock = () =&gt; (float)sw.Elapsed.TotalSeconds;</c> ——
        /// 注入后限频行为确定可复现；不注入也不会抛异常（由 <see cref="LogThrottle"/> 自动降级）。</para>
        /// </summary>
        public static Func<float> Clock
        {
            get => LogThrottle.Clock;
            set => LogThrottle.Clock = value;
        }

        /// <summary>
        /// Unity 时钟是否已被探测为不可用（= 已降级到进程单调时钟）。
        /// <para>探测与降级的状态由 <see cref="LogThrottle"/> 内部持有 ——
        /// 这里按引擎的时钟来源判定：`"Process"` ⇔ 已探测到 Unity 原生时钟不可用并降级。</para>
        /// </summary>
        public static bool UnityClockUnavailable => LogThrottle.ClockSource == "Process";

        /// <summary>
        /// 当前生效的时钟来源：`"Injected"`（已注入 <see cref="Clock"/>）/ `"Unity"`（默认，尚未探测）/
        /// `"Process"`（Unity 时钟不可用，已降级到 `Stopwatch`）。**供自检与排障用**，业务不要依赖它。
        /// </summary>
        public static string ClockSource => LogThrottle.ClockSource;

        // ── 基础转发 ─────────────────────────────────────────────────────────
        /// <summary>信息（正常流程节点：站点切换、地图生成、任务状态…）。</summary>
        public static void Info(string tag, string msg)
        {
            if (Suppress) return;
            Game.Logger.Info(Normalize(tag), msg);
        }

        /// <summary>警告（非预期但可恢复：素材缺失、寻路失败、参数缺失…）。</summary>
        public static void Warn(string tag, string msg)
        {
            if (Suppress) return;
            Game.Logger.Warn(Normalize(tag), msg);
        }

        /// <summary>错误（不该发生：空引用兜底、非法状态、加载失败…）。</summary>
        public static void Error(string tag, string msg, Exception ex = null)
        {
            if (Suppress) return;
            Game.Logger.Error(Normalize(tag), msg, ex);
        }

        /// <summary>调试（默认会被引擎按级别过滤，别拿它当正式日志）。</summary>
        public static void Debug(string tag, string msg)
        {
            if (Suppress) return;
            Game.Logger.Debug(Normalize(tag), msg);
        }

        // ── 防刷屏（闸门与时钟转发 `CloverEngine.LogThrottle`；tag 规范化留在本层）──
        // 下面 4 个入口必须「先过闸门 → 再走本层 `Warn`/`Error` 输出」，**不许**直接调
        //    `LogThrottle.WarnThrottled` / `ErrorOnce`：那一层不做 tag 校验，
        //    会绕过 `Normalize(tag)` ⇒ 验收脚本按 `[tag]` 检索日志会失效。
        /// <summary>
        /// 限频警告：同一 <paramref name="key"/> 在 <paramref name="intervalSeconds"/> 秒内只输出第一条。
        /// 用于**高频回调**里的非预期分支（每帧都可能触发的素材缺失/寻路失败等）。
        /// </summary>
        /// <returns>本次是否真的输出了日志。</returns>
        public static bool WarnThrottled(string tag, string key, string msg, float intervalSeconds = 5f)
        {
            if (!ShouldLog(key, intervalSeconds)) return false;
            Warn(tag, msg);
            return true;
        }

        /// <summary>限频错误：语义同 <see cref="WarnThrottled"/>，级别为 Error。</summary>
        /// <returns>本次是否真的输出了日志。</returns>
        public static bool ErrorThrottled(string tag, string key, string msg, float intervalSeconds = 5f)
        {
            if (!ShouldLog(key, intervalSeconds)) return false;
            Error(tag, msg);
            return true;
        }

        /// <summary>
        /// 只报一次：同一 <paramref name="key"/> **整个进程生命周期内**只输出一条。
        /// 用于「初始化失败」「降级到占位资源」这类说一遍就够的事。
        /// </summary>
        /// <returns>本次是否真的输出了日志。</returns>
        public static bool ErrorOnce(string tag, string key, string msg)
        {
            if (!ShouldLog(key, float.PositiveInfinity)) return false;
            Error(tag, msg);
            return true;
        }

        /// <summary>只报一次（Warn 级），语义同 <see cref="ErrorOnce"/>。</summary>
        /// <returns>本次是否真的输出了日志。</returns>
        public static bool WarnOnce(string tag, string key, string msg)
        {
            if (!ShouldLog(key, float.PositiveInfinity)) return false;
            Warn(tag, msg);
            return true;
        }

        /// <summary>限频闸门：返回 true 表示「现在可以打」。命中闸门时不产生任何日志。</summary>
        public static bool ShouldLog(string key, float intervalSeconds = 5f)
        {
            // ① 项目侧的全局静默**先短路**：`Suppress = true` ⇒ 恒 false 且零日志
            //    （也不能碰时钟 —— fullcheck 在 `Cfg` 预热期间正靠这条规避原生 ECall）。
            if (Suppress) return false;

            // ② 其余全在引擎里（同一套语义）：空/`null` key ⇒ 不输出 + 只报一次；
            //    `intervalSeconds` 有限 ⇒ 限频窗口；`PositiveInfinity` ⇒ 只报一次；三级时钟。
            return LogThrottle.ShouldLog(key, intervalSeconds);
        }

        /// <summary>清空限频记录（换场景/重进游戏时调用，避免「上次进图报过就不再报」）。</summary>
        public static void ResetThrottle() => LogThrottle.Reset();

        // ── 内部 ─────────────────────────────────────────────────────────────
        private static string Normalize(string tag)
        {
            if (string.IsNullOrEmpty(tag))
            {
                if (!_tagWarned)
                {
                    _tagWarned = true;
                    Game.Logger.Warn(FallbackTag, "日志缺少 tag（调用方用模块名，如 Map/Flow）；已用 " + FallbackTag);
                }
                return FallbackTag;
            }

            if (!KnownTags.Contains(tag) && !_tagWarned)
            {
                // 不算错误（新增模块可能还没登记），但说出来一次：验收脚本按 [tag] 检索日志
                _tagWarned = true;
                Game.Logger.Warn(FallbackTag,
                    $"日志 tag「{tag}」不在白名单（KnownTags）里；验收脚本按 [tag] 检索，请统一用模块名或补登记");
            }

            return tag;
        }
    }
}
