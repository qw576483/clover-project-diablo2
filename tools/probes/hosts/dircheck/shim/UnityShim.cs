// ─────────────────────────────────────────────────────────────────────────────
// E-core-19 最小复现宿主：**外部符号替身**（只补非 Unity 进程里跑不了的东西）
//
// 为什么需要：
//   · `Runtime/Core/LogThrottle.cs` 引用 `Game.Logger`（引擎门面）与 `UnityEngine.Time`；
//   · 真身 `UnityEngine.Time` 是原生 ECall，非 Unity 进程读它会抛 SecurityException。
// 被测逻辑（`IsoLayout.DirectionTo/DirectionDelta`）**一行都没有替身** —— 用的是真实源码。
// ─────────────────────────────────────────────────────────────────────────────

using System;

namespace CloverEngine
{
    /// <summary>`Runtime/Core/Logger.cs` 的 `ILogger`（签名逐条对齐真实引擎）。</summary>
    public interface ILogger
    {
        void Info(string tag, string msg);
        void Warn(string tag, string msg);
        void Error(string tag, string msg, Exception ex = null);
        void Debug(string tag, string msg);
        void Fatal(string tag, string msg, Exception ex = null);
    }

    /// <summary>控制台日志（宿主自用）。</summary>
    public sealed class ConsoleLogger : ILogger
    {
        public void Info(string tag, string msg) { Console.WriteLine("[INFO ] [" + tag + "] " + msg); }
        public void Warn(string tag, string msg) { Console.WriteLine("[WARN ] [" + tag + "] " + msg); }
        public void Error(string tag, string msg, Exception ex = null) { Console.WriteLine("[ERROR] [" + tag + "] " + msg); }
        public void Debug(string tag, string msg) { Console.WriteLine("[DEBUG] [" + tag + "] " + msg); }
        public void Fatal(string tag, string msg, Exception ex = null) { Console.WriteLine("[FATAL] [" + tag + "] " + msg); }
    }

    /// <summary>`Runtime/Core/Game.cs:107` 的门面替身（只列本用例用到的成员）。</summary>
    public static class Game
    {
        public static ILogger Logger = new ConsoleLogger();
    }
}

// ── Time（替身必须待在 UnityEngine 命名空间里，与真身同名 ⇒ 源码优先）───────────
namespace UnityEngine
{
    /// <summary>`UnityEngine.Time.realtimeSinceStartup` 的替身（本用例不依赖时间）。</summary>
    public static class Time
    {
        public static float realtimeSinceStartup => 0f;
    }
}
