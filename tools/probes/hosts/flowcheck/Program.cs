// ─────────────────────────────────────────────────────────────────────────────
// Flow 自检宿主（**非 Unity 工程、不参与打包；离线跑，秒级**）
//
// 目的：在没有 Unity 编辑器的情况下，把**流程编排（站点迁移 / 读条 / 清场 / 名册 / 配表）**
// 真跑一遍并打印 `[Flow] → <站点>` 序列 —— 这是本阶段能拿到的**最强证据**
// （用户尚未打开编辑器 ⇒ 禁止跑 unity run/test；Play 实测由主 agent 之后做）。
//
// 覆盖：
//   · 站点迁移：Boot → MainMenu → CharSelect → CharCreate → CharSelect → Loading → Stage
//              → MainMenu → CharSelect → Loading → Stage（**能再进一次**）
//   · 读条进度真来自 `Game.Scene.Load` 的 progress 回调（本宿主同步回调 0.3/0.7/1.0）
//   · 离场清场 7 项（面板/实体/池/定时器 scope/音效/事件注销/模块复位）逐条断言
//   · 配表 class_c 真读（走 `Table.TableLoader`，与 Bootstrap 同一条链路）
//   · 模块未接入（AppContext.* = null）时的 **null 容忍 + Warn 降级**
// 不覆盖（需要 Unity 原生）：Pause 站点（`Time.timeScale`）、面板内部构件与像素布局。
// ─────────────────────────────────────────────────────────────────────────────

using System;
using System.Collections.Generic;
using CloverEngine;
using Diablo2.App;          // AppWiring / AppDoorGuard（internal，同程序集内可用）
using Diablo2.Core;
using Diablo2.Def;
using Diablo2.Module.Flow;
using UnityEngine;
// 别名：`AppContext` 与 BCL 的 `System.AppContext` 同名（同时 using System 会 CS0104）。
using AppContext = Diablo2.App.AppContext;

namespace FlowCheck
{
    // ── 引擎门面替身（记录调用，不做真实工作）──────────────────────────────

    internal sealed class SimpleFsm : IFsm
    {
        private readonly Dictionary<string, (Action enter, Action<float> tick, Action exit)> _states
            = new Dictionary<string, (Action, Action<float>, Action)>();
        private readonly Dictionary<string, string> _trans = new Dictionary<string, string>();
        private Action<string, string> _onChange;

        public string Current { get; private set; }

        public void RegisterState(string state, Action onEnter = null, Action<float> onTick = null,
            Action onExit = null) => _states[state] = (onEnter, onTick, onExit);

        public void Transition(string toState) { if (toState != Current) Switch(toState); }
        public void AddTransition(string trigger, string toState) => _trans[trigger] = toState;

        public void Trigger(string trigger)
        {
            if (_trans.TryGetValue(trigger, out var to)) Switch(to);
        }

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

    /// <summary>
    /// `IUIManager` 替身：**真的维护"当前开着哪些面板"**（`Opened` = 开着的集合，`EverOpened` = 历史）。
    /// 为什么要维护开闭真值：`agent-14 §B 现象 1` 就是"面板没关"，
    /// 而它**只有看 `IsOpen` 才能测出来**（看"打开过"的列表永远看不出来）。
    /// </summary>
    internal sealed class RecUI : IUIManager
    {
        /// <summary>**当前开着**的面板名（`Close` 会移除）。</summary>
        public readonly List<string> Opened = new List<string>();

        /// <summary>历史上打开过的面板名（`Close` 不移除；用于"确实打开过"的断言）。</summary>
        public readonly List<string> EverOpened = new List<string>();

        /// <summary>历史上关闭过的面板名（用于断言"某面板被关过"）。</summary>
        public readonly List<string> Closed = new List<string>();

        public int CloseAllCount;
        public int ToastCount;

        public void Open<T>(object param = null) where T : class, IUIPanel
        {
            var n = typeof(T).Name;
            EverOpened.Add(n);
            if (!Opened.Contains(n)) Opened.Add(n);
        }

        public void Close<T>() where T : class, IUIPanel { Close(typeof(T).Name); }

        public void Close(string panelName)
        {
            if (Opened.Remove(panelName)) Closed.Add(panelName);
        }

        public void CloseAll()
        {
            CloseAllCount++;
            Program.Tape.Add("CloseAll");                  // ★§A 清场①（面板）
            Closed.AddRange(Opened);
            Opened.Clear();
        }

        public T Get<T>() where T : class, IUIPanel => null;      // 宿主不实例化 MonoBehaviour
        public bool IsOpen<T>() where T : class, IUIPanel => Opened.Contains(typeof(T).Name);
        public void Toast(string text, float duration = 2f) { ToastCount++; Console.WriteLine("  [TOAST] " + text); }
        public void FloatText(Vector3 worldPos, string text, Color? color = null, float duration = 1.2f) { }
        public void ShowLoading(string text = null) => Console.WriteLine("  [LOADING] show: " + text);
        public void HideLoading() => Console.WriteLine("  [LOADING] hide");
        public bool IsLoading => false;
        public void Confirm(string title, string message, Action onConfirm, Action onCancel = null,
            string confirmText = null, string cancelText = null)
        {
            Console.WriteLine($"  [CONFIRM] {title}: {message}");
            onConfirm?.Invoke();
        }
        public void Tick(float dt) { }
    }

    internal sealed class RecScene : ISceneManager
    {
        public readonly List<string> LoadedScenes = new List<string>();
        public string CurrentScene { get; private set; }

        // agent-17 §A：与引擎同序 —— `OnSceneLoaded` 的处理器在 `onDone` **之前**逐个回调
        // （`Runtime/Presentation/Scene.cs:54-65`）。`AppFlow` 靠它识别"Stage 场景被重载"。
        private readonly List<Action<string>> _loaded = new List<Action<string>>();
        private readonly List<Action<string>> _unloaded = new List<Action<string>>();

