// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · Module/View/EquipVisual.cs
// 「角色装备外观套」的**纯规则层**（本片新增）：决定**用哪一套帧**（= 哪个 unitKey）。
//
// 为什么单独一个文件、且全是纯函数：这套规则（key 拼法 + 逐级回退）是本次交付里
//   **唯一"能算错但不报错"**的东西 —— 算错的后果是"角色手上没东西"或"帧数取到 0 ⇒ 静默占位块"。
//   埋在 `MonoBehaviour`/`ViewModule` 里的话，离线宿主**一行都测不到**（`ViewModule` 要
//   `new GameObject` 才跑得到）。抽成纯函数后 `tools/probes/hosts/animcheck` 可直接逐条断言
//   （★ 同一教训见 `ViewAnimState` 的注释：那串三元链当年藏了一个"受击动画从没被渲染"的缺陷）。
//
// 素材契约（导出侧，见 `tools/probes/measure/d2_equip_sets_check.py` 文件头）：
//   目录 = `Chars/{class}/equip/{key}/`，`{key}` 的拼法就是本文件 `KeyOf` 的那三条：
//     ① 主手武器 + 副手盾 都有 ⇒ `"{weapon}_{shield}"`（例 `jav_buc`）
//     ② 只有武器            ⇒ `"{weapon}"`（例 `jav`）
//     ③ 只有盾              ⇒ `"{shield}"`（例 `buc`）
//     ④ 都没有              ⇒ **徒手**（走 `Chars/{class}/` 那套已验收素材，key = null）
//   该 key 的套不存在 ⇒ **逐级回退**：去掉盾 → 去掉武器 → 徒手（`Candidates` 的顺序）。
//
// ⛔ 本文件**不引用** `UnityEngine`，也不查磁盘：套是否存在由调用方注入
//   `Func<string,bool> exists`（生产 = `EquipFrameCounts.Has`，它由 manifest 生成；见生成器
//   `tools/probes/measure/gen_equip_frame_counts.py`）。这样规则本身**可离线直调**。
//
// ⛔ 分层：本文件不 `using Diablo2.Module.*`（`conventions.md` 的分层自检 ②：`Module/*` 里
//   0 处跨模块 using）。装备部位判定因此在这里**重述最小口径**（只判"武器 / 盾"两件事），
//   并由离线断言 `animcheck §7` 与 `Module/Item/Equipment.SlotOf(row)` 在**整张 `item_c`** 上
//   逐行对账 ⇒ 两处不许漂移。
// ─────────────────────────────────────────────────────────────────────────────

using System;
using System.Collections.Generic;
using Diablo2.Def;

namespace Diablo2.Module.View
{
    /// <summary>装备外观套的 key 规则与回退链（纯函数；见文件头）。</summary>
    /// <remarks>
    /// 只有"占哪只手"这件事用一个**已有**的枚举表达 = `Def.ItemSlot`（武器 / 盾 / 无），
    /// 本文件因此**不新增类型**（`constraints.md` #1「一个 `.cs` 一个类」）。
    /// 返回 `ItemSlot` 还带来一个好处：判据可以把本方法与
    /// `Module/Item/Equipment.SlotOf(row)` 在整张 `item_c` 上**逐行对账**（见 `animcheck §7`）。
    /// </remarks>
    internal static class EquipVisual
    {
        /// <summary>武器与盾的 key 连接符（= 导出侧目录名里的下划线，例 `jav_buc`）。</summary>
        public const string Join = "_";

        /// <summary>徒手时用的 key 名（**不是目录名**，只用于日志/断言：徒手 key = null）。</summary>
        public const string BareHandLabel = "徒手";

        /// <summary>回退原因文案里必须出现的那句话（判据会 grep 它，保证"未导出"能被人一眼看到）。</summary>
        public const string MissingReason = "该装备外观未导出";

