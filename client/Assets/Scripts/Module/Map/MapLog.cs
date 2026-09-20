// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · Module/Map/MapLog.cs
// 地图模块**统一日志入口**：tag 固定 = `Map`（`docs/agents/agent-04-地图模块.md` §4）。
//
// 为什么再包一层：`_common.md` §3 要求「一律 `Game.Logger.*` + tag 用模块名」，
// 而 `Module/Map/**` 里日志点很多（生成失败 / 重试 / 连通性 / 素材缺失 / 寻路失败…），
// 每处都手写 "Map" 字符串会写错、改名会漏。这里收敛成一处常量。
//
// ⛔ 不打裸 `Debug.Log`；⛔ 高频回调一律走 `WarnThrottled`（防刷屏）。
// ─────────────────────────────────────────────────────────────────────────────

using Diablo2.Core;
using Diablo2.Def;

namespace Diablo2.Module.Map
{
    /// <summary>地图模块日志门面（tag = <see cref="Tag"/>）。</summary>
    public static class MapLog
    {
        /// <summary>模块日志 tag（`Log.KnownTags` 白名单内的模块名）。</summary>
        public const string Tag = "Map";

        /// <summary>信息级。</summary>
        public static void Info(string msg) => Log.Info(Tag, msg);

        /// <summary>警告级（非预期但可恢复）。</summary>
        public static void Warn(string msg) => Log.Warn(Tag, msg);

        /// <summary>错误级（不该发生）。</summary>
        public static void Error(string msg, System.Exception ex = null) => Log.Error(Tag, msg, ex);

        /// <summary>限频警告（高频回调里的非预期分支）。</summary>
        public static bool WarnThrottled(string key, string msg, float intervalSeconds = 5f)
            => Log.WarnThrottled(Tag, key, msg, intervalSeconds);

        /// <summary>只报一次（素材缺失 / 降级这类说一遍就够的事）。</summary>
        public static bool WarnOnce(string key, string msg) => Log.WarnOnce(Tag, key, msg);

        /// <summary>区域中文标签（日志/自证输出用；正式 UI 文本走配表 `level_c.name`）。</summary>
        public static string AreaLabel(AreaId area)
        {
            switch (area)
            {
                case AreaId.Town: return "罗格营地";
                case AreaId.BloodMoor: return "血腥荒野";
                case AreaId.DenOfEvil: return "邪恶洞穴";
                default:
                    Warn($"AreaLabel: 未登记的区域 {(int)area}，按 Unknown 输出");
                    return "Unknown";
            }
        }
    }
}
