// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · Module/Npc/NpcModule.cs
// NPC 门面实现：**5 个 NPC（阿卡拉 / 卡夏 / 恰西 / 基德 / 瓦瑞夫）的站位、对话、商店、修理**。
//
// 装配契约：`internal sealed class NpcModule : INpcModule`，无参构造（供 `AppContext.AutoWire()`）。
//
// 站位来自 `IMapModule.NpcPoints`（**下标 = `(int)NpcId`**，见 `docs/agents/_common.md` §3.5）：
//   地图（罗格营地）每次生成后重建一次；地图 seed/区域变化时自动重建。
//
// 依赖（只走接口）：
//   · `IMapModule.NpcPoints / Seed / Area` —— 站位与"同一局货物不变"的 seed；
//   · `IQuestModule.DenOfEvil / CanTurnInDen` —— 对话文本阶段 + 可否接/交任务；
//   · `IItemModule` —— 金币结算 / 背包空间校验 / 修理 / 造货。
//
// 事件：
//   发 `DialogOpen(NpcDialogArgs)` / `DialogClose` / `ShopOpen(ShopOpenArgs)` / `ShopChanged(ShopOpenArgs)` /
//      `QuestAcceptRequest(int)` / `QuestTurnInRequest(int)`
//   收 `DialogOptionChosen(int)` / `DialogClose` / `ShopBuyRequest(ShopTradeArgs)` /
//      `ShopSellRequest(ShopTradeArgs)` / `ShopRepairRequest(ShopTradeArgs)` / `ShopClose`
// ─────────────────────────────────────────────────────────────────────────────

using System.Collections.Generic;
using CloverEngine;
using Diablo2.App;
using Diablo2.Core;
using Diablo2.Def;
using UnityEngine;

namespace Diablo2.Module.Npc
{
    /// <summary>NPC 门面实现（对话 / 商店 / 修理）。</summary>
    internal sealed class NpcModule : INpcModule
    {
        /// <summary>
        /// NPC 名称 = **原版串表**里的名字（面板的说话人行显示的就是它，所以必须与原版一致；
        /// 原版中文是繁体）。串 id：阿卡拉 **2892**（键 `Akara`）/ 卡夏 **2893**（`Kashya`）/
        /// 恰西 **2894**（`Charsi`）/ 基得 **2891**（`Gheed`）/ 瓦瑞夫 **2896**（`Warriv`）。
        /// <para>⚠️ 上一版写的是「基德」，原版串是「**基得**」（2891）⇒ 本轮按原版串改。</para>
        /// </summary>
        private static readonly string[] Names = { "阿卡拉", "卡夏", "恰西", "基得", "瓦瑞夫" };

        private readonly List<NpcDef> _defs = new List<NpcDef>();
        private readonly NpcShop _shop = new NpcShop();

        private int _builtSeed = int.MinValue;
        private int _builtArea = -1;

        /// <summary>当前正在对话的 NPC（`Events.DialogOptionChosen` 只带下标，必须记住是谁）。</summary>
        private int _currentNpcId = (int)NpcId.None;

        /// <summary>「点了这个 NPC，走到就说话」的意图（-1 = 无）。见 <see cref="OnMoveCommand"/>。</summary>
        private int _pendingNpcId = (int)NpcId.None;

