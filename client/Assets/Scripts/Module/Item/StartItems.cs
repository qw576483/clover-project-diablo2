// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · Module/Item/StartItems.cs
// **新角色起始装备**：把官方 `charstats.txt` 的 `item1..item10`（配表 `start_item_c`）
// 按职业生成物品并落位（装备栏 / 背包）。
//
// 用户实测反馈「创建角色以后，没用默认武器…我用你妈逼打啊」。原版 D2 新角色自带起始装备。
//
// 数据来源（**逐条带出处**，一律不硬编码）：
//   · 哪几件 / 什么位置 / 几件 → `start_item_c`（`Tables.Default.StartItem`），
//     由 `tools/table-convert/convert.py` 的 `build_startitem()` 从
//     `原版资源/d2lod1.10txt-1.10f/data/global/excel/charstats.txt` 的 `itemN/itemNloc/itemNcount`
//     三组列**程序化抽取**；每行带 `src_line` 列 = 该职业在官方 txt 里的行号（可逐条跳去核对）。
//   · 物品属性 / 尺寸 / 伤害 / 耐久档 → `item_c`（`Tables.Default.Item`）。
//
// 原版口径（**不要自己加防具**）：官方 `charstats` 只给「武器 + 盾 + 药水 + 卷轴」，
//    新角色**没有**衣服 / 鞋子 / 头盔 —— 表里没有的行就是原版没有的行。
// `hp1`（微型治疗药水）在 `item_c` 里 `stack=0 / max_stack=0`（官方 `Misc.txt` 原值如此：
//    `stackable=0 / minstack=0 / maxstack=0`，只有 `belt=1`）⇒ 按"不可堆叠"处理：
//    count=4 ⇒ 占 **4 个背包格**。（"原版是不是该放进腰带而不是背包"见回报「未决」。）
//
// 落位复用**本模块自己的** `Inventory` / `Equipment`（不另写一份格子算法、不重造轮子）：
//   · `loc` = rarm/larm/head/… ⇒ 装备到对应槽位（`Equipment.TryEquip`，槽位靠 `item_c` 判定）；
//   · `loc` 为空 ⇒ 进背包（`Inventory.TryPlace`）。
//
// 分层：本类属 `Module/Item`，**只被本模块（`ItemModule.LoadFrom`）调用**；
//   `Module/Flow` 侧不引用它（`AppFlow` 只走已有的 `IItemModule.Reset/LoadFrom/WriteTo`）
//   ⇒ 全工程 0 处 `using Diablo2.Module.*`（分层自检 ② 的口径）。
//   入口仍是**纯函数**：`Apply(CharacterSave)` 只依赖 `Def` + `Table` + 本模块的
//   `Inventory`/`Equipment` ⇒ 可被离线宿主（`tools/probes/hosts/itemcheck`）直接调用断言。
// ─────────────────────────────────────────────────────────────────────────────

using System.Collections.Generic;
using CloverEngine;
using Diablo2.Core;
using Diablo2.Def;
using Table;

namespace Diablo2.Module.Item
{
    /// <summary>职业起始装备：`start_item_c` + `item_c` → 装备栏 / 背包。</summary>
    internal static class StartItems
    {
        private const string Tag = "Item";

        /// <summary>
        /// 生成起始物品用的**确定性**随机种子。
        /// <para>
        /// 为什么需要：<see cref="ItemFactory.Create"/> 要求非 null 的 <see cref="Rng"/>（"随机必须可复现"）。
        /// 起始装备一律**普通品质**（`ItemQuality.Normal`）⇒ 不掷词缀、不带磨损（`worn=false`）
        /// ⇒ 这个 rng **一次都不会被消费**，取值只是为了满足签名；写死常量让断言可复现。
        /// </para>
        /// </summary>
        private const int RngSeed = 20260922;

        // ── 新鲜草稿档判定 ───────────────────────────────────────────────────────

