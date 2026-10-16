// ─────────────────────────────────────────────────────────────────────────────
//   ★ tooltip-text：物品 tooltip 的**文案口径**（审计 #7 / #8 / #9 / #10）——
//     ① 字段行 = 原版中文串表**逐字**（与 `原版资源/d2text/chi_string.txt` 逐字节比对）
//     ② 行集 / 顺序照原版（未鉴定只有「未辨視」；没有"类型 / 任务物品 / （无附加属性）"自造行）
//     ③ 背包悬停**不出价格行**（价格只在商店两页显式传）
//     ④ 商店买入页 tooltip = 完整属性（`ShopEntry.item` 透传）
//
//   为什么单开文件 + 两处接线都要：uicheck 是**白名单**宿主（`EnableDefaultCompileItems=false`）——
//   新检查文件必须同步（a）在本目录 `Uicheck.csproj` 加 `<Compile Include="TooltipTextCheck.cs" />`
//   （b）在 `Program.cs` 的 Run 区调用 `TooltipTextCheck.Run()`。少一处 = 编进去但从不运行 /
//   或直接 CS0103 编译停。
//
//   ⚠️「逐字」是本文件的核心判据，两个**看着像笔误、其实是串表原文**的点各配一条断言盯着：
//   3457「耐久度:」是**半角**冒号、3331「出售價格： 」自带**尾随空格** —— 任何一处 `Trim()`
//   都会把它们洗掉、把判据变松（= 假绿）。所以本文件全程 `Ordinal` 比对、⛔ 绝不 Trim。
// ─────────────────────────────────────────────────────────────────────────────

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Diablo2.Def;
using Diablo2.UI;

namespace Uicheck
{
    /// <summary>物品 tooltip 文案口径（串表逐字 / 行集 / 价格行 / 买入页透传）的离线断言。</summary>
    internal static class TooltipTextCheck
    {
        public static void Run()
        {
            Console.WriteLine("── Ⓕ 物品 tooltip 文案口径（串表逐字 / 行集 / 价格行；审计 #7-#10）──");
            LabelConstantsShape();
            LabelsVerbatimAgainstTable();
            LineSetTruthTable();
            SourceShapeNoSelfAuthored();
            ShopPassthroughWiring();
        }

        // ═════════════════════════════════════════════════════════════════════
        // Ⓕ-1 常量形状：两个"看着像笔误"的串表原文点（防手滑 Trim / 换标点）
        // ═════════════════════════════════════════════════════════════════════
        private static void LabelConstantsShape()
        {
            // 3457「耐久度:」= 3 汉字 + **半角**冒号（串表原文如此；英文表同 id = "Durability:"）
            var dur = ItemTooltip.LabelDurability;
            Program.Check("Ⓕ-1a 3457「耐久度:」末字符是**半角**冒号 U+003A（串表原文；改全角 U+FF1A = 逐字口径破）",
                dur.Length == 4 && dur[3] == ':',
                $"len={dur.Length} 末字符=U+{(dur.Length > 0 ? ((int)dur[dur.Length - 1]).ToString("X4") : "----")}");

            // 3331「出售價格： 」= 全角冒号 + **尾随空格**（串表原文；英文表同 id = "Sell value: "）
            var sell = ItemTooltip.LabelSellPrice;
            Program.Check("Ⓕ-1b 3331「出售價格： 」自带**尾随空格**（串表原文；Trim 掉 = 逐字口径破）",
                sell.EndsWith(" ", StringComparison.Ordinal),
                $"len={sell.Length} 末字符=U+{(sell.Length > 0 ? ((int)sell[sell.Length - 1]).ToString("X4") : "----")}");
        }

