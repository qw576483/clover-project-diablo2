// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · Module/Npc/NpcDialog.cs
// **NPC 对话文本**：按任务阶段切换（阿卡拉是任务发布者，4 个阶段 4 段文本）。
//
// ★★ 文本来源（2026「对话面板 = 原版串」轮）—— **画面文案 0 条自写**，全部取原版串表：
//   · 串表：`原版资源/d2text/chi_string.txt`（格式 `id<TAB>[<换行数>\n]<文本>`，换行是字面 `\n`）；
//   · 键名对照：`原版资源/参考工程_Diablierie/Diablerie/Assets/StreamingAssets/data/local/string.txt`
//     （行号 = 串 id，`Key<TAB>值`）。
//   ⚠️ 原版中文是**繁体**，所以下面的字面量就是繁体 —— 这是对的（片 3 已钉死）。
//
//   **任务对话（键名规律 `A1Q1{阶段}{NPC}`，阶段 = Init / AfterInit / EarlyReturn / Successful）**：
//     阿卡拉：`A1Q1InitAkara` **64** / `A1Q1AfterInitAkara` **65** /
//             `A1Q1EarlyReturnAkara` **71** / `A1Q1SuccessfulAkara` **76**
//     卡夏  ：`A1Q1AfterInitKashya` **66** / `A1Q1EarlyReturnKashya` **72** / `A1Q1SuccessfulKashya` **77**
//     恰西  ：`A1Q1AfterInitCharsiMain` **67** / `A1Q1EarlyReturnCharsi` **73** / `A1Q1SuccessfulCharsi` **78**
//     基得  ：`A1Q1AfterInitGheed` **69** / `A1Q1EarlyReturnGheed` **74** / `A1Q1SuccessfulGheed` **79**
//     瓦瑞夫：`A1Q1AfterInitWarriv` **70** / `A1Q1EarlyReturnWarriv` **75** / `A1Q1SuccessfulWarriv` **80**
//
//   **本文件选用的那一句（逐条给理由）**：
//     · 阿卡拉 未接取 → **64**（原版给任务的整段话）／进行中 → **71**（= "回城搭话但还没清完"，
//       正是本面板每次打开时的场景）／可交付 & 已完成 → **76**（原版串表里阿卡拉在任务 1 之后
//       **没有**独立的"已完成"台词，`A1Q1SuccessfulAkara` 就是她关于本任务的最后一句 ⇒ 两态共用，
//       **不编新台词**）。⚠️ 同阶段的 **65**（`A1Q1AfterInitAkara`，接受任务后紧接着说的追加句）
//       本项目没有单独的状态位（只有 4 态）⇒ 未使用，出处照记。
//     · 其余 4 人：任务未完成 → `A1Q1AfterInit*`（**66/67/69/70**），已完成 → `A1Q1Successful*`（**77/78/79/80**）。
//       （`EarlyReturn*` **72/73/74/75** 是同阶段的另一句"还没清完？"版，本项目 2 分支状态位下未使用，
//        出处照记，供后续把状态位细分时替换。）
//
//   ⚠️ **原版串里的硬换行不保留**：它们是按原版 800×600 的框宽排的（每行 ~29 字），而本工程对话石框
//      的正文宽 ≈ 347 画布px（≈26 个汉字）⇒ 保留硬换行会让每行只占半宽，实测最长的 64 号串会从
//      **8 行涨到 15 行**、溢出石框。⇒ 段内**合并**（换行交给按宽换行：算法 = 原版
//      `D2WINTEXTBOX_WordWrapAndSetText @0x4fcda0` 口径，见 `UI/D2Text.WrapLines`），
//      **段间的空行保留**（原版的那一行"空格行"就是段落分隔）。登记见 `策划/验收表.md` 的「允许的差异」。
//
// 选项约定（`Def.NpcDialogArgs.options` + `Events.DialogOptionChosen`）：
//   **下标 0 恒为"离开（关闭）"**（契约：`ChooseOption` 的 optionIndex 0 = 关闭），
//   其余动作项按顺序追加：任务动作 → 商店入口。NpcModule.ChooseOption 用同一套规则反解。
//   ⛔ 选项文案同样**只许用原版串**（原版串表里 `NPCMenu*` 全族：
//     `NPCMenuLeave` **3394**「離開」/ `NPCMenuNews0` **3386**「重要消息」/
//     `NPCMenuTradeRepair` **3334**「交易/修理」/ `NPCMenuTrade` **3396**「交易」
//     —— 原版**没有**"接受任务 / 交付任务 / 结束对话"这些按钮字样，那是本项目上一版自写的）。
// ─────────────────────────────────────────────────────────────────────────────

