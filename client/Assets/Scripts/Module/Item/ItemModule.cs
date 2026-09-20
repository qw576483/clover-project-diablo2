// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · Module/Item/ItemModule.cs
// 物品门面实现：**掉落 / 拾取 / 背包 / 装备 / 腰带 / 金币 / 修理 / 词缀**。
//
// 装配契约（`docs/agents/_common.md` §3.5）：`internal sealed class ItemModule : IItemModule`，
//   无参构造 ⇒ `AppContext.AutoWire()` 能反射实例化。
//
// 依赖获取方式（**不许 using 别的模块的具体类型**）：
//   · `AppContext.I.Player`  → `IPlayerModule`（金币 / 生命 / 法力 / 等级 / 力量 / 格坐标）
//   · `AppContext.I.View`    → `IViewModule`（地面物品视图；见「未决」第 1 条）
//   · `Game.Event`           → 输入事件（拾取 / 喝药 / 装备 / 丢弃）
//
// 金币**唯一归属 `IPlayerModule`**（`PlayerStatsDto.gold` 与 HUD 都读它）：本模块的 `Gold`/`AddGold`
//   在 Player 接入时全部转发；只有 Player 未接入（降级）才用本模块内存兜底并 Warn 一次。
//
// 事件（**只用 `Core/Events.cs` 的常量**）：
//   发：`InventoryChanged` / `EquipChanged`（属性重算由 Player 模块监听）/ `ItemPicked` / `ItemDropped` /
//       `ItemUsed` / `GroundItemRemoved` / `InventoryFull`
//   收：`UseBeltRequest` / `PickupRequest` / `EquipToggleRequest` / `ItemDropRequest`
// ─────────────────────────────────────────────────────────────────────────────

using System.Collections.Generic;
using CloverEngine;
using Diablo2.App;
using Diablo2.Core;
using Diablo2.Def;
using Table;
using UnityEngine;
// 别名：本类同时有 `Inventory`/`Equipment`/`Belt`/`GroundItems` **属性**（接口要求），
// 与同命名空间下的**同名辅助类**会形成"成员名 vs 类型名"歧义 ⇒ 用别名消歧。
using BeltHelper = Diablo2.Module.Item.Belt;
using EquipHelper = Diablo2.Module.Item.Equipment;
using GroundHelper = Diablo2.Module.Item.GroundItems;
using InvHelper = Diablo2.Module.Item.Inventory;

namespace Diablo2.Module.Item
{
    /// <summary>物品门面实现（掉落 / 拾取 / 背包 / 装备 / 腰带 / 金币 / 修理）。</summary>
    internal sealed class ItemModule : IItemModule
    {
        /// <summary>修理费系数（**本项目新增折算**：缺耐久比例 × 基础价 × 本系数）。</summary>
        private const float RepairPriceRatio = 0.5f;

        /// <summary>超时回收的检查间隔（秒）。</summary>
        private const float SweepInterval = 5f;

        /// <summary>
        /// 「点击物品 → 走过去 → 拾取」的容差（格）：点击格与该格附近 <see cref="GameConst.PickupRange"/>
        /// 内的地面物品才算"点了那件物品"。**本项目新增的手感参数**（原版是"点哪件就捡哪件"，
        /// 本项目点击只有 `Events.MoveCommand` 一种落点 ⇒ 用容差把"点了物品"识别出来）。
        /// </summary>
        private const float ClickPickupSlack = 1.0f;

        /// <summary>完整回复药水（官方 code `rvl`）的恢复上限比例；其余回复药水 35%（原版）。</summary>
        private const int FullRejuvPercent = 100;
        private const int RejuvPercent = 35;

        private readonly InvHelper _inv = new InvHelper();
        private readonly EquipHelper _equip = new EquipHelper();
        private readonly BeltHelper _belt = new BeltHelper();
        private readonly GroundHelper _ground = new GroundHelper();
        private readonly ItemFactory _factory = new ItemFactory();
        private readonly LootRoller _roller;

        /// <summary>仅当 `IPlayerModule` 未接入时的金币兜底（正常链路不用）。</summary>
        private int _fallbackGold;

        /// <summary>本模块自走时钟（掉落时间 / 超时回收；不用 `Time.time`，离线宿主也能跑）。</summary>
        private float _clock;

