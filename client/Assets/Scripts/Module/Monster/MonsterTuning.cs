// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · Module/Monster/MonsterTuning.cs
// 怪物 AI 的**手感/节奏常量**集中处。
//
// 边界（`tools/ai-skill/conventions.md` §数值规则）：
//   · **同质化数值**（怪物的血/伤害/AC/AR/经验/抗性/速度/群数/掉落）**一律走配表**
//     `monster_c` / `monumod_c` / `level_c` / `treasureclass_c` —— 本文件**一个都不放**；
//   · 本文件只放"原版**由可执行程序里的 AI 脚本 + `AiParms.txt` 参数 + `AnimData` 动画帧**驱动、
//     本项目尚无对应配表列"的**行为节奏参数**。
//
// ═════════════════════════════════════════════════════════════════════════════
// ═════════════════════════════════════════════════════════════════════════════
// 权威表（LoD 1.10 官方 txt，随参考工程一起落盘）：`<根>/原版资源/参考工程_Diablerie/d2lod1.10txt/data/global/excel/`
//   · `MonStats.txt`  的 `aidel`（**AI 思考延迟，帧**；本项目 8 只怪全是 15）、`aip1..8`、`Velocity`、`Run`、
//     `MinGrp`/`MaxGrp`/`Rarity`
//   · `AiParms.txt`   —— 每个 AI 的 `aip1..P4` **语义说明**（原版自带文档；见文件内逐条引用）
//   · `Levels.txt`   的 `MonDen`（怪物密度）、`MonUMin`/`MonUMax`（冠军/精英下限上限）
// 参考实现：`<根>/原版资源/参考工程_Diablerie/libd2/packages/game/src/{monai,montable,gameserver}.zig`
//   · `montable.zig:50`  `aidel` = "the AI think-delay (frames between AI runs)"（原版 25 fps 逻辑帧率）
//   · `gameserver.zig:675` "One `tick()` is one iteration of the real **25-fps** per-game server loop"
//   · `gameserver.zig:3604-3606` 尸体保留 `CORPSE_TTL = 500` 帧（注释原文 "~20s at 25fps"）
//   · `monai.zig:139-149` 堕落者的逃跑触发 = **同伴尸体在 15 subtiles 内**（不是血量比例）
//
// **明确不采信**：`libd2/.../ai.zig` 自己的文件头写着 "a slice-level approximation …
//
//   `MonsterAggroRange`(8) / `MonsterLeashRange`(14) / `MeleeRange`(1.6) / `RangedRange`(8)
// ─────────────────────────────────────────────────────────────────────────────

namespace Diablo2.Module.Monster
{
    /// <summary>怪物 AI 节奏常量（每条都有出处或【例外】编号，见各成员注释）。</summary>
    internal static class MonsterTuning
    {
        /// <summary>
        /// 原版逻辑帧率（帧/秒）。出处：参考实现 `libd2/packages/game/src/gameserver.zig:675`
        /// 原文 "the real 25-fps per-game server loop"。
        /// <para>`MonStats.aidel`、`CORPSE_TTL` 这些"帧"口径的官方值都按它换算成秒。</para>
        /// </summary>
        public const float LogicFps = 25f;

        /// <summary>
        /// 官方 `MonStats.txt::aidel`（AI 思考延迟，**帧**）——本项目 8 只 Act I 怪的值为 **15**。
        /// 出处：`d2lod1.10txt/.../MonStats.txt`（`fallen1`/`fallenshaman1`/`quillrat1`/`zombie1`/
        /// `corruptrogue1`/`foulcrow2`/`brute1`/`wraith1` 八行的 `aidel` 列全为 15）；
        /// 语义出处：参考实现 `montable.zig:50`（"the AI think-delay (frames between AI runs)"）。
        /// </summary>
        public const int OfficialAiDelayFrames = 15;

        /// <summary>
        /// 官方 `CORPSE_TTL`（尸体保留帧数）。出处：参考实现 `gameserver.zig:3604-3606`
        /// （`const CORPSE_TTL: i32 = 500;`，注释 "~20s at 25fps"）。
        /// </summary>
        public const int OfficialCorpseTtlFrames = 500;

