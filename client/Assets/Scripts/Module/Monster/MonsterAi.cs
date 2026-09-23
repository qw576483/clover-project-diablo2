// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · Module/Monster/MonsterAi.cs
// **4 种 AI 的决策逻辑**（纯逻辑，无渲染；每 tick 由 `MonsterModule` 驱动一次）。
//
//   ① `Melee`   —— 追击近战：进入 `GameConst.MeleeRange` 就出手。
//   ② `Range`   —— 保持距离射击：`< RangedKeepDistance` 就后撤，`> RangedRange` 就靠近，
//                  中间区间出手（原版尖刺鼠就是这种"放风筝"行为）。
//   ③ `Shaman`  —— 萨满：**优先复活已死的同伴**（有冷却、要吃尸体），其余时间与 `Range` 同。
//   ④ `Coward`  —— 低血逃跑（原版堕落者）：血量 ≤ `CowardFleeHpRatio` 就背向玩家逃一段时间。
//
// 硬要求（`docs/agents/agent-07-*.md` §4）：
//   1. **寻路一律走 `IMapModule.FindPath`**（本文件不实现任何寻路），并做**路径节流**：
//      已有路径 + 目标格未漂移（`> RepathOnGoalDrift` 才重算）+ 冷却未到 ⇒ 直接沿原路走。
//   2. **仇恨有时效**：一段时间没挨打/没看见玩家就遗忘（`MonsterTuning.AggroMemorySeconds`），
//      玩家离图/死亡时立刻脱战。遗忘后**回原位**（`Home`），不原地卡住。
//
// ⛔ 所有"没按预期走"的分支都留日志（找不到路 / 目标格非法 / 退无可退 / 未登记的 AI 类型）。
// ⛔ 高频分支用 `MonsterLog.WarnThrottled`（**自己想做的无时钟降频**，见该文件头）。
// ─────────────────────────────────────────────────────────────────────────────

using Diablo2.Core;
using Diablo2.Def;
using UnityEngine;

namespace Diablo2.Module.Monster
{
    /// <summary>怪物 AI 决策（4 种行为）。</summary>
    internal static class MonsterAi
    {
        /// <summary>本 tick 的决策结果（供 `MonsterModule` 统计/自证用）。</summary>
        internal enum Action
        {
            /// <summary>什么都不做（移动/等待/逃跑/复活）。</summary>
            None = 0,

            /// <summary>向玩家出手了。</summary>
            Attack = 1,
        }

        /// <summary>8 邻（与 `CloverEngine.AStar` / `GridMap` 同一套偏移，用于"挑一个更远离玩家的可走格"）。</summary>
        private static readonly Vector2Int[] Neighbors8 =
        {
            new Vector2Int(1, 0), new Vector2Int(-1, 0), new Vector2Int(0, 1), new Vector2Int(0, -1),
            new Vector2Int(1, 1), new Vector2Int(1, -1), new Vector2Int(-1, 1), new Vector2Int(-1, -1),
        };

        /// <summary>推进一只怪物一个 tick。</summary>
        public static Action Step(MonsterModule owner, MonsterRuntime m, float dt)
        {
            // ── 计时器 ──
            if (m.AttackTimer > 0f) m.AttackTimer -= dt;
            if (m.RepathTimer > 0f) m.RepathTimer -= dt;
            if (m.ReviveTimer > 0f) m.ReviveTimer -= dt;
            if (m.AttackAnimTimer > 0f)
            {
                m.AttackAnimTimer -= dt;
                if (m.AttackAnimTimer <= 0f) m.ViewDirty = true;
            }

            // ── 受击硬直：不动、不出手（`MonsterModule.ApplyDamage` 设置）──
            if (m.HitStunTimer > 0f)
            {
                m.HitStunTimer -= dt;
                if (m.HitStunTimer <= 0f) m.ViewDirty = true;
                return Action.None;
            }

            var player = owner.Player;
            var hasPlayer = player != null && !player.IsDead;
            var playerCenter = MonsterRuntime.Center(owner.PlayerGrid);
            var dist = Vector2.Distance(m.Pos, playerCenter);

            UpdateEngagement(owner, m, hasPlayer, dist, dt);

            if (!m.Engaged)
            {
                if (m.Returning) ReturnHome(owner, m, dt);
                return Action.None;
            }

            switch (m.State.ai)
            {
                case MonsterAI.Melee:
                    return Melee(owner, m, playerCenter, dist, dt);

                case MonsterAI.Range:
                    return Ranged(owner, m, playerCenter, dist, dt);

                case MonsterAI.Shaman:
                    return Shaman(owner, m, playerCenter, dist, dt);

                case MonsterAI.Coward:
                    return Coward(owner, m, playerCenter, dist, dt);

                default:
                    MonsterLog.WarnThrottled("ai.unknown",
                        $"m#{m.State.id} {m.State.name} 的 AI 类型 {(int)m.State.ai} 未登记" +
                        "（契约只有 Melee/Range/Shaman/Coward）⇒ 按 Melee 处理");
                    return Melee(owner, m, playerCenter, dist, dt);
            }
        }