        /// <summary>「点了这件物品，走到就捡」的意图（-1 = 无）。见 <see cref="OnMoveCommand"/>。</summary>
        private int _pendingPickupId = -1;

        private float _sweepAcc;

        private bool _warnedNoPlayer;

        public ItemModule()
        {
            _roller = new LootRoller(_factory);

            var bus = Game.Event;
            if (bus == null)
            {
                Log.Warn("Item", "ItemModule 构造时 Game.Event 为 null（引擎未启动？）⇒ 不订阅输入事件（拾取/喝药需 UI 直调）");
                return;
            }
            bus.On<int>(Events.UseBeltRequest, OnUseBeltRequest);
            bus.On<int>(Events.PickupRequest, OnPickupRequest);
            bus.On<int>(Events.EquipToggleRequest, OnEquipToggleRequest);
            bus.On<int>(Events.ItemDropRequest, OnItemDropRequest);
            // 「点击地面物品 → 走过去 → 拾取」：当前**没有任何模块发 `Events.PickupRequest`**
            // （点击只落到 `Events.MoveCommand`，见 `Module/Input/InputReader.cs` 与 `Module/Player/PlayerModule.cs`）
            // ⇒ 本模块用 `MoveCommand` 的落点识别"点了哪件物品"，走到位后自动拾取一次。
            // 若将来 Input/UI 补上 `PickupRequest`，两条路径并存且不冲突（本路径只对"新点击"生效一次）。
            bus.On<Vector2Int>(Events.MoveCommand, OnMoveCommand);
        }

        // ── 门面属性 ───────────────────────────────────────────────────────────

        /// <summary>当前金币（转发 Player；Player 未接入时用兜底）。</summary>
        public int Gold
        {
            get
            {
                var p = Player;
                if (p != null) return p.Gold;
                WarnNoPlayer("Gold");
                return _fallbackGold;
            }
        }

        /// <summary>背包格快照。</summary>
        public IReadOnlyList<InventorySlot> Inventory
        {
            get { return _inv.Slots; }
        }

        /// <summary>已装备物品。</summary>
        public IReadOnlyList<ItemStack> Equipment
        {
            get { return _equip.Items; }
        }

        /// <summary>腰带（4 格，空格为 null）。</summary>
        public IReadOnlyList<ItemStack> Belt
        {
            get { return _belt.Slots; }
        }

        /// <summary>地面物品。</summary>
        public IReadOnlyList<KeyValuePair<int, ItemStack>> GroundItems
        {
            get { return _ground.All; }
        }

        /// <summary>背包是否已满（放不下任何 1×1 物品）。</summary>
        public bool IsFull
        {
            get { return _inv.IsFull; }
        }

        /// <summary>背包 / 装备 / 腰带 / 金币快照（`Events.InventoryChanged` 的参数）。</summary>
        public InventoryChangedArgs Snapshot()
        {
            return new InventoryChangedArgs
            {
                gold = Gold,
                inventory = _inv.ToList(),
                equip = _equip.ToList(),
                belt = _belt.ToList(),
            };
        }

        // ── 生成与掉落 ─────────────────────────────────────────────────────────

        /// <summary>按等级随机生成一件物品。</summary>
        public ItemStack CreateRandom(int level, Rng rng)
        {
            var st = _roller.RollOne(level, rng);
            if (st == null)
            {
                Log.Warn("Item", $"CreateRandom(level={level}) 未产出物品（配表未加载或池为空）");
            }
            return st;
        }

        /// <summary>
        /// 按掉落表生成一批掉落（怪物死亡时由 `Module/Combat/DeathFlow` 调用）。
        /// ⚠️ 契约签名是 `int treasureClassId`，而 `treasureclass_c` 主键是字符串 ⇒
        /// **口径 = `Tables.Default.Treasureclass.All()` 的 1 基行序**，与唯一真实调用方
        /// agent-07 `DeathFlow.TreasureClassIdOf()` 完全一致（`DeathFlow.cs:227-243`，已逐行核对）。
        /// 无效值 Warn + 按等级退回兜底 TC（不让怪变得不掉东西）。
        /// </summary>
        public void DropLoot(int treasureClassId, int monsterLevel, Vector2Int grid, Rng rng)
        {
            var items = _roller.Roll(treasureClassId, monsterLevel, rng);
            if (items.Count == 0)
            {
                Log.Info("Item", $"DropLoot：tcId={treasureClassId} level={monsterLevel} 本次无掉落");
                return;
            }

            for (var i = 0; i < items.Count; i++) DropToGround(items[i], grid);
            Log.Info("Item", $"DropLoot：tcId={treasureClassId}（\"{LootRoller.ResolveTcName(treasureClassId, monsterLevel)}\"）"
                + $" level={monsterLevel} grid=({grid.x},{grid.y}) 掉落 {items.Count} 件（其中金币 {CountGold(items)} 件）");
        }

