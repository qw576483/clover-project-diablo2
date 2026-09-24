// ─────────────────────────────────────────────────────────────────────────────
//
// 为什么需要它（补的是哪条断链，逐条）：
//   已有离线判据只覆盖了传送点的**两端**：
//     · `uicheck ①-b`       —— `WaypointPanel.PlanDests` 纯函数 + 接线文本守卫 + 预制体在盘（面板侧）；
//     ① `App/AppWaypoint.RestoreVisited` / `SnapshotVisited` / `ResetStaticForNewPlaySession`
//        —— 读档回灌 / 存盘快照 / 新局清空，三者的**行为**从未被断言过（只有 AppWiring 的一行文本检索）；
//     ② 点锚点 ⇒ 走过去 ⇒ **开面板** 这条链路（`OnMoveCommand` / `OnPlayerGridChanged` / `OpenPanel`）；
//     ③ 选目的地 ⇒ **切区** 的四个拒绝分支 + 一个放行分支（`OnTravelRequest`）。
//   本条不是"再抄一遍源码"：本文件**真跑**生产类（`AppWaypoint` 与 `WaypointPanel` 由 csproj 直接编进来，
//      与 `Assets/Scripts` 同一份文件），断言的是**行为**（返回值 / 事件载荷 / 日志），不是文本匹配。
//
//     若把退化样本喂进同一条判据，它**必须变红** —— 下面的 `D*` 就是这一步的实测留痕。
//       ㈠ `ConsoleEventBus` 与真引擎同口径（同优先级 = **后注册先执行**，见 shim 头注）；
//       ㈡ 本文件**先**在"未 Install"的总线上跑一遍同一条链（= `33f75bb5` 修前的形状），
//          再 Install 后跑第二遍 ⇒ 两遍的差值就是"接线"这一个变量。
//
//   **离线可判**的 —— R1「锚点无渲染写入方」（视图层 0 消费者 + 素材不在盘 + 缺口登记，三者自洽）
//   与 R3「驱动同帧两次 `ScreenCapture`」。R3 **判两半**：① **文本级** = 面板截图与发传送请求之间
//   必须有帧界（`PanelShotYieldsFrame`）；② **数据级** = `SHOT …_1_panel.png frame=N` ⇒
//   `SHOT …_2_landed.png frame=M` 必须 `M ≥ N + 2`（`ShotFrameGapOk`，坏样本 = **真实旧批次**
//   `u32play` 的 `N = M = 192`）。其余（进营地肉眼找传送台 / 落地黑窗 0.428s / 面板图重采）
//   **只能上机**，不在这里假装判过。
//
//   `AppWaypoint` 只经 `AppWiring.Ctx`（= `AppContext.I`）与 `Game.Event` / `Game.UI` 取世界；
//   离线没有 Unity 场景 ⇒ 桩是**唯一**能让这些分支真跑的载体。
//   桩**不镜像**生产的判定逻辑（不复制任何 if/阈值）—— 它只提供"地图长什么样"这一件事，
//      判定全部落在被验证的生产类里。
//
// 桩坐标刻意用一个**明显是桩**的值（4,7），**不是**原版锚点：原版锚点 (31,26) 的出处与断言
// ─────────────────────────────────────────────────────────────────────────────

using System;
using System.Collections.Generic;
using System.IO;
using CloverEngine;
using Diablo2.App;
using Diablo2.Core;
using Diablo2.Def;
using Diablo2.Module;
using Diablo2.UI;
using UnityEngine;

namespace Uicheck
{
    /// <summary>传送点**链路中段**的离线行为断言（见文件头）。</summary>
    internal static class WaypointFlowCheck
    {
        private static readonly Vector2Int WpStub = new Vector2Int(4, 7);

        private static void Check(string what, bool ok, string detail) => Program.Check(what, ok, detail);

        /// <summary>入口（由 `Program.CheckWaypoint` 调；只加断言，不动既有步骤）。</summary>
        public static void Run()
        {
            Console.WriteLine("── ①-c 传送点·链路中段（交互 ⇒ 面板 ⇒ 切区 / 列表回灌与快照）──");

            // ① 清干净的状态 + 一条**空**的总线（下面所有订阅都建在它上面 ⇒ 测试间不串味）
            AppWaypoint.ResetStaticForNewPlaySession();
            Game.Event = new ConsoleEventBus();
            var map = new StubMap { Area = AreaId.Town, IsGenerated = true, WalkableCell = WpStub };
            map.WaypointCells.Add(WpStub);
            // 必须写成 `Diablo2.App.AppContext`：本文件有 `using System;` ⇒ 裸 `AppContext`
            //    会与 `System.AppContext` 二义（CS0104，实测撞过）。
            var ctx = Diablo2.App.AppContext.Create();
            ctx.Map = map;
            ctx.Player = null;                       // 本文件不测"玩家已在相邻格直接开"那条分支（需要 IPlayerModule 桩，见 §未决）
            var ui = new FakeUI();
            Game.UI = ui;

            try
            {
                CheckListRestore();      // ② 已激活列表：回灌 / 快照 / 新局清空
                CheckPanelOpen(ctx);     // ③ 点锚点 ⇒ 走过去 ⇒ 开面板
                CheckTravelGate();       // ④ 选目的地 ⇒（拒绝 4 种 / 放行 1 种）
                CheckPresentationResiduals();   // ⑤ 表现线残余里**离线可判**的那两条（R1 / R3）
            }
            finally
            {
                // 收尾：把静态状态与三个门面复位，不让本文件影响后续检查
                AppWaypoint.ResetStaticForNewPlaySession();
                Diablo2.App.AppContext.ResetStaticForNewPlaySession();
                Game.UI = null;
                Game.Event = new ConsoleEventBus();
            }

            Console.WriteLine();
        }

