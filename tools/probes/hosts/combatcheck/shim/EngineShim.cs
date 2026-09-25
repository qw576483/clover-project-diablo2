// ─────────────────────────────────────────────────────────────────────────────
// 自检宿主专用：**引擎门面替身**（不是引擎本体，不参与 Unity 打包）
//
// 只提供 `Diablo2/Assets/Scripts/**` 实际引用到的引擎成员，**签名逐条对齐真实引擎**
// （出处写在每条注释里）：一旦真实引擎改签名，本文件会**编译报错** —— 这就是它存在的意义
// （覆盖率哨兵）。做法与 `tools/probes/hosts/mapcheck/shim/EngineShim.cs`、`tools/probes/hosts/flowcheck/shim/EngineShim.cs` 一致，
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
        void Off(string eventName, Action handler);
        void Off<T>(string eventName, Action<T> handler);
        void Emit(string eventName);
        void Emit<T>(string eventName, T arg1);
    }

    /// <summary>自检宿主用的事件总线（同步派发，与引擎同语义）。</summary>
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
    // `IFsm` / `Fsm` 由引擎真实源码 `Runtime/Core/Fsm.cs` 提供（纯 C#、无 Unity 依赖 ⇒ 本宿主直接链它，
    // `Game.NewFsm()` 建出来的就是引擎那棵真状态机）。

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
    // `UILayer` / `IUIPanel` / `UIPanel` / `IUIManager` 的定义在引擎真实源码
    // `Runtime/Core/PresentationContracts.cs`（本宿主已把它链进编译集）⇒ shim 不再重复声明：
    //   重复声明 = CS0101，且会让"引擎改了这几个契约"在宿主编译期**不可见**（哨兵失效）。

    /// <summary>`Runtime/Presentation/UIWidgets.cs:34`（UIFactory 的**基础半**：默认字体 / 建件；
    /// 布局与条状控件的扩展半在引擎 `Runtime/Presentation/UIWidgetControls.cs`，本宿主已链）。
    /// 引擎两份都是 `partial` ⇒ 这里也必须声明 `partial` 才与它们合并为同一个类型。</summary>
    public static partial class UIFactory
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
    // `ISceneManager` / `SoundGroup` / `ISoundManager` / `IResourceManager` 同样由引擎真实源码提供
    // （`Runtime/Core/PresentationContracts.cs` / `Runtime/Core/Contracts.cs`，两者都在本宿主编译集里）。

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

        /// <summary>`Runtime/Core/Game.cs:193`（`Module/Monster/MonsterAi.cs:150` 用它给每只怪建一棵独立状态机）。</summary>
        public static IFsm NewFsm() => new Fsm();

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
