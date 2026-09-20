// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · Module/Item/ItemIconAvailability.cs   ★ agent-23 新增
//
// 唯一职责：回答「这件物品有没有**原版图标**」—— 决定它能不能进生成 / 掉落 / 商店候选池。
//
// 为什么要这个文件（**Play 实测病根**，不是推断）：
//   本项目的原版素材 = **经典版**（2000 官中版）解包产物：`d2data.mpq` 的
//   `data/global/items/*.dc6` 共 583 个文件，其中 `inv*.dc6` **348 张**已导入
//   `client/Assets/Resources/Clover/D2/Items/`（见 `原版资源/清单.md` §5）。
//   而 `item_c` 取自官方 **1.10f（资料片）** 数据表 —— 其中 14 件是**资料片职业专属装备**，
//   它们的图标（`invktr / invwrb / invaxf / invob1..3 / invdr1..2 / invba1..2 /
//   invpa1..2 / invne1..2`）**只在资料片包里**。实测（`storm.py` 逐名试探，2026 本轮）：
//     · `d2data.mpq`：listfile 11066 条，items 597 条，按名取这 14 个 **全部 MISS**；
//     · `patch_d2.mpq`：1140 条，其中 `items/*.dc6` **0 条**（它是**经典版**补丁包）；
//     · 磁盘 `Resources/Clover/D2/Items/` 348 张里也没有这 14 个文件名。
//   ⇒ 这 14 件物品一旦被生成，UI 只能退回暗色占位（Play 实测日志
//     `[Ui] 原版物品图不在本批素材里：D2/Items/invktr …` / `… invob1 …`；
//     用户投诉「商店没商品图标」）⇒ **生成池里直接剔除**（经典版原版本来就没有这些装备）。
//
// 判据（**只用配表列**，字段出处 `Table/Base/BaseItem.cs` 的 `Type` = `item_c.type`）：
//   `item_c.type` ∈ 资料片新增的 6 个 itemtype ——
//     `h2h` 刺客爪 / `orb` 法师宝珠 / `pelt` 德鲁伊皮甲 / `phlm` 野人盔 /
//     `ashd` 圣骑盾 / `head` 死灵头颅。
//   实测：item_c 全 137 行中，**缺图的 14 行恰好就是这 6 个 type 的全部行**
//   （id 37,38,39 / 40,41,42 / 64,65 / 66,67 / 68,69 / 70,71），其余 123 行全部能对上磁盘上的原版 PNG。
//
// ⛔ 本文件在 `Module/Item` 内：不引用其它 `Module/*` 的具体类型、不引用 `UI/**`（分层约束）。
// ─────────────────────────────────────────────────────────────────────────────

using Table;

namespace Diablo2.Module.Item
{
    /// <summary>「这件物品有没有原版图标」的唯一判据（纯函数，离线宿主可直接断言）。</summary>
    internal static class ItemIconAvailability
    {
        /// <summary>
        /// 资料片（LoD）新增的职业专属 itemtype —— 本批原版素材（经典版）里没有它们的图标。
        /// 逐条出处：官方 `itemtypes.txt`（资料片新增）；本项目实测见文件头「判据」一节。
        /// </summary>
        private static readonly string[] ExpansionOnlyTypes =
        {
            "h2h",   // 刺客爪（Katar / Wrist Blade / Hatchet Hands …）
            "orb",   // 法师宝珠（Eagle Orb / Sacred Globe / Smoked Sphere …）
            "pelt",  // 德鲁伊皮甲（Wolf Head / Hawk Helm …）
            "phlm",  // 野蛮人专用盔（Jawbone Cap / Fanged Helm …）
            "ashd",  // 圣骑士专用盾（Small Shield / Kite Shield …）
            "head",  // 死灵法师头颅（Preserved Head / Zombie Head …）
        };

        /// <summary>该 type 是否属于「资料片职业专属」= 本批素材里没有原版图标。</summary>
        public static bool IsExpansionOnlyType(string type)
        {
            if (string.IsNullOrEmpty(type)) return false;
            for (var i = 0; i < ExpansionOnlyTypes.Length; i++)
            {
                if (string.Equals(ExpansionOnlyTypes[i], type, System.StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// 这件物品的原版图标**是否在本批素材里**（false ⇒ UI 只能显示占位，不该进生成池）。
        /// <para>null 视为 false 并让调用方点名（不静默接受一个空行）。</para>
        /// </summary>
        public static bool HasOriginalIcon(BaseItemRow row)
        {
            if (row == null) return false;
            return !IsExpansionOnlyType(row.Type);
        }
    }
}
