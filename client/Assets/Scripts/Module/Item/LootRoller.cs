// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · Module/Item/LootRoller.cs
// 掉落：**按 `treasureclass_c` 递归**（`picks` / `nodrop` / `drops` / `next_tc`）。
//
// 掉落表的事实（见 `client/Assets/Scripts/Table/Tsv/Treasureclass.tsv`）：
//   · 主键 = TC 名（字符串，如 `Act 1 H2H A`）；
//   · `drops` = `token;prob|token;prob`，token 可能是**子 TC 名**（如 `Act 1 Equip A`）、
//     也可能是**物品 code**（如 `hp1` / `gld`）；含逗号时该 token 在 tsv 里被引号包住
//     （例：`"gld,mul=1280";60`）⇒ 解析时必须去掉引号；
//   · `next_tc` 是 `drops` 里指向 TC 的那些 token（本项目按 `drops` 判断即可，`next_tc` 仅审计）。
//
// 已导入的 TC 闭包（59 个）里，**全部 token 可解析**：TC 名 / 物品 code / `gld` / 分层伪码，零落空
//   仍**按设计**走兜底的是分层伪码 `weapN/armoN/bowN/meleN`（官方那批 TC 未导入；处置 ①）。
//   兜底/告警三分支（缺一个就有"静默丢权重"的可能 ⇒ 三个都不能删）：
//     ① `weapN/armoN/bowN/meleN` → 按 `item_c.item_level ≤ N` 从对应池里随机取（**只用配表列**，不发明数值）；
//     ② 未知物品 code → 跳过该条并限频 Warn（不伪造物品）；
//     ③ 未知 TC 名 → 限频 Warn 后退回该等级的 `Act 1 Equip ?` 兜底 TC（保证怪物仍有掉落）。
// ─────────────────────────────────────────────────────────────────────────────

using System.Collections.Generic;
using CloverEngine;
using Diablo2.Core;
using Diablo2.Def;
using Table;
using UnityEngine;

namespace Diablo2.Module.Item
{
    /// <summary>掉落表递归求解器（TC → 子 TC → 物品）。</summary>
    internal sealed class LootRoller
    {
        /// <summary>递归深度上限（防御配表成环 / 病态深链）。</summary>
        private const int MaxDepth = 8;

        /// <summary>兜底 TC 家族名（官方 Act I 装备 TC 的公共前缀）。</summary>
        private const string EquipTcPrefix = "Act 1 Equip ";

        /// <summary>金币掉落基准上限系数（**本项目新增**：原版 gld 金额公式在 TreasureClassEx 之外）。</summary>
        private const int GoldPerLevel = 4;

        /// <summary>`gld,mul=` 的归一化基准（官方 mul 以 256 为一档）。</summary>
        private const int GoldMulBase = 256;

        private readonly ItemFactory _factory;

        // 限频告警：**不用** `Log.WarnThrottled`（它读 `UnityEngine.Time.realtimeSinceStartup`，
        // 离线宿主 `tools/probes/hosts/itemcheck` 会抛 SecurityException）⇒ 本类自带"同 key 只报一次"。
        private static readonly HashSet<string> Warned = new HashSet<string>();
        private const int WarnedCap = 64;

        private static void WarnOnce(string key, string msg)
        {
            if (Warned.Count > WarnedCap) return;
            if (!Warned.Add(key)) return;
            Log.Warn("Item", msg);
        }

        public LootRoller(ItemFactory factory)
        {
            _factory = factory;
        }

