// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · Module/Flow/AppFlow.cs
// 启动与流程编排（`IAppFlow` 的实现，internal）。
//
// 分工（照 skill `patterns/client/app-flow.md` §2）：
//   · `Game.Fsm` 只标记「我在哪个站点」（`Events.Fsm.State*`）；
//   · **面板与场景的开关全部写在本类**（各站点的 onEnter/onExit）；
//   · 面板只发/收事件（`Core/Events.cs` 的常量），本类订阅后驱动。
//
// 两处**刻意的设计选择**（与模板不同，原因是模板那样写会出 bug）：
//   ① `Stage` 站点**不注册 onExit 清场**：站点图里 `Stage →(Pause)→ Pause`，
//      若把清场挂在 Stage.onExit，**每次暂停都会把地图/实体/面板清干净**。
//      故清场由 `BackToMain()` 显式调用 `LeaveStage()`（幂等，`_stageActive` 守卫）。
//   ② `Pause →(Resume)→ Stage` 会再次触发 `Stage.onEnter`：`OnEnterStage` 用
//      `_stageActive` 守卫，避免**重新生成地图**（第二次进图才会真的重建）。
//       —— 真正「第二次进图」走的是 CharSelect → Loading → Stage（`_stageActive` 已复位）。
//
// 三处**容错**（都是"没按预期走"，一律打日志）：
//   · 模块未接入：`AppContext.X == null` → `FlowLog.Missing` + 功能降级；
//   · `Game.Scene.Load` 找不到场景时引擎**不回调 onDone** ⇒ 进图看门狗超时回主菜单；
//   · 面板缺参数 / 事件参数类型不符 ⇒ Warn（不静默）。
//
// ── agent-14 §B 修的两处流程缺陷（改动点都标了 `★§B`）─────────────────────────
//   ① **站点日志必须每站点恰好一条**：站点日志的唯一出口是 `Game.Fsm.OnChange → FlowLog.Station`
//      （`_common.md` §3.5）。原 `Enter()` 在"已经在 Boot 站点"的分支里**又打了一条**
//      `[Flow] → Boot`，加上 `Bootstrap` 里那次 `Force(Boot)` 触发的 OnChange ⇒ 启动时两条
//      （Play 日志 seq 47/48）。现在：要么是真迁移（OnChange 打），要么什么站点日志都不打。
//   ② **站点迁移必须把上一个站点的面板全部关掉**：原 `Pause.onExit` 只关了 `PausePanel`，
//      而暂停菜单里的「选项」是 `SettingsPanel`（Popup 层，`UI/PausePanel.cs:56`）⇒
//      `Pause → Stage`（继续）之后**选项面板永远挂在屏幕上**（Play 实测复现，见本文件 `SweepStaleStationPanels`）。
//      除逐个 onExit 补齐外，另加**兜底清扫**：站点迁移后凡不属于新站点的菜单类面板一律关掉 + Warn，
//      这样"漏关"这一类问题不会再复发（同类漏关 = 一个 Warn，而不是静默叠屏）。
//
// ── agent-17 §A 修的一处流程缺陷（改动点都标了 `★§A`）─────────────────────────
//   **进图可重入 ⇒ Stage 场景被重载、视图引用永久悬空**（agent-16 的 Play 取证）：
//   已经在 Stage 时再次发进图请求（选角屏「进入」/ 注入），原 `GoStage` 会
//   `Game.Scene.Load(SceneNames.Stage)` —— 引擎对**同名场景**不挡重载（`Runtime/Presentation/Scene.cs:21-69`），
//   于是 Stage 场景被重载、全部视图节点被销毁；而 `OnEnterStage` 因 `_stageActive == true`
//   **提前 return**（既不重建也不清场）⇒ 视图引用永久悬空（视图侧只能兜住不崩，根因在这里）。
//   三处修法：
//     ① `GoStage` 重入守卫：已在 Stage 且**同区** ⇒ 直接忽略（只打一条可检索 Info，**不重载场景、不清场**）；
//        已在 Stage 且**不同区** ⇒ 先 `LeaveStage()` 清场（7 项，见 `app-flow.md` §5）再进图 ——
//        **绝不允许**在不清场的情况下重载场景；
//        正在读条时又来一次进图请求 ⇒ 同样忽略（否则两个 `Game.Scene.Load` 并发，同类的重载风险）。
//     ② `OnEnterStage` 的 `_stageActive` 提前返回**之前**必须核对"当前 Stage 与实际状态是否一致"：
//        用「Stage 场景加载代号」（订阅 `Game.Scene.OnSceneLoaded`，`Runtime/Presentation/Scene.cs:101-104`
//        注册、`:54-65` 在 `onDone` **之前**逐个回调）识别"场景已被重载"
//        ⇒ **补一次清场 + 重建**，而不是静默 return。
//     ③ 两条可检索日志（验收 grep 用）：`[Flow] 进图请求被忽略（已在 Stage 同一区域）` /
//        `[Flow] 检测到 Stage 场景已被重载 ⇒ 补清场重建`。
//
// ── 「经典 load 动画」轮修的一处流程缺陷（改动点都标了 `★load`）────────────────
//   **缺陷（用户报，Play 实测）**：真流程里读条屏只闪过「第 1/10 帧 → 第 10/10 帧」。
//   根因：读条进度**只**铺在 `Game.Scene.Load` 上（`Runtime/Presentation/Scene.cs:34-43`），
//   而本工程 Stage 场景极小 ⇒ 引擎 ~40ms 就把 `op.progress` 推到门控上限 0.9
//   （Play 日志 20:36:40.229 → :40.231，**2ms** 走完 10 帧）⇒ 「经典的 load 动画」等于看不见。
//
//   原版把读条屏**铺在世界构建上**（Diablerie `Game/World/WorldBuilder.cs:29-64` 的
//   `LoadActCoroutine`：`Show(0.5f)` → `CreateAct` → `Show(0.75f)` → 主角 → `Show(0.9f)`
//   → `Show(1.0f)` → 一帧后才 `Hide()`）。本轮照它改：
//     ① **分档推进**（`LoadingSteps` + `RunBuildStep`）：10 档各绑一个**真实里程碑**
//        —— 场景就位 / 地图生成 / 主角装配 / 刷怪 / 相机 / 世界就绪，每 tick 推进一档
//        ⇒ 世界构建与首帧准备都铺在读条屏上（不再只铺 `Scene.Load`）；
//     ② **关屏时机**改为**世界就绪之后**（原版就是构建完才关）：`OnStageSceneReady` 不再关屏，
//        关屏在 `OnLoadingTick` 的「真实档到第 10 档 + 门已开满第 10 帧 + 再等一帧」三步都满足时发生；
//     ③ **进度仍是真的**：档位只由真实里程碑前移（`AdvanceReal`），呈现档位
//        = min(真实档位, 原版节奏放行档位, 已呈现+1) ⇒ 门**永不超前**真实进度；
//        节奏下限 `LoadingSteps.FrameCadenceSeconds` 只决定"看得见多久"（本工程真实管线 ~0.3s，
//        不设下限就"一帧跳到底"）；10 档铺开 ≈ 0.63s，与原版进图观感同量级。
//     ④ 装配自 `OnEnterStage` 抽出为 `RunBuildStep`，由 Loading 站点逐档执行；
//        `OnEnterStage` 只做收尾（订阅过门 / `[Stage]` 日志 / `StageEntered`），
//        并在「装配没在读条里做完」时（场景重载恢复 / 离线宿主 / 别的代码直接 Trigger(StageReady)）
//        本帧一次性补齐 + Warn —— 这条兜底分支是有意保留的（否则那些路径会进一张空地图）。
// ─────────────────────────────────────────────────────────────────────────────

using System;
using System.Collections.Generic;
using CloverEngine;
using Diablo2.Core;
using Diablo2.Def;
using Diablo2.UI;
using UnityEngine;
// 别名：`AppContext` 与 BCL 的 `System.AppContext` 同名（同时 `using System;` 会 CS0104）。
using AppContext = Diablo2.App.AppContext;

namespace Diablo2.Module.Flow
{
    /// <summary>流程编排实现（唯对外门面 = <see cref="IAppFlow"/>）。</summary>
    internal sealed class AppFlow : IAppFlow
    {
        private readonly CharRoster _roster = new CharRoster();

        /// <summary>当前选中的角色（进图用；回主菜单时清掉）。</summary>
        private CharacterSave _selected;

        /// <summary>当前区域（进图/区域切换用）。</summary>
        private AreaId _area = AreaId.Town;

        /// <summary>是否真的「在游戏里」（守卫 Pause↔Stage 往返时重建地图）。</summary>
        private bool _stageActive;

        /// <summary>是否正在读条进图（看门狗用）。</summary>
        private bool _loading;

        /// <summary>
        /// 进图看门狗截止时刻（**纯 .NET 墙钟 ticks**，不依赖 Unity 原生 `Time`）：
        /// 读条期间的 `Time.timeScale` 恒为 1，用墙钟与游戏时间等价；好处是这一路径
        /// 不碰引擎原生 API，可被 `tools/flowcheck` 的离线自检宿主完整跑一遍。
        /// </summary>
        private long _loadDeadlineTicks;

        /// <summary>正在做区域切换（防重入）。</summary>
        private bool _switchingArea;

        // ── ★ 片 T（S-08）：自环过门请求的**限频**状态（同一去处只报一次 + 每 N 次汇总）──────
        /// <summary>`出入口指向当前区域` 上一次报的"当前区域"（`-1` = 还没报过）。</summary>
        private int _dupExitFrom = -1;

        /// <summary>上一次报的"目标区域"。</summary>
        private int _dupExitTo = -1;

        /// <summary>同一去处被连续忽略的次数（换去处 / 真的切了区域就归零）。</summary>
        private int _dupExitCount;

        /// <summary>汇总报告间隔（每这么多次重复打一条 Warn；⛔ 不是静默，是"记一次 + 汇总"）。</summary>
        private const int DupExitReportEvery = 1000;

        /// <summary>★ T0FIX-D：最近一次存档的结果（`Events.SaveDone` 的参数）。</summary>
        private bool _lastSaveOk;

        /// <summary>
        /// ★ R7（本片 Q）：本会话**已经弹过提示**的读档失败原因（含档名）。
        /// 为什么必须去重：`ISaveModule.ListAll()` 是**逐个 `Load()`** 的（`SaveModule.cs:340-350`），
        /// 选角屏每次刷新都会再过一遍 ⇒ 不去重就是"每开一次选角屏弹一个框"，玩家关不掉。
        /// </summary>
        private readonly HashSet<string> _loadFailureNotified = new HashSet<string>();

        /// <summary>★ T0FIX-D：本次会话已消费的存档事件次数（可观测账目）。</summary>
        private int _savesObserved;

        /// <summary>★ T0FIX-D：最近一次存档是否成功（`Events.SaveDone` 的消费者账目；自证/诊断用）。</summary>
        public bool LastSaveOk { get { return _lastSaveOk; } }

