// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · Module/Item/ItemFactory.cs
// 物品生成：**品质判定 + 词缀组合 + 名字拼装**。
//
// 数据来源（**一律取配表，不硬编码数值**）：
//   · 物品尺寸/等级需求/力量需求/伤害/防御/价格/可堆叠 → `item_c`（`Tables.Default.Item`）
//   · 词缀等级 / 可穿物品类型 / 排除类型 / 组互斥 / 数值范围 → `affix_c`（`Tables.Default.Affix`）
//   · 品质权重基准（unique/set/rare/magic 成色系数）→ `treasureclass_c` 行的同名四列
//
//   ① 品质权重**上限系数**（见 RollQuality 常量表）—— 配表无品质列，按 TC 成色系数×等级系数折算；
//   ② 耐久上限 —— `item_c` 无耐久列，按 `price` 分档（见 MaxDurabilityOf）。
//   两处都是"算法参数"，不是同质化配置；要调数值只改这里。
// ─────────────────────────────────────────────────────────────────────────────

using System.Collections.Generic;
using CloverEngine;
using Diablo2.Core;
using Diablo2.Def;
using Table;
using UnityEngine;

namespace Diablo2.Module.Item
{
    /// <summary>物品生成器（品质判定 / 词缀组合 / 名字拼装 / 耐久档位）。</summary>
    internal sealed class ItemFactory
    {
        // ── 品质权重（本项目新增；配表无品质列）────────────────────────────────
        // 基准权重：普通 1000；魔法 = 上限 240 × (TC.magic / 1024)，TC.magic 缺省时按 1024 处理；
        // 稀有/套装/暗金 = 魔法权重 × 比例 × 等级系数 hi（hi = clamp((itemLevel-1)/12, 0, 1)）。
        // 实测分布（1000 次，见 tools/probes/hosts/itemcheck）：普通 ≈ 74%、魔法 ≈ 18%、稀有 ≈ 4%、套装 ≈ 3%、暗金 ≈ 1%。
        private const int WNormal = 1000;

        /// <summary>魔法品质权重上限（TC.magic == 1024 时取本值）。</summary>
        private const int WMagicMax = 240;

        /// <summary>稀有 = 魔法权重 × 本比例 × 等级系数。</summary>
        private const float RareOfMagic = 0.25f;

        /// <summary>套装 = 魔法权重 × 本比例 × 等级系数。</summary>
        private const float SetOfMagic = 0.15f;

        /// <summary>暗金 = 魔法权重 × 本比例 × 等级系数。</summary>
        private const float UniqueOfMagic = 0.10f;

        /// <summary>等级系数达到 1 所需的物品等级（= 0.12 的倒数取整语义：itemLevel-1 除以本值）。</summary>
        private const float LevelFactorSpan = 12f;

        /// <summary>`treasureclass_c` 成色系数为 0 时使用的缺省值（官方 TreasureClassEx 最常见的一组）。</summary>
        private const int DefaultUniqueFactor = 800;
        private const int DefaultSetFactor = 800;
        private const int DefaultRareFactor = 972;
        private const int DefaultMagicFactor = 1024;

        /// <summary>成色系数的归一化基准（官方 magic 默认 1024）。</summary>
        private const int FactorBase = 1024;

        // ── 限频告警 ──────────────────────────────────────────────────────────
        // 这里**不用** `Log.WarnThrottled`：它内部读 `UnityEngine.Time.realtimeSinceStartup`（原生 API），
        //    在 `tools/probes/hosts/itemcheck` 这种离线宿主里会抛 SecurityException ⇒ 掉落/生成这类核心路径没法离线自检。
        //    改用本类自带的"同 key 只报一次"（同样的防刷屏目的，且不依赖 Unity 原生）。
        private static readonly HashSet<string> Warned = new HashSet<string>();
        private const int WarnedCap = 64;

        private static void WarnOnce(string key, string msg)
        {
            if (Warned.Count > WarnedCap) return;
            if (!Warned.Add(key)) return;
            Log.Warn("Item", msg);
        }

        // ── 生成 ───────────────────────────────────────────────────────────────

