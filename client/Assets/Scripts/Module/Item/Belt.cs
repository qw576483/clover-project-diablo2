// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · Module/Item/Belt.cs
// **4 格腰带 + 数字键 1~4 喝药**（`GameConst.BeltSlots`）。
//
// 只放药水：判定用配表 `item_c.subtype`（hpot 治疗 / mpot 法力 / spot 耐力 / rpot 回复）。
// 药水回复量（**本项目新增折算**：`item_c` 没有"回复量"列 ⇒ 取 `item_c.price` 当回复点数；
//   原版回复量在 Misc.txt 之外的数据里，等官方数值表补齐后只需改本方法一处）。
// ─────────────────────────────────────────────────────────────────────────────

using System.Collections.Generic;
using Diablo2.Core;
using Diablo2.Def;
using Table;

namespace Diablo2.Module.Item
{
    /// <summary>腰带（纯数据操作；生效走 <see cref="ItemModule.UseBeltSlot"/>）。</summary>
    internal sealed class Belt
    {
        private readonly List<ItemStack> _slots = new List<ItemStack>();

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

        public Belt()
        {
            Reset();
        }

        /// <summary>4 格（空格为 null）。</summary>
        public IReadOnlyList<ItemStack> Slots
        {
            get { return _slots; }
        }

        /// <summary>重置成 4 个空格。</summary>
        public void Reset()
        {
            _slots.Clear();
            for (var i = 0; i < GameConst.BeltSlots; i++) _slots.Add(null);
        }

        /// <summary>清空。</summary>
        public void Clear()
        {
            Reset();
        }

        /// <summary>
        /// 是不是药水（`item_c.source = misc` 且 `item_c.type` ∈ hpot/mpot/spot/rpot）。
        /// 药剂的细分类在 **`type`** 列（`subtype` 为空），别读错列。
        /// </summary>
        public static bool IsPotion(ItemStack item)
        {
            var kind = PotionKind(item);
            return kind == "hpot" || kind == "mpot" || kind == "spot" || kind == "rpot";
        }

        /// <summary>药水类型（用于决定恢复哪一项）；不是药水返回 null。</summary>
        public static string PotionKind(ItemStack item)
        {
            if (item == null) return null;
            var row = Tables.Default.Item.Get(item.itemId);
            if (row == null || row.Source != "misc") return null;
            return row.Type;
        }

        /// <summary>药水回复点数（**本项目新增折算**：= 配表 `item_c.price`；见本文件头注释）。</summary>
        public static int EffectAmountOf(ItemStack item)
        {
            if (item == null) return 0;
            var row = Tables.Default.Item.Get(item.itemId);
            var amount = row != null ? row.Price : item.price;
            if (amount <= 0)
            {
                WarnOnce("potion.amount",
                    $"腰带：药水「{item.name}」的 item_c.price={amount}（≤0）⇒ 回复量按 0 处理");
            }
            return amount > 0 ? amount : 0;
        }

        /// <summary>
        /// 放进腰带。`index &lt; 0` = 自动找位（先并入同类堆，再找空格）。
        /// 非药水 / 腰带满 → 返回 false 并打日志（调用方负责留在原地/留在背包）。
        /// </summary>
        public bool TryAdd(ItemStack item, int index)
        {
            if (item == null)
            {
                Log.Warn("Item", "Belt.TryAdd 收到 null 物品 ⇒ 拒绝");
                return false;
            }
            if (!IsPotion(item))
            {
                Log.Warn("Item", $"Belt.TryAdd：「{item.name}」不是药水（只允许 item_c.source=misc 且 item_c.type = hpot/mpot/spot/rpot）⇒ 拒绝");
                return false;
            }

            if (index >= 0 && index < _slots.Count)
            {
                _slots[index] = item;
                return true;
            }
            if (index >= _slots.Count)
            {
                Log.Warn("Item", $"Belt.TryAdd 下标越界 index={index}（0..{_slots.Count - 1}）⇒ 改为自动找位");
            }

            for (var i = 0; i < _slots.Count; i++)
            {
                if (_slots[i] == null)
                {
                    _slots[i] = item;
                    return true;
                }
            }

            Log.Warn("Item", $"腰带已满（{_slots.Count} 格）⇒「{item.name}」未放入腰带");
            return false;
        }

        /// <summary>消耗一格（数量 &gt; 1 则减一，否则腾空）。成功返回 true 并带出被用的那一个。</summary>
        public bool Take(int index, out ItemStack used)
        {
            used = null;
            if (index < 0 || index >= _slots.Count)
            {
                Log.Warn("Item", $"Belt.Take 下标越界 index={index}（0..{_slots.Count - 1}）");
                return false;
            }

            var stack = _slots[index];
            if (stack == null)
            {
                Log.Warn("Item", $"Belt.Take：腰带格 {index} 是空的（没有药水可喝）");
                return false;
            }

            // 用掉一个：数量 > 1 只减一；否则整格腾空（原版一格就是一"瓶"叠加）
            var one = new ItemStack
            {
                itemId = stack.itemId,
                name = stack.name,
                type = stack.type,
                quality = stack.quality,
                count = 1,
                gridW = stack.gridW,
                gridH = stack.gridH,
                lvlReq = stack.lvlReq,
                strReq = stack.strReq,
                dmgMin = stack.dmgMin,
                dmgMax = stack.dmgMax,
                defMin = stack.defMin,
                defMax = stack.defMax,
                price = stack.price,
                durability = stack.durability,
                maxDurability = stack.maxDurability,
                isGold = false,
                isQuestItem = stack.isQuestItem,
            };
            if (stack.affixes != null)
            {
                for (var i = 0; i < stack.affixes.Count; i++) one.affixes.Add(stack.affixes[i]);
            }

            stack.count -= 1;
            if (stack.count <= 0) _slots[index] = null;

            used = one;
            return true;
        }

        /// <summary>按存档恢复（非药水 Warn 跳过；长度不足补空）。</summary>
        public void LoadFrom(List<ItemStack> saved)
        {
            Reset();
            if (saved == null)
            {
                Log.Warn("Item", "Belt.LoadFrom 收到 null（存档里没有腰带字段）⇒ 按空腰带处理");
                return;
            }

            for (var i = 0; i < saved.Count && i < _slots.Count; i++)
            {
                var it = saved[i];
                if (it == null) continue;
                if (!IsPotion(it))
                {
                    Log.Warn("Item", $"Belt.LoadFrom：存档里腰带格 {i} 的「{it.name}」不是药水 ⇒ 跳过");
                    continue;
                }
                _slots[i] = it;
            }
        }

        /// <summary>导出存档用（4 格，空格为 null）。</summary>
        public List<ItemStack> ToList()
        {
            return new List<ItemStack>(_slots);
        }
    }
}
