// ─────────────────────────────────────────────────────────────────────────────
// uicheck · 人物属性面板「四系抗性」一行的**折行判据**（片 u52-resist，2026-09-24）
//
// 挡的是什么缺陷（用户第三批投诉 U52 的 D10，且是**我们自己引入的回归**）：
//   底图左下（原版 art x 0..81 × y 314..415 的空白大理石）那 4 行四系抗性里，
//   **4 字中文标签被塞进 46 art 宽的框** ⇒ 生产折行口径下折成 2 行，
//   4 行标签 = 8 行交错（实机放大图 `.ai-tmp/test/u52run2_crop_left.png` 逐行可见）。
//   那个 46 art 是片 `charstat` 为修「值列折行」时**同步收窄**过来的 ⇒ 属回归。
//
// ── 判据口径（唯一口径；**不要再用裸 `lineCount`**）────────────────────────────
//   生产实际画字的是 `D2Label.BuildBitmap`（`UI/D2Text.cs:981-983`）：
//       availPx = Mathf.Max(1, Mathf.RoundToInt(size.x / scale))      // size.x = 节点矩形宽（画布px）
//       scale   = D2Text.ScaleFor(fontSize, chi, font) = fontSize / cellH
//       lines   = D2Text.WrapLines(font, text, chi, availPx, wrap)
//   而 `WrapLines`（`D2Text.cs:593`）对"单段、要换行、框非 0"的文本，**单行的充要条件**是
//       `MeasureNative(font, text, chi) < availPx`            （注意是**严格小于**：断点条件是 `next >= availPx`）
//   ⇒ 本判据 = 「`needNative < availPx`」这两步的复算（`SingleLine` / `AvailPx` 都是纯函数）。
//
//   ⛔ **为什么不能用 `Text.cachedTextGenerator.lineCount`（旧口径，实测恒为 0）**：
//      本面板的标签是 `UiArt.Label` 建的 uGUI `Text`，但建完立刻被 `D2TextMirror.Attach`
//      设成 `font = null` + `enabled = false`（`UI/D2TextMirror.cs:65-67`）—— 它只是**数据持有者**，
//      画面由 `D2Label` 用原版位图字模画 ⇒ 它的 TextGenerator **从不被布局刷新**，
//      `lineCount` 恒为 0（u52play 11:38:35 的实机读数就是 `lines=0`；0 ≠ 1 行 ⇒ **假判**）。
//   ⛔ **为什么也不能用 `Text.preferredWidth / preferredHeight`**：这两个同样走 TextGenerator
//      （`GetGenerationSettings` 带的是 `font == null`）⇒ 恒为 0 ⇒ `preferredWidth <= rect.width`
//      是**恒真**断言（"判据本身恒真" = 还没在判该判的东西）。**真值只在 `D2Label` 那条路上**。
//   ⛔ **为什么离线不敢直接调 `D2Text.MeasureNative(..., chi: true)`**：chi 字模是**异步加载**的
//      （`EnsureChi` 要 `Game.Res`，本宿主 `Game.Res == null`）⇒ `HasGlyph` 恒 false
//      ⇒ `StepOf` 逐字返回 **0** ⇒ 量出来"什么都放得下"，又是一条恒真断言。
//      ⇒ 本宿主**自己解一次同一份素材**（`font16_chi_map.txt` + `font_chi_s2t.txt`，
//        口径与 `D2Text.ParseChiMap` / `D2Text.HasGlyph` 逐字段一致），并把
//        「解出来的 CELL == 代码默认 `D2Text.ChiCellW/H`」也判掉（两条路不许漂）。
//      拉丁侧则直接用**生产函数** `D2Text.Measure`（源码里的常量表，离线可用）。
//
// ── 覆盖口径 = 影响域（只重判受影响的行）────────────────────────────────────
//   ① 标签框几何（`CharResistNameX/W`）—— 4 行标签
//   ② 值列几何（`CharResistValueX/W`）—— 4 行值
//   ③ 字号（`FontPx16`，唯一出处；本片不动，但要跟着重算）
//   ④ 底图暗区（左下空白大理石的可用几何：art 0..81 / 行带 y 314..415）
//   ⑤ 四维行与派生行（**不动** —— 但要判"没被碰到"：标签/值框与它们的行矩形不相交）
//   ⑥ 其它同类短框（只出**读数**：它们的文案由存档数据驱动，判不成常量；见 §⑥）
//
// ── 退化样本（每条判据都要能红）──────────────────────────────────────────
//   把**修前那个折行形状**喂进同一个 `SingleLine` ⇒ 必须判成"折行"；
//   把**比最小可放宽度小 1 art** 的框喂进去 ⇒ 必须红；缺字形的样本 ⇒ 必须报 MISSING。
// ─────────────────────────────────────────────────────────────────────────────

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Diablo2.UI;
using UnityEngine;