        // ═════════════════════════════════════════════════════════════════════
        // Ⓕ-2 串表逐字：11 个 label 常量 ↔ `chi_string.txt` 同 id 原文（逐字节 Ordinal）
        // ═════════════════════════════════════════════════════════════════════
        private static void LabelsVerbatimAgainstTable()
        {
            var table = Path.Combine(Program.ProjectRoot, "原版资源", "d2text", "chi_string.txt");
            if (!File.Exists(table))
            {
                Program._skip++;
                Console.WriteLine("[SKIP] Ⓕ-2 串表逐字比对（" + table + " 不在本机 ⇒ 跳过；恢复 = 把原版"
                    + " d2text/chi_string.txt 放回 原版资源/ —— ⛔ 不内嵌一份副本冒充（副本被改了判据就瞎了））");
                return;
            }

            // 解析口径 = `id<TAB>正文`（实测：LF 行尾 / UTF-8 / 无 BOM / 5392 条）。⛔ 正文**不 Trim**。
            var byId = new Dictionary<int, string>();
            foreach (var raw in File.ReadAllLines(table, Encoding.UTF8))
            {
                if (raw.Length == 0) continue;
                var tab = raw.IndexOf('\t');
                if (tab <= 0) continue;
                if (!int.TryParse(raw.Substring(0, tab), out var id)) continue;
                byId[id] = raw.Substring(tab + 1);
            }

            // 自我防坏：解析器一旦坏了（0 条）会让下面**全体**"找不到 id" ⇒ 先靠这条炸出来，
            //   而不是把每条都红成一堆（更难读）或静默跳过（更坏）。
            Program.Check("Ⓕ-2p 串表解析出足够条目（防「解析器坏 ⇒ 全体找不到 ⇒ 假绿/假红」）",
                byId.Count >= 1000, Path.GetFileName(table) + " 条目=" + byId.Count);

            Pair(byId, 3455, ItemTooltip.LabelUnidentified, "未辨視");
            Pair(byId, 3457, ItemTooltip.LabelDurability, "耐久度:");
            Pair(byId, 3458, ItemTooltip.LabelStrReq, "須要的力量點數：");
            Pair(byId, 3461, ItemTooltip.LabelDefense, "防禦：");
            Pair(byId, 3465, ItemTooltip.LabelOneHandDamage, "單手傷害：");
            Pair(byId, 3466, ItemTooltip.LabelTwoHandDamage, "雙手傷害：");
            Pair(byId, 3469, ItemTooltip.LabelLevelReq, "須要等級：");
            Pair(byId, 3463, ItemTooltip.RangeOf, "之（of）");
            Pair(byId, 3464, ItemTooltip.RangeTo, "到（to）");
            Pair(byId, 3329, ItemTooltip.LabelCost, "花費：");
            Pair(byId, 3331, ItemTooltip.LabelSellPrice, "出售價格： ");

            // 已知错误样本必须红（退化断言；样本 = 修复前代码里真实存在过的写法）
            Program.Check("Ⓕ-2 退化：修复前自造句「伤害:」对串表「傷害：」⇒ 同一判据必须红",
                !Verbatim("傷害：", "伤害:"), "退化样本 Verbatim=False（必须 False）");
            Program.Check("Ⓕ-2 退化：「出售價格：」（不带尾随空格）对串表「出售價格： 」⇒ 同一判据必须红",
                !Verbatim("出售價格： ", "出售價格："), "退化样本 Verbatim=False（必须 False）");
        }

        /// <summary>逐字节比对（Ordinal；⛔ 无 Trim —— 见文件头「不 Trim 是故意的」）。</summary>
        internal static bool Verbatim(string fromTable, string constValue)
            => string.Equals(fromTable, constValue, StringComparison.Ordinal);

        private static void Pair(Dictionary<int, string> byId, int id, string actual, string what)
        {
            if (!byId.TryGetValue(id, out var fromTable))
            {
                Program.Check($"Ⓕ-2 串 id {id}「{what}」在串表里能找到", false,
                    "串表缺该 id（文件被换过 / 解析口径变了？）");
                return;
            }
            Program.Check($"Ⓕ-2 串 id {id}「{what}」逐字相等（含标点与尾随空格）",
                Verbatim(fromTable, actual), $"串表=「{Vis(fromTable)}」 / 常量=「{Vis(actual)}」");
        }

