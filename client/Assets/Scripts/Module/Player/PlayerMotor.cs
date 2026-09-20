// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · Module/Player/PlayerMotor.cs
// 主角**移动**：目标格 → `IMapModule.FindPath` → 路点列表 → 沿路点推进（原版点击移动语义）。
//
// 为什么自己写而不给引擎：原版 D2 是**等距点击移动**（`tools/ai-skill/conventions.md`「与全局
// skill 的差异」），位置由本地解算；引擎 `Game.Camera.Follow` 只是「锁 Z 的简单跟随」，
// 移动本身没有可复用件（且本项目地图是业务侧格子数据，见引擎 `CloverEngine.AStar`，源码在
// `clover-client-unity-engine/Runtime/Core/AStar.cs`）。
//
// 坐标系（本项目最容易错的地方，`tools/ai-skill/conventions.md` §坐标与等距投影）：
//   · 内部用**连续格坐标**（cell-center 空间）：格 (gx,gy) 的中心 = (gx+0.5, gy+0.5)；
//   · 世界坐标 = 等距投影：`x = (cx-cy)*Iso.HalfW`、`y = -(cx+cy)*Iso.HalfH`、`z = 0`
//     —— 该式与 `Iso.GridToWorld(gx,gy)` 在 (cx,cy)=(gx+0.5,gy+0.5) 处**逐项相等**，
//     且是线性变换 ⇒ 格空间的直线推进投影到世界仍是直线（方向无关，速度均匀）。
//   · 反向取格一律 `Iso.WorldToGrid`（内部 `Mathf.FloorToInt`，`constraints.md` #4）。
//
// ⛔ 速度单位 = **格/秒**（`GameConst.PlayerWalkSpeed` 为基准）；不引入第二份速度常量。
//    「跑/走切换」（原版 R 键，`docs/agents/agent-13-修复轮.md` §A 第 5 项）通过
//    <see cref="SpeedScale"/> 缩放基准速度（跑 = 1.0，走 = 0.5），仍**只有一份**基准常量。
// ⛔ 不穿墙：进入每个路点前校验该格 `Walkable`，斜向步还要校验两侧格（`CloverEngine.AStar` 同规则）。
// ─────────────────────────────────────────────────────────────────────────────

using System.Collections.Generic;
using Diablo2.Core;
using Diablo2.Def;
using Diablo2.Module;
using UnityEngine;

namespace Diablo2.Module.Player
{
    /// <summary>主角移动器（纯逻辑：给地图接口就能跑，不碰 Transform / 不建对象）。</summary>
    internal sealed class PlayerMotor
    {
        /// <summary>单帧最多推进的路点数（防 dt 异常大时一帧走完几百格）。</summary>
        private const int MaxWaypointsPerTick = 256;

        private Vector2Int _grid;
        private Vector3 _world;
        private Vector2 _pos;                 // 连续格坐标（cell-center 空间）
        private Dir8 _dir = Dir8.S;

        private List<Vector2Int> _path;
        private int _pathIndex;
        private Vector2Int _pathStart;
        private Vector2Int _targetGrid;
        private bool _hasTarget;
        private float _speedScale = 1f;       // 速度倍率（1 = 跑，0.5 = 走；原版 R 键切换）
        private int _tickFrames;              // 本次移动命令已消耗的帧数
        private int _lastFrames;              // 上一次成功到达所用帧数（自证输出用）
        private int _lastSteps;               // 上一次路径长度（自证输出用）
        private bool _blockedLogged;          // 「受阻」只报一次（防刷屏；用标志位不用 Log.WarnThrottled）
        private bool _warnedNoMap;            // 「地图不可用」只报一次
        private bool _budgetLogged;           // ★ R1-D 移动积分口径只报一次（见 LogBudgetOnce）

        /// <summary>当前格（`World` 反投影的格）。</summary>
        public Vector2Int Grid => _grid;

        /// <summary>当前世界坐标（渲染插值后的实际位置）。</summary>
        public Vector3 World => _world;

        /// <summary>当前朝向。</summary>
        public Dir8 Dir => _dir;

        /// <summary>是否正在沿路径移动。</summary>
        public bool IsMoving => _path != null && _pathIndex < _path.Count;

        /// <summary>速度倍率（1 = 跑，0.5 = 走）。低于 0.01 时按下限钳制（防"速度为 0 卡死移动"）。</summary>
        public float SpeedScale
        {
            get { return _speedScale; }
            set { _speedScale = value < 0.01f ? 0.01f : value; }
        }

