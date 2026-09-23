// ─────────────────────────────────────────────────────────────────────────────
// ★ 片 dialog-options2（2026-09-24）：对话选项「可读性」+ 物品 tooltip「可见性」的离线判据
//
// 这一节判什么（对应用户/主 agent 报的两条表现类缺陷，把它们的**可离线部分**钉成真值表）：
//   ① 「NPC 对话选项按钮**一个字都没有**」（主 agent 读图
//      `.ai-tmp/screenshots/btnlabel_recap_g1_dialog_options.png` 的结论）。
//      ⇒ 本片实机复核后**部分推翻**：文案链没问题（运行时日志
//        `[Ui] 对话面板已刷新：NPC=阿卡拉 台词 54 字 选项 3 项=[離開 / 重要消息 / 交易]`），
//        按钮**有字**（第一项「離開」在实机图上清晰可读）；被糊住的是**鼠标悬停那一颗** ——
//        中等按钮的悬停底图 `D2/UI/Menu/btn_med_sel.png` 是**麻点图**（用错调色板的典型症状），
//        底板花斑把文案盖住。⇒「没字」与「被盖住」在画面上长得一样、且都不报错，
//        所以这里把**两类判据都钉住**：文案侧（Ⓐ-1/Ⓐ-2/Ⓐ-4c）+ 底图侧（Ⓐ-5）。
//      ⚠️ 2026-09-24 收口（片 **final-close**）：麻点图的**根因已修**（资产按 `fechar` 调色板重出
//        `0.424 → 0.054`；`UI/UiArt.cs` 中等分支的 `highlight` 已接回 `ResPaths.BtnMedSel`）
//        ⇒ Ⓐ-5 那一节**前提消失**，其三条判据已由「**记录缺陷**（坏图未接线 / 降级为常态帧）」
//           翻成「**守护修复**（悬停必须用高亮帧 / 接线的图必须是干净图）」，见下面 Ⓐ-5 节头。
//   ② 「离开背包后残留一块空框」（`.ai-tmp/test/report-playverify.md` §1.2）
//      ⇒ `ItemTooltip.ShouldBeVisible(panelOpen, pointerInsidePanel, hasItem)` 三条合取（Ⓐ-3），
//        并且**必须真被调用点用起来**（Ⓐ-4：防"孤立纯函数"—— 前片把这两个纯函数写完就死了，
//        正是这种失败模式）。
//
// ⚠️ 本文件是「**同一交付的唯一实现**」：2026-09-24 02:4x 出现过一次**并发互删**——
//    `V6Check.cs` 里曾有过一个**同名**类（由 `V6Check.Run()` 调用），本片为去重删了自己的文件，
//    对方随后也把 `V6Check.cs` 那份删了（两份都消失）⇒ 本文件重新落地，并且
//    `Program.cs` 的调用**放回 `Main` 的 Check 列表**（见 `DialogOptionsCheck.Run()` 那一行）。
//    ⛔ 若再有人要在别处实现同名类，请**先**删本文件的 Compile 行再动手，别让两份同时消失。
//
// ⛔ 本文件只**新增**断言，不改既有判据（`Program.cs` 里已有的 S1~S7 / ⑬ 节一个字都不动）。
// ⛔ 也不是"把断言写松"：下面每条都给了**具体的期望值**，并自带"已知错误样本必须红"的自证（Ⓐ-5c）。
// ─────────────────────────────────────────────────────────────────────────────

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using Diablo2.Core;
using Diablo2.UI;
using UnityEngine;

namespace Uicheck
{
    /// <summary>对话选项可读性 + tooltip 可见性（纯函数真值表 / 退化用例 / 调用点 / 坏图未接线）。</summary>
    internal static class DialogOptionsCheck
    {
        public static void Run()
        {
            Console.WriteLine("── Ⓐ 对话选项可读性 / tooltip 可见性（纯函数真值表 + 退化用例 + 坏图未接线）──");

            LabelTruthTable();
            ReadableDegenerateCases();
            TooltipTruthTable();
            CallSitesWired();
            MediumHighlightBadArt();
        }