        // ═════════════════════════════════════════════════════════════════════
        // 仇恨：进入 / 记忆 / 遗忘 / 脱战
        // ═════════════════════════════════════════════════════════════════════
        private static void UpdateEngagement(MonsterModule owner, MonsterRuntime m, bool hasPlayer, float dist, float dt)
        {
            if (!hasPlayer)
            {
                // 玩家死亡 / 模块未接入：官方行为是怪物停止追击（玩家尸体不挨打）
                if (m.Engaged)
                {
                    MonsterLog.Info($"disengage m#{m.State.id} {m.State.name}：玩家不可用（死亡/未接入）⇒ 脱战回原位");
                    Disengage(m);
                }
                m.AggroMemory = 0f;
                return;
            }

            if (m.Engaged)
            {
                // ① 超过"栓绳半径"：立刻脱战（官方：跑远了就放弃）
                if (dist > GameConst.MonsterLeashRange)
                {
                    MonsterLog.Info($"leash m#{m.State.id} {m.State.name}：距离 {dist:0.0} > " +
                                    $"{GameConst.MonsterLeashRange:0.0} ⇒ 脱战回原位 {m.Home}");
                    Disengage(m);
                    return;
                }

                // ② 玩家仍在"发现半径"内（或刚被打过 ⇒ `NotifyAttacked` 已把记忆刷满）⇒ 续上记忆。
                //    ⚠️ 这里**必须**刷新：否则速度慢的怪（僵尸 speed=1）在 7 格外追击时会在半路
                //    因为记忆耗尽而"遗忘"，来回拉锯永远到不了玩家身边（实测踩到过）。
                if (dist <= GameConst.MonsterAggroRange)
                {
                    m.AggroMemory = MonsterTuning.AggroMemorySeconds;
                    return;
                }

                // ③ 玩家跑出发现半径但还没到栓绳上限：记忆开始衰减 ⇒ 一段时间后遗忘（"仇恨有时效"）
                m.AggroMemory -= dt;
                if (m.AggroMemory <= 0f)
                {
                    MonsterLog.Info($"forget m#{m.State.id} {m.State.name}：玩家已跑出 {GameConst.MonsterAggroRange:0.0} 格" +
                                    $"且 {MonsterTuning.AggroMemorySeconds:0.0}s 内没再挨打 ⇒ 仇恨遗忘，回原位 {m.Home}");
                    Disengage(m);
                }
                return;
            }

            if (dist <= GameConst.MonsterAggroRange)
            {
                m.Engaged = true;
                m.Returning = false;
                m.AggroMemory = MonsterTuning.AggroMemorySeconds;
                m.ClearPath();
                m.WarnedNoPath = false;
                MonsterLog.Info($"aggro m#{m.State.id} {m.State.name}（{m.State.ai}）：发现玩家" +
                                $"，距离 {dist:0.0} ≤ {GameConst.MonsterAggroRange:0.0} ⇒ 进入仇恨");
            }
        }

