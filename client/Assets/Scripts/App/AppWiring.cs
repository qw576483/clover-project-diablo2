// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · App/AppWiring.cs      （agent-12「最后一次接线」）
// **App 层的运行时接线入口**：把 agent-04~09 交付的模块真正连成一条能跑的链路。
//
// 为什么只能放在 App 层：`Module/X` 之间不许 `using` 别的模块的具体类型
// （`tools/ai-skill/conventions.md`）⇒「谁在什么时机 emit / 调用谁」必须有一处唯一知道。
//
// 拆成 4 个同级文件（每个 ≤ 200 行，见 `_common.md` §4）：
//   · 本文件           —— 装配入口 + 场景根节点注入 + Stage 进/出（HUD 开关）+ 进图断言
//   · `AppSnapshots.cs`    —— 进图全量快照 + 面板打开前补发        （§3 的第 2、3 项）
//   · `AppDoorGuard.cs`    —— 过门去重断言                        （§3 的第 5 项）
//   · `AppEventRouting.cs` —— UI/输入请求 → 门面方法              （§3 的第 7 项落地）
// ⛔ 本层不含业务逻辑：不判伤害/距离/数值/掉落/任务条件，只做「事件 ↔ 门面」的搬运。
// ─────────────────────────────────────────────────────────────────────────────

using System.Reflection;
using CloverEngine;
using Diablo2.Core;
using Diablo2.Def;
using Diablo2.UI;
using UnityEngine;

namespace Diablo2.App
{
    /// <summary>App 层运行时接线（幂等；只做事件 ↔ 门面的搬运）。</summary>
    internal static class AppWiring
    {
        /// <summary>日志 tag（`Core/Log.cs` 白名单内）。</summary>
        public const string Tag = "App";

        private static bool _installed;
        private static bool _stageActive;

        /// <summary>App 注入的模块注册表（null = Bootstrap 未装配 ⇒ 全部降级并打日志）。</summary>
        public static AppContext Ctx => AppContext.I;

        /// <summary>是否已在 Stage 站点内（`StageEntered` 之后、`StageLeft` 之前）。</summary>
        public static bool StageActive => _stageActive;

        /// <summary>
        /// 新一局 Play 的静态复位（由 `Bootstrap` 的 `[RuntimeInitializeOnLoadMethod(SubsystemRegistration)]` 调）。
        /// 为什么必须：工程若开了「不重载域」（P-3），`_installed` 会残留 ⇒ 第二局本层**一次都不订阅**
        /// ⇒ HUD / 全量快照 / 过门断言全部失效，且**不报错**。
        /// </summary>
        internal static void ResetStaticForNewPlaySession()
        {
            _installed = false;
            _stageActive = false;
        }

        /// <summary>接线（`Bootstrap` 在 `AppContext.AutoWire()` **之后**调一次；重复调用幂等）。</summary>
        public static void Install(AppContext ctx)
        {
            var bus = Game.Event;
            if (bus == null)
            {
                Game.Logger.Error(Tag, "AppWiring.Install：Game.Event 为 null（Game.Launch 未调用？）⇒ 接线未生效");
                return;
            }

            if (_installed)
            {
                Game.Logger.Warn(Tag, "AppWiring.Install 被重复调用 ⇒ 只重新注入根节点（订阅不重复建）");
                AttachRoots(ctx);
                return;
            }
            _installed = true;

            // ★ 订阅顺序说明（agent-05 更正 agent-12 的老注释）：
            //   `Game.Event` 同一优先级是「**后注册先执行**」（`Runtime/Core/Event.cs:141-143` + `:319-343`），
            //   所以下面这行注册的 `AppDoorGuard` 跑到**本类**后面 —— 老注释里"AppDoorGuard 必须先收到
            //   StageEntered"正好是反的，那正是 agent-14 §B 现象 3 的根因（快照回声被误计成重复生成）。
            //   现已改成**与顺序无关**的两条规则：① App 自己的快照回声不计入（`AppSnapshots.EchoInFlight`）；
            //   ② `AppDoorGuard` 改在边界事件（`StageEntered` / `ExitEntered`）上报计数。
            AppDoorGuard.Install(ctx);
            AppSnapshots.Install(ctx);        // 全量快照 + 面板打开前补发
            AppEventRouting.Install(ctx);     // UI/输入请求 → 门面方法
            bus.On(Events.StageEntered, OnStageEntered);
            bus.On(Events.StageLeft, OnStageLeft);
            AttachRoots(ctx);

            Game.Logger.Info(Tag, "App 接线完成：Stage 进/出（HUD + 全量快照）/ 过门去重断言 / 根节点注入 / 请求转发");
        }

        /// <summary>
        /// 把 `Stage` 场景的「地图根 / 实体根」交给两个模块（**幂等**：`StageRoots` 注入后再调一次即可）。
        /// ⚠️ 用**反射**找 `AttachRoot(Transform)` 而不是 `is MapModule`：它是**非契约**入口，
        /// 直接写具体类型会让 App 层编译期依赖 `Module/Map` 与 `Module/View`，
        /// 而 `tools/flowcheck` / `tools/uicheck` 刻意只编子集 ⇒ 会把别人的半成品算成它们的编译失败。
        /// </summary>
        public static void AttachRoots(AppContext ctx)
        {
            if (ctx == null)
            {
                Game.Logger.Error(Tag, "AttachRoots：AppContext 为 null ⇒ 根节点未注入（模块将自建根）");
                return;
            }

            if (ctx.MapRoot == null)
            {
                Game.Logger.Warn(Tag,
                    "AppContext.MapRoot 为空（Stage 场景未注入 / 未挂 StageRoots）⇒ 地图根由 MapModule 自建（功能正常，只是场景层级不干净）");
            }
            else TryInvokeAttachRoot(ctx.Map, ctx.MapRoot, "IMapModule");

            if (ctx.EntityRoot == null)
            {
                Game.Logger.Warn(Tag,
                    "AppContext.EntityRoot 为空（Stage 场景未注入 / 未挂 StageRoots）⇒ 实体根由 ViewModule 自建");
            }
            else TryInvokeAttachRoot(ctx.View, ctx.EntityRoot, "IViewModule");
        }

