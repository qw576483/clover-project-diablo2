// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · Module/Monster/MonsterRuntime.cs
// **一只怪物的运行时记录**（AI 状态 + 连续位置 + 路径 + 计时器）。
//
// 为什么要有它（而不是直接用 `Def.MonsterState`）：
//   `MonsterState` 是**冻结的 DTO**（`Module/Contracts.cs`），只有"对外可见"的字段；
//   AI 需要的东西（连续坐标、剩余路径、重寻路节流、仇恨剩余时长、受击硬直、复活冷却…）
//   **不能**塞进 DTO（改 DTO = 改契约）。⇒ 运行时状态放这里，`State` 只做"对外快照"，
//   由 `Sync()` 在每次变化后写回（`gridX/gridY/worldX/Y/Z/dir/hitStun/attacking`）。
//
// 坐标口径（与 `Core/Iso` **完全同一套**，只是输入允许小数）：
//   格 (gx,gy) 覆盖 [gx,gx+1]×[gy,gy+1]，**中心** = (gx+0.5, gy+0.5)；
//   `world.x = (px - py) * Iso.HalfW`，`world.y = -(px + py + 1) * Iso.HalfH`。
//   当 `pos == (gx+0.5, gy+0.5)` 时，其结果**逐位等于** `Iso.GridToWorld(gx, gy)`。
//
//   `CloverEngine.PathFollower`（`Runtime/Core/PathFollower.cs`）—— 本类只做**薄转发**：
//   · `Pos` / `Dir` / `Path` / `PathIndex` / `PathTarget` / `HasPathTarget` / `RepathTimer`
//     与 `SnapTo` / `SetPath` / `ClearPath` / `HasRemainingPath` / `Advance` / `StepToward`
//     **公开名字与签名一字未改**（调用点零改动），内部一律读写引擎件；
//   · 两个项目数值（`MonsterTuning.MinMoveSpeed` / `RepathIntervalSeconds`）经构造参数注入。
// ─────────────────────────────────────────────────────────────────────────────

using System.Collections.Generic;
using Diablo2.Core;
using Diablo2.Def;
using UnityEngine;

namespace Diablo2.Module.Monster
{
    /// <summary>怪物的运行时状态（AI 私有；`State` 是对外 DTO）。</summary>
    internal sealed class MonsterRuntime
    {
        // ── 静态身份 ─────────────────────────────────────────────────────────
        /// <summary>对外快照（引用类型，原地更新；视图/UI 不要缓存副本）。</summary>
        public MonsterState State;

        /// <summary>配表行（`monster_c`；速度/群数/掉落/抗性都从这里读，不硬编码）。</summary>
        public Table.BaseMonsterRow Row;

        /// <summary>刷出该怪的区域（`MonsterState` 没有区域字段，任务计数靠它）。</summary>
        public AreaId Area;

        /// <summary>刷新点（脱战/遗忘后回位的"家"）。</summary>
        public Vector2Int Home;

        /// <summary>
        /// **生效抗性**（下标 = `(int)Def.DamageType`，**6 系**：物/火/冰/电/毒/魔）=
        /// `monster_c.res_*` + 精英词缀 `monumod_c.res_*`（已按官方上限 75% 夹取）。
        /// <para>
        /// 为什么放在运行时而不是 `MonsterState`：`MonsterState` 是冻结 DTO，没有抗性字段；
        /// 而抗性是"这只怪的属性"（含词缀加成），必须在刷怪时算定，不能每帧回查配表。
        /// </para>
        /// </summary>
        public int[] Resists;

        /// <summary>取某系生效抗性（未设置时按 0）。</summary>
        public int ResistOf(DamageType type)
        {
            if (Resists == null) return 0;
            var i = (int)type;
            if (i < 0 || i >= Resists.Length) return 0;
            return Resists[i];
        }

        // ── 路径跟随（**实现已下沉引擎** `CloverEngine.PathFollower`；本类只做薄转发）──────
        /// <summary>
        /// 引擎的等距布局（**只为取朝向**：`PathFollower` 只调它的 `DirectionTo`，
        /// 不读半格尺寸 —— 半格尺寸是项目语义，由 `Iso.HalfW/HalfH` 给）。
        /// </summary>
        private static readonly CloverEngine.IsoLayout DirectionLayout = new CloverEngine.IsoLayout(
            Iso.HalfW, Iso.HalfH, GameConst.SortOrderStep, GameConst.SortOrderBase);

        /// <summary>
        /// 引擎路径跟随器：连续坐标 / 朝向 / 剩余路径 / 路径目标 / 重寻路计时**全在它里面**。
        /// <para>
        /// `Advance` / `StepToward` / `SetPath` / `ClearPath` / 朝向更新的算法**一字未改**地搬到了
        /// 引擎 `Runtime/Core/PathFollower.cs`（那边是唯一真相）；本类只保留同样的公开名字转发。
        /// 两个项目数值经构造参数注入（引擎不预设任何业务数值）。
        /// </para>
        /// </summary>
        private readonly CloverEngine.PathFollower _follow = new CloverEngine.PathFollower(
            DirectionLayout, MonsterTuning.MinMoveSpeed, MonsterTuning.RepathIntervalSeconds);

