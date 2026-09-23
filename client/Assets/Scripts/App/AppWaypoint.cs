// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · App/AppWaypoint.cs   ★ 片 g1-resume 新增（验收表 S-40 / 用户报「传送点没效果」）
//
// 为什么需要它（链条断在哪）：传送点的三件事**一件都没有** ——
//   ① 世界里没有物件（`IMapModule` 上也没这个点位）；② 没有人判"走到/点到传送点"；
//   ③ 没有面板，更没有"选了目的地 ⇒ 切区域"。本类补 ②（判点击 + 判到达 + 组装列表），
//   面板本身 = `UI/WaypointPanel.cs`，切区域复用**既有**那条链（见下）。
//
// 装配契约（与 `AppDoorGuard` / `AppSnapshots` 同形）：
//   `internal static class AppWaypoint` + `Install(AppContext)`，由 `AppWiring.Install` 调。
//   ⛔ 不持有任何模块的**实现类型**：一律走 `AppWiring.Ctx` 的接口 + `Core/Events` 的事件。
//
// 事件口径（只增不改，都是**已有的**）：
//   · 收 `Events.MoveCommand`(Vector2Int)  —— 判"点的是不是传送点那一格"（与 `NpcModule`
//     的"点 NPC 走过去说话"同一条兜底路径：本项目点击只落到 MoveCommand）；
//   · 收 `Events.PlayerGridChanged`(Vector2Int) —— 判"走到了没有"（8 邻 = 可交互，同
//     `ItemModule.Pickup` 的 `Iso.IsAdjacent` 口径）；
//   · 收 `Events.StageEntered` / `Events.AreaChanged`(AreaId) —— 记"已去过区域"（目的地集合来源）；
//   · 收 `Events.WaypointTravelRequest`(int) —— 面板选了目的地；
//   · 发 `Events.ExitEntered`(AreaId) —— **只走这一条**切区域（`AppFlow.EnterArea`：
//     重生成地图 / 移怪 / 挪玩家 / 关面板 / 发 `AreaChanged` 全在那条链上）。
//     ⚠️ 口径说明（已写进 `Core/Events.cs` 的常量注释）：`ExitEntered` 在引擎侧被
//     `AppDoorGuard`（过门计数）/ `AudioHook`（传送音效，其注释原文就是"传送 ← ExitEntered"）/
//     `QuestModule`（换区）/ `AppFlow`（切区）共同消费 —— 传送点换区在语义上正是"一次区域切换"
//     ⇒ 复用它是**语义一致**的，且不新增一条与它对等的旁路（⛔ 旁路必然漂移）。
// ─────────────────────────────────────────────────────────────────────────────

using System.Collections.Generic;
using CloverEngine;
using Diablo2.Core;
using Diablo2.Def;
// ⚠️ 2026-09-23 主 agent 补（`g1-resume` 落卡时漏的 using）：`IMapModule` / `IPlayerModule` 定义在
//    `Module/Contracts.cs` 的 **`Diablo2.Module`** 命名空间里（该文件另有一个 `Diablo2.Def` 段），
//    漏这一行 ⇒ CS0246 ×4 ⇒ **Unity 整棵树编不过**（`AppWaypoint.cs:179,236,247,252`）。
using Diablo2.Module;
using Diablo2.UI;
using UnityEngine;

namespace Diablo2.App
{
    /// <summary>传送点：锚点交互 + 「已去过区域」集合 + 把面板的选择转成一次区域切换。</summary>
    internal static class AppWaypoint
    {
        private const string Tag = AppWiring.Tag;

        /// <summary>已去过的区域（面板的"已激活目的地"就从这里来；按 `AreaId` 枚举序输出）。</summary>
        private static readonly HashSet<AreaId> Visited = new HashSet<AreaId>();

        /// <summary>玩家点了传送点、正在走过去（到达 8 邻即开面板）。</summary>
        private static bool _walking;

        /// <summary>等待到达的锚点格。</summary>
        private static Vector2Int _target;