        /// <summary>反射调用模块上的**非契约**入口 `AttachRoot(Transform)`（找不到只 Warn，不抛）。</summary>
        private static void TryInvokeAttachRoot(object module, Transform root, string facadeName)
        {
            if (module == null) { Missing(facadeName); return; }

            const string MethodName = "AttachRoot";
            try
            {
                var m = module.GetType().GetMethod(MethodName,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (m == null)
                {
                    Game.Logger.Warn(Tag, $"{module.GetType().Name} 上没有 {MethodName}(Transform)（非契约入口缺失？）" +
                                          $"⇒ 跳过根节点注入，{facadeName} 将自建根");
                    return;
                }
                m.Invoke(module, new object[] { root });
                Game.Logger.Info(Tag, $"根节点已注入：{module.GetType().Name}.{MethodName}(\"{root.name}\")");
            }
            catch (TargetInvocationException tie)
            {
                Game.Logger.Error(Tag, $"调用 {module.GetType().Name}.{MethodName} 抛异常：" +
                                       $"{tie.InnerException?.GetType().Name}: {tie.InnerException?.Message} ⇒ 该模块自建根",
                    tie.InnerException);
            }
            catch (System.Exception e)
            {
                Game.Logger.Error(Tag, $"反射调用 {module.GetType().Name}.{MethodName} 失败：{e.GetType().Name}: {e.Message}", e);
            }
        }

        // ═════════════════════════════════════════════════════════════════════
        // Stage 进 / 出
        // ═════════════════════════════════════════════════════════════════════

        private static void OnStageEntered()
        {
            _stageActive = true;
            var ctx = Ctx;

            AssertMapAndSpawn(ctx);

            // ★ 顺序要紧：**先开 HUD**（它的 `Awake` 才订阅那些事件），**再广播快照**；
            //   反过来则 HUD 收不到本次快照 ⇒ 第一次按 I/C/T/Q 打开的面板全是空的。
            var stats = ctx?.Player?.Snapshot();
            Game.UI?.Open<HudPanel>(stats);
            Game.Logger.Info(Tag, $"[Stage] HUD 已随 {Events.StageEntered} 打开（约定 §3.5 的 App 开关方案）");

            AppSnapshots.Broadcast("StageEntered");
        }

        private static void OnStageLeft()
        {
            _stageActive = false;
            AppSnapshots.Reset();
            AppDoorGuard.Reset();
            Game.UI?.Close<HudPanel>();
            Game.Logger.Info(Tag, $"[Stage] 已随 {Events.StageLeft} 关闭 HUD");
        }

        /// <summary>进图断言：地图已生成 + 城镇不刷怪（把"顺序错了 / 配表错了"当场暴露，而不是静默）。</summary>
        private static void AssertMapAndSpawn(AppContext ctx)
        {
            if (ctx?.Map == null)
            {
                Missing("IMapModule");
            }
            else if (!ctx.Map.IsGenerated)
            {
                Game.Logger.Error(Tag, $"进图断言失败：{Events.StageEntered} 时地图尚未生成 ⇒ 进图链条顺序有误");
            }
            else
            {
                // ⚠️ `GensSinceLastBoundary` 由 `AppDoorGuard.OnStageEntered` 上报后清零；本回调注册得**晚**
                //    （后注册先执行，见 Install 的说明）⇒ 这里读到的是"本次进图那一次生成"，正是想要的数。
                //    断言本身不依赖这个时序（`AppDoorGuard` 自己上报，见 `AppDoorGuard.Report`）。
                Game.Logger.Info(Tag,
                    $"[Assert] 进图地图已生成：区域={(int)ctx.Map.Area}({ctx.Map.Area}) {ctx.Map.Width}x{ctx.Map.Height} " +
                    $"seed={ctx.Map.Seed} 障碍={ctx.Map.BlockedCount} 可走={ctx.Map.WalkableCount} " +
                    $"本次进图累计生成 {AppDoorGuard.GensSinceLastBoundary} 次");
            }

            if (ctx?.Monster == null) { Missing("IMonsterModule"); return; }

            var alive = ctx.Monster.AliveCount;
            var area = ctx.Map != null ? ctx.Map.Area : AreaId.Town;
            if (area == AreaId.Town && alive != 0)
            {
                Game.Logger.Warn(Tag,
                    $"[Assert] 罗格营地不应刷怪，但 AliveCount={alive} ⇒ 检查 level_c.monsters / mon_density 与 AreaLevelTable");
            }
            else
            {
                Game.Logger.Info(Tag, $"[Assert] 刷怪已就绪：区域={area} 存活 {alive} 只（城镇必须 0；野外/洞穴 >0）");
            }
        }

        /// <summary>模块未装配的统一日志（降级但可定位）。</summary>
        public static void Missing(string what)
        {
            Game.Logger.Warn(Tag, $"模块 {what} 未装配（AppContext 里为 null）⇒ 该项接线降级（功能不可用）");
        }
    }
}
