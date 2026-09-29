// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · Def/ContainerArgs.cs  本项目新增
//
// `Events.ContainerBroken` 的载荷：**哪一格的容器**被谁（几级）打碎了。
//
// 为什么是"格 + 等级"这两个字段：
//   · 格 —— 地图侧要清该格的 deco 登记表并让画布重铺；物品侧要把掉落物放在该格。
//   · 等级 —— 官方 `TreasureClassEx.txt` 的 `Act 1 Chest A/B/C` 三行各带 `level` = 0/5/9，
//     档位就按这个门限取（见 `Def.ContainerTc`）；掉落物等级也用它（与怪物掉落的 `state.level` 同一口径）。
//
// 放 `Def`（而不是某个 Module）的原因：这一个载荷同时被 `Module/Map` 与 `Module/Item` 收，
//   `Def` 是各模块都可见的共享层 —— 若挂在其中任一模块下，另一个模块就得 `using` 它（跨模块 using 违规）。
// ─────────────────────────────────────────────────────────────────────────────
using UnityEngine;

namespace Diablo2.Def
{
    /// <summary>见文件头。</summary>
    internal sealed class ContainerArgs
    {
        /// <summary>容器所在格。</summary>
        public Vector2Int cell;

        /// <summary>打碎它的玩家等级（用于挑官方 TC 档位 + 掉落物等级）。</summary>
        public int level;
    }
}
