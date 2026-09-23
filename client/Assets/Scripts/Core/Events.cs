// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · Core/Events.cs
// 事件名 / 状态机站点名 / 触发器名**唯一来源**。
// ⛔ `Game.Event.On/Emit/Off` 一律传本文件的常量，**禁止裸字符串**
//    （裸字符串编译器查不出、改名必漏，`_common.md` §4 自检 ⑤ 会命中）。
//
// 命名法：`D2.{域}.{动作}`（`tools/ai-skill/conventions.md`「事件名」）。
// ⚠️ 不要蹭引擎的 `Net.` / `App.` 前缀（会撞引擎事件名，`docs/步骤文档.md` §3.4）。
//
// ⛔ 契约冻结：标「契约 §3.5」的常量逐字来自 `docs/步骤文档.md` §3.5，**名字与参数类型都不许改**。
//    其余为增补项（可增补、不许改已有的）。
//
// 参数约定（订阅方按此写 `Action<T>`）：
//   无参          → `Game.Event.On(name, handler)`
//   T             → `Game.Event.On<T>(name, handler)`
//   ⚠️ `Game.Event` **没有句柄**：注销必须用**同一个方法引用**，长驻模块禁止用匿名 lambda。
// ─────────────────────────────────────────────────────────────────────────────

using UnityEngine;

namespace Diablo2.Core
{
    /// <summary>事件名常量 + 流程状态机站点/触发器名常量。</summary>
    public static class Events
    {
        // ═════════════════════════════════════════════════════════════════════
        // 契约 §3.5（**逐字冻结：名字 + 参数类型都不许改**）
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>契约 §3.5：启动画面完成（无参）。</summary>
        public const string BootDone = "D2.Flow.BootDone";

        /// <summary>契约 §3.5：点击移动命令（参数 <see cref="Vector2Int"/> 格坐标）。</summary>
        public const string MoveCommand = "D2.Input.Move";

        /// <summary>契约 §3.5：战斗目标变化（参数 <see cref="int"/> monsterId；-1 = 无目标）。</summary>
        public const string TargetChanged = "D2.Combat.TargetChanged";

        /// <summary>契约 §3.5：产生伤害（参数 <c>Def.DamageArgs</c>）。</summary>
        public const string DamageDealt = "D2.Combat.Damage";

        /// <summary>契约 §3.5：怪物被击杀（参数 <see cref="int"/> monsterId）。</summary>
        public const string MonsterKilled = "D2.Combat.Killed";

        /// <summary>契约 §3.5：拾取物品（参数 <c>Def.ItemStack</c>）。</summary>
        public const string ItemPicked = "D2.Item.Picked";

        /// <summary>契约 §3.5：背包/装备/腰带/金币发生变化（参数 <c>Def.InventoryChangedArgs</c>）。</summary>
        public const string InventoryChanged = "D2.Item.InventoryChanged";

        /// <summary>契约 §3.5：升级（参数 <see cref="int"/> 新等级）。</summary>
        public const string LevelUp = "D2.Player.LevelUp";

        /// <summary>契约 §3.5：任务状态变化（参数 <c>Def.QuestStateDto</c>）。</summary>
        public const string QuestChanged = "D2.Quest.Changed";

        /// <summary>契约 §3.5：NPC 对话开始（参数 <c>Def.NpcDialogArgs</c>）。</summary>
        public const string DialogOpen = "D2.Npc.DialogOpen";

        /// <summary>契约 §3.5：商店打开（参数 <c>Def.ShopOpenArgs</c>）。</summary>
        public const string ShopOpen = "D2.Npc.ShopOpen";

        /// <summary>契约 §3.5：玩家死亡（无参）。</summary>
        public const string PlayerDied = "D2.Player.Died";

        /// <summary>契约 §3.5：HUD 需要刷新（参数 <c>Def.PlayerStatsDto</c>）。</summary>
        public const string HudDirty = "D2.Ui.HudDirty";

        // ═════════════════════════════════════════════════════════════════════
        // 流程（Flow）—— 增补
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>流程站点切换（参数 <see cref="string"/> 站点名，见 <see cref="Fsm"/>）。</summary>
        public const string FlowStationChanged = "D2.Flow.StationChanged";