        /// <summary>
        /// 生成一件物品实例。失败（配表无该 id）返回 null 并打日志。
        /// </summary>
        /// <param name="itemId">`item_c` 主键。</param>
        /// <param name="itemLevel">用来筛选词缀与判品质的等级。</param>
        /// <param name="quality">品质（普通物品传 <see cref="ItemQuality.Normal"/>）。</param>
        /// <param name="rng">注入式随机器。</param>
        /// <param name="worn">true = 带磨损（掉落物用，耐久不满，便于修理系统有意义）。</param>
        public ItemStack Create(int itemId, int itemLevel, ItemQuality quality, Rng rng, bool worn = false)
        {
            if (rng == null)
            {
                Log.Warn("Item", $"ItemFactory.Create 收到 null Rng（itemId={itemId}）⇒ 拒绝生成（随机必须可复现）");
                return null;
            }

            var row = Tables.Default.Item.Get(itemId);
            if (row == null)
            {
                Log.Warn("Item", $"ItemFactory.Create：`item_c` 里没有 id={itemId} 的行（配表未加载或引用错 id）⇒ 生成失败");
                return null;
            }

            var equip = IsEquipment(row);
            var st = new ItemStack
            {
                itemId = itemId,
                name = string.IsNullOrEmpty(row.Name) ? row.Code : row.Name,
                type = TypeOf(row),
                quality = quality,
                count = 1,
                gridW = row.GridW > 0 ? row.GridW : 1,
                gridH = row.GridH > 0 ? row.GridH : 1,
                lvlReq = row.LvlReq,
                strReq = row.StrReq,
                dmgMin = row.DmgMin,
                dmgMax = row.DmgMax,
                defMin = row.DefMin,
                defMax = row.DefMax,
                price = row.Price,
                isGold = false,
                isQuestItem = row.Quest != 0,
            };

            // 耐久：只有装备有（配表无耐久列 ⇒ 按 price 分档，见 MaxDurabilityOf）
            st.maxDurability = MaxDurabilityOf(row);
            if (st.maxDurability > 0)
            {
                st.durability = worn
                    ? rng.Next(st.maxDurability * 40 / 100, st.maxDurability + 1)
                    : st.maxDurability;
            }

            // 词缀：只有装备能带，且品质非普通
            if (equip && quality != ItemQuality.Normal) AddAffixes(st, row, itemLevel, quality, rng);

            return st;
        }

        /// <summary>品质判定（TC 成色系数 × 物品等级；非装备一律普通，与原版一致）。</summary>
        public ItemQuality RollQuality(BaseItemRow row, int itemLevel, BaseTreasureclassRow tc, Rng rng)
        {
            if (rng == null) return ItemQuality.Normal;
            if (row == null || !IsEquipment(row)) return ItemQuality.Normal;

            int fu = DefaultUniqueFactor, fs = DefaultSetFactor, fr = DefaultRareFactor, fm = DefaultMagicFactor;
            if (tc != null && (tc.Unique > 0 || tc.Set > 0 || tc.Rare > 0 || tc.Magic > 0))
            {
                fu = tc.Unique > 0 ? tc.Unique : 0;
                fs = tc.Set > 0 ? tc.Set : 0;
                fr = tc.Rare > 0 ? tc.Rare : 0;
                fm = tc.Magic > 0 ? tc.Magic : DefaultMagicFactor;
            }

            var hi = Mathf.Clamp01((itemLevel - 1) / LevelFactorSpan);

            var wMagic = Mathf.Max(0, Mathf.RoundToInt(WMagicMax * (fm / (float)FactorBase)));
            var wRare = Mathf.RoundToInt(wMagic * RareOfMagic * hi);
            var wSet = Mathf.RoundToInt(wMagic * SetOfMagic * hi);
            var wUnique = Mathf.RoundToInt(wMagic * UniqueOfMagic * hi);
            // 成色系数本身的差别（unique/set/rare 列）再微调一次，让"高成色 TC"真的更容易出好东西
            wRare = wRare + Mathf.RoundToInt(wRare * (fr / (float)FactorBase));
            wSet = wSet + Mathf.RoundToInt(wSet * (fs / (float)FactorBase));
            wUnique = wUnique + Mathf.RoundToInt(wUnique * (fu / (float)FactorBase));

            var weights = new List<int> { WNormal, wMagic, wRare, wSet, wUnique };
            var idx = rng.PickWeighted(weights);
            switch (idx)
            {
                case 1: return ItemQuality.Magic;
                case 2: return ItemQuality.Rare;
                case 3: return ItemQuality.Set;
                case 4: return ItemQuality.Unique;
                case 0: return ItemQuality.Normal;
                default:
                    WarnOnce("quality.idx", $"RollQuality 得到非法权重下标 {idx} ⇒ 按普通处理");
                    return ItemQuality.Normal;
            }
        }