        // ═════════════════════════════════════════════════════════════════════
        // Ⓐ-1 `NpcDialogPanel.IsReadableLabel` 真值表
        // ═════════════════════════════════════════════════════════════════════
        private static void LabelTruthTable()
        {
            var cases = new[]
            {
                new object[] { null, false },                    // 模块漏给（DTO 里 options[i] == null）
                new object[] { "", false },                      // 空串 = 画面上一个字的空按钮（本片要抓的形态）
                new object[] { " ", false },                     // 纯半角空白
                new object[] { "\t", false },
                new object[] { "\u3000", false },                // 全角空格（中文串表里真会出现）
                new object[] { "\u3000\u3000", false },
                new object[] { "離開", true },                    // 原版串 3394（NPCMenuLeave）
                new object[] { "交易", true },                    // 原版串 3396（NPCMenuTrade）
                new object[] { "交易/修理", true },                // 原版串 3334（NPCMenuTradeRepair）
                new object[] { "重要消息", true },                 // 原版串 3386（NPCMenuNews0）
                new object[] { " A ", true },                    // 有内容即算可读（不修剪）
            };

            var bad = "";
            foreach (var c in cases)
            {
                var text = (string)c[0];
                var want = (bool)c[1];
                var got = NpcDialogPanel.IsReadableLabel(text);
                if (got != want)
                    bad += "[" + (text ?? "<null>") + "]期望" + want + "实得" + got + " ";
            }

            Program.Check("Ⓐ-1：`IsReadableLabel` 真值表（空/null/纯空白 ⇒ 不可读；原版串 ⇒ 可读）",
                bad.Length == 0, bad.Length == 0 ? $"逐条一致（{cases.Length} 条）" : bad);
        }

        // ═════════════════════════════════════════════════════════════════════
        // Ⓐ-2 `NpcDialogPanel.IsReadable(text, fontSize, color)` 退化对
        //     口径：三条件合取（文案非空 + 字号 > 0 + 字色 alpha > 0）——
        //     退化用例 = 每次只让**一条**不成立，期望恰好由 true 翻成 false。
        //     `(int)UiLayoutGame.FontPx16` + `UiArt.ButtonText` 就是 `Rebuild` 的真实实参。
        // ═════════════════════════════════════════════════════════════════════
        private static void ReadableDegenerateCases()
        {
            var op = Color.white;
            var transparent = new Color(1f, 1f, 1f, 0f);
            var almost = new Color(1f, 1f, 1f, 0.01f);
            var font = (int)UiLayoutGame.FontPx16;

            var rows = new[]
            {
                new object[] { "離開", font, op, true, "真实实参（文案/字号/字色全成立）" },
                new object[] { "", font, op, false, "退化①文案空" },
                new object[] { null, font, op, false, "退化①文案 null" },
                new object[] { "離開", 0, op, false, "退化②字号 0" },
                new object[] { "離開", -1, op, false, "退化②字号负" },
                new object[] { "離開", font, transparent, false, "退化③字色 alpha 0（字画不出来）" },
                new object[] { "離開", font, almost, true, "边界：alpha 0.01 > 0 ⇒ 仍算可读（只判画不画得出）" },
                new object[] { "", 0, transparent, false, "三条同时不成立" },
            };

            var bad = "";
            foreach (var r in rows)
            {
                var got = NpcDialogPanel.IsReadable((string)r[0], (int)r[1], (Color)r[2]);
                var want = (bool)r[3];
                if (got != want)
                    bad += "[" + r[4] + "]期望" + want + "实得" + got + " ";
            }

            Program.Check("Ⓐ-2：`IsReadable` 退化对（空文案 / 字号 0 / 字色 alpha 0 各单独退化 ⇒ 全为 false）",
                bad.Length == 0, bad.Length == 0 ? $"逐条一致（{rows.Length} 条）" : bad);

            Program.Check("Ⓐ-2b：真实实参下「離開 / 重要消息 / 交易/修理」都可读（= Rebuild 实际喂的三条原版串）",
                NpcDialogPanel.IsReadable("離開", font, UiArt.ButtonText)
                && NpcDialogPanel.IsReadable("重要消息", font, UiArt.ButtonText)
                && NpcDialogPanel.IsReadable("交易/修理", font, UiArt.ButtonText),
                "fontPx=" + font + " color=" + UiArt.ButtonText);
        }

