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

        /// <summary>请求与 NPC 交互/对话（参数 <see cref="int"/> 见 <c>Def.NpcId</c>）。</summary>
        public const string NpcInteractRequest = "D2.Input.NpcInteract";

        /// <summary>请求切换面板（参数 <see cref="string"/> 面板类名）。</summary>
        public const string PanelToggleRequest = "D2.Ui.PanelToggle";

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
        /// ⚠️ **暂无收方**：`IItemModule`（契约冻结）没有 `MoveItem/Swap`
        /// ⇒ `App/AppEventRouting.cs` 只打一条可定位的 Warn，等主 agent 裁决（加契约方法或 UI 改两步走）。
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
