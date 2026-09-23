// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · App/AppEventRouting.cs    （agent-12「最后一次接线」，agent-12 §3 的第 7 项落地）
// **UI / 输入发来的「请求事件」→ 门面方法**的转发层。
//
// 为什么需要这一层：`UI/**` 不许 `using Diablo2.Module`（分层自检 ③），只能 `Game.Event.Emit`
// 请求；而**并非每个请求都有模块订阅**。已核对（2026-03）：
//   · 有订阅者、无需本层：`AttackRequest`(Combat) `PickupRequest`/`EquipToggleRequest`/
//     `ItemDropRequest`/`UseBeltRequest`(Item) `NpcInteractRequest`/`DialogOptionChosen`/
//     `ShopBuy|Sell|RepairRequest`(Npc) `QuestAccept|TurnInRequest`(Quest)
//     `StatAllocateRequest`(Player) `VolumeChanged`(AudioHook) `ReviveRequest`(Combat) …
//   · **没有订阅者**（本层补齐）：
//       `SkillLearnRequest`      技能树面板点「学习」—— `SkillModule` 一个事件都没订阅
//       `SkillSelected`          技能树面板右键设按钮技能 —— 同上
//       `UnequipRequest`         装备栏点击卸下（**agent-12 新增的常量**）
//       `ShopOpenRequest`        显式「打开商店」（**agent-12 新增的常量**）
//       `Revived`                复活完成通知（**agent-12 新增的常量**；发送方见下）
//       `MoveInInventoryRequest` 背包内移动物品（**agent-12 新增的常量**）—— ★ 片 G1 起**真的落地**：
//                                   `IItemModule.MoveItem(from, to, out reason)`（八年前那句
//                                   「无 Move/Swap ⇒ 无法移动物品」的 Warn 已删除）
//
// ⛔ 本层**不做判定**：不查血量/距离/金币/背包空间 —— 那些由模块的门面方法自己判并打日志。
// ─────────────────────────────────────────────────────────────────────────────

using CloverEngine;
using Diablo2.Core;
using Diablo2.Def;

namespace Diablo2.App
{
    /// <summary>UI/输入请求事件 → 门面方法 的转发。</summary>
    internal static class AppEventRouting
    {
        private const string Tag = AppWiring.Tag;

        /// <summary>`SkillSelected` 的防回灌标记（`SkillModule.SelectSkill` 自己也会 Emit 它）。</summary>
        private static bool _forwardingSkillSelected;

        /// <summary>最近一次已转发的技能 id（同一技能重复点击不再转发）。</summary>
        private static int _lastSelectedSkill = int.MinValue;

        /// <summary>是否观察到过玩家死亡（用于判「复活」而不是「误解一次 ReviveRequest」）。</summary>
        private static bool _deadObserved;

        public static void Install(AppContext ctx)
        {
            var bus = Game.Event;
            if (bus == null)
            {
                Game.Logger.Error(Tag, "AppEventRouting.Install：Game.Event 为 null ⇒ 请求转发未生效");
                return;
            }

            bus.On<int>(Events.SkillLearnRequest, OnSkillLearnRequest);
            bus.On<int>(Events.SkillSelected, OnSkillSelected);
            bus.On<int>(Events.UnequipRequest, OnUnequipRequest);
            bus.On<int>(Events.ShopOpenRequest, OnShopOpenRequest);
            bus.On<int>(Events.MoveInInventoryRequest, OnMoveInInventoryRequest);

            // `Revived` 的发送方：本类在「ReviveRequest 处理完之后」发（本类订阅晚于 CombatModule，
            // 见 Install 的调用时序），并且**只有真的观测到死亡时才发**。
            bus.On(Events.PlayerDied, OnPlayerDied);
            bus.On(Events.ReviveRequest, OnReviveRequest);
        }