        public NpcModule()
        {
            var bus = Game.Event;
            if (bus == null)
            {
                Log.Warn("Npc", "NpcModule 构造时 Game.Event 为 null（引擎未启动？）⇒ 不订阅事件（只能靠显式调用驱动）");
                return;
            }
            bus.On<int>(Events.DialogOptionChosen, OnDialogOptionChosen);
            bus.On(Events.DialogClose, OnDialogClose);
            // ★ 任务阶段 → 对话的**实时**联动（原版行为：在阿卡拉处接/交任务后，台词当场就换）。
            //   没有这一条时，面板要"关掉再打开"才看得到新台词（本轮实测：接取后正文仍是接取前那段，
            //   `canAcceptQuest` 也还是旧值）⇒ 验收 #40「状态机驱动 NPC 对话」不成立。
            bus.On<QuestStateDto>(Events.QuestChanged, OnQuestChanged);
            bus.On<int>(Events.NpcInteractRequest, OnInteractRequest);
            bus.On<ShopTradeArgs>(Events.ShopBuyRequest, OnBuyRequest);
            bus.On<ShopTradeArgs>(Events.ShopSellRequest, OnSellRequest);
            bus.On<ShopTradeArgs>(Events.ShopRepairRequest, OnRepairRequest);
            bus.On(Events.ShopClose, OnShopClose);
            // 「点击 NPC → 走过去 → 自动对话」：当前**没有任何模块发 `Events.NpcInteractRequest`**
            // （点击只落到 `Events.MoveCommand`）⇒ 本模块用落点识别"点了哪个 NPC"，到位后交互一次。
            // 若将来 Input/UI 补上 `NpcInteractRequest`，两条路径并存且不冲突。
            bus.On<Vector2Int>(Events.MoveCommand, OnMoveCommand);
        }

        // ── 定义 ───────────────────────────────────────────────────────────────

        /// <summary>全部 NPC 定义（5 个；站位随地图重建）。</summary>
        public IReadOnlyList<NpcDef> All
        {
            get
            {
                EnsureBuilt();
                return _defs;
            }
        }

        /// <summary>按 id 取定义；不存在返回 null 并打日志。</summary>
        public NpcDef Get(int npcId)
        {
            EnsureBuilt();
            for (var i = 0; i < _defs.Count; i++)
            {
                if (_defs[i].id == npcId) return _defs[i];
            }
            Log.Warn("Npc", $"Get(npcId={npcId})：没有这个 NPC（本项目只有 5 个：0={Names[0]} 1={Names[1]} "
                + $"2={Names[2]} 3={Names[3]} 4={Names[4]}）");
            return null;
        }

        /// <summary>
        /// 取离某格最近、且在 `GameConst.TalkRange` 内的 NPC；没有返回 null。
        /// <para>
        /// ★ 只有**罗格营地**才有 NPC（`NpcDef.areaId` 恒为 `AreaId.Town`）。非城镇区域里
        /// `IMapModule.NpcPoints` 是空列表 ⇒ `EnsureBuilt` 的站位会退化成 (0,0)。
        /// 若不在这里拦住，「洞里点/走到 (0,0) 附近」会**误开阿卡拉的对话**
        /// （agent-26 验收 #38 实机复现：进邪恶洞穴后 `MoveCommand` 落点靠近 (0,0)
        ///  ⇒ 洞里弹出阿卡拉对话，且面板带着"未清光"的旧参数 ⇒ 回城交付时『交付任务』按钮是灰的、
        ///  真鼠标点击无效，只能靠 invoke 兜底）。
        /// </para>
        /// </summary>
        public NpcDef FindNearest(Vector2Int grid)
        {
            EnsureBuilt();

            if (!InTownForNpc())
            {
                var m0 = Map;
                Log.Info("Npc", $"FindNearest(({grid.x},{grid.y}))：当前区域 {(m0 != null ? m0.Area.ToString() : "无地图")} "
                    + "不是罗格营地 ⇒ 没有可交互 NPC（NPC 只属于城镇；站位在非城镇会退化为 (0,0)，故在此拦住）");
                return null;
            }

            NpcDef best = null;
            var bestD = float.MaxValue;
            for (var i = 0; i < _defs.Count; i++)
            {
                var d = _defs[i];
                var dx = grid.x - d.gridX;
                var dy = grid.y - d.gridY;
                var dist = Mathf.Sqrt(dx * dx + dy * dy);
                if (dist > GameConst.TalkRange) continue;
                if (dist < bestD)
                {
                    bestD = dist;
                    best = d;
                }
            }
            if (best == null)
            {
                Log.Info("Npc", $"FindNearest(({grid.x},{grid.y}))：{GameConst.TalkRange:0.00} 格内没有 NPC");
            }
            return best;
        }

        // ── 对话 ───────────────────────────────────────────────────────────────

