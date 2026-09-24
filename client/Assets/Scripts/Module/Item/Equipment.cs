// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · Module/Item/Equipment.cs
// 装备栏：头盔 / 盔甲 / 武器 / 盾 / 手套 / 靴子 / 腰带 / 项链 / **双戒指**（`Def.ItemSlot`）。
//
// 槽位判定**只认配表**：`item_c.type`（weap/armo/misc）+ `item_c.subtype`（官方 type2/wclass）。
// 同槽可多件（`Def.ItemSlot` 注释：戒指两枚；武器按双武器组处理）→ 用 **槽内序号 slotIndex** 区分：
//   `slotIndex` = 在装备列表里，**同类槽位中的出现次序**（0 起）。
//
// 本类只做"格子/槽位/替换"纯数据操作；**属性重算由 ItemModule 发 `Events.EquipChanged` 通知 Player 模块**。
// ─────────────────────────────────────────────────────────────────────────────

using System.Collections.Generic;
using Diablo2.Core;
using Diablo2.Def;
using Table;

namespace Diablo2.Module.Item
{
    /// <summary>装备栏（纯数据操作；属性生效走事件）。</summary>
    internal sealed class Equipment
    {
        private readonly List<ItemStack> _items = new List<ItemStack>();

        /// <summary>
        /// 双武器组：**生效**组的下标（0 = Ⅰ组 / 1 = Ⅱ组）。
        /// 读取一律经 <see cref="ActiveWeaponIndex"/>（按实际武器件数钳制）⇒ 本字段可以"脏"。
        /// </summary>
        private int _activeWeapon;

        // 限频告警：**不用** `Log.WarnThrottled`（它读 `UnityEngine.Time.realtimeSinceStartup`，
        // 离线宿主 `tools/itemcheck/` 会抛 SecurityException）⇒ 本类自带"同 key 只报一次"。
        private static readonly HashSet<string> Warned = new HashSet<string>();
        private const int WarnedCap = 64;

        private static void WarnOnce(string key, string msg)
        {
            if (Warned.Count > WarnedCap) return;
            if (!Warned.Add(key)) return;
            Log.Warn("Item", msg);
        }

        /// <summary>已装备物品（顺序即槽内序号；视图/存档直接用它）。</summary>
        public IReadOnlyList<ItemStack> Items
        {
            get { return _items; }
        }

        /// <summary>
        /// `item_c` 行 → 装备槽位（不是装备返回 <see cref="ItemSlot.None"/>）。
        /// 列语义：`source` = 大类（weap/armo/misc）；`type` = 官方 `type`（axe/tors/helm/ring…）；
        /// `subtype` = 官方 `type2`（1hs/stf/bow/xbw/tpot…，护具与首饰**为空**）
        /// ⇒ 细分类必须 **`type` 与 `subtype` 一起看**。
        /// </summary>
        public static ItemSlot SlotOf(BaseItemRow row)
        {
            if (row == null) return ItemSlot.None;

            if (row.Source == "weap")
            {
                // 投掷药水（官方 type2 = tpot）不是可穿戴武器
                return row.Subtype == "tpot" ? ItemSlot.None : ItemSlot.Weapon;
            }

            if (row.Source == "armo")
            {
                var t = row.Type;
                switch (t)
                {
                    case "helm":
                    case "pelt":
                    case "phlm":
                    case "circ":
                        return ItemSlot.Helm;
                    case "tors":
                        return ItemSlot.Armor;
                    case "shie":
                    case "ashd":
                        return ItemSlot.Shield;
                    case "glov":
                        return ItemSlot.Gloves;
                    case "boot":
                        return ItemSlot.Boots;
                    case "belt":
                        return ItemSlot.Belt;
                }
                // 防具但 type 没登记：按盔甲处理（原版 all armor 都可穿）
                WarnOnce("slot.armo." + t, $"装备槽位：armo type=\"{t}\" 未登记 ⇒ 按盔甲处理");
                return ItemSlot.Armor;
            }

            if (row.Source == "misc")
            {
                if (row.Type == "amul") return ItemSlot.Amulet;
                if (row.Type == "ring") return ItemSlot.Ring;
            }
            return ItemSlot.None;
        }

        /// <summary>物品 → 槽位（查配表；配表缺行时退回 `ItemStack.type`）。</summary>
        public static ItemSlot SlotOf(ItemStack item)
        {
            if (item == null) return ItemSlot.None;
            var row = Tables.Default.Item.Get(item.itemId);
            if (row != null) return SlotOf(row);

            WarnOnce("slot.rowmiss." + item.itemId,
                $"装备槽位：`item_c` 缺 id={item.itemId} 的行 ⇒ 按 ItemStack.type 粗略判定");
            if (item.type == ItemType.Weapon) return ItemSlot.Weapon;
            if (item.type == ItemType.Armor) return ItemSlot.Armor;
            return ItemSlot.None;
        }