        /// <summary>把一件物品直接放到地面。</summary>
        public void DropToGround(ItemStack item, Vector2Int grid)
        {
            var id = _ground.Add(item, grid, _clock);
            if (id < 0) return;

            Game.Event?.Emit(Events.ItemDropped, item);

            // 视图需要 (groundItemId, item, grid) 三样，而 `Events.ItemDropped` 的参数只有 ItemStack
            // ⇒ 这里直接经门面接口通知 View（**已登记为「未决」第 1 条**，避免 UI 端重复建视图）。
            var view = View;
            if (view != null) view.CreateGroundItem(id, item, grid);

            Log.Info("Item", $"地面物品 #{id}：「{item.name}」({item.gridW}×{item.gridH}) at ({grid.x},{grid.y})");
        }

        /// <summary>
        /// 拾取（**距离校验**：超距 / 背包满 ⇒ 返回 false 且**物品留在原地**）。
        /// </summary>
        public bool Pickup(int groundItemId)
        {
            var item = _ground.Get(groundItemId);
            if (item == null)
            {
                Log.Warn("Item", $"Pickup：地面物品 id={groundItemId} 不存在（可能已被拾取）");
                return false;
            }

            Vector2Int cell;
            if (!_ground.TryGetCell(groundItemId, out cell))
            {
                Log.Warn("Item", $"Pickup：地面物品 id={groundItemId} 没有格坐标（数据不一致）⇒ 拒绝");
                return false;
            }

            var p = Player;
            if (p != null)
            {
                var d = GroundHelper.Distance(p.Grid, cell);
                if (d > GameConst.PickupRange)
                {
                    Log.Warn("Item", $"Pickup：距离过远（玩家格 ({p.Grid.x},{p.Grid.y}) ↔ 物品格 ({cell.x},{cell.y}) "
                        + $"距离 {d:0.00} > {GameConst.PickupRange:0.00}）⇒ 不拾取，物品留在原地");
                    return false;
                }
            }
            else
            {
                WarnNoPlayer("Pickup（跳过距离校验）");
            }

            if (item.isGold)
            {
                var amount = Mathf.Max(1, item.count);
                if (!AddGoldInternal(amount))
                {
                    Log.Warn("Item", $"Pickup：加金币 {amount} 失败 ⇒ 金币堆留在原地");
                    return false;
                }
                _ground.Remove(groundItemId);
                Game.Event?.Emit(Events.GroundItemRemoved, groundItemId);
                View?.RemoveGroundItem(groundItemId);
                Log.Info("Item", $"拾取金币 {amount}（金币堆 #{groundItemId} 移除），当前金币 {Gold}");
                return true;
            }

            if (!AddToInventory(item))
            {
                Log.Warn("Item", $"Pickup：「{item.name}」背包放不下 ⇒ 返回 false，物品**留在原地**（地面 #{groundItemId}）");
                Game.Event?.Emit(Events.InventoryFull);
                return false;
            }

            _ground.Remove(groundItemId);
            Game.Event?.Emit(Events.ItemPicked, item);
            Game.Event?.Emit(Events.GroundItemRemoved, groundItemId);
            View?.RemoveGroundItem(groundItemId);
            Log.Info("Item", $"拾取「{item.name}」（地面 #{groundItemId} 移除）；背包空格 {_inv.FreeCellCount}");
            return true;
        }

        /// <summary>拾取离某格最近、且在范围内的物品。</summary>
        public bool PickupNearest(Vector2Int grid, float maxRange)
        {
            var id = _ground.Nearest(grid, maxRange);
            if (id < 0)
            {
                Log.Warn("Item", $"PickupNearest：({grid.x},{grid.y}) 周围 {maxRange:0.00} 格内没有地面物品");
                return false;
            }
            return Pickup(id);
        }