        // ═════════════════════════════════════════════════════════════════════
        // ② 已激活列表（`RestoreVisited` / `SnapshotVisited` / 新局清空）
        //    缺口（修前）：`Visited` 是进程内 static、**从不落盘**、也没有"读档回灌"这一步
        //    ⇒ 读档后传送面板永远只认当前区域（用户看到的「尚未啟動其他傳送點」）。
        // ═════════════════════════════════════════════════════════════════════
        private static void CheckListRestore()
        {
            Program._logger.Clear();

            // ── 正向：读档回灌（存档里记过的两个区域）──────────────────────────
            var added = AppWaypoint.RestoreVisited(new List<int> { (int)AreaId.Town, (int)AreaId.BloodMoor });
            var snap = AppWaypoint.SnapshotVisited();
            Check("回灌：RestoreVisited(存档 [营地,血腥荒野]) ⇒ 本次新增 2 个",
                added == 2, $"added={added}");
            Check("回灌：快照 == [营地,血腥荒野]（升序，与存档字段同口径）",
                snap.Count == 2 && snap[0] == (int)AreaId.Town && snap[1] == (int)AreaId.BloodMoor,
                "[" + string.Join(",", snap) + "]");

            // ── 正向：回灌之后，面板列表才有"别的区域" ────────────────────────
            var destsAfter = WaypointPanel.PlanDests(AppWaypoint.SnapshotVisited(), (int)mapArea());
            Check("回灌后开面板：PlanDests(回灌后的列表, 当前=营地) ⇒ 恰 1 条 = 血腥荒野",
                destsAfter.Count == 1 && destsAfter[0].area == (int)AreaId.BloodMoor,
                $"count={destsAfter.Count}");

            var destsNoRestore = WaypointPanel.PlanDests(new List<int> { (int)AreaId.Town }, (int)mapArea());
            Check("★ 退化（D1）：不回灌（旧缺陷形状）⇒ 同一判据给 0 条 ⇒ 上一条不是恒真",
                destsNoRestore.Count == 0, $"count={destsNoRestore.Count}");

            // ── 幂等：同一次回灌跑两遍 ⇒ 第二遍新增 0（并入语义）─────────────
            var addedAgain = AppWaypoint.RestoreVisited(new List<int> { (int)AreaId.Town, (int)AreaId.BloodMoor });
            var snap2 = AppWaypoint.SnapshotVisited();
            Check("回灌幂等：同一份存档再灌一次 ⇒ 新增 0 且集合不变（并入，不重复）",
                addedAgain == 0 && snap2.Count == 2, $"added={addedAgain} count={snap2.Count}");

            // ── 旧档兼容：null / 空集合 ⇒ 什么都不做、**不报错**（旧档没这两个字段是正常情形）──
            Program._logger.Clear();
            var addNull = AppWaypoint.RestoreVisited(null);
            var addEmpty = AppWaypoint.RestoreVisited(new List<int>());
            Check("旧档兼容：RestoreVisited(null) / 空集合 ⇒ 新增 0、不抛、**不打 Warn**（正常情形）",
                addNull == 0 && addEmpty == 0 && !Program._logger.Has("WARN", "App", "回灌"),
                $"null→{addNull} empty→{addEmpty} warn={Program._logger.Has("WARN", "App", "回灌")}");

            // ── 坏值：表外的区域号 ⇒ 跳过 + Warn（不把坏值塞进集合，否则面板会列出非法目的地）──
            Program._logger.Clear();
            var addBad = AppWaypoint.RestoreVisited(new List<int> { 999 });
            var snapBad = AppWaypoint.SnapshotVisited();
            Check("坏档：RestoreVisited([999]) ⇒ 新增 0、集合不变、且**点名 Warn**（不静默）",
                addBad == 0 && snapBad.Count == 2 && Program._logger.Has("WARN", "App", "登记表"),
                $"added={addBad} count={snapBad.Count} warn={Program._logger.Has("WARN", "App", "登记表")}");

            // ── 快照是**只读副本**：调用方改它不许影响内部集合（防"快照泄漏"）──
            var s = AppWaypoint.SnapshotVisited();
            s.Add(999);
            var afterMutate = AppWaypoint.SnapshotVisited();
            Check("SnapshotVisited 返回新列表：外部改它 ⇒ 内部集合不变（无泄漏）",
                afterMutate.Count == 2, $"count={afterMutate.Count}");

            // ── 新局复位（**内存全空对照**）────────────────────────────────────
            //   正向前置：先证明"复位前非空"（否则下面的 0 可能是恒真）。
            AppWaypoint.ResetStaticForNewPlaySession();
            var afterReset = AppWaypoint.SnapshotVisited();
            Check("★ 新局复位：ResetStaticForNewPlaySession ⇒ 已激活列表 = 0 个（第二局不带上一局的已激活集）",
                afterReset.Count == 0, $"count={afterReset.Count}（复位前 = 2，见上一条）");
            Check("★ 退化（D2）：复位前该集合**确实非空**（2 个）⇒ 上面那条 0 不是恒真",
                snapBad.Count == 2, $"复位前 count={snapBad.Count}");
        }

        private static AreaId mapArea()
            => AppWiring.Ctx != null && AppWiring.Ctx.Map != null ? AppWiring.Ctx.Map.Area : AreaId.Town;

