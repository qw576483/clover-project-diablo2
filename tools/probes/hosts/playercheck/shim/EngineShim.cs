// ─────────────────────────────────────────────────────────────────────────────
// 自检宿主专用：**引擎门面替身**（不是引擎本体，不参与 Unity 打包）
//
// 只提供 `client/Assets/Scripts/**` 里被编译到的文件实际引用到的引擎成员，签名逐条对齐：
//   · `ILogger`      ← Runtime/Core/Contracts.cs（Game.Logger 的类型）
//   · `IEventBus`    ← Runtime/Core/Event.cs:10
//   · `IResourceManager` ← Runtime/Core/Contracts.cs:998（MapView 用）
//   · `IInputManager`← Runtime/Core/Input.cs:66（InputReader 用）
//   · `ISetting`     ← Runtime/Core/Setting.cs:11（PlayerModule 读 WASD 开关）
//   · `Game`         ← Runtime/Core/Game.cs
//
// **一旦真实引擎改了签名，本文件会编译报错** —— 这就是它存在的意义（覆盖率哨兵）。
// 做法与 `tools/mapcheck/shim/EngineShim.cs`（agent-04）、`tools/flowcheck/shim/EngineShim.cs`（agent-05）一致。
//
// ★ `CaptureLogger` 额外把日志行留在内存里：本宿主用它断言
//   「不可达/受阻/升级/装备 等分支真的留下了可定位日志」。
// ─────────────────────────────────────────────────────────────────────────────

using System;
using System.Collections.Generic;
using UnityEngine;

namespace CloverEngine
{
    // ── 日志 ────────────────────────────────────────────────────────────────
    /// <summary>`Runtime/Core/Contracts.cs` 的 `ILogger`。</summary>
    public interface ILogger
    {
        void Info(string tag, string msg);
        void Warn(string tag, string msg);
        void Error(string tag, string msg, Exception ex = null);
        void Debug(string tag, string msg);
        void Fatal(string tag, string msg, Exception ex = null);
    }

    /// <summary>把日志转发到控制台并留档（自检用）。</summary>
    public sealed class CaptureLogger : ILogger
    {
        /// <summary>全部日志行（`[级别] [tag] 消息`）。</summary>
        public static readonly List<string> Lines = new List<string>();

        public void Info(string tag, string msg) => Add("INFO ", tag, msg);
        public void Warn(string tag, string msg) => Add("WARN ", tag, msg);
        public void Error(string tag, string msg, Exception ex = null) => Add("ERROR", tag, msg);
        public void Debug(string tag, string msg) => Add("DEBUG", tag, msg);
        public void Fatal(string tag, string msg, Exception ex = null) => Add("FATAL", tag, msg);

        private static void Add(string level, string tag, string msg)
        {
            var line = "[" + level + "] [" + tag + "] " + msg;
            Lines.Add(line);
            Console.WriteLine("    " + line);
        }

        /// <summary>是否出现过含某片段的日志行。</summary>
        public static bool Has(string needle)
        {
            for (var i = 0; i < Lines.Count; i++)
                if (Lines[i].IndexOf(needle, StringComparison.Ordinal) >= 0) return true;
            return false;
        }

        /// <summary>含某片段的日志行数。</summary>
        public static int Count(string needle)
        {
            var n = 0;
            for (var i = 0; i < Lines.Count; i++)
                if (Lines[i].IndexOf(needle, StringComparison.Ordinal) >= 0) n++;
            return n;
        }

        /// <summary>取最后一条含某片段的日志行（找不到返回空串）。</summary>
        public static string Last(string needle)
        {
            for (var i = Lines.Count - 1; i >= 0; i--)
                if (Lines[i].IndexOf(needle, StringComparison.Ordinal) >= 0) return Lines[i];
            return string.Empty;
        }

        public static void Clear() => Lines.Clear();
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

    /// <summary>记录型事件总线（与引擎同样按事件名 + DynamicInvoke 派发）。</summary>
    public sealed class RecordingEventBus : IEventBus
    {
        private readonly Dictionary<string, List<Delegate>> _handlers = new Dictionary<string, List<Delegate>>();

        /// <summary>已发出的事件（`名字|参数`），按顺序。</summary>
        public readonly List<string> Emitted = new List<string>();

        public void On(string eventName, Action handler) => Add(eventName, handler);
        public void On<T>(string eventName, Action<T> handler) => Add(eventName, handler);
        public void Off(string eventName, Action handler) => Remove(eventName, handler);
        public void Off<T>(string eventName, Action<T> handler) => Remove(eventName, handler);

