// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · Module/Audio/SfxRegistry.cs
// **音效键 → 期望文件名 的唯一登记表**（全项目只有这一份）。
//
// 为什么要有它：`IAudioModule.Sfx/SfxAt/Bgm(string key, …)` 的语义是「**键就是资源名**」
//   —— 引擎按 `Sound/SFX/{键}` 与 `Sound/BGM/{键}` 取资源（`Runtime/Presentation/Sound.cs:107/63`），
//   所以「键 ↔ 素材文件名」的对应关系必须有一处**可检索、可自检**的登记表：
//     · 素材到位前：`client/资源欠缺清单.md` 的清单由本表生成（键名 + 期望 `.wav` 文件名）；
//     · 素材到位后：**只改本文件的字符串值**，不动任何触发点（`AudioHook` / 战斗管线）。
//
// 键名对齐（硬要求）：**战斗/技能键的值一律取自 `Module/Combat/SfxKeys.cs`**（`Combat.SfxKeys.*`），
//   本文件**不复制**那些字面量、也不另起一套键名 —— 单一来源，改名只需改 `SfxKeys.cs`。
//   `Combat.SfxKeys` 经**命名空间解析**（`Diablo2.Module.Combat`）取得，**不写 `using`**：
//   分层自检 ② 要求 `Module/*` 里 0 处 `using Diablo2.Module.*`（跨模块协作走事件或 App 注入接口）。
//
// 除战斗键外的键（脚步/拾取/UI/传送/进图/BGM）**本项目新增**，属音频模块自有，与 `SfxKeys` 无重名。
//
// ⛔ 素材未到位时本表只用于「登记 + 缺失降级」，**不许引入任何非暗黑2 的音频**。
// ─────────────────────────────────────────────────────────────────────────────

using System;
using System.Collections.Generic;
using System.Text;
using Diablo2.Def;

namespace Diablo2.Module.Audio
{
    /// <summary>音效/BGM 键登记表（键 = 资源名；文件 = 期望的素材文件名）。</summary>
    internal static class SfxRegistry
    {
        // ═════════════════════════════════════════════════════════════════════
        // 战斗 / 技能键 —— **值直接取自 `Module/Combat/SfxKeys.cs`**（不写第二份字面量）
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>命中（挥砍打到目标）。触发：`Module/Combat/DamagePipeline.cs`。</summary>
        public const string Hit = Combat.SfxKeys.Hit;

        /// <summary>未命中（挥空）。触发：`DamagePipeline`。</summary>
        public const string Miss = Combat.SfxKeys.Miss;

        /// <summary>玩家受伤。触发：`DamagePipeline`。</summary>
        public const string PlayerHurt = Combat.SfxKeys.PlayerHurt;

        /// <summary>玩家死亡。触发：`DamagePipeline`。</summary>
        public const string PlayerDie = Combat.SfxKeys.PlayerDie;

        /// <summary>玩家复活。触发：`Events.Revived`（复活**完成**，见 `AudioHook`）。</summary>
        public const string PlayerRevive = Combat.SfxKeys.PlayerRevive;

        /// <summary>怪物死亡。触发：`DamagePipeline`。</summary>
        public const string MonsterDie = Combat.SfxKeys.MonsterDie;

        /// <summary>怪物攻击。触发：`Module/Monster/MonsterModule.cs`。</summary>
        public const string MonsterAttack = Combat.SfxKeys.MonsterAttack;

        /// <summary>萨满复活同伴。触发：`Module/Monster/MonsterModule.cs`。</summary>
        public const string MonsterRevive = Combat.SfxKeys.MonsterRevive;

        /// <summary>技能施放（通用回落）。触发：`Module/Skill/SkillModule.cs`。</summary>
        public const string Cast = Combat.SfxKeys.Cast;

        /// <summary>火系技能。触发：`SkillModule`（按 `DamageType` 选键）。</summary>
        public const string CastFire = Combat.SfxKeys.CastFire;

