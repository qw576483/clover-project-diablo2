// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · App/AppDoorGuard.cs      （agent-12 建，agent-05 按 agent-14 §B 现象 3 重写）
// **进图/过门的地图生成次数断言**：一次「进图」或一次「过门」只允许生成 1 次地图。
//
// ── 为什么要重写（原实现的根因，都有 Play 实测证据）───────────────────────────
//   现象：`[Warn] [App] [Assert] 本次过门已第 2 次收到 D2.Map.Generated（>1）⇒ 重复生成！`
//   真相：**地图只生成了一次**（Play 日志里 `[Map] Generate 完成` 只有 1 条），
//         第 2 次是 `AppSnapshots` 给 HUD/小地图**补发的同一张图的"回声"**。
//   原实现为什么把它算成"又生成了一张"：
//     ① `Game.Event` 同一优先级是「**后注册先执行**」（`Runtime/Core/Event.cs:141-143` 与 `:319-343`：
//        存储是执行顺序的逆序、派发时倒序遍历）—— 老注释里"AppDoorGuard 必须先收到 StageEntered"
//        的假设正好**是反的**：后注册的 `AppWiring.OnStageEntered`（里面 `AppSnapshots.Broadcast`）
//        先跑，`AppDoorGuard.OnStageEntered`（先注册）后跑 ⇒ "关窗口"永远来不及；
//     ② 窗口默认 `true`（`Reset()` 也置 `true`）⇒ 连"进图首次生成"都被算进过门窗口。
//
// ── 重写后的两条规则（都**不依赖订阅顺序**，因此不会再被顺序问题咬到）────────
//   ① **只数"真的生成"**：`AppSnapshots` 自己发 `Events.MapGenerated`（快照回声）时会置
//      `AppSnapshots.EchoInFlight`，本类见到就直接跳过（同步 Emit ⇒ 标志绝对可靠）。
//   ② **边界上报**：计数窗口 = 相邻两个"边界事件"之间。边界有两种：
//      · `Events.StageEntered`（本次进图装配完毕；地图生成就发生在它之前一点点）；
//      · `Events.ExitEntered`（一次过门；地图重生成发生在同一次派发里、Flow 的处理器中）。
//      每到边界就**上报一次计数并清零** ⇒ 每条边界恰好对应"一张图"，与谁先谁后无关。
//      （顺序只影响"哪条边界认领这次生成"：因为 `AppFlow` 的 `ExitEntered` 处理器**总是**晚注册
//        ⇒ 总是先跑，所以过门这次生成**稳定地**落在本次过门自己的窗口里，不存在错位。）
//
// ⛔ 去重规则本身**不在这里**（也不该在这里）：
//   · `PlayerModule.CheckExit`   —— 同一出口格只触发一次（`_lastExitGrid`），且目标区域 == 当前区域时不发；
//   · `AppFlow.EnterArea`        —— `_switchingArea` 防重入 + `to == _area` 直接忽略。
//   本文件的存在意义 = **把"重复生成"变成一条可检索的日志**（否则它是静默的：玩家只觉得"卡了一下"）。
// ─────────────────────────────────────────────────────────────────────────────

using CloverEngine;
using Diablo2.Core;
using Diablo2.Def;

namespace Diablo2.App
{
    /// <summary>进图 / 过门的地图生成次数断言。</summary>
    internal static class AppDoorGuard
    {
        private const string Tag = AppWiring.Tag;

        /// <summary>已处理的过门次数（`Events.ExitEntered` 次数）。</summary>
        private static int _doorSerial;

        /// <summary>自上次边界之后**真的**生成了几次地图（回声不计）。</summary>
        private static int _gensSinceLastBoundary;

        /// <summary>自上次边界之后被识别为"快照回声"而跳过的 `Events.MapGenerated` 次数（排障用）。</summary>
        private static int _echoesSinceLastBoundary;

        /// <summary>自上次边界之后真的生成地图的次数（**供日志/断言读取**）。</summary>
        public static int GensSinceLastBoundary => _gensSinceLastBoundary;

        /// <summary>已处理过的过门次数。</summary>
        public static int DoorSerial => _doorSerial;

        public static void Install(AppContext ctx)
        {
            var bus = Game.Event;
            if (bus == null)
            {
                Game.Logger.Error(Tag, "AppDoorGuard.Install：Game.Event 为 null ⇒ 进图/过门生成次数断言未生效");
                return;
            }
            bus.On<AreaId>(Events.ExitEntered, OnExitEntered);
            bus.On(Events.StageEntered, OnStageEntered);
            bus.On<MinimapArgs>(Events.MapGenerated, OnMapGenerated);
        }

