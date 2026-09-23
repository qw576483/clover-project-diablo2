// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · Module/Audio/AudioLog.cs
// 音频模块的日志出口（tag 固定 = `Audio`，见 `Core/Log.cs` 的 tag 白名单）。
//
// ⛔ **刻意不用 `Core/Log.WarnOnce / WarnThrottled / ErrorOnce`**：
//   那三个入口的降频闸门读了 `UnityEngine.Time.realtimeSinceStartup`（`Core/Log.cs:133`），
//   在**非 Unity 宿主**（`tools/audiocheck` 离线自检、纯 .NET 进程）会抛 `SecurityException`，
//   一抛就把「只报一次」变成「报一次就崩」。所以本类用**私有标志/集合**自己实现"只报一次"。
//
// 「只报一次」的粒度：
//   · **文件缺失** —— 每个键各报一次（素材一次没到位，键数有限 ⇒ 不会刷屏）；
//   · **基础设施缺失**（`Game.Sound` / `Game.Res` / `Game.Setting` / `Game.Event` 为 null）—— 各报一次；
//   · 标志是**静态**的（进程生命周期内一次），与"素材到位前反复请求同一缺失键"这条路径对得上。
//
// 素材到位后这些告警自动消失（探测到文件后不再进缺失分支），**无需改代码**。
// ─────────────────────────────────────────────────────────────────────────────

using System;
using System.Collections.Generic;
using Diablo2.Core;

namespace Diablo2.Module.Audio
{
    /// <summary>音频模块日志（防刷屏由**私有标志**实现，不依赖引擎时钟）。</summary>
    internal static class AudioLog
    {
        /// <summary>日志 tag（`Core/Log.cs` 白名单里的模块名）。</summary>
        public const string Tag = "Audio";

        /// <summary>已就「文件缺失」告警过的 SFX 键。</summary>
        private static readonly HashSet<string> MissingSfxWarned = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>已就「文件缺失」告警过的 BGM 键。</summary>
        private static readonly HashSet<string> MissingBgmWarned = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>已就「未登记的键」告警过的键。</summary>
        private static readonly HashSet<string> UnregisteredWarned = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>已就「同键重复过快被节流」告警过的键（★ 片 C4 新增，每键一条）。</summary>
        private static readonly HashSet<string> ThrottledWarned = new HashSet<string>(StringComparer.Ordinal);

        private static bool _noSoundWarned;
        private static bool _noResWarned;
        private static bool _noSettingWarned;
        private static bool _noEventWarned;
        private static bool _emptyKeyWarned;
        private static bool _nullPayloadWarned;
        private static bool _unknownAreaWarned;
        private static bool _noMapWarned;

        /// <summary>「文件缺失」累计告警条数（自检断言"恰好 1 条"用；正常流程只读）。</summary>
        internal static int MissingWarnCount;

        /// <summary>「未登记键」累计告警条数（自检用）。</summary>
        internal static int UnregisteredWarnCount;

        /// <summary>「同键重复过快」累计告警条数（★ 片 C4，自检用；生产只读）。</summary>
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
            if (!MissingSfxWarned.Add(key)) return;
            MissingWarnCount++;
            Log.Warn(Tag,
                $"音效文件缺失：键=\"{key}\" 期望文件=\"{fileName}\"（路径 {path}）" +
                "⇒ 本次及之后**静默跳过**（只报这一条）；素材到位后自动生效，无需改代码");
        }

        /// <summary>BGM 文件缺失（每键一次）。</summary>
        public static void MissingBgm(string key, string fileName, string path)
        {
            if (!MissingBgmWarned.Add(key)) return;
            MissingWarnCount++;
            Log.Warn(Tag,
                $"BGM 文件缺失：键=\"{key}\" 期望文件=\"{fileName}\"（路径 {path}）" +
                "⇒ 本次及之后**静默跳过**（只报这一条）；素材到位后自动生效，无需改代码");
        }

