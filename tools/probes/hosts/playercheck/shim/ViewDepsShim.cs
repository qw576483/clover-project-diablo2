// ─────────────────────────────────────────────────────────────────────────────
// playercheck · shim/ViewDepsShim.cs   ★ U27（人物抖动三分判据）新增 ★
//
// **为什么有这份文件**：本片要把 `Module/View` 编进本宿主，才能在同一段输入下分列
// ① 相机（`CameraRig.Position`）② 角色**渲染**节点（`ViewModule.EntityWorld` = `EntityView.Root.transform.position`
// 的**唯一写入口径**，`ViewModule.cs:1345`）③ 角色逻辑（`IPlayerModule.World`）。
//
// `Module/View/*.cs` 里只有 `GroundItemVisual.cs` 编不进来：它链到 `Diablo2.UI.D2Icon`
// （再把 `UiLog` / `UiArt` / `ItemQualityColor` 与 uGUI 一起拖进来 = 整条 UI 依赖）。
// 而它做的两件事（地面物品**图标路径** / **品质色调**）与**位置**毫无关系 —— 本判据一个字都不碰。
// ⇒ 用最小替身顶住那两个成员，签名逐字对齐生产件 `Module/View/GroundItemVisual.cs:56/80`。
//
// ⛔ 这不是"镜像生产逻辑"：替身**只提供编译期可解析的签名**，返回 null / 纯白；
//    位置判据拿到的是**真实生产的** `ViewModule.EntityWorld`（纯函数，本宿主可离线调用）。
// ⛔ 生产代码一行未改：排除写在 `PlayerCheck.csproj` 的 `Compile Remove` 里。
// ─────────────────────────────────────────────────────────────────────────────

using Diablo2.Def;
using UnityEngine;

namespace Diablo2.Module.View
{
    /// <summary>
    /// `Module/View/GroundItemVisual.cs` 的**最小替身**（只 `IconPathOf` / `TintOf` 两个成员；
    /// 只让 `ViewModule.cs` 编得过，判据不调用它）。
    /// </summary>
    internal static class GroundItemVisual
    {
        /// <summary>同生产件签名（宿主无配表/无资源 ⇒ 恒 null = 「退回品质色块」那条分支）。</summary>
        public static string IconPathOf(ItemStack item) => null;

        /// <summary>同生产件签名（宿主不着色 ⇒ 纯白 = 原版像素）。</summary>
        public static Color TintOf(ItemQuality quality) => Color.white;
    }
}