        /// <summary>
        /// 按掉落表掉落一批（**金币**也在结果里，`isGold = true`）。
        /// </summary>
        /// <param name="treasureClassId">
        /// **口径已与唯一真实调用方对齐**：= `Tables.Default.Treasureclass.All()` 的 **1 基行序**
        /// （它从 `monster_c.treasure_class` 取 TC 名再转成本口径），见 `DeathFlow.cs:227-243`。
        /// 无效值不会静默：Warn + 按 <paramref name="monsterLevel"/> 退回 `Act 1 Equip ?`（保证怪仍会掉东西）。
        /// </param>
        public List<ItemStack> Roll(int treasureClassId, int monsterLevel, Rng rng)
        {
            var result = new List<ItemStack>();
            if (rng == null)
            {
                Log.Warn("Item", $"LootRoller.Roll 收到 null Rng（treasureClassId={treasureClassId}）⇒ 不掉落");
                return result;
            }

            var level = Mathf.Max(1, monsterLevel);
            var tcName = ResolveTcName(treasureClassId, level);
            if (string.IsNullOrEmpty(tcName))
            {
                Log.Warn("Item", $"LootRoller.Roll：treasureClassId={treasureClassId} 解析不出 TC ⇒ 本次不掉落");
                return result;
            }

            RollTc(tcName, level, rng, 0, result, true, null);   // 掉落物：保留「未鉴定」掷骰
            return result;
        }

        /// <summary>
        /// `treasureClassId`（`Treasureclass.All()` 的 1 基行序）→ TC 名。
        /// 越界/无效 ⇒ 限频 Warn 后返回按等级选的兜底装备 TC（**绝不返回 null 让调用方掉进空引用**）。
        /// </summary>
        public static string ResolveTcName(int treasureClassId, int level)
        {
            var all = Tables.Default.Treasureclass.All();
            if (all != null && treasureClassId >= 1 && treasureClassId <= all.Count)
            {
                var row = all[treasureClassId - 1];
                if (row != null && !string.IsNullOrEmpty(row.Name)) return row.Name;
                WarnOnce("tc.id.nullrow." + treasureClassId,
                    $"LootRoller：treasureClassId={treasureClassId} 指向的行是空的（配表异常）⇒ 退回 {FallbackEquipTc(level)}");
            }
            else
            {
                WarnOnce("tc.id.range." + treasureClassId,
                    $"LootRoller：treasureClassId={treasureClassId} 越界（treasureclass_c 共 {all.Count} 行；"
                    + "口径 = All() 的 1 基行序，与 DeathFlow.TreasureClassIdOf 一致）"
                    + $"⇒ 退回 {FallbackEquipTc(level)}");
            }
            return FallbackEquipTc(level);
        }

        /// <summary>
        /// 按等级随机取一件物品（`IItemModule.CreateRandom` 用）。
        /// <para><paramref name="canBeUnidentified"/> = 是否参与「未鉴定」掷骰：
        /// 商店货架走 <c>false</c>（原版商店卖的永远是已鉴定物品 —— `CreateRandom` 的注释同口径）；
        /// 掉落继续走 <c>true</c>。</para>
        /// </summary>
        public ItemStack RollOne(int level, Rng rng, bool canBeUnidentified = true)
        {
            if (rng == null)
            {
                Log.Warn("Item", "LootRoller.RollOne 收到 null Rng ⇒ 返回 null");
                return null;
            }
            var lv = Mathf.Max(1, level);
            var list = new List<ItemStack>();
            RollTc(FallbackEquipTc(lv), lv, rng, 0, list, canBeUnidentified, null);
            for (var i = 0; i < list.Count; i++)
            {
                if (list[i] != null && !list[i].isGold) return list[i];
            }
            // 兜底 TC 没抽出物品（配表异常）⇒ 直接用候选池造一件，保证调用方拿到东西
            var pool = CollectTierItems("weap", 0, lv);
            if (pool.Count == 0) pool = CollectTierItems("armo", 0, lv);
            if (pool.Count == 0)
            {
                Log.Warn("Item", $"LootRoller.RollOne：等级 {lv} 的候选物品池为空（item_c 未加载？）⇒ 返回 null");
                return null;
            }
            var row = pool[rng.Next(pool.Count)];
            var q = _factory.RollQuality(row, lv, null, rng);
            // ⚠️ `worn` 用**命名参数**：本方法只服务商店（`CreateRandom`）⇒ 货架品**满耐久**
            //   （worn 默认 false = 与 HEAD 行为一致）；`canBeUnidentified` 是第 6 参，
            //   当年按位置补 `true` 曾把 worn 顶成 true（商店品带磨损）—— 名字写出来防再踩。
            return _factory.Create(row.Id, lv, q, rng, canBeUnidentified: canBeUnidentified);
        }