        // ── 仇恨（官方行为：一段时间不挨打/看不见玩家就遗忘）────────────────────
        /// <summary>
        /// 【例外 **E28**】保持交战所需的"最后接触"记忆时长（秒）。
        /// <para>
        /// 官方**没有**"仇恨记忆时长"这一参数：原版 AI 每个 `aidel` 帧（= 15/25 = 0.6s）跑一次思考，
        /// 由 `MonStats.ai` 选定的 AI 脚本用 `aip1..8` + 感知距离（`aidist`）自行决定追/停；
        /// 没有"记住玩家 N 秒"这种状态量（`AiParms.txt` 里 8 只怪的 AI 参数段也没有该项）。
        /// ⇒ 本项是**本项目新增**，登记 E28（为什么必须这样 + 消除条件见 `策划/验收表.md`）。
        /// </para>
        /// </summary>
        public const float AggroMemorySeconds = 5f;

        // ── 寻路节流 ─────────────────────────────────────────────────────────
        /// <summary>
        /// 两次重寻路之间的最小间隔（秒）= 官方 `MonStats.aidel` 15 帧 ÷ 25 fps = **0.6s**。
        /// <para>出处：见 <see cref="OfficialAiDelayFrames"/> 与 <see cref="LogicFps"/>（原版 AI 每 15 帧才思考一次）。</para>
        /// </summary>
        public const float RepathIntervalSeconds = OfficialAiDelayFrames / LogicFps;   // = 0.6s

        /// <summary>【本项目新增】（纯逻辑节流参数，官方无对应量）：目标格变化超过 1 格时允许提前重算。</summary>
        public const int RepathOnGoalDrift = 1;

        // ── 出手节奏（官方由 `AnimData` 的攻速帧决定，本项目无该列）─────────────
        /// <summary>
        /// 【例外 **E28**】怪物两次出手之间的最小间隔（秒）。
        /// <para>
        /// 官方**出处是动画帧数**（`MonStats.Code` 对应的 `.cof` 动画里 A1 模式的帧数 ÷ 25），
        /// 本批没有把 `.cof` 的逐怪 A1 帧数解析成表 ⇒ **本项目新增**近似值（登记 E28）。
        /// 注意 `AiParms.txt` 的 `P1`（如 Brute/Fallen 的 "Pct chance to strike - set this to affect
        /// attack speed"）是**出手概率**，不是间隔秒数 ⇒ 不能拿它当出处。
        /// </para>
        /// </summary>
        public const float AttackIntervalSeconds = 1.10f;

        /// <summary>【例外 **E28**】"正在攻击"标记保持时长（秒）——视图据此播攻击动画；官方 = A1 动画帧数（同 E28）。</summary>
        public const float AttackAnimSeconds = 0.35f;

        /// <summary>【例外 **E28**】受击硬直时长（秒）——官方 = 受击动画（`MonMode` 的 `GH/HD`）帧数（同 E28）。</summary>
        public const float HitStunSeconds = 0.18f;

        // ── 移动 ─────────────────────────────────────────────────────────────
        /// <summary>
        /// 【本项目新增】（防配表写成 0 导致怪物永远不动的**守卫**，不是手感参数）：速度下限（格/秒）。
        /// </summary>
        public const float MinMoveSpeed = 0.2f;

        /// <summary>
        /// <para>为什么是 0.2：官方 `Velocity` 的单位是 **map 单位/秒**，而 **1 格 = 5 map 单位**
        /// ⇒ 1 ÷ 5 = **0.2**（旧值 1.0 把"5 map 单位/秒"当成了"5 格/秒" ⇒ 怪物快 5 倍 = 用户说的"飘着走"）。</para>
        /// <para>出处（逐条可查）：
        /// ① `1 格 = 5 map 单位`：`原版资源/参考工程_Diablerie/Diablerie/Assets/Scripts/Diablerie/Engine/Iso.cs:9-12`
        /// 的 `SubTileCount = 5`（换算点 `Engine/World/LevelBuilder.cs:245/328/508` 的 `* Iso.SubTileCount`）；
        /// ② 玩家侧的量纲自证：`GameConst.PlayerWalkSpeed` 的推导（`Player.cs:48-49` 的
        /// `runSpeed = 15` map 单位/秒 = 3.0 格/秒）用的是同一个 ÷5 口径；
        /// ③ 官方数值（取值域复核）：`原版资源/d2raw/data/global/excel/monstats.txt` 的 `Velocity` 列
        /// —— 第 21 行 `Fallen` = 5（⇒ 1.0 格/秒）、第 7 行 `Zombie` = 1（⇒ 0.2 格/秒）、
        /// 第 65 行 `QuillRat` = 3（⇒ 0.6 格/秒）。</para>
        /// </summary>
        public const float SpeedToTilesPerSecond = 0.2f;