        /// <summary>
        /// `item_c` 的 `source`/`type`/`subtype` → 该物品占哪只手
        /// （只可能返回 <see cref="ItemSlot.Weapon"/> / <see cref="ItemSlot.Shield"/> / <see cref="ItemSlot.None"/>）。
        /// <para>口径（与 `Module/Item/Equipment.SlotOf(row)` 的 weap / armo 分支**同义**）：
        /// `source == "weap"` ⇒ 武器，但 `subtype == "tpot"`（官方 `type2` = 投掷药水）不是可穿戴武器；
        /// `source == "armo"` 且 `type ∈ {shie, ashd}` ⇒ 盾（前者 = 普通盾、后者 = 圣骑士专用盾）。</para>
        /// <para>其余一律 <see cref="ItemSlot.None"/>（含没登记过的防具类型）—— 本层只管外观，
        /// **不做**"防具但没登记 ⇒ 按盔甲处理"那样的兜底（那会凭空造出一个外观语义）。</para>
        /// </summary>
        public static ItemSlot HandOf(string source, string type, string subtype)
        {
            if (string.IsNullOrEmpty(source)) return ItemSlot.None;

            if (Eq(source, "weap"))
            {
                return Eq(subtype, "tpot") ? ItemSlot.None : ItemSlot.Weapon;
            }

            if (Eq(source, "armo"))
            {
                return Eq(type, "shie") || Eq(type, "ashd") ? ItemSlot.Shield : ItemSlot.None;
            }

            return ItemSlot.None;
        }

        /// <summary>装备外观套的 key（见文件头 ①②③④）；都没装备 ⇒ **null**（徒手）。</summary>
        public static string KeyOf(string weaponCode, string shieldCode)
        {
            var w = Norm(weaponCode);
            var s = Norm(shieldCode);
            if (w != null && s != null) return w + Join + s;
            if (w != null) return w;
            if (s != null) return s;
            return null;
        }

        /// <summary>
        /// 回退链（**顺序即优先级**）：`{w}_{s}` → `{w}` → `{s}` → **null（徒手）**。
        /// 重复项按首次出现去重（只有武器时链里不该出现两次同一个 key）。
        /// </summary>
        public static string[] Candidates(string weaponCode, string shieldCode)
        {
            var list = new List<string>(4);
            Add(list, KeyOf(weaponCode, shieldCode));
            Add(list, KeyOf(weaponCode, null));   // 去掉盾
            Add(list, KeyOf(null, shieldCode));   // 去掉武器
            Add(list, null);                      // 徒手
            return list.ToArray();
        }

        /// <summary>
        /// 按回退链选套：返回**第一个存在**的 key（`null` = 徒手）。
        /// <paramref name="wanted"/> = 按装备算出来的"理想 key"（用于日志里说明"本来要哪个"）。
        /// <paramref name="fellBack"/> = 是否发生了回退（选中的 ≠ 理想的）。
        /// <para><paramref name="exists"/> 为 null ⇒ 视为"一套都没有"（全部回退到徒手），
        /// 不静默：那是调用方接线错，由调用方留 Warn。</para>
        /// </summary>
        public static string Select(string weaponCode, string shieldCode, Func<string, bool> exists,
            out string wanted, out bool fellBack)
        {
            wanted = KeyOf(weaponCode, shieldCode);
            var chain = Candidates(weaponCode, shieldCode);

            for (var i = 0; i < chain.Length; i++)
            {
                var key = chain[i];
                if (key == null) break;                       // 徒手：链尾，无条件成立
                if (exists != null && exists(key))
                {
                    fellBack = key != wanted;
                    return key;
                }
            }

            fellBack = wanted != null;
            return null;                                      // 徒手
        }

        /// <summary>装备外观套的 unitKey（= 生成物 `EquipFrameCounts.ByUnit` 的键）；key 为空 ⇒ null。</summary>
        public static string UnitKeyOf(PlayerClass cls, string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            return cls.ToString().ToLowerInvariant() + "/equip/" + key.ToLowerInvariant();
        }

