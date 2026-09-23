// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · App/AppProgress.cs   ★ 片 save-progress 新增（2026-09-24）
//
// 作用：**读档后把"进度类状态"灌回去 / 存盘时把它们收进来**。管两块：
//   ① 传送点已激活列表（`App/AppWaypoint.Visited`，进程内 static）；
//   ② 小地图已探索格（权威 = 渲染层 `Module/Map/MapView._explored`，经 `IMapModule.ExploredCells`
//      对外投影；**作用域 = 当前区域**）。
//
// 为什么需要这一层（链条断在哪，逐条对齐片 save-areaid 的《同族穷举表》）：
//   · 存盘：`Module/Save/SaveModule.Save()`（无参）在**新造**的 `CharacterSave` 上逐字段从 Live 收集
//     ⇒ 凡"没被显式收集"的字段就恒为默认值。上面两项的**持有者不在模块侧**（一个在 App 交互类、
//     一个在渲染层）⇒ `SaveModule` 发 `Events.SaveCollect`，**本类**把两块填进去（同一个收集阶段）。
//   · 读档：`SaveModule.Load()` 把数据装回 Player/Item/Quest/Skill/Npc，但上面两块**没人装**
//     （⛔ 也不能在 Load 里装：那一刻地图还没生成、面板还没建，而且 Player 的落格是 Flow 在进图时做的）
//     ⇒ 本类在 `Events.LoadDone` 时**暂存**，在**进图装配完成之后**（`AppWiring.OnStageEntered`）与
//     **换区铺装完成之后**（`Events.MapAreaReady`）再回灌。
//
// 口径（逐条）：
//   ① **每区域一份**：格坐标是区域局部的（`IMapModule.ExploredCells` 注释原文：「⛔ 跨区域合并成一个集合
//      在语义上是错的」）⇒ 本类按 `areaId` 分别累积（会话内跨区域），落盘 = `exploredByArea` 列表；
//   ② **累积来源**：`Events.MapExplored`（Map 的唯一增量出口，"走过即记忆"）**加上**存盘那一刻从
//      `IMapModule.ExploredCells` 抄一遍当前区域（权威对齐，避免"某格被标了但事件没收全"）；
//   ③ **回灌 = 并入（幂等）**：走 `Events.MapExploredRestore`（收方 `MapModule` 把格并进已探索位图）；
//      ⛔ 不新增第二条"设置已探索"的路（权威只有渲染层位图那一份）。
//   ④ **尺寸不同 ⇒ 不套用**：野外/洞穴的尺寸是随机生成的，而本项目按原版口径**每局重掷 seed**
//      （`AppFlow.RollEntrySeed`）⇒ 上一局那张图的已探索掩码套到本局另一张尺寸/布局不同的图上**是错的**
//      ⇒ 尺寸不符时**点名 Warn 并跳过**（营地是固定布局 ⇒ 正常能对上，正是"读档后 automap 记得范围"的场景）。
//
// ⛔ 无 Unity 依赖的调用面（AppContext / 事件总线都是引擎门面）：本类只读接口 + 发事件。
// ─────────────────────────────────────────────────────────────────────────────

using System.Collections.Generic;
using CloverEngine;
using Diablo2.Core;
using Diablo2.Def;
using Diablo2.Module;
using UnityEngine;

namespace Diablo2.App
{
    /// <summary>把"传送点已激活 / 小地图已探索"两块进度类状态在**存档**与 **Live** 之间搬运（见文件头）。</summary>
    internal static class AppProgress
    {
        private const string Tag = AppWiring.Tag;

        /// <summary>一个区域的已探索集合（`cells` = 行优先格索引；`w/h` = 抄它时的地图尺寸）。</summary>
        private sealed class AreaSet
        {
            public int w;
            public int h;
            public readonly HashSet<int> cells = new HashSet<int>();
        }

        /// <summary>本会话**按区域**累积的已探索格（跨区域、跨换区；只在"新一局 Play"时清）。</summary>
        private static readonly Dictionary<int, AreaSet> ByArea = new Dictionary<int, AreaSet>();

        /// <summary>`Events.LoadDone` 暂存的"待回灌"数据（进图装配完成后才用得上）。</summary>
        private static List<int> _pendingVisited;
        private static List<ExploredAreaDto> _pendingExplored;
        private static bool _pendingReady;

        /// <summary>本类是否已接线（`AppWiring.Install` 调一次）。</summary>
        private static bool _installed;

