// ═══════════════════════════════════════════════════════════════════════════
//
//   ① **帧号绑定**（源码文本，判「过程」不判「结果」）：`UI/ShopPanel.cs` 里
//      · `_repairButton` 的底图帧必须是 `2`（常态，按下 = 3），`close` 的必须是 `10`（按下 = 11）；
//      · **每个** `UiArt.SquareButton(...)` 调用点都必须在后随几行内有 `ApplyBuySellButtonArt(<同一接收者>, <帧>)`
//        —— `SquareButton` 的契约就是"底图由调用方贴"（它自己只给 `UiArt.ButtonBg` 占位色），
//        漏贴 = 纯色块上图，正是本条要挡的形状；
//   ② **判据自检（退化样本必须变红，同一判据函数不吃偏袒）**：
//      · 退化 A：把帧 `2/10` 换回 `0/0`（修前形状）⇒ `Judge` 必须判为不合格；
//      · 退化 B：删掉 `ApplyBuySellButtonArt` 整行 ⇒ 未绑定数 ≥ 1 ⇒ 必须判为不合格；
//        防"判据其实在吃真文件的偶然排版/注释"（只跑真文件+负样本时，这两向都可能同真同假）。
//   ③ **帧素材**：`Resources/Clover/D2/UI/Panel/buysellbtn_{0,1,2,3,10,11}.png` 在盘、
//      导入设置与已验证可加载的 `buyselltabs_0.png` 逐项一致（Sprite/Single/Point）、
//      且**逐像素**证明「帧 2/3/10/11 是**带图形**的钮」（墨量 ≥ 150），
//      「帧 0/1 是空白石钮」（墨量 ≤ 80）、「帧 2 与帧 10 是两个**不同**的图形」（差异 ≥ 100）。
//
//  为什么能离线判：① 是源码文本断言（与 `V6Check` 同口径：去注释后再判）；
//  ③ 是解 PNG 像素（宿主已链 Unity 托管 DLL，不需要 Unity 运行时）。
//
//  **结论已硬编码在下面常量里，判据不依赖该脚本存在**；原始输出是一次性件、已不在盘）：
//    frame  32x32  不透明 992(常态)/900(按下)  内区墨量 = 0:21  1:45  2:228  3:237  10:264  11:284
//    两两差异 = diff(0,2)=217  diff(0,10)=277  diff(2,10)=265  diff(2,3)=352  diff(10,11)=358
//    ⇒ 阈值取 150 / 80 / 100（图形帧最低 186、空白帧 21 ⇒ 双向余量都很大）。
//     （`策划/验收表.md` E4 行 / `策划/差异登记.tsv` E4 条，8× 最近邻放大联络图）；
//     本条判据只机械证明"这四帧**确实带图形**、且与空白石钮/彼此都不同"，
//     语义命名仍引 E4 的既有出处，不在这里替它下结论。
// ═══════════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using Diablo2.UI;
using UnityEngine;

namespace Uicheck
{
    internal static class ShopArtCheck
    {
        private static void Check(string what, bool ok, string detail) => Program.Check(what, ok, detail);

        /// <summary>图形帧的内区墨量下限（实测图形帧 186~284、空白石钮帧 21）。</summary>
        private const int GlyphMinInk = 150;

        /// <summary>空白石钮（帧 0/1）的内区墨量上限（实测 21 / 45）。</summary>
        private const int BlankMaxInk = 80;

        /// <summary>两帧"不是同一个图形"的最小差异像素数（实测最小 217）。</summary>
        private const int MinFrameDiff = 100;

        /// <summary>修理 = 常态帧 2（按下 3）；关闭 = 常态帧 10（按下 11）。出处 = `策划/验收表.md` E4。</summary>
        private const int RepairFrame = 2;
        private const int CloseFrame = 10;

        private static readonly int[] BlankFrames = { 0, 1 };

        public static void Run()
        {
            Console.WriteLine("── (u53-shopart) 商店修理/关闭按钮底图 = 原版 buysellbtn 图形帧（帧号绑定 + 退化样本 + 帧像素）──");
            var src = File.ReadAllText(Path.Combine(Program.UiDir, "ShopPanel.cs"));
            CheckFrameBindings(src);
            CheckPolarity(src);
            CheckFrameAssets();
            CheckConsumerScan();
            CheckNoVisibleLabel();
            CheckSlotGeometry();
            Console.WriteLine();
        }

        // ═════════════════════════════════════════════════════════════════════
        // ① 帧号绑定（源码文本断言；纯函数 ⇒ ② 的退化样本能喂进同一判据）
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>`Judge` 的判据结果（每个字段都是"从源码里读出来的事实"，不含任何自评）。</summary>
        private sealed class JudgeResult
        {
            public readonly List<string> SquareButtons = new List<string>();      // 接收者（无法判定时 = "?"）
            public readonly List<string> Applied = new List<string>();            // "接收者=帧号"
            public readonly List<int> AppliedFrames = new List<int>();
            public bool UsesBlankFrame;                                          // 出现 0/1 帧
            public bool PressedDerivedFromNormal;                                // 方法体内有 normalFrame + 1
            public bool EvenGuard;                                               // 方法体内有偶数/范围守卫
            public bool DerivesPressedSprite;                                    // 方法体内真的取了 pressed 帧