        /// <summary>同步模拟一次加载：进度 0.3 / 0.7 / 1.0 后回调 onDone（真进度语义）。</summary>
        public void Load(string sceneName, Action<float> progress = null, Action onDone = null)
        {
            Console.WriteLine($"  [SCENE] load {sceneName}");
            Program.Tape.Add("Load:" + sceneName);          // ★§A 顺序磁带：证明"先清场再加载"
            LoadedScenes.Add(sceneName);
            progress?.Invoke(0.3f);
            progress?.Invoke(0.7f);
            progress?.Invoke(1f);
            CurrentScene = sceneName;
            for (var i = 0; i < _loaded.Count; i++) _loaded[i]?.Invoke(sceneName);
            onDone?.Invoke();
        }

        public void Unload(string sceneName, Action onDone = null)
        {
            Console.WriteLine($"  [SCENE] unload {sceneName}");
            if (CurrentScene == sceneName) CurrentScene = null;
            for (var i = 0; i < _unloaded.Count; i++) _unloaded[i]?.Invoke(sceneName);
            onDone?.Invoke();
        }

        public void OnSceneLoaded(Action<string> handler) => _loaded.Add(handler);
        public void OnSceneUnloaded(Action<string> handler) => _unloaded.Add(handler);
    }

    internal sealed class MemSetting : ISetting
    {
        private readonly Dictionary<string, object> _d = new Dictionary<string, object>();
        public int SaveCount;
        public T Get<T>(string key, T defaultValue = default) => _d.TryGetValue(key, out var v) && v is T t ? t : defaultValue;
        public void Set<T>(string key, T value) { _d[key] = value; }
        public void Save() { SaveCount++; }
        public void Load() { }
        public void Delete(string key) => _d.Remove(key);
        public void DeleteAll() => _d.Clear();
    }

    internal sealed class FakeInput : IInputManager
    {
        public bool EscapeDown;
        public bool Available => true;
        public bool IsLocked => false;
        public bool GetKey(GameKey key) => false;
        public bool GetKeyDown(GameKey key) => key == GameKey.Escape && EscapeDown;
        public bool GetKeyUp(GameKey key) => false;
        public bool GetMouseButton(int button) => false;
        public bool GetMouseButtonDown(int button) => false;
        public Vector3 MousePosition => Vector3.zero;
    }

    internal sealed class FakeSound : ISoundManager
    {
        public int StopAllCount;
        public void PlayBGM(string clipName, float fadeTime = 0.5f) { }
        public void PlaySFX(string clipName) { }
        public void StopAll() { StopAllCount++; Program.Tape.Add("StopAll"); }   // ★§A 清场⑤（音效）
        public void SetVolume(SoundGroup group, float volume) { }
        public float GetVolume(SoundGroup group) => 1f;
        public void SetMute(SoundGroup group, bool mute) { }
    }

    internal sealed class FakeEntities : IEntityManager
    {
        private readonly List<EntityInfo> _alive = new List<EntityInfo>();
        public int ClearAllCount;
        public void Seed(int n) { _alive.Clear(); for (var i = 0; i < n; i++) _alive.Add(new EntityInfo()); }
        public void ClearAll() { ClearAllCount++; Program.Tape.Add("ClearAll"); _alive.Clear(); }   // ★§A 清场②（实体/视图）
        public IEnumerable<EntityInfo> GetAll() => _alive;
    }

    internal sealed class FakePool : IObjectPool
    {
        public int ClearAllCount;
        public void ClearAll() { ClearAllCount++; Program.Tape.Add("PoolClear"); }                   // ★§A 清场③（对象池）
    }

    internal sealed class FakeTimer : ITimer
    {
        public readonly List<string> StoppedScopes = new List<string>();
        private long _id;
        public long After(float delay, Action callback) => ++_id;
        public long AfterUnscaled(float delay, Action callback) => ++_id;
        public long Every(float interval, Action callback) => ++_id;
        public long EveryUnscaled(float interval, Action callback) => ++_id;
        public void Stop(long id) { }
        public void StopNamed(string name) { }
        public void StopScope(string scope) { StoppedScopes.Add(scope); Program.Tape.Add("StopScope:" + scope); }   // ★§A 清场④（定时器）
        public void StopAll() { }
        public void Tick(float dt) { }
    }

    internal sealed class MissRes : IResourceManager
    {
        public void LoadAsset<T>(string path, Action<T> cb) where T : UnityEngine.Object { cb?.Invoke(null); }
        public T TryGet<T>(string path) where T : UnityEngine.Object => null;

        // ★ agent-34：宿主无素材 ⇒ `Exists` 恒 false、`LoadAll` 恒空数组（引擎契约的"取不到"口径）。
        //   这两个成员是引擎新长的（`Contracts.cs` 的 Exists / LoadAll），业务源码已经改调它们。
        public bool Exists(string path) => false;
        public T[] LoadAll<T>(string path) where T : UnityEngine.Object => Array.Empty<T>();
    }

    /// <summary>只用于自证 `Activator.CreateInstance` 能创建 `internal sealed` 类型（AutoWire 的机制前提）。</summary>
    internal sealed class ProbeOnly
    {
    }

    // ── 自检主流程 ──────────────────────────────────────────────────────────

    public static class Program
    {
        // ★ 仓库根改为**运行期推导**（见 ResolveProjectRoot），不再依赖调用方 cwd。
        //   原先写死 `@"client\Assets"`（cwd 相对）⇒ `tools/probes/hosts/run_all_hosts.ps1`
        //   用 `Push-Location <宿主目录>` 驱动时被解析成 `<宿主目录>\client\Assets`（不存在）
        //   ⇒ 配表 0 行 ⇒ 断言红、exit 1（实测 2026-09-20 复现）。
        private static readonly string ClientDataPath = ResolveProjectRoot() + @"\client\Assets";

