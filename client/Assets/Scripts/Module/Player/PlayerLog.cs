// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · Module/Player/PlayerLog.cs
//
// 而 `Module/Player/**` 里日志点很多（寻路失败 / 受阻 / 升级 / 加点 / 装备生效…），
// 每处手写 "Player" 字符串会写错、改名会漏。这里收敛成一处常量。
//
// 两个硬约定（验收脚本按它们 grep，改格式前先看这里）：
//   ① 升级那条必须是 `[Player] level 1→2 …`：tag 由本类提供，**消息体以 `level` 开头**
//      （`策划/验收表.md` 第 18 行）；
//      由 <see cref="Move"/> 统一加前缀。
//
// 本模块**故意不用** `Log.WarnThrottled/WarnOnce/ErrorOnce`：`Core/Log.cs:133` 的降频闸门
//    依赖 Unity 原生 `Time.realtimeSinceStartup`，在离线自检宿主（非 Unity 进程）里会抛
//    `SecurityException`（实测见 `tools/mapcheck/Program.cs:61-68`），而 playercheck 必须能
//    把「受阻 / 不可达 / 装备词缀未映射 / 无相机」这些**非预期分支**真跑一遍。
//    ⇒ 本模块的降频一律用**私有 bool 标志位**（同一事件只报一次），语义等价且宿主可跑。
// ─────────────────────────────────────────────────────────────────────────────

using System;
using Diablo2.Core;

namespace Diablo2.Module.Player
{
    /// <summary>Player 模块日志门面（tag = <see cref="Tag"/>）。</summary>
    internal static class PlayerLog
    {
        /// <summary>模块日志 tag（`Core/Log.cs` 的 KnownTags 白名单内的模块名）。</summary>
        public const string Tag = "Player";

        /// <summary>空名兜底（正常路径由 Flow/创角屏保证非空；走到这里会 Warn）。</summary>
        public const string FallbackName = "Hero";

        /// <summary>信息级（正常流程节点：移动、到达、升级、复活…）。</summary>
        public static void Info(string msg) => Log.Info(Tag, msg);

        /// <summary>警告级（非预期但可恢复：不可达、受阻、属性点不足…）。</summary>
        public static void Warn(string msg) => Log.Warn(Tag, msg);

        /// <summary>错误级（不该发生：配表缺失、未创建角色就操作…）。</summary>
        public static void Error(string msg, Exception ex = null) => Log.Error(Tag, msg, ex);

        /// <summary>
        /// 移动专用：统一加 `[Move] ` 前缀（验收脚本 grep `[Move] steps=` / `path=`）。
        /// 调用方按 `steps=… path=… from=… to=…` 的顺序拼，保证两种 grep 都能命中。
        /// </summary>
        public static void Move(string msg) => Log.Info(Tag, "[Move] " + msg);
    }
}
