// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · Module/View/ViewLog.cs
// 精灵视图模块的**统一日志入口**（tag 固定 = `View`，在 `Core/Log.cs` 的白名单内）。
//
// ⚠️ 本文件自己实现 `WarnOnce/WarnThrottled`，不用 `Core/Log.cs` 的同名方法：
//    后者内部用 `UnityEngine.Time.realtimeSinceStartup`（`Core/Log.cs:133`，原生 API），
//    在非 Unity 进程（离线自检宿主 `tools/combatcheck/`）里会抛 `SecurityException`。
//    语义保持一致，逐条理由见 `Module/Combat/CombatLog.cs` 文件头。
// ⛔ 禁止裸 `Debug.Log`。
// ─────────────────────────────────────────────────────────────────────────────

using System;
using System.Collections.Generic;
using Diablo2.Core;

namespace Diablo2.Module.View
{
    /// <summary>视图模块日志门面（tag = <see cref="Tag"/>）。</summary>
    public static class ViewLog
    {
        /// <summary>模块日志 tag。</summary>
        public const string Tag = "View";

        private static readonly HashSet<string> OnceDone = new HashSet<string>(StringComparer.Ordinal);
        private static readonly Dictionary<string, int> Counts = new Dictionary<string, int>(StringComparer.Ordinal);

        /// <summary>信息级。</summary>
        public static void Info(string msg) => Log.Info(Tag, msg);

        /// <summary>警告级。</summary>
        public static void Warn(string msg) => Log.Warn(Tag, msg);

        /// <summary>错误级。</summary>
        public static void Error(string msg, Exception ex = null) => Log.Error(Tag, msg, ex);

        /// <summary>只报一次。</summary>
        public static bool WarnOnce(string key, string msg)
        {
            if (string.IsNullOrEmpty(key))
            {
                Log.Warn(Tag, "ViewLog.WarnOnce 收到空 key ⇒ 不输出（避免自刷屏）");
                return false;
            }
            if (!OnceDone.Add(key)) return false;
            Log.Warn(Tag, msg);
            return true;
        }

        /// <summary>无时钟降频（第 1/10/100/200… 次输出）。</summary>
        public static bool WarnThrottled(string key, string msg)
        {
            if (string.IsNullOrEmpty(key))
            {
                Log.Warn(Tag, "ViewLog.WarnThrottled 收到空 key ⇒ 不输出（避免自刷屏）");
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

        /// <summary>清空降频记录。</summary>
        public static void ResetThrottle()
        {
            OnceDone.Clear();
            Counts.Clear();
        }
    }
}