        /// <summary>交互（发 `Events.DialogOpen`；有商店时面板里给出商店入口选项）。成功返回 true。</summary>
        public bool Interact(int npcId)
        {
            var def = Get(npcId);
            if (def == null) return false;

            // 非城镇区域不提供 NPC 交互（见 FindNearest 的 ★ 注释：防止"洞里也能跟阿卡拉说话"）
            if (!InTownForNpc())
            {
                var m0 = Map;
                Log.Warn("Npc", $"Interact(npcId={npcId})：当前区域 {(m0 != null ? m0.Area.ToString() : "无地图")} "
                    + "不是罗格营地 ⇒ 拒绝交互（NPC 只存在于城镇）");
                return false;
            }

            var args = GetDialog(npcId);
            if (args == null)
            {
                Log.Warn("Npc", $"Interact(npcId={npcId})：对话组装失败 ⇒ 拒绝");
                return false;
            }

            _currentNpcId = npcId;
            Game.Event?.Emit(Events.DialogOpen, args);
            Log.Info("Npc", $"对话开始：{def.name}（questGiver={def.isQuestGiver} hasShop={def.hasShop} "
                + $"canAccept={args.canAcceptQuest} canTurnIn={args.canTurnInQuest} 选项 {args.options.Count} 项）");
            return true;
        }

        /// <summary>取当前对话内容（**文本随任务阶段变化**）。</summary>
        public NpcDialogArgs GetDialog(int npcId)
        {
            var def = Get(npcId);
            if (def == null) return null;

            var quest = Quest;
            var state = quest != null ? quest.DenOfEvil : QuestState.NotStarted;
            var canAccept = def.isQuestGiver && state == QuestState.NotStarted;
            var canTurnIn = def.isQuestGiver && quest != null && quest.CanTurnInDen;

            if (quest == null)
            {
                Log.Warn("Npc", $"GetDialog(npcId={npcId})：`IQuestModule` 未接入 ⇒ 按未接取阶段给文本");
            }

            return NpcDialog.Build(def, state, (int)QuestId.DenOfEvil, canAccept, canTurnIn);
        }

        /// <summary>
        /// 推进对话（选项下标；0 = 关闭）。
        /// 下标语义与 <see cref="NpcDialog.Build"/> 的组装顺序一致：0=关闭 → 任务动作 → 商店入口。
        /// </summary>
        public void ChooseOption(int npcId, int optionIndex)
        {
            if (optionIndex <= 0)
            {
                Log.Info("Npc", $"对话关闭（npcId={npcId}）");
                _currentNpcId = (int)NpcId.None;
                Game.Event?.Emit(Events.DialogClose);
                return;
            }

            var args = GetDialog(npcId);
            if (args == null)
            {
                Log.Warn("Npc", $"ChooseOption(npcId={npcId}, idx={optionIndex})：NPC 不存在 ⇒ 忽略");
                return;
            }
            if (optionIndex >= args.options.Count)
            {
                Log.Warn("Npc", $"ChooseOption：选项下标 {optionIndex} 越界（共 {args.options.Count} 项）⇒ 忽略");
                return;
            }

            if (args.canAcceptQuest && optionIndex == 1)
            {
                Log.Info("Npc", $"对话选项：{args.npcName} → 接取任务「{args.questId}」");
                Game.Event?.Emit(Events.QuestAcceptRequest, args.questId);
                return;
            }
            if (args.canTurnInQuest && optionIndex == 1)
            {
                Log.Info("Npc", $"对话选项：{args.npcName} → 交付任务「{args.questId}」");
                Game.Event?.Emit(Events.QuestTurnInRequest, args.questId);
                return;
            }
            if (args.hasShop)
            {
                var shop = GetShop(npcId);
                if (shop == null)
                {
                    Log.Warn("Npc", $"对话选项：{args.npcName} 的商店打开失败 ⇒ 忽略");
                    return;
                }
                Log.Info("Npc", $"对话选项：{args.npcName} → 打开商店（{shop.stock.Count} 件商品）");
                Game.Event?.Emit(Events.ShopOpen, shop);
                return;
            }

            Log.Warn("Npc", $"ChooseOption：选项 {optionIndex} 无对应动作（canAccept={args.canAcceptQuest} "
                + $"canTurnIn={args.canTurnInQuest} hasShop={args.hasShop}）⇒ 忽略");
        }