        // ═════════════════════════════════════════════════════════════════════
        // ③ 点锚点 ⇒ 走过去 ⇒ 开面板
        //    本组的关键设计 = **同一段代码在"未接线"与"已接线"下各跑一遍**，
        //       两遍的唯一变量就是 `Install` ⇒ 第二遍的"开"一定是接线带来的。
        // ═════════════════════════════════════════════════════════════════════
        private static void CheckPanelOpen(Diablo2.App.AppContext ctx)
        {
            var ui = (FakeUI)Game.UI;

            // ── 退化样本（D3）= 修前形状：未 Install ⇒ 点锚点 + 走到相邻格 ⇒ 0 次打开 ──
            ui.Reset();
            Game.Event.Emit(Events.MoveCommand, WpStub);
            Game.Event.Emit(Events.PlayerGridChanged, new Vector2Int(WpStub.x - 1, WpStub.y));
            Check("★ 退化（D3，= 33f75bb5 修前形状）：`Install` **未调**时点锚点并走到相邻格 ⇒ 面板 0 次打开" +
                  "（类在、编译过、日志干净 —— 这就是当年那条静默失效）",
                ui.OpenCount == 0, $"open={ui.OpenCount}");

            // ── 接线（之后同一段序列必须开出面板）────────────────────────────
            AppWaypoint.Install(ctx);
            // 回灌两个区域，好让面板参数里**确实有目的地**（否则只验到"开了"、验不到"参数对不对"）
            AppWaypoint.RestoreVisited(new List<int> { (int)AreaId.Town, (int)AreaId.BloodMoor });

            ui.Reset();
            Program._logger.Clear();
            Game.Event.Emit(Events.MoveCommand, WpStub);
            Program._logger.Clear();
            Game.Event.Emit(Events.PlayerGridChanged, new Vector2Int(WpStub.x - 1, WpStub.y));   // 8 邻
            var args = ui.OpenedParam as WaypointArgs;
            Check("点锚点 ⇒ 走到相邻格 ⇒ 打开 `WaypointPanel` 恰 1 次（接线后）",
                ui.OpenCount == 1 && ui.OpenedType == typeof(WaypointPanel),
                $"open={ui.OpenCount} type={ui.OpenedType?.Name ?? "-"}");
            Check("面板打开参数 = `WaypointArgs`，且目的地 == PlanDests(已激活, 当前区域)（恰 1 条 = 血腥荒野）",
                args != null && args.dests != null && args.dests.Count == 1
                && args.dests[0].area == (int)AreaId.BloodMoor,
                args == null ? "(param 不是 WaypointArgs)" : $"dests={args.dests.Count}");
            Check("走到锚点附近 ⇒ 留痕一条 Info（不是静默；非预期分支才用 Warn）",
                Program._logger.Has("INFO", "App", "打开传送面板"),
                "(App tag 的 Info 留痕)");

            // ── 退化（D4）：距离 > 1 ⇒ **不许**开（证明 8 邻门槛不是摆设）──────
            //   这里**不许**再调 `AppWaypoint.Install`：它没有幂等守卫，重复调 = 同一事件
            //      收到两份回调 ⇒ 面板会被开两次（那会让下面的 `== 1` 变成假红）。接线只做一次。
            ui.Reset();
            Game.Event.Emit(Events.MoveCommand, WpStub);
            Game.Event.Emit(Events.PlayerGridChanged, new Vector2Int(WpStub.x, WpStub.y + 3));   // Chebyshev = 3
            Check("★ 退化（D4）：距锚点 3 格 ⇒ 面板 0 次打开（8 邻门槛有效；⛔ 不是「走到哪都开」）",
                ui.OpenCount == 0, $"open={ui.OpenCount}");

            // ── 退化（D5）：点的**不是**锚点格 ⇒ 即使距离 0 也不开 ────────────
            ui.Reset();
            Game.Event.Emit(Events.MoveCommand, new Vector2Int(1, 1));     // 非锚点格
            Game.Event.Emit(Events.PlayerGridChanged, new Vector2Int(1, 1));  // 距离 0
            Check("★ 退化（D5）：点非锚点格（距离 0）⇒ 面板 0 次打开（证明 D3/D4 依赖的是**锚点格**判定）",
                ui.OpenCount == 0, $"open={ui.OpenCount}");

            // ── 退化（D6）：地图未生成 ⇒ 交互整体不启动 ──────────────────────
            ui.Reset();
            var map = (StubMap)ctx.Map;
            map.IsGenerated = false;
            Game.Event.Emit(Events.MoveCommand, WpStub);
            map.IsGenerated = true;
            Game.Event.Emit(Events.PlayerGridChanged, new Vector2Int(WpStub.x - 1, WpStub.y));
            Check("★ 退化（D6）：地图未生成时点锚点 ⇒ 之后走到相邻格也 0 次打开（不拿未生成的地图开面板）",
                ui.OpenCount == 0, $"open={ui.OpenCount}");

            //    且**后续走到相邻格也不许"补开"**（证明放弃真的落地，而不是延后触发）────
            ui.Reset();
            Program._logger.Clear();
            map.WalkableCell = new Vector2Int(-1, -1);      // 锚点变成不可走
            Game.Event.Emit(Events.MoveCommand, WpStub);
            Game.Event.Emit(Events.PlayerGridChanged, new Vector2Int(WpStub.x, WpStub.y + 3));   // 距离 3 ⇒ 走到"不可走"分支
            var warned = Program._logger.Has("WARN", "App", "不可走");
            Game.Event.Emit(Events.PlayerGridChanged, new Vector2Int(WpStub.x - 1, WpStub.y));   // 距离 1
            Check("★ 退化（D7）：锚点不可走 ⇒ 本次交互放弃并 Warn（不静默），且之后走到相邻格**也不**开面板",
                warned && ui.OpenCount == 0, $"warn={warned} open={ui.OpenCount}");
            map.WalkableCell = WpStub;                      // 复原

            // ── 正向：玩家**走到锚点格本身**（锚点可走，正常会走上去）⇒ 也开 ──
            ui.Reset();
            Game.Event.Emit(Events.MoveCommand, WpStub);
            Game.Event.Emit(Events.PlayerGridChanged, WpStub);   // 距离 0
            Check("点锚点 ⇒ 走到锚点格**本身**（距离 0）⇒ 同样打开面板（8 邻含同格）",
                ui.OpenCount == 1, $"open={ui.OpenCount}");
        }