        // ── 远程怪保持距离（`Range` / `Shaman`）────────────────────────────────
        /// <summary>
        /// 【例外 **E28**】远程怪希望保持的最小距离（格）——小于它就后撤。
        /// <para>
        /// 官方是**逐 AI 的 `aipN`** 且**单位不同**：`AiParms.txt`「Spike Fiend」`P1` = 考虑发射飞弹的距离
        /// （`quillrat1` 的 `aip1 = 10`）；「Fallen Shaman」`P5` = 施放 Skill2 的距离。
        /// 本项目 `monster_c` **没有** `aipN` 列（契约冻结）且用一个常量覆盖两种怪 ⇒ 登记 E28。
        /// </para>
        /// </summary>
        public const float RangedKeepDistance = 4.0f;

        /// <summary>【本项目新增】（纯逻辑参数）：后撤时选点的搜索半径（格）。</summary>
        public const int BackOffSearchRadius = 3;

        /// <summary>
        /// 【例外 **E28**（新增一条）】**远程 / 萨满出手距离的上限（格）** —— 不得在画面外攻击。
        /// <para>
        /// 旧口径 = `GameConst.RangedRange`(8 格)：8 格 = **16 世界单位**，而**可见半宽**
        /// = 相机 ortho 尺寸 **6** ×(16/9 画幅) ÷ **2.0 世界单位/格**（`GameConst.IsoTilePxW` 128 ÷ `PixelsPerUnit` 64
        /// ⇒ 一格宽 2.0、高 1.0）= **5.33 格** ⇒ 8 格一定在画面外。
        /// </para>
        /// <para>
        /// 取 **5.0 格**：① 严格小于可见半宽 5.33（含相机跟随滞后 `CameraRig` 的余量）
        /// ⇒ **出手时怪物一定在画面内**；② 仍是"远程"（远大于近战 1.6 格、且 &gt; `RangedKeepDistance`
        /// 的计算区间）；③ 原版 `AiParms.txt`「Spike Fiend」`P1`（= 考虑发射飞弹的距离，`quillrat1` 的 `aip1 = 10`）
        /// 的单位是 **world subtiles**，而 subtile → 格 的换算本项目**未确证**（同
        /// <see cref="ShamanReviveRange"/> 的 E28 备注）⇒ 不拿 `aip1` 当出处，登记 E28。
        /// </para>
        /// </summary>
        public const float RangedAttackMaxRange = 5.0f;

        // ── 萨满复活同伴 ─────────────────────────────────────────────────────
        /// <summary>
        /// 复活冷却（秒）= 官方 `aidel` 15 帧 ÷ 25 fps = **0.6s**（官方萨满每个 AI 思考帧才判定一次
        /// "要不要复活"，`AiParms.txt`「Fallen Shaman」`P1` = 有尸体时复活/召唤的概率）。
        /// <para>出处：<see cref="OfficialAiDelayFrames"/> + <see cref="LogicFps"/>；
        /// 参考实现的动作序与参数语义见 `monai.zig:151-211`（`decideFallenShaman`）。</para>
        /// </summary>
        public const float ShamanReviveCooldownSeconds = OfficialAiDelayFrames / LogicFps;   // = 0.6s

        /// <summary>
        /// 【例外 **E28**】可复活目标的最大距离（格）。官方 = `fallenshaman1` 的 `aip4 = 24`
        /// （`AiParms.txt`「Fallen Shaman」段：`aip4` = 复活扫描半径；参考实现 `monai.zig:170-175` 原文
        /// "aip4 (revive scan radius) is applied by the host's corpse scan"）。
        /// </summary>
        public const float ShamanReviveRange = 7.0f;

