// ─────────────────────────────────────────────────────────────────────────────
// 自检宿主专用：**引擎门面替身**（不是引擎本体，不参与 Unity 打包）
//
// 只提供 `Diablo2/Assets/Scripts/{Core,Def,Module/Contracts,Module/Combat/SfxKeys,Module/Audio}`
// 及其依赖实际引用到的引擎成员，**签名逐条对齐真实引擎**（出处见每条注释）。
// **一旦真实引擎改签名，本文件会编译报错** —— 这就是它存在的意义（覆盖率哨兵）。
// ─────────────────────────────────────────────────────────────────────────────

using System;
using System.Collections.Generic;
using UnityEngine;

namespace CloverEngine
{
    // ── 日志 ────────────────────────────────────────────────────────────────
    /// <summary>`Runtime/Core/Logger.cs`。</summary>
    public interface ILogger
    {
        void Info(string tag, string msg);
        void Warn(string tag, string msg);
        void Error(string tag, string msg, Exception ex = null);
        void Debug(string tag, string msg);
        void Fatal(string tag, string msg, Exception ex = null);
    }

    /// <summary>自检宿主用的控制台日志（默认实现；自检主流程会换成计数版）。</summary>
    public sealed class ConsoleLogger : ILogger
    {
        public void Info(string tag, string msg) { Console.WriteLine("[INFO ] [" + tag + "] " + msg); }
        public void Warn(string tag, string msg) { Console.WriteLine("[WARN ] [" + tag + "] " + msg); }
        public void Error(string tag, string msg, Exception ex = null) { Console.WriteLine("[ERROR] [" + tag + "] " + msg); }
        public void Debug(string tag, string msg) { }
        public void Fatal(string tag, string msg, Exception ex = null) { Console.WriteLine("[FATAL] [" + tag + "] " + msg); }
    }

    // ── 事件总线 ────────────────────────────────────────────────────────────
    /// <summary>`Runtime/Core/Event.cs:10`（子集）。</summary>
    public interface IEventBus
    {
        void On(string eventName, Action handler);
        void On<T>(string eventName, Action<T> handler);
        void Off(string eventName, Action handler);
        void Off<T>(string eventName, Action<T> handler);
        void Emit(string eventName);
        void Emit<T>(string eventName, T arg1);
    }

    /// <summary>自检宿主用的事件总线（与引擎同样按事件名 + DynamicInvoke 派发）。</summary>
    public sealed class ConsoleEventBus : IEventBus
    {
        private readonly Dictionary<string, List<Delegate>> _handlers = new Dictionary<string, List<Delegate>>();

        public void On(string eventName, Action handler) { Add(eventName, handler); }
        public void On<T>(string eventName, Action<T> handler) { Add(eventName, handler); }
        public void Off(string eventName, Action handler) { Remove(eventName, handler); }
        public void Off<T>(string eventName, Action<T> handler) { Remove(eventName, handler); }
        public void Emit(string eventName) { Invoke(eventName, Array.Empty<object>()); }
        public void Emit<T>(string eventName, T arg1) { Invoke(eventName, new object[] { arg1 }); }

        /// <summary>该事件当前的监听器数量（自检"接线/注销"用）。</summary>
        public int HandlerCount(string eventName)
            => _handlers.TryGetValue(eventName, out var list) ? list.Count : 0;

        private void Add(string name, Delegate d)
        {
            if (!_handlers.TryGetValue(name, out var list)) { list = new List<Delegate>(); _handlers[name] = list; }
            list.Add(d);
        }

        private void Remove(string name, Delegate d)
        {
            if (_handlers.TryGetValue(name, out var list)) list.Remove(d);
        }

        private void Invoke(string name, object[] args)
        {
            if (!_handlers.TryGetValue(name, out var list) || list.Count == 0) return;
            var snapshot = list.ToArray();
            for (var i = 0; i < snapshot.Length; i++) snapshot[i].DynamicInvoke(args);
        }
    }

    // ── 设置 ────────────────────────────────────────────────────────────────
    /// <summary>`Runtime/Core/Setting.cs:11`（子集）。</summary>
    public interface ISetting
    {
        T Get<T>(string key, T defaultValue = default);
        void Set<T>(string key, T value);
        void Save();
        void Load();
        void Delete(string key);
        void DeleteAll();
    }

    // ── 资源 ────────────────────────────────────────────────────────────────
    /// <summary>`Runtime/Core/Contracts.cs:998`（子集）。</summary>
    public interface IResourceManager
    {
        void LoadAsset<T>(string path, Action<T> callback) where T : UnityEngine.Object;
        T TryGet<T>(string path) where T : UnityEngine.Object;

        //   签名逐字对齐 `clover-client-unity-engine/Runtime/Core/Contracts.cs`（本文件是覆盖率哨兵）。
        bool Exists(string path);
        T[] LoadAll<T>(string path) where T : UnityEngine.Object;
    }

    // ── 声音 ────────────────────────────────────────────────────────────────
    /// <summary>`Runtime/Core/PresentationContracts.cs:281`。</summary>
    public enum SoundGroup
    {
        BGM = 0,
        SFX = 1,
        Voice = 2
    }

    /// <summary>`Runtime/Core/PresentationContracts.cs:291`（子集；签名逐条对齐 `Sound.cs`）。</summary>
    public interface ISoundManager
    {
        void PlayBGM(string clipName, float fadeTime = 0.5f);
        void StopBGM(float fadeTime = 0.5f);
        void PlaySFX(string clipName);
        void PlaySFXAt(string clipName, Vector3 position);
        void PlayVoice(string clipName);
        void StopAll();
        void SetVolume(SoundGroup group, float volume);
        float GetVolume(SoundGroup group);
        void SetMute(SoundGroup group, bool mute);
    }

    // ── 输入键位 ────────────────────────────────────────────────────────────
    /// <summary>
    /// `Runtime/Core/Input.cs:14`（**顺序与取值必须与引擎一致**）。
    /// 本宿主编译 `Def/GameKeyAlias.cs`（它的常量值 = 引擎 `GameKey`）⇒ 必须有此枚举。
    /// </summary>
    public enum GameKey
    {
        None = 0,
        A, B, C, D, E, F, G, H, I, J, K, L, M,
        N, O, P, Q, R, S, T, U, V, W, X, Y, Z,
        Num0, Num1, Num2, Num3, Num4, Num5, Num6, Num7, Num8, Num9,
        LeftArrow, RightArrow, UpArrow, DownArrow,
        Space, Enter, KeypadEnter, Escape, Backspace, Tab,
        LeftShift, RightShift, LeftCtrl, RightCtrl, LeftAlt, RightAlt,
        F1, F2, F3, F4, F5, F6, F7, F8, F9, F10, F11, F12,
        Semicolon, Comma, Period, Slash, Backslash, Minus, Equals, Plus,
        MouseLeft, MouseRight, MouseMiddle
    }

    // ── 引导类 ──────────────────────────────────────────────────────────────
    /// <summary>`Runtime/Core/Game.cs:107`（门面替身；字段名/类型与引擎一致）。</summary>
    public static class Game
    {
        public static ILogger Logger = new ConsoleLogger();
        public static IEventBus Event;
        public static ISetting Setting;
        public static IResourceManager Res;
        public static ISoundManager Sound;
        public static bool IsRunning;
    }
}