        // ── 词缀 ───────────────────────────────────────────────────────────────

        /// <summary>
        /// 按品质给物品挂词缀：魔法 1~2 个、稀有 2~4 个、套装/暗金 2 个。
        /// 候选必须满足：`affix_c.lvl ≤ itemLevel`、`item_types` 与本物品匹配、不与已选词缀同组（`group`）。
        /// </summary>
        public void AddAffixes(ItemStack stack, BaseItemRow row, int itemLevel, ItemQuality quality, Rng rng)
        {
            if (stack == null || row == null || rng == null) return;

            var pre = 0;
            var suf = 0;
            switch (quality)
            {
                case ItemQuality.Magic:
                    pre = 1;
                    suf = rng.Chance(0.5f) ? 1 : 0;      // 1~2 个
                    break;
                case ItemQuality.Rare:
                    pre = rng.Next(1, 3);                 // 1~2
                    suf = rng.Next(1, 3);                 // 1~2（合计 2~4）
                    break;
                case ItemQuality.Set:
                case ItemQuality.Unique:
                    pre = 1;
                    suf = 1;
                    break;
                default:
                    return;                                // 普通物品无词缀
            }

            var used = new List<int>();
            for (var i = 0; i < pre; i++) TryAddAffix(stack, row, itemLevel, AffixKind.Prefix, rng, used);
            for (var i = 0; i < suf; i++) TryAddAffix(stack, row, itemLevel, AffixKind.Suffix, rng, used);

            RebuildName(stack, row);
        }

        private void TryAddAffix(ItemStack stack, BaseItemRow row, int itemLevel, AffixKind kind, Rng rng,
            List<int> usedGroups)
        {
            var all = Tables.Default.Affix.All();
            var cands = new List<BaseAffixRow>();
            var weights = new List<int>();
            for (var i = 0; i < all.Count; i++)
            {
                var a = all[i];
                if (a == null) continue;
                if (KindOf(a) != kind) continue;
                if (a.Lvl > itemLevel) continue;                       // 词缀等级 ≤ 物品等级
                if (a.Group > 0 && usedGroups.Contains(a.Group)) continue;
                if (!MatchesAffix(row, a)) continue;
                cands.Add(a);
                // `freq` 是官方出现频率（0~9）。0 = 官方不给随机出现机会，
                // 但本项目**不剔除**（否则低等级物品可能一个候选都没有）⇒ 计权重 1 并打一次日志。
                weights.Add(a.Freq > 0 ? a.Freq : 1);
            }

            if (cands.Count == 0)
            {
                WarnOnce("affix.empty." + kind + "." + row.Type,
                    $"AddAffixes：{row.Name}（type={row.Type}/source={row.Source}）在 itemLevel={itemLevel} 下找不到"
                    + $"{(kind == AffixKind.Prefix ? "前缀" : "后缀")}候选（affix_c 的 lvl/item_types 过滤后为空）");
                return;
            }

            var idx = rng.PickWeighted(weights);
            if (idx < 0 || idx >= cands.Count)
            {
                WarnOnce("affix.idx", $"AddAffixes：PickWeighted 返回非法下标 {idx} ⇒ 本次不加词缀");
                return;
            }

            var pick = cands[idx];
            var lo = Mathf.Min(pick.Min, pick.Max);
            var hi = Mathf.Max(pick.Min, pick.Max);
            var value = lo == hi ? lo : rng.Next(lo, hi + 1);

            if (pick.Group > 0) usedGroups.Add(pick.Group);
            stack.affixes.Add(new ItemAffix
            {
                affixId = pick.Id,
                kind = kind,
                name = string.IsNullOrEmpty(pick.NameCn) ? pick.Name : pick.NameCn,
                mod = pick.Mod,
                min = lo,
                max = hi,
                value = value,
            });
        }

        /// <summary>
        /// 拼显示名：`前缀名… 基础名 之 后缀名…`（无词缀时就是配表 `item_c.name`）。
        /// </summary>
        public static void RebuildName(ItemStack stack, BaseItemRow row)
        {
            if (stack == null) return;
            var baseName = row != null && !string.IsNullOrEmpty(row.Name) ? row.Name
                : (row != null ? row.Code : stack.name);

            var pre = new List<string>();
            var suf = new List<string>();
            for (var i = 0; i < stack.affixes.Count; i++)
            {
                var a = stack.affixes[i];
                if (a == null || string.IsNullOrEmpty(a.name)) continue;
                if (a.kind == AffixKind.Prefix) pre.Add(a.name);
                else suf.Add(a.name);
            }

            if (pre.Count == 0 && suf.Count == 0)
            {
                stack.name = baseName;
                return;
            }

            var sb = new System.Text.StringBuilder();
            for (var i = 0; i < pre.Count; i++) { sb.Append(pre[i]); sb.Append(' '); }
            sb.Append(baseName);
            for (var i = 0; i < suf.Count; i++)
            {
                sb.Append(i == 0 ? " 之 " : " ");
                sb.Append(suf[i]);
            }
            stack.name = sb.ToString();
        }