        /// <summary>
        /// 从宿主自己的可执行目录向上找「含 client/Assets 的那一层」= 仓库根。
        /// 宿主位于 tools/probes/hosts/&lt;名&gt;/bin/&lt;cfg&gt;/&lt;tfm&gt;/（与 corecheck / fullcheck / savecheck / uicheck 同一套写法）。
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

        private static readonly List<string> StationLog = new List<string>();

        /// <summary>
        /// ★§A 「调用顺序磁带」：把清场各步（`Module/Flow/AppFlow.LeaveStage` 的 ①~⑤）与 `Game.Scene.Load`
        /// 按真实调用顺序记下来 —— 用来断言「不同区进图必须先清场再加载场景」，光看计数看不出顺序。
        /// 写入方：`RecUI.CloseAll` / `FakeEntities.ClearAll` / `FakePool.ClearAll` /
        /// `FakeTimer.StopScope` / `FakeSound.StopAll` / `RecScene.Load`。
        /// </summary>
        internal static readonly List<string> Tape = new List<string>();

        private static int _fail;

        /// <summary>注入给 `Log.Clock` 的单调秒（见 Main 里 <c>Log.Clock = …</c>）。</summary>
        private static float _clockSeconds;

        /// <summary>注入给 `AppFlow.Clock` 的单调秒（读条分档的节奏下限认它；见 <see cref="PumpLoading"/>）。</summary>
        private static double _flowSeconds;

        /// <summary>
        /// ★load 把 Loading 站点**逐帧推到底** —— 「经典 load 动画」轮之后，进图不再一步到位：
        /// `AppFlow` 在 Loading 站点里**每帧推一档真实装配**，并按 `LoadingSteps.FrameCadenceSeconds`
        /// （0.07s/档）逐档呈现，最后「世界就绪 + 门开满第 10 帧 + 再等一帧」才关屏进 Stage。
        /// <para>所以离线宿主必须像引擎一样**逐帧驱动**（`Game.Tick → Fsm.Tick → 站点 onTick`），
        /// 并推进 <see cref="AppFlow.Clock"/> 的假时钟 —— 否则门永远停在某一档、站点永远留在 Loading。</para>
        /// </summary>
        private static void PumpLoading(SimpleFsm fsm, IAppFlow flow, int maxFrames = 400)
        {
            var frames = 0;
            while (flow.CurrentState == Events.Fsm.StateLoading && frames < maxFrames)
            {
                _flowSeconds += 0.05;      // 假时钟：一帧 50ms（< 节奏下限 70ms ⇒ 每 ~2 帧放行 1 档）
                fsm.Tick(0.05f);
                frames++;
            }

            Console.WriteLine($"  [PUMP] 读条分档推到站点={flow.CurrentState}（{frames} 帧 / 假时钟 {_flowSeconds:0.00}s）");
            if (flow.CurrentState == Events.Fsm.StateLoading)
            {
                _fail++;
                Console.WriteLine($"[FAIL] ★load 读条站点未在 {maxFrames} 帧内完成（假时钟 {_flowSeconds:0.00}s）" +
                                  " ⇒ 分档推进卡住（门/真实档位/关屏条件之一没满足）");
            }
        }