        /// <summary>★ T0FIX-D：已消费的存档事件次数（自证用）。</summary>
        public int SavesObserved { get { return _savesObserved; } }

        /// <summary>
        /// ★§A「Stage 场景加载代号」：每次引擎回调 `Game.Scene.OnSceneLoaded(Stage)` 自增。
        /// <para>用途：`OnEnterStage` 在 `_stageActive` 重入时判断**本次重入前场景是否被重载过**
        /// （重载过 ⇒ 旧场景节点连同视图已被销毁、引用全部悬空 ⇒ 必须补清场重建）。</para>
        /// <para>为什么用引擎回调而不是自己数 `Load` 调用：`OnSceneLoaded` 是**引擎侧的客观事实**
        /// （`Runtime/Presentation/Scene.cs:54-65`，在 `onDone` **之前**回调），
        /// 别的代码路径直接重载场景时也能被识别到。</para>
        /// </summary>
        private int _stageSceneEpoch;

        /// <summary>★§A 本次舞台装配对应的场景代号（`OnEnterStage` 记账，重入时与 <see cref="_stageSceneEpoch"/> 比对）。</summary>
        private int _stageEpochAtSetup;

        // ═════════════════════════════════════════════════════════════════════
        // ★load 进图读条屏的分档推进（见文件头「经典 load 动画」轮）
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>★load 读条屏打开的时刻（`Now()` 口径）—— 门的"第 1 档"起点，用来算节奏与可见时长。</summary>
        private long _loadStartedTicks;

        /// <summary>★load **真实**里程碑已到的档号（0 = 刚打开；9 = 世界就绪）。只前移、不回退。</summary>
        private int _realFrame;

        /// <summary>★load **已呈现**到第几档（门显示的是第 `_doorFrame + 1` 帧）。</summary>
        private int _doorFrame;

        /// <summary>★load 下一个待执行的装配档（0..`BuildStepCount`；= `BuildStepCount` 表示装配已完成）。</summary>
        private int _buildNext;

        /// <summary>★load `Stage` 场景是否已就位（引擎 `onDone` 到了 = 原版 `Show(0.5f)` 的前提）。</summary>
        private bool _sceneReady;

        /// <summary>★load 是否已排定"下一帧关屏"（原版 `Show(1.0f)` 之后还有一次 `yield return null`）。</summary>
        private bool _closeNextTick;

        /// <summary>★load 本局进图的 seed（装配时掷定；地图生成与 `[Stage]` 日志共用）。</summary>
        private int _entrySeed;

        /// <summary>★load 进图装配世代：每次进 Loading 站点 +1（`LeaveStage` 把它作废）。</summary>
        private int _buildEpoch;

        /// <summary>★load 已完成装配的世代（`OnEnterStage` 据此判断"装配是否已在读条分档里做完"）。</summary>
        private int _builtEpoch = -1;

        /// <summary>
        /// ★load 可注入的墙钟（单调秒，`double`；与 `Core/Log.Clock` 同口径）。
        /// <para>默认 `null` = 用 `DateTime.UtcNow`。**刻意不用 Unity 的 `Time`**：那是原生 ECall，
        /// 在非 Unity 进程（`.ai-tmp/hosts/*check` 离线宿主）里会抛 `SecurityException`
        /// ⇒ 本文件与看门狗一样只走 BCL 时钟，离线宿主可注入假时钟让读条节奏**完全可复现**。</para>
        /// </summary>
        public static Func<double> Clock { get; set; }

        /// <summary>★load 当前时刻（墙钟 ticks；离线宿主可经 <see cref="Clock"/> 注入）。</summary>
        private static long Now()
            => Clock != null ? (long)(Clock() * TimeSpan.TicksPerSecond) : DateTime.UtcNow.Ticks;

        /// <summary>两个 ticks 之间的秒数（负数按 0 处理，避免注入时钟回拨时算出负节奏）。</summary>
        private static double SecondsBetween(long fromTicks, long toTicks)
        {
            if (toTicks <= fromTicks) return 0d;
            return (toTicks - fromTicks) / (double)TimeSpan.TicksPerSecond;
        }

        /// <summary>构造函数即注册站点/迁移/订阅：保证 `Game.Fsm.Force("Boot")` 不会打到未注册状态。</summary>
        public AppFlow()
        {
            if (Game.Fsm == null || Game.Event == null)
            {
                // 正常路径：Bootstrap 先 Game.Launch 再 new AppFlow。走到这里说明顺序错了。
                Log.Error(FlowLog.Tag, "AppFlow 构造时引擎未启动（Game.Launch 未调用）⇒ 站点/迁移/事件订阅未注册");
                return;
            }

            RegisterStates();
            RegisterTransitions();
            Subscribe();

            // ★§A：订阅「场景加载完成」以识别"Stage 场景被重载"（见 `_stageSceneEpoch` 与 `OnEnterStage`）。
            if (Game.Scene != null)
            {
                Game.Scene.OnSceneLoaded(OnAnySceneLoaded);
            }
            else
            {
                // 非预期分支（引擎未启动）：可重入兜底退化为只靠 `GoStage` 守卫，必须留日志。
                Log.Warn(FlowLog.Tag,
                    "游戏启动时 Game.Scene 为 null ⇒ 未订阅 OnSceneLoaded（进图可重入兜底退化为只靠 GoStage 守卫）");
            }
        }

        /// <inheritdoc/>
        public string CurrentState => Game.Fsm.Current;

        /// <summary>App 注入的模块注册表（null = Bootstrap 未装配 ⇒ 全部降级）。</summary>
        private static AppContext Ctx => AppContext.I;

        // ═════════════════════════════════════════════════════════════════════
        // 对外
        // ═════════════════════════════════════════════════════════════════════

        /// <inheritdoc/>
        public void Enter()
        {
            if (Game.Fsm == null)
            {
                Log.Error(FlowLog.Tag, "Enter() 时 Game.Fsm 为 null（Game.Launch 未调用），流程无法启动");
                return;
            }

            if (Game.Fsm.Current != Events.Fsm.StateBoot)
            {
                // ★§B：这条 Force 会经 `Fsm.SwitchTo → OnChange` 打**唯一一条** `[Flow] → Boot`
                //       （`Runtime/Core/Fsm.cs:174-189`）。调用方（`App/Bootstrap`）**不许**再 Force 一次。
                Game.Fsm.Force(Events.Fsm.StateBoot);      // 触发 Boot.onEnter → 打开启动画面
            }
            else
            {
                // ★§B：已经在 Boot 站点 ⇒ **不是站点迁移** ⇒ 这里绝不能打 `[Flow] → Boot`
                //       （否则站点日志变成两条，验收 grep `[Flow] →` 会数出 2；agent-14 §B 现象 2）。
                Log.Info(FlowLog.Tag, "重复调用 Enter()：当前已在 Boot 站点，无站点迁移 ⇒ 不重复打站点日志");
            }

            Log.Info(FlowLog.Tag, $"流程已进入，当前站点={CurrentState}（菜单类站点共用场景 {SceneNames.Menu}）");
        }

        /// <inheritdoc/>
        public void GoStage(AreaId area)
        {
            if (_selected == null)
            {
                Log.Warn(FlowLog.Tag, $"GoStage({area}) 被调用但没有已选角色 ⇒ 忽略；请先在选角屏选一个角色");
                Game.UI.Toast("请先选择角色");
                return;
            }

            if (Game.Scene == null)
            {
                Log.Error(FlowLog.Tag, "GoStage：Game.Scene 为 null（引擎未启动），无法读条进图");
                return;
            }

            // ── ★§A 重入守卫 ────────────────────────────────────────────────────
            //   进图可重入 = 本缺陷的根因：`Game.Scene.Load` 对**同名场景**不挡重载
            //   （`Runtime/Presentation/Scene.cs:21-69`，`LoadSceneAsync` 直接重载）
            //   ⇒ 不清场就重载 ⇒ 场景内视图节点全销毁、`OnEnterStage` 又不重建 ⇒ 引用悬空。
            //   因此：**Stage 活动期间任何进图请求都必须先经过这里**。
            if (_stageActive)
            {
                if (area == _area)
                {
                    // 就是当前区域 ⇒ 什么都不做（**不重载场景、不清场、不做站点迁移**）。
                    // 注：tag（`Flow`）由 `Log` 统一加前缀 ⇒ 整行是 `[INFO ] [Flow] 进图请求被忽略（已在 Stage 同一区域）…`
                    //     （验收 grep 的片段就是这一句；消息里**不再**重复写 `[Flow]`）
                    Log.Info(FlowLog.Tag,
                        $"进图请求被忽略（已在 Stage 同一区域）区域={area} 站点={CurrentState} " +
                        $"角色={_selected.name} 场景代号=#{_stageSceneEpoch} ⇒ 不重载 {SceneNames.Stage} 场景、不清场" +
                        "（若放行则会重载场景并让全部视图引用悬空）");
                    return;
                }

                // 不同区域 ⇒ 走「先清场再进图」的正规路径：清场 7 项照 skill `patterns/client/app-flow.md` §5
                //   （面板 / 实体与视图 / 对象池 / 定时器 scope / 音效 / 事件订阅 / 模块状态）。
                Log.Warn(FlowLog.Tag,
                    $"进图请求切换区域 {_area} → {area}：当前已在 Stage ⇒ **先清场再进图**" +
                    "（清场 7 项：面板/实体/对象池/定时器/音效/事件订阅/模块状态；绝不允许不清场就重载场景）");
                var keepSelected = _selected;
                LeaveStage();                  // 幂等清场；会把 _stageActive/_selected/_area/_switchingArea 复位
                _selected = keepSelected;      // 进图仍然要用这个角色（LeaveStage 把它清成了 null）
            }
            else if (_loading)
            {
                // 读条中又来一次进图请求 ⇒ 放行就是**两个 `Game.Scene.Load` 并发**（同类的场景重载风险）。
                Log.Warn(FlowLog.Tag,
                    $"读条中的重复进图请求已忽略（请求区域={area} 在途加载区域={_area} " +
                    $"目标场景={SceneNames.Stage}）⇒ 等本次读条结束后再由玩家重新发起");
                return;
            }

            _area = area;
            _selected.areaId = (int)area;
            if (_selected.mapSeed == 0)
            {
                _selected.mapSeed = Rng.FromTime().Seed;
                Log.Info(FlowLog.Tag, $"角色「{_selected.name}」本局地图 seed 已决定：{_selected.mapSeed}");
            }

            Game.Fsm.Trigger(Events.Fsm.TriggerEnterStage);   // → Loading（onEnter 打开 LoadingPanel 并起分档）
            Game.Scene.Load(SceneNames.Stage, OnSceneProgress, OnStageSceneReady);
            Log.Info(FlowLog.Tag,
                $"读条进图：角色={_selected.name} 区域={area} seed={_selected.mapSeed} 场景={SceneNames.Stage}" +
                "（读条屏按真实里程碑分档推进；世界构建 + 首帧准备都铺在它上面，关屏在世界就绪之后）");
        }