        /// <summary>"地图不可用"只报一次（避免每次事件刷屏）。</summary>
        private static bool _warnedNoMap;

        /// <summary>接线（由 `AppWiring.Install` 调；重复调用幂等）。</summary>
        internal static void Install(AppContext ctx)
        {
            var bus = Game.Event;
            if (bus == null)
            {
                Game.Logger.Error(Tag, "AppProgress.Install：Game.Event 为 null ⇒ 进度类状态（传送点/探索格）"
                    + "既不收集也不回灌（读档后两者仍会丢 —— 这正是本片要修的那条断链）");
                return;
            }
            if (_installed)
            {
                Game.Logger.Warn(Tag, "AppProgress.Install 被重复调用 ⇒ 订阅不重复建");
                return;
            }
            _installed = true;

            bus.On<CharacterSave>(Events.SaveCollect, OnSaveCollect);              // 存盘：填这两块
            bus.On<CharacterSave>(Events.LoadDone, OnLoadDone);                    // 读档：暂存待回灌
            bus.On<IReadOnlyCollection<Vector2Int>>(Events.MapExplored, OnMapExplored);   // 累积（按区域）
            bus.On(Events.MapAreaReady, OnMapAreaReady);                           // 换区铺装完成 ⇒ 回灌该区域

            Game.Logger.Info(Tag, $"进度类状态已接线：存盘收集（{Events.SaveCollect}）/ 读档暂存"
                + $"（{Events.LoadDone}）/ 已探索累积（{Events.MapExplored}）/ 换区回灌（{Events.MapAreaReady}）");
        }

        /// <summary>新一局 Play 的静态复位（域不重载时上局的累积会残留 ⇒ 第二局带着上一局的探索记忆）。</summary>
        internal static void ResetStaticForNewPlaySession()
        {
            ByArea.Clear();
            _pendingVisited = null;
            _pendingExplored = null;
            _pendingReady = false;
            _warnedNoMap = false;
            // _installed 由 AppWiring.ResetStaticForNewPlaySession 一并复位（它管本类的接线标志）
        }

        /// <summary>接线标志复位（只由 `AppWiring.ResetStaticForNewPlaySession` 调）。</summary>
        internal static void ResetInstalledFlag()
        {
            _installed = false;
        }

        // ── 存盘：收集 ────────────────────────────────────────────────────────

        /// <summary>
        /// `Events.SaveCollect`（发方 = `SaveModule.Save()` 的收集阶段）⇒ 把两块进度写进本次存档。
        /// </summary>
        private static void OnSaveCollect(CharacterSave data)
        {
            if (data == null)
            {
                Game.Logger.Warn(Tag, $"{Events.SaveCollect} 载荷为 null ⇒ 进度类状态未收集（存档会缺这两项）");
                return;
            }

            data.visitedWaypoints = AppWaypoint.SnapshotVisited();
            data.exploredByArea = SnapshotExplored();

            var cells = 0;
            if (data.exploredByArea != null)
            {
                for (var i = 0; i < data.exploredByArea.Count; i++)
                {
                    var dto = data.exploredByArea[i];
                    if (dto == null) continue;
                    var tmp = new List<int>();
                    cells += ExploredCodec.Decode(dto, tmp);
                }
            }
            Game.Logger.Info(Tag, $"[App] 进度收集：传送点已激活 {data.visitedWaypoints.Count} 个"
                + $"［{string.Join(",", data.visitedWaypoints)}］；已探索区域 {data.exploredByArea.Count} 个"
                + $"（合计 {cells} 格）⇒ 写进本次存档");
        }

        /// <summary>
        /// 存盘那一刻的"每区域已探索格"：先把**当前区域**从权威（`IMapModule.ExploredCells`）对齐进累积表，
        /// 再全部编码成 `ExploredAreaDto`（紧凑位图，见 `Def/ExploredCodec`）。
        /// </summary>
        private static List<ExploredAreaDto> SnapshotExplored()
        {
            MergeCurrentAreaIntoAccumulator();

            var areas = new List<int>(ByArea.Keys);
            areas.Sort();                                  // 确定性：存档字节稳定（幂等断言要靠它）

            var res = new List<ExploredAreaDto>(areas.Count);
            for (var i = 0; i < areas.Count; i++)
            {
                var area = areas[i];
                var set = ByArea[area];
                var dto = new ExploredAreaDto
                {
                    area = area,
                    w = set.w,
                    h = set.h,
                    cells = ExploredCodec.Encode(set.cells, set.w, set.h),
                };
                res.Add(dto);
            }
            return res;
        }

