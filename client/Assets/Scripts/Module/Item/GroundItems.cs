// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · Module/Item/GroundItems.cs
// 地面物品（怪物掉落 / 玩家丢弃）。
//
// 键 = **地面物品 id**（唯一，从 `GameConst.GroundItemIdBase` 起递增）：
//   与 `IItemModule.Pickup(int groundItemId)`、`Events.GroundItemRemoved(int groundItemId)`、
//   `IViewModule.CreateGroundItem(int groundItemId, ...)` 的口径一致。
//
// 位置单独存一份：`Pickup` 的距离校验（`GameConst.PickupRange`）与视图摆位都要它。
// 掉落时间单独存一份：`GameConst.GroundItemLifetimeSeconds` 到期回收（`Tick` 里做）。
//
// 契约硬要求：**拾取失败（超距 / 背包满）物品必须留在原地** ⇒ 本类不提供"取出即删"的接口，
//   只有显式的 `Remove(id)`；调用方确认成功后才删。
// ─────────────────────────────────────────────────────────────────────────────

using System.Collections.Generic;
using Diablo2.Core;
using Diablo2.Def;
using UnityEngine;

namespace Diablo2.Module.Item
{
    /// <summary>地面物品表（纯数据）。</summary>
    internal sealed class GroundItems
    {
        private readonly Dictionary<int, ItemStack> _items = new Dictionary<int, ItemStack>();
        private readonly Dictionary<int, Vector2Int> _cells = new Dictionary<int, Vector2Int>();
        private readonly Dictionary<int, float> _droppedAt = new Dictionary<int, float>();
        private readonly List<int> _order = new List<int>();

        private List<KeyValuePair<int, ItemStack>> _snapshot;
        private int _nextId = GameConst.GroundItemIdBase;


        /// <summary>当前地面物品数。</summary>
        public int Count
        {
            get { return _order.Count; }
        }

        /// <summary>快照（`IItemModule.GroundItems` 返回它；内容变化时重建）。</summary>
        public IReadOnlyList<KeyValuePair<int, ItemStack>> All
        {
            get
            {
                if (_snapshot == null)
                {
                    _snapshot = new List<KeyValuePair<int, ItemStack>>(_order.Count);
                    for (var i = 0; i < _order.Count; i++)
                    {
                        var id = _order[i];
                        ItemStack it;
                        if (_items.TryGetValue(id, out it))
                            _snapshot.Add(new KeyValuePair<int, ItemStack>(id, it));
                    }
                }
                return _snapshot;
            }
        }

        /// <summary>落到地面（返回新的地面物品 id）。</summary>
        public int Add(ItemStack item, Vector2Int cell, float now)
        {
            if (item == null)
            {
                Log.Warn("Item", "GroundItems.Add 收到 null 物品 ⇒ 忽略");
                return -1;
            }

            var id = _nextId++;
            _items[id] = item;
            _cells[id] = cell;
            _droppedAt[id] = now;
            _order.Add(id);
            _snapshot = null;
            return id;
        }

        /// <summary>移除（拾取成功 / 超时回收）。</summary>
        public bool Remove(int id)
        {
            if (!_items.Remove(id))
            {
                Log.Warn("Item", $"GroundItems.Remove：地面物品 id={id} 不存在（可能已被拾取）");
                return false;
            }
            _cells.Remove(id);
            _droppedAt.Remove(id);
            _order.Remove(id);
            _snapshot = null;
            return true;
        }

        /// <summary>取物品。</summary>
        public ItemStack Get(int id)
        {
            ItemStack it;
            return _items.TryGetValue(id, out it) ? it : null;
        }

        /// <summary>取所在格；不存在返回 false。</summary>
        public bool TryGetCell(int id, out Vector2Int cell)
        {
            return _cells.TryGetValue(id, out cell);
        }

        /// <summary>取掉落时刻（秒，`Time.time` 口径由调用方给）。</summary>
        public bool TryGetDroppedAt(int id, out float droppedAt)
        {
            return _droppedAt.TryGetValue(id, out droppedAt);
        }

        /// <summary>
        /// 距 <paramref name="from"/> 最近、且在 <paramref name="maxRange"/> 格内的地面物品 id（无则 -1）。
        /// <para>
        /// ★ ★ 距离口径 = **Chebyshev（八向步数，`Iso.GridDistance`）**，⛔ 不是欧氏 ——
        /// 与拾取判定的格邻接口径**同一个**（2026-09-23 修 N1：斜邻欧氏 √2≈1.414 会被
        /// `maxRange=1.0`（`ItemModule.ClickPickupSlack`）/`1.4`（`GameConst.PickupRange`）
        /// 判成"太远"⇒ 斜角的格子永远点不到、捡不到）。
        /// 8 邻域口径下斜邻 = **1 格**，与本项目"最近可走格回退"（`Module/Player/PlayerModule`）
        /// 同一套（见 `Iso.GridDistance` 的注释）。
        /// </para>
        /// </summary>
        public int Nearest(Vector2Int from, float maxRange)
        {
            var best = -1;
            var bestD = float.MaxValue;
            for (var i = 0; i < _order.Count; i++)
            {
                var id = _order[i];
                Vector2Int cell;
                if (!_cells.TryGetValue(id, out cell)) continue;
                var d = Iso.GridDistance(from, cell);          // Chebyshev（八向步数）
                if (d > maxRange) continue;
                if (d < bestD)
                {
                    bestD = d;
                    best = id;
                }
            }
            return best;
        }

        /// <summary>
        /// 两格间**欧氏**距离（格为单位）。用途只剩**日志/诊断**（把"看起来多远"写清楚）；
        /// ⛔ 判定一律走 Chebyshev（<see cref="Iso.GridDistance"/> / <see cref="Iso.IsAdjacent"/>）。
        /// </summary>
        public static float Distance(Vector2Int a, Vector2Int b)
        {
            var dx = a.x - b.x;
            var dy = a.y - b.y;
            return Mathf.Sqrt(dx * dx + dy * dy);
        }

        /// <summary>清空（退出 Stage / 回主菜单）。</summary>
        public void Clear()
        {
            _items.Clear();
            _cells.Clear();
            _droppedAt.Clear();
            _order.Clear();
            _snapshot = null;
            _nextId = GameConst.GroundItemIdBase;
        }

        /// <summary>回收超时掉落；返回被回收的 id 列表（调用方发 `Events.GroundItemRemoved`）。</summary>
        public List<int> CollectExpired(float now, float lifetimeSeconds, List<int> outIds)
        {
            if (outIds == null) outIds = new List<int>();
            if (lifetimeSeconds <= 0f) return outIds;

            for (var i = _order.Count - 1; i >= 0; i--)
            {
                var id = _order[i];
                float t;
                if (!_droppedAt.TryGetValue(id, out t)) continue;
                if (now - t < lifetimeSeconds) continue;
                outIds.Add(id);
            }

            for (var i = 0; i < outIds.Count; i++)
            {
                var id = outIds[i];
                _items.Remove(id);
                _cells.Remove(id);
                _droppedAt.Remove(id);
                _order.Remove(id);
            }
            if (outIds.Count > 0) _snapshot = null;
            return outIds;
        }
    }
}