        /// <summary>冰系技能。触发：`SkillModule`。</summary>
        public const string CastCold = Combat.SfxKeys.CastCold;

        /// <summary>电系技能。触发：`SkillModule`。</summary>
        public const string CastLightning = Combat.SfxKeys.CastLightning;

        /// <summary>毒系技能。触发：`SkillModule`。</summary>
        public const string CastPoison = Combat.SfxKeys.CastPoison;

        /// <summary>升级。触发：`Events.LevelUp`（见 `AudioHook`）。</summary>
        public const string LevelUp = Combat.SfxKeys.LevelUp;

        // ═════════════════════════════════════════════════════════════════════
        // 本项目新增：音频模块自有的音效键（与 `SfxKeys` 无重名，`ValidateUnique` 会校验）
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>脚步（按移动速度节流）。触发：`Events.PlayerGridChanged`。</summary>
        public const string Footstep = "footstep";

        /// <summary>拾取物品。触发：`Events.ItemPicked`（非金币）。</summary>
        public const string ItemPickup = "item_pickup";

        /// <summary>拾取金币。触发：`Events.ItemPicked`（`ItemStack.isGold`）。</summary>
        public const string GoldPickup = "gold_pickup";

        /// <summary>使用物品（药水 / 卷轴）。触发：`Events.ItemUsed`。</summary>
        public const string ItemUse = "item_use";

        /// <summary>UI 点击。触发：`Events.PanelToggleRequest` / `Events.DialogOptionChosen`。</summary>
        public const string UiClick = "ui_click";

        /// <summary>NPC 对话开始。触发：`Events.DialogOpen`。</summary>
        public const string DialogOpen = "dialog_open";

        /// <summary>商店打开。触发：`Events.ShopOpen`。</summary>
        public const string ShopOpen = "shop_open";

        /// <summary>传送 / 踩出入口。触发：`Events.ExitEntered`。</summary>
        public const string Portal = "portal";

        /// <summary>进图（进入 Stage）。触发：`Events.StageEntered`。</summary>
        public const string AreaEnter = "area_enter";

        /// <summary>任务完成。触发：`Events.QuestCompleted`。</summary>
        public const string QuestComplete = "quest_complete";

        // ═════════════════════════════════════════════════════════════════════
        // 本项目新增：BGM 键（原版每区域一首；素材来自 `d2music.mpq`）
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>罗格营地（固定布局城镇）。</summary>
        public const string BgmTown = "town";

        /// <summary>血腥荒野（随机野外）。</summary>
        public const string BgmBloodMoor = "bloodmoor";

        /// <summary>邪恶洞穴（随机地牢）。</summary>
        public const string BgmDenOfEvil = "denofevil";

        /// <summary>SFX 期望扩展名（`Resources` 按名取、扩展名无关；此处仅供"素材清单"落地文件用）。</summary>
        public const string SfxExtension = ".wav";

        /// <summary>BGM 期望扩展名（`.wav`/`.ogg` 均可，引擎按名取）。</summary>
        public const string BgmExtension = ".wav";

        // ── 登记表本体 ───────────────────────────────────────────────────────
        private static readonly Dictionary<string, string> SfxFiles =
            new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // 战斗 / 技能（触发点见各 const 的注释；均为**已有代码**里的直连调用）
            { Hit, "hit" + SfxExtension },
            { Miss, "miss" + SfxExtension },
            { PlayerHurt, "player_hurt" + SfxExtension },
            { PlayerDie, "player_die" + SfxExtension },
            { PlayerRevive, "player_revive" + SfxExtension },
            { MonsterDie, "monster_die" + SfxExtension },
            { MonsterAttack, "monster_attack" + SfxExtension },
            { MonsterRevive, "monster_revive" + SfxExtension },
            { Cast, "cast" + SfxExtension },
            { CastFire, "cast_fire" + SfxExtension },
            { CastCold, "cast_cold" + SfxExtension },
            { CastLightning, "cast_lightning" + SfxExtension },
            { CastPoison, "cast_poison" + SfxExtension },
            { LevelUp, "level_up" + SfxExtension },