        /// <summary>当前实际移动速度（格/秒）= `GameConst.PlayerWalkSpeed × SpeedScale`。</summary>
        public float Speed => GameConst.PlayerWalkSpeed * _speedScale;

        /// <summary>本局累计「受阻停下」次数（自证输出用）。</summary>
        public int BlockedStops { get; private set; }

        /// <summary>本次/上次路径长度（含起终点）。</summary>
        public int LastSteps => _lastSteps;

        /// <summary>上一次成功到达实际用的帧数（自证输出用）。</summary>
        public int LastArriveFrames => _lastFrames;

        /// <summary>当前移动目标格（`HasTarget == false` 时无意义）。</summary>
        public Vector2Int TargetGrid => _targetGrid;

        /// <summary>是否有移动目标。</summary>
        public bool HasTarget => _hasTarget;

        /// <summary>剩余路点数（含尚未进入的当前目标格）。</summary>
        public int RemainingWaypoints => _path == null ? 0 : Mathf.Max(0, _path.Count - _pathIndex);

        // ═════════════════════════════════════════════════════════════════════
        // 对外
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>
        /// 设路径并开始移动。`path` 必须含起点与终点（`CloverEngine.AStar` 的约定）。
        /// 返回 false = 路径非法（空/单点），此时不动且已打日志。
        /// </summary>
        public bool SetPath(List<Vector2Int> path, Vector2Int target)
        {
            if (path == null || path.Count <= 1)
            {
                PlayerLog.Warn($"SetPath：路径为 null 或不足 2 个点（{(path == null ? "null" : path.Count.ToString())}）" +
                               $"⇒ 不移动（from=({_grid.x},{_grid.y}) to=({target.x},{target.y})）");
                return false;
            }

            _path = path;
            _pathIndex = 1;                    // path[0] 就是当前格，跳过
            _pathStart = _grid;
            _targetGrid = target;
            _hasTarget = true;
            _tickFrames = 0;
            _lastSteps = path.Count;
            _blockedLogged = false;
            return true;
        }

        /// <summary>停止移动（清空路径）。返回是否真的有路径被清掉。</summary>
        public bool Stop()
        {
            if (_path == null)
            {
                _hasTarget = false;
                return false;
            }

            var had = _path.Count - _pathIndex;
            _path = null;
            _pathIndex = 0;
            _hasTarget = false;
            PlayerLog.Info($"停止移动：清空剩余 {had} 个路点（当前格 ({_grid.x},{_grid.y}) 朝向 {_dir}）");
            return true;
        }

