// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · UI/D2Text.cs
//
// 原版**位图字体**渲染：把文本用原版字模逐字拼出来（不用引擎默认 TTF）。
//
// 两套字模（都是原版 `data/local/font/**` 的同一套机制：`.tbl` 表 + `.dc6` 图像）：
//   · 拉丁（数字 / 英文 / 半角符号）：`D2/Fonts/font{16,24,30,42}.png`
//     —— 已有的多子 sprite 图集（`AssetImporter.FontGrids` 切分），口径**未改**。
//   · 中文（汉字 / 全角标点）：`D2/Fonts/font{N}_chi.png`
//     —— 片 1 解出的**整幅图集**（每字号 13806 帧按行主序规则网格摆放），
//        帧→字符 + 排版度量在 `D2/Fonts/font{N}_chi_map.txt`（运行期数据资产，
//        不是 `.cs` 常量：13806×4 条写死不可维护，也无法"换素材不动逻辑"）。
//
// ★ 排版口径**全部有出处**（libd2 `packages/formats/src/font.zig`，同目录 `原版资源/
//   参考工程_Diablerie/libd2/`）：
//   · 步进 advance = 表里的 `width`（L67 注释原话："How far to advance after drawing it.
//     This is the whole reason the table exists."）⇒ 不猜字距。
//   · 行高 lineHeight = 一帧的最高（L141-146 `lineHeight()`）= 格子高（13/19/24/37）。
//   · 基线：字形画在基线上方，`top = y - frame.height`（L148-153）⇒ 字模格子底边 = 基线。
//   · **表里没有的字符：不画、也不推进**（L130-131 注释 + L170 `orelse continue`）
//     —— 原版自己的行为就是这样，本项目照做（并且**打 Error 日志**，不静默）。
//   · 换行：L188-216 `breakLine`（走一遍、停在"量到 ≥ 框宽"的那一字；路过空格就在空格断，
//     没空格就按能塞下的最后一个字断）。
//
// ⚠️ 一个**必须写明的事实**（本文件的中文回退表就是为它存在的）：
//   原版 chi 字模是**繁体**字集（13800 个码位：含 個/為/買/羅/營，不含 个/为/买/罗/营），
//   而本工程配表文本是**简体** ⇒ 简体字直接查**查不到字模**。
//   处理：查不到时按 `D2/Fonts/font_chi_s2t.txt`（简体→原版字形 码位映射，
//   逐条经"字模 + 原版语料"双重校验后由 `tools/d2codec/make_chifont_assets.py` 生成）
//   换成**原版的繁字形**再画 —— 画的仍是原版字模，且与 A 的繁体中文版画面字形一致。
//
// 分层：只引用 `CloverEngine` / `Diablo2.Core` / UnityEngine(.UI)。
// ─────────────────────────────────────────────────────────────────────────────

