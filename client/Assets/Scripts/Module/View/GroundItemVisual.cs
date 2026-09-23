// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · Module/View/GroundItemVisual.cs   ★ 片 ground-item-icon 新增
//
// 唯一职责：回答「**地面上的这件物品该画哪张图、什么色调**」（纯函数，离线宿主可断言）。
//
// ── 为什么需要这个文件（用户报的缺陷，前片定位到行，本片自己复核过）──────────────
//   `ViewModule.CreateGroundItem`（改动前 :519-522）只按**品质色**建了一个 40×79 的
//   占位四边形（`SpriteFrames.QualityColor(quality)`，而 `Normal` 品质 = `Color.white`
//   ⇒ 一块浅灰方块），整条路径**没有任何**加载物品图标的调用（`ApplyFrame` 对
//   `IsGroundItem` 直接 `return`）⇒ 地面上的东西永远看不出是什么物品。
//   用户图 `uifix4_tour_drag_3.png` / `pv_drag_3_right_box_zoom.png` 右侧那块灰矩形就是它
//   —— **不是拖拽残留，是地面物品从来没画过图标**。
//
// ── 口径（咬死一条，⛔ 不许各处自己拼路径）──────────────────────────────────────
//   地面物品图 **= 背包 / 装备栏 / 腰带 / 商店 用的同一张原版物品图**。
//   依据：原版 D2 地面物品用的就是物品的 `invfile`（背包图）——没有第二套"地面图"，
//   掉落时那一下的翻飞动画（`flippyfile`）本项目未接（登记见回报）。
//   · **路径解析唯一来源 = `Diablo2.UI.D2Icon.ItemIconPath`** —— 它带 `code → invfile`
//     别名表（50 条，出处见 `D2Icon.IconFileAlias` 的文件头：原版
//     `Weapons/Armor/Misc.txt` 的 `invfile` 列）。本文件**只转发、不复制**那张表：
//     复制 = 同一份真源两处，漂移一处就静默取不到图（`constraints.md` #9 那类失败）。
//   · **色调 = `D2Icon.QualityTint`**（与另外三个图标面同款：品质色往白里提 62%，
//     原版 8bit 像素不被染死）。`Normal` 品质 = 纯白 ⇒ 普通物品**逐像素等于原版**。
//
// ── 分层（如实登记，⛔ 不是"忘了"）─────────────────────────────────────────────
//   本文件在 `Module/View`，引用了 `Diablo2.UI.D2Icon`。两条理由：
//     ① 既有先例：`Module/Flow/{AppFlow,LoadingSteps,ClassTable}.cs` 早已 `using Diablo2.UI;`；
//     ② `D2Icon` 里那部分是**纯函数路径解析**（不碰任何 UI 节点），复用它才保住"唯一来源"。
//   ⛔ 反例（本片没走）：把手写别名表搬到 `Module/Item` ⇒ 两处真源 + 需手动同步，
//     而 `Module/View` 若引用 `Module/Item` 的表同样要跨层 —— 不如直接复用既有唯一来源。
//   影响面已核对：编译 `Module/View/**` 的四个离线宿主（animcheck / combatcheck /
//   movecheck / fullcheck）**都同时链了 `UI/*.cs`** ⇒ 不会出现"宿主缺 UI 编不过"。
// ─────────────────────────────────────────────────────────────────────────────

using Diablo2.Def;
using Diablo2.UI;
using UnityEngine;

namespace Diablo2.Module.View
{
    /// <summary>地面物品的**原版图与色调**（纯函数；⛔ 不读时钟、不碰节点 ⇒ 离线可断言）。</summary>
    internal static class GroundItemVisual
    {
        /// <summary>
        /// 该物品在地上的**原版图标资源路径**（`D2/Items/inv{code}`，code 已过 `invfile` 别名表）。
        /// <para>返回 null 的两种情况（调用方据此退回**品质色块**当可见的缺失信号）：
        /// ① `item == null` / `itemId &lt;= 0`（空物品，不该来取图）；
        /// ② 配表 `item_c` 里没有这个 id（`D2Icon.ItemIconPath` 已点名 Warn）。
        /// ⚠️ 它**不判**"磁盘上到底有没有这张图"（那要 `Game.Res.Exists`，是运行期的事，
        /// 见 `ViewModule.CreateGroundItem`）——本方法保持纯函数，才能被离线宿主断言。</para>
        /// </summary>
        public static string IconPathOf(ItemStack item)
        {
            if (item == null || item.itemId <= 0) return null;
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