        // ── 背包 ───────────────────────────────────────────────────────────────

        /// <summary>放入背包（先并入同类堆，再自动找位；放不下返回 false）。</summary>
        public bool AddToInventory(ItemStack item)
        {
            if (item == null)
            {
                Log.Warn("Item", "AddToInventory 收到 null 物品 ⇒ 拒绝");
                return false;
            }

            var row = Tables.Default.Item.Get(item.itemId);
            var maxStack = row != null ? row.MaxStack : 0;
            if (item.count > 1 && _inv.TryStackInto(item, maxStack))
            {
                EmitInventoryChanged();
                return true;
            }

            int anchor;
            if (!_inv.TryPlace(item, out anchor))
            {
                Log.Warn("Item", $"AddToInventory：「{item.name}」({item.gridW}×{item.gridH}) 放不下"
                    + $"（空格 {_inv.FreeCellCount}）⇒ 失败（物品不入包）");
                return false;
            }

            EmitInventoryChanged();
            Log.Info("Item", $"入包「{item.name}」锚点格 {anchor}（空格 {_inv.FreeCellCount}）");
            return true;
        }

        /// <summary>从背包移除某锚点格上的物品。</summary>
        public bool RemoveFromInventory(int anchorIndex)
        {
            if (!_inv.RemoveAt(anchorIndex)) return false;
            EmitInventoryChanged();
            Log.Info("Item", $"移出背包锚点格 {anchorIndex}（空格 {_inv.FreeCellCount}）");
            return true;
        }

        // ── 装备 ───────────────────────────────────────────────────────────────

        /// <summary>装备背包里某锚点格的物品（换下的回背包；失败返回 false）。</summary>
        public bool EquipFromInventory(int anchorIndex)
        {
            var item = _inv.GetAt(anchorIndex);
            if (item == null)
            {
                Log.Warn("Item", $"EquipFromInventory：背包锚点格 {anchorIndex} 上没有物品");
                return false;
            }

            var slot = EquipHelper.SlotOf(item);
            if (slot == ItemSlot.None)
            {
                Log.Warn("Item", $"EquipFromInventory：「{item.name}」不是可装备物品 ⇒ 失败");
                return false;
            }

            var p = Player;
            if (p != null)
            {
                if (p.Level < item.lvlReq)
                {
                    Log.Warn("Item", $"EquipFromInventory：「{item.name}」需要等级 {item.lvlReq}，当前 {p.Level} ⇒ 失败");
                    return false;
                }
                if (p.Str < item.strReq)
                {
                    Log.Warn("Item", $"EquipFromInventory：「{item.name}」需要力量 {item.strReq}，当前 {p.Str} ⇒ 失败");
                    return false;
                }
            }

            _inv.RemoveAt(anchorIndex);

            ItemSlot eqSlot;
            int eqIndex;
            ItemStack replaced;
            if (!_equip.TryEquip(item, out eqSlot, out eqIndex, out replaced))
            {
                // 理论上不会到这里（上面已判过 None）；放回去避免丢东西
                _inv.TryPlace(item, out _);
                Log.Error("Item", $"EquipFromInventory：TryEquip 失败（slot={slot}）⇒ 已把物品放回背包");
                return false;
            }

            if (replaced != null)
            {
                int back;
                if (!_inv.TryPlace(replaced, out back))
                {
                    // 回滚：把换下的装回原槽，新物品放回背包（若也放不下则留在背包外 —— 记 Error 便于追查）
                    ItemStack back2;
                    _equip.Replace(eqSlot, eqIndex, replaced, out back2);
                    if (back2 != null && !_inv.TryPlace(back2, out _))
                    {
                        Log.Error("Item", $"EquipFromInventory：换装回滚失败，「{back2.name}」无处安放"
                            + "（背包溢出）⇒ 该物品暂留内存（请上报）");
                    }
                    Log.Warn("Item", $"EquipFromInventory：换下的「{replaced.name}」背包放不下 ⇒ 取消本次换装");
                    EmitInventoryChanged();
                    return false;
                }
            }

            EmitInventoryChanged();
            Game.Event?.Emit(Events.EquipChanged, Snapshot());
            Log.Info("Item", $"装备「{item.name}」到 {eqSlot}[{eqIndex}]"
                + (replaced != null ? $"（换下「{replaced.name}」）" : "") + $"，当前命中 {AttackRatingOfPlayer()} 防御 {DefenseOfPlayer()}");
            return true;
        }