        /// <inheritdoc/>
        public void BackToMain()
        {
            var from = Game.Fsm != null ? Game.Fsm.Current : null;

            LeaveStage();

            if (from == Events.Fsm.StateMainMenu)
            {
                Log.Info(FlowLog.Tag, "已在主菜单，刷新主菜单面板（幂等）");
                OpenMainMenu();
                return;
            }

            Game.Fsm.Trigger(Events.Fsm.TriggerToMain);       // 任意站点 → MainMenu（触发器表是全局映射）
            Log.Info(FlowLog.Tag, $"回主菜单（从站点 {from}）");
        }

        /// <inheritdoc/>
        public void QuitGame()
        {
            Log.Info(FlowLog.Tag, "退出游戏（先把设置落盘）");
            Game.Setting?.Save();

#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        // ═════════════════════════════════════════════════════════════════════
        // 站点 / 迁移 / 订阅
        // ═════════════════════════════════════════════════════════════════════

        private void RegisterStates()
        {
            var fsm = Game.Fsm;

            fsm.RegisterState(Events.Fsm.StateBoot,
                onEnter: () => Game.UI.Open<BootPanel>(),
                onExit: () => Game.UI.Close<BootPanel>());

            fsm.RegisterState(Events.Fsm.StateMainMenu,
                onEnter: OpenMainMenu,
                onExit: () =>
                {
                    Game.UI.Close<SettingsPanel>();
                    Game.UI.Close<MainMenuPanel>();
                });

            fsm.RegisterState(Events.Fsm.StateCharSelect,
                onEnter: OpenCharSelect,
                onExit: () =>
                {
                    Game.UI.Close<SettingsPanel>();
                    Game.UI.Close<CharSelectPanel>();
                });

            fsm.RegisterState(Events.Fsm.StateCharCreate,
                onEnter: OpenCharCreate,
                onExit: () => Game.UI.Close<CharCreatePanel>());

            fsm.RegisterState(Events.Fsm.StateLoading,
                onEnter: OnEnterLoading,
                onTick: OnLoadingTick,
                onExit: () => Game.UI.Close<LoadingPanel>());

            // ★ 见文件头 ①：Stage 不注册 onExit（暂停不该清场）。
            fsm.RegisterState(Events.Fsm.StateStage,
                onEnter: OnEnterStage,
                onTick: OnStageTick);

            fsm.RegisterState(Events.Fsm.StatePause,
                onEnter: OnEnterPause,
                onTick: OnPauseTick,
                onExit: OnExitPause);
        }

        private void RegisterTransitions()
        {
            var fsm = Game.Fsm;

            fsm.AddTransition(Events.Fsm.TriggerBootDone, Events.Fsm.StateMainMenu);
            fsm.AddTransition(Events.Fsm.TriggerNewGame, Events.Fsm.StateCharSelect);
            fsm.AddTransition(Events.Fsm.TriggerContinue, Events.Fsm.StateCharSelect);
            fsm.AddTransition(Events.Fsm.TriggerNeedCreate, Events.Fsm.StateCharCreate);
            fsm.AddTransition(Events.Fsm.TriggerCreated, Events.Fsm.StateCharSelect);
            fsm.AddTransition(Events.Fsm.TriggerEnterStage, Events.Fsm.StateLoading);
            fsm.AddTransition(Events.Fsm.TriggerStageReady, Events.Fsm.StateStage);
            fsm.AddTransition(Events.Fsm.TriggerPause, Events.Fsm.StatePause);
            fsm.AddTransition(Events.Fsm.TriggerResume, Events.Fsm.StateStage);
            fsm.AddTransition(Events.Fsm.TriggerToMain, Events.Fsm.StateMainMenu);
        }

        /// <summary>
        /// Flow 常驻：所有订阅一律用**具名私有方法**（`Game.Event` 没有句柄，匿名 lambda 注销不掉）。
        /// UI 侧「请求站点」的做法 = 把 `Events.Fsm.Trigger*` 常量当事件名 Emit（不是裸字符串）。
        /// </summary>
        private void Subscribe()
        {
            // ★§B：`Fsm.OnChange` **全进程只注册一次** —— 站点日志（`FlowLog.Station`）从这里出来，
            //      注册两次就会把每个站点打成两条（验收要求"每站点恰好一条"）。
            //      正常路径只有一个 `AppFlow`（`Bootstrap` 显式 new，`IAppFlow` 不参与 AutoWire）；
            //      走到 else 说明有第二个实例 —— 那是**非预期分支，必须留日志**。
            if (_stationChangeHooked)
            {
                Log.Warn(FlowLog.Tag,
                    "检测到第二个 AppFlow 实例（Fsm.OnChange 已注册过）⇒ 本次不重复注册，" +
                    "站点日志仍为每站点一条。请检查谁又 new 了 AppFlow（IAppFlow 不应参与 AutoWire）");
            }
            else
            {
                _stationChangeHooked = true;
                Game.Fsm.OnChange(OnStationChanged);
            }

            Game.Event.On(Events.BootDone, OnBootDone);
            Game.Event.On(Events.Fsm.TriggerNewGame, OnNewGameRequest);
            Game.Event.On(Events.Fsm.TriggerContinue, OnContinueRequest);
            Game.Event.On(Events.Fsm.TriggerNeedCreate, OnNeedCreateRequest);
            Game.Event.On<CharacterSave>(Events.CharCreateRequest, OnCharCreateSubmit);
            Game.Event.On<string>(Events.CharSelectRequest, OnCharSelectRequest);
            Game.Event.On<string>(Events.CharDeleteRequest, OnCharDeleteRequest);
            Game.Event.On(Events.PauseRequest, OnPauseRequest);
            Game.Event.On(Events.ResumeRequest, OnResumeRequest);
            Game.Event.On(Events.SaveAndExitRequest, OnSaveAndExitRequest);
            // ★ T0FIX-D：`Events.SaveDone` 的**唯一消费者**（此前 0 生产者 / 0 消费者）
            Game.Event.On<bool>(Events.SaveDone, OnSaveDone);
            // ★ R7（本片 Q）：`Events.LoadDone` 的**唯一消费者**（`Core/Events.cs:368`：参数 null = 读档失败）。
            //   修前该事件只有 `SaveModule.Load` 成功路径在发、且 **0 订阅者** ⇒ 损坏档完全静默。
            Game.Event.On<CharacterSave>(Events.LoadDone, OnLoadDone);
            Game.Event.On(Events.ToMainMenuRequest, OnToMainMenuRequest);
            Game.Event.On(Events.QuitRequest, OnQuitRequest);
            Game.Event.On(Events.MultiplayerUnavailable, OnMultiplayerUnavailable);
        }

        private void OnStationChanged(string from, string to)
        {
            FlowLog.Station(to);                                        // 验收要抄的那一行
            SweepStaleStationPanels(to);                                // ★§B 兜底：上一个站点的面板不许留
            Game.Event.Emit(Events.FlowStationChanged, to);
        }

        // ═════════════════════════════════════════════════════════════════════
        // 站点迁移的「面板兜底清扫」（★§B 现象 1 的通用修法）
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>`Game.Fsm.OnChange` 是否已挂钩（见 `Subscribe`）。</summary>
        private static bool _stationChangeHooked;

        /// <summary>
        /// 新一局 Play 的静态复位（由 `App/Bootstrap` 的 `[RuntimeInitializeOnLoadMethod]` 调；skill P-3）。
        /// 域不重载时 `_stationChangeHooked` 会残留 ⇒ 第二局**一条站点日志都不会有**（验收直接判失败）。
        /// </summary>
        internal static void ResetStaticForNewPlaySession()
        {
            _stationChangeHooked = false;
            Clock = null;      // ★load：上一局若有探针注入过假时钟，不许带进本局（否则读条节奏错乱）
        }

        /// <summary>
        /// 站点迁移后：**凡不属于新站点的菜单类面板一律关掉并 Warn**。
        /// <para>为什么要有它：各站点的 `onExit` 是手写的，**漏一个就是静默叠屏**（玩家看到两个界面叠在一起，
        /// 日志里什么都没有）。实测就踩到过 `Pause → Stage` 漏关「选项」面板（见 `OnExitPause`）。
        /// 有了这一步，"漏关"这一类问题的表现变成**一条可检索的 Warn**，而不是"看不出来"。</para>
        /// <para>为什么不是 `Game.UI.CloseAll()`：本方法在 `onEnter` **之后**跑（`SwitchTo` 先 OnEnter 再
        /// 派发 OnChange），`CloseAll` 会把新站点刚开好的面板一起关掉。</para>
        /// </summary>
        private void SweepStaleStationPanels(string station)
        {
            if (Game.UI == null)
            {
                Log.Warn(FlowLog.Tag, $"站点已切到 {station}，但 Game.UI 为 null ⇒ 面板兜底清扫跳过（可能残留旧面板）");
                return;
            }

            // 站点 → 该站点允许存在的菜单类面板（其余一律关）。`SettingsPanel` 是 MainMenu/Pause 的**子面板**。
            CloseUnlessAllowed<BootPanel>(station, Events.Fsm.StateBoot);
            CloseUnlessAllowed<MainMenuPanel>(station, Events.Fsm.StateMainMenu);
            CloseUnlessAllowed<CharSelectPanel>(station, Events.Fsm.StateCharSelect);
            CloseUnlessAllowed<CharCreatePanel>(station, Events.Fsm.StateCharCreate);
            CloseUnlessAllowed<LoadingPanel>(station, Events.Fsm.StateLoading);
            CloseUnlessAllowed<PausePanel>(station, Events.Fsm.StatePause);
            CloseUnlessAllowed<SettingsPanel>(station, Events.Fsm.StateMainMenu, Events.Fsm.StatePause);
        }

        /// <summary>该面板若开着且新站点不在 <paramref name="allowed"/> 里 ⇒ 关掉 + Warn（越界即"漏关"证据）。</summary>
        private static void CloseUnlessAllowed<T>(string station, params string[] allowed) where T : class, IUIPanel
        {
            if (!Game.UI.IsOpen<T>()) return;

            for (var i = 0; i < allowed.Length; i++)
            {
                if (allowed[i] == station) return;
            }

            Log.Warn(FlowLog.Tag,
                $"站点已切到 {station}，但上一个站点的面板 {typeof(T).Name} 仍开着 ⇒ 兜底关闭" +
                "（真正的修法是补它的 onExit；本条 Warn 就是「漏关」的证据）");
            Game.UI.Close<T>();
        }

        // ═════════════════════════════════════════════════════════════════════
        // 车站点：打开面板
        // ═════════════════════════════════════════════════════════════════════

        private void OpenMainMenu()
        {
            EnsureMenuScene(() =>
            {
                Game.UI.Close<CharSelectPanel>();
                Game.UI.Close<CharCreatePanel>();
                Game.UI.Open<MainMenuPanel>(new MainMenuPanel.Args { hasSave = _roster.HasAny });
                Log.Info(FlowLog.Tag, $"主菜单已打开：存档={(_roster.HasAny ? "有" : "无")}（来源={( _roster.Persisted ? "ISaveModule" : "会话内内存(降级)" )}），继续按钮={( _roster.HasAny ? "可点" : "置灰")}");
            });
        }

        private void OpenCharSelect()
        {
            EnsureMenuScene(() =>
            {
                Game.UI.Close<CharCreatePanel>();
                Game.UI.Open<CharSelectPanel>(BuildCharSelectArgs());
            });
        }

        private void OpenCharCreate()
        {
            EnsureMenuScene(() => Game.UI.Open<CharCreatePanel>(BuildCharCreateArgs()));
        }

        private CharSelectPanel.Args BuildCharSelectArgs()
        {
            var args = new CharSelectPanel.Args();
            var all = _roster.ListAll();
            for (var i = 0; i < all.Count; i++)
            {
                var save = all[i];
                if (save == null || string.IsNullOrEmpty(save.name))
                {
                    Log.Warn(FlowLog.Tag, $"存档列表第 {i} 条数据非法（name 为空），已跳过");
                    continue;
                }

                args.entries.Add(new CharSelectPanel.Entry
                {
                    name = save.name,
                    className = ClassTable.NameOf(save.cls),
                    level = save.level,
                    cls = save.cls,
                });
            }
            return args;
        }

        private static CharCreatePanel.Args BuildCharCreateArgs()
        {
            return new CharCreatePanel.Args
            {
                classes = ClassTable.Build(),
                defaultName = Cfg.DefaultPlayerName,
            };
        }

        /// <summary>菜单类站点统一在 `Menu` 场景里（已在则不重复加载，避免"切界面要等读条"）。</summary>
        private void EnsureMenuScene(Action onReady)
        {
            if (Game.Scene == null)
            {
                Log.Error(FlowLog.Tag, "EnsureMenuScene：Game.Scene 为 null（引擎未启动），面板无法在正确场景打开");
                return;
            }

            if (Game.Scene.CurrentScene == SceneNames.Menu)
            {
                onReady?.Invoke();
                return;
            }

            Log.Info(FlowLog.Tag, $"加载菜单场景 {SceneNames.Menu}（当前场景={Game.Scene.CurrentScene ?? "(null)"}）");
            Game.Scene.Load(SceneNames.Menu, null, () =>
            {
                Log.Info(FlowLog.Tag, $"菜单场景 {SceneNames.Menu} 已就绪");
                onReady?.Invoke();
            });
        }

        // ═════════════════════════════════════════════════════════════════════
        // 进图：读条 → Stage
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>
        /// ★§A 引擎「场景加载完成」回调（`ISceneManager.OnSceneLoaded`，在 `onDone` **之前**回调）：
        /// 只关心 `Stage`，用来给场景打「加载代号」—— 这是 `OnEnterStage` 识别"场景已被重载"的唯一依据。
        /// <para>只在**重载**（舞台仍活动、场景又加载了一次）时才可能出问题，所以那种情况额外提示一句。</para>
        /// </summary>
        private void OnAnySceneLoaded(string sceneName)
        {
            if (sceneName != SceneNames.Stage) return;

            _stageSceneEpoch++;

            if (_stageActive)
            {
                Log.Warn(FlowLog.Tag,
                    $"Stage 场景加载代号 = #{_stageSceneEpoch}，但**舞台仍处于活动状态** ⇒ 本次加载会重载场景、" +
                    "旧场景节点与视图引用将失效（下一次进入 Stage 站点时必须补清场重建）");
            }
            else
            {
                Log.Info(FlowLog.Tag, $"Stage 场景加载代号 = #{_stageSceneEpoch}");
            }
        }

        /// <summary>
        /// ★load 引擎场景加载的**真进度**回调（`Game.Scene.Load(name, onProgress, …)`）。
        /// <para>它只推进门的**前 5 档**（[0, 0.9] → 门开到一半，见 `LoadingSteps.SceneLoadFrameIndex`）；
        /// 真正的呈现统一发生在 `OnLoadingTick` 的呈现步里（分档 + 原版节奏 + 不超前真实）。
        /// 修前这里直接 `panel.SetProgress(整段映射)` ⇒ 场景一加载完门就到第 10 帧。</para>
        /// </summary>
        private void OnSceneProgress(float progress)
        {
            var frame = LoadingSteps.SceneLoadFrameIndex(progress);
            if (frame > _realFrame)
            {
                AdvanceReal(frame, $"引擎场景加载真进度 {progress:0.###}（值域 [0,0.9] → 门的前 5 档）");
            }
        }

        /// <summary>
        /// ★load 引擎「场景加载完成」回调（`Game.Scene.Load` 的 `onDone`）：
        /// **不再关屏**，改为进入「世界构建」分档（对应原版 `WorldBuilder.cs:33` 的 `Show(0.5f)`）。
        /// </summary>
        private void OnStageSceneReady()
        {
            _sceneReady = true;
            AdvanceReal(LoadingSteps.SceneLoadedFrame,
                $"{SceneNames.Stage} 场景就位（引擎 onDone）＝ 原版 Show(0.5f)");
            Log.Info(FlowLog.Tag,
                $"{SceneNames.Stage} 场景已就位（读条屏第 {_doorFrame + 1}/{LoadingSteps.Count} 帧）" +
                "⇒ 开始「世界构建 + 首帧准备」分档；**读条屏继续显示**，关屏推迟到世界就绪之后");
        }

        /// <summary>★load 读条屏打开（Loading 站点 onEnter）：起第一档并开始计时。</summary>
        private void OnEnterLoading()
        {
            _loading = true;
            _loadStartedTicks = Now();
            _loadDeadlineTicks = _loadStartedTicks
                + TimeSpan.FromSeconds(FlowConst.StageLoadTimeoutSeconds).Ticks;

            // 分档推进的复位（每次进图都是从第 1 帧、第 1 档开始）
            _sceneReady = false;
            _closeNextTick = false;
            _realFrame = LoadingSteps.SceneOpenedFrame;
            _doorFrame = LoadingSteps.SceneOpenedFrame;
            _buildNext = 0;
            _buildEpoch++;

            Game.UI.Open<LoadingPanel>(FlowConst.LoadingTipStage);
            Log.Info(FlowLog.Tag,
                $"读条屏已打开：分档推进 {LoadingSteps.Count} 档（帧号 = 档号，逐档绑真实里程碑）；" +
                $"呈现节奏下限 {LoadingSteps.FrameCadenceSeconds:0.###}s/档" +
                $"（10 档铺开 ≈ {LoadingSteps.FrameCadenceSeconds * (LoadingSteps.Count - 1):0.00}s）；" +
                "关屏时机 = 世界就绪（装配完成 + 首帧已渲染）**且**门已开满第 10 帧");
        }

        /// <summary>
        /// ★load Loading 站点的逐帧推进：**先做真实工作、再呈现、最后判关屏**。
        /// <para>三步各自都有硬约束：① 每 tick 最多执行一档装配（⇒ 每档至少跨一个已渲染帧，
        /// 世界构建真的铺在好几帧上）；② 门只按真实档位 + 原版节奏开；③ 关屏要三个条件同时满足。</para>
        /// </summary>
        private void OnLoadingTick(float dt)
        {
            if (!_loading) return;

            if (Now() > _loadDeadlineTicks)
            {
                // 引擎在场景缺失时只打 Error、**不会**回调 onDone ⇒ 不兜底就会永远卡在读条屏。
                _loading = false;
                Log.Error(FlowLog.Tag,
                    $"读条超时 {FlowConst.StageLoadTimeoutSeconds:0}s：{SceneNames.Stage} 场景未加载完成" +
                    $"（场景缺失或未加进 Build Settings？真实档 {_realFrame + 1}/{LoadingSteps.Count}" +
                    $"，已呈现 {_doorFrame + 1}/{LoadingSteps.Count}）⇒ 回主菜单，避免卡死");
                Game.UI.Toast("进图失败：场景加载超时（见日志）");
                BackToMain();
                return;
            }

            // ① 真实工作：每 tick 最多一档（场景就位之后才开始世界构建分档）
            if (_sceneReady && _buildNext < BuildStepCount)
            {
                RunBuildStep(_buildNext);
                _buildNext++;
            }

            // ② 呈现：门一档一档地开（节奏下限），且永不超前真实档位
            PresentDoor();

            // ③ 关屏：真实档到「世界就绪」**且**门已开满第 10 帧 ⇒ 再等一帧才关
            //    （原版 `WorldBuilder.cs:57-58` 也是 `Show(1.0f)` 之后还有一次 `yield return null`）
            if (_realFrame < LoadingSteps.WorldReadyFrame || _doorFrame < LoadingSteps.WorldReadyFrame) return;

            if (!_closeNextTick)
            {
                _closeNextTick = true;
                Log.Info(FlowLog.Tag,
                    $"读条屏已呈现第 {LoadingSteps.Count}/{LoadingSteps.Count} 帧且世界就绪" +
                    $"（门 10 档用 {SecondsBetween(_loadStartedTicks, Now()):0.00}s）⇒ 下一帧关屏（原版 Show(1.0f) 后的那次 yield）");
                return;
            }

            CloseLoadingAndEnterStage();
        }

        /// <summary>
        /// ★load 门（原版 10 帧读条图）的**呈现**：一档一档地开，且**永不超前**真实进度。
        /// <para>呈现档位 = min(真实档位, 原版节奏放行的档位, 已呈现档位 + 1)。三个上限的含义：
        /// ① 真实档位 ⇒ 门只开到"世界里真做完的事"那么多（**不许假进度**，任务书 ③）；
        /// ② 节奏放行 ⇒ 每档至少可见 <see cref="LoadingSteps.FrameCadenceSeconds"/>（任务书 ②：可见时长，
        ///    本工程真实管线只有 ~0.3s，不加下限就"一帧跳到底"）；
        /// ③ 已呈现 + 1 ⇒ 每帧最多前进一档（保证每一档都真的被渲染过，不会跳过中间帧）。</para>
        /// </summary>
        private void PresentDoor()
        {
            var paced = LoadingSteps.MaxIndexAt(SecondsBetween(_loadStartedTicks, Now()));
            var allowed = _realFrame < paced ? _realFrame : paced;
            if (_doorFrame + 1 < allowed) allowed = _doorFrame + 1;
            if (allowed <= _doorFrame) return;

            _doorFrame = allowed;

            var panel = Game.UI.Get<LoadingPanel>();
            if (panel == null)
            {
                // 非预期分支：读条期间面板不在（不该发生）⇒ 留一条限频 Warn，但**不卡住流程**
                // （档位照常记账，关屏条件仍可满足；否则这里会变成"永久卡在 Loading"）。
                Log.WarnThrottled(FlowLog.Tag, "loading.panel.missing.present",
                    "读条期间 LoadingPanel 不在（Game.UI.Get 返回 null）⇒ 门的档位照常推进，" +
                    "但这一档没有任何画面被显示");
                return;
            }

            panel.SetProgress(LoadingSteps.CompletenessOf(_doorFrame), LoadingSteps.ReasonOf(_doorFrame));
        }

        /// <summary>★load 关屏并进 Stage 站点（= 原版 `LoadingScreen.Hide()` 的位置：世界构建完之后）。</summary>
        private void CloseLoadingAndEnterStage()
        {
            _loading = false;
            _closeNextTick = false;

            var visible = SecondsBetween(_loadStartedTicks, Now());
            Game.UI.Close<LoadingPanel>();
            Game.Fsm.Trigger(Events.Fsm.TriggerStageReady);     // → Stage（onEnter 只做收尾 + 发 StageEntered）
            Log.Info(FlowLog.Tag,
                $"{SceneNames.Stage} 场景加载完成、世界就绪 ⇒ 关掉读条屏并切换到 Stage 站点" +
                $"（读条屏可见 {visible:0.00}s，门 {LoadingSteps.Count} 档 × {LoadingSteps.FrameCadenceSeconds:0.###}s；" +
                $"其中真实世界构建每档一帧，见上面各行的日志时间戳）");
        }

        /// <summary>
        /// ★load 把**真实**里程碑前移到第 <paramref name="frame"/> 档（只前移、不回退）。
        /// <para>这是门唯一的"进度来源"：每一档都必须由一件**世界里真做完的事**驱动
        /// （场景就位 / 地图生成 / 主角装配 / 刷怪 / 相机 / 世界就绪），没有任何"随时间自增"。</para>
        /// </summary>
        private void AdvanceReal(int frame, string why)
        {
            if (frame <= _realFrame) return;
            _realFrame = frame >= LoadingSteps.Count ? LoadingSteps.Count - 1 : frame;

            Log.Info(FlowLog.Tag,
                $"读条真实档 {_realFrame + 1}/{LoadingSteps.Count}：{why}" +
                $"（门当前第 {_doorFrame + 1} 帧；呈现永不超前真实，只会滞后）");
        }

        /// <summary>★load 进图装配的**真实**分档数（每档 = 一件真事；`RunBuildStep` 的取值 0..4）。</summary>
        private const int BuildStepCount = 5;

        /// <summary>
        /// ★load 装配的一档（由 `OnLoadingTick` 每帧推进一档；`OnEnterStage` 的兜底分支一次性跑完）。
        /// <para>为什么不一次做完：全部塞在一帧里 = 世界构建**完全铺不上**读条屏（本轮修的就是它）。
        /// 每档都对应 `LoadingSteps` 里的一个真实里程碑，做完立刻 `AdvanceReal` 让门可以跟上。</para>
        /// </summary>
        private void RunBuildStep(int step)
        {
            var ctx = Ctx;
            if (ctx == null)
            {
                FlowLog.WarnOnce("ctx.null", "AppContext.I 为 null（Bootstrap 未装配）⇒ 地图/角色/怪物全部不可用");
            }

            switch (step)
            {
                case 0:
                    // 地图分档：本局 seed（**每次进图都重新掷**，见 `RollEntrySeed`）+ 生成 + 地形渲染
                    RollEntrySeed();
                    if (ctx?.Map != null)
                    {
                        ctx.Map.Generate(_area, _entrySeed);
                        ctx.Map.ShowArea(_area);
                    }
                    else FlowLog.Missing("IMapModule");
                    AdvanceReal(LoadingSteps.MapFrame, "地图生成 + 地形渲染完成");
                    break;

                case 1:
                    // 主角分档：数据（属性/装备/技能）→ 视图（精灵/动画）
                    var save = _selected;
                    if (ctx?.Player != null && save != null) ctx.Player.LoadFrom(save);
                    else if (save != null) FlowLog.Missing("IPlayerModule");

                    if (ctx?.View != null && save != null) ctx.View.CreatePlayer(save.cls);
                    else if (save != null) FlowLog.Missing("IViewModule");
                    AdvanceReal(LoadingSteps.PlayerFrame, "主角数据 + 视图装配完成");
                    break;

                case 2:
                    if (ctx?.Monster != null) ctx.Monster.SpawnArea(_area);
                    else FlowLog.Missing("IMonsterModule");
                    AdvanceReal(LoadingSteps.MonsterFrame, "刷怪 / NPC 装配完成");
                    break;

                case 3:
                    if (ctx?.Camera != null)
                    {
                        var focus = ctx.Player?.Grid ?? ctx.Map?.SpawnPoint ?? Vector2Int.zero;
                        ctx.Camera.SetTargetGrid(focus);
                        ctx.Camera.SnapToTarget();
                    }
                    else FlowLog.Missing("ICameraRig");
                    AdvanceReal(LoadingSteps.CameraFrame, "相机就位（原版 Show(0.9f)）");
                    break;

                default:
                    // 世界就绪：装配账记上（`OnEnterStage` 据此知道"不必再补做"）；
                    // 「首帧已渲染」由 `OnLoadingTick` 的第 ③ 步保证（门开满后还要再等一帧才关屏）。
                    _builtEpoch = _buildEpoch;
                    AdvanceReal(LoadingSteps.WorldReadyFrame,
                        $"世界就绪（装配 {BuildStepCount}/{BuildStepCount} 完成 + 首帧已渲染）");
                    break;
            }
        }

        /// <summary>★load 一次性跑完剩下的装配档（兜底路径：没有读条屏可铺时用）。</summary>
        private void RunBuildStepsToEnd()
        {
            for (var s = _buildNext; s < BuildStepCount; s++) RunBuildStep(s);
            _buildNext = BuildStepCount;
        }

        /// <summary>
        /// 掷本局地图 seed：**一律重新掷**（不读存档里的 `mapSeed`）——原版单人每次从菜单进游戏都是
        /// **新的一局**，野外/地牢**重新生成**；把上一局的 mapSeed 存盘再拿回来当种子，
        /// 会让"读档再进"永远是同一张图（用户报的"野外不是随机的地图"就是这个）。
        /// 存档里的 `mapSeed` 保留为"上一局用过什么"的记录，只写不读。
        /// </summary>
        private void RollEntrySeed()
        {
            var save = _selected;
            var previousSeed = save != null ? save.mapSeed : 0;
            _entrySeed = Rng.FromTime().Seed;
            if (save != null) save.mapSeed = _entrySeed;

            Log.Info(FlowLog.Tag,
                $"[Map] 新一局：重新掷地图 seed={_entrySeed}（上一局记录 seed={previousSeed}，不复用）" +
                $"区域={_area}");
        }

        private void OnEnterStage()
        {
            if (_stageActive)
            {
                // Pause →(Resume)→ Stage 会再进来一次；正常情况这里**不能**重建地图（否则一暂停一恢复地图就换了）。
                // ★§A：但提前 return **之前必须确认"当前 Stage 与实际状态一致"** —— 若期间 `Game.Scene`
                //   被重载过（场景加载代号变了），旧场景节点连同视图已被销毁、引用全部悬空
                //   ⇒ 必须**补一次清场 + 重建**，而不是静默返回。
                if (_stageSceneEpoch == _stageEpochAtSetup)
                {
                    Log.Info(FlowLog.Tag,
                        $"重复进入 Stage 站点（Pause→Resume 或重复触发；场景代号 #{_stageSceneEpoch} 未变），不重建地图/实体");
                    return;
                }

                Log.Warn(FlowLog.Tag,
                    $"检测到 Stage 场景已被重载 ⇒ 补清场重建（装配时代号 #{_stageEpochAtSetup}，当前 #{_stageSceneEpoch}；" +
                    "旧场景节点与视图引用已失效）");
                var keepSelected = _selected;
                var keepArea = _area;
                LeaveStage();                       // 补清场（7 项，幂等）；会把 _stageActive/_selected/_area/_builtEpoch 复位
                _selected = keepSelected;
                _area = keepArea;
                if (_selected == null)
                {
                    Log.Error(FlowLog.Tag,
                        $"补清场重建时没有已选角色（_selected == null）⇒ 地图/角色装配将用临时 seed 降级，区域仍按 {_area}");
                }
                // 落到下面的正常装配（重建地图/角色/怪物/相机/事件订阅）
            }

            _stageActive = true;
            _loading = false;
            _stageEpochAtSetup = _stageSceneEpoch;      // ★§A：记账本次装配对应的场景代号

            if (_builtEpoch != _buildEpoch)
            {
                // ★load 兜底（非预期分支）：装配没在读条屏的分档里做完 —— 场景被重载 / 别的代码直接
                //   `Trigger(StageReady)` / 离线宿主不经读条。此刻**没有读条屏可铺**，只能本帧一次性补齐。
                //   正常路径（`GoStage` → Loading 站点分档）不会走到这里。
                Log.Warn(FlowLog.Tag,
                    $"进图装配未在读条屏的分档里完成（装配世代 #{_buildEpoch}，已完成 #{_builtEpoch}）" +
                    $"⇒ 本帧一次性补齐 {BuildStepCount} 档装配（此刻没有读条屏可呈现；" +
                    "正常路径由 Loading 站点逐档做完，见 RunBuildStep 的日志）");
                _buildNext = 0;
                RunBuildStepsToEnd();
            }

            FinishStageEntry();
        }

        /// <summary>
        /// Stage 站点的收尾（原 `OnEnterStage` 的后半段）：订阅过门事件 + 打 `[Stage]` 装配摘要 +
        /// 发 `StageEntered`（HUD 由 UI 侧监听它自行打开，约定见 `_common.md` §3.5）。
        /// <para>★load：它现在发生在**读条屏关掉之后的一帧内**（原版 `Hide()` 的位置），
        /// 所以区域名弹出/HUD 不会在读条屏后面被"耗掉"时间。</para>
        /// </summary>
        private void FinishStageEntry()
        {
            var save = _selected;

            // Stage 作用域订阅：离场时用**同一方法引用**注销（见 LeaveStage ⑥）。
            Game.Event.On<AreaId>(Events.ExitEntered, OnExitEntered);
            Game.Event.On(Events.MapAreaReady, OnMapAreaReady);   // ★ travel-black：换区第二拍的落位信号

            var ctx = Ctx;
            Log.Info(FlowLog.Tag,
                $"[Stage] 区域={_area} seed={_entrySeed} 地图={ctx?.Map?.Width}x{ctx?.Map?.Height} " +
                $"障碍={ctx?.Map?.BlockedCount} 角色={(save != null ? save.name : "-")} 等级={(save != null ? save.level : 0)} " +
                $"（HUD 由 UI 侧监听 {Events.StageEntered} 自行打开）");

            Game.Event.Emit(Events.StageEntered);
        }

        private void OnStageTick(float dt)
        {
            // ★ travel-black：换区第二拍的**超时兜底**（正常路径由 `Events.MapAreaReady` 触发，见 OnMapAreaReady）。
            if (_arrivalTo >= 0 && Time.realtimeSinceStartup >= _arrivalDeadline)
            {
                if (!_arrivalTimedOutLogged)
                {
                    _arrivalTimedOutLogged = true;
                    Log.Warn(FlowLog.Tag, $"换区落位等待超时 {ArrivalMapTimeoutSeconds:0.#}s：地图侧未发 {Events.MapAreaReady}" +
                        "（MapView 未建满/未被调用？）⇒ 本帧**强制落位**（非预期分支：屏上可能仍有空块）");
                }
                CompleteArrival("超时兜底（地图侧未发 AreaReady）");
            }

            if (!EscPressed()) return;

            // 选项面板开着时 ESC 归它（它自己关闭），Flow 不抢。
            if (Game.UI.IsOpen<SettingsPanel>()) return;

            Game.Fsm.Trigger(Events.Fsm.TriggerPause);
        }

        private void OnEnterPause()
        {
            Time.timeScale = 0f;                       // 真暂停（原版单人行为）
            Game.UI.Open<PausePanel>();
            Log.Info(FlowLog.Tag, $"已暂停（Time.timeScale=0；暂停内定时器必须用 *Unscaled）");
        }

        private void OnPauseTick(float dt)
        {
            if (!EscPressed()) return;
            if (Game.UI.IsOpen<SettingsPanel>()) return;   // 选项面板优先吃 ESC
            Game.Fsm.Trigger(Events.Fsm.TriggerResume);
        }

        private void OnExitPause()
        {
            Time.timeScale = 1f;
            Game.UI.Close<PausePanel>();

            // ★§B：暂停菜单里的「选项」是**子面板**（`UI/PausePanel.cs:56` → `UILayer.Popup`）。
            //       漏关它 = 继续游戏后选项面板一直叠在 HUD 上（Play 实测复现：
            //       `[PF] fsm=Stage PausePanel=False SettingsPanel=True`，7 秒后仍在）。
            //       与 `MainMenu.onExit` / `CharSelect.onExit` 保持一致（它们都关 SettingsPanel）。
            Game.UI.Close<SettingsPanel>();
        }

        /// <summary>
        /// ★ T0FIX-C：ESC 一律走**别名** `GameKeyAlias.KeyPause`（键位的单一来源），
        /// ⛔ 不再直连 `GameKey.Escape` —— 直连会让"改键位只改一处"失效（D11 的零消费别名）。
        /// 值不变（两者都是 `GameKey.Escape`）⇒ 行为逐字不变。
        /// </summary>
        private static bool EscPressed()
        {
            if (Game.Input == null)
            {
                FlowLog.WarnOnce("input.null", "Game.Input 未挂载（CloverInput.Init 未调用）⇒ ESC 暂停/继续不可用");
                return false;
            }
            return Game.Input.GetKeyDown(GameKeyAlias.KeyPause);
        }

        // ═════════════════════════════════════════════════════════════════════
        // 事件处理（具名私有方法 = 可被 Off 掉）
        // ═════════════════════════════════════════════════════════════════════

        private void OnBootDone()
        {
            Game.Fsm.Trigger(Events.Fsm.TriggerBootDone);
        }

        private void OnNewGameRequest()
        {
            EnsureMenuScene(() =>
            {
                Game.Fsm.Trigger(Events.Fsm.TriggerNewGame);
                if (_roster.HasAny) return;

                Log.Info(FlowLog.Tag, "无存档角色 ⇒ 新游戏直接进入创角屏（站点图 MainMenu → CharSelect → CharCreate）");
                Game.Fsm.Trigger(Events.Fsm.TriggerNeedCreate);
            });
        }

        private void OnContinueRequest()
        {
            if (!_roster.HasAny)
            {
                Log.Warn(FlowLog.Tag, "收到「继续」请求但没有任何存档（按钮本应置灰）⇒ 只刷新主菜单");
                Game.UI.Toast("暂无可继续的存档");
                OpenMainMenu();
                return;
            }

            EnsureMenuScene(() => Game.Fsm.Trigger(Events.Fsm.TriggerContinue));
        }

        private void OnNeedCreateRequest()
        {
            EnsureMenuScene(() => Game.Fsm.Trigger(Events.Fsm.TriggerNeedCreate));
        }

        /// <summary>创角提交（`Events.CharCreateRequest`，载荷 = `Def.CharacterSave`）。</summary>
        private void OnCharCreateSubmit(CharacterSave save)
        {
            if (save == null)
            {
                Log.Error(FlowLog.Tag, "收到空的创角请求（CharacterSave == null），忽略");
                return;
            }

            if (string.IsNullOrEmpty(save.name))
            {
                Log.Warn(FlowLog.Tag, "创角失败：角色名为空");
                Game.UI.Toast("请输入角色名");
                return;
            }

            if (_roster.Exists(save.name))
            {
                Log.Warn(FlowLog.Tag, $"创角失败：角色名「{save.name}」已存在（原版也不允许重名）");
                Game.UI.Toast("角色名已存在");
                return;
            }

            if (save.mapSeed == 0)
            {
                save.mapSeed = Rng.FromTime().Seed;   // 建角即定本局 seed（存档可复现同一张图）
                Log.Info(FlowLog.Tag, $"创角：「{save.name}」本局地图 seed={save.mapSeed}");
            }

            // ★ 起始装备（配表 `start_item_c` ← 官方 charstats.txt 的 item1..item10）：
            //   原版新角色自带「武器（+ 盾）+ 药水 + 卷轴」⇒ 必须在**写档 / 入名册之前**落到 `save` 里，
            //   否则落盘的是一份空装备档（用户实测：新角色徒手打不动怪）。
            //   走**已有的 `IItemModule` 契约**（`LoadFrom` 内部会对"新鲜草稿档"按职业补装备，
            //   见 `Module/Item/StartItems.cs` 与 `ItemModule.LoadFrom`），`WriteTo` 再把结果回写进 `save`。
            //   ⛔ 这里**不引用** `Diablo2.Module.Item` 的任何类型 —— 分层自检 ② 要求
            //      `Module/*` 里 0 处 `using Diablo2.Module.*`（跨模块协作走事件或 App 注入接口）。
            var itemMod = Ctx?.Item;
            if (itemMod == null)
            {
                FlowLog.Missing("IItemModule");      // 起始装备发不了 —— 必须留痕（不许静默）
            }
            else
            {
                itemMod.LoadFrom(save);              // 新鲜草稿档 ⇒ 按职业补 `equip` / `inventory`
                itemMod.WriteTo(save);               // 回写（写档之前）
            }

            if (!_roster.Create(save))
            {
                Log.Error(FlowLog.Tag, $"创角失败：角色「{save.name}」未能写入名册（见上一行原因）");
                Game.UI.Toast("创建角色失败，见日志");
                return;
            }

            _selected = null;      // 保持原版行为：建角后回选角屏，由玩家点「进入」
            Log.Info(FlowLog.Tag,
                $"建角成功：{save.name} 职业={ClassTable.NameOf(save.cls)} 等级={save.level} " +
                $"生命={save.life} 法力={save.mana} 耐力={save.stamina} 剩余点数={save.statPoints} ⇒ 回选角屏");
            Log.Info(FlowLog.Tag,
                $"建角『{save.name}』起始装备已落档：装备 {save.equip.Count} 件 / 背包格 {save.inventory.Count} 格"
                + $"（锚点 {CountAnchors(save.inventory)} 个）→ "
                + DescribeStartEquip(save));
            Game.Fsm.Trigger(Events.Fsm.TriggerCreated);
        }

        /// <summary>背包锚点数（= 实际入包的物品件数；创角日志用）。</summary>
        private static int CountAnchors(List<InventorySlot> inv)
        {
            if (inv == null) return 0;
            var n = 0;
            for (var i = 0; i < inv.Count; i++)
            {
                if (inv[i] != null && inv[i].isAnchor && inv[i].item != null) n++;
            }
            return n;
        }

        /// <summary>起始装备的可读清单（`装备:名字` + 背包锚点，日志/自证用）。</summary>
        private static string DescribeStartEquip(CharacterSave save)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("装备=[");
            for (var i = 0; i < save.equip.Count; i++)
            {
                var it = save.equip[i];
                if (it == null) continue;
                if (sb.Length > 1) sb.Append(',');
                sb.Append(it.name).Append('×').Append(it.count);
            }
            sb.Append("] 背包=[");
            var first = true;
            for (var i = 0; i < save.inventory.Count; i++)
            {
                var s = save.inventory[i];
                if (s == null || !s.isAnchor || s.item == null) continue;
                if (!first) sb.Append(',');
                first = false;
                sb.Append(s.item.name).Append('×').Append(s.item.count).Append('@').Append(s.index);
            }
            sb.Append(']');
            return sb.ToString();
        }