namespace Uicheck
{
    /// <summary>人物属性面板 · 四系抗性行「折行」离线判据（含退化样本）。</summary>
    public static class U52ResistCheck
    {
        private static void Check(string what, bool ok, string detail) => Program.Check(what, ok, detail);

        // ═════════════════════════════════════════════════════════════════════
        // 判据本体（纯函数 ⇒ 退化样本喂的是**同一个**判据，不是另写一套）
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>
        /// 生产折行口径第 1 步：可用排版宽（原版/字模 px）。
        /// 逐字 = `D2Label.BuildBitmap`（`UI/D2Text.cs:981`）的
        /// `Mathf.Max(1, Mathf.RoundToInt(size.x / scale))`。
        /// </summary>
        internal static int AvailPx(float boxCanvasPx, float scale)
            => Mathf.Max(1, Mathf.RoundToInt(boxCanvasPx / scale));

        /// <summary>
        /// 生产折行口径第 2 步：整串是否落在**一行**内。
        /// 逐字 = `D2Text.WrapLines`（`UI/D2Text.cs:593`）的单段早退分支
        /// `MeasureNative(font, s, chi) < availPx`（**严格小于**）。
        /// </summary>
        internal static bool SingleLine(int needNative, int availPx) => needNative < availPx;

        /// <summary>枚举出「单行放得下」所需的**最小框宽**（原版 art px）—— 给退化样本定标用。</summary>
        internal static int MinBoxArt(int needNative, float scalePerArt)
        {
            for (var w = 1; w < 4096; w++)
                if (SingleLine(needNative, AvailPx(w * UiLayoutGame.K, scalePerArt)))
                    return w;
            return -1;
        }

        // ═════════════════════════════════════════════════════════════════════
        // 素材解析（口径 = D2Text.ParseChiMap / D2Text.HasGlyph）
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>`code → advance`（原版 px）。</summary>
        private static readonly Dictionary<int, int> ChiAdvance = new Dictionary<int, int>();

        /// <summary>简体 → 原版（繁体）码位（`font_chi_s2t.txt`）。</summary>
        private static readonly Dictionary<int, int> ChiS2T = new Dictionary<int, int>();

        private static int _chiCellW;
        private static int _chiCellH;
        private static int _chiCols;
        private static int _chiCount;

        private static string ChiMapPath
            => Path.Combine(Program.ProjectRoot, "client", "Assets", "Resources", "Clover", "D2", "Fonts",
                "font16_chi_map.txt");

        private static string ChiS2TPath
            => Path.Combine(Program.ProjectRoot, "client", "Assets", "Resources", "Clover", "D2", "Fonts",
                "font_chi_s2t.txt");

        /// <summary>
        /// 一个字符在这条文本里的**排版步进**（原版 px）+ 有没有字形。
        /// 口径与 `D2Text.HasGlyph`（`D2Text.cs:537-553`）一致：**直查 → 简体回退**；两处都没有 ⇒ 不画、不推进（0）。
        /// </summary>
        private static int StepOf(string text, int i, out bool missing, out bool viaS2T)
        {
            missing = false;
            viaS2T = false;
            var c = text[i];
            int adv;
            if (ChiAdvance.TryGetValue(c, out adv)) return adv;

            int alt;
            if (ChiS2T.TryGetValue(c, out alt) && ChiAdvance.TryGetValue(alt, out adv))
            {
                viaS2T = true;
                return adv;
            }

            missing = true;     // ⛔ 缺字形**不是**"放得下"：调用方必须把它报红（见 §⑤ 的缺字形退化样本）
            return 0;
        }

        /// <summary>整串宽度（原版 px）+ 缺字形清单 + 走简体回退的字符清单。</summary>
        private static int ChiMeasure(string text, out List<char> missing, out List<char> viaS2T)
        {
            missing = new List<char>();
            viaS2T = new List<char>();
            var w = 0;
            for (var i = 0; i < text.Length; i++)
            {
                bool miss, s2t;
                w += StepOf(text, i, out miss, out s2t);
                if (miss) missing.Add(text[i]);
                if (s2t) viaS2T.Add(text[i]);
            }
            return w;
        }

        /// <summary>按 `D2Text.IsLatinOnly` 的口径选字模（`D2Text.cs:210-222`：32..126 全落内 ⇒ 拉丁）。</summary>
        private static bool LatinOnly(string s) => D2Text.IsLatinOnly(s);

