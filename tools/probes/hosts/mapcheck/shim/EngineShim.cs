// ─────────────────────────────────────────────────────────────────────────────
// 自检宿主专用：**引擎门面替身**（不是引擎本体，不参与 Unity 打包）
//
// 只提供 `Diablo2/Assets/Scripts/**` 实际引用到的引擎成员，签名逐条对齐：
//   · `ILogger`            ← Runtime/Core/Contracts.cs（Game.Logger 类型）
//   · `IEventBus`          ← Runtime/Core/Event.cs:10
//   · `IResourceManager`   ← Runtime/Core/Contracts.cs:998（LoadAsset:1006 / TryGet:1050）
//   · `Game`（Logger/Event/Res/UI…）← Runtime/Core/Game.cs
//
// 一旦真实签名叫法变了，本文件会**编译报错** —— 这就是它存在的意义（覆盖率哨兵）。
// ─────────────────────────────────────────────────────────────────────────────

using System;
using System.Collections.Generic;
using UnityEngine;

namespace CloverEngine
{
    /// <summary>引擎日志接口替身。</summary>
    public interface ILogger
    {
        void Info(string tag, string msg);
        void Warn(string tag, string msg);
        void Error(string tag, string msg, Exception ex = null);
        void Debug(string tag, string msg);
        void Fatal(string tag, string msg, Exception ex = null);
    }

    /// <summary>把引擎日志转发到控制台（自检宿主用）。</summary>
    public sealed class ConsoleLogger : ILogger
    {
        public void Info(string tag, string msg) { Console.WriteLine("[INFO ] [" + tag + "] " + msg); }
        public void Warn(string tag, string msg) { Console.WriteLine("[WARN ] [" + tag + "] " + msg); }
        public void Error(string tag, string msg, Exception ex = null) { Console.WriteLine("[ERROR] [" + tag + "] " + msg + (ex != null ? " | " + ex.Message : "")); }
        public void Debug(string tag, string msg) { Console.WriteLine("[DEBUG] [" + tag + "] " + msg); }
        public void Fatal(string tag, string msg, Exception ex = null) { Console.WriteLine("[FATAL] [" + tag + "] " + msg + (ex != null ? " | " + ex.Message : "")); }
    }

    /// <summary>引擎事件总线接口替身（子集）。</summary>
    public interface IEventBus
    {
        void On(string eventName, Action handler);
        void On<T>(string eventName, Action<T> handler);
        void Off(string eventName, Action handler);
        void Off<T>(string eventName, Action<T> handler);
        void Emit(string eventName);
        void Emit<T>(string eventName, T arg1);
    }

    /// <summary>把事件打印到控制台的事件总线（自检宿主用）。</summary>
    public sealed class ConsoleEventBus : IEventBus
    {
        private readonly Dictionary<string, List<Delegate>> _handlers = new Dictionary<string, List<Delegate>>();

        public void On(string eventName, Action handler) { Add(eventName, handler); }
        public void On<T>(string eventName, Action<T> handler) { Add(eventName, handler); }

        public void Off(string eventName, Action handler) { Remove(eventName, handler); }
        public void Off<T>(string eventName, Action<T> handler) { Remove(eventName, handler); }

        public void Emit(string eventName)
        {
            Console.WriteLine("[EVENT] " + eventName);
            Invoke(eventName, Array.Empty<object>());
        }

        public void Emit<T>(string eventName, T arg1)
        {
            Console.WriteLine("[EVENT] " + eventName + " arg=" + arg1);
            Invoke(eventName, new object[] { arg1 });
        }

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

    /// <summary>引擎资源管理接口替身（子集）。</summary>
    public interface IResourceManager
    {
        void LoadAsset<T>(string path, Action<T> callback) where T : UnityEngine.Object;
        T TryGet<T>(string path) where T : UnityEngine.Object;

        //   签名逐字对齐 `clover-client-unity-engine/Runtime/Core/Contracts.cs`（本文件是覆盖率哨兵）。
        bool Exists(string path);
        T[] LoadAll<T>(string path) where T : UnityEngine.Object;
    }

    /// <summary>永远取不到资源的管理器（自检宿主无素材 ⇒ 走纯色占位分支）。</summary>
    public sealed class EmptyResourceManager : IResourceManager
    {
        public void LoadAsset<T>(string path, Action<T> callback) where T : UnityEngine.Object
        {
            Console.WriteLine("[RES  ] LoadAsset<" + typeof(T).Name + "> miss: " + path);
            if (callback != null) callback(null);
        }

        public T TryGet<T>(string path) where T : UnityEngine.Object { return null; }

        /// <summary>宿主无素材 ⇒ 恒「不存在」（契约语义：只回答，不加载不驻留）。</summary>
        public bool Exists(string path) { return false; }

        /// <summary>宿主无素材 ⇒ 恒空数组（契约语义：取不到就是空数组，宿主不抛）。</summary>
        public T[] LoadAll<T>(string path) where T : UnityEngine.Object { return Array.Empty<T>(); }
    }

    /// <summary>`Game` 门面替身 —— 只暴露本模块用到的 `Logger` / `Event` / `Res`。</summary>
    public static class Game
    {
        public static ILogger Logger = new ConsoleLogger();
        public static IEventBus Event = new ConsoleEventBus();
        public static IResourceManager Res = new EmptyResourceManager();
    }
}