        private void OnCharSelectRequest(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                // 约定：空名 = 「仅返回选角屏」（创角屏的返回按钮用；不选中任何角色）。
                Log.Info(FlowLog.Tag, $"选角请求：空角色名 ⇒ 仅返回选角屏（当前站点={CurrentState}）");
                if (CurrentState == Events.Fsm.StateCharSelect) OpenCharSelect();
                else Game.Fsm.Trigger(Events.Fsm.TriggerCreated);
                return;
            }

            // ★ R7（本片 Q）：改走契约的 `TryLoad`（修前**全仓 0 调用点**）—— 它给出"读到没读到"的布尔，
            //   配合契约既有的 `LastError` 就能**区分两种 null**：
            //   · 失败 + `LastError` 非空 ⇒ 真的读不出来（损坏 / 槽位目录不可用）—— 用户可见反馈已由
            //     `OnLoadDone` 弹过 `D2ConfirmPanel` ⇒ 这里 ⛔ **不再叠一条"找不到该角色"的 Toast**（那是误导）；
            //   · 失败 + `LastError` 为空 ⇒ 档不存在（正常）⇒ 保留原有的"找不到该角色"轻提示。
            CharacterSave save;
            var saveMod = Ctx?.Save;
            if (saveMod != null)
            {
                if (!saveMod.TryLoad(name, out save)) save = null;
            }
            else
            {
                save = _roster.Load(name);      // 降级路径：无存档模块 ⇒ 会话内名册
            }

