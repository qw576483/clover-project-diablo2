// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · Module/Audio/AudioLog.cs
// 音频模块的日志出口（tag 固定 = `Audio`，见 `Core/Log.cs` 的 tag 白名单）。
//
//    自己维护"每键一次 / 每事一次"；现在全部换成引擎的时间口径闸门
//    `CloverEngine.LogThrottle.ShouldLog(key, float.PositiveInfinity)`（= 同一 key 整个进程只放行一次）。
//    本文件**不再持有任何限频状态容器**；`MissingWarnCount` / `UnregisteredWarnCount` /
//    `ThrottledWarnCount` 三个**计数**是给离线宿主断言用的业务账目，保留。
//    （原生 ECall），在**纯 .NET 宿主**（`tools/probes/hosts/audiocheck`）里会抛 `SecurityException`
//    ⇒「只报一次」变成「报一次就崩」。该性质现在由**引擎**保证（`Runtime/Core/LogThrottle.cs`
//    §语义约束 ①：三级时钟、**永不抛异常**，非 Unity 进程首次探测失败即自动降级到进程单调时钟）
//    ⇒ 本类可以直接委托；要确定性计时请注入 `Log.Clock`（本层不自行改全局时钟）。
//
// key 都加了 `Audio/` 前缀 + 用途段（引擎的限频表是**全局一张**，原实现是每类一张
// ⇒ 必须防跨用途/跨模块撞 key；前缀对调用方不可见，key 从不进日志）。
// 「只报一次」的粒度（**与原实现逐条一致**）：
//   · **文件缺失** —— 每个键各报一次（SFX / BGM 各占一个 key 段，互不影响）；
//   · **基础设施缺失**（`Game.Sound` / `Game.Res` / `Game.Setting` / `Game.Event` 为 null）—— 各报一次；
//   · 记录是**进程级**的（引擎静态表），与"素材到位前反复请求同一缺失键"这条路径对得上。
// 素材到位后这些告警自动消失（探测到文件后不再进缺失分支），**无需改代码**。
// ─────────────────────────────────────────────────────────────────────────────

using System;
using CloverEngine;
using Diablo2.Core;

namespace Diablo2.Module.Audio
{
    /// <summary>音频模块日志（防刷屏 = 引擎 <see cref="LogThrottle"/> 的时间口径闸门）。</summary>
    internal static class AudioLog
    {
        /// <summary>日志 tag（`Core/Log.cs` 白名单里的模块名）。</summary>
        public const string Tag = "Audio";

        /// <summary>本模块在引擎全局限频表里的 key 前缀（引擎是一张全局表 ⇒ 防跨模块撞 key）。</summary>
        private const string KeyPrefix = "Audio/";

        /// <summary>「文件缺失」累计告警条数（自检断言"恰好 1 条"用；正常流程只读）。</summary>
        internal static int MissingWarnCount;

        /// <summary>「未登记键」累计告警条数（自检用）。</summary>
        internal static int UnregisteredWarnCount;

        internal static int ThrottledWarnCount;

        // ── 普通转发（重载引擎 `Game.Logger`，tag 固定）────────────────────────

        /// <summary>信息（登记表内容 / 音量变化 / BGM 切歌）。</summary>
        public static void Info(string msg) => Log.Info(Tag, msg);

        /// <summary>错误。</summary>
        public static void Error(string msg) => Log.Error(Tag, msg);

        // ── 一次性告警 ───────────────────────────────────────────────────────

        /// <summary>音效文件缺失（每键一次）：不重复调用引擎，静默降级。</summary>
        public static void MissingSfx(string key, string fileName, string path)
        {
            if (!Once(KeyPrefix + "missing.sfx/" + key)) return;
            MissingWarnCount++;
            Log.Warn(Tag,
                $"音效文件缺失：键=\"{key}\" 期望文件=\"{fileName}\"（路径 {path}）" +
                "⇒ 本次及之后**静默跳过**（只报这一条）；素材到位后自动生效，无需改代码");
        }

        /// <summary>BGM 文件缺失（每键一次）。</summary>
        public static void MissingBgm(string key, string fileName, string path)
        {
            if (!Once(KeyPrefix + "missing.bgm/" + key)) return;
            MissingWarnCount++;
            Log.Warn(Tag,
                $"BGM 文件缺失：键=\"{key}\" 期望文件=\"{fileName}\"（路径 {path}）" +
                "⇒ 本次及之后**静默跳过**（只报这一条）；素材到位后自动生效，无需改代码");
        }

        /// <summary>
        /// <para>闸门 = 引擎 <see cref="LogThrottle.ShouldLog"/>（`+∞` ⇒ 每键一条）：不再依赖
        /// `UnityEngine.Time`，纯 .NET 宿主也跑得通（引擎自动降级，永不抛）。</para>
        /// </summary>
        public static void WarnThrottledSfx(string key, float sinceSeconds)
        {
            if (string.IsNullOrEmpty(key)) return;
            if (!Once(KeyPrefix + "sfx.throttled/" + key)) return;   // 每个键只留一条痕（防"节流日志自己刷屏"）
            ThrottledWarnCount++;
            Log.Warn(Tag,
                $"音效键 \"{key}\" 重复过快：距上次起播仅 {sinceSeconds * 1000f:0} ms" +
                $"（最小间隔 {SfxThrottle.MinRepeatSeconds * 1000f:0} ms）⇒ 本次丢弃" +
                $"（累计丢弃 {SfxThrottle.DropCount} 次；本键只报这一条）");
        }