        // ═════════════════════════════════════════════════════════════════════
        // 技能
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>技能树面板左键「学习」→ `ISkillModule.Learn`。</summary>
        private static void OnSkillLearnRequest(int skillId)
        {
            var ctx = AppWiring.Ctx;
            if (ctx?.Skill == null) { AppWiring.Missing("ISkillModule"); return; }
            if (!ctx.Skill.Learn(skillId))
            {
                Game.Logger.Warn(Tag, $"学习技能 {skillId} 失败（等级/前置/技能点不满足，原因见 [Skill] 日志）");
            }
        }

        /// <summary>
        /// 技能树面板右键「设为按钮技能」→ `ISkillModule.SelectSkill`。
        /// ⚠️ `SelectSkill` 内部会 Emit `SkillSelected` ⇒ 必须防回灌（否则无限递归）。
        /// </summary>
        private static void OnSkillSelected(int skillId)
        {
            if (_forwardingSkillSelected) return;               // 由本类转发触发的回声 ⇒ 丢弃

            var ctx = AppWiring.Ctx;
            if (ctx?.Skill == null) { AppWiring.Missing("ISkillModule"); return; }

            if (skillId == _lastSelectedSkill) return;          // 同一技能重复点击：状态没变

            _lastSelectedSkill = skillId;
            _forwardingSkillSelected = true;
            try { ctx.Skill.SelectSkill(skillId); }
            finally { _forwardingSkillSelected = false; }
        }

        // ═════════════════════════════════════════════════════════════════════
        // 装备 / 背包 / 商店 / 复活
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>
        /// 卸下装备：参数 = `((int)Def.ItemSlot &amp; 0xFF) | (slotIndex &lt;&lt; 8)`
        /// （`Events.UnequipRequest` 的注释里有同一口径）。契约 `IItemModule.Unequip(ItemSlot, int)` 不变。
        /// </summary>
        private static void OnUnequipRequest(int packed)
        {
            var ctx = AppWiring.Ctx;
            if (ctx?.Item == null) { AppWiring.Missing("IItemModule"); return; }

            var slot = (ItemSlot)(packed & 0xFF);
            var slotIndex = packed >> 8;
            if (!ctx.Item.Unequip(slot, slotIndex))
            {
                Game.Logger.Warn(Tag, $"卸下装备失败：slot={slot} slotIndex={slotIndex}（槽为空或背包放不下，见 [Item] 日志）");
            }
        }

        /// <summary>显式「打开商店」：取快照后发 `Events.ShopOpen`（面板与 HUD 都订阅它）。</summary>
        private static void OnShopOpenRequest(int npcId)
        {
            var ctx = AppWiring.Ctx;
            if (ctx?.Npc == null) { AppWiring.Missing("INpcModule"); return; }

            var shop = ctx.Npc.GetShop(npcId);
            if (shop == null)
            {
                Game.Logger.Warn(Tag, $"{Events.ShopOpenRequest} 收到 npcId={npcId}，但该 NPC 没有商店（或不存在）⇒ 不打开商店面板");
                return;
            }

            Game.Event.Emit(Events.ShopOpen, shop);
            Game.Logger.Info(Tag, $"商店已打开：{shop.npcName}（{shop.stock.Count} 件商品，金币 {shop.playerGold}）");
        }

        /// <summary>
        /// 背包内移动/交换：参数 = `fromAnchor | (toAnchor &lt;&lt; 16)`（口径同 `Core/Events.cs` 与
        /// `UI/InventoryPanel.PackMoveInInventory`）。
        /// <para>
        /// ★ **片 G1 落地**（修用户报的「道具没法拖动！」）：本层原先只打一条
        /// 「`IItemModule` 无 Move/Swap 方法 ⇒ 无法移动物品」的 Warn（UI 侧意图对了，**落格没地方去**）。
        /// `IItemModule.MoveItem(from, to, out reason)` 补齐后，这里**真的转发**；失败时把模块给出的
        /// **同一句话**既写日志又弹 Toast（⛔ 不许只把 Warn 换成另一条 Warn）。
        /// </para>
        /// </summary>
        private static void OnMoveInInventoryRequest(int packed)
        {
            var ctx = AppWiring.Ctx;
            if (ctx?.Item == null) { AppWiring.Missing("IItemModule"); return; }

            var fromAnchor = packed & 0xFFFF;
            var toAnchor = (packed >> 16) & 0xFFFF;

            if (ctx.Item.MoveItem(fromAnchor, toAnchor, out var reason))
            {
                Game.Logger.Info(Tag, $"{Events.MoveInInventoryRequest}({fromAnchor} → {toAnchor})：移动/交换已完成");
                return;
            }

            Game.Logger.Warn(Tag, $"{Events.MoveInInventoryRequest}({fromAnchor} → {toAnchor}) 被拒绝：{reason}");
            if (!string.IsNullOrEmpty(reason)) Game.UI.Toast(reason);
        }