        /// <summary>挂接线（由 `AppWiring.Install` 调用，只调一次）。</summary>
        public static void Install(AppContext ctx)
        {
            var bus = Game.Event;
            if (bus == null)
            {
                Game.Logger.Error(Tag, "AppWaypoint.Install：Game.Event 为 null ⇒ 传送点交互未接线");
                return;
            }

            bus.On<Vector2Int>(Events.MoveCommand, OnMoveCommand);
            bus.On<Vector2Int>(Events.PlayerGridChanged, OnPlayerGridChanged);
            bus.On(Events.StageEntered, OnStageBoundary);
            bus.On<AreaId>(Events.AreaChanged, OnAreaChanged);
            bus.On<int>(Events.WaypointTravelRequest, OnTravelRequest);

            Game.Logger.Info(Tag, $"传送点已接线：点锚点走过去 ⇒ 开传送面板；面板选目的地 ⇒ "
                + $"{Events.ExitEntered}（原版字串/目的地集合口径见 UI/WaypointPanel.cs）");
        }

        /// <summary>
        /// 新一局 Play 的静态复位（域不重载时 `Visited` 会跨局残留 ⇒ 第二局一开局就"去过"上一局的区域）。
        /// 与 `AppDoorGuard.ResetStaticForNewPlaySession` 同口径，由 `AppWiring.Install` 一并调。
        /// </summary>
        internal static void ResetStaticForNewPlaySession()
        {
            Visited.Clear();
            _walking = false;
            _target = Vector2Int.zero;
        }

        // ── 交互 ─────────────────────────────────────────────────────────────

        /// <summary>点了传送点那一格 ⇒ 记下"走过去"；点别处 ⇒ 撤销。</summary>
        private static void OnMoveCommand(Vector2Int target)
        {
            var map = Map;
            if (map == null || !map.IsGenerated) return;

            if (!IsWaypointCell(map, target))
            {
                if (_walking) _walking = false;
                return;
            }

            var p = Player;
            if (p != null && Iso.IsAdjacent(p.Grid, target))
            {
                // 已经站在旁边 ⇒ 不用走，直接开（否则不会有 PlayerGridChanged，面板永远不开）
                Game.Logger.Info(Tag, $"点击传送点 ({target.x},{target.y})：玩家已在相邻格 "
                    + $"({p.Grid.x},{p.Grid.y}) ⇒ 直接打开传送面板");
                OpenPanel();
                return;
            }

            _walking = true;
            _target = target;
            Game.Logger.Info(Tag, $"点击传送点 ({target.x},{target.y}) ⇒ 走过去后打开传送面板");
        }

        /// <summary>玩家换格：到达锚点 8 邻就开面板。</summary>
        private static void OnPlayerGridChanged(Vector2Int g)
        {
            if (!_walking) return;

            if (Iso.GridDistance(g, _target) <= 1)
            {
                _walking = false;
                Game.Logger.Info(Tag, $"已走到传送点 {( _target.x)},{(_target.y)} 的相邻格 ({g.x},{g.y}) ⇒ 打开传送面板");
                OpenPanel();
                return;
            }

            // 目标格被人占了 / 不可走 ⇒ 走不到就放弃（不静默）
            var map = Map;
            if (map != null && map.IsGenerated && !map.Walkable(_target))
            {
                _walking = false;
                Game.Logger.Warn(Tag, $"传送点锚点 ({_target.x},{_target.y}) 不可走 ⇒ 放弃本次交互"
                    + "（正常不该发生：生成期已校验锚点可走）");
            }
        }

        /// <summary>进图 / 换区 ⇒ 记下"已去过"（面板的激活集来源）。</summary>
        private static void OnStageBoundary()
        {
            RecordVisited();
        }

        private static void OnAreaChanged(AreaId to)
        {
            RecordVisited();
        }

        private static void RecordVisited()
        {
            var map = Map;
            if (map == null || !map.IsGenerated) return;

            if (Visited.Add(map.Area))
            {
                Game.Logger.Info(Tag, $"传送点：新区域已激活「{WaypointPanel.NameOf((int)map.Area)}」"
                    + $"（area={(int)map.Area}）—— 已激活 {Visited.Count} 个");
            }
        }