        // ── 商店 ───────────────────────────────────────────────────────────────

        /// <summary>取商店快照（`Events.ShopOpen` 的参数）。没有商店返回 null 并打日志。</summary>
        public ShopOpenArgs GetShop(int npcId)
        {
            var def = Get(npcId);
            if (def == null) return null;
            if (!def.hasShop)
            {
                Log.Warn("Npc", $"GetShop(npcId={npcId})：「{def.name}」没有商店");
                return null;
            }

            var item = Item;
            if (item == null)
            {
                Log.Warn("Npc", $"GetShop：「{def.name}」需要 `IItemModule` 才能上架/结算，但它未接入 ⇒ 返回 null");
                return null;
            }

            EnsureShop(def, item);

            var gold = item.Gold;
            _shop.RefreshAffordable(gold);

            return new ShopOpenArgs
            {
                npcId = def.id,
                npcName = def.name,
                canRepair = def.canRepair,
                playerGold = gold,
                stock = new List<ShopEntry>(_shop.Entries),
                playerItems = CollectSellableSlots(item),
                repairAllCost = item.GetRepairAllCost(),
            };
        }

        /// <summary>买入（金币结算 + 背包空间校验）。</summary>
        public bool Buy(int npcId, int index, int count)
        {
            var def = Get(npcId);
            if (def == null || !def.hasShop)
            {
                Log.Warn("Npc", $"Buy(npcId={npcId})：NPC 不存在或没有商店 ⇒ 失败");
                return false;
            }

            var item = Item;
            if (item == null)
            {
                Log.Warn("Npc", $"Buy：「{def.name}」需要 `IItemModule` 才能结算 ⇒ 失败");
                return false;
            }

            EnsureShop(def, item);

            var entry = _shop.EntryAt(index);
            if (entry == null)
            {
                Log.Warn("Npc", $"Buy：「{def.name}」没有第 {index} 号商品（共 {_shop.Entries.Count} 件）⇒ 失败");
                return false;
            }
            if (entry.count == 0)
            {
                Log.Warn("Npc", $"Buy：「{entry.name}」已售完 ⇒ 失败");
                return false;
            }

            ItemStack template;
            if (!_shop.TryGetItem(index, out template) || template == null)
            {
                Log.Warn("Npc", $"Buy：「{entry.name}」没有可生成的物品数据 ⇒ 失败");
                return false;
            }

            if (count < 1) count = 1;
            var total = entry.price * count;
            if (item.Gold < total)
            {
                Log.Warn("Npc", $"Buy：金币不足（需 {total}，有 {item.Gold}）⇒ 失败，金币不变");
                return false;
            }

            // 先把东西放进背包，全部成功才扣钱（避免"扣了钱没拿到货"）
            var added = new List<ItemStack>();
            for (var i = 0; i < count; i++)
            {
                var copy = CopyOf(template);
                if (!item.AddToInventory(copy))
                {
                    Rollback(item, added);
                    Log.Warn("Npc", $"Buy：背包放不下「{entry.name}」（第 {i + 1}/{count} 件）⇒ 整笔取消，金币不变");
                    Game.Event?.Emit(Events.InventoryFull);
                    return false;
                }
                added.Add(copy);
            }

            if (!item.AddGold(-total))
            {
                Rollback(item, added);
                Log.Warn("Npc", $"Buy：扣金币 {total} 失败（余额 {item.Gold}）⇒ 整笔取消");
                return false;
            }

            _shop.Consume(index);
            Log.Info("Npc", $"买入「{entry.name}」×{count}：花费 {total}，剩余金币 {item.Gold}，"
                + $"该商品剩余 {(entry.count < 0 ? "无限" : entry.count.ToString())}");
            Game.Event?.Emit(Events.ShopChanged, GetShop(npcId));
            return true;
        }