        // ── TC 递归 ────────────────────────────────────────────────────────────

        private void RollTc(string tcName, int level, Rng rng, int depth, List<ItemStack> outList,
            bool canBeUnidentified, BaseTreasureclassRow qualityTc)
        {
            if (depth > MaxDepth)
            {
                Log.Warn("Item", $"LootRoller：TC 递归超过 {MaxDepth} 层（tc={tcName}）⇒ 停止（配表可能成环）");
                return;
            }

            var row = Tables.Default.Treasureclass.Get(tcName);
            //   品质 TC 线程（G 组「虚拟 TC 分层」核心）：原版在**含 unique/set/rare/magic 列的最顶层 TC**
            //   掷一次品质，经子 TC 递归落到的具体物品**沿用该品质判定**（每层不各自掷）。本工程 TC 表里品质列
            //   只出现在怪物顶层 TC（如 `Act 1 Citem A`：800/800/800/1024），子 TC（如 `Act 1 Equip A`）全为 0 ⇒
            //   当前 TC 若自带品质列则接管为活跃品质 TC，否则继承上层传入的 qualityTc。
            var activeQ = (row != null && (row.Unique > 0 || row.Set > 0 || row.Rare > 0 || row.Magic > 0))
                ? row : qualityTc;
            if (row == null)
            {
                // 未知 TC：闭包外的名字（配表导入范围 / 新表引用了未进闭包的 TC）。限频 Warn 后退回按等级的
                // 装备 TC，保证有掉落。当前 59 个 TC 的闭包自洽（不会有此分支），它只兜"将来配表再引新 TC"。
                WarnOnce("tc.miss." + tcName,
                    $"LootRoller：`treasureclass_c` 里没有 TC \"{tcName}\"（配表导入范围）⇒ 退回 {FallbackEquipTc(level)}");
                if (depth == 0)
                {
                    // 只顶层退回（子 TC 缺失时退到顶层兜底 TC 会造成"整批重复掉落"）
                    RollTc(FallbackEquipTc(level), level, rng, depth + 1, outList, canBeUnidentified, qualityTc);
                }
                return;
            }

            //   官方 `Picks` 的含义（出处：官方 `TreasureClassEx.txt` 的 `Picks` 列 + 社区 TC 解析
            //   《暗黑2进阶——TreasureClassEx文件解析》：「Picks：宝藏挑选次数。正数为有放回的挑选，
            //   负数为不放回的挑选。」）：
            //     正数 N ⇒ 有放回抽 N 次（同一条目可被重复选中）；
            //     负数 -N ⇒ **不放回**抽 N 次（选中的条目从候选里移除 ⇒ 同一 TC 的出项互不相同、
            //              条目抽空后剩下的次数不再产出）。
            //   两者的抽取次数都是 |Picks|；差别只在"放回/不放回"。官方 `Act 1 Champ=-2 / Unique=-3 /
            //   Super=-4` 三族正是负值行。
            var withoutReplacement = row.Picks < 0;
            var picks = Mathf.Abs(row.Picks);
            if (picks == 0) picks = 1;

            // 池 = drops 的各项 + （nodrop > 0 时）一个"空"项
            var tokens = new List<string>();
            var weights = new List<int>();
            var hasNoDrop = row.Nodrop > 0;
            if (hasNoDrop)
            {
                tokens.Add(null);
                weights.Add(row.Nodrop);
            }

            var entries = ParseDrops(row.Drops);
            for (var i = 0; i < entries.Count; i++)
            {
                tokens.Add(entries[i].Key);
                weights.Add(entries[i].Value > 0 ? entries[i].Value : 1);
            }

            if (weights.Count == 0)
            {
                WarnOnce("tc.empty." + tcName, $"LootRoller：TC \"{tcName}\" 的 drops 为空 ⇒ 本次无掉落");
                return;
            }

            for (var p = 0; p < picks; p++)
            {
                if (withoutReplacement && weights.Count == 0)
                {
                    // 非预期分支：负 Picks 的候选已被抽空（条目数 < |Picks|）⇒ 剩下的次数不再产出
                    WarnOnce("tc.picks.exhausted." + tcName,
                        $"LootRoller：TC \"{tcName}\" 的 picks={row.Picks}（负值 = 不放回）已把候选抽空 ⇒ " +
                        $"第 {p + 1}/{picks} 次起不再产出（官方该行条目数少于 |picks|）");
                    break;
                }

                var idx = rng.PickWeighted(weights);
                if (idx < 0 || idx >= tokens.Count)
                {
                    WarnOnce("tc.idx", $"LootRoller：TC \"{tcName}\" 权重抽取返回非法下标 {idx} ⇒ 本次跳过");
                    continue;
                }
                var token = tokens[idx];
                var weight = weights[idx];
                if (withoutReplacement)
                {
                    // 不放回：本次选中的槽位（含 NoDrop 槽）从候选移除，后续不再被选中
                    tokens.RemoveAt(idx);
                    weights.RemoveAt(idx);
                }
                if (string.IsNullOrEmpty(token)) continue;          // NoDrop
                //   否则"抽中未导入 token"只能报出 token、报不出是谁的槽位、也报不出丢了多少权重。
                Resolve(row, token, weight, level, rng, depth, outList, canBeUnidentified, activeQ);
            }
        }