        /// <summary>卸下某槽位。</summary>
        public bool Unequip(ItemSlot slot, int slotIndex)
        {
            ItemStack removed;
            if (!_equip.Unequip(slot, slotIndex, out removed)) return false;

            int back;
            if (!_inv.TryPlace(removed, out back))
            {
                ItemStack back2;
                _equip.Replace(slot, slotIndex, removed, out back2);
                Log.Warn("Item", $"Unequip：卸下的「{removed.name}」背包放不下（空格 {_inv.FreeCellCount}）⇒ 取消卸下");
                return false;
            }

            EmitInventoryChanged();
            Game.Event?.Emit(Events.EquipChanged, Snapshot());
            Log.Info("Item", $"卸下「{removed.name}」（{slot}[{slotIndex}] → 背包格 {back}）");
            return true;
        }

        // ── 使用物品 / 腰带 ─────────────────────────────────────────────────────

        /// <summary>使用某锚点格的物品（药水/卷轴）。</summary>
        public bool UseItem(int anchorIndex)
        {
            var item = _inv.GetAt(anchorIndex);
            if (item == null)
            {
                Log.Warn("Item", $"UseItem：背包锚点格 {anchorIndex} 上没有物品");
                return false;
            }

            var kind = BeltHelper.PotionKind(item);
            if (kind == null)
            {
                Log.Warn("Item", $"UseItem：「{item.name}」不可使用（只有药水在本次范围内）");
                return false;
            }

            if (!ApplyPotion(item, kind)) return false;

            // 消耗一个
            if (item.count > 1) item.count -= 1;
            else _inv.RemoveAt(anchorIndex);

            Game.Event?.Emit(Events.ItemUsed, item);
            EmitInventoryChanged();
            return true;
        }

        /// <summary>腰带格喝药（0..3）。</summary>
        public bool UseBeltSlot(int index)
        {
            ItemStack used;
            if (!_belt.Take(index, out used)) return false;

            var kind = BeltHelper.PotionKind(used);
            if (kind == null)
            {
                // 理论上 TryAdd 已挡住；真出现就说明腰带里被塞了非药水（数据不一致）
                Log.Warn("Item", $"UseBeltSlot：腰带格 {index} 的「{used.name}」不是药水 ⇒ 拒绝（物品已消耗，请上报）");
                return false;
            }

            if (!ApplyPotion(used, kind))
            {
                // 没生效（Player 未接入）⇒ 把药水放回去，避免白喝
                _belt.TryAdd(used, index);
                return false;
            }

            Game.Event?.Emit(Events.ItemUsed, used);
            EmitInventoryChanged();
            Log.Info("Item", $"喝药（腰带格 {index}，「{used.name}」）→ 生命 {LifeOfPlayer()} / 法力 {ManaOfPlayer()}"
                + $"；腰带剩余 {(Belt[index] != null ? Belt[index].count : 0)}");
            return true;
        }

        /// <summary>把物品放进腰带（index &lt; 0 = 自动找位）。</summary>
        public bool AddToBelt(ItemStack item, int index)
        {
            if (!_belt.TryAdd(item, index)) return false;
            EmitInventoryChanged();
            return true;
        }

        // ── 金币 ───────────────────────────────────────────────────────────────

        /// <summary>增减金币（余额不足返回 false）。</summary>
        public bool AddGold(int amount)
        {
            return AddGoldInternal(amount);
        }

        // ── 修理 ───────────────────────────────────────────────────────────────