        /// <summary>
        /// 每帧推进（由 `PlayerModule.Tick` 转发）。`map` 为 null / 未生成时只打一次日志并原地不动。
        /// </summary>
        public void Tick(float dt, IMapModule map)
        {
            if (_path == null) return;
            if (dt <= 0f) return;               // 暂停（timeScale=0 ⇒ dt=0）：不推进、不刷屏

            if (map == null || !map.IsGenerated)
            {
                if (!_warnedNoMap)
                {
                    _warnedNoMap = true;
                    PlayerLog.Warn("移动中地图不可用（IMapModule 为 null 或未 Generate）⇒ 停止移动；" +
                                   "集成时确认 Flow 已先调 ctx.Map.Generate");
                }
                Stop();
                return;
            }

            _tickFrames++;
            var budget = GameConst.PlayerWalkSpeed * _speedScale * dt;   // 单位：格（跑/走倍率见 SpeedScale）

            var guard = 0;
            while (_pathIndex < _path.Count && guard++ < MaxWaypointsPerTick)
            {
                var next = _path[_pathIndex];
                var prev = _path[_pathIndex - 1];
                _dir = Iso.DirectionTo(prev, next);

                // ① 每步校验：目标格必须可走（路点是格中心；跨格边界前先判，保证不穿墙）
                if (!map.Walkable(next))
                {
                    BlockedStop(
                        $"下一格不可走（{(next.x)},{next.y}）—— 路点被改变/地图已换？", next, map);
                    return;
                }

                // ② 斜向步：两侧格也要可走（与 `CloverEngine.AStar` 的移动规则同口径）
                if (next.x != prev.x && next.y != prev.y)
                {
                    var sideA = new Vector2Int(prev.x + (next.x - prev.x), prev.y);
                    var sideB = new Vector2Int(prev.x, prev.y + (next.y - prev.y));
                    if (!map.Walkable(sideA) || !map.Walkable(sideB))
                    {
                        BlockedStop(
                            $"斜向穿角（两侧格 {sideA}/{sideB} 有不可走）—— 拒绝穿墙", next, map);
                        return;
                    }
                }

                var target = CellCenter(next);
                var delta = target - _pos;
                var dist = delta.magnitude;

                // ── ★ R1-D：移动积分口径（用户投诉「人物移动抖动」的真根因之一）──────────────
                // 旧口径（**已修掉**）：`dist <= ArriveEpsilon(0.08)` 时先不推进，随后 `_pos = target`
                //   **吸到格心却不扣预算** ⇒ 吸过去的那段（(0, 0.08] 格）是**白送**的位移，于是"落格"
                //   那一帧的位移 = 白送量 + 预算(speed×dt)，最多达 **2× speed×dt**
                //   （实测跑 speed=3、dt=1/60：0.05 → 0.10 格；走 speed=1.4：0.028 → 0.056 格）。
                //   而 dt=1/60 时预算 0.05 < eps 0.08 ⇒ **每过一个路点必然走这条分支**，
                //   即"每走一格，必有一帧位移翻倍"⇒ 观感 = 每格顿一下 / 人物发抖（与帧率无关）。
                // 新口径：**能走到格心就走到并同步扣预算**（位移恒 ≤ speed×dt）；
                //   落点仍用**赋值**（不是累加）⇒ 浮点误差不累积、终点逐帧可复现（断言 §15 b）。
                //   `GameConst.ArriveEpsilon` 不再参与积分（它仍是 `Arrive()` 的位置校验阈值）。
                // 离线断言：`tools/probes/hosts/playercheck` §15 a（每帧位移 ≤ speed×dt、相邻位移不反向）。
                if (dist <= budget)
                {
                    _pos = target;
                    budget -= dist;
                    _pathIndex++;
                    if (!_budgetLogged) LogBudgetOnce();
                    continue;                      // 本帧还有预算就继续走下一个路点
                }

                // 预算不够走到格心 ⇒ 按剩余预算推进（位移恒 ≤ speed×dt），本帧结束
                if (budget > 0f)
                {
                    _pos += delta * (budget / dist);
                    budget = 0f;
                }

                break;                             // 预算用尽，下一帧继续
            }

            SyncTransform();

            if (_pathIndex >= _path.Count)
            {
                Arrive();
            }
        }

        /// <summary>直接落到某格（传送/进图/读档/复活用，不做寻路）。不可走则退回出生点并 Warn。</summary>
        public void Teleport(Vector2Int grid, IMapModule map)
        {
            var target = grid;
            if (map != null && map.IsGenerated && !map.Walkable(target))
            {
                PlayerLog.Warn($"Teleport 目标格不可走 ({(target.x)},{target.y}) " +
                               $"⇒ 改用出生点 ({map.SpawnPoint.x},{map.SpawnPoint.y})（读档位置非法/地图已变？）");
                target = map.SpawnPoint;
            }

            _path = null;
            _pathIndex = 0;
            _hasTarget = false;
            _blockedLogged = false;
            _warnedNoMap = false;

            _grid = target;
            _pos = CellCenter(target);
            _world = Iso.GridToWorld(target);
        }

        // ⛔ 这里**没有**「朝某方向走一格」的入口：原版 D2 只有鼠标点地面移动
        //   ⇒ 按全局 skill §0「A 没有 ⇒ 不加」整体删除（验收表 U-1；本项目 bug 表 B35）。
        //   移动的唯一入口仍是点击/按住下发的 `MoveTo`（A* 寻路）。

        /// <summary>复位（回主菜单/换角色）。</summary>
        public void Reset()
        {
            _grid = Vector2Int.zero;
            _pos = CellCenter(Vector2Int.zero);
            _world = Iso.GridToWorld(Vector2Int.zero);
            _dir = Dir8.S;
            _path = null;
            _pathIndex = 0;
            _hasTarget = false;
            _speedScale = 1f;                 // 复位回默认「跑」
            _tickFrames = 0;
            _lastFrames = 0;
            _lastSteps = 0;
            BlockedStops = 0;
            _blockedLogged = false;
            _warnedNoMap = false;
            _budgetLogged = false;
        }