        /// <summary>解析 `drops` 单元格：`token;prob|token;prob`，token 允许被引号包裹。</summary>
        public static List<KeyValuePair<string, int>> ParseDrops(string drops)
        {
            var res = new List<KeyValuePair<string, int>>();
            if (string.IsNullOrEmpty(drops)) return res;

            var parts = drops.Split('|');
            for (var i = 0; i < parts.Length; i++)
            {
                var part = parts[i].Trim();
                if (part.Length == 0) continue;

                var kv = part.Split(';');
                var token = kv[0].Trim().Trim('"').Trim();
                var prob = kv.Length > 1 ? ParseInt(kv[1]) : 1;
                if (token.Length == 0) continue;
                res.Add(new KeyValuePair<string, int>(token, prob));
            }
            return res;
        }

        private static int ParseInt(string s)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            if (int.TryParse(s.Trim(), out var v)) return v;
            WarnOnce("tc.prob", $"LootRoller：掉落概率 \"{s}\" 不是整数 ⇒ 按 1 处理");
            return 1;
        }

        /// <summary>
        /// 把一个 token 落成"子 TC 递归"或"具体物品"。
        /// </summary>
        /// <param name="parentRow">该 slot 直属的 TC 行（**只用于日志点名**，见 R4）。</param>
        /// <param name="prob">该 slot 在所属 TC 里的权重（**只用于日志点名**：说明丢了多少权重）。</param>
        /// <param name="qualityTc">活跃品质 TC（含 unique/set/rare/magic 列的最顶层，由 <see cref="RollTc"/> 线程下来）。</param>
        private void Resolve(BaseTreasureclassRow parentRow, string rawToken, int prob, int level, Rng rng, int depth,
            List<ItemStack> outList, bool canBeUnidentified, BaseTreasureclassRow qualityTc)
        {
            var tcName = parentRow != null ? parentRow.Name : string.Empty;   // 仅日志点名（哪条 TC 的槽位）
            var token = rawToken;

            // 金币：`gld` 或 `gld,mul=1280`
            var mul = 0;
            var comma = token.IndexOf(',');
            if (comma >= 0)
            {
                var modifier = token.Substring(comma + 1);
                token = token.Substring(0, comma);
                var eq = modifier.IndexOf('=');
                if (eq >= 0) mul = ParseInt(modifier.Substring(eq + 1));
            }

            if (string.Equals(token, "gld", System.StringComparison.OrdinalIgnoreCase))
            {
                outList.Add(MakeGold(level, mul, rng));
                return;
            }

            // 子 TC？
            if (Tables.Default.Treasureclass.Get(token) != null)
            {
                RollTc(token, level, rng, depth + 1, outList, canBeUnidentified, qualityTc);
                return;
            }

            // 官方分层 TC（weap3 / armo6 / bow9 / mele12 …）：配表未导入 ⇒ 按 item_level ≤ N 兜底
            var tier = ParseTierToken(token, out var family);
            if (tier > 0)
            {
                WarnOnce("tc.tier." + token,
                    $"LootRoller：TC \"{token}\" 不在 treasureclass_c 里（配表导入范围）⇒ 按 item_c.item_level ≤ {tier} 从 {family} 池兜底");
                var pool = CollectTierItems(family, tier, level);
                if (pool.Count == 0)
                {
                    WarnOnce("tc.tier.empty." + token, $"LootRoller：{family} 池在 item_level ≤ {tier} 下为空 ⇒ 本次跳过");
                    return;
                }
                var row = pool[rng.Next(pool.Count)];
                var q = _factory.RollQuality(row, level, qualityTc, rng);
                var st = _factory.Create(row.Id, level, q, rng, worn: true, canBeUnidentified: canBeUnidentified);
                if (st != null) outList.Add(st);
                return;
            }

            // 具体物品 code？
            var itemId = ItemFactory.ItemIdOfCode(token);
            if (itemId > 0)
            {
                var row = Tables.Default.Item.Get(itemId);
                var q = _factory.RollQuality(row, level, qualityTc, rng);
                var st = _factory.Create(itemId, level, q, rng, worn: true, canBeUnidentified: canBeUnidentified);
                if (st != null) outList.Add(st);
                return;
            }

            //   也报出该槽位丢了多少权重（曾实测 TC `Jewelry A` 的 `jew;2|cm3;2|cm2;2|cm1;2` ⇒ 20 份权重丢 8 份 = 40%；
            //   已修，见文件头注）。若本告警再次出现 ⇒ TC 闭包引用了 item_c 未导入的 code，
            //   跑 `convert.py --report` 看「资料片行补导入」清单核对（别放着手动补单行 —— 补导入是按闭包自动的）。
            WarnOnce("tc.token." + tcName + "." + token,
                $"LootRoller：TC \"{tcName}\" 的 token \"{token}\" 既不是 TC 名也不是 item_c.code ⇒ 本次抽取落空"
                + $"（该槽位权重 {prob}）。原因 = TC 闭包引用了 item_c 未导入的物品 code —— "
                + "对照 `tools/table-convert/convert.py` 打表日志的「TC 闭包引用」补导入清单；"
                + "修复前 `jew`/`cm1..3` 就是这样丢掉的 8/20 权重（已收口）");
        }

