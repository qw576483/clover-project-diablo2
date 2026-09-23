// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · Module/Combat/MeleeShape.cs
// **攻击判定形状的唯一口径**（纯函数 / 无 MonoBehaviour / 可离线逐例驱动）。
//
// 为什么要有它（用户本轮原话）：
//   「打击感就是一坨，**你是圆形判断的打击范围**」「为什么打击范围这么奇怪」
//   ⇒ 旧实现（`CombatModule.PlayerBasicAttack`）只用 `Iso.GridDistanceEuclidean` 比一个半径
//     —— 那是**圆**：背后的、正侧方的、隔着墙水的目标只要落在半径内就能打到。
//
// 形状口径（⛔ 不许在别处再写一份）：
//   ① `InFrontCone` —— **正面扇形**：目标相对「攻击者当前朝向」的夹角 ≤ `FrontConeHalfAngleDeg`。
//      ★ 特例（`melee-samecell`，2026-09-23）：**偏移 (0,0)（与攻击者同格）⇒ 恒命中** ——
//        角度在零距离上无定义，而**原版是按"距离 / 外接框求交"判的**（出处见下「同格」一条），
//        距离 0 ≤ 任何 reach 恒真 ⇒ 同格必命中。⛔ 只补这一个退化点，**不把扇形放宽成圆形**。
//   ② `InMeleeRect` —— **以朝向为轴的矩形走廊**：沿轴投影 ∈ [0, reach] 且 |垂距| ≤ `MeleeHalfWidth`。
//      （零偏移天然满足：沿轴 0 ∈ [0, reach]、垂距 0 ≤ 半宽。）
//   ③ `LineClear`   —— **线段不得被不可走地形阻断**（不许隔墙/隔水/跨河打到）。
//      （from == to 时无中间格 ⇒ 恒通。）
//   ①② 同时成立才算"在攻击形状内"；③ 是独立的一关（近战与远程都要过）。
//
// 出处：
//   · 形状（扇形 + 走廊 + 线段碰撞）：**用户本轮明确指定**（"原版是扇形/矩形"，本轮任务书 §1 第 1 条）
//     —— 原版 `Missiles.txt` 的 `Collision`(列 79) / `CollideType`(列 75) 列即"沿一条线段推进并按
//     单位外接框求交"的口径（`<根>/原版资源/参考工程_Diablerie/d2lod1.10txt/data/global/excel/Missiles.txt`）；
//     本项目把"线段求交"折算成格上的 `LineClear`（本项目无逐帧 sub-tile 单位框）。
//   · 正面锥半角 60°：**本项目新增**（8 向朝向 `Dir8` 的**最大量化误差是 22.5°**，
//     60° 锥 ⇒ 8 向里"朝目标"与"朝目标相邻一向"两个朝向都还能打到，再偏一向（45°）就打到 ⇒
//     既满足"正侧方 90° 不命中"，又不会把 8 向游戏的斜向贴身怪判丢）。
//   · 走廊半宽 `MeleeHalfWidth`：**本项目新增**，取 1.2 格 —— 8 向朝向下、距离 1.6 格处
//     45° 偏差的垂距 = 1.6 × sin45° = 1.13 ≤ 1.2（能打到），90° 侧方 = 1.6 > 1.2（打不到）。
//   · **同格（偏移 (0,0)）必命中**：出处 = 原版的近战触及是**"距离 / 外接框"整数口径**，不是角度口径 ——
//     ① `<根>/原版资源/d2lod1.10txt-1.10f/data/global/excel/Weapons.txt` 第 20 列 `rangeadder`
//        （近战武器追加触及：短短剑 / 手斧 = 空(=0)，战杖 `War Staff` = 1）⇒ 触及 = 1 + rangeadder **格**；
//     ② 同目录 `MonStats2.txt` 第 8 列 `MeleeRng`（怪物近战触及，`skeleton1` = 0）⇒ 同样是**格数**。
//     两处都表明"够不够得着"是**沿距离比较**（`0 ≤ reach` 恒真，且同格时两者外接框必然重叠）
//     —— 角度锥（本文件 ①）是**本项目新增**的量化近似（见上一条），它**不该**在"距离 0"这个
//     本该恒真的点上把攻击拒掉：实测 40 次真实左键全被拒（`report-audioverify2.md` §2.3）。
//   ⛔ 判据见 `tools/probes/hosts/combatcheck` 第 18 节（同格命中 / 正前方命中 / 正侧方不命中 /
//     超距不命中 / 隔墙不命中）。
// ─────────────────────────────────────────────────────────────────────────────

using System;
using UnityEngine;

namespace Diablo2.Module.Combat
{
    /// <summary>攻击判定形状：正面扇形 + 矩形走廊 + 线段通畅（全部纯函数）。</summary>
    internal static class MeleeShape
    {
        /// <summary>正面扇形半角（度）。60° ⇒ 8 向量化误差（±22.5°）与斜向贴身都不丢。</summary>
        public const float FrontConeHalfAngleDeg = 60f;