        // ═════════════════════════════════════════════════════════════════════
        // Ⓐ-3 `ItemTooltip.ShouldBeVisible` 全真值表（8 行）
        // ═════════════════════════════════════════════════════════════════════
        private static void TooltipTruthTable()
        {
            var bad = "";
            var rows = 0;
            for (var b = 0; b < 8; b++)
            {
                var open = (b & 1) != 0;
                var inside = (b & 2) != 0;
                var has = (b & 4) != 0;
                var want = open && inside && has;
                var got = ItemTooltip.ShouldBeVisible(open, inside, has);
                rows++;
                if (got != want)
                    bad += "(open=" + open + " inside=" + inside + " hasItem=" + has
                        + " 期望" + want + "实得" + got + ") ";
            }

            Program.Check("Ⓐ-3：`ShouldBeVisible` 8 行真值表（三条件合取：面板开 ∧ 指针在面板内 ∧ 格上有物品）",
                bad.Length == 0 && rows == 8, bad.Length == 0 ? "8 行逐条一致" : bad);

            // 「离开背包后残留空框」的两个**必隐**组合单独点名（回归时能一眼看出是哪一支破了）
            Program.Check("Ⓐ-3b：必隐的两支 —— ①指针移出面板 ②指针在面板内但格是空的",
                !ItemTooltip.ShouldBeVisible(true, false, true)
                && !ItemTooltip.ShouldBeVisible(true, true, false),
                "ShouldBeVisible(true,false,true)=false / ShouldBeVisible(true,true,false)=false");
        }

        // ═════════════════════════════════════════════════════════════════════
        // Ⓐ-4 纯函数**必须真被调用点用起来**（防"孤立纯函数"回归）
        //     前片（`dialog-options`）把 `ShouldBeVisible` / `IsReadable` 写完就死了 ——
        //     孤立纯函数在离线断言里全绿、在画面上毫无作用 ⇒ 这条断言专抓那个形态。
        // ═════════════════════════════════════════════════════════════════════
        private static void CallSitesWired()
        {
            var inv = NoComments(ReadUi("InventoryPanel.cs"));
            var tip = NoComments(ReadUi("ItemTooltip.cs"));
            var dlg = NoComments(ReadUi("NpcDialogPanel.cs"));

            var invCalls = CountOf(inv, "ItemTooltip.ShouldBeVisible(");
            Program.Check("Ⓐ-4a：`InventoryPanel` 的悬停路径真的调 `ShouldBeVisible`（≥4 处：输入不可用 / 面板外 / 空格 / 无命中格）",
                invCalls >= 4 && CountOf(tip, "ItemTooltip.ShouldBeVisible(") == 0,
                $"InventoryPanel 调用 {invCalls} 处；ItemTooltip 内自调 0 处（定义不算调用）");

            Program.Check("Ⓐ-4b：`Game.Input` 不可用的分支**必须隐**（旧写法是直接 return ⇒ tooltip 停在最后一帧 = 残留空框）",
                inv.Contains("ShouldBeVisible(panelOpen, false, false)")
                && inv.Contains("tooltip.hover.noinput"),
                "见 InventoryPanel.UpdateHover 的 noinput 分支");

            Program.Check("Ⓐ-4c：`NpcDialogPanel.Rebuild` 真的用 `IsReadable(...)` 判空文案（而不是只定义不调用）",
                dlg.Contains("!IsReadable(label,") && CountOf(dlg, "IsReadableLabel(") >= 2
                && dlg.Contains("dialog.option.text.empty."),
                "调用 " + CountOf(dlg, "!IsReadable(label,") + " 处；WarnOnce 键 dialog.option.text.empty.*");
        }