        /// <summary>修理（锚点格索引；&lt; 0 = 全部）。返回修理费，-1 = 失败。</summary>
        public int Repair(int anchorIndex)
        {
            if (anchorIndex >= 0)
            {
                var item = _inv.GetAt(anchorIndex);
                if (item == null)
                {
                    Log.Warn("Item", $"Repair：背包锚点格 {anchorIndex} 上没有物品");
                    return -1;
                }

                var cost = RepairCostOf(item);
                if (cost <= 0)
                {
                    Log.Info("Item", $"Repair：「{item.name}」无需修理（耐久 {item.durability}/{item.maxDurability}）");
                    return 0;
                }
                if (!AddGoldInternal(-cost))
                {
                    Log.Warn("Item", $"Repair：金币不足（需 {cost}，有 {Gold}）⇒ 修理失败，耐久不变");
                    return -1;
                }

                item.durability = item.maxDurability;
                EmitInventoryChanged();
                Log.Info("Item", $"修理「{item.name}」花费 {cost}，耐久恢复 {item.durability}/{item.maxDurability}，金币 {Gold}");
                return cost;
            }

            var total = GetRepairAllCost();
            if (total <= 0)
            {
                Log.Info("Item", "Repair(全部)：没有需要修理的装备");
                return 0;
            }
            if (!AddGoldInternal(-total))
            {
                Log.Warn("Item", $"Repair(全部)：金币不足（需 {total}，有 {Gold}）⇒ 修理失败，耐久不变");
                return -1;
            }

            RestoreAllDurability();
            EmitInventoryChanged();
            Game.Event?.Emit(Events.EquipChanged, Snapshot());
            Log.Info("Item", $"修理全部花费 {total}，金币 {Gold}");
            return total;
        }

        /// <summary>总修理费（商店面板显示用）。</summary>
        public int GetRepairAllCost()
        {
            var total = 0;
            for (var i = 0; i < _inv.Slots.Count; i++)
            {
                if (_inv.Slots[i].isAnchor) total += RepairCostOf(_inv.Slots[i].item);
            }
            for (var i = 0; i < _equip.Items.Count; i++) total += RepairCostOf(_equip.Items[i]);
            return total;
        }

        // ── 存档 ───────────────────────────────────────────────────────────────

        /// <summary>按存档恢复背包/装备/腰带。</summary>
        public void LoadFrom(CharacterSave save)
        {
            if (save == null)
            {
                Log.Warn("Item", "ItemModule.LoadFrom 收到 null 存档 ⇒ 复位为空");
                Reset();
                return;
            }

            _inv.LoadFrom(save.inventory);
            _equip.LoadFrom(save.equip);
            _belt.LoadFrom(save.belt);
            _fallbackGold = 0;

            // 金币归 Player；Player 未接入时才用兜底（并 Warn）
            if (Player == null && save.gold != 0)
            {
                _fallbackGold = save.gold;
                WarnNoPlayer("LoadFrom（金币用本模块兜底）");
            }

            EmitInventoryChanged();
            Game.Event?.Emit(Events.EquipChanged, Snapshot());
            Log.Info("Item", $"读档：背包锚点 {CountAnchors()} 个 / 装备 {_equip.Items.Count} 件 / "
                + $"腰带 {_belt.Slots.Count} 格 / 地面物品已清 {_ground.Count}");
        }

        /// <summary>写回存档对象（金币由 Player 写）。</summary>
        public void WriteTo(CharacterSave save)
        {
            if (save == null)
            {
                Log.Warn("Item", "ItemModule.WriteTo 收到 null 存档 ⇒ 跳过");
                return;
            }
            save.inventory = _inv.ToList();
            save.equip = _equip.ToList();
            save.belt = _belt.ToList();
            Log.Info("Item", $"存档：背包 {_inv.FreeCellCount} 空格 / 装备 {_equip.Items.Count} 件 / 腰带 {_belt.Slots.Count} 格");
        }

        // ── 帧推进 / 复位 ───────────────────────────────────────────────────────

        /// <summary>每帧推进（走到位后的自动拾取 + 地面物品超时回收）。</summary>
        public void Tick(float dt)
        {
            _clock += dt;
            TryArrivePickup();

            _sweepAcc += dt;
            if (_sweepAcc < SweepInterval) return;
            _sweepAcc = 0f;

            var expired = _ground.CollectExpired(_clock, GameConst.GroundItemLifetimeSeconds, null);
            for (var i = 0; i < expired.Count; i++)
            {
                Game.Event?.Emit(Events.GroundItemRemoved, expired[i]);
                View?.RemoveGroundItem(expired[i]);
                Log.Info("Item", $"地面物品 #{expired[i]} 超时（>{GameConst.GroundItemLifetimeSeconds}s）已回收");
            }
        }