        /// <summary>脱战（遗忘/超出栓绳/玩家不可用）：清路径、置回位标记。</summary>
        private static void Disengage(MonsterRuntime m)
        {
            m.Engaged = false;
            m.AggroMemory = 0f;
            m.Returning = true;
            m.FleeTimer = 0f;
            m.ClearPath();
        }

        /// <summary>回原位（脱战后）；到达即停。</summary>
        private static void ReturnHome(MonsterModule owner, MonsterRuntime m, float dt)
        {
            if (m.Grid == m.Home)
            {
                m.Returning = false;
                m.ClearPath();
                return;
            }
            if (!TryPathTo(owner, m, m.Home)) return;
            m.Advance(owner.SpeedOf(m), dt);
        }

        // ═════════════════════════════════════════════════════════════════════
        // ① Melee / ② Range / ③ Shaman / ④ Coward
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>
        /// **出手判定距离** —— 必须与**结算层同一把尺子**。
        /// <para>
        /// 结算侧 `CombatModule.RequestMonsterAttack`（`Module/Combat/CombatModule.cs:408-421`）用的是
        /// `Iso.GridDistanceEuclidean(player.Grid, monsterGrid)`，即**格心到格心**；
        /// 而 `MonsterRuntime.Pos` 是**连续**格坐标（`Advance` 会把它推进到两格之间）。
        /// </para>
        /// <para>
        /// ★ 片 melee-ai-why 的缺陷根因（实测）：僵尸沿轴逼近，连续距离停在 **1.60**（= `GameConst.MeleeRange`）
        /// 时所在格距玩家 **2 格**（`(8,58)` vs `(8,56)` ⇒ 格欧氏 **2.00**）⇒
        /// AI 认为"已在射程内"⇒ `TryAttack` ⇒ 结算层判 `2.00 > 1.60` **拒绝**；
        /// 而 AI 自己那条已满足，于是**永不再靠近**（死锁）⇒ 怪物一辈子打不到玩家。
        /// </para>
        /// <para>
        /// ⇒ 出手判定统一改用 <see cref="AttackDistance"/>；移动/仇恨仍用连续距离（那是观感/感知量，不是契约量）。
        /// </para>
        /// </summary>
        private static float AttackDistance(MonsterModule owner, MonsterRuntime m)
            => Iso.GridDistanceEuclidean(owner.PlayerGrid, m.Grid);

        /// <summary>远程/萨满的出手距离上限 —— 与 `CombatModule.cs:418-420` 逐字同式（近战不受影响）。</summary>
        private static float RangedAttackDistanceCap
            => Mathf.Min(GameConst.RangedRange, MonsterTuning.RangedAttackMaxRange);

        /// <summary>
        /// **出手前自检：本次攻击的线段是否通畅** —— 与结算层**同一把尺子**。
        /// <para>
        /// 尺子本体 = `Diablo2.Module.Combat.CombatModule.AttackLineClear`（内部就是
        /// `MeleeShape.LineClear` + 同一份 `WalkableProbe`），⛔ 本文件**不另写一份**判定。
        /// 跨模块只取这一句纯判定、不 `using` 对方的具体类型（与 `SkillModule` 取
        /// `Combat.DamagePipeline` 同一写法，见 `DamagePipeline.cs` 文件头）。
        /// </para>
        /// <para>
        /// ★ 片 lineclear-fix 的缺陷（前片 `melee-ai-why` §7 登记③，用户可见"怪物隔墙反复挥空"）：
        /// 结算层 `RequestMonsterAttack` 判"线段被不可走地形阻断"时**只打日志就 return**，
        /// 而 `TryAttack` 在**发起前**已经把出手动画 / 出手音效 / 出手计时器都写好了
        /// （`m.AttackTimer` / `m.AttackAnimTimer` / `m.ViewDirty`，见 `TryAttack`）⇒
        /// 怪每 `AttackIntervalSeconds` 挥一次空、且**永远不知道**自己被拒（死循环）。
        /// 现在出手前先用同一把尺子自检：不通 ⇒ **不发起**，改走"绕路"（原版近战怪够不着时
        /// 不会原地挥空）。
        /// </para>
        /// <para>
        /// ⛔ 这不是"把 `LineClear` 放宽"（那是让怪穿墙打人，与原版语义相反）：判据一字未动，
        /// 只是把"结算层的拒绝"提前到"发起前"，两侧仍然同一句。
        /// </para>
        /// <para>
        /// **原版口径（出处）**：`原版资源/d2lod1.10txt-1.10f/data/global/excel/MonAi.txt` 的 AI 指令序列里，
        /// **"出手"之前的第一个指令一律是"接近"** —— 第 5 行 `Zombie` = `approach? | aware dist | | att1/att2?`、
        /// 第 4 行 `Skeleton` = `approach? | stall time | attack? | att1/att2?`、
        /// 第 8 行 `Fallen` = `cmd:attack? | | attack? | att1/att2?`
        /// （怪 → AI 名的映射在 `MonStats.txt` 的 AI 列）。序列里**没有**"隔着障碍发起攻击"这一支 ⇒
        /// "线段被地形阻断时**不发起**、继续接近（绕路）"与原版指令序列一致。
        /// </para>
        /// </summary>
        private static bool AttackLineClear(MonsterModule owner, MonsterRuntime m)
            => Diablo2.Module.Combat.CombatModule.AttackLineClear(m.Grid, owner.PlayerGrid);