        /// <summary>
        /// ★ 片 C4：**同一音效键重复过快，本次被节流丢弃**（每键一条，见 `SfxThrottle`）。
        /// <para>为什么不用 `Core/Log.WarnThrottled`：那个入口读 `Time.realtimeSinceStartup`（`Core/Log.cs:133`），
        /// 在离线自检宿主（纯 .NET 进程）会抛 `SecurityException` —— 与本类文件头记的同一个坑；
        /// 这里用私有集合实现"每键一次"，离线宿主也跑得通。</para>
        /// </summary>
        public static void WarnThrottledSfx(string key, float sinceSeconds)
        {
            if (string.IsNullOrEmpty(key)) return;
            if (!ThrottledWarned.Add(key)) return;          // 每个键只留一条痕（防"节流日志自己刷屏"）
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
            if (!UnregisteredWarned.Add((isBgm ? "B:" : "S:") + key)) return;
            UnregisteredWarnCount++;
            Log.Warn(Tag,
                $"音效键未登记：\"{key}\"（{(isBgm ? "BGM" : "SFX")}）不在 `SfxRegistry` 里" +
                "⇒ 仍按「键即资源名」尝试播放；请补登 `Module/Audio/SfxRegistry.cs`");
        }

        /// <summary>`Game.Sound` 未挂载（引擎表现域没起来）。</summary>
        public static void NoSoundManager()
        {
            if (_noSoundWarned) return;
            _noSoundWarned = true;
            Log.Warn(Tag, "`Game.Sound` 为 null（引擎表现域未挂载？）⇒ 所有音效/BGM 调用被跳过（只报这一条）");
        }

        /// <summary>`Game.Res` 未挂载。</summary>
        public static void NoResourceManager()
        {
            if (_noResWarned) return;
            _noResWarned = true;
            Log.Warn(Tag, "`Game.Res` 为 null（CloverRes.Init 未调用？）⇒ 无法探测音频文件是否存在，按未到位处理（只报这一条）");
        }

        /// <summary>`Game.Setting` 未挂载（音量无法持久化）。</summary>
        public static void NoSetting()
        {
            if (_noSettingWarned) return;
            _noSettingWarned = true;
            Log.Warn(Tag, "`Game.Setting` 为 null ⇒ 音量改动只作用于本次会话，不落盘（只报这一条）");
        }

        /// <summary>`Game.Event` 未挂载（触发点无法接线）。</summary>
        public static void NoEventBus()
        {
            if (_noEventWarned) return;
            _noEventWarned = true;
            Log.Warn(Tag, "`Game.Event` 为 null（Game.Launch 未调用？）⇒ 事件触发点未接线，只有直连调用会出声（只报这一条）");
        }

        /// <summary>收到空键。</summary>
        public static void EmptyKey()
        {
            if (_emptyKeyWarned) return;
            _emptyKeyWarned = true;
            Log.Warn(Tag, "收到空音效键 ⇒ 忽略（只报这一条）");
        }

        /// <summary>事件载荷为 null（不该发生：发送方数据异常）。</summary>
        public static void NullPayload(string what)
        {
            if (_nullPayloadWarned) return;
            _nullPayloadWarned = true;
            Log.Warn(Tag, $"事件载荷为 null（{what}）⇒ 忽略该次音效（只报这一条）");
        }

        /// <summary>区域没有对应 BGM 登记（数据异常）。</summary>
        public static void UnknownArea(object area)
        {
            if (_unknownAreaWarned) return;
            _unknownAreaWarned = true;
            Log.Warn(Tag, $"区域「{area}」没有登记的 BGM ⇒ 保持当前曲目（只报这一条）");
        }

        /// <summary>进图时还没收到过 `Events.AreaChanged`（Map 模块未接入 / 未发事件）。</summary>
        public static void NoAreaChanged()
        {
            if (_noMapWarned) return;
            _noMapWarned = true;
            Log.Warn(Tag,
                $"进图（{Events.StageEntered}）前没有收到过 {Events.AreaChanged}" +
                "（IMapModule 未接入或未发该事件？）⇒ BGM 暂按区域 Town 处理（只报这一条）");
        }

        /// <summary>
        /// 清空"只报一次"状态与计数器。**仅供离线自检宿主**（同一进程里跑多个场景用例）：
        /// 生产流程**没有**调用点（素材到位后这些告警本来就不会再出现）。
        /// </summary>
        internal static void ResetForTest()
        {
            MissingSfxWarned.Clear();
            MissingBgmWarned.Clear();
            UnregisteredWarned.Clear();
            ThrottledWarned.Clear();
            _noSoundWarned = false;
            _noResWarned = false;
            _noSettingWarned = false;
            _noEventWarned = false;
            _emptyKeyWarned = false;
            _nullPayloadWarned = false;
            _unknownAreaWarned = false;
            _noMapWarned = false;
            MissingWarnCount = 0;
            UnregisteredWarnCount = 0;
            ThrottledWarnCount = 0;
        }
    }
}