        /// <summary>UI 请求创建角色（参数 <c>Def.CharacterSave</c>：已填名字/职业/初始四维）。</summary>
        public const string CharCreateRequest = "D2.Flow.CharCreateRequest";

        /// <summary>UI 请求选择角色（参数 <see cref="string"/> 角色名）。</summary>
        public const string CharSelectRequest = "D2.Flow.CharSelectRequest";

        /// <summary>UI 请求删除角色（参数 <see cref="string"/> 角色名）。</summary>
        public const string CharDeleteRequest = "D2.Flow.CharDeleteRequest";

        /// <summary>UI 请求暂停（无参）。</summary>
        public const string PauseRequest = "D2.Flow.PauseRequest";

        /// <summary>UI 请求继续（无参）。</summary>
        public const string ResumeRequest = "D2.Flow.ResumeRequest";

        /// <summary>UI 请求保存并退出（存档）（无参）。</summary>
        public const string SaveAndExitRequest = "D2.Flow.SaveAndExitRequest";

        /// <summary>UI 请求回主菜单（无参）。</summary>
        public const string ToMainMenuRequest = "D2.Flow.ToMainMenuRequest";

        /// <summary>UI 请求退出游戏（无参）。</summary>
        public const string QuitRequest = "D2.Flow.QuitRequest";

        /// <summary>多人游戏入口被点击（无参）——本项目未实装联机，收方给 Toast + Warn。</summary>
        public const string MultiplayerUnavailable = "D2.Flow.MultiplayerUnavailable";

        /// <summary>已进入 Stage 场景并装配完毕（无参）。</summary>
        public const string StageEntered = "D2.Flow.StageEntered";

        /// <summary>已离开 Stage 场景（清场完毕）（无参）。</summary>
        public const string StageLeft = "D2.Flow.StageLeft";

        // ═════════════════════════════════════════════════════════════════════
        // 地图（Map）
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>地图生成完成（参数 <c>Def.MinimapArgs</c>，小地图用）。</summary>
        public const string MapGenerated = "D2.Map.Generated";

        /// <summary>当前区域切换（参数 <c>Def.AreaId</c>）。</summary>
        public const string AreaChanged = "D2.Map.AreaChanged";

        /// <summary>玩家所在格变化（参数 <see cref="Vector2Int"/> 格坐标）。</summary>
        public const string PlayerGridChanged = "D2.Map.PlayerGridChanged";

        /// <summary>
        /// 有新格**首次**被记为已探索（参数 <c>IReadOnlyCollection&lt;Vector2Int&gt;</c> = 本次新增的格集合）。
        /// ★ 2026-09-23 新增（S2，Tab 自动地图的"记忆式已探索"数据源）。
        /// <para>
        /// **发方 = `Module/Map/MapModule.OnPlayerGridChanged`**（唯一发方），
        /// **只在 `MapView.MarkExplored` 回"这一格确实是第一次"时发**（重复走过同一格 **0 次**发出）
        /// ⇒ ⛔ 不是每帧发、也不是每次 `PlayerGridChanged` 都发。
        /// 载荷是"本次新增的格"（当前实现每次恰 1 格 + 1 个新数组引用 ⇒ 收方可以安全持有该引用）。
        /// </para>
        /// <para>
        /// **收方 = `UI/MiniMapPanel`**（S1 提供的注入接缝，**已接线** 2026-09-23）：
        /// `MiniMapPanel.cs:678` 订阅本事件 / `:719-721` 转 `ApplyExplored(...)` / `:687` 注销。
        /// 接管后 `ExploredInjected == true`，面板**不再**自行揭示（接管前保留它自己的兜底口径）。
        /// </para>
        /// <para>口径 = 访问过即记忆、本局内累积、作用域为当前区域（详见 `IMapModule.ExploredCells`）。</para>
        /// </summary>
        public const string MapExplored = "D2.Map.Explored";

        /// <summary>玩家踩到出入口（参数 <c>Def.AreaId</c> 目标区域）。</summary>
        public const string ExitEntered = "D2.Map.ExitEntered";

        // ═════════════════════════════════════════════════════════════════════
        // 输入/交互（Input）
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>悬停目标变化（参数 <c>Def.HoverTarget</c>）。</summary>
        public const string HoverTargetChanged = "D2.Input.HoverChanged";