        /// <summary>把空格 / 制表符画成可见形式（判据详情里"看不出空格"是经典的假绿来源）。</summary>
        private static string Vis(string s)
            => (s ?? "<null>").Replace(" ", "<SP>").Replace("\t", "<TAB>");

        // ═════════════════════════════════════════════════════════════════════
        // Ⓕ-3 行集真值表（`BuildLines` 纯函数；样本 = 手搭 `ItemStack`，不经模块）
        // ═════════════════════════════════════════════════════════════════════
        private static void LineSetTruthTable()
        {
            // 纯函数范围文本（先钉最底层，行集断言全建立在它上面）
            Program.Check("Ⓕ-3z `RangeText`：a==b ⇒ 单值「8」；a!=b ⇒「4 到 8」（token = 3464「到」）",
                ItemTooltip.RangeText(8, 8) == "8" && ItemTooltip.RangeText(4, 8) == "4 到 8",
                $"8,8 ⇒「{ItemTooltip.RangeText(8, 8)}」 4,8 ⇒「{ItemTooltip.RangeText(4, 8)}」");

            var oneHand = new ItemStack
            {
                itemId = 1, name = "短劍", baseName = "短劍", type = ItemType.Weapon, quality = ItemQuality.Normal,
                dmgMin = 4, dmgMax = 8, maxDurability = 24, durability = 24, strReq = 15, lvlReq = 3,
                affixes = new List<ItemAffix>
                {
                    new ItemAffix { kind = AffixKind.Prefix, name = "利刃", mod = "dmg", min = 1, max = 3, value = 2 },
                },
            };
            var wl = ItemTooltip.BuildLines(oneHand, null);
            Program.Check("Ⓕ-3a 单手剑（背包悬停 priceLine=null）：單手傷害 +「耐久度: a 之 b」+ 需求两行 + 词缀行，"
                + "且**无**价格行（审计 #7）",
                Same(wl, new[] { "單手傷害：4 到 8", "耐久度:24 之 24", "須要的力量點數：15", "須要等級：3", "利刃 +2" }),
                Join(wl));

            var buyLa = ItemTooltip.BuildLines(oneHand, ItemTooltip.LabelCost + 100);
            Program.Check("Ⓕ-3b 商店买入行（priceLine = LabelCost + 标价）追加为**最后一行**「花費：100」",
                Same(buyLa, new[] { "單手傷害：4 到 8", "耐久度:24 之 24", "須要的力量點數：15", "須要等級：3", "利刃 +2", "花費：100" }),
                Join(buyLa));

            var twoHand = new ItemStack
            {
                itemId = 2, name = "巨斧", type = ItemType.Weapon, quality = ItemQuality.Normal,
                dmgMin = 7, dmgMax = 14, dmg2Min = 13, dmg2Max = 21, twoHanded = true,
            };
            Program.Check("Ⓕ-3c 仅双手武器（2handed）：**只出**「雙手傷害：13 到 21」（无单手工行）",
                Same(ItemTooltip.BuildLines(twoHand, null), new[] { "雙手傷害：13 到 21" }),
                Join(ItemTooltip.BuildLines(twoHand, null)));

            var oneOrTwo = new ItemStack
            {
                itemId = 3, name = "雙刃劍", type = ItemType.Weapon, quality = ItemQuality.Normal,
                dmgMin = 7, dmgMax = 14, dmg2Min = 13, dmg2Max = 21, oneOrTwo = true,
            };
            Program.Check("Ⓕ-3d 可单可双武器（1or2handed）：**两行都出**，顺序 = 單手在前 / 雙手在后",
                Same(ItemTooltip.BuildLines(oneOrTwo, null), new[] { "單手傷害：7 到 14", "雙手傷害：13 到 21" }),
                Join(ItemTooltip.BuildLines(oneOrTwo, null)));

            var fallbackTwo = new ItemStack
            {
                itemId = 4, name = "斷裂巨斧", type = ItemType.Weapon, quality = ItemQuality.Normal,
                dmgMin = 7, dmgMax = 14, twoHanded = true,          // dmg2_* = 0（旧档缺字段 / 配表缺口）
            };
            Program.Check("Ⓕ-3e 仅双手但 dmg2_* 缺（旧档默认 0）⇒ 双手行**回退用单手数值**（向后兼容口径）",
                Same(ItemTooltip.BuildLines(fallbackTwo, null), new[] { "雙手傷害：7 到 14" }),
                Join(ItemTooltip.BuildLines(fallbackTwo, null)));

            var armor = new ItemStack
            {
                itemId = 5, name = "皮甲", type = ItemType.Armor, quality = ItemQuality.Normal,
                defMin = 8, defMax = 8, maxDurability = 30, durability = 30, strReq = 21,
            };
            Program.Check("Ⓕ-3f 防具（defMin==defMax）：「防禦：8」单值（不写 8 到 8），且无伤害行",
                Same(ItemTooltip.BuildLines(armor, null), new[] { "防禦：8", "耐久度:30 之 30", "須要的力量點數：21" }),
                Join(ItemTooltip.BuildLines(armor, null)));

            var singleDmg = new ItemStack
            {
                itemId = 6, name = "闊斧", type = ItemType.Weapon, quality = ItemQuality.Normal,
                dmgMin = 5, dmgMax = 5,                              // 单值伤害
            };
            Program.Check("Ⓕ-3g 单值伤害（dmgMin==dmgMax）：「單手傷害：5」（不写 5 到 5）",
                Same(ItemTooltip.BuildLines(singleDmg, null), new[] { "單手傷害：5" }),
                Join(ItemTooltip.BuildLines(singleDmg, null)));

            var magic = new ItemStack
            {
                itemId = 7, name = "利刃 短劍 之 力量", baseName = "短劍",
                type = ItemType.Weapon, quality = ItemQuality.Magic, unidentified = true,
                dmgMin = 4, dmgMax = 8, maxDurability = 24, durability = 24, strReq = 15, lvlReq = 3,
                affixes = new List<ItemAffix>
                {
                    new ItemAffix { kind = AffixKind.Prefix, name = "利刃", mod = "dmg", min = 1, max = 3, value = 2 },
                    new ItemAffix { kind = AffixKind.Suffix, name = "力量", mod = "str", min = 10, max = 10, value = 10 },
                },
            };
            var ml = ItemTooltip.BuildLines(magic, null);
            Program.Check("Ⓕ-3h 未鉴定：正文**只有一行**「未辨視」（属性 / 词缀全部不列 —— 原版口径）；"
                + "样本自带 2 条词缀 + 伤害 / 耐久 / 需求字段（否则这条断言没意义）",
                magic.affixes.Count == 2 && Same(ml, new[] { "未辨視" }), Join(ml));

            // 退化：同一件样本改成已鉴定 ⇒ 同一判据必须变（证明"未鉴定分支"受数据驱动、不是写死的常量）
            magic.unidentified = false;
            var ml2 = ItemTooltip.BuildLines(magic, null);
            Program.Check("Ⓕ-3h 退化：同一件样本置为已鉴定 ⇒ 行数必须 > 1 且不再含「未辨視」",
                ml2.Count > 1 && !ml2.Contains("未辨視"), $"已鉴定行数={ml2.Count} / {Join(ml2)}");

            var potion = new ItemStack { itemId = 8, name = "體力藥水", type = ItemType.Misc, quality = ItemQuality.Normal };
            var pl = ItemTooltip.BuildLines(potion, null);
            Program.Check("Ⓕ-3i 无属性物品（药水）：行集 = **空**（不再有「（无附加属性）」兜底自造句 —— 审计 #8）",
                pl.Count == 0, "行数=" + pl.Count);

            var quest = new ItemStack
            {
                itemId = 9, name = "神聖之血", type = ItemType.Misc, quality = ItemQuality.Normal, isQuestItem = true,
            };
            var ql = ItemTooltip.BuildLines(quest, null);
            Program.Check("Ⓕ-3j 任务物品：行集 = **空**（不再有「任务物品（不可丢弃 / 不可出售）」自造句 —— 审计 #8）",
                ql.Count == 0, "行数=" + ql.Count);
        }

