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

        /// <summary>
        /// 该事件当前的**监听器数量**（★ 片 assert-audit 新增）。
        /// 用途：判「转发路径存在」这类结论 —— 判据从「写死 OK」变成**真的问一次总线**
        /// （`AppEventRouting.Install` 没订阅 ⇒ 0 ⇒ 断言变红）。与 `uicheck` 的 `HandlerCount` 同口径。
        /// </summary>
        public int HandlerCount(string eventName)
            => _handlers.TryGetValue(eventName, out var list) ? list.Count : 0;

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

        /// <summary>
        /// 宿主槽位档的**沙盒目录** = `&lt;仓库根&gt;/.ai-tmp/test/host-setting/&lt;宿主名&gt;`（每次跑前清空）。
        /// <para>为什么必须显式给 `Game.Config.SettingDir`（2026-09-20 闸门/卫生对齐轮）：
        /// `Module/Save/SaveModule.cs:96-98` 在 `Game.Config` 为空时回落**相对目录** `"setting"`
        /// ⇒ 槽位档落在 `&lt;调用方 cwd&gt;/setting/saves/`。于是：① 从仓库根跑
        /// `dotnet run --project tools/probes/hosts/fullcheck` 就在**仓库根**留一份
        /// `setting/saves/FullCheckHero.json`（实测 2026-09-20：仓库根 `setting/` 未入仓、
        /// 违 skill §1.8「一次性产物只许 `.ai-tmp/test/`」）；② `run_all_hosts.ps1`
        /// （`Push-Location`）则写进**宿主目录**下那份**已入仓**的 `setting/saves/` ⇒
        /// **验证器每次跑都改脏它验证的检出**。指向 `.ai-tmp/` 沙盒并每次清空 ⇒
        /// 不依赖 cwd、不留仓库残留、断言真正从零开始。业务断言一字未改。</para>
        /// </summary>
        private static string HostSandboxSettingDir(string host)
        {
            var p = System.IO.Path.Combine(ResolveProjectRoot(), ".ai-tmp", "test", "host-setting", host);
            try
            {
                if (System.IO.Directory.Exists(p)) System.IO.Directory.Delete(p, true);
                System.IO.Directory.CreateDirectory(p);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[warn] 沙盒目录不可用（{p}）：{ex.GetType().Name}: {ex.Message}");
            }
            return p;
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

            // ★ 片 ground-item-icon：地面物品图（原版物品图 vs 品质色块）—— 独立文件，只加断言。
            //   走 Run(...) 包一层：单步隔离（炸掉也不吞掉后面的汇总输出）。
            Run(() => { _fail += GroundIconCheck.Run(); });

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
            // ★ 槽位档沙盒（2026-09-20 闸门/卫生对齐轮）：显式给 SaveModule 一个绝对 `SettingDir`
            //   ⇒ 不再跟随 cwd 在仓库根 / 宿主目录留 `setting/saves/` 残留（详见 HostSandboxSettingDir）。
            Game.Config = new GameConfig { SettingDir = HostSandboxSettingDir("fullcheck") };

            // ★ 片 assert-audit：原为硬编码 `true`（永真 ⇒ 等于没判）。改成**逐个门面真的问一次非空**。
            Check("引擎门面替身已就位（Logger/Event/Fsm/UI/Scene/Setting/Input/Sound/Entity/Pool/Timer/Res）",
                Game.Logger != null && Game.Event != null && Game.Fsm != null && Game.UI != null
                && Game.Scene != null && Game.Setting != null && Game.Input != null && Game.Sound != null
                && Game.Entity != null && Game.Pool != null && Game.Timer != null && Game.Res != null
                && Game.IsRunning,
                "12 个门面逐个非空 + IsRunning；单机最小集：**不调** CloverNet.Init（本项目形态=单机）");

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
            var warmOk = false;                  // ★ 片 assert-audit：原 Check 是硬编码 `true` ⇒ 逐值真判
            try
            {
                warm = $"bgm={Cfg.BgmVolume:0.00} sfx={Cfg.SfxVolume:0.00} fullscreen={Cfg.Fullscreen} " +
                       $"name={Cfg.DefaultPlayerName}";
                // 判据 = 「不抛异常」+「回落的默认值**真的可用**」（音量在 0~1、默认名非空）
                warmOk = Cfg.BgmVolume >= 0f && Cfg.BgmVolume <= 1f
                      && Cfg.SfxVolume >= 0f && Cfg.SfxVolume <= 1f
                      && !string.IsNullOrWhiteSpace(Cfg.DefaultPlayerName);
            }
            catch (Exception e)
            {
                Console.WriteLine("  [探针] Cfg 预热异常（按默认值继续）：" + e.GetType().Name + ": " + e.Message);
            }
            Log.Suppress = false;

            Check("Cfg（config.json）已预热（离线读不到文件 ⇒ 回落默认值，不抛异常）", warmOk, warm);

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

            // ── 3b. ★ hover-probe 片：`D2.Input.HoverChanged` **往返**（发送方 + 消费方都在线）──
            //   为什么补它：状态矩阵 L3801/L3802 判「有订阅者（被消费）」，旧证据只引
            //   `Core/Events.cs` / `Module/Contracts.cs`（事件的**声明处本身**）⇒ 只证得出
            //   "事件名 + 载荷类存在"，证不出"真有人收到"（而且它就是 freshness 比对的文件 ⇒ 恒红）。
            //   本宿主是**唯一同时编进 `Module/Input/InputReader`（发送方）与 `UI/EntityTooltip`
            //   （消费方）**的宿主 ⇒ 两端都要断言，并做退化校验（摘掉订阅方 ⇒ 收不到）。
            //   ⚠️ 载荷类型写全限定名 `Diablo2.Def.HoverTarget`：`using Diablo2.UI;` 下另有一个
            //      同名 MonoBehaviour（`UI/HoverTarget.cs`）⇒ 裸写 `HoverTarget` 是 CS0104。
            Check("事件名常量 == 状态矩阵实体 id `D2.Input.HoverChanged`",
                Events.HoverTargetChanged == "D2.Input.HoverChanged", Events.HoverTargetChanged);

            var hoverReceived = 0;
            Diablo2.Def.HoverTarget hoverGot = null;
            Action<Diablo2.Def.HoverTarget> onHover = h => { hoverReceived++; hoverGot = h; };

            Game.Event.On<Diablo2.Def.HoverTarget>(Events.HoverTargetChanged, onHover);
            var hoverPayload = new Diablo2.Def.HoverTarget
            {
                hasTarget = true, cursor = CursorKind.Attack, id = 4242,
                name = "悬停自检", gridX = 7, gridY = 9,
            };
            Game.Event.Emit(Events.HoverTargetChanged, hoverPayload);

            Check("★ 往返：真实订阅回调收到 `D2.Input.HoverChanged`（1 次 + 载荷同一实例）",
                hoverReceived == 1 && ReferenceEquals(hoverGot, hoverPayload),
                hoverGot == null ? "回调收到 null"
                                 : $"回调 {hoverReceived} 次 id={hoverGot.id} cursor={hoverGot.cursor}" +
                                   $" 同一实例={ReferenceEquals(hoverGot, hoverPayload)}");

            Game.Event.Off<Diablo2.Def.HoverTarget>(Events.HoverTargetChanged, onHover);
            var hoverAfterOff = hoverReceived;
            Game.Event.Emit(Events.HoverTargetChanged, hoverPayload);
            Check("★ 退化：摘掉订阅方（Off）⇒ 同一条派发不再进回调（断言不是摆设）",
                hoverReceived == hoverAfterOff, $"回调 {hoverAfterOff} → {hoverReceived}");

            // 发送方入口（真派发的那一支）：`InputReader.UpdateHover(bool)` 必须存在且在
            // `PlayerModule.Tick` 里被调 —— 前者反射查编译产物，后者查源码行（口径见回报）。
            var upd = typeof(InputReader).GetMethod("UpdateHover",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic);
            Check("★ 发送方入口存在：`Module/Input/InputReader.UpdateHover(bool)`",
                upd != null && upd.GetParameters().Length == 1
                && upd.GetParameters()[0].ParameterType == typeof(bool),
                upd == null ? "找不到 UpdateHover"
                            : $"{upd.Name}({upd.GetParameters()[0].ParameterType.Name})");

            var et = typeof(EntityTooltip).GetMethod("OnHoverChanged",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic);
            var etPs = et != null ? et.GetParameters() : new System.Reflection.ParameterInfo[0];
            Check("★ 消费点存在：`UI/EntityTooltip.OnHoverChanged(Diablo2.Def.HoverTarget)`",
                et != null && etPs.Length == 1 && etPs[0].ParameterType == typeof(Diablo2.Def.HoverTarget),
                et == null ? "找不到 OnHoverChanged"
                           : $"{et.Name}({(etPs.Length == 1 ? etPs[0].ParameterType.FullName : "参数个数=" + etPs.Length)})");

            // 宿主没有 `using System.IO`（保持既有 using 不动）⇒ 一律写全名。
            var irSrc = ClientAssets + @"\Scripts\Module\Input\InputReader.cs";
            var emitLine = System.IO.File.Exists(irSrc)
                ? Array.FindIndex(System.IO.File.ReadAllLines(irSrc), l => l.Contains("Emit(Events.HoverTargetChanged"))
                : -1;
            Check("★ 发送方真的 Emit：`InputReader.cs` 里存在 `Emit(Events.HoverTargetChanged, …)`",
                emitLine >= 0, emitLine >= 0 ? $"{irSrc}:{emitLine + 1}" : ("读不到 " + irSrc));

            var pmSrc = ClientAssets + @"\Scripts\Module\Player\PlayerModule.cs";
            var callLine = System.IO.File.Exists(pmSrc)
                ? Array.FindIndex(System.IO.File.ReadAllLines(pmSrc), l => l.Contains("_input.UpdateHover("))
                : -1;
            Check("★ 发送方每帧被驱动：`PlayerModule.cs` 里存在 `_input.UpdateHover(` 调用",
                callLine >= 0, callLine >= 0 ? $"{pmSrc}:{callLine + 1}" : ("读不到 " + pmSrc));
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
            // ★ 片 assert-audit：原为硬编码 `true` ⇒ 改成真的读一次 `CameraRig.TargetGrid`
            //   （离线无相机时 `SnapToTarget` 只推进内部状态，但 `SetTargetGrid` 的落点必须真存下来）。
            var rig = ctx.Camera as CameraRig;
            Check("跟随相机已对准玩家（`SetTargetGrid` 的落点 == 玩家格；无相机时只在内部状态推进）",
                rig != null && rig.TargetGrid == ctx.Player.Grid,
                $"CameraRig.TargetGrid={(rig != null ? rig.TargetGrid.ToString() : "(不是 CameraRig)")} "
                + $"玩家格={ctx.Player.Grid}");

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

            // ★ C3（用户本轮「你是圆形判断的打击范围」）之后近战有**形状**闸门：
            //   正面扇形 ±60° + 以朝向为轴的矩形走廊 + 线段不得被地形阻断；唯一出处
            //   `Module/Combat/MeleeShape.cs`。
            //   ⚠️ 2026-09-23（片 `melee-samecell`）**更正**：该文件头原先写「零距离（与攻击者同格）
            //   ⇒ false」—— 那是**错的**：原版近战触及是**距离 / 外接框**口径（`Weapons.txt` 第 20 列
            //   `rangeadder` / `MonStats2.txt` 第 8 列 `MeleeRng`），`0 ≤ reach` 恒真 ⇒ **同格必命中**；
            //   而实测（`.ai-tmp/test/report-audioverify2.md` §2.3）玩家沿 `MoveCommand` 就会走到怪格上，
            //   旧口径下 40 次真实左键**全被拒** ⇒ 贴身永远打不到。该退化点已修（见文件头）。
            //   本节因此**同时**覆盖两种姿态：正前方一格（既有，下面那条）与**同格**（新增，见下）。
            var dv = Iso.DirectionDelta(ctx.Player.Dir);

            // ★ melee-samecell 的**纯函数**判据（与 `combatcheck` 第 18 节同一把尺子；⛔ 不改上一条断言）
            {
                float sfx, sfy;
                var nv2 = Iso.DirectionDelta(Diablo2.Def.Dir8.N);   // ⛔ 必须限定：本宿主同时可见 CloverEngine.Dir8
                var solvable = MeleeShape.ToUnit(nv2.x, nv2.y, out sfx, out sfy);
                const float sReach = 1.6f;
                Check("纯函数：正前方 1.5 格 ⇒ 命中",
                    solvable && MeleeShape.InFrontCone(sfx, sfy, 0f, -1.5f, MeleeShape.FrontConeCos)
                    && MeleeShape.InMeleeRect(sfx, sfy, 0f, -1.5f, sReach, MeleeShape.MeleeHalfWidth),
                    "偏移 (0,-1.5)");
                Check("纯函数：正侧方 1.5 格 ⇒ **不**命中（C3 定稿的\"扇形不是圆\"口径）",
                    solvable && !MeleeShape.InFrontCone(sfx, sfy, 1.5f, 0f, MeleeShape.FrontConeCos)
                    && !MeleeShape.InMeleeRect(sfx, sfy, 1.5f, 0f, sReach, MeleeShape.MeleeHalfWidth),
                    "偏移 (1.5,0)：90° 侧方");
                Check("纯函数：距离 > 攻击范围 ⇒ 不命中",
                    solvable && !MeleeShape.InMeleeRect(sfx, sfy, 0f, -2.5f, sReach, MeleeShape.MeleeHalfWidth),
                    "沿轴 2.5 > reach 1.60");
                Check("纯函数：**同格（偏移 (0,0)）⇒ 命中**（★ melee-samecell）",
                    solvable && MeleeShape.InFrontCone(sfx, sfy, 0f, 0f, MeleeShape.FrontConeCos)
                    && MeleeShape.InMeleeRect(sfx, sfy, 0f, 0f, sReach, MeleeShape.MeleeHalfWidth),
                    "零偏移受距离/框口径保护（0 ≤ reach 恒真），不参与角度比较");
            }
            // ★ 判据资产修复（2026-09-24，team-lead 批准）：原实现**写死**落点 = `mon - dv`，
            //   实测该格（Blood Moor 的 (7,15)）**可能不可走** ⇒ `PlayerModule` 走降级分支
            //   "Teleport 目标格不可走 ⇒ 改用出生点" ⇒ 玩家离靶 28.02 格 ⇒ 下面 4 条判据
            //   （单次普攻掉血 / 连击打死 / 击杀链 / 击杀经验）**连锁变红**，而它们要测的东西根本没被测到
            //   （失败发生在 `dist > MeleeRange` 分支，⛔ 根本走不到 `InFrontCone`）。
            //   现在在怪周围**找一个可走格**再传送：优先"正前方一格"（原意），其次朝向轴 ±45° 的邻格
            //   （同样能过形状闸门：cos45°=0.707 ≥ 0.5、沿轴 1.00 ≤ 1.60、垂距 1.00 ≤ 1.20），
            //   并要求线段通畅（不许站在墙后挥）。⛔ 判据条件本身（单次普攻必须掉血 / 连击必须打死）一字未动。
            float mfx, mfy;
            if (!MeleeShape.ToUnit(dv.x, dv.y, out mfx, out mfy)) { mfx = 0f; mfy = 0f; }
            var posture = FindMeleePosture(ctx, mon, mfx, mfy, new Vector2Int(mon.gridX - dv.x, mon.gridY - dv.y));
            ctx.Player.TeleportTo(posture);
            Console.WriteLine($"  [站位] 玩家=({ctx.Player.Grid.x},{ctx.Player.Grid.y}) 怪=({mon.gridX},{mon.gridY})"
                              + $" 朝向={ctx.Player.Dir} 偏移=({mon.gridX - ctx.Player.Grid.x},{mon.gridY - ctx.Player.Grid.y})"
                              + $" 距离={Iso.GridDistanceEuclidean(ctx.Player.Grid, new Vector2Int(mon.gridX, mon.gridY)):0.00}"
                              + $" 可走={ctx.Map.Walkable(ctx.Player.Grid)}"
                              + $"（判据资产修复：不再假设 `mon-dv` 一定可走；候选口径 = 可走 ∧ 锥 ∧ 走廊 ∧ 线段通）");

            var hp0 = mon.hp;
            var dmg0 = _bus.CountOf(Events.DamageDealt);

            ctx.Combat.RequestAttack(mon.id);
            Check("攻击请求已发出（当前目标 = 该怪）", ctx.Combat.CurrentTargetId == mon.id,
                $"目标 m#{mon.id}（{mon.name}）玩家格=({ctx.Player.Grid.x},{ctx.Player.Grid.y}) 朝向={ctx.Player.Dir} "
                + $"偏移=({mon.gridX - ctx.Player.Grid.x},{mon.gridY - ctx.Player.Grid.y}) 距离 "
                + $"{Iso.GridDistanceEuclidean(ctx.Player.Grid, new Vector2Int(mon.gridX, mon.gridY)):0.00} 格");

            Ticks(ctx, 12, 0.1f);
            Check("单次普攻产生伤害（DamageDealt 事件 + 目标掉血）",
                _bus.CountOf(Events.DamageDealt) > dmg0 && (mon.hp < hp0 || !mon.alive),
                $"hp {hp0} → {mon.hp}（alive={mon.alive}）；DamageDealt {dmg0} → {_bus.CountOf(Events.DamageDealt)}");

            // ★ melee-samecell（2026-09-23 缺陷修复）**真实链路**的同格用例：
            //   每次出手前把玩家挪到**怪所在那一格**（生产玩法里 `MoveCommand` 就会走到怪格上），
            //   再走生产入口 `RequestAttack` ⇒ 必须掉血。⛔ 只**新增**断言；
            //   跑完把站位**放回"怪的正前方一格"**（同一姿态口径）⇒ 下面击杀循环的既有判据条件不变。
            {
                // ⚠️ 本块**必须零副作用**（否则会把下面既有判据的初始条件搅乱）：
                //   ⓪ **不许打死 Step7 的靶子 `mon`** —— 它要留给下面"连续普攻把怪物打死"那条判据，
                //      而那条读的 `alive0 = AliveCount` 在本块**之后** ⇒ 块内打死 `mon` 会让它变红
                //      （实测踩到：AliveCount 26 → 26 FAIL）。⇒ 同格用例改打**除靶子外血最厚**的一只
                //      （血厚 ⇒ 一次命中打不死），并且**一旦掉血立刻停**（最多 1 次有效命中）。
                //   ① 出手前记下玩家**当前**格，跑完**原样放回**；
                //   ② 只推进 **Combat 模块自己的时钟**（清攻击冷却），⛔ 不用 `ctx.Tick` 推进怪物 AI
                //      （那会让怪在断言之间移动/脱战，属改变既有条件）。
                var scTarget = TankiestOther(ctx, mon.id);
                if (scTarget == null)
                {
                    Check("同格（偏移 (0,0)）攻击必须结算（★ melee-samecell，真实链路）", false,
                        "本步无法执行：血腥荒野里除靶子外没有别的存活怪（⚠️ 这是**未测到**，不是通过）");
                }
                else
                {
                    var restoreGrid = ctx.Player.Grid;
                    var lastOffset = "(n/a)";
                    var scHit = false;
                    var scAttempts = 0;
                    var hpBeforeSc = scTarget.hp;
                    for (var i = 0; i < 3 && !scHit && scTarget.alive; i++)
                    {
                        ctx.Player.TeleportTo(new Vector2Int(scTarget.gridX, scTarget.gridY));   // ← 同格
                        lastOffset = $"({scTarget.gridX - ctx.Player.Grid.x},{scTarget.gridY - ctx.Player.Grid.y})";
                        var hpBeforeIter = scTarget.hp;
                        scAttempts++;
                        ctx.Combat.RequestAttack(scTarget.id);
                        ctx.Combat.Tick(GameConst.PlayerAttackInterval + 0.01f);   // 只清冷却，不推进怪物
                        if (scTarget.hp < hpBeforeIter) scHit = true;              // 一掉血就停（不打死它）
                    }

                    Check("同格（偏移 (0,0)）攻击必须结算（★ melee-samecell，真实链路）", scHit,
                        $"靶子 m#{scTarget.id} {scTarget.name}（**除 Step7 靶子外**血最厚的一只，故不会打死它）："
                        + $"出手 {scAttempts} 次、出手瞬间偏移 {lastOffset}；hp {hpBeforeSc} → {scTarget.hp}"
                        + $"（alive={scTarget.alive}）；玩家朝向={ctx.Player.Dir}（零偏移下命中不依赖朝向）");

                    ctx.Player.TeleportTo(restoreGrid);      // 原样放回 ⇒ 击杀循环的条件一字未变
                    if (ctx.Player.Grid != restoreGrid)
                    {
                        Console.WriteLine($"  [GAP] melee-samecell 用例未能把玩家放回 {restoreGrid}"
                                          + $"（现为 {ctx.Player.Grid}）⇒ 后续判据的条件已被改变，见上行 Player 日志");
                    }
                }
            }

            var alive0 = ctx.Monster.AliveCount;
            for (var i = 0; i < 60 && mon.alive; i++)
            {
                // ★ 判据资产修复（同 920 行那条，team-lead 批准）：每轮**先把玩家重新摆到能打到怪的合法站位**
                //   再出手 —— 怪会追人 / 脱战回原位而漂移，而 `RequestAttack` 自己不移动玩家
                //   （实测：怪漂到 2.00 → 4.12 格 > 近战范围 1.60 ⇒ 60 轮里一次都没结算，
                //    于是"连续普攻把怪打死"这条**测的其实是怪会不会站着不动**）。
                //   ⛔ 判据条件（连续普攻必须打死它）一字未动，只修"怎么连续出手"。
                ctx.Player.TeleportTo(FindMeleePosture(ctx, mon, mfx, mfy,
                    new Vector2Int(mon.gridX - dv.x, mon.gridY - dv.y)));
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
                // ★ 片 assert-audit：原为硬编码 `true` + 文案自认「跳过断言」⇒ 那等于这条**永远绿**
                //   （而它恰恰是本图"学习转发链"的唯一覆盖点）。改判**路由器真的收到了这件事**：
                //   转发层 `AppEventRouting.OnSkillLearnRequest` 只有在**真的调过** `ISkillModule.Learn`
                //   之后才可能打出这句 Warn（`Learn` 返回 false 的唯一分支）⇒ 有这行 = 转发链被走过。
                //   ⛔ 不是放宽：原先它连寄存器都不看，现在它是可失败的。
                const int bogusSkillId = 999999;                 // 不属于任何职业 ⇒ Learn 必 false
                Game.Event.Emit(Events.SkillLearnRequest, bogusSkillId);
                var learnFwdLog = $"学习技能 {bogusSkillId} 失败";
                Check("`Events.SkillLearnRequest` 转发路径存在（本次无可学技能 ⇒ 用非法 id 逼出转发层的拒绝日志）",
                    _log.Contains(learnFwdLog),
                    $"技能点={sp}；期望日志「[App] {learnFwdLog}（等级/前置/技能点不满足…）」");
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
                // ★ 片 assert-audit：原为硬编码 `true` + 文案自认「跳过断言」⇒ 永远绿。
                //   本图无已学技能 ⇒ 判不了"选技能真的生效"，但仍然**能判转发链这一层**：
                //   ① 总线上必须有 `SkillSelected` 的监听者（= `AppEventRouting.Install` 真的订阅了；
                //      没订阅 ⇒ 0 ⇒ 这条变红）；② 转发层不得把同一个事件再发一次（防回灌）；
                //   ③ 非法 id 必须不改状态。
                var selBefore = _bus.CountOf(Events.SkillSelected);
                Game.Event.Emit(Events.SkillSelected, 999999);       // 非法 id ⇒ ValidateSelectable 拒
                Check("`Events.SkillSelected` 转发链已接线 + 防回灌机制存在（本次无已学技能 ⇒ 判接线层）",
                    _bus.HandlerCount(Events.SkillSelected) >= 1
                    && _bus.CountOf(Events.SkillSelected) == selBefore + 1
                    && ctx.Skill.SelectedSkillId == selectedBefore,
                    $"监听器={_bus.HandlerCount(Events.SkillSelected)} 事件数 {selBefore} → {_bus.CountOf(Events.SkillSelected)}"
                    + $"（+1 = 未回灌） SelectedSkillId 保持 {ctx.Skill.SelectedSkillId}");
            }

            var gold = ctx.Item.Gold;
            Game.Event.Emit(Events.ShopOpenRequest, (int)NpcId.None);
            Check("`Events.ShopOpenRequest` 转发：NPC 不存在时给可定位 Warn（不静默）",
                _log.Contains(Events.ShopOpenRequest) && ctx.Item.Gold == gold, "见上方 [App] D2.Npc.ShopOpenRequest 收到 …");

            var invBackup = ctx.Item.Snapshot();
            Game.Event.Emit(Events.UnequipRequest, ((int)ItemSlot.Weapon) & 0xFF);
            // ⚠️ 2026-09-23 主 agent 落（片 `impl-gate-close` §⑥-2 定位 + 给出同一行修法）：
            //   断言口径从「**空槽** ⇒ 只 Warn」改成「**转发链真的被调用**」—— 判**过程**不判结果（SKILL §4.10）。
            //   为什么必须改（实测根因，不是"为了变绿"）：片 `impl-startequip` 按官方 `charstats.txt`
            //   给**新角色加了起始装备**（item1=jav/item2=buc …）⇒ 本宿主里 Weapon 槽**非空**
            //   ⇒ 这一次 `UnequipRequest` **真的卸下了**武器（同一份输出里紧邻的行是
            //   `[Item] [StartItems] 起始装备已应用 … 装备槽 = 【Weapon:标枪×1, Shield:圆盾×1】`
            //   与 `[Item] 卸下「标枪」（Weapon[0] → 背包格 6）`），**不再走**"空槽只 Warn"那条分支。
            //   旧断言因此是**前提过期**（把好链路判成 FAIL），不是链路坏了。
            //   两种合法结果都接受：槽非空 ⇒ 真卸下（`卸下「`）；槽为空 ⇒ 只 Warn 不崩（`卸下装备失败`）。
            Check("`Events.UnequipRequest` 转发到 `IItemModule.Unequip`（槽非空 ⇒ 真卸下；空槽 ⇒ 只 Warn 不崩）",
                _log.Contains("卸下装备失败") || _log.Contains("卸下「"), $"背包快照格数={invBackup.inventory.Count}");

            // ★ 片 assert-audit：原文案「无契约能力 ⇒ 明确 Warn」**已过期**（片 G1 起
            //   `AppEventRouting.OnMoveInInventoryRequest` 真的转发到 `IItemModule.MoveItem`）；
            //   原判据是硬编码 `true` ⇒ 无论转发链在不在都绿。改判**转发真的发生**：
            //   转发层的两条出口日志都带事件名 ⇒ 事件名出现次数必须 +1（只发事件、不转发 ⇒ 不增长）。
            var mvHits = _log.Count(Events.MoveInInventoryRequest);
            Game.Event.Emit(Events.MoveInInventoryRequest, 0);          // 空锚点 ⇒ 必被拒（不许假装成功）
            Check("`Events.MoveInInventoryRequest` 转发到 `IItemModule.MoveItem`（结果写日志，⛔ 不假装成功）",
                _log.Count(Events.MoveInInventoryRequest) == mvHits + 1
                && (_log.Contains("移动/交换已完成") || _log.Contains("被拒绝")),
                $"事件名日志行数 {mvHits} → {_log.Count(Events.MoveInInventoryRequest)}（转发层出口 1 行）");
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

        /// <summary>
        /// 除 <paramref name="excludeId"/> 外**血量上限最厚**的一只存活怪（同血量取最小 id ⇒ 确定性）。
        /// <para>用途：melee-samecell 的同格用例要在"不打死 Step7 靶子"的前提下打出一次伤害。</para>
        /// </summary>
        private static MonsterState TankiestOther(AppContext ctx, int excludeId)
        {
            var all = ctx.Monster.All;
            MonsterState best = null;
            for (var i = 0; i < all.Count; i++)
            {
                var s = all[i];
                if (s == null || !s.alive || s.id == excludeId) continue;
                if (best == null || s.maxHp > best.maxHp || (s.maxHp == best.maxHp && s.id < best.id)) best = s;
            }
            return best;
        }

        /// <summary>
        /// ★ 判据资产修复（2026-09-24，team-lead 批准）：在怪周围找一个**可走**且**能通过生产形状闸门**
        /// 的近战站位（`MeleeShape` = 锥 ∧ 走廊 ∧ 线段通畅，与产品同一把尺子）。
        /// <para>
        /// 起因：原实现写死落点 `mon - dv`，实测它能落到**不可走**的格 ⇒ `PlayerModule` 降级到出生点
        /// ⇒ 玩家离靶 28.02 格 ⇒ 4 条战斗判据连锁变红（失败发生在 `dist > MeleeRange` 分支，根本走不到
        /// `InFrontCone`）。**判据条件本身未动**，只修"怎么把玩家摆到合法姿态"。
        /// </para>
        /// <para>
        /// 候选 = 8 邻域里"玩家站上去后怪落在玩家正面扇形内"的格：偏移 = `+delta`，
        /// 按与朝向轴的夹角排序（0° → ±45°；±45° 也过闸门：cos=0.707 ≥ 0.5、沿轴 1.00 ≤ reach、垂距 1.00 ≤ 半宽），
        /// 同角按 dx,dy 升序（确定性）。全部不合格 ⇒ 退回 <paramref name="fallback"/> 并**打一行披露**
        /// （⛔ 不把"找不到站位"伪装成"打不到"）。
        /// </para>
        /// </summary>
        private static Vector2Int FindMeleePosture(AppContext ctx, MonsterState mon, float fx, float fy,
                                                   Vector2Int fallback)
        {
            var monGrid = new Vector2Int(mon.gridX, mon.gridY);
            var best = fallback;
            var bestDot = -2f;
            var found = false;
            var considered = 0;

            for (var dx = -1; dx <= 1; dx++)
            {
                for (var dy = -1; dy <= 1; dy++)
                {
                    if (dx == 0 && dy == 0) continue;
                    var cell = new Vector2Int(mon.gridX - dx, mon.gridY - dy);
                    if (!ctx.Map.Walkable(cell)) continue;
                    considered++;

                    var len = Mathf.Sqrt((float)(dx * dx + dy * dy));
                    var dot = (fx * dx + fy * dy) / len;
                    if (dot < MeleeShape.FrontConeCos) continue;                                   // 正面扇形
                    if (!MeleeShape.InMeleeRect(fx, fy, dx, dy, GameConst.MeleeRange, MeleeShape.MeleeHalfWidth))
                        continue;                                                                  // 矩形走廊
                    if (!MeleeShape.LineClear(ctx.Map.Walkable, cell, monGrid)) continue;           // 线段通畅
                    if (found && dot <= bestDot + 1e-4f) continue;                                  // 同角保留先到的（确定性）

                    best = cell;
                    bestDot = dot;
                    found = true;
                }
            }

            if (!found)
            {
                Console.WriteLine($"  [GAP] FindMeleePosture: 怪格=({mon.gridX},{mon.gridY}) 的 8 邻域里"
                                  + $"可走格 {considered} 个，但**没有一个**能过形状闸门 ⇒ 退回写死落点 {fallback}"
                                  + "（这条会由下面的判据自己变红，不是静默通过）");
            }
            return best;
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