        /// <summary>① 近战：贴上去打。</summary>
        private static Action Melee(MonsterModule owner, MonsterRuntime m, Vector2 playerCenter, float dist, float dt)
        {
            if (AttackDistance(owner, m) <= GameConst.MeleeRange)
            {
                // ★ 片 lineclear-fix：够得着还不够 —— **线段必须通畅**（与结算层同一把尺子）。
                //   隔墙/隔水时不发起（否则每次都在结算层被拒 = 挥空），落到下面**绕路**那一支。
                if (AttackLineClear(owner, m)) return TryAttack(owner, m);
            }

            var goal = owner.PlayerGrid;
            if (!TryPathTo(owner, m, goal)) return Action.None;
            m.Advance(owner.SpeedOf(m), dt);
            return Action.None;
        }

        /// <summary>② 远程：保持 `RangedKeepDistance` ~ `RangedRange` 的射击距离。</summary>
        private static Action Ranged(MonsterModule owner, MonsterRuntime m, Vector2 playerCenter, float dist, float dt)
        {
            if (dist < MonsterTuning.RangedKeepDistance)
            {
                if (StepAway(owner, m, playerCenter, dt)) return Action.None;
                // 退无可退（角落里）：只能就地射击，而不是站着不动被白打
                // ★ 片 lineclear-fix：**仍要过线段那一关**（与结算层同一把尺子）——
                //   被墙挡住时"就地射击"同样会被结算层拒 ⇒ 不发起（原版怪物不隔墙放枪）。
                if (AttackLineClear(owner, m)) return TryAttack(owner, m);
                return Action.None;
            }

            // ★ 片 melee-ai-why：靠近的上限改成**结算层实际放行的距离**（`RangedAttackDistanceCap`，
            //   与 `CombatModule.cs:418-420` 同式）。旧写法用 `GameConst.RangedRange`(8) ⇒ 怪在
            //   5 < 格距 ≤ 8 时会"反复请求出手 → 每次被结算层以 `> 射程 5.00` 拒绝，又因为
            //   自己那条不满足靠近条件而**永远不靠近**" ⇒ 站着不动、一枪不发的死区。
            // ★ 片 lineclear-fix：**同一族**的第二个死区 —— 格距已在射程内但**线段被地形挡住**
            //   （隔墙/隔水）时，旧写法照常 `TryAttack` ⇒ 每次被结算层拒、又永不移动（站着放空枪）。
            //   现在"够不着 **或** 看不见"一律走**靠近**那一支（挪到有视线的格子）。
            if (AttackDistance(owner, m) > RangedAttackDistanceCap || !AttackLineClear(owner, m))
            {
                if (!TryPathTo(owner, m, owner.PlayerGrid)) return Action.None;
                m.Advance(owner.SpeedOf(m), dt);
                return Action.None;
            }

            return TryAttack(owner, m);
        }