        /// <summary>
        /// 这份存档是不是**刚创出来、还什么都没发过的角色**（= 该发起始装备）。
        /// <para>
        /// 判据（**与调用顺序无关**，四条同时成立）：等级 ≤ 1 + 装备栏空 + 背包里一个锚点都没有 +
        /// 腰带全空。不用 `savedAtTicks == 0` 当唯一判据 —— 那个条件会被"先写档后发装备"的
        /// 顺序变化悄悄破坏（本类的调用点就在写档之前）。
        /// </para>
        /// <para>反面（不该发的情况，逐个已被这四条挡住）：进图读档的**老角色**必然等级 &gt; 1 或身上有东西；
        /// 刚创完角、档里已经有起始装备的角色 `equip`/`inventory` 非空。</para>
        /// </summary>
        public static bool IsFreshDraft(CharacterSave save)
        {
            if (save == null) return false;
            if (save.level > 1) return false;
            if (save.equip != null && save.equip.Count > 0) return false;
            if (save.belt != null)
            {
                for (var i = 0; i < save.belt.Count; i++)
                {
                    if (save.belt[i] != null) return false;
                }
            }
            if (save.inventory != null)
            {
                for (var i = 0; i < save.inventory.Count; i++)
                {
                    var s = save.inventory[i];
                    if (s != null && s.isAnchor && s.item != null) return false;
                }
            }
            return true;
        }

        /// <summary>
        /// 官方 `charstats.itemNloc` → 装备槽位。空串 = 官方不给位置 ⇒ **不装备（进背包）**。
        /// 返回 <see cref="ItemSlot.None"/> 有两种含义，调用方需用 <paramref name="known"/> 区分
        /// （"官方就是空" vs "认不出的 loc"）—— 后者必须留日志。
        /// </summary>
        public static ItemSlot SlotOfLoc(string loc, out bool known)
        {
            known = true;
            if (string.IsNullOrEmpty(loc)) return ItemSlot.None;
            switch (loc)
            {
                case "rarm": return ItemSlot.Weapon;    // 右手 = 主手武器
                case "larm": return ItemSlot.Shield;    // 左手 = 盾（原版双臂共用：另见 Equipment.MaxPerSlot）
                case "head": return ItemSlot.Helm;
                case "torso": return ItemSlot.Armor;
                case "glov": return ItemSlot.Gloves;
                case "boot": return ItemSlot.Boots;
                case "belt": return ItemSlot.Belt;
                case "neck": return ItemSlot.Amulet;
                case "ring": return ItemSlot.Ring;
                default:
                    known = false;
                    return ItemSlot.None;
            }
        }

        // ── 入口 ───────────────────────────────────────────────────────────────

        /// <summary>
        /// **纯函数入口**：按 <paramref name="save"/>.cls 取 `start_item_c` 的行，逐条造物品并落位，
        /// 最后**整体覆盖** `save.equip` / `save.inventory`（`save.belt` 不动 —— 配方里没有腰带队列）。
        /// <para>离线宿主用它做逐条断言；生产路径由 <see cref="ItemModule.LoadFrom"/> 走
        /// <see cref="ApplyInto"/>（同一份逻辑）。</para>
        /// </summary>
        public static void Apply(CharacterSave save)
        {
            if (save == null)
            {
                Log.Warn(Tag, "StartItems.Apply 收到 null 存档（CharacterSave）⇒ 跳过，不改任何字段");
                return;
            }

            var equip = new Equipment();
            var inv = new Inventory();
            ApplyInto(equip, inv, save.cls, save.name);
            save.equip = equip.ToList();
            save.inventory = inv.ToList();
        }