        /// <summary>一条文本的（needNative, scale）；拉丁走**生产函数** `D2Text.Measure`。
        /// 同宿主复用（`CharTopRightTextCheck`）—— ⛔ 仅放开可见性，逻辑一字未改。</summary>
        internal static int NeedNative(string text, out float scale, out string fontKind,
            out List<char> missing, out List<char> viaS2T)
        {
            var chi = !LatinOnly(text);
            fontKind = chi ? "chi" : "latin";
            var cellH = chi ? _chiCellH : D2Text.CellHeight(D2Text.D2Font.Font16);
            scale = D2Text.ScaleFor((int)UiLayoutGame.FontPx16, chi, D2Text.D2Font.Font16);

            if (!chi)
            {
                missing = new List<char>();
                viaS2T = new List<char>();
                return D2Text.Measure(D2Text.D2Font.Font16, text);   // 生产函数（纯常量表，离线可用）
            }
            return ChiMeasure(text, out missing, out viaS2T);
        }

        /// <summary>
        /// 一条文本的**原版像素宽度**（= 生产 `D2Text.MeasureNative(font, text, chi)` 的离线等价物）。
        /// <para>
        /// 出处/裁定：team-lead 2026-09-24 派活（做法 (a)）—— 由**本文件暴露**这一个入口，
        /// 由 `HoverSelectCheck`（`u44impl`）在自己的判据里调用：`expected = NativeWidth(t) * scale + 12 * K`。
        /// </para>
        /// <para>
        /// ⚠️ **为什么不直接调生产方法**：宿主里 chi 字模是**异步**加载的（`EnsureChi` 要 `Game.Res`，
        /// 本宿主 `Game.Res == null`）⇒ `HasGlyph` 恒 false ⇒ `MeasureNative(..., chi:true)` **逐字返 0**
        /// ⇒ **拿生产方法本身当判据 = 恒真断言**（`popupaudit` 已实证两例）。真值只在本文件这条解码路上。
        /// </para>
        /// <para>
        /// ⛔ 本入口**只暴露**：内部复用同一个 `NeedNative`（⇒ `ChiMeasure` / `D2Text.Measure`），
        /// **不新增第二份解码器**（重复实现必然会漂），且 ⛔ **不改** `SingleLine` / `AvailPx` 的现有签名。
        /// </para>
        /// </summary>
        internal static int NativeWidth(string text)
        {
            float scale;
            string fontKind;
            List<char> missing, viaS2T;
            return NeedNative(text, out scale, out fontKind, out missing, out viaS2T);
        }

        // ═════════════════════════════════════════════════════════════════════
        // 入口
        // ═════════════════════════════════════════════════════════════════════
        public static void Run()
        {
            Console.WriteLine("── ⑧ u52-resist：人物属性面板「四系抗性」行的折行判据（U52/D10 + U1）──");

            if (!LoadFontTables()) return;

            SectionFontTables();
            SectionCriterionSelfTest();
            SectionLabels();
            SectionValues();
            SectionGeometry();
            SectionOtherShortBoxes();
            SectionDegradation();
            Console.WriteLine();
        }

        // visible to CharTopRightTextCheck (same host, same font tables) -- visibility only.
        internal static bool LoadFontTables()
        {
            var map = ChiMapPath;
            var s2t = ChiS2TPath;
            if (!File.Exists(map) || !File.Exists(s2t))
            {
                Program._skip++;
                Console.WriteLine("[SKIP] 字模表不在本机 ⇒ 折行判据无法判："
                    + map + " / " + s2t);
                return false;
            }

            foreach (var raw in File.ReadAllLines(map, Encoding.UTF8))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                var f = line.Split(' ');
                if (f[0] == "COLS") { _chiCols = Parse(f[1], _chiCols); continue; }
                if (f[0] == "CELL")
                {
                    if (f.Length >= 3) { _chiCellW = Parse(f[1], _chiCellW); _chiCellH = Parse(f[2], _chiCellH); }
                    continue;
                }
                if (f[0] == "COUNT") { _chiCount = Parse(f[1], _chiCount); continue; }
                if (f.Length < 5) continue;
                ChiAdvance[Parse(f[0], -1)] = Parse(f[2], _chiCellW);      // code frame advance col row
            }