        // ── 纯查询（装备/槽位/耐久/类型）────────────────────────────────────────

        /// <summary>是否装备（`item_c.source` = weap/armo）。</summary>
        public static bool IsEquipment(BaseItemRow row)
        {
            if (row == null) return false;
            return row.Source == "weap" || row.Source == "armo";
        }

        /// <summary>
        /// 配表大类 → <see cref="ItemType"/>。
        /// 列语义（以真实打表产物为准）：`item_c.source` = **weap / armo / misc**（大类），
        /// `item_c.type` = 官方 `type`（axe / swor / hpot / helm / ring …），`item_c.subtype` = 官方 `type2`
        /// （1hs / stf / bow / xbw / tpot …，护具与药剂**为空**）⇒ 大类只看 `source`。
        /// </summary>
        public static ItemType TypeOf(BaseItemRow row)
        {
            if (row == null) return ItemType.Misc;
            if (row.Source == "weap") return ItemType.Weapon;
            if (row.Source == "armo") return ItemType.Armor;
            return ItemType.Misc;
        }

        /// <summary>
        /// 耐久上限（**本项目新增**：`item_c` 无耐久列 ⇒ 按 `price` 分档 24~120；非装备 = 0 = 不适用）。
        /// </summary>
        public static int MaxDurabilityOf(BaseItemRow row)
        {
            if (!IsEquipment(row)) return 0;
            var v = 24 + row.Price / 40;
            return Mathf.Clamp(v, 24, 120);
        }

        /// <summary>词缀种类（`affix_c.kind`：pre / suf；其它值按后缀处理并限频告警）。</summary>
        public static AffixKind KindOf(BaseAffixRow a)
        {
            if (a == null) return AffixKind.Suffix;
            if (a.Kind == "pre") return AffixKind.Prefix;
            if (a.Kind == "suf") return AffixKind.Suffix;
            WarnOnce("affix.kind", $"affix_c id={a.Id} 的 kind=\"{a.Kind}\" 不是 pre/suf ⇒ 按后缀处理");
            return AffixKind.Suffix;
        }

        /// <summary>
        /// 词缀是否适用于该物品：先看 `etypes` 排除，再看 `item_types` 命中
        /// （命中口径：官方类型码 与 `item_c.type` / `subtype` / `source` 任一相等）。
        /// 职业专属词缀（`class_only != 0`）在物品生成阶段不知道职业 ⇒ 一律不选。
        /// </summary>
        public static bool MatchesAffix(BaseItemRow row, BaseAffixRow a)
        {
            if (row == null || a == null) return false;
            if (a.ClassOnly != 0) return false;

            if (a.Etypes != null)
            {
                for (var i = 0; i < a.Etypes.Length; i++)
                {
                    var e = a.Etypes[i];
                    if (string.IsNullOrEmpty(e)) continue;
                    if (e == row.Type || e == row.Subtype || e == row.Source) return false;
                }
            }

            var its = a.ItemTypes;
            if (its == null || its.Length == 0) return true;      // 无类型限制 = 通用词缀
            for (var i = 0; i < its.Length; i++)
            {
                var t = its[i];
                if (string.IsNullOrEmpty(t)) continue;
                if (t == row.Type || t == row.Subtype || t == row.Source) return true;
            }
            return false;
        }

        /// <summary>按官方 code 找 `item_c` 主键（找不到返回 0 并限频告警）。</summary>
        public static int ItemIdOfCode(string code)
        {
            if (string.IsNullOrEmpty(code)) return 0;
            var all = Tables.Default.Item.All();
            for (var i = 0; i < all.Count; i++)
            {
                if (all[i] != null && string.Equals(all[i].Code, code, System.StringComparison.OrdinalIgnoreCase))
                    return all[i].Id;
            }
            WarnOnce("code.miss." + code, $"item_c 里找不到 code=\"{code}\" 的物品（掉落表引用了未导入的行）");
            return 0;
        }
    }
}
