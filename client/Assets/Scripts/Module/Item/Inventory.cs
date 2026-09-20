// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · Module/Item/Inventory.cs
// **10×4 格子背包**（`GameConst.InventoryCols` × `GameConst.InventoryRows`）；
// 物品占位尺寸来自配表 `item_c.grid_w/grid_h`（**不硬编码**）。
//
// 占用模型（与 `Def.InventorySlot` 的注释一致）：
//   · 物品只挂在**锚点格**（左上角格，`isAnchor = true`、`item != null`）；
//   · 其余被覆盖的格 `occupied = true`、`item = null`、`anchorIndex = 锚点线性下标`；
//   · 线性下标 = `y * InventoryCols + x`（行优先）。
//
// 放不下时**必须明确失败返回 false**（不许假装成功）—— 由调用方（ItemModule）负责留在原地 + Toast。
// ─────────────────────────────────────────────────────────────────────────────

using System.Collections.Generic;
using Diablo2.Core;
using Diablo2.Def;

namespace Diablo2.Module.Item
{
    /// <summary>格子背包（纯数据操作，不碰事件/金币）。</summary>
    internal sealed class Inventory
    {
        private readonly List<InventorySlot> _slots = new List<InventorySlot>();

        public Inventory()
        {
            Reset();
        }

        /// <summary>40 格快照（长度恒为 `GameConst.InventoryCellCount`）。</summary>
        public IReadOnlyList<InventorySlot> Slots
        {
            get { return _slots; }
        }

        /// <summary>背包是否已满（放不下任何 1×1 物品）。</summary>
        public bool IsFull
        {
            get
            {
                for (var i = 0; i < _slots.Count; i++)
                {
                    if (!_slots[i].occupied) return false;
                }
                return true;
            }
        }

        /// <summary>空格数（日志/自证用）。</summary>
        public int FreeCellCount
        {
            get
            {
                var n = 0;
                for (var i = 0; i < _slots.Count; i++)
                {
                    if (!_slots[i].occupied) n++;
                }
                return n;
            }
        }

        /// <summary>清成 40 个空格。</summary>
        public void Reset()
        {
            _slots.Clear();
            for (var y = 0; y < GameConst.InventoryRows; y++)
            {
                for (var x = 0; x < GameConst.InventoryCols; x++)
                {
                    _slots.Add(new InventorySlot
                    {
                        index = y * GameConst.InventoryCols + x,
                        x = x,
                        y = y,
                        occupied = false,
                        isAnchor = false,
                        item = null,
                        anchorIndex = -1,
                    });
                }
            }
        }

        /// <summary>取某锚点格上的物品；不是锚点/无物品返回 null。</summary>
        public ItemStack GetAt(int anchorIndex)
        {
            if (anchorIndex < 0 || anchorIndex >= _slots.Count) return null;
            var s = _slots[anchorIndex];
            return s.isAnchor ? s.item : null;
        }

        /// <summary>
        /// 自动找位放入（行优先找**第一个**能容下 `gridW × gridH` 的锚点）。
        /// 放不下返回 false（**不改动任何格**）。
        /// </summary>
        public bool TryPlace(ItemStack item, out int anchorIndex)
        {
            anchorIndex = -1;
            if (item == null)
            {
                Log.Warn("Item", "Inventory.TryPlace 收到 null 物品 ⇒ 拒绝");
                return false;
            }

            var w = item.gridW > 0 ? item.gridW : 1;
            var h = item.gridH > 0 ? item.gridH : 1;

            for (var y = 0; y < GameConst.InventoryRows; y++)
            {
                for (var x = 0; x < GameConst.InventoryCols; x++)
                {
                    if (!CanPlaceBlock(x, y, w, h)) continue;
                    PlaceBlock(item, x, y, w, h);
                    anchorIndex = y * GameConst.InventoryCols + x;
                    return true;
                }
            }

            Log.Warn("Item", $"背包放不下「{item.name}」({w}×{h})：空格 {FreeCellCount} 但无连续块");
            return false;
        }