            // 本项目新增（触发点全部在 `AudioHook`，经 `Game.Event` 订阅）
            { Footstep, "footstep" + SfxExtension },
            { ItemPickup, "item_pickup" + SfxExtension },
            { GoldPickup, "gold_pickup" + SfxExtension },
            { ItemUse, "item_use" + SfxExtension },
            { UiClick, "ui_click" + SfxExtension },
            { DialogOpen, "dialog_open" + SfxExtension },
            { ShopOpen, "shop_open" + SfxExtension },
            { Portal, "portal" + SfxExtension },
            { AreaEnter, "area_enter" + SfxExtension },
            { QuestComplete, "quest_complete" + SfxExtension },
        };

        private static readonly Dictionary<string, string> BgmFiles =
            new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { BgmTown, "town" + BgmExtension },
            { BgmBloodMoor, "bloodmoor" + BgmExtension },
            { BgmDenOfEvil, "denofevil" + BgmExtension },
        };

        // ═════════════════════════════════════════════════════════════════════
        // 素材出处表（键 → 原版条目名 │ 原版 mpq 内路径）
        //
        // 素材已到位（`Agent` 第 4 步）：磁盘上的文件名 = **键名 + `.wav`**，
        // 内容 = 下表里的**原版 `.wav` 原字节**（未重采样 / 未转码 / 未裁剪）。
        // 本表**只用于"可追溯 + 自检"**（证明每个键都对上了暗黑2 原版素材），
        //   **不参与播放逻辑** —— 播放永远走 `SfxFiles`/`BgmFiles` 的"键即资源名"。
        //
        // 名字来源（不是记忆）：`data/global/excel/Sounds.txt`（1.10 版，LOD）的
        //   第 1 列 `Sound`（原版条目名）/ 第 3 列 `FileName`（相对路径）；
        //   音效前缀 `data\global\sfx\`、音乐前缀 `data\global\music\` 已逐条用 StormLib 实测命中。
        // 取用口径：默认职业是亚马逊 ⇒ 玩家类音效取亚马逊；元素施法取法师；
        //   每区域 BGM 取原版该区域的音乐条目（`music_town_1` / `music_wilderness` / `music_caves`）。
        // 人类可读的完整对照表（含触发点）见同目录 `SoundMap.md`。
        // ═════════════════════════════════════════════════════════════════════

        private const string SfxMpq = "d2sfx.mpq:data\\global\\sfx\\";
        private const string MusMpq = "d2music.mpq:data\\global\\music\\";

        private static readonly Dictionary<string, string> Origins =
            new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // ── 战斗 / 技能（原版条目名 │ 原版文件）──────────────────────────
            { Hit, "impact_blade_swing_1 │ " + SfxMpq + "combat\\impact\\sword1.wav" },
            { Miss, "weapon_1hs_small_1 │ " + SfxMpq + "combat\\weapon\\one hand swing small01.wav" },
            { PlayerHurt, "amazon_hit_1 │ " + SfxMpq + "combat\\player\\amazon\\soft4.wav" },
            { PlayerDie, "amazon_death_1 │ " + SfxMpq + "combat\\player\\amazon\\death1.wav" },
            { PlayerRevive, "necromancer_revive_target │ " + SfxMpq + "skill\\necromancer\\revivetarget.wav" },
            { MonsterDie, "fallen_death_1 │ " + SfxMpq + "monster\\fallen\\death1.wav" },
            { MonsterAttack, "fallen_attack_1 │ " + SfxMpq + "monster\\fallen\\roar1.wav" },
            { MonsterRevive, "fallenshaman_resurrect │ " + SfxMpq + "monster\\fallenshaman\\resurrect.wav" },
            { Cast, "amazon_magicarrow_1 │ " + SfxMpq + "skill\\amazon\\magicarrow1.wav" },
            { CastFire, "monster_cast_fire │ " + SfxMpq + "skill\\sorceress\\firecast.wav" },
            { CastCold, "monster_cast_cold │ " + SfxMpq + "skill\\sorceress\\coldcast.wav" },
            { CastLightning, "monster_cast_lightning │ " + SfxMpq + "skill\\sorceress\\eleccast.wav" },
            { CastPoison, "amazon_cast_poison │ " + SfxMpq + "skill\\amazon\\poisoncast.wav" },
            { LevelUp, "cursor_level_up │ " + SfxMpq + "cursor\\levelup.wav" },

            // ── 本项目新增的音效键（原版没有同名条目 ⇒ 取语义最近的**原版音**）──
            { Footstep, "light_walk_dirt_1 │ " + SfxMpq + "ambient\\footstep\\LightDirt1.wav" },
            { ItemPickup, "item_pickup │ " + SfxMpq + "cursor\\pickup.wav" },
            { GoldPickup, "item_gold │ " + SfxMpq + "item\\gold.wav" },
            { ItemUse, "item_potion_drink │ " + SfxMpq + "item\\potiondrink.wav" },
            { UiClick, "cursor_button_click │ " + SfxMpq + "cursor\\button.wav" },
            { DialogOpen, "cursor_select（原版点选音）│ " + SfxMpq + "cursor\\select.wav" },
            { ShopOpen, "cursor_error / cursor_switch（原版开窗口音）│ " + SfxMpq + "cursor\\windowopen.wav" },
            { Portal, "player_townportal_cast │ " + SfxMpq + "skill\\misc\\portalcast.wav" },
            { AreaEnter, "object_stairs │ " + SfxMpq + "object\\stairs.wav" },
            { QuestComplete, "cairn_success（原版任务完成音）│ " + SfxMpq + "object\\cairnsuccess.wav" },

            // ── BGM（原版每区域一首）──────────────────────────────────────────
            { BgmTown, "music_town_1 │ " + MusMpq + "act1\\town1.wav" },
            { BgmBloodMoor, "music_wilderness │ " + MusMpq + "act1\\wild.wav" },
            { BgmDenOfEvil, "music_caves │ " + MusMpq + "act1\\caves.wav" },
        };

        // ── 查询 ────────────────────────────────────────────────────────────

        /// <summary>该键是否是已登记的音效键。</summary>
        public static bool IsSfx(string key)
        {
            return !string.IsNullOrEmpty(key) && SfxFiles.ContainsKey(key);
        }

        /// <summary>该键是否是已登记的 BGM 键。</summary>
        public static bool IsBgm(string key)
        {
            return !string.IsNullOrEmpty(key) && BgmFiles.ContainsKey(key);
        }

        /// <summary>该音效键期望的素材文件名（未登记返回 null）。</summary>
        public static string SfxFileName(string key)
        {
            return key != null && SfxFiles.TryGetValue(key, out var file) ? file : null;
        }

        /// <summary>该 BGM 键期望的素材文件名（未登记返回 null）。</summary>
        public static string BgmFileName(string key)
        {
            return key != null && BgmFiles.TryGetValue(key, out var file) ? file : null;
        }

        /// <summary>
        /// 该键素材的**原版出处**（"原版条目名 │ 原版 mpq 内路径"；未登记返回 null）。
        /// 仅供可追溯与自检（`ValidateOrigins` / 离线宿主），**播放逻辑不使用它**。
        /// </summary>
        public static string Origin(string key)
        {
            return key != null && Origins.TryGetValue(key, out var o) ? o : null;
        }

        /// <summary>已登记的全部音效键（只读；顺序不保证）。</summary>
        public static IReadOnlyCollection<string> AllSfxKeys => SfxFiles.Keys;

        /// <summary>已登记的全部 BGM 键（只读；顺序不保证）。</summary>
        public static IReadOnlyCollection<string> AllBgmKeys => BgmFiles.Keys;

        /// <summary>
        /// 区域 → BGM 键。未登记的区域返回 null（调用方打**一次** Warn 并跳过切歌，不抛异常）。
        /// </summary>
        public static string BgmKeyOf(AreaId area)
        {
            switch (area)
            {
                case AreaId.Town: return BgmTown;
                case AreaId.BloodMoor: return BgmBloodMoor;
                case AreaId.DenOfEvil: return BgmDenOfEvil;
                default: return null;
            }
        }

        /// <summary>
        /// 登记自检：键集合**无重复值**（两个 SfxKeys 写成同一字符串 = 静默互覆，最难查）。
        /// 返回 null = 通过；否则返回可定位的错误串。
        /// </summary>
        public static string ValidateUnique()
        {
            var seen = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var kv in SfxFiles)
            {
                if (seen.TryGetValue(kv.Key, out var prev))
                    return $"音效键重复：\"{kv.Key}\" 与 \"{prev}\" 取了同一个键名 ⇒ 会互相覆盖";
                seen[kv.Key] = kv.Value;
            }

            seen.Clear();
            foreach (var kv in BgmFiles)
            {
                if (seen.TryGetValue(kv.Key, out var prev))
                    return $"BGM 键重复：\"{kv.Key}\" 与 \"{prev}\" 取了同一个键名 ⇒ 会互相覆盖";
                seen[kv.Key] = kv.Value;
            }

            // 同一个字符串不可同时是 SFX 与 BGM（会让 Bgm/Sfx 的登记语义互串）
            foreach (var key in BgmFiles.Keys)
                if (SfxFiles.ContainsKey(key))
                    return $"键名冲突：\"{key}\" 同时登记为 SFX 与 BGM";

            return null;
        }

        /// <summary>
        /// 素材出处自检：**每个已登记的键都必须写明原版出处**（`Origins`），
        /// 否则素材来源就不可追溯（= 可能混进了非暗黑2 的音频）。
        /// 返回 null = 通过；否则返回可定位的错误串（列出缺出处的键）。
        /// </summary>
        public static string ValidateOrigins()
        {
            var missing = new StringBuilder();
            foreach (var key in SfxFiles.Keys)
                if (!Origins.ContainsKey(key)) missing.Append(missing.Length == 0 ? string.Empty : " / ").Append(key);
            foreach (var key in BgmFiles.Keys)
                if (!Origins.ContainsKey(key)) missing.Append(missing.Length == 0 ? string.Empty : " / ").Append(key);

            if (missing.Length > 0) return "以下音效键没有登记原版出处（`Origins`）：" + missing;

            // 反向：出处表里不许有"已删掉的键"（残留 = 过期事实，比没有更糟）。
            var stale = new StringBuilder();
            foreach (var key in Origins.Keys)
                if (!SfxFiles.ContainsKey(key) && !BgmFiles.ContainsKey(key))
                    stale.Append(stale.Length == 0 ? string.Empty : " / ").Append(key);
            if (stale.Length > 0) return "`Origins` 里有未登记的音效键（过期残留）：" + stale;

            return null;
        }

        /// <summary>
        /// 登记表全文（启动时打一条 Info；也是 `client/资源欠缺清单.md` 音效清单的来源）。
        /// </summary>
        public static string Dump()
        {
            var sb = new StringBuilder();
            sb.Append("SFX ").Append(SfxFiles.Count).Append(" 键：");
            var first = true;
            foreach (var kv in SfxFiles)
            {
                sb.Append(first ? " " : " / ").Append(kv.Key).Append("→").Append(kv.Value);
                first = false;
            }

            sb.Append("　BGM ").Append(BgmFiles.Count).Append(" 键：");
            first = true;
            foreach (var kv in BgmFiles)
            {
                sb.Append(first ? " " : " / ").Append(kv.Key).Append("→").Append(kv.Value);
                first = false;
            }

            return sb.ToString();
        }
    }
}