        private static bool Same(List<string> got, string[] want)
        {
            if (got == null || got.Count != want.Length) return false;
            for (var i = 0; i < want.Length; i++)
            {
                if (!string.Equals(got[i], want[i], StringComparison.Ordinal)) return false;
            }
            return true;
        }

        private static string Join(List<string> lines)
            => lines == null || lines.Count == 0 ? "（空）" : string.Join(" / ", lines.ToArray());

        // ═════════════════════════════════════════════════════════════════════
        // Ⓕ-4 源码形状：零处自造简体字段行（审计 #8 的原始词表）+ 价格前缀不许裸写字面量
        // ═════════════════════════════════════════════════════════════════════
        private static void SourceShapeNoSelfAuthored()
        {
            // 审计 #8 逐字原词表（= 修复前代码里真实存在过的自造句；改这条词表 = 改判据，慎动）
            var banned = new[]
            {
                "伤害:", "防御:", "耐久:", "需要力量", "需要等级:", "售价:", "花费:",
                "武器（双手）", "武器（单手）", "防具（身体）", "（无附加属性）",
                "任务物品（不可丢弃", "未鉴定（使用鉴定卷轴",
            };

            var tip = NoComments(ReadUi("ItemTooltip.cs"));
            var hits = 0;
            var firstHit = "";
            foreach (var b in banned)
            {
                var n = CountOf(tip, b);
                if (n > 0 && firstHit.Length == 0) firstHit = b;
                hits += n;
            }
            Program.Check("Ⓕ-4a `ItemTooltip` 代码（去注释）里**零处**自造简体字段行 / 三种自造行",
                hits == 0, hits == 0
                    ? $"0 命中（词表 {banned.Length} 条）"
                    : $"命中 {hits} 处，首例「{firstHit}」（词表 {banned.Length} 条）");

            var shop = NoComments(ReadUi("ShopPanel.cs"));
            var sellHits = CountOf(shop, "\"售价:");
            var costHits = CountOf(shop, "\"花费:");
            Program.Check("Ⓕ-4b `ShopPanel` 代码（去注释）里零处 `\"售价:` / `\"花费:` 裸字面量"
                + "（价格前缀只能来自 `ItemTooltip` 常量）",
                sellHits + costHits == 0, $"售价:={sellHits} 花费:={costHits}");

            Program.Check("Ⓕ-4c 卖出页价格行真的接成 `ItemTooltip.LabelSellPrice + GameConst.SellPriceOf(...)`",
                shop.Contains("ItemTooltip.LabelSellPrice + GameConst.SellPriceOf("), "见 ShopPanel.UpdateHover");

            Program.Check("Ⓕ-4d 买入页价格行由 `ShowStock` 用 `LabelCost + entry.price` 构造 + 有 item 时走 `BuildLines`",
                tip.Contains("LabelCost + entry.price") && tip.Contains("BuildLines(item, priceLine)"),
                "见 ItemTooltip.ShowStock");

            // 退化：把旧写法喂进同一词表 ⇒ 命中必须 > 0（判据不是恒 0 的哑弹）
            const string degraded = "lines.Add($\"售价: 100\");\nlines.Add($\"伤害: 1-2\");";
            var degHits = 0;
            foreach (var b in banned) degHits += CountOf(degraded, b);
            Program.Check("Ⓕ-4 退化：旧写法样本（`售价:` / `伤害:`）喂进同一词表 ⇒ 命中必须 > 0",
                degHits > 0, "退化样本命中=" + degHits);
        }