        public void Emit(string eventName)
        {
            Emitted.Add(eventName);
            Invoke(eventName, Array.Empty<object>());
        }

        public void Emit<T>(string eventName, T arg1)
        {
            Emitted.Add(eventName + "|" + (arg1 == null ? "(null)" : arg1.ToString()));
            Invoke(eventName, new object[] { arg1 });
        }

        /// <summary>某个事件名发出过几次。</summary>
        public int CountOf(string eventName)
        {
            var n = 0;
            for (var i = 0; i < Emitted.Count; i++)
                if (Emitted[i] == eventName || Emitted[i].StartsWith(eventName + "|", StringComparison.Ordinal)) n++;
            return n;
        }

        /// <summary>取某个事件名最后一次的载荷字符串。</summary>
        public string LastArgOf(string eventName)
        {
            for (var i = Emitted.Count - 1; i >= 0; i--)
            {
                if (Emitted[i].StartsWith(eventName + "|", StringComparison.Ordinal))
                    return Emitted[i].Substring(eventName.Length + 1);
            }
            return string.Empty;
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

    // ── 资源 / 输入 / 设置 ───────────────────────────────────────────────────
    /// <summary>`Runtime/Core/Contracts.cs:998`（子集；MapView 用）。</summary>
    public interface IResourceManager
    {
        void LoadAsset<T>(string path, Action<T> callback) where T : UnityEngine.Object;
        T TryGet<T>(string path) where T : UnityEngine.Object;

        // ★ agent-34（引擎下沉 A3）：引擎 `IResourceManager` 新增的两个**同步**入口。
        //   签名逐字对齐 `clover-client-unity-engine/Runtime/Core/Contracts.cs`（本文件是覆盖率哨兵）。
        bool Exists(string path);
        T[] LoadAll<T>(string path) where T : UnityEngine.Object;
    }

    /// <summary>永远取不到资源的管理器（宿主无素材 ⇒ 走纯色占位分支）。</summary>
    public sealed class EmptyResourceManager : IResourceManager
    {
        public void LoadAsset<T>(string path, Action<T> callback) where T : UnityEngine.Object
        {
            if (callback != null) callback(null);
        }

        public T TryGet<T>(string path) where T : UnityEngine.Object => null;

        /// <summary>宿主无素材 ⇒ 恒「不存在」（契约语义：只回答，不加载不驻留）。</summary>
        public bool Exists(string path) => false;

        /// <summary>宿主无素材 ⇒ 恒空数组（契约语义：取不到就是空数组，宿主不抛）。</summary>
        public T[] LoadAll<T>(string path) where T : UnityEngine.Object => Array.Empty<T>();
    }

    /// <summary>`Runtime/Core/Input.cs:66`（子集；`InputReader` 用的全部成员）。</summary>
    public interface IInputManager
    {
        bool Available { get; }
        bool IsLocked { get; }
        bool GetKey(GameKey key);
        bool GetKeyDown(GameKey key);
        bool GetKeyUp(GameKey key);
        bool GetMouseButton(int button);
        bool GetMouseButtonDown(int button);
        bool GetMouseButtonUp(int button);
        Vector3 MousePosition { get; }
        Vector2 MouseDelta { get; }
        float GetAxis(string axis, bool raw = false);
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

    /// <summary>
    /// 由测试脚本逐帧驱动的假输入（**只记录状态，不碰真实设备**）。
    /// 有了它，「点击/按住/腰带快捷键/滚轮」这些分支都能在离线宿主里真的走一遍。
    /// </summary>
    public sealed class ScriptedInput : IInputManager
    {
        /// <summary>后端是否可用（模拟 `CloverInput.Init` 未调用）。</summary>
        public bool Available { get; set; } = true;

        /// <summary>输入是否被 UI 锁住。</summary>
        public bool IsLocked { get; set; }

        /// <summary>鼠标屏幕坐标。</summary>
        public Vector3 MousePosition { get; set; } = new Vector3(960f, 540f, 0f);

        /// <summary>鼠标位移。</summary>
        public Vector2 MouseDelta { get; set; } = Vector2.zero;

        /// <summary>滚轮轴值（`Mouse ScrollWheel`）。</summary>
        public float Wheel;

        private readonly HashSet<GameKey> _down = new HashSet<GameKey>();
        private readonly HashSet<GameKey> _held = new HashSet<GameKey>();
        private readonly HashSet<GameKey> _up = new HashSet<GameKey>();
        private bool _mouse0Down;
        private bool _mouse0Held;
        private bool _mouse0Up;

        // ★ impl-I-input（审计 R1）：右键（button 1）也必须能注入 ——
        //   改动前本替身**只认 button 0**（与生产代码同款缺口），右键链在离线宿主根本无法驱动。
        private bool _mouse1Down;
        private bool _mouse1Held;
        private bool _mouse1Up;

        public bool GetKey(GameKey key) => _held.Contains(key) || _down.Contains(key);
        public bool GetKeyDown(GameKey key) => _down.Contains(key);
        public bool GetKeyUp(GameKey key) => _up.Contains(key);
        public bool GetMouseButton(int button) => button == 0 ? _mouse0Held : button == 1 && _mouse1Held;
        public bool GetMouseButtonDown(int button) => button == 0 ? _mouse0Down : button == 1 && _mouse1Down;
        public bool GetMouseButtonUp(int button) => button == 0 ? _mouse0Up : button == 1 && _mouse1Up;
        public float GetAxis(string axis, bool raw = false)
            => string.Equals(axis, "Mouse ScrollWheel", StringComparison.Ordinal) ? Wheel : 0f;

        /// <summary>新一帧开始：清掉"本帧按下/抬起"。</summary>
        public void BeginFrame()
        {
            _down.Clear();
            _up.Clear();
            _mouse0Down = false;
            _mouse0Up = false;
            _mouse1Down = false;
            _mouse1Up = false;
        }

        /// <summary>模拟按下（本帧 down + held）。</summary>
        public void Press(GameKey key) { _down.Add(key); _held.Add(key); }

        /// <summary>模拟抬起。</summary>
        public void Release(GameKey key) { _held.Remove(key); _up.Add(key); }

        /// <summary>模拟左键按下（本帧 down + held）。</summary>
        public void MouseDown() { _mouse0Down = true; _mouse0Held = true; }

        /// <summary>模拟左键抬起。</summary>
        public void MouseUp() { _mouse0Held = false; _mouse0Up = true; }

        /// <summary>是否按住左键。</summary>
        public bool LeftHeld => _mouse0Held;

        /// <summary>模拟**右键**按下（本帧 down + held；★ impl-I-input，审计 R1 的驱动点）。</summary>
        public void RightMouseDown() { _mouse1Down = true; _mouse1Held = true; }

        /// <summary>模拟**右键**抬起。</summary>
        public void RightMouseUp() { _mouse1Held = false; _mouse1Up = true; }

        /// <summary>是否按住右键。</summary>
        public bool RightHeld => _mouse1Held;
    }

    /// <summary>`Runtime/Core/Setting.cs:11`（子集；内存实现）。</summary>
    public interface ISetting
    {
        T Get<T>(string key, T defaultValue = default);
        void Set<T>(string key, T value);
        void Save();
        void Load();
    }

    /// <summary>内存设置（自检用）。</summary>
    public sealed class MemSetting : ISetting
    {
        private readonly Dictionary<string, object> _d = new Dictionary<string, object>();

        /// <summary>被要求落盘的次数。</summary>
        public int SaveCount;

        public T Get<T>(string key, T defaultValue = default)
            => _d.TryGetValue(key, out var v) && v is T t ? t : defaultValue;

        public void Set<T>(string key, T value) { _d[key] = value; }
        public void Save() { SaveCount++; }
        public void Load() { }
    }

    // ── Game 门面 ───────────────────────────────────────────────────────────
    /// <summary>
    /// `Runtime/Core/Game.cs`（只列本宿主编译到的源码引用到的字段）。
    /// 只放这几个：`Module/{Map,Player,Camera,Input}` + `App/AppContext` 全部只用
    /// `Logger` / `Event` / `Res` / `Input` / `Setting`（**不编** `UI/**` 与 `App/Bootstrap`，
    /// 原因见 `PlayerCheck.csproj` 里的说明）。
    /// </summary>
    public static class Game
    {
        public static ILogger Logger = new CaptureLogger();
        public static IEventBus Event = new RecordingEventBus();
        public static IResourceManager Res = new EmptyResourceManager();
        public static IInputManager Input = new ScriptedInput();
        public static ISetting Setting = new MemSetting();
        public static bool IsRunning;

        public static void Launch(GameConfig config) { IsRunning = true; }
        public static void Shutdown() { IsRunning = false; }
    }

    /// <summary>`Runtime/Core/Game.cs:10`（只列本宿主引用到的字段；不实例化）。</summary>
    public class GameConfig
    {
        public string ServerAddr;
        public bool UseTls;
        public string LogDir;
        public string SettingDir;
        public string ResourceRoot;
        public string DataDir;
    }
}