            if (save == null)
            {
                var reason = saveMod != null ? saveMod.LastError : null;
                if (!string.IsNullOrEmpty(reason))
                {
                    Log.Warn(FlowLog.Tag, $"选角失败：角色「{name}」的存档读不出来 ⇒ 已弹「存档损坏」用户可见提示" +
                        $"并留在选角屏；原因={reason}");
                }
                else
                {
                    Log.Warn(FlowLog.Tag, $"选角失败：名册里找不到角色「{name}」");
                    Game.UI.Toast("找不到该角色");
                }
                OpenCharSelect();
                return;
            }

            _selected = save;
            Log.Info(FlowLog.Tag,
                $"已选角色「{save.name}」职业={save.cls} 等级={save.level} 区域={save.areaId} seed={save.mapSeed}");

            var ctx = Ctx;
            if (ctx?.Skill != null) ctx.Skill.ResetForClass(save.cls, save);
            else FlowLog.Missing("ISkillModule");

            GoStage(ToArea(save.areaId));
        }

        private void OnCharDeleteRequest(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                Log.Warn(FlowLog.Tag, "删除角色请求：角色名为空，忽略");
                return;
            }

            if (_selected != null && _selected.name == name) _selected = null;

            if (!_roster.Delete(name))
            {
                Log.Error(FlowLog.Tag, $"删除角色「{name}」失败（见上一行原因）");
                Game.UI.Toast("删除失败，见日志");
            }

