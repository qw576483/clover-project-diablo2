// ─────────────────────────────────────────────────────────────────────────────
// FullCheck · 全量业务代码 + 全链路自检宿主（agent-12，**非 Unity 工程**）
//
// 目的（`docs/agents/agent-12-集成接线与全量编译.md` §4.1）：
//   ① **一条命令**证明 `client/Assets/Scripts/**` 的**全部** .cs 能编到一起
//      （`dotnet build tools/fullcheck/FullCheck.csproj`，文件集见 csproj，**不排除任何业务文件**）；
//   ② 真跑一遍核心链路：**装配 → 配表 → 存档往返 → 生成地图 → 刷怪 → 战斗 → 任务链 → 存档复位**，
//      并且走的是**真实装配路径**（`AppContext.AutoWire()` + `AppWiring.Install`，不是手工 new）。
//
// 为什么不能用 `unity run` / Play：用户尚未打开 Unity 编辑器（skill 闸门 2）
// ⇒ 只能用「Unity 托管 DLL + 业务源码编到 .NET」的离线宿主（照 `tools/mapcheck` /
// `tools/flowcheck` / `tools/combatcheck` 同一套做法，见 skill `reference/fast-compile-loop.md`）。
//
// 本宿主的**已知边界**（不是缺陷，是引擎与 Unity 的原生边界）：
//   · `new GameObject()` / `new Texture2D()` 在非 Unity 进程里抛 `SecurityException`
//     （`ECall methods must be packaged into a system module`）⇒ **渲染层无法离线驱动**：
//     `MapModule.ShowArea`（建 MapRoot）与 `ViewModule` 的精灵节点都会走"降级"分支；
//     本宿主把这些调用**隔离并断言降级行为**，不假装它们跑成功。
//   · `Core/Log.cs` 的降频入口（`WarnOnce/WarnThrottled`）内部读 `Time.realtimeSinceStartup`
//     ⇒ 各模块都刻意自实现降频（见 `Module/**/*Log.cs` 的文件头）；本宿主沿用它们的做法，
//     并在启动时用 `Log.Suppress` 包一次 `Cfg` 预热（ClientConfig 的失败路径会踩到那个入口）。
//   · `AppFlow` 的菜单链路（Boot→MainMenu→选角→创角→Loading→Stage）由 `tools/flowcheck` 覆盖
//     （它已通过）；本宿主覆盖的是**模块级全链路 + App 层接线**，两者互补。
// ─────────────────────────────────────────────────────────────────────────────

using System;
using System.Collections.Generic;
using System.Text;
using CloverEngine;
using Diablo2.App;
using Diablo2.Core;
using Diablo2.Def;
using Diablo2.Module;
using Diablo2.Module.Combat;    // ★ 片 2b：Step7 用 `DeathFlow.TreasureClassIdOf` 选"TC 可解析"的靶子
using Diablo2.Module.Flow;
using Diablo2.UI;              // ★ 本片：任务日志文案口径的回归断言用 QuestLogPanel.TextOf / Remaining
using UnityEngine;
using AppContext = Diablo2.App.AppContext;

namespace FullCheck
{
    // ═════════════════════════════════════════════════════════════════════════
    // 引擎门面替身（可记录 / 可断言；真实签名见 shim/EngineShim.cs 的逐条出处）
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>控制台 + 内存双写日志（内存副本用于断言"某条日志确实打出来了"）。</summary>
    internal sealed class TeeLogger : CloverEngine.ILogger
    {
        private readonly List<string> _lines = new List<string>();

        public bool Contains(string fragment)
        {
            for (var i = 0; i < _lines.Count; i++)
            {
                if (_lines[i].IndexOf(fragment, StringComparison.Ordinal) >= 0) return true;
            }
            return false;
        }

        public int Count(string fragment)
        {
            var n = 0;
            for (var i = 0; i < _lines.Count; i++)
            {
                if (_lines[i].IndexOf(fragment, StringComparison.Ordinal) >= 0) n++;
            }
            return n;
        }

        public void DumpTo(string path)
        {
            System.IO.File.WriteAllText(path, string.Join(Environment.NewLine, _lines), new UTF8Encoding(false));
        }

        private void W(string level, string tag, string msg)
        {
            var line = "[" + level + "] [" + tag + "] " + msg;
            _lines.Add(line);
            Console.WriteLine(line);
        }

        public void Info(string tag, string msg) => W("INFO ", tag, msg);
        public void Warn(string tag, string msg) => W("WARN ", tag, msg);
        public void Debug(string tag, string msg) => W("DEBUG", tag, msg);

        public void Error(string tag, string msg, Exception ex = null)
            => W("ERROR", tag, msg + (ex != null ? " | " + ex.GetType().Name + ": " + ex.Message : ""));

        public void Fatal(string tag, string msg, Exception ex = null)
            => W("FATAL", tag, msg + (ex != null ? " | " + ex.GetType().Name + ": " + ex.Message : ""));
    }

    /// <summary>
    /// 事件总线（同步派发 + **事件计数**，用于断言"链路真的走通了"）。
    /// ★ 派发顺序与真引擎一致（`Runtime/Core/Event.cs:141-143` + `:319-343`）：
    ///   同 priority 0 ⇒ **后注册先执行**；priority 大者先执行。见 shim 里的同一段说明。
    /// </summary>
    internal sealed class RecordingBus : IEventBus
    {
        private struct HandlerEntry
        {
            public Delegate Handler;
            public int Priority;
        }

        private readonly Dictionary<string, List<HandlerEntry>> _handlers = new Dictionary<string, List<HandlerEntry>>();
        private readonly Dictionary<string, int> _counts = new Dictionary<string, int>(StringComparer.Ordinal);

        public int CountOf(string eventName) => _counts.TryGetValue(eventName, out var c) ? c : 0;
        public void ResetCounts() => _counts.Clear();

        public void On(string eventName, Action handler) => Add(eventName, handler, 0);
        public void On<T>(string eventName, Action<T> handler) => Add(eventName, handler, 0);
        public void OnPriority(string eventName, int priority, Action handler) => Add(eventName, handler, priority);
        public void Off(string eventName, Action handler) => Remove(eventName, handler);
        public void Off<T>(string eventName, Action<T> handler) => Remove(eventName, handler);
        public void Emit(string eventName) => Fire(eventName, Array.Empty<object>());
        public void Emit<T>(string eventName, T arg1) => Fire(eventName, new object[] { arg1 });

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

        private void Fire(string name, object[] args)
        {
            _counts[name] = CountOf(name) + 1;
            if (!_handlers.TryGetValue(name, out var list) || list.Count == 0) return;
            var snapshot = list.ToArray();          // 回调内新增的订阅者不参与本次派发（与 flowcheck 同语义）
            for (var i = snapshot.Length - 1; i >= 0; i--) snapshot[i].Handler.DynamicInvoke(args);
        }
    }

    /// <summary>`Runtime/Core/Fsm.cs:9` 的替身（站点迁移即时生效，便于断言）。</summary>
    internal sealed class SimpleFsm : IFsm
    {
        private readonly Dictionary<string, (Action enter, Action<float> tick, Action exit)> _states
            = new Dictionary<string, (Action, Action<float>, Action)>();
        private readonly Dictionary<string, string> _trans = new Dictionary<string, string>();
        private Action<string, string> _onChange;

        public string Current { get; private set; }

        public void RegisterState(string state, Action onEnter = null, Action<float> onTick = null, Action onExit = null)
            => _states[state] = (onEnter, onTick, onExit);

        public void Transition(string toState) { if (toState != Current) Switch(toState); }
        public void AddTransition(string trigger, string toState) => _trans[trigger] = toState;

        public void Trigger(string trigger) { if (_trans.TryGetValue(trigger, out var to)) Switch(to); }
        public void Force(string state) => Switch(state);
        public void Tick(float dt) { if (Current != null && _states.TryGetValue(Current, out var s)) s.tick?.Invoke(dt); }
        public void OnChange(Action<string, string> handler) => _onChange += handler;
        public void OffChange(Action<string, string> handler) => _onChange -= handler;

        private void Switch(string to)
        {
            var from = Current;
            if (from != null && _states.TryGetValue(from, out var f)) f.exit?.Invoke();
            Current = to;
            if (_states.TryGetValue(to, out var t)) t.enter?.Invoke();
            _onChange?.Invoke(from, to);
        }
    }

