// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · Module/Combat/CombatLog.cs
// 战斗子系统（Combat + 与之共用伤害管线的 Monster/Skill/View）的**统一日志入口**，
// tag 固定 = `Combat`（`Core/Log.cs` 的 `KnownTags` 白名单内）。
//
// ⚠️ 为什么本文件自己实现 `WarnOnce/WarnThrottled`，而不是直接用 `Core/Log.cs` 的同名方法？
//    `Log.WarnOnce/VarnThrottled` 内部走 `Log.ShouldLog` → `UnityEngine.Time.realtimeSinceStartup`
//    （`client/Assets/Scripts/Core/Log.cs:133`）——**引擎原生 API**。用户尚未打开 Unity 编辑器，
//    本阶段的验证手段只能是**离线自检宿主**（`tools/combatcheck/`，与 `tools/mapcheck/` 同一套做法），
//    而在非 Unity 进程里 `Time.realtimeSinceStartup` 会抛 `SecurityException`
//    （实测见 `tools/mapcheck/Program.cs:59-68` 的输出）⇒ 任何走到 `Log.WarnOnce/WarnThrottled`
//    的业务分支都会**把自检炸掉**，从而掩盖真正的验证结果。
//    ⇒ 本项目在**会被离线自检覆盖的路径**上用「本地守卫 + `Log.Warn`」，语义完全一致：
//      · `WarnOnce`     —— 同一 key 整个进程只输出一条；
//      · `WarnThrottled`—— 第 1、10、100、200… 次各输出一条（**不依赖时钟**的降频，
//                         比"每 N 秒一条"更可复现，且不会把后续证据永久吞掉）。
// ⛔ 仍然禁止裸 `Debug.Log`（`_common.md` §3.1）。
// ─────────────────────────────────────────────────────────────────────────────

using System;
using System.Collections.Generic;
using Diablo2.Core;

namespace Diablo2.Module.Combat
{
    /// <summary>战斗日志门面（tag = <see cref="Tag"/>）。</summary>
    public static class CombatLog
    {
        /// <summary>模块日志 tag。</summary>
        public const string Tag = "Combat";

        private static readonly HashSet<string> OnceDone = new HashSet<string>(StringComparer.Ordinal);
        private static readonly Dictionary<string, int> Counts = new Dictionary<string, int>(StringComparer.Ordinal);

        /// <summary>信息级（正常流程节点）。</summary>
        public static void Info(string msg) => Log.Info(Tag, msg);

        /// <summary>警告级（非预期但可恢复）。</summary>
        public static void Warn(string msg) => Log.Warn(Tag, msg);

        /// <summary>错误级（不该发生）。</summary>
        public static void Error(string msg, Exception ex = null) => Log.Error(Tag, msg, ex);

        /// <summary>只报一次（同一 key 整个进程一条）。</summary>
        /// <returns>本次是否真的输出。</returns>
        public static bool WarnOnce(string key, string msg)
        {
            if (string.IsNullOrEmpty(key))
            {
                Log.Warn(Tag, "CombatLog.WarnOnce 收到空 key ⇒ 不输出（避免自刷屏）");
                return false;
            }
            if (!OnceDone.Add(key)) return false;
            Log.Warn(Tag, msg);
            return true;
        }

        /// <summary>无时钟降频（第 1/10/100/200… 次输出）。</summary>
        /// <returns>本次是否真的输出。</returns>
        public static bool WarnThrottled(string key, string msg)
        {
            if (string.IsNullOrEmpty(key))
            {
                Log.Warn(Tag, "CombatLog.WarnThrottled 收到空 key ⇒ 不输出（避免自刷屏）");
                return false;
            }

            Counts.TryGetValue(key, out var n);
            n++;
            Counts[key] = n;

            if (n == 1 || n == 10 || n % 100 == 0)
            {
                Log.Warn(Tag, msg + $"（第 {n} 次）");
                return true;
            }
            return false;
        }

        /// <summary>清空降频/只报一次记录（换区域 / 重进游戏时调用）。</summary>
        public static void ResetThrottle()
        {
            OnceDone.Clear();
            Counts.Clear();
        }
    }
}