            foreach (var raw in File.ReadAllLines(s2t, Encoding.UTF8))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line[0] == '#' || line.StartsWith("COUNT", StringComparison.Ordinal))
                    continue;
                var f = line.Split(' ');
                if (f.Length < 2) continue;
                ChiS2T[Parse(f[0], -1)] = Parse(f[1], -1);
            }
            return true;
        }

        private static int Parse(string s, int fallback)
        {
            int v;
            return int.TryParse(s, out v) ? v : fallback;
        }

        // ── §① 字模表本身：两条路（素材 vs 代码默认）不许漂 ────────────────────
        private static void SectionFontTables()
        {
            Console.WriteLine("  §① 字模表在盘 + 与代码默认值交叉核对");
            Check("font16_chi_map 表头可读（CELL 宽×高 / COLS / COUNT 非 0）",
                _chiCellW > 0 && _chiCellH > 0 && _chiCols > 0 && _chiCount > 0,
                $"CELL {_chiCellW}×{_chiCellH} COLS {_chiCols} COUNT {_chiCount}（{ChiMapPath}）");
            Check("素材 CELL == 代码侧默认 `D2Text.ChiCellW/H(Font16)`（两条路不许漂：离线判据用素材值，"
                + "运行时 `ParseChiMap` 也用素材值 ⇒ 必须同一个数）",
                _chiCellW == D2Text.ChiCellW(D2Text.D2Font.Font16)
                && _chiCellH == D2Text.ChiCellH(D2Text.D2Font.Font16),
                $"素材 {_chiCellW}×{_chiCellH} vs 代码 {D2Text.ChiCellW(D2Text.D2Font.Font16)}×"
                + $"{D2Text.ChiCellH(D2Text.D2Font.Font16)}");
            Check("简体→原版字形回退表非空（`font_chi_s2t.txt`）", ChiS2T.Count > 0, $"{ChiS2T.Count} 条");
        }

        // ── §② 判据自身：真值表（防"恒真断言"）──────────────────────────────
        private static void SectionCriterionSelfTest()
        {
            Console.WriteLine("  §② 判据本体真值表（同一条 `SingleLine`，两侧对立读数）");
            var scaleChi = D2Text.ScaleFor((int)UiLayoutGame.FontPx16, true, D2Text.D2Font.Font16);
            var box46 = AvailPx(46f * UiLayoutGame.K, scaleChi);
            var box66 = AvailPx(UiLayoutGame.CharResistNameW * UiLayoutGame.K, scaleChi);
            Check("`SingleLine` **不是恒真**：need 52 在 avail 38 下必须为 false、在 avail 55 下必须为 true",
                !SingleLine(52, 38) && SingleLine(52, 55),
                $"SingleLine(52,38)={SingleLine(52, 38)}（应为 false）；SingleLine(52,55)={SingleLine(52, 55)}（应为 true）");
            Check("`AvailPx` 真的随框宽变（46 art ⇒ 38、66 art ⇒ 55，同一个 scale）",
                box46 == 38 && box66 == 55, $"46 art→{box46}；66 art→{box66}；scale={scaleChi:0.0000}");
        }

        // ── §③ 4 个标签：逐字有字形 + 单行 ────────────────────────────────────
        private static void SectionLabels()
        {
            Console.WriteLine("  §③ 四系抗性标签（chi 字模；文案出处 = 配表 `Affix.tsv:19-26` 的四个词缀显示名）");
            var scale = D2Text.ScaleFor((int)UiLayoutGame.FontPx16, true, D2Text.D2Font.Font16);
            var minBox = 0;
            var detail = new StringBuilder();

            for (var i = 0; i < CharacterPanel.ResistName.Length; i++)
            {
                var text = CharacterPanel.ResistName[i];
                List<char> missing, viaS2T;
                var need = ChiMeasure(text, out missing, out viaS2T);
                var needOk = missing.Count == 0 && ChiAdvance.Count > 0;
                Check($"[{i}] 标签「{text}」**逐字都有字形**（缺字形 = 不画，不等于放得下）",
                    needOk, missing.Count == 0
                        ? $"advance 合计 {need} art px"
                          + (viaS2T.Count > 0 ? $"；其中经简体回退 {new string(viaS2T.ToArray())}" : "")
                        : $"缺字形 {new string(missing.ToArray())}");

                var min = MinBoxArt(need, scale);
                if (min > minBox) minBox = min;
                detail.Append($"「{text}」need={need} art/最小框={min}；");
            }

            Check("4 个标签的**最小可放宽度**（单行）≤ 声明框宽 `UiLayoutGame.CharResistNameW`",
                minBox <= UiLayoutGame.CharResistNameW,
                $"{detail}⇒ 取最大 {minBox} art ≤ 声明 {UiLayoutGame.CharResistNameW} art");
            Check("声明框宽**不是**「刚好贴着旧值」：旧 46 art 必须**判不到单行**（否则本断言无信息量）",
                !SingleLine(52, AvailPx(46f * UiLayoutGame.K, scale)),
                $"46 art ⇒ availPx {AvailPx(46f * UiLayoutGame.K, scale)} < need 52");
        }

        // ── §④ 值列：`-100%` 与**整个可达集合** ────────────────────────────────
        private static void SectionValues()
        {
            Console.WriteLine("  §④ 抗性值列（latin 字模；可达值 = `PlayerStats.MinResist..MaxResist` 夹取后的整数）");

            var minResist = ReadIntConst("client/Assets/Scripts/Module/Player/PlayerStats.cs", "MinResist", -100);
            var maxResist = ReadIntConst("client/Assets/Scripts/Module/Player/PlayerStats.cs", "MaxResist", 75);
            Check("可达区间取自生产源码 `Module/Player/PlayerStats.cs` 的 MinResist/MaxResist（不是本文件拍的）",
                minResist == -100 && maxResist == 75, $"MinResist={minResist} MaxResist={maxResist}");

            var scale = D2Text.ScaleFor((int)UiLayoutGame.FontPx16, false, D2Text.D2Font.Font16);
            float sc;
            string kind;
            List<char> miss, s2t;
            var worst = (text: "", need: 0);
            var wrapped = new List<string>();
            for (var r = minResist; r <= maxResist; r++)
            {
                var t = r + "%";
                var n = NeedNative(t, out sc, out kind, out miss, out s2t);
                if (n > worst.need) worst = (t, n);
                if (!SingleLine(n, AvailPx(UiLayoutGame.CharResistValueW * UiLayoutGame.K, scale)))
                    wrapped.Add(t);
            }
            Check($"**可达集合穷举**：{minResist}%..{maxResist}%（{maxResist - minResist + 1} 个值）在声明值列 "
                + $"`CharResistValueW`={UiLayoutGame.CharResistValueW} art 下**全部单行**",
                wrapped.Count == 0,
                wrapped.Count == 0
                    ? $"最长值 = 「{worst.text}」need={worst.need} art < availPx "
                      + $"{AvailPx(UiLayoutGame.CharResistValueW * UiLayoutGame.K, scale)}"
                    : $"折行的值 {wrapped.Count} 个，如 {string.Join(",", wrapped.GetRange(0, Math.Min(5, wrapped.Count)).ToArray())}");

            var needWorst = NeedNative(worst.text, out sc, out kind, out miss, out s2t);
            Check("值列的最小可放宽度（按最长可达值）≤ 声明宽（余量 = 非负）",
                MinBoxArt(needWorst, scale) <= UiLayoutGame.CharResistValueW,
                $"最长值「{worst.text}」need={needWorst} art ⇒ 最小框 {MinBoxArt(needWorst, scale)} art "
                + $"≤ 声明 {UiLayoutGame.CharResistValueW} art");
            Check("值列仍**与四维数值列右沿对齐**（主 agent 2026-09-24 裁决的锚点 art 112.5，本片不许动）",
                NearOrZero(UiLayoutGame.CharResistValueX + UiLayoutGame.CharResistValueW * 0.5f,
                    UiLayoutGame.CharStatValueX + UiLayoutGame.CharStatValueW * 0.5f),
                $"抗性值列右沿 {UiLayoutGame.CharResistValueX + UiLayoutGame.CharResistValueW * 0.5f} node vs "
                + $"四维 {UiLayoutGame.CharStatValueX + UiLayoutGame.CharStatValueW * 0.5f} node（= art 112.5）");
        }

        // ── §⑤ 几何：两框不相交、都在面板内、与四维/派生行不碰 ─────────────────
        private static void SectionGeometry()
        {
            Console.WriteLine("  §⑤ 几何：标签框 × 值列框 × 原版行矩形 × 面板矩形");
            var pw = CharacterPanel.PanelSize.x;
            var ph = CharacterPanel.PanelSize.y;
            var panelLeft = CharacterPanel.PanelPos.x - pw * 0.5f;
            var panelRight = CharacterPanel.PanelPos.x + pw * 0.5f;
            var panelBottom = CharacterPanel.PanelPos.y - ph * 0.5f;

            // 框中心的**画布 x** = 面板中心 + node x × K（node 原点 = 面板矩形中心，与 `Char*RowOrig` 同口径）
            var labelL = CharacterPanel.PanelPos.x + UiLayoutGame.CharResistNameX * UiLayoutGame.K
                         - UiLayoutGame.CharResistNameW * UiLayoutGame.K * 0.5f;
            var labelR = labelL + UiLayoutGame.CharResistNameW * UiLayoutGame.K;
            var valueL = CharacterPanel.PanelPos.x + UiLayoutGame.CharResistValueX * UiLayoutGame.K
                         - UiLayoutGame.CharResistValueW * UiLayoutGame.K * 0.5f;
            var valueR = valueL + UiLayoutGame.CharResistValueW * UiLayoutGame.K;

            Check("标签框与值列框**不相交**（间隔 > 0；「压字」这一格就是两者相交的后果）",
                labelR < valueL, $"标签右沿 {labelR:0.0} < 值列左沿 {valueL:0.0}（间隔 {valueL - labelR:0.0} 画布px "
                + $"= {(valueL - labelR) / UiLayoutGame.K:0.0} art px）");

            Check("标签框左沿 == 面板左沿（原版 art x 0；见 `CharResistNameX` 的预算说明）",
                Math.Abs(labelL - panelLeft) < 0.01f, $"labelL {labelL:0.0} vs panelLeft {panelLeft:0.0}");

            Check("两框都在面板矩形内（左沿 ≥ 面板左沿、右沿 ≤ 面板右沿）",
                valueR <= panelRight + 0.01f && labelL >= panelLeft - 0.01f,
                $"标签框 [{labelL:0.0},{labelR:0.0}]、值列框 [{valueL:0.0},{valueR:0.0}]"
                + $" ⊂ 面板 [{panelLeft:0.0},{panelRight:0.0}]×[{panelBottom:0.0},{panelBottom + ph:0.0}]");

            // 与「原版 9 个行矩形」（四维 4 + 派生 4 + 防御 1）不相交 —— 本片**没动**它们，但要判"没被碰到"
            var hit = new List<string>();
            var labelCy = CharacterPanel.PanelPos.y + UiLayoutGame.CharResistRowOrig[0].y * UiLayoutGame.K;
            var labelH = UiLayoutGame.CharResistRowSize.y * UiLayoutGame.K;
            for (var i = 0; i < UiLayoutGame.CharStatRowOrig.Length; i++)
            {
                var c = CharacterPanel.PanelPos + UiLayoutGame.CharStatRowOrig[i] * UiLayoutGame.K;
                if (Overlap(labelL, labelCy - labelH * 0.5f, UiLayoutGame.CharResistNameW * UiLayoutGame.K, labelH,
                        c, UiLayoutGame.CharStatRowSize))
                    hit.Add("Stat" + i);
            }
            for (var i = 0; i < UiLayoutGame.CharDerivedRowOrig.Length; i++)
            {
                var c = CharacterPanel.PanelPos + UiLayoutGame.CharDerivedRowOrig[i] * UiLayoutGame.K;
                var size = i == 0 ? UiLayoutGame.CharDefenseSize : UiLayoutGame.CharDerivedSize;
                if (Overlap(labelL, labelCy - labelH * 0.5f, UiLayoutGame.CharResistNameW * UiLayoutGame.K, labelH,
                        c, size)
                    || Overlap(valueL, labelCy - labelH * 0.5f, UiLayoutGame.CharResistValueW * UiLayoutGame.K, labelH,
                        c, size))
                    hit.Add("Derived" + i);
            }
            Check("标签框/值列框与原版四维行、派生行矩形**不相交**（本片只动抗性行，不许碰这些行）",
                hit.Count == 0, hit.Count == 0 ? "0 处相交" : string.Join(",", hit.ToArray()));

            // 「左沿对齐四维标签隔间 art 10」在预算内**不可行** —— 把它判出来，防止将来"顺手对齐"
            var scaleChi = D2Text.ScaleFor((int)UiLayoutGame.FontPx16, true, D2Text.D2Font.Font16);
            var scaleLat = D2Text.ScaleFor((int)UiLayoutGame.FontPx16, false, D2Text.D2Font.Font16);
            var minLabel = MinBoxArt(52, scaleChi);
            var minValue = MinBoxArt(47, scaleLat);
            var rightEdgeArt = UiLayoutGame.CharStatValueX + UiLayoutGame.CharStatValueW * 0.5f + 160f; // node → art
            Check("「标签左沿对齐四维标签隔间 art 10」在 art 0..112.5 的预算内**放不下**（不是懒得对齐）",
                10 + minLabel + 1 + minValue > rightEdgeArt,
                $"10 + {minLabel}（标签最小） + 1（最小间隔） + {minValue}（值列最小） = {10 + minLabel + 1 + minValue}"
                + $" > 可用右界 {rightEdgeArt:0.#} art px");
        }

        // ── §⑥ 同类短框：只出读数（文案由数据驱动，判不成常量）────────────────
        private static void SectionOtherShortBoxes()
        {
            Console.WriteLine("  §⑥ 同类短框读数（**只看，不判**：这些框里的字符串由存档/进度数据决定；"
                + "列 = 节点 / 框宽 art / 最坏样本 / need / availPx / 结论）");
            var scaleChi = D2Text.ScaleFor((int)UiLayoutGame.FontPx16, true, D2Text.D2Font.Font16);
            var scaleLat = D2Text.ScaleFor((int)UiLayoutGame.FontPx16, false, D2Text.D2Font.Font16);

            // ⚠️ 单位（2026-09-24，P-2b 抓到的**自身**缺陷）：`…Size` 由 `UiLayoutGame.Size()` 造出 ⇒
            //   **已 ×K**（画布px）；而 `Row()` 的第 2 参是**裸 art**（内部还会再 ×K）⇒ 本组原先直接传
            //   `…Size.x` ⇒ availPx 被放大 K²≈3.24× ⇒ **读数偏乐观 = 假绿读数**（它会说"单行"而真值折行）。
            //   一律先 `/ UiLayoutGame.K` 换回 art。`CharStatValueW/CharDefValueW/CharCurMaxW/
            //   CharResistNameW/CharResistValueW` 是**裸 art** 常量 ⇒ 不动。
            Row("StatName0..3", UiLayoutGame.CharStatRowSize.x / UiLayoutGame.K * 0.58f, "力量", scaleChi);
            Row("DerivedName0", UiLayoutGame.CharDefenseSize.x / UiLayoutGame.K * 0.55f, "防御", scaleChi);
            Row("DerivedName1..3", UiLayoutGame.CharDerivedSize.x / UiLayoutGame.K * 0.55f, "耐力", scaleChi);
            Row("ExtraName0..1", UiLayoutGame.CharBottomRightSize.x / UiLayoutGame.K * 0.55f, "命中", scaleChi);
            Row("CloseLabel", UiLayoutGame.CharCloseSize.x / UiLayoutGame.K, "关闭", scaleChi);
            Row("TopRight", UiLayoutGame.CharTopRightSize.x / UiLayoutGame.K, "等级 99", scaleChi);
            Row("Band2Mid", UiLayoutGame.CharBand2MidSize.x / UiLayoutGame.K, "技能点 99", scaleChi);
            Row("Band2Right", UiLayoutGame.CharBand2RightSize.x / UiLayoutGame.K, "经验 3837739017/3837739017", scaleChi);
            Row("CharName", UiLayoutGame.CharNameSize.x / UiLayoutGame.K, "S2203805", scaleLat);
            Row("StatValue0..3", UiLayoutGame.CharStatValueW, "999", scaleLat);
            Row("DerivedValue0", UiLayoutGame.CharDefValueW, "9999", scaleLat);
            Row("ExtraValue0（命中 AR）", UiLayoutGame.CharDefValueW, "123456", scaleLat);
            Row("DerivedValue1..3", UiLayoutGame.CharCurMaxW, "9999/9999", scaleLat);
            Row("ResistName0..3", UiLayoutGame.CharResistNameW, CharacterPanel.ResistName[0], scaleChi);
            Row("ResistValue0..3", UiLayoutGame.CharResistValueW, "-100%", scaleLat);
            Console.WriteLine();
        }

        private static void Row(string node, float boxArt, string sample, float scale)
        {
            float sc; string kind; List<char> m, s;
            var need = NeedNative(sample, out sc, out kind, out m, out s);
            var avail = AvailPx(boxArt * UiLayoutGame.K, scale);
            Console.WriteLine($"      │ {node,-24} 框 {boxArt,6:0.0} art  样本「{sample}」(need {need,3} art)  "
                + $"availPx {avail,3}  ⇒ {(SingleLine(need, avail) ? "单行" : "**折行**")}");
        }

        // ── §⑦ 退化样本 ───────────────────────────────────────────────────────
        private static void SectionDegradation()
        {
            Console.WriteLine("  §⑦ 退化样本（把修前的形状喂进**同一个**判据，必须变红）");
            var scaleChi = D2Text.ScaleFor((int)UiLayoutGame.FontPx16, true, D2Text.D2Font.Font16);
            var scaleLat = D2Text.ScaleFor((int)UiLayoutGame.FontPx16, false, D2Text.D2Font.Font16);

            // ① 修前那个折行形状：46 art 标签框 + 「火焰抗性」
            List<char> miss, s2t;
            var needLabel = ChiMeasure(CharacterPanel.ResistName[0], out miss, out s2t);
            Check("退化①：**修前的 46 art 标签框**喂进判据 ⇒ 必须判成「折行」（这就是 D10 的形状）",
                !SingleLine(needLabel, AvailPx(46f * UiLayoutGame.K, scaleChi)),
                $"46 art ⇒ availPx {AvailPx(46f * UiLayoutGame.K, scaleChi)} < need {needLabel} ⇒ 折行");

            // ② 最小可放宽度的**边界**：min-1 必须折、min 必须不折
            var minLabel = MinBoxArt(needLabel, scaleChi);
            Check($"退化②：标签框 **{minLabel - 1} art 必须折行**、**{minLabel} art 必须单行**（边界两侧对立读数）",
                !SingleLine(needLabel, AvailPx((minLabel - 1) * UiLayoutGame.K, scaleChi))
                && SingleLine(needLabel, AvailPx(minLabel * UiLayoutGame.K, scaleChi)),
                $"{minLabel - 1} art ⇒ availPx {AvailPx((minLabel - 1) * UiLayoutGame.K, scaleChi)}；"
                + $"{minLabel} art ⇒ availPx {AvailPx(minLabel * UiLayoutGame.K, scaleChi)}（need {needLabel}）");

            // ③ 值列边界：41 art（比最小可放宽度小 1）⇒ `-100%` 必须折行
            var needVal = D2Text.Measure(D2Text.D2Font.Font16, "-100%");
            var minVal = MinBoxArt(needVal, scaleLat);
            Check($"退化③：值列 **{minVal - 1} art 必须折行**、**{minVal} art 必须单行**（`-100%` need {needVal} art）",
                !SingleLine(needVal, AvailPx((minVal - 1) * UiLayoutGame.K, scaleLat))
                && SingleLine(needVal, AvailPx(minVal * UiLayoutGame.K, scaleLat)),
                $"{minVal - 1} art ⇒ availPx {AvailPx((minVal - 1) * UiLayoutGame.K, scaleLat)}；"
                + $"{minVal} art ⇒ availPx {AvailPx(minVal * UiLayoutGame.K, scaleLat)}");

            // ④ 拉丁字模表 = 生产常量表：源码表被改 ⇒ 本判据立刻红（说明它绑的是真值，不是自算）
            Check("退化④：`D2Text.Measure(Font16, \"-100%\")` == 47（拉丁 advance 表被改 ⇒ 本判据立刻红）",
                needVal == 47, $"实测 {needVal} art px（'-'5 '1'5 '0'12 '0'12 '%'13）");

            // ⑤ 缺字形：判据不许把「字模里没有这个字」静默当成「放得下」。
            //    样本**现场扫出来**（不硬编码某一个字 —— 字模表更新后硬编码样本会静默失效，
            //    那正是"判据变成恒真"的又一条路）。
            const string pool = "€⿕龘㊣㍿☃亜鳳龔㊙〇";
            var missChar = '\0';
            foreach (var c in pool)
            {
                int alt;
                var viaS2T = ChiS2T.TryGetValue(c, out alt) && ChiAdvance.ContainsKey(alt);
                if (!ChiAdvance.ContainsKey(c) && !viaS2T) { missChar = c; break; }
            }
            if (missChar == '\0')
            {
                Program._skip++;
                Console.WriteLine("[SKIP] 缺字形退化样本：候选池里每个字都有字形 ⇒ 无法构造（往 pool 里加字即可）");
            }
            else
            {
                List<char> m5, s5;
                var n5 = ChiMeasure(missChar + "火", out m5, out s5);
                Check($"退化⑤：样本含字模里**没有的**字「{missChar}」时 `missing` 必须非空（⛔ 不许静默当成放得下）",
                    m5.Count > 0,
                    $"missing=[{new string(m5.ToArray())}]（need 只算了 {n5} art —— 缺字是**不画**，不等于放得下）");
            }
        }

        // ═════════════════════════════════════════════════════════════════════
        // 小工具
        // ═════════════════════════════════════════════════════════════════════
        private static bool NearOrZero(float a, float b) => Math.Abs(a - b) <= 0.01f;

        private static bool Overlap(float ax, float ay, float aw, float ah, Vector2 center, Vector2 size)
            => ax < center.x + size.x * 0.5f && ax + aw > center.x - size.x * 0.5f
            && ay < center.y + size.y * 0.5f && ay + ah > center.y - size.y * 0.5f;

        /// <summary>从生产源码里读一个 `const int Name = 值;`（判据绑源码，不绑本文件里的复制品）。</summary>
        private static int ReadIntConst(string relPath, string name, int fallback)
        {
            var p = Path.Combine(Program.ProjectRoot, relPath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(p)) return fallback;
            foreach (var raw in File.ReadAllLines(p, Encoding.UTF8))
            {
                var line = raw.Trim();
                if (!line.Contains("const") || !line.Contains(name) || !line.Contains("=")) continue;
                var i = line.IndexOf('=');
                var tail = line.Substring(i + 1).Trim().TrimEnd(';').Trim();
                int v;
                if (int.TryParse(tail, out v)) return v;
            }
            return fallback;
        }
    }
}