        /// <summary>把当前区域（权威 = `IMapModule.ExploredCells`）并进累积表（幂等）。</summary>
        private static void MergeCurrentAreaIntoAccumulator()
        {
            var map = Map;
            if (map == null || !map.IsGenerated) return;

            var w = map.Width;
            var h = map.Height;
            if (w <= 0 || h <= 0) return;

            var set = GetOrCreate((int)map.Area, w, h);
            var live = map.ExploredCells;
            if (live == null) return;
            foreach (var c in live)
            {
                var i = ExploredCodec.IndexOf(c.x, c.y, w, h);
                if (i >= 0) set.cells.Add(i);
            }
        }

        // ── 读档：暂存 + 回灌 ─────────────────────────────────────────────────

        private static void OnLoadDone(CharacterSave data)
        {
            if (data == null)
            {
                // 读档失败（`Events.LoadDone` 的 null = 失败，见 `Core/Events.cs`）⇒ 别把上一次的待回灌数据留在手里
                _pendingVisited = null;
                _pendingExplored = null;
                _pendingReady = false;
                Game.Logger.Warn(Tag, $"收到 {Events.LoadDone}(null)（读档失败）⇒ 作废待回灌的进度数据");
                return;
            }

            _pendingVisited = data.visitedWaypoints;
            _pendingExplored = data.exploredByArea;
            _pendingReady = true;
            Game.Logger.Info(Tag, $"读档：已暂存待回灌的进度数据 —— 传送点 {((_pendingVisited == null) ? 0 : _pendingVisited.Count)} 个、"
                + $"已探索区域 {((_pendingExplored == null) ? 0 : _pendingExplored.Count)} 个"
                + "（等进图装配完成后回灌：地图此刻还没生成，见本文件头）");
        }

        /// <summary>
        /// 进图装配完成（`AppWiring.OnStageEntered`，**在快照广播之前**）⇒ 回灌当前区域的进度：
        /// ① 首次 ⇒ 把存档里的传送点激活列表并进 `AppWaypoint`、把各区域已探索格装进累积表；
        /// ② 每次都 ⇒ 把**当前区域**的已探索格下发地图（`Events.MapExploredRestore`）。
        /// </summary>
        internal static void RestoreForCurrentStage()
        {
            if (_pendingReady)
            {
                _pendingReady = false;
                AppWaypoint.RestoreVisited(_pendingVisited);
                SeedAccumulator(_pendingExplored);
                _pendingVisited = null;
                _pendingExplored = null;
            }
            RestoreArea(CurrentArea(), "读档进图 / 进 Stage");
        }

        /// <summary>换区铺装完成（`Events.MapAreaReady`）⇒ 该区域若有存档带来的记忆，回灌它。</summary>
        private static void OnMapAreaReady()
        {
            RestoreArea(CurrentArea(), $"换区铺装完成（{Events.MapAreaReady}）");
        }

        /// <summary>`Events.MapExplored`（Map 的唯一增量出口）⇒ 按当前区域累积（会话内跨区域）。</summary>
        private static void OnMapExplored(IReadOnlyCollection<Vector2Int> cells)
        {
            var map = Map;
            if (map == null || !map.IsGenerated)
            {
                if (!_warnedNoMap)
                {
                    _warnedNoMap = true;
                    Game.Logger.Warn(Tag, $"{Events.MapExplored} 到达时地图不可用 ⇒ 本次增量未累积"
                        + "（只报一次；存档里的「已探索」会少这一批格）");
                }
                return;
            }
            if (cells == null || cells.Count == 0) return;

            var set = GetOrCreate((int)map.Area, map.Width, map.Height);
            var n = 0;
            foreach (var c in cells)
            {
                var i = ExploredCodec.IndexOf(c.x, c.y, map.Width, map.Height);
                if (i >= 0 && set.cells.Add(i)) n++;
            }
            if (n > 0) Game.Logger.Info(Tag, $"已探索累积：区域 {(int)map.Area} 新增 {n} 格 ⇒ 该区域累计 {set.cells.Count} 格");
        }

