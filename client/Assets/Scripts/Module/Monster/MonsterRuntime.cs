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

        // ── 位置 ─────────────────────────────────────────────────────────────
        /// <summary>连续格坐标（格中心制，见文件头）。</summary>
        public Vector2 Pos;

        /// <summary>当前朝向（8 方向）。</summary>
        public Dir8 Dir = Dir8.S;

        // ── 路径（走 `IMapModule.FindPath`，本类只**沿路走**）──────────────────
        /// <summary>剩余路径（格）；null = 无路径。</summary>
        public List<Vector2Int> Path;

        /// <summary>下一个要到达的路点下标。</summary>
        public int PathIndex;

        /// <summary>上次寻路的目标格（用于"目标格变化 &gt; 1 才重算"的节流）。</summary>
        public Vector2Int PathTarget;

        /// <summary>是否已经寻过一次路（`PathTarget` 是否有效）。</summary>
        public bool HasPathTarget;

        /// <summary>距离下次允许重寻路的秒数（路径节流）。</summary>
        public float RepathTimer;

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

        /// <summary>落到某格中心（进图/复活/刷怪用），并清空路径。</summary>
        public void SnapTo(Vector2Int g)
        {
            Pos = Center(g);
            Path = null;
            PathIndex = 0;
            HasPathTarget = false;
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

        /// <summary>设为路径（跳过起点格；`FindPath` 的返回值含起点）。</summary>
        public void SetPath(List<Vector2Int> path, Vector2Int target)
        {
            PathTarget = target;
            HasPathTarget = true;
            RepathTimer = MonsterTuning.RepathIntervalSeconds;

            if (path == null || path.Count <= 1)
            {
                Path = null;
                PathIndex = 0;
                return;
            }

            Path = path;
            PathIndex = 1;      // 第 0 个是当前格，不用"到达"
        }

        /// <summary>丢弃当前路径。</summary>
        public void ClearPath()
        {
            Path = null;
            PathIndex = 0;
        }

        /// <summary>当前路径是否还剩余路点。</summary>
        public bool HasRemainingPath
        {
            get { return Path != null && PathIndex < Path.Count; }
        }

        /// <summary>
        /// 沿路径推进 <paramref name="tilesPerSecond"/> × <paramref name="dt"/> 格。
        /// </summary>
        /// <returns>true = 路径已走完（或本来就没有路径）。</returns>
        public bool Advance(float tilesPerSecond, float dt)
        {
            if (!HasRemainingPath) return true;

            var speed = tilesPerSecond < MonsterTuning.MinMoveSpeed ? MonsterTuning.MinMoveSpeed : tilesPerSecond;
            var step = speed * dt;
            if (step <= 0f) return false;

            var fromGrid = Grid;

            while (step > 0f && PathIndex < Path.Count)
            {
                var wp = Center(Path[PathIndex]);
                var d = wp - Pos;
                var len = d.magnitude;

                if (len <= 1e-4f)
                {
                    PathIndex++;
                    continue;
                }

                if (len <= step)
                {
                    Pos = wp;
                    step -= len;
                    PathIndex++;
                }
                else
                {
                    Pos = Pos + d * (step / len);
                    step = 0f;
                }
            }

            UpdateDir(fromGrid);

            if (PathIndex >= Path.Count)
            {
                Path = null;
                PathIndex = 0;
                return true;
            }
            return false;
        }

        /// <summary>
        /// 朝某点**直线**走一步（逃跑/紧急脱身用；不做寻路）。
        /// </summary>
        /// <returns>true = 已到达（距离 &lt; 0.05 格）。</returns>
        public bool StepToward(Vector2 target, float tilesPerSecond, float dt)
        {
            var speed = tilesPerSecond < MonsterTuning.MinMoveSpeed ? MonsterTuning.MinMoveSpeed : tilesPerSecond;
            var step = speed * dt;
            var d = target - Pos;
            var len = d.magnitude;
            if (len <= 0.05f) return true;

            var fromGrid = Grid;
            Pos = len <= step ? target : Pos + d * (step / len);
            UpdateDir(fromGrid);
            return false;
        }

        /// <summary>按"格"变化更新朝向（**零增量不调 `Iso.DirectionTo`**：它会打限频日志）。</summary>
        private void UpdateDir(Vector2Int fromGrid)
        {
            var to = Grid;
            if (to.x == fromGrid.x && to.y == fromGrid.y) return;
            Dir = Iso.DirectionTo(fromGrid, to);
        }
    }
}