        /// <summary>卖出背包某锚点格的物品（任务物品不可出售）。</summary>
        public bool Sell(int npcId, int anchorIndex)
        {
            var def = Get(npcId);
            if (def == null || !def.hasShop)
            {
                Log.Warn("Npc", $"Sell(npcId={npcId})：NPC 不存在或没有商店 ⇒ 失败");
                return false;
            }

            var item = Item;
            if (item == null)
            {
                Log.Warn("Npc", $"Sell：「{def.name}」需要 `IItemModule` 才能结算 ⇒ 失败");
                return false;
            }

            var target = FindAnchorItem(item, anchorIndex);
            if (target == null)
            {
                Log.Warn("Npc", $"Sell：背包锚点格 {anchorIndex} 上没有物品 ⇒ 失败");
                return false;
            }
            if (target.isQuestItem)
            {
                Log.Warn("Npc", $"Sell：「{target.name}」是任务物品 ⇒ 不可出售");
                return false;
            }

            var price = NpcShop.SellPriceOf(target);
            if (!item.RemoveFromInventory(anchorIndex))
            {
                Log.Warn("Npc", $"Sell：从背包移出锚点格 {anchorIndex} 失败 ⇒ 整笔取消");
                return false;
            }
            if (!item.AddGold(price))
            {
                // 移出成功但加钱失败：把物品放回去（尽量不丢东西）
                if (!item.AddToInventory(target))
                {
                    Log.Error("Npc", $"Sell：加金币失败且「{target.name}」放不回背包 ⇒ 该物品暂留内存（请上报）");
                }
                Log.Warn("Npc", $"Sell：加金币 {price} 失败 ⇒ 整笔取消");
                return false;
            }

            Log.Info("Npc", $"卖出「{target.name}」得到 {price}，当前金币 {item.Gold}");
            Game.Event?.Emit(Events.ShopChanged, GetShop(npcId));
            return true;
        }

        /// <summary>修理（锚点格索引 &lt; 0 = 全部）。返回修理费，-1 = 失败。</summary>
        public int Repair(int npcId, int anchorIndex)
        {
            var def = Get(npcId);
            if (def == null || !def.canRepair)
            {
                Log.Warn("Npc", $"Repair(npcId={npcId})：NPC 不存在或不提供修理（只有恰西能修）⇒ 失败");
                return -1;
            }

            var item = Item;
            if (item == null)
            {
                Log.Warn("Npc", $"Repair：「{def.name}」需要 `IItemModule` 才能结算 ⇒ 失败");
                return -1;
            }

            var cost = item.Repair(anchorIndex);
            if (cost < 0)
            {
                Log.Warn("Npc", $"Repair：「{def.name}」修理失败（金币不足或物品不存在）");
                return -1;
            }

            Log.Info("Npc", $"修理完成（index={anchorIndex}）花费 {cost}，当前金币 {item.Gold}");
            Game.Event?.Emit(Events.ShopChanged, GetShop(npcId));
            return cost;
        }

        // ── 存档 / 帧推进 / 复位 ─────────────────────────────────────────────────

        /// <summary>按存档恢复（对话文本由任务阶段驱动，任务状态由 `IQuestModule` 读档）。</summary>
        public void LoadFrom(CharacterSave save)
        {
            if (save == null)
            {
                Log.Warn("Npc", "NpcModule.LoadFrom 收到 null 存档 ⇒ 忽略（对话文本只依赖任务阶段）");
                return;
            }
            Log.Info("Npc", $"读档：NPC 模块就绪（所在区域 areaId={save.areaId}；对话文本随后按任务阶段实时生成）");
        }

        /// <summary>每帧推进（NPC 待机动画由 View 负责；这里只做"重建站位"与"走到就说话"）。</summary>
        public void Tick(float dt)
        {
            EnsureBuilt();
            TryAutoInteract();
        }