        // ═════════════════════════════════════════════════════════════════════
        // Ⓐ-5 中等按钮悬停底图：**接线 + 资产双守护**（极性：记录缺陷 ⇒ 守护修复）
        //
        //  历史（片 `dialog-options2`，2026-09-24 前半段）：`D2/UI/Menu/btn_med_sel.png`
        //  （= 原版 `FrontEnd/MediumSelButtonBlank.dc6` 帧 0 按 **ACT1** 调色板解出）是**麻点图**
        //  —— 与 `tools/d2codec` 解码器输出**逐像素全等**（maxdiff=0）⇒ 不是写坏，是**调色板错**；
        //  实机后果 = 悬停时底板花斑糊住按钮文案（`.ai-tmp/screenshots/btnlabel_recap_g1_dialog_options.png`）。
        //  ⇒ 那一片把悬停帧**降级为常态帧**，并钉「坏图**没有**被任何 UI 代码接线」——
        //     那三条是**缺陷记录**（旧前提 = 资产坏、只能降级）。
        //
        //  现状（片 `dialog-options` **定稿**，2026-09-24 后半段）：**前提已消失** ——
        //    · 资产：`btn_med_sel` 按 **`fechar`** 调色板重出（`regen_med_sel_fechar.py`；
        //      15 套 palette 逐套量同一麻点判据，只有 `fechar` 落进干净带）⇒ **0.424 → 0.054**；
        //    · 接线：`UI/UiArt.cs` 的 `ButtonSpritesFor` 中等分支 `highlight` **已接回** `ResPaths.BtnMedSel`。
        //  ⇒ 本节三条判据极性**翻转**（fail-to-pass 的正向形式，⛔ 不是放宽、不是删掉、更不是永真）：
        //    · `Ⓐ-5a`「悬停**必须**用高亮帧 `BtnMedSel`」（旧前提「降级为常态帧」作废）；
        //    · `Ⓐ-5c` helper 自证的两侧换成 已接线(`BtnMedSel`)/未接线(`BtnMedSelPressed`)；
        //    · `Ⓐ-5d` 由「坏图未接线」改为「**量接线的图**」：麻点比 ≤ 0.10（旧图 0.424 必红）
        //      **且** 高饱和像素 ≥ 50（防「换成全透明图 ⇒ hot=0 ⇒ 比值恒 0」的假绿）。
        //
        //  退化自证（片 final-close **实跑过**，见 `.ai-tmp/test/report-finalclose.md`）：
        //    ① 把 `UiArt` 的 highlight 临时改回常态帧 ⇒ Ⓐ-5a / Ⓐ-5c 变红，还原后回绿；
        //    ② 把 PNG 临时换回 HEAD 旧图（麻点 0.424）⇒ Ⓐ-5d 变红，还原后回绿（`git diff` 干净）。
        //  独立交叉核对（宿主外，PIL）：`.ai-tmp/test/finalclose_speckle.py`
        //    `btn_med_normal 0.040 / btn_med_pressed 0.066 / btn_med_sel 0.054 / sel_pressed 0.055`；
        //    HEAD 旧图 `btn_med_sel 0.424 (128/302)` / `sel_pressed 0.410 (120/293)`。
        //  ⚠️ 残留资产 `btn_med_sel_pressed.png`（同族干净，0.055）**无消费方** ⇒ 仍钉「未接线」。
        // ═════════════════════════════════════════════════════════════════════
        private static void MediumHighlightBadArt()
        {
            UiArt.ButtonSpritesFor(new Vector2(128f, 35f), out var mn, out var mp, out var mh);
            UiArt.ButtonSpritesFor(new Vector2(272f, 35f), out var wn, out var wp, out var wh);

            // ★ final-close（2026-09-24）：极性**翻转** —— 旧断言是「悬停降级为常态帧」（前提 = 资产坏）。
            //   资产已重出 + `UiArt` 已接线 ⇒ 现在必须钉住「悬停**真的**用了高亮帧」。
            Program.Check("Ⓐ-5a：中等按钮悬停**必须**用原版高亮帧 `BtnMedSel`（常态/按下仍走单帧原版图）；"
                + "旧极性「降级为常态帧」随资产修复作废",
                mn == ResPaths.BtnMedNormal && mp == ResPaths.BtnMedPressed
                && mh == ResPaths.BtnMedSel && mh != ResPaths.BtnMedNormal,
                "med: n=" + Name(mn) + " p=" + Name(mp) + " h=" + Name(mh));

            Program.Check("Ⓐ-5b：宽按钮口径未被本片改动（高亮 = 常态，原版宽按钮只有常态/按下两态）",
                wn == ResPaths.BtnWideNormal && wp == ResPaths.BtnWidePressed && wh == ResPaths.BtnWideNormal,
                "wide: n=" + Name(wn) + " p=" + Name(wp) + " h=" + Name(wh));

            // 剥注释后的 UI 源码（+ ResPaths.cs 作为常量定义处，单独列出 ⇒ 定义不算接线）
            var sources = new List<string>();
            foreach (var f in Directory.GetFiles(Program.UiDir, "*.cs"))
                sources.Add(NoComments(File.ReadAllText(f, Encoding.UTF8)));
            var resPaths = NoComments(File.ReadAllText(
                Path.Combine(Program.ProjectRoot, "client", "Assets", "Scripts", "Core", "ResPaths.cs"), Encoding.UTF8));

            // ★ final-close：极性翻转后的**守护式**断言 —— 接线那半条落在 `BtnMedSel`（必须已接线），
            //   未接线那半条落在无消费方的 `BtnMedSelPressed`（helper 两向都动 ⇒ 不是恒真/恒假）。
            // 三个探针各算一次、detail 由**实得值**合成（⛔ 不写死一句话：判红时那句会撒谎）
            var wiredNormal = IsWired("BtnMedNormal", sources);
            var wiredSel = IsWired("BtnMedSel", sources, resPaths);
            var wiredSelPressed = IsWired("BtnMedSelPressed", sources, resPaths);
            Program.Check("Ⓐ-5c：**接线守护** —— 中等悬停帧 `ResPaths.BtnMedSel` 必须被 `UI/**` 真引用"
                + "（且 helper 两个方向都能动：无消费方的 `BtnMedSelPressed` 必须报『未接线』）",
                wiredNormal && wiredSel && !wiredSelPressed,
                $"BtnMedNormal=>{(wiredNormal ? "已接线" : "未接线")}；"
                + $"BtnMedSel=>{(wiredSel ? "已接线" : "未接线")}（接线点应见 UiArt.ButtonSpritesFor 中等分支）；"
                + $"BtnMedSelPressed=>{(wiredSelPressed ? "已接线" : "未接线")}（只剩 ResPaths.cs 的定义，已摘出）");

            // ★ final-close：由「坏图未接线」翻成「**量接线的图**」—— 判据从"路径没被引用"变成
            //   "被引用的那张图本身干净"，这才是资产的守护（路径对、图坏 ⇒ 旧口径照样绿）。
            var sel = MeasureSpeckle(Path.Combine(Program.ResourceRoot, "Clover",
                "D2", "UI", "Menu", "btn_med_sel.png"));
            Program.Check("Ⓐ-5d：接线的 `btn_med_sel.png` 是**干净**高亮帧 —— 128×35 且麻点比 ≤ 0.10"
                + "（旧 ACT1 麻点图 0.424 必红）、高饱和像素 ≥ 50（防「换全透明图 ⇒ hot=0 ⇒ 比值恒 0」）",
                sel.Decoded && sel.W == 128 && sel.H == 35 && sel.Ratio <= 0.10 && sel.Hot >= 50,
                sel.Decoded
                    ? $"PNG {sel.W}×{sel.H} 麻点比={sel.Ratio:0.000}（iso/hot={sel.Iso}/{sel.Hot}）"
                    : "PNG 读不出（解码失败或文件缺失）");

            Program.Check("Ⓐ-5e：防回归 ——『用条带第 1 帧顶替悬停帧』那套已删"
                + "（条带 `button_medium` 第 1 帧实测是**按下帧**，悬停显示它 = 把输入反馈画反）",
                !NoComments(ReadUi("UiArt.cs")).Contains("LoadMediumHighlightFromStrip"),
                "UiArt.cs 剥注释后 0 命中 LoadMediumHighlightFromStrip");

            Program.Check("Ⓐ-5f：高亮帧与对照图都在盘（判据锚点，⛔ 不靠『记忆里那张图』）",
                File.Exists(Path.Combine(Program.ResourceRoot, "Clover", "D2", "UI", "Menu", "btn_med_sel.png"))
                && File.Exists(Path.Combine(Program.ResourceRoot, "Clover", "D2", "UI", "Menu", "btn_med_normal.png")),
                "Resources/Clover/D2/UI/Menu/btn_med_{sel,normal}.png");

            // ★ final-close：**判据自检**（skill §8 第 3 条）——麻点度量本身必须能被已知样本证伪，
            //   否则 Ⓐ-5d 可能是"常数比大小"式的假绿。
            //   合成脏样本 = 纯灰底板 + 每 4×4 一个**孤立**高饱和像素（正是"麻点"的定义形态 ⇒ 比值≈1）；
            //   合成干净样本 = 纯灰板（hot=0 ⇒ 比值 0，但必须被 Ⓐ-5d 的 `hot ≥ 50` 挡下）。
            var dirty = SpeckleOf(IsolatedHotSample(64, 16), 64, 16, out var dIso, out var dHot);
            var flat = SpeckleOf(FlatSample(64, 16), 64, 16, out _, out var fHot);
            Program.Check("Ⓐ-5g：(判据自检) 同一麻点度量在合成样本上两向都能动：孤立高饱和像素板 ⇒ 比值 > 0.10（判脏）；"
                + "纯灰板 ⇒ hot=0（比值 0，但被 Ⓐ-5d 的 hot 下限挡下）",
                dirty > 0.10 && dHot > 0 && flat == 0.0 && fHot == 0,
                $"合成脏样本 ratio={dirty:0.000}（iso/hot={dIso}/{dHot}）；纯灰板 ratio={flat:0.000} hot={fHot}");
        }