        // ═════════════════════════════════════════════════════════════════════
        // Ⓕ-5 契约形状 + 透传接线（审计 #9 的数据源）
        // ═════════════════════════════════════════════════════════════════════
        private static void ShopPassthroughWiring()
        {
            var shopEntryItem = typeof(ShopEntry).GetField("item");
            Program.Check("Ⓕ-5a 契约 `ShopEntry.item` 存在且类型 = `ItemStack`（买入页 tooltip 的完整属性数据源）",
                shopEntryItem != null && shopEntryItem.FieldType == typeof(ItemStack),
                shopEntryItem == null ? "无该字段" : "Type=" + shopEntryItem.FieldType.Name);

            var ist = typeof(ItemStack);
            Program.Check("Ⓕ-5b `ItemStack` 的 4 个加性字段齐（dmg2Min / dmg2Max: `int`；twoHanded / oneOrTwo: `bool`）"
                + " —— 旧档缺字段 = 默认 0/false（向后兼容）",
                ist.GetField("dmg2Min")?.FieldType == typeof(int)
                && ist.GetField("dmg2Max")?.FieldType == typeof(int)
                && ist.GetField("twoHanded")?.FieldType == typeof(bool)
                && ist.GetField("oneOrTwo")?.FieldType == typeof(bool),
                "4/4");

            var nshop = NoComments(File.ReadAllText(Path.Combine(
                Program.ProjectRoot, "client", "Assets", "Scripts", "Module", "Npc", "NpcShop.cs"), Encoding.UTF8));
            Program.Check("Ⓕ-5c `NpcShop.AddEntry` 把完整 `ItemStack` 写进 `ShopEntry.item`（`item = st,`）"
                + " —— 模块侧行为断言在 itemcheck §10（本宿主不链 Npc 模块，只钉接线形状）",
                Regex.IsMatch(nshop, @"item\s*=\s*st\s*,"), "见 NpcShop.AddEntry");

            // 退化：非 st 的右值 ⇒ 同一正则必须不命中
            Program.Check("Ⓕ-5 退化：`item = other,`（不是 st）⇒ 同一正则必须不命中",
                !Regex.IsMatch("item = other,", @"item\s*=\s*st\s*,"), "退化样本=False（必须 False）");
        }