            /// <summary>未绑定底图的方钮数（每个 SquareButton 调用点必须各有一次 art 应用）。</summary>
            public int Unbound { get { return Math.Max(0, SquareButtons.Count - Applied.Count); } }

            public bool Ok { get { return Unbound == 0 && !UsesBlankFrame; } }
        }

        /// <summary>去注释（与 `V6Check.Strip` 同口径：逐行丢注释行 + 去行尾注释）。</summary>
        private static string Strip(string src)
        {
            if (string.IsNullOrEmpty(src)) return string.Empty;
            var sb = new StringBuilder(src.Length);
            foreach (var rawLine in src.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
            {
                var t = rawLine.TrimStart();
                if (t.StartsWith("//", StringComparison.Ordinal) || t.StartsWith("/*", StringComparison.Ordinal)
                    || t.StartsWith("*", StringComparison.Ordinal) || t.StartsWith("*/", StringComparison.Ordinal))
                    continue;
                var i = rawLine.IndexOf("//", StringComparison.Ordinal);
                sb.Append(i >= 0 ? rawLine.Substring(0, i) : rawLine).Append('\n');
            }
            return sb.ToString();
        }

        /// <summary>
        /// **判据本体（纯函数）**：从 `ShopPanel.cs` 源码文本里读出
        /// 「方钮调用点 / 各自的底图帧 / 是否用了空白帧 / 按下帧是不是从常态帧 +1 推出来的」。
        /// 同一份函数必须对"修后源码"绿、对"退化样本"红（见 <see cref="CheckPolarity"/>）。
        /// </summary>
        private static JudgeResult Judge(string source)
        {
            var res = new JudgeResult();
            var lines = Strip(source).Split('\n');

            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                if (line.IndexOf("UiArt.SquareButton(", StringComparison.Ordinal) >= 0)
                {
                    // 接收者 = 调用点同一行的赋值目标（`X = UiArt.SquareButton(` 或 `var X = …`）
                    var m = Regex.Match(line, @"([A-Za-z_][A-Za-z0-9_\.]*|[A-Za-z_][A-Za-z0-9_]*)\s*=\s*(?:UiArt\.)?SquareButton\(");
                    var recv = m.Success ? m.Groups[1].Value : "?";
                    res.SquareButtons.Add(recv);

                    // 后随 6 行内必须有一次对**同一接收者**的底图应用
                    for (var k = i + 1; k <= i + 6 && k < lines.Length; k++)
                    {
                        var am = Regex.Match(lines[k],
                            @"ApplyBuySellButtonArt\(\s*([A-Za-z_][A-Za-z0-9_]*)\s*,\s*([0-9]+)\s*\)");
                        if (!am.Success) continue;
                        if (am.Groups[1].Value != recv) continue;
                        var frame = int.Parse(am.Groups[2].Value);
                        res.Applied.Add(recv + "=" + frame);
                        res.AppliedFrames.Add(frame);
                        if (Array.IndexOf(BlankFrames, frame) >= 0) res.UsesBlankFrame = true;
                        break;
                    }
                }
            }

            // 按下帧必须由常态帧推出（判"过程"：+1 且带偶数守卫），而不是另给一个魔数
            var body = Regex.Match(Strip(source),
                @"private static void ApplyBuySellButtonArt\(Image img, int normalFrame\)(.*?)\n        \}",
                RegexOptions.Singleline);
            if (body.Success)
            {
                var b = body.Groups[1].Value;
                res.PressedDerivedFromNormal = b.IndexOf("normalFrame + 1", StringComparison.Ordinal) >= 0;
                res.EvenGuard = b.IndexOf("normalFrame % 2 != 0", StringComparison.Ordinal) >= 0;
                res.DerivesPressedSprite = b.IndexOf("var pressed =", StringComparison.Ordinal) >= 0;
            }

            return res;
        }

        private static void CheckFrameBindings(string src)
        {
            var before = File.Exists(Path.Combine(Program.UiDir, "ShopPanel.cs"));
            Check("被测文件在位：client/Assets/Scripts/UI/ShopPanel.cs 可读", before,
                before ? Program.UiDir : "缺失");

            var r = Judge(src);
            var applied = string.Join(",", r.Applied.ToArray());

            Check($"① 修理/关闭两颗方钮都绑了底图帧（每个 `UiArt.SquareButton` 调用点各一次 art 应用）"
                + $"，未绑定数 == 0",
                r.Unbound == 0,
                $"SquareButton={r.SquareButtons.Count} 调用点，art 应用=[{applied}]，未绑定={r.Unbound}");

            Check($"① 修理钮（`_repairButton`）常态帧 == {RepairFrame}（按下 = {RepairFrame + 1}）",
                r.AppliedFrames.Count >= 1 && r.AppliedFrames[0] == RepairFrame,
                r.AppliedFrames.Count >= 1 ? "读到 " + r.Applied[0] : "未读到 art 应用");

            Check($"① 关闭钮（`close`）常态帧 == {CloseFrame}（按下 = {CloseFrame + 1}）",
                r.AppliedFrames.Count >= 2 && r.AppliedFrames[1] == CloseFrame,
                r.AppliedFrames.Count >= 2 ? "读到 " + r.Applied[1] : "未读到第二处 art 应用");

            Check("① ⛔ 不许再用空白石钮帧 0/1（旧实现的错处；帧 2/10 才是带图形的钮）",
                !r.UsesBlankFrame, r.UsesBlankFrame ? "命中帧 0/1" : "0 命中");

            Check("① 按下帧由常态帧推出（方法体内 `normalFrame + 1` + 偶数守卫 `normalFrame % 2 != 0` + 真的取了 pressed 帧）"
                + "—— 防「另给一个魔数按下帧」",
                r.PressedDerivedFromNormal && r.EvenGuard && r.DerivesPressedSprite,
                $"normalFrame+1={r.PressedDerivedFromNormal} evenGuard={r.EvenGuard} pressedVar={r.DerivesPressedSprite}");
        }

