// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · Module/Npc/NpcShop.cs
// **商店货物与价格**：恰西（铁匠：武器/防具/修理）、基德 & 阿卡拉（杂货：药水/卷轴/钥匙/弹药）。
//
// 数值来源（**取配表，不硬编码价格**）：
//   · 买入价 = `item_c.price`（`Def.ItemStack.price` 原样带过来）；
//   · 卖出价 = 买入价 × `GameConst.SellPriceRatio`（折算系数在 `Core/GameConst.cs`，
//     商店面板的悬停价格显示与这里共用同一份）；
//   · 商品清单：铁匠的装备由 `IItemModule.CreateRandom` 生成（尺寸/耐久/词缀全由 Item 模块负责，
//     本模块**不重复**物品生成逻辑）；杂货铺的消耗品由 `item_c` 行直接落成（无词缀、无耐久，
//     只用配表列，见 MakeConsumable 注释）。
//   · 货物确定性：铁匠装备的随机器 seed = `mapSeed * 31 + npcId` ⇒ **同一局同一家店货物不变**。
//
// 为什么消耗品要本地落成：冻结的 `IItemModule` 只有 `CreateRandom(level, rng)`，**没有"按 id 造物品"**，
//    而商店必须能卖指定的药水/卷轴 ⇒ 只能由本模块按配表行构造（已在回报「未决」第 5 条登记）。
// ─────────────────────────────────────────────────────────────────────────────

using System.Collections.Generic;
using CloverEngine;
using Diablo2.Core;
using Diablo2.Def;
using Table;
using UnityEngine;

namespace Diablo2.Module.Npc
{
    /// <summary>一家店的货物（纯数据；买卖结算是 NpcModule 的事）。</summary>
    internal sealed class NpcShop
    {
        /// <summary>铁匠店的装备件数。</summary>
        private const int BlacksmithItemCount = 8;

        /// <summary>杂货店的消耗品件数上限。</summary>
        private const int MiscItemCount = 14;

        /// <summary>铁匠店装备的等级上限（只用配表 `item_c.item_level` 过滤）。</summary>
        private const int BlacksmithLevelCap = 12;

        /// <summary>
        /// 杂货铺经营的细分类（消耗品；值取 `item_c.type`：药水/卷轴/钥匙/弹药。
        /// 「不是我方」的金币 `gld`、首饰 `amu`/`rin`、宝石等都不算）。
        /// </summary>
        private static readonly string[] MiscTypes = { "hpot", "mpot", "spot", "rpot", "scro", "key", "bowq", "xboq" };

        private readonly List<ShopEntry> _entries = new List<ShopEntry>();
        private readonly List<ItemStack> _items = new List<ItemStack>();

        /// <summary>
        /// 「只报一次」标记（同一家店同类问题说一遍就够）：货物占格 `gridW/gridH` 非正
        /// （配表 `item_c.grid_w/grid_h` 缺值/为 0）⇒ 收敛到 1×1 并点名（见 <see cref="AddEntry"/>）。
        /// </summary>
        private bool _warnedBadGrid;

        /// <summary>本次构建用的 NPC（重开店时用于判断货物是否还是同一家）。</summary>
        public int BuiltNpcId { get; private set; } = (int)NpcId.None;

        /// <summary>本次构建用的地图 seed。</summary>
        public int BuiltSeed { get; private set; }

        /// <summary>货物快照（`ShopOpenArgs.stock`）。</summary>
        public IReadOnlyList<ShopEntry> Entries
        {
            get { return _entries; }
        }

        /// <summary>
        /// 构建货物。`item` 为空（降级）时只留空货架并打日志。
        /// </summary>
        public void Build(NpcDef npc, int mapSeed, int playerLevel, IItemModule item, int playerGold)
        {
            _entries.Clear();
            _items.Clear();
            _warnedBadGrid = false;
            BuiltNpcId = npc != null ? npc.id : (int)NpcId.None;
            BuiltSeed = mapSeed;

            if (npc == null) return;

            if (item == null)
            {
                Log.Warn("Npc", $"商店构建：`IItemModule` 未接入 ⇒ 「{npc.name}」的货架为空（买卖会失败）");
                return;
            }

            if (npc.isBlacksmith) BuildBlacksmith(npc, mapSeed, playerLevel, item, playerGold);
            else BuildMisc(npc, playerGold);

            Log.Info("Npc", $"商店已上架：「{npc.name}」{_entries.Count} 件"
                + $"（blacksmith={npc.isBlacksmith} seed={mapSeed} 玩家金币={playerGold}）");
        }

        /// <summary>取第 index 件货物（index 越界或不卖返回 null）。用于结算时拿"要造哪件"。</summary>
        public ShopEntry EntryAt(int index)
        {
            if (index < 0 || index >= _entries.Count) return null;
            return _entries[index];
        }

        /// <summary>取第 index 件货物的物品模板（装备为生成实例；消耗品为配表行落成）。</summary>
        public bool TryGetItem(int index, out ItemStack item)
        {
            item = null;
            if (index < 0 || index >= _items.Count) return false;
            item = _items[index];
            return item != null;
        }