        /// <summary>
        /// 点击落点靠近某个 NPC（`GameConst.TalkRange` 内）⇒ 记下"走过去跟他说话"的意图。
        /// 点击只落到 `Events.MoveCommand`（没人发 `NpcInteractRequest`）⇒ 这是"点 NPC 说话"的兜底实现。
        /// </summary>
        private void OnMoveCommand(Vector2Int target)
        {
            var def = FindNearest(target);
            if (def == null)
            {
                if (_pendingNpcId != (int)NpcId.None) _pendingNpcId = (int)NpcId.None;
                return;
            }
            if (def.id == _currentNpcId || def.id == _pendingNpcId) return;

            _pendingNpcId = def.id;
            Log.Info("Npc", $"点击 ({target.x},{target.y}) 落在 {def.name} 的对话范围（{GameConst.TalkRange:0.00} 格）内"
                + "⇒ 走过去后自动对话");
        }

        /// <summary>走到待对话 NPC 的对话范围内就交互一次（已在对话中则不重复开）。</summary>
        private void TryAutoInteract()
        {
            if (_pendingNpcId == (int)NpcId.None) return;

            var p = Player;
            if (p == null) return;

            var def = Get(_pendingNpcId);
            if (def == null)
            {
                _pendingNpcId = (int)NpcId.None;
                return;
            }
            if (_currentNpcId == def.id)
            {
                _pendingNpcId = (int)NpcId.None;          // 已经在跟他说话
                return;
            }

            var dx = p.Grid.x - def.gridX;
            var dy = p.Grid.y - def.gridY;
            if (Mathf.Sqrt(dx * dx + dy * dy) > GameConst.TalkRange) return;

            var id = _pendingNpcId;
            _pendingNpcId = (int)NpcId.None;
            Interact(id);
        }

        /// <summary>复位（回主菜单时调用）。</summary>
        public void Reset()
        {
            _defs.Clear();
            _builtSeed = int.MinValue;
            _builtArea = -1;
            _currentNpcId = (int)NpcId.None;
            _pendingNpcId = (int)NpcId.None;
            _shop.Build(null, 0, 1, null, 0);
            Log.Info("Npc", "NPC 模块已复位（定义清空、商店下架）");
        }

        // ── 内部：定义构建 ─────────────────────────────────────────────────────

        private static IMapModule Map
        {
            get
            {
                var ctx = AppContext.I;
                return ctx != null ? ctx.Map : null;
            }
        }

        /// <summary>
        /// 当前是否"在 NPC 所在区域"（罗格营地）。5 个 NPC 的定义 `areaId` 恒为 <see cref="AreaId.Town"/>，
        /// 站位也只在该区域有效（其它区域的 `IMapModule.NpcPoints` 是空列表 ⇒ 站位退化为 (0,0)）。
        /// 地图未生成 / 未接入 ⇒ false（宁可拒绝交互，也不产生"幽灵 NPC"）。
        /// </summary>
        private static bool InTownForNpc()
        {
            var map = Map;
            return map != null && map.IsGenerated && map.Area == AreaId.Town;
        }

        private static IQuestModule Quest
        {
            get
            {
                var ctx = AppContext.I;
                return ctx != null ? ctx.Quest : null;
            }
        }

        private static IItemModule Item
        {
            get
            {
                var ctx = AppContext.I;
                return ctx != null ? ctx.Item : null;
            }
        }

        /// <summary>地图（seed + 区域）没变就不重建；变了（换局/换图）就重新取站位。</summary>
        private void EnsureBuilt()
        {
            var map = Map;
            var seed = map != null ? map.Seed : int.MinValue;
            var area = map != null ? (int)map.Area : -1;
            if (_defs.Count > 0 && seed == _builtSeed && area == _builtArea) return;

            _defs.Clear();

            var points = map != null ? map.NpcPoints : null;
            for (var i = 0; i < Names.Length; i++)
            {
                var gx = 0;
                var gy = 0;
                if (points != null && i < points.Count)
                {
                    gx = points[i].x;
                    gy = points[i].y;
                }
                else
                {
                    Log.Warn("Npc", $"NPC「{Names[i]}」的站位缺失（`IMapModule.NpcPoints` 长度 "
                        + $"{(points == null ? "null" : points.Count.ToString())} < {i + 1}）⇒ 暂用 (0,0)");
                }

                _defs.Add(new NpcDef
                {
                    id = i,
                    name = Names[i],
                    gridX = gx,
                    gridY = gy,
                    areaId = (int)AreaId.Town,
                    isQuestGiver = i == (int)NpcId.Akara,
                    hasShop = i == (int)NpcId.Akara || i == (int)NpcId.Charsi || i == (int)NpcId.Gheed,
                    canRepair = i == (int)NpcId.Charsi,
                    isBlacksmith = i == (int)NpcId.Charsi,
                });
            }

            _builtSeed = seed;
            _builtArea = area;
            Log.Info("Npc", $"NPC 站位已按地图装配（seed={seed} 区域={(map != null ? map.Area.ToString() : "无地图")}）："
                + $"{Names.Length} 个（阿卡拉/卡夏/恰西/基德/瓦瑞夫）");
        }