        // ═════════════════════════════════════════════════════════════════════
        // 麻点度量（口径 = `tools/probes/measure/probe_med_sel_palette.py::speckle`，逐行对齐）
        //   ch = 非透明像素的 max(R,G,B) − min(R,G,B)；hot = ch > 60；
        //   iso = hot 且 **4 邻域 ch 均值 < 30**（越界回卷，对齐 numpy 的 `np.roll`）；
        //   比值 = iso / hot。孤立高饱和像素 / 高饱和像素 = 0.424（旧 ACT1 麻点图）vs 0.054（fechar 干净图）。
        // ═════════════════════════════════════════════════════════════════════
        private struct SpeckleStat
        {
            public bool Decoded;
            public int W, H, Iso, Hot;
            public double Ratio;
        }

        /// <summary>按同心口径量一张磁盘 PNG 的麻点比；解码失败 ⇒ <c>Decoded=false</c>（调用方如实报红）。</summary>
        private static SpeckleStat MeasureSpeckle(string file)
        {
            var st = new SpeckleStat();
            if (!File.Exists(file)) return st;
            if (!TryDecodeRgba(file, out var w, out var h, out var px)) return st;
            st.Decoded = true;
            st.W = w;
            st.H = h;
            st.Ratio = SpeckleOf(px, w, h, out var iso, out var hot);
            st.Iso = iso;
            st.Hot = hot;
            return st;
        }

