// ─────────────────────────────────────────────────────────────────────────────
//   人物属性面板「等级 / 经验 / 技能点」三框的**单行判据**（含退化样本）。
//
//   现行形状（`CharacterPanel.Refresh` 里三处 `*Text.text` 赋值）：
//     · `TopRight`   用 `UiLayoutGame.CharTopRightSize`   → 「等级 {level}」
//     · `Band2Right` 用 `UiLayoutGame.CharBand2RightSize` → 「经验 {exp}」（只显示当前经验）
//     · `Band2Mid`   用 `UiLayoutGame.CharBand2MidSize`   → 「技能点 {skillPoints}」
//
// ── 判据口径（与 ⑧ 同源，不另立一套）─────────────────────────────────────
//   单行的充要条件 = 生产折行口径 `MeasureNative(font, text, chi) < availPx`
//   （`D2Text.WrapLines` 的断点条件是 `next >= availPx` ⇒ **严格小于**）。
//   `NeedNative` / `AvailPx` / `SingleLine` / `MinBoxArt` / 字模表加载**全部复用 ⑧**
//   （`U52ResistCheck`）⇒ 本文件**不重复实现**字模解码（两条实现必然会漂）。
//   不用 `Text.cachedTextGenerator.lineCount` / `preferredWidth`：镜像 `Text` 的 `font` 被
//      `D2TextMirror.Attach` 置 null（`UI/D2TextMirror.cs:65-67`）⇒ 两者恒为 0 ⇒ **恒真/恒假断言**。
//   不直接调 `D2Text.MeasureNative(..., chi:true)`：chi 字模异步加载、宿主 `Game.Res == null`
//      ⇒ `HasGlyph` 恒 false ⇒ 逐字返 0 ⇒ 又是一条**恒真断言**（⑧ 的头注释有实测）。
//
// ── 单位陷阱（本文件的第一条断言就是为它设的）────────────────────────────
//   `UiLayoutGame` 里**两种口径并存**：
//     · `Size(w,h)` / `S(...)` **已 ×K** ⇒ `CharTopRightSize` 是**画布px**（art 117 → 210.6 px）
//     · `CharResistNameW = 66f` 这类是**裸 art**（⑧ 里写 `… * UiLayoutGame.K`）
//   ⇒ 本判据先把三框换回 **art**（`/K`）再与 `needNative`（art px）比，并对换回来的 art 宽
//     打一条锚点断言（117 / 118 / 118）—— 单位写错会立刻红，而不是悄悄判出一个错的绿。
//
// ── 可达值域（穷举；不许只挑一个"看起来合理"的数）────────────────────────
//   · 等级   1..99      ← 出处 `client/Assets/Scripts/Table/Tsv/Experience.tsv` 的 `level` 列（1..99）
//   · 技能点  0..99     ← 与等级同域的保守上界（每级 1 点 + 任务奖励；99 级为最高等级）
//   · 经验   「经验 {exp}」的最坏 = **表内最大 exp**（99 级 = 3837739017，10 位数字）；
//       并列「经验 {exp}/{expNext}」是退化样本 (c)、**不是**面板现行文本
//       单调性论证：十进制串长随数值**单调不降** ⇒ 取最大值即最坏宽度
//       ⇒ 无需 99×99 组合穷举（那是 9801 次无信息的重复）
//
// ── 退化样本（喂的是**同一个**判据，不是另写一套）────────────────────────
//   (a) 框宽 = 「最小可放宽度 − 1 art」 ⇒ 必须判**折行**（确定性，由 `MinBoxArt` 定义保证）
//   (b) 文案人为加长 40 字 ⇒ 必须判**折行**（不依赖表内容的确定性样本）
//   (c) 并列「经验 {exp}/{expNext}」在 `Band2Right` 框内 ⇒ 必须判**折行**（钉死"本框只显示当前经验"）
//   三条都在**内存**里改数（(c) 用表内真实最大值），不碰真常量、不碰真文件。
// ─────────────────────────────────────────────────────────────────────────────

using System;
using System.Collections.Generic;
using System.IO;
using Diablo2.UI;      // UiLayoutGame（几何常量与 K）
using UnityEngine;    // Mathf

namespace Uicheck
{
    /// <summary>人物属性面板 · 「等级 / 经验 / 技能点」三框的单行判据（含退化样本）。</summary>
    public static class CharTopRightTextCheck
    {
        private static void Check(string what, bool ok, string detail) => Program.Check(what, ok, detail);

        private static bool Near(float a, float b, float eps = 0.01f) => Mathf.Abs(a - b) <= eps;