        /// <summary>
        /// 核心：把 <paramref name="cls"/> 的起始装备直接填进**给定的**装备栏与背包
        /// （供 `ItemModule.LoadFrom` 发起始装备；也可由 `Apply(CharacterSave)` 用临时实例调用）。
        /// </summary>
        /// <returns>实际生成并落位的物品件数（含拆分出的每一格）。</returns>
        public static int ApplyInto(Equipment equip, Inventory inv, PlayerClass cls, string who)
        {
            if (equip == null || inv == null)
            {
                Log.Warn(Tag, "StartItems.ApplyInto 收到 null 装备栏/背包 ⇒ 跳过（调用方接线错）");
                return 0;
            }

            var table = Tables.Default.StartItem;
            if (table == null || table.Count == 0)
            {
                Log.Error(Tag, "起始装备：配表 `start_item_c` 读到 0 行 ⇒ 新角色不会拿到任何起始装备。"
                    + "检查 ① TableLoader.TsvFiles 是否声明了 StartItem.tsv ② StreamingAssets/Table/StartItem.tsv 是否存在"
                    + "（见 TableLoader.LoadAll 的错误日志）");
                return 0;
            }

            var clsId = (int)cls;
            var factory = new ItemFactory();
            var rng = new Rng(RngSeed);

            var equipCount = 0;
            var invCount = 0;
            var noTableRows = 0;
            var badLoc = 0;
            var badCode = 0;
            var fullBag = 0;

            var rows = table.All();
            for (var i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                if (row == null) continue;
                if (row.Class != clsId) continue;
                noTableRows++;

                var itemId = ItemFactory.ItemIdOfCode(row.Code);
                if (itemId <= 0)
                {
                    // ItemIdOfCode 自己已打限频 Warn；这里再补一条带出处的（表缺行/引用错 code 必须留痕）
                    badCode++;
                    Log.Warn(Tag, $"起始装备：`start_item_c` id={row.Id}（class={row.Class} slot_index={row.SlotIndex} "
                        + $"src_line={row.SrcLine}）的 code=\"{row.Code}\" 在 `item_c` 里查不到 ⇒ 该件不发");
                    continue;
                }

                var itemRow = Tables.Default.Item.Get(itemId);
                if (itemRow == null)
                {
                    badCode++;
                    Log.Warn(Tag, $"起始装备：`item_c` 取不到 id={itemId}（code=\"{row.Code}\"）的行 ⇒ 该件不发");
                    continue;
                }

                var want = SlotOfLoc(row.Loc, out var knownLoc);
                if (!knownLoc)
                {
                    badLoc++;
                    Log.Warn(Tag, $"起始装备：`start_item_c` id={row.Id} 的 loc=\"{row.Loc}\" 不在已知槽位集"
                        + "（rarm/larm/head/torso/glov/boot/belt/neck/ring，空 = 进背包）⇒ 按**进背包**处理");
                }

                var isEquipRow = want != ItemSlot.None && ItemFactory.IsEquipment(itemRow);
                if (want != ItemSlot.None && !ItemFactory.IsEquipment(itemRow))
                {
                    // 官方 loc 给了位置，但这件在 item_c 里不是装备（例如把药剂写到 rarm）——非预期，留痕并降级
                    Log.Warn(Tag, $"起始装备：`start_item_c` id={row.Id} 指定 loc=\"{row.Loc}\"（= {want}），"
                        + $"但「{itemRow.Name}」(code={itemRow.Code}, source={itemRow.Source}) 不是装备 ⇒ 改为放进背包");
                    isEquipRow = false;
                }

                var stackable = itemRow.Stack != 0;
                var total = row.Count > 0 ? row.Count : 1;

                if (isEquipRow)
                {
                    // 装备：1 件（官方 loc 行 count 恒为 1）；loc 与该物品的**自然槽**不一致时留痕
                    var natural = Equipment.SlotOf(itemRow);
                    if (natural != want)
                    {
                        Log.Warn(Tag, $"起始装备：`start_item_c` id={row.Id} 的 loc=\"{row.Loc}\" 映射到 {want}，"
                            + $"但「{itemRow.Name}」的自然槽是 {natural} ⇒ 按自然槽装备（原版槽位语义见 Equipment.SlotOf）");
                    }

                    var st = factory.Create(itemId, 1, ItemQuality.Normal, rng);
                    if (st == null) { badCode++; continue; }
                    st.count = 1;

                    if (equip.TryEquip(st, out var slot, out var slotIndex, out var replaced))
                    {
                        equipCount++;
                        if (replaced != null)
                        {
                            Log.Warn(Tag, $"起始装备：「{st.name}」装到 {slot}[{slotIndex}]，换下了「{replaced.name}」"
                                + "（原版起始配置不该出现同槽两件 ⇒ 请核对 charstats 的 loc）");
                        }
                    }
                    else
                    {
                        Log.Warn(Tag, $"起始装备：「{st.name}」TryEquip 失败（见上一行原因）⇒ 改为放进背包");
                        if (inv.TryPlace(st, out _)) invCount++; else fullBag++;
                    }
                    continue;
                }

                // 非装备（或 loc 降级）：按 count 拆 —— 可堆叠 = 一叠 count 个；不可堆叠 = 占 count 格
                var copies = stackable ? 1 : total;
                for (var k = 0; k < copies; k++)
                {
                    var st = factory.Create(itemId, 1, ItemQuality.Normal, rng);
                    if (st == null) { badCode++; break; }
                    st.count = stackable ? total : 1;

                    if (inv.TryPlace(st, out _)) invCount++;
                    else
                    {
                        fullBag++;
                        // Inventory.TryPlace 已打 Warn（含尺寸与空格数）；这里只记次数，末尾汇总
                        break;
                    }
                }
            }

            if (noTableRows == 0)
            {
                Log.Warn(Tag, $"起始装备：`start_item_c` 里没有 class={clsId}（{cls}）的任何行 "
                    + $"⇒ 「{who}」不会拿到起始装备（检查打表是否覆盖了该职业）");
            }

            Log.Info(Tag, $"[StartItems] 起始装备已应用：「{who}」职业={cls}(id={clsId}) "
                + $"表行 {noTableRows} 条 ⇒ 装备 {equipCount} 件 / 背包 {invCount} 件"
                + $"（可堆叠按一叠计；不可堆叠按 count 占格）；"
                + $"查不到 code {badCode} 条 / loc 认不出 {badLoc} 条 / 背包放不下 {fullBag} 件。"
                + $"装备槽 = 【{DescribeEquip(equip)}】，背包锚点 {CountAnchors(inv)} 个");
            return equipCount + invCount;
        }