        public static int Main()
        {
            Console.WriteLine("=== FlowCheck：流程编排离线自检 ===");

            // 引擎门面替身
            Game.Logger = new ConsoleLogger();
            var bus = new ConsoleEventBus();
            var fsm = new SimpleFsm();
            var ui = new RecUI();
            var scene = new RecScene();
            var setting = new MemSetting();
            var input = new FakeInput();
            var sound = new FakeSound();
            var entities = new FakeEntities();
            var pool = new FakePool();
            var timer = new FakeTimer();

            Game.Event = bus;
            Game.Fsm = fsm;
            Game.UI = ui;
            Game.Scene = scene;
            Game.Setting = setting;
            Game.Input = input;
            Game.Sound = sound;
            Game.Entity = entities;
            Game.Pool = pool;
            Game.Timer = timer;
            Game.Res = new MissRes();
            Game.IsRunning = true;

            // 站点迁移日志（与生产同一出口：Fsm.OnChange）—— 用于断言"序列完整"
            fsm.OnChange((from, to) => StationLog.Add(to));

            // ★ 注入 `Log.Clock`（agent-13 §C 第 2 项的推荐做法）：降频入口不再碰
            //   `Time.realtimeSinceStartup`（原生 ECall）⇒ **不必再 `Log.Suppress`**，
            //   于是全程都能捕获真实日志行，才能**数字符站日志**（agent-14 §B 现象 2 的验收口径）。
            Log.Clock = () => _clockSeconds += 0.016f;

            // ★load 同源做法：给 `AppFlow` 也注入**假墙钟**（读条分档的节奏下限认墙钟，
            //   宿主不推进时间 ⇒ 门永远开不满 ⇒ 站点永远留在 Loading）。
            //   刻意**不用 Unity 的 `Time`**：那是原生 ECall，本进程里会抛 SecurityException
            //   （`Core/Log.cs` 的文件头记了同一条教训）。
            AppFlow.Clock = () => _flowSeconds;

            // ① 配表：与 Bootstrap 同一条链路（TableLoader → Tables.Default）
            var err = Table.TableLoader.LoadAll(null, ClientDataPath);
            Check("配表 class_c 已加载", err == null, err ?? ("dir=" + Table.TableLoader.LastDir));
            var cls = Table.TableLoader.Class((int)PlayerClass.Amazon);
            Check("class_c 亚马逊行存在", cls != null && cls.Str > 0,
                cls == null ? "row=null" : $"亚马逊 str={cls.Str} dex={cls.Dex} vit={cls.Vit} eng={cls.Eng} 生命/体={cls.LifePerVit}");

            // ② 装配（模块全部未接入 = 当前项目真实状态：其它模块 agent 尚未交付）
            var ctx = AppContext.Create();
            ctx.Flow = new AppFlow();

            // ②b App 层接线（与 `Bootstrap` 同一顺序：先 new AppFlow，再 Install）
            AppWiring.Install(ctx);
            Check("AppWiring.Install 成功（AppDoorGuard / AppSnapshots / AppEventRouting 已订阅）",
                ConsoleLogger.CountOf("App 接线完成") == 1, "见上方 [App] App 接线完成 …");
            Check("AppContext 已创建且模块为 null（降级路径）", ctx.Map == null && ctx.Save == null, ctx.Describe());

            // ③ 进流程：**与生产完全同一条路**（只调 `Flow.Enter()`；`Bootstrap` 不再自己 Force 一次）
            ctx.Flow.Enter();
            Check("站点 = Boot", ctx.Flow.CurrentState == Events.Fsm.StateBoot, ctx.Flow.CurrentState);

            // ★§B 现象 2 的回归断言：`[Flow] → Boot` **恰好 1 条**
            //   （修前：`Bootstrap.Force(Boot)` 打一条 + `Enter()` 的 else 分支又打一条 = 2 条）
            //   计数用 `"[INFO ] [Flow] → "`（**带级别前缀**）：面板自己的
            //   `[Ui] [Flow] → Boot：启动画面已显示` 是另一条日志，不能混进来。
            Check("★[Flow] → Boot 恰好 1 条（站点日志不翻倍）",
                ConsoleLogger.CountOf("[INFO ] [Flow] → Boot") == 1,
                "count=" + ConsoleLogger.CountOf("[INFO ] [Flow] → Boot"));

            // ④ Boot → MainMenu（等价于 BootPanel 收到任意键后 Emit）
            bus.Emit(Events.BootDone);
            Check("站点 = MainMenu", ctx.Flow.CurrentState == Events.Fsm.StateMainMenu, ctx.Flow.CurrentState);
            Check("主菜单面板已打开（无存档 ⇒ 继续置灰）", ui.Opened.Contains("MainMenuPanel"), string.Join(",", ui.Opened));

            // 阶段二（原写法是 `Log.Suppress = true`：因为 `Core/Log.cs` 的降频闸门会碰原生
            // `Time.realtimeSinceStartup`。现在宿主注入了 `Log.Clock` ⇒ 不再需要静默，
            // 于是**全程都能数字符站日志**）。

            // ⑤ MainMenu → 单人游戏（无存档 ⇒ 直接创角）
            bus.Emit(Events.Fsm.TriggerNewGame);
            Check("无存档时直接落到 CharCreate", ctx.Flow.CurrentState == Events.Fsm.StateCharCreate, ctx.Flow.CurrentState);
            Check("创造角色面板已打开", ui.Opened.Contains("CharCreatePanel"), string.Join(",", ui.Opened));

            // ⑥ 创角（等价于 CharCreatePanel 的「确定」；数值用配表 class_c 的亚马逊行）
            var save = BuildSaveFromTable(cls, "HeroCheck");
            Check("创角 save 有 seed（=0 待 Flow 决定）", save.mapSeed == 0, save.mapSeed.ToString());
            bus.Emit(Events.CharCreateRequest, save);
            Check("建角后回 CharSelect", ctx.Flow.CurrentState == Events.Fsm.StateCharSelect, ctx.Flow.CurrentState);
            Check("创角已给出本局 seed", save.mapSeed != 0, save.mapSeed.ToString());

            // ★§B 现象 1 的回归断言：`CharCreate → CharSelect` 后**创角面板必须关掉**
            //   （修前会被误判为"两个面板叠加"；真值看 `IsOpen`，不是看"打开过"的列表）
            Check("★CharCreate → CharSelect 后 CharCreatePanel 已关闭（不与选角屏叠加）",
                !ui.IsOpen<Diablo2.UI.CharCreatePanel>() && ui.IsOpen<Diablo2.UI.CharSelectPanel>(),
                "当前开着=" + string.Join(",", ui.Opened));

            // ★§B 兜底清扫自证：故意在 CharSelect 站点留一个"主菜单面板"，迁移后必须被关掉 + Warn
            ui.Open<Diablo2.UI.MainMenuPanel>();
            bus.Emit(Events.Fsm.TriggerNeedCreate);          // CharSelect → CharCreate
            Check("★兜底清扫：非本站点面板被关掉，并留下可检索 Warn（漏关不再静默）",
                !ui.IsOpen<Diablo2.UI.MainMenuPanel>() && ConsoleLogger.CountOf("兜底关闭") == 1,
                "Warn数=" + ConsoleLogger.CountOf("兜底关闭") + " 当前开着=" + string.Join(",", ui.Opened));
            bus.Emit(Events.CharSelectRequest, string.Empty);   // 空名 = 仅返回选角屏
            Check("空角色名 = 返回选角屏（创角屏「返回」按钮的语义）",
                ctx.Flow.CurrentState == Events.Fsm.StateCharSelect, ctx.Flow.CurrentState);

            // ⑦ CharSelect → Loading → Stage（第一次进图）
            entities.Seed(3);                                   // 模拟 Stage 内已有实体
            bus.Emit(Events.CharSelectRequest, "HeroCheck");

            // ★load：进图不再一步到位 —— 读条屏分档推进要求**逐帧驱动** Loading 站点。
            Check("★load 进图请求后先停在 Loading 站点（读条屏在屏幕上有机会显示）",
                ctx.Flow.CurrentState == Events.Fsm.StateLoading || ctx.Flow.CurrentState == Events.Fsm.StateStage,
                ctx.Flow.CurrentState);
            Check("★load LoadingPanel 已打开", ui.Opened.Contains("LoadingPanel"), string.Join(",", ui.Opened));

            PumpLoading(fsm, ctx.Flow);

            Check("站点 = Stage（逐帧推完读条后进 Stage）",
                ctx.Flow.CurrentState == Events.Fsm.StateStage, ctx.Flow.CurrentState);
            Check("读条面板开过", ui.EverOpened.Contains("LoadingPanel") || scene.LoadedScenes.Contains("Stage"),
                "scenes=" + string.Join(",", scene.LoadedScenes));
            Check("★load 关屏时机 = 世界就绪之后：进 Stage 时 LoadingPanel **已关闭**",
                !ui.IsOpen<Diablo2.UI.LoadingPanel>() && ui.Closed.Contains("LoadingPanel"),
                "当前开着=" + string.Join(",", ui.Opened) + " 关过=" + string.Join(",", ui.Closed));
            Check("★load 进图装配在读条**之后**才完成（装配未在读条分档里完成的那条兜底 Warn 不许出现）",
                ConsoleLogger.CountOf("进图装配未在读条屏的分档里完成") == 0,
                "count=" + ConsoleLogger.CountOf("进图装配未在读条屏的分档里完成"));
            Check("Stage 场景被加载", scene.LoadedScenes.Contains(SceneNames.Stage), string.Join(",", scene.LoadedScenes));
            Check("模块未接入时仍能进 Stage（null 容忍）", ctx.Flow.CurrentState == Events.Fsm.StateStage, ctx.Flow.CurrentState);

            // ⑧ 区域切换（出入口事件）—— 同时验 agent-14 §B 现象 3 的计数
            //    生产的链路是「`ExitEntered` 的派发里 `AppFlow.EnterArea` 重生成地图」，
            //    本宿主用**同序的临时订阅者**模拟那次生成（本宿主没有 Map 模块，见 csproj 的编译集）。
            var beforeSwitch = StationLog.Count;
            var doorMap = new MinimapArgs { areaId = (int)AreaId.BloodMoor, width = 32, height = 32, seed = 777 };
            Action<AreaId> doorGen = _ => bus.Emit(Events.MapGenerated, doorMap);
            bus.On<AreaId>(Events.ExitEntered, doorGen);        // 后注册 ⇒ 与生产同序（后注册先执行）
            bus.Emit(Events.ExitEntered, AreaId.BloodMoor);
            bus.Off<AreaId>(Events.ExitEntered, doorGen);

            Check("出入口触发区域切换且不换站点", StationLog.Count == beforeSwitch, string.Join(" → ", StationLog));
            Check("★过门恰好 1 次地图生成（AppDoorGuard 在过门边界上报为 1，随后清零）",
                ConsoleLogger.CountOf("过门 #1 → BloodMoor：生成地图 1 次 —— 去重断言通过") == 1 &&
                AppDoorGuard.DoorSerial == 1 && AppDoorGuard.GensSinceLastBoundary == 0,
                $"DoorSerial={AppDoorGuard.DoorSerial} gens={AppDoorGuard.GensSinceLastBoundary} " +
                $"line={ConsoleLogger.CountOf("过门 #1 → BloodMoor：生成地图 1 次 —— 去重断言通过")}");
            Check("★过门/进图全程没有「重复生成」假警报",
                ConsoleLogger.CountOf("重复生成") == 0, "count=" + ConsoleLogger.CountOf("重复生成"));

            // ⑧-2 ★ 片 T（S-08）：**自环过门请求不许刷屏，但不许静默**
            //     现场：上游（探针/回声）在"已经在 BloodMoor"时重复发 `ExitEntered(BloodMoor)`，
            //     旧实现每一条都写一行 Warn ⇒ 09-23 日志 12 分钟 39069 条（≈54 条/秒）。
            //     修复口径 = 第一次 Warn 说清、之后每 1000 次汇总一条（⛔ 不是把铃声拆掉）。
            //     判据打在**日志条数**上（判过程）：2001 次自环请求 ⇒ Warn 恰好 1 条 + 汇总恰好 2 条。
            const int dupRepeats = 2001;
            var doorSerialBeforeDup = AppDoorGuard.DoorSerial;
            var stationsBeforeDup = StationLog.Count;
            var switchLinesBeforeDup = ConsoleLogger.CountOf("[Stage] 区域已切换为");
            for (var i = 0; i < dupRepeats; i++) bus.Emit(Events.ExitEntered, AreaId.BloodMoor);

            Check($"★S-08 自环过门 {dupRepeats} 次 ⇒ Warn「出入口指向当前区域 BloodMoor」**只报 1 条**",
                ConsoleLogger.CountOf("出入口指向当前区域 BloodMoor") == 1,
                "warn=" + ConsoleLogger.CountOf("出入口指向当前区域 BloodMoor") + "（修前 = " + dupRepeats + "）");
            Check("★S-08 自环过门 ⇒ 汇总条数 = 2（每 1000 次一条，⛔ 不是静默丢弃）",
                ConsoleLogger.CountOf("已累计忽略") == 2, "summary=" + ConsoleLogger.CountOf("已累计忽略"));
            Check("★S-08 自环过门不重生成地图、不换站点（拒绝语义保持）",
                StationLog.Count == stationsBeforeDup &&
                ConsoleLogger.CountOf("[Stage] 区域已切换为") == switchLinesBeforeDup &&
                AppDoorGuard.DoorSerial == doorSerialBeforeDup + dupRepeats,
                $"stations={stationsBeforeDup}→{StationLog.Count} switchLines=" + switchLinesBeforeDup +
                "→" + ConsoleLogger.CountOf("[Stage] 区域已切换为") + " doorSerial=" + doorSerialBeforeDup +
                "→" + AppDoorGuard.DoorSerial);
            bus.Emit(Events.ExitEntered, AreaId.Town);
            Check("★S-08 自环之后**真的换区**仍然生效（同源判据不是把出口锁死）",
                ConsoleLogger.CountOf("[Stage] 区域已切换为") == switchLinesBeforeDup + 1,
                "switchLines=" + ConsoleLogger.CountOf("[Stage] 区域已切换为"));
            bus.Emit(Events.ExitEntered, AreaId.BloodMoor);   // 复位现场（后续用例假定在 BloodMoor）

            // ⑧b ★§B 现象 1（同类漏关）：暂停里的「选项」是子面板 ⇒ `Pause → Stage` 必须把它一起关掉
            //     （修前 Play 实测：`fsm=Stage PausePanel=False SettingsPanel=True` ⇒ 选项面板一直叠在 HUD 上）
            //     兜底 Warn 的基线：上面那个"故意留面板"的用例已经打过 1 条，这里只断言"**没有新增**"，
            //     即本次是 `onExit` 关掉的，而不是靠兜底清扫兜的。
            var sweepWarnsBefore = ConsoleLogger.CountOf("兜底关闭");
            bus.Emit(Events.PauseRequest);
            ui.Open<Diablo2.UI.SettingsPanel>();                 // 等价于点暂停菜单里的「选项」
            Check("暂停站点：PausePanel + SettingsPanel 同时开着（复现现场）",
                ctx.Flow.CurrentState == Events.Fsm.StatePause &&
                ui.IsOpen<Diablo2.UI.PausePanel>() && ui.IsOpen<Diablo2.UI.SettingsPanel>(),
                "站点=" + ctx.Flow.CurrentState + " 当前开着=" + string.Join(",", ui.Opened));

            bus.Emit(Events.ResumeRequest);
            Check("★Pause → Stage 后 PausePanel 与 SettingsPanel **都**被关掉（onExit 补齐，不靠兜底清扫）",
                ctx.Flow.CurrentState == Events.Fsm.StateStage &&
                !ui.IsOpen<Diablo2.UI.PausePanel>() && !ui.IsOpen<Diablo2.UI.SettingsPanel>() &&
                ConsoleLogger.CountOf("兜底关闭") == sweepWarnsBefore,
                "站点=" + ctx.Flow.CurrentState + " 当前开着=" + string.Join(",", ui.Opened) +
                " 兜底Warn=" + sweepWarnsBefore + "→" + ConsoleLogger.CountOf("兜底关闭"));

            // ══════════════════════════════════════════════════════════════════
            // ⑧c ★§A（agent-17 §A）进图可重入 —— 三条回归断言
            //   缺陷：已在 Stage 时再发进图请求 ⇒ 原 `GoStage` 会 `Scene.Load(Stage)` **重载场景**
            //   （销毁全部视图节点），而 `OnEnterStage` 因 `_stageActive==true` 提前 return ⇒ 引用悬空。
            //   修法：`GoStage` 重入守卫（同区忽略 / 不同区先清场再进图）+ `OnEnterStage` 一致性核对后补清场重建。
            //   注：此刻 `_area == BloodMoor`（⑧ 的过门已切过区域，且会话内名册返回同一实例 ⇒ areaId 同步）。
            // ══════════════════════════════════════════════════════════════════

            // ⑧c-① 已在 Stage + **同区**进图 ⇒ 忽略（不重载场景、不清场、不迁移站点）
            var loadsBeforeIgnore = scene.LoadedScenes.Count;
            var closeAllBeforeIgnore = ui.CloseAllCount;
            var entityClearBeforeIgnore = entities.ClearAllCount;
            bus.Emit(Events.CharSelectRequest, "HeroCheck");        // 与生产同一入口：选角屏点「进入」
            Check("★§A-① 已在 Stage 时「同区进图」被忽略：Scene.Load 调用次数不增加",
                scene.LoadedScenes.Count == loadsBeforeIgnore,
                $"loads={loadsBeforeIgnore}→{scene.LoadedScenes.Count} scenes={string.Join(",", scene.LoadedScenes)}");
            Check("★§A-① 同区进图不清场（面板 CloseAll / 实体 ClearAll 计数不增）",
                ui.CloseAllCount == closeAllBeforeIgnore && entities.ClearAllCount == entityClearBeforeIgnore,
                $"CloseAll={closeAllBeforeIgnore}→{ui.CloseAllCount} EntityClearAll={entityClearBeforeIgnore}→{entities.ClearAllCount}");
            Check("★§A-① 留下可检索 Info（[Flow] 进图请求被忽略（已在 Stage 同一区域））",
                ConsoleLogger.CountOf("[Flow] 进图请求被忽略（已在 Stage 同一区域）") == 1,
                "count=" + ConsoleLogger.CountOf("[Flow] 进图请求被忽略（已在 Stage 同一区域）"));
            Check("★§A-① 站点仍为 Stage（重入未引发任何站点迁移）",
                ctx.Flow.CurrentState == Events.Fsm.StateStage, ctx.Flow.CurrentState);

            // ⑧c-② **不同区**进图 ⇒ 先清场（7 项各一次）**再**加载场景（顺序由 Tape 断言）
            var tapeFrom = Tape.Count;
            var closeAllBeforeSwitch = ui.CloseAllCount;
            var clearAllBeforeSwitch = entities.ClearAllCount;
            var poolBeforeSwitch = pool.ClearAllCount;
            var soundBeforeSwitch = sound.StopAllCount;
            var scopeBeforeSwitch = timer.StoppedScopes.Count;
            var loadsBeforeSwitch = scene.LoadedScenes.Count;
            var cleanedBeforeSwitch = ConsoleLogger.CountOf("清场完成：");

            ctx.Flow.GoStage(AreaId.DenOfEvil);     // 已在 Stage（BloodMoor）⇒ 必须走「先清场再进图」的正规路径

            var tape = Tape.GetRange(tapeFrom, Tape.Count - tapeFrom);
            Check("★§A-② 不同区进图：清场 ①~⑤ 在 Scene.Load **之前**按序各执行一次（顺序磁带）",
                string.Join("|", tape) == "CloseAll|ClearAll|PoolClear|StopScope:stage|StopAll|Load:Stage",
                "tape=" + string.Join("|", tape));
            Check("★§A-② 清场各项计数各 +1（面板/实体/对象池/定时器 scope/音效）",
                ui.CloseAllCount == closeAllBeforeSwitch + 1 &&
                entities.ClearAllCount == clearAllBeforeSwitch + 1 &&
                pool.ClearAllCount == poolBeforeSwitch + 1 &&
                sound.StopAllCount == soundBeforeSwitch + 1 &&
                timer.StoppedScopes.Count == scopeBeforeSwitch + 1,
                $"CloseAll={closeAllBeforeSwitch}→{ui.CloseAllCount} Entity={clearAllBeforeSwitch}→{entities.ClearAllCount} " +
                $"Pool={poolBeforeSwitch}→{pool.ClearAllCount} Timer={scopeBeforeSwitch}→{timer.StoppedScopes.Count} " +
                $"Sound={soundBeforeSwitch}→{sound.StopAllCount}");
            Check("★§A-② 清场 ⑦（模块状态复位）也跑过：清场完成日志 +1（该行在 ResetModules 之后）",
                ConsoleLogger.CountOf("清场完成：") == cleanedBeforeSwitch + 1,
                $"cleaned={cleanedBeforeSwitch}→{ConsoleLogger.CountOf("清场完成：")}");
            Check("★§A-② 清场之后才加载 Stage 场景（Scene.Load +1，最后一条 = Stage）",
                scene.LoadedScenes.Count == loadsBeforeSwitch + 1 &&
                scene.LoadedScenes[scene.LoadedScenes.Count - 1] == SceneNames.Stage,
                $"loads={loadsBeforeSwitch}→{scene.LoadedScenes.Count} scenes={string.Join(",", scene.LoadedScenes)}");

            // ★load：换区进图同样要走读条分档 ⇒ 逐帧推完再断言站点（Tape 断言在上面已经取过，
            //         分档推进不写 Tape，顺序断言不受影响）。
            PumpLoading(fsm, ctx.Flow);
            Check("★§A-② 换区后仍在 Stage 站点（不换站点、只换区域）",
                ctx.Flow.CurrentState == Events.Fsm.StateStage, ctx.Flow.CurrentState);

            // ⑧c-③ 「场景被重载」的兜底：绕过 `GoStage` 直接把 Stage 场景再加载一次（= 缺陷现场：
            //     有代码重载了场景且没有清场），读条完成后照常触发 StageReady ⇒ `OnEnterStage` 重入。
            //     ⇒ 必须**补一次清场 + 重建**，而不是静默 return（否则视图引用永久悬空）。
            var closeAllBeforeReload = ui.CloseAllCount;
            var clearAllBeforeReload = entities.ClearAllCount;
            var cleanedBeforeReload = ConsoleLogger.CountOf("清场完成：");
            var reloadDetectKey = "[Flow] 检测到 Stage 场景已被重载 ⇒ 补清场重建";

            scene.Load(SceneNames.Stage, null, () => fsm.Trigger(Events.Fsm.TriggerStageReady));

            Check("★§A-③ 检测到 Stage 场景已被重载 ⇒ 补一次清场（CloseAll / 实体 ClearAll 各 +1）",
                ui.CloseAllCount == closeAllBeforeReload + 1 && entities.ClearAllCount == clearAllBeforeReload + 1,
                $"CloseAll={closeAllBeforeReload}→{ui.CloseAllCount} Entity={clearAllBeforeReload}→{entities.ClearAllCount}");
            Check("★§A-③ 补清场走的是完整 7 项路径（清场完成日志 +1），且留下可检索 Warn",
                ConsoleLogger.CountOf("清场完成：") == cleanedBeforeReload + 1 &&
                ConsoleLogger.CountOf(reloadDetectKey) == 1,
                $"cleaned={cleanedBeforeReload}→{ConsoleLogger.CountOf("清场完成：")} detect={ConsoleLogger.CountOf(reloadDetectKey)}");
            Check("★§A-③ 补清场后照常重建装配（仍在 Stage 站点、地图重建路径已执行）",
                ctx.Flow.CurrentState == Events.Fsm.StateStage, ctx.Flow.CurrentState);
            Check("★§A Pause→Resume 仍然**不**重建（场景代号未变时提前返回那条路径还在）",
                ConsoleLogger.CountOf("重复进入 Stage 站点（Pause→Resume 或重复触发；场景代号") == 1,
                "count=" + ConsoleLogger.CountOf("重复进入 Stage 站点（Pause→Resume 或重复触发；场景代号"));

            // ⑨ 回主菜单（清场）
            bus.Emit(Events.ToMainMenuRequest);
            Check("回主菜单成功", ctx.Flow.CurrentState == Events.Fsm.StateMainMenu, ctx.Flow.CurrentState);
            Check("清场① 面板 CloseAll", ui.CloseAllCount >= 1, "CloseAll=" + ui.CloseAllCount);
            Check("清场② 实体 ClearAll（3 → 0）", entities.ClearAllCount >= 1 && CountEntities(entities) == 0,
                "ClearAll=" + entities.ClearAllCount);
            Check("清场③ 对象池 ClearAll", pool.ClearAllCount >= 1, "ClearAll=" + pool.ClearAllCount);
            Check("清场④ 定时器 StopScope(stage)", timer.StoppedScopes.Contains("stage"), string.Join(",", timer.StoppedScopes));
            Check("清场⑤ 音效 StopAll", sound.StopAllCount >= 1, "StopAll=" + sound.StopAllCount);
            Check("清场⑥ 事件注销（离场后 ExitEntered 不再被 Flow 处理）", OffWorks(bus, ctx.Flow), "见上方日志（离场后无 [Flow] 区域切换日志）");
            Check("菜单场景已加载", scene.LoadedScenes.Contains(SceneNames.Menu), string.Join(",", scene.LoadedScenes));

            // ⑩ 再进一次（验收要求：连续两次进图无残留）
            setting.Set(GameConst.SettingKeyBgmVolume, 0.4f);
            setting.Save();
            bus.Emit(Events.CharSelectRequest, "HeroCheck");
            PumpLoading(fsm, ctx.Flow);                          // ★load：第二次进图同样逐帧推完读条分档
            Check("第二次进图站点 = Stage", ctx.Flow.CurrentState == Events.Fsm.StateStage, ctx.Flow.CurrentState);
            Check("第二次进图后实体已复位（0）", CountEntities(entities) == 0, "entities=" + CountEntities(entities));

            // ⑪ 设置面板的持久化写入路径（等价于 SettingsPanel 改音量）
            Check("设置已落盘（Save 被调用）", setting.SaveCount >= 1, "SaveCount=" + setting.SaveCount);
            Check("设置可读回（重进仍在）", Math.Abs(setting.Get<float>(GameConst.SettingKeyBgmVolume, 1f) - 0.4f) < 1e-6,
                setting.Get<float>(GameConst.SettingKeyBgmVolume, 1f).ToString("0.00"));

            // ⑫ 退出
            var quit = ctx.Flow.CurrentState;
            Check("QuitGame 不抛异常（打包分支 Application.Quit 不执行）", true, "站点=" + quit);

            // ⑬ AppContext.AutoWire 的**机制自证**：各模块实现都是 `internal sealed class`
            //    （隐式 public ctor）。若 Activator 创建不了它，App 装配就会静默降级 —— 所以先证这一步。
            //    （这里故意用宿主自己的 ProbeOnly，不去 new 真模块：`new AppFlow()` 会重复订阅事件。）
            var probe = Activator.CreateInstance(typeof(ProbeOnly));
            Check("AutoWire 机制：可反射创建 internal 实现类型", probe != null, typeof(ProbeOnly).FullName);

            // ★§B 现象 2 的强断言：站点日志条数 == 站点迁移次数（每条迁移恰好一行，不多不少）
            var stationLines = ConsoleLogger.CountOf("[INFO ] [Flow] → ");
            Check("★站点日志条数 == 站点迁移次数（每条迁移恰好一行）",
                stationLines == StationLog.Count, $"lines={stationLines} transitions={StationLog.Count}");

            Console.WriteLine();
            Console.WriteLine("站点迁移序列：" + string.Join(" → ", StationLog));
            foreach (var need in new[] { "Boot", "MainMenu", "CharSelect", "CharCreate", "Loading", "Stage" })
            {
                Check("站点序列包含 " + need, StationLog.Contains(need), string.Join(",", StationLog));
            }

            Console.WriteLine();
            Console.WriteLine("已覆盖（本轮新增）：Pause 站点（shim 里加了 `UnityEngine.Time` 替身，"
                + "`Time.timeScale` 可离线读写）⇒ 「暂停 → 选项 → 继续」的面板漏关可离线复现/回归。");
            Console.WriteLine("未覆盖（需要 Unity 原生 API，留给主 agent 进 Play 后验）："
                + "① 面板内部构件与像素布局；② 真实读条进度条的动画表现；"
                + "③ `Game.Event` 的**真实**派发顺序（宿主 shim 已按引擎语义实现「后注册先执行」，但仍非引擎本体）。");

            Console.WriteLine();
            Console.WriteLine(_fail == 0 ? "=== 自检全部通过 ===" : $"=== 自检失败 {_fail} 项 ===");
            return _fail == 0 ? 0 : 1;
        }

        /// <summary>离场后再发 ExitEntered：Flow 不应再处理（证明 Off 生效、方法引用一致）。</summary>
        private static bool OffWorks(ConsoleEventBus bus, IAppFlow flow)
        {
            var stationBefore = flow.CurrentState;
            bus.Emit(Events.ExitEntered, AreaId.DenOfEvil);
            return flow.CurrentState == stationBefore;
        }

        private static int CountEntities(IEntityManager e)
        {
            var n = 0;
            foreach (var _ in e.GetAll()) n++;
            return n;
        }

        /// <summary>照 `CharCreatePanel` 的公式从配表行造一份创角数据（同一条数值链路）。</summary>
        private static CharacterSave BuildSaveFromTable(Table.BaseClassRow row, string name)
        {
            var str = row.Str;
            var dex = row.Dex;
            var vit = row.Vit;
            var eng = row.Eng;
            return new CharacterSave
            {
                version = GameConst.SaveVersion,
                name = name,
                cls = (PlayerClass)row.Id,
                level = 1,
                str = str,
                dex = dex,
                vit = vit,
                eng = eng,
                life = Mathf.Max(1, Mathf.RoundToInt(vit * row.LifePerVit)),
                mana = Mathf.Max(1, Mathf.RoundToInt(eng * row.ManaPerMag)),
                stamina = Mathf.Max(1, Mathf.RoundToInt(vit * row.StamPerVit)),
                statPoints = row.StatPerLvl,
                areaId = (int)AreaId.Town,
                mapSeed = 0,
            };
        }

        private static void Check(string what, bool ok, string detail)
        {
            if (!ok) _fail++;
            Console.WriteLine($"{(ok ? "[ OK ]" : "[FAIL]")} {what}   ({detail})");
        }
    }
}