        // ═════════════════════════════════════════════════════════════════════
        // ② 判据自检：同一判据喂退化样本必须变红
        // ═════════════════════════════════════════════════════════════════════
        private static void CheckPolarity(string src)
        {
            var real = Judge(src);
            Check("② 判据自检（第一向 真文件 ⇒ 绿）：真源码喂进 `Judge` ⇒ 合格"
                + "（本例必须先绿，否则下面的样本没有意义）",
                real.Ok, $"unbound={real.Unbound} blankFrame={real.UsesBlankFrame}");

            // 为什么必须有这一向：只跑「真文件 + 负样本」时，判据完全可能在**吃真文件的偶然排版/注释**
            // （真文件恰好带某种格式才匹配 ⇒ 换一份等价写法就假红，或负样本红是因为另一条无关规则）。
            // 本向用的接收者名 `fake`、帧号 `4` 都是**真文件里不存在**的字面量 ⇒ 绿只能来自判据本身正确。
            var pos = string.Join("\n", new string[] {
                "        private static void ApplyBuySellButtonArt(Image img, int normalFrame)",
                "        {",
                "            if (normalFrame % 2 != 0) return;",
                "            var pressed = normalFrame + 1;",
                "            img.sprite = SpriteFor(normalFrame);",
                "        }",
                "        private void Build(Transform parent)",
                "        {",
                "            var fake = UiArt.SquareButton(parent, \"fake\");",
                "            ApplyBuySellButtonArt(fake, 4);",
                "        }"
            });
            var rp = Judge(pos);
            Check("② 判据自检（第三向 正例片段 ⇒ 绿）：最小合成源码（接收者 `fake` / 帧 `4` 均为真文件里不存在的字面量）"
                + "喂进**同一判据** ⇒ 必须合格",
                rp.Ok && rp.Unbound == 0 && rp.AppliedFrames.Count == 1 && rp.AppliedFrames[0] == 4
                && rp.PressedDerivedFromNormal && rp.EvenGuard && rp.DerivesPressedSprite,
                $"ok={rp.Ok} unbound={rp.Unbound} frames=[{string.Join(",", rp.Applied.ToArray())}]"
                + $" normalFrame+1={rp.PressedDerivedFromNormal} evenGuard={rp.EvenGuard} pressedVar={rp.DerivesPressedSprite}");

            // 退化 A：帧号换回修前的 0（空白石钮）
            var degA = src.Replace("ApplyBuySellButtonArt(_repairButton, " + RepairFrame + ")",
                                   "ApplyBuySellButtonArt(_repairButton, 0)")
                          .Replace("ApplyBuySellButtonArt(close, " + CloseFrame + ")",
                                   "ApplyBuySellButtonArt(close, 0)");
            var ra = Judge(degA);
            Check("② 退化样本 A（帧 2/10 → 0/0 = 修前形状）喂进**同一判据** ⇒ 必须变红",
                degA != src && !ra.Ok,
                $"源码被替换={degA != src} ok={ra.Ok} blankFrame={ra.UsesBlankFrame} frames=[{string.Join(",", ra.Applied.ToArray())}]");

            // 退化 B：删掉关闭钮那一次 art 应用（= 漏贴底图 ⇒ 纯色块上图）
            var degB = src.Replace("ApplyBuySellButtonArt(close, " + CloseFrame + ");", ";");
            var rb = Judge(degB);
            Check("② 退化样本 B（删掉关闭钮的 art 应用 = 漏贴底图）喂进**同一判据** ⇒ 未绑定数 ≥ 1 ⇒ 必须变红",
                degB != src && !rb.Ok && rb.Unbound >= 1,
                $"源码被替换={degB != src} ok={rb.Ok} unbound={rb.Unbound}");
        }

        // ═════════════════════════════════════════════════════════════════════
        // ③ 帧素材：在盘 + 导入设置 + 逐像素（带图形 / 空白 / 两个不同图形）
        // ═════════════════════════════════════════════════════════════════════
        private static string FrameDir
        {
            get { return Path.Combine(Program.ResourceRoot, "Clover", "D2", "UI", "Panel"); }
        }