        private static string ExpTsvPath
            => Path.Combine(Program.ProjectRoot, "client", "Assets", "Scripts", "Table", "Tsv", "Experience.tsv");

        /// <summary>穷举一个整数值域，返回最坏文本 / 其 needNative / 折行清单 / 本次 scale。</summary>
        private static void SweepInt(int from, int to, Func<int, string> tpl, float boxArt,
            out string worst, out int worstNeed, out List<string> wrapped, out float scale)
        {
            worst = string.Empty;
            worstNeed = -1;
            wrapped = new List<string>();
            scale = 1f;
            for (var v = from; v <= to; v++)
            {
                var t = tpl(v);
                float sc;
                string kind;
                List<char> miss, s2t;
                var n = U52ResistCheck.NeedNative(t, out sc, out kind, out miss, out s2t);
                scale = sc;
                if (n > worstNeed) { worstNeed = n; worst = t; }
                if (!U52ResistCheck.SingleLine(n, U52ResistCheck.AvailPx(boxArt * UiLayoutGame.K, sc)))
                    wrapped.Add(t);
            }
        }

        private static string Head(List<string> xs, int n)
            => string.Join(",", xs.GetRange(0, Math.Min(n, xs.Count)).ToArray());

        /// <summary>读 `Experience.tsv`（等级域 + 表内最大累计经验）—— 可达上界的出处。</summary>
        private static bool LoadExp(out List<int> levels, out long maxExp)
        {
            levels = new List<int>();
            maxExp = 0L;
            var p = ExpTsvPath;
            var ok = File.Exists(p);
            Program.Check("经验表可读（`Scripts/Table/Tsv/Experience.tsv`）—— 可达值域的唯一出处",
                ok, ok ? $"{p}（{new FileInfo(p).Length} B）" : $"缺文件 ⇒ 本判据 fail-closed（{p}）");
            if (!ok) return false;

            var lines = File.ReadAllLines(p);
            for (var i = 1; i < lines.Length; i++)   // 第 1 行 = 表头 level/exp/exp_ratio
            {
                var f = lines[i].Split('\t');
                if (f.Length < 2) continue;
                int lv;
                long e;
                if (!int.TryParse(f[0], out lv) || !long.TryParse(f[1], out e)) continue;
                levels.Add(lv);
                if (e > maxExp) maxExp = e;
            }
            var hasRows = levels.Count > 0;
            Program.Check("经验表至少 99 行（等级 1..99 全覆盖）",
                hasRows && levels.Count >= 99,
                $"rowCount={levels.Count} 首={levels[0]} 末={levels[levels.Count - 1]} maxExp={maxExp}");
            return hasRows;
        }