        /// <summary>请求了登记表里没有的键（每键一次）：照常尝试播放，但提醒补登记。</summary>
        public static void UnregisteredKey(string key, bool isBgm)
        {
            if (string.IsNullOrEmpty(key)) return;
            if (!Once(KeyPrefix + "unregistered/" + (isBgm ? "B:" : "S:") + key)) return;
            UnregisteredWarnCount++;
            Log.Warn(Tag,
                $"音效键未登记：\"{key}\"（{(isBgm ? "BGM" : "SFX")}）不在 `SfxRegistry` 里" +
                "⇒ 仍按「键即资源名」尝试播放；请补登 `Module/Audio/SfxRegistry.cs`");
        }

        /// <summary>`Game.Sound` 未挂载（引擎表现域没起来）。</summary>
        public static void NoSoundManager()
        {
            if (!Once(KeyPrefix + "no.sound")) return;
            Log.Warn(Tag, "`Game.Sound` 为 null（引擎表现域未挂载？）⇒ 所有音效/BGM 调用被跳过（只报这一条）");
        }

        /// <summary>`Game.Res` 未挂载。</summary>
        public static void NoResourceManager()
        {
            if (!Once(KeyPrefix + "no.res")) return;
            Log.Warn(Tag, "`Game.Res` 为 null（CloverRes.Init 未调用？）⇒ 无法探测音频文件是否存在，按未到位处理（只报这一条）");
        }

        /// <summary>`Game.Setting` 未挂载（音量无法持久化）。</summary>
        public static void NoSetting()
        {
            if (!Once(KeyPrefix + "no.setting")) return;
            Log.Warn(Tag, "`Game.Setting` 为 null ⇒ 音量改动只作用于本次会话，不落盘（只报这一条）");
        }

        /// <summary>`Game.Event` 未挂载（触发点无法接线）。</summary>
        public static void NoEventBus()
        {
            if (!Once(KeyPrefix + "no.event")) return;
            Log.Warn(Tag, "`Game.Event` 为 null（Game.Launch 未调用？）⇒ 事件触发点未接线，只有直连调用会出声（只报这一条）");
        }

        /// <summary>收到空键。</summary>
        public static void EmptyKey()
        {
            if (!Once(KeyPrefix + "empty.key")) return;
            Log.Warn(Tag, "收到空音效键 ⇒ 忽略（只报这一条）");
        }

        /// <summary>事件载荷为 null（不该发生：发送方数据异常）。</summary>
        public static void NullPayload(string what)
        {
            if (!Once(KeyPrefix + "null.payload")) return;
            Log.Warn(Tag, $"事件载荷为 null（{what}）⇒ 忽略该次音效（只报这一条）");
        }

        /// <summary>区域没有对应 BGM 登记（数据异常）。</summary>
        public static void UnknownArea(object area)
        {
            if (!Once(KeyPrefix + "unknown.area")) return;
            Log.Warn(Tag, $"区域「{area}」没有登记的 BGM ⇒ 保持当前曲目（只报这一条）");
        }

        /// <summary>进图时还没收到过 `Events.AreaChanged`（Map 模块未接入 / 未发事件）。</summary>
        public static void NoAreaChanged()
        {
            if (!Once(KeyPrefix + "no.area.changed")) return;
            Log.Warn(Tag,
                $"进图（{Events.StageEntered}）前没有收到过 {Events.AreaChanged}" +
                "（IMapModule 未接入或未发该事件？）⇒ BGM 暂按区域 Town 处理（只报这一条）");
        }

        /// <summary>
        /// "只报一次"的闸门 = 引擎时间口径 <see cref="LogThrottle.ShouldLog"/>（`+∞` ⇒ 同一 key 只放行一次）。
        /// 与 <see cref="Log.ShouldLog"/> 的区别：这里**不**先短路项目静默开关（`Log.Suppress`）——
        /// 原实现（`HashSet.Add`）在静默期同样会**推进**"已报"状态，换成先短路会让静默期后的首条告警
        /// 与本类计数账目对不上。输出仍然走 `Log.Warn`（tag 规范化 + 静默开关在那一层）。
        /// </summary>
        private static bool Once(string key) => LogThrottle.ShouldLog(key, float.PositiveInfinity);

        /// <summary>
        /// 清空"只报一次"状态与计数器。**仅供离线自检宿主**（同一进程里跑多个场景用例）：
        /// 生产流程**没有**调用点（素材到位后这些告警本来就不会再出现）。
        /// <para>引擎只提供**整体**清空（<see cref="LogThrottle.Reset"/>，时间口径 + 计数口径一起清），
        /// 没有"按 key 清"的入口 ⇒ 这里会把 `Log` / 其他模块的限频记录一并清掉。宿主用例本来就要求
        /// 干净起点，故可接受；已在回报中登记。</para>
        /// </summary>
        internal static void ResetForTest()
        {
            LogThrottle.Reset();
            MissingWarnCount = 0;
            UnregisteredWarnCount = 0;
            ThrottledWarnCount = 0;
        }
    }
}