        private static void CheckFrameAssets()
        {
            var frames = new List<int>();
            frames.AddRange(BlankFrames);
            frames.Add(RepairFrame);
            frames.Add(RepairFrame + 1);
            frames.Add(CloseFrame);
            frames.Add(CloseFrame + 1);

            var missing = new List<string>();
            var px = new Dictionary<int, byte[]>();
            var sizes = new Dictionary<int, string>();
            foreach (var f in frames)
            {
                var file = Path.Combine(FrameDir, "buysellbtn_" + f + ".png");
                if (!File.Exists(file)) { missing.Add(Path.GetFileName(file)); continue; }
                int w, h; byte[] rgba;
                if (!TryDecodeRgba(file, out w, out h, out rgba)) { missing.Add(Path.GetFileName(file) + "(解码失败)"); continue; }
                px[f] = rgba; sizes[f] = w + "x" + h;
            }

            Check("③ 六帧单帧文件在盘且可解 RGBA：buysellbtn_{0,1,2,3,10,11}.png",
                missing.Count == 0,
                missing.Count == 0 ? string.Join(" ", ToArray(sizes)) : "缺 " + string.Join(",", missing.ToArray()));
            if (missing.Count > 0) return;

            var badSize = string.Empty;
            foreach (var f in frames) if (sizes[f] != "32x32") badSize += "buysellbtn_" + f + "=" + sizes[f] + " ";
            Check("③ 六帧尺寸全 = 32×32（= 原版 `buysellbtn.DC6` 单帧实测；E4 记录值）",
                badSize.Length == 0, badSize.Length == 0 ? "6/6 = 32x32" : "异常：" + badSize);

            // 导入设置：与已验证可加载的 buyselltabs_0.png 逐项一致（否则"按名加载失效"会重演）
            var refMeta = Path.Combine(FrameDir, "buyselltabs_0.png.meta");
            var keys = new[] { "textureType", "spriteMode", "filterMode", "spritePixelsToUnits", "alphaIsTransparency" };
            var metaBad = new List<string>();
            foreach (var f in frames)
            {
                var meta = Path.Combine(FrameDir, "buysellbtn_" + f + ".png.meta");
                if (!File.Exists(meta)) { metaBad.Add("buysellbtn_" + f + ".meta 缺"); continue; }
                foreach (var k in keys)
                {
                    if (MetaVal(meta, k) != MetaVal(refMeta, k)) metaBad.Add("f" + f + "." + k);
                }
            }
            Check("③ 六帧的导入设置与 `buyselltabs_0.png` 逐项一致（textureType/spriteMode/filterMode/PixelsToUnits/alphaIsTransparency）"
                + " ⇒ 「单帧按名加载」这条路的前提成立",
                metaBad.Count == 0, metaBad.Count == 0 ? "5 项 × 6 帧全一致" : "不一致：" + string.Join(",", metaBad.ToArray()));

            // 像素：图形帧带图形 / 空白帧是空白 / 两个图形不同
            var det = new StringBuilder();
            foreach (var f in frames) det.Append("f").Append(f).Append("=").Append(Ink(px[f])).Append(' ');
            var glyphBad = string.Empty;
            foreach (var f in new[] { RepairFrame, RepairFrame + 1, CloseFrame, CloseFrame + 1 })
                if (Ink(px[f]) < GlyphMinInk) glyphBad += "f" + f + "(" + Ink(px[f]) + ") ";
            Check($"③ 帧 {RepairFrame}/{RepairFrame + 1}/{CloseFrame}/{CloseFrame + 1} 的内区墨量 ≥ {GlyphMinInk}"
                + "（= 钮上**有图形**，不是空白石钮）",
                glyphBad.Length == 0, glyphBad.Length == 0 ? det.ToString().Trim() : "墨量不够：" + glyphBad);

            var blankBad = string.Empty;
            foreach (var f in BlankFrames) if (Ink(px[f]) > BlankMaxInk) blankBad += "f" + f + "(" + Ink(px[f]) + ") ";
            Check($"③ 帧 0/1（空白石钮）的内区墨量 ≤ {BlankMaxInk} —— 这条同时是**退化样本的下界**："
                + "把 0/1 当修理/关闭图形喂进来必然判定不合规",
                blankBad.Length == 0, blankBad.Length == 0 ? det.ToString().Trim() : "空白帧墨量偏高：" + blankBad);

            var d02 = Diff(px[0], px[RepairFrame]);
            var d010 = Diff(px[0], px[CloseFrame]);
            var d210 = Diff(px[RepairFrame], px[CloseFrame]);
            Check($"③ 帧 0（空白）与帧 {RepairFrame} / 帧 {CloseFrame} 是两个不同画面（差异 ≥ {MinFrameDiff}）"
                + $"；且帧 {RepairFrame} 与帧 {CloseFrame} 彼此不同（修理 ≠ 关闭图形）",
                d02 >= MinFrameDiff && d010 >= MinFrameDiff && d210 >= MinFrameDiff,
                $"diff(0,{RepairFrame})={d02} diff(0,{CloseFrame})={d010} diff({RepairFrame},{CloseFrame})={d210}");
        }

        private static string[] ToArray(Dictionary<int, string> d)
        {
            var list = new List<string>();
            foreach (var kv in d) list.Add("f" + kv.Key + "=" + kv.Value);
            list.Sort();
            return list.ToArray();
        }

        private static string MetaVal(string metaFile, string key)
        {
            if (!File.Exists(metaFile)) return "(meta 缺)";
            foreach (var line in File.ReadAllLines(metaFile))
            {
                var t = line.Trim();
                if (t.StartsWith(key + ":", StringComparison.Ordinal)) return t;
            }
            return "(未找到 " + key + ")";
        }

