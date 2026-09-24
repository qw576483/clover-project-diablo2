// ─────────────────────────────────────────────────────────────────────────────
// BuildCheck 专用：**引擎门面最小替身**（不是引擎本体，不参与 Unity 打包）
//
// 为什么需要：本宿主链了引擎 `Runtime/Core/LogThrottle.cs`（`Editor/SceneScaffold.cs` 的告警走它），
// 而 `LogThrottle` 只经 `Game.Logger`（引擎 `ILogger`）出日志。引擎自己的 `Game` 门面静态属性是
// `{ get; private set; }` 且 `Launch` 会建真 Logger / EngineRunner（Unity 运行时）⇒ 离线宿主不能编它，
// 只能由本替身提供 `Game.Logger`。签名逐条对齐引擎 `Runtime/Core/Logger.cs` 的 `ILogger`：
// 引擎改了 `ILogger`，`LogThrottle` 会在这里编译报错 —— 这就是它存在的意义（覆盖率哨兵）。
// ─────────────────────────────────────────────────────────────────────────────

using System;

namespace CloverEngine
{
    /// <summary>`Runtime/Core/Logger.cs`（`Game.Logger` 的类型）。</summary>
    public interface ILogger
    {
        void Info(string tag, string msg);
        void Warn(string tag, string msg);
        void Error(string tag, string msg, Exception ex = null);
        void Debug(string tag, string msg);
        void Fatal(string tag, string msg, Exception ex = null);
    }

    /// <summary>写控制台的日志替身（本宿主只编译 Editor 代码，不跑游戏逻辑）。</summary>
    public sealed class ConsoleLogger : ILogger
    {
        public void Info(string tag, string msg) { Console.WriteLine("[INFO ] [" + tag + "] " + msg); }
        public void Warn(string tag, string msg) { Console.WriteLine("[WARN ] [" + tag + "] " + msg); }
        public void Error(string tag, string msg, Exception ex = null) { Console.WriteLine("[ERROR] [" + tag + "] " + msg + (ex != null ? " | " + ex.Message : "")); }
        public void Debug(string tag, string msg) { Console.WriteLine("[DEBUG] [" + tag + "] " + msg); }
        public void Fatal(string tag, string msg, Exception ex = null) { Console.WriteLine("[FATAL] [" + tag + "] " + msg + (ex != null ? " | " + ex.Message : "")); }
    }

    /// <summary>`Runtime/Core/Game.cs` 的最小门面替身（只暴露本宿主编译集真正用到的 `Logger`）。</summary>
    public static class Game
    {
        public static ILogger Logger = new ConsoleLogger();
    }
}