        /// <summary>
        /// ★ R1-D（只报一次）：**移动积分口径**——给下一批进 Play 当数值证据用。
        /// 写清"生效口径"：每帧位移 ≤ speed×dt；落格心用赋值（不累加）但**同步扣预算**。
        /// </summary>
        private void LogBudgetOnce()
        {
            _budgetLogged = true;
            PlayerLog.Info(
                "[R1-D] 移动积分口径：每帧位移 ≤ speed×dt（跑 3.0 / 走 1.4 格/秒 × dt；" +
                $"dt=1/60 ⇒ 跑 {GameConst.PlayerWalkSpeed / 60f:0.####} 格 / 走 {GameConst.PlayerWalkSpeed * GameConst.PlayerWalkSpeedFactor / 60f:0.####} 格）；" +
                "走到格心用赋值（不累加 ⇒ 浮点不累积、终点可复现）且**同步扣预算** ⇒ 已消除旧口径" +
                $"「吸格心(≤ArriveEpsilon {GameConst.ArriveEpsilon})不扣预算」造成的单帧位移翻倍（最多 2× speed×dt，每格一次 = 移动发抖）。" +
                "断言：tools/probes/hosts/playercheck §15 a；变 dt 终点一致性见 §15 b");
        }

        // ═════════════════════════════════════════════════════════════════════
        // 内部
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>一帧内把 `_pos/_grid/_world` 同步（`_grid` 用 `Iso.WorldToGrid`，负数走 Floor）。</summary>
        private void SyncTransform()
        {
            _world = WorldOf(_pos);
            var g = Iso.WorldToGrid(_world);
            if (g != _grid)
            {
                _grid = g;
            }
        }

        /// <summary>到达终点：记帧数、打日志（验收要抄的 `[Move] arrived steps=… frames=…`）、清路径。</summary>
        private void Arrive()
        {
            _lastFrames = _tickFrames;
            if (Vector2.Distance(_pos, CellCenter(_targetGrid)) > GameConst.ArriveEpsilon)
            {
                // 理论上到不了这里（说明路径被中途改过）：把位置吸到目标格并留下日志
                PlayerLog.Warn($"[Move] 路径走完但位置 ({_pos.x:0.00},{_pos.y:0.00}) 未到目标格中心 " +
                               $"⇒ 吸附到 ({_targetGrid.x},{_targetGrid.y})");
                _pos = CellCenter(_targetGrid);
                SyncTransform();
            }

            _grid = Iso.WorldToGrid(_world);
            PlayerLog.Move(
                $"arrived steps={_lastSteps} frames={_lastFrames} " +
                $"from=({_pathStart.x},{_pathStart.y}) to=({_targetGrid.x},{_targetGrid.y}) dir={_dir}");

            _path = null;
            _pathIndex = 0;
            _hasTarget = false;
        }

        /// <summary>受阻停下：只记一次日志（带起终点与当前格），清路径但**保持当前位置**。</summary>
        private void BlockedStop(string why, Vector2Int blocked, IMapModule map)
        {
            BlockedStops++;
            if (!_blockedLogged)
            {
                _blockedLogged = true;
                PlayerLog.Warn(
                    $"移动受阻：{why}；当前格=({_grid.x},{_grid.y}) 目标=({_targetGrid.x},{_targetGrid.y}) " +
                    $"受阻格=({blocked.x},{blocked.y}) 该格地形={map.TileAt(blocked)} 剩余路点={RemainingWaypoints} " +
                    "⇒ 停在原地（不穿墙）");
            }

            _path = null;
            _pathIndex = 0;
            _hasTarget = false;
            _world = Iso.GridToWorld(_grid);      // 停在格中心上，避免卡在格边界
            _pos = CellCenter(_grid);
        }

        /// <summary>格中心（cell-center 空间）。</summary>
        private static Vector2 CellCenter(Vector2Int g) => new Vector2(g.x + 0.5f, g.y + 0.5f);

        /// <summary>
        /// 连续格坐标 → 世界（等距投影；与 `Iso.GridToWorld` 在格中心处逐项相等）。
        /// 见文件头「坐标系」。
        /// </summary>
        public static Vector3 WorldOf(Vector2 cellCenter)
        {
            return new Vector3(
                (cellCenter.x - cellCenter.y) * Iso.HalfW,
                -(cellCenter.x + cellCenter.y) * Iso.HalfH,
                0f);
        }

        /// <summary>世界坐标 → 连续格坐标（`Iso.WorldToGridContinuous` 的孪生，供自检核对）。</summary>
        public static Vector2 CellCenterOf(Vector3 world) => Iso.WorldToGridContinuous(world);
    }
}