using System.Collections.Generic;
using Diablo2.Def;

namespace Diablo2.Module.Npc
{
    /// <summary>对话文本 + 选项组装（纯函数，不碰事件）。</summary>
    internal sealed class NpcDialog
    {
        // ═════════════════════════════════════════════════════════════════════
        // 选项文案（**原版串**）
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>关闭选项文本（恒为下标 0）= 原版串 **3394**（键 `NPCMenuLeave`）。</summary>
        public const string OptionClose = "離開";

        /// <summary>
        /// 任务动作项（接取 / 交付共用）= 原版串 **3386**（键 `NPCMenuNews0`，原版 NPC 菜单族里
        /// "关于本任务的消息"那一项；`NPCMenuNews0..4` 共 5 项，本项目只做 Act I 第 1 个任务 ⇒ 取第 0 项）。
        /// <para>两头（接取态 / 交付态）**不会同屏**（`canAcceptQuest` 与 `canTurnInQuest` 互斥）⇒ 同一条串不会造成歧义。</para>
        /// </summary>
        public const string OptionQuestNews = "重要消息";

        /// <summary>接任务选项文本（= <see cref="OptionQuestNews"/>）。</summary>
        public const string OptionAcceptQuest = OptionQuestNews;

        /// <summary>交任务选项文本（= <see cref="OptionQuestNews"/>）。</summary>
        public const string OptionTurnInQuest = OptionQuestNews;

        /// <summary>铁匠（恰西）的商店入口文本 = 原版串 **3334**（键 `NPCMenuTradeRepair`）。</summary>
        public const string OptionShopBlacksmith = "交易/修理";

        /// <summary>其它商人的商店入口文本 = 原版串 **3396**（键 `NPCMenuTrade`）。</summary>
        public const string OptionShopGoods = "交易";

        // ═════════════════════════════════════════════════════════════════════
        // 台词（**原版串**；段落内的硬换行已按文件头口径合并）
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>阿卡拉·未接取 = 原版串 **64**（键 `A1Q1InitAkara`）。</summary>
        public const string AkaraNotStarted =
            "在荒地中有一個極度邪惡的地方。卡夏的蘿格斥候已經告訴我們那個洞穴附近到處都是影子般的生物，以及從墳墓中爬出來的怪物。"
            + "\n \n"
            + "我害怕這些生物會群聚並攻擊我們的營地。如果你真的要幫助我們，找到這個黑暗的迷宮並摧毀所有邪惡的怪物。"
            + "\n \n"
            + "願偉大之眼眷顧你們。";

        /// <summary>阿卡拉·进行中 = 原版串 **71**（键 `A1Q1EarlyReturnAkara`）。</summary>
        public const string AkaraInProgress =
            "除非你殺死這個洞窟中的所有惡魔，你的任務就不算完成。";

        /// <summary>阿卡拉·可交付 / 已完成 = 原版串 **76**（键 `A1Q1SuccessfulAkara`）。</summary>
        public const string AkaraSuccessful =
            "你已經清除了洞窟中的邪惡，也贏得了我的信任，並回復了我對人類的信心。"
            + "\n \n"
            + "你的報酬是自行選擇一項全新的技能。";

        /// <summary>卡夏·任务未完成 = 原版串 **66**（键 `A1Q1AfterInitKashya`）。</summary>
        public const string KashyaBefore =
            "這個洞穴中的惡魔宣告我們之前最好的弓箭手都為他們效力。我懷疑你會不會打贏！";

        /// <summary>卡夏·已完成 = 原版串 **77**（键 `A1Q1SuccessfulKashya`）。</summary>
        public const string KashyaDone =
            "嗯。我對你能夠完成這項考驗十分驚訝，外地人。去找阿卡拉，她可能有些獎賞。";