        /// <summary>③ 萨满：优先复活同伴；其余时间按远程节奏施法。</summary>
        private static Action Shaman(MonsterModule owner, MonsterRuntime m, Vector2 playerCenter, float dist, float dt)
        {
            if (m.ReviveTimer <= 0f)
            {
                var corpseId = owner.FindRevivableCorpse(m);
                if (corpseId >= 0)
                {
                    if (owner.ReviveMonster(corpseId, m)) m.ReviveTimer = MonsterTuning.ShamanReviveCooldownSeconds;
                    return Action.None;      // 复活是"施法"，本 tick 不再移动/攻击
                }
            }

            if (dist < MonsterTuning.RangedKeepDistance && StepAway(owner, m, playerCenter, dt))
                return Action.None;

            // ★ 片 melee-ai-why：同 ② 远程 —— 靠近上限对齐结算层（`RangedAttackDistanceCap`）。
            // ★ 片 lineclear-fix：同 ② —— "够不着 **或** 看不见"一律靠近（别隔墙放法术）。
            if (AttackDistance(owner, m) > RangedAttackDistanceCap || !AttackLineClear(owner, m))
            {
                if (!TryPathTo(owner, m, owner.PlayerGrid)) return Action.None;
                m.Advance(owner.SpeedOf(m), dt);
                return Action.None;
            }

            return TryAttack(owner, m);
        }

        /// <summary>④ 逃跑型：低血逃跑（原版堕落者），否则**等同近战**。</summary>
        private static Action Coward(MonsterModule owner, MonsterRuntime m, Vector2 playerCenter, float dist, float dt)
        {
            if (m.FleeTimer > 0f)
            {
                m.FleeTimer -= dt;
                StepAway(owner, m, playerCenter, dt);
                return Action.None;
            }

            var ratio = m.State.maxHp > 0 ? (float)m.State.hp / m.State.maxHp : 0f;
            if (ratio <= MonsterTuning.CowardFleeHpRatio)
            {
                m.FleeTimer = MonsterTuning.CowardFleeSeconds;
                m.ClearPath();
                MonsterLog.Info($"flee m#{m.State.id} {m.State.name}：hp {m.State.hp}/{m.State.maxHp}" +
                                $"（{ratio * 100f:0}%）≤ {MonsterTuning.CowardFleeHpRatio * 100f:0}% ⇒ 背向玩家逃跑" +
                                $"（持续 {MonsterTuning.CowardFleeSeconds:0.0}s）");
                StepAway(owner, m, playerCenter, dt);
                return Action.None;
            }

            // ★ 片 melee-ai-why：非逃跑路径 = "等同近战" ⇒ 出手判定必须与 ① 同一把尺子。
            // ★ 片 lineclear-fix：同 ① —— 线段不通不发起（落到下面的绕路支）。
            if (AttackDistance(owner, m) <= GameConst.MeleeRange && AttackLineClear(owner, m))
                return TryAttack(owner, m);

            if (!TryPathTo(owner, m, owner.PlayerGrid)) return Action.None;
            m.Advance(owner.SpeedOf(m), dt);
            return Action.None;
        }