        public static void Run()
        {
            Console.WriteLine("── ⑨ u52-resist：人物属性面板「等级/经验/技能点」三框的单行判据（U52 / P-2b）──");

            if (!U52ResistCheck.LoadFontTables()) return;

            // ── 几何锚点（单位陷阱就在这条上）──────────────────────────────────
            var topArtW = UiLayoutGame.CharTopRightSize.x / UiLayoutGame.K;
            var bandRW = UiLayoutGame.CharBand2RightSize.x / UiLayoutGame.K;
            var bandMW = UiLayoutGame.CharBand2MidSize.x / UiLayoutGame.K;
            Check("三框 art 宽 = 117 / 118 / 118（`Size()` 已 ×K ⇒ 本判据换回 art，单位写错必红）",
                Near(topArtW, 117f) && Near(bandRW, 118f) && Near(bandMW, 118f),
                $"TopRight {topArtW:0.###} / Band2Right {bandRW:0.###} / Band2Mid {bandMW:0.###} art"
                + $"（K={UiLayoutGame.K}，字号 FontPx16={UiLayoutGame.FontPx16} 画布px）");

            // ── 可达值域 ───────────────────────────────────────────────────────
            List<int> levels;
            long maxExp;
            if (!LoadExp(out levels, out maxExp)) return;

            // ── ① 等级（TopRight）───────────────────────────────────────────────
            string worstLv;
            int needLv;
            List<string> wrapLv;
            float scLv;
            SweepInt(1, 99, v => $"等级 {v}", topArtW, out worstLv, out needLv, out wrapLv, out scLv);
            Check("「等级 N」N∈1..99 在 `TopRight` 框内**全部单行**",
                wrapLv.Count == 0,
                wrapLv.Count == 0
                    ? $"最坏「{worstLv}」need={needLv} art < availPx "
                      + $"{U52ResistCheck.AvailPx(topArtW * UiLayoutGame.K, scLv)}（框 {topArtW:0.#} art）"
                    : $"折行 {wrapLv.Count} 个，如 {Head(wrapLv, 5)}");

            // ── ② 技能点（Band2Mid）─────────────────────────────────────────────
            string worstSp;
            int needSp;
            List<string> wrapSp;
            float scSp;
            SweepInt(0, 99, v => $"技能点 {v}", bandMW, out worstSp, out needSp, out wrapSp, out scSp);
            Check("「技能点 N」N∈0..99 在 `Band2Mid` 框内**全部单行**",
                wrapSp.Count == 0,
                wrapSp.Count == 0
                    ? $"最坏「{worstSp}」need={needSp} art < availPx "
                      + $"{U52ResistCheck.AvailPx(bandMW * UiLayoutGame.K, scSp)}（框 {bandMW:0.#} art）"
                    : $"折行 {wrapSp.Count} 个，如 {Head(wrapSp, 5)}");

            // ── ③ 经验（Band2Right）—— 最坏 = 表内最大值（10 位）────────────────
            //   本框只显示当前经验：原版 `CharstatPanel.prefab` 无等级/经验节点，并列「当前/下一等级」的载体
            //   是底部经验条的悬停提示串（`原版资源/d2text/chi_string.txt` 串 4163 `經驗： %u / %u`）。
            //   ⇒ 钉两条：① 「经验 cur」必须单行；② 退化样本「经验 cur/next」必须判**折行**（能失败）。
            var txtExp = $"经验 {maxExp}";
            float scEx;
            string kEx;
            List<char> mEx, vEx;
            var needEx = U52ResistCheck.NeedNative(txtExp, out scEx, out kEx, out mEx, out vEx);
            var availEx = U52ResistCheck.AvailPx(bandRW * UiLayoutGame.K, scEx);
            var missNote = mEx.Count > 0 ? $" ⚠️ 缺字形 {new string(mEx.ToArray())}" : string.Empty;
            Check("「经验 cur」最坏值（表内最大值，10 位）在 `Band2Right` 框内单行",
                U52ResistCheck.SingleLine(needEx, availEx),
                $"最坏「{txtExp}」need={needEx} art vs availPx {availEx}（框 {bandRW:0.#} art）"
                + $"；余量 {availEx - needEx} art{missNote}");

            // ── ③b 退化样本：「经验 cur/next」并列 ⇒ 必须判**折行**（同一判据，非另写一套）──
            var txtExpPair = $"经验 {maxExp}/{maxExp}";
            float scExpPair;
            string kExpPair;
            List<char> mExpPair, vExpPair;
            var needExpPair = U52ResistCheck.NeedNative(txtExpPair, out scExpPair, out kExpPair, out mExpPair, out vExpPair);
            var availExpPair = U52ResistCheck.AvailPx(bandRW * UiLayoutGame.K, scExpPair);
            Check("退化样本：并列「经验 cur/next」在 `Band2Right` 框内必须判**折行**（⇒ 本框只显示当前经验）",
                !U52ResistCheck.SingleLine(needExpPair, availExpPair),
                $"并列样本「{txtExpPair}」need={needExpPair} art ≥ availPx {availExpPair}（框 {bandRW:0.#} art）⇒ 折行");

            // ── ④ 退化样本 (a)：框宽 =「最小可放宽度 − 1 art」⇒ 必须折行（能失败）──
            var minArt = U52ResistCheck.MinBoxArt(needLv, scLv);
            string worstD;
            int needD;
            List<string> wrapD;
            float scD;
            SweepInt(1, 99, v => $"等级 {v}", minArt - 1f, out worstD, out needD, out wrapD, out scD);
            Check("退化样本(a)：`TopRight` 框收窄到「最小可放宽度 − 1 art」⇒ 判据必须判**折行**",
                minArt > 1 && wrapD.Count > 0,
                $"等级最坏 need={needLv} art ⇒ 最小可放框 {minArt} art；喂 {minArt - 1} art ⇒ 折行 {wrapD.Count} 个");

            // ── ⑤ 退化样本 (b)：文案人为加长 ⇒ 必须折行（不依赖表内容）──────────
            var longText = "技能点 " + new string('0', 40);
            float scB;
            string kB;
            List<char> mB, vB;
            var needB = U52ResistCheck.NeedNative(longText, out scB, out kB, out mB, out vB);
            var availB = U52ResistCheck.AvailPx(bandMW * UiLayoutGame.K, scB);
            Check("退化样本(b)：文案加长到 40 位数字 ⇒ 判据必须判**折行**（同一判据，非另写一套）",
                !U52ResistCheck.SingleLine(needB, availB),
                $"加长样本 need={needB} art ≥ availPx {availB} ⇒ 折行");

            Console.WriteLine();
        }
    }
}
