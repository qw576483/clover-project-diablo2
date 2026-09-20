// ─────────────────────────────────────────────────────────────────────────────
// 自检宿主专用：**引擎门面替身**（不是引擎本体，不参与 Unity 打包）
//
// 只提供 `Diablo2/Assets/Scripts/**` 实际引用到的引擎成员，**签名逐条对齐真实引擎**
// （出处写在每条注释里）：一旦真实引擎改签名，本文件会**编译报错** —— 这就是它存在的意义
// （覆盖率哨兵）。做法与 `tools/mapcheck/shim/EngineShim.cs`、`tools/flowcheck/shim/EngineShim.cs` 一致，
// 本文件在其基础上增补 `WorldHpBar`（`Module/View/ViewModule.cs` 用它做头顶血条）。
// ─────────────────────────────────────────────────────────────────────────────

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace CloverEngine
{
    // ── 日志 ────────────────────────────────────────────────────────────────
    /// <summary>`Runtime/Core/Logger.cs`（Game.Logger 的类型）。</summary>
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
        public void Error(string tag, string msg, Exception ex = null) { Console.WriteLine("[ERROR] [" + tag + "] " + msg + (ex != null ? " | " + ex.Message : "")); }
        public void Debug(string tag, string msg) { Console.WriteLine("[DEBUG] [" + tag + "] " + msg); }
        public void Fatal(string tag, string msg, Exception ex = null) { Console.WriteLine("[FATAL] [" + tag + "] " + msg + (ex != null ? " | " + ex.Message : "")); }
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
    /// 自检宿主用的事件总线。
    /// ★ **派发顺序必须与真引擎一致**（`Runtime/Core/Event.cs:141-143` + `:319-343`）：
    ///   同为 priority 0 时「**后注册先执行**」、priority 大者先执行。
    ///   老版本这里按注册顺序正序派发 ⇒ 与真引擎相反，于是"订阅顺序假设写反了"这类 bug
    ///   在离线宿主里测不出来（agent-14 §B 现象 3 正是被它漏掉的）。
    /// </summary>
    public sealed class ConsoleEventBus : IEventBus
    {
        private struct HandlerEntry
        {
            public Delegate Handler;
            public int Priority;
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
            var idx = 0;
            while (idx < list.Count && list[idx].Priority <= priority) idx++;   // 与引擎的 Insert 一致
            list.Insert(idx, new HandlerEntry { Handler = d, Priority = priority });
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

    // ── 定时器 / 设置 ───────────────────────────────────────────────────────
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
    public enum UILayer { Background = 0, Normal = 1, Popup = 2, Top = 3, System = 4 }

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

    /// <summary>`Runtime/Core/PresentationContracts.cs:161`。</summary>
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
    /// 且 `App/Bootstrap.cs` 注册它）。
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

    /// <summary>
    /// `Runtime/Presentation/UIWidgets.cs:928` **世界空间头顶血条** —— 业务用它做怪物血条
    /// （`Module/View/ViewModule.cs`）。常量与签名逐条对齐：`DefaultWidth=1.0` / `DefaultHeight=0.12` /
    /// `DefaultYOffset=2.15` / `Create(Transform,float,float,float,string)` / `SetHp(float,float)` /
    /// `SetRatio` / `SetVisible` / `SetYOffset`。
    /// <para>宿主里 `Create` 返回 null（非 Unity 进程建不了 Quad）⇒ 只用于**编译期**校验签名。</para>
    /// </summary>
    public sealed class WorldHpBar : MonoBehaviour
    {
        public const float DefaultWidth = 1.0f;
        public const float DefaultHeight = 0.12f;
        public const float DefaultYOffset = 2.15f;

        public float Ratio => 1f;
        public float Hp => 1f;
        public float MaxHp => 1f;

        public static WorldHpBar Create(Transform target, float width = DefaultWidth,
            float height = DefaultHeight, float yOffset = DefaultYOffset, string tag = "HpBar")
        {
            return null;
        }

        public void SetHp(float hp, float maxHp) { }
        public void SetRatio(float ratio) { }
        public void SetVisible(bool visible) { }
        public void SetYOffset(float yOffset) { }
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
        void LoadAsset<T>(string path, Action<T> callback) where T : UnityEngine.Object;
        T TryGet<T>(string path) where T : UnityEngine.Object;

        // ★ agent-34（引擎下沉 A3）：引擎 `IResourceManager` 新增的两个**同步**入口。
        //   `UI/{UiArt,D2Text,D2Icon}.cs` / `Core/ClientConfig.cs` 已从「直连 Resources」切到它们
        //   （验收表 E1 例外消失）⇒ 替身必须跟着长出来（签名逐字对齐，本文件是覆盖率哨兵）。
        bool Exists(string path);
        T[] LoadAll<T>(string path) where T : UnityEngine.Object;
    }

    /// <summary>`Runtime/Core/PresentationContracts.cs:281`。</summary>
    public enum SoundGroup { BGM = 0, SFX = 1, Voice = 2 }

    /// <summary>`Runtime/Core/PresentationContracts.cs:291`（子集）。</summary>
    public interface ISoundManager
    {
        void PlayBGM(string clipName, float fadeTime = 0.5f);
        void PlaySFX(string clipName);

        /// <summary>`Runtime/Core/PresentationContracts.cs:291`（`Module/Audio/AudioModule.cs:275` 用它做世界坐标音效）。</summary>
        void PlaySFXAt(string clipName, Vector3 position);

        /// <summary>`Runtime/Core/PresentationContracts.cs:291`（`AudioModule.cs:157` 用它换 BGM）。</summary>
        void StopBGM(float fadeTime = 0.5f);

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

        /// <summary>`Runtime/Core/Input.cs:101-107`（`Module/Input/InputReader.cs:95` 用它判"松开"）。</summary>
        bool GetMouseButtonUp(int button);

        /// <summary>`Runtime/Core/Input.cs`（`InputReader` 与 `CameraRig.cs:503` 用它做滚轮缩放）。</summary>
        float GetAxis(string axis, bool raw = false);

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

        /// <summary>`Runtime/Core/Game.cs:341`（`Game.Config`）；`Module/Save/SaveModule.cs:96` 读它的 `SettingDir`
        /// 拼槽位目录 —— agent-37 的 A6 下沉后本宿主才第一次需要它（`# direct-fix:` agent-38 补）。</summary>
        public static GameConfig Config;

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
