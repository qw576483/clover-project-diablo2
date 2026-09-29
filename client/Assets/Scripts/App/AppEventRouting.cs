// ─────────────────────────────────────────────────────────────────────────────
// **UI / 输入发来的「请求事件」→ 门面方法**的转发层。
//
// 为什么需要这一层：`UI/**` 不许 `using Diablo2.Module`（分层自检 ③），只能 `Game.Event.Emit`
// 请求；而**并非每个请求都有模块订阅**。已核对（2026-03）：
//   · 有订阅者、无需本层：`AttackRequest`(Combat) `PickupRequest`/`EquipToggleRequest`/
//     `ItemDropRequest`/`UseBeltRequest`(Item) `NpcInteractRequest`/`DialogOptionChosen`/
//     `ShopBuy|Sell|RepairRequest`(Npc)
//     `StatAllocateRequest`(Player) `VolumeChanged`(AudioHook) `ReviveRequest`(Combat) …
//   · **没有订阅者**（本层补齐）：
//       `SkillLearnRequest`      技能树面板点「学习」—— `SkillModule` 一个事件都没订阅
//       `SkillSelected`          技能树面板右键设按钮技能 —— 同上
//                                   `IItemModule.MoveItem(from, to, out reason)`（八年前那句
//
// 本层**不做判定**：不查血量/距离/金币/背包空间 —— 那些由模块的门面方法自己判并打日志。
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
        /// `SelectSkill` 内部会 Emit `SkillSelected` ⇒ 必须防回灌（否则无限递归）。
        /// <para>幂等判据取**模块当前的右键技能**（`SelectedSkillId`），不是"上次转发过的 id"：
        /// 后者会把「先右键一个还没学的技能（转发过、被模块拒），学会之后再右键它」误判成重复而整个丢掉。</para>
        /// </summary>
        private static void OnSkillSelected(int skillId)
        {
            if (_forwardingSkillSelected) return;               // 由本类转发触发的回声 ⇒ 丢弃

            var ctx = AppWiring.Ctx;
            if (ctx?.Skill == null) { AppWiring.Missing("ISkillModule"); return; }

            if (ctx.Skill.SelectedSkillId == skillId) return;   // 已经是当前右键技能：状态没变

            _forwardingSkillSelected = true;
            try { ctx.Skill.SelectSkill(skillId); }
            finally { _forwardingSkillSelected = false; }
        }

        // ═════════════════════════════════════════════════════════════════════
        // 装备 / 背包 / 商店
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
        /// 「`IItemModule` 无 Move/Swap 方法 ⇒ 无法移动物品」的 Warn（UI 侧意图对了，**落格没地方去**）。
        /// `IItemModule.MoveItem(from, to, out reason)` 补齐后，这里**真的转发**；失败时把模块给出的
        /// **同一句话**既写日志又弹 Toast（不许只把 Warn 换成另一条 Warn）。
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
    }
}