        // ── 面板 ─────────────────────────────────────────────────────────────

        private static void OpenPanel()
        {
            var map = Map;
            if (map == null || !map.IsGenerated)
            {
                Game.Logger.Warn(Tag, "传送面板：地图未生成 ⇒ 不开（正常不该发生）");
                return;
            }

            var args = BuildArgs(map);
            // 已打开时 `Game.UI.Open` 走引擎的"已存在分支"（置顶 + 再调一次 `OnOpen`）
            // ⇒ 这里不需要额外的"是否已开"标志（`D2ConfirmPanel` 同口径）。
            Game.Logger.Info(Tag, $"打开传送面板：当前区域 {(int)map.Area}，目的地 {args.dests.Count} 条");
            Game.UI.Open<WaypointPanel>(args);
        }

        /// <summary>
        /// 组装打开参数。目的地集合 = `WaypointPanel.PlanDests(已去过, 当前区域)`
        /// （纯函数；离线宿主 `uicheck` 逐项断言，⛔ 本类不再自己算一份，避免两处口径漂移）。
        /// </summary>
        private static WaypointArgs BuildArgs(IMapModule map)
        {
            var visited = new List<int>(Visited.Count);
            foreach (var a in Visited) visited.Add((int)a);

            var dests = WaypointPanel.PlanDests(visited, (int)map.Area);
            if (dests.Count == 0)
            {
                Game.Logger.Info(Tag, "传送面板：还没有激活任何**别的**区域（原版那 8 个传送点里"
                    + "本工程只做了 Act I 的三张图）⇒ 显示原版字串「尚未啟動其他傳送點」");
            }
            return new WaypointArgs { dests = dests };
        }

        // ── 传送 ─────────────────────────────────────────────────────────────

        /// <summary>面板选了目的地：校验后发 `Events.ExitEntered`（切区域的唯一链路）。</summary>
        private static void OnTravelRequest(int area)
        {
            if (!System.Enum.IsDefined(typeof(AreaId), area))
            {
                Game.Logger.Warn(Tag, $"传送请求被拒绝：区域号 {area} 不在 AreaId 登记表里（非法请求）");
                return;
            }

            var dest = (AreaId)area;
            var map = Map;
            if (map == null || !map.IsGenerated)
            {
                Game.Logger.Warn(Tag, "传送请求被拒绝：地图未生成");
                return;
            }

            if (!Visited.Contains(dest))
            {
                Game.Logger.Warn(Tag, $"传送请求被拒绝：目标 {dest} **未激活**（面板只列已激活的目的地；"
                    + "非法请求不下发切区）");
                return;
            }

            if (dest == map.Area)
            {
                Game.Logger.Warn(Tag, $"传送请求被拒绝：目标 {dest} 就是当前区域（原地传送无意义）");
                return;
            }

            Game.Logger.Info(Tag, $"传送：{map.Area} → {dest}（发 {Events.ExitEntered}，"
                + "切区由 AppFlow.EnterArea 完成）");

            // 先关面板，再发切区（切区链自己也会关所有面板；先关可避免"面板挂在旧图上"的中间态）
            Game.UI.Close<WaypointPanel>();
            Game.Event.Emit(Events.ExitEntered, dest);
        }

        // ── 内部 ─────────────────────────────────────────────────────────────

        /// <summary>本体是否是传送点锚点格。</summary>
        private static bool IsWaypointCell(IMapModule map, Vector2Int g)
        {
            var pts = map.WaypointPoints;
            if (pts == null) return false;
            for (var i = 0; i < pts.Count; i++)
            {
                if (pts[i] == g) return true;
            }
            return false;
        }

        private static IMapModule Map
        {
            get { return AppWiring.Ctx != null ? AppWiring.Ctx.Map : null; }
        }

        private static IPlayerModule Player
        {
            get { return AppWiring.Ctx != null ? AppWiring.Ctx.Player : null; }
        }
    }
}