        /// <summary>金币掉落（**本项目新增折算**：按等级给基数，`mul` 以 256 为一档）。</summary>
        private static ItemStack MakeGold(int level, int mul, Rng rng)
        {
            var lv = Mathf.Max(1, level);
            var amount = rng.Next(1, 1 + lv * GoldPerLevel);
            if (mul > 0) amount = amount * mul / GoldMulBase;
            amount = Mathf.Max(1, amount);

            return new ItemStack
            {
                itemId = 0,                       // 金币不是 item_c 行（拾取时直接进金币、不进背包）
                name = "金币",
                type = ItemType.Misc,
                quality = ItemQuality.Normal,
                count = amount,
                gridW = 1,
                gridH = 1,
                price = 0,
                isGold = true,
            };
        }

        // ── 兜底池 ─────────────────────────────────────────────────────────────

        /// <summary>
        /// `weapN / armoN / bowN / meleN` → (family, N)。非该形态返回 (null, 0)。
        /// </summary>
        public static int ParseTierToken(string token, out string family)
        {
            family = null;
            if (string.IsNullOrEmpty(token)) return 0;

            string[] fams = { "weap", "armo", "bow", "mele" };
            for (var i = 0; i < fams.Length; i++)
            {
                var f = fams[i];
                if (token.Length <= f.Length) continue;
                if (string.Compare(token, 0, f, 0, f.Length, System.StringComparison.OrdinalIgnoreCase) != 0) continue;

                var digits = token.Substring(f.Length);
                if (!int.TryParse(digits, out var n) || n <= 0) continue;
                family = f;
                return n;
            }
            return 0;
        }