            OpenCharSelect();     // 刷新列表（Open 已打开的面板 = 重新 OnOpen）
        }

        private void OnPauseRequest()
        {
            if (CurrentState != Events.Fsm.StateStage)
            {
                Log.Warn(FlowLog.Tag, $"收到暂停请求，但当前站点={CurrentState}（只在 Stage 生效），忽略");
                return;
            }
            Game.Fsm.Trigger(Events.Fsm.TriggerPause);
        }

        private void OnResumeRequest()
        {
            if (CurrentState != Events.Fsm.StatePause)
            {
                Log.Warn(FlowLog.Tag, $"收到继续请求，但当前站点={CurrentState}（只在 Pause 生效），忽略");
                return;
            }
            Game.Fsm.Trigger(Events.Fsm.TriggerResume);
        }

        private void OnSaveAndExitRequest()
        {
            SaveCurrentCharacter();
            BackToMain();
        }

        /// <summary>
        /// ★ T0FIX-D：`Events.SaveDone`（参数 = 是否成功）的**唯一消费者**。
        /// <para>为什么这样处置（而不是删事件）：`Events.SaveDone` 在 `Core/Events.cs:316` 已有明确的
        /// 参数语义（bool 是否成功），而 `Core/` 是冻结层（删它要改 Core）⇒ 按验收表**规则 7**
        /// 「定义了但没人用」的本意，补上**生产者**（`SaveModule.Save/Save(CharacterSave)` 的
        /// 成功/失败**两条**出口都发）与**消费者**（本方法）。</para>
        /// <para>消费者形态 = **可观测的最小消费者**：存档结果账（`LastSaveOk`）+ 每次存档一条
        /// 可检索日志。⛔ **不加"保存中/已保存"的 UI 元件** —— 原版 D2 单机存档是**静默**的
        /// （没有该提示的素材/出处），按全局 skill §0「A 没有 ⇒ 不加」。</para>
        /// </summary>
        private void OnSaveDone(bool ok)
        {
            _lastSaveOk = ok;
            _savesObserved++;
            if (ok)
            {
                Log.Info(FlowLog.Tag,
                    $"[T0FIX] 收到 {Events.SaveDone}(success=true) ⇒ 存档结果账 = 成功（第 {_savesObserved} 次）");
                return;
            }

            Log.Warn(FlowLog.Tag,
                $"[T0FIX] 收到 {Events.SaveDone}(success=false) ⇒ 本次存档失败（第 {_savesObserved} 次；" +
                "详细原因见 Save 模块的 Error 行）");
        }

        /// <summary>
        /// ★ R7（本片 Q）：`Events.LoadDone` 的**消费者**（`Core/Events.cs:368`：参数 null = 读档失败）。
        /// <para>**缺陷**（穷举审计片 `audit-C` 红行 R7，用户没报过）：`SaveModule.Load()` 的"档不存在"与
        /// "解析失败"**都返回 null**，而 `LastError` 的唯一消费者是**保存**失败分支
        /// ⇒ 玩家的读档失败**没有任何用户可见反馈**（损坏档在选角屏表现为"角色凭空消失"）。</para>
        /// <para>修法：把两种失败按 `LastError` 分流（空 = 档不存在 = 正常，不报错；非空 = 真失败 ⇒ 提示）。</para>
        /// </summary>
        private void OnLoadDone(CharacterSave data)
        {
            if (data != null) return;                 // 成功：`SaveModule.Load` 自己已记完整日志

            var save = Ctx?.Save;
            if (save == null)
            {
                Log.Warn(FlowLog.Tag, $"收到 {Events.LoadDone}(null) 但 ISaveModule 未接入 ⇒ 拿不到失败原因");
                return;
            }

            var reason = save.LastError;
            if (string.IsNullOrEmpty(reason))
            {
                // 档不存在 = **正常情形**（新玩家 / 空槽）⇒ 不报错、不弹提示（R7 情况 ①）
                Log.Info(FlowLog.Tag, $"收到 {Events.LoadDone}(null)：LastError 为空 ⇒ 判定为「档不存在」（正常），不提示");
                return;
            }

            NotifyLoadFailure(reason);
        }

        /// <summary>
        /// ★ R7：读档失败的**用户可见反馈**（复用项目既有的 `UI/D2ConfirmPanel`：原版窗框 + 原版中等按钮）。
        /// <para>⛔ **不新造面板 / 不换皮**（`D2ConfirmPanel` 的文件头已论证过"引擎 `Game.UI.Confirm` 是引擎默认 uGUI，
        /// 与本项目的原版石雕按钮同屏两种风格"）。</para>
        /// <para>⛔ **不在此处替玩家删档** —— 损坏文件原样保留（引擎 `FileSlotStore` 另有 `.corrupt` 留档），
        /// 删档只能由玩家在选角屏显式点 DELETE；故本提示的两个出口都只关闭弹窗（组件本身恒为两按钮）。</para>
        /// </summary>
        private void NotifyLoadFailure(string reason)
        {
            if (!_loadFailureNotified.Add(reason))
            {
                // 已提示过（`ListAll()` 每次刷新都会重新 `Load` 一遍同一批档）⇒ 只留痕，不再弹
                Log.Info(FlowLog.Tag, $"[R7] 同一读档失败本会话已提示过 ⇒ 不再重复弹框：{reason}");
                return;
            }

            Log.Error(FlowLog.Tag,
                $"[R7·读档失败·用户可见] 提示玩家「存档损坏」：{reason}；" +
                "该存档不会出现在角色列表中（文件未被覆盖/删除）");

            if (Game.UI == null)
            {
                Log.Warn(FlowLog.Tag, "[R7] 读档失败但 Game.UI 未接入 ⇒ 无法弹提示（已在日志里点名，不静默）");
                return;
            }

            D2ConfirmPanel.Show(
                "存档损坏",
                ShortReason(reason),
                () => Log.Info(FlowLog.Tag, "[R7] 玩家确认了「存档损坏」提示"),
                () => Log.Info(FlowLog.Tag, "[R7] 玩家关闭了「存档损坏」提示"),
                "确定", "关闭");
        }

        /// <summary>
        /// ★ R7：把 <see cref="ISaveModule.LastError"/> 压成能放进 `D2ConfirmPanel` 正文框的一句玩家话。
        /// <para>为什么必须有这一步（**实机图给的教训**，⛔ 不是想当然）：提示框正文框只有
        /// **272×90 原版px**（`UiLayoutFlow.Confirm.MessageSizeOrig`）＝约 17 个汉字/行 × 2 行；
        /// 第一版直接把 `LastError`（含引擎判定 + `.corrupt` 副本路径，80+ 字）塞进去，
        /// 实机图 `.ai-tmp/screenshots/q3_corrupt_dialog.png` 上文字**冲出框外**。
        /// 修法：框里只放一句话，**完整技术细节留在 `[Save]` 的 Error 行**（排障入口不变）。</para>
        /// </summary>
        private static string ShortReason(string reason)
        {
            const int max = 32;
            if (string.IsNullOrEmpty(reason)) return "存档读取失败";
            return reason.Length <= max ? reason : reason.Substring(0, max) + "...";
        }

        private void OnToMainMenuRequest()
        {
            Log.Info(FlowLog.Tag, $"收到回主菜单请求（当前站点={CurrentState}）");
            BackToMain();
        }

        private void OnQuitRequest()
        {
            Log.Info(FlowLog.Tag, "收到退出请求");
            QuitGame();
        }

        private void OnMultiplayerUnavailable()
        {
            Log.Warn(FlowLog.Tag, "多人游戏未实装联机（本项目形态=单机，见策划案 §0）⇒ 给出提示并留在主菜单");
            Game.UI.Toast("本版本未实装联机");
        }

        /// <summary>玩家踩到出入口 ⇒ 切换区域（原地重生成地图，不重载场景）。</summary>
        private void OnExitEntered(AreaId target)
        {
            EnterArea(target);
        }

        private void EnterArea(AreaId to)
        {
            if (_switchingArea)
            {
                Log.Warn(FlowLog.Tag, $"区域切换进行中，忽略重复请求 {to}");
                return;
            }

            // ★ 片 T（S-08）：**与发送方同源判据** —— `PlayerModule.CheckExit` 决定"发不发过门请求"
            //   用的就是 `IMapModule.Area`（出口目标由它推出），而这里原来只比 `_area`
            //   ⇒ 两道闸门不同源：地图还没重生成/地图未接入时 `_area` 已前进、`map.Area` 还停在旧值，
            //     同一个去处会被反复拒（这正是两道闸门"各判各的"那种缺陷的形状）。
            //   现在：以**已生成的地图**为准（它就是玩家脚下那张图），地图不可用时才退回 `_area`。
            var ctxMap = Ctx?.Map;
            var cur = ctxMap != null && ctxMap.IsGenerated ? ctxMap.Area : _area;
            if (to == cur)
            {
                // ⛔ 不静默（不是把铃声拆掉）：第一次把数据异常完整说清；之后按 1000 次汇总，
                //   防日志风暴 —— 实测 `.ai-tmp/test` 记的 12 分钟 39069 条就是这一行刷出来的。
                if (_dupExitFrom != (int)cur || _dupExitTo != (int)to)
                {
                    _dupExitFrom = (int)cur;
                    _dupExitTo = (int)to;
                    _dupExitCount = 0;
                    Log.Warn(FlowLog.Tag, $"出入口指向当前区域 {to}（数据异常？）⇒ 忽略，不重生成地图"
                        + $"（判据 = 地图 Area，与 PlayerModule.CheckExit 同源；同一去处只报一次）");
                }
                else if (++_dupExitCount % DupExitReportEvery == 0)
                {
                    Log.Warn(FlowLog.Tag, $"出入口仍指向当前区域 {to} ⇒ 已累计忽略 {_dupExitCount} 次"
                        + $"（形如上游在重复发同一次过门请求，见上一条 Warn）");
                }
                return;
            }

            if (_dupExitCount > 0)
            {
                Log.Info(FlowLog.Tag, $"出入口自环请求结束：{_dupExitFrom} 共被忽略 {_dupExitCount} 次");
                _dupExitCount = 0;
            }

            _switchingArea = true;
            var from = _area;
            _area = to;
            if (_selected != null)
            {
                _selected.areaId = (int)to;
                _selected.mapSeed = Rng.FromTime().Seed;     // 每次换区域都是一局新的随机地图
            }

            var seed = _selected != null ? _selected.mapSeed : Rng.FromTime().Seed;
            Log.Info(FlowLog.Tag, $"区域切换 {from} → {to}（seed={seed}）");

            Game.UI.ShowLoading($"正在进入 {to}…");

            var ctx = Ctx;
            if (ctx?.Map != null)
            {
                ctx.Map.Generate(to, seed);
                ctx.Map.ShowArea(to);       // ★ travel-black：只**登记**重铺（范围按**落点**算，相机仍留在旧区）
            }
            else FlowLog.Missing("IMapModule");

            // ── ★ travel-black：第一拍到此为止 —— **不在这里挪玩家/相机** ──────────────────────────
            //   缺陷（实机逐帧量到，`.ai-tmp/screenshots/travelblack_tb1.log`）：旧顺序是 `ShowArea` **之后**
            //   立刻挪玩家 + `SnapToTarget` ⇒ 相机已经落到新区域的出生格，而生效的渲染集还是**旧区**那张图
            //   （出生格超出旧图范围 ⇒ 屏上零地砖）⇒ **落地整屏黑 ≈1.84 s**，直到地图侧第二次重铺才补回来。
            //   新顺序：等 `Events.MapAreaReady`（新区域**建满并已切换**才发，见 `Module/Map/MapView.cs` 的
            //   `NotifyAreaReadyIfOwed`）再落位 ⇒ 同一帧里"新图 + 玩家/相机在新位置"一起生效，中间帧不出现空屏。
            _arrivalTo = (int)to;
            _arrivalFrom = (int)from;
            _arrivalSpawn = ctx?.Map?.SpawnPoint ?? Vector2Int.zero;
            _arrivalDeadline = Time.realtimeSinceStartup + ArrivalMapTimeoutSeconds;
            _arrivalTimedOutLogged = false;
            Log.Info(FlowLog.Tag, $"[Stage] 区域切换 {from} → {to}：地图已生成 + 已登记重铺（范围按落点 " +
                $"({_arrivalSpawn.x},{_arrivalSpawn.y}) 算，相机留在旧区）；**等 {Events.MapAreaReady} 才挪玩家/相机**" +
                $"（超时 {ArrivalMapTimeoutSeconds:0.#}s 兜底）");

            // 地图模块没接上时不会有人发 AreaReady ⇒ 本帧直接落位（非预期分支，点名不留卡死）
            if (ctx?.Map == null)
            {
                Log.Warn(FlowLog.Tag, $"IMapModule 未接入 ⇒ 没有人会发 {Events.MapAreaReady}，本帧直接落位（区域里将只有空图）");
                CompleteArrival("IMapModule 未接入（直接落位，超时兜底）");
            }
        }

        /// <summary>★ travel-black：等"新区域建满并已切换"的超时（秒）。⛔ 兜底必须有 —— 地图侧不发事件时不许卡死在旧区。</summary>
        private const float ArrivalMapTimeoutSeconds = 8f;

        /// <summary>★ travel-black：待落位的区域（-1 = 没有待落位）。</summary>
        private int _arrivalTo = -1;

        /// <summary>★ travel-black：本次切换的来源区域（只进日志）。</summary>
        private int _arrivalFrom = -1;

        /// <summary>★ travel-black：落点（= 新区域的出生格）。</summary>
        private Vector2Int _arrivalSpawn;

        /// <summary>★ travel-black：落位等待的截止时刻（<see cref="Time.realtimeSinceStartup"/> 口径）。</summary>
        private float _arrivalDeadline;

        /// <summary>★ travel-black：超时兜底只报一次。</summary>
        private bool _arrivalTimedOutLogged;

        /// <summary>
        /// ★ travel-black：`Events.MapAreaReady` 的收方（`MapView` 只在**换区那次**重铺建满并切换后发一次）。
        /// </summary>
        private void OnMapAreaReady()
        {
            if (_arrivalTo < 0) return;      // 不是换区（例如进图那一次重铺）：与本流程无关，不发日志
            CompleteArrival($"{Events.MapAreaReady}（新区域已建满并已切换）");
        }

        /// <summary>
        /// ★ travel-black：换区的**第二拍** —— 挪怪 / 挪玩家 / 挪相机 / 关读条屏 / 发 `AreaChanged`。
        /// <para>它由 <see cref="OnMapAreaReady"/>（正常）或 <see cref="OnStageTick"/> 的超时分支（兜底）调用。</para>
        /// </summary>
        private void CompleteArrival(string why)
        {
            var to = (AreaId)_arrivalTo;
            var from = _arrivalFrom;
            var spawn = _arrivalSpawn;
            _arrivalTo = -1;
            _arrivalFrom = -1;
            _arrivalTimedOutLogged = false;

            var ctx = Ctx;
            if (ctx?.Monster != null)
            {
                ctx.Monster.DespawnAll();
                ctx.Monster.SpawnArea(to);
            }
            else FlowLog.Missing("IMonsterModule");

            if (ctx?.Player != null)
            {
                ctx.Player.TeleportTo(spawn);
                if (_selected != null)
                {
                    _selected.gridX = spawn.x;
                    _selected.gridY = spawn.y;
                }
            }
            else FlowLog.Missing("IPlayerModule");

            if (ctx?.Camera != null)
            {
                ctx.Camera.SetTargetGrid(spawn);
                ctx.Camera.SnapToTarget();
            }

            Game.UI.HideLoading();
            Game.Event.Emit(Events.AreaChanged, to);
            Log.Info(FlowLog.Tag, $"[Stage] 区域已切换为 {to}（{from} → {to}），出生格=({spawn.x},{spawn.y})；" +
                $"落位依据 = {why}");
            _switchingArea = false;
        }

        // ═════════════════════════════════════════════════════════════════════
        // 存档（只经 ISaveModule；未接入则明确降级）
        // ═════════════════════════════════════════════════════════════════════

        private void SaveCurrentCharacter()
        {
            var ctx = Ctx;
            if (ctx?.Save == null)
            {
                Log.Warn(FlowLog.Tag, "保存失败：ISaveModule 未接入（AppContext.Save == null）⇒ 本次未落盘");
                Game.UI.Toast("存档模块未接入，本次未保存");
                return;
            }

            if (_selected == null)
            {
                Log.Warn(FlowLog.Tag, "保存失败：当前没有已选角色");
                Game.UI.Toast("没有可保存的角色");
                return;
            }

            // 先把各模块的运行时状态写回存档对象，再交给 Save 模块落盘。
            ctx.Player?.WriteTo(_selected);
            ctx.Item?.WriteTo(_selected);
            ctx.Quest?.WriteTo(_selected);
            _selected.savedAtTicks = DateTime.UtcNow.Ticks;

            if (ctx.Save.Save())
            {
                Log.Info(FlowLog.Tag, $"已保存角色「{_selected.name}」（槽位 saves/{_selected.name}.json）");
            }
            else
            {
                Log.Error(FlowLog.Tag, $"保存角色「{_selected.name}」失败：{ctx.Save.LastError}");
                Game.UI.Toast("保存失败，见日志");
            }
        }

        // ═════════════════════════════════════════════════════════════════════
        // 清场（回主菜单 / 退出；**不在 Pause 上触发**，见文件头 ①）
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>
        /// 离开舞台：7 项清场（面板 / 实体 / 对象池 / 定时器 / 音效 / 事件订阅 / 模块状态）。
        /// 幂等：不在 Stage 时直接返回。
        /// </summary>
        private void LeaveStage()
        {
            if (!_stageActive)
            {
                Log.Info(FlowLog.Tag, "LeaveStage：当前不在 Stage 站点，跳过清场");
                return;
            }

            _stageActive = false;
            _loading = false;
            _switchingArea = false;

            // ★load：装配账一并作废 —— 下一次 `OnEnterStage` 若发现「装配世代 != 已完成世代」
            //   就会补做完整装配（清场之后世界是空的，必须重装配）。
            _builtEpoch = -1;
            _buildNext = BuildStepCount;
            _sceneReady = false;
            _closeNextTick = false;

            var before = CountEntities();

            // ① 面板（HUD 与所有游戏内面板）
            Game.UI?.CloseAll();

            // ② 实体与视图
            Game.Entity?.ClearAll();

            // ③ 对象池
            Game.Pool?.ClearAll();

            // ④ 定时器（舞台内统一 scope）
            Game.Timer?.StopScope(FlowConst.StageScope);

            // ⑤ 音效 / BGM
            Game.Sound?.StopAll();

            // ⑥ 事件订阅（**同一方法引用**；Flow 常驻，只有舞台级订阅需要注销）
            Game.Event.Off<AreaId>(Events.ExitEntered, OnExitEntered);
            Game.Event.Off(Events.MapAreaReady, OnMapAreaReady);      // ★ travel-black：同一方法引用注销
            _arrivalTo = -1;                                          // ★ travel-black：待落位状态一并作废
            _arrivalFrom = -1;
            _arrivalTimedOutLogged = false;

            // ⑦ 模块状态复位（第二次进图必须是干净的）
            ResetModules();

            _selected = null;
            _area = AreaId.Town;

            Log.Info(FlowLog.Tag,
                $"清场完成：实体 {before} → {CountEntities()}，对象池/scope \"{FlowConst.StageScope}\" 定时器/音效/订阅 已清");

            Game.Event.Emit(Events.StageLeft);
        }

        private static void ResetModules()
        {
            var ctx = Ctx;
            if (ctx == null)
            {
                FlowLog.WarnOnce("ctx.null.reset", "AppContext.I 为 null ⇒ 模块复位跳过");
                return;
            }

            if (ctx.Map == null) FlowLog.Missing("IMapModule"); else ctx.Map.Clear();
            if (ctx.Player == null) FlowLog.Missing("IPlayerModule"); else ctx.Player.Reset();
            if (ctx.Monster == null) FlowLog.Missing("IMonsterModule"); else ctx.Monster.DespawnAll();
            if (ctx.Combat == null) FlowLog.Missing("ICombatModule"); else ctx.Combat.Reset();
            if (ctx.Item == null) FlowLog.Missing("IItemModule"); else ctx.Item.Reset();
            if (ctx.Quest == null) FlowLog.Missing("IQuestModule"); else ctx.Quest.Reset();
            if (ctx.Npc == null) FlowLog.Missing("INpcModule"); else ctx.Npc.Reset();
            if (ctx.View == null) FlowLog.Missing("IViewModule"); else ctx.View.Clear();
            if (ctx.Camera == null) FlowLog.Missing("ICameraRig"); else ctx.Camera.Reset();
            if (ctx.Audio == null) FlowLog.Missing("IAudioModule"); else ctx.Audio.Reset();
            // ISkillModule 无 Reset()：技能状态在下次选角时经 ResetForClass(cls, save) 重建。
        }

        private static int CountEntities()
        {
            if (Game.Entity == null) return -1;
            var n = 0;
            foreach (var _ in Game.Entity.GetAll()) n++;
            return n;
        }

        /// <summary>存档里的 areaId → `AreaId`（越界视为数据损坏，回城镇并打日志）。</summary>
        private static AreaId ToArea(int areaId)
        {
            if (areaId < (int)AreaId.Town || areaId > (int)AreaId.DenOfEvil)
            {
                Log.Warn(FlowLog.Tag, $"存档 areaId={areaId} 越界（合法 {AreaId.Town}..{AreaId.DenOfEvil}）⇒ 回城镇");
                return AreaId.Town;
            }
            return (AreaId)areaId;
        }
    }
}