        // ── 位置 ─────────────────────────────────────────────────────────────
        /// <summary>连续格坐标（格中心制，见文件头；**存储与推进在引擎 `PathFollower` 里**）。</summary>
        public Vector2 Pos
        {
            get { return _follow.Pos; }
            set { _follow.Pos = value; }
        }

        /// <summary>
        /// 当前朝向（8 方向）。
        /// <para>引擎 `CloverEngine.Dir8` 与项目 `Diablo2.Def.Dir8` 是**逐值直转**的两个枚举
        /// （顺序同为 `S=0 SW=1 W=2 NW=3 N=4 NE=5 E=6 SE=7`，见 `Core/Iso.cs` 的映射说明）——
        /// 这里只转值，不做任何档位偏移。</para>
        /// </summary>
        public Dir8 Dir
        {
            get { return (Dir8)(int)_follow.Dir; }
            set { _follow.Dir = (CloverEngine.Dir8)(int)value; }
        }

        // ── 路径（走 `IMapModule.FindPath`，本类只**沿路走**；存储与推进在引擎件里）──────
        /// <summary>剩余路径（格）；null = 无路径。</summary>
        public List<Vector2Int> Path
        {
            get { return _follow.Path; }
            set { _follow.Path = value; }
        }

        /// <summary>下一个要到达的路点下标。</summary>
        public int PathIndex
        {
            get { return _follow.PathIndex; }
            set { _follow.PathIndex = value; }
        }

        /// <summary>上次寻路的目标格（用于"目标格变化 &gt; 1 才重算"的节流）。</summary>
        public Vector2Int PathTarget
        {
            get { return _follow.PathTarget; }
            set { _follow.PathTarget = value; }
        }

        /// <summary>是否已经寻过一次路（`PathTarget` 是否有效）。</summary>
        public bool HasPathTarget
        {
            get { return _follow.HasPathTarget; }
            set { _follow.HasPathTarget = value; }
        }

        /// <summary>距离下次允许重寻路的秒数（路径节流）。</summary>
        public float RepathTimer
        {
            get { return _follow.RepathTimer; }
            set { _follow.RepathTimer = value; }
        }

        // ── AI 状态骨架（引擎 `Game.NewFsm()`，由 `MonsterAi` 建与驱动）────────────────
        /// <summary>
        /// 本怪**自己的**状态机（`CloverEngine.Game.NewFsm()`，专为"每单位一棵"公开）。
        /// <para>引擎的 `Game.Fsm` 是**应用级单例**（登录 / 主城 / 战斗流程），多个实体共用一个
        /// `Current` 会互相覆盖 ⇒ 不能给每只怪共用。</para>
        /// <para>骨架 = `Idle→Aggro→Chase→Attack→Return/Flee`；**行为与数值的唯一真相仍是
        /// `MonsterAi` 那 4 个 AI 函数**，状态回调只做骨架归位 / 转移留痕。</para>
        /// </summary>
        public CloverEngine.IFsm Ai;

        /// <summary>本 tick 的玩家格中心（`MonsterAi` 每 tick 写入，供 `Ai` 的状态回调读）。</summary>
        public Vector2 AiPlayerCenter;

        /// <summary>本 tick 与玩家的连续距离（同上）。</summary>
        public float AiDist;

        /// <summary>本 tick 的决策结果（由 `Ai` 的 Chase 回调写入，`MonsterAi.Step` 读出返回）。</summary>
        public MonsterAi.Action AiResult;

        // ── AI 计时器 ────────────────────────────────────────────────────────
        /// <summary>距离下次可出手的秒数。</summary>
        public float AttackTimer;

        /// <summary>仇恨记忆剩余秒数（官方行为：一段时间没挨打/没看见玩家就遗忘）。</summary>
        public float AggroMemory;

        /// <summary>当前是否处于交战（锁定玩家）。</summary>
        public bool Engaged;

        /// <summary>是否正在回原位（脱战之后）。</summary>
        public bool Returning;

        /// <summary>受击硬直剩余秒数。</summary>
        public float HitStunTimer;

        /// <summary>
        /// 脚步：从上次出脚步声起**已走的距离（格）**。
        /// 出处 = 原版 `MonSounds.FsCnt`（一个走路循环几步 ⇒ 每 `1/FsCnt` 格一步）。
        /// </summary>
        public float StepAccum;

        /// <summary>待播的**怪物自身受击音**键（`MonSounds.HitSound`；null = 没有）。</summary>
        public string PendingHitSfx;