        // ── 日志/自证助手 ───────────────────────────────────────────────────────

        /// <summary>装备清单（日志/自证用；`槽位:名字×数量`）。</summary>
        public static string DescribeEquip(Equipment equip)
        {
            if (equip == null) return "空";
            var items = equip.Items;
            if (items == null || items.Count == 0) return "空";
            var sb = new System.Text.StringBuilder();
            for (var i = 0; i < items.Count; i++)
            {
                var it = items[i];
                if (it == null) continue;
                if (sb.Length > 0) sb.Append(", ");
                sb.Append(Equipment.SlotOf(it)).Append(':').Append(it.name).Append('×').Append(it.count);
            }
            return sb.Length > 0 ? sb.ToString() : "空";
        }

        /// <summary>背包锚点数（= 实际入包的物品件数）。</summary>
        public static int CountAnchors(Inventory inv)
        {
            if (inv == null) return 0;
            var n = 0;
            var slots = inv.Slots;
            for (var i = 0; i < slots.Count; i++)
            {
                if (slots[i] != null && slots[i].isAnchor && slots[i].item != null) n++;
            }
            return n;
        }

        /// <summary>
        /// 起始装备的**期望值**（纯读表，不造物品）：`槽位|code|count|slot序号|出处行号` 列表。
        /// 离线宿主用它做"逐条等于 charstats.txt 期望值"的对照；生产路径不调它。
        /// </summary>
        public static List<string> ExpectedOf(PlayerClass cls)
        {
            var res = new List<string>();
            var table = Tables.Default.StartItem;
            if (table == null) return res;
            var rows = table.All();
            var clsId = (int)cls;
            for (var i = 0; i < rows.Count; i++)
            {
                var r = rows[i];
                if (r == null || r.Class != clsId) continue;
                var slot = SlotOfLoc(r.Loc, out var known);
                res.Add($"{(known ? slot.ToString() : "?loc=" + r.Loc)}|{r.Code}|{r.Count}|slot{r.SlotIndex}|line{r.SrcLine}");
            }
            return res;
        }
    }
}
