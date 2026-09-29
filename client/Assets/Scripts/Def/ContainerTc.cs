// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · Def/ContainerTc.cs  本项目新增
//
// 容器掉落的**档位映射**：等级 → 官方 `Act 1 Chest A/B/C`。放 `Def` 的原因见 `ContainerArgs.cs` 文件头
// （地图侧要打日志、物品侧要挑表，两侧都不能 `using` 对方）。
//
// 出处（⛔ 不是自创门限）：官方 `TreasureClassEx.txt` 的三行
//   `Act 1 Chest A` / `Act 1 Chest B` / `Act 1 Chest C` 的 `level` 列 = **0 / 5 / 9**
//   （本项目已按普通难度 Act 1 范围导入到 `treasureclass_c`，逐行见 `策划/数值文档/treasureclass_c.txt`）。
//   ⇒ 门限就取这三行自己的 `level`：`< 5 ⇒ A`、`5..8 ⇒ B`、`≥ 9 ⇒ C`。
//   等级从哪来：原版按**区域等级**；本项目没有导入区域等级列 ⇒ 用操作者（玩家）等级代
//   （口径只写在这一处 + `MapModule` 的破坏日志会把实际用的值打出来）。
// ─────────────────────────────────────────────────────────────────────────────
namespace Diablo2.Def
{
    /// <summary>容器 TC 档位（见文件头）。</summary>
    internal static class ContainerTc
    {
        /// <summary>`Act 1 Chest B` 的官方 `level`（门限下界）。</summary>
        public const int TierBLevel = 5;

        /// <summary>`Act 1 Chest C` 的官方 `level`（门限下界）。</summary>
        public const int TierCLevel = 9;

        /// <summary>该等级用哪一档（0/1/2 = `Act 1 Chest A/B/C`）。</summary>
        public static int TierOf(int level)
        {
            if (level >= TierCLevel) return 2;
            if (level >= TierBLevel) return 1;
            return 0;
        }

        /// <summary>档位 → 官方 TC 名。</summary>
        public static string NameOf(int tier)
        {
            switch (tier)
            {
                case 0: return "Act 1 Chest A";
                case 1: return "Act 1 Chest B";
                default: return "Act 1 Chest C";
            }
        }
    }
}