        // ═════════════════════════════════════════════════════════════════════
        // 移动 / 出手
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>
        /// 挑一个"比当前格更远离玩家"的可走 8 邻格并走一步（后撤/逃跑共用）。
        /// 只在 8 邻里挑 ⇒ **绝不会把非法格喂给 `FindPath`**（`CloverEngine.AStar` 对非法入参会打限频日志）。
        /// </summary>
        /// <returns>true = 移动了一步；false = 退无可退。</returns>
        private static bool StepAway(MonsterModule owner, MonsterRuntime m, Vector2 playerCenter, float dt)
        {
            var map = owner.Map;
            if (map == null || !map.IsGenerated)
            {
                MonsterLog.WarnOnce("ai.nomap.stepaway", "StepAway: 地图未就绪（AppContext.Map == null / 未生成）⇒ 怪物无法后撤");
                return false;
            }

            var cur = m.Grid;
            var best = cur;
            var bestDist = Vector2.Distance(MonsterRuntime.Center(cur), playerCenter);

            for (var i = 0; i < Neighbors8.Length; i++)
            {
                var cand = new Vector2Int(cur.x + Neighbors8[i].x, cur.y + Neighbors8[i].y);
                if (!map.Walkable(cand)) continue;
                var d = Vector2.Distance(MonsterRuntime.Center(cand), playerCenter);
                if (d > bestDist + 0.05f)
                {
                    bestDist = d;
                    best = cand;
                }
            }

            if (best == cur)
            {
                MonsterLog.WarnThrottled("ai.nowayout",
                    $"StepAway: m#{m.State.id} {m.State.name} 被围住/贴边（8 邻里没有更远的可走格）⇒ 退无可退");
                return false;
            }

            m.ClearPath();
            m.StepToward(MonsterRuntime.Center(best), owner.SpeedOf(m), dt);
            return true;
        }

        /// <summary>
        /// 走到某个**可走**格：带路径节流（目标漂移 &gt; `RepathOnGoalDrift` 或冷却到才重算）。
        /// </summary>
        /// <returns>true = 已有可用路径（可以继续 `Advance`）。</returns>
        private static bool TryPathTo(MonsterModule owner, MonsterRuntime m, Vector2Int goal)
        {
            var map = owner.Map;
            if (map == null || !map.IsGenerated)
            {
                MonsterLog.WarnOnce("ai.nomap",
                    "TryPathTo: 地图未就绪（AppContext.Map == null / 未生成）⇒ 怪物原地不动");
                return false;
            }

            var from = m.Grid;

            // ── 入参自检：非法格**绝不**喂给 FindPath（否则 CloverEngine.AStar 会打限频日志）──
            if (!map.InBounds(from) || !map.Walkable(from))
            {
                if (!m.WarnedBadGoal)
                {
                    m.WarnedBadGoal = true;
                    MonsterLog.Warn($"TryPathTo: m#{m.State.id} {m.State.name} 自身所在格 {from} 不可走/越界" +
                                    "（被地形改动挤进墙里？）⇒ 原地待命");
                }
                return false;
            }
            if (!map.InBounds(goal) || !map.Walkable(goal))
            {
                if (!m.WarnedBadGoal)
                {
                    m.WarnedBadGoal = true;
                    MonsterLog.Warn($"TryPathTo: m#{m.State.id} {m.State.name} 的目标格 {goal} 不可走/越界" +
                                    "（玩家站到障碍上？）⇒ 原地待命");
                }
                return false;
            }

            // ── 路径节流：已有路径 + 目标没怎么变 + 冷却未到 ⇒ 沿用 ──
            if (m.HasRemainingPath && m.HasPathTarget
                && Iso.GridDistance(m.PathTarget, goal) <= MonsterTuning.RepathOnGoalDrift
                && m.RepathTimer > 0f)
            {
                return true;
            }

            var path = map.FindPath(from, goal);
            if (path == null)
            {
                if (!m.WarnedNoPath)
                {
                    m.WarnedNoPath = true;
                    MonsterLog.Warn($"TryPathTo: m#{m.State.id} {m.State.name} 找不到 {from} → {goal} 的路径" +
                                    "（连通性问题？）⇒ 原地待命（该怪只报一次）");
                }
                m.ClearPath();
                return false;
            }

            m.WarnedNoPath = false;
            m.SetPath(path, goal);
            return m.HasRemainingPath || m.Grid == goal;
        }

        /// <summary>出手（受 `AttackIntervalSeconds` 节流）：写攻击动画标记并请 `CombatModule` 结算。</summary>
        private static Action TryAttack(MonsterModule owner, MonsterRuntime m)
        {
            if (m.AttackTimer > 0f) return Action.None;

            m.AttackTimer = MonsterTuning.AttackIntervalSeconds;
            m.AttackAnimTimer = MonsterTuning.AttackAnimSeconds;
            m.ViewDirty = true;
            owner.RequestMonsterAttack(m);
            return Action.Attack;
        }
    }
}
