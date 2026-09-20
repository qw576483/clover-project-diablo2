// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · Module/Flow/FlowLog.cs
// Flow 的日志出口：**tag 固定 = `Flow`**。
//
// ★ 站点迁移是验收硬指标（`docs/agents/agent-05-流程与菜单链路.md` §5）：
//     「站点迁移完整：[Flow] → Boot/MainMenu/CharSelect/CharCreate/Loading/Stage/Pause 各出现一次」
//   因此每次站点切换都由 `AppFlow` 经 `FlowLog.Station` 打**一行** `[Flow] → <站点>`，
//   日志落盘后可直接 grep `[Flow] →` 抄进验收表。
// ─────────────────────────────────────────────────────────────────────────────

using Diablo2.Core;

namespace Diablo2.Module.Flow
{
    /// <summary>Flow 日志助手（tag 统一 + 防刷屏，禁止裸字符串 tag）。</summary>
    internal static class FlowLog
    {
        /// <summary>日志 tag（模块名，验收脚本按它检索）。</summary>
        public const string Tag = "Flow";

        /// <summary>站点切换日志：`[Flow] → MainMenu`。</summary>
        public static void Station(string station)
        {
            if (string.IsNullOrEmpty(station)) return;
            Log.Info(Tag, $"→ {station}");
        }

        /// <summary>未接入的模块（null 容忍，只报一次）。</summary>
        public static void Missing(string module)
        {
            if (string.IsNullOrEmpty(module)) return;
            Log.WarnOnce(Tag, "missing." + module,
                $"模块 {module} 未注册（AppContext 里为 null）⇒ 相关功能降级，等待该模块 agent 接入");
        }

        /// <summary>只报一次（同一条消息在整个进程生命周期内只出现一次）。</summary>
        public static void WarnOnce(string key, string msg)
        {
            Log.WarnOnce(Tag, key, msg);
        }
    }
}
