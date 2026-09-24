// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · UI/UiLog.cs
// UI 层日志门面（tag 恒为 `Ui`）+ `OnOpen(param)` 参数校验。
//
// 为什么要这一层（而不是每个面板各写 `Log.Warn("Ui", …)`）：
//   ① UI 层 tag 只有这一处（`[Ui]`），面板不各自拼 tag；
//   ② `constraints.md` #7 要求「面板状态值只能由调用方传入，漏传参数要打 Warn」——
//      `Require<T>` 把「判空 + 打 Warn + 返回 null 让面板降级」收敛成一处，
//      于是每个面板的 `OnOpen` 都长一样，且**漏参数时不会静默退回默认值**。
//
// 参数校验的判定 / 降级口径 / 留痕闸门（走 `LogThrottle`，同一面板同一原因只报一次）
//   由引擎件 `CloverEngine.UIPanelGuards` 提供
//   （`clover-client-unity-engine/Runtime/Presentation/UIPanelGuards.cs`）；
//   本文件只钉死 UI 层 tag（`[Ui]`）。
//
// 本文件在 UI 层：只允许引用 `CloverEngine` / `Diablo2.Core` / `Diablo2.Def`，
//    不得引用 `Diablo2.Module` 下任何类型（分层自检 ③）。
// ─────────────────────────────────────────────────────────────────────────────

using System;
using CloverEngine;
using Diablo2.Core;

namespace Diablo2.UI
{
    /// <summary>UI 层日志与参数校验助手（tag 恒为 <see cref="Tag"/>）。</summary>
    internal static class UiLog
    {
        /// <summary>UI 层日志 tag（`Core/Log.cs` 白名单内的 `Ui`）。</summary>
        public const string Tag = "Ui";

        /// <summary>正常流程节点（面板开关、参数已装配、事件已接线）。</summary>
        public static void Info(string msg) => Log.Info(Tag, msg);

        /// <summary>非预期但可恢复（参数缺失、素材缺失、事件没人处理）。</summary>
        public static void Warn(string msg) => Log.Warn(Tag, msg);

        /// <summary>不该发生（空引用兜底、非法状态、加载失败）。</summary>
        public static void Error(string msg, Exception ex = null) => Log.Error(Tag, msg, ex);

        /// <summary>只报一次（同类降级信息说一遍就够，避免刷屏）。</summary>
        public static bool WarnOnce(string key, string msg) => Log.WarnOnce(Tag, key, msg);

        /// <summary>只报一次（Error 级）。</summary>
        public static bool ErrorOnce(string key, string msg) => Log.ErrorOnce(Tag, key, msg);

        /// <summary>
        /// 限频警告（同一 key 在 interval 秒内只输出一条）。
        /// 内部会读 `Time.realtimeSinceStartup`（引擎原生 API）⇒ **离线自检宿主不要走这条路径**
        /// （`Core/Log.cs:133`，与 `tools/flowcheck` 的说明同一原因）。
        /// </summary>
        public static bool WarnThrottled(string key, string msg, float intervalSeconds = 5f)
            => Log.WarnThrottled(Tag, key, msg, intervalSeconds);

        /// <summary>
        /// 校验 `OnOpen(param)` 的载荷：类型不符/为空 ⇒ **留痕 + 返回 null**（面板按空数据打开，不崩）。
        /// <para>判定 / 降级口径 / 留痕闸门（走 `LogThrottle`，同一面板同一原因**只报一次**，不刷屏）
        /// 由引擎件 <c>CloverEngine.UIPanelGuards.Require</c> 提供
        /// （`clover-client-unity-engine/Runtime/Presentation/UIPanelGuards.cs`）；
        /// 本方法只钉死 UI 层 tag（`[Ui]`，验收脚本按它检索日志）。</para>
        /// </summary>
        /// <typeparam name="T">期望的载荷类型（`Diablo2.Def` 里的 DTO）。</typeparam>
        /// <param name="param">`OnOpen` 收到的 object。</param>
        /// <param name="panel">面板类名（日志里点名，便于定位）。</param>
        public static T Require<T>(object param, string panel) where T : class
        {
            UIPanelGuards.Require(param, panel, Tag, out T value);
            return value;
        }

        /// <summary>
        /// 校验 `OnOpen(param)` 里的整型载荷（如 skillId / questId）；缺失或类型不符 ⇒ 留痕 + 返回兜底值。
        /// <para>判定与留痕由引擎件 <c>CloverEngine.UIPanelGuards.RequireValue</c> 提供（同一留痕口径：只报一次）。</para>
        /// </summary>
        public static int RequireInt(object param, string panel, int fallback)
            => UIPanelGuards.RequireValue(param, panel, fallback, Tag);
    }
}