        /// <summary>光标形态变化（参数 <c>Def.CursorKind</c>）。</summary>
        public const string CursorChanged = "D2.Input.CursorChanged";

        /// <summary>请求使用腰带格（参数 <see cref="int"/> 0..3）。</summary>
        public const string UseBeltRequest = "D2.Input.UseBelt";

        /// <summary>请求拾取地面物品（参数 <see cref="int"/> 地面物品 id；-1 = 最近一个）。</summary>
        public const string PickupRequest = "D2.Input.Pickup";

        /// <summary>
        /// 请求切换武器组（**无参**）—— 原版 <c>W</c> 键（`Def/GameKeyAlias.KeySwapWeapon`）。
        /// <para>
        /// 发送方：`Module/Player/PlayerModule.RequestSwapWeapon`（读键的唯一入口
        /// `Module/Input/InputReader.SwapWeaponPressed`）。
        /// 收方：`Module/Item/ItemModule`（订阅后切 `Equipment` 的生效武器组）；
        /// 与 `Events.UseBeltRequest` 同一条"读键 → 发请求 → Item 侧改状态"的路子。
        /// </para>
        /// <para>★ 本轮新增（T0 判据挖出的缺口 2「双武器组缺失」）；**只增不改**，
        /// 因为原版 D2 确有双武器组（武器切换）而本工程此前 0 处消费该键位。</para>
        /// </summary>
        public const string SwapWeaponRequest = "D2.Item.SwapWeaponRequest";

        /// <summary>请求与 NPC 交互/对话（参数 <see cref="int"/> 见 <c>Def.NpcId</c>）。</summary>
        public const string NpcInteractRequest = "D2.Input.NpcInteract";

        /// <summary>请求切换面板（参数 <see cref="string"/> 面板类名）。</summary>
        public const string PanelToggleRequest = "D2.Ui.PanelToggle";

        /// <summary>
        /// 地面物品名牌变化（参数 <c>Def.GroundItemLabelsArgs</c>）。
        /// <para>★ impl-I-input 新增。发送方 = `Module/Input/InputReader.UpdateHover`
        /// （消费 `InputReader.ShowGroundItems` = 原版 `Alt` 常显，以及当前悬停格）；
        /// 收方 = `UI/GroundItemLabelView`（HUD 持有的名牌层）。</para>
        /// <para>为什么需要它：原版 D2 悬停地面物品会显示该物品的名牌、按住 `Alt` 则常显全部
        /// 地面物品名 —— 改动前 `InputReader.ShowGroundItems` 与 `HoverTargetChanged` **都没有消费方**，
        /// 这两条表现完全缺失（审计 R5）。载荷放 `Diablo2.Def` 是为了让 UI 层能合法消费
        /// （分层自检 ③：`UI/**` 不许 `using Diablo2.Module`）。</para>
        /// <para>只在**内容真的变化**时发（按 id/格/Alt 态去重），不是逐帧刷。</para>
        /// </summary>
        public const string GroundItemLabelsChanged = "D2.Input.GroundItemLabels";

        // ═════════════════════════════════════════════════════════════════════
        // 战斗（Combat）
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>请求攻击指定怪物（参数 <see cref="int"/> monsterId）。</summary>
        public const string AttackRequest = "D2.Combat.AttackRequest";

        /// <summary>
        /// 玩家**真的挥出了一刀**（参数 <see cref="int"/> monsterId）。
        /// <para>语义 = "这一次挥击已经落地"（冷却已扣、距离已够；**命中与未命中都算一次挥击**），
        /// 与 <see cref="AttackRequest"/> 的区别：那个是"请求"（会被冷却/超距丢弃，按住左键时每帧都发），
        /// 本事件只在真的出手时发一次 ⇒ 视图侧据此播**挥击动作**。</para>
        /// <para>发送方：`Module/Combat/CombatModule.PlayerBasicAttack`（玩家普攻的唯一结算入口）；
        /// 收方：`Module/View/ViewModule`（`OnPlayerAttacked` ⇒ 播原版 `attack` 动作，
        /// 时长 = <c>GameConst.PlayerAttackInterval</c>）。</para>
        /// </summary>
        public const string PlayerAttacked = "D2.Combat.PlayerAttacked";