        /// <summary>`InFrontCone` 的余弦阈值 = cos(60°) = 0.5（避免每帧算 acos）。</summary>
        public static readonly float FrontConeCos = Mathf.Cos(FrontConeHalfAngleDeg * Mathf.Deg2Rad);

        /// <summary>矩形走廊半宽（格）—— 见文件头"走廊半宽"的推导。</summary>
        public const float MeleeHalfWidth = 1.2f;

        /// <summary>线段遍历的格数上限（防御：BadLine 参数导致死循环时能退出并报 false）。</summary>
        private const int MaxLineSteps = 1024;

        /// <summary>
        /// 朝向的**格增量** → **单位向量**（格坐标下的向量，不是屏幕方向）。
        /// <para>
        /// ⛔ 本类**不自己写 `Dir8` 映射表**：格增量一律由调用方用**引擎权威表**
        /// `Iso.DirectionDelta(dir)`（= `CloverEngine.IsoLayout.DirectionDelta`）取好再传进来
        /// —— 这样本文件对地图/投影零依赖，可被最小自检宿主单独编译驱动（见 §"为什么这样切"）。
        /// </para>
        /// </summary>
        /// <returns>false = 朝向向量不可解（0 向量；调用方据此**拒绝**本次攻击并留痕）。</returns>
        public static bool ToUnit(int dx, int dy, out float fx, out float fy)
        {
            fx = dx;
            fy = dy;
            var len = Mathf.Sqrt(fx * fx + fy * fy);
            if (len <= 0f)
            {
                fx = 0f;
                fy = 0f;
                return false;
            }
            fx /= len;
            fy /= len;
            return true;
        }

        /// <summary>
        /// **正面扇形**：目标偏移 (dx,dy) 与朝向单位向量 (fx,fy) 的夹角余弦 ≥ <paramref name="cosMin"/>。
        /// <para>
        /// ★ **零偏移（与攻击者同格，dx=dy=0）⇒ 返回 true（命中）** ——
        /// 零距离上"夹角"无定义，而**原版的近战触及是距离/外接框口径**（`Weapons.txt` `rangeadder` /
        /// `MonStats2.txt` `MeleeRng`，见文件头「同格必命中」一条）：`0 ≤ reach` 恒真 ⇒ 同格必命中。
        /// ⛔ 只补这一个退化点；扇形本身（±60°）与"正侧方 90° 不命中"的口径一字未动。
        /// </para>
        /// </summary>
        public static bool InFrontCone(float fx, float fy, float dx, float dy, float cosMin)
        {
            var len = Mathf.Sqrt(dx * dx + dy * dy);
            if (len <= 1e-6f) return true;      // 同格 ⇒ 命中（原版口径：距离 0 ≤ 任何 reach）
            return (dx * fx + dy * fy) / len >= cosMin;
        }

        /// <summary>
        /// **矩形走廊**：以朝向 (fx,fy) 为轴、长 <paramref name="reach"/>、半宽 <paramref name="halfWidth"/>。
        /// <para>`along = (dx,dy)·朝向`（必须 ∈ [0, reach]）；`perp = (dx,dy)·朝向的左法线`（|perp| ≤ halfWidth）。</para>
        /// </summary>
        public static bool InMeleeRect(float fx, float fy, float dx, float dy, float reach, float halfWidth)
        {
            var along = dx * fx + dy * fy;
            if (along < 0f || along > reach) return false;
            var perp = dx * (-fy) + dy * fx;
            return Mathf.Abs(perp) <= halfWidth;
        }

        /// <summary>
        /// **线段通畅**：从 <paramref name="from"/> 到 <paramref name="to"/> 的 Bresenham 线上，
        /// **除两端点外**的每一格都必须 <paramref name="walkable"/>。
        /// <para>
        /// 用途：攻击（近战 / 远程）不得穿过墙 / 水 / 地图外 —— 用户原话「屏幕外都能打我？」
        /// （隔着挡视线的地形也能打到）。⛔ `walkable == null`（地图未接入）⇒ 放行 + 由调用方留痕，
        /// 不把"拿不到地图"变成"打不到"。
        /// </para>
        /// </summary>
        public static bool LineClear(Func<Vector2Int, bool> walkable, Vector2Int from, Vector2Int to)
        {
            if (walkable == null) return true;

            var x = from.x;
            var y = from.y;
            var x1 = to.x;
            var y1 = to.y;
            var dx = Mathf.Abs(x1 - x);
            var dy = Mathf.Abs(y1 - y);
            var sx = x < x1 ? 1 : -1;
            var sy = y < y1 ? 1 : -1;
            var err = dx - dy;
            var guard = 0;

            while (guard++ < MaxLineSteps)
            {
                if (x == x1 && y == y1) return true;

                var e2 = 2 * err;
                if (e2 > -dy) { err -= dy; x += sx; }
                if (e2 < dx) { err += dx; y += sy; }

                if (x == x1 && y == y1) return true;          // 到达终点：两端点不判可走
                if (!walkable(new Vector2Int(x, y))) return false;
            }

            return false;                                       // 步数爆掉 = 入参异常 ⇒ 按"不通"处理并让调用方留痕
        }
    }
}