        /// <summary>【例外 **E28**】被复活同伴恢复的血量比例（官方 txt/AiParms 里没有这条；本项目新增）。</summary>
        public const float ReviveHpRatio = 0.5f;

        // ── 逃跑型（原版堕落者）──────────────────────────────────────────────
        /// <summary>
        /// 【例外 **E28**】触发逃跑的血量比例（≤ 该值即逃）。
        /// <para>
        /// 这条**与官方机制不同**：堕落者的官方逃跑触发是**同伴尸体在 15 subtiles 内**
        /// （参考实现 `monai.zig:139-149`：`FALLEN_CORPSE_FLEE_RANGE = 15`，
        /// `fallenShouldFlee(corpseWithinRange)` —— 原版 AI_Function1_Fallen 的**无条件**逃跑标志），
        /// **不是**血量比例。官方按血量比例逃的只有 `Conservative` AI（`AiParms.txt`「Conservative」`P4`
        /// = "Pct health to flee"，如 Evil Spider）与 `Sand Raider`（`P1`）——本项目 8 只怪都不是它。
        /// </para>
        /// </summary>
        public const float CowardFleeHpRatio = 0.35f;

        /// <summary>【本项目新增】（官方无此项）：一次逃跑持续时间（秒）。</summary>
        public const float CowardFleeSeconds = 3.0f;

        /// <summary>【本项目新增】（官方无此项）：逃跑时希望拉开的距离（格）。</summary>
        public const float CowardFleeDistance = 6.0f;

        /// <summary>【本项目新增】（官方无此项）：逃跑时每步的直线位移（格）——只挑可走格。</summary>
        public const float FleeStepDistance = 1.5f;

        // ── 尸体 / 回收 ──────────────────────────────────────────────────────
        /// <summary>
        /// 尸体保留时长（秒）= 官方 `CORPSE_TTL` 500 帧 ÷ 25 fps = **20s**。
        /// <para>出处：参考实现 `gameserver.zig:3604-3606`（`CORPSE_TTL = 500`，注释 "~20s at 25fps"）。
        /// 与旧值 25s 的差异来自"旧值是本项目随手定的"，本片按官方改。</para>
        /// </summary>
        public const float CorpseLifetimeSeconds = OfficialCorpseTtlFrames / LogicFps;   // = 20s

        // ── 刷怪 ─────────────────────────────────────────────────────────────
        /// <summary>
        /// 【例外 **E28**】每个怪物种类刷出的**群数** = `level_c.mon_density / 该值`。
        /// <para>
        /// 官方 `Levels.txt::MonDen` **不是**"群数"：参考实现 `monpop.zig:23` 原文
        /// "The per-slot density gate (game seed % 100000 &lt;= MonDen)"，`:460` 又在刷怪时把它
        /// "clamped to 10000" ⇒ 它是**逐槽的抽样门槛**，要配合引擎的逐槽抽样循环才成立
        /// ⇒ 用"除以 200"折成群数是**本项目新增**（登记 E28）。
        /// </para>
        /// </summary>
        public const float DensityPerPack = 200f;

        /// <summary>【本项目新增】（守卫）：群数上限（防止密度列写错导致一次刷出上千只）。</summary>
        public const int MaxPacksPerSpecies = 8;

        /// <summary>【本项目新增】（守卫）：怪物离玩家出生点的最小距离（格）——别一进图就贴脸。</summary>
        public const float SpawnMinDistanceFromPlayer = 5f;

        /// <summary>【本项目新增】（守卫）：刷怪落点重试次数上限（超了就用随机可走格兜底）。</summary>
        public const int SpawnPlaceAttempts = 8;

        /// <summary>
        /// 【例外 **E28**】精英群数保底。
        /// <para>
        /// 官方该值来自 `Levels.txt` 的 `MonUMin`/`MonUMax`（本项目 `level_c.mon_umin/mon_umax`，
        /// 三个区域**官方值都是 0** ⇒ 按官方"一次都不刷"则整个 Act I 都看不到精英词缀）。
        /// 保底 1 群是**本项目新增**（登记 E28）。
        /// </para>
        /// </summary>
        public const int ElitePackFallback = 1;

        /// <summary>【本项目新增】（守卫）：精英群数上限。</summary>
        public const int MaxElitePacks = 4;
    }
}