        /// <summary>玩家受伤（参数 <c>Def.DamageArgs</c>）。</summary>
        public const string PlayerDamaged = "D2.Combat.PlayerDamaged";

        /// <summary>怪物出生（参数 <c>Def.MonsterState</c>）。</summary>
        public const string MonsterSpawned = "D2.Combat.MonsterSpawned";

        /// <summary>怪物状态变化（参数 <c>Def.MonsterState</c>，限频刷新视图）。</summary>
        public const string MonsterChanged = "D2.Combat.MonsterChanged";

        /// <summary>玩家请求复活（无参）。</summary>
        public const string ReviveRequest = "D2.Combat.ReviveRequest";

        // ═════════════════════════════════════════════════════════════════════
        // 角色（Player）
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>角色属性/派生属性变化（参数 <c>Def.PlayerStatsDto</c>）。</summary>
        public const string PlayerStatsChanged = "D2.Player.StatsChanged";

        /// <summary>金币变化（参数 <see cref="int"/> 当前金币）。</summary>
        public const string GoldChanged = "D2.Player.GoldChanged";

        /// <summary>请求分配属性点（参数 <c>Def.StatAllocArgs</c>）。</summary>
        public const string StatAllocateRequest = "D2.Player.StatAllocate";

        // ═════════════════════════════════════════════════════════════════════
        // 技能（Skill）
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>请求学习技能（参数 <see cref="int"/> skillId）。</summary>
        public const string SkillLearnRequest = "D2.Skill.LearnRequest";

        /// <summary>技能学会（参数 <see cref="int"/> skillId）。</summary>
        public const string SkillLearned = "D2.Skill.Learned";

        /// <summary>技能树数据变化（参数 <c>Def.SkillTreeArgs</c>，技能面板刷新）。</summary>
        public const string SkillTreeChanged = "D2.Skill.TreeChanged";

        /// <summary>当前右键技能变化（参数 <see cref="int"/> skillId；-1 = 普通攻击）。</summary>
        public const string SkillSelected = "D2.Skill.Selected";

        /// <summary>技能施放（参数 <see cref="int"/> skillId）。</summary>
        public const string SkillCast = "D2.Skill.Cast";

        /// <summary>技能冷却就绪（参数 <see cref="int"/> skillId）。</summary>
        public const string SkillReady = "D2.Skill.Ready";

        /// <summary>
        /// 请求把某个技能槽键（原版 `F1`~`F8`）对应的已学技能绑到左/右键技能格
        /// （参数 <see cref="int"/> = 槽号 **1..8**：1~4 绑左键、5~8 绑右键）。
        /// <para>★ impl-I-input 新增。发送方 = `Module/Input/InputReader.PollHotkeys`
        /// （读键的唯一入口；键位单一来源 = `Def/GameKeyAlias.cs` 的 `SkillSlotKey(int)`）；
        /// 收方 = `Module/Skill/SkillModule`（把"第 N 个已学技能"解析出来并调契约的
        /// `ISkillModule.AssignToButton(button, skillId)`）。</para>
        /// <para>为什么用事件而不是新增契约方法：`ISkillModule` 是**冻结契约**不许改；
        /// 而「槽号 → 第 N 个已学技能」这层解析属技能模块的业务，不该塞进输入层。</para>
        /// <para>绑定结果经既有 `CharacterSave.buttonSkills` 落档（读档时 `LoadButtonsFrom` 还原），
        /// 并经 <see cref="SkillButtonsChanged"/> 通知 HUD。</para>
        /// </summary>
        public const string SkillSlotAssignRequest = "D2.Skill.SlotAssignRequest";

        /// <summary>
        /// 左右键技能格的绑定发生变化（参数 <c>Def.SkillButtonsArgs</c>）。
        /// <para>★ impl-I-input 新增。发送方 = `Module/Skill/SkillModule`
        /// （`SelectSkill` / `AssignToButton` / `ResetForClass` 之后）；
        /// 收方 = `UI/HudPanel`（把 `LeftSkill` / `RightSkill` 两格的图标换成绑定技能的图标）。</para>
        /// <para>与既有的 <see cref="SkillSelected"/> 的区别：那个只带**右键**技能 id、只表达
        /// "当前选中的施放技能"（战斗/HUD 语义）；本事件同时带左右两格 + 显示名，专供 HUD 换图。</para>
        /// </summary>
        public const string SkillButtonsChanged = "D2.Skill.ButtonsChanged";

