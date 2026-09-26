// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · Module/Map/AreaLighting.cs
//
// 区域光照（暗区压暗）的**纯函数口径**：哪些区域暗、压暗到什么颜色、以角色为圆心的
// 光照半径与衰减曲线。渲染侧（`Module/View/PlayerLightMask`）只消费这里的取值。
//
// 出处（逐条都是原版载体，数值一律搬运、不新造）：
//
// ① **区域环境光** = 官方 `Levels.txt` 的 `Intensity` / `Red` / `Green` / `Blue`
//    （`原版资源/d2lod1.10txt-1.10f/data/global/excel/Levels.txt`，首行 = 列名）：
//      · line 3  `Act 1 - Town`         Intensity=0 Red=0   Green=0   Blue=0     ← 罗格营地
//      · line 4  `Act 1 - Wilderness 1` Intensity=0 Red=0   Green=0   Blue=0     ← 血腥荒野
//      · line 10 `Act 1 - Cave 1`       Intensity=0 Red=255 Green=255 Blue=255   ← 邪恶洞穴
//    四个字段**全 0 = 官方「该区域不设环境光」**（字段释义：*"若所有相关字段均为 0，游戏会
//    忽略对其环境色的设置"*）⇒ 该区域**不做任何压暗**（罗格营地 / 血腥荒野 = 全亮）。
//    非全 0 ⇒ 该区域走光照半径压暗、半径外压到该环境光色；`Intensity` = 环境光强度
//    ⇒ 0 = 无强度 = 黑（`Act 1 - Cave 1` 的 Intensity 正是 0 ⇒ 邪恶洞穴半径外压到黑）。
//
// ② **角色基准光照半径** 的数值口径 = **两条并列**，缺一不可：
//    ⓐ 出处（网页 / 官方数据表字段释义）：`charstats.txt` 的 `LightRadius` = **13**（8 个职业一致，
//       释义 *"Baseline radius size of the character's Light Radius"*）—— D2R Data Guide 的
//       charstats 页；该页把 `LightRadius` 与 `WalkVelocity`/`RunVelocity`/`RunDrain` 同组，
//       并注明这一组"全职业统一"。
//    ⓑ 本机实测（**不是本机有这张表**）：本工程在手的经典表
//       `原版资源/d2lod1.10txt-1.10f/data/global/excel/charstats.txt` 共 **79 列，逐列看过、
//       没有任何 light 列**；`LightRadius` 这个名字在本机只出现在 `ItemStatCost.txt` /
//       `Properties.txt` / `Armor.txt` / `Misc.txt` / `Weapons.txt` —— 那全是**物品**的光半径
//       属性，不是角色基础半径。
//    ⇒ 经典版（1.10f）该值由引擎持有、不落在 excel 里；本值 = ⓐ 的名字/数值 + ⓑ 说明它不在经典表。
//    换版本 / 拿不到 ⓐ 时，这条注释与 `RadiusUnits` 必须一起重核（见 ③ 的标定）。
//
// ③ **半径的单位标定** = 原版实机地牢帧
//    （`策划/基线图/原版_实机_HUD+automap_20260923.png`，量法脚本读数）：
//      · 距角色中心 ~160 px 处地板亮度落到背景黑（`v≈6/255`；径向剖面可由
//        `<项目根>/.ai-tmp/test/p_light_shot2.py` 在同一张图上复算）；
//      · 该帧的格宽 ≈ 123 px（帧宽 616 px / 原版 800 px = 0.77 缩放 ⇒ 原版 160 px 格宽）；
//      ⇒ 13 单位 ≈ **1.3 格**（即 1 单位 ≈ 1/10 格）。
//    ⚠️ 帧缩放由帧宽推得（HUD 生命球实测受红箭头标注污染，未作为独立标定），
//    半径量级有 ±1 倍不确定度 —— 但两种读法（13 = 1.3 格 / 13 = 2.6 格）里，
//    **2.6 格会让整屏（原版 800 px 宽只有 5 格）几乎全亮**，与原版"地牢暗"的实际渲染不符，
//    故取 1.3 格。
//
// ④ **衰减剖面** = 上述实机帧的径向亮度剖面**单调下降、无平台**（中心最亮 → 沿半径渐暗 →
//    半径处到环境光）⇒ 本实现取**线性**衰减（`ShadowAlphaAt`）。剖面拟合不是搬运值，
//    所以只取"单调下降、无平台"这两条实测性质，不引入曲线参数。
// ─────────────────────────────────────────────────────────────────────────────

using Diablo2.Core;
using Diablo2.Def;
using UnityEngine;

namespace Diablo2.Module.Map
{
    /// <summary>区域光照口径（纯函数；离线可断言、与渲染节点无关）。</summary>
    internal static class AreaLighting
    {
        /// <summary>
        /// 角色基准光照半径（**格**）= 13 单位 × 1/10 格/单位 = 1.3 格（标定见文件头 ③）。
        /// </summary>
        public const float RadiusCells = 1.3f;

        /// <summary>
        /// 光照半径（**世界单位**）= 1.3 格 × `Iso.TileWorldWidth`(2.0) = **2.6**。
        /// </summary>
        public const float RadiusUnits = RadiusCells * Iso.TileWorldWidth;

        /// <summary>未知区域的告警是否已经说过（非预期分支只报一次）。</summary>
        private static bool _unknownAreaWarned;

        /// <summary>
        /// 该区域是否走光照半径压暗；是则一并给出**半径外要压到的颜色**（= 官方区域环境光）。
        /// <para>判据 = 官方 `Intensity/Red/Green/Blue` 是否全 0（全 0 = 官方"不设环境光"哨兵 ⇒ 全亮）。</para>
        /// <para>返回 true 时 <paramref name="ambient"/> = 官方环境光色（`Intensity == 0` ⇒ 黑）。</para>
        /// </summary>
        public static bool IsDark(AreaId area, out Color ambient)
        {
            switch (area)
            {
                // Levels.txt:3 `Act 1 - Town`：Intensity=0 Red=0 Green=0 Blue=0 ⇒ 官方未设环境光
                case AreaId.Town:
                // Levels.txt:4 `Act 1 - Wilderness 1`：同上
                case AreaId.BloodMoor:
                    ambient = Color.black;
                    return false;

                // Levels.txt:10 `Act 1 - Cave 1`：Intensity=0 Red=255 Green=255 Blue=255
                //   ⇒ 设了环境光，但强度 0 ⇒ 半径外压到黑
                case AreaId.DenOfEvil:
                    ambient = new Color(0f, 0f, 0f, 1f);
                    return true;

                default:
                    ambient = Color.black;
                    if (!_unknownAreaWarned)
                    {
                        _unknownAreaWarned = true;
                        MapLog.Warn($"AreaLighting.IsDark: 区域 {area} 不在光照表内（罗格营地 / 血腥荒野 / " +
                                    "邪恶洞穴三处之外）⇒ 不做压暗；新增区域时按官方 Levels.txt 的 " +
                                    "Intensity/Red/Green/Blue 补一行");
                    }
                    return false;
            }
        }

        /// <summary>
        /// 距角色 <paramref name="distanceUnits"/> 处的遮蔽不透明度（世界单位的圆形半径）：
        /// 0 = 全亮（不压暗）、1 = 压到区域环境光色。
        /// <para>线性衰减（实测剖面的性质 = 单调下降、无平台；见文件头 ④）。</para>
        /// </summary>
        public static float ShadowAlphaAt(float distanceUnits)
        {
            var t = distanceUnits / RadiusUnits;
            if (t <= 0f) return 0f;
            return t >= 1f ? 1f : t;
        }
    }
}