using System.Collections.Generic;
using CloverEngine;
using Diablo2.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Diablo2.UI
{
    /// <summary>原版位图字体的取模 / 排版度量（纯数据 + 异步字模缓存，可离线自检）。</summary>
    internal static class D2Text
    {
        /// <summary>可用字号（对应原版 `font{16,24,30,42}`）。</summary>
        public enum D2Font
        {
            /// <summary>16px（原版最小号，中文格子 13×13；HUD / 面板正文都用它）。</summary>
            Font16 = 0,

            /// <summary>24px（中文格子 19×19）。</summary>
            Font24 = 1,

            /// <summary>30px（中文格子 24×24）。</summary>
            Font30 = 2,

            /// <summary>42px（中文格子 37×37）。</summary>
            Font42 = 3,
        }

        /// <summary>可渲染字符范围（原版 256 字形里的可打印 ASCII 段）。</summary>
        public const int FirstChar = 32;

        /// <summary>可渲染字符范围上界（含）。</summary>
        public const int LastChar = 126;

        /// <summary>本表覆盖的字形数（95）。</summary>
        public const int GlyphCount = LastChar - FirstChar + 1;

        // ── 原版 advance 表（下标 = 字符码 - 32；单位 px）──────────────────────
        // 来源：Assets/ThirdParty/Diablo2/StreamingAssets/data/local/font/font{N}.txt
        // 提取方式（可复现）：
        //   python: nums=[int(x) for x in open('font16.txt').read().replace('\r','').replace('\n',',').split(',') if x.strip()]
        //           adv=[nums[2*i+1] for i in range(32,127)]   # 每字形 [垂直步进, 水平步进]
        private static readonly byte[] AdvFont16 =
        {
             8,  8,  7,  8,  8, 13, 12,  4,  5,  5,  6,  8,  5,  5,  5,  9,
            12,  5,  9,  8,  9,  9,  8,  8,  7,  8,  5,  5,  6,  7,  6,  8,
            11, 12,  7,  9, 10,  8,  8, 10,  9,  5,  5,  9,  8, 12, 10, 11,
             9, 12, 10,  7, 11, 12, 13, 16, 12, 12, 10,  5,  9,  5,  5,  9,
             5, 10,  7,  8,  8,  7,  7,  9,  7,  4,  4,  8,  7, 10,  9, 10,
             7, 10,  9,  7,  9, 10, 10, 13, 10, 10,  7,  6,  3,  6,  6,
        };

        private static readonly byte[] AdvFont24 =
        {
            12, 12, 11, 11, 11, 18, 17,  6,  8,  7,  8, 10,  6,  8,  6, 12,
            17,  8, 12, 12, 13, 12, 12, 12, 11, 12,  6,  8,  9,  9,  9, 12,
            17, 18, 12, 15, 15, 12, 12, 14, 14,  7,  7, 14, 11, 17, 15, 17,
            12, 17, 15, 11, 16, 18, 18, 24, 18, 18, 13,  7, 12,  7,  8, 10,
             7, 14, 11, 11, 13,  9, 10, 11, 11,  6,  6, 11, 10, 15, 13, 13,
            10, 14, 13,  9, 14, 15, 15, 20, 14, 14, 12,  9,  4, 10,  9,
        };

        private static readonly byte[] AdvFont30 =
        {
            16, 16, 14, 14, 15, 23, 21,  8, 10,  9, 11, 12,  8, 10,  8, 16,
            22,  9, 15, 15, 16, 15, 14, 15, 15, 14,  8,  8, 10, 12, 10, 15,
            21, 22, 15, 17, 18, 15, 15, 17, 16,  8,  8, 17, 14, 22, 19, 21,
            14, 21, 18, 14, 20, 23, 23, 31, 22, 22, 16,  9, 16,  8, 10, 15,
             8, 19, 12, 15, 16, 13, 12, 15, 14,  7,  8, 14, 12, 19, 16, 18,
            13, 18, 16, 13, 17, 19, 19, 24, 18, 19, 14, 12,  6, 12, 11,
        };

        private static readonly byte[] AdvFont42 =
        {
            21, 21, 18, 19, 19, 31, 28, 10, 12, 12, 14, 16, 11, 13, 10, 21,
            29, 12, 21, 20, 22, 20, 19, 20, 19, 20, 10, 11, 13, 15, 13, 19,
            28, 30, 20, 23, 24, 20, 19, 23, 22, 11, 11, 23, 19, 30, 26, 28,
            20, 28, 25, 19, 28, 31, 31, 41, 29, 29, 22, 11, 21, 11, 12, 21,
            12, 26, 17, 20, 21, 17, 17, 20, 19, 10, 10, 19, 17, 24, 21, 23,
            17, 24, 21, 16, 23, 25, 26, 33, 24, 24, 18, 16,  7, 15, 13,
        };

        /// <summary>取某字号的拉丁 advance 表。</summary>
        public static byte[] Table(D2Font font)
        {
            switch (font)
            {
                case D2Font.Font16: return AdvFont16;
                case D2Font.Font24: return AdvFont24;
                case D2Font.Font30: return AdvFont30;
                case D2Font.Font42: return AdvFont42;
                default:
                    // 新增字号却忘了登记 advance ⇒ 退回 Font16 并点名（不让排版静默错位）
                    UiLog.WarnOnce("font.unknown." + (int)font,
                        $"D2Text 未登记字号 {(int)font} 的 advance 表 ⇒ 退回 font16（请在 D2Text 补一行）");
                    return AdvFont16;
            }
        }

        /// <summary>字号对应的拉丁图集路径（`Resources/Clover/{路径}`，引擎会再加 `Clover/` 前缀）。</summary>
        public static string AtlasPath(D2Font font)
        {
            switch (font)
            {
                case D2Font.Font16: return ResPaths.Font16;
                case D2Font.Font24: return ResPaths.Font24;
                case D2Font.Font30: return ResPaths.Font30;
                case D2Font.Font42: return ResPaths.Font42;
                default: return ResPaths.Font16;
            }
        }

        /// <summary>拉丁字模格子宽（= 该图集字形最大宽 + 对齐余量，实测值）。</summary>
        public static int CellWidth(D2Font font)
        {
            switch (font)
            {
                case D2Font.Font16: return 16;
                case D2Font.Font24: return 24;
                case D2Font.Font30: return 31;
                case D2Font.Font42: return 41;
                default: return 16;
            }
        }

        /// <summary>拉丁字模格子高（= 字形高 + 2px 打包间隙，实测值；切图按此高度，不然逐行累积错位）。</summary>
        public static int CellHeight(D2Font font)
        {
            switch (font)
            {
                case D2Font.Font16: return 18;
                case D2Font.Font24: return 28;
                case D2Font.Font30: return 32;
                case D2Font.Font42: return 43;
                default: return 18;
            }
        }

        /// <summary>原版行距（`font{N}.fontsettings` 的 `m_LineSpacing`）。</summary>
        public static int LineSpacing(D2Font font)
        {
            switch (font)
            {
                case D2Font.Font16: return 16;
                case D2Font.Font24: return 24;
                case D2Font.Font30: return 30;
                case D2Font.Font42: return 42;
                default: return 16;
            }
        }

        /// <summary>某字符的拉丁排版步进（px）；超出可渲染范围时返回格子宽。</summary>
        public static int Advance(D2Font font, char c)
        {
            if (c < FirstChar || c > LastChar)
                return CellWidth(font);

            return Table(font)[c - FirstChar];
        }

        /// <summary>纯拉丁串的整串宽度（px）。</summary>
        public static int Measure(D2Font font, string text)
        {
            if (string.IsNullOrEmpty(text))
                return 0;

            var width = 0;
            for (var i = 0; i < text.Length; i++)
                width += Advance(font, text[i]);
            return width;
        }

        /// <summary>
        /// 是否**全部**字符都在 32..126 内（数字 / 英文 / 半角符号）⇒ 走拉丁字模。
        /// 含中文（或全角标点）⇒ false ⇒ 整条走 chi 字模（见 <see cref="HasGlyph"/> 的注释）。
        /// </summary>
        public static bool IsLatinOnly(string text)
        {
            if (string.IsNullOrEmpty(text))
                return false;

            for (var i = 0; i < text.Length; i++)
            {
                var c = text[i];
                if (c < FirstChar || c > LastChar)
                    return false;
            }
            return true;
        }

        // ═════════════════════════════════════════════════════════════════════
        // 中文（chi）字模：码位 → 图集格子 + 步进
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>字号对应的原版 px（16/24/30/42）。</summary>
        public static int SizeOf(D2Font font)
        {
            switch (font)
            {
                case D2Font.Font16: return 16;
                case D2Font.Font24: return 24;
                case D2Font.Font30: return 30;
                case D2Font.Font42: return 42;
                default: return 16;
            }
        }

        /// <summary>一个中文（chi）字模：图集里的格子 + 原版步进。</summary>
        internal struct ChiGlyph
        {
            /// <summary>图集里的列（格）。</summary>
            public int Col;

            /// <summary>图集里的行（格）。</summary>
            public int Row;

            /// <summary>原版步进（= 表里的 `width`，px）。</summary>
            public int Advance;
        }

        internal sealed class ChiFont
        {
            public bool Loading;                 // 正在加载（避免重复发起）
            public bool Ready;                   // 映射表 + 图集都到手
            public int Cols;                     // 图集列数（格）
            public int CellW;
            public int CellH;
            public Texture2D Atlas;              // 整幅图集
            public readonly Dictionary<int, ChiGlyph> Glyphs = new Dictionary<int, ChiGlyph>(16384);
        }

        /// <summary>已就绪的 chi 字模（图集 + 映射）；没就绪返回 null。</summary>
        internal static ChiFont ChiAtlasOf(D2Font font)
        {
            var s = Slot(font);
            return s.Ready ? s : null;
        }

        /// <summary>
        /// 某字模在图集里的 UV 矩形。
        /// <para>口径：图集按**行主序**摆放，格子 = <c>CellW×CellH</c>，第 i 帧的格子 = (i % Cols, i / Cols)；
        /// PNG 行 0 在**上**，而 Unity 的 UV (0,0) 在**左下** ⇒ y 要按 <c>1-(row+1)*CellH/H</c> 翻。</para>
        /// </summary>
        internal static Rect CellUv(D2Font font, ChiGlyph g)
        {
            var s = Slot(font);
            var w = s.Atlas != null && s.Atlas.width > 0 ? s.Atlas.width : 1;
            var h = s.Atlas != null && s.Atlas.height > 0 ? s.Atlas.height : 1;
            return new Rect(
                g.Col * (float)s.CellW / w,
                1f - (g.Row + 1) * (float)s.CellH / h,
                (float)s.CellW / w,
                (float)s.CellH / h);
        }

        private static readonly ChiFont[] ChiCache = new ChiFont[4];

        /// <summary>chi 字模格子尺寸的**兜底默认**（映射表到手后一律以表头为准）。</summary>
        private static int DefaultChiCell(D2Font font)
        {
            switch (font)
            {
                case D2Font.Font16: return 13;
                case D2Font.Font24: return 19;
                case D2Font.Font30: return 24;
                case D2Font.Font42: return 37;
                default: return 13;
            }
        }

        private static ChiFont Slot(D2Font font)
        {
            var i = (int)font;
            if (i < 0 || i >= ChiCache.Length) i = 0;
            var f = ChiCache[i];
            if (f == null)
            {
                f = new ChiFont();
                f.CellW = f.CellH = DefaultChiCell((D2Font)i);
                ChiCache[i] = f;
            }
            return f;
        }

        /// <summary>chi 字模格子高（行高 / 基线口径都按它）。</summary>
        public static int ChiCellH(D2Font font) { return Slot(font).CellH; }

        /// <summary>chi 字模格子宽。</summary>
        public static int ChiCellW(D2Font font) { return Slot(font).CellW; }

        /// <summary>chi 字模是否已就绪（映射表 + 图集都在内存里）。</summary>
        public static bool ChiReady(D2Font font) { return Slot(font).Ready; }

        /// <summary>
        /// `Game.Res` 未就绪时被**延迟**的字模（见 <see cref="EnsureChi"/>；不降级、不丢）。
        /// 由 <see cref="RetryDeferred"/> 重试 —— 逐帧被 <see cref="D2TextMirror.Update"/> 调一次。
        /// </summary>
        private static readonly List<D2Font> _deferred = new List<D2Font>();

        /// <summary>`EnsureS2T` 是否因 `Game.Res == null` 被延迟（见 <see cref="EnsureS2T"/>）。</summary>
        private static bool _s2tDeferred;

        /// <summary>
        /// 重试"因 `Game.Res` 未就绪而延迟"的字模加载（幂等、极廉价：队列空 ⇒ 一次判空返回）。
        /// <para>为什么必须有它：`EnsureChi` 的加载是异步的，而**触发重画**的
        /// `D2Label.RebuildAll()` 只在加载完成回调里调 —— 若当初因 `Game.Res == null` 直接返回，
        /// 就再没有任何人会发起加载 ⇒ 那些"启动期建好的标签"会永远空着。
        /// 调用点 = <see cref="D2TextMirror.Update"/>（每帧一次，字模一到就自愈）。</para>
        /// </summary>
        public static void RetryDeferred()
        {
            if (_deferred.Count == 0 && !_s2tDeferred) return;
            if (Game.Res == null) return;                 // 还没就绪 ⇒ 继续等（不再降级）

            for (var i = _deferred.Count - 1; i >= 0; i--)
            {
                var f = _deferred[i];
                _deferred.RemoveAt(i);
                EnsureChi(f);                             // 就绪后的加载；完成后会自动 RebuildAll
            }
            if (_s2tDeferred)
            {
                _s2tDeferred = false;
                EnsureS2T();
            }
        }

        /// <summary>触发某字号 chi 字模的异步加载（幂等；就绪后自动重画已有标签）。</summary>
        public static void EnsureChi(D2Font font)
        {
            var slot = Slot(font);
            if (slot.Ready || slot.Loading) return;

            if (Game.Res == null)
            {
                // ★★ V6 修（**全项目文字降级的唯一根因**，实机日志可复跑）：
                //   改前这里调 `OnAtlasFailure` ⇒ `D2Label.MarkBitmapUnavailable`
                //   ⇒ **`_bitmapUnavailable` 是一局之内不再恢复的静态开关** ⇒ 整个 Play 里
                //   所有文字都退化成引擎默认 TTF（原版位图字模再也回不来）。
                //   而这次触发点**每次进 Play 都会命中**（实测 11:57:46 / 12:04:27 / 12:54:41 /
                //   13:05:58 / 13:12:39 五局五次）：`Game.Launch` 期间就有业务面板建了中文标签
                //   （`CloverRes.Init` 还没调用 ⇒ `Game.Res == null`），引擎那条 `D2TextMirror` 挂钩
                //   （`UI/D2EngineTextHook.cs`）**只保护它自己接收的引擎 Text**，业务面板直接
                //   `UiArt.Label` / `D2Label.Create` 建的标签不经过它。
                //   ⇒ 这里改成**延迟**（与 `D2EngineTextHook` 的"寄存"同一思路）：不降级、不丢，
                //     记下字号，等 `Game.Res` 就绪后由 `RetryDeferred()` 重新发起加载。
                //   判据：实机 `STAGE-READY` 的 `bitmapUnavailable=0` + 日志出现
                //   `[原版中文字模] font16 就绪` 且**没有** `位图字模整体不可用` 的 Error。
                if (!_deferred.Contains(font)) _deferred.Add(font);
                slot.Loading = false;
                UiLog.WarnOnce("chifont.deferred." + SizeOf(font),
                    $"Game.Res 未初始化（CloverRes.Init 未调用）⇒ font{SizeOf(font)} 字模加载**延后**"
                    + "（不降级：不再把全项目字模永久切成系统字体）；Game.Res 就绪后由 "
                    + "`D2Text.RetryDeferred()` 自动重试（见 D2Text.EnsureChi 注释）");
                return;
            }

            slot.Loading = true;
            var mapPath = ResPaths.FontChiMap(SizeOf(font));
            Game.Res.LoadAsset<TextAsset>(mapPath, ta =>
            {
                if (ta == null)
                {
                    slot.Loading = false;
                    OnAtlasFailure($"原版中文字模映射表取不到：{mapPath}");
                    return;
                }

                ParseChiMap(slot, ta.text, font);

                var atlasPath = ResPaths.FontChi(SizeOf(font));
                Game.Res.LoadAsset<Texture2D>(atlasPath, tex =>
                {
                    slot.Loading = false;
                    if (tex == null)
                    {
                        OnAtlasFailure($"原版中文字模图集取不到：{atlasPath}");
                        return;
                    }

                    slot.Atlas = tex;
                    slot.Ready = true;
                    UiLog.Info($"[原版中文字模] font{SizeOf(font)} 就绪：码位 {slot.Glyphs.Count} 个，"
                               + $"图集 {tex.width}×{tex.height}，格子 {slot.CellW}×{slot.CellH}，列 {slot.Cols}，"
                               + $"行高 {slot.CellH}px（口径：libd2 font.zig L141-146 lineHeight）");
                    D2Label.RebuildAll();
                });
            });
        }

        /// <summary>解析 `font{N}_chi_map.txt`（表头 3 行 + 每行 `code frame advance col row`）。</summary>
        private static void ParseChiMap(ChiFont slot, string text, D2Font font)
        {
            if (string.IsNullOrEmpty(text))
            {
                OnAtlasFailure($"原版中文字模映射表是空的（font{SizeOf(font)}）");
                return;
            }

            var lines = text.Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (line.Length == 0 || line[0] == '#')
                    continue;

                if (line.StartsWith("COLS", System.StringComparison.Ordinal))
                {
                    var pc = line.Split(' ');
                    if (pc.Length >= 2) slot.Cols = ParseInt(pc[1], slot.Cols);
                    continue;
                }
                if (line.StartsWith("CELL", System.StringComparison.Ordinal))
                {
                    var p = line.Split(' ');
                    if (p.Length >= 3)
                    {
                        slot.CellW = ParseInt(p[1], slot.CellW);
                        slot.CellH = ParseInt(p[2], slot.CellH);
                    }
                    continue;
                }
                if (line.StartsWith("COUNT", System.StringComparison.Ordinal))
                    continue;

                var f = line.Split(' ');
                if (f.Length < 5)
                    continue;

                ChiGlyph g;
                g.Col = ParseInt(f[3], 0);
                g.Row = ParseInt(f[4], 0);
                g.Advance = ParseInt(f[2], slot.CellW);
                slot.Glyphs[ParseInt(f[0], -1)] = g;
            }

            if (slot.Glyphs.Count == 0)
                OnAtlasFailure($"原版中文字模映射表解析出 0 条（font{SizeOf(font)}）");
        }

        private static int ParseInt(string s, int fallback)
        {
            int v;
            return int.TryParse(s, out v) ? v : fallback;
        }

        // ── 简体 → 原版字形 码位映射（字模查不到时的**唯一**回退）───────────────
        private static Dictionary<int, int> _s2t;
        private static bool _s2tLoading;
        private static readonly HashSet<int> ReportedMissing = new HashSet<int>();

        /// <summary>
        /// 触发简繁映射表加载（幂等；就绪后重画已有标签 —— 首次显示时它可能还没到）。
        /// </summary>
        public static void EnsureS2T()
        {
            if (_s2t != null || _s2tLoading) return;

            if (Game.Res == null)
            {
                // ★ V6：与 `EnsureChi` 同一处置 —— 启动期（`CloverRes.Init` 之前）不把"暂时取不到"
                //   当成"永久缺失"，只记延迟，等 `RetryDeferred()` 再取。
                _s2tDeferred = true;
                UiLog.WarnOnce("chifont.s2t.nores",
                    "Game.Res 未初始化 ⇒ 简体→原版字形映射表（font_chi_s2t）加载**延后**（不视为缺失）；"
                    + "就绪后由 `D2Text.RetryDeferred()` 自动重试");
                return;
            }

            _s2tLoading = true;
            Game.Res.LoadAsset<TextAsset>(ResPaths.FontChiS2T, ta =>
            {
                _s2tLoading = false;
                if (ta == null)
                {
                    UiLog.ErrorOnce("chifont.s2t.missing",
                        $"简体→原版字形映射表取不到：{ResPaths.FontChiS2T} ⇒ 字模没有的简体字会不显示（不静默）");
                    return;
                }

                var map = new Dictionary<int, int>(4096);
                var lines = ta.text.Split('\n');
                for (var i = 0; i < lines.Length; i++)
                {
                    var line = lines[i].Trim();
                    if (line.Length == 0 || line[0] == '#')
                        continue;
                    if (line.StartsWith("COUNT", System.StringComparison.Ordinal))
                        continue;

                    var f = line.Split(' ');
                    if (f.Length < 2)
                        continue;
                    map[ParseInt(f[0], -1)] = ParseInt(f[1], -1);
                }

                _s2t = map;
                UiLog.Info($"[原版中文字模] 简体→原版字形 映射就绪：{map.Count} 条（font_chi_s2t）");
                D2Label.RebuildAll();
            });
        }

        /// <summary>取某字符的 chi 字模（直查 → 简体回退）。查不到返回 false。</summary>
        public static bool HasGlyph(D2Font font, char c, out ChiGlyph glyph)
        {
            var slot = Slot(font);
            if (slot.Ready && slot.Glyphs.TryGetValue(c, out glyph))
                return true;

            // 简体字：换成**原版的繁字形**再查（映射表逐条经"字模 + 原版语料"双重校验）
            if (_s2t != null)
            {
                int alt;
                if (_s2t.TryGetValue(c, out alt) && slot.Ready && slot.Glyphs.TryGetValue(alt, out glyph))
                    return true;
            }

            glyph = default(ChiGlyph);
            return false;
        }

        /// <summary>按字符取排版步进（px）：拉丁走拉丁表，其余走 chi 表；两边都没有 ⇒ 0（原版行为）。</summary>
        public static int StepOf(D2Font font, char c, bool chi)
        {
            if (!chi)
                return Advance(font, c);

            ChiGlyph g;
            return HasGlyph(font, c, out g) ? g.Advance : 0;
        }

        /// <summary>量一串的宽度（px）。<paramref name="chi"/> = 是否走 chi 字模。</summary>
        public static int MeasureNative(D2Font font, string text, bool chi)
        {
            if (string.IsNullOrEmpty(text))
                return 0;

            var w = 0;
            for (var i = 0; i < text.Length; i++)
                w += StepOf(font, text[i], chi);
            return w;
        }

        /// <summary>
        /// 换行（口径 = 原版：libd2 `font.zig` L188-216 `breakLine`）。
        /// 先把 `\n` 当硬换行拆段，再对每段贪心塞字：
        /// 走到"量到 ≥ 框宽"就断；路过空格就在**最后一个空格**断（空格不带到下一行）；
        /// 没空格（中文就是这种）就断在能塞下的最后一个字；框为 0 也至少出一个字（否则死循环）。
        /// </summary>
        public static List<string> WrapLines(D2Font font, string text, bool chi, int availPx, bool wrap)
        {
            var lines = new List<string>();
            if (string.IsNullOrEmpty(text))
                return lines;

            var paragraphs = text.Split('\n');
            for (var p = 0; p < paragraphs.Length; p++)
            {
                var s = paragraphs[p];
                if (!wrap || availPx <= 0 || MeasureNative(font, s, chi) < availPx)
                {
                    lines.Add(s);
                    continue;
                }

                var start = 0;
                while (start < s.Length)
                {
                    var w = 0;
                    var fits = 0;
                    var lastSpace = -1;
                    while (start + fits < s.Length)
                    {
                        if (s[start + fits] == ' ') lastSpace = start + fits;
                        var next = w + StepOf(font, s[start + fits], chi);
                        if (next >= availPx)
                            break;
                        w = next;
                        fits++;
                    }

                    if (start + fits >= s.Length)
                    {
                        lines.Add(s.Substring(start));
                        break;
                    }

                    if (lastSpace > start)
                    {
                        lines.Add(s.Substring(start, lastSpace - start));
                        start = lastSpace;
                        while (start < s.Length && s[start] == ' ') start++;   // 断在空格的：空格不带下去
                        continue;
                    }

                    var take = fits > 0 ? fits : 1;                            // 框太窄也至少出一个字
                    lines.Add(s.Substring(start, take));
                    start += take;
                }
            }
            return lines;
        }

        // ── 字号档位 / 缩放（画布单位 ↔ 原版 px）───────────────────────────────
        /// <summary>
        /// 画布单位字号 → 原版字号档位（按"离哪个档的原版 px 最近"选，纯函数）。
        /// 本项目画布 = 1920×1080（引擎固定），原版屏 = 800×600 ⇒ ×1.8（见 `UiLayoutFlow.Scale`）。
        /// </summary>
        public static D2Font FontFor(int canvasFontSize)
        {
            var best = D2Font.Font16;
            var bestD = float.MaxValue;
            for (var i = 0; i < 4; i++)
            {
                var f = (D2Font)i;
                var target = CellHeight(f) * 1.8f;
                var d = Mathf.Abs(target - canvasFontSize);
                if (d < bestD)
                {
                    bestD = d;
                    best = f;
                }
            }
            return best;
        }

        /// <summary>画布单位字号 → 字模缩放系数（字号 / 字模格高）。</summary>
        public static float ScaleFor(int canvasFontSize, bool chi, D2Font font)
        {
            if (canvasFontSize <= 0)
                return 1f;
            var cell = chi ? ChiCellH(font) : CellHeight(font);
            return cell <= 0 ? 1f : canvasFontSize / (float)cell;
        }

        /// <summary>整串行数（给调用方按行留高度用；口径与 <see cref="WrapLines"/> 完全一致）。</summary>
        public static int CountLines(D2Font font, string text, bool chi, int availPx, bool wrap)
        {
            return WrapLines(font, text, chi, availPx, wrap).Count;
        }

        // ── 失败通道（不静默）────────────────────────────────────────────────
        internal static void OnAtlasFailure(string reason)
        {
            D2Label.MarkBitmapUnavailable(reason);
        }

        /// <summary>报告"字模里确实没有这个字符"（每个字符只报一次，避免刷屏）。</summary>
        internal static void ReportMissing(D2Font font, char c)
        {
            if (!ReportedMissing.Add(c))
                return;
            UiLog.ErrorOnce("chifont.missing." + (int)c,
                $"原版中文字模 font{SizeOf(font)} 里没有字符 '{c}'（U+{(int)c:X4}），且简繁映射表也没有 ⇒ "
                + "按原版行为**不画**该字（libd2 font.zig L170 `orelse continue`）。"
                + "若这字确实要显示，请把它补进 font_chi_s2t（映射表由 tools/d2codec/make_chifont_assets.py 生成）");
        }
    }

    /// <summary>
    /// 一条原版字体标签：**所有**可见文字都用原版字模画（拉丁走 latin 图集，其余走 chi 图集）。
    /// <para>非 MonoBehaviour（由面板 / <c>D2TextMirror</c> 持有）。</para>
    /// </summary>
    internal sealed class D2Label
    {
        // ── 拉丁字模缓存（跨面板共享；命中即同步可用）──
        private static readonly Dictionary<string, Sprite> Glyphs = new Dictionary<string, Sprite>(System.StringComparer.Ordinal);
        private static readonly HashSet<string> Requested = new HashSet<string>(System.StringComparer.Ordinal);
        private static readonly List<D2Label> Live = new List<D2Label>();

        /// <summary>整体降级开关：字模取不到时置 true（**只可能是资源缺失**，正常工程永不触发）。</summary>
        private static bool _bitmapUnavailable;

        /// <summary>降级原因（只报一次）。</summary>
        internal static void MarkBitmapUnavailable(string reason)
        {
            if (_bitmapUnavailable) return;
            _bitmapUnavailable = true;
            UiLog.ErrorOnce("bitmap.unavailable",
                reason + " ⇒ 位图字模整体不可用，回退引擎默认字体（UIFactory.DefaultFont()）；"
                + "这是**资源缺失**的错误态，正常工程不该出现（见 D2Text 文件头）");
            RebuildAll();
        }

        /// <summary>字模（异步）就绪后重画全部标签。</summary>
        public static void RebuildAll()
        {
            for (var i = Live.Count - 1; i >= 0; i--)
            {
                var label = Live[i];
                if (label == null || label._root == null) { Live.RemoveAt(i); continue; }
                label.Render();
            }
        }

        private readonly RectTransform _root;
        private D2Text.D2Font _font;
        private TextAnchor _anchor;
        private Vector2 _size;
        private RectTransform _glyphLayer;
        private Text _fallback;
        private string _text = string.Empty;
        private Color _color = Color.white;

        /// <summary>目标字高（画布单位）。0 ⇒ 按原版 px 1:1 画（不缩放）。</summary>
        private int _fontSize;

        /// <summary>行距（画布单位）。0 ⇒ 自动（= 字模格高 × 缩放）。</summary>
        private float _linePitch;

        /// <summary>是否按框宽换行（`HorizontalWrapMode.Wrap`）。</summary>
        private bool _wrap;

        /// <summary>
        /// 强制整条走 **chi（原版中文）字模**，即使文案全是 ASCII。
        /// <para>★ 为什么需要这个开关（根因，2026 品牌署名轮实测）：
        /// **原版拉丁字模 `font{16,24,30,42}.png` 是不分大小写的** —— 码位 97..122（a..z）的格子里
        /// 放的是**缩小号的同形大写**（实测 `font24_98`＝小号 B、`font24_121`＝小号 Y，
        /// 且 g/p/q/y 一律**没有降部**：底边与基线齐平）。于是 `by clover-engine` 经拉丁字模画出来
        /// 是 `BY CLOVER-ENGINE`（小型大写），**不是逐字小写** —— 违反全局 skill §1.6 的判据。
        /// 原版**中文**字模 `font{N}_chi` 里 ASCII 是**真小写**（实测 `font24_chi` 码位 97＝真 a、
        /// 103＝带降部的 g、121＝带降部的 y）⇒ 只有它能把这一行画成逐字小写。</para>
        /// </summary>
        private bool _forceChi;

        /// <summary>是否"缩到框里"（对应 uGUI 的 `resizeTextForBestFit`）。</summary>
        private bool _bestFit;
        private int _bestFitMin;
        private int _bestFitMax;

        /// <summary>上一次渲染出来的行数（调用方按行留高度用）。</summary>
        private int _lineCount = 1;

        private D2Label(RectTransform root, D2Text.D2Font font, TextAnchor anchor, Vector2 size, int fontSize)
        {
            _root = root;
            _font = font;
            _anchor = anchor;
            _size = size;
            _fontSize = fontSize;
        }

        /// <summary>标签根节点（面板可继续改它的位置 / 显隐）。</summary>
        public RectTransform Root { get { return _root; } }

        /// <summary>根节点（与 uGUI `Text` 同名成员，便于替换）。</summary>
        public RectTransform rectTransform { get { return _root; } }

        /// <summary>根节点 GameObject。</summary>
        public GameObject gameObject { get { return _root != null ? _root.gameObject : null; } }

        /// <summary>文案（与 uGUI `Text.text` 同名）。</summary>
        public string text { get { return _text; } set { SetText(value); } }

        /// <summary>颜色（与 uGUI `Text.color` 同名）。</summary>
        public Color color { get { return _color; } set { SetColor(value); } }

        /// <summary>最近一次渲染出来的行数。</summary>
        public int LineCount { get { return _lineCount; } }

        /// <summary>目标字高（画布单位；0 = 原版 px 1:1）。</summary>
        public int fontSize { get { return _fontSize; } set { if (_fontSize != value) { _fontSize = value; Render(); } } }

        /// <summary>行距（画布单位；0 = 自动）。</summary>
        public float linePitch { get { return _linePitch; } set { if (!Mathf.Approximately(_linePitch, value)) { _linePitch = value; Render(); } } }

        /// <summary>换行策略（只有 `Wrap` 才换行，`Overflow` 一律不换）。</summary>
        public HorizontalWrapMode horizontalOverflow
        {
            get { return _wrap ? HorizontalWrapMode.Wrap : HorizontalWrapMode.Overflow; }
            set
            {
                var w = value == HorizontalWrapMode.Wrap;
                if (_wrap != w) { _wrap = w; Render(); }
            }
        }

        /// <summary>竖排溢出策略（本项目一律 Overflow；留成员只为与 `Text` 接口对齐）。</summary>
        public VerticalWrapMode verticalOverflow { get; set; }

        /// <summary>强制走 chi 字模（见 <see cref="_forceChi"/>；改它会重排）。</summary>
        public bool forceChi
        {
            get { return _forceChi; }
            set { if (_forceChi != value) { _forceChi = value; Render(); } }
        }

        /// <summary>缩到框里（对应 `Text.resizeTextForBestFit`）。</summary>
        public bool resizeTextForBestFit
        {
            get { return _bestFit; }
            set { if (_bestFit != value) { _bestFit = value; Render(); } }
        }

        /// <summary>缩放下限（画布单位字号）。</summary>
        public int resizeTextMinSize { get { return _bestFitMin; } set { _bestFitMin = value; } }

        /// <summary>缩放上限（画布单位字号）。</summary>
        public int resizeTextMaxSize { get { return _bestFitMax; } set { _bestFitMax = value; } }

        /// <summary>射线命中（本项目文字一律不挡点击，写进来只为与 `Text` 接口对齐）。</summary>
        public bool raycastTarget { get; set; }

        /// <summary>对齐（与 uGUI `Text.alignment` 同名；改它会重排）。</summary>
        public TextAnchor anchor
        {
            get { return _anchor; }
            set { if (_anchor != value) { _anchor = value; Render(); } }
        }

        /// <summary>造一条标签（居中定尺 + 中心偏移；与原 <c>UiArt.Label</c> 的位置语义一致）。</summary>
        public static D2Label Create(Transform parent, string name, string content, D2Text.D2Font font,
            TextAnchor anchor, Color colorVar, Vector2 size, Vector2 pos, int fontSize = 0)
        {
            var root = UIFactory.CreateCentered(name, parent, size, pos);
            return Attach(root, content, font, anchor, colorVar, size, fontSize);
        }

        /// <summary>
        /// 在**已有**节点上挂一条标签（节点尺寸/位置由调用方布局决定，本类不动它）。
        /// 用途：把面板里现成的那套 `Text` 节点改造成"位图字模渲染"（见 <see cref="D2TextMirror"/>）。
        /// </summary>
        public static D2Label Attach(RectTransform root, string content, D2Text.D2Font font,
            TextAnchor anchor, Color colorVar, Vector2 size, int fontSize)
        {
            var label = new D2Label(root, font, anchor, size, fontSize);
            label._color = colorVar;
            label._wrap = true;
            Live.Add(label);
            D2Text.EnsureChi(font);            // 先起加载（空文案时 Render 会提前返回，这里兜一次）
            D2Text.EnsureS2T();
            label.SetText(content);
            return label;
        }

        /// <summary>改文案（自动决定用哪套字模，并重排）。</summary>
        public void SetText(string content)
        {
            var next = content ?? string.Empty;
            if (string.Equals(next, _text, System.StringComparison.Ordinal) && _glyphLayer != null) return;
            _text = next;
            Render();
        }

        /// <summary>改颜色（重排；位数很少，开销可忽略）。</summary>
        public void SetColor(Color colorVar)
        {
            if (_color == colorVar) return;
            _color = colorVar;
            Render();
        }

        /// <summary>显隐。</summary>
        public void SetActive(bool on)
        {
            if (_root != null) _root.gameObject.SetActive(on);
        }

        /// <summary>销毁（面板关闭时调用，顺便从降级广播表里摘掉自己）。</summary>
        public void Destroy()
        {
            Live.Remove(this);
            if (_root != null)
                UnityEngine.Object.Destroy(_root.gameObject);
        }

        // ── 渲染 ─────────────────────────────────────────────────────────────
        private void Render()
        {
            if (_root == null) return;

            if (_text.Length == 0)
            {
                ClearChildren();
                _glyphLayer = null;
                _fallback = null;
                _lineCount = 1;
                return;
            }

            if (!_bitmapUnavailable)
            {
                // `_forceChi`（品牌署名行）：拉丁字模不分大小写，ASCII 也改走 chi 字模，见 _forceChi 注释
                var chi = _forceChi || !D2Text.IsLatinOnly(_text);
                if (chi)
                {
                    D2Text.EnsureChi(_font);      // 中文/混合：整条走 chi 字模
                    D2Text.EnsureS2T();

                    // ★★ V6 修（缺陷 1：实机图 `v5_03_npc_dialog.png` 里「標題 + 正文」整行画在石框**之外**）：
                    //   根因 = 「字模**在途**」这一瞬被当成了「字模**不可用**」——
                    //   chi 字模是异步加载的（`EnsureChi`），到货前 `BuildBitmap` 返回 false，
                    //   于是下面 `BuildFallback()` **用系统 TTF 顶上**画了一帧；而系统字体那条路的
                    //   排版框是坏的（见 `BuildFallback` 的 sizeDelta 注释）⇒ 字被画到框外半屏处；
                    //   等到字模到货 `RebuildAll` 才画成位图字 ⇒ 一屏里先看到"跑出框的字"。
                    //   `BuildBitmap` 自己的注释早就写明了本意：**"字模在途：本帧不画（等就绪后
                    //   RebuildAll 重画）"** —— 这里把这个本意落实：**在途就不画，且绝不用系统字体顶替**
                    //   （系统字体只允许在**真的取不到**字模时出现，那一支由 `_bitmapUnavailable` 走）。
                    //   判据：实机 `v6_01_dialog.png`（标题/正文落在石框内）+ `uicheck` 的 D2Text 断言
                    //   （`BuildFallback` 只在 `_bitmapUnavailable` 时可达）。
                    if (!D2Text.ChiReady(_font))
                    {
                        ClearChildren();
                        _glyphLayer = null;
                        _fallback = null;
                        return;
                    }
                }
                if (BuildBitmap(chi)) return;
            }

            BuildFallback();
        }

        /// <summary>
        /// 位图模式：按原版步进逐字摆字模方块。
        /// 返回 false = 字模还没到位（已发起加载），**本帧不画**（等就绪后 <see cref="RebuildAll"/> 重画）。
        /// </summary>
        private bool BuildBitmap(bool chi)
        {
            if (chi && !D2Text.ChiReady(_font))
                return false;                       // 字模在途：不画（不拿空图占位，避免"整格实心块"）

            ClearChildren();
            _fallback = null;
            _glyphLayer = UIFactory.CreateNode("Glyphs", _root);
            _glyphLayer.anchorMin = _glyphLayer.anchorMax = new Vector2(0f, 1f);   // 以标签左上角为原点
            _glyphLayer.pivot = new Vector2(0f, 1f);
            _glyphLayer.sizeDelta = Vector2.zero;

            var size = RectSize();
            var scale = _fontSize > 0 ? D2Text.ScaleFor(_fontSize, chi, _font) : 1f;
            var cellW = chi ? D2Text.ChiCellW(_font) : D2Text.CellWidth(_font);
            var cellH = chi ? D2Text.ChiCellH(_font) : D2Text.CellHeight(_font);

            // 「缩到框里」：宽度超框就整体缩一档（对应 uGUI 的 resizeTextForBestFit）
            if (_bestFit && size.x > 0f)
            {
                var need = D2Text.MeasureNative(_font, _text, chi) * scale;
                if (need > size.x)
                {
                    var minScale = _bestFitMin > 0 ? D2Text.ScaleFor(_bestFitMin, chi, _font) : 0f;
                    var fit = scale * size.x / need;
                    scale = Mathf.Max(fit, minScale);
                }
            }

            var availPx = _wrap && size.x > 0f ? Mathf.Max(1, Mathf.RoundToInt(size.x / scale)) : 0;
            var lines = D2Text.WrapLines(_font, _text, chi, availPx, _wrap);
            _lineCount = lines.Count;

            var pitch = _linePitch > 0f ? _linePitch : cellH * scale;
            var blockH = lines.Count * pitch;

            // ⚠️ **符号口径**（★ 片 4b 修）：`VerticalOrigin` 返回的是「首行顶端相对框顶边**向下**的偏移」，
            //   而下面摆字用的 anchor/pivot 都是 `(0,1)`（左上）⇒ 在 Unity 里
            //   `anchoredPosition.y` 是**向上为正**的，所以这里的 y 必须**取负**。
            //   修之前直接把 y0 当 upward 用 ⇒ **所有** Middle*/Lower* 对齐的位图文本被整体顶到框顶**以上**。
            //   实测证据（两处，都可复跑）：
            //     ① 拍①最小实验（同为 anchor(0,1)/pivot(0,1)，容器顶边 world.y = +50）：
            //        `apos.y = +8.5` ⇒ 字块中心 world.y = **49.5**（跑到框外上方）；
            //        `apos.y = -8.5` ⇒ **32.5**（框内居中位：8.5..26.5 居中于 17.5/35）。
            //     ② 实机 dump（主菜单 `Single` 按钮，1920×1080 画布，探针 `[PDUMP]`）：
            //        文字字模中心 canvas y = **539.1**，而按钮中心 = **508.5**
            //        （偏高 30.6 = 2×8.5×1.8）⇒ 实机图上文字骑在按钮上沿、一半溢出。
            //   多行同理：修前各行沿**向上**递增（顺序倒过来且与框重叠），修后自上而下、行距 = pitch。
            var yDown = VerticalOrigin(size.y, blockH);
            for (var li = 0; li < lines.Count; li++)
            {
                var line = lines[li];
                var lineW = D2Text.MeasureNative(_font, line, chi) * scale;
                var x = HorizontalOrigin(size.x, lineW);
                var y = -(yDown + li * pitch);

                for (var i = 0; i < line.Length; i++)
                {
                    var c = line[i];
                    var step = D2Text.StepOf(_font, c, chi) * scale;

                    if (chi)
                    {
                        D2Text.ChiGlyph g;
                        if (D2Text.HasGlyph(_font, c, out g))
                            PlaceChiGlyph(x, y, cellW * scale, cellH * scale, g);
                        else
                            D2Text.ReportMissing(_font, c);       // 原版行为：不画、不推进
                    }
                    else
                    {
                        PlaceLatinGlyph(x, y, cellW * scale, cellH * scale, c);
                    }

                    x += step;
                }
            }
            return true;
        }

        /// <summary>摆一个 chi 字模方块（整格 + UV 取格）。</summary>
        private void PlaceChiGlyph(float x, float y, float w, float h, D2Text.ChiGlyph g)
        {
            var slot = D2Text.ChiAtlasOf(_font);
            if (slot == null) return;

            var rt = UIFactory.CreateNode("G" + g.Col + "_" + g.Row, _glyphLayer);
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.sizeDelta = new Vector2(w, h);
            rt.anchoredPosition = new Vector2(x, y);

            var img = rt.gameObject.AddComponent<RawImage>();
            img.raycastTarget = false;
            img.color = _color;
            img.texture = slot.Atlas;
            img.uvRect = D2Text.CellUv(_font, g);
        }

        /// <summary>摆一个拉丁字模方块（沿用原有 sprite 字模口径）。</summary>
        private void PlaceLatinGlyph(float x, float y, float w, float h, char c)
        {
            var rt = UIFactory.CreateNode("G" + (int)c, _glyphLayer);
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.sizeDelta = new Vector2(w, h);
            rt.anchoredPosition = new Vector2(x, y);

            var img = rt.gameObject.AddComponent<Image>();
            img.raycastTarget = false;
            img.color = _color;
            img.type = Image.Type.Simple;
            ApplyGlyph(img, _font, c);
        }

        /// <summary>
        /// 默认字体模式。
        /// ⚠️ 只可能在"字模资源缺失"时走到（<see cref="MarkBitmapUnavailable"/> 已打 **Error**）；
        /// 正常工程里这一支**永不触发**（验收：实机日志无 `bitmap.unavailable`）。
        /// </summary>
        private void BuildFallback()
        {
            ClearChildren();
            _glyphLayer = null;
            _lineCount = Mathf.Max(1, _text.Split('\n').Length);
            var size = RectSize();
            var pt = _fontSize > 0 ? _fontSize : FallbackFontSize(_font);
            _fallback = UIFactory.CreateText("Text", _root, _text, pt, _anchor, _color);
            UIFactory.Stretch(_fallback.rectTransform);
            _fallback.raycastTarget = false;

            // ★★ V6 修（缺陷 1 的第二半：系统字体这条兜底路把字画到了框外）：
            //   原来这里还有一句 `if (size.x > 0f) _fallback.rectTransform.sizeDelta = size;` ——
            //   `Stretch` 之后子节点的矩形**已经** == 标签节点（= 排版框），再设一次 `sizeDelta`
            //   会在**拉伸锚点**下把矩形从锚框**再向外撑大** `size` ⇒ 矩形左上角跑到
            //   （节点中心 − 半宽, 节点中心 + 半高）之外 ⇒ 左上对齐的文本整行画到**框外**、
            //   居中的文本被整体上抬半框。实机证据（V6 逐节点 dump，2026-09-23 13:06）：
            //   `body` 节点 screen=(607,533)-(1302,774)，而画面上那行字出现在 screen y≈187..213
            //   （= 石框之外、屏幕上方），偏移量正是 (±W/2, ±H/2)。
            //   ⇒ 删掉这一句：兜底文本与位图文本**共用同一个框**（节点矩形），位置口径从此只有一套。
            //   判据：V6 的 `body.Glyphs` / 兜底路径取证 + 实机图 `v6_01_dialog.png`。
            if (_bestFit)
            {
                _fallback.resizeTextForBestFit = true;
                _fallback.resizeTextMinSize = _bestFitMin > 0 ? _bestFitMin : 8;
                _fallback.resizeTextMaxSize = _bestFitMax > 0 ? _bestFitMax : pt;
            }
            // ⛔ V6 已删除本方法末尾那句"给兜底文本再设一次框尺寸"的赋值（见上一条注释的实机证据）：
            //    `Stretch` 之后它的矩形**就是**标签节点的框，再设一次只会在拉伸锚点下把框撑大一倍。
            _fallback.horizontalOverflow = _wrap ? HorizontalWrapMode.Wrap : HorizontalWrapMode.Overflow;
        }

        /// <summary>整串降级为默认字体时用的字号（位图字号 → uGUI 字号，视觉接近）。</summary>
        private static int FallbackFontSize(D2Text.D2Font font)
        {
            switch (font)
            {
                case D2Text.D2Font.Font16: return 16;
                case D2Text.D2Font.Font24: return 22;
                case D2Text.D2Font.Font30: return 28;
                case D2Text.D2Font.Font42: return 38;
                default: return 16;
            }
        }

        private void ClearChildren()
        {
            if (_root == null) return;
            for (var i = _root.childCount - 1; i >= 0; i--)
                UnityEngine.Object.Destroy(_root.GetChild(i).gameObject);
        }

        /// <summary>
        /// 排版用的框尺寸：**显式传进来的优先**（流程面板按原版 px 排版后再整体 ×1.8，
        /// 传的就是原版 px）；没传（= 0）才取节点实际矩形（面板里那套 `Text` 由布局定尺）。
        /// </summary>
        private Vector2 RectSize()
        {
            if (_size.x > 0f || _size.y > 0f) return _size;
            if (_root == null) return Vector2.zero;
            return _root.rect.size;
        }

        /// <summary>水平起点（相对根节点左上角）。</summary>
        private float HorizontalOrigin(float box, float content)
        {
            switch (_anchor)
            {
                case TextAnchor.UpperLeft:
                case TextAnchor.MiddleLeft:
                case TextAnchor.LowerLeft:
                    return 0f;
                case TextAnchor.UpperRight:
                case TextAnchor.MiddleRight:
                case TextAnchor.LowerRight:
                    return box - content;
                default:
                    return (box - content) * 0.5f;
            }
        }

        /// <summary>
        /// 首行顶端（相对根节点顶边的**向下**偏移）。
        /// <para>⚠️ **返回值是「向下为正」的偏移**，与子节点 `anchoredPosition.y`（Unity 里向上为正）
        /// **符号相反** ⇒ 调用点必须取负（见 <see cref="BuildBitmap"/> 的「符号口径」注释与实测）。</para>
        /// </summary>
        private float VerticalOrigin(float box, float content)
        {
            switch (_anchor)
            {
                case TextAnchor.UpperLeft:
                case TextAnchor.UpperCenter:
                case TextAnchor.UpperRight:
                    return 0f;
                case TextAnchor.LowerLeft:
                case TextAnchor.LowerCenter:
                case TextAnchor.LowerRight:
                    return Mathf.Max(0f, box - content);
                default:
                    return Mathf.Max(0f, box - content) * 0.5f;
            }
        }

        // ── 拉丁字模加载（含整图集 LoadAll 兜底；口径未改）─────────────────────
        /// <summary>已经同步试过「整图集 LoadAll」的字号（每个字号只试一次）。</summary>
        private static readonly HashSet<D2Text.D2Font> BulkTried = new HashSet<D2Text.D2Font>();

        private static void ApplyGlyph(Image img, D2Text.D2Font font, char c)
        {
            var key = D2Text.AtlasPath(font) + "_" + (int)c;
            if (Glyphs.TryGetValue(key, out var hit))
            {
                if (img != null) img.sprite = hit;
                return;
            }

            if (!BulkTried.Contains(font) && TryBulkLoad(font, false))
            {
                BulkTried.Add(font);
                UiLog.Info($"[原版字模] font{(int)font} 逐字形按名取在本工程取不到 ⇒ 已改用整图集 LoadAll"
                           + "（同步，首帧即可显示；见 UI/D2Text.cs 的 ApplyGlyph 注释）");
            }

            if (Glyphs.TryGetValue(key, out var afterBulk))
            {
                if (img != null) img.sprite = afterBulk;
                return;
            }

            if (Requested.Contains(key)) return;
            Requested.Add(key);

            if (Game.Res == null)
            {
                MarkBitmapUnavailable($"Game.Res 未初始化（CloverRes.Init 未调用）⇒ 无法取字模 {key}");
                return;
            }

            Game.Res.LoadAsset<Sprite>(key, sp =>
            {
                if (sp != null)
                {
                    Glyphs[key] = sp;
                    if (img != null) img.sprite = sp;
                    return;
                }

                if (TryBulkLoad(font)) return;
                MarkBitmapUnavailable($"原版字模缺失：{key}（子资源名与图集均取不到）");
            });
        }

        private static bool TryBulkLoad(D2Text.D2Font font) { return TryBulkLoad(font, true); }

        private static bool TryBulkLoad(D2Text.D2Font font, bool rebuildLive)
        {
            // ★★ hud-redo3 修（**`D2Text 兜底 LoadAll 异常 … NullReferenceException` 警告洪水的根因**）：
            //   本函数是 `ApplyGlyph` 的**第一步**（`D2Text.cs:1188`），而它比后面那句
            //   `if (Game.Res == null)`（:1204）**先跑** ⇒ 启动期 `CloverRes.Init` 之前（`Game.Res == null`）
            //   每一次 `SetText` 都会在这里对 null 调 `Game.Res.LoadAll<Sprite>` ⇒ **NRE** ⇒
            //   被下面的 catch 打成一条 Warn（实测：12ms 内 3 条同文 `seq 738/739/740`，图集 `D2/Fonts/font24`）。
            //   这是**真缺陷**：一条"资源加载异常"的 Warn 在表达"资源模块还没就绪"（后者是正常启动时序，
            //   且 `EnsureChi`/`EnsureS2T` 都已按"延迟、等 `RetryDeferred()` 重试"处理）。
            //   ⇒ 这里补上判空：**没就绪就安静返回 false**（不是失败、不刷 Warn、不改 `BulkTried`），
            //     字模由 `D2Text.RetryDeferred()` 在 `Game.Res` 就绪后重新发起。
            //   ⚠️ 这不是"关日志掩盖"：真正取不到图的那条路（`all == null || all.Length == 0`、
            //   以及 `ApplyGlyph` 末尾的 `MarkBitmapUnavailable`）**原地保留且照旧报错**。
            //   判据：进一次 Play 后 `console_status` 里 `D2Text 兜底 LoadAll 异常` **0 条**，
            //   且 `bitmapUnavailable=0`（V6 的降级修不得回退）。
            if (Game.Res == null) return false;

            // ⚠️ 路径口径：引擎（`Game.Res.LoadAll`）会自己拼 `CloverRes.Init("Clover")` 的根前缀
            //   ⇒ 这里传**相对路径**（`D2/Fonts/font42`）；⛔ 不要再加 `ResPaths.Root + "/"`
            //   （那是 Unity `Resources` 的拼法，加了就变成 `Clover/Clover/…` 取不到）。
            var atlas = D2Text.AtlasPath(font);
            Sprite[] all;
            try
            {
                all = Game.Res.LoadAll<Sprite>(atlas);
            }
            catch (System.Exception ex)
            {
                UiLog.Warn($"D2Text 兜底 LoadAll 异常（图集 {atlas}）：{ex.GetType().Name}: {ex.Message}");
                return false;
            }

            if (all == null || all.Length == 0)
                return false;

            // 子 sprite 名 = `{图集**文件名**}_{序号}`（`font42_67`），**不含路径**（实测，见回报）。
            var prefix = D2Text.AtlasPath(font);
            prefix = prefix.Substring(prefix.LastIndexOf('/') + 1) + "_";
            var taken = 0;
            for (var i = 0; i < all.Length; i++)
            {
                var sp = all[i];
                if (sp == null || sp.name == null || !sp.name.StartsWith(prefix, System.StringComparison.Ordinal))
                    continue;

                Glyphs[ResPaths.D2Fonts + sp.name] = sp;
                taken++;
            }

            if (taken == 0) return false;

            UiLog.Info($"[原版字模] font{(int)font} 走整图集 LoadAll（图集 {atlas}，取到 {taken} 个字模）"
                       + "—— 本工程逐字形按名取会失败（实测），LoadAll 是同步的 ⇒ 首帧就有字");
            if (rebuildLive) RebuildAll();
            return true;
        }
    }
}