        // ═════════════════════════════════════════════════════════════════════
        // 物品（Item）
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>地面掉落新物品（参数 <c>Def.ItemStack</c>）。</summary>
        public const string ItemDropped = "D2.Item.Dropped";

        /// <summary>地面物品被移除（参数 <see cref="int"/> 地面物品 id）。</summary>
        public const string GroundItemRemoved = "D2.Item.GroundRemoved";

        /// <summary>使用物品（药水/卷轴）（参数 <c>Def.ItemStack</c>）。</summary>
        public const string ItemUsed = "D2.Item.Used";

        /// <summary>装备变化（参数 <c>Def.InventoryChangedArgs</c>）。</summary>
        public const string EquipChanged = "D2.Item.EquipChanged";

        /// <summary>请求装备/卸下背包物品（参数 <see cref="int"/> 背包锚点格索引）。</summary>
        public const string EquipToggleRequest = "D2.Item.EquipToggle";

        /// <summary>请求从背包移出/丢弃物品（参数 <see cref="int"/> 背包锚点格索引）。</summary>
        public const string ItemDropRequest = "D2.Item.DropRequest";

        /// <summary>背包已满（无参）——收方 Toast「背包已满」。</summary>
        public const string InventoryFull = "D2.Item.InventoryFull";

        // ── 以下 4 条为 **agent-12 新增**（只增不改：`docs/agents/agent-12-*.md` §3 第 7 项）──
        //    UI 侧原先只能打 Warn「`Core/Events.cs` 里没有对应事件」而无法接线（见 `UI/InventoryPanel.cs`
        //    与 `UI/DeathPanel.cs` 的注释）；常量补齐后由 `App/AppEventRouting.cs` 统一转发到门面方法。

        /// <summary>
        /// 请求卸下某装备槽。
        /// 参数 <see cref="int"/> = `((int)Diablo2.Def.ItemSlot &amp; 0xFF) | (slotIndex &lt;&lt; 8)`
        /// （单槽位 slotIndex = 0；**戒指/武器组等同槽多件**用高位区分）。
        /// 收方：`App/AppEventRouting.cs` → `IItemModule.Unequip(ItemSlot, int)`。
        /// </summary>
        public const string UnequipRequest = "D2.Item.UnequipRequest";

        /// <summary>
        /// 请求在背包内移动/交换物品。
        /// 参数 <see cref="int"/> = `fromAnchor | (toAnchor &lt;&lt; 16)`（两个背包锚点格索引）。
        /// <para>
        /// ★ 片 G1 起**真的落地**（修用户报的「道具没法拖动！」）：收方 =
        /// `App/AppEventRouting.cs` → `IItemModule.MoveItem(from, to, out reason)`
        /// （`Module/Item/ItemModule.cs`，落格实现 = `Inventory.Move`：空格放下 / 与另一件交换 /
        /// 大件放小空位 ⇒ 拒绝并把可直接展示的中文原因回给 UI 做 Toast）。
        /// 发送方 = `UI/InventoryPanel.OnEndDrag`（`PlanDrop` 判出 `DropKind.Move`；落面板外走
        /// `ItemDropRequest` = 丢地上）。
        /// </para>
        /// </summary>
        public const string MoveInInventoryRequest = "D2.Item.MoveInInventoryRequest";

        /// <summary>
        /// 玩家复活完成（无参）。
        /// 发送方：`App/AppEventRouting.cs`（在 `Events.ReviveRequest` 被 `CombatModule` 处理完、
        /// 且玩家已 `IsDead == false` 时广播）—— 因为 `CombatModule` / `PlayerModule` 都不发本事件。
        /// </summary>
        public const string Revived = "D2.Player.Revived";

        /// <summary>
        /// 请求打开商店（参数 <see cref="int"/> = `Def.NpcId`）。
        /// 收方：`App/AppEventRouting.cs` → `INpcModule.GetShop(npcId)` 后发 `Events.ShopOpen`（面板/HUD 订阅它）。
        /// </summary>
        public const string ShopOpenRequest = "D2.Npc.ShopOpenRequest";