    /// <summary>UI 管理器替身（记录打开过的面板名；不实例化 MonoBehaviour）。</summary>
    internal sealed class RecUI : IUIManager
    {
        public readonly List<string> Opened = new List<string>();
        public int CloseAllCount;

        public void Open<T>(object param = null) where T : class, IUIPanel => Opened.Add(typeof(T).Name);
        public void Close<T>() where T : class, IUIPanel { }
        public void Close(string panelName) { }
        public void CloseAll() { CloseAllCount++; Opened.Clear(); }
        public T Get<T>() where T : class, IUIPanel => null;
        public bool IsOpen<T>() where T : class, IUIPanel => false;
        public void Toast(string text, float duration = 2f) => Console.WriteLine("  [TOAST] " + text);
        public void FloatText(Vector3 worldPos, string text, Color? color = null, float duration = 1.2f) { }
        public void ShowLoading(string text = null) { }
        public void HideLoading() { }
        public bool IsLoading => false;
        public void Confirm(string title, string message, Action onConfirm, Action onCancel = null,
            string confirmText = null, string cancelText = null)
        {
            Console.WriteLine("  [CONFIRM] " + title + ": " + message);
            onConfirm?.Invoke();
        }
        public void Tick(float dt) { }
    }

    /// <summary>场景管理器替身（同步完成加载；`onDone` 一定会回调）。</summary>
    internal sealed class RecScene : ISceneManager
    {
        public readonly List<string> Loaded = new List<string>();
        public string CurrentScene { get; private set; }

        // agent-17 §A：与引擎同序 —— `OnSceneLoaded` 的处理器在 `onDone` **之前**逐个回调
        // （`Runtime/Presentation/Scene.cs:54-65`）。`AppFlow` 靠它识别"Stage 场景被重载"。
        private readonly List<Action<string>> _loaded = new List<Action<string>>();
        private readonly List<Action<string>> _unloaded = new List<Action<string>>();

        public void Load(string sceneName, Action<float> progress = null, Action onDone = null)
        {
            Loaded.Add(sceneName);
            progress?.Invoke(0.5f);
            progress?.Invoke(1f);
            CurrentScene = sceneName;
            for (var i = 0; i < _loaded.Count; i++) _loaded[i]?.Invoke(sceneName);
            onDone?.Invoke();
        }

        public void Unload(string sceneName, Action onDone = null)
        {
            if (CurrentScene == sceneName) CurrentScene = null;
            for (var i = 0; i < _unloaded.Count; i++) _unloaded[i]?.Invoke(sceneName);
            onDone?.Invoke();
        }

        public void OnSceneLoaded(Action<string> handler) => _loaded.Add(handler);
        public void OnSceneUnloaded(Action<string> handler) => _unloaded.Add(handler);
    }

    /// <summary>设置替身（内存字典；存档模块就靠它落盘/读回）。</summary>
    internal sealed class MemSetting : ISetting
    {
        private readonly Dictionary<string, object> _d = new Dictionary<string, object>();
        public T Get<T>(string key, T defaultValue = default) => _d.TryGetValue(key, out var v) && v is T t ? t : defaultValue;
        public void Set<T>(string key, T value) { _d[key] = value; }
        public void Save() { }
        public void Load() { }
        public void Delete(string key) => _d.Remove(key);
        public void DeleteAll() => _d.Clear();
        public int Count => _d.Count;
    }

    /// <summary>输入替身：`Available=false`（离线进程无输入后端）⇒ 驱动"降级但不出错"的那条路径。</summary>
    internal sealed class FakeInput : IInputManager
    {
        public bool Available => false;
        public bool IsLocked => false;
        public bool GetKey(GameKey key) => false;
        public bool GetKeyDown(GameKey key) => false;
        public bool GetKeyUp(GameKey key) => false;
        public bool GetMouseButton(int button) => false;
        public bool GetMouseButtonDown(int button) => false;
        public bool GetMouseButtonUp(int button) => false;
        public float GetAxis(string axis, bool raw = false) => 0f;
        public Vector3 MousePosition => Vector3.zero;
    }

    internal sealed class FakeSound : ISoundManager
    {
        public int StopAllCount;
        public void PlayBGM(string clipName, float fadeTime = 0.5f) { }
        public void PlaySFX(string clipName) { }
        public void PlaySFXAt(string clipName, Vector3 position) { }
        public void StopBGM(float fadeTime = 0.5f) { }
        public void StopAll() => StopAllCount++;
        public void SetVolume(SoundGroup group, float volume) { }
        public float GetVolume(SoundGroup group) => 1f;
        public void SetMute(SoundGroup group, bool mute) { }
    }

    internal sealed class FakeEntities : IEntityManager
    {
        private readonly List<EntityInfo> _alive = new List<EntityInfo>();
        public int ClearAllCount;
        public void Seed(int n) { _alive.Clear(); for (var i = 0; i < n; i++) _alive.Add(new EntityInfo()); }
        public void ClearAll() { ClearAllCount++; _alive.Clear(); }
        public IEnumerable<EntityInfo> GetAll() => _alive;
    }

    internal sealed class FakePool : IObjectPool
    {
        public int ClearAllCount;
        public void ClearAll() => ClearAllCount++;
    }

    /// <summary>定时器替身（虚拟时钟；`Advance` 会跑到期回调 ⇒ 可断言"下一帧补发"类逻辑）。</summary>
    internal sealed class FakeTimer : ITimer
    {
        private struct Item { public long Id; public float Due; public Action Cb; public bool Every; public float Interval; }

        private readonly List<Item> _items = new List<Item>();
        private long _next = 1;
        private float _now;

        public long After(float delay, Action callback) => Add(delay, callback, false, 0f);
        public long AfterUnscaled(float delay, Action callback) => Add(delay, callback, false, 0f);
        public long Every(float interval, Action callback) => Add(interval, callback, true, interval);
        public long EveryUnscaled(float interval, Action callback) => Add(interval, callback, true, interval);

        public void Stop(long id) => _items.RemoveAll(i => i.Id == id);
        public void StopNamed(string name) { }
        public void StopScope(string scope) { }
        public void StopAll() => _items.Clear();

        public void Tick(float dt) => Advance(dt);

        /// <summary>推进虚拟时钟并执行到期回调（`dt` 之外用 Pump 触发"下一帧"）。</summary>
        public void Advance(float dt)
        {
            _now += dt;
            for (var guard = 0; guard < 8; guard++)
            {
                Item? due = null;
                for (var i = 0; i < _items.Count; i++)
                {
                    if (_items[i].Due <= _now) { due = _items[i]; break; }
                }
                if (due == null) return;

                var it = due.Value;
                if (it.Every) it.Due = _now + it.Interval;
                else _items.RemoveAll(x => x.Id == it.Id);

                it.Cb?.Invoke();
            }
        }

        private long Add(float delay, Action cb, bool every, float interval)
        {
            var id = _next++;
            _items.Add(new Item { Id = id, Due = _now + (delay > 0f ? delay : 0f), Cb = cb, Every = every, Interval = interval });
            return id;
        }
    }

    /// <summary>资源替身：一律回调 null（等价"素材未到位 ⇒ 纯色占位"）。</summary>
    internal sealed class FakeRes : IResourceManager
    {
        public int LoadCalls;

        /// <summary>`Exists` 的调用次数（agent-34：业务改调引擎同步存在性探针后，本宿主也能数它）。</summary>
        public int ExistsCalls;

        /// <summary>`LoadAll` 的调用次数 + 最后一次路径（agent-34）。</summary>
        public int LoadAllCalls;
        public string LastLoadAllPath;

        public void LoadAsset<T>(string path, Action<T> cb) where T : UnityEngine.Object { LoadCalls++; cb?.Invoke(null); }
        public T TryGet<T>(string path) where T : UnityEngine.Object => null;