        private static void OnPlayerDied()
        {
            _deadObserved = true;
        }

        /// <summary>
        /// `ReviveRequest` 由死亡面板发出、`CombatModule` 处理（`ICombatModule.RevivePlayer`）。
        /// 若玩家确实不再 `IsDead`，就广播 `Events.Revived`（**`CombatModule` / `PlayerModule`
        /// 都不发这个事件**，已登记为需返工项；本层是临时发送方）。
        /// <para>
        /// ★ **片 11 修正（BL-5，实测）**：旧实现假设"本类订阅晚于 `CombatModule`" —— **实测相反**：
        /// 本层 handler 在 `01:56:17.290` 跑、Combat 的复活在 `01:56:17.293` ⇒ 判 `IsDead` 时它**还是 true**
        /// ⇒ 旧实现直接 `Warn` + `return`，**从不广播 `Revived`**，死亡屏只能靠 3s 看门狗放开按钮
        /// （用户看到"复活未完成，请重试"，点「繼續」后屏不自动关）。
        /// </para>
        /// <para>
        /// 修法 = **把复查推到下一帧**：`ReviveRequest` 的全部订阅者都在**本帧内**跑完（Combat 的复活是
        /// 同步写 `IsDead`），下一帧读到的一定是最终值 ⇒ 不再依赖"谁先订阅"这个脆弱假设。
        /// 必须用 `AfterUnscaled`：死亡屏时 `Time.timeScale == 0`，`After` 永不触发（`constraints.md` #2）。
        /// </para>
        /// </summary>
        private static void OnReviveRequest()
        {
            var ctx = AppWiring.Ctx;
            if (ctx?.Player == null) { AppWiring.Missing("IPlayerModule"); return; }

            if (!_deadObserved)
            {
                Game.Logger.Info(Tag, $"{Events.ReviveRequest}：本局未观测到死亡（按钮本应置灰）⇒ 不广播 {Events.Revived}");
                return;
            }

            if (Game.Timer == null)
            {
                // 非预期分支：定时器拿不到 ⇒ 只能当场判（退化为旧行为），但要留痕
                Game.Logger.Warn(Tag, $"Game.Timer 为 null ⇒ {Events.Revived} 的下一帧复查无法安排，当场判定");
                CheckRevivedNow("当场");
                return;
            }

            Game.Timer.AfterUnscaled(0f, () => CheckRevivedNow("下一帧复查"));
        }

        /// <summary>`ReviveRequest` 之后的实际判定（当场 / 下一帧共用，口径一致）。</summary>
        private static void CheckRevivedNow(string how)
        {
            var ctx = AppWiring.Ctx;
            if (ctx?.Player == null) { AppWiring.Missing("IPlayerModule"); return; }

            if (ctx.Player.IsDead)
            {
                Game.Logger.Warn(Tag,
                    $"{Events.ReviveRequest} 处理完毕（{how}）但玩家仍 IsDead ⇒ 复活未成功，" +
                    $"不广播 {Events.Revived}（见 [Combat] 死亡链日志）");
                return;
            }

            _deadObserved = false;
            Game.Event.Emit(Events.Revived);
            Game.Logger.Info(Tag, $"[Assert] 玩家已复活（IsDead=false，{how}）⇒ 广播 {Events.Revived}");
        }
    }
}