        /// <summary>
        /// 新一局 Play 的静态复位（见 `Bootstrap` 的 `[RuntimeInitializeOnLoadMethod]`／skill P-3）：
        /// 域不重载时计数会跨局残留 ⇒ 第二局的第一次过门会被算成"第 2 次生成"。
        /// </summary>
        internal static void ResetStaticForNewPlaySession()
        {
            _doorSerial = 0;
            _gensSinceLastBoundary = 0;
            _echoesSinceLastBoundary = 0;
        }

        /// <summary>离场复位（回主菜单后下一次进图重新计数）。</summary>
        public static void Reset()
        {
            if (_gensSinceLastBoundary > 1)
            {
                // 极端情况：离场时窗口里还压着"多次生成" ⇒ 别让它悄悄消失（Warn 已经在收到第 2 次时打过）
                Game.Logger.Warn(Tag,
                    $"[Assert] 离场时本次进图/过门窗口内累计生成 {_gensSinceLastBoundary} 次（>1）⇒ 重复生成的现场保留在本行");
            }

            _doorSerial = 0;
            _gensSinceLastBoundary = 0;
            _echoesSinceLastBoundary = 0;
        }

        // ═════════════════════════════════════════════════════════════════════
        // 边界事件
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>一次过门：先上报"上一个窗口"（含本次过门自己的重生成），再开新窗口。</summary>
        private static void OnExitEntered(AreaId to)
        {
            _doorSerial++;
            Report($"过门 #{_doorSerial} → {to}");
            Game.Logger.Info(Tag, $"[Stage] 过门 #{_doorSerial} → {to}（生成计数已清零，期望这一次过门恰好生成 1 张图）");
        }

        /// <summary>本次进图装配完毕：上报"进图窗口"。</summary>
        private static void OnStageEntered()
        {
            Report("本次进图");
        }

        /// <summary>
        /// 上报并清零。计数与`Events.MapGenerated`**无关**于订阅顺序：
        /// 地图生成总是发生在边界事件"之内或之前"（见文件头 ②）。
        /// </summary>
        private static void Report(string what)
        {
            var echoes = _echoesSinceLastBoundary > 0 ? $"，另有 {_echoesSinceLastBoundary} 次快照回声已忽略" : string.Empty;

            if (_gensSinceLastBoundary == 1)
            {
                Game.Logger.Info(Tag, $"[Assert] {what}：生成地图 1 次 —— 去重断言通过{echoes}");
            }
            else if (_gensSinceLastBoundary == 0)
            {
                Game.Logger.Info(Tag, $"[Assert] {what}：未生成地图（上游已去重：重复请求被忽略）{echoes}");
            }
            else
            {
                Game.Logger.Warn(Tag,
                    $"[Assert] {what}：生成地图 {_gensSinceLastBoundary} 次（期望 =1）⇒ 重复生成！" +
                    $"请检查 {Events.ExitEntered} 的发送方与 {Events.MapGenerated} 的发送方{echoes}");
            }

            _gensSinceLastBoundary = 0;
            _echoesSinceLastBoundary = 0;
        }

        // ═════════════════════════════════════════════════════════════════════
        // 计数
        // ═════════════════════════════════════════════════════════════════════

        private static void OnMapGenerated(MinimapArgs args)
        {
            // ① 快照回声（App 层把**已有的**地图再播一次给 HUD/小地图）⇒ 不是"又生成了一张"。
            //    必须**在计数之前**判掉，否则就是 agent-14 §B 现象 3 的那条假警报。
            if (AppSnapshots.EchoInFlight)
            {
                _echoesSinceLastBoundary++;
                return;
            }

            _gensSinceLastBoundary++;
            var area = args != null ? args.areaId : -1;

            if (_gensSinceLastBoundary == 1)
            {
                Game.Logger.Info(Tag,
                    $"[Map] 地图已生成（本窗口第 1 次）：areaId={area} " +
                    $"{(args != null ? args.width + "x" + args.height : "?")} seed={(args != null ? args.seed : 0)} " +
                    $"tiles={(args != null ? args.tiles.Count : 0)} markers={(args != null ? args.markerX.Count : 0)}");
            }
            else
            {
                Game.Logger.Warn(Tag,
                    $"[Assert] 本次进图/过门已第 {_gensSinceLastBoundary} 次收到 {Events.MapGenerated}（>1）⇒ 重复生成！" +
                    "（一次进图/过门只应生成一次地图）");
            }
        }
    }
}