        // ═════════════════════════════════════════════════════════════════════
        // 传送点（Waypoint）★ 片 g1-resume 新增
        //
        // 契约（**只增不改**）：
        //   · 锚点数据 = `IMapModule.WaypointPoints`（罗格营地 1 个，坐标出自原版表）；
        //   · "点它 ⇒ 面板开" 不走事件：`App/AppWaypoint.cs` 自己判点击 + 就近到达后
        //     `Game.UI.Open<WaypointPanel>(args)`（与 `NpcModule` 的"点 NPC 走过去说话"同形）；
        //   · 本事件 = **面板里选了目的地**那一步，见下。
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>
        /// 请求传送到某个区域（参数 <see cref="int"/> = `(int)Def.AreaId`）。
        /// <para>
        /// 发送方 = `UI/WaypointPanel.cs`（点列表里的一条目的地）。收方 = `App/AppWaypoint.cs`：
        /// 校验"该区域确实是已激活的目的地"后，转发 `Events.ExitEntered`（切区域那条既有链路
        /// `AppFlow.EnterArea`）——⛔ 本事件**不绕过** `ExitEntered`，因为区域切换（重生成地图 /
        /// 移怪 / 挪玩家 / 关所有面板）的唯一实现就在那条链上。
        /// </para>
        /// <para>非法请求（未激活 / 就是当前区域 / 地图未生成）⇒ 收方点名 Warn 并拒绝，⛔ 不静默。</para>
        /// </summary>
        public const string WaypointTravelRequest = "D2.Map.WaypointTravelRequest";

        /// <summary>
        /// 换区那次整图重铺**建满并已切换**（无参数）。★ travel-black 新增。
        /// <para>
        /// 发送方 = `Module/Map/MapView.cs`（只在"这次重铺是**换区**触发的"那一次切换/保底铺完后发一次；
        /// 贴图到位重铺 / 迷雾开关重铺**不发**）。收方 = `Module/Flow/AppFlow.cs`。
        /// </para>
        /// <para>
        /// 为什么需要它：换区时玩家/相机要**等新区域的地砖建好**再落位 —— 否则相机已经跳到落点、
        /// 而生效集还是**旧区**那张图（落点超出旧图范围 ⇒ 屏上零地砖 = 落地整屏黑；实机逐帧量到 ≈1.84 s，
        /// 见 `.ai-tmp/screenshots/travelblack_tb1.log`）。所以 `AppFlow.EnterArea` 拆成两拍：
        /// ① 生成 + 登记重铺（相机**留在旧区**，旧图仍完整可见）→ ② 收到本事件才挪玩家/相机/怪 + 关读条屏。
        /// </para>
        /// </summary>
        public const string MapAreaReady = "D2.Map.AreaReady";

        // ═════════════════════════════════════════════════════════════════════
        // 任务（Quest）
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>请求接取任务（参数 <see cref="int"/> 见 <c>Def.QuestId</c>）。</summary>
        public const string QuestAcceptRequest = "D2.Quest.AcceptRequest";

        /// <summary>请求交付任务（参数 <see cref="int"/> 见 <c>Def.QuestId</c>）。</summary>
        public const string QuestTurnInRequest = "D2.Quest.TurnInRequest";

        /// <summary>任务完成（参数 <see cref="int"/> 见 <c>Def.QuestId</c>）。</summary>
        public const string QuestCompleted = "D2.Quest.Completed";

        /// <summary>任务未达成交付条件（参数 <see cref="int"/> 见 <c>Def.QuestId</c>）。</summary>
        public const string QuestTurnInDenied = "D2.Quest.TurnInDenied";

        // ═════════════════════════════════════════════════════════════════════
        // NPC / 商店（Npc）
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>请求推进对话（参数 <see cref="int"/> 选项下标；0 = 关闭）。</summary>
        public const string DialogOptionChosen = "D2.Npc.DialogOption";

        /// <summary>关闭对话（无参）。</summary>
        public const string DialogClose = "D2.Npc.DialogClose";