        /// <summary>同槽最多几件（双戒指 / 双武器组；其余 1）。</summary>
        public static int MaxPerSlot(ItemSlot slot)
        {
            switch (slot)
            {
                case ItemSlot.Ring: return 2;
                case ItemSlot.Weapon: return 2;
                case ItemSlot.None: return 0;
                default: return 1;
            }
        }

        /// <summary>取某槽某个序号的装备（无则 null）。</summary>
        public ItemStack Get(ItemSlot slot, int slotIndex)
        {
            var seen = 0;
            for (var i = 0; i < _items.Count; i++)
            {
                var it = _items[i];
                if (it == null || SlotOf(it) != slot) continue;
                if (seen == slotIndex) return it;
                seen++;
            }
            return null;
        }


        /// <summary>
        /// 当前**生效**的武器组下标（0 = Ⅰ组 / 1 = Ⅱ组）。
        /// <para>
        /// 语义：<see cref="ItemSlot.Weapon"/> 槽最多两件（<see cref="MaxPerSlot"/> = 2，本类文件头已写
        /// 「武器按双武器组处理」）⇒ 槽内序号 0 = Ⅰ组、1 = Ⅱ组。**只有生效组那把算"主手"**：
        /// 属性/伤害只吃它的词缀（见 <see cref="ToActiveList"/>），另一把是备用的另一组（原版切出去
        /// 的那一套不生效）。
        /// </para>
        /// <para>读取一律**按当前实际武器件数钳制**（卸下/丢弃后序号自然回落）⇒ 永不越界。</para>
        /// </summary>
        public int ActiveWeaponIndex
        {
            get
            {
                var n = WeaponCount;
                if (n <= 0) return 0;
                if (_activeWeapon < 0) return 0;
                return _activeWeapon >= n ? n - 1 : _activeWeapon;
            }
        }

        /// <summary>武器槽里的件数（0/1/2；= 已装备的武器组套数）。</summary>
        public int WeaponCount
        {
            get
            {
                var n = 0;
                for (var i = 0; i < _items.Count; i++)
                {
                    var it = _items[i];
                    if (it != null && SlotOf(it) == ItemSlot.Weapon) n++;
                }
                return n;
            }
        }

        /// <summary>当前生效武器组里的那把武器（没有武器 ⇒ null）。</summary>
        public ItemStack ActiveWeapon => Get(ItemSlot.Weapon, ActiveWeaponIndex);

        /// <summary>
        /// 设生效武器组（**读档 / 刚装备一把新武器**时用）；越界 ⇒ 钳到合法范围并告警一次
        /// （非预期分支必须留痕，用本类自带的 `WarnOnce`）。
        /// </summary>
        public void SetActiveWeapon(int index)
        {
            var n = WeaponCount;
            if (n <= 0)
            {
                if (index != 0) WarnOnce("equip.wgroup.none", $"设置武器组 {index}：当前没有装备任何武器 ⇒ 按 0 处理");
                _activeWeapon = 0;
                return;
            }

            if (index < 0 || index >= n)
            {
                var clamped = index < 0 ? 0 : n - 1;
                WarnOnce("equip.wgroup.range", $"武器组下标 {index} 越界（当前 {n} 件武器）⇒ 钳到 {clamped}");
                _activeWeapon = clamped;
                return;
            }

            _activeWeapon = index;
        }

        /// <summary>
        /// 在两组武器之间切换（Ⅰ ⇄ Ⅱ），返回切换后的下标。
        /// **只有 1 件武器（或没有）⇒ 不改动、原样返回**（调用方据此打 Warn：原版切组需要两套武器）。
        /// 原版 D2 的 <c>W</c> 做的正是这件事 —— 本项目把"两套武器"落在武器槽的两个序号上。
        /// </summary>
        public int ToggleActiveWeapon()
        {
            if (WeaponCount < 2) return ActiveWeaponIndex;   // 无可切换目标
            _activeWeapon = ActiveWeaponIndex == 0 ? 1 : 0;
            return _activeWeapon;
        }