        // ── 内部：商店 ─────────────────────────────────────────────────────────

        private void EnsureShop(NpcDef def, IItemModule item)
        {
            var map = Map;
            var seed = map != null ? map.Seed : 0;
            if (_shop.BuiltNpcId == def.id && _shop.BuiltSeed == seed && _shop.Entries.Count > 0) return;

            var level = 1;
            var p = Player;
            if (p != null) level = p.Level;

            _shop.Build(def, seed, level, item, item.Gold);
        }

        private static IPlayerModule Player
        {
            get
            {
                var ctx = AppContext.I;
                return ctx != null ? ctx.Player : null;
            }
        }

        /// <summary>玩家背包里可出售的锚点格（供 `ShopOpenArgs.playerItems`）。</summary>
        private static List<InventorySlot> CollectSellableSlots(IItemModule item)
        {
            var res = new List<InventorySlot>();
            var inv = item.Inventory;
            if (inv == null) return res;

            for (var i = 0; i < inv.Count; i++)
            {
                var s = inv[i];
                if (s == null || !s.isAnchor || s.item == null) continue;
                if (s.item.isQuestItem) continue;                  // 任务物品不可卖，不列出来
                res.Add(s);
            }
            return res;
        }

        private static ItemStack FindAnchorItem(IItemModule item, int anchorIndex)
        {
            var inv = item.Inventory;
            if (inv == null || anchorIndex < 0 || anchorIndex >= inv.Count) return null;
            var s = inv[anchorIndex];
            return s != null && s.isAnchor ? s.item : null;
        }

        /// <summary>背包里"就是这几个实例"的锚点格（买多件失败时逐个回滚用）。</summary>
        private static void Rollback(IItemModule item, List<ItemStack> added)
        {
            if (added.Count == 0) return;
            var inv = item.Inventory;
            for (var i = 0; i < added.Count; i++)
            {
                var copy = added[i];
                for (var k = 0; k < inv.Count; k++)
                {
                    var s = inv[k];
                    if (s == null || !s.isAnchor || s.item == null) continue;
                    if (!ReferenceEquals(s.item, copy)) continue;
                    item.RemoveFromInventory(k);
                    break;
                }
            }
            Log.Warn("Npc", $"已回滚 {added.Count} 件刚买入的物品（背包恢复原样）");
        }

        /// <summary>物品深拷贝（买同一件商品多次时不能共用同一实例）。</summary>
        private static ItemStack CopyOf(ItemStack src)
        {
            var copy = new ItemStack
            {
                itemId = src.itemId,
                name = src.name,
                type = src.type,
                quality = src.quality,
                count = src.count,
                gridW = src.gridW,
                gridH = src.gridH,
                lvlReq = src.lvlReq,
                strReq = src.strReq,
                dmgMin = src.dmgMin,
                dmgMax = src.dmgMax,
                defMin = src.defMin,
                defMax = src.defMax,
                price = src.price,
                durability = src.durability,
                maxDurability = src.maxDurability,
                isGold = src.isGold,
                isQuestItem = src.isQuestItem,
            };
            if (src.affixes != null)
            {
                for (var i = 0; i < src.affixes.Count; i++)
                {
                    var a = src.affixes[i];
                    if (a == null) continue;
                    copy.affixes.Add(new ItemAffix
                    {
                        affixId = a.affixId,
                        kind = a.kind,
                        name = a.name,
                        mod = a.mod,
                        min = a.min,
                        max = a.max,
                        value = a.value,
                    });
                }
            }
            return copy;
        }

