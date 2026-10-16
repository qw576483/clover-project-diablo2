// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · Module/Map/ContainerBreak.cs  本项目新增
//
// **可破坏容器**（桶 / 箱）的地图侧判定与落地（一处口径）。
//
// 为什么要有它：原版 `Objects.txt` 里的容器（`Barrel` ID7 / `Chest5..7`）本来就能打碎并掉东西，
//   而本项目此前只把它们的贴图当装饰画出来（`MapGenDeco`），点它没有任何后果。
//
// 判定与数值的出处（⛔ 不自创常数）：
//   · 哪些 deco 是容器 = `MapGenDeco.Kinds` 的 `Dir`（= 官方 `Objects.txt` 的 `Token` 小写）：
//       `b1` = Barrel（ID 7）、`cy` / `cx` / `cu` = Chest5 / Chest6 / Chest7（ID 240 / 241 / 242）
//       —— 逐条见 `Resources/Clover/D2/Objects/deco-manifest.json` 的 `classToObj`。
//   · 掉落档位与等级口径 = `Def.ContainerTc`（官方 `Act 1 Chest A/B/C` 的 `level` 列 0/5/9）。
//
// 与其它层的分工（都不 `using` 对方，全走事件）：
//   · `Module/Player`：走到相邻格（它知道玩家在哪）；到点发 `Events.ContainerBroken`。
//   · 本文件所在模块：收 `Events.ContainerBroken` ⇒ 清 deco 登记表 + 让画布重铺（`MapView.ClearDecoAt`）。
//   · `Module/Item`：收同一条事件 ⇒ 按 `Def.ContainerTc` 挑表并 `DropLoot`。
// ─────────────────────────────────────────────────────────────────────────────
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Diablo2.Module.Map
{
    /// <summary>可破坏容器的判定与地图侧破坏动作（见文件头）。</summary>
    internal static class ContainerBreak
    {
        /// <summary>本工程会落地的容器 deco 目录名（= 官方 Token 小写；出处见文件头）。</summary>
        private static readonly string[] ContainerDirs = { "b1", "cy", "cx", "cu" };

        /// <summary>这一格是不是可破坏容器（图外 / 没有 deco / deco 不是容器 ⇒ false）。</summary>
        public static bool IsContainerAt(GridMap map, Vector2Int cell)
        {
            if (map == null) return false;
            if (!map.TryGetDeco(cell.x, cell.y, out var kind) || kind < 0) return false;
            return IsContainerKind(kind);
        }

        /// <summary>`MapGenDeco.Kinds` 的下标是不是容器类。</summary>
        public static bool IsContainerKind(int kind)
        {
            if (kind < 0 || kind >= MapGenDeco.Kinds.Length) return false;
            var dir = MapGenDeco.Kinds[kind].Dir;
            for (var i = 0; i < ContainerDirs.Length; i++)
            {
                if (string.Equals(ContainerDirs[i], dir, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        /// <summary>
        /// 破坏一格容器：清 deco 登记表 → 让画布（若有）重铺该格。
        /// <para>顺序刻意如此：**先摘登记表再重铺** —— 重铺失败也不会让"已经打碎的桶"又出现在画布上
        /// （`PlanCell` 读的就是这张表）。掉落不在本文件：由 `Module/Item` 收同一条事件后做。</para>
        /// </summary>
        /// <returns>true = 真的破坏了；false 时 <paramref name="why"/> 说明原因（非预期分支必留痕）。</returns>
        public static bool TryBreak(GridMap map, Vector2Int cell, Action<Vector2Int> onCleared, out string why)
        {
            why = null;

            if (map == null)
            {
                why = "地图未装配";
                return false;
            }
            if (!map.TryGetDeco(cell.x, cell.y, out var kind) || kind < 0)
            {
                why = "该格没有装饰物件（已破坏 / 换图）";
                return false;
            }
            if (!IsContainerKind(kind))
            {
                why = $"该格的装饰物件不是容器（{MapGenDeco.Kinds[kind].Dir} / {MapGenDeco.Kinds[kind].ClassName}）";
                return false;
            }

            if (!map.ClearDeco(cell.x, cell.y))
            {
                why = "清 deco 登记失败（图外 / 未开启逐格覆盖）";
                return false;
            }
            if (onCleared != null) onCleared(cell);
            return true;
        }

        /// <summary>
        /// 走到容器**相邻格**的落点（8 邻域里离 <paramref name="from"/> 最近的**可走**格）。
        /// 找不到（被围死）⇒ 返回 <c>null</c>（调用方留日志，不做"猜一格"的兜底）。
        /// </summary>
        public static Vector2Int? ApproachCell(GridMap map, Vector2Int container, Vector2Int from)
        {
            if (map == null) return null;

            Vector2Int? best = null;
            var bestDist = int.MaxValue;
            for (var dx = -1; dx <= 1; dx++)
            {
                for (var dy = -1; dy <= 1; dy++)
                {
                    if (dx == 0 && dy == 0) continue;
                    var c = new Vector2Int(container.x + dx, container.y + dy);
                    if (!map.InBounds(c) || !map.Walkable(c)) continue;
                    if (c == from) return c;                        // 已经贴着 ⇒ 就用脚下这格

                    var d = Mathf.Max(Mathf.Abs(c.x - from.x), Mathf.Abs(c.y - from.y));
                    if (d < bestDist) { bestDist = d; best = c; }
                }
            }
            return best;
        }

        /// <summary>两个格是否相邻（含同格；Chebyshev ≤ 1）——"贴上去才能打"的判据。</summary>
        public static bool IsAdjacent(Vector2Int a, Vector2Int b)
            => Mathf.Max(Mathf.Abs(a.x - b.x), Mathf.Abs(a.y - b.y)) <= 1;

        /// <summary>地图上现在还剩几个容器（自证 / 断言用；`into` 非空时同时填出它们的格）。</summary>
        public static int CountContainers(GridMap map, List<Vector2Int> into = null)
        {
            if (map == null) return 0;
            if (into != null) into.Clear();
            var n = 0;
            for (var x = 0; x < map.Width; x++)
            {
                for (var y = 0; y < map.Height; y++)
                {
                    if (!map.TryGetDeco(x, y, out var k) || !IsContainerKind(k)) continue;
                    n++;
                    if (into != null) into.Add(new Vector2Int(x, y));
                }
            }
            return n;
        }
    }
}
