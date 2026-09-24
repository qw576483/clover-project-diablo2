// ─────────────────────────────────────────────────────────────────────────────
//
// 唯一职责：回答「**地面上的这件物品该画哪张图、什么色调**」（纯函数，离线宿主可断言）。
//
//   `ViewModule.CreateGroundItem` 只按**品质色**建了一个 40×79 的占位四边形
//   （`SpriteFrames.QualityColor(quality)`，而 `Normal` 品质 = `Color.white` ⇒ 一块浅灰方块），
//   `ApplyFrame` 对 `IsGroundItem` 直接 `return` ⇒ 地面上的东西看不出是什么物品。
//   故本文件提供真正的图标路径 + 色调（下面两条口径）。
//
// ── 口径（咬死一条，不许各处自己拼路径）──────────────────────────────────────
//   地面物品图 **= 背包 / 装备栏 / 腰带 / 商店 用的同一张原版物品图**。
//   依据：原版 D2 地面物品用的就是物品的 `invfile`（背包图）——没有第二套"地面图"，
//   掉落时那一下的翻飞动画（`flippyfile`）本项目未接。
//   · **路径解析唯一来源 = `Diablo2.UI.D2Icon.ItemIconPath`** —— 它带 `code → invfile`
//     别名表（50 条，出处见 `D2Icon.IconFileAlias` 的文件头：原版
//     `Weapons/Armor/Misc.txt` 的 `invfile` 列）。本文件**只转发、不复制**那张表：
//   · **色调 = `D2Icon.QualityTint`**（与另外三个图标面同款：品质色往白里提 62%，
//     原版 8bit 像素不被染死）。`Normal` 品质 = 纯白 ⇒ 普通物品**逐像素等于原版**。
//   · **特例：金币**（`itemId = 0` 的"金币堆"，**不是** `item_c` 行 ⇒ `D2Icon` 覆盖不到它）：
//     原版地面金币图 = `invgld*.dc6`（按金额**三档**小/中/大，三张图都在盘）；**分档阈值无权威载体**
//
// ── 分层（如实登记，不是"忘了"）─────────────────────────────────────────────
//   本文件在 `Module/View`，引用了 `Diablo2.UI.D2Icon`。两条理由：
//     ① 既有先例：`Module/Flow/{AppFlow,LoadingSteps,ClassTable}.cs` 早已 `using Diablo2.UI;`；
//     ② `D2Icon` 里那部分是**纯函数路径解析**（不碰任何 UI 节点），复用它才保住"唯一来源"。
//     而 `Module/View` 若引用 `Module/Item` 的表同样要跨层 —— 不如直接复用既有唯一来源。
//   影响面已核对：编译 `Module/View/**` 的四个离线宿主（animcheck / combatcheck /
//   movecheck / fullcheck）**都同时链了 `UI/*.cs`** ⇒ 不会出现"宿主缺 UI 编不过"。
// ─────────────────────────────────────────────────────────────────────────────

using Diablo2.Core;
using Diablo2.Def;
using Diablo2.UI;
using UnityEngine;

namespace Diablo2.Module.View
{
    /// <summary>地面物品的**原版图与色调**（纯函数；不读时钟、不碰节点 ⇒ 离线可断言）。</summary>
    internal static class GroundItemVisual
    {
        /// <summary>
        /// 该物品在地上的**原版图标资源路径**（`D2/Items/inv{code}`，code 已过 `invfile` 别名表）。
        /// <para>返回 null 的两种情况（调用方据此退回**品质色块**当可见的缺失信号）：
        /// ① `item == null` / `itemId &lt;= 0`（空物品，不该来取图）；
        /// ② 配表 `item_c` 里没有这个 id（`D2Icon.ItemIconPath` 已点名 Warn）。
        /// 它**不判**"磁盘上到底有没有这张图"（那要 `Game.Res.Exists`，是运行期的事，
        /// 见 `ViewModule.CreateGroundItem`）——本方法保持纯函数，才能被离线宿主断言。</para>
        /// </summary>
        public static string IconPathOf(ItemStack item)
        {
            if (item == null) return null;

            // 金币（`Module/Item/LootRoller.cs:327` 造的金币堆：`itemId = 0, isGold = true`）
            //   **不是 `item_c` 行** ⇒ 走不到 `D2Icon`（它对 itemId ≤ 0 直接返回 null）
            //   原版地面金币的画法 = 物品图 `invgld*.dc6`（按金额分小/中/大**三档**，三张图都在盘：
            //   `D2/Items/{invgld,invgldm,invgldh}.png`）。
            //   （不许自己拍一个金额阈值 —— 那就成了编造数据）。
            //   路径拼法复用项目**唯一**入口 `ResPaths.ItemIcon`（= `D2/Items/inv` + code）。
            if (item.isGold) return ResPaths.ItemIcon("gld");

            if (item.itemId <= 0) return null;
            return D2Icon.ItemIconPath(item.itemId);
        }

        /// <summary>
        /// 地面图上要乘的色调（= 另外三个图标面同款；见文件头「口径」）。
        /// <para>原版地面物品图**不着色**（品质色只染**名字**，由 `UI/GroundItemLabelView`
        /// 负责）；本项目为了"地上看到的颜色 = 背包里看到的颜色"统一取 `QualityTint`，
        /// 而它对 `Normal` 品质返回**纯白** ⇒ 绝大多数物品（普通品质）逐像素等于原版。</para>
        /// </summary>
        public static Color TintOf(ItemQuality quality)
        {
            return D2Icon.QualityTint(quality);
        }
    }
}