        // ── 本文件自带的三个小工具（与 DialogOptionsCheck 的同名做法一致：白名单宿主里
        //    各检查文件自带工具，不共享静态类 ⇒ 单飞文件可整份删除不影响别人）──────────────
        /// <summary>剥注释（保留字符串字面量；`//` 到行尾、`/* */` 成对）——源码形状判据必须先剥，
        /// 否则注释里提到词表（如「已删「（无附加属性）」」的说明）会被误判成命中。</summary>
        private static string NoComments(string src)
        {
            var sb = new StringBuilder(src.Length);
            var inStr = false;
            for (var i = 0; i < src.Length; i++)
            {
                var c = src[i];
                if (inStr)
                {
                    sb.Append(c);
                    if (c == '\\' && i + 1 < src.Length) { sb.Append(src[i + 1]); i++; continue; }
                    if (c == '"') inStr = false;
                    continue;
                }
                if (c == '"') { inStr = true; sb.Append(c); continue; }
                if (c == '/' && i + 1 < src.Length && src[i + 1] == '/')
                {
                    while (i < src.Length && src[i] != '\n') i++;
                    sb.Append('\n');
                    continue;
                }
                if (c == '/' && i + 1 < src.Length && src[i + 1] == '*')
                {
                    i += 2;
                    while (i + 1 < src.Length && !(src[i] == '*' && src[i + 1] == '/')) i++;
                    i++;
                    continue;
                }
                sb.Append(c);
            }
            return sb.ToString();
        }

        private static string ReadUi(string file)
            => File.ReadAllText(Path.Combine(Program.UiDir, file), Encoding.UTF8);

        private static int CountOf(string text, string needle)
        {
            var n = 0;
            var i = 0;
            while (true)
            {
                var at = text.IndexOf(needle, i, StringComparison.Ordinal);
                if (at < 0) return n;
                n++;
                i = at + needle.Length;
            }
        }
    }
}