        // ── 内部：事件 ─────────────────────────────────────────────────────────

        /// <summary>别的模块（Input/UI）若发 `NpcInteractRequest`：直接交互（与本模块的点击兜底互不干扰）。</summary>
        private void OnInteractRequest(int npcId)
        {
            if (npcId == _currentNpcId) return;
            _pendingNpcId = (int)NpcId.None;
            Interact(npcId);
        }

        private void OnDialogOptionChosen(int optionIndex)
        {
            if (_currentNpcId == (int)NpcId.None)
            {
                Log.Warn("Npc", $"DialogOptionChosen(idx={optionIndex})：当前没有进行中的对话 ⇒ 忽略");
                return;
            }
            ChooseOption(_currentNpcId, optionIndex);
        }

        private void OnDialogClose()
        {
            _currentNpcId = (int)NpcId.None;
        }

        /// <summary>
        /// 任务阶段变化 ⇒ **正在对话的那个 NPC 立刻改用新阶段的话术与选项**
        /// （原版：在阿卡拉处接下/交付任务后，台词与选项当场就变，不需要关掉重开）。
        /// 只在"确实有对话进行中"时才重发 `Events.DialogOpen`；没有对话时什么都不做。
        /// </summary>
        private void OnQuestChanged(QuestStateDto quest)
        {
            if (_currentNpcId == (int)NpcId.None) return;

            var def = Get(_currentNpcId);
            if (def == null)
            {
                // 非预期分支：对话中的 NPC 定义丢失（复位/换图）⇒ 不刷新，留可定位日志
                Log.Warn("Npc", $"任务阶段变化，但当前对话的 NPC(id={_currentNpcId}) 取不到定义 ⇒ 不刷新对话");
                return;
            }

            var args = GetDialog(_currentNpcId);
            if (args == null)
            {
                Log.Warn("Npc", $"任务阶段变化 ⇒ 「{def.name}」的对话重组装失败 ⇒ 保持旧台词（见上一行原因）");
                return;
            }

            Log.Info("Npc", $"[Npc] 任务阶段变化（state={(quest != null ? quest.state.ToString() : "null")}）"
                + $"⇒ 实时刷新「{def.name}」的对话（可接={args.canAcceptQuest} 可交={args.canTurnInQuest}"
                + $" 选项 {args.options.Count} 项）");

            Game.Event?.Emit(Events.DialogOpen, args);
        }

        private void OnBuyRequest(ShopTradeArgs args)
        {
            if (args == null)
            {
                Log.Warn("Npc", "ShopBuyRequest：参数为 null ⇒ 忽略");
                return;
            }
            Buy(ResolveNpcId(args.npcId), args.index, args.count);
        }

        private void OnSellRequest(ShopTradeArgs args)
        {
            if (args == null)
            {
                Log.Warn("Npc", "ShopSellRequest：参数为 null ⇒ 忽略");
                return;
            }
            Sell(ResolveNpcId(args.npcId), args.index);
        }

        private void OnRepairRequest(ShopTradeArgs args)
        {
            if (args == null)
            {
                Log.Warn("Npc", "ShopRepairRequest：参数为 null ⇒ 忽略");
                return;
            }
            Repair(ResolveNpcId(args.npcId), args.index);
        }

        private void OnShopClose()
        {
            Log.Info("Npc", "商店已关闭");
        }

        /// <summary>事件里的 npcId 缺省时用"当前正在对话的 NPC"（面板可能不带该字段）。</summary>
        private int ResolveNpcId(int fromArgs)
        {
            if (fromArgs != (int)NpcId.None) return fromArgs;
            if (_currentNpcId != (int)NpcId.None) return _currentNpcId;
            Log.Warn("Npc", "交易请求没带 npcId，且当前没有进行中的对话 ⇒ 用 0（阿卡拉）兜底");
            return (int)NpcId.Akara;
        }
    }
}