        /// <summary>恰西·任务未完成 = 原版串 **67**（键 `A1Q1AfterInitCharsiMain`）。</summary>
        public const string CharsiBefore =
            "這個從洞穴而來的怪物開始在鄉村四處遊蕩，你最好出去時多加小心。";

        /// <summary>恰西·已完成 = 原版串 **78**（键 `A1Q1SuccessfulCharsi`）。</summary>
        public const string CharsiDone =
            "你真是英勇而技巧精湛的人...阿卡拉很擔心你們。";

        /// <summary>基得·任务未完成 = 原版串 **69**（键 `A1Q1AfterInitGheed`）。</summary>
        public const string GheedBefore =
            "你擁有勇敢的靈魂！我很快就會啟動我的聖杖，刺入最污穢、像紅寶石般的娼妓體內，然後踢進這個洞穴之中。";

        /// <summary>基得·已完成 = 原版串 **79**（键 `A1Q1SuccessfulGheed`）。</summary>
        public const string GheedDone =
            "我只能說，最好的惡魔就是死惡魔。"
            + "\n \n"
            + "對了，你有沒有在那個洞窟中找到願意出售的東西？";

        /// <summary>瓦瑞夫·任务未完成 = 原版串 **70**（键 `A1Q1AfterInitWarriv`）。</summary>
        public const string WarrivBefore =
            "不管是誰在找尋這個洞窟，都是在自尋死亡。";

        /// <summary>瓦瑞夫·已完成 = 原版串 **80**（键 `A1Q1SuccessfulWarriv`）。</summary>
        public const string WarrivDone =
            "...那些東西如果沒能殺了你，就會讓你更加強壯。";

        /// <summary>组装一份对话（<paramref name="npc"/> 必须非 null）。</summary>
        public static NpcDialogArgs Build(NpcDef npc, QuestState denState, int questId,
            bool canAcceptQuest, bool canTurnInQuest)
        {
            var args = new NpcDialogArgs
            {
                npcId = npc.id,
                npcName = npc.name,
                hasShop = npc.hasShop,
                canAcceptQuest = canAcceptQuest,
                canTurnInQuest = canTurnInQuest,
                questId = questId,
                options = new List<string>(),
            };

            args.text = TextOf(npc.id, denState);

            // 下标 0 = 关闭（契约）
            args.options.Add(OptionClose);
            if (canAcceptQuest) args.options.Add(OptionAcceptQuest);
            else if (canTurnInQuest) args.options.Add(OptionTurnInQuest);
            if (npc.hasShop) args.options.Add(npc.isBlacksmith ? OptionShopBlacksmith : OptionShopGoods);

            return args;
        }

        /// <summary>某 NPC 在某任务阶段的对话正文（**逐句都是原版串**，串 id 见上面各常量）。</summary>
        public static string TextOf(int npcId, QuestState denState)
        {
            var done = denState == QuestState.Done;

            switch ((NpcId)npcId)
            {
                case NpcId.Akara:
                    switch (denState)
                    {
                        case QuestState.NotStarted:
                            return AkaraNotStarted;              // 原版串 64
                        case QuestState.InProgress:
                            return AkaraInProgress;              // 原版串 71
                        case QuestState.ReadyToTurnIn:
                            return AkaraSuccessful;              // 原版串 76
                        case QuestState.Done:
                            return AkaraSuccessful;              // 原版串 76（她关于本任务的最后一句）
                    }
                    break;

                case NpcId.Kashya:
                    return done ? KashyaDone : KashyaBefore;     // 原版串 77 / 66
                case NpcId.Charsi:
                    return done ? CharsiDone : CharsiBefore;     // 原版串 78 / 67
                case NpcId.Gheed:
                    return done ? GheedDone : GheedBefore;       // 原版串 79 / 69
                case NpcId.Warriv:
                    return done ? WarrivDone : WarrivBefore;     // 原版串 80 / 70

                default:
                    return string.Empty;                          // 不编文案（名字由 NpcModule 给）
            }
            return string.Empty;
        }
    }
}