        /// <summary>限量商品卖出一件（`count &gt; 0` 时递减；无限商品不动）。</summary>
        public void Consume(int index)
        {
            var e = EntryAt(index);
            if (e == null || e.count < 0) return;
            if (e.count > 0) e.count -= 1;
        }

        /// <summary>按当前金币刷新"买得起"标记（面板高亮用）。</summary>
        public void RefreshAffordable(int playerGold)
        {
            for (var i = 0; i < _entries.Count; i++)
            {
                _entries[i].affordable = _entries[i].count != 0 && playerGold >= _entries[i].price;
            }
        }

        /// <summary>卖出价（= `GameConst.SellPriceOf(item.price)`，至少 1；`item == null` ⇒ 0）。</summary>
        public static int SellPriceOf(ItemStack item)
            => item == null ? 0 : GameConst.SellPriceOf(item.price);

        // ── 内部：两类货架 ─────────────────────────────────────────────────────

        private void BuildBlacksmith(NpcDef npc, int mapSeed, int playerLevel, IItemModule item, int playerGold)
        {
            // 随机器 seed 只由 (mapSeed, npcId) 决定 ⇒ 同一局同一家店货物固定（重开面板不会变）
            var rng = new Rng(unchecked(mapSeed * 31 + npc.id));
            var level = Mathf.Clamp(Mathf.Max(1, playerLevel), 1, BlacksmithLevelCap);

            for (var i = 0; i < BlacksmithItemCount; i++)
            {
                var st = item.CreateRandom(level, rng);
                if (st == null) break;
                if (st.isGold) continue;                      // 商店不卖金币
                AddEntry(st, 1, playerGold);                  // 装备限量 1 件（原版一物一件）
            }
        }

        private void BuildMisc(NpcDef npc, int playerGold)
        {
            var all = Tables.Default.Item.All();
            var rows = new List<BaseItemRow>();
            for (var i = 0; i < all.Count; i++)
            {
                var r = all[i];
                if (r == null || r.Source != "misc") continue;      // 大类看 source
                if (!IsMiscType(r.Type)) continue;                  // 细分类看 type
                rows.Add(r);
            }
            rows.Sort((a, b) => a.Price.CompareTo(b.Price));

            var n = Mathf.Min(MiscItemCount, rows.Count);
            for (var i = 0; i < n; i++)
            {
                var st = MakeConsumable(rows[i]);
                if (st == null) continue;
                AddEntry(st, -1, playerGold);                 // 消耗品无限量
            }
        }

        private static bool IsMiscType(string type)
        {
            for (var i = 0; i < MiscTypes.Length; i++)
            {
                if (MiscTypes[i] == type) return true;
            }
            return false;
        }

        /// <summary>
        /// 由 `item_c` 行直接落成一件消耗品（药水/卷轴/钥匙/弹药）。
        /// **只拷配表列**：尺寸 / 等级需求 / 力量需求 / 价格；无词缀、无耐久（消耗品本来就没有）。
        /// </summary>
        private static ItemStack MakeConsumable(BaseItemRow row)
        {
            if (row == null) return null;
            return new ItemStack
            {
                itemId = row.Id,
                name = string.IsNullOrEmpty(row.Name) ? row.Code : row.Name,
                type = ItemType.Misc,
                quality = ItemQuality.Normal,
                count = 1,
                gridW = row.GridW > 0 ? row.GridW : 1,
                gridH = row.GridH > 0 ? row.GridH : 1,
                lvlReq = row.LvlReq,
                strReq = row.StrReq,
                price = row.Price,
                isGold = false,
                isQuestItem = row.Quest != 0,
            };
        }

        private void AddEntry(ItemStack st, int count, int playerGold)
        {
            var idx = _entries.Count;

            //   尺寸的唯一来源 = `ItemFactory` 从配表 `item_c.grid_w/grid_h` 填进 `ItemStack` 的那一份
            //   （本模块**不另算**；消耗品见 `MakeConsumable` 的同源赋值）。
            var gw = st.gridW > 0 ? st.gridW : 1;
            var gh = st.gridH > 0 ? st.gridH : 1;
            if (st.gridW <= 0 || st.gridH <= 0)
            {
                // 非预期分支（配表缺列/为 0）：收敛到 1×1 并点名，不静默（只报一次，避免刷屏）
                if (!_warnedBadGrid)
                {
                    _warnedBadGrid = true;
                    Log.Warn("Npc", $"商店货物占格非正 ⇒ 收敛到 1×1：「{st.name}」"
                        + $"(itemId={st.itemId}, gridW={st.gridW}, gridH={st.gridH})；"
                        + "请核对配表 item_c.grid_w/grid_h（同一家店同类问题只报一次）");
                }
            }

            _entries.Add(new ShopEntry
            {
                index = idx,
                itemId = st.itemId,
                name = st.name,
                quality = st.quality,
                gridW = gw,
                gridH = gh,
                price = st.price,
                count = count,
                affordable = playerGold >= st.price,
            });
            _items.Add(st);
        }
    }
}