        // ═════════════════════════════════════════════════════════════════════
        // ④ 选目的地 ⇒ 切区（`OnTravelRequest` 的四个拒绝分支 + 一个放行分支）
        //    放行 = 发 `Events.ExitEntered`（载荷 = `AreaId`）—— 切区域的**唯一**链路，
        //    因此"落地区域 id 正确"在本层就是**载荷逐值相等**。
        // ═════════════════════════════════════════════════════════════════════
        private static void CheckTravelGate()
        {
            var map = (StubMap)AppWiring.Ctx.Map;
            var ui = (FakeUI)Game.UI;

            var exitCount = 0;
            var exitPayload = new List<AreaId>();
            Action<AreaId> onExit = a => { exitCount++; exitPayload.Add(a); };
            Game.Event.On<AreaId>(Events.ExitEntered, onExit);

            try
            {
                // 每一条都从同一个已知状态出发：已激活 = {营地, 血腥荒野}，当前区域 = 营地
                Action reset = () =>
                {
                    AppWaypoint.ResetStaticForNewPlaySession();
                    AppWaypoint.RestoreVisited(new List<int> { (int)AreaId.Town, (int)AreaId.BloodMoor });
                    map.Area = AreaId.Town;
                    map.IsGenerated = true;
                    exitCount = 0;
                    exitPayload.Clear();
                    ui.Reset();
                };

                // ── 退化（D8）：**未激活**的目的地 ⇒ 拒绝（0 次 ExitEntered）+ 点名 Warn ──
                reset();
                AppWaypoint.ResetStaticForNewPlaySession();          // 清掉已激活列表 = "该区域还没去过"
                Program._logger.Clear();
                Game.Event.Emit(Events.WaypointTravelRequest, (int)AreaId.BloodMoor);
                Check("★ 退化（D8）：目的地**未激活** ⇒ 0 次 ExitEntered + Warn（未激活不下发切区）",
                    exitCount == 0 && Program._logger.Has("WARN", "App", "未激活"),
                    $"exit={exitCount} warn={Program._logger.Has("WARN", "App", "未激活")}");

                // ── 正向：**已激活**且 ≠ 当前区域 ⇒ 放行一次，载荷 = 该区域（落地区域 id 正确）──
                reset();
                Program._logger.Clear();
                Game.Event.Emit(Events.WaypointTravelRequest, (int)AreaId.BloodMoor);
                Check("★ 已激活可跨区：目的地=血腥荒野（已激活，≠ 当前营地）⇒ 恰 1 次 ExitEntered，" +
                      "载荷逐值 == 血色荒野（AreaId.BloodMoor）⇒ 落地区域 id 正确",
                    exitCount == 1 && exitPayload.Count == 1 && exitPayload[0] == AreaId.BloodMoor,
                    $"exit={exitCount} payload=[{string.Join(",", exitPayload)}]");
                Check("放行时先关面板（`Game.UI.Close<WaypointPanel>()`）⇒ 不留「面板挂在旧图上」",
                    ui.CloseCount == 1 && ui.ClosedType == typeof(WaypointPanel),
                    $"close={ui.CloseCount} type={ui.ClosedType?.Name ?? "-"}");

                // ── 退化（D9）：目标 == 当前区域 ⇒ 拒绝 ────────────────────────
                reset();
                Program._logger.Clear();
                Game.Event.Emit(Events.WaypointTravelRequest, (int)AreaId.Town);
                Check("★ 退化（D9）：目标 = 当前区域 ⇒ 0 次 ExitEntered + Warn（原地传送无意义）",
                    exitCount == 0 && Program._logger.Has("WARN", "App", "当前区域"),
                    $"exit={exitCount} warn={Program._logger.Has("WARN", "App", "当前区域")}");

                // ── 退化（D10）：表外的区域号 ⇒ 拒绝 ──────────────────────────
                reset();
                Program._logger.Clear();
                Game.Event.Emit(Events.WaypointTravelRequest, 999);
                Check("★ 退化（D10）：区域号 999（表外）⇒ 0 次 ExitEntered + Warn（非法载荷不下发切区）",
                    exitCount == 0 && Program._logger.Has("WARN", "App", "999"),
                    $"exit={exitCount} warn={Program._logger.Has("WARN", "App", "999")}");

                // ── 退化（D11）：地图未生成 ⇒ 拒绝（不拿未生成的地图切区）──────
                reset();
                map.IsGenerated = false;
                Program._logger.Clear();
                Game.Event.Emit(Events.WaypointTravelRequest, (int)AreaId.BloodMoor);
                map.IsGenerated = true;
                Check("★ 退化（D11）：地图未生成 ⇒ 0 次 ExitEntered + Warn（不许在空地图上切区）",
                    exitCount == 0 && Program._logger.Has("WARN", "App", "地图未生成"),
                    $"exit={exitCount} warn={Program._logger.Has("WARN", "App", "地图未生成")}");

                // ── 反向：放行分支**也必须**能红 —— 把"已激活"这一条拿掉（D8）就红，
                //    把"≠当前区域"拿掉（D9）就红 ⇒ D8/D9/D10/D11 与上面那条正向是**互证**的。
            }
            finally
            {
                // 只摘掉本文件自己加的那个捕获器（`AppWaypoint` 的订阅在 `Run()` 的 finally 里
                // 随整条总线一起作废 —— 那时换的是**新的空总线**，不去动别人的订阅）
                Game.Event.Off<AreaId>(Events.ExitEntered, onExit);
            }
        }