        /// <summary>内区墨量 = 内圈（去 5px 边框）不透明像素里，亮度偏离"该帧内区中位亮度"超过 24 的像素数。</summary>
        private static int Ink(byte[] rgba)
        {
            // 先按 32×32 的内区收样本（尺寸已单独断言过）
            var lums = new List<float>();
            var idx = new List<int>();
            for (var y = 5; y <= 32 - 6; y++)
            {
                for (var x = 5; x <= 32 - 6; x++)
                {
                    var o = (y * 32 + x) * 4;
                    if (rgba[o + 3] <= 8) continue;
                    lums.Add(0.299f * rgba[o] + 0.587f * rgba[o + 1] + 0.114f * rgba[o + 2]);
                    idx.Add(o);
                }
            }
            if (lums.Count == 0) return 0;
            var sorted = new List<float>(lums);
            sorted.Sort();
            var med = sorted[sorted.Count / 2];
            var ink = 0;
            for (var i = 0; i < lums.Count; i++) if (Math.Abs(lums[i] - med) > 24f) ink++;
            return ink;
        }

        /// <summary>两帧 RGBA 逐像素最大分量差 &gt; 24 的像素数（同尺寸 32×32）。</summary>
        private static int Diff(byte[] a, byte[] b)
        {
            var n = 0;
            var lim = Math.Min(a.Length, b.Length) / 4;
            for (var i = 0; i < lim; i++)
            {
                var o = i * 4;
                var d = Math.Max(Math.Max(Math.Abs(a[o] - b[o]), Math.Abs(a[o + 1] - b[o + 1])),
                                 Math.Max(Math.Abs(a[o + 2] - b[o + 2]), Math.Abs(a[o + 3] - b[o + 3])));
                if (d > 24) n++;
            }
            return n;
        }