        /// <summary>请求购买（参数 <c>Def.ShopTradeArgs</c>）。</summary>
        public const string ShopBuyRequest = "D2.Npc.Buy";

        /// <summary>请求出售（参数 <c>Def.ShopTradeArgs</c>）。</summary>
        public const string ShopSellRequest = "D2.Npc.Sell";

        /// <summary>请求修理（参数 <c>Def.ShopTradeArgs</c>；index &lt; 0 = 全部修理）。</summary>
        public const string ShopRepairRequest = "D2.Npc.Repair";

        /// <summary>交易完成（参数 <c>Def.ShopOpenArgs</c>，商店面板刷新）。</summary>
        public const string ShopChanged = "D2.Npc.ShopChanged";

        /// <summary>关闭商店（无参）。</summary>
        public const string ShopClose = "D2.Npc.ShopClose";

        // ═════════════════════════════════════════════════════════════════════
        // 存档（Save）
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>存档完成（参数 <see cref="bool"/> 是否成功）。</summary>
        public const string SaveDone = "D2.Save.Done";

        /// <summary>读档完成（参数 <c>Def.CharacterSave</c>；null = 失败）。</summary>
        public const string LoadDone = "D2.Save.LoadDone";

        // ═════════════════════════════════════════════════════════════════════
        // 音频（Audio）—— 音效触发点由各模块 Emit，Audio 模块统一收
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>播放 2D 音效（参数 <see cref="string"/> 音效键，见 `ResPaths.Sfx`）。</summary>
        public const string PlaySfx = "D2.Audio.PlaySfx";

        /// <summary>切换 BGM（参数 <see cref="string"/> BGM 键，见 `ResPaths.Bgm`；空串 = 停）。</summary>
        public const string PlayBgm = "D2.Audio.PlayBgm";

        /// <summary>音量变化（参数 <c>Def.AudioVolumeArgs</c>）。</summary>
        public const string VolumeChanged = "D2.Audio.VolumeChanged";

        // ═════════════════════════════════════════════════════════════════════
        // 流程状态机站点名 / 触发器名（`Game.Fsm` 用；避免裸字符串）
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>
        /// `Game.Fsm.RegisterState` / `AddTransition` / `Force` 用的站点名与触发器名。
        /// 站点表与迁移图见 `docs/步骤文档.md` §5 与 `tools/ai-skill/registry.md`「流程站点」。
        /// </summary>
        public static class Fsm
        {
            // 站点（registerState / Force）
            /// <summary>启动画面。</summary>
            public const string StateBoot = "Boot";

            /// <summary>主菜单（设置是其子面板，仍停在本站点）。</summary>
            public const string StateMainMenu = "MainMenu";

            /// <summary>角色选择。</summary>
            public const string StateCharSelect = "CharSelect";

            /// <summary>创建角色。</summary>
            public const string StateCharCreate = "CharCreate";

            /// <summary>读条进图。</summary>
            public const string StateLoading = "Loading";

            /// <summary>游戏内。</summary>
            public const string StateStage = "Stage";

            /// <summary>暂停。</summary>
            public const string StatePause = "Pause";

            // 触发器（AddTransition / Trigger）——顺序与迁移图一致
            /// <summary>Boot → MainMenu。</summary>
            public const string TriggerBootDone = "BootDone";

            /// <summary>MainMenu → CharSelect（新游戏）。</summary>
            public const string TriggerNewGame = "NewGame";

            /// <summary>MainMenu → CharSelect（继续）。</summary>
            public const string TriggerContinue = "Continue";

            /// <summary>CharSelect → CharCreate（无角色）。</summary>
            public const string TriggerNeedCreate = "NeedCreate";

            /// <summary>CharCreate → CharSelect（建角完成）。</summary>
            public const string TriggerCreated = "Created";

            /// <summary>CharSelect → Loading。</summary>
            public const string TriggerEnterStage = "EnterStage";

            /// <summary>Loading → Stage。</summary>
            public const string TriggerStageReady = "StageReady";

            /// <summary>Stage → Pause。</summary>
            public const string TriggerPause = "Pause";

            /// <summary>Pause → Stage。</summary>
            public const string TriggerResume = "Resume";

            /// <summary>Pause → MainMenu。</summary>
            public const string TriggerToMain = "ToMain";
        }
    }
}