        /// <summary>按 `item_c` 的 `source`/`subtype`/`item_level` 取兜底候选池（**只用配表列**）。</summary>
        public static List<BaseItemRow> CollectTierItems(string family, int tier, int level)
        {
            var res = new List<BaseItemRow>();
            var cap = tier > 0 ? tier : Mathf.Max(1, level);
            var all = Tables.Default.Item.All();
            var noIcon = 0;
            for (var i = 0; i < all.Count; i++)
            {
                var r = all[i];
                if (r == null) continue;
                if (r.ItemLevel > cap) continue;
                if (!MatchesFamily(r, family)) continue;
                //   `ItemIconAvailability` 文件头的实测依据）。否则它们一旦被生成，商店/背包/地面
                //   都只能显示暗色占位（商店没商品图标）。
                if (!ItemIconAvailability.HasOriginalIcon(r)) { noIcon++; continue; }
                res.Add(r);
            }

            // 非预期分支：这一档真的剔除掉了东西 ⇒ 留一条限频日志（同 key 只报一次，不刷屏）
            if (noIcon > 0)
            {
                WarnOnce("pool.noicon." + family,
                    $"LootRoller：{family} 池里有 {noIcon} 行属于**资料片职业专属装备**（h2h/orb/pelt/phlm/ashd/head，"
                    + "本批经典版素材里没有它们的原版图标）⇒ 本次已剔除，不出现在掉落与商店里（见 `ItemIconAvailability`）");
            }
            return res;
        }

        private static bool MatchesFamily(BaseItemRow r, string family)
        {
            switch (family)
            {
                case "armo":
                    return r.Source == "armo";
                case "bow":
                    return r.Source == "weap" && (r.Subtype == "bow" || r.Subtype == "xbw");
                case "mele":
                    return r.Source == "weap" && r.Subtype != "bow" && r.Subtype != "xbw" && r.Subtype != "tpot";
                case "weap":
                default:
                    return r.Source == "weap";
            }
        }

        /// <summary>该等级对应的兜底装备 TC（`Act 1 Equip A/B/C` 按 `treasureclass_c.level` 选最高不超过者）。</summary>
        public static string FallbackEquipTc(int level)
        {
            var all = Tables.Default.Treasureclass.All();
            BaseTreasureclassRow best = null;
            for (var i = 0; i < all.Count; i++)
            {
                var r = all[i];
                if (r == null || r.Name == null) continue;
                if (!r.Name.StartsWith(EquipTcPrefix, System.StringComparison.Ordinal)) continue;
                if (r.Level > level) continue;
                if (best == null || r.Level > best.Level) best = r;
            }
            if (best != null) return best.Name;

            WarnOnce("tc.fallback", $"LootRoller：找不到 level ≤ {level} 的 {EquipTcPrefix}? 兜底 TC（配表未加载？）");
            return "Act 1 Equip A";
        }
    }
}