        /// <summary>把存档里的"每区域已探索格"解回累积表（**并入**；旧档缺字段 ⇒ 什么都不做）。</summary>
        private static void SeedAccumulator(List<ExploredAreaDto> list)
        {
            if (list == null || list.Count == 0)
            {
                Game.Logger.Info(Tag, "存档里没有已探索记录（旧档 / 本档没存过）⇒ automap 从空白开始（正常，不报错）");
                return;
            }

            for (var i = 0; i < list.Count; i++)
            {
                var dto = list[i];
                if (dto == null) continue;
                if (dto.w <= 0 || dto.h <= 0)
                {
                    Game.Logger.Warn(Tag, $"存档里的已探索记录尺寸非法（area={dto.area} {dto.w}x{dto.h}）⇒ 跳过该项");
                    continue;
                }
                var set = GetOrCreate(dto.area, dto.w, dto.h);
                var tmp = new List<int>();
                var n = ExploredCodec.Decode(dto, tmp);
                for (var k = 0; k < tmp.Count; k++) set.cells.Add(tmp[k]);
                Game.Logger.Info(Tag, $"读档：区域 {dto.area} 的已探索记忆已装入（{dto.w}x{dto.h} 解出 {n} 格"
                    + $" ⇒ 累积 {set.cells.Count} 格）");
            }
        }

        /// <summary>
        /// 把某区域的已探索格**下发地图**（`Events.MapExploredRestore`）。
        /// <para>⛔ 尺寸不符（本局 seed 重掷 ⇒ 另一张图）⇒ 点名 Warn 并**不套用**上一局的掩码（见文件头 ④）。</para>
        /// </summary>
        private static void RestoreArea(int area, string why)
        {
            if (area < 0) return;

            var map = Map;
            if (map == null || !map.IsGenerated)
            {
                Game.Logger.Warn(Tag, $"回灌已探索（{why}）：地图未生成 ⇒ 本次不回灌（区域 {area}）");
                return;
            }

            AreaSet set;
            if (!ByArea.TryGetValue(area, out set) || set.cells.Count == 0) return;   // 没有该区域的记忆：正常，静默

            if (set.w != map.Width || set.h != map.Height)
            {
                Game.Logger.Warn(Tag, $"回灌已探索（{why}）：区域 {area} 的记忆尺寸 {set.w}x{set.h} 与本局地图 "
                    + $"{map.Width}x{map.Height} 不同 ⇒ **不套用**（野外/洞穴每局重掷 seed ⇒ 那是另一张图上的掩码，"
                    + "套过来会画出错的地形轮廓；营地是固定布局，正常能对上）");
                return;
            }

            var cells = new List<Vector2Int>(set.cells.Count);
            foreach (var i in set.cells)
            {
                int x, y;
                bool ok;
                ExploredCodec.ToCell(i, set.w, set.h, out x, out y, out ok);
                if (ok) cells.Add(new Vector2Int(x, y));
            }
            if (cells.Count == 0) return;

            Game.Logger.Info(Tag, $"回灌已探索（{why}）：区域 {area} 下发 {cells.Count} 格"
                + $"（{set.w}x{set.h}，来源 = 存档 `exploredByArea` / 本会话累积）");
            if (Game.Event == null) return;
            Game.Event.Emit<IReadOnlyCollection<Vector2Int>>(Events.MapExploredRestore, cells);
        }

        // ── 取用 ─────────────────────────────────────────────────────────────

        private static AreaSet GetOrCreate(int area, int w, int h)
        {
            AreaSet set;
            if (!ByArea.TryGetValue(area, out set))
            {
                set = new AreaSet { w = w, h = h };
                ByArea[area] = set;
                return set;
            }
            // 尺寸变了（同一区域在本会话里重新生成过）⇒ 旧掩码作废（索引口径变了，留着就是错的地图轮廓）
            if (set.w != w || set.h != h)
            {
                Game.Logger.Warn(Tag, $"已探索累积：区域 {area} 的地图尺寸从 {set.w}x{set.h} 变为 {w}x{h}"
                    + " ⇒ 本会话该区域的旧掩码作废（索引口径已变）");
                set.w = w;
                set.h = h;
                set.cells.Clear();
            }
            return set;
        }

        private static int CurrentArea()
        {
            var map = Map;
            return (map != null && map.IsGenerated) ? (int)map.Area : -1;
        }

        private static IMapModule Map
        {
            get { return AppWiring.Ctx != null ? AppWiring.Ctx.Map : null; }
        }
    }
}