        // ═════════════════════════════════════════════════════════════════════
        // ④ 消费点扫描：全仓"按钮底板"这条路必须都落到原版帧上
        //    （`UiArt.ButtonBg` 是**唯一**的占位色常量；它只允许作为异步在途/缺图时的兜底，
        //     不允许有哪条按钮工厂"只给占位色、不贴原版帧"）
        // ═════════════════════════════════════════════════════════════════════
        private static void CheckConsumerScan()
        {
            var uiDir = Program.UiDir;
            var src = File.ReadAllText(Path.Combine(uiDir, "UiArt.cs"));
            var s = Strip(src);

            // SquareButton 自己**不**贴图（契约：调用方贴）⇒ 全仓每个调用点都必须在 ShopPanel 里被 art 应用。
            // 口径：`UiArt.cs` 内是**定义**（`public static Image SquareButton(`），定义里**不许**再出现
            //   `UiArt.SquareButton(`（那会是自己调自己的死循环）；调用点只应出现在 ShopPanel。
            var squareCall = Regex.Matches(s, @"UiArt\.SquareButton\(").Count;
            var scripts = Directory.GetFiles(Path.Combine(Program.ProjectRoot, "client", "Assets", "Scripts"),
                "*.cs", SearchOption.AllDirectories);
            var squareSites = 0;
            var inShop = 0;
            foreach (var f in scripts)
            {
                var txt = Strip(File.ReadAllText(f));
                var n = Regex.Matches(txt, @"SquareButton\(").Count
                        - Regex.Matches(txt, @"static Image SquareButton\(").Count;
                if (n <= 0) continue;
                squareSites += n;
                if (Path.GetFileName(f) == "ShopPanel.cs") inShop += n;
            }
            Check("④ `UiArt.SquareButton`（自带占位色、底图由调用方贴）的全部调用点都在 ShopPanel 内，"
                + "且调用点数 == art 应用数（⛔ 不留「只给占位色」的方钮）",
                squareCall == 0 && squareSites == 2 && inShop == 2,
                $"UiArt.cs 内自调用={squareCall}（应 0）全仓调用点={squareSites}（应 2，ShopPanel 内 {inShop}）");

            //   ★ 落点名称随下沉更新：`ApplyButtonFrame` → `ApplyButtonFrames`（帧表 → 按钮语义，项目侧；
            //     四态落进 uGUI 那半在引擎 `SpriteSwapButton.Apply`）。判的仍是「这条工厂真的贴了原版帧」。
            Check("④ 另两条按钮工厂（`UiArt.Button` / `OrigButton`）都真的贴了原版帧（过程断言）",
                s.IndexOf("ApplyButtonFrames(", StringComparison.Ordinal) >= 0
                && s.IndexOf("LoadOrig(", StringComparison.Ordinal) >= 0
                && src.IndexOf("UiArt.TradeButton", StringComparison.Ordinal) < 0,
                $"ApplyButtonFrames={s.IndexOf("ApplyButtonFrames(", StringComparison.Ordinal) >= 0}"
                + $" LoadOrig={s.IndexOf("LoadOrig(", StringComparison.Ordinal) >= 0}"
                + $" 旧 tradebtn 控件={src.IndexOf("UiArt.TradeButton", StringComparison.Ordinal) >= 0}");
        }

        // ═════════════════════════════════════════════════════════════════════
        //   判据本体 = 纯函数 `LabelPolicyBad(src)`（源码级：两颗动作钮不许有常显文字节点、
        //   工厂不再收 `ButtonLabelRect`、悬停提示条数 == 动作钮颗数）；退化样本喂同一函数必须变红。
        //   几何那半 = 钮矩形（按钮 local 空间）平移到面板 local 空间后比 `panel` / 金币名牌。
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>
        /// **判据本体（源码级纯函数）**：商店底部两颗动作钮**不许有常显文字节点** —— 原版那两个槽
        /// 只有图形（帧 2 = 锤+铁砧、帧 10 = ⊘），文字只该出现在悬停提示里。
        /// <para>读三件事：① 每个 `UiArt.SquareButton(` 调用点之后必须摘掉工厂建的 `Label`
        /// （`DropVisibleLabel(<接收者>)`）；② 不许再给工厂传 `ButtonLabelRect(...)`；
        /// ③ `ControlTip.Create(` 的条数 == 动作钮颗数。返回"不合规"清单（空 = 合格）。</para>
        /// </summary>
        private static List<string> LabelPolicyBad(string src)
        {
            var s = Strip(src);
            var bad = new List<string>();
            var lines = s.Split('\n');
            var want = 0;
            for (var i = 0; i < lines.Length; i++)
            {
                if (lines[i].IndexOf("UiArt.SquareButton(", StringComparison.Ordinal) < 0) continue;
                var m = Regex.Match(lines[i], @"([A-Za-z_][A-Za-z0-9_]*)\s*=\s*(?:UiArt\.)?SquareButton\(");
                if (!m.Success) { bad.Add("第 " + (i + 1) + " 行调用点认不出接收者"); continue; }
                var recv = m.Groups[1].Value;
                want++;
                var dropped = false;
                for (var k = i + 1; k <= i + 8 && k < lines.Length; k++)
                {
                    if (lines[k].IndexOf("DropVisibleLabel(" + recv + ")", StringComparison.Ordinal) >= 0)
                    { dropped = true; break; }
                }
                if (!dropped) bad.Add(recv + " 的常显文字没摘（缺 `DropVisibleLabel(" + recv + ")`）");
            }
            if (want == 0) bad.Add("没有读到任何 `UiArt.SquareButton(` 调用点（判据空转 ⇒ 不许绿）");
            if (Regex.IsMatch(s, @"ButtonLabelRect\(")) bad.Add("仍在给工厂传 `ButtonLabelRect(...)`");
            var tips = Regex.Matches(s, @"ControlTip\.Create\(").Count;
            if (tips != want) bad.Add("悬停提示 " + tips + " 条 ≠ 动作钮 " + want + " 颗");
            return bad;
        }

        /// <summary>
        /// 矩形包含（`outer` 完整包住 `inner`）。为什么不用 `Rect.Contains(Rect)`：
        /// 本机 Unity 托管 DLL 里 `Rect.Contains` **只有 `Vector2` / `Vector3` 重载**，
        /// 没有 rect 版（实测 CS1503）⇒ 这里自己写四边比较（含 0.001 容差）。
        /// </summary>
        private static bool ContainsAll(Rect outer, Rect inner)
            => inner.xMin >= outer.xMin - 0.001f && inner.xMax <= outer.xMax + 0.001f
            && inner.yMin >= outer.yMin - 0.001f && inner.yMax <= outer.yMax + 0.001f;

        /// <summary>把按钮 local 空间的矩形平移到面板 local 空间（按钮中心 = <paramref name="center"/>）。</summary>
        private static Rect ToPanelSpace(Rect inButtonSpace, Vector2 center)
            => new Rect(center.x + inButtonSpace.xMin, center.y + inButtonSpace.yMin,
                        inButtonSpace.width, inButtonSpace.height);

        private static void CheckNoVisibleLabel()
        {
            Console.WriteLine("── (u53-shopart ⑤) 商店底部两颗动作钮：无常显文字节点 + 各挂一条悬停提示 ──");

            var panel = new Rect(-ShopPanel.PanelSize.x * 0.5f, -ShopPanel.PanelSize.y * 0.5f,
                                 ShopPanel.PanelSize.x, ShopPanel.PanelSize.y);
            var info = new Rect(UiLayoutGame.ShopInfoBarPos.x - UiLayoutGame.ShopInfoBarSize.x * 0.5f,
                                UiLayoutGame.ShopInfoBarPos.y - UiLayoutGame.ShopInfoBarSize.y * 0.5f,
                                UiLayoutGame.ShopInfoBarSize.x, UiLayoutGame.ShopInfoBarSize.y);
            var slots = UiLayoutGame.ShopBottomSlotX;

            // ① 几何（纯函数）：四个雕槽的**钮矩形**都在面板内 + 不压金币名牌
            var bad = new List<string>();
            var det = new StringBuilder();
            for (var i = 0; i < slots.Length; i++)
            {
                var center = ShopPanel.SlotCenter(i);       // 钮中心（⛔ 不用陈旧的 `ShopBottomSlotY` = 槽顶沿）
                var b = ToPanelSpace(ShopPanel.ButtonRect(i), center);
                det.Append("槽").Append(i).Append("[钮(").Append(b.xMin.ToString("0.#")).Append(',')
                   .Append(b.yMin.ToString("0.#")).Append(") ").Append(b.width.ToString("0.#")).Append('×')
                   .Append(b.height.ToString("0.#")).Append("] ");
                if (!ContainsAll(panel, b)) bad.Add("槽" + i + "(越面板)");
                if (b.Overlaps(info)) bad.Add("槽" + i + "(压金币名牌)");
            }
            Check($"⑤ 四个雕槽：钮矩形整块落在面板内、且不压金币名牌（面板 = {panel.width:0.#}×{panel.height:0.#}）",
                bad.Count == 0, bad.Count == 0 ? det.ToString().Trim() : "不合规：" + string.Join(",", bad.ToArray()));

            // ② 口径（源码级）：两颗钮**不许有常显文字节点** + 各挂一条悬停提示（判据 `LabelPolicyBad`）
            var shopSrc = File.ReadAllText(Path.Combine(Program.UiDir, "ShopPanel.cs"));
            var polBad = LabelPolicyBad(shopSrc);
            var flat = Strip(shopSrc);
            // `DropVisibleLabel(` 的**方法定义**占 1 次 ⇒ 读数减 1；另外两项是纯计数。
            Console.WriteLine("   [read] ShopPanel：`DropVisibleLabel(` 调用 "
                + (Regex.Matches(flat, @"DropVisibleLabel\(").Count - 1) + " 次、`ControlTip.Create(` "
                + Regex.Matches(flat, @"ControlTip\.Create\(").Count + " 次、`ButtonLabelRect(` "
                + Regex.Matches(flat, @"ButtonLabelRect\(").Count + " 次（应 2 / 2 / 0）");
            Check("⑤ 两颗动作钮的常显文字都被摘掉、工厂不再收 `ButtonLabelRect(...)`、两颗各挂一条 `ControlTip.Create`",
                polBad.Count == 0,
                polBad.Count == 0 ? "2 颗钮 = 0 个常显文字节点、2 条悬停提示" : string.Join("；", polBad.ToArray()));

            // ③ 判据自检：三个退化样本喂进**同一判据**必须变红
            var degA = Regex.Replace(shopSrc, @"\s*DropVisibleLabel\(close\);", ";");                  // 漏摘第二颗
            var degB = shopSrc.Replace("SlotCenter(2), OnRepairAll)",
                                      "SlotCenter(2), OnRepairAll, ButtonLabelRect(2))");              // 修前形状：文字交给工厂
            var degC = Regex.Replace(shopSrc, @"\s*_closeTip = ControlTip\.Create\([^;]*\);", "");     // 少一条悬停提示
            var ra = LabelPolicyBad(degA);
            var rb2 = LabelPolicyBad(degB);
            var rc = LabelPolicyBad(degC);
            Check("⑤ 判据自检：真源码 ⇒ 合格；**退化 A（漏摘一颗钮的 Label）**、"
                + "退化 B（仍把文字交给工厂 = 修前形状）、退化 C（少一条悬停提示）⇒ 三条必须全变红",
                polBad.Count == 0 && degA != shopSrc && degB != shopSrc && degC != shopSrc
                && ra.Count > 0 && rb2.Count > 0 && rc.Count > 0,
                $"真={polBad.Count} 条；退化A={ra.Count} 条、退化B={rb2.Count} 条、退化C={rc.Count} 条"
                + "（真必须 0，后三个必须 > 0）");

        }

        // ═════════════════════════════════════════════════════════════════════
        //   判据本体 = 纯函数 `JudgeSlotFit`；退化样本（修前形状 / 艺术 1:1 形状）必须变红。
        //   坐标口径：全部在**面板 local 空间**（面板中心 = 原点，画布单位）。
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>
        /// **判据本体（纯函数）**：钮矩形 ⊆ 槽内凹区矩形 + 钮中心与槽中心距 ≤ <paramref name="tol"/>（画布px）
        /// + 钮整块在面板内。
        /// 同一函数必须对"现在的几何"绿、对"修前（中心=槽顶沿 381）/ 艺术 1:1（32px 塞 29px 内凹区）"红。
        /// </summary>
        private static bool JudgeSlotFit(Rect button, Rect inner, float tol, Rect panel)
            => ContainsAll(inner, button)
            && Math.Abs(button.center.x - inner.center.x) <= tol
            && Math.Abs(button.center.y - inner.center.y) <= tol
            && ContainsAll(panel, button);

        /// <summary>面板 local 空间里那颗钮的矩形（= `SlotCenter(slot)` ± 半个边长）。</summary>
        private static Rect ButtonPanelRect(int slot)
        {
            var c = ShopPanel.SlotCenter(slot);
            var s = UiLayoutGame.ShopBottomSlotSize;
            return new Rect(c.x - s * 0.5f, c.y - s * 0.5f, s, s);
        }

        private static void CheckSlotGeometry()
        {
            var panel = new Rect(-ShopPanel.PanelSize.x * 0.5f, -ShopPanel.PanelSize.y * 0.5f,
                                 ShopPanel.PanelSize.x, ShopPanel.PanelSize.y);
            var tol = UiLayoutGame.K;                       // 1 原版px
            var n = UiLayoutGame.ShopBottomSlotX.Length;

            // ① 逐槽：钮 ⊆ 内凹区 + 中心距 ≤ 1 原版px + 在面板内
            var bad = new List<string>();
            var det = new StringBuilder();
            for (var i = 0; i < n; i++)
            {
                var b = ButtonPanelRect(i);
                var inner = ShopPanel.SlotInnerRect(i);
                det.Append("槽").Append(i).Append("[钮中心(").Append(b.center.x.ToString("0.#")).Append(',')
                   .Append(b.center.y.ToString("0.#")).Append(") 槽中心(").Append(inner.center.x.ToString("0.#"))
                   .Append(',').Append(inner.center.y.ToString("0.#")).Append(") 距=")
                   .Append((b.center - inner.center).magnitude.ToString("0.###")).Append(']');
                if (!JudgeSlotFit(b, inner, tol, panel)) bad.Add("槽" + i);
            }
            Check($"⑥ 四个槽：钮矩形 ⊆ 槽内凹区（{'y'}{ShopPanel.SlotInnerTop}..{ShopPanel.SlotInnerBottom} × 宽{ShopPanel.SlotInnerWidth}）"
                + $"、钮中心与槽中心距 ≤ 1 原版px(={tol:0.#} 画布px)、钮在面板内（判据 `JudgeSlotFit`）",
                bad.Count == 0, bad.Count == 0 ? det.ToString() : "不合规：" + string.Join(",", bad.ToArray()));

            // ② 四槽钮两两不重叠（pitch 93.6 > 边长 50.4）
            var pairBad = new List<string>();
            for (var i = 0; i < n; i++)
            {
                for (var j = i + 1; j < n; j++)
                {
                    if (ButtonPanelRect(i).Overlaps(ButtonPanelRect(j))) pairBad.Add($"槽{i}×槽{j}");
                }
            }
            Check("⑥ 四个槽的钮两两不重叠（pitch 93.6 画布px > 边长 50.4）",
                pairBad.Count == 0, pairBad.Count == 0 ? "6 对全不重叠" : "重叠：" + string.Join(",", pairBad.ToArray()));

            // ③ 判据自检：退化样本必须变红
            var okNow = JudgeSlotFit(ButtonPanelRect(2), ShopPanel.SlotInnerRect(2), tol, panel);
            // 退化 A：修前形状（中心取旧常量 381 原版y；尺寸仍是 28）
            var oldCenterY = (216f - 381f) * UiLayoutGame.K;
            var size = UiLayoutGame.ShopBottomSlotSize;
            var degA = JudgeSlotFit(
                new Rect(UiLayoutGame.ShopBottomSlotX[2] - size * 0.5f, oldCenterY - size * 0.5f, size, size),
                ShopPanel.SlotInnerRect(2), tol, panel);
            // 退化 B：原版艺术 1:1（32 原版px = 57.6 画布px）塞进 29 原版px 高的内凹区
            var art = 32f * UiLayoutGame.K;
            var c2 = ShopPanel.SlotCenter(2);
            var degB = JudgeSlotFit(new Rect(c2.x - art * 0.5f, c2.y - art * 0.5f, art, art),
                ShopPanel.SlotInnerRect(2), tol, panel);
            Check("⑥ 判据自检：现在几何 ⇒ 合格；**退化 A（修前中心 = 槽顶沿 381，抬高 18 原版px）**、"
                + "**退化 B（原版艺术 32px 按 1:1 塞进 29px 内凹区）** ⇒ 两条必须全变红",
                okNow && !degA && !degB,
                $"ok={okNow} 退化A={degA} 退化B={degB}（后两个都必须 False）；"
                + $"旧中心 y={oldCenterY:0.#} vs 新中心 y={c2.y:0.#}，差={(oldCenterY - c2.y):0.#} 画布px = "
                + $"{(oldCenterY - c2.y) / UiLayoutGame.K:0.#} 原版px");

            // ④ 口径断言：ShopPanel 不再读陈旧的 `UiLayoutGame.ShopBottomSlotY`，且两处都用 `SlotCenter`
            var src = Strip(File.ReadAllText(Path.Combine(Program.UiDir, "ShopPanel.cs")));
            var stale = Regex.Matches(src, @"ShopBottomSlotY").Count;
            var used = Regex.Matches(src, @"SlotCenter\(\s*[0-9]+\s*\)").Count;
            Check("⑥ 口径断言：`UI/ShopPanel` **不再引用**陈旧的 `UiLayoutGame.ShopBottomSlotY`（旧值 381 = 槽顶沿）"
                + "，且两颗钮都用 `SlotCenter(slot)` 定位",
                stale == 0 && used >= 2,
                $"ShopBottomSlotY 命中={stale}（应 0）；SlotCenter(…) 出现 {used} 次（应 ≥2）");

            // ⑤ 与几何的组合：钮居中后，钮矩形整块在面板内且不越出槽内凹区（`JudgeSlotFit` 已在 ① 判过；
            //   这里给"钮下沿 / 面板下沿"的读数，供实机对账）
            var bp = ButtonPanelRect(2);
            var inner2 = ShopPanel.SlotInnerRect(2);
            Check("⑥ 钮居中后钮矩形整块在面板内、且不越出槽内凹区",
                ContainsAll(panel, bp) && ContainsAll(inner2, bp),
                $"钮(面板空间) y {bp.yMin:0.#}..{bp.yMax:0.#}，面板 yMin={panel.yMin:0.#}；"
                + $"槽内凹区 y {inner2.yMin:0.#}..{inner2.yMax:0.#}");
        }

        // ── PNG 解码（8bit / 非隔行 / 色彩类型 2·6）与 ShopGridCheck 同口径 ──────
        private static bool TryDecodeRgba(string file, out int w, out int h, out byte[] rgba)
        {
            w = 0; h = 0; rgba = null;
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
                    if (b[d + 12] != 0) return false;        // 隔行不支持
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
                    var so = x * bpp;
                    var dst = (y * w + x) * 4;
                    rgba[dst] = cur[so];
                    rgba[dst + 1] = cur[so + 1];
                    rgba[dst + 2] = cur[so + 2];
                    rgba[dst + 3] = bpp == 4 ? cur[so + 3] : (byte)255;
                }
                Array.Copy(cur, prev, stride);
            }
            return true;
        }
    }
}