        /// <summary>
        /// ★ 片 Y（R4）：生产用的 <c>exists</c> 适配器 —— 把**裸 key**（`jav` / `hax_buc`）
        /// 加上职业前缀换成生成物的 **unitKey**（`{class}/equip/{key}`）再查表。
        /// <para>⛔ 为什么必须有这一层：`EquipFrameCounts.Has` 的形参是 **unitKey**
        /// （`ByUnit` 的键 = `"barbarian/equip/buc"`），而 <see cref="Select"/> 的回调收到的是
        /// **裸 key**（`"buc"`）。直接把 `EquipFrameCounts.Has` 当回调传进去 ⇒ 任何 key 都查不到
        /// ⇒ **装备外观套永远回退徒手**（实机实测：装上武器后仍走 `Chars/{class}/`，且 Warn 说
        /// "该装备外观未导出" —— 而 `Chars/barbarian/equip/{hax,buc,hax_buc}` 都在盘上）。
        /// 离线宿主旧判据注入了"裸 key 的 exists"，所以没碰到这条接线错位（缺陷只在生产接线上）。</para>
        /// </summary>
        public static Func<string, bool> ExistsAdapter(PlayerClass cls)
        {
            return key => EquipFrameCounts.Has(UnitKeyOf(cls, key));
        }

        /// <summary>
        /// 回退时的日志文案（**必须能定位**：玩家职业 + 装备 code + 回退到哪个 key + "该装备外观未导出"）。
        /// 抽成纯函数是为了让判据能 grep 住"这几个字段一个都不许少"。
        /// </summary>
        public static string FallbackWarnText(PlayerClass cls, string weaponCode, string shieldCode,
            string wanted, string chosen)
        {
            return $"[装备外观] 职业={cls} 装备 code=[武器={Show(weaponCode)}, 盾={Show(shieldCode)}] " +
                   $"⇒ 本应使用外观套「{Show(wanted)}」，但它{MissingReason} ⇒ " +
                   $"回退到「{Show(chosen)}」" +
                   (chosen == null ? $"（{BareHandLabel}，走 Chars/{cls.ToString().ToLowerInvariant()}/）" : "") +
                   "；修正办法 = 补跑 tools/d2codec/export_chars.py --equip-sets 后重跑 " +
                   "tools/probes/measure/gen_equip_frame_counts.py";
        }

        /// <summary>装备查表失败时的日志文案（配表缺行 / itemId=0 ⇒ 外观无从判断，按"没这件"处理）。</summary>
        public static string RowMissingWarnText(PlayerClass cls, int itemId, string name)
        {
            return $"[装备外观] 职业={cls} 的装备「{name}」(itemId={itemId}) 在 `item_c` 里查不到行 " +
                   "⇒ 无法判定它占哪只手，该件不计入外观 key（若这就不对，先查打表是否漏了这把武器）";
        }

        private static string Show(string key)
        {
            return string.IsNullOrEmpty(key) ? BareHandLabel : key;
        }

        private static void Add(List<string> list, string key)
        {
            if (key == null)
            {
                if (!list.Contains(null)) list.Add(null);
                return;
            }
            if (!list.Contains(key)) list.Add(key);
        }

        /// <summary>忽略大小写的等值（配表列可能大小写不一，例 `shie` / `ashd`）。</summary>
        private static bool Eq(string a, string b)
        {
            return !string.IsNullOrEmpty(a) && string.Equals(a.Trim(), b, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>规整 code：去空白 + 转小写；空 ⇒ null（"没有"和"空串"必须归一到同一个表示）。</summary>
        private static string Norm(string code)
        {
            if (string.IsNullOrEmpty(code)) return null;
            var t = code.Trim().ToLowerInvariant();
            return t.Length == 0 ? null : t;
        }
    }
}