        /// <summary>复位（回主菜单时调用）。</summary>
        public void Reset()
        {
            _inv.Reset();
            _equip.Clear();
            _belt.Reset();
            _ground.Clear();
            _fallbackGold = 0;
            _clock = 0f;
            _sweepAcc = 0f;
            _pendingPickupId = -1;
            Log.Info("Item", "物品模块已复位（背包/装备/腰带/地面物品清空）");
        }

        // ── 内部：依赖 ─────────────────────────────────────────────────────────

        private static IPlayerModule Player
        {
            get
            {
                var ctx = AppContext.I;
                return ctx != null ? ctx.Player : null;
            }
        }

        private static IViewModule View
        {
            get
            {
                var ctx = AppContext.I;
                return ctx != null ? ctx.View : null;
            }
        }

        private void WarnNoPlayer(string where)
        {
            if (_warnedNoPlayer) return;
            _warnedNoPlayer = true;
            Log.Warn("Item", $"{where}：`IPlayerModule` 未接入（AppContext.Player == null）⇒ "
                + "金币/属性走本模块兜底（**不落盘**）；Player 接入后自动改为转发");
        }

        // ── 内部：金币 ─────────────────────────────────────────────────────────

        private bool AddGoldInternal(int amount)
        {
            var p = Player;
            if (p != null) return p.AddGold(amount);

            WarnNoPlayer("AddGold");
            if (amount < 0 && _fallbackGold + amount < 0)
            {
                Log.Warn("Item", $"AddGold：金币不足（{_fallbackGold} + {amount} < 0）⇒ 不改动");
                return false;
            }
            _fallbackGold += amount;
            EmitInventoryChanged();
            return true;
        }

        // ── 内部：药水生效 ──────────────────────────────────────────────────────

        private bool ApplyPotion(ItemStack item, string kind)
        {
            var p = Player;
            if (p == null)
            {
                WarnNoPlayer("ApplyPotion");
                Log.Warn("Item", $"ApplyPotion：「{item.name}」无法生效（Player 未接入）");
                return false;
            }

            switch (kind)
            {
                case "hpot":
                    p.Heal(BeltHelper.EffectAmountOf(item));
                    return true;
                case "mpot":
                    p.RestoreMana(BeltHelper.EffectAmountOf(item));
                    return true;
                case "spot":
                    p.RestoreStamina(BeltHelper.EffectAmountOf(item));
                    return true;
                case "rpot":
                {
                    // 原版：回复药水按**上限百分比**恢复；完全回复药水（code = rvl）100%，其余 35%
                    var row = Tables.Default.Item.Get(item.itemId);
                    var pct = row != null && row.Code == "rvl" ? FullRejuvPercent : RejuvPercent;
                    p.Heal(Mathf.Max(1, p.MaxLife * pct / 100));
                    p.RestoreMana(Mathf.Max(1, p.MaxMana * pct / 100));
                    return true;
                }
                default:
                    Log.Warn("Item", $"ApplyPotion：未知药水子类 \"{kind}\"（item_c.subtype）⇒ 不生效");
                    return false;
            }
        }

        // ── 内部：修理 ─────────────────────────────────────────────────────────

        private static int RepairCostOf(ItemStack item)
        {
            if (item == null || item.maxDurability <= 0) return 0;
            var missing = item.maxDurability - item.durability;
            if (missing <= 0) return 0;
            return Mathf.Max(1, Mathf.RoundToInt(missing / (float)item.maxDurability * item.price * RepairPriceRatio));
        }

        private void RestoreAllDurability()
        {
            for (var i = 0; i < _inv.Slots.Count; i++)
            {
                var s = _inv.Slots[i];
                if (s.isAnchor && s.item != null && s.item.maxDurability > 0) s.item.durability = s.item.maxDurability;
            }
            for (var i = 0; i < _equip.Items.Count; i++)
            {
                var it = _equip.Items[i];
                if (it != null && it.maxDurability > 0) it.durability = it.maxDurability;
            }
        }

        // ── 内部：事件 ─────────────────────────────────────────────────────────