        /// <summary>麻点比（见本区段头注释的口径）。</summary>
        private static double SpeckleOf(byte[] rgba, int w, int h, out int isoCount, out int hotCount)
        {
            var ch = new int[w * h];
            hotCount = 0;
            for (var i = 0; i < w * h; i++)
            {
                if (rgba[i * 4 + 3] == 0) continue;                 // 透明像素不参与（ch 保持 0）
                var r = rgba[i * 4];
                var g = rgba[i * 4 + 1];
                var b = rgba[i * 4 + 2];
                var mx = Math.Max(r, Math.Max(g, b));
                var mn = Math.Min(r, Math.Min(g, b));
                ch[i] = mx - mn;
                if (ch[i] > 60) hotCount++;
            }

            isoCount = 0;
            if (hotCount == 0) return 0.0;                          // 无高饱和像素 ⇒ 比值未定义，记 0（由 hot 下限兜住）

            for (var y = 0; y < h; y++)
                for (var x = 0; x < w; x++)
                {
                    var i = y * w + x;
                    if (ch[i] <= 60) continue;
                    var xm = (x + w - 1) % w;                       // np.roll 是回卷的 ⇒ 这里也必须回卷，
                    var xp = (x + 1) % w;                           // 否则边缘像素邻域均值会偏小、比值偏高
                    var ym = (y + h - 1) % h;
                    var yp = (y + 1) % h;
                    var nb = ch[ym * w + x] + ch[yp * w + x] + ch[y * w + xm] + ch[y * w + xp];
                    if (nb / 4.0 < 30.0) isoCount++;
                }

            return (double)isoCount / hotCount;
        }

