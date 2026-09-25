// ─────────────────────────────────────────────────────────────────────────────
// savecheck 自检宿主专用：**引擎门面替身**（不是引擎本体，不参与 Unity 打包）
//
// 复用同目录族的 `itemcheck/shim/EngineShim.cs`（签名逐条对齐真实引擎、出处见各条注释；
// **一旦真实引擎改签名，本文件会编译报错** —— 这就是它存在的意义：覆盖率哨兵）。
//     它按 `LogLevel` 分流 Warn/Error（`Setting.cs:440-461`）。
// 做法与 `tools/probes/hosts/mapcheck/shim/EngineShim.cs` 一致。
// ─────────────────────────────────────────────────────────────────────────────

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

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

    /// <summary>自检宿主用的控制台日志。</summary>
    public sealed class ConsoleLogger : ILogger
    {
        public void Info(string tag, string msg) { Console.WriteLine("[INFO ] [" + tag + "] " + msg); }
        public void Warn(string tag, string msg) { Console.WriteLine("[WARN ] [" + tag + "] " + msg); }
        public void Error(string tag, string msg, Exception ex = null) { Console.WriteLine("[ERROR] [" + tag + "] " + msg); }
        public void Debug(string tag, string msg) { Console.WriteLine("[DEBUG] [" + tag + "] " + msg); }
        public void Fatal(string tag, string msg, Exception ex = null) { Console.WriteLine("[FATAL] [" + tag + "] " + msg); }
    }

    /// <summary>
    /// 日志级别。出处 `Runtime/Core/Logger.cs:28-`（`Debug = 0` 起，`Error` 及以上走 Error 通道）。
    /// 本宿主新增：真实 `Runtime/Core/Setting.cs` 的 `Report(LogLevel, ...)` 要它。
    /// </summary>
    public enum LogLevel
    {
        Debug = 0,
        Info = 1,
        Warn = 2,
        Error = 3,
        Fatal = 4
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

    // ── 状态机 ──────────────────────────────────────────────────────────────
    /// <summary>`Runtime/Core/Fsm.cs:9`。</summary>
    public interface IFsm
    {
        void RegisterState(string state, Action onEnter = null, Action<float> onTick = null, Action onExit = null);
        void Transition(string toState);
        void AddTransition(string trigger, string toState);
        void Trigger(string trigger);
        void Force(string state);
        void Tick(float dt);
        void OnChange(Action<string, string> handler);
        void OffChange(Action<string, string> handler);
        string Current { get; }
    }

    // ── 定时器 ──────────────────────────────────────────────────────────────
    /// <summary>`Runtime/Core/Timer.cs:10`（子集）。</summary>
    public interface ITimer
    {
        long After(float delay, Action callback);
        long AfterUnscaled(float delay, Action callback);
        long Every(float interval, Action callback);
        long EveryUnscaled(float interval, Action callback);
        void Stop(long id);
        void StopNamed(string name);
        void StopScope(string scope);
        void StopAll();
        void Tick(float dt);
    }

    // ── 设置 ────────────────────────────────────────────────────────────────
    // 本宿主**链了真实** `Runtime/Core/Setting.cs`（它同时定义 `ISetting` + `Setting`）
    //    ⇒ 这里**不再**放 `ISetting` 替身（否则 CS0101 重复定义）。这与 itemcheck 不同：

    // ── UI ─────────────────────────────────────────────────────────────────
    /// <summary>`Runtime/Core/PresentationContracts.cs:20`。</summary>
    public enum UILayer
    {
        Background = 0,
        Normal = 1,
        Popup = 2,
        Top = 3,
        System = 4
    }

    /// <summary>`Runtime/Core/PresentationContracts.cs:32`。</summary>
    public interface IUIPanel
    {
        string PanelName { get; }
        UILayer Layer { get; }
        void OnOpen(object param);
        void OnClose();
        void OnUpdate(float dt);
        GameObject Root { get; }
    }

    /// <summary>`Runtime/Core/PresentationContracts.cs:161`（默认值一致）。</summary>
    public abstract class UIPanel : MonoBehaviour, IUIPanel
    {
        public virtual string PanelName => GetType().Name;
        public virtual UILayer Layer => UILayer.Normal;
        public GameObject Root => gameObject;
        public virtual void OnOpen(object param) { }
        public virtual void OnClose() { }
        public virtual void OnUpdate(float dt) { }
    }

    /// <summary>`Runtime/Core/PresentationContracts.cs:51`（子集）。</summary>
    public interface IUIManager
    {
        void Open<T>(object param = null) where T : class, IUIPanel;
        void Close<T>() where T : class, IUIPanel;
        void Close(string panelName);
        void CloseAll();
        T Get<T>() where T : class, IUIPanel;
        bool IsOpen<T>() where T : class, IUIPanel;
        void Toast(string text, float duration = 2f);
        void FloatText(Vector3 worldPos, string text, Color? color = null, float duration = 1.2f);
        void ShowLoading(string text = null);
        void HideLoading();
        bool IsLoading { get; }
        void Confirm(string title, string message, Action onConfirm, Action onCancel = null,
            string confirmText = null, string cancelText = null);
        void Tick(float dt);
    }

    /// <summary>`Runtime/Presentation/UIWidgets.cs:31`（UIFactory 公开面）。</summary>
    public static class UIFactory
    {
        public static Font DefaultFont() => null;
        public static RectTransform CreateNode(string name, Transform parent) => null;
        public static void Stretch(RectTransform rt) { }
        public static RectTransform CreateCentered(string name, Transform parent, Vector2 size, Vector2 pos) => null;
        public static Image CreatePanel(string name, Transform parent, Color color, bool raycastTarget) => null;
        public static Text CreateText(string name, Transform parent, string content, int fontSize,
            TextAnchor alignment, Color color, bool raycastTarget = false) => null;
        public static Image CreateButton(string name, Transform parent, string label, Vector2 size, Vector2 pos,
            Color bg, Action onClick) => null;
        public static Camera UICamera() => null;
    }

    // ── 场景 / 实体 / 对象池 / 资源 / 声音 / 输入 ─────────────────────────
    /// <summary>`Runtime/Core/PresentationContracts.cs:187`。</summary>
    public interface ISceneManager
    {
        string CurrentScene { get; }
        void Load(string sceneName, Action<float> progress = null, Action onDone = null);
        void Unload(string sceneName, Action onDone = null);
        void OnSceneLoaded(Action<string> handler);
        /// <summary>`Runtime/Core/PresentationContracts.cs:198`。</summary>
        void OnSceneUnloaded(Action<string> handler);
    }

    /// <summary>`Runtime/Core/EntityPool.cs`。</summary>
    public class EntityInfo { }

    /// <summary>`Runtime/Core/EntityPool.cs`（子集）。</summary>
    public interface IEntityManager
    {
        void ClearAll();
        IEnumerable<EntityInfo> GetAll();
    }

    /// <summary>`Runtime/Presentation/ObjectPool.cs:99`（子集）。</summary>
    public interface IObjectPool
    {
        void ClearAll();
    }

    /// <summary>`Runtime/Core/ResourceContracts.cs`（子集）。</summary>
    public interface IResourceManager
    {
        void LoadAsset<T>(string path, Action<T> cb) where T : UnityEngine.Object;
        T TryGet<T>(string path) where T : UnityEngine.Object;

        //   签名逐字对齐 `clover-client-unity-engine/Runtime/Core/Contracts.cs`（本文件是覆盖率哨兵）。
        bool Exists(string path);
        T[] LoadAll<T>(string path) where T : UnityEngine.Object;
    }

    /// <summary>`Runtime/Core/PresentationContracts.cs:281`。</summary>
    public enum SoundGroup
    {
        BGM = 0,
        SFX = 1,
        Voice = 2
    }

    /// <summary>`Runtime/Core/PresentationContracts.cs:291`（子集）。</summary>
    public interface ISoundManager
    {
        void PlayBGM(string clipName, float fadeTime = 0.5f);
        void PlaySFX(string clipName);
        void StopAll();
        void SetVolume(SoundGroup group, float volume);
        float GetVolume(SoundGroup group);
        void SetMute(SoundGroup group, bool mute);
    }

    /// <summary>`Runtime/Core/Input.cs:14`（**顺序与取值必须与引擎一致**）。</summary>
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

    /// <summary>`Runtime/Core/Input.cs:66`（子集）。</summary>
    public interface IInputManager
    {
        bool Available { get; }
        bool IsLocked { get; }
        bool GetKey(GameKey key);
        bool GetKeyDown(GameKey key);
        bool GetKeyUp(GameKey key);
        bool GetMouseButton(int button);
        bool GetMouseButtonDown(int button);
        Vector3 MousePosition { get; }
    }

    // ── 引导类 ──────────────────────────────────────────────────────────────
    /// <summary>`Runtime/Core/Game.cs:10`（只列业务用到的字段）。</summary>
    public class GameConfig
    {
        public string ServerAddr;
        public bool UseTls;
        public string LogDir;
        public string SettingDir;
        public string ResourceRoot;
        public string DataDir;
    }

    /// <summary>`Runtime/Core/Game.cs:107`（门面替身；字段名/类型与引擎一致）。</summary>
    public static class Game
    {
        /// <summary>
        /// `Runtime/Core/Game.cs:341`：`public static GameConfig Config { get; private set; }`，由
        /// <see cref="Launch"/> 在 `:370` 赋值（`Config = config;`）。
        /// `Module/Save/SaveModule.cs` 的 `Store` 就是从它拼 `<SettingDir>/saves/`）；
        /// 真实引擎本就有这一项，故补进替身不算放宽契约。
        /// </summary>
        public static GameConfig Config { get; private set; }

        public static ILogger Logger = new ConsoleLogger();
        public static IEventBus Event = new ConsoleEventBus();
        public static IFsm Fsm;
        public static ITimer Timer;
        public static ISetting Setting;
        public static IUIManager UI;
        public static ISceneManager Scene;
        public static IEntityManager Entity;
        public static IObjectPool Pool;
        public static IResourceManager Res;
        public static ISoundManager Sound;
        public static IInputManager Input;
        public static bool IsRunning;

        public static void Launch(GameConfig config) { Config = config; IsRunning = true; }
        public static void Tick(float dt) { }
        public static void Shutdown() { IsRunning = false; }
    }

    /// <summary>`Runtime/Resource/CloverRes.cs:26`。</summary>
    public static class CloverRes
    {
        public static void Init(string root) { }
    }

    /// <summary>`Runtime/Presentation/CloverInput.cs:22`。</summary>
    public static class CloverInput
    {
        public static void Init() { }
        public static bool Ready => false;
    }
}