        // ═════════════════════════════════════════════════════════════════════
        //
        //   台账 `U32` 状态 = 「**部分**（功能线闭环；**表现线残余见右**）」。三条残余逐条归类，
        //   不把"只能上机看"的东西硬塞成离线断言：
        //     · R1 锚点看不出是传送台（`WP-ART-1`）—— 拆成**两个半边**：
        //       ① 【外部素材】原版世界内传送台美术（`data/global/objects/wp/*`）**不在盘**，
        //       ② 【功能/代码 · 离线可判】视图层**没有任何消费者**写 `IMapModule.WaypointPoints`
        //          ⇒ 即使素材到齐也**无处可画**（台账点名的「`MapView.cs` 对 Waypoint 0 命中」就是它）。
        //          本组判②：把「缺口 ↔ 登记」做成**自洽闸门** + 一条素材到齐当天自动生效的判据。
        //     · R2 换区落地黑窗 0.428s —— **只能上机**（逐帧覆盖率 + 帧计时；`travelblack` 驱动在采）。
        //         （后一张顶掉前一张），`drivers/d2u32_drive.cs` 已修 ⇒ 本组判「让帧还在」（防复发）。
        //
        //   判据形态（同上：判过程 + 每条必须有能变红的退化样本）：
        //     · R1 闸门 = 纯函数 `WpArtResidualConsistent(素材, 写入方数, 登记)`，三个分支各有代价 ⇒
        //       真输入绿、三条退化样本（D12/D13/D14）各自单独红；
        //     · 扫描器配**注入式直证**（`README` 第 33 条：作用在整份文件文本 + 注入真文本必须转红）
        //       与一条"注释里的同串不算"的正例（剥注释走 `LayoutGameCheck.StripCsComments`，本宿主唯一实现）；
        //     · R3 守卫判的是「两个**真调用点**之间有没有帧界」，不是"两常量行号比大小"。
        // ═════════════════════════════════════════════════════════════════════
        private static void CheckPresentationResiduals()
        {
            Console.WriteLine("── ①-d 表现线残余里离线可判的两条（R1 锚点无渲染写入方 / R3 驱动同帧双拍）──");

            // ── R1-b 读数：读的**路径**逐条写明（`README` 第 35 条）——
            //   视图层 = `Scripts/Module/View/**/*.cs` + `Scripts/Module/Map/MapView.cs`；
            //   不含 `UI/` 与 `App/`：那些消费者是"面板/交互"，不是"把锚点画出来"。
            var viewDir = Path.Combine(Program.ProjectRoot,
                "client", "Assets", "Scripts", "Module", "View");
            var mapViewFile = Path.Combine(Program.ProjectRoot,
                "client", "Assets", "Scripts", "Module", "Map", "MapView.cs");
            var viewFiles = new List<string>();
            if (Directory.Exists(viewDir))
                viewFiles.AddRange(Directory.GetFiles(viewDir, "*.cs", SearchOption.AllDirectories));
            if (File.Exists(mapViewFile)) viewFiles.Add(mapViewFile);

            var viewReaders = 0;
            for (var i = 0; i < viewFiles.Count; i++)
                viewReaders += CountNeedle(File.ReadAllText(viewFiles[i]), "WaypointPoints");

            // 素材"在位" = 目录里 ≥ 1 个**非 `.meta`** 文件（`.meta` 是 Unity 自己产的，不算素材）
            var artDir = Path.Combine(Program.ResourceRoot, "Clover", "D2", "Objects", "waypoint");
            var artPresent = false;
            if (Directory.Exists(artDir))
            {
                var artFiles = Directory.GetFiles(artDir, "*", SearchOption.AllDirectories);
                for (var i = 0; i < artFiles.Length; i++)
                {
                    if (!artFiles[i].EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                    { artPresent = true; break; }
                }
            }

            // 登记：`client/资源欠缺清单.md` 里那条 `WP-ART-1` 行必须仍标 `BLOCKED`
            var registryFile = Path.Combine(Program.ProjectRoot, "client", "资源欠缺清单.md");
            var registrySrc = File.Exists(registryFile) ? File.ReadAllText(registryFile) : string.Empty;
            var registered = registrySrc.Contains("WP-ART-1") && registrySrc.Contains("BLOCKED");

            var readings = "素材在位=" + artPresent + " 视图层消费者=" + viewReaders
                + " 登记=" + registered + "（扫了 " + viewFiles.Count + " 个视图层文件：Module/View/** + MapView.cs）";

            // ① 自洽闸门（**判过程**）：缺口与登记互相钉住，三个分支各有代价 ——
            //    · 素材到位却没人画 ⇒ 红（这就是"给了图却无处可画"，本残余最贵的形态，且它**静默**）；
            //    · 素材没到却接了渲染写入方 ⇒ 红（那画的一定不是原版图 = 自创贴图，铁律 1/3）；
            //    · 素材没到且没人画 ⇒ **只要登记还在**就自洽（= 现在的形状）。
            Check("R1 自洽闸门（真输入：素材不在盘 + 视图层 0 写入方 + 登记在）⇒ 绿",
                WpArtResidualConsistent(artPresent, viewReaders, registered), readings);
            Check("★ 退化（D12）：素材到齐却没人画（true/0）⇒ 必须红（防「给了图但无处可画」）",
                !WpArtResidualConsistent(true, 0, true), "这是本残余最贵的失败形态，而且它**静默**");
            Check("★ 退化（D13）：素材没到却接了渲染写入方（false/1）⇒ 必须红（那画的一定不是原版图）",
                !WpArtResidualConsistent(false, 1, true), "铁律 1/3：写不出出处的图/量不许进工程");
            Check("★ 退化（D14）：素材没到、没人画、**登记被删**（false/0/false）⇒ 必须红（缺口不许被忘掉）",
                !WpArtResidualConsistent(false, 0, false), "那条 `WP-ART-1`/BLOCKED 登记行就是这条的锚点");
            Check("★ 正例：素材到位 + 有人画（true/1）⇒ 绿（闸门不是永假）",
                WpArtResidualConsistent(true, 1, false), "闭合后的形状（将来素材到齐时走这条）");

            // ② 扫描器直证（`README` 第 33 条：作用在**整份文件文本**上 + 配一条"注入到真文件文本"的直证）：
            //    把 needle 注入 `MapView.cs` 的**真文本**，同一个扫描必须从 0 处变 1 处。
            var mapViewReal = File.Exists(mapViewFile) ? File.ReadAllText(mapViewFile) : string.Empty;
            var realHits = CountNeedle(mapViewReal, "WaypointPoints");
            var injectedHits = CountNeedle(mapViewReal
                + "\ninternal static class Zz { static void Yy() { var w = Ctx.Map.WaypointPoints; } }\n",
                "WaypointPoints");
            Check("★ 扫描器直证：`MapView.cs` 真文本 0 处；同一文本追加一行读 `WaypointPoints` ⇒ 必须报 1 处",
                mapViewReal.Length > 0 && realHits == 0 && injectedHits == 1,
                "真文本 " + realHits + " 处 ⇒ 注入后 " + injectedHits + " 处（证明扫描不是恒 0、也不是恒真）");

            Check("★ 正例片段：最小片段 1 处 ⇒ 不是永 0；且**注释里的同串算 0**（剥注释后）",
                CountNeedle("void X(){ var w = m.WaypointPoints; }", "WaypointPoints") == 1
                && CountNeedle("// m.WaypointPoints\nvoid X(){ }", "WaypointPoints") == 0,
                "needle 走 LayoutGameCheck.StripCsComments（本宿主唯一的剥注释实现）");

            // ── R3：驱动"同帧双拍"守卫（`tools/probes/drivers/d2u32_drive.cs`）────────────
            //   `WaypointTravelRequest` ⇒ 同帧两次 `ScreenCapture`，而它只在**帧末**写一次
            //   判据 = 「面板那张截图」与「发传送请求」两个**真调用点**之间必须有帧界
            //   （`yield return null;`）—— 不是行号比大小，作用在整份（剥注释后的）文件文本上。
            var drvFile = Path.Combine(Program.ProjectRoot,
                "tools", "probes", "drivers", "d2u32_drive.cs");
            var drvSrc = File.Exists(drvFile)
                ? LayoutGameCheck.StripCsComments(File.ReadAllText(drvFile))
                : string.Empty;
            Check("R3 守卫：`d2u32_drive.cs` 在「面板截图」与「发传送请求」之间让出帧（≥1 处 `yield return null;`）",
                drvSrc.Length > 0 && PanelShotYieldsFrame(drvSrc),
                File.Exists(drvFile) ? "在 drivers/d2u32_drive.cs 内检索（剥注释）" : "缺 " + drvFile);

            // 已知错样本 = **在真文件文本上**把那段窗口里的让帧全删掉（注入式，不写盘）
            var brokenDrv = DropPanelShotYields(drvSrc);
            var positiveDrv = "Shot(\"a_1_panel.png\"); yield return null;"
                + " Emit(Events.WaypointTravelRequest, 1);";
            Check("★ 退化（D15）：真文本里把「面板截图 ⇒ 传送请求」之间的让帧全删掉 ⇒ 必须红（= 首跑那个形状）",
                drvSrc.Length > 0 && PanelShotYieldsFrame(drvSrc)
                && brokenDrv != drvSrc && !PanelShotYieldsFrame(brokenDrv),
                "真文本 绿 / 删掉窗口内让帧后 " + PanelShotYieldsFrame(brokenDrv));
            Check("★ 正例片段：shot ⇒ yield ⇒ travel 的最小片段 ⇒ 绿（守卫不是永假）",
                PanelShotYieldsFrame(positiveDrv), "三事件按正确顺序的最小正例");

            //   上面那条是**文本级**补充（判源码里有没有让帧）；本条判**数据级结果**：
            //   机制 = `ScreenCapture.CaptureScreenshot` 只在**帧末**写一次 ⇒ 同帧两次只留**后**一张
            //   （当年 `_1_panel.png` 因此整张没落盘）。⇒「面板那张」必须**先于**「落地那张」至少 **2 帧**
            //   （= 驱动 `d2u32_drive.cs` 里那两处 `yield return null;`）。
            //   两侧真实读数（都在盘，可原样复核）：
            //     · 坏 = `u32_evidence_u32play.txt`：`_1_panel.png frame=192` 与 `_2_landed.png frame=192`（**同帧**）；
            //     · 好 = `u32_evidence_u32p2.txt` ：`_1_panel.png frame=99`  → `_2_landed.png frame=101`（差 2）。
            Check("★ R3-c 坏样本 = **真实旧批次** u32play（面板/落地同为 frame=192）⇒ 同一判据必须判红",
                !ShotFrameGapOk(192, 192),
                "读数出处 `.ai-tmp/screenshots/u32_evidence_u32play.txt`（当年面板图被落地图顶掉的形状）");
            Check("★ R3-c 好样本 = 真实修后批次 u32p2（frame 99 → 101）⇒ 判绿",
                ShotFrameGapOk(99, 101), "读数出处 `.ai-tmp/screenshots/u32_evidence_u32p2.txt`");
            Check("★ R3-c 边界：差 1 帧（99→100）判红、差 2 帧（99→101）判绿（门槛 = 驱动里那两处让帧）",
                !ShotFrameGapOk(99, 100) && ShotFrameGapOk(99, 101), "边界两侧对立读数");
            // 实盘解析（比常量更硬：证明「帧号」是从真文本解析出来的，不是我抄的）
            // 证据文件在 `.ai-tmp/screenshots/`（**临时件，会被清理**）⇒ 不在位时打 `[SKIP]`、不算失败。
            var evDir = Path.Combine(Program.ProjectRoot, ".ai-tmp", "screenshots");
            var evGood = Path.Combine(evDir, "u32_evidence_u32p2.txt");
            var evBad = Path.Combine(evDir, "u32_evidence_u32play.txt");
            if (File.Exists(evGood) && File.Exists(evBad))
            {
                var goodTxt = File.ReadAllText(evGood);
                var badTxt = File.ReadAllText(evBad);
                var gPanel = FrameOfShot(goodTxt, "_1_panel.png");
                var gLanded = FrameOfShot(goodTxt, "_2_landed.png");
                var bPanel = FrameOfShot(badTxt, "_1_panel.png");
                var bLanded = FrameOfShot(badTxt, "_2_landed.png");
                Check("R3-c 实盘：两个批次的 `SHOT … frame=` 都解析成功（证明上面的读数出自真文本、非手抄）",
                    gPanel >= 0 && gLanded >= 0 && bPanel >= 0 && bLanded >= 0,
                    $"u32p2=({gPanel},{gLanded}) u32play=({bPanel},{bLanded})");
                Check("R3-c 实盘：同一函数在两种**真实**数据上两侧对立（u32p2 绿 / u32play 红）",
                    ShotFrameGapOk(gPanel, gLanded) && !ShotFrameGapOk(bPanel, bLanded),
                    $"u32p2 帧差={gLanded - gPanel} ⇒ 绿；u32play 帧差={bLanded - bPanel} ⇒ 红");
            }
            else
            {
                Console.WriteLine("[SKIP] R3-c 实盘解析：`.ai-tmp/screenshots/u32_evidence_*.txt` 不在位"
                    + "（临时件，随时会被清理）⇒ 本条只跑上面三条纯函数判据");
            }

            Console.WriteLine();
        }

        /// <summary>
        /// R1（`WP-ART-1`）的**自洽闸门**：素材与"有没有人画"必须互相钉住。
        /// <list type="bullet">
        /// <item>素材**在位** ⇒ 必须有渲染写入方（否则 = 「给了图却无处可画」，且静默）。</item>
        /// <item>素材**不在位** ⇒ 不许有渲染写入方（那画的一定不是原版图 = 自创贴图，铁律 1/3）。</item>
        /// <item>素材不在位且没人画 ⇒ **只要缺口仍登记着**就自洽。</item>
        /// </list>
        /// 纯函数（三个入参），故三条退化样本可以各自单独喂进来判红（D12/D13/D14）。
        /// </summary>
        internal static bool WpArtResidualConsistent(bool artPresent, int viewReaders, bool registered)
        {
            if (artPresent) return viewReaders > 0;
            if (viewReaders > 0) return false;
            return registered;
        }

        /// <summary>
        /// 整份文件文本里 <paramref name="needle"/> 的出现次数（**先剥注释**，同
        /// <see cref="LayoutGameCheck.StripCsComments"/>）。
        /// 判据作用在**整份文本**上、不依赖任何行号 ⇒ 将来新增的代码也自动落在判据内。
        /// </summary>
        private static int CountNeedle(string src, string needle)
        {
            var s = LayoutGameCheck.StripCsComments(src);
            var n = 0;
            var i = 0;
            while ((i = s.IndexOf(needle, i, StringComparison.Ordinal)) >= 0) { n++; i += needle.Length; }
            return n;
        }

        /// <summary>
        /// `d2u32_drive.cs`（已剥注释）：「面板那张截图」与「发传送请求」之间**有没有帧界**
        /// （即窗口内至少一处 `yield return null;`）。
        /// </summary>
        internal static bool PanelShotYieldsFrame(string stripped)
        {
            var shot = stripped.IndexOf("_1_panel.png", StringComparison.Ordinal);
            if (shot < 0) return false;
            var travel = stripped.IndexOf("WaypointTravelRequest", shot, StringComparison.Ordinal);
            if (travel < 0) return false;
            var y = stripped.IndexOf("yield return null;", shot, StringComparison.Ordinal);
            return y > shot && y < travel;
        }

        /// <summary>已知错样本用：把窗口内**每一处** `yield return null;` 都删掉（在内存里，不写盘）。</summary>
        private static string DropPanelShotYields(string stripped)
        {
            const string Y = "yield return null;";
            var shot = stripped.IndexOf("_1_panel.png", StringComparison.Ordinal);
            if (shot < 0) return stripped;
            var res = stripped;
            while (true)
            {
                var travel = res.IndexOf("WaypointTravelRequest", shot, StringComparison.Ordinal);
                var y = res.IndexOf(Y, shot, StringComparison.Ordinal);
                if (travel < 0 || y < 0 || y > travel) break;
                res = res.Remove(y, Y.Length);
            }
            return res;
        }

        /// <summary>
        /// R3-c 的数据级判据：**面板那张图**必须先于**落地那张图**至少 **2 帧**
        /// （`ScreenCapture.CaptureScreenshot` 只在帧末写一次 ⇒ 同帧两次只留后一张）。
        /// 入参 = 两次 `SHOT … frame=` 的帧号；任一无值（&lt; 0）一律判红（**不可复核 = 不可绿**）。
        /// </summary>
        internal static bool ShotFrameGapOk(int panelFrame, int landedFrame)
            => panelFrame >= 0 && landedFrame >= panelFrame + 2;

        /// <summary>
        /// 从一次 Play 批次的证据日志里取某张图的帧号（形如
        /// `[time] [Info] [U32] SHOT u32_u32p2_1_panel.png frame=99 note=…`）。
        /// 找不到名字 / 找不到 `frame=` / 后面不是数字 ⇒ **-1**（不返回 0：0 是合法帧号）。
        /// </summary>
        private static int FrameOfShot(string evidenceText, string shotNameTail)
        {
            var i = evidenceText.IndexOf(shotNameTail, StringComparison.Ordinal);
            if (i < 0) return -1;
            i = evidenceText.IndexOf("frame=", i, StringComparison.Ordinal);
            if (i < 0) return -1;
            i += "frame=".Length;
            var j = i;
            while (j < evidenceText.Length && char.IsDigit(evidenceText[j])) j++;
            if (j == i) return -1;
            int v;
            return int.TryParse(evidenceText.Substring(i, j - i), out v) ? v : -1;
        }

        // ═════════════════════════════════════════════════════════════════════
        // 桩：只提供"世界长什么样"，不镜像任何生产判定
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>`IMapModule` 的离线桩（成员签名与 `Module/Contracts.cs` 逐条一致）。</summary>
        private sealed class StubMap : IMapModule
        {
            public AreaId Area { get; set; } = AreaId.Town;
            public bool IsGenerated { get; set; } = true;

            /// <summary>唯一"可走"的那一格（锚点也放这里）。</summary>
            public Vector2Int WalkableCell = new Vector2Int(4, 7);

            /// <summary>锚点格的**可写**容器（契约属性 `WaypointPoints` 是只读投影，见下）。</summary>
            public readonly List<Vector2Int> WaypointCells = new List<Vector2Int>();

            public IReadOnlyList<Vector2Int> WaypointPoints => WaypointCells;

            public int Width => 80;
            public int Height => 80;
            public int Seed => 20250916;
            public int BlockedCount => 0;
            public int WalkableCount => 0;
            public Vector2Int SpawnPoint => new Vector2Int(1, 1);
            public IReadOnlyList<Vector2Int> Exits => new List<Vector2Int>();
            public Vector2Int? CaveEntrance => null;
            public IReadOnlyList<Vector2Int> NpcPoints => new List<Vector2Int>();
            public IReadOnlyList<Vector2Int> MonsterSpawns => new List<Vector2Int>();
            public IReadOnlyCollection<Vector2Int> ExploredCells => new List<Vector2Int>();

            public bool InBounds(Vector2Int g) => true;
            public bool Walkable(Vector2Int g) => g == WalkableCell;
            public bool IsDeckGrid(Vector2Int g) => false;
            public TileKind TileAt(Vector2Int g) => TileKind.Grass;
            public void Generate(AreaId area, int seed) { Area = area; IsGenerated = true; }
            public void Clear() { IsGenerated = false; }
            public List<Vector2Int> FindPath(Vector2Int from, Vector2Int to) => new List<Vector2Int> { from, to };
            public Vector2Int RandomWalkableTile(Rng rng) => WalkableCell;
            public void ShowArea(AreaId area) { }
            public MinimapArgs BuildMinimap() => null;
        }

        /// <summary>
        /// `IUIManager` 的离线桩：只记录"谁被开/关过"。
        /// <para>不实例化任何 MonoBehaviour（离线进程建不了 Unity 对象）—— 面板的像素布局由
        /// Play 驱动采（本片不进 Play），本桩只判"开没开、参数是什么"。</para>
        /// </summary>
        private sealed class FakeUI : IUIManager
        {
            public int OpenCount;
            public Type OpenedType;
            public object OpenedParam;
            public int CloseCount;
            public Type ClosedType;

            public void Reset() { OpenCount = 0; OpenedType = null; OpenedParam = null; CloseCount = 0; ClosedType = null; }

            public void Open<T>(object param = null) where T : class, IUIPanel
            {
                OpenCount++; OpenedType = typeof(T); OpenedParam = param;
            }

            public void Close<T>() where T : class, IUIPanel { CloseCount++; ClosedType = typeof(T); }
            public void Close(string panelName) { }
            public void CloseAll() { }
            public T Get<T>() where T : class, IUIPanel => null;
            public bool IsOpen<T>() where T : class, IUIPanel => false;
            public void Toast(string text, float duration = 2f) { }
            public void FloatText(Vector3 worldPos, string text, Color? color = null, float duration = 1.2f) { }
            public void ShowLoading(string text = null) { }
            public void HideLoading() { }
            public bool IsLoading => false;
            public void Confirm(string title, string message, Action onConfirm, Action onCancel = null,
                string confirmText = null, string cancelText = null) { }
            public void Tick(float dt) { }
        }
    }
}