        /// <summary>合成脏样本：纯灰底板 + 每 4×4 一个**孤立**纯红像素（邻域 ch=0 ⇒ 必判 iso）。</summary>
        private static byte[] IsolatedHotSample(int w, int h)
        {
            var px = FlatSample(w, h);
            for (var y = 0; y < h; y++)
                for (var x = 0; x < w; x++)
                {
                    if (x % 4 != 0 || y % 4 != 0) continue;
                    var o = (y * w + x) * 4;
                    px[o] = 255;                                    // R
                    px[o + 1] = 0;                                  // G
                    px[o + 2] = 0;                                  // B
                }
            return px;
        }

        /// <summary>合成干净样本：纯灰板（ch=0 ⇒ hot=0）。</summary>
        private static byte[] FlatSample(int w, int h)
        {
            var px = new byte[w * h * 4];
            for (var i = 0; i < w * h; i++)
            {
                px[i * 4] = 128;
                px[i * 4 + 1] = 128;
                px[i * 4 + 2] = 128;
                px[i * 4 + 3] = 255;
            }
            return px;
        }

        /// <summary>
        /// 解 PNG 的 RGBA8 像素（只处理 8bit / 非隔行 / 色彩类型 2·6）。
        /// 与 `ShopGridCheck.TryDecodeRgba` / `W3GameCheck` 那份同源实现（此处自带一份：本文件被
        /// 独立认领，不去改别人的大文件以免并发写冲突）。解不出来返回 false ⇒ 调用方如实报红。
        /// </summary>
        private static bool TryDecodeRgba(string file, out int w, out int h, out byte[] rgba)
        {
            w = 0;
            h = 0;
            rgba = null;
            if (!File.Exists(file)) return false;
            var b = File.ReadAllBytes(file);
            if (b.Length < 8 || b[0] != 0x89 || b[1] != 0x50) return false;

            var idat = new MemoryStream();
            var pos = 8;
            int bitDepth = 0, colorType = 0;
            while (pos + 8 <= b.Length)
            {
                var len = (b[pos] << 24) | (b[pos + 1] << 16) | (b[pos + 2] << 8) | b[pos + 3];
                if (len < 0 || pos + 12 + len > b.Length) break;
                var tag = Encoding.ASCII.GetString(b, pos + 4, 4);
                var d = pos + 8;
                if (tag == "IHDR")
                {
                    w = (b[d] << 24) | (b[d + 1] << 16) | (b[d + 2] << 8) | b[d + 3];
                    h = (b[d + 4] << 24) | (b[d + 5] << 16) | (b[d + 6] << 8) | b[d + 7];
                    bitDepth = b[d + 8];
                    colorType = b[d + 9];
                    if (b[d + 12] != 0) return false;               // 隔行不支持
                }
                else if (tag == "IDAT") idat.Write(b, d, len);
                else if (tag == "IEND") break;
                pos = d + len + 4;
            }

            if (bitDepth != 8 || (colorType != 6 && colorType != 2)) return false;

            byte[] raw;
            try
            {
                using (var ms = new MemoryStream(idat.ToArray()))
                using (var z = new ZLibStream(ms, CompressionMode.Decompress))
                using (var outMs = new MemoryStream())
                {
                    z.CopyTo(outMs);
                    raw = outMs.ToArray();
                }
            }
            catch (Exception)
            {
                return false;
            }

            var bpp = colorType == 6 ? 4 : 3;
            var stride = w * bpp;
            if (raw.Length < (stride + 1) * h) return false;
            rgba = new byte[w * h * 4];
            var prev = new byte[stride];
            var cur = new byte[stride];
            var o = 0;
            for (var y = 0; y < h; y++)
            {
                var filter = raw[o++];
                Array.Copy(raw, o, cur, 0, stride);
                o += stride;
                for (var x = 0; x < stride; x++)
                {
                    int a = x >= bpp ? cur[x - bpp] : 0;
                    int bb = prev[x];
                    int c = x >= bpp ? prev[x - bpp] : 0;
                    switch (filter)
                    {
                        case 1: cur[x] = (byte)(cur[x] + a); break;
                        case 2: cur[x] = (byte)(cur[x] + bb); break;
                        case 3: cur[x] = (byte)(cur[x] + ((a + bb) >> 1)); break;
                        case 4:
                            {
                                var pp = a + bb - c;
                                var pa = Math.Abs(pp - a);
                                var pb = Math.Abs(pp - bb);
                                var pc = Math.Abs(pp - c);
                                cur[x] = (byte)(cur[x] + (pa <= pb && pa <= pc ? a : (pb <= pc ? bb : c)));
                                break;
                            }
                    }
                }
                for (var x = 0; x < w; x++)
                {
                    rgba[(y * w + x) * 4] = cur[x * bpp];
                    rgba[(y * w + x) * 4 + 1] = cur[x * bpp + 1];
                    rgba[(y * w + x) * 4 + 2] = cur[x * bpp + 2];
                    rgba[(y * w + x) * 4 + 3] = bpp == 4 ? cur[x * bpp + 3] : (byte)255;
                }
                var t = prev;
                prev = cur;
                cur = t;
            }
            return true;
        }