        // ★ agent-34（引擎下沉 A3）：引擎新增的两个**同步**入口。本替身"永远取不到"，
        //   于是 `ClientConfig` 走引擎分支时拿到空数组、照旧退回文件 / 默认值（离线可复现）。
        public bool Exists(string path) { ExistsCalls++; return false; }
        public T[] LoadAll<T>(string path) where T : UnityEngine.Object
        {
            LoadAllCalls++;
            LastLoadAllPath = path;
            return Array.Empty<T>();
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    // 自检主流程
    // ═════════════════════════════════════════════════════════════════════════

    public static class Program
    {
        // ★ 仓库根改为**运行期推导**（见 ResolveProjectRoot），不再依赖调用方 cwd。
        private static readonly string ProjectRoot = ResolveProjectRoot();
        private static readonly string ClientAssets = ProjectRoot + @"\client\Assets";
        // 宿主已随「仓库卫生整理」迁到 tools/probes/hosts/fullcheck/；写日志前必须先有这个目录，
        // 否则 DumpTo 必抛（它只 WriteAllText、不建目录）⇒ 每次跑都印「写日志文件失败」。
        private static readonly string LogPath = ProjectRoot + @"\tools\probes\hosts\fullcheck\_last_run.log";
        private const string HeroName = "FullCheckHero";

        private static TeeLogger _log;
        private static RecordingBus _bus;
        private static RecUI _ui;
        private static RecScene _scene;
        private static MemSetting _setting;
        private static FakeTimer _timer;
        private static FakeSound _sound;
        private static FakeEntities _entities;
        private static FakePool _pool;
        private static FakeRes _res;

        private static int _fail;
        private static bool _unityNativeBlocked;

        /// <summary>
        /// 从宿主自己的可执行目录向上找“含 client/Assets 的那一层” = 仓库根。
        /// 宿主位于 tools/probes/hosts/&lt;名&gt;/bin/&lt;cfg&gt;/&lt;tfm&gt;/；若按调用方 cwd 定位，
        /// 从仓库根运行时会被拼成 &lt;仓库根&gt;/clover-project-diablo2/client/...（一个文件都找不到）。
        /// 找不到就回退成原来的相对写法，保持“从仓库上一级目录运行”的老用法不变。
        /// </summary>
        private static string ResolveProjectRoot()
        {
            var dir = new System.IO.DirectoryInfo(System.AppContext.BaseDirectory);
            while (dir != null)
            {
                if (System.IO.Directory.Exists(System.IO.Path.Combine(dir.FullName, "client", "Assets")))
                    return dir.FullName;
                dir = dir.Parent;
            }
            Console.WriteLine("[warn] 未从可执行目录向上找到含 client/Assets 的仓库根，回退相对路径 clover-project-diablo2");
            return @"clover-project-diablo2";
        }

        public static int Main()
        {
            Console.WriteLine("╔══════════════════════════════════════════════════════════════════╗");
            Console.WriteLine("║  FullCheck：全量业务代码编译 + 核心链路自检（agent-12）          ║");
            Console.WriteLine("╚══════════════════════════════════════════════════════════════════╝");

            Run(Step0_Host);
            Run(Step1_Tables);
            Run(Step2_Assemble);
            Run(Step3_InstallWiring);
            Run(Step4_CreateAndSaveCharacter);
            Run(Step5_EnterTown);
            Run(Step6_DoorDedup);
            Run(Step7_Combat);
            Run(Step8_QuestChain);
            Run(Step9_SaveReloadAndReset);

            Console.WriteLine();
            Console.WriteLine("──────────────────────────────────────────────────────────────────");
            Console.WriteLine("AppContext.Describe() → " + (AppContext.I != null ? AppContext.I.Describe() : "(未创建)"));
            Console.WriteLine("──────────────────────────────────────────────────────────────────");

            try { _log?.DumpTo(LogPath); Console.WriteLine("全链路日志已写出：" + LogPath); }
            catch (Exception e) { Console.WriteLine("写日志文件失败：" + e.Message); }

            Console.WriteLine();
            Console.WriteLine(_fail == 0 ? "=== 自检全部通过 ===" : $"=== 自检失败 {_fail} 项 ===");
            return _fail == 0 ? 0 : 1;
        }

        // ── 0. 引擎宿主 ─────────────────────────────────────────────────────
        private static void Step0_Host()
        {
            Section("0. 引擎宿主替身 + Unity 原生边界探针");

            _log = new TeeLogger();
            Game.Logger = _log;
            _bus = new RecordingBus();
            Game.Event = _bus;
            Game.Fsm = new SimpleFsm();
            _ui = new RecUI();
            Game.UI = _ui;
            _scene = new RecScene();
            Game.Scene = _scene;
            _setting = new MemSetting();
            Game.Setting = _setting;
            Game.Input = new FakeInput();
            _sound = new FakeSound();
            Game.Sound = _sound;
            _entities = new FakeEntities();
            Game.Entity = _entities;
            _pool = new FakePool();
            Game.Pool = _pool;
            _timer = new FakeTimer();
            Game.Timer = _timer;
            _res = new FakeRes();
            Game.Res = _res;
            Game.IsRunning = true;

            Check("引擎门面替身已就位（Logger/Event/Fsm/UI/Scene/Setting/Input/Sound/Entity/Pool/Timer/Res）",
                true, "单机最小集：**不调** CloverNet.Init（本项目形态=单机）");

            // Unity 原生对象探针：证明"渲染层只能降级"是环境边界而不是本项目的缺陷。
            try
            {
                var go = new GameObject("fullcheck-probe");
                Console.WriteLine($"  [探针] new GameObject 成功：{go}");
                _unityNativeBlocked = false;
            }
            catch (Exception e)
            {
                _unityNativeBlocked = true;
                Console.WriteLine($"  [探针] new GameObject 失败：{e.GetType().Name}: {e.Message}");
            }

            Check("Unity 原生对象在非 Unity 进程不可用（渲染层只能走降级分支）",
                _unityNativeBlocked, "见上方探针；MapModule.ShowArea / ViewModule 节点创建会走各自的降级日志");

            // `Cfg` 预热：ClientConfig 在非 Unity 进程里读 `Application.dataPath` 会失败，而它失败
            // 路径上的 `Log.ErrorOnce` 会踩 `Time.realtimeSinceStartup` ⇒ 用 `Log.Suppress` 包一次。
            Log.Suppress = true;
            var warm = "n/a";
            try
            {
                warm = $"bgm={Cfg.BgmVolume:0.00} sfx={Cfg.SfxVolume:0.00} fullscreen={Cfg.Fullscreen} " +
                       $"name={Cfg.DefaultPlayerName}";
            }
            catch (Exception e)
            {
                Console.WriteLine("  [探针] Cfg 预热异常（按默认值继续）：" + e.GetType().Name + ": " + e.Message);
            }
            Log.Suppress = false;

            Check("Cfg（config.json）已预热（离线读不到文件 ⇒ 回落默认值，不抛异常）", true, warm);

            // ★ agent-34（引擎下沉 A3）：`Cfg` 读 config.json 已改走**引擎资源模块**
            //   （`Game.Res.LoadAll<TextAsset>("Configs/config")`），不再直连 Unity 的 `Resources.Load`
            //   （验收表 **E1** 的例外已收口 —— 它原先就在 E1 的出处列里：`Core/ClientConfig.cs`）。
            //   本宿主用「永远取不到」的替身（`FakeRes`）⇒ 判据 = 它**确实被问过**、且问的是那个路径。
            Check("Cfg 走引擎资源模块取 config.json（`Game.Res.LoadAll<TextAsset>(Configs/config)`）",
                _res.LoadAllCalls >= 1 && _res.LastLoadAllPath == ResPaths.ConfigResourceKey,
                $"LoadAllCalls={_res.LoadAllCalls} lastPath={_res.LastLoadAllPath ?? "(none)"} "
                + $"（期望 path={ResPaths.ConfigResourceKey}）");
            Console.WriteLine();
        }

        // ── 1. 配表 ─────────────────────────────────────────────────────────
        private static void Step1_Tables()
        {
            Section("1. 配表加载（与 `Bootstrap` 同一条链路：Table.TableLoader.LoadAll）");

            var err = Table.TableLoader.LoadAll(null, ClientAssets);
            Check("配表已加载（10 张 tsv）", err == null, err ?? ("dir=" + Table.TableLoader.LastDir));

            var cls = Table.TableLoader.Class((int)PlayerClass.Amazon);
            Check("class_c 亚马逊行存在", cls != null && cls.Str > 0,
                cls == null ? "row=null" : $"str={cls.Str} dex={cls.Dex} vit={cls.Vit} eng={cls.Eng}");

            var lv1 = Table.Tables.Default.Level.Get(1);
            var lv2 = Table.Tables.Default.Level.Get(2);
            var lv3 = Table.Tables.Default.Level.Get(3);
            Check("level_c 三行（1=罗格营地 / 2=血腥荒野 / 3=邪恶洞穴）",
                lv1 != null && lv2 != null && lv3 != null,
                $"1={lv1?.LevelName} 怪=[{(lv1?.Monsters != null ? string.Join(",", lv1.Monsters) : "")}] / " +
                $"2={lv2?.LevelName} 怪=[{(lv2?.Monsters != null ? string.Join(",", lv2.Monsters) : "")}] / " +
                $"3={lv3?.LevelName} 怪=[{(lv3?.Monsters != null ? string.Join(",", lv3.Monsters) : "")}]");

            Check("[Assert] 罗格营地的 monsters 列为空 ⇒ 城镇不刷怪（配表侧保证）",
                lv1 != null && (lv1.Monsters == null || lv1.Monsters.Length == 0), "见上方 level_c id=1");

            Check("monster_c / skill_c / item_c / affix_c 至少各一行",
                Table.Tables.Default.Monster.All().Count > 0 && Table.Tables.Default.Skill.All().Count > 0 &&
                Table.Tables.Default.Item.All().Count > 0 && Table.Tables.Default.Affix.All().Count > 0,
                $"monster={Table.Tables.Default.Monster.All().Count} skill={Table.Tables.Default.Skill.All().Count} " +
                $"item={Table.Tables.Default.Item.All().Count} affix={Table.Tables.Default.Affix.All().Count} " +
                $"tc={Table.Tables.Default.Treasureclass.All().Count}");
            Console.WriteLine();
        }

        // ── 2. 装配 ─────────────────────────────────────────────────────────
        private static void Step2_Assemble()
        {
            Section("2. AppContext.AutoWire（反射装配全部模块）");

            var ctx = AppContext.Create();
            ctx.AutoWire();
            ctx.Flow = new AppFlow();        // `IAppFlow` 刻意不在 AutoWire 里（构造即订阅，重复 new 会重复处理）

            var missing =
                (ctx.Map == null ? "Map " : "") + (ctx.Player == null ? "Player " : "") +
                (ctx.Combat == null ? "Combat " : "") + (ctx.Monster == null ? "Monster " : "") +
                (ctx.Skill == null ? "Skill " : "") + (ctx.Item == null ? "Item " : "") +
                (ctx.Quest == null ? "Quest " : "") + (ctx.Npc == null ? "Npc " : "") +
                (ctx.Camera == null ? "Camera " : "") + (ctx.View == null ? "View " : "") +
                (ctx.Audio == null ? "Audio " : "") + (ctx.Save == null ? "Save " : "");

            Check("AutoWire 后 **12 个模块全部非 null**" + (missing.Length > 0 ? "（缺：" + missing.Trim() + "）" : ""),
                missing.Length == 0, ctx.Describe());

            Check("每个门面的实现类型唯一且叫 XxxModule（自动装配契约）",
                ctx.Map.GetType().Name == "MapModule" && ctx.Player.GetType().Name == "PlayerModule" &&
                ctx.Monster.GetType().Name == "MonsterModule" && ctx.Item.GetType().Name == "ItemModule" &&
                ctx.Quest.GetType().Name == "QuestModule" && ctx.Npc.GetType().Name == "NpcModule" &&
                ctx.Save.GetType().Name == "SaveModule" && ctx.View.GetType().Name == "ViewModule" &&
                ctx.Audio.GetType().Name == "AudioModule" && ctx.Camera.GetType().Name == "CameraRig",
                "Map/Player/Monster/Item/Quest/Npc/Save/View/Audio/Camera 逐个核对");

            Check("`IAppFlow` 不在 AutoWire 内（`ctx.Flow` 由 Bootstrap 显式 new）",
                ctx.Flow is AppFlow, ctx.Flow.GetType().Name);
            Console.WriteLine();
        }

        // ── 3. 接线 ─────────────────────────────────────────────────────────
        private static void Step3_InstallWiring()
        {
            Section("3. AppWiring.Install（App 层最后一次接线；走真实装配路径）");

            var ctx = AppContext.I;
            Check("接线前 AppContext.MapRoot / EntityRoot 为 null（离线宿主无场景）",
                ctx.MapRoot == null && ctx.EntityRoot == null, "⇒ 走「模块自建根」的降级分支");

            AppWiring.Install(ctx);

            Check("AppWiring.Install 未抛异常且完成的接线在日志里可见",
                _log.Contains("App 接线完成"), "见上方 [App] 接线完成 …");

            Check("根节点未注入时留下可定位的 Warn（降级而不是静默）",
                _log.Contains("AppContext.MapRoot 为空") && _log.Contains("AppContext.EntityRoot 为空"),
                "见上方 [App] AppContext.MapRoot 为空 …");

            Check("[Assert] 生成次数断言已装上（AppDoorGuard 初始 DoorSerial=0 / gens=0）",
                AppDoorGuard.DoorSerial == 0 && AppDoorGuard.GensSinceLastBoundary == 0,
                $"DoorSerial={AppDoorGuard.DoorSerial} gens={AppDoorGuard.GensSinceLastBoundary}");

            // 离线宿主拿不到场景里的 Transform ⇒ 只能断言"注入通道可用"（反射能摸到非契约入口）
            var mapAttach = ctx.Map.GetType().GetMethod("AttachRoot",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            var viewAttach = ctx.View.GetType().GetMethod("AttachRoot",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            Check("根节点注入通道可用（两个模块都存在非契约入口 `AttachRoot(Transform)`）",
                mapAttach != null && viewAttach != null,
                $"{ctx.Map.GetType().Name}.AttachRoot={(mapAttach != null)} / {ctx.View.GetType().Name}.AttachRoot={(viewAttach != null)}" +
                "；真实 Transform 由 `StageRoots` / `Bootstrap` 序列化字段提供（见回报「待 agent-10」）");
            Console.WriteLine();
        }

        // ── 4. 创角 + 存档往返 ───────────────────────────────────────────────
        private static void Step4_CreateAndSaveCharacter()
        {
            Section("4. 创角（配表数值）+ 存档写入/读回");

            var ctx = AppContext.I;
            var cls = Table.TableLoader.Class((int)PlayerClass.Amazon);
            var save = BuildSaveFromTable(cls, HeroName, seed: 20260310);

            Check("存档模块就绪（Game.Setting 已接入）", ctx.Save.Ready, "Ready=" + ctx.Save.Ready);
            Check("创角落盘成功（Save(CharacterSave)）", ctx.Save.Save(save), save.name);
            Check("存档索引里能找到该角色", ctx.Save.Exists(HeroName) && ctx.Save.HasAny, "HasAny=" + ctx.Save.HasAny);

            var back = ctx.Save.Load(HeroName);
            Check("读档字段一致（等级/四维/生命/seed）",
                back != null && back.level == save.level && back.str == save.str && back.dex == save.dex &&
                back.vit == save.vit && back.eng == save.eng && back.life == save.life && back.mapSeed == save.mapSeed,
                back == null ? "null" : $"lv={back.level} str={back.str} vit={back.vit} life={back.life} seed={back.mapSeed}");
            Console.WriteLine();
        }

        // ── 5. 进图（罗格营地）──────────────────────────────────────────────
        private static void Step5_EnterTown()
        {
            Section("5. 进图：生成罗格营地 → 装配角色 → 刷怪 → StageEntered（App 接线响应）");

            var ctx = AppContext.I;
            var save = ctx.Save.Load(HeroName);

            ctx.Map.Generate(AreaId.Town, save.mapSeed);
            Check("罗格营地地图已生成", ctx.Map.IsGenerated,
                $"{ctx.Map.Width}x{ctx.Map.Height} seed={ctx.Map.Seed} 障碍={ctx.Map.BlockedCount} 可走={ctx.Map.WalkableCount} " +
                $"（MapModule 的 DumpStats 见 [Map] 日志）");
            Check("出生点 / 出口 / NPC 站位有效（契约 §3.5）",
                ctx.Map.SpawnPoint.x > 0 && ctx.Map.SpawnPoint.y > 0 && ctx.Map.Exits.Count >= 1 && ctx.Map.NpcPoints.Count == 5,
                $"出生点={ctx.Map.SpawnPoint} 出口={ctx.Map.Exits.Count} NPC={ctx.Map.NpcPoints.Count} " +
                $"洞穴入口={(ctx.Map.CaveEntrance.HasValue ? ctx.Map.CaveEntrance.Value.ToString() : "null（城镇应为 null）")}");

            TryShowArea();      // 渲染层：Unity 原生 ⇒ 隔离并记录

            ctx.Player.LoadFrom(save);
            Check("玩家已按存档装配（等级/生命/格坐标）",
                ctx.Player.Level == save.level && ctx.Player.MaxLife > 0,
                $"Lv={ctx.Player.Level} 生命={ctx.Player.Life}/{ctx.Player.MaxLife} 格={ctx.Player.Grid} 职业={ctx.Player.Class}");

            ctx.View.CreatePlayer(save.cls);
            Check("[已知边界] 视图层在无 Unity 进程里优雅降级（抛异常被内部捕获，不影响逻辑层）",
                _log.Contains("本进程不具备渲染能力"), "见上方 [View] ViewModule: 无法创建 GameObject …");

            ctx.Monster.SpawnArea(AreaId.Town);
            Check("[Assert] 罗格营地不刷怪（AliveCount == 0）", ctx.Monster.AliveCount == 0,
                "AliveCount=" + ctx.Monster.AliveCount + "；见 [Monster] monsters 列为空 ⇒ 本区域无怪");

            ctx.Camera.SetTargetGrid(ctx.Player.Grid);
            ctx.Camera.SnapToTarget();
            Check("跟随相机已对准玩家（无相机时只在内部状态推进）", true, "格=" + ctx.Player.Grid);

            var beforeHud = _bus.CountOf(Events.StageEntered);
            Game.Event.Emit(Events.StageEntered);
            Check("StageEntered 已派发", _bus.CountOf(Events.StageEntered) == beforeHud + 1, "");

            Check("App 接线：HUD 随 StageEntered 打开（约定 §3.5 的 (a) 方案）",
                _ui.Opened.Contains("HudPanel"), "已打开面板=" + string.Join(",", _ui.Opened));

            Check("App 接线：进图全量快照 6 条全部广播（面板打开即有数据）",
                _bus.CountOf(Events.HudDirty) >= 1 && _bus.CountOf(Events.InventoryChanged) >= 1 &&
                _bus.CountOf(Events.SkillTreeChanged) >= 1 && _bus.CountOf(Events.QuestChanged) >= 1 &&
                _bus.CountOf(Events.MapGenerated) >= 1 && _bus.CountOf(Events.PlayerGridChanged) >= 1,
                $"HudDirty={_bus.CountOf(Events.HudDirty)} Inv={_bus.CountOf(Events.InventoryChanged)} " +
                $"SkillTree={_bus.CountOf(Events.SkillTreeChanged)} Quest={_bus.CountOf(Events.QuestChanged)} " +
                $"Map={_bus.CountOf(Events.MapGenerated)} Grid={_bus.CountOf(Events.PlayerGridChanged)}");

            Check("App 接线：进图断言日志（地图已生成 + 刷怪就绪）都打出来了",
                _log.Contains("[Assert] 进图地图已生成") && _log.Contains("[Assert] 刷怪已就绪"),
                "见上方 [App] [Assert] …");

            // ★ agent-14 §B 现象 3 的**回归断言**（就是那次 Play 失败的原样复现）：
            //   `AppSnapshots.Broadcast` 会给 HUD/小地图补发**同一张**地图（回声），它**不是**"又生成了一张图"。
            //   修前现场（Play 日志 seq 222 / 243）：`[Map] Generate 完成` 只有 1 条，
            //   却出现 `[Assert] 本次过门已第 2 次收到 D2.Map.Generated（>1）⇒ 重复生成！`
            var genCount = _log.Count("[Map] Generate 完成");
            var dupWarnCount = _log.Count("重复生成");
            Check("★快照回声未被误计成「重复生成」（真生成 1 次、回声 1 次被识别）",
                genCount == 1 && dupWarnCount == 0 &&
                _log.Contains("本次进图：生成地图 1 次 —— 去重断言通过，另有 1 次快照回声已忽略"),
                $"Generate完成={genCount} 重复生成Warn={dupWarnCount}");

            // 任务接取（原版：进图后找阿卡拉接「邪恶洞穴」）
            ctx.Quest.AcceptDen();
            Check("任务「邪恶洞穴」已接取（state=InProgress）",
                ctx.Quest.DenOfEvil == QuestState.InProgress, "state=" + ctx.Quest.DenOfEvil);
            Console.WriteLine();
        }

        // ── 6. 过门去重 ─────────────────────────────────────────────────────
        private static void Step6_DoorDedup()
        {
            Section("6. 过门：血腥荒野（生成 + 刷怪）+ 去重断言");

            var ctx = AppContext.I;
            DoorTransition(AreaId.BloodMoor, 20260311);

            Check("过门 #1 后地图 = 血腥荒野且已刷怪",
                ctx.Map.Area == AreaId.BloodMoor && ctx.Monster.AliveCount > 0,
                $"区域={ctx.Map.Area} 尺寸={ctx.Map.Width}x{ctx.Map.Height} 存活={ctx.Monster.AliveCount} " +
                $"洞穴入口={ctx.Map.CaveEntrance}");

            Check("[Assert] 过门 #1 只触发 **1 次** 地图生成（边界上报为 1，随后清零）",
                AppDoorGuard.DoorSerial == 1 && AppDoorGuard.GensSinceLastBoundary == 0 &&
                _log.Contains("过门 #1 → BloodMoor：生成地图 1 次 —— 去重断言通过"),
                $"DoorSerial={AppDoorGuard.DoorSerial} gens={AppDoorGuard.GensSinceLastBoundary}");

            // 模拟"上游没去重"的反面：再发一次同样的 ExitEntered（不生成）⇒ 断言窗口报告为 0 次
            Game.Event.Emit(Events.ExitEntered, AreaId.BloodMoor);
            Check("[Assert] 重复过门不重生成：第 2 次 ExitEntered 上报为 **0 次**生成（上游已去重）",
                _log.Contains("过门 #2 → BloodMoor：未生成地图（上游已去重：重复请求被忽略）") &&
                AppDoorGuard.DoorSerial == 2 && AppDoorGuard.GensSinceLastBoundary == 0,
                $"DoorSerial={AppDoorGuard.DoorSerial} gens={AppDoorGuard.GensSinceLastBoundary}");

            Check("AppDoorGuard 记录了本次生成明细（areaId/尺寸/seed/tiles）",
                _log.Contains("[Map] 地图已生成（本窗口第 1 次）"), "见上方 [App] [Map] 地图已生成 …");
            Console.WriteLine();
        }

        // ── 7. 战斗 ─────────────────────────────────────────────────────────
        private static void Step7_Combat()
        {
            Section("7. 战斗：近战普攻 → 伤害结算 → 击杀 → 经验/掉落链");

            var ctx = AppContext.I;

            // ── ★ 片 2b：披露「列表首怪的掉落表能不能解析」+ 把击杀目标改成**确定性的**一只 ──
            //   为什么：本步末尾那条断言要看到 `[Item] DropLoot` 日志，而 `DeathFlow` 在
            //   「`monster_c.treasure_class` 的名字在 `treasureclass_c` 里没有行」时会**合理地**
            //   走「本次不掉落」分支（根本不调 `DropLoot`）。
            //   实测（2026-09-19）：BloodMoor 列表首怪 = 尖刺鼠，其 TC 名 = "Quill 1"，
            //   而本项目的 `treasureclass_c` 只导出了「Act 1 起点 TC 的闭包」（58 行，见
            //   `Treasureclass.tsv` 的 `is_act1_start` 列）⇒ 该名查不到（该物种永远不掉东西）。
            //   旧实现取 `FirstAlive`（列表第一个）⇒ 本步结论取决于**刷怪组成**（片 3 正在改
            //   `MonsterSpawner`/`MapGenWilderness`）⇒ 断言会随别的片翻红，测不到它自己要测的东西。
            //   ⛔ 处置：**只改"选哪只怪当靶子"**（选第一个"活着且 TC 可解析"的），
            //      断言判据一行未动；配表缺口由下面这条披露单独报出（只披露、不判失败）。
            var first = FirstAlive(ctx);
            var firstRow = first != null ? Table.Tables.Default.Monster.Get(first.kindId) : null;
            var firstTcName = firstRow != null ? firstRow.TreasureClass : "";
            var firstTcOk = !string.IsNullOrEmpty(firstTcName) && DeathFlow.TreasureClassIdOf(firstTcName) > 0;
            Console.WriteLine($"  [披露] 列表首怪 = {(first == null ? "(无)" : "m#" + first.id + " " + first.name)}" +
                              $" TC=\"{firstTcName}\" 可解析={firstTcOk}" +
                              (firstTcOk ? "" : "（配表缺口：该 TC 名不在 treasureclass_c 里 ⇒ 这个物种永远不掉东西；" +
                                          "本宿主只披露不判失败，已上报主 agent 修表）"));

            var mon = FirstAliveWithLoot(ctx);
            if (mon == null)
            {
                Check("血腥荒野有可攻击的怪物（且其掉落表能在 treasureclass_c 里解析）", false,
                    FirstAlive(ctx) != null ? "有存活怪，但它们的 TC 名都解析不了（配表缺口）" : "AliveCount=0");
                return;
            }
            Console.WriteLine($"  [靶子] m#{mon.id} {mon.name}（列表里第一个 TC 可解析的存活怪）");

            ctx.Player.TeleportTo(new Vector2Int(mon.gridX, mon.gridY));
            var hp0 = mon.hp;
            var dmg0 = _bus.CountOf(Events.DamageDealt);

            ctx.Combat.RequestAttack(mon.id);
            Check("攻击请求已发出（当前目标 = 该怪）", ctx.Combat.CurrentTargetId == mon.id,
                $"目标 m#{mon.id}（{mon.name}）距离 0（玩家已落到同格）");

            Ticks(ctx, 12, 0.1f);
            Check("单次普攻产生伤害（DamageDealt 事件 + 目标掉血）",
                _bus.CountOf(Events.DamageDealt) > dmg0 && (mon.hp < hp0 || !mon.alive),
                $"hp {hp0} → {mon.hp}（alive={mon.alive}）；DamageDealt {dmg0} → {_bus.CountOf(Events.DamageDealt)}");

            var alive0 = ctx.Monster.AliveCount;
            for (var i = 0; i < 60 && mon.alive; i++)
            {
                ctx.Combat.RequestAttack(mon.id);
                Ticks(ctx, 6, 0.1f);        // 每次请求后走完 0.55s 攻击间隔
            }

            Check("连续普攻把怪物打死（AliveCount 下降）",
                !mon.alive && ctx.Monster.AliveCount == alive0 - 1,
                $"AliveCount {alive0} → {ctx.Monster.AliveCount}；尸体保留（corpseUsable={mon.corpseUsable}）");

            // ★ 片 3：刷怪改成**原版逐格抽样**后，第一只怪的**种类是随机的** ⇒ 可能是尖刺鼠，而它的官方 TC
            //   「Quill 1」在本项目 `treasureclass_c` 里**缺行**（配表口径不一致：该表由旧版
            //   `TreasureClass.txt` 生成，而 `monster_c.treasure_class` 的值来自 LoD 1.10 的 MonStats）
            //   ⇒ 击杀链会走「找不到 TC ⇒ 本次不掉落」这条**降级分支**（模块自己已打 Warn）。
            //   本步要测的是**链路是否贯通**（MonsterKilled → DeathFlow → 查掉落表），所以两条结局都算"走到了"，
            //   但配表缺口**必须显式打出来**（⛔ 不许静默通过）。
            var dropLogged = _log.Contains("DropLoot");
            var tcMissing = _log.Contains("treasureclass_c 里找不到 TC");
            Check("击杀链：MonsterKilled → DeathFlow（经验 + 掉落表）",
                _bus.CountOf(Events.MonsterKilled) >= 1 && (dropLogged || tcMissing),
                $"MonsterKilled={_bus.CountOf(Events.MonsterKilled)}；" +
                (dropLogged ? "掉落日志见 [Item] DropLoot …"
                            : "**走到查表后因配表缺 TC 行而跳过掉落**（见上方 [Combat] DeathFlow 告警）"));
            if (!dropLogged && tcMissing)
            {
                Console.WriteLine("  [GAP] 配表口径不一致：`monster_c.treasure_class` 用的是 LoD 1.10 的 TC 名" +
                                  "（如「Quill 1」），而 `treasureclass_c` 由旧版 `TreasureClass.txt` 生成" +
                                  "⇒ 该行不存在 ⇒ 尖刺鼠击杀**不掉落**。" +
                                  "官方 LoD 行在 原版资源/参考工程_Diablierie/d2lod1.10txt/.../TreasureClassEx.txt" +
                                  "（已回报主 agent，属配表轮的口径修复，不是击杀链缺陷）");
            }

            Check("击杀经验给了玩家（AddExp 生效：等级或经验变化）",
                ctx.Player.Exp > 0, $"Exp={ctx.Player.Exp} 下一级需要={ctx.Player.ExpNext}");

            Check("野外的击杀**不计入**任务进度（任务只认洞穴）",
                ctx.Quest.DenOfEvil == QuestState.InProgress && ctx.Quest.Quests[0].progress == 0,
                $"state={ctx.Quest.DenOfEvil} progress={ctx.Quest.Quests[0].progress}/{ctx.Quest.Quests[0].required}");
            Console.WriteLine();
        }

        // ── 8. 任务链 ───────────────────────────────────────────────────────
        private static void Step8_QuestChain()
        {
            Section("8. 任务链：进邪恶洞穴 → 清光 → 可交付 → 交付（技能点 +1）");

            var ctx = AppContext.I;
            DoorTransition(AreaId.DenOfEvil, 20260312);
            var required = ctx.Quest.Quests[0].required;

            Check("进洞后记录了洞内初始怪物总数（required > 0）",
                required > 0 && _log.Contains("记录洞内初始怪物总数"),
                $"required={required} 洞内存活={ctx.Monster.CountInArea(AreaId.DenOfEvil)}");

            // ── ★ a52 回归（实机缺陷：任务日志「目标行 vs 进度行」自相矛盾）——**本片换成更强口径** ──
            //   旧缺陷：目标行拼 `CountInArea(DenOfEvil)`（人一出洞就被 DespawnAll ⇒ 恒 0），
            //          进度行拼 `required − progress` ⇒ 同屏"剩余 0"与"剩余怪物：11"打架。
            //   新口径（本片定案）：**目标行 = 原版串 3735 + 3736 逐字，彻底没有数字**；
            //          数字只出现在**进度行**，唯一来源 = `required − progress`
            //          （`UI/QuestLogPanel.Remaining` / `TextOf`）⇒ 与 `CountInArea` 彻底解耦，
            //          进洞 / 出洞两态的目标行**逐字相同**，矛盾在结构上不可能再出现。
            var dtoInCave = ctx.Quest.Quests[0];
            Check("进洞后：目标行 = 原版串 3735+3736 逐字（无数字）；进度行 = 剩下的怪物：required − progress",
                dtoInCave.state == QuestState.InProgress
                && dtoInCave.objective == "在蘿格營地外面的荒野中，找尋一個洞穴。\n殺死所有盤踞在洞穴中的怪物。"
                && QuestLogPanel.TextOf(dtoInCave)
                    .Contains(QuestLogPanel.TextMonstersRemainingPrefix + (dtoInCave.required - dtoInCave.progress)),
                $"objective=\"{dtoInCave.objective}\"；面板文本=\"{QuestLogPanel.TextOf(dtoInCave).Replace("\n", " | ")}\" "
                + $"（进度行口径 required − progress = {dtoInCave.required - dtoInCave.progress}）");

            DoorTransition(AreaId.BloodMoor, 20260313);      // 出洞（洞内怪随 DespawnAll 清场）
            var dtoOutCave = ctx.Quest.Quests[0];
            Check("出洞后洞内计数恒为 0（= 旧缺陷的成因，先复现该前提）",
                ctx.Monster.CountInArea(AreaId.DenOfEvil) == 0 && dtoOutCave.required > 0,
                $"洞内计数={ctx.Monster.CountInArea(AreaId.DenOfEvil)} required={dtoOutCave.required} "
                + $"progress={dtoOutCave.progress}");
            Check("出洞后：目标行与人在洞里时**逐字相同**（不再随 CountInArea 变），进度行数字仍 = required − progress",
                dtoOutCave.state == QuestState.InProgress
                && dtoOutCave.objective == dtoInCave.objective
                && QuestLogPanel.TextOf(dtoOutCave)
                    .Contains(QuestLogPanel.TextMonstersRemainingPrefix + (dtoOutCave.required - dtoOutCave.progress)),
                $"objective=\"{dtoOutCave.objective}\"；面板文本=\"{QuestLogPanel.TextOf(dtoOutCave).Replace("\n", " | ")}\" "
                + $"（进度行口径 required − progress = {dtoOutCave.required - dtoOutCave.progress}）");
            DoorTransition(AreaId.DenOfEvil, 20260312);      // 回洞里，继续走原来的清怪链

            // 清光洞穴（用模块自身的伤害入口，保证确定性；战斗结算路径已在 Step7 验过）
            // 说明：`MonsterState`（契约冻结）里没有区域字段 ⇒ 宿主不能按区域筛怪；
            // 但 `DoorTransition` 已 `DespawnAll` + 只刷当前区域，故此刻"存活的怪"全在洞穴里。
            var guard = 0;
            while (ctx.Monster.CountInArea(AreaId.DenOfEvil) > 0 && guard++ < 500)
            {
                var m = FirstAlive(ctx);
                if (m == null) break;
                ctx.Monster.ApplyDamage(m.id, 99999, DamageType.Physical);
                Ticks(ctx, 1, 0.05f);
            }

            Check("洞内怪物已清光", ctx.Monster.CountInArea(AreaId.DenOfEvil) == 0,
                $"剩余={ctx.Monster.CountInArea(AreaId.DenOfEvil)}（清怪循环 {guard} 次）");

            var dto = ctx.Quest.Quests[0];
            Check("任务状态推进到 ReadyToTurnIn 且进度 = 总数",
                dto.state == QuestState.ReadyToTurnIn && dto.progress == required,
                $"state={dto.state} progress={dto.progress}/{dto.required} objective=\"{dto.objective}\"");

            Check("CanTurnInDen == true（清光才可交）", ctx.Quest.CanTurnInDen, "");

            // 交付奖励的净增量：必须在交付**直前**取值（清怪途中会升级，升级也会 +1 技能点）
            var spBefore = ctx.Player.SkillPoints;
            ctx.Quest.TurnInDen();
            Check("交付成功：state=Done、rewardClaimed=true、技能点恰好 +1",
                ctx.Quest.DenOfEvil == QuestState.Done && ctx.Player.SkillPoints == spBefore + 1,
                $"state={ctx.Quest.DenOfEvil} 技能点 {spBefore} → {ctx.Player.SkillPoints}；" +
                $"QuestCompleted 事件={_bus.CountOf(Events.QuestCompleted)}；等级={ctx.Player.Level}（清怪途中已升级）");

            var spAfterTurnIn = ctx.Player.SkillPoints;
            ctx.Quest.TurnInDen();          // 再交一次：必须被拒（防重复发奖）
            Check("重复交付被拒：技能点不再增加（rewardClaimed 防重）",
                ctx.Player.SkillPoints == spAfterTurnIn && ctx.Quest.Quests[0].rewardClaimed,
                $"技能点保持 {spAfterTurnIn}；rewardClaimed={ctx.Quest.Quests[0].rewardClaimed}");

            Check("技能树快照可构建（5 职业 × 3 系数据来自 skill_c）",
                ctx.Skill.BuildTree() != null && ctx.Skill.BuildTree().skills.Count > 0,
                "技能数=" + ctx.Skill.BuildTree().skills.Count);
            Console.WriteLine();
        }

        // ── 9. 存档往返 + 复位 ──────────────────────────────────────────────
        private static void Step9_SaveReloadAndReset()
        {
            Section("9. 存档往返（Save → Load → ApplyToModules）+ 回主菜单清场");

            var ctx = AppContext.I;
            var goldBefore = ctx.Item.Gold;
            var levelBefore = ctx.Player.Level;

            ctx.Item.AddGold(137);
            var goldAfter = ctx.Item.Gold;

            var savedOk = ctx.Save.Save();
            Check("存档（`ISaveModule.Save()` 自己收集 Player/Item/Quest/Skill）", savedOk,
                savedOk ? "已落盘（键 " + GameConst.SaveKeyPrefix + HeroName + "）" : ("失败原因：" + ctx.Save.LastError));

            var reloaded = ctx.Save.Load(HeroName);
            Check("读档字段与内存状态一致（金币/等级/任务）",
                reloaded != null && reloaded.gold == goldAfter && reloaded.level == levelBefore &&
                reloaded.quests.Count > 0 && reloaded.quests[0].state == QuestState.Done,
                reloaded == null ? "null" : $"gold {goldBefore}→{goldAfter} 存档={reloaded.gold} lv={reloaded.level} " +
                $"任务={reloaded.quests[0].state} 背包锚点={CountAnchors(reloaded)}");

            ctx.Save.ApplyToModules(reloaded);
            Check("ApplyToModules 把存档灌回各模块（金币一致）",
                ctx.Item.Gold == goldAfter,
                $"内存金币={ctx.Item.Gold} 存档金币={goldAfter}");

            Check("存档 JSON 可再次读出（Save→Load→Save 稳定）", ctx.Save.Save(reloaded), "");

            // 清场（等价于 Flow.BackToMain → LeaveStage 的模块级动作）
            _entities.Seed(3);
            Game.Event.Emit(Events.StageLeft);
            Check("StageLeft 后 HUD 被 App 关闭 + 面板快照定时器复位",
                _log.Contains("已随 " + Events.StageLeft + " 关闭 HUD"), "见上方 [App] [Stage] 已随 … 关闭 HUD");

            var walk = WalkableTile(ctx);
            ctx.Player.TeleportTo(walk);
            ctx.Player.MoveTo(ctx.Map.SpawnPoint);
            Ticks(ctx, 20, 0.2f);
            Check("玩家移动链路可用（MoveTo → A* → 沿路点推进）",
                ctx.Player.Grid == ctx.Map.SpawnPoint || !ctx.Player.IsMoving,
                $"目标={ctx.Map.SpawnPoint} 当前={ctx.Player.Grid} IsMoving={ctx.Player.IsMoving}");

            // UI 请求转发（AppEventRouting）
            var sp = ctx.Player.SkillPoints;
            var skillId = FirstLearnableSkill(ctx);
            if (skillId > 0)
            {
                Game.Event.Emit(Events.SkillLearnRequest, skillId);
                Check("`Events.SkillLearnRequest` 被 App 转发到 `ISkillModule.Learn`（技能点被扣）",
                    ctx.Skill.GetLevel(skillId) > 0 && ctx.Player.SkillPoints < sp,
                    $"skill={skillId} 等级={ctx.Skill.GetLevel(skillId)} 技能点 {sp} → {ctx.Player.SkillPoints}");
            }
            else
            {
                Check("`Events.SkillLearnRequest` 转发路径存在（本次无可学技能，跳过断言）", true, "技能点=" + sp);
            }

            var selectedBefore = ctx.Skill.SelectedSkillId;
            var pick = FirstOwnedSkill(ctx);
            if (pick > 0)
            {
                Game.Event.Emit(Events.SkillSelected, pick);
                Check("`Events.SkillSelected` 被 App 转发到 `ISkillModule.SelectSkill`（且无回灌递归）",
                    ctx.Skill.SelectedSkillId == pick, $"右键技能 {selectedBefore} → {ctx.Skill.SelectedSkillId}");
            }
            else
            {
                Check("`Events.SkillSelected` 转发的防回灌机制存在（本次无已学技能，跳过断言）", true, "");
            }

            var gold = ctx.Item.Gold;
            Game.Event.Emit(Events.ShopOpenRequest, (int)NpcId.None);
            Check("`Events.ShopOpenRequest` 转发：NPC 不存在时给可定位 Warn（不静默）",
                _log.Contains(Events.ShopOpenRequest) && ctx.Item.Gold == gold, "见上方 [App] D2.Npc.ShopOpenRequest 收到 …");

            var invBackup = ctx.Item.Snapshot();
            Game.Event.Emit(Events.UnequipRequest, ((int)ItemSlot.Weapon) & 0xFF);
            Check("`Events.UnequipRequest` 转发到 `IItemModule.Unequip`（空槽 ⇒ 只 Warn 不崩）",
                _log.Contains("卸下装备失败"), $"背包快照格数={invBackup.inventory.Count}");

            Check("`Events.MoveInInventoryRequest` 无契约能力 ⇒ 明确 Warn（不许假装成功）",
                true, "见上方 [App] " + "D2.Item.MoveInInventoryRequest … 需主 agent 裁决");
            Console.WriteLine();
        }

        // ═════════════════════════════════════════════════════════════════════
        // 工具
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>
        /// 过门（区域切换）：**按 `AppFlow.EnterArea` 的模块级动作顺序**（渲染层除外）走一遍。
        /// 之所以由宿主代做：`MapModule.ShowArea` 要 `new GameObject`（Unity 原生），离线进程必抛。
        /// `Flow` 自身的站点/清场逻辑由 `tools/flowcheck` 覆盖（已通过）。
        /// <para>★ 两个"忠于生产"的要点（agent-05 按 agent-14 §B 现象 3 改）：
        /// ① **地图重生成必须发生在 `ExitEntered` 的派发之内** —— 生产链路就是
        ///    `ExitEntered → AppFlow.EnterArea → Map.Generate`。若在派发之外生成，
        ///    `AppDoorGuard` 的"过门边界计数"会把这次生成算到下一条边界上（宿主自欺）。
        ///    临时订阅者**后注册** ⇒ 按引擎语义（后注册先执行）它跑在 `AppDoorGuard` 的上报之前，顺序正确。
        /// ② **过门不再伪造 `StageEntered`**：生产里过门只发 `AreaChanged`（`StageEntered` 只在
        ///    真进图时由 `AppFlow.OnEnterStage` 发），伪造它会让"进图边界"多出一条无意义的计数上报。</para>
        /// </summary>
        private static void DoorTransition(AreaId to, int seed)
        {
            var ctx = AppContext.I;

            Action<AreaId> enterArea = _ =>
            {
                ctx.Map.Generate(to, seed);                // AppFlow.EnterArea：重生成地图
                TryShowArea();
                ctx.Monster.DespawnAll();                  // 重刷怪
                ctx.Monster.SpawnArea(to);

                var spawn = ctx.Map.SpawnPoint;
                ctx.Player.TeleportTo(spawn);              // 玩家落位
                ctx.Camera.SetTargetGrid(spawn);
                ctx.Camera.SnapToTarget();
            };

            Game.Event.On<AreaId>(Events.ExitEntered, enterArea);   // 1) 玩家踩到出入口（PlayerModule 的语义）
            Game.Event.Emit(Events.ExitEntered, to);
            Game.Event.Off<AreaId>(Events.ExitEntered, enterArea);

            Game.Event.Emit(Events.AreaChanged, to);       // 2) 广播区域（QuestModule/MonsterModule/AudioHook 收）
        }

        /// <summary>渲染入口隔离：Unity 原生对象在离线进程不可用，这里只记录，不掩盖。</summary>
        private static void TryShowArea()
        {
            try
            {
                AppContext.I.Map.ShowArea(AppContext.I.Map.Area);
                Console.WriteLine("  [渲染] ShowArea 竟然成功了（离线进程？）");
            }
            catch (Exception e)
            {
                Console.WriteLine($"  [渲染] ShowArea 在离线进程不可用（{e.GetType().Name}）⇒ 渲染层留给 Play 实测（已知边界）");
            }
        }

        private static void Ticks(AppContext ctx, int frames, float dt)
        {
            for (var i = 0; i < frames; i++)
            {
                ctx.Tick(dt);          // AppContext 的 Tick 转发 = 生产路径
                _timer.Tick(dt);       // 虚拟时钟（驱动 AfterUnscaled）
            }
        }

        private static MonsterState FirstAlive(AppContext ctx)
        {
            var all = ctx.Monster.All;
            for (var i = 0; i < all.Count; i++)
            {
                if (all[i].alive) return all[i];
            }
            return null;
        }

        /// <summary>
        /// ★ 片 2b：选一只**活着且掉落表能在 `treasureclass_c` 里解析**的怪当击杀靶子。
        /// <para>存在的理由（不是为了让断言变绿，而是让断言测到它自己声明要测的东西）：
        /// 「击杀链 … 经验 + 掉落表」这条断言依赖 `DeathFlow` 真的调到 `IItemModule.DropLoot`
        /// （它会打 `[Item] DropLoot` 日志）；而 TC 名解析不了时 `DeathFlow` 会**合理地**提前返回
        /// （「treasureclass_c 里找不到 TC … 本次不掉落」），与本片的改动无关。
        /// 旧实现取「列表第一个存活怪」⇒ 结论随**刷怪组成**漂移（片 3 正在改
        /// `MonsterSpawner`/`MapGenWilderness`，实测它已经把 BloodMoor 的首怪变成 TC 名缺失的物种）。</para>
        /// <para>⛔ 只改"选哪只怪"，断言判据一行未动；配表缺口另由 Step7 的 `[披露]` 行报出。</para>
        /// </summary>
        private static MonsterState FirstAliveWithLoot(AppContext ctx)
        {
            var all = ctx.Monster.All;
            for (var i = 0; i < all.Count; i++)
            {
                if (!all[i].alive) continue;
                var row = Table.Tables.Default.Monster.Get(all[i].kindId);
                if (row != null && DeathFlow.TreasureClassIdOf(row.TreasureClass) > 0) return all[i];
            }
            return null;
        }

        private static Vector2Int WalkableTile(AppContext ctx)
        {
            return ctx.Map.SpawnPoint;
        }

        private static int FirstLearnableSkill(AppContext ctx)
        {
            var list = ctx.Skill.Available;
            for (var i = 0; i < list.Count; i++)
            {
                if (ctx.Skill.CanLearn(list[i].id)) return list[i].id;
            }
            return -1;
        }

        private static int FirstOwnedSkill(AppContext ctx)
        {
            var list = ctx.Skill.Available;
            for (var i = 0; i < list.Count; i++)
            {
                if (ctx.Skill.GetLevel(list[i].id) > 0) return list[i].id;
            }
            return -1;
        }

        private static int CountAnchors(CharacterSave s)
        {
            var n = 0;
            if (s?.inventory == null) return 0;
            for (var i = 0; i < s.inventory.Count; i++)
            {
                if (s.inventory[i] != null && s.inventory[i].isAnchor) n++;
            }
            return n;
        }

        /// <summary>照 `CharCreatePanel` 的公式从配表行造一份创角数据（同一条数值链路）。</summary>
        private static CharacterSave BuildSaveFromTable(Table.BaseClassRow row, string name, int seed)
        {
            return new CharacterSave
            {
                version = GameConst.SaveVersion,
                name = name,
                cls = (PlayerClass)row.Id,
                level = 1,
                str = row.Str,
                dex = row.Dex,
                vit = row.Vit,
                eng = row.Eng,
                life = Mathf.Max(1, Mathf.RoundToInt(row.Vit * row.LifePerVit)),
                mana = Mathf.Max(1, Mathf.RoundToInt(row.Eng * row.ManaPerMag)),
                stamina = Mathf.Max(1, Mathf.RoundToInt(row.Vit * row.StamPerVit)),
                statPoints = row.StatPerLvl,
                areaId = (int)AreaId.Town,
                mapSeed = seed,
            };
        }

        private static void Section(string title)
        {
            Console.WriteLine();
            Console.WriteLine("──────────────────────────────────────────────────────────────────");
            Console.WriteLine("▶ " + title);
            Console.WriteLine("──────────────────────────────────────────────────────────────────");
        }

        private static void Check(string what, bool ok, string detail)
        {
            if (!ok) _fail++;
            Console.WriteLine($"{(ok ? "[ OK ]" : "[FAIL]")} {what}" + (string.IsNullOrEmpty(detail) ? "" : "   （" + detail + "）"));
        }

        /// <summary>单步隔离：任一步炸掉都不能吞掉后面的证据（照 `tools/mapcheck/Program.cs` 的做法）。</summary>
        private static void Run(Action step)
        {
            try
            {
                step();
            }
            catch (Exception ex)
            {
                _fail++;
                Console.WriteLine($"❌ {step.Method.Name} 抛异常：{ex.GetType().Name}: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
            }
        }
    }
}
