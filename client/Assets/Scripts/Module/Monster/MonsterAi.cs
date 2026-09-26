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
//   0. **两层推进**：**决策**（4 个 AI 函数，每 `aidel` 帧跑一次）只定**移动意图**与"要不要出手"，
//      位移积分（`Advance` / `StepToward`）每帧由该意图驱动 —— 见 `DispatchTick` / `AdvanceIntent`。
//   1. **寻路一律走 `IMapModule.FindPath`**（本文件不实现任何寻路），并按
//      `MonsterTuning.RepathIntervalSeconds` 做**重寻路节流**；
//   2. **仇恨有时效**：一段时间没挨打/没看见玩家就遗忘（`MonsterTuning.AggroMemorySeconds`），
//      玩家离图/死亡时立刻脱战。遗忘后**回原位**（`Home`），不原地卡住。
//
// 所有"没按预期走"的分支都留日志（找不到路 / 目标格非法 / 退无可退 / 未登记的 AI 类型）。
// 高频分支用 `MonsterLog.WarnThrottled`（无时钟降频，见该文件头）。
//
//   1. **推进**：`Advance` / `StepToward` / 路径状态在引擎 `CloverEngine.PathFollower`
//      （本文件经 `MonsterRuntime` 转发调用）。
//   2. **状态骨架显式化**：每只怪一棵引擎状态机（`CloverEngine.Game.NewFsm()`；引擎全局那份是
//      应用级流程，不能共用），骨架 = `Idle → Aggro → Chase → Attack → Return / Flee`。
//      · **归位**：每 tick 由 `PhaseOf` 算一次（`Fsm` 忽略自环 ⇒ 不会重跑 OnEnter/OnExit）；
//      · **事件点显式转移**：出手成功 ⇒ `Attack`（`TryAttack`）；进入逃跑 ⇒ `Flee`（`Coward`）；
//        脱战 ⇒ `Return`（`Disengage`）。
//   **行为与数值的唯一真相仍是本文件那 4 个 AI 函数**（Melee / Range / Shaman / Coward）：
//      状态回调只做"骨架归位 + 转移留痕"，不许把射程 / 距离 / 冷却等数值搬进状态回调或 FSM 定义。
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

        /// <summary>
        /// 决策层定下的**移动意图**（推进层每帧按它积分位移；只描述"往哪走"，不含时长）。
        /// </summary>
        internal enum AiIntent
        {
            /// <summary>原地不动（出手 / 施法 / 等下一次决策）。</summary>
            None = 0,

            /// <summary>沿路径接近玩家当前格。</summary>
            Approach = 1,

            /// <summary>朝远离玩家的方向走一步（后撤 / 逃跑共用）。</summary>
            Retreat = 2,
        }

        // ── 状态骨架的状态名（引擎 `Fsm` 的 key；**字符串常量**，不散落字面量）──────────
        /// <summary>待机：未交战、也不在回原位。</summary>
        private const string PhaseIdle = "Idle";

        /// <summary>刚锁定玩家（进入仇恨的那一站，留痕用）。</summary>
        private const string PhaseAggro = "Aggro";

        /// <summary>交战推进（**行为入口**：调 4 个 AI 函数）。</summary>
        private const string PhaseChase = "Chase";

        /// <summary>本 tick 出手（事件标签，由 `TryAttack` 转进）。</summary>
        private const string PhaseAttack = "Attack";

        /// <summary>脱战回原位（`ReturnHome`）。</summary>
        private const string PhaseReturn = "Return";

        /// <summary>低血逃跑中（事件标签，由 `Coward` 转进；持续到 `FleeTimer` 归零）。</summary>
        private const string PhaseFlee = "Flee";

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
            if (m.AiThinkTimer > 0f) m.AiThinkTimer -= dt;   // 思考节拍恒在走（决策层只在它归零的那一 tick 跑）
            if (m.FleeTimer > 0f) m.FleeTimer -= dt;         // 逃跑时长每帧走（逃跑位移在推进层）
            if (m.AttackAnimTimer > 0f)
            {
                m.AttackAnimTimer -= dt;
                if (m.AttackAnimTimer <= 0f) m.ViewDirty = true;
            }
            if (m.PendingAttackTimer > 0f)
            {
                // 接触帧结算：挥击在 TryAttack 发起，命中/伤害排期到 A1 的官方触发帧
                // （`MonsterTuning.AttackContactSecondsOf`）；结算层在那一刻按当时的
                // 格距/视线重判 ⇒ 玩家已跑出近战格的本次空挥。受击/死亡会清零本计时器
                // （挥击被打断 ⇒ 不结算）。
                m.PendingAttackTimer -= dt;
                if (m.PendingAttackTimer <= 0f)
                {
                    m.PendingAttackTimer = 0f;
                    owner.RequestMonsterAttack(m);
                }
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

            // 骨架先建好：`UpdateEngagement` / `Coward` 会在**事件点**显式转状态（需要 `m.Ai` 已存在）。
            var fsm = FsmOf(m, owner);
            m.AiPlayerCenter = playerCenter;
            m.AiDist = dist;
            m.AiResult = Action.None;      // 每 tick 复位：本 tick 没走行为分支时不会残留上一帧的结果

            UpdateEngagement(owner, m, hasPlayer, dist, dt);

            // ── 骨架归位 + 驱动 ──
            // 归位（自环被 `Fsm` 忽略 ⇒ 不重跑回调）；真正的行为在 Chase / Return / Flee / Aggro / Idle
            // 的状态回调里（`DispatchTick` / `ReturnHome`），这里不做任何 AI 判定。
            fsm.Transition(PhaseOf(m));
            fsm.Tick(dt);
            return m.AiResult;
        }

        // ═════════════════════════════════════════════════════════════════════
        // 状态骨架（引擎 `Fsm`）：建 / 归位 / 行为入口
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>
        /// 本 tick 的**骨架归位**（有意不改任何行为）。
        /// <para>
        /// `Attack` / `Flee` 是"事件点"显式转移的目标，这里**不复算**它们的条件 ——
        /// `Attack` 只活到下一 tick 归位为止；`Flee` 由 `FleeTimer` 决定何时交回 `Chase`
        /// （该计时器由 `Step` 每帧递减，本方法不碰）。
        /// </para>
        /// </summary>
        private static string PhaseOf(MonsterRuntime m)
        {
            if (!m.Engaged) return m.Returning ? PhaseReturn : PhaseIdle;

            // 逃跑中：骨架停在 Flee（其 tick 仍走 `Coward` 的逃跑分支，行为与改动前一致）。
            if (m.State != null && m.State.ai == MonsterAI.Coward && m.FleeTimer > 0f) return PhaseFlee;

            // 刚锁定玩家 / 刚被复用（`Current == null`）：先走一站 Aggro 留痕，下一 tick 归位到 Chase。
            if (m.Ai.Current == null || m.Ai.Current == PhaseIdle) return PhaseAggro;

            return PhaseChase;
        }

        /// <summary>
        /// 取（首次则建）**本怪自己的**状态机。注册一次、之后复用 —— 闭包捕获 `owner` / `m`，
        /// 行为入口一律走 <see cref="DispatchTick"/> / <see cref="ReturnHome"/>。
        /// </summary>
        private static CloverEngine.IFsm FsmOf(MonsterRuntime m, MonsterModule owner)
        {
            if (m.Ai != null) return m.Ai;

            var fsm = CloverEngine.Game.NewFsm();

            fsm.RegisterState(PhaseIdle,
                onTick: _ => { m.AiResult = Action.None; });

            fsm.RegisterState(PhaseAggro,
                onEnter: () => MonsterLog.Info($"ai-state m#{m.State.id} {m.State.name}: → {PhaseAggro}（已锁定玩家）"),
                onTick: dt => DispatchTick(owner, m, dt));

            fsm.RegisterState(PhaseChase,
                onTick: dt => DispatchTick(owner, m, dt));

            fsm.RegisterState(PhaseAttack,
                onEnter: () => MonsterLog.Info($"ai-state m#{m.State.id} {m.State.name}: → {PhaseAttack}（本 tick 出手）"),
                onTick: dt => DispatchTick(owner, m, dt));

            fsm.RegisterState(PhaseReturn,
                onEnter: () => MonsterLog.Info($"ai-state m#{m.State.id} {m.State.name}: → {PhaseReturn}（回原位 {m.Home}）"),
                onTick: dt => ReturnHome(owner, m, dt));

            fsm.RegisterState(PhaseFlee,
                onEnter: () => MonsterLog.Info($"ai-state m#{m.State.id} {m.State.name}: → {PhaseFlee}（低血逃跑中）"),
                onTick: dt => DispatchTick(owner, m, dt));

            m.Ai = fsm;
            return fsm;
        }

        /// <summary>
        /// <see cref="MonsterRuntime.AiResult"/>。这里不做任何数值判断 —— 判定全在 4 个 AI 函数里。
        /// </summary>
        private static void DispatchTick(MonsterModule owner, MonsterRuntime m, float dt)
        {
            // ── ① 决策层：官方 AI **每 `aidel` 帧才思考一次**（逐怪 `MonsterTuning.AiDelaySecondsOf`）
            //    ⇒ 出手决定只落在节拍点上 ⇒ 有效间隔 = `ceil(A1 / aidel) × aidel`
            //    （`MonsterTuning.AttackIntervalSecondsOf` 与判据窗口同源于那一个函数）。
            //    节拍**只累加、不因出手重置** ⇒ 相位固定，出手被量化到节拍点。
            if (m.AiThinkTimer <= 0f)
            {
                m.AiResult = Decide(owner, m, m.AiPlayerCenter, m.AiDist);
                m.AiThinkTimer += MonsterTuning.AiDelaySecondsOf(m.State.kindId);
            }

            // ── ② 推进层：位移每帧照旧积分（决策每拍只更新一次意图，位移不能跟着停）──
            AdvanceIntent(owner, m, dt);
        }

        /// <summary>
        /// 推进层：按决策定下的 <see cref="MonsterRuntime.Intent"/> 积分**本帧**位移。
        /// <para>每帧跑、不受节拍约束 —— 移动积分若跟着决策一起卡，怪会在两次思考之间被钉在原地。</para>
        /// </summary>
        private static void AdvanceIntent(MonsterModule owner, MonsterRuntime m, float dt)
        {
            switch (m.Intent)
            {
                case AiIntent.Approach:
                    if (!TryPathTo(owner, m, owner.PlayerGrid)) return;
                    m.Advance(owner.SpeedOf(m), dt);
                    return;

                case AiIntent.Retreat:
                    StepAway(owner, m, m.AiPlayerCenter, dt);
                    return;

                default:
                    return;
            }
        }

        /// <summary>
        /// **4 种 AI 的行为分发**（行为与数值的**唯一真相**；每个节拍点跑一次）。
        /// <para>本方法只**定意图**（<see cref="MonsterRuntime.Intent"/>）与"要不要出手"，不动坐标 ——
        /// 位移一律由 <see cref="AdvanceIntent"/> 每帧积分，出手由 <see cref="TryAttack"/> 的 A1 闸门把关。</para>
        /// </summary>
        private static Action Decide(MonsterModule owner, MonsterRuntime m, Vector2 playerCenter, float dist)
        {
            m.Intent = AiIntent.None;
            switch (m.State.ai)
            {
                case MonsterAI.Melee:
                    return Melee(owner, m, playerCenter, dist);

                case MonsterAI.Range:
                    return Ranged(owner, m, playerCenter, dist);

                case MonsterAI.Shaman:
                    return Shaman(owner, m, playerCenter, dist);

                case MonsterAI.Coward:
                    return Coward(owner, m, playerCenter, dist);

                default:
                    MonsterLog.WarnThrottled("ai.unknown",
                        $"m#{m.State.id} {m.State.name} 的 AI 类型 {(int)m.State.ai} 未登记" +
                        "（契约只有 Melee/Range/Shaman/Coward）⇒ 按 Melee 处理");
                    return Melee(owner, m, playerCenter, dist);
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
                //    这里**必须**刷新：否则速度慢的怪（僵尸 speed=1）在 7 格外追击时会在半路
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
            m.Intent = AiIntent.None;           // 脱战即丢掉上一拍定下的移动意图（否则回原位期间残留）
            m.ClearPath();
            m.Ai?.Transition(PhaseReturn);      // 事件点：脱战 ⇒ 骨架置 Return（下一 tick 由 PhaseOf 接管）
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
        /// `MeleeShape.LineClear` + 同一份 `WalkableProbe`），本文件**不另写一份**判定。
        /// 跨模块只取这一句纯判定、不 `using` 对方的具体类型（与 `SkillModule` 取
        /// `Combat.DamagePipeline` 同一写法，见 `DamagePipeline.cs` 文件头）。
        /// </para>
        /// <para>
        /// 结算层 `RequestMonsterAttack` 判"线段被不可走地形阻断"时**只打日志就 return**，
        /// 而 `TryAttack` 在**发起前**已经把出手动画 / 出手音效 / 出手计时器都写好了
        /// （`m.AttackTimer` / `m.AttackAnimTimer` / `m.ViewDirty`，见 `TryAttack`）⇒
        /// 怪每 `AttackIntervalSecondsOf(kindId)` 挥一次空、且**永远不知道**自己被拒（死循环）。
        /// 现在出手前先用同一把尺子自检：不通 ⇒ **不发起**，改走"绕路"（原版近战怪够不着时
        /// 不会原地挥空）。
        /// </para>
        /// <para>
        /// 这不是"把 `LineClear` 放宽"（那是让怪穿墙打人，与原版语义相反）：判据一字未动，
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
        private static Action Melee(MonsterModule owner, MonsterRuntime m, Vector2 playerCenter, float dist)
        {
            //   隔墙/隔水时不发起（否则每次都在结算层被拒 = 挥空），落到下面**绕路**那一支。
            if (AttackDistance(owner, m) <= GameConst.MeleeRange && AttackLineClear(owner, m))
                return TryAttack(owner, m);

            m.Intent = AiIntent.Approach;
            return Action.None;
        }

        /// <summary>② 远程：保持 `RangedKeepDistance` ~ `RangedRange` 的射击距离。</summary>
        private static Action Ranged(MonsterModule owner, MonsterRuntime m, Vector2 playerCenter, float dist)
        {
            if (dist < MonsterTuning.RangedKeepDistance)
            {
                //   退得动就退（本拍只定意图，位移在推进层）；真退不动 ⇒ 落下面"就地射击"那一支。
                if (CanStepAway(owner, m, playerCenter))
                {
                    m.Intent = AiIntent.Retreat;
                    return Action.None;
                }

                // 退无可退（角落里）：只能就地射击，而不是站着不动被白打
                //   被墙挡住时"就地射击"同样会被结算层拒 ⇒ 不发起（原版怪物不隔墙放枪）。
                if (AttackLineClear(owner, m)) return TryAttack(owner, m);
                return Action.None;
            }

            //   与 `CombatModule.cs` 同一把尺子）。`RangedAttackDistanceCap` 必须与结算层的射程上限
            //   同值：否则 5 < 格距 ≤ 8 会成为死区 —— 怪反复请求出手、每次被结算层以 `> 射程 5.00`
            //   拒绝，又因为不满足靠近条件而**永远不靠近** ⇒ 站着不动、一枪不发。
            //   "够不着 **或** 看不见"一律走**靠近**那一支（挪到有视线的格子）。
            if (AttackDistance(owner, m) > RangedAttackDistanceCap || !AttackLineClear(owner, m))
            {
                m.Intent = AiIntent.Approach;
                return Action.None;
            }

            return TryAttack(owner, m);
        }

        /// <summary>③ 萨满：优先复活同伴；其余时间按远程节奏施法。</summary>
        private static Action Shaman(MonsterModule owner, MonsterRuntime m, Vector2 playerCenter, float dist)
        {
            if (m.ReviveTimer <= 0f)
            {
                var corpseId = owner.FindRevivableCorpse(m);
                if (corpseId >= 0)
                {
                    if (owner.ReviveMonster(corpseId, m)) m.ReviveTimer = MonsterTuning.ShamanReviveCooldownSeconds;
                    return Action.None;      // 复活是"施法"，本拍不再移动/攻击
                }
            }

            if (dist < MonsterTuning.RangedKeepDistance && CanStepAway(owner, m, playerCenter))
            {
                m.Intent = AiIntent.Retreat;
                return Action.None;
            }

            if (AttackDistance(owner, m) > RangedAttackDistanceCap || !AttackLineClear(owner, m))
            {
                m.Intent = AiIntent.Approach;
                return Action.None;
            }

            return TryAttack(owner, m);
        }

        /// <summary>④ 逃跑型：低血逃跑（原版堕落者），否则**等同近战**。</summary>
        private static Action Coward(MonsterModule owner, MonsterRuntime m, Vector2 playerCenter, float dist)
        {
            // 逃跑中（`FleeTimer` 由 `Step` 每帧递减）：继续背向玩家退
            if (m.FleeTimer > 0f)
            {
                m.Intent = AiIntent.Retreat;
                return Action.None;
            }

            var ratio = m.State.maxHp > 0 ? (float)m.State.hp / m.State.maxHp : 0f;
            if (ratio <= MonsterTuning.CowardFleeHpRatio)
            {
                m.FleeTimer = MonsterTuning.CowardFleeSeconds;
                m.ClearPath();
                m.Ai?.Transition(PhaseFlee);    // 事件点：低血 ⇒ 骨架置 Flee（持续到 FleeTimer 归零）
                MonsterLog.Info($"flee m#{m.State.id} {m.State.name}：hp {m.State.hp}/{m.State.maxHp}" +
                                $"（{ratio * 100f:0}%）≤ {MonsterTuning.CowardFleeHpRatio * 100f:0}% ⇒ 背向玩家逃跑" +
                                $"（持续 {MonsterTuning.CowardFleeSeconds:0.0}s）");
                m.Intent = AiIntent.Retreat;
                return Action.None;
            }

            if (AttackDistance(owner, m) <= GameConst.MeleeRange && AttackLineClear(owner, m))
                return TryAttack(owner, m);

            m.Intent = AiIntent.Approach;
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

            Vector2Int best;
            if (!TryPickFartherGrid(owner, m, playerCenter, out best))
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
        /// 决策层的**纯判定**：本拍退不退得动（不动坐标、不打日志）。
        /// <para>决策要求"意图"在动手之前就定下来（位移归推进层），所以"退得动吗"必须是可单独求值的问题。</para>
        /// </summary>
        private static bool CanStepAway(MonsterModule owner, MonsterRuntime m, Vector2 playerCenter)
        {
            Vector2Int best;
            return TryPickFartherGrid(owner, m, playerCenter, out best);
        }

        /// <summary>
        /// 挑一个"比当前格更远离玩家"的可走 8 邻格（**纯判定**，不移动坐标）。
        /// 只在 8 邻里挑 ⇒ **绝不会把非法格喂给 `FindPath`**（`CloverEngine.AStar` 对非法入参会打限频日志）。
        /// </summary>
        /// <returns>true = 有可退的格（写入 <paramref name="best"/>）；false = 退无可退 / 地图未就绪。</returns>
        private static bool TryPickFartherGrid(MonsterModule owner, MonsterRuntime m, Vector2 playerCenter, out Vector2Int best)
        {
            var cur = m.Grid;
            best = cur;

            var map = owner.Map;
            if (map == null || !map.IsGenerated) return false;

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

            return best != cur;
        }

        /// <summary>
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

        /// <summary>
        /// 出手：写攻击动画标记并请 `CombatModule` 结算。
        /// <para>两道节流合起来 = <see cref="MonsterTuning.AttackIntervalSecondsOf"/>（判据窗口同源于那一个函数）：
        /// ① **思考节拍**（由调用方 `DispatchTick` 保证：本方法只在节拍点被调用，官方 `MonStats.aidel`
        /// 逐怪出处见 `MonsterTuning.AiDelayFramesByCode` / `AiDelaySecondsOf`）；
        /// ② `AttackTimer` = 这次出手**动作本身的时长**（A1）—— 动作没播完不能再来一次。</para>
        /// <para>节拍点落在 A1 之内 ⇒ 那一拍不出手、等下一拍 ⇒ 有效间隔 = `ceil(A1 / aidel) × aidel`。</para>
        /// </summary>
        private static Action TryAttack(MonsterModule owner, MonsterRuntime m)
        {
            if (m.AttackTimer > 0f) return Action.None;

            m.AttackTimer = MonsterTuning.AttackAnimSecondsOf(m.State.kindId);
            m.AttackAnimTimer = MonsterTuning.AttackAnimSecondsOf(m.State.kindId);
            m.ViewDirty = true;
            owner.PlayAttackSfx(m);             // 出手音随起手帧（挥空也有这一声，与原版一致）
            var contact = MonsterTuning.AttackContactSecondsOf(m.State.kindId);
            if (contact > 0f) m.PendingAttackTimer = contact;   // 命中/伤害排期到 A1 接触帧（Step 倒计时）
            else owner.RequestMonsterAttack(m);                 // 官方表无触发帧 ⇒ 出手即结算
            m.Ai?.Transition(PhaseAttack);      // 事件点：出手 ⇒ 骨架置 Attack（下一 tick 由 PhaseOf 接管）
            return Action.Attack;
        }
    }
}