        /// <summary>
        /// **生效装备列表**（`Events.EquipChanged` / `Events.InventoryChanged` 的载荷）
        /// = 全部装备**去掉非生效组的武器**。
        /// <para>
        /// 为什么必须过滤（双武器组"真的生效"的关键）：`PlayerStats.ApplyEquipment` 会对载荷里
        /// **每一件**装备的词缀求和 ⇒ 若两把武器都给它，备用武器会**同时**加属性/伤害
        /// （成了"背着两把武器 = 双倍词缀"）。原版只有当前那一套武器生效。
        /// </para>
        /// <para>
        /// 另一个后果正是我们要的**可见反馈**：`UI/InventoryPanel.ApplyEquip` 用
        /// `FindEquipped(equip, ItemSlot.Weapon, 0)` 取右手槽（`rarm`）显示的武器 ⇒ 过滤后
        /// **切组会真的换掉背包面板右手槽上的那把武器图**（用的是原版物品图 + 原版槽位，未自画任何图）。
        /// </para>
        /// </summary>
        public List<ItemStack> ToActiveList()
        {
            var res = new List<ItemStack>(_items.Count);
            var active = ActiveWeaponIndex;
            var seen = 0;
            for (var i = 0; i < _items.Count; i++)
            {
                var it = _items[i];
                if (it == null) continue;

                if (SlotOf(it) == ItemSlot.Weapon)
                {
                    var mine = seen++;
                    if (mine != active) continue;            // 非生效组的武器：不进载荷
                }
                res.Add(it);
            }
            return res;
        }

        /// <summary>
        /// 装备一件（自动挑选槽内空位；满则该槽序号 0 被换下）。
        /// 返回 false 表示"不是装备"（<paramref name="replaced"/> 无意义）。
        /// </summary>
        public bool TryEquip(ItemStack item, out ItemSlot slot, out int slotIndex, out ItemStack replaced)
        {
            replaced = null;
            slot = ItemSlot.None;
            slotIndex = -1;
            if (item == null) return false;

            slot = SlotOf(item);
            if (slot == ItemSlot.None)
            {
                Log.Warn("Item", $"Equipment.TryEquip：「{item.name}」不是可装备物品（配表 type/subtype 不落在装备槽）⇒ 拒绝");
                return false;
            }

            var max = MaxPerSlot(slot);
            for (var i = 0; i < max; i++)
            {
                if (Get(slot, i) == null)
                {
                    // 追加到列表尾（顺序不影响正确性：槽位/序号靠扫描得出）
                    _items.Add(item);
                    slotIndex = i;
                    return true;
                }
            }

            // 满了：换下序号 0（原版也是"装新的把旧的丢回背包"）
            slotIndex = 0;
            Replace(slot, 0, item, out replaced);
            Log.Info("Item", $"Equipment.TryEquip：{slot} 槽已满（{max} 件）⇒ 换下「{(replaced != null ? replaced.name : "-")}」");
            return true;
        }

        /// <summary>
        /// 把某槽某序号上的装备**原地替换**成 <paramref name="item"/>（用于"换装回滚"）。
        /// 该序号为空时等价于装上。被换下的物品从 <paramref name="old"/> 带出（可能为 null）。
        /// </summary>
        public void Replace(ItemSlot slot, int slotIndex, ItemStack item, out ItemStack old)
        {
            old = Get(slot, slotIndex);
            if (old != null)
            {
                var at = _items.IndexOf(old);
                if (at >= 0)
                {
                    _items[at] = item;
                    return;
                }
            }

            WarnOnce("equip.replace." + slot,
                $"Equipment.Replace：{slot} 槽序号 {slotIndex} 上没有装备（回滚路径）⇒ 直接追加");
            _items.Add(item);
        }

        /// <summary>卸下某槽某序号；成功返回 true 并带出物品。</summary>
        public bool Unequip(ItemSlot slot, int slotIndex, out ItemStack removed)
        {
            removed = Get(slot, slotIndex);
            if (removed == null)
            {
                Log.Warn("Item", $"Equipment.Unequip：{slot} 槽序号 {slotIndex} 上没有装备（槽为空或序号越界）⇒ 失败");
                return false;
            }
            _items.Remove(removed);
            return true;
        }

        /// <summary>清空（含生效武器组复位）。</summary>
        public void Clear()
        {
            _items.Clear();
            _activeWeapon = 0;
        }

        /// <summary>按存档恢复（非装备条目 Warn 跳过）。</summary>
        public void LoadFrom(List<ItemStack> saved)
        {
            _items.Clear();
            _activeWeapon = 0;                  // ★ 双武器组：读档先归零，由 ItemModule 按存档字段设（缺字段 ⇒ 默认 0）
            if (saved == null)
            {
                Log.Warn("Item", "Equipment.LoadFrom 收到 null（存档里没有装备字段）⇒ 按空装备处理");
                return;
            }

            for (var i = 0; i < saved.Count; i++)
            {
                var it = saved[i];
                if (it == null) continue;
                var slot = SlotOf(it);
                if (slot == ItemSlot.None)
                {
                    Log.Warn("Item", $"Equipment.LoadFrom：存档里的「{it.name}」不是装备 ⇒ 跳过");
                    continue;
                }
                _items.Add(it);
            }
        }

        /// <summary>导出存档用深拷贝（浅拷贝元素引用即可 —— 存档只读字段）。</summary>
        public List<ItemStack> ToList()
        {
            return new List<ItemStack>(_items);
        }
    }
}