        /// <summary>
        /// 把可堆叠物品并入已有同类堆（同 `itemId` 且无词缀）。合并成功返回 true 并把 <paramref name="item"/>
        /// 的剩余数量写回（0 = 全部并入）。`item_c.max_stack` 由调用方写进 <see cref="ItemStack.count"/> 的语义之外，
        /// 这里只需保证不超过 <paramref name="maxStack"/>（≤ 0 = 不限制）。
        /// </summary>
        public bool TryStackInto(ItemStack item, int maxStack)
        {
            if (item == null || item.count <= 1) return false;
            var merged = false;
            for (var i = 0; i < _slots.Count; i++)
            {
                var s = _slots[i];
                if (!s.isAnchor || s.item == null) continue;
                if (s.item.itemId != item.itemId) continue;
                if (s.item.affixes != null && s.item.affixes.Count > 0) continue;
                if (item.affixes != null && item.affixes.Count > 0) continue;

                var cap = maxStack > 0 ? maxStack : int.MaxValue;
                var room = cap - s.item.count;
                if (room <= 0) continue;

                var move = room < item.count ? room : item.count;
                s.item.count += move;
                item.count -= move;
                merged = true;
                if (item.count <= 0) return true;
            }
            return merged && item.count <= 0;
        }

        /// <summary>移除某锚点格上的物品（连带释放它占的全部格）。失败返回 false 并打日志。</summary>
        public bool RemoveAt(int anchorIndex)
        {
            if (anchorIndex < 0 || anchorIndex >= _slots.Count)
            {
                Log.Warn("Item", $"Inventory.RemoveAt 下标越界 anchorIndex={anchorIndex}（0..{_slots.Count - 1}）");
                return false;
            }

            var anchor = _slots[anchorIndex];
            if (!anchor.occupied || !anchor.isAnchor || anchor.item == null)
            {
                Log.Warn("Item", $"Inventory.RemoveAt：格 {anchorIndex} 不是物品锚点（occupied={anchor.occupied} "
                    + $"isAnchor={anchor.isAnchor}）⇒ 拒绝");
                return false;
            }

            for (var i = 0; i < _slots.Count; i++)
            {
                if (_slots[i].anchorIndex != anchorIndex) continue;
                _slots[i].occupied = false;
                _slots[i].isAnchor = false;
                _slots[i].item = null;
                _slots[i].anchorIndex = -1;
            }
            return true;
        }

        /// <summary>按存档恢复（保留存档里的格位；非法格位只 Warn 跳过）。</summary>
        public void LoadFrom(List<InventorySlot> saved)
        {
            Reset();
            if (saved == null)
            {
                Log.Warn("Item", "Inventory.LoadFrom 收到 null（存档里没有背包字段）⇒ 按空背包处理");
                return;
            }

            for (var i = 0; i < saved.Count; i++)
            {
                var s = saved[i];
                if (s == null || !s.isAnchor || s.item == null) continue;

                var w = s.item.gridW > 0 ? s.item.gridW : 1;
                var h = s.item.gridH > 0 ? s.item.gridH : 1;
                if (!CanPlaceBlock(s.x, s.y, w, h))
                {
                    Log.Warn("Item", $"Inventory.LoadFrom：存档格位 ({s.x},{s.y}) {w}×{h} 越界或重叠 ⇒ 跳过「{s.item.name}」");
                    continue;
                }
                PlaceBlock(s.item, s.x, s.y, w, h);
            }
        }

        /// <summary>导出存档用深拷贝（40 格，含空格）。</summary>
        public List<InventorySlot> ToList()
        {
            var res = new List<InventorySlot>(_slots.Count);
            for (var i = 0; i < _slots.Count; i++)
            {
                var s = _slots[i];
                res.Add(new InventorySlot
                {
                    index = s.index,
                    x = s.x,
                    y = s.y,
                    occupied = s.occupied,
                    isAnchor = s.isAnchor,
                    item = s.item,
                    anchorIndex = s.anchorIndex,
                });
            }
            return res;
        }

        // ── 内部 ───────────────────────────────────────────────────────────────

        private bool CanPlaceBlock(int x, int y, int w, int h)
        {
            if (x < 0 || y < 0) return false;
            if (x + w > GameConst.InventoryCols || y + h > GameConst.InventoryRows) return false;

            for (var yy = y; yy < y + h; yy++)
            {
                for (var xx = x; xx < x + w; xx++)
                {
                    if (_slots[yy * GameConst.InventoryCols + xx].occupied) return false;
                }
            }
            return true;
        }

        private void PlaceBlock(ItemStack item, int x, int y, int w, int h)
        {
            var anchorIndex = y * GameConst.InventoryCols + x;
            var anchor = _slots[anchorIndex];
            anchor.occupied = true;
            anchor.isAnchor = true;
            anchor.item = item;
            anchor.anchorIndex = anchorIndex;

            for (var yy = y; yy < y + h; yy++)
            {
                for (var xx = x; xx < x + w; xx++)
                {
                    if (yy == y && xx == x) continue;
                    var s = _slots[yy * GameConst.InventoryCols + xx];
                    s.occupied = true;
                    s.isAnchor = false;
                    s.item = null;
                    s.anchorIndex = anchorIndex;
                }
            }
        }
    }
}
