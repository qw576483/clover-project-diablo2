// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · Def/SceneInteractArgs.cs
// 「可交互场景物」的统一载荷（神殿 / 篝火 / 玩家回城门）—— 与 `Def/ContainerArgs.cs` 同一档：
// 放在 `Diablo2.Def` 是为了让**事件两端**（地图模块发、玩家模块 / UI 收）都能引用，
// 且不必让任一方 `using` 对方的模块命名空间。
//
// 三类物件的官方出处（逐条，见各自常量 / 表）：
//   · 神殿   = 官方 `objects.txt` Id **81**（`AF` ForestAltar）/ Id **83**（`HS` HornShrine），
//              `Name` 列都是 `Shrine`；行为表 = 官方 `Shrines.txt`（见 `Def/ShrineTable.cs`）。
//   · 篝火   = 官方 `objects.txt` Id **39**（`RB` RogueBonfire，`Name` = `fire`）。
//   · 回城门 = 官方 `objects.txt` Id **59**（`TP` Portal）+ 官方 `Misc.txt` 的
//              `Town Portal Scroll`（本工程物品表 `Item.tsv` 的 code = `tsc`，id 89）。
// ─────────────────────────────────────────────────────────────────────────────
using UnityEngine;

namespace Diablo2.Def
{
    /// <summary>可交互场景物的类别（与 `Diablo2.Def` 的其它枚举同档：只描述"是什么"）。</summary>
    public enum SceneObjectKind
    {
        /// <summary>神殿（官方 `Objects.txt` Id 81 / 83；效果 = 官方 `Shrines.txt` 的一行）。</summary>
        Shrine = 0,

        /// <summary>篝火（官方 `Objects.txt` Id 39 `RB` RogueBonfire）。</summary>
        Bonfire = 1,

        /// <summary>玩家回城门（官方 `Objects.txt` Id 59 `TP`；由 `Town Portal Scroll` 开启）。</summary>
        TownPortal = 2,
    }

    /// <summary>
    /// 一次"走到可交互场景物旁边并操作它"的载荷。
    /// <para>神殿那支带**官方 `Shrines.txt` 那一行**的全部取值（行号 + Effect/Arg0/Arg1/Duration），
    /// 由地图模块在**点击那一刻**按官方 `rarity` 权重掷出并写进本载荷 ⇒ 收方（玩家模块）不重新掷、
    /// 也不再读表，画面/数值/日志三者必然同一行。</para>
    /// </summary>
    public sealed class SceneInteractArgs
    {
        /// <summary>物件类别。</summary>
        public SceneObjectKind kind;

        /// <summary>物件所在的格。</summary>
        public Vector2Int cell;

        /// <summary>官方 `Shrines.txt` 的行号（0 = 表头下的 `None` 行；仅 <see cref="SceneObjectKind.Shrine"/> 有效）。</summary>
        public int shrineRow;

        /// <summary>官方 `Shrines.txt` 的 `Effect` 列（效果号）。</summary>
        public int effect;

        /// <summary>官方 `Shrines.txt` 的 `Arg0` 列。</summary>
        public int arg0;

        /// <summary>官方 `Shrines.txt` 的 `Arg1` 列。</summary>
        public int arg1;

        /// <summary>官方 `Shrines.txt` 的 `Duration in frames` 列（0 = 无时限）。</summary>
        public int durationFrames;

        /// <summary>官方 `Shrines.txt` 的 `Shrine name` 列的简体中文（日志 / 弹幕用）。</summary>
        public string name;

        /// <summary>官方 `Shrines.txt` 的 `Shrine Type` 列（`Recharge` / `Booster` / `Magic`）。</summary>
        public string shrineType;
    }
}
