// ─────────────────────────────────────────────────────────────────────────────
// 自检宿主专用：**引擎门面替身**（不是引擎本体，不参与 Unity 打包）
//
// 只提供 `Diablo2/Assets/Scripts/{Module/Flow,UI,App}` 及其依赖实际引用到的引擎成员，
// 签名逐条对齐真实引擎（出处见每条注释）。**一旦真实引擎改签名，本文件会编译报错**
// —— 这就是它存在的意义（覆盖率哨兵）。做法与 `tools/mapcheck/shim/EngineShim.cs` 一致。
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

    /// <summary>
    /// 自检宿主用的控制台日志（**同时留内存副本**）。
    /// 内存副本的用途：站点日志是验收硬指标（「`[Flow] → <站点>` 每站点恰好一条」），
    /// 只断言"状态对了"是抓不到"多打了一条"的 ⇒ 必须能**数日志行**。
    /// </summary>
    public sealed class ConsoleLogger : ILogger
    {
        /// <summary>全部日志行（形如 `[INFO ] [Flow] → Boot`）。</summary>
        public static readonly List<string> Lines = new List<string>();

        /// <summary>清空（宿主分段跑时用）。</summary>
        public static void Clear() { Lines.Clear(); }

        /// <summary>包含该片段的日志行数（验收计数用）。</summary>
        public static int CountOf(string fragment)
        {
            var n = 0;
            for (var i = 0; i < Lines.Count; i++)
            {
                if (Lines[i].IndexOf(fragment, StringComparison.Ordinal) >= 0) n++;
            }
            return n;
        }

        private static void W(string level, string tag, string msg)
        {
            var line = "[" + level + "] [" + tag + "] " + msg;
            Lines.Add(line);
            Console.WriteLine(line);
        }

        public void Info(string tag, string msg) { W("INFO ", tag, msg); }
        public void Warn(string tag, string msg) { W("WARN ", tag, msg); }
        public void Error(string tag, string msg, Exception ex = null) { W("ERROR", tag, msg); }
        public void Debug(string tag, string msg) { W("DEBUG", tag, msg); }
        public void Fatal(string tag, string msg, Exception ex = null) { W("FATAL", tag, msg); }
    }

    // ── 事件总线 ────────────────────────────────────────────────────────────
    /// <summary>`Runtime/Core/Event.cs:10`（子集）。</summary>
    public interface IEventBus
    {
        void On(string eventName, Action handler);
        void On<T>(string eventName, Action<T> handler);
        /// <summary>`Runtime/Core/Event.cs:63`（**有优先级**：priority 大者先执行）。</summary>
        void OnPriority(string eventName, int priority, Action handler);
        void Off(string eventName, Action handler);
        void Off<T>(string eventName, Action<T> handler);
        void Emit(string eventName);
        void Emit<T>(string eventName, T arg1);
    }

    /// <summary>
    /// 自检宿主用的事件总线（按事件名 + `DynamicInvoke` 派发）。
    /// ★ **派发顺序必须与真引擎一致**：`Runtime/Core/Event.cs:141-143` 说明存储是
    ///   「执行顺序的逆序」，`InvokeHandlers` 倒序遍历 ⇒ **同优先级 = 后注册先执行**、
    ///   priority 大者先执行。老版本这里按注册顺序正序派发，与真引擎相反 ——
    ///   于是"订阅顺序假设写反了"这类 bug 在离线宿主里**测不出来**（agent-14 §B 现象 3 就是它漏掉的）。
    /// </summary>
    public sealed class ConsoleEventBus : IEventBus
    {
        private readonly struct HandlerEntry
        {
            public readonly Delegate Handler;
            public readonly int Priority;
            public HandlerEntry(Delegate handler, int priority) { Handler = handler; Priority = priority; }
        }

        private readonly Dictionary<string, List<HandlerEntry>> _handlers = new Dictionary<string, List<HandlerEntry>>();

        public void On(string eventName, Action handler) { Add(eventName, handler, 0); }
        public void On<T>(string eventName, Action<T> handler) { Add(eventName, handler, 0); }
        public void OnPriority(string eventName, int priority, Action handler) { Add(eventName, handler, priority); }
        public void Off(string eventName, Action handler) { Remove(eventName, handler); }
        public void Off<T>(string eventName, Action<T> handler) { Remove(eventName, handler); }
        public void Emit(string eventName) { Invoke(eventName, Array.Empty<object>()); }
        public void Emit<T>(string eventName, T arg1) { Invoke(eventName, new object[] { arg1 }); }

        private void Add(string name, Delegate d, int priority)
        {
            if (!_handlers.TryGetValue(name, out var list)) { list = new List<HandlerEntry>(); _handlers[name] = list; }
            // 与 `Runtime/Core/Event.cs:286-294` 的 Insert 一致：插到第一个 priority 更大的项之前
            var idx = 0;
            while (idx < list.Count && list[idx].Priority <= priority) idx++;
            list.Insert(idx, new HandlerEntry(d, priority));
        }

        private void Remove(string name, Delegate d)
        {
            if (!_handlers.TryGetValue(name, out var list)) return;
            for (var i = 0; i < list.Count; i++)
            {
                if (list[i].Handler != d) continue;
                list.RemoveAt(i);
                return;
            }
        }

        private void Invoke(string name, object[] args)
        {
            if (!_handlers.TryGetValue(name, out var list) || list.Count == 0) return;
            var snapshot = list.ToArray();
            // 倒序 = 「优先级大者先执行、同优先级后注册者先执行」（与真引擎一致）
            for (var i = snapshot.Length - 1; i >= 0; i--) snapshot[i].Handler.DynamicInvoke(args);
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

    /// <summary>`Runtime/Presentation/TextHooks.cs`（E-core-12）：业务可注册的"文字渲染挂钩"。</summary>
    public interface ITextHook
    {
        /// <summary>引擎刚创建了一个 `Text`（文案 / 字号 / 对齐 / 颜色 / 溢出策略均已就位）。</summary>
        void OnTextCreated(Text text);
    }

    /// <summary>
    /// `Runtime/Presentation/TextHooks.cs`（E-core-12）：引擎侧唯一入口（真实实现由 `UIFactory.CreateText`
    /// 末尾调用）。宿主里 `UIFactory.CreateText` 是**空壳**（返回 null：非 Unity 进程建不了节点）⇒
    /// 本入口不会被调到；这两个类型只为**编译期签名校验**存在（`UI/D2EngineTextHook.cs` 实现 `ITextHook`，
    /// 且 `App/Bootstrap.cs` 注册它）。本文件被 `tools/uicheck` 复用 ⇒ 两处一起生效。
    /// </summary>
    public static class TextHooks
    {
        public static ITextHook Current { get; set; }

        public static void NotifyCreated(Text text)
        {
            var hook = Current;
            if (hook == null) return;
            try
            {
                hook.OnTextCreated(text);
            }
            catch (Exception ex)
            {
                // 与引擎同口径：吞掉 + 只 Warn 一次（宿主里从不注册挂钩 ⇒ 这条分支不会产生输出）
                Game.Logger?.Warn("UI", "文字渲染挂钩 OnTextCreated 抛异常（已吞掉）：" + ex.Message);
            }
        }
    }

    // ── 场景 / 实体 / 对象池 / 资源 / 声音 / 输入 ─────────────────────────
    /// <summary>`Runtime/Core/PresentationContracts.cs:187`。</summary>
    public interface ISceneManager
    {
        string CurrentScene { get; }
        void Load(string sceneName, Action<float> progress = null, Action onDone = null);
        void Unload(string sceneName, Action onDone = null);
        /// <summary>`Runtime/Core/PresentationContracts.cs:196`（agent-17 §A 起 `Module/Flow/AppFlow` 用它识别"Stage 场景被重载"）。</summary>
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

        // ★ agent-34（引擎下沉 A3）：引擎 `IResourceManager` 新增这两个**同步**入口
        //   （`Exists` = 只回答"在不在"；`LoadAll` = 会加载、批量取）。
        //   `client/Assets/Scripts/UI/{UiArt,D2Text,D2Icon}.cs` 与 `Core/ClientConfig.cs` 已从
        //   「直连 Unity 的 Resources」切到它们（验收表 E1 例外消失）⇒ 替身必须跟着长出来，
        //   否则业务源码在本宿主编不过 —— 本文件就是覆盖率哨兵。
        //   签名逐字对齐 `clover-client-unity-engine/Runtime/Core/Contracts.cs:1019`。
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

        public static void Launch(GameConfig config) { IsRunning = true; }
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

// ── Time（自检宿主替身；**必须待在 UnityEngine 命名空间里**）───────────────────
namespace UnityEngine
{
    /// <summary>
    /// `UnityEngine.Time` 的替身。真身是**原生 ECall**，在非 Unity 进程里读写会抛
    /// `SecurityException: ECall methods must be packaged into a system module`
    /// ⇒ 离线宿主原先根本走不了 `Pause` 站点（`AppFlow.OnEnterPause/OnExitPause` 要写 `Time.timeScale`）。
    /// 本替身让那两条路径可离线复现（**CS0436**：与引用的 `UnityEngine.CoreModule` 里的同名类型冲突，
    /// **源码优先**；已在 csproj 的 NoWarn 里放行）。
    /// ⚠️ 只覆盖本项目实际用到的 4 个成员；真引擎改签名时这里会编译报错（哨兵作用保留）。
    /// </summary>
    public static class Time
    {
        private static float _unscaled;

        /// <summary>`Module/Flow/AppFlow.cs` 暂停/继续要写它。</summary>
        public static float timeScale { get; set; } = 1f;

        /// <summary>`UI/*` 面板的逐帧动画用。</summary>
        public static float deltaTime => 0.016f;

        /// <summary>`UI/UiArt.cs` 等的动画用。</summary>
        public static float unscaledTime => _unscaled;

        /// <summary>`Core/Log.cs` 的降频闸门用（本宿主另外会注入 `Log.Clock`）。</summary>
        public static float realtimeSinceStartup => _unscaled;

        /// <summary>宿主推进「单调秒」（不随 `timeScale` 变化，语义同 unscaled）。</summary>
        public static void AdvanceUnscaled(float dt) { _unscaled += dt; }
    }
}