        /// <summary>`ResPaths.<paramref name="constName"/>` 是否被 UI 源码（剥注释后）**引用**。</summary>
        /// <param name="resPathsNoComments">
        /// `Core/ResPaths.cs` 剥注释后的源码 —— 里面那一行是**常量定义**，不算"接线" ⇒ 先摘出去。
        /// </param>
        private static bool IsWired(string constName, List<string> uiSources, string resPathsNoComments = null)
        {
            var needle = "ResPaths." + constName;
            if (resPathsNoComments != null && resPathsNoComments.Contains(needle))
            {
                // 定义处被摘掉后，UI 源码里必须**一处都没有**才算"未接线"
                foreach (var s in uiSources)
                    if (s.Contains(needle)) return true;
                return false;
            }
            foreach (var s in uiSources)
                if (s.Contains(needle)) return true;
            return false;
        }

        private static string Name(string path)
            => path == null ? "<null>" : path.Substring(path.LastIndexOf('/') + 1);

        private static string ReadUi(string file)
            => File.ReadAllText(Path.Combine(Program.UiDir, file), Encoding.UTF8);

        private static int CountOf(string text, string needle)
        {
            var n = 0;
            var i = 0;
            while (true)
            {
                i = text.IndexOf(needle, i, StringComparison.Ordinal);
                if (i < 0) return n;
                n++;
                i += needle.Length;
            }
        }

        /// <summary>
        /// 去掉 `//…` 与 `/*…*/`（**字符串字面量里的不算**）。
        /// 为什么必须剥：本片在注释里**引用**了坏图路径 `ResPaths.BtnMedSel` / 改前的做法
        /// （说明为什么禁用）⇒ 扫原文会把"说明"当成"接线"，假阳性。
        /// </summary>
        private static string NoComments(string src)
        {
            var sb = new StringBuilder(src.Length);
            var inStr = false;
            for (var i = 0; i < src.Length; i++)
            {
                var c = src[i];
                if (inStr)
                {
                    sb.Append(c);
                    if (c == '\\' && i + 1 < src.Length) { sb.Append(src[i + 1]); i++; continue; }
                    if (c == '"') inStr = false;
                    continue;
                }
                if (c == '"') { inStr = true; sb.Append(c); continue; }
                if (c == '/' && i + 1 < src.Length && src[i + 1] == '/')
                {
                    while (i < src.Length && src[i] != '\n') i++;
                    sb.Append('\n');
                    continue;
                }
                if (c == '/' && i + 1 < src.Length && src[i + 1] == '*')
                {
                    i += 2;
                    while (i + 1 < src.Length && !(src[i] == '*' && src[i + 1] == '/')) i++;
                    i++;
                    sb.Append('\n');
                    continue;
                }
                sb.Append(c);
            }
            return sb.ToString();
        }
    }
}
