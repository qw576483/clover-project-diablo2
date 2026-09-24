// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · Module/Monster/AreaLevelTable.cs
// **`Def.AreaId` → `level_c` 配表行**的唯一解析入口。
//
// 为什么需要它（**契约不一致**）：
//   `Def.AreaId`（`Def/Enums.cs`）是 **0 基**：`Town=0 / BloodMoor=1 / DenOfEvil=2`，
//   注释写着"取值 = 配表 `level_c` 的主键"；
//   而打表产物 `level_c` 的 `id` 是 **1 基**：`1=罗格营地(Rogue Encampment) / 2=血腥荒野(Blood Moor)
//   / 3=邪恶洞穴(Den of Evil)`（见 `client/Assets/Scripts/Table/Tsv/Level.tsv`）。
//   ⇒ `Level.Get((int)AreaId.BloodMoor)` 会拿到**罗格营地**那一行。
//
// 处置（不改契约、不改配表、不猜数字）：
//   本类按**数据自证**的方式解析，并同时兼容两种口径：
//     ① 先试 `id = (int)area + 1`（当前数据：1 基）——并用官方 `LevelName` 校验；
//     ② 再试 `id = (int)area`（若将来契约对齐为 0 基，这里自动切回，无需改业务）；
//     ③ 都不匹配 ⇒ 按官方 `LevelName` 全表搜一遍；
//     ④ 仍找不到 ⇒ 返回 null 并打 Error（调用方按"无怪"降级，且日志可定位）。
//   校验用的 `LevelName` 是**官方 `Levels.txt` 的关卡名**（契约哨兵，不是玩法数值）。
// ─────────────────────────────────────────────────────────────────────────────

using Diablo2.Def;

namespace Diablo2.Module.Monster
{
    /// <summary>`AreaId` → `level_c` 行（含 0 基/1 基两种口径的兼容解析）。</summary>
    internal static class AreaLevelTable
    {
        /// <summary>解析某区域的 `level_c` 行；找不到返回 null。</summary>
        /// <param name="area">区域。</param>
        /// <param name="warnOnOffByOne">是否就"0 基枚举 vs 1 基主键"这件事说明一次（只报一次）。</param>
        public static Table.BaseLevelRow Resolve(AreaId area, bool warnOnOffByOne = true)
        {
            var expected = ExpectedLevelNameOf(area);

            // ① 当前数据口径：level_c.id = (int)AreaId + 1
            var plusOne = Table.Tables.Default.Level.Get((int)area + 1);
            if (Matches(plusOne, expected))
            {
                if (warnOnOffByOne)
                {
                    MonsterLog.WarnOnce("level.offbyone",
                        $"AreaLevelTable：`Def.AreaId`（0 基，{area}={(int)area}）与 `level_c.id`（1 基）**差 1**" +
                        $"，已按 id={(int)area + 1} 解析到「{plusOne.Name}」。" +
                        "（契约不一致：`Def/Enums.cs` 的 AreaId 注释写「取值 = 配表 level_c 的主键」，" +
                        "但实际数据是 1 基；已回报主 agent，本类两种口径都兼容）");
                }
                return plusOne;
            }

            // ② 若将来契约对齐为 0 基
            var same = Table.Tables.Default.Level.Get((int)area);
            if (Matches(same, expected))
            {
                MonsterLog.WarnOnce("level.same",
                    $"AreaLevelTable：按 id={(int)area}（0 基口径）解析到「{same.Name}」——" +
                    "说明 level_c 的主键已对齐为与 AreaId 同值（契约修好了）");
                return same;
            }

            // ③ 按官方关卡名全表搜
            var all = Table.Tables.Default.Level.All();
            if (all != null)
            {
                for (var i = 0; i < all.Count; i++)
                {
                    if (Matches(all[i], expected))
                    {
                        MonsterLog.Warn($"AreaLevelTable：按 id 两种口径都没匹配上，改用 LevelName=\"{expected}\" 搜到" +
                                        $" id={all[i].Id}「{all[i].Name}」（配表 id 口径又变了？请核对）");
                        return all[i];
                    }
                }
            }

            MonsterLog.Error($"AreaLevelTable：level_c 里找不到区域 {area}（期望 LevelName=\"{expected}\"，" +
                             $"已试 id={(int)area} 与 id={(int)area + 1}）⇒ 该区域按【无怪】降级（配表/契约核对）");
            return null;
        }

        /// <summary>`AreaId` 对应的官方关卡名（`Levels.txt` 的 `LevelName`；契约哨兵）。</summary>
        public static string ExpectedLevelNameOf(AreaId area)
        {
            switch (area)
            {
                case AreaId.Town: return "Rogue Encampment";
                case AreaId.BloodMoor: return "Blood Moor";
                case AreaId.DenOfEvil: return "Den of Evil";
                default:
                    MonsterLog.WarnThrottled("level.area.unknown", $"ExpectedLevelNameOf: 未登记的区域 {(int)area}");
                    return null;
            }
        }

        /// <summary>行存在且（若有期望名）关卡名一致。</summary>
        private static bool Matches(Table.BaseLevelRow row, string expectedName)
        {
            if (row == null) return false;
            if (string.IsNullOrEmpty(expectedName)) return true;
            return string.Equals(row.LevelName, expectedName, System.StringComparison.OrdinalIgnoreCase);
        }
    }
}