        /// <summary>
        /// 待播受击音的剩余延迟（秒）= 原版 `MonSounds.HitDelay` **帧** ÷ `MonsterTuning.LogicFps`。
        /// </summary>
        public float PendingHitSfxTimer;

        /// <summary>攻击动画标记剩余秒数（写回 `State.attacking`）。</summary>
        public float AttackAnimTimer;

        /// <summary>尸体保留剩余秒数（到 0 回收）。</summary>
        public float CorpseTimer;

        /// <summary>萨满复活同伴的冷却剩余秒数。</summary>
        public float ReviveTimer;

        /// <summary>逃跑剩余秒数（Coward 低血时进入）。</summary>
        public float FleeTimer;

        // ── 一次性告警标记（**自己实现降频**，不依赖 `UnityEngine.Time`；
        //    见 `Module/Combat/CombatLog.cs` 文件头：`Log.WarnThrottled` 在离线自检宿主里会抛异常）
        /// <summary>已就"找不到路"告警过。</summary>
        public bool WarnedNoPath;

        /// <summary>已就"目标格非法"告警过。</summary>
        public bool WarnedBadGoal;

        // ── 视图同步 ─────────────────────────────────────────────────────────
        /// <summary>本帧是否有需要刷新的视图变化（位置/朝向/血量/动作标记）。</summary>
        public bool ViewDirty = true;

        /// <summary>当前格（连续坐标向下取整；**必须 Floor**，`constraints.md` #4）。</summary>
        public Vector2Int Grid
        {
            get { return new Vector2Int(Mathf.FloorToInt(Pos.x), Mathf.FloorToInt(Pos.y)); }
        }

        /// <summary>格中心（连续坐标）。</summary>
        public static Vector2 Center(Vector2Int g)
        {
            return new Vector2(g.x + 0.5f, g.y + 0.5f);
        }

        /// <summary>连续格坐标 → 世界坐标（与 `Iso` 同一套公式，输入允许小数）。</summary>
        public static Vector3 WorldOf(Vector2 pos)
        {
            return new Vector3((pos.x - pos.y) * Iso.HalfW, -(pos.x + pos.y + 1f) * Iso.HalfH, 0f);
        }

        /// <summary>落到某格中心（进图/复活/刷怪用），并清空路径（转发引擎件）+ 置视图脏。</summary>
        public void SnapTo(Vector2Int g)
        {
            // 引擎件的 SnapTo 做完全部四件事：Pos = Center(g) / Path = null / PathIndex = 0 / HasPathTarget = false
            _follow.SnapTo(g);
            ViewDirty = true;
        }

        /// <summary>把运行时状态写回对外 DTO（位置/朝向/动作标记）。</summary>
        public void Sync()
        {
            if (State == null) return;

            var g = Grid;
            var w = WorldOf(Pos);
            State.gridX = g.x;
            State.gridY = g.y;
            State.worldX = w.x;
            State.worldY = w.y;
            State.worldZ = w.z;
            State.dir = Dir;
            State.hitStun = HitStunTimer > 0f;
            State.attacking = AttackAnimTimer > 0f;
        }

        /// <summary>
        /// 设为路径（跳过起点格；`FindPath` 的返回值含起点）—— **转发引擎 `PathFollower`**。
        /// <para>长度 ≤ 1（起点==终点）判为"无路径"，与 `AStar.Find` 的单元素返回对齐；
        /// 同时把 `RepathTimer` 重置为注入的 `MonsterTuning.RepathIntervalSeconds`。</para>
        /// </summary>
        public void SetPath(List<Vector2Int> path, Vector2Int target)
        {
            _follow.SetPath(path, target);
        }

        /// <summary>丢弃当前路径（**不动** `PathTarget` / `HasPathTarget`，与改动前一致）。</summary>
        public void ClearPath()
        {
            _follow.ClearPath();
        }

        /// <summary>当前路径是否还剩余路点。</summary>
        public bool HasRemainingPath
        {
            get { return _follow.HasRemainingPath; }
        }

        /// <summary>
        /// 沿路径推进 <paramref name="tilesPerSecond"/> × <paramref name="dt"/> 格 —— 转发引擎件。
        /// </summary>
        /// <returns>true = 路径已走完（或本来就没有路径）。</returns>
        public bool Advance(float tilesPerSecond, float dt)
        {
            return _follow.Advance(tilesPerSecond, dt);
        }

        /// <summary>
        /// 朝某点**直线**走一步（逃跑/紧急脱身用；不做寻路）—— 转发引擎件。
        /// </summary>
        /// <returns>true = 已到达（距离 &lt; 0.05 格）。</returns>
        public bool StepToward(Vector2 target, float tilesPerSecond, float dt)
        {
            return _follow.StepToward(target, tilesPerSecond, dt);
        }
    }
}