        private void EmitInventoryChanged()
        {
            Game.Event?.Emit(Events.InventoryChanged, Snapshot());
        }

        /// <summary>
        /// 点击落点 → 是不是"点了某件地面物品"（容差 <see cref="ClickPickupSlack"/> 格）。
        /// 是 ⇒ 记下意图，等玩家走到 `GameConst.PickupRange` 内再捡（**距离校验仍然生效**）。
        /// </summary>
        private void OnMoveCommand(Vector2Int target)
        {
            var id = _ground.Nearest(target, ClickPickupSlack);
            if (id < 0)
            {
                if (_pendingPickupId >= 0)
                {
                    Log.Info("Item", $"点击 ({target.x},{target.y}) 附近没有地面物品 ⇒ 取消上一次待拾取意图 #{_pendingPickupId}");
                    _pendingPickupId = -1;
                }
                return;
            }

            if (id == _pendingPickupId) return;
            _pendingPickupId = id;
            var it = _ground.Get(id);
            Log.Info("Item", $"点击 ({target.x},{target.y}) 命中地面物品 #{id}「{(it != null ? it.name : "?")}」"
                + "⇒ 走过去后自动拾取（背包满则留在原地）；取消上一次意图");
        }

        /// <summary>走到待拾取物品的 `GameConst.PickupRange` 内就捡一次（失败不再重试，避免刷日志）。</summary>
        private void TryArrivePickup()
        {
            if (_pendingPickupId < 0) return;

            var p = Player;
            if (p == null) return;

            Vector2Int cell;
            if (!_ground.TryGetCell(_pendingPickupId, out cell))
            {
                _pendingPickupId = -1;                       // 已被别人捡走
                return;
            }
            if (GroundHelper.Distance(p.Grid, cell) > GameConst.PickupRange) return;

            var id = _pendingPickupId;
            _pendingPickupId = -1;
            Log.Info("Item", $"已走到地面物品 #{id} 旁（玩家格 ({p.Grid.x},{p.Grid.y})）⇒ 自动拾取");
            Pickup(id);
        }

        private void OnUseBeltRequest(int index)
        {
            UseBeltSlot(index);
        }

        private void OnPickupRequest(int groundItemId)
        {
            if (groundItemId < 0)
            {
                var p = Player;
                var grid = p != null ? p.Grid : Vector2Int.zero;
                PickupNearest(grid, GameConst.PickupRange);
                return;
            }
            Pickup(groundItemId);
        }

        private void OnEquipToggleRequest(int anchorIndex)
        {
            EquipFromInventory(anchorIndex);
        }

        private void OnItemDropRequest(int anchorIndex)
        {
            var item = _inv.GetAt(anchorIndex);
            if (item == null)
            {
                Log.Warn("Item", $"ItemDropRequest：锚点格 {anchorIndex} 上没有物品");
                return;
            }
            if (item.isQuestItem)
            {
                Log.Warn("Item", $"ItemDropRequest：「{item.name}」是任务物品 ⇒ 不可丢弃");
                return;
            }

            var p = Player;
            var grid = p != null ? p.Grid : Vector2Int.zero;
            if (!RemoveFromInventory(anchorIndex)) return;
            DropToGround(item, grid);
            Log.Info("Item", $"丢弃「{item.name}」到 ({grid.x},{grid.y})");
        }

        // ── 内部：杂项 ─────────────────────────────────────────────────────────

        private static int CountGold(List<ItemStack> items)
        {
            var n = 0;
            for (var i = 0; i < items.Count; i++)
            {
                if (items[i] != null && items[i].isGold) n++;
            }
            return n;
        }

        private int CountAnchors()
        {
            var n = 0;
            for (var i = 0; i < _inv.Slots.Count; i++)
            {
                if (_inv.Slots[i].isAnchor) n++;
            }
            return n;
        }

        private int AttackRatingOfPlayer()
        {
            var p = Player;
            return p != null ? p.AttackRating : 0;
        }

        private int DefenseOfPlayer()
        {
            var p = Player;
            return p != null ? p.Defense : 0;
        }

        private int LifeOfPlayer()
        {
            var p = Player;
            return p != null ? p.Life : 0;
        }

        private int ManaOfPlayer()
        {
            var p = Player;
            return p != null ? p.Mana : 0;
        }
    }
}
