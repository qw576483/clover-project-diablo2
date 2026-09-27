// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · Module/Npc/NpcDialog.cs
// **NPC 对话文本**：每个 NPC 一句（原版 Act I 营地对话串）。
//
// 文本来源 —— **画面文案 0 条自写**，全部取原版串表：
//   · 串表：`原版资源/d2text/chi_string.txt`（格式 `id<TAB>[<换行数>\n]<文本>`，换行是字面 `\n`）；
//   · 键名对照：`原版资源/参考工程_Diablierie/Diablerie/Assets/StreamingAssets/data/local/string.txt`
//     （行号 = 串 id，`Key<TAB>值`）。
//
//   **原版串里的硬换行不保留**：它们是按原版 800×600 的框宽排的（每行 ~29 字），而本工程对话石框
//      的正文宽 ≈ 347 画布px（≈26 个汉字）⇒ 保留硬换行会让每行只占半宽，实测最长的 64 号串会从
//      **8 行涨到 15 行**、溢出石框。⇒ 段内**合并**（换行交给按宽换行：算法 = 原版
//      `D2WINTEXTBOX_WordWrapAndSetText @0x4fcda0` 口径，见 `UI/D2Text.WrapLines`），
//      **段间的空行保留**（原版的那一行"空格行"就是段落分隔）。登记见 `策划/验收表.md` 的「允许的差异」。
//
// 选项约定（`Def.NpcDialogArgs.options` + `Events.DialogOptionChosen`）：
//   **下标 0 恒为"离开（关闭）"**（契约：`ChooseOption` 的 optionIndex 0 = 关闭），
//   其余动作项按顺序追加：商店入口。NpcModule.ChooseOption 用同一套规则反解。
//   选项文案同样**只许用原版串**（原版串表里 `NPCMenu*` 全族：
//     `NPCMenuLeave` **3394**「離開」/ `NPCMenuTradeRepair` **3334**「交易/修理」/
//     `NPCMenuTrade` **3396**「交易」）。
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

        /// <summary>铁匠（恰西）的商店入口文本 = 原版串 **3334**（键 `NPCMenuTradeRepair`）。</summary>
        public const string OptionShopBlacksmith = "交易/修理";

        /// <summary>其它商人的商店入口文本 = 原版串 **3396**（键 `NPCMenuTrade`）。</summary>
        public const string OptionShopGoods = "交易";

        // ═════════════════════════════════════════════════════════════════════
        // 台词（**原版串**；段落内的硬换行已按文件头口径合并）
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>阿卡拉 = 原版串 **64**（键 `A1Q1InitAkara`）。</summary>
        public const string Akara =
            "在荒地中有一個極度邪惡的地方。卡夏的蘿格斥候已經告訴我們那個洞穴附近到處都是影子般的生物，以及從墳墓中爬出來的怪物。"
            + "\n \n"
            + "我害怕這些生物會群聚並攻擊我們的營地。如果你真的要幫助我們，找到這個黑暗的迷宮並摧毀所有邪惡的怪物。"
            + "\n \n"
            + "願偉大之眼眷顧你們。";

        /// <summary>卡夏 = 原版串 **66**（键 `A1Q1AfterInitKashya`）。</summary>
        public const string Kashya =
            "這個洞穴中的惡魔宣告我們之前最好的弓箭手都為他們效力。我懷疑你會不會打贏！";

        /// <summary>恰西 = 原版串 **67**（键 `A1Q1AfterInitCharsiMain`）。</summary>
        public const string Charsi =
            "這個從洞穴而來的怪物開始在鄉村四處遊蕩，你最好出去時多加小心。";

        /// <summary>基得 = 原版串 **69**（键 `A1Q1AfterInitGheed`）。</summary>
        public const string Gheed =
            "你擁有勇敢的靈魂！我很快就會啟動我的聖杖，刺入最污穢、像紅寶石般的娼妓體內，然後踢進這個洞穴之中。";

        /// <summary>瓦瑞夫 = 原版串 **70**（键 `A1Q1AfterInitWarriv`）。</summary>
        public const string Warriv =
            "不管是誰在找尋這個洞窟，都是在自尋死亡。";

        /// <summary>组装一份对话（<paramref name="npc"/> 必须非 null）。</summary>
        public static NpcDialogArgs Build(NpcDef npc)
        {
            var args = new NpcDialogArgs
            {
                npcId = npc.id,
                npcName = npc.name,
                hasShop = npc.hasShop,
                options = new List<string>(),
            };

            args.text = TextOf(npc.id);

            // 下标 0 = 关闭（契约）
            args.options.Add(OptionClose);
            if (npc.hasShop) args.options.Add(npc.isBlacksmith ? OptionShopBlacksmith : OptionShopGoods);

            return args;
        }

        /// <summary>某 NPC 的对话正文（**逐句都是原版串**，串 id 见上面各常量）。</summary>
        public static string TextOf(int npcId)
        {
            switch ((NpcId)npcId)
            {
                case NpcId.Akara:   return Akara;
                case NpcId.Kashya:  return Kashya;
                case NpcId.Charsi:  return Charsi;
                case NpcId.Gheed:   return Gheed;
                case NpcId.Warriv:  return Warriv;
                default:            return string.Empty;      // 不编文案（名字由 NpcModule 给）
            }
        }
    }
}
